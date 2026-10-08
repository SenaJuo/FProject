using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 세상 효과음(총성·문·쓰러짐·차 문·빗소리 등). 실제 녹음 같은 음원(VARCO Sound로 만든 Resources/Sfx/World/{이름})이
/// 있으면 그것을 쓰고, 없을 때만 코드로 합성한 소리를 쓴다. 한 번 불러온 클립은 다시 쓴다.
/// </summary>
public static class ProceduralSfx
{
    const int Rate = 44100;
    static readonly Dictionary<string, AudioClip> cache = new();

    public static AudioClip Gunshot => Get("Gunshot", 1.6f, (t, rng) =>
    {
        // 날카로운 파열음(잡음) + 낮은 쿵 소리 + 긴 잔향(도시 골목의 메아리)
        float crack = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 38f);
        float thump = Mathf.Sin(2 * Mathf.PI * 62f * t) * Mathf.Exp(-t * 9f) * 0.9f;
        float tail = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 3.2f) * 0.18f;
        return Mathf.Clamp(crack + thump + tail, -1f, 1f);
    });

    public static AudioClip DoorOpen => Get("DoorOpen", 1.1f, (t, rng) =>
    {
        // 손잡이 걸쇠 '딸깍' 두 번 + 경첩이 도는 짧은 마찰음
        float click = Click(t, 0.02f) + Click(t, 0.09f) * 0.7f;
        float creak = t > 0.15f && t < 0.85f
            ? Mathf.Sin(2 * Mathf.PI * (180f + 90f * Mathf.Sin(t * 9f)) * t) * 0.12f * Mathf.Sin(Mathf.PI * (t - 0.15f) / 0.7f)
            : 0f;
        float rumble = (float)(rng.NextDouble() * 2 - 1) * 0.04f * Mathf.Exp(-Mathf.Abs(t - 0.5f) * 6f);
        return Mathf.Clamp(click + creak + rumble, -1f, 1f);
    });

    /// <summary>집 밖에서 차 문이 닫히는 소리(음원이 없으면 쓰러짐과 같은 둔탁한 소리)</summary>
    public static AudioClip CarDoor => Get("CarDoor", 0.7f, (t, rng) =>
    {
        float thud = Mathf.Sin(2 * Mathf.PI * 55f * t) * Mathf.Exp(-t * 12f);
        return Mathf.Clamp(thud + (float)(rng.NextDouble() * 2 - 1) * 0.2f * Mathf.Exp(-t * 20f), -1f, 1f);
    });

    public static AudioClip BodyFall => Get("BodyFall", 0.7f, (t, rng) =>
    {
        float thud = Mathf.Sin(2 * Mathf.PI * 48f * t) * Mathf.Exp(-t * 11f);
        float cloth = (float)(rng.NextDouble() * 2 - 1) * 0.25f * Mathf.Exp(-t * 14f);
        return Mathf.Clamp(thud + cloth, -1f, 1f);
    });

    public static AudioClip PlasticHit => Get("PlasticHit", 0.35f, (t, rng) =>
    {
        // 카세트 플레이어가 바닥에 떨어지는 딱딱한 플라스틱 소리
        float knock = Mathf.Sin(2 * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 60f) * 0.6f;
        float body = Mathf.Sin(2 * Mathf.PI * 340f * t) * Mathf.Exp(-t * 25f) * 0.5f;
        return Mathf.Clamp(knock + body + (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 80f) * 0.3f, -1f, 1f);
    });

    public static AudioClip ButtonClick => Get("ButtonClick", 0.25f, (t, rng) =>
    {
        // 카세트 플레이어의 기계식 버튼: 짧은 '찰'(눌림) + 낮은 '칵'(걸림)
        float press = Click(t, 0.005f);
        float latch = Mathf.Sin(2 * Mathf.PI * 520f * Mathf.Max(0f, t - 0.05f)) * Mathf.Exp(-Mathf.Max(0f, t - 0.05f) * 45f) * (t > 0.05f ? 0.5f : 0f);
        return Mathf.Clamp(press + latch, -1f, 1f);
    });

    /// <summary>끊김 없이 반복되는 빗소리(잡음을 낮은 주파수 쪽으로 걸러 '쏴아' 소리로 만든다)</summary>
    public static AudioClip RainLoop
    {
        get
        {
            if (cache.TryGetValue("RainLoop", out var clip) && clip) return clip;
            if (Recorded("RainLoop") is AudioClip rec) return cache["RainLoop"] = rec;
            int n = Rate * 4;
            var data = new float[n];
            var rng = new System.Random(7);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.35f;
                lp2 += (lp - lp2) * 0.5f;
                // 가끔 처마에서 떨어지는 굵은 빗방울
                float drop = rng.NextDouble() < 0.0006 ? 0.6f : 0f;
                data[i] = lp2 * 0.5f + drop * (float)(rng.NextDouble() * 2 - 1);
            }
            // 이음매가 들리지 않게 앞뒤 0.2초를 겹쳐 섞는다
            int fade = Rate / 5;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = data[i] * w + data[n - fade + i] * (1f - w);
            }
            System.Array.Resize(ref data, n - fade);
            clip = AudioClip.Create("RainLoop", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            cache["RainLoop"] = clip;
            return clip;
        }
    }

    /// <summary>3D 위치에서 한 번 재생(volume 1 = 원래 크기)</summary>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float spatial = 0.6f, float pitch = 1f)
    {
        var go = new GameObject("Sfx_" + clip.name);
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume * GameSettings.Gain(GameSettings.Channel.Sfx); // 옵션의 효과음 크기
        source.spatialBlend = spatial;
        source.minDistance = 4f;
        source.maxDistance = 60f;
        source.ignoreListenerPause = true;
        source.pitch = pitch;
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    // ---------- 타격(HitFeel의 예비 소리: 실제 음원이 Resources/Sfx/Hit에 있으면 그쪽을 쓴다) ----------

    public static AudioClip Hit(string name) => name switch
    {
        "whoosh" => Get("whoosh", 0.22f, (t, rng) => Noise(rng) * Bell(t, 0.09f, 0.07f) * 0.35f),
        "whoosh_heavy" => Get("whoosh_heavy", 0.32f, (t, rng) => Noise(rng) * Bell(t, 0.14f, 0.1f) * 0.45f),
        "impact_light" => Get("impact_light", 0.25f, (t, rng) => Punch(t, rng, 110f, 0.7f)),
        "impact_mid" => Get("impact_mid", 0.32f, (t, rng) => Punch(t, rng, 85f, 0.9f)),
        "impact_heavy" => Get("impact_heavy", 0.45f, (t, rng) => Punch(t, rng, 60f, 1.1f)),
        "impact_bullet" => Get("impact_bullet", 0.3f, (t, rng) => Mathf.Clamp(Noise(rng) * Mathf.Exp(-t * 60f) + Mathf.Sin(2 * Mathf.PI * 70f * t) * Mathf.Exp(-t * 18f) * 0.8f, -1f, 1f)),
        "grunt" => Get("grunt", 0.3f, (t, rng) => Voice(t, rng, 115f)),
        "grunt_player" => Get("grunt_player", 0.3f, (t, rng) => Voice(t, rng, 130f)),
        "tape_stretch" => Get("tape_stretch", 0.9f, (t, rng) =>
            Mathf.Sin(2 * Mathf.PI * (420f - 300f * t) * t) * 0.3f * Mathf.Sin(Mathf.PI * t / 0.9f) + Noise(rng) * 0.04f),
        _ => null,
    };

    static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2 - 1);
    static float Bell(float t, float at, float width) => Mathf.Exp(-Mathf.Pow((t - at) / width, 2f));
    /// <summary>둔탁한 타격: 짧은 파열(잡음) + 낮은 쿵 + 살·옷이 눌리는 중간 소리</summary>
    static float Punch(float t, System.Random rng, float low, float gain)
    {
        float crack = Noise(rng) * Mathf.Exp(-t * 90f);
        float thump = Mathf.Sin(2 * Mathf.PI * low * t * (1f + 0.6f * Mathf.Exp(-t * 30f))) * Mathf.Exp(-t * 16f);
        float body = Noise(rng) * Mathf.Exp(-t * 25f) * 0.35f;
        return Mathf.Clamp((crack * 0.6f + thump + body) * gain, -1f, 1f);
    }
    /// <summary>짧은 '흡' 신음: 낮은 성대음 + 숨</summary>
    static float Voice(float t, System.Random rng, float f0)
    {
        float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.28f)) * Mathf.Exp(-t * 4f);
        float tone = Mathf.Sin(2 * Mathf.PI * f0 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * f0 * 2.02f * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * f0 * 3.1f * t);
        return Mathf.Clamp((tone * 0.25f + Noise(rng) * 0.12f) * env, -1f, 1f);
    }

    static float Click(float t, float at)
    {
        float d = t - at;
        return d < 0f ? 0f : Mathf.Sin(2 * Mathf.PI * 2400f * d) * Mathf.Exp(-d * 400f) * 0.8f;
    }

    /// <summary>VARCO Sound로 만든 실제 음원(Resources/Sfx/World/{이름}). 없으면 null</summary>
    public static AudioClip Recorded(string name) => Resources.Load<AudioClip>("Sfx/World/" + name);

    static AudioClip Get(string name, float seconds, System.Func<float, System.Random, float> sample)
    {
        if (cache.TryGetValue(name, out var clip) && clip) return clip;
        if (Recorded(name) is AudioClip rec) return cache[name] = rec;
        int n = Mathf.CeilToInt(Rate * seconds);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++) data[i] = sample(i / (float)Rate, rng);
        clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        cache[name] = clip;
        return clip;
    }
}
