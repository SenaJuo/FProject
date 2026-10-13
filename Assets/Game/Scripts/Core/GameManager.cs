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
    public bool MessageVisible => StoryState.Now < messageUntil; // 이야기 시계: 일시정지 중에는 사라지지 않는다
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
        // 계단 현상 줄이기(2026-10-13): SMAA는 멈춘 화면의 모서리만 다듬어서, 움직일 때 계단 끝·타일 줄이 여전히 반짝였다
        // → TAA(앞 프레임과 섞어 깜빡임을 없앤다)
        var cam = Camera.main;
        if (cam && cam.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data))
        {
            data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing;
            data.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            var taa = data.taaSettings;
            taa.quality = UnityEngine.Rendering.Universal.TemporalAAQuality.High;
            data.taaSettings = taa;
        }
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    // 저장 지점은 되감지 않는다: "저장됨"을 본 뒤에 되감아도 저장은 그대로 남는다
    public object CaptureState() => (CurrentZone, StageCleared);

    public void RestoreState(object state, bool preview)
    {
        var s = ((CombatZone zone, bool cleared))state;
        CurrentZone = s.zone;
        StageCleared = s.cleared;
        if (!s.cleared && Message == "STAGE CLEAR") messageUntil = 0f; // 마지막 적을 쓰러뜨리기 전으로 되감으면 문구도 지운다
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

    Coroutine pendingRespawn;

    public void RespawnFromCheckpoint()
    {
        if (pendingRespawn == null) pendingRespawn = StartCoroutine(RespawnAfterDelay()); // 이미 기다리는 중이면 두 번 부활하지 않는다
    }

    void Update()
    {
        if (player.transform.position.y < killHeight) RespawnPlayer();
    }

    public void ShowMessage(string message, float duration)
    {
        Message = message;
        messageUntil = StoryState.Now + duration;
    }

    /// <summary>오른쪽 아래의 작은 알림(테이프 획득 등). 화면 가운데 큰 글자는 장소 이름 같은 큰 일에만 쓴다</summary>
    public void Notify(string text, float duration = 2.6f)
    {
        notices.RemoveAll(n => n.text == text); // 같은 알림은 새로 띄운다
        notices.Add((text, StoryState.Now, StoryState.Now + duration));
        if (notices.Count > 4) notices.RemoveAt(0);
    }

    /// <summary>떠 있는 작은 알림(글자, 뜬 시각, 사라질 시각) — 오래된 것부터</summary>
    public System.Collections.Generic.IReadOnlyList<(string text, float from, float until)> Notices
    {
        get { notices.RemoveAll(n => StoryState.Now >= n.until); return notices; }
    }
    readonly System.Collections.Generic.List<(string text, float from, float until)> notices = new();

    /// <summary>마지막으로 저장한 시각(이야기 시계). HUD가 잠깐 "저장됨"을 띄운다</summary>
    public float SavedAt { get; private set; } = -99f;

    /// <summary>저장 지점(가게·구역 시작점): 되감을 횟수 없이 쓰러지면 여기서 다시 시작한다</summary>
    public void Save(Transform point)
    {
        SetCheckpoint(point);
        SavedAt = StoryState.Now;
        // 저장 지점에 가면 체력도 최대까지 회복한다
        if (player && player.TryGetComponent(out Health h) && !h.IsDead) LastSaveHealed = h.Heal(h.Max) > 0;
        Notify(LastSaveHealed ? "저장됨 · 체력 회복" : "저장됨");
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
        pendingRespawn = null;
        RespawnPlayer();
    }

    /// <summary>기다리지 않고 바로 저장 지점에서 다시 시작한다(화면이 어두운 동안 옮길 때)</summary>
    public void RespawnNow() => RespawnPlayer();

    void RespawnPlayer()
    {
        // 떨어져서 곧바로 다시 시작하면 기다리던 부활은 취소하고, 사망 화면·되감기도 거둔다(남아 있으면 R을 누를 때 게임이 멈췄다)
        if (pendingRespawn != null) { StopCoroutine(pendingRespawn); pendingRespawn = null; }
        if (player.TryGetComponent(out Walkman walkman)) walkman.ResetForRespawn();
        player.Respawn(checkpointPosition, checkpointRotation);
        PlayerRespawned?.Invoke();
        TimeRewind.Clear(); // 이번 죽음 이전의 기록으로는 되감을 수 없다
    }
}
