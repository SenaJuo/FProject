using System;
using UnityEngine;

public enum Team { Player, Enemy }

public class Health : MonoBehaviour, IRewindable
{
    public Team team = Team.Enemy;
    public int maxHealth = 100;
    [Tooltip("맞은 뒤 무적 시간(초, 0 = 없음)")]
    public float invulnerableAfterHit;

    public int Current { get; private set; }
    public int Max => maxHealth;
    public bool IsDead => Current <= 0;
    public bool Invulnerable { get; set; }

    /// <summary>피해량과 피해가 가해진 월드 위치.</summary>
    public event Action<int, Vector3> Damaged;
    /// <summary>막았다(방패 등): 피해량과 위치. 피해는 들어가지 않았다</summary>
    public event Action<int, Vector3> Blocked;
    /// <summary>있으면 맞기 직전에 묻는다: true를 돌려주면 막은 것(피해 없음)</summary>
    public Func<int, Vector3, bool> Blocks;
    public event Action Died;

    float invulnerableUntil;

    void Awake()
    {
        Current = maxHealth;
        TimeRewind.Register(this);
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    public object CaptureState() => new Vector2Int(Current, maxHealth);

    public void RestoreState(object state, bool preview)
    {
        var s = (Vector2Int)state;
        Current = s.x;
        maxHealth = s.y;
        Invulnerable = false;
        invulnerableUntil = 0f;
    }

    /// <summary>받는 피해 배율(앰비언트 테이프 능력 「고요」가 잠시 낮춘다)</summary>
    [System.NonSerialized] public float damageTakenScale = 1f;

    /// <summary>
    /// 체력 바닥: 0보다 크면 체력이 이 아래로 떨어지지 않는다(2026-10-14). 보스의 이야기 장면(1장 반장 25% 테이프 소각,
    /// 3장 파트너 40% 도주, 4장 파트너 60%·40%)을 큰 한 방(기억의 일격 ×3 + 능력 ×1.5)이 건너뛰어
    /// 1장 보스전이 갑자기 2장으로 넘어가던 버그. 감독이 그 장면에 들어서면 0으로 푼다
    /// </summary>
    [System.NonSerialized] public int floor;

    public bool TakeDamage(int amount, Vector3 source)
    {
        if (IsDead || Invulnerable || Time.time < invulnerableUntil || amount <= 0) return false;
        if (Blocks != null && Blocks(amount, source)) { Blocked?.Invoke(amount, source); return false; }

        if (!Mathf.Approximately(damageTakenScale, 1f)) amount = Mathf.Max(1, Mathf.RoundToInt(amount * damageTakenScale)); // 테이프 능력(고요)
        Current = Mathf.Max(floor > 0 ? Mathf.Min(floor, Current) : 0, Current - amount);
        invulnerableUntil = Time.time + invulnerableAfterHit;
        Damaged?.Invoke(amount, source);
        if (Current == 0) Died?.Invoke();
        return true;
    }

    public void SetMax(int value, bool refill)
    {
        maxHealth = value;
        Current = refill ? value : Mathf.Min(Current, value);
    }

    public void Revive() => Current = maxHealth;

    /// <summary>체력을 그 값으로 맞춘다(피해 이벤트 없이). 예: 보스 마지막 싸움 중 다시 시작하면 그때 남은 체력으로</summary>
    public void SetCurrent(int value) => Current = Mathf.Clamp(value, 0, maxHealth);

    /// <summary>체력을 amount만큼 채운다(최대 체력까지). 실제로 찬 양을 돌려준다</summary>
    public int Heal(int amount)
    {
        if (IsDead || amount <= 0) return 0;
        int before = Current;
        Current = Mathf.Min(maxHealth, Current + amount);
        return Current - before;
    }

    public void GrantInvulnerability(float seconds) => invulnerableUntil = Time.time + seconds;
}
