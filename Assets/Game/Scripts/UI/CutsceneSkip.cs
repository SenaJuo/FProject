using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 컷신 건너뛰기: 컷신 중에 Space를 누르고 있으면 오른쪽 아래에 "건너뛰기" 단추가 뜨고 둘레의 원이 채워진다.
/// 원이 다 차면(HoldSeconds) StoryState.SkipRequested가 켜지고, 감독(PrologueDirector)이 남은 컷신을 곧바로 넘긴다.
/// 다 차기 전에 손을 떼면 원이 다시 비워지고 건너뛰지 않는다.
/// </summary>
public class CutsceneSkip : MonoBehaviour
{
    public const float HoldSeconds = 1.2f;

    /// <summary>시험용: 키보드 대신 누르고 있는 것으로 친다</summary>
    public static bool SimulateHold;
    /// <summary>지금 채워진 비율(0~1). 시험·화면 표시용</summary>
    public static float Progress { get; private set; }

    static CutsceneSkip instance;
    float shown;          // 단추가 보이는 정도(누르면 나타나고, 떼면 서서히 사라진다)
    Texture2D ring, dot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() { SimulateHold = false; Progress = 0f; }

    GUIStyle keyStyle, labelStyle;
    float styleScale = -1f;

    void Awake() => useGUILayout = false; // GUILayout을 쓰지 않는다

    public static void Ensure()
    {
        if (!instance) instance = new GameObject("CutsceneSkip").AddComponent<CutsceneSkip>();
    }

    void Update()
    {
        bool can = StoryState.CutsceneActive && !StoryState.SkipRequested;
        bool held = can && (SimulateHold || (Keyboard.current != null && Keyboard.current.spaceKey.isPressed));
        if (held)
        {
            Progress += Time.unscaledDeltaTime / HoldSeconds;
            if (Progress >= 1f)
            {
                Progress = 0f;
                StoryState.SkipRequested = true;
                StoryState.ClearLine();
            }
        }
        else Progress = 0f; // 손을 떼면 처음부터
        shown = Mathf.MoveTowards(shown, held ? 1f : 0f, Time.unscaledDeltaTime * (held ? 8f : 3f));
    }

    void OnGUI()
    {
        if (shown <= 0.01f) return;
        float s = Screen.height / 1080f;
        GUI.depth = -20;
        if (!ring) { ring = MakeRing(128, 0.78f); dot = MakeRing(128, 0f); }

        float r = 30f * s;
        var centre = new Vector2(Screen.width - 90f * s, Screen.height - 82f * s);
        var box = new Rect(centre.x - r, centre.y - r, r * 2f, r * 2f);
        // 바탕 원 + 채워지는 둘레(작은 점을 원 둘레에 촘촘히 찍는다)
        GUI.color = new Color(0f, 0f, 0f, 0.55f * shown);
        GUI.DrawTexture(new Rect(box.x - 4 * s, box.y - 4 * s, box.width + 8 * s, box.height + 8 * s), dot);
        GUI.color = new Color(1f, 1f, 1f, 0.25f * shown);
        GUI.DrawTexture(box, ring);
        GUI.color = new Color(0.95f, 0.78f, 0.35f, shown);
        int steps = Mathf.CeilToInt(96 * Progress);
        float thick = 5.5f * s;
        for (int i = 0; i < steps; i++)
        {
            float a = (i / 96f) * Mathf.PI * 2f - Mathf.PI / 2f; // 12시 방향부터 시계 방향
            var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r - thick * 0.6f);
            GUI.DrawTexture(new Rect(p.x - thick / 2f, p.y - thick / 2f, thick, thick), dot);
        }
        GUI.color = Color.white;

        if (!Mathf.Approximately(styleScale, s))
        {
            styleScale = s;
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * s), alignment = TextAnchor.MiddleRight };
        }
        var key = keyStyle;
        key.normal.textColor = new Color(1f, 1f, 1f, shown);
        GUI.Label(box, "Space", key);
        var label = labelStyle;
        label.normal.textColor = new Color(0f, 0f, 0f, 0.6f * shown);
        var lr = new Rect(box.x - 220 * s, centre.y - 18 * s, 200 * s, 36 * s);
        GUI.Label(new Rect(lr.x + 2, lr.y + 2, lr.width, lr.height), "건너뛰기", label);
        label.normal.textColor = new Color(0.92f, 0.9f, 0.85f, shown);
        GUI.Label(lr, "건너뛰기", label);
    }

    /// <summary>inner = 0이면 꽉 찬 원, 0~1이면 그만큼 속이 빈 고리</summary>
    static Texture2D MakeRing(int size, float inner)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float R = size / 2f, Ri = R * inner;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(R, R));
            float a = Mathf.Clamp01(R - d) * (inner > 0f ? Mathf.Clamp01(d - Ri) : 1f);
            t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        t.Apply();
        return t;
    }
}
