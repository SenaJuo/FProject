using UnityEngine;

/// <summary>프로토타입용 임시 IMGUI HUD — 레이아웃이 확정되면 UI Toolkit/uGUI로 교체한다.</summary>
public class HUD : MonoBehaviour
{
    // 색은 공통 꾸밈(UiTheme)을 따른다(2026-10-14 새 꾸밈)
    static Color Paper => UiTheme.Paper;
    static readonly Color Ink = new(0f, 0f, 0f, 0.6f);   // 글자 그림자(3D 화면 위 글자만)
    static Color Accent => UiTheme.Tape;
    static Color Gold => UiTheme.Amber;
    static readonly Color Teal = new(0.35f, 0.75f, 0.80f);
    static readonly Color WindowBg = new(0.06f, 0.06f, 0.07f, 0.97f);
    static readonly Color TitleBg = new(0.11f, 0.10f, 0.09f, 1f);
    static readonly Color Border = new(0.55f, 0.47f, 0.30f, 1f);
    static readonly Color SlotBg = new(0.11f, 0.11f, 0.12f, 1f);
    static readonly Color SlotBorder = new(0.26f, 0.25f, 0.23f, 1f);

    public PlayerController player;

    Health health;
    Walkman walkman;
    Camera cam;
    GUIStyle small, smallCentre, smallRight, smallWrap, normal, normalBold, normalCentre, right, big, title, labelInk, eyebrow, eyebrowRight, keyStyle, tagStyle;
    GUIStyle letterHead, letterText;
    float styleScale = -1f;

    // 매 OnGUI마다 같은 글자를 새로 만들지 않게 값이 바뀔 때만 다시 만든다
    string hpText, potionText, objectiveText, subtitleText, subtitleSpeaker, subtitleLine, objectiveStory, objectiveFinal;
    int hpCurrent = -1, hpMax = -1, potionCount = -1, objectiveCleared = -2, objectiveRemaining = -2;
    ZoneGate objectiveGate;
    bool objectiveFighting, objectiveValid;


    void Start()
    {
        health = player.GetComponent<Health>();
        walkman = player.GetComponent<Walkman>();
        cam = Camera.main;
    }

    void OnGUI()
    {
        UiTheme.Begin(); // 게임 글꼴(Noto Sans KR)
        float s = UIText.Scale;
        GUI.depth = 10; // 게임 HUD는 맨 뒤(설명 알림·일시정지가 위)
        // 배치(Layout) 단계는 그리는 순서(GUI.depth)만 정하고 바로 끝낸다 — 이 단계를 아예 끄면(useGUILayout=false) 순서가 무시되고 먼저 생긴 순으로 굳었다
        if (Event.current.type == EventType.Layout) return;
        BuildStyles(s);
        var gm = GameManager.Instance;

        // 컷신: 게임 HUD는 모두 숨기고 영화처럼 위아래 검은 띠와 자막만 보인다
        if (StoryState.CutsceneActive)
        {
            float bar = Screen.height * 0.11f;
            Fill(new Rect(0, 0, Screen.width, bar), Color.black);
            Fill(new Rect(0, Screen.height - bar, Screen.width, bar), Color.black);
            DrawSubtitle(s, Screen.height - bar + 18 * s);
            if (!string.IsNullOrEmpty(StoryState.Prompt)) DrawPrompt(StoryState.Prompt, s); // 컷신 속 선택(종장 R/F)도 보이게
            DrawStoryOverlay(s);
            return;
        }

        bool menu = walkman && walkman.InventoryOpen || TapeShop.Current;
        // 창(인벤토리 · 가게)이 열려 있으면 위쪽 상태 · 목표는 숨긴다: 작은 게임 창에서 창 제목과 겹쳤다(2026-10-17)
        if (!menu) DrawTopHud(gm, s);

        DrawEnemyHealth(s);
        DrawPickups(s);
        DrawSuspicion(s);
        DrawRest(gm, menu, s);
    }

    void DrawTopHud(GameManager gm, float s)
    {
        // 플레이어 상태(좌상단): 작은 영문 표제 + 숫자 + 둥근 막대(낮으면 붉게) · 회복약은 키 모양 [1]과 함께
        float hx = 40 * s, hy = 32 * s, hw = 320 * s;
        float hpRatio = (float)health.Current / health.Max;
        ShadowLabel(new Rect(hx, hy, 200 * s, 22 * s), "H E A L T H", eyebrow, Gold);
        if (health.Current != hpCurrent || health.Max != hpMax) { hpCurrent = health.Current; hpMax = health.Max; hpText = $"{hpCurrent} / {hpMax}"; }
        ShadowLabel(new Rect(hx, hy - 4 * s, hw, 28 * s), hpText, smallRight);
        UiTheme.Bar(new Rect(hx, hy + 28 * s, hw, 8 * s), hpRatio, hpRatio < 0.3f ? Accent : Paper, s);
        if (walkman && (walkman.potions > 0 || !StoryState.RewindLocked) && !StoryState.CutsceneActive)
        {
            if (walkman.potions != potionCount) { potionCount = walkman.potions; potionText = $"회복약 {potionCount}개"; }
            var cap = UiTheme.KeyCap(new Vector2(hx, hy + 48 * s), 24 * s, "1", keyStyle, s);
            ShadowLabel(new Rect(cap.xMax + 10 * s, cap.y - 2 * s, 300 * s, 28 * s), potionText, small);
        } // 프롤로그 첫 회차(아직 모름)에는 숨긴다

        // 목표 (우상단) + 웨이포인트
        if (gm && !gm.StageCleared)
        {
            var gate = gm.CurrentGate;
            // 이야기 목표가 있으면 그것이 우선이다(프롤로그처럼 관문이 없는 구간)
            bool fighting = gm.CurrentZone && !gm.CurrentZone.IsCleared;
            string objective = ObjectiveText(gm, gate, fighting);
            ShadowLabel(new Rect(Screen.width - 740 * s, 32 * s, 700 * s, 22 * s), "O B J E C T I V E", eyebrowRight, Gold);
            ShadowLabel(new Rect(Screen.width - 740 * s, 56 * s, 700 * s, 80 * s), objective, right);

            Vector3? waypoint = StoryState.Objective != null
                ? (StoryState.Waypoint ? StoryState.Waypoint.position : null)
                : WaypointTarget(gm, gate);
            if (!fighting && cam && waypoint is Vector3 target) DrawWaypoint(target, s);
        }
    }

    void DrawRest(GameManager gm, bool menu, float s)
    {
        if (walkman && !menu && !StoryState.WalkmanStowed && StoryState.LetterText == null) DrawWalkmanWidget(s); // 프롤로그에서는 아직 착용 전이라 숨긴다 · 편지를 펼친 동안도(편지 밑에 겹쳤다)
        if (walkman && walkman.Rewinding) DrawPlayback(s);
        if (walkman && walkman.PromptOpen) DrawRewindPrompt(s);
        if (walkman && walkman.ChoiceOpen && walkman.NextManualRewindTape != null)
        {
            var g = walkman.NextManualRewindTape.Genre; // 능력은 지금 횟수를 쓸 테이프의 장르
            DrawChoice("되감기", Same(ref choiceSlot, (long)g, null) ? choiceSlot.text : choiceSlot.text = $"{TapeAbility.Name(g)} ({TapeAbility.Seconds(g):0}초)", s);
        }
        DrawAbility(s);
        if (walkman && walkman.InventoryOpen) DrawInventory(s);
        if (TapeShop.Current) DrawShop(TapeShop.Current, s);
        else if (TapeShop.Nearby && !player.IsDead && !menu)
            KeyPrompt(Screen.height * RowInteract, "F", TapeShop.Nearby.shopName, s);
        else if (StoryState.Nearby && !player.IsDead && !menu)
            KeyPrompt(Screen.height * RowInteract, "F", StoryState.Nearby.prompt, s);
        if (Takedown.Target && !player.IsDead && !menu)
            KeyPrompt(Screen.height * RowTakedown, "Q", "몰래 제압", s);

        // 중앙 메시지
        bool rewindUi = walkman && (walkman.PromptOpen || walkman.Rewinding);
        string centre = rewindUi ? null : player.IsDead ? "YOU DIED" : gm && gm.MessageVisible ? gm.Message : null;
        if (centre != null) ShadowLabel(new Rect(0, Screen.height * 0.12f, Screen.width, 120 * s), centre, big);

        // 조작법은 화면에 늘 띄우지 않는다(Esc 메뉴 → 조작법)
        if (gm) DrawNotices(gm, s);

        // 보스 체력 막대(이름 포함, 화면 아래)가 있으면 자막을 그 위로 올린다(작은 화면에서 보스 이름과 겹쳤다)
        float subY = Screen.height * 0.78f;
        if (bossBarTop > 0f) subY = Mathf.Min(subY, bossBarTop - 104 * s);
        if (!menu && !StoryState.Paused) DrawSubtitle(s, subY); // 가게 · 인벤토리 · 메뉴 위에 겹쳐 그려졌다 → 닫은 뒤에 보인다(이야기 시계가 멈춰 있어 대사는 그대로 남는다)
        if (!string.IsNullOrEmpty(StoryState.Prompt) && !(walkman && walkman.Rewinding)) DrawPrompt(StoryState.Prompt, s);
        DrawStoryOverlay(s);
    }

