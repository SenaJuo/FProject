using System.Collections;
using UnityEngine;

/// <summary>밤 → 새벽: 하늘(HarborSky)·안개·주변광·달빛을 함께 바꾼다. 감독이 Dawn(seconds)을 부른다</summary>
public class SkyCycle : MonoBehaviour
{
    public Material sky;
    public Light moon;
    [Tooltip("하늘만 담는 반사 탐침: 하늘이 바뀌면 다시 찍어 바다에 비치는 하늘도 바뀐다")]
    public ReflectionProbe probe;
    float lastProbe = -1f;
    // 밤 값은 너무 어둡다는 지적(2026-10-12)으로 올렸다: BrightnessPass와 같은 값
    public Color nightFog = new(0.07f, 0.078f, 0.105f), dawnFog = new(0.55f, 0.42f, 0.4f);
    public Color nightAmbient = new(0.4f, 0.41f, 0.48f), dawnAmbient = new(0.52f, 0.48f, 0.52f);
    public float nightFogDensity = 0.011f, dawnFogDensity = 0.006f;
    public float nightMoon = 0.75f, dawnMoon = 1.1f;

    static readonly int DawnId = Shader.PropertyToID("_Dawn");

    void Start() { Set(0f); if (probe) probe.RenderProbe(); }

    public void Set(float k)
    {
        if (sky) sky.SetFloat(DawnId, k);
        RenderSettings.fogColor = Color.Lerp(nightFog, dawnFog, k);
        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dawnFogDensity, k);
        RenderSettings.ambientLight = Color.Lerp(nightAmbient, dawnAmbient, k);
        if (moon)
        {
            // 달빛 → 수평선 가까이 낮게 뜬 해(바다 쪽, +z)
            moon.color = Color.Lerp(new Color(0.55f, 0.64f, 0.85f), new Color(1f, 0.66f, 0.42f), k);
            moon.intensity = Mathf.Lerp(nightMoon, dawnMoon, k);
            moon.transform.rotation = Quaternion.Slerp(Quaternion.Euler(38f, -35f, 0f), Quaternion.Euler(4f, 199f, 0f), k);
        }
        if (probe && Mathf.Abs(k - lastProbe) > 0.04f) { lastProbe = k; probe.RenderProbe(); }
    }

    public IEnumerator Dawn(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            Set(Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        Set(1f);
    }
}
