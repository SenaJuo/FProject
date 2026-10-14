using UnityEngine;

/// <summary>
/// 화면 꾸밈 공통(2026-10-14 "UI가 촌스럽고 안 예쁘다"): 누아르 · 카세트 분위기.
///  · 글꼴: Noto Sans KR(SIL OFL — 게임에 넣어 배포해도 된다). 모든 OnGUI 처음에 Begin()을 불러 기본 글꼴로 쓴다
///  · 판: 어두운 반투명 + 둥근 모서리 + 아주 얇은 테두리 + 부드러운 그림자(두꺼운 네모 테두리 대신)
///  · 색: 따뜻한 흰색 글씨, 호박색 강조(고른 줄), 붉은 테이프색(경고), 흐린 회갈색(보조 글씨)
/// 둥근 판 그림은 실행할 때 한 번 만들어 9칸 늘이기(GUIStyle border)로 그린다.
/// </summary>
public static class UiTheme
{
    public static readonly Color Ink = new(0.055f, 0.055f, 0.065f, 0.92f);      // 판
    public static readonly Color InkSoft = new(0.10f, 0.10f, 0.115f, 0.85f);    // 줄 바탕
    public static readonly Color InkHover = new(0.15f, 0.145f, 0.14f, 0.95f);   // 고른 줄 바탕
    public static readonly Color Hair = new(1f, 1f, 1f, 0.10f);                 // 얇은 선
    public static readonly Color Paper = new(0.93f, 0.90f, 0.84f);              // 글씨
    public static readonly Color Dim = new(0.60f, 0.58f, 0.54f);                // 보조 글씨
    public static readonly Color Amber = new(0.96f, 0.72f, 0.30f);              // 강조
    public static readonly Color AmberSoft = new(0.96f, 0.72f, 0.30f, 0.16f);
    public static readonly Color Tape = new(0.86f, 0.33f, 0.24f);               // 경고 · 체력 낮음
    public static readonly Color Track = new(1f, 1f, 1f, 0.10f);                // 밀대 바탕

    static Font font;
    static Texture2D round, roundSoft, shadow, roundSmall, roundTiny;
    static GUIStyle roundStyle, softStyle, shadowStyle, smallStyle, tinyStyle;

    /// <summary>칸 크기에 맞는 둥근 판 그림: 모서리 그림(12px)이 칸보다 크면 찌그러져 "+" 모양이 됐다 → 작은 칸은 작은 반지름</summary>
    static GUIStyle RoundFor(Rect r)
    {
        float m = Mathf.Min(r.width, r.height);
        return m >= 26f ? roundStyle : m >= 10f ? smallStyle : tinyStyle;
    }

    /// <summary>게임 글꼴(없으면 null — 유니티 기본 글꼴)</summary>
    public static Font Font
    {
        get
        {
            if (!font) font = Resources.Load<Font>("Fonts/NotoSansKR");
            return font;
        }
    }

    /// <summary>OnGUI 처음에 부른다: 기본 글꼴을 게임 글꼴로</summary>
    public static void Begin()
    {
        var f = Font;
        if (f && GUI.skin.font != f) GUI.skin.font = f;
    }

    // ---------- 그리기 ----------

    /// <summary>둥근 판(색은 color, 모서리 반지름 radius px — 화면 배율과 무관한 그림을 늘여 쓴다)</summary>
    public static void Panel(Rect r, Color color, float s)
    {
        if (Event.current.type != EventType.Repaint) return;
        Ensure();
        var keep = GUI.color;
        GUI.color = color;
        RoundFor(r).Draw(r, false, false, false, false);
        GUI.color = keep;
    }

    /// <summary>판 + 그림자 + 얇은 테두리(창 하나)</summary>
    public static void Window(Rect r, float s, Color? fill = null)
    {
        if (Event.current.type != EventType.Repaint) return;
        Ensure();
        var keep = GUI.color;
        float o = 18f * s;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        shadowStyle.Draw(new Rect(r.x - o, r.y - o + 6f * s, r.width + o * 2f, r.height + o * 2f), false, false, false, false);
        GUI.color = fill ?? Ink;
        roundStyle.Draw(r, false, false, false, false);
        GUI.color = Hair;
        softStyle.Draw(r, false, false, false, false);
        GUI.color = keep;
    }

