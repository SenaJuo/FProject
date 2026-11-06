using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 체험판(2026-11-06, 게임 박람회용 약 10분): 프롤로그 → 1장 3번 부두 반장전 → 체험판 끝 화면 → 타이틀.
/// 빌드에 2장이 없으면 체험판이다(Title · Prologue · Chapter1만 넣어 빌드). 시험은 Force로 켠다.
/// 박람회에서 다음 사람이 바로 할 수 있게: 이어하기가 없고(저장 파일을 쓰지 않는다), 2분 동안 아무 입력이 없으면 타이틀로 돌아간다.
/// </summary>
public class Demo : MonoBehaviour
{
    public const string Stage = "Chapter1", StageCheckpoint = "c1_zone3", TitleScene = "Title";
    public const float IdleSeconds = 120f;

    /// <summary>에디터·시험에서 체험판 흐름을 켠다</summary>
    public static bool Force;

    public static bool Active => Force || (!Application.isEditor && !SceneLoader.Has("Chapter2"));

    float lastInput;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Force = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Active || FindAnyObjectByType<Demo>()) return;
        var go = new GameObject("Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<Demo>();
    }

    void Update()
    {
        if (AnyInput() || SceneManager.GetActiveScene().name == TitleScene) { lastInput = Time.unscaledTime; return; }
        if (Time.unscaledTime - lastInput < IdleSeconds) return;
        // 아무도 하지 않는다: 다음 사람을 위해 처음(타이틀)으로
        lastInput = Time.unscaledTime;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneLoader.Load(TitleScene);
    }

    static bool AnyInput()
    {
        var k = Keyboard.current;
        if (k != null && k.anyKey.isPressed) return true;
        var m = Mouse.current;
        if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.delta.ReadValue().sqrMagnitude > 0.5f)) return true;
        var g = Gamepad.current;
        if (g != null && (g.leftStick.ReadValue().sqrMagnitude > 0.04f || g.rightStick.ReadValue().sqrMagnitude > 0.04f
                          || g.buttonSouth.isPressed || g.buttonEast.isPressed || g.buttonWest.isPressed || g.buttonNorth.isPressed || g.startButton.isPressed))
            return true;
        return false;
    }

    /// <summary>
    /// 프롤로그 끝에서 반장전으로: 첫 테이프를 가득 채우고, 장르 능력(시티팝 — 시간 감속)을 맛볼 테이프 하나를 더 준다.
    /// 본편이라면 1장 가게에서 바꿔 얻는 테이프다
    /// </summary>
    public static void GiveStageTapes(Walkman walkman)
    {
        if (!walkman) return;
        foreach (var t in walkman.tapes) t.charges = CassetteTape.MaxCharges;
        if (walkman.tapes.Exists(t => t.Genre != TapeGenre.None)) return;
        walkman.tapes.Add(new CassetteTape
        {
            title = "시티팝 모음 1",
            musicId = "citypop_1",
            trackTitles = new[] { "네온 드라이브", "한밤의 정류장", "다시 봄" },
            trackGains = new[] { 0.90f, 0.95f, 0.90f },
            trackLength = 120f,
            playable = true,
            charges = CassetteTape.MaxCharges,
        });
    }
}
