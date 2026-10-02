using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 효과음 후보 고르기·다듬기 도구. 생성 사이트가 주는 소리는 5초 길이에 앞뒤가 비어 있어서,
///  · Analyze: 후보마다 시작 시각·최고점·타격 횟수(여러 번 치는 소리 걸러내기)·잔향 길이·낮은 소리 비율·밝기를 잰다
///  · Waveforms: 소리 모양(시작점부터 일정 구간)을 그림으로 그린다
///  · Export: 고른 소리의 시작 직전부터 잘라 끝을 부드럽게 줄이고 크기를 맞춰 WAV로 저장한다
/// </summary>
public static class SfxTool
{
    public class Info
    {
        public string name;
        public float length, onset, peakAt, peakDb, tail, lowRatio, zcr, rmsDb;
        public int hits;
        public override string ToString() =>
            $"{name,-16} 시작 {onset:0.00}s 최고점 +{peakAt - onset:0.000}s {peakDb:0.0}dB 타격 {hits}번 잔향 {tail:0.00}s 저음 {lowRatio * 100:0}% 밝기 {zcr:0.000} 평균 {rmsDb:0.0}dB";
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

    public static AudioClip Load(string path)
    {
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        var st = imp.defaultSampleSettings;
        if (st.loadType != AudioClipLoadType.DecompressOnLoad) { st.loadType = AudioClipLoadType.DecompressOnLoad; imp.defaultSampleSettings = st; imp.SaveAndReimport(); }
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        c.LoadAudioData();
        return c;
    }

    public static Info Analyze(AudioClip c)
    {
        var x = Mono(c);
        int rate = c.frequency, win = rate / 100; // 10ms
        int n = x.Length / win;
        var env = new float[n];
        for (int w = 0; w < n; w++) { double s = 0; for (int i = 0; i < win; i++) { float v = x[w * win + i]; s += v * v; } env[w] = Mathf.Sqrt((float)(s / win)); }
        float peak = env.Max(); int pw = System.Array.IndexOf(env, peak);
        int on = 0; while (on < n && env[on] < peak * 0.1f) on++;
        // 타격 횟수: 최고점의 45%를 넘는 봉우리(60ms 이상 떨어진 것)
        int hits = 0; int last = -100;
        for (int w = 1; w < n - 1; w++)
            if (env[w] >= peak * 0.45f && env[w] >= env[w - 1] && env[w] >= env[w + 1] && w - last > 6) { hits++; last = w; }
        // 잔향: 최고점 뒤로 최고점의 5% 아래로 떨어져 끝까지 다시 오르지 않는 지점
        int end = n - 1; while (end > pw && env[end] < peak * 0.05f) end--;
        // 저음 비율(약 180Hz 아래, 1극 저역 필터)과 밝기(영점 교차율): 시작부터 0.3초 구간
        int s0 = on * win, s1 = Mathf.Min(x.Length, s0 + (int)(rate * 0.3f));
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * 180f / rate), lp = 0; double el = 0, et = 0; int zc = 0;
        for (int i = s0; i < s1; i++) { lp += a * (x[i] - lp); el += lp * lp; et += x[i] * x[i]; if (i > s0 && (x[i] >= 0) != (x[i - 1] >= 0)) zc++; }
        double rms = 0; int cnt = 0; for (int w = on; w <= end; w++) { rms += env[w] * env[w]; cnt++; }
        return new Info
        {
            name = c.name, length = c.length, onset = on * 0.01f, peakAt = pw * 0.01f, peakDb = 20f * Mathf.Log10(Mathf.Max(1e-6f, peak)),
            hits = hits, tail = (end - on) * 0.01f, lowRatio = et > 0 ? (float)(el / et) : 0, zcr = zc / (float)Mathf.Max(1, s1 - s0),
            rmsDb = 20f * Mathf.Log10(Mathf.Max(1e-6f, Mathf.Sqrt((float)(rms / Mathf.Max(1, cnt))))),
        };
    }

