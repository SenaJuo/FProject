using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 되감기의 기억: 지워진 시간은 레이 혼자만 기억한다(적은 아무것도 모른다 — 되감은 뒤에도 처음처럼 군다).
///  · 지워진 시간에 레이를 발견했던 순찰 → 한동안 그 순찰의 손전등 시야가 바닥에 보인다(가리는 것까지 반영). 이번엔 들키지 않고 지나가라고.
///  · 지워진 시간에 레이를 때렸던 적 → 다음 공격 몇 번은 머리 위에 타이밍 막대가 뜨고 완벽 회피가 조금 쉬워진다. 이번엔 받아치라고.
/// 사건은 일어난 순간의 되감기 시계(TimeRewind.Clock)로 적어 두고, 되감아 도착한 시각보다 뒤의 사건만 "지워진 미래"로 친다.
/// </summary>
public class RewindMemory : MonoBehaviour
{
    /// <summary>발견했던 순찰의 시야가 보이는 시간(게임 초)</summary>
    public const float SightSeconds = 12f;
    /// <summary>때렸던 적의 공격을 미리 아는 횟수</summary>
    public const int ForeseenAttacks = 3;

    static RewindMemory instance;
    static Material material;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly RaycastHit[] hits = new RaycastHit[16];

    struct Moment { public float clock; public Object who; public bool hit; }
    static readonly List<Moment> moments = new();

    class Cone { public PatrolSight sight; public float until, born; public Mesh mesh; public Vector3[] verts; public MeshRenderer renderer; }
    readonly List<Cone> cones = new();
    MaterialPropertyBlock block;

