using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class EnemyAI : MonoBehaviour, IRewindable
{
    public static readonly List<EnemyAI> All = new();

    public string displayName = "Machine";
    public bool isBoss;

    [Header("근접 공격")]
    public float attackRange = 1.8f;
    public float attackAngle = 70f;
    public int damage = 10;
    [Tooltip("공격 직전 예고 시간 — 플레이어가 피할 수 있는 틈")]
    public float windup = 0.55f;
    public float recover = 0.6f;
    public float attackCooldown = 1.1f;
    [Tooltip("맞으면 행동이 끊긴다(공격 준비 중은 제외)")]
    public bool staggerable = true;
    public Color telegraphColor = new(1f, 0.3f, 0.1f);

    [Header("범위 공격 (aoeEvery = 0이면 사용 안 함)")]
    public int aoeEvery;
    public float aoeRadius = 6f;
    public float aoeWindup = 1.2f;
    public int aoeDamage = 30;
    public Transform aoeIndicator;

    [Header("사람 적(휴머노이드)")]
    [Tooltip("모델의 Animator. 있으면 피격·공격 동작 클립을 재생한다")]
    public Animator body;
    [Tooltip("있으면 죽을 때 물리로 쓰러지고 시체가 남는다(작아지며 사라지는 대신)")]
    public Ragdoll ragdoll;

    [Header("총잡이 (ranged = true)")]
    public bool ranged;
    public float shootRange = 11f;
    [Tooltip("붉은 조준선으로 겨누는 시간 — 피하거나 되감을 틈")]
    public float aimTime = 1.1f;
    public int shotDamage = 45;
    public float shotCooldown = 2.2f;
    [Tooltip("겨누는 팔과 총(HumanoidIK). 총구 위치도 여기서 얻는다")]
    public HumanoidIK aimIK;
    [Tooltip("총을 처음 겨눌 때 한 번 알린다(되감기 안내용)")]
    public System.Action FirstAim;

    /// <summary>소속 CombatZone이 시작될 때 설정된다. 활성화된 적은 구역 안 어디서든 플레이어를 추적한다.</summary>
    public bool IsActive { get; private set; }
    public Health Health { get; private set; }

    enum State { Idle, Chase, Windup, Recover, Stagger, Dead }

    State state;
    NavMeshAgent agent;
    BodyTint tint;
    Transform player;
    Health playerHealth;
    Vector3 spawnPosition, baseScale;
    Quaternion spawnRotation;
    float timer, nextAttackTime, repathTime;
    int attackCount;
    bool currentIsAoe, punchStarted;
    // 주먹(크로스) 동작에서 주먹이 가장 멀리 뻗는 순간까지 걸리는 시간(실측 0.28초). 그만큼 먼저 동작을 시작해 피해와 맞춘다
    const float PunchImpactDelay = 0.28f;
    Vector3 knockback;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        Health = GetComponent<Health>();
        tint = GetComponent<BodyTint>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        baseScale = transform.localScale;
        Health.Damaged += OnDamaged;
        Health.Died += OnDied;
        TimeRewind.Register(this);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    struct Snapshot
    {
        public Pose pose;
        public Vector3 scale;
        public bool visible, dead, active;
    }

    public object CaptureState() => new Snapshot
    {
        pose = new Pose(transform.position, transform.rotation),
        scale = transform.localScale,
        visible = gameObject.activeSelf,
        dead = state == State.Dead,
        active = IsActive,
    };

    public void RestoreState(object saved, bool preview)
    {
        var s = (Snapshot)saved;
        StopAllCoroutines();

        if (preview)
        {
            // 역재생 중: AI는 돌리지 않고 포즈(그리고 거꾸로 되돌아가는 사망 축소 연출)만 보여준다.
            if (!s.dead && ragdoll && ragdoll.IsActive) ragdoll.Restore(); // 쓰러지기 전으로 감기면 다시 선다
            HideLaser();
            if (gameObject.activeSelf != s.visible) gameObject.SetActive(s.visible);
            if (agent.enabled) agent.enabled = false;
            transform.SetPositionAndRotation(s.pose.position, s.pose.rotation);
            transform.localScale = s.scale;
            return;
        }

        transform.localScale = baseScale;
        IsActive = s.active;
        ClearTelegraph();

        if (s.dead)
        {
            state = State.Dead;
            if (!ragdoll) gameObject.SetActive(false); // 사람 적은 쓰러진 시체를 그대로 둔다
            return;
        }
        if (ragdoll && ragdoll.IsActive) ragdoll.Restore();
        HideLaser();
        if (aimIK) aimIK.aim = false;

        // 스냅샷 시점에 살아 있었다 — 그 뒤에 죽었다면 되살린다.
        gameObject.SetActive(true);
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
        agent.enabled = true;
        agent.Warp(s.pose.position);
        transform.rotation = s.pose.rotation;
        state = State.Idle;
        timer = 0f;
        knockback = Vector3.zero;
        attackCount = 0;
    }

    void Start()
    {
        // 태그 검색은 같은 태그가 붙은 자식(캐릭터 모델 등)을 잡을 수 있으므로, 게임 관리자에 등록된 플레이어를 쓴다.
        var gm = GameManager.Instance;
        if (gm && gm.player)
        {
            player = gm.player.transform;
            playerHealth = gm.player.GetComponent<Health>();
        }

        // 에이전트는 꺼진 상태로 시작하므로, 씬의 NavMeshSurface가 데이터를 등록한 뒤에야 바인딩된다.
        agent.enabled = true;
        if (aoeIndicator) aoeIndicator.gameObject.SetActive(false);
        GameManager.PlayerRespawned += ResetToSpawn;
    }

    void OnDestroy()
    {
        GameManager.PlayerRespawned -= ResetToSpawn;
        TimeRewind.Unregister(this);
    }

    public void Activate() => IsActive = true;

    /// <summary>이 적이 속한 교전 구역(CombatZone이 정한다). 없으면 null</summary>
    public CombatZone Zone { get; set; }

    /// <summary>추격을 멈추고 제자리에 선다(컷신에서 배우처럼 세워 둘 때).</summary>
    public void Deactivate()
    {
        IsActive = false;
        StopMoving();
        ClearTelegraph();
        HideLaser();
        if (aimIK) aimIK.aim = false;
        if (state != State.Dead) state = State.Idle;
    }

    /// <summary>내비메시 위의 가장 가까운 자리로 옮기고 지정한 방향을 보게 한다(컷신 배치용).</summary>
    public void PlaceAt(Vector3 position, Quaternion rotation)
    {
        if (NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas)) position = hit.position;
        if (agent.enabled && agent.isOnNavMesh) agent.Warp(position);
        else transform.position = position;
        transform.rotation = rotation;
    }

    void Update()
    {
        if (state == State.Dead || !IsActive || player == null || !agent.isOnNavMesh) return;

        float dt = Time.deltaTime;
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        switch (state)
        {
            case State.Idle:
                if (!playerHealth.IsDead) state = State.Chase;
                break;

            case State.Chase:
                if (playerHealth.IsDead) { StopMoving(); state = State.Idle; break; }
                agent.isStopped = false;
                if (ranged)
                {
                    // 총잡이는 거리를 둔다: 멀면 다가가고, 5m 안으로 붙으면 물러나고, 그 사이에서는 멈춰 겨눈다
                    if (Time.time >= repathTime)
                    {
                        // 사이 거리인데 가려서 못 쏘면 다가가서 쏠 자리를 찾는다(엄폐물 앞에서 영원히 서 있던 문제)
                        Vector3 dest = distance > shootRange * 0.75f ? player.position
                                     : distance < 5f ? transform.position - toPlayer.normalized * 3f
                                     : HasLineOfSight() ? transform.position : player.position;
                        agent.SetDestination(dest);
                        repathTime = Time.time + 0.25f;
                    }
                    if (distance >= 5f && distance <= shootRange) Face(toPlayer, 360f);
                    if (distance <= shootRange && Time.time >= nextAttackTime && HasLineOfSight()) BeginWindup(false);
                    break;
                }
                if (Time.time >= repathTime)
                {
                    agent.SetDestination(player.position);
                    repathTime = Time.time + 0.2f;
                }
                bool aoeTurn = aoeEvery > 0 && (attackCount + 1) % aoeEvery == 0;
                float range = aoeTurn ? aoeRadius * 0.6f : attackRange;
                if (distance <= range && Time.time >= nextAttackTime) BeginWindup(aoeTurn);
                break;

            case State.Windup when ranged:
                // 겨누기: 팔을 들어 플레이어 가슴을 노리고, 붉은 조준선이 점점 진해진다
                timer += dt;
                Face(toPlayer, 540f);
                if (aimIK) { aimIK.aimTarget = PlayerChest(); aimIK.aim = true; }
                DrawLaser(Mathf.Clamp01(timer / aimTime));
                if (timer >= aimTime) Shoot();
                break;

            case State.Windup:
                timer += dt;
                float duration = currentIsAoe ? aoeWindup : windup;
                float t = Mathf.Clamp01(timer / duration);
                // 준비 동작 초반에는 플레이어를 따라가다가 방향을 확정한다. 그래야 늦은 회피가 통한다.
                if (!currentIsAoe && t < 0.6f) Face(toPlayer, 360f);
                if (tint) tint.SetTint(telegraphColor, t);
                if (currentIsAoe) ShowIndicator(t);
                if (!currentIsAoe && body && !punchStarted && timer >= duration - PunchImpactDelay)
                {
                    body.CrossFadeInFixedTime("Cross", 0.06f); // 사람 적: 주먹이 뻗는 순간이 피해 시각과 겹치게 미리 시작한다
                    HitFeel.Swing(HitFeel.Tier.Light, transform.position + Vector3.up * 1.3f);
                    punchStarted = true;
                }
                if (timer >= duration) ReleaseAttack(distance, toPlayer);
                break;

            case State.Recover:
                timer += dt;
                if (timer >= recover) state = State.Chase;
                break;

            case State.Stagger:
                timer += dt;
                agent.Move(knockback * dt);
                knockback = Vector3.Lerp(knockback, Vector3.zero, 10f * dt);
                if (timer >= 0.35f) state = State.Chase;
                break;
        }
    }

    void BeginWindup(bool aoe)
    {
        state = State.Windup;
        timer = 0f;
        currentIsAoe = aoe;
        punchStarted = false;
        StopMoving();
        if (ranged && FirstAim != null) { FirstAim(); FirstAim = null; }
    }

    void ReleaseAttack(float distance, Vector3 toPlayer)
    {
        ClearTelegraph();
        attackCount++;

        if (currentIsAoe)
        {
            if (distance <= aoeRadius) playerHealth.TakeDamage(aoeDamage, transform.position);
        }
        else
        {
            // 주먹과 함께 한 발 들어가되, 플레이어 몸을 파고들지 않게 0.9m 앞에서 멈춘다
            agent.Move(transform.forward * Mathf.Clamp(distance - 0.9f, 0f, 0.4f));
            if (body && !punchStarted) body.CrossFadeInFixedTime("Cross", 0.06f); // 준비 동작이 짧아 미리 시작하지 못한 경우
            if (distance <= attackRange + 0.6f && Vector3.Angle(transform.forward, toPlayer) <= attackAngle)
                playerHealth.TakeDamage(damage, transform.position);
        }

        state = State.Recover;
        timer = 0f;
        nextAttackTime = Time.time + attackCooldown;
    }

    void OnDamaged(int amount, Vector3 source)
    {
        if (!IsActive) Activate();
        if (state == State.Idle) state = State.Chase;
        if (!staggerable || state == State.Windup || state == State.Dead) return;

        state = State.Stagger;
        timer = 0f;
        StopMoving();
        HideLaser();
        if (aimIK) aimIK.aim = false;
        if (body) body.CrossFadeInFixedTime(amount >= 18 ? "HitHeavy" : "HitLight", 0.05f);
        Vector3 away = transform.position - source;
        away.y = 0f;
        knockback = away.normalized * (amount >= 18 ? 11f : 5f); // 마무리(돌려차기)는 멀리 날아간다
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.4f);
    }

    void OnDied()
    {
        state = State.Dead;
        ClearTelegraph();
        StopMoving();
        agent.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        HideLaser();
        if (aimIK) aimIK.aim = false;
        if (ragdoll)
        {
            // 맞은 방향으로 물리 쓰러짐(시체는 남는다)
            Vector3 push = knockback.sqrMagnitude > 0.01f ? knockback.normalized : -transform.forward;
            ragdoll.Activate(push * 40f + Vector3.up * 4f, HumanBodyBones.Chest);
            return;
        }
        StartCoroutine(DeathRoutine());
    }

    // ---------- 총잡이 ----------

    LineRenderer laser;
    Transform chestPoint;

    Transform PlayerChest()
    {
        if (chestPoint == null && player)
        {
            var anim = player.GetComponentInChildren<Animator>();
            chestPoint = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Chest) : player;
        }
        return chestPoint;
    }

    Vector3 Muzzle() => aimIK && aimIK.heldItem ? aimIK.heldItem.position + aimIK.heldItem.forward * 0.16f + aimIK.heldItem.up * 0.04f
                                               : transform.position + Vector3.up * 1.45f + transform.forward * 0.5f;

    bool HasLineOfSight()
    {
        Vector3 from = transform.position + Vector3.up * 1.5f, to = PlayerChest().position;
        foreach (var hit in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<PlayerController>()) continue;
            if (hit.rigidbody) continue; // 쓰러진 몸(래그돌)은 가리지 않는다
            return false;
        }
        return true;
    }

    void DrawLaser(float strength)
    {
        if (laser == null)
        {
            laser = new GameObject("AimLaser").AddComponent<LineRenderer>();
            laser.transform.SetParent(transform, false);
            laser.positionCount = 2;
            laser.useWorldSpace = true;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(4f, 0.15f, 0.1f, 1f)); // 1보다 밝게 해서 블룸으로 빛나 보이게
            laser.sharedMaterial = mat;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        laser.enabled = true;
        laser.widthMultiplier = Mathf.Lerp(0.004f, 0.018f, strength);
        laser.SetPosition(0, Muzzle());
        laser.SetPosition(1, PlayerChest().position);
    }

    void HideLaser()
    {
        if (laser) laser.enabled = false;
    }

    void Shoot()
    {
        HideLaser();
        Vector3 from = Muzzle(), to = PlayerChest().position;
        ProceduralSfx.PlayAt(ProceduralSfx.Gunshot, from, 0.9f, 0.7f);
        StartCoroutine(Flash(from));
        // 회피(무적) 중이거나 그새 엄폐물 뒤로 숨었으면 빗나간다
        if (HasLineOfSight()) playerHealth.TakeDamage(shotDamage, transform.position);
        state = State.Recover;
        timer = 0f;
        nextAttackTime = Time.time + shotCooldown;
        attackCount++;
    }

    IEnumerator Flash(Vector3 at)
    {
        var light = new GameObject("MuzzleFlash").AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.5f);
        light.intensity = 10f;
        light.range = 6f;
        light.transform.position = at;
        Destroy(light.gameObject, 0.06f); // 되감기가 이 코루틴을 멈춰도 불빛은 사라진다
        yield return new WaitForSeconds(0.06f);
        yield return new WaitForSeconds(0.35f);
        if (aimIK && state != State.Windup) aimIK.aim = false;
    }

    IEnumerator DeathRoutine()
    {
        Vector3 start = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
        {
            transform.localScale = Vector3.Lerp(start, Vector3.zero, t * t);
            yield return null;
        }
        gameObject.SetActive(false);
    }

    void ResetToSpawn()
    {
        if (state == State.Dead) return;
        ClearTelegraph();
        HideLaser();
        if (aimIK) aimIK.aim = false;
        if (agent.isOnNavMesh) agent.Warp(spawnPosition);
        transform.rotation = spawnRotation;
        Health.Revive();
        state = State.Idle;
        attackCount = 0;
        // 제자리에서 기다린다: 구역 밖에서 깨운 적이 멀리 떨어진 저장 지점까지 쫓아오지 않게.
        // 레이가 아직 싸우는 구역 안에서 다시 시작하면(구역 입구가 저장 지점) 싸움을 이어 간다
        if (!(Zone && Zone.HoldsPlayer)) IsActive = false;
    }

    void StopMoving()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    void Face(Vector3 direction, float degreesPerSecond)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
    }

    void ShowIndicator(float t)
    {
        if (aoeIndicator == null) return;
        aoeIndicator.gameObject.SetActive(true);
        float diameter = aoeRadius * 2f * t / transform.lossyScale.x;
        aoeIndicator.localScale = new Vector3(diameter, 0.005f, diameter);
    }

    void ClearTelegraph()
    {
        if (tint) tint.SetTint(telegraphColor, 0f);
        if (aoeIndicator) aoeIndicator.gameObject.SetActive(false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
