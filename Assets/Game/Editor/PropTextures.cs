using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// 소품 텍스처 압축(2026-10-13 최적화): glTFast로 들어온 GLB 텍스처는 압축 옵션이 없어 2048 한 장이 원본 그대로(ARGB32, 밉 포함 약 21MB) 올라간다.
/// 장면 전체 149장 ≈ 3.7GB(에디터 기준). 텍스처를 PNG로 꺼내 유니티 압축(BC7)과 소품 크기에 맞는 최대 크기를 걸고,
/// 그 텍스처를 쓰는 재질 복사본으로 프리팹을 바꾼다. 원본 GLB는 그대로.
/// 꺼낸 것: Art/Baroque/Props/Textures/{소품}_{image_n}.png · 재질: Art/Baroque/Props/Materials/{소품}_{n}.mat
/// </summary>
public static class PropTextures
{
    const string PropRoot = "Assets/Game/Art/Baroque/Props";
    const string TexRoot = PropRoot + "/Textures";
    const string MatRoot = PropRoot + "/Materials";

    /// <summary>소품 크기(가장 긴 변, m)에 맞는 텍스처 최대 크기: 화면에서 작게 보이는 것은 작게</summary>
    public static int SizeFor(float metres) => metres < 0.6f ? 512 : metres < 1.5f ? 1024 : 2048;

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
    }

    /// <summary>GLB 텍스처를 PNG로 꺼내 압축 설정을 건다(이미 있으면 최대 크기만 맞춘다)</summary>
    static Texture2D Extract(Texture2D t, string glbName, int maxSize)
    {
        Folder(TexRoot);
        string path = $"{TexRoot}/{glbName}_{t.name}.png";
        bool srgb = GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);
        if (!System.IO.File.Exists(path))
        {
            // 읽을 수 없는 텍스처라 그래픽 카드에서 복사해 읽는다(색 공간을 원본과 같게: 색은 sRGB, 노멀·금속도는 선형)
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, !srgb);
            copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(path, copy.EncodeToPNG());
            Object.DestroyImmediate(copy);
            AssetDatabase.ImportAsset(path);
        }
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        int size = Mathf.Min(maxSize, Mathf.NextPowerOfTwo(Mathf.Max(t.width, t.height)));
        if (ti.sRGBTexture != srgb || ti.maxTextureSize != size || ti.textureCompression != TextureImporterCompression.CompressedHQ || !ti.mipmapEnabled)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.alphaIsTransparency = srgb;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = size;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; // BC7: 원본의 1/4, 품질은 거의 같다
            ti.anisoLevel = 4;
            ti.wrapMode = t.wrapMode;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>GLB 재질의 압축본(텍스처만 바꾼 복사본). GLB 밖의 재질은 그대로</summary>
    public static Material Optimized(Material source, int maxSize)
    {
        if (!source) return source;
        string path = AssetDatabase.GetAssetPath(source);
        if (!path.StartsWith(PropRoot) || !path.EndsWith(".glb")) return source;
        string glbName = System.IO.Path.GetFileNameWithoutExtension(path);
        var mats = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().OrderBy(m => m.name).ToList();
        string matPath = $"{MatRoot}/{glbName}_{Mathf.Max(0, mats.IndexOf(source))}.mat";
        var copy = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool fresh = !copy;
        if (fresh) copy = new Material(source);
        foreach (var prop in source.GetTexturePropertyNames())
        {
            if (source.GetTexture(prop) is not Texture2D tex || AssetDatabase.GetAssetPath(tex) != path) continue;
            copy.SetTexture(prop, Extract(tex, glbName, maxSize));
        }
        if (fresh) { Folder(MatRoot); AssetDatabase.CreateAsset(copy, matPath); }
        else EditorUtility.SetDirty(copy);
        return copy;
    }

    /// <summary>모델 안 렌더러의 재질을 압축본으로 바꾼다(바꾼 개수). 크기는 모델 전체에서 잰다</summary>
    public static int Swap(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return 0;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        int maxSize = SizeFor(Mathf.Max(b.size.x, b.size.y, b.size.z));
        int n = 0;
        foreach (var r in rs)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var o = Optimized(mats[i], maxSize);
                if (o != mats[i]) { mats[i] = o; changed = true; n++; }
            }
            if (changed) r.sharedMaterials = mats;
        }
        return n;
    }

    [MenuItem("Game/Baroque/Compress Prop Textures")]
    public static string ApplyToPrefabs()
    {
        var done = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropRoot + "/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            int n = Swap(root);
            if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); done.Add(System.IO.Path.GetFileNameWithoutExtension(path)); }
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        return $"재질 압축본으로 바꾼 프리팹 {done.Count}개";
    }
}
