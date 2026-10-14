using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 빌드 성능 측정(2026-10-13 "전체 최적화"): 빌드한 게임을 <c>-benchmark</c>로 실행하면 장면마다 4초 기다린 뒤 8초 동안
/// 프레임 시간 · GPU 시간 · GC 횟수 · 메모리를 재서 실행 파일 옆 benchmark.txt에 적고 스스로 끝난다.
/// 실제 저장은 건드리지 않는다(SaveGame 끔). 평소 실행에는 아무 일도 하지 않는다.
/// </summary>
public class Benchmark : MonoBehaviour
{
    static readonly string[] Scenes = { "Prologue", "Chapter1", "Chapter2", "Chapter3", "Chapter4", "Chapter5" };
    const float Warmup = 4f, Sample = 8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Application.isEditor || !System.Environment.GetCommandLineArgs().Contains("-benchmark")) return;
        var go = new GameObject("Benchmark");
        DontDestroyOnLoad(go);
        go.AddComponent<Benchmark>();
    }

    readonly List<string> lines = new();
    readonly FrameTiming[] timing = new FrameTiming[1];

    IEnumerator Start()
    {
        SaveGame.Enabled = false;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0; // 화면 주사율에 묶이지 않은 실제 속도
        lines.Add($"빌드 측정 {System.DateTime.Now:yyyy-MM-dd HH:mm} · {Screen.width}x{Screen.height} · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize}MB) · {SystemInfo.processorType}");
        foreach (var scene in Scenes)
        {
            SceneManager.LoadScene(scene);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < Warmup) { Hold(); yield return null; }
            var frames = new List<float>(); var gpu = new List<float>();
            // -profile(개발 빌드): 장면마다 프로파일러 기록을 파일로 남긴다(에디터의 PerfProbe.AnalyzeRaw로 느린 프레임을 뜯어본다)
            bool profile = Debug.isDebugBuild && System.Environment.GetCommandLineArgs().Contains("-profile");
            if (profile)
            {
                UnityEngine.Profiling.Profiler.logFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), $"prof_{scene}_0");
                UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                UnityEngine.Profiling.Profiler.enabled = true;
            }
            int gc0 = System.GC.CollectionCount(0);
            var spikes = new List<string>();
            int chunk = 0;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < Sample)
            {
                Hold();
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                // 느린 프레임(20ms 넘게): 언제, 그때 무슨 장면이었는지 적는다
                if (Time.unscaledDeltaTime > 0.02f && spikes.Count < 8)
                    spikes.Add($"    느린 프레임 {Time.unscaledDeltaTime * 1000f:0.0}ms @ {Time.realtimeSinceStartup - t0:0.00}초 (프레임 {frames.Count}) · 목표 '{StoryState.Objective}' · 자막 '{(StoryState.LineVisible ? StoryState.Line : "")}'");
                // 프로파일 기록은 1500프레임마다 새 파일로(에디터가 한 파일에서 2000프레임까지만 읽는다)
                if (profile && frames.Count % 1500 == 0)
                    UnityEngine.Profiling.Profiler.logFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), $"prof_{scene}_{++chunk}");
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0) gpu.Add((float)timing[0].gpuFrameTime);
            }
            if (profile) { UnityEngine.Profiling.Profiler.enabled = false; UnityEngine.Profiling.Profiler.logFile = null; }
            var sorted = frames.OrderBy(x => x).ToList();
            float P(float p) => sorted[Mathf.Clamp((int)(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
            lines.Add($"{scene,-9} 평균 {frames.Average():0.0}ms ({1000f / frames.Average():0}fps) · 중간 {P(0.5f):0.0} · 99% {P(0.99f):0.0} · 최악 {sorted[^1]:0.0}ms" +
                      (gpu.Count > 0 ? $" · GPU {gpu.Average():0.0}ms" : "") +
                      $" · GC {System.GC.CollectionCount(0) - gc0}회 · 관리 메모리 {System.GC.GetTotalMemory(false) / 1048576}MB" +
                      $" · 전체 {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576}MB");
            lines.AddRange(spikes);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "benchmark.txt"), lines);
        }
        lines.Add("끝");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "benchmark.txt"), lines);
        Application.Quit();
    }

    // 연출이 흘러가도 레이가 쓰러지지 않게(측정이 끊기지 않게)
    static void Hold()
    {
        var p = FindAnyObjectByType<PlayerController>();
        if (p && p.TryGetComponent(out Health h)) h.GrantInvulnerability(1f);
    }
}
