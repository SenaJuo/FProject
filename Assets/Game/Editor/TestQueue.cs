using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 시험 줄 세우기(2026-10-16 "버그 없는지 확인해줘"): 플레이 모드 시험을 차례로 — 장면 열기 → 플레이 → 감독이 준비되면 시험 시작 →
/// 기록 파일이 생기면 멈추고 다음. 플레이에 들어갈 때 스크립트가 다시 불려도 이어지게 SessionState에 적어 둔다.
/// 결과 요약: QA/alltests/summary.txt
/// </summary>
[InitializeOnLoad]
public static class TestQueue
{
    const string Root = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\alltests";
    const string KeyQueue = "TestQueue.queue", KeyStage = "TestQueue.stage", KeyT = "TestQueue.t";

    /// <summary>이름|장면|시작 식(호출할 정적 메서드와 인자)</summary>
    static readonly string[] All =
    {
        "PrologueFlowTest|Prologue|2", "CutsceneSkipTest|Prologue|2", "HitFeelTest|Prologue|2", "CombatVisualTest|Prologue|2",
        "Chapter1Test|Chapter1|2", "BugFixTest|Chapter1|1", "BossTest|Chapter1|2", "CombatTest|Chapter1|1", "CounterTest|Chapter1|2",
        "MemoryTest|Chapter1|2", "ExecuteMemoryTest|Chapter4|2", "RollTest|Chapter1|2", "TapeAbilityTest|Chapter1|2", "SaveTest|Chapter1|1",
        "PauseMenuTest|Chapter1|1",
        "Chapter2Test|Chapter2|2", "Chapter3Test|Chapter3|2", "Chapter4Test|Chapter4|2", "Chapter5Test|Chapter5|E1", "Chapter5Test|Chapter5|E2",
        "TitleDeckTest|Title|T",
    };

    public static void RunAll() => Run(All);
    public static void Run(params string[] items)
    {
        Directory.CreateDirectory(Root);
        foreach (var f in Directory.GetFiles(Root, "*.log")) File.Delete(f);
        if (File.Exists(Path.Combine(Root, "summary.txt"))) File.Delete(Path.Combine(Root, "summary.txt"));
        SessionState.SetString(KeyQueue, string.Join(";", items));
        SessionState.SetInt(KeyStage, 0);
        Hook();
    }

    static TestQueue() { if (!string.IsNullOrEmpty(SessionState.GetString(KeyQueue, ""))) Hook(); }
    static void Hook() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

    static string[] Queue => SessionState.GetString(KeyQueue, "").Split(';').Where(s => s.Length > 0).ToArray();
    static int Stage { get => SessionState.GetInt(KeyStage, 0); set { SessionState.SetInt(KeyStage, value); SessionState.SetFloat(KeyT, (float)EditorApplication.timeSinceStartup); } }
    static float Since => (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyT, 0f);

    static string LogOf(string item) { var p = item.Split('|'); return Path.Combine(Root, p[0] + (p[2].StartsWith("E") ? "_" + p[2] : "") + ".log"); }

    // 단계: 0 = 멈추고 장면 열기 · 플레이, 1 = 감독 기다렸다 시작, 2 = 기록 기다림
    static void Tick()
    {
        var q = Queue;
        if (q.Length == 0) { EditorApplication.update -= Tick; return; }
        var item = q[0]; var parts = item.Split('|');
        switch (Stage)
        {
            case 0:
                if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                EditorSceneManager.OpenScene($"Assets/Game/Scenes/{parts[1]}.unity");
                Stage = 1;
                EditorApplication.isPlaying = true;
                return;
            case 1:
                if (!EditorApplication.isPlaying) { if (Since > 60f) Finish(item, "플레이에 들어가지 못함"); return; }
                if (Since < 3f) return; // 감독 · 장면 준비
                EditorApplication.QueuePlayerLoopUpdate();
                try { Start(parts); Stage = 2; }
                catch (System.Exception e) { Finish(item, "시작 실패: " + (e.InnerException ?? e).Message); }
                return;
            case 2:
                EditorApplication.QueuePlayerLoopUpdate();
                string log = LogOf(item);
                bool done = File.Exists(log) && Since > 2f && (Text(log).Contains("시험 끝") || Text(log).Contains("완료") || File.GetLastWriteTime(log) < System.DateTime.Now.AddSeconds(-40));
                if (done || Since > 600f || !EditorApplication.isPlaying) Finish(item, File.Exists(log) ? null : (Since > 600f ? "시간 초과(10분)" : "기록 없음"));
                return;
        }
    }

    static string Text(string path) { try { return File.ReadAllText(path); } catch { return ""; } }

    static void Start(string[] p)
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(p[0])).FirstOrDefault(t => t != null);
        string log = LogOf(string.Join("|", p)), shots = Path.Combine(Root, p[0] + "_shots");
        var begin = type.GetMethods().First(m => m.Name == "Begin" && m.GetParameters().Length == (p[2] == "1" ? 1 : p[2].StartsWith("E") ? 3 : 2));
        object[] args = p[2] switch
        {
            "1" => new object[] { log },
            "T" => new object[] { log, Path.Combine(Root, "title.png") },
            "E1" => new object[] { log, shots, 1 },
            "E2" => new object[] { log, shots + "_2", 2 },
            _ => new object[] { log, shots },
        };
        begin.Invoke(null, args);
    }

    static void Finish(string item, string problem)
    {
        string log = LogOf(item);
        string last = File.Exists(log) ? File.ReadAllLines(log).LastOrDefault(l => l.Trim().Length > 0) ?? "" : "";
        File.AppendAllText(Path.Combine(Root, "summary.txt"), $"{item.Split('|')[0]} {item.Split('|')[2]}: {(problem ?? last)}\n");
        var rest = Queue.Skip(1).ToArray();
        SessionState.SetString(KeyQueue, string.Join(";", rest));
        Stage = 0;
        if (rest.Length == 0)
        {
            File.AppendAllText(Path.Combine(Root, "summary.txt"), "모두 끝\n");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.update -= Tick;
        }
    }
}
