using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 편집기 연결 없이 영상 녹화(2026-10-20): QA/trailer/go.txt 에 "intro" · "intro_preview" · "film" · "film_preview" 를 쓰면
/// ("refresh" = 스크립트를 다시 읽기만)
/// 노래를 미리 불러오게 바꾸고 → 프롤로그를 열어 플레이 → 녹화 → 끝나면 플레이를 멈추고 QA/trailer/autorun.log 에 결과를 남긴다.
/// (노래 가져오기 설정은 녹화 뒤 git으로 되돌린다)
/// </summary>
[InitializeOnLoad]
static class FilmAutorun
{
    const string Dir = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\trailer";
    static string Go => Path.Combine(Dir, "go.txt");
    static string LogFile => Path.Combine(Dir, "autorun.log");
    const string Key = "FilmAutorun.mode";

    static FilmAutorun()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayMode;
        if (EditorApplication.isPlaying && SessionState.GetString(Key, "") != "") Application.logMessageReceived += OnLog;
    }

    static void Note(string line) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss ") + line + "\n"); } catch { } }

    static void Poll()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Go)) return;
        string mode;
        try { mode = File.ReadAllText(Go).Trim(); File.Delete(Go); } catch { return; }
        if (mode == "refresh") { Note("새로 읽기"); AssetDatabase.Refresh(); return; } // 편집기가 뒤에 있어도 스크립트를 다시 읽는다
        if (mode.StartsWith("shot:"))
        {
            // "shot:장면이름:초" = 그 장면을 1920×1080으로 플레이해 정한 초 뒤에 게임 화면을 찍는다(QA/trailer/shot_장면.png)
            var parts = mode.Split(':');
            SessionState.SetString(ShotKey, parts[1] + ":" + (parts.Length > 2 ? parts[2] : "6") + (parts.Length > 3 ? ":" + parts[3] : ""));
            EditorSceneManager.OpenScene($"Assets/Game/Scenes/{parts[1]}.unity");
            PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "Shot");
            Note("사진: " + parts[1]);
            EditorApplication.isPlaying = true;
            return;
        }
        if (mode == "build")
        {
            // Windows 빌드(C:\Users\MBC-501-12\Builds\REWIND\REWIND.exe) → 결과를 기록
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var sc in EditorBuildSettings.scenes) if (sc.enabled) scenes.Add(sc.path);
            var o = new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = @"C:\Users\MBC-501-12\Builds\REWIND\REWIND.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
            var r = BuildPipeline.BuildPlayer(o);
            Note($"빌드 끝: {r.summary.result} {r.summary.totalSize / 1048576}MB 오류 {r.summary.totalErrors} 장면 {scenes.Count}");
            return;
        }
        if (mode == "uitest")
        {
            // UI 글자 검사(해상도 8종 × 화면 전부) → QA/ui/report.txt
            SessionState.SetString(UiKey, "1");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Chapter1.unity");
            Note("UI 글자 검사");
            EditorApplication.isPlaying = true;
            return;
        }
        if (mode == "titlebug")
        {
            // 타이틀 설정 창을 닫는 클릭이 카세트 건반을 누르지 않는지(2026-10-20 버그) → QA/trailer/autorun.log
            SessionState.SetString(BugKey, "1");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Title.unity");
            PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "Shot");
            Note("타이틀 버그 시험");
            EditorApplication.isPlaying = true;
            return;
        }
        if (mode == "music")
        {
            // 곡 고르기: 모든 곡의 밝기 · 템포 · 크기 흐름(QA/trailer/music_report.txt, music_env.png). 가져오기 설정은 뒤에 git으로 되돌린다
            var paths = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Game/Resources/Music" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();
            foreach (var x in paths)
            {
                var ai = AssetImporter.GetAtPath(x) as AudioImporter; if (ai == null) continue;
                var st = ai.defaultSampleSettings; st.loadType = AudioClipLoadType.DecompressOnLoad; st.preloadAudioData = true; ai.defaultSampleSettings = st; ai.loadInBackground = false; ai.SaveAndReimport();
            }
            var sb = new System.Text.StringBuilder();
            foreach (var x in paths) { try { sb.AppendLine(MusicQA.Analyze(x).ToString()); } catch (Exception e) { sb.AppendLine(x + " 실패 " + e.Message); } }
            File.WriteAllText(Path.Combine(Dir, "music_report.txt"), sb.ToString());
            MusicQA.Envelopes(paths, Path.Combine(Dir, "music_env.png"));
            Note("음악 분석 끝");
            return;
        }
        var known = new[] { "intro", "intro_preview", "film", "film_preview", "play", "play_preview", "titlegif" };
        if (Array.IndexOf(known, mode.Split(':')[0]) < 0) { Note("모르는 명령: " + mode); return; } // 모르는 명령으로 녹화가 시작되지 않게
        Note("시작: " + mode);
        // 곡 · 소리를 통째로 미리 풀어 둔다(스트리밍이면 GetData가 0을 준다)
        foreach (var path in new[] { mode.StartsWith("intro") || mode.StartsWith("play") || mode == "titlegif" ? FilmMaker.IntroMusic : "Assets/Game/Resources/Music/night_drive-2.mp3", "Assets/Game/Art/Trailer/TapeRewindReal.mp3" })
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter; if (imp == null) continue;
            var s = imp.defaultSampleSettings; s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.PCM; s.preloadAudioData = true;
            imp.defaultSampleSettings = s; imp.loadInBackground = false; imp.SaveAndReimport();
        }
        EditorSceneManager.OpenScene("Assets/Game/Scenes/Prologue.unity");
        SessionState.SetString(Key, mode);
        EditorApplication.isPlaying = true;
    }

    const string ShotKey = "FilmAutorun.shot";
    static double shotAt; static string shotScene; static bool shotTaken;
    static void ShotWatch()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= ShotWatch; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (!shotTaken && EditorApplication.timeSinceStartup >= shotAt)
        {
            shotTaken = true; shotAt = EditorApplication.timeSinceStartup + 1.5;
            ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "shot_" + shotScene + (TitleScreen.HideMenu ? "_clean" : "") + ".png"));
            return;
        }
        if (shotTaken && EditorApplication.timeSinceStartup >= shotAt)
        {
            EditorApplication.update -= ShotWatch;
            SessionState.EraseString(ShotKey);
            Note("사진 끝: " + shotScene); TitleScreen.HideMenu = false;
            EditorApplication.isPlaying = false;
        }
    }

    const string BugKey = "FilmAutorun.titlebug";
    static int bugStep, bugWait; static Vector2 deckPos; static TitleScreen title;
    static UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode bugInputWas;
    static UnityEngine.InputSystem.InputSettings.BackgroundBehavior bugBgWas;
    const System.Reflection.BindingFlags AnyI = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    static string Held() => typeof(TitleScreen).GetField("held", AnyI).GetValue(title) + $" (막음 {typeof(TitleScreen).GetField("waitRelease", AnyI)?.GetValue(title)}, 마우스 {UnityEngine.InputSystem.Mouse.current.position.ReadValue()} 눌림 {UnityEngine.InputSystem.Mouse.current.leftButton.isPressed}, 프레임 {Time.frameCount})";
    // 시험 전용 가상 마우스(실제 마우스 · 편집기 창 초점과 섞이지 않게)
    static UnityEngine.InputSystem.Mouse virt;
    static void MouseState(Vector2 pos, bool down)
    {
        if (virt == null) virt = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("FilmTestMouse");
        virt.MakeCurrent();
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(virt, new UnityEngine.InputSystem.LowLevel.MouseState { position = pos, buttons = (ushort)(down ? 1 : 0) });
    }
    static void BugTick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= BugTick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (bugWait > 0) { bugWait--; return; }
        switch (bugStep)
        {
            case 0: // 메뉴가 뜰 때까지
                title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
                if (!title || typeof(TitleScreen).GetField("state", AnyI).GetValue(title).ToString() != "Menu" || (float)typeof(TitleScreen).GetField("t", AnyI).GetValue(title) < 5.5f) return;
                var keyAt = typeof(TitleScreen).GetMethod("KeyAt", AnyI);
                deckPos = Vector2.zero;
                for (int y = 0; y < Screen.height && deckPos == Vector2.zero; y += 10)
                    for (int x = 0; x < Screen.width; x += 10)
                        if (keyAt.Invoke(title, new object[] { new Vector2(x, y) }).ToString() == "Rew") { deckPos = new Vector2(x + 15, Screen.height - y - 15); break; }
                Note($"REW 건반 화면 위치 {deckPos} (화면 {Screen.width}x{Screen.height})");
                PauseMenu.ShowOptions(); bugStep = 1; bugWait = 6; return;
            case 1: // 설정 창을 닫는 바로 그 프레임에 건반 위치에서 클릭
                Note($"설정 창 열림 {PauseMenu.IsOpen}");
                typeof(PauseMenu).GetMethod("Close", AnyI).Invoke(UnityEngine.Object.FindAnyObjectByType<PauseMenu>(), null);
                MouseState(deckPos, true); bugStep = 2; bugWait = 3; return;
            case 2:
                Note($"① 창을 닫은 클릭: 눌린 건반 {Held()} (기대 None)");
                MouseState(deckPos, false); bugStep = 3; bugWait = 40; return;
            case 3: // 손을 뗀 뒤 다시 누르면 건반이 눌려야 한다
                Note($"   뗀 뒤: {Held()}");
                MouseState(deckPos, true); bugStep = 4; bugWait = 6; return;
            case 4:
                Note($"② 손을 뗀 뒤 새 클릭: 눌린 건반 {Held()} (기대 Rew)");
                MouseState(deckPos, false); bugStep = 5; bugWait = 30; return;
            default:
                EditorApplication.update -= BugTick; SessionState.EraseString(BugKey);
                if (virt != null) { UnityEngine.InputSystem.InputSystem.RemoveDevice(virt); virt = null; }
                Note("타이틀 버그 시험 끝"); EditorApplication.isPlaying = false; return;
        }
    }

    const string UiKey = "FilmAutorun.uitest";
    static DateTime uiStart;
    static void UiWatch()
    {
        var rep = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\ui\report.txt";
        if (!EditorApplication.isPlaying) { EditorApplication.update -= UiWatch; return; }
        if (File.Exists(rep) && File.GetLastWriteTime(rep) > uiStart && File.ReadAllText(rep).Contains("끝"))
        {
            EditorApplication.update -= UiWatch; SessionState.EraseString(UiKey);
            Note("UI 글자 검사 끝: " + File.ReadAllLines(rep)[0]);
            EditorApplication.isPlaying = false;
        }
    }

    static void OnPlayMode(PlayModeStateChange st)
    {
        if (SessionState.GetString(UiKey, "") != "")
        {
            if (st == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true; uiStart = DateTime.Now;
                EditorApplication.delayCall += () => UiLayoutTest.Begin();
                EditorApplication.update -= UiWatch; EditorApplication.update += UiWatch;
            }
            else if (st == PlayModeStateChange.EnteredEditMode) { SessionState.EraseString(UiKey); Note("플레이 멈춤"); }
            return;
        }
        if (SessionState.GetString(BugKey, "") != "")
        {
            if (st == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true; bugStep = 0; bugWait = 0;
                // 편집기 창이 뒤에 있어도 넣은 마우스 입력이 게임으로 가게(시험 동안만)
                bugInputWas = UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode;
                UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                // 편집기 창이 뒤에 있으면 입력 장치가 꺼진다(그래서 넣은 클릭이 닿지 않았다) — 시험 동안만 초점과 상관없이
                bugBgWas = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior;
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                EditorApplication.update -= BugTick; EditorApplication.update += BugTick;
            }
            else if (st == PlayModeStateChange.EnteredEditMode) { SessionState.EraseString(BugKey); UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = bugInputWas; UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = bugBgWas; Note("플레이 멈춤"); }
            return;
        }
        string shot = SessionState.GetString(ShotKey, "");
        if (shot != "" && st == PlayModeStateChange.EnteredPlayMode)
        {
            var p = shot.Split(':'); shotScene = p[0]; shotTaken = false;
            TitleScreen.HideMenu = p.Length > 2 && p[2] == "clean"; // "shot:Title:7:clean" = 메뉴 글자 없이
            Application.runInBackground = true;
            shotAt = EditorApplication.timeSinceStartup + float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
            EditorApplication.update -= ShotWatch; EditorApplication.update += ShotWatch;
            return;
        }
        if (shot != "" && st == PlayModeStateChange.EnteredEditMode) { SessionState.EraseString(ShotKey); Note("플레이 멈춤"); return; }
        string mode = SessionState.GetString(Key, "");
        if (mode == "") return;
        if (st == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // "intro_preview:컷이름" = 그 컷부터 미리보기
                    var parts = mode.Split(':'); FilmMaker.From = parts.Length > 1 ? parts[1] : ""; mode = parts[0];
                    bool prev = mode.EndsWith("_preview");
                    FilmMaker.ShowCaptions = true;
                    if (mode == "titlegif") { FilmMaker.TitleGif = new GifWriter(640, 360, 10f); FilmMaker.MakeIntro(true); status = () => FilmMaker.Status; } // 타이틀 장면 움짤
                    else if (mode.StartsWith("play")) { TrailerMaker.MakePlay(prev); status = () => TrailerMaker.Status; } // 발표 자료용 플레이 영상
                    else { if (mode.StartsWith("intro")) FilmMaker.MakeIntro(prev); else FilmMaker.Make(prev); status = () => FilmMaker.Status; }
                    EditorApplication.update -= Watch; EditorApplication.update += Watch;
                }
                catch (Exception e) { Note("실패: " + e); Stop(); }
            };
        }
        else if (st == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseString(Key);
            Application.logMessageReceived -= OnLog;
            Note("플레이 멈춤");
        }
    }

    static string last = "";
    static Func<string> status = () => FilmMaker.Status;
    static void Watch()
    {
        var s = status();
        if (s != last) { last = s; Note("상태: " + s); }
        if (s.Contains("끝") || s.StartsWith("중단")) { EditorApplication.update -= Watch; Stop(); }
    }

    static void Stop() { EditorApplication.delayCall += () => EditorApplication.isPlaying = false; }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || msg.Contains("[필름]")) Note($"{type}: {msg}");
    }
}
