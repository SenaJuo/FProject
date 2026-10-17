using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// 처음 해 보는 사람처럼 타이틀부터 끝까지 진짜 입력(키보드·마우스 흉내)만으로 플레이하며 재미를 재는 자동 플레이어.
/// 순간 이동·건너뛰기 없음. 길잡이를 향해 달리고(W+Shift), F로 상호작용, 편지는 읽고 닫고, "R 길게"가 뜨면 R,
/// 적이 가까우면 좌클릭으로 치고 적이 공격을 예고하면 가끔 우클릭으로 구르고, Q 제압이 뜨면 Q, 쓰러지면 R로 되감는다.
/// 기록: 단계별 시각, 컷신·대사 잠김 시간 대 직접 조작 시간, 싸움 시간, "아무 일 없이 걷기만 한" 가장 긴 구간,
/// 받은 피해·쓰러짐·되감기·적 처치·제압 횟수, 단계가 바뀔 때 사진.
/// </summary>
public static class PlaytestBot
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly FieldInfo YawField = typeof(ThirdPersonCamera).GetField("yaw", Any);
    static readonly FieldInfo EnemyState = typeof(EnemyAI).GetField("state", Any);

    static string dir;
    static readonly List<string> lines = new();
    static float tickAt, startReal, last;
    static string lastPhase = "", lastScene = "";
    static int shot;

    // 지표
    static float cutscene, locked, control, combat, explore, rewindTime, quiet, quietMax, dialogueTime;
    static string quietMaxWhere = "";
    static readonly List<string> quietLong = new();
    static int deaths, kills, takedowns, rewinds, shopVisits, interacts, dodges, swings, perfects, potionsUsed, pickups;
    static float nextHealAt, lastWindupSeenAt;
    static EnemyAI lastDodged; static int lastDodgedCount = -1;
    static float damageTaken, damageDealt;
    static int prevHp = -1; static bool prevDead;
    static readonly Dictionary<EnemyAI, int> enemyHp = new();
    static readonly HashSet<EnemyAI> counted = new();
    static readonly Dictionary<string, float> phaseTime = new();

    // 입력
    static readonly HashSet<Key> hold = new();
    static Key tap = Key.None;
    static bool mouseLeft, mouseRight;
    static float nextTapAt, holdRUntil, letterSince = -1f, shopStep = -1f, noWaypointNoteAt;
    static Vector3 stuckPos; static float stuckSince;
    static bool startPressed, finished, shopDone;

    [MenuItem("Game/Test/Playtest Bot")]
    public static void Begin() => Begin(@"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\play");

    public static void Begin(string outDir)
    {
        dir = outDir;
        System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); quietLong.Clear(); enemyHp.Clear(); counted.Clear(); phaseTime.Clear();
        cutscene = locked = control = combat = explore = rewindTime = quiet = quietMax = dialogueTime = 0f;
        abilities = 0; nextAbilityAt = sceneStart = 0f; chapterAt.Clear();
        deaths = kills = takedowns = rewinds = shopVisits = interacts = dodges = swings = perfects = potionsUsed = pickups = 0;
        EnemyAI.PerfectDodged -= OnPerfect; EnemyAI.PerfectDodged += OnPerfect;
        PatrolSight.Spotted -= OnCaught; PatrolSight.Spotted += OnCaught;
        Searchlight.Spotted -= OnCaughtLight; Searchlight.Spotted += OnCaughtLight;
        caughtCount = 0; waitNoteAt = 0f;
        damageTaken = damageDealt = 0f; prevHp = -1; prevDead = false;
        startPressed = finished = shopDone = false; shot = 0; lastPhase = lastScene = "";
        Application.runInBackground = true;
        startReal = last = Time.realtimeSinceStartup;
        refocuses = 0; refocusAt = 0f; unfocusedSince = -1f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시작");
    }

    static int abilities;
    static float nextAbilityAt, sceneStart;
    static readonly Dictionary<string, float> chapterAt = new();
    static float Now => Time.realtimeSinceStartup - startReal;

    // Unity 창이 뒤로 가면 에디터가 이동 입력을 게임에 넘기지 않는다(키를 누른 채라 다시 앞으로 와도 그대로 멈춰 있다)
    // → 창을 다시 앞으로 가져오고 키를 한 번 뗀다(5초에 한 번까지)
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(System.IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
    static float refocusAt, unfocusedSince = -1f;
    static int refocuses;

    static bool KeepFocus()
    {
        if (Application.isFocused) { unfocusedSince = -1f; return true; }
        if (unfocusedSince < 0f) unfocusedSince = Now;
        if (Now - unfocusedSince < 1f || Now < refocusAt) return false;
        refocusAt = Now + 5f;
        var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        keybd_event(0x12, 0, 0, System.UIntPtr.Zero);
        SetForegroundWindow(h);
        keybd_event(0x12, 0, 2, System.UIntPtr.Zero);
        EditorWindow.FocusWindowIfItsOpen(System.Type.GetType("UnityEditor.GameView,UnityEditor"));
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        refocuses++;
        Note($"[측정 보조] Unity 창이 뒤로 가서 다시 앞으로 가져옴(#{refocuses}, {Now - unfocusedSince:0}초 멈춤 — 빈 시간에서 뺄 것)");
        return false;
    }
    static void OnPerfect(EnemyAI e) { perfects++; Note($"완벽 회피: {e.name}"); }
    static void Note(string t) { t = $"[{(int)(Now / 60f):00}:{Now % 60f:00.0}] {t}"; lines.Add(t); System.IO.File.WriteAllLines(dir + "/play.log", lines); }
    static void Shot(string n) { ScreenCapture.CaptureScreenshot($"{dir}/{shot++:00}_{n}.png"); }

    static string Phase()
    {
        var c1 = Object.FindAnyObjectByType<Chapter1Director>();
        if (c1) return "1장 " + c1.CurrentPhase;
        var c2 = Object.FindAnyObjectByType<Chapter2Director>(); if (c2) return "2장 " + c2.CurrentPhase;
        var c3 = Object.FindAnyObjectByType<Chapter3Director>(); if (c3) return "3장 " + c3.CurrentPhase;
        var c4 = Object.FindAnyObjectByType<Chapter4Director>(); if (c4) return "4장 " + c4.CurrentPhase;
        var c5 = Object.FindAnyObjectByType<Chapter5Director>(); if (c5) return "5장 " + c5.CurrentPhase;
        var pd = Object.FindAnyObjectByType<PrologueDirector>();
        if (pd) return $"프롤로그{PrologueDirector.Loop}회차 " + typeof(PrologueDirector).GetField("phase", Any).GetValue(pd);
        return SceneManager.GetActiveScene().name;
    }

    // ---------- 매 틱 ----------

    static void Tick()
    {
        SaveGame.Enabled = false; // 자동 플레이는 실제 저장을 덮어쓰지 않는다
        if (!EditorApplication.isPlaying) { Finish("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        float real = Time.realtimeSinceStartup;
        if (real < tickAt) return;
        tickAt = real + 0.05f;
        float dt = Mathf.Min(real - last, 0.5f); last = real;

        string scene = SceneManager.GetActiveScene().name;
        if (scene != lastScene)
        {
            if (lastScene != "" && lastScene != "Title" && scene == "Title") { Finish($"{lastScene} 끝 → 타이틀로 돌아옴"); return; }
            if (lastScene != "") chapterAt[lastScene] = Now - sceneStart;
            sceneStart = Now;
            Note($"장면 → {scene}"); lastScene = scene; prevHp = -1; enemyHp.Clear(); counted.Clear();
        }
        if (Now > 80f * 60f) { Finish("시간 초과(80분)"); return; }

        if (!KeepFocus()) return; // 창이 뒤에 있는 동안은 입력을 보내지 않는다(키를 뗀 상태로 두었다가 돌아오면 새로 누른다)

        if (scene == "Title") { TitleInput(); Send(); return; }

        var p = Object.FindAnyObjectByType<PlayerController>();
        if (!p) { Send(); return; }
        var hp = p.GetComponent<Health>();
        var w = p.GetComponent<Walkman>();
        string ph = Phase();
        if (ph != lastPhase) { Note($"단계 → {ph}  (목표 '{StoryState.Objective}')"); lastPhase = ph; Shot(Safe(ph)); }
        phaseTime[ph] = phaseTime.TryGetValue(ph, out var pt) ? pt + dt : dt;

        Measure(p, hp, w, dt);
        Act(p, hp, w, dt);
        Send();
    }

    static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    static void TitleInput()
    {
        hold.Clear(); mouseLeft = mouseRight = false;
        var ts = Object.FindAnyObjectByType<TitleScreen>();
        // 저장이 있으면 Enter는 '이어하기'라서, 시험은 언제나 처음부터(새로 시작) 시작한다
        if (ts && !startPressed && ts.TitleTime > 5f) { ts.StartGameNow(); startPressed = true; Note("타이틀: 게임 시작"); Shot("title"); }
    }

    // ---------- 재기 ----------

    static void Measure(PlayerController p, Health hp, Walkman w, float dt)
    {
        // 피해·쓰러짐
        if (prevHp >= 0 && hp.Current < prevHp) damageTaken += prevHp - hp.Current;
        prevHp = hp.Current;
        if (p.IsDead && !prevDead) { deaths++; Note($"쓰러짐 #{deaths} ({lastPhase}, 받은 피해 누계 {damageTaken:0})"); Shot("death" + deaths); }
        prevDead = p.IsDead;
        // 적: 입힌 피해·처치
        foreach (var e in EnemyAI.All.ToList())
        {
            if (!e) continue;
            int cur = e.Health.Current;
            if (enemyHp.TryGetValue(e, out var before) && cur < before) damageDealt += before - cur;
            enemyHp[e] = cur;
            if (e.Health.IsDead && counted.Add(e)) kills++;
        }

        // 시간 나누기
        bool combatNow = Target(p, 15f) != null;
        if (StoryState.LineVisible) dialogueTime += dt;
        if (w && (w.Rewinding || w.PromptOpen)) rewindTime += dt;
        else if (StoryState.CutsceneActive) cutscene += dt;
        else if (StoryState.InputLocked) locked += dt;
        else
        {
            control += dt;
            if (combatNow) combat += dt; else explore += dt;
        }
        // 아무 일 없는 구간: 직접 조작 중인데 싸움·대사·상호작용·편지가 없다
        bool quietNow = !StoryState.InputLocked && !combatNow && !StoryState.LineVisible && !StoryState.Nearby && !TapeShop.Nearby && StoryState.LetterText == null && !(w && (w.Rewinding || w.PromptOpen));
        if (quietNow) { quiet += dt; if (quiet > quietMax) { quietMax = quiet; quietMaxWhere = $"{lastPhase} / 목표 '{StoryState.Objective}'"; } }
        else { if (quiet > 20f) quietLong.Add($"{quiet:0}초 — {lastPhase} / 목표 '{StoryState.Objective}'"); quiet = 0f; }
    }

    // ---------- 조작 ----------

    static EnemyAI Target(PlayerController p, float range)
    {
        EnemyAI best = null; float bd = range;
        foreach (var e in EnemyAI.All)
        {
            if (!e || !e.IsActive || e.Health.IsDead) continue;
            Vector3 d = e.transform.position - p.transform.position;
            if (Mathf.Abs(d.y) > 2.5f) continue;
            float m = d.magnitude;
            if (m < bd) { bd = m; best = e; }
        }
        return best;
    }

    static void Act(PlayerController p, Health hp, Walkman w, float dt)
    {
        hold.Clear(); mouseLeft = mouseRight = false;
        float t = Now;

        if (w && w.PromptOpen) { if (t > nextTapAt) { tap = Key.R; nextTapAt = t + 1f; rewinds++; Note("쓰러진 화면: R로 되감기"); } return; }
        if (w && w.Rewinding) return;
        // 장르 테이프 선택지: 싸우는 중이면 E(능력), 아니면 Q(되감기)
        if (w && w.ChoiceOpen)
        {
            if (t > nextTapAt) { bool fight = Target(p, 12f) != null; tap = fight ? Key.E : Key.Q; nextTapAt = t + 0.8f; if (fight) abilities++; Note(fight ? $"선택지 → E 능력({w.Inserted?.Genre})" : "선택지 → Q 되감기"); }
            return;
        }
        if (TapeShop.Current) { ShopInput(t); return; }
        if (KeypadPanel.IsOpen) { if (t > nextTapAt) { KeypadPanel.Type("0417"); nextTapAt = t + 1f; Note("비밀번호 패널: 0417"); } return; }
        if (StoryState.LetterText != null)
        {
            if (letterSince < 0f) letterSince = t;
            if (t - letterSince > 5f && t > nextTapAt) { tap = Key.F; nextTapAt = t + 1f; } // 읽는 시간
            return;
        }
        letterSince = -1f;
        if (!string.IsNullOrEmpty(StoryState.Prompt) || t < holdRUntil)
        {
            if (t >= holdRUntil && !string.IsNullOrEmpty(StoryState.Prompt) && StoryState.Prompt.Contains("R")) { holdRUntil = t + 1.6f; rewinds++; Note($"안내 '{StoryState.Prompt}' → R 누르고 있기"); }
            if (t < holdRUntil) { hold.Add(Key.R); return; }
        }
        if (StoryState.InputLocked) return;

        // 사람처럼: 체력이 바닥나면 회복약(1), 편지에 적힌 대로 되감기를 써 보라면 R
        if (hp.Current < 35 && w && w.potions > 0 && t > nextHealAt) { tap = Key.Digit1; nextHealAt = t + 2f; potionsUsed++; Note($"회복약 사용(체력 {hp.Current})"); return; }
        if (!StoryState.RewindLocked && !string.IsNullOrEmpty(StoryState.Objective) && StoryState.Objective.Contains("되감기") && t > holdRUntil + 8f)
        { holdRUntil = t + 1.6f; rewinds++; Note($"목표 '{StoryState.Objective}' → R 누르고 있기"); hold.Add(Key.R); return; }

        var cam = Object.FindAnyObjectByType<ThirdPersonCamera>();
        if (Takedown.Target && t > nextTapAt) { tap = Key.Q; nextTapAt = t + 1.2f; takedowns++; Note($"Q 제압: {Takedown.Target.name}"); return; }

        var enemy = Target(p, 12f);
        // 사람처럼: 적이 둘 이상이거나 큰 적이면 장르 테이프 능력을 켜 본다(20초에 한 번)
        if (enemy && w && !StoryState.RewindLocked && TapeAbility.Current == TapeGenre.None && w.CurrentCharges > 0 && w.Inserted != null && w.Inserted.Genre != TapeGenre.None
            && t > nextAbilityAt && (EnemyAI.All.Count(e => e && e.IsActive && !e.Health.IsDead && Vector3.Distance(e.transform.position, p.transform.position) < 12f) >= 2 || enemy.Health.Max >= 150))
        { tap = Key.R; nextAbilityAt = t + 20f; nextTapAt = t + 0.3f; return; }
        if (enemy)
        {
            Vector3 to = enemy.transform.position - p.transform.position; to.y = 0f;
            Face(cam, to);
            // 예고를 보고 주먹이 닿기 직전에 구른다(강타는 꼭, 보통 주먹은 열에 여섯)
            bool winding = EnemyState != null && EnemyState.GetValue(enemy).ToString() == "Windup" && !enemy.ranged;
            if (winding && to.magnitude < 3.2f)
            {
                float timer = (float)typeof(EnemyAI).GetField("timer", Any).GetValue(enemy);
                float left = (enemy.HeavyTelegraph ? enemy.heavyWindup : enemy.windup) - timer;
                int count = (int)typeof(EnemyAI).GetField("attackCount", Any).GetValue(enemy);
                bool fresh = enemy != lastDodged || count != lastDodgedCount;
                if (fresh && left <= 0.28f)
                {
                    lastDodged = enemy; lastDodgedCount = count;
                    if (enemy.HeavyTelegraph || Random.value < 0.6f) { mouseRight = true; hold.Add(Key.S); dodges++; nextTapAt = t + 0.5f; return; }
                }
            }
            if (to.magnitude > 1.6f) { hold.Add(Key.W); if (to.magnitude > 5f) hold.Add(Key.LeftShift); }
            else hold.Add(Key.W);
            if (to.magnitude < 2.4f && t > nextTapAt) { mouseLeft = true; swings++; nextTapAt = t + 0.22f; }
            return;
        }

        if (StoryState.Nearby && t > nextTapAt) { tap = Key.F; nextTapAt = t + 1.5f; interacts++; Note($"F: {StoryState.Nearby.prompt}"); return; }
        if (TapeShop.Nearby && !shopDone && t > nextTapAt) { tap = Key.F; nextTapAt = t + 1.5f; shopVisits++; shopStep = t; Note($"가게 들어감: {TapeShop.Nearby.shopName}"); return; }

        // 길잡이로 달린다. 길잡이가 없는데 목표가 있으면(예: "조직원들을 제압한다") 눈에 보이는 가장 가까운 적에게 다가간다
        Transform wp = StoryState.Waypoint;
        if (!wp && !string.IsNullOrEmpty(StoryState.Objective))
        {
            float bd = 30f;
            foreach (var e in EnemyAI.All)
                if (e && !e.Health.IsDead && Mathf.Abs(e.transform.position.y - p.transform.position.y) < 2.5f)
                {
                    float d = Vector3.Distance(e.transform.position, p.transform.position);
                    if (d < bd) { bd = d; wp = e.transform; }
                }
            if (wp && t > noWaypointNoteAt) { Note($"길잡이 없음 → 보이는 적({wp.name})에게 다가감"); noWaypointNoteAt = t + 20f; }
        }
        if (!wp) return;
        var path = new NavMeshPath();
        Vector3 goal = Reachable(p.transform.position, wp);
        Vector3 from = p.transform.position;
        Vector3 dirTo;
        if (NavMesh.SamplePosition(from, out var a, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(goal, out var b, 4f, NavMesh.AllAreas)
            && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 1)
        {
            int i = 1; while (i < path.corners.Length - 1 && Vector3.Distance(Flat(path.corners[i]), Flat(from)) < 0.8f) i++;
            dirTo = path.corners[i] - from;
        }
        else dirTo = goal - from;
        dirTo.y = 0f;
        if (dirTo.magnitude < 0.6f) return;
        if (Stealthy() && Sneak(cam, from, path.status == NavMeshPathStatus.PathInvalid ? null : path.corners, t)) return;
        Face(cam, dirTo);
        hold.Add(Key.W); hold.Add(Key.LeftShift);

        // 막힘: 6초 동안 1m도 못 갔으면 점프하며 옆으로
        if (Vector3.Distance(from, stuckPos) > 1f) { stuckPos = from; stuckSince = t; }
        else if (t - stuckSince > 6f) { tap = Key.Space; hold.Add(Random.value < 0.5f ? Key.A : Key.D); if (t - stuckSince > 6.2f) { Note($"막힘: {lastPhase} 위치 {from:F1} → 길잡이 {wp.name}"); stuckSince = t; } }
    }

    // ---------- 잠입: 순찰·탐조등을 몇 초 앞까지 내다보고 움직인다 ----------
    // 순찰은 앞으로 1.5m/s(길 끝 근처면 돌아설 방향도), 탐조등은 실제 경로를 따라 미리 굴려 본다.
    // 지금 길(달리기 9m/s)이 2초 동안 안 보이면 간다 → 아니면 제자리가 안전하면 기다린다 → 아니면 가까운 숨을 곳(안전하게 가서 3초 버틸 곳)으로 옮긴다.
    // 등을 보인 순찰이 가까우면(다른 눈에 안 걸리게) 길을 따라 뒤로 붙어 Q로 제압한다.

    const float Run = 9f, Horizon = 2f;
    static float waitNoteAt, refugeUntil, goSince, waitSince = -1f, detourUntil, detourTriedAt = -99f;
    static Vector3 refuge, goPos, detour;
    static int caughtCount;
    static readonly FieldInfo SlAlong = typeof(Searchlight).GetField("along", Any), SlDir = typeof(Searchlight).GetField("dir", Any), SlLen = typeof(Searchlight).GetField("totalLength", Any);
    static readonly MethodInfo SlPoint = typeof(Searchlight).GetMethod("PointAlong", Any);
    static readonly FieldInfo LookPhase = typeof(PatrolSight).GetField("lookPhase", Any);
    static readonly RaycastHit[] rayBuf = new RaycastHit[16];

    static bool Awake(PatrolSight ps) => ps && ps.isActiveAndEnabled && ps.CanDetect && !ps.Alerted && !(ps.Holding && ps.Staged) && !(ps.Enemy && ps.Enemy.Health.IsDead);
    static bool Stealthy() => Object.FindAnyObjectByType<Searchlight>() || PatrolSight.All.Any(Awake);

    static Vector3 LightAt(Searchlight sl, float tau)
    {
        if (sl.Alarmed || sl.path.Length < 2 || SlLen == null) return sl.Aim;
        float len = (float)SlLen.GetValue(sl);
        if (len < 0.1f) return sl.Aim;
        float al = (float)SlAlong.GetValue(sl) + (float)SlDir.GetValue(sl) * sl.speed * tau;
        for (int i = 0; i < 4 && (al > len || al < 0f); i++) al = al > len ? 2f * len - al : -al;
        return (Vector3)SlPoint.Invoke(sl, new object[] { Mathf.Clamp(al, 0f, len) });
    }

    static bool Blocked(Vector3 eye, Vector3 to, PatrolSight ps)
    {
        Vector3 d = to - eye; float len = d.magnitude;
        if (len < 0.01f) return false;
        int n = Physics.RaycastNonAlloc(eye, d / len, rayBuf, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = rayBuf[i].collider;
            if (c.transform.IsChildOf(ps.transform) || c.GetComponentInParent<PlayerController>() || rayBuf[i].rigidbody || c.GetComponentInParent<EnemyAI>()) continue;
            return true;
        }
        return false;
    }

    /// <summary>tau초 뒤 그 자리가 보이는가(ignore 순찰은 뺀다)</summary>
    static bool Seen(Vector3 pos, float tau, PatrolSight ignore = null)
    {
        foreach (var sl in Object.FindObjectsByType<Searchlight>(FindObjectsSortMode.None))
            if (sl.isActiveAndEnabled && Flat(pos - LightAt(sl, tau)).magnitude < sl.radius + 1.6f) return true;
        foreach (var ps in PatrolSight.All)
        {
            if (ps == ignore || !Awake(ps)) continue;
            Vector3 fwd = Flat(ps.transform.forward).normalized;
            if (ps.route.Length <= 1 && LookPhase != null) // 제자리 보초: 둘러보는 고개를 τ초 앞까지 돌려 본다
            {
                float ph = (float)LookPhase.GetValue(ps);
                fwd = Quaternion.Euler(0f, 55f / 1.4f * (Mathf.Cos(1.4f * ph) - Mathf.Cos(1.4f * (ph + tau))), 0f) * fwd;
            }
            Vector3 at = ps.transform.position + (ps.Holding ? Vector3.zero : fwd * 1.5f * tau);
            Vector3 d = Flat(pos - at); float m = d.magnitude;
            if (m > ps.viewDistance + 1f || Mathf.Abs(pos.y - at.y) > 2.5f) continue;
            bool cone = m < ps.nearSense + 0.8f || Vector3.Angle(fwd, d) < ps.viewAngle + 6f;
            // 길 끝 가까이면 곧 돌아선다: 다른 길목 쪽도 본다고 친다
            if (!cone && !ps.Holding)
                foreach (var r in ps.route)
                {
                    if (!r) continue;
                    float rd = Flat(r.position - ps.transform.position).magnitude;
                    if (rd < 1f || rd > 1.5f * tau + 1.5f) continue;
                    foreach (var o in ps.route) if (o && o != r && Vector3.Angle(Flat(o.position - r.position), d) < ps.viewAngle + 6f) cone = true;
                }
            if (!cone) continue;
            if (Blocked(at + Vector3.up * 1.6f, pos + Vector3.up * 1.2f, ps)) continue;
            return true;
        }
        return false;
    }

    static Vector3 Along(Vector3[] pts, float dist)
    {
        for (int i = 1; i < pts.Length; i++)
        {
            float seg = Vector3.Distance(pts[i - 1], pts[i]);
            if (dist <= seg) return Vector3.Lerp(pts[i - 1], pts[i], seg > 0f ? dist / seg : 1f);
            dist -= seg;
        }
        return pts[pts.Length - 1];
    }

    static float Length(Vector3[] pts) { float l = 0f; for (int i = 1; i < pts.Length; i++) l += Vector3.Distance(pts[i - 1], pts[i]); return l; }

    /// <summary>pts를 달려가는 동안(최대 horizon초) 안 보이고, 도착하면 stay초 더 버틸 수 있나</summary>
    static bool RouteSafe(Vector3[] pts, float horizon, float stay = 0f, PatrolSight ignore = null)
    {
        float arrive = Length(pts) / Run;
        for (float tau = 0.25f; tau <= Mathf.Min(horizon, arrive) + 0.001f; tau += 0.25f)
            if (Seen(Along(pts, tau * Run), tau, ignore)) return false;
        if (arrive <= horizon) for (float k = 0f; k <= stay; k += 0.5f) if (Seen(pts[pts.Length - 1], arrive + k, ignore)) return false;
        return true;
    }

    static Vector3[] PathTo(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        if (!NavMesh.SamplePosition(from, out var a, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 1.5f, NavMesh.AllAreas)) return null;
        if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return null;
        var c = path.corners; c[0] = from; return c;
    }

    /// <summary>길 모서리를 따라 달린다. 3초 동안 1m도 못 가면(차·벽에 걸림) 옆으로 비키며 뛴다</summary>
    static void Go(ThirdPersonCamera cam, Vector3 from, Vector3[] pts, float t)
    {
        int i = 1;
        while (i < pts.Length - 1 && Flat(pts[i] - from).magnitude < 0.8f) i++;
        Face(cam, Flat(pts[Mathf.Min(i, pts.Length - 1)] - from));
        hold.Add(Key.W); hold.Add(Key.LeftShift);
        if (Flat(from - goPos).magnitude > 1f || t - goSince > 10f) { goPos = from; goSince = t; }
        else if (t - goSince > 3f) { tap = Key.Space; hold.Add(((int)t & 1) == 0 ? Key.A : Key.D); if (t - goSince > 3.1f) { Note($"걸림: {lastPhase} {from:F1}"); goSince = t - 1.5f; } }
    }

    /// <summary>보초가 한 바퀴 둘러보는 동안(4.5초) 내내 안 보이는 자리인가</summary>
    static bool AlwaysHidden(Vector3 pos) { for (float k = 0f; k <= 4.5f; k += 0.5f) if (Seen(pos, k)) return false; return true; }

    /// <summary>앞으로 4.5초 안에 출발하면 끝까지 안 보이고 달릴 수 있는 때가 있는가</summary>
    static bool HasWindow(Vector3[] pts)
    {
        float arrive = Length(pts) / Run;
        if (arrive > 4f) return true; // 먼 길은 가면서 다시 본다
        for (float start = 0f; start <= 4.5f; start += 0.25f)
        {
            bool ok = true;
            for (float tau = 0f; tau <= arrive + 0.001f && ok; tau += 0.2f) if (Seen(Along(pts, tau * Run), start + tau)) ok = false;
            if (ok) return true;
        }
        return false;
    }

    static bool StaticSafe(Vector3[] pts)
    {
        if (pts == null) return false;
        float len = Length(pts);
        for (float d = 0.75f; d <= len; d += 1.5f) if (!AlwaysHidden(Along(pts, d))) return false;
        return AlwaysHidden(pts[pts.Length - 1]);
    }

    /// <summary>언제나 안 보이는 길로 갈 수 있고 그 자리도 언제나 안 보이는 경유점 중, 목표까지 가장 짧은 곳.
    /// 경유점→목표 마지막 다리는 보초가 고개를 돌린 틈에 달린다(RouteSafe가 때를 본다)</summary>
    static bool FindDetour(Vector3 from, Vector3 goal, out Vector3 via)
    {
        via = goal;
        float best = float.MaxValue;
        Vector3 mid = (from + goal) * 0.5f;
        float span = Mathf.Clamp(Flat(goal - from).magnitude * 0.5f + 10f, 12f, 30f);
        for (float x = -span; x <= span; x += 3f)
            for (float z = -span; z <= span; z += 3f)
            {
                if (!NavMesh.SamplePosition(mid + new Vector3(x, 0f, z), out var hit, 1.4f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - from.y) > 2f || !AlwaysHidden(hit.position)) continue;
                var a = PathTo(from, hit.position); if (a == null) continue;
                float la = Length(a); if (la + Flat(goal - hit.position).magnitude >= best) continue;
                var b = PathTo(hit.position, goal); if (b == null) continue;
                float total = la + Length(b);
                if (total >= best || !StaticSafe(a) || !HasWindow(b)) continue;
                best = total; via = hit.position;
            }
        return best < float.MaxValue;
    }

    /// <summary>잠입 한 틱. true면 이번 틱 입력을 정했다</summary>
    static bool Sneak(ThirdPersonCamera cam, Vector3 from, Vector3[] route, float t)
    {
        stuckPos = from; stuckSince = t; // 바깥 막힘 검사는 쓰지 않는다(Go가 따로 본다)

        // 1) 등 뒤 제압
        foreach (var ps in PatrolSight.All)
        {
            if (!Awake(ps) || !ps.Enemy) continue;
            Vector3 me = Flat(from - ps.transform.position);
            if (me.magnitude > 12f || Mathf.Abs(ps.transform.position.y - from.y) > 2f) continue;
            if (Vector3.Angle(-Flat(ps.transform.forward), me) > 55f) continue;
            var pts = PathTo(from, ps.transform.position - Flat(ps.transform.forward).normalized * 0.9f);
            if (pts == null || Length(pts) > 16f || Seen(from, 0f, ps) || !RouteSafe(pts, Horizon, 0f, ps)) continue;
            if (t > waitNoteAt) { Note($"잠입: {ps.name} 등 뒤로 붙음"); waitNoteAt = t + 6f; }
            Go(cam, from, pts, t);
            return true;
        }

        // 2) 숨을 곳으로 가는 중
        if (t < refugeUntil)
        {
            var pts = Flat(refuge - from).magnitude > 0.7f ? PathTo(from, refuge) : null;
            if (pts != null) { Go(cam, from, pts, t); return true; }
            refugeUntil = 0f;
        }

        // 3) 길이 2초 동안 안전하면 간다
        if (route == null || route.Length < 2) return false;
        route = (Vector3[])route.Clone(); route[0] = from;
        Vector3 goalPos = route[route.Length - 1];

        // 우회 중: 경유점까지(그 길도 2초 앞까지 안전할 때만) 간다
        if (t < detourUntil)
        {
            var dp = Flat(detour - from).magnitude > 1f ? PathTo(from, detour) : null;
            if (dp == null) detourUntil = 0f;
            else if (RouteSafe(dp, Horizon)) { waitSince = -1f; Go(cam, from, dp, t); return true; }
        }
        else if (RouteSafe(route, Horizon)) { waitSince = -1f; Go(cam, from, route, t); return true; }

        // 3초 넘게 막혀 있으면: 시야를 피해 돌아가는 경유점을 찾는다(지금 이 순간 안 보이는 두 다리로 이어지는 곳 중 가장 짧은 길)
        if (waitSince < 0f) waitSince = t;
        if (t - waitSince > 3f && t - detourTriedAt > 4f && t >= detourUntil)
        {
            detourTriedAt = t;
            if (FindDetour(from, goalPos, out var via))
            {
                detour = via; detourUntil = t + 12f;
                Note($"잠입: 시야를 피해 {via:F0}로 돌아감");
            }
            else Note("잠입: 돌아갈 길 없음 → 계속 기다림");
        }

        // 4) 제자리가 안전하면 기다린다
        bool stay = true;
        for (float k = 0f; k <= 2.5f; k += 0.5f) if (Seen(from, k)) { stay = false; break; }
        if (stay) { goSince = t; if (t > waitNoteAt) { Note("잠입: 앞길이 보여서 기다림"); waitNoteAt = t + 8f; } return true; }

        // 5) 숨을 곳: 3·6·9m 둘레 16방향 중, 안전하게 가서 3초 버틸 수 있는 곳(목표에 가까운 쪽)
        Vector3 goal = route[route.Length - 1];
        float best = float.MaxValue; Vector3 pick = from; bool found = false;
        for (float r = 3f; r <= 9f; r += 3f)
            for (int i = 0; i < 16; i++)
            {
                Vector3 c = from + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * r;
                if (!NavMesh.SamplePosition(c, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                var pts = PathTo(from, hit.position);
                if (pts == null || Length(pts) > r * 1.6f || !RouteSafe(pts, 3f, 3f)) continue;
                float score = Vector3.Distance(hit.position, goal) + Length(pts) * 0.3f;
                if (score < best) { best = score; pick = hit.position; found = true; }
            }
        if (found)
        {
            refuge = pick; refugeUntil = t + 3f;
            Note($"잠입: 보일 것 같아 숨을 곳 {pick:F0}로");
            var pp = PathTo(from, pick); if (pp != null) Go(cam, from, pp, t);
            return true;
        }

        // 숨을 곳도 없으면 목표 쪽으로 달려 빠져나간다
        Go(cam, from, route, t);
        return true;
    }

    static void OnCaught(PatrolSight s) { caughtCount++; Note($"들킴 #{caughtCount}: 순찰 {s.name} ({lastPhase})"); }
    static void OnCaughtLight(Searchlight s) { caughtCount++; Note($"들킴 #{caughtCount}: 탐조등 ({lastPhase})"); }

    // 목표 둘레(0~3.2m, 8방향)에서 실제로 걸어서 닿는 점 중 목표에 가장 가까운 곳(그냥 가장 가까운 길 위 점은 벽 너머 다른 방일 수 있다)
    static Transform reachFor; static Vector3 reachPoint; static float reachAt;
    static Vector3 Reachable(Vector3 from, Transform wp)
    {
        float now = Now;
        if (wp == reachFor && now - reachAt < 2f) return reachPoint;
        reachFor = wp; reachAt = now; reachPoint = wp.position;
        Vector3 goal = wp.position;
        float bestR = float.MaxValue, bestLen = float.MaxValue;
        for (float r = 0f; r <= 3.2f; r += 0.8f)
        {
            for (int i = 0; i < (r == 0f ? 1 : 8); i++)
            {
                Vector3 c = goal + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * r;
                if (!NavMesh.SamplePosition(c, out var h, 0.9f, NavMesh.AllAreas) || Mathf.Abs(h.position.y - goal.y) > 2.2f) continue;
                var pts = PathTo(from, h.position);
                if (pts == null) continue;
                float len = Length(pts);
                if (r < bestR || len < bestLen && r <= bestR) { bestR = r; bestLen = len; reachPoint = h.position; }
            }
            if (bestR < float.MaxValue) break;
        }
        return reachPoint;
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    static void Face(ThirdPersonCamera cam, Vector3 dir)
    {
        if (!cam || YawField == null || dir.sqrMagnitude < 0.0001f) return;
        YawField.SetValue(cam, Quaternion.LookRotation(dir).eulerAngles.y);
    }

    static void ShopInput(float t)
    {
        if (shopStep < 0f) shopStep = t;
        // 하나 바꿔 본다: 진열대에서 고르고(F) → 내줄 테이프를 넣고(F, 미리 골라져 있으면 건너뜀) → 교환(Space) → 모자라 남아 있으면 뒤로(C) → 나가기(C)
        float k = t - shopStep; var shop = TapeShop.Current;
        if (k > 3f && k < 3.3f) tap = Key.F;
        else if (k > 3.8f && k < 4.1f && shop && shop.Offering && shop.Offer.Count == 0) tap = Key.F;
        else if (k > 4.6f && k < 4.9f) tap = Key.Space;
        else if (k > 5.4f && k < 5.7f && shop && shop.Offering) tap = Key.C;
        else if (k > 6.2f) { tap = Key.C; shopDone = true; Note("가게 나옴"); shopStep = -1f; }
    }

    static void Send()
    {
        var keys = new List<Key>(hold);
        if (tap != Key.None) { keys.Add(tap); tap = Key.None; }
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys.ToArray()));
        if (Mouse.current != null)
        {
            var ms = new MouseState { position = Mouse.current.position.ReadValue() };
            if (mouseLeft) ms = ms.WithButton(MouseButton.Left);
            if (mouseRight) ms = ms.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse.current, ms);
        }
    }

    static void Finish(string why)
    {
        if (finished) return;
        finished = true;
        EditorApplication.update -= Tick;
        if (quiet > 20f) quietLong.Add($"{quiet:0}초 — {lastPhase}");
        float total = cutscene + locked + control + rewindTime;
        Note("끝: " + why);
        Note($"총 {Now / 60f:0.0}분 | 직접 조작 {control:0}초({control / Mathf.Max(1, total) * 100:0}%) = 싸움 {combat:0}초 + 이동·탐색 {explore:0}초 | 컷신 {cutscene:0}초({cutscene / Mathf.Max(1, total) * 100:0}%) | 대사로 조작 잠김 {locked:0}초 | 되감기·사망화면 {rewindTime:0}초 | 자막 떠 있던 시간 {dialogueTime:0}초");
        Note($"아무 일 없이 걷기만 한 가장 긴 구간 {quietMax:0}초 ({quietMaxWhere})");
        foreach (var q in quietLong) Note("  20초 넘는 빈 구간: " + q);
        Note($"완벽 회피 {perfects} | 회복약 {potionsUsed} | 테이프 {(Object.FindAnyObjectByType<Walkman>() ? Object.FindAnyObjectByType<Walkman>().tapes.Count : -1)}개 보유");
        EnemyAI.PerfectDodged -= OnPerfect;
        Note($"쓰러짐 {deaths} | 되감기 {rewinds} | 받은 피해 {damageTaken:0} | 입힌 피해 {damageDealt:0} | 처치 {kills} | Q 제압 시도 {takedowns} | 휘두름 {swings} | 구르기 {dodges} | 상호작용 {interacts} | 가게 {shopVisits}");
        Note($"테이프 능력 {abilities}회 | 잠입 들킴 {caughtCount}");
        PatrolSight.Spotted -= OnCaught; Searchlight.Spotted -= OnCaughtLight;
        foreach (var kv in chapterAt) Note($"  장 시간 {kv.Key}: {kv.Value / 60f:0.0}분");
        foreach (var kv in phaseTime.OrderBy(k => k.Key)) Note($"  단계 시간 {kv.Key}: {kv.Value:0}초");
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
    }
}
