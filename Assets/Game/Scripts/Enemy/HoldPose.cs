using UnityEngine;

/// <summary>
/// 손에 든 물건을 '쥔 자세'로 든다: 블렌더에서 만든 상체 자세(Hold_Flashlight / Hold_ShieldBaton)를
/// 애니메이터의 팔 전용 레이어(HoldRight·HoldLeft)로 걷기·달리기 위에 덮는다.
///  - 손전등: 순찰 때는 팔을 반쯤 내려 앞을 비춘다. 싸움이 시작되면 바닥에 떨어뜨리고 맨주먹으로 덤빈다(떨어진 손전등은 켜진 채 바닥을 비춘다).
///  - 방패·진압봉: 왼팔뚝을 가슴 앞에 가로질러 방패를 받치고, 오른손은 진압봉을 쥐고 대기한다.
/// 주먹을 내지르거나 맞는 동안에는 그 팔의 쥔 자세를 잠깐 푼다(공격 동작이 보이게).
/// 예전에는 IK로 팔을 6m 앞으로 쭉 뻗게 해(좀비 팔) 손전등이 손에 꽂힌 것처럼 보였고, 방패는 몸 옆에 떠 있었다.
/// </summary>
public class HoldPose : MonoBehaviour, IRewindable
{
    public enum Kind { Flashlight, ShieldBaton }
    public Kind kind;
    public Animator animator;
    public HumanoidIK ik;
    [Tooltip("오른손에 쥔 물건(손전등·진압봉)")]
    public Transform item;
    [Tooltip("싸움이 시작되면 오른손 물건을 떨어뜨린다(손전등)")]
    public bool dropWhenFighting = true;

    const string RightLayer = "HoldRight", LeftLayer = "HoldLeft";

    EnemyAI ai;
    PatrolSight sight;
    ShieldGuard guard;
    int rightLayer = -1, leftLayer = -1;
    float rightWeight, leftWeight;
    Transform itemParent;
    public bool Dropped { get; private set; }

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        sight = GetComponent<PatrolSight>();
        guard = GetComponent<ShieldGuard>();
        if (item) itemParent = item.parent;
        if (kind == Kind.ShieldBaton) dropWhenFighting = false;
        TimeRewind.Register(this);
        GameManager.PlayerRespawned += PickUp; // 저장 지점부터 다시: 떨어뜨렸던 손전등을 다시 든다
    }

    void OnDestroy()
    {
        TimeRewind.Unregister(this);
        GameManager.PlayerRespawned -= PickUp;
    }

    void Start()
    {
        if (!animator) return;
        rightLayer = animator.GetLayerIndex(RightLayer);
        leftLayer = animator.GetLayerIndex(LeftLayer);
        string pose = kind == Kind.Flashlight ? "Flashlight" : "Baton";
        if (rightLayer >= 0) animator.Play(pose, rightLayer, Random.value);
        if (leftLayer >= 0 && kind == Kind.ShieldBaton) animator.Play("Shield", leftLayer, 0f);
        if (ik)
        {
            ik.followForearm = true; // 쥔 물건은 팔뚝 방향을 따른다(손전등 빛이 팔이 가리키는 곳을 비춘다)
            ik.aim = false;
        }
    }

    void Update()
    {
        if (TimeRewind.PlayingBack) return;
        bool dead = ai && ai.Health && ai.Health.IsDead;
        bool fighting = ai && (ai.IsActive || (sight && sight.Alerted));
        if (kind == Kind.Flashlight && dropWhenFighting && fighting && !Dropped && !dead) Drop(true);

        bool busy = ai && ai.InAction;
        bool holdRight = !dead && !Dropped && item && !busy;
        bool holdLeft = !dead && kind == Kind.ShieldBaton && guard && !guard.Dropped && !guard.Broken;
        float k = 1f - Mathf.Exp(-Time.deltaTime / 0.08f);
        rightWeight = Mathf.Lerp(rightWeight, holdRight ? 1f : 0f, k);
        leftWeight = Mathf.Lerp(leftWeight, holdLeft ? 1f : 0f, k);
        if (animator)
        {
            if (rightLayer >= 0) animator.SetLayerWeight(rightLayer, rightWeight);
            if (leftLayer >= 0) animator.SetLayerWeight(leftLayer, leftWeight);
        }
    }

    /// <summary>손전등을 바닥에 떨어뜨린다(켜진 채). toss면 앞으로 툭 던지듯 놓는다</summary>
    void Drop(bool toss)
    {
        if (!item || Dropped) return;
        Dropped = true;
        if (ik && ik.heldItem == item) ik.heldItem = null;
        item.SetParent(null, true);
        var box = item.GetComponent<BoxCollider>();
        if (!box) { box = item.gameObject.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0f, 0.04f); box.size = new Vector3(0.06f, 0.06f, 0.24f); }
        box.enabled = true;
        var rb = item.GetComponent<Rigidbody>();
        if (!rb) rb = item.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.mass = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (toss)
        {
            rb.AddForce(transform.forward * 0.8f + transform.right * 0.4f + Vector3.up * 0.6f, ForceMode.VelocityChange);
            rb.AddTorque(Random.onUnitSphere * 4f, ForceMode.VelocityChange);
        }
    }

    void PickUp()
    {
        if (!item || !Dropped) return;
        var rb = item.GetComponent<Rigidbody>();
        if (rb) Destroy(rb);
        var box = item.GetComponent<BoxCollider>();
        if (box) box.enabled = false;
        item.SetParent(itemParent, true);
        if (ik) ik.heldItem = item;
        Dropped = false;
    }

    struct Snapshot { public bool dropped; public Vector3 pos; public Quaternion rot; }

    public object CaptureState() => new Snapshot { dropped = Dropped, pos = item ? item.position : default, rot = item ? item.rotation : default };

    public void RestoreState(object state, bool preview)
    {
        if (!item) return;
        var s = (Snapshot)state;
        if (!s.dropped) { PickUp(); return; } // 떨어뜨리기 전으로 감기면 다시 손에 든다
        if (!Dropped) Drop(false);
        var rb = item.GetComponent<Rigidbody>();
        if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        item.SetPositionAndRotation(s.pos, s.rot);
    }
}
