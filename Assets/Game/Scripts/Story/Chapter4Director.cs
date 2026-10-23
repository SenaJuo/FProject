using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 4장 「송신소」 감독 — 산 위 한결 FM 송신소에서 옛 파트너와 결판.
///  구역 1 산길: 국도변 노점(저장) → 굽잇길 매복(정리반) → 송신소 정문(저장)
///         매복(2026-10-23): 처음 올라서면 숨어 있던 넷이 한꺼번에 일어나 겨눈다 — 그 순간 시간이 멈추고 고른다(R 되감기 / Space 싸움).
///         되감으면 레이만 그 자리를 안다: 바닥에 놈들의 시야가 보이고, 아직 눈치채지 못했으니 돌아 들어가 하나씩 몰래 제압할 수 있다(들키면 그냥 싸움)
///  구역 2 송신소 마당: 파트너와 결투 → 송신탑 발판의 저격수에게 맞아 쓰러진다
///         → 분노로 1분 가까이 되감긴다(프롤로그처럼 — 레이만 기억) → 저격수를 먼저 처리
///         → 파트너가 눈치채고 본부에 보고("또 일어났다") → 결투 → 무너진 파트너의 자백 → 입막음 저격.
/// </summary>
public class Chapter4Director : MonoBehaviour
{
    const string Ray = "레이";
    const string Partner = "옛 파트너";

    public PlayerController player;
    public CinemachineBrain brain;
    public CinemachineCamera gameplayCamera, dynamicA;
    public ThirdPersonCamera gameplayRig;
    public AudioSource rainAudio;
    public RewindEffect rewindEffect;
    public Ragdoll playerRagdoll;

    [Header("구역 1 — 산길")]
    public Transform zoneStart;
    public TapeShop shop;
    public StoryTrigger shopApproach;
    public Transform shopSave, roadSave;
    public CombatZone zoneAmbush;
    public StoryTrigger gateTrigger;

    [Header("구역 2 — 송신소")]
    public Transform gateSave, partnerStart, duelShot;
    public StoryTrigger yardTrigger;
    public EnemyAI partner, sniper;
    public CombatZone zoneSniper;

    public enum Phase { Opening, Road, Gate, Duel1, Shot, Hunt, Duel2, Confess, End }
    public Phase CurrentPhase { get; private set; }
    /// <summary>레이가 본 저격(되감아도 레이는 기억한다)</summary>
    public bool KnowsSniper { get; private set; }
    /// <summary>레이가 겪은 매복(되감아도 레이는 기억한다)</summary>
    public bool KnowsAmbush { get; private set; }
    bool ambushArmed, ambushSprung, ambushRevealed, springing, ambushDone, ambushLineSaid;
    int seenGeneration;
    Bounds ambushArea; // 꺼 둔 충돌체의 bounds는 비어 있으므로 끄기 전에 적어 둔다
    Vector3[] ambushHome;
    Quaternion[] ambushRot;

    Walkman walkman;
    bool shopGreeted, partnerAt60, partnerAt40, inYardFight;
    Coroutine main;
    readonly List<CinemachineCamera> cutsceneCameras = new();

    const string CpStart = "c4_start", CpShop = "c4_shop", CpRoad = "c4_road", CpGate = "c4_gate";

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
        if (shopApproach) shopApproach.Entered += OnShopApproach;
        if (partner) partner.GetComponent<Health>().Damaged += OnPartnerHit;
        if (zoneSniper) zoneSniper.GetComponent<Collider>().enabled = false; // 처음엔 아무도 모른다(되감은 뒤에 켠다)
        TapeShop.Opened += OnShopOpened;
        TapeShop.Traded += OnTraded;
        CombatZone.Entered += OnZoneEntered;
        GameManager.PlayerRespawned += OnRespawned;
        PatrolSight.Spotted += OnAmbushSpotted;

