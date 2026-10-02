using UnityEngine;

/// <summary>
/// 이야기 진행(프롤로그 등)이 다른 시스템에 알리는 공용 상태.
/// 플레이어 조작·카세트 플레이어·카메라·HUD가 이 값을 보고 스스로 멈추거나 바뀐다.
/// 씬을 다시 불러올 때마다 ResetAll()로 초기화한다(정적 값이라 씬 전환에도 남기 때문).
/// </summary>
public static class StoryState
{
    /// <summary>컷신 중: HUD 대신 레터박스·자막만 보이고, 모든 조작이 잠긴다.</summary>
    public static bool CutsceneActive;
    /// <summary>컷신이 아니어도 조작만 잠글 때(예: 대사 중)</summary>
    public static bool ControlLocked;
    /// <summary>공격 금지(프롤로그 첫 회차: 주인공은 그저 친구에게 물어보러 가는 길이다)</summary>
    public static bool CombatLocked;
    /// <summary>되감기·인벤토리 금지(되감기 능력을 아직 모르는 첫 회차)</summary>
    public static bool RewindLocked;
    /// <summary>카세트 플레이어를 아직 착용하지 않음(프롤로그): 테이프가 재생되지 않고 왼쪽 아래 카세트 위젯도 숨긴다</summary>
    public static bool WalkmanStowed;

    /// <summary>연습 되감기(튜토리얼): 되감아도 테이프의 되감기 횟수가 줄지 않고, 재생용 테이프도 계속 재생된다</summary>
    public static bool FreeRewind;
    /// <summary>컷신 건너뛰기(Space 길게 누르기, CutsceneSkip): 켜지면 남은 컷신의 기다림·연출을 곧바로 넘긴다. 컷신이 끝나면 꺼진다</summary>
    public static bool SkipRequested;
    /// <summary>연출로 시간을 붙잡아 둔 동안(실전 첫 되감기에서 R을 기다릴 때): 되감기 기록과 테이프 재생을 멈춘다.
    /// 기다린 시간이 기록되면 "5초 전"이 들키기 전이 아니라 기다리던 도중이 되어 버린다</summary>
    public static bool HoldTime;
    /// <summary>설명 팝업 같은 화면이 열려 있음: 게임이 멈추고 조작이 잠긴다</summary>
    public static bool MenuOpen;

    public static bool InputLocked => CutsceneActive || ControlLocked || MenuOpen;

    /// <summary>null이 아니면 화면 가운데에 편지를 크게 펼쳐 보여 준다(편지 뒷면 읽기)</summary>
    public static string LetterHeading, LetterText;

    /// <summary>null이 아니면 HUD 목표 문구를 이 값으로 바꾼다</summary>
    public static string Objective;
    /// <summary>목표 지점 표시(없으면 null)</summary>
    public static Transform Waypoint;
    /// <summary>지금 F로 쓸 수 있는 상호작용 대상(HUD가 안내 문구를 띄운다)</summary>
    public static StoryInteractable Nearby;
    /// <summary>3인칭 카메라 거리 덮어쓰기(0 이하면 기본값). 좁은 실내에서 쓴다</summary>
    public static float CameraDistance;
    /// <summary>화면 덮개(0 = 없음, 1 = 완전히 덮음). 장면 전환 페이드에 쓴다</summary>
    public static float Fade;
    public static Color FadeColor = Color.black;
    /// <summary>화면 가운데 큰 제목(예: "PROLOGUE"). null이면 없음</summary>
    public static string Title;
    /// <summary>지금 눌러야 할 키 안내(예: 실전 첫 되감기 "R 길게 누르기"). null이면 없음</summary>
    public static string Prompt;

    public static string Speaker { get; private set; }
    public static string Line { get; private set; }
    static float lineUntil;
    public static bool LineVisible => Line != null && Time.unscaledTime < lineUntil;

    /// <summary>자막을 띄운다. speaker가 비어 있으면 지문(괄호 설명)처럼 이름 없이 보인다.</summary>
    public static void Say(string speaker, string line, float seconds)
    {
        if (SkipRequested) return; // 건너뛰는 중에는 자막을 띄우지 않는다
        Speaker = speaker;
        Line = line;
        lineUntil = Time.unscaledTime + seconds;
    }

    public static void ClearLine() => lineUntil = 0f;

    public static void ResetAll()
    {
        CutsceneActive = ControlLocked = CombatLocked = RewindLocked = WalkmanStowed = MenuOpen = FreeRewind = SkipRequested = HoldTime = false;
        LetterHeading = LetterText = null;
        Objective = null;
        Waypoint = null;
        Nearby = null;
        CameraDistance = 0f;
        Fade = 0f;
        FadeColor = Color.black;
        Title = null;
        Prompt = null;
        Speaker = Line = null;
        lineUntil = 0f;
    }

    // 에디터에서 도메인 재로드 없이 플레이를 다시 시작해도 이전 값이 남지 않게 한다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() => ResetAll();
}
