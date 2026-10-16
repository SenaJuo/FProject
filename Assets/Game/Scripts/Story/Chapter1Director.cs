using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 1장 「B면」 진행. 구역 1(대니네 동네 → 근린공원 → 큰길의 가게 1) → 구역 2(시장 광장).
///  1) 대니 집 뒷마당에서 시작(구역 시작점 자동 저장). 손에는 프롤로그에서 되감는 데 쓴 믹스테이프(재생 불가, 되감기 1회).
///  2) 공원 입구 화장실 모퉁이에서 손전등에 걸린다 → 화면이 느려지고 R을 누를 때까지 기다린다 → 실전 첫 되감기(믹스테이프의 마지막 1회).
///     되감고 나면 그 조직원은 모퉁이를 오가며 순찰한다. 이번에는 숨어서 지나간다.
///  3) 공원 관리 도로의 검은 차 옆에서 통화를 엿듣는다(대니가 숨긴 것, 정리반). 차 번호판은 경찰 업무용이다. 차 옆 상자에 상표 없는 테이프.
///  4) 공원 출구를 지키는 세 명과는 피할 수 없는 싸움.
///  5) 큰길 모퉁이의 「심야 레코드」: 저장 지점. 다 쓴 믹스테이프를 재생용 테이프로 바꾼다.
///  6) 큰길 북쪽 끝: 경찰 검문소가 조직의 검은 승합차를 그냥 통과시키는 것을 엿본다(컷신) → 구역 2 시작점 자동 저장.
///  7) 검문소: 방패 경찰(정면 공격을 막는다 — 돌려차기로 쳐내거나 옆·뒤로) + 손전등 경찰.
///  8) 시장 광장에 들어서면 동쪽 옥상의 저격수가 붉은 조준선으로 겨눈다 → 엄폐물(노점 방수포·트럭·팔레트) 사이로 골목 철계단까지.
///  9) 옥상에서 저격수를 제압하고 심문한다: 정리반은 항구 3번 부두, 새벽 다섯 시 배. "위에서 다 안다".
/// 10) 북쪽 항구 길로 가면 (이어서 계속).
/// </summary>
public class Chapter1Director : MonoBehaviour
{
    const string Ray = "레이";
    const string Thug = "조직원";

    [Header("인물·카메라")]
    public PlayerController player;
    public Transform pocketWalkman;
    public CinemachineBrain brain;
    public CinemachineCamera gameplayCamera;
    public ThirdPersonCamera gameplayRig;
    public CinemachineCamera dynamicA, dynamicB;
    public AudioSource rainAudio;

    [Header("구역 1")]
    [Tooltip("구역 1 시작점(대니 집 뒷마당) — 자동 저장")]
    public Transform zoneStart;
    public Transform parkExit;
    [Tooltip("공원 입구 통로: 여기서 손전등에 걸린다")]
    public StoryTrigger spotTrigger;
    public PatrolSight spotter;
    [Tooltip("모퉁이에서 한 걸음 나와 레이를 비추는 자리")]
    public Transform spotterStep;
    public StoryTrigger callTrigger;
    public PatrolSight caller;
    public StoryTrigger plateTrigger;
    public TapePickup vanTape;
    public CombatZone exitZone;
    public EnemyAI exitGunner;

    [Header("가게 1")]
    public TapeShop shop;
    public StoryTrigger shopApproach;
    public Transform shopSave;
    [Tooltip("큰길 북쪽: 검문소가 보이는 자리(구역 1의 끝)")]
    public Transform northEnd;
    public StoryTrigger endTrigger;

    [Header("구역 2 · 시장 광장")]
    [Tooltip("구역 2 시작점(검문소를 엿보는 차 뒤) — 자동 저장")]
    public Transform zone2Start;
    public StoryTrigger checkpointView;
    public Transform checkpointCam, checkpointLook;
    public GameObject van;
    public Transform[] vanPath;
    public Transform gate, gateOpen;
    public CombatZone checkpointZone;
    public StoryTrigger squareTrigger;
    [Tooltip("광장(옥상 포함): 저격수가 레이를 노리는 범위")]
    public Bounds square;
    public EnemyAI sniper;
    public Transform stairsFoot, roofTop;
    public CombatZone roofZone;
    public StoryTrigger harborTrigger;
    public Transform harborEnd;

    [Header("가게 2 · 구역 3 (항구)")]
    public TapeShop shop2;
    public StoryTrigger shop2Approach;
    public Transform shop2Save;
    public Transform gateLeaf;
    public Vector3 gateOpenPos;
    public StoryInteractable gateUse;
    public Transform zone3Start, pier;
    public StoryTrigger pierTrigger;
    public Searchlight searchlight;
    public CombatZone dockZone, pierZone;
    [Header("3번 부두 · 반장")]
    public EnemyAI boss;
    public BossCleaner bossBrain;
    public EnemyAI[] pierThugs = System.Array.Empty<EnemyAI>();
    public Transform burnBarrel, dawnSeat;
    public FireFlicker barrelFire;
    public SkyCycle skyCycle;
    const string Boss = "정리반 반장", Danny2 = "대니 (녹음)";
    bool shop2Greeted, shop2Traded, gateUsed;

    readonly List<CinemachineCamera> cutsceneCameras = new();
    Walkman walkman;
    bool callDone, plateSeen, tapeLineDone, shopGreeted, traded;

    public enum Phase { Opening, Sneak, ForcedRewind, Park, ExitFight, Avenue, CheckpointScene, Checkpoint, Square, Interrogation, Harbor, Docks, BossIntro, Boss, Burn, Finale, End }
    public Phase CurrentPhase { get; private set; }

    void Awake()
    {
        StoryState.ResetAll();
        if (dynamicA) cutsceneCameras.Add(dynamicA);
        if (dynamicB) cutsceneCameras.Add(dynamicB);
        foreach (var c in cutsceneCameras) c.Priority = 0;
        gameplayCamera.Priority = 10;
        brain.IgnoreTimeScale = true; // 슬로모션·역재생 중에도 카메라 전환은 실제 시간으로 흐른다
    }