    // 화면 가운데 아래 세로 칸(화면 높이 비율): 위에서부터 Q 제압 · F 상호작용 · 안내 상자(64) · 능력 이름+막대 · 기억의 일격 · 자막(0.78) · 보스 체력
    // 칸끼리 겹치지 않게 한곳에 모았다(전에는 안내 상자 0.6과 F 0.62, 기억의 일격 0.8과 자막 0.78이 겹쳤다)
    const float RowTakedown = 0.535f, RowInteract = 0.572f, RowPrompt = 0.612f, RowAbility = 0.676f, RowMemory = 0.728f;

    bool memoryWasReady, memoryLabel;

    /// <summary>켜진 테이프 능력: 화면 아래 가운데에 이름과 남은 시간 막대(장르 색)</summary>
    void DrawAbility(float s)
    {
        // 기억의 일격: 되감은 직후 3초 — 다음 주먹이 세 배. 글자는 처음 한 번만 띄운다(2026-10-17: 되감을 때마다 떠서 거슬렸다)
        bool memory = PlayerController.MemoryStrikeReady;
        if (memory && !memoryWasReady) memoryLabel = Tutorial.FirstTime("memory_strike_label");
        memoryWasReady = memory;
        if (memory && memoryLabel) ShadowLabel(new Rect(0, Screen.height * RowMemory, Screen.width, 30 * s), "기억의 일격", normalCentre);
        var g = TapeAbility.Current;
        if (g == TapeGenre.None || TapeAbility.Duration <= 0f) return;
        float w = 320 * s;
        var r = new Rect((Screen.width - w) / 2f, Screen.height * RowAbility + 38 * s, w, 8 * s); // 막대는 이름 글자 아래로 띄운다(글자 밑을 가렸다)
        int tenths = Mathf.CeilToInt(Mathf.Max(0f, TapeAbility.Remaining) * 10f);
        ShadowLabel(new Rect(r.x, r.y - 38 * s, w, 30 * s), (Same(ref abilitySlot, (long)g * 100000 + tenths, null) ? abilitySlot.text : abilitySlot.text = $"♪ {TapeAbility.Name(g)}  {tenths / 10f:0.0}"), normalCentre);
        Bar(r, TapeAbility.Remaining / TapeAbility.Duration, TapeAbility.Tint(g));
    }

    /// <summary>지금 눌러야 할 키 안내: 화면 가운데 아래쪽, 어두운 띠 위에 크게</summary>
    void DrawPrompt(string text, float s)
    {
        float w = 760 * s, h = 64 * s;
        var r = new Rect((Screen.width - w) / 2f, Screen.height * RowPrompt, w, h);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
        UiTheme.Panel(r, new Color(0.04f, 0.04f, 0.05f, 0.78f), s);
        UiTheme.Outline(r, Tint(Gold, 0.35f * pulse));
        UiTheme.Fill(new Rect(r.x, r.y + h * 0.25f, 3 * s, h * 0.5f), Tint(Gold, pulse));
        normalCentre.richText = true;
        PlainLabel(new Rect(r.x, r.y + (h - 34 * s) / 2f, r.width, 34 * s), text, normalCentre, Paper);
    }

    /// <summary>되감기/능력 고르기(2026-10-14 "상자 말고 캐릭터 기준 왼쪽 오른쪽에 글자만"):
    /// 상자 없이 캐릭터 왼쪽에 Q 되감기, 오른쪽에 E 능력. 키 글자만 호박색</summary>
    void DrawChoice(string left, string right, float s)
    {
        float cx = Screen.width / 2f, y = Screen.height * RowPrompt + 14 * s, h = 36 * s, off = 110 * s, gap = 12 * s;
        choiceKey ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        choiceKey.fontSize = normal.fontSize;
        keyMeasure.text = "Q"; float kw = choiceKey.CalcSize(keyMeasure).x;
        measure.text = Loc.T(left); float lw = normal.CalcSize(measure).x;
        measure.text = Loc.T(right); float rw = normal.CalcSize(measure).x;
        // 왼쪽: [Q 되감기]가 캐릭터 왼쪽에서 끝나게
        float lx = cx - off - lw - gap - kw;
        ShadowLabel(new Rect(lx, y, kw + 6 * s, h), "Q", choiceKey, UiTheme.Amber);
        ShadowLabel(new Rect(lx + kw + gap, y, lw + 8 * s, h), left, normal);
        // 오른쪽: [E 능력]이 캐릭터 오른쪽에서 시작
        float rx = cx + off;
        ShadowLabel(new Rect(rx, y, kw + 6 * s, h), "E", choiceKey, UiTheme.Amber);
        ShadowLabel(new Rect(rx + kw + gap, y, rw + 8 * s, h), right, normal);
    }

    GUIStyle choiceKey;

