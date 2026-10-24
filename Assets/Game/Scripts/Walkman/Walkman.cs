using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 주인공의 카세트 플레이어. 어떤 테이프든 넣을 수 있고, 재생 가능한 테이프는 노래가 계속 흐른다.
/// 테이프마다 되감기·능력 횟수(최대 3)가 정해져 있고, 듣는다고 다시 차지 않는다(2026-10-14). R을 누르고 있으면 Walkman에 든 테이프로 되감는다: 월드가 VCR처럼
/// 거꾸로 재생되고, 손을 떼면 다음 5초 지점에서 멈춘다 — 5초를 되돌릴 때마다 충전 1회를 쓴다.
/// </summary>
[RequireComponent(typeof(Health))]
public class Walkman : MonoBehaviour
{
    public const int InventoryColumns = 5;

    public List<CassetteTape> tapes = new();
    [Tooltip("되감기 1회가 되돌리는 시간(초)")]
    public float secondsPerCharge = 5f;
    [Tooltip("역재생이 실제 시간보다 몇 배 빠른지(4 = 5초를 1.25초에)")]
    public float playbackSpeed = 4f;
    [Tooltip("죽음에서 되감은 직후 무적 시간 — 반응하기도 전에 다시 죽지 않게")]
    public float invulnerableAfterDeathRewind = 1.5f;

    [Header("회복약 (1)")]
    public const int MaxPotions = 5;
    [Tooltip("가진 회복약. 테이프처럼 되감아도 줄거나 늘지 않는다(가방 속 물건)")]
    public int potions;
    public int potionHeal = 25;

    [Header("음악")]
    [Range(0f, 1f)] public float musicVolume = 0.42f;

    enum Phase { Playing, Prompt, Playback, Recover, Choice }

    /// <summary>Walkman에 든 테이프: (재생 가능하면) 재생되고, R로 되감을 때 이 테이프를 쓴다.</summary>
    public CassetteTape Inserted { get; private set; }
    /// <summary>현재 되감기(또는 사망 화면)가 사용 중인 테이프.</summary>
    public CassetteTape RewindTape { get; private set; }
    public bool PromptOpen => phase == Phase.Prompt;
    /// <summary>장르 테이프로 R을 눌러 Q(되감기)·E(능력)를 고르는 중</summary>
    public bool ChoiceOpen => phase == Phase.Choice;
    public bool PlayingBack => phase == Phase.Playback;
    public bool Rewinding => phase is Phase.Playback or Phase.Recover;
    public bool InventoryOpen { get; private set; }
    /// <summary>어느 워크맨이든 인벤토리를 연 중(대사·알림을 멈추는 데 쓴다)</summary>
    public static bool InventoryShown { get; private set; }
    public int InventorySelected { get; private set; }
    public float RewoundSeconds { get; private set; }
    public float MaxRewind { get; private set; }
    public int NotchesSoFar => Mathf.Max(1, Mathf.CeilToInt(RewoundSeconds / secondsPerCharge - 0.001f));
    public int RewindableTapeCount { get { int n = 0; foreach (var t in tapes) if (t.charges > 0) n++; return n; } } // FindAll은 매번 목록을 새로 만들어(HUD가 매 프레임 부른다) 직접 센다
    /// <summary>가진 테이프 전부의 남은 되감기 횟수(R은 끼운 테이프가 비면 다른 테이프로 되감는다)</summary>
    public int TotalCharges { get { int n = 0; foreach (var t in tapes) n += t.charges; return n; } }
    /// <summary>지금 R을 누르면 쓰일 테이프의 남은 횟수(테이프 한 개당 최대 3회). 끼운 테이프가 비었으면 다음 테이프의 횟수</summary>
    public int CurrentCharges => ManualRewindTape()?.charges ?? 0;
    /// <summary>그 밖에 횟수가 남은 테이프 수</summary>
    public int SpareTapeCount { get { var inUse = ManualRewindTape(); int n = 0; foreach (var t in tapes) if (t.charges > 0 && t != inUse) n++; return n; } }
    public int SpentTapeCount { get { int n = 0; foreach (var t in tapes) if (t.IsSpent) n++; return n; } }
    /// <summary>카세트 릴이 회전한 각도. HUD에서 쓴다.</summary>
    public float ReelAngle { get; private set; }

