using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 2장 「내사」 감독. 지금은 구역 1(경찰서 뒤 압수차량 보관소)까지.
///  골목(정보원의 전파사 = 저장 지점) → 보관소 철망 틈 → 순찰 경찰·감시탑 탐조등을 피해 1장의 검은 승합차를 찾는다
///  → 출입증과 반쯤 탄 운송장 → 경찰서 뒷문 → (이어서 계속).
/// 경찰은 옛 동료들이라 싸우지 않는다: 들키면 시간이 멈추고 되감는다(되감을 수 없으면 붙잡혀 마지막 저장 지점부터).
/// </summary>
public class Chapter2Director : MonoBehaviour
{
    const string Ray = "레이";

    public PlayerController player;
    public CinemachineBrain brain;
    public CinemachineCamera gameplayCamera, dynamicA;
    public ThirdPersonCamera gameplayRig;
    public AudioSource rainAudio;

    [Header("구역 1")]
    public Transform zoneStart;
    public TapeShop shop;
    public StoryTrigger shopApproach;
    public Transform shopSave;
    public StoryInteractable fenceGap;
    public Transform fenceInside;
    public StoryInteractable vanUse;
    public Transform van, vanSave;
    public Transform backDoor;
    public StoryTrigger backDoorTrigger;
    public Searchlight searchlight;
    [Tooltip("승합차 실내등(뒤질 때 켠다)")]
    public Light vanDome;

    [Header("구역 2(경찰서 안)")]
    public Transform stationEntry, stairsTop;
    public StoryTrigger floor2Trigger;
    public StoryInteractable keypadUse, dannyBox, recordsDoorUse, terminalUse, rayDeskUse;
    public Transform evidenceDoor, evidenceInside, recordsDoor, recordsInside;
    public EvidenceClerk clerk;

    [Header("구역 3(탈출)")]
    public AlarmLights alarm;
    public CombatZone zoneUpstairs, zoneDownstairs, zoneLot;
    [Tooltip("경보 전엔 숨겨 둔 정리반·특공대")]
    public GameObject[] squads = System.Array.Empty<GameObject>();
    public StoryInteractable backDoorOut;
    public Transform outsideDoor, traitorShot;
    public GameObject traitor;
    public Light traitorLight;
    public PoliceLights[] beacons = System.Array.Empty<PoliceLights>();
    public EnemyAI subBoss;

    public enum Phase { Opening, Alley, Lot, VanSearch, ToDoor, Station, Floor2, EvidenceRoom, Records, Alarm, Downstairs, Outside, Reveal, End }
    public Phase CurrentPhase { get; private set; }
    /// <summary>들켜서 시간이 멈춘 장면을 보여 주는 중(시험용)</summary>
    public bool Caught { get; private set; }

    Walkman walkman;
    bool gapUsed, vanSearched, shopGreeted;
    readonly List<CinemachineCamera> cutsceneCameras = new();

    const string CpStart = "c2_start", CpShop = "c2_shop", CpVan = "c2_van", CpInside = "c2_inside", CpKey = "c2_key", CpAlarm = "c2_alarm", CpOut = "c2_out";
    /// <summary>레이가 본 보관실 비밀번호(되감아도 레이는 기억한다 — 되감지 않는 값)</summary>
    public bool CodeKnown { get; private set; }
    bool keyFound, deskSeen, indoors;

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
        if (rainAudio)
        {
            rainAudio.clip = ProceduralSfx.RainLoop;
            rainAudio.loop = true;
            rainAudio.Play();
        }
        if (fenceGap) fenceGap.Used += () => gapUsed = true;
        if (vanUse) vanUse.Used += () => vanSearched = true;
        if (keypadUse) keypadUse.Used += OnKeypad;
        GameManager.PlayerRespawned += OnRespawned;
        if (dannyBox) dannyBox.Used += () =>
        {
            if (CurrentPhase != Phase.EvidenceRoom || keyFound) return;
            var ai = clerk ? clerk.GetComponent<EnemyAI>() : null;
            if (clerk && clerk.Current == EvidenceClerk.State.Inside && ai && !ai.Health.IsDead) return; // 담당부터
            StartCoroutine(OpenDannyBox());
        };
        if (recordsDoorUse) recordsDoorUse.Used += OnRecordsDoor;
        if (terminalUse) terminalUse.Used += () => { if (CurrentPhase == Phase.Records && recordsOpen) StartCoroutine(ReadRecords()); };
        if (rayDeskUse) rayDeskUse.Used += OnRayDesk;
        if (backDoorOut) backDoorOut.Used += OnBackDoorOut;
        CombatZone.Entered += OnZoneEntered;
        if (clerk)
        {
            clerk.Keying += OnClerkKeying;
            clerk.Entered += OnClerkEntered;
            clerk.Refused += () => StoryState.Say("증거물 담당", "어? 왜 안 열려… 됐다, 내일 하지 뭐.", 3f);
            clerk.SawRay += () => { if (CanBeCaught()) StartCoroutine(CaughtRoutine(clerk.transform)); };
        }
        if (shopApproach) shopApproach.Entered += OnShopApproach;
        TapeShop.Opened += OnShopOpened;
        TapeShop.Traded += OnTraded;
        PatrolSight.Spotted += OnSpotted;
        Searchlight.Spotted += OnSearchlight;

