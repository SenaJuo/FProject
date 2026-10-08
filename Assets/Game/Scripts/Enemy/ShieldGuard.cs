using System;
using UnityEngine;

/// <summary>
/// 방패 경찰. 정면(±guardAngle°)에서 오는 가벼운 공격은 방패로 막는다 — 금속음과 함께 피해가 들어가지 않는다.
/// 무거운 한 방(연속 공격 세 번째 돌려차기처럼 피해 breakDamage 이상)은 방패를 쳐내 guardBreak초 동안 방패를 내린다.
/// 옆·뒤에서 치거나 방패를 내린 동안에는 그대로 맞는다. 쓰러지면 방패는 손에서 떨어진다(되감으면 다시 쥔다).
///
/// 쥐는 모양: 왼손은 IK로 몸 왼쪽 앞 '방어 자세' 자리(가슴 아래)에 두고 방패 뒤 손잡이를 쥔다. 방패는 매 프레임
/// 그 손 바로 앞(0.2m)에 곧게 세운다(몸 정면에서 바깥으로 살짝 튼다) → 손과 팔은 늘 방패 뒤에 있어 뚫고 나오지 않는다.
/// 막으면 손이 뒤로 밀리며 방패가 떨리고, 쳐내면 손이 허리 옆으로 내려가며 방패가 바깥으로 젖혀진다.
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public class ShieldGuard : MonoBehaviour, IRewindable
{
    [Tooltip("방패(몸 앞에 붙은 자식). 막을 때 흔들리고, 쳐내면 바깥 아래로 젖혀진다")]
    public Transform shield;
    [Tooltip("왼손 IK(있으면 방패 손잡이를 쥔다)")]
    public HumanoidIK ik;
    public float guardAngle = 70f;
    public int breakDamage = 18;
    public float guardBreak = 2.4f;
    [Tooltip("정면으로 두 번 연달아 막은 뒤 방패로 밀칠 때의 피해(동작을 끊는 게 목적)")]
    public int bashDamage = 4;
    [Tooltip("왼손 손목 회전(방어 자세 기준): 주먹이 앞을 보고 세로로 손잡이를 쥐게")]
    public Vector3 gripEuler = new(0f, -90f, -90f); // 손목 후보 여섯을 가까이 찍어 비교해 고른 값(주먹이 세로로 손잡이를 쥔다)
    [Range(0f, 1f)] public float gripRotationWeight = 1f;

    /// <summary>처음으로 방패에 막혔을 때(설명을 띄운다)</summary>
    public static event Action<ShieldGuard> FirstBlocked;
    static bool anyBlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() => anyBlocked = false;

    public bool Broken => brokenFor > 0f;
    public bool Dropped { get; private set; }

    Health health;
    float brokenFor, shake, lowered;
    Transform elbow, hand, guard, elbowHint;

    // 방어 자세(루트 기준): 왼손은 가슴 앞 왼쪽, 팔꿈치는 바깥 아래. 쳐내면 손이 허리 옆으로 내려간다
    static readonly Vector3 GuardUp = new(-0.2f, 1.14f, 0.36f), GuardDown = new(-0.4f, 0.86f, 0.12f);
    static readonly Vector3 ElbowUp = new(-0.46f, 0.98f, -0.02f), ElbowDown = new(-0.42f, 0.9f, -0.18f);
    const float Standoff = 0.2f;      // 손목에서 방패 가운데 면까지(손가락이 닿지 않는 거리)
    const float GripHeight = 0.52f;   // 방패 바닥에서 손잡이까지(0.9m 방패의 가운데보다 조금 위)
    const float OutwardYaw = -14f;    // 몸 정면에서 왼쪽 바깥으로 트는 각도

    static AudioClip Clang => ProceduralSfx.Recorded("ShieldBlock") ?? HitFeel.Clip("impact_light") ?? ProceduralSfx.PlasticHit;

    void Awake()
    {
        health = GetComponent<Health>();
        health.Blocks = Blocks;
        health.Died += Drop;
        TimeRewind.Register(this);
    }

    void Start()
    {
        var anim = ik ? ik.GetComponent<Animator>() : GetComponentInChildren<Animator>();
        if (anim && anim.isHuman)
        {
            elbow = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        }
        guard = new GameObject("ShieldGuardPose").transform;
        guard.SetParent(transform, false);
        guard.localPosition = GuardUp;
        elbowHint = new GameObject("ShieldElbowHint").transform;
        elbowHint.SetParent(transform, false);
        elbowHint.localPosition = ElbowUp;
        if (ik)
        {
            ik.leftGrip = guard;
            ik.leftElbowHint = elbowHint;
            ik.leftRotationWeight = gripRotationWeight;
            ik.holdLeft = true;
        }
        var fingers = GetComponentInChildren<FingerPose>();
        if (fingers) fingers.alwaysFist = true; // 손잡이를 꽉 쥔 주먹(편 손이 방패 앞으로 삐져나오지 않게)
    }

    void OnDestroy()
    {
        if (health) health.Died -= Drop;
        TimeRewind.Unregister(this);
    }

    bool Blocks(int amount, Vector3 source)
    {
        if (Broken || Dropped || !shield) return false;
        Vector3 to = source - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, to) > guardAngle) return false; // 옆·뒤
        Vector3 at = shield.position + Vector3.up * 0.15f;
        if (amount >= breakDamage)
        {
            // 쳐냈다: 방패가 젖혀지고 이 한 방은 그대로 들어간다
            brokenFor = guardBreak;
            ProceduralSfx.PlayAt(Clang, at, 1f, 0.6f, 0.8f);
            return false;
        }
        shake = 1f;
        // 정면으로 두 번 연달아 막으면 방패로 밀쳐낸다: 연타로는 돌려차기(세 번째)까지 닿지 못한다 → 옆으로 돌거나 완벽 회피로 틈을 만든다
        blockStreak = Time.time - lastBlockAt < 1.4f ? blockStreak + 1 : 1;
        lastBlockAt = Time.time;
        if (blockStreak >= 2) { blockStreak = 0; bashPending = true; }
        ProceduralSfx.PlayAt(Clang, at, 0.85f, 0.6f, UnityEngine.Random.Range(0.95f, 1.08f));
        HitFeel.HitStop(0.045f);
        Spark(at);
        if (!anyBlocked) { anyBlocked = true; FirstBlocked?.Invoke(this); }
        return true;
    }

    int blockStreak;
    float lastBlockAt = -9f;
    bool bashPending;

    /// <summary>완벽 회피에 당해 방패가 내려간다(돌려차기로 쳐낸 것과 같다)</summary>
    public void ForceBreak()
    {
        if (Dropped) return;
        brokenFor = guardBreak;
    }

    /// <summary>방패 밀치기: 레이를 뒤로 밀어 연타를 끊는다(가벼운 피해)</summary>
    void Bash()
    {
        var gm = GameManager.Instance;
        var player = gm && gm.player ? gm.player : FindAnyObjectByType<PlayerController>();
        if (!player || health.IsDead || Broken) return;
        Vector3 to = player.transform.position - transform.position; to.y = 0f;
        if (to.magnitude > 2.6f) return;
        var ai = GetComponent<EnemyAI>();
        if (ai && ai.body) ai.body.CrossFadeInFixedTime("Cross", 0.05f);
        ProceduralSfx.PlayAt(Clang, shield.position, 1f, 0.6f, 0.7f);
        player.GetComponent<Health>().TakeDamage(bashDamage, transform.position);
        player.Shove(transform.position); // 직전에 맞아 피해가 안 들어가도(무적 시간) 밀려나며 연타가 끊긴다
        brokenFor = Mathf.Max(brokenFor, 0.9f); // 밀친 뒤 방패가 잠깐 내려간다: 밀려난 뒤 바로 다시 들어가 칠 틈(연타만으로도 느리지만 이길 수 있게)
    }

    static void Spark(Vector3 at)
    {
        // 금속에 부딪힌 순간의 짧은 불빛
        var light = new GameObject("ShieldSpark").AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.6f);
        light.intensity = 6f;
        light.range = 2.5f;
        light.transform.position = at;
        Destroy(light.gameObject, 0.06f);
    }

    void Update()
    {
        if (Dropped) return;
        if (bashPending) { bashPending = false; Bash(); }
        if (brokenFor > 0f) brokenFor -= Time.deltaTime;
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 6f);
        lowered = Mathf.MoveTowards(lowered, Broken ? 1f : 0f, Time.deltaTime * (Broken ? 9f : 2.5f));
        // 막으면 방어 자세가 뒤로 살짝 밀리며 떨리고, 쳐내면 아래로 내려간다(방패는 팔을 따라 함께 움직인다)
        if (guard)
        {
            guard.localPosition = Vector3.Lerp(GuardUp, GuardDown, lowered) + new Vector3(0f, 0f, -0.08f) * shake
                                  + new Vector3(Mathf.Sin(Time.time * 55f) * 0.015f * shake, 0f, 0f);
            guard.localRotation = Quaternion.Euler(gripEuler);
            if (ik) ik.leftRotationWeight = gripRotationWeight;
            elbowHint.localPosition = Vector3.Lerp(ElbowUp, ElbowDown, lowered);
        }
    }

    void LateUpdate()
    {
        // 애니메이션·IK가 끝난 뒤의 실제 왼손 자리 앞에 방패를 곧게 세운다
        if (Dropped || !shield || !hand) return;
        var rot = transform.rotation * Quaternion.Euler(-4f * lowered, OutwardYaw - 55f * lowered, 18f * lowered + Mathf.Sin(Time.time * 60f) * 3f * shake);
        Vector3 forward = rot * Vector3.forward, up = rot * Vector3.up;
        shield.SetPositionAndRotation(hand.position + forward * Standoff - up * GripHeight, rot);
    }

    void Drop()
    {
        if (!shield || Dropped) return;
        Dropped = true;
        if (ik) ik.holdLeft = false;
        shield.SetParent(null, true);
        var box = shield.GetComponent<BoxCollider>();
        if (!box)
        {
            box = shield.gameObject.AddComponent<BoxCollider>();
            FitBox(box);
        }
        box.enabled = true;
        var rb = shield.GetComponent<Rigidbody>();
        if (!rb) rb = shield.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.mass = 4f;
        rb.AddForce(transform.forward * 1.5f + Vector3.up, ForceMode.VelocityChange);
    }

    /// <summary>방패 뿌리에는 그림이 없어(자식 Model) 상자가 기본 1m가 된다 → 자식 메시 크기에 맞춘다(떨어진 방패가 허공에 뜨지 않게)</summary>
    static void FitBox(BoxCollider box)
    {
        var root = box.transform;
        bool any = false;
        var b = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        if (!any) return;
        box.center = b.center;
        box.size = Vector3.Max(b.size, Vector3.one * 0.03f);
    }

    void Reattach()
    {
        if (!shield) return;
        var rb = shield.GetComponent<Rigidbody>();
        if (rb) Destroy(rb);
        var box = shield.GetComponent<BoxCollider>();
        if (box) box.enabled = false;
        shield.SetParent(transform, true);
        Dropped = false;
        if (ik) ik.holdLeft = true;
    }

    struct Snapshot { public float broken; public bool dropped; }

    public object CaptureState() => new Snapshot { broken = brokenFor, dropped = Dropped };

    public void RestoreState(object state, bool preview)
    {
        var s = (Snapshot)state;
        if (!s.dropped && Dropped) Reattach(); // 쓰러지기 전으로 감기면 다시 방패를 쥔다
        if (!preview) brokenFor = s.broken;
    }
}
