using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 트레일러 만들기(2026-10-16 "기깔나게 영화 같은 트레일러"): 플레이 중에 장면을 차례로 불러 영화식 카메라 움직임으로 찍는다.
/// 2.39:1 검은 띠 · 자막(Noto Sans KR) · 검은 화면 전환 · 타이틀 곡 + 효과음을 섞어 Unity 내장 인코더(MediaEncoder)로 MP4.
/// 결과: QA/trailer/REWIND_trailer.mp4 (미리보기 모드는 컷마다 사진 한 장: QA/trailer/preview_*.png)
/// MakePlay(2026-10-19 "20초 정도 플레이 영상 중심"): 1장에서 실제 게임을 돌려 찍는다 — 몰래 제압 · 맨손 연타 · 되감기 · 네온 러시.
/// 레이의 동작은 스크립트가 정한 때에 직접 일으키고(공격 · 구르기 · 제압 · 되감기 · 능력), 화면은 게임 카메라 그대로, 소리는 게임 소리(AudioRenderer) + 타이틀 곡.
/// 결과: QA/trailer/REWIND_gameplay.mp4
/// </summary>
public static class TrailerMaker
{
    const int W = 1920, H = 1080, Fps = 30, Rate = 48000;
    const string Out = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\trailer";

    public class Shot
    {
        public string scene, caption, sub;
        public float seconds = 3f, fov = 40f, fadeIn, fadeOut, captionAt = 0.4f, rewind;
        public Vector3 from, to, lookFrom, lookTo;
        public Action setup;                 // 장면을 불러 둔 뒤 한 번(사람 배치 · 깨우기)
        public Action<float> each;           // 매 프레임(0~1)
        public bool black, logo;             // 검은 화면(자막만) · 마지막 로고
        public string sfx; public float sfxAt; public float sfxGain = 1f;
        // 플레이 영상: 게임 카메라로 찍는다(각도는 레이 기준 yaw · pitch · 거리), 정한 때에 레이의 동작을 일으킨다
        public bool gameplay; public float camYawOff, camPitch = 12f, camDist = 4.5f, camDrift, camYaw;
        public List<(float at, Action act)> cues = new();
    }

    static List<Shot> shots;
    static int index, frame, stage;
    static float settleUntil;
    static Camera cam;
    static RenderTexture rt;
    static Texture2D frameTex;
    static MediaEncoder encoder;
    static float[] audio; static int audioCursor;
    static Text caption, sub; static Image fade, logoImg; static CanvasGroup capGroup;
    static bool preview;
    static string musicName = "title-2"; static float musicStart = 13f; // 32초쯤 잠잠했다 34초부터 커진다 → 몽타주 시작(20.7초)에 맞춘다
    static float musicFadeIn = 2.5f, musicGain = 0.8f;
    static bool playVariant; static int cueIndex;
    static bool youtube;
    static string OutFile => youtube ? "REWIND_youtube.mp4" : playVariant ? "REWIND_gameplay.mp4" : "REWIND_trailer.mp4";
    public static string Status = "";

    // ---------------- 컷 구성 ----------------