    Phase phase;
    float startClock, stopAt;
    bool fromDeath;
    Vector2Int navigationHeld;
    Health health;
    RewindEffect effect;
    InputAction rewindAction, jumpAction, nextAction, previousAction, inventoryAction, moveAction, interactAction, closeAction, healAction;
    AudioSource music;
    AudioClip musicClip, loadedClip;
    CassetteTape loadedTape;
    int loadedTrack = -1;
    float nextPotionTime;

    void Awake()
    {
        health = GetComponent<Health>();
        var actions = InputSystem.actions;
        rewindAction = actions.FindAction("Player/Rewind", true);
        jumpAction = actions.FindAction("Player/Jump", true);
        nextAction = actions.FindAction("Player/Next", true);
        previousAction = actions.FindAction("Player/Previous", true);
        inventoryAction = actions.FindAction("Player/Inventory", true);
        moveAction = actions.FindAction("Player/Move", true);
        interactAction = actions.FindAction("Player/Interact", true);
        closeAction = actions.FindAction("Player/Crouch", true);
        healAction = actions.FindAction("Player/Heal", false);
        // 이어폰으로 듣는 음악: 위치와 상관없이 들리는 2D 소리
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = false;
        music.spatialBlend = 0f;
        music.volume = musicVolume;
        music.ignoreListenerPause = false;
        Inserted = tapes.Count > 0 ? tapes[0] : null;
        InventoryShown = false;
        TimeRewind.Clear();
    }

    void Start() => effect = FindAnyObjectByType<RewindEffect>();

    void Update()
    {
        UpdateMusic();
        switch (phase)
        {
            case Phase.Prompt: UpdatePrompt(); return;
            case Phase.Playback: UpdatePlayback(); return;
            case Phase.Recover: return;
            case Phase.Choice: UpdateChoice(); return;
        }
        if (InventoryOpen) { UpdateInventory(); return; }
        if (health.IsDead || TapeShop.Current) return;

        // 컷신 중이거나 아직 되감기 능력을 모르는 구간(프롤로그 첫 회차)에는 조작을 받지 않는다. 녹화와 재생은 계속된다.
        bool asked = StoryState.RewindAsked; // 이야기가 되감기를 요구하는 중: 컷신이어도 R은 바로 되감기
        bool locked = (StoryState.InputLocked && !(asked && !StoryState.MenuOpen)) || StoryState.RewindLocked; // 메뉴·설명 창이 열려 있으면 그래도 막는다
        if (!locked && !asked && Inserted != null && Inserted.Genre != TapeGenre.None) Tutorial.Show("genre"); // 장르 테이프를 처음 끼우면 장르 능력 설명(한 번만)
        // 연출로 시간을 붙잡은 동안(1장 강제 되감기)에는 열지 않는다: 닫을 때 시간이 정상 속도로 풀려 버렸다
        if (!locked && !asked && !StoryState.HoldTime && inventoryAction.WasPressedThisFrame()) { OpenInventory(); return; }
        if (!locked && rewindAction.WasPressedThisFrame()) { if (asked || !TryOpenChoice()) TryRewindNow(); return; }
        if (!StoryState.InputLocked && healAction != null && healAction.WasPressedThisFrame()) UsePotion();

        float dt = Time.unscaledDeltaTime;
        // 연출로 시간을 붙잡아 둔 동안·설명 팝업이 열려 게임이 멈춘 동안은 기록하지도, 테이프를 돌리지도 않는다
        if (StoryState.HoldTime || StoryState.MenuOpen) return;
        TimeRewind.Record(dt);
        if (!locked && nextAction.WasPressedThisFrame()) CycleInserted(1);
        if (!locked && previousAction.WasPressedThisFrame()) CycleInserted(-1);
        PlayInserted(dt);
    }