    /// <summary>시험용: 지금 시야가 보이는 순찰 수</summary>
    public static int SightCount => instance ? instance.cones.Count : 0;
    /// <summary>시험용: 해당 순찰의 시야 꼭짓점 중 가장 먼 거리(가림 반영 확인)</summary>
    public static float SightReach(PatrolSight s)
    {
        if (!instance) return 0f;
        foreach (var c in instance.cones)
            if (c.sight == s) { float m = 0f; for (int i = 1; i < c.verts.Length; i++) m = Mathf.Max(m, Vector3.Distance(c.verts[0], c.verts[i])); return m; }
        return 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart()
    {
        moments.Clear();
        TimeRewind.Rewound -= OnRewound;
        TimeRewind.Rewound += OnRewound;
        PatrolSight.SawPlayer -= OnSpotted;
        PatrolSight.SawPlayer += OnSpotted;
        EnemyAI.HitPlayer -= OnHit;
        EnemyAI.HitPlayer += OnHit;
        GameManager.PlayerRespawned -= Forget;
        GameManager.PlayerRespawned += Forget;
    }

    static void OnSpotted(PatrolSight sight) => Note(sight, false);
    static void OnHit(EnemyAI enemy) => Note(enemy, true);

    /// <summary>연출로 들킨 순간(실제 발각 없이 의심만 채운 경우)도 기억에 남긴다</summary>
    public static void NoteSpotted(PatrolSight sight) => Note(sight, false);

    static void Note(Object who, bool hit)
    {
        float now = TimeRewind.Clock;
        moments.RemoveAll(m => now - m.clock > TimeRewind.HistoryLength || m.who == null);
        moments.Add(new Moment { clock = now, who = who, hit = hit });
    }

    /// <summary>저장 지점에서 다시 시작: 기억도 지운다(그건 되감기가 아니다)</summary>
    static void Forget()
    {
        moments.Clear();
        if (instance) instance.Clear();
        foreach (var e in EnemyAI.All) if (e) e.Foresee(0, true);
    }

    static void OnRewound(WorldSnapshot landed, List<WorldSnapshot> skipped)
    {
        if (landed == null) return;
        var sights = new List<PatrolSight>();
        var hitters = new List<EnemyAI>();
        foreach (var m in moments)
        {
            if (m.clock <= landed.Time || m.who == null) continue;
            if (m.who is PatrolSight s && !sights.Contains(s)) sights.Add(s);
            if (m.hit && m.who is EnemyAI e && !hitters.Contains(e)) hitters.Add(e);
        }
        moments.RemoveAll(m => m.clock > landed.Time); // 그 미래는 지워졌다
        if (sights.Count == 0 && hitters.Count == 0) return;
        if (!instance) instance = new GameObject("RewindMemory").AddComponent<RewindMemory>(); // 장면마다 새로(장면을 떠나면 함께 사라진다)
        instance.Remember(sights, hitters);
    }

    void Awake() => block = new MaterialPropertyBlock();

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Remember(List<PatrolSight> sights, List<EnemyAI> hitters)
    {
        foreach (var s in sights)
        {
            if (!s || s.Enemy.Health.IsDead) continue;
            var cone = cones.Find(c => c.sight == s);
            if (cone == null) cones.Add(cone = MakeCone(s));
            cone.until = Time.time + SightSeconds;
        }
        foreach (var e in hitters)
            if (e && !e.Health.IsDead) e.Foresee(ForeseenAttacks);

        var parts = new List<string>();
        if (sights.Count > 0) parts.Add($"나를 본 놈 {sights.Count}");
        if (hitters.Count > 0) parts.Add($"나를 친 놈 {hitters.Count}");
        if (GameManager.Instance) GameManager.Instance.Notify($"기억난다 — {string.Join(" · ", parts)}", 3f);
        Tutorial.Show("memory");
    }

    // ---------- 시야 부채꼴 ----------

    const int Rays = 28;

    Cone MakeCone(PatrolSight s)
    {
        var go = new GameObject("Sight_" + s.name);
        go.transform.SetParent(transform, false);
        var mesh = new Mesh { name = "SightCone" };
        mesh.MarkDynamic();
        var verts = new Vector3[Rays + 2];
        var tris = new int[Rays * 3];
        for (int i = 0; i < Rays; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2; }
        mesh.vertices = verts;
        mesh.triangles = tris;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Material();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return new Cone { sight = s, mesh = mesh, verts = verts, renderer = mr, born = Time.time };
    }

    void LateUpdate()
    {
        var gm = GameManager.Instance;
        Transform player = gm && gm.player ? gm.player.transform : null;
        for (int i = cones.Count - 1; i >= 0; i--)
        {
            var c = cones[i];
            // 끝났거나, 쓰러졌거나, 이미 들켜 싸우는 중이면 더 보여 줄 필요가 없다
            if (!c.sight || c.sight.Enemy.Health.IsDead || c.sight.Alerted || Time.time >= c.until)
            {
                if (c.renderer) Destroy(c.renderer.gameObject);
                cones.RemoveAt(i);
                continue;
            }
            Shape(c, player);
            float fade = Mathf.Clamp01((c.until - Time.time) / 1.5f) * Mathf.Clamp01((Time.time - c.born) / 0.4f);
            // 의심이 차오를수록 붉어진다
            Color col = Color.Lerp(new Color(1f, 0.82f, 0.5f, 0.1f), new Color(1f, 0.22f, 0.12f, 0.3f), c.sight.Suspicion);
            col.a *= fade;
            block.SetColor(BaseColor, col);
            c.renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>손전등이 실제로 닿는 바닥: 부채꼴 방향마다 눈높이에서 광선을 쏴 가리는 것까지만</summary>
    void Shape(Cone c, Transform player)
    {
        var s = c.sight;
        Transform t = s.transform;
        Vector3 origin = t.position + Vector3.up * 0.06f;
        Vector3 eye = t.position + Vector3.up * 1.2f;
        c.verts[0] = origin;
        for (int i = 0; i <= Rays; i++)
        {
            float a = Mathf.Lerp(-s.viewAngle, s.viewAngle, i / (float)Rays);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Flat(t.forward);
            float reach = Reach(eye, dir, s.viewDistance, t, player);
            c.verts[i + 1] = origin + dir * reach;
        }
        c.mesh.vertices = c.verts;
        c.mesh.RecalculateBounds();
    }

    static float Reach(Vector3 from, Vector3 dir, float max, Transform self, Transform player)
    {
        int n = Physics.RaycastNonAlloc(from, dir, hits, max, ~0, QueryTriggerInteraction.Ignore);
        float best = max;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            Transform ht = h.collider.transform;
            if (ht.IsChildOf(self) || (player && ht.IsChildOf(player))) continue;
            if (h.rigidbody) continue; // 쓰러진 몸은 가리지 않는다
            if (h.collider.GetComponentInParent<EnemyAI>()) continue;
            if (h.distance < best) best = h.distance;
        }
        return best;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    void Clear()
    {
        foreach (var c in cones) if (c.renderer) Destroy(c.renderer.gameObject);
        cones.Clear();
    }

    // ---------- 재질 ----------

    // 프로젝트에 둔 재질(Resources/Materials/RewindMemory)을 쓴다: 빌드에서 반투명 셰이더 변형이 빠지지 않게. 없으면 코드로 만든다
    static Material Material()
    {
        if (material) return material;
        material = Resources.Load<Material>("Materials/RewindMemory");
        if (material) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "RewindMemory" };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetColor(BaseColor, Color.white);
        return material;
    }
}
