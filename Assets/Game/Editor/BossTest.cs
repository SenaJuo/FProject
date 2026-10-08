using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 구역 3 보스 시험(플레이 중, 1장 시작 직후 → 항구로 건너뛴다):
///  ① 3번 부두 앞 마당에 들어서면 반장 소개 컷신 → 보스 싸움(목표·보스 활성)
///  ② 체력 60% 아래 → 2단계(권총, 숨은 부하 둘 등장)
///  ③ 25% 아래 → 테이프를 태우러 달려간다 → 불에 들어가면 시간이 멈추고 R 안내 → 되감기
///  ④ 되감은 뒤 두 번째 달리기: 바닥에 기억의 길 → 레이가 붙어 때리면 막힌다 → 마지막 말·새벽·대니 녹음 → 끝
/// </summary>
public static class BossTest
{
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots;
    static float since, t0;
    static Chapter1Director d;
    static PlayerController p;
    static Walkman w;

    public static void Begin(string logPath, string shotDir)
    {
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0;
        t0 = since = Time.realtimeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t)
    {
        t = $"[{Time.realtimeSinceStartup - t0:0.0}s] {(d ? d.CurrentPhase.ToString() : "")} | {t}";
        lines.Add(t); Debug.Log("[보스 시험] " + t);
        if (log != null) System.IO.File.WriteAllLines(log, lines);
    }
    static bool Waited(float s) => Time.realtimeSinceStartup - since > s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Shot(string n) { ScreenCapture.CaptureScreenshot($"{dir}/{shots++:00}_{n}.png"); Note("사진 " + n); }
    static void End(string why) { Note("시험 끝: " + why); EditorApplication.update -= Tick; }
    static void Tp(Vector3 at, Vector3 lookAt) { var f = lookAt - at; f.y = 0f; p.Teleport(at, Quaternion.LookRotation(f)); d.gameplayRig.SnapBehindTarget(); }

