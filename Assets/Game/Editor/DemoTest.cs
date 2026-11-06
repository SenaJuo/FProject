using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 체험판 흐름 시험(2026-11-06, 플레이 중 · 프롤로그 장면에서 시작):
///  ① 체험판으로 켜고 프롤로그를 끝까지(PrologueFlowTest가 몬다)
///  ② 1장 3번 부두로 바로 넘어온다: '체험판 3번 부두' 제목, 저장 지점 근처, 테이프 둘(가득) · 장르 테이프 끼움, 이어하기 없음
///  ③ 1장 끝 처리 → '체험판 끝' 화면 → 타이틀로 돌아온다(2장으로 가지 않는다)
/// 반장 싸움 자체는 BossTest가 본편에서 시험한다
/// </summary>
public static class DemoTest
{
    static readonly List<string> lines = new();
    static string log, dir;
    static int step;
    static float since, t0, lastBeat;
    static bool sawStageTitle, sawEndTitle;

    public static void Begin(string logPath, string shotDir)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; sawStageTitle = sawEndTitle = false;
        t0 = since = Time.realtimeSinceStartup;
        Demo.Force = true;
        PrologueFlowTest.Begin(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(logPath), "DemoTest_prologue.log"), dir + "/prologue");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시험 시작: 체험판 켬, 프롤로그 진행");
    }

    static void Note(string t)
    {
        t = $"[{Time.realtimeSinceStartup - t0:0.0}s] {SceneManager.GetActiveScene().name} | {t}";
        lines.Add(t); Debug.Log("[체험판 시험] " + t);
        if (log != null) System.IO.File.WriteAllLines(log, lines);
    }
    static bool Waited(float s) => Time.realtimeSinceStartup - since > s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void End(string why) { Note("시험 끝: " + why); Demo.Force = false; EditorApplication.update -= Tick; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        string scene = SceneManager.GetActiveScene().name;
        if (StoryState.Title == "체험판   3번 부두") sawStageTitle = true;
        if (StoryState.Title == "체험판 끝") sawEndTitle = true;
        switch (step)
        {
            case 0: // 프롤로그 → 1장
                if (Time.realtimeSinceStartup - lastBeat > 15f) { lastBeat = Time.realtimeSinceStartup; Note("프롤로그 진행 중"); } // 시험 줄(TestQueue)이 40초 조용하면 끝난 것으로 본다
                if (scene == "Chapter1" && Object.FindAnyObjectByType<Chapter1Director>()) { Note("1장으로 넘어옴"); Next(); }
                else if (scene == "Chapter2") End("실패: 2장으로 넘어감");
                else if (Waited(240f)) End("실패: 프롤로그 뒤 1장으로 넘어오지 않음");
                break;
            case 1:
            {
                var d = Object.FindAnyObjectByType<Chapter1Director>();
                if (d.CurrentPhase != Chapter1Director.Phase.Docks || StoryState.InputLocked || StoryState.Fade > 0.05f)
                {
                    if (Waited(20f)) End($"실패: 3번 부두 단계로 시작하지 않음(단계 {d.CurrentPhase})");
                    break;
                }
                var w = d.player.GetComponent<Walkman>();
                float dist = d.zone3Start ? Vector3.Distance(d.player.transform.position, d.zone3Start.position) : -1f;
                string tapes = string.Join(", ", w.tapes.ConvertAll(t => $"{t.title}({t.Genre}, {t.charges}회)"));
                Note($"부두 시작: 제목 보임 {sawStageTitle}(기대 True) · 저장 지점과 거리 {dist:0.0}m(기대 <3) · 테이프 [{tapes}] · 끼운 테이프 {w.Inserted?.title}(기대 시티팝) · 이어하기 {SaveGame.HasSave}(기대 False)");
                bool ok = sawStageTitle && dist >= 0f && dist < 3f && w.tapes.Count >= 2 && w.tapes.TrueForAll(t => t.charges == CassetteTape.MaxCharges)
                          && w.Inserted != null && w.Inserted.Genre == TapeGenre.CityPop && !SaveGame.HasSave;
                if (!ok) { End("실패: 부두 시작 상태가 기대와 다름"); break; }
                ScreenCapture.CaptureScreenshot(dir + "/01_demo_pier.png");
                // 반장 싸움은 건너뛰고 1장 끝 처리만 부른다
                d.StopAllCoroutines();
                var m = typeof(Chapter1Director).GetMethod("ChapterEnd", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                d.StartCoroutine((System.Collections.IEnumerator)m.Invoke(d, null));
                Next();
                break;
            }
            case 2: // 체험판 끝 → 타이틀
                if (sawEndTitle && Waited(5f) && step == 2 && !System.IO.File.Exists(dir + "/02_demo_end.png")) ScreenCapture.CaptureScreenshot(dir + "/02_demo_end.png");
                if (scene == "Title") { End(sawEndTitle ? "완료 — 체험판 끝 화면 뒤 타이틀로 돌아옴" : "실패: 끝 화면 없이 타이틀로"); }
                else if (scene == "Chapter2") End("실패: 2장으로 넘어감");
                else if (Waited(30f)) End("실패: 타이틀로 돌아오지 않음");
                break;
        }
    }
}
