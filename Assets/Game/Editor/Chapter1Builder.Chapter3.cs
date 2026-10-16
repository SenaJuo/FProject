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
/// 3장 「한결 레코드」 장면 — 그 회사(한결 레코드 · 한결 FM)의 본사 한결 타워. 메뉴: Game/Build Chapter 3.
///
/// 배치(미터, x 동쪽 · z 북쪽):
///   항만로(동서 차도 z -56~-44, x -36~76): 서쪽 끝에서 시작. 길 북쪽 인도에 테이프 노점(저장·교환).
///     (2026-10-14 "쓸데없이 넓다" → 길을 x -82~118에서 줄였다: 시작에서 하역장 입구까지 120m → 80m, 하역장 동쪽의 막다른 길도 없앴다)
///   한결 타워: x -20~30, z 0~50(높이 60). 남쪽 앞마당 z -40~0(정문 경비 — 갈 곳이 아니다).
///   동쪽 하역장: x 30~62, z -40~40(동쪽은 창고 벽, 2026-10-14 40×100m에서 줄였다). 화물 트럭 둘·컨테이너·지게차, 경비 순찰 셋, CCTV 둘. 직원 통로 x 30, z 34.
///   녹음실 층(따로, x 300~340, z 0~30): 복도 z 12~18 · 북쪽 테이프 보관실/조정실/실험 부스 · 남쪽 독방 셋 · 동쪽 끝 엘리베이터.
///   옥상(따로, x 296~342, z 102~140): 엘리베이터 탑 · 헬기장 · 실외기들 · 북동쪽 펜트하우스(금고실 · 전용 엘리베이터).
///     (2026-10-14 "쓸데없이 넓다" → 60×50m에서 46×38m로, 펜트하우스 20×16m를 16×12m로 줄였다)
/// </summary>
public static partial class Chapter1Builder
{
    const string Ch3ScenePath = "Assets/Game/Scenes/Chapter3.unity";
    const string Ch3NavMeshPath = "Assets/Game/Scenes/Chapter3_NavMesh.asset";
    const string Ch3VolumePath = P.ArtRoot + "/Chapter3Volume.asset";
    const string SubjectCharacter = "Subject";

    class Built3
    {
        public Transform zoneStart, shopSave, dockSave, studioEntry, roofEntry, boothGlass, partnerLift, vaultShot;
        public TapeShop shop;
        public StoryTrigger shopApproach, dockTrigger, corridorTrigger, controlTrigger, vaultTrigger;
        public StoryInteractable serviceDoor, switchUse, elevatorUse;
        public ExperimentBooth booth;
        public Transform[] cellDoors;
        public GameObject[] subjects;
        public CombatZone zoneSubjects, zoneRoof;
        public EnemyAI partner;
    }

    [MenuItem("Game/Build Chapter 3")]
    static void BuildChapter3Menu()
    {
        if (EditorUtility.DisplayDialog("3장 만들기", $"{Ch3ScenePath}를 새로 만듭니다(기존 내용은 덮어씀).", "만들기", "취소")) BuildChapter3();
    }

