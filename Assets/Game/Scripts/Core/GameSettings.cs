using System;
using UnityEngine;

/// <summary>
/// 옵션(소리 크기). PlayerPrefs에 저장돼 다음 실행에도 남는다.
///  · 전체: AudioListener.volume — 모든 소리
///  · 음악: 워크맨으로 듣는 테이프 음악
///  · 효과음: 타격·총성·발소리·문·되감기 등 한 번씩 나는 소리
///  · 환경음: 빗소리처럼 깔리는 소리
/// 밀대 값(0~1)은 귀에 들리는 크기에 맞춰 제곱해서 쓴다(50% ≈ 소리 세기 1/4 ≈ -12dB: 절반쯤 작게 들린다).
/// </summary>
public static class GameSettings
{
    public enum Channel { Master, Music, Sfx, Ambience }

    public static readonly string[] Names = { "전체 음량", "음악 (워크맨)", "효과음", "환경음 (비)" };
    static readonly string[] Keys = { "volume.master", "volume.music", "volume.sfx", "volume.ambience" };
    const float Default = 1f; // 처음에는 지금까지 들리던 그대로

    static readonly float[] values = { Default, Default, Default, Default };
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

    /// <summary>디스크에 쓴다(옵션 창을 닫을 때)</summary>
    public static void Save() => PlayerPrefs.Save();

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 0; i < values.Length; i++) values[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(Keys[i], Default));
        AudioListener.volume = Gain(Channel.Master);
    }
}
