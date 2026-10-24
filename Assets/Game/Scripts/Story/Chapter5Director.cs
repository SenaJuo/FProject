using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 5장 「30주년」 + 종장 감독 — 한결 FM 30주년 기념 방송의 밤, 한결 타워 지하.
///  구역 1 지하 주차장: 7번의 테이프(저장 — 3장에서 살린 사람) → 정리반 → 화물 엘리베이터
///  구역 2 지하 3층 연구소: 실험실 복도의 실험체들 → 격벽 → 마스터 테이프 홀(저장)
///  구역 3 홀: 유리 너머 윗선이 지켜본다 → 실험체 0번(보스, 절반에서 특수경비) → 마스터 테이프 → 생방송에 대니의 녹음
///         마스터 테이프 유리장(2026-10-23): 잠겨 있다. 손대면 회장이 위층 패널에 번호를 눌러 소각로로 떨어뜨린다 —
///         되감아 그 번호(1996, 개국한 해)로 먼저 연다. 번호를 본 건 레이뿐이다
///  종장: R 길게 = 마스터 테이프(원하는 만큼 감기는 테이프)로 일주일 전으로 되감는다(대니는 산다, 방송은 사라진다) / F = 테이프를 부순다(진실은 남는다).
/// </summary>
public class Chapter5Director : MonoBehaviour
{
    const string Ray = "레이";
    const string Seven = "7번";

    public PlayerController player;
    public CinemachineBrain brain;
    public CinemachineCamera gameplayCamera, dynamicA;
    public ThirdPersonCamera gameplayRig;
    public RewindEffect rewindEffect;

    [Header("구역 1 — 지하 주차장")]
    public Transform zoneStart, shopSave;
    public TapeShop shop;
    public StoryTrigger shopApproach;
    public CombatZone zoneGarage;
    public StoryInteractable liftUse;

    [Header("구역 2 — 연구소")]
    public Transform labEntry;
    public CombatZone zoneLab;
    public StoryInteractable hallDoor;

    [Header("구역 3 — 마스터 테이프 홀")]
    public Transform hallEntry, galleryShot, masterShot;
    public StoryTrigger hallTrigger;
    public EnemyAI zero;
    public GameObject[] guards = System.Array.Empty<GameObject>();
    public StoryInteractable masterUse, consoleUse;
    public Transform masterTape;

    public enum Phase { Opening, Garage, Lab, Hall, Boss, Master, Broadcast, Choice, EndingA, EndingB, End }
    public Phase CurrentPhase { get; private set; }

    Walkman walkman;
    bool shopGreeted, liftUsed, doorUsed, masterTaken, broadcast, zeroHalf;
    /// <summary>회장이 유리장 번호를 누르는 걸 봤다(되감아도 레이는 기억한다)</summary>
    public bool CaseCodeKnown { get; private set; }
    bool caseBurning, caseOpenedByCode;
    const string CaseCode = "1996";
    readonly List<CinemachineCamera> cutsceneCameras = new();

    const string CpStart = "c5_start", CpShop = "c5_shop", CpLab = "c5_lab", CpHall = "c5_hall";

    void Awake()
    {
        StoryState.ResetAll();
        if (dynamicA) cutsceneCameras.Add(dynamicA);
        foreach (var c in cutsceneCameras) c.Priority = 0;
        gameplayCamera.Priority = 10;
        brain.IgnoreTimeScale = true;
    }

