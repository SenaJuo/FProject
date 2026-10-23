using UnityEngine;

/// <summary>
/// 경보: 켜면 실내 형광등이 붉은 비상등으로 바뀌어 천천히 맥박치고, 사이렌이 울린다.
/// 2장 구역 3(기록 조회에 걸린 뒤 경찰서 탈출). 되감아도 경보는 그대로다(이미 울린 일).
/// </summary>
public class AlarmLights : MonoBehaviour
{
    public Light[] lights = System.Array.Empty<Light>();
    [Tooltip("형광등 판(발광 재질) — 켜지면 붉은 재질로 바꾼다")]
    public Renderer[] panels = System.Array.Empty<Renderer>();
    public Material alarmPanel;
    public Color alarmColor = new(1f, 0.16f, 0.1f);
    [Tooltip("원래 밝기에 곱한다(맥박의 가장 밝은 때)")]
    public float intensityScale = 0.75f;
    [Tooltip("맥박 한 번(초)")]
    public float period = 1.4f;
    public AudioSource siren;
    [Tooltip("사이렌 크기 배율(밖으로 나가면 감독이 줄인다)")]
    public float sirenScale = 1f;

    public bool On { get; private set; }
    float[] baseIntensity;
    Color[] baseColor;
    Material[] basePanel;

    public void TurnOn()
    {
        if (On) return;
        On = true;
        baseIntensity = new float[lights.Length];
        baseColor = new Color[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            if (!lights[i]) continue;
            baseIntensity[i] = lights[i].intensity;
            baseColor[i] = lights[i].color;
            lights[i].color = alarmColor;
        }
        basePanel = new Material[panels.Length];
        for (int i = 0; i < panels.Length; i++) if (panels[i]) basePanel[i] = panels[i].sharedMaterial;
        if (alarmPanel) foreach (var r in panels) if (r) r.sharedMaterial = alarmPanel;
        if (siren)
        {
            siren.clip = ProceduralSfx.AlarmLoop; // VARCO 녹음(Resources/Sfx/World/Alarm)이 있으면 그것
            siren.loop = true;
            siren.Play();
        }
    }

    /// <summary>
    /// 경보를 거둔다(2026-10-23: 2장 2층에서 들켜 울린 경보를 되감아 없던 일로 할 때). 탈출 때의 경보는 끄지 않는다
    /// </summary>
    public void TurnOff()
    {
        if (!On) return;
        On = false;
        for (int i = 0; i < lights.Length; i++)
            if (lights[i]) { lights[i].intensity = baseIntensity[i]; lights[i].color = baseColor[i]; }
        for (int i = 0; i < panels.Length; i++) if (panels[i] && basePanel[i]) panels[i].sharedMaterial = basePanel[i];
        if (siren) siren.Stop();
    }

    void Update()
    {
        if (!On) return;
        // 사인 맥박: 어두운 때도 완전히 꺼지지 않게(길은 보여야 한다)
        float k = Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / period));
        for (int i = 0; i < lights.Length; i++)
            if (lights[i]) lights[i].intensity = baseIntensity[i] * intensityScale * k;
        if (siren) siren.volume = 0.32f * sirenScale * GameSettings.Gain(GameSettings.Channel.Sfx);
    }
}
