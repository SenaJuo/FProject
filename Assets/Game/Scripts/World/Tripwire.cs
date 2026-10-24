using System;
using UnityEngine;

/// <summary>
/// 문턱의 인계철선(2026-10-23, 3장 펜트하우스 입구). 발목 높이의 가는 철선을 건드리면 옆의 폭약이 터져 레이가 쓰러진다.
/// 어두운 데서는 거의 보이지 않는다 — 한 번 밟아 보고 되감아야 안다(레이만 기억한다).
/// 알고 나면(Known) 철선이 호박색으로 보이고, 가까이 가면 F로 끊을 수 있다. 뛰어서 넘어가도 된다.
/// 되감으면 터지기 전으로(철선이 다시 걸린다), 끊은 것도 끊기 전으로 돌아간다.
/// 장면 빌더 대신 감독이 Create로 만든다(문 위치만 알면 된다).
/// </summary>
public class Tripwire : MonoBehaviour, IRewindable
{
    /// <summary>철선을 건드려 터졌다(되감아도 레이는 기억한다 — 감독이 Known을 켠다)</summary>
    public event Action Tripped;

    public bool Armed { get; private set; } = true;
    public bool Cut { get; private set; }

    bool known;
    /// <summary>레이가 철선을 안다: 철선이 보이고 끊을 수 있다(되감지 않는다)</summary>
    public bool Known
    {
        get => known;
        set { known = value; Refresh(); }
    }

    Vector3 a, b, charge;
    LineRenderer wire;
    Material wireMat;
    Light led, flash;
    StoryInteractable cutUse;
    MeshRenderer strip;
    float flashUntil;

    static readonly Color Dark = new(0.08f, 0.08f, 0.08f, 1f), Glow = new(1f, 0.7f, 0.3f, 1f);

    /// <summary>a~b 사이(발목 높이)에 철선을, charge 자리에 폭약을 단다</summary>
    public static Tripwire Create(Vector3 a, Vector3 b, Vector3 charge)
    {
        var go = new GameObject("Tripwire");
        go.transform.position = (a + b) * 0.5f;
        var t = go.AddComponent<Tripwire>();
        t.Build(a, b, charge);
        return t;
    }

