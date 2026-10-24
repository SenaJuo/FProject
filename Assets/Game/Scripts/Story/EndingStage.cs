using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 엔딩 장면(2026-10-23: 예전에는 검은 화면에 자막만 흘렀다). 5장에서 고른 뒤 이미 있는 장면을 무대로 불러와 찍는다.
///  · 엔딩 A 「다시, A면」 — 프롤로그 장면: 일주일 전 새벽 1시, 레이의 방에서 대니의 전화 → 대니 집까지 달린다 → 현관에 살아 있는 대니.
///  · 엔딩 B 「끝난 테이프」 — 1장 장면: 3번 부두 끝, 1장에서 대니의 녹음을 들었던 자리. 새벽 뉴스 → 라디오를 끄고 믹스테이프.
/// 레이의 마지막 대사가 끝나면 화면이 천천히 검게 잠기고 타이틀로 돌아간다(크레딧 없음 — 사용자 요청).
/// 그 장면의 원래 감독은 Start 전에 꺼 둔다(진행이 시작되지 않는다). 적·전투 구역·상호작용도 모두 치운다.
/// Space 길게 누르기로 건너뛰면 곧바로 어두워진다.
/// </summary>
public class EndingStage : MonoBehaviour
{
    public enum Kind { None, Rewind, Break }

    const string Ray = "레이", Danny = "대니";
    static Kind pending;
    /// <summary>시험용: 지금 찍고 있는 엔딩 · 끝까지 갔는가 · 지금 장면 이름</summary>
    public static Kind Playing { get; private set; }
    public static bool Finished { get; private set; }
    public static string Beat { get; private set; } = "";

    Kind kind;
    PlayerController player;
    CinemachineBrain brain;
    CinemachineCamera camA, camB;
    readonly List<CinemachineCamera> allCams = new();
    AudioSource music, radio;
    float musicLevel, radioLevel;
    Coroutine sequence;
    bool finishing;

