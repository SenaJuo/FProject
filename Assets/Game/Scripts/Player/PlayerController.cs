using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController), typeof(Health))]
public class PlayerController : MonoBehaviour, IRewindable
{
    [Serializable]
    public struct AttackStep
    {
        public float duration;
        public float hitTime;
        public float damageMultiplier;
        [Tooltip("타격 전에 앞으로 미끄러지는 거리(m)")]
        public float lunge;
        public float radius;
        public float hitStop;
        public Vector3 swingFrom;
        public Vector3 swingTo;
        [Tooltip("재생할 애니메이터 상태(맨손 격투). 비우면 검 휘두르기")]
        public string animState;
    }

    [Header("이동")]
    public float walkSpeed = 5.5f;
    public float sprintSpeed = 9f;
    public float turnSpeed = 900f;
    public float jumpHeight = 1.4f;
    public float gravity = -25f;
    [Tooltip("오를 수 없는 가파른 경사에서 미끄러져 내려가는 속도")]
    public float slideSpeed = 10f;

    [Header("회피")]
    public float dodgeSpeed = 17f;
    public float dodgeDuration = 0.22f;
    public float dodgeCooldown = 0.3f;
    public Color dodgeTint = new(0.5f, 0.9f, 1f);

    [Header("애니메이션")]
    [Tooltip("Starter Assets 이동 파라미터로 움직이는 휴머노이드 Animator")]
    public Animator animator;
    [Tooltip("달리기 클립의 발이 땅과 맞는 속도. 이보다 빠르면 클립 재생도 빨라진다")]
    public float runClipSpeed = 6f;
    [Tooltip("검을 휘두를 때 상체가 따라 도는 정도(공격 클립이 없어서 대신 쓴다)")]
    public float swingTwist = 0.35f;

    [Header("전투")]
    [Tooltip("맨손 격투: 무기를 숨기고 공격 단계마다 animState 동작을 재생한다(검 휘두르기·상체 비틀기 없음)")]
    public bool unarmed;
    public int attackPower = 10;
    public Transform model;
    public Transform weaponPivot;
    public Vector3 weaponRestEuler = new(-60f, 180f, 0f);
    public float attackReach = 1.2f;
    public float autoAimRange = 7f;
    public float hurtDuration = 0.3f;
    public AttackStep[] combo =
    {
        new AttackStep { duration = 0.38f, hitTime = 0.12f, damageMultiplier = 1f, lunge = 0.6f, radius = 1.2f, hitStop = 0.04f, swingFrom = new Vector3(10f, 110f, 0f), swingTo = new Vector3(10f, -80f, 0f) },
        new AttackStep { duration = 0.38f, hitTime = 0.12f, damageMultiplier = 1f, lunge = 0.6f, radius = 1.2f, hitStop = 0.04f, swingFrom = new Vector3(10f, -110f, 0f), swingTo = new Vector3(10f, 80f, 0f) },
        new AttackStep { duration = 0.6f, hitTime = 0.26f, damageMultiplier = 2f, lunge = 1.2f, radius = 1.6f, hitStop = 0.09f, swingFrom = new Vector3(-80f, 0f, 0f), swingTo = new Vector3(40f, 0f, 0f) },
    };

    enum State { Move, Attack, Dodge, Hurt, Dead }

    public bool IsDead => state == State.Dead;
    /// <summary>공격 동작 중(손가락 쥐기 등에서 쓴다)</summary>
    public bool IsAttacking => state == State.Attack;

    State state;
    CharacterController controller;
    Health health;
    BodyTint tint;
    Walkman walkman;
    Transform cam;
    InputAction moveAction, jumpAction, sprintAction, attackAction, dodgeAction;
    float verticalVelocity, stateTimer, dodgeReadyTime;
    int comboIndex;
    float lungeDistance;
    Transform lungeTarget;
    bool attackQueued, hitDone;
    Vector3 dodgeDirection, hurtPush;
    Vector3 groundNormal = Vector3.up;
    bool onSteepSlope, jumpedThisFrame;
    float airTime, animSpeed;
    Vector3 swingEuler;
    Transform chest;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
    static readonly int GroundedId = Animator.StringToHash("Grounded");
    static readonly int JumpId = Animator.StringToHash("Jump");
    static readonly int FreeFallId = Animator.StringToHash("FreeFall");
    static readonly int HitLightHash = Animator.StringToHash("HitLight");
    static readonly int RollHash = Animator.StringToHash("Roll");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        tint = GetComponent<BodyTint>();
        walkman = GetComponent<Walkman>();

