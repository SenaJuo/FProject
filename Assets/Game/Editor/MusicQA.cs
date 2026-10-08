using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 배경음악 후보 고르기 도구(타이틀 곡 등). 귀 대신 숫자와 그림으로 비교한다.
///  · Analyze: 길이·최고점·평균 크기·찢어짐(클리핑)·흐름(2초 단위 크기의 폭)·곡 중간의 끊김·템포·밝기(스펙트럼 무게중심)·
///    저음 비율·사람 목소리 대역 두드러짐(보컬 의심 지표)·도입부 길이·끝맺음 시작 시각
///  · Envelopes: 곡마다 크기 흐름을 한 줄씩 그린다
///  · FindLoop: 도입·끝맺음을 뺀 가운데에서, 끝과 시작의 소리 색이 가장 비슷한 반복 구간을 찾는다
/// </summary>
public static class MusicQA
{
    public class Report
    {
        public string name;
        public float length, peakDb, rmsDb, spreadDb, longestGap, bpm, centroid, lowRatio, voiceRatio, voiceFlux, introEnd, outroStart;
        public int clipped;
        public override string ToString() =>
            $"{name,-12} {length,5:0}s 최고 {peakDb,5:0.0}dB 평균 {rmsDb,5:0.0}dB 흐름폭 {spreadDb,4:0.0}dB 끊김 {longestGap,4:0.0}s 찢김 {clipped,4} " +
            $"템포 {bpm,4:0} 밝기 {centroid,5:0}Hz 저음 {lowRatio * 100,3:0}% 목소리대역 {voiceRatio * 100,3:0}% 목소리흔들림 {voiceFlux:0.00} 도입 {introEnd,4:0}s 끝맺음 {outroStart,4:0}s";
    }

    static float[] Mono(AudioClip c)
    {
        var d = new float[c.samples * c.channels];
        c.GetData(d, 0);
        if (c.channels == 1) return d;
        var m = new float[c.samples];
        for (int i = 0; i < c.samples; i++) { float s = 0; for (int k = 0; k < c.channels; k++) s += d[i * c.channels + k]; m[i] = s / c.channels; }
        return m;
    }

    static float Db(float v) => 20f * Mathf.Log10(Mathf.Max(1e-6f, v));

    /// <summary>창 길이(초)마다 RMS</summary>
    static float[] Envelope(float[] x, int rate, float window)
    {
        int win = Mathf.Max(1, (int)(rate * window)), n = x.Length / win;
        var env = new float[n];
        for (int w = 0; w < n; w++) { double s = 0; for (int i = 0; i < win; i++) { float v = x[w * win + i]; s += v * v; } env[w] = Mathf.Sqrt((float)(s / win)); }
        return env;
    }

