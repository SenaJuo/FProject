using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

/// <summary>
/// 타이틀 화면(Title.unity): 비 오는 창가 책상 위 카세트 플레이어(흑백, REW 버튼만 붉다) + "REWIND" + 게임 시작 · 옵션 · 종료.
/// 정지 그림 위에 살아 있는 연출을 얹는다: 아주 느린 카메라 밀기, 창에 흘러내리는 빗방울, 빛줄기 속 먼지, REW 버튼의 붉은 숨결,
/// 구름이 지나가듯 바뀌는 밝기, 필름 입자, 비네트. 소리는 빗소리 + 타이틀 전용 곡(VARCO Sound, 3곡을 차례로).
/// 게임 시작: REW를 누른 듯 테이프가 감기는 소리와 화면이 흔들리고 어두워진 뒤 프롤로그로 간다.
/// 그림 속 카세트 플레이어의 건반은 진짜 테이프 데크처럼 누를 수 있다(마우스):
///  · PLAY 타이틀 음악 재생(건반이 눌린 채 걸린다) · STOP 테이프가 멈추듯 음이 내려가며 정지(PLAY가 풀린다)
///  · FF 감는 소리와 함께 곡을 앞으로 감는다(누르고 있으면 계속, 한 번 누르면 1초쯤). 곡 끝을 넘기면 다음 곡
///  · REW 게임 속 되감기 효과(색 빠짐·색 번짐·테이프 잡음)와 함께 타이틀 화면 자체가 거꾸로 감긴다 —
///    빗방울이 위로 오르고, 메뉴·로고가 나타난 순서의 반대로 사라지고, 음악도 처음으로 감긴 뒤 철컥 하고 타이틀이 처음부터 다시 나온다
/// </summary>
public class TitleScreen : MonoBehaviour
{
    public Texture2D background, logo;

    [Header("배경 그림 속 자리(0~1, 왼쪽 위가 0)")]
    [Tooltip("빗방울이 흘러내리는 창유리")]
    public Rect windowArea = new(0.70f, 0f, 0.30f, 0.34f);
    [Tooltip("붉은 REW 버튼 가운데")]
    public Vector2 rewButton = new(0.641f, 0.671f);
    [Tooltip("먼지가 떠다니는 빛줄기")]
    public Rect lightArea = new(0.30f, 0.30f, 0.45f, 0.45f);

    public string firstScene = "Prologue";
    [Tooltip("타이틀 테이프에 든 곡들(Resources 경로). 한 곡이 끝나거나 FF로 끝을 넘기면 다음 곡")]
    public string[] musicTracks = { "Music/Title/title-1", "Music/Title/title-2", "Music/Title/title-3" }; // VARCO Sound로 만든 타이틀 전용 곡(누아르 재즈 · 피아노와 첼로 · 되감기 피아노)

    [Header("카세트 건반(그림 속 자리, 0~1, 왼쪽 위가 0)")]
    public Rect keyPlay = new(0.557f, 0.613f, 0.066f, 0.078f);
    public Rect keyRew = new(0.607f, 0.633f, 0.070f, 0.087f);
    public Rect keyFf = new(0.661f, 0.660f, 0.073f, 0.087f);
    public Rect keyStop = new(0.721f, 0.685f, 0.073f, 0.093f);

    // 저장이 있으면 '이어하기'가 맨 위(기본 선택), 없으면 '게임 시작'
    string[] items = { "게임 시작", "설정", "종료" };
    const string ContinueItem = "이어하기", NewGameItem = "새로 시작", StartItem = "게임 시작", OptionsItem = "설정", QuitItem = "종료";
    static readonly Color Paper = new(0.92f, 0.90f, 0.86f);
    static readonly Color Dim = new(0.55f, 0.54f, 0.52f);

    enum State { Intro, Menu, Starting, Rewinding }
    const float RewindDuration = 2.6f;
    float rewindFrom, rewindStart, rewindMusicFrom;
    enum DeckKey { None = -1, Play, Rew, Ff, Stop }
    static readonly string[] KeyNames = { "PLAY  재생", "REW  처음으로 되감기", "FF  빨리 감기", "STOP  정지" };

    // 테이프 데크 상태
    int track, winding;          // winding: -1 되감는 중, +1 빨리 감는 중
    bool playing = true, musicHeld; // musicHeld: 정지·감기 때문에 우리가 멈춰 둔 상태(곡이 끝나 멈춘 것과 구별)
    float windUntil, motor = 1f, motorTarget = 1f, flashUntil;
    DeckKey hover = DeckKey.None, held = DeckKey.None, flash = DeckKey.None;
    Rect lastCover;
    AudioClip stopClip;

