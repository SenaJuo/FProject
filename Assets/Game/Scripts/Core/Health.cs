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

    public bool TakeDamage(int amount, Vector3 source)
    {
        if (IsDead || Invulnerable || Time.time < invulnerableUntil || amount <= 0) return false;
        if (Blocks != null && Blocks(amount, source)) { Blocked?.Invoke(amount, source); return false; }

        if (!Mathf.Approximately(damageTakenScale, 1f)) amount = Mathf.Max(1, Mathf.RoundToInt(amount * damageTakenScale)); // 테이프 능력(고요)
        Current = Mathf.Max(0, Current - amount);
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