    void Start()
    {
        CutsceneSkip.Ensure();
        Tutorial.Unlock();
        walkman = player.GetComponent<Walkman>();
        if (shopApproach) shopApproach.Entered += OnShopApproach;
        if (liftUse) liftUse.Used += () => { if (CurrentPhase == Phase.Garage && (!zoneGarage || zoneGarage.IsCleared)) liftUsed = true; else { StoryInteractable.Refuse(); StoryState.Say(Ray, "…놈들부터 정리한다.", 2f); } };
        if (hallDoor) hallDoor.Used += () => { if (CurrentPhase == Phase.Lab && (!zoneLab || zoneLab.IsCleared)) doorUsed = true; else { StoryInteractable.Refuse(); StoryState.Say(Ray, "…등 뒤를 비워 둘 순 없다.", 2f); } };
        if (masterUse)
        {
            masterUse.oneShot = false;
            masterUse.Used += OnMasterCase;
        }
        if (consoleUse) consoleUse.Used += () => { if (CurrentPhase == Phase.Broadcast) broadcast = true; else StoryInteractable.Refuse(); };
        if (zero) zero.GetComponent<Health>().Damaged += OnZeroHit;
        TapeShop.Opened += OnShopOpened;
        TapeShop.Traded += OnTraded;
        CombatZone.Entered += OnZoneEntered;
        GameManager.PlayerRespawned += OnRespawned;
        foreach (var g in guards) if (g) g.SetActive(false);

        var resume = SaveGame.TakePending(gameObject.scene.name);
        if (resume != null) SaveGame.ApplyInventory(resume, walkman);
        string cp = resume?.checkpoint;
        if (cp == CpHall) { StartCoroutine(Resume(hallEntry, Phase.Hall)); return; }
        if (cp == CpLab) { StartCoroutine(Resume(labEntry, Phase.Lab)); return; }
        if (cp == CpShop) { StartCoroutine(Resume(shopSave, Phase.Garage)); return; }
        Checkpoint(zoneStart, CpStart);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        TapeShop.Opened -= OnShopOpened;
        TapeShop.Traded -= OnTraded;
        CombatZone.Entered -= OnZoneEntered;
        GameManager.PlayerRespawned -= OnRespawned;
    }

    // ---------- 구역 1: 지하 주차장 ----------

    IEnumerator Run()
    {
        CurrentPhase = Phase.Opening;
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        StoryState.Title = "5장   30주년";
        yield return Wait(2.6f);
        StoryState.Title = null;
        yield return FadeTo(0f, 1.6f);
        StoryState.ControlLocked = false;
        StoryState.Say("", "(머리 위 어딘가에서 생방송이 시작된다 — '한결 FM, 서른 번째 밤입니다')", 3.6f);
        yield return Wait(3.8f);
        StoryState.Say(Ray, "…윗선은 지금 저 아래 있다. 테이프와 함께.", 2.8f);
        yield return FromGarage();
    }

    IEnumerator FromGarage()
    {
        CurrentPhase = Phase.Garage;
        StoryState.Objective = "지하 주차장을 지나 화물 엘리베이터로 간다";
        StoryState.Waypoint = liftUse ? liftUse.transform : null;
        yield return new WaitUntil(() => liftUsed);
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        StoryState.Say("", "(엘리베이터가 지하 3층으로 내려간다. 버튼 옆에 손으로 쓴 글씨 — '연구동')", 2.8f);
        yield return Wait(1.6f);
        yield return FadeTo(1f, 0.8f);
        if (labEntry) player.Teleport(labEntry.position, labEntry.rotation);
        gameplayRig.SnapBehindTarget();
        Checkpoint(labEntry, CpLab);
        yield return Wait(0.3f);
        yield return FadeTo(0f, 1f);
        StoryState.ControlLocked = false;
        yield return FromLab();
    }

    void OnZoneEntered(CombatZone zone)
    {
        if (zone == zoneGarage) StoryState.Say("정리반", "왔다! 회장님 근처에도 못 가게 해!", 2.2f);
        else if (zone == zoneLab) StoryState.Say("", "(실험실마다 무언가가 일어선다)", 2.2f);
    }

    // ---------- 구역 2: 연구소 ----------

    IEnumerator FromLab()
    {
        CurrentPhase = Phase.Lab;
        StoryState.Say(Ray, "…7번이 있던 곳보다 깊다.", 2.4f);
        StoryState.Objective = "연구소 끝의 격벽으로 간다";
        StoryState.Waypoint = hallDoor ? hallDoor.transform : null;
        yield return new WaitUntil(() => doorUsed);
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        StoryState.Say("", "(격벽이 무겁게 열린다. 안에서 낮은 웅웅거림 — 거대한 릴이 돈다)", 2.8f);
        yield return Wait(1.4f);
        yield return FadeTo(1f, 0.8f);
        if (hallEntry) player.Teleport(hallEntry.position, hallEntry.rotation);
        gameplayRig.SnapBehindTarget();
        Checkpoint(hallEntry, CpHall);
        yield return Wait(0.3f);
        yield return FadeTo(0f, 1f);
        StoryState.ControlLocked = false;
        yield return FromHall();
    }

    // ---------- 구역 3: 마스터 테이프 홀 ----------

    IEnumerator FromHall()
    {
        CurrentPhase = Phase.Hall;
        ResetZero();
        StoryState.Objective = "홀 가운데의 마스터 테이프로 간다";
        StoryState.Waypoint = masterTape;
        yield return new WaitUntil(() => Reached(hallTrigger));
        yield return BossIntro();
    }

