using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 비밀번호 패널(2026-10-13 "비밀번호 패널 같은 거 나와서 내가 직접 쳐서 들어가고"):
/// 숫자 키(또는 마우스)로 네 자리를 누르면 바로 맞는지 본다. 맞으면 닫히며 onOpen, 틀리면 빨갛게 깜빡이고 비운다.
/// Backspace 지우기, Esc·F 닫기. 열려 있는 동안은 조작이 잠긴다(시간은 흐른다).
/// </summary>
public class KeypadPanel : MonoBehaviour
{
    static KeypadPanel instance;
    public static bool IsOpen => instance && instance.open;

    bool open;
    string code = "", entered = "";
    Action onOpen;
    float wrongAt = -9f, openedAt;
    GUIStyle digit, button, display;

    /// <summary>패널을 연다. 맞는 번호를 누르면 onOpen</summary>
    public static void Open(string code, Action onOpen)
    {
        if (!instance) instance = new GameObject("KeypadPanel").AddComponent<KeypadPanel>();
        instance.code = code;
        instance.onOpen = onOpen;
        instance.entered = "";
        instance.open = true;
        instance.openedAt = Time.unscaledTime;
        StoryState.MenuOpen = true;
    }

    /// <summary>시험용: 번호를 한꺼번에 누른다</summary>
    public static void Type(string digits) { if (IsOpen) foreach (char c in digits) instance.Press(c); }

    void Close()
    {
        open = false;
        StoryState.MenuOpen = false;
        StoryState.MenuClosedFrame = Time.frameCount; // 같은 Esc로 일시정지가 열리지 않게
    }

    void Press(char c)
    {
        if (!open || entered.Length >= code.Length) return;
        entered += c;
        ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, Camera.main ? Camera.main.transform.position : Vector3.zero, 0.5f, 0f, 1.6f);
        if (entered.Length < code.Length) return;
        if (entered == code)
        {
            Close();
            onOpen?.Invoke();
        }
        else
        {
            wrongAt = Time.unscaledTime;
            ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, Camera.main ? Camera.main.transform.position : Vector3.zero, 0.7f, 0f, 0.5f);
        }
    }

    void Update()
    {
        if (!open) return;
        if (Time.unscaledTime - wrongAt > 0.45f && entered.Length >= code.Length && entered != code) entered = ""; // 틀린 번호는 깜빡인 뒤 비운다
        var k = Keyboard.current;
        if (k == null || Time.unscaledTime - openedAt < 0.15f) return; // 연 F가 바로 닫지 않게
        if (k.escapeKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame) { Close(); return; }
        if (k.backspaceKey.wasPressedThisFrame && entered.Length > 0 && Time.unscaledTime - wrongAt > 0.45f) entered = entered.Substring(0, entered.Length - 1);
        for (int i = 0; i <= 9; i++)
            if (k[(Key)((int)Key.Digit0 + i)].wasPressedThisFrame || k[(Key)((int)Key.Numpad0 + i)].wasPressedThisFrame) Press((char)('0' + i));
    }

    void OnGUI()
    {
        UiTheme.Begin(); // 게임 글꼴(Noto Sans KR)
        if (!open) return;
        GUI.depth = -25;
        if (Event.current.type == EventType.Layout) return;
        float s = UIText.Scale;
        if (digit == null)
        {
            digit = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            display = new GUIStyle(digit);
            button = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
        digit.fontSize = Mathf.RoundToInt(22 * s);
        display.fontSize = Mathf.RoundToInt(40 * s);
        button.fontSize = Mathf.RoundToInt(28 * s);

        // 새 꾸밈(2026-10-14): 둥근 판 · 녹색 표시창 · 둥근 단추(유니티 기본 회색 단추 대신)
        float w = 320 * s, h = 450 * s;
        var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        UiTheme.Window(r, s);

        // 표시창: 누른 자리는 ●, 남은 자리는 _
        bool wrong = Time.unscaledTime - wrongAt < 0.45f;
        var screen = new Rect(r.x + 24 * s, r.y + 24 * s, r.width - 48 * s, 76 * s);
        UiTheme.Panel(screen, wrong ? new Color(0.40f, 0.07f, 0.06f, 0.95f) : new Color(0.03f, 0.10f, 0.05f, 0.95f), s);
        UiTheme.Outline(screen, wrong ? new Color(1f, 0.4f, 0.35f, 0.5f) : new Color(0.4f, 1f, 0.55f, 0.25f));
        string shown = "";
        for (int i = 0; i < code.Length; i++) shown += (i < entered.Length ? entered[i].ToString() : "_") + (i < code.Length - 1 ? " " : "");
        display.normal.textColor = wrong ? new Color(1f, 0.5f, 0.45f) : new Color(0.5f, 1f, 0.6f);
        UIText.Label(screen, shown, display);

        // 3×4 단추
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "←", "0", "×" };
        float bw = (r.width - 48 * s - 2 * 12 * s) / 3f, bh = 64 * s;
        for (int i = 0; i < keys.Length; i++)
        {
            var b = new Rect(r.x + 24 * s + (i % 3) * (bw + 12 * s), screen.yMax + 22 * s + (i / 3) * (bh + 12 * s), bw, bh);
            if (!KeyButton(b, keys[i], s)) continue;
            if (keys[i] == "×") Close();
            else if (keys[i] == "←") { if (entered.Length > 0 && !wrong) entered = entered.Substring(0, entered.Length - 1); }
            else Press(keys[i][0]);
        }
    }

    /// <summary>둥근 단추: 마우스를 올리면 밝아지고, 누르면 true</summary>
    bool KeyButton(Rect b, string label, float s)
    {
        var e = Event.current;
        bool hover = b.Contains(e.mousePosition);
        UiTheme.Panel(b, hover ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.06f), s);
        if (hover) UiTheme.Outline(b, new Color(UiTheme.Amber.r, UiTheme.Amber.g, UiTheme.Amber.b, 0.7f));
        digit.fontSize = button.fontSize;
        digit.normal.textColor = label == "×" ? UiTheme.Tape : hover ? UiTheme.Amber : UiTheme.Paper;
        UIText.Label(b, label, digit);
        if (e.type != EventType.MouseDown || e.button != 0 || !hover) return false;
        e.Use();
        return true;
    }

    static void Fill(Rect r, Color c)
    {
        var keep = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = keep;
    }

    void OnDestroy() { if (open) StoryState.MenuOpen = false; }
}
