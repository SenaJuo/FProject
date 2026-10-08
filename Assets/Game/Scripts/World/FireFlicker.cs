using UnityEngine;

/// <summary>드럼통 불: 빛 세기와 높이가 불규칙하게 흔들린다(불꽃 입자는 따로). 꺼 두면(enabled = false) 빛도 꺼진다</summary>
[RequireComponent(typeof(Light))]
public class FireFlicker : MonoBehaviour
{
    public float baseIntensity = 6f;
    public float amount = 0.35f;
    Light lamp;
    Vector3 basePos;
    float seed;

    void Awake()
    {
        lamp = GetComponent<Light>();
        basePos = transform.localPosition;
        seed = Random.value * 100f;
    }

    void OnEnable() { if (lamp) lamp.enabled = true; }
    void OnDisable() { if (lamp) lamp.enabled = false; }

    void Update()
    {
        float t = Time.time * 9f + seed;
        float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, seed + 3f) * 0.3f;
        lamp.intensity = baseIntensity * (1f - amount + n * amount * 2f);
        transform.localPosition = basePos + new Vector3(0f, n * 0.08f, 0f);
    }
}
