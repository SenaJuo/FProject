using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 소품 메시 LOD(2026-10-13 "전체 최적화"): VARCO 소품은 하나에 1만~5만 삼각형이라 수십 개씩 놓이면 한 장면이 수백만 삼각형이 된다
/// (1장 940만 · 2장 807만 — 생울타리 58개만 248만). 원본 GLB는 그대로 두고, 메시를 복사해 유니티 메시 LOD(멀수록 단순한 단계)를 만든 뒤
/// 프리팹이 그 복사본을 쓰게 바꾼다. 가까이서는 원본(LOD0) 그대로라 모양이 바뀌지 않는다.
/// 복사본: Art/Baroque/Props/Lod/{소품}_{n}.asset. BaroquePropSetup이 프리팹을 다시 만들 때도 이것을 쓴다.
/// </summary>
public static class PropLods
{
    const string PropRoot = "Assets/Game/Art/Baroque/Props";
    const string LodRoot = PropRoot + "/Lod";
    const int MinTriangles = 2500; // 이보다 가벼운 메시는 그대로
    const int MaxLods = 6;

    static long Triangles(Mesh m) { long t = 0; for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t; }

    /// <summary>원본 메시의 LOD 복사본(없으면 만든다). 가벼운 메시나 GLB 밖의 메시는 원본을 그대로 돌려준다</summary>
    public static Mesh LodOf(Mesh source)
    {
        if (!source) return source;
        string path = AssetDatabase.GetAssetPath(source);
        if (!path.StartsWith(PropRoot) || !path.EndsWith(".glb") || Triangles(source) < MinTriangles) return source;
        var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderBy(m => m.name).ToList();
        int index = meshes.IndexOf(source);
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string lodPath = $"{LodRoot}/{name}_{Mathf.Max(0, index)}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(lodPath);
        if (existing) return existing;
        if (!AssetDatabase.IsValidFolder(LodRoot)) AssetDatabase.CreateFolder(PropRoot, "Lod");
        var copy = Object.Instantiate(source);
        copy.name = $"{name}_{Mathf.Max(0, index)}";
        MeshLodUtility.GenerateMeshLods(copy, MaxLods);
        AssetDatabase.CreateAsset(copy, lodPath);
        return copy;
    }

    /// <summary>모델 안의 MeshFilter가 LOD 복사본을 쓰게 바꾼다(바꾼 개수)</summary>
    public static int Swap(GameObject model)
    {
        int n = 0;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var lod = LodOf(mf.sharedMesh);
            if (lod && lod != mf.sharedMesh) { mf.sharedMesh = lod; n++; }
        }
        return n;
    }

    /// <summary>소품 프리팹 전부에 적용한다(장면의 소품은 프리팹 인스턴스라 함께 바뀐다)</summary>
    [MenuItem("Game/Baroque/Apply Prop LODs")]
    public static string ApplyToPrefabs()
    {
        var report = new List<string>();
        long before = 0, lod1 = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropRoot + "/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            int n = Swap(root);
            if (n > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; if (!m) continue;
                    long t0 = Triangles(m); before += t0;
                    lod1 += m.lodCount > 1 ? m.GetLod(0, 1).indexCount / 3 : t0;
                }
                report.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)} ({n})");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        return $"LOD 적용 {report.Count}개: {string.Join(", ", report)} | 원본 {before / 1000}k → LOD1 {lod1 / 1000}k 삼각형(프리팹 하나씩 합)";
    }
}
