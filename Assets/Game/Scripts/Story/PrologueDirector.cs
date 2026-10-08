using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 프롤로그 진행 감독.
///  1회차: 집(대니 앞으로 온 택배 속 카세트 플레이어와 편지) → 카세트 플레이어가 뭔지 물으러 비 오는 거리로 → 친구 집(친구는 죽어 있고 조직원들이 있다)
///         → 그 자리에서 붙잡혀 총에 맞음 → 떨어진 카세트 플레이어의 되감기 버튼이 눌리고, 방금까지의 장면이 거꾸로 감긴다 → 2회차로
///  2회차: 친구 집 문 앞, 방문하기 직전으로 돌아와 있다. 맞서 싸우고 뒷창문으로 빠져나간다(되감기는 아직 못 쓴다).
///  프롤로그 끝: 뒷마당에 숨어 대니의 편지 뒷면을 읽는다 → 이때부터 되감기를 쓸 수 있고, 게임 설명(튜토리얼)이 뜨기 시작한다.
/// 회차(Loop)는 씬을 다시 불러와도 남도록 정적 값으로 둔다.
/// 컷신 카메라는 시네머신 가상 카메라의 우선순위로 전환한다(게임 카메라 10, 컷신 카메라 20).
/// </summary>
public class PrologueDirector : MonoBehaviour
{
    public static int Loop = 1;

    // 플레이 버튼을 새로 누를 때는 항상 1회차부터(씬을 다시 불러오는 회차 전환에는 영향 없음)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() => Loop = 1;

    // 인물 이름(가칭) — 자막 화자 표시에만 쓴다
    public const string Chapter1Scene = "Chapter1";
    const string Ray = "레이";
    const string Danny = "대니";
    const string Thug = "조직원";

    [Header("플레이어")]
    public PlayerController player;
    public HumanoidIK playerIK;
    public Ragdoll playerRagdoll;
    [Tooltip("주머니 속 카세트 플레이어(평소에는 숨겨져 있고, 총에 맞아 쓰러질 때 떨어진다)")]
    public Transform pocketWalkman;
    [Tooltip("오프닝 때 책상 위에 놓인 카세트 플레이어(주인공이 챙기면 사라진다)")]
    public GameObject deskWalkman;
    [Tooltip("오프닝 때 책상 위에 놓인 대니의 편지(카세트 플레이어와 함께 챙긴다)")]
    public GameObject deskLetter;
    [Tooltip("총구가 겨누는 지점(플레이어 가슴 높이의 자식)")]
    public Transform playerAimPoint;

    [Header("카메라")]
    public CinemachineBrain brain;
    public CinemachineCamera gameplayCamera;
    public ThirdPersonCamera gameplayRig;
    public CinemachineCamera[] openingShots = new CinemachineCamera[3];
    public CinemachineCamera[] revealShots = new CinemachineCamera[4];
    [Tooltip("실행 중에 자리를 정하는 컷신 카메라 두 대(번갈아 쓰며 블렌드)")]
    public CinemachineCamera dynamicA, dynamicB;

    [Header("장소")]
    public Transform homeSpawn;
    public StoryInteractable homeDoor;
    public Transform streetStart;
    public StoryInteractable friendDoor;
    public Transform friendRoomEntry;
    public Transform friendOutside;
    [Tooltip("2회차 싸움이 끝나면 열리는 친구 집 뒷창문(빠져나가는 길)")]
    public StoryInteractable friendWindow;
    [Tooltip("뒷창문으로 빠져나와 숨는 곳(대니 집 뒷마당): 여기서 편지 뒷면을 읽는다")]
    public Transform letterSpot;
    [Tooltip("이 z보다 북쪽(큰 값)이면 실내: 비가 그친다")]
    public float indoorZ = 45f;

    [Header("인물")]
    public EnemyAI[] roomThugs;
    public Ragdoll dannyBody;

    [Header("연출")]
    public RewindEffect rewindEffect;
    public AudioSource rainAudio;
    public ParticleSystem rain;
    public Light muzzleFlash;

    enum Phase { Intro, Desk, Home, Street, Reveal, Execution, Loop2, Fight, Letter, Trial, End }
    Phase phase;
    bool homeDoorUsed, friendDoorUsed, windowUsed, letterClosed;
    Vector3 deathSpot, deathToward, deathSide, deathShooter; // 1회차 총격 자리(되감기 장면 카메라를 여기에 맞춘다)
    float executionClock = float.MinValue; // 총격 장면이 시작된 기록 시각: 되감기 화면은 여기까지만 보여 준다
    float shotClock = float.MinValue;      // 총에 맞아 쓰러지기 시작한 기록 시각
    // 되감기 중 쓰러진 자세 붙잡기: 총 맞은 순간까지는 누운 몸 그대로, 그 순간을 지나면 일어서듯 기록된 자세로 섞는다
    Transform[] heldBones;
    Vector3[] heldPositions;
    Quaternion[] heldRotations;
    float holdBlend; // 0 = 누운 자세 그대로, 1 = 애니메이션(기록된) 자세
    readonly List<CinemachineCamera> cutsceneCameras = new();

    // ---------- 시작 ----------

    void Awake()
    {
        StoryState.ResetAll();
        foreach (var c in openingShots) if (c) cutsceneCameras.Add(c);
        foreach (var c in revealShots) if (c) cutsceneCameras.Add(c);
        if (dynamicA) cutsceneCameras.Add(dynamicA);
        if (dynamicB) cutsceneCameras.Add(dynamicB);
        foreach (var c in cutsceneCameras) c.Priority = 0;
        gameplayCamera.Priority = 10;
        brain.IgnoreTimeScale = true; // 슬로모션·역재생 중에도 카메라 전환은 실제 시간으로 흐른다
    }

