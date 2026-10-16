using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이 검수에서 찾은 버그 6개가 고쳐졌는지 확인한다(플레이 중, 1장 시작 직후 — 숨어 지나가는 단계에서).
///  ① 장면을 넘기는 자리를 미리 밟아도 세지 않는다  ② 검문소를 정리하면 길잡이가 광장 입구로
///  ③ 계단 발치에서 길잡이가 옥상으로  ④ 출구 경비를 구역을 깨우지 않고 몰래 제압할 수 있다
///  ⑤ R은 음악 테이프보다 재생이 안 되는 테이프를 먼저 쓴다  ⑥ 총잡이는 처음 자리에서 6m 넘게 물러나지 않는다
/// </summary>
public static class BugFixTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly List<string> lines = new();
    static string log;
    static int step;
    static float next, t0, maxRetreat;
    static Chapter1Director d;
    static PlayerController p;
    static EnemyAI guard, gunner;
    static Vector3 gunnerSpawn;

    public static void Begin(string logPath)
    {
        Application.runInBackground = true;
        log = logPath; lines.Clear(); step = 0; maxRetreat = 0f;
        t0 = Time.unscaledTime; next = Time.unscaledTime + 0.5f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { t = $"[{Time.unscaledTime - t0:0.0}s] {t}"; lines.Add(t); Debug.Log("[버그 시험] " + t); }
    static T Get<T>(object o, string f) => (T)o.GetType().GetField(f, Any).GetValue(o);

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.3f;
        switch (step++)
        {
            case 0:
                d = Object.FindAnyObjectByType<Chapter1Director>();
                p = d ? d.player : null;
                if (!d || d.CurrentPhase != Chapter1Director.Phase.Sneak || StoryState.InputLocked) { step--; return; }
                p.GetComponent<Health>().Invulnerable = true; // 시험 중에는 쓰러지지 않게
                // ⑤ R은 끼운 테이프만 쓴다(사용자 규칙): 음악 테이프를 끼워 두면 그 테이프로 되감는다
                var w = p.GetComponent<Walkman>();
                var music = new CassetteTape { title = "시험 음악", musicId = "rain_jazz", trackTitles = new[] { "a", "b", "c" }, playable = true, charges = 1 };
                w.AddTape(music);
                w.Equip(w.tapes.IndexOf(music));
                var pick = w.NextManualRewindTape;
                Note($"⑤ 끼운 테이프 '{w.Inserted?.title}'(음악, 횟수 1) → R이 쓸 테이프 '{pick?.title}' (기대: 끼운 테이프 = '시험 음악')");
                w.tapes.Remove(music);
                w.Equip(0);
                // ① 큰길 끝 자리를 미리 밟는다
                Tp(d.endTrigger.transform.position + Vector3.down * 1.3f);
                next = Time.unscaledTime + 0.6f;
                break;
            case 1:
                Note($"① 숨어 지나가는 단계에 큰길 끝 자리를 밟음: 셈 {d.endTrigger.Fired}, 켜짐 {d.endTrigger.enabled} (기대 False, False)");
                // ④ 출구 경비 A의 등 뒤(구역 밖)로
                guard = GameObject.Find("Thug_Exit_A").GetComponent<EnemyAI>();
                gunner = d.exitGunner;
                gunnerSpawn = gunner.transform.position;
                var sight = guard.GetComponent<PatrolSight>();
                sight.Holding = true;
                // 옆 경비 B가 다른 쪽을 보는 틈에 제압한다(2026-10-16: 동료가 당하는 걸 보면 소리친다 — B를 붙잡아 두어 "안 보는 틈"을 만든다)
                var other = GameObject.Find("Thug_Exit_B").GetComponent<PatrolSight>();
                other.Holding = true;
                Vector3 behind = guard.transform.position - guard.transform.forward * 1.1f;
                behind.y = guard.transform.position.y + 0.02f;
                p.Teleport(behind, Quaternion.LookRotation(guard.transform.position - behind));
                next = Time.unscaledTime + 0.5f;
                break;
            case 2:
            {
                bool inZone = d.exitZone.GetComponent<Collider>().bounds.Contains(p.transform.position);
                Note($"④ 경비 등 뒤: 위치 {p.transform.position:F1}, 출구 구역 안 {inZone}, 제압 안내 {(Takedown.Target ? Takedown.Target.name : "없음")}, 구역 시작 {Get<bool>(d.exitZone, "started")} (기대 False, Thug_Exit_A, False)");
                if (Takedown.Target == guard)
                {
                    var td = p.GetComponent<Takedown>();
                    td.StartCoroutine((IEnumerator)typeof(Takedown).GetMethod("Perform", Any).Invoke(td, new object[] { guard }));
                }
                next = Time.unscaledTime + 2.6f; // 목 조르기는 2.1초 뒤에 축 늘어진다
                break;
            }
            case 3:
                Note($"   제압 뒤: 경비 쓰러짐 {guard.Health.IsDead}, 구역 시작 {Get<bool>(d.exitZone, "started")}, 단계 {d.CurrentPhase}, 총잡이 깨어남 {gunner.IsActive} (기대 True, False, Sneak, False)");
                // ⑥ 총잡이 바로 앞으로 가서(구역에 들어감) 계속 붙어 다닌다
                Chase();
                next = Time.unscaledTime + 0.3f;
                break;
            case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13:
            case 14: case 15: case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23:
                maxRetreat = Mathf.Max(maxRetreat, Vector3.Distance(gunner.transform.position, gunnerSpawn));
                Chase();
                break;
            case 24:
                Note($"⑥ 총잡이에게 6초 동안 붙어 다님: 처음 자리에서 가장 멀리 간 거리 {maxRetreat:0.0}m (기대 6.5m 이하), 깨어남 {gunner.IsActive}");
                // ② ③ 길잡이: 단계를 흉내 내 감독의 갱신을 한 번 부른다
                var setPhase = typeof(Chapter1Director).GetProperty("CurrentPhase").GetSetMethod(true);
                var update = typeof(Chapter1Director).GetMethod("UpdateZone2", Any);
                setPhase.Invoke(d, new object[] { Chapter1Director.Phase.Checkpoint });
                typeof(CombatZone).GetProperty("IsCleared").GetSetMethod(true).Invoke(d.checkpointZone, new object[] { true });
                StoryState.Waypoint = d.checkpointLook;
                update.Invoke(d, null);
                Note($"② 검문소 정리 뒤 길잡이: {(StoryState.Waypoint ? StoryState.Waypoint.name : "없음")} (기대 SquareTrigger)");
                setPhase.Invoke(d, new object[] { Chapter1Director.Phase.Square });
                Tp(d.stairsFoot.position + new Vector3(-6f, 0f, 0f));
                update.Invoke(d, null);
                string far = StoryState.Waypoint ? StoryState.Waypoint.name : "없음";
                Tp(d.stairsFoot.position + new Vector3(-1f, 0f, 0f));
                update.Invoke(d, null);
                Note($"③ 계단에서 6m 떨어짐: {far} (기대 StairsFoot) / 계단 발치: {(StoryState.Waypoint ? StoryState.Waypoint.name : "없음")} (기대 RoofTop)");
                EditorApplication.update -= Tick;
                System.IO.File.WriteAllLines(log, lines);
                break;
        }
    }

    static void Tp(Vector3 at) => p.Teleport(at + Vector3.up * 0.05f, p.transform.rotation);

    static void Chase()
    {
        // 레이를 총잡이 3m 앞(공원 쪽)에 계속 둔다 — 총잡이는 물러나려 한다
        Vector3 to = gunner.transform.position - new Vector3(140f, 0f, 60f);
        to.y = 0f;
        Vector3 at = gunner.transform.position - to.normalized * 3f;
        at.y = gunner.transform.position.y + 0.02f;
        p.Teleport(at, Quaternion.LookRotation(gunner.transform.position - at));
    }
}
