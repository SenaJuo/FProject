using UnityEngine;

/// <summary>
/// 글꼴 예열(2026-10-13 최적화): 빌드 측정에서 순간 끊김(50~114ms)은 전부 한글 글자를 처음 쓸 때
/// 글꼴이 그 자리에서 글자 모양을 만들고(GlyphRasterizer) 글자 간격 표를 찾는(GetPairAdjustmentRecords) 비용이었다.
/// 게임에 나오는 글자 목록(Resources/GameChars.txt, 에디터 GameChars가 코드에서 모은다)을 켜자마자 몇 프레임에 나눠
/// 보통·굵은 글씨로 한 번씩 그려 두면, 그 뒤 자막·안내가 처음 떠도 멈추지 않는다.
/// </summary>
public class FontWarmup : MonoBehaviour
{
    const int PerFrame = 120; // 한 프레임에 준비할 글자 수(타이틀 화면에서 끊김이 보이지 않을 만큼)

    string chars;
    int at;
    GUIStyle normal, bold, wrap;
    static readonly GUIContent content = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var list = Resources.Load<TextAsset>("GameChars");
        if (!list) return;
        var go = new GameObject("FontWarmup");
        DontDestroyOnLoad(go);
        go.AddComponent<FontWarmup>().chars = list.text;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        normal ??= new GUIStyle(GUI.skin.label) { fontSize = 24, wordWrap = false };
        bold ??= new GUIStyle(normal) { fontStyle = FontStyle.Bold };
        int n = Mathf.Min(PerFrame, chars.Length - at);
        content.text = chars.Substring(at, n);
        // 투명하게 그린다: 글자 모양·간격 준비는 그리기에서 일어난다(화면 밖이면 잘려서 준비를 건너뛸 수 있다)
        var r = new Rect(0f, 0f, 6000f, 60f);
        var keep = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0f);
        GUI.Label(r, content, normal);
        GUI.Label(r, content, bold);
        GUI.color = keep;
        normal.CalcSize(content); bold.CalcSize(content);
        // 자막처럼 줄바꿈·색 태그가 있는 배치도 한 번씩(2026-10-14: 프롤로그 첫 자막 — 긴 문장이 처음 여러 줄로 꺾일 때 21ms 멈췄다)
        wrap ??= new GUIStyle(normal) { wordWrap = true, richText = true, alignment = TextAnchor.UpperCenter };
        content.text = "<color=#E8C46A>" + chars.Substring(at, Mathf.Min(4, n)) + "</color>   " + chars.Substring(at, n);
        var wr = new Rect(0f, 0f, 900f, 300f);
        GUI.color = new Color(1f, 1f, 1f, 0f);
        GUI.Label(wr, content, wrap);
        GUI.color = keep;
        wrap.CalcHeight(content, wr.width);
        at += n;
        if (at >= chars.Length) Destroy(gameObject);
    }
}
