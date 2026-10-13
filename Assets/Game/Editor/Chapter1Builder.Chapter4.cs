using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using P = PrologueBuilder;

/// <summary>
/// 4장 「송신소」 장면 — 도시 외곽 산 위의 한결 FM 송신소. 메뉴: Game/Build Chapter 4.
///
/// 배치(미터, x 동쪽 · z 북쪽, y 높이):
///   국도변 공터(시작, y 0): x -24~24, z -135~-110. 서쪽에 노점(저장·교환).
///   오르막 1: z -110(y 0) → z -40(y 10), 폭 12.
///   굽잇길 공터(매복, y 10): x -30~30, z -40~-10 — 넓다. 사고 난 승합차·상자·드럼통(엄폐물), 정리반 넷.
///   오르막 2: z -10(y 10) → z 50(y 20).
///   송신소(y 20): x -50~50, z 50~130, 철망. 정문 z 50. 서쪽 송신 건물, 동쪽 송신탑(높이 60, 8m에 발판 — 저격수, 계단).
///   양옆은 숲(나무)과 낭떠러지: 보이지 않는 벽으로 막는다.
/// </summary>
public static partial class Chapter1Builder
{
    const string Ch4ScenePath = "Assets/Game/Scenes/Chapter4.unity";
    const string Ch4NavMeshPath = "Assets/Game/Scenes/Chapter4_NavMesh.asset";
    const string Ch4VolumePath = P.ArtRoot + "/Chapter4Volume.asset";
    const float TopY = 20f, BendY = 10f;

    class Built4
    {
        public Transform zoneStart, shopSave, roadSave, gateSave, partnerStart, duelShot;
        public TapeShop shop;
        public StoryTrigger shopApproach, gateTrigger, yardTrigger;
        public CombatZone zoneAmbush, zoneSniper;
        public EnemyAI partner, sniper;
    }

    [MenuItem("Game/Build Chapter 4")]
    static void BuildChapter4Menu()
    {
        if (EditorUtility.DisplayDialog("4장 만들기", $"{Ch4ScenePath}를 새로 만듭니다(기존 내용은 덮어씀).", "만들기", "취소")) BuildChapter4();
    }