    static List<Shot> Build()
    {
        Vector3 V(float x, float y, float z) => new(x, y, z);
        var list = new List<Shot>
        {
            // 1. 밤거리 — 대니의 집으로 천천히
            new Shot { scene = "Prologue", seconds = 5.5f, fadeIn = 1.2f, from = V(96, 1.6f, -9), to = V(101, 1.7f, 1), lookFrom = V(103, 2.6f, 12), lookTo = V(103, 2.2f, 12), fov = 38,
                       caption = "그날 밤,", sub = "친구가 죽었다.", captionAt = 1.4f },
            // 2. 대니의 방 — 쓰러진 친구와 조직원
            new Shot { scene = "Prologue", seconds = 4.2f, from = V(96.2f, 0.9f, 67.5f), to = V(97.4f, 0.8f, 68.4f), lookFrom = V(101, 0.4f, 70.5f), lookTo = V(101.5f, 0.6f, 70.8f), fov = 34,
                       setup = () => Show("Danny_Body", "Thug_Room_A", "Thug_Room_B") },
            // 3. 레이 — 얼굴 가까이(찌푸림)
            new Shot { scene = "Chapter1", seconds = 4.5f, fadeIn = 0.4f, fov = 22,
                       setup = () => Face(0.8f, new Vector3(0.35f, 0.04f, 0.95f), new Vector3(0.12f, 0.02f, 0.72f)),
                       caption = "남은 건", sub = "카세트 플레이어 하나.", captionAt = 1.0f },
            // 4. 되감기 — 대니의 집 앞에서 세상이 거꾸로(뒤로 빠지는 카메라)
            new Shot { scene = "Prologue", seconds = 4.0f, rewind = 1f, from = V(100.6f, 1.75f, 2.2f), to = V(99.5f, 2.2f, -2.5f), lookFrom = V(103, 1.5f, 11), lookTo = V(103, 1.6f, 11), fov = 42,
                       setup = () => Place(V(101.5f, 0.02f, 4f), V(103, 0, 12)),
                       caption = "시간을 되감는다.", captionAt = 0.8f, sfx = "World/TapeRewind", sfxAt = 0f },
            // 5. 검은 화면
            new Shot { black = true, seconds = 2.2f, caption = "단,", sub = "5초씩.", captionAt = 0.2f, sfx = "World/TapeStop", sfxAt = 0.05f },
            // 6. 1장 야시장 — 경찰 바리케이드
            new Shot { scene = "Chapter1", seconds = 3.0f, from = V(164, 1.2f, 136.5f), to = V(164, 1.3f, 139.5f), lookFrom = V(164, 2.4f, 151), lookTo = V(164, 2.5f, 151), fov = 38, // 간판이 위 검은 띠에 잘리지 않게
                       sfx = "Hit/impact_mid-1", sfxAt = 0f, sfxGain = 0.6f },
            // 7. 1장 부두 — 반장과 부하들에게 둘러싸인 레이
            new Shot { scene = "Chapter1", seconds = 3.6f, from = V(195.2f, 1.6f, 322.2f), to = V(195.9f, 1.6f, 323.3f), lookFrom = V(198.2f, 1.55f, 334), lookTo = V(198.3f, 1.5f, 334), fov = 32,
                       setup = () =>
                       {
                           Place(V(197.2f, 0.02f, 326.0f), V(197.2f, 0, 334));
                           Pose("Boss_Cleaner", V(197.2f, 0.02f, 327.6f), V(197.2f, 0, 320));
                           Pose("Thug_Pier_A", V(196.1f, 0.02f, 327.2f), V(197.2f, 0, 325.5f));
                           Pose("Thug_Pier_B", V(198.4f, 0.02f, 327.3f), V(197.2f, 0, 325.5f));
                       },
                       sfx = "Hit/impact_heavy-1", sfxAt = 3.0f },
            // 8. 2장 — 경찰서 주차장의 탐조등
            new Shot { scene = "Chapter2", seconds = 3.4f, from = V(30, 3.5f, 20), to = V(38, 4.3f, 23), lookFrom = V(78, 8, 50), lookTo = V(74, 5, 46), fov = 42,
                       caption = "경찰은 이미", sub = "그들의 편이었다.", captionAt = 0.5f },
            // 9. 3장 — 형광등 복도, 문틈의 실험체
            new Shot { scene = "Chapter3", seconds = 3.2f, from = V(308, 1.6f, 15), to = V(312, 1.6f, 15), lookFrom = V(330, 1.4f, 15), lookTo = V(330, 1.4f, 15), fov = 50,
                       setup = () => { Wake("Subject_A", "Subject_B", "Subject_C"); Lights("KeyLight"); } },
            // 10. 4장 — 정상의 파트너
            new Shot { scene = "Chapter4", seconds = 3.0f, from = V(-5.5f, 21.7f, 44.5f), to = V(-4.6f, 21.6f, 46), lookFrom = V(-1, 21.5f, 51), lookTo = V(-1, 21.6f, 51), fov = 34 },
            // 11. 4장 — 송신탑 올려다보기 → 저격
            new Shot { scene = "Chapter4", seconds = 2.8f, from = V(4, 21.5f, 38), to = V(4.5f, 21.3f, 39), lookFrom = V(16, 30, 59), lookTo = V(16, 52, 59), fov = 52,
                       sfx = "World/SniperShot", sfxAt = 2.55f },
            // 12. 5장 — 0번과 유리 관람석
            new Shot { scene = "Chapter5", seconds = 4.0f, from = V(420, 1.6f, 1), to = V(420, 1.5f, 6), lookFrom = V(420, 2.2f, 14), lookTo = V(420, 3.4f, 22), fov = 38,
                       caption = "윗선이 지켜보는 가운데", sub = "모든 걸 되돌릴 테이프가 있다.", captionAt = 0.6f },
            // 13. 레이 — 결심
            new Shot { scene = "Chapter1", seconds = 4.2f, fov = 22, fadeOut = 0.6f,
                       setup = () => Face(1f, new Vector3(-0.28f, 0f, 1.0f), new Vector3(-0.08f, 0.01f, 0.85f)),
                       caption = "죽어도 되감으면 된다.", sub = "— 단, 테이프가 남아 있다면.", captionAt = 0.5f },
            // 14. 로고
            new Shot { black = true, logo = true, seconds = 5.0f, sfx = "World/TapeRewind", sfxAt = 0.0f, sfxGain = 0.6f, sub = "카세트 누아르 액션", captionAt = 1.6f },
        };
        return list;
    }

    // ---------------- 플레이 영상(약 21초) ----------------

