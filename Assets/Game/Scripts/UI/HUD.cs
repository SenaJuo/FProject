using UnityEngine;

/// <summary>프로토타입용 임시 IMGUI HUD — 레이아웃이 확정되면 UI Toolkit/uGUI로 교체한다.</summary>
public class HUD : MonoBehaviour
{
    static readonly Color Paper = new(0.85f, 0.82f, 0.72f);
    static readonly Color Ink = new(0.18f, 0.17f, 0.15f, 0.75f);
    static readonly Color Accent = new(0.85f, 0.35f, 0.25f);
    static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
    static readonly Color Teal = new(0.35f, 0.75f, 0.80f);
    static readonly Color WindowBg = new(0.06f, 0.06f, 0.07f, 0.97f);
    static readonly Color TitleBg = new(0.11f, 0.10f, 0.09f, 1f);
    static readonly Color Border = new(0.55f, 0.47f, 0.30f, 1f);
    static readonly Color SlotBg = new(0.11f, 0.11f, 0.12f, 1f);
    static readonly Color SlotBorder = new(0.26f, 0.25f, 0.23f, 1f);
    static readonly Color Shell = new(0.13f, 0.13f, 0.14f, 0.95f);
    static readonly Color PlayableLabel = new(0.92f, 0.86f, 0.70f);
    static readonly Color FoundLabel = new(0.70f, 0.78f, 0.86f);
    static readonly Color TapeBrown = new(0.32f, 0.20f, 0.12f);

    public PlayerController player;

    Health health;
    Walkman walkman;
    Camera cam;
    Texture2D circle;
    GUIStyle small, smallCentre, smallRight, smallWrap, normal, normalBold, normalCentre, right, big, title, labelInk;
    GUIStyle letterHead, letterText;
    float styleScale = -1f;

    // 매 OnGUI마다 같은 글자를 새로 만들지 않게 값이 바뀔 때만 다시 만든다
    string hpText, potionText, objectiveText, subtitleText, subtitleSpeaker, subtitleLine, objectiveStory, objectiveFinal;
    int hpCurrent = -1, hpMax = -1, potionCount = -1, objectiveCleared = -2, objectiveRemaining = -2;
    ZoneGate objectiveGate;
    bool objectiveFighting, objectiveValid;
    readonly string[] helpTexts = new string[4];

    void Awake() => useGUILayout = false; // GUILayout을 쓰지 않는다 → OnGUI 배치 단계를 건너뛴다

    void Start()
    {
        health = player.GetComponent<Health>();
        walkman = player.GetComponent<Walkman>();
        cam = Camera.main;
        circle = CreateCircle(64);
    }

