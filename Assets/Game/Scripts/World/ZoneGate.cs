using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// 열리면 땅속으로 가라앉는 차단벽. GameManager.progression에 등록된 게이트는
/// 필요한 구역이 모두 클리어되면 자동으로 열린다.
/// </summary>
public class ZoneGate : MonoBehaviour, IRewindable
{
    public bool startOpen;
    public float sinkDepth = 5f;
    public float speed = 6f;

    [Header("진행")]
    public CombatZone[] requiredZones = Array.Empty<CombatZone>();
    public string objective;
    public string openMessage;

    public bool IsOpen => open;
    public int ClearedCount => requiredZones.Count(z => z.IsCleared);
    public bool RequirementsMet => ClearedCount == requiredZones.Length;

    Vector3 closedPosition;
    bool open;

    void Awake()
    {
        closedPosition = transform.position;
        open = startOpen;
        if (open) transform.position = OpenPosition;
        TimeRewind.Register(this);
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    public object CaptureState() => (open, transform.position);

    public void RestoreState(object state, bool preview) => (open, transform.position) = ((bool, Vector3))state;

    Vector3 OpenPosition => closedPosition + Vector3.down * sinkDepth;

    public void Open() => open = true;
    public void Close() => open = false;

    void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, open ? OpenPosition : closedPosition, speed * Time.deltaTime);
    }
}
