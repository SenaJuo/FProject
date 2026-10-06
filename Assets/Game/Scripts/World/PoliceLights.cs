using UnityEngine;

/// <summary>순찰차 경광등: 빨강·파랑이 번갈아 깜빡인다(한 색이 켜진 동안 두 번 짧게 번쩍)</summary>
public class PoliceLights : MonoBehaviour
{
    public Light red, blue;
    public Renderer redLens, blueLens;
    public float intensity = 5f;
    [Tooltip("1초에 몇 번 색이 바뀌는가")]
    public float rate = 1.6f;

    MaterialPropertyBlock block;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Update()
    {
        float t = Time.time * rate + transform.position.x * 0.13f; // 차마다 박자가 조금씩 다르게
        bool redTurn = Mathf.Repeat(t, 1f) < 0.5f; // %는 음수에서 음수가 나와 깜빡이지 않는다
        float flash = Mathf.Repeat(t * 4f, 1f) < 0.55f ? 1f : 0.15f;
        Set(red, redLens, redTurn ? flash : 0f, new Color(1f, 0.08f, 0.05f));
        Set(blue, blueLens, redTurn ? 0f : flash, new Color(0.1f, 0.25f, 1f));
    }

    void Set(Light light, Renderer lens, float k, Color color)
    {
        if (light) light.intensity = intensity * k;
        if (!lens) return;
        block ??= new MaterialPropertyBlock();
        lens.GetPropertyBlock(block);
        block.SetColor(EmissionId, color * (0.3f + 6f * k));
        lens.SetPropertyBlock(block);
    }
}
