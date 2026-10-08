using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 싸움 개선 시험(플레이 중, 1장 시작 직후): ① 조직원 세 번째 주먹마다 강타(붉은 예고·센 피해) ② 주먹이 닿기 직전 구르면 완벽 회피
/// (시간 느려짐·적 빈틈·피해 없음) ③ 너무 일찍 구르면 완벽 회피가 아니다 ④ 방패 경찰은 정면 두 번 막으면 밀쳐낸다.
/// </summary>
public static class CombatTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log;
    static int step, heavySeen, attacksSeen, lastAttackCount;
    static float next, t0, hpBefore;
    static PlayerController p;
    static Health hp;
    static EnemyAI thug;
    static bool dodgeQueued, perfectSeen, earlyDone;
    static int phase; // 0: 강타 세기, 1: 완벽 회피, 2: 이른 회피

    public static void Begin(string logPath)
    {
        Application.runInBackground = true;
        log = logPath; lines.Clear(); step = 0; heavySeen = attacksSeen = 0; phase = 0;
        dodgeQueued = perfectSeen = earlyDone = false;
        t0 = Time.unscaledTime; next = Time.unscaledTime + 0.5f;
        EnemyAI.PerfectDodged -= OnPerfect;
        EnemyAI.PerfectDodged += OnPerfect;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void OnPerfect(EnemyAI e) { perfectSeen = true; Note($"   · 완벽 회피 사건: {e.name}, 시간 배율 {Time.timeScale:0.00}, 빈틈 {e.Exposed}"); }
    static void Note(string t) { t = $"[{Time.unscaledTime - t0:0.0}s] {t}"; lines.Add(t); Debug.Log("[싸움 시험] " + t); }
    static string State(EnemyAI e) => typeof(EnemyAI).GetField("state", Any).GetValue(e).ToString();
    static float Timer(EnemyAI e) => (float)typeof(EnemyAI).GetField("timer", Any).GetValue(e);
    static int AttackCount(EnemyAI e) => (int)typeof(EnemyAI).GetField("attackCount", Any).GetValue(e);
    static void Dodge() => typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(p, new object[] { -p.transform.forward });

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Done(); return; }
        EditorApplication.QueuePlayerLoopUpdate();

        // 매 프레임: 적이 주먹을 준비하는 순간을 지켜본다(완벽 회피는 프레임 단위 타이밍이라)
        if (thug && step >= 2 && step < 6)
        {
            if (State(thug) == "Windup")
            {
                bool heavy = thug.HeavyTelegraph;
                float duration = heavy ? thug.heavyWindup : thug.windup;
                float left = duration - Timer(thug);
                if (phase == 1 && !dodgeQueued && left <= 0.15f) { dodgeQueued = true; hpBefore = hp.Current; Dodge(); Note($"   완벽 회피 시도: 주먹까지 {left:0.00}초 남기고 구름(강타 {heavy})"); }
                if (phase == 2 && !dodgeQueued && left <= 0.6f && left > 0.45f) { dodgeQueued = true; hpBefore = hp.Current; Dodge(); Note($"   이른 회피 시도: 주먹까지 {left:0.00}초 남기고 구름"); }
            }
            int ac = AttackCount(thug);
            if (ac != lastAttackCount) { lastAttackCount = ac; attacksSeen++; }
        }

        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.25f;
        switch (step++)
        {
            case 0:
            {
                var d = Object.FindAnyObjectByType<Chapter1Director>();
                if (!d || d.CurrentPhase != Chapter1Director.Phase.Sneak || StoryState.InputLocked) { step--; return; }
                p = d.player; hp = p.GetComponent<Health>();
                thug = GameObject.Find("Thug_Exit_B").GetComponent<EnemyAI>();
                thug.GetComponent<PatrolSight>().Holding = true;
                Vector3 at = thug.transform.position + thug.transform.forward * 1.3f;
                at.y = thug.transform.position.y + 0.02f;
                p.Teleport(at, Quaternion.LookRotation(thug.transform.position - at));
                thug.Activate();
                lastAttackCount = AttackCount(thug);
                Note($"① 조직원(강타 {thug.heavyEvery}번째마다) 앞에 서서 맞기만 한다");
                break;
            }
            case 1:
            {
                // 강타를 셀 때까지(최대 12초) 맞는다 — 체력이 모자라면 채워 준다
                if (hp.Current < 40) hp.Heal(100);
                if (thug.HeavyTelegraph) heavySeen++;
                if (attacksSeen < 3 && Time.unscaledTime - t0 < 14f) { step--; return; }
                Note($"   주먹 {attacksSeen}번 동안 강타 예고 프레임 {heavySeen}번 보임, 체력 {hp.Current} (기대: 3번째가 강타)");
                hp.Heal(100);
                phase = 1; dodgeQueued = false;
                break;
            }
            case 2:
                // ② 완벽 회피: 다음 주먹을 기다렸다가 닿기 0.15초 전에 구른다
                if (!dodgeQueued || State(thug) == "Windup") { if (Time.unscaledTime - t0 < 40f) { step--; return; } }
                next = Time.unscaledTime + 0.1f;
                break;
            case 3:
                Note($"② 완벽 회피 뒤: 사건 {perfectSeen}, 적 빈틈 {thug.Exposed}, 적 상태 {State(thug)}, 받은 피해 {hpBefore - hp.Current} (기대 True, True, Stagger, 0)");
                phase = 2; dodgeQueued = false;
                next = Time.unscaledTime + 1.6f;
                break;
            case 4:
                if (!dodgeQueued || State(thug) == "Windup") { if (Time.unscaledTime - t0 < 60f) { step--; return; } }
                next = Time.unscaledTime + 0.4f;
                break;
            case 5:
            {
                Note($"③ 이른 회피 뒤: 빈틈 {thug.Exposed} (기대 False)");
                thug.Deactivate();
                thug.Health.TakeDamage(999, thug.transform.position); // 방패 시험에 끼어들지 않게
                // ④ 방패 경찰 정면 두 번
                var cop = GameObject.Find("Cop_Shield_A").GetComponent<EnemyAI>();
                Vector3 front = cop.transform.position + cop.transform.forward * 1.4f;
                front.y = cop.transform.position.y + 0.02f;
                p.Teleport(front, Quaternion.LookRotation(cop.transform.position - front));
                hp.Heal(100);
                hpBefore = hp.Current;
                cop.Health.TakeDamage(6, p.transform.position);
                Note($"④ 방패 정면 첫 주먹: 방패 체력 {cop.Health.Current}/{cop.Health.Max}");
                next = Time.unscaledTime + 0.3f;
                break;
            }
            case 6:
            {
                var cop = GameObject.Find("Cop_Shield_A").GetComponent<EnemyAI>();
                Vector3 before = p.transform.position;
                cop.Health.TakeDamage(6, p.transform.position);
                Note($"   두 번째 주먹(막힘) 직후 — 다음 프레임 밀치기");
                next = Time.unscaledTime + 0.35f;
                break;
            }
            case 7:
            {
                var cop = GameObject.Find("Cop_Shield_A").GetComponent<EnemyAI>();
                Note($"   밀치기 뒤: 레이 체력 {hpBefore}→{hp.Current}, 레이 상태 {typeof(PlayerController).GetField("state", Any).GetValue(p)}, 방패와 거리 {Vector3.Distance(p.transform.position, cop.transform.position):0.0}m(처음 1.4m), 방패 체력 {cop.Health.Current} (기대: 4 깎임, 밀려나 거리가 늘어남, 방패 그대로)");
                Done();
                break;
            }
        }
    }

    static void Done()
    {
        EditorApplication.update -= Tick;
        EnemyAI.PerfectDodged -= OnPerfect;
        if (log != null) System.IO.File.WriteAllLines(log, lines);
    }
}
