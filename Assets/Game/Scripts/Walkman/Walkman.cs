using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 주인공의 카세트 플레이어. 어떤 테이프든 넣을 수 있고, 재생 가능한 테이프는 재생되며 트랙을 하나 끝낼 때마다
/// 되감기 1회가 충전된다(최대 3). R을 누르고 있으면 Walkman에 든 테이프로 되감는다: 월드가 VCR처럼
/// 거꾸로 재생되고, 손을 떼면 다음 5초 지점에서 멈춘다 — 5초를 되돌릴 때마다 충전 1회를 쓴다.
/// </summary>
[RequireComponent(typeof(Health))]
public class Walkman : MonoBehaviour
{
    public const int InventoryColumns = 5;

    public List<CassetteTape> tapes = new();
    [Tooltip("되감기 1회가 되돌리는 시간(초)")]
    public float secondsPerCharge = 5f;
    [Tooltip("역재생이 실제 시간보다 몇 배 빠른지")]
    public float playbackSpeed = 10f;
    [Tooltip("죽음에서 되감은 직후 무적 시간 — 반응하기도 전에 다시 죽지 않게")]
    public float invulnerableAfterDeathRewind = 1.5f;

    [Header("회복약 (1)")]
    public const int MaxPotions = 5;
    [Tooltip("가진 회복약. 테이프처럼 되감아도 줄거나 늘지 않는다(가방 속 물건)")]
    public int potions;
    public int potionHeal = 25;

    [Header("음악")]
    [Range(0f, 1f)] public float musicVolume = 0.42f;

    enum Phase { Playing, Prompt, Playback, Recover }

