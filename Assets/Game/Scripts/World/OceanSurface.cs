using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 바다: 카메라를 따라다니는 촘촘한 격자 판을 거스트너 파도(여러 겹)로 출렁이게 한다. 가까운 곳은 촘촘하고 먼 곳은 성기게(동심원 격자).
/// 반짝임은 재질(매끄러움 높음)이 하늘·조명을 비춰서, 잔물결은 흘러가는 노멀맵으로.
/// 최적화(2026-10-13): 정점 계산을 Burst 병렬 잡으로, 노멀은 파도 식에서 바로(RecalculateNormals 대신), 배열은 한 번만 만든다.
/// 전에는 1장에서 매 프레임 3.2ms + 47KB 할당이었다. 화면에 안 보이면 계산하지 않는다.
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

    // 잡에 넘기는 파도 하나: 방향(정규화) · 파수 k · 각속도 ω · 진폭 · 가파름
    struct WaveData { public float2 dir; public float k, omega, amp, steep; }

    Mesh mesh;
    Material mat;
    Camera cam;
    MeshRenderer meshRenderer;
    NativeArray<float3> baseVerts;
    NativeArray<float> fades;
    NativeArray<WaveData> waves;
    NativeArray<Vector3> verts, normals;
    NativeArray<Vector2> uvs;
    static readonly int BaseST = Shader.PropertyToID("_BaseMap_ST");

    void Awake()
    {
        mesh = new Mesh { name = "Ocean" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.MarkDynamic();
        int vc = (rings + 1) * segments;
        baseVerts = new NativeArray<float3>(vc, Allocator.Persistent);
        fades = new NativeArray<float>(vc, Allocator.Persistent);
        verts = new NativeArray<Vector3>(vc, Allocator.Persistent);
        normals = new NativeArray<Vector3>(vc, Allocator.Persistent);
        uvs = new NativeArray<Vector2>(vc, Allocator.Persistent);
        var tris = new int[rings * segments * 6];
        for (int r = 0; r <= rings; r++)
        {
            // 가까운 곳 촘촘: 반지름이 제곱으로 늘어난다
            float k = r / (float)rings;
            float rad = 0.5f + radius * k * k;
            // 멀수록 파도를 낮춘다: 성긴 먼 격자가 각진 줄무늬로 보이지 않게(먼 바다는 잔잔하게 반짝인다)
            float fade = 1f - Mathf.Clamp01((rad - 40f) / 160f);
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                baseVerts[r * segments + s] = new float3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                fades[r * segments + s] = fade;
                verts[r * segments + s] = baseVerts[r * segments + s];
                normals[r * segments + s] = Vector3.up;
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
        waves = new NativeArray<WaveData>(Waves.Length, Allocator.Persistent);
        for (int i = 0; i < Waves.Length; i++)
        {
            var w = Waves[i];
            float k = 2f * Mathf.PI / w.length;
            waves[i] = new WaveData { dir = math.normalize(new float2(w.dir.x, w.dir.y)), k = k, omega = Mathf.Sqrt(9.8f * k) * w.speed, amp = w.amp, steep = w.steep };
        }
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(radius * 2.2f, 10f, radius * 2.2f));
        GetComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        mat = meshRenderer.material;
    }

    void OnDestroy()
    {
        if (baseVerts.IsCreated) baseVerts.Dispose();
        if (fades.IsCreated) fades.Dispose();
        if (waves.IsCreated) waves.Dispose();
        if (verts.IsCreated) verts.Dispose();
        if (normals.IsCreated) normals.Dispose();
        if (uvs.IsCreated) uvs.Dispose();
        if (mesh) Destroy(mesh);
    }

    [BurstCompile]
    struct WaveJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> baseVerts;
        [ReadOnly] public NativeArray<float> fades;
        [ReadOnly] public NativeArray<WaveData> waves;
        public float3 origin;
        public float minZ, time, scale;
        [WriteOnly] public NativeArray<Vector3> verts, normals;
        [WriteOnly] public NativeArray<Vector2> uvs;

        public void Execute(int i)
        {
            float3 b = baseVerts[i];
            float wx = origin.x + b.x, wz = math.max(origin.z + b.z, minZ); // 안벽 안쪽은 물을 접어 둔다
            float3 p = new(wx - origin.x, 0f, wz - origin.z);
            // 접힌 정점은 출렁이지 않는다: 반지름마다 파도 높이가 달라 안벽 선을 따라 얇은 물 커튼(사선 줄무늬)이 섰다
            float fade = origin.z + b.z < minZ ? 0f : fades[i];
            // 거스트너 파도의 위치와 노멀(GPU Gems 1, 1장): N = (-Σ D.x·k·A·cos, 1 - Σ Q·k·A·sin, -Σ D.y·k·A·cos)
            float3 n = new(0f, 1f, 0f);
            if (fade > 0f)
            {
                for (int w = 0; w < waves.Length; w++)
                {
                    var wave = waves[w];
                    float phase = wave.k * (wave.dir.x * wx + wave.dir.y * wz) - wave.omega * time;
                    math.sincos(phase, out float s, out float c);
                    float a = wave.amp * scale * fade;
                    p.x += wave.dir.x * wave.steep * a * c;
                    p.z += wave.dir.y * wave.steep * a * c;
                    p.y += a * s;
                    float ka = wave.k * a;
                    n.x -= wave.dir.x * ka * c;
                    n.z -= wave.dir.y * ka * c;
                    n.y -= wave.steep * ka * s;
                }
                n = math.normalize(n);
            }
            verts[i] = p;
            normals[i] = n;
            uvs[i] = new Vector2(wx, wz) / 18f;
        }
    }

    void LateUpdate()
    {
        if (!cam) cam = Camera.main;
        if (!cam) return;
        if (!meshRenderer.isVisible) return; // 바다가 안 보이면(실내·다른 구역) 계산하지 않는다
        // 카메라를 따라간다(2m 단위로 끊어 파도가 미끄러져 보이지 않게)
        Vector3 c = cam.transform.position;
        Vector3 origin = new(Mathf.Round(c.x / 2f) * 2f, transform.position.y, Mathf.Round(Mathf.Max(c.z, minZ) / 2f) * 2f);
        transform.position = origin;
        float time = Time.time;
        new WaveJob
        {
            baseVerts = baseVerts, fades = fades, waves = waves,
            origin = origin, minZ = minZ, time = time, scale = waveScale,
            verts = verts, normals = normals, uvs = uvs,
        }.Schedule(verts.Length, 256).Complete();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        // 잔물결: 노멀맵이 천천히 흘러간다
        mat.SetVector(BaseST, new Vector4(1f, 1f, time * rippleSpeed.x, time * rippleSpeed.y));
    }
}