    /// <summary>5장에서 부른다: 엔딩 무대 장면을 불러온다</summary>
    public static void Play(Kind which)
    {
        pending = which;
        Playing = which;
        Finished = false;
        Beat = "load";
        SceneLoader.Load(which == Kind.Rewind ? "Prologue" : "Chapter1", which == Kind.Rewind ? Color.white : Color.black);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart()
    {
        pending = Kind.None;
        Playing = Kind.None;
        Finished = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 장면의 Awake 뒤, Start 앞: 원래 감독을 꺼서 프롤로그 · 1장 진행이 시작되지 않게 한다
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (pending == Kind.None) return;
        var which = pending;
        pending = Kind.None;
        foreach (var p in FindObjectsByType<PrologueDirector>(FindObjectsSortMode.None)) p.enabled = false;
        foreach (var c in FindObjectsByType<Chapter1Director>(FindObjectsSortMode.None)) c.enabled = false;
        new GameObject("EndingStage").AddComponent<EndingStage>().kind = which;
    }

    void Start()
    {
        player = GameManager.Instance && GameManager.Instance.player ? GameManager.Instance.player : FindAnyObjectByType<PlayerController>();
        CutsceneSkip.Ensure();
        TutorialUI.ClearPending();
        Time.timeScale = 1f;
        StoryState.CutsceneActive = StoryState.ControlLocked = StoryState.CombatLocked = StoryState.RewindLocked = StoryState.WalkmanStowed = true;
        StoryState.Objective = null;
        StoryState.Waypoint = null;
        if (player)
        {
            if (player.TryGetComponent(out Health hp)) hp.Invulnerable = true;
            player.scriptedVelocity = Vector3.zero;
            player.SetWeaponVisible(false); // 이제 싸울 일은 없다
        }
        // 무대만 남긴다: 적(순찰 · 보스 · 시체), 전투 구역, 이야기 트리거와 상호작용, 테이프 줍기
        foreach (var e in new List<EnemyAI>(EnemyAI.All)) if (e) e.gameObject.SetActive(false);
        foreach (var z in FindObjectsByType<CombatZone>(FindObjectsSortMode.None)) foreach (var c in z.GetComponents<Collider>()) c.enabled = false;
        foreach (var t in FindObjectsByType<StoryTrigger>(FindObjectsSortMode.None)) t.gameObject.SetActive(false);
        foreach (var i in FindObjectsByType<StoryInteractable>(FindObjectsSortMode.None)) i.gameObject.SetActive(false);
        foreach (var p in FindObjectsByType<TapePickup>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        foreach (var c in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None)) allCams.Add(c);

        music = gameObject.AddComponent<AudioSource>();
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        radio = gameObject.AddComponent<AudioSource>();
        radio.loop = true;
        radio.playOnAwake = false;
        radio.spatialBlend = 0f;

        sequence = StartCoroutine(kind == Kind.Rewind ? EndingRewind() : EndingBreak());
    }

    void Update()
    {
        // 건너뛰기(Space 길게): 남은 장면을 접고 어두워진다
        if (StoryState.SkipRequested && !finishing)
        {
            if (sequence != null) StopCoroutine(sequence);
            StartCoroutine(Finish(0.8f));
        }
        if (music.isPlaying) music.volume = musicLevel * GameSettings.Gain(GameSettings.Channel.Music);
        if (radio.isPlaying) radio.volume = radioLevel * GameSettings.Gain(GameSettings.Channel.Sfx);
    }

    // =============== 엔딩 A — 다시, A면 ===============

    IEnumerator EndingRewind()
    {
        var d = FindAnyObjectByType<PrologueDirector>();
        brain = d.brain; camA = d.dynamicA; camB = d.dynamicB;
        brain.IgnoreTimeScale = true;
        if (d.deskWalkman) d.deskWalkman.SetActive(false); // 택배는 아직 오지 않았다
        if (d.deskLetter) d.deskLetter.SetActive(false);
        if (d.pocketWalkman) d.pocketWalkman.gameObject.SetActive(false);
        if (d.rainAudio) d.rainAudio.Stop(); // 비는 오지 않는다
        if (d.rain) d.rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var danny = d.dannyBody;
        if (danny)
        {
            if (danny.IsActive) danny.Restore();
            danny.gameObject.SetActive(false);
        }

        // ---- 레이의 방, 새벽 1시 ----
        Beat = "room";
        player.Teleport(d.homeSpawn.position, d.homeSpawn.rotation);
        StoryState.FadeColor = Color.white;
        StoryState.Fade = 1f;
        var wide = d.openingShots.Length > 0 && d.openingShots[0] ? d.openingShots[0] : camA;
        Shot(wide, 0f);
        yield return FadeTo(0f, 2.6f); // 하얗게 감긴 끝에서 방이 떠오른다
        StoryState.FadeColor = Color.black;
        StoryState.Title = "일주일 전";
        yield return Wait(2.2f);
        StoryState.Title = null;
        Vector3 desk = d.deskWalkman ? d.deskWalkman.transform.position : player.transform.position + player.transform.forward;
        StoryState.Say("", "(새벽 1시. 브라운관 TV가 지직거린다. 책상 위 휴대폰이 떨린다 — '대니')", 3.8f);
        ProceduralSfx.PlayAt(ProceduralSfx.PhoneBuzz, desk, 0.9f, 0.6f);
        yield return Wait(1.9f);
        ProceduralSfx.PlayAt(ProceduralSfx.PhoneBuzz, desk, 0.9f, 0.6f);
        yield return Wait(2.1f);

        // 레이 앞 얼굴(전화를 받는다)
        Beat = "call";
        Vector3 head = Head(player.animator, player.transform);
        Vector3 f = Flat(player.transform.forward), r = Vector3.Cross(Vector3.up, f);
        Vector3 c0 = head + f * 1.15f + r * 0.38f + Vector3.up * 0.04f, c1 = head + f * 0.85f + r * 0.28f + Vector3.up * 0.03f;
        Place(camA, c0, head + Vector3.down * 0.06f, 30f);
        Shot(camA, 1.0f);
        StartCoroutine(Dolly(camA, c0, c1, head + Vector3.down * 0.06f, head + Vector3.down * 0.06f, 30f, 28f, 16f));
        yield return Lines(
            ("대니 (전화)", "…레이? 자냐? 나야. 부탁 하나만 하자. 택배가 하나 갈 거야—", 3.8f),
            (Ray, "대니.", 1.6f),
            ("대니 (전화)", "…왜 그래, 목소리가.", 2.0f),
            (Ray, "택배 부치지 마. 문 잠그고 기다려. 누가 와도 열어 주지 마.", 3.6f),
            ("대니 (전화)", "…레이? 너, 뭘 알고—", 2.0f),
            (Ray, "가서 말할게. 이번엔— 둘 다 산다.", 3.0f));

        // 문으로: 현관 쪽에서 걸어오는 레이
        Beat = "leave";
        Vector3 door = d.homeDoor ? d.homeDoor.transform.position : player.transform.position - f * 3f;
        Vector3 toDoor = Flat(door - player.transform.position);
        Place(camB, door + Vector3.up * 0.55f - toDoor * 0.4f + Vector3.Cross(Vector3.up, toDoor) * 0.6f, player.transform.position + Vector3.up * 1.4f, 44f);
        Shot(camB, 0f);
        player.scriptedVelocity = toDoor * 2.2f;
        yield return Wait(1.3f);
        player.scriptedVelocity = Vector3.zero;
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("DoorOpen") ?? ProceduralSfx.DoorOpen, player.transform.position, 0.9f, 0f);
        yield return FadeTo(1f, 0.45f);

        // ---- 밤길: 대니 집까지 달린다(길 건너편에서 따라 찍는다) ----
        Beat = "run";
        Vector3 porch = d.friendOutside ? d.friendOutside.position : new Vector3(103f, 0.15f, 8.2f);
        player.Teleport(porch + new Vector3(-13f, 0f, 0.2f), Quaternion.LookRotation(Vector3.right));
        yield return Wait(0.25f);
        player.scriptedVelocity = Vector3.right * 6.2f;
        var track = StartCoroutine(Track(camA, () => player.transform.position, new Vector3(2.4f, 1.7f, -6.2f), new Vector3(0.6f, 1.1f, 0f), 42f));
        Shot(camA, 0f);
        yield return FadeTo(0f, 0.5f);
        float runUntil = StoryState.Now + 4f;
        while (player.transform.position.x < porch.x - 0.6f && StoryState.Now < runUntil) yield return null;
        player.scriptedVelocity = Vector3.zero;
        StopCoroutine(track);
        player.Teleport(Ground(new Vector3(porch.x, porch.y, porch.z + 1.4f)), Quaternion.LookRotation(Vector3.forward));

        // ---- 현관: 노크 → 불이 켜지고 대니가 나온다 ----
        Beat = "knock";
        Vector3 doorAt = new(porch.x, 1.5f, 11.9f);
        Vector3 rayAt = player.transform.position;
        Place(camB, rayAt + new Vector3(1.1f, 1.55f, -2.6f), doorAt + Vector3.down * 0.25f, 40f);
        Shot(camB, 0f);
        yield return Wait(0.7f);
        ProceduralSfx.PlayAt(ProceduralSfx.Knock, doorAt, 1f, 0.6f);
        yield return Wait(1.8f);
        Light porchLight = null;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.name == "PorchLight" && Vector3.Distance(l.transform.position, doorAt) < 2.5f) porchLight = l;
        if (porchLight) StartCoroutine(LightUp(porchLight, porchLight.intensity, new Color(1f, 0.82f, 0.62f), 0.35f)); // 더 밝으면 대니 얼굴이 하얗게 떴다
        ProceduralSfx.PlayAt(ProceduralSfx.Recorded("DoorOpen") ?? ProceduralSfx.DoorOpen, doorAt, 0.9f, 0.6f);
        if (danny)
        {
            // 현관 바닥 높이에 세운다(바닥을 재지 않으면 계단 높이만큼 땅에 박혔다)
            danny.transform.SetPositionAndRotation(Ground(new Vector3(porch.x - 0.05f, 0f, 11.2f)), Quaternion.LookRotation(Vector3.back));
            danny.gameObject.SetActive(true);
        }
        yield return Wait(1.2f);

