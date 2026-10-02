using System;
using System.Collections;
using UnityEngine;

public class GameManager : MonoBehaviour, IRewindable
{
    public static GameManager Instance { get; private set; }
    public static event Action PlayerRespawned;

    public PlayerController player;
    public string stageTitle = "STAGE 01";
    public float respawnDelay = 2.5f;
    public float killHeight = -30f;

    [Header("진행")]
    [Tooltip("플레이어가 만나는 순서대로 놓은 관문")]
    public ZoneGate[] progression = Array.Empty<ZoneGate>();
    public CombatZone finalZone;
    public string finalObjective;

    public string Message { get; private set; }
    public bool MessageVisible => Time.unscaledTime < messageUntil;
    public CombatZone CurrentZone { get; private set; }
    public bool StageCleared { get; private set; }
    public ZoneGate CurrentGate => Array.Find(progression, g => !g.IsOpen);

    Vector3 checkpointPosition;
    Quaternion checkpointRotation;
    float messageUntil;

    void Awake()
    {
        Instance = this;
        checkpointPosition = player.transform.position;
        checkpointRotation = player.transform.rotation;
        TimeRewind.Register(this);
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    public object CaptureState() => (checkpointPosition, checkpointRotation, CurrentZone, StageCleared);

    public void RestoreState(object state, bool preview)
    {
        var s = ((Vector3 position, Quaternion rotation, CombatZone zone, bool cleared))state;
        checkpointPosition = s.position;
        checkpointRotation = s.rotation;
        CurrentZone = s.zone;
        StageCleared = s.cleared;
    }

    void OnEnable()
    {
        CombatZone.Entered += OnZoneEntered;
        CombatZone.Cleared += OnZoneCleared;
    }

    void OnDisable()
    {
        CombatZone.Entered -= OnZoneEntered;
        CombatZone.Cleared -= OnZoneCleared;
    }

    void Start()
    {
        player.GetComponent<Health>().Died += OnPlayerDied;
        ShowMessage(stageTitle, 3f);
    }

    void OnPlayerDied()
    {
        // Walkman에 충전된 테이프가 있으면 되감기를 제안하고, 없으면 마지막 세이브 지점으로 돌아간다.
        if (!StoryState.RewindLocked && player.TryGetComponent(out Walkman walkman) && walkman.OfferRewind()) return;
        RespawnFromCheckpoint();
    }

    public void RespawnFromCheckpoint() => StartCoroutine(RespawnAfterDelay());

    void Update()
    {
        if (player.transform.position.y < killHeight) RespawnPlayer();
    }

    public void ShowMessage(string message, float duration)
    {
        Message = message;
        messageUntil = Time.unscaledTime + duration;
    }

    /// <summary>마지막으로 저장한 시각(실제 시간). HUD가 잠깐 "저장됨"을 띄운다</summary>
    public float SavedAt { get; private set; } = -99f;

    /// <summary>저장 지점(가게·구역 시작점): 되감을 횟수 없이 쓰러지면 여기서 다시 시작한다</summary>
    public void Save(Transform point)
    {
        SetCheckpoint(point);
        SavedAt = Time.unscaledTime;
        // 저장 지점에 가면 체력도 최대까지 회복한다
        if (player && player.TryGetComponent(out Health h) && !h.IsDead) LastSaveHealed = h.Heal(h.Max) > 0;
    }

    /// <summary>마지막 저장 때 체력이 실제로 찼는가(HUD가 "저장됨 · 체력 회복"을 띄운다)</summary>
    public bool LastSaveHealed { get; private set; }

    public void SetCheckpoint(Transform point)
    {
        if (point == null) return;
        checkpointPosition = point.position;
        checkpointRotation = point.rotation;
    }

    void OnZoneEntered(CombatZone zone)
    {
        CurrentZone = zone;
        if (zone.saveOnEnter)
        {
            if (zone.checkpoint) SetCheckpoint(zone.checkpoint);
            else SetCheckpoint(player.transform);
        }
        if (!string.IsNullOrEmpty(zone.zoneName)) ShowMessage(zone.zoneName, 2.5f);
    }

    void OnZoneCleared(CombatZone zone)
    {
        if (zone.isFinal)
        {
            StageCleared = true;
            ShowMessage("STAGE CLEAR", float.MaxValue);
            return;
        }

        var gate = CurrentGate;
        if (gate && gate.RequirementsMet)
        {
            gate.Open();
            ShowMessage(gate.openMessage, 3f);
        }
        else
        {
            ShowMessage("적 무리 소탕", 2f);
        }
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        RespawnPlayer();
    }

    void RespawnPlayer()
    {
        player.Respawn(checkpointPosition, checkpointRotation);
        PlayerRespawned?.Invoke();
        TimeRewind.Clear(); // 이번 죽음 이전의 기록으로는 되감을 수 없다
    }
}
