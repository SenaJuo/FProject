using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 1장 「B면」 진행. 지금은 구역 1(대니네 동네 → 근린공원 → 큰길의 가게 1)까지.
///  1) 대니 집 뒷마당에서 시작(구역 시작점 자동 저장). 손에는 프롤로그에서 되감는 데 쓴 믹스테이프(재생 불가, 되감기 1회).
///  2) 공원 입구 화장실 모퉁이에서 손전등에 걸린다 → 화면이 느려지고 R을 누를 때까지 기다린다 → 실전 첫 되감기(믹스테이프의 마지막 1회).
///     되감고 나면 그 조직원은 모퉁이를 오가며 순찰한다. 이번에는 숨어서 지나간다.
///  3) 공원 관리 도로의 검은 차 옆에서 통화를 엿듣는다(대니가 숨긴 것, 정리반). 차 번호판은 경찰 업무용이다. 차 옆 상자에 상표 없는 테이프.
///  4) 공원 출구를 지키는 세 명과는 피할 수 없는 싸움.
///  5) 큰길 모퉁이의 「심야 레코드」: 저장 지점. 다 쓴 믹스테이프를 재생용 테이프로 바꾼다.
///  6) 큰길을 따라 시내 쪽으로 가면 (이어서 계속).
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
    public CinemachineCamera dynamicA;
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

    [Header("가게 1 · 끝")]
    public TapeShop shop;
    public StoryTrigger shopApproach;
    public Transform shopSave;
    public Transform northEnd;
    public StoryTrigger endTrigger;

    readonly List<CinemachineCamera> cutsceneCameras = new();
    Walkman walkman;
    bool callDone, plateSeen, tapeLineDone, shopGreeted, traded;

    public enum Phase { Opening, Sneak, ForcedRewind, Park, ExitFight, Avenue, End }
    public Phase CurrentPhase { get; private set; }

    void Awake()
    {
        StoryState.ResetAll();
        if (dynamicA) cutsceneCameras.Add(dynamicA);
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
        TapeShop.Opened += OnShopOpened;
        TapeShop.Traded += OnTraded;
        CombatZone.Entered += OnZoneEntered;
        if (exitGunner) exitGunner.FirstAim = () => Tutorial.Show("aim");

        if (GameManager.Instance) GameManager.Instance.Save(zoneStart);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        TapeShop.Opened -= OnShopOpened;
        TapeShop.Traded -= OnTraded;
        CombatZone.Entered -= OnZoneEntered;
    }

    void Update()
    {
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

        CurrentPhase = Phase.Avenue;
        yield return Wait(0.8f);
        StoryState.Say(Ray, "…하아. 놈들 차가 시내 쪽으로 빠졌어. 따라가 보자.", 3.2f);
        StoryState.Objective = "큰길을 따라 시내 쪽으로 간다";
        StoryState.Waypoint = northEnd;

        yield return new WaitUntil(() => endTrigger.Fired);
        CurrentPhase = Phase.End;
        StoryState.ControlLocked = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        yield return FadeTo(1f, 1.4f);
        StoryState.Say("", "(이어서 계속)", 30f);
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
        yield return new WaitForSecondsRealtime(0.6f);
        StoryState.Objective = "되감아서 들키기 전으로 돌아간다";
        StoryState.Prompt = "R 을 길게 눌러 되감는다   ·   5초 전, 들키기 전으로";
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
        if (zone != exitZone) return;
        CurrentPhase = Phase.ExitFight;
        StoryState.Say(Thug, "여기로 나온다! 잡아!", 2f);
    }

    // ---------- 가게 1 ----------

    void OnShopApproach()
    {
        // 가게는 저장 지점이다(찾아가는 목표가 아니라, 길에 있으면 들른다)
        if (GameManager.Instance) GameManager.Instance.Save(shopSave);
        Tutorial.Show("shop");
        if (!StoryState.LineVisible) StoryState.Say(Ray, "…이 새벽에 불 켜진 레코드 가게라.", 2.6f);
    }

    void OnShopOpened(TapeShop opened)
    {
        if (opened != shop) return;
        if (GameManager.Instance) GameManager.Instance.Save(shopSave);
        if (shopGreeted) return;
        shopGreeted = true;
        // 주인은 안쪽 방에 있다(목소리만 들린다)
        StoryState.Say("가게 주인 (안쪽에서)", "…이 시간에 손님이라. 둘러봐. 다 쓴 테이프가 있으면 바꿔 주지.", 4f);
    }

    void OnTraded(TapeShop at, CassetteTape given, CassetteTape received)
    {
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
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until && !StoryState.SkipRequested) yield return null;
    }

    static IEnumerator FadeTo(float target, float seconds)
    {
        float start = StoryState.Fade, t = 0f;
        while (t < seconds && !StoryState.SkipRequested)
        {
            t += Time.unscaledDeltaTime;
            StoryState.Fade = Mathf.Lerp(start, target, t / seconds);
            yield return null;
        }
        StoryState.Fade = target;
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
