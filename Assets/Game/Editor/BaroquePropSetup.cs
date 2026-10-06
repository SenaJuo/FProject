using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 바로크 소품 GLB(가장 긴 변이 1, 중심이 원점)를 실제 크기·바닥 기준점·충돌체를 갖춘 프리팹으로 만든다.
/// 결과: Assets/Game/Art/Baroque/Props/Prefabs/{이름}.prefab — 맵 빌더는 이 프리팹만 쓴다.
/// </summary>
public static class BaroquePropSetup
{
    public const string PropRoot = "Assets/Game/Art/Baroque/Props";
    public const string PrefabRoot = PropRoot + "/Prefabs";

    enum Axis { X, Y, Z }

    // (이름, 기준 축, 그 축의 실제 길이 m, 충돌체 여부, 앞면 보정 회전°)
    // 손에 드는 작은 물건은 충돌체를 달지 않는다. 회전은 앞면이 +z(유니티 forward)를 보도록 맞추는 값
    static readonly (string name, Axis axis, float meters, bool collider, float yaw)[] Table =
    {
        // 현대 거리
        ("StreetLampLed",       Axis.Y, 6.0f,  true, 0f),
        ("BusStop",             Axis.Y, 2.6f,  true, 0f),
        ("Sedan",               Axis.Z, 4.8f,  true, 180f),
        ("EScooter",            Axis.Y, 1.2f,  true, 0f),
        ("VendingMachine",      Axis.Y, 1.9f,  true, 0f),
        ("PedestrianLight",     Axis.Y, 3.0f,  true, 0f),
        ("RecyclingBins",       Axis.Y, 1.0f,  true, 0f),
        ("ConstructionBarrier", Axis.Y, 1.0f,  true, 90f),
        ("CctvCamera",          Axis.Z, 0.5f,  false, 180f), // 렌즈가 앞(+z), 벽 고정판이 뒤
        ("ParcelLocker",        Axis.Y, 2.0f,  true, 0f),
        ("Dumpster",            Axis.X, 1.8f,  true, 0f),
        ("AcUnit",              Axis.Y, 0.8f,  true, 0f),
        ("FireHydrant",         Axis.Y, 0.8f,  true, 0f),
        ("TrashPile",           Axis.X, 1.6f,  true, 0f),
        // 주택가 소품(바로크 생성). 집 자체는 HouseBuilder로 직접 짓는다(생성 집은 창틀이 녹고 텍스처가 번져 쓰지 않는다)
        ("Mailbox",             Axis.Y, 1.15f, true, 0f),
        ("StreetTree",          Axis.Y, 7f,    true, 0f),
        ("Hedge",               Axis.X, 2.2f,  true, 90f),  // 돌린 뒤 긴 쪽이 x, 그 길이를 2.2m로
        // 주인공 집
        ("CrtTv",               Axis.Y, 0.45f, true, 0f),
        ("CassetteShelf",       Axis.Y, 1.6f,  true, 0f),
        ("Desk",                Axis.X, 1.3f,  true, -90f), // 서랍 면이 앞(+z), 긴 변이 x
        ("Bed",                 Axis.Z, 2.05f, true, 0f),   // 발치가 앞(+z)
        ("Sofa",                Axis.X, 2.1f,  true, 0f),
        ("Pistol",              Axis.Z, 0.19f, false, 0f),  // 총구가 +z
        // 손에 드는 물건
        ("ParcelBox",           Axis.Z, 0.35f, false, 0f),
        ("OpenedParcel",        Axis.X, 0.5f,  false, 0f),  // 뜯어진 택배 상자(날개를 펼친 폭)
        ("Walkman",             Axis.X, 0.13f, false, 0f),
        ("CassetteTape",        Axis.X, 0.10f, false, 0f),
        // 1장(바로크 3D, 2026-10-02): 레코드 가게 안, 공원, 순찰 손전등, 회복약 구급상자
        ("RecordCrate",         Axis.X, 0.62f, true,  0f),
        ("Turntable",           Axis.X, 0.46f, false, 0f),
        ("CashRegister",        Axis.X, 0.42f, false, 0f),
        ("ParkBin",             Axis.Y, 0.95f, true,  0f),
        ("Flashlight",          Axis.Z, 0.24f, false, -90f), // 원본은 렌즈(넓은 머리)가 +x: 돌려서 +z를 보게
        ("MedKit",              Axis.X, 0.24f, false, 0f),
        // 1장 구역 2 시장 광장(바로크 3D, 2026-10-06): 후보 둘씩 만들어 밝은 촬영장 4방향+45°로 비교해 고른 것
        ("PoliceCar",           Axis.Z, 4.9f,  true,  180f), // 원본은 앞(밀대 범퍼)이 -z
        ("BlackVan",            Axis.Z, 5.4f,  true,  0f),
        ("BoxTruck",            Axis.Z, 5.2f,  true,  0f),
        ("FoodCart",            Axis.Y, 2.3f,  true,  0f),   // 셔터 면이 앞
        ("RiotShield",          Axis.Y, 0.9f,  false, 0f),   // 바깥면(보기 창)이 앞
        ("SniperRifle",         Axis.Z, 1.2f,  false, 90f),  // 원본은 총구가 -x
        ("TrafficCone",         Axis.Y, 0.7f,  true,  0f),
        ("PoliceBarricade",     Axis.Y, 1.1f,  true,  0f),   // 줄무늬 판이 x로 길다
        ("HandCart",            Axis.Y, 1.2f,  true,  90f),
        ("GasCylinders",        Axis.Y, 1.2f,  true,  90f),
        ("StoolStack",          Axis.Y, 1.3f,  true,  0f),
        ("FloodLight",          Axis.Y, 2.2f,  true,  0f),   // 등이 앞(+z)을 비춘다
        ("PalletBoxes",         Axis.Y, 1.9f,  true,  0f),
        ("WaterDrums",          Axis.Y, 0.95f, true,  0f),
        ("CrateStack",          Axis.X, 1.2f,  true,  0f),   // 빈 나무 상자 둘(2차 생성)
    };