    void PlayInserted(float deltaTime)
    {
        var tape = Inserted;
        if (tape == null || !tape.CanPlay || StoryState.WalkmanStowed) return; // 착용 전에는 재생되지 않는다

        ReelAngle += 150f * deltaTime;
        // 노래만 흐른다: 횟수는 차지 않고, 테이프 끝에 닿으면 처음 곡부터 다시 돈다
        tape.position += deltaTime;
        if (tape.position >= tape.Length) tape.position = 0f;
    }

    // ---------- 테이프 ----------

    public void AddTape(CassetteTape tape)
    {
        tapes.Add(tape);
        // 바로 끼우는 경우: 카세트 플레이어가 비었거나, 끼운 테이프를 다 썼거나(횟수 0·재생 불가), 재생 불가 테이프 대신 재생용을 얻었을 때.
        // 그 밖에는 인벤토리에 넣어 두고 플레이어가 직접 끼운다
        if (Inserted == null || (Inserted.IsSpent && (tape.charges > 0 || tape.CanPlay)) || (tape.CanPlay && !Inserted.CanPlay && Inserted.charges == 0))
        {
            bool swapped = Inserted != null;
            Inserted = tape;
            if (swapped) Announce($"▶ {tape.title}");
        }
    }

    /// <summary>다 쓴 테이프 하나를 꺼내 반환한다(상점 교환용). 없으면 null을 반환한다.</summary>
    public CassetteTape TakeSpentTape()
    {
        var spent = tapes.Find(t => t.IsSpent);
        if (spent == null) return null;
        tapes.Remove(spent);
        if (Inserted == spent) Inserted = null;
        return spent;
    }

    /// <summary>테이프 하나를 꺼낸다(상점 교환). 끼워 둔 테이프였으면 카세트 플레이어가 빈다</summary>
    public void RemoveTape(CassetteTape tape)
    {
        if (!tapes.Remove(tape)) return;
        if (Inserted == tape) Inserted = null;
        InventorySelected = Mathf.Clamp(InventorySelected, 0, Mathf.Max(0, tapes.Count - 1));
    }

    /// <summary><paramref name="index"/> 위치의 테이프를 Walkman에 넣는다.</summary>
    public void Equip(int index)
    {
        if (index < 0 || index >= tapes.Count) return;
        var tape = tapes[index];
        if (tape == Inserted) return;
        Inserted = tape;
        Announce($"▶ {tape.title}");
    }

    /// <summary>불러온 저장에서: 알림 없이 그 번호의 테이프를 끼운다(없으면 비운다)</summary>
    public void EquipIndex(int index)
    {
        Inserted = index >= 0 && index < tapes.Count ? tapes[index] : (tapes.Count > 0 ? tapes[0] : null);
    }

    void CycleInserted(int direction)
    {
        if (tapes.Count < 2) return;
        Equip(Wrap(Mathf.Max(0, tapes.IndexOf(Inserted)) + direction));
    }

    int Wrap(int i) => (i % tapes.Count + tapes.Count) % tapes.Count;

    // ---------- 인벤토리 ----------

    public void SelectInventorySlot(int index)
    {
        if (index >= 0 && index < tapes.Count) InventorySelected = index;
    }

