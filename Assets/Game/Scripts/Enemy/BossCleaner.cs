using UnityEngine;

/// <summary>
/// 정리반 반장(전직 형사, 레이의 옛 선배) — 3번 부두 앞 보스. EnemyAI와 함께 붙는다.
///  1단계(체력 100~60%): 진압봉 맞싸움. 두 번째 주먹마다 붉은 강타 — 완벽 회피 뒤 반격으로 깎는다.
///  2단계(60~25%): 드럼통 뒤로 물러나 권총을 쏘고, 숨겨 둔 부하 둘을 부른다. 부하가 쓰러지거나 15초가 지나면 다시 맞싸움.
///  3단계(25% 아래): 감독(Chapter1Director)이 넘겨받는다 — 대니의 테이프를 불에 던지러 달려간다(되감기 연출).
/// 되감으면 단계도 되감는다(체력 기준으로 다시 정해진다).
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public class BossCleaner : MonoBehaviour, IRewindable
{
    public EnemyAI[] reinforcements = System.Array.Empty<EnemyAI>();
    [Tooltip("2단계에서 숨어 쏘는 자리(드럼통 뒤)")]
    public Transform coverPoint;
    [Tooltip("손에 든 대니의 테이프(3단계에서 불에 던진다)")]
    public Transform heldTape;
    public float phase2At = 0.6f, phase3At = 0.25f;

    public int Phase { get; private set; } = 1;
    /// <summary>3단계에 들어섰다(감독이 넘겨받는다)</summary>
    public bool WantsToBurn => Phase >= 3;
    public static event System.Action<BossCleaner, int> PhaseChanged;

    EnemyAI ai;
    Health health;
    float phase2Started;

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        health = GetComponent<Health>();
        TimeRewind.Register(this);
    }

    void OnDestroy() => TimeRewind.Unregister(this);

    public object CaptureState() => Phase;

    public void RestoreState(object state, bool preview)
    {
        if (preview) return;
        int p = (int)state;
        // 테이프를 태우러 달리기 시작한 뒤(3단계)로는 되돌리지 않는다: 그 되감기 자체가 연출의 일부다
        if (Phase >= 3 || p == Phase) return;
        Phase = p;
        Apply(false);
    }

    void Update()
    {
        if (health.IsDead || Phase >= 3 || !ai.IsActive) return;
        float ratio = health.Current / (float)health.Max;
        if (Phase == 1 && ratio <= phase2At) { Phase = 2; phase2Started = Time.time; Apply(true); PhaseChanged?.Invoke(this, 2); }
        else if (Phase == 2)
        {
            bool helpersDown = true;
            foreach (var r in reinforcements) if (r && r.gameObject.activeInHierarchy && !r.GetComponent<Health>().IsDead) helpersDown = false;
            if (ratio <= phase3At) { Phase = 3; Apply(true); PhaseChanged?.Invoke(this, 3); }
            else if (helpersDown && Time.time - phase2Started > 4f || Time.time - phase2Started > 15f) { Phase = 1; Apply(true); }
        }
        else if (Phase == 1 && ratio <= phase3At) { Phase = 3; Apply(true); PhaseChanged?.Invoke(this, 3); }
    }

    /// <summary>단계에 맞게 싸움 방식을 바꾼다. fresh면 처음 들어선 것(외치고 숨는 곳으로 간다), 아니면 되감기로 돌아온 것</summary>
    void Apply(bool fresh)
    {
        bool gun = Phase == 2;
        ai.ranged = gun;
        ai.holdPosition = gun;
        if (ai.aimIK && ai.aimIK.heldItem) ai.aimIK.heldItem.gameObject.SetActive(gun);
        if (!gun && ai.aimIK) ai.aimIK.aim = false;
        if (gun && fresh)
        {
            if (coverPoint) ai.PlaceAt(coverPoint.position, coverPoint.rotation);
            StoryState.Say("정리반 반장", "애들아! 나와!", 1.6f);
            // 숨어 있던 부하: 꺼져 있던 동안은 체력이 아직 0(Awake 전)이라 '쓰러짐'으로 읽힌다 → 켜져 있지 않으면 먼저 켠다
            foreach (var r in reinforcements)
            {
                if (!r) continue;
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                if (!r.Health.IsDead) r.Activate();
            }
        }
        if (Phase >= 3) ai.Deactivate();
    }
}