    void Start()
    {
        homeDoor.Used += () => homeDoorUsed = true;
        friendDoor.Used += () => friendDoorUsed = true;
        if (friendWindow)
        {
            friendWindow.Used += () => windowUsed = true;
            friendWindow.gameObject.SetActive(false);
        }
        // 2회차 싸움 중 되감을 테이프 없이 쓰러지면 체크포인트 부활 대신 2회차 처음부터 다시 한다
        GameManager.PlayerRespawned += OnPlayerRespawned;
        CutsceneSkip.Ensure(); // 컷신 중 Space 길게 누르기 = 건너뛰기

        StoryState.CombatLocked = true;   // 주인공은 그저 친구에게 물어보러 가는 길이다
        StoryState.WalkmanStowed = true;  // 프롤로그 내내 카세트 플레이어는 주머니 속: 재생도 위젯도 없다
        pocketWalkman.gameObject.SetActive(false);
        var walkman = player.GetComponent<Walkman>();
        if (walkman && walkman.tapes.Count > 0)
        {
            // 레이가 집 서랍에 굴러다니던 아무 테이프나 끼워 넣은 것(대니가 보낸 건 카세트 플레이어뿐이다)
            walkman.tapes[0].title = "오래된 믹스테이프";
            walkman.tapes[0].trackTitles = new[] { "A면 1곡", "A면 2곡", "A면 3곡" };
        }
        if (muzzleFlash) muzzleFlash.enabled = false;
        if (dannyBody) StartCoroutine(SettleBody(dannyBody));

        if (rainAudio)
        {
            rainAudio.clip = ProceduralSfx.RainLoop;
            rainAudio.loop = true;
            rainAudio.Play();
        }

        StartCoroutine(Loop == 1 ? FirstLoop() : SecondLoop());
    }

    void OnDestroy() => GameManager.PlayerRespawned -= OnPlayerRespawned;

