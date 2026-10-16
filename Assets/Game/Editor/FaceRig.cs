using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 주인공 얼굴 손질(2026-10-16 "얼굴 가까이서 보면 눈코입이 녹아내린 것 같고, 표정이 없어 사이코패스 같다").
/// 원인: VARCO 모델의 얼굴이 텍스처 안에서 31조각으로 흩어져 있어(얼굴 몫 약 190×190px) 조각 경계마다 색 · 노멀이 끊기고 번졌다.
/// Blender(Protagonist_Face.blend)에서 얼굴 952면을 한 조각으로 다시 펼쳐 2048 텍스처에 굽고(조각 경계 자국은 메움),
/// 표정 모양 8개와 눈꺼풀(깜빡임)을 만들어 Protagonist_FaceRig.json으로 내보냈다. 이 도구는 FingerRig 다음에:
///  1) Blender 좌표 → 메시 좌표 변환을 얼굴 정점으로 맞춰 찾고(축 48가지 × 크기)
///  2) 얼굴 삼각형을 새 정점(새 UV)으로 떼어 두 번째 재질 칸(얼굴 재질)에 넣고
///  3) 눈꺼풀을 머리 뼈에 붙여 더하고 4) 표정 모양(블렌드셰이프)을 넣고 5) FaceExpression을 붙인다.
/// </summary>
public static class FaceRig
{
    const string Dir = "Assets/Game/Art/Baroque/Characters/Protagonist";
    const string Json = Dir + "/Protagonist_FaceRig.json";
    const string MatPath = Dir + "/Protagonist_Face.mat";

    [System.Serializable] class V { public float[] p; public float[] uv; public float[] n; }
    [System.Serializable] class Poly { public int[] v; }
    [System.Serializable] class Item { public float[] p; public float[] d; }
    [System.Serializable] class Shape { public string name; public Item[] items; }
    [System.Serializable] class D { public float[] d; }
    [System.Serializable] class LidShape { public string name; public D[] deltas; }
    [System.Serializable] class Data { public float[] bodyMin, bodyMax; public V[] faceVerts; public Poly[] facePolys; public Shape[] shapes; public V[] lidVerts; public Poly[] lidPolys; public LidShape[] lidShapes; }

    /// <summary>깜빡임은 눈꺼풀(가림판)에 얼굴 피부 눌림을 이만큼 섞는다</summary>
    const float SkinBlink = 0.5f;

    const string BasePath = Dir + "/texture_mapping_Fingers_Base.asset";

