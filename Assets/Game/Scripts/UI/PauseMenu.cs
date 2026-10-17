using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

/// <summary>
/// Esc(패드 Start) 일시정지 메뉴: 계속하기 · 설정(탭: 소리 · 화면 · 조작) · 조작법 · 튜토리얼 · 타이틀로 · 게임 종료.
///  · 열면 게임 시간과 소리가 멈춘다. 컷신 중에도 열 수 있다(연출의 기다림·자막은 StoryState.Now라 함께 멈춘다).
///  · 닫으면 열기 전 시간 속도로 돌아간다(연출 슬로모션 중이었으면 그대로 이어진다).
///  · 인벤토리·가게·설명 팝업이 열려 있으면 Esc는 그 창을 닫는다. 되감기·사망 화면 중에는 열지 않는다.
/// 모든 장면에 하나만 있다(처음 장면을 불러올 때 스스로 만든다).
/// </summary>
[DefaultExecutionOrder(-1000)] // 이야기 시계를 매 프레임 가장 먼저 진행한다
public class PauseMenu : MonoBehaviour
{
    enum Page { Main, Options, Controls, Tutorials, Confirm }

    // 색은 공통 꾸밈(UiTheme)을 따른다
    static Color Gold => UiTheme.Amber;
    static Color Paper => UiTheme.Paper;
    static Color Dim => UiTheme.Dim;

    static readonly string[] MainItems = { "계속하기", "설정", "조작법", "튜토리얼", "타이틀로", "게임 종료" };
    const int ControlsItem = 2, TutorialsItem = 3, TitleItem = 4, QuitItem = 5;
    static Tutorial.Entry[] tutorialList;
    static Tutorial.Entry[] tutorials => tutorialList ??= System.Linq.Enumerable.ToArray(Tutorial.All); // 튜토리얼 목록(분류 순) + 맨 끝 "뒤로"

    // 조작법(키, 하는 일) — 화면 아래에 늘 띄우던 안내를 여기로 옮겼다
    static readonly (string key, string what)[] Controls =
    {
        ("W A S D", "이동"), ("마우스", "시점"), ("Shift", "달리기"), ("Space", "점프"),
        ("좌클릭", "공격 · 완벽 회피 뒤 반격"), ("우클릭", "구르기 · 완벽 회피"),
        ("Q", "몰래 제압 (적 등 뒤에서)"), ("F", "상호작용"),
        ("R (길게)", "되감기 (1회 = 5초)"), ("I", "인벤토리 (테이프 끼우기)"),
        ("2", "다음 테이프로 교체"), ("1", "회복약"), ("H", "새 설명 알림 열기"), ("Esc", "일시정지 · 튜토리얼"),
    };
    static readonly string[] ConfirmItems = { "아니요", "예" };
    public const string TitleScene = "Title";

    // 설정 탭(2026-10-14): 쪽마다 줄 몇 개 + 기본값 + 뒤로. 탭은 Q/E · Tab · 패드 LB/RB · 마우스로 넘긴다
    enum Tab { Sound, Display, Controls }
    static readonly string[] TabNames = { "소리", "화면", "조작" };
    static readonly string[][] RowNames =
    {
        GameSettings.Names,
        new[] { "화면 모드", "해상도", "화질", "프레임 제한" },
        new[] { "마우스 감도", "상하 반전" },
    };
    const int MaxRows = 4; // 가장 줄이 많은 탭(화면)
    static readonly string[] OnOff = { "끔", "켬" };
    Tab tab;
    int RowCount => RowNames[(int)tab].Length;
    int OptionCount => RowCount + 2; // + 기본값 + 뒤로
    bool IsSlider(int row) => row < RowCount && (tab == Tab.Sound || (tab == Tab.Controls && row == 0));

    static PauseMenu instance;

    /// <summary>일시정지 메뉴가 열려 있다</summary>
    public static bool IsOpen => instance && instance.open;

    /// <summary>타이틀 화면처럼 Esc로 일시정지를 열면 안 되는 곳에서 켠다</summary>
    public static bool Suppressed;

