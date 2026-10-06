using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 손전등을 들고 순찰하는 조직원(EnemyAI와 함께 붙인다).
///  · 평소: 경로점을 차례로 걸으며 멈출 때마다 좌우를 비춰 본다. EnemyAI는 꺼져 있다(IsActive = false).
///  · 레이가 손전등 빛(앞쪽 부채꼴) 안, 가리는 것 없이 보이면 머리 위 의심 막대(?)가 찬다. 가까울수록 빨리 찬다.
///    다 차면 소리치며(!) 전투를 시작하고, 근처 순찰도 함께 몰려온다.
///  · 덤불·건물·차 같은 충돌체 뒤에 있으면 보이지 않는다.
///  · 되감으면 경로 위치·의심·발각 여부도 함께 돌아간다. 레이가 쓰러져 저장 지점에서 다시 시작하면 처음 순찰로 돌아간다.
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public class PatrolSight : MonoBehaviour, IRewindable
{
    public static readonly List<PatrolSight> All = new();

    [Tooltip("순서대로 걷는 경로점(끝나면 처음으로). 비우면 제자리에 서서 둘러본다")]
    public Transform[] route = Array.Empty<Transform>();
    public float walkSpeed = 1.5f;
    [Tooltip("경로점마다 멈춰 둘러보는 시간")]
    public float lookAround = 2.2f;

    [Header("시야(손전등)")]
    public float viewDistance = 11f;
    [Tooltip("앞쪽 부채꼴의 반각(도)")]
    public float viewAngle = 36f;
    [Tooltip("이 거리 안이면 방향과 상관없이 알아챈다(바로 뒤에 붙으면 들킨다)")]
    public float nearSense = 1.6f;
    [Tooltip("등 뒤(뒤쪽 반)에서는 이 거리 안이라야 알아챈다 — 몰래 다가가 제압할 틈")]
    public float backSense = 0.6f;
    [Tooltip("소리치면 이 거리 안의 다른 순찰도 함께 몰려온다")]
    public float alertRadius = 16f;
    public string spottedLine = "저기다!";
    public Light flashlight;
    [Tooltip("손전등을 비추는 지점(IK 겨누기 대상). 몸 앞 바닥 쪽")]
    public Transform beamTarget;

    /// <summary>감독이 정한 연출 중: 순찰·감지를 멈추고 그 자리에 둔다</summary>
    public bool Holding;
    /// <summary>연출로 "들켰다"는 표시(!)만 보여 줄 때(실제로 싸움을 시작하지는 않는다)</summary>
    public bool Staged;
    /// <summary>꺼 두면 보지 못한다(통화 중처럼 딴 데 정신이 팔린 상태)</summary>
    public bool CanDetect = true;

    public float Suspicion { get; private set; }
    public bool Alerted { get; private set; }
    /// <summary>소리친 시각(실제 시간). HUD가 잠깐 ! 를 띄운다</summary>
    public float AlertedAt { get; private set; } = -99f;
    public EnemyAI Enemy { get; private set; }

    public static event Action<PatrolSight> Spotted;

    NavMeshAgent agent;
    HumanoidIK ik;
    Transform player, playerHead, playerChest;
    PlayerController playerController;
    static readonly RaycastHit[] rayHits = new RaycastHit[32]; // 매 프레임 배열을 새로 만들지 않는다
    int index;
    float pauseTimer;
    float chaseSpeed, chaseStop;
    float lookPhase;
    bool silenced;

    /// <summary>몰래 제압: 이번에 맞아도 소리치거나 알아채지 않는다</summary>
    public void Silence() => silenced = true;

    void Awake()
    {
        Enemy = GetComponent<EnemyAI>();
        agent = GetComponent<NavMeshAgent>();
        ik = GetComponentInChildren<HumanoidIK>();
        TimeRewind.Register(this);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        chaseSpeed = agent.speed;
        chaseStop = agent.stoppingDistance;
        var gm = GameManager.Instance;
        if (gm && gm.player)
        {
            player = gm.player.transform;
            playerController = gm.player;
            var anim = gm.player.animator;
            playerHead = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : player;
            playerChest = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Chest) : player;
        }
        Enemy.Health.Damaged += (_, _) => { if (!Alerted && !silenced) Alert(false); }; // 맞으면 당연히 알아챈다(몰래 제압은 예외)
        GameManager.PlayerRespawned += ResetPatrol;
        BeginPatrol();
    }

    void OnDestroy()
    {
        GameManager.PlayerRespawned -= ResetPatrol;
        TimeRewind.Unregister(this);
    }

    // Holding·CanDetect는 감독이 정하는 연출 표시라 되감지 않는다(통화가 끝난 뒤 통화 중으로 되감으면 영원히 굳어 있었다)
    struct Snapshot { public int index; public float pause, suspicion; public bool alerted; }

    public object CaptureState() => new Snapshot { index = index, pause = pauseTimer, suspicion = Suspicion, alerted = Alerted };

    public void RestoreState(object state, bool preview)
    {
        if (preview) return; // 역재생 중의 몸 위치는 EnemyAI가 보여 준다
        var s = (Snapshot)state;
        silenced = false;
        index = s.index;
        pauseTimer = s.pause;
        Suspicion = s.suspicion;
        Alerted = s.alerted;
        if (!Alerted) BeginPatrol();
        else if (flashlight) flashlight.enabled = true; // 쓰러진 뒤에서 살아 있던 때로 감기면 다시 켠다(아직 쓰러져 있으면 Update가 끈다)
    }

    void ResetPatrol()
    {
        if (Enemy.Health.IsDead) return;
        Alerted = false;
        Suspicion = 0f;
        index = 0;
        pauseTimer = 0f;
        Enemy.Deactivate();
        BeginPatrol();
    }

    void BeginPatrol()
    {
        if (agent.enabled) agent.speed = walkSpeed;
        SetBeam(true);
    }

    void SetBeam(bool patrolling)
    {
        if (flashlight) flashlight.enabled = !Enemy.Health.IsDead;
        if (ik && beamTarget)
        {
            // 순찰 중에는 손전등을 앞으로 들고 비춘다. 싸울 때는 팔을 내리고 주먹을 쓴다
            ik.aimTarget = beamTarget;
            ik.aim = patrolling;
        }
    }

    void Update()
    {
        if (Enemy.Health.IsDead) { if (flashlight && flashlight.enabled) flashlight.enabled = false; return; }
        if (Alerted || Enemy.IsActive)
        {
            if (!Alerted) Alert(false); // 전투 구역 등 다른 이유로 깨어났다
            return;
        }
        if (!agent.enabled || !agent.isOnNavMesh || TimeRewind.PlayingBack) return;
        if (Holding) { if (!agent.isStopped) { agent.isStopped = true; agent.ResetPath(); } return; }

        float distance = 0f;
        bool seen = CanDetect && SeesPlayer(out distance);
        if (seen)
        {
            // 가까울수록 빨리 알아챈다: 1.6m 0.45초, 11m 1.6초
            float fill = Mathf.Lerp(2.2f, 0.62f, Mathf.InverseLerp(nearSense, viewDistance, distance));
            Suspicion = Mathf.Min(1f, Suspicion + fill * Time.deltaTime);
            // 의심이 들면 걸음을 멈추고 그쪽을 본다
            agent.isStopped = true;
            Vector3 to = player.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 220f * Time.deltaTime);
            if (Suspicion >= 1f) { Alert(true); return; }
        }
        else
        {
            Suspicion = Mathf.Max(0f, Suspicion - 0.3f * Time.deltaTime);
            if (Suspicion <= 0.25f) Walk();
        }
        if (beamTarget) Sweep();
    }

    void Walk()
    {
        agent.speed = walkSpeed; // EnemyAI가 에이전트를 켜는 순서와 상관없이 순찰은 걷는다
        // 싸울 때는 상대 0.9m 앞에서 멈추지만, 순찰은 경로점까지 바짝 걸어가야 "도착"으로 친다(0.9m 앞에서 영원히 서 있던 버그)
        agent.stoppingDistance = 0.1f;
        agent.isStopped = false;
        if (route.Length == 0) { LookAround(); return; }
        var target = route[index % route.Length];
        if (!agent.hasPath || (agent.destination - target.position).sqrMagnitude > 0.25f) agent.SetDestination(target.position);
        if (!agent.pathPending && agent.remainingDistance <= 0.35f)
        {
            pauseTimer += Time.deltaTime;
            agent.isStopped = true;
            LookAround();
            if (pauseTimer >= lookAround)
            {
                pauseTimer = 0f;
                index = (index + 1) % route.Length;
            }
        }
    }

    void LookAround()
    {
        // 멈춰 서서 좌우로 천천히 둘러본다
        lookPhase += Time.deltaTime;
        transform.Rotate(0f, Mathf.Sin(lookPhase * 1.4f) * 55f * Time.deltaTime, 0f);
    }

    void Sweep()
    {
        // 손전등 끝을 걸음에 맞춰 살짝 흔든다(몸 앞 6m, 바닥 쪽)
        float wobble = Mathf.Sin(Time.time * 1.7f + GetInstanceID()) * 0.6f;
        beamTarget.position = transform.position + transform.forward * 6f + transform.right * wobble + Vector3.up * 0.3f;
    }

    bool SeesPlayer(out float distance)
    {
        distance = float.MaxValue;
        if (!player || !playerHead) return false;
        if (playerController && playerController.IsDead) return false;
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 flat = player.position - transform.position; flat.y = 0f;
        distance = flat.magnitude;
        if (distance > viewDistance) return false;
        float angle = Vector3.Angle(transform.forward, flat);
        float near = angle > 100f ? backSense : nearSense; // 등 뒤에서는 바짝 붙어야 기척을 느낀다
        if (distance > near && angle > viewAngle) return false;
        // 머리나 가슴 둘 중 하나라도 가리는 것 없이 보이면 보인 것이다
        return Clear(eye, playerHead.position) || Clear(eye, playerChest.position);
    }

    bool Clear(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float length = d.magnitude;
        int n = Physics.RaycastNonAlloc(from, d / length, rayHits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = rayHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.IsChildOf(player)) continue;
            if (hit.rigidbody) continue; // 쓰러진 몸(래그돌)은 가리지 않는다
            if (hit.collider.GetComponentInParent<EnemyAI>()) continue; // 다른 조직원 몸은 가림으로 치지 않는다
            return false;
        }
        return true;
    }

    /// <summary>발각: 소리치고 전투를 시작한다. shout이면 대사와 함께 근처 순찰도 깨운다</summary>
    public void Alert(bool shout)
    {
        if (Alerted) return;
        Alerted = true;
        Holding = false;
        Suspicion = 1f;
        AlertedAt = Time.unscaledTime;
        if (agent.enabled) { agent.speed = chaseSpeed; agent.stoppingDistance = chaseStop; agent.isStopped = false; }
        SetBeam(false);
        Enemy.Activate();
        if (shout)
        {
            StoryState.Say("조직원", spottedLine, 1.4f);
            foreach (var other in All)
                if (other != this && !other.Alerted && !other.Enemy.Health.IsDead && Vector3.Distance(other.transform.position, transform.position) <= alertRadius)
                    other.Alert(false);
        }
        Spotted?.Invoke(this);
    }

    /// <summary>연출용: 의심 막대를 바로 채워 보이게 한다(실제 발각은 Alert)</summary>
    public void ForceSuspicion(float value) => Suspicion = Mathf.Clamp01(value);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.6f);
        Vector3 o = transform.position + Vector3.up * 1.6f;
        Gizmos.DrawLine(o, o + Quaternion.Euler(0f, viewAngle, 0f) * transform.forward * viewDistance);
        Gizmos.DrawLine(o, o + Quaternion.Euler(0f, -viewAngle, 0f) * transform.forward * viewDistance);
    }
}