    void Start()
    {
        CutsceneSkip.Ensure();
        Tutorial.Unlock(); // 편지 뒷면은 프롤로그에서 읽었다(1장부터 바로 시작해도 설명이 뜨게)
        if (pocketWalkman) pocketWalkman.gameObject.SetActive(false);

        // 프롤로그에서 넘어온 테이프: 레이 집에 굴러다니던 믹스테이프. 죽으며 되감는 데 써서 재생은 안 되고, 되감기 1회가 남았다
        walkman = player.GetComponent<Walkman>();
        if (walkman && walkman.tapes.Count > 0)
        {
            var mix = walkman.tapes[0];
            mix.title = "오래된 믹스테이프";
            mix.trackTitles = new[] { "A면 1곡", "A면 2곡", "A면 3곡" };
            mix.charges = 1;
            mix.rewound = true;
            mix.position = 0f;
        }

        if (rainAudio)
        {
            rainAudio.clip = ProceduralSfx.RainLoop;
            rainAudio.loop = true;
            rainAudio.Play();
        }

        // 손전등에 걸리기 전까지 그 조직원은 화장실 건물 뒤에 서 있다(아직 아무도 보지 못한다)
        spotter.Holding = true;
        spotter.CanDetect = false;
        caller.Holding = true;

        callTrigger.Entered += () => StartCoroutine(PhoneCall());
        plateTrigger.Entered += () => plateSeen = true;
        shopApproach.Entered += OnShopApproach;
        if (shop2Approach) shop2Approach.Entered += OnShop2Approach;
        if (gateUse) gateUse.Used += () => gateUsed = true;
        TapeShop.Opened += OnShopOpened;
        TapeShop.Traded += OnTraded;
        CombatZone.Entered += OnZoneEntered;
        ShieldGuard.FirstBlocked += OnFirstBlocked;
        if (exitGunner) exitGunner.FirstAim = () => Tutorial.Show("aim");
        if (gate) { gateClosedPos = gate.position; gateClosedRot = gate.rotation; }

        // 타이틀 '이어하기'로 왔으면 저장 지점부터, 아니면 처음부터
        var resume = SaveGame.TakePending(gameObject.scene.name);
        if (resume != null && resume.checkpoint != CpStart)
        {
            SaveGame.ApplyInventory(resume, walkman);
            StartCoroutine(Resume(resume.checkpoint));
            return;
        }
        if (resume != null) SaveGame.ApplyInventory(resume, walkman);
        Checkpoint(zoneStart, CpStart);
        StartCoroutine(Run());
    }

    // ---------- 진행 저장 ----------

    const string CpStart = "c1_start", CpShop1 = "c1_shop1", CpZone2 = "c1_zone2", CpShop2 = "c1_shop2", CpZone3 = "c1_zone3";

    /// <summary>저장 지점: 쓰러지면 여기서 다시 시작하고(체력 회복), 파일에도 기록한다(타이틀 '이어하기')</summary>
    void Checkpoint(Transform point, string id)
    {
        if (!point) return;
        if (GameManager.Instance) GameManager.Instance.Save(point);
        SaveGame.Write(id, walkman);
    }

    /// <summary>
    /// 이어하기: 저장 지점으로 옮기고 지나온 구역의 적을 치운 뒤, 그 지점부터 진행을 잇는다.
    /// (테이프·회복약·주운 자리·가게는 SaveGame.ApplyInventory가 이미 되돌렸다)
    /// </summary>
    IEnumerator Resume(string id)
    {
        Arm(endTrigger, false);
        Arm(squareTrigger, false);
        Arm(harborTrigger, false);
        Arm(pierTrigger, false);
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        spotter.CanDetect = false;
        Transform at; float behindZ; IEnumerator next;
        switch (id)
        {
            // 단계를 옮기기 전에 먼저 맞춘다: 가게 앞에 서자마자 가게 저장이 다시 일어나는데, 단계가 처음(Opening)이면 '1장 처음'으로 덮어썼다
            case CpShop1: at = shopSave; behindZ = 70f; CurrentPhase = Phase.Avenue; next = FromAvenue(); break;
            case CpZone2: at = zone2Start; behindZ = 98f; CurrentPhase = Phase.Avenue; next = FromCheckpointScene(); break;
            case CpShop2: at = shop2Save; behindZ = 225f; CurrentPhase = Phase.Harbor; next = HarborFromGate(); break;
            default: // CpZone3
                at = zone3Start; behindZ = 250f; CurrentPhase = Phase.Docks; next = FromDocks();
                if (gateLeaf) gateLeaf.position = gateOpenPos; // 철조망 문은 이미 열었다
                if (gateUse) gateUse.gameObject.SetActive(false);
                break;
        }
        // 지나온 구역의 적은 이미 처리했다(앞으로 갈 길만 남는다)
        foreach (var e in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            if (e.transform.position.z < behindZ) e.gameObject.SetActive(false);
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at); // 쓰러지면 여기서 다시
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        yield return next;
    }

    void OnDestroy()
    {
        GameManager.PlayerRespawned -= OnLastStandRespawn; // 마지막 싸움 도중 타이틀로 나가도 남지 않게
        TapeShop.Opened -= OnShopOpened;
        TapeShop.Traded -= OnTraded;
        CombatZone.Entered -= OnZoneEntered;
        ShieldGuard.FirstBlocked -= OnFirstBlocked;
    }

    void OnFirstBlocked(ShieldGuard _)
    {
        Tutorial.Show("shield");
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…방패. 정면으론 안 돼.", 2.2f);
    }

    float rainBase = -1f, vanBase = -1f;
    AudioSource vanAudio;

    /// <summary>옵션의 소리 크기를 계속 깔리는 소리(비·승합차)에 적용한다</summary>
    void ApplyVolumes()
    {
        if (rainAudio)
        {
            if (rainBase < 0f) rainBase = rainAudio.volume;
            rainAudio.volume = rainBase * GameSettings.Gain(GameSettings.Channel.Sfx);
        }
        if (van && !vanAudio) vanAudio = van.GetComponent<AudioSource>();
        if (vanAudio)
        {
            if (vanBase < 0f) vanBase = vanAudio.volume;
            vanAudio.volume = vanBase * GameSettings.Gain(GameSettings.Channel.Sfx);
        }
    }

    void Update()
    {
        ApplyVolumes();
        UpdateZone2();
        // 반장 싸움 중 쓰러져 구역 시작점에서 다시 시작하면 반장은 제자리에서 기다린다: 어디로 가야 하는지 길잡이를 띄운다
        if (CurrentPhase == Phase.Boss && boss && !boss.Health.IsDead)
        {
            var want = boss.IsActive ? null : boss.transform;
            if (StoryState.Waypoint != want) StoryState.Waypoint = want;
        }
        // 차 번호판·테이프 한마디: 다른 대사가 없을 때 끼워 넣는다
        if (StoryState.LineVisible || StoryState.InputLocked) return;
        if (plateSeen && callDone)
        {
            plateSeen = false;
            StoryState.Say(Ray, "저 번호판… 경찰 업무용 차야. 경찰 차가 왜 놈들이랑…", 3.6f);
        }
        else if (!tapeLineDone && vanTape && !vanTape.gameObject.activeSelf)
        {
            tapeLineDone = true;
            StoryState.Say(Ray, "상표도 없는 테이프… 놈들이 쓰는 물건인가.", 3.2f);
        }
    }

    // ---------- 진행 ----------

