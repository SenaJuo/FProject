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
                Tp(new Vector3(164f, 0.02f, 272f), new Vector3(164f, 0f, 320f));
                Note("항구 마당(문 안)부터 시작");
                Next();
                break;
            }
            case 1:
                if (Waited(1f)) { Tp(new Vector3(180f, 0.02f, 319f), new Vector3(198f, 0f, 333.5f)); Note("부두 앞 마당으로(컨테이너 쪽에서)"); Next(); }
                break;
            case 2:
                // 등장 장면은 Space로 건너뛴다(2026-10-16: 건너뛴 표시가 남아 마지막 대사 · 새벽 장면까지 건너뛰어지던 버그를 같이 본다)
                if (d.CurrentPhase == Chapter1Director.Phase.BossIntro && Waited(1.5f)) { Shot("boss_intro"); CutsceneSkip.SimulateHold = true; Next(); }
                else if (Waited(10f) && d.CurrentPhase != Chapter1Director.Phase.BossIntro) End("반장 소개가 시작되지 않음");
                break;
            case 3:
                if (d.CurrentPhase == Chapter1Director.Phase.Boss) CutsceneSkip.SimulateHold = false;
                if (d.CurrentPhase == Chapter1Director.Phase.Boss && Waited(2f))
                {
                    Note($"등장 장면 건너뜀 뒤 건너뛰기 표시 {StoryState.SkipRequested}(기대 False)");
                    Note($"싸움: 목표 '{StoryState.Objective}', 반장 활성 {d.boss.IsActive}, 체력 {d.boss.Health.Current}/{d.boss.Health.Max}");
                    Shot("boss_fight");
                    d.boss.Health.TakeDamage(d.boss.Health.Max / 2, p.transform.position);
                    Next();
                }
                else if (Waited(30f)) End("싸움으로 넘어가지 않음");
                break;
            case 4:
                if (Waited(0.5f))
                {
                    // 넘어가는 장면: 외치며 엄폐물로 달려가는 중(순간 이동하지 않는다), 그동안 무적
                    float toCover = d.bossBrain.coverPoint ? Vector3.Distance(d.boss.transform.position, d.bossBrain.coverPoint.position) : -1f;
                    Note($"2단계로 넘어가는 중: 진행 {d.bossBrain.Transitioning}(기대 True), 무적 {d.boss.Health.Invulnerable}(기대 True), 자막 [{StoryState.Speaker}] {StoryState.Line}, 엄폐물까지 {toCover:0.0}m");
                    Shot("boss_to_cover");
                    Next();
                }
                break;
            case 5:
                if (!d.bossBrain.Transitioning && Waited(0.3f))
                {
                    int helpers = 0; foreach (var r in d.bossBrain.reinforcements) if (r && r.gameObject.activeInHierarchy && r.IsActive) helpers++;
                    float atCover = d.bossBrain.coverPoint ? Vector3.Distance(d.boss.transform.position, d.bossBrain.coverPoint.position) : -1f;
                    Note($"2단계: 단계 {d.bossBrain.Phase}(기대 2), 권총 {d.boss.ranged}, 사격 거리 {d.boss.shootRange:0}m(기대 30), 부하 등장 {helpers}(기대 2), 체력 {d.boss.Health.Current}, 엄폐물까지 {atCover:0.0}m, 무적 {d.boss.Health.Invulnerable}(기대 False), 활성 {d.boss.IsActive}");
                    Shot("boss_phase2");
                    foreach (var r in d.bossBrain.reinforcements) if (r && r.gameObject.activeInHierarchy) r.GetComponent<Health>().TakeDamage(999, p.transform.position);
                    // 큰 한 방(되감기 기억 3배 · 능력 등)으로 25% 아래까지 한꺼번에 깎여도 쓰러지지 않고 소각 장면으로 가야 한다(10-14 버그: 바로 2장으로 넘어감)
                    d.boss.Health.TakeDamage(9999, p.transform.position);
                    Note($"큰 한 방 뒤: 체력 {d.boss.Health.Current}/{d.boss.Health.Max}(기대 {Mathf.FloorToInt(d.boss.Health.Max * 0.25f)}), 쓰러짐 {d.boss.Health.IsDead}(기대 False)");
                    Tp(new Vector3(219f, 0.02f, 320.5f), new Vector3(198f, 0f, 329.5f)); // 멀리 떨어져서 반장이 불에 닿게 둔다
                    Next();
                }
                else if (Waited(6f)) End("엄폐물에 도착하지 못함(넘어가는 장면이 끝나지 않음)");
                break;
            case 6:
                if (d.CurrentPhase == Chapter1Director.Phase.Burn && Waited(0.8f))
                {
                    Note($"3단계 달리기: 목표 '{StoryState.Objective}', 컷신 {StoryState.CutsceneActive}(기대 True)");
                    Shot("burn_run");
                    // 첫 달리기에서 맞아도 막은 것이 되면 안 된다(2026-10-17: 때리면 테이프 장면이 통째로 건너뛰어졌다)
                    d.boss.Health.TakeDamage(10, p.transform.position);
                    Next();
                }
                else if (Waited(8f)) End("3단계(태우러 달리기)로 넘어가지 않음");
                break;
            case 7:
                if (!string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.3f))
                {
                    Note($"불에 들어감: 안내 '{StoryState.Prompt}', 시간 배율 {Time.timeScale:0.00}, 목표 '{StoryState.Objective}'");
                    Shot("burn_prompt");
                    Note($"되감기 {w.RewindSteps(1)}");
                    Next();
                }
                else if (Waited(14f)) End("불에 들어가 R 안내가 뜨지 않음");
                break;
            case 8:
                if (!w.Rewinding && Waited(1.5f))
                {
                    var line = GameObject.Find("BossMemoryPath");
                    Note($"되감은 뒤: 컷신 {StoryState.CutsceneActive}(기대 False), 시간 배율 {Time.timeScale:0.00}, 기억의 길 {(line ? line.GetComponent<LineRenderer>().enabled.ToString() : "없음")}, 반장 테이프 손에 {(d.bossBrain.heldTape ? d.bossBrain.heldTape.parent != null : false)}");
                    Shot("after_rewind");
                    // 반장 곁으로 붙어 때린다
                    var bp = d.boss.transform.position;
                    Tp(bp - d.boss.transform.forward * 1.2f, bp);
                    Next();
                }
                break;
            case 9:
                if (Waited(0.3f))
                {
                    // 도망치는 반장의 등 뒤에 붙어도 Q 제압 대상이 되면 안 된다(엔딩까지 목 조르는 자세로 남는 버그)
                    Note($"반장 등 뒤 Q 제압 대상: {(Takedown.Target ? Takedown.Target.name : "없음")}(기대 없음)");
                    d.boss.Health.TakeDamage(10, p.transform.position);
                    Next();
                }
                break;
            case 10:
                // 막은 뒤: 바로 끝나지 않고 반장이 남은 체력으로 맞선다(2026-10-16) → 다 깎아야 마무리
                if (d.CurrentPhase == Chapter1Director.Phase.Boss && d.boss.IsActive && Waited(1f))
                {
                    Note($"막은 뒤 마지막 싸움: 반장 체력 {d.boss.Health.Current}/{d.boss.Health.Max}(기대 0보다 큼), 싸움 중 {d.boss.IsActive}, 목표 '{StoryState.Objective}'");
                    Shot("last_stand");
                    d.boss.Health.TakeDamage(9999, p.transform.position);
                    step = 13; since = Time.realtimeSinceStartup;
                }
                else if (d.CurrentPhase == Chapter1Director.Phase.Finale) End("막자마자 마무리로 넘어감(남은 체력을 깎지 않음)");
                else if (Waited(10f)) End($"막은 뒤 마지막 싸움이 시작되지 않음(단계 {d.CurrentPhase})");
                break;
            case 13:
                if (d.CurrentPhase == Chapter1Director.Phase.Finale && Waited(3f)) { Shot("finale_down"); step = 11; since = Time.realtimeSinceStartup; }
                else if (Waited(10f) && d.CurrentPhase != Chapter1Director.Phase.Finale) End("반장을 쓰러뜨린 뒤 마무리로 넘어가지 않음");
                break;
            case 11:
                if (StoryState.Speaker == "대니 (녹음)" && Waited(0.5f)) { Shot("dawn"); Next(); }
                else if (Waited(30f)) End("새벽 녹음 장면이 나오지 않음");
                break;
            case 12:
                if (d.CurrentPhase == Chapter1Director.Phase.End && Waited(1f)) End("완료");
                else if (Waited(45f)) End("끝 단계로 넘어가지 않음");
                break;
        }
    }
}