    void OpenInventory()
    {
        InventoryOpen = InventoryShown = true;
        InventorySelected = Mathf.Max(0, tapes.IndexOf(Inserted));
        navigationHeld = Vector2Int.one;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CloseInventory()
    {
        InventoryOpen = InventoryShown = false;
        StoryState.MenuClosedFrame = Time.frameCount; // 닫은 Esc로 일시정지가 바로 열리지 않게
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UpdateInventory()
    {
        if (inventoryAction.WasPressedThisFrame() || closeAction.WasPressedThisFrame() || PauseMenu.BackPressed) { CloseInventory(); return; }
        if (tapes.Count == 0) return;
        if (nextAction.WasPressedThisFrame()) // 2: 빠른 교체(다음 테이프를 끼운다)
        {
            CycleInserted(1);
            InventorySelected = Mathf.Max(0, tapes.IndexOf(Inserted));
            return;
        }

        // 격자 탐색: 스틱을 한 번 밀 때마다 한 칸 이동한다. 좌우는 같은 행 안에서, 상하는 행 사이를 오간다.
        Vector2 move = moveAction.ReadValue<Vector2>();
        var dir = new Vector2Int(Mathf.Abs(move.x) > 0.5f ? (int)Mathf.Sign(move.x) : 0,
                                 Mathf.Abs(move.y) > 0.5f ? (int)Mathf.Sign(move.y) : 0);
        if (dir.x != 0 && navigationHeld.x == 0)
            InventorySelected = Mathf.Clamp(InventorySelected + dir.x, 0, tapes.Count - 1);
        if (dir.y != 0 && navigationHeld.y == 0)
        {
            int target = InventorySelected - dir.y * InventoryColumns;
            if (target >= 0 && target < tapes.Count) InventorySelected = target;
        }
        navigationHeld = new Vector2Int(dir.x != 0 ? 1 : 0, dir.y != 0 ? 1 : 0);

        InventorySelected = Mathf.Clamp(InventorySelected, 0, tapes.Count - 1);
        if (interactAction.WasPressedThisFrame()) Equip(InventorySelected);
    }

    // ---------- 회복약 ----------

    /// <summary>테이프를 줍는 곳에서 회복약도 함께 얻는다(최대 MaxPotions개). 실제로 얻은 수를 돌려준다</summary>
    public int AddPotions(int count)
    {
        int before = potions;
        potions = Mathf.Clamp(potions + count, 0, MaxPotions);
        return potions - before;
    }

    void UsePotion()
    {
        if (Time.unscaledTime < nextPotionTime) return;
        if (potions <= 0) { Announce("회복약이 없다"); return; }
        if (health.Current >= health.Max) { Announce("체력이 가득 차 있다"); return; }
        int gained = health.Heal(potionHeal);
        potions--;
        nextPotionTime = Time.unscaledTime + 0.6f;
        ProceduralSfx.PlayAt(HitFeel.Clip("potion") ?? ProceduralSfx.ButtonClick, transform.position, 0.8f, 0f);
        Announce($"체력 +{gained}");
    }

    // ---------- 음악(이어폰) ----------

    /// <summary>
    /// 끼운 재생용 테이프의 지금 곡을 테이프 위치에 맞춰 튼다. 테이프가 멈춰 있으면(주머니 속·컷신의 시간 붙잡기·되감기·
    /// 메뉴·사망) 음악도 멈춘다. 되감기·능력에 쓴 테이프도 노래는 계속 흐른다.
    /// </summary>
    void UpdateMusic()
    {
        var tape = Inserted;
        bool rolling = phase == Phase.Playing && !InventoryOpen && !TapeShop.Current && !health.IsDead
                       && !StoryState.WalkmanStowed && !StoryState.HoldTime && !StoryState.MenuOpen
                       && tape != null && tape.CanPlay && !string.IsNullOrEmpty(tape.musicId);
        if (!rolling)
        {
            if (music.isPlaying) music.Pause();
            return;
        }
        if (tape != loadedTape || tape.CurrentTrack != loadedTrack || loadedClip == null) // 곡이 바뀔 때만 찾는다(웹은 받는 동안 비어 있어 다시 묻는다)
        {
            loadedTape = tape;
            loadedTrack = tape.CurrentTrack;
            loadedClip = MusicLibrary.Get($"{tape.musicId}-{loadedTrack + 1}");
        }
        var clip = loadedClip;
        if (clip == null) { if (music.isPlaying) music.Stop(); return; }
        float at = Mathf.Clamp(tape.position - tape.CurrentTrack * tape.trackLength, 0f, clip.length - 0.05f);
        if (clip != musicClip)
        {
            musicClip = clip;
            music.clip = clip;
            music.time = at;
            music.Play();
            return;
        }
        if (!music.isPlaying) { music.time = at; music.UnPause(); if (!music.isPlaying) music.Play(); }
        else if (Mathf.Abs(music.time - at) > 0.35f) music.time = at; // 테이프 위치와 어긋나면 맞춘다
        // 곡은 테이프 한 곡 길이(최대 2분)에서 끊는다: 처음 0.4초는 서서히 커지고, 끝나기 3.5초 전부터 줄어든다
        float fade = Mathf.Clamp01(at / 0.4f) * Mathf.Clamp01((tape.trackLength - at) / 3.5f);
        float gain = tape.trackGains != null && tape.CurrentTrack < tape.trackGains.Length ? tape.trackGains[tape.CurrentTrack] : 1f;
        music.volume = Mathf.Min(1f, musicVolume * gain * fade) * GameSettings.Gain(GameSettings.Channel.Music); // 옵션의 음악 크기
    }

    // ---------- 되감기 ----------

    // ---------- 장르 테이프: R → Q 되감기 / E 능력 ----------

    TapeAbility ability;
    float choiceUntil, choiceScale;
    bool rewindByQ;

    /// <summary>장르가 있는 테이프(횟수 남음)를 끼웠으면 고르기 창을 연다. 장르가 없으면 예전처럼 바로 되감는다</summary>
    bool TryOpenChoice()
    {
        var tape = ManualRewindTape();
        if (tape == null || tape.Genre == TapeGenre.None || StoryState.FreeRewind) return false; // 튜토리얼 연습 되감기는 그대로
        if (TimeRewind.RecordedSeconds < 0.2f) return false;
        if (!ability) ability = GetComponent<TapeAbility>() ?? gameObject.AddComponent<TapeAbility>();
        if (TapeAbility.Current != TapeGenre.None) ability.EndNow(); // 켜진 능력 위에서 다시 고른다
        phase = Phase.Choice;
        choiceUntil = Time.unscaledTime + 4f;
        choiceScale = Time.timeScale;
        Time.timeScale = Mathf.Min(Time.timeScale, 0.15f); // 고르는 동안 세상이 거의 멈춘다
        if (effect) effect.SetWeight(0.15f);
        return true;
    }

    void UpdateChoice()
    {
        var kb = Keyboard.current;
        bool q = kb != null && kb.qKey.wasPressedThisFrame, e = kb != null && kb.eKey.wasPressedThisFrame;
        bool cancel = rewindAction.WasPressedThisFrame() || (kb != null && kb.escapeKey.wasPressedThisFrame) || Time.unscaledTime > choiceUntil || health.IsDead;
        if (!q && !e && !cancel) return;
        Choose(q ? 1 : e ? 2 : 0);
    }

    /// <summary>고르기 창을 닫는다: 1 = 되감기(Q), 2 = 능력(E), 0 = 취소. 열린 창이 없으면 장르 테이프로 연 뒤 바로 고른다(시험용)</summary>
    public bool Choose(int pick)
    {
        if (phase != Phase.Choice && !TryOpenChoice()) return false;
        phase = Phase.Playing;
        Time.timeScale = choiceScale > 0.99f ? 1f : choiceScale;
        if (effect) effect.SetWeight(0f);
        if (pick == 1) { rewindByQ = true; TryRewindNow(); return true; }
        if (pick != 2) return true;
        var tape = ManualRewindTape();
        if (tape == null) return false;
        tape.charges--; // 횟수만 쓴다: 테이프는 계속 재생된다(되감기와 다른 점)
        ability.Activate(tape.Genre);
        return true;
    }

    void TryRewindNow()
    {
        var tape = ManualRewindTape();
        if (tape == null)
        {
            Announce("되감기 없음");
            return;
        }
        if (TimeRewind.RecordedSeconds < 0.2f) return;
        TimeRewind.RecordNow();
        BeginPlayback(tape, false);
    }

    /// <summary>지금 R을 누르면 쓰일 테이프: 끼운 테이프뿐이다(횟수가 없으면 null). 인벤토리 안내에 쓴다</summary>
    public CassetteTape NextManualRewindTape => ManualRewindTape();

    /// <summary>저장 지점에서 다시 시작할 때: 사망 화면·되감기 도중이면 모두 거두고 정상 시간으로 돌아간다</summary>
    public void ResetForRespawn()
    {
        StoryState.RewindAsked = false;
        if (InventoryOpen) CloseInventory();
        if (phase == Phase.Playing) return;
        StopAllCoroutines();
        if (effect)
        {
            if (phase == Phase.Playback) effect.StopWhir();
            effect.SetWeight(0f);
        }
        phase = Phase.Playing;
        TimeRewind.PlayingBack = false;
        RewoundSeconds = 0f;
        Time.timeScale = 1f;
    }

    /// <summary>되감기·능력에 쓸 테이프: 끼운 테이프에 횟수가 있으면 그것, 없으면 인벤토리에서 횟수가 남은 테이프(음악 없는 테이프부터).
    /// 끼운 테이프는 바꾸지 않는다 — 횟수를 다 써도 듣던 노래는 계속 흐른다</summary>
    CassetteTape ManualRewindTape() => Inserted != null && Inserted.charges > 0 ? Inserted : SpareWithCharges();

    CassetteTape SpareWithCharges() => tapes.Find(t => t.charges > 0 && !t.CanPlay) ?? tapes.Find(t => t.charges > 0);


    /// <summary>
    /// 버튼을 누르고 있지 않아도 정해진 횟수만큼 5초 단위로 되감는다 — 컷신(떨어뜨린
    /// Walkman)과 테스트용이다. 플레이 중이든 사망 화면에서든 동작한다.
    /// </summary>
    public bool RewindSteps(int steps)
    {
        bool dying = phase == Phase.Prompt;
        if (phase != Phase.Playing && !dying) return false;
        var tape = dying ? RewindTape : ManualRewindTape();
        if (tape == null || tape.charges == 0 || TimeRewind.RecordedSeconds < 0.01f) return false;

        if (!dying) TimeRewind.RecordNow();
        BeginPlayback(tape, dying);
        stopAt = Mathf.Min(MaxRewind, Mathf.Clamp(steps, 1, tape.charges) * secondsPerCharge);
        return true;
    }

    /// <summary>사망 시 호출된다. 충전이 남은 테이프가 하나도 없으면 false를 반환한다.</summary>
    public bool OfferRewind()
    {
        if (InventoryOpen) CloseInventory();
        TimeRewind.RecordNow();
        // 쓰러지면: 끼운 테이프로 되감는다. 끼운 테이프에 횟수가 없으면 횟수가 남은 다른 테이프로(끼운 테이프는 그대로 — 노래가 이어진다)
        var tape = ManualRewindTape();
        if (tape == null || TimeRewind.RecordedSeconds <= 0.01f) return false;

        RewindTape = tape;
        phase = Phase.Prompt;
        RewoundSeconds = 0f;
        Time.timeScale = 0.1f;
        if (effect) effect.SetWeight(0.25f);
        return true;
    }

    void UpdatePrompt()
    {
        if (nextAction.WasPressedThisFrame()) CycleRewindTape(1);
        if (previousAction.WasPressedThisFrame()) CycleRewindTape(-1);

        if (rewindAction.WasPressedThisFrame()) BeginPlayback(RewindTape, true);
        else if (jumpAction.WasPressedThisFrame())
        {
            // 거절: 충전은 그대로 두고, 대신 마지막 세이브 지점으로 돌아간다.
            phase = Phase.Playing;
            Time.timeScale = 1f;
            if (effect) effect.SetWeight(0f);
            GameManager.Instance.RespawnFromCheckpoint();
        }
    }

    void CycleRewindTape(int direction)
    {
        int start = Mathf.Max(0, tapes.IndexOf(RewindTape));
        for (int step = 1; step <= tapes.Count; step++)
        {
            var tape = tapes[Wrap(start + direction * step)];
            if (tape.charges > 0) { RewindTape = Inserted = tape; return; }
        }
    }

    void BeginPlayback(CassetteTape tape, bool afterDeath)
    {
        StoryState.RewindAsked = false;
        RewindTape = tape;
        fromDeath = afterDeath;
        startClock = TimeRewind.Clock;
        MaxRewind = Mathf.Min(tape.charges * secondsPerCharge, TimeRewind.RecordedSeconds);
        RewoundSeconds = 0f;
        stopAt = -1f;
        phase = Phase.Playback;
        TimeRewind.PlayingBack = true;
        Time.timeScale = 0f;
        if (effect)
        {
            effect.StartWhir();
            effect.SetWeight(0.9f);
        }
    }

    void UpdatePlayback()
    {
        // 누르고 있으면: 계속 거꾸로 재생한다. 떼면: 다음 5초 지점에서 멈춘다.
        bool holding = rewindAction.IsPressed() || (rewindByQ && Keyboard.current != null && Keyboard.current.qKey.isPressed);
        if (stopAt < 0f && !holding)
            stopAt = Mathf.Min(MaxRewind, NotchesSoFar * secondsPerCharge);

        float dt = Time.unscaledDeltaTime;
        float limit = stopAt >= 0f ? stopAt : MaxRewind;
        RewoundSeconds = Mathf.Min(limit, RewoundSeconds + dt * playbackSpeed);
        ReelAngle -= 150f * playbackSpeed * dt;
        TimeRewind.Preview(TimeRewind.At(startClock - RewoundSeconds));

        if (stopAt >= 0f && RewoundSeconds >= stopAt) StartCoroutine(StopTape());
    }

    IEnumerator StopTape()
    {
        phase = Phase.Recover;
        rewindByQ = false;
        var tape = RewindTape;
        int cost = Mathf.Clamp(Mathf.CeilToInt(stopAt / secondsPerCharge - 0.001f), 1, tape.charges);
        bool practice = StoryState.FreeRewind; // 튜토리얼 연습: 횟수를 쓰지 않고, 재생용 테이프도 그대로 둔다
        if (!practice) tape.charges -= cost; // 횟수만 준다: 테이프는 계속 재생된다

        TimeRewind.RewindTo(TimeRewind.At(startClock - stopAt));
        TimeRewind.PlayingBack = false;
        if (effect) effect.StopWhir();

        yield return new WaitForSecondsRealtime(0.1f);
        yield return Fade(0.25f, 0f);
        Time.timeScale = 1f;
        if (fromDeath) health.GrantInvulnerability(invulnerableAfterDeathRewind);
        phase = Phase.Playing;
        if (!practice) PlayerController.MemoryStrikeUntil = Time.unscaledTime + 3f; // 기억의 일격: 3초 안의 첫 주먹은 세 배

        // 남은 횟수는 왼쪽 아래 칸으로 보인다(알림 없음)
    }

    IEnumerator Fade(float duration, float to)
    {
        if (!effect) yield break;
        float from = effect.Weight;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            effect.SetWeight(Mathf.Lerp(from, to, t));
            yield return null;
        }
        effect.SetWeight(to);
    }

    static void Announce(string message)
    {
        if (GameManager.Instance) GameManager.Instance.Notify(message);
    }
}