    void OnPlayerRespawned()
    {
        if (phase != Phase.Fight) return;
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameObject.scene.name); // Loop는 2로 남아 있다
    }

    void Update()
    {
        // 실내에 들어가면 비와 빗소리를 줄인다
        bool indoor = player.transform.position.z > indoorZ;
        float rainTarget = (indoor ? 0.08f : 0.45f) * GameSettings.Gain(GameSettings.Channel.Ambience); // 옵션의 환경음 크기
        if (rainAudio) rainAudio.volume = Mathf.MoveTowards(rainAudio.volume, rainTarget, Time.unscaledDeltaTime * 0.8f);
        if (rain)
        {
            var emission = rain.emission;
            emission.enabled = !indoor;
        }
    }

    // ---------- 1회차 집: 택배와 혼잣말 ----------

    [System.NonSerialized] public StoryInteractable deskParcel; // 시험이 F 대신 쓴다
    bool deskOpened;

    const string FrontLetterText =
        "레이, 갑자기 미안.\n\n" +
        "이건 평범한 카세트 플레이어가 아니야. 테이프가 돌아간 만큼… 시간을 되감을 수 있어.\n\n" +
        "누가 이걸 찾으러 오면 절대 넘기지 마. 자세한 건 만나서 얘기하자.\n\n" +
        "— 대니";

    /// <summary>책상 위 택배(편지와 카세트 플레이어) 앞에서 F를 누를 수 있는 자리</summary>
    StoryInteractable MakeDeskParcel()
    {
        var at = deskLetter ? deskLetter.transform.position : deskWalkman ? deskWalkman.transform.position : player.transform.position;
        var go = new GameObject("DeskParcel");
        go.transform.position = at;
        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1.4f;
        var it = go.AddComponent<StoryInteractable>();
        it.prompt = "택배를 열어 본다";
        it.Used += () => deskOpened = true;
        return it;
    }

    /// <summary>조작하는 동안 흐르는 혼잣말. 그 단계를 벗어나면(문을 열거나 되감기를 마치면) 멈춘다</summary>
    IEnumerator Thoughts(Phase during, params (string speaker, string text, float seconds)[] lines)
    {
        foreach (var (speaker, text, seconds) in lines)
        {
            if (phase != during && !(during == Phase.Trial && phase == Phase.Letter)) yield break;
            StoryState.Say(speaker, text, seconds);
            yield return Wait(seconds + 0.2f);
        }
    }

    /// <summary>현관으로 걸어가는 동안의 혼잣말(조작은 그대로). 문을 나서면 멈춘다</summary>
    IEnumerator HomeThoughts()
    {
        var lines = new (string speaker, string text, float seconds)[]
        {
            (Ray, "……시간을 되감는다고?", 2.4f),
            (Ray, "형사 배지 반납한 지 반년. 이런 수상한 일은 이제 내 몫이 아닌데.", 3.6f),
            ("", "(반신반의하며, 서랍에 굴러다니던 아무 테이프나 끼워 본다. 아무 일도 일어나지 않는다)", 3.6f),
            (Ray, $"{Danny} 녀석, 직접 물어봐야겠어.", 2.4f),
        };
        foreach (var (speaker, text, seconds) in lines)
        {
            if (phase != Phase.Home) yield break;
            StoryState.Say(speaker, text, seconds);
            yield return Wait(seconds + 0.2f);
        }
    }

    // ---------- 1회차 ----------

    IEnumerator FirstLoop()
    {
        StoryState.RewindLocked = true; // 되감기 능력은 아직 모른다
        StoryState.CameraDistance = 3.2f;
        player.Teleport(homeSpawn.position, homeSpawn.rotation);

        // 오프닝(짧게, 6초): 제목 → 방이 밝아지면 곧바로 조작. 택배는 레이가 직접 열어 본다(예전 34초짜리 컷신을 줄였다)
        phase = Phase.Intro;
        BeginCutscene();
        StoryState.Fade = 1f;
        StoryState.Title = "PROLOGUE";
        yield return Wait(2f);
        StoryState.Title = null;
        Shot(openingShots[0], 0f);
        yield return FadeTo(0f, 1.4f);
        StoryState.Say("", $"(새벽 1시. 낡은 브라운관 TV에서 지직거리는 소리만 흘러나온다. 책상 위에 {Danny}가 보낸 택배가 있다)", 3.4f);
        yield return Wait(2.6f);
        EndCutscene(1.2f);

        // 책상 위 택배: F로 열면 대니의 편지가 펼쳐진다(직접 읽고 F로 닫는다)
        phase = Phase.Desk;
        StoryState.Objective = "책상 위의 택배를 열어 본다";
        deskParcel = MakeDeskParcel();
        StoryState.Waypoint = deskParcel.transform;
        yield return new WaitUntil(() => deskOpened);
        StoryState.Waypoint = null;
        StoryState.ControlLocked = true;
        StoryState.LetterHeading = $"{Danny}의 편지";
        StoryState.LetterText = FrontLetterText;
        letterClosed = false;
        var interactAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/Interact", true);
        float openedAt = StoryState.Now;
        yield return new WaitUntil(() => letterClosed || (StoryState.Now - openedAt > 1f && interactAction.WasPressedThisFrame()));
        StoryState.LetterText = null;
        StoryState.LetterHeading = null;
        StoryState.ControlLocked = false;
        if (deskWalkman) deskWalkman.SetActive(false); // 카세트 플레이어와 편지를 주머니에 챙긴다
        if (deskLetter) deskLetter.SetActive(false);
        if (deskParcel) deskParcel.gameObject.SetActive(false);

        phase = Phase.Home;
        StoryState.Objective = "현관문을 열고 나간다";
        StoryState.Waypoint = homeDoor.transform;
        StartCoroutine(HomeThoughts());
        yield return new WaitUntil(() => homeDoorUsed);
        yield return DoorTransition(streetStart, 0f);

        phase = Phase.Street;
        StoryState.Objective = $"{Danny}에게 카세트 플레이어에 대해 물어보러 간다";
        StoryState.Waypoint = friendDoor.transform;
        StoryState.Say(Ray, "비까지 오네.", 2.5f);
        yield return new WaitUntil(() => friendDoorUsed);

        StoryState.ControlLocked = true;
        StoryState.Waypoint = null;
        StoryState.Say(Ray, $"{Danny}, 나야. 네가 보낸 그 카세트 플레이어 말인데—", 2.4f);
        yield return Wait(2.4f);
        StoryState.Say("", "(대답이 없다. 문이 살짝 열려 있다)", 2.4f);
        yield return Wait(2.4f);
        yield return DoorTransition(friendRoomEntry, 3.2f);
        yield return Reveal();
    }

    IEnumerator Reveal()
    {
        phase = Phase.Reveal;
        BeginCutscene();
        foreach (var thug in roomThugs)
        {
            var ik = thug.GetComponentInChildren<HumanoidIK>();
            if (ik) { ik.lookTarget = playerAimPoint; ik.look = true; }
        }
        Shot(revealShots[0], 0f);
        StoryState.Say(Ray, $"{Danny}? 문이 열려 있길래—", 2.4f);
        yield return Wait(2.4f);
        Shot(revealShots[1], 1.8f);
        StoryState.Say(Ray, "……!", 2.2f);
        yield return Wait(2.6f);
        Shot(revealShots[2], 0f);
        StoryState.Say(Thug, "…카세트 플레이어다. 저놈이 가지고 있어.", 2.8f);
        yield return Wait(2.8f);
        StoryState.Say(Thug, "잡아.", 1.4f);
        yield return Wait(1.2f);
        Shot(revealShots[3], 0f);
        StoryState.Say(Ray, "젠장…!", 1.4f);
        // 뒷걸음질 쳐 보지만 도망칠 틈도 없이 붙잡힌다(1회차에는 도망치는 조작이 없다)
        player.scriptedVelocity = -player.transform.forward * 1.5f;
        yield return Wait(0.6f);
        player.scriptedVelocity = Vector3.zero;
        StoryState.Say(Thug, "어딜.", 1.2f);
        yield return Wait(1.1f);
        yield return Execution();
    }

    // ---------- 붙잡힘 → 총격 → 되감기 ----------

    IEnumerator Execution()
    {
        if (phase == Phase.Execution) yield break;
        phase = Phase.Execution;
        BeginCutscene();
        StoryState.Objective = null;
        StoryState.Waypoint = null;

        // 가장 가까운 두 명이 앞을 막아서고, 나머지는 그 자리에 선다
        Vector3 p = player.transform.position;
        var thugs = new List<EnemyAI>();
        foreach (var t in AllThugs()) if (t.isActiveAndEnabled) { t.Deactivate(); thugs.Add(t); }
        thugs.Sort((a, b) => FlatDistance(a.transform.position, p).CompareTo(FlatDistance(b.transform.position, p)));
        Vector3 toward = thugs.Count > 0 ? Flat(thugs[0].transform.position - p) : -player.transform.forward;
        if (toward.sqrMagnitude < 0.01f) toward = -player.transform.forward;
        toward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toward);
        player.Teleport(p, Quaternion.LookRotation(toward));
        playerIK.lookTarget = null;

        EnemyAI shooter = thugs.Count > 0 ? thugs[0] : null;
        if (shooter)
        {
            shooter.PlaceAt(p + toward * 3.2f, Quaternion.LookRotation(-toward));
            var ik = shooter.GetComponentInChildren<HumanoidIK>();
            if (ik)
            {
                ik.aimTarget = playerAimPoint;
                ik.aim = true;
                if (ik.heldItem) ik.heldItem.gameObject.SetActive(true);
            }
        }
        if (thugs.Count > 1) thugs[1].PlaceAt(p + toward * 3.8f + side * 1.6f, Quaternion.LookRotation(-toward));
        Vector3 shooterPos = shooter ? shooter.transform.position : p + toward * 3.2f;
        deathSpot = p; deathToward = toward; deathSide = side; deathShooter = shooterPos;
        executionClock = TimeRewind.Clock; // 이 앞은 조직원을 제자리로 옮긴 순간이라, 거꾸로 감으면 순간이동처럼 사라져 보인다

        // 1) 총잡이 어깨 너머로 주인공
        Place(dynamicA, shooterPos + toward * 0.9f + side * 0.55f + Vector3.up * 1.7f, p + Vector3.up * 1.45f, 34f);
        Shot(dynamicA, 0f);
        StoryState.Say(Thug, "카세트 플레이어 내놔.", 2.0f);
        yield return Wait(2.0f);
        // 2) 주인공 얼굴
        Place(dynamicB, p + toward * 1.15f - side * 0.35f + Vector3.up * 1.62f, p + Vector3.up * 1.6f, 30f);
        Shot(dynamicB, 0f);
        StoryState.Say(Ray, $"{Danny}한테… 무슨 짓을 한 거야.", 2.3f);
        yield return Wait(2.3f);
        // 3) 옆에서 낮게 잡은 넓은 장면
        Place(dynamicA, p + toward * 1.6f + side * 3.4f + Vector3.up * 0.7f, p + toward * 1.6f + Vector3.up * 1.3f, 46f);
        Shot(dynamicA, 0f);
        StoryState.Say(Thug, "질문은 내가 한다.", 1.6f);
        yield return Wait(1.6f);

        // 발버둥: 총잡이에게 달려든다
        StoryState.Say(Ray, "이 자식이—!", 1f);
        player.scriptedVelocity = toward * 4.5f;
        yield return Wait(0.35f);
        player.scriptedVelocity = Vector3.zero;

        // 총성
        Vector3 muzzle = shooter ? shooterPos + Vector3.up * 1.45f - toward * 0.6f : p + toward * 2f;
        ProceduralSfx.PlayAt(ProceduralSfx.Gunshot, muzzle, 1f, 0.2f);
        StartCoroutine(MuzzleFlash(muzzle));
        StartCoroutine(Flash(Color.white, 0.55f, 0.18f));
        StartCoroutine(Shake(dynamicA, 0.25f, 0.08f));
        Time.timeScale = 0.35f;

        // 쓰러짐: 컨트롤러를 끄고 물리로 넘어간다. 재킷 주머니에서 카세트 플레이어가 빠져나와 옆으로 튕겨 나간다
        player.GetComponent<CharacterController>().enabled = false;
        shotClock = TimeRewind.Clock;
        playerRagdoll.Activate(-toward * 55f + Vector3.up * 6f, HumanBodyBones.Chest);
        pocketWalkman.gameObject.SetActive(true);
        var probe = Drop(pocketWalkman, side * 2.2f - toward * 0.4f + Vector3.up * 1.6f, ProceduralSfx.PlasticHit);
        ProceduralSfx.PlayAt(ProceduralSfx.BodyFall, p, 0.8f, 0.4f);
        StoryState.ClearLine();
        var hips = player.animator.GetBoneTransform(HumanBodyBones.Hips);

        // 4) 바닥 가까이에서 떨어지는 카세트 플레이어를 따라간다. 카메라는 늘 쓰러진 몸의 반대편에서 본다(몸에 가리지 않게)
        Shot(dynamicB, 0f);
        float until = StoryState.Now + 1.7f;
        while (StoryState.Now < until && !probe.HasHit && !StoryState.SkipRequested)
        {
            Vector3 w = pocketWalkman.position;
            Place(dynamicB, w + AwayFrom(hips.position, w, side) * 1.2f + Vector3.up * 0.5f, w, 40f);
            yield return null;
        }
        float settle = StoryState.Now + 0.6f;
        while (StoryState.Now < settle && !StoryState.SkipRequested)
        {
            Vector3 w = pocketWalkman.position;
            Place(dynamicB, w + AwayFrom(hips.position, w, side) * 0.9f + Vector3.up * 0.4f, w, 38f);
            yield return null;
        }

        // 5) 떨어진 충격에 되감기 버튼이 눌린다 — 어두운 바닥이라 작은 보조광으로 카세트 플레이어만 비춘다
        Vector3 wm = pocketWalkman.position;
        Vector3 away = AwayFrom(hips.position, wm, side);
        Place(dynamicA, wm + away * 0.32f + Vector3.up * 0.26f, wm, 30f);
        Shot(dynamicA, 0f);
        var keyLight = new GameObject("CloseupLight").AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.color = new Color(1f, 0.86f, 0.7f);
        keyLight.intensity = 0.8f; // 1.6에서는 윗면이 하얗게 날아갔다
        keyLight.range = 1.4f;
        keyLight.transform.position = wm + away * 0.25f + Vector3.up * 0.4f;
        ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, wm, 1f, 0f);
        StoryState.Say("", "(찰칵 — 떨어진 충격에, 카세트 플레이어의 되감기 버튼이 눌린다)", 2.2f);
        yield return Wait(1.6f);
        Destroy(keyLight.gameObject);
        Time.timeScale = 1f;

        yield return PlayHistoryBackwards();
    }

    IEnumerator PlayHistoryBackwards()
    {
        // 녹화된 기록으로 되돌린다: 쓰러지기 전부터 방금까지의 장면이 비디오처럼 거꾸로 감긴다.
        // 쓰러진 몸은 총 맞은 순간까지 바닥에 그대로 두고(기록에는 물리 자세가 없다), 그 순간을 지나며 일으켜 세운다
        HoldLyingPose();
        pocketWalkman.gameObject.SetActive(false);
        // 거꾸로 감기는 동안은 방 안 높은 곳에 세운 고정 카메라로 레이와 조직원을 함께 본다
        // (게임 카메라는 쓰러진 몸 뒤 바닥에 있어서, 레이 몸과 방문이 화면 앞을 가렸다)
        PlaceRewindShot(dynamicA);
        Shot(dynamicA, 0f);

        rewindEffect.StartWhir();
        rewindEffect.SetWeight(1f);
        TimeRewind.RecordNow();
        TimeRewind.PlayingBack = true;
        Time.timeScale = 0f;
        float cursor = TimeRewind.Clock;
        float end = Mathf.Max(cursor - TimeRewind.RecordedSeconds + 0.05f, executionClock + 0.2f); // 기록 사이를 섞어 보여 주므로 옮기기 직전 기록이 섞이지 않게 여유를 둔다
        float speed = 5f;
        while (cursor > end && !StoryState.SkipRequested)
        {
            cursor -= StoryState.DeltaTime * speed;
            speed = Mathf.Min(9f, speed + StoryState.DeltaTime * 2.5f); // 감을수록 빨라진다
            TimeRewind.Preview(TimeRewind.At(cursor));
            if (cursor <= shotClock && playerRagdoll.IsActive) playerRagdoll.Restore(); // 총 맞기 전으로 넘어가면 물리 몸을 뗀다
            if (heldBones != null && !playerRagdoll.IsActive)
            {
                holdBlend += StoryState.DeltaTime / 0.25f;
                if (holdBlend >= 1f) heldBones = null;
            }
            if (cursor - end < 1.2f) StoryState.Fade = Mathf.Clamp01(1f - (cursor - end) / 1.2f);
            StoryState.FadeColor = Color.white;
            yield return null;
        }
        StoryState.Fade = 1f;
        heldBones = null;
        rewindEffect.StopWhir();
        yield return Wait(0.6f);

        TimeRewind.PlayingBack = false;
        Time.timeScale = 1f;
        Loop = 2;
        SceneManager.LoadScene(gameObject.scene.name);
    }

    // ---------- 2회차 ----------

    IEnumerator SecondLoop()
    {
        phase = Phase.Loop2;
        StoryState.RewindLocked = true; // 되감기는 편지 뒷면을 읽은 뒤에야 쓸 수 있다
        // 1회차에 죽으며 되감는 데 쓴 테이프: 처음까지 감겨 있고, 규칙대로 다시는 재생되지 않는다
        var usedWalkman = player.GetComponent<Walkman>();
        if (usedWalkman && usedWalkman.tapes.Count > 0) { usedWalkman.tapes[0].rewound = true; usedWalkman.tapes[0].position = 0f; }
        player.Teleport(friendOutside.position + friendOutside.forward * 2.5f, Quaternion.LookRotation(-friendOutside.forward));

        BeginCutscene();
        StoryState.FadeColor = Color.white;
        StoryState.Fade = 1f;
        Vector3 p = player.transform.position, f = player.transform.forward, r = player.transform.right;
        Place(dynamicA, p + f * 1.2f + r * 0.3f + Vector3.up * 1.62f, p + Vector3.up * 1.6f, 32f);
        Shot(dynamicA, 0f);
        yield return FadeTo(0f, 1.1f);
        StoryState.Say(Ray, "……하아, 하아…!", 1.8f);
        yield return Wait(1.6f);
        StoryState.FadeColor = Color.black;
        EndCutscene(1.0f);
        // 나머지 혼잣말은 대니 집 문으로 걸어가는 동안(조작은 그대로)
        StartCoroutine(Thoughts(Phase.Loop2,
            (Ray, "방금… 분명히 총에 맞았는데.", 2.6f),
            ("", "(주머니 속 카세트 플레이어를 꺼내 보니, 테이프가 처음으로 되감겨 있다)", 3.2f),
            (Ray, "…편지에 쓰여 있던 게, 진짜였어?", 2.4f)));

        StoryState.CombatLocked = false; // 이번에는 맞서 싸운다
        StoryState.Objective = $"{Danny}의 집에 다시 들어간다";
        StoryState.Waypoint = friendDoor.transform;
        yield return new WaitUntil(() => friendDoorUsed);

        GameManager.Instance.SetCheckpoint(friendRoomEntry);
        yield return DoorTransition(friendRoomEntry, 3.2f);
        yield return Fight();
    }

    /// <summary>
    /// 2회차: 대니 집 안의 조직원 둘(근접 1, 총잡이 1)을 맨손으로 제압한다.
    /// 설명(튜토리얼)은 아직 띄우지 않는다 — 편지 뒷면을 읽은 뒤부터 뜬다.
    /// </summary>
    IEnumerator Fight()
    {
        phase = Phase.Fight;
        var melee = roomThugs[0];
        var gunner = roomThugs[1];
        // 튜토리얼 같은 첫 싸움이라 약하게: 근접은 한 대 5(스무 대를 버틴다), 준비 동작·공격 간격을 넉넉히
        melee.damage = 5;
        melee.attackCooldown = 1.8f;
        melee.attackRange = 1.5f;
        melee.windup = 0.75f;
        melee.Health.SetMax(60, true);
        gunner.ranged = true;
        if (gunner.aimIK && gunner.aimIK.heldItem) gunner.aimIK.heldItem.gameObject.SetActive(true); // 권총을 꺼내 들고 있다
        gunner.shotDamage = 15; // 일곱 발을 버틴다
        gunner.aimTime = 1.5f;  // 붉은 조준선을 보고 피할 시간
        gunner.shotCooldown = 3f;
        gunner.Health.SetMax(50, true);
        gunner.FirstAim = () => Tutorial.Show("aim"); // 아직 잠겨 있어 프롤로그에서는 뜨지 않는다

        BeginCutscene();
        Shot(revealShots[0], 0f);
        StoryState.Say(Ray, "…아직 안에 있군.", 2.2f);
        yield return Wait(2.2f);
        StoryState.Say(Ray, "이번엔 내가 먼저다.", 2f);
        yield return Wait(1.8f);
        EndCutscene(0.5f);
        StoryState.Objective = "조직원들을 제압한다";
        StoryState.Waypoint = null;

        // 다가가거나 먼저 때리면 알아챈다
        // (매 프레임 부르는 조건이라 LINQ 대신 직접 센다)
        bool Noticed()
        {
            foreach (var t in roomThugs) if (t.IsActive || FlatDistance(t.transform.position, player.transform.position) < 4.5f) return true;
            return false;
        }
        bool AllDown()
        {
            foreach (var t in roomThugs) if (!t.Health.IsDead) return false;
            return true;
        }
        yield return new WaitUntil(Noticed);
        StoryState.Say(Thug, "뭐야, 너 어떻게…!", 2f);
        foreach (var t in roomThugs) t.Activate();

        yield return new WaitUntil(AllDown);
        yield return new WaitForSeconds(1f);
        StoryState.Say(Ray, "하아… 하아…", 2f);
        yield return Wait(2f);
        ProceduralSfx.PlayAt(ProceduralSfx.CarDoor, friendRoomEntry.position + Vector3.back * 6f, 0.7f, 0f); // 바깥의 차 문 소리
        yield return Wait(0.35f);
        ProceduralSfx.PlayAt(ProceduralSfx.CarDoor, friendRoomEntry.position + Vector3.back * 6f, 0.6f, 0f, 0.93f);
        StoryState.Say("", "(밖에서 차 문 닫히는 소리. 더 오고 있다)", 3f);
        yield return Wait(3f);
        StoryState.Say(Ray, "…여기서 빠져나가야 해.", 2.2f);
        if (friendWindow)
        {
            friendWindow.gameObject.SetActive(true);
            StoryState.Objective = "뒷창문으로 빠져나간다";
            StoryState.Waypoint = friendWindow.transform;
            yield return new WaitUntil(() => windowUsed);
        }

        yield return LetterBack();
    }

    // ---------- 프롤로그 끝: 편지 뒷면 ----------

    const string LetterBackText =
        "레이, 앞면만 보고 넘기지 마. 이게 진짜 중요한 거야.\n\n" +
        "하나. 테이프를 넣고 그냥 들어. 노래가 한 곡 끝날 때마다 되감을 수 있는 횟수가 하나씩 쌓여. 세 번까지.\n" +
        "둘. 되감기 버튼을 누르고 있으면 시간이 거꾸로 흘러. 손을 떼면 멈춰. 딱 5초씩이야.\n" +
        "셋. 듣던 테이프로 되감으면, 그 테이프는 다시는 소리가 안 나. 아껴 써.\n" +
        "넷. 숨이 넘어가기 직전이라도, 버튼만 누를 수 있으면 돌아올 수 있어.\n" +
        "다섯. 다 쓴 테이프는 버리지 마. 바꿔 주는 가게가 있어.\n\n" +
        "그리고… 이걸 노리는 놈들한테는 절대 넘기지 마. 이런 걸 맡겨서 미안해.\n\n" +
        "— 대니";

    /// <summary>테스트용: 편지 화면을 닫는다(F와 같다)</summary>
    public void CloseLetter() => letterClosed = true;

    IEnumerator LetterBack()
    {
        phase = Phase.Letter;
        StoryState.Waypoint = null;
        StoryState.Objective = null;
        BeginCutscene();
        ProceduralSfx.PlayAt(ProceduralSfx.DoorOpen, player.transform.position, 0.8f, 0f);
        yield return FadeTo(1f, 1f);
        StoryState.Title = "PROLOGUE  END";
        yield return Wait(2.0f);
        StoryState.Title = null;

        // 뒷창문을 넘어 대니 집 뒷마당 그늘로. 되감기를 쓰게 되었을 때 방 안으로 되돌아가지 않도록 기록을 여기서 새로 시작한다
        player.Teleport(letterSpot.position, letterSpot.rotation);
        StoryState.CameraDistance = 0f;
        gameplayRig.SnapBehindTarget();
        TimeRewind.Clear();
        Vector3 p = player.transform.position, f = player.transform.forward, r = player.transform.right;
        Place(dynamicA, p + f * 1.3f + r * 0.35f + Vector3.up * 1.6f, p + Vector3.up * 1.55f, 34f);
        Shot(dynamicA, 0f);
        yield return Wait(0.3f);
        yield return FadeTo(0f, 1.2f);
        StoryState.Say(Ray, "…놈들은 이 카세트 플레이어를 노리고 있었어. 대니도 그래서…", 3.0f);
        yield return Wait(2.6f);
        Place(dynamicB, p - f * 0.55f + r * 0.45f + Vector3.up * 1.75f, p + f * 0.45f + Vector3.up * 1.1f, 42f); // 어깨 너머로 손에 든 편지
        Shot(dynamicB, 0.8f);
        StoryState.Say("", "(대니의 편지를 다시 꺼낸다. 급하게 읽느라 놓친 뒷면에도 글씨가 빼곡하다)", 2.6f);
        yield return Wait(2.4f);

        // 편지 뒷면을 펼쳐 읽는다(F로 닫는다)
        StoryState.ClearLine();
        StoryState.SkipRequested = false; // 앞 장면의 건너뛰기가 편지까지 넘기지 않게: 편지는 따로 길게 눌러야 넘어간다
        StoryState.LetterHeading = "편지 뒷면";
        StoryState.LetterText = LetterBackText;
        letterClosed = false;
        var interact = UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/Interact", true);
        float opened = StoryState.Now;
        yield return new WaitUntil(() => letterClosed || StoryState.SkipRequested || (StoryState.Now - opened > 1f && interact.WasPressedThisFrame()));
        StoryState.LetterText = null;
        StoryState.LetterHeading = null;

        // 편지에는 5초씩이라고 적혀 있는데, 1회차에는 1분 넘게 돌아갔다(살고 싶다는 간절함과 복수심 때문 — 레이는 아직 모른다).
        // 이 혼잣말은 조작을 돌려준 뒤에 흐른다(되감기 연습을 하면서 듣는다)
        var trialThoughts = StartCoroutine(Thoughts(Phase.Trial,
            (Ray, "……진작 끝까지 읽었어야 했는데.", 2.4f),
            (Ray, "…그런데 딱 5초씩이라고? 아까는 대니 집 문 앞까지 통째로 돌아왔는데.", 3.6f),
            (Ray, "……일단 직접 써 보자.", 2.2f)));

        // 이제부터 되감기를 쓸 수 있다: 카세트 플레이어를 꺼내 쓰고(왼쪽 아래 위젯), 게임 설명이 뜨기 시작한다
        StoryState.RewindLocked = false;
        StoryState.WalkmanStowed = false;
        var walkman = player.GetComponent<Walkman>();
        if (walkman && walkman.tapes.Count > 0) walkman.tapes[0].charges = Mathf.Max(1, walkman.tapes[0].charges);
        Tutorial.Unlock();
        EndCutscene(1.2f);
        Tutorial.Show("rewind");
        Tutorial.Show("combat");

        // 처음으로 직접 되감아 보면 끝난다. 연습이라 되감기 횟수는 쓰지 않는다
        phase = Phase.Trial;
        StoryState.FreeRewind = true;
        StoryState.Objective = "편지에 적힌 대로 되감기를 써 본다";
        if (walkman)
        {
            yield return new WaitUntil(() => walkman.Rewinding);
            yield return new WaitUntil(() => !walkman.Rewinding);
        }
        StoryState.FreeRewind = false;
        StoryState.Objective = null;
        if (trialThoughts != null) StopCoroutine(trialThoughts); // 되감기를 빨리 해 보면 남은 혼잣말은 건너뛴다(다음 대사와 겹치지 않게)
        StoryState.ClearLine();
        yield return Wait(0.6f);
        StoryState.Say(Ray, "…정말 딱 5초야.", 2.4f);
        yield return Wait(2.6f);
        StoryState.Say(Ray, "그럼 아까 그건… 대체 뭐였지?", 3f);
        yield return Wait(3.4f);

        phase = Phase.End;
        BeginCutscene();
        yield return FadeTo(1f, 1.4f);
        // 프롤로그 끝 → 1장(대니 집 뒷마당에서 그대로 이어진다)
        if (Application.CanStreamedLevelBeLoaded(Chapter1Scene))
        {
            yield return StoryState.Sleep(0.8f);
            SceneManager.LoadScene(Chapter1Scene);
        }
        else StoryState.Say("", "(이어서 계속)", 30f);
    }

    // ---------- 연출 도구 ----------

    void BeginCutscene()
    {
        StoryState.CutsceneActive = true;
        player.scriptedVelocity = Vector3.zero;
    }

    void EndCutscene(float blend)
    {
        gameplayRig.SnapBehindTarget();
        Shot(gameplayCamera, blend);
        StoryState.CutsceneActive = false;
        StoryState.ControlLocked = false;
        StoryState.SkipRequested = false; // 건너뛰기는 컷신 하나에만
    }

    /// <summary>해당 카메라로 전환한다. blend가 0이면 바로 컷, 아니면 그 시간 동안 부드럽게 옮겨 간다.</summary>
    void Shot(CinemachineCamera cam, float blend)
    {
        brain.DefaultBlend = blend <= 0f
            ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
            : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blend);
        foreach (var c in cutsceneCameras) c.Priority = c == cam ? 20 : 0;
        gameplayCamera.Priority = cam == gameplayCamera ? 20 : 10;
    }

    /// <summary>
    /// 컷신 카메라를 놓는다. 바라보는 지점에서 카메라 쪽으로 레이를 쏴서 벽·건물에 막히면 그 앞으로 당긴다
    /// (좁은 골목에서 카메라가 벽 속에 들어가 화면이 까매지는 것을 막는다). 인물·래그돌은 막힘으로 치지 않는다.
    /// </summary>
    void HoldLyingPose()
    {
        var root = player.animator.transform;
        var all = root.GetComponentsInChildren<Transform>();
        heldBones = new Transform[all.Length - 1];
        heldPositions = new Vector3[heldBones.Length];
        heldRotations = new Quaternion[heldBones.Length];
        for (int i = 1, j = 0; i < all.Length; i++, j++) // 0번은 모델 자신(방향은 기록대로 둔다)
        {
            heldBones[j] = all[i];
            heldPositions[j] = all[i].localPosition;
            heldRotations[j] = all[i].localRotation;
        }
        holdBlend = 0f;
    }

    void LateUpdate()
    {
        // 애니메이터가 자세를 계산한 뒤에 덮어써야 하므로 LateUpdate에서 한다
        if (heldBones == null) return;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(holdBlend));
        for (int i = 0; i < heldBones.Length; i++)
        {
            var b = heldBones[i];
            if (!b) continue;
            b.localPosition = Vector3.Lerp(heldPositions[i], b.localPosition, k);
            b.localRotation = Quaternion.Slerp(heldRotations[i], b.localRotation, k);
        }
    }

    /// <summary>
    /// 되감기 장면용 카메라 자리: 총격 자리를 둘러싼 여러 후보 중, 벽·가구 안이 아니고
    /// 레이(발·가슴)와 총잡이까지 가리는 것이 없는 곳을 고른다. 옆에서 보는 각도를 조금 더 좋아한다.
    /// </summary>
    void PlaceRewindShot(CinemachineCamera cam)
    {
        Vector3 focus = deathSpot + deathToward * 1.4f + Vector3.up * 1.0f;
        var targets = new[] { deathSpot + Vector3.up * 0.3f, deathSpot + Vector3.up * 1.4f, deathShooter + Vector3.up * 1.4f };
        Vector3 best = focus + deathSide * 3.4f + Vector3.up * 1.0f;
        float bestScore = float.MinValue;
        foreach (float height in new[] { 2.2f, 1.9f })
            foreach (float radius in new[] { 3.0f, 3.6f, 4.2f })
                for (int i = 0; i < 24; i++)
                {
                    Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * deathSide;
                    Vector3 c = deathSpot + deathToward * 1.4f + dir * radius + Vector3.up * height;
                    if (Physics.CheckSphere(c, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    int clear = 0;
                    foreach (var t in targets) if (ClearLine(c, t)) clear++;
                    float score = clear * 10f + Mathf.Abs(Vector3.Dot(dir, deathSide)) * 2f + height - Mathf.Abs(radius - 3.6f);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
        Place(cam, best, focus, 52f);
    }

    static readonly RaycastHit[] castHits = new RaycastHit[32]; // 떨어지는 워크맨을 따라가는 동안 매 프레임 쓴다

    /// <summary>두 점 사이를 벽·문·가구가 막는지(사람 몸은 가림으로 치지 않는다)</summary>
    static bool ClearLine(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float length = d.magnitude;
        int n = Physics.RaycastNonAlloc(from, d / length, castHits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!castHits[i].rigidbody && !castHits[i].collider.GetComponentInParent<Health>()) return false;
        return true;
    }

    static void Place(CinemachineCamera cam, Vector3 position, Vector3 lookAt, float fov)
    {
        Vector3 dir = position - lookAt;
        float wanted = dir.magnitude;
        if (wanted > 0.01f)
        {
            float allowed = wanted;
            int n = Physics.SphereCastNonAlloc(lookAt, 0.12f, dir / wanted, castHits, wanted, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = castHits[i];
                if (hit.distance <= 0f || hit.rigidbody || hit.collider.GetComponentInParent<Health>()) continue;
                allowed = Mathf.Min(allowed, hit.distance - 0.1f);
            }
            position = lookAt + dir / wanted * Mathf.Max(0.3f, allowed);
        }
        cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
        cam.Lens.FieldOfView = fov;
    }

    IEnumerator DoorTransition(Transform to, float cameraDistance, System.Action midpoint = null)
    {
        StoryState.ControlLocked = true;
        ProceduralSfx.PlayAt(ProceduralSfx.DoorOpen, player.transform.position, 0.9f, 0f);
        yield return FadeTo(1f, 0.35f);
        player.Teleport(to.position, to.rotation);
        StoryState.CameraDistance = cameraDistance;
        gameplayRig.SnapBehindTarget();
        gameplayCamera.PreviousStateIsValid = false;
        midpoint?.Invoke();
        yield return Wait(0.3f);
        yield return FadeTo(0f, 0.45f);
        StoryState.ControlLocked = false;
    }

    IEnumerator FadeTo(float target, float seconds)
    {
        float start = StoryState.Fade, t = 0f;
        while (t < seconds && !StoryState.SkipRequested)
        {
            t += StoryState.DeltaTime;
            StoryState.Fade = Mathf.Lerp(start, target, t / seconds);
            yield return null;
        }
        StoryState.Fade = target;
    }

    IEnumerator Flash(Color color, float peak, float seconds)
    {
        var previous = StoryState.FadeColor;
        StoryState.FadeColor = color;
        float t = 0f;
        while (t < seconds)
        {
            t += StoryState.DeltaTime;
            StoryState.Fade = peak * (1f - t / seconds);
            yield return null;
        }
        StoryState.Fade = 0f;
        StoryState.FadeColor = previous;
    }

    IEnumerator MuzzleFlash(Vector3 at)
    {
        if (!muzzleFlash) yield break;
        muzzleFlash.transform.position = at;
        muzzleFlash.enabled = true;
        yield return StoryState.Sleep(0.07f);
        muzzleFlash.enabled = false;
    }

    static IEnumerator Shake(CinemachineCamera cam, float seconds, float amplitude)
    {
        Vector3 origin = cam.transform.position;
        float t = 0f;
        while (t < seconds)
        {
            t += StoryState.DeltaTime;
            cam.transform.position = origin + Random.insideUnitSphere * amplitude * (1f - t / seconds);
            yield return null;
        }
        cam.transform.position = origin;
    }

    static ImpactProbe Drop(Transform item, Vector3 velocity, AudioClip sound)
    {
        item.SetParent(null, true);
        if (!item.TryGetComponent(out Collider _))
        {
            var box = item.gameObject.AddComponent<BoxCollider>();
            var r = item.GetComponentInChildren<Renderer>();
            if (r)
            {
                box.center = item.InverseTransformPoint(r.bounds.center);
                box.size = item.InverseTransformVector(r.bounds.size);
                box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));
            }
        }
        var rb = item.gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 6f;
        var probe = item.gameObject.AddComponent<ImpactProbe>();
        probe.sound = sound;
        return probe;
    }

    static IEnumerator SettleBody(Ragdoll body)
    {
        // 쓰러진 친구: 시작하자마자 물리로 쓰러뜨리고, 멈추면 고정해 둔다
        yield return null;
        body.Activate(body.transform.forward * -25f, HumanBodyBones.Chest);
        float until = Time.time + 6f;
        yield return new WaitForSeconds(1.5f);
        while (Time.time < until && body.MaxSpeed() > 0.05f) yield return null;
        body.Freeze();
    }

    IEnumerable<EnemyAI> AllThugs()
    {
        foreach (var t in roomThugs) if (t) yield return t;
    }

    /// <summary>쓰러진 몸에서 물건 쪽으로 향하는 수평 방향(카메라를 몸 반대편에 둘 때). 둘이 겹치면 fallback 방향</summary>
    static Vector3 AwayFrom(Vector3 body, Vector3 item, Vector3 fallback)
    {
        Vector3 d = Flat(item - body);
        return d.sqrMagnitude > 0.01f ? d.normalized : fallback.normalized;
    }

    /// <summary>실제 시간으로 기다린다. 컷신 건너뛰기가 켜지면 바로 끝난다</summary>
    static IEnumerator Wait(float seconds)
    {
        // 이야기 시계로 잰다: 메뉴(일시정지·설명 팝업·인벤토리·가게)를 연 동안은 대사도 멈춘다
        float until = StoryState.Now + seconds;
        while (StoryState.Now < until && !StoryState.SkipRequested) yield return null;
    }
    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    static float FlatDistance(Vector3 a, Vector3 b) => Flat(a - b).magnitude;
}
