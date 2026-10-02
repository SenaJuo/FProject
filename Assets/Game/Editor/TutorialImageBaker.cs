using UnityEditor;
using UnityEngine;

/// <summary>
/// 설명 팝업에 넣을 그림을 프롤로그 장면에서 직접 찍어 Resources/Tutorials에 저장한다.
/// 프롤로그 장면이 열려 있어야 한다(Game/Prologue/Bake Tutorial Images).
/// </summary>
public static class TutorialImageBaker
{
    const string Folder = "Assets/Game/Resources/Tutorials";
    const int W = 800, H = 450;

    [MenuItem("Game/Prologue/Bake Tutorial Images")]
    public static string BakeAll()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game/Resources")) AssetDatabase.CreateFolder("Assets/Game", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Resources", "Tutorials");

        var desk = GameObject.Find("Prologue/Home/DeskWalkman");
        if (!desk) return "프롤로그 장면을 먼저 여세요";

        // 1) 되감기: 책상 위 카세트 플레이어
        Vector3 w = desk.transform.position;
        Save("rewind_1", QaShots.Render(w + new Vector3(0.36f, 0.26f, -0.26f), w + Vector3.up * 0.01f, 28f, W, H));

        // 2) 테이프와 충전: 카세트 플레이어의 테이프 창(릴) 가까이 — 진열장 모델은 책이 꽂힌 모양이라 쓰지 않는다
        var rsDesk = desk.GetComponentsInChildren<Renderer>();
        var db = rsDesk[0].bounds; foreach (var r in rsDesk) db.Encapsulate(r.bounds);
        Save("rewind_2", QaShots.Render(db.center + new Vector3(0.06f, 0.24f, -0.12f), db.center, 30f, W, H));

        // 3) 쓰러졌을 때: 대니 방 나무 바닥에 떨어진 카세트 플레이어(이야기에서 실제로 떨어지는 곳). 잠깐 놓고 작은 조명을 비춰 찍은 뒤 지운다
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Baroque/Props/Prefabs/Walkman.prefab");
        var dropped = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var key = new GameObject("TutorialKeyLight").AddComponent<Light>();
        try
        {
            const float floorY = 0f;
            dropped.transform.SetPositionAndRotation(new Vector3(103.6f, floorY, 67.6f), Quaternion.Euler(-90f, 0f, 35f));
            var rs = dropped.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            dropped.transform.position += Vector3.up * (floorY - b.min.y); // 바닥에 눕힌다
            Vector3 d = b.center + Vector3.up * (floorY - b.min.y);
            Vector3 cam = d + new Vector3(-0.5f, 0.18f, -0.36f);
            key.type = LightType.Point;
            key.color = new Color(1f, 0.86f, 0.7f);
            key.intensity = 0.8f;
            key.range = 1.4f;
            key.transform.position = d + (cam - d).normalized * 0.25f + Vector3.up * 0.4f;
            Save("rewind_3", QaShots.Render(cam, d, 30f, W, H));
        }
        finally
        {
            Object.DestroyImmediate(dropped);
            Object.DestroyImmediate(key.gameObject);
        }

        AssetDatabase.Refresh();
        foreach (var name in new[] { "rewind_1", "rewind_2", "rewind_3", "combat_1", "combat_2" }) // combat_*는 격투 시험(CombatVisualTest) 실제 화면에서 잘라 둔 그림
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{Folder}/{name}.png");
            imp.textureType = TextureImporterType.Default;
            imp.mipmapEnabled = false;
            imp.npotScale = TextureImporterNPOTScale.None; // 800×450 그대로(2의 거듭제곱으로 늘리지 않는다)
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        return "저장: rewind_1~3";
    }

    static void Save(string name, Texture2D tex)
    {
        System.IO.File.WriteAllBytes($"{Application.dataPath}/Game/Resources/Tutorials/{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
