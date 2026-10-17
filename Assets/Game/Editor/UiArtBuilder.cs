using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 화면 그림 만들기(2026-10-17 "UI가 밤티 난다 — 바르코로 분위기에 맞게, 테이프는 실제 같은 테이프로"):
/// 바르코(nano-banana-2)로 만든 사진을 게임에 쓸 수 있게 다듬는다.
///  · 카세트 두 종(가게 테이프: 반투명 회색 + 크림색 라벨 / 주운 테이프: 검은 무지): 초록 바탕을 빼고(반투명 껍데기에 비친 초록도 뺀다),
///    카세트 둘레로 잘라 Resources/UI/Cassette_*.png. 릴 허브는 따로 오려(Cassette_*_Hub.png) 재생 중에 돌린다.
///  · 창 바탕: 어두운 가죽 질감(Resources/UI/PanelTexture.png).
/// 각 카세트의 라벨 · 줄무늬 · 허브 자리(0~1 비율)는 콘솔에 찍고 HUD.CassetteArt에 옮겨 적는다.
/// </summary>
public static class UiArtBuilder
{
    const string OutDir = "Assets/Game/Resources/UI";

    public struct Spec
    {
        public string src, name;
        public Vector2 hubL, hubR; // 원본 사진의 허브 중심(대략, px · 위에서부터)
        public float hubRadius;    // 오려 낼 허브 반지름(px)
        public RectInt label, stripe; // 원본 사진의 제목 칸 · 줄무늬(px · 위에서부터)
    }

    public static string Build(string srcDir)
    {
        Directory.CreateDirectory(OutDir);
        var log = new StringBuilder();
        Cassette(new Spec
        {
            src = srcDir + "/tapeB.png", name = "Cassette_Label",
            hubL = new Vector2(470, 368), hubR = new Vector2(905, 368), hubRadius = 74,
            label = new RectInt(250, 128, 880, 150), stripe = new RectInt(230, 505, 920, 20),
        }, log);
        Cassette(new Spec
        {
            src = srcDir + "/blackB.png", name = "Cassette_Black",
            hubL = new Vector2(458, 368), hubR = new Vector2(920, 368), hubRadius = 64,
            label = new RectInt(220, 112, 940, 150), stripe = new RectInt(0, 0, 0, 0),
        }, log);
        // 창 바탕: 그대로 쓰되 크기만 줄인다
        var tex = Load(srcDir + "/texB.png");
        Save(Resize(tex, 512, 512), OutDir + "/PanelTexture.png", false);
        AssetDatabase.Refresh();
        foreach (var n in new[] { "Cassette_Label", "Cassette_Black", "Cassette_Label_Hub", "Cassette_Black_Hub", "PanelTexture" }) Import(OutDir + "/" + n + ".png");
        Debug.Log("[UiArt]\n" + log);
        return log.ToString();
    }

    /// <summary>
    /// 창 · 버튼 꾸밈(2026-10-17 "메뉴 · 인벤토리 · 가게 UI도 싹 다 바르코로, 너무 단조롭다"):
    ///  창 — 카세트 덱 앞판(일시정지 · 설정 · 설명 · 키패드), 가죽 카세트 가방(인벤토리), 레코드 가게 나무 계산대(가게)
    ///  버튼 — 하이파이 덱 누름 단추(평소 · 호박빛 켜짐). 창 제목 뒤 다이모 라벨은 글씨를 가려 뺐다
    /// 모두 9칸 늘이기로 그린다(UiTheme.Nine): 모서리(나사 · 리벳 · 놋쇠)는 그대로, 가운데만 늘어난다.
    /// </summary>
    public static string BuildSkins(string srcDir)
    {
        Directory.CreateDirectory(OutDir);
        var log = new StringBuilder();
        // 덱 앞판: 바깥 검은 여백을 잘라 낸다
        var deck = Crop(Load(srcDir + "/deckB.png"), 38, 36, 862, 1168); // 앞판만(바깥 검은 여백 제외) — 자동 자르기는 아래 나사까지 잘랐다
        Save(Resize(deck, 600, Mathf.RoundToInt(600f * deck.height / deck.width)), OutDir + "/Skin_Deck.png", false);
        log.AppendLine($"Skin_Deck 600x{Mathf.RoundToInt(600f * deck.height / deck.width)}");
        var bag = Load(srcDir + "/caseB.png");
        Save(Resize(bag, 1024, Mathf.RoundToInt(1024f * bag.height / bag.width)), OutDir + "/Skin_Case.png", false);
        var wood = Load(srcDir + "/woodA.png");
        Save(Resize(wood, 1024, Mathf.RoundToInt(1024f * wood.height / wood.width)), OutDir + "/Skin_Wood.png", false);
        foreach (var (src, name, width) in new[] { ("btnB", "Skin_Button", 512), ("btnLit", "Skin_ButtonLit", 512) })
        {
            if (!File.Exists($"{srcDir}/{src}.png")) { log.AppendLine($"{name}: 원본 없음"); continue; }
            var keyed = KeyGreen(Load($"{srcDir}/{src}.png"));
            int hh = Mathf.RoundToInt(width * (float)keyed.height / keyed.width);
            Save(Resize(keyed, width, hh), $"{OutDir}/{name}.png", true);
            log.AppendLine($"{name} {width}x{hh}");
        }
        AssetDatabase.Refresh();
        foreach (var n in new[] { "Skin_Deck", "Skin_Case", "Skin_Wood", "Skin_Button", "Skin_ButtonLit" }) Import(OutDir + "/" + n + ".png");
        Debug.Log("[UiArt]\n" + log);
        return log.ToString();
    }

