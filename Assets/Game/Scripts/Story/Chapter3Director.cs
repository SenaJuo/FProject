using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 3장 「한결 레코드」 감독 — 그 회사(한결 레코드 · 한결 FM) 본사, 한결 타워.
///  구역 1 항만로: 테이프 노점(저장) → 본사 동쪽 하역장(경비 순찰 · CCTV, 들키면 되감기) → 직원 통로
///  구역 2 녹음실 층: 유리 너머 실험 부스 — 처음엔 눈앞에서 쓰러진다 → 되감아서 장비를 먼저 끈다(레이만 기억)
///         → 살린 녹음 대상 7번의 증언 → 풀려난 실험체들과 싸움 → 엘리베이터
///  구역 3 옥상: 정리반 → 금고실(마스터 테이프는 이미 없다) → 옛 파트너와 짧은 결투 → 그는 전용 엘리베이터로 빠진다.
/// </summary>
public class Chapter3Director : MonoBehaviour
{
    const string Ray = "레이";
    const string Seven = "녹음 대상 7번";
    const string Partner = "옛 파트너";

    public PlayerController player;
    public CinemachineBrain brain;
    public CinemachineCamera gameplayCamera, dynamicA;
    public ThirdPersonCamera gameplayRig;
    public AudioSource rainAudio;

    [Header("구역 1 — 항만로 · 하역장")]
    public Transform zoneStart;
    public TapeShop shop;
    public StoryTrigger shopApproach;
    public Transform shopSave, dockSave;
    public StoryTrigger dockTrigger;
    public StoryInteractable serviceDoor;

    [Header("구역 2 — 녹음실 층")]
    public Transform studioEntry;
    public StoryTrigger corridorTrigger, controlTrigger;
    public ExperimentBooth booth;
    public StoryInteractable switchUse;
    public Transform boothGlass;
    public Transform[] cellDoors = System.Array.Empty<Transform>();
    public GameObject[] subjects = System.Array.Empty<GameObject>();
    public CombatZone zoneSubjects;
    public StoryInteractable elevatorUse;

    [Header("구역 3 — 옥상")]
    public Transform roofEntry;
    public CombatZone zoneRoof;
    public StoryTrigger vaultTrigger;
    public EnemyAI partner;
    public Transform partnerLift, vaultShot;

    public enum Phase { Opening, Street, Dock, Studio, Watch, Saved, Subjects, ToLift, Roof, Vault, Duel, End }
    public Phase CurrentPhase { get; private set; }
    public bool Caught { get; private set; }
    /// <summary>레이가 본 그 죽음(되감아도 레이는 기억한다 — 되감지 않는 값)</summary>
    public bool KnowsCollapse { get; private set; }

    Walkman walkman;
    bool doorUsed, switched, liftUsed, shopGreeted, partnerBroken;
    readonly List<CinemachineCamera> cutsceneCameras = new();

    const string CpStart = "c3_start", CpShop = "c3_shop", CpDock = "c3_dock", CpStudio = "c3_studio", CpRoof = "c3_roof";

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
        if (serviceDoor) serviceDoor.Used += () => doorUsed = true;
        if (switchUse) switchUse.Used += OnSwitch;
        if (elevatorUse) elevatorUse.Used += () => { if (CurrentPhase == Phase.ToLift) liftUsed = true; else StoryInteractable.Refuse(); };
        if (booth) booth.Collapsed += OnCollapsed;
        if (controlTrigger) controlTrigger.Entered += OnControlRoom;
        if (shopApproach) shopApproach.Entered += OnShopApproach;
        if (partner) partner.GetComponent<Health>().Damaged += OnPartnerHit;
        TapeShop.Opened += OnShopOpened;
        TapeShop.Traded += OnTraded;
        PatrolSight.Spotted += OnSpotted;
        Searchlight.Spotted += OnSearchlight;
        CombatZone.Entered += OnZoneEntered;
        GameManager.PlayerRespawned += OnRespawned;

        foreach (var s in subjects) if (s) s.SetActive(false);
        if (partner) partner.gameObject.SetActive(false);

