using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 타이틀 장면(Title.unity)을 만든다: 검은 배경 카메라 + TitleScreen(배경 그림·로고).
/// 바로크(VARCO)로 뽑은 그림을 쓴다 — 배경: Art/Title/TitleBackground.png, 로고: 검은 바탕의 흰 "REWIND"를
/// 밝기 → 투명도로 바꾼 Art/Title/TitleLogo.png. 빌드 장면 목록 맨 앞에 넣는다(게임은 타이틀부터 시작).
/// </summary>
public static class TitleBuilder
{
    const string Dir = "Assets/Game/Art/Title";
    const string ScenePath = "Assets/Game/Scenes/Title.unity";

    /// <summary>검은 바탕 흰 글씨 로고 → 밝기를 투명도로(글씨만 남기고 바깥은 잘라 낸다)</summary>
    public static void ProcessLogo(string sourcePng)
    {
        var src = new Texture2D(2, 2);
        src.LoadImage(File.ReadAllBytes(sourcePng));
        int w = src.width, h = src.height;
        var px = src.GetPixels();
        var alpha = new float[px.Length];
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int i = 0; i < px.Length; i++)
        {
            float lum = px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f;
            alpha[i] = Mathf.Clamp01((lum - 0.08f) / 0.62f);
            if (alpha[i] > 0.1f)
            {
                int x = i % w, y = i / w;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
        }
        const int pad = 12;
        minX = Mathf.Max(0, minX - pad); minY = Mathf.Max(0, minY - pad);
        maxX = Mathf.Min(w - 1, maxX + pad); maxY = Mathf.Min(h - 1, maxY + pad);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        var dst = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        var paper = new Color(0.94f, 0.92f, 0.87f);
        var outPx = new Color[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                float a = alpha[(y + minY) * w + x + minX];
                outPx[y * cw + x] = new Color(paper.r, paper.g, paper.b, a);
            }
        dst.SetPixels(outPx);
        dst.Apply();
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Dir + "/TitleLogo.png", dst.EncodeToPNG());
        Object.DestroyImmediate(src);
        Object.DestroyImmediate(dst);
        AssetDatabase.ImportAsset(Dir + "/TitleLogo.png");
        Debug.Log($"[TitleBuilder] 로고 {cw}x{ch}");
    }

    static void ImportAs(string path, bool alpha)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Trilinear;
        imp.alphaIsTransparency = alpha;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
    }

    [MenuItem("Game/Build Title")]
    public static void Build()
    {
        ImportAs(Dir + "/TitleBackground.png", false);
        ImportAs(Dir + "/TitleLogo.png", true);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0; // 그릴 3D가 없다(그림은 화면 위에 그린다)
        cam.gameObject.AddComponent<AudioListener>();

        var title = new GameObject("TitleScreen").AddComponent<TitleScreen>();
        title.background = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/TitleBackground.png");
        title.logo = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/TitleLogo.png");

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.skybox = null;

        EditorSceneManager.SaveScene(scene, ScenePath);

        // 빌드 목록: 타이틀 → 프롤로그 → 1장
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("[TitleBuilder] 타이틀 장면 저장, 빌드 목록: " + string.Join(", ", list.Select(s => Path.GetFileNameWithoutExtension(s.path))));
    }
}
