using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 설명(튜토리얼).
///  · 설명이 필요한 순간 Tutorial.Show(id)를 부르면 화면 왼쪽에 작은 알림(분류 · 제목 · "H 보기")이 뜬다.
///  · 알림이 떠 있을 때 H를 누르면 설명 팝업이 열린다: 강조 글씨, 오른쪽 그림, 이전/다음 페이지, 닫기.
///  · 레이가 대니의 편지 뒷면을 읽기 전(프롤로그)에는 아무 설명도 띄우지 않는다 — Tutorial.Unlock() 이후부터.
/// 그림은 Resources/Tutorials/{이름}.png (TutorialImageBaker가 장면에서 찍어 둔다).
/// </summary>
public static class Tutorial
{
    public class Page
    {
        public string heading, body, image;
        public Page(string heading, string body, string image = null) { this.heading = heading; this.body = body; this.image = image; }
    }

    public class Entry
    {
        public string id, category, title;
        public Page[] pages;
    }

    /// <summary>편지 뒷면을 읽었는가(이때부터 설명이 뜬다). 씬을 옮겨도 남는다.</summary>
    public static bool Unlocked { get; private set; }

    static readonly HashSet<string> shown = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart()
    {
        Unlocked = false;
        shown.Clear();
    }

    public static void Unlock() => Unlocked = true;

    /// <summary>설명 알림을 띄운다(이미 띄운 설명이거나 아직 잠겨 있으면 무시). 컷신 중이면 컷신이 끝난 뒤 뜬다.</summary>
    public static void Show(string id)
    {
        if (!Unlocked || shown.Contains(id) || !Catalog.TryGetValue(id, out var entry)) return;
        shown.Add(id);
        TutorialUI.Ensure().Enqueue(entry);
    }

    // ---------- 설명 목록 ----------

    // 강조 글씨(주황)
    static string H(string text) => $"<color=#F2A93B>{text}</color>";

