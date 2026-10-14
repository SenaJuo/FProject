using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 글자가 칸을 넘치지 않게(2026-10-13 "글자 안 맞는 거 싹 고쳐 줘"): 칸보다 길면 들어갈 때까지 글자 크기를 줄인다.
/// 한 줄 글자는 폭을, 줄바꿈 글자는 높이를 잰다. 줄이지 않아도 되면 원래 크기 그대로(대부분은 계산 한 번).
/// 검사(Recording)를 켜면 그린 글자가 실제로 차지한 영역을 모아 둔다 — 겹침·넘침 검사(UiLayoutTest)가 쓴다.
/// </summary>
public static class UIText
{
    static readonly GUIContent content = new();
    const int MinSize = 9;

    /// <summary>화면 배율: 1920×1080 기준. 세로만 보면 좁은 화면(게임 창을 작게 띄운 경우)에서 가로가 모자라 글자·칸이 겹친다 → 둘 중 작은 쪽</summary>
    public static float Scale => Mathf.Min(Screen.height / 1080f, Screen.width / 1920f);

    // ---------- 검사용 기록 ----------
    public struct Drawn { public Rect box, cell; public string text; public bool overflow; public int size, orig; public float depth; public int frame; }
    public static bool Recording;
    // 그룹(BeginGroup) 안에서 그린 글자도 화면 좌표로: 유니티 내부 GUIClip.Unclip
    static System.Reflection.MethodInfo unclip;
    static Vector2 Unclip(Vector2 p)
    {
        unclip ??= typeof(GUI).Assembly.GetType("UnityEngine.GUIClip")?.GetMethod("Unclip", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(Vector2) }, null);
        return unclip != null ? (Vector2)unclip.Invoke(null, new object[] { p }) : p;
    }
    public static readonly List<Drawn> Log = new();

    static readonly Regex Tags = new("<[^>]+>");
    /// <summary>서식 태그(&lt;color&gt; 등)를 뺀 글자</summary>
    public static string Plain(string text) => string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text : Tags.Replace(text, "");

    /// <summary>칸에 맞는 글자 크기(원래 크기보다 커지지는 않는다)</summary>
    public static int FitSize(Rect r, string text, GUIStyle style) { var f = Fit(r, text, style); return f.size; }

    struct Fitted { public int size; public bool fits, single; public string text; }

    /// <summary>
    /// 칸 높이가 두 줄이 안 되면 "한 줄 칸": 줄바꿈 없이 폭에만 맞춘다(유니티 기본 라벨은 줄바꿈이 켜져 있어,
    /// 한 줄 칸에서도 글자가 두 줄로 꺾여 아래로 삐져나가거나 잘렸다). 가장 작게 해도 안 들어가면 끝을 …로 줄인다.
    /// </summary>
    static Fitted Fit(Rect r, string text, GUIStyle style)
    {
        int original = style.fontSize > 0 ? style.fontSize : (style.font ? style.font.fontSize : 14);
        var f = new Fitted { size = original, fits = true, text = text };
        if (string.IsNullOrEmpty(text) || r.width <= 1f) return f;
        int keepSize = style.fontSize; bool keepWrap = style.wordWrap;
        style.fontSize = original;
        f.single = !style.wordWrap || (r.height < style.lineHeight * 1.8f && text.IndexOf('\n') < 0);
        style.wordWrap = !f.single;
        content.text = text;
        int size = original;
        for (int i = 0; i < 24; i++)
        {
            style.fontSize = size;
            if (f.single)
            {
                // 한 줄: 폭이 들어가야 하고, 칸 높이보다 글자가 너무 크면(30% 넘게) 줄인다 — 조금 큰 것은 칸 밖으로 살짝 나와도 잘리지 않게 그린다
                var sz = style.CalcSize(content);
                f.fits = sz.x <= r.width + 1f && sz.y - style.padding.vertical <= r.height * 1.3f + 1f;
            }
            else f.fits = style.CalcHeight(content, r.width) <= Mathf.Max(r.height, style.lineHeight) + 1f;
            if (f.fits || size <= MinSize) break;
            size = Mathf.Max(MinSize, Mathf.FloorToInt(size * 0.93f));
        }
        f.size = size;
        if (!f.fits && f.single)
        {
            // 끝을 줄인다(서식 태그가 있으면 태그를 뺀 글자로)
            string plain = Plain(text);
            for (int n = plain.Length - 1; n > 0; n--)
            {
                content.text = plain.Substring(0, n).TrimEnd() + "…";
                if (style.CalcSize(content).x <= r.width + 1f) { f.text = content.text; f.fits = true; break; }
            }
        }
        style.fontSize = keepSize; style.wordWrap = keepWrap;
        return f;
    }

    /// <summary>style(이미 맞춘 크기)로 r 안에 그린 글자가 실제로 차지하는 영역</summary>
    static Rect Occupied(Rect r, string text, GUIStyle style)
    {
        content.text = text;
        Vector2 size = style.CalcSize(content);
        if (style.wordWrap) size = new Vector2(Mathf.Min(r.width, size.x), style.CalcHeight(content, r.width));
        size.x = Mathf.Max(0f, size.x - style.padding.horizontal);
        size.y = Mathf.Max(0f, size.y - style.padding.vertical);
        float x = r.x + style.padding.left, y = r.y + style.padding.top;
        float freeX = r.width - style.padding.horizontal - size.x, freeY = r.height - style.padding.vertical - size.y;
        switch (style.alignment)
        {
            case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x += freeX / 2f; break;
            case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x += freeX; break;
        }
        switch (style.alignment)
        {
            case TextAnchor.MiddleLeft: case TextAnchor.MiddleCenter: case TextAnchor.MiddleRight: y += freeY / 2f; break;
            case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: y += freeY; break;
        }
        return new Rect(x, y, size.x, size.y);
    }

    static void Record(Rect r, string text, GUIStyle style, bool fits, int orig)
    {
        if (!Recording || Event.current == null || Event.current.type != EventType.Repaint) return;
        string plain = Plain(text);
        if (string.IsNullOrWhiteSpace(plain)) return;
        var box = Occupied(r, text, style);
        Log.Add(new Drawn
        {
            box = new Rect(Unclip(box.position), box.size),
            cell = new Rect(Unclip(r.position), r.size),
            text = plain, overflow = !fits, size = style.fontSize, depth = GUI.depth, frame = Time.frameCount, orig = orig,
        });
    }

    /// <summary>
    /// 글자 색을 모든 상태(평소 · 마우스 올림 · 누름 · 선택)에 똑같이(2026-10-14): 유니티 기본 글자 칸은 마우스가 올라가면
    /// '마우스 올림' 상태의 색으로 그리는데, 우리는 평소 색만 바꿔 왔다 → 마우스를 올리면 그림자까지 밝게 찍혀 글자가 두 겹으로 번졌다
    /// </summary>
    static void Ink(GUIStyle style, Color c)
    {
        style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = c;
        style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = c;
    }

    /// <summary>GUI.Label과 같지만 넘치면 줄여서 그린다</summary>
    public static void Label(Rect r, string text, GUIStyle style)
    {
        Ink(style, style.normal.textColor);
        var f = Fit(r, text, style);
        int keep = style.fontSize; bool wrap = style.wordWrap; var clip = style.clipping;
        style.fontSize = f.size; style.wordWrap = !f.single; style.clipping = TextClipping.Overflow;
        GUI.Label(r, f.text, style);
        Record(r, f.text, style, f.fits, keep);
        style.fontSize = keep; style.wordWrap = wrap; style.clipping = clip;
    }

    /// <summary>그림자를 따로 그릴 때(검사에 넣지 않는다)</summary>
    public static void ShadowOnly(Rect r, string text, GUIStyle style)
    {
        Ink(style, style.normal.textColor);
        var f = Fit(r, text, style);
        int keep = style.fontSize; bool wrap = style.wordWrap; var clip = style.clipping;
        style.fontSize = f.size; style.wordWrap = !f.single; style.clipping = TextClipping.Overflow;
        GUI.Label(r, Plain(f.text), style);
        style.fontSize = keep; style.wordWrap = wrap; style.clipping = clip;
    }

    /// <summary>글자 크기에 맞춘 그림자 어긋남: 작은 글자에 2px 그림자는 글자가 두 겹으로 보인다</summary>
    public static float ShadowOffset(int fontSize, float max = 2f) => Mathf.Clamp(Mathf.Round(fontSize / 14f), 1f, max);

    /// <summary>그림자 + 본문: 두 번 모두 같은 크기로. 그림자는 서식 색을 뺀다(색 글자가 두 번 찍혀 보이지 않게)</summary>
    public static void Shadowed(Rect r, string text, GUIStyle style, Color ink, Color paper, float offset = 2f, string shadowText = null)
    {
        var f = Fit(r, text, style);
        int keep = style.fontSize; bool wrap = style.wordWrap; var clip = style.clipping;
        style.fontSize = f.size; style.wordWrap = !f.single; style.clipping = TextClipping.Overflow;
        float o = ShadowOffset(f.size, offset);
        Ink(style, ink);
        GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), Plain(shadowText != null && f.text == text ? shadowText : f.text), style);
        Ink(style, paper);
        GUI.Label(r, f.text, style);
        Record(r, f.text, style, f.fits, keep);
        style.fontSize = keep; style.wordWrap = wrap; style.clipping = clip;
    }
}
