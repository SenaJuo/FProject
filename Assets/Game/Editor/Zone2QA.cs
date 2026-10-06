using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 구역 2 검수 도구(플레이 중에 부른다): 방패 경찰을 여러 방향·공격 동작 중에 찍는다.
/// 밤 장면이라 결함이 보이게 잠깐 검수용 조명을 켰다가 끈다.
/// </summary>
public static class Zone2QA
{
    public static void ShieldShots(string pngPath, string copName = "Cop_Shield_A")
    {
        var cop = GameObject.Find(copName);
        var p = cop.transform.position;
        var lights = new List<GameObject> { QaLight(p + new Vector3(0.6f, 2.4f, -1.4f), 22f), QaLight(p + new Vector3(-1.2f, 2f, 1.2f), 14f) };
        var shots = new List<Texture2D>();
        double until = EditorApplication.timeSinceStartup + 1.5;
        int stage = 0;
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
            if (EditorApplication.timeSinceStartup < until) return;
            Vector3 c = cop.transform.position + Vector3.up * 1.15f, f = cop.transform.forward, r = cop.transform.right;
            if (stage == 0)
            {
                shots.Add(QaShots.Render(c + f * 1.8f + r * 0.4f + Vector3.up * 0.2f, c, 45f, 480, 480)); // 앞
                shots.Add(QaShots.Render(c - r * 1.8f + f * 0.3f, c, 45f, 480, 480));                     // 왼쪽(방패 쪽)
                shots.Add(QaShots.Render(c - f * 1.6f - r * 0.6f + Vector3.up * 0.3f, c, 45f, 480, 480)); // 뒤 왼쪽: 손이 방패 안쪽인가
                shots.Add(QaShots.Render(c + r * 1.8f, c, 45f, 480, 480));                                 // 오른쪽
                cop.GetComponent<EnemyAI>().body.CrossFadeInFixedTime("Cross", 0.05f);                     // 주먹 동작 중
                stage = 1;
                until = EditorApplication.timeSinceStartup + 0.25;
            }
            else
            {
                shots.Add(QaShots.Render(c + f * 1.8f + r * 0.6f, c, 45f, 480, 480));
                shots.Add(QaShots.Render(c - f * 1.6f - r * 0.8f + Vector3.up * 0.3f, c, 45f, 480, 480));
                EditorApplication.update -= cb;
                QaShots.Sheet(pngPath, shots.ToArray(), 3);
                foreach (var l in lights) Object.Destroy(l);
                Debug.Log("[Zone2QA] 방패 사진 " + pngPath);
            }
        };
        EditorApplication.update += cb;
    }

    /// <summary>왼손 손목 회전 후보들을 차례로 넣어 보며 손·방패를 가까이 찍는다(한 줄 = 후보 하나: 오른쪽 뒤·위에서·왼쪽)</summary>
    public static void GripCandidates(string pngPath, Vector3[] eulers, string copName = "Cop_Shield_A")
    {
        var cop = GameObject.Find(copName);
        var guard = cop.GetComponent<ShieldGuard>();
        var hand = cop.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.LeftHand);
        var lights = new List<GameObject> { QaLight(cop.transform.position + new Vector3(0.8f, 2.2f, 0.2f), 18f), QaLight(cop.transform.position + new Vector3(-0.8f, 1.8f, 1.4f), 12f) };
        var shots = new List<Texture2D>();
        int i = 0;
        double until = EditorApplication.timeSinceStartup + 0.8;
        guard.gripEuler = eulers[0];
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
            if (EditorApplication.timeSinceStartup < until) return;
            Vector3 h = hand.position, f = cop.transform.forward, r = cop.transform.right;
            shots.Add(QaShots.Render(h + r * 0.75f - f * 0.35f + Vector3.up * 0.15f, h + f * 0.08f, 40f, 360, 360));
            shots.Add(QaShots.Render(h + Vector3.up * 0.8f - f * 0.2f + r * 0.2f, h + f * 0.08f, 40f, 360, 360));
            shots.Add(QaShots.Render(h - r * 0.9f + f * 0.05f + Vector3.up * 0.1f, h, 40f, 360, 360));
            i++;
            if (i < eulers.Length) { guard.gripEuler = eulers[i]; until = EditorApplication.timeSinceStartup + 0.8; return; }
            EditorApplication.update -= cb;
            QaShots.Sheet(pngPath, shots.ToArray(), 3);
            foreach (var l in lights) Object.Destroy(l);
            Debug.Log("[Zone2QA] 손목 후보 사진 " + pngPath);
        };
        EditorApplication.update += cb;
    }

    /// <summary>
    /// 몰래 제압 시험(플레이 중, 1장): 순찰 하나(앞·뒤·발각 뒤)와 옥상 저격수에게 차례로 다가가 안내가 뜨는지,
    /// Q(키보드 입력을 흉내)로 쓰러지는지, 근처 순찰이 몰려오지 않는지 기록한다.
    /// </summary>
    public static void TakedownTest(string logPath)
    {
        var lines = new List<string>();
        Application.runInBackground = true; // 에디터가 뒤에 가려져도 게임 프레임이 멈추지 않게(멈추면 시험 단계만 앞서 나간다)
        var player = Object.FindAnyObjectByType<PlayerController>();
        EnemyAI Find(string n) => GameObject.Find(n).GetComponent<EnemyAI>();
        var lawn = Find("Thug_Lawn");
        var lawnSight = lawn.GetComponent<PatrolSight>();
        var pavilion = Find("Thug_Pavilion").GetComponent<PatrolSight>();
        var sniper = Find("Cop_Sniper");
        var kb = UnityEngine.InputSystem.Keyboard.current;
        int step = 0;
        float next = Time.unscaledTime + 1.0f;
        float t0 = Time.unscaledTime;
        void Note(string t) { t = $"[{Time.unscaledTime - t0:0.00}s] {t}"; lines.Add(t); Debug.Log("[제압 시험] " + t); }
        void Place(EnemyAI e, float dist, bool behind)
        {
            var ag = e.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (ag && ag.enabled && ag.isOnNavMesh) ag.isStopped = true;
            Vector3 p = e.transform.position + e.transform.forward * (behind ? -dist : dist);
            p.y = e.transform.position.y + 0.02f;
            player.Teleport(p, Quaternion.LookRotation(e.transform.position - p));
        }
        void Press(bool down) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(down ? new[] { UnityEngine.InputSystem.Key.Q } : new UnityEngine.InputSystem.Key[0]));
        // 누가 언제 알아챘는지(발각 이벤트)와 제압 안내가 바뀌는 순간을 모두 적는다
        System.Action<PatrolSight> spotted = s => Note($"  · 발각 이벤트: {s.name} {s.transform.position:F1} (레이 {player.transform.position:F1}, {Vector3.Distance(s.transform.position, player.transform.position):0.0}m, 단계 {step})");
        PatrolSight.Spotted += spotted;
        string lastTarget = "";
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { PatrolSight.Spotted -= spotted; EditorApplication.update -= cb; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            string tn = Takedown.Target ? Takedown.Target.name : "";
            if (tn != lastTarget) { Note($"  · 안내 바뀜: '{lastTarget}' → '{tn}' (단계 {step})"); lastTarget = tn; }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.35f;
            if (step == 0 && StoryState.InputLocked) return; // 장 시작 제목·페이드 동안은 조작이 잠겨 있다
            switch (step++)
            {
                case 0: lawnSight.Holding = true; Place(lawn, 1.2f, true); break;
                case 1:
                    Note($"순찰 등 뒤 1.2m: 안내 {(Takedown.Target ? Takedown.Target.name : "없음")} (기대: Thug_Lawn), 조작 가능 {player.CanAct}, 의심 {lawnSight.Suspicion:0.00}");
                    lawnSight.Holding = false;
                    Press(true);
                    break;
                case 2: Press(false); next = Time.unscaledTime + 1.2f; break;
                case 3:
                    Note($"Q 뒤: 순찰 쓰러짐 {lawn.Health.IsDead}, 발각 {lawnSight.Alerted}, 근처 정자 순찰 발각 {pavilion.Alerted}, 레이 체력 {player.GetComponent<Health>().Current}");
                    Place(pavilion.Enemy, 1.2f, false);
                    next = Time.unscaledTime + 0.1f;
                    break;
                case 4: Note($"정자 순찰 정면 1.2m: 안내 {(Takedown.Target ? Takedown.Target.name : "없음")} (기대: 없음), 정자 순찰 {pavilion.transform.position:F1} 레이 {player.transform.position:F1}"); pavilion.Alert(false); break;
                case 5: Place(pavilion.Enemy, 1.1f, true); next = Time.unscaledTime + 0.1f; break;
                case 6: Note($"발각된 순찰 등 뒤: 안내 {(Takedown.Target ? Takedown.Target.name : "없음")} (기대: 없음)"); pavilion.Enemy.Health.TakeDamage(999, pavilion.transform.position); break;
                case 7:
                    // 옥상 저격수: 조준경을 보는 중(활성)에도 등 뒤에서는 된다
                    player.GetComponent<Health>().Revive();
                    sniper.Activate();
                    Place(sniper, 1.0f, true);
                    break;
                case 8: Note($"저격수 등 뒤(활성): 안내 {(Takedown.Target ? Takedown.Target.name : "없음")} (기대: Cop_Sniper)"); Press(true); break;
                case 9: Press(false); next = Time.unscaledTime + 1.2f; break;
                case 10:
                    Note($"저격수 쓰러짐 {sniper.Health.IsDead}");
                    PatrolSight.Spotted -= spotted;
                    EditorApplication.update -= cb;
                    System.IO.File.WriteAllLines(logPath, lines);
                    break;
            }
        };
        EditorApplication.update += cb;
    }

    /// <summary>
    /// 제압이 끊기는 경우(플레이 중, 1장): ① 누르자마자 되감으면 적이 그대로 살아 있고 알아채지도 않는다
    /// ② 누른 직후 다른 적에게 맞으면 제압이 취소되고 그 적이 알아챈다.
    /// </summary>
    public static void TakedownInterruptTest(string logPath, string targetName = "Thug_Lawn")
    {
        Application.runInBackground = true;
        var lines = new List<string>();
        var player = Object.FindAnyObjectByType<PlayerController>();
        var walkman = player.GetComponent<Walkman>();
        var e = GameObject.Find(targetName).GetComponent<EnemyAI>();
        var sight = e.GetComponent<PatrolSight>();
        var kb = UnityEngine.InputSystem.Keyboard.current;
        int step = 0;
        float next = Time.unscaledTime + 1f, t0 = Time.unscaledTime;
        void Note(string t) { t = $"[{Time.unscaledTime - t0:0.00}s] {t}"; lines.Add(t); Debug.Log("[제압 끊김 시험] " + t); }
        void Press(bool down) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(down ? new[] { UnityEngine.InputSystem.Key.Q } : new UnityEngine.InputSystem.Key[0]));
        void Behind()
        {
            sight.Holding = true;
            Vector3 p = e.transform.position - e.transform.forward * 1.1f;
            p.y = e.transform.position.y + 0.02f;
            player.Teleport(p, Quaternion.LookRotation(e.transform.position - p));
        }
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.4f;
            if (step == 0 && StoryState.InputLocked) return;
            switch (step++)
            {
                case 0: Behind(); next = Time.unscaledTime + 2.5f; break; // 되감을 기록이 쌓이게 잠깐 선다
                case 1: Note($"① 등 뒤: 안내 {(Takedown.Target ? Takedown.Target.name : "없음")}"); sight.Holding = false; Press(true); next = Time.unscaledTime + 0.05f; break;
                case 2: Press(false); Note($"   Q 직후 제압 중 {player.IsBusy}, 되감기 시작 {walkman.RewindSteps(1)}"); break;
                case 3: if (walkman.Rewinding) { step--; break; } next = Time.unscaledTime + 1.2f; break;
                case 4: Note($"   되감은 뒤: 쓰러짐 {e.Health.IsDead} (기대 False), 발각 {sight.Alerted} (기대 False), 레이 제압 중 {player.IsBusy}"); Behind(); break;
                case 5: Note($"② 등 뒤: 안내 {(Takedown.Target ? Takedown.Target.name : "없음")}"); sight.Holding = false; Press(true); next = Time.unscaledTime + 0.05f; break;
                case 6:
                    Press(false);
                    player.GetComponent<Health>().TakeDamage(5, player.transform.position + player.transform.forward * 2f);
                    Note($"   Q 직후 맞음: 레이 제압 중 {player.IsBusy}");
                    next = Time.unscaledTime + 1.2f;
                    break;
                case 7:
                    Note($"   맞은 뒤: 쓰러짐 {e.Health.IsDead} (기대 False), 발각 {sight.Alerted} (기대 True)");
                    EditorApplication.update -= cb;
                    System.IO.File.WriteAllLines(logPath, lines);
                    break;
            }
        };
        EditorApplication.update += cb;
    }

    /// <summary>몰래 제압 동작을 옆에서 시간 순서대로 찍는다(붙기 → 주먹 → 쓰러짐)</summary>
    public static void TakedownShots(string pngPath, string targetName = "Cop_Torch")
    {
        Application.runInBackground = true;
        var player = Object.FindAnyObjectByType<PlayerController>();
        var e = GameObject.Find(targetName).GetComponent<EnemyAI>();
        var sight = e.GetComponent<PatrolSight>();
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var shots = new List<Texture2D>();
        float[] at = { 0f, 0.12f, 0.24f, 0.34f, 0.6f, 1.4f };
        int stage = -1, shot = 0;
        float next = Time.unscaledTime + 1f, pressedAt = 0f;
        Vector3 side = Vector3.zero, centre = Vector3.zero;
        GameObject light = null;
        void Press(bool down) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(down ? new[] { UnityEngine.InputSystem.Key.Q } : new UnityEngine.InputSystem.Key[0]));
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            if (Time.unscaledTime < next) return;
            if (stage == -1)
            {
                if (StoryState.InputLocked) return;
                if (sight) sight.Holding = true;
                Vector3 p = e.transform.position - e.transform.forward * 1.1f;
                p.y = e.transform.position.y + 0.02f;
                player.Teleport(p, Quaternion.LookRotation(e.transform.position - p));
                centre = e.transform.position + Vector3.up * 1.1f - e.transform.forward * 0.45f;
                side = centre + e.transform.right * 3.2f + Vector3.up * 0.4f;
                light = QaLight(centre + Vector3.up * 1.8f + e.transform.right * 1.2f, 16f);
                stage = 0; next = Time.unscaledTime + 0.4f;
                return;
            }
            if (stage == 0)
            {
                shots.Add(QaShots.Render(side, centre, 45f, 420, 420)); // 붙기 전
                if (sight) sight.Holding = false;
                Press(true);
                stage = 1; pressedAt = Time.unscaledTime; next = Time.unscaledTime + 0.05f;
                return;
            }
            if (stage == 1) { Press(false); stage = 2; }
            shot++;
            if (shot < at.Length)
            {
                if (Time.unscaledTime - pressedAt < at[shot]) { next = pressedAt + at[shot]; shot--; return; }
                shots.Add(QaShots.Render(side, centre, 45f, 420, 420));
                return;
            }
            EditorApplication.update -= cb;
            QaShots.Sheet(pngPath, shots.ToArray(), 3);
            if (light) Object.Destroy(light);
            Debug.Log($"[Zone2QA] 제압 사진 {pngPath} (쓰러짐 {e.Health.IsDead})");
        };
        EditorApplication.update += cb;
    }

    static GameObject QaLight(Vector3 at, float intensity)
    {
        var go = new GameObject("QALight");
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 6f;
        l.intensity = intensity;
        go.transform.position = at;
        return go;
    }
}
