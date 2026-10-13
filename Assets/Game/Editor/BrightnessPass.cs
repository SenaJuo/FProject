using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 밝기 손질(2026-10-12, "맵들이 처음부터 끝까지 너무 어두워"): 이미 지은 장면의 밤 조명을 한꺼번에 올린다.
/// 주변광·달빛·안개·화면 노출·가장자리 어둡게(비네트). 하늘이 밤 값을 다시 덮어쓰는 장면(SkyCycle)은 그 값도 함께.
/// 빌더(PrologueBuilder.SetupEnvironment 등)의 기본값도 같은 값으로 바꿔 두었다 — 다시 지어도 유지된다.
/// </summary>
public static class BrightnessPass
{
    public static readonly Color Ambient = new(0.4f, 0.41f, 0.48f);
    public const float Moon = 0.75f, FogDensity = 0.011f, PostExposure = 1.7f, VignetteIntensity = 0.24f;
    public static readonly Color FogColor = new(0.07f, 0.078f, 0.105f);

    /// <summary>지금 열린 장면에 적용하고 저장한다</summary>
    public static string ApplyToOpenScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        RenderSettings.ambientLight = Ambient;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogColor = FogColor;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.name == "Moonlight") { l.intensity = Moon; EditorUtility.SetDirty(l); }
        foreach (var sky in Object.FindObjectsByType<SkyCycle>(FindObjectsSortMode.None))
        {
            sky.nightAmbient = Ambient;
            sky.nightFog = FogColor;
            sky.nightFogDensity = FogDensity;
            sky.nightMoon = Moon;
            EditorUtility.SetDirty(sky);
        }
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) Tune(v.sharedProfile);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return scene.name;
    }

    public static void Tune(VolumeProfile profile)
    {
        if (!profile) return;
        if (profile.TryGet(out ColorAdjustments color)) color.postExposure.Override(PostExposure);
        if (profile.TryGet(out Vignette vignette)) vignette.intensity.Override(VignetteIntensity);
        EditorUtility.SetDirty(profile);
    }

    [MenuItem("Game/Lighting/Brighten All Story Scenes")]
    public static string ApplyAll()
    {
        string log = "";
        foreach (var path in new[] { "Assets/Game/Scenes/Prologue.unity", "Assets/Game/Scenes/Chapter1.unity", "Assets/Game/Scenes/Chapter2.unity" })
        {
            EditorSceneManager.OpenScene(path);
            log += ApplyToOpenScene() + " ";
        }
        AssetDatabase.SaveAssets();
        return "밝기 손질: " + log;
    }
}
