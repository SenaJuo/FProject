using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 5장 + 종장 시험(플레이 중, Chapter5 장면). choice: 1 = 되감기 엔딩, 2 = 부수기 엔딩.
///  주차장 싸움 → 엘리베이터 → 연구소 실험체 → 격벽 → 홀: 0번(절반에서 경비) → 마스터 테이프 → 방송 → 선택 → 엔딩 → 크레딧.
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
        lines.Clear(); step = 0; shots = 0; choice = endingChoice;
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
        if (!d && step < 20) { d = Object.FindAnyObjectByType<Chapter5Director>(); p = Object.FindAnyObjectByType<PlayerController>(); if (!d) return; }
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
                if (d.CurrentPhase == Chapter5Director.Phase.Lab && !StoryState.ControlLocked && Waited(1f)) { Note($"연구소: 목표 '{StoryState.Objective}'"); Tp(new Vector3(316f, 0.05f, 19f), 90f); Next(); }
                else if (Waited(10f)) End("연구소로 내려가지 못함");
                break;
            case 4:
                if (Waited(1.5f)) { Note($"연구소 싸움: {Line}, 적 {Alive(d.zoneLab)}명"); Shot("lab"); KillAll(d.zoneLab); Next(); }
                break;
            case 5:
                if (d.zoneLab.IsCleared && Waited(1f)) { Tp(new Vector3(341.5f, 0.05f, 19f), 90f); Fire(d.hallDoor); Next(); }
                else if (Waited(6f)) End("연구소가 정리되지 않음");
                break;
            case 6:
                if (d.CurrentPhase == Chapter5Director.Phase.Hall && !StoryState.ControlLocked && Waited(1f)) { Note("홀 도착"); Tp(new Vector3(420f, 0.05f, 10f), 0f); Next(); }
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
                if (d.CurrentPhase == Chapter5Director.Phase.Master && Waited(0.5f)) { Tp(new Vector3(420f, 0.05f, 20.6f), 0f); Fire(d.masterUse); Next(); }
                else if (Waited(12f)) End($"0번 뒤 마스터 테이프 단계로 넘어가지 않음({d.CurrentPhase})");
                break;
            case 11:
                if (d.CurrentPhase == Chapter5Director.Phase.Broadcast && !StoryState.CutsceneActive && Waited(0.5f)) { Note($"마스터 테이프: 목표 '{StoryState.Objective}'"); Tp(new Vector3(406f, 0.05f, 29.6f), 0f); Fire(d.consoleUse); Next(); }
                else if (Waited(30f)) End("방송 단계로 넘어가지 않음");
                break;
            case 12:
                if (d.CurrentPhase == Chapter5Director.Phase.Choice && !string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.5f))
                {
                    Note($"선택: 안내 '{StoryState.Prompt}'");
                    Shot("choice");
                    Chapter5Director.ForcedChoice = choice;
                    Next();
                }
                else if (Waited(60f)) End($"선택이 나오지 않음({d.CurrentPhase})");
                break;
            case 13:
                if ((d.CurrentPhase == Chapter5Director.Phase.EndingA || d.CurrentPhase == Chapter5Director.Phase.EndingB) && Waited(9f)) { Note($"엔딩: {d.CurrentPhase}, {Line}, 제목 '{StoryState.Title}'"); Shot("ending"); Next(); }
                else if (Waited(20f)) End("엔딩으로 넘어가지 않음");
                break;
            case 14:
                if (d && d.CurrentPhase == Chapter5Director.Phase.End && Waited(1f)) { Note($"크레딧: {Line}"); Chapter5Director.ForcedChoice = 0; End("완료"); }
                else if (Waited(40f)) End("크레딧으로 넘어가지 않음");
                break;
        }
    }

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
