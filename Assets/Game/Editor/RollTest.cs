using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 구르기 시험(플레이 중, 1장 시작 직후). 매 프레임 위치·상태·애니메이션 상태를 기록한다.
///  ① 손 떼고 구르기: 약 3.2m 나아가고, 착지 뒤 일어서는 동안 위치가 그대로인가(밀림 없음). 무적은 처음 0.35초만.
///  ② W를 누른 채 구르기: 착지하자마자 걷기·달리기 동작으로 넘어가는가(구르기 동작인 채 미끄러지지 않는가).
/// </summary>
public static class RollTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly List<string> lines = new();
    static string log, dir;
    static int round, shots;
    static float t0, rollAt;
    static bool rolling;
    static Vector3 startPos, lastPos;
    static PlayerController p;
    static Animator anim;
    static float settleMove, rollEndedAt;
    static readonly List<Texture2D> sideShots = new(), frontShots = new();

    public static void Begin(string logPath, string shotDir)
    {
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); round = 0; shots = 0; rolling = false;
        p = Object.FindAnyObjectByType<PlayerController>();
        anim = p.GetComponentInChildren<Animator>();
        t0 = Time.unscaledTime;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { t = $"[{Time.unscaledTime - t0:0.00}s] {t}"; lines.Add(t); Debug.Log("[구르기 시험] " + t); }
    static string State() => typeof(PlayerController).GetField("state", Any).GetValue(p).ToString();
    static string AnimState()
    {
        var s = anim.GetCurrentAnimatorStateInfo(0);
        string n = s.IsName("Roll") ? "Roll" : s.IsName("Idle Walk Run Blend") ? "Loco" : s.shortNameHash.ToString();
        return anim.IsInTransition(0) ? n + "→" : n;
    }
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
    static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return (a - b).magnitude; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Done(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        float now = Time.unscaledTime;
        if (StoryState.InputLocked) { Keys(); return; } // 장 시작 제목·페이드
        if (!rolling)
        {
            if (now - t0 < 1.5f + round * 2.5f) return;
            if (round == 4) { Keys(); Done(); return; }
            // 구른다(① 손 떼고 ② W를 누른 채)
            startPos = lastPos = p.transform.position;
            if (round == 1) Keys(Key.W); else Keys();
            // 회피는 마우스 오른쪽 — 시험에서는 바로 부른다(앞으로 구른다)
            if (round >= 2) typeof(PlayerController).GetMethod("BeginAttack", Any).Invoke(p, new object[] { round - 2, Vector3.zero }); // ③ 잽 ④ 크로스
            else typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(p, new object[] { p.transform.forward });
            rolling = true; rollAt = now; settleMove = 0f; rollEndedAt = -1f;
            Note($"── {(round == 0 ? "구르기 ①(손 뗌)" : round == 1 ? "구르기 ②(W 누른 채)" : round == 2 ? "③ 잽" : "④ 크로스")}");
            return;
        }
        if (round == 1) Keys(Key.W); else Keys();
        float t = now - rollAt;
        var pos = p.transform.position;
        bool inv = p.GetComponent<Health>().Invulnerable;
        var hips = anim.GetBoneTransform(HumanBodyBones.Hips).position; var head = anim.GetBoneTransform(HumanBodyBones.Head).position;
        Vector3 lean = Quaternion.Inverse(p.transform.rotation) * (head - hips);
        Note($"t {t:0.00} 상체 앞기울기 {Mathf.Atan2(lean.z, lean.y) * Mathf.Rad2Deg:0}° 상태 {State()} 동작 {AnimState()} 이동 {Flat(pos, startPos):0.00}m 프레임당 {Flat(pos, lastPos) * 100f:0.0}cm 무적 {inv}");
        // 실제로 구르는 동안 옆·비스듬히 앞에서 찍는다(애니메이터가 실제로 움직인 자세)
        if (shots < 10 && t > shots * 0.08f)
        {
            Vector3 c = pos + Vector3.up * 0.7f, side = p.transform.right, fwd = p.transform.forward;
            side.y = fwd.y = 0f;
            sideShots.Add(QaShots.Render(c - side * 3.4f + Vector3.up * 0.3f, c, 40f, 360, 360));
            frontShots.Add(QaShots.Render(c + fwd * 3.2f - side * 1.6f + Vector3.up * 0.4f, c, 40f, 360, 360));
            shots++;
        }
        if (State() != "Dodge" && rollEndedAt < 0f) rollEndedAt = t;
        lastPos = pos;
        if (t > (round >= 2 ? 0.7f : 1.3f))
        {
            Note($"   끝: 총 이동 {Flat(pos, startPos):0.00}m, 구르기 끝 {rollEndedAt:0.00}초, 마지막 동작 {AnimState()}");
            if (sideShots.Count > 0)
            {
                sideShots.AddRange(frontShots);
                QaShots.Sheet($"{dir}/roll_round{round}.png", sideShots.ToArray(), 10);
                sideShots.Clear(); frontShots.Clear();
            }
            rolling = false; round++; shots = 0; Keys();
        }
    }

    static void Done()
    {
        EditorApplication.update -= Tick;
        Note("시험 끝");
        System.IO.File.WriteAllLines(log, lines);
    }
}
