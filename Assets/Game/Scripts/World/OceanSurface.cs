using UnityEngine;

/// <summary>
/// 바다: 카메라를 따라다니는 촘촘한 격자 판을 거스트너 파도(여러 겹)로 출렁이게 한다. 가까운 곳은 촘촘하고 먼 곳은 성기게(동심원 격자).
/// 반짝임은 재질(매끄러움 높음)이 하늘·조명을 비춰서, 잔물결은 흘러가는 노멀맵으로.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanSurface : MonoBehaviour
{
    [Tooltip("물이 이 선(z)보다 남쪽으로는 그려지지 않는다(안벽 안쪽 땅 밑으로 파고들지 않게)")]
    public float minZ = 424f;
    public float radius = 420f;
    public int rings = 60, segments = 96;
    public float waveScale = 1f;
    [Tooltip("잔물결 노멀맵이 흘러가는 빠르기")]
    public Vector2 rippleSpeed = new(0.012f, 0.02f);

    struct Wave { public Vector2 dir; public float length, amp, speed, steep; }
    static readonly Wave[] Waves =
    {
        new() { dir = new Vector2(0.2f, 1f), length = 34f, amp = 0.32f, speed = 1.0f, steep = 0.5f },
        new() { dir = new Vector2(-0.6f, 0.8f), length = 17f, amp = 0.15f, speed = 1.1f, steep = 0.6f },
        new() { dir = new Vector2(0.8f, 0.5f), length = 9f, amp = 0.07f, speed = 1.3f, steep = 0.6f },
        new() { dir = new Vector2(-0.2f, -1f), length = 5.5f, amp = 0.035f, speed = 1.5f, steep = 0.5f },
    };

    Mesh mesh;
    Vector3[] baseVerts, verts;
    Material mat;
    Camera cam;
    static readonly int BaseST = Shader.PropertyToID("_BaseMap_ST");

    void Awake()
    {
        mesh = new Mesh { name = "Ocean" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.MarkDynamic();
        int vc = (rings + 1) * segments;
        baseVerts = new Vector3[vc];
        var uv = new Vector2[vc];
        var tris = new int[rings * segments * 6];
        for (int r = 0; r <= rings; r++)
        {
            // 가까운 곳 촘촘: 반지름이 제곱으로 늘어난다
            float k = r / (float)rings;
            float rad = 0.5f + radius * k * k;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                baseVerts[r * segments + s] = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
            }
        }
        int t = 0;
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segments; s++)
        {
            int a = r * segments + s, b = r * segments + (s + 1) % segments, c = a + segments, d = b + segments;
            tris[t++] = a; tris[t++] = c; tris[t++] = b;
            tris[t++] = b; tris[t++] = c; tris[t++] = d;
        }
        verts = new Vector3[vc];
        mesh.vertices = baseVerts;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(radius * 2.2f, 10f, radius * 2.2f));
        GetComponent<MeshFilter>().sharedMesh = mesh;
        mat = GetComponent<MeshRenderer>().material;
    }

    void LateUpdate()
    {
        if (!cam) cam = Camera.main;
        if (!cam) return;
        // 카메라를 따라간다(2m 단위로 끊어 파도가 미끄러져 보이지 않게)
        Vector3 c = cam.transform.position;
        Vector3 origin = new(Mathf.Round(c.x / 2f) * 2f, transform.position.y, Mathf.Round(Mathf.Max(c.z, minZ) / 2f) * 2f);
        transform.position = origin;
        float time = Time.time;
        var uv = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 b = baseVerts[i];
            float wx = origin.x + b.x, wz = origin.z + b.z;
            if (wz < minZ) wz = minZ;              // 안벽 안쪽은 물을 접어 둔다
            Vector3 p = new(wx - origin.x, 0f, wz - origin.z);
            // 멀수록 파도를 낮춘다: 성긴 먼 격자가 각진 줄무늬로 보이지 않게(먼 바다는 잔잔하게 반짝인다)
            float fade = 1f - Mathf.Clamp01((b.magnitude - 40f) / 160f);
            foreach (var w in Waves)
            {
                Vector2 dir = w.dir.normalized;
                float k = 2f * Mathf.PI / w.length;
                float phase = k * (dir.x * wx + dir.y * wz) - Mathf.Sqrt(9.8f * k) * w.speed * time;
                float a = w.amp * waveScale * fade;
                p.x += dir.x * (w.steep * a) * Mathf.Cos(phase);
                p.z += dir.y * (w.steep * a) * Mathf.Cos(phase);
                p.y += a * Mathf.Sin(phase);
            }
            verts[i] = p;
            uv[i] = new Vector2(wx, wz) / 18f;
        }
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        // 잔물결: 노멀맵이 천천히 흘러간다
        mat.SetVector(BaseST, new Vector4(1f, 1f, time * rippleSpeed.x, time * rippleSpeed.y));
    }
}