    public static string AnalyzeFolder(string folder, string prefix = "")
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            if (!Path.GetFileName(g).StartsWith(prefix)) continue;
            sb.AppendLine(Analyze(Load(g)).ToString());
        }
        return sb.ToString();
    }

    public class Hit
    {
        public string file; public int index;
        public float start, end, attack, peakDb, lowRatio, zcr, sub; // sub = 앞 150ms 안의 큰 봉우리 수(1~2면 깔끔한 한 방)
        public float Length => end - start;
        public override string ToString() =>
            $"{file}#{index} {start:0.00}~{end:0.00}s 길이 {Length:0.00} 공격 {attack * 1000:0}ms {peakDb:0.0}dB 저음 {lowRatio * 100:0}% 밝기 {zcr:0.000} 봉우리 {sub}";
    }

    /// <summary>파일 안의 소리를 조용한 틈(80ms 이상)으로 나눠 한 방씩 잰다</summary>
    public static List<Hit> Segment(string assetPath)
    {
        var c = Load(assetPath); var x = Mono(c);
        int rate = c.frequency, win = rate / 200; // 5ms
        int n = x.Length / win;
        var env = new float[n];
        for (int w = 0; w < n; w++) { double sum = 0; for (int i = 0; i < win; i++) { float v = x[w * win + i]; sum += v * v; } env[w] = Mathf.Sqrt((float)(sum / win)); }
        float gpk = env.Max();
        var hits = new List<Hit>();
        int k = 0;
        while (k < n)
        {
            if (env[k] < gpk * 0.06f) { k++; continue; }
            int on = k, last = k, pw = k;
            while (k < n)
            {
                if (env[k] > env[pw]) pw = k;
                if (env[k] >= env[pw] * 0.03f) last = k;
                if (k - last > 16) break; // 80ms 조용하면 끝
                k++;
            }
            int end = last + 1;
            float pk = env[pw];
            int sub = 0; int lastSub = -100;
            for (int w = on + 1; w < Mathf.Min(end, on + 30) - 1; w++)
                if (env[w] >= pk * 0.5f && env[w] >= env[w - 1] && env[w] >= env[w + 1] && w - lastSub > 4) { sub++; lastSub = w; }
            int s0 = on * win, s1 = Mathf.Min(x.Length, end * win);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 180f / rate), lp = 0; double el = 0, et = 0; int zc = 0;
            for (int i = s0; i < s1; i++) { lp += a * (x[i] - lp); el += lp * lp; et += x[i] * x[i]; if (i > s0 && (x[i] >= 0) != (x[i - 1] >= 0)) zc++; }
            hits.Add(new Hit
            {
                file = Path.GetFileNameWithoutExtension(assetPath), index = hits.Count, start = on * 0.005f, end = end * 0.005f, attack = (pw - on) * 0.005f,
                peakDb = 20f * Mathf.Log10(Mathf.Max(1e-6f, pk)), lowRatio = et > 0 ? (float)(el / et) : 0, zcr = zc / (float)Mathf.Max(1, s1 - s0), sub = sub,
            });
        }
        return hits;
    }

    /// <summary>시작점 직전 30ms부터 seconds 동안의 소리 모양을 한 줄씩 그린다</summary>
    public static void Waveforms(string folder, string prefix, float seconds, string pngPath)
    {
        var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).StartsWith(prefix)).OrderBy(p => p).ToList();
        int W = 900, H = 90;
        var tex = new Texture2D(W, H * paths.Count, TextureFormat.RGB24, false);
        var px = new Color32[W * H * paths.Count];
        for (int k = 0; k < paths.Count; k++)
        {
            var c = Load(paths[k]); var x = Mono(c); var info = Analyze(c);
            int s0 = Mathf.Max(0, (int)((info.onset - 0.03f) * c.frequency)), len = (int)(seconds * c.frequency);
            float peak = 1e-4f; for (int i = s0; i < Mathf.Min(x.Length, s0 + len); i++) peak = Mathf.Max(peak, Mathf.Abs(x[i]));
            int rowTop = (paths.Count - 1 - k) * H;
            for (int col = 0; col < W; col++)
            {
                int a = s0 + col * len / W, b = s0 + (col + 1) * len / W; float mn = 0, mx = 0;
                for (int i = a; i < b && i < x.Length; i++) { mn = Mathf.Min(mn, x[i]); mx = Mathf.Max(mx, x[i]); }
                int y0 = (int)((mn / peak * 0.48f + 0.5f) * (H - 1)), y1 = (int)((mx / peak * 0.48f + 0.5f) * (H - 1));
                for (int y = 0; y < H; y++)
                {
                    bool tick = col % (W / 10) == 0;
                    Color32 bg = k % 2 == 0 ? new Color32(18, 18, 22, 255) : new Color32(26, 26, 32, 255);
                    px[(rowTop + y) * W + col] = y >= y0 && y <= y1 ? new Color32(240, 190, 90, 255) : tick ? new Color32(60, 60, 70, 255) : bg;
                }
            }
        }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    /// <summary>
    /// 스펙트로그램(시간-주파수 그림): 가로 = 시간(start부터 seconds), 세로 = 주파수(아래 0 ~ 위 maxHz, 로그 눈금), 밝을수록 셈.
    /// 여러 구간을 한 장에 줄줄이 그린다. specs = "파일경로|시작|길이" 목록
    /// </summary>
    public static void Spectrograms(string[] specs, float maxHz, string pngPath)
    {
        int W = 420, H = 140, N = 1024;
        var tex = new Texture2D(W, H * specs.Length, TextureFormat.RGB24, false);
        var px = new Color32[W * H * specs.Length];
        var re = new float[N]; var im = new float[N];
        for (int k = 0; k < specs.Length; k++)
        {
            var parts = specs[k].Split('|');
            var c = Load(parts[0]); var x = Mono(c); int rate = c.frequency;
            float st = float.Parse(parts[1]), len = float.Parse(parts[2]);
            var col = new float[W, H]; float mx = 1e-9f;
            for (int cx = 0; cx < W; cx++)
            {
                int center = (int)((st + len * cx / W) * rate);
                for (int i = 0; i < N; i++)
                {
                    int j = center - N / 2 + i;
                    float w = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / (N - 1));
                    re[i] = j >= 0 && j < x.Length ? x[j] * w : 0f; im[i] = 0f;
                }
                Fft(re, im);
                for (int y = 0; y < H; y++)
                {
                    float hz = 40f * Mathf.Pow(maxHz / 40f, y / (float)(H - 1)); // 40Hz ~ maxHz 로그
                    int bin = Mathf.Clamp(Mathf.RoundToInt(hz * N / rate), 1, N / 2 - 1);
                    float m = Mathf.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]);
                    col[cx, y] = m; mx = Mathf.Max(mx, m);
                }
            }
            int top = (specs.Length - 1 - k) * H;
            for (int cx = 0; cx < W; cx++)
            for (int y = 0; y < H; y++)
            {
                float db = 20f * Mathf.Log10(col[cx, y] / mx + 1e-6f); // 0 ~ -60
                float v = Mathf.Clamp01((db + 60f) / 60f);
                px[(top + y) * W + cx] = y == 0 ? new Color32(255, 0, 255, 255) : new Color32((byte)(255 * Mathf.Pow(v, 0.8f)), (byte)(200 * v * v), (byte)(80 * v), 255);
            }
        }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
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

    /// <summary>start(초)부터 length초를 잘라, 끝 fade초를 부드럽게 줄이고, 최고점을 peakDb로 맞춰 16비트 WAV로 저장한다</summary>
    public static string Export(string assetPath, float start, float length, float fade, float peakDb, string outPath, bool snapAttack = false, float speed = 1f)
    {
        var c = Load(assetPath); var x = Mono(c);
        if (!Mathf.Approximately(speed, 1f))
        {
            // 빠르게(speed배) 다시 표본화: 테이프를 빨리 돌리는 것과 같다(음 높이도 함께 오른다)
            var y2 = new float[(int)(x.Length / speed)];
            for (int i = 0; i < y2.Length; i++) { float p = i * speed; int a = (int)p; float f2 = p - a; y2[i] = a + 1 < x.Length ? Mathf.Lerp(x[a], x[a + 1], f2) : x[x.Length - 1]; }
            x = y2; start /= speed; length /= speed;
        }
        int rate = c.frequency, s0 = Mathf.Clamp((int)(start * rate), 0, x.Length - 1), n = Mathf.Min((int)(length * rate), x.Length - s0);
        if (snapAttack)
        {
            // 앞쪽의 약한 웅웅거림을 건너뛰고, 실제 타격(구간 최고점의 25%를 처음 넘는 곳) 6ms 앞에서 시작한다(소리가 화면보다 늦지 않게)
            float pk = 0f; for (int i = s0; i < s0 + n; i++) pk = Mathf.Max(pk, Mathf.Abs(x[i]));
            int first = s0; while (first < s0 + n && Mathf.Abs(x[first]) < pk * 0.25f) first++;
            int moved = Mathf.Max(s0, first - (int)(0.006f * rate)) - s0;
            s0 += moved; n -= moved;
        }
        var y = new float[n];
        int fadeIn = Mathf.Min(n, (int)(0.002f * rate)), fadeOut = Mathf.Min(n, (int)(fade * rate));
        for (int i = 0; i < n; i++)
        {
            float g = 1f;
            if (i < fadeIn) g *= i / (float)fadeIn;
            if (i > n - fadeOut) g *= Mathf.SmoothStep(0f, 1f, (n - i) / (float)fadeOut);
            y[i] = x[s0 + i] * g;
        }
        float peak = y.Max(Mathf.Abs), target = Mathf.Pow(10f, peakDb / 20f);
        if (peak > 1e-5f) for (int i = 0; i < n; i++) y[i] *= target / peak;
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        using (var w = new BinaryWriter(File.Create(outPath)))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            foreach (var v in y) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }
        return $"{Path.GetFileName(outPath)} {n / (float)rate:0.000}초 (시작 {s0 / (float)rate:0.000}s)";
    }
}
