using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using P = PrologueBuilder;

/// <summary>
/// 5장 「30주년」 + 종장 장면 — 한결 타워 지하. 메뉴: Game/Build Chapter 5.
///
/// 배치(미터, y 0):
///   지하 주차장: x -18~18, z 0~40, 천장 3.6. 남쪽 입구에서 시작. 서남쪽 구석에 7번(가게). 북쪽 벽 가운데 화물 엘리베이터.
///     (2026-10-14 "주차장이 쓸데없이 넓다" → 60×60에서 36×40으로 줄였다. 싸움 한 판 + 가게 + 엘리베이터만 있는 곳이다)
///   연구소(따로, x 300~328, z 12~28): 복도 z 17~21 · 북쪽 실험실 넷(5.5m 폭, 문이 열려 있다) · 남쪽 장비실 둘(14×5m). 동쪽 끝 격벽.
///     (2026-10-14 "쓸데없이 넓다" → 44×20m · 복도 폭 6m에서 줄였다)
///   마스터 테이프 홀(따로, x 406~434, z 0~30, 높이 8): 가운데 받침대의 거대한 릴 녹음기와 마스터 테이프 · 서북쪽 방송 콘솔
///     · 북쪽 벽 높이 4m의 유리 관람석(z 26~30, 윗선 셋이 서 있다).
///     (2026-10-14 "마지막 보스 구역이 너무 크다" → 40×36m에서 28×26m로 줄였다. 0번 하나 + 특수경비 둘이 싸우는 곳)
/// </summary>
public static partial class Chapter1Builder
{
    const string Ch5ScenePath = "Assets/Game/Scenes/Chapter5.unity";
    const string Ch5NavMeshPath = "Assets/Game/Scenes/Chapter5_NavMesh.asset";
    const string Ch5VolumePath = P.ArtRoot + "/Chapter5Volume.asset";

    class Built5
    {
        public Transform zoneStart, shopSave, labEntry, hallEntry, galleryShot, masterShot, masterTape;
        public TapeShop shop;
        public StoryTrigger shopApproach, hallTrigger;
        public CombatZone zoneGarage, zoneLab;
        public StoryInteractable liftUse, hallDoor, masterUse, consoleUse;
        public EnemyAI zero;
        public GameObject[] guards;
    }

    [MenuItem("Game/Build Chapter 5")]
    static void BuildChapter5Menu()
    {
        if (EditorUtility.DisplayDialog("5장 만들기", $"{Ch5ScenePath}를 새로 만듭니다(기존 내용은 덮어씀).", "만들기", "취소")) BuildChapter5();
    }

