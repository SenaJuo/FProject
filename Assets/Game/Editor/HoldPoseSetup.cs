using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 손에 든 물건의 '쥔 자세'(HoldPose) 준비:
///  1) 블렌더에서 만든 자세 클립(Animation/Hold/Hold_Flashlight·Hold_ShieldBaton.fbx)을 사람형으로 들여온다
///  2) 공용 애니메이터에 팔 전용 레이어 두 개(HoldRight·HoldLeft, 기본 무게 0 — 주인공에는 영향 없음)를 만든다
///  3) 손전등 순찰·방패 경찰에 HoldPose를 붙인다(방패 경찰은 총 대신 진압봉)
/// </summary>
public static class HoldPoseSetup
{
    const string Dir = "Assets/Game/Animation/Hold";
    const string ControllerPath = "Assets/Game/Animation/HumanoidLocomotionIK.controller";

    [MenuItem("Game/Animation/Setup Hold Poses")]
    public static string Setup()
    {
        var flash = ImportClip("Hold_Flashlight");
        var shield = ImportClip("Hold_ShieldBaton");
        var right = Mask("RightArm", AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.RightFingers);
        var left = Mask("LeftArm", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.LeftFingers);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Layer(ac, "HoldRight", right, ("Flashlight", flash), ("Baton", shield));
        Layer(ac, "HoldLeft", left, ("Shield", shield));
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return $"자세 클립 {flash.name} {flash.length:0.0}초, {shield.name} {shield.length:0.0}초, 레이어 {string.Join(", ", ac.layers.Select(l => l.name))}";
    }

    static AnimationClip ImportClip(string name)
    {
        string path = $"{Dir}/{name}.fbx";
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.globalScale = 1.8f;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        var c = imp.defaultClipAnimations.First();
        c.name = name;
        c.loopTime = true;
        c.lockRootRotation = true; c.keepOriginalOrientation = true;
        c.lockRootHeightY = true; c.keepOriginalPositionY = true;
        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
        imp.clipAnimations = new[] { c };
        imp.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(x => !x.name.StartsWith("__preview"));
    }

    static AvatarMask Mask(string name, params AvatarMaskBodyPart[] parts)
    {
        string path = $"{Dir}/{name}.mask";
        var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (!m) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, path); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, parts.Contains((AvatarMaskBodyPart)i));
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Layer(AnimatorController ac, string name, AvatarMask mask, params (string state, AnimationClip clip)[] states)
    {
        var layers = ac.layers;
        int idx = System.Array.FindIndex(layers, l => l.name == name);
        if (idx < 0)
        {
            ac.AddLayer(name);
            layers = ac.layers;
            idx = layers.Length - 1;
        }
        var layer = layers[idx];
        layer.avatarMask = mask;
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.iKPass = false;
        var sm = layer.stateMachine;
        var empty = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Empty") ?? sm.AddState("Empty");
        sm.defaultState = empty;
        foreach (var (stateName, clip) in states)
        {
            var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName) ?? sm.AddState(stateName);
            st.motion = clip;
            st.writeDefaultValues = true;
        }
        layers[idx] = layer;
        ac.layers = layers;
    }

    /// <summary>적 하나에 쥔 자세를 붙인다. 손전등 순찰이면 손전등을, 방패 경찰이면 진압봉을 오른손에 쥐게 한다</summary>
    public static HoldPose Equip(EnemyAI ai)
    {
        var ik = ai.aimIK;
        if (!ik || !ai.body) return null;
        var hp = ai.GetComponent<HoldPose>();
        if (!hp) hp = ai.gameObject.AddComponent<HoldPose>();
        hp.animator = ai.body;
        hp.ik = ik;
        if (ai.GetComponent<ShieldGuard>())
        {
            hp.kind = HoldPose.Kind.ShieldBaton;
            var old = ai.body.transform.Find("Baton");
            if (old) Object.DestroyImmediate(old.gameObject);
            var baton = MakeBaton(ai.body.transform);
            var pistol = ai.body.transform.Find("Pistol");
            if (pistol) pistol.gameObject.SetActive(false);
            ik.heldItem = baton;
            ik.heldOffset = new Vector3(0f, 0f, 0.02f);
            ik.heldPitch = 65f; // 손에서 위로 세워 쥔다(팔뚝 방향 그대로면 방패 옆으로 길게 삐져나왔다)
            hp.item = baton;
        }
        else
        {
            hp.kind = HoldPose.Kind.Flashlight;
            hp.item = ik.heldItem;          // 손전등(Chapter1Builder.PatrolAs가 만든다)
            ik.heldOffset = new Vector3(0f, 0f, 0.06f); // 손전등 몸통이 주먹 안, 머리는 앞으로(0.03이면 손 안에 파묻혀 렌즈만 떠 보였다)
            ik.heldPitch = 0f;
        }
        ik.followForearm = true;
        // 손가락을 말아 쥔다(편 손이면 손전등·진압봉이 손가락 사이를 뚫고 지나갔다)
        var fingers = ai.body.GetComponentInChildren<FingerPose>(true);
        if (fingers) { fingers.alwaysFist = true; EditorUtility.SetDirty(fingers); }
        EditorUtility.SetDirty(ai.gameObject);
        return hp;
    }

    /// <summary>진압봉: 검은 고무 막대 + 손잡이 테이프(길이 60cm). 쥐는 곳이 원점, 앞(+z)으로 뻗는다</summary>
    static Transform MakeBaton(Transform parent)
    {
        var root = new GameObject("Baton").transform;
        root.SetParent(parent, false);
        var mat = Mat("BatonRubber", new Color(0.05f, 0.05f, 0.055f), 0.45f);
        var grip = Mat("BatonGrip", new Color(0.12f, 0.11f, 0.1f), 0.2f);
        Part(root, "Rod", new Vector3(0f, 0f, 0.24f), 0.034f, 0.56f, mat);
        Part(root, "Grip", new Vector3(0f, 0f, -0.01f), 0.038f, 0.14f, grip);
        Part(root, "Cap", new Vector3(0f, 0f, -0.085f), 0.044f, 0.012f, mat);
        return root;
    }

    static void Part(Transform root, string name, Vector3 at, float dia, float length, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localPosition = at;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 원기둥 축(y) → 앞(z)
        go.transform.localScale = new Vector3(dia, length * 0.5f, dia);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    static Material Mat(string name, Color c, float smooth)
    {
        string path = $"Assets/Game/Art/Prologue/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>지금 열린 장면의 손전등 순찰·방패 경찰 모두에게 붙인다(장면을 다시 짓지 않고)</summary>
    public static string ApplyToScene()
    {
        int flash = 0, shield = 0;
        foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool isShield = ai.GetComponent<ShieldGuard>();
            bool isTorch = ai.GetComponent<PatrolSight>() && ai.aimIK && ai.aimIK.heldItem && ai.aimIK.heldItem.name == "Flashlight";
            if (!isShield && !isTorch) continue;
            if (Equip(ai)) { if (isShield) shield++; else flash++; }
        }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        return $"{scene.name}: 손전등 {flash}명, 방패 {shield}명";
    }
}