    static List<Shot> BuildPlay()
    {
        Vector3 V(float x, float y, float z) => new(x, y, z);
        var list = new List<Shot>();
        // 글자는 넣지 않는다(2026-10-19 "이상한 자막 다 빼고 싸우다가 장르별로 되감기 · 능력 쓰는 것만")

        // 1. 맨손 싸움: 부두 불빛 아래 셋을 상대로 연타 · 구르기 · 다시 연타
        var fight = new Shot { scene = "Chapter1", gameplay = true, seconds = 3.2f, fadeIn = 0.4f, camYawOff = 40f, camPitch = 7f, camDist = 2.8f, camDrift = -14f };
        fight.setup = () =>
        {
            Unscript();
            Place(V(199f, 0.02f, 311.5f), V(199f, 0f, 314f));
            Fighters(V(199f, 0f, 311.5f), "Thug_Dock_A", "Thug_Dock_B", "Thug_Dock_C");
        };
        Attacks(fight, (0.10f, 0), (0.50f, 1), (0.95f, 2), (2.15f, 0), (2.55f, 1), (2.95f, 2));
        fight.cues.Add((1.60f, () => Dodge(-90f)));
        list.Add(fight);

        // 2~4. 장르 테이프: R → (세상이 거의 멈춘 사이) E → 능력
        list.Add(Ability("rain_jazz", V(206f, 0.02f, 320f), new[] { "Thug_Pier_A", "Thug_Pier_B", "Thug_Yard_A" }, 3.8f, 55f,
            new[] { (1.55f, -90f), (2.45f, 90f) }, (0.10f, 0), (0.50f, 1), (1.95f, 2), (2.95f, 0), (3.30f, 1)));        // 재즈 「즉흥」: 구르고 받아친다
        list.Add(Ability("dock_soul", V(164f, 0.02f, 100f), new[] { "Thug_Lane", "Thug_Caller", "Thug_Spotter" }, 3.6f, -40f,
            new (float, float)[0], (0.10f, 0), (1.35f, 0), (1.70f, 1), (2.10f, 2), (2.70f, 1), (3.10f, 2)));            // 소울 「그루브」: 무겁게 친다
        // 4. 네온 러시: 켜자마자 적에게 내달린다(옆에서 찍어 형광 잔상이 꼬리처럼 보이게) → 느려진 적들 사이에서 연타
        var neon = new Shot { scene = "Chapter1", gameplay = true, seconds = 4.8f, camYawOff = 95f, camPitch = 6f, camDist = 3.6f, camDrift = -25f };
        Vector3 neonAt = V(136f, 0.02f, 178f);
        neon.setup = () =>
        {
            EndAbility();
            Place(neonAt + Vector3.back * 9f, neonAt);
            Fighters(neonAt + Vector3.back * 1.2f, "Thug_Market_A", "Thug_Market_B", "Thug_Road");
            UseTape("citypop_1");
        };
        neon.cues.Add((0.10f, OpenChoice));
        neon.cues.Add((0.45f, () => Pick(2))); // E
        neon.cues.Add((0.55f, () => Run(Vector3.forward, 7.5f)));
        neon.cues.Add((1.60f, StopRun));
        Attacks(neon, (1.65f, 0), (1.95f, 1), (2.30f, 2), (2.80f, 0), (3.10f, 1), (3.45f, 2), (3.95f, 0), (4.25f, 1));
        list.Add(neon);

        // 5. 내달려 한 놈을 쓰러뜨리고 → 크게 맞는다 → R → Q 되감기(이 컷만 천천히: 레이가 뒷걸음치고 쓰러진 놈이 일어서는 게 보이게) → 이번엔 피하고 받아친다
        var rewind = new Shot { scene = "Chapter1", gameplay = true, seconds = 7.6f, camYawOff = -60f, camPitch = 8f, camDist = 3.6f, camDrift = 6f };
        Vector3 rwAt = V(163f, 0.02f, 82f);
        rewind.setup = () =>
        {
            EndAbility();
            Place(rwAt + Vector3.back * 5f, rwAt);
            Fighters(rwAt + Vector3.back * 1.2f, "Thug_Exit_A", "Thug_Exit_B", "Thug_Yard_B");
            UseTape("lighthouse");
            var ph = Player().GetComponent<Health>(); ph.Invulnerable = false; ph.SetCurrent(ph.Max);
            var w = UnityEngine.Object.FindAnyObjectByType<Walkman>(); if (w) w.playbackSpeed = 1.6f; // 보통 4배속 → 이 컷만 천천히
            TimeRewind.Clear(); // 이 자리부터만 되감긴다
        };
        rewind.cues.Add((0.10f, () => Run(Vector3.forward, 6f)));
        rewind.cues.Add((0.75f, StopRun));
        Attacks(rewind, (0.80f, 0), (1.15f, 1), (1.55f, 2), (6.55f, 2), (7.00f, 0));
        rewind.cues.Add((2.30f, () => { var e = Enemy("Thug_Exit_B"); var ph = Player().GetComponent<Health>(); ph.TakeDamage(35, e ? e.transform.position : Player().transform.position + Vector3.right); }));
        rewind.cues.Add((2.75f, OpenChoice));
        rewind.cues.Add((3.15f, () => Pick(1))); // Q
        rewind.cues.Add((6.05f, () => { Player().GetComponent<Health>().Invulnerable = true; Dodge(180f); }));
        rewind.cues.Add((7.50f, () => { var w = UnityEngine.Object.FindAnyObjectByType<Walkman>(); if (w) w.playbackSpeed = 4f; }));
        list.Add(rewind);

        // 6. 로고(글자 없이)
        list.Add(new Shot { black = true, logo = true, seconds = 3.0f, sfx = "World/TapeStop", sfxAt = 0.05f, sfxGain = 0.8f });
        return list;
    }

    /// <summary>장르 테이프 컷: 그 장르 테이프를 끼우고 싸우다가 R(고르기 · 세상이 거의 멈춤) → E(능력)</summary>
    static Shot Ability(string musicId, Vector3 at, string[] enemies, float seconds, float yawOff, (float at, float deg)[] dodges, params (float at, int step)[] attacks)
    {
        var s = new Shot { scene = "Chapter1", gameplay = true, seconds = seconds, camYawOff = yawOff, camPitch = 7f, camDist = 3.0f, camDrift = 16f };
        s.setup = () =>
        {
            EndAbility();
            Place(at, at + Vector3.forward * 2f);
            Fighters(at, enemies);
            UseTape(musicId);
        };
        Attacks(s, attacks);
        foreach (var (t, deg) in dodges) s.cues.Add((t, () => Dodge(deg)));
        s.cues.Add((0.60f, OpenChoice));
        s.cues.Add((1.05f, () => Pick(2))); // E
        return s;
    }

    static void Attacks(Shot s, params (float at, int step)[] list)
    {
        foreach (var (at, i) in list) s.cues.Add((at, () => Attack(i)));
        s.cues.Sort((x, y) => x.at.CompareTo(y.at));
    }

    /// <summary>레이 둘레(앞 · 오른쪽 · 왼쪽 뒤)에 덤비는 적 셋</summary>
    static void Fighters(Vector3 at, params string[] names)
    {
        var offs = new[] { new Vector3(0f, 0f, 2.1f), new Vector3(2.2f, 0f, -0.3f), new Vector3(-2.1f, 0f, -0.9f) };
        for (int i = 0; i < names.Length && i < offs.Length; i++) Fighter(names[i], at + offs[i] + Vector3.up * 0.02f);
    }

    /// <summary>그 장르 테이프(횟수 3)를 끼운다</summary>
    static void UseTape(string musicId)
    {
        var w = UnityEngine.Object.FindAnyObjectByType<Walkman>(); if (!w) return;
        w.AddTape(new CassetteTape { title = musicId, musicId = musicId, playable = true, charges = CassetteTape.MaxCharges });
        w.Equip(w.tapes.Count - 1);
    }

