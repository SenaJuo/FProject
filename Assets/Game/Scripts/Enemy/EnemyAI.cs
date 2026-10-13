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

    [Header("처형기 (executeEvery = 0이면 없음) — 정예·보스: 예고 없이 빠른 큰 공격. 처음엔 못 보지만, 한 번 맞고 되감으면 레이가 기억해 보라색으로 예고된다")]
    public int executeEvery;
    public int executeDamage = 45;
    public float executeWindup = 0.3f;
    /// <summary>레이가 처형기를 맞아 본 적들(되감아도 지워지지 않는다 — 레이만 기억한다)</summary>
    public static readonly System.Collections.Generic.HashSet<EnemyAI> KnownExecute = new();
    /// <summary>처형기를 준비 중이고 레이가 그것을 기억한다(HUD가 보라색 예고를 그린다)</summary>
    public bool ExecuteTelegraph => state == State.Windup && currentIsExecute && KnownExecute.Contains(this);
    bool currentIsExecute;

    [Header("강타 (heavyEvery = 0이면 사용 안 함) — 붉게 오래 예고하는 센 주먹: 맞받아 치지 말고 피하라는 신호")]
    public int heavyEvery;
    public int heavyDamage = 20;
    public float heavyWindup = 0.95f;

    /// <summary>지금 강타를 준비 중(HUD가 머리 위에 붉은 !!를 띄운다)</summary>
    public bool HeavyTelegraph => state == State.Windup && currentIsHeavy;
    /// <summary>완벽 회피에 당해 빈틈이 드러난 중</summary>
    public bool Exposed => state == State.Stagger && exposed;
    /// <summary>완벽 회피가 처음 일어났다(설명용)</summary>
    public static event System.Action<EnemyAI> PerfectDodged;

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
    [Tooltip("제자리를 지킨다(옥상 저격수): 다가가거나 물러나지 않고 그 자리에서 겨눈다")]
    public bool holdPosition;
    [Tooltip("겨누는 중에도 맞으면 겨누기가 끊긴다(가까이 붙으면 저격수를 제압할 수 있게)")]
    public bool aimInterruptible;
    [Tooltip("0보다 크면 앞쪽 ±이 각도 안의 표적만 본다(조준경을 들여다보는 저격수: 등 뒤로 돌아가면 눈치채지 못한다)")]
    public float awarenessArc;
    [Tooltip("조준선 굵기 배율(멀리서도 보이게)")]
    public float laserWidth = 1f;
    [Tooltip("총소리(Resources/Sfx/World/이름). 비우면 기본 총성")]
    public string shotSound;
    public float shotVolume = 0.9f;
    [Tooltip("쥔 총의 손잡이에서 총구까지(m). 권총 0.16, 저격총은 더 길다")]
    public float muzzleDistance = 0.16f;

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
        CullWhenOffscreen();
    }

    /// <summary>
    /// 최적화(2026-10-13): 화면 밖 적은 뼈·피부 계산을 건너뛴다. 전에는 모든 적이 화면 밖에서도 매 프레임 피부를 다시 계산했다(updateWhenOffscreen).
    /// 경계 상자는 엉덩이 뼈 기준 2.6m 정육면체로 넉넉히 잡아 화면 가장자리에서 굳어 보이지 않게 한다.
    /// 애니메이터는 상태·이벤트(공격 타이밍)는 계속 돌고, 뼈를 옮기는 것만 멈춘다(CullUpdateTransforms) — 전투 판정은 그대로.
    /// </summary>
    void CullWhenOffscreen()
    {
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var b = smr.localBounds;
            smr.localBounds = new Bounds(b.center, Vector3.one * Mathf.Max(2.6f, b.size.x, b.size.y, b.size.z));
            smr.updateWhenOffscreen = false;
        }
        var anim = GetComponentInChildren<Animator>();
        if (anim) anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
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
        // 쓰러질 때 끈 충돌체만 다시 켠다(그사이 몸에 붙은 다른 충돌체 — 예: 떨어졌다 다시 쥔 방패의 상자 — 는 건드리지 않는다)
        foreach (var c in disabledOnDeath) if (c) c.enabled = true;
        disabledOnDeath.Clear();
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
        MakeKeyLight();
    }

    // ---------- 보이게 하는 조명 ----------

    [Tooltip("어두운 밤에도 적의 몸이 보이게 얼굴 앞에 두는 약한 조명의 세기(0이면 끔)")]
    public float keyLight = 1.6f;
    Light keyLightSource;

    void MakeKeyLight()
    {
        if (keyLight <= 0f) return;
        var go = new GameObject("KeyLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 2.2f, 0.8f);
        keyLightSource = go.AddComponent<Light>();
        keyLightSource.type = LightType.Point;
        keyLightSource.range = 3.6f;
        keyLightSource.intensity = keyLight;
        keyLightSource.color = new Color(0.78f, 0.84f, 1f);
        keyLightSource.shadows = LightShadows.None;
        // 이 조명은 적의 몸에만 비친다(렌더링 레이어): 바닥·벽에 둥근 빛자국이 생겨 "등이 하나 더 있는" 것처럼 보였다
        keyLightSource.renderingLayerMask = KeyLightLayer;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r is SkinnedMeshRenderer) r.renderingLayerMask |= (uint)KeyLightLayer;
    }

    const int KeyLightLayer = 1 << 7;

    void LateUpdate()
    {
        if (!keyLightSource) return;
        // 레이 근처(28m)의 살아 있는 적만 비춘다
        bool on = state != State.Dead && player && (player.position - transform.position).sqrMagnitude < 28f * 28f;
        if (keyLightSource.enabled != on) keyLightSource.enabled = on;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearKnown() => KnownExecute.Clear();

    void OnDestroy()
    {
        KnownExecute.Remove(this);
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
                        Vector3 dest = holdPosition ? transform.position
                                     : distance > shootRange * 0.75f ? player.position
                                     : distance < 5f ? RetreatPoint(toPlayer)
                                     : HasLineOfSight() ? transform.position : player.position;
                        agent.SetDestination(dest);
                        repathTime = Time.time + 0.25f;
                    }
                    // 시야각이 정해진 적(저격수)은 등 뒤의 레이를 보지 못한다
                    if (awarenessArc > 0f && Vector3.Angle(transform.forward, toPlayer) > awarenessArc) break;
                    if ((distance >= 5f || holdPosition) && distance <= shootRange) Face(toPlayer, 360f);
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
                float duration = WindupDuration;
                float t = Mathf.Clamp01(timer / duration);
                // 준비 동작 초반에는 플레이어를 따라가다가 방향을 확정한다. 그래야 늦은 회피가 통한다.
                if (!currentIsAoe && t < 0.6f) Face(toPlayer, 360f);
                if (currentIsExecute)
                {
                    // 처형기: 모르면 아무 빛도 없다(그냥 빠른 주먹처럼 보인다). 기억하면 보라색으로 번쩍인다
                    if (tint) tint.SetTint(ExecuteColor, KnownExecute.Contains(this) ? 0.6f + 0.4f * Mathf.Sin(timer * 30f) : 0f);
                }
                else if (tint) tint.SetTint(currentIsHeavy ? HeavyColor : telegraphColor, currentIsHeavy ? Mathf.Max(t, 0.5f + 0.5f * Mathf.Sin(timer * 18f)) : t);
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
                if (timer >= 0.35f) { state = State.Chase; if (exposed) { exposed = false; ClearTelegraph(); } }
                break;
        }
    }

    void BeginWindup(bool aoe)
    {
        state = State.Windup;
        timer = 0f;
        currentIsAoe = aoe;
        currentIsExecute = !aoe && !ranged && executeEvery > 0 && (attackCount + 1) % executeEvery == 0;
        currentIsHeavy = !currentIsExecute && !aoe && !ranged && heavyEvery > 0 && (attackCount + 1) % heavyEvery == 0;
        if (currentIsHeavy) Tutorial.Show("perfect"); // 처음 강타를 보면 완벽 회피를 알려 준다
        punchStarted = false;
        StopMoving();
        if (ranged && FirstAim != null) { FirstAim(); FirstAim = null; }
    }

    void ReleaseAttack(float distance, Vector3 toPlayer)
    {
        ClearTelegraph();
        attackCount++;
        bool foreseen = Foreseen > 0;
        if (foreseen) Foreseen--;

        if (currentIsAoe)
        {
            if (distance <= aoeRadius) DamagePlayer(aoeDamage);
        }
        else
        {
            // 주먹과 함께 한 발 들어가되, 플레이어 몸을 파고들지 않게 0.9m 앞에서 멈춘다
            agent.Move(transform.forward * Mathf.Clamp(distance - 0.9f, 0f, 0.4f));
            if (body && !punchStarted) body.CrossFadeInFixedTime("Cross", 0.06f); // 준비 동작이 짧아 미리 시작하지 못한 경우
            bool inReach = distance <= attackRange + 0.6f && Vector3.Angle(transform.forward, toPlayer) <= attackAngle;
            // 완벽 회피: 맞을 자리에서 주먹이 닿기 직전(0.3초 안)에 굴렀다 → 시간이 느려지고 이 적은 한동안 빈틈투성이
            // 되감기로 이 주먹을 기억하면 조금 일찍 굴러도(ForeseenWindow 안) 완벽 회피다
            var pc = playerHealth.GetComponent<PlayerController>();
            if (inReach && pc && pc.IsDodging && pc.DodgeAge <= PerfectWindow) { Expose(); return; }
            if (foreseen && pc && pc.SinceDodge <= ForeseenWindow && distance <= attackRange + 3f) { Expose(); return; }
            if (inReach && currentIsExecute)
            {
                bool first = KnownExecute.Add(this); // 맞았다: 이제 레이는 이 공격을 안다
                DamagePlayer(executeDamage);
                HitFeel.Impact(HitFeel.Tier.Heavy, playerHealth.transform.position + Vector3.up * 1.4f, playerHealth.transform.position - transform.position, playerHealth.gameObject, false, false);
                if (first && GameManager.Instance) GameManager.Instance.Notify("기억했다", 2f);
            }
            else if (inReach) DamagePlayer(currentIsHeavy ? heavyDamage : damage);
        }

        state = State.Recover;
        timer = 0f;
        nextAttackTime = Time.time + attackCooldown;
    }

    const float PerfectWindow = 0.3f;
    /// <summary>되감기로 기억한 공격: 주먹이 닿기 이 초 전부터 굴러도 완벽 회피(보통은 구르는 중에 닿아야 한다)</summary>
    public const float ForeseenWindow = 0.45f;

    /// <summary>레이를 실제로 다치게 했다(되감기의 기억이 "친 놈"으로 적어 둔다)</summary>
    public static event System.Action<EnemyAI> HitPlayer;
    /// <summary>되감기의 기억: 레이가 미리 아는 남은 공격 수. 머리 위 타이밍 막대가 뜨고 완벽 회피가 조금 쉬워진다</summary>
    public int Foreseen { get; private set; }
    public void Foresee(int attacks, bool replace = false) => Foreseen = replace ? attacks : Mathf.Max(Foreseen, attacks);
    /// <summary>공격 준비 중이면 공격까지 남은 초, 아니면 -1</summary>
    public float WindupLeft => state == State.Windup ? WindupDuration - timer : -1f;
    /// <summary>주먹을 준비·내지르거나 맞아 휘청이는 중(손에 든 자세를 잠깐 푼다)</summary>
    public bool InAction => state is State.Windup or State.Recover or State.Stagger or State.Dead;
    public float WindupDuration => ranged ? aimTime : currentIsAoe ? aoeWindup : currentIsExecute ? executeWindup : currentIsHeavy ? heavyWindup : windup;

    void DamagePlayer(int amount)
    {
        if (playerHealth.TakeDamage(amount, transform.position)) HitPlayer?.Invoke(this);
    }
    static readonly Color HeavyColor = new(1f, 0.05f, 0.02f);
    static readonly Color ExecuteColor = new(0.75f, 0.2f, 1f);
    bool currentIsHeavy, exposed;

    /// <summary>완벽 회피에 당했다: 1.2초 동안 휘청이며 막지도 치지도 못한다(방패도 내려간다)</summary>
    void Expose()
    {
        ClearTelegraph();
        state = State.Stagger;
        timer = -0.85f; // 비틀거림(보통 0.35초)을 1.2초로
        exposed = true;
        knockback = Vector3.zero;
        nextAttackTime = Time.time + 1.6f;
        if (TryGetComponent(out ShieldGuard guard)) guard.ForceBreak();
        if (tint) tint.SetTint(new Color(0.6f, 0.85f, 1f), 0.7f);
        HitFeel.SlowMotion(0.3f, 0.35f);
        if (playerHealth && playerHealth.TryGetComponent(out PlayerController pc)) pc.OfferCounter(this); // 지금 공격하면 회피 반격
        PerfectDodged?.Invoke(this);
    }

    void OnDamaged(int amount, Vector3 source)
    {
        if (!IsActive) Activate();
        if (state == State.Idle) state = State.Chase;
        if (!staggerable || (state == State.Windup && !aimInterruptible) || state == State.Dead) return;

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
        disabledOnDeath.Clear();
        foreach (var c in GetComponentsInChildren<Collider>())
            if (c.enabled) { c.enabled = false; disabledOnDeath.Add(c); }
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

    Vector3 Muzzle() => aimIK && aimIK.heldItem ? aimIK.heldItem.position + aimIK.heldItem.forward * muzzleDistance + aimIK.heldItem.up * 0.04f
                                               : transform.position + Vector3.up * 1.45f + transform.forward * 0.5f;

    readonly List<Collider> disabledOnDeath = new();
    static readonly RaycastHit[] sightHits = new RaycastHit[32]; // 매 프레임 배열을 새로 만들지 않는다

    [Tooltip("총잡이가 물러날 수 있는 거리(처음 자리 기준). 끝없이 물러나 싸움이 다른 구역까지 끌려가지 않게")]
    public float retreatLeash = 6f;

    Vector3 RetreatPoint(Vector3 toPlayer)
    {
        Vector3 p = transform.position - toPlayer.normalized * 3f;
        return (p - spawnPosition).sqrMagnitude > retreatLeash * retreatLeash ? transform.position : p; // 더 못 물러나면 버티고 싸운다
    }

    bool HasLineOfSight()
    {
        Vector3 from = transform.position + Vector3.up * 1.5f, to = PlayerChest().position;
        int n = Physics.RaycastNonAlloc(from, (to - from).normalized, sightHits, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = sightHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.IsChildOf(player)) continue;
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
        laser.widthMultiplier = Mathf.Lerp(0.004f, 0.018f, strength) * laserWidth;
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
        var clip = string.IsNullOrEmpty(shotSound) ? null : ProceduralSfx.Recorded(shotSound);
        ProceduralSfx.PlayAt(clip ? clip : ProceduralSfx.Gunshot, from, shotVolume, clip ? 0.35f : 0.7f); // 저격 총성은 멀리서도 크게 울린다
        StartCoroutine(Flash(from));
        // 회피(무적) 중이거나 그새 엄폐물 뒤로 숨었으면 빗나간다
        if (Foreseen > 0) Foreseen--;
        if (HasLineOfSight()) DamagePlayer(shotDamage);
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
        Foreseen = 0;
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
