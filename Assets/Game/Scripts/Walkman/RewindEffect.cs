using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>테이프 되감기 연출과 소리: 색이 바래고 일그러진 거친 화면과, 절차적으로 만든 되감기 윙 소리.</summary>
public class RewindEffect : MonoBehaviour
{
    Volume volume;
    AudioSource whirSource, clickSource;
    AudioClip clunk;

    public float Weight => volume.weight;

    void Awake()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var color = profile.Add<ColorAdjustments>(true);
        color.saturation.Override(-85f);
        color.contrast.Override(25f);
        color.colorFilter.Override(new Color(0.85f, 0.95f, 1f));
        profile.Add<ChromaticAberration>(true).intensity.Override(1f);
        profile.Add<LensDistortion>(true).intensity.Override(-0.35f);
        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Large01);
        grain.intensity.Override(1f);

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 0f;
        volume.sharedProfile = profile;

        whirSource = gameObject.AddComponent<AudioSource>();
        whirSource.clip = ProceduralSfx.Recorded("TapeRewind") ?? CreateWhir(2f); // 실제 음원(VARCO)이 있으면 그것을
        whirSource.loop = true;
        whirSource.playOnAwake = false;
        clickSource = gameObject.AddComponent<AudioSource>();
        clickSource.playOnAwake = false;
        clunk = ProceduralSfx.Recorded("TapeStop") ?? CreateClunk();
    }

    public void SetWeight(float weight) => volume.weight = weight;

    public void StartWhir()
    {
        whirSource.pitch = 1f;
        whirSource.volume = GameSettings.Gain(GameSettings.Channel.Sfx); // 옵션의 효과음 크기
        whirSource.Play();
    }

    public void StopWhir()
    {
        whirSource.Stop();
        clickSource.PlayOneShot(clunk, GameSettings.Gain(GameSettings.Channel.Sfx));
    }

    void Update()
    {
        // 버튼을 오래 누를수록 테이프가 빨리 돈다.
        if (whirSource.isPlaying) whirSource.pitch = Mathf.Min(1.8f, whirSource.pitch + Time.unscaledDeltaTime * 0.4f);
    }

    const int SampleRate = 44100;

    static AudioClip CreateWhir(float seconds)
    {
        int count = (int)(SampleRate * seconds);
        var data = new float[count];
        var rng = new System.Random(7);
        float phase = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float frequency = 1400f + Mathf.Sin(t * Mathf.PI * 2f * 6f) * 250f; // 테이프 음 떨림
            phase = (phase + frequency / SampleRate) % 1f;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            data[i] = ((phase * 2f - 1f) * 0.12f + noise * 0.08f) * (0.8f + 0.2f * Mathf.Sin(t * Mathf.PI * 2f * 23f));
        }
        var clip = AudioClip.Create("TapeRewind", count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip CreateClunk()
    {
        int count = SampleRate / 6;
        var data = new float[count];
        var rng = new System.Random(3);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float body = Mathf.Sin(t * Mathf.PI * 2f * 110f) * Mathf.Exp(-t * 30f);
            float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 400f);
            data[i] = body * 0.6f + click * 0.5f;
        }
        var clip = AudioClip.Create("TapeStop", count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
