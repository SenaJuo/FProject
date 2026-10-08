using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 되감기의 기억 시험(플레이 중, 1장 시작 직후):
///  ① 순찰에게 들킨 뒤 되감으면 → 그 순찰은 아무것도 모르고(발각 풀림) 레이만 기억한다: 바닥에 시야 부채꼴이 보인다(가림 반영). 사진.
///  ② 조직원에게 맞은 뒤 되감으면 → 그 조직원은 "미리 아는 공격" 3번이 되고, 보통이면 너무 이른 구르기(0.4초 전)도 완벽 회피가 된다.
/// </summary>
public static class MemoryTest
{
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots;
    static float next, t0, waitStart, hpBefore;
    static Chapter1Director d;
    static PlayerController p;
    static Health hp;
    static Walkman w;
    static PatrolSight lawn;
    static EnemyAI thug;
    static bool hitSeen, perfectSeen, dodged, armed;

    public static void Begin(string logPath, string shotDir)
    {
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0; hitSeen = perfectSeen = dodged = armed = false;
        t0 = Time.unscaledTime; next = Time.unscaledTime + 0.5f;
        EnemyAI.HitPlayer -= OnHit; EnemyAI.HitPlayer += OnHit;
        EnemyAI.PerfectDodged -= OnPerfect; EnemyAI.PerfectDodged += OnPerfect;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void OnHit(EnemyAI e) { if (e == thug) hitSeen = true; }
    static void OnPerfect(EnemyAI e) { if (e == thug) perfectSeen = true; }
    static void Note(string t) { t = $"[{Time.unscaledTime - t0:0.0}s] {t}"; lines.Add(t); Debug.Log("[기억 시험] " + t); }

    static void Shot(string name, Vector3 from, Vector3 at)
    {
        var tex = QaShots.Render(from, at, 55f, 960, 540);
        System.IO.File.WriteAllBytes($"{dir}/{shots++:00}_{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static void Dodge() => typeof(PlayerController).GetMethod("BeginDodge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(p, new object[] { -p.transform.forward });

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Done(); return; }
        EditorApplication.QueuePlayerLoopUpdate();

        // ② 매 프레임: 기억한 주먹이 닿기 0.4초 전에 구른다(평소라면 구르기가 0.22초라 이미 끝나 완벽 회피가 아니다)
        if (step == 9 && armed && !dodged && thug.WindupLeft > 0f && thug.WindupLeft <= 0.4f)
        {
            dodged = true; hpBefore = hp.Current;
            Note($"   주먹까지 {thug.WindupLeft:0.00}초 남기고 구름(기억한 공격 {thug.Foreseen}번 남음)");
            Dodge();
        }

        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.25f;
        switch (step++)
        {
            case 0:
            {
                d = Object.FindAnyObjectByType<Chapter1Director>();
                if (!d || d.CurrentPhase != Chapter1Director.Phase.Sneak || StoryState.InputLocked) { step--; return; }
                p = d.player; hp = p.GetComponent<Health>(); w = p.GetComponent<Walkman>();
                foreach (var t in w.tapes) t.charges = 3;
                lawn = GameObject.Find("Thug_Lawn").GetComponent<PatrolSight>();
                lawn.CanDetect = false;
                Note($"① 6초 기록 뒤 잔디밭 순찰 앞 5m(손전등 안)에 서서 감지를 켠다");
                next = Time.unscaledTime + 6f;
                break;
            }
            case 1:
            {
                Vector3 at = lawn.transform.position + lawn.transform.forward * 5f; at.y = lawn.transform.position.y + 0.05f;
                p.Teleport(at, Quaternion.LookRotation(lawn.transform.position - at));
                lawn.CanDetect = true; waitStart = Time.unscaledTime;
                break;
            }
            case 2:
                if (!lawn.Alerted && Time.unscaledTime - waitStart < 6f) { step--; return; }
                Note($"   들킴: {lawn.Alerted} ({Time.unscaledTime - waitStart:0.0}초 만에)");
                next = Time.unscaledTime + 0.6f;
                break;
            case 3:
                Note($"   되감기 {w.RewindSteps(1)}");
                break;
            case 4:
            {
                if (w.Rewinding) { step--; return; }
                lawn.CanDetect = false; // 살펴보는 동안 다시 들키지 않게
                Note($"   되감은 뒤: 순찰 발각 {lawn.Alerted}(기대 False) · 시야가 보이는 순찰 {RewindMemory.SightCount}(기대 1) · 시야 최대 거리 {RewindMemory.SightReach(lawn):0.0}m(0보다 크고 {lawn.viewDistance}m 이하)");
                next = Time.unscaledTime + 0.6f;
                break;
            }
            case 5:
            {
                Vector3 c = lawn.transform.position + lawn.transform.forward * 4f;
                Shot("sight_cone_top", c + Vector3.up * 12f - lawn.transform.forward * 6f, c);
                Vector3 me = p.transform.position, to = lawn.transform.position - me; to.y = 0f; to.Normalize();
                Shot("sight_cone_player", me + Vector3.up * 2.6f - to * 4f, lawn.transform.position);
                break;
            }
            case 6:
            {
                // ② 조직원에게 한 대 맞는다
                lawn.CanDetect = true; lawn.Holding = true; // 이후 시험에 끼어들지 않게 세워 둔다
                thug = GameObject.Find("Thug_Exit_B").GetComponent<EnemyAI>();
                thug.GetComponent<PatrolSight>().Holding = true;
                Vector3 at = thug.transform.position + thug.transform.forward * 1.3f; at.y = thug.transform.position.y + 0.02f;
                p.Teleport(at, Quaternion.LookRotation(thug.transform.position - at));
                hp.Heal(100);
                next = Time.unscaledTime + 6f; // 되감기(5초)가 ①의 자리까지 넘어가지 않게 기록을 쌓는다
                Note($"② 출구 조직원 앞에 섬. 기억한 공격 {thug.Foreseen}(기대 0)");
                break;
            }
            case 7:
                thug.Activate(); waitStart = Time.unscaledTime;
                break;
            case 8:
                if (!hitSeen && Time.unscaledTime - waitStart < 8f) { step--; return; }
                Note($"   맞음: {hitSeen}, 체력 {hp.Current}. 되감기 {w.RewindSteps(1)}");
                break;
            case 9:
                if (w.Rewinding) { step--; return; }
                if (!armed) { armed = true; Note($"   되감은 뒤 기억한 공격 {thug.Foreseen}(기대 3)"); }
                if (Time.unscaledTime - waitStart < 30f && (!dodged || thug.WindupLeft > 0f)) { if (!thug.IsActive) thug.Activate(); step--; return; }
                next = Time.unscaledTime + 0.3f;
                break;
            case 10:
                Note($"   되감은 뒤 결과: 완벽 회피 {perfectSeen}(기대 True) · 받은 피해 {hpBefore - hp.Current}(기대 0) · 기억한 공격 남음 {thug.Foreseen}(기대 2)");
                Done();
                break;
        }
    }

    static void Done()
    {
        EditorApplication.update -= Tick;
        EnemyAI.HitPlayer -= OnHit;
        EnemyAI.PerfectDodged -= OnPerfect;
        if (log != null) System.IO.File.WriteAllLines(log, lines);
        log = null;
    }
}