    /// <summary>Walkman에 든 테이프: (재생 가능하면) 재생되고, R로 되감을 때 이 테이프를 쓴다.</summary>
    public CassetteTape Inserted { get; private set; }
    /// <summary>현재 되감기(또는 사망 화면)가 사용 중인 테이프.</summary>
    public CassetteTape RewindTape { get; private set; }
    public bool PromptOpen => phase == Phase.Prompt;
    public bool PlayingBack => phase == Phase.Playback;
    public bool Rewinding => phase is Phase.Playback or Phase.Recover;
    public bool InventoryOpen { get; private set; }
    public int InventorySelected { get; private set; }
    public float RewoundSeconds { get; private set; }
    public float MaxRewind { get; private set; }
    public int NotchesSoFar => Mathf.Max(1, Mathf.CeilToInt(RewoundSeconds / secondsPerCharge - 0.001f));
    public int RewindableTapeCount => tapes.FindAll(t => t.charges > 0).Count;
    /// <summary>가진 테이프 전부의 남은 되감기 횟수(R은 끼운 테이프가 비면 다른 테이프로 되감는다)</summary>
    public int TotalCharges { get { int n = 0; foreach (var t in tapes) n += t.charges; return n; } }
    public int SpentTapeCount => tapes.FindAll(t => t.IsSpent).Count;
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
        }
        if (InventoryOpen) { UpdateInventory(); return; }
        if (health.IsDead || TapeShop.Current) return;

        // 컷신 중이거나 아직 되감기 능력을 모르는 구간(프롤로그 첫 회차)에는 조작을 받지 않는다. 녹화와 재생은 계속된다.
        bool locked = StoryState.InputLocked || StoryState.RewindLocked;
        // 연출로 시간을 붙잡은 동안(1장 강제 되감기)에는 열지 않는다: 닫을 때 시간이 정상 속도로 풀려 버렸다
        if (!locked && !StoryState.HoldTime && inventoryAction.WasPressedThisFrame()) { OpenInventory(); return; }
        if (!locked && rewindAction.WasPressedThisFrame()) { TryRewindNow(); return; }
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
        int finishedBefore = tape.CompletedTracks;
        tape.position = Mathf.Min(tape.position + deltaTime, tape.Length);
        if (tape.CompletedTracks > finishedBefore && tape.charges < CassetteTape.MaxCharges)
        {
            tape.charges++;
            Announce($"♪ {tape.trackTitles[finishedBefore]} 끝 — 되감기 {tape.charges}회 충전");
        }
    }

    // ---------- 테이프 ----------

    public void AddTape(CassetteTape tape)
    {
        tapes.Add(tape);
        // Walkman이 비어 있거나 재생할 수 없는 테이프가 들어 있으면, 재생 가능한 테이프를 바로 넣는다.
        if (Inserted == null || (tape.CanPlay && !Inserted.CanPlay)) Inserted = tape;
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

    /// <summary><paramref name="index"/> 위치의 테이프를 Walkman에 넣는다.</summary>
    public void Equip(int index)
    {
        if (index < 0 || index >= tapes.Count) return;
        var tape = tapes[index];
        if (tape == Inserted) return;
        Inserted = tape;
        Announce(tape.CanPlay ? $"카세트 플레이어에 끼움: {tape.title} ▶ 재생"
            : $"카세트 플레이어에 끼움: {tape.title} — 재생은 안 되고 R 되감기에 쓸 수 있다 ({tape.charges}회)");
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
        InventoryOpen = true;
        InventorySelected = Mathf.Max(0, tapes.IndexOf(Inserted));
        navigationHeld = Vector2Int.one;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CloseInventory()
    {
        InventoryOpen = false;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UpdateInventory()
    {
        if (inventoryAction.WasPressedThisFrame() || closeAction.WasPressedThisFrame()) { CloseInventory(); return; }
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
        Announce($"회복약: 체력 +{gained}   (남은 회복약 {potions}개)");
    }

    // ---------- 음악(이어폰) ----------

    /// <summary>
    /// 끼운 재생용 테이프의 지금 곡을 테이프 위치에 맞춰 튼다. 테이프가 멈춰 있으면(주머니 속·컷신의 시간 붙잡기·되감기·
    /// 메뉴·사망) 음악도 멈춘다. 되감는 데 쓴 재생용 테이프는 다시 소리가 나지 않는다(규칙).
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
        if (tape != loadedTape || tape.CurrentTrack != loadedTrack) // 곡이 바뀔 때만 찾는다
        {
            loadedTape = tape;
            loadedTrack = tape.CurrentTrack;
            loadedClip = Resources.Load<AudioClip>($"Music/{tape.musicId}-{loadedTrack + 1}");
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
        music.volume = Mathf.Min(1f, musicVolume * gain * fade);
    }

    // ---------- 되감기 ----------

    void TryRewindNow()
    {
        var tape = ManualRewindTape();
        if (tape == null) { Announce(tapes.Count == 0 ? "카세트 플레이어에 테이프가 없다" : "되감을 수 있는 테이프가 없다"); return; }
        if (TimeRewind.RecordedSeconds < 0.2f) { Announce("아직 되감을 시간이 없다"); return; }
        TimeRewind.RecordNow();
        BeginPlayback(tape, false);
    }

    /// <summary>
    /// R로 되감을 테이프: 끼워 둔 테이프에 횟수가 남았으면 그것(고른 테이프를 존중한다).
    /// 없으면 횟수가 남은 다른 테이프를 대신 쓴다 — 급할 때 R이 안 먹히지 않게. 이때는 듣는 음악이 끊기지 않도록
    /// 재생이 안 되는 테이프(되감기 전용·이미 되감은 테이프)를 먼저 쓰고, 재생용 테이프는 마지막에 쓴다.
    /// </summary>
    /// <summary>지금 R을 누르면 쓰일 테이프(없으면 null). 인벤토리 안내에 쓴다</summary>
    public CassetteTape NextManualRewindTape => ManualRewindTape();

    /// <summary>저장 지점에서 다시 시작할 때: 사망 화면·되감기 도중이면 모두 거두고 정상 시간으로 돌아간다</summary>
    public void ResetForRespawn()
    {
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

    CassetteTape ManualRewindTape()
    {
        if (Inserted != null && Inserted.charges > 0) return Inserted;
        return tapes.Find(t => t.charges > 0 && !t.CanPlay) ?? tapes.Find(t => t.charges > 0);
    }

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
        var tape = Inserted != null && Inserted.charges > 0 ? Inserted : tapes.Find(t => t.charges > 0);
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
            if (tape.charges > 0) { RewindTape = tape; return; }
        }
    }

    void BeginPlayback(CassetteTape tape, bool afterDeath)
    {
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
        if (stopAt < 0f && !rewindAction.IsPressed())
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
        var tape = RewindTape;
        int cost = Mathf.Clamp(Mathf.CeilToInt(stopAt / secondsPerCharge - 0.001f), 1, tape.charges);
        bool practice = StoryState.FreeRewind; // 튜토리얼 연습: 횟수를 쓰지 않고, 재생용 테이프도 그대로 둔다
        if (!practice)
        {
            tape.charges -= cost;
            if (tape.playable) tape.rewound = true;
        }

        TimeRewind.RewindTo(TimeRewind.At(startClock - stopAt));
        TimeRewind.PlayingBack = false;
        if (effect) effect.StopWhir();

        yield return new WaitForSecondsRealtime(0.1f);
        yield return Fade(0.25f, 0f);
        Time.timeScale = 1f;
        if (fromDeath) health.GrantInvulnerability(invulnerableAfterDeathRewind);
        phase = Phase.Playing;

        Announce($"남은 횟수 : {TotalCharges}번"); // 되감은 뒤에는 (가진 테이프 전부의) 남은 횟수만 짧게 알린다(연습이면 줄지 않은 횟수)
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
        if (GameManager.Instance) GameManager.Instance.ShowMessage(message, 2.5f);
    }
}