    static void OpenChoice()
    {
        var w = UnityEngine.Object.FindAnyObjectByType<Walkman>();
        if (w) typeof(Walkman).GetMethod("TryOpenChoice", Any).Invoke(w, null);
    }

    /// <summary>레이를 그 방향(월드)으로 달리게 한다: 연출 이동(조작 잠금 동안 감독이 정한 속도로 움직이는 길)을 빌린다</summary>
    static void Run(Vector3 dir, float speed) { StoryState.ControlLocked = true; Player().scriptedVelocity = dir.normalized * speed; }
    static void StopRun() { Player().scriptedVelocity = Vector3.zero; StoryState.ControlLocked = false; }

    static void Pick(int pick) { var w = UnityEngine.Object.FindAnyObjectByType<Walkman>(); if (w) w.Choose(pick); }

    static void EndAbility() { var ab = UnityEngine.Object.FindAnyObjectByType<TapeAbility>(); if (ab) ab.EndNow(); Time.timeScale = 1f; }

    static PlayerController Player() => UnityEngine.Object.FindAnyObjectByType<PlayerController>();
    static EnemyAI Enemy(string name) => UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == name);
    const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    /// <summary>1장 이야기를 멈춘다(컷신 · 대사 · 잠금이 촬영에 끼어들지 않게). 카세트 노래는 끄고 되감을 테이프를 넉넉히</summary>
    static void Unscript()
    {
        var d = UnityEngine.Object.FindAnyObjectByType<Chapter1Director>();
        if (d)
        {
            d.StopAllCoroutines(); d.enabled = false;
            // 시작 장면의 연출 카메라가 켜져 있으면 게임 카메라로 돌린다
            if (d.gameplayCamera) typeof(Chapter1Director).GetMethod("Shot", Any).Invoke(d, new object[] { d.gameplayCamera, 0f });
        }
        StoryState.CutsceneActive = StoryState.ControlLocked = StoryState.CombatLocked = StoryState.RewindLocked = StoryState.WalkmanStowed = StoryState.HoldTime = false;
        StoryState.Objective = null; StoryState.Waypoint = null;
        var w = UnityEngine.Object.FindAnyObjectByType<Walkman>();
        if (w)
        {
            w.musicVolume = 0f; // 트레일러 곡과 겹치지 않게
            for (int i = 0; i < 3; i++) w.AddTape(new CassetteTape { title = "트레일러", playable = false, charges = CassetteTape.MaxCharges });
        }
    }

    /// <summary>적을 그 자리에 세운다(순찰 걸음을 멈추고, 아직 싸우지 않는다)</summary>
    static void Stand(EnemyAI e, Vector3 at, Vector3 facing)
    {
        if (!e) return;
        e.gameObject.SetActive(true);
        e.Deactivate();
        var ps = e.GetComponent<PatrolSight>(); if (ps) { ps.Holding = true; ps.Staged = false; ps.CanDetect = false; }
        e.PlaceAt(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up)));
    }

    /// <summary>레이를 노리고 덤비는 적으로 세운다</summary>
    static void Fighter(string name, Vector3 at)
    {
        var e = Enemy(name); if (!e) { Debug.LogWarning("[트레일러] 적 없음 " + name); return; }
        var p = Player();
        e.gameObject.SetActive(true);
        var ps = e.GetComponent<PatrolSight>(); if (ps) { ps.Holding = false; ps.Staged = false; }
        e.PlaceAt(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(p.transform.position - at, Vector3.up)));
        e.Activate();
    }

    /// <summary>주먹: 닿는 거리(약 2.4m — 달라붙는 거리 포함)에 적이 있을 때만 그쪽을 보고 친다.
    /// 없으면 허공에 휘두르지 않고 가장 가까운 적에게 달려간다(2026-10-19 "허공에 주먹질하는 장면이 너무 많다")</summary>
    static void Attack(int step)
    {
        var p = Player();
        var target = NearestEnemy(p.transform.position, out float d);
        if (!target) return;
        if (d > 2.4f) { Chase(target); return; }
        StopChase();
        Vector3 to = Vector3.ProjectOnPlane(target.transform.position - p.transform.position, Vector3.up);
        if (to.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.LookRotation(to);
        typeof(PlayerController).GetMethod("BeginAttack", Any).Invoke(p, new object[] { step, Vector3.zero });
    }

    static EnemyAI NearestEnemy(Vector3 from, out float distance)
    {
        EnemyAI best = null; distance = float.MaxValue;
        foreach (var e in EnemyAI.All)
        {
            if (!e || !e.isActiveAndEnabled || e.Health.IsDead) continue;
            float dd = Vector3.Distance(Vector3.ProjectOnPlane(e.transform.position - from, Vector3.up), Vector3.zero);
            if (dd < distance && dd < 12f) { distance = dd; best = e; }
        }
        return best;
    }

    static EnemyAI chaseTarget;
    static void Chase(EnemyAI e) { chaseTarget = e; Run(e.transform.position - Player().transform.position, 6.5f); }
    static void StopChase() { if (chaseTarget) { chaseTarget = null; StopRun(); } }

    /// <summary>매 프레임: 쫓는 중이면 방향을 고쳐 잡고, 닿을 거리면 멈춘다</summary>
    static void UpdateChase()
    {
        if (!chaseTarget) return;
        var p = Player();
        if (chaseTarget.Health.IsDead) { StopChase(); return; }
        Vector3 to = Vector3.ProjectOnPlane(chaseTarget.transform.position - p.transform.position, Vector3.up);
        if (to.magnitude < 1.6f) { StopChase(); return; }
        p.scriptedVelocity = to.normalized * 6.5f;
    }

    /// <summary>구르기: 레이 앞 기준 각도(도)로</summary>
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

    /// <summary>게임 카메라 각도: 촬영 시작 때 레이 방향 + 각도로 정하고, 컷 동안 천천히 돈다</summary>
    static void SetGameCamera(Shot s, float t)
    {
        var tpc = UnityEngine.Object.FindAnyObjectByType<ThirdPersonCamera>(); if (!tpc) return;
        typeof(ThirdPersonCamera).GetField("yaw", Any).SetValue(tpc, s.camYaw + s.camDrift * t);
        typeof(ThirdPersonCamera).GetField("pitch", Any).SetValue(tpc, s.camPitch);
        tpc.distance = s.camDist;
    }

    // ---------------- 시작 ----------------

    public static void Make(bool previewOnly)
    {
        playVariant = false; musicStart = 13f; musicFadeIn = 2.5f; musicGain = 0.8f;
        Start(previewOnly, Build());
    }

    /// <summary>20초쯤의 플레이 영상(1장)</summary>
    /// <summary>유튜브 올리기용: 플레이 영상과 같은 구성을 고화질로(QA/trailer/REWIND_youtube.mp4)</summary>
    public static void MakeYouTube() { MakePlay(false); youtube = true; }

    public static void MakePlay(bool previewOnly)
    {
        youtube = false;
        playVariant = true; musicStart = 34f; musicFadeIn = 0.4f; musicGain = 0.55f; // 34초부터 곡이 커진다 · 게임 소리가 묻히지 않게 조금 작게
        Start(previewOnly, BuildPlay());
    }

    static void Start(bool previewOnly, List<Shot> list)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 중에만");
        preview = previewOnly;
        Directory.CreateDirectory(Out);
        shots = list;
        index = -1; stage = -1;
        Status = "소리 불러오는 중";
        SetupCamera();
        Preload(); settleUntil = Time.realtimeSinceStartup + 30f;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    const int LogoLayer = 31, OverlayLayer = 30;
    static Camera uiCam;

    static RenderTexture uiRt; static Texture2D uiTex;
    static Color32[] scenePx, uiPx;

    /// <summary>장면 카메라(후처리 · 로고 빛 번짐)와 덧그림 카메라(검은 띠 · 자막 · 전환, 후처리 없음)를 따로 그려 CPU에서 겹친다.
    /// 한 카메라에 다 넣으면 되감기 효과가 자막과 검은 띠까지 휘게 했고, 덧그림 카메라를 같은 화면에 '지우지 않고' 그리면 URP가 바탕을 파랗게 덮었다</summary>
    static void SetupCamera()
    {
        rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        frameTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var go = new GameObject("TrailerCamera"); UnityEngine.Object.DontDestroyOnLoad(go);
        cam = go.AddComponent<Camera>(); cam.enabled = false; cam.nearClipPlane = 0.03f; cam.farClipPlane = 600f; cam.allowHDR = true; cam.targetTexture = rt;
        var add = go.AddComponent<UniversalAdditionalCameraData>(); add.renderPostProcessing = true; add.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var ugo = new GameObject("TrailerOverlayCamera"); UnityEngine.Object.DontDestroyOnLoad(ugo); ugo.transform.position = new Vector3(0, -2000, 0);
        uiCam = ugo.AddComponent<Camera>(); uiCam.enabled = false; uiCam.nearClipPlane = 0.01f; uiCam.farClipPlane = 10f; uiCam.allowHDR = false; uiCam.allowMSAA = false;
        uiRt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); uiTex = new Texture2D(W, H, TextureFormat.RGBA32, false); // 깊이 버퍼가 없으면 URP가 아무것도 그리지 않았다
        uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = new Color(0, 0, 0, 0); uiCam.cullingMask = 1 << OverlayLayer; uiCam.targetTexture = uiRt;
        var uadd = ugo.AddComponent<UniversalAdditionalCameraData>(); uadd.renderPostProcessing = false; uadd.antialiasing = AntialiasingMode.None;

        Canvas MakeCanvas(string n, Camera c, int layer)
        {
            var g = new GameObject(n); UnityEngine.Object.DontDestroyOnLoad(g); g.layer = layer;
            var cv = g.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = c; cv.planeDistance = 1f;
            var sc = g.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(W, H);
            return cv;
        }
        var over = MakeCanvas("TrailerCanvas", uiCam, OverlayLayer);
        var logoCv = MakeCanvas("TrailerLogoCanvas", cam, LogoLayer);
        GameObject Child(Transform parent, string n) { var g = new GameObject(n); g.transform.SetParent(parent, false); g.layer = parent.gameObject.layer; return g; }
        Image Rect01(string n, Vector2 min, Vector2 max, Color c) { var im = Child(over.transform, n).AddComponent<Image>(); im.color = c; var r = im.rectTransform; r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero; return im; }
        fade = Rect01("Fade", Vector2.zero, Vector2.one, new Color(0, 0, 0, 0));
        float bar = (1f - (W / 2.39f) / H) / 2f; // 2.39:1
        Rect01("BarTop", new Vector2(0, 1 - bar), Vector2.one, Color.black);
        Rect01("BarBottom", Vector2.zero, new Vector2(1, bar), Color.black);
        var cap = Child(over.transform, "Captions");
        capGroup = cap.AddComponent<CanvasGroup>();
        var cr = cap.AddComponent<RectTransform>(); cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
        Text T(string n, float y, int size, Color c, FontStyle st)
        {
            var g = Child(cap.transform, n); var t = g.AddComponent<Text>();
            t.font = UiTheme.Font ? UiTheme.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.color = c; t.fontStyle = st; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; var r = t.rectTransform; r.anchorMin = new Vector2(0, y); r.anchorMax = new Vector2(1, y); r.sizeDelta = new Vector2(0, size * 1.6f);
            var sh = g.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }
        caption = T("Caption", 0.235f, 34, new Color(0.93f, 0.90f, 0.84f), FontStyle.Normal);
        sub = T("Sub", 0.185f, 46, new Color(0.96f, 0.72f, 0.30f), FontStyle.Bold);
        // 로고: 장면 카메라 쪽(빛 번짐을 받는다)
        logoImg = Child(logoCv.transform, "Logo").AddComponent<Image>(); logoImg.preserveAspect = true;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Title/TitleLogo.png");
        if (tex) logoImg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        var lr = logoImg.rectTransform; lr.anchorMin = new Vector2(0.25f, 0.42f); lr.anchorMax = new Vector2(0.75f, 0.68f); lr.offsetMin = lr.offsetMax = Vector2.zero;
        logoImg.color = new Color(1, 1, 1, 0);
    }

    static void NextShot()
    {
        index++;
        if (index >= shots.Count) { Finish(); return; }
        var s = shots[index];
        Status = $"컷 {index + 1}/{shots.Count}";
        frame = 0;
        if (s.black) { stage = 2; return; }
        if (SceneManager.GetActiveScene().name != s.scene) { SceneManager.LoadScene(s.scene); stage = 1; settleUntil = Time.realtimeSinceStartup + 4.5f; }
        else { stage = 1; settleUntil = Time.realtimeSinceStartup + 0.6f; }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Abort("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (stage == -1)
        {
            if (!AudioReady && Time.realtimeSinceStartup < settleUntil) return;
            if (!preview) BeginEncoder();
            NextShot();
            return;
        }
        var s = shots[index];
        if (stage == 1)
        {
            if (Time.realtimeSinceStartup < settleUntil) return;
            Calm();
            s.setup?.Invoke();
            if (s.gameplay)
            {
                var pl = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                s.camYaw = (pl ? pl.transform.eulerAngles.y : 0f) + s.camYawOff;
                SetGameCamera(s, 0f);
                if (!preview) AudioRenderer.Start(); // 게임 소리를 화면과 같은 박자로 받아 둔다
            }
            s.cues.Sort((x, y) => x.at.CompareTo(y.at)); // 정한 때 순서대로
            cueIndex = 0;
            Time.captureFramerate = Fps;
            stage = 2; lastFrame = -1;
            return;
        }
        if (stage != 2) return;
        if (Time.frameCount == lastFrame) return; // 게임 한 프레임에 한 장
        lastFrame = Time.frameCount;
        int total = Mathf.RoundToInt(s.seconds * Fps);
        float t = frame / (float)Mathf.Max(1, total - 1);
        float now = frame / (float)Fps;
        // 정한 때가 된 동작(다음 게임 프레임부터 보인다)
        while (cueIndex < s.cues.Count && s.cues[cueIndex].at <= now)
        {
            try { s.cues[cueIndex].act(); } catch (Exception ex) { Debug.LogWarning($"[트레일러] 신호 실패 {index + 1}-{cueIndex}: {ex.GetBaseException().Message}"); }
            cueIndex++;
        }
        if (s.gameplay) { SetGameCamera(s, t); UpdateChase(); }
        Render(s, t, now);
        frame++;
        if (frame == Mathf.RoundToInt(total * 0.6f)) // 컷마다 한 장 남긴다(확인용). 미리보기면 거기서 다음 컷으로
        {
            File.WriteAllBytes(Path.Combine(Out, $"preview_{index + 1:00}.jpg"), frameTex.EncodeToJPG(85));
            if (preview) frame = total;
        }
        else if (s.gameplay && (frame == Mathf.RoundToInt(total * 0.2f) || frame == Mathf.RoundToInt(total * 0.4f) || frame == Mathf.RoundToInt(total * 0.8f)))
            File.WriteAllBytes(Path.Combine(Out, $"preview_{index + 1:00}_{frame * 100 / total:00}.jpg"), frameTex.EncodeToJPG(80)); // 플레이 영상은 동작 확인용으로 더
        if (frame >= total)
        {
            Time.captureFramerate = 0; stage = 0;
            if (s.gameplay && !preview) AudioRenderer.Stop();
            if (s.gameplay) StopChase();
            NextShot();
        }
    }
    static int lastFrame;

    static void Render(Shot s, float t, float sec)
    {
        float e = Mathf.SmoothStep(0f, 1f, t);
        // 화면 전환: 들어올 때 · 나갈 때 검게
        float a = 0f;
        if (s.fadeIn > 0f) a = Mathf.Max(a, 1f - Mathf.Clamp01(sec / s.fadeIn));
        if (s.fadeOut > 0f) a = Mathf.Max(a, Mathf.Clamp01((sec - (s.seconds - s.fadeOut)) / s.fadeOut));
        fade.color = new Color(0, 0, 0, a);
        // 자막
        caption.text = s.caption ?? ""; sub.text = s.sub ?? "";
        float ca = Mathf.Clamp01((sec - s.captionAt) / 0.5f) * Mathf.Clamp01((s.seconds - 0.25f - sec) / 0.4f);
        capGroup.alpha = ca;
        if (s.logo)
        {
            float la = Mathf.Clamp01((sec - 0.4f) / 1.0f) * Mathf.Clamp01((s.seconds - sec) / 0.8f);
            logoImg.color = new Color(1, 1, 1, la);
            caption.text = ""; sub.fontSize = 30;
        }
        else { logoImg.color = new Color(1, 1, 1, 0); sub.fontSize = 46; }
        if (s.gameplay)
        {
            var mc = Camera.main;
            if (mc) { cam.transform.SetPositionAndRotation(mc.transform.position, mc.transform.rotation); cam.fieldOfView = mc.fieldOfView * 0.82f; } // 게임 화각(55°)은 넓어 사람이 작게 보였다 → 조금 당겨 찍는다
        }
        else if (!s.black)
        {
            cam.transform.position = Vector3.Lerp(s.from, s.to, e);
            cam.transform.LookAt(Vector3.Lerp(s.lookFrom, s.lookTo, e));
            cam.fieldOfView = s.fov;
        }
        // 되감기 화면 효과
        var fx = UnityEngine.Object.FindAnyObjectByType<RewindEffect>();
        if (fx && !s.gameplay) fx.SetWeight(s.rewind > 0f ? s.rewind * (0.75f + 0.25f * Mathf.Sin(sec * 9f)) : 0f); // 플레이 영상은 게임의 되감기 효과 그대로
        s.each?.Invoke(t);
        cam.clearFlags = s.black ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox; cam.backgroundColor = Color.black;
        cam.cullingMask = s.black ? 1 << LogoLayer : ~((1 << 5) | (1 << OverlayLayer) | (1 << LogoLayer));
        cam.Render();
        RenderTexture.active = rt; frameTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        uiCam.Render();
        RenderTexture.active = uiRt; uiTex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
        // 덧그림은 투명 바탕 위에 그려져 색이 이미 알파를 곱한 값이다 → 결과 = 덧그림 + 장면 × (1 - 알파)
        scenePx = frameTex.GetPixels32(); uiPx = uiTex.GetPixels32();
        for (int i = 0; i < scenePx.Length; i++)
        {
            var u = uiPx[i]; if (u.a == 0) continue;
            int k = 255 - u.a; var c = scenePx[i];
            scenePx[i] = new Color32((byte)Mathf.Min(255, u.r + c.r * k / 255), (byte)Mathf.Min(255, u.g + c.g * k / 255), (byte)Mathf.Min(255, u.b + c.b * k / 255), 255);
        }
        frameTex.SetPixels32(scenePx);
        if (!preview) { encoder.AddFrame(frameTex); PushAudio(s.gameplay); }
    }

    // ---------------- 장면 손질 ----------------

    /// <summary>게임 쪽 연출이 끼어들지 않게: 대사 · 목표 · 화면 덮개 지우고, 레이는 다치지 않게</summary>
    static void Calm()
    {
        StoryState.ClearLine(); StoryState.Title = null; StoryState.Fade = 0f; StoryState.Prompt = null;
        var p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (p) p.GetComponent<Health>().Invulnerable = true;
        foreach (var ps in PatrolSight.All) ps.CanDetect = false;
    }

    static void Show(params string[] names)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (names.Contains(t.name)) t.gameObject.SetActive(true);
    }

    static void Wake(params string[] names) => Wake(names, false);
    static void Wake(string name, bool fight) => Wake(new[] { name }, fight);
    static void Wake(string[] names, bool fight)
    {
        foreach (var e in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!names.Contains(e.name)) continue;
            e.gameObject.SetActive(true);
            var ps = e.GetComponent<PatrolSight>(); if (ps) { ps.Holding = false; ps.Staged = false; }
            if (fight) e.Activate();
        }
    }

    static void Place(Vector3 at, Vector3 lookAt)
    {
        var p = UnityEngine.Object.FindAnyObjectByType<PlayerController>(); if (!p) return;
        p.Teleport(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(lookAt - at, Vector3.up)));
    }

    /// <summary>적을 그 자리에 세워 둔다(AI를 꺼서 움직이지 않게)</summary>
    static void Pose(string name, Vector3 at, Vector3 lookAt)
    {
        var e = UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == name); if (!e) return;
        e.gameObject.SetActive(true);
        var ps = e.GetComponent<PatrolSight>(); if (ps) ps.enabled = false;
        e.enabled = false;
        var ag = e.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag) ag.enabled = false;
        e.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(lookAt - at, Vector3.up)));
    }

    static void Lights(string name)
    {
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (l.name == name) l.enabled = true;
    }

    /// <summary>레이 얼굴: 찌푸린 표정(표정 컴포넌트를 끄고 직접 맞춘다). 카메라 자리는 머리 기준(옆, 위, 앞 m)</summary>
    static void Face(float browDown, Vector3 from, Vector3 to)
    {
        var p = UnityEngine.Object.FindAnyObjectByType<PlayerController>(); if (!p) return;
        var fe = p.GetComponentInChildren<FaceExpression>(); if (fe) fe.enabled = false;
        var smr = p.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("Blink_L") >= 0);
        if (smr)
        {
            void Set(string n, float v) { int i = smr.sharedMesh.GetBlendShapeIndex(n); if (i >= 0) smr.SetBlendShapeWeight(i, v * 100f); }
            Set("BrowDown", browDown); Set("MouthPress", 0.6f); Set("Squint", 0.3f);
        }
        var head = p.animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 c = head.position + Vector3.up * 0.06f, f = p.transform.forward, rr = p.transform.right;
        Vector3 W(Vector3 o) => c + rr * o.x + Vector3.up * o.y + f * o.z;
        var s = shots[index];
        s.from = W(from); s.to = W(to); s.lookFrom = c + Vector3.up * 0.01f; s.lookTo = c;
    }

    // ---------------- 소리 ----------------

    static void BeginEncoder()
    {
        string path = Path.Combine(Out, OutFile);
        if (File.Exists(path)) File.Delete(path);
        var v = new VideoTrackAttributes { frameRate = new MediaRational(Fps), width = W, height = H, includeAlpha = false, bitRateMode = playVariant && !youtube ? VideoBitrateMode.Medium : VideoBitrateMode.High }; // 발표 자료용 플레이 영상은 20MB 제한이라 낮춘다, 유튜브용은 높게
        var au = new AudioTrackAttributes { sampleRate = new MediaRational(Rate), channelCount = 2, language = "ko" };
        encoder = new MediaEncoder(path, v, au);
        audio = MixAudio(); audioCursor = 0;
    }

    static void PushAudio(bool gameSound)
    {
        int n = Rate / Fps * 2;
        using var buf = new NativeArray<float>(n, Allocator.Temp);
        var arr = new float[n];
        for (int i = 0; i < n; i++) arr[i] = audioCursor + i < audio.Length ? audio[audioCursor + i] : 0f;
        audioCursor += n;
        if (gameSound)
        {
            // 이 프레임의 게임 소리(때리는 소리 · 구르기 · 되감기 테이프 소리): 출력이 48kHz 스테레오일 때만 그대로 더한다
            int count = AudioRenderer.GetSampleCountForCaptureFrame();
            int ch = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            if (count > 0)
            {
                using var game = new NativeArray<float>(count * ch, Allocator.Temp);
                if (AudioRenderer.Render(game) && AudioSettings.outputSampleRate == Rate && ch == 2)
                    for (int i = 0; i < Mathf.Min(n, game.Length); i++) arr[i] = Mathf.Clamp(arr[i] + game[i], -1f, 1f);
            }
        }
        buf.CopyFrom(arr);
        encoder.AddSamples(buf);
    }

    /// <summary>음악(타이틀 곡 한 구간) + 컷마다 효과음. 48kHz 스테레오</summary>
    static float[] MixAudio()
    {
        float total = shots.Sum(s => s.seconds);
        int len = Mathf.CeilToInt(total * Rate) * 2;
        var mix = new float[len];
        var music = Load($"Music/Title/{musicName}");
        if (music != null)
        {
            int start = Mathf.RoundToInt(musicStart * Rate) * 2;
            for (int i = 0; i < len && start + i < music.Length; i++)
            {
                float tsec = i / 2f / Rate;
                float env = Mathf.Clamp01(tsec / musicFadeIn) * Mathf.Clamp01((total - tsec) / 3.5f);
                mix[i] += music[start + i] * musicGain * env;
            }
        }
        float at = 0f;
        foreach (var s in shots)
        {
            if (!string.IsNullOrEmpty(s.sfx))
            {
                var fx = Load("Sfx/" + s.sfx);
                if (fx != null)
                {
                    int o = Mathf.RoundToInt((at + s.sfxAt) * Rate) * 2;
                    for (int i = 0; i < fx.Length && o + i < len; i++) mix[o + i] += fx[i] * 0.9f * s.sfxGain;
                }
            }
            at += s.seconds;
        }
        float peak = mix.Max(Mathf.Abs);
        if (peak > 0.98f) for (int i = 0; i < len; i++) mix[i] *= 0.98f / peak;
        return mix;
    }

    /// <summary>클립을 48kHz 스테레오 float로. 플레이 중에만(편집 모드에서는 음악 불러오기가 끝나지 않는다).
    /// 음악은 미리 '불러올 때 풀기 · PCM'으로 바꿔 두고, 끝나면 .meta를 git으로 되돌린다</summary>
    static float[] Load(string resource)
    {
        var clip = Resources.Load<AudioClip>(resource);
        if (!clip) { Debug.LogWarning("[트레일러] 소리 없음 " + resource); return null; }
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) { Debug.LogWarning("[트레일러] 소리 못 읽음 " + resource + " " + clip.loadState); return null; }
        int ch = clip.channels, freq = clip.frequency;
        int outLen = Mathf.FloorToInt(clip.samples * (Rate / (float)freq));
        var res = new float[outLen * 2];
        for (int i = 0; i < outLen; i++)
        {
            float src = i * (freq / (float)Rate); int i0 = Mathf.Min((int)src, clip.samples - 1), i1 = Mathf.Min(i0 + 1, clip.samples - 1); float f = src - i0;
            for (int c = 0; c < 2; c++)
            {
                int sc = Mathf.Min(c, ch - 1);
                res[i * 2 + c] = Mathf.Lerp(data[i0 * ch + sc], data[i1 * ch + sc], f);
            }
        }
        return res;
    }

    static List<AudioClip> pending;
    /// <summary>쓸 소리를 모두 불러오기 시작한다</summary>
    static void Preload()
    {
        pending = new List<AudioClip>();
        foreach (var n in new[] { $"Music/Title/{musicName}" }.Concat(shots.Where(x => !string.IsNullOrEmpty(x.sfx)).Select(x => "Sfx/" + x.sfx)))
        {
            var c = Resources.Load<AudioClip>(n); if (!c) continue;
            c.LoadAudioData(); pending.Add(c);
        }
    }
    static bool AudioReady => pending.All(c => c.loadState != AudioDataLoadState.Loading && c.loadState != AudioDataLoadState.Unloaded);

    /// <summary>음악 고르기용: 곡의 초마다 소리 크기(0~1)</summary>
    public static string Envelope(string name)
    {
        var c = Resources.Load<AudioClip>("Music/Title/" + name); if (c.loadState != AudioDataLoadState.Loaded) { c.LoadAudioData(); return "불러오는 중 " + c.loadState; }
        var d = Load("Music/Title/" + name); if (d == null) return "없음";
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s * Rate * 2 < d.Length; s += 2)
        {
            double sum = 0; int n = 0;
            for (int i = s * Rate * 2; i < Math.Min(d.Length, (s + 2) * Rate * 2); i += 16) { sum += d[i] * d[i]; n++; }
            sb.Append(Math.Sqrt(sum / Math.Max(1, n)).ToString("0.00")).Append(' ');
        }
        return sb.ToString();
    }

    // ---------------- 끝 ----------------

    static void Finish()
    {
        if (playVariant) EndAbility();
        EditorApplication.update -= Tick;
        Time.captureFramerate = 0;
        if (encoder != null) { encoder.Dispose(); encoder = null; }
        Cleanup();
        Status = preview ? "미리보기 끝" : "끝";
        Debug.Log("[트레일러] " + Status);
    }

    static void Abort(string why) { EditorApplication.update -= Tick; Time.captureFramerate = 0; AudioRenderer.Stop(); if (encoder != null) { encoder.Dispose(); encoder = null; } Status = "중단: " + why; }

    static void Cleanup()
    {
        if (cam) UnityEngine.Object.Destroy(cam.gameObject);
        if (uiCam) UnityEngine.Object.Destroy(uiCam.gameObject);
        foreach (var n in new[] { "TrailerCanvas", "TrailerLogoCanvas" }) { var cv = GameObject.Find(n); if (cv) UnityEngine.Object.Destroy(cv); }
        if (rt) rt.Release();
        if (uiRt) uiRt.Release();
    }
}
