using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 처형기 · 기억의 일격 시험(플레이 중, Chapter4 장면 — 마당의 옛 파트너를 쓴다):
///  ① 처형기 처음: 예고 없음 → 레이가 맞는다(큰 피해) → 이제 레이가 기억 ② 되감기 → 「기억의 일격」 준비
///  ③ 레이 첫 주먹 = 세 배 ④ 다음 처형기는 보라색 예고(ExecuteTelegraph).
/// </summary>
public static class ExecuteMemoryTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots, hpBefore;
    static float since;
    static EnemyAI e;
    static PlayerController p;
    static Walkman w;
    static Health ph;
    static bool sawTelegraph;

    public static void Begin(string logPath, string shotDir)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0; since = Time.realtimeSinceStartup; sawTelegraph = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { lines.Add(t); Debug.Log("[처형기 시험] " + t); }
    static bool Waited(float s) => Time.realtimeSinceStartup - since >= s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Shot(string n) => ScreenCapture.CaptureScreenshot($"{dir}/{shots++:00}_{n}.png");
    static void SetCount(int n) => typeof(EnemyAI).GetField("attackCount", Any).SetValue(e, n);

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { End("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        switch (step)
        {
            case 0:
                if (Waited(4f))
                {
                    p = Object.FindAnyObjectByType<PlayerController>(); w = p.GetComponent<Walkman>(); ph = p.GetComponent<Health>();
                    foreach (var x in Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (x.name == "Partner") e = x;
                    StoryState.ControlLocked = false; StoryState.CutsceneActive = false; StoryState.Fade = 0f;
                    var t = new CassetteTape { title = "시험", playable = false, charges = 3 }; w.AddTape(t); w.Equip(w.tapes.IndexOf(t));
                    // 레이 앞 2m에 파트너를 세우고 다음 공격이 처형기가 되게
                    e.gameObject.SetActive(true);
                    e.Health.Invulnerable = false;
                    e.PlaceAt(p.transform.position + p.transform.forward * 1.6f, Quaternion.LookRotation(-p.transform.forward));
                    SetCount(e.executeEvery - 1);
                    e.Activate();
                    hpBefore = ph.Current;
                    Note($"준비: 처형기 {e.executeEvery}번째마다 {e.executeDamage} 피해, 레이 체력 {hpBefore}, 기억 {EnemyAI.KnownExecute.Contains(e)}");
                    Next();
                }
                break;
            case 1:
                if (e.ExecuteTelegraph) sawTelegraph = true;
                if (ph.Current < hpBefore && Waited(0.3f))
                {
                    Note($"① 처형기(처음): 피해 {hpBefore - ph.Current}(기대 {e.executeDamage}), 예고 보였나 {sawTelegraph}(기대 False), 이제 기억 {EnemyAI.KnownExecute.Contains(e)}(기대 True)");
                    e.Deactivate();
                    Note($"② 되감기 {w.RewindSteps(1)}");
                    Next();
                }
                else if (Waited(8f)) End($"처형기에 맞지 않음(레이 {ph.Current}, 상태 {e.InAction})");
                break;
            case 2:
                if (!w.Rewinding && Waited(0.4f))
                {
                    Note($"   되감은 뒤: 기억의 일격 준비 {PlayerController.MemoryStrikeReady}(기대 True), 기억은 남음 {EnemyAI.KnownExecute.Contains(e)}(기대 True)");
                    Shot("memory_ready");
                    // ③ 첫 주먹: 파트너를 바로 앞에 두고 주먹 판정을 직접 부른다
                    e.Deactivate();
                    e.PlaceAt(p.transform.position + p.transform.forward * 1.1f, Quaternion.LookRotation(-p.transform.forward));
                    e.Health.Invulnerable = false;
                    Next();
                }
                else if (Waited(10f)) End("되감기가 끝나지 않음");
                break;
            case 3:
                if (Waited(0.3f))
                {
                    var combo = (System.Array)typeof(PlayerController).GetField("combo", Any).GetValue(p);
                    var stepObj = combo.GetValue(0);
                    float mult = (float)stepObj.GetType().GetField("damageMultiplier").GetValue(stepObj);
                    int hp0 = e.Health.Current;
                    typeof(PlayerController).GetMethod("PerformHit", Any).Invoke(p, new[] { stepObj });
                    int dealt = hp0 - e.Health.Current;
                    Note($"③ 기억의 일격: 피해 {dealt}(보통 {Mathf.RoundToInt(p.attackPower * mult)} → 기대 세 배 {Mathf.RoundToInt(p.attackPower * mult) * 3}), 소진 {!PlayerController.MemoryStrikeReady}");
                    int hp1 = e.Health.Current;
                    typeof(PlayerController).GetMethod("PerformHit", Any).Invoke(p, new[] { stepObj });
                    Note($"   두 번째 주먹: {hp1 - e.Health.Current}(보통으로 돌아옴)");
                    // ④ 다음 처형기: 이번엔 보라색 예고
                    e.Health.Invulnerable = true; ph.Invulnerable = true;
                    e.PlaceAt(p.transform.position + p.transform.forward * 1.6f, Quaternion.LookRotation(-p.transform.forward));
                    SetCount(e.executeEvery * 2 - 1);
                    sawTelegraph = false;
                    e.Activate();
                    Next();
                }
                break;
            case 4:
                if (e.ExecuteTelegraph && !sawTelegraph) { sawTelegraph = true; Shot("execute_known"); }
                if (Waited(4f))
                {
                    Note($"④ 기억한 처형기: 보라색 예고 {sawTelegraph}(기대 True)");
                    ph.Invulnerable = false;
                    End("완료");
                }
                break;
        }
    }

    static void End(string why)
    {
        EditorApplication.update -= Tick;
        Note("시험 끝: " + why);
        if (log != null) System.IO.File.WriteAllLines(log, lines);
        log = null;
    }
}