    /// <summary>옵션 창만 바로 연다(타이틀 화면). 게임 시간·소리는 건드리지 않고, 뒤로 가면 그냥 닫힌다</summary>
    public static void ShowOptions()
    {
        if (!instance) Create();
        var m = instance;
        if (m.open) return;
        m.open = true;
        m.standalone = true;
        m.page = Page.Options;
        m.tab = Tab.Sound;
        m.selected = 0;
        m.dragging = -1;
        StoryState.MenuOpen = true;
    }

    /// <summary>시험용: 지금 보이는 쪽(Main·Options·Confirm, 닫혀 있으면 null)과 고른 줄</summary>
    public static string CurrentPage => IsOpen ? instance.page.ToString() : null;
    public static int SelectedIndex => instance ? instance.selected : -1;

    /// <summary>Esc: 인벤토리·가게 같은 창을 닫는 "뒤로" 입력(그 창들이 함께 쓴다)</summary>
    public static bool BackPressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

    bool open, closeNextFrame, standalone;
    bool confirmQuit; // 확인 창: true = 게임 종료, false = 타이틀로
    Page page;
    int selected;
    float previousScale = 1f;
    CursorLockMode previousLock;
    bool previousVisible;
    Walkman walkman;
    float repeatAt, lastPreview = -1f;
    int dragging = -1;
    Vector2 lastMouse;
    // 싸우다 연타한 키·클릭이 메뉴를 고르지 않게: 쪽이 바뀐 직후 잠깐은 결정을 받지 않고,
    // 마우스 클릭은 메뉴가 열린 뒤 마우스를 실제로 움직였을 때만 받는다(공격 클릭이 '타이틀로'·'예'를 누르던 문제)
    const float ActivateDelay = 0.25f;
    float pageSince;
    bool mouseArmed, syncMouse;
    GUIStyle title, item, itemDim, itemLeft, small, smallLeft, percent, eyebrow, arrow;
    float styleScale = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance) return;
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PauseMenu>();
    }


    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        walkman = null;
        if (!open) return;
        // 열린 채로 장면이 바뀌었다(정상 흐름에서는 없다): 새 장면은 정상 속도로 시작한다
        open = closeNextFrame = false;
        AudioListener.pause = false;
        Time.timeScale = 1f;
    }

    void OnDestroy()
    {
        if (open) { AudioListener.pause = false; Time.timeScale = previousScale; StoryState.MenuOpen = false; }
        if (instance == this) instance = null;
    }

    // ---------- 입력 ----------

    void Update()
    {
        StoryState.Tick();
        // "계속하기"를 고른 입력(Enter·클릭)이 같은 프레임에 게임 입력으로 새지 않게 한 프레임 뒤에 닫는다
        if (closeNextFrame) { closeNextFrame = false; Close(); return; }

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool toggle = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame);
        if (!open)
        {
            if (toggle && CanOpen()) Open();
            return;
        }

        if (TutorialUI.MenuPopupOpen) return; // 설명 팝업이 위에 떠 있다: Esc·H·A/D는 팝업이 받는다
        if (toggle || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Back(); return; }

        int count = page == Page.Main ? MainItems.Length : page == Page.Options ? OptionCount : page == Page.Controls ? 1
                  : page == Page.Tutorials ? tutorials.Length + 1 : ConfirmItems.Length;
        if (Pressed(kb?.wKey, kb?.upArrowKey, pad?.dpad.up)) Select((selected - 1 + count) % count);
        if (Pressed(kb?.sKey, kb?.downArrowKey, pad?.dpad.down)) Select((selected + 1) % count);
        if (page == Page.Confirm && Pressed(kb?.aKey, kb?.leftArrowKey, pad?.dpad.left, kb?.dKey, kb?.rightArrowKey, pad?.dpad.right))
            Select(1 - selected); // 예/아니요는 가로로 놓여 있다

        if (page == Page.Options)
        {
            // 탭 넘기기
            int tabDir = Pressed(kb?.qKey, pad?.leftShoulder) ? -1 : Pressed(kb?.eKey, kb?.tabKey, pad?.rightShoulder) ? 1 : 0;
            if (tabDir != 0) SwitchTab((Tab)(((int)tab + tabDir + TabNames.Length) % TabNames.Length));
            if (selected < RowCount)
            {
                int dir = Pressed(kb?.aKey, kb?.leftArrowKey, pad?.dpad.left) ? -1 : Pressed(kb?.dKey, kb?.rightArrowKey, pad?.dpad.right) ? 1 : 0;
                if (IsSlider(selected))
                {
                    // 밀대 좌우: 누르고 있으면 잠시 뒤부터 빠르게 반복한다
                    if (dir != 0) repeatAt = Time.unscaledTime + 0.35f;
                    else if (Time.unscaledTime >= repeatAt)
                    {
                        dir = Held(kb?.aKey, kb?.leftArrowKey, pad?.dpad.left) ? -1 : Held(kb?.dKey, kb?.rightArrowKey, pad?.dpad.right) ? 1 : 0;
                        if (dir != 0) repeatAt = Time.unscaledTime + 0.06f;
                    }
                    if (dir != 0) NudgeRow(selected, dir);
                }
                else if (dir != 0) Cycle(selected, dir); // 고르기 줄: 한 번 누를 때 한 칸
            }
        }

        // 결정은 Enter(패드 A)만: Space(회피)·F(상호작용)는 싸우며 연타하는 키라 메뉴를 잘못 고른다
        if (Pressed(kb?.enterKey, kb?.numpadEnterKey, pad?.buttonSouth) && Time.unscaledTime - pageSince >= ActivateDelay) Activate(selected);
    }

    static bool Pressed(params ButtonControl[] buttons)
    {
        foreach (var b in buttons) if (b != null && b.wasPressedThisFrame) return true;
        return false;
    }

    static bool Held(params ButtonControl[] buttons)
    {
        foreach (var b in buttons) if (b != null && b.isPressed) return true;
        return false;
    }

    bool CanOpen()
    {
        if (Suppressed) return false;                                    // 타이틀 화면
        if (SceneLoader.Busy) return false;                              // 장면을 불러오는 중(검은 화면)
        if (StoryState.MenuClosedFrame == Time.frameCount) return false; // 같은 Esc로 다른 창을 방금 닫았다
        if (StoryState.Paused) return false;                             // 인벤토리·가게·설명 팝업이 열려 있다(Esc는 그 창을 닫는다)
        if (TimeRewind.PlayingBack) return false;
        if (!walkman) walkman = FindAnyObjectByType<Walkman>();
        if (walkman && (walkman.Rewinding || walkman.PromptOpen)) return false; // 되감기·사망 화면은 그 자체가 멈춘 화면이다
        return true;
    }

    void Open()
    {
        open = true;
        page = Page.Main;
        selected = 0;
        dragging = -1;
        pageSince = Time.unscaledTime;
        mouseArmed = false;
        syncMouse = true;
        // 타격 멈춤 중이었으면 정상 속도로, 연출 슬로모션(저격수 등장 등) 중이었으면 그 속도로 돌아간다
        previousScale = HitFeel.ReleaseTime() ? 1f : Time.timeScale;
        Time.timeScale = 0f;
        StoryState.MenuOpen = true;
        AudioListener.pause = true; // 비·음악·엔진 소리도 멈춘다(메뉴 효과음은 따로 들린다)
        previousLock = Cursor.lockState;
        previousVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UiSound();
    }

    void Close()
    {
        if (!open) return;
        open = false;
        dragging = -1;
        if (standalone)
        {
            // 타이틀의 옵션 창: 시간·소리·커서는 처음부터 건드리지 않았다
            standalone = false;
            StoryState.MenuOpen = false;
            StoryState.MenuClosedFrame = Time.frameCount;
            GameSettings.Save();
            return;
        }
        Time.timeScale = previousScale;
        StoryState.MenuOpen = false;
        StoryState.MenuClosedFrame = Time.frameCount;
        AudioListener.pause = false;
        Cursor.lockState = previousLock;
        Cursor.visible = previousVisible;
        GameSettings.Save();
    }

    void Back()
    {
        UiSound();
        if (page == Page.Main || (standalone && page == Page.Options)) closeNextFrame = true;
        else GoTo(Page.Main, page == Page.Options ? 1 : page == Page.Controls ? ControlsItem : page == Page.Tutorials ? TutorialsItem : confirmQuit ? QuitItem : TitleItem);
    }

    void GoTo(Page to, int select)
    {
        page = to;
        selected = select;
        dragging = -1;
        pageSince = Time.unscaledTime;
        if (to == Page.Main) GameSettings.Save();
    }

    void Select(int index)
    {
        if (index == selected) return;
        selected = index;
        UiSound();
    }

    void Activate(int index)
    {
        UiSound();
        switch (page)
        {
            case Page.Main:
                if (index == 0) closeNextFrame = true;
                else if (index == 1) { tab = Tab.Sound; GoTo(Page.Options, 0); } // 설정은 늘 소리 탭부터
                else if (index == ControlsItem) GoTo(Page.Controls, 0);
                else if (index == TutorialsItem) GoTo(Page.Tutorials, 0);
                else { confirmQuit = index == QuitItem; GoTo(Page.Confirm, 0); }
                break;
            case Page.Controls:
                GoTo(Page.Main, ControlsItem);
                break;
            case Page.Tutorials:
                if (index >= tutorials.Length) GoTo(Page.Main, TutorialsItem);
                else TutorialUI.Ensure().OpenFromMenu(tutorials[index]);
                break;
            case Page.Options:
                if (index < RowCount) { if (!IsSlider(index)) Cycle(index, 1); } // 고르기 줄은 Enter로도 다음 값
                else if (index == RowCount) ResetTab();
                else { if (standalone) closeNextFrame = true; else GoTo(Page.Main, 1); }
                break;
            case Page.Confirm:
                if (index == 0) GoTo(Page.Main, confirmQuit ? QuitItem : TitleItem);
                else if (confirmQuit) Quit();
                else ToTitle();
                break;
        }
    }

    void SwitchTab(Tab to)
    {
        if (to == tab) return;
        tab = to;
        selected = 0;
        dragging = -1;
        UiSound();
    }

    /// <summary>밀대 줄 값(0~1)과 오른쪽에 쓰는 글자</summary>
    (float v, string text) SliderValue(int row)
    {
        if (tab == Tab.Sound)
        {
            float v = GameSettings.Get((GameSettings.Channel)row);
            return (v, $"{Mathf.RoundToInt(v * 100f)}%");
        }
        float m = GameSettings.MouseSensitivity;
        return ((m - GameSettings.MouseMin) / (GameSettings.MouseMax - GameSettings.MouseMin), m.ToString("0.0"));
    }

    void SetSlider(int row, float v)
    {
        if (tab == Tab.Sound) GameSettings.Set((GameSettings.Channel)row, Mathf.Clamp01(v));
        else GameSettings.SetMouseSensitivity(Mathf.Lerp(GameSettings.MouseMin, GameSettings.MouseMax, Mathf.Clamp01(v)));
    }

    void NudgeRow(int row, int dir)
    {
        if (tab == Tab.Sound)
        {
            var ch = (GameSettings.Channel)row;
            float before = GameSettings.Get(ch);
            GameSettings.Set(ch, Mathf.Round((before + dir * 0.05f) * 20f) / 20f);
            if (!Mathf.Approximately(before, GameSettings.Get(ch))) Preview(ch, false);
        }
        else GameSettings.SetMouseSensitivity(GameSettings.MouseSensitivity + dir * 0.1f);
    }

    /// <summary>고르기 줄의 지금 값(글자)</summary>
    string ChoiceValue(int row)
    {
        if (tab == Tab.Controls) return OnOff[GameSettings.InvertY ? 1 : 0];
        return row switch
        {
            0 => GameSettings.WindowModeNames[GameSettings.WindowMode],
            1 => GameSettings.ResolutionName(GameSettings.ResolutionIndex),
            2 => GameSettings.QualityNames[GameSettings.Quality],
            _ => GameSettings.FrameNames[GameSettings.Frame],
        };
    }

    /// <summary>고르기 줄을 한 칸 넘긴다(끝에서는 처음으로 돈다)</summary>
    void Cycle(int row, int dir)
    {
        UiSound();
        static int Wrap(int i, int n) => ((i % n) + n) % n;
        if (tab == Tab.Controls) { GameSettings.SetInvertY(!GameSettings.InvertY); return; }
        switch (row)
        {
            case 0: GameSettings.SetWindowMode(Wrap(GameSettings.WindowMode + dir, GameSettings.WindowModeNames.Length)); break;
            case 1: GameSettings.SetResolution(Wrap(GameSettings.ResolutionIndex + dir, Mathf.Max(1, GameSettings.Resolutions.Count))); break;
            case 2: GameSettings.SetQuality(Wrap(GameSettings.Quality + dir, GameSettings.QualityNames.Length)); break;
            default: GameSettings.SetFrame(Wrap(GameSettings.Frame + dir, GameSettings.FrameNames.Length)); break;
        }
    }

    void ResetTab()
    {
        if (tab == Tab.Sound) { GameSettings.ResetDefaults(); Preview(GameSettings.Channel.Sfx, true); }
        else if (tab == Tab.Display) GameSettings.ResetDisplay();
        else GameSettings.ResetControls();
    }

    /// <summary>바꾼 크기를 바로 들어 보게 짧은 소리를 낸다(전체·효과음). 음악은 멈춰 있어 닫으면 들린다</summary>
    void Preview(GameSettings.Channel channel, bool force)
    {
        if (!force && channel != GameSettings.Channel.Master && channel != GameSettings.Channel.Sfx) return;
        if (!force && Time.unscaledTime - lastPreview < 0.09f) return;
        lastPreview = Time.unscaledTime;
        var clip = ProceduralSfx.ButtonClick;
        if (clip) ProceduralSfx.PlayAt(clip, Vector3.zero, 0.9f, 0f);
    }

    void UiSound()
    {
        var clip = ProceduralSfx.ButtonClick;
        if (clip) ProceduralSfx.PlayAt(clip, Vector3.zero, 0.35f, 0f, 1.25f);
    }

    void ToTitle()
    {
        Close();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneLoader.Load(TitleScene);
    }

    static void Quit()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- 그리기 ----------

    void OnGUI()
    {
        if (!open || TutorialUI.MenuPopupOpen) return; // 설명 팝업을 보는 동안은 그 팝업만
        float s = UIText.Scale;
        UiTheme.Begin();
        BuildStyles(s);
        GUI.depth = -30; // HUD·설명 알림 위에
        // 배치(Layout) 단계는 그리는 순서(GUI.depth)만 정하고 바로 끝낸다 — 이 단계를 아예 끄면(useGUILayout=false) 순서가 무시되고 먼저 생긴 순으로 굳었다
        if (Event.current.type == EventType.Layout) return;
        var e = Event.current;
        if (syncMouse && e.type == EventType.Repaint) { lastMouse = e.mousePosition; syncMouse = false; } // 열릴 때 마우스 자리를 기준으로
        bool mouseMoved = e.type == EventType.Repaint && (e.mousePosition - lastMouse).sqrMagnitude > 1f;
        if (e.type == EventType.Repaint) lastMouse = e.mousePosition;
        if (mouseMoved) mouseArmed = true;

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.01f, 0.01f, 0.02f, 0.68f));
        switch (page)
        {
            case Page.Main: DrawMain(s, mouseMoved); break;
            case Page.Options: DrawOptions(s, mouseMoved); break;
            case Page.Controls: DrawControls(s, mouseMoved); break;
            case Page.Tutorials: DrawTutorials(s, mouseMoved); break;
            case Page.Confirm: DrawConfirm(s, mouseMoved); break;
        }
    }

    static string Eyebrow(string heading) => heading switch
    {
        "일시정지" => "P A U S E D", "설정" => "S E T T I N G S", "조작법" => "C O N T R O L S", "튜토리얼" => "T U T O R I A L S", _ => "C O N F I R M",
    };

    /// <summary>고른 줄 글씨: 호박빛 단추 위에서는 금색이 묻혀 따뜻한 흰색으로</summary>
    static Color Lit => UiTheme.HasButtonArt ? new Color(1f, 0.96f, 0.88f) : Gold;

    Rect Window(float w, float h, float s, string heading)
    {
        var win = new Rect((Screen.width - w * s) / 2f, (Screen.height - h * s) / 2f, w * s, h * s);
        UiTheme.Window(win, s);
        // 작은 영문 표제(호박색) + 한글 제목 + 가는 선
        Label(new Rect(win.x, win.y + 22 * s, win.width, 24 * s), Eyebrow(heading), eyebrow, Gold);
        Label(new Rect(win.x, win.y + 44 * s, win.width, 46 * s), heading, title, Paper); // 제목 뒤 띠(다이모 라벨)는 글씨를 가려 뺐다(2026-10-17)
        UiTheme.Hairline(win.x + 50 * s, win.y + 96 * s, win.width - 100 * s, s);
        return win;
    }

    void DrawMain(float s, bool mouseMoved)
    {
        var win = Window(520, 720, s, "일시정지");
        for (int i = 0; i < MainItems.Length; i++)
        {
            var r = new Rect(win.x + 60 * s, win.y + 128 * s + i * 80 * s, win.width - 120 * s, 62 * s);
            Row(r, i, mouseMoved, s);
            Label(r, MainItems[i], item, i == selected ? Lit : Paper);
        }
        Label(new Rect(win.x, win.yMax - 56 * s, win.width, 30 * s), "W/S 선택   Enter 결정   Esc 계속하기", small, Dim);
    }

    void DrawOptions(float s, bool mouseMoved)
    {
        var win = Window(860, 680, s, "설정");
        var e = Event.current;

        // 탭: [소리] [화면] [조작]
        float tabW = (win.width - 100 * s) / TabNames.Length;
        for (int t = 0; t < TabNames.Length; t++)
        {
            var tr = new Rect(win.x + 50 * s + t * tabW, win.y + 104 * s, tabW, 50 * s);
            bool on = (int)tab == t;
            if (on) UiTheme.Fill(new Rect(tr.center.x - 28 * s, tr.yMax - 4 * s, 56 * s, 3 * s), Gold);
            Label(tr, TabNames[t], on ? item : itemDim, on ? Gold : Dim);
            if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition) && mouseArmed)
            {
                SwitchTab((Tab)t);
                e.Use();
            }
        }

        var names = RowNames[(int)tab];
        for (int i = 0; i < names.Length; i++)
        {
            var r = new Rect(win.x + 50 * s, win.y + 176 * s + i * 72 * s, win.width - 100 * s, 60 * s);
            Row(r, i, mouseMoved, s, activateOnClick: false);
            Label(new Rect(r.x + 28 * s, r.y, 260 * s, r.height), names[i], itemLeft, i == selected ? Lit : Paper);
            var area = new Rect(r.x + 300 * s, r.y, 420 * s, r.height); // 값 칸(밀대 · ◀ 값 ▶)

            if (IsSlider(i))
            {
                // 밀대: 클릭·끌기로 바꾼다
                var bar = new Rect(area.x, r.y + r.height / 2f - 3 * s, 300 * s, 6 * s);
                var hit = new Rect(bar.x - 10 * s, r.y, bar.width + 20 * s, r.height);
                var (v, text) = SliderValue(i);
                UiTheme.Panel(bar, UiTheme.Track, s);
                if (v > 0.001f) UiTheme.Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * v), bar.height), i == selected ? Gold : new Color(0.80f, 0.66f, 0.40f, 0.85f), s);
                float kr = 9 * s;
                UiTheme.Panel(new Rect(bar.x + bar.width * v - kr, bar.center.y - kr, kr * 2f, kr * 2f), Paper, s);
                Label(new Rect(bar.xMax + 16 * s, r.y, 100 * s, r.height), text, percent, i == selected ? Lit : Paper);
                if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
                {
                    dragging = i;
                    selected = i;
                    SetSlider(i, (e.mousePosition.x - bar.x) / bar.width);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && dragging == i)
                {
                    SetSlider(i, (e.mousePosition.x - bar.x) / bar.width);
                    e.Use();
                }
            }
            else
            {
                // 고르기: ◀ 값 ▶ — 왼쪽 반을 누르면 앞으로, 오른쪽 반을 누르면 다음으로
                var left = new Rect(area.x, r.y, 44 * s, r.height);
                var right = new Rect(area.xMax - 44 * s, r.y, 44 * s, r.height);
                Color c = i == selected ? Lit : Paper;
                Label(left, "‹", arrow, i == selected ? Lit : Dim);
                Label(new Rect(left.xMax, r.y, right.x - left.xMax, r.height), ChoiceValue(i), item, c);
                Label(right, "›", arrow, i == selected ? Lit : Dim);
                if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition) && mouseArmed && Time.unscaledTime - pageSince >= ActivateDelay)
                {
                    selected = i;
                    Cycle(i, e.mousePosition.x < area.center.x ? -1 : 1);
                    e.Use();
                }
            }
        }
        if (e.type == EventType.MouseUp && dragging >= 0)
        {
            if (tab == Tab.Sound) Preview((GameSettings.Channel)dragging, true);
            dragging = -1;
            e.Use();
        }

        // 기본값 · 뒤로(탭마다 줄 수가 달라도 같은 자리에)
        float by = win.y + 176 * s + MaxRows * 72 * s + 16 * s;
        var reset = new Rect(win.x + 50 * s, by, (win.width - 120 * s) / 2f, 58 * s);
        var back = new Rect(reset.xMax + 20 * s, by, reset.width, reset.height);
        Row(reset, RowCount, mouseMoved, s);
        Label(reset, "기본값으로", item, selected == RowCount ? Lit : Paper);
        Row(back, RowCount + 1, mouseMoved, s);
        Label(back, "뒤로", item, selected == RowCount + 1 ? Lit : Paper);

        Label(new Rect(win.x, win.yMax - 52 * s, win.width, 30 * s), "Q/E 탭   W/S 선택   A/D 조절   Esc 뒤로", small, Dim);
    }

    void DrawControls(float s, bool mouseMoved)
    {
        int half = (Controls.Length + 1) / 2;
        var win = Window(1000, 154 + half * 52 + 150, s, "조작법");
        float colW = (win.width - 120 * s) / 2f;
        for (int i = 0; i < Controls.Length; i++)
        {
            int col = i / half, row = i % half;
            var r = new Rect(win.x + 50 * s + col * (colW + 20 * s), win.y + 118 * s + row * 52 * s, colW, 46 * s);
            UiTheme.Panel(r, UiTheme.InkSoft, s);
            Label(new Rect(r.x + 18 * s, r.y, 150 * s, r.height), Controls[i].key, itemLeft, Gold);
            Label(new Rect(r.x + 170 * s, r.y, r.width - 180 * s, r.height), Controls[i].what, smallLeft, Paper);
        }
        var back = new Rect(win.x + (win.width - 260 * s) / 2f, win.yMax - 112 * s, 260 * s, 56 * s);
        Row(back, 0, mouseMoved, s);
        Label(back, "뒤로", item, Gold);
        Label(new Rect(win.x, win.yMax - 48 * s, win.width, 30 * s), "Enter 또는 Esc 뒤로", small, Dim);
    }

    /// <summary>튜토리얼 목록: 두 줄로 나눠 놓고, 고르면 설명 팝업이 열린다(닫으면 이 목록으로)</summary>
    void DrawTutorials(float s, bool mouseMoved)
    {
        int half = (tutorials.Length + 1) / 2;
        var win = Window(1000, 154 + half * 58 + 150, s, "튜토리얼");
        float colW = (win.width - 120 * s) / 2f;
        for (int i = 0; i < tutorials.Length; i++)
        {
            int col = i / half, row = i % half;
            var r = new Rect(win.x + 50 * s + col * (colW + 20 * s), win.y + 118 * s + row * 58 * s, colW, 50 * s);
            Row(r, i, mouseMoved, s);
            Label(new Rect(r.x + 20 * s, r.y, 170 * s, r.height), tutorials[i].category, smallLeft, Dim);
            Label(new Rect(r.x + 190 * s, r.y, r.width - 200 * s, r.height), tutorials[i].title, itemLeft, i == selected ? Lit : Paper);
        }
        var back = new Rect(win.x + (win.width - 260 * s) / 2f, win.yMax - 112 * s, 260 * s, 56 * s);
        Row(back, tutorials.Length, mouseMoved, s);
        Label(back, "뒤로", item, selected == tutorials.Length ? Lit : Paper);
        Label(new Rect(win.x, win.yMax - 48 * s, win.width, 30 * s), "W/S 선택   Enter 보기   Esc 뒤로", small, Dim);
    }

    void DrawConfirm(float s, bool mouseMoved)
    {
        var win = Window(640, 400, s, confirmQuit ? "게임을 종료할까요?" : "타이틀 화면으로 돌아갈까요?");
        Label(new Rect(win.x, win.y + 110 * s, win.width, 60 * s), confirmQuit
            ? "마지막 저장 지점까지 저장됨"
            : "마지막 저장 지점 이후의 진행은 사라집니다", small, Dim);
        for (int i = 0; i < ConfirmItems.Length; i++)
        {
            float w = (win.width - 140 * s) / 2f;
            var r = new Rect(win.x + 50 * s + i * (w + 40 * s), win.y + 210 * s, w, 62 * s);
            Row(r, i, mouseMoved, s);
            Label(r, ConfirmItems[i], item, i == selected ? Lit : Paper);
        }
        Label(new Rect(win.x, win.yMax - 52 * s, win.width, 30 * s), "A/D 선택   Enter 결정   Esc 뒤로", small, Dim);
    }

    /// <summary>한 줄 바탕: 고른 줄은 밝게 + 왼쪽 금색 막대. 마우스를 올리면 고르고, 누르면 결정한다</summary>
    void Row(Rect r, int index, bool mouseMoved, float s, bool activateOnClick = true)
    {
        var e = Event.current;
        if (mouseMoved && r.Contains(e.mousePosition) && dragging < 0) Select(index);
        UiTheme.RowBg(r, index == selected, s);
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            if (!mouseArmed || Time.unscaledTime - pageSince < ActivateDelay) { e.Use(); return; } // 움직이지 않은 마우스의 클릭(공격 연타)은 무시
            selected = index;
            if (!activateOnClick) return; // 밀대 줄: 고르기만 한다(값은 밀대를 눌러 바꾼다)
            e.Use();
            Activate(index);
        }
    }

    void BuildStyles(float s)
    {
        if (Mathf.Approximately(s, styleScale)) return;
        styleScale = s;
        title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(32 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        item = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24 * s), alignment = TextAnchor.MiddleCenter };
        itemDim = new GUIStyle(item);
        itemLeft = new GUIStyle(item) { alignment = TextAnchor.MiddleLeft };
        eyebrow = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        arrow = new GUIStyle(item) { fontSize = Mathf.RoundToInt(34 * s) };
        smallLeft = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), alignment = TextAnchor.MiddleLeft };
        small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * s), alignment = TextAnchor.MiddleCenter, wordWrap = true };
        percent = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24 * s), alignment = TextAnchor.MiddleRight };
    }

    // 어두운 판 위라 그림자를 넣지 않는다(2026-10-14: 작은 화면에서 1px 그림자가 글자를 두 겹으로 보이게 해 읽기 어려웠다)
    static void Label(Rect r, string text, GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        UIText.Label(r, text, style); // 칸을 넘치면 글자를 줄인다
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