    /// <summary>원본 사진 좌표(왼쪽 위 기준 px)로 자른다</summary>
    static Texture2D Crop(Texture2D t, int left, int top, int right, int bottom)
    {
        int cw = right - left, ch = bottom - top;
        var o = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        o.SetPixels(t.GetPixels(left, t.height - bottom, cw, ch)); o.Apply();
        return o;
    }

    /// <summary>둘레의 어두운 여백을 잘라 낸다: 가운데 절반 줄의 평균 밝기가 threshold를 넘는 첫 줄까지</summary>
    static Texture2D AutoCrop(Texture2D t, float threshold)
    {
        int w = t.width, h = t.height; var px = t.GetPixels();
        float Col(int x) { float s = 0; for (int y = h / 4; y < h * 3 / 4; y++) s += px[y * w + x].grayscale; return s / (h / 2); }
        float Row(int y) { float s = 0; for (int x = w / 4; x < w * 3 / 4; x++) s += px[y * w + x].grayscale; return s / (w / 2); }
        int x0 = 0, x1 = w - 1, y0 = 0, y1 = h - 1;
        while (x0 < w / 4 && Col(x0) < threshold) x0++;
        while (x1 > w * 3 / 4 && Col(x1) < threshold) x1--;
        while (y0 < h / 4 && Row(y0) < threshold) y0++;
        while (y1 > h * 3 / 4 && Row(y1) < threshold) y1--;
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var o = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        o.SetPixels(t.GetPixels(x0, y0, cw, ch)); o.Apply();
        return o;
    }