    IEnumerator Run()
    {
        CurrentPhase = Phase.Opening;
        // 다음 장면으로 넘기는 자리는 그 단계가 되어서야 센다: 싸우다 미리 밟으면 가게·큰길 구간이 통째로 건너뛰어졌다
        Arm(endTrigger, false);
        Arm(squareTrigger, false);
        Arm(harborTrigger, false);
        Arm(pierTrigger, false);
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        StoryState.Title = "1장   B면";
        yield return Wait(2.6f);
        StoryState.Title = null;
        yield return FadeTo(0f, 1.6f);
        StoryState.ControlLocked = false;
        StoryState.Say(Ray, "…앞길은 놈들 차로 막혔을 거야. 뒤쪽 공원으로 빠져나가자.", 3.4f);
        StoryState.Objective = "조직원들을 피해 공원을 지나 큰길로 빠져나간다";
        StoryState.Waypoint = parkExit;

        CurrentPhase = Phase.Sneak;
        yield return new WaitUntil(() => spotTrigger.Fired);
        yield return ForcedRewind();

        CurrentPhase = Phase.Park;
        yield return new WaitUntil(() => exitZone.IsCleared);
        yield return FromAvenue();
    }

    /// <summary>큰길(가게 1) → 구역 2. 이어하기(가게 1)도 여기서 시작한다</summary>
    IEnumerator FromAvenue()
    {
        CurrentPhase = Phase.Avenue;
        yield return Wait(0.8f);
        StoryState.Say(Ray, "…하아. 놈들 차가 시내 쪽으로 빠졌어. 따라가 보자.", 3.2f);
        StoryState.Objective = "큰길을 따라 시내 쪽으로 간다";
        StoryState.Waypoint = northEnd;
        Arm(endTrigger, true);

        yield return new WaitUntil(() => Reached(endTrigger));
        yield return FromCheckpointScene();
    }

    /// <summary>구역 2: 검문소 엿보기 → 광장 → 저격수 → 심문 → 항구 길. 이어하기(구역 2)도 여기서 시작한다</summary>
    IEnumerator FromCheckpointScene()
    {
        yield return CheckpointScene();
        CurrentPhase = Phase.Checkpoint;
        Checkpoint(zone2Start, CpZone2); // 구역 시작점 자동 저장
        StoryState.Objective = "검문소를 지나 시장 광장으로 들어간다";
        StoryState.Waypoint = checkpointLook;
        // 광장 입구는 검문소를 정리한 뒤에야 센다: 경찰을 남겨 둔 채 빠져나가면 저격수 구간까지 경찰이 따라와 끝없이 쓰러졌다
        yield return new WaitUntil(() => !checkpointZone || checkpointZone.IsCleared);
        Arm(squareTrigger, true);

        yield return new WaitUntil(() => Reached(squareTrigger));
        yield return SniperIntro();
        CurrentPhase = Phase.Square;
        StoryState.Objective = "엄폐물 사이로 다가가 옥상의 저격수를 처리한다";

        yield return new WaitUntil(() => sniper == null || sniper.Health.IsDead);
        yield return Interrogation();
        yield return HarborFromGate();
    }

    /// <summary>항구 길(가게 2) → 철조망 문 → 구역 3 3번 부두 → 반장 → 새벽. 시험이 여기부터 바로 시작할 수 있게 따로 둔다</summary>
    IEnumerator HarborFromGate()
    {
        CurrentPhase = Phase.Harbor;
        StoryState.Objective = "북쪽 항구로 간다";
        StoryState.Waypoint = harborEnd;
        yield return new WaitUntil(() => gateUsed || !gateUse);
        yield return OpenHarborGate();
        yield return FromDocks();
    }

    /// <summary>구역 3: 3번 부두 → 반장 → 새벽. 이어하기(구역 3)도 여기서 시작한다</summary>
    IEnumerator FromDocks()
    {
        CurrentPhase = Phase.Docks;
        if (zone3Start) Checkpoint(zone3Start, CpZone3); // 구역 시작점 자동 저장
        StoryState.Say(Ray, "3번 부두… 새벽 다섯 시 배. 그 전에 끝낸다.", 3.2f);
        StoryState.Objective = "3번 부두로 간다";
        StoryState.Waypoint = pier;
        Arm(pierTrigger, true);
        yield return new WaitUntil(() => Reached(pierTrigger));
        if (boss)
        {
            // 부두 큰길 싸움 등에서 쫓아온 조직원이 남아 있으면 먼저 처리한다(조작이 잠긴 소개 장면 동안 얻어맞지 않게)
            while (ChasersNearby())
            {
                StoryState.Objective = "주변의 조직원을 처리한다";
                StoryState.Waypoint = null;
                yield return new WaitUntil(() => !ChasersNearby());
            }
            yield return BossIntro();
            CurrentPhase = Phase.Boss;
            StoryState.Objective = "정리반 반장을 쓰러뜨린다";
            StoryState.Waypoint = null;
            boss.Activate();
            foreach (var t in pierThugs) if (t && !t.Health.IsDead) t.Activate();
            yield return new WaitUntil(() => !boss || boss.Health.IsDead || (bossBrain && bossBrain.WantsToBurn));
            yield return BurnSequence();
            yield return LastStand();
            yield return Finale();
        }
        CurrentPhase = Phase.End;
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        yield return FadeTo(1f, 1.4f);
        yield return StoryState.Sleep(2f);
        // 2장으로: 테이프·회복약을 그대로 들고 간다
        SaveGame.CarryOver("Chapter2", "c2_start", walkman);
        SceneLoader.Load("Chapter2");
    }

    // ---------- 구역 2 ----------

    Vector3 gateClosedPos;
    Quaternion gateClosedRot;
    Coroutine vanRoutine;

