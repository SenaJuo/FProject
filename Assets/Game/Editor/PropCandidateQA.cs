using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 바로크 소품 후보 검수: 후보 GLB를 실제 크기로 세워 1장 밤거리와 비슷한 조명(가로등 + 어두운 주변광 + 젖은 바닥) 아래에서
/// 앞·오른쪽·뒤·왼쪽·45° 위 다섯 방향을 플레이어 거리에서 찍어, 후보마다 한 줄씩 묶은 사진 한 장으로 저장한다.
/// 옆에 사람(조직원 모델, 1.8m)을 세워 크기도 함께 본다. 장면은 저장하지 않는다.
/// </summary>
public static class PropCandidateQA
{
    public enum Axis { X, Y, Z, Max }

    /// <summary>후보들(GLB 경로)을 같은 크기 기준으로 세워 한 장에 찍는다. 한 줄 = 한 후보, 다섯 칸 = 다섯 방향</summary>
    public static string Sheet(string[] glbPaths, Axis axis, float meters, float yaw, string outPath, int shot = 320) =>
        Sheet(glbPaths, axis, meters, yaw, outPath, InScene, shot);

    /// <summary>1장 큰길(가로등 사이 차도)에 세워 실제 배경·조명에서 본다. 장면은 저장하지 않는다</summary>
    public static readonly Vector3 InScene = new(162.2f, 0f, 114f);   // 서쪽 가로등(z 114) 바로 앞 차도
    /// <summary>밝은 촬영장(결함 확인용): 빈 장면에 고른 조명</summary>
    public static readonly Vector3 Studio = Vector3.zero;

    public static string Sheet(string[] glbPaths, Axis axis, float meters, float yaw, string outPath, Vector3 spot, int shot = 320)
    {
        const string scene = "Assets/Game/Scenes/Chapter1.unity";
        if (spot == Studio) { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); Stage(); }
        else if (EditorSceneManager.GetActiveScene().path != scene) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        Object.DestroyImmediate(QaShots.Render(spot + new Vector3(0f, 3f, -8f), spot, 50f, 64, 64)); // 셰이더 준비용으로 한 장 버린다
        var shots = new List<Texture2D>();
        var report = new System.Text.StringBuilder();
        foreach (var path in glbPaths)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) { report.AppendLine($"{path}: 없음"); continue; }
            var root = new GameObject("Candidate");
            root.transform.position = spot;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var b = Bounds(model);
            float size = axis == Axis.X ? b.size.x : axis == Axis.Y ? b.size.y : axis == Axis.Z ? b.size.z : Mathf.Max(b.size.x, b.size.y, b.size.z);
            model.transform.localScale = Vector3.one * (meters / Mathf.Max(0.0001f, size));
            b = Bounds(model);
            model.transform.localPosition = new Vector3(spot.x - b.center.x, spot.y - b.min.y, spot.z - b.center.z); // 바닥 가운데가 그 자리에
            b = Bounds(model);
            int tris = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
            report.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)}: 크기 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}m, 삼각형 {tris}");

            // 사람 기준(옆 1m 떨어진 곳)
            // 1m가 넘는 물건만 옆에 사람을 세운다(작은 물건은 사람이 화면을 가린다)
            var person = b.size.y >= 1f ? Person(new Vector3(b.max.x + 0.9f, spot.y, b.min.z - 0.9f)) : null; // 비스듬히 뒤쪽(어느 방향에서도 가리지 않게)
            float radius = Mathf.Max(b.extents.x, b.extents.z, b.extents.y * 0.8f);
            float dist = Mathf.Clamp(radius * 2.6f + (b.size.y >= 1f ? 1.2f : 0.4f), 0.8f, 9f);   // 플레이어 카메라 거리(작은 물건 2m대, 차 7~9m)
            Vector3 c = spot + new Vector3(0f, b.size.y * 0.5f, 0f);
            float eye = b.size.y >= 1f ? Mathf.Clamp(1.6f, b.size.y * 0.3f, 6f) : b.size.y * 0.5f + radius * 0.8f; // 작은 물건은 가까이 비스듬히
            foreach (float a in new[] { 0f, 90f, 180f, 270f })
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                shots.Add(QaShots.Render(c + dir * dist + Vector3.up * (eye - b.size.y * 0.5f), c, 50f, shot, shot));
            }
            Vector3 d45 = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward;
            shots.Add(QaShots.Render(c + d45 * dist * 0.85f + Vector3.up * (b.size.y * 0.9f + 1.4f), c, 50f, shot, shot));
            Object.DestroyImmediate(root);
            if (person) Object.DestroyImmediate(person);
        }
        if (shots.Count > 0) QaShots.Sheet(outPath, shots.ToArray(), 5);
        return report.ToString();
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>1장 밤거리 조명을 흉내 낸 촬영장: 젖은 아스팔트, 가로등(주황) 두 개, 푸른 달빛, 어두운 주변광</summary>
    static void Stage()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.08f);
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.012f;
        var groundMat = AssetDatabase.LoadAssetAtPath<Material>(PrologueBuilder.ArtRoot + "/Materials/Asphalt.mat")
                        ?? new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 6f;
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        var cam = new GameObject("Sky").AddComponent<Camera>(); // 하늘색(빈 배경)을 밤색으로
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
        cam.enabled = false;
        Light(new Vector3(4f, 6f, 3f), new Color(1f, 0.78f, 0.52f), 26f, 18f);
        Light(new Vector3(-5f, 6f, -4f), new Color(1f, 0.78f, 0.52f), 18f, 18f);
        var moon = new GameObject("Moon").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.55f, 0.65f, 0.9f);
        moon.intensity = 1.1f;
        moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        moon.shadows = LightShadows.Soft;
    }

    static void Light(Vector3 p, Color c, float intensity, float range)
    {
        var l = new GameObject("Lamp").AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.Soft;
        l.transform.position = p;
    }

    static GameObject Person(Vector3 at)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaroqueCharacterSetup.CharacterRoot + "/Enforcer/Enforcer_Model.prefab");
        if (!prefab) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, -90f, 0f));
        return go;
    }
}
