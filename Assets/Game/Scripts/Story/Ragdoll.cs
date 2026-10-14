using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 휴머노이드 Animator의 뼈대에 실행 중 물리 몸(강체·충돌체·관절)을 붙여 쓰러뜨린다.
/// 총에 맞은 주인공, 바닥에 쓰러져 있는 친구처럼 애니메이션 클립이 없는 동작을 물리로 만든다.
/// Restore()로 물리 몸을 떼고 애니메이션으로 되돌릴 수 있다(되감기 연출용).
/// </summary>
public class Ragdoll : MonoBehaviour
{
    public bool IsActive { get; private set; }

    // 가라앉기(2026-10-14 "죽으면 목이나 몸이 계속 달랑달랑 움직인다"): 쓰러진 몸이 멈춘 뒤에도 물리가 계속 돌며
    // 관절이 떨리고, 손에 쥔 물건 · 지나가는 레이가 몸을 건드렸다 → 거의 멈추면(또는 늦어도 5초 뒤) 그 자세로 굳히고 충돌도 끈다
    const float SettleMinTime = 1.2f, SettleMaxTime = 5f, SettleSpeed = 0.3f, SettleHold = 0.4f;
    float activatedAt, calmSince = -1f;
    bool settled;

    readonly List<Object> created = new();
    readonly List<Rigidbody> bodies = new();
    Animator animator;

    struct Part
    {
        public HumanBodyBones bone, child, parent;
        public float mass, radiusScale;
        public Part(HumanBodyBones bone, HumanBodyBones child, HumanBodyBones parent, float mass, float radiusScale)
        { this.bone = bone; this.child = child; this.parent = parent; this.mass = mass; this.radiusScale = radiusScale; }
    }

    // 부모가 먼저 오도록 나열한다(관절이 부모 강체를 찾을 수 있게). 엉덩이(Hips)는 부모가 없음을 자기 자신으로 표시
    static readonly Part[] Parts =
    {
        new(HumanBodyBones.Hips,          HumanBodyBones.Spine,         HumanBodyBones.Hips,          12f, 0.9f),
        new(HumanBodyBones.Chest,         HumanBodyBones.Neck,          HumanBodyBones.Hips,          14f, 0.8f),
        new(HumanBodyBones.Head,          HumanBodyBones.Head,          HumanBodyBones.Chest,          5f, 1f),
        new(HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  HumanBodyBones.Chest,          2.5f, 0.22f),
        new(HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,      HumanBodyBones.LeftUpperArm,   1.8f, 0.2f),
        new(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.Chest,          2.5f, 0.22f),
        new(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,     HumanBodyBones.RightUpperArm,  1.8f, 0.2f),
        new(HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  HumanBodyBones.Hips,           7f, 0.2f),
        new(HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      HumanBodyBones.LeftUpperLeg,   4f, 0.17f),
        new(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.Hips,           7f, 0.2f),
        new(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     HumanBodyBones.RightUpperLeg,  4f, 0.17f),
    };

    /// <summary>애니메이션을 끄고 물리로 쓰러뜨린다. impulse는 맞은 부위(hitBone)에 주는 충격(N·s).</summary>
    public void Activate(Vector3 impulse, HumanBodyBones hitBone = HumanBodyBones.Chest)
    {
        if (IsActive) return;
        animator = GetComponent<Animator>();
        if (!animator || !animator.isHuman) return;
        IsActive = true;

        // 현재 애니메이션 자세를 그대로 물려받은 상태에서 물리를 시작한다
        animator.enabled = false;
        var byBone = new Dictionary<HumanBodyBones, Rigidbody>();
        foreach (var part in Parts)
        {
            Transform t = animator.GetBoneTransform(part.bone);
            if (!t && part.bone == HumanBodyBones.Chest) t = animator.GetBoneTransform(HumanBodyBones.Spine); // 가슴 뼈가 없는 아바타
            if (!t || t.GetComponent<Rigidbody>()) continue;
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = part.mass;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 2.5f;      // 목·팔이 흔들거리지 않게(전에는 0.8)
            rb.solverIterations = 12;      // 관절이 덜 떨리게
            rb.solverVelocityIterations = 4;
            rb.sleepThreshold = 0.1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            created.Add(rb);
            bodies.Add(rb);
            byBone[part.bone] = rb;
            created.Add(AddCollider(t, part));

            // 부모 강체: 표에 적힌 뼈가 없으면 위로 올라가며 가장 가까운 강체를 쓴다
            Rigidbody parentBody = null;
            if (part.parent != part.bone && !byBone.TryGetValue(part.parent, out parentBody) && t.parent)
                parentBody = t.parent.GetComponentInParent<Rigidbody>();
            if (parentBody)
            {
                var joint = t.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parentBody;
                joint.enablePreprocessing = false;
                joint.enableProjection = true; // 관절이 벌어지며 튀는 것을 막는다
                bool knee = part.bone is HumanBodyBones.LeftLowerLeg or HumanBodyBones.RightLowerLeg;
                bool elbow = part.bone is HumanBodyBones.LeftLowerArm or HumanBodyBones.RightLowerArm;
                joint.lowTwistLimit = new SoftJointLimit { limit = knee ? -90f : elbow ? -100f : -25f };
                joint.highTwistLimit = new SoftJointLimit { limit = knee || elbow ? 0f : 25f };
                joint.swing1Limit = new SoftJointLimit { limit = knee || elbow ? 5f : part.bone == HumanBodyBones.Head ? 35f : 45f };
                joint.swing2Limit = new SoftJointLimit { limit = knee || elbow ? 5f : 30f };
                created.Add(joint);
            }
        }

        activatedAt = Time.time;
        calmSince = -1f;
        settled = false;
        if (byBone.TryGetValue(hitBone, out var hit)) hit.AddForce(impulse, ForceMode.Impulse);
        else if (bodies.Count > 0) bodies[0].AddForce(impulse, ForceMode.Impulse);
    }

