using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 4장 시험(플레이 중, Chapter4 장면):
///  ① 시작 → 굽잇길 매복(정리반 넷) → 정문 ② 마당: 파트너와 결투 → 60%에서 저격 → 1분 되감기(정문으로)
///  ③ 저격수 처리 → 파트너의 보고 → 결투 → 40%에서 자백 → 입막음 → 4장 끝.
/// </summary>
public static class Chapter4Test
{
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots;
    static float since, t0;
    static Chapter4Director d;
    static PlayerController p;

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

    static void Note(string t) { t = $"[{Time.realtimeSinceStartup - t0:0.0}s] {(d ? d.CurrentPhase.ToString() : "-")} | {t}"; lines.Add(t); Debug.Log("[4장 시험] " + t); }
    static void Shot(string name) => ScreenCapture.CaptureScreenshot($"{dir}/{shots++:00}_{name}.png");
    static bool Waited(float s) => Time.realtimeSinceStartup - since >= s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Tp(Vector3 at, float yaw) => p.Teleport(at, Quaternion.Euler(0f, yaw, 0f));
    static string Line => $"[{StoryState.Speaker}] {StoryState.Line}";

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { End("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (!d) { d = Object.FindAnyObjectByType<Chapter4Director>(); p = Object.FindAnyObjectByType<PlayerController>(); if (!d) return; }
        switch (step)
        {
            case 0:
                if (d.CurrentPhase == Chapter4Director.Phase.Road && Waited(1f)) { Note($"시작: 목표 '{StoryState.Objective}'"); Shot("start"); Tp(new Vector3(0f, 10.1f, -36f), 0f); Next(); }
                else if (Waited(15f)) End("산길 단계로 넘어가지 않음");
                break;
            case 1:
                if (Waited(1.5f)) { Note($"매복: {Line}, 적 {Alive(d.zoneAmbush)}명"); Shot("ambush"); KillAll(d.zoneAmbush); Next(); }
                break;
            case 2:
                if (d.zoneAmbush.IsCleared && Waited(1f)) { Note($"매복 정리: {Line}"); Tp(new Vector3(0f, 20.1f, 23f), 0f); Next(); }
                else if (Waited(6f)) End("매복이 정리되지 않음");
                break;
            case 3:
                if (d.CurrentPhase == Chapter4Director.Phase.Gate && Waited(0.5f)) { Tp(new Vector3(0f, 20.1f, 38f), 0f); Next(); }
                else if (Waited(8f)) End("정문에 닿지 않음");
                break;
            case 4:
                if (d.CurrentPhase == Chapter4Director.Phase.Duel1 && Waited(1f))
                {
                    var h = d.partner.Health;
                    Note($"결투 1: 파트너 {h.Current}/{h.Max}, 활성 {d.partner.IsActive}");
                    Shot("duel1");
                    h.TakeDamage(h.Current - Mathf.FloorToInt(h.Max * 0.55f), d.partner.transform.position - d.partner.transform.forward);
                    Next();
                }
                else if (Waited(30f)) End("결투가 시작되지 않음");
                break;
            case 5:
                if (d.CurrentPhase == Chapter4Director.Phase.Shot && Waited(1.2f)) { var rd = p.GetComponentInChildren<Ragdoll>(); Note($"저격: {Line}, 쓰러짐(래그돌) {rd && rd.IsActive}, 머리 높이 {p.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head).position.y - p.transform.position.y:0.0}m"); Shot("shot"); Next(); }
                else if (Waited(10f)) End($"저격 장면이 나오지 않음({d.CurrentPhase})");
                break;
            case 6:
                if (d.CurrentPhase == Chapter4Director.Phase.Hunt && !StoryState.CutsceneActive && Waited(1f))
                {
                    Note($"1분 되감기 뒤: 위치 ({p.transform.position.x:0},{p.transform.position.y:0},{p.transform.position.z:0}), 앎 {d.KnowsSniper}, 목표 '{StoryState.Objective}', 파트너 {d.partner.Health.Current}/{d.partner.Health.Max}");
                    Shot("after_rewind");
                    Tp(new Vector3(16f, 20.1f, 38.5f), 0f); // 계단 아래(저격수 구역)
                    Next();
                }
                else if (Waited(40f)) End($"1분 되감기 뒤 저격수 단계로 넘어가지 않음({d.CurrentPhase})");
                break;
            case 7:
                if (Waited(1.5f))
                {
                    Note($"저격수: 활성 {d.sniper.IsActive}, {Line}");
                    d.sniper.Health.TakeDamage(9999, d.sniper.transform.position - d.sniper.transform.forward);
                    Next();
                }
                break;
            case 8:
                if (d.CurrentPhase == Chapter4Director.Phase.Duel2 && Waited(1f))
                {
                    var h = d.partner.Health;
                    Note($"결투 2: 파트너 {h.Current}/{h.Max}");
                    Shot("duel2");
                    h.TakeDamage(h.Current - Mathf.FloorToInt(h.Max * 0.35f), d.partner.transform.position - d.partner.transform.forward);
                    Next();
                }
                else if (Waited(40f)) End($"결투 2가 시작되지 않음({d.CurrentPhase})");
                break;
            case 9:
                if (d.CurrentPhase == Chapter4Director.Phase.Confess && Waited(8f)) { Note($"자백: {Line}"); Shot("confess"); Next(); }
                else if (Waited(12f)) End("자백 장면이 나오지 않음");
                break;
            case 10:
                if (d.CurrentPhase == Chapter4Director.Phase.End && Waited(0.5f)) { Note($"끝: 파트너 쓰러짐 {d.partner.Health.IsDead}, {Line}"); End("완료"); }
                else if (Waited(30f)) End("4장 끝으로 넘어가지 않음");
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
