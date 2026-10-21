using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 움직이는 GIF(GIF89a, 무한 반복)를 만든다(2026-10-21: 발표 자료에 타이틀 화면 움짤).
/// 프레임은 줄여서 받고, 고정 팔레트(회색 160 + 색 96)로 바꾼 뒤 LZW로 압축한다. 바깥 프로그램 없이 쓴다.
/// </summary>
public class GifWriter
{
    readonly int w, h, delayCs;
    readonly List<byte[]> frames = new();
    static Color32[] palette;
    static byte[] lut; // 32×32×32 → 팔레트 번호

    public GifWriter(int width, int height, float fps) { w = width; h = height; delayCs = Mathf.Max(2, Mathf.RoundToInt(100f / fps)); BuildPalette(); }

    /// <summary>원본 프레임(가로 sw, 세로 sh, 아래에서 위로)을 줄여서 한 장 더한다</summary>
    public void Add(Color32[] src, int sw, int sh)
    {
        var idx = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Mathf.Clamp((int)((h - 1 - y + 0.5f) * sh / h), 0, sh - 1); // GIF는 위에서 아래로
            for (int x = 0; x < w; x++)
            {
                // 2×2 평균(줄일 때 반짝임 줄이기)
                int sx = Mathf.Clamp((int)((x + 0.5f) * sw / w), 0, sw - 1);
                int sx2 = Mathf.Min(sx + 1, sw - 1), sy2 = Mathf.Min(sy + 1, sh - 1);
                var a = src[sy * sw + sx]; var b = src[sy * sw + sx2]; var c = src[sy2 * sw + sx]; var d = src[sy2 * sw + sx2];
                int r = (a.r + b.r + c.r + d.r) >> 2, g = (a.g + b.g + c.g + d.g) >> 2, bl = (a.b + b.b + c.b + d.b) >> 2;
                idx[y * w + x] = lut[((r >> 3) << 10) | ((g >> 3) << 5) | (bl >> 3)];
            }
        }
        frames.Add(idx);
    }

    public int Count => frames.Count;

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var o = new BinaryWriter(fs);
        o.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        o.Write((ushort)w); o.Write((ushort)h);
        o.Write((byte)0xF7); o.Write((byte)0); o.Write((byte)0); // 전역 팔레트 256색
        foreach (var c in palette) { o.Write(c.r); o.Write(c.g); o.Write(c.b); }
        // 무한 반복
        o.Write(new byte[] { 0x21, 0xFF, 0x0B }); o.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); o.Write(new byte[] { 3, 1, 0, 0, 0 });
        foreach (var f in frames)
        {
            o.Write(new byte[] { 0x21, 0xF9, 4, 0x04 }); o.Write((ushort)delayCs); o.Write((byte)0); o.Write((byte)0);
            o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0);
            o.Write((byte)8);
            var data = Lzw(f);
            for (int i = 0; i < data.Count; i += 255) { int n = Mathf.Min(255, data.Count - i); o.Write((byte)n); for (int k = 0; k < n; k++) o.Write(data[i + k]); }
            o.Write((byte)0);
        }
        o.Write((byte)0x3B);
    }

    static List<byte> Lzw(byte[] px)
    {
        var outp = new List<byte>(px.Length / 2);
        int clear = 256, eoi = 257, next = 258, size = 9;
        var dict = new Dictionary<int, int>(4096);
        int bitBuf = 0, bitCnt = 0;
        void Emit(int code) { bitBuf |= code << bitCnt; bitCnt += size; while (bitCnt >= 8) { outp.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCnt -= 8; } }
        Emit(clear);
        int cur = px[0];
        for (int i = 1; i < px.Length; i++)
        {
            int k = px[i], key = (cur << 8) | k;
            if (dict.TryGetValue(key, out int code)) { cur = code; continue; }
            Emit(cur);
            if (next < 4096) { dict[key] = next++; if (next > (1 << size) && size < 12) size++; } // 해독기는 한 번호 늦게 표를 채우므로, 다음 번호가 길이를 넘은 뒤에 한 비트 늘린다
            else { Emit(clear); dict.Clear(); next = 258; size = 9; }
            cur = k;
        }
        Emit(cur); Emit(eoi);
        if (bitCnt > 0) outp.Add((byte)(bitBuf & 0xFF));
        return outp;
    }

    /// <summary>타이틀은 거의 흑백이라 회색을 촘촘히(160단), 나머지 96색은 붉은 REW · 호박색 글씨용 색 공간</summary>
    static void BuildPalette()
    {
        if (palette != null) return;
        palette = new Color32[256];
        for (int i = 0; i < 160; i++) { byte v = (byte)Mathf.RoundToInt(i * 255f / 159f); palette[i] = new Color32(v, v, v, 255); }
        int n = 160;
        for (int r = 0; r < 4; r++) for (int g = 0; g < 6; g++) for (int b = 0; b < 4; b++)
                    palette[n++] = new Color32((byte)(r * 85), (byte)(g * 51), (byte)(b * 85), 255);
        lut = new byte[32 * 32 * 32];
        for (int r = 0; r < 32; r++) for (int g = 0; g < 32; g++) for (int b = 0; b < 32; b++)
                {
                    int R = r * 8 + 4, G = g * 8 + 4, B = b * 8 + 4, best = 0, bd = int.MaxValue;
                    for (int i = 0; i < 256; i++)
                    {
                        var c = palette[i]; int dr = R - c.r, dg = G - c.g, db = B - c.b;
                        int dd = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                        if (dd < bd) { bd = dd; best = i; }
                    }
                    lut[(r << 10) | (g << 5) | b] = (byte)best;
                }
    }
}