    /// <summary>순찰 조직원 머리 위 의심 표시: 차오르는 ? (주황), 들키면 잠깐 ! (빨강)</summary>
    void DrawSuspicion(float s)
    {
        if (!cam || StoryState.CutsceneActive) return;
        mark ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        mark.fontSize = Mathf.RoundToInt(44 * s);
        foreach (var p in PatrolSight.All)
        {
            if (p.Enemy.Health.IsDead) continue;
            bool shouting = p.Staged || p.Alerted && Time.unscaledTime - p.AlertedAt < 1.4f;
            if (!shouting && (p.Alerted || p.Suspicion < 0.04f)) continue;
            Vector3 screen = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.25f);
            if (screen.z < 0f) continue;
            var r = new Rect(screen.x - 22 * s, Screen.height - screen.y - 50 * s, 44 * s, 50 * s);
            if (shouting) { Glyph(r, "!", new Color(1f, 0.29f, 0.23f), 1f); continue; }
            // 바탕은 흐린 ?, 의심만큼 아래에서 위로 진한 주황 ? 가 차오른다
            Glyph(r, "?", new Color(1f, 1f, 1f, 0.35f), 1f);
            Glyph(r, "?", new Color(0.95f, 0.66f, 0.23f), Mathf.Clamp01(p.Suspicion));
        }
    }

    GUIStyle mark;

    /// <summary>글자 하나를 그림자와 함께 그린다. fill(0~1)만큼 아래쪽부터 잘라 보여 준다</summary>
    void Glyph(Rect r, string text, Color color, float fill)
    {
        if (fill <= 0.001f) return;
        GUI.BeginGroup(new Rect(r.x, r.y + r.height * (1f - fill), r.width + 3f, r.height * fill + 3f));
        var local = new Rect(0f, -r.height * (1f - fill), r.width, r.height);
        mark.normal.textColor = new Color(0f, 0f, 0f, 0.7f * color.a);
        float o = UIText.ShadowOffset(mark.fontSize);
        UIText.ShadowOnly(new Rect(local.x + o, local.y + o, local.width, local.height), text, mark);
        mark.normal.textColor = color;
        UIText.Label(local, text, mark);
        GUI.EndGroup();
    }

    // ---------- 이야기 연출(자막·페이드·제목) ----------

    void DrawSubtitle(float s, float y)
    {
        if (!StoryState.LineVisible) return;
        if (!ReferenceEquals(subtitleSpeaker, StoryState.Speaker) || !ReferenceEquals(subtitleLine, StoryState.Line))
        {
            subtitleSpeaker = StoryState.Speaker;
            subtitleLine = StoryState.Line;
            subtitleText = string.IsNullOrEmpty(subtitleSpeaker) ? subtitleLine : $"<color=#E8C46A>{subtitleSpeaker}</color>   {subtitleLine}";
        }
        string text = subtitleText;
        normalCentre.richText = true;
        // 왼쪽 아래 카세트와 오른쪽 아래 알림을 피해 가운데 칸에만 쓴다(넘치면 두 줄 → 그래도 넘치면 UIText가 줄인다)
        float side = StoryState.CutsceneActive ? 340 * s : 480 * s; // 컷신: 오른쪽 아래 "Space 건너뛰기"(약 330)를 피한다 // 카세트 위 "+ 테이프 N개"(x 340~450)에서도 떨어지게
        // 위에서부터 쓴다: 보통 한두 줄, 좁은 화면은 세 줄까지. 화면 아래 끝은 넘지 않게(영어 대사는 길어 컷신 띠 밖으로 나갔다, 2026-10-21)
        ShadowLabel(new Rect(side, y, Screen.width - side * 2f, Mathf.Min(100 * s, Screen.height - y - 4 * s)), text, normalCentre);
    }

    void DrawStoryOverlay(float s)
    {
        if (StoryState.LetterText != null) DrawLetter(s);
        if (StoryState.Fade > 0.001f) Fill(new Rect(0, 0, Screen.width, Screen.height), Tint(StoryState.FadeColor, Mathf.Clamp01(StoryState.Fade)));
        if (!string.IsNullOrEmpty(StoryState.Title))
            ShadowLabel(new Rect(0, Screen.height * 0.4f, Screen.width, 120 * s), StoryState.Title, big);
    }

    /// <summary>화면 가운데에 펼친 편지(손편지 종이 + 잉크 글씨). F로 닫거나 Space를 길게 눌러 이어지는 장면까지 건너뛴다</summary>
    void DrawLetter(float s)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
        float w = 860 * s, h = Mathf.Min(760 * s, Screen.height - 80 * s); // 아래 안내 글이 화면 안에 들어오게
        var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f - 20 * s, w, h);
        Fill(new Rect(r.x + 8 * s, r.y + 10 * s, w, h), new Color(0f, 0f, 0f, 0.45f)); // 종이 그림자
        Fill(r, new Color(0.90f, 0.87f, 0.79f));
        Fill(new Rect(r.x + 40 * s, r.y + 104 * s, w - 80 * s, 2 * s), new Color(0.55f, 0.5f, 0.42f, 0.5f)); // 제목 아래 가는 선
        var ink = new Color(0.16f, 0.17f, 0.24f);
        var head = letterHead;
        var text = letterText;
        head.normal.textColor = ink;
        text.normal.textColor = ink;
        UIText.Label(new Rect(r.x, r.y + 44 * s, w, 50 * s), StoryState.LetterHeading ?? "", head);
        UIText.Label(new Rect(r.x + 60 * s, r.y + 130 * s, w - 120 * s, h - 170 * s), StoryState.LetterText, text);
        ShadowLabel(new Rect(0, Mathf.Min(r.yMax + 14 * s, Screen.height - 32 * s), Screen.width, 30 * s), "F  닫기        Space 길게 누르기  건너뛰기", smallCentre);
    }

    float bossBarTop; // 이번 프레임 보스 이름 칸의 위 끝(없으면 0)

    void DrawEnemyHealth(float s)
    {
        bossBarTop = 0f;
        foreach (var enemy in EnemyAI.All)
        {
            if (!enemy.IsActive || enemy.Health.IsDead) continue;
            float ratio = (float)enemy.Health.Current / enemy.Health.Max;

            if (enemy.isBoss)
            {
                float w = 900 * s;
                var rect = new Rect((Screen.width - w) / 2f, Screen.height - 110 * s, w, 14 * s);
                ShadowLabel(new Rect(rect.x, rect.y - 38 * s, w, 34 * s), enemy.displayName, normal);
                bossBarTop = rect.y - 38 * s;
                Bar(rect, ratio, Accent);
            }
            if (enemy.ExecuteTelegraph && cam)
            {
                // 처형기(레이가 기억함): 머리 위 보라색 크게 — 지금 굴러라
                Vector3 xs = cam.WorldToScreenPoint(enemy.transform.position + Vector3.up * (2.5f * enemy.transform.lossyScale.y));
                if (xs.z > 0f) { var r = new Rect(xs.x - 60 * s, Screen.height - xs.y - 40 * s, 120 * s, 40 * s); normalCentre.normal.textColor = new Color(0.8f, 0.4f, 1f); UIText.Label(r, "!!!", normalCentre); normalCentre.normal.textColor = Paper; }
            }
            if (enemy.HeavyTelegraph && cam)
            {
                // 강타 예고: 머리 위 붉은 !! (피하라는 신호)
                Vector3 hs = cam.WorldToScreenPoint(enemy.transform.position + Vector3.up * (2.5f * enemy.transform.lossyScale.y));
                if (hs.z > 0f) { var r = new Rect(hs.x - 40 * s, Screen.height - hs.y - 34 * s, 80 * s, 34 * s); normalCentre.normal.textColor = Accent; UIText.Label(r, "!!", normalCentre); normalCentre.normal.textColor = Paper; }
            }
            if (enemy.Foreseen > 0 && cam)
            {
                // 되감기의 기억: 레이는 이 적이 언제 칠지 안다 — 막대가 줄어들어 밝은 칸에 들어오면 구른다
                Vector3 fs = cam.WorldToScreenPoint(enemy.transform.position + Vector3.up * (2.1f * enemy.transform.lossyScale.y));
                if (fs.z > 0f) DrawForesight(new Rect(fs.x - 70 * s, Screen.height - fs.y, 140 * s, 10 * s), enemy, s);
            }
            if (enemy.isBoss) { }
            else if (ratio < 1f && cam)
            {
                Vector3 screen = cam.WorldToScreenPoint(enemy.transform.position + Vector3.up * (2.4f * enemy.transform.lossyScale.y));
                if (screen.z > 0f) Bar(new Rect(screen.x - 40 * s, Screen.height - screen.y, 80 * s, 7 * s), ratio, Accent);
            }
        }
    }

    static readonly Color Memory = new(1f, 0.82f, 0.5f);

    /// <summary>오른쪽 아래 작은 알림: 새 것이 맨 아래, 위로 쌓인다. 처음 0.2초 오른쪽에서 밀려 들어오고 끝 0.5초 흐려진다</summary>
    void DrawNotices(GameManager gm, float s)
    {
        var list = gm.Notices;
        float now = StoryState.Now, y = Screen.height - 54 * s;
        for (int i = list.Count - 1; i >= 0; i--, y -= 34 * s)
        {
            var (text, from, until) = list[i];
            float a = Mathf.Clamp01((until - now) / 0.5f) * Mathf.Clamp01((now - from) / 0.15f);
            float slide = (1f - Mathf.Clamp01((now - from) / 0.2f)) * 40 * s;
            var r = new Rect(0, y, Screen.width - 40 * s + slide, 30 * s);
            var c = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
            ShadowLabel(r, text, smallRight);
            GUI.color = c;
        }
    }

    /// <summary>기억한 적의 공격 타이밍 막대: 평소엔 흐린 빈 막대, 준비 동작에 들어가면 남은 시간만큼 차 있다가 줄어든다</summary>
    void DrawForesight(Rect r, EnemyAI enemy, float s)
    {
        float left = enemy.WindupLeft;
        var dim = Memory; dim.a = 0.35f;
        ShadowLabel(new Rect(r.x - 20 * s, r.y - 26 * s, r.width + 40 * s, 24 * s), left >= 0f ? "" : "기억", smallCentre);
        Fill(new Rect(r.x - s, r.y - s, r.width + 2 * s, r.height + 2 * s), new Color(0f, 0f, 0f, 0.55f));
        if (left < 0f) { Fill(new Rect(r.x, r.y + r.height / 2f - s, r.width, 2 * s), dim); return; }

        float total = Mathf.Max(0.05f, enemy.WindupDuration);
        float window = enemy.ranged ? 0.2f : EnemyAI.ForeseenWindow; // 총은 구르는 동안(무적) 날아와야 빗나간다
        float zone = Mathf.Clamp01(window / total) * r.width;
        Fill(new Rect(r.x, r.y, zone, r.height), dim);                                     // 구를 칸
        Fill(new Rect(r.x, r.y, Mathf.Clamp01(left / total) * r.width, r.height), Memory); // 남은 시간
        if (left <= window)
        {
            // 깜빡이는 테두리(막대는 가리지 않는다)
            var now = Accent; now.a = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 30f);
            float b = 3 * s;
            Fill(new Rect(r.x - b, r.y - b, r.width + 2 * b, b), now);
            Fill(new Rect(r.x - b, r.yMax, r.width + 2 * b, b), now);
            Fill(new Rect(r.x - b, r.y, b, r.height), now);
            Fill(new Rect(r.xMax, r.y, b, r.height), now);
            ShadowLabel(new Rect(r.x - 20 * s, r.y - 26 * s, r.width + 40 * s, 24 * s), "지금!", smallCentre);
        }
    }

    // ---------- Walkman 위젯 (좌하단) ----------

    void DrawWalkmanWidget(float s)
    {
        var tape = walkman.Inserted;
        float w = 300 * s, h = 190 * s, x = 40 * s, y = Screen.height - h - 30 * s;

        if (tape == null)
        {
            ShadowLabel(new Rect(x, y - 40 * s, 600 * s, 34 * s), "카세트 플레이어 비어 있음 — I로 테이프 끼우기", normal);
            DrawCassette(new Rect(x, y, w, h), null, false, 0.4f, s);
            return;
        }

        // 남은 되감기만 보인다(테이프 상태 줄·조작 안내 줄은 뺐다 — 사용자 요청).
        // 지금 R에 쓰일 테이프의 횟수(최대 3 — 끼운 테이프가 비면 다른 테이프). 횟수가 남은 테이프가 더 있으면 옆에 개수만 적는다
        int current = walkman.CurrentCharges, spare = walkman.SpareTapeCount;
        ShadowLabel(new Rect(x, y - 66 * s, 200 * s, 22 * s), "R E W I N D", eyebrow, Gold);
        ShadowLabel(new Rect(x, y - 44 * s, 600 * s, 34 * s), (Same(ref rewindSlot, current, null) ? rewindSlot.text : rewindSlot.text = $"남은 되감기 {current}회"), normal);
        Pips(new Rect(x + 190 * s, y - 36 * s, 0, 18 * s), current);
        if (spare > 0) ShadowLabel(new Rect(x + 300 * s, y - 40 * s, 300 * s, 30 * s), (Same(ref spareSlot, spare, null) ? spareSlot.text : spareSlot.text = $"+ 테이프 {spare}개"), small);
        DrawCassette(new Rect(x, y, w, h), tape, tape.CanPlay || walkman.Rewinding, tape.IsSpent && !tape.CanPlay ? 0.5f : 1f, s); // 횟수를 다 써도 노래가 흐르면 흐리게 하지 않는다
    }

    string StatusLine(CassetteTape tape)
    {
        if (!tape.playable) return tape.IsSpent ? "다 씀" : "되감기 전용";
        if (!tape.CanPlay) return "재생 불가";
        return $"♪ {tape.CurrentTrack + 1}/{tape.trackTitles.Length}  {tape.trackTitles[tape.CurrentTrack]}";
    }

    /// <summary>카세트 그림(바르코로 만든 사진, UiArtBuilder): 가게 테이프는 반투명 회색 + 크림색 라벨, 주운 테이프는 검은 무지.
    /// 자리 값(0~1, 위에서부터)은 UiArtBuilder가 찍은 값이다</summary>
    sealed class CassetteArt
    {
        public Texture2D body, hub;
        public float aspect, hubR;
        public Vector2 hubL, hubRight;
        public Rect label, stripe;
    }
    static CassetteArt labelArt, blackArt;

    static CassetteArt Art(bool playable)
    {
        if (labelArt == null)
        {
            labelArt = new CassetteArt
            {
                body = Resources.Load<Texture2D>("UI/Cassette_Label"), hub = Resources.Load<Texture2D>("UI/Cassette_Label_Hub"),
                aspect = 1.556f, hubR = 0.070f, hubL = new Vector2(0.294f, 0.470f), hubRight = new Vector2(0.706f, 0.470f),
                label = new Rect(0.085f, 0.114f, 0.837f, 0.222f), stripe = new Rect(0.066f, 0.672f, 0.875f, 0.030f),
            };
            blackArt = new CassetteArt
            {
                body = Resources.Load<Texture2D>("UI/Cassette_Black"), hub = Resources.Load<Texture2D>("UI/Cassette_Black_Hub"),
                aspect = 1.585f, hubR = 0.057f, hubL = new Vector2(0.294f, 0.468f), hubRight = new Vector2(0.706f, 0.467f),
                label = new Rect(0.081f, 0.103f, 0.840f, 0.212f), stripe = default,
            };
        }
        return playable ? labelArt : blackArt;
    }

    /// <summary><paramref name="r"/>에 맞춰(비율을 지켜 가운데) 그리는 카세트. 재생 중에는 릴 허브가 돈다.
    /// alpha가 1보다 작으면(다 쓴 테이프 · 교환한 진열품) 어둡게 그린다</summary>
    void DrawCassette(Rect r, CassetteTape tape, bool spinning, float alpha, float s)
    {
        var art = Art(tape == null || tape.playable);
        if (!art.body) return;
        // 비율을 지켜 칸 가운데에
        float w = Mathf.Min(r.width, r.height * art.aspect), h = w / art.aspect;
        var d = new Rect(r.x + (r.width - w) / 2f, r.y + (r.height - h) / 2f, w, h);
        Rect Sub(Rect q) => new(d.x + q.x * w, d.y + q.y * h, q.width * w, q.height * h);
        float bright = Mathf.Lerp(0.42f, 1f, alpha), fade = Mathf.Lerp(0.75f, 1f, alpha);
        var tint = new Color(bright, bright, bright, fade);

        if (Event.current.type == EventType.Repaint)
        {
            GUI.color = tint;
            GUI.DrawTexture(d, art.body, ScaleMode.StretchToFill, true);
            // 릴 허브: 사진의 허브 위에 오려 둔 허브를 돌려 그린다
            float angle = spinning ? walkman.ReelAngle : 0f, hr = art.hubR * w;
            var matrix = GUI.matrix;
            foreach (var c in new[] { art.hubL, art.hubRight })
            {
                var centre = new Vector2(d.x + c.x * w, d.y + c.y * h);
                GUI.matrix = matrix;
                GUIUtility.RotateAroundPivot(angle, centre);
                if (art.hub) GUI.DrawTexture(new Rect(centre.x - hr, centre.y - hr, hr * 2f, hr * 2f), art.hub, ScaleMode.StretchToFill, true);
            }
            GUI.matrix = matrix;
            GUI.color = Color.white;
        }
        // 장르 줄무늬: 라벨의 붉은 줄을 장르 색으로 덮는다(장르 없는 재생용 테이프는 붉은 줄 그대로)
        var genre = tape?.Genre ?? TapeGenre.None;
        if (art.stripe.width > 0f && genre != TapeGenre.None)
        {
            var g = TapeAbility.Tint(genre);
            Fill(Sub(art.stripe), new Color(g.r * 0.85f * bright, g.g * 0.85f * bright, g.b * 0.85f * bright, 0.92f * fade));
        }

        // 이름표 글씨: 읽을 수 있을 만큼 큰 카세트에만(화면 크기와 무관)
        if (tape != null && w / s > 165f && art.label.height * h >= 16f) // 라벨이 너무 작으면(작은 게임 창) 이름이 "…"로 잘려 쓰지 않는다
        {
            var label = Sub(art.label);
            float k = w / 300f;
            labelInk.normal.textColor = new Color(0.13f, 0.11f, 0.10f, 0.92f * fade);
            string name = tape.title;
            var nameRect = new Rect(label.x + 8 * k, label.y + 2 * k, label.width - 16 * k, label.height - 4 * k);
            labelInk.fontSize = Mathf.RoundToInt(21 * k);
            measure.text = Loc.T(name);
            bool oneLine = labelInk.CalcSize(measure).x <= nameRect.width;
            if (!oneLine) labelInk.fontSize = Mathf.RoundToInt(17 * k);
            labelInk.wordWrap = !oneLine;
            labelInk.alignment = TextAnchor.MiddleCenter;
            UIText.Label(nameRect, name, labelInk);
            labelInk.wordWrap = false;
            labelInk.alignment = TextAnchor.UpperLeft;
        }
    }

    // ---------- 되감기 ----------

    void DrawRewindPrompt(float s)
    {
        // 글자 설명은 뺐다(사용자 요청): 되감기 표시만 남긴다. 조작(R 되감기 · Space 저장 지점에서 부활)은 그대로
        ShadowLabel(new Rect(0, Screen.height * 0.26f, Screen.width, 80 * s), "◀◀  REWIND", big);
    }

    /// <summary>되감는 동안: 테이프 잡음 위에 ◀◀ REWIND 표시만(초·타임라인 글자는 뺐다 — 사용자 요청)</summary>
    void DrawPlayback(float s)
    {
        DrawTapeNoise(s);
        if (!walkman.PlayingBack) return;
        ShadowLabel(new Rect(0, Screen.height * 0.26f, Screen.width, 80 * s), "◀◀  REWIND", big);
    }

    void DrawTapeNoise(float s)
    {
        if (Event.current.type != EventType.Repaint) return;
        int frame = Time.frameCount;
        for (int i = 0; i < 14; i++)
        {
            float y = Mathf.PerlinNoise(frame * 0.37f, i * 1.7f) * Screen.height;
            float h = (2f + Mathf.PerlinNoise(i * 3.1f, frame * 0.21f) * 10f) * s;
            Fill(new Rect(0, y, Screen.width, h), new Color(Paper.r, Paper.g, Paper.b, 0.18f));
        }
    }

    // ---------- 인벤토리 (I) ----------

    void DrawInventory(float s)
    {
        var tapes = walkman.tapes;
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.01f, 0.01f, 0.02f, 0.62f));

        const int columns = Walkman.InventoryColumns;
        float slot = 116 * s, gap = 10 * s;
        int rows = Mathf.Max(3, Mathf.CeilToInt(tapes.Count / (float)columns));
        float gridW = columns * slot + (columns - 1) * gap, gridH = rows * slot + (rows - 1) * gap;
        float sideW = 300 * s, pad = 24 * s, titleH = 60 * s, footerH = 50 * s;
        float w = sideW * 2 + gridW + pad * 4;
        float h = titleH + Mathf.Max(gridH, 470 * s) + pad * 2 + footerH;
        var win = Window(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), "INVENTORY",
            $"카세트 테이프 {tapes.Count}개  ·  다 쓴 테이프 {walkman.SpentTapeCount}개", s, UiTheme.Skin.Case);

        float top = win.y + titleH + pad;

        // 왼쪽: Walkman 슬롯
        var left = new Rect(win.x + pad, top, sideW, h - titleH - pad * 2 - footerH);
        UiTheme.Panel(left, UiTheme.InkSoft, s);
        PlainLabel(new Rect(left.x + 16 * s, left.y + 12 * s, sideW, 28 * s), "장착 중 — WALKMAN", normalBold);
        var inserted = walkman.Inserted;
        DrawCassette(new Rect(left.x + 20 * s, left.y + 52 * s, sideW - 40 * s, (sideW - 40 * s) * 190f / 300f),
            inserted, inserted != null && inserted.CanPlay, inserted == null || inserted.IsSpent ? 0.5f : 1f, s);
        float infoY = left.y + 52 * s + (sideW - 40 * s) * 190f / 300f + 16 * s;
        if (inserted == null)
            PlainLabel(new Rect(left.x + 16 * s, infoY, sideW - 32 * s, 60 * s), "비어 있음", normal);
        else
        {
            PlainLabel(new Rect(left.x + 16 * s, infoY, sideW - 32 * s, 30 * s), inserted.title, normalBold);
            PlainLabel(new Rect(left.x + 16 * s, infoY + 32 * s, sideW - 32 * s, 50 * s), StatusLine(inserted), smallWrap);
            PlainLabel(new Rect(left.x + 16 * s, infoY + 84 * s, sideW, 26 * s), "되감기", small);
            Pips(new Rect(left.x + 90 * s, infoY + 86 * s, 0, 20 * s), inserted.charges);
            if (inserted.Genre != TapeGenre.None)
            {
                PlainLabel(new Rect(left.x + 16 * s, infoY + 120 * s, sideW - 32 * s, 30 * s), $"E  {TapeAbility.Name(inserted.Genre)}  {TapeAbility.Seconds(inserted.Genre):0}초", small, TapeAbility.Tint(inserted.Genre));
                PlainLabel(new Rect(left.x + 16 * s, infoY + 148 * s, sideW - 32 * s, 62 * s), TapeAbility.Describe(inserted.Genre), smallWrap, UiTheme.Dim);
            }
        }

        // 가운데: 슬롯 격자
        float gx = left.xMax + pad;
        var e = Event.current;
        for (int i = 0; i < rows * columns; i++)
        {
            var r = new Rect(gx + (i % columns) * (slot + gap), top + (i / columns) * (slot + gap), slot, slot);
            if (i >= tapes.Count)
            {
                UiTheme.Panel(r, new Color(0f, 0f, 0f, 0.35f), s); // 빈 칸: 가죽에 눌린 자국처럼 어둡게만
                UiTheme.Outline(r, UiTheme.Hair);
                continue;
            }

            var tape = tapes[i];
            bool selected = i == walkman.InventorySelected;
            UiTheme.RowBg(r, selected, s); // 덱 누름 단추(고르면 호박빛)
            DrawCassette(new Rect(r.x + 10 * s, r.y + 16 * s, slot - 20 * s, (slot - 20 * s) * 190f / 300f),
                tape, tape == inserted && tape.CanPlay, tape.IsSpent ? 0.35f : 1f, s);
            Pips(new Rect(r.x + (slot - 60 * s) / 2f, r.yMax - 26 * s, 0, 14 * s), tape.charges);
            if (tape == inserted) Tag(new Rect(r.x + 4 * s, r.y + 4 * s, 44 * s, 20 * s), "장착", Teal, s);
            if (tape.IsSpent) Tag(new Rect(r.xMax - 48 * s, r.y + 4 * s, 44 * s, 20 * s), "다 씀", UiTheme.Dim, s); // 카세트 그림 위에 겹쳐 읽기 어려웠다

            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                walkman.SelectInventorySlot(i);
                if (e.clickCount >= 2) walkman.Equip(i);
                e.Use();
            }
        }

        // 오른쪽: 선택한 테이프의 상세 정보
        var detail = new Rect(gx + gridW + pad, top, sideW, left.height);
        UiTheme.Panel(detail, UiTheme.InkSoft, s);
        if (tapes.Count > 0) DrawTapeDetails(detail, tapes[Mathf.Clamp(walkman.InventorySelected, 0, tapes.Count - 1)], s);

        PlainLabel(new Rect(win.x, win.yMax - footerH + 12 * s, w, 30 * s),
            "F 끼우기    I 닫기", smallCentre);
    }

    void DrawTapeDetails(Rect d, CassetteTape tape, float s)
    {
        float x = d.x + 16 * s, w = d.width - 32 * s, y = d.y + 12 * s;
        PlainLabel(new Rect(x, y, w, 30 * s), tape.title, normalBold);
        Tag(new Rect(x, y + 38 * s, 86 * s, 22 * s), tape.playable ? "재생용" : "주운 테이프", tape.playable ? Accent : Teal, s);
        // 상태 문구는 두 줄로 줄바꿈될 수 있으므로 아래 항목과 겹치지 않게 두 줄 높이를 확보한다
        PlainLabel(new Rect(x, y + 70 * s, w, 56 * s), StatusLine(tape), smallWrap);

        PlainLabel(new Rect(x, y + 132 * s, w, 26 * s), "되감기", small);
        Pips(new Rect(x + 74 * s, y + 134 * s, 0, 20 * s), tape.charges);

        if (tape.playable) PlainLabel(new Rect(x, y + 170 * s, w, 26 * s), "수록곡", small);
        for (int i = 0; tape.playable && i < tape.trackTitles.Length; i++) // 주운 테이프는 곡이 없다
        {
            string mark = !tape.playable ? "·" : i < tape.CompletedTracks ? "✓" : tape == walkman.Inserted && i == tape.CurrentTrack && tape.CanPlay ? "♪" : "·";
            PlainLabel(new Rect(x + 8 * s, y + (198 + i * 26) * s, w, 26 * s), $"{mark} {i + 1}. {tape.trackTitles[i]}", small);
        }

        // 장르 능력(R → E)만 짧게
        if (tape.Genre != TapeGenre.None)
        {
            float descY = y + (210 + tape.trackTitles.Length * 26) * s;
            PlainLabel(new Rect(x, descY, w, 28 * s), $"E  {TapeAbility.Name(tape.Genre)}  {TapeAbility.Seconds(tape.Genre):0}초", small, TapeAbility.Tint(tape.Genre));
            PlainLabel(new Rect(x, descY + 28 * s, w, 62 * s), TapeAbility.Describe(tape.Genre), smallWrap, UiTheme.Dim);
        }

        // 장착 버튼
        var button = new Rect(x, d.yMax - 56 * s, w, 42 * s);
        bool equipped = tape == walkman.Inserted;
        UiTheme.Button(button, !equipped, s, dim: equipped);
        // 버튼 높이 전체에서 세로 가운데로: 위에서 9px 내린 위쪽 정렬이라 한글 줄 높이만큼 처져 아래 테두리에 걸쳤다
        normalCentre.alignment = TextAnchor.MiddleCenter;
        PlainLabel(button, equipped ? "끼움" : "끼우기  F", normalCentre);
        normalCentre.alignment = TextAnchor.UpperCenter;
        var e = Event.current;
        if (!equipped && e.type == EventType.MouseDown && button.Contains(e.mousePosition))
        {
            walkman.Equip(walkman.InventorySelected);
            e.Use();
        }
    }

    // ---------- 테이프 상점 ----------

    /// <summary>① 진열대: 받을 테이프를 고른다. ② 고르면 내줄 테이프를 고르는 화면으로(DrawShopOffer)</summary>
    void DrawShop(TapeShop shop, float s)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.01f, 0.01f, 0.02f, 0.55f));
        if (shop.Offering && shop.Selected < shop.stock.Count) { DrawShopOffer(shop, s); return; }
        float w = 960 * s, rowH = 108 * s, titleH = 60 * s, pad = 20 * s, footerH = 50 * s;
        float h = titleH + pad * 2 + shop.stock.Count * (rowH + 10 * s) + footerH;
        int spent = 0, rewindCharges = 0;
        if (walkman) foreach (var t in walkman.tapes) { if (t.IsSpent) spent++; else if (TapeShop.Tradable(t)) rewindCharges += t.charges; }
        bool afford = shop.CanAfford;
        var win = Window(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), shop.shopName,
            $"다 쓴 테이프 {spent}개  ·  되감기용 테이프 {rewindCharges}회", s, UiTheme.Skin.Wood);
        var e = Event.current;

        for (int i = 0; i < shop.stock.Count; i++)
        {
            var item = shop.stock[i];
            var row = new Rect(win.x + pad, win.y + titleH + pad + i * (rowH + 10 * s), w - pad * 2, rowH);
            bool selected = i == shop.Selected;
            UiTheme.RowBg(row, selected, s);
            if (selected) UiTheme.Outline(row, Tint(Gold, 0.6f));

            DrawCassette(new Rect(row.x + 14 * s, row.y + 12 * s, 132 * s, 84 * s), item.tape, false, item.traded ? 0.35f : 1f, s);
            // 왼쪽 글자 칸은 오른쪽 상태 글자 앞에서 끝난다(좁은 화면에서 설명과 상태 글자가 붙었다)
            float tx = row.x + 166 * s, textW = row.width - 166 * s - 170 * s;
            PlainLabel(new Rect(tx, row.y + 12 * s, textW, 30 * s), item.tape.title, normalBold);
            var g = item.tape.Genre;
            if (g != TapeGenre.None)
            {
                PlainLabel(new Rect(tx, row.y + 46 * s, textW, 26 * s), $"E  {TapeAbility.Name(g)}  {TapeAbility.Seconds(g):0}초", small, TapeAbility.Tint(g));
                PlainLabel(new Rect(tx, row.y + 72 * s, textW, 26 * s), TapeAbility.Describe(g), small, UiTheme.Dim);
            }
            else PlainLabel(new Rect(tx, row.y + 46 * s, textW, 26 * s), $"{item.tape.trackTitles.Length}곡  ·  {string.Join(" / ", item.tape.trackTitles)}", small, UiTheme.Dim);
            string status = item.traded ? "교환 완료" : afford ? "교환 가능" : "테이프 부족";
            PlainLabel(new Rect(row.x, row.y + 40 * s, row.width - 20 * s, 30 * s), status, right, item.traded || !afford ? UiTheme.Dim : Paper);

            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition)) { shop.Choose(i); e.Use(); }
        }

        PlainLabel(new Rect(win.x, win.yMax - footerH + 12 * s, w, 30 * s), "W/S 선택   F 고르기   C / Esc 나가기", smallCentre);
    }

    /// <summary>② 내줄 테이프 고르기: 다 쓴 테이프 하나, 또는 되감기용 테이프 횟수 합 3(2026-10-17)</summary>
    void DrawShopOffer(TapeShop shop, float s)
    {
        var item = shop.stock[shop.Selected];
        var list = shop.Candidates;
        float w = 960 * s, rowH = 68 * s, rowGap = 8 * s, titleH = 60 * s, pad = 20 * s, headH = 120 * s, sumH = 64 * s, footerH = 50 * s;
        // 화면에 다 안 들어가면 고른 줄 둘레만 보인다(위아래 ▲▼)
        float fixedH = titleH + pad + headH + pad + sumH + footerH;
        int visible = Mathf.Max(1, Mathf.Min(list.Count, Mathf.FloorToInt((Screen.height * 0.92f - fixedH) / (rowH + rowGap))));
        int first = Mathf.Clamp(shop.OfferCursor - visible / 2, 0, Mathf.Max(0, list.Count - visible));
        float h = fixedH + visible * (rowH + rowGap);
        var win = Window(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), shop.shopName, "내줄 테이프를 고른다", s, UiTheme.Skin.Wood);
        var e = Event.current;

        // 받을 테이프
        var head = new Rect(win.x + pad, win.y + titleH + pad, w - pad * 2, headH);
        UiTheme.Panel(head, UiTheme.InkSoft, s);
        DrawCassette(new Rect(head.x + 14 * s, head.y + 14 * s, 144 * s, 92 * s), item.tape, false, 1f, s);
        float hx = head.x + 176 * s, hw = head.width - 176 * s - 150 * s;
        PlainLabel(new Rect(hx, head.y + 10 * s, hw, 20 * s), "받을 테이프", eyebrow, UiTheme.Dim);
        PlainLabel(new Rect(hx, head.y + 30 * s, hw, 30 * s), item.tape.title, normalBold);
        var genre = item.tape.Genre;
        if (genre != TapeGenre.None)
        {
            PlainLabel(new Rect(hx, head.y + 64 * s, hw, 26 * s), $"E  {TapeAbility.Name(genre)}  {TapeAbility.Seconds(genre):0}초", small, TapeAbility.Tint(genre));
            PlainLabel(new Rect(hx, head.y + 88 * s, hw, 26 * s), TapeAbility.Describe(genre), small, UiTheme.Dim);
        }
        PlainLabel(new Rect(head.x, head.y + 40 * s, head.width - 120 * s, 26 * s), "되감기", smallRight);
        Pips(new Rect(head.xMax - 100 * s, head.y + 43 * s, 0, 18 * s), CassetteTape.MaxCharges);

        // 내 테이프(내줄 수 있는 것만)
        float top = head.yMax + pad;
        if (first > 0) PlainLabel(new Rect(win.x, top - pad + 2 * s, w, 18 * s), "▲", smallCentre, UiTheme.Dim);
        if (first + visible < list.Count) PlainLabel(new Rect(win.x, top + visible * (rowH + rowGap) - 6 * s, w, 18 * s), "▼", smallCentre, UiTheme.Dim);
        for (int i = first; i < first + visible; i++)
        {
            var t = list[i];
            var row = new Rect(win.x + pad, top + (i - first) * (rowH + rowGap), w - pad * 2, rowH);
            bool cursor = i == shop.OfferCursor, picked = shop.Offer.Contains(t);
            UiTheme.RowBg(row, cursor, s);
            if (picked) UiTheme.Panel(row, UiTheme.AmberSoft, s);
            UiTheme.Outline(row, cursor ? Tint(Gold, 0.6f) : UiTheme.Hair);

            // 고른 표시: 네모 칸
            var box = new Rect(row.x + 16 * s, row.center.y - 11 * s, 22 * s, 22 * s);
            Fill(box, picked ? Gold : new Color(1f, 1f, 1f, 0.08f)); // 둥근 판은 작은 화면에서 모서리가 깨졌다
            Frame(box, picked ? Gold : new Color(1f, 1f, 1f, 0.3f), Mathf.Max(1f, s)); // 둥근 테두리는 작은 칸에서 모서리끼리 겹쳐 X자로 보였다
            if (picked) PlainLabel(new Rect(box.x, box.y - 1 * s, box.width, box.height), "✓", smallCentre, UiTheme.Ink);

            DrawCassette(new Rect(row.x + 52 * s, row.y + 7 * s, 84 * s, 54 * s), t, false, t.IsSpent ? 0.45f : 1f, s);
            PlainLabel(new Rect(row.x + 150 * s, row.y + 10 * s, row.width - 400 * s, 28 * s), t.title, normalBold);
            PlainLabel(new Rect(row.x + 150 * s, row.y + 38 * s, row.width - 400 * s, 24 * s), t.IsSpent ? "다 쓴 테이프" : "되감기용 테이프", small, UiTheme.Dim);
            if (t == walkman.Inserted) Tag(new Rect(row.x + row.width - 330 * s, row.y + 22 * s, 48 * s, 22 * s), "장착", Teal, s);
            PlainLabel(new Rect(row.x, row.y + 20 * s, row.width - 120 * s, 28 * s), t.IsSpent ? "다 씀" : $"{t.charges}회", right);
            Pips(new Rect(row.xMax - 100 * s, row.y + 25 * s, 0, 18 * s), t.charges);

            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition)) { shop.ToggleOffer(i); e.Use(); }
        }

        // 모은 값과 교환 단추
        int value = shop.OfferValue;
        bool enough = shop.OfferEnough;
        var sum = new Rect(win.x + pad, top + visible * (rowH + rowGap) + 6 * s, w - pad * 2, sumH - 12 * s);
        string have = value > TapeShop.PriceCharges
            ? $"되감기 {TapeShop.PriceCharges}/{TapeShop.PriceCharges}  ·  남는 {value - TapeShop.PriceCharges}회는 사라진다"
            : $"되감기 {Mathf.Min(value, TapeShop.PriceCharges)}/{TapeShop.PriceCharges}";
        PlainLabel(new Rect(sum.x + 4 * s, sum.y + 2 * s, sum.width - 220 * s, 28 * s), have, normal, enough ? Paper : UiTheme.Dim);
        Bar(new Rect(sum.x + 4 * s, sum.y + 36 * s, sum.width - 240 * s, 8 * s), Mathf.Clamp01(value / (float)TapeShop.PriceCharges), enough ? Gold : new Color(1f, 1f, 1f, 0.35f));
        var button = new Rect(sum.xMax - 200 * s, sum.y, 200 * s, 46 * s);
        UiTheme.Button(button, enough, s, dim: !enough);
        PlainLabel(new Rect(button.x, button.y + 10 * s, button.width, 26 * s), "교환  Space", normalCentre, enough ? Paper : UiTheme.Dim);
        if (e.type == EventType.MouseDown && button.Contains(e.mousePosition)) { shop.ConfirmOffer(); e.Use(); }

        PlainLabel(new Rect(win.x, win.yMax - footerH + 12 * s, w, 30 * s), "W/S 선택   F 넣기·빼기   Space 교환   C / Esc 뒤로", smallCentre);
    }

    // ---------- 웨이포인트 ----------

    /// <summary>현재 차단벽을 닫아 두고 있는 미클리어 교전 구역 중 가장 가까운 곳. 없으면 그 차단벽, 그것도 없으면 최종 구역이다.</summary>
    Vector3? WaypointTarget(GameManager gm, ZoneGate gate)
    {
        if (gate == null) return gm.finalZone ? gm.finalZone.transform.position : null;

        Vector3 from = player.transform.position;
        CombatZone nearest = null;
        foreach (var zone in gate.requiredZones)
            if (!zone.IsCleared && (nearest == null || Vector3.Distance(from, zone.transform.position) < Vector3.Distance(from, nearest.transform.position)))
                nearest = zone;
        return nearest ? nearest.transform.position : gate.transform.position;
    }

    /// <summary>가까운 테이프(아직 안 주운 것) 자리를 작게 표시한다: 길에서 조금 벗어나 주울 이유가 보이게. 멀수록 흐리다</summary>
    void DrawPickups(float s)
    {
        if (!cam || StoryState.InputLocked) return;
        Vector3 me = player.transform.position;
        foreach (var pickup in TapePickup.All)
        {
            if (!pickup || !pickup.HasTape) continue;
            Vector3 at = pickup.transform.position + Vector3.up * 0.9f;
            float d = Vector3.Distance(me, at);
            if (d > 22f || d < 1.5f) continue;
            Vector3 sp = cam.WorldToScreenPoint(at);
            if (sp.z < 0f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height) continue;
            float a = Mathf.Clamp01((22f - d) / 10f) * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f));
            var c = new Color(1f, 0.82f, 0.5f, a);
            float w = 26 * s, h = 16 * s;
            var r = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);
            // 카세트 모양: 테두리 + 두 릴
            Fill(new Rect(r.x, r.y, r.width, 2 * s), c); Fill(new Rect(r.x, r.yMax - 2 * s, r.width, 2 * s), c);
            Fill(new Rect(r.x, r.y, 2 * s, r.height), c); Fill(new Rect(r.xMax - 2 * s, r.y, 2 * s, r.height), c);
            Fill(new Rect(r.x + 6 * s, r.center.y - 2 * s, 4 * s, 4 * s), c); Fill(new Rect(r.xMax - 10 * s, r.center.y - 2 * s, 4 * s, 4 * s), c);
            int dm = Mathf.RoundToInt(d);
            if (d < 12f) ShadowLabel(new Rect(r.center.x - 60 * s, r.yMax + 2 * s, 120 * s, 24 * s), (Same(ref tapeDistSlot, dm, null) ? tapeDistSlot.text : tapeDistSlot.text = $"테이프 {dm}m"), smallCentre);
        }
    }

    void DrawWaypoint(Vector3 target, float s)
    {
        Vector3 point = target + Vector3.up * 2f;
        Vector3 local = cam.transform.InverseTransformPoint(point);
        Vector3 screen = cam.WorldToScreenPoint(point);
        // 카메라 뒤 · 옆(카메라 면에 가까우면 화면 좌표가 크게 튄다)이면 화면 아래 가장자리, 목표 쪽에 고정한다
        if (local.z < 0.5f) screen = new Vector3(Screen.width * (local.x < 0f ? 0.35f : 0.65f), 0f, 0f);
        float margin = 60 * s;
        var centre = new Vector2(Mathf.Clamp(screen.x, margin, Screen.width - margin),
                                 Mathf.Clamp(Screen.height - screen.y, 130 * s, Screen.height - margin * 2f)); // 위쪽 체력 · 목표 글자를 가리지 않게

        float size = 16 * s;
        var matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, centre);
        Fill(new Rect(centre.x - size / 2f, centre.y - size / 2f, size, size), Paper);
        GUI.matrix = matrix;

        int metres = Mathf.RoundToInt(Vector3.Distance(player.transform.position, target));
        ShadowLabel(new Rect(centre.x - 60 * s, centre.y + size, 120 * s, 30 * s), (Same(ref metresSlot, metres, null) ? metresSlot.text : metresSlot.text = $"{metres}m"), smallCentre);
    }

    // ---------- 헬퍼 ----------

    // 매 프레임 같은 글자를 새로 만들지 않게(GC 할당 줄이기, 2026-10-13 최적화): 열쇠(값)가 같으면 전에 만든 글자를 쓴다
    struct TextSlot { public long key; public object of; public string text; }
    static readonly GUIContent measure = new(), keyMeasure = new(); // 글자 폭 재기용(매 프레임 새로 만들지 않게)
    TextSlot rewindSlot, spareSlot, metresSlot, abilitySlot, choiceSlot, interactSlot, tapeDistSlot;

    /// <summary>같은 열쇠면 true(전 글자를 쓴다). 다르면 열쇠를 바꾸고 false(그 자리에서 slot.text를 새로 만든다)</summary>
    static bool Same(ref TextSlot slot, long key, object of)
    {
        if (slot.text != null && slot.key == key && ReferenceEquals(slot.of, of)) return true;
        slot.key = key; slot.of = of;
        return false;
    }

    /// <summary>제목 표시줄이 달린 테두리 창을 그리고, 창의 rect를 반환한다.</summary>
    Rect Window(Rect r, string heading, string subtitle, float s, UiTheme.Skin skin = UiTheme.Skin.Deck)
    {
        UiTheme.Window(r, s, null, skin);
        // 제목: 모서리 나사 · 리벳을 피해 안쪽으로(뒤에 띠를 깔면 오히려 읽기 어려웠다)
        PlainLabel(new Rect(r.x + 50 * s, r.y + 14 * s, r.width, 40 * s), heading, title);
        PlainLabel(new Rect(r.x, r.y + 22 * s, r.width - 50 * s, 30 * s), subtitle, smallRight, UiTheme.Dim);
        UiTheme.Hairline(r.x + 40 * s, r.y + 62 * s, r.width - 80 * s, s);
        return r;
    }

    void Tag(Rect r, string text, Color color, float s) => UiTheme.Tag(r, text, color, tagStyle, s);

    /// <summary>오른쪽 위 목표 글씨(이야기 목표·관문 진행·교전 중). 바뀔 때만 새로 만든다</summary>
    string ObjectiveText(GameManager gm, ZoneGate gate, bool fighting)
    {
        int cleared = StoryState.Objective == null && gate ? gate.ClearedCount : -1;
        int remaining = fighting ? gm.CurrentZone.Remaining : -1;
        if (objectiveValid && ReferenceEquals(objectiveStory, StoryState.Objective) && objectiveGate == gate && objectiveCleared == cleared
            && objectiveFighting == fighting && objectiveRemaining == remaining && ReferenceEquals(objectiveFinal, gm.finalObjective))
            return objectiveText;
        objectiveValid = true;
        objectiveStory = StoryState.Objective;
        objectiveGate = gate;
        objectiveCleared = cleared;
        objectiveFighting = fighting;
        objectiveRemaining = remaining;
        objectiveFinal = gm.finalObjective;
        string objective = StoryState.Objective ?? (gate ? $"{gate.objective}  ({cleared}/{gate.requiredZones.Length})" : gm.finalObjective);
        if (fighting) objective += $"\n교전 중 · 남은 적 {remaining}";
        return objectiveText = objective;
    }

    void BuildStyles(float s)
    {
        if (Mathf.Approximately(s, styleScale)) return;
        styleScale = s;
        small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * s) };
        smallCentre = new GUIStyle(small) { alignment = TextAnchor.UpperCenter };
        smallRight = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
        smallWrap = new GUIStyle(small) { wordWrap = true };
        normal = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24 * s) };
        normalBold = new GUIStyle(normal) { fontStyle = FontStyle.Bold };
        normalCentre = new GUIStyle(normal) { alignment = TextAnchor.UpperCenter };
        right = new GUIStyle(normal) { alignment = TextAnchor.UpperRight };
        big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(56 * s), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * s), fontStyle = FontStyle.Bold };
        labelInk = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
        letterHead = new GUIStyle(normalBold) { fontSize = Mathf.RoundToInt(28 * s), alignment = TextAnchor.UpperCenter };
        letterText = new GUIStyle(normal) { fontSize = Mathf.RoundToInt(25 * s), wordWrap = true, richText = true };
        eyebrow = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Bold };
        eyebrowRight = new GUIStyle(eyebrow) { alignment = TextAnchor.UpperRight };
        keyStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        tagStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    static Color Tint(Color c, float alpha) => new(c.r, c.g, c.b, c.a * alpha);

    static void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static void Frame(Rect r, Color color, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), color);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), color);
        Fill(new Rect(r.x, r.y, t, r.height), color);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), color);
    }

    static void Bar(Rect rect, float ratio, Color fill) => UiTheme.Bar(rect, ratio, fill, 1f);

    /// <summary>되감기 충전 가능 횟수마다 하나씩, 상자 세 개를 그린다. 채워진 상자 = 아직 쓸 수 있음.</summary>
    static void Pips(Rect origin, int filled)
    {
        float size = origin.height, gap = size * 0.5f;
        int slots = CassetteTape.MaxCharges; // 가진 테이프를 다 합쳐 최대 3회
        for (int i = 0; i < slots; i++)
            UiTheme.Panel(new Rect(origin.x + i * (size + gap), origin.y, size, size), i < filled ? Paper : new Color(1f, 1f, 1f, 0.16f), 1f);
    }

    static void ShadowLabel(Rect rect, string text, GUIStyle style) => ShadowLabel(rect, text, style, Paper);

    /// <summary>3D 화면 위 글자: 은은한 그림자와 함께</summary>
    static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color)
    {
        UIText.Shadowed(rect, text, style, Ink, color); // 칸을 넘치면 글자를 줄인다
    }

    /// <summary>판 위 글자: 그림자 없이(작은 화면에서 두 겹으로 보이지 않게)</summary>
    static void PlainLabel(Rect rect, string text, GUIStyle style) => PlainLabel(rect, text, style, Paper);

    static void PlainLabel(Rect rect, string text, GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        UIText.Label(rect, text, style);
    }

    /// <summary>키 모양 + 할 일(화면 가운데 줄): [F] 철조망 문을 밀어 연다</summary>
    void KeyPrompt(float y, string key, string label, float s)
    {
        measure.text = Loc.T(label);
        float lw = normal.CalcSize(measure).x, kh = 30 * s, gap = 12 * s;
        keyMeasure.text = Loc.T(key); float kw = Mathf.Max(kh, keyStyle.CalcSize(keyMeasure).x + 14 * s);
        float x = (Screen.width - (kw + gap + lw)) / 2f;
        var cap = UiTheme.KeyCap(new Vector2(x, y + 2 * s), kh, key, keyStyle, s);
        ShadowLabel(new Rect(cap.xMax + gap, y, lw + 8 * s, 34 * s), label, normal);
    }
}
