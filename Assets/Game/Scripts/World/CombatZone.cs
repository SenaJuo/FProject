using System;
using UnityEngine;

/// <summary>
/// 교전 구역: 트리거에 들어가면 소속 적들이 깨어난다(그리고 선택적으로, 적이 모두 죽을 때까지
/// 플레이어 뒤의 차단벽을 봉쇄한다).
/// </summary>
public class CombatZone : MonoBehaviour, IRewindable
{
    public static event Action<CombatZone> Entered;
    public static event Action<CombatZone> Cleared;

    [Tooltip("들어올 때 화면에 띄울 이름. 일반 필드 전투는 비워 둔다")]
    public string zoneName;
    [Tooltip("이 구역이 진행 중일 때의 부활 지점. 비우면 플레이어가 들어온 자리")]
    public Transform checkpoint;
    [Tooltip("들어올 때 저장 지점을 여기로 옮긴다. 1장부터는 가게와 구역 시작점에서만 저장하므로 끈다")]
    public bool saveOnEnter = true;
    public EnemyAI[] enemies;
    public ZoneGate[] closeOnEnter = Array.Empty<ZoneGate>();
    [Tooltip("이 구역을 정리하면 스테이지 클리어")]
    public bool isFinal;

    public bool IsCleared { get; private set; }
    public int Remaining { get; private set; }

    bool started;

    /// <summary>싸움이 시작되었고 레이가 구역 안에 있다</summary>
    public bool HoldsPlayer
    {
        get
        {
            var gm = GameManager.Instance;
            return started && gm && gm.player && GetComponent<Collider>().bounds.Contains(gm.player.transform.position);
        }
    }

    void Awake()
    {
        TimeRewind.Register(this);
        GameManager.PlayerRespawned += OnPlayerRespawned;
    }

    void OnDestroy()
    {
        TimeRewind.Unregister(this);
        GameManager.PlayerRespawned -= OnPlayerRespawned;
    }

    /// <summary>
    /// 끝내지 못한 싸움에서 쓰러져 저장 지점으로 돌아가면 이 구역도 처음 상태로: 살아 있는 적은 제자리에 서서 기다리고,
    /// 다시 들어오면 싸움이 시작된다(멀리 떨어진 저장 지점까지 적이 쫓아오지 않게). 이미 쓰러뜨린 적은 그대로다.
    /// </summary>
    void OnPlayerRespawned()
    {
        if (IsCleared || !started) return;
        // 구역 안에서 다시 시작하면(구역 입구가 저장 지점인 경우) 트리거에 다시 들어오는 일이 없으니 싸움을 그대로 이어 간다
        if (HoldsPlayer) return;
        started = false;
        foreach (var gate in closeOnEnter) gate.Open();
        foreach (var enemy in enemies) if (enemy && !enemy.Health.IsDead) enemy.Deactivate();
    }

    public object CaptureState() => (started, IsCleared, Remaining);

    public void RestoreState(object state, bool preview)
    {
        var s = ((bool started, bool cleared, int remaining))state;
        started = s.started;
        IsCleared = s.cleared;
        Remaining = s.remaining;
    }

    void Start()
    {
        GetComponent<Collider>().isTrigger = true;
        Remaining = 0;
        foreach (var enemy in enemies)
        {
            if (!enemy) continue; // 빈 칸은 건너뛴다
            Remaining++;
            enemy.Zone = this;
            enemy.Health.Died += OnEnemyDied;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (started || !other.TryGetComponent(out PlayerController _)) return; // 태그 대신 실제 플레이어 몸통인지 확인
        started = true;

        foreach (var gate in closeOnEnter) gate.Close();
        foreach (var enemy in enemies) if (enemy) enemy.Activate();
        Entered?.Invoke(this);
        if (Remaining == 0) Clear();
    }

    void OnEnemyDied()
    {
        Remaining--;
        if (started && Remaining <= 0) Clear();
    }

    void Clear()
    {
        if (IsCleared) return;
        IsCleared = true;
        foreach (var gate in closeOnEnter) gate.Open();
        Cleared?.Invoke(this);
    }
}