    public static void BuildChapter5()
    {
        P.boxMeshes.Clear();
        beamCone = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        P.root = new GameObject("Chapter5").transform;
        var M = P.CreateMaterials();
        var X = ExtraMaterials();
        var H = HarborMaterials();
        var b = new Built5();
        var triggers = new GameObject("Triggers").transform;
        var people = new GameObject("People").transform;

        var g = P.Group("Ch5");
        Ch5Garage(g, M, H, triggers, people, b);
        Ch5Lab(g, triggers, people, b);
        Ch5Hall(g, M, H, triggers, people, b);

        b.zoneStart = P.Marker(triggers, "Ch5Start", new Vector3(0f, 0.05f, 3f), 0f);
        var player = P.CreatePlayer(b.zoneStart, out _, out _, out _, out _);

        var mainCam = Object.FindAnyObjectByType<Camera>();
        mainCam.farClipPlane = 300f;
        mainCam.nearClipPlane = 0.05f;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
        mainCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var brain = mainCam.gameObject.AddComponent<CinemachineBrain>();
        var cams = new GameObject("Cameras").transform;
        var start = b.zoneStart.position;
        var gameplay = P.VCam(cams, "GameplayCamera", start + new Vector3(0f, 2f, -3f), start + Vector3.up * 1.5f, 55f);
        var rig = gameplay.gameObject.AddComponent<ThirdPersonCamera>();
        rig.target = player.transform;
        rig.distance = 4.6f;
        var dynA = P.VCam(cams, "Shot_Dynamic_A", Vector3.zero, Vector3.forward, 40f);

        var systems = new GameObject("GameSystems");
        var gm = systems.AddComponent<GameManager>();
        gm.player = player;
        gm.stageTitle = "";
        gm.finalObjective = "";
        systems.AddComponent<HUD>().player = player;
        var rewind = systems.AddComponent<RewindEffect>();

        var d = systems.AddComponent<Chapter5Director>();
        d.player = player;
        d.brain = brain;
        d.gameplayCamera = gameplay;
        d.gameplayRig = rig;
        d.dynamicA = dynA;
        d.rewindEffect = rewind;
        d.zoneStart = b.zoneStart;
        d.shopSave = b.shopSave;
        d.shop = b.shop;
        d.shopApproach = b.shopApproach;
        d.zoneGarage = b.zoneGarage;
        d.liftUse = b.liftUse;
        d.labEntry = b.labEntry;
        d.zoneLab = b.zoneLab;
        d.hallDoor = b.hallDoor;
        d.hallEntry = b.hallEntry;
        d.galleryShot = b.galleryShot;
        d.masterShot = b.masterShot;
        d.hallTrigger = b.hallTrigger;
        d.zero = b.zero;
        d.guards = b.guards;
        d.masterUse = b.masterUse;
        d.consoleUse = b.consoleUse;
        d.masterTape = b.masterTape;

        P.SetupEnvironment(Ch5VolumePath);
        RenderSettings.ambientLight = new Color(0.4f, 0.41f, 0.48f);
        RenderSettings.fog = false; // 실내뿐
        P.BakeNavMesh(Ch5NavMeshPath);
        P.EnforceMaterialKeywords();

        EditorSceneManager.SaveScene(scene, Ch5ScenePath);
        var list = EditorBuildSettings.scenes.Where(s => s.path != Ch5ScenePath).ToList();
        int ch4 = list.FindIndex(s => s.path.EndsWith("/Chapter4.unity"));
        list.Insert(ch4 >= 0 ? ch4 + 1 : list.Count, new EditorBuildSettingsScene(Ch5ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[5장] {Ch5ScenePath} 생성 완료");
    }

    /// <summary>싸우지 않는 사람(모델 + 서 있는 동작만)</summary>
    static GameObject StandingCharacter(Transform parent, string name, string character, Vector3 pos, float yaw)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BaroqueCharacterSetup.CharacterRoot + $"/{character}/{character}_Model.prefab");
        if (!asset) { Debug.LogWarning($"[5장] {character} 모델 없음"); return null; }
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        model.name = name;
        model.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var anim = model.GetComponent<Animator>();
        if (anim) anim.runtimeAnimatorController = BaroqueCharacterSetup.EnsureIKController();
        return model;
    }

    // =====================================================================
    // 지하 주차장
    // =====================================================================

