using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 글자 겹침·넘침 검사(2026-10-13 "글자 겹치는 거, 이상한 위치, 2중으로 겹치는 거 싹 고치고 제대로 검증"):
/// 플레이 중인 장면에서 화면(HUD·자막·안내·인벤토리·가게·일시정지·튜토리얼·비밀번호 패널)을 하나씩 띄우고,
/// 여러 해상도에서 UIText가 기록한 "글자가 실제로 차지한 영역"끼리 겹치는지, 칸을 넘치는지, 화면 밖으로 나가는지 잰다.
/// 장면마다 스크린샷도 남긴다. 결과: QA/ui/report.txt
/// </summary>
public static class UiLayoutTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly (int w, int h)[] Sizes = { (1920, 1080), (1280, 720), (1366, 768), (2560, 1080), (1280, 1024), (1024, 768), (800, 600) };

    class Scenario { public string name; public Action setup, teardown; }

    static string dir;
    static readonly List<string> report = new();
    static List<Scenario> scenarios;
    static int sizeIndex, scenIndex, stage, waitFrame, problems;
    static readonly Dictionary<string, int> problemKinds = new();

    [MenuItem("Game/Test/UI Layout")]
    public static void Begin() => Begin(@"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\ui");

    public static void Begin(string outDir)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 중에만");
        dir = outDir;
        System.IO.Directory.CreateDirectory(dir);
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.png")) System.IO.File.Delete(f);
        report.Clear(); problemKinds.Clear(); problems = 0;
        SaveGame.Enabled = false;
        Application.runInBackground = true;
        scenarios = Build();
        sizeIndex = scenIndex = stage = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Write();
    }

    static void Write() => System.IO.File.WriteAllLines(dir + "/report.txt", report);

    // ---------- 진행 ----------

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; report.Add("플레이가 멈춰 중단"); Write(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount < waitFrame) return;
        var (w, h) = Sizes[sizeIndex];
        var sc = scenarios[scenIndex];
        switch (stage)
        {
            case 0: // 해상도
                if (scenIndex == 0) PlayModeWindow.SetCustomRenderingResolution((uint)w, (uint)h, "UiTest");
                Reset();
                stage = 1; waitFrame = Time.frameCount + 4; break;
            case 1: // 화면 띄우기
                try { sc.setup(); } catch (Exception e) { report.Add($"[{w}x{h}] {sc.name}: 띄우기 실패 {e.GetBaseException().Message}"); }
                stage = 2; waitFrame = Time.frameCount + 4; break;
            case 2: // 기록 시작
                UIText.Log.Clear(); UIText.Recording = true;
                stage = 3; waitFrame = Time.frameCount + 2; break;
            case 3: // 재기 + 스크린샷
                UIText.Recording = false;
                if (Screen.width != w || Screen.height != h) report.Add($"[{w}x{h}] {sc.name}: 화면이 {Screen.width}x{Screen.height}");
                Check($"{w}x{h}", sc.name, w, h);
                ScreenCapture.CaptureScreenshot($"{dir}/{w}x{h}_{sc.name}.png");
                stage = 4; waitFrame = Time.frameCount + 2; break;
            case 4: // 닫기
                try { sc.teardown?.Invoke(); } catch (Exception e) { report.Add($"{sc.name}: 닫기 실패 {e.GetBaseException().Message}"); }
                Reset();
                stage = 1; waitFrame = Time.frameCount + 3;
                if (++scenIndex >= scenarios.Count)
                {
                    scenIndex = 0; stage = 0;
                    if (++sizeIndex >= Sizes.Length) { Finish(); return; }
                }
                break;
        }
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        report.Insert(0, $"문제 {problems}건  ({string.Join(", ", problemKinds.Select(k => $"{k.Key} {k.Value}"))})");
        report.Add("끝");
        Write();
        // 게임 창은 원래대로(자유 비율)
        var gv = Type.GetType("UnityEditor.GameView,UnityEditor");
        var win = EditorWindow.GetWindow(gv, false, null, false);
        gv.GetMethod("SizeSelectionCallback", Any)?.Invoke(win, new object[] { 0, null });
    }

    // ---------- 검사 ----------

    static void Problem(string kind, string line) { problems++; problemKinds[kind] = problemKinds.TryGetValue(kind, out var n) ? n + 1 : 1; report.Add(line); }

    static void Check(string res, string scene, int w, int h)
    {
        // 마지막 한 프레임만(같은 글자를 여러 프레임에 걸쳐 세지 않게)
        int last = UIText.Log.Count > 0 ? UIText.Log.Max(d => d.frame) : 0;
        var log = UIText.Log.Where(d => d.frame == last).ToList();
        bool menu = scene.StartsWith("pause") || scene.StartsWith("tut_") || scene is "inventory" or "shop" or "keypad" or "letter";
        report.Add($"[{res}] {scene}: 글자 {log.Count}개");
        foreach (var d in log)
        {
            if (d.overflow) Problem("넘침", $"  넘침   '{Cut(d.text)}' 칸 {R(d.cell)} 크기 {d.size}");
            if (d.box.xMin < -1f || d.box.yMin < -1f || d.box.xMax > w + 1f || d.box.yMax > h + 1f) Problem("화면밖", $"  화면밖 '{Cut(d.text)}' {R(d.box)}");
            if (d.orig > 0 && d.size < d.orig * 0.8f) Problem("줄어듦", $"  줄어듦 '{Cut(d.text)}' {d.orig}→{d.size}px 칸 {R(d.cell)}");
            if (w == 1920 && d.size < 14) Problem("작음", $"  작음   '{Cut(d.text)}' 글자 {d.size}px");
            if (d.text.Contains("…") && d.size <= 9) Problem("잘림", $"  잘림   '{Cut(d.text)}' 칸 {R(d.cell)}");
        }
        for (int i = 0; i < log.Count; i++)
        for (int j = i + 1; j < log.Count; j++)
        {
            var a = log[i]; var b = log[j];
            if (a.depth != b.depth && menu) continue; // 메뉴가 HUD 위를 덮는 화면에서는 다른 층끼리 보지 않는다(아래 HUD는 어둡게 가려진다)
            if (a.text == b.text && Vector2.Distance(a.box.position, b.box.position) < 4f) { Problem("2중", $"  2중    '{Cut(a.text)}' {R(a.box)}"); continue; }
            var o = Intersect(a.box, b.box);
            float gap = 6f * h / 1080f; // 거의 붙은 것도 겹침으로 본다
            var near = Intersect(Grow(a.box, gap), b.box);
            float vOverlap = Mathf.Min(a.box.yMax, b.box.yMax) - Mathf.Max(a.box.yMin, b.box.yMin);
            bool sideBySide = vOverlap > 0.5f * Mathf.Min(a.box.height, b.box.height); // 위아래로 쌓인 줄(제목 아래 설명)은 붙어 있어도 정상
            if (o.width <= 1.5f && sideBySide && near.width > 0.5f && near.height > 0.5f) { Problem("붙음", $"  붙음   '{Cut(a.text)}' {R(a.box)}  ↔  '{Cut(b.text)}' {R(b.box)}"); continue; }
            if (o.width > 1.5f && o.height > 1.5f) Problem("겹침", $"  겹침   '{Cut(a.text)}' {R(a.box)}  ↔  '{Cut(b.text)}' {R(b.box)}");
        }
        Write();
    }

    static Rect Grow(Rect r, float g) => new(r.x - g, r.y - g, r.width + 2 * g, r.height + 2 * g);

    static Rect Intersect(Rect a, Rect b)
    {
        float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin), x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
        return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
    }

    static string R(Rect r) => $"({r.x:0},{r.y:0} {r.width:0}x{r.height:0})";
    static string Cut(string t) { t = t.Replace("\n", "⏎"); return t.Length > 28 ? t.Substring(0, 28) + "…" : t; }

    // ---------- 화면들 ----------

    static TapeAbility Ability() { var w = W; var a = w.GetComponent<TapeAbility>(); return a ? a : w.gameObject.AddComponent<TapeAbility>(); }
    static Walkman W => UnityEngine.Object.FindAnyObjectByType<Walkman>();
    static GameManager GM => GameManager.Instance;
    static void Set(object target, string field, object value) => (target is Type t ? t : target.GetType()).GetField(field, Any).SetValue(target is Type ? null : target, value);
    static void SetProp(Type t, string prop, object value) => t.GetProperty(prop, Any).SetValue(null, value);
    static void WalkmanPhase(string phase)
    {
        var w = W; var pt = typeof(Walkman).GetNestedType("Phase", Any);
        typeof(Walkman).GetField("phase", Any).SetValue(w, Enum.Parse(pt, phase));
    }

    // "Q 몰래 제압"·"F 상호작용"을 억지로 띄운다: 매 프레임 다시 찾는 쪽(Takedown·상호작용 범위 검사)을 잠시 멈춘다
    static void ForceRows()
    {
        var td = UnityEngine.Object.FindAnyObjectByType<Takedown>();
        if (td) td.enabled = false;
        var e = EnemyAI.All.FirstOrDefault(x => x && !x.isBoss);
        if (e) SetProp(typeof(Takedown), "Target", e);
        var it = UnityEngine.Object.FindObjectsByType<StoryInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderByDescending(x => x.prompt?.Length ?? 0).FirstOrDefault();
        if (it) { it.enabled = false; StoryState.Nearby = it; }
    }

    static void ReleaseRows()
    {
        var td = UnityEngine.Object.FindAnyObjectByType<Takedown>(FindObjectsInactive.Include);
        if (td) td.enabled = true;
        if (StoryState.Nearby) StoryState.Nearby.enabled = true;
        StoryState.Nearby = null;
    }

    const string LongLine = "…대니, 네가 남긴 테이프. 그 회사 사람들이 왜 이걸 그렇게까지 찾았는지 이제야 알 것 같아. 끝까지 들어 볼게.";

    static void Reset()
    {
        StoryState.CutsceneActive = StoryState.ControlLocked = false;
        StoryState.Objective = null; StoryState.Waypoint = null; StoryState.Prompt = null; StoryState.Title = null;
        StoryState.LetterText = StoryState.LetterHeading = null; StoryState.Nearby = null; StoryState.Fade = 0f;
        StoryState.ClearLine();
        CutsceneSkip.SimulateHold = false;
        PlayerController.MemoryStrikeUntil = 0f;
        SetProp(typeof(Takedown), "Target", null);
        if (GM) { ((System.Collections.IList)typeof(GameManager).GetField("notices", Any).GetValue(GM)).Clear(); Set(GM, "messageUntil", 0f); }
        Time.timeScale = 1f;
    }

    static void EnsureTapes(int count)
    {
        var w = W; if (!w) return;
        var src = w.tapes.FirstOrDefault(t => t.playable) ?? w.tapes.FirstOrDefault();
        string[] names = { "Walkman Mix Vol.1", "심야 라디오 B면 (복사본)", "Harbor Blues", "City Pop 1984", "Seven Hope", "Lighthouse", "Night Drive", "Rain Jazz" };
        string[] music = { "rain_jazz", "late_radio", "harbor_blues", "citypop_1", "seven_hope", "lighthouse", "night_drive", "rain_jazz" };
        while (w.tapes.Count < count)
        {
            var t = new CassetteTape { trackTitles = src != null ? src.trackTitles : new[] { "Track 1", "Track 2" }, playable = true };
            int i = w.tapes.Count % names.Length;
            t.title = names[i]; t.musicId = music[i]; t.charges = i % 4;
            w.tapes.Add(t);
        }
    }

    static List<Scenario> Build()
    {
        var list = new List<Scenario>();

        // 1) 플레이 중 한꺼번에: 목표 · 길잡이 · 알림 셋 · 긴 자막 · 회복약 · 카세트(+예비) · 센터 문구 · 몰래 제압 · 상호작용 · 기억의 일격
        list.Add(new Scenario
        {
            name = "play_busy",
            setup = () =>
            {
                EnsureTapes(4);
                var w = W; w.potions = 3;
                StoryState.Objective = "본사 동쪽 하역장으로 숨어들어 직원 통로를 찾는다";
                StoryState.Waypoint = UnityEngine.Object.FindAnyObjectByType<PlayerController>().transform;
                GM.Notify("테이프 획득 — 한밤의 라디오 심야 특집 B면", 99f); GM.Notify("회복약 +1", 99f); GM.Notify("+1 되감기", 99f);
                StoryState.Say("레이", LongLine, 99f);
                GM.ShowMessage("한결 레코드 — 동쪽 하역장", 99f);
                PlayerController.MemoryStrikeUntil = Time.unscaledTime + 99f;
                ForceRows();
            },
            teardown = ReleaseRows,
        });
        // 2) 안내(R 길게) + 상호작용 안내가 같이
        list.Add(new Scenario
        {
            name = "prompt_and_interact",
            setup = () =>
            {
                StoryState.Prompt = "R 을 길게 눌러 되감는다";
                ForceRows();
                StoryState.Say("경찰", "거기 누구야! 움직이지 마!", 99f);
                PlayerController.MemoryStrikeUntil = Time.unscaledTime + 99f;
                Ability().Activate(TapeGenre.Soul);
            },
            teardown = () => { ReleaseRows(); Ability().EndNow(); },
        });
        // 3) 테이프 선택지(Q/E) + 능력 막대
        list.Add(new Scenario
        {
            name = "choice",
            setup = () =>
            {
                var w = W; EnsureTapes(4);
                var t = w.tapes.First(x => x.Genre != TapeGenre.None);
                typeof(Walkman).GetProperty("Inserted", Any).SetValue(w, t);
                w.enabled = false; // 선택지가 시간이 지나 저절로 닫히지 않게
                WalkmanPhase("Choice");
                StoryState.Say("레이", "…한 번 더.", 99f);
            },
            teardown = () => { WalkmanPhase("Playing"); W.enabled = true; },
        });
        list.Add(new Scenario
        {
            name = "ability",
            setup = () => { Ability().Activate(TapeGenre.Jazz); PlayerController.MemoryStrikeUntil = Time.unscaledTime + 99f; StoryState.Say("레이", LongLine, 99f); },
            teardown = () => Ability().EndNow(),
        });
        // 4) 쓰러진 화면(되감기 안내)
        list.Add(new Scenario { name = "rewind_prompt", setup = () => { W.enabled = false; WalkmanPhase("Prompt"); GM.Notify("되감기 없음 — I", 99f); }, teardown = () => { WalkmanPhase("Playing"); W.enabled = true; } });
        // 5) 보스 체력
        list.Add(new Scenario
        {
            name = "boss",
            setup = () =>
            {
                var b = EnemyAI.All.FirstOrDefault(x => x && x.isBoss);
                if (b) { b.gameObject.SetActive(true); b.Activate(); }
                StoryState.Say("정리반 반장", LongLine, 99f);
                GM.Notify("기억했다", 99f);
            },
            teardown = () => { var b = EnemyAI.All.FirstOrDefault(x => x && x.isBoss); if (b) b.Deactivate(); },
        });
        // 6) 컷신: 위아래 띠 + 자막 + 안내 + 건너뛰기 + 장 제목
        list.Add(new Scenario
        {
            name = "cutscene",
            setup = () =>
            {
                StoryState.CutsceneActive = true;
                StoryState.Say("정보과장", LongLine, 99f);
                StoryState.Prompt = "R  길게 — 되감는다          F — 부순다";
                StoryState.Title = "5장   30주년";
                CutsceneSkip.SimulateHold = true;
            },
            teardown = () => { CutsceneSkip.SimulateHold = false; StoryState.SkipRequested = false; },
        });
        // 7) 편지
        list.Add(new Scenario
        {
            name = "letter",
            setup = () =>
            {
                StoryState.LetterHeading = "레이에게";
                StoryState.LetterText = string.Join("\n\n", Enumerable.Repeat(LongLine, 5));
            },
        });
        // 8) 인벤토리 / 가게
        list.Add(new Scenario
        {
            name = "inventory",
            setup = () => { EnsureTapes(12); typeof(Walkman).GetMethod("OpenInventory", Any).Invoke(W, null); },
            teardown = () => typeof(Walkman).GetMethod("CloseInventory", Any).Invoke(W, null),
        });
        list.Add(new Scenario
        {
            name = "shop",
            setup = () =>
            {
                var shop = UnityEngine.Object.FindAnyObjectByType<TapeShop>(FindObjectsInactive.Include);
                if (!shop) throw new Exception("가게 없음");
                shop.gameObject.SetActive(true);
                typeof(TapeShop).GetMethod("Open", Any).Invoke(shop, null);
            },
            teardown = () => { var shop = TapeShop.Current; if (shop) typeof(TapeShop).GetMethod("Close", Any).Invoke(shop, null); },
        });
        // 9) 일시정지 쪽들(설정은 탭 셋: 소리 · 화면 · 조작)
        foreach (var page in new[] { "Main", "Options:Sound", "Options:Display", "Options:Controls", "Controls", "Tutorials", "Confirm" })
            list.Add(new Scenario
            {
                name = "pause_" + page.Replace(":", "_"),
                setup = () =>
                {
                    var pm = UnityEngine.Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
                    typeof(PauseMenu).GetMethod("Open", Any).Invoke(pm, null);
                    var parts = page.Split(':');
                    var pt = typeof(PauseMenu).GetNestedType("Page", Any);
                    typeof(PauseMenu).GetField("page", Any).SetValue(pm, Enum.Parse(pt, parts[0]));
                    if (parts.Length > 1)
                    {
                        var tt = typeof(PauseMenu).GetNestedType("Tab", Any);
                        typeof(PauseMenu).GetField("tab", Any).SetValue(pm, Enum.Parse(tt, parts[1]));
                    }
                },
                teardown = () => { var pm = UnityEngine.Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include); typeof(PauseMenu).GetMethod("Close", Any).Invoke(pm, null); },
            });
        // 10) 튜토리얼: 알림 하나 + 모든 설명의 모든 쪽
        var catalog = (System.Collections.IDictionary)typeof(Tutorial).GetField("Catalog", Any).GetValue(null);
        var first = catalog.Values.Cast<Tutorial.Entry>().First();
        list.Add(new Scenario
        {
            name = "tutorial_toast",
            setup = () => { var ui = TutorialUI.Ensure(); Set(ui, "toast", first); Set(ui, "toastStart", Time.unscaledTime - 1.5f); StoryState.Say("레이", LongLine, 99f); EnsureTapes(3); },
            teardown = () => Set(TutorialUI.Ensure(), "toast", null),
        });
        foreach (Tutorial.Entry entry in catalog.Values)
            for (int p = 0; p < entry.pages.Length; p++)
            {
                var e = entry; int pageNo = p;
                list.Add(new Scenario
                {
                    name = $"tut_{e.id}_{pageNo}",
                    setup = () => { var ui = TutorialUI.Ensure(); ui.OpenPopup(e); ui.Turn(pageNo); },
                    teardown = () => TutorialUI.Ensure().ClosePopup(),
                });
            }
        // 11) 비밀번호 패널
        list.Add(new Scenario
        {
            name = "keypad",
            setup = () => { KeypadPanel.Open("0417", null); KeypadPanel.Type("04"); StoryState.Objective = "증거물 보관실에 들어간다"; },
            teardown = () => { KeypadPanel.Type("99"); var k = UnityEngine.Object.FindAnyObjectByType<KeypadPanel>(); if (k) typeof(KeypadPanel).GetMethod("Close", Any).Invoke(k, null); },
        });
        return list;
    }
}