    void OnGUI()
    {
        float s = Screen.height / 1080f;
        BuildStyles(s);
        var gm = GameManager.Instance;

        // 컷신: 게임 HUD는 모두 숨기고 영화처럼 위아래 검은 띠와 자막만 보인다
        if (StoryState.CutsceneActive)
        {
            float bar = Screen.height * 0.11f;
            Fill(new Rect(0, 0, Screen.width, bar), Color.black);
            Fill(new Rect(0, Screen.height - bar, Screen.width, bar), Color.black);
            DrawSubtitle(s, Screen.height - bar + 18 * s);
            DrawStoryOverlay(s);
            return;
        }

        // 플레이어 상태 (좌상단)
        Bar(new Rect(40 * s, 40 * s, 460 * s, 16 * s), (float)health.Current / health.Max, Paper);
        if (health.Current != hpCurrent || health.Max != hpMax) { hpCurrent = health.Current; hpMax = health.Max; hpText = $"HP {hpCurrent} / {hpMax}"; }
        ShadowLabel(new Rect(40 * s, 60 * s, 600 * s, 34 * s), hpText, normal);
        if (walkman && (walkman.potions > 0 || !StoryState.RewindLocked) && !StoryState.CutsceneActive)
        {
            if (walkman.potions != potionCount) { potionCount = walkman.potions; potionText = $"회복약 {potionCount}개   [1]"; }
            ShadowLabel(new Rect(40 * s, 96 * s, 600 * s, 30 * s), potionText, small);
        } // 프롤로그 첫 회차(아직 모름)에는 숨긴다

        // 목표 (우상단) + 웨이포인트
        if (gm && !gm.StageCleared)
        {
            var gate = gm.CurrentGate;
            // 이야기 목표가 있으면 그것이 우선이다(프롤로그처럼 관문이 없는 구간)
            bool fighting = gm.CurrentZone && !gm.CurrentZone.IsCleared;
            string objective = ObjectiveText(gm, gate, fighting);
            ShadowLabel(new Rect(Screen.width - 740 * s, 40 * s, 700 * s, 80 * s), objective, right);

            Vector3? waypoint = StoryState.Objective != null
                ? (StoryState.Waypoint ? StoryState.Waypoint.position : null)
                : WaypointTarget(gm, gate);
            if (!fighting && cam && waypoint is Vector3 target) DrawWaypoint(target, s);
        }

        DrawEnemyHealth(s);
        DrawSuspicion(s);

        bool menu = walkman && walkman.InventoryOpen || TapeShop.Current;
        if (walkman && !menu && !StoryState.WalkmanStowed) DrawWalkmanWidget(s); // 프롤로그에서는 아직 착용 전이라 숨긴다
        if (walkman && walkman.Rewinding) DrawPlayback(s);
        if (walkman && walkman.PromptOpen) DrawRewindPrompt(s);
        if (walkman && walkman.InventoryOpen) DrawInventory(s);
        if (TapeShop.Current) DrawShop(TapeShop.Current, s);
        else if (TapeShop.Nearby && !player.IsDead && !menu)
            ShadowLabel(new Rect(0, Screen.height * 0.62f, Screen.width, 34 * s), $"F: {TapeShop.Nearby.shopName}", normalCentre);
        else if (StoryState.Nearby && !player.IsDead && !menu)
            ShadowLabel(new Rect(0, Screen.height * 0.62f, Screen.width, 34 * s), $"F: {StoryState.Nearby.prompt}", normalCentre);
        if (Takedown.Target && !player.IsDead && !menu)
            ShadowLabel(new Rect(0, Screen.height * 0.58f, Screen.width, 34 * s), "Q: 몰래 제압", normalCentre);

        // 중앙 메시지
        bool rewindUi = walkman && (walkman.PromptOpen || walkman.Rewinding);
        string centre = rewindUi ? null : player.IsDead ? "YOU DIED" : gm && gm.MessageVisible ? gm.Message : null;
        if (centre != null) ShadowLabel(new Rect(0, Screen.height * 0.12f, Screen.width, 120 * s), centre, big);

        if (!menu)
        {
            // 아직 쓸 수 없는 조작은 안내에서 뺀다(프롤로그 첫 회차에는 공격·되감기를 모른다)
            int helpIndex = (StoryState.CombatLocked ? 1 : 0) + (StoryState.RewindLocked ? 2 : 0);
            string help = helpTexts[helpIndex] ??= "WASD 이동  마우스 시점"
                          + (StoryState.CombatLocked ? "" : "  좌클릭 공격  Q 몰래 제압")
                          + "  우클릭 회피  Space 점프  Shift 달리기"
                          + (StoryState.RewindLocked ? "" : "  R 되감기  I 인벤토리  2 테이프 교체  1 회복약")
                          + "  F 상호작용";
            ShadowLabel(new Rect(0, Screen.height - 40 * s, Screen.width - 40 * s, 30 * s), help, smallRight);
        }

        if (gm && Time.unscaledTime - gm.SavedAt < 2.5f && !menu)
            ShadowLabel(new Rect(0, Screen.height - 78 * s, Screen.width - 40 * s, 30 * s), gm.LastSaveHealed ? "저장됨 · 체력 회복" : "저장됨", smallRight);

        DrawSubtitle(s, Screen.height * 0.78f);
        if (!string.IsNullOrEmpty(StoryState.Prompt) && !(walkman && walkman.Rewinding)) DrawPrompt(StoryState.Prompt, s);
        DrawStoryOverlay(s);
    }

