using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 타격감 시험(플레이 중 프롤로그에서 시작). 레이가 조직원을 잽 → 크로스 → 돌려차기로 치고(실제 콤보 코드),
/// 마지막에는 체력을 낮춰 마무리 한 방(느린 화면)을 만든다. 끝으로 조직원 주먹에 레이가 맞는다.
/// 매 프레임 시간 배율·카메라 시야각·흔들림·효과 입자 수·재생 중인 소리를 기록하고, 맞는 순간과 직후 화면을 찍는다.
/// </summary>
public static class HitFeelTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string log, dir;
    static readonly List<string> lines = new();
    static int step, shot, lastHp, lastPlayerHp;
    static float since, hitAt = -1f, minScale = 1f, minFov = 999f, baseFov;
    static PlayerController player;
    static EnemyAI thug;
    static Camera cam;
    static readonly HashSet<string> taken = new();
    static string label = "", hitLabel = "";

    public static void Begin(string logPath, string shotDir)
    {
        if (!EditorApplication.isPlaying || !Object.FindAnyObjectByType<PrologueDirector>()) throw new System.InvalidOperationException("플레이 중인 프롤로그 장면에서만 시작할 수 있다");
        log = logPath; dir = shotDir;
        System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); taken.Clear(); step = 0; shot = 0; hitAt = -1f;
        Application.runInBackground = true;
        var d = Object.FindAnyObjectByType<PrologueDirector>();
        d.StopAllCoroutines(); d.enabled = false;
        typeof(PrologueDirector).GetMethod("Shot", Any).Invoke(d, new object[] { d.gameplayCamera, 0f }); // 오프닝 컷신 카메라 → 게임 카메라
        StoryState.CutsceneActive = false; StoryState.ControlLocked = false; StoryState.CombatLocked = false;
        StoryState.Fade = 0f; StoryState.Title = null; StoryState.ClearLine();
        player = d.player;
        thug = d.roomThugs[0];
        d.roomThugs[1].gameObject.SetActive(false);
        if (d.dannyBody) d.dannyBody.gameObject.SetActive(false);
        thug.Health.SetMax(500, true);
        player.Teleport(new Vector3(103.0f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
        thug.Deactivate();
        thug.PlaceAt(new Vector3(104.6f, 0f, 67.8f), Quaternion.LookRotation(Vector3.left)); // 1.6m: 달라붙기 시험
        var key = new GameObject("HitTestKeyLight").AddComponent<Light>();
        key.type = LightType.Point; key.range = 8f; key.intensity = 2.5f; key.transform.position = new Vector3(104.5f, 2.6f, 70f);
        cam = Camera.main;
        var rig = Object.FindAnyObjectByType<ThirdPersonCamera>();
        rig.SnapBehindTarget(10f);
        // 옆에서 보도록 카메라 방향을 돌린다(뒤따르는 카메라 그대로 쓰되 시선만 옆)
        typeof(ThirdPersonCamera).GetField("yaw", Any).SetValue(rig, 180f); StoryState.CameraDistance = 3.4f; // 방 안쪽(+z)에서 두 사람을 옆으로 본다
        lastHp = thug.Health.Current; lastPlayerHp = player.GetComponent<Health>().Current;
        since = Time.realtimeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시작: 레이와 조직원 거리 1.6m");
    }

    static void Note(string t) { lines.Add($"[{Time.realtimeSinceStartup - since:00.000}] {t}"); System.IO.File.WriteAllLines(log, lines); }
    static T Get<T>(object o, string f) => (T)o.GetType().GetField(f, Any).GetValue(o);
    static void Shot(string n) { if (!taken.Add(n)) return; ScreenCapture.CaptureScreenshot($"{dir}/{shot++:00}_{n}.png"); Note("사진 " + n); }

    static int Particles()
    {
        var hf = GameObject.Find("HitFeel");
        if (!hf) return 0;
        int n = 0; foreach (var ps in hf.GetComponentsInChildren<ParticleSystem>()) n += ps.particleCount;
        return n;
    }

    static string Sounds()
    {
        var names = new List<string>();
        foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (s.isPlaying && s.gameObject.name.StartsWith("Sfx_")) names.Add(s.clip.name + "(음높이 " + s.pitch.ToString("0.00") + ")");
        return string.Join(", ", names);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        var vcam = cam ? Object.FindAnyObjectByType<ThirdPersonCamera>().GetComponent<Unity.Cinemachine.CinemachineCamera>() : null;
        float fov = vcam ? vcam.Lens.FieldOfView : 0f;
        if (baseFov == 0f) baseFov = fov;
        minScale = Mathf.Min(minScale, Time.timeScale);
        minFov = Mathf.Min(minFov, fov);
        int combo = Get<int>(player, "comboIndex");
        float timer = Get<float>(player, "stateTimer");
        string state = Get<object>(player, "state").ToString();

        if (thug.Health.Current != lastHp)
        {
            var gap = Vector3.Distance(new Vector3(player.transform.position.x, 0, player.transform.position.z), new Vector3(thug.transform.position.x, 0, thug.transform.position.z));
            Note($"[{label}] 명중: 피해 {lastHp - thug.Health.Current}, 공격 {combo + 1}타 {timer:0.000}초, 그 순간 두 사람 거리 {gap:0.00}m, 시간 배율 {Time.timeScale:0.00}");
            lastHp = thug.Health.Current; hitAt = Time.realtimeSinceStartup; minScale = Time.timeScale; minFov = fov; hitLabel = label;
        }
        int php = player.GetComponent<Health>().Current;
        if (php != lastPlayerHp) { Note($"[레이 맞음] 피해 {lastPlayerHp - php}, 시간 배율 {Time.timeScale:0.00}"); lastPlayerHp = php; hitAt = Time.realtimeSinceStartup; minScale = Time.timeScale; minFov = fov; hitLabel = "레이 맞음"; }

        if (hitAt > 0f)
        {
            float e = Time.realtimeSinceStartup - hitAt;
            if (e > 0.02f && !taken.Contains(hitLabel + "_impact")) { Shot(hitLabel + "_impact"); Note($"   맞은 직후: 입자 {Particles()}개, 소리 [{Sounds()}]"); }
            if (e > 0.12f && !taken.Contains(hitLabel + "_after")) Shot(hitLabel + "_after");
            if (e > 0.6f) { Note($"   맞은 뒤 0.6초 동안: 최저 시간 배율 {minScale:0.00}, 최저 시야각 {minFov:0.0} (평소 {baseFov:0.0}), 지금 배율 {Time.timeScale:0.00}"); hitAt = -1f; }
        }

        switch (step)
        {
            case 0: if (Time.realtimeSinceStartup - since > 1f) { label = "잽"; typeof(PlayerController).GetMethod("BeginAttack", Any).Invoke(player, new object[] { 0, Vector3.zero }); step++; } break;
            case 1:
                if (combo == 0 && timer > 0.2f) typeof(PlayerController).GetField("attackQueued", Any).SetValue(player, true);
                if (combo == 1) { label = "크로스"; step++; }
                break;
            case 2:
                if (timer > 0.2f) typeof(PlayerController).GetField("attackQueued", Any).SetValue(player, true);
                if (combo == 2) { label = "돌려차기"; step++; }
                break;
            case 3: if (state == "Move" && hitAt < 0f) { step++; since2 = Time.realtimeSinceStartup; } break;
            case 4: // 마무리: 체력을 낮추고 다시 돌려차기까지
                if (Time.realtimeSinceStartup - since2 > 0.8f)
                {
                    thug.Health.SetMax(500, true);
                    player.Teleport(new Vector3(103.0f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
                    thug.Deactivate(); thug.PlaceAt(new Vector3(104.2f, 0f, 67.8f), Quaternion.LookRotation(Vector3.left));
                    lastHp = thug.Health.Current;
                    typeof(PlayerController).GetMethod("BeginAttack", Any).Invoke(player, new object[] { 2, Vector3.zero });
                    thug.Health.SetMax(20, true); lastHp = 20;
                    label = "마무리"; step++;
                }
                break;
            case 5: if (thug.Health.IsDead && hitAt < 0f && Time.realtimeSinceStartup - since2 > 0f) { since2 = Time.realtimeSinceStartup; step = 50; } break;
            case 50: if (Time.realtimeSinceStartup - since2 > 0.8f) { Note($"마무리 뒤 시간 배율 {Time.timeScale:0.00} (1이어야 함), 조직원 래그돌 {thug.GetComponentInChildren<Ragdoll>().IsActive}"); step = 6; since2 = Time.realtimeSinceStartup; } break;
            case 6: // 다른 조직원이 레이를 때린다
                if (Time.realtimeSinceStartup - since2 > 1f)
                {
                    var other = Object.FindAnyObjectByType<PrologueDirector>().roomThugs[1];
                    other.gameObject.SetActive(true);
                    other.ranged = false; other.damage = 5; other.windup = 0.5f; other.attackRange = 1.5f;
                    player.Teleport(new Vector3(103.0f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
                    other.PlaceAt(new Vector3(104.2f, 0f, 67.8f), Quaternion.LookRotation(Vector3.left));
                    other.Activate();
                    step++; since2 = Time.realtimeSinceStartup;
                }
                break;
            case 7: if (taken.Contains("레이 맞음_after") && hitAt < 0f || Time.realtimeSinceStartup - since2 > 8f) { Note("시험 끝"); EditorApplication.update -= Tick; } break;
        }
    }

    static float since2;
}
