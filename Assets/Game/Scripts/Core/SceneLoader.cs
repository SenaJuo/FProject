using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 장면 전환(2026-10-14 최적화): 예전에는 LoadScene(동기)이라 큰 장면을 불러오는 몇 초 동안 게임 전체가 멈췄다(소리가 끊기고 창이 응답 없음처럼 된다).
/// 이제 뒤에서 불러오고(LoadSceneAsync), 그동안 화면을 검게 덮는다. 이전 장면은 시간을 멈추고 조작·되감기를 잠가
/// 검은 화면 뒤에서 아무 일도 일어나지 않게 한다(새 장면의 감독이 Awake에서 상태를 다시 맞춘다).
/// </summary>
public class SceneLoader : MonoBehaviour
{
    static SceneLoader instance;

    /// <summary>장면을 불러오는 중(일시정지 메뉴 등은 열리지 않는다)</summary>
    public static bool Busy { get; private set; }

    /// <summary>이 빌드에 들어 있는 장면인가(빌드에서 뺀 장면의 저장으로 '이어하기'를 하지 않게)</summary>
    public static bool Has(string scene) => Application.CanStreamedLevelBeLoaded(scene);

    /// <summary>불러오는 동안 화면을 덮는 색(엔딩 A는 하얗게 감긴 뒤라 흰색으로 덮는다)</summary>
    static Color cover = Color.black;

    public static void Load(string scene, Color coverColor)
    {
        cover = coverColor;
        Load(scene);
    }

    public static void Load(string scene)
    {
        if (Busy) return;
        if (!instance)
        {
            var go = new GameObject("SceneLoader");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SceneLoader>();
        }
        instance.StartCoroutine(instance.Run(scene));
    }

    IEnumerator Run(string scene)
    {
        Busy = true;
        StoryState.ControlLocked = true;
        StoryState.RewindLocked = true;
        Time.timeScale = 0f;
        yield return null; // 검은 화면을 먼저 한 번 그린다
        var priority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.High; // 화면이 검으니 불러오기에 힘을 쏟는다
        var op = SceneManager.LoadSceneAsync(scene);
        while (op != null && !op.isDone) yield return null;
        Application.backgroundLoadingPriority = priority;
        Time.timeScale = 1f;
        yield return null; // 새 장면의 첫 프레임까지 덮는다(장면이 스스로 페이드인한다)
        Busy = false;
        cover = Color.black;
    }

    void OnGUI()
    {
        UiTheme.Begin(); // 게임 글꼴(Noto Sans KR)
        if (!Busy) return;
        GUI.depth = -100; // 모든 화면 위에
        var keep = GUI.color;
        GUI.color = cover;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = keep;
    }
}