    void ResetZero()
    {
        zeroHalf = false;
        if (!zero) return;
        zero.gameObject.SetActive(true);
        var h = zero.GetComponent<Health>();
        h.Revive();
        h.Invulnerable = true;
        zero.Deactivate();
        foreach (var g in guards) if (g) g.SetActive(false);
    }

    IEnumerator BossIntro()
    {
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (galleryShot) { Place(dynamicA, galleryShot.position, galleryShot.position + galleryShot.forward * 10f, 38f); Shot(dynamicA, 0.8f); }
        string[,] lines =
        {
            { "", "(위쪽 유리 너머 — 정장 차림의 사람들. 와인 잔. 방송 모니터)", "3.0" },
            { "회장", "…저 사람이군. 1분을 넘게 감았다는.", "2.8" },
            { "경찰청 차장", "죽은 줄 알았던 레이 형사. 파트너가 끝까지 못 막았군요.", "3.4" },
            { "국회의원", "상관없어요. 저 아이가 있으니까.", "2.6" },
            { "회장", "0번. 데려와. 살아 있으면 더 좋고.", "2.8" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            if (i == 4 && zero) { Place(dynamicA, zero.transform.position + zero.transform.forward * 3.4f + Vector3.up * 1.6f, zero.transform.position + Vector3.up * 1.5f, 36f); Shot(dynamicA, 0.6f); }
            yield return Wait(t + 0.2f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.5f);
        hp.Invulnerable = false;
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        CurrentPhase = Phase.Boss;
        StoryState.Objective = "실험체 0번을 쓰러뜨린다";
        if (zero) { zero.GetComponent<Health>().Invulnerable = false; zero.Activate(); }
        yield return new WaitUntil(() => !zero || zero.Health.IsDead);
        // 0번이 쓰러진 순간부터 다시 기록한다: 유리장 소각을 되감다가 0번이 살아나 다시 덤비지 않게(되감기는 이 뒤로만 간다)
        TimeRewind.Clear();
        foreach (var g in guards) if (g && g.activeSelf && g.TryGetComponent(out EnemyAI e) && !e.Health.IsDead) g.SetActive(false); // 0번이 쓰러지면 남은 경비들은 달아난다
        yield return Wait(1.2f);
        StoryState.Say(Ray, "…미안하다. 너도 누군가였을 텐데.", 2.6f);
        yield return Wait(2.8f);
        yield return TakeMaster();
    }

    void OnZeroHit(int amount, Vector3 from)
    {
        if (zeroHalf || CurrentPhase != Phase.Boss || !zero) return;
        var h = zero.GetComponent<Health>();
        if (h.Current > h.Max * 0.5f) return;
        zeroHalf = true;
        StoryState.Say("회장", "경비! 들어가!", 1.8f);
        foreach (var g in guards) if (g) { g.SetActive(true); if (g.TryGetComponent(out EnemyAI e)) e.Activate(); }
    }

    void Update()
    {
        // 0번이 쓰러지면 남은 경비는 달아났다: 되감기는 적을 늘 보이게 되돌리므로(EnemyAI) 다시 치운다
        if (CurrentPhase >= Phase.Master)
            foreach (var g in guards) if (g && g.activeSelf) g.SetActive(false);
    }

    void OnMasterCase()
    {
        if (CurrentPhase != Phase.Master || caseBurning || masterTaken) { StoryInteractable.Refuse(); return; }
        if (!CaseCodeKnown) { StartCoroutine(CaseBurns()); return; }
        if (!KeypadPanel.IsOpen) KeypadPanel.Open(CaseCode, () => { caseOpenedByCode = true; masterTaken = true; });
    }

    /// <summary>
    /// 처음 유리장에 손대면: 회장이 위층 패널에 번호를 누르고, 테이프가 소각로로 떨어진다 → 시간이 거의 멈추고 R을 기다린다.
    /// 되감기 횟수가 없어도 이번만은 감긴다(이야기를 이어 가야 한다 — 횟수는 쓰지 않는다)
    /// </summary>
    IEnumerator CaseBurns()
    {
        caseBurning = true;
        if (masterUse) masterUse.gameObject.SetActive(false); // 연출 중에는 F 안내를 띄우지 않는다
        StoryState.HoldTime = true; // 손댄 순간에서 기록을 멈춘다: 되감으면 그 5초 전으로
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        StoryState.Say("", "(유리장이 잠겨 있다. 옆에 번호판)", 2.0f);
        yield return Wait(1.6f);
        if (galleryShot) { Place(dynamicA, galleryShot.position, galleryShot.position + galleryShot.forward * 10f, 34f); Shot(dynamicA, 0.4f); }
        StoryState.Say("회장", "손대지 마! — 소각로 열어!", 1.8f);
        yield return Wait(1.6f);
        StoryState.Say("", "(위층 패널 — 삑 · 삑 · 삑 · 삑 — 1 · 9 · 9 · 6)", 2.4f);
        CaseCodeKnown = true;
        yield return Wait(2.2f);

        // 유리장 바닥이 열리고 테이프가 불 속으로
        Vector3 home = masterTape ? masterTape.position : player.transform.position;
        if (masterShot) { Place(dynamicA, masterShot.position, home + Vector3.down * 0.35f, 34f); Shot(dynamicA, 0.25f); }
        var fire = new GameObject("CaseFire").AddComponent<Light>();
        fire.type = LightType.Point;
        fire.color = new Color(1f, 0.45f, 0.12f);
        fire.range = 6f;
        fire.shadows = LightShadows.None;
        fire.transform.position = home + Vector3.down * 0.6f;
        for (float t = 0f; t < 1f; t += StoryState.DeltaTime / 0.5f)
        {
            if (masterTape) masterTape.position = Vector3.Lerp(home, home + Vector3.down * 0.9f, t * t);
            fire.intensity = Mathf.Lerp(0f, 18f, t);
            yield return null;
        }
        if (masterTape) masterTape.position = home + Vector3.down * 0.9f; // 프레임이 길어 중간에서 끝나도 끝 자리에
        fire.intensity = 18f;
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("BodyFall") ?? ProceduralSfx.ButtonClick, home, 0.6f, 0.7f, 0.6f);
        StoryState.Say(Ray, "안 돼—!", 1.4f);
        Time.timeScale = 0.05f;
        yield return StoryState.Sleep(0.5f);

        // 되감기: 횟수가 없으면 이번만 연습처럼(횟수를 쓰지 않는다)
        CassetteTape lent = null;
        if (walkman.CurrentCharges == 0)
        {
            lent = walkman.Inserted ?? (walkman.tapes.Count > 0 ? walkman.tapes[0] : null);
            if (lent != null) lent.charges = 1;
            StoryState.FreeRewind = true;
        }
        StoryState.CutsceneActive = false;
        if (walkman.CurrentCharges > 0)
        {
            StoryState.Objective = "되감아서 테이프가 떨어지기 전으로 돌아간다";
            StoryState.AskRewind("R 을 길게 눌러 되감는다");
            yield return new WaitUntil(() => walkman.Rewinding);
            StoryState.HoldTime = false;
            StoryState.Prompt = null;
            yield return new WaitUntil(() => !walkman.Rewinding);
        }
        else
        {
            // 테이프가 하나도 없다(빌려줄 횟수도 없다): 되감은 것으로 치고 넘어간다 — 멈춘 채 갇히지 않게
            StoryState.HoldTime = false;
            yield return FadeTo(1f, 0.4f);
            yield return FadeTo(0f, 0.6f);
        }
        Time.timeScale = 1f;
        if (lent != null) { lent.charges = 0; StoryState.FreeRewind = false; }
        if (masterTape) masterTape.position = home;
        if (masterUse) masterUse.gameObject.SetActive(true);
        Destroy(fire.gameObject);
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0f);
        hp.Invulnerable = false;
        StoryState.Objective = "마스터 테이프를 꺼낸다";
        StoryState.Waypoint = masterUse ? masterUse.transform : null;
        StoryState.Say(Ray, "…1996. 개국한 해. 놈이 누르기 전에 연다.", 3f);
        caseBurning = false;
    }

