using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 5장 + 종장 시험(플레이 중, Chapter5 장면). choice: 1 = 되감기 엔딩, 2 = 부수기 엔딩.
///  주차장 싸움 → 엘리베이터 → 연구소 실험체 → 격벽 → 홀: 0번(절반에서 경비) → 마스터 테이프
///  (2026-10-23: 유리장에 손대면 회장이 번호를 눌러 소각 → 되감기 → 본 번호 1996으로 연다) → 방송 → 선택 → 엔딩 장면(EndingStage) → 천천히 어두워지고 타이틀.
/// </summary>
public static class Chapter5Test
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots, choice;
    static float since, t0;
    static Chapter5Director d;
    static PlayerController p;

    public static void Begin(string logPath, string shotDir, int endingChoice)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0; choice = endingChoice; lastBeat = null;
        Chapter5Director.ForcedChoice = 0;
        t0 = since = Time.realtimeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { t = $"[{Time.realtimeSinceStartup - t0:0.0}s] {(d ? d.CurrentPhase.ToString() : "-")} | {t}"; lines.Add(t); Debug.Log("[5장 시험] " + t); }
    static void Shot(string name) => ScreenCapture.CaptureScreenshot($"{dir}/{shots++:00}_{name}.png");
    static bool Waited(float s) => Time.realtimeSinceStartup - since >= s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Tp(Vector3 at, float yaw) => p.Teleport(at, Quaternion.Euler(0f, yaw, 0f));
    static void Fire(StoryInteractable i) => (typeof(StoryInteractable).GetField("Used", Any)?.GetValue(i) as System.Action)?.Invoke();
    static string Line => $"[{StoryState.Speaker}] {StoryState.Line}";

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { End("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (!d && step < 16) { d = Object.FindAnyObjectByType<Chapter5Director>(); p = Object.FindAnyObjectByType<PlayerController>(); if (!d) return; }
        switch (step)
        {
            case 0:
                if (d.CurrentPhase == Chapter5Director.Phase.Garage && Waited(1f)) { Note($"시작: 목표 '{StoryState.Objective}'"); Shot("garage"); Tp(new Vector3(0f, 0.05f, 22f), 0f); Next(); }
                else if (Waited(15f)) End("주차장 단계로 넘어가지 않음");
                break;
            case 1:
                if (Waited(1.5f)) { Note($"주차장 싸움: {Line}, 적 {Alive(d.zoneGarage)}명"); Shot("garage_fight"); KillAll(d.zoneGarage); Next(); }
                break;
            case 2:
                if (d.zoneGarage.IsCleared && Waited(1f)) { Tp(new Vector3(0f, 0.05f, 37f), 0f); Fire(d.liftUse); /* 주차장을 줄였다(북쪽 벽 z 40) */ Next(); }
                else if (Waited(6f)) End("주차장이 정리되지 않음");
                break;
            case 3:
                if (d.CurrentPhase == Chapter5Director.Phase.Lab && !StoryState.ControlLocked && Waited(1f)) { Note($"연구소: 목표 '{StoryState.Objective}'"); Tp(new Vector3(310f, 0.05f, 19f), 90f); Next(); }
                else if (Waited(10f)) End("연구소로 내려가지 못함");
                break;
            case 4:
                if (Waited(1.5f)) { Note($"연구소 싸움: {Line}, 적 {Alive(d.zoneLab)}명"); Shot("lab"); KillAll(d.zoneLab); Next(); }
                break;
            case 5:
                if (d.zoneLab.IsCleared && Waited(1f)) { Tp(new Vector3(325.5f, 0.05f, 19f), 90f); Fire(d.hallDoor); Next(); }
                else if (Waited(6f)) End("연구소가 정리되지 않음");
                break;
            case 6:
                if (d.CurrentPhase == Chapter5Director.Phase.Hall && !StoryState.ControlLocked && Waited(1f)) { Note("홀 도착"); Tp(new Vector3(420f, 0.05f, 7f), 0f); Next(); }
                else if (Waited(10f)) End("홀로 가지 못함");
                break;
            case 7:
                if (d.CurrentPhase == Chapter5Director.Phase.Hall && StoryState.CutsceneActive && Waited(3.5f)) { Note($"윗선: {Line}"); Shot("gallery"); Next(); }
                else if (Waited(10f)) End("보스 소개가 나오지 않음");
                break;
            case 8:
                if (d.CurrentPhase == Chapter5Director.Phase.Boss && Waited(1f))
                {
                    var h = d.zero.Health;
                    Note($"0번: 보스 {d.zero.isBoss}, 활성 {d.zero.IsActive}, 체력 {h.Current}/{h.Max}");
                    Shot("zero");
                    h.TakeDamage(h.Current - Mathf.FloorToInt(h.Max * 0.45f), d.zero.transform.position - d.zero.transform.forward);
                    Next();
                }
                else if (Waited(30f)) End("0번과 싸움이 시작되지 않음");
                break;
            case 9:
                if (Waited(1.5f))
                {
                    int guards = 0; foreach (var g in d.guards) if (g && g.activeInHierarchy) guards++;
                    Note($"절반: {Line}, 경비 {guards}명(기대 2)");
                    d.zero.Health.TakeDamage(9999, d.zero.transform.position - d.zero.transform.forward);
                    Next();
                }
                break;
            case 10:
                if (d.CurrentPhase == Chapter5Director.Phase.Master && Waited(0.5f)) { Tp(new Vector3(420f, 0.05f, 13.6f), 0f); Fire(d.masterUse); Next(); }
                else if (Waited(12f)) End($"0번 뒤 마스터 테이프 단계로 넘어가지 않음({d.CurrentPhase})");
                break;
            case 11:
            {
                var w = p.GetComponent<Walkman>();
                if (StoryState.RewindAsked && Waited(0.5f))
                {
                    Note($"유리장: {Line}, 번호 앎 {d.CaseCodeKnown}(기대 True), 테이프 높이 {d.masterTape.position.y:0.00}, 되감기 {w.RewindSteps(1)}");
                    Shot("case_burn");
                    Next();
                }
                else if (Waited(15f)) End("유리장에 손댄 뒤 되감기를 묻지 않음");
                break;
            }
            case 12:
                if (!p.GetComponent<Walkman>().Rewinding && StoryState.Objective == "마스터 테이프를 꺼낸다" && Waited(1f))
                {
                    Note($"되감은 뒤: {Line}, 테이프 높이 {d.masterTape.position.y:0.00}");
                    Fire(d.masterUse);
                    Note($"   번호판 열림 {KeypadPanel.IsOpen}(기대 True)");
                    KeypadPanel.Type("1996");
                    Next();
                }
                else if (Waited(12f)) End($"되감은 뒤 유리장으로 돌아오지 않음(목표 '{StoryState.Objective}')");
                break;
            case 13:
                if (d.CurrentPhase == Chapter5Director.Phase.Broadcast && !StoryState.CutsceneActive && Waited(0.5f)) { Note($"마스터 테이프: 목표 '{StoryState.Objective}'"); Tp(new Vector3(410f, 0.05f, 20.1f), 0f); Fire(d.consoleUse); Next(); }
                else if (Waited(30f)) End("방송 단계로 넘어가지 않음");
                break;
            case 14:
                if (d.CurrentPhase == Chapter5Director.Phase.Choice && !string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.5f))
                {
                    Note($"선택: 안내 '{StoryState.Prompt}'");
                    Shot("choice");
                    Chapter5Director.ForcedChoice = choice;
                    Next();
                }
                else if (Waited(60f)) End($"선택이 나오지 않음({d.CurrentPhase})");
                break;
            case 15:
                // 2026-10-23: 엔딩은 다른 장면(A = 프롤로그, B = 1장)을 무대로 찍는다(EndingStage)
                if (EndingStage.Playing != EndingStage.Kind.None && Waited(0.5f)) { Note($"엔딩 시작: {EndingStage.Playing}(기대 {(choice == 1 ? "Rewind" : "Break")})"); Next(); }
                else if (Waited(20f)) End("엔딩으로 넘어가지 않음");
                break;
            case 16:
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (EndingStage.Beat != lastBeat) { lastBeat = EndingStage.Beat; Note($"엔딩 장면 {lastBeat} ({scene}): {Line}"); if (lastBeat is "danny" or "tape") Shot("ending_" + lastBeat); }
                if (EndingStage.Finished && scene == "Title" && Waited(1f)) { Chapter5Director.ForcedChoice = 0; End("완료 — 엔딩 뒤 타이틀로 돌아옴"); }
                else if (Waited(120f)) End($"엔딩이 끝나지 않음(장면 {EndingStage.Beat})");
                break;
            }
        }
    }

    static string lastBeat;

    static int Alive(CombatZone z)
    {
        int n = 0;
        foreach (var e in z.enemies) if (e && e.gameObject.activeInHierarchy && !e.Health.IsDead) n++;
        return n;
    }

    static void KillAll(CombatZone z)
    {
        foreach (var e in z.enemies)
            if (e && e.gameObject.activeInHierarchy && !e.Health.IsDead)
                e.Health.TakeDamage(9999, e.transform.position - e.transform.forward * 2f);
    }

    static void End(string why)
    {
        EditorApplication.update -= Tick;
        Note("시험 끝: " + why);
        if (log != null) System.IO.File.WriteAllLines(log, lines);
        log = null; d = null;
    }
}
