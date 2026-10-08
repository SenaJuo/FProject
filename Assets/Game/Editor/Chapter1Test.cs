using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 1장 구역 1·2 시험(플레이 중인 Chapter1 장면에서 시작). 실제 키 대신 순간 이동과 되감기 함수로 진행하며 단계마다 기록·사진을 남긴다.
///  1) 시작 상태: 믹스테이프(재생 불가, 1회), 구역 시작점 저장, 제목
///  2) 입구 통로에서 손전등에 걸림 → 시간이 붙잡히고(R 안내) → 되감기 → 5초 전 자리, 조직원은 건물 뒤에서 순찰 시작, 믹스테이프는 다 쓴 테이프
///  3) 생울타리 뒤에 숨으면 순찰에게 보이지 않는다 / 앞에 서면 의심이 차서 들킨다
///  4) 통화 엿듣기 → 번호판 한마디 → 상표 없는 테이프 줍기
///  5) 들킨 뒤 되감으면 발각이 없던 일이 된다
///  6) 출구 싸움(저장 지점은 그대로) → 큰길 목표
///  7) 가게: 다가가면 저장 · 교환(다 쓴 믹스테이프 → 재생용) · 주인 대사 · 설명
///  8) 되감을 횟수 없이 쓰러지면 가게에서 다시 시작, 순찰은 처음 상태
///  9) 큰길 북쪽 끝 → 검문소 컷신(검은 승합차가 통과) → 구역 2 시작점 저장
/// 10) 방패 경찰: 정면 가벼운 주먹은 막힘, 돌려차기(20)는 방패를 쳐냄, 옆에서는 맞음
/// 11) 광장 진입 → 저격수 소개(느린 화면) → 트인 곳에서 조준선·피격, 트럭 뒤에서는 맞지 않음
/// 12) 옥상: 경비 싸움 → 저격수 제압 → 심문 → 항구 길(가게 2 저장) → 철조망 문(F) → 3번 부두 → (이어서 계속)
/// </summary>
public static class Chapter1Test
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string log, dir;
    static readonly List<string> lines = new();
    static int step, shot;
    static float since;
    static Chapter1Director d;
    static Walkman w;
    static PatrolSight lawn;
    static Transform[] lawnRoute;
    static Vector3 beforeSpot;
    static string lastLine;
    static EnemyAI cop;
    static int hpBefore;
    static bool laserShot;

    public static void Begin(string logPath, string shotDir)
    {
        if (!EditorApplication.isPlaying || !Object.FindAnyObjectByType<Chapter1Director>()) throw new System.InvalidOperationException("플레이 중인 1장 장면에서만 시작할 수 있다");
        log = logPath; dir = shotDir;
        System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shot = 0; lastLine = null;
        since = Time.realtimeSinceStartup;
        Application.runInBackground = true;
        d = Object.FindAnyObjectByType<Chapter1Director>();
        w = d.player.GetComponent<Walkman>();
        foreach (var p in PatrolSight.All) if (p.name == "Thug_Lawn") lawn = p;
        lawnRoute = lawn.route;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시작");
    }

    static void Note(string t)
    {
        lines.Add($"[{Time.realtimeSinceStartup:0000.0}] {(d ? d.CurrentPhase.ToString() : "-")} | {t}");
        System.IO.File.WriteAllLines(log, lines);
    }

    static void Use(StoryInteractable it)
    {
        if (it.oneShot) it.enabled = false; // 실제로 F를 눌렀을 때처럼 한 번 쓰면 꺼진다
        var del = (System.Action)typeof(StoryInteractable).GetField("Used", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(it);
        del?.Invoke();
    }

    static bool Waited(float s) => Time.realtimeSinceStartup - since > s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Shot(string n) { ScreenCapture.CaptureScreenshot($"{dir}/{shot++:00}_{n}.png"); Note("사진 " + n); }
    static void End(string why) { Note("시험 끝: " + why); EditorApplication.update -= Tick; }
    static void Tp(Vector3 p, float yaw)
    {
        d.player.Teleport(p, Quaternion.Euler(0f, yaw, 0f));
        var rig = Object.FindAnyObjectByType<ThirdPersonCamera>();
        if (rig) { typeof(ThirdPersonCamera).GetField("yaw", Any)?.SetValue(rig, yaw); rig.SnapBehindTarget(); } // 순간 이동 직후 카메라가 엉뚱한 곳에 남지 않게
    }
    static string Tapes() => string.Join(" / ", w.tapes.ConvertAll(t => $"{t.title}(횟수 {t.charges}, 재생 {(t.CanPlay ? "가능" : "불가")}, 다 씀 {t.IsSpent})"));
    static Vector3 Checkpoint() => (Vector3)typeof(GameManager).GetField("checkpointPosition", Any).GetValue(GameManager.Instance);

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        if (!d) return;
        if (StoryState.LineVisible && StoryState.Line != lastLine) { lastLine = StoryState.Line; Note($"자막 [{StoryState.Speaker}] {StoryState.Line}"); }
        var p = d.player.transform;
        switch (step)
        {
            case 0:
                if (Waited(0.6f)) { Note($"제목 '{StoryState.Title}', 테이프 {Tapes()}, 저장 지점 {Checkpoint()} (시작점 {d.zoneStart.position})"); Next(); }
                break;
            case 1:
                if (!StoryState.ControlLocked && Waited(0.5f)) { Note($"조작 가능, 목표 '{StoryState.Objective}'"); Shot("start"); Tp(new Vector3(104f, 0.15f, 30.2f), 0f); Next(); }
                break;
            case 2:
                // 되감을 기록(5초)이 쌓이게 기다렸다가 통로로
                if (Waited(6f)) { beforeSpot = p.position; Tp(new Vector3(104f, 0.15f, 36.6f), 0f); Note($"통로로 이동(되감기 기록 {TimeRewind.RecordedSeconds:0.0}초)"); Next(); }
                break;
            case 3:
                if (d.CurrentPhase == Chapter1Director.Phase.ForcedRewind && StoryState.Prompt != null)
                {
                    Note($"걸림: 시간 배율 {Time.timeScale:0.00}, 시간 붙잡음 {StoryState.HoldTime}, 안내 '{StoryState.Prompt}', 목표 '{StoryState.Objective}', 조직원 위치 {d.spotter.transform.position}, 표시 {d.spotter.Staged}");
                    Shot("spotted"); Next();
                }
                else if (Waited(6f)) End("손전등에 걸리지 않음");
                break;
            case 4:
                if (Waited(2.5f)) // 일부러 기다린다: 기다린 시간이 되감기 기록에 들어가면 안 된다
                {
                    Note($"2.5초 기다린 뒤 기록 {TimeRewind.RecordedSeconds:0.0}초");
                    Note($"되감기 시작 {w.RewindSteps(1)}"); Next();
                }
                break;
            case 5:
                if (!w.Rewinding && Waited(0.6f))
                {
                    float off = Vector3.Distance(p.position, beforeSpot);
                    Note($"되감은 뒤: 레이 위치 {p.position} (5초 전 자리와 {off:0.00}m), 시간 배율 {Time.timeScale:0.00}, 붙잡음 {StoryState.HoldTime}, 안내 '{StoryState.Prompt}'");
                    Note($"조직원: 위치 {d.spotter.transform.position}, 붙잡힘 {d.spotter.Holding}, 감지 {d.spotter.CanDetect}, 발각 {d.spotter.Alerted}, 표시 {d.spotter.Staged}");
                    Note($"테이프 {Tapes()}, 설명 알림 {TutorialUI.ToastId}");
                    Shot("after_rewind"); Next();
                }
                break;
            case 6:
                if (Waited(4f)) { Note($"4초 뒤 조직원 위치 {d.spotter.transform.position} (순찰 중), 의심 {d.spotter.Suspicion:0.00}, 설명 알림 {TutorialUI.ToastId}"); Shot("stealth_toast"); Next(); }
                break;
            case 7:
                // 숨기: 잔디밭 순찰 앞 생울타리 뒤. 순찰을 제자리에 세우고 레이를 울타리 반대편에 둔다
                lawn.Enemy.PlaceAt(new Vector3(113f, 0.15f, 45.6f), Quaternion.LookRotation(Vector3.left));
                lawn.route = new Transform[0]; // 시험 동안 제자리에서 둘러보게(감지는 그대로)
                Tp(new Vector3(106.6f, 0.15f, 45.6f), 90f); // 사이에 x 108.6 생울타리
                Note("숨기 시험: 레이와 순찰 사이에 생울타리"); Next();
                break;
            case 8:
                if (Waited(2.5f)) { Note($"생울타리 뒤 2.5초: 의심 {lawn.Suspicion:0.00}, 발각 {lawn.Alerted}"); Next(); }
                break;
            case 9:
                // 엿듣기: 산책길 북쪽 끝(차 뒤쪽)으로
                lawn.route = lawnRoute;
                Tp(new Vector3(106.2f, 0.15f, 55.8f), 0f); Note("통화 엿듣는 자리로"); Next();
                break;
            case 10:
                if (Waited(16f)) { Note($"통화 뒤: 통화하던 조직원 붙잡힘 {d.caller.Holding}, 발각 {d.caller.Alerted}"); Shot("call"); Next(); }
                break;
            case 11:
                Tp(new Vector3(104.4f, 0.15f, 60.2f), 270f); Note("번호판 앞으로"); Next();
                break;
            case 12:
                if (Waited(4.5f)) { Tp(d.vanTape.transform.position + Vector3.down * 0.3f, 0f); Note("테이프 상자로"); Next(); }
                break;
            case 13:
                if (Waited(4f)) { Note($"테이프 주운 뒤 {Tapes()}, 상자 위 테이프 보임 {d.vanTape.gameObject.activeSelf}, 회복약 {w.potions}개"); Next(); }
                break;
            case 14:
                // 들키기: 잔디밭 순찰 바로 앞
                lawn.route = new Transform[0];
                lawn.Enemy.PlaceAt(new Vector3(120f, 0.15f, 50f), Quaternion.LookRotation(Vector3.back));
                Tp(new Vector3(120f, 0.15f, 45.5f), 0f);
                Note("들키기 시험: 순찰 정면 4.5m"); Next();
                break;
            case 15:
                if (lawn.Alerted) { Note($"들킴: {Time.realtimeSinceStartup - since:0.00}초, 적 활성 {lawn.Enemy.IsActive}, 함께 몰려온 순찰 {PatrolSight.All.FindAll(x => x.Alerted).Count}명"); Shot("alerted"); Next(); }
                else if (Waited(4f)) { Note($"4초 지나도 안 들킴(의심 {lawn.Suspicion:0.00})"); Next(); }
                break;
            case 16:
                if (Waited(0.8f)) { Note($"되감기 시작 {w.RewindSteps(1)}"); Next(); }
                break;
            case 17:
                if (!w.Rewinding && Waited(0.6f)) { Note($"되감은 뒤 순찰: 발각 {lawn.Alerted}, 적 활성 {lawn.Enemy.IsActive}, 의심 {lawn.Suspicion:0.00}, 테이프 {Tapes()}"); Next(); }
                break;
            case 18:
                lawn.route = lawnRoute;
                Tp(new Vector3(148f, 0.15f, 60f), 90f); Note("출구 싸움 구역으로(출구 틈)"); Next();
                break;
            case 19:
                if (d.CurrentPhase == Chapter1Director.Phase.ExitFight && Waited(1.5f))
                {
                    Note($"출구 싸움 시작: 적 {d.exitZone.Remaining}명, 저장 지점 {Checkpoint()} (바뀌면 안 됨)"); Shot("exit_fight");
                    foreach (var e in d.exitZone.enemies) e.Health.TakeDamage(999, p.position);
                    Next();
                }
                else if (Waited(5f)) End("출구 싸움이 시작되지 않음");
                break;
            case 20:
                if (d.CurrentPhase == Chapter1Director.Phase.Avenue && Waited(1.5f)) { Note($"싸움 끝: 목표 '{StoryState.Objective}'"); Next(); }
                else if (Waited(6f)) End("싸움 뒤 큰길 단계로 넘어가지 않음");
                break;
            case 21:
                Tp(new Vector3(171.6f, 0.15f, 63f), 90f); Note("가게 앞으로"); Next();
                break;
            case 22:
                if (Waited(1.5f)) { Note($"가게 앞: 저장 지점 {Checkpoint()}, 저장 표시 {StoryState.Now - GameManager.Instance.SavedAt < 2.5f}, 체력 {d.player.GetComponent<Health>().Current}/{d.player.GetComponent<Health>().Max} (저장 때 회복 {GameManager.Instance.LastSaveHealed}), 설명 {TutorialUI.ToastId}"); Shot("shop_front"); Next(); }
                break;
            case 23:
                Tp(new Vector3(171.6f, 0.15f, 66.4f), 90f); Next();
                break;
            case 24:
                if (Waited(0.8f))
                {
                    var shop = d.shop;
                    Note($"가게 근처 {TapeShop.Nearby == shop}");
                    typeof(TapeShop).GetMethod("Open", Any).Invoke(shop, null);
                    Note($"가게 열림 {TapeShop.Current == shop}, 시간 배율 {Time.timeScale}");
                    Shot("shop_menu");
                    typeof(TapeShop).GetMethod("Trade", Any).Invoke(shop, new object[] { shop.stock[0] });
                    Note($"교환 뒤 {Tapes()}, 끼운 테이프 '{w.Inserted?.title}'");
                    Next();
                }
                break;
            case 25:
                if (Waited(1.5f)) { typeof(TapeShop).GetMethod("Close", Any).Invoke(d.shop, null); Note($"가게 닫음, 시간 배율 {Time.timeScale}"); Next(); }
                break;
            case 26:
                if (Waited(3f)) { Note($"재생 중: '{w.Inserted?.title}' 위치 {w.Inserted?.position:0.0}초, 재생 가능 {w.Inserted?.CanPlay}"); Next(); }
                break;
            case 27:
                // 되감을 횟수 없이 쓰러지기 → 가게에서 다시 시작
                foreach (var t in w.tapes) t.charges = 0;
                lawn.route = new Transform[0];
                lawn.Enemy.PlaceAt(new Vector3(120f, 0.15f, 50f), Quaternion.LookRotation(Vector3.back));
                Tp(new Vector3(120f, 0.15f, 45.5f), 0f);
                Next();
                break;
            case 28:
                if (lawn.Alerted && Waited(0.5f)) { d.player.GetComponent<Health>().TakeDamage(999, p.position); Note("순찰에게 들킨 채 쓰러짐"); Next(); }
                else if (Waited(4f)) { d.player.GetComponent<Health>().TakeDamage(999, p.position); Note("(들키지 않아) 그냥 쓰러뜨림"); Next(); }
                break;
            case 29:
                if (Waited(4.5f))
                {
                    Note($"다시 시작한 자리 {p.position} (가게 저장 {d.shopSave.position}), 순찰 발각 {lawn.Alerted}, 적 활성 {lawn.Enemy.IsActive}, 순찰 위치 {lawn.transform.position}, 쓰러짐 {d.player.IsDead}");
                    lawn.route = lawnRoute;
                    Next();
                }
                break;
            case 30:
                Tp(new Vector3(164f, 0.15f, 108f), 0f); Next();
                break;
            case 31:
                Tp(new Vector3(164f, 0.15f, 124f), 0f); Next();
                break;
            case 32:
                if (Waited(0.4f)) { Tp(new Vector3(164f, 0.15f, 128f), 0f); Note("큰길 북쪽 끝(검문소가 보이는 자리)"); Next(); }
                break;
            case 33:
                if (d.CurrentPhase == Chapter1Director.Phase.CheckpointScene && Waited(3.2f)) { Note($"검문소 컷신: 레이 위치 {p.position}, 승합차 {(d.van ? d.van.activeSelf + " " + d.van.transform.position : "없음")}"); Shot("checkpoint_cut"); Next(); }
                else if (Waited(8f)) End("검문소 컷신이 시작되지 않음");
                break;
            case 34:
                if ((bool)typeof(Chapter1Director).GetField("vanStopped", Any).GetValue(d) && Waited(1.2f)) { Note($"승합차 검문소 앞 정지 {d.van.transform.position}, 문 {d.gate.position}"); Shot("van_stop"); Next(); }
                else if (Waited(14f)) End("승합차가 검문소에 서지 않음");
                break;
            case 35:
                if (d.CurrentPhase == Chapter1Director.Phase.Checkpoint && Waited(0.5f))
                {
                    Note($"컷신 끝: 저장 지점 {Checkpoint()} (구역 2 시작 {d.zone2Start.position}), 목표 '{StoryState.Objective}', 문 제자리 {Vector3.Distance(d.gate.position, (Vector3)typeof(Chapter1Director).GetField("gateClosedPos", Any).GetValue(d)) < 0.05f}, 컷신 {StoryState.CutsceneActive}");
                    Next();
                }
                else if (Waited(25f)) End("검문소 컷신 뒤 조작으로 돌아오지 않음");
                break;
            case 36:
                // 방패 경찰: 정면에서 주먹 → 막힘
                cop = GameObject.Find("Cop_Shield_A").GetComponent<EnemyAI>();
                Tp(cop.transform.position + cop.transform.forward * 1.4f, 0f);
                hpBefore = cop.Health.Current;
                bool frontLight = cop.Health.TakeDamage(10, cop.transform.position + cop.transform.forward * 1.4f);
                Note($"방패 정면 주먹(10): 맞음 {frontLight} 체력 {hpBefore}→{cop.Health.Current} (기대: 막힘)");
                Shot("shield_cop");
                Next();
                break;
            case 37:
                if (Waited(0.6f))
                {
                    var g = cop.GetComponent<ShieldGuard>();
                    bool kick = cop.Health.TakeDamage(20, cop.transform.position + cop.transform.forward * 1.4f);
                    Note($"정면 돌려차기(20): 맞음 {kick}, 방패 쳐냄 {g.Broken}, 체력 {cop.Health.Current} (기대: 맞음·쳐냄)");
                    Next();
                }
                break;
            case 38:
                if (Waited(3f))
                {
                    var g = cop.GetComponent<ShieldGuard>();
                    int before = cop.Health.Current;
                    bool side = cop.Health.TakeDamage(10, cop.transform.position + cop.transform.right * 1.4f);
                    Note($"방패 다시 듦 {!g.Broken}, 옆에서 주먹: 맞음 {side} 체력 {before}→{cop.Health.Current} (기대: 맞음)");
                    foreach (var n in new[] { "Cop_Shield_A", "Cop_Shield_B", "Cop_Torch" })
                    {
                        var e = GameObject.Find(n).GetComponent<EnemyAI>();
                        e.Health.TakeDamage(999, e.transform.position - e.transform.forward * 1.5f);
                    }
                    Note($"검문소 정리: 구역 정리 {d.checkpointZone.IsCleared}, 방패 떨어짐 {cop.GetComponent<ShieldGuard>().Dropped}");
                    Next();
                }
                break;
            case 39:
                if (Waited(1.5f)) { Tp(new Vector3(164f, 0.15f, 187f), 0f); Note("광장으로"); Next(); }
                break;
            case 40:
                if (d.CurrentPhase == Chapter1Director.Phase.Square && Waited(0.5f)) { Note($"저격수 등장: 활성 {d.sniper.IsActive}, 목표 '{StoryState.Objective}', 설명 {TutorialUI.ToastId}, 시간 배율 {Time.timeScale}"); Next(); }
                else if (Waited(6f)) End("저격수 소개로 넘어가지 않음");
                break;
            case 41:
                // 트인 곳(짐 마당 가운데)에 선다 → 조준선 → 맞는다
                d.player.GetComponent<Health>().Revive();
                laserShot = false; Tp(new Vector3(186f, 0.15f, 216f), 90f); hpBefore = d.player.GetComponent<Health>().Current; Next();
                break;
            case 42:
            {
                var laser = d.sniper.GetComponentInChildren<LineRenderer>(true);
                if (!laserShot && laser && laser.enabled && Waited(1.2f)) { laserShot = true; Shot("sniper_laser"); }
                int hp = d.player.GetComponent<Health>().Current;
                if (hp < hpBefore) { Note($"트인 곳: 저격에 맞음 {hpBefore}→{hp}"); Next(); }
                else if (Waited(7f)) End("트인 곳에서 저격수가 쏘지 않음");
                break;
            }
            case 43:
                // 트럭 뒤(서쪽)에 숨으면 맞지 않는다
                d.player.GetComponent<Health>().Revive();
                Tp(new Vector3(172.6f, 0.15f, 197f), 90f); hpBefore = d.player.GetComponent<Health>().Current; Next();
                break;
            case 44:
                if (Waited(6f)) { Note($"트럭 뒤 6초: 체력 {hpBefore}→{d.player.GetComponent<Health>().Current} (기대: 그대로)"); Next(); }
                break;
            case 45:
                // 골목 계단으로 옥상에: 길잡이·옥상 경비
                Note($"길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "없음")}");
                Tp(new Vector3(203.6f, 9.15f, 193.6f), 0f); Next();
                break;
            case 46:
                if (Waited(1.5f))
                {
                    var guard = GameObject.Find("Thug_Roof").GetComponent<EnemyAI>();
                    Note($"옥상: 경비 깨어남 {guard.IsActive}, 길잡이 {(StoryState.Waypoint ? StoryState.Waypoint.name : "없음")}");
                    Shot("roof");
                    Tp(d.sniper.transform.position + new Vector3(1.4f, 0.1f, 0f), -90f);
                    Next();
                }
                break;
            case 47:
                if (Waited(0.8f)) { d.sniper.Health.TakeDamage(999, p.position); Note("저격수 제압"); Next(); }
                break;
            case 48:
                // 옥상 경비가 아직 덤비는데 저격수부터 쓰러뜨렸다: 심문 컷신은 경비를 처리할 때까지 미룬다(잠긴 채 얻어맞지 않게)
                if (Waited(2.5f))
                {
                    var guard = GameObject.Find("Thug_Roof").GetComponent<EnemyAI>();
                    Note($"경비가 살아 있을 때: 컷신 {StoryState.CutsceneActive}, 목표 '{StoryState.Objective}' (기대: False, '옥상의 조직원을 처리한다'), 경비 활성 {guard.IsActive}");
                    guard.Health.TakeDamage(999, p.position);
                    Next();
                }
                break;
            case 49:
                if (d.CurrentPhase == Chapter1Director.Phase.Interrogation && StoryState.CutsceneActive && Waited(3f)) { Shot("interrogation"); Next(); }
                else if (Waited(8f)) End("심문으로 넘어가지 않음");
                break;
            case 50:
                if (d.CurrentPhase == Chapter1Director.Phase.Harbor && Waited(0.5f)) { Note($"심문 끝: 목표 '{StoryState.Objective}', 길잡이 {StoryState.Waypoint?.name}"); Tp(new Vector3(164f, 0.15f, 256f), 0f); Next(); }
                else if (Waited(40f)) End("심문 뒤 항구 단계로 넘어가지 않음");
                break;
            case 51:
                // 가게 2 앞 인도: 다가가면 저장
                if (Waited(0.4f)) { Tp(new Vector3(156.6f, 0.15f, 285.5f), 0f); Note("항구 길 · 가게 2 앞으로"); Next(); }
                break;
            case 52:
                if (Waited(0.8f))
                {
                    var gm = GameManager.Instance;
                    Note($"가게 2: 가까운 가게 '{(TapeShop.Nearby ? TapeShop.Nearby.shopName : "없음")}', 저장 시각 {gm.SavedAt:0.0} / 지금 {StoryState.Now:0.0}, 자막 '{StoryState.Line}'");
                    Shot("shop2");
                    Tp(new Vector3(160f, 0.15f, 315.4f), 0f);
                    Next();
                }
                break;
            case 53:
                if (Waited(0.6f))
                {
                    Note($"철조망 문 앞: 안내 '{StoryState.Nearby?.prompt}', 단계 {d.CurrentPhase}(기대 Harbor)");
                    Use(d.gateUse);
                    Next();
                }
                break;
            case 54:
                if (d.CurrentPhase == Chapter1Director.Phase.Docks && Waited(1.6f))
                {
                    Note($"문 열림: 왼짝 위치 x {d.gateLeaf.position.x:0.0}(기대 {d.gateOpenPos.x:0.0}), 목표 '{StoryState.Objective}', 길잡이 {StoryState.Waypoint?.name}");
                    Tp(new Vector3(164f, 0.02f, 322f), 0f);
                    Next();
                }
                else if (Waited(6f)) End("철조망 문이 열리지 않음");
                break;
            case 55:
                if (Waited(1.2f)) { Shot("docks_from_gate"); Tp(new Vector3(182f, 0.02f, 401f), 0f); Note("3번 부두 앞 마당으로"); Next(); }
                break;
            case 56:
                // 반장 소개가 시작되면 여기까지(보스전·새벽 장면은 BossTest가 따로 확인한다)
                if (d.CurrentPhase == Chapter1Director.Phase.BossIntro && Waited(1f)) { Note($"반장 소개 시작: 자막 '{StoryState.Line}'"); End("완료"); }
                else if (Waited(8f)) End("반장 소개로 넘어가지 않음");
                break;
        }
        if (Waited(40f)) End($"시간 초과(단계 {step})");
    }
}