    [MenuItem("Game/Baroque/Rebuild Prop Prefabs")]
    public static void RebuildAll()
    {
        if (!AssetDatabase.IsValidFolder(PrefabRoot)) AssetDatabase.CreateFolder(PropRoot, "Prefabs");
        int made = 0;
        foreach (var e in Table)
            if (Build(e.name, e.axis, e.meters, e.collider, e.yaw) != null) made++;
        AssetDatabase.SaveAssets();
        Debug.Log($"[바로크 소품] 프리팹 {made}/{Table.Length}개 생성");
    }

    static string Build(string name, Axis axis, float meters, bool collider, float yaw)
    {
        string glb = $"{PropRoot}/{name}.glb";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
        if (source == null) { Debug.LogWarning($"[바로크 소품] {glb} 없음"); return null; }

        var root = new GameObject(name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); // 크기를 재기 전에 먼저 돌려야 축이 맞는다

        // 원래 크기를 재고, 기준 축이 실제 길이가 되도록 균일 배율을 준다
        var b = WorldBounds(model);
        float size = axis == Axis.X ? b.size.x : axis == Axis.Y ? b.size.y : b.size.z;
        float scale = meters / Mathf.Max(0.0001f, size);
        model.transform.localScale = Vector3.one * scale;

        // 바닥 가운데가 루트의 원점이 되게 옮긴다
        b = WorldBounds(model);
        model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        b = WorldBounds(model);

        if (collider)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
            if (System.Array.IndexOf(PoleColliders, name) >= 0)
            {
                // 기둥형 소품: 위로 뻗은 팔·신호등 상자까지 감싸면 그 아래 차도·인도가 보이지 않는 벽이 된다.
                // 땅에서 0.4m 아래 정점(기둥 밑동)의 범위만 위로 올려 충돌체로 쓴다.
                var foot = FootprintBounds(model, 0.4f);
                box.center = new Vector3(foot.center.x, b.size.y / 2f, foot.center.z);
                box.size = new Vector3(foot.size.x + 0.04f, b.size.y, foot.size.z + 0.04f);
            }
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        string path = $"{PrefabRoot}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return path;
    }

    static readonly string[] PoleColliders = { "StreetLampLed", "PedestrianLight", "StreetTree", "Mailbox" };

    /// <summary>땅에서 height 아래에 있는 정점들의 범위(루트 기준). 기둥 밑동처럼 실제로 땅에 닿는 부분을 잰다.</summary>
    static Bounds FootprintBounds(GameObject model, float height)
    {
        var root = model.transform.parent;
        bool any = false;
        var result = new Bounds();
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            var toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices)
            {
                var p = toRoot.MultiplyPoint3x4(v);
                if (p.y > height) continue;
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