        var actions = InputSystem.actions;
        moveAction = actions.FindAction("Player/Move", true);
        jumpAction = actions.FindAction("Player/Jump", true);
        sprintAction = actions.FindAction("Player/Sprint", true);
        attackAction = actions.FindAction("Player/Attack", true);
        dodgeAction = actions.FindAction("Player/Dodge", true);

        health.Damaged += OnDamaged;
        health.Died += OnDied;
        TimeRewind.Register(this);
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    void Start()
    {
        if (Camera.main) cam = Camera.main.transform;
        if (animator && animator.isHuman)
            chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        ResetWeapon();
    }

    void OnDisable() => Time.timeScale = 1f;

    void Update()
    {
        if (TimeRewind.PlayingBack || TapeShop.Current || (walkman && walkman.InventoryOpen)) return;
        if (!controller.enabled) return; // 래그돌·순간이동 중
        float dt = Time.deltaTime;
        bool locked = StoryState.InputLocked;
        Vector3 input = locked ? Vector3.zero : CameraRelative(moveAction.ReadValue<Vector2>());
        onSteepSlope = controller.isGrounded && Vector3.Angle(groundNormal, Vector3.up) > controller.slopeLimit;

        Vector3 horizontal = state switch
        {
            // 컷신 중에는 입력 대신 감독 스크립트가 정한 속도로만 걷는다(0이면 제자리)
            State.Move when locked => UpdateScriptedMove(dt),
            State.Move => UpdateMove(input),
            State.Attack => UpdateAttack(input, dt),
            State.Dodge => UpdateDodge(dt),
            State.Hurt => UpdateHurt(dt),
            _ => Vector3.zero,
        };

        // CharacterController는 스스로 미끄러지지 않으므로, 이게 없으면 깡충깡충 뛰어서 절벽을 오를 수 있다.
        if (onSteepSlope) horizontal += Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized * slideSpeed;

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;
        groundNormal = Vector3.up;
        controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);
        UpdateAnimator(horizontal, dt);
    }

    void UpdateAnimator(Vector3 horizontal, float dt)
    {
        if (!animator) return;

        // 휘두르거나 경직된 동안에는 다리가 땅에 고정되고, 회피는 짧게 내달리는 것처럼 보인다.
        float speed = state switch
        {
            State.Move => new Vector2(horizontal.x, horizontal.z).magnitude,
            State.Dodge => dodgeSpeed,
            _ => 0f,
        };
        // SetFloat의 감쇠 오버로드 대신 여기서 보간한다. 그 오버로드는 플레이 중 목표값에 못 미친 채 멈췄다.
        animSpeed = Mathf.Lerp(animSpeed, Mathf.Min(speed, runClipSpeed), 1f - Mathf.Exp(-dt / 0.06f));
        animator.SetFloat(SpeedId, animSpeed);
        animator.SetFloat(MotionSpeedId, speed > runClipSpeed ? speed / runClipSpeed : 1f);

        // CharacterController.isGrounded는 경사면에서 깜빡이므로, 짧은 레이로 착지/낙하 상태를 안정시킨다.
        bool grounded = controller.isGrounded ||
                        Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.35f, ~0, QueryTriggerInteraction.Ignore);
        airTime = grounded ? 0f : airTime + dt;
        animator.SetBool(GroundedId, grounded);
        if (jumpedThisFrame) animator.SetBool(JumpId, true);
        else if (grounded && verticalVelocity <= 0f) animator.SetBool(JumpId, false);
        animator.SetBool(FreeFallId, !grounded && airTime > 0.15f);
        jumpedThisFrame = false;
    }

    void LateUpdate()
    {
        // 공격 클립이 없으므로 검과 함께 상체를 비틀어, 휘두르기가 몸 전체의 동작으로 보이게 한다.
        if (!chest || state != State.Attack || TimeRewind.PlayingBack || unarmed) return;
        chest.rotation = Quaternion.AngleAxis(swingEuler.y * swingTwist, transform.up)
                       * Quaternion.AngleAxis(swingEuler.x * swingTwist * 0.6f, transform.right)
                       * chest.rotation;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.moveDirection.y < -0.3f && !IsStepEdge(hit)) groundNormal = hit.normal;
    }

    /// <summary>
    /// 인도 턱 같은 낮은 계단의 모서리에 캡슐이 닿으면 접촉 방향이 비스듬해서 '가파른 경사'로 잘못 판정되고,
    /// 미끄러짐 때문에 걸어서는 못 올라간다. 닿은 곳이 발에서 stepOffset 이내이고 그 위가 평평하면 계단으로 보고 무시한다.
    /// (진짜 가파른 경사는 위쪽도 계속 기울어 있으므로 그대로 미끄러진다.)
    /// </summary>
    bool IsStepEdge(ControllerColliderHit hit)
    {
        if (Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit) return false;
        float feet = transform.position.y + controller.center.y - controller.height / 2f;
        float rise = hit.point.y - feet;
        if (rise > controller.stepOffset + 0.02f) return false;
        Vector3 inward = -Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized; // 턱 안쪽(올라설 방향)
        Vector3 probe = hit.point + inward * 0.06f + Vector3.up * (controller.stepOffset + 0.05f);
        return Physics.Raycast(probe, Vector3.down, out var top, controller.stepOffset + 0.15f, ~0, QueryTriggerInteraction.Ignore)
               && Vector3.Angle(top.normal, Vector3.up) <= controller.slopeLimit;
    }

    /// <summary>컷신에서 캐릭터를 걷게 할 때 쓰는 속도(월드 기준, m/s). 컷신이 끝나면 0으로 돌려 놓는다.</summary>
    [System.NonSerialized] public Vector3 scriptedVelocity;

    Vector3 UpdateScriptedMove(float dt)
    {
        Vector3 v = Vector3.ProjectOnPlane(scriptedVelocity, Vector3.up);
        if (v.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(v), turnSpeed * 0.5f * dt);
        return v;
    }

    public void SetWeaponVisible(bool visible)
    {
        if (weaponPivot) weaponPivot.gameObject.SetActive(visible);
    }

    Vector3 UpdateMove(Vector3 input)
    {
        if (StoryState.CombatLocked) attackQueued = false;
        else if (attackAction.WasPressedThisFrame()) { BeginAttack(0, input); return Vector3.zero; }
        if (dodgeAction.WasPressedThisFrame() && Time.time >= dodgeReadyTime) { BeginDodge(input); return Vector3.zero; }
        if (jumpAction.WasPressedThisFrame() && controller.isGrounded && !onSteepSlope)
        {
            verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
            jumpedThisFrame = true;
        }

        if (input.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(input), turnSpeed * Time.deltaTime);

        return input * (sprintAction.IsPressed() ? sprintSpeed : walkSpeed);
    }

    // ---------- 공격 ----------

    void BeginAttack(int index, Vector3 input)
    {
        state = State.Attack;
        comboIndex = index;
        stateTimer = 0f;
        attackQueued = false;
        hitDone = false;
        FaceTarget(input);
        // 달라붙기: 노린 적이 있으면 맞히는 순간 알맞은 거리(0.85m)에 서도록 앞으로 나가는 거리를 맞춘다(허공을 치지 않게).
        // 노린 적이 없으면 제자리에서 친다 — 허공에 연타할 때 조금씩 앞으로 밀려 나가지 않게
        lungeDistance = 0f;
        if (lungeTarget)
        {
            Vector3 to = lungeTarget.position - transform.position; to.y = 0f;
            lungeDistance = Mathf.Clamp(to.magnitude - 0.85f, 0f, Mathf.Max(combo[index].lunge, 1.6f));
        }
        HitFeel.Swing(index >= 2 ? HitFeel.Tier.Heavy : HitFeel.Tier.Light, transform.position + Vector3.up * 1.3f);
        string clip = combo[index].animState;
        if (animator && !string.IsNullOrEmpty(clip)) animator.CrossFadeInFixedTime(clip, 0.06f);
    }

    Vector3 UpdateAttack(Vector3 input, float dt)
    {
        AttackStep step = combo[comboIndex];
        stateTimer += dt;
        if (attackAction.WasPressedThisFrame()) attackQueued = true;

        // 타격이 들어간 뒤에는 회피로 휘두르기를 끊을 수 있고, 미리 눌러 둔 입력이 다음 단계로 이어진다.
        if (hitDone)
        {
            if (dodgeAction.WasPressedThisFrame() && Time.time >= dodgeReadyTime) { BeginDodge(input); return Vector3.zero; }
            if (attackQueued && comboIndex < combo.Length - 1 && stateTimer >= step.hitTime + 0.1f) { BeginAttack(comboIndex + 1, input); return Vector3.zero; }
        }

        if (!unarmed)
        {
            float swing = 1f - Mathf.Pow(1f - Mathf.Clamp01(stateTimer / step.hitTime), 3f);
            swingEuler = Vector3.Lerp(step.swingFrom, step.swingTo, swing);
            weaponPivot.localRotation = Quaternion.Euler(swingEuler);
        }

        if (!hitDone && stateTimer >= step.hitTime)
        {
            hitDone = true;
            PerformHit(step);
        }

        if (stateTimer >= step.duration)
        {
            state = State.Move;
            ResetWeapon();
        }

        return stateTimer < step.hitTime ? transform.forward * (lungeDistance / step.hitTime) : Vector3.zero;
    }

    /// <summary>소프트 락: 스틱 방향에 가장 잘 맞는 적 쪽으로 즉시 돌아선다.</summary>
    void FaceTarget(Vector3 input)
    {
        Vector3 preferred = input.sqrMagnitude > 0.01f ? input.normalized : transform.forward;
        EnemyAI best = null;
        float bestScore = float.MaxValue;

        foreach (var enemy in EnemyAI.All)
        {
            if (enemy.Health.IsDead) continue;
            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            float angle = Vector3.Angle(preferred, to);
            if (distance > autoAimRange || angle > 100f) continue;

            float score = distance + angle * 0.05f;
            if (score < bestScore) { bestScore = score; best = enemy; }
        }

        Vector3 direction = preferred;
        lungeTarget = best ? best.transform : null;
        if (best)
        {
            direction = best.transform.position - transform.position;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
    }

    void PerformHit(AttackStep step)
    {
        Vector3 center = transform.position + Vector3.up + transform.forward * attackReach;
        int damage = Mathf.RoundToInt(attackPower * step.damageMultiplier);
        var tier = comboIndex >= 2 ? HitFeel.Tier.Heavy : comboIndex == 1 ? HitFeel.Tier.Medium : HitFeel.Tier.Light;
        var struck = new System.Collections.Generic.HashSet<Health>();

        foreach (var hit in Physics.OverlapSphere(center, step.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = hit.GetComponentInParent<Health>();
            if (!target || target.team == Team.Player || struck.Contains(target)) continue;
            if (!target.TakeDamage(damage, transform.position)) continue;
            struck.Add(target);
            // 맞은 자리: 주먹은 얼굴 높이, 발차기는 가슴 높이
            Vector3 p = hit.ClosestPoint(center);
            p.y = target.transform.position.y + (tier == HitFeel.Tier.Heavy ? 1.2f : 1.5f);
            bool finisher = target.IsDead && !AnyEnemyLeft();
            HitFeel.Impact(tier, p, target.transform.position - transform.position, target.gameObject, false, finisher);
        }
    }

    /// <summary>근처(30m)에 아직 서 있는 적이 있는가(마지막 한 방 연출용). 아직 눈치채지 못한 적도 센다</summary>
    bool AnyEnemyLeft()
    {
        foreach (var e in EnemyAI.All)
            if (e.isActiveAndEnabled && !e.Health.IsDead && (e.transform.position - transform.position).sqrMagnitude < 900f) return true;
        return false;
    }


    void ResetWeapon()
    {
        if (weaponPivot) weaponPivot.localRotation = Quaternion.Euler(weaponRestEuler);
    }

    // ---------- 회피 ----------

    void BeginDodge(Vector3 input)
    {
        bool hasInput = input.sqrMagnitude > 0.01f;
        dodgeDirection = hasInput ? input.normalized : -transform.forward;
        transform.rotation = Quaternion.LookRotation(dodgeDirection); // 구르는 쪽을 본다(방향이 없으면 뒤로 돌아서 구른다)
        if (animator && animator.HasState(0, RollHash)) animator.CrossFadeInFixedTime(RollHash, 0.05f);

        state = State.Dodge;
        stateTimer = 0f;
        health.Invulnerable = true;
        dodgeReadyTime = Time.time + dodgeDuration + dodgeCooldown;
        if (tint) tint.SetTint(dodgeTint, 0.6f);
        ResetWeapon();
    }

    Vector3 UpdateDodge(float dt)
    {
        stateTimer += dt;
        if (stateTimer >= dodgeDuration) EndDodge();
        float k = Mathf.Clamp01(stateTimer / dodgeDuration);
        return dodgeDirection * (dodgeSpeed * (1f - 0.6f * k * k)); // 구르기 끝 무렵 느려진다
    }

    void EndDodge()
    {
        state = State.Move;
        health.Invulnerable = false;
        if (tint) tint.SetTint(dodgeTint, 0f);
    }

    // ---------- 피해 / 사망 ----------

    void OnDamaged(int amount, Vector3 source)
    {
        if (health.IsDead) return;
        Vector3 away = transform.position - source;
        away.y = 0f;
        hurtPush = away.sqrMagnitude > 0.001f ? away.normalized * 6f : Vector3.zero;
        // 타격감: 멀리서 맞았으면 총알, 가까우면 주먹
        float range = away.magnitude;
        HitFeel.Impact(amount >= 30 ? HitFeel.Tier.Heavy : HitFeel.Tier.Medium, transform.position + Vector3.up * 1.4f, -away, gameObject, true, false, bullet: range > 3f);
        if (animator && animator.HasState(0, HitLightHash)) animator.CrossFadeInFixedTime(amount >= 30 ? "HitHeavy" : "HitLight", 0.05f);
        state = State.Hurt;
        stateTimer = 0f;
        ResetWeapon();
    }

    Vector3 UpdateHurt(float dt)
    {
        stateTimer += dt;
        if (stateTimer >= hurtDuration) state = State.Move;
        return hurtPush * (1f - stateTimer / hurtDuration);
    }

    void OnDied()
    {
        state = State.Dead;
        ResetWeapon();
        if (model) model.localRotation = Quaternion.Euler(-80f, 0f, 0f);
    }

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        Teleport(position, rotation);
        health.Revive();
    }

    /// <summary>체력은 건드리지 않고 플레이어를 이동시킨 뒤, 아무 동작도 하지 않고 서 있는 상태로 되돌린다. 컷신 장면 전환에도 쓴다.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;

        EndDodge();
        verticalVelocity = 0f;
        animSpeed = 0f; // 순간이동한 자리에서 달리던 다리 동작이 이어지지 않게
        if (model) model.localRotation = Quaternion.identity;
        ResetWeapon();
    }

    struct Snapshot
    {
        public Pose pose;
        public Quaternion model, weapon;
        public int animState;
        public float animTime, animSpeed;
        public bool grounded;
    }

    public object CaptureState()
    {
        var s = new Snapshot
        {
            pose = new Pose(transform.position, transform.rotation),
            model = model ? model.localRotation : Quaternion.identity,
            weapon = weaponPivot ? weaponPivot.localRotation : Quaternion.identity,
        };
        if (animator)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            s.animState = info.fullPathHash;
            s.animTime = info.normalizedTime;
            s.animSpeed = animator.GetFloat(SpeedId);
            s.grounded = animator.GetBool(GroundedId);
        }
        return s;
    }

    public void RestoreState(object state, bool preview)
    {
        var s = (Snapshot)state;
        if (preview)
        {
            // 역재생 중: 포즈만 적용해서 휘두르기, 낙하, 발걸음이 눈에 보이게 거꾸로 흐른다.
            controller.enabled = false;
            transform.SetPositionAndRotation(s.pose.position, s.pose.rotation);
            if (model) model.localRotation = s.model;
            if (weaponPivot) weaponPivot.localRotation = s.weapon;
        }
        else Teleport(s.pose.position, s.pose.rotation);

        if (animator && s.animState != 0)
        {
            // Animator를 기록된 프레임으로 건너뛴다. Update(0)은 시간이 멈춘 동안에도 포즈를 계산한다.
            animSpeed = s.animSpeed;
            animator.SetFloat(SpeedId, animSpeed);
            animator.SetBool(GroundedId, s.grounded);
            animator.SetBool(JumpId, false);
            animator.SetBool(FreeFallId, false);
            animator.Play(s.animState, 0, s.animTime);
            animator.Update(0f);
        }
    }

    Vector3 CameraRelative(Vector2 stick)
    {
        if (cam == null) return new Vector3(stick.x, 0f, stick.y);
        Vector3 forward = cam.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = cam.right;
        right.y = 0f;
        right.Normalize();
        return Vector3.ClampMagnitude(forward * stick.y + right * stick.x, 1f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up + transform.forward * attackReach, 1.2f);
    }
}
