using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using P = PrologueBuilder;

/// <summary>
/// 2장 「내사」 장면 — 지금은 구역 1(경찰서 뒤 압수차량 보관소)까지. 메뉴: Game/Build Chapter 2. 1장 빌더의 도구(철망·탐조등·투광등·창고·가게)를 그대로 쓴다.
///
/// 배치(미터, x 동쪽 · z 북쪽):
///   뒷골목(시작): 서쪽 건물(x -22~-2)과 동쪽 건물(x 8~30) 사이 골목 x -2~8, z -48~2. 남쪽 끝은 막다른 벽.
///     골목 서쪽 건물 1층에 정보원 영감의 「골목 전파사」(앞면 x -2, z -26~-17) = 저장 지점·테이프 교환.
///   뒷길(동서): z 2~12, x -30~150. 길 북쪽이 보관소 철망(z 14), 남쪽은 창고 줄.
///   압수차량 보관소: x 10~140, z 14~100. 서쪽 끝 철망이 썩어 들춰진 틈(x 14). 동쪽 차량 문(x 120~132)과 경비 초소.
///     차 다섯 줄(z 26·40·54·68·82, 차는 남북으로 세움), 가운데 감시탑(x 75, z 60, 높이 7m)의 탐조등, 투광등 기둥 여섯.
///     북동쪽 구석 차고 옆에 1장의 검은 승합차. 서북쪽에 보관소 사무실 컨테이너.
///   경찰서: 보관소 북쪽(z 106~140), 뒷면이 보관소를 본다. 북쪽 철망(z 100)의 사람 문(x 67~73)을 지나 뒷문(x 70, z 106).
/// </summary>
public static partial class Chapter1Builder
{
    const string Ch2ScenePath = "Assets/Game/Scenes/Chapter2.unity";
    const string Ch2NavMeshPath = "Assets/Game/Scenes/Chapter2_NavMesh.asset";
    const string Ch2VolumePath = P.ArtRoot + "/Chapter2Volume.asset";
    const float LotX0 = 10f, LotX1 = 140f, LotZ0 = 14f, LotZ1 = 100f;
    const float AlleyX0 = -2f, AlleyX1 = 8f;
    const float StationZ0 = 106f, StationZ1 = 140f;

    class Built2
    {
        public Transform zoneStart, shopSave, fenceInside, van, vanSave, backDoor, towerTop;
        public TapeShop shop;
        public StoryTrigger shopApproach, backDoorTrigger;
        public StoryInteractable fenceGap, vanUse;
        public Searchlight searchlight;
        public Light vanDome;
    }

    [MenuItem("Game/Build Chapter 2")]
    static void BuildChapter2Menu()
    {
        if (EditorUtility.DisplayDialog("2장 만들기", $"{Ch2ScenePath}를 새로 만듭니다(기존 내용은 덮어씀).", "만들기", "취소")) BuildChapter2();
    }

