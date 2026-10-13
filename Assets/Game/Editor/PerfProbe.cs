using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 성능 재기(2026-10-13 "전체 최적화"): 플레이 중에 장면을 하나씩 불러 레이 자리에서 몇 초 머물며
/// 프레임 시간 · 메인 스레드 · GC 할당 · 그리기 호출(Batches/SetPass) · 삼각형 · 그림자 드리우는 물체 수를 재고,
/// 프로파일러 기록에서 자기 시간(self)이 큰 표시(marker) 상위 25개를 뽑는다. 장면 구성(빛·그림자 빛·렌더러·스킨 메시·파티클·소리)도 센다.
/// 결과: QA/perf/report.txt
/// </summary>
public static class PerfProbe
{
    static readonly string[] Scenes = { "Prologue", "Chapter1", "Chapter2", "Chapter3", "Chapter4", "Chapter5" };
    const float Warmup = 4f, Sample = 6f;

    static string dir;
    static readonly List<string> report = new();
    static int index;
    static float t0;
    static int stage;
    static readonly List<float> frames = new();
    static ProfilerRecorder mainThread, gcAlloc, batches, setPass, tris, casters, gcCount;
    static readonly List<double> mainMs = new(), gcBytes = new(), gcCalls = new(), gpuMs = new();
    static readonly FrameTiming[] timing = new FrameTiming[1];
    static long batchSum, setPassSum, trisSum, castersSum; static int statFrames;
    static int firstFrame;
    public static string[] Only;

    [MenuItem("Game/Test/Perf Probe")]
    public static void Begin() => Begin(@"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\perf");

