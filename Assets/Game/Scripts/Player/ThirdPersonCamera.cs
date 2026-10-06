using UnityEngine;
using UnityEngine.InputSystem;

// 시네머신 브레인(실행 순서 0)보다 먼저 움직여야, 브레인이 같은 프레임의 위치를 읽어 한 프레임 늦는 떨림이 없다
[DefaultExecutionOrder(-100)]
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 targetOffset = new(0f, 1.6f, 0f);
    public float distance = 5.5f;
    [Tooltip("마우스 1픽셀당 회전 각도")]
    public float mouseSensitivity = 0.12f;
    [Tooltip("스틱을 끝까지 밀었을 때 초당 회전 각도")]
    public float stickSensitivity = 180f;
    public float minPitch = -30f;
    public float maxPitch = 70f;
    public float collisionRadius = 0.25f;

    // ---------- 타격 흔들림·당김(HitFeel) ----------
    static ThirdPersonCamera active;
    Vector3 shakeDir; float shakeAmount, shakeUntil, shakeLength;
    float kickDegrees, kickStart, kickLength, baseFov = -1f;
    Unity.Cinemachine.CinemachineCamera vcam;

    /// <summary>맞은 방향으로 짧게 흔든다(실제 시간 기준이라 히트스톱 중에도 흔들린다)</summary>
    public static void Shake(Vector3 direction, float amount, float seconds)
    {
        if (!active) return;
        if (Time.unscaledTime < active.shakeUntil && amount < active.shakeAmount * 0.6f) return; // 더 센 흔들림이 진행 중
        active.shakeDir = direction; active.shakeAmount = amount;
        active.shakeLength = seconds; active.shakeUntil = Time.unscaledTime + seconds;
    }

    /// <summary>화면을 잠깐 당긴다(시야각을 degrees만큼 좁혔다가 되돌린다)</summary>
    public static void Kick(float degrees, float seconds)
    {
        if (!active) return;
        active.kickDegrees = degrees; active.kickStart = Time.unscaledTime; active.kickLength = seconds;
    }

    void OnEnable() => active = this;
    void OnDisable() { if (active == this) active = null; }

    InputAction lookAction;
    Walkman walkman;
    float yaw, pitch = 15f, currentDistance;

    void Start()
    {
        lookAction = InputSystem.actions.FindAction("Player/Look", true);
        if (target)
        {
            yaw = target.eulerAngles.y;
            walkman = target.GetComponent<Walkman>();
        }
        currentDistance = distance;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    static readonly RaycastHit[] castHits = new RaycastHit[32]; // 매 프레임 배열을 새로 만들지 않는다

    void LateUpdate()
    {
        if (target == null) return;

        // 메뉴가 열리면 마우스 커서가 풀린다. 그때 커서를 움직여도 카메라가 돌아가면 안 된다.
        bool menuOpen = TapeShop.Current || (walkman && walkman.InventoryOpen) || StoryState.InputLocked;
        Vector2 look = menuOpen ? Vector2.zero : lookAction.ReadValue<Vector2>();
        bool pointer = lookAction.activeControl?.device is Pointer;
        float scale = pointer ? mouseSensitivity : stickSensitivity * Time.unscaledDeltaTime;
        yaw += look.x * scale;
        pitch = Mathf.Clamp(pitch - look.y * scale, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        Vector3 back = rotation * Vector3.back;

        // 레벨 지오메트리에 막힐 때만 당겨진다. 지나가는 캐릭터 때문에 카메라가 줌되면 안 된다.
        float maxDistance = StoryState.CameraDistance > 0f ? StoryState.CameraDistance : distance; // 좁은 실내에서는 가깝게
        float wanted = maxDistance;
        int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, back, castHits, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = castHits[i];
            if (hit.distance > 0f && hit.distance < wanted && hit.collider.GetComponentInParent<Health>() == null)
                wanted = hit.distance;
        }

        currentDistance = wanted < currentDistance
            ? wanted
            : Mathf.Lerp(currentDistance, wanted, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));

        transform.SetPositionAndRotation(pivot + back * currentDistance, rotation);
        ApplyHitFeel(rotation);
    }

    void ApplyHitFeel(Quaternion rotation)
    {
        float now = Time.unscaledTime;
        if (now < shakeUntil)
        {
            float k = (shakeUntil - now) / shakeLength;               // 1 → 0으로 줄어든다
            float wave = Mathf.Sin(now * 70f) * k * k;
            Vector3 jolt = shakeDir * wave * shakeAmount
                         + rotation * new Vector3(Mathf.PerlinNoise(now * 40f, 1f) - 0.5f, Mathf.PerlinNoise(1f, now * 40f) - 0.5f, 0f) * shakeAmount * k;
            transform.position += jolt;
        }
        if (!vcam) vcam = GetComponent<Unity.Cinemachine.CinemachineCamera>();
        if (!vcam) return;
        if (baseFov < 0f) baseFov = vcam.Lens.FieldOfView;
        float e = kickLength > 0f ? (now - kickStart) / kickLength : 1f;
        // 빠르게 당겼다가(앞 20%) 천천히 돌아온다
        float pull = e >= 1f ? 0f : e < 0.2f ? e / 0.2f : 1f - (e - 0.2f) / 0.8f;
        var lens = vcam.Lens;
        lens.FieldOfView = baseFov - kickDegrees * Mathf.SmoothStep(0f, 1f, pull);
        vcam.Lens = lens;
    }

    /// <summary>컷신이 끝난 뒤 카메라를 캐릭터 등 뒤로 돌려 놓는다.</summary>
    public void SnapBehindTarget(float pitchDegrees = 12f)
    {
        if (!target) return;
        yaw = target.eulerAngles.y;
        pitch = pitchDegrees;
    }
}