    /// <summary>고를 수 있는 줄: 평소엔 옅은 바탕, 고르면 밝은 바탕 + 왼쪽 호박색 막대 + 옅은 호박빛</summary>
    public static void RowBg(Rect r, bool selected, float s)
    {
        if (Event.current.type != EventType.Repaint) return;
        Panel(r, selected ? InkHover : InkSoft, s);
        if (!selected) return;
        Panel(r, AmberSoft, s);
        Fill(new Rect(r.x, r.y + r.height * 0.22f, 3f * s, r.height * 0.56f), Amber);
    }

    public static void Fill(Rect r, Color c)
    {
        if (Event.current.type != EventType.Repaint) return;
        var keep = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = keep;
    }

    /// <summary>둥근 테두리만(고른 칸 표시 등)</summary>
    public static void Outline(Rect r, Color c)
    {
        if (Event.current.type != EventType.Repaint) return;
        Ensure();
        var keep = GUI.color;
        GUI.color = c;
        softStyle.Draw(r, false, false, false, false);
        GUI.color = keep;
    }

    /// <summary>둥근 막대(체력 · 능력 시간): 옅은 바탕 위에 ratio만큼 채운다</summary>
    public static void Bar(Rect r, float ratio, Color fill, float s, Color? track = null)
    {
        Panel(r, track ?? new Color(0f, 0f, 0f, 0.45f), s);
        ratio = Mathf.Clamp01(ratio);
        if (ratio <= 0.001f) return;
        Panel(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * ratio), r.height), fill, s);
    }

    /// <summary>키 모양(밝은 둥근 칸 + 어두운 글자). 칸 폭은 글자에 맞춘다. 반환: 그린 칸</summary>
    public static Rect KeyCap(Vector2 at, float height, string key, GUIStyle style, float s)
    {
        keyContent.text = key;
        float w = Mathf.Max(height, style.CalcSize(keyContent).x + 14f * s);
        var r = new Rect(at.x, at.y, w, height);
        Panel(r, new Color(0.93f, 0.90f, 0.84f, 0.95f), s);
        style.normal.textColor = new Color(0.08f, 0.08f, 0.09f);
        UIText.Label(r, key, style);
        return r;
    }
    static readonly GUIContent keyContent = new();

    /// <summary>작은 꼬리표(장착 · 재생용 등): 둥근 색 칸 + 어두운 글자</summary>
    public static void Tag(Rect r, string text, Color color, GUIStyle style, float s)
    {
        Panel(r, color, s);
        style.normal.textColor = new Color(0.07f, 0.07f, 0.08f);
        UIText.Label(r, text, style);
    }

    /// <summary>가는 가로선</summary>
    public static void Hairline(float x, float y, float w, float s, Color? c = null) => Fill(new Rect(x, y, w, Mathf.Max(1f, s)), c ?? Hair);

    // ---------- 둥근 판 그림 ----------

    static void Ensure()
    {
        if (round) return;
        round = Rounded(64, 12, false, 0f);
        roundSoft = Rounded(64, 12, true, 0f);   // 테두리만(1.5px)
        shadow = Rounded(96, 40, false, 28f);    // 가장자리가 흐린 그림자
        roundStyle = Nine(round, 12);
        roundSmall = Rounded(16, 4, false, 0f);
        roundTiny = Rounded(8, 2, false, 0f);
        smallStyle = Nine(roundSmall, 4);
        tinyStyle = Nine(roundTiny, 2);
        softStyle = Nine(roundSoft, 12);
        shadowStyle = Nine(shadow, 40);
    }

    static GUIStyle Nine(Texture2D tex, int border) => new() { normal = { background = tex }, border = new RectOffset(border, border, border, border) };

    /// <summary>size×size 둥근 사각형(반지름 radius). outline이면 테두리만, blur > 0이면 가장자리를 흐리게</summary>
    static Texture2D Rounded(int size, int radius, bool outline, float blur)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 둥근 사각형까지의 거리(안쪽 음수)
            float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
            float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            float a;
            if (blur > 0f) a = Mathf.Clamp01(-outside / blur);
            else if (outline) a = Mathf.Clamp01(1f - Mathf.Abs(outside + 0.75f) / 0.9f);
            else a = Mathf.Clamp01(0.5f - outside);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