    static readonly Dictionary<string, Entry> Catalog = new()
    {
        ["rewind"] = new Entry
        {
            id = "rewind", category = "카세트 플레이어", title = "되감기",
            pages = new[]
            {
                new Page("시간을 되감는다",
                    $"{H("R")}을 누르고 있으면 주변의 시간이 거꾸로 흐릅니다. 손을 떼면 다음 {H("5초")} 지점에서 멈추고, 그 순간부터 다시 움직입니다.\n\n" +
                    $"되감기 {H("1회")}가 {H("5초")}를 되돌리며, 카세트 플레이어에 끼운 테이프의 {H("남은 되감기 횟수")}를 씁니다.",
                    "rewind_1"),
                new Page("테이프와 충전",
                    $"재생용 테이프를 끼우고 있으면 노래가 흐르고, {H("곡이 하나 끝날 때마다 되감기 1회")}가 충전됩니다(최대 {H("3회")}).\n\n" +
                    $"단, 재생용 테이프로 한 번 되감으면 그 테이프는 {H("더 이상 재생되지 않습니다")}. {H("I")}로 인벤토리를 열거나 {H("2")}로 다른 테이프를 끼울 수 있고, " +
                    $"다 쓴 테이프는 {H("테이프 가게")}에서 새 테이프로 바꿀 수 있습니다.",
                    "rewind_2"),
                new Page("쓰러졌을 때",
                    $"쓰러져도 되감을 수 있는 테이프가 남아 있으면 {H("REWIND")} 화면이 뜹니다.\n\n" +
                    $"{H("R")}을 누르고 있으면 쓰러지기 전으로 돌아가고, 되감은 직후 {H("1.5초")} 동안은 공격을 받지 않습니다. " +
                    $"{H("Space")}를 누르면 되감기를 아끼는 대신 {H("마지막 저장 지점")}에서 다시 시작합니다.",
                    "rewind_3"),
            },
        },
        ["combat"] = new Entry
        {
            id = "combat", category = "전투", title = "맨손 격투",
            pages = new[]
            {
                new Page("연속 공격",
                    $"{H("좌클릭")}을 연타하면 {H("잽 → 크로스 → 돌려차기")}로 이어집니다.\n\n" +
                    $"마지막 {H("돌려차기")}가 가장 강하고, 세게 맞은 적은 크게 휘청입니다.",
                    "combat_1"),
                new Page("회피",
                    $"{H("우클릭")}을 누르면 누르고 있던 방향으로(아무 방향도 누르지 않았다면 뒤로) {H("구릅니다")}. " +
                    $"구르는 동안에는 {H("공격을 받지 않습니다")}.\n\n" +
                    $"{H("Shift")}를 누르고 있으면 달리고, {H("Space")}로 점프합니다.",
                    "combat_2"),
            },
        },
        ["aim"] = new Entry
        {
            id = "aim", category = "전투", title = "붉은 조준선",
            pages = new[]
            {
                new Page("총을 든 적",
                    $"총을 든 적이 {H("붉은 조준선")}으로 겨누면 약 1초 뒤에 쏩니다.\n\n" +
                    $"조준선이 보이면 {H("우클릭")}으로 피하거나 {H("벽 · 차 뒤")}로 몸을 숨기세요. 총알은 가려진 곳을 지나지 못합니다. " +
                    $"맞았다면 {H("R")}로 되감아 다시 해 볼 수 있습니다."),
            },
        },
        ["stealth"] = new Entry
        {
            id = "stealth", category = "잠입", title = "손전등과 숨기",
            pages = new[]
            {
                new Page("들키지 않고 지나가기",
                    $"순찰하는 조직원은 {H("손전등이 비추는 앞쪽")}만 봅니다. 그 안에 들어가면 머리 위의 {H("?")}가 차오르고, " +
                    $"다 차면 {H("!")}와 함께 근처 조직원이 모두 몰려옵니다.\n\n" +
                    $"{H("덤불 · 건물 · 차 뒤")}에 있으면 보이지 않습니다. 바로 뒤까지 붙으면 등 뒤라도 알아챕니다. " +
                    $"들켰다면 {H("R")}로 되감아 들키기 전으로 돌아갈 수 있습니다."),
            },
        },
        ["listen"] = new Entry
        {
            id = "listen", category = "카세트 플레이어", title = "듣고 모으기",
            pages = new[]
            {
                new Page("재생용 테이프",
                    $"가게에서 받은 {H("재생용 테이프")}를 끼우면 테이프가 돌아가고, {H("곡이 하나 끝날 때마다 되감기 1회")}가 쌓입니다(최대 {H("3회")}). " +
                    $"싸우는 동안에도 쌓입니다.\n\n" +
                    $"재생용 테이프로 한 번 되감으면 그 테이프는 {H("다시 재생되지 않습니다")}. 남은 횟수만큼은 계속 되감을 수 있고, 다 쓰면 가게에서 바꿀 수 있습니다."),
            },
        },
        ["heal"] = new Entry
        {
            id = "heal", category = "회복", title = "회복약",
            pages = new[]
            {
                new Page("회복약",
                    $"테이프와 함께 놓인 {H("빨간 구급상자")}에서 {H("회복약")}을 얻을 수 있습니다(최대 {H("5개")}).\n\n" +
                    $"{H("1")}을 누르면 체력이 {H("25")} 회복됩니다. 회복약은 되감아도 줄거나 늘지 않습니다. " +
                    $"{H("저장 지점")}(테이프 가게·구역 시작점)에 가면 체력이 {H("모두")} 회복됩니다."),
            },
        },
        ["shop"] = new Entry
        {
            id = "shop", category = "테이프 가게", title = "저장과 교환",
            pages = new[]
            {
                new Page("테이프 가게",
                    $"길 곳곳의 {H("테이프 가게")}는 {H("저장 지점")}입니다. 들르면 {H("체력이 모두 회복")}되고, 되감을 횟수 없이 쓰러지면 마지막으로 들른 가게나 {H("구역 시작점")}에서 다시 시작합니다.\n\n" +
                    $"가게 앞에서 {H("F")}를 누르면 {H("다 쓴 테이프 1개")}를 진열대의 {H("새 재생용 테이프 1개")}로 바꿀 수 있습니다. 진열된 테이프는 하나씩 한 번만 바꿀 수 있습니다."),
            },
        },
    };
}

