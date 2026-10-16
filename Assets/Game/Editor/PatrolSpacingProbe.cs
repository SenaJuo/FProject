using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 순찰 간격 재기(2026-10-16 "순찰하는 애들이 너무 붙어 있다 — 옆 애가 제압당해도 모른다"):
/// 플레이 중에 장면을 하나씩 불러 순찰을 실제로 걷게 두고(들키지 않게 시야만 끈다, 4배속),
/// 순찰 둘이 3.5m 안에 붙어 있는 시간 비율과 가장 가까웠던 거리를 짝마다 센다. 결과: QA/patrol/report.txt
/// </summary>
public static class PatrolSpacingProbe
{
    static readonly string[] Scenes = { "Chapter1", "Chapter2", "Chapter3", "Chapter4", "Chapter5" };
    const float Warmup = 2f, Sample = 30f, Close = 3.5f; // 실제 초(4배속이라 게임 시간 120초)

    static string dir;
    static readonly List<string> report = new();
    static int index, stage, samples;
    static float t0, nextSample;
    static PatrolSight[] sights;
    static int[,] closeCount;
    static float[,] minDist;
    public static string[] Only;

    public static void Begin() => Begin(@"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\patrol");

    public static void Begin(string outDir)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        dir = outDir;
        System.IO.Directory.CreateDirectory(dir);
        report.Clear();
        index = -1;
        NextScene();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static string[] List => Only ?? Scenes;

    static void NextScene()
    {
        index++;
        if (index >= List.Length) { Finish(); return; }
        stage = 0;
        t0 = Time.realtimeSinceStartup;
        Time.timeScale = 1f;
        SceneManager.LoadScene(List[index]);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        float t = Time.realtimeSinceStartup - t0;
        if (stage == 0 && t > Warmup)
        {
            sights = Object.FindObjectsByType<PatrolSight>(FindObjectsSortMode.None).ToArray();
            foreach (var s in sights) { s.CanDetect = false; s.Holding = false; s.Staged = false; }
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p) p.GetComponent<Health>().Invulnerable = true;
            int n = sights.Length;
            closeCount = new int[n, n];
            minDist = new float[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) minDist[i, j] = float.MaxValue;
            samples = 0;
            StoryState.ControlLocked = false;
            Time.timeScale = 4f;
            stage = 1;
            nextSample = t;
            report.Add($"== {List[index]}  순찰 {n}명");
            return;
        }
        if (stage == 1)
        {
            if (t >= nextSample)
            {
                nextSample = t + 0.1f;
                samples++;
                for (int i = 0; i < sights.Length; i++)
                for (int j = i + 1; j < sights.Length; j++)
                {
                    var a = sights[i]; var b = sights[j];
                    if (!a || !b || a.Enemy.Health.IsDead || b.Enemy.Health.IsDead) continue;
                    Vector3 d = a.transform.position - b.transform.position;
                    if (Mathf.Abs(d.y) > 2f) continue; // 다른 층
                    d.y = 0f;
                    float m = d.magnitude;
                    if (m < minDist[i, j]) minDist[i, j] = m;
                    if (m < Close) closeCount[i, j]++;
                }
            }
            if (t > Warmup + Sample)
            {
                var rows = new List<(float ratio, string text)>();
                for (int i = 0; i < sights.Length; i++)
                for (int j = i + 1; j < sights.Length; j++)
                {
                    if (minDist[i, j] > 8f) continue;
                    float ratio = closeCount[i, j] / (float)Mathf.Max(1, samples);
                    rows.Add((ratio, $"{ratio * 100f,5:0}%  최소 {minDist[i, j],4:0.0}m  {sights[i].name} ({Pos(sights[i])}) ↔ {sights[j].name} ({Pos(sights[j])})"));
                }
                foreach (var r in rows.OrderByDescending(r => r.ratio)) report.Add("  " + r.text);
                if (rows.Count == 0) report.Add("  8m 안으로 다가가는 짝 없음");
                Time.timeScale = 1f;
                NextScene();
            }
        }
    }

    static string Pos(PatrolSight s)
    {
        var r = s.route != null && s.route.Length > 0 ? s.route[0].position : s.transform.position;
        return $"{r.x:0},{r.z:0}";
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        Time.timeScale = 1f;
        report.Add("끝");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "report.txt"), report);
        Debug.Log("[순찰 간격] " + string.Join("\n", report));
    }
}