        var resume = SaveGame.TakePending(gameObject.scene.name);
        if (resume != null) SaveGame.ApplyInventory(resume, walkman);
        string cp = resume?.checkpoint;
        if (cp == CpGate || cp == CpRoad)
        {
            // 매복은 이미 정리했다(저장은 정리한 뒤에만 된다): 다시 불러와도 되살아난 넷이 그 자리에서 다시 터지지 않게 치운다
            ambushDone = true;
            if (zoneAmbush)
            {
                zoneAmbush.GetComponent<Collider>().enabled = false;
                foreach (var e in zoneAmbush.enemies) if (e) e.gameObject.SetActive(false);
            }
        }
        if (cp == CpGate) { main = StartCoroutine(Resume(gateSave, true)); return; }
        if (cp == CpRoad) { main = StartCoroutine(Resume(roadSave, false)); return; }
        if (cp == CpShop) { main = StartCoroutine(Resume(shopSave, false)); return; }
        Checkpoint(zoneStart, CpStart);
        main = StartCoroutine(Run());
    }

    void OnDestroy()
    {
        TapeShop.Opened -= OnShopOpened;
        TapeShop.Traded -= OnTraded;
        CombatZone.Entered -= OnZoneEntered;
        GameManager.PlayerRespawned -= OnRespawned;
        PatrolSight.Spotted -= OnAmbushSpotted;
    }

    void Update()
    {
        if (rainAudio) rainAudio.volume = 0.5f * GameSettings.Gain(GameSettings.Channel.Sfx);
        if (!ambushArmed || !zoneAmbush) return;
        var list = zoneAmbush.enemies;
        // 아직 터지기 전: 넷은 숨어 있다. 되감기는 적을 늘 보이게 되돌리므로(EnemyAI) 다시 숨긴다
        if (!KnowsAmbush && !ambushSprung)
            foreach (var e in list) if (e && e.gameObject.activeSelf && !e.Health.IsDead) e.gameObject.SetActive(false);
        // 되감기가 끝났다: 싸움이 시작되기 전으로 돌아갔으면 다시 숨은 자리에(시야도 다시)
        if (TimeRewind.Generation != seenGeneration && walkman && !walkman.Rewinding)
        {
            seenGeneration = TimeRewind.Generation;
            if (KnowsAmbush && !springing && !zoneAmbush.Started && !zoneAmbush.IsCleared) ambushRevealed = false;
        }
        // 매복: 처음엔 굽잇길 평지에 올라서면 터진다
        if (!KnowsAmbush && !ambushSprung && CurrentPhase == Phase.Road && !player.IsDead && ambushArea.Contains(player.transform.position))
            StartCoroutine(SpringAmbush());
        // 겪고 나서 되감았다(또는 다시 시작했다): 놈들은 아직 숨어 있고, 레이는 자리를 안다
        if (KnowsAmbush && !ambushRevealed && !springing && !player.IsDead && walkman && !walkman.Rewinding) RevealAmbush();
        // 몰래 다 쓰러뜨렸다: 구역을 정리한 것으로
        if (ambushRevealed && !zoneAmbush.IsCleared && System.Array.TrueForAll(list, e => !e || e.Health.IsDead)) zoneAmbush.Engage();
    }

    /// <summary>굽잇길 매복을 건다: 넷은 숨어 있다(보이지 않는다). 구역 트리거 대신 감독이 들어섬을 본다</summary>
    void ArmAmbush()
    {
        if (!zoneAmbush || zoneAmbush.IsCleared || ambushArmed || ambushDone) return;
        ambushArmed = true;
        var col = zoneAmbush.GetComponent<Collider>();
        ambushArea = col.bounds;
        col.enabled = false;
        var list = zoneAmbush.enemies;
        ambushHome = new Vector3[list.Length];
        ambushRot = new Quaternion[list.Length];
        for (int i = 0; i < list.Length; i++)
        {
            if (!list[i]) continue;
            ambushHome[i] = list[i].transform.position;
            ambushRot[i] = list[i].transform.rotation;
            // 시야는 처음부터 붙여 둔다: 되감기 기록에 들어가 있어야 "들킨 뒤 되감기"로 들키기 전으로 돌아간다
            if (!list[i].GetComponent<PatrolSight>())
            {
                var sight = list[i].gameObject.AddComponent<PatrolSight>();
                sight.viewDistance = 13f;
                sight.viewAngle = 34f;
                sight.alertRadius = 30f; // 하나가 소리치면 넷 모두
                sight.spottedLine = "어떻게 알고…! 쏴!";
            }
            if (!KnowsAmbush) list[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 처음: 넷이 한꺼번에 일어선다 — 총구가 다 레이를 향한 순간 시간이 멈춘다. 레이만 그 자리를 본다.
    /// R = 올라서기 전으로 되감는다(놈들은 아직 모른다 → 몰래 정리), Space = 그대로 싸운다(2026-10-23: 무조건 쓰러지던 것을 고를 수 있게).
    /// 되감기 횟수가 없으면 그대로 싸운다
    /// </summary>
    IEnumerator SpringAmbush()
    {
        ambushSprung = true;
        springing = true;
        KnowsAmbush = true;
        StoryState.HoldTime = true; // 올라선 순간에서 기록을 멈춘다: 되감으면 올라서기 전으로
        StoryState.ControlLocked = true; // 일어서는 동안 R·공격을 받지 않는다(고르는 안내가 뜰 때 푼다)
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        var list = zoneAmbush.enemies;
        foreach (var e in list) if (e && !e.Health.IsDead) { e.gameObject.SetActive(true); e.Deactivate(); FacePlayer(e); }
        StoryState.Say("정리반", "지금이다! 쏴!", 1.4f);
        Time.timeScale = 0.3f; // 사방에서 일어서는 게 보이게
        yield return StoryState.Sleep(0.5f);
        foreach (var e in list) if (e && !e.Health.IsDead) e.Activate(); // 총 든 놈은 붉은 조준선을 겨눈다
        yield return StoryState.Sleep(0.35f);
        Time.timeScale = 0.03f;
        StoryState.Say(Ray, "…매복이다. 넷.", 2f);
        yield return StoryState.Sleep(0.4f);

        bool rewound = false;
        StoryState.ControlLocked = false;
        if (walkman && walkman.CurrentCharges > 0 && TimeRewind.RecordedSeconds >= 2f)
        {
            StoryState.AskRewind("R 되감는다   ·   Space 그대로 싸운다");
            var jump = UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/Jump", true);
            while (!walkman.Rewinding && !jump.WasPressedThisFrame()) yield return null;
            rewound = walkman.Rewinding;
        }
        StoryState.HoldTime = false;
        StoryState.Prompt = null;
        hp.Invulnerable = false;
        if (rewound)
        {
            yield return new WaitUntil(() => !walkman.Rewinding);
            Time.timeScale = 1f;
            springing = false; // 되감아 일어섰다: Update가 자리를 드러낸다
            yield break;
        }
        // 그대로 싸운다(또는 되감을 수 없다)
        StoryState.RewindAsked = false;
        Time.timeScale = 1f;
        ambushRevealed = true;
        springing = false;
        zoneAmbush.Engage();
    }

    void FacePlayer(EnemyAI e)
    {
        Vector3 to = player.transform.position - e.transform.position; to.y = 0f;
        if (to.sqrMagnitude > 0.01f) e.transform.rotation = Quaternion.LookRotation(to);
    }

    /// <summary>되감은 뒤: 넷을 제자리에(아직 모른다), 바닥에 시야를 보여 준다</summary>
    void RevealAmbush()
    {
        ambushRevealed = true;
        if (!zoneAmbush || zoneAmbush.IsCleared) return;
        var sights = new List<PatrolSight>();
        var list = zoneAmbush.enemies;
        for (int i = 0; i < list.Length; i++)
        {
            var e = list[i];
            if (!e || e.Health.IsDead) continue;
            e.gameObject.SetActive(true);
            e.Deactivate();
            if (ambushHome != null) e.PlaceAt(ambushHome[i], ambushRot[i]);
            var sight = e.GetComponent<PatrolSight>();
            if (sight) { sight.Rearm(); sights.Add(sight); }
        }
        RewindMemory.ShowSights(sights, 600f);
        if (!ambushLineSaid) StoryState.Say(Ray, "…넷. 양옆에 총 든 놈 둘, 길 가운데 둘. 아직 내가 온 줄 모른다.", 4f);
        ambushLineSaid = true;
    }

    void OnAmbushSpotted(PatrolSight s)
    {
        if (springing || !zoneAmbush || zoneAmbush.Started || System.Array.IndexOf(zoneAmbush.enemies, s.Enemy) < 0) return;
        zoneAmbush.Engage(); // 들켰다: 그냥 싸움(넷 다 깨어난다)
    }

    // ---------- 구역 1: 산길 ----------

    IEnumerator Run()
    {
        CurrentPhase = Phase.Opening;
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        StoryState.Title = "4장   송신소";
        yield return Wait(2.6f);
        StoryState.Title = null;
        yield return FadeTo(0f, 1.6f);
        StoryState.ControlLocked = false;
        StoryState.Say(Ray, "…놈의 차가 이 산으로 올라갔다. 한결 FM 송신소.", 3.2f);
        yield return Wait(3.4f);
        StoryState.Say(Ray, "이번엔 도망 못 간다.", 2.2f);
        yield return FromRoad();
    }

    IEnumerator FromRoad()
    {
        CurrentPhase = Phase.Road;
        ArmAmbush();
        StoryState.Objective = "산길을 올라 송신소로 간다";
        StoryState.Waypoint = gateTrigger ? gateTrigger.transform : null;
        bool saved = false;
        while (!Reached(gateTrigger))
        {
            if (!saved && zoneAmbush && zoneAmbush.IsCleared) { saved = true; Checkpoint(roadSave, CpRoad); StoryState.Say(Ray, "…길을 막은 걸 보니, 위에 있긴 있군.", 2.6f); }
            yield return null;
        }
        Checkpoint(gateSave, CpGate);
        yield return FromGate();
    }

    void OnZoneEntered(CombatZone zone)
    {
        if (zone == zoneAmbush) { if (!System.Array.TrueForAll(zone.enemies, e => !e || e.Health.IsDead)) StoryState.Say("정리반", "올라온다! 길 막아!", 2f); } // 몰래 다 쓰러뜨려 정리된 경우는 소리칠 사람이 없다
        else if (zone == zoneSniper) StoryState.Say(Ray, "…거기 있는 거 안다.", 1.8f);
    }

    // ---------- 구역 2: 송신소 ----------

    IEnumerator FromGate()
    {
        CurrentPhase = Phase.Gate;
        ResetPartner();
        StoryState.Objective = KnowsSniper ? "송신탑 발판의 저격수를 먼저 처리한다" : "송신소 마당으로 들어간다";
        StoryState.Waypoint = KnowsSniper && sniper ? sniper.transform : yardTrigger ? yardTrigger.transform : null;
        if (KnowsSniper && sniper && !sniper.Health.IsDead) yield return Hunt();
        yield return new WaitUntil(() => Reached(yardTrigger) || inYardFight);
        yield return DuelIntro();
    }

    void ResetPartner()
    {
        if (!partner) return;
        partnerAt60 = partnerAt40 = false;
        inYardFight = false;
        partner.gameObject.SetActive(true);
        var h = partner.GetComponent<Health>();
        h.Revive();
        h.Invulnerable = true;
        if (partnerStart) partner.PlaceAt(partnerStart.position, partnerStart.rotation);
        partner.Deactivate();
    }

    IEnumerator DuelIntro()
    {
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (duelShot && partner) { Place(dynamicA, duelShot.position, partner.transform.position + Vector3.up * 1.5f, 34f); Shot(dynamicA, 0.8f); }
        bool afterHunt = KnowsSniper && sniper && sniper.Health.IsDead;
        string[,] lines = afterHunt
            ? new[,]
            {
                { Partner, "…저격수 자리를 어떻게 알았지? 올라오는 길에 한 번도 안 쳐다봤잖아.", "4.0" },
                { Ray, "글쎄.", "1.4" },
                { Partner, "……설마. 또 감았나.", "2.4" },
                { "", "(파트너가 무전기를 든다)", "1.8" },
                { Partner, "본부. 레이다. …또 일어났다. 1분 가까이.", "3.4" },
                { Partner, "이제 회사가 직접 널 데리러 갈 거다, 레이. 그 전에 내가 끝내 주지.", "4.0" },
            }
            : new[,]
            {
                { Partner, "역시 왔군. 너라면 엘리베이터 기록부터 뒤질 줄 알았지.", "3.6" },
                { Ray, "7년 동안 웃으면서 내 뒤통수를 본 거냐.", "3.0" },
                { Partner, "넌 몰라. 그 회사가 뭘 할 수 있는지. 난 살아남는 쪽을 골랐을 뿐이야.", "4.2" },
                { Ray, "대니는 못 살아남았지.", "2.4" },
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
        inYardFight = true;
        CurrentPhase = afterHunt ? Phase.Duel2 : Phase.Duel1;
        StoryState.Objective = "옛 파트너를 쓰러뜨린다";
        if (partner)
        {
            var ph = partner.GetComponent<Health>();
            ph.Invulnerable = false;
            ph.floor = Mathf.FloorToInt(ph.Max * (CurrentPhase == Phase.Duel1 ? 0.6f : 0.4f)); // 저격(60%)·자백(40%) 장면을 한 방이 건너뛰지 않게
            partner.Activate();
        }
        if (CurrentPhase == Phase.Duel1)
        {
            yield return new WaitUntil(() => partnerAt60);
            if (sniper && !sniper.Health.IsDead) { yield return SniperShot(); yield break; }
            CurrentPhase = Phase.Duel2; // 저격수를 미리 없앴다(되감기 없이): 그대로 이어서 싸운다
            if (partner) { var ph = partner.GetComponent<Health>(); ph.floor = Mathf.FloorToInt(ph.Max * 0.4f); }
        }
        yield return new WaitUntil(() => partnerAt40);
        yield return Confess();
    }

    void OnPartnerHit(int amount, Vector3 from)
    {
        if (!partner) return;
        var h = partner.GetComponent<Health>();
        if (CurrentPhase == Phase.Duel1 && !partnerAt60 && h.Current <= h.Max * 0.6f) partnerAt60 = true;
        if (CurrentPhase == Phase.Duel2 && !partnerAt40 && h.Current <= h.Max * 0.4f) { partnerAt40 = true; h.floor = 0; h.Invulnerable = true; partner.Deactivate(); } // 자백 뒤 입막음으로 쓰러질 수 있게 바닥을 푼다
    }

    /// <summary>송신탑 발판의 저격수 — 레이가 쓰러진다. 분노가 테이프를 1분 가까이 감는다</summary>
    IEnumerator SniperShot()
    {
        CurrentPhase = Phase.Shot;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (partner) { partner.Deactivate(); partner.GetComponent<Health>().Invulnerable = true; }
        Vector3 from = sniper ? sniper.transform.position + Vector3.up * 1.5f : player.transform.position + Vector3.up * 10f;
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("SniperShot") ?? ProceduralSfx.Gunshot, from, 1.3f, 0.4f);
        // 쓰러짐: 컨트롤러를 끄고 물리로 넘어간다(프롤로그와 같다 — 켜 두면 몸이 선 채로 붙잡힌다)
        player.GetComponent<CharacterController>().enabled = false;
        if (playerRagdoll) playerRagdoll.Activate((player.transform.position - from).normalized * 60f + Vector3.up * 10f, HumanBodyBones.Chest);
        StoryState.Say("", "(탕 — 송신탑 쪽에서 섬광. 가슴이 뜨겁다)", 2.4f);
        Vector3 body = player.transform.position + Vector3.up * 0.4f;
        Place(dynamicA, body + new Vector3(1.6f, 1.2f, -1.6f), body, 40f);
        Shot(dynamicA, 0.4f);
        yield return Wait(2.6f);
        if (duelShot && partner) { Place(dynamicA, duelShot.position, partner.transform.position + Vector3.up * 1.5f, 32f); Shot(dynamicA, 0.6f); }
        StoryState.Say(Partner, "…미안하다, 레이. 이번엔 진짜 끝이야.", 3f);
        yield return Wait(3.2f);
        Place(dynamicA, body + new Vector3(0.6f, 0.4f, -0.8f), body, 36f);
        Shot(dynamicA, 0.5f);
        StoryState.Say(Ray, "……아니. 아직이야.", 2.4f);
        yield return Wait(2.2f);
        StoryState.Say("", "(주머니 속 카세트 플레이어가 저절로 미친 듯이 감기기 시작한다)", 2.8f);
        yield return Wait(1.6f);
        yield return LongRewind();
    }

    /// <summary>
    /// 1분 가까운 되감기(프롤로그와 같은 연출): 기록된 장면을 거꾸로 보여 주고 하얗게 바랜 뒤, 송신소 정문으로 돌아간다.
    /// 기록은 16초뿐이라 그 너머는 하얀 빛 속에서 건너뛴다 — 레이만 저격수 자리를 기억한다.
    /// </summary>
    IEnumerator LongRewind()
    {
        KnowsSniper = true;
        if (rewindEffect) { rewindEffect.StartWhir(); rewindEffect.SetWeight(1f); }
        TimeRewind.RecordNow();
        TimeRewind.PlayingBack = true;
        Time.timeScale = 0f;
        float cursor = TimeRewind.Clock;
        float end = cursor - TimeRewind.RecordedSeconds + 0.05f;
        float speed = 5f;
        StoryState.FadeColor = Color.white;
        while (cursor > end && !StoryState.SkipRequested)
        {
            cursor -= StoryState.DeltaTime * speed;
            speed = Mathf.Min(9f, speed + StoryState.DeltaTime * 2.5f);
            TimeRewind.Preview(TimeRewind.At(cursor));
            if (playerRagdoll && playerRagdoll.IsActive && TimeRewind.Clock - cursor > 3f) playerRagdoll.Restore();
            if (cursor - end < 1.2f) StoryState.Fade = Mathf.Clamp01(1f - (cursor - end) / 1.2f);
            yield return null;
        }
        StoryState.Fade = 1f;
        if (rewindEffect) rewindEffect.StopWhir();
        TimeRewind.RewindTo(TimeRewind.At(Mathf.Max(end, cursor)));
        TimeRewind.PlayingBack = false;
        TimeRewind.Clear();
        Time.timeScale = 1f;
        if (playerRagdoll && playerRagdoll.IsActive) playerRagdoll.Restore();
        player.GetComponent<CharacterController>().enabled = true;
        // 1분 전: 송신소 정문
        if (gateSave) player.Teleport(gateSave.position, gateSave.rotation);
        var hp = player.GetComponent<Health>();
        hp.Revive();
        ResetPartner();
        if (zoneSniper) zoneSniper.GetComponent<Collider>().enabled = true;
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0f);
        yield return Wait(0.6f);
        if (rewindEffect) rewindEffect.SetWeight(0f);
        yield return FadeTo(0f, 1.2f);
        StoryState.FadeColor = Color.black;
        hp.Invulnerable = false;
        StoryState.CutsceneActive = false;
        StoryState.SkipRequested = false;
        StoryState.Say(Ray, "……하아, 하아. 정문이다.", 2.2f);
        yield return Wait(2.4f);
        StoryState.Say(Ray, "또 1분을 감았어. …저격수. 송신탑 발판이다.", 3f);
        yield return FromGate();
    }

    IEnumerator Hunt()
    {
        CurrentPhase = Phase.Hunt;
        StoryState.Objective = "송신탑 발판의 저격수를 먼저 처리한다";
        StoryState.Waypoint = sniper ? sniper.transform : null;
        yield return new WaitUntil(() => !sniper || sniper.Health.IsDead);
        StoryState.Say(Ray, "…이번엔 네가 먼저야.", 2f);
        yield return Wait(1.2f);
        CurrentPhase = Phase.Gate;
        StoryState.Objective = "옛 파트너에게 간다";
        StoryState.Waypoint = partner ? partner.transform : null;
        inYardFight = true; // 이미 마당 안: 다시 들어가지 않아도 결투가 시작된다
    }

    IEnumerator Confess()
    {
        CurrentPhase = Phase.Confess;
        StoryState.CutsceneActive = true;
        StoryState.Objective = null;
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = true;
        if (duelShot && partner) { Place(dynamicA, duelShot.position, partner.transform.position + Vector3.up * 1.3f, 32f); Shot(dynamicA, 0.8f); }
        string[,] lines =
        {
            { Partner, "……졌다. 하나만 알려 주지.", "2.6" },
            { Partner, "마스터 테이프는 본사 지하 연구소다.", "2.8" },
            { Partner, "다음 주 금요일, 한결 FM 30주년 기념 방송. 그날 밤 윗선이 전부 모인다. 테이프를 '쓰는' 날이야.", "5.0" },
            { Ray, "쓴다니. 뭘 되돌리려고.", "2.2" },
            { Partner, "…그건 네 눈으로 봐. 레이, 7년 전 그날 난—", "3.2" },
        };
        for (int i = 0; i < lines.GetLength(0); i++)
        {
            float t = float.Parse(lines[i, 2], System.Globalization.CultureInfo.InvariantCulture);
            StoryState.Say(lines[i, 0], lines[i, 1], t);
            yield return Wait(t + 0.2f);
        }
        // 입막음: 먼 능선에서 한 발
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("SniperShot") ?? ProceduralSfx.Gunshot, player.transform.position + new Vector3(-60f, 20f, 40f), 1.2f, 0.3f);
        if (partner)
        {
            var h = partner.GetComponent<Health>();
            h.Invulnerable = false;
            h.TakeDamage(9999, partner.transform.position + new Vector3(-6f, 2f, 4f));
        }
        StoryState.Say("", "(탕 — 먼 능선에서 한 발. 파트너가 무너진다)", 2.6f);
        yield return Wait(2.8f);
        StoryState.Say(Ray, "……입막음인가.", 2.2f);
        yield return Wait(2.4f);
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, 0.6f);
        StoryState.Say(Ray, "다음 주 금요일. 한결 FM.", 2.4f);
        yield return Wait(2.6f);
        StoryState.SkipRequested = false;
        CurrentPhase = Phase.End;
        StoryState.ControlLocked = true;
        yield return FadeTo(1f, 1.6f);
        StoryState.Title = "4장   끝";
        yield return StoryState.Sleep(3f);
        StoryState.Title = null;
        yield return StoryState.Sleep(1f);
        hp.Invulnerable = false;
        // 5장으로: 테이프·회복약을 그대로 들고 간다
        SaveGame.CarryOver("Chapter5", "c5_start", walkman);
        SceneLoader.Load("Chapter5");
    }

    /// <summary>송신소에서 쓰러져 저장 지점(정문)부터 다시: 결투도 처음부터(레이가 아는 것은 그대로)</summary>
    void OnRespawned()
    {
        if (CurrentPhase == Phase.Road && KnowsAmbush && zoneAmbush && !zoneAmbush.IsCleared) ambushRevealed = false; // 다시 숨은 자리에(시야도 다시 보인다)
        if (CurrentPhase is not (Phase.Duel1 or Phase.Duel2 or Phase.Hunt or Phase.Gate)) return;
        if (main != null) StopCoroutine(main);
        StoryState.CutsceneActive = false;
        main = StartCoroutine(FromGate());
    }

    // ---------- 가게(국도변 노점) ----------

    void OnShopApproach()
    {
        Checkpoint(shopSave, CpShop);
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…이런 산 밑에도.", 2f);
    }

    void OnShopOpened(TapeShop opened)
    {
        if (opened != shop) return;
        Checkpoint(shopSave, CpShop);
        if (shopGreeted) return;
        shopGreeted = true;
        StoryState.Say("노점 할아버지", "송신소에 가는구먼. 나는 거기서 밤마다 비명 섞인 전파를 쐈지. …굽잇길에 놈들이 진을 쳤어.", 5f);
    }

    void OnTraded(TapeShop at, CassetteTape given, CassetteTape received)
    {
        if (at == shop) SaveGame.Write(CurrentPhase >= Phase.Gate ? CpGate : CpShop, walkman);
    }

    // ---------- 저장 · 이어하기 ----------

    void Checkpoint(Transform point, string id)
    {
        if (!point) return;
        if (GameManager.Instance) GameManager.Instance.Save(point);
        SaveGame.Write(id, walkman);
    }

    IEnumerator Resume(Transform at, bool gate)
    {
        StoryState.Fade = 1f;
        StoryState.ControlLocked = true;
        if (at) player.Teleport(at.position, at.rotation);
        gameplayRig.SnapBehindTarget();
        if (GameManager.Instance && at) GameManager.Instance.Save(at);
        yield return Wait(0.4f);
        yield return FadeTo(0f, 1.2f);
        StoryState.ControlLocked = false;
        yield return gate ? FromGate() : FromRoad();
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