    /// <summary>
    /// 검문소 엿보기: 레이는 세워 둔 차 뒤에 숨는다. 대니 집 앞에 있던 검은 승합차가 뒤에서 와서 검문소 앞에 서고,
    /// 경찰이 바리케이드를 치워 그냥 보내 준다. 승합차는 광장을 지나 항구 길로 사라진다.
    /// </summary>
    IEnumerator CheckpointScene()
    {
        CurrentPhase = Phase.CheckpointScene;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        player.scriptedVelocity = Vector3.zero;
        player.Teleport(zone2Start.position, zone2Start.rotation);
        Place(dynamicA, checkpointCam.position, checkpointLook.position, 30f);
        Shot(dynamicA, 0.9f);
        StoryState.Say(Ray, "…검문소? 경찰이 길을 막았어.", 2.6f);
        yield return Wait(2.2f);

        vanRoutine = StartCoroutine(DriveVan());
        yield return Wait(1.6f);
        StoryState.Say(Ray, "저 차… 대니 집 앞에 서 있던 그 검은 승합차다.", 3f);
        yield return WaitVanStop(8f);
        StoryState.Say("경찰", "…통과.", 1.6f);
        yield return Wait(0.8f);
        yield return MoveGate(true, 0.9f);
        vanGo = true;
        yield return Wait(2.6f);
        StoryState.Say(Ray, "경찰이 놈들 차를 그냥 보내 줘…? 이 동네, 생각보다 더 썩었군.", 3.8f);
        yield return Wait(3.4f);
        yield return MoveGate(false, 0.9f);

        if (StoryState.SkipRequested)
        {
            // 건너뛰면 승합차는 이미 지나갔고 문은 닫혀 있다
            if (vanRoutine != null) StopCoroutine(vanRoutine);
            if (van) van.SetActive(false);
            if (gate) gate.SetPositionAndRotation(gateClosedPos, gateClosedRot);
        }
        StoryState.Say(Ray, "정면으로는 못 지나가. …저 방패들부터 치워야겠군.", 3f);
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.8f);
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
    }

    bool vanStopped, vanGo;

    IEnumerator DriveVan()
    {
        if (!van || vanPath == null || vanPath.Length < 4) yield break;
        van.SetActive(true);
        var audio = van.GetComponent<AudioSource>();
        if (audio) { audio.clip = ProceduralSfx.Recorded("VanPass"); if (audio.clip) audio.Play(); }
        yield return DriveTo(vanPath[0].position, vanPath[1].position, 10f);
        vanStopped = true;
        yield return new WaitUntil(() => vanGo);
        if (audio && audio.clip) audio.Play();
        yield return DriveTo(vanPath[1].position, vanPath[2].position, 9f);
        yield return DriveTo(vanPath[2].position, vanPath[3].position, 12f);
        van.SetActive(false);
    }

    IEnumerator DriveTo(Vector3 a, Vector3 b, float speed)
    {
        float len = Vector3.Distance(a, b);
        var rot = Quaternion.LookRotation(Flat(b - a).sqrMagnitude > 0.01f ? Flat(b - a) : Vector3.forward);
        for (float d = 0f; d < len; d += speed * Time.deltaTime)
        {
            // 출발·정지 때 부드럽게(앞뒤 4m에서 속도를 줄인다)
            van.transform.SetPositionAndRotation(Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, d / len) * 0.3f + d / len * 0.7f), rot);
            yield return null;
        }
        van.transform.SetPositionAndRotation(b, rot);
    }

    IEnumerator WaitVanStop(float timeout)
    {
        float until = StoryState.Now + timeout;
        while (!vanStopped && StoryState.Now < until && !StoryState.SkipRequested) yield return null;
    }

    IEnumerator MoveGate(bool open, float seconds)
    {
        if (!gate || !gateOpen) yield break;
        Vector3 fromP = gate.position, toP = open ? gateOpen.position : gateClosedPos;
        Quaternion fromR = gate.rotation, toR = open ? gateOpen.rotation : gateClosedRot;
        for (float t = 0f; t < 1f && !StoryState.SkipRequested; t += Time.deltaTime / seconds)
        {
            gate.SetPositionAndRotation(Vector3.Lerp(fromP, toP, Mathf.SmoothStep(0f, 1f, t)), Quaternion.Slerp(fromR, toR, Mathf.SmoothStep(0f, 1f, t)));
            yield return null;
        }
        gate.SetPositionAndRotation(toP, toR);
    }

    /// <summary>광장에 들어서는 순간: 옥상에서 붉은 조준선이 내려온다(잠깐 느려진 화면으로 저격수를 보여 준다)</summary>
    IEnumerator SniperIntro()
    {
        if (!sniper) yield break;
        Vector3 p = player.transform.position + Vector3.up * 1.6f;
        Vector3 s = sniper.transform.position + Vector3.up * 1.5f;
        Vector3 dir = Flat(s - p).normalized;
        Place(dynamicB, p - dir * 2.2f + Vector3.up * 0.4f + Vector3.Cross(Vector3.up, dir) * 0.8f, s, 30f);
        Shot(dynamicB, 0.35f);
        StoryState.ControlLocked = true; // 느린 화면 동안 인벤토리·되감기가 시간 배율을 바꾸지 않게
        Time.timeScale = 0.35f;
        StoryState.Say(Ray, "…옥상에 붉은 선—저격수다!", 2.4f);
        yield return StoryState.Sleep(1.5f);
        if (Mathf.Approximately(Time.timeScale, 0.35f)) Time.timeScale = 1f; // 그사이 쓰러져 사망 화면이 시간을 가져갔으면 그대로 둔다
        StoryState.ControlLocked = false;
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.3f);
        sniper.Activate();
        Tutorial.Show("sniper");
    }

    /// <summary>쓰러진 저격수를 심문한다: 정리반은 항구로 갔다</summary>
    IEnumerator Interrogation()
    {
        CurrentPhase = Phase.Interrogation;
        while (true)
        {
            yield return new WaitForSeconds(1.2f); // 쓰러지는 몸이 자리 잡을 때까지
            if (sniper && !sniper.Health.IsDead)
            {
                // 그사이 되감아 저격수가 되살아났다 → 다시 쓰러뜨릴 때까지 광장 단계로(UpdateZone2가 다시 겨누게 한다)
                CurrentPhase = Phase.Square;
                StoryState.Objective = "엄폐물 사이로 다가가 옥상의 저격수를 처리한다";
                yield return new WaitUntil(() => !sniper || sniper.Health.IsDead);
                CurrentPhase = Phase.Interrogation;
                continue;
            }
            if (RoofFighting())
            {
                // 옥상 경비가 아직 덤빈다 → 먼저 처리해야 심문한다(조작이 잠긴 컷신 동안 얻어맞지 않게)
                StoryState.Objective = "옥상의 조직원을 처리한다";
                StoryState.Waypoint = null;
                yield return new WaitUntil(() => !RoofFighting() || (sniper && !sniper.Health.IsDead));
                continue;
            }
            break;
        }
        StoryState.CutsceneActive = true;
        TimeRewind.Clear(); // 심문을 시작하면 저격수가 살아 있던 때로는 되감을 수 없다
        // 컷신 동안은 아무도 덤비지 않는다(끝나면 다시 깨운다)
        var paused = new List<EnemyAI>();
        foreach (var e in EnemyAI.All) if (e.IsActive && !e.Health.IsDead) paused.Add(e);
        foreach (var e in paused) e.Deactivate();
        var playerHealth = player.GetComponent<Health>();
        playerHealth.Invulnerable = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        player.scriptedVelocity = Vector3.zero;
        var hips = sniper.body ? sniper.body.GetBoneTransform(HumanBodyBones.Hips) : null;
        Vector3 down = hips ? hips.position : sniper.transform.position;
        Vector3 me = player.transform.position;
        Vector3 away = Flat(me - down).sqrMagnitude > 0.01f ? Flat(me - down).normalized : Vector3.back;
        Vector3 side = Vector3.Cross(Vector3.up, away);
        // 레이 어깨 너머로 바닥의 저격수를 내려다본다
        Place(dynamicA, me + away * 1.3f + side * 0.6f + Vector3.up * 1.8f, down + Vector3.up * 0.3f, 38f);
        Shot(dynamicA, 0.8f);
        string[,] lines =
        {
            { Ray, "정리반, 어디로 갔어.", "2.4" },
            { "저격수", "크윽… 형사님 아니세요…? 아, 이제 형사도 아니시지.", "3.4" },
            { Ray, "대답해.", "1.6" },
            { "저격수", "항구요… 3번 부두. 새벽 다섯 시에 배가 떠요. 대니가 녹음해 둔 테이프… 찾으면 손대지 말고 그대로 실으래요. 위에서 직접 듣겠다고…", "5.4" },
            { Ray, "경찰이 왜 놈들 차를 지켜 줘. 누가 시켰어.", "3.0" },
            { "저격수", "위에서… 다 알아요. 당신이 뭘 캐고 다녔는지도. 그래서 잘린 거잖아요.", "4.2" },
            { Ray, "……대니가 뭘 녹음했길래. …항구다.", "2.6" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.15f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.8f);
        playerHealth.Invulnerable = false;
        foreach (var e in paused) if (e && !e.Health.IsDead) e.Activate();
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
    }

    /// <summary>옥상 경비가 깨어나 아직 싸우는 중인가</summary>
    bool RoofFighting()
    {
        if (!roofZone) return false;
        foreach (var e in roofZone.enemies) if (e && e != sniper && e.IsActive && !e.Health.IsDead) return true;
        return false;
    }

    static void Arm(StoryTrigger trigger, bool on)
    {
        if (trigger) trigger.enabled = on;
    }

    /// <summary>그 자리에 닿았다: 켜진 뒤 들어왔거나, 켜질 때 이미 그 안에 서 있었다</summary>
    bool Reached(StoryTrigger trigger) =>
        !trigger || trigger.Fired || (trigger.enabled && trigger.TryGetComponent(out Collider c) && c.bounds.Contains(player.transform.position));

    void UpdateZone2()
    {
        // 검문소를 정리하면 길잡이를 광장 입구로(검문소 자체를 가리켜 다음 갈 곳을 몰랐다)
        if (CurrentPhase == Phase.Checkpoint && checkpointZone && checkpointZone.IsCleared && StoryState.Waypoint == checkpointLook && squareTrigger)
            StoryState.Waypoint = squareTrigger.transform;
        if (CurrentPhase != Phase.Square || !sniper || sniper.Health.IsDead) return;
        // 계단에 오르면 길잡이를 저격수로. 다시 시작해(저장 지점) 광장에 돌아오면 저격수가 다시 겨눈다
        Vector3 me = player.transform.position;
        bool onStairs = me.y > 0.5f || (stairsFoot && Flat(me - stairsFoot.position).magnitude < 4f);
        StoryState.Waypoint = me.y > 2.5f ? sniper.transform : onStairs && roofTop ? roofTop : stairsFoot;
        if (!sniper.IsActive && !player.IsDead && square.Contains(player.transform.position)) sniper.Activate();
    }

    /// <summary>
    /// 실전 첫 되감기: 손전등에 걸린 순간 화면이 거의 멈추고, R을 눌러 되감을 때까지 기다린다.
    /// 되감기 기록에는 이 연출 전의 상태가 남아 있어 조직원은 다시 건물 뒤에 서 있다 → 그때부터 모퉁이를 오가며 순찰한다.
    /// 되감을 횟수가 없으면(앞에서 이미 썼다면) 그냥 들킨 것으로 싸움이 시작된다.
    /// </summary>
    IEnumerator ForcedRewind()
    {
        CurrentPhase = Phase.ForcedRewind;
        bool canRewind = walkman && walkman.tapes.Exists(t => t.charges > 0) && TimeRewind.RecordedSeconds >= 2f;
        if (canRewind) StoryState.HoldTime = true; // 들킨 순간에서 기록을 멈춘다: 되감으면 정확히 들키기 5초 전으로 간다

        // 조직원이 모퉁이에서 한 걸음 나와 레이를 비춘다
        Vector3 to = Flat(player.transform.position - spotterStep.position);
        spotter.Enemy.PlaceAt(spotterStep.position, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : Vector3.back));
        if (spotter.beamTarget) spotter.beamTarget.position = player.transform.position + Vector3.up * 1.1f;
        spotter.ForceSuspicion(1f);
        spotter.Staged = true;
        RewindMemory.NoteSpotted(spotter); // 되감은 뒤 이 순찰의 시야가 보인다(레이만 기억한다)
        StoryState.Say(Thug, "…어? 저기다!", 2.4f);

        if (!canRewind)
        {
            spotter.Staged = false;
            spotter.Holding = false;
            spotter.CanDetect = true;
            spotter.Alert(true);
            yield break;
        }

        // 어깨 너머로 레이를 본다. 화면은 거의 멈춘 채 R을 기다린다
        Vector3 p = player.transform.position, s = spotterStep.position;
        Vector3 dir = Flat(p - s).normalized, side = Vector3.Cross(Vector3.up, dir);
        Place(dynamicA, s - dir * 1.1f + side * 0.55f + Vector3.up * 1.75f, p + Vector3.up * 1.2f, 42f);
        Shot(dynamicA, 0.25f);
        Time.timeScale = 0.04f;
        yield return StoryState.Sleep(0.6f);
        StoryState.Objective = "되감아서 들키기 전으로 돌아간다";
        StoryState.AskRewind("R 을 길게 눌러 되감는다   ·   5초 전, 들키기 전으로");
        yield return new WaitUntil(() => walkman.Rewinding);
        StoryState.HoldTime = false;
        StoryState.Prompt = null;
        yield return new WaitUntil(() => !walkman.Rewinding);

        // 되감은 뒤: 조직원은 기록대로 건물 뒤에 다시 서 있다. 이제 모퉁이를 오가며 순찰한다
        Time.timeScale = 1f;
        spotter.Staged = false;
        spotter.Holding = false;
        spotter.CanDetect = true;
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0f);
        StoryState.Objective = "조직원들을 피해 공원을 지나 큰길로 빠져나간다";
        StoryState.Say(Ray, "…5초 전이다. 놈이 저 모퉁이에서 나온다는 걸 알았으니, 이번엔 숨어서 지나간다.", 3.8f);
        Tutorial.Show("stealth");
    }

    /// <summary>엿듣기: 공원 관리 도로의 검은 차 옆, 등을 돌리고 통화하는 조직원</summary>
    IEnumerator PhoneCall()
    {
        string[] lines =
        {
            "…네. 카세트 플레이어는 놓쳤습니다. 뒷창문으로 튄 것 같습니다.",
            "대니 놈이 숨긴 것도 아직이고요. 집을 다 뒤졌는데 안 나옵니다.",
            "…알겠습니다. 정리반을 부르죠. 그놈, 멀리는 못 갔을 겁니다.",
        };
        foreach (var line in lines)
        {
            if (caller.Alerted || caller.Enemy.Health.IsDead) break;
            StoryState.Say("조직원 (통화 중)", line, 3.6f);
            yield return Wait(3.8f);
        }
        caller.Holding = false; // 통화를 마치고 순찰을 돈다
        if (!caller.Alerted && !caller.Enemy.Health.IsDead)
        {
            StoryState.Say(Ray, "대니가 숨긴 거…? 카세트 플레이어 말고 또 있다는 건가.", 3.4f);
            yield return Wait(3.6f);
            StoryState.Say(Ray, "정리반이라… 놈들보다 먼저 찾아야 해.", 2.8f);
            yield return Wait(2.8f);
        }
        callDone = true;
    }

    void OnZoneEntered(CombatZone zone)
    {
        if (zone == checkpointZone) { StoryState.Say("경찰", "거기 서! 경찰이다!", 2f); return; }
        if (zone == roofZone) { StoryState.Say(Thug, "올라왔다! 막아!", 1.8f); return; }
        if (zone != exitZone) return;
        CurrentPhase = Phase.ExitFight;
        StoryState.Say(Thug, "여기로 나온다! 잡아!", 2f);
    }

    // ---------- 3번 부두: 반장 ----------

    /// <summary>반장 무리 말고, 깨어나 레이를 쫓는 조직원이 가까이(35m) 있는가</summary>
    bool ChasersNearby()
    {
        foreach (var e in EnemyAI.All)
        {
            if (!e || e == boss || e.Health.IsDead || !e.IsActive) continue;
            if (System.Array.IndexOf(pierThugs, e) >= 0) continue;
            if (bossBrain && System.Array.IndexOf(bossBrain.reinforcements, e) >= 0) continue;
            if ((e.transform.position - player.transform.position).sqrMagnitude < 35f * 35f) return true;
        }
        return false;
    }

    IEnumerator BossIntro()
    {
        CurrentPhase = Phase.BossIntro;
        StoryState.CutsceneActive = true;
        player.scriptedVelocity = Vector3.zero;
        Vector3 b = boss.transform.position + Vector3.up * 1.6f;
        Vector3 r = player.transform.position + Vector3.up * 1.6f;
        Vector3 dir = Flat(b - r).normalized, side = Vector3.Cross(Vector3.up, dir);
        // 레이 어깨 너머로 드럼통 불 앞의 반장을 본다
        Place(dynamicA, r - dir * 1.6f + side * 0.7f + Vector3.up * 0.2f, b, 34f);
        Shot(dynamicA, 0.6f);
        boss.transform.rotation = Quaternion.LookRotation(Flat(player.transform.position - boss.transform.position));
        string[,] lines =
        {
            { Boss, "…레이. 오랜만이군.", "2.6" },
            { Ray, "선배…? 정리반 반장이 당신이었어?", "3.0" },
            { Boss, "대니가 숨긴 건 이거 하나였다. 위에선 직접 들어 보기 전엔 아무도 손대지 말라더군.", "4.4" },
            { Ray, "그 테이프 내놔요.", "2.0" },
            { Boss, "형사 시절에 가르쳐 줬지. 끝까지 물고 늘어지지 말라고.", "3.6" },
        };
        var myHealth = player.GetComponent<Health>();
        bool wasInvulnerable = myHealth.Invulnerable;
        myHealth.Invulnerable = true;
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            if (i == 2) { Place(dynamicB, b + Flat(-dir) * 2.4f + side * 0.9f, b - Vector3.up * 0.1f, 30f); Shot(dynamicB, 0.5f); }
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.15f);
        }
        myHealth.Invulnerable = wasInvulnerable;
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.5f);
        StoryState.CutsceneActive = false;
        Tutorial.Show("perfect");
    }

    /// <summary>
    /// 반장이 대니의 테이프를 드럼통 불에 던지러 달려간다. 막지 못해 불에 들어가면 → 시간이 멈추고 R 안내 → 되감으면
    /// 레이는 놈이 어디로 갈지 기억한다(바닥에 길이 보인다) → 다시 막으러 간다. 실패해도 끝나지 않는다(언제나 아슬아슬하게).
    /// 되감을 횟수가 없으면 라이터가 헛돌아 테이프가 떨어진다.
    /// </summary>
    IEnumerator BurnSequence()
    {
        if (!boss || boss.Health.IsDead) yield break;
        CurrentPhase = Phase.Burn;
        int attempt = 0;
        var memoryLine = MakeMemoryLine();
        while (true)
        {
            attempt++;
            boss.Deactivate();
            boss.Health.Invulnerable = false;
            if (boss.Health.IsDead) boss.Health.Revive();
            bool intercepted = false;
            void OnHit(int amount, Vector3 from) => intercepted = true;
            boss.Health.Damaged += OnHit;
            Vector3 throwSpot = burnBarrel.position + Flat(boss.transform.position - burnBarrel.position).normalized * 1.1f;
            var agent = boss.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent && !agent.enabled) agent.enabled = true;
            if (agent) { agent.isStopped = false; agent.speed = attempt == 1 ? 5.2f : 3.0f; agent.stoppingDistance = 0.2f; agent.SetDestination(throwSpot); }
            StoryState.Say(Boss, attempt == 1 ? "…위에서 뭐라 하든, 너한테 넘기느니 태워 버린다!" : "비켜, 레이!", attempt == 1 ? 2.6f : 1.8f); // 처음엔 '그대로 가져오라'는 명령 때문에 안 태웠다(2026-10-16)
            StoryState.Objective = "반장이 테이프를 태우기 전에 막는다";
            StoryState.Waypoint = boss.transform;
            if (memoryLine) { memoryLine.enabled = attempt > 1; memoryLine.SetPosition(0, boss.transform.position + Vector3.up * 0.06f); memoryLine.SetPosition(1, throwSpot + Vector3.up * 0.06f); }
            float started = StoryState.Now;
            yield return new WaitUntil(() => intercepted || boss.Health.IsDead
                                              || Flat(boss.transform.position - throwSpot).magnitude < 0.6f
                                              || StoryState.Now - started > 9f);
            boss.Health.Damaged -= OnHit;
            if (memoryLine) memoryLine.enabled = false;
            if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            if (intercepted || boss.Health.IsDead) break;

            // 불에 닿았다
            boss.transform.rotation = Quaternion.LookRotation(Flat(burnBarrel.position - boss.transform.position));
            bool canRewind = walkman && walkman.tapes.Exists(t => t.charges > 0) && TimeRewind.RecordedSeconds >= 2f;
            if (!canRewind)
            {
                // 되감을 횟수가 없다: 라이터가 헛돈다 → 테이프가 떨어진다(막힘 없이 이어진다)
                StoryState.Say(Boss, "…빌어먹을, 불이 왜—", 2f);
                if (boss.body) boss.body.CrossFadeInFixedTime("HitLight", 0.1f);
                yield return Wait(1.2f);
                break;
            }
            yield return BurnAndRewind();
        }
        if (memoryLine) Destroy(memoryLine.gameObject);
        StoryState.Waypoint = null;
    }

    /// <summary>
    /// 테이프를 막아 낸 뒤(또는 라이터가 헛돈 뒤): 반장은 남은 체력으로 끝까지 맞선다 — 체력을 다 깎아야 마지막 장면으로 넘어간다.
    /// (2026-10-16 "보스 피가 다 안 깎였는데 다 스킵되고 2장으로": 전에는 한 대만 가로막아도 25% 남은 반장이 즉사 처리됐다)
    /// 그사이 쓰러져 다시 시작하면 반장 체력은 처음이 아니라 이 싸움을 시작할 때의 체력으로 돌아간다.
    /// </summary>
    IEnumerator LastStand()
    {
        if (!boss || boss.Health.IsDead) yield break;
        CurrentPhase = Phase.Boss;
        // 테이프를 태우러 가는 사이 레이가 쓰러져 다시 시작하면 반장 체력이 처음으로 찼다 → 많아야 25%에서 시작
        int startHp = Mathf.Clamp(boss.Health.Current, 1, Mathf.CeilToInt(boss.Health.Max * 0.25f));
        boss.Health.SetCurrent(startHp);
        lastStandHp = startHp;
        GameManager.PlayerRespawned -= OnLastStandRespawn;
        GameManager.PlayerRespawned += OnLastStandRespawn;
        boss.Health.Invulnerable = false;
        StoryState.Say(Boss, "…이건 못 넘긴다. 끝까지 간다, 레이!", 2.2f);
        StoryState.Objective = "정리반 반장을 쓰러뜨린다";
        StoryState.Waypoint = null;
        boss.Activate();
        yield return new WaitUntil(() => !boss || boss.Health.IsDead);
        GameManager.PlayerRespawned -= OnLastStandRespawn;
    }

    int lastStandHp;
    void OnLastStandRespawn() { if (boss && !boss.Health.IsDead) boss.Health.SetCurrent(lastStandHp); }

    /// <summary>테이프가 불에 들어간다 → 시간이 거의 멈추고 R을 기다린다 → 되감기</summary>
    IEnumerator BurnAndRewind()
    {
        StoryState.HoldTime = true; // 던지는 순간에서 기록을 멈춘다: 되감으면 그 5초 전으로
        if (boss.body) boss.body.CrossFadeInFixedTime("Cross", 0.06f);
        var tape = bossBrain ? bossBrain.heldTape : null;
        Vector3 from = tape ? tape.position : boss.transform.position + Vector3.up * 1.3f;
        Transform tapeParent = tape ? tape.parent : null;
        if (tape) tape.SetParent(null, true);
        for (float t = 0f; t < 1f; t += StoryState.DeltaTime / 0.45f)
        {
            if (tape) tape.position = Vector3.Lerp(from, burnBarrel.position + Vector3.up * 0.9f, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.6f;
            yield return null;
        }
        if (barrelFire) barrelFire.baseIntensity = 22f; // 불길이 확 오른다
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("BodyFall") ?? ProceduralSfx.ButtonClick, burnBarrel.position, 0.6f, 0.7f, 0.6f);
        Vector3 fire = burnBarrel.position + Vector3.up * 1f;
        Place(dynamicA, fire + Flat(player.transform.position - fire).normalized * 2.4f + Vector3.up * 0.6f, fire, 32f);
        Shot(dynamicA, 0.25f);
        StoryState.Say(Ray, "안 돼—!", 1.6f);
        Time.timeScale = 0.05f;
        yield return StoryState.Sleep(0.5f);
        StoryState.Objective = "되감아서 테이프가 타기 전으로 돌아간다";
        StoryState.AskRewind("R 을 길게 눌러 되감는다");
        yield return new WaitUntil(() => walkman.Rewinding);
        StoryState.HoldTime = false;
        StoryState.Prompt = null;
        yield return new WaitUntil(() => !walkman.Rewinding);
        Time.timeScale = 1f;
        if (barrelFire) barrelFire.baseIntensity = 7f;
        if (tape && tapeParent) { tape.SetParent(tapeParent, false); tape.localPosition = tapeHome; tape.localRotation = tapeHomeRot; }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0f);
        StoryState.Say(Ray, "…놈이 어디로 달려갈지 안다. 이번엔 먼저 막는다.", 3.2f);
    }

    Vector3 tapeHome; Quaternion tapeHomeRot;

    LineRenderer MakeMemoryLine()
    {
        if (bossBrain && bossBrain.heldTape) { tapeHome = bossBrain.heldTape.localPosition; tapeHomeRot = bossBrain.heldTape.localRotation; }
        var go = new GameObject("BossMemoryPath");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.widthMultiplier = 0.25f;
        lr.useWorldSpace = true;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.35f) * 2f);
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.enabled = false;
        return lr;
    }

    /// <summary>반장을 막았다 → 쓰러진 반장의 마지막 말 → 새벽 부두, 대니의 녹음</summary>
    IEnumerator Finale()
    {
        CurrentPhase = Phase.Finale;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        foreach (var e in EnemyAI.All) if (e && e != boss && !e.Health.IsDead) e.Deactivate();
        if (bossBrain && bossBrain.heldTape) bossBrain.heldTape.gameObject.SetActive(false); // 레이가 테이프를 낚아챘다
        if (boss && !boss.Health.IsDead) boss.Health.TakeDamage(9999, player.transform.position);
        yield return Wait(1.4f);
        Vector3 down = boss ? boss.transform.position : player.transform.position;
        var hips = boss && boss.body ? boss.body.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hips) down = hips.position;
        Vector3 me = player.transform.position;
        Vector3 away = Flat(me - down).sqrMagnitude > 0.01f ? Flat(me - down).normalized : Vector3.back;
        player.transform.rotation = Quaternion.LookRotation(-away); // 쓰러진 반장을 내려다본다
        // 옆에서 낮게: 쓰러진 반장과 내려다보는 레이가 한 화면에(레이 몸이 반장을 가리지 않게)
        Vector3 side = Vector3.Cross(Vector3.up, away);
        Vector3 between = Vector3.Lerp(down + Vector3.up * 0.3f, me + Vector3.up * 1.1f, 0.4f);
        // 양옆 중 드럼통·컨테이너에 가리지 않는 쪽에서 찍는다
        Vector3 camAt = between + side * 3.6f - away * 0.4f + Vector3.up * 1.3f;
        Vector3 other = between - side * 3.6f - away * 0.4f + Vector3.up * 1.3f;
        bool Blocked(Vector3 from) =>
            Physics.Linecast(from, down + Vector3.up * 0.3f, out var h, ~0, QueryTriggerInteraction.Ignore) && !h.transform.IsChildOf(boss.transform) && !h.transform.IsChildOf(player.transform);
        if (boss && Blocked(camAt) && !Blocked(other)) camAt = other;
        Place(dynamicA, camAt, between + Vector3.up * 0.15f, 44f);
        Shot(dynamicA, 0.8f);
        string[,] lines =
        {
            { Boss, "…형사님.", "2.4" },
            { Ray, "……선배.", "1.8" },
            { Boss, "위는… 네가 생각하는 것보다 가까이 있어. 그 테이프… 끝까지 들어라.", "4.6" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        yield return FadeTo(1f, 1.6f);

        // 새벽: 3번 부두 끝 계선주에 앉아 테이프를 끼운다
        StoryState.Title = "새벽 5시 12분";
        if (dawnSeat) player.Teleport(dawnSeat.position, dawnSeat.rotation);
        // 비가 그친다. 어둠 속에서 시작해, 녹음이 흐르는 동안 수평선이 붉게 밝아 온다(SkyCycle)
        if (rainAudio) rainAudio.volume *= 0.15f;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.name == "Rain") ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.name == "Flood" || l.name == "Beam" || l.name == "AisleLamp") l.intensity *= 0.35f; // 새벽: 투광등이 빛을 잃는다
        Vector3 seat = player.transform.position;
        // 레이 왼쪽 뒤 낮은 곳에서 해 뜨는 바다 쪽을 본다: 레이는 왼쪽 1/3, 해와 수평선은 오른쪽, 배는 화면 밖
        player.transform.rotation = Quaternion.LookRotation(new Vector3(0.3f, 0f, 1f));
        Place(dynamicB, seat + new Vector3(-2.2f, 1.2f, -3.0f), seat + new Vector3(3f, 2.2f, 30f), 46f);
        Shot(dynamicB, 0f);
        if (skyCycle) skyCycle.Set(0.15f);
        yield return Wait(1.6f);
        StoryState.Title = null;
        if (skyCycle) StartCoroutine(skyCycle.Dawn(16f));
        yield return FadeTo(0f, 2.2f);
        string[,] dawn =
        {
            { "", "(찰칵. 테이프가 돌아가는 소리. 그리고 낮게 속삭이는 목소리)", "3.4" },
            { Danny2, "…레이. 이걸 듣고 있다면, 난 이미 늦은 거겠지.", "3.8" },
            { Danny2, "미안하다. 그 회사 일… 그리고 우리 쪽에도 그놈들 사람이 있어. 아주 가까이.", "4.6" },
            { Danny2, "아무도 믿지 마. …나도 말이야.", "3.2" },
            { "", "(찰칵. 테이프가 멈춘다)", "2.4" },
            { Ray, "……대니.", "2.4" },
        };
        for (int i = 0; i < dawn.GetLength(0); i++)
        {
            float t = float.Parse(dawn[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(dawn[i, 0], dawn[i, 1], t);
            yield return Wait(t + 0.25f);
        }
        yield return FadeTo(1f, 2f);
        StoryState.Title = "1장 「B면」 끝";
        yield return Wait(3f);
        StoryState.Title = null;
    }

    // ---------- 항구 철조망 문 ----------

    IEnumerator OpenHarborGate()
    {
        if (!gateLeaf) yield break;
        if (gateUse) gateUse.gameObject.SetActive(false);
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("DoorOpen") ?? ProceduralSfx.ButtonClick, gateLeaf.position, 0.8f, 0.7f, 0.7f);
        Vector3 from = gateLeaf.position;
        for (float t = 0f; t < 1f; t += StoryState.DeltaTime / 1.3f)
        {
            gateLeaf.position = Vector3.Lerp(from, gateOpenPos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        gateLeaf.position = gateOpenPos;
    }

    // ---------- 가게 2 ----------

    void OnShop2Approach()
    {
        Checkpoint(shop2Save, CpShop2);
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…부두 앞에 불 켜진 수리점이라. 여기도 그쪽 사람인가.", 3f);
    }

    // ---------- 가게 1 ----------

    void OnShopApproach()
    {
        // 가게는 저장 지점이다(찾아가는 목표가 아니라, 길에 있으면 들른다)
        Checkpoint(shopSave, ShopId());
        Tutorial.Show("shop");
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…이 새벽에 불 켜진 레코드 가게라.", 2.6f);
    }

    void OnShopOpened(TapeShop opened)
    {
        if (opened == shop2)
        {
            Checkpoint(shop2Save, CpShop2);
            if (shop2Greeted) return;
            shop2Greeted = true;
            StoryState.Say("수리점 노인", "…그 카세트 플레이어. 어디서 났는지 알 것 같군. 정리반 반장을 만나러 가나? 그 사람도 예전엔 형사였지.", 5f);
            return;
        }
        if (opened != shop) return;
        Checkpoint(shopSave, ShopId());
        if (shopGreeted) return;
        shopGreeted = true;
        // 주인은 안쪽 방에 있다(목소리만 들린다)
        StoryState.Say("가게 주인 (안쪽에서)", "…이 시간에 손님이라. 둘러봐. 다 쓴 테이프가 있으면 바꿔 주지.", 4f);
    }

    /// <summary>가게 1 저장 지점: 출구 싸움을 끝낸 뒤에만 '가게부터 이어하기'(싸움 전에 들렀으면 1장 처음부터)</summary>
    string ShopId() => CurrentPhase >= Phase.Avenue ? CpShop1 : CpStart;

    void OnTraded(TapeShop at, CassetteTape given, CassetteTape received)
    {
        // 바꾼 테이프도 바로 기록한다(가게에 들어설 때 저장한 뒤에 바꿨다)
        SaveGame.Write(at == shop2 ? CpShop2 : ShopId(), walkman);
        if (at == shop2 && !shop2Traded)
        {
            shop2Traded = true;
            StoryState.Say("수리점 노인", "나도 거기서 테이프를 찍었어. 사람 울음소리를 녹음해서… 그만 묻게. 부두에선 크레인 불빛을 조심해.", 5f);
            return;
        }
        if (at != shop || traded) return;
        traded = true;
        bool hasCompanyTape = walkman && walkman.tapes.Exists(t => !t.playable);
        StoryState.Say("가게 주인 (안쪽에서)", hasCompanyTape
            ? "…주머니에 그 상표 없는 테이프. 그 회사 물건이야. 어디서 났는지는 안 물을게. 조심해."
            : "잘 들어. 끝까지 들으면 그만큼 돌려받는 법이니까.", 4.4f);
        Tutorial.Show("listen");
    }

    // ---------- 연출 도구 ----------

    void Shot(CinemachineCamera cam, float blend)
    {
        brain.DefaultBlend = blend <= 0f
            ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
            : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blend);
        foreach (var c in cutsceneCameras) c.Priority = c == cam ? 20 : 0;
        gameplayCamera.Priority = cam == gameplayCamera ? 20 : 10;
    }

    static void Place(CinemachineCamera cam, Vector3 position, Vector3 lookAt, float fov)
    {
        cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
        cam.Lens.FieldOfView = fov;
    }

    static IEnumerator Wait(float seconds)
    {
        // 이야기 시계로 잰다: 메뉴(일시정지·설명 팝업·인벤토리·가게)를 연 동안은 대사도 멈춘다
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

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
