using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 프롤로그 흐름 자동 시험(플레이 중에 시작한다).
/// 걷기·문 열기 같은 조작은 지름길(순간 이동·상호작용 직접 호출)로 넘기고, 단계마다 상태를 기록한다:
/// 1회차 대니 방에서 바로 붙잡히는지 → 2회차에 설명 메시지가 없고 되감기가 잠겨 있는지 → 창문 → PROLOGUE END → 편지 뒷면 →
/// 되감기·설명 해제 → 알림·팝업 → 첫 되감기 → 끝. 화면 사진도 남긴다(IMGUI 포함).
/// 결과는 Log 경로의 텍스트 파일에 쓴다.
/// </summary>
public static class PrologueFlowTest
{
    public static string Log, ShotDir;
    static int step;
    static float stepStart, lastNote;
    static readonly List<string> lines = new();
    static readonly HashSet<string> seenMessages = new();
    static string lastToast, lastPhase;
    static float toastSeenAt;
    static int shot;

    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static bool parcelUsed;

    static string keptSeen;

    public static void Begin(string logPath, string shotDir)
    {
        // 설명 알림은 "처음 한 번만" 뜬다(PlayerPrefs에 남는다): 시험 동안만 본 기록을 비우고, 끝나면 되돌린다
        keptSeen = PlayerPrefs.GetString("tutorial_seen", "");
        Tutorial.ForgetSeen();
        parcelUsed = false;
        // 편집 모드에서 부르면 시험용 변경(감독 끄기 등)이 장면에 그대로 남는다 → 플레이 중에만
        if (!EditorApplication.isPlaying || !Object.FindAnyObjectByType<PrologueDirector>()) throw new System.InvalidOperationException("플레이 중인 프롤로그 장면에서만 시작할 수 있다");
        Log = logPath;
        ShotDir = shotDir;
        System.IO.Directory.CreateDirectory(shotDir);
        lines.Clear();
        seenMessages.Clear();
        step = 0;
        shot = 0;
        lastToast = lastPhase = null;
        stepStart = Time.realtimeSinceStartup;
        Application.runInBackground = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시험 시작");
    }

    public static void End(string why)
    {
        Note("시험 끝: " + why);
        EditorApplication.update -= Tick;
        if (keptSeen != null) { PlayerPrefs.SetString("tutorial_seen", keptSeen); PlayerPrefs.Save(); keptSeen = null; }
    }

    static void Note(string text)
    {
        var d = Object.FindAnyObjectByType<PrologueDirector>();
        string line = $"[{Time.realtimeSinceStartup - stepStart:000.0}s] 회차 {PrologueDirector.Loop} 단계 {Phase(d)} | {text}";
        lines.Add(line);
        System.IO.File.WriteAllLines(Log, lines);
    }

    static string Phase(PrologueDirector d) => d ? typeof(PrologueDirector).GetField("phase", Any).GetValue(d).ToString() : "-";

    static void Use(StoryInteractable it)
    {
        if (it.oneShot) it.enabled = false; // 실제로 F를 눌렀을 때처럼 한 번 쓰면 꺼진다
        var del = (System.Action)typeof(StoryInteractable).GetField("Used", Any).GetValue(it);
        del?.Invoke();
    }

    static void Shot(string name)
    {
        string path = $"{ShotDir}/{shot++:00}_{name}.png";
        ScreenCapture.CaptureScreenshot(path);
        Note("사진 " + System.IO.Path.GetFileName(path));
    }

