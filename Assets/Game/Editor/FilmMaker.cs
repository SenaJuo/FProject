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
public static class FilmMaker
{
    const int W = 1920, H = 1080, Fps = 30;
    const string Out = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\trailer";
    const string MusicPath = "Music/night_drive-2";
    const float Bpm = 139.38f, MusicStartT = 3.2f, MusicTrackAt = 82.83f; // 트레일러 3.2초 = 곡 82.83초(박)
    static float Beat => 60f / Bpm;
    static float B(float n) => MusicStartT + n * Beat;
    static float DeathT => B(60);
    static float DropT => B(79);
    static float EndT => B(127);

    public class Cut
    {
        public string scene, name;
        public float start, dur, preroll;
        public bool cont, black, logo;
        // 카메라: rig = 대상에게 붙는다(오프셋은 컷 시작 때 대상의 오른쪽 · 위 · 앞), 아니면 세계 좌표 from → to
        public bool rig = true; public string anchor, second; public Vector3 rigFrom, rigTo;
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
    static Image fade, logoImg, capBar, keyBox; static Text capTitle, capBody, keyText, keyLabel, bigText, subText; static Shadow bigShadow;
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
            h.Invulnerable = false; typeof(Health).GetField("invulnerableUntil", Any)?.SetValue(h, 0f);
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

    // ======================== 설명 ========================
    // 0 = 아래 띠(왼쪽, 제목 + 한 줄), 1 = 조작 키(아래 띠 오른쪽), 2 = 가운데 큰 글씨, 3 = 로고 밑
    struct Cap { public float t0, t1; public int kind; public string a, b; public Color col; }
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

    /// <summary>false면 설명 없이(포트폴리오 사진용)</summary>
    public static bool ShowCaptions = true;

    static float CapAlpha(Cap c, float t) => Mathf.Clamp01((t - c.t0) / 0.22f) * Mathf.Clamp01((c.t1 - t) / 0.22f);

    static void UpdateCaptions(float t)
    {
        Cap? low = null, key = null, big = null, sub = null;
        foreach (var c in caps)
        {
            if (!ShowCaptions) break;
            if (t < c.t0 || t > c.t1) continue;
            if (c.kind == 0) low = c; else if (c.kind == 1) key = c; else if (c.kind == 2) big = c; else sub = c;
        }
        float la = low.HasValue ? CapAlpha(low.Value, t) : 0f;
        if (low.HasValue) { capTitle.text = low.Value.a; capBody.text = low.Value.b; capBar.color = WithA(low.Value.col, la); capTitle.color = WithA(low.Value.col, la); }
        else { capBar.color = capTitle.color = Color.clear; }
        capBody.color = WithA(new Color(0.86f, 0.84f, 0.80f), la);

        float ka = key.HasValue ? CapAlpha(key.Value, t) : 0f;
        if (key.HasValue)
        {
            keyText.text = key.Value.a; keyLabel.text = key.Value.b;
            // 오른쪽 끝에 맞춘다: [키] 설명
            float lw = keyLabel.preferredWidth, kw = Mathf.Max(64f, keyText.preferredWidth + 34f);
            var lr = keyLabel.rectTransform; lr.sizeDelta = new Vector2(lw + 4f, 50f); lr.anchoredPosition = new Vector2(-120f - lw / 2f, 69f);
            var kr = keyBox.rectTransform; kr.sizeDelta = new Vector2(kw, 50f); kr.anchoredPosition = new Vector2(-120f - lw - 18f - kw / 2f, 69f);
        }
        keyBox.color = WithA(new Color(0.93f, 0.89f, 0.80f), ka); keyText.color = WithA(new Color(0.08f, 0.07f, 0.06f), ka); keyLabel.color = WithA(Color.white, ka);

        float ba = big.HasValue ? CapAlpha(big.Value, t) : 0f;
        if (big.HasValue) bigText.text = big.Value.a;
        bigText.color = WithA(Color.white, ba); bigShadow.effectColor = new Color(0, 0, 0, 0.85f * ba);

        float sa = sub.HasValue ? CapAlpha(sub.Value, t) : 0f;
        if (sub.HasValue) subText.text = sub.Value.a;
        subText.color = WithA(Paper, sa);
    }

    static Color WithA(Color c, float a) => new(c.r, c.g, c.b, a);

    /// <summary>음악 크기(트레일러 시각 t): 3.2초에 들어와, 죽는 박에 가라앉았다가, 다시 터지는 박에 돌아오고, 로고 박에 끊긴다</summary>
    static float MusicGain(float t)
    {
        if (t < MusicStartT || t >= EndT + 0.06f) return 0f;
        float g = Mathf.Clamp01((t - MusicStartT) / 0.12f);
        if (t >= DeathT && t < DropT) g *= Mathf.Lerp(1f, 0.28f, Mathf.Clamp01((t - DeathT) / 0.3f));
        if (t >= EndT) g *= 1f - (t - EndT) / 0.06f;
        return g * 0.62f;
    }

    static List<(float t, string sfx, float gain)> SfxList() => new()
    {
        (2.78f, "World/ButtonClick", 0.9f),
        (B(49.6f), "World/SniperShot", 0.75f),
        (DeathT - 0.04f, "World/Gunshot", 0.9f),
        (DropT, "Hit/impact_heavy-1", 0.7f),
        (EndT, "World/TapeStop", 1.0f),
    };

    // ======================== 시작 ========================