        // 탈출 전투 구역과 뒷문 출구는 경보가 울릴 때까지 꺼 둔다(지나가기만 해도 부반장 대사·"뒷문으로 나간다"가 먼저 나오던 버그)
        Arm(zoneUpstairs, false); Arm(zoneDownstairs, false); Arm(zoneLot, false);
        if (backDoorOut) backDoorOut.gameObject.SetActive(false);

        var resume = SaveGame.TakePending(gameObject.scene.name);
        if (resume != null) SaveGame.ApplyInventory(resume, walkman);
        if (resume != null && resume.checkpoint == CpOut) { StartCoroutine(ResumeEscape(outside: true)); return; }
        if (resume != null && resume.checkpoint == CpAlarm) { StartCoroutine(ResumeEscape(outside: false)); return; }
        if (resume != null && resume.checkpoint == CpKey) { StartCoroutine(ResumeInside(evidenceInside, afterKey: true)); return; }
        if (resume != null && resume.checkpoint == CpInside) { StartCoroutine(ResumeInside(stationEntry, afterKey: false)); return; }
        if (resume != null && resume.checkpoint == CpVan) { StartCoroutine(Resume(vanSave, fromVan: true)); return; }
        if (resume != null && resume.checkpoint == CpShop) { StartCoroutine(Resume(shopSave, fromVan: false)); return; }
        Checkpoint(zoneStart, CpStart);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        TapeShop.Opened -= OnShopOpened;
        TapeShop.Traded -= OnTraded;
        PatrolSight.Spotted -= OnSpotted;
        GameManager.PlayerRespawned -= OnRespawned;
        Searchlight.Spotted -= OnSearchlight;
        CombatZone.Entered -= OnZoneEntered;
    }

    void Update()
    {
        if (rainAudio) rainAudio.volume = (indoors ? 0.1f : 0.45f) * GameSettings.Gain(GameSettings.Channel.Ambience); // 안에서는 창 너머로 멀게
    }

    // ---------- 진행 ----------

    IEnumerator Run()
    {
        CurrentPhase = Phase.Opening;
        Arm(backDoorTrigger, false);
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        StoryState.Title = "2장   내사";
        yield return Wait(2.6f);
        StoryState.Title = null;
        yield return FadeTo(0f, 1.6f);
        StoryState.ControlLocked = false;
        StoryState.Say(Ray, "…그 승합차 번호판. 우리 서 업무용이었어.", 3.2f);
        yield return Wait(3.4f);
        StoryState.Say(Ray, "놈들 차가 붙잡혔다면 서 뒤 압수차량 보관소에 있을 거다.", 3.4f);
        yield return FromAlley();
    }

    IEnumerator FromAlley()
    {
        CurrentPhase = Phase.Alley;
        StoryState.Objective = "경찰서 뒤 압수차량 보관소로 숨어든다";
        StoryState.Waypoint = fenceGap ? fenceGap.transform : null;
        yield return new WaitUntil(() => gapUsed || !fenceGap);
        // 철망 틈으로 기어 들어간다(잠깐 어두워졌다가 안쪽에서)
        StoryState.ControlLocked = true;
        yield return FadeTo(1f, 0.35f);
        if (fenceGap) fenceGap.gameObject.SetActive(false);
        if (fenceInside) player.Teleport(fenceInside.position, fenceInside.rotation);
        gameplayRig.SnapBehindTarget();
        // 붙잡히면 철망 안쪽에서 다시(틈은 이미 지났다). 파일 저장은 하지 않는다 — 이어하기는 가게부터
        if (GameManager.Instance && fenceInside) GameManager.Instance.SetCheckpoint(fenceInside);
        yield return Wait(0.2f);
        yield return FadeTo(0f, 0.5f);
        StoryState.ControlLocked = false;
        StoryState.Say(Ray, "…들키면 끝이다. 옛 동료들한테 주먹을 쓸 순 없지.", 3.2f);
        Tutorial.Show("takedown");
        yield return FromLot();
    }

    IEnumerator FromLot()
    {
        CurrentPhase = Phase.Lot;
        StoryState.Objective = "보관소 안에서 그 검은 승합차를 찾는다";
        StoryState.Waypoint = van;
        yield return new WaitUntil(() => vanSearched || !vanUse);
        yield return SearchVan();
        yield return FromVan();
    }

    IEnumerator FromVan()
    {
        CurrentPhase = Phase.ToDoor;
        Checkpoint(vanSave, CpVan);
        StoryState.Objective = "출입증으로 경찰서 뒷문을 연다";
        StoryState.Waypoint = backDoor;
        Arm(backDoorTrigger, true);
        yield return new WaitUntil(() => Reached(backDoorTrigger));
        StoryState.ControlLocked = true;
        entering = true; // 출입증을 대고 문이 열리는 동안(조작이 잠긴 2초)은 들키지 않는다 — 전에는 문 옆 경찰이 그사이에 잡았다
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        StoryState.Say(Ray, "(삑 — 출입증이 통한다. 문이 열린다)", 2.4f);
        yield return Wait(1.4f);
        yield return FadeTo(1f, 0.8f);
        GoIndoors();
        if (stationEntry) player.Teleport(stationEntry.position, stationEntry.rotation);
        gameplayRig.SnapBehindTarget();
        Checkpoint(stationEntry, CpInside);
        yield return Wait(0.3f);
        yield return FadeTo(0f, 1.0f);
        StoryState.ControlLocked = false;
        entering = false;
        StoryState.Say(Ray, "…7년 만이군. 냄새도 그대로야.", 2.6f);
        yield return FromStation();
    }

    // ---------- 구역 2: 경찰서 안 ----------

    bool recordsOpen;

    /// <summary>비를 끄고 빗소리를 멀게(실내)</summary>
    void GoIndoors()
    {
        indoors = true;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.name == "Rain") ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    IEnumerator FromStation()
    {
        CurrentPhase = Phase.Station;
        StoryState.Objective = "2층 증거물 보관실로 올라간다";
        StoryState.Waypoint = stairsTop;
        yield return new WaitUntil(() => Reached(floor2Trigger));
        CurrentPhase = Phase.Floor2;
        if (clerk) clerk.Called = true; // 휴게실의 담당이 잠시 뒤 보관실로 온다(되감아도 같은 시각에 다시 나선다)
        StoryState.Say(Ray, "…증거물 보관실. 비밀번호 문이군.", 2.6f);
        StoryState.Objective = "증거물 보관실에 들어간다";
        StoryState.Waypoint = keypadUse ? keypadUse.transform : null;
        yield return new WaitUntil(() => CurrentPhase == Phase.EvidenceRoom);
        yield return new WaitUntil(() => keyFound);
        yield return FromKey();
    }

    void OnClerkKeying()
    {
        if (CurrentPhase != Phase.Floor2) return;
        // 레이가 같은 층 근처에 있으면 번호를 본다
        bool sees = Vector3.Distance(player.transform.position, clerk.keypadSpot.position) < 18f && Mathf.Abs(player.transform.position.y - clerk.keypadSpot.position.y) < 2f;
        StoryState.Say("", "(삑 · 삑 · 삑 · 삑 — 0 · 4 · 1 · 7)", 2.6f);
        if (sees && !CodeKnown) StartCoroutine(LearnCode());
    }

    IEnumerator LearnCode()
    {
        CodeKnown = true;
        yield return Wait(2.8f);
        StoryState.Say(Ray, "…0417. 외웠다.", 2.2f);
    }

    void OnClerkEntered() => StartCoroutine(SwingDoor(evidenceDoor, true, 0.5f, 1.2f));

    const string EvidenceCode = "0417";

    /// <summary>보관실 안에서 쓰러져 저장 지점(1층)으로 돌아왔으면: 다시 2층 문 앞부터(문은 닫히고 담당은 휴게실로)</summary>
    void OnRespawned()
    {
        if (CurrentPhase != Phase.EvidenceRoom || keyFound) return;
        StopCoroutine(nameof(ClerkInside));
        CurrentPhase = Phase.Floor2;
        StartCoroutine(SwingDoor(evidenceDoor, false, 0.01f, 0f));
        StoryState.Objective = "증거물 보관실에 들어간다";
        StoryState.Waypoint = keypadUse ? keypadUse.transform : null;
    }

    void OnKeypad()
    {
        if (CurrentPhase != Phase.Floor2 || KeypadPanel.IsOpen) return;
        KeypadPanel.Open(EvidenceCode, () => StartCoroutine(EnterEvidence()));
    }

    /// <summary>안에 눌러앉은 담당: 등을 보이고 서류를 본다 — 몇 초 안에 Q로 제압하지 못하면 돌아서서 덤빈다(총은 없다)</summary>
    IEnumerator ClerkInside()
    {
        var ai = clerk ? clerk.GetComponent<EnemyAI>() : null;
        if (!ai) yield break;
        clerk.enabled = false; // 정해진 길 걷기는 끝
        StoryState.Objective = "증거물 담당을 쓰러뜨린다";
        StoryState.Waypoint = clerk.transform;
        float until = StoryState.Now + 3f;
        while (StoryState.Now < until && !ai.Health.IsDead && !ai.IsActive) yield return null;
        if (!ai.Health.IsDead && !ai.IsActive)
        {
            StoryState.Say("증거물 담당", "누, 누구야!", 1.6f);
            ai.damage = 8;
            ai.Activate();
        }
        yield return new WaitUntil(() => ai.Health.IsDead);
        StoryState.Objective = "대니 사건 기록을 찾는다";
        StoryState.Waypoint = dannyBox ? dannyBox.transform : null;
    }

    IEnumerator EnterEvidence()
    {
        StoryState.ControlLocked = true;
        StoryState.Say("", "(삑 · 삑 · 삑 · 삑 — 철컥)", 1.6f);
        yield return SwingDoor(evidenceDoor, true, 0.45f, 0f);
        yield return FadeTo(1f, 0.3f);
        if (evidenceInside) player.Teleport(evidenceInside.position, evidenceInside.rotation);
        gameplayRig.SnapBehindTarget();
        yield return SwingDoor(evidenceDoor, false, 0.01f, 0f);
        bool someoneInside = clerk && clerk.Current == EvidenceClerk.State.Inside;
        if (clerk && !someoneInside) clerk.LockedFromInside = true; // 안에서 걸쇠를 건다: 담당이 와도 못 연다
        CurrentPhase = Phase.EvidenceRoom;
        StoryState.Objective = "대니 사건 기록을 찾는다";
        StoryState.Waypoint = dannyBox ? dannyBox.transform : null;
        yield return FadeTo(0f, 0.5f);
        StoryState.ControlLocked = false;
        if (someoneInside) StartCoroutine(nameof(ClerkInside));
    }

    IEnumerator OpenDannyBox()
    {
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        if (dannyBox) dannyBox.gameObject.SetActive(false);
        Vector3 me = player.transform.position + Vector3.up * 1.55f;
        Vector3 at = dannyBox ? dannyBox.transform.position : me + player.transform.forward;
        Vector3 dir = Flat(at - me).normalized, side = Vector3.Cross(Vector3.up, dir);
        Place(dynamicA, me - dir * 0.9f + side * 0.5f + Vector3.up * 0.3f, at, 40f);
        Shot(dynamicA, 0.6f);
        string[,] lines =
        {
            { "", "(상자 겉면 — '실종: 대니 오' · 접수 사흘 만에 '종결' 도장)", "3.6" },
            { Ray, "…사흘 만에 종결? 찾지도 않았다는 거군.", "2.8" },
            { "", "(결재란 — 정보과장. 서명은 번져 읽을 수 없다)", "3.2" },
            { "", "(사건 서류 사이에 끼인 통장 사본 — '오대니' 명의. 매달 같은 날, '한결 물류' 입금)", "4.0" },
            { Ray, "…대니. 너도 받고 있었어?", "2.6" },
            { "", "(상자 바닥에 테이프로 붙여 둔 열쇠 — '기록실')", "3.0" },
            { Ray, "그런데 이걸 숨겨 둔 것도 너겠지. …무슨 생각이었던 거야.", "3.4" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.15f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.6f);
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        keyFound = true;
        StartCoroutine(SwingDoor(evidenceDoor, true, 0.5f, 0f)); // 걸쇠를 풀고 나간다
        Checkpoint(evidenceInside, CpKey);
    }

    IEnumerator FromKey()
    {
        CurrentPhase = Phase.Records;
        StoryState.Objective = "기록실에서 승합차 임대 기록을 찾는다";
        StoryState.Waypoint = recordsDoorUse ? recordsDoorUse.transform : null;
        yield break;
    }

    void OnRecordsDoor()
    {
        if (!keyFound) { StoryState.Say(Ray, "…잠겨 있다. 열쇠가 있어야 해.", 2.2f); return; }
        recordsOpen = true;
        if (recordsDoorUse) recordsDoorUse.gameObject.SetActive(false);
        StartCoroutine(SwingDoor(recordsDoor, true, 0.6f, 0f));
        StoryState.Say(Ray, "(철컥 — 열쇠가 맞는다)", 1.6f);
        StoryState.Waypoint = terminalUse ? terminalUse.transform : null;
    }

    IEnumerator ReadRecords()
    {
        CurrentPhase = Phase.Alarm;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        if (terminalUse) terminalUse.gameObject.SetActive(false);
        Vector3 me = player.transform.position + Vector3.up * 1.55f;
        Vector3 at = terminalUse ? terminalUse.transform.position + Vector3.up * 0.1f : me + player.transform.forward;
        Vector3 dir = Flat(at - me).normalized, side = Vector3.Cross(Vector3.up, dir);
        Place(dynamicA, me - dir * 0.8f + side * 0.45f + Vector3.up * 0.2f, at, 38f);
        Shot(dynamicA, 0.6f);
        string[,] lines =
        {
            { "", "(차량 조회 — 업무용 승합차 3대 · 장기 임차)", "3.0" },
            { "", "(임차인: 한결 물류 — 직원도 사무실도 없는 서류뿐인 회사)", "3.4" },
            { "", "(모회사: 한결 레코드 · 한결 FM — 항만로 1번지, 한결 타워)", "3.6" },
            { Ray, "…한결 레코드. 대니가 말한 '그 회사'.", "2.8" },
            { "", "(삐— 삐— 화면 구석에 빨간 글씨: '조회 경보 — 정보과 통보')", "3.4" },
            { Ray, "열람에 걸렸어…! 나가야 해.", "2.4" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            if (i == 4) StartAlarm(); // 빨간 글씨와 함께 경보가 울린다
            yield return Wait(t + 0.15f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.6f);
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        yield return Escape();
    }

    // ---------- 구역 3: 탈출 ----------

    /// <summary>경보: 붉은 비상등·사이렌·경광등. 일반 경찰은 정문으로 빠지고(사라진다) 정리반·특공대가 나온다</summary>
    void StartAlarm()
    {
        if (alarm) alarm.TurnOn();
        foreach (var b in beacons) if (b) b.enabled = true;
        foreach (var s in FindObjectsByType<PatrolSight>(FindObjectsSortMode.None)) s.gameObject.SetActive(false);
        if (clerk) clerk.gameObject.SetActive(false);
        if (searchlight) searchlight.gameObject.SetActive(false); // 탑 경비도 정문으로
    }

    /// <summary>숨겨 둔 무리를 꺼낸다(그 구역의 적만)</summary>
    static void Show(CombatZone zone)
    {
        if (!zone) return;
        Arm(zone, true);
        foreach (var e in zone.enemies) if (e) e.gameObject.SetActive(true);
    }

    IEnumerator Escape()
    {
        CurrentPhase = Phase.Alarm;
        if (backDoorOut) backDoorOut.gameObject.SetActive(true);
        Checkpoint(recordsInside, CpAlarm);
        Show(zoneUpstairs);
        Show(zoneDownstairs);
        Show(zoneLot);
        // 방송: 배신자의 목소리(얼굴은 아직)
        StoryState.Say("구내 방송", "정보과다. 2층 기록실에 침입자. 근무자는 전원 정문으로 나가 대기한다. …안쪽은 우리가 맡는다.", 5f);
        yield return Wait(5.2f);
        StoryState.Say(Ray, "…이 목소리.", 2f);
        yield return FromAlarm();
    }

    IEnumerator FromAlarm()
    {
        CurrentPhase = Phase.Alarm;
        StoryState.Objective = "경찰서를 빠져나간다";
        StoryState.Waypoint = stairsTop;
        yield return new WaitUntil(() => !zoneUpstairs || zoneUpstairs.IsCleared || player.transform.position.y < 2f);
        CurrentPhase = Phase.Downstairs;
        StoryState.Waypoint = backDoorOut ? backDoorOut.transform : null;
        yield return new WaitUntil(() => CurrentPhase == Phase.Outside);
        yield return FromOutside();
    }

    void OnBackDoorOut()
    {
        if (CurrentPhase != Phase.Downstairs && CurrentPhase != Phase.Alarm) return;
        if (zoneDownstairs && !zoneDownstairs.IsCleared && zoneDownstairs.HoldsPlayer) { StoryState.Say(Ray, "…등을 보이면 쏜다. 먼저 정리한다.", 2.2f); return; }
        StartCoroutine(GoOutside());
    }

    IEnumerator GoOutside()
    {
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        yield return FadeTo(1f, 0.5f);
        indoors = false;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Rain") ps.Play(true);
        if (alarm) alarm.sirenScale = 0.25f; // 벽 너머로 멀게
        if (outsideDoor) player.Teleport(outsideDoor.position, outsideDoor.rotation);
        gameplayRig.SnapBehindTarget();
        Checkpoint(outsideDoor, CpOut);
        CurrentPhase = Phase.Outside;
        yield return Wait(0.3f);
        yield return FadeTo(0f, 0.8f);
        StoryState.ControlLocked = false;
    }

    IEnumerator FromOutside()
    {
        CurrentPhase = Phase.Outside;
        StoryState.Say(Ray, "…보관소를 가로질러 철망 틈으로.", 2.4f);
        StoryState.Objective = "보관소를 가로질러 빠져나간다";
        StoryState.Waypoint = zoneLot ? zoneLot.transform : null;
        yield return new WaitUntil(() => !zoneLot || zoneLot.IsCleared);
        yield return Reveal();
    }

    void OnZoneEntered(CombatZone zone)
    {
        if (zone == zoneUpstairs) StoryState.Say("정리반", "저기다! 테이프 든 놈!", 2f);
        else if (zone == zoneLot) StoryState.Say("정리반 부반장", "여기까지다, 레이. 위에서 너 하나 지우라더군.", 3.2f);
    }

    /// <summary>이겼다: 경찰서 2층 창에 불이 켜지고, 배신자가 내려다본다(얼굴만 — 정체는 3장)</summary>
    IEnumerator Reveal()
    {
        CurrentPhase = Phase.Reveal;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        yield return Wait(1.2f);
        // 레이 어깨 너머로 경찰서 뒷면 — 창 하나에 불이 들어온다
        Vector3 window = traitorShot ? traitorShot.position + new Vector3(0.3f, 0.4f, 3.2f) : player.transform.position + Vector3.up * 6f;
        Vector3 me = player.transform.position + Vector3.up * 1.6f;
        Vector3 dir = Flat(window - me).normalized, side = Vector3.Cross(Vector3.up, dir);
        player.transform.rotation = Quaternion.LookRotation(dir);
        Place(dynamicA, me - dir * 2.2f + side * 0.8f + Vector3.up * 0.1f, window, 34f);
        Shot(dynamicA, 0.8f);
        yield return Wait(1.0f);
        if (traitorLight) traitorLight.enabled = true;
        if (traitor) traitor.SetActive(true);
        ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, window, 0.5f, 0.8f, 0.7f); // 스위치 딸깍
        yield return Wait(1.6f);
        // 창 앞 — 얼굴
        if (traitorShot && traitor) Place(dynamicA, traitorShot.position, traitor.transform.position + Vector3.up * 1.4f, 30f);
        Shot(dynamicA, 1.4f);
        yield return Wait(1.8f);
        string[,] lines =
        {
            { "창가의 남자", "…7년 전에 조용히 죽어 줬으면 좋았잖나, 레이.", "3.6" },
            { Ray, "…너였냐.", "2.0" },
            { Ray, "내 파트너가 정보과장이라… 대니도, 그 밤도 네 짓이었어.", "3.4" },
            { "옛 파트너", "회사에서 기다리지. 한결 타워 꼭대기. 테이프는 가져오고.", "3.6" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            if (i == 1) { Place(dynamicA, me - dir * 2.2f + side * 0.8f + Vector3.up * 0.1f, window, 34f); Shot(dynamicA, 0.6f); } // 레이 쪽으로
            if (i == 3 && traitorShot && traitor) { Place(dynamicA, traitorShot.position, traitor.transform.position + Vector3.up * 1.4f, 30f); Shot(dynamicA, 0.6f); }
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        // 불이 꺼지고 창이 빈다
        if (traitorLight) traitorLight.enabled = false;
        if (traitor) traitor.SetActive(false);
        ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, window, 0.5f, 0.8f, 0.7f);
        yield return Wait(1.2f);
        Place(dynamicA, me - dir * 2.2f + side * 0.8f + Vector3.up * 0.1f, window, 34f);
        Shot(dynamicA, 0.8f);
        StoryState.Say(Ray, "…그래. 가 주지.", 2.4f);
        yield return Wait(2.6f);
        StoryState.SkipRequested = false;
        CurrentPhase = Phase.End;
        StoryState.ControlLocked = true;
        yield return FadeTo(1f, 1.6f);
        StoryState.Title = "2장   끝";
        yield return StoryState.Sleep(3f);
        StoryState.Title = null;
        yield return StoryState.Sleep(1f);
        hp.Invulnerable = false;
        // 3장으로: 테이프·회복약을 그대로 들고 간다
        SaveGame.CarryOver("Chapter3", "c3_start", walkman);
        SceneLoader.Load("Chapter3");
    }

    /// <summary>이어하기: 경보 뒤(기록실) 또는 밖(뒷문 앞)</summary>
    IEnumerator ResumeEscape(bool outside)
    {
        if (backDoorOut) backDoorOut.gameObject.SetActive(true);
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        gapUsed = vanSearched = true;
        keyFound = CodeKnown = recordsOpen = true;
        foreach (var i in new[] { vanUse, fenceGap, dannyBox, terminalUse, recordsDoorUse }) if (i) i.gameObject.SetActive(false);
        StartCoroutine(SwingDoor(evidenceDoor, true, 0.01f, 0f));
        StartCoroutine(SwingDoor(recordsDoor, true, 0.01f, 0f));
        StartAlarm();
        Transform at;
        if (outside)
        {
            Show(zoneLot); // 안의 두 무리는 이미 지나왔다
            at = outsideDoor;
            if (alarm) alarm.sirenScale = 0.25f; // 벽 너머로 멀게
        }
        else
        {
            GoIndoors();
            Show(zoneUpstairs);
            Show(zoneDownstairs);
            Show(zoneLot);
            at = recordsInside;
        }
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at);
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        yield return outside ? FromOutside() : FromAlarm();
    }

    void OnRayDesk()
    {
        if (deskSeen || StoryState.LineVisible) return;
        deskSeen = true;
        StoryState.Say(Ray, "…내 자리. 짐 상자가 그대로네. 명패도 안 치웠군.", 3f);
    }

    /// <summary>문짝을 경첩(서쪽 끝) 축으로 연다(복도 쪽으로 95°). closeAfter > 0이면 그만큼 뒤 닫는다</summary>
    IEnumerator SwingDoor(Transform hinge, bool open, float seconds, float closeAfter)
    {
        if (!hinge) yield break;
        float from = hinge.localEulerAngles.y, to = open ? -95f : 0f;
        if (from > 180f) from -= 360f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
        {
            hinge.localRotation = Quaternion.Euler(0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)), 0f);
            yield return null;
        }
        hinge.localRotation = Quaternion.Euler(0f, to, 0f);
        if (closeAfter > 0f) { yield return new WaitForSeconds(closeAfter); yield return SwingDoor(hinge, false, 0.5f, 0f); }
    }

    IEnumerator ResumeInside(Transform at, bool afterKey)
    {
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        gapUsed = vanSearched = true;
        if (vanUse) vanUse.gameObject.SetActive(false);
        if (fenceGap) fenceGap.gameObject.SetActive(false);
        GoIndoors();
        CurrentPhase = afterKey ? Phase.Records : Phase.Station;
        if (afterKey)
        {
            keyFound = CodeKnown = true;
            if (dannyBox) dannyBox.gameObject.SetActive(false);
            if (clerk) clerk.gameObject.SetActive(false);
            StartCoroutine(SwingDoor(evidenceDoor, true, 0.01f, 0f));
        }
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at);
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        yield return afterKey ? FromKey() : FromStation();
    }


    /// <summary>승합차를 뒤진다: 조수석 서랍의 출입증, 재떨이 속 반쯤 탄 운송장</summary>
    IEnumerator SearchVan()
    {
        CurrentPhase = Phase.VanSearch;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        if (vanUse) vanUse.gameObject.SetActive(false);
        if (vanDome) vanDome.enabled = true; // 조수석 문을 열자 실내등이 켜진다
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        // 레이 어깨 너머로 열린 조수석 문 안을 본다
        Vector3 me = player.transform.position + Vector3.up * 1.55f;
        Vector3 into = van ? van.position + Vector3.up * 1.2f : me + player.transform.forward;
        Vector3 dir = Flat(into - me).normalized, side = Vector3.Cross(Vector3.up, dir);
        Place(dynamicA, me - dir * 1.1f + side * 0.6f + Vector3.up * 0.25f, into, 38f);
        Shot(dynamicA, 0.6f);
        string[,] lines =
        {
            { Ray, "…이거다. 대니 집 앞에 서 있던 그 차.", "2.8" },
            { "", "(조수석 서랍 안쪽, 테이프로 붙여 둔 출입증이 나온다)", "3.2" },
            { Ray, "우리 서 출입증. 정보과… 이름은 긁어서 지워 놨군.", "3.4" },
            { "", "(재떨이 속, 반쯤 탄 운송장 — '한결 물류' … 뒤는 타서 읽을 수 없다)", "3.8" },
            { Ray, "회사 이름 반쪽. 나머지는 서 안 기록실에 있을 거다.", "3.2" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.15f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.6f);
        hp.Invulnerable = false;
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
    }

    // ---------- 들켰다: 시간이 멈추고 되감는다 ----------

    void OnSpotted(PatrolSight s) { if (CanBeCaught()) StartCoroutine(CaughtRoutine(s.transform)); }
    void OnSearchlight(Searchlight s) { if (CanBeCaught()) StartCoroutine(CaughtRoutine(null)); }

    bool entering;
    bool CanBeCaught() => !Caught && !entering && !StoryState.CutsceneActive && CurrentPhase is Phase.Lot or Phase.ToDoor or Phase.Station or Phase.Floor2 or Phase.EvidenceRoom or Phase.Records;

    IEnumerator CaughtRoutine(Transform by)
    {
        Caught = true;
        StoryState.HoldTime = true; // 들킨 순간에서 기록을 멈춘다: 되감으면 그 5초 전으로
        StoryState.Say("경찰", by ? "거기 누구야! 움직이지 마!" : "감시탑이다, 저기 사람이 있다!", 1.8f);
        Time.timeScale = 0.05f;
        yield return StoryState.Sleep(0.6f);
        bool canRewind = walkman && walkman.CurrentCharges > 0 && TimeRewind.RecordedSeconds >= 2f;
        if (canRewind)
        {
            StoryState.Prompt = "R 을 길게 눌러 되감는다";
            yield return new WaitUntil(() => walkman.Rewinding);
            StoryState.HoldTime = false;
            StoryState.Prompt = null;
            yield return new WaitUntil(() => !walkman.Rewinding);
            Time.timeScale = 1f;
            StoryState.Say(Ray, "…다시. 이번엔 들키지 않는다.", 2.4f);
        }
        else
        {
            // 되감을 수 없다: 붙잡혔다 → 마지막 저장 지점부터
            StoryState.HoldTime = false;
            Time.timeScale = 1f;
            StoryState.ControlLocked = true;
            StoryState.Say(Ray, "…여기까지인가.", 1.8f);
            yield return FadeTo(1f, 0.8f);
            if (GameManager.Instance) GameManager.Instance.RespawnNow(); // 어두운 동안 옮긴다(경찰도 제자리로)
            gameplayRig.SnapBehindTarget();
            yield return Wait(1.0f);
            StoryState.ControlLocked = false;
            yield return FadeTo(0f, 0.8f);
        }
        Caught = false;
    }

    // ---------- 가게(정보원의 전파사) ----------

    void OnShopApproach()
    {
        Checkpoint(shopSave, CpShop);
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…영감님 가게, 아직 하는군.", 2.4f);
    }

    void OnShopOpened(TapeShop opened)
    {
        if (opened != shop) return;
        Checkpoint(shopSave, CpShop);
        if (shopGreeted) return;
        shopGreeted = true;
        StoryState.Say("전파사 영감 (안쪽에서)", "…레이 형사? 죽었다는 소문이 돌던데. 서에 들어갈 생각이면 뒤쪽 보관소 철망이 썩었어. 서쪽 끝.", 5f);
    }

    void OnTraded(TapeShop at, CassetteTape given, CassetteTape received)
    {
        if (at == shop) SaveGame.Write(CurrentPhase >= Phase.ToDoor ? CpVan : CpShop, walkman);
    }

    // ---------- 저장 · 이어하기 ----------

    void Checkpoint(Transform point, string id)
    {
        if (!point) return;
        if (GameManager.Instance) GameManager.Instance.Save(point);
        SaveGame.Write(id, walkman);
    }

    IEnumerator Resume(Transform at, bool fromVan)
    {
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        Arm(backDoorTrigger, false);
        CurrentPhase = fromVan ? Phase.ToDoor : Phase.Alley;
        if (fromVan)
        {
            vanSearched = gapUsed = true;
            if (vanUse) vanUse.gameObject.SetActive(false);
            if (fenceGap) fenceGap.gameObject.SetActive(false);
        }
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at);
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        yield return fromVan ? FromVan() : FromAlley();
    }

    // ---------- 도우미 ----------

    static void Arm(StoryTrigger trigger, bool on) { if (trigger) trigger.enabled = on; }
    /// <summary>전투 구역의 들어섬 판정(트리거)을 켜고 끈다</summary>
    static void Arm(CombatZone zone, bool on) { if (zone) foreach (var c in zone.GetComponents<Collider>()) c.enabled = on; }

    bool Reached(StoryTrigger trigger) =>
        !trigger || trigger.Fired || (trigger.enabled && trigger.TryGetComponent(out Collider c) && c.bounds.Contains(player.transform.position));

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

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
        // 이야기 시계로 잰다: 메뉴를 연 동안은 대사도 멈춘다
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
