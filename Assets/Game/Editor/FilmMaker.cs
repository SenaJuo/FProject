using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 유튜브 트레일러(2026-10-19 "지금까지 만든 트레일러는 다시 새로"): 약 55초.
///  · 음악: night_drive-2(139.4 BPM). 컷은 모두 박자에 떨어진다. 109초쯤 가라앉는 구간에 레이가 죽고, 116.8초 다시 터지는 박에 되살아나 받아친다.
///  · 카메라: 게임 카메라가 아니라 레이(또는 적)에게 붙는 영화식 카메라 — 컷이 시작될 때의 방향을 기준으로 낮게 붙고, 손떨림 · 얕은 심도.
///  · 같은 장면에서 이어지는 컷(cont)은 게임 시간을 끊지 않고 카메라만 바꾼다(연타 → 다른 각도의 발차기, 죽음 → 쓰러짐 → 되감기).
///  · 설명(2026-10-19 "뭐하는 게임인지 모르겠어, 능력도 보였으면"): 아래 띠에 짧은 설명 · 오른쪽에 조작 키 · 흐름이 바뀌는 순간만 가운데 큰 글씨.
///    능력 컷은 장르 색으로 이름과 효과를 보여 주고, 읽을 수 있게 컷마다 8박(3.4초).
///  · 소리는 게임 소리(AudioRenderer) + 음악 + 몇 개의 효과음.
/// 결과: QA/trailer/REWIND_film.mp4 (미리보기는 컷마다 사진 세 장 QA/trailer/film_*.jpg)
/// </summary>
/// MakeIntro(2026-10-20 "진짜 게임 소개 영상처럼"): 약 1분 25초. 기능 제목 카드(01 잠입 · 02 맨손 격투 · 03 시간 되감기 · 04 카세트 테이프 능력 · 05 다섯 장의 이야기)
///  다음에 그 기능을 실제 게임 화면(게임 카메라 + HUD, FilmGrabber로 뜬다)과 영화식 컷을 섞어 보여 준다. 결과: QA/trailer/REWIND_intro.mp4
public static class FilmMaker
{
    const int W = 1920, H = 1080, Fps = 30;
    const string Out = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\trailer";
    // 소개 영상 곡(2026-10-20 "너무 밝다"): VARCO Sound로 만든 어두운 네오 누아르 곡, 95.2 BPM. 108초에 거의 무음 → 111.13초에 다시 터진다
    public const string IntroMusic = "Assets/Game/Art/Trailer/RewindToDarkness.mp3";
    const float IntroBeat = 60f / 95.2f, IntroTrackAt = 63.225f; // 영상 3.2초 = 곡 63.225초(박) → 되감은 뒤 받아치는 박(DropT)이 곡의 111.13초
    static string MusicPath => intro ? IntroMusic : "Music/night_drive-2";
    const float Bpm = 139.38f;
    /// <summary>소개 영상 맨 앞의 타이틀 화면 길이(2026-10-21: 타이틀 → 카세트 건반 → REW 되감기 → 본편)</summary>
    const float TitlePre = 11.6f;
    static float MusicStartT => (intro ? TitlePre : 0f) + 3.2f;
    static bool intro;
    // 트레일러 3.2초 = 곡 82.83초(박). 소개 영상은 32박 앞에서 들어와 앞부분이 길다(죽음 · 되감기 · 다시 터지는 박은 곡의 같은 자리)
    static float MusicTrackAt => intro ? IntroTrackAt : 82.83f;
    static string OutName => intro ? "REWIND_intro.mp4" : "REWIND_film.mp4";
    static string StillPrefix => intro ? "intro_" : "film_";
    static float Beat => 60f / Bpm;
    /// <summary>
    /// n박 뒤의 영상 시각. 컷 구성은 예전 곡(139.4 BPM)의 박 수로 적혀 있고, 소개 영상은 새 곡(95.2 BPM)의 가장 가까운 박으로 맞춘다
    /// </summary>
    static float B(float n)
    {
        float t = MusicStartT + n * Beat;
        return intro ? MusicStartT + Mathf.Round((t - MusicStartT) / IntroBeat) * IntroBeat : t;
    }
    static float DeathT => B(intro ? 92 : 60);
    static float DropT => B(intro ? 111 : 79);
    static float EndT => B(intro ? 192 : 127);

    public class Cut
    {
        public string scene, name;
        public float start, dur, preroll;
        public bool cont, black, logo, cassette;
        // 카메라: rig = 대상에게 붙는다(오프셋은 컷 시작 때 대상의 오른쪽 · 위 · 앞), 아니면 세계 좌표 from → to
        public bool rig = true; public string anchor, second; public Vector3 rigFrom, rigTo;
        // 게임 화면: 게임 카메라(레이 뒤 · 컷 시작 때 레이가 보는 쪽 기준 yaw)로 찍고 HUD까지 그대로
        public bool game; public float gYaw, gPitch = 12f, gDist = 4f, gDrift, gBase;
        public float lookMix, lookUp, shake = 0.35f, fov = 38f, fovTo = -1f, aperture = 2.8f, fadeIn, fadeOut, fill = 2.6f;
        public Vector3 from, to, lookFrom, lookTo;
        public Action setup;
        public List<(float at, Action act)> cues = new();
        // 실행 중
        public Vector3 R, F, anchorPos, camPos; public bool framed, camSet;
        public Cut At(float t, Action a) { cues.Add((t, a)); return this; }
    }

    static List<Cut> cuts;
    static int index, frame, total, stage, lastFrame, cueIndex;
    static float settleUntil;
    static bool preview, audioOn;
    static Scene preparedScene;
    static int Rate;
    static Camera cam, uiCam; static Light fillLight;
    static RenderTexture rt, uiRt; static Texture2D frameTex, uiTex; static Color32[] scenePx, uiPx;
    static Image fade, logoImg, capBar, keyBox, barTop, barBottom, casBody, casHubL, casHubR; static Image[] casLines; static Text capTitle, capBody, keyText, keyLabel, bigText, subText, featNum, featTitle, featBody; static Shadow bigShadow;
    static Volume volume; static DepthOfField dof; static ColorAdjustments grade;
    static MediaEncoder encoder; static float[] mix; static int mixCursor;
    static List<AudioClip> pending;
    public static string Status = "";

    // ======================== 컷 구성 ========================

    static List<Cut> Build()
    {
        Vector3 V(float x, float y, float z) => new(x, y, z);
        var list = new List<Cut>();
        Cut Add(string scene, float t0, float t1, string name) { var c = new Cut { scene = scene, start = t0, dur = t1 - t0, name = name }; list.Add(c); return c; }

        // ---- 0. 차가운 시작: 레이 방 책상 위 카세트 플레이어, 재생 버튼 → 음악 ----
        var c0 = Add("Prologue", 0f, MusicStartT, "walkman");
        c0.rig = false; c0.fov = 30f; c0.aperture = 2.8f; c0.fill = 0f; c0.fadeIn = 1.0f; c0.shake = 0.15f;
        c0.setup = () =>
        {
            var wm = GameObject.Find("DeskWalkman"); if (!wm) return;
            SitOnSurface(wm);
            var b = BoundsOf(wm); var dir = V(1f, 0.32f, 0.2f).normalized;
            c0.from = b.center + dir * 0.62f; c0.to = b.center + dir * 0.34f; c0.lookFrom = c0.lookTo = b.center;
            Dim("CeilingLamp", 0.35f);
        };

        // ---- 1~4. 세계: 밤 항구 → 걸어오는 레이 → 손전등 순찰 → 몰래 제압 ----
        var c1 = Add("Chapter1", B(0), B(8), "harbor");
        c1.rig = false; c1.fov = 42f; c1.aperture = 0f; c1.shake = 0.12f; c1.fill = 0f;
        c1.from = V(174, 24, 289); c1.to = V(181, 15, 301); c1.lookFrom = V(206, 3, 338); c1.lookTo = V(205, 2, 336);

        var c2 = Add("Chapter1", B(8), B(14), "walk");
        c2.rigFrom = V(-0.6f, 1.05f, 3.4f); c2.rigTo = V(-0.45f, 1.1f, 2.6f); c2.fov = 34f; c2.aperture = 2.0f;
        c2.setup = () => { Place(V(164, 0.02f, 34), V(164, 0, 40)); };
        c2.At(0f, () => Walk(Vector3.forward, 1.7f));

        var c3 = Add("Chapter1", B(14), B(20), "patrol");
        c3.anchor = "Thug_Lawn"; c3.second = "player"; c3.rigFrom = V(0.8f, 1.0f, 3.6f); c3.rigTo = V(0.55f, 1.05f, 2.9f); c3.lookMix = 0.35f; c3.fov = 36f; c3.aperture = 2.2f;
        c3.setup = () =>
        {
            var e = Enemy("Thug_Lawn"); Stand(e, V(121f, 0.02f, 46.5f), V(1f, 0f, 0.2f));
            Place(e.transform.position - e.transform.forward * 4.0f, e.transform.position);
        };
        c3.At(0f, () => { var e = Enemy("Thug_Lawn"); Walk(e.transform.position - Player().transform.position, 1.25f); });

        var c4 = Add("Chapter1", B(20), B(28), "takedown");
        c4.cont = true; c4.anchor = "Thug_Lawn"; c4.second = "player"; c4.rigFrom = V(2.1f, 1.15f, 0.5f); c4.rigTo = V(1.6f, 1.1f, 0.2f); c4.lookMix = 0.3f; c4.fov = 34f; c4.aperture = 2.0f;
        c4.At(0f, () => { StopWalk(); Choke("Thug_Lawn"); });

        // ---- 5~10. 몽타주(4박마다) ----
        var c5 = Add("Chapter1", B(28), B(32), "pier_combo");
        c5.rigFrom = V(-1.9f, 1.3f, 1.7f); c5.rigTo = V(-1.5f, 1.2f, 2.1f); c5.lookMix = 0.45f; c5.shake = 0.6f; c5.preroll = 0.3f;
        c5.setup = () => { Place(V(199f, 0.02f, 311.5f), V(199f, 0f, 314f)); Fighters(V(199f, 0f, 311.5f), Vector3.forward, "Thug_Dock_A", "Thug_Dock_B", "Thug_Dock_C"); };
        Attacks(c5, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.4f, 0));

        var c6 = Add("Chapter1", B(32), B(36), "pier_kick");
        c6.cont = true; c6.rigFrom = V(-1.3f, 0.8f, -1.7f); c6.rigTo = V(-1.05f, 0.75f, -1.3f); c6.lookMix = 0.6f; c6.shake = 0.5f;
        Attacks(c6, (0.05f, 1), (0.4f, 2));
        c6.At(0.62f, () => SlowMo(0.3f)).At(1.25f, () => SlowMo(1f));

