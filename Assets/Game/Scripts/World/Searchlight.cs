using UnityEngine;

/// <summary>
/// 크레인 탐조등: 높은 곳에서 큰 원형 빛이 정해진 길(바닥 위 점들)을 천천히 오가며 훑는다.
///  · 빛 원(반지름 radius) 안에, 탐조등에서 가리는 것 없이 보이는 곳에 레이가 있으면 의심이 찬다(약 0.8초).
///  · 다 차면 빛이 붉어지고 레이를 몇 초 따라가며, 근처(alertRadius) 순찰을 모두 깨운다.
///  · 컨테이너·창고 그늘에 들어가면 빛이 닿지 않는다. 되감으면 빛의 자리·의심·발각도 함께 돌아간다.
/// </summary>
public class Searchlight : MonoBehaviour, IRewindable
{
    [Tooltip("빛이 지나가는 바닥의 점들(순서대로 갔다가 되돌아온다)")]
    public Vector3[] path = System.Array.Empty<Vector3>();
    [Tooltip("빛 원이 바닥을 움직이는 빠르기(m/s)")]
    public float speed = 3.2f;
    [Tooltip("바닥에 비치는 빛 원의 반지름(m)")]
    public float radius = 4.2f;
    public float alertRadius = 26f;
    public Light beam;
    [Tooltip("빗속에 보이는 빛줄기(길이 7m·끝 반지름 약 2.5m 원뿔 메시를 늘려 쓴다)")]
    public Transform cone;
    public Color calmColor = new(0.85f, 0.92f, 1f);
    public Color alarmColor = new(1f, 0.25f, 0.18f);

    /// <summary>시험용</summary>
    public float Suspicion { get; private set; }
    public bool Alarmed => alarmUntil > Time.time;
    public Vector3 Aim => aim;

    public static event System.Action<Searchlight> Spotted;

    float along, dir = 1f, alarmUntil = -1f, totalLength;
    Vector3 aim;
    Transform player, playerChest;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        TimeRewind.Register(this);
        for (int i = 1; i < path.Length; i++) totalLength += Vector3.Distance(path[i - 1], path[i]);
        aim = path.Length > 0 ? path[0] : transform.position + transform.forward * 10f;
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm && gm.player)
        {
            player = gm.player.transform;
            var anim = gm.player.animator;
            playerChest = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Chest) : player;
        }
        GameManager.PlayerRespawned += OnRespawn;
    }

    void OnDisable() => GameManager.PlayerRespawned -= OnRespawn;

    void OnRespawn() { Suspicion = 0f; alarmUntil = -1f; }

    struct Snapshot { public float along, dir, suspicion, alarmLeft; public Vector3 aim; }

    public object CaptureState() => new Snapshot { along = along, dir = dir, suspicion = Suspicion, alarmLeft = alarmUntil - Time.time, aim = aim };

    public void RestoreState(object state, bool preview)
    {
        var s = (Snapshot)state;
        along = s.along; dir = s.dir; aim = s.aim;
        if (preview) { Point(); return; }
        Suspicion = s.suspicion;
        alarmUntil = Time.time + s.alarmLeft;
        Point();
    }

    void Update()
    {
        if (TimeRewind.PlayingBack) return;
        float dt = Time.deltaTime;
        if (Alarmed && player)
        {
            // 들켰다: 레이를 쫓아간다(조금 늦게)
            aim = Vector3.MoveTowards(aim, Ground(player.position), speed * 2.2f * dt);
        }
        else if (path.Length > 1 && totalLength > 0.1f)
        {
            along += dir * speed * dt;
            if (along >= totalLength) { along = totalLength; dir = -1f; }
            if (along <= 0f) { along = 0f; dir = 1f; }
            Vector3 target = PointAlong(along);
            aim = Vector3.MoveTowards(aim, target, speed * 2.5f * dt); // 들켰다 풀린 뒤 길로 부드럽게 돌아온다
        }
        Point();
        Detect(dt);
    }

    void Point()
    {
        Vector3 from = transform.position;
        Vector3 to = aim - from;
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.LookRotation(to);
        if (beam)
        {
            float dist = to.magnitude;
            beam.range = dist + 8f;
            beam.spotAngle = Mathf.Clamp(Mathf.Atan(radius / dist) * Mathf.Rad2Deg * 2.2f, 4f, 60f);
            beam.innerSpotAngle = beam.spotAngle * 0.7f;
            beam.color = Color.Lerp(calmColor, alarmColor, Alarmed ? 1f : Suspicion);
        }
        if (cone)
        {
            float dist = to.magnitude;
            cone.localScale = new Vector3(radius / 2.5f, radius / 2.5f, dist / 7f);
        }
    }

    void Detect(float dt)
    {
        if (!player || !playerChest) return;
        var pc = player.GetComponent<PlayerController>();
        if (pc && pc.IsDead) return;
        Vector3 flat = Ground(player.position) - aim;
        bool inside = flat.magnitude <= radius && Clear(transform.position, playerChest.position);
        Suspicion = inside ? Mathf.Min(1f, Suspicion + dt * 1.25f) : Mathf.Max(0f, Suspicion - dt * 0.5f);
        if (Suspicion >= 1f && !Alarmed) Alarm();
        if (Alarmed && inside) alarmUntil = Mathf.Max(alarmUntil, Time.time + 1.5f);
    }

    void Alarm()
    {
        alarmUntil = Time.time + 4f;
        foreach (var sight in PatrolSight.All)
        {
            if (!sight || sight.Alerted || sight.Enemy.Health.IsDead) continue;
            if (Vector3.Distance(sight.transform.position, player.position) <= alertRadius) sight.Alert(false);
        }
        StoryState.Say("조직원", "탐조등에 걸렸다! 저기다!", 1.8f);
        Spotted?.Invoke(this);
    }

    bool Clear(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float length = d.magnitude;
        int n = Physics.RaycastNonAlloc(from, d / length, hits, length - 0.3f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var t = hits[i].collider.transform;
            if (player && t.IsChildOf(player)) continue;
            if (hits[i].rigidbody) continue;
            if (hits[i].collider.GetComponentInParent<EnemyAI>()) continue;

            return false;
        }
        return true;
    }

    Vector3 PointAlong(float s)
    {
        for (int i = 1; i < path.Length; i++)
        {
            float seg = Vector3.Distance(path[i - 1], path[i]);
            if (s <= seg) return Vector3.Lerp(path[i - 1], path[i], seg > 0f ? s / seg : 0f);
            s -= seg;
        }
        return path[^1];
    }

    static Vector3 Ground(Vector3 p) => new(p.x, 0f, p.z);
}
