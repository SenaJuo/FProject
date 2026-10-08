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
        deaths = kills = takedowns = rewinds = shopVisits = interacts = dodges = swings = perfects = potionsUsed = pickups = 0;
        EnemyAI.PerfectDodged -= OnPerfect; EnemyAI.PerfectDodged += OnPerfect;
        damageTaken = damageDealt = 0f; prevHp = -1; prevDead = false;
        startPressed = finished = shopDone = false; shot = 0; lastPhase = lastScene = "";
        Application.runInBackground = true;
        startReal = last = Time.realtimeSinceStartup;
        refocuses = 0; refocusAt = 0f; unfocusedSince = -1f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시작");
    }

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
        var pd = Object.FindAnyObjectByType<PrologueDirector>();
        if (pd) return $"프롤로그{PrologueDirector.Loop}회차 " + typeof(PrologueDirector).GetField("phase", Any).GetValue(pd);
        return SceneManager.GetActiveScene().name;
    }

    // ---------- 매 틱 ----------

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Finish("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        float real = Time.realtimeSinceStartup;
        if (real < tickAt) return;
        tickAt = real + 0.05f;
        float dt = Mathf.Min(real - last, 0.5f); last = real;

        string scene = SceneManager.GetActiveScene().name;
        if (scene != lastScene)
        {
            if (lastScene == "Chapter1" && scene == "Title") { Finish("1장 끝 → 타이틀로 돌아옴"); return; }
            Note($"장면 → {scene}"); lastScene = scene; prevHp = -1; enemyHp.Clear(); counted.Clear();
        }
        if (Now > 45f * 60f) { Finish("시간 초과(45분)"); return; }

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
        if (ts && !startPressed && ts.TitleTime > 5f) { tap = Key.Enter; startPressed = true; Note("타이틀: 게임 시작"); Shot("title"); }
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
        if (TapeShop.Current) { ShopInput(t); return; }
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
        Vector3 goal = wp.position;
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
        Face(cam, dirTo);
        hold.Add(Key.W); hold.Add(Key.LeftShift);

        // 막힘: 6초 동안 1m도 못 갔으면 점프하며 옆으로
        if (Vector3.Distance(from, stuckPos) > 1f) { stuckPos = from; stuckSince = t; }
        else if (t - stuckSince > 6f) { tap = Key.Space; hold.Add(Random.value < 0.5f ? Key.A : Key.D); if (t - stuckSince > 6.2f) { Note($"막힘: {lastPhase} 위치 {from:F1} → 길잡이 {wp.name}"); stuckSince = t; } }
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
        if (t - shopStep > 3f && t - shopStep < 3.3f) tap = Key.F;          // 하나 바꿔 본다
        else if (t - shopStep > 5f) { tap = Key.C; shopDone = true; Note("가게 나옴"); shopStep = -1f; }
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
        foreach (var kv in phaseTime.OrderBy(k => k.Key)) Note($"  단계 시간 {kv.Key}: {kv.Value:0}초");
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
    }
}