        Beat = "danny";
        Vector3 dHead = danny ? Head(danny.GetComponentInChildren<Animator>(), danny.transform) : doorAt;
        Vector3 rHead = Head(player.animator, player.transform);
        // 대니 정면(레이 어깨 너머) ↔ 레이 정면(대니 어깨 너머)
        Place(camA, rHead + new Vector3(0.42f, 0.02f, -0.35f), dHead + Vector3.down * 0.05f, 30f);
        Place(camB, dHead + new Vector3(-0.4f, 0.02f, 0.3f), rHead + Vector3.down * 0.05f, 30f);
        Shot(camA, 0f);
        yield return Lines(("대니", "…레이? 이 시간에 웬일이야. 숨은 왜 그렇게 차?", 3.4f));
        Shot(camB, 0f);
        yield return Lines((Ray, "……살아 있네.", 2.4f));
        Shot(camA, 0f);
        yield return Lines(("대니", "뭐?", 1.4f));
        Shot(camB, 0f);
        yield return Lines((Ray, "아니. 택배, 아직 안 부쳤지?", 2.6f));
        Shot(camA, 0f);
        yield return Lines(("대니", "어… 내일 아침에 부치려고 했는데. 그걸 어떻게—", 3.2f));
        // 마지막: 둘이 한 화면에(옆에서)
        Place(camB, rayAt + new Vector3(2.2f, 1.3f, -1.8f), Vector3.Lerp(rHead, dHead, 0.5f) + Vector3.down * 0.2f, 38f); // 오른쪽에서: 왼쪽은 현관등이 대니 머리 뒤에서 번졌다
        Shot(camB, 0.8f);
        yield return Lines((Ray, "들어가자. 얘기가 길어. 이번엔 처음부터— 같이 듣자.", 3.8f));
        // 대니가 문 쪽으로 돌아서는 동안 천천히 어두워진다(문짝이 따로 없어 걸어 들어가면 문을 뚫고 지나갔다)
        if (danny) StartCoroutine(TurnActor(danny.transform, Quaternion.LookRotation(Vector3.forward), 1.6f));
        yield return Finish(4f);
    }

    // =============== 엔딩 B — 끝난 테이프 ===============

    IEnumerator EndingBreak()
    {
        var d = FindAnyObjectByType<Chapter1Director>();
        brain = d.brain; camA = d.dynamicA; camB = d.dynamicB;
        brain.IgnoreTimeScale = true;
        // 새벽 하늘: 1장의 바다 하늘(HarborSky)로 — 밤 하늘로 바꿔 둔 장면이어도 해가 뜨게
        var sky = d.skyCycle;
        // 재질은 복사본으로: 원본(HarborSky.mat)에 새벽 값을 쓰면 에디터에서 플레이한 뒤 파일이 바뀐 채 남았다
        if (sky && sky.sky) { sky.sky = new Material(sky.sky); RenderSettings.skybox = sky.sky; DynamicGI.UpdateEnvironment(); }
        if (sky) sky.Set(0.32f);
        if (d.rainAudio) d.rainAudio.Stop();
        foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.name == "Rain") ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.name == "Flood" || l.name == "Beam" || l.name == "AisleLamp") l.intensity *= 0.35f; // 새벽: 투광등이 빛을 잃는다

        Vector3 seat = d.dawnSeat ? d.dawnSeat.position : player.transform.position;
        player.Teleport(seat, Quaternion.LookRotation(new Vector3(0.3f, 0f, 1f)));
        MakeShip(seat);

        StoryState.FadeColor = Color.black;
        StoryState.Fade = 1f;
        Beat = "news";
        radio.clip = ProceduralSfx.RadioHiss;
        radioLevel = 0.5f;
        radio.Play();
        // 레이 뒤 낮은 곳에서 해 뜨는 바다 쪽(1장 끝과 같은 바다를 다시 본다)
        Vector3 w0 = seat + new Vector3(-1.5f, 1.35f, -3.3f), w1 = seat + new Vector3(-1.1f, 1.3f, -2.5f);
        Vector3 wl = seat + new Vector3(1.6f, 1.9f, 30f);
        Place(camB, w0, wl, 46f);
        Shot(camB, 0f);
        StartCoroutine(Dolly(camB, w0, w1, wl, wl, 46f, 44f, 24f));
        if (sky) StartCoroutine(DawnFrom(sky, 0.32f, 1f, 30f));
        StoryState.Say("", "(새벽. 부두 끝, 낡은 라디오)", 2.6f);
        yield return FadeTo(0f, 3f);
        yield return Lines(
            ("뉴스", "…다음 소식입니다. 어젯밤 한결 FM 30주년 기념 생방송 도중 송출된 녹음과 관련해,", 4.2f),
            ("뉴스", "한결 레코드 회장과 경찰청 차장, 현직 국회의원 등 열두 명이 오늘 새벽 체포됐습니다.", 4.4f),
            ("뉴스", "한결 타워 지하에서는 실종 신고됐던 스물세 명이 발견돼 병원으로 옮겨졌습니다.", 4.2f),
            ("뉴스", "경찰은 7년 전 중단된 내사 기록도 다시 들여다볼 방침입니다.", 3.6f));

        // 레이 옆얼굴(뒤로 해 뜨는 바다): 라디오를 끄고, 믹스테이프
        Beat = "tape";
        Vector3 head = Head(player.animator, player.transform);
        Vector3 f = Flat(player.transform.forward), r = Vector3.Cross(Vector3.up, f);
        // 레이 오른쪽에서(왼쪽은 배 몸체가 벽처럼 화면을 막았다). 살짝 위를 보고(하늘을 넓게 · 물 위 이음매가 화면 아래로 빠지게),
        // 다가가는 것은 조금만(얼굴이 화면 끝에 걸리지 않게)
        Vector3 p0 = head + r * 1.05f + f * 0.45f + Vector3.down * 0.04f, p1 = head + r * 0.97f + f * 0.43f + Vector3.down * 0.035f;
        Vector3 pl = head + f * 0.3f + Vector3.up * 0.05f; // 얼굴이 화면 왼쪽 1/4쯤(더 앞을 보면 얼굴이 화면 끝에 잘렸다)
        Place(camA, p0, pl, 34f);
        Shot(camA, 0f);
        ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, player.transform.position, 0.7f, 0f, 0.8f);
        radio.Stop();
        StoryState.Say("", "(찰칵. 라디오가 꺼진다)", 2.0f);
        yield return Wait(2.2f);
        StoryState.Say("", "(주머니에서 낡은 믹스테이프를 꺼내 카세트 플레이어에 끼운다)", 3.4f);
        yield return Wait(2.6f);
        ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, player.transform.position, 0.8f, 0f, 1f);
        StartMusic("lighthouse-1", 0.55f, 3f);
        yield return Wait(1.2f);
        StartCoroutine(Dolly(camA, p0, p1, pl, pl, 34f, 32f, 12f));
        yield return Lines(
            (Ray, "…잘 가라, 대니.", 2.6f),
            (Ray, "네 B면은 내가 끝까지 들었다.", 3.4f));
        yield return Finish(4.5f);
    }

    /// <summary>수평선 가까이, 해 옆에 멈춰 선 배 한 척(부두의 화물선 몸체를 본떠 불을 켜 둔다)</summary>
    void MakeShip(Vector3 seat)
    {
        GameObject hull = GameObject.Find("ShipHull");
        if (!hull) return;
        var root = new GameObject("EndingShip").transform;
        root.position = seat + new Vector3(70f, 0f, 150f);
        var copy = Instantiate(hull, root);
        copy.transform.localPosition = new Vector3(0f, hull.transform.position.y, 0f);
        copy.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * hull.transform.rotation;
        foreach (var c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (float z in new[] { -12f, 0f, 12f })
        {
            var lamp = new GameObject("ShipLamp").AddComponent<Light>();
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = new Vector3(z, 7f, 0f);
            lamp.type = LightType.Point;
            lamp.range = 14f;
            lamp.intensity = 3f;
            lamp.color = new Color(1f, 0.8f, 0.55f);
            lamp.shadows = LightShadows.None;
        }
    }

    static IEnumerator DawnFrom(SkyCycle sky, float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds && sky; t += StoryState.DeltaTime)
        {
            sky.Set(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
            yield return null;
        }
        if (sky) sky.Set(to);
    }

    // =============== 끝: 천천히 어두워지고 타이틀로 ===============

    IEnumerator Finish(float seconds)
    {
        finishing = true;
        Beat = "end";
        StoryState.SkipRequested = false;
        StoryState.Title = null;
        StoryState.FadeColor = Color.black;
        float m0 = musicLevel, r0 = radioLevel, f0 = StoryState.Fade;
        for (float t = 0f; t < seconds; t += StoryState.DeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            StoryState.Fade = Mathf.Lerp(f0, 1f, k);
            musicLevel = m0 * (1f - k);
            radioLevel = r0 * (1f - k);
            yield return null;
        }
        StoryState.Fade = 1f;
        StoryState.Say("", "", 0.01f);
        music.Stop();
        radio.Stop();
        yield return StoryState.Sleep(1.2f);
        Finished = true;
        Playing = Kind.None;
        if (SaveGame.Enabled) SaveGame.Delete(); // 끝까지 봤다: 다음엔 처음부터(시험 · 에디터 실행은 실제 저장을 건드리지 않는다)
        SceneLoader.Load(PauseMenu.TitleScene);
    }

    // =============== 도우미 ===============

    void StartMusic(string id, float level, float fadeIn)
    {
        StartCoroutine(PlayWhenReady(id, level, fadeIn));
    }

    IEnumerator PlayWhenReady(string id, float level, float fadeIn)
    {
        AudioClip clip = null;
        for (float t = 0f; t < 8f && !finishing && !(clip = MusicLibrary.Get(id)); t += StoryState.DeltaTime) yield return null; // 웹: 받는 동안 기다린다
        if (!clip || finishing) yield break;
        music.clip = clip;
        music.time = 0f;
        musicLevel = 0f;
        music.Play();
        yield return Ramp(level, fadeIn);
    }

    IEnumerator Ramp(float to, float seconds)
    {
        float from = musicLevel;
        for (float t = 0f; t < seconds && !finishing; t += StoryState.DeltaTime) { musicLevel = Mathf.Lerp(from, to, t / seconds); yield return null; }
        if (!finishing) musicLevel = to;
    }

    IEnumerator Lines(params (string speaker, string line, float seconds)[] lines)
    {
        foreach (var (speaker, line, seconds) in lines)
        {
            StoryState.Say(speaker, line, seconds);
            yield return Wait(seconds + 0.25f);
        }
    }

    /// <summary>그 자리의 바닥(현관 계단 · 마루 위). 충돌체가 없으면 받은 높이 그대로</summary>
    static Vector3 Ground(Vector3 at)
    {
        // 사람 키 높이에서 아래로(현관 지붕을 잡지 않게) — 그 아래에서 가장 높은 바닥
        var hits = Physics.RaycastAll(new Vector3(at.x, at.y + 1.8f, at.z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.rigidbody || h.collider.GetComponentInParent<Health>() || h.collider is CharacterController) continue; // 사람 몸은 바닥이 아니다
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? at : new Vector3(at.x, best + 0.01f, at.z);
    }

    static Vector3 Head(Animator anim, Transform root)
    {
        var h = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        return h ? h.position + Vector3.up * 0.05f : root.position + Vector3.up * 1.62f;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    static void Place(CinemachineCamera cam, Vector3 position, Vector3 lookAt, float fov)
    {
        cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
        cam.Lens.FieldOfView = fov;
    }

    void Shot(CinemachineCamera cam, float blend)
    {
        brain.DefaultBlend = blend <= 0f
            ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
            : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blend);
        foreach (var c in allCams) if (c) c.Priority = c == cam ? 30 : 0;
    }

    /// <summary>카메라를 천천히 옮긴다(위치 · 바라보는 곳 · 화각을 함께)</summary>
    static IEnumerator Dolly(CinemachineCamera cam, Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float fovFrom, float fovTo, float seconds)
    {
        for (float t = 0f; t < seconds && cam; t += StoryState.DeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            Place(cam, Vector3.Lerp(from, to, k), Vector3.Lerp(lookFrom, lookTo, k), Mathf.Lerp(fovFrom, fovTo, k));
            yield return null;
        }
    }

    /// <summary>달리는 인물을 따라 찍는다</summary>
    static IEnumerator Track(CinemachineCamera cam, System.Func<Vector3> target, Vector3 offset, Vector3 lookOffset, float fov)
    {
        while (cam)
        {
            Vector3 p = target();
            Place(cam, p + offset, p + lookOffset, fov);
            yield return null;
        }
    }

    /// <summary>천천히 돌아선다(대니가 문 쪽으로)</summary>
    static IEnumerator TurnActor(Transform who, Quaternion to, float seconds)
    {
        Quaternion from = who.rotation;
        for (float t = 0f; t < seconds && who; t += StoryState.DeltaTime)
        {
            who.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
    }

    static IEnumerator LightUp(Light l, float intensity, Color color, float seconds)
    {
        float i0 = l.intensity; Color c0 = l.color;
        for (float t = 0f; t < seconds; t += StoryState.DeltaTime)
        {
            l.intensity = Mathf.Lerp(i0, intensity, t / seconds);
            l.color = Color.Lerp(c0, color, t / seconds);
            yield return null;
        }
        l.intensity = intensity;
        l.color = color;
    }

    static IEnumerator Wait(float seconds)
    {
        float until = StoryState.Now + seconds;
        while (StoryState.Now < until && !StoryState.SkipRequested) yield return null;
    }

    static IEnumerator FadeTo(float target, float seconds)
    {
        float start = StoryState.Fade, t = 0f;
        while (t < seconds)
        {
            t += StoryState.DeltaTime;
            StoryState.Fade = Mathf.Lerp(start, target, t / seconds);
            yield return null;
        }
        StoryState.Fade = target;
    }
}