    public static Report Analyze(string path)
    {
        var c = SfxTool.Load(path);
        var x = Mono(c);
        int rate = c.frequency;
        var r = new Report { name = Path.GetFileNameWithoutExtension(path), length = c.length };

        // 찢어짐: 원래(스테레오) 샘플 기준
        var raw = new float[c.samples * c.channels];
        c.GetData(raw, 0);
        float peak = 0f;
        foreach (var v in raw) { float a = Mathf.Abs(v); if (a > peak) peak = a; if (a >= 0.999f) r.clipped++; }
        r.peakDb = Db(peak);

        // 2초 단위 크기: 흐름폭(가운데 80% 구간에서 상위 10% - 하위 10%), 곡 중간의 끊김(-45dB 아래가 이어진 가장 긴 시간)
        var env2 = Envelope(x, rate, 2f).Select(Db).ToArray();
        double all = 0; foreach (var v in x) all += v * v;
        r.rmsDb = Db(Mathf.Sqrt((float)(all / x.Length)));
        float median = env2.OrderBy(v => v).ElementAt(env2.Length / 2);
        int a0 = 0; while (a0 < env2.Length && env2[a0] < median - 6f) a0++;
        int b0 = env2.Length - 1; while (b0 > a0 && env2[b0] < median - 6f) b0--;
        r.introEnd = a0 * 2f;
        // 끝맺음: 뒤에서부터 크기가 중앙값 -3dB 아래로 계속 머무는 구간의 시작
        int o = env2.Length - 1; while (o > a0 && env2[o] < median - 3f) o--;
        r.outroStart = (o + 1) * 2f;
        var mid = env2.Skip(a0).Take(Mathf.Max(1, b0 - a0 + 1)).OrderBy(v => v).ToArray();
        r.spreadDb = mid[(int)(mid.Length * 0.9f)] - mid[(int)(mid.Length * 0.1f)];
        var env05 = Envelope(x, rate, 0.5f);
        int run = 0, best = 0, s0 = (int)(r.introEnd / 0.5f), s1 = Mathf.Min(env05.Length, (int)(r.outroStart / 0.5f));
        for (int i = s0; i < s1; i++) { if (Db(env05[i]) < -45f) { run++; best = Mathf.Max(best, run); } else run = 0; }
        r.longestGap = best * 0.5f;

        // 템포: 10ms 크기의 증가량(박의 시작) → 자기상관이 가장 큰 간격(40~180 BPM)
        var env10 = Envelope(x, rate, 0.01f);
        var flux = new float[env10.Length];
        for (int i = 1; i < env10.Length; i++) flux[i] = Mathf.Max(0f, env10[i] - env10[i - 1]);
        float bestScore = 0f; int bestLag = 0;
        for (int lag = 33; lag <= 150; lag++)
        {
            double s = 0; for (int i = 0; i + lag < flux.Length; i++) s += flux[i] * flux[i + lag];
            float score = (float)s * (1f + 0.15f * Mathf.Exp(-Mathf.Pow((60f / (lag * 0.01f) - 75f) / 30f, 2f))); // 느린 쪽으로 살짝 기운다(배 박자 혼동 줄이기)
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        r.bpm = bestLag > 0 ? 60f / (bestLag * 0.01f) : 0f;

        // 스펙트럼: 1초마다 2048점 FFT → 무게중심, 200Hz 아래 비율, 목소리 대역(250~3500Hz) 비율과 그 대역의 프레임 사이 흔들림
        int N = 2048; var re = new float[N]; var im = new float[N];
        double cenSum = 0, lowSum = 0, voiceSum = 0, totSum = 0, fluxSum = 0; int frames = 0;
        float[] prevVoice = null;
        int start = (int)(r.introEnd * rate), stop = Mathf.Min(x.Length - N, (int)(r.outroStart * rate));
        for (int p = start; p < stop; p += rate / 4)
        {
            for (int i = 0; i < N; i++) { float w = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / (N - 1)); re[i] = x[p + i] * w; im[i] = 0f; }
            Fft(re, im);
            double tot = 0, cen = 0, low = 0, voice = 0;
            int v0 = Mathf.RoundToInt(250f * N / rate), v1 = Mathf.RoundToInt(3500f * N / rate);
            var cur = new float[v1 - v0];
            for (int b = 1; b < N / 2; b++)
            {
                float hz = b * (float)rate / N, m = re[b] * re[b] + im[b] * im[b];
                tot += m; cen += hz * m; if (hz < 200f) low += m;
                if (b >= v0 && b < v1) { voice += m; cur[b - v0] = Mathf.Log(1e-9f + m); }
            }
            if (tot < 1e-9) continue;
            cenSum += cen / tot; lowSum += low / tot; voiceSum += voice / tot; frames++;
            if (prevVoice != null) { double d = 0; for (int i = 0; i < cur.Length; i++) d += Mathf.Abs(cur[i] - prevVoice[i]); fluxSum += d / cur.Length; }
            prevVoice = cur;
        }
        if (frames > 0) { r.centroid = (float)(cenSum / frames); r.lowRatio = (float)(lowSum / frames); r.voiceRatio = (float)(voiceSum / frames); r.voiceFlux = (float)(fluxSum / Mathf.Max(1, frames - 1)); }
        return r;
    }

