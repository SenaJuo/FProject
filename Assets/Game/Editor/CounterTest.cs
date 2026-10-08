using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 회피 반격 시험(플레이 중, 1장 시작 직후):
///  ① 조직원 주먹을 완벽 회피한 직후 공격 → 그 적에게 달려들어 강한 돌려차기(공격력 × 3.5)가 들어가는가. 멀리(3m) 서 있어도 달려드는가. 사진.
///  ② 완벽 회피 뒤 반격 시간(1.1초)을 넘겨 공격하면 반격이 아니라 보통 공격인가.
/// </summary>
public static class CounterTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, hpBefore, round;
    static float next, t0, dodgeAt, startDistance;
    static PlayerController p;
    static Health hp;
    static EnemyAI thug;
    static bool dodged, perfect, landed;

    public static void Begin(string logPath, string shotDir)
    {
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; round = 0; dodged = perfect = landed = false;
        t0 = Time.unscaledTime; next = Time.unscaledTime + 0.5f;
        EnemyAI.PerfectDodged -= OnPerfect; EnemyAI.PerfectDodged += OnPerfect;
        PlayerController.CounterLanded -= OnLanded; PlayerController.CounterLanded += OnLanded;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void OnPerfect(EnemyAI e) { if (e == thug) perfect = true; }
    static void OnLanded() => landed = true;
    static void Note(string t) { t = $"[{Time.unscaledTime - t0:0.0}s] {t}"; lines.Add(t); Debug.Log("[반격 시험] " + t); }
    static void Dodge() => typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(p, new object[] { -p.transform.forward });
    static bool Counter() => (bool)typeof(PlayerController).GetMethod("TryCounter", Any).Invoke(p, null);
    static string State() => typeof(PlayerController).GetField("state", Any).GetValue(p).ToString();
    static float Distance() { Vector3 d = thug.transform.position - p.transform.position; d.y = 0f; return d.magnitude; }

    static void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot($"{dir}/{name}.png");
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Done(); return; }
        EditorApplication.QueuePlayerLoopUpdate();

        // 매 프레임: 주먹이 닿기 0.12초 전에 뒤로 구른다(완벽 회피)
        if ((step == 2 || step == 6) && !dodged && thug.WindupLeft > 0f && thug.WindupLeft <= 0.12f)
        {
            dodged = true; dodgeAt = Time.unscaledTime;
            Dodge();
        }
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.05f;
        switch (step++)
        {
            case 0:
            {
                var d = Object.FindAnyObjectByType<Chapter1Director>();
                if (!d || d.CurrentPhase != Chapter1Director.Phase.Sneak || StoryState.InputLocked) { step--; return; }
                p = d.player; hp = p.GetComponent<Health>();
                thug = GameObject.Find("Thug_Exit_B").GetComponent<EnemyAI>();
                thug.GetComponent<PatrolSight>().Holding = true;
                Vector3 at = thug.transform.position + thug.transform.forward * 1.3f; at.y = thug.transform.position.y + 0.02f;
                p.Teleport(at, Quaternion.LookRotation(thug.transform.position - at));
                d.gameplayRig.SnapBehindTarget();
                thug.Activate();
                t0 = Time.unscaledTime;
                Note($"① 조직원(체력 {thug.Health.Current}) 앞에서 완벽 회피를 기다린다");
                break;
            }
            case 1: break;
            case 2:
                // 완벽 회피가 날 때까지
                if (hp.Current < 50) hp.Heal(100);
                if (!perfect) { if (Time.unscaledTime - t0 < 20f) { if (dodged && Time.unscaledTime - dodgeAt > 0.6f) dodged = false; step--; return; } }
                startDistance = Distance();
                hpBefore = thug.Health.Current;
                Note($"   완벽 회피 {perfect}, 반격 준비 {p.CounterReady}, 적과 거리 {startDistance:0.0}m, 적 체력 {hpBefore}");
                next = Time.unscaledTime + 0.15f;
                break;
            case 3:
                Note($"   공격 → 반격 {Counter()} (기대 True), 레이 상태 {State()}");
                next = Time.unscaledTime + 0.18f;
                break;
            case 4:
                Shot("01_counter_kick");
                next = Time.unscaledTime + 0.6f;
                break;
            case 5:
            {
                int dealt = hpBefore - thug.Health.Current;
                Note($"   반격 뒤: 적중 {landed}(기대 True), 피해 {dealt}(기대 {Mathf.RoundToInt(p.attackPower * p.counterDamageMultiplier)} 또는 쓰러짐), 적 쓰러짐 {thug.Health.IsDead}, 달려든 거리 {startDistance:0.0}m → {Distance():0.0}m");
                // ② 시간을 넘긴 공격: 새 조직원으로
                round = 1; perfect = dodged = landed = false;
                if (!thug.Health.IsDead) { thug.Deactivate(); thug.Health.TakeDamage(9999, thug.transform.position); }
                thug = GameObject.Find("Thug_Exit_A").GetComponent<EnemyAI>();
                thug.GetComponent<PatrolSight>().Holding = true;
                Vector3 at = thug.transform.position + thug.transform.forward * 1.3f; at.y = thug.transform.position.y + 0.02f;
                p.Teleport(at, Quaternion.LookRotation(thug.transform.position - at));
                hp.Heal(100);
                thug.Activate();
                t0 = Time.unscaledTime;
                Note("② 다른 조직원: 완벽 회피 뒤 1.6초 기다렸다가 공격");
                break;
            }
            case 6:
                if (hp.Current < 50) hp.Heal(100);
                if (!perfect) { if (Time.unscaledTime - t0 < 20f) { if (dodged && Time.unscaledTime - dodgeAt > 0.6f) dodged = false; step--; return; } }
                next = Time.unscaledTime + 1.6f / Mathf.Max(0.3f, Time.timeScale); // 게임 시간 1.6초 남짓
                break;
            case 7:
                Note($"   1.6초 뒤: 반격 준비 {p.CounterReady}(기대 False), 공격 → 반격 {Counter()}(기대 False)");
                Done();
                break;
        }
    }

    static void Done()
    {
        EditorApplication.update -= Tick;
        EnemyAI.PerfectDodged -= OnPerfect;
        PlayerController.CounterLanded -= OnLanded;
        if (log != null) System.IO.File.WriteAllLines(log, lines);
        log = null;
    }
}
