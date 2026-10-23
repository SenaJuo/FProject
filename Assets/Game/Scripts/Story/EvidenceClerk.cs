using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 2장 경찰서 2층의 증거물 담당 경찰(정해진 대로 움직이는 사람).
/// 평소에는 휴게실에서 나오지 않는다. 2층에 침입자 경보가 울리면(Rush) 보관실로 뛰어와 비밀번호를 누르고 들어가 문을 잠근다
/// (2026-10-23: 숨어서 보기만 하면 되던 번호를 "들켜야만 볼 수 있는" 번호로 — 보고 나서 되감아야 쓸 수 있다).
/// 되감으면 위치·진행·부름이 함께 되감긴다 — 하지만 레이는 본 비밀번호를 기억한다(감독이 따로 기억한다).
/// 레이가 먼저 들어가 안에서 잠갔으면, 번호를 눌러도 안 열려 투덜대며 돌아간다.
/// 걷는 동안 바로 앞의 레이를 보면 알린다(SawRay).
/// </summary>
public class EvidenceClerk : MonoBehaviour, IRewindable
{
    public enum State { Waiting, ToKeypad, Keying, Inside, Leaving, Gone }

    public Transform lounge, keypadSpot, insideSpot;
    [Tooltip("부른 뒤 휴게실에서 나서기까지(초)")]
    public float departDelay = 4f;
    public float keyingTime = 2.4f;
    public float sightDistance = 7f;

    /// <summary>경보로 불려 나왔다(되감으면 경보 전으로 함께 돌아간다)</summary>
    [NonSerialized] public bool Called;
    /// <summary>레이가 먼저 들어가 안에서 잠갔다(되감지 않는다)</summary>
    [NonSerialized] public bool LockedFromInside;

    public State Current { get; private set; }
    public event Action Keying, Entered, Refused, SawRay;

    NavMeshAgent agent;
    EnemyAI ai;
    Transform player;
    float t, keyT;
    bool keyedThisRun;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ai = GetComponent<EnemyAI>();
        TimeRewind.Register(this);
        GameManager.PlayerRespawned += ResetClerk;
    }

    void OnDestroy()
    {
        TimeRewind.Unregister(this);
        GameManager.PlayerRespawned -= ResetClerk;
    }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc) player = pc.transform;
        if (ai) ai.Deactivate(); // 싸우지 않는다: 길만 걷는다
    }

    void Update()
    {
        if (!Called || TimeRewind.PlayingBack || StoryState.InputLocked && Current == State.Waiting) return;
        if (agent && !agent.enabled) agent.enabled = true;
        float dt = Time.deltaTime;
        t += dt;
        switch (Current)
        {
            case State.Waiting:
                if (t >= departDelay) { Current = State.ToKeypad; Go(keypadSpot); }
                break;
            case State.ToKeypad:
                LookForRay();
                if (Arrived()) { Current = State.Keying; keyT = 0f; keyedThisRun = false; Face(keypadSpot.forward); }
                break;
            case State.Keying:
                keyT += dt;
                if (!keyedThisRun && keyT > 0.4f) { keyedThisRun = true; Keying?.Invoke(); }
                if (keyT >= keyingTime)
                {
                    if (LockedFromInside) { Current = State.Leaving; Go(lounge); Refused?.Invoke(); }
                    else { Current = State.Inside; Entered?.Invoke(); Warp(insideSpot); }
                }
                break;
            case State.Leaving:
                LookForRay();
                if (Arrived()) { Current = State.Gone; SetVisible(false); }
                break;
        }
    }

    /// <summary>경보: 보관실 문 앞으로 뛰어와(장면이 바뀌는 사이) 곧바로 번호를 누른다</summary>
    public void Rush()
    {
        Called = true;
        Warp(keypadSpot);
        Face(keypadSpot.forward);
        SetVisible(true);
        Current = State.Keying;
        keyT = 0f;
        keyedThisRun = false;
    }

    void LookForRay()
    {
        if (!player || SawRay == null) return;
        Vector3 to = player.position - transform.position; to.y = 0f;
        if (to.magnitude > sightDistance || Vector3.Angle(transform.forward, to) > 40f) return;
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        if (Physics.Linecast(eye, player.position + Vector3.up * 1.2f, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(player)) return;
        SawRay?.Invoke();
    }

    void Go(Transform at)
    {
        if (!at || !agent || !agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.speed = 1.7f;
        agent.stoppingDistance = 0.15f;
        agent.SetDestination(at.position);
    }

    // 문 앞 장애물(닫힌 문짝) 때문에 몇십 cm 앞에서 멈출 수 있다: 가까이 와서 멈췄으면 도착
    bool Arrived() => agent && agent.isOnNavMesh && !agent.pathPending
                      && (agent.remainingDistance <= 0.6f || (agent.remainingDistance <= 1.2f && agent.velocity.sqrMagnitude < 0.01f));

    void Face(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    void Warp(Transform at)
    {
        if (!at) return;
        if (agent && agent.isOnNavMesh) { agent.Warp(at.position); agent.isStopped = true; }
        else transform.position = at.position;
        transform.rotation = at.rotation;
    }

    void SetVisible(bool on)
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = on;
    }

    void ResetClerk()
    {
        Called = false; // 경보는 없던 일: 다시 휴게실에 눌러앉는다
        LockedFromInside = false;
        Current = State.Waiting;
        t = keyT = 0f;
        Warp(lounge);
        SetVisible(true);
        enabled = true;
        var ai = GetComponent<EnemyAI>();
        if (ai && !ai.Health.IsDead) { ai.Deactivate(); ai.damage = 0; } // 보관실에서 싸우다 쓰러졌으면 다시 얌전한 담당으로
    }

    struct Snapshot { public State state; public float t, keyT; public Vector3 pos; public Quaternion rot; public bool called; }

    public object CaptureState() => new Snapshot { state = Current, t = t, keyT = keyT, pos = transform.position, rot = transform.rotation, called = Called };

    public void RestoreState(object state, bool preview)
    {
        var s = (Snapshot)state;
        if (agent && agent.isOnNavMesh) agent.Warp(s.pos); else transform.position = s.pos;
        transform.rotation = s.rot;
        if (preview) return;
        Current = s.state;
        Called = s.called;
        t = s.t;
        keyT = s.keyT;
        keyedThisRun = keyT > 0.4f;
        SetVisible(Current != State.Gone);
        if (Current == State.ToKeypad) Go(keypadSpot);
        else if (Current == State.Leaving) Go(lounge);
        else if (agent && agent.isOnNavMesh) agent.isStopped = true;
    }
}
