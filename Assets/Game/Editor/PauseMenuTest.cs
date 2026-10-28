using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 일시정지 메뉴 시험(플레이 중, 1장): 키보드 입력을 흉내 내 Esc로 열고 닫으며
/// 시간·소리·이야기 시계가 멈추는지, 옵션에서 소리 크기가 바뀌고 저장되는지, 다른 창이 열려 있을 때 Esc가 그 창만 닫는지,
/// 연출 슬로모션·타격 멈춤 중에 열고 닫으면 알맞은 속도로 돌아오는지 기록한다.
/// </summary>
public static class PauseMenuTest
{
    static readonly List<string> lines = new();
    static string log;
    static int step;
    static float next, t0;
    static bool sleepDone;
    static float nowAtOpen, savedMaster, savedSfx;
    static Vector3 posAtOpen;

    public static void Begin(string logPath)
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("플레이 중에만");
        Application.runInBackground = true;
        log = logPath;
        lines.Clear();
        step = 0;
        t0 = Time.unscaledTime;
        next = Time.unscaledTime + 0.5f;
        savedMaster = GameSettings.Get(GameSettings.Channel.Master);
        savedSfx = GameSettings.Get(GameSettings.Channel.Sfx);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t)
    {
        t = $"[{Time.unscaledTime - t0:0.00}s] {t}";
        lines.Add(t);
        Debug.Log("[일시정지 시험] " + t);
    }

    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));

    /// <summary>한 번 눌렀다 뗀다(다음 단계에서 뗀다)</summary>
    static void Tap(Key key)
    {
        Keys(key);
        // 좌우는 0.35초 누르고 있으면 연달아 바뀐다(PauseMenu): 단계 간격과 같아 가끔 한 칸 더 갔다 → 짧게 누르고 뗀다
        if (key == Key.A || key == Key.D) next = Time.unscaledTime + 0.12f;
    }
    static void Release() => Keys();

    static IEnumerator SleepProbe()
    {
        yield return StoryState.Sleep(1f);
        sleepDone = true;
    }

    static PlayerController Player => Object.FindAnyObjectByType<PlayerController>();
    static Walkman Walkman => Object.FindAnyObjectByType<Walkman>();

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.35f; // 메뉴는 쪽이 바뀐 뒤 0.25초 동안 결정을 받지 않는다
        if (step == 0 && StoryState.InputLocked) return; // 장 시작 제목·페이드
        var p = Player;
        switch (step++)
        {
            // ① Esc로 열기: 시간·소리·이야기 시계가 멈춘다
            case 0:
                sleepDone = false;
                p.StartCoroutine(SleepProbe());
                StoryState.Say("시험", "일시정지 중에도 남아 있어야 하는 자막", 1.2f);
                Tap(Key.Escape);
                break;
            case 1:
                Release();
                nowAtOpen = StoryState.Now;
                posAtOpen = p.transform.position;
                Note($"① Esc: 열림 {PauseMenu.IsOpen}, 쪽 {PauseMenu.CurrentPage}, 시간 배율 {Time.timeScale}, 소리 멈춤 {AudioListener.pause}, 조작 잠김 {StoryState.InputLocked} (기대 True, Main, 0, True, True)");
                next = Time.unscaledTime + 1.5f;
                break;
            case 2:
                Release();
                Note($"   1.5초 뒤: 이야기 시계 {StoryState.Now - nowAtOpen:0.000}초 흐름(기대 0), 레이 이동 {Vector3.Distance(p.transform.position, posAtOpen):0.000}m(기대 0), 기다림 끝남 {sleepDone}(기대 False), 자막 {StoryState.LineVisible}(기대 True)");
                Tap(Key.S); // 옵션으로
                break;
            case 3: Release(); break;
            case 4: Note($"   S: 고른 줄 {PauseMenu.SelectedIndex}(기대 1)"); Tap(Key.Enter); break;
            case 5: Release(); break;
            // ② 옵션: 전체 음량 A 두 번 → 90%
            case 6: Note($"② 옵션: 쪽 {PauseMenu.CurrentPage}(기대 Options), 고른 줄 {PauseMenu.SelectedIndex}"); GameSettings.Set(GameSettings.Channel.Master, 1f); Tap(Key.A); break;
            case 7: Release(); break;
            case 8: Tap(Key.A); break;
            case 9: Release(); break;
            case 10:
                Note($"   전체 A×2: {GameSettings.Get(GameSettings.Channel.Master):0.00}(기대 0.90), AudioListener.volume {AudioListener.volume:0.000}(기대 0.810), 저장값 {PlayerPrefs.GetFloat("volume.master", -1f):0.00}");
                Tap(Key.S);
                break;
            case 11: Release(); break;
            case 12: Tap(Key.S); break;
            case 13: Release(); break;
            case 14: Note($"   S×2: 고른 줄 {PauseMenu.SelectedIndex}(기대 2 = 효과음)"); Tap(Key.A); break;
            case 15: Release(); break;
            case 16:
                Note($"   효과음 A: {GameSettings.Get(GameSettings.Channel.Sfx):0.00}(기대 {Mathf.Max(0f, savedSfx - 0.05f):0.00}), 소리 크기 {GameSettings.Gain(GameSettings.Channel.Sfx):0.000}");
                Tap(Key.Escape); // 옵션 → 처음 쪽
                break;
            case 17: Release(); break;
            case 18: Note($"   Esc: 쪽 {PauseMenu.CurrentPage}(기대 Main), 고른 줄 {PauseMenu.SelectedIndex}(기대 1)"); Tap(Key.Escape); break;
            case 19: Release(); next = Time.unscaledTime + 0.3f; break;
            case 20:
                Note($"③ Esc로 닫기: 열림 {PauseMenu.IsOpen}, 시간 배율 {Time.timeScale}, 소리 멈춤 {AudioListener.pause}, 조작 잠김 {StoryState.InputLocked} (기대 False, 1, False, False)");
                next = Time.unscaledTime + 1.3f;
                break;
            case 21:
                Note($"   닫고 1.3초: 기다림 끝남 {sleepDone}(기대 True — 멈춘 동안은 세지 않았다)");
                // ④ 연출 슬로모션 중에 열고 닫기 → 그 속도로 돌아간다
                Time.timeScale = 0.35f;
                Tap(Key.Escape);
                break;
            case 22: Release(); Tap(Key.Escape); break;
            case 23: Release(); next = Time.unscaledTime + 0.3f; break;
            case 24:
                Note($"④ 슬로모션 0.35 중 열고 닫기: 시간 배율 {Time.timeScale:0.00}(기대 0.35)");
                Time.timeScale = 1f;
                // ⑤ 타격 멈춤 중에 열면 닫을 때 정상 속도
                HitFeel.HitStop(0.6f);
                Tap(Key.Escape);
                break;
            case 25: Release(); next = Time.unscaledTime + 0.9f; break; // 멈춤 시간(0.6초)보다 오래 열어 둔다
            case 26: Tap(Key.Escape); break;
            case 27: Release(); next = Time.unscaledTime + 0.3f; break;
            case 28:
                Note($"⑤ 타격 멈춤 중 열고 닫기: 시간 배율 {Time.timeScale:0.00}(기대 1)");
                // ⑥ 인벤토리에서 Esc: 인벤토리만 닫히고 일시정지는 열리지 않는다
                Tap(Key.I);
                break;
            case 29: Release(); break;
            case 30: Note($"⑥ I: 인벤토리 {Walkman.InventoryOpen}(기대 True)"); Tap(Key.Escape); break;
            case 31: Release(); break;
            case 32:
                Note($"   Esc: 인벤토리 {Walkman.InventoryOpen}, 일시정지 {PauseMenu.IsOpen}, 시간 배율 {Time.timeScale} (기대 False, False, 1)");
                // ⑦ 타이틀로 확인 쪽: 열기 → S S(조작법 확인) → S S Enter → 아니요 → 처음 쪽(조작법·튜토리얼이 끼어 타이틀로는 5번째)
                Tap(Key.Escape);
                break;
            case 33: Release(); break;
            case 34: Tap(Key.S); break;
            case 35: Release(); break;
            case 36: Tap(Key.S); break;
            case 37: Release(); break;
            case 38: Note($"   S×2: 고른 줄 {PauseMenu.SelectedIndex}(기대 2 = 조작법)"); Tap(Key.Enter); break;
            case 39: Release(); break;
            case 40: Note($"   조작법: 쪽 {PauseMenu.CurrentPage}(기대 Controls)"); Tap(Key.Escape); break;
            case 41: Release(); break;
            case 42: Note($"   Esc: 쪽 {PauseMenu.CurrentPage}(기대 Main), 고른 줄 {PauseMenu.SelectedIndex}(기대 2)"); Tap(Key.S); break;
            case 43: Release(); break;
            case 44: Tap(Key.S); break; // 튜토리얼이 끼어 타이틀로는 5번째(4)
            case 45: Release(); break;
            case 46: Note($"   S×2: 고른 줄 {PauseMenu.SelectedIndex}(기대 4 = 타이틀로)"); Tap(Key.Enter); break;
            case 47: Release(); break;
            case 48: Note($"⑦ 타이틀로: 쪽 {PauseMenu.CurrentPage}(기대 Confirm), 고른 줄 {PauseMenu.SelectedIndex}(기대 0 = 아니요)"); Tap(Key.Enter); break;
            case 49: Release(); break;
            case 50: Note($"   아니요: 쪽 {PauseMenu.CurrentPage}(기대 Main), 고른 줄 {PauseMenu.SelectedIndex}(기대 4)"); Tap(Key.Escape); break;
            case 51: Release(); next = Time.unscaledTime + 0.3f; break;
            case 52:
                Note($"   닫힘 {!PauseMenu.IsOpen}, 시간 배율 {Time.timeScale}");
                // 시험 전 소리 설정으로 돌려 둔다
                GameSettings.Set(GameSettings.Channel.Master, savedMaster);
                GameSettings.Set(GameSettings.Channel.Sfx, savedSfx);
                GameSettings.Save();
                Note("시험 끝");
                EditorApplication.update -= Tick;
                System.IO.File.WriteAllLines(log, lines);
                break;
        }
    }
}