    static float since;
    static bool Waited(float seconds) => Time.realtimeSinceStartup - since > seconds;
    static void Next() { step++; since = Time.realtimeSinceStartup; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { End("플레이 종료"); return; }
        var d = Object.FindAnyObjectByType<PrologueDirector>();
        if (!d) return;
        string phase = Phase(d);
        var gm = GameManager.Instance;

        // 언제나 기록: 단계 변화, 중앙 메시지, 설명 알림
        if (phase != lastPhase) { lastPhase = phase; Note($"단계 → {phase} (되감기 잠김 {StoryState.RewindLocked}, 위젯 숨김 {StoryState.WalkmanStowed}, 설명 해제 {Tutorial.Unlocked}, 목표 '{StoryState.Objective}')"); }
        if (gm && gm.MessageVisible && seenMessages.Add(gm.Message)) Note("중앙 메시지: " + gm.Message);
        string toast = TutorialUI.ToastId;
        if (toast != lastToast) { lastToast = toast; toastSeenAt = Time.realtimeSinceStartup; Note("설명 알림: " + (toast ?? "없음")); }
        if (StoryState.LineVisible && Time.realtimeSinceStartup - lastNote > 0.2f && seenMessages.Add("자막:" + StoryState.Line)) { lastNote = Time.realtimeSinceStartup; Note($"자막 [{StoryState.Speaker}] {StoryState.Line}"); }
        if (!string.IsNullOrEmpty(StoryState.Title) && seenMessages.Add("제목:" + StoryState.Title)) Note("제목: " + StoryState.Title);

        var player = d.player;
        switch (step)
        {
            case 0: // 1회차 집: 책상 택배(F) → 편지 읽고 닫기 → 현관문
                if (phase == "Desk" && StoryState.LetterText == null && !parcelUsed) { Note("책상 택배 열기(F)"); Use(d.deskParcel); parcelUsed = true; }
                else if (phase == "Desk" && StoryState.LetterText != null && Waited(1f)) { Note($"편지 펼침: {StoryState.LetterHeading} — 닫음(F)"); d.CloseLetter(); }
                else if (phase == "Home") { Note($"책상 편지 보임 {d.deskLetter.activeSelf}, 카세트 보임 {d.deskWalkman.activeSelf}"); Use(d.homeDoor); Next(); }
                break;
            case 1: // 거리: 대니 집 문 앞으로 순간 이동해 노크
                if (phase == "Street" && !StoryState.ControlLocked && Waited(1.5f))
                {
                    player.Teleport(d.friendDoor.transform.position + Vector3.back * 1.2f, Quaternion.identity);
                    Note($"도망 문 존재 {GameObject.Find("FriendExitDoor") != null}, 거리 조직원 {GameObject.Find("Thug_Street_A") != null}");
                    Use(d.friendDoor); Next();
                }
                break;
            case 2: // 드러남 → 바로 처형으로 이어지는지
                if (phase == "Execution") { Note("드러남 뒤 바로 붙잡힘(도망 단계 없음)"); Next(); }
                break;
            case 3: // 되감기 → 2회차
                if (PrologueDirector.Loop == 2 && phase == "Loop2") { Note("2회차 시작"); Next(); }
                break;
            case 4: // 2회차 깨어남 컷신이 끝나면 문으로
                if (!StoryState.CutsceneActive && StoryState.Objective != null && Waited(2f))
                {
                    Note($"2회차 조작 가능: 되감기 잠김 {StoryState.RewindLocked}, 공격 잠김 {StoryState.CombatLocked}");
                    Shot("loop2_after_wake");
                    player.Teleport(d.friendDoor.transform.position + Vector3.back * 1.2f, Quaternion.identity);
                    Use(d.friendDoor); Next();
                }
                break;
            case 5: // 싸움: 시작 컷신 뒤 조직원을 쓰러뜨린다
                if (phase == "Fight" && !StoryState.CutsceneActive && Waited(2.5f))
                {
                    Shot("fight_start");
                    Note($"싸움 시작: 목표 '{StoryState.Objective}', 근접 한 대 {d.roomThugs[0].damage}(준비 {d.roomThugs[0].windup}초, 간격 {d.roomThugs[0].attackCooldown}초), 총잡이 한 발 {d.roomThugs[1].shotDamage}(조준 {d.roomThugs[1].aimTime}초, 간격 {d.roomThugs[1].shotCooldown}초), 레이 HP {player.GetComponent<Health>().Current}");
                    foreach (var t in d.roomThugs) t.Health.TakeDamage(999, player.transform.position);
                    Note("조직원 제압(시험용 피해)"); Next();
                }
                break;
            case 6: // 창문
                if (d.friendWindow && d.friendWindow.gameObject.activeInHierarchy && Waited(1f)) { Use(d.friendWindow); Note("뒷창문 사용"); Next(); }
                break;
            case 7: // PROLOGUE END → 뒷마당 → 편지 뒷면
                if (StoryState.Title == "PROLOGUE  END" && Waited(0.5f)) { Shot("prologue_end"); Next(); }
                break;
            case 8:
                if (StoryState.LetterText != null && Waited(1.5f))
                {
                    Note($"편지 화면 열림, 위치 {player.transform.position}, 되감기 잠김 {StoryState.RewindLocked}, 설명 해제 {Tutorial.Unlocked}");
                    Shot("letter_back"); Next();
                }
                break;
            case 9:
                if (Waited(1.5f)) { d.CloseLetter(); Note("편지 닫음(F)"); Next(); }
                break;
            case 10: // 해제 → 알림(다 미끄러져 들어온 뒤 찍는다)
                if (phase == "Trial" && TutorialUI.ToastId == "rewind" && Time.realtimeSinceStartup - toastSeenAt > 1f)
                {
                    Note($"해제됨: 되감기 잠김 {StoryState.RewindLocked}, 위젯 숨김 {StoryState.WalkmanStowed}, 설명 해제 {Tutorial.Unlocked}, 상호작용 안내 '{StoryState.Nearby?.prompt}'");
                    Shot("toast_rewind"); Next();
                }
                break;
            case 11:
                if (Waited(0.6f)) { bool ok = TutorialUI.Ensure().OpenToast(); Note($"H: 팝업 열기 {ok}, 열림 {TutorialUI.PopupOpen}, 시간배율 {Time.timeScale}, 조작 잠김 {StoryState.InputLocked}"); Next(); }
                break;
            case 12: if (Waited(0.6f)) { Shot("popup_rewind_p1"); Next(); } break;
            case 13: if (Waited(0.4f)) { TutorialUI.Ensure().Turn(1); Next(); } break;
            case 14: if (Waited(0.4f)) { Shot("popup_rewind_p2"); Next(); } break;
            case 15: if (Waited(0.4f)) { TutorialUI.Ensure().Turn(1); Next(); } break;
            case 16: if (Waited(0.4f)) { Shot("popup_rewind_p3"); Note($"페이지 {TutorialUI.PageIndex + 1}"); Next(); } break;
            case 17:
                if (Waited(0.4f)) { TutorialUI.Ensure().ClosePopup(); Note($"팝업 닫힘: 열림 {TutorialUI.PopupOpen}, 시간배율 {Time.timeScale}, 조작 잠김 {StoryState.InputLocked}"); Next(); }
                break;
            case 18: // 두 번째 알림(격투)
                if (TutorialUI.ToastId == "combat" && Time.realtimeSinceStartup - toastSeenAt > 1f) { Shot("toast_combat"); Next(); }
                break;
            case 19: if (Waited(0.5f)) { TutorialUI.Ensure().OpenToast(); Next(); } break;
            case 20: if (Waited(0.6f)) { Shot("popup_combat_p1"); Next(); } break;
            case 21: // 사진은 프레임 끝에 찍히므로, 다음 프레임 이후에 쪽을 넘긴다
                if (Waited(0.3f) && TutorialUI.PageIndex == 0) TutorialUI.Ensure().Turn(1);
                if (Waited(0.9f)) { Shot("popup_combat_p2"); Next(); }
                break;
            case 22: if (Waited(0.3f)) { TutorialUI.Ensure().ClosePopup(); Next(); } break;
            case 23: // 첫 되감기
                if (Waited(1f))
                {
                    var w = player.GetComponent<Walkman>();
                    Note($"테이프 '{w.Inserted?.title}' 남은 되감기 {w.Inserted?.charges}, 되감은 표시 {w.Inserted?.rewound}, 재생 가능 {w.Inserted?.CanPlay}, 연습 플래그 {StoryState.FreeRewind}, 기록 {TimeRewind.RecordedSeconds:0.0}초");
                    Shot("hud_after_unlock");
                    bool ok = w.RewindSteps(1);
                    Note("되감기 시작 " + ok); Next();
                }
                break;
            case 24:
                if (phase == "End" && Waited(2.5f))
                {
                    var w = player.GetComponent<Walkman>();
                    Note($"연습 되감기 뒤 테이프: 남은 되감기 {w.Inserted?.charges}, 되감은 표시 {w.Inserted?.rewound}, 재생 가능 {w.Inserted?.CanPlay}, 연습 플래그 {StoryState.FreeRewind}");
                    Shot("end"); End("완료");
                }
                break;
        }

        if (Time.realtimeSinceStartup - since > 90f && step < 24) { Shot("timeout"); End($"시간 초과 (단계 {step})"); }
    }
}
