using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 맨손 격투 동작을 무료 애니메이션으로 입힌다(바로크 생성 동작은 팔이 비스듬히 올라가고 발차기가 어색해 바꿨다).
///  · 잽·크로스: Quaternius Universal Animation Library(CC0) — 가드에서 어깨 높이로 곧게 뻗었다가 돌아온다
///  · 돌려차기·맞는 동작: Motifect Combat Motion Pack(상업 사용 무료, 원본 파일 재배포 금지)
/// 모두 '제자리' 동작이라 회전·높이·수평 위치를 포즈에 굽는다. Quaternius 원본은 -z를 보고 있어 방향만 '몸 방향' 기준으로 굽는다.
/// 재생 속도는 손발이 가장 멀리 뻗는 순간(실측)이 공격 판정 시각(PlayerController 콤보의 hitTime)과 맞도록 정했다.
///   잽: 원본 최고점 0.22초 → 속도 0.9 → 0.245초(판정 0.25)   크로스: 0.28초 → 속도 1.0(판정 0.27)
///   돌려차기: 1.0~3.0초 구간, 최고점 구간 안 0.74초 → 속도 1.64 → 0.45초(판정 0.45)
///   얼굴 맞음: 0.75~2.1초 구간, 머리가 가장 젖혀지는 순간 0.45초 → 속도 1.8 → 0.25초
///   몸통 맞음: 1.0~2.6초 구간, 가장 숙여지는 순간 0.5초 → 속도 1.6 → 0.31초
/// </summary>
public static class CombatAnimationSetup
{
    const string Root = "Assets/Game/Art/ThirdParty/Animations/";
    const string Quaternius = Root + "Quaternius/UAL1_Standard.fbx";
    const string LocomotionState = "Idle Walk Run Blend";

    // (상태, 파일, 원본 클립 이름 끝부분(Quaternius) 또는 null(Motifect 한 개짜리), 구간 시작·끝 초(0이면 전체), 재생 속도, 걷기로 돌아가는 지점)
    static readonly (string state, string file, string take, float from, float to, float speed, float exitAt)[] States =
    {
        ("Jab",      Quaternius,                                 "Punch_Jab",   0f,    0f,   0.9f,  0.7f),
        ("Cross",    Quaternius,                                 "Punch_Cross", 0f,    0f,   1.0f,  0.75f),
        ("Kick",     Root + "Motifect/roundhouse_kick_right.fbx", null,         1.0f,  3.0f, 1.64f, 0.75f),
        ("HitLight", Root + "Motifect/hit_react_face.fbx",        null,         0.75f, 2.1f, 1.8f,  0.7f),
        ("HitHeavy", Root + "Motifect/hit_react_chest.fbx",       null,         1.0f,  2.6f, 1.6f,  0.7f),
        // 회피 = 구르기: 원본 1.47초(웅크림 → 몸 날림 → 바닥에서 한 바퀴 → 일어섬)를 1.7배로 0.86초. 이동은 처음 0.55초(PlayerController)
        ("Roll",     Quaternius,                                 "|Roll",       0f,    0f,   1.7f,  0.92f),
    };

    [MenuItem("Game/Animation/Setup Combat Clips")]
    public static string Apply()
    {
        var report = new System.Text.StringBuilder();
        ConfigureQuaternius();
        foreach (var s in States.Where(s => s.take == null)) ConfigureMotifect(s.file, s.state, s.from, s.to);

        var controller = (AnimatorController)BaroqueCharacterSetup.EnsureIKController();
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.states.First(x => x.state.name == LocomotionState).state;
        foreach (var s in States)
        {
            var clip = FindClip(s.file, s.take ?? s.state);
            if (clip && s.state == "Roll") clip = TightRoll(clip);
            if (!clip) { report.AppendLine($"{s.state}: 클립 없음"); continue; }
            var state = machine.states.FirstOrDefault(x => x.state.name == s.state).state
                        ?? machine.AddState(s.state, new Vector3(520, 60 * System.Array.IndexOf(States, s), 0));
            state.motion = clip;
            state.speed = s.speed;
            foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);
            var back = state.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = s.exitAt;
            back.duration = 0.12f;
            back.hasFixedDuration = true;
            report.AppendLine($"{s.state}: {clip.name} {clip.length:0.00}초 × 속도 {s.speed} → {clip.length / s.speed:0.00}초");
        }
        // 쓰지 않는 상태(예전 바로크 '휘청')는 지운다
        foreach (var x in machine.states.Where(x => x.state.name is "Stagger" or "Backstep").ToArray()) machine.RemoveState(x.state);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        // 구르기 재생 빠르기는 RollSpeed 값으로(이동 시간에 맞춘다)
        report.AppendLine(RollSetup.Apply());
        return report.ToString();
    }

    static void ConfigureQuaternius()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(Quaternius);
        Humanoid(imp);
        var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            InPlace(c);
            c.keepOriginalOrientation = false; // 원본 파일이 -z를 본다 → 방향은 몸 방향 기준
            c.loopTime = c.name.Contains("Loop");
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    static void ConfigureMotifect(string path, string name, float from, float to)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        Humanoid(imp);
        var c = (imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations)[0];
        c.name = name;
        if (to > from)
        {
            c.firstFrame = from * 30f; // 원본은 30fps
            c.lastFrame = to * 30f;
        }
        InPlace(c);
        c.loopTime = false;
        imp.clipAnimations = new[] { c };
        imp.SaveAndReimport();
    }

    static void Humanoid(ModelImporter imp)
    {
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        imp.importCameras = false;
        imp.importLights = false;
    }

    static void InPlace(ModelImporterClipAnimation c)
    {
        c.lockRootRotation = true; c.keepOriginalOrientation = true;
        c.lockRootHeightY = true; c.keepOriginalPositionY = true;
        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
    }

    /// <summary>
    /// 구르기 원본은 다리를 옆으로 크게 벌린다(오른 허벅지 벌림 근육값이 최대 1.0) — 사람이 구를 때는 무릎을 모은다.
    /// 허벅지 벌림(In-Out)은 0.15배, 허벅지·정강이 비틀림은 0.3배로 줄인 사본을 만든다(나머지 곡선은 그대로).
    /// </summary>
    public const float LegSpreadScale = 0.15f, LegTwistScale = 0.3f; // 원본·0.3배·0.15배를 비교해 고름(무릎 최대 간격 77 → 47cm)

    static AnimationClip TightRoll(AnimationClip source)
    {
        const string path = "Assets/Game/Animation/Roll_Tight.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = source.frameRate;
        foreach (var b in AnimationUtility.GetCurveBindings(source))
        {
            var curve = AnimationUtility.GetEditorCurve(source, b);
            string n = b.propertyName;
            float scale = n.Contains("Upper Leg In-Out") ? LegSpreadScale
                        : n.Contains("Leg Twist In-Out") ? LegTwistScale : 1f;
            if (scale != 1f)
            {
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++) { keys[i].value *= scale; keys[i].inTangent *= scale; keys[i].outTangent *= scale; }
                curve.keys = keys;
            }
            AnimationUtility.SetEditorCurve(clip, b, curve);
        }
        AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationClip FindClip(string path, string nameEnd) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview") && c.name.EndsWith(nameEnd));
}
