using UnityEditor;
using UnityEngine;

/// <summary>
/// 주인공 목에 걸린 헤드폰: 생성 모델에는 양쪽 귀 덮개만 있고 둘을 잇는 밴드가 없었다.
/// 블렌더에서 만든 밴드(Headphone_Band.fbx, 원본 모델과 같은 좌표·1m 기준)를 목 뼈에 붙인다.
/// 붙일 자리는 피부 메시의 바인드 포즈(뼈가 처음 놓였던 자리)로 계산한다 — 지금 애니메이션 자세와 상관없이 정확하다.
/// </summary>
public static class HeadphoneBandSetup
{
    const string Dir = "Assets/Game/Art/Baroque/Characters/Protagonist/";
    const string BandPath = Dir + "Headphone_Band.fbx";
    const string MatPath = Dir + "Headphone_Band.mat";
    public const string ObjectName = "Headphone_Band";
    /// <summary>원본 모델은 키 1m로 만들어 1.8배로 들여온다</summary>
    const float ModelScale = 1.8f;

    /// <summary>모델 아래의 주인공 피부 메시를 찾아 목 뼈에 밴드를 붙인다(이미 있으면 다시 맞춘다). 붙인 개수를 돌려준다</summary>
    public static int Attach(GameObject root)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BandPath);
        if (!mesh) throw new System.InvalidOperationException("밴드 메시 없음: " + BandPath);
        int n = 0;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.sharedMaterial || !smr.sharedMaterial.name.StartsWith("Protagonist")) continue;
            int ni = System.Array.FindIndex(smr.bones, b => b && b.name == "Neck");
            if (ni < 0) continue;
            Transform neck = smr.bones[ni];
            var existing = neck.Find(ObjectName);
            var go = existing ? existing.gameObject : new GameObject(ObjectName);
            go.transform.SetParent(neck, false);
            // 밴드 점(원본 1m 좌표, Y-up) → 모델 루트 공간: 1.8배. 모델 루트 공간 → 메시 공간 → 목 뼈 공간(바인드 포즈)
            Transform modelRoot = smr.transform.parent; // 피부 메시는 모델 루트 바로 아래(회전 270°만 있다)
            Matrix4x4 meshInModel = Matrix4x4.TRS(smr.transform.localPosition, smr.transform.localRotation, smr.transform.localScale);
            Matrix4x4 neckInModel = meshInModel * smr.sharedMesh.bindposes[ni].inverse;
            Matrix4x4 bandInNeck = neckInModel.inverse * Matrix4x4.Scale(Vector3.one * ModelScale);
            go.transform.localPosition = bandInNeck.GetColumn(3);
            go.transform.localRotation = bandInNeck.rotation;
            go.transform.localScale = bandInNeck.lossyScale;
            var mf = go.GetComponent<MeshFilter>();
            if (!mf) mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            if (!mr) mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material();
            mr.renderingLayerMask = smr.renderingLayerMask; // 적 키 조명 등 같은 빛을 받게
            go.layer = smr.gameObject.layer;
            n++;
        }
        return n;
    }

    static Material Material()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        // 귀 덮개와 같은, 닳은 어두운 금속·플라스틱 빛
        mat.SetColor("_BaseColor", new Color(0.30f, 0.27f, 0.24f));
        mat.SetFloat("_Metallic", 0.5f);
        mat.SetFloat("_Smoothness", 0.45f);
        AssetDatabase.CreateAsset(mat, MatPath);
        return mat;
    }

    [MenuItem("Game/Baroque/Attach Headphone Band (Player prefab + open scene)")]
    public static string AttachEverywhere()
    {
        string log = "";
        const string prefabPath = "Assets/Game/Prefabs/Player.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        log += $"프리팹 {Attach(prefab)}개";
        PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        PrefabUtility.UnloadPrefabContents(prefab);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        int s = 0;
        foreach (var go in scene.GetRootGameObjects()) s += Attach(go);
        if (s > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        log += $", 장면 {scene.name} {s}개";
        return log;
    }
}
