using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 바로크(3D 생성 도구)에서 받은 리깅 캐릭터를 게임에서 바로 쓸 수 있는 프리팹으로 만든다.
/// 폴더 구성: {이름}.fbx(뼈대·스킨) + {이름}_BaseColor/_MetalRough/_Normal.png(GLB에서 추출한 텍스처)
/// FBX에는 텍스처가 들어 있지 않아서, 텍스처는 따로 받아 URP 머티리얼로 다시 묶는다.
/// </summary>
public static class BaroqueCharacterSetup
{
    const string StarterControllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    // Starter Assets 원본을 복사해 IK Pass만 켠 컨트롤러(상자 들기·총 겨누기·시선 IK용). 원본은 건드리지 않는다
    public const string ControllerPath = "Assets/Game/Animation/HumanoidLocomotionIK.controller";

    public static RuntimeAnimatorController EnsureIKController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (controller == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Animation")) AssetDatabase.CreateFolder("Assets/Game", "Animation");
            AssetDatabase.CopyAsset(StarterControllerPath, ControllerPath);
            controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        }
        var layers = controller.layers; // 배열 복사본이라 고친 뒤 다시 넣어야 저장된다
        layers[0].iKPass = true;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        return controller;
    }
    public const string CharacterRoot = "Assets/Game/Art/Baroque/Characters";

    [MenuItem("Game/Baroque/Rebuild Character Prefabs")] // 메뉴·파일 이름은 영어로 (한글 경로 문제 방지)
    static void RebuildAll()
    {
        foreach (var name in new[] { "Protagonist", "Enforcer", "Friend" })
            if (File.Exists($"{CharacterRoot}/{name}/{name}.fbx")) Setup(name, 1.8f);
    }

    /// <summary>캐릭터 하나를 설정하고, 만들어진 모델 프리팹 경로를 돌려준다.</summary>
    public static string Setup(string name, float targetHeight)
    {
        string folder = $"{CharacterRoot}/{name}";
        string fbxPath = $"{folder}/{name}.fbx";

        // 1) 휴머노이드로 가져오고, 실제 키에 맞게 배율을 정한다
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; // 머티리얼은 아래에서 직접 만든다
        importer.globalScale = 1f;
        importer.SaveAndReimport();
        float height = MeasureHeight(fbxPath);
        importer.globalScale = targetHeight / height;
        importer.SaveAndReimport();

        // 2) 텍스처 설정 + 금속/거칠기 → URP 금속/매끄러움 변환
        var baseColor = PrepareTexture($"{folder}/{name}_BaseColor.png", TextureImporterType.Default, true);
        var normal = PrepareTexture($"{folder}/{name}_Normal.png", TextureImporterType.NormalMap, false);
        var metallic = ConvertMetalRough($"{folder}/{name}_MetalRough.png", $"{folder}/{name}_MetallicSmoothness.png");

        // 3) URP Lit 머티리얼
        string matPath = $"{folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetTexture("_BaseMap", baseColor);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", normal);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", metallic);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Smoothness", 1f);                // 매끄러움은 텍스처 알파값을 그대로 쓴다
        mat.SetFloat("_SmoothnessTextureChannel", 0f);  // 0 = 금속 텍스처의 알파 채널
        EditorUtility.SetDirty(mat);

        // 4) 모델 프리팹: 애니메이터(Kyle과 같은 걷기·달리기·점프) + 머티리얼
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = name;
        // Unity 오브젝트는 ?? 연산자가 "없음"을 제대로 판단하지 못하므로 명시적으로 확인한다
        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = EnsureIKController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.updateWhenOffscreen = true; // 컷신 근접 촬영 때 화면 가장자리에서 잘려 사라지지 않게
        }
        string prefabPath = $"{folder}/{name}_Model.prefab";
        PrefabUtility.SaveAsPrefabAsset(model, prefabPath);
        Object.DestroyImmediate(model);
        AssetDatabase.SaveAssets();
        Debug.Log($"[바로크 캐릭터] {name}: 원래 키 {height:0.00}m → {targetHeight:0.00}m (배율 {importer.globalScale:0.000}) · {prefabPath}");
        return prefabPath;
    }

    static float MeasureHeight(string fbxPath)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        float min = float.MaxValue, max = float.MinValue;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            // 원본 메시 정점으로 잰다(렌더러 범위는 자세·갱신 상태에 따라 부정확할 수 있음)
            var m = r.transform.localToWorldMatrix;
            foreach (var v in r.sharedMesh.vertices)
            {
                float y = m.MultiplyPoint3x4(v).y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }
        }
        return Mathf.Max(0.01f, max - min);
    }

    static Texture2D PrepareTexture(string path, TextureImporterType type, bool sRGB)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = type;
        ti.sRGBTexture = sRGB;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 바로크 금속도 채널은 실제 금속 부위(헤드폰·지퍼)가 아니라 가죽·피부·머리카락에 무작위로 0.12~0.5가 깔려 있어서
    // 그대로 쓰면 사람이 청동상처럼 보인다(주인공 텍스처 픽셀의 98%가 금속 32~127/255, 얼굴 평균 75/255).
    // 같은 자리·같은 카메라로 비교한 결과 금속 0 + 매끄러움 0.75배가 피부·가죽 모두 가장 자연스러웠다.
    const float MetallicScale = 0f;
    const float SmoothnessScale = 0.75f;

    /// <summary>glTF 금속/거칠기(G=거칠기, B=금속) → URP 금속/매끄러움(R=금속, A=매끄러움=1-거칠기).</summary>
    static Texture2D ConvertMetalRough(string sourcePath, string outPath)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
        ti.sRGBTexture = false;
        ti.isReadable = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();

        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        var pixels = src.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            byte metal = (byte)(p.b * MetallicScale);
            pixels[i] = new Color32(metal, metal, metal, (byte)((255 - p.g) * SmoothnessScale));
        }
        var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
        dst.SetPixels32(pixels);
        dst.Apply();
        File.WriteAllBytes(outPath, dst.EncodeToPNG());
        Object.DestroyImmediate(dst);
        AssetDatabase.ImportAsset(outPath);

        var oi = (TextureImporter)AssetImporter.GetAtPath(outPath);
        oi.sRGBTexture = false;
        oi.alphaSource = TextureImporterAlphaSource.FromInput;
        oi.maxTextureSize = 2048;
        oi.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }
}