    public static void BuildChapter4()
    {
        P.boxMeshes.Clear();
        beamCone = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        P.root = new GameObject("Chapter4").transform;
        var M = P.CreateMaterials();
        var X = ExtraMaterials();
        var H = HarborMaterials();
        var b = new Built4();
        var triggers = new GameObject("Triggers").transform;
        var people = new GameObject("People").transform;

        var g = P.Group("Ch4");
        Ch4Mountain(g, M, X, H, triggers, b);
        Ch4Ambush(g, M, H, triggers, people, b);
        Ch4Station(g, M, X, H, triggers, people, b);

        b.zoneStart = P.Marker(triggers, "Ch4Start", new Vector3(8f, 0.05f, -130f), 0f);
        var player = P.CreatePlayer(b.zoneStart, out _, out var playerRagdoll, out _, out _);

        var mainCam = Object.FindAnyObjectByType<Camera>();
        mainCam.farClipPlane = 600f;
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

        var systems = new GameObject("GameSystems");
        var gm = systems.AddComponent<GameManager>();
        gm.player = player;
        gm.stageTitle = "";
        gm.finalObjective = "";
        systems.AddComponent<HUD>().player = player;
        var rewind = systems.AddComponent<RewindEffect>();
        var rainAudio = new GameObject("RainAudio").AddComponent<AudioSource>();
        rainAudio.transform.SetParent(systems.transform);
        rainAudio.spatialBlend = 0f;
        rainAudio.volume = 0.5f;
        // 비 없음(2026-10-13): P.CreateRain을 부르지 않는다

        var d = systems.AddComponent<Chapter4Director>();
        d.player = player;
        d.brain = brain;
        d.gameplayCamera = gameplay;
        d.gameplayRig = rig;
        d.dynamicA = dynA;
        // 비 없음(2026-10-13 사용자 요청): 빗소리를 연결하지 않는다
        d.rewindEffect = rewind;
        d.playerRagdoll = playerRagdoll;
        d.zoneStart = b.zoneStart;
        d.shop = b.shop;
        d.shopApproach = b.shopApproach;
        d.shopSave = b.shopSave;
        d.roadSave = b.roadSave;
        d.zoneAmbush = b.zoneAmbush;
        d.gateTrigger = b.gateTrigger;
        d.gateSave = b.gateSave;
        d.partnerStart = b.partnerStart;
        d.duelShot = b.duelShot;
        d.yardTrigger = b.yardTrigger;
        d.partner = b.partner;
        d.sniper = b.sniper;
        d.zoneSniper = b.zoneSniper;

        P.SetupEnvironment(Ch4VolumePath);
        var sky = SetupHarborSky(mainCam);
        sky.transform.SetParent(systems.transform);
        RenderSettings.ambientLight = new Color(0.4f, 0.41f, 0.48f);
        P.BakeNavMesh(Ch4NavMeshPath);
        P.EnforceMaterialKeywords();

        EditorSceneManager.SaveScene(scene, Ch4ScenePath);
        var list = EditorBuildSettings.scenes.Where(s => s.path != Ch4ScenePath).ToList();
        int ch3 = list.FindIndex(s => s.path.EndsWith("/Chapter3.unity"));
        list.Insert(ch3 >= 0 ? ch3 + 1 : list.Count, new EditorBuildSettingsScene(Ch4ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[4장] {Ch4ScenePath} 생성 완료");
    }

    /// <summary>윗면이 a(낮은 끝)→b(높은 끝)로 오르는 두꺼운 경사판(아랫면은 산 속에 묻힌다)</summary>
    static void Ramp(Transform g, string name, Vector3 a, Vector3 b, float width, Material mat)
    {
        Vector3 along = b - a;
        float len = along.magnitude;
        var dir = along / len;
        var normal = Vector3.Cross(dir, Vector3.Cross(Vector3.up, dir)).normalized; // 경사면의 위쪽 수직
        if (normal.y < 0f) normal = -normal;
        const float thick = 24f;
        var box = P.Box(g, name, (a + b) / 2f - normal * thick / 2f, new Vector3(width, thick, len), mat, new Vector2(4f, 4f));
        box.transform.rotation = Quaternion.LookRotation(dir, normal);
    }

    // =====================================================================
    // 산 · 길 · 노점
    // =====================================================================

    static void Ch4Mountain(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built4 b)
    {
        var g = new GameObject("Mountain").transform;
        g.SetParent(parent);
        var dirt = P.Lit("HillDirt", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.36f, 0.33f, 0.28f), 0.15f);
        var rock = P.Lit("HillRock", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.3f, 0.3f, 0.31f), 0.25f);
        // 국도변 공터
        P.Box(g, "Lot", new Vector3(0f, -0.25f, -122f), new Vector3(48f, 0.5f, 26f), M["Asphalt"], new Vector2(4f, 4f));
        P.Box(g, "Highway", new Vector3(0f, -0.26f, -150f), new Vector3(300f, 0.5f, 30f), M["Asphalt"], new Vector2(4f, 4f));
        for (float x = -140f; x < 140f; x += 6f)
            P.Box(g, "LaneDash", new Vector3(x, 0.01f, -150f), new Vector3(3f, 0.01f, 0.15f), M["Paint"], Vector2.one, collider: false);
        InvisibleWall(g, "LotBlock_S", new Vector3(0f, 2f, -136f), new Vector3(60f, 4f, 0.5f));
        InvisibleWall(g, "LotBlock_W", new Vector3(-24f, 2f, -122f), new Vector3(0.5f, 4f, 28f));
        InvisibleWall(g, "LotBlock_E", new Vector3(24f, 2f, -122f), new Vector3(0.5f, 4f, 28f));
        // 길: 오르막 1 · 굽잇길 공터 · 오르막 2
        Ramp(g, "Road_Up1", new Vector3(0f, 0f, -110f), new Vector3(0f, BendY, -40f), 12f, M["Asphalt"]);
        P.Box(g, "Bend", new Vector3(0f, BendY - 12f, -25f), new Vector3(60f, 24f, 30f), M["Asphalt"], new Vector2(4f, 4f));
        Ramp(g, "Road_Up2", new Vector3(0f, BendY, -10f), new Vector3(0f, TopY, 50f), 12f, M["Asphalt"]);
        P.Box(g, "Summit", new Vector3(0f, TopY - 12f, 90f), new Vector3(100f, 24f, 80f), dirt, new Vector2(4f, 4f));
        // 길 양옆: 가드레일(보이는 것) + 보이지 않는 벽(떨어지지 않게) · 비탈 덩어리
        foreach (var (a, c) in new[] { (new Vector3(0f, 0f, -110f), new Vector3(0f, BendY, -40f)), (new Vector3(0f, BendY, -10f), new Vector3(0f, TopY, 50f)) })
            foreach (float sx in new[] { -6.2f, 6.2f })
            {
                Vector3 p0 = a + Vector3.right * sx, p1 = c + Vector3.right * sx;
                var rail = P.Box(g, "GuardRail", (p0 + p1) / 2f + Vector3.up * 0.75f, new Vector3(0.15f, 0.35f, Vector3.Distance(p0, p1)), H["Steel"], Vector2.one, collider: false);
                rail.transform.rotation = Quaternion.LookRotation(p1 - p0);
                var wall = new GameObject("RoadEdge");
                wall.transform.SetParent(g);
                wall.transform.SetPositionAndRotation((p0 + p1) / 2f + Vector3.up * 2f, Quaternion.LookRotation(p1 - p0));
                wall.AddComponent<BoxCollider>().size = new Vector3(0.4f, 4f, Vector3.Distance(p0, p1));
                // 길 바깥 비탈(길보다 조금 낮은 흙 덩어리) — 아래가 허공으로 보이지 않게
                float side = Mathf.Sign(sx);
                var slope = P.Box(g, "Slope", (p0 + p1) / 2f + new Vector3(side * 16f, -8f, 0f), new Vector3(20f, 16f, Vector3.Distance(p0, p1)), dirt, new Vector2(4f, 4f), collider: false);
                slope.transform.rotation = Quaternion.LookRotation(p1 - p0) * Quaternion.Euler(0f, 0f, side * 14f);
            }
        // 굽잇길 공터 가장자리(남·북은 길이 드나드는 가운데만 비운다)
        InvisibleWall(g, "BendEdge_W", new Vector3(-30f, BendY + 2f, -25f), new Vector3(0.5f, 4f, 30f));
        InvisibleWall(g, "BendEdge_E", new Vector3(30f, BendY + 2f, -25f), new Vector3(0.5f, 4f, 30f));
        foreach (float z in new[] { -40f, -10f })
            foreach (float x in new[] { -18f, 18f })
                InvisibleWall(g, "BendEdge_SN", new Vector3(x, BendY + 2f, z), new Vector3(24f, 4f, 0.5f));
        // 공터 북쪽 가장자리(오르막 1 양옆) · 산꼭대기 둘레(철망 바깥으로 떨어지지 않게)
        foreach (float x in new[] { -15f, 15f }) InvisibleWall(g, "LotEdge_N", new Vector3(x, 2f, -109.5f), new Vector3(18f, 4f, 0.5f));
        foreach (float x in new[] { -28f, 28f }) InvisibleWall(g, "SummitEdge_S", new Vector3(x, TopY + 2f, 50.2f), new Vector3(44f, 4f, 0.5f));
        InvisibleWall(g, "SummitEdge_W", new Vector3(-50f, TopY + 2f, 90f), new Vector3(0.5f, 4f, 80f));
        InvisibleWall(g, "SummitEdge_E", new Vector3(50f, TopY + 2f, 90f), new Vector3(0.5f, 4f, 80f));
        InvisibleWall(g, "SummitEdge_N", new Vector3(0f, TopY + 2f, 130f), new Vector3(100f, 4f, 0.5f));
        // 숲: 길과 공터 둘레
        var rnd = new System.Random(4);
        for (int i = 0; i < 70; i++)
        {
            float z = -110f + (float)rnd.NextDouble() * 220f;
            float side = rnd.Next(2) == 0 ? -1f : 1f;
            float x = side * (12f + (float)rnd.NextDouble() * 30f);
            float y = z < -40f ? Mathf.Lerp(0f, BendY, (z + 110f) / 70f) : z < -10f ? BendY : z < 50f ? Mathf.Lerp(BendY, TopY, (z + 10f) / 60f) : TopY;
            if (z > 50f && Mathf.Abs(x) < 52f) continue;   // 송신소 안은 비운다
            if (z > -40f && z < -10f && Mathf.Abs(x) < 32f) continue; // 굽잇길 공터
            var tree = TryProp("StreetTree", g, new Vector3(x, y - 2.5f, z), (float)rnd.NextDouble() * 360f);
            if (tree) foreach (var c in tree.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        // ---------- 국도변 노점(서쪽, 남쪽을 본다) ----------
        var k = new GameObject("RoadsideStall").transform;
        k.SetParent(g);
        Vector3 stall = new(-12f, 0f, -118f);
        P.Box(k, "StallCounter", stall + new Vector3(0f, 0.55f, 0f), new Vector3(3.2f, 1.1f, 1.0f), M["DarkWood"], Vector2.one);
        P.Box(k, "StallBack", stall + new Vector3(0f, 1.3f, 0.9f), new Vector3(3.4f, 2.6f, 0.1f), X["ShopWall"], Vector2.one);
        P.Box(k, "StallRoof", stall + new Vector3(0f, 2.65f, 0.2f), new Vector3(3.8f, 0.1f, 2.0f), X["Awning"], Vector2.one, collider: false);
        var sign = P.Box(k, "StallSign", stall + new Vector3(0f, 2.35f, -0.72f), new Vector3(2.4f, 0.42f, 0.03f), Neon("SignTapes", "TAPES", new Color(1f, 0.75f, 0.3f) * 3.5f, new[] { "Bahnschrift", "Arial" }), Vector2.one, collider: false);
        FitUVSouth(sign, 2.4f, 0.42f);
        TryProp("CassetteShelf", k, stall + new Vector3(-1f, 0f, 0.6f), 180f);
        TryProp("Turntable", k, stall + new Vector3(0.9f, 1.1f, 0f), 180f);
        P.AddLight(k, "StallLight", stall + new Vector3(0f, 2.3f, -0.2f), LightType.Point, new Color(1f, 0.78f, 0.5f), 3f, 7f);
        TryProp("Sedan", g, new Vector3(10f, 0f, -126f), 0f); // 레이가 타고 온 차
        var shopGo = new GameObject("TapeShop_Roadside");
        shopGo.transform.SetParent(triggers);
        shopGo.transform.position = stall + new Vector3(0f, 1f, -1.6f);
        var sc = shopGo.AddComponent<BoxCollider>();
        sc.size = new Vector3(3.2f, 2f, 2.2f);
        sc.isTrigger = true;
        var shop = shopGo.AddComponent<TapeShop>();
        shop.shopName = "국도변 노점";
        shop.stock = new List<TapeShop.Item>
        {
            new() { tape = MusicTape("심야 라디오 B면 (복사본)", "late_radio", new[] { 0.90f, 0.92f, 0.86f }, "비 오는 고가도로", "새벽 세 시", "꺼지지 않는 간판") },
            new() { tape = MusicTape("밤길 드라이브", "night_drive", new[] { 0.91f, 0.83f }, "송신탑으로 가는 길", "와이퍼 소리") }, // 4장 전용(VARCO Sound)
        };
        b.shop = shop;
        b.shopApproach = Trigger(triggers, "StallApproach", stall + new Vector3(0f, 1f, -2.5f), new Vector3(6f, 2f, 3f));
        b.shopSave = P.Marker(triggers, "StallSave", stall + new Vector3(0f, 0.05f, -2f), 0f);
        TapeSpot(g, triggers, "LotTape", new Vector3(20f, 0f, -114f), 1, 1, box: true);
    }

    // =====================================================================
    // 굽잇길 매복(정리반 넷)
    // =====================================================================

    static void Ch4Ambush(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Transform triggers, Transform people, Built4 b)
    {
        var g = new GameObject("BendAmbush").transform;
        g.SetParent(parent);
        float y = BendY;
        (string name, Vector3 at, float yaw)[] props =
        {
            ("BlackVan", new Vector3(-8f, y, -22f), 70f), ("CrateStack", new Vector3(10f, y, -30f), 20f),
            ("SteelDrums", new Vector3(16f, y, -20f), 0f), ("ConstructionBarrier", new Vector3(-2f, y, -14f), 0f),
            ("ConstructionBarrier", new Vector3(4f, y, -14f), 0f), ("PalletStack", new Vector3(-20f, y, -30f), 40f),
            ("TrafficCone", new Vector3(-4f, y, -36f), 0f), ("TrafficCone", new Vector3(4.5f, y, -35f), 30f),
        };
        foreach (var (name, at, yaw) in props) TryProp(name, g, at, yaw);
        // 승합차 전조등(공터를 비춘다) · 작업등
        P.AddLight(g, "VanHeadlight", new Vector3(-5.6f, y + 0.9f, -21f), LightType.Spot, new Color(1f, 0.95f, 0.85f), 30f, 30f, 60f, Quaternion.Euler(5f, 160f, 0f));
        FloodPole(g, H, new Vector3(22f, y, -14f), true);
        var a1 = Cleaner(people, "Bend_A", new Vector3(-4f, y + 0.02f, -18f), 180f, false);
        var a2 = Cleaner(people, "Bend_B", new Vector3(8f, y + 0.02f, -24f), 180f, false);
        var a3 = Cleaner(people, "Bend_Gun1", new Vector3(-14f, y + 0.02f, -14f), 160f, true);
        var a4 = Cleaner(people, "Bend_Gun2", new Vector3(14f, y + 0.02f, -13f), 200f, true);
        b.zoneAmbush = Zone(triggers, "Zone_Bend", new Vector3(0f, y + 1.5f, -25f), new Vector3(56f, 3f, 26f), a1, a2, a3, a4);
        b.roadSave = P.Marker(triggers, "BendSave", new Vector3(0f, y + 0.05f, -16f), 0f);
    }

    // =====================================================================
    // 송신소(마당 · 송신 건물 · 송신탑과 발판 · 파트너 · 저격수)
    // =====================================================================

    static void Ch4Station(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Transform people, Built4 b)
    {
        var g = new GameObject("TransmitterStation").transform;
        g.SetParent(parent);
        float y = TopY;
        // 마당 포장(콘크리트) · 철망(정문 x -5~5만 열림)
        P.Box(g, "Yard", new Vector3(0f, y - 0.1f, 90f), new Vector3(96f, 0.2f, 76f), P.Lit("YardConcrete", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.62f, 0.62f, 0.6f), 0.3f), new Vector2(4f, 4f));
        LotFence(g, M, new Vector3(-48f, y, 52f), new Vector3(-5f, y, 52f));
        LotFence(g, M, new Vector3(5f, y, 52f), new Vector3(48f, y, 52f));
        LotFence(g, M, new Vector3(-48f, y, 52f), new Vector3(-48f, y, 128f));
        LotFence(g, M, new Vector3(48f, y, 52f), new Vector3(48f, y, 128f));
        LotFence(g, M, new Vector3(-48f, y, 128f), new Vector3(48f, y, 128f));
        foreach (float x in new[] { -5f, 5f }) P.Box(g, "GatePost", new Vector3(x, y + 1.8f, 52f), new Vector3(0.3f, 3.6f, 0.3f), H["Steel"], Vector2.one);
        var leaf = P.Box(g, "GateLeaf_Open", new Vector3(-8.5f, y + 1.2f, 50.5f), new Vector3(0.08f, 2.4f, 4.6f), M["ChainLink"], Vector2.one, collider: false);
        leaf.transform.rotation = Quaternion.Euler(0f, -30f, 0f);
        P.AddLight(g, "GateLamp", new Vector3(5.5f, y + 3.6f, 52.5f), LightType.Point, new Color(1f, 0.85f, 0.6f), 4f, 12f);
        b.gateTrigger = Trigger(triggers, "GateTrigger", new Vector3(0f, y + 1f, 49f), new Vector3(12f, 2f, 4f));
        b.gateSave = P.Marker(triggers, "GateSave", new Vector3(0f, y + 0.05f, 49f), 0f);
        b.yardTrigger = Trigger(triggers, "YardTrigger", new Vector3(0f, y + 1f, 64f), new Vector3(40f, 2f, 10f));

        // 송신 건물(서쪽): 콘크리트 · 창에 불 · 발전기
        P.Building(g, "TransmitterHouse", new Vector3(-30f, y + 4f, 104f), new Vector3(24f, 8f, 18f), M["FacadeC"], M["BuildingBack"], Vector3.right);
        TryProp("AcUnit", g, new Vector3(-17f, y, 98f), 90f);
        TryProp("GasCylinders", g, new Vector3(-17.5f, y, 110f), 90f);
        P.AddLight(g, "HouseLamp", new Vector3(-17.5f, y + 4f, 104f), LightType.Spot, new Color(1f, 0.82f, 0.55f), 30f, 18f, 100f, Quaternion.Euler(60f, 90f, 0f));
        // 엄폐물(마당)
        foreach (var (name, at, yaw) in new[] { ("PalletStack", new Vector3(-8f, y, 78f), 15f), ("CrateStack", new Vector3(10f, y, 74f), 70f), ("SteelDrums", new Vector3(-14f, y, 90f), 0f), ("BoxTruck", new Vector3(30f, y, 70f), 160f) })
            TryProp(name, g, at, yaw);
        FloodPole(g, H, new Vector3(-20f, y, 62f), true);
        FloodPole(g, H, new Vector3(36f, y, 112f), false);
        // 결투 자리(마당 한가운데)가 어둡지 않게: 투광등 둘 더 + 송신탑 밑 작업등
        FloodPole(g, H, new Vector3(18f, y, 60f), true);
        FloodPole(g, H, new Vector3(-24f, y, 92f), false);
        P.AddLight(g, "TowerBaseLamp", new Vector3(22f, y + 5f, 92f), LightType.Spot, new Color(1f, 0.85f, 0.6f), 40f, 26f, 110f, Quaternion.Euler(70f, 200f, 0f));

        // 송신탑(동쪽): 다리 넷이 위로 갈수록 좁아지고, 가로대·대각선, 꼭대기 빨간 항공등. 8m 높이 발판 + 계단
        Vector3 tc = new(22f, y, 100f);
        var steel = H["Steel"];
        const float towerH = 60f;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            Vector3 bottom = tc + new Vector3(sx * 4f, 0f, sz * 4f), top = tc + new Vector3(sx * 0.8f, towerH, sz * 0.8f);
            var leg = P.Box(g, "TowerLeg", (bottom + top) / 2f, new Vector3(0.35f, Vector3.Distance(bottom, top), 0.35f), steel, Vector2.one, collider: false);
            leg.transform.rotation = Quaternion.LookRotation(Vector3.forward, top - bottom);
            P.Box(g, "TowerFoot", bottom + Vector3.up * 0.5f, new Vector3(0.9f, 1f, 0.9f), steel, Vector2.one);
        }
        for (float h = 6f; h < towerH; h += 6f)
        {
            float half = Mathf.Lerp(4f, 0.8f, h / towerH);
            foreach (var (c, s) in new[] { (new Vector3(0f, h, half), new Vector3(half * 2f, 0.15f, 0.15f)), (new Vector3(0f, h, -half), new Vector3(half * 2f, 0.15f, 0.15f)), (new Vector3(half, h, 0f), new Vector3(0.15f, 0.15f, half * 2f)), (new Vector3(-half, h, 0f), new Vector3(0.15f, 0.15f, half * 2f)) })
                P.Box(g, "TowerBrace", tc + c, s, steel, Vector2.one, collider: false);
        }
        P.AddLight(g, "AviationRed", tc + Vector3.up * (towerH + 0.6f), LightType.Point, new Color(1f, 0.08f, 0.04f), 6f, 14f);
        P.Box(g, "AviationLamp", tc + Vector3.up * (towerH + 0.3f), new Vector3(0.5f, 0.5f, 0.5f), P.Unlit("AviationGlow", new Color(1f, 0.1f, 0.05f) * 4f), Vector2.one, collider: false);
        // 발판(8m): 사방 난간, 남쪽 가운데로 계단이 올라온다
        const float deckY = 8f;
        var deckMat = P.Lit("DeckGrate", null, new Color(0.3f, 0.31f, 0.33f), 0.4f, metallic: 0.5f);
        P.Box(g, "Deck", tc + new Vector3(0f, deckY - 0.15f, 0f), new Vector3(10f, 0.3f, 10f), deckMat, Vector2.one);
        foreach (var (c, s) in new[] { (new Vector3(-2.9f, deckY + 0.55f, -5f), new Vector3(4.2f, 1.1f, 0.08f)), (new Vector3(2.9f, deckY + 0.55f, -5f), new Vector3(4.2f, 1.1f, 0.08f)), (new Vector3(0f, deckY + 0.55f, 5f), new Vector3(10f, 1.1f, 0.08f)), (new Vector3(-5f, deckY + 0.55f, 0f), new Vector3(0.08f, 1.1f, 10f)), (new Vector3(5f, deckY + 0.55f, 0f), new Vector3(0.08f, 1.1f, 10f)) })
            P.Box(g, "DeckRail", tc + c, s, steel, Vector2.one);
        var stairS = new Dictionary<string, Material> { ["Floor"] = deckMat };
        Stairs(g, stairS, tc + new Vector3(0f, 0f, -5f - 14f), 1.6f, 14f, deckY);
        P.AddLight(g, "DeckLamp", tc + new Vector3(0f, deckY + 2.4f, 0f), LightType.Point, new Color(0.9f, 0.95f, 1f), 3f, 10f);
        // 저격수: 발판 위, 마당(남쪽)을 겨눈다. 되감은 뒤에야 레이가 안다(구역은 감독이 켠다)
        var sniper = P.CreateThug(people, "Tower_Sniper", tc + new Vector3(1.5f, deckY + 0.02f, -2f), 180f, "Enforcer");
        SetCombat(sniper, true);
        sniper.displayName = "저격수";
        sniper.holdPosition = true;
        sniper.aimInterruptible = true;
        sniper.awarenessArc = 120f;
        sniper.shootRange = 70f;
        sniper.aimTime = 2.0f;
        sniper.shotDamage = 40;
        sniper.shotCooldown = 3.0f;
        sniper.laserWidth = 3f;
        sniper.shotSound = "SniperShot";
        sniper.shotVolume = 1.2f;
        sniper.GetComponent<Health>().maxHealth = 70;
        var ik = sniper.aimIK;
        if (ik.heldItem) Object.DestroyImmediate(ik.heldItem.gameObject);
        var hold = new GameObject("RifleHold").transform;
        hold.SetParent(ik.transform, false);
        var rifle = TryProp("SniperRifle", hold, Vector3.zero, 0f);
        if (rifle)
        {
            rifle.transform.localPosition = new Vector3(0f, -0.08f, 0.28f);
            rifle.transform.localRotation = Quaternion.identity;
            foreach (var t in rifle.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
        ik.heldItem = hold;
        sniper.muzzleDistance = 0.88f;
        b.sniper = sniper;
        b.zoneSniper = Zone(triggers, "Zone_Tower", tc + new Vector3(0f, 4f, -8f), new Vector3(16f, 12f, 26f), sniper);

        // 옛 파트너: 마당 한가운데에서 정문을 보고 기다린다
        b.partnerStart = P.Marker(triggers, "PartnerStart", new Vector3(0f, y + 0.02f, 84f), 180f);
        var partner = P.CreateThug(people, "Partner", b.partnerStart.position, 180f, TraitorCharacter);
        SetCombat(partner, false);
        partner.displayName = "옛 파트너";
        partner.isBoss = true;
        partner.GetComponent<Health>().maxHealth = 320;
        partner.damage = 13;
        partner.heavyEvery = 2;
        partner.heavyDamage = 24;
        partner.windup = 0.46f;
        partner.heavyWindup = 0.82f;
        partner.attackCooldown = 1.05f;
        partner.recover = 0.42f;
        partner.staggerable = false;
        partner.executeEvery = 3; // 처형기: 결판이라 더 자주
        partner.executeDamage = 45;
        b.partner = partner;
        b.duelShot = P.Marker(triggers, "DuelShot", new Vector3(-3.5f, y + 1.7f, 76f), 0f);
        TapeSpot(g, triggers, "YardTape", new Vector3(-17f, y, 116f), 2, 1, box: true);
    }
}