    /// <summary>지금 눌러야 할 키 안내: 화면 가운데 아래쪽, 어두운 띠 위에 크게</summary>
    void DrawPrompt(string text, float s)
    {
        float w = 760 * s, h = 64 * s;
        var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.6f, w, h);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
        Fill(r, new Color(0f, 0f, 0f, 0.62f));
        Fill(new Rect(r.x, r.y, 4 * s, h), Tint(Accent, pulse));
        normalCentre.richText = true;
        ShadowLabel(r, text, normalCentre);
    }

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
        GUI.Label(new Rect(local.x + 2f, local.y + 2f, local.width, local.height), text, mark);
        mark.normal.textColor = color;
        GUI.Label(local, text, mark);
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
        ShadowLabel(new Rect(80 * s, y, Screen.width - 160 * s, 60 * s), text, normalCentre);
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
        float w = 860 * s, h = 760 * s;
        var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f - 20 * s, w, h);
        Fill(new Rect(r.x + 8 * s, r.y + 10 * s, w, h), new Color(0f, 0f, 0f, 0.45f)); // 종이 그림자
        Fill(r, new Color(0.90f, 0.87f, 0.79f));
        Fill(new Rect(r.x + 40 * s, r.y + 104 * s, w - 80 * s, 2 * s), new Color(0.55f, 0.5f, 0.42f, 0.5f)); // 제목 아래 가는 선
        var ink = new Color(0.16f, 0.17f, 0.24f);
        var head = letterHead;
        var text = letterText;
        head.normal.textColor = ink;
        text.normal.textColor = ink;
        GUI.Label(new Rect(r.x, r.y + 44 * s, w, 50 * s), StoryState.LetterHeading ?? "", head);
        GUI.Label(new Rect(r.x + 60 * s, r.y + 130 * s, w - 120 * s, h - 170 * s), StoryState.LetterText, text);
        ShadowLabel(new Rect(0, r.yMax + 22 * s, Screen.width, 30 * s), "F  닫기        Space 길게 누르기  건너뛰기", smallCentre);
    }

    void DrawEnemyHealth(float s)
    {
        foreach (var enemy in EnemyAI.All)
        {
            if (!enemy.IsActive || enemy.Health.IsDead) continue;
            float ratio = (float)enemy.Health.Current / enemy.Health.Max;

            if (enemy.isBoss)
            {
                float w = 900 * s;
                var rect = new Rect((Screen.width - w) / 2f, Screen.height - 110 * s, w, 14 * s);
                ShadowLabel(new Rect(rect.x, rect.y - 38 * s, w, 34 * s), enemy.displayName, normal);
                Bar(rect, ratio, Accent);
            }
            else if (ratio < 1f && cam)
            {
                Vector3 screen = cam.WorldToScreenPoint(enemy.transform.position + Vector3.up * (2.4f * enemy.transform.lossyScale.y));
                if (screen.z > 0f) Bar(new Rect(screen.x - 40 * s, Screen.height - screen.y, 80 * s, 7 * s), ratio, Accent);
            }
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
        // 끼운 테이프가 비어도 다른 테이프로 되감으므로 가진 테이프 전부의 횟수를 센다
        int total = walkman.TotalCharges;
        ShadowLabel(new Rect(x, y - 44 * s, 600 * s, 34 * s), $"남은 되감기 {total}회", normal);
        Pips(new Rect(x + 190 * s, y - 38 * s, 0, 22 * s), total);
        DrawCassette(new Rect(x, y, w, h), tape, tape.CanPlay || walkman.Rewinding, tape.IsSpent ? 0.5f : 1f, s);
    }

    string StatusLine(CassetteTape tape)
    {
        if (tape.IsSpent) return "다 쓴 테이프 — 가게에서 교환";
        if (!tape.playable) return "주운 테이프 — 재생 불가 · 되감기 전용";
        if (tape.rewound) return "되감은 테이프 — 재생 불가";
        if (!tape.CanPlay) return "A면 끝";
        return $"♪ {tape.CurrentTrack + 1}/{tape.trackTitles.Length}  {tape.trackTitles[tape.CurrentTrack]}";
    }

    /// <summary><paramref name="r"/>에 맞춰 그리는 카세트(300×190 기준으로 디자인). 재생 중에는 릴이 돈다.</summary>
    void DrawCassette(Rect r, CassetteTape tape, bool spinning, float alpha, float s)
    {
        float k = r.width / 300f;
        Fill(r, Tint(Shell, alpha));
        Frame(r, Tint(SlotBorder, alpha), Mathf.Max(1f, 2f * k));

        var label = new Rect(r.x + 18 * k, r.y + 14 * k, r.width - 36 * k, 62 * k);
        Color labelColor = tape == null ? new Color(0.4f, 0.4f, 0.4f) : tape.playable ? PlayableLabel : FoundLabel;
        Fill(label, Tint(labelColor, alpha));
        Fill(new Rect(label.x, r.y + 58 * k, label.width, 7 * k), Tint(tape != null && !tape.playable ? Teal : Accent, alpha));
        if (r.width / s > 165f) // 글자를 읽을 수 있을 만큼 큰 카세트에만 제목을 표시한다 (화면 크기와 무관)
        {
            labelInk.fontSize = Mathf.RoundToInt(22 * k);
            labelInk.normal.textColor = new Color(0.12f, 0.11f, 0.10f, alpha);
            GUI.Label(new Rect(label.x + 10 * k, label.y + 4 * k, label.width - 20 * k, 40 * k), tape?.title ?? "— EMPTY —", labelInk);
        }

        var window = new Rect(r.x + 60 * k, r.y + 86 * k, r.width - 120 * k, 58 * k);
        Fill(window, Tint(new Color(0.04f, 0.04f, 0.05f, 0.95f), alpha));
        float progress = tape != null && tape.playable ? Mathf.Clamp01(tape.position / tape.Length) : 0.5f;
        float angle = spinning ? walkman.ReelAngle : 0f;
        Reel(new Vector2(window.x + 42 * k, window.center.y), Mathf.Lerp(26f, 12f, progress) * k, 10f * k, angle, alpha);
        Reel(new Vector2(window.xMax - 42 * k, window.center.y), Mathf.Lerp(12f, 26f, progress) * k, 10f * k, angle, alpha);

        Fill(new Rect(r.x + 70 * k, r.yMax - 34 * k, r.width - 140 * k, 24 * k), Tint(new Color(0.2f, 0.2f, 0.21f), alpha));
    }

    void Reel(Vector2 centre, float woundRadius, float hub, float angle, float alpha)
    {
        Disc(centre, woundRadius, Tint(TapeBrown, alpha));
        Disc(centre, hub, Tint(Paper, alpha));
        var matrix = GUI.matrix;
        for (int i = 0; i < 3; i++)
        {
            GUI.matrix = matrix;
            GUIUtility.RotateAroundPivot(angle + i * 120f, centre);
            Fill(new Rect(centre.x - hub * 0.15f, centre.y - hub, hub * 0.3f, hub), Tint(Shell, alpha));
        }
        GUI.matrix = matrix;
    }

    // ---------- 되감기 ----------

    void DrawRewindPrompt(float s)
    {
        var tape = walkman.RewindTape;
        float top = Screen.height * 0.26f;
        ShadowLabel(new Rect(0, top, Screen.width, 80 * s), "◀◀  REWIND", big);

        string kind = tape.playable ? "재생용 테이프" : "주운 테이프";
        ShadowLabel(new Rect(0, top + 80 * s, Screen.width, 34 * s),
            $"{tape.title} ({kind})  ·  되감기 {tape.charges}회 = 최대 {tape.charges * walkman.secondsPerCharge:0}초 전", normalCentre);

        string hint = "R을 누르고 있으면 되감긴다 · 손을 떼면 정지 (1회 = 5초)";
        if (walkman.RewindableTapeCount > 1) hint += "\n2: 되감을 테이프 선택";
        hint += "\nSpace: 되감지 않고 마지막 저장 지점에서 부활";
        ShadowLabel(new Rect(0, top + 130 * s, Screen.width, 100 * s), hint, smallCentre);
    }

    /// <summary>VCR 스타일 타임라인: 시간이 거꾸로 흐르면 헤드가 왼쪽으로 움직이고, 5초마다 눈금이 있다.</summary>
    void DrawPlayback(float s)
    {
        DrawTapeNoise(s);
        if (!walkman.PlayingBack) return;

        float seconds = walkman.RewoundSeconds, max = Mathf.Max(walkman.MaxRewind, 0.01f);
        ShadowLabel(new Rect(0, Screen.height * 0.26f, Screen.width, 80 * s), $"◀◀  -{seconds:00.0}초", big);

        float width = 700 * s, x0 = (Screen.width - width) / 2f, y = Screen.height * 0.26f + 100 * s;
        Bar(new Rect(x0, y, width, 6 * s), 1f, Ink);
        Bar(new Rect(x0 + width * (1f - seconds / max), y, width * seconds / max, 6 * s), 1f, Accent);
        for (float mark = walkman.secondsPerCharge; mark <= max + 0.01f; mark += walkman.secondsPerCharge)
        {
            float mx = x0 + width * (1f - mark / max);
            Bar(new Rect(mx - 2 * s, y - 8 * s, 4 * s, 22 * s), 1f, seconds >= mark - 0.01f ? Accent : Paper);
            ShadowLabel(new Rect(mx - 50 * s, y + 18 * s, 100 * s, 26 * s), $"-{mark:0}초", smallCentre);
        }
        // 기록이 5초보다 짧으면 실제로 감기는 만큼만 적는다
        ShadowLabel(new Rect(0, y + 50 * s, Screen.width, 30 * s),
            $"정지 지점: {Mathf.Min(walkman.NotchesSoFar * walkman.secondsPerCharge, walkman.MaxRewind):0}초 전" + (StoryState.FreeRewind ? "" : $" (되감기 {walkman.NotchesSoFar}회 사용)"), smallCentre);
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
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

        const int columns = Walkman.InventoryColumns;
        float slot = 116 * s, gap = 10 * s;
        int rows = Mathf.Max(3, Mathf.CeilToInt(tapes.Count / (float)columns));
        float gridW = columns * slot + (columns - 1) * gap, gridH = rows * slot + (rows - 1) * gap;
        float sideW = 300 * s, pad = 24 * s, titleH = 60 * s, footerH = 50 * s;
        float w = sideW * 2 + gridW + pad * 4;
        float h = titleH + Mathf.Max(gridH, 470 * s) + pad * 2 + footerH;
        var win = Window(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), "INVENTORY",
            $"카세트 테이프 {tapes.Count}개  ·  다 쓴 테이프 {walkman.SpentTapeCount}개", s);

        float top = win.y + titleH + pad;

        // 왼쪽: Walkman 슬롯
        var left = new Rect(win.x + pad, top, sideW, h - titleH - pad * 2 - footerH);
        Fill(left, SlotBg);
        Frame(left, SlotBorder, 2f * s);
        ShadowLabel(new Rect(left.x + 16 * s, left.y + 12 * s, sideW, 28 * s), "장착 중 — WALKMAN", normalBold);
        var inserted = walkman.Inserted;
        DrawCassette(new Rect(left.x + 20 * s, left.y + 52 * s, sideW - 40 * s, (sideW - 40 * s) * 190f / 300f),
            inserted, inserted != null && inserted.CanPlay, inserted == null || inserted.IsSpent ? 0.5f : 1f, s);
        float infoY = left.y + 52 * s + (sideW - 40 * s) * 190f / 300f + 16 * s;
        if (inserted == null)
            ShadowLabel(new Rect(left.x + 16 * s, infoY, sideW - 32 * s, 60 * s), "비어 있음", normal);
        else
        {
            ShadowLabel(new Rect(left.x + 16 * s, infoY, sideW - 32 * s, 30 * s), inserted.title, normalBold);
            ShadowLabel(new Rect(left.x + 16 * s, infoY + 32 * s, sideW - 32 * s, 50 * s), StatusLine(inserted), smallWrap);
            ShadowLabel(new Rect(left.x + 16 * s, infoY + 84 * s, sideW, 26 * s), "되감기", small);
            Pips(new Rect(left.x + 90 * s, infoY + 86 * s, 0, 20 * s), inserted.charges);
            var forR = walkman.NextManualRewindTape; // 끼운 테이프에 횟수가 없으면 R은 다른 테이프를 쓴다
            string rLine = forR == inserted ? "R 되감기에는 이 테이프가 쓰인다"
                         : forR != null ? $"이 테이프는 횟수가 없어, R 되감기에는 「{forR.title}」이(가) 쓰인다"
                         : "되감을 횟수가 남은 테이프가 없다";
            ShadowLabel(new Rect(left.x + 16 * s, infoY + 120 * s, sideW - 32 * s, 50 * s), rLine, smallWrap);
        }

        // 가운데: 슬롯 격자
        float gx = left.xMax + pad;
        var e = Event.current;
        for (int i = 0; i < rows * columns; i++)
        {
            var r = new Rect(gx + (i % columns) * (slot + gap), top + (i / columns) * (slot + gap), slot, slot);
            Fill(r, SlotBg);
            if (i >= tapes.Count)
            {
                Frame(r, SlotBorder, 2f * s);
                continue;
            }

            var tape = tapes[i];
            bool selected = i == walkman.InventorySelected;
            if (selected) Fill(r, new Color(Gold.r, Gold.g, Gold.b, 0.12f));
            DrawCassette(new Rect(r.x + 10 * s, r.y + 16 * s, slot - 20 * s, (slot - 20 * s) * 190f / 300f),
                tape, tape == inserted && tape.CanPlay, tape.IsSpent ? 0.35f : 1f, s);
            Pips(new Rect(r.x + (slot - 60 * s) / 2f, r.yMax - 26 * s, 0, 14 * s), tape.charges);
            if (tape == inserted) Tag(new Rect(r.x + 4 * s, r.y + 4 * s, 44 * s, 20 * s), "장착", Teal, s);
            if (tape.IsSpent) ShadowLabel(new Rect(r.x, r.y + slot * 0.36f, slot, 26 * s), "다 씀", smallCentre);
            Frame(r, selected ? Gold : SlotBorder, (selected ? 3f : 2f) * s);

            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                walkman.SelectInventorySlot(i);
                if (e.clickCount >= 2) walkman.Equip(i);
                e.Use();
            }
        }

        // 오른쪽: 선택한 테이프의 상세 정보
        var detail = new Rect(gx + gridW + pad, top, sideW, left.height);
        Fill(detail, SlotBg);
        Frame(detail, SlotBorder, 2f * s);
        if (tapes.Count > 0) DrawTapeDetails(detail, tapes[Mathf.Clamp(walkman.InventorySelected, 0, tapes.Count - 1)], s);

        ShadowLabel(new Rect(win.x, win.yMax - footerH + 12 * s, w, 30 * s),
            "WASD 이동   클릭 선택   F / 더블클릭 카세트 플레이어에 끼우기   2 빠른 교체   I 닫기", smallCentre);
    }

    void DrawTapeDetails(Rect d, CassetteTape tape, float s)
    {
        float x = d.x + 16 * s, w = d.width - 32 * s, y = d.y + 12 * s;
        ShadowLabel(new Rect(x, y, w, 30 * s), tape.title, normalBold);
        Tag(new Rect(x, y + 38 * s, 86 * s, 22 * s), tape.playable ? "재생용" : "주운 테이프", tape.playable ? Accent : Teal, s);
        // 상태 문구는 두 줄로 줄바꿈될 수 있으므로 아래 항목과 겹치지 않게 두 줄 높이를 확보한다
        ShadowLabel(new Rect(x, y + 70 * s, w, 56 * s), StatusLine(tape), smallWrap);

        ShadowLabel(new Rect(x, y + 132 * s, w, 26 * s), "되감기", small);
        Pips(new Rect(x + 74 * s, y + 134 * s, 0, 20 * s), tape.charges);

        ShadowLabel(new Rect(x, y + 170 * s, w, 26 * s), "수록곡", small);
        for (int i = 0; i < tape.trackTitles.Length; i++)
        {
            string mark = !tape.playable ? "·" : i < tape.CompletedTracks ? "✓" : tape == walkman.Inserted && i == tape.CurrentTrack && tape.CanPlay ? "♪" : "·";
            ShadowLabel(new Rect(x + 8 * s, y + (198 + i * 26) * s, w, 26 * s), $"{mark} {i + 1}. {tape.trackTitles[i]}", small);
        }

        float descY = y + (210 + tape.trackTitles.Length * 26) * s;
        string desc = tape.IsSpent ? "되감기를 다 썼다. 테이프 가게에서 새 테이프 1개와 바꿀 수 있다."
            : !tape.playable ? "현장에서 주운 테이프. 재생은 안 되지만 카세트 플레이어에 끼우면 R로 되감을 수 있다."
            : tape.rewound ? "한 번 되감은 테이프라 다시 재생할 수 없다. 남은 횟수만큼 되감기는 가능하다."
            : "재생하면 곡이 하나 끝날 때마다 되감기가 1회 충전된다 (최대 3회).";
        ShadowLabel(new Rect(x, descY, w, 90 * s), desc, smallWrap);

        // 장착 버튼
        var button = new Rect(x, d.yMax - 56 * s, w, 42 * s);
        bool equipped = tape == walkman.Inserted;
        Fill(button, equipped ? new Color(0.2f, 0.2f, 0.2f) : new Color(Gold.r * 0.55f, Gold.g * 0.45f, Gold.b * 0.25f));
        Frame(button, equipped ? SlotBorder : Gold, 2f * s);
        ShadowLabel(new Rect(button.x, button.y + 9 * s, button.width, 26 * s), equipped ? "장착 중" : "카세트 플레이어에 끼우기  [F]", normalCentre);
        var e = Event.current;
        if (!equipped && e.type == EventType.MouseDown && button.Contains(e.mousePosition))
        {
            walkman.Equip(walkman.InventorySelected);
            e.Use();
        }
    }

    // ---------- 테이프 상점 ----------

    void DrawShop(TapeShop shop, float s)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));
        float w = 860 * s, rowH = 92 * s, titleH = 60 * s, pad = 20 * s, footerH = 50 * s;
        float h = titleH + pad * 2 + shop.stock.Count * (rowH + 10 * s) + footerH;
        int spent = walkman ? walkman.SpentTapeCount : 0;
        var win = Window(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), shop.shopName,
            $"다 쓴 테이프 {spent}개 보유  ·  1개 = 새 테이프 1개", s);

        for (int i = 0; i < shop.stock.Count; i++)
        {
            var item = shop.stock[i];
            var row = new Rect(win.x + pad, win.y + titleH + pad + i * (rowH + 10 * s), w - pad * 2, rowH);
            bool selected = i == shop.Selected;
            Fill(row, selected ? new Color(Gold.r, Gold.g, Gold.b, 0.12f) : SlotBg);
            Frame(row, selected ? Gold : SlotBorder, (selected ? 3f : 2f) * s);

            DrawCassette(new Rect(row.x + 12 * s, row.y + 10 * s, 114 * s, 72 * s), item.tape, false, item.traded ? 0.35f : 1f, s);
            ShadowLabel(new Rect(row.x + 146 * s, row.y + 14 * s, 500 * s, 30 * s), item.tape.title, normalBold);
            ShadowLabel(new Rect(row.x + 146 * s, row.y + 50 * s, 520 * s, 26 * s),
                $"{item.tape.trackTitles.Length}곡  ·  {string.Join(" / ", item.tape.trackTitles)}", small);
            string status = item.traded ? "교환 완료" : spent > 0 ? "교환 가능" : "다 쓴 테이프 필요";
            ShadowLabel(new Rect(row.x, row.y + 32 * s, row.width - 20 * s, 30 * s), status, right);
        }

        ShadowLabel(new Rect(win.x, win.yMax - footerH + 12 * s, w, 30 * s), "W/S 선택   F 교환   C 나가기", smallCentre);
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

    void DrawWaypoint(Vector3 target, float s)
    {
        Vector3 screen = cam.WorldToScreenPoint(target + Vector3.up * 2f);
        if (screen.z < 0f) screen = new Vector3(Screen.width - screen.x, 0f, 0f); // 카메라 뒤쪽이면 화면 아래 가장자리에 고정한다
        float margin = 60 * s;
        var centre = new Vector2(Mathf.Clamp(screen.x, margin, Screen.width - margin),
                                 Mathf.Clamp(Screen.height - screen.y, margin, Screen.height - margin * 2f));

        float size = 16 * s;
        var matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, centre);
        Fill(new Rect(centre.x - size / 2f, centre.y - size / 2f, size, size), Paper);
        GUI.matrix = matrix;

        int metres = Mathf.RoundToInt(Vector3.Distance(player.transform.position, target));
        ShadowLabel(new Rect(centre.x - 60 * s, centre.y + size, 120 * s, 30 * s), $"{metres}m", smallCentre);
    }

    // ---------- 헬퍼 ----------

    /// <summary>제목 표시줄이 달린 테두리 창을 그리고, 창의 rect를 반환한다.</summary>
    Rect Window(Rect r, string heading, string subtitle, float s)
    {
        Fill(r, WindowBg);
        var bar = new Rect(r.x, r.y, r.width, 60 * s);
        Fill(bar, TitleBg);
        Fill(new Rect(r.x, bar.yMax - 2 * s, r.width, 2 * s), Border);
        Frame(r, Border, 2f * s);
        ShadowLabel(new Rect(r.x + 24 * s, r.y + 12 * s, r.width, 40 * s), heading, title);
        ShadowLabel(new Rect(r.x, r.y + 20 * s, r.width - 24 * s, 30 * s), subtitle, smallRight);
        return r;
    }

    void Tag(Rect r, string text, Color color, float s)
    {
        Fill(r, color);
        labelInk.fontSize = Mathf.RoundToInt(15 * s);
        labelInk.normal.textColor = new Color(0.07f, 0.07f, 0.08f);
        labelInk.alignment = TextAnchor.MiddleCenter;
        GUI.Label(r, text, labelInk);
        labelInk.alignment = TextAnchor.UpperLeft;
    }

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
        labelInk = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
        letterHead = new GUIStyle(normalBold) { fontSize = Mathf.RoundToInt(28 * s), alignment = TextAnchor.UpperCenter };
        letterText = new GUIStyle(normal) { fontSize = Mathf.RoundToInt(25 * s), wordWrap = true, richText = true };
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

    void Disc(Vector2 centre, float radius, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), circle);
        GUI.color = Color.white;
    }

    static void Bar(Rect rect, float ratio, Color fill)
    {
        Fill(rect, Ink);
        Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), fill);
    }

    /// <summary>되감기 충전 가능 횟수마다 하나씩, 상자 세 개를 그린다. 채워진 상자 = 아직 쓸 수 있음.</summary>
    static void Pips(Rect origin, int filled)
    {
        float size = origin.height, gap = size * 0.5f;
        int slots = Mathf.Clamp(filled, CassetteTape.MaxCharges, 9); // 테이프 여러 개를 합치면 3칸을 넘을 수 있다
        for (int i = 0; i < slots; i++)
            Bar(new Rect(origin.x + i * (size + gap), origin.y, size, size), i < filled ? 1f : 0f, Paper);
    }

    static void ShadowLabel(Rect rect, string text, GUIStyle style)
    {
        style.normal.textColor = Ink;
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
        style.normal.textColor = Paper;
        GUI.Label(rect, text, style);
    }

    static Texture2D CreateCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
        }
        tex.Apply();
        return tex;
    }
}
