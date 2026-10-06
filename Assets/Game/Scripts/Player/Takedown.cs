using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 몰래 제압(Q): 눈치채지 못한 적의 등 뒤(reach 안, 뒤쪽 ±backAngle°)에 붙으면 "Q 몰래 제압" 안내가 뜨고,
/// 누르면 등 뒤로 바짝 붙어 뒤통수를 강하게 쳐 한 번에 쓰러뜨린다. 제압당한 적은 소리치지 않는다(근처 순찰이 몰려오지 않는다).
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

    /// <summary>제압할 때 레이가 서는 자리: 적 등 뒤 0.7m(적이 선 높이)</summary>
    static Vector3 BehindPoint(EnemyAI e) => e.transform.position - e.transform.forward * 0.7f + Vector3.up * 0.02f;

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
        var sight = e.GetComponent<PatrolSight>();
        if (sight) return !sight.Alerted && sight.Suspicion < 0.95f;
        return !e.IsActive || e.holdPosition;
    }

    IEnumerator Perform(EnemyAI e)
    {
        busy = true;
        Target = null;
        int generation = TimeRewind.Generation;
        // 등 뒤 0.7m에 붙어 적을 본다
        Vector3 back = BehindPoint(e);
        Vector3 look = e.transform.position - back;
        look.y = 0f;
        player.Teleport(back, Quaternion.LookRotation(look.sqrMagnitude > 0.001f ? look : transform.forward));
        var sight = e.GetComponent<PatrolSight>();
        if (sight) sight.Silence(); // 맞아도 소리치지 않는다
        e.Deactivate();
        player.BeginBusy(0.8f, "Cross");
        HitFeel.Swing(HitFeel.Tier.Heavy, transform.position + Vector3.up * 1.3f);
        yield return new WaitForSeconds(0.28f); // 크로스 주먹이 가장 멀리 뻗는 순간
        bool rewound = generation != TimeRewind.Generation; // 되감기·다시 시작: 적 상태는 이미 되돌아갔다
        if (rewound || !player.IsBusy || player.IsDead)
        {
            // 다른 적에게 맞아 끊겼다 → 제압당하던 적도 알아챈다
            if (!rewound && !e.Health.IsDead) { if (sight) sight.Alert(false); else e.Activate(); }
            busy = false;
            yield break;
        }
        if (!e.Health.IsDead)
        {
            Vector3 head = e.transform.position + Vector3.up * 1.55f;
            HitFeel.Impact(HitFeel.Tier.Heavy, head, e.transform.position - transform.position, e.gameObject, false);
            e.Health.TakeDamage(9999, transform.position); // 뒤에서 맞아 앞으로 쓰러진다
        }
        yield return new WaitForSeconds(0.5f);
        busy = false;
    }
}
