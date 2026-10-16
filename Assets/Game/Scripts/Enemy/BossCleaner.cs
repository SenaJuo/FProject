using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 정리반 반장(전직 형사, 레이의 옛 선배) — 3번 부두 앞 보스. EnemyAI와 함께 붙는다.
///  1단계(체력 100~60%): 진압봉 맞싸움. 두 번째 주먹마다 붉은 강타 — 완벽 회피 뒤 반격으로 깎는다.
///  2단계(60~25%): 드럼통 뒤로 물러나 권총을 쏘고, 숨겨 둔 부하 둘을 부른다. 부하가 쓰러지거나 15초가 지나면 다시 맞싸움.
///  3단계(25% 아래): 감독(Chapter1Director)이 넘겨받는다 — 대니의 테이프를 불에 던지러 달려간다(되감기 연출).
/// 되감으면 단계도 되감는다(체력 기준으로 다시 정해진다).
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public class BossCleaner : MonoBehaviour, IRewindable
{
    public EnemyAI[] reinforcements = System.Array.Empty<EnemyAI>();
    [Tooltip("2단계에서 숨어 쏘는 자리(드럼통 뒤)")]
    public Transform coverPoint;
    [Tooltip("손에 든 대니의 테이프(3단계에서 불에 던진다)")]
    public Transform heldTape;
    public float phase2At = 0.6f, phase3At = 0.25f;

    public int Phase { get; private set; } = 1;
    /// <summary>3단계에 들어섰다(감독이 넘겨받는다)</summary>
    public bool WantsToBurn => Phase >= 3;
    public static event System.Action<BossCleaner, int> PhaseChanged;

    EnemyAI ai;
    Health health;
    NavMeshAgent agent;
    float phase2Started, baseSpeed;
    /// <summary>"애들아! 나와!"로 부하를 이미 불렀다(한 번만 — 다시 맞싸움으로 돌아와도 또 외치지 않는다)</summary>
    bool calledHelp;
    Coroutine toCover;
    /// <summary>2단계로 넘어가는 중(외치고 엄폐물로 달려가는 동안 — 무적, 공격하지 않는다)</summary>
    public bool Transitioning => toCover != null;

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        health = GetComponent<Health>();
        agent = GetComponent<NavMeshAgent>();
        baseSpeed = agent ? agent.speed : 0f;
        TimeRewind.Register(this);
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    void OnEnable() => GameManager.PlayerRespawned += OnRespawned;
    void OnDisable() => GameManager.PlayerRespawned -= OnRespawned;

    /// <summary>레이가 쓰러져 다시 시작하면 반장도 처음부터: 1단계, 부하는 다시 숨는다(외치면 또 나온다). 테이프를 태우러 간 뒤로는 감독이 맡는다</summary>
    void OnRespawned()
    {
        if (Phase >= 3) return;
        CancelTransition();
        Phase = 1; calledHelp = false;
        Apply(false);
        StartCoroutine(HideHelpers());
    }

    /// <summary>부하는 한 프레임 뒤에 숨긴다: 같은 순간 EnemyAI가 처음 자리로 옮기는데, 먼저 꺼 버리면 옮겨지지 않아 쓰러지던 자리에서 다시 나왔다</summary>
    IEnumerator HideHelpers()
    {
        yield return null;
        foreach (var r in reinforcements) if (r) r.gameObject.SetActive(false);
    }

    public object CaptureState() => new Vector2Int(Phase, calledHelp ? 1 : 0);

    public void RestoreState(object state, bool preview)
    {
        if (preview) return;
        var st = (Vector2Int)state; int p = st.x;
        // 테이프를 태우러 달리기 시작한 뒤(3단계)로는 되돌리지 않는다: 그 되감기 자체가 연출의 일부다
        if (Phase >= 3) return;
        calledHelp = st.y == 1;
        if (p == Phase) return;
        CancelTransition();
        Phase = p;
        Apply(false);
        if (Phase == 2 && !health.IsDead) ai.Activate(); // 되감아 돌아온 2단계: 달려가던 중이었어도 바로 싸운다
    }

    void Update()
    {
        // 단계를 건너뛰지 않는다(2026-10-16 "애들아! 나와!가 안 나오는 경우가 많다": 큰 한 방에 60%를 건너뛰고 25%로 떨어져 2단계가 통째로 빠졌다)
        // 부하를 부르기 전에는 60%에서, 그 뒤 테이프 소각(3단계) 전에는 25%에서 멈춘다
        health.floor = Phase >= 3 ? 0 : Mathf.FloorToInt(health.Max * (calledHelp ? phase3At : phase2At));
        if (health.IsDead || Phase >= 3 || Transitioning || !ai.IsActive) return;
        float ratio = health.Current / (float)health.Max;
        if (Phase == 1 && !calledHelp && ratio <= phase2At) { calledHelp = true; Phase = 2; phase2Started = Time.time; Apply(true); PhaseChanged?.Invoke(this, 2); }
        else if (Phase == 2)
        {
            bool helpersDown = true;
            foreach (var r in reinforcements) if (r && r.gameObject.activeInHierarchy && !r.GetComponent<Health>().IsDead) helpersDown = false;
            if (ratio <= phase3At) { Phase = 3; Apply(true); PhaseChanged?.Invoke(this, 3); }
            else if (helpersDown && Time.time - phase2Started > 4f || Time.time - phase2Started > 15f) { Phase = 1; Apply(true); }
        }
        else if (Phase == 1 && calledHelp && ratio <= phase3At) { Phase = 3; Apply(true); PhaseChanged?.Invoke(this, 3); }
    }

    /// <summary>단계에 맞게 싸움 방식을 바꾼다. fresh면 처음 들어선 것(외치고 숨는 곳으로 간다), 아니면 되감기로 돌아온 것</summary>
    void Apply(bool fresh)
    {
        bool gun = Phase == 2;
        if (gun && fresh) { toCover = StartCoroutine(RunToCover()); return; }
        ai.ranged = gun;
        ai.holdPosition = gun;
        ShowGun(gun);
        if (!gun && ai.aimIK) ai.aimIK.aim = false;
        if (Phase >= 3) ai.Deactivate();
    }

    void ShowGun(bool on)
    {
        if (ai.aimIK && ai.aimIK.heldItem) ai.aimIK.heldItem.gameObject.SetActive(on);
    }

    /// <summary>
    /// 2단계로 넘어가는 장면: 하던 주먹을 끊고 → 그 순간 외치며 부하를 부르고 → 총을 뽑아 → 엄폐물(드럼통 뒤)로 눈에 보이게 달려간 뒤 쏘기 시작한다.
    /// 예전에는 엄폐물로 순간 이동했고, 싸우던 동작이 끝난 뒤에야 외침이 들려 어색했다.
    /// </summary>
    IEnumerator RunToCover()
    {
        ai.Deactivate();          // 하던 주먹·조준을 바로 끊는다
        health.Invulnerable = true;
        StoryState.Say("정리반 반장", "애들아! 나와!", 1.8f);
        // 숨어 있던 부하: 꺼져 있던 동안은 체력이 아직 0(Awake 전)이라 '쓰러짐'으로 읽힌다 → 켜져 있지 않으면 먼저 켠다
        foreach (var r in reinforcements)
        {
            if (!r) continue;
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            if (!r.Health.IsDead) r.Activate();
        }
        yield return new WaitForSeconds(0.3f);
        ShowGun(true);            // 허리에서 권총을 뽑는다
        if (coverPoint && agent && agent.enabled && agent.isOnNavMesh)
        {
            Vector3 dest = NavMesh.SamplePosition(coverPoint.position, out var hit, 3f, NavMesh.AllAreas) ? hit.position : coverPoint.position;
            agent.speed = Mathf.Max(baseSpeed, 6.5f); // 엄폐물까지는 전력으로 달린다
            agent.isStopped = false;
            agent.SetDestination(dest);
            float until = Time.time + 3.5f;
            while (Time.time < until && (agent.pathPending || agent.remainingDistance > 0.6f)) yield return null;
            agent.speed = baseSpeed;
        }
        else if (coverPoint) ai.PlaceAt(coverPoint.position, coverPoint.rotation);
        // 엄폐물에 붙어 레이를 보며 쏘기 시작한다
        ai.ranged = true;
        ai.holdPosition = true;
        health.Invulnerable = false;
        phase2Started = Time.time;
        toCover = null;
        if (!health.IsDead) ai.Activate();
    }

    void CancelTransition()
    {
        if (toCover == null) return;
        StopCoroutine(toCover);
        toCover = null;
        health.Invulnerable = false;
        if (agent) agent.speed = baseSpeed;
    }
}