    public static string AnalyzeFolder(string folder, string prefix = "")
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).StartsWith(prefix)).OrderBy(p => p))
            sb.AppendLine(Analyze(p).ToString());
        return sb.ToString();
    }

    /// <summary>곡마다 크기 흐름(1초 단위, -50~0dB)을 한 줄씩 그린다. 세로 금색 선 = 30초마다</summary>
    public static void Envelopes(string[] paths, string pngPath)
    {
        int W = 900, H = 90;
        var tex = new Texture2D(W, H * paths.Length, TextureFormat.RGB24, false);
        var px = Enumerable.Repeat(new Color32(12, 12, 14, 255), W * H * paths.Length).ToArray();
        float maxLen = paths.Max(p => SfxTool.Load(p).length);
        for (int k = 0; k < paths.Length; k++)
        {
            var c = SfxTool.Load(paths[k]); var env = Envelope(Mono(c), c.frequency, 1f);
            int top = (paths.Length - 1 - k) * H;
            for (int sec = 30; sec < maxLen; sec += 30) { int cx = (int)(sec / maxLen * (W - 1)); for (int y = 0; y < H; y++) px[(top + y) * W + cx] = new Color32(90, 75, 30, 255); }
            for (int i = 0; i < env.Length; i++)
            {
                int cx = (int)(i / maxLen * (W - 1));
                int h = (int)(Mathf.Clamp01((Db(env[i]) + 50f) / 50f) * (H - 6));
                for (int y = 2; y < 2 + h; y++) px[(top + y) * W + cx] = new Color32(230, 200, 140, 255);
            }
            for (int cx = 0; cx < W; cx++) px[top * W + cx] = new Color32(255, 0, 255, 255);
        }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    /// <summary>
    /// 반복 구간 찾기: 도입·끝맺음을 뺀 가운데에서 길이 minLen~maxLen초인 [a, b]를 고른다 —
    /// b 직전 2초와 a 직전 2초의 소리 색(12개 대역 에너지)과 크기가 가장 비슷한 곳(이어 붙이면 티가 안 나게)
    /// </summary>
    public static string FindLoop(string path, float minLen, float maxLen)
    {
        var r = Analyze(path);
        var c = SfxTool.Load(path); var x = Mono(c); int rate = c.frequency;
        float step = 0.25f; int N = 2048; var re = new float[N]; var im = new float[N];
        int frames = (int)(c.length / step);
        var feat = new float[frames][];
        float[] edges = { 60, 120, 250, 400, 600, 900, 1300, 1900, 2800, 4000, 6000, 9000, 14000 };
        for (int f = 0; f < frames; f++)
        {
            int p = (int)(f * step * rate); feat[f] = new float[13];
            if (p + N >= x.Length) continue;
            for (int i = 0; i < N; i++) { float w = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / (N - 1)); re[i] = x[p + i] * w; im[i] = 0f; }
            Fft(re, im);
            for (int b = 1; b < N / 2; b++)
            {
                float hz = b * (float)rate / N; float m = re[b] * re[b] + im[b] * im[b];
                for (int e = 0; e < 12; e++) if (hz >= edges[e] && hz < edges[e + 1]) feat[f][e] += m;
            }
            float tot = 0; for (int e = 0; e < 12; e++) tot += feat[f][e];
            feat[f][12] = Mathf.Log(1e-9f + tot);
            for (int e = 0; e < 12; e++) feat[f][e] = Mathf.Log(1e-9f + feat[f][e]) - feat[f][12];
        }
        int ctx = (int)(2f / step);
        int lo = (int)((r.introEnd + 4f) / step), hi = (int)((r.outroStart - 2f) / step);
        float bestD = float.MaxValue; int bestA = 0, bestB = 0;
        for (int a = Mathf.Max(lo, ctx); a < hi; a++)
            for (int b = a + (int)(minLen / step); b <= Mathf.Min(hi, a + (int)(maxLen / step)); b++)
            {
                float d = 0;
                for (int k = 1; k <= ctx; k++) for (int e = 0; e <= 12; e++) { float q = feat[a - k][e] - feat[b - k][e]; d += q * q * (e == 12 ? 3f : 1f); }
                if (d < bestD) { bestD = d; bestA = a; bestB = b; }
            }
        return $"{r.name}: 반복 {bestA * step:0.00}~{bestB * step:0.00}s (길이 {(bestB - bestA) * step:0.0}s) 어긋남 {bestD / (ctx * 13):0.000}";
    }

    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * Mathf.PI / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < len / 2; k++)
                {
                    float wr = Mathf.Cos(ang * k), wi = Mathf.Sin(ang * k);
                    int a = i + k, b = i + k + len / 2;
                    float xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                }
        }
    }
}
