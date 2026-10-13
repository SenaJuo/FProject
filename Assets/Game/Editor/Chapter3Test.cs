using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 3장 시험(플레이 중, Chapter3 장면):
///  ① 시작 → 하역장(저장) ② 경비에게 들킴 → 되감기 ③ 직원 통로 → 녹음실 층
///  ④ 조정실: 눈앞에서 쓰러짐 → 되감기 → 장비를 끈다 → 7번의 증언 ⑤ 실험체 셋 ⑥ 엘리베이터 → 옥상 정리반
///  ⑦ 금고실 → 옛 파트너 → 40%에서 빠져나감 → 3장 끝.
/// </summary>
public static class Chapter3Test
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots;
    static float since, t0;
    static Chapter3Director d;
    static PlayerController p;
    static Walkman w;

    public static void Begin(string logPath, string shotDir)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0;
        t0 = since = Time.realtimeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { t = $"[{Time.realtimeSinceStartup - t0:0.0}s] {(d ? d.CurrentPhase.ToString() : "-")} | {t}"; lines.Add(t); Debug.Log("[3장 시험] " + t); }
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
        if (!d) { d = Object.FindAnyObjectByType<Chapter3Director>(); p = Object.FindAnyObjectByType<PlayerController>(); w = p ? p.GetComponent<Walkman>() : null; if (!d) return; }
        switch (step)
        {
            case 0:
                if (d.CurrentPhase == Chapter3Director.Phase.Street && Waited(1f))
                {
                    Note($"시작: 목표 '{StoryState.Objective}', 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "-")}");
                    Shot("street");
                    if (w.Inserted != null) w.Inserted.charges = 3;
                    Tp(new Vector3(50f, 0.05f, -34f), 0f);
                    Next();
                }
                else if (Waited(15f)) End("거리 단계로 넘어가지 않음");
                break;
            case 1:
                if (d.CurrentPhase == Chapter3Director.Phase.Dock && Waited(1.5f))
                {
                    Note($"하역장: 목표 '{StoryState.Objective}'");
                    Shot("dock");
                    Find("Guard_West").Alert(true);
                    Next();
                }
                else if (Waited(8f)) End("하역장에 들어가지 못함");
                break;
            case 2:
                if (d.Caught && !string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.4f))
                {
                    Note($"들킴: {Line}, 안내 '{StoryState.Prompt}', 시간 {Time.timeScale:0.00}");
                    Note($"   되감기 {w.RewindSteps(1)}");
                    Next();
                }
                else if (Waited(6f)) End("들켜도 멈추지 않음");
                break;
            case 3:
                if (!d.Caught && !w.Rewinding && Waited(1f))
                {
                    Note($"되감은 뒤: 경계 {Find("Guard_West").Alerted}(기대 False)");
                    Tp(new Vector3(31.8f, 0.05f, 44f), -90f);
                    Fire(d.serviceDoor);
                    Next();
                }
                else if (Waited(10f)) End("되감은 뒤 풀리지 않음");
                break;
            case 4:
                if (d.CurrentPhase == Chapter3Director.Phase.Studio && !StoryState.ControlLocked && Waited(1f))
                {
                    Note($"녹음실 층: 위치 ({p.transform.position.x:0},{p.transform.position.z:0}), 목표 '{StoryState.Objective}', 비 꺼짐");
                    Shot("studio");
                    if (w.Inserted != null) w.Inserted.charges = 3;
                    Tp(new Vector3(305f, 0.05f, 15f), 90f); // 복도 시작(실험이 시작된다)
                    Next();
                }
                else if (Waited(10f)) End("녹음실 층으로 가지 못함");
                break;
            case 5:
                if (Waited(2.5f))
                {
                    Note($"실험 시작: 부스 {d.booth.Current}, 경과 {d.booth.Elapsed:0.0}초");
                    Fire(d.switchUse);
                    Note($"   모를 때 장비: {Line}");
                    Tp(new Vector3(316f, 0.05f, 20f), 0f); // 조정실 문 안
                    Next();
                }
                break;
            case 6:
                if (d.Caught && !string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.3f))
                {
                    Note($"쓰러짐: {Line}, 부스 {d.booth.Current}, 레이가 앎 {d.KnowsCollapse}");
                    Shot("collapse");
                    Note($"   되감기 {w.RewindSteps(1)}");
                    Next();
                }
                else if (Waited(10f)) End($"조정실에서 쓰러지는 장면이 나오지 않음(부스 {d.booth.Current})");
                break;
            case 7:
                if (!d.Caught && !w.Rewinding && Waited(1f))
                {
                    Note($"되감은 뒤: 부스 {d.booth.Current}(기대 Running), 앎 {d.KnowsCollapse}(기대 True), 목표 '{StoryState.Objective}'");
                    Tp(new Vector3(318.4f, 0.05f, 24f), 90f);
                    Fire(d.switchUse);
                    Next();
                }
                else if (Waited(10f)) End("되감은 뒤 풀리지 않음");
                break;
            case 8:
                if (d.CurrentPhase == Chapter3Director.Phase.Saved && Waited(8f)) { Note($"살림: 부스 {d.booth.Current}, {Line}"); Shot("seven"); Next(); }
                else if (Waited(12f)) End($"장비를 꺼도 살리지 못함(부스 {d.booth.Current})");
                break;
            case 9:
                if (d.CurrentPhase == Chapter3Director.Phase.Subjects && !StoryState.CutsceneActive && Waited(0.5f))
                {
                    Tp(new Vector3(316f, 0.05f, 15f), 180f);
                    Next();
                }
                else if (Waited(40f)) End("실험체 단계로 넘어가지 않음");
                break;
            case 10:
                if (Waited(1.5f)) { Note($"실험체: {Line}, 적 {Alive(d.zoneSubjects)}명"); Shot("subjects"); KillAll(d.zoneSubjects); Next(); }
                break;
            case 11:
                if (d.CurrentPhase == Chapter3Director.Phase.ToLift && Waited(0.5f)) { Tp(new Vector3(338f, 0.05f, 15f), 90f); Fire(d.elevatorUse); Next(); }
                else if (Waited(10f)) End("실험체를 정리해도 엘리베이터 단계로 넘어가지 않음");
                break;
            case 12:
                if (d.CurrentPhase == Chapter3Director.Phase.Roof && !StoryState.ControlLocked && Waited(1f))
                {
                    Note($"옥상: 위치 ({p.transform.position.x:0},{p.transform.position.z:0}), 목표 '{StoryState.Objective}'");
                    Tp(new Vector3(310f, 0.05f, 112f), 0f);
                    Next();
                }
                else if (Waited(10f)) End("옥상으로 가지 못함");
                break;
            case 13:
                if (Waited(1.5f)) { Note($"옥상 싸움: {Line}, 적 {Alive(d.zoneRoof)}명"); Shot("roof"); KillAll(d.zoneRoof); Next(); }
                break;
            case 14:
                if (d.zoneRoof.IsCleared && Waited(1f)) { Tp(new Vector3(336f, 0.05f, 136f), 0f); Next(); }
                else if (Waited(6f)) End("옥상이 정리되지 않음");
                break;
            case 15:
                if (d.CurrentPhase == Chapter3Director.Phase.Vault && Waited(4f)) { Note($"금고실: {Line}"); Shot("vault"); Next(); }
                else if (Waited(10f)) End("금고실 장면이 나오지 않음");
                break;
            case 16:
                if (d.CurrentPhase == Chapter3Director.Phase.Duel && Waited(1f))
                {
                    var h = d.partner.Health;
                    Note($"결투: 파트너 보스 {d.partner.isBoss}, 활성 {d.partner.IsActive}, 체력 {h.Current}/{h.Max}");
                    Shot("duel");
                    h.TakeDamage(h.Current - Mathf.FloorToInt(h.Max * 0.35f), d.partner.transform.position - d.partner.transform.forward);
                    Next();
                }
                else if (Waited(25f)) End("결투로 넘어가지 않음");
                break;
            case 17:
                if (d.CurrentPhase == Chapter3Director.Phase.End && Waited(1f)) { Note($"끝: {Line}, 파트너 남은 체력 {d.partner.Health.Current}(쓰러지지 않음)"); Shot("end"); End("완료"); }
                else if (Waited(30f)) End($"3장 끝으로 넘어가지 않음({d.CurrentPhase})");
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

    static PatrolSight Find(string name)
    {
        foreach (var s in Object.FindObjectsByType<PatrolSight>(FindObjectsSortMode.None)) if (s.name == name) return s;
        return null;
    }

    static void End(string why)
    {
        EditorApplication.update -= Tick;
        Note("시험 끝: " + why);
        if (log != null) System.IO.File.WriteAllLines(log, lines);
        log = null; d = null;
    }
}
