using System.Collections.Generic;
using UnityEngine;

/// <summary>Walkman이 시간을 되감을 때 상태가 되돌아가는 모든 대상.</summary>
public interface IRewindable
{
    object CaptureState();

    /// <param name="preview">
    /// 월드가 거꾸로 재생되는 동안은 true이다(가벼운 시각 갱신만 한다).
    /// 되감기가 멈추는 순간의 최종 복원에서는 false이다.
    /// </param>
    void RestoreState(object state, bool preview);
}

public sealed class WorldSnapshot
{
    /// <summary>이 스냅샷이 기록된 시점의 게임플레이 시계(일시정지 시간을 뺀 실제 초).</summary>
    public float Time;
    /// <summary>이 스냅샷이 기록된 시점의 벽시계 시간. 되감기를 실제 시간과 대조해 검증하는 데 쓴다.</summary>
    public float RealTime;
    public readonly Dictionary<IRewindable, object> States = new();
}

/// <summary>
/// 되감기 가능한 상태의 최근 몇 초를 계속 덮어쓰며 기록한다. 되감기 중에는 VCR처럼 거꾸로
/// 재생되고, 테이프가 멈춘 지점에서 정확히 복원된다. 시계는 실제(unscaled) 시간으로 흐르므로 히트스톱
/// 슬로모션이 "5초" 되감기를 짧게 만들지 않는다. 일시정지 시간(메뉴, 사망 화면)은 기록하지 않는다.
/// Inventory는 일부러 등록하지 않는다.
/// </summary>
public static class TimeRewind
{
    public const float RecordInterval = 0.05f;
    public const float HistoryLength = 16f;

    static readonly List<IRewindable> registered = new();
    static readonly List<WorldSnapshot> history = new();
    static readonly Stack<WorldSnapshot> pool = new(); // 지난 스냅샷을 다시 써서 초당 20번의 할당을 없앤다
    static float sinceLastRecord;

    /// <summary>되감기·기록 지우기마다 1씩 는다. 진행 중이던 동작이 "그 사이 시간이 바뀌었는지" 알아보는 데 쓴다</summary>
    public static int Generation { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart()
    {
        // 도메인 다시 읽기를 끈 설정에서도 지난 플레이의 기록·되감기 중 표시가 남지 않게
        registered.Clear();
        history.Clear();
        pool.Clear();
        Clock = sinceLastRecord = 0f;
        PlayingBack = false;
        LastLanded = null;
    }

    public static float Clock { get; private set; }
    public static bool PlayingBack { get; set; }
    public static float RecordedSeconds => history.Count > 0 ? Clock - history[0].Time : 0f;
    /// <summary>마지막 되감기가 도달한 스냅샷.</summary>
    public static WorldSnapshot LastLanded { get; private set; }

    public static void Register(IRewindable rewindable) => registered.Add(rewindable);
    public static void Unregister(IRewindable rewindable) => registered.Remove(rewindable);

    /// <summary>
    /// 실제 경과 시간만큼 시계를 진행하고 기록할 때가 되면 기록한다. 게임플레이 프레임마다 한 번 호출한다. 클램프하지 않는다:
    /// 프레임이 끊기더라도 "5초 전"은 실제 5초를 뜻해야 한다.
    /// </summary>
    public static void Record(float unscaledDeltaTime)
    {
        Clock += unscaledDeltaTime;
        sinceLastRecord += unscaledDeltaTime;
        if (history.Count > 0 && sinceLastRecord < RecordInterval) return;
        RecordNow();
    }

    public static void RecordNow()
    {
        sinceLastRecord = 0f;
        var snapshot = pool.Count > 0 ? pool.Pop() : new WorldSnapshot();
        snapshot.Time = Clock;
        snapshot.RealTime = UnityEngine.Time.realtimeSinceStartup;
        foreach (var r in registered) snapshot.States[r] = r.CaptureState();
        history.Add(snapshot);
        int expired = 0;
        while (expired < history.Count - 1 && Clock - history[expired].Time > HistoryLength) expired++;
        if (expired == 0) return;
        for (int i = 0; i < expired; i++) Recycle(history[i]);
        history.RemoveRange(0, expired);
    }

    /// <summary><paramref name="time"/> 시점 또는 그 이전의 가장 최근 스냅샷. 기록이 그보다 짧으면 가장 오래된 스냅샷이다.</summary>
    public static WorldSnapshot At(float time)
    {
        for (int i = history.Count - 1; i >= 0; i--)
            if (history[i].Time <= time) return history[i];
        return history.Count > 0 ? history[0] : null;
    }

    public static void Preview(WorldSnapshot snapshot) => Apply(snapshot, true);

    /// <summary>되감기가 끝났다: (도착한 기록, 건너뛴 기록들 — 오래된 순). 건너뛴 기록은 이 호출 뒤 버려지므로 필요한 것은 그 자리에서 옮겨 둔다</summary>
    public static event System.Action<WorldSnapshot, List<WorldSnapshot>> Rewound;
    static readonly List<WorldSnapshot> skippedBuffer = new();

    public static void RewindTo(WorldSnapshot snapshot)
    {
        if (snapshot != null && Rewound != null)
        {
            skippedBuffer.Clear();
            foreach (var h in history) if (h.Time > snapshot.Time) skippedBuffer.Add(h);
            Rewound(snapshot, skippedBuffer);
            skippedBuffer.Clear();
        }
        Apply(snapshot, false);
        while (history.Count > 0 && history[^1].Time > snapshot.Time)
        {
            Recycle(history[^1]);
            history.RemoveAt(history.Count - 1);
        }
        Clock = snapshot.Time;
        Generation++;
        sinceLastRecord = 0f;
        LastLanded = snapshot;
    }

    public static void Clear()
    {
        foreach (var h in history) Recycle(h);
        history.Clear();
        sinceLastRecord = 0f;
        Generation++;
    }

    static void Recycle(WorldSnapshot snapshot)
    {
        if (snapshot == LastLanded) return; // 밖에서 아직 들여다볼 수 있다
        snapshot.States.Clear();
        pool.Push(snapshot);
    }

    static void Apply(WorldSnapshot snapshot, bool preview)
    {
        if (snapshot == null) return; // 기록이 비어 있으면(다시 시작 직후) 되돌릴 것이 없다
        foreach (var pair in snapshot.States)
            if (pair.Key as Object != null) pair.Key.RestoreState(pair.Value, preview);
    }
}
