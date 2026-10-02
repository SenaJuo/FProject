using UnityEngine;

/// <summary>
/// 검수용 사진: 지정한 위치·방향에서 장면을 렌더해 PNG로 저장하고, 여러 장을 한 장(격자)으로 묶는다.
/// 배치한 모델을 플레이어 거리에서 사방으로 확인할 때 쓴다.
/// </summary>
public static class QaShots
{
    public static Texture2D Render(Vector3 position, Vector3 lookAt, float fov, int width, int height)
    {
        var go = new GameObject("QaShotCamera") { hideFlags = HideFlags.HideAndDontSave };
        // 셰이더 비동기 컴파일 중에는 아직 준비 안 된 물체가 빠진 채 찍히므로, 찍는 동안만 끈다
        bool async = UnityEditor.ShaderUtil.allowAsyncCompilation;
        UnityEditor.ShaderUtil.allowAsyncCompilation = false;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render(); // 재질을 바꾼 직후 첫 렌더는 일부 물체가 빠질 수 있어 한 번 버린다
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
        finally
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = async;
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>여러 장을 columns열 격자로 묶어 저장한다(모두 같은 크기여야 한다)</summary>
    public static void Sheet(string path, Texture2D[] shots, int columns)
    {
        int w = shots[0].width, h = shots[0].height;
        int rows = (shots.Length + columns - 1) / columns;
        var sheet = new Texture2D(w * columns + 4 * (columns - 1), h * rows + 4 * (rows - 1), TextureFormat.RGB24, false);
        var fill = new Color32[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 0, 255, 255);
        sheet.SetPixels32(fill);
        for (int i = 0; i < shots.Length; i++)
        {
            int c = i % columns, r = rows - 1 - i / columns;
            sheet.SetPixels32(c * (w + 4), r * (h + 4), w, h, shots[i].GetPixels32());
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
        foreach (var s in shots) Object.DestroyImmediate(s);
        Object.DestroyImmediate(sheet);
    }
}