    /// <summary>얼굴 손질. force면 이미 손질된 메시도 손질 전 원본(Base)에서 다시 만든다(데이터를 고친 뒤)</summary>
    public static string Apply(Animator animator, bool force = false)
    {
        var smr = animator.GetComponentInChildren<SkinnedMeshRenderer>();
        var target = smr.sharedMesh;
        Mesh mesh;
        if (target.GetBlendShapeIndex("Blink_L") >= 0)
        {
            if (!force) { Finish(animator, smr); return $"{animator.name}: 얼굴은 이미 손질됨"; }
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BasePath) ?? SaveBase(Reconstruct(target));
        }
        else mesh = SaveBase(target);
        if (!File.Exists(Json)) return $"{animator.name}: {Json} 없음 — 얼굴을 손질하지 않았다";
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(Json));

        var verts = mesh.vertices; var normals = mesh.normals; var uvs = mesh.uv; var weights = mesh.boneWeights;
        // 1) 좌표 변환 찾기: 얼굴 정점이 메시 정점과 가장 잘 겹치는 축 · 부호 · 크기
        var face = data.faceVerts.Select(v => new Vector3(v.p[0], v.p[1], v.p[2])).ToArray();
        var (M, offset, err) = FitTransform(face, verts, new Vector3(data.bodyMin[0], data.bodyMin[1], data.bodyMin[2]), new Vector3(data.bodyMax[0], data.bodyMax[1], data.bodyMax[2]));
        float unit = M.GetColumn(0).magnitude; // Blender 1 = 메시 몇 단위
        if (err > 0.0003f * unit) return $"{animator.name}: 좌표 맞추기 실패(오차 {err / unit * 1000f:0.00}mm) — 얼굴을 손질하지 않았다";
        Vector3 ToMesh(float[] p) => M.MultiplyVector(new Vector3(p[0], p[1], p[2])) + offset;
        Vector3 Dir3(float[] d) => M.MultiplyVector(new Vector3(d[0], d[1], d[2]));
        float scale = M.GetColumn(0).magnitude;

        // 2) 메시 정점 → 얼굴 정점 번호
        var faceMesh = face.Select(p => M.MultiplyVector(p) + offset).ToArray();
        var grid = new Grid(faceMesh, 0.004f * scale);
        float tol = 0.0003f * scale;
        var faceId = new int[verts.Length];
        for (int i = 0; i < verts.Length; i++) faceId[i] = grid.Nearest(verts[i], tol);
        var polySets = data.facePolys.Select(p => new HashSet<int>(p.v)).ToArray();
        var byVert = new Dictionary<int, List<int>>();
        for (int pi = 0; pi < polySets.Length; pi++) foreach (int v in polySets[pi]) { if (!byVert.TryGetValue(v, out var l)) byVert[v] = l = new List<int>(); l.Add(pi); }

        var tris = mesh.triangles;
        var keep = new List<int>(); var faceTris = new List<int>();
        var vList = verts.ToList(); var nList = normals.ToList(); var uvList = uvs.ToList(); var wList = weights.ToList();
        var dup = new Dictionary<int, int>(); // 원래 정점 → 얼굴 쪽 새 정점
        var srcOf = new List<int>();          // 새 정점 → 원래 정점(표정 모양을 옮길 때)
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            int fa = faceId[a], fb = faceId[b], fc = faceId[c];
            bool inFace = fa >= 0 && fb >= 0 && fc >= 0 && byVert.TryGetValue(fa, out var cand) && cand.Any(pi => polySets[pi].Contains(fb) && polySets[pi].Contains(fc));
            if (!inFace) { keep.Add(a); keep.Add(b); keep.Add(c); continue; }
            foreach (int v in new[] { a, b, c })
            {
                if (!dup.TryGetValue(v, out int nv))
                {
                    nv = vList.Count; dup[v] = nv;
                    vList.Add(verts[v]); nList.Add(normals[v]); wList.Add(weights[v]);
                    var fv = data.faceVerts[faceId[v]];
                    uvList.Add(new Vector2(fv.uv[0], fv.uv[1]));
                }
                faceTris.Add(nv);
            }
        }
        int faceTriCount = faceTris.Count / 3;
        if (faceTriCount < 800) return $"{animator.name}: 얼굴 삼각형이 {faceTriCount}개뿐 — 얼굴을 손질하지 않았다";

        // 3) 눈꺼풀: 머리 뼈 하나에 붙인다
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        int headBone = System.Array.IndexOf(smr.bones, head);
        if (headBone < 0) return $"{animator.name}: 머리 뼈를 찾지 못해 얼굴을 손질하지 않았다";
        int lidStart = vList.Count;
        foreach (var lv in data.lidVerts)
        {
            vList.Add(ToMesh(lv.p)); uvList.Add(new Vector2(lv.uv[0], lv.uv[1]));
            nList.Add(Dir3(lv.n ?? new[] { 0f, -1f, 0f }).normalized); // 그 자리 얼굴 피부와 같은 방향(빛이 같게)
            wList.Add(new BoneWeight { boneIndex0 = headBone, weight0 = 1f });
        }
        bool flip = M.determinant < 0f; // 손잡이 방향이 바뀌면 삼각형 감김을 뒤집는다
        foreach (var lp in data.lidPolys)
            for (int k = 1; k + 1 < lp.v.Length; k++)
            {
                int a = lidStart + lp.v[0], b = lidStart + lp.v[k], c = lidStart + lp.v[k + 1];
                if (flip) { faceTris.Add(a); faceTris.Add(c); faceTris.Add(b); } else { faceTris.Add(a); faceTris.Add(b); faceTris.Add(c); }
            }
        // 눈꺼풀이 보통 때(뜬 상태)에도 앞을 보는지: 뒤를 보면 감김을 뒤집는다
        {
            int a = faceTris[faceTris.Count - 3], b = faceTris[faceTris.Count - 2], c = faceTris[faceTris.Count - 1];
            var n = Vector3.Cross(vList[b] - vList[a], vList[c] - vList[a]);
            if (Vector3.Dot(n, nList[a]) < 0f)
                for (int i = faceTris.Count - data.lidPolys.Length * 6; i < faceTris.Count; i += 3) (faceTris[i + 1], faceTris[i + 2]) = (faceTris[i + 2], faceTris[i + 1]);
        }

        var outMesh = Object.Instantiate(mesh);
        outMesh.name = target.name;
        outMesh.ClearBlendShapes();
        outMesh.indexFormat = vList.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : outMesh.indexFormat;
        outMesh.SetVertices(vList); outMesh.SetNormals(nList); outMesh.SetUVs(0, uvList);
        outMesh.boneWeights = wList.ToArray();
        outMesh.bindposes = mesh.bindposes;
        outMesh.subMeshCount = 2;
        outMesh.SetTriangles(keep, 0);
        outMesh.SetTriangles(faceTris, 1);
        outMesh.RecalculateTangents(); // 얼굴 노멀 맵은 새 UV 기준
        outMesh.RecalculateBounds();

        // 4) 표정 모양: 위치로 짝지어 옮긴다(원래 정점과 떼어 낸 얼굴 정점 모두)
        int total = vList.Count;
        var lidDeltas = data.lidShapes.ToDictionary(s => s.name, s => s.deltas);
        foreach (var sh in data.shapes)
        {
            var pts = sh.items.Select(it => ToMesh(it.p)).ToArray();
            var g2 = new Grid(pts, 0.004f * scale);
            var deltas = new Vector3[total];
            float k = sh.name.StartsWith("Blink") ? SkinBlink : 1f;
            for (int i = 0; i < lidStart; i++)
            {
                int j = g2.Nearest(vList[i], tol);
                if (j >= 0) deltas[i] = Dir3(sh.items[j].d) * k;
            }
            if (lidDeltas.TryGetValue(sh.name, out var ld))
                for (int i = 0; i < ld.Length; i++) deltas[lidStart + i] = Dir3(ld[i].d);
            outMesh.AddBlendShapeFrame(sh.name, 100f, deltas, null, null);
        }

        // 저장(같은 자리 덮어쓰기 — GUID 유지)
        string path = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(path) || path.EndsWith(".fbx") || path == BasePath) path = $"{Dir}/texture_mapping_Fingers.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        // 통째 복사(CopySerialized)는 이미 그리던 렌더러의 그래픽 버퍼와 어긋나 몸이 안 그려졌다 → 같은 메시 객체에 API로 다시 넣는다
        if (existing) { CopyMesh(outMesh, existing); Object.DestroyImmediate(outMesh); outMesh = existing; EditorUtility.SetDirty(existing); }
        else AssetDatabase.CreateAsset(outMesh, path);
        AssetDatabase.SaveAssets();
        smr.sharedMesh = outMesh;
        Finish(animator, smr);
        return $"{animator.name}: 얼굴 손질 — 좌표 오차 {err / unit * 1000f:0.000}mm, 얼굴 삼각형 {faceTriCount}개(새 정점 {dup.Count}), 눈꺼풀 {data.lidVerts.Length}점, 표정 {data.shapes.Length}개";
    }

    /// <summary>손질 전 메시를 따로 보관(다시 손질할 때 여기서 시작)</summary>
    static Mesh SaveBase(Mesh src)
    {
        var copy = Object.Instantiate(src); copy.name = "texture_mapping_Fingers_Base";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(BasePath);
        if (existing) { CopyMesh(copy, existing); Object.DestroyImmediate(copy); AssetDatabase.SaveAssets(); return existing; }
        AssetDatabase.CreateAsset(copy, BasePath); AssetDatabase.SaveAssets();
        return copy;
    }

    /// <summary>이미 손질된 메시에서 손질 전 메시를 되살린다: 원래 정점은 앞쪽 N개(FBX 메시 정점 수), 얼굴 삼각형은 같은 자리 원래 정점으로, 눈꺼풀은 버린다</summary>
    static Mesh Reconstruct(Mesh applied)
    {
        var fbx = AssetDatabase.LoadAllAssetsAtPath(Dir + "/Protagonist.fbx").OfType<Mesh>().First();
        int n = fbx.vertexCount;
        var v = applied.vertices; var map = new Dictionary<Vector3, int>();
        for (int i = 0; i < n; i++) if (!map.ContainsKey(v[i])) map[v[i]] = i;
        var tris = applied.GetTriangles(0).ToList();
        var face = applied.GetTriangles(1);
        for (int t = 0; t < face.Length; t += 3)
        {
            var idx = new int[3]; bool ok = true;
            for (int k = 0; k < 3; k++) { int a = face[t + k]; if (a < n) idx[k] = a; else if (map.TryGetValue(v[a], out int o)) idx[k] = o; else ok = false; }
            if (ok) tris.AddRange(idx);
        }
        var m = new Mesh { name = "texture_mapping_Fingers_Base", indexFormat = applied.indexFormat };
        m.SetVertices(v.Take(n).ToArray()); m.SetNormals(applied.normals.Take(n).ToArray()); m.SetTangents(applied.tangents.Take(n).ToArray());
        m.SetUVs(0, fbx.uv); m.boneWeights = applied.boneWeights.Take(n).ToArray(); m.bindposes = applied.bindposes;
        m.SetTriangles(tris, 0); m.RecalculateBounds();
        return m;
    }

    static void CopyMesh(Mesh src, Mesh dst)
    {
        dst.Clear();
        dst.ClearBlendShapes();
        dst.indexFormat = src.indexFormat;
        dst.SetVertices(src.vertices); dst.SetNormals(src.normals); dst.SetTangents(src.tangents); dst.SetUVs(0, src.uv);
        dst.boneWeights = src.boneWeights; dst.bindposes = src.bindposes;
        dst.subMeshCount = src.subMeshCount;
        for (int i = 0; i < src.subMeshCount; i++) dst.SetTriangles(src.GetTriangles(i), i);
        var dv = new Vector3[src.vertexCount];
        for (int b = 0; b < src.blendShapeCount; b++)
        {
            src.GetBlendShapeFrameVertices(b, 0, dv, null, null);
            dst.AddBlendShapeFrame(src.GetBlendShapeName(b), src.GetBlendShapeFrameWeight(b, 0), dv, null, null);
        }
        dst.RecalculateBounds();
        dst.UploadMeshData(false);
    }

    /// <summary>재질 칸 둘(몸 · 얼굴)과 표정 컴포넌트</summary>
    static void Finish(Animator animator, SkinnedMeshRenderer smr)
    {
        var mats = smr.sharedMaterials;
        var faceMat = EnsureMaterial(mats.Length > 0 ? mats[0] : null);
        if (mats.Length < 2 || mats[1] != faceMat) smr.sharedMaterials = new[] { mats[0], faceMat };
        if (!animator.GetComponent<FaceExpression>()) animator.gameObject.AddComponent<FaceExpression>();
    }

    public static Material EnsureMaterial(Material body)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat) return mat;
        var baseTex = ImportFaceTexture(Dir + "/Protagonist_Face_BaseColor.png", false);
        var normal = ImportFaceTexture(Dir + "/Protagonist_Face_Normal.png", true);
        var ms = MetallicSmoothness(Dir + "/Protagonist_Face_MetalRough.png", Dir + "/Protagonist_Face_MetallicSmoothness.png");
        mat = body ? new Material(body) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = "Protagonist_Face";
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetTexture("_BumpMap", normal);
        mat.SetTexture("_MetallicGlossMap", ms);
        AssetDatabase.CreateAsset(mat, MatPath);
        return mat;
    }

    static Texture2D ImportFaceTexture(string path, bool isNormal)
    {
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !isNormal;
        ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.CompressedHQ; // BC7: 얼굴은 압축 뭉개짐이 눈에 띈다
        ti.anisoLevel = 4;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>BaroqueCharacterSetup과 같은 규칙: 금속 0, 매끈함 = (255 − 거칠기) × 0.75</summary>
    static Texture2D MetallicSmoothness(string src, string dst)
    {
        AssetDatabase.ImportAsset(src);
        var ti = (TextureImporter)AssetImporter.GetAtPath(src);
        ti.sRGBTexture = false; ti.isReadable = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(src);
        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, (byte)((255 - px[i].g) * 0.75f));
        var outTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
        outTex.SetPixels32(px); outTex.Apply();
        File.WriteAllBytes(dst, outTex.EncodeToPNG()); Object.DestroyImmediate(outTex);
        AssetDatabase.ImportAsset(dst);
        var oi = (TextureImporter)AssetImporter.GetAtPath(dst);
        oi.sRGBTexture = false; oi.alphaSource = TextureImporterAlphaSource.FromInput; oi.maxTextureSize = 2048; oi.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    /// <summary>
    /// Blender 점 → 메시 점: 축 순서 · 부호 48가지마다 전신 경계 상자로 크기와 위치를 정하고(Blender 전신 상자 bodyMin/Max ↔ 메시 상자),
    /// 얼굴 점이 메시 정점에 가장 가깝게 겹치는 것을 고른 뒤 위치를 몇 번 다듬는다. 반환: (변환, 위치, 평균 오차 — 메시 단위)
    /// </summary>
    static (Matrix4x4, Vector3, float) FitTransform(Vector3[] blender, Vector3[] meshVerts, Vector3 bMin, Vector3 bMax)
    {
        var mb = new Bounds(meshVerts[0], Vector3.zero); foreach (var v in meshVerts) mb.Encapsulate(v);
        float height = Mathf.Max(mb.size.x, Mathf.Max(mb.size.y, mb.size.z));
        var meshGrid = new Grid(meshVerts, height * 0.004f);
        var perms = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        Vector3 bSize = bMax - bMin, bCenter = (bMin + bMax) / 2f;
        var best = (Matrix4x4.identity, Vector3.zero, float.MaxValue);
        foreach (var pm in perms)
            for (int sgn = 0; sgn < 8; sgn++)
            {
                var m = Matrix4x4.zero;
                for (int r = 0; r < 3; r++) m[r, pm[r]] = (sgn >> r & 1) == 1 ? -1f : 1f;
                m[3, 3] = 1f;
                var rs = m.MultiplyVector(bSize); rs = new Vector3(Mathf.Abs(rs.x), Mathf.Abs(rs.y), Mathf.Abs(rs.z));
                // 축이 맞으면 세 축 크기 비가 같다
                float sx = mb.size.x / rs.x, sy = mb.size.y / rs.y, sz = mb.size.z / rs.z;
                float s = (sx + sy + sz) / 3f;
                if (Mathf.Abs(sx - s) > s * 0.05f || Mathf.Abs(sy - s) > s * 0.05f || Mathf.Abs(sz - s) > s * 0.05f) continue;
                var ms = Matrix4x4.Scale(Vector3.one * s) * m;
                Vector3 off = mb.center - ms.MultiplyVector(bCenter);
                var rot = blender.Select(p => ms.MultiplyVector(p)).ToArray();
                float err = float.MaxValue;
                for (int it = 0; it < 6; it++)
                {
                    Vector3 sum = Vector3.zero; float e = 0f; int n = 0;
                    for (int i = 0; i < rot.Length; i++)
                    {
                        var q = rot[i] + off; int j = meshGrid.NearestAny(q);
                        if (j < 0) continue;
                        var d = meshVerts[j] - q; sum += d; e += d.magnitude; n++;
                    }
                    if (n < rot.Length / 2) { err = float.MaxValue; break; }
                    off += sum / n; err = e / n;
                }
                if (err < best.Item3) best = (ms, off, err);
            }
        return best;
    }

    static Vector3 Center(Vector3[] pts) { var s = Vector3.zero; foreach (var p in pts) s += p; return s / pts.Length; }
    static float Spread(Vector3[] pts, Vector3 c) { float s = 0; foreach (var p in pts) s += (p - c).magnitude; return s / pts.Length; }

    /// <summary>점 찾기용 칸 나누기</summary>
    class Grid
    {
        readonly Dictionary<Vector3Int, List<int>> cells = new();
        readonly Vector3[] pts; readonly float size;
        public Grid(Vector3[] pts, float size) { this.pts = pts; this.size = size; for (int i = 0; i < pts.Length; i++) { var k = Key(pts[i]); if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>(); l.Add(i); } }
        Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / size);
        public int Nearest(Vector3 p, float tol)
        {
            int best = -1; float bd = tol * tol; var k = Key(p);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (cells.TryGetValue(k + new Vector3Int(x, y, z), out var l))
                    foreach (int i in l) { float d = (pts[i] - p).sqrMagnitude; if (d <= bd) { bd = d; best = i; } }
            return best;
        }
        public int NearestAny(Vector3 p) => Nearest(p, size);
    }
}