        var resume = SaveGame.TakePending(gameObject.scene.name);
        if (resume != null) SaveGame.ApplyInventory(resume, walkman);
        string cp = resume?.checkpoint;
        if (cp == CpRoof) { StartCoroutine(Resume(roofEntry, Phase.Roof)); return; }
        if (cp == CpStudio) { StartCoroutine(Resume(studioEntry, Phase.Studio)); return; }
        if (cp == CpDock) { StartCoroutine(Resume(dockSave, Phase.Dock)); return; }
        if (cp == CpShop) { StartCoroutine(Resume(shopSave, Phase.Street)); return; }
        Checkpoint(zoneStart, CpStart);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        TapeShop.Opened -= OnShopOpened;
        TapeShop.Traded -= OnTraded;
        PatrolSight.Spotted -= OnSpotted;
        Searchlight.Spotted -= OnSearchlight;
        CombatZone.Entered -= OnZoneEntered;
        GameManager.PlayerRespawned -= OnRespawned;
    }

    /// <summary>결투 중 쓰러져 옥상 저장 지점부터: 파트너는 금고 앞으로 돌아가 기다린다(대사 없이 다시 붙는다)</summary>
    void OnRespawned()
    {
        if (CurrentPhase != Phase.Duel || !partner || partnerBroken) return;
        StopAllCoroutines();
        StoryState.CutsceneActive = false;
        var h = partner.GetComponent<Health>();
        h.Revive();
        partner.PlaceAt(partnerStart, Quaternion.Euler(0f, 180f, 0f));
        partner.Deactivate();
        StartCoroutine(DuelAgain());
    }

    Vector3 partnerStart;

    IEnumerator DuelAgain()
    {
        StoryState.Objective = "옛 파트너를 붙잡는다";
        StoryState.Waypoint = vaultTrigger ? vaultTrigger.transform : null;
        yield return new WaitUntil(() => vaultTrigger && vaultTrigger.GetComponent<Collider>().bounds.Contains(player.transform.position));
        StoryState.Waypoint = null;
        partner.Activate();
        yield return new WaitUntil(() => partnerBroken);
        yield return PartnerEscapes();
    }

    bool indoors;

    void Update()
    {
        if (rainAudio) rainAudio.volume = (indoors ? 0.06f : 0.45f) * GameSettings.Gain(GameSettings.Channel.Sfx);
    }

    // ---------- 구역 1 ----------

    IEnumerator Run()
    {
        CurrentPhase = Phase.Opening;
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        StoryState.Title = "3장   한결 레코드";
        yield return Wait(2.6f);
        StoryState.Title = null;
        yield return FadeTo(0f, 1.6f);
        StoryState.ControlLocked = false;
        StoryState.Say(Ray, "…한결 레코드. 대니가 말한 '그 회사'.", 3f);
        yield return Wait(3.2f);
        StoryState.Say(Ray, "정문은 무리다. 동쪽 하역장으로 돈다.", 2.8f);
        yield return FromStreet();
    }

    IEnumerator FromStreet()
    {
        CurrentPhase = Phase.Street;
        StoryState.Objective = "본사 동쪽 하역장으로 숨어든다";
        StoryState.Waypoint = dockTrigger ? dockTrigger.transform : null;
        yield return new WaitUntil(() => Reached(dockTrigger));
        yield return FromDock();
    }

    IEnumerator FromDock()
    {
        CurrentPhase = Phase.Dock;
        Checkpoint(dockSave, CpDock);
        StoryState.Say(Ray, "경비에 카메라까지. …들키면 끝이다.", 2.6f);
        StoryState.Objective = "직원 통로로 들어간다";
        StoryState.Waypoint = serviceDoor ? serviceDoor.transform : null;
        yield return new WaitUntil(() => doorUsed || !serviceDoor);
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        StoryState.Say("", "(경비가 흘린 출입증 — 삑. 화물 엘리베이터가 내려온다)", 2.6f);
        yield return Wait(1.6f);
        yield return FadeTo(1f, 0.8f);
        GoIndoors();
        if (studioEntry) player.Teleport(studioEntry.position, studioEntry.rotation);
        gameplayRig.SnapBehindTarget();
        Checkpoint(studioEntry, CpStudio);
        yield return Wait(0.3f);
        yield return FadeTo(0f, 1.0f);
        StoryState.ControlLocked = false;
        yield return FromStudio();
    }

    void GoIndoors()
    {
        indoors = true;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.name == "Rain") ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        // 하역장 경비·카메라는 이제 상관없다
        foreach (var s in FindObjectsByType<PatrolSight>(FindObjectsSortMode.None)) s.gameObject.SetActive(false);
        foreach (var s in FindObjectsByType<Searchlight>(FindObjectsSortMode.None)) s.enabled = false;
    }

    // ---------- 구역 2: 녹음실 층 ----------

    IEnumerator FromStudio()
    {
        CurrentPhase = Phase.Studio;
        StoryState.Say(Ray, "…방음벽. 여기서 뭘 녹음하는 거지.", 2.6f);
        StoryState.Objective = "녹음실 층을 살핀다";
        StoryState.Waypoint = controlTrigger ? controlTrigger.transform : null;
        yield return new WaitUntil(() => Reached(corridorTrigger));
        if (booth) booth.Begin(); // 실험이 시작된다(유리 너머에서 소리가 샌다)
        yield return new WaitUntil(() => switched);
        yield return SaveSeven();
        yield return FromSubjects();
    }

    /// <summary>처음 들어선 조정실: 유리 너머의 사람이 눈앞에서 쓰러진다(레이가 아직 모를 때만)</summary>
    void OnControlRoom()
    {
        if (KnowsCollapse || !booth || booth.Current != ExperimentBooth.State.Running) return;
        StartCoroutine(Watch());
    }

    IEnumerator Watch()
    {
        CurrentPhase = Phase.Watch;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        if (boothGlass && booth && booth.subject)
        {
            // 창 바로 앞 비스듬히: 레이 등에 가리지 않고 유리 너머가 보이게
            Vector3 face = booth.subject.transform.position + Vector3.up * 1.4f;
            Place(dynamicA, boothGlass.position + new Vector3(-1.1f, 0.15f, -1.7f), face, 40f);
            Shot(dynamicA, 0.5f);
        }
        StoryState.Say("", "(유리 너머 — 헤드폰을 쓴 사람이 몸을 떤다. 스피커에서 찢어지는 소리)", 2.6f);
        yield return Wait(2.6f);
        if (booth && booth.Current == ExperimentBooth.State.Running) booth.ForceCollapse();
    }

    void OnCollapsed()
    {
        if (Caught) return;
        StartCoroutine(CollapseRoutine());
    }

    IEnumerator CollapseRoutine()
    {
        Caught = true;
        bool first = !KnowsCollapse;
        KnowsCollapse = true; // 레이는 이 장면을 잊지 않는다
        StoryState.Say("", "(쿵 — 유리 너머의 몸이 무너진다. 스피커가 끊긴다)", 1.6f);
        yield return Wait(1.4f); // 쓰러지는 모습을 보여 준 뒤에 시간을 늦춘다(바로 늦추면 선 채로 멈춰 보인다)
        StoryState.HoldTime = true;
        StoryState.Say(Ray, first ? "…안 돼. 숨을 안 쉬어." : "…또 늦었어.", 2f);
        Time.timeScale = 0.05f;
        yield return StoryState.Sleep(0.8f);
        bool canRewind = walkman && walkman.CurrentCharges > 0 && TimeRewind.RecordedSeconds >= 2f;
        if (canRewind)
        {
            StoryState.Prompt = "R 을 길게 눌러 되감는다";
            yield return new WaitUntil(() => walkman.Rewinding);
            StoryState.HoldTime = false;
            StoryState.Prompt = null;
            yield return new WaitUntil(() => !walkman.Rewinding);
            Time.timeScale = 1f;
        }
        else
        {
            StoryState.HoldTime = false;
            Time.timeScale = 1f;
            StoryState.ControlLocked = true;
            yield return FadeTo(1f, 0.8f);
            if (GameManager.Instance) GameManager.Instance.RespawnNow(); // 부스도 처음부터(ExperimentBooth가 되돌린다)
            gameplayRig.SnapBehindTarget();
            yield return Wait(0.8f);
            StoryState.ControlLocked = false;
            yield return FadeTo(0f, 0.8f);
            if (booth) booth.Begin();
        }
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.4f);
        CurrentPhase = Phase.Studio;
        StoryState.Say(Ray, "…아직 살아 있어. 이번엔 장비부터 끈다.", 2.6f);
        StoryState.Objective = "조정실의 녹음 장비를 끈다";
        StoryState.Waypoint = switchUse ? switchUse.transform : null;
        Caught = false;
    }

    void OnSwitch()
    {
        if (switched || !booth) return;
        if (!KnowsCollapse) { StoryInteractable.Refuse(); StoryState.Say(Ray, "…함부로 만졌다간 저 사람이 다칠지도 몰라.", 2.4f); return; }
        if (booth.Current != ExperimentBooth.State.Running) { StoryInteractable.Refuse(); return; }
        booth.Stop();
        switched = true;
        if (switchUse) switchUse.gameObject.SetActive(false);
    }

    IEnumerator SaveSeven()
    {
        CurrentPhase = Phase.Saved;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        StoryState.Say("", "(찢어지는 소리가 멎는다. 유리 너머의 사람이 헤드폰을 벗고 숨을 몰아쉰다)", 3.2f);
        if (booth && booth.subject)
        {
            Vector3 face = booth.subject.transform.position + Vector3.up * 1.5f;
            Vector3 eye = player.transform.position + Vector3.up * 1.6f;
            Vector3 dir = Flat(face - eye).normalized;
            Place(dynamicA, eye - dir * 0.9f + Vector3.Cross(Vector3.up, dir) * 0.45f, face, 34f);
            Shot(dynamicA, 0.6f);
        }
        yield return Wait(3.4f);
        string[,] lines =
        {
            { Seven, "…들려요? 소리가… 멈췄어.", "2.8" },
            { Ray, "괜찮아. 이제 끝났어.", "2.2" },
            { Seven, "그 사람들… 울음을 녹음해요. 공포, 분노… 끝까지 짜내면 테이프가 길어진대요.", "4.6" },
            { Seven, "마스터 테이프는 꼭대기 금고에. 처음까지 전부 되감는 테이프.", "4.0" },
            { Seven, "…대니라는 경찰이 있었어요. 우리 녹음실 문을 지키던 사람.", "3.6" },
            { Seven, "마지막 날, 제 부스 앞에서 울었어요. 그리고 그 기계를… 카세트 플레이어를 들고 나갔어요.", "4.6" },
            { Ray, "…그래서 돌아섰구나, 대니.", "2.6" },
            { Seven, "그 사람들… 당신 얘기도 했어요. 1분을 넘게 되감은 남자.", "3.8" },
            { Ray, "…날 원한다는 거군.", "2.4" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        // 경보: 복도 건너편 독방 문이 열린다
        StoryState.Say("", "(쾅 — 복도 건너편 독방 문들이 한꺼번에 열린다)", 2.6f);
        foreach (var s in subjects) if (s) s.SetActive(true);
        foreach (var d in cellDoors) if (d) StartCoroutine(Swing(d, -100f, 0.35f));
        yield return Wait(1.6f);
        StoryState.Say(Seven, "…다른 애들이에요. 이젠 아무것도 못 알아봐요. 도망쳐요!", 3f);
        yield return Wait(3.1f);
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.5f);
        hp.Invulnerable = false;
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
    }

    IEnumerator FromSubjects()
    {
        CurrentPhase = Phase.Subjects;
        StoryState.Objective = "풀려난 실험체들을 막는다";
        StoryState.Waypoint = null;
        yield return new WaitUntil(() => !zoneSubjects || zoneSubjects.IsCleared);
        yield return Wait(1f);
        StoryState.Say(Ray, "…사람이었어. 전부.", 2.4f);
        yield return FromLift();
    }

    IEnumerator FromLift()
    {
        CurrentPhase = Phase.ToLift;
        StoryState.Objective = "엘리베이터로 꼭대기에 오른다";
        StoryState.Waypoint = elevatorUse ? elevatorUse.transform : null;
        yield return new WaitUntil(() => liftUsed);
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        yield return FadeTo(1f, 0.8f);
        indoors = false;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Rain") ps.Play(true);
        if (roofEntry) player.Teleport(roofEntry.position, roofEntry.rotation);
        gameplayRig.SnapBehindTarget();
        Checkpoint(roofEntry, CpRoof);
        yield return Wait(0.3f);
        yield return FadeTo(0f, 1.0f);
        StoryState.ControlLocked = false;
        yield return FromRoof();
    }

    // ---------- 구역 3: 옥상 ----------

    IEnumerator FromRoof()
    {
        CurrentPhase = Phase.Roof;
        StoryState.Say(Ray, "…꼭대기. 금고실은 저 펜트하우스 안이다.", 2.6f);
        StoryState.Objective = "옥상의 정리반을 뚫고 금고실로 간다";
        StoryState.Waypoint = vaultTrigger ? vaultTrigger.transform : null;
        yield return new WaitUntil(() => (!zoneRoof || zoneRoof.IsCleared) && Reached(vaultTrigger));
        yield return Vault();
    }

    void OnZoneEntered(CombatZone zone)
    {
        if (zone == zoneSubjects) StoryState.Say("", "(사람의 목소리라기엔 너무 갈라진 비명)", 2.2f);
        else if (zone == zoneRoof) StoryState.Say("정리반", "엘리베이터 문 열린다! 쏴!", 2f);
    }

    IEnumerator Vault()
    {
        CurrentPhase = Phase.Vault;
        // 펜트하우스 안: 지붕이 있다 — 비를 멈춘다(빗소리는 멀게)
        indoors = true;
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.name == "Rain") ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (partner)
        {
            partner.gameObject.SetActive(true);
            partnerStart = partner.transform.position;
            partner.Deactivate();
        }
        if (vaultShot && partner) { Place(dynamicA, vaultShot.position, partner.transform.position + Vector3.up * 1.5f, 36f); Shot(dynamicA, 0.8f); }
        string[,] lines =
        {
            { "", "(활짝 열린 금고 — 텅 빈 선반. 테이프 하나 놓였던 자국만 남았다)", "3.4" },
            { Partner, "늦었어, 레이. 마스터는 어젯밤에 옮겼다.", "3.0" },
            { Ray, "…7년이다. 날 쫓아낸 것도 너였냐.", "3.0" },
            { Partner, "널 살린 것도 나야. 그때 끝까지 파고들었으면, 너도 저 부스에 앉아 있었을걸.", "4.4" },
            { Ray, "그 입 닥쳐.", "1.8" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.5f);
        hp.Invulnerable = false;
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        CurrentPhase = Phase.Duel;
        StoryState.Objective = "옛 파트너를 붙잡는다";
        if (partner) { var ph = partner.GetComponent<Health>(); ph.floor = Mathf.FloorToInt(ph.Max * 0.4f); partner.Activate(); } // 40%에서 도망친다: 한 방에 쓰러지지 않게
        yield return new WaitUntil(() => partnerBroken);
        yield return PartnerEscapes();
    }

    void OnPartnerHit(int amount, Vector3 from)
    {
        if (partnerBroken || CurrentPhase != Phase.Duel || !partner) return;
        var h = partner.GetComponent<Health>();
        if (h.Current <= h.Max * 0.4f) { partnerBroken = true; h.Invulnerable = true; partner.Deactivate(); }
    }

    IEnumerator PartnerEscapes()
    {
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (partner && partnerLift)
        {
            partner.PlaceAt(partnerLift.position, partnerLift.rotation);
            if (vaultShot) { Place(dynamicA, vaultShot.position, partnerLift.position + Vector3.up * 1.5f, 34f); Shot(dynamicA, 0.6f); }
        }
        string[,] lines =
        {
            { Partner, "…여전하군. 그 고집 때문에 대니도 죽은 거야.", "3.4" },
            { Partner, "다음엔 네 발로 와. 마스터 테이프가 있는 곳으로.", "3.2" },
            { "", "(전용 엘리베이터 문이 닫힌다)", "2.2" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            if (i == 2 && partner) partner.gameObject.SetActive(false);
            yield return Wait(t + 0.2f);
        }
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.6f);
        StoryState.Say(Ray, "…다음엔 놓치지 않는다.", 2.4f);
        yield return Wait(2.6f);
        StoryState.SkipRequested = false;
        CurrentPhase = Phase.End;
        StoryState.ControlLocked = true;
        yield return FadeTo(1f, 1.6f);
        StoryState.Title = "3장   끝";
        yield return StoryState.Sleep(3f);
        StoryState.Title = null;
        yield return StoryState.Sleep(1f);
        hp.Invulnerable = false;
        // 4장으로: 테이프·회복약을 그대로 들고 간다
        SaveGame.CarryOver("Chapter4", "c4_start", walkman);
        SceneLoader.Load("Chapter4");
    }

    // ---------- 들켰다(하역장): 시간이 멈추고 되감는다 ----------

    void OnSpotted(PatrolSight s) { if (CanBeCaught()) StartCoroutine(CaughtRoutine(s.transform)); }
    void OnSearchlight(Searchlight s) { if (CanBeCaught()) StartCoroutine(CaughtRoutine(null)); }

    bool CanBeCaught() => !Caught && !StoryState.CutsceneActive && CurrentPhase is Phase.Street or Phase.Dock;

    IEnumerator CaughtRoutine(Transform by)
    {
        Caught = true;
        StoryState.HoldTime = true;
        StoryState.Say("경비", by ? "거기! 누구야!" : "카메라에 사람이 잡혔다!", 1.8f);
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
            StoryState.Say(Ray, "…다시.", 1.6f);
        }
        else
        {
            StoryState.HoldTime = false;
            Time.timeScale = 1f;
            StoryState.ControlLocked = true;
            StoryState.Say(Ray, "…여기까지인가.", 1.8f);
            yield return FadeTo(1f, 0.8f);
            if (GameManager.Instance) GameManager.Instance.RespawnNow();
            gameplayRig.SnapBehindTarget();
            yield return Wait(1.0f);
            StoryState.ControlLocked = false;
            yield return FadeTo(0f, 0.8f);
        }
        Caught = false;
    }

    // ---------- 가게(테이프 노점) ----------

    void OnShopApproach()
    {
        Checkpoint(shopSave, CpShop);
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…이런 데 노점이.", 2f);
    }

    void OnShopOpened(TapeShop opened)
    {
        if (opened != shop) return;
        Checkpoint(shopSave, CpShop);
        if (shopGreeted) return;
        shopGreeted = true;
        StoryState.Say("노점 아주머니", "…저 건물 들어가려고? 나도 저기서 울었어. 사흘 밤낮을. 동쪽 하역장 카메라는 왼쪽부터 돌아.", 5f);
    }

    void OnTraded(TapeShop at, CassetteTape given, CassetteTape received)
    {
        if (at == shop) SaveGame.Write(CurrentPhase >= Phase.Dock ? CpDock : CpShop, walkman);
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
        if (phase >= Phase.Studio)
        {
            doorUsed = true;
            GoIndoors();
        }
        if (phase >= Phase.Roof)
        {
            switched = liftUsed = KnowsCollapse = true;
            if (booth) booth.gameObject.SetActive(false);
            if (switchUse) switchUse.gameObject.SetActive(false);
            indoors = false;
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Rain") ps.Play(true);
        }
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at);
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        if (phase == Phase.Roof) yield return FromRoof();
        else if (phase == Phase.Studio) yield return FromStudio();
        else if (phase == Phase.Dock) yield return FromDock();
        else yield return FromStreet();
    }

    // ---------- 도우미 ----------

    IEnumerator Swing(Transform hinge, float to, float seconds)
    {
        float from = hinge.localEulerAngles.y;
        if (from > 180f) from -= 360f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
        {
            hinge.localRotation = Quaternion.Euler(0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)), 0f);
            yield return null;
        }
        hinge.localRotation = Quaternion.Euler(0f, to, 0f);
    }

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