    /// <summary>초록 바탕을 빼고(가장자리 초록 번짐도 뺀다) 물건 둘레로 자른다</summary>
    static Texture2D KeyGreen(Texture2D src)
    {
        int w = src.width, h = src.height;
        var px = src.GetPixels32();
        float bgG = 0f;
        foreach (var p in new[] { px[0], px[w - 1], px[(h - 1) * w], px[h * w - 1] }) bgG += p.g - Mathf.Max(p.r, p.b);
        bgG /= 4f;
        int x0 = w, y0 = h, x1 = 0, y1 = 0;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            float k = (c.g - Mathf.Max(c.r, c.b)) / bgG;
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.70f, k));
            byte g = (byte)Mathf.Min(c.g, (c.r + c.b) / 2 + 4);
            px[i] = new Color32(c.r, g, c.b, (byte)(a * 255f));
            if (a > 0.8f) { int x = i % w, y = i / w; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
        }
        x0 = Mathf.Max(0, x0 - 2); y0 = Mathf.Max(0, y0 - 2); x1 = Mathf.Min(w - 1, x1 + 2); y1 = Mathf.Min(h - 1, y1 + 2);
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var o = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        var op = new Color32[cw * ch];
        for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) op[y * cw + x] = px[(y + y0) * w + x + x0];
        o.SetPixels32(op); o.Apply();
        return o;
    }

    static void Cassette(Spec sp, StringBuilder log)
    {
        var src = Load(sp.src);
        int w = src.width, h = src.height;
        var px = src.GetPixels32();
        // 바탕 초록: 네 귀퉁이의 '초록 정도'
        float bgG = 0f;
        foreach (var p in new[] { px[0], px[w - 1], px[(h - 1) * w], px[h * w - 1] }) bgG += p.g - Mathf.Max(p.r, p.b);
        bgG /= 4f;
        var outPx = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            float green = c.g - Mathf.Max(c.r, c.b);
            float k = green / bgG;
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.70f, k));
            // 반투명 껍데기 · 가장자리에 비친 초록을 뺀다
            byte g = (byte)Mathf.Min(c.g, (c.r + c.b) / 2 + 4); // 회색 껍데기가 초록빛으로 보이지 않게(라벨 크림색 · 갈색 테이프는 거의 그대로)
            outPx[i] = new Color32(c.r, g, c.b, (byte)(a * 255f));
        }
        // 카세트 둘레(알파가 찬 곳)로 자른다
        int x0 = w, y0 = h, x1 = 0, y1 = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (outPx[y * w + x].a > 200) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
        x0 = Mathf.Max(0, x0 - 3); y0 = Mathf.Max(0, y0 - 3); x1 = Mathf.Min(w - 1, x1 + 3); y1 = Mathf.Min(h - 1, y1 + 3);
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var crop = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        var cpx = new Color32[cw * ch];
        for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) cpx[y * cw + x] = outPx[(y + y0) * w + x + x0];
        crop.SetPixels32(cpx); crop.Apply();

        // 허브: 대략 자리 둘레의 투명한 구멍 무게중심으로 맞춘 뒤 원으로 오린다(텍스처 y는 아래에서부터)
        Vector2 Hub(Vector2 approx)
        {
            Vector2 sum = Vector2.zero; int n = 0;
            int ax = (int)approx.x - x0, ay = (h - 1 - (int)approx.y) - y0;
            for (int y = ay - 45; y <= ay + 45; y++)
            for (int x = ax - 45; x <= ax + 45; x++)
            {
                if (x < 0 || y < 0 || x >= cw || y >= ch) continue;
                if ((new Vector2(x, y) - new Vector2(ax, ay)).sqrMagnitude > 45 * 45) continue;
                if (cpx[y * cw + x].a < 60) { sum += new Vector2(x, y); n++; }
            }
            return n > 20 ? sum / n : new Vector2(ax, ay);
        }
        var hl = Hub(sp.hubL); var hr = Hub(sp.hubR);
        int r = Mathf.RoundToInt(sp.hubRadius), size = r * 2;
        var hub = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var hpx = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int sx = Mathf.RoundToInt(hl.x) - r + x, sy = Mathf.RoundToInt(hl.y) - r + y;
            var c = sx >= 0 && sy >= 0 && sx < cw && sy < ch ? cpx[sy * cw + sx] : new Color32(0, 0, 0, 0);
            float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude;
            float edge = Mathf.Clamp01(r - d); // 둘레 1px 부드럽게
            c.a = (byte)(c.a * edge);
            hpx[y * size + x] = c;
        }
        hub.SetPixels32(hpx); hub.Apply();

        // 줄여서 저장(가로 768)
        int ow = 768, oh = Mathf.RoundToInt(768f * ch / cw);
        Save(Resize(crop, ow, oh), $"{OutDir}/{sp.name}.png", true);
        int hs = Mathf.RoundToInt(size * (ow / (float)cw));
        Save(Resize(hub, hs, hs), $"{OutDir}/{sp.name}_Hub.png", true);

        // 0~1 비율(위에서부터): HUD에 옮겨 적는다
        string N(float v) => v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        Rect Norm(RectInt q) => new((q.x - x0) / (float)cw, (q.y - (h - 1 - y1)) / (float)ch, q.width / (float)cw, q.height / (float)ch);
        var lab = Norm(sp.label); var str = Norm(sp.stripe);
        log.AppendLine($"{sp.name}: aspect {N(cw / (float)ch)}  hubL ({N(hl.x / cw)}, {N(1f - hl.y / ch)})  hubR ({N(hr.x / cw)}, {N(1f - hr.y / ch)})  hubR/w {N(r / (float)cw)}");
        log.AppendLine($"   label ({N(lab.x)}, {N(lab.y)}, {N(lab.width)}, {N(lab.height)})  stripe ({N(str.x)}, {N(str.y)}, {N(str.width)}, {N(str.height)})");
    }

    static Texture2D Load(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    static Texture2D Resize(Texture2D src, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        src.filterMode = FilterMode.Bilinear;
        // 큰 그림을 한 번에 줄이면 계단이 진다 → 절반씩 줄인다
        Texture cur = src; RenderTexture tmp = null;
        int cwid = src.width, chei = src.height;
        while (cwid / 2 >= w && chei / 2 >= h)
        {
            cwid /= 2; chei /= 2;
            var next = RenderTexture.GetTemporary(cwid, chei, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(cur, next);
            if (tmp) RenderTexture.ReleaseTemporary(tmp);
            cur = tmp = next;
        }
        Graphics.Blit(cur, rt);
        if (tmp) RenderTexture.ReleaseTemporary(tmp);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
        o.ReadPixels(new Rect(0, 0, w, h), 0, 0); o.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return o;
    }

    static void Save(Texture2D t, string path, bool alpha) => File.WriteAllBytes(path, t.EncodeToPNG());

    static void Import(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!ti) return;
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.CompressedHQ; // BC7
        ti.SaveAndReimport();
    }
}
