using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 격투 동작 실제 시간 시험(플레이 중 프롤로그 장면에서 시작한다).
/// 레이가 조직원 앞에서 잽 → 크로스 → 돌려차기를 실제 콤보 코드로 이어 치고(버튼 연타와 같게 다음 공격을 예약),
/// 공격마다 '맞히는 순간'과 맞은 뒤의 화면을 옆에서 찍는다. 조직원 체력이 언제 줄었는지(공격 시작 뒤 몇 초)도 기록한다.
/// 마지막으로 조직원이 레이를 때리게 해서, 피해가 들어가는 순간에 주먹이 뻗어 있는지 찍는다.
/// 시간은 0.5배로 느리게 돌려 찍는 순간을 정확히 잡는다(기록하는 시각은 게임 시간).
/// </summary>
public static class CombatVisualTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static string log, dir;
    static readonly List<string> lines = new();
    static int step, shot, lastThugHp, lastPlayerHp;
    static float stepTime, impactAt = -1f;
    static PrologueDirector d;
    static PlayerController player;
    static EnemyAI thug;
    static readonly HashSet<string> taken = new();

    public static void Begin(string logPath, string shotDir)
    {
        // 편집 모드에서 부르면 시험용 변경(감독 끄기 등)이 장면에 그대로 남는다 → 플레이 중에만
        if (!EditorApplication.isPlaying || !Object.FindAnyObjectByType<PrologueDirector>()) throw new System.InvalidOperationException("플레이 중인 프롤로그 장면에서만 시작할 수 있다");
        log = logPath; dir = shotDir;
        System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); taken.Clear();
        step = 0; shot = 0; impactAt = -1f;
        Application.runInBackground = true;
        d = Object.FindAnyObjectByType<PrologueDirector>();
        d.StopAllCoroutines(); d.enabled = false;
        StoryState.CutsceneActive = false; StoryState.ControlLocked = false; StoryState.CombatLocked = false;
        StoryState.Fade = 0f; StoryState.Title = null; StoryState.ClearLine();
        player = d.player;
        thug = d.roomThugs[0];
        d.roomThugs[1].gameObject.SetActive(false);
        if (d.dannyBody) d.dannyBody.gameObject.SetActive(false);
        thug.Health.SetMax(500, true);
        PlaceForCombo();
        // 화면: 시네머신을 끄고 옆에서 본다
        var brain = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineBrain>();
        if (brain) brain.enabled = false;
        var cam = Camera.main.transform;
        cam.SetPositionAndRotation(new Vector3(103.75f, 1.35f, 71.2f), Quaternion.LookRotation(new Vector3(103.75f, 1.05f, 67.8f) - new Vector3(103.75f, 1.35f, 71.2f)));
        Camera.main.fieldOfView = 42f;
        var key = new GameObject("CombatTestKeyLight").AddComponent<Light>();
        key.type = LightType.Point; key.range = 8f; key.intensity = 2.5f; key.transform.position = new Vector3(104.5f, 2.6f, 70f);
        Time.timeScale = 0.5f;
        lastThugHp = thug.Health.Current; lastPlayerHp = player.GetComponent<Health>().Current;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시작");
    }

    static void PlaceForCombo()
    {
        player.Teleport(new Vector3(103.2f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
        thug.Deactivate();
        thug.PlaceAt(new Vector3(104.3f, 0f, 67.8f), Quaternion.LookRotation(Vector3.left));
    }

    static void Note(string text)
    {
        lines.Add($"[게임 {Time.time:000.00}s] {text}");
        System.IO.File.WriteAllLines(log, lines);
    }

    static void Shot(string name)
    {
        if (!taken.Add(name)) return;
        // 두 사람 가운데를 옆에서 본다(밀려나도 화면에 남게)
        var mid = (step >= 7 ? player.transform.position : (player.transform.position + thug.transform.position) / 2f) + Vector3.up * 0.9f; // 구르기는 레이만 따라간다
        Camera.main.transform.SetPositionAndRotation(mid + new Vector3(0f, 0.3f, 3.4f), Quaternion.LookRotation(-new Vector3(0f, 0.3f, 3.4f)));
        ScreenCapture.CaptureScreenshot($"{dir}/{shot++:00}_{name}.png");
        var pa = player.animator;
        var chest = pa.GetBoneTransform(HumanBodyBones.Chest).position;
        float fist = Mathf.Max(pa.GetBoneTransform(HumanBodyBones.LeftHand).position.x, pa.GetBoneTransform(HumanBodyBones.RightHand).position.x) - chest.x;
        float foot = Mathf.Max(pa.GetBoneTransform(HumanBodyBones.LeftFoot).position.y, pa.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
        float gap = Vector3.Distance(new Vector3(player.transform.position.x, 0, player.transform.position.z), new Vector3(thug.transform.position.x, 0, thug.transform.position.z));
        Note($"사진 {name} | 레이 주먹 앞뻗음 {fist:0.00}m 발 높이 {foot:0.00}m | 조직원 HP {thug.Health.Current} | 두 사람 거리 {gap:0.00}m");
    }

    static float rollAt;
    static Vector3 rollStart;

    static void RollShots(string tag)
    {
        float e = Time.time - rollAt;
        foreach (var t in new[] { 0.1f, 0.25f, 0.4f, 0.55f, 0.75f })
            if (e >= t && !taken.Contains($"{tag}_{t:0.00}"))
            {
                Note($"{tag} {t:0.00}초: 무적 {player.GetComponent<Health>().Invulnerable}, 상태 {Get<object>(player, "state")}, 엉덩이 높이 {player.animator.GetBoneTransform(HumanBodyBones.Hips).position.y:0.00}m");
                Shot($"{tag}_{t:0.00}");
            }
    }

    static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, Any).GetValue(o);
    static void Set(object o, string field, object v) => o.GetType().GetField(field, Any).SetValue(o, v);

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        string state = Get<object>(player, "state").ToString();
        int combo = Get<int>(player, "comboIndex");
        float timer = Get<float>(player, "stateTimer");
        if (thug.Health.Current != lastThugHp)
        {
            Note($"조직원 피해 {lastThugHp - thug.Health.Current} — 레이 공격 {combo + 1}타, 공격 시작 뒤 {timer:0.000}초");
            lastThugHp = thug.Health.Current;
        }
        if (player.GetComponent<Health>().Current != lastPlayerHp)
        {
            var ta = thug.GetComponentInChildren<Animator>();
            var chest = ta.GetBoneTransform(HumanBodyBones.Chest).position;
            float fist = Mathf.Max(-(ta.GetBoneTransform(HumanBodyBones.LeftHand).position.x - chest.x), -(ta.GetBoneTransform(HumanBodyBones.RightHand).position.x - chest.x));
            Note($"레이 피해 {lastPlayerHp - player.GetComponent<Health>().Current} — 그 순간 조직원 주먹 앞뻗음 {fist:0.00}m");
            Shot("thug_punch_impact");
            lastPlayerHp = player.GetComponent<Health>().Current;
        }

        switch (step)
        {
            case 0:
                typeof(PlayerController).GetMethod("BeginAttack", Any).Invoke(player, new object[] { 0, Vector3.zero });
                Note("잽 시작"); step++; break;
            case 1: // 잽
                if (combo == 0 && timer >= 0.10f) Shot("jab_0.10");
                if (combo == 0 && timer >= 0.25f) Shot("jab_hit_0.25");
                if (combo == 0 && timer >= 0.20f) Set(player, "attackQueued", true); // 연타
                if (combo == 1) { Note("크로스로 이어짐"); step++; }
                break;
            case 2: // 크로스
                if (timer >= 0.27f) Shot("cross_hit_0.27");
                if (timer >= 0.20f) Set(player, "attackQueued", true);
                if (timer >= 0.38f) Shot("cross_after_0.38_thug_reaction");
                if (combo == 2) { Note("돌려차기로 이어짐"); step++; }
                break;
            case 3: // 돌려차기
                if (timer >= 0.25f) Shot("kick_0.25");
                if (timer >= 0.45f) Shot("kick_hit_0.45");
                if (timer >= 0.60f) Shot("kick_after_0.60_thug_reaction");
                if (timer >= 0.80f) Shot("kick_after_0.80_thug_reaction");
                if (state == "Move") { Note("콤보 끝 → 걷기"); step++; stepTime = Time.time; }
                break;
            case 4: // 조직원이 레이를 때린다
                if (Time.time - stepTime > 0.5f)
                {
                    player.Teleport(new Vector3(103.2f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
                    thug.PlaceAt(new Vector3(104.4f, 0f, 67.8f), Quaternion.LookRotation(Vector3.left));
                    thug.damage = 10; thug.windup = 0.6f; thug.attackRange = 1.5f;
                    thug.Activate();
                    Note("조직원 공격 시작"); step++; stepTime = Time.time;
                }
                break;
            case 5:
                if (taken.Contains("thug_punch_impact") && impactAt < 0f) impactAt = Time.time;
                if (impactAt > 0f && Time.time - impactAt > 0.25f) { Shot("ray_hit_reaction_0.25"); step++; stepTime = Time.time; }
                if (Time.time - stepTime > 8f) { Note("조직원이 때리지 않음(시간 초과)"); step = 7; }
                break;
            case 6:
                if (Time.time - stepTime > 0.35f) { Shot("ray_hit_reaction_later"); step++; }
                break;
            case 7: // 구르기: 방향을 넣고(화면 기준 오른쪽 = -x) 회피
                thug.Deactivate();
                thug.PlaceAt(new Vector3(106f, 0f, 70.5f), Quaternion.LookRotation(Vector3.left));
                player.Teleport(new Vector3(104.5f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
                rollStart = player.transform.position;
                typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(player, new object[] { Vector3.left });
                rollAt = Time.time; Note("구르기(방향 있음) 시작"); step++;
                break;
            case 8:
                RollShots("roll_dir");
                if (Time.time - rollAt > 1.0f) { Note($"구르기(방향 있음) 끝: 이동 {Vector3.Distance(rollStart, player.transform.position):0.00}m, 바라보는 방향 {player.transform.forward:0.00}"); step++; }
                break;
            case 9: // 방향 없이 회피 → 뒤로 돌아서 구른다
                player.Teleport(new Vector3(102.5f, 0f, 67.8f), Quaternion.LookRotation(Vector3.right));
                rollStart = player.transform.position;
                typeof(PlayerController).GetMethod("BeginDodge", Any).Invoke(player, new object[] { Vector3.zero });
                rollAt = Time.time; Note("구르기(방향 없음) 시작"); step++;
                break;
            case 10:
                RollShots("roll_back");
                if (Time.time - rollAt > 1.0f) { Note($"구르기(방향 없음) 끝: 이동 {Vector3.Distance(rollStart, player.transform.position):0.00}m (뒤쪽 -x로), 끝 위치 x {player.transform.position.x:0.00}"); step++; }
                break;
            case 11:
                Time.timeScale = 1f;
                Note("시험 끝");
                EditorApplication.update -= Tick;
                break;
        }
    }
}
