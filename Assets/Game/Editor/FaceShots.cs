using UnityEditor;
using UnityEngine;

/// <summary>얼굴 표정 확인 사진(플레이 중): 표정마다 몇 프레임 기다린 뒤 찍는다(블렌드셰이프는 다음 그리기에 반영된다). 결과 QA/face_expr.png</summary>
public static class FaceShots
{
    static readonly string[] Names = { "Blink_L", "Blink_R", "BrowDown", "BrowSadInner", "Squint", "MouthFrown", "MouthPress", "MouthOpen" };
    static readonly float[][] Poses =
    {
        new float[] { 0, 0, 0, 0, 0, 0, 0, 0 },          // 평소
        new float[] { 100, 100, 0, 0, 0, 0, 0, 0 },      // 눈 감음
        new float[] { 0, 0, 100, 0, 60, 0, 100, 0 },     // 싸움(찌푸림)
        new float[] { 0, 0, 0, 100, 80, 100, 0, 0 },     // 아픔
        new float[] { 0, 0, 0, 0, 0, 0, 0, 70 },         // 말하기
    };
    static int step, wait;
    static Texture2D sheet;
    static SkinnedMeshRenderer smr;
    static Camera cam;
    public static float Distance = 0.75f;

    public static void Begin()
    {
        var p = Object.FindAnyObjectByType<PlayerController>();
        var fe = p.animator.GetComponent<FaceExpression>(); if (fe) fe.enabled = false;
        smr = p.animator.GetComponentInChildren<SkinnedMeshRenderer>();
        var head = p.animator.GetBoneTransform(HumanBodyBones.Head);
        cam = new GameObject("qa_face").AddComponent<Camera>();
        cam.CopyFrom(Camera.main); cam.fieldOfView = 22f; cam.nearClipPlane = 0.05f; cam.enabled = false;
        cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
        Vector3 face = head.position + Vector3.up * 0.09f;
        cam.transform.position = face + p.transform.forward * Distance + Vector3.up * 0.02f; cam.transform.LookAt(face);
        sheet = new Texture2D(500 * Poses.Length, 600);
        step = 0; wait = 0;
        Set(0);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void Set(int k) { for (int i = 0; i < Names.Length; i++) smr.SetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex(Names[i]), Poses[k][i]); }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (++wait < 4) return;
        wait = 0;
        var rt = new RenderTexture(500, 600, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, 500, 600), 500 * step, 0);
        RenderTexture.active = null; cam.targetTexture = null; rt.Release();
        step++;
        if (step < Poses.Length) { Set(step); return; }
        sheet.Apply();
        System.IO.File.WriteAllBytes(@"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\face_expr.png", sheet.EncodeToPNG());
        for (int i = 0; i < Names.Length; i++) smr.SetBlendShapeWeight(i, 0);
        Object.Destroy(cam.gameObject);
        EditorApplication.update -= Tick;
        Debug.Log("[얼굴 사진] QA/face_expr.png");
    }
}