/// <summary>설명 알림과 팝업을 그리는 화면(IMGUI, HUD와 같은 방식). 처음 Tutorial.Show 때 스스로 생긴다.</summary>
public class TutorialUI : MonoBehaviour
{
    static readonly Color Panel = new(0.05f, 0.05f, 0.06f, 0.9f);
    static readonly Color PanelSolid = new(0.06f, 0.06f, 0.07f, 0.98f);
    static readonly Color Line = new(0.32f, 0.30f, 0.27f, 1f);
    static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
    static readonly Color Paper = new(0.88f, 0.86f, 0.80f);
    static readonly Color Dim = new(0.62f, 0.60f, 0.56f);
    static readonly Color Close = new(0.86f, 0.22f, 0.18f);
    static readonly Color Shadow = new(0f, 0f, 0f, 0.6f);

    const float ToastSeconds = 9f;

    static TutorialUI instance;
    readonly Queue<Tutorial.Entry> queue = new();
    Tutorial.Entry toast, open;
    float toastStart;
    int page;
    float savedTimeScale = 1f;
    readonly Dictionary<string, Texture2D> images = new();
    GUIStyle tiny, small, body, bold, heading, key, centre;
    float styleScale = -1f;

    public static TutorialUI Ensure()
    {
        if (!instance) instance = new GameObject("TutorialUI").AddComponent<TutorialUI>();
        return instance;
    }

    /// <summary>지금 팝업이 열려 있는가(열려 있는 동안 게임은 멈추고 조작이 잠긴다)</summary>
    public static bool PopupOpen => instance && instance.open != null;
    /// <summary>테스트용: 지금 떠 있는 알림의 id(없으면 null)</summary>
    public static string ToastId => instance && instance.toast != null ? instance.toast.id : null;
    public static int PageIndex => instance ? instance.page : 0;

    public void Enqueue(Tutorial.Entry entry) => queue.Enqueue(entry);

