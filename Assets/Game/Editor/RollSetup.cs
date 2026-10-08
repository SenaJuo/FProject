using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 회피 구르기: Quaternius 다이빙 구르기(Roll_Tight — 다리 벌림을 줄인 사본)를 애니메이터 Roll 상태에 넣고,
/// 재생 빠르기는 RollSpeed 값으로 PlayerController가 이동 + 일어서기 시간에 맞춘다.
/// (블렌더로 손수 키를 잡은 구르기 두 가지는 사용자 검토에서 탈락 — 모션캡처 다이빙이 더 멋있다)
/// </summary>
public static class RollSetup
{
    const string ClipPath = "Assets/Game/Animation/Roll_Tight.anim";
    const string ControllerPath = "Assets/Game/Animation/HumanoidLocomotionIK.controller";

    [MenuItem("Game/Animation/Setup Roll")]
    public static string Apply()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (!clip) return "구르기 클립 없음: " + ClipPath;
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!ac.parameters.Any(p => p.name == "RollSpeed"))
            ac.AddParameter(new AnimatorControllerParameter { name = "RollSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1.7f });
        var roll = ac.layers[0].stateMachine.states.Select(s => s.state).First(s => s.name == "Roll");
        roll.motion = clip;
        roll.speed = 1f;
        roll.speedParameterActive = true;
        roll.speedParameter = "RollSpeed";
        foreach (var t in roll.transitions) if (t.hasExitTime) { t.exitTime = 0.97f; t.duration = 0.12f; }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return $"구르기 클립 {clip.name} {clip.length:0.000}초, Roll 상태 연결(빠르기 = RollSpeed)";
    }

    /// <summary>지금 열린 장면의 주인공 회피 값을 새 기본값으로 맞춘다(장면에 예전 값이 저장돼 있다)</summary>
    public static string ApplyToScenePlayer()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (!pc) return "주인공 없음";
        Undo.RecordObject(pc, "Roll values");
        Set(pc);
        EditorUtility.SetDirty(pc);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);
        return "장면 주인공 회피 값 갱신: " + pc.gameObject.scene.name;
    }

    public static void Set(PlayerController pc)
    {
        pc.dodgeDistance = 3.8f; pc.dodgeDuration = 0.64f; pc.dodgeRecovery = 0.18f; pc.dodgeIFrames = 0.35f; pc.dodgeCooldown = 0.25f;
    }
}
