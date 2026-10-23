using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 몰래 제압(Q): 눈치채지 못한 적의 등 뒤(reach 안, 뒤쪽 ±backAngle°)에 붙으면 "Q 몰래 제압" 안내가 뜨고,
/// 누르면 등 뒤로 바짝 붙어 팔로 목을 졸라(두 사람 동작 ChokeHold·ChokeVictim, Blender에서 제작) 2초 남짓 만에 기절시킨다. 그동안 카메라가 옆에서 가까이 비춘다.
/// 제압당한 적은 소리치지 않는다(근처 순찰이 몰려오지 않는다).
///  · 손전등 순찰: 발각 전(의심 막대가 다 차기 전)
///  · 그 밖의 적: 아직 싸움을 시작하지 않았을 때(구역에 들어오기 전 등)
///  · 옥상 저격수처럼 한자리를 지키며 조준경을 들여다보는 적: 언제든 등 뒤에서
/// 되감으면 쓰러진 적도 함께 되살아난다(적 상태는 TimeRewind가 되돌린다).
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class Takedown : MonoBehaviour
{
    [Tooltip("등 뒤 이 거리(m) 안이면 제압할 수 있다")]
    public float reach = 1.7f;
    [Tooltip("적의 등 쪽 정면에서 이 각도(°) 안이어야 '등 뒤'다")]
    public float backAngle = 65f;

    /// <summary>지금 Q를 누르면 제압할 적(HUD 안내용). 없으면 null</summary>
    public static EnemyAI Target { get; private set; }

    PlayerController player;
    InputAction action;
    bool busy;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() => Target = null;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        action = InputSystem.actions.FindAction("Player/Takedown", false);
    }

    void OnDisable()
    {
        Target = null;
        busy = false; // 꺼지면 코루틴도 멈춘다 → 다시 켜졌을 때 제압이 영영 막히지 않게
        Choking = false; Victim = null; // 남아 있으면 그 적을 보는 순찰마다 바로 소리쳤다
    }

    void Update()
    {
        // 시간이 멈추거나 느린 동안(인벤토리·가게·되감기 직후·타격 멈춤)에는 하지 않는다
        bool can = !busy && player.CanAct && !StoryState.InputLocked && !StoryState.CombatLocked && !TimeRewind.PlayingBack && Time.timeScale > 0.99f;
        Target = can ? Find() : null;
        if (!Target) return;
        Tutorial.Show("takedown");
        if (action != null && action.WasPressedThisFrame()) StartCoroutine(Perform(Target));
    }

    EnemyAI Find()
    {
        EnemyAI best = null;
        float bestDistance = reach;
        foreach (var e in EnemyAI.All)
        {
            if (!e.isActiveAndEnabled || e.Health.IsDead) continue;
            Vector3 to = transform.position - e.transform.position;
            if (Mathf.Abs(to.y) > 0.8f) continue;
            to.y = 0f;
            float d = to.magnitude;
            if (d > bestDistance || d < 0.15f) continue;
            if (Vector3.Angle(-e.transform.forward, to) > backAngle) continue; // 등 뒤가 아니다
            if (!Unaware(e)) continue;
            // 벽 너머의 적은 안 된다: 레이↔적, 적↔붙을 자리 사이가 트여 있어야 한다(벽을 뚫고 순간 이동하지 않게)
            Vector3 chest = e.transform.position + Vector3.up * 1.2f;
            if (!Clear(chest, transform.position + Vector3.up * 1.2f, e) || !Clear(chest, BehindPoint(e) + Vector3.up * 1.2f, e)) continue;
            best = e;
            bestDistance = d;
        }
        return best;
    }

    /// <summary>제압할 때 레이가 서는 자리: 적 등 뒤 0.18m(Blender에서 맞물려 만든 동작의 간격 0.10 × 모델 배율 1.8)</summary>
    static Vector3 BehindPoint(EnemyAI e) => e.transform.position - e.transform.forward * 0.18f + Vector3.up * 0.02f;

    [Tooltip("목 조르기 동작(Blender에서 만든 두 사람 클립, 2.47초) 중 적이 축 늘어져 쓰러지는 때(초)")]
    public float limpAt = 2.1f;
    [Tooltip("동작 전체 길이(초): 레이가 팔을 풀고 일어설 때까지")]
    public float chokeLength = 2.45f;
    const string RayState = "ChokeHold", VictimState = "ChokeVictim", Locomotion = "Idle Walk Run Blend";

    bool Clear(Vector3 from, Vector3 to, EnemyAI e)
    {
        Vector3 d = to - from;
        float length = d.magnitude;
        if (length < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(from, d / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var t = hits[i].collider.transform;
            if (t.IsChildOf(transform) || t.IsChildOf(e.transform) || hits[i].rigidbody) continue; // 레이·대상·쓰러진 몸
            if (hits[i].collider.GetComponentInParent<Health>()) continue; // 옆에 선 다른 사람 몸은 벽이 아니다
            return false;
        }
        return true;
    }

    static bool Unaware(EnemyAI e)
    {
        // 보스는 목을 조를 수 없다(도망칠 때 잠시 꺼져 있어도 — 때려서 막아야 한다)
        if (e.GetComponent<BossCleaner>()) return false;
        // 2장 증거물 담당: 휴게실에서 재워 버리면 경보 때 번호를 누를 사람이 없다(보관실 안에 눌러앉은 뒤에는 된다)
        if (e.TryGetComponent(out EvidenceClerk clerk) && clerk.Current != EvidenceClerk.State.Inside) return false;
        var sight = e.GetComponent<PatrolSight>();
        if (sight) return !sight.Alerted && sight.Suspicion < 0.95f;
        return !e.IsActive || e.holdPosition;
    }

    /// <summary>목 조르기 중(시험용)</summary>
    public static bool Choking { get; private set; }
    /// <summary>지금 목이 졸리는 적(근처 순찰이 목격하는지 본다). 없으면 null</summary>
    public static EnemyAI Victim { get; private set; }

    IEnumerator Perform(EnemyAI e)
    {
        busy = true;
        Target = null;
        Choking = true;
        Victim = e;
        int generation = TimeRewind.Generation;
        var sight = e.GetComponent<PatrolSight>();
        if (sight) { sight.Silence(); sight.Holding = true; } // 맞아도 소리치지 않고, 순찰 걸음도 멈춘다
        e.Deactivate();
        // 바짝 붙는 동안 두 사람의 몸(충돌체)이 서로 밀어내지 않게 한다 — 안 그러면 레이가 뒤로 밀려나 팔이 목에 닿지 않았다
        var mineCol = GetComponent<CharacterController>();
        var theirCols = e.GetComponentsInChildren<Collider>();
        foreach (var c in theirCols) if (c && mineCol) Physics.IgnoreCollision(mineCol, c, true);
        // 등 뒤에 바짝 붙어 적과 같은 쪽을 본다(두 사람 동작이 이 간격·방향에 맞춰 만들어져 있다)
        Vector3 facing = e.transform.forward; facing.y = 0f;
        player.Teleport(BehindPoint(e), Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? facing : transform.forward));
        player.BeginBusy(chokeLength, null);

        // 두 사람 동작을 같이 튼다. 적이 손전등을 겨누던 손 자세(IK)는 끈다
        Animator mine = player.animator, theirs = e.body;
        var theirIk = theirs ? theirs.GetComponent<HumanoidIK>() : null;
        if (theirIk) { theirIk.aim = false; theirIk.look = false; }
        if (mine) mine.CrossFadeInFixedTime(RayState, 0.12f);
        if (theirs) theirs.CrossFadeInFixedTime(VictimState, 0.12f);
        // 손가락: 레이는 팔을 꽉 움켜쥐고, 적은 감긴 팔뚝을 붙잡는다(편 손이 보이지 않게)
        var myFingers = mine ? mine.GetComponentInChildren<FingerPose>() : null;
        var theirFingers = theirs ? theirs.GetComponentInChildren<FingerPose>() : null;
        bool myFist = myFingers && myFingers.alwaysFist, theirFist = theirFingers && theirFingers.alwaysFist;
        if (myFingers) myFingers.alwaysFist = true;
        if (theirFingers) theirFingers.alwaysFist = true;
        HitFeel.Swing(HitFeel.Tier.Medium, transform.position + Vector3.up * 1.4f);
        var shot = CloseUp(e);

        bool interrupted = false;
        for (float t = 0f; t < limpAt; t += Time.deltaTime)
        {
            if (generation != TimeRewind.Generation || !player.IsBusy || player.IsDead || e.Health.IsDead) { interrupted = true; break; }
            yield return null;
        }

        bool rewound = generation != TimeRewind.Generation; // 되감기·다시 시작: 적 상태는 이미 되돌아갔다
        if (interrupted && (rewound || !player.IsBusy || player.IsDead))
        {
            // 다른 적에게 맞아 끊겼다 → 졸리던 적은 풀려나 알아챈다
            EndShot(shot);
            if (myFingers) myFingers.alwaysFist = myFist;
            if (theirFingers) theirFingers.alwaysFist = theirFist;
            foreach (var c in theirCols) if (c && mineCol) Physics.IgnoreCollision(mineCol, c, false);
            if (theirs && !e.Health.IsDead) theirs.CrossFadeInFixedTime(Locomotion, 0.15f);
            if (mine && !player.IsDead) mine.CrossFadeInFixedTime(Locomotion, 0.15f);
            if (!rewound && !e.Health.IsDead) { if (sight) { sight.Holding = false; sight.Alert(false); } else e.Activate(); }
            busy = false;
            Choking = false;
            Victim = null;
            yield break;
        }

        // 축 늘어진다: 적은 앞으로 무너지고(물리), 레이는 동작 끝까지 팔을 풀며 일어선다
        if (!e.Health.IsDead) e.Health.TakeDamage(9999, e.transform.position - transform.forward * 0.2f);
        for (float t = limpAt; t < chokeLength && player.IsBusy; t += Time.deltaTime) yield return null;
        if (mine && !player.IsDead) mine.CrossFadeInFixedTime(Locomotion, 0.25f);
        if (myFingers) myFingers.alwaysFist = myFist;
        EndShot(shot);
        foreach (var c in theirCols) if (c && mineCol) Physics.IgnoreCollision(mineCol, c, false);
        busy = false;
        Choking = false;
        Victim = null;
    }

    static void EndShot(GameObject shot)
    {
        if (shot) Destroy(shot);
        if (savedBlend.HasValue)
        {
            var brain = FindAnyObjectByType<Unity.Cinemachine.CinemachineBrain>();
            if (brain) brain.DefaultBlend = savedBlend.Value;
            savedBlend = null;
        }
    }

    static Unity.Cinemachine.CinemachineBlendDefinition? savedBlend;

    /// <summary>옆에서 두 사람을 가까이 비추는 카메라(가려지지 않는 쪽을 고른다). 짧게 넘어간다</summary>
    GameObject CloseUp(EnemyAI e)
    {
        var brain = FindAnyObjectByType<Unity.Cinemachine.CinemachineBrain>();
        if (!brain) return null;
        Vector3 neck = e.transform.position + Vector3.up * 1.45f;
        Vector3 f = e.transform.forward, r = e.transform.right;
        Vector3[] spots = { neck + r * 1.5f + f * 0.9f + Vector3.up * 0.1f, neck - r * 1.5f + f * 0.9f + Vector3.up * 0.1f, neck + f * 1.9f + Vector3.up * 0.15f };
        Vector3 at = spots[0];
        foreach (var spot in spots)
        {
            if (!Physics.Linecast(neck, spot, out var hit, ~0, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<Health>()) { at = spot; break; }
        }
        var go = new GameObject("TakedownCloseUp");
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(neck - Vector3.up * 0.15f - at));
        var cam = go.AddComponent<Unity.Cinemachine.CinemachineCamera>();
        var lens = cam.Lens; lens.FieldOfView = 42f; cam.Lens = lens;
        cam.Priority = 100;
        savedBlend ??= brain.DefaultBlend;
        brain.DefaultBlend = new Unity.Cinemachine.CinemachineBlendDefinition(Unity.Cinemachine.CinemachineBlendDefinition.Styles.EaseInOut, 0.3f);
        return go;
    }
}