    public static void Make(bool previewOnly)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 중에만");
        preview = previewOnly;
        Directory.CreateDirectory(Out);
        foreach (var f in Directory.GetFiles(Out, "film_*.jpg")) File.Delete(f);
        Rate = AudioSettings.outputSampleRate;
        cuts = Build(); caps = Captions();
        index = -1; stage = -1; audioOn = false;
        Status = "소리 불러오는 중";
        SetupCamera();
        pending = new List<AudioClip>();
        foreach (var n in new[] { MusicPath }.Concat(SfxList().Select(x => "Sfx/" + x.sfx)))
        { var c = Resources.Load<AudioClip>(n); if (c) { c.LoadAudioData(); pending.Add(c); } }
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
        Rect01("BarTop", new Vector2(0, 1 - bar), Vector2.one, Color.black);
        Rect01("BarBottom", Vector2.zero, new Vector2(1, bar), Color.black);
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
    }

    static Cut Current => cuts[index];
    static int FrameOf(float t) => Mathf.RoundToInt(t * Fps);

    static void NextCut()
    {
        index++;
        if (index >= cuts.Count) { Finish(); return; }
        var c = Current;
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
        if (!cut.black) UpdateChase();

        if (frame >= 0)
        {
            float t = total > 1 ? frame / (float)(total - 1) : 0f;
            Render(cut, t, now);
            if (frame == total / 6 || frame == total / 2 || frame == total * 5 / 6) // 확인용 사진(녹화할 때도)
                File.WriteAllBytes(Path.Combine(Out, $"film_{index:00}_{cut.name}_{frame * 10 / Mathf.Max(1, total)}.jpg"), frameTex.EncodeToJPG(82));
        }
        else if (audioOn) DiscardGameAudio();
        frame++;
        if (frame >= total)
        {
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
        fade.color = new Color(0, 0, 0, c.black && !c.logo ? 1f : a);
        logoImg.color = new Color(1, 1, 1, c.logo ? Mathf.Clamp01((sec - 0.35f) / 0.9f) * Mathf.Clamp01((c.dur - sec) / 0.7f) : 0f);
        UpdateCaptions(c.start + sec);

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
        uiCam.Render();
        RenderTexture.active = uiRt; uiTex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
        scenePx = frameTex.GetPixels32(); uiPx = uiTex.GetPixels32();
        for (int i = 0; i < scenePx.Length; i++)
        {
            var u = uiPx[i]; if (u.a == 0) continue;
            int k = 255 - u.a; var s = scenePx[i];
            scenePx[i] = new Color32((byte)Mathf.Min(255, u.r + s.r * k / 255), (byte)Mathf.Min(255, u.g + s.g * k / 255), (byte)Mathf.Min(255, u.b + s.b * k / 255), 255);
        }
        frameTex.SetPixels32(scenePx);
        if (!preview) { encoder.AddFrame(frameTex); PushAudio(!c.black && audioOn); }
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
            if (w) { w.musicVolume = 0f; for (int i = 0; i < 3; i++) w.AddTape(new CassetteTape { title = "필름", playable = false, charges = CassetteTape.MaxCharges }); }
        }
        StoryState.CutsceneActive = StoryState.ControlLocked = StoryState.CombatLocked = StoryState.RewindLocked = StoryState.WalkmanStowed = StoryState.HoldTime = StoryState.MenuOpen = false;
        StoryState.ClearLine(); StoryState.Title = null; StoryState.Fade = 0f; StoryState.Prompt = null; StoryState.Objective = null;
        Time.timeScale = 1f;
        var p = Player(); if (p) { p.GetComponent<Health>().Invulnerable = true; var h = p.GetComponent<Health>(); h.SetCurrent(h.Max); }
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
    /// <summary>부두에 서 있던 1장 반장 · 부두 조직원: 되감기가 다시 켜 놓아 받아치는 컷에 걸어 들어왔다</summary>
    static void HideBoss() { foreach (var n in new[] { "Boss_Cleaner", "Thug_Pier_C", "Thug_Pier_D" }) { var x = Enemy(n); if (x) x.gameObject.SetActive(false); } }

    static void Ability(TapeGenre g)
    {
        var p = Player(); var ab = p.GetComponent<TapeAbility>() ?? p.gameObject.AddComponent<TapeAbility>();
        ab.Activate(g);
    }

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
        string path = Path.Combine(Out, "REWIND_film.mp4");
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
            var fx = Load("Sfx/" + sfx); if (fx == null) continue;
            int o = Mathf.RoundToInt(t * Rate) * 2;
            for (int i = 0; i < fx.Length && o + i < n; i++) m[o + i] += fx[i] * gain;
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

    static float[] Load(string resource)
    {
        var clip = Resources.Load<AudioClip>(resource);
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
        Status = preview ? "미리보기 끝" : "끝";
        Debug.Log("[필름] " + Status);
    }

    static void Abort(string why) { EditorApplication.update -= Tick; Time.captureFramerate = 0; if (audioOn) { AudioRenderer.Stop(); audioOn = false; } if (encoder != null) { encoder.Dispose(); encoder = null; } Status = "중단: " + why; }

    static void Cleanup()
    {
        foreach (var n in new[] { "FilmCamera", "FilmOverlayCamera", "FilmCanvas", "FilmVolume" }) { var g = GameObject.Find(n); if (g) UnityEngine.Object.Destroy(g); }
        if (rt) rt.Release(); if (uiRt) uiRt.Release();
    }
}
