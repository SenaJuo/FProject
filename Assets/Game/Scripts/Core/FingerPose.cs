using UnityEngine;

/// <summary>
/// 손가락 쥐기. 바로크 캐릭터 뼈대에는 손가락 뼈가 없어서(FingerRig가 손가락 뼈를 만들어 붙인다)
/// 애니메이션이 손가락을 움직이지 못한다. 그래서 애니메이션이 끝난 뒤(LateUpdate) 손가락 마디를 직접 접는다.
///  · 주인공: 공격 중이거나 공격한 지 얼마 안 됐을 때 꽉 쥔 주먹, 그 밖에는 살짝 오므린 손
///  · 적: 싸우는 중이면 주먹, 아니면 오므린 손
/// </summary>
[DefaultExecutionOrder(500)]
public class FingerPose : MonoBehaviour
{
    [System.Serializable]
    public class Joint
    {
        public Transform bone;
        public Quaternion rest;      // 쉼 자세의 로컬 회전
        public Vector3 axis;         // 손 바닥 쪽으로 접히는 축(뼈 로컬)
        public float relaxed;        // 오므린 손일 때 각도
        public float fist;           // 꽉 쥔 주먹일 때 각도
        public Vector3 axis2;        // 엄지만: 손가락 쪽으로 덮는 두 번째 축
        public float relaxed2, fist2;
    }

    public Joint[] joints;
    [Tooltip("공격이 끝난 뒤에도 주먹을 유지하는 시간(초) — 연타 사이에 손이 펴지지 않게")]
    public float holdFist = 1.2f;
    [Tooltip("늘 주먹을 쥔다(방패 손잡이를 쥔 경찰 등)")]
    public bool alwaysFist;

    PlayerController player;
    EnemyAI enemy;
    float grip, fistUntil;

    void Awake()
    {
        player = GetComponentInParent<PlayerController>();
        enemy = GetComponentInParent<EnemyAI>();
        grip = 0f;
    }

    float appliedGrip = -1f;

    void LateUpdate()
    {
        if (joints == null) return;
        bool fighting = player ? player.IsAttacking : enemy && enemy.IsActive && !enemy.Health.IsDead;
        if (fighting || alwaysFist) fistUntil = Time.time + holdFist;
        float target = Time.time < fistUntil ? 1f : 0f;
        grip = Mathf.MoveTowards(grip, target, Time.unscaledDeltaTime * 8f);
        // 손가락 뼈는 애니메이터가 건드리지 않는다(사람형 아바타 밖): 쥔 정도가 그대로면 지난번 회전이 남아 있으니 다시 넣지 않는다
        if (grip == appliedGrip) return;
        appliedGrip = grip;
        foreach (var j in joints)
        {
            if (!j.bone) continue;
            var r = j.rest * Quaternion.AngleAxis(Mathf.Lerp(j.relaxed, j.fist, grip), j.axis);
            if (j.axis2 != Vector3.zero) r *= Quaternion.AngleAxis(Mathf.Lerp(j.relaxed2, j.fist2, grip), j.axis2);
            j.bone.localRotation = r;
        }
    }
}