    IEnumerator TakeMaster()
    {
        CurrentPhase = Phase.Master;
        StoryState.Objective = "마스터 테이프를 꺼낸다";
        StoryState.Waypoint = masterUse ? masterUse.transform : null;
        yield return new WaitUntil(() => masterTaken);
        if (masterUse) masterUse.gameObject.SetActive(false);
        if (masterTape) masterTape.gameObject.SetActive(false);
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        if (masterShot) { Place(dynamicA, masterShot.position, masterShot.position + masterShot.forward * 5f, 34f); Shot(dynamicA, 0.6f); }
        if (caseOpenedByCode)
        {
            StoryState.Say("회장", "……그 번호를 어떻게?!", 2.2f);
            yield return Wait(2.4f);
            StoryState.Say(Ray, "방금 네가 눌렀잖아.", 2.2f);
            yield return Wait(2.4f);
        }
        string[,] lines =
        {
            { "", "(릴이 멈춘다. 손바닥만 한 카세트 하나 — 라벨도 없다)", "3.2" },
            { "회장", "그걸 내려놔. 그게 뭘 할 수 있는지 알기나 해?", "3.0" },
            { Ray, "알아. 원하는 만큼 되감는 테이프.", "2.6" },
            { "회장", "그럼 알겠군. 오늘 밤 우린 그걸로 지난 30년을 깨끗이 지울 참이었네.", "4.0" },
            { Ray, "…지우기 전에, 들려줄 게 있다.", "2.6" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.5f);
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        CurrentPhase = Phase.Broadcast;
        StoryState.Objective = "방송 콘솔에 대니의 녹음을 건다";
        StoryState.Waypoint = consoleUse ? consoleUse.transform : null;
        yield return new WaitUntil(() => broadcast);
        yield return Broadcast();
    }

    /// <summary>생방송에 대니의 녹음: 전국이 듣는다</summary>
    IEnumerator Broadcast()
    {
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (galleryShot) { Place(dynamicA, galleryShot.position, galleryShot.position + galleryShot.forward * 10f, 38f); Shot(dynamicA, 0.8f); }
        string[,] lines =
        {
            { "", "(콘솔의 '송출' 단추. 위층 기념 방송의 소리가 끊기고 — 잡음, 그리고 테이프가 돈다)", "3.6" },
            { "대니 (녹음)", "…레이. 이걸 듣고 있다면, 난 이미 늦은 거겠지.", "3.4" },
            { "", "(그 새벽 끝까지 듣지 못했던 테이프의 B면이 이어진다)", "3.0" },
            { "대니 (녹음)", "한결 레코드는 사람을 울려서 테이프를 찍는다. 돈은 경찰청과 국회로 간다.", "4.2" },
            { "대니 (녹음)", "날짜, 계좌, 이름. 전부 이 뒤에 읽어 둔다. 나도 그 안에 있어. …미안하다.", "4.4" },
            { "", "(유리 너머 — 와인 잔이 떨어진다. 모니터마다 같은 방송)", "3.0" },
            { "국회의원", "끄라고! 당장 꺼!", "1.8" },
            { "회장", "……이미 나갔어. 전국에.", "2.6" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        yield return Choice();
    }

    // ---------- 종장: 선택 ----------

    IEnumerator Choice()
    {
        CurrentPhase = Phase.Choice;
        StoryState.RewindLocked = true; // R은 이 테이프에만
        // 레이를 앞에서 비스듬히(상반신과 손이 함께 들어오게)
        Vector3 pf = player.transform.forward, pr = player.transform.right, pp = player.transform.position;
        Place(dynamicA, pp + pf * 2.6f + pr * 0.9f + Vector3.up * 1.55f, pp + Vector3.up * 1.35f, 40f);
        Shot(dynamicA, 0.8f);
        string[,] lines =
        {
            { "", "(손 안의 테이프. 원하는 만큼 감을 수 있다 — 대니가 살아 있던 그날 밤까지도)", "4.2" },
            { Ray, "…이걸 감으면 대니가 산다. 방금 나간 방송도, 저놈들이 잡혀갈 아침도 함께 사라진다.", "4.6" },
            { Ray, "부수면, 대니는 돌아오지 않는다. 대신 진실은 남는다.", "3.6" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        StoryState.SkipRequested = false;
        StoryState.Prompt = "R 길게: 일주일 전으로 되감는다   ·   F: 테이프를 부순다";
        float held = 0f;
        int chosen = 0;
        while (chosen == 0)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                held = kb.rKey.isPressed ? held + Time.unscaledDeltaTime : 0f;
                if (held >= 1.2f) chosen = 1;
                else if (kb.fKey.wasPressedThisFrame) chosen = 2;
            }
            if (ForcedChoice != 0) chosen = ForcedChoice;
            yield return null;
        }
        StoryState.Prompt = null;
        yield return chosen == 1 ? EndingRewind() : EndingBreak();
    }

    /// <summary>시험용: 1 = 되감기, 2 = 부수기</summary>
    public static int ForcedChoice;

    /// <summary>엔딩 A — 다시, A면: 일주일 전 그 밤까지 되감는다. 하얗게 감긴 끝에서 일주일 전 그날 밤으로(EndingStage가 프롤로그 장면에서 찍는다)</summary>
    IEnumerator EndingRewind()
    {
        CurrentPhase = Phase.EndingA;
        if (rewindEffect) { rewindEffect.StartWhir(); rewindEffect.SetWeight(1f); }
        StoryState.Say("", "(단추를 누른 채, 일주일 전 그 밤까지 감는다. 테이프가 비명처럼 감긴다)", 2.8f);
        StoryState.FadeColor = Color.white;
        yield return FadeTo(1f, 3.2f);
        if (rewindEffect) { rewindEffect.StopWhir(); rewindEffect.SetWeight(0f); }
        StoryState.Fade = 1f;
        yield return StoryState.Sleep(0.8f);
        CurrentPhase = Phase.End;
        EndingStage.Play(EndingStage.Kind.Rewind);
    }

    /// <summary>엔딩 B — 끝난 테이프: 부순다. 대니는 돌아오지 않고, 진실은 남는다(EndingStage가 1장 부두에서 찍는다)</summary>
    IEnumerator EndingBreak()
    {
        CurrentPhase = Phase.EndingB;
        StoryState.Say("", "(테이프를 바닥에 내리친다. 플라스틱이 갈라지고, 갈색 테이프가 엉켜 쏟아진다)", 3f);
        ProceduralSfx.PlayAt(ProceduralSfx.PlasticHit, player.transform.position, 1.2f, 0f, 0.8f);
        yield return StoryState.Sleep(3.2f);
        StoryState.FadeColor = Color.black;
        yield return FadeTo(1f, 2f);
        yield return StoryState.Sleep(0.6f);
        CurrentPhase = Phase.End;
        EndingStage.Play(EndingStage.Kind.Break);
    }

    void OnRespawned()
    {
        if (CurrentPhase != Phase.Boss) return;
        StopAllCoroutines();
        StoryState.CutsceneActive = false;
        StartCoroutine(FromHall());
    }

    // ---------- 가게(7번의 테이프) ----------

    void OnShopApproach()
    {
        Checkpoint(shopSave, CpShop);
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…7번?", 1.6f);
    }

    void OnShopOpened(TapeShop opened)
    {
        if (opened != shop) return;
        Checkpoint(shopSave, CpShop);
        if (shopGreeted) return;
        shopGreeted = true;
        StoryState.Say(Seven, "…올 줄 알았어요. 다른 사람들이 숨겨 준 테이프예요. 저 아래, 0번이 있어요. 조심해요.", 5f);
    }

    void OnTraded(TapeShop at, CassetteTape given, CassetteTape received)
    {
        if (at == shop) SaveGame.Write(CurrentPhase >= Phase.Lab ? CpLab : CpShop, walkman);
    }

    // ---------- 저장 · 이어하기 ----------

    void Checkpoint(Transform point, string id)
    {
        if (!point) return;
        if (GameManager.Instance) GameManager.Instance.Save(point);
        SaveGame.Write(id, walkman);
    }

    IEnumerator Resume(Transform at, Phase phase)
    {
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        if (phase >= Phase.Lab) liftUsed = true;
        if (phase >= Phase.Hall) doorUsed = true;
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at);
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        if (phase == Phase.Hall) yield return FromHall();
        else if (phase == Phase.Lab) yield return FromLab();
        else yield return FromGarage();
    }

    // ---------- 도우미 ----------

    bool Reached(StoryTrigger trigger) =>
        !trigger || trigger.Fired || (trigger.enabled && trigger.TryGetComponent(out Collider c) && c.bounds.Contains(player.transform.position));

    static void Place(CinemachineCamera cam, Vector3 position, Vector3 lookAt, float fov)
    {
        cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
        cam.Lens.FieldOfView = fov;
    }

    void Shot(CinemachineCamera cam, float blend)
    {
        brain.DefaultBlend = blend <= 0f
            ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
            : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blend);
        foreach (var c in cutsceneCameras) c.Priority = c == cam ? 20 : 0;
        gameplayCamera.Priority = cam == gameplayCamera ? 20 : 10;
    }

    static IEnumerator Wait(float seconds)
    {
        float until = StoryState.Now + seconds;
        while (StoryState.Now < until && !StoryState.SkipRequested) yield return null;
    }

    static IEnumerator FadeTo(float target, float seconds)
    {
        float start = StoryState.Fade, t = 0f;
        while (t < seconds && !StoryState.SkipRequested)
        {
            t += StoryState.DeltaTime;
            StoryState.Fade = Mathf.Lerp(start, target, t / seconds);
            yield return null;
        }
        StoryState.Fade = target;
    }
}