    public static void Begin(string outDir)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        dir = outDir;
        System.IO.Directory.CreateDirectory(dir);
        report.Clear();
        report.Add($"측정 {System.DateTime.Now:HH:mm} · 1920x1080 · 장면마다 {Warmup}초 대기 후 {Sample}초");
        SaveGame.Enabled = false;
        Application.runInBackground = true;
        index = 0; stage = 0;
        PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "Perf"); // 실제 해상도로 잰다
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Write();
    }

    static void Write() => System.IO.File.WriteAllLines(dir + "/report.txt", report);
    static float Now => (float)EditorApplication.timeSinceStartup;
    static string[] List => Only ?? Scenes;

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; report.Add("플레이가 멈춰 중단"); Write(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        switch (stage)
        {
            case 0: // 장면 불러오기
                SceneManager.LoadScene(List[index]);
                t0 = Now; stage = 1;
                break;
            case 1: // 연출이 시작되고 안정될 때까지
                if (Now - t0 < Warmup) return;
                Hold();
                StartSampling();
                t0 = Now; stage = 2;
                break;
            case 2: // 재기
                Hold();
                frames.Add(Time.unscaledDeltaTime * 1000f);
                Read();
                if (Now - t0 < Sample) return;
                StopSampling();
                Summarize(List[index]);
                if (++index >= List.Length) { Finish(); return; }
                stage = 0;
                break;
        }
    }

    // 대사·연출이 진행돼도 레이는 그 자리에 둔다(쓰러지지 않게)
    static void Hold()
    {
        var p = Object.FindAnyObjectByType<PlayerController>();
        if (p) { var h = p.GetComponent<Health>(); if (h) h.GrantInvulnerability(1f); }
    }

    static void StartSampling()
    {
        frames.Clear(); mainMs.Clear(); gcBytes.Clear(); gcCalls.Clear(); gpuMs.Clear();
        PlayerSettings.enableFrameTimingStats = true; // 그래픽 카드 시간
        batchSum = setPassSum = trisSum = castersSum = 0; statFrames = 0;
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count", 1);
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
        tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
        casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count", 1);
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.enabled = true;
        UnityEngine.Profiling.Profiler.enabled = true;
        firstFrame = -1;
    }

    static void Read()
    {
        if (mainThread.Valid && mainThread.LastValue > 0) mainMs.Add(mainThread.LastValue / 1e6);
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0) gpuMs.Add(timing[0].gpuFrameTime);
        if (gcAlloc.Valid) gcBytes.Add(gcAlloc.LastValue);
        if (gcCount.Valid) gcCalls.Add(gcCount.LastValue);
        if (batches.Valid && batches.LastValue > 0)
        {
            batchSum += batches.LastValue; setPassSum += setPass.LastValue; trisSum += tris.LastValue; castersSum += casters.LastValue; statFrames++;
        }
        if (firstFrame < 0) firstFrame = ProfilerDriver.lastFrameIndex;
    }

    static void StopSampling()
    {
        UnityEngine.Profiling.Profiler.enabled = false;
        ProfilerDriver.enabled = false;
        mainThread.Dispose(); gcAlloc.Dispose(); gcCount.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); casters.Dispose();
    }

    static string Pct(List<float> v, float p) { if (v.Count == 0) return "-"; var s = v.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(p * (s.Count - 1)), 0, s.Count - 1)].ToString("0.0"); }

    static void Summarize(string scene)
    {
        report.Add("");
        report.Add($"■ {scene}");
        report.Add($"  프레임 {frames.Count}개: 평균 {frames.DefaultIfEmpty(0).Average():0.0}ms · 중간 {Pct(frames, 0.5f)} · 95% {Pct(frames, 0.95f)} · 최악 {frames.DefaultIfEmpty(0).Max():0.0}ms");
        if (mainMs.Count > 0) report.Add($"  메인 스레드 평균 {mainMs.Average():0.00}ms" + (gpuMs.Count > 0 ? $" · GPU 평균 {gpuMs.Average():0.00}ms (95% {gpuMs.OrderBy(x => x).ElementAt((int)(gpuMs.Count * 0.95)):0.00})" : " · GPU 측정 안 됨"));
        if (gcBytes.Count > 0) report.Add($"  GC 할당 평균 {gcBytes.Average() / 1024:0.0}KB/프레임 · 최대 {gcBytes.Max() / 1024:0.0}KB · 할당 횟수 평균 {gcCalls.DefaultIfEmpty(0).Average():0}");
        if (statFrames > 0) report.Add($"  Batches {batchSum / statFrames} · SetPass {setPassSum / statFrames} · 삼각형 {trisSum / statFrames / 1000}k · 그림자 드리우는 물체 {castersSum / statFrames}");

        // 장면 구성
        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled).ToList();
        int shadowed = lights.Count(l => l.shadows != LightShadows.None);
        int shadowedPoint = lights.Count(l => l.shadows != LightShadows.None && l.type == LightType.Point);
        report.Add($"  빛 {lights.Count}개(그림자 {shadowed} — 점광원 그림자 {shadowedPoint}) · 렌더러 {Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.enabled && r.gameObject.activeInHierarchy)}" +
                   $" · 스킨 메시 {Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Count(r => r.gameObject.activeInHierarchy)}" +
                   $" · 파티클 {Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Count(p => p.gameObject.activeInHierarchy)}" +
                   $" · 소리 재생 중 {Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a => a.isPlaying)}" +
                   $" · 내비 에이전트 {Object.FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled)}" +
                   $" · 적 {EnemyAI.All.Count}");

        // 프로파일러: 자기 시간이 큰 표시
        var self = new Dictionary<string, (double ms, int calls)>();
        var gcSelf = new Dictionary<string, double>();
        int last = ProfilerDriver.lastFrameIndex, first = Mathf.Max(ProfilerDriver.firstFrameIndex, last - 300);
        int n = 0;
        for (int f = first; f <= last; f++)
        {
            using var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false);
            if (view == null || !view.valid) continue;
            n++;
            var stack = new Stack<(int id, string parent)>(); stack.Push((view.GetRootItemID(), ""));
            var kids = new List<int>();
            while (stack.Count > 0)
            {
                var (id, parentName) = stack.Pop();
                string name = view.GetItemName(id);
                double ms = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                int calls = (int)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                if (ms > 0) { self.TryGetValue(name, out var cur); self[name] = (cur.ms + ms, cur.calls + calls); }
                kids.Clear(); view.GetItemChildren(id, kids);
                // 할당: 이 표시의 할당에서 자식 표시들의 할당을 뺀 몫(자기 할당)
                double gc = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                foreach (var k in kids) gc -= view.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnGcMemory);
                if (gc > 0 && name != "GC.Alloc") { gcSelf.TryGetValue(name, out var g); gcSelf[name] = g + gc; }
                else if (gc > 0) { string parent = parentName; gcSelf.TryGetValue(parent, out var g); gcSelf[parent] = g + gc; }
                foreach (var k in kids) stack.Push((k, name));
            }
        }
        if (n > 0)
        {
            report.Add($"  자기 시간 상위(프레임 {n}개 평균, ms):");
            foreach (var kv in self.OrderByDescending(k => k.Value.ms).Take(25))
                report.Add($"    {kv.Value.ms / n,7:0.000}  ×{kv.Value.calls / (float)n,6:0.#}  {kv.Key}");
            report.Add($"  GC 할당 상위(프레임 평균, 바이트):");
            foreach (var kv in gcSelf.Where(k => !k.Key.StartsWith("EditorLoop")).OrderByDescending(k => k.Value).Take(15))
                report.Add($"    {kv.Value / n,8:0}  {kv.Key}");
        }
        Write();
    }

    /// <summary>빌드에서 남긴 프로파일러 기록(.raw)을 읽어 가장 느린 프레임들에서 무엇이 시간을 썼는지 적는다</summary>
    public static string AnalyzeRaw(string rawPath, int worst = 6)
    {
        if (!ProfilerDriver.LoadProfile(rawPath, false)) return "못 읽음: " + rawPath;
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
        var times = new List<(int f, float ms)>();
        for (int f = first; f <= last; f++)
        {
            using var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
            if (v != null && v.valid) times.Add((f, v.frameTimeMs));
        }
        var lines = new List<string> { $"{System.IO.Path.GetFileName(rawPath)}: 프레임 {times.Count}개, 평균 {times.Average(t => t.ms):0.0}ms" };
        foreach (var (f, ms) in times.OrderByDescending(t => t.ms).Take(worst))
        {
            using var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false);
            var self = new Dictionary<string, double>();
            var stack = new Stack<int>(); stack.Push(v.GetRootItemID()); var kids = new List<int>();
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                string name = v.GetItemName(id);
                double t = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                if (t > 0.3) { self.TryGetValue(name, out var c); self[name] = c + t; }
                kids.Clear(); v.GetItemChildren(id, kids); foreach (var k in kids) stack.Push(k);
            }
            lines.Add($"  프레임 {f - first} ({ms:0.0}ms): " + string.Join(" · ", self.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value:0.0}")));
        }
        return string.Join("\n", lines);
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        report.Add("");
        report.Add("끝");
        Write();
        EditorApplication.isPlaying = false; // 측정이 끝나면 플레이를 끈다(켜 둔 채로 코드를 고치면 플레이 중 컴파일이 된다)
        var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); // 게임 창은 자유 비율로 되돌린다
        gv.GetMethod("SizeSelectionCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
          ?.Invoke(EditorWindow.GetWindow(gv, false, null, false), new object[] { 0, null });
    }
}
