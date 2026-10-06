using UnityEngine;

/// <summary>MaterialPropertyBlock으로 피격 플래시와 공격 예고 색조를 입힌다. 그래서 머티리얼은 공유 상태로 유지된다.</summary>
public class BodyTint : MonoBehaviour
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public Renderer[] renderers;
    public Color flashColor = Color.white;
    public float flashDuration = 0.08f;

    MaterialPropertyBlock block;
    Color[] baseColors, applied;
    Color tintColor;
    float tintAmount;
    float flashUntil;

    void Awake()
    {
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length];
        applied = new Color[renderers.Length];
        for (int i = 0; i < applied.Length; i++) applied[i] = new Color(-1f, -1f, -1f, -1f); // 처음 한 번은 꼭 쓴다
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
            // 색이 바뀔 때만 쓴다(캐릭터마다 매 프레임 렌더러 수만큼 블록을 읽고 쓰던 비용을 없앤다)
            Color c = flashing ? flashColor : Color.Lerp(baseColors[i], tintColor, tintAmount);
            if (c == applied[i] || !renderers[i]) continue;
            applied[i] = c;
            renderers[i].GetPropertyBlock(block);
            block.SetColor(BaseColor, c);
            renderers[i].SetPropertyBlock(block);
        }
    }
}
