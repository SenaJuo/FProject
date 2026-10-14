using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 설정(2026-10-14 탭 설정 메뉴: 소리 · 화면 · 조작). PlayerPrefs에 저장돼 다음 실행에도 남는다.
/// 소리
///  · 전체: AudioListener.volume — 모든 소리
///  · 음악: 워크맨으로 듣는 테이프 음악
///  · 효과음: 타격·총성·발소리·문·되감기 등. 예전의 "환경음(비)"는 게임에서 비를 없애 타이틀 빗소리만 남았으므로 효과음에 합쳤다
///  밀대 값(0~1)은 귀에 들리는 크기에 맞춰 제곱해서 쓴다(50% ≈ 소리 세기 1/4 ≈ -12dB: 절반쯤 작게 들린다).
/// 화면: 화면 모드 · 해상도 · 화질(QualitySettings 0 낮음 / 1 높음) · 프레임 제한
/// 조작: 마우스 감도(배율) · 상하 반전
/// </summary>
public static class GameSettings
{
    // ---------- 소리 ----------

    public enum Channel { Master, Music, Sfx }

    public static readonly string[] Names = { "전체 음량", "음악 (워크맨)", "효과음" };
    static readonly string[] Keys = { "volume.master", "volume.music", "volume.sfx" };
    const float Default = 1f; // 처음에는 지금까지 들리던 그대로

    static readonly float[] values = { Default, Default, Default };
    static bool loaded;

    /// <summary>값이 바뀌었다(소리를 계속 내는 곳이 바로 따라가게)</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void OnPlayStart()
    {
        loaded = false;
        Load();
    }

    /// <summary>밀대 값(0~1)</summary>
    public static float Get(Channel channel)
    {
        Load();
        return values[(int)channel];
    }

    /// <summary>소리에 곱할 크기(밀대 값의 제곱)</summary>
    public static float Gain(Channel channel)
    {
        float v = Get(channel);
        return v * v;
    }

    public static void Set(Channel channel, float value)
    {
        Load();
        value = Mathf.Clamp01(Mathf.Round(value * 100f) / 100f);
        if (Mathf.Approximately(values[(int)channel], value)) return;
        values[(int)channel] = value;
        PlayerPrefs.SetFloat(Keys[(int)channel], value);
        if (channel == Channel.Master) AudioListener.volume = Gain(Channel.Master);
        Changed?.Invoke();
    }

    public static void ResetDefaults()
    {
        for (int i = 0; i < values.Length; i++) Set((Channel)i, Default);
    }

    // ---------- 화면 ----------

    public static readonly string[] WindowModeNames = { "전체 화면", "창 모드" };
    public static readonly string[] QualityNames = { "낮음", "높음" };
    public static readonly string[] FrameNames = { "모니터에 맞춤", "30", "60", "120", "제한 없음" };
    static readonly int[] FrameRates = { 0, 30, 60, 120, -1 }; // 0 = 수직동기화

    /// <summary>0 전체 화면, 1 창 모드</summary>
    public static int WindowMode => Screen.fullScreenMode == FullScreenMode.Windowed ? 1 : 0;
    public static int Quality { get; private set; } = 1;
    public static int Frame { get; private set; }

    /// <summary>고를 수 있는 해상도(가로 1024 이상, 같은 크기는 하나만, 작은 것부터)</summary>
    public static List<Vector2Int> Resolutions
    {
        get
        {
            if (resolutions != null) return resolutions;
            resolutions = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 1024 && !resolutions.Contains(v)) resolutions.Add(v);
            }
            var now = new Vector2Int(Screen.width, Screen.height);
            if (!resolutions.Contains(now)) resolutions.Add(now); // 에디터·특이한 창 크기
            resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return resolutions;
        }
    }
    static List<Vector2Int> resolutions;

    /// <summary>지금 해상도의 목록 번호(목록에 없으면 가장 가까운 것)</summary>
    public static int ResolutionIndex
    {
        get
        {
            var list = Resolutions;
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                float d = Mathf.Abs(list[i].x - Screen.width) + Mathf.Abs(list[i].y - Screen.height);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }

    public static string ResolutionName(int index)
    {
        var list = Resolutions;
        if (list.Count == 0) return $"{Screen.width} × {Screen.height}";
        var r = list[Mathf.Clamp(index, 0, list.Count - 1)];
        return $"{r.x} × {r.y}";
    }

    public static void SetWindowMode(int mode)
    {
        mode = Mathf.Clamp(mode, 0, 1);
        var r = Resolutions.Count > 0 ? Resolutions[ResolutionIndex] : new Vector2Int(Screen.width, Screen.height);
        Screen.SetResolution(r.x, r.y, mode == 0 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    public static void SetResolution(int index)
    {
        var list = Resolutions;
        if (list.Count == 0) return;
        var r = list[Mathf.Clamp(index, 0, list.Count - 1)];
        Screen.SetResolution(r.x, r.y, Screen.fullScreenMode);
    }

    public static void SetQuality(int level)
    {
        Quality = Mathf.Clamp(level, 0, QualityNames.Length - 1);
        PlayerPrefs.SetInt("display.quality", Quality);
        ApplyDisplay();
    }

    public static void SetFrame(int option)
    {
        Frame = Mathf.Clamp(option, 0, FrameNames.Length - 1);
        PlayerPrefs.SetInt("display.frame", Frame);
        ApplyDisplay();
    }

    /// <summary>화질·프레임 제한을 실제로 적용한다(화질을 바꾸면 그 단계의 수직동기화 값이 따라오므로 프레임은 그 뒤에)</summary>
    static void ApplyDisplay()
    {
        if (QualitySettings.GetQualityLevel() != Quality && Quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(Quality, true);
        int fps = FrameRates[Frame];
        QualitySettings.vSyncCount = fps == 0 ? 1 : 0;
        Application.targetFrameRate = fps > 0 ? fps : -1;
    }

    public static void ResetDisplay()
    {
        var native = Screen.currentResolution;
        Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        SetQuality(1);
        SetFrame(0);
    }

    // ---------- 조작 ----------

    public const float MouseMin = 0.2f, MouseMax = 3f;
    /// <summary>마우스(·패드) 시점 감도 배율. 1 = 기본</summary>
    public static float MouseSensitivity { get; private set; } = 1f;
    public static bool InvertY { get; private set; }

    public static void SetMouseSensitivity(float value)
    {
        MouseSensitivity = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, MouseMin, MouseMax);
        PlayerPrefs.SetFloat("input.mouse", MouseSensitivity);
    }

    public static void SetInvertY(bool on)
    {
        InvertY = on;
        PlayerPrefs.SetInt("input.invertY", on ? 1 : 0);
    }

    public static void ResetControls()
    {
        SetMouseSensitivity(1f);
        SetInvertY(false);
    }

    // ---------- 저장 ----------

    /// <summary>디스크에 쓴다(설정 창을 닫을 때)</summary>
    public static void Save() => PlayerPrefs.Save();

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        resolutions = null;
        for (int i = 0; i < values.Length; i++) values[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(Keys[i], Default));
        AudioListener.volume = Gain(Channel.Master);
        Quality = Mathf.Clamp(PlayerPrefs.GetInt("display.quality", 1), 0, QualityNames.Length - 1);
        Frame = Mathf.Clamp(PlayerPrefs.GetInt("display.frame", 0), 0, FrameNames.Length - 1);
        MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("input.mouse", 1f), MouseMin, MouseMax);
        InvertY = PlayerPrefs.GetInt("input.invertY", 0) == 1;
        ApplyDisplay(); // 화면 모드·해상도는 유니티가 스스로 기억한다
    }
}
