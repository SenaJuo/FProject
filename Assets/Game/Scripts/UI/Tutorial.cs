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

    // 한 번 띄운 설명은 다시 띄우지 않는다 — 플레이를 새로 시작하거나 이어 해도(PlayerPrefs에 남긴다).
    // 전에는 메모리에만 있어 이어 할 때마다 회복약·완벽 회피 알림이 또 떴다(2026-10-13). 다시 보기는 Esc → 튜토리얼
    static readonly HashSet<string> shown = new();
    const string SeenKey = "tutorial_seen";

    static void LoadSeen()
    {
        shown.Clear();
        foreach (var id in PlayerPrefs.GetString(SeenKey, "").Split(',')) if (id.Length > 0) shown.Add(id);
    }

    static void SaveSeen() { PlayerPrefs.SetString(SeenKey, string.Join(",", shown)); PlayerPrefs.Save(); }

    /// <summary>시험용: 본 기록을 지운다(다음 Show부터 다시 뜬다)</summary>
    public static void ForgetSeen() { shown.Clear(); PlayerPrefs.DeleteKey(SeenKey); }

    /// <summary>Esc → 튜토리얼 목록(분류 순)</summary>
    public static IEnumerable<Entry> All
    {
        get
        {
            var order = new[] { "카세트 플레이어", "전투", "잠입", "회복", "테이프 가게" };
            var list = new List<Entry>(Catalog.Values);
            list.Sort((a, b) => System.Array.IndexOf(order, a.category).CompareTo(System.Array.IndexOf(order, b.category)));
            return list;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart()
    {
        Unlocked = false;
        LoadSeen();
        TutorialUI.ClearPending();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (TutorialUI.HasPending) TutorialUI.Ensure();
    }

    public static void Unlock() => Unlocked = true;

    /// <summary>새 게임(타이틀에서 시작): 설명 잠금·못 띄운 알림을 처음으로(본 설명은 새 게임에서도 다시 띄우지 않는다)</summary>
    public static void ResetProgress()
    {
        Unlocked = false;
        TutorialUI.ClearPending();
    }

    /// <summary>설명 알림을 띄운다(이미 띄운 설명이거나 아직 잠겨 있으면 무시). 컷신 중이면 컷신이 끝난 뒤 뜬다.</summary>
    public static void Show(string id)
    {
        if (!Unlocked || shown.Contains(id) || !Catalog.TryGetValue(id, out var entry)) return;
        shown.Add(id);
        SaveSeen();
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
        ["genre"] = new Entry
        {
            id = "genre", category = "카세트 플레이어", title = "장르 테이프",
            pages = new[]
            {
                new Page("되감을까, 능력을 쓸까",
                    $"장르가 있는 테이프를 끼우고 {H("R")}을 누르면 잠깐 시간이 느려지며 고를 수 있습니다.\n\n" +
                    $"{H("Q")} — 되감기\n{H("E")} — 그 테이프의 {H("장르 능력")} (되감기 1회를 씁니다)\n\n" +
                    $"테이프의 장르와 능력은 {H("I")} 인벤토리에서 테이프를 고르면 보입니다."),
                new Page($"시티팝 — {TapeAbility.Name(TapeGenre.CityPop)} ({TapeAbility.Seconds(TapeGenre.CityPop):0}초)",
                    $"주변의 시간이 {H("4분의 1")}로 느려지고, {H("레이만 제 속도")}로 움직입니다.\n\n" +
                    $"적이 굳은 듯 느려진 사이 여럿을 한꺼번에 정리하세요. 레이 뒤로 네온 잔상이 남습니다."),
                new Page($"재즈 — {TapeAbility.Name(TapeGenre.Jazz)} ({TapeAbility.Seconds(TapeGenre.Jazz):0}초)",
                    $"구르기의 {H("무적 시간이 두 배")}, 구르는 거리도 조금 늘어납니다.\n\n" +
                    $"완벽 회피가 {H("더 쉽게")} 나오고, 회피 반격의 피해가 {H("1.5배")}입니다."),
                new Page($"소울 — {TapeAbility.Name(TapeGenre.Soul)} ({TapeAbility.Seconds(TapeGenre.Soul):0}초)",
                    $"주먹의 피해가 {H("1.5배")}입니다.\n\n" +
                    $"방패 경찰의 방패도 {H("막지 못합니다")} — 정면에서 그대로 쳐도 들어갑니다."),
                new Page($"앰비언트 — {TapeAbility.Name(TapeGenre.Ambient)} ({TapeAbility.Seconds(TapeGenre.Ambient):0}초)",
                    $"받는 피해가 {H("절반")}으로 줄어듭니다.\n\n" +
                    $"켜져 있는 동안 적을 쓰러뜨릴 때마다 체력이 {H("8")} 회복됩니다."),
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
        ["shield"] = new Entry
        {
            id = "shield", category = "전투", title = "방패 경찰",
            pages = new[]
            {
                new Page("방패를 든 적",
                    $"방패 경찰은 {H("정면")}에서 오는 주먹을 방패로 막습니다(금속 소리가 나고 피해가 들어가지 않습니다).\n\n" +
                    $"연속 공격의 {H("세 번째 돌려차기")}는 방패를 쳐내 잠시 {H("무방비")}로 만듭니다. " +
                    $"아니면 {H("옆 · 뒤")}로 돌아가 치세요."),
            },
        },
        ["takedown"] = new Entry
        {
            id = "takedown", category = "잠입", title = "몰래 제압",
            pages = new[]
            {
                new Page("등 뒤에서 한 번에",
                    $"아직 {H("눈치채지 못한 적")}의 {H("등 뒤")}로 바짝 다가가면 {H("Q: 몰래 제압")}이 뜹니다.\n\n" +
                    $"{H("Q")}를 누르면 뒤에서 한 번에 쓰러뜨립니다. 소리를 내지 않아 {H("근처 적이 몰려오지 않습니다")}. " +
                    $"손전등 순찰은 의심 막대가 다 차기 전, 옥상의 저격수는 조준경을 보는 동안 등 뒤에서 제압할 수 있습니다."),
            },
        },
        ["perfect"] = new Entry
        {
            id = "perfect", category = "전투", title = "완벽 회피",
            pages = new[]
            {
                new Page("주먹이 닿기 직전에 구른다",
                    $"적의 주먹이 닿기 {H("직전")}에 {H("우클릭")}으로 구르면 시간이 느려지고, 그 적은 1초 남짓 {H("빈틈투성이")}가 됩니다. 방패도 내려갑니다.\n\n" +
                    $"그 순간 {H("좌클릭")}하면 그 적에게 달려들어 {H("회피 반격")}(강한 돌려차기)이 나갑니다.\n\n" +
                    $"머리 위에 {H("붉은 !!")}가 뜨면 {H("강타")}입니다. 맞받아 치지 말고 피하세요. 방패 경찰은 정면으로 두 번 막으면 밀쳐냅니다 — 옆으로 돌거나 완벽 회피로 틈을 만드세요."),
            },
        },
        ["memory"] = new Entry
        {
            id = "memory", category = "카세트 플레이어", title = "되감기의 기억",
            pages = new[]
            {
                new Page("지워진 시간은 레이만 기억한다",
                    $"되감으면 세상은 아무것도 모른 채 되돌아가지만, {H("레이는 지워진 몇 초를 기억합니다.")}\n\n" +
                    $"그때 레이를 {H("발견했던 순찰")}은 한동안 손전등 {H("시야가 바닥에 보입니다")}. 의심할수록 붉어집니다. 이번엔 그 빛을 피해 지나가세요.\n\n" +
                    $"그때 레이를 {H("때렸던 적")}은 다음 공격 {RewindMemory.ForeseenAttacks}번 동안 머리 위에 {H("타이밍 막대")}가 뜹니다. " +
                    $"막대가 줄어들어 {H("지금!")}이 뜰 때 {H("우클릭")}으로 구르면 완벽 회피입니다(평소보다 조금 일찍 굴러도 됩니다)."),
            },
        },
        ["sniper"] = new Entry
        {
            id = "sniper", category = "전투", title = "저격수",
            pages = new[]
            {
                new Page("멀리서 겨누는 총",
                    $"옥상의 저격수는 멀리서도 {H("굵은 붉은 조준선")}으로 겨누고, 약 2초 뒤 {H("크게 아픈 한 발")}을 쏩니다.\n\n" +
                    $"조준선이 보이면 {H("노점 방수포 · 트럭 · 팔레트")}처럼 사람 키보다 높은 것 뒤로 숨으세요. " +
                    $"맞았다면 {H("R")}로 되감아 엄폐물 뒤로 돌아갈 수 있습니다. 골목의 {H("철계단")}으로 옥상에 올라가 가까이 붙어 치면 겨누기가 끊깁니다."),
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
    static readonly Queue<Tutorial.Entry> queue = new(); // 장면이 바뀌어도 남는다(프롤로그 끝에 못 띄운 알림을 1장에서 띄운다)
    Tutorial.Entry toast, open;
    bool fromMenu; // Esc → 튜토리얼에서 열었다: 시간·메뉴 상태는 일시정지 메뉴가 쥐고 있다
    float toastStart;
    int page;
    readonly Dictionary<string, Texture2D> images = new();
    GUIStyle tiny, small, body, bold, heading, key, centre, glyphStyle, closeStyle;
    float styleScale = -1f;
    readonly Dictionary<(GUIStyle, TextAnchor), GUIStyle> midStyles = new();
    static readonly Dictionary<string, string> shadowTexts = new();


    public static TutorialUI Ensure()
    {
        if (!instance) instance = new GameObject("TutorialUI").AddComponent<TutorialUI>();
        return instance;
    }

    /// <summary>지금 팝업이 열려 있는가(열려 있는 동안 게임은 멈추고 조작이 잠긴다)</summary>
    public static bool PopupOpen => instance && instance.open != null;
    /// <summary>Esc → 튜토리얼에서 연 팝업이 열려 있다(일시정지 메뉴는 그동안 입력을 받지 않고 그리지 않는다)</summary>
    public static bool MenuPopupOpen => instance && instance.open != null && instance.fromMenu;

    /// <summary>일시정지 메뉴에서 설명을 연다: 시간·커서는 메뉴가 이미 멈춰 두었다</summary>
    public void OpenFromMenu(Tutorial.Entry entry)
    {
        open = entry;
        page = 0;
        fromMenu = true;
    }
    /// <summary>테스트용: 지금 떠 있는 알림의 id(없으면 null)</summary>
    public static string ToastId => instance && instance.toast != null ? instance.toast.id : null;
    public static int PageIndex => instance ? instance.page : 0;

    public void Enqueue(Tutorial.Entry entry) => queue.Enqueue(entry);

    void OnDestroy()
    {
        if (open != null) ClosePopup();
        if (toast != null) // 띄우던 알림은 다음 장면에서 처음부터 다시
        {
            var rest = queue.ToArray();
            queue.Clear();
            queue.Enqueue(toast);
            foreach (var e in rest) queue.Enqueue(e);
            toast = null;
        }
        if (instance == this) instance = null;
    }

    /// <summary>아직 띄우지 못한 알림이 있는가</summary>
    public static bool HasPending => queue.Count > 0;

    /// <summary>플레이를 시작할 때 비운다</summary>
    public static void ClearPending() => queue.Clear();

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
        // 컷신·페이드·메뉴에 가려진 동안은 알림 시간이 흐르지 않는다(보지도 못하고 사라지면 다시 뜨지 않는다)
        if (busy || StoryState.Paused) toastStart += Time.unscaledDeltaTime;
        if (Time.unscaledTime - toastStart > ToastSeconds) { toast = null; return; }
        // 다른 것이 시간을 멈추거나 늦춘 동안(타격 멈춤·슬로모션·인벤토리·가게·사망 화면)에는 열지 않는다:
        // 닫을 때 그 시간 속도를 되살리면서 게임이 슬로모션에 갇히거나 가게 창 뒤로 세상이 움직였다
        if (!busy && !StoryState.InputLocked && Time.timeScale > 0.99f && kb != null && kb.hKey.wasPressedThisFrame) OpenPopup(toast);
    }

    /// <summary>알림의 설명을 팝업으로 연다(H). 테스트에서도 부른다.</summary>
    public void OpenPopup(Tutorial.Entry entry)
    {
        open = entry;
        toast = null;
        page = 0;
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
        if (fromMenu) { open = null; fromMenu = false; return; } // 메뉴로 돌아간다(시간은 메뉴가 계속 멈춰 둔다)
        open = null;
        Time.timeScale = 1f; // 정상 속도에서만 열린다
        StoryState.MenuOpen = false;
        StoryState.MenuClosedFrame = Time.frameCount; // 닫은 Esc로 일시정지가 바로 열리지 않게
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
        if (open == null && (toast == null || StoryState.CutsceneActive || PauseMenu.IsOpen)) return; // 그릴 것이 없으면 바로 끝(일시정지 화면 위에는 알림을 띄우지 않는다)
        float s = UIText.Scale;
        BuildStyles(s);
        GUI.depth = -10; // HUD 위에
        // 배치(Layout) 단계는 그리는 순서(GUI.depth)만 정하고 바로 끝낸다 — 이 단계를 아예 끄면(useGUILayout=false) 순서가 무시되고 먼저 생긴 순으로 굳었다
        if (Event.current.type == EventType.Layout) return;
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
        Label(new Rect(x, r.y + 8 * s, w - (x - r.x), 28 * s), e.category, Mid(tiny, TextAnchor.MiddleLeft), Tint(Dim, alpha));
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
        Label(r, glyph, glyphStyle, Tint(Paper, alpha));
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
        Label(close, "✕", closeStyle, Color.white);
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
    GUIStyle Mid(GUIStyle style, TextAnchor anchor)
    {
        // 매 OnGUI마다 새 스타일을 만들지 않게 한 번 만든 것을 다시 쓴다(크기가 바뀌면 BuildStyles가 비운다)
        if (!midStyles.TryGetValue((style, anchor), out var mid))
            midStyles[(style, anchor)] = mid = new GUIStyle(style) { alignment = anchor, clipping = TextClipping.Overflow };
        return mid;
    }

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
        glyphStyle = new GUIStyle(centre) { fontSize = Mathf.RoundToInt(30 * s), alignment = TextAnchor.MiddleCenter };
        closeStyle = new GUIStyle(centre) { fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleCenter };
        midStyles.Clear();
    }

    static Color Tint(Color c, float a) => new(c.r, c.g, c.b, c.a * a);

    static void Label(Rect r, string text, GUIStyle style, Color color, bool shadow = true)
    {
        if (!shadow) { style.normal.textColor = color; UIText.Label(r, text, style); return; }
        // 그림자 + 글씨(강조 색 태그는 그대로 두고, 그림자는 색 태그를 지운 글씨로)
        // 색 태그를 지운 글씨는 한 번만 만든다(정규식을 매 프레임 돌리지 않게)
        if (!shadowTexts.TryGetValue(text, out var shadowText))
        {
            shadowText = text.IndexOf('<') < 0 ? text : System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
            if (shadowTexts.Count > 256) shadowTexts.Clear();
            shadowTexts[text] = shadowText;
        }
        UIText.Shadowed(r, text, style, new Color(0f, 0f, 0f, Shadow.a * color.a), color, 2f, shadowText); // 칸을 넘치면 글자를 줄인다
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
