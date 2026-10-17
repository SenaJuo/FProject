using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 테이프 능력 시험(플레이 중, 아무 장면 — 적이 있으면 좋다):
///  ① 시티팝 테이프 R → 고르기 창(세상 0.15) → E: 네온 러시(세상 0.25, 레이 ×4, 잔상) → 7초 뒤 원래대로, 횟수 −1, 테이프는 계속 재생 가능
///  ② 재즈·소울·앰비언트 E: 값이 바뀌고 끝나면 돌아오는가 ③ 장르 테이프 R → Q: 되감기(테이프는 다시 못 듣게) ④ 장르 없는 테이프 R: 창 없이 바로 되감기.
/// </summary>
public static class TapeAbilityTest
{
    static readonly List<string> lines = new();
    static string log, dir;
    static int step, shots;
    static float since;
    static Walkman w;
    static PlayerController p;
    static CassetteTape tape;

    public static void Begin(string logPath, string shotDir)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        Application.runInBackground = true;
        log = logPath; dir = shotDir; System.IO.Directory.CreateDirectory(dir);
        lines.Clear(); step = 0; shots = 0; since = Time.realtimeSinceStartup;
        w = Object.FindAnyObjectByType<Walkman>(); p = w.GetComponent<PlayerController>();
        StoryState.ControlLocked = false; StoryState.CutsceneActive = false; StoryState.Fade = 0f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { lines.Add(t); Debug.Log("[테이프 능력 시험] " + t); }
    static bool Waited(float s) => Time.realtimeSinceStartup - since >= s;
    static void Next() { step++; since = Time.realtimeSinceStartup; }
    static void Shot(string n) => ScreenCapture.CaptureScreenshot($"{dir}/{shots++:00}_{n}.png");

    static CassetteTape Give(string musicId, string title)
    {
        var t = new CassetteTape { title = title, musicId = musicId, trackTitles = new[] { "A", "B" }, trackLength = 60f, playable = true, charges = 3 };
        w.AddTape(t);
        w.Equip(w.tapes.IndexOf(t));
        return t;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { End("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        switch (step)
        {
            case 0:
                if (Waited(4f)) // 기록이 쌓이게 잠깐 기다린다
                {
                    StoryState.ControlLocked = false; StoryState.CutsceneActive = false; StoryState.Fade = 0f;
                    tape = Give("citypop_1", "시험 시티팝");
                    var open = typeof(Walkman).GetMethod("TryOpenChoice", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    bool opened = (bool)open.Invoke(w, null);
                    Note($"① 고르기 창: 열림 {opened}/{w.ChoiceOpen}, 세상 {Time.timeScale:0.00}(기대 0.15), 장르 {tape.Genre}");
                    Shot("choice");
                    Next();
                }
                break;
            case 1:
                if (Waited(0.4f))
                {
                    w.Choose(2);
                    Note($"   E: 능력 {TapeAbility.Current}, 남은 {TapeAbility.Remaining:0.0}s, 횟수 {tape.charges}(기대 2)");
                    Next();
                }
                break;
            case 2:
                // 레이를 조금씩 옮겨 잔상이 늘어서게
                p.Teleport(p.transform.position + p.transform.forward * 0.12f, p.transform.rotation);
                if (Waited(1.2f))
                {
                    int ghosts = 0; foreach (var go in Resources.FindObjectsOfTypeAll<MeshRenderer>()) if (go.name == "NeonGhost" && go.gameObject.activeInHierarchy) ghosts++;
                    Note($"   네온 러시 중: 세상 {Time.timeScale:0.00}(기대 0.25), 레이 {PlayerController.SelfTime:0.0}(기대 4), 잔상 {ghosts}개, 테이프 재생 가능 {tape.CanPlay}");
                    Shot("neon_rush");
                    Next();
                }
                break;
            case 3:
                if (TapeAbility.Current == TapeGenre.None && Waited(0.2f))
                {
                    Note($"   끝(실제 {Time.realtimeSinceStartup - since + 1.2f:0.0}s 뒤): 세상 {Time.timeScale:0.00}, 레이 {PlayerController.SelfTime:0.0}");
                    // ② 재즈
                    tape = Give("rain_jazz", "시험 재즈");
                    float before = p.dodgeIFrames;
                    w.Choose(2);
                    Note($"② 재즈 「즉흥」: 무적 회피 {before:0.00}→{p.dodgeIFrames:0.00}, 반격 창 {p.counterWindow:0.00}");
                    Next();
                }
                else if (Waited(8f)) End("네온 러시가 끝나지 않음");
                break;
            case 4:
                if (Waited(0.5f))
                {
                    tape = Give("dock_soul", "시험 소울"); // 재즈를 끄고 소울로(고르기 창이 켜진 능력을 끈다)
                    w.Choose(2);
                    Note($"   재즈 꺼짐 뒤 무적 회피 {p.dodgeIFrames:0.00}(원래로) · 소울 「그루브」: 공격력 {p.attackPower}(기대 15), 방패 무시 {TapeAbility.Current == TapeGenre.Soul}");
                    Next();
                }
                break;
            case 5:
                if (Waited(0.5f))
                {
                    tape = Give("seven_hope", "시험 앰비언트");
                    w.Choose(2);
                    var h = p.GetComponent<Health>();
                    Note($"   소울 꺼짐 뒤 공격력 {p.attackPower}(기대 10) · 앰비언트 「고요」: 받는 피해 배율 {h.damageTakenScale:0.0}(기대 0.5)");
                    Shot("ambient");
                    Next();
                }
                break;
            case 6:
                if (Waited(0.5f))
                {
                    // ③ 장르 테이프 R → Q: 되감기
                    tape = Give("night_drive", "시험 신스");
                    bool ok = w.Choose(1);
                    Note($"③ Q 되감기 시작 {ok}, 되감는 중 {w.Rewinding}, 앰비언트 꺼짐 {TapeAbility.Current == TapeGenre.None}, 피해 배율 {p.GetComponent<Health>().damageTakenScale:0.0}");
                    Next();
                }
                break;
            case 7:
                if (!w.Rewinding && Waited(0.5f))
                {
                    Note($"   되감은 뒤: 횟수 {tape.charges}(기대 2), 테이프 재생 가능 {tape.CanPlay}(기대 True — 2026-10-14 규칙: 노래는 계속 흐른다)");
                    // ④ 장르 없는 테이프
                    var plain = new CassetteTape { title = "주운 테이프", playable = false, charges = 2 };
                    w.AddTape(plain); w.Equip(w.tapes.IndexOf(plain));
                    var open = typeof(Walkman).GetMethod("TryOpenChoice", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Note($"④ 장르 없는 테이프: 고르기 창 {(bool)open.Invoke(w, null)}(기대 False — R은 바로 되감기)");
                    End("완료");
                }
                else if (Waited(10f)) End("되감기가 끝나지 않음");
                break;
        }
    }

    static void End(string why)
    {
        EditorApplication.update -= Tick;
        Note("시험 끝: " + why);
        if (log != null) System.IO.File.WriteAllLines(log, lines);
        log = null;
    }
}