    void Build(Vector3 from, Vector3 to, Vector3 at)
    {
        a = from; b = to; charge = at;
        // 먼저 돌려 둔다: 자식(폭약·불)을 붙인 뒤에 돌리면 자식도 함께 돌아 문 밖으로 나갔다
        Vector3 along = b - a;
        transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, along.normalized), Vector3.up);
        wire = gameObject.AddComponent<LineRenderer>();
        wire.positionCount = 2;
        wire.SetPositions(new[] { a, b });
        wire.useWorldSpace = true;
        wire.widthMultiplier = 0.006f;
        wire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wireMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "TripwireWire" };
        wire.sharedMaterial = wireMat;

        // 폭약: 문틀 안쪽 구석의 작은 상자 + 깜박이는 붉은 불
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "TripwireCharge";
        box.transform.SetParent(transform, true);
        box.transform.position = charge;
        box.transform.localScale = new Vector3(0.24f, 0.16f, 0.1f);
        Destroy(box.GetComponent<Collider>());
        var mr = box.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TripwireCharge" };
        mr.sharedMaterial.SetColor("_BaseColor", new Color(0.18f, 0.2f, 0.14f));
        led = new GameObject("TripwireLed").AddComponent<Light>();
        led.transform.SetParent(transform, true);
        led.transform.position = charge + Vector3.up * 0.12f;
        led.type = LightType.Point;
        led.color = new Color(1f, 0.1f, 0.05f);
        led.range = 0.7f;
        led.intensity = 0f;
        led.shadows = LightShadows.None;

        flash = new GameObject("TripwireFlash").AddComponent<Light>();
        flash.transform.SetParent(transform, true);
        flash.transform.position = charge + Vector3.up * 0.6f;
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.55f, 0.2f);
        flash.range = 14f;
        flash.intensity = 0f;
        flash.shadows = LightShadows.None;

        // 기억의 띠: 알고 나면 철선 아래 바닥이 호박색으로(순찰 시야를 보여 주는 되감기 기억과 같은 색)
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "TripwireMemory";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetPositionAndRotation(new Vector3(transform.position.x, Mathf.Min(a.y, b.y) - 0.1f, transform.position.z),
            Quaternion.LookRotation(Vector3.down, (b - a).normalized));
        quad.transform.localScale = new Vector3(0.6f, (b - a).magnitude + 0.2f, 1f);
        quad.transform.SetParent(transform, true);
        strip = quad.GetComponent<MeshRenderer>();
        strip.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        strip.receiveShadows = false;
        var memory = Resources.Load<Material>("Materials/RewindMemory");
        if (memory) strip.sharedMaterial = memory;
        var block = new MaterialPropertyBlock();
        // 가운데만 빛나고 가장자리는 스러진다: 고른 색 사각형은 어두운 바닥에서 판자처럼 보였다
        block.SetColor("_BaseColor", new Color(1f, 0.62f, 0.25f, 0.42f));
        block.SetTexture("_BaseMap", SoftStrip());
        strip.SetPropertyBlock(block);

        // 건드리는 판정: 철선을 따라 발목 높이까지만(뛰어 넘으면 닿지 않는다)
        var trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = transform.InverseTransformPoint(new Vector3(transform.position.x, Mathf.Min(a.y, b.y) + 0.12f, transform.position.z));
        trigger.size = new Vector3(along.magnitude, 0.36f, 0.3f);

        // 끊기: 알고 난 뒤에만
        var use = new GameObject("TripwireCut");
        use.transform.SetParent(transform, false);
        use.transform.position = transform.position + Vector3.up * 0.8f;
        var c = use.AddComponent<SphereCollider>();
        c.radius = 1.6f;
        cutUse = use.AddComponent<StoryInteractable>();
        cutUse.prompt = "철선을 끊는다";
        cutUse.oneShot = false;
        cutUse.Used += () => { if (Armed && !Cut && known) { Cut = true; ProceduralSfx.PlayAt(ProceduralSfx.ButtonClick, transform.position, 0.6f, 0.8f, 0.6f); Refresh(); } };

        TimeRewind.Register(this);
        GameManager.PlayerRespawned += Rearm;
        Refresh();
    }

    void OnDestroy()
    {
        TimeRewind.Unregister(this);
        GameManager.PlayerRespawned -= Rearm;
    }

    /// <summary>쓰러져 저장 지점부터 다시: 철선도 다시 걸려 있다(레이는 안다 — Known은 그대로)</summary>
    void Rearm()
    {
        Armed = true;
        Cut = false;
        Refresh();
    }

    static Texture2D softStrip;

    /// <summary>띠 무늬: 폭(u)은 가운데가 진하고 양옆으로 스러지며, 길이(v)는 양 끝에서 스러진다(흰색 + 알파)</summary>
    static Texture2D SoftStrip()
    {
        if (softStrip) return softStrip;
        const int n = 32;
        softStrip = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "TripwireSoftStrip", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float across = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
            float along = Mathf.SmoothStep(0f, 1f, Mathf.Min(v, 1f - v) / 0.12f);
            px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * across * along));
        }
        softStrip.SetPixels32(px);
        softStrip.Apply(false, true);
        return softStrip;
    }

    void Refresh()
    {
        bool live = Armed && !Cut;
        if (wire)
        {
            wire.enabled = Armed; // 끊은 철선은 늘어진 채 남는다(색만 어둡게)
            wire.widthMultiplier = known && live ? 0.03f : 0.006f;
            if (wireMat) wireMat.SetColor("_BaseColor", known && live ? Glow : Dark);
            if (Cut) wire.SetPositions(new[] { a, Vector3.Lerp(a, b, 0.45f) + Vector3.down * Mathf.Max(0f, a.y - 0.02f) });
            else wire.SetPositions(new[] { a, b });
        }
        if (cutUse) cutUse.gameObject.SetActive(known && live);
        if (strip) strip.enabled = known && live;
    }

    void Update()
    {
        if (led) led.intensity = Armed && !Cut && Mathf.Repeat(Time.time, 1.2f) < 0.12f ? 0.6f : 0f;
        if (flash) flash.intensity = Time.time < flashUntil ? Mathf.Lerp(0f, 60f, (flashUntil - Time.time) / 0.5f) : 0f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Armed || Cut || TimeRewind.PlayingBack || !other.TryGetComponent(out PlayerController player)) return;
        Explode(player);
    }

    void Explode(PlayerController player)
    {
        Armed = false;
        Refresh();
        flashUntil = Time.time + 0.5f;
        ProceduralSfx.PlayAt(ProceduralSfx.Explosion, charge, 1f, 0.5f);
        var hp = player.GetComponent<Health>();
        hp.Invulnerable = false;
        hp.GrantInvulnerability(0f);
        hp.floor = 0;
        hp.TakeDamage(9999, charge);
        Tripped?.Invoke();
    }

    struct Snapshot { public bool armed, cut; }

    public object CaptureState() => new Snapshot { armed = Armed, cut = Cut };

    public void RestoreState(object state, bool preview)
    {
        if (preview) return;
        var s = (Snapshot)state;
        Armed = s.armed;
        Cut = s.cut;
        Refresh();
    }
}