    static string seen;
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (d && StoryState.Line != seen && !string.IsNullOrEmpty(StoryState.Line)) { seen = StoryState.Line; Note($"자막 [{StoryState.Speaker}] {StoryState.Line}"); }
        switch (step)
        {
            case 0:
            {
                d = Object.FindAnyObjectByType<Chapter1Director>();
                if (!d || d.CurrentPhase != Chapter1Director.Phase.Sneak || StoryState.InputLocked) return;
                p = d.player; w = p.GetComponent<Walkman>();
                p.GetComponent<Health>().Invulnerable = true;
                foreach (var t in w.tapes) t.charges = 3;
                // 감독을 항구 단계로 건너뛰게: 앞 구역 목표를 순서대로 강제 진행하는 대신, 항구 철조망 문부터 다시 시작
                var run = typeof(Chapter1Director).GetMethod("HarborFromGate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                d.StopAllCoroutines();
                StoryState.CutsceneActive = false; StoryState.ControlLocked = false;
                d.StartCoroutine((System.Collections.IEnumerator)run.Invoke(d, null));
                // 철조망 문은 연 것으로(F를 누른 것처럼)
                var used = (System.Action)typeof(StoryInteractable).GetField("Used", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(d.gateUse);
                used?.Invoke();
                Tp(new Vector3(164f, 0.02f, 322f), new Vector3(164f, 0f, 400f));
                Note("항구 마당(문 안)부터 시작");
                Next();
                break;
            }
            case 1:
                if (Waited(1f)) { Tp(new Vector3(182f, 0.02f, 401f), new Vector3(201f, 0f, 417f)); Note("부두 앞 마당으로(컨테이너 쪽에서)"); Next(); }
                break;
            case 2:
                if (d.CurrentPhase == Chapter1Director.Phase.BossIntro && Waited(4f)) { Shot("boss_intro"); Next(); }
                else if (Waited(10f) && d.CurrentPhase != Chapter1Director.Phase.BossIntro) End("반장 소개가 시작되지 않음");
                break;
            case 3:
                if (d.CurrentPhase == Chapter1Director.Phase.Boss && Waited(2f))
                {
                    Note($"싸움: 목표 '{StoryState.Objective}', 반장 활성 {d.boss.IsActive}, 체력 {d.boss.Health.Current}/{d.boss.Health.Max}");
                    Shot("boss_fight");
                    d.boss.Health.TakeDamage(d.boss.Health.Max / 2, p.transform.position);
                    Next();
                }
                else if (Waited(30f)) End("싸움으로 넘어가지 않음");
                break;
            case 4:
                if (Waited(2.5f))
                {
                    int helpers = 0; foreach (var r in d.bossBrain.reinforcements) if (r && r.gameObject.activeInHierarchy && r.IsActive) helpers++;
                    Note($"2단계: 단계 {d.bossBrain.Phase}(기대 2), 권총 {d.boss.ranged}, 부하 등장 {helpers}(기대 2), 체력 {d.boss.Health.Current}");
                    Shot("boss_phase2");
                    foreach (var r in d.bossBrain.reinforcements) if (r && r.gameObject.activeInHierarchy) r.GetComponent<Health>().TakeDamage(999, p.transform.position);
                    d.boss.Health.TakeDamage(d.boss.Health.Current - d.boss.Health.Max / 5, p.transform.position);
                    Tp(new Vector3(222f, 0.02f, 404f), new Vector3(201f, 0f, 413f)); // 멀리 떨어져서 반장이 불에 닿게 둔다
                    Next();
                }
                break;
            case 5:
                if (d.CurrentPhase == Chapter1Director.Phase.Burn && Waited(0.8f)) { Note($"3단계 달리기: 목표 '{StoryState.Objective}'"); Shot("burn_run"); Next(); }
                else if (Waited(8f)) End("3단계(태우러 달리기)로 넘어가지 않음");
                break;
            case 6:
                if (!string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.3f))
                {
                    Note($"불에 들어감: 안내 '{StoryState.Prompt}', 시간 배율 {Time.timeScale:0.00}, 목표 '{StoryState.Objective}'");
                    Shot("burn_prompt");
                    Note($"되감기 {w.RewindSteps(1)}");
                    Next();
                }
                else if (Waited(14f)) End("불에 들어가 R 안내가 뜨지 않음");
                break;
            case 7:
                if (!w.Rewinding && Waited(1.5f))
                {
                    var line = GameObject.Find("BossMemoryPath");
                    Note($"되감은 뒤: 시간 배율 {Time.timeScale:0.00}, 기억의 길 {(line ? line.GetComponent<LineRenderer>().enabled.ToString() : "없음")}, 반장 테이프 손에 {(d.bossBrain.heldTape ? d.bossBrain.heldTape.parent != null : false)}");
                    Shot("after_rewind");
                    // 반장 곁으로 붙어 때린다
                    var bp = d.boss.transform.position;
                    Tp(bp - d.boss.transform.forward * 1.2f, bp);
                    Next();
                }
                break;
            case 8:
                if (Waited(0.3f))
                {
                    // 도망치는 반장의 등 뒤에 붙어도 Q 제압 대상이 되면 안 된다(엔딩까지 목 조르는 자세로 남는 버그)
                    Note($"반장 등 뒤 Q 제압 대상: {(Takedown.Target ? Takedown.Target.name : "없음")}(기대 없음)");
                    d.boss.Health.TakeDamage(10, p.transform.position);
                    Next();
                }
                break;
            case 9:
                if (d.CurrentPhase == Chapter1Director.Phase.Finale && Waited(3f)) { Shot("finale_down"); Next(); }
                else if (Waited(10f) && d.CurrentPhase != Chapter1Director.Phase.Finale) End("막은 뒤 마무리로 넘어가지 않음");
                break;
            case 10:
                if (StoryState.Speaker == "대니 (녹음)" && Waited(0.5f)) { Shot("dawn"); Next(); }
                else if (Waited(30f)) End("새벽 녹음 장면이 나오지 않음");
                break;
            case 11:
                if (d.CurrentPhase == Chapter1Director.Phase.End && Waited(1f)) End("완료");
                else if (Waited(45f)) End("끝 단계로 넘어가지 않음");
                break;
        }
    }
}