    static void Ch5Garage(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Transform triggers, Transform people, Built5 b)
    {
        var g = new GameObject("Garage").transform;
        g.SetParent(parent);
        var concrete = P.Lit("GarageConcrete", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.72f, 0.72f, 0.72f), 0.25f);
        var paint = P.Lit("GaragePaintGreen", null, new Color(0.25f, 0.45f, 0.35f), 0.4f);
        const float x0 = -18f, x1 = 18f, z0 = 0f, z1 = 40f, h = 3.6f, cz = (z0 + z1) / 2f;
        P.Box(g, "Floor", new Vector3(0f, -0.15f, cz), new Vector3(x1 - x0, 0.3f, z1 - z0), paint, new Vector2(4f, 4f));
        P.Box(g, "Ceiling", new Vector3(0f, h + 0.15f, cz), new Vector3(x1 - x0, 0.3f, z1 - z0), concrete, new Vector2(4f, 4f));
        P.Box(g, "Wall_W", new Vector3(x0, h / 2f, cz), new Vector3(0.4f, h, z1 - z0), concrete, new Vector2(4f, 4f));
        P.Box(g, "Wall_E", new Vector3(x1, h / 2f, cz), new Vector3(0.4f, h, z1 - z0), concrete, new Vector2(4f, 4f));
        P.Box(g, "Wall_N", new Vector3(0f, h / 2f, z1), new Vector3(x1 - x0, h, 0.4f), concrete, new Vector2(4f, 4f));
        P.Box(g, "Wall_S_W", new Vector3(-11f, h / 2f, z0), new Vector3(14f, h, 0.4f), concrete, new Vector2(4f, 4f));
        P.Box(g, "Wall_S_E", new Vector3(11f, h / 2f, z0), new Vector3(14f, h, 0.4f), concrete, new Vector2(4f, 4f));
        P.Box(g, "Wall_S_Gate", new Vector3(0f, h / 2f, z0 - 0.5f), new Vector3(8f, h, 0.4f), H["ShedDark"], Vector2.one); // 들어온 셔터(내려왔다)
        // 기둥 · 주차선 · 차: 양옆 벽에 차 한 줄씩, 가운데는 싸움판(기둥 넷과 차 둘이 엄폐물)
        foreach (float x in new[] { -9f, 9f })
            foreach (float z in new[] { 13f, 27f })
                P.Box(g, "Pillar", new Vector3(x, h / 2f, z), new Vector3(0.8f, h, 0.8f), concrete, new Vector2(2f, 2f));
        var rnd = new System.Random(5);
        string[] cars = { "Sedan", "Sedan", "PoliceCar", "BlackVan" };
        foreach (float z in new[] { 9f, 19f, 29f })
            foreach (float x in new[] { -15.5f, 15.5f })
                if (rnd.NextDouble() < 0.8) TryProp(cars[rnd.Next(cars.Length)], g, new Vector3(x, 0f, z), x < 0 ? 90f : -90f);
        foreach (var (x, z) in new[] { (-5f, 16f), (6f, 25f) }) TryProp("Sedan", g, new Vector3(x, 0f, z), rnd.Next(2) == 0 ? 0f : 180f);
        // 형광등
        foreach (float x in new[] { -9f, 9f })
            foreach (float z in new[] { 7f, 19f, 31f })
            {
                P.Box(g, "FluorPanel", new Vector3(x, h - 0.03f, z), new Vector3(1.2f, 0.04f, 0.3f), P.Unlit("GarageFluor", new Color(0.95f, 1f, 0.95f) * 2.4f), Vector2.one, collider: false);
                var l = P.AddLight(g, "Fluor", new Vector3(x, h - 0.3f, z), LightType.Point, new Color(0.88f, 0.95f, 0.9f), 7f, 13f);
                l.shadows = LightShadows.None;
            }
        // 7번의 테이프(서남쪽 구석): 상자 위 테이프 · 작은 전등
        Vector3 stash = new(-14f, 0f, 4f);
        TryProp("CrateStack", g, stash + new Vector3(-1.5f, 0f, 0f), 0f);
        TryProp("CassetteShelf", g, stash + new Vector3(-2.8f, 0f, 2f), 90f);
        P.AddLight(g, "StashLamp", stash + new Vector3(0f, 2.4f, 0.5f), LightType.Point, new Color(1f, 0.8f, 0.55f), 3f, 7f);
        StandingCharacter(g, "Seven", SubjectCharacter, stash + new Vector3(-0.6f, 0f, 1.2f), 90f);
        var shopGo = new GameObject("TapeShop_Seven");
        shopGo.transform.SetParent(triggers);
        shopGo.transform.position = stash + new Vector3(1f, 1f, 1.2f);
        var sc = shopGo.AddComponent<BoxCollider>();
        sc.size = new Vector3(2.6f, 2f, 2.6f);
        sc.isTrigger = true;
        var shop = shopGo.AddComponent<TapeShop>();
        shop.shopName = "7번의 테이프";
        shop.stock = new List<TapeShop.Item>
        {
            new() { tape = MusicTape("빗소리 재즈 (복사본)", "rain_jazz", new[] { 1.01f, 1.18f, 1.16f }, "젖은 트럼펫", "골목의 블루스", "마지막 손님") },
            new() { tape = MusicTape("희망의 속삭임", "seven_hope", new[] { 1.07f, 1.08f }, "숨겨 둔 테이프", "부스 밖의 아침") }, // 5장 전용(VARCO Sound) — 7번이 고른 곡
        };
        b.shop = shop;
        b.shopApproach = Trigger(triggers, "StashApproach", stash + new Vector3(2.5f, 1f, 1.5f), new Vector3(4f, 2f, 5f));
        b.shopSave = P.Marker(triggers, "StashSave", stash + new Vector3(2.2f, 0.05f, 1.2f), -90f);
        TapeSpot(g, triggers, "GarageTape", new Vector3(16.2f, 0f, 37f), 2, 1, box: true);