    public static void BuildChapter2()
    {
        P.boxMeshes.Clear();
        beamCone = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        P.root = new GameObject("Chapter2").transform;
        var M = P.CreateMaterials();
        var X = ExtraMaterials();
        var H = HarborMaterials();
        var b = new Built2();
        var triggers = new GameObject("Triggers").transform;
        var people = new GameObject("Police").transform;
        var routes = new GameObject("PatrolRoutes").transform;

        var g = P.Group("Ch2_Zone1");
        Ch2Ground(g, M);
        Ch2Alley(g, M, X, H, triggers, b);
        Ch2Road(g, M, H);
        Ch2Lot(g, M, H, triggers, b);
        Ch2Station(g, M, X, H, triggers, b);
        Ch2Lights(g, M, H);

        var bi = new BuiltInside();
        Ch2Interior(g, M, triggers, people, routes, bi);
        var be = new BuiltEscape();
        Ch2Escape(g, triggers, be); // 구역 3(탈출): 실내 형광등·주차장 경찰차를 찾아 쓰므로 그 둘 뒤에

        b.zoneStart = P.Marker(triggers, "Ch2Start", new Vector3(3f, 0f, -42f), 0f);
        var player = P.CreatePlayer(b.zoneStart, out _, out _, out _, out _);
        Ch2Police(people, routes, triggers, H, b);

        // ---------- 카메라 ----------
        var mainCam = Object.FindAnyObjectByType<Camera>();
        mainCam.farClipPlane = 400f;
        mainCam.nearClipPlane = 0.05f;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
        mainCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
        var cams = new GameObject("Cameras").transform;
        var start = b.zoneStart.position;
        var gameplay = P.VCam(cams, "GameplayCamera", start + new Vector3(0f, 2f, -3f), start + Vector3.up * 1.5f, 55f);
        var rig = gameplay.gameObject.AddComponent<ThirdPersonCamera>();
        rig.target = player.transform;
        rig.distance = 4.8f;
        var dynA = P.VCam(cams, "Shot_Dynamic_A", Vector3.zero, Vector3.forward, 40f);

        // ---------- 시스템 ----------
        var systems = new GameObject("GameSystems");
        var gm = systems.AddComponent<GameManager>();
        gm.player = player;
        gm.stageTitle = "";
        gm.finalObjective = "";
        systems.AddComponent<HUD>().player = player;
        systems.AddComponent<RewindEffect>();
        var rainAudio = new GameObject("RainAudio").AddComponent<AudioSource>();
        rainAudio.transform.SetParent(systems.transform);
        rainAudio.spatialBlend = 0f;
        rainAudio.volume = 0.45f;
        // 비 없음(2026-10-13): P.CreateRain을 부르지 않는다

        var d = systems.AddComponent<Chapter2Director>();
        d.player = player;
        d.brain = brain;
        d.gameplayCamera = gameplay;
        d.gameplayRig = rig;
        d.dynamicA = dynA;
        // 비 없음(2026-10-13 사용자 요청): 빗소리를 연결하지 않는다
        d.zoneStart = b.zoneStart;
        d.shop = b.shop;
        d.shopApproach = b.shopApproach;
        d.shopSave = b.shopSave;
        d.fenceGap = b.fenceGap;
        d.fenceInside = b.fenceInside;
        d.vanUse = b.vanUse;
        d.van = b.van;
        d.vanSave = b.vanSave;
        d.backDoor = b.backDoor;
        d.backDoorTrigger = b.backDoorTrigger;
        d.searchlight = b.searchlight;
        d.vanDome = b.vanDome;
        // 구역 2(경찰서 안)
        d.stationEntry = bi.entry;
        d.stairsTop = bi.stairsTop;
        d.floor2Trigger = bi.floor2Trigger;
        d.keypadUse = bi.keypadUse;
        d.evidenceDoor = bi.evidenceDoor;
        d.evidenceInside = bi.evidenceInside;
        d.dannyBox = bi.dannyBox;
        d.recordsDoor = bi.recordsDoor;
        d.recordsDoorUse = bi.recordsDoorUse;
        d.recordsInside = bi.recordsInside;
        d.terminalUse = bi.terminalUse;
        d.rayDeskUse = bi.rayDeskUse;
        d.clerk = bi.clerk;
        // 구역 3(탈출)
        d.alarm = be.alarm;
        d.zoneUpstairs = be.zoneUpstairs;
        d.zoneDownstairs = be.zoneDownstairs;
        d.zoneLot = be.zoneLot;
        d.squads = be.squads;
        d.backDoorOut = be.backDoorOut;
        d.outsideDoor = be.outsideDoor;
        d.traitor = be.traitor;
        d.traitorLight = be.traitorLight;
        d.traitorShot = be.traitorShot;
        d.beacons = be.beacons;
        d.subBoss = be.subBoss;

        P.SetupEnvironment(Ch2VolumePath);
        var sky = SetupHarborSky(mainCam); // 그라데이션 하늘 + 수평선 도시 실루엣
        sky.transform.SetParent(systems.transform);
        RenderSettings.ambientLight = new Color(0.4f, 0.41f, 0.48f);
        P.BakeNavMesh(Ch2NavMeshPath);
        P.EnforceMaterialKeywords();

        EditorSceneManager.SaveScene(scene, Ch2ScenePath);
        // 빌드 순서: … 1장 → 2장
        var list = EditorBuildSettings.scenes.Where(s => s.path != Ch2ScenePath).ToList();
        int ch1 = list.FindIndex(s => s.path.EndsWith("/Chapter1.unity"));
        list.Insert(ch1 >= 0 ? ch1 + 1 : list.Count, new EditorBuildSettingsScene(Ch2ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[2장] {Ch2ScenePath} 생성 완료");
    }

    // =====================================================================
    // 바닥 · 골목 · 뒷길
    // =====================================================================

    static void Ch2Ground(Transform g, Dictionary<string, Material> M)
    {
        // 장면 전체 바닥(윗면 0): 젖은 아스팔트
        P.Box(g, "Ground", new Vector3(60f, -0.25f, 45f), new Vector3(200f, 0.5f, 200f), M["Asphalt"], new Vector2(4f, 4f));
    }

    static void Ch2Alley(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built2 b)
    {
        var g = new GameObject("BackAlley").transform;
        g.SetParent(parent);
        Material back = M["BuildingBack"];
        // 골목 양옆 건물(골목 쪽이 정면), 남쪽 막다른 벽
        P.Building(g, "Alley_W_South", new Vector3(-12f, 7f, -37f), new Vector3(20f, 14f, 22f), M["FacadeB"], back, Vector3.right);
        P.Building(g, "Alley_W_North", new Vector3(-12f, 8f, -6.5f), new Vector3(20f, 16f, 17f), M["FacadeC"], back, Vector3.right, Vector3.forward);
        P.Building(g, "Alley_E", new Vector3(19f, 9f, -23f), new Vector3(22f, 18f, 50f), M["FacadeA"], back, Vector3.left, Vector3.forward);
        P.Box(g, "DeadEnd", new Vector3(3f, 5f, -49f), new Vector3(10f, 10f, 2f), M["BuildingBack"], new Vector2(4f, 4f));
        // 정보원의 가게(서쪽 건물 1층, 골목(+x)을 본다)
        Ch2Shop(g, M, X, H, triggers, b);
        // 골목 소품: 쓰레기통·쓰레기 더미·에어컨 실외기·가로등 하나
        (string name, Vector3 at, float yaw)[] props =
        {
            ("Dumpster", new Vector3(6.6f, 0f, -38f), -90f), ("TrashPile", new Vector3(6.8f, 0f, -33.5f), 20f),
            ("RecyclingBins", new Vector3(-1.2f, 0f, -31f), 90f), ("AcUnit", new Vector3(7.4f, 2.4f, -28f), -90f),
            ("AcUnit", new Vector3(7.4f, 2.6f, -12f), -90f), ("CrateStack", new Vector3(6.4f, 0f, -8f), 10f),
            ("TrashPile", new Vector3(0.4f, 0f, -4f), -40f),
        };
        foreach (var (name, at, yaw) in props) TryProp(name, g, at, yaw);
        P.AddLight(g, "AlleyLamp", new Vector3(7.2f, 4.2f, -30f), LightType.Spot, new Color(1f, 0.72f, 0.42f), 9f, 14f, 95f, Quaternion.Euler(70f, -90f, 0f));
        P.AddLight(g, "AlleyMouth", new Vector3(3f, 4.6f, 0f), LightType.Point, new Color(1f, 0.66f, 0.36f), 3f, 10f);
    }

    /// <summary>골목 전파사: 라디오·카세트 수리점(정보원 영감, 목소리만). 앞면 x = -2, 골목(+x)을 본다</summary>
    static void Ch2Shop(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built2 b)
    {
        var g = new GameObject("Shop_AlleyRadio").transform;
        g.SetParent(parent);
        const float z0 = -26f, z1 = -17f, doorZ = -19.4f;
        float x0 = AlleyX0, depth = 8f, x1 = x0 - depth, h = 3.4f, zc = (z0 + z1) / 2f, wz = z1 - z0;
        P.Box(g, "ShopFloor", new Vector3((x0 + x1) / 2f, 0.075f, zc), new Vector3(depth, 0.15f, wz), M["FloorWood"], new Vector2(2f, 2f));
        P.Box(g, "ShopCeiling", new Vector3((x0 + x1) / 2f, h + 0.05f, zc), new Vector3(depth, 0.1f, wz), M["Ceiling"], new Vector2(2f, 2f), collider: false);
        P.Box(g, "ShopBack", new Vector3(x1 - 0.1f, h / 2f, zc), new Vector3(0.2f, h, wz), X["ShopWall"], new Vector2(2f, 2f));
        P.Box(g, "ShopSide_S", new Vector3((x0 + x1) / 2f, h / 2f, z0 + 0.1f), new Vector3(depth, h, 0.2f), X["ShopWall"], new Vector2(2f, 2f));
        P.Box(g, "ShopSide_N", new Vector3((x0 + x1) / 2f, h / 2f, z1 - 0.1f), new Vector3(depth, h, 0.2f), X["ShopWall"], new Vector2(2f, 2f));
        float glassZ0 = z0 + 0.6f, glassZ1 = doorZ - 1.0f, sill = 0.6f, top = 2.75f;
        P.Box(g, "FrontSill", new Vector3(x0 + 0.1f, sill / 2f, (glassZ0 + glassZ1) / 2f), new Vector3(0.24f, sill, glassZ1 - glassZ0), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontHeader", new Vector3(x0 + 0.1f, (top + h) / 2f + 0.05f, zc), new Vector3(0.24f, h - top + 0.1f, wz + 0.2f), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_S", new Vector3(x0 + 0.1f, h / 2f, (z0 + glassZ0) / 2f), new Vector3(0.24f, h, glassZ0 - z0 + 0.2f), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_Mid", new Vector3(x0 + 0.1f, h / 2f, (glassZ1 + doorZ - 0.6f) / 2f), new Vector3(0.24f, h, doorZ - 0.6f - glassZ1), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_N", new Vector3(x0 + 0.1f, h / 2f, (doorZ + 0.6f + z1) / 2f), new Vector3(0.24f, h, z1 - doorZ - 0.6f + 0.2f), M["FacadeBase"], Vector2.one);
        var glass = P.Box(g, "ShopWindow", new Vector3(x0 + 0.06f, (sill + top) / 2f, (glassZ0 + glassZ1) / 2f), new Vector3(0.03f, top - sill, glassZ1 - glassZ0), X["Glass"], Vector2.one);
        glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        HouseBuilder.InteriorDoor(g, "ShopDoor", new Vector3(x0 + 0.22f, G, doorZ), 90f, HouseBuilder.Door("Green"), HouseBuilder.SharedMaterials());
        P.Box(g, "Awning", new Vector3(x0 + 0.8f, top + 0.2f, zc), new Vector3(1.4f, 0.12f, wz - 0.4f), X["Awning"], new Vector2(1f, 1f), collider: false)
            .transform.rotation = Quaternion.Euler(0f, 0f, 12f);
        P.Box(g, "Sign", new Vector3(x0 + 0.3f, h + 0.6f, zc), new Vector3(0.16f, 1.0f, 5.6f), X["SignBoard"], Vector2.one, collider: false);
        var sign = P.Box(g, "SignRadio", new Vector3(x0 + 0.39f, h + 0.6f, zc), new Vector3(0.01f, 0.6f, 4.8f), Neon("ShopSignAlleyRadio", "RADIO · TV · TAPE", new Color(0.4f, 1f, 0.6f) * 3.2f, new[] { "Bahnschrift", "Arial" }), Vector2.one, collider: false);
        FitUVEast(sign, 4.8f, 0.6f);
        P.AddLight(g, "SignLight", new Vector3(x0 + 0.9f, h + 0.7f, zc), LightType.Point, new Color(0.45f, 1f, 0.6f), 1.4f, 6f);
        // 실내: 진열창 뒤 작업대, 선반, 계산대
        foreach (float z in new[] { z0 + 1.4f, z0 + 2.8f, z0 + 4.2f, z0 + 5.6f })
            P.Prop("CassetteShelf", g, new Vector3(x1 + 0.2f, G, z), 90f);
        P.Prop("Desk", g, new Vector3(x0 - 1.2f, G, z0 + 2.4f), 90f);
        TryProp("CrtTv", g, new Vector3(x0 - 1.35f, G + 0.76f, z0 + 1.95f), 90f);
        TryProp("Turntable", g, new Vector3(x0 - 1.2f, G + 0.76f, z0 + 2.85f), 90f);
        P.Box(g, "Counter", new Vector3(x0 - 5f, G + 0.5f, z1 - 1.6f), new Vector3(2.4f, 1f, 0.7f), M["DarkWood"], Vector2.one);
        TryProp("CashRegister", g, new Vector3(x0 - 4.4f, G + 1f, z1 - 1.62f), 180f);
        P.AddLight(g, "ShopLight", new Vector3(x0 - 4f, h - 0.3f, zc), LightType.Point, new Color(1f, 0.74f, 0.48f), 3.4f, 8f);
        P.AddLight(g, "ShopSpill", new Vector3(x0 + 0.35f, 2.3f, (glassZ0 + glassZ1) / 2f), LightType.Point, new Color(1f, 0.7f, 0.45f), 1.1f, 5f);

        var shopGo = new GameObject("TapeShop_AlleyRadio");
        shopGo.transform.SetParent(triggers);
        shopGo.transform.position = new Vector3(x0 + 1.3f, 1f, doorZ);
        var sc = shopGo.AddComponent<BoxCollider>();
        sc.size = new Vector3(2.6f, 2f, 2.6f);
        sc.isTrigger = true;
        var shop = shopGo.AddComponent<TapeShop>();
        shop.shopName = "골목 전파사";
        // 곡: 2장 전용 한 개(VARCO Sound) + 1장 가게 테이프의 복사본 하나
        shop.stock = new List<TapeShop.Item>
        {
            new() { tape = MusicTape("골목 라디오", "alley_radio", new[] { 0.95f, 1.13f }, "새벽 두 시의 골목", "진공관이 데워질 때") }, // 2장 전용(VARCO Sound, 2026-10-13)
            new() { tape = MusicTape("부두의 블루스 (복사본)", "harbor_blues", new[] { 1.15f, 1.11f, 1.15f }, "안개 낀 부두", "닻을 내린 밤", "새벽 첫 배") },
        };
        b.shop = shop;
        b.shopApproach = Trigger(triggers, "ShopApproach", new Vector3(x0 + 1.6f, 1f, zc), new Vector3(3.2f, 2f, wz + 2f));
        b.shopSave = P.Marker(triggers, "ShopSave", new Vector3(x0 + 1.6f, 0.15f, doorZ), 90f);
    }

    static void Ch2Road(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H)
    {
        var g = new GameObject("BackRoad").transform;
        g.SetParent(parent);
        // 뒷길 남쪽: 낮은 창고 줄(길을 본다) — 골목 동쪽 건물부터 동쪽 끝까지
        Shed(g, H, "Shed_Road_S", new Vector3((30f + 150f) / 2f, 0f, -6f), new Vector3(120f, 9f, 14f), Vector3.forward);
        // 서쪽 끝: 골목 서쪽 건물 너머 높은 벽, 동쪽 끝: 막힌 길(공사 바리케이드)
        P.Box(g, "Road_W_Wall", new Vector3(-28f, 6f, 7f), new Vector3(4f, 12f, 12f), M["BuildingBack"], new Vector2(4f, 4f));
        P.Box(g, "Road_E_Wall", new Vector3(150f, 6f, 7f), new Vector3(4f, 12f, 16f), M["BuildingBack"], new Vector2(4f, 4f));
        for (float x = 144f; x <= 148f; x += 2f) TryProp("ConstructionBarrier", g, new Vector3(x, 0f, 7f), 90f);
        // 길 가장자리 연석·주차된 차 몇 대(길에서 숨을 곳)
        P.Box(g, "Curb_N", new Vector3(70f, 0.08f, 12.6f), new Vector3(140f, 0.16f, 0.4f), M["Curb"], new Vector2(1f, 1f), collider: false);
        foreach (var (x, yaw) in new[] { (36f, 90f), (58f, -90f), (92f, 90f) }) TryProp("Sedan", g, new Vector3(x, 0f, 4.6f), yaw);
        TryProp("BoxTruck", g, new Vector3(110f, 0f, 5f), -90f);
        TryProp("Dumpster", g, new Vector3(15.8f, 0f, 11.2f), 0f); // 철망 틈 앞을 가린다
        foreach (float x in new[] { 30f, 70f, 110f })
            P.AddLight(g, "RoadLamp", new Vector3(x, 6f, 1f), LightType.Spot, new Color(1f, 0.64f, 0.32f), 60f, 18f, 110f, Quaternion.Euler(80f, 0f, 0f));
    }

    // =====================================================================
    // 압수차량 보관소
    // =====================================================================

    static void Ch2Lot(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Transform triggers, Built2 b)
    {
        var g = new GameObject("ImpoundLot").transform;
        g.SetParent(parent);
        // 철망(높이 3.4m + 위 철조망). 서쪽 끝 틈(x 13~15.4)은 낮게 들춰져 있다
        LotFence(g, M, new Vector3(LotX0, 0f, LotZ0), new Vector3(13f, 0f, LotZ0));
        LotFence(g, M, new Vector3(15.6f, 0f, LotZ0), new Vector3(120f, 0f, LotZ0));
        LotFence(g, M, new Vector3(132f, 0f, LotZ0), new Vector3(LotX1, 0f, LotZ0));
        LotFence(g, M, new Vector3(LotX0, 0f, LotZ0), new Vector3(LotX0, 0f, LotZ1));
        LotFence(g, M, new Vector3(LotX1, 0f, LotZ0), new Vector3(LotX1, 0f, LotZ1));
        LotFence(g, M, new Vector3(LotX0, 0f, LotZ1), new Vector3(67f, 0f, LotZ1));
        LotFence(g, M, new Vector3(73f, 0f, LotZ1), new Vector3(LotX1, 0f, LotZ1));
        // 들춰진 철망 자락(틈 위로 축 늘어진 그물) + 들어가는 곳
        var flap = P.Box(g, "FenceFlap", new Vector3(14.2f, 1.9f, LotZ0 + 0.25f), new Vector3(2.4f, 2.6f, 0.01f), M["ChainLink"], Vector2.one, collider: false);
        flap.transform.rotation = Quaternion.Euler(-28f, 0f, 0f);
        P.Box(g, "FenceGapLow", new Vector3(14.2f, 0.55f, LotZ0), new Vector3(2.4f, 0.1f, 0.06f), M["Galvanized"], Vector2.one, collider: false);
        InvisibleWall(g, "FenceGapBlock", new Vector3(14.2f, 1.5f, LotZ0), new Vector3(2.4f, 3f, 0.3f)); // 걸어서는 못 지나간다(F로 기어 든다)
        b.fenceGap = P.Interactable(g, "FenceGapUse", new Vector3(14.2f, 1f, LotZ0 - 1.3f), new Vector3(2.6f, 2f, 1.8f), "썩은 철망 틈으로 기어 들어간다");
        b.fenceInside = P.Marker(triggers, "FenceInside", new Vector3(14.2f, 0.05f, LotZ0 + 2f), 0f);
        // 차량 문(닫힘) + 경비 초소
        foreach (float x in new[] { 120f, 126f, 132f })
            P.Box(g, "GatePost", new Vector3(x, 1.8f, LotZ0), new Vector3(0.2f, 3.6f, 0.2f), M["Galvanized"], Vector2.one);
        LotFence(g, M, new Vector3(120.2f, 0f, LotZ0 + 0.1f), new Vector3(131.8f, 0f, LotZ0 + 0.1f));
        P.Box(g, "GuardBooth", new Vector3(126f, 1.35f, LotZ0 + 5.5f), new Vector3(3f, 2.7f, 2.6f), M["FacadeBase"], new Vector2(2f, 2f));
        P.Box(g, "BoothRoof", new Vector3(126f, 2.78f, LotZ0 + 5.5f), new Vector3(3.4f, 0.14f, 3f), H["Steel"], Vector2.one, collider: false);
        P.Box(g, "BoothWindow", new Vector3(126f, 1.7f, LotZ0 + 4.18f), new Vector3(2.2f, 0.9f, 0.02f), Mat("WindowGlow") ?? M["NightWindow"], Vector2.one, collider: false);
        P.AddLight(g, "BoothLight", new Vector3(126f, 2.3f, LotZ0 + 5.5f), LightType.Point, new Color(1f, 0.85f, 0.6f), 2.2f, 6f);

        // 바닥 주차선
        for (int r = 0; r < 5; r++)
        {
            float z = 26f + r * 14f;
            for (float x = 20f; x <= 132f; x += 3.2f)
                P.Box(g, "ParkLine", new Vector3(x - 1.6f, 0.004f, z), new Vector3(0.12f, 0.01f, 5.4f), H["EdgePaint"], Vector2.one, collider: false);
        }
        // 압수 차: 다섯 줄, 남북으로 세운다(가운데 x 56~63 · 96~103은 가로 통로로 비운다)
        var rnd = new System.Random(7);
        string[] kinds = { "Sedan", "Sedan", "PoliceCar", "Sedan", "BoxTruck", "Sedan", "PoliceCar" };
        for (int r = 0; r < 5; r++)
        {
            float z = 26f + r * 14f;
            for (float x = 21.6f; x <= 132f; x += 3.2f)
            {
                if ((x > 54f && x < 64f) || (x > 94f && x < 104f)) continue; // 가로 통로
                if (rnd.NextDouble() < 0.28) continue;                       // 빈 칸(숨을 틈)
                if (r == 4 && x > 112f) continue;                            // 북동쪽: 승합차 자리 주변은 비운다
                string kind = kinds[rnd.Next(kinds.Length)];
                if (kind == "BoxTruck" && rnd.NextDouble() < 0.5) kind = "Sedan";
                float yaw = (rnd.Next(2) == 0 ? 0f : 180f) + (float)(rnd.NextDouble() * 6 - 3);
                TryProp(kind, g, new Vector3(x, 0f, z), yaw);
            }
        }
        // 북동쪽 구석: 압수 차고(골판 창고) + 그 앞의 검은 승합차(1장 대니 집 앞의 그 차)
        Shed(g, H, "ImpoundGarage", new Vector3(131f, 0f, 90f), new Vector3(14f, 6f, 14f), Vector3.left);
        var van = TryProp("BlackVan", g, new Vector3(118f, 0f, 88f), 180f);
        b.van = van ? van.transform : P.Marker(triggers, "Van", new Vector3(118f, 0f, 88f), 180f);
        foreach (var (x, z, yaw) in new[] { (115.5f, 84.8f, 0f), (120.6f, 84.6f, 10f) }) TryProp("TrafficCone", g, new Vector3(x, 0f, z), yaw);
        // 차고 처마의 작업등: 승합차 양옆을 위에서 고르게(한쪽만 비추면 뒤지는 쪽이 새까맸다)
        P.AddLight(g, "VanLamp", new Vector3(118f, 6.2f, 86f), LightType.Spot, new Color(1f, 0.82f, 0.55f), 70f, 14f, 100f, Quaternion.Euler(80f, 0f, 0f));
        // 실내등: 문을 열면 켜진다(감독이 켠다)
        var dome = P.AddLight(g, "VanDome", new Vector3(118f, 1.95f, 87.2f), LightType.Point, new Color(1f, 0.86f, 0.62f), 2.2f, 3.5f); // 차 안 앞좌석 위(창으로 새어 나온다)
        dome.enabled = false;
        b.vanDome = dome;
        b.vanUse = P.Interactable(g, "VanSearch", new Vector3(116.4f, 1.1f, 88f), new Vector3(1.8f, 2.2f, 4f), "승합차를 뒤진다");
        b.vanSave = P.Marker(triggers, "VanSave", new Vector3(114.6f, 0.05f, 88f), 0f);

        // 서북쪽: 보관소 사무실(컨테이너 사무실, 창에 불)
        P.Box(g, "Office", new Vector3(28f, 1.45f, 93f), new Vector3(12f, 2.9f, 4f), H["ContGrey"], new Vector2(2.44f, 2.59f));
        P.Box(g, "OfficeRoof", new Vector3(28f, 2.98f, 93f), new Vector3(12.4f, 0.1f, 4.4f), H["Steel"], Vector2.one, collider: false);
        foreach (float x in new[] { 24f, 31f })
            P.Box(g, "OfficeWindow", new Vector3(x, 1.7f, 90.98f), new Vector3(2f, 0.9f, 0.02f), Mat("WindowGlow") ?? M["NightWindow"], Vector2.one, collider: false);
        P.AddLight(g, "OfficeSpill", new Vector3(27f, 1.8f, 89.5f), LightType.Point, new Color(1f, 0.85f, 0.62f), 2.4f, 7f);
        TapeSpot(g, triggers, "OfficeTape", new Vector3(36.5f, 0f, 95.6f), 2, 1, box: true); // 사무실 뒤

        // 가운데 감시탑: 다리 넷 · 7m 위 발판 · 지붕 · 탐조등
        var tower = new Vector3(78f, 0f, 61f);
        foreach (float sx in new[] { -1.4f, 1.4f })
        foreach (float sz in new[] { -1.4f, 1.4f })
            P.Box(g, "TowerLeg", tower + new Vector3(sx, 3.5f, sz), new Vector3(0.22f, 7f, 0.22f), H["Steel"], Vector2.one);
        P.Box(g, "TowerDeck", tower + new Vector3(0f, 7.05f, 0f), new Vector3(3.6f, 0.15f, 3.6f), H["Steel"], Vector2.one, collider: false);
        P.Box(g, "TowerCab", tower + new Vector3(0f, 8.1f, 0f), new Vector3(3.2f, 1.9f, 3.2f), M["FacadeBase"], Vector2.one, collider: false);
        P.Box(g, "TowerRoof", tower + new Vector3(0f, 9.15f, 0f), new Vector3(3.8f, 0.15f, 3.8f), H["Steel"], Vector2.one, collider: false);
        b.towerTop = P.Marker(g, "TowerSearchlight", tower + new Vector3(0f, 8.6f, -1.9f), 180f);
        P.Box(g, "SearchlightHousing", b.towerTop.position + Vector3.up * 0.1f, new Vector3(0.9f, 0.7f, 0.9f), H["Steel"], Vector2.one, collider: false);

        // 쌓아 둔 타이어·부품 상자(엄폐물)
        (string name, Vector3 at, float yaw)[] props =
        {
            ("CrateStack", new Vector3(59.5f, 0f, 33f), 10f), ("PalletStack", new Vector3(99f, 0f, 47f), -20f),
            ("CrateStack", new Vector3(60.2f, 0f, 75f), 80f), ("SteelDrums", new Vector3(100.4f, 0f, 61f), 30f),
            ("PalletBoxes", new Vector3(45f, 0f, 96.5f), 0f), ("GasCylinders", new Vector3(136.5f, 0f, 30f), -90f),
            ("Forklift", new Vector3(98.5f, 0f, 89f), 160f), ("ConstructionBarrier", new Vector3(62.5f, 0f, 97.6f), 0f), // 사람 문(x 67~73) 옆
        };
        foreach (var (name, at, yaw) in props) TryProp(name, g, at, yaw);
    }

    /// <summary>보관소 철망 한 칸: 기둥 · 철망(높이 3.4m) · 위 철조망 단. 걸어서 넘을 수 없다</summary>
    static void LotFence(Transform parent, Dictionary<string, Material> M, Vector3 a, Vector3 b)
    {
        const float height = 3.4f;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.1f) return;
        var rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, d.normalized));
        Vector3 mid = (a + b) / 2f;
        var net = P.Box(parent, "Fence", mid + Vector3.up * (height / 2f), new Vector3(len, height, 0.01f), M["ChainLink"], new Vector2(1f, 1f), collider: false);
        net.transform.rotation = rot;
        var top = P.Box(parent, "FenceWire", mid + Vector3.up * (height + 0.25f), new Vector3(len, 0.4f, 0.01f), M["ChainLink"], new Vector2(1f, 1f), collider: false);
        top.transform.rotation = rot;
        var rail = P.Box(parent, "FenceRail", mid + Vector3.up * height, new Vector3(len, 0.05f, 0.05f), M["Galvanized"], Vector2.one, collider: false);
        rail.transform.rotation = rot;
        int posts = Mathf.Max(1, Mathf.RoundToInt(len / 3f));
        for (int i = 0; i <= posts; i++)
            P.Box(parent, "FencePost", Vector3.Lerp(a, b, i / (float)posts) + Vector3.up * (height / 2f + 0.1f), new Vector3(0.07f, height + 0.2f, 0.07f), M["Galvanized"], Vector2.one, collider: false);
        var col = new GameObject("FenceCollider");
        col.transform.SetParent(parent);
        col.transform.SetPositionAndRotation(mid + Vector3.up * (height / 2f), rot);
        col.AddComponent<BoxCollider>().size = new Vector3(len, height + 0.6f, 0.2f);
        GameObjectUtility.SetStaticEditorFlags(col, StaticEditorFlags.NavigationStatic);
    }

    // =====================================================================
    // 경찰서(뒷면)
    // =====================================================================

    static void Ch2Station(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built2 b)
    {
        var g = new GameObject("PoliceStation").transform;
        g.SetParent(parent);
        // 4층 건물, 뒷면(남쪽)이 보관소를 본다. 창에 드문드문 불
        P.Building(g, "Station", new Vector3(75f, 9f, (StationZ0 + StationZ1) / 2f), new Vector3(150f, 18f, StationZ1 - StationZ0), M["FacadeB"], M["BuildingBack"], Vector3.back);
        // 직원 주차장(z 100~106): 경찰차 몇 대
        foreach (var (x, yaw) in new[] { (30f, 0f), (36f, 0f), (104f, 180f), (110f, 0f), (116f, 0f) }) TryProp("PoliceCar", g, new Vector3(x, 0f, 103.4f), yaw);
        // 뒷문: 차양 · 문틀 · 카드 단말기 · 불빛
        float doorX = 70f;
        P.Box(g, "BackDoor", new Vector3(doorX, 1.15f, StationZ0 - 0.06f), new Vector3(1.4f, 2.3f, 0.1f), H["Steel"], Vector2.one, collider: false);
        P.Box(g, "BackDoorFrame", new Vector3(doorX, 2.4f, StationZ0 - 0.1f), new Vector3(1.8f, 0.18f, 0.2f), M["FacadeBase"], Vector2.one, collider: false);
        P.Box(g, "BackCanopy", new Vector3(doorX, 2.8f, StationZ0 - 0.9f), new Vector3(3.2f, 0.12f, 1.8f), H["Steel"], Vector2.one, collider: false);
        P.Box(g, "CardReader", new Vector3(doorX + 1.05f, 1.25f, StationZ0 - 0.08f), new Vector3(0.12f, 0.18f, 0.05f), Mat("BollardGlow") ?? M["LampGlow"], Vector2.one, collider: false);
        P.AddLight(g, "BackDoorLight", new Vector3(doorX, 2.6f, StationZ0 - 1.2f), LightType.Point, new Color(0.85f, 0.92f, 1f), 3f, 7f);
        TryProp("CctvCamera", g, new Vector3(doorX - 1.6f, 3.2f, StationZ0 - 0.35f), 180f);
        b.backDoor = P.Marker(triggers, "BackDoor", new Vector3(doorX, 0.05f, StationZ0 - 1.2f), 0f);
        b.backDoorTrigger = Trigger(triggers, "BackDoorTrigger", new Vector3(doorX, 1f, StationZ0 - 1.4f), new Vector3(3f, 2f, 2f));
    }

    // =====================================================================
    // 조명
    // =====================================================================

    static void Ch2Lights(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H)
    {
        var g = new GameObject("LotLights").transform;
        g.SetParent(parent);
        // 투광등 기둥(나트륨 주황): 보관소 군데군데를 웅덩이처럼 밝히고 사이는 어둡게(숨을 곳)
        var poles = new[] { new Vector3(24f, 0f, 19f), new Vector3(62f, 0f, 47f), new Vector3(110f, 0f, 22f), new Vector3(40f, 0f, 77f), new Vector3(96f, 0f, 75f), new Vector3(136f, 0f, 58f) };
        for (int i = 0; i < poles.Length; i++) FloodPole(g, H, poles[i], i < 3);
    }

    // =====================================================================
    // 경찰(순찰 · 초소 · 뒷문) · 감시탑 탐조등
    // =====================================================================

    static void Ch2Police(Transform people, Transform routes, Transform triggers, Dictionary<string, Material> H, Built2 b)
    {
        // 순찰(손전등): 서쪽 세로 통로 · 가운데 가로 통로 · 감시탑 둘레 · 북쪽 통로(승합차 가는 길)
        Cop(people, routes, "Cop_West", new Vector3(17f, 0.02f, 22f), 0f, new Vector3(17f, 0.02f, 22f), new Vector3(17f, 0.02f, 94f));
        Cop(people, routes, "Cop_Cross", new Vector3(24f, 0.02f, 47f), 90f, new Vector3(24f, 0.02f, 47f), new Vector3(130f, 0.02f, 47f));
        Cop(people, routes, "Cop_Tower", new Vector3(68f, 0.02f, 54f), 0f,
            new Vector3(68f, 0.02f, 54f), new Vector3(68f, 0.02f, 68f), new Vector3(88f, 0.02f, 68f), new Vector3(88f, 0.02f, 54f));
        Cop(people, routes, "Cop_North", new Vector3(132f, 0.02f, 75f), -90f, new Vector3(132f, 0.02f, 75f), new Vector3(62f, 0.02f, 75f));
        // 초소 경찰(밖 길을 본다) · 뒷문 앞 경찰(담배, 보관소를 본다) — 제자리
        Cop(people, routes, "Cop_Gate", new Vector3(126f, 0.02f, LotZ0 + 3.2f), 180f, new Vector3(126f, 0.02f, LotZ0 + 3.2f));
        Cop(people, routes, "Cop_Door", new Vector3(76.5f, 0.02f, StationZ0 - 2.2f), 200f, new Vector3(76.5f, 0.02f, StationZ0 - 2.2f));

        // 감시탑 탐조등: 보관소 차 줄 사이를 천천히 훑는다
        var sl = new GameObject("TowerSearchlight");
        sl.transform.SetParent(people.parent ? people.parent : null);
        sl.transform.position = b.towerTop.position;
        var light = P.AddLight(sl.transform, "Beam", Vector3.zero, LightType.Spot, new Color(0.85f, 0.92f, 1f), 260f, 60f, 12f, local: true);
        light.shadows = LightShadows.Soft;
        var cone = new GameObject("BeamCone");
        cone.transform.SetParent(sl.transform, false);
        cone.AddComponent<MeshFilter>().sharedMesh = BeamConeMesh();
        var cr = cone.AddComponent<MeshRenderer>();
        cr.sharedMaterial = BeamMaterial();
        cr.shadowCastingMode = ShadowCastingMode.Off;
        var search = sl.AddComponent<Searchlight>();
        search.beam = light;
        search.cone = cone.transform;
        search.shoutSpeaker = "";
        search.shoutLine = ""; // 외침은 장면 감독이 한다(경찰 말투)
        search.path = new[]
        {
            new Vector3(30f, 0f, 33f), new Vector3(110f, 0f, 33f), new Vector3(120f, 0f, 61f),
            new Vector3(110f, 0f, 89f), new Vector3(40f, 0f, 89f), new Vector3(30f, 0f, 61f),
        };
        b.searchlight = search;
    }

    /// <summary>제복 경찰(손전등). 레이는 싸우지 않는다 — 들키면 시간이 멈춘다(Chapter2Director)</summary>
    static PatrolSight Cop(Transform people, Transform routes, string name, Vector3 pos, float yaw, params Vector3[] route)
    {
        var sight = PatrolAs("Officer", people, routes, name, pos, yaw, route);
        var ai = sight.GetComponent<EnemyAI>();
        ai.displayName = "경찰";
        ai.damage = 0;
        var hp = ai.GetComponent<HoldPose>();
        if (hp) hp.dropWhenFighting = false; // 싸우지 않는다: 들켜도 손전등을 든 채 비춘다
        return sight;
    }
}
