using UnityEngine;

/// <summary>MaterialPropertyBlock으로 피격 플래시와 공격 예고 색조를 입힌다. 그래서 머티리얼은 공유 상태로 유지된다.</summary>
public class BodyTint : MonoBehaviour
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public Renderer[] renderers;
    public Color flashColor = Color.white;
    public float flashDuration = 0.08f;

    MaterialPropertyBlock block;
    Color[] baseColors;
    Color tintColor;
    float tintAmount;
    float flashUntil;

    void Awake()
    {
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i].sharedMaterial.GetColor(BaseColor);

        if (TryGetComponent(out Health health)) health.Damaged += (_, _) => Flash();
    }

    // 실제 시간을 쓴다. 게임 시간이 멈춘 동안(되감기 재생, 상점) 플래시가 켜진 채로 굳지 않게 하기 위해서다.
    public void Flash() => flashUntil = Time.unscaledTime + flashDuration;

    public void SetTint(Color color, float amount)
    {
        tintColor = color;
        tintAmount = amount;
    }

    void LateUpdate()
    {
        bool flashing = Time.unscaledTime < flashUntil;
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(block);
            block.SetColor(BaseColor, flashing ? flashColor : Color.Lerp(baseColors[i], tintColor, tintAmount));
            renderers[i].SetPropertyBlock(block);
        }
    }
}