    State state;
    float t, startT;
    int selected;
    Vector2 lastMouse;
    bool mouseArmed, mouseSynced;
    AudioSource rain, music, whir;
    float musicBase = 0.45f, rainBase = 0.4f; // 타이틀 전용 곡이 앞에, 빗소리는 그 뒤에 깔린다
    Texture2D grain, vignette, glow, dot, streak;
    GUIStyle item, hint, keyLabel;
    float styleScale = -1f;

    struct Drop { public float x, y, speed, length, alpha; }
    struct Mote { public Vector2 p, v; public float size, phase; }
    readonly Drop[] drops = new Drop[34];
    readonly Mote[] motes = new Mote[46];

    void OnEnable() => PauseMenu.Suppressed = true;   // 타이틀에서는 Esc 일시정지를 열지 않는다
    void OnDisable() => PauseMenu.Suppressed = false;

    void Start()
    {
        if (SaveGame.HasSave) items = new[] { ContinueItem, NewGameItem, OptionsItem, QuitItem };
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 새 게임: 지난 판의 진행(프롤로그 회차·설명 알림)을 지운다
        PrologueDirector.Loop = 1;
        Tutorial.ResetProgress();

        rain = gameObject.AddComponent<AudioSource>();
        rain.clip = ProceduralSfx.RainLoop;
        rain.loop = true;
        rain.spatialBlend = 0f;
        rain.volume = 0f;
        rain.Play();

        // 음악은 따로 둔 오브젝트에서 튼다(시험 도구가 TitleMusic으로 찾는다)
        var radio = new GameObject("TitleMusic");
        radio.transform.SetParent(transform, false);
        music = radio.AddComponent<AudioSource>();
        music.loop = false; // 곡이 끝나면 다음 곡으로(Update)
        LoadTrack(0);
        music.spatialBlend = 0f;
        music.volume = 0f;
        if (music.clip) music.Play();

        whir = gameObject.AddComponent<AudioSource>();
        whir.clip = ProceduralSfx.Recorded("TapeRewind");
        whir.loop = true;
        whir.spatialBlend = 0f;
        stopClip = ProceduralSfx.Recorded("TapeStop");

        var rng = new System.Random(930);
        for (int i = 0; i < drops.Length; i++) drops[i] = NewDrop(rng, true);
        for (int i = 0; i < motes.Length; i++)
            motes[i] = new Mote
            {
                p = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                v = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.6f) * 0.012f,
                size = 1.5f + (float)rng.NextDouble() * 3f,
                phase = (float)rng.NextDouble() * 10f,
            };
        MakeTextures();
    }

    static Drop NewDrop(System.Random rng, bool anywhere) => new()
    {
        x = (float)rng.NextDouble(),
        y = anywhere ? (float)rng.NextDouble() : -0.1f - (float)rng.NextDouble() * 0.6f,
        speed = 0.025f + (float)rng.NextDouble() * 0.09f,
        length = 0.02f + (float)rng.NextDouble() * 0.05f,
        alpha = 0.10f + (float)rng.NextDouble() * 0.22f,
    };

    readonly System.Random dropRng = new(31);

    // ---------- 진행 ----------

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (state == State.Rewinding) UpdateRewind(); else t += dt;
        float flow = state == State.Rewinding ? -7f : 1f; // 되감는 동안 비와 먼지가 거꾸로 흐른다

        // 빗방울: 흘러내리다 멈칫하고 다시 흐른다
        for (int i = 0; i < drops.Length; i++)
        {
            var d = drops[i];
            float stutter = Mathf.PerlinNoise(i * 3.1f, t * 0.8f) > 0.35f ? 1f : 0.15f;
            d.y += d.speed * stutter * dt * flow;
            if (d.y > 1.1f) d = NewDrop(dropRng, false);
            else if (d.y < -0.15f && flow < 0f) { d = NewDrop(dropRng, false); d.y = 1.02f + (float)dropRng.NextDouble() * 0.3f; }
            drops[i] = d;
        }
        // 먼지: 빛줄기 속에서 천천히 떠다닌다(넘어가면 반대편에서 다시)
        for (int i = 0; i < motes.Length; i++)
        {
            var m = motes[i];
            m.p += (m.v + new Vector2(Mathf.Sin(t * 0.3f + m.phase), Mathf.Cos(t * 0.23f + m.phase)) * 0.004f) * dt * flow;
            m.p.x = Mathf.Repeat(m.p.x, 1f);
            m.p.y = Mathf.Repeat(m.p.y, 1f);
            motes[i] = m;
        }

        // 소리: 옵션 크기를 따라가며 서서히 커진다(시작하면 테이프가 멈추듯 음이 내려간다)
        float fadeIn = Mathf.Clamp01(t / 3f);
        float leaving = state == State.Starting ? Mathf.Clamp01((t - startT) / 1.8f) : 0f;
        if (rain) rain.volume = rainBase * fadeIn * (1f - leaving) * GameSettings.Gain(GameSettings.Channel.Sfx);
        UpdateDeck(dt);
        if (music)
        {
            music.volume = musicBase * fadeIn * (1f - leaving) * GameSettings.Gain(GameSettings.Channel.Music);
            music.pitch = Mathf.Lerp(motor, 0.35f, leaving);
        }
        if (whir) whir.volume = 0.8f * GameSettings.Gain(GameSettings.Channel.Sfx) * (state == State.Starting ? 1f - Mathf.Clamp01((t - startT - 1.3f) / 0.6f) : 1f);

        if (state == State.Starting || state == State.Rewinding || PauseMenu.IsOpen) { hover = held = DeckKey.None; return; }
        UpdateDeckInput();

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool any = (kb != null && kb.anyKey.wasPressedThisFrame) || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
                   || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (state == State.Intro)
        {
            if (t > 4.2f || (any && t > 0.3f)) { state = State.Menu; t = Mathf.Max(t, 4.2f); } // 아무 키나 누르면 바로 메뉴
            return;
        }

        if (Pressed(kb?.wKey, kb?.upArrowKey, pad?.dpad.up)) Select((selected + items.Length - 1) % items.Length);
        if (Pressed(kb?.sKey, kb?.downArrowKey, pad?.dpad.down)) Select((selected + 1) % items.Length);
        // 결정은 Enter(패드 A)만: 게임에서 연타하던 Space·F가 타이틀로 넘어와 '종료'를 누르지 않게
        if (Pressed(kb?.enterKey, kb?.numpadEnterKey, pad?.buttonSouth) && t > 4.6f) Activate(selected);
    }

    static bool Pressed(params ButtonControl[] buttons)
    {
        foreach (var b in buttons) if (b != null && b.wasPressedThisFrame) return true;
        return false;
    }

    void Select(int index)
    {
        if (index == selected) return;
        selected = index;
        Click(0.3f, 1.25f);
    }

    void Activate(int index)
    {
        switch (items[index])
        {
            case ContinueItem: StartCoroutine(StartGame(true)); break;
            case StartItem:
            case NewGameItem: StartCoroutine(StartGame(false)); break;
            case OptionsItem: Click(0.35f, 1.25f); PauseMenu.ShowOptions(); break;
            case QuitItem: Click(0.35f, 1.25f); Quit(); break;
        }
    }

    /// <summary>시험용: 게임 시작을 고른 것과 같다</summary>
    public void StartGameNow() { if (state != State.Starting) StartCoroutine(StartGame(false)); }

    /// <summary>cont = 이어하기(마지막 저장 지점부터), 아니면 프롤로그부터 새로</summary>
    IEnumerator StartGame(bool cont)
    {
        state = State.Starting;
        startT = t;
        winding = 0;
        Click(1f, 0.85f); // 피아노 건반 같은 버튼을 꾹 누르는 소리
        if (whir && whir.clip) whir.Play();
        yield return new WaitForSecondsRealtime(2.4f);
        if (cont && SaveGame.Continue()) yield break;
        SaveGame.Enabled = true; // 타이틀에서 시작한 게임만 진행을 저장한다
        SceneLoader.Load(firstScene);
    }

    // ---------- 카세트 데크 ----------

    // ---------- REW: 타이틀을 처음으로 되감는다 ----------

    /// <summary>시험용: 지금 되감는 중인가</summary>
    public bool Rewinding => state == State.Rewinding;
    public float TitleTime => t;

    void StartRewind()
    {
        if (state != State.Menu) return;
        state = State.Rewinding;
        rewindFrom = t;
        rewindStart = Time.unscaledTime;
        rewindMusicFrom = music && music.clip ? music.time : 0f;
        winding = 0;
        musicHeld = true;
        if (music && music.isPlaying) music.Pause();
        if (whir && whir.clip) { whir.pitch = 0.9f; whir.Play(); }
    }

    /// <summary>감을수록 빨라지다가(게임 속 되감기처럼) 처음에 닿는다: 화면 시간 t와 음악 위치를 함께 0으로</summary>
    void UpdateRewind()
    {
        float p = Mathf.Clamp01((Time.unscaledTime - rewindStart) / RewindDuration);
        float left = (1f - p) * (1f - p);
        t = rewindFrom * left;
        if (music && music.clip) music.time = Mathf.Clamp(rewindMusicFrom * left, 0f, music.clip.length - 0.05f);
        if (whir) whir.pitch = Mathf.Lerp(0.9f, 1.7f, p);
        if (p < 1f) return;
        // 처음에 닿았다: 철컥 — 타이틀을 처음부터 다시
        t = 0f;
        state = State.Intro;
        if (whir) { whir.Stop(); whir.pitch = 1f; }
        if (stopClip) ProceduralSfx.PlayAt(stopClip, Vector3.zero, 0.8f, 0f);
        if (music) { LoadTrack(0); playing = true; ResumeMusic(); }
    }

    /// <summary>되감기 효과 세기(0~1): 들어올 때 빠르게 켜지고, 처음에 닿기 직전 꺼진다</summary>
    float RewindK()
    {
        if (state != State.Rewinding) return 0f;
        float p = Mathf.Clamp01((Time.unscaledTime - rewindStart) / RewindDuration);
        return Mathf.Clamp01(p / 0.1f) * Mathf.Clamp01((1f - p) / 0.08f);
    }

    /// <summary>게임 속 되감기 화면(RewindEffect + HUD)을 타이틀에서 그린다: 색 번짐·푸른빛·테이프 잡음 줄</summary>
    void DrawRewindOverlay(Rect cover, float k, float W, float H, float s)
    {
        // 색 번짐: 그림을 좌우로 살짝 어긋나게 붉게·푸르게 겹친다
        float o = 7f * s * k;
        GUI.color = new Color(1f, 0.25f, 0.2f, 0.2f * k);
        GUI.DrawTexture(new Rect(cover.x + o, cover.y, cover.width, cover.height), background);
        GUI.color = new Color(0.2f, 0.6f, 1f, 0.2f * k);
        GUI.DrawTexture(new Rect(cover.x - o, cover.y, cover.width, cover.height), background);
        // 색이 빠지고 푸르스름하게(게임: 채도 -85, 색 필터 0.85·0.95·1)
        Fill(new Rect(0, 0, W, H), new Color(0.72f, 0.84f, 1f, 0.16f * k));
        // 테이프 잡음 줄(게임 HUD와 같은 모양)
        int frame = Time.frameCount;
        for (int i = 0; i < 14; i++)
        {
            float y = Mathf.PerlinNoise(frame * 0.37f, i * 1.7f) * H;
            float h = (2f + Mathf.PerlinNoise(i * 3.1f, frame * 0.21f) * 10f) * s;
            Fill(new Rect(0, y, W, h), new Color(Paper.r, Paper.g, Paper.b, 0.18f * k));
        }
        GUI.color = Color.white; // 타이틀에서는 "◀◀ -초" 숫자를 띄우지 않는다(화면 효과만)
    }

    void LoadTrack(int index)
    {
        if (musicTracks == null || musicTracks.Length == 0) return;
        track = ((index % musicTracks.Length) + musicTracks.Length) % musicTracks.Length;
        music.clip = Resources.Load<AudioClip>(musicTracks[track]);
        music.time = 0f;
    }

    /// <summary>테이프를 지금 자리에서 다시 돌린다(모터가 붙으며 음이 살짝 올라온다)</summary>
    void ResumeMusic()
    {
        musicHeld = false;
        motor = 0.6f;
        motorTarget = 1f;
        if (!music.clip) return;
        float at = music.time;
        if (!music.isPlaying) music.Play();
        music.time = Mathf.Min(at, music.clip.length - 0.05f);
    }

    void UpdateDeck(float dt)
    {
        if (!music || !music.clip) return;
        if (winding != 0)
        {
            // 감는 동안 음악은 멈춘 채 위치만 빠르게 옮긴다(14배속)
            float at = music.time + winding * 14f * dt;
            if (at >= music.clip.length - 0.1f) { LoadTrack(track + 1); at = 0f; }      // 테이프 끝을 넘기면 다음 곡 처음
            if (at <= 0f) { at = 0f; if (winding < 0) { music.time = 0f; EndWind(); return; } } // 되감기는 처음에서 멈춘다
            music.time = at;
            bool stillHeld = held == (winding < 0 ? DeckKey.Rew : DeckKey.Ff);
            if (!stillHeld && Time.unscaledTime >= windUntil) EndWind();
            return;
        }
        motor = Mathf.MoveTowards(motor, motorTarget, dt * (motorTarget < motor ? 2f : 3f));
        if (!playing && motor <= 0.31f && music.isPlaying) music.Pause();
        // 곡이 끝났다: 다음 곡(재생 중일 때)
        if (playing && !musicHeld && !music.isPlaying && state != State.Starting) { LoadTrack(track + 1); ResumeMusic(); }
    }

    void EndWind()
    {
        winding = 0;
        if (whir) whir.Stop();
        Click(0.6f, 0.7f); // 철컥: 감기가 끝나 건반이 튀어 오른다
        if (playing) ResumeMusic();
    }

    void UpdateDeckInput()
    {
        var mouse = Mouse.current;
        if (mouse == null || state != State.Menu) { hover = DeckKey.None; return; }
        Vector2 p = mouse.position.ReadValue();
        hover = KeyAt(new Vector2(p.x, Screen.height - p.y));
        if (mouse.leftButton.wasPressedThisFrame && hover != DeckKey.None) { held = hover; PressKey(hover); }
        if (!mouse.leftButton.isPressed) held = DeckKey.None;
    }

    /// <summary>시험용: 건반을 누른 것과 같다(0 PLAY, 1 REW, 2 FF, 3 STOP)</summary>
    public void PressKeyNow(int key) => PressKey((DeckKey)key);
    public bool DeckPlaying => playing;
    public int DeckWinding => winding;
    public int DeckTrack => track;
    public float DeckTime => music ? music.time : 0f;
    public bool MusicAudible => music && music.isPlaying && winding == 0;

    void PressKey(DeckKey key)
    {
        Click(0.9f, 0.8f); // 건반 누르는 소리
        flash = key;
        flashUntil = Time.unscaledTime + 0.18f;
        switch (key)
        {
            case DeckKey.Play:
                if (winding != 0) { winding = 0; if (whir) whir.Stop(); }
                if (!playing || musicHeld || !music.isPlaying) { playing = true; ResumeMusic(); }
                break;
            case DeckKey.Stop:
                if (winding != 0) { winding = 0; if (whir) whir.Stop(); music.Pause(); musicHeld = true; playing = false; break; }
                if (playing)
                {
                    playing = false;
                    musicHeld = true;
                    motorTarget = 0.3f; // 테이프가 멈추듯 음이 내려간 뒤 멈춘다
                    if (stopClip) ProceduralSfx.PlayAt(stopClip, Vector3.zero, 0.7f, 0f);
                }
                break;
            case DeckKey.Rew:
                StartRewind();
                break;
            case DeckKey.Ff:
                winding = 1;
                windUntil = Time.unscaledTime + 1.0f;
                musicHeld = true;
                if (music.isPlaying) music.Pause();
                if (whir && whir.clip) { whir.pitch = winding < 0 ? 1f : 1.25f; whir.Play(); }
                break;
        }
    }

    Rect KeyRect(DeckKey k) => k switch { DeckKey.Play => keyPlay, DeckKey.Rew => keyRew, DeckKey.Ff => keyFf, _ => keyStop };

    DeckKey KeyAt(Vector2 gui)
    {
        if (lastCover.width <= 0f) return DeckKey.None;
        var uv = new Vector2((gui.x - lastCover.x) / lastCover.width, (gui.y - lastCover.y) / lastCover.height);
        DeckKey best = DeckKey.None; float bestD = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var r = KeyRect((DeckKey)i);
            if (!r.Contains(uv)) continue;
            float d = (r.center - uv).sqrMagnitude; // 건반이 비스듬히 겹치는 모서리는 가운데가 가까운 쪽
            if (d < bestD) { bestD = d; best = (DeckKey)i; }
        }
        return best;
    }

    bool KeyDown(DeckKey k) =>
        (k == DeckKey.Play && playing && winding == 0) ||          // PLAY는 재생 중 눌린 채 걸려 있다
        (k == DeckKey.Rew && (winding < 0 || state == State.Starting || state == State.Rewinding)) ||
        (k == DeckKey.Ff && winding > 0) ||
        (k == flash && Time.unscaledTime < flashUntil);

    /// <summary>눌린 건반은 조금 가라앉아(아래로 밀려) 어둡게, 마우스를 올린 건반은 밝게 + 이름</summary>
    void DrawKeys(Rect cover, float s)
    {
        for (int i = 0; i < 4; i++)
        {
            var k = (DeckKey)i;
            var r = KeyRect(k);
            var screen = new Rect(cover.x + r.x * cover.width, cover.y + r.y * cover.height, r.width * cover.width, r.height * cover.height);
            if (KeyDown(k))
            {
                float sink = 3f * s * cover.width / Screen.width;
                var tc = new Rect(r.x, 1f - (r.y + r.height), r.width, r.height);
                GUI.DrawTextureWithTexCoords(new Rect(screen.x, screen.y + sink, screen.width, screen.height), background, tc);
                GUI.color = new Color(0f, 0f, 0f, 0.32f);
                GUI.DrawTexture(new Rect(screen.x + screen.width * 0.08f, screen.y + sink + screen.height * 0.08f, screen.width * 0.84f, screen.height * 0.84f), glow);
                GUI.color = Color.white;
            }
            else if (k == hover && state == State.Menu)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.16f);
                GUI.DrawTexture(screen, glow);
                GUI.color = Color.white;
            }
        }
        if (hover != DeckKey.None && state == State.Menu)
        {
            var r = KeyRect(hover);
            var p = new Vector2(cover.x + r.center.x * cover.width, cover.y + r.y * cover.height);
            Label(new Rect(p.x - 150f * s, p.y - 44f * s, 300f * s, 34f * s), KeyNames[(int)hover], keyLabel, new Color(Paper.r, Paper.g, Paper.b, 0.95f));
        }
    }

    static void Click(float volume, float pitch)
    {
        var clip = ProceduralSfx.ButtonClick;
        if (clip) ProceduralSfx.PlayAt(clip, Vector3.zero, volume, 0f, pitch);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- 그리기 ----------

    void OnGUI()
    {
        UiTheme.Begin(); // 게임 글꼴(Noto Sans KR)
        if (!background) return;
        float W = Screen.width, H = Screen.height, s = H / 1080f;
        GUI.depth = 10; // 맨 뒤(옵션 창이 위에 그려지게). 정하지 않으면 나중에 생긴 쪽이 위로 올라왔다
        // 배치(Layout) 단계는 그리는 순서(GUI.depth)만 정하고 바로 끝낸다 — 이 단계를 아예 끄면(useGUILayout=false) 순서가 무시되고 먼저 생긴 순으로 굳었다
        if (Event.current.type == EventType.Layout) return;
        BuildStyles(s);
        var e = Event.current;

        // 배경: 화면을 꽉 채우고(잘라서) 아주 느리게 REW 버튼 쪽으로 다가갔다 물러난다
        float leaving = state == State.Starting ? Mathf.Clamp01((t - startT) / 1.6f) : 0f;
        float rw = RewindK();
        float zoom = 1.04f + 0.025f * Mathf.Sin(t * Mathf.PI * 2f / 70f) + leaving * leaving * 0.08f + rw * 0.035f; // 되감을 때 볼록 렌즈처럼
        float scale = Mathf.Max(W / background.width, H / background.height) * zoom;
        Vector2 size = new(background.width * scale, background.height * scale);
        Vector2 focus = Vector2.Lerp(new Vector2(0.5f, 0.5f), rewButton, 0.25f + leaving * 0.5f);
        var cover = new Rect(W / 2f - focus.x * size.x, H / 2f - focus.y * size.y, size.x, size.y);
        cover.x = Mathf.Clamp(cover.x, W - size.x, 0f);
        cover.y = Mathf.Clamp(cover.y, H - size.y, 0f);

        lastCover = cover;
        if (e.type == EventType.Repaint)
        {
            if (state == State.Starting) DrawRewindBands(cover, leaving, W, H);
            else if (rw > 0f) DrawRewindBands(cover, 0.35f * rw, W, H);
            else if (winding != 0) DrawRewindBands(cover, 0.12f, W, H); // 감는 동안 화면도 살짝 떨린다
            else GUI.DrawTexture(cover, background);
            if (state != State.Starting) DrawKeys(cover, s);
            if (rw > 0f) DrawRewindOverlay(cover, rw, W, H, s);

            DrawRain(cover, s);
            DrawDust(cover, s);
            DrawRewGlow(cover, s, leaving);

            // 구름이 지나가듯 밝기가 천천히 바뀐다 + 필름 입자 + 가장자리 어둡게
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.05f + 0.07f * Mathf.PerlinNoise(t * 0.25f, 3.3f)));
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(0, 0, W, H), vignette);
            float gx = Random.value, gy = Random.value;
            GUI.color = new Color(1f, 1f, 1f, 0.07f + 0.13f * rw); // 되감을 때 입자가 굵어진다
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, W, H), grain, new Rect(gx, gy, W / (256f * 1.5f), H / (256f * 1.5f)));
            GUI.color = Color.white;

            // 영화 같은 위아래 검은 띠
            float bar = H * 0.065f;
            Fill(new Rect(0, 0, W, bar), Color.black);
            Fill(new Rect(0, H - bar, W, bar), Color.black);
        }

        // 제목과 메뉴(왼쪽 어두운 자리)
        float uiAlpha = state == State.Starting ? 1f - Mathf.Clamp01((t - startT) / 0.6f) : 1f;
        float logoAlpha = Mathf.Clamp01((t - 1.0f) / 1.8f) * uiAlpha;
        // 영사기처럼 처음에 살짝 깜빡이며 떠오른다
        if (t < 3.2f) logoAlpha *= 0.75f + 0.25f * Mathf.PerlinNoise(t * 18f, 1.7f);
        var logoRect = new Rect(W * 0.065f, H * 0.29f, W * 0.31f, 0f);
        if (logo) logoRect.height = logoRect.width * logo.height / logo.width;
        if (logo && logoAlpha > 0.01f && e.type == EventType.Repaint)
        {
            GUI.color = new Color(1f, 1f, 1f, logoAlpha);
            GUI.DrawTexture(logoRect, logo);
            GUI.color = Color.white;
        }

        float menuAlpha = Mathf.Clamp01((t - 2.6f) / 1.0f) * uiAlpha;
        if (state == State.Intro) menuAlpha = Mathf.Min(menuAlpha, Mathf.Clamp01((t - 2.6f) / 1.0f));
        if (menuAlpha > 0.01f)
        {
            if (!mouseSynced && e.type == EventType.Repaint) { lastMouse = e.mousePosition; mouseSynced = true; }
            bool mouseMoved = e.type == EventType.Repaint && (e.mousePosition - lastMouse).sqrMagnitude > 1f;
            if (e.type == EventType.Repaint) lastMouse = e.mousePosition;
            if (mouseMoved) mouseArmed = true;
            float x = logoRect.x + 22f * s, y = logoRect.yMax + H * 0.05f;
            for (int i = 0; i < items.Length; i++)
            {
                var r = new Rect(x, y + i * 58f * s, 360f * s, 48f * s);
                bool interactive = state == State.Menu && !PauseMenu.IsOpen;
                if (interactive && mouseMoved && r.Contains(e.mousePosition)) Select(i);
                // 마우스를 움직인 뒤의 클릭만 받는다(게임에서 연타하던 공격 클릭이 '종료'를 누르지 않게)
                if (interactive && mouseArmed && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    selected = i;
                    e.Use();
                    Activate(i);
                }
                bool on = i == selected;
                if (on)
                {
                    // ▶ 표시(고른 줄은 조금 밝게 숨 쉰다)
                    float pulse = 0.8f + 0.2f * Mathf.Sin(t * 3f);
                    Label(new Rect(r.x - 34f * s, r.y, 30f * s, r.height), "▶", item, new Color(UiTheme.Amber.r, UiTheme.Amber.g, UiTheme.Amber.b, menuAlpha * pulse)); // 호박색(다른 메뉴의 고른 줄과 같은 색)
                }
                Label(new Rect(r.x + 8f * s, r.y, r.width, r.height), items[i], item, on ? new Color(Paper.r, Paper.g, Paper.b, menuAlpha) : new Color(Dim.r, Dim.g, Dim.b, menuAlpha));
            }
            Label(new Rect(0, H - H * 0.065f - 40f * s, W - 40f * s, 30f * s), "W/S 선택   Enter 결정", hint, new Color(Dim.r, Dim.g, Dim.b, menuAlpha * 0.6f));
        }

        // 처음 어둠에서 밝아지고, 시작하면 다시 어두워진다
        float black = 1f - Mathf.Clamp01(t / 2.2f);
        if (state == State.Starting) black = Mathf.Max(black, Mathf.Clamp01((t - startT - 1.2f) / 1.0f));
        if (black > 0.001f && e.type == EventType.Repaint) Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, black));
    }

    Vector2 ToScreen(Rect cover, Vector2 uv) => new(cover.x + uv.x * cover.width, cover.y + uv.y * cover.height);

    void DrawRain(Rect cover, float s)
    {
        foreach (var d in drops)
        {
            if (d.y < 0f || d.y > 1f) continue;
            var uv = new Vector2(windowArea.x + d.x * windowArea.width, windowArea.y + d.y * windowArea.height);
            var p = ToScreen(cover, uv);
            float len = d.length * windowArea.height * cover.height;
            GUI.color = new Color(1f, 1f, 1f, d.alpha);
            GUI.DrawTexture(new Rect(p.x, p.y - len, 2.2f * s, len), streak);
            // 물줄기 끝의 맺힌 방울
            GUI.DrawTexture(new Rect(p.x - 2f * s, p.y - 3f * s, 6f * s, 6f * s), dot);
        }
        GUI.color = Color.white;
    }

    void DrawDust(Rect cover, float s)
    {
        foreach (var m in motes)
        {
            var uv = new Vector2(lightArea.x + m.p.x * lightArea.width, lightArea.y + m.p.y * lightArea.height);
            var p = ToScreen(cover, uv);
            float twinkle = 0.5f + 0.5f * Mathf.Sin(t * 1.3f + m.phase * 3f);
            // 빛줄기 가운데일수록 잘 보인다
            float edge = Mathf.Sin(m.p.x * Mathf.PI) * Mathf.Sin(m.p.y * Mathf.PI);
            GUI.color = new Color(1f, 1f, 1f, 0.35f * twinkle * edge);
            float r = m.size * s;
            GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), dot);
        }
        GUI.color = Color.white;
    }

    void DrawRewGlow(Rect cover, float s, float leaving)
    {
        var p = ToScreen(cover, rewButton);
        float breathe = 0.16f + 0.07f * Mathf.Sin(t * 1.4f);
        if (winding < 0) breathe = 0.42f; // 되감는 중에는 REW가 환하다
        float a = Mathf.Lerp(breathe, 0.75f, Mathf.Clamp01(leaving * 2.5f));
        float r = (120f + 60f * leaving) * s * cover.width / Screen.width;
        GUI.color = new Color(1f, 0.12f, 0.08f, a);
        GUI.DrawTexture(new Rect(p.x - r, p.y - r * 0.6f, r * 2f, r * 1.2f), glow);
        GUI.color = Color.white;
    }

    /// <summary>시작: 테이프를 감을 때처럼 화면이 가로 띠로 찢겨 흔들리고 흰 잡음 줄이 지나간다</summary>
    void DrawRewindBands(Rect cover, float k, float W, float H)
    {
        int bands = 24;
        float bh = H / bands;
        for (int i = 0; i < bands; i++)
        {
            float y = i * bh;
            float n = Mathf.PerlinNoise(i * 0.7f, t * 9f) - 0.5f;
            float shift = n * k * W * 0.08f;
            // 이 띠가 배경 그림의 어디에 해당하는지(텍스처 좌표는 아래가 0)
            float v0 = 1f - (y + bh - cover.y) / cover.height, vh = bh / cover.height;
            float u0 = (0f - shift - cover.x) / cover.width, uw = W / cover.width;
            GUI.DrawTextureWithTexCoords(new Rect(0, y, W, bh + 1f), background, new Rect(u0, v0, uw, vh));
        }
        for (int i = 0; i < 3; i++)
        {
            float y = Mathf.Repeat(t * (0.9f + i * 0.37f) + i * 0.31f, 1f) * H;
            Fill(new Rect(0, y, W, 2f + i), new Color(1f, 1f, 1f, 0.25f * k));
        }
    }

    // ---------- 바탕 텍스처(코드로 만든다) ----------

    void MakeTextures()
    {
        var rng = new System.Random(7);
        grain = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var px = new Color32[256 * 256];
        for (int i = 0; i < px.Length; i++) { byte v = (byte)rng.Next(256); px[i] = new Color32(v, v, v, 255); }
        grain.SetPixels32(px);
        grain.Apply();

        vignette = Radial(256, (d) => new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 0.85f, Mathf.InverseLerp(0.45f, 1.05f, d))));
        glow = Radial(128, (d) => new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f)));
        dot = Radial(32, (d) => new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * d)));

        streak = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) streak.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(y / 63f, 1.5f))); // 아래(앞)가 진하다
        streak.Apply();
    }

    static Texture2D Radial(int n, System.Func<float, Color> f)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                tex.SetPixel(x, y, f(Mathf.Sqrt(dx * dx + dy * dy)));
            }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        foreach (var tex in new[] { grain, vignette, glow, dot, streak }) if (tex) Destroy(tex);
    }

    void BuildStyles(float s)
    {
        if (Mathf.Approximately(s, styleScale)) return;
        styleScale = s;
        item = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * s), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow };
        hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17 * s), alignment = TextAnchor.MiddleRight };
        keyLabel = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    static void Label(Rect r, string text, GUIStyle style, Color color) =>
        UIText.Shadowed(r, text, style, new Color(0f, 0f, 0f, 0.7f * color.a), color); // 칸을 넘치면 글자를 줄인다

    static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