    void OnDestroy()
    {
        if (open != null) ClosePopup();
        if (instance == this) instance = null;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (open != null)
        {
            if (kb == null) return;
            if (kb.hKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) ClosePopup();
            else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) Turn(1);
            else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) Turn(-1);
            return;
        }

        // 알림은 컷신·메뉴가 아닐 때만 하나씩 띄운다
        bool busy = StoryState.CutsceneActive || StoryState.Fade > 0.05f;
        if (toast == null && !busy && queue.Count > 0) { toast = queue.Dequeue(); toastStart = Time.unscaledTime; }
        if (toast == null) return;
        if (Time.unscaledTime - toastStart > ToastSeconds) { toast = null; return; }
        if (!busy && !StoryState.InputLocked && kb != null && kb.hKey.wasPressedThisFrame) OpenPopup(toast);
    }

    /// <summary>알림의 설명을 팝업으로 연다(H). 테스트에서도 부른다.</summary>
    public void OpenPopup(Tutorial.Entry entry)
    {
        open = entry;
        toast = null;
        page = 0;
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        StoryState.MenuOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>테스트용: 떠 있는 알림을 연다(H와 같다)</summary>
    public bool OpenToast()
    {
        if (toast == null) return false;
        OpenPopup(toast);
        return true;
    }

    public void ClosePopup()
    {
        open = null;
        Time.timeScale = savedTimeScale <= 0f ? 1f : savedTimeScale;
        StoryState.MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void Turn(int direction)
    {
        if (open == null) return;
        page = Mathf.Clamp(page + direction, 0, open.pages.Length - 1);
    }

    // ---------- 그리기 ----------

    void OnGUI()
    {
        float s = Screen.height / 1080f;
        BuildStyles(s);
        GUI.depth = -10; // HUD 위에
        if (open != null) DrawPopup(open, s);
        else if (toast != null && !StoryState.CutsceneActive) DrawToast(toast, s);
    }

    void DrawToast(Tutorial.Entry e, float s)
    {
        float t = Time.unscaledTime - toastStart;
        // 왼쪽에서 미끄러져 들어오고, 끝날 무렵 흐려진다
        float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f));
        float alpha = Mathf.Clamp01((ToastSeconds - t) / 0.6f) * slide;
        float w = 500 * s, h = 104 * s;
        var r = new Rect(Mathf.Lerp(-w, 40 * s, slide), Screen.height * 0.5f, w, h);

        Fill(r, Tint(Panel, alpha));
        Fill(new Rect(r.x, r.y, 4 * s, h), Tint(Gold, alpha));
        var icon = new Rect(r.x + 18 * s, r.y + 16 * s, 72 * s, 72 * s);
        DrawIcon(icon, e.category, alpha, s);

        float x = icon.xMax + 18 * s;
        Label(new Rect(x, r.y + 8 * s, w, 28 * s), $"{e.category}  ―", Mid(tiny, TextAnchor.MiddleLeft), Tint(Dim, alpha));
        Label(new Rect(x, r.y + 34 * s, w - (x - r.x), 40 * s), e.title, Mid(bold, TextAnchor.MiddleLeft), Tint(Gold, alpha));
        var cap = new Rect(x, r.y + 74 * s, 26 * s, 24 * s);
        Fill(cap, Tint(Paper, alpha));
        Label(cap, "H", key, Tint(new Color(0.08f, 0.08f, 0.09f), alpha), shadow: false); // 밝은 키 모양 위라 그림자를 넣으면 번져 보인다
        Label(new Rect(cap.xMax + 8 * s, cap.y - 2 * s, 120 * s, 28 * s), "보기", Mid(tiny, TextAnchor.MiddleLeft), Tint(Paper, alpha));
    }

    void DrawIcon(Rect r, string category, float alpha, float s)
    {
        Fill(r, Tint(new Color(0.12f, 0.12f, 0.13f), alpha));
        Frame(r, Tint(Line, alpha), 2f * s);
        // 분류마다 다른 표시: 카세트 플레이어 = 되감기 기호, 그 밖 = 마름모
        string glyph = category == "카세트 플레이어" ? "◀◀" : "◆";
        var style = new GUIStyle(centre) { fontSize = Mathf.RoundToInt(30 * s), alignment = TextAnchor.MiddleCenter };
        Label(r, glyph, style, Tint(Paper, alpha));
    }

    void DrawPopup(Tutorial.Entry e, float s)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
        float w = 1160 * s, h = 680 * s;
        var win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        Fill(win, PanelSolid);
        Frame(win, Line, 2f * s);

        // 제목 줄 + 닫기
        Label(new Rect(win.x + 36 * s, win.y + 14 * s, w, 60 * s), $"{e.category} 튜토리얼", Mid(heading, TextAnchor.MiddleLeft), Paper);
        var close = new Rect(win.xMax - 78 * s, win.y + 22 * s, 48 * s, 40 * s);
        Fill(close, Close);
        Label(close, "✕", new GUIStyle(centre) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleCenter }, Color.white);
        if (Clicked(close)) { ClosePopup(); return; }

        var p = e.pages[page];
        // 부제 줄
        var sub = new Rect(win.x + 36 * s, win.y + 84 * s, w - 72 * s, 48 * s);
        Fill(sub, new Color(0.10f, 0.10f, 0.11f));
        Fill(new Rect(sub.x, sub.yMax - 2 * s, sub.width, 2 * s), Line);
        Label(new Rect(sub.x + 16 * s, sub.y, sub.width - 32 * s, sub.height), $"「{e.title}」 {p.heading}", Mid(bold, TextAnchor.MiddleLeft), Paper);

        // 본문(왼쪽) + 그림(오른쪽). 그림이 없으면 본문이 폭을 다 쓴다
        var content = new Rect(win.x + 36 * s, sub.yMax + 24 * s, w - 72 * s, h - (sub.yMax - win.y) - 24 * s - 136 * s);
        Fill(content, new Color(0.08f, 0.08f, 0.09f));
        var image = Image(p.image);
        float pad = 36 * s;
        if (image)
        {
            float iw = content.width * 0.46f, ih = iw * image.height / image.width;
            ih = Mathf.Min(ih, content.height - pad * 2f);
            iw = ih * image.width / image.height;
            var ir = new Rect(content.xMax - pad - iw, content.y + (content.height - ih) / 2f, iw, ih);
            GUI.DrawTexture(ir, image, ScaleMode.ScaleAndCrop);
            Frame(ir, Line, 2f * s);
            Label(new Rect(content.x + pad, content.y + pad, ir.x - content.x - pad * 2f, content.height - pad * 2f), p.body, body, Paper);
        }
        else
            Label(new Rect(content.x + pad, content.y + pad, content.width - pad * 2f, content.height - pad * 2f), p.body, body, Paper);

        // 페이지 넘기기
        float by = win.yMax - 112 * s, bw = 210 * s, bh = 46 * s;
        var prev = new Rect(win.center.x - bw - 90 * s, by, bw, bh);
        var next = new Rect(win.center.x + 90 * s, by, bw, bh);
        bool hasPrev = page > 0, hasNext = page < e.pages.Length - 1;
        PageButton(prev, "이전 페이지", hasPrev, s);
        PageButton(next, "다음 페이지", hasNext, s);
        Label(new Rect(win.center.x - 80 * s, by, 160 * s, bh), $"{page + 1:00} / {e.pages.Length:00}", Mid(bold, TextAnchor.MiddleCenter), Paper);
        Label(new Rect(win.x, win.yMax - 50 * s, w - 36 * s, 30 * s), "A / D  페이지 넘기기     H / Esc  닫기", Mid(tiny, TextAnchor.MiddleRight), Dim);
        if (hasPrev && Clicked(prev)) Turn(-1);
        if (hasNext && Clicked(next)) Turn(1);
    }

    void PageButton(Rect r, string text, bool enabled, float s)
    {
        Fill(r, enabled ? new Color(0.16f, 0.16f, 0.17f) : new Color(0.10f, 0.10f, 0.11f));
        Frame(r, enabled ? Gold : Line, 2f * s);
        Label(r, text, Mid(bold, TextAnchor.MiddleCenter), enabled ? Paper : Dim);
    }

    /// <summary>한 줄 글씨를 칸 안에서 세로 가운데로(칸 높이를 넉넉히 주면 작은 화면에서도 잘리지 않는다)</summary>
    static GUIStyle Mid(GUIStyle style, TextAnchor anchor) => new(style) { alignment = anchor, clipping = TextClipping.Overflow };

    Texture2D Image(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!images.TryGetValue(name, out var tex)) images[name] = tex = Resources.Load<Texture2D>("Tutorials/" + name);
        return tex;
    }

    static bool Clicked(Rect r)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
        e.Use();
        return true;
    }

    void BuildStyles(float s)
    {
        if (Mathf.Approximately(s, styleScale)) return;
        styleScale = s;
        tiny = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17 * s), richText = true };
        small = new GUIStyle(tiny) { fontSize = Mathf.RoundToInt(19 * s) };
        body = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(23 * s), wordWrap = true, richText = true };
        bold = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24 * s), fontStyle = FontStyle.Bold, richText = true };
        heading = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(32 * s), fontStyle = FontStyle.Bold };
        key = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        centre = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
    }

    static Color Tint(Color c, float a) => new(c.r, c.g, c.b, c.a * a);

    static void Label(Rect r, string text, GUIStyle style, Color color, bool shadow = true)
    {
        if (!shadow) { style.normal.textColor = color; GUI.Label(r, text, style); return; }
        // 그림자 + 글씨(강조 색 태그는 그대로 두고, 그림자는 색 태그를 지운 글씨로)
        var shadowText = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
        style.normal.textColor = new Color(0f, 0f, 0f, Shadow.a * color.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), shadowText, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
    }

    static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static void Frame(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
}