        var c7 = Add("Chapter1", B(36), B(40), "shield");
        c7.rigFrom = V(-1.9f, 1.1f, 1.4f); c7.rigTo = V(-1.55f, 1.05f, 1.8f); c7.lookMix = 0.5f; c7.preroll = 0.2f;
        c7.setup = () => { Place(V(164f, 0.02f, 146f), V(164f, 0f, 150f)); Fighters(V(164f, 0f, 146f), Vector3.forward, "Cop_Shield_A", "Cop_Shield_B"); };
        Attacks(c7, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.45f, 2));

        var c8 = Add("Chapter1", B(40), B(44), "roll_counter");
        c8.rigFrom = V(-1.9f, 1.0f, 1.2f); c8.rigTo = V(-2.2f, 1.0f, 0.6f); c8.lookMix = 0.4f; c8.fov = 44f; c8.preroll = 0.2f;
        c8.setup = () => { Place(V(163f, 0.02f, 80f), V(163f, 0f, 84f)); Fighters(V(163f, 0f, 80f), Vector3.forward, "Thug_Exit_A", "Thug_Exit_B", "Thug_Yard_B"); };
        Attacks(c8, (0.05f, 0), (1.0f, 2));
        c8.At(0.45f, () => Dodge(-90f)).At(1.05f, () => SlowMo(0.3f)).At(1.5f, () => SlowMo(1f));

        var c9 = Add("Chapter3", B(44), B(48), "subjects");
        c9.rigFrom = V(0.7f, 1.5f, -2.6f); c9.rigTo = V(0.55f, 1.45f, -2.0f); c9.lookMix = 0.35f; c9.fov = 46f; c9.preroll = 0.4f;
        c9.setup = () => { Lights("KeyLight"); Place(V(322f, 0.02f, 15f), V(330f, 0f, 15f)); Fighters(V(322f, 0f, 15f), Vector3.right, "Subject_A", "Subject_B", "Subject_C"); };
        Attacks(c9, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.4f, 0));

        var c10 = Add("Chapter4", B(48), B(50), "sniper");
        c10.second = "Tower_Sniper"; c10.rigFrom = V(-0.9f, 0.55f, -1.7f); c10.rigTo = V(-0.8f, 0.5f, -1.5f); c10.lookMix = 0.85f; c10.fov = 48f; c10.aperture = 0f; c10.preroll = 1.6f;
        c10.setup = () =>
        {
            PlaceGround(V(5f, 21f, 40f), V(18f, 0f, 57f));
            var s = Enemy("Tower_Sniper"); if (s) { s.gameObject.SetActive(true); s.Activate(); }
        };

        // ---- 11~14. 죽음 → 되감기 (음악이 가라앉는 구간) ----
        var d1 = Add("Chapter1", B(50), DeathT, "last_fight");
        d1.rigFrom = V(-2.3f, 1.3f, 2.4f); d1.rigTo = V(-1.9f, 1.2f, 2.1f); d1.lookMix = 0.35f; d1.preroll = 0.2f;
        d1.setup = () =>
        {
            Place(V(206f, 0.02f, 320f), V(206f, 0f, 324f));
            Fighters(V(206f, 0f, 320f), Vector3.forward, "Thug_Pier_A", "Thug_Pier_B", "Thug_Yard_A");
            Fighter("Thug_Dock_Gunner", V(201f, 0.02f, 327f));
            foreach (var n in new[] { "Boss_Cleaner", "Thug_Pier_C", "Thug_Pier_D" }) { var x = Enemy(n); if (x) x.gameObject.SetActive(false); }
            var w = Walkman(); if (w) w.playbackSpeed = 1.1f; // 이 장면만 천천히 되감는다(보통 4배)
            TimeRewind.Clear();
        };
        Attacks(d1, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.9f, 0), (2.3f, 1), (2.75f, 2));
        d1.At(d1.dur - 0.04f, () =>
        {
            var g = Enemy("Thug_Dock_Gunner"); var h = Player().GetComponent<Health>();
            h.floor = 0; h.Invulnerable = false; typeof(Health).GetField("invulnerableUntil", Any)?.SetValue(h, 0f);
            h.TakeDamage(9999, g ? g.transform.position : Player().transform.position + Vector3.left);
            if (!h.IsDead) Debug.LogWarning("[필름] 레이가 쓰러지지 않음");
        });

        var d2 = Add("Chapter1", DeathT, B(64), "fall");
        d2.cont = true; d2.rigFrom = V(-1.1f, 2.7f, 1.5f); d2.rigTo = V(-0.85f, 2.35f, 1.15f); d2.lookUp = -0.35f; d2.fov = 40f; d2.aperture = 0f; d2.shake = 0.2f;
        d2.At(0.02f, () => { SlowMo(0.6f); Debug.Log("[필름] 쓰러짐 " + Player().GetComponent<Health>().IsDead); }).At(1.45f, () => SlowMo(0.15f));

        var d3 = Add("Chapter1", B(64), B(72), "rewind_wide");
        d3.cont = true; d3.rigFrom = V(-3.4f, 1.7f, 1.2f); d3.rigTo = V(-3.9f, 1.85f, 0.7f); d3.lookUp = -0.3f; d3.fov = 42f; d3.aperture = 0f; d3.shake = 0.15f;
        d3.At(0.15f, () => { var w = Walkman(); if (w) w.RewindSteps(1); });

        var d4 = Add("Chapter1", B(72), DropT, "rewind_low");
        d4.cont = true; d4.rigFrom = V(-1.8f, 1.1f, 2.3f); d4.rigTo = V(-1.5f, 1.05f, 1.9f); d4.lookMix = 0.35f; d4.shake = 0.3f;
        d4.At(0f, HideBoss).At(1.2f, HideBoss).At(2.4f, HideBoss);
        d4.At(d4.dur - 0.38f, () => { Player().GetComponent<Health>().Invulnerable = true; Dodge(-90f); });

        // ---- 15. 다시 터지는 박: 받아친다 ----
        var e1 = Add("Chapter1", DropT, B(85), "counter");
        e1.cont = true; e1.rigFrom = V(-1.7f, 1.3f, -1.1f); e1.rigTo = V(-2.0f, 1.25f, -0.5f); e1.lookMix = 0.6f; e1.shake = 0.6f;
        e1.At(0f, () => { HideBoss(); Attack(2); SlowMo(0.3f); }).At(0.55f, () => SlowMo(1f));
        Attacks(e1, (0.95f, 0), (1.35f, 1), (1.8f, 2));
        e1.At(e1.dur - 0.05f, () => { var w = Walkman(); if (w) w.playbackSpeed = 4f; });

        // ---- 16~19. 장르 능력 ----
        var e2 = Add("Chapter1", B(85), B(93), "neon");
        e2.rigFrom = V(-3.2f, 1.15f, 0.4f); e2.rigTo = V(-2.5f, 1.05f, 1.6f); e2.lookMix = 0.2f; e2.fov = 40f;
        e2.setup = () => { EndAbility(); Place(V(136f, 0.02f, 167f), V(136f, 0f, 178f)); Fighters(V(136f, 0f, 176.8f), Vector3.forward, "Thug_Market_A", "Thug_Market_B", "Thug_Road"); };
        e2.At(0f, () => Ability(TapeGenre.CityPop)).At(0.05f, () => Walk(Vector3.forward, 7.5f)).At(1.1f, StopWalk);
        Attacks(e2, (1.15f, 0), (1.45f, 1), (1.8f, 2), (2.2f, 0), (2.45f, 1), (2.85f, 2), (3.2f, 0));

        Cut AbilityCut(float t0, float t1, string name, TapeGenre g, Vector3 at, Vector3 rf, Vector3 rt2, string[] who, bool dodge)
        {
            var c = Add("Chapter1", t0, t1, name);
            c.rigFrom = rf; c.rigTo = rt2; c.lookMix = 0.45f; c.preroll = 0.2f;
            c.setup = () => { EndAbility(); Place(at, at + Vector3.forward * 4f); Fighters(at, Vector3.forward, who); };
            c.At(0f, () => Ability(g));
            if (dodge) { Attacks(c, (0.05f, 0), (0.95f, 2), (1.6f, 0), (2.0f, 1), (2.85f, 2)); c.At(0.45f, () => Dodge(90f)).At(2.35f, () => Dodge(-90f)); }
            else Attacks(c, (0.05f, 0), (0.4f, 1), (0.85f, 2), (1.4f, 0), (1.8f, 1), (2.25f, 2), (2.8f, 0));
            return c;
        }
        AbilityCut(B(93), B(101), "jazz", TapeGenre.Jazz, V(164f, 0.02f, 100f), V(-1.8f, 1.15f, 1.6f), V(-1.4f, 1.1f, 1.9f), new[] { "Thug_Lane", "Thug_Caller", "Thug_Spotter" }, true);
        AbilityCut(B(101), B(109), "soul", TapeGenre.Soul, V(199f, 0.02f, 302f), V(-1.7f, 1.15f, 1.8f), V(-1.35f, 1.1f, 2.1f), new[] { "Thug_Dock_A", "Thug_Dock_B", "Thug_Dock_C" }, false);
        AbilityCut(B(109), B(117), "ambient", TapeGenre.Ambient, V(163f, 0.02f, 80f), V(-1.6f, 1.4f, -2.2f), V(-1.3f, 1.3f, -1.8f), new[] { "Thug_Exit_A", "Thug_Exit_B", "Thug_Yard_B" }, false);

        // ---- 20~22. 마지막: 0번 → 레이 얼굴 → 로고 ----
        var f1 = Add("Chapter5", B(117), B(121), "zero");
        f1.second = "Subject_Zero"; f1.rigFrom = V(0.75f, 1.45f, -1.7f); f1.rigTo = V(0.6f, 1.4f, -1.25f); f1.lookMix = 0.8f; f1.fov = 40f; f1.aperture = 2.0f; f1.preroll = 0.5f;
        f1.setup = () => { EndAbility(); Place(V(420f, 0.02f, 4.5f), V(420f, 0f, 13f)); var z = Enemy("Subject_Zero"); if (z) { z.gameObject.SetActive(true); z.Activate(); } };

        var f2 = Add("Chapter1", B(121), EndT, "face");
        f2.rig = false; f2.fov = 24f; f2.aperture = 1.6f; f2.shake = 0.12f;
        f2.setup = () => { Place(V(198f, 0.02f, 326f), V(198f, 0f, 334f)); Face(f2); };

        var f3 = Add(null, EndT, EndT + 4.2f, "logo");
        f3.black = true; f3.logo = true;
        return list;
    }

    // ======================== 게임 소개 영상 ========================

    /// <summary>
    /// 기능 다섯 개를 제목 카드 → 게임 화면(HUD) · 영화식 컷 순서로. 박자는 트레일러와 같은 곡(32박 앞에서 시작),
    /// 죽음 → 되감기 → 받아치기는 곡이 가라앉았다 다시 터지는 자리.
    /// </summary>
    static List<Cut> BuildIntro()
    {
        Vector3 V(float x, float y, float z) => new(x, y, z);
        var list = new List<Cut>();
        Cut Add(string scene, float t0, float t1, string name) { var c = new Cut { scene = scene, start = t0, dur = t1 - t0, name = name }; list.Add(c); return c; }
        Cut Game(string scene, float t0, float t1, string name, float yaw, float pitch, float dist, float drift)
        { var c = Add(scene, t0, t1, name); c.game = true; c.gYaw = yaw; c.gPitch = pitch; c.gDist = dist; c.gDrift = drift; return c; }

        // ---- 도입: 카세트 플레이어 → 밤 항구 → 걸어오는 레이 ----
        // ---- 맨 앞: 게임 타이틀 화면(게임 화면 그대로) — STOP · PLAY · FF로 카세트 건반을 눌러 보이고, REW로 화면이 처음까지 되감기면 본편 ----
        var t0 = Game("Title", 0f, TitlePre, "title", 0f, 0f, 0f, 0f);
        t0.setup = () =>
        {
            var ts = UnityEngine.Object.FindAnyObjectByType<TitleScreen>(); if (!ts) return;
            TitleScreen.HideMenu = false;
            // 장면을 불러 두는 동안 흐른 시간을 지우고 처음부터(로고가 떠오르는 것부터 보이게)
            typeof(TitleScreen).GetField("t", Any).SetValue(ts, 0f);
            var sf = typeof(TitleScreen).GetField("state", Any); sf.SetValue(ts, Enum.Parse(sf.FieldType, "Intro"));
        };
        void Deck(float at, int key) => t0.At(at, () => { var ts = UnityEngine.Object.FindAnyObjectByType<TitleScreen>(); if (ts) ts.PressKeyNow(key); });
        Deck(4.8f, 3); // STOP: 음이 내려가며 멈춘다
        Deck(6.0f, 0); // PLAY
        Deck(7.2f, 2); // FF: 빨리 감기
        Deck(8.8f, 1); // REW: 화면 · 음악이 처음까지 되감긴다(2.6초)

        var c0 = Add("Prologue", TitlePre, MusicStartT, "walkman");
        c0.rig = false; c0.fov = 30f; c0.aperture = 2.8f; c0.fill = 0f; c0.fadeIn = 1.0f; c0.shake = 0.15f;
        c0.setup = () =>
        {
            var wm = GameObject.Find("DeskWalkman"); if (!wm) return;
            SitOnSurface(wm);
            var b = BoundsOf(wm); var dir = V(1f, 0.32f, 0.2f).normalized;
            c0.from = b.center + dir * 0.62f; c0.to = b.center + dir * 0.34f; c0.lookFrom = c0.lookTo = b.center;
            Dim("CeilingLamp", 0.35f);
        };

        var c1 = Add("Chapter1", B(0), B(8), "harbor");
        c1.rig = false; c1.fov = 42f; c1.aperture = 0f; c1.shake = 0.12f; c1.fill = 0f;
        c1.from = V(174, 24, 289); c1.to = V(181, 15, 301); c1.lookFrom = V(206, 3, 338); c1.lookTo = V(205, 2, 336);

        var c2 = Add("Chapter1", B(8), B(16), "walk");
        c2.rigFrom = V(-0.6f, 1.05f, 3.6f); c2.rigTo = V(-0.45f, 1.1f, 2.6f); c2.fov = 34f; c2.aperture = 2.0f;
        c2.setup = () => { Place(V(164, 0.02f, 32), V(164, 0, 40)); };
        c2.At(0f, () => Walk(Vector3.forward, 1.7f));

        // ---- 01 잠입: 손전등 순찰(영화식) → 등 뒤로 다가가 몰래 제압(게임 화면) ----
        var s1 = Add("Chapter1", B(16), B(22), "patrol");
        s1.anchor = "Thug_Lawn"; s1.second = "player"; s1.rigFrom = V(0.8f, 1.0f, 3.6f); s1.rigTo = V(0.55f, 1.05f, 2.9f); s1.lookMix = 0.35f; s1.fov = 36f; s1.aperture = 2.2f;
        s1.setup = () =>
        {
            StopWalk();
            var e = Enemy("Thug_Lawn"); Stand(e, V(121f, 0.02f, 46.5f), V(1f, 0f, 0.2f));
            Place(e.transform.position - e.transform.forward * 4.6f, e.transform.position);
        };
        s1.At(0f, () => { var e = Enemy("Thug_Lawn"); Walk(e.transform.position - Player().transform.position, 1.25f); });

        var s2 = Game("Chapter1", B(22), B(36), "takedown", 35f, 10f, 3.4f, -8f);
        s2.cont = true;
        s2.At(0f, StopWalk).At(1.1f, () => Choke("Thug_Lawn"));

        // ---- 02 맨손 격투: 연타(게임) → 회피 반격(영화식, 느리게) → 방패 경찰(영화식) ----
        var f1 = Game("Chapter1", B(36), B(46), "combo", 30f, 12f, 4.2f, -10f);
        f1.preroll = 0.3f;
        f1.setup = () => { Place(V(199f, 0.02f, 311.5f), V(199f, 0f, 314f)); Fighters(V(199f, 0f, 311.5f), Vector3.forward, "Thug_Dock_A", "Thug_Dock_B", "Thug_Dock_C"); };
        Attacks(f1, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.7f, 0), (2.1f, 1), (2.55f, 2), (3.4f, 0), (3.8f, 1));

        // 게임 규칙 그대로: 적의 주먹이 닿기 직전에 구르면 완벽 회피(적이 휘청, 시간이 느려짐) → 곧바로 공격하면 그 적에게 달려드는 3배 돌려차기.
        // 2026-10-20 "너무 빨라 회피 반격인지 모르겠다": 구르는 순간부터 돌려차기가 꽂힐 때까지 느리게, 두 사람 온몸이 보이게 옆에서 넓게
        var f3 = Add("Chapter1", B(46), B(62), "roll_counter");
        f3.rigFrom = V(-3.6f, 1.25f, 0.7f); f3.rigTo = V(-3.3f, 1.2f, 0.3f); f3.lookMix = 0.5f; f3.fov = 40f; f3.aperture = 0f; f3.shake = 0.15f; f3.preroll = 0.2f;
        f3.setup = () =>
        {
            Place(V(163f, 0.02f, 80f), V(163f, 0f, 84f));
            Fighter("Thug_Exit_B", V(163f, 0.02f, 81.5f));
            foreach (var n in new[] { "Thug_Exit_A", "Thug_Yard_B" }) { var x = Enemy(n); if (x) x.gameObject.SetActive(false); } // 한 명과의 맞대결로 잘 보이게
        };
        f3.At(0f, () => { ArmCounter(); var e = Enemy("Thug_Exit_B"); if (e) typeof(EnemyAI).GetField("nextAttackTime", Any).SetValue(e, Time.time + 0.7f); });
        f3.At(f3.dur - 0.05f, () => { counterArmed = false; Time.timeScale = 1f; });

        var f4 = Add("Chapter1", B(62), B(68), "shield");
        f4.rigFrom = V(-1.9f, 1.1f, 1.4f); f4.rigTo = V(-1.55f, 1.05f, 1.8f); f4.lookMix = 0.5f; f4.preroll = 0.2f;
        f4.setup = () => { Place(V(164f, 0.02f, 146f), V(164f, 0f, 150f)); Fighters(V(164f, 0f, 146f), Vector3.forward, "Cop_Shield_A", "Cop_Shield_B"); };
        Attacks(f4, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.45f, 2));

        // ---- 03 시간 되감기: 싸움(게임) → 총에 맞음 → 쓰러짐 → REWIND 화면(게임) → 되감기(게임) → 다시 터지는 박에 받아친다 ----
        var r1 = Game("Chapter1", B(68), B(84), "last_fight", 25f, 12f, 3.8f, 12f);
        r1.preroll = 0.2f;
        r1.setup = () =>
        {
            EndAbility();
            Place(V(206f, 0.02f, 320f), V(206f, 0f, 324f));
            Fighters(V(206f, 0f, 320f), Vector3.forward, "Thug_Pier_A", "Thug_Pier_B", "Thug_Yard_A");
            Fighter("Thug_Dock_Gunner", V(201f, 0.02f, 327f));
            foreach (var n in new[] { "Boss_Cleaner", "Thug_Pier_C", "Thug_Pier_D" }) { var x = Enemy(n); if (x) x.gameObject.SetActive(false); }
            var w = Walkman(); if (w) w.playbackSpeed = 1.1f; // 이 장면만 천천히 되감는다(보통 4배)
            TimeRewind.Clear();
        };
        // 다 쓰러뜨리지 않게 띄엄띄엄 치고, 사이에 구른다(적의 !! 예고 · 총잡이 조준선이 보이게)
        Attacks(r1, (0.05f, 0), (0.45f, 1), (0.9f, 2), (3.2f, 0), (3.6f, 1), (5.6f, 0), (6.0f, 1));
        r1.At(2.2f, () => Dodge(-90f)).At(4.6f, () => Dodge(90f));

        var r2 = Add("Chapter1", B(84), DeathT, "last_angle");
        r2.cont = true; r2.rigFrom = V(-2.3f, 1.3f, 2.4f); r2.rigTo = V(-1.9f, 1.2f, 2.1f); r2.lookMix = 0.35f;
        Attacks(r2, (0.05f, 0), (0.45f, 1), (0.9f, 2), (2.0f, 0), (2.4f, 1));
        r2.At(r2.dur - 0.04f, () =>
        {
            var g = Enemy("Thug_Dock_Gunner"); var h = Player().GetComponent<Health>();
            h.floor = 0; h.Invulnerable = false; typeof(Health).GetField("invulnerableUntil", Any)?.SetValue(h, 0f);
            int before = h.Current; bool hit = h.TakeDamage(9999, g ? g.transform.position : Player().transform.position + Vector3.left);
            Debug.Log($"[필름] 대본의 한 방: {before} → {h.Current}, 맞음 {hit}, 쓰러짐 {h.IsDead}, 되감기 {Walkman()?.ChoiceOpen}");
        });

        var r3 = Add("Chapter1", DeathT, B(96), "fall");
        r3.cont = true; r3.rigFrom = V(-1.1f, 2.7f, 1.5f); r3.rigTo = V(-0.85f, 2.35f, 1.15f); r3.lookUp = -0.35f; r3.fov = 40f; r3.aperture = 0f; r3.shake = 0.2f;
        r3.At(0.02f, () => SlowMo(0.6f)).At(1.45f, () => SlowMo(0.15f));
        r3.At(1.0f, () => { var h = Player().GetComponent<Health>(); var w = Walkman(); Debug.Log($"[필름] 쓰러진 뒤: 체력 {h.Current}, 단계 {typeof(Walkman).GetField("phase", Any)?.GetValue(w)}, 시간 {Time.timeScale}"); });

        var r4 = Game("Chapter1", B(96), B(100), "prompt", 40f, 30f, 4.2f, 6f);
        r4.cont = true;
        r4.At(0f, () => SlowMo(0.1f)); // 게임에서 쓰러졌을 때와 같은 느린 시간

        var r5 = Game("Chapter1", B(100), B(108), "rewind", 40f, 22f, 4.4f, -10f);
        r5.cont = true;
        r5.At(0.1f, () => { var w = Walkman(); if (w) w.RewindSteps(1); });
        r5.At(r5.dur - 0.1f, () => Player().GetComponent<Health>().floor = 1); // 되감은 뒤에는 다시 쓰러지지 않게

        var r6 = Add("Chapter1", B(108), DropT, "rewind_low");
        r6.cont = true; r6.rigFrom = V(-2.4f, 1.6f, 1.3f); r6.rigTo = V(-2.0f, 1.5f, 1.0f); r6.lookMix = 0.35f; r6.shake = 0.3f;
        r6.At(0f, HideBoss).At(0.6f, HideBoss);
        r6.At(r6.dur - 0.38f, () => { Player().GetComponent<Health>().Invulnerable = true; Dodge(-90f); });

        var r7 = Add("Chapter1", DropT, B(117), "counter");
        r7.cont = true; r7.rigFrom = V(-1.7f, 1.3f, -1.1f); r7.rigTo = V(-2.0f, 1.25f, -0.5f); r7.lookMix = 0.6f; r7.shake = 0.6f;
        r7.At(0f, () => { HideBoss(); Attack(2); SlowMo(0.3f); }).At(0.55f, () => SlowMo(1f));
        Attacks(r7, (0.95f, 0), (1.35f, 1), (1.8f, 2));
        r7.At(r7.dur - 0.05f, () => { var w = Walkman(); if (w) w.playbackSpeed = 4f; });

        // ---- 04 카세트 테이프 능력: R → Q/E 고르기(게임) → 네온 러시, 재즈(영화식) · 소울(게임) · 앰비언트(영화식) ----
        var a1 = Game("Chapter1", B(117), B(131), "neon", 75f, 8f, 4.2f, -18f);
        a1.setup = () => { EndAbility(); Place(V(136f, 0.02f, 167f), V(136f, 0f, 178f)); InsertTape("심야 라디오 B면", "late_radio"); };
        a1.At(2.5f, OpenChoice);
        a1.At(3.4f, () => { var w = Walkman(); if (w) w.Choose(2); Fighters(V(136f, 0f, 176.8f), Vector3.forward, "Thug_Market_A", "Thug_Market_B", "Thug_Road"); });
        a1.At(3.45f, () => Walk(Vector3.forward, 7.5f)).At(4.1f, StopWalk);
        Attacks(a1, (4.15f, 0), (4.45f, 1), (4.8f, 2), (5.2f, 0), (5.5f, 1));

        Cut AbilityCut(float t0, float t1, string name, TapeGenre g, Vector3 at, Vector3 rf, Vector3 rt2, string[] who, bool dodge)
        {
            var c = Add("Chapter1", t0, t1, name);
            c.rigFrom = rf; c.rigTo = rt2; c.lookMix = 0.45f; c.preroll = 0.2f;
            c.setup = () => { EndAbility(); Place(at, at + Vector3.forward * 4f); Fighters(at, Vector3.forward, who); };
            c.At(0f, () => Ability(g));
            if (dodge) { Attacks(c, (0.05f, 0), (0.95f, 2), (1.6f, 0), (2.0f, 1), (2.85f, 2)); c.At(0.45f, () => Dodge(90f)).At(2.35f, () => Dodge(-90f)); }
            else Attacks(c, (0.05f, 0), (0.4f, 1), (0.85f, 2), (1.4f, 0), (1.8f, 1), (2.25f, 2), (2.8f, 0));
            return c;
        }
        AbilityCut(B(131), B(139), "jazz", TapeGenre.Jazz, V(164f, 0.02f, 100f), V(-1.8f, 1.15f, 1.6f), V(-1.4f, 1.1f, 1.9f), new[] { "Thug_Lane", "Thug_Caller", "Thug_Spotter" }, true);

        var a3 = Game("Chapter1", B(139), B(147), "soul", 30f, 12f, 3.8f, 10f);
        a3.preroll = 0.2f;
        a3.setup = () => { EndAbility(); Place(V(199f, 0.02f, 302f), V(199f, 0f, 306f)); Fighters(V(199f, 0f, 302f), Vector3.forward, "Thug_Dock_A", "Thug_Dock_B", "Thug_Dock_C"); InsertTape("Harbor Blues", "harbor_blues"); };
        a3.At(0f, () => Ability(TapeGenre.Soul));
        Attacks(a3, (0.05f, 0), (0.4f, 1), (0.85f, 2), (1.4f, 0), (1.8f, 1), (2.25f, 2), (2.8f, 0));

        AbilityCut(B(147), B(155), "ambient", TapeGenre.Ambient, V(163f, 0.02f, 80f), V(-1.6f, 1.4f, -2.2f), V(-1.3f, 1.3f, -1.8f), new[] { "Thug_Exit_A", "Thug_Exit_B", "Thug_Yard_B" }, false);

        // ---- 05 다섯 장의 이야기(2026-10-20 "1장부터 차례대로, 제목 뒤에 싸움 말고"): 부두를 내려다보는 조용한 장면 위에 제목 →
        //      1장 반장 → 2장 보관소 경찰 → 3장 실험체 → 4장 저격수 → 5장 0번 → 카세트 되감기 → 로고
        var e0 = Add("Chapter1", B(155), B(161), "chapters");
        e0.rig = false; e0.fov = 44f; e0.aperture = 0f; e0.shake = 0.1f; e0.fill = 0f;
        e0.from = V(232, 26, 300); e0.to = V(225, 21, 309); e0.lookFrom = V(192, 2, 352); e0.lookTo = V(194, 2, 350);
        e0.setup = () => { EndAbility(); HideBoss(); };

        var k1 = Add("Chapter1", B(161), B(166), "ch1_boss");
        k1.second = "Boss_Cleaner"; k1.rigFrom = V(0.9f, 1.6f, -2.6f); k1.rigTo = V(0.7f, 1.55f, -2.0f); k1.lookMix = 0.6f; k1.fov = 42f; k1.aperture = 2.0f; k1.preroll = 0.3f;
        k1.setup = () => { Place(V(198f, 0.02f, 376f), V(198f, 0f, 384f)); Fighter("Boss_Cleaner", V(198f, 0.02f, 380.5f)); };
        Attacks(k1, (0.9f, 0), (1.3f, 1));

        var k2 = Add("Chapter2", B(166), B(171), "ch2_station");
        k2.anchor = "Cop_Cross"; k2.second = "player"; k2.rigFrom = V(0.8f, 1.0f, 3.6f); k2.rigTo = V(0.55f, 1.05f, 2.9f); k2.lookMix = 0.35f; k2.fov = 38f; k2.aperture = 2.2f; k2.preroll = 0.3f;
        k2.setup = () =>
        {
            var e = Enemy("Cop_Cross"); Stand(e, V(40f, 0.02f, 41f), V(1f, 0f, 0f));
            Place(e.transform.position - e.transform.forward * 4.2f, e.transform.position);
        };
        k2.At(0f, () => { var e = Enemy("Cop_Cross"); Walk(e.transform.position - Player().transform.position, 1.2f); });
        k2.At(k2.dur - 0.05f, StopWalk);

        var e1 = Add("Chapter3", B(171), B(176), "subjects");
        e1.rigFrom = V(0.7f, 1.5f, -2.6f); e1.rigTo = V(0.55f, 1.45f, -2.0f); e1.lookMix = 0.35f; e1.fov = 46f; e1.preroll = 0.4f;
        e1.setup = () => { EndAbility(); StopWalk(); Lights("KeyLight"); Place(V(322f, 0.02f, 15f), V(330f, 0f, 15f)); Fighters(V(322f, 0f, 15f), Vector3.right, "Subject_A", "Subject_B", "Subject_C"); };
        Attacks(e1, (0.05f, 0), (0.45f, 1), (0.9f, 2), (1.6f, 0));

        var e2 = Add("Chapter4", B(176), B(180), "sniper");
        e2.second = "Tower_Sniper"; e2.rigFrom = V(-0.9f, 0.55f, -1.7f); e2.rigTo = V(-0.8f, 0.5f, -1.5f); e2.lookMix = 0.85f; e2.fov = 48f; e2.aperture = 0f; e2.preroll = 1.6f;
        e2.setup = () =>
        {
            PlaceGround(V(5f, 21f, 40f), V(18f, 0f, 57f));
            var sn = Enemy("Tower_Sniper"); if (sn) { sn.gameObject.SetActive(true); sn.Activate(); }
        };

        var e3 = Add("Chapter5", B(180), B(186), "zero");
        e3.second = "Subject_Zero"; e3.rigFrom = V(0.75f, 1.45f, -1.9f); e3.rigTo = V(0.6f, 1.4f, -1.25f); e3.lookMix = 0.8f; e3.fov = 40f; e3.aperture = 2.0f; e3.preroll = 0.5f;
        e3.setup = () => { EndAbility(); Place(V(420f, 0.02f, 4.5f), V(420f, 0f, 13f)); var z = Enemy("Subject_Zero"); if (z) { z.gameObject.SetActive(true); z.Activate(); } };

        // 마지막(2026-10-20): 레이 얼굴 대신 카세트가 빠르게 되감긴다 → 멈추는 소리와 함께 로고
        var e4 = Add(null, B(186), EndT, "cassette");
        e4.black = true; e4.cassette = true;

        var e5 = Add(null, EndT, EndT + 4.2f, "logo");
        e5.black = true; e5.logo = true;
        return list;
    }


    // ======================== 설명 ========================
    // 0 = 아래 띠(왼쪽, 제목 + 한 줄), 1 = 조작 키(아래 띠 오른쪽), 2 = 가운데 큰 글씨, 3 = 로고 밑, 4 = 기능 제목 카드(번호 · 제목 · 한 줄)
    // 게임 화면 컷에서는 HUD(왼쪽 아래 카세트 · 왼쪽 위 체력)를 피해 0 · 1을 위 가운데로 옮긴다
    struct Cap { public float t0, t1; public int kind; public string a, b, n; public Color col; }
    static List<Cap> caps;
    static readonly Color Paper = new(0.93f, 0.89f, 0.80f);

    static List<Cap> Captions()
    {
        var l = new List<Cap>();
        void Low(float t0, float t1, string a, string b, Color col) => l.Add(new Cap { t0 = t0, t1 = t1, kind = 0, a = a, b = b, col = col });
        void Key(float t0, float t1, string key, string what) => l.Add(new Cap { t0 = t0, t1 = t1, kind = 1, a = key, b = what, col = Color.white });
        void Big(float t0, float t1, string a) => l.Add(new Cap { t0 = t0, t1 = t1, kind = 2, a = a, col = Color.white });
        Color amber = new(1f, 0.72f, 0.35f);

        Low(0.5f, MusicStartT - 0.15f, "친구가 남긴 카세트 플레이어", "되감기 버튼을 누르면 시간이 거꾸로 흐른다", amber);
        Low(B(0) + 0.25f, B(8) - 0.2f, "전직 형사 레이", "친구를 죽인 조직을 쫓는 3인칭 누아르 액션", amber);
        Low(B(14) + 0.2f, B(28) - 0.2f, "잠입", "손전등에 들키지 않게 등 뒤로", amber);
        Key(B(20) + 0.1f, B(28) - 0.2f, "Q", "몰래 제압");
        Low(B(28) + 0.15f, B(44) - 0.2f, "맨손 격투", "연타 · 발차기 · 구르기 반격", amber);
        Key(B(28) + 0.15f, B(40) - 0.1f, "좌클릭", "공격");
        Key(B(40) + 0.05f, B(44) - 0.2f, "우클릭", "구르기 → 반격");
        Low(B(44) + 0.15f, B(50) - 0.15f, "다섯 장의 이야기", "부두 · 경찰서 · 음반사 본사 · 송신소 · 30주년 무대", amber);

        Big(DeathT + 0.35f, B(64) - 0.1f, "쓰러져도");
        Low(B(64) + 0.1f, DropT - 0.2f, "시간 되감기", "쓰러지면 5초씩 되돌린다 · 테이프 하나에 세 번", Paper);
        Key(B(64) + 0.1f, B(72) - 0.1f, "R", "되감기");
        Big(B(72) + 0.1f, DropT - 0.25f, "되감은 시간은 레이만 기억한다");

        void Ability(float t0, float t1, TapeGenre g, string genre)
        {
            Low(t0 + 0.15f, t1 - 0.2f, genre + " 테이프 · " + TapeAbility.Name(g), TapeAbility.Describe(g), TapeAbility.Tint(g));
            Key(t0 + 0.15f, t1 - 0.2f, "R → E", "장르 능력");
        }
        Ability(B(85), B(93), TapeGenre.CityPop, "시티팝");
        Ability(B(93), B(101), TapeGenre.Jazz, "재즈");
        Ability(B(101), B(109), TapeGenre.Soul, "소울");
        Ability(B(109), B(117), TapeGenre.Ambient, "앰비언트");

        Low(B(117) + 0.15f, B(121) - 0.15f, "마지막 상대 · 0번", "음반사가 숨겨 온 실험체", amber);
        Big(B(121) + 0.3f, EndT - 0.1f, "되감을 것인가, 끝낼 것인가");
        l.Add(new Cap { t0 = EndT + 0.9f, t1 = EndT + 4.0f, kind = 3, a = "3인칭 누아르 액션  ·  PC", col = Paper });
        return l;
    }

    static List<Cap> IntroCaptions()
    {
        var l = new List<Cap>();
        void Low(float t0, float t1, string a, string b, Color col) => l.Add(new Cap { t0 = t0, t1 = t1, kind = 0, a = a, b = b, col = col });
        void Key(float t0, float t1, string key, string what) => l.Add(new Cap { t0 = t0, t1 = t1, kind = 1, a = key, b = what, col = Color.white });
        void Big(float t0, float t1, string a) => l.Add(new Cap { t0 = t0, t1 = t1, kind = 2, a = a, col = Color.white });
        void Feature(float at, string n, string a, string b, float len = 2.8f) => l.Add(new Cap { t0 = at + 0.1f, t1 = at + 0.1f + len, kind = 4, n = n, a = a, b = b, col = Color.white });
        Color amber = new(1f, 0.72f, 0.35f);

        Low(TitlePre + 0.5f, MusicStartT - 0.15f, "친구가 남긴 카세트 플레이어", "되감기 버튼을 누르면 시간이 거꾸로 흐른다", amber);
        Low(B(0) + 0.25f, B(8) - 0.2f, "전직 형사 레이", "친구 대니를 죽인 조직을 쫓는 3인칭 누아르 액션", amber);
        Low(B(8) + 0.2f, B(16) - 0.2f, "죽으면 되감는다", "단, 테이프에 든 횟수만큼만", amber);

        Feature(B(16), "01", "잠입", "손전등 순찰의 시야를 피해 등 뒤로");
        Low(B(22) + 0.2f, B(36) - 0.3f, "몰래 제압", "눈치채지 못한 적의 등 뒤에서 — 한 번에, 조용히", amber);

        Feature(B(36), "02", "맨손 격투", "연타 · 발차기 · 구르기 반격");
        Key(B(36) + 3.0f, B(46) - 0.2f, "좌클릭", "연타 — 잽 · 크로스 · 돌려차기");
        Low(B(46) + 0.1f, B(62) - 0.2f, "회피 반격", "주먹이 닿기 직전에 구르면 적이 휘청인다 — 그 순간 받아친다", amber);
        Key(B(46) + 0.1f, B(62) - 0.2f, "우클릭 → 좌클릭", "완벽 회피 → 반격");
        Low(B(62) + 0.15f, B(68) - 0.2f, "방패 경찰", "잽은 막히고, 돌려차기가 방패를 쳐낸다", amber);

        Feature(B(68), "03", "시간 되감기", "쓰러지면 카세트를 되감는다");
        Low(B(68) + 3.1f, B(84) - 0.2f, "예고를 보고 피한다", "!! 는 강타, 붉은 선은 총구", amber);
        Big(DeathT + 0.35f, B(96) - 0.1f, "쓰러져도");
        Key(B(96) + 0.1f, B(100) - 0.05f, "R", "되감기");
        Low(B(100) + 0.1f, B(108) - 0.2f, "5초씩 되감는다", "테이프 하나에 세 번 · 한 번 쓴 횟수는 다시 차지 않는다", Paper);
        Big(B(108) + 0.05f, DropT - 0.15f, "되감은 시간은 레이만 기억한다");

        Feature(B(117), "04", "카세트 테이프 능력", "장르 테이프로 R — Q는 되감기, E는 능력");
        Key(B(117) + 2.5f, B(131) - 0.2f, "R → E", "장르 능력");
        void Ability(float t0, float t1, TapeGenre g, string genre)
        {
            Low(t0, t1, genre + " 테이프 · " + TapeAbility.Name(g), TapeAbility.Describe(g), TapeAbility.Tint(g));
        }
        Ability(B(117) + 3.45f, B(131) - 0.2f, TapeGenre.CityPop, "시티팝");
        Ability(B(131) + 0.15f, B(139) - 0.2f, TapeGenre.Jazz, "재즈");
        Ability(B(139) + 0.15f, B(147) - 0.2f, TapeGenre.Soul, "소울");
        Ability(B(147) + 0.15f, B(155) - 0.2f, TapeGenre.Ambient, "앰비언트");
        Key(B(131) + 0.15f, B(155) - 0.2f, "R → E", "장르 능력");

        Feature(B(155), "05", "다섯 장의 이야기", "부두 · 경찰서 · 음반사 본사 · 송신소 · 30주년 무대", B(161) - B(155) - 0.25f); // 부두 장면 안에서 끝낸다
        Low(B(161) + 0.1f, B(166) - 0.1f, "1장 「B면」", "부두의 정리반 반장", amber);
        Low(B(166) + 0.1f, B(171) - 0.1f, "2장 「내사」", "경찰서에 숨어들어 배신자를 찾는다", amber);
        Low(B(171) + 0.1f, B(176) - 0.1f, "3장 「한결 레코드」", "음반사 지하의 실험체들", amber);
        Low(B(176) + 0.1f, B(180) - 0.1f, "4장 「송신소」", "산 위의 저격수", amber);
        Low(B(180) + 0.1f, B(186) - 0.15f, "5장 「30주년」", "생방송의 밤, 지하의 실험체 0번", amber);
        Big(B(186) + 0.3f, EndT - 0.1f, "되감을 것인가, 끝낼 것인가");
        // 로고 밑 글귀는 넣지 않는다(2026-10-20: 로고만)
        return l;
    }

    /// <summary>false면 설명 없이(포트폴리오 사진용)</summary>
    public static bool ShowCaptions = true;

    static float CapAlpha(Cap c, float t) => Mathf.Clamp01((t - c.t0) / 0.22f) * Mathf.Clamp01((c.t1 - t) / 0.22f);

    /// <summary>설명을 그 시각에 맞춰 띄운다. 돌려주는 값 = 기능 제목 카드의 진하기(그동안 화면을 어둡게)</summary>
    static float UpdateCaptions(float t, bool game)
    {
        Cap? low = null, key = null, big = null, sub = null, feat = null;
        foreach (var c in caps)
        {
            if (!ShowCaptions) break;
            if (t < c.t0 || t > c.t1) continue;
            if (c.kind == 0) low = c; else if (c.kind == 1) key = c; else if (c.kind == 2) big = c; else if (c.kind == 4) feat = c; else sub = c;
        }
        // 자리: 영화식 컷은 아래 검은 띠, 게임 화면은 위 가운데(HUD를 가리지 않게)
        Place(capTitle, game, new Vector2(742f, 89f), new Vector2(0f, -76f));
        Place(capBody, game, new Vector2(742f, 47f), new Vector2(0f, -118f));
        float la = low.HasValue ? CapAlpha(low.Value, t) : 0f;
        if (low.HasValue) { capTitle.text = low.Value.a; capBody.text = low.Value.b; capBar.color = WithA(low.Value.col, game ? 0f : la); capTitle.color = WithA(low.Value.col, la); }
        else { capBar.color = capTitle.color = Color.clear; }
        capBody.color = WithA(new Color(0.86f, 0.84f, 0.80f), la);

        float ka = key.HasValue ? CapAlpha(key.Value, t) : 0f;
        if (key.HasValue)
        {
            keyText.text = key.Value.a; keyLabel.text = key.Value.b;
            // 오른쪽 끝에 맞춘다: [키] 설명
            float lw = keyLabel.preferredWidth, kw = Mathf.Max(64f, keyText.preferredWidth + 34f);
            var lr = keyLabel.rectTransform; lr.sizeDelta = new Vector2(lw + 4f, 50f);
            var kr = keyBox.rectTransform; kr.sizeDelta = new Vector2(kw, 50f);
            if (game)
            {
                // 위 가운데: [키] 설명 한 줄을 가운데 맞춤, 설명 두 줄이 있으면 그 아래
                var top = new Vector2(0.5f, 1f); lr.anchorMin = lr.anchorMax = kr.anchorMin = kr.anchorMax = top;
                float w = kw + 18f + lw, y = low.HasValue ? -172f : -92f;
                kr.anchoredPosition = new Vector2(-w / 2f + kw / 2f, y); lr.anchoredPosition = new Vector2(-w / 2f + kw + 18f + lw / 2f, y);
            }
            else
            {
                var right = new Vector2(1f, 0f); lr.anchorMin = lr.anchorMax = kr.anchorMin = kr.anchorMax = right;
                lr.anchoredPosition = new Vector2(-120f - lw / 2f, 69f); kr.anchoredPosition = new Vector2(-120f - lw - 18f - kw / 2f, 69f);
            }
        }
        keyBox.color = WithA(new Color(0.93f, 0.89f, 0.80f), ka); keyText.color = WithA(new Color(0.08f, 0.07f, 0.06f), ka); keyLabel.color = WithA(Color.white, ka);

        float ba = big.HasValue ? CapAlpha(big.Value, t) : 0f;
        if (big.HasValue) bigText.text = big.Value.a;
        bigText.color = WithA(Color.white, ba); bigShadow.effectColor = new Color(0, 0, 0, 0.85f * ba);

        float sa = sub.HasValue ? CapAlpha(sub.Value, t) : 0f;
        if (sub.HasValue) subText.text = sub.Value.a;
        subText.color = WithA(Paper, sa);

        float fa = feat.HasValue ? Mathf.Clamp01((t - feat.Value.t0) / 0.3f) * Mathf.Clamp01((feat.Value.t1 - t) / 0.35f) : 0f;
        if (feat.HasValue) { featNum.text = feat.Value.n; featTitle.text = feat.Value.a; featBody.text = feat.Value.b; }
        featNum.color = WithA(new Color(1f, 0.72f, 0.35f), fa); featTitle.color = WithA(Color.white, fa); featBody.color = WithA(Paper, fa);
        return fa;
    }

    static void Place(Text x, bool game, Vector2 bottomLeft, Vector2 topCenter)
    {
        var r = x.rectTransform;
        if (game) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f); r.anchoredPosition = topCenter; x.alignment = TextAnchor.MiddleCenter; }
        else { r.anchorMin = r.anchorMax = Vector2.zero; r.anchoredPosition = bottomLeft; x.alignment = TextAnchor.MiddleLeft; }
    }

    static Color WithA(Color c, float a) => new(c.r, c.g, c.b, a);

    /// <summary>음악 크기(트레일러 시각 t): 3.2초에 들어와, 죽는 박에 가라앉았다가, 다시 터지는 박에 돌아오고, 로고 박에 끊긴다</summary>
    static float MusicGain(float t)
    {
        if (t < MusicStartT || t >= EndT + 0.06f) return 0f;
        float g = Mathf.Clamp01((t - MusicStartT) / 0.12f);
        if (intro) g *= Mathf.Lerp(0.5f, 1f, Mathf.Clamp01((t - MusicStartT) / 8f)); // 소개 영상: 곡 가운데서 들어오므로 처음 8초는 낮게 시작해 올린다
        if (t >= DeathT && t < DropT) g *= Mathf.Lerp(1f, intro ? 0.45f : 0.28f, Mathf.Clamp01((t - DeathT) / 0.3f)); // 새 곡은 스스로 가라앉으니 덜 줄인다
        if (t >= EndT) g *= 1f - (t - EndT) / 0.06f;
        return g * 0.62f;
    }

    // 효과음: "Sfx/..." 아래 이름, 또는 "Assets/..." 경로. "@초"를 붙이면 그 지점부터 재생
    const string RealRewind = "Assets/Game/Art/Trailer/TapeRewindReal.mp3"; // VARCO Sound: 테이프가 빠르게 감기며 높아지는 회전음 → 4.6초에 '탁' 멈춤
    static List<(float t, string sfx, float gain)> SfxList() => new()
    {
        ((intro ? TitlePre : 0f) + 2.78f, "World/ButtonClick", 0.9f),
        (intro ? B(176) + 0.69f : B(49.6f), "World/SniperShot", 0.75f),
        (DeathT - 0.04f, "World/Gunshot", 0.9f),
        (DropT, "Hit/impact_heavy-1", 0.7f),
        (intro ? -1f : EndT, "World/TapeStop", 1.0f),
        (intro ? B(186) - 0.05f : -1f, "World/ButtonClick", 0.9f),
        (intro ? EndT - 2.6f : -1f, RealRewind + "@2.0", 3.5f), // 카세트 장면 내내 감기다가 로고에서 '탁'
    };

    static string SfxPath(string sfx) => sfx.StartsWith("Assets/") ? sfx.Split('@')[0] : "Sfx/" + sfx;
    static float SfxFrom(string sfx) { var p = sfx.Split('@'); return p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 0f; }

    // ======================== 시작 ========================

    /// <summary>미리보기를 이 이름의 컷부터(빈 값이면 처음부터)</summary>
    public static string From = "";

    public static void Make(bool previewOnly) { intro = false; Begin(previewOnly); }

    /// <summary>게임 소개 영상(기능 제목 카드 + 게임 화면): QA/trailer/REWIND_intro.mp4</summary>
    public static void MakeIntro(bool previewOnly) { intro = true; Begin(previewOnly); }

    static void Begin(bool previewOnly)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 중에만");
        preview = previewOnly;
        Directory.CreateDirectory(Out);
        foreach (var f in Directory.GetFiles(Out, StillPrefix + "*.jpg")) File.Delete(f);
        Rate = AudioSettings.outputSampleRate;
        cuts = intro ? BuildIntro() : Build(); caps = intro ? IntroCaptions() : Captions();
        if (preview && !string.IsNullOrEmpty(From)) { int i = cuts.FindIndex(x => x.name == From); if (i > 0) cuts.RemoveRange(0, i); } // 미리보기: 이 컷부터
        if (intro) PlayModeWindow.SetCustomRenderingResolution((uint)W, (uint)H, "Film"); // 게임 화면 컷은 게임 창을 그대로 뜬다
        index = -1; stage = -1; audioOn = false;
        Status = "소리 불러오는 중";
        SetupCamera();
        pending = new List<AudioClip>();
        foreach (var n in new[] { MusicPath }.Concat(SfxList().Select(x => SfxPath(x.sfx))))
        { var c = Clip(n); if (c) { c.LoadAudioData(); pending.Add(c); } }
        settleUntil = Time.realtimeSinceStartup + 30f;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void SetupCamera()
    {
        rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        frameTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var go = new GameObject("FilmCamera"); UnityEngine.Object.DontDestroyOnLoad(go);
        cam = go.AddComponent<Camera>(); cam.enabled = false; cam.nearClipPlane = 0.02f; cam.farClipPlane = 700f; cam.allowHDR = true; cam.targetTexture = rt;
        var add = go.AddComponent<UniversalAdditionalCameraData>(); add.renderPostProcessing = true; add.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; add.antialiasingQuality = AntialiasingQuality.High;
        add.requiresDepthOption = CameraOverrideOption.On;

        // 보조 조명: 카메라 왼쪽 위에 붙는 부드러운 점광(밤 장면에서 인물이 실루엣만 남지 않게 — 촬영장의 반사판 몫)
        var lgo = new GameObject("FilmFill"); lgo.transform.SetParent(go.transform, false); lgo.transform.localPosition = new Vector3(-0.7f, 0.6f, 0.2f);
        fillLight = lgo.AddComponent<Light>(); fillLight.type = LightType.Point; fillLight.range = 7f; fillLight.color = new Color(1f, 0.9f, 0.8f); fillLight.shadows = LightShadows.None;
        // 심도 · 노출: 촬영용 볼륨(우선순위 높게)
        var vgo = new GameObject("FilmVolume"); UnityEngine.Object.DontDestroyOnLoad(vgo);
        volume = vgo.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 900f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        dof = profile.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focalLength.Override(50f); dof.aperture.Override(2.8f); dof.focusDistance.Override(3f);
        grade = profile.Add<ColorAdjustments>(true); grade.postExposure.Override(0.55f); grade.contrast.Override(14f); grade.saturation.Override(6f);
        volume.sharedProfile = profile;

        var ugo = new GameObject("FilmOverlayCamera"); UnityEngine.Object.DontDestroyOnLoad(ugo); ugo.transform.position = new Vector3(0, -3000, 0);
        uiCam = ugo.AddComponent<Camera>(); uiCam.enabled = false; uiCam.nearClipPlane = 0.01f; uiCam.farClipPlane = 10f; uiCam.allowHDR = false; uiCam.allowMSAA = false;
        uiRt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); uiTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = new Color(0, 0, 0, 0); uiCam.cullingMask = 1 << 30; uiCam.targetTexture = uiRt;
        var uadd = ugo.AddComponent<UniversalAdditionalCameraData>(); uadd.renderPostProcessing = false;

        var cgo = new GameObject("FilmCanvas"); UnityEngine.Object.DontDestroyOnLoad(cgo); cgo.layer = 30;
        var cv = cgo.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = uiCam; cv.planeDistance = 1f;
        var sc = cgo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(W, H);
        Image Rect01(string n, Vector2 min, Vector2 max, Color c) { var g = new GameObject(n); g.layer = 30; g.transform.SetParent(cgo.transform, false); var im = g.AddComponent<Image>(); im.color = c; var r = im.rectTransform; r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero; return im; }
        fade = Rect01("Fade", Vector2.zero, Vector2.one, new Color(0, 0, 0, 0));
        float bar = (1f - (W / 2.39f) / H) / 2f;
        barTop = Rect01("BarTop", new Vector2(0, 1 - bar), Vector2.one, Color.black);
        barBottom = Rect01("BarBottom", Vector2.zero, new Vector2(1, bar), Color.black);
        logoImg = Rect01("Logo", new Vector2(0.27f, 0.40f), new Vector2(0.73f, 0.64f), new Color(1, 1, 1, 0)); logoImg.preserveAspect = true;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Title/TitleLogo.png");
        if (tex) logoImg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        // 설명: 아래 검은 띠 안(그림을 가리지 않게). 왼쪽 = 색 막대 + 제목 + 한 줄, 오른쪽 = 키
        var font = UiTheme.Font ? UiTheme.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text Label(string n, int size, FontStyle st, TextAnchor al, Vector2 anchor, Vector2 pos, Vector2 sz)
        {
            var g = new GameObject(n); g.layer = 30; g.transform.SetParent(cgo.transform, false);
            var t = g.AddComponent<Text>(); t.font = font; t.fontSize = size; t.fontStyle = st; t.alignment = al; t.color = Color.clear;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            var r = t.rectTransform; r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = pos; r.sizeDelta = sz;
            return t;
        }
        capBar = Rect01("CapBar", Vector2.zero, Vector2.zero, Color.clear);
        { var r = capBar.rectTransform; r.pivot = new Vector2(0, 0.5f); r.anchoredPosition = new Vector2(120f, 69f); r.sizeDelta = new Vector2(5f, 78f); }
        capTitle = Label("CapTitle", 38, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(142f + 600f, 89f), new Vector2(1200f, 46f));
        capBody = Label("CapBody", 27, FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(142f + 600f, 47f), new Vector2(1200f, 36f));
        keyBox = Rect01("KeyBox", new Vector2(1, 0), new Vector2(1, 0), Color.clear);
        keyText = Label("KeyText", 28, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 50f));
        keyText.transform.SetParent(keyBox.transform, false); keyText.rectTransform.anchorMin = Vector2.zero; keyText.rectTransform.anchorMax = Vector2.one; keyText.rectTransform.sizeDelta = Vector2.zero;
        keyLabel = Label("KeyLabel", 30, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(1, 0), new Vector2(-200f, 69f), new Vector2(300f, 50f));
        bigText = Label("Big", 56, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(1600f, 90f));
        bigShadow = bigText.gameObject.AddComponent<Outline>(); bigShadow.effectDistance = new Vector2(2f, -2f); bigShadow.effectColor = Color.clear;
        subText = Label("LogoSub", 30, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1200f, 50f));
        // 기능 제목 카드(가운데): 번호 · 제목 · 한 줄
        featNum = Label("FeatNum", 34, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(400f, 50f));
        featTitle = Label("FeatTitle", 84, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(1600f, 110f));
        featBody = Label("FeatBody", 32, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(1600f, 50f));
        // 게임 화면 위에서도 읽히게 글자 테두리(글자 진하기를 따라간다)
        foreach (var x in new[] { capTitle, capBody, keyLabel, featNum, featTitle, featBody })
        { var o = x.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.85f); o.effectDistance = new Vector2(2f, -2f); }
        Image Pic(string n, string res)
        {
            var g = new GameObject(n); g.layer = 30; g.transform.SetParent(cgo.transform, false);
            var im = g.AddComponent<Image>(); im.raycastTarget = false; im.color = new Color(1, 1, 1, 0);
            var t = Resources.Load<Texture2D>(res); if (t) im.sprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
            var r = im.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            return im;
        }
        casBody = Pic("CassetteBody", "UI/Cassette_Label");
        casHubL = Pic("CassetteHubL", "UI/Cassette_Label_Hub"); casHubR = Pic("CassetteHubR", "UI/Cassette_Label_Hub");
        casLines = new Image[6];
        for (int i = 0; i < casLines.Length; i++) { casLines[i] = Rect01("TapeLine" + i, new Vector2(0, 0.5f), new Vector2(1, 0.5f), Color.clear); casLines[i].rectTransform.sizeDelta = new Vector2(0, 3f); }
        var grab = new GameObject("FilmGrabber"); UnityEngine.Object.DontDestroyOnLoad(grab); grab.AddComponent<FilmGrabber>();
    }

    static Cut Current => cuts[index];
    static int FrameOf(float t) => Mathf.RoundToInt(t * Fps);

    static void NextCut()
    {
        index++;
        if (index >= cuts.Count) { Finish(); return; }
        var c = Current;
        FilmGrabber.Want = c.game; // 첫 프레임부터 새로 뜬 게임 화면이 있게(전에는 지난 게임 컷의 마지막 장면이 한 프레임 끼었다)
        Status = $"컷 {index + 1}/{cuts.Count} {c.name}";
        total = FrameOf(c.start + c.dur) - FrameOf(c.start);
        frame = -FrameOf(c.preroll); cueIndex = 0; c.framed = false; c.camSet = false; lastFrame = -1;
        c.cues.Sort((a, b) => a.at.CompareTo(b.at));
        if (c.black) { stage = 2; return; }
        if (c.cont) { c.setup?.Invoke(); stage = 2; return; }
        StopCapture();
        if (SceneManager.GetActiveScene().name != c.scene) { SceneManager.LoadScene(c.scene); settleUntil = Time.realtimeSinceStartup + 4.5f; }
        else settleUntil = Time.realtimeSinceStartup + 0.5f;
        stage = 1;
    }

    static void StopCapture()
    {
        Time.captureFramerate = 0;
        if (audioOn) { AudioRenderer.Stop(); audioOn = false; }
        StopChase();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Abort("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (stage == -1)
        {
            if (pending.Any(c => c.loadState == AudioDataLoadState.Loading) && Time.realtimeSinceStartup < settleUntil) return;
            if (!preview) BeginEncoder();
            NextCut();
            return;
        }
        var cut = Current;
        if (stage == 1)
        {
            if (Time.realtimeSinceStartup < settleUntil) return;
            Prepare();
            cut.setup?.Invoke();
            Time.captureFramerate = Fps;
            if (!audioOn && !preview) { AudioRenderer.Start(); audioOn = true; }
            stage = 2;
            return;
        }
        if (stage != 2 || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        float now = frame / (float)Fps;
        while (cueIndex < cut.cues.Count && cut.cues[cueIndex].at <= now)
        {
            try { cut.cues[cueIndex].act(); } catch (Exception ex) { Debug.LogWarning($"[필름] {cut.name} 신호 {cueIndex}: {ex.GetBaseException().Message}"); }
            cueIndex++;
        }
        if (!cut.black) { UpdateChase(); UpdateCounter(); }

        if (frame >= 0)
        {
            float t = total > 1 ? frame / (float)(total - 1) : 0f;
            Render(cut, t, now);
            if (preview && counterFrame > 0 && (Time.frameCount - counterFrame) % 6 == 0 && Time.frameCount - counterFrame <= 48) // 회피 반격 확인용(미리보기)
                File.WriteAllBytes(Path.Combine(Out, $"{StillPrefix}{index:00}_{cut.name}_counter{Time.frameCount - counterFrame:00}{(landedFrame > 0 ? "_hit" : "")}.jpg"), frameTex.EncodeToJPG(82));
            if (frame == total / 6 || frame == total / 2 || frame == total * 5 / 6) // 확인용 사진(녹화할 때도)
                File.WriteAllBytes(Path.Combine(Out, $"{StillPrefix}{index:00}_{cut.name}_{frame * 10 / Mathf.Max(1, total)}.jpg"), frameTex.EncodeToJPG(82));
        }
        else if (audioOn) DiscardGameAudio();
        frame++;
        if (frame >= total)
        {
            // 컷 끝 바로 앞에 건 신호가 프레임 반올림으로 빠지지 않게(소개 영상에서 '총에 맞음'이 실행되지 않았다)
            for (; cueIndex < cut.cues.Count; cueIndex++)
                try { cut.cues[cueIndex].act(); } catch (Exception ex) { Debug.LogWarning($"[필름] {cut.name} 신호 {cueIndex}: {ex.GetBaseException().Message}"); }
            bool nextCont = index + 1 < cuts.Count && cuts[index + 1].cont;
            if (!nextCont) StopCapture();
            NextCut();
        }
    }

    // ======================== 그리기 ========================

    static void Render(Cut c, float t, float sec)
    {
        float e = Mathf.SmoothStep(0f, 1f, t);
        float a = 0f;
        if (c.fadeIn > 0f) a = Mathf.Max(a, 1f - Mathf.Clamp01(sec / c.fadeIn));
        if (c.fadeOut > 0f) a = Mathf.Max(a, Mathf.Clamp01((sec - (c.dur - c.fadeOut)) / c.fadeOut));
        float card = UpdateCaptions(c.start + sec, c.game);
        fade.color = new Color(0, 0, 0, c.black && !c.logo ? 1f : Mathf.Max(a, 0.55f * card));
        logoImg.color = new Color(1, 1, 1, c.logo ? Mathf.Clamp01((sec - 0.35f) / 0.9f) * Mathf.Clamp01((c.dur - sec) / 0.7f) : 0f);
        barTop.enabled = barBottom.enabled = !c.game;
        DrawCassette(c, sec);
        volume.enabled = !c.game;      // 게임 화면은 게임 그대로의 색 · 초점
        FilmGrabber.Want = c.game;

        if (c.game) GameShot(c, t);
        else
        {
        Vector3 look;
        if (c.black) { look = Vector3.forward; }
        else if (c.rig)
        {
            var anchor = Who(c.anchor);
            if (!anchor) anchor = Player()?.transform;
            if (!c.framed)
            {
                c.F = Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized; if (c.F.sqrMagnitude < 0.01f) c.F = Vector3.forward;
                c.R = Vector3.Cross(Vector3.up, c.F); c.anchorPos = anchor.position; c.framed = true;
            }
            c.anchorPos = Vector3.Lerp(c.anchorPos, anchor.position, 0.35f);
            var off = Vector3.Lerp(c.rigFrom, c.rigTo, e);
            var want = c.anchorPos + c.R * off.x + Vector3.up * off.y + c.F * off.z;
            look = LookPoint(anchor) + Vector3.up * c.lookUp;
            var sec2 = c.second == "player" ? Player()?.transform : c.second != null ? Who(c.second) : NearestEnemy(anchor.position, 7f)?.transform;
            if (sec2 && c.lookMix > 0f) look = Vector3.Lerp(look, LookPoint(sec2), c.lookMix);
            // 후보: 원래 자리 → 반대쪽 → 높이 → 가까이. 벽에 많이 막히거나 다른 사람이 레이를 가리면 다음 후보(2026-10-19: 적 등이 화면을 가렸다)
            var mirror = c.anchorPos - c.R * off.x + Vector3.up * off.y + c.F * off.z;
            var raised = Vector3.Lerp(look, want, 0.85f) + Vector3.up * 1.0f;
            var closer = Vector3.Lerp(look, want, 0.55f) + Vector3.up * 0.3f;
            var high = Vector3.Lerp(look, want, 0.75f) + Vector3.up * 2.0f;
            var subject = c.anchor == null || c.anchor == "player" ? Player()?.transform : anchor;
            Vector3 pos = Unblocked(look, want);
            foreach (var cand in new[] { want, mirror, raised, closer, high })
            {
                var q = Unblocked(look, cand);
                if ((q - look).magnitude < (cand - look).magnitude * 0.6f) continue; // 벽에 많이 막힘
                if (Occluded(q, LookPoint(subject), subject)) continue;               // 다른 사람이 가림
                pos = q; break;
            }
            // 자리를 바꿀 때 뚝 끊기지 않게 미끄러지듯
            c.camPos = c.camSet ? Vector3.Lerp(c.camPos, pos, 0.22f) : pos; c.camSet = true;
            cam.transform.position = c.camPos;
            cam.transform.LookAt(look);
        }
        else
        {
            cam.transform.position = Vector3.Lerp(c.from, c.to, e);
            look = Vector3.Lerp(c.lookFrom, c.lookTo, e);
            cam.transform.LookAt(look);
        }
        if (!c.black && c.shake > 0f)
        {
            float n = sec * 0.9f + index * 13.1f;
            cam.transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(n, 1.7f) - 0.5f) * 2f * c.shake, (Mathf.PerlinNoise(3.3f, n) - 0.5f) * 2f * c.shake, 0f);
        }
        cam.fieldOfView = c.fovTo > 0f ? Mathf.Lerp(c.fov, c.fovTo, e) : c.fov;
        fillLight.intensity = c.black ? 0f : c.fill;
        dof.active = c.aperture > 0f && !c.black;
        if (dof.active) { dof.aperture.value = c.aperture; dof.focusDistance.value = Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, look)); }

        cam.clearFlags = c.black ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox; cam.backgroundColor = Color.black;
        cam.cullingMask = c.black ? 0 : ~((1 << 5) | (1 << 30));
        cam.Render();
        RenderTexture.active = rt; frameTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        scenePx = frameTex.GetPixels32();
        }
        uiCam.Render();
        RenderTexture.active = uiRt; uiTex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
        uiPx = uiTex.GetPixels32();
        for (int i = 0; i < scenePx.Length; i++)
        {
            var u = uiPx[i]; if (u.a == 0) continue;
            int k = 255 - u.a; var s = scenePx[i];
            scenePx[i] = new Color32((byte)Mathf.Min(255, u.r + s.r * k / 255), (byte)Mathf.Min(255, u.g + s.g * k / 255), (byte)Mathf.Min(255, u.b + s.b * k / 255), 255);
        }
        frameTex.SetPixels32(scenePx);
        if (!preview) { encoder.AddFrame(frameTex); PushAudio(!c.black && audioOn); }
    }

    /// <summary>
    /// 마지막 카세트 장면: HUD와 같은 카세트 그림을 화면 가운데 크게, 릴이 점점 빨라지며 거꾸로(되감기) 돈다.
    /// 테이프 잡음 줄이 위아래로 흐르고, 그림이 아주 조금 떨린다
    /// </summary>
    static void DrawCassette(Cut c, float sec)
    {
        float a = c.cassette ? Mathf.Clamp01(sec / 0.35f) * Mathf.Clamp01((c.dur - sec) / 0.12f) : 0f;
        foreach (var im in new[] { casBody, casHubL, casHubR }) im.color = new Color(1, 1, 1, a);
        for (int i = 0; i < casLines.Length; i++) casLines[i].color = new Color(1, 1, 1, c.cassette ? a * 0.10f : 0f);
        if (!c.cassette) return;
        const float w = 820f, aspect = 1.556f, hubR = 0.070f; float h = w / aspect;
        var jit = new Vector2(Mathf.PerlinNoise(sec * 7f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, sec * 7f) - 0.5f) * 3f;
        var centre = new Vector2(0f, 70f) + jit;
        casBody.rectTransform.sizeDelta = new Vector2(w, h); casBody.rectTransform.anchoredPosition = centre;
        // 릴: 0.6초 동안 빨라져 초당 세 바퀴 반, 거꾸로(시계 반대 방향 = 되감기)
        float spin = 1300f * (sec - 0.3f * (1f - Mathf.Exp(-sec / 0.3f)));
        float hs = hubR * 2f * w;
        casHubL.rectTransform.sizeDelta = casHubR.rectTransform.sizeDelta = new Vector2(hs, hs);
        casHubL.rectTransform.anchoredPosition = centre + new Vector2((0.294f - 0.5f) * w, (0.5f - 0.470f) * h);
        casHubR.rectTransform.anchoredPosition = centre + new Vector2((0.706f - 0.5f) * w, (0.5f - 0.470f) * h);
        casHubL.rectTransform.localRotation = casHubR.rectTransform.localRotation = Quaternion.Euler(0f, 0f, spin);
        for (int i = 0; i < casLines.Length; i++)
        {
            float y = Mathf.Repeat(i * 0.173f + sec * (0.9f + i * 0.21f), 1f) * H - H / 2f;
            casLines[i].rectTransform.anchoredPosition = new Vector2(0f, y);
        }
    }

    /// <summary>게임 화면 컷: 게임 카메라 각도만 정하고(레이 기준), 방금 그려진 게임 창(HUD 포함)을 그대로 쓴다</summary>
    static EditorWindow gameView; static int directCount, missCount;
    static void GameShot(Cut c, float t)
    {
        var p = Player();
        if (!c.framed && p) { c.gBase = p.transform.eulerAngles.y + c.gYaw; c.framed = true; }
        var tpc = UnityEngine.Object.FindAnyObjectByType<ThirdPersonCamera>();
        if (tpc)
        {
            typeof(ThirdPersonCamera).GetField("yaw", Any).SetValue(tpc, c.gBase + c.gDrift * t);
            typeof(ThirdPersonCamera).GetField("pitch", Any).SetValue(tpc, c.gPitch);
            tpc.distance = c.gDist;
        }
        // 게임 창을 매 프레임 다시 그리게(편집기가 뒤에 있으면 게임 창이 멈춰 프레임 끝 캡처가 오지 않았다)
        gameView ??= EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"), false, null, false);
        if (gameView) gameView.Repaint();
        var shot = FilmGrabber.Last;
        if (!shot || FilmGrabber.LastFrame < Time.frameCount - 2) directCount++; // 새로 뜬 게 없으면 지난 프레임을 그대로
        if (shot && shot.width == W && shot.height == H) scenePx = shot.GetPixels32();
        else { if (scenePx == null || scenePx.Length != W * H) scenePx = new Color32[W * H]; missCount++; if (missCount % 30 == 1) Debug.LogWarning($"[필름] 게임 창 크기 {(shot ? shot.width + "x" + shot.height : "없음")}"); }
        if (frame % 60 == 0) Debug.Log($"[필름] 게임 화면 {c.name} 프레임 {frame}: 프레임 끝 캡처 {FilmGrabber.LastFrame}/{Time.frameCount}, 지난 프레임 다시 씀 {directCount}, 못 찍음 {missCount}");
    }

    /// <summary>카메라와 보는 곳 사이에 벽 · 상자가 있으면 그 앞으로 당긴다(사람 몸은 무시 — 벽 속에서 찍히던 컷)</summary>
    static readonly RaycastHit[] camHits = new RaycastHit[16];
    static Vector3 Unblocked(Vector3 look, Vector3 want)
    {
        var d = want - look; float len = d.magnitude; if (len < 0.05f) return want;
        int n = Physics.SphereCastNonAlloc(look, 0.18f, d / len, camHits, len, ~0, QueryTriggerInteraction.Ignore);
        float best = len;
        for (int i = 0; i < n; i++)
        {
            var h = camHits[i]; if (h.distance <= 0f) continue;
            if (h.collider.GetComponentInParent<Health>() || h.rigidbody) continue;
            best = Mathf.Min(best, h.distance - 0.12f);
        }
        return look + d / len * Mathf.Max(0.6f, best);
    }

    /// <summary>카메라와 인물 가슴 사이를 다른 사람이 가리는가</summary>
    static bool Occluded(Vector3 from, Vector3 to, Transform subject)
    {
        foreach (var col in Physics.OverlapSphere(from, 0.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            var o = col.GetComponentInParent<Health>();
            if (o && subject && !o.transform.IsChildOf(subject) && !subject.IsChildOf(o.transform)) return true;
        }
        var d = to - from; float len = d.magnitude; if (len < 0.1f) return false;
        int n = Physics.SphereCastNonAlloc(from, 0.2f, d / len, camHits, len - 0.3f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = camHits[i].collider.GetComponentInParent<Health>();
            if (h && subject && !h.transform.IsChildOf(subject) && !subject.IsChildOf(h.transform)) return true;
        }
        return false;
    }

    static Vector3 LookPoint(Transform t)
    {
        var an = t.GetComponentInChildren<Animator>();
        if (an && an.isHuman) { var ch = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.Spine); if (ch) return ch.position + Vector3.up * 0.15f; }
        return t.position + Vector3.up * 1.35f;
    }

    // ======================== 장면 손질 ========================

    /// <summary>장면을 새로 불렀으면 한 번: 이야기 감독을 멈추고(컷신 · 대사 · 잠금이 끼어들지 않게), 노래는 끄고, 되감을 테이프를 넉넉히</summary>
    static void Prepare()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene != preparedScene)
        {
            preparedScene = scene;
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb && mb.GetType().Name.EndsWith("Director")) { mb.StopAllCoroutines(); mb.enabled = false; }
            var w = Walkman();
            if (w) { w.musicVolume = 0f; for (int i = 0; i < 3; i++) w.AddTape(new CassetteTape { title = "오래된 믹스테이프", playable = false, charges = CassetteTape.MaxCharges }); }
        }
        StoryState.CutsceneActive = StoryState.ControlLocked = StoryState.CombatLocked = StoryState.RewindLocked = StoryState.WalkmanStowed = StoryState.HoldTime = StoryState.MenuOpen = false;
        StoryState.ClearLine(); StoryState.Title = null; StoryState.Fade = 0f; StoryState.Prompt = null; StoryState.Objective = null;
        Time.timeScale = 1f;
        var p = Player(); if (p) { var h = p.GetComponent<Health>(); h.Invulnerable = true; h.SetCurrent(h.Max); h.floor = 1; } // 구르기가 무적을 풀어도 대본의 한 방 전에는 쓰러지지 않게
        foreach (var ps in PatrolSight.All) ps.CanDetect = false;
    }

    static PlayerController Player() => UnityEngine.Object.FindAnyObjectByType<PlayerController>();
    static Walkman Walkman() => UnityEngine.Object.FindAnyObjectByType<Walkman>();
    static EnemyAI Enemy(string n) => UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == n);
    static Transform Who(string n) => n == null ? null : n == "player" ? Player()?.transform : Enemy(n)?.transform;
    const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    static void Place(Vector3 at, Vector3 lookAt) { var p = Player(); if (p) p.Teleport(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(lookAt - at, Vector3.up))); }

    static void PlaceGround(Vector3 at, Vector3 lookAt)
    {
        // at.y 근처에서 아래로(높이 60에서 쏘면 송신탑 발판 위에 올라섰다)
        if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 0.02f;
        Place(at, new Vector3(lookAt.x, at.y, lookAt.z));
    }

    static void Stand(EnemyAI e, Vector3 at, Vector3 facing)
    {
        if (!e) return;
        e.gameObject.SetActive(true); e.Deactivate();
        var ps = e.GetComponent<PatrolSight>(); if (ps) { ps.Holding = true; ps.Staged = false; ps.CanDetect = false; }
        e.PlaceAt(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up)));
    }

    static void Fighter(string name, Vector3 at)
    {
        var e = Enemy(name); if (!e) { Debug.LogWarning("[필름] 적 없음 " + name); return; }
        var p = Player();
        e.gameObject.SetActive(true);
        var ps = e.GetComponent<PatrolSight>(); if (ps) { ps.Holding = false; ps.Staged = false; }
        e.PlaceAt(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(p.transform.position - at, Vector3.up)));
        e.Activate();
    }

    /// <summary>레이 둘레(앞 · 오른쪽 · 왼쪽 뒤)에 덤비는 적. facing = 레이가 보는 쪽</summary>
    static void Fighters(Vector3 at, Vector3 facing, params string[] names)
    {
        var f = facing.normalized; var r = Vector3.Cross(Vector3.up, f);
        var offs = new[] { f * 2.0f + r * 0.2f, r * 2.1f - f * 0.3f, r * 1.4f + f * 1.6f };
        for (int i = 0; i < names.Length && i < offs.Length; i++) Fighter(names[i], at + offs[i] + Vector3.up * 0.02f);
    }

    static void Attack(int step)
    {
        var p = Player();
        var target = NearestEnemy(p.transform.position, 12f);
        if (!target) return;
        float d = Vector3.ProjectOnPlane(target.transform.position - p.transform.position, Vector3.up).magnitude;
        if (d > 2.4f) { Chase(target); return; }
        StopChase();
        Vector3 to = Vector3.ProjectOnPlane(target.transform.position - p.transform.position, Vector3.up);
        if (to.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.LookRotation(to);
        typeof(PlayerController).GetMethod("BeginAttack", Any).Invoke(p, new object[] { step, Vector3.zero });
    }

    static void Attacks(Cut c, params (float at, int step)[] list) { foreach (var (at, i) in list) c.At(at, () => Attack(i)); }

    static EnemyAI NearestEnemy(Vector3 from, float max)
    {
        EnemyAI best = null; float bd = max;
        foreach (var e in EnemyAI.All)
        {
            if (!e || !e.isActiveAndEnabled || e.Health.IsDead) continue;
            float d = Vector3.ProjectOnPlane(e.transform.position - from, Vector3.up).magnitude;
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    static EnemyAI chaseTarget;
    static void Chase(EnemyAI e) { chaseTarget = e; Walk(e.transform.position - Player().transform.position, 6.5f); }
    static void StopChase() { if (chaseTarget) { chaseTarget = null; StopWalk(); } }
    static void UpdateChase()
    {
        if (!chaseTarget) return;
        var p = Player(); if (!p) return;
        if (chaseTarget.Health.IsDead) { StopChase(); return; }
        Vector3 to = Vector3.ProjectOnPlane(chaseTarget.transform.position - p.transform.position, Vector3.up);
        if (to.magnitude < 1.6f) { StopChase(); return; }
        p.scriptedVelocity = to.normalized * 6.5f;
    }

    static void Walk(Vector3 dir, float speed) { StoryState.ControlLocked = true; Player().scriptedVelocity = Vector3.ProjectOnPlane(dir, Vector3.up).normalized * speed; }
    static void StopWalk() { var p = Player(); if (p) p.scriptedVelocity = Vector3.zero; StoryState.ControlLocked = false; }

    static void Dodge(float degrees)
    {
        var p = Player();
        typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(p, new object[] { Quaternion.Euler(0f, degrees, 0f) * p.transform.forward });
    }

    static void Choke(string name)
    {
        var e = Enemy(name); var td = Player().GetComponent<Takedown>();
        if (!e || !td) return;
        td.StartCoroutine((IEnumerator)typeof(Takedown).GetMethod("Perform", Any).Invoke(td, new object[] { e }));
    }

    static void SlowMo(float scale) => Time.timeScale = scale;

    // ---- 회피 반격: 적의 주먹이 닿기 0.12초 전에 뒤로 구르고(완벽 회피), 적이 휘청이면 바로 반격, 이어서 두 대 ----
    static bool counterArmed, counterDodged; static int perfectFrame = -1, counterFrame = -1, landedFrame = -1;
    static void ArmCounter()
    {
        counterArmed = true; counterDodged = false; perfectFrame = counterFrame = landedFrame = -1;
        EnemyAI.PerfectDodged -= OnPerfectDodge; EnemyAI.PerfectDodged += OnPerfectDodge;
        PlayerController.CounterLanded -= OnCounterLanded; PlayerController.CounterLanded += OnCounterLanded;
    }
    static void OnCounterLanded() { if (counterArmed && landedFrame < 0) { landedFrame = Time.frameCount; Debug.Log("[필름] 반격 적중"); } }
    static void OnPerfectDodge(EnemyAI e) { if (counterArmed && perfectFrame < 0) { perfectFrame = Time.frameCount; Debug.Log("[필름] 완벽 회피: " + e.name); } }
    static void UpdateCounter()
    {
        if (!counterArmed) return;
        var p = Player(); if (!p) return;
        // 느린 화면을 매 프레임 다시 건다(게임의 완벽 회피 연출이 0.35초 뒤 1배로 돌려놓는다)
        if (counterDodged)
        {
            float slow = landedFrame > 0 ? (Time.frameCount < landedFrame + 18 ? 0.45f : 1f) : counterFrame > 0 ? 0.5f : 0.3f;
            Time.timeScale = slow;
        }
        if (counterFrame > 0)
        {
            // 반격 돌려차기가 다 들어간 뒤에 이어서 두 대(전에는 이어 치기가 반격을 끊을 수 있었다)
            if (landedFrame > 0 && Time.frameCount == landedFrame + 26) Attack(0);
            if (landedFrame > 0 && Time.frameCount == landedFrame + 38) { Attack(1); counterArmed = false; Time.timeScale = 1f; }
            return;
        }
        if (perfectFrame > 0)
        {
            if (Time.frameCount < perfectFrame + 12) return; // 휘청이는 적을 느리게 조금 보여 주고
            bool ok = (bool)typeof(PlayerController).GetMethod("TryCounter", Any).Invoke(p, null);
            Debug.Log("[필름] 회피 반격 " + ok);
            counterFrame = Time.frameCount;
            return;
        }
        if (counterDodged) return;
        foreach (var e in EnemyAI.All)
        {
            if (!e || !e.isActiveAndEnabled || e.Health.IsDead) continue;
            float w = e.WindupLeft;
            Vector3 to = Vector3.ProjectOnPlane(e.transform.position - p.transform.position, Vector3.up);
            if (w > 0f && w <= 0.12f && to.magnitude < 3f)
            {
                counterDodged = true;
                typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(p, new object[] { -to.normalized });
                return;
            }
        }
    }
    /// <summary>부두에 서 있던 1장 반장 · 부두 조직원: 되감기가 다시 켜 놓아 받아치는 컷에 걸어 들어왔다</summary>
    static void HideBoss() { foreach (var n in new[] { "Boss_Cleaner", "Thug_Pier_C", "Thug_Pier_D" }) { var x = Enemy(n); if (x) x.gameObject.SetActive(false); } }

    static void Ability(TapeGenre g)
    {
        var p = Player(); var ab = p.GetComponent<TapeAbility>() ?? p.gameObject.AddComponent<TapeAbility>();
        ab.Activate(g);
    }

    /// <summary>장르 테이프를 끼운다(HUD 카세트 · R → Q/E 고르기가 그 장르로)</summary>
    static void InsertTape(string title, string music)
    {
        var w = Walkman(); if (!w) return;
        var tape = w.tapes.FirstOrDefault(x => x.title == title);
        if (tape == null) { tape = new CassetteTape { title = title, musicId = music, playable = true, charges = CassetteTape.MaxCharges }; w.AddTape(tape); }
        tape.charges = CassetteTape.MaxCharges;
        w.EquipIndex(w.tapes.IndexOf(tape));
    }

    /// <summary>게임에서 R을 누른 것과 같이: 시간이 느려지고 Q 되감기 / E 능력 고르기가 뜬다</summary>
    static void OpenChoice() { var w = Walkman(); if (w) typeof(Walkman).GetMethod("TryOpenChoice", Any).Invoke(w, null); }

    static void EndAbility() { var ab = UnityEngine.Object.FindAnyObjectByType<TapeAbility>(); if (ab) ab.EndNow(); Time.timeScale = 1f; }

    static void Lights(string name) { foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (l.name == name) l.enabled = true; }
    static void Dim(string name, float k) { foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (l.name == name || l.transform.parent && l.transform.parent.name == name) l.intensity *= k; }

    static Bounds BoundsOf(GameObject g)
    {
        var rs = g.GetComponentsInChildren<Renderer>(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>소품을 아래 면(종이 · 책상)에 내려놓는다: 떠 있으면 가까이서 틈이 보였다</summary>
    static void SitOnSurface(GameObject g)
    {
        var b = BoundsOf(g);
        var hits = Physics.RaycastAll(new Vector3(b.center.x, b.max.y + 0.05f, b.center.z), Vector3.down, 1.0f, ~0, QueryTriggerInteraction.Ignore);
        float top = float.MinValue;
        foreach (var h in hits) if (!h.transform.IsChildOf(g.transform) && h.point.y < b.min.y + 0.01f) top = Mathf.Max(top, h.point.y);
        if (top > float.MinValue) g.transform.position += Vector3.down * (b.min.y - top - 0.001f);
    }

    /// <summary>레이 얼굴: 찌푸린 표정, 카메라는 머리 기준 앞 오른쪽에서 조금씩 다가간다</summary>
    static void Face(Cut c)
    {
        var p = Player(); if (!p) return;
        var fe = p.GetComponentInChildren<FaceExpression>(); if (fe) fe.enabled = false;
        var smr = p.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("Blink_L") >= 0);
        if (smr) { void Set(string n, float v) { int i = smr.sharedMesh.GetBlendShapeIndex(n); if (i >= 0) smr.SetBlendShapeWeight(i, v * 100f); } Set("BrowDown", 0.9f); Set("MouthPress", 0.6f); Set("Squint", 0.35f); }
        var head = p.animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 ctr = head.position + Vector3.up * 0.06f, f = p.transform.forward, r = p.transform.right;
        c.from = ctr + r * 0.30f + f * 1.05f + Vector3.up * 0.02f; c.to = ctr + r * 0.12f + f * 0.82f; c.lookFrom = c.lookTo = ctr;
    }

    // ======================== 소리 ========================

    static void BeginEncoder()
    {
        string path = Path.Combine(Out, OutName);
        if (File.Exists(path)) File.Delete(path);
        var v = new VideoTrackAttributes { frameRate = new MediaRational(Fps), width = W, height = H, includeAlpha = false, bitRateMode = VideoBitrateMode.High };
        var au = new AudioTrackAttributes { sampleRate = new MediaRational(Rate), channelCount = 2, language = "ko" };
        encoder = new MediaEncoder(path, v, au);
        mix = MixAudio(); mixCursor = 0;
    }

    static float[] MixAudio()
    {
        float len = cuts.Last().start + cuts.Last().dur;
        int n = FrameOf(len) * (Rate / Fps) * 2;
        var m = new float[n];
        var music = Load(MusicPath);
        if (music != null)
            for (int i = 0; i < n; i += 2)
            {
                float t = i / 2f / Rate; float g = MusicGain(t); if (g <= 0f) continue;
                int src = Mathf.RoundToInt((MusicTrackAt + (t - MusicStartT)) * Rate) * 2;
                if (src + 1 < music.Length) { m[i] += music[src] * g; m[i + 1] += music[src + 1] * g; }
            }
        foreach (var (t, sfx, gain) in SfxList())
        {
            if (t < 0f) continue;
            var fx = Load(SfxPath(sfx)); if (fx == null) continue;
            int o = Mathf.RoundToInt(t * Rate) * 2, skip = Mathf.RoundToInt(SfxFrom(sfx) * Rate) * 2;
            for (int i = skip; i < fx.Length && o + i - skip < n; i++) m[o + i - skip] += fx[i] * gain;
        }
        return m;
    }

    static void PushAudio(bool gameSound)
    {
        int n = Rate / Fps * 2;
        var arr = new float[n];
        for (int i = 0; i < n; i++) arr[i] = mixCursor + i < mix.Length ? mix[mixCursor + i] : 0f;
        mixCursor += n;
        if (gameSound)
        {
            int count = AudioRenderer.GetSampleCountForCaptureFrame();
            int ch = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            if (count > 0)
            {
                using var game = new NativeArray<float>(count * ch, Allocator.Temp);
                if (AudioRenderer.Render(game) && ch == 2)
                    for (int i = 0; i < Mathf.Min(n, game.Length); i++) arr[i] += game[i] * 0.9f;
            }
        }
        for (int i = 0; i < n; i++) arr[i] = Mathf.Clamp(arr[i], -1f, 1f);
        using var buf = new NativeArray<float>(arr, Allocator.Temp);
        encoder.AddSamples(buf);
    }

    static void DiscardGameAudio()
    {
        int count = AudioRenderer.GetSampleCountForCaptureFrame();
        if (count <= 0) return;
        using var game = new NativeArray<float>(count * 2, Allocator.Temp);
        AudioRenderer.Render(game);
    }

    static AudioClip Clip(string path) => path.StartsWith("Assets/") ? AssetDatabase.LoadAssetAtPath<AudioClip>(path) : Resources.Load<AudioClip>(path);

    static float[] Load(string resource)
    {
        var clip = Clip(resource);
        if (!clip) { Debug.LogWarning("[필름] 소리 없음 " + resource); return null; }
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) { Debug.LogWarning("[필름] 소리 못 읽음 " + resource); return null; }
        int ch = clip.channels, freq = clip.frequency;
        int outLen = Mathf.FloorToInt(clip.samples * (Rate / (float)freq));
        var res = new float[outLen * 2];
        for (int i = 0; i < outLen; i++)
        {
            float s = i * (freq / (float)Rate); int i0 = Mathf.Min((int)s, clip.samples - 1), i1 = Mathf.Min(i0 + 1, clip.samples - 1); float f = s - i0;
            for (int c = 0; c < 2; c++) { int sc = Mathf.Min(c, ch - 1); res[i * 2 + c] = Mathf.Lerp(data[i0 * ch + sc], data[i1 * ch + sc], f); }
        }
        return res;
    }

    // ======================== 끝 ========================

    static void Finish()
    {
        EditorApplication.update -= Tick;
        StopCapture(); Time.timeScale = 1f;
        if (encoder != null) { encoder.Dispose(); encoder = null; }
        Cleanup();
        RestoreGameView();
        Status = preview ? "미리보기 끝" : "끝";
        Debug.Log("[필름] " + Status);
    }

    static void RestoreGameView()
    {
        if (!intro) return;
        var gv = Type.GetType("UnityEditor.GameView,UnityEditor");
        var win = EditorWindow.GetWindow(gv, false, null, false);
        gv.GetMethod("SizeSelectionCallback", Any)?.Invoke(win, new object[] { 0, null });
    }

    static void Abort(string why) { EditorApplication.update -= Tick; RestoreGameView(); Time.captureFramerate = 0; if (audioOn) { AudioRenderer.Stop(); audioOn = false; } if (encoder != null) { encoder.Dispose(); encoder = null; } Status = "중단: " + why; }

    static void Cleanup()
    {
        foreach (var n in new[] { "FilmCamera", "FilmOverlayCamera", "FilmCanvas", "FilmVolume", "FilmGrabber" }) { var g = GameObject.Find(n); if (g) UnityEngine.Object.Destroy(g); }
        if (rt) rt.Release(); if (uiRt) uiRt.Release();
    }
}
