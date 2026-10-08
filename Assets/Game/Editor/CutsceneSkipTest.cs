using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 컷신 건너뛰기 시험(플레이 중 프롤로그에서 시작). Space 대신 CutsceneSkip.SimulateHold로 누르고 있는 것처럼 한다.
///  1) 오프닝 컷신: 0.6초만 누르고 떼기 → 건너뛰지 않아야 한다(원이 다시 0으로)
///  2) 오프닝: 끝까지 누르기 → 바로 집(조작)으로
///  3) 대니 집 드러남~총격~되감기 → 건너뛰면 2회차로
///  4) 2회차 깨어남, 싸움 시작, 편지 뒷면 컷신도 건너뛰기 → 각각 다음 조작 단계로, 편지 뒤에는 되감기·설명이 풀려 있어야 한다
///     (편지 앞 장면을 건너뛰어도 편지는 펼쳐져야 하고, 편지는 따로 눌러야 넘어간다)
/// </summary>
public static class CutsceneSkipTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static string log, dir;
    static readonly List<string> lines = new();
    static int step, shot;
    static float since, holdStart;
    static string lastPhase;
    static bool letterShot;

    static bool parcelUsed;

    public static void Begin(string logPath, string shotDir)
    {
        parcelUsed = false;
        if (!EditorApplication.isPlaying || !Object.FindAnyObjectByType<PrologueDirector>()) throw new System.InvalidOperationException("플레이 중인 프롤로그 장면에서만 시작할 수 있다");
        log = logPath; dir = shotDir;
        System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shot = 0; lastPhase = null; letterShot = false;
        since = Time.realtimeSinceStartup;
        Application.runInBackground = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Note("시작");
    }

    static void Note(string t)
    {
        var d = Object.FindAnyObjectByType<PrologueDirector>();
        lines.Add($"[{Time.realtimeSinceStartup:0000.0}] 회차 {PrologueDirector.Loop} 단계 {Phase(d)} | {t}");
        System.IO.File.WriteAllLines(log, lines);
    }

    static string Phase(PrologueDirector d) => d ? typeof(PrologueDirector).GetField("phase", Any).GetValue(d).ToString() : "-";
    static void Use(StoryInteractable it) { if (it.oneShot) it.enabled = false; ((System.Action)typeof(StoryInteractable).GetField("Used", Any).GetValue(it))?.Invoke(); }
    static bool Waited(float s) => Time.realtimeSinceStartup - since > s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Shot(string n) { ScreenCapture.CaptureScreenshot($"{dir}/{shot++:00}_{n}.png"); Note("사진 " + n); }
    static void Hold(bool on) { CutsceneSkip.SimulateHold = on; if (on) holdStart = Time.realtimeSinceStartup; }

    static void End(string why) { CutsceneSkip.SimulateHold = false; Note("시험 끝: " + why); EditorApplication.update -= Tick; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        var d = Object.FindAnyObjectByType<PrologueDirector>();
        if (!d) return;
        string phase = Phase(d);
        if (phase != lastPhase) { lastPhase = phase; Note($"단계 → {phase} (컷신 {StoryState.CutsceneActive}, 목표 '{StoryState.Objective}', 되감기 잠김 {StoryState.RewindLocked}, 설명 해제 {Tutorial.Unlocked})"); }
        var p = d.player;
        switch (step)
        {
            case 0: if (phase == "Intro" && Waited(0.6f)) { Hold(true); Note("오프닝에서 누르기 시작(0.6초만)"); Next(); } break;
            case 1:
                if (Time.realtimeSinceStartup - holdStart > 0.3f && !lines.Exists(l => l.Contains("ring_half"))) Shot("ring_half");
                if (Time.realtimeSinceStartup - holdStart > 0.6f) { Note($"떼기 직전 채움 {CutsceneSkip.Progress:0.00}"); Hold(false); Next(); }
                break;
            case 2:
                if (Waited(1.5f)) { Note($"뗀 뒤 1.5초: 채움 {CutsceneSkip.Progress:0.00}, 건너뛰기 {StoryState.SkipRequested}, 단계 {phase}, 컷신 {StoryState.CutsceneActive} → 건너뛰지 않아야 한다"); Hold(true); Note("다시 끝까지 누르기"); Next(); }
                break;
            case 3:
                if (phase == "Desk") { Note($"건너뜀: 누른 지 {Time.realtimeSinceStartup - holdStart:0.00}초 만에 책상 단계, 컷신 {StoryState.CutsceneActive}, 페이드 {StoryState.Fade:0.00}, 책상 카세트 숨김 {!d.deskWalkman.activeSelf}"); Hold(false); Next(); }
                else if (Waited(6f)) End("오프닝 건너뛰기 실패");
                break;
            case 4:
                if (phase == "Desk" && StoryState.LetterText == null && !parcelUsed) { Use(d.deskParcel); parcelUsed = true; }
                else if (StoryState.LetterText != null) d.CloseLetter();
                else if (phase == "Home" && Waited(0.8f)) { Shot("after_intro_skip"); Use(d.homeDoor); Next(); }
                break;
            case 5:
                if (phase == "Street" && !StoryState.ControlLocked && Waited(1.2f)) { p.Teleport(d.friendDoor.transform.position + Vector3.back * 1.2f, Quaternion.identity); Use(d.friendDoor); Next(); }
                break;
            case 6: if (phase == "Reveal" && Waited(1f)) { Hold(true); Note("드러남 컷신에서 끝까지 누르기"); Next(); } break;
            case 7:
                if (PrologueDirector.Loop == 2) { Note($"건너뜀: 누른 지 {Time.realtimeSinceStartup - holdStart:0.00}초 만에 2회차"); Hold(false); Next(); }
                else if (Waited(10f)) End("드러남~되감기 건너뛰기 실패");
                break;
            case 8: if (phase == "Loop2" && StoryState.CutsceneActive && Waited(1f)) { Hold(true); Note("깨어남 컷신에서 누르기"); Next(); } break;
            case 9:
                if (!StoryState.CutsceneActive && StoryState.Objective != null) { Note($"건너뜀: {Time.realtimeSinceStartup - holdStart:0.00}초, 목표 '{StoryState.Objective}', 공격 잠김 {StoryState.CombatLocked}"); Hold(false); Next(); }
                else if (Waited(6f)) End("깨어남 건너뛰기 실패");
                break;
            case 10: if (Waited(0.8f)) { p.Teleport(d.friendDoor.transform.position + Vector3.back * 1.2f, Quaternion.identity); Use(d.friendDoor); Next(); } break;
            case 11: if (phase == "Fight" && StoryState.CutsceneActive && Waited(0.6f)) { Hold(true); Note("싸움 시작 컷신에서 누르기"); Next(); } break;
            case 12:
                if (!StoryState.CutsceneActive) { Note($"건너뜀: {Time.realtimeSinceStartup - holdStart:0.00}초, 목표 '{StoryState.Objective}'"); Hold(false); Next(); }
                else if (Waited(6f)) End("싸움 시작 건너뛰기 실패");
                break;
            case 13: if (Waited(1f)) { foreach (var t in d.roomThugs) t.Health.TakeDamage(999, p.transform.position); Next(); } break;
            case 14: if (d.friendWindow && d.friendWindow.gameObject.activeInHierarchy && Waited(0.5f)) { Use(d.friendWindow); Next(); } break;
            // 편지 앞 장면(PROLOGUE END)을 건너뛰어도 편지 뒷면(되감기 규칙)은 그대로 펼쳐져야 한다
            case 15: if (phase == "Letter" && StoryState.LetterText == null && StoryState.CutsceneActive && Waited(0.5f)) { Hold(true); Note("편지 앞 장면에서 누르기"); Next(); } break;
            case 16:
                if (StoryState.LetterText != null) { Note($"편지 앞 장면 건너뜀 → 편지는 그대로 펼쳐짐 ({Time.realtimeSinceStartup - holdStart:0.00}초)"); Hold(false); Next(); }
                else if (phase == "Trial") End("편지 앞 장면을 건너뛰자 편지까지 넘어감");
                else if (Waited(15f)) End("편지가 뜨지 않음");
                break;
            // 편지 종이가 펼쳐진 상태에서 누른다(편지 화면 자체도 건너뛸 수 있어야 한다)
            case 17: if (phase == "Letter" && StoryState.LetterText != null && Waited(0.5f)) { Hold(true); Note("편지가 펼쳐진 상태에서 누르기"); Next(); } break;
            case 18:
                if (!letterShot && CutsceneSkip.Progress > 0.5f && StoryState.LetterText != null) { letterShot = true; Shot("letter_hold"); } // 편지 위에 원이 보이는지
                if (phase == "Trial") { Note($"건너뜀: {Time.realtimeSinceStartup - holdStart:0.00}초, 편지 닫힘 {StoryState.LetterText == null}, 되감기 잠김 {StoryState.RewindLocked}, 위젯 숨김 {StoryState.WalkmanStowed}, 설명 해제 {Tutorial.Unlocked}, 연습 {StoryState.FreeRewind}, 컷신 {StoryState.CutsceneActive}, 페이드 {StoryState.Fade:0.00}"); Hold(false); Next(); }
                else if (Waited(10f)) End("편지 건너뛰기 실패");
                break;
            case 19: if (Waited(1.5f)) { Shot("after_letter_skip"); Note($"설명 알림 {TutorialUI.ToastId}"); End("완료"); } break;
        }
        if (Waited(40f) && step < 19) End($"시간 초과(단계 {step})");
    }
}
