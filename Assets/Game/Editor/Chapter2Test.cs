using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 2장 구역 1 시험(플레이 중, Chapter2 장면):
///  ① 시작 → 골목(목표·길잡이) ② 철망 틈(F) → 보관소 안 ③ 경찰에게 들킴 → 시간이 멈추고 되감기 안내 → 되감으면 풀린다
///  ④ 되감을 횟수 없이 들킴 → 붙잡혀 저장 지점부터 ⑤ 승합차를 뒤짐(대사) → 뒷문 목표 ⑥ 뒷문 → 끝.
/// </summary>
public static class Chapter2Test
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots;
    static float since, t0;
    static Chapter2Director d;
    static PlayerController p;
    static Walkman w;
    static Vector3 posBefore;

    public static void Begin(string logPath, string shotDir)
    {
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0;
        t0 = since = Time.realtimeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { t = $"[{Time.realtimeSinceStartup - t0:0.0}s] {(d ? d.CurrentPhase.ToString() : "-")} | {t}"; lines.Add(t); Debug.Log("[2장 시험] " + t); }
    static void Shot(string name) => ScreenCapture.CaptureScreenshot($"{dir}/{shots++:00}_{name}.png");
    static bool Waited(float s) => Time.realtimeSinceStartup - since >= s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Tp(Vector3 at, float yaw) => p.Teleport(at, Quaternion.Euler(0f, yaw, 0f));
    static void Use(StoryInteractable i) => typeof(StoryInteractable).GetMethod("Use", Any)?.Invoke(i, null);
    static void Fire(StoryInteractable i)
    {
        // F를 누른 것과 같다: Used 이벤트를 직접 부른다
        var f = typeof(StoryInteractable).GetField("Used", Any);
        (f?.GetValue(i) as System.Action)?.Invoke();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { End("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (!d) { d = Object.FindAnyObjectByType<Chapter2Director>(); p = Object.FindAnyObjectByType<PlayerController>(); w = p ? p.GetComponent<Walkman>() : null; if (!d) return; }
        switch (step)
        {
            case 0:
                if (d.CurrentPhase == Chapter2Director.Phase.Alley && Waited(1f)) { Note($"골목: 목표 '{StoryState.Objective}', 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "-")}"); Shot("alley"); Next(); }
                else if (Waited(15f)) End("골목 단계로 넘어가지 않음");
                break;
            case 1:
                Tp(new Vector3(14.2f, 0.05f, 11.8f), 0f);
                Fire(d.fenceGap);
                Next();
                break;
            case 2:
                if (d.CurrentPhase == Chapter2Director.Phase.Lot && Waited(1.5f))
                {
                    Note($"보관소 안: 위치 z {p.transform.position.z:0.0}(기대 >14), 목표 '{StoryState.Objective}', 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "-")}");
                    Shot("inside");
                    // ③ 들킴: 서쪽 순찰이 본 것처럼
                    posBefore = p.transform.position;
                    if (w && w.Inserted != null) w.Inserted.charges = Mathf.Max(w.Inserted.charges, 1);
                    Find("Cop_West").Alert(true);
                    Next();
                }
                else if (Waited(10f)) End("보관소 안으로 들어가지 못함");
                break;
            case 3:
                if (d.Caught && !string.IsNullOrEmpty(StoryState.Prompt) && Waited(0.5f))
                {
                    Note($"들킴: 자막 [{StoryState.Speaker}] {StoryState.Line}, 안내 '{StoryState.Prompt}', 시간 배율 {Time.timeScale:0.00}");
                    Shot("caught");
                    Note($"되감기 {w.RewindSteps(1)}");
                    Next();
                }
                else if (Waited(6f)) End($"들켜도 멈추지 않음(Caught {d.Caught}, 안내 '{StoryState.Prompt}')");
                break;
            case 4:
                if (!d.Caught && !w.Rewinding && Waited(1f))
                {
                    var cop = Find("Cop_West");
                    Note($"되감은 뒤: 시간 배율 {Time.timeScale:0.00}, 순찰 경계 {cop.Alerted}(기대 False), 자막 [{StoryState.Speaker}] {StoryState.Line}");
                    // ④ 되감기 없이 들킴 → 붙잡힘
                    foreach (var t in w.tapes) t.charges = 0;
                    Find("Cop_Cross").Alert(true);
                    Next();
                }
                else if (Waited(10f)) End("되감은 뒤 풀리지 않음");
                break;
            case 5:
                if (!d.Caught && Waited(2f) && !StoryState.ControlLocked)
                {
                    Note($"붙잡힘 → 다시: 위치 ({p.transform.position.x:0},{p.transform.position.z:0}), 순찰 경계 {Find("Cop_Cross").Alerted}(기대 False)");
                    // ⑤ 승합차
                    Tp(new Vector3(79f, 0.05f, 88f), 90f);
                    Fire(d.vanUse);
                    Next();
                }
                else if (Waited(12f)) End("붙잡힌 뒤 다시 시작하지 못함");
                break;
            case 6:
                if (StoryState.CutsceneActive && Waited(1.2f)) { Note($"승합차: 자막 [{StoryState.Speaker}] {StoryState.Line}"); Shot("van"); Next(); }
                else if (Waited(5f)) End("승합차를 뒤지지 못함");
                break;
            case 7:
                if (d.CurrentPhase == Chapter2Director.Phase.ToDoor && Waited(0.5f))
                {
                    Note($"뒷문으로: 목표 '{StoryState.Objective}', 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "-")}, 저장 {SaveGame.Read()?.checkpoint ?? "(저장 안 함 — 타이틀에서 시작하지 않음)"}");
                    Tp(d.backDoor.position + Vector3.back * 0.2f, 0f);
                    Next();
                }
                else if (Waited(30f)) End("승합차 뒤 단계로 넘어가지 않음");
                break;
            case 8:
                if (d.CurrentPhase == Chapter2Director.Phase.Station && Waited(2.5f))
                {
                    Note($"경찰서 안: 위치 ({p.transform.position.x:0},{p.transform.position.y:0.0},{p.transform.position.z:0}), 목표 '{StoryState.Objective}', 비 {RainOn()}(기대 False)");
                    Shot("station");
                    // 2층으로(계단 위)
                    Tp(d.stairsTop.position, -90f);
                    if (w.Inserted != null) w.Inserted.charges = 3; // 되감기 넉넉히
                    Next();
                }
                else if (Waited(10f)) End("경찰서 안으로 들어가지 못함");
                break;
            case 9:
                if (d.CurrentPhase == Chapter2Director.Phase.Floor2 && d.clerk.Called)
                {
                    Note($"2층: 담당 부름 {d.clerk.Called}, 목표 '{StoryState.Objective}'");
                    // 담당이 지나가는 길의 등 뒤(휴게실 문 서쪽 복도)에 숨어 지켜본다. 복도 순찰·형사는 잠시 세워 둔다
                    Tp(new Vector3(323f, 4.25f, 18.6f), 90f);
                    Find("Cop_Hall2").Holding = true;
                    Find("Det_Pacing").Holding = true;
                    Find("Det_Desk").Holding = true;
                    Next();
                }
                else if (Waited(8f)) End("2층 단계로 넘어가지 않음");
                break;
            case 10:
                // 비밀번호 패널(2026-10-13): 담당이 누르는 번호를 보고(자막), 패널에 직접 넣는다. 먼저 틀린 번호
                if (d.CodeKnown && d.clerk.Current == EvidenceClerk.State.Inside && Waited(0.3f))
                {
                    Note($"엿봄: 비밀번호 앎 {d.CodeKnown}, 담당 {d.clerk.Current}, 목표 '{StoryState.Objective}'");
                    Shot("clerk_inside");
                    Fire(d.keypadUse);
                    Note($"   패널 열림 {KeypadPanel.IsOpen}(기대 True)");
                    KeypadPanel.Type("1111");
                    Next();
                }
                else if (Waited(30f)) End($"담당이 번호를 누르고 들어가지 않음(상태 {d.clerk.Current}, 앎 {d.CodeKnown})");
                break;
            case 11:
                if (Waited(0.6f))
                {
                    Note($"   틀린 번호 1111: 패널 열림 {KeypadPanel.IsOpen}(기대 True), 단계 {d.CurrentPhase}(기대 Floor2)");
                    KeypadPanel.Type("0417");
                    Note($"   맞는 번호 0417: 패널 닫힘 {!KeypadPanel.IsOpen}(기대 True)");
                    Next();
                }
                break;
            case 12:
                // 안에 담당이 있다: 3초 안에 제압하지 않으면 돌아서서 덤빈다
                if (d.CurrentPhase == Chapter2Director.Phase.EvidenceRoom && Waited(4.5f)) // 페이드 0.5초 + 담당이 돌아서기까지 3초
                {
                    var ai = d.clerk.GetComponent<EnemyAI>();
                    Note($"보관실 안: 목표 '{StoryState.Objective}'(기대 증거물 담당을 쓰러뜨린다), 담당 덤빔 {ai.IsActive}(기대 True), 자막 [{StoryState.Speaker}] {StoryState.Line}");
                    Shot("evidence");
                    ai.Health.TakeDamage(999, ai.transform.position + ai.transform.forward);
                    Next();
                }
                else if (Waited(10f)) End($"보관실에 들어가지 못함(단계 {d.CurrentPhase})");
                break;
            case 13:
                if (StoryState.Objective == "대니 사건 기록을 찾는다" && Waited(0.5f))
                {
                    Note($"담당을 쓰러뜨림: 목표 '{StoryState.Objective}'");
                    Fire(d.dannyBox);
                    Next();
                }
                else if (Waited(10f)) End($"담당을 쓰러뜨린 뒤 목표가 바뀌지 않음('{StoryState.Objective}')");
                break;
            case 14:
                if (d.CurrentPhase == Chapter2Director.Phase.Records && Waited(1f))
                {
                    Note($"열쇠: 목표 '{StoryState.Objective}', 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "-")}");
                    Tp(d.recordsDoorUse.transform.position + new Vector3(0f, -1f, 0.6f), 180f);
                    Fire(d.recordsDoorUse);
                    Next();
                }
                else if (Waited(30f)) End("대니 상자 뒤 기록실 단계로 넘어가지 않음");
                break;
            case 15:
                if (Waited(1.2f)) { Tp(d.terminalUse.transform.position + new Vector3(0f, -1f, 0.4f), 180f); Fire(d.terminalUse); Next(); }
                break;
            case 16:
                if (d.CurrentPhase == Chapter2Director.Phase.Alarm && !StoryState.CutsceneActive && Waited(1f))
                {
                    Note($"경보: 자막 [{StoryState.Speaker}] {StoryState.Line}, 경보 {d.alarm && d.alarm.On}, 저장 {SaveGame.Read()?.checkpoint ?? "-"}, 순찰 남음 {Object.FindObjectsByType<PatrolSight>(FindObjectsSortMode.None).Length}(기대 0)");
                    Shot("alarm");
                    // ① 2층 복도로 나가 싸움을 연다
                    Tp(new Vector3(312f, F2 + 0.05f, 18f), 90f);
                    Next();
                }
                else if (Waited(40f)) End("기록 조회 뒤 경보 단계로 넘어가지 않음");
                break;
            case 17:
                if (Waited(1.5f))
                {
                    Note($"2층 복도 싸움: 자막 [{StoryState.Speaker}] {StoryState.Line}, 적 {Alive(d.zoneUpstairs)}명");
                    Shot("fight_2f");
                    KillAll(d.zoneUpstairs);
                    Next();
                }
                break;
            case 18:
                if (d.zoneUpstairs.IsCleared && Waited(1f))
                {
                    // ② 계단 아래 1층 뒤 복도
                    Tp(new Vector3(330f, 0.05f, 3f), -90f);
                    Next();
                }
                else if (Waited(6f)) End("2층 복도가 정리되지 않음");
                break;
            case 19:
                if (Waited(1.5f))
                {
                    Note($"1층 복도: 단계 {d.CurrentPhase}(기대 Alarm 또는 Downstairs — 경보 장면 뒤 바뀐다), 적 {Alive(d.zoneDownstairs)}명, 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "-")}");
                    Fire(d.backDoorOut);
                    Note($"   싸우는 중 뒷문: 자막 [{StoryState.Speaker}] {StoryState.Line}");
                    KillAll(d.zoneDownstairs);
                    Next();
                }
                break;
            case 20:
                if (d.zoneDownstairs.IsCleared && Waited(1f)) { Tp(d.backDoorOut.transform.position + new Vector3(0.8f, -0.95f, 0f), -90f); Fire(d.backDoorOut); Next(); }
                else if (Waited(6f)) End("1층 복도가 정리되지 않음");
                break;
            case 21:
                if (d.CurrentPhase == Chapter2Director.Phase.Outside && !StoryState.ControlLocked && Waited(1.5f))
                {
                    Note($"밖: 위치 ({p.transform.position.x:0},{p.transform.position.z:0}), 비 {RainOn()}(기대 False — 10-13 비 없앰), 목표 '{StoryState.Objective}', 저장 {SaveGame.Read()?.checkpoint ?? "-"}");
                    Shot("outside");
                    // ③ 사람 문을 지나 보관소 북쪽으로
                    Tp(new Vector3(70f, 0.05f, 97.5f), 180f);
                    Next();
                }
                else if (Waited(10f)) End("밖으로 나가지 못함");
                break;
            case 22:
                if (Waited(2f))
                {
                    var hud = d.subBoss;
                    Note($"부반장: 자막 [{StoryState.Speaker}] {StoryState.Line}, 보스 {hud.isBoss} 활성 {hud.IsActive}, 적 {Alive(d.zoneLot)}명");
                    Shot("subboss");
                    KillAll(d.zoneLot);
                    Next();
                }
                break;
            case 23:
                if (d.CurrentPhase == Chapter2Director.Phase.Reveal && d.traitor && d.traitor.activeInHierarchy && Waited(4.5f))
                {
                    Note($"창가: 자막 [{StoryState.Speaker}] {StoryState.Line}, 창 불 {d.traitorLight.enabled}");
                    Shot("traitor");
                    Next();
                }
                else if (Waited(20f)) End($"배신자 장면이 나오지 않음({d.CurrentPhase})");
                break;
            case 24:
                if (d.CurrentPhase == Chapter2Director.Phase.End && Waited(3f)) { Note($"끝: 제목 '{StoryState.Title}', 자막 {StoryState.Line}"); Shot("end"); End("완료"); }
                else if (Waited(40f)) End("2장 끝으로 넘어가지 않음");
                break;
        }
    }

    const float F2 = 4.2f;

    static int Alive(CombatZone z)
    {
        int n = 0;
        foreach (var e in z.enemies) if (e && e.gameObject.activeInHierarchy && !e.Health.IsDead) n++;
        return n;
    }

    /// <summary>구역의 적을 모두 쓰러뜨린다(등 뒤에서: 방패에 막히지 않게)</summary>
    static void KillAll(CombatZone z)
    {
        foreach (var e in z.enemies)
            if (e && e.gameObject.activeInHierarchy && !e.Health.IsDead)
                e.Health.TakeDamage(9999, e.transform.position - e.transform.forward * 2f);
    }

    static bool RainOn()
    {
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.name == "Rain" && ps.isEmitting) return true;
        return false;
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
