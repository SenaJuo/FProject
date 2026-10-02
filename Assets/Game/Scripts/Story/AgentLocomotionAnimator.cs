using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshAgent로 움직이는 휴머노이드(조직원 등)의 실제 이동 속도를 Starter Assets 이동 애니메이터에 넘긴다.
/// 모델(Animator)이 자식에 있고, 이 컴포넌트는 에이전트가 있는 몸통에 붙인다.
/// </summary>
public class AgentLocomotionAnimator : MonoBehaviour
{
    public Animator animator;
    [Tooltip("달리기 클립의 발이 땅과 맞는 속도(플레이어와 같은 값)")]
    public float runClipSpeed = 6f;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
    static readonly int GroundedId = Animator.StringToHash("Grounded");

    NavMeshAgent agent;
    Vector3 lastPosition;
    float smoothed;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        if (!animator) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return; // 되감기 역재생(시간 정지) 중에는 다리 동작도 멈춘다

        // 에이전트 속도 대신 실제 위치 변화로 잰다: 순간이동·경직·밀려남까지 그대로 반영된다
        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        float speed = new Vector2(delta.x, delta.z).magnitude / dt;
        if (speed > 30f) speed = 0f; // 순간이동한 프레임

        smoothed = Mathf.Lerp(smoothed, Mathf.Min(speed, runClipSpeed), 1f - Mathf.Exp(-dt / 0.08f));
        animator.SetFloat(SpeedId, smoothed);
        animator.SetFloat(MotionSpeedId, speed > runClipSpeed ? speed / runClipSpeed : 1f);
        animator.SetBool(GroundedId, agent == null || !agent.enabled || agent.isOnNavMesh);
    }
}
