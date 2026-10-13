using UnityEngine;

/// <summary>
/// 걷기·달리기 클립 위에 손·고개 자세를 덧입힌다(Animator의 IK Pass가 켜진 컨트롤러가 필요하다).
///  - carry: 두 손을 carryPoint 양옆에 붙여 상자를 안고 있는 자세
///  - aim:   오른손을 aimTarget 쪽으로 뻗어 총을 겨누는 자세(heldItem이 있으면 총구가 정확히 대상을 향하게 돌린다)
///  - look:  고개를 lookTarget 쪽으로 돌린다
/// 가중치는 목표값으로 부드럽게 따라가므로, 켜고 끌 때 팔이 튀지 않는다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class HumanoidIK : MonoBehaviour
{
    [Header("상자 들기")]
    public Transform carryPoint;
    [Tooltip("상자 중심에서 양손까지의 가로 거리(m)")]
    public float carryHalfWidth = 0.17f;
    public bool carry;

    [Header("겨누기")]
    public Transform aimTarget;
    [Tooltip("오른손에 쥔 물건(총). 매 프레임 총구가 대상을 향하도록 돌린다")]
    public Transform heldItem;
    [Tooltip("쥔 물건이 손뼈에서 떨어진 자리(손뼈 축 기준). 손가락 뼈가 없는 모델이라 손바닥 안에 오도록 맞춘다")]
    public Vector3 heldOffset = new(0f, -0.02f, 0.06f);
    public bool aim;
    [Tooltip("겨누지 않을 때 쥔 물건이 팔뚝 방향(팔꿈치→손목)을 따른다. 끄면 몸 정면을 본다")]
    public bool followForearm;
    [Tooltip("팔뚝 방향에서 물건을 위로 세우는 각도(진압봉은 손에서 위로 세워 쥔다. 손전등은 0)")]
    public float heldPitch;

    [Header("왼손으로 쥐기(방패)")]
    [Tooltip("왼손이 붙잡을 자리(방패 손잡이). 위치와 방향을 그대로 따른다")]
    public Transform leftGrip;
    public bool holdLeft;
    [Tooltip("왼손 회전을 leftGrip에 맞추는 정도(0이면 위치만 맞추고 손목은 동작 그대로)")]
    [Range(0f, 1f)] public float leftRotationWeight = 0.8f;
    [Tooltip("왼팔 팔꿈치가 향할 자리(방패를 들 때 팔꿈치를 바깥 아래로)")]
    public Transform leftElbowHint;

    [Header("시선")]
    public Transform lookTarget;
    public bool look;

    [Tooltip("가중치가 목표값을 따라가는 속도(1/초)")]
    public float blendSpeed = 6f;

    Animator animator;
    float carryWeight, aimWeight, lookWeight, leftWeight;
    Transform rightHand, rightLowerArm;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
    }

    void Update()
    {
        // 컷신 슬로모션 중에도 자세 전환은 일정한 속도로 보이게 실제 시간 기준으로 섞는다
        float k = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
        carryWeight = Mathf.Lerp(carryWeight, carry && carryPoint ? 1f : 0f, k);
        aimWeight = Mathf.Lerp(aimWeight, aim && aimTarget ? 1f : 0f, k);
        lookWeight = Mathf.Lerp(lookWeight, look && lookTarget ? 1f : 0f, k);
        leftWeight = Mathf.Lerp(leftWeight, holdLeft && leftGrip ? 1f : 0f, k);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (carryPoint && carryWeight > 0.001f)
        {
            Vector3 side = carryPoint.right * carryHalfWidth;
            // 손바닥이 상자 옆면을 향하도록: 왼손은 오른쪽(+x), 오른손은 왼쪽(-x)을 본다
            SetHand(AvatarIKGoal.LeftHand, carryPoint.position - side, Quaternion.LookRotation(carryPoint.forward, carryPoint.right), carryWeight);
            SetHand(AvatarIKGoal.RightHand, carryPoint.position + side, Quaternion.LookRotation(carryPoint.forward, -carryPoint.right), carryWeight);
        }

        if (leftGrip && leftWeight > 0.001f)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftWeight * leftRotationWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftGrip.position);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftGrip.rotation);
            if (leftElbowHint)
            {
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftWeight);
                animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftElbowHint.position);
            }
        }

        if (aimTarget && aimWeight > 0.001f)
        {
            Vector3 shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            Vector3 dir = (aimTarget.position - shoulder).normalized;
            SetHand(AvatarIKGoal.RightHand, shoulder + dir * 0.58f, Quaternion.LookRotation(dir, Vector3.up), aimWeight);
        }

        float lw = Mathf.Max(lookWeight, aimWeight); // 겨눌 때는 대상도 쳐다본다
        if (lw > 0.001f)
        {
            Transform target = lookTarget ? lookTarget : aimTarget;
            if (target)
            {
                animator.SetLookAtWeight(lw, 0.25f, 0.8f, 1f, 0.6f);
                animator.SetLookAtPosition(target.position);
            }
        }
    }

    void SetHand(AvatarIKGoal goal, Vector3 position, Quaternion rotation, float weight)
    {
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKRotationWeight(goal, weight * 0.8f);
        animator.SetIKPosition(goal, position);
        animator.SetIKRotation(goal, rotation);
    }

    void LateUpdate()
    {
        // 쥔 총은 손에 붙어 다니되, 겨누는 동안에는 총구(+z)가 대상을 정확히 향하게 한다
        if (heldItem && rightHand)
        {
            heldItem.position = rightHand.position + rightHand.rotation * heldOffset;
            if (!animator.enabled && rightLowerArm)
            {
                // 쓰러져 물리로 넘어간 몸(래그돌): 쥔 물건을 팔뚝 방향으로 눕힌다(허공에 수평으로 떠 있지 않게)
                Vector3 arm = rightHand.position - rightLowerArm.position;
                if (arm.sqrMagnitude > 0.0001f) heldItem.rotation = Quaternion.LookRotation(arm, Vector3.up);
                return;
            }
            if (followForearm && rightLowerArm && aimWeight <= 0.5f)
            {
                // 주먹 안에 쥔다: 팔뚝을 그대로 이어 앞으로 나온다(손전등 머리·진압봉이 팔이 가리키는 쪽)
                Vector3 dir = (rightHand.position - rightLowerArm.position).normalized;
                Vector3 side = Vector3.Cross(transform.up, dir);
                Vector3 along = side.sqrMagnitude > 0.0001f ? Quaternion.AngleAxis(-heldPitch, side.normalized) * dir : dir;
                heldItem.SetPositionAndRotation(rightHand.position + dir * heldOffset.z, Quaternion.LookRotation(along, transform.up));
                return;
            }
            Vector3 forward = aimTarget && aimWeight > 0.5f ? aimTarget.position - heldItem.position : transform.forward;
            heldItem.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
