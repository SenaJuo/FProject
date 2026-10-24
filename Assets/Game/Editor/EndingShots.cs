using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 엔딩 장면 점검(2026-10-23): 플레이 중에 엔딩을 바로 틀고, 장면이 바뀔 때와 1.5초마다 화면을 찍는다 → QA/ending/{A|B}/.
/// 끝 카드까지 가서 타이틀로 돌아가면 멈춘다. 기록: QA/ending/{A|B}/log.txt
/// </summary>
public static class EndingShots
{
    static string dir;
    static int n;
    static string lastBeat;
    static double lastShot, started;
    static System.Collections.Generic.List<string> log = new();

    public static void Begin(EndingStage.Kind kind)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        dir = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\ending\" + (kind == EndingStage.Kind.Rewind ? "A" : "B");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        n = 0; lastBeat = null; log.Clear();
        Application.runInBackground = true;
        started = EditorApplication.timeSinceStartup;
        EndingStage.Play(kind);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop("플레이 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        double now = EditorApplication.timeSinceStartup;
        string beat = EndingStage.Beat;
        bool changed = beat != lastBeat;
        if (changed || now - lastShot > 1.5)
        {
            if (changed) log.Add($"[{now - started:0.0}s] 장면 {beat} — 자막 [{StoryState.Speaker}] {StoryState.Line} · 제목 '{StoryState.Title}' · 어둠 {StoryState.Fade:0.00}");
            lastBeat = beat;
            lastShot = now;
            ScreenCapture.CaptureScreenshot($"{dir}/{n++:000}_{beat}.png");
        }
        if (EndingStage.Finished && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Title") Stop("끝 — 타이틀로 돌아옴");
        else if (now - started > 240) Stop("시간 초과");
    }

    static void Stop(string why)
    {
        EditorApplication.update -= Tick;
        log.Add($"[{EditorApplication.timeSinceStartup - started:0.0}s] {why}");
        File.WriteAllLines(dir + "/log.txt", log);
    }
}
