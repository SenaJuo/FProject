using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// itch.io용 웹(WebGL) 빌드(2026-10-24). itch.io 웹 게임 한도: 압축을 푼 전체 500MB, 파일 하나 200MB.
/// Brotli 압축 + 압축 풀기 대체(decompressionFallback): itch.io 서버가 압축 헤더를 붙이지 않아도 브라우저에서 풀린다.
/// 제품 이름은 잠시 "REWIND"(브라우저 탭 제목) — 짓고 나면 되돌린다.
/// scenes가 비면 빌드 설정의 장면 전부(본편), 아니면 그 장면만(체험판).
/// 파일 하나 200MB를 넘지 않게: 텍스처 최대 크기(maxTexture, 이 빌드에만), 음악은 데이터 파일 밖으로 —
/// 짓는 동안 Resources/Music을 프로젝트 밖(Temp)에 잠시 치우고, mp3를 StreamingAssets/Music에 둔다(MusicLibrary가 받아 온다).
/// 웹에서는 음악 압축 설정이 듣지 않아(원본 mp3를 그대로 쓴다) 이렇게 뺀다. 짓고 나면 모두 되돌린다.
/// 결과: C:\Users\MBC-501-12\Builds\REWIND_Web(또는 REWIND_WebDemo), 내역 QA/build/web_report.txt
/// </summary>
public static class WebBuild
{
    const string Report = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\build\web_report.txt";

    public static void Build(string outDir, string[] scenes = null, int maxTexture = 0)
    {
        string name = PlayerSettings.productName;
        var compression = PlayerSettings.WebGL.compressionFormat;
        bool fallback = PlayerSettings.WebGL.decompressionFallback;
        int keepMax = EditorUserBuildSettings.overrideMaxTextureSize;
        var lines = new List<string>();
        try
        {
            PlayerSettings.productName = "REWIND";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            if (maxTexture > 0) EditorUserBuildSettings.overrideMaxTextureSize = maxTexture;
            lines.Add(StashMusic());
            if (scenes == null || scenes.Length == 0) scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            lines.Add($"결과 {r.summary.result} · 오류 {r.summary.totalErrors} · {r.summary.totalTime.TotalMinutes:0.0}분 · 장면 {scenes.Length} · 텍스처 제한 {(maxTexture > 0 ? maxTexture.ToString() : "없음")}");
            if (r.summary.result == BuildResult.Succeeded) Inspect(r, outDir, lines);
            else foreach (var step in r.steps) foreach (var m in step.messages) if (m.type == LogType.Error) lines.Add("  오류: " + m.content);
        }
        catch (System.Exception e) { lines.Add("예외: " + e); }
        finally
        {
            lines.Add(RestoreMusic());
            PlayerSettings.productName = name;
            PlayerSettings.WebGL.compressionFormat = compression;
            PlayerSettings.WebGL.decompressionFallback = fallback;
            EditorUserBuildSettings.overrideMaxTextureSize = keepMax;
            lines.Add("끝");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllLines(Report, lines);
        }
    }

    static string Project => Path.GetDirectoryName(Application.dataPath);
    static string MusicDir => Path.Combine(Application.dataPath, "Game", "Resources", "Music");
    static string Stash => Path.Combine(Project, "Temp", "WebMusicStash");
    static string StreamDir => Path.Combine(Application.dataPath, "StreamingAssets");
    static bool madeStreaming;

    /// <summary>음악을 빌드 데이터에서 빼고 StreamingAssets/Music에 mp3로 둔다</summary>
    static string StashMusic()
    {
        if (!Directory.Exists(MusicDir)) return "음악 폴더 없음";
        madeStreaming = !Directory.Exists(StreamDir);
        string dst = Path.Combine(StreamDir, "Music");
        int n = 0;
        foreach (var f in Directory.GetFiles(MusicDir, "*.mp3", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dst, Path.GetRelativePath(MusicDir, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(f, to, true);
            n++;
        }
        if (Directory.Exists(Stash)) Directory.Delete(Stash, true);
        Directory.CreateDirectory(Stash);
        Directory.Move(MusicDir, Path.Combine(Stash, "Music"));
        File.Move(MusicDir + ".meta", Path.Combine(Stash, "Music.meta"));
        AssetDatabase.Refresh();
        return $"음악 {n}곡을 StreamingAssets/Music으로(빌드 데이터에서 뺌)";
    }

    static string RestoreMusic()
    {
        string stashed = Path.Combine(Stash, "Music");
        if (!Directory.Exists(stashed)) return "음악: 되돌릴 것 없음";
        Directory.Move(stashed, MusicDir);
        File.Move(Path.Combine(Stash, "Music.meta"), MusicDir + ".meta");
        string dst = Path.Combine(StreamDir, "Music");
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        if (File.Exists(dst + ".meta")) File.Delete(dst + ".meta");
        if (madeStreaming && Directory.Exists(StreamDir) && Directory.GetFileSystemEntries(StreamDir).Length == 0)
        {
            Directory.Delete(StreamDir);
            if (File.Exists(StreamDir + ".meta")) File.Delete(StreamDir + ".meta");
        }
        AssetDatabase.Refresh();
        return "음악 제자리로";
    }

    static void Inspect(BuildReport r, string outDir, List<string> lines)
    {
        // itch.io 한도: 파일 하나 200MB, 전체 500MB(압축 상태 그대로 올라간다)
        long total = 0, biggest = 0; string biggestName = "";
        var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
        foreach (var f in files)
        {
            long len = new FileInfo(f).Length;
            total += len;
            if (len > biggest) { biggest = len; biggestName = Path.GetFileName(f); }
        }
        bool ok = biggest <= 200L * 1024 * 1024 && total <= 500L * 1024 * 1024;
        lines.Add($"itch.io 한도: 전체 {total / 1048576.0:0.0}MB(≤500), 가장 큰 파일 {biggestName} {biggest / 1048576.0:0.0}MB(≤200), 파일 {files.Length}개 → {(ok ? "통과" : "넘침")}");
        foreach (var f in files.OrderByDescending(f => new FileInfo(f).Length).Take(6)) lines.Add($"  {new FileInfo(f).Length / 1048576.0,8:0.0}MB  {Path.GetFileName(f)}");
        // 압축 전 내용: 종류별 · 원본 파일별
        var byType = new Dictionary<string, ulong>();
        var bySource = new Dictionary<string, ulong>();
        foreach (var file in r.packedAssets)
            foreach (var c in file.contents)
            {
                string type = c.type != null ? c.type.Name : "?";
                string key = string.IsNullOrEmpty(c.sourceAssetPath) ? "(내장)" : c.sourceAssetPath;
                byType.TryGetValue(type, out var t); byType[type] = t + c.packedSize;
                bySource.TryGetValue(key, out var v); bySource[key] = v + c.packedSize;
            }
        lines.Add($"압축 전 내용 {byType.Values.Aggregate(0UL, (a, b) => a + b) / 1048576.0:0.0}MB — 종류별");
        foreach (var kv in byType.OrderByDescending(k => k.Value).Take(10)) lines.Add($"  {kv.Value / 1048576.0,8:0.0}MB  {kv.Key}");
        lines.Add("원본 파일별(큰 것 40개)");
        foreach (var kv in bySource.OrderByDescending(k => k.Value).Take(40)) lines.Add($"  {kv.Value / 1048576.0,8:0.0}MB  {kv.Key}");
    }
}