    public static void BuildChapter3()
    {
        P.boxMeshes.Clear();
        beamCone = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        P.root = new GameObject("Chapter3").transform;
        var M = P.CreateMaterials();
        var X = ExtraMaterials();
        var H = HarborMaterials();
        var b = new Built3();
        var triggers = new GameObject("Triggers").transform;
        var people = new GameObject("People").transform;
        var routes = new GameObject("PatrolRoutes").transform;

        var g = P.Group("Ch3");
        Ch3Street(g, M, X, H, triggers, b);
        Ch3Dock(g, M, H, triggers, people, routes, b);
        Ch3Studio(g, M, triggers, people, b);
        Ch3Roof(g, M, H, triggers, people, b);

        b.zoneStart = P.Marker(triggers, "Ch3Start", new Vector3(-31f, 0.05f, -50f), 90f);
        var player = P.CreatePlayer(b.zoneStart, out _, out _, out _, out _);

        // ---------- 카메라 ----------
        var mainCam = Object.FindAnyObjectByType<Camera>();
        mainCam.farClipPlane = 500f;
        mainCam.nearClipPlane = 0.05f;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
        mainCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
        var cams = new GameObject("Cameras").transform;
        var start = b.zoneStart.position;
        var gameplay = P.VCam(cams, "GameplayCamera", start + new Vector3(-3f, 2f, 0f), start + Vector3.up * 1.5f, 55f);
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

        var d = systems.AddComponent<Chapter3Director>();
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
        d.dockSave = b.dockSave;
        d.dockTrigger = b.dockTrigger;
        d.serviceDoor = b.serviceDoor;
        d.studioEntry = b.studioEntry;
        d.corridorTrigger = b.corridorTrigger;
        d.controlTrigger = b.controlTrigger;
        d.booth = b.booth;
        d.switchUse = b.switchUse;
        d.boothGlass = b.boothGlass;
        d.cellDoors = b.cellDoors;
        d.subjects = b.subjects;
        d.zoneSubjects = b.zoneSubjects;
        d.elevatorUse = b.elevatorUse;
        d.roofEntry = b.roofEntry;
        d.zoneRoof = b.zoneRoof;
        d.vaultTrigger = b.vaultTrigger;
        d.partner = b.partner;
        d.partnerLift = b.partnerLift;
        d.vaultShot = b.vaultShot;

        P.SetupEnvironment(Ch3VolumePath);
        var sky = SetupHarborSky(mainCam);
        sky.transform.SetParent(systems.transform);
        RenderSettings.ambientLight = new Color(0.4f, 0.41f, 0.48f);
        P.BakeNavMesh(Ch3NavMeshPath);
        P.EnforceMaterialKeywords();

        EditorSceneManager.SaveScene(scene, Ch3ScenePath);
        var list = EditorBuildSettings.scenes.Where(s => s.path != Ch3ScenePath).ToList();
        int ch2 = list.FindIndex(s => s.path.EndsWith("/Chapter2.unity"));
        list.Insert(ch2 >= 0 ? ch2 + 1 : list.Count, new EditorBuildSettingsScene(Ch3ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[3장] {Ch3ScenePath} 생성 완료");
    }

    // =====================================================================
    // 항만로 · 한결 타워 · 노점
    // =====================================================================

    static void Ch3Street(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built3 b)
    {
        var g = new GameObject("HarborRoad1").transform;
        g.SetParent(parent);
        P.Box(g, "Ground", new Vector3(20f, -0.25f, 0f), new Vector3(240f, 0.5f, 200f), M["Asphalt"], new Vector2(4f, 4f));
        P.Box(g, "Sidewalk_N", new Vector3(20f, 0.08f, -42f), new Vector3(200f, 0.16f, 4f), M["Sidewalk"], new Vector2(2f, 2f));
        P.Box(g, "Sidewalk_S", new Vector3(20f, 0.08f, -58f), new Vector3(200f, 0.16f, 4f), M["Sidewalk"], new Vector2(2f, 2f));
        for (float x = -32f; x < 74f; x += 6f)
            P.Box(g, "LaneDash", new Vector3(x, 0.01f, -50f), new Vector3(3f, 0.01f, 0.15f), M["Paint"], Vector2.one, collider: false);

        // 길 남쪽: 건물 줄(길을 본다) · 길 양 끝 막힘
        P.Building(g, "Row_S_W", new Vector3(-22.5f, 9f, -72f), new Vector3(35f, 18f, 24f), M["FacadeA"], M["BuildingBack"], Vector3.forward);
        P.Building(g, "Row_S_E", new Vector3(50f, 11f, -72f), new Vector3(110f, 22f, 24f), M["FacadeC"], M["BuildingBack"], Vector3.forward);
        P.Box(g, "Road_W_Wall", new Vector3(-36f, 8f, -50f), new Vector3(4f, 16f, 20f), M["BuildingBack"], new Vector2(4f, 4f));
        P.Box(g, "Road_E_Wall", new Vector3(76f, 8f, -50f), new Vector3(4f, 16f, 20f), M["BuildingBack"], new Vector2(4f, 4f));
        // 길 북쪽 서편: 사무 빌딩(타워 서쪽)
        P.Building(g, "Row_N_W", new Vector3(-30f, 12f, 10f), new Vector3(20f, 24f, 100f), M["FacadeB"], M["BuildingBack"], Vector3.back);

        // 한결 타워: 높이 60m, 앞(남)면에 창. 옥상 네온 HG FM 98.1 · 1층 위 HANGYEOL RECORDS
        P.Building(g, "HangyeolTower", new Vector3(5f, 30f, 25f), new Vector3(50f, 60f, 50f), M["FacadeB"], M["BuildingBack"], Vector3.back);
        var sign = P.Box(g, "SignRecords", new Vector3(5f, 6.2f, -0.12f), new Vector3(16f, 1.6f, 0.05f), Neon("SignHangyeolRecords", "HANGYEOL RECORDS", new Color(1f, 0.55f, 0.85f) * 4f, new[] { "Bahnschrift", "Arial" }), Vector2.one, collider: false);
        FitUVSouth(sign, 16f, 1.6f);
        var fm = P.Box(g, "SignFM", new Vector3(5f, 57f, -0.15f), new Vector3(14f, 3.2f, 0.05f), Neon("SignHgFm", "HG FM 98.1", new Color(0.4f, 0.85f, 1f) * 4.5f, new[] { "Bahnschrift", "Arial" }), Vector2.one, collider: false);
        FitUVSouth(fm, 14f, 3.2f);
        P.AddLight(g, "SignGlow", new Vector3(5f, 6.4f, -2.5f), LightType.Point, new Color(1f, 0.55f, 0.85f), 6f, 14f);
        // 정문(유리문 + 차양) · 앞마당 화단 · 가로등
        P.Box(g, "Entrance", new Vector3(5f, 1.6f, -0.08f), new Vector3(10f, 3.2f, 0.1f), X["Glass"], Vector2.one, collider: false);
        P.Box(g, "EntranceCanopy", new Vector3(5f, 3.6f, -3f), new Vector3(14f, 0.25f, 6f), H["Steel"], Vector2.one, collider: false);
        P.AddLight(g, "EntranceLight", new Vector3(5f, 3.2f, -3f), LightType.Point, new Color(1f, 0.9f, 0.75f), 6f, 12f);
        foreach (float x in new[] { -14f, 24f })
            P.Box(g, "Planter", new Vector3(x, 0.45f, -14f), new Vector3(6f, 0.9f, 6f), M["FacadeBase"], new Vector2(2f, 2f));
        foreach (float x in new[] { -19f, 2f, 24f, 48f, 70f })
            TryProp("StreetLampLed", g, new Vector3(x, 0.16f, -43f), 180f);
        // 정문 경비 둘(제자리, 길을 본다) — 정문 쪽으로 가면 들킨다
        TapeSpot(g, triggers, "PlanterTape", new Vector3(-14f, 0.9f, -10.5f), 1, 1, box: false);

        // ---------- 테이프 노점(길 북쪽 인도, 서편) ----------
        var k = new GameObject("TapeStall").transform;
        k.SetParent(g);
        Vector3 stall = new(-27.5f, 0.16f, -40.6f);
        P.Box(k, "StallCounter", stall + new Vector3(0f, 0.55f, 0f), new Vector3(3.2f, 1.1f, 1.0f), M["DarkWood"], Vector2.one);
        P.Box(k, "StallBack", stall + new Vector3(0f, 1.3f, 0.9f), new Vector3(3.4f, 2.6f, 0.1f), X["ShopWall"], Vector2.one);
        P.Box(k, "StallRoof", stall + new Vector3(0f, 2.65f, 0.2f), new Vector3(3.8f, 0.1f, 2.0f), X["Awning"], Vector2.one, collider: false);
        var tapes = P.Box(k, "StallSign", stall + new Vector3(0f, 2.35f, -0.72f), new Vector3(2.4f, 0.42f, 0.03f), Neon("SignTapes", "TAPES", new Color(1f, 0.75f, 0.3f) * 3.5f, new[] { "Bahnschrift", "Arial" }), Vector2.one, collider: false);
        FitUVSouth(tapes, 2.4f, 0.42f);
        TryProp("CassetteShelf", k, stall + new Vector3(-1f, 0f, 0.6f), 180f);
        TryProp("CashRegister", k, stall + new Vector3(0.9f, 1.1f, 0f), 180f);
        P.AddLight(k, "StallLight", stall + new Vector3(0f, 2.3f, -0.2f), LightType.Point, new Color(1f, 0.78f, 0.5f), 3f, 7f);
        var shopGo = new GameObject("TapeShop_Stall");
        shopGo.transform.SetParent(triggers);
        shopGo.transform.position = stall + new Vector3(0f, 1f, -1.6f);
        var sc = shopGo.AddComponent<BoxCollider>();
        sc.size = new Vector3(3.2f, 2f, 2.2f);
        sc.isTrigger = true;
        var shop = shopGo.AddComponent<TapeShop>();
        shop.shopName = "테이프 노점";
        shop.stock = new List<TapeShop.Item>
        {
            new() { tape = MusicTape("항만로 네온", "harbor_neon", new[] { 1.0f, 0.86f }, "젖은 네온", "첫 배를 기다리며") }, // 3장 전용(VARCO Sound)
            new() { tape = MusicTape("빗소리 재즈 (복사본)", "rain_jazz", new[] { 1.01f, 1.18f, 1.16f }, "젖은 트럼펫", "골목의 블루스", "마지막 손님") },
        };
        b.shop = shop;
        b.shopApproach = Trigger(triggers, "StallApproach", stall + new Vector3(0f, 1f, -2.5f), new Vector3(6f, 2f, 3f));
        b.shopSave = P.Marker(triggers, "StallSave", stall + new Vector3(0f, 0f, -2f), 0f);
    }

    // =====================================================================
    // 동쪽 하역장(잠입)
    // =====================================================================

    static void Ch3Dock(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Transform triggers, Transform people, Transform routes, Built3 b)
    {
        var g = new GameObject("LoadingDock").transform;
        g.SetParent(parent);
        // 동쪽 창고(하역장 동쪽 벽) · 북쪽 끝 벽
        Shed(g, H, "Dock_Warehouse", new Vector3(82f, 0f, 10f), new Vector3(40f, 12f, 100f), Vector3.left);
        P.Box(g, "Dock_N_Wall2", new Vector3(46f, 5f, 41f), new Vector3(34f, 10f, 2f), M["BuildingBack"], new Vector2(4f, 4f)); // 하역장 북쪽 끝
        P.Box(g, "Dock_N_Wall", new Vector3(15.5f, 5f, 61f), new Vector3(111f, 10f, 2f), M["BuildingBack"], new Vector2(4f, 4f));
        // 입구 차단기(길에서 들어오는 곳)
        P.Box(g, "GateBooth", new Vector3(34f, 1.3f, -37f), new Vector3(2.4f, 2.6f, 2.4f), M["FacadeBase"], new Vector2(2f, 2f));
        P.Box(g, "BoomArm", new Vector3(42f, 1.05f, -37.5f), new Vector3(12f, 0.12f, 0.12f), H["NoEntry"], Vector2.one, collider: false);
        // 하역 문(셔터 둘) · 문 위 작업등 · 직원 통로
        foreach (float z in new[] { 14f, 24f })
        {
            P.Box(g, "DockShutter", new Vector3(30.06f, 2.2f, z), new Vector3(0.1f, 4.4f, 4.4f), H["ShedDark"], Vector2.one, collider: false);
            P.AddLight(g, "DockLamp", new Vector3(31.2f, 5f, z), LightType.Spot, new Color(1f, 0.8f, 0.55f), 40f, 12f, 100f, Quaternion.Euler(70f, 90f, 0f));
        }
        P.Box(g, "ServiceDoor", new Vector3(30.06f, 1.15f, 34f), new Vector3(0.1f, 2.3f, 1.2f), H["Steel"], Vector2.one, collider: false);
        P.AddLight(g, "ServiceDoorLight", new Vector3(31f, 2.7f, 34f), LightType.Point, new Color(0.85f, 0.92f, 1f), 5f, 9f);
        b.serviceDoor = P.Interactable(g, "ServiceDoorUse", new Vector3(31.2f, 1f, 34f), new Vector3(1.6f, 2f, 2f), "직원 통로로 들어간다");
        // 화물 트럭(하역 문에 뒤를 대고) · 컨테이너 · 지게차 · 팔레트(숨을 곳)
        TryProp("BoxTruck", g, new Vector3(36f, 0f, 14f), 90f);
        TryProp("BoxTruck", g, new Vector3(36.5f, 0f, 24.5f), 90f);
        Container(g, H, new Vector3(50f, 0f, -6f), false, H["ContBlue"]);
        Container(g, H, new Vector3(53f, 0f, 6f), false, H["ContRed"]);
        Container(g, H, new Vector3(47f, 0f, 36.5f), true, H["ContGreen"]);
        (string name, Vector3 at, float yaw)[] props =
        {
            ("Forklift", new Vector3(46f, 0f, 2f), 200f), ("PalletStack", new Vector3(40f, 0f, -18f), 10f),
            ("CrateStack", new Vector3(57.5f, 0f, -31f), 80f), ("PalletBoxes", new Vector3(52f, 0f, 30f), 0f),
            ("SteelDrums", new Vector3(42.5f, 0f, -6f), 30f), ("Dumpster", new Vector3(59.6f, 0f, -4f), -90f),
            ("PalletStack", new Vector3(49f, 0f, 21f), -15f), ("GasCylinders", new Vector3(32.2f, 0f, 38f), 90f),
        };
        foreach (var (name, at, yaw) in props) TryProp(name, g, at, yaw);
        foreach (var at in new[] { new Vector3(60f, 0f, -36f), new Vector3(60f, 0f, 38f), new Vector3(44f, 0f, -14f), new Vector3(54f, 0f, 16f) }) FloodPole(g, H, at, false);

        b.dockTrigger = Trigger(triggers, "DockEntry", new Vector3(46f, 1f, -34f), new Vector3(30f, 2f, 6f));
        b.dockSave = P.Marker(triggers, "DockSave", new Vector3(46f, 0.05f, -38f), 0f);

        // 경비(손전등 순찰): 서쪽 통로 · 동쪽 통로 · 트럭 옆 제자리(마당을 본다)
        Guard(people, routes, "Guard_West", new Vector3(40f, 0.02f, -22f), 0f, new Vector3(40f, 0.02f, -22f), new Vector3(40f, 0.02f, 12f)); // 트럭 옆 경비 7m 앞에서 돌아선다(전에는 2.6m 옆을 지나갔다)
        Guard(people, routes, "Guard_East", new Vector3(57.5f, 0.02f, 34f), 180f, new Vector3(57.5f, 0.02f, 34f), new Vector3(57.5f, 0.02f, -26f));
        Guard(people, routes, "Guard_Truck", new Vector3(37f, 0.02f, 19.2f), 90f, new Vector3(37f, 0.02f, 19.2f));
        // 정문 경비 둘(앞마당, 제자리)
        Guard(people, routes, "Guard_Front_W", new Vector3(0f, 0.02f, -3f), 180f, new Vector3(0f, 0.02f, -3f));
        Guard(people, routes, "Guard_Front_E", new Vector3(10f, 0.02f, -3f), 180f, new Vector3(10f, 0.02f, -3f));

        // CCTV 둘: 하역장 남쪽(타워 모서리) · 북쪽(창고 벽)
        Cctv(g, "Cctv_South", new Vector3(31f, 7f, -2f), new[] { new Vector3(38f, 0f, -26f), new Vector3(58f, 0f, -26f), new Vector3(58f, 0f, 2f), new Vector3(38f, 0f, 2f) });
        Cctv(g, "Cctv_North", new Vector3(61f, 7f, 22f), new[] { new Vector3(42f, 0f, 8f), new Vector3(58f, 0f, 8f), new Vector3(58f, 0f, 34f), new Vector3(42f, 0f, 34f) });
        TapeSpot(g, triggers, "DockTape", new Vector3(60.6f, 0f, 30f), 2, 1, box: true);
    }

    /// <summary>회사 경비(손전등 순찰, 조직원 모델). 들키면 시간이 멈춘다(Chapter3Director)</summary>
    static PatrolSight Guard(Transform people, Transform routes, string name, Vector3 pos, float yaw, params Vector3[] route)
    {
        var sight = PatrolAs("Enforcer", people, routes, name, pos, yaw, route);
        var ai = sight.GetComponent<EnemyAI>();
        ai.displayName = "경비";
        var hp = ai.GetComponent<HoldPose>();
        if (hp) hp.dropWhenFighting = false;
        return sight;
    }

    /// <summary>벽에 붙은 감시 카메라: 좁은 빛줄기가 정해진 길을 천천히 훑는다(탐조등과 같은 장치, 작게)</summary>
    static void Cctv(Transform parent, string name, Vector3 at, Vector3[] path)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = at;
        TryProp("CctvCamera", go.transform, at, 0f);
        var light = P.AddLight(go.transform, "Beam", Vector3.zero, LightType.Spot, new Color(0.75f, 0.95f, 1f), 70f, 26f, 14f, local: true);
        light.shadows = LightShadows.Soft;
        var cone = new GameObject("BeamCone");
        cone.transform.SetParent(go.transform, false);
        cone.AddComponent<MeshFilter>().sharedMesh = BeamConeMesh();
        var cr = cone.AddComponent<MeshRenderer>();
        cr.sharedMaterial = BeamMaterial();
        cr.shadowCastingMode = ShadowCastingMode.Off;
        var s = go.AddComponent<Searchlight>();
        s.beam = light;
        s.cone = cone.transform;
        s.radius = 2.6f;
        s.speed = 2.2f;
        s.shoutSpeaker = "";
        s.shoutLine = "";
        s.path = path;
    }

    // =====================================================================
    // 녹음실 층(실험 부스 · 독방 · 엘리베이터)
    // =====================================================================

    static void Ch3Studio(Transform parent, Dictionary<string, Material> M, Transform triggers, Transform people, Built3 b)
    {
        var S = StationMaterials();
        S["Wall"] = P.Lit("StudioWall", P.Tex("StationPaint", 256, PaintPixel), new Color(0.46f, 0.47f, 0.52f), 0.2f);
        S["WallLow"] = P.Lit("StudioWallLow", null, new Color(0.16f, 0.17f, 0.2f), 0.3f);
        S["Floor"] = P.Lit("StudioCarpet", null, new Color(0.2f, 0.21f, 0.24f), 0.05f);
        var foam = P.Lit("StudioFoam", null, new Color(0.13f, 0.13f, 0.15f), 0.02f);
        var g = new GameObject("StudioFloor").transform;
        g.SetParent(parent);
        const float x0 = 300f, x1 = 340f, z0 = 0f, z1 = 30f;
        P.Box(g, "Floor", new Vector3((x0 + x1) / 2f, -0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), S["Floor"], new Vector2(2f, 2f));
        P.Box(g, "Ceiling", new Vector3((x0 + x1) / 2f, WallH + 0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), S["Ceiling"], new Vector2(2f, 2f));
        // 바깥 벽
        WallX(g, S, z0, x0, x1, 0f);
        WallX(g, S, z1, x0, x1, 0f);
        WallZ(g, S, x0, z0, z1, 0f);
        WallZ(g, S, x1, z0, z1, 0f);
        // 복도(z 12~18) 북쪽 벽: 보관실(문 x 305) · 조정실(문 x 316) · 휴게실(문 x 335)
        WallX(g, S, 18f, x0, x1, 0f, (305f, 1.6f), (316f, 1.6f), (335f, 1.6f));
        WallZ(g, S, 310f, 18f, z1, 0f);
        WallZ(g, S, 330f, 18f, z1, 0f);
        // 조정실 | 부스: x 322, 가운데 큰 창(z 21~27, 허리 높이 위)
        WallZ(g, S, 322f, 18f, z1, 0f, (24f, 6f));
        P.Box(g, "WindowSill", new Vector3(322f, 0.5f, 24f), new Vector3(0.2f, 1.0f, 6f), S["WallLow"], Vector2.one);
        InvisibleWall(g, "BoothGlass", new Vector3(322f, 1.65f, 24f), new Vector3(0.1f, 1.3f, 6f));
        foreach (float z in new[] { 21.05f, 26.95f }) // 가운데 살은 두지 않는다(유리 너머 사람을 가린다)
            P.Box(g, "WindowFrame", new Vector3(322f, 1.65f, z), new Vector3(0.12f, 1.3f, 0.06f), S["Steel"], Vector2.one, collider: false);
        b.boothGlass = P.Marker(g, "BoothGlassMark", new Vector3(322f, 1.6f, 24f), -90f);
        // 남쪽 독방 셋(문은 복도 쪽 z 12) · 서쪽 창고
        WallX(g, S, 12f, x0, x1, 0f, (316f, 1.6f), (324f, 1.6f), (332f, 1.6f));
        foreach (float x in new[] { 312f, 320f, 328f, 336f }) WallZ(g, S, x, z0, 12f, 0f);
        // 부스 흡음재(벽에 붙인 짙은 판)
        foreach (var (c, s) in new[] { (new Vector3(329.85f, 2.2f, 24f), new Vector3(0.06f, 3f, 10f)), (new Vector3(326f, 2.2f, 29.85f), new Vector3(7.6f, 3f, 0.06f)), (new Vector3(326f, 2.2f, 18.15f), new Vector3(7.6f, 3f, 0.06f)) })
            P.Box(g, "Foam", c, s, foam, new Vector2(0.6f, 0.6f), collider: false);
        // 문짝: 독방 셋(감독이 연다) · 다른 방은 열린 채(구멍)
        b.cellDoors = new[]
        {
            Door(g, S, new Vector3(316f, 0f, 12f), 1.6f, "CellDoor_A"),
            Door(g, S, new Vector3(324f, 0f, 12f), 1.6f, "CellDoor_B"),
            Door(g, S, new Vector3(332f, 0f, 12f), 1.6f, "CellDoor_C"),
        };
        // 조명(형광등): 복도 · 방마다
        (float x, float z, float k)[] lamps = { (304f, 15f, 1f), (316f, 15f, 1f), (328f, 15f, 1f), (338f, 15f, 0.8f), (305f, 24f, 0.8f), (316f, 24f, 0.9f), (335f, 24f, 0.7f), (316f, 6f, 0.5f), (324f, 6f, 0.5f), (332f, 6f, 0.5f) };
        foreach (var (x, z, k) in lamps)
        {
            P.Box(g, "FluorPanel", new Vector3(x, WallH - 0.03f, z), new Vector3(1.2f, 0.04f, 0.3f), S["Fluor"], Vector2.one, collider: false);
            var l = P.AddLight(g, "Fluor", new Vector3(x, WallH - 0.3f, z), LightType.Point, new Color(0.9f, 0.93f, 1f), 5f * k, 10f);
            l.shadows = LightShadows.None;
        }

        // ---------- 방 꾸밈 ----------
        // 테이프 보관실: 릴 녹음기 셋 · 테이프 선반 · 테이프(되감기 보충)
        foreach (float x in new[] { 302.5f, 305f, 307.5f }) TryProp("ReelToReel", g, new Vector3(x, 0f, 29f), 180f);
        foreach (float z in new[] { 21f, 23.5f, 26f }) TryProp("CassetteShelf", g, new Vector3(300.6f, 0f, z), 90f);
        TapeSpot(g, triggers, "ArchiveTape", new Vector3(308.6f, 0f, 21f), 2, 1, box: true);
        // 조정실: 믹싱 콘솔(창 앞, 앉는 쪽이 서쪽) · 의자 · 장비를 끄는 곳
        TryProp("MixingConsole", g, new Vector3(320f, 0f, 24f), -90f);
        TryProp("OfficeChair", g, new Vector3(318.6f, 0f, 24f), 90f);
        TryProp("ReelToReel", g, new Vector3(311f, 0f, 29f), 180f);
        var sw = P.Interactable(g, "BoothPowerSwitch", new Vector3(319.3f, 1f, 24f), new Vector3(1.6f, 2f, 3f), "녹음 장비를 끈다");
        sw.oneShot = false;
        b.switchUse = sw;
        b.controlTrigger = Trigger(triggers, "ControlRoom", new Vector3(316f, 1.5f, 24f), new Vector3(11.5f, 3f, 11.5f));
        // 휴게실(동쪽): 소파 · 자판기
        TryProp("Sofa", g, new Vector3(335f, 0f, 28.8f), 180f);
        TryProp("VendingMachine", g, new Vector3(339.4f, 0f, 22f), -90f);

        // ---------- 부스: 녹음 대상 7번 ----------
        var boothGo = new GameObject("ExperimentBooth");
        boothGo.transform.SetParent(g);
        var booth = boothGo.AddComponent<ExperimentBooth>();
        booth.collapseAt = 20f; // 처음엔 조정실에 들어선 순간 쓰러진다(감독). 되감은 뒤 장비를 끌 여유
        var seven = SubjectModel(g, "Seven", new Vector3(327f, 0f, 24f), -90f);
        if (seven)
        {
            booth.subject = seven.GetComponent<Animator>();
            booth.ragdoll = seven.AddComponent<Ragdoll>();
        }
        TryProp("StudioMic", g, new Vector3(325.9f, 0f, 23.25f), 70f); // 얼굴 조금 옆(조정실에서 얼굴이 가리지 않게)
        booth.boothLight = P.AddLight(g, "BoothLight", new Vector3(325.4f, 2.7f, 24f), LightType.Point, new Color(0.95f, 0.9f, 0.8f), 6f, 8f); // 얼굴 앞 위: 조정실에서 보이게
        var tone = boothGo.AddComponent<AudioSource>();
        tone.clip = ProceduralSfxTone();
        tone.spatialBlend = 0.7f;
        tone.playOnAwake = false;
        tone.loop = true;
        tone.volume = 0.2f;
        booth.tone = tone;
        boothGo.transform.position = new Vector3(326f, 1.5f, 24f);
        b.booth = booth;

        // ---------- 독방의 실험체 셋(감독이 문을 열고 꺼낸다) ----------
        var subs = new List<EnemyAI>();
        foreach (var (x, i) in new[] { (316f, 0), (324f, 1), (332f, 2) })
        {
            var e = P.CreateThug(people, $"Subject_{(char)('A' + i)}", new Vector3(x, 0.02f, 6f), 0f, SubjectCharacter);
            SetCombat(e, false);
            e.displayName = "실험체";
            e.damage = 9;
            e.windup = 0.42f;      // 빠르고 거칠다
            e.recover = 0.5f;
            e.attackCooldown = 1.0f;
            e.heavyEvery = 0;
            e.GetComponent<Health>().maxHealth = 90;
            e.GetComponent<UnityEngine.AI.NavMeshAgent>().speed = 7.4f;
            subs.Add(e);
        }
        b.subjects = subs.Select(s => s.gameObject).ToArray();
        b.zoneSubjects = Zone(triggers, "Zone_Corridor", new Vector3(320f, 1.5f, 15f), new Vector3(40f, 3f, 6f), subs.ToArray());

        b.studioEntry = P.Marker(triggers, "StudioEntry", new Vector3(301.6f, 0.05f, 15f), 90f);
        b.corridorTrigger = Trigger(triggers, "CorridorStart", new Vector3(305f, 1.5f, 15f), new Vector3(3f, 3f, 6f));
        // 동쪽 끝 엘리베이터(꼭대기로)
        P.Box(g, "LiftDoor", new Vector3(339.88f, 1.2f, 15f), new Vector3(0.06f, 2.4f, 1.8f), S["Steel"], Vector2.one, collider: false);
        b.elevatorUse = P.Interactable(g, "LiftUse", new Vector3(338.8f, 1f, 15f), new Vector3(1.8f, 2f, 2.4f), "엘리베이터로 꼭대기에 오른다");
    }

    /// <summary>실험체 모델만(싸우지 않는 녹음 대상). 서 있는 동작</summary>
    static GameObject SubjectModel(Transform parent, string name, Vector3 pos, float yaw)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BaroqueCharacterSetup.CharacterRoot + $"/{SubjectCharacter}/{SubjectCharacter}_Model.prefab");
        if (!asset) { Debug.LogWarning("[3장] 실험체 모델 없음"); return null; }
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        model.name = name;
        model.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var anim = model.GetComponent<Animator>();
        if (anim) anim.runtimeAnimatorController = BaroqueCharacterSetup.EnsureIKController();
        return model;
    }

    /// <summary>부스 스피커에서 새는 찢어지는 소리(반복): 낮게 떨리는 톤 두 개가 맥놀이</summary>
    static AudioClip ProceduralSfxTone()
    {
        const string path = "Assets/Game/Audio/BoothTone.wav";
        var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (existing) return existing;
        const int rate = 44100;
        int n = rate * 2;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            // 220Hz와 223Hz(2초에 6번 맥놀이) + 거친 배음 — 2초에 정수 번 돌아 이음매 없이 반복
            float a = Mathf.Sin(2 * Mathf.PI * 220f * t), c = Mathf.Sin(2 * Mathf.PI * 223f * t);
            float buzz = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 110f * t)) * 0.15f;
            data[i] = (a * 0.45f + c * 0.45f + buzz) * 0.5f;
        }
        System.IO.Directory.CreateDirectory("Assets/Game/Audio");
        WriteWav(path, data, rate);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void WriteWav(string path, float[] data, int rate)
    {
        using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create);
        using var w = new System.IO.BinaryWriter(fs);
        int bytes = data.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (var f in data) w.Write((short)Mathf.Clamp(f * 32767f, -32768f, 32767f));
    }

    // =====================================================================
    // 옥상(정리반 · 금고실 · 옛 파트너)
    // =====================================================================

    static void Ch3Roof(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Transform triggers, Transform people, Built3 b)
    {
        var g = new GameObject("Rooftop").transform;
        g.SetParent(parent);
        const float x0 = 296f, x1 = 342f, z0 = 102f, z1 = 140f;
        var roofMat = P.Lit("RoofDeck", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.75f, 0.76f, 0.78f), 0.3f);
        P.Box(g, "RoofDeck", new Vector3((x0 + x1) / 2f, -0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), roofMat, new Vector2(4f, 4f));
        // 난간(허리 높이 벽) — 떨어지지 않게
        P.Box(g, "Parapet_S", new Vector3((x0 + x1) / 2f, 0.55f, z0 + 0.15f), new Vector3(x1 - x0, 1.1f, 0.3f), M["FacadeBase"], new Vector2(2f, 2f));
        P.Box(g, "Parapet_N", new Vector3((x0 + x1) / 2f, 0.55f, z1 - 0.15f), new Vector3(x1 - x0, 1.1f, 0.3f), M["FacadeBase"], new Vector2(2f, 2f));
        P.Box(g, "Parapet_W", new Vector3(x0 + 0.15f, 0.55f, (z0 + z1) / 2f), new Vector3(0.3f, 1.1f, z1 - z0), M["FacadeBase"], new Vector2(2f, 2f));
        P.Box(g, "Parapet_E", new Vector3(x1 - 0.15f, 0.55f, (z0 + z1) / 2f), new Vector3(0.3f, 1.1f, z1 - z0), M["FacadeBase"], new Vector2(2f, 2f));
        InvisibleWall(g, "RoofEdgeBlock_S", new Vector3((x0 + x1) / 2f, 2f, z0), new Vector3(x1 - x0, 4f, 0.4f));
        InvisibleWall(g, "RoofEdgeBlock_N", new Vector3((x0 + x1) / 2f, 2f, z1), new Vector3(x1 - x0, 4f, 0.4f));
        InvisibleWall(g, "RoofEdgeBlock_W", new Vector3(x0, 2f, (z0 + z1) / 2f), new Vector3(0.4f, 4f, z1 - z0));
        InvisibleWall(g, "RoofEdgeBlock_E", new Vector3(x1, 2f, (z0 + z1) / 2f), new Vector3(0.4f, 4f, z1 - z0));
        // 아래 도시(난간 너머로 보이는 불빛 빌딩들) · 먼 바닥
        P.Box(g, "CityFloor", new Vector3(320f, -62f, 125f), new Vector3(600f, 1f, 600f), M["Asphalt"], new Vector2(8f, 8f), collider: false);
        var rnd = new System.Random(31);
        for (int i = 0; i < 18; i++)
        {
            float ang = i / 18f * Mathf.PI * 2f, dist = 70f + (float)rnd.NextDouble() * 60f;
            float h = 30f + (float)rnd.NextDouble() * 50f;
            var c = new Vector3(320f + Mathf.Cos(ang) * dist, -62f + h / 2f, 125f + Mathf.Sin(ang) * dist);
            var face = new Vector3(320f - c.x, 0f, 125f - c.z).normalized;
            Vector3 front = Mathf.Abs(face.x) > Mathf.Abs(face.z) ? new Vector3(Mathf.Sign(face.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(face.z));
            P.Building(g, "CityTower", c, new Vector3(18f, h, 18f), i % 3 == 0 ? M["FacadeA"] : i % 3 == 1 ? M["FacadeB"] : M["FacadeC"], M["BuildingBack"], front);
        }

        // 엘리베이터 탑(도착) · 헬기장 · 실외기·물탱크(엄폐물)
        P.Box(g, "LiftTower", new Vector3(300f, 2f, 104.4f), new Vector3(6f, 4f, 4.5f), M["FacadeBase"], new Vector2(2f, 2f));
        P.AddLight(g, "LiftTowerLight", new Vector3(300f, 3.2f, 107.2f), LightType.Point, new Color(0.85f, 0.92f, 1f), 3f, 9f);
        b.roofEntry = P.Marker(triggers, "RoofEntry", new Vector3(300f, 0.05f, 108f), 0f);
        var pad = P.Lit("HelipadPaint", null, new Color(0.9f, 0.85f, 0.3f), 0.3f);
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2f;
            var seg = P.Box(g, "PadRing", new Vector3(318f + Mathf.Cos(a) * 8f, 0.01f, 118f + Mathf.Sin(a) * 8f), new Vector3(2.2f, 0.01f, 0.35f), pad, Vector2.one, collider: false);
            seg.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
        }
        P.Box(g, "PadH_L", new Vector3(316.2f, 0.01f, 118f), new Vector3(0.6f, 0.01f, 5f), pad, Vector2.one, collider: false);
        P.Box(g, "PadH_R", new Vector3(319.8f, 0.01f, 118f), new Vector3(0.6f, 0.01f, 5f), pad, Vector2.one, collider: false);
        P.Box(g, "PadH_M", new Vector3(318f, 0.01f, 118f), new Vector3(3f, 0.01f, 0.6f), pad, Vector2.one, collider: false);
        foreach (var at in new[] { new Vector3(306f, 0f, 113f), new Vector3(306f, 0f, 125f), new Vector3(330f, 0f, 111f), new Vector3(337f, 0f, 118f), new Vector3(300f, 0f, 133f) })
            TryProp("AcUnit", g, at, 90f);
        P.Box(g, "WaterTank", new Vector3(300.6f, 2f, 123f), new Vector3(5f, 4f, 5f), H["Steel"], Vector2.one);
        P.Box(g, "VentBox", new Vector3(326f, 0.9f, 105f), new Vector3(4f, 1.8f, 2f), H["Steel"], Vector2.one);
        foreach (var at in new[] { new Vector3(305f, 0f, 103f), new Vector3(340f, 0f, 104f), new Vector3(298f, 0f, 138f), new Vector3(312f, 0f, 133f) })
        {
            P.Box(g, "LightPole", at + Vector3.up * 2.5f, new Vector3(0.18f, 5f, 0.18f), H["Steel"], Vector2.one);
            P.AddLight(g, "RoofLamp", at + Vector3.up * 5f, LightType.Spot, new Color(1f, 0.85f, 0.6f), 30f, 22f, 110f, Quaternion.Euler(80f, 0f, 0f));
        }
        P.AddLight(g, "AviationRed", new Vector3(339.5f, 5.3f, 137.5f), LightType.Point, new Color(1f, 0.1f, 0.05f), 2f, 6f);

        // 펜트하우스(북동쪽): 남쪽 벽 가운데가 열린 입구 · 안에 금고 · 동쪽 끝 전용 엘리베이터
        var ph = P.Lit("PenthouseWall", null, new Color(0.18f, 0.19f, 0.22f), 0.5f, metallic: 0.2f);
        const float px0 = 324f, px1 = 340f, pz0 = 126f, pz1 = 138f, ph_h = 4f, pc = (px0 + px1) / 2f;
        P.Box(g, "PH_S_W", new Vector3((px0 + pc - 2f) / 2f, ph_h / 2f, pz0), new Vector3(pc - 2f - px0, ph_h, 0.3f), ph, Vector2.one);
        P.Box(g, "PH_S_E", new Vector3((pc + 2f + px1) / 2f, ph_h / 2f, pz0), new Vector3(px1 - pc - 2f, ph_h, 0.3f), ph, Vector2.one);
        P.Box(g, "PH_S_Top", new Vector3(pc, ph_h - 0.6f, pz0), new Vector3(4f, 1.2f, 0.3f), ph, Vector2.one);
        P.Box(g, "PH_N", new Vector3((px0 + px1) / 2f, ph_h / 2f, pz1), new Vector3(px1 - px0, ph_h, 0.3f), ph, Vector2.one);
        P.Box(g, "PH_W", new Vector3(px0, ph_h / 2f, (pz0 + pz1) / 2f), new Vector3(0.3f, ph_h, pz1 - pz0), ph, Vector2.one);
        P.Box(g, "PH_E", new Vector3(px1, ph_h / 2f, (pz0 + pz1) / 2f), new Vector3(0.3f, ph_h, pz1 - pz0), ph, Vector2.one);
        P.Box(g, "PH_Roof", new Vector3((px0 + px1) / 2f, ph_h + 0.15f, (pz0 + pz1) / 2f), new Vector3(px1 - px0 + 0.4f, 0.3f, pz1 - pz0 + 0.4f), ph, Vector2.one);
        P.AddLight(g, "PH_Light", new Vector3(pc, ph_h - 0.4f, pz0 + 7f), LightType.Point, new Color(1f, 0.85f, 0.65f), 8f, 14f);
        P.AddLight(g, "PH_Light2", new Vector3(pc - 5f, ph_h - 0.4f, pz0 + 3f), LightType.Point, new Color(1f, 0.85f, 0.65f), 5f, 11f);
        P.AddLight(g, "VaultGlow", new Vector3(pc, 2.4f, pz1 - 2.4f), LightType.Point, new Color(0.8f, 0.9f, 1f), 3f, 5f);
        // 금고: 두꺼운 강철 틀 + 활짝 열린 문 + 텅 빈 선반
        var steel = H["Steel"];
        P.Box(g, "VaultFrame", new Vector3(pc, 1.4f, pz1 - 0.6f), new Vector3(3f, 2.8f, 0.9f), steel, Vector2.one);
        P.Box(g, "VaultInside", new Vector3(pc, 1.4f, pz1 - 1.08f), new Vector3(2.2f, 2.2f, 0.06f), P.Lit("VaultDark", null, new Color(0.05f, 0.05f, 0.06f), 0.4f), Vector2.one, collider: false);
        var vdoor = P.Box(g, "VaultDoor", new Vector3(pc - 2.4f, 1.4f, pz1 - 1.8f), new Vector3(0.25f, 2.4f, 2.2f), steel, Vector2.one);
        vdoor.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
        P.Box(g, "VaultShelf", new Vector3(pc, 1.2f, pz1 - 1.02f), new Vector3(1.8f, 0.05f, 0.4f), steel, Vector2.one, collider: false);
        // 전용 엘리베이터(펜트하우스 동쪽 벽)
        P.Box(g, "PrivateLift", new Vector3(px1 - 0.2f, 1.2f, pz0 + 7f), new Vector3(0.06f, 2.4f, 1.8f), P.Lit("LiftGold", null, new Color(0.55f, 0.45f, 0.25f), 0.7f, metallic: 0.8f), Vector2.one, collider: false);
        b.partnerLift = P.Marker(triggers, "PartnerLift", new Vector3(px1 - 1.2f, 0.02f, pz0 + 7f), 90f);
        b.vaultTrigger = Trigger(triggers, "VaultRoom", new Vector3(pc, 1.5f, pz0 + 4f), new Vector3(px1 - px0 - 2f, 3f, 6f));
        b.vaultShot = P.Marker(triggers, "VaultShot", new Vector3(pc - 3.5f, 1.7f, pz0 + 2.5f), 0f);

        // 정리반(옥상): 근접 둘 · 총잡이 둘
        var r1 = Cleaner(people, "Roof_Cleaner_A", new Vector3(312f, 0.02f, 118f), 180f, false);
        var r2 = Cleaner(people, "Roof_Cleaner_B", new Vector3(326f, 0.02f, 121f), 180f, false);
        var r3 = Cleaner(people, "Roof_Cleaner_Gun1", new Vector3(316f, 0.02f, 124f), 180f, true);
        var r4 = Cleaner(people, "Roof_Cleaner_Gun2", new Vector3(331f, 0.02f, 114f), 200f, true);
        b.zoneRoof = Zone(triggers, "Zone_Roof", new Vector3(319f, 1.5f, 117f), new Vector3(30f, 3f, 22f), r1, r2, r3, r4);

        // 옛 파트너: 금고 앞(감독이 꺼낸다). 맨손 격투 — 형사 출신
        var partner = P.CreateThug(people, "Partner", new Vector3(pc, 0.02f, pz1 - 3.5f), 180f, TraitorCharacter);
        SetCombat(partner, false);
        partner.displayName = "옛 파트너";
        partner.isBoss = true;
        partner.GetComponent<Health>().maxHealth = 280;
        partner.damage = 12;
        partner.heavyEvery = 2;
        partner.heavyDamage = 22;
        partner.windup = 0.48f;
        partner.heavyWindup = 0.85f;
        partner.attackCooldown = 1.1f;
        partner.recover = 0.45f;
        partner.staggerable = false;
        partner.executeEvery = 4; // 처형기(되감으면 보인다)
        partner.executeDamage = 42;
        b.partner = partner;
    }
}