    /// <summary>물리가 멈춘 뒤 고정해 둔다(바닥에 쓰러진 친구처럼 계속 놓여 있을 때 계산을 아낀다).</summary>
    public void Freeze()
    {
        settled = true;
        foreach (var rb in bodies) if (rb) rb.isKinematic = true;
        foreach (var o in created) if (o is Collider c) c.enabled = false; // 굳은 몸은 밟거나 지나가도 흔들리지 않는다
    }

    void Update()
    {
        if (!IsActive || settled) return;
        float age = Time.time - activatedAt;
        if (age < SettleMinTime) return;
        if (MaxSpeed() < SettleSpeed) { if (calmSince < 0f) calmSince = Time.time; }
        else calmSince = -1f;
        if ((calmSince >= 0f && Time.time - calmSince >= SettleHold) || age >= SettleMaxTime) Freeze();
    }

    /// <summary>물리 몸을 모두 떼고 애니메이션을 다시 켠다.</summary>
    public void Restore()
    {
        // 실제 삭제는 프레임 끝에 일어나므로, 그 사이 물리가 애니메이션 자세를 흔들지 않게 먼저 고정한다
        foreach (var rb in bodies) if (rb) rb.isKinematic = true;
        // 관절 → 충돌체 → 강체 순으로 지워야 의존 관계 오류가 나지 않는다
        foreach (var o in created) if (o is Joint) Destroy(o);
        foreach (var o in created) if (o is Collider) Destroy(o);
        foreach (var o in created) if (o is Rigidbody) Destroy(o);
        created.Clear();
        bodies.Clear();
        if (animator) animator.enabled = true;
        IsActive = false;
        settled = false;
    }

    public float MaxSpeed()
    {
        float v = 0f;
        foreach (var rb in bodies) if (rb && !rb.isKinematic) v = Mathf.Max(v, rb.linearVelocity.magnitude);
        return v;
    }

    Collider AddCollider(Transform t, Part part)
    {
        float scale = Mathf.Max(0.0001f, t.lossyScale.x);
        if (part.bone == HumanBodyBones.Head)
        {
            var sphere = t.gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.11f / scale;
            sphere.center = t.InverseTransformPoint(t.position + transform.up * 0.08f);
            return sphere;
        }

        Transform child = animator.GetBoneTransform(part.child);
        if (part.bone == HumanBodyBones.Hips || part.bone == HumanBodyBones.Chest)
        {
            // 몸통은 상자: 높이는 다음 뼈까지, 폭은 양 어깨/골반 너비를 대충 따른다
            Vector3 top = child ? child.position : t.position + transform.up * 0.25f;
            float h = Mathf.Max(0.12f, Vector3.Distance(t.position, top));
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.center = t.InverseTransformPoint((t.position + top) * 0.5f);
            box.size = new Vector3(0.32f, h, 0.2f) * part.radiusScale / scale;
            return box;
        }

        Vector3 end = child ? child.position : t.position + transform.up * -0.3f;
        Vector3 localEnd = t.InverseTransformPoint(end);
        int axis = Mathf.Abs(localEnd.x) > Mathf.Abs(localEnd.y)
            ? (Mathf.Abs(localEnd.x) > Mathf.Abs(localEnd.z) ? 0 : 2)
            : (Mathf.Abs(localEnd.y) > Mathf.Abs(localEnd.z) ? 1 : 2);
        float length = localEnd.magnitude;
        var capsule = t.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = axis;
        capsule.center = localEnd * 0.5f;
        capsule.radius = length * part.radiusScale;
        capsule.height = length + capsule.radius;
        return capsule;
    }
}