        // 정리반 · 특수경비
        var e1 = Cleaner(people, "Garage_A", new Vector3(-6f, 0.02f, 21f), 180f, false);
        var e2 = Cleaner(people, "Garage_B", new Vector3(5f, 0.02f, 18f), 180f, false);
        var e3 = Cleaner(people, "Garage_C", new Vector3(0f, 0.02f, 28f), 180f, false);
        var e4 = Cleaner(people, "Garage_Gun", new Vector3(-11f, 0.02f, 32f), 160f, true);
        var s1 = ShieldCop(people, "Garage_Shield", new Vector3(11f, 0.02f, 30f), 180f);
        s1.displayName = "특수경비";
        b.zoneGarage = Zone(triggers, "Zone_Garage", new Vector3(0f, 1.5f, 23f), new Vector3(32f, 3f, 26f), e1, e2, e3, e4, s1);
        // 화물 엘리베이터
        P.Box(g, "LiftDoor", new Vector3(0f, 1.3f, z1 - 0.25f), new Vector3(3f, 2.6f, 0.1f), H["Steel"], Vector2.one, collider: false);
        P.AddLight(g, "LiftLight", new Vector3(0f, 3f, z1 - 1f), LightType.Point, new Color(1f, 0.85f, 0.6f), 3f, 7f);
        b.liftUse = P.Interactable(g, "LiftUse", new Vector3(0f, 1f, z1 - 1.2f), new Vector3(3f, 2f, 2f), "화물 엘리베이터로 지하 3층에 내려간다");
    }

    // =====================================================================
    // 지하 3층 연구소
    // =====================================================================

    static void Ch5Lab(Transform parent, Transform triggers, Transform people, Built5 b)
    {
        var S = StationMaterials();
        S["Wall"] = P.Lit("LabWall", P.Tex("StationPaint", 256, PaintPixel), new Color(0.86f, 0.88f, 0.9f), 0.35f);
        S["WallLow"] = P.Lit("LabWallLow", null, new Color(0.55f, 0.6f, 0.62f), 0.4f);
        S["Floor"] = P.Lit("LabFloor", null, new Color(0.62f, 0.64f, 0.66f), 0.55f);
        var g = new GameObject("Lab").transform;
        g.SetParent(parent);
        // 좁고 답답하게(2026-10-13·14, "방이 쓸데없이 넓다"): 실험실 5.5×7m 독방 넷, 복도 폭 4m, 장비실 14×5m 둘, 전체 28×16m
        const float x0 = 300f, x1 = 328f, z0 = 12f, z1 = 28f, cz0 = 17f, cz1 = 21f, cz = (cz0 + cz1) / 2f;
        float[] labs = { 303f, 308.5f, 314f, 319.5f, 325f }; // 실험실 벽(그 사이 넷이 방)
        P.Box(g, "Floor", new Vector3((x0 + x1) / 2f, -0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), S["Floor"], new Vector2(2f, 2f));
        P.Box(g, "Ceiling", new Vector3((x0 + x1) / 2f, WallH + 0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), S["Ceiling"], new Vector2(2f, 2f));
        WallX(g, S, z0, x0, x1, 0f);
        WallX(g, S, z1, x0, x1, 0f);
        WallZ(g, S, x0, z0, z1, 0f);
        WallZ(g, S, x1, z0, z1, 0f);
        // 복도(z 17~21) 북쪽: 실험실 넷(문 열림, 양 끝은 막힌 창고) · 남쪽: 장비실 둘
        var doors = new List<(float, float)>();
        for (int i = 0; i + 1 < labs.Length; i++) doors.Add(((labs[i] + labs[i + 1]) / 2f, 1.6f));
        WallX(g, S, cz1, x0, x1, 0f, doors.ToArray());
        foreach (float x in labs) WallZ(g, S, x, cz1, z1, 0f);
        WallX(g, S, cz0, x0, x1, 0f, (307f, 1.6f), (321f, 1.6f));
        WallZ(g, S, 314f, z0, cz0, 0f);
        // 실험실 안: 끈 달린 의자 하나 — 사람은 실험체로 나온다
        for (int i = 0; i + 1 < labs.Length; i++)
        {
            float x = (labs[i] + labs[i + 1]) / 2f;
            TryProp("OfficeChair", g, new Vector3(x + 1.2f, 0f, z1 - 1.4f), 180f);
            P.Box(g, "Restraint", new Vector3(x + 1.2f, 0.6f, z1 - 1f), new Vector3(0.7f, 0.05f, 0.05f), S["Steel"], Vector2.one, collider: false);
        }
        // 장비실: 릴 녹음기 · 콘솔(서쪽 방) · 테이프 선반(동쪽 방)
        foreach (float x in new[] { 302.2f, 305.2f, 308.2f }) TryProp("ReelToReel", g, new Vector3(x, 0f, z0 + 0.6f), 0f);
        TryProp("MixingConsole", g, new Vector3(311.6f, 0f, z0 + 2.4f), 0f);
        foreach (float x in new[] { 317f, 320.5f, 324f }) TryProp("CassetteShelf", g, new Vector3(x, 0f, z0 + 0.4f), 0f);
        TapeSpot(g, triggers, "LabTape", new Vector3(326.6f, 0f, z0 + 1.2f), 2, 1, box: true);
        // 형광등
        foreach (float x in new[] { 305f, 314f, 323f })
            foreach (float z in new[] { cz, 24.5f, 14.5f })
            {
                P.Box(g, "FluorPanel", new Vector3(x, WallH - 0.03f, z), new Vector3(1.2f, 0.04f, 0.3f), S["Fluor"], Vector2.one, collider: false);
                var l = P.AddLight(g, "Fluor", new Vector3(x, WallH - 0.3f, z), LightType.Point, new Color(0.9f, 0.95f, 1f), 4.5f, 9f);
                l.shadows = LightShadows.None;
            }
        // 실험체 넷(방마다 하나)
        var subs = new List<EnemyAI>();
        for (int i = 0; i + 1 < labs.Length; i++)
        {
            float x = (labs[i] + labs[i + 1]) / 2f;
            var e = P.CreateThug(people, $"Lab_Subject_{i}", new Vector3(x, 0.02f, 24f), 180f, SubjectCharacter);
            SetCombat(e, false);
            e.displayName = "실험체";
            e.damage = 10;
            e.windup = 0.42f;
            e.recover = 0.5f;
            e.attackCooldown = 1.0f;
            e.heavyEvery = 0;
            e.GetComponent<Health>().maxHealth = 95;
            e.GetComponent<UnityEngine.AI.NavMeshAgent>().speed = 7.4f;
            subs.Add(e);
        }
        b.zoneLab = Zone(triggers, "Zone_Lab", new Vector3((x0 + 4f + x1) / 2f, 1.5f, cz), new Vector3(x1 - x0 - 4f, 3f, cz1 - cz0), subs.ToArray()); // 입구 4m는 비운다(들어서자마자 깨지 않게)
        b.labEntry = P.Marker(triggers, "LabEntry", new Vector3(301.6f, 0.05f, cz), 90f);
        // 동쪽 끝 격벽
        P.Box(g, "BlastDoor", new Vector3(x1 - 0.15f, 1.8f, cz), new Vector3(0.12f, 3.6f, 3.6f), P.Lit("BlastDoorPaint", null, new Color(0.55f, 0.48f, 0.15f), 0.4f, metallic: 0.5f), Vector2.one, collider: false);
        P.AddLight(g, "BlastLamp", new Vector3(x1 - 1.2f, 3.6f, cz), LightType.Point, new Color(1f, 0.3f, 0.15f), 3f, 7f);
        b.hallDoor = P.Interactable(g, "BlastDoorUse", new Vector3(x1 - 1.4f, 1f, cz), new Vector3(1.8f, 2f, 3.6f), "격벽을 연다");
    }

    // =====================================================================
    // 마스터 테이프 홀
    // =====================================================================

    static void Ch5Hall(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Transform triggers, Transform people, Built5 b)
    {
        var g = new GameObject("MasterHall").transform;
        g.SetParent(parent);
        const float x0 = 406f, x1 = 434f, z0 = 0f, z1 = 30f, h = 8f, zn = z1 - 4f; // zn: 관람석 아래 벽(걸어 다니는 홀의 북쪽 끝)
        var wall = P.Lit("HallWall", null, new Color(0.14f, 0.15f, 0.18f), 0.4f, metallic: 0.2f);
        var floor = P.Lit("HallFloor", null, new Color(0.22f, 0.23f, 0.26f), 0.7f);
        P.Box(g, "Floor", new Vector3(420f, -0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), floor, new Vector2(2f, 2f));
        P.Box(g, "Ceiling", new Vector3(420f, h + 0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), wall, new Vector2(4f, 4f));
        P.Box(g, "Wall_W", new Vector3(x0, h / 2f, (z0 + z1) / 2f), new Vector3(0.4f, h, z1 - z0), wall, new Vector2(4f, 4f));
        P.Box(g, "Wall_E", new Vector3(x1, h / 2f, (z0 + z1) / 2f), new Vector3(0.4f, h, z1 - z0), wall, new Vector2(4f, 4f));
        P.Box(g, "Wall_S", new Vector3(420f, h / 2f, z0), new Vector3(x1 - x0, h, 0.4f), wall, new Vector2(4f, 4f));
        // 북쪽: 아래는 벽, 높이 4m부터 유리 관람석(뒤로 4m 깊이)
        P.Box(g, "Wall_N_Low", new Vector3(420f, 2.25f, zn), new Vector3(x1 - x0, 4.5f, 0.4f), wall, new Vector2(4f, 4f));
        P.Box(g, "GalleryFloor", new Vector3(420f, 4.4f, zn + 2f), new Vector3(x1 - x0, 0.2f, 4f), wall, Vector2.one);
        P.Box(g, "GalleryBack", new Vector3(420f, 6.25f, z1), new Vector3(x1 - x0, 3.5f, 0.4f), P.Lit("GalleryPanel", null, new Color(0.45f, 0.36f, 0.26f), 0.5f), Vector2.one);
        P.Box(g, "GallerySill", new Vector3(420f, 4.68f, zn), new Vector3(x1 - x0, 0.36f, 0.3f), wall, Vector2.one); // 낮은 턱: 아래에서 올려다봐도 윗선이 보이게
        InvisibleWall(g, "GalleryGlass", new Vector3(420f, 6.4f, zn), new Vector3(x1 - x0, 3.2f, 0.2f));
        foreach (float x in new[] { 409f, 414f, 426f, 431f }) // 가운데(윗선이 서는 자리)는 비운다
            P.Box(g, "GalleryMullion", new Vector3(x, 6.4f, zn), new Vector3(0.1f, 3.2f, 0.12f), H["Steel"], Vector2.one, collider: false);
        P.AddLight(g, "GalleryLight", new Vector3(420f, 6.9f, zn - 0.4f), LightType.Point, new Color(1f, 0.85f, 0.65f), 3f, 8f); // 유리 앞 위: 얼굴을 비춘다(뒤에 두면 실루엣만)
        P.AddLight(g, "GalleryBack", new Vector3(420f, 7.4f, z1 - 0.6f), LightType.Point, new Color(1f, 0.8f, 0.6f), 0.6f, 6f);
        // 윗선 셋(유리 너머, 홀을 내려다본다)
        StandingCharacter(g, "Chairman", "Chairman", new Vector3(420f, 4.5f, zn + 1.6f), 180f);
        StandingCharacter(g, "PoliceChief", "Officer", new Vector3(416.5f, 4.5f, zn + 2f), 170f);
        StandingCharacter(g, "Lawmaker", "Lawmaker", new Vector3(423.6f, 4.5f, zn + 1.9f), 190f);
        var gs = P.Marker(triggers, "GalleryShot", new Vector3(420f, 3.4f, zn - 6.5f), 0f); // 받침대 릴 녹음기 뒤(북쪽): 앞에 두면 릴이 화면 아래를 가린다
        gs.rotation = Quaternion.LookRotation(new Vector3(420f, 5.7f, zn + 2f) - gs.position);
        b.galleryShot = gs;

        // 가운데 받침대: 거대한 릴 녹음기 + 마스터 테이프(작은 카세트 하나, 차가운 빛)
        P.Box(g, "Pedestal", new Vector3(420f, 0.3f, 17f), new Vector3(5f, 0.6f, 4f), H["Steel"], Vector2.one);
        var reel = TryProp("ReelToReel", g, new Vector3(420f, 0.6f, 18f), 180f);
        if (reel) reel.transform.localScale = Vector3.one * 1.8f;
        P.Box(g, "TapeStand", new Vector3(420f, 1.0f, 15.6f), new Vector3(0.6f, 0.8f, 0.4f), H["Steel"], Vector2.one);
        var tape = TryProp("CassetteTape", g, new Vector3(420f, 1.42f, 15.6f), 180f);
        if (tape) { tape.transform.localScale = Vector3.one * 1.6f; b.masterTape = tape.transform; }
        else b.masterTape = P.Marker(g, "MasterTape", new Vector3(420f, 1.42f, 15.6f), 0f);
        P.AddLight(g, "MasterSpot", new Vector3(420f, h - 0.4f, 16f), LightType.Spot, new Color(0.75f, 0.85f, 1f), 60f, 14f, 40f, Quaternion.Euler(90f, 0f, 0f));
        b.masterUse = P.Interactable(g, "MasterUse", new Vector3(420f, 1f, 14.6f), new Vector3(2.4f, 2f, 1.6f), "마스터 테이프를 꺼낸다");
        var ms = P.Marker(triggers, "MasterShot", new Vector3(418.2f, 1.9f, 13.6f), 0f);
        ms.rotation = Quaternion.LookRotation(new Vector3(420f, 1.4f, 15.6f) - ms.position);
        b.masterShot = ms;
        // 방송 콘솔(서북쪽, 앉는 쪽이 남쪽)
        TryProp("MixingConsole", g, new Vector3(410f, 0f, 22.5f), 180f);
        P.AddLight(g, "ConsoleLamp", new Vector3(410f, 3f, 21f), LightType.Point, new Color(0.6f, 0.9f, 1f), 3f, 6f);
        b.consoleUse = P.Interactable(g, "ConsoleUse", new Vector3(410f, 1f, 20.9f), new Vector3(2.6f, 2f, 1.8f), "대니의 녹음을 생방송에 건다");
        // 홀 조명(가장자리 기둥등) · 바닥 띠
        foreach (float x in new[] { 408f, 432f })
            foreach (float z in new[] { 6f, 14f, 22f })
                P.AddLight(g, "HallSide", new Vector3(x, 5f, z), LightType.Point, new Color(0.55f, 0.65f, 0.9f), 9f, 16f);
        P.AddLight(g, "HallFill", new Vector3(420f, 6f, 8f), LightType.Point, new Color(0.8f, 0.85f, 1f), 12f, 20f);
        P.AddLight(g, "HallFill2", new Vector3(413f, 6f, 19f), LightType.Point, new Color(0.8f, 0.85f, 1f), 8f, 16f);
        P.AddLight(g, "HallFill3", new Vector3(427f, 6f, 19f), LightType.Point, new Color(0.8f, 0.85f, 1f), 8f, 16f);

        b.hallEntry = P.Marker(triggers, "HallEntry", new Vector3(420f, 0.05f, 3f), 0f);
        b.hallTrigger = Trigger(triggers, "HallCentre", new Vector3(420f, 1.5f, 7f), new Vector3(x1 - x0 - 2f, 3f, 5f));

        // 실험체 0번(보스): 다른 실험체보다 한 뼘 크고 빠르다
        var zero = P.CreateThug(people, "Subject_Zero", new Vector3(420f, 0.02f, 12.5f), 180f, SubjectCharacter);
        SetCombat(zero, false);
        zero.displayName = "실험체 0번";
        zero.isBoss = true;
        zero.transform.localScale = Vector3.one * 1.18f;
        zero.GetComponent<Health>().maxHealth = 420;
        zero.damage = 13;
        zero.heavyEvery = 2;
        zero.heavyDamage = 26;
        zero.windup = 0.4f;
        zero.heavyWindup = 0.8f;
        zero.attackCooldown = 0.95f;
        zero.recover = 0.4f;
        zero.staggerable = false;
        zero.executeEvery = 3; // 처형기(되감으면 보인다)
        zero.executeDamage = 50;
        zero.GetComponent<UnityEngine.AI.NavMeshAgent>().speed = 7.6f;
        b.zero = zero;
        var g1 = ShieldCop(people, "Hall_Shield_W", new Vector3(409f, 0.02f, 12f), 90f);
        var g2 = ShieldCop(people, "Hall_Shield_E", new Vector3(431f, 0.02f, 12f), -90f);
        g1.displayName = g2.displayName = "특수경비";
        b.guards = new[] { g1.gameObject, g2.gameObject };
    }
}
