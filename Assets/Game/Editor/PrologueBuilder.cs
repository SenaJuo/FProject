using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 프롤로그 씬(비 오는 밤의 도시 거리 + 주인공 집 + 친구 집)을 처음부터 만든다.
/// 메뉴: Game/Build Prologue  (스크립트에서는 PrologueBuilder.Build())
///
/// 배치(미터):  거리는 x축을 따라 -20~160. 차도 z -4~4, 인도 z ±4~8, 건물 벽면 z = ±8.
///   주인공 건물 x -20~14 (현관 x = -2) / 친구 건물 x 90~114 (현관 x = 102) / 막다른 골목 x 136~144 (북쪽으로 z 30까지)
///   실내는 거리와 겹치지 않게 북쪽 멀리(z 65~74)에 따로 두고, 문을 열면 화면 전환으로 오간다.
/// </summary>
public static class PrologueBuilder
{
    const string ScenePath = "Assets/Game/Scenes/Prologue.unity";
    internal const string ArtRoot = "Assets/Game/Art/Prologue";
    internal const string PropPrefabs = BaroquePropSetup.PrefabRoot;
    const string PlayerPrefabPath = "Assets/Game/Prefabs/Player.prefab";

    internal static readonly Dictionary<string, Mesh> boxMeshes = new();
    internal static Transform root;

    [MenuItem("Game/Build Prologue")]
    static void BuildMenu()
    {
        if (EditorUtility.DisplayDialog("프롤로그 만들기", $"{ScenePath}를 새로 만듭니다(기존 내용은 덮어씀).", "만들기", "취소")) Build();
    }

    public static void Build()
    {
        boxMeshes.Clear();
        EnsureFolder(ArtRoot);
        EnsureFolder(ArtRoot + "/Textures");
        EnsureFolder(ArtRoot + "/Materials");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        root = new GameObject("Prologue").transform;
        var M = CreateMaterials();

        BuildStreet(M);
        var home = BuildHome(M);
        var friend = BuildFriendRoom(M);
        BuildAlleyAndEnds(M);

        // ---------- 플레이어 ----------
        var player = CreatePlayer(home.spawn, out var ik, out var ragdoll, out var walkman, out var aimPoint);

        // ---------- 조직원 ----------
        var thugParent = new GameObject("Thugs").transform;
        var roomThugs = new[]
        {
            CreateThug(thugParent, "Thug_Room_A", new Vector3(99.2f, 0f, 71.6f), 210f),
            CreateThug(thugParent, "Thug_Room_B", new Vector3(105.4f, 0f, 70.8f), 140f),
        };

        // ---------- 쓰러져 있는 친구 ----------
        var danny = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BaroqueCharacterSetup.CharacterRoot + "/Friend/Friend_Model.prefab"), root);
        danny.name = "Danny_Body";
        danny.transform.SetPositionAndRotation(new Vector3(101.2f, 0.02f, 70.4f), Quaternion.Euler(0f, 160f, 0f));
        var dannyRagdoll = danny.AddComponent<Ragdoll>();

        // ---------- 카메라 ----------
        var mainCam = Object.FindAnyObjectByType<Camera>();
        mainCam.farClipPlane = 400f;
        mainCam.nearClipPlane = 0.05f;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
        mainCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var brain = mainCam.gameObject.AddComponent<CinemachineBrain>();

        var cams = new GameObject("Cameras").transform;
        var gameplay = VCam(cams, "GameplayCamera", home.spawn.position + new Vector3(0, 2f, -3f), home.spawn.position + Vector3.up * 1.5f, 55f);
        var rig = gameplay.gameObject.AddComponent<ThirdPersonCamera>();
        rig.target = player.transform;
        rig.distance = 4.8f;

        Vector3 hs = home.spawn.position, hf = home.spawn.forward, hr = home.spawn.right;
        var opening = new[]
        {
            VCam(cams, "Shot_Open_TV", home.tv + new Vector3(-0.85f, 0.12f, 0.25f), home.tv + new Vector3(0f, 0.05f, 0f), 38f),
            VCam(cams, "Shot_Open_Desk", home.desk + new Vector3(0.95f, 0.5f, -0.35f), home.desk, 38f),
            VCam(cams, "Shot_Open_Ray", hs + hf * 2.1f - hr * 0.5f + Vector3.up * 1.55f, hs + Vector3.up * 1.35f, 40f),
        };
        Vector3 fe = friend.entry.position;
        var reveal = new[]
        {
            VCam(cams, "Shot_Reveal_OverShoulder", fe + new Vector3(-0.45f, 1.72f, -0.75f), new Vector3(101.6f, 0.9f, 70.4f), 46f),
            VCam(cams, "Shot_Reveal_Body", new Vector3(101.9f, 0.95f, 68.3f), new Vector3(101.2f, 0.25f, 70.4f), 40f),
            VCam(cams, "Shot_Reveal_Thugs", new Vector3(102.2f, 1.2f, 67.4f), new Vector3(102.3f, 1.5f, 71.2f), 52f),
            VCam(cams, "Shot_Reveal_Face", fe + new Vector3(0.3f, 1.62f, 1.15f), fe + new Vector3(0f, 1.6f, 0f), 32f),
        };
        var dynA = VCam(cams, "Shot_Dynamic_A", Vector3.zero, Vector3.forward, 40f);
        var dynB = VCam(cams, "Shot_Dynamic_B", Vector3.zero, Vector3.forward, 40f);

        // ---------- 시스템 ----------
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
        rainAudio.volume = 0.45f;
        var rain = CreateRain(player.transform, M["RainStreak"]);

        var muzzle = AddLight(systems.transform, "MuzzleFlash", Vector3.zero, LightType.Point, new Color(1f, 0.8f, 0.5f), 12f, 7f);

        var director = systems.AddComponent<PrologueDirector>();
        director.player = player;
        director.playerIK = ik;
        director.playerRagdoll = ragdoll;
        director.pocketWalkman = walkman;
        director.deskWalkman = home.deskWalkman;
        director.deskLetter = home.deskLetter;
        director.playerAimPoint = aimPoint;
        director.brain = brain;
        director.gameplayCamera = gameplay;
        director.gameplayRig = rig;
        director.openingShots = opening;
        director.revealShots = reveal;
        director.dynamicA = dynA;
        director.dynamicB = dynB;
        director.homeSpawn = home.spawn;
        director.homeDoor = home.door;
        director.streetStart = home.streetStart;
        director.friendDoor = friend.outsideDoor;
        director.friendRoomEntry = friend.entry;
        director.friendWindow = friend.window;
        director.friendOutside = friend.outsideSpawn;
        director.letterSpot = friend.letterSpot;
        director.indoorZ = 45f;
        director.roomThugs = roomThugs;
        director.dannyBody = dannyRagdoll;
        director.rewindEffect = rewind;
        director.rainAudio = rainAudio;
        director.rain = rain;
        director.muzzleFlash = muzzle;

        SetupEnvironment();
        BakeNavMesh();
        EnforceMaterialKeywords();

        EditorSceneManager.SaveScene(scene, ScenePath);
        // 게임은 프롤로그부터 시작한다(0번), Stage01은 그대로 남긴다
        var buildScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[프롤로그] {ScenePath} 생성 완료");
    }

    // =====================================================================
    // 거리
    // =====================================================================

    // 주택가: 차도(z ±4) → 인도(2.5m) → 앞마당 잔디 → 집(앞면 z ±12). 북쪽 집은 거리를 보도록 180° 돌린다.
    internal const float HouseLine = 12f;     // 집 앞면이 놓이는 z(북쪽 +, 남쪽 -)
    internal const float BackFence = 26f;     // 뒷마당 담장
    internal const float Ground = 0.15f;      // 인도·잔디 윗면 높이

    // 집은 HouseBuilder로 직접 짓는다(이미지→3D 생성 집은 창틀이 녹고 텍스처가 번져 쓰지 않는다).
    // 레이의 집은 북쪽 x -4 방갈로, 대니의 집은 북쪽 x 103 남색 2층집. x 124.8~133.2는 막다른 뒷골목.
    // 붙은 타운하우스는 폭 6.4 + 0.2 간격(6.6)으로 두어 코니스가 딱 맞닿게 한다.
    internal static HouseBuilder.Spec Suburban(string wall, string door, string shutter, int seed, bool chimney = false, float doorOffset = 0f) => new HouseBuilder.Spec
    {
        style = HouseBuilder.Style.Suburban, width = 10f, depth = 9f, floors = 2, wall = HouseBuilder.Wall(wall), door = HouseBuilder.Door(door),
        shutter = shutter == null ? null : HouseBuilder.Shutter(shutter), porch = true, chimney = chimney, doorOffset = doorOffset, seed = seed,
    };
    internal static HouseBuilder.Spec Bungalow(string wall, string door, string shutter, int seed, float doorOffset = 0f, float width = 12f) => new HouseBuilder.Spec
    {
        style = HouseBuilder.Style.Bungalow, width = width, depth = 9f, floors = 1, wall = HouseBuilder.Wall(wall), door = HouseBuilder.Door(door),
        shutter = shutter == null ? null : HouseBuilder.Shutter(shutter), porch = true, chimney = true, doorOffset = doorOffset, seed = seed,
    };
    internal static HouseBuilder.Spec Townhouse(string wall, string door, int seed) => new HouseBuilder.Spec
    {
        style = HouseBuilder.Style.Townhouse, width = 6.4f, depth = 10f, floors = 3, wall = HouseBuilder.Wall(wall), door = HouseBuilder.Door(door), seed = seed,
    };

    internal static (float x, HouseBuilder.Spec spec)[] NorthHouses() => new[]
    {
        (-4f, Bungalow("Beige", "Green", "White", 1)),                 // 레이의 집
        (10f, Suburban("White", "Red", "Black", 2, chimney: true)),
        (21f, Townhouse("Brick", "Black", 3)), (27.6f, Townhouse("Butter", "Navy", 4)),
        (38f, Suburban("Brick", "White", null, 5, doorOffset: 1.6f)),
        (50f, Bungalow("Sage", "Red", "White", 6, doorOffset: -1.5f)),
        (63f, Suburban("BlueGrey", "White", "Navy", 7, chimney: true)),
        (75f, Townhouse("Brick", "Green", 8)), (81.6f, Townhouse("White", "Black", 9)),
        (91f, Suburban("Beige", "Navy", "Green", 10, doorOffset: -1.6f)),
        (DannyHouseX, Suburban("Navy", "White", "White", 11)),         // 대니의 집
        (116f, Suburban("White", "Black", "Black", 12, chimney: true)),
        (136.8f, Townhouse("Butter", "Red", 13)), (143.4f, Townhouse("Brick", "Navy", 14)),
    };
    internal static (float x, HouseBuilder.Spec spec)[] SouthHouses() => new[]
    {
        (-2f, Suburban("Brick", "Black", null, 21, chimney: true)),
        (7f, Townhouse("White", "Red", 22)), (13.6f, Townhouse("Brick", "Green", 23)),
        (26f, Suburban("Sage", "White", "White", 24, doorOffset: 1.6f)),
        (38.5f, Bungalow("Butter", "Navy", "White", 25, width: 11f)),       // 옆집과 처마가 닿지 않게 11m, 동쪽 집과 1m 띄운다
        (50f, Suburban("BlueGrey", "Red", "White", 26)),
        (61f, Suburban("Brick", "Green", null, 27, chimney: true, doorOffset: -1.6f)),
        (74f, Suburban("White", "Navy", "Navy", 28)),
        (86f, Townhouse("Butter", "Black", 29)), (92.6f, Townhouse("Brick", "Red", 30)),
        (102f, Bungalow("BlueGrey", "White", "White", 31, doorOffset: 1.5f, width: 11f)),
        (114f, Suburban("Brick", "White", null, 32, doorOffset: 1.6f)),
        (128f, Suburban("Beige", "Green", "Green", 33, chimney: true)),
        (139f, Townhouse("White", "Navy", 34)),
    };
    public const float RayHouseX = -4f, DannyHouseX = 103f;
    /// <summary>1장: 대니 집 뒷담장 쪽문의 x, 공원의 서쪽 끝 x</summary>
    public const float ParkGateX = 106.2f, ParkWestX = 55f;

    internal static void BuildStreet(Dictionary<string, Material> M, bool chapterOne = false)
    {
        var street = Group("Street");
        // 땅: 차도 판 하나 위에 인도·연석, 그 바깥은 잔디(앞마당·뒷마당)
        Box(street, "Road", new Vector3(65f, -0.25f, 0f), new Vector3(180f, 0.5f, 8f), M["Asphalt"], new Vector2(4f, 4f));
        Box(street, "Sidewalk_N", new Vector3(65f, 0.075f, 5.25f), new Vector3(180f, 0.15f, 2.5f), M["Sidewalk"], new Vector2(2f, 2f));
        Box(street, "Sidewalk_S", new Vector3(65f, 0.075f, -5.25f), new Vector3(180f, 0.15f, 2.5f), M["Sidewalk"], new Vector2(2f, 2f));
        Box(street, "Curb_N", new Vector3(65f, 0.08f, 4.05f), new Vector3(180f, 0.16f, 0.2f), M["Curb"], new Vector2(1f, 1f), collider: false);
        Box(street, "Curb_S", new Vector3(65f, 0.08f, -4.05f), new Vector3(180f, 0.16f, 0.2f), M["Curb"], new Vector2(1f, 1f), collider: false);
        // 잔디는 뒷마당 담장(z ±26) 안쪽까지만. 담장 바깥(나무가 서는 땅)은 따로 두어 걸어서 이어지지 않게 한다
        Box(street, "Lawn_N", new Vector3(61.5f, 0.075f, 16.25f), new Vector3(187f, 0.15f, 19.5f), M["Grass"], new Vector2(3f, 3f));
        Box(street, "Lawn_S", new Vector3(61.5f, 0.075f, -16.25f), new Vector3(187f, 0.15f, 19.5f), M["Grass"], new Vector2(3f, 3f));
        if (chapterOne) // 1장: 대니 집 뒤쪽은 공원 땅이 덮는다(같은 높이의 판이 겹치면 깜박인다)
            Box(street, "Backdrop_N", new Vector3((-32f + ParkWestX) / 2f, 0.075f, 34.1f), new Vector3(ParkWestX + 32f, 0.15f, 16f), M["Grass"], new Vector2(3f, 3f));
        else Box(street, "Backdrop_N", new Vector3(61.5f, 0.075f, 34.1f), new Vector3(187f, 0.15f, 16f), M["Grass"], new Vector2(3f, 3f));
        Box(street, "Backdrop_S", new Vector3(61.5f, 0.075f, -34.1f), new Vector3(187f, 0.15f, 16f), M["Grass"], new Vector2(3f, 3f));
        for (float x = -14f; x < 146f; x += 6f)
            Box(street, "LaneDash", new Vector3(x, 0.004f, 0f), new Vector3(3f, 0.01f, 0.12f), M["Paint"], Vector2.one, collider: false);

        // 집: 집마다 현관 보도·포치 조명·우편함. 집과 집 사이는 나무 담장으로 막아 뒷마당으로 빠지지 않게 한다
        var houseMats = HouseBuilder.SharedMaterials();
        PlaceRow(street, NorthHouses(), true, houseMats, M);
        PlaceRow(street, SouthHouses(), false, houseMats, M);
        // 대니 집 뒷벽 등: 뒷창문으로 빠져나와 숨는 뒷마당(편지 뒷면을 읽고 처음 되감기를 써 보는 곳)이 캄캄하지 않게. 1층 뒷창 둘 사이
        HouseBuilder.WallLamp(street, "BackLight_Danny", new Vector3(DannyHouseX - 1.85f, Ground + 0.6f + 2.0f, HouseLine + 9f), 180f, houseMats);
        if (chapterOne)
        {
            // 1장: 대니 집 뒷마당에서 공원으로 나가는 쪽문(열려 있다). 뒷담장을 쪽문 양옆으로 나눈다
            WoodFence(street, new Vector3(-32f, Ground, BackFence), new Vector3(ParkGateX - 1.2f, Ground, BackFence), 2f, M);
            WoodFence(street, new Vector3(ParkGateX + 1.2f, Ground, BackFence), new Vector3(124.6f, Ground, BackFence), 2f, M);
        }
        else WoodFence(street, new Vector3(-32f, Ground, BackFence), new Vector3(124.6f, Ground, BackFence), 2f, M);
        WoodFence(street, new Vector3(133.4f, Ground, BackFence), new Vector3(150f, Ground, BackFence), 2f, M);
        WoodFence(street, new Vector3(-32f, Ground, -BackFence), new Vector3(150f, Ground, -BackFence), 2f, M);
        // 담장 너머는 나무가 줄지어 서 있다(어두운 하늘을 가리는 실루엣)
        for (float x = -14f; x < 144f; x += 9f)
        {
            if (!chapterOne || x < ParkWestX - 4f) // 1장: 담장 너머가 공원이다(가로수 줄 대신 공원을 짓는다)
                Prop("StreetTree", street, new Vector3(x, Ground, BackFence + 3f), x * 37f % 360f);
            Prop("StreetTree", street, new Vector3(x + 4.5f, Ground, -BackFence - 3f), x * 53f % 360f);
        }

        // 서쪽 끝: 길 끝을 마주 보는 집(시야를 닫는다)
        // 서쪽 끝 집: 앞면이 +x(거리)를 본다. 폭 13m로 양옆을 담장 줄(z ±6.5)에 맞추고, 담장 끝(x -16)과 집 앞 모서리를 잇는다
        var westSpec = Suburban("Brick", "Red", null, 40, chimney: true);
        westSpec.width = 13f;
        HouseBuilder.Build(street, "House_West", new Vector3(-19.5f, Ground, 0f), -90f, westSpec, houseMats, out _);
        WoodFence(street, new Vector3(-16f, Ground, 6.5f), new Vector3(-19.5f, Ground, 6.5f), 2f, M);
        WoodFence(street, new Vector3(-16f, Ground, -6.5f), new Vector3(-19.5f, Ground, -6.5f), 2f, M);
        Box(street, "WestLawn", new Vector3(-24f, 0.075f, 0f), new Vector3(16f, 0.15f, 52f), M["Grass"], new Vector2(3f, 3f));
        WoodFence(street, new Vector3(-32f, Ground, -BackFence), new Vector3(-32f, Ground, BackFence), 2f, M); // 서쪽 끝을 닫는다
        WoodFence(street, new Vector3(-16f, Ground, 6.5f), new Vector3(-16f, Ground, BackFence), 2f, M);
        WoodFence(street, new Vector3(-16f, Ground, -6.5f), new Vector3(-16f, Ground, -BackFence), 2f, M);

        // 가로등(주택가라 간격을 넓게, 양쪽 번갈아)
        foreach (float x in new[] { 4f, 30f, 56f, 82f, 108f, 134f }) Lamp(street, x, true, M["LampGlow"]);
        foreach (float x in new[] { 17f, 43f, 69f, 95f, 121f }) Lamp(street, x, false, M["LampGlow"]);

        // 길가 소품
        Prop("Sedan", street, new Vector3(36f, 0f, 2.6f), -90f);
        Prop("Sedan", street, new Vector3(70f, 0f, 2.6f), -90f);
        Prop("Sedan", street, new Vector3(18f, 0f, -2.6f), 90f);
        Prop("Sedan", street, new Vector3(64f, 0f, -2.6f), 90f);
        Prop("Sedan", street, new Vector3(112f, 0f, -2.6f), 90f);
        Prop("FireHydrant", street, new Vector3(24.5f, Ground, 4.55f), 0f);
        Prop("FireHydrant", street, new Vector3(80.5f, Ground, -4.55f), 180f);
        Prop("RecyclingBins", street, new Vector3(25f, Ground, 6.2f), 180f);
        Prop("RecyclingBins", street, new Vector3(89f, Ground, -6.2f), 0f);
        Prop("EScooter", street, new Vector3(78.6f, Ground, 6.0f), 100f);
        Prop("ConstructionBarrier", street, new Vector3(126f, 0f, -2.8f), 10f);
    }

    /// <summary>한쪽 줄의 집들: 집(포치 조명 포함) + 현관 보도 + 우편함 + (번갈아) 가로수·덤불 + 집 사이 담장</summary>
    internal static void PlaceRow(Transform parent, (float x, HouseBuilder.Spec spec)[] row, bool north, HouseBuilder.Materials houseMats, Dictionary<string, Material> M)
    {
        float side = north ? 1f : -1f;
        float yaw = north ? 0f : 180f;   // 집 로컬 앞면은 -z: 북쪽 줄은 그대로, 남쪽 줄은 돌려서 거리를 보게 한다
        var spans = new List<(float x0, float x1)>();
        for (int i = 0; i < row.Length; i++)
        {
            var (x, spec) = row[i];
            HouseBuilder.Build(parent, $"House_{(north ? "N" : "S")}_{x:0}", new Vector3(x, Ground, side * HouseLine), yaw, spec, houseMats, out Vector3 doorFront);
            spans.Add((x - spec.width / 2f, x + spec.width / 2f));

            // 현관 보도(인도 → 현관 계단 아래까지), 우편함은 보도 옆 집 가운데 쪽
            float doorX = doorFront.x;
            Box(parent, "Walkway", new Vector3(doorX, Ground + 0.01f, side * ((6.5f + HouseLine) / 2f)), new Vector3(1.3f, 0.02f, HouseLine - 6.5f), M["Sidewalk"], new Vector2(1f, 1f), collider: false);
            float mailX = doorX + (doorX > x + 0.1f ? -1.25f : 1.25f);
            Prop("Mailbox", parent, new Vector3(mailX, Ground, side * 6.9f), north ? 180f : 0f);
            if (spec.style == HouseBuilder.Style.Townhouse) continue; // 타운하우스는 앞마당이 좁아 나무·덤불을 두지 않는다
            // 앞마당에는 나무를 두지 않는다: 수관에는 충돌체가 없어, 인도에서 거리 쪽을 보면 뒤따르는 카메라가 수관 속으로 들어가
            // 화면이 새카매진다(카메라 위치 18200곳 중 265곳). 낮은 덤불은 충돌체가 있어 카메라가 앞에서 멈춘다.
            Prop("Hedge", parent, new Vector3(doorX - 2.3f, Ground, side * 7.4f), 0f);
            Prop("Hedge", parent, new Vector3(doorX + 2.3f, Ground, side * 7.4f), 0f);
        }
        // 집 사이 틈은 집 앞면보다 1.5m 뒤에서 옆집과 옆집을 잇는 담장으로 가로막는다(뒷마당으로 못 들어가게).
        // 붙어 있는 타운하우스 사이는 건너뛰고, 막다른 뒷골목(x 124.8~133.2) 입구는 열어 둔다.
        spans.Sort((a, b) => a.x0.CompareTo(b.x0));
        float z = side * (HouseLine + 1.5f);
        void Cross(float x0, float x1)
        {
            if (x1 - x0 > 0.3f) WoodFence(parent, new Vector3(x0, Ground, z), new Vector3(x1, Ground, z), 1.9f, M);
        }
        Cross(-16f, spans[0].x0);                // 서쪽 끝 담장 → 첫 집
        Cross(spans[^1].x1, 149f);               // 마지막 집 → 동쪽 끝 철망
        for (int i = 0; i + 1 < spans.Count; i++)
        {
            float gap0 = spans[i].x1, gap1 = spans[i + 1].x0;
            if (gap1 - gap0 < 0.6f) continue;
            if (north && gap0 < 124.8f && gap1 > 133.2f) { Cross(gap0, 124.8f); Cross(133.2f, gap1); continue; }
            Cross(gap0, gap1);
        }
    }

    /// <summary>나무 판자 담장(주택가 경계). 판자 질감은 상자 하나에 줄무늬 텍스처로 낸다</summary>
    internal static void WoodFence(Transform parent, Vector3 a, Vector3 b, float height, Dictionary<string, Material> M)
    {
        Vector3 dir = b - a;
        float length = dir.magnitude;
        var f = Box(parent, "WoodFence", (a + b) / 2f + Vector3.up * height / 2f, new Vector3(length, height, 0.08f), M["WoodFence"], new Vector2(1.2f, height));
        f.transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir.normalized));
    }

    internal static void Lamp(Transform parent, float x, bool north, Material glow)
    {
        // 프리팹은 기둥과 등 머리가 x축 양 끝에 있다(기둥 -x, 머리 +x).
        // 북쪽 가로등은 머리가 -z(차도), 남쪽은 +z를 보게 돌리고, 기둥이 연석(z ±4.7)에 오도록 루트를 1m 안쪽에 둔다.
        float yaw = north ? 90f : -90f;
        var lamp = Prop("StreetLampLed", parent, new Vector3(x, 0.15f, north ? 3.7f : -3.7f), yaw);
        var head = new Vector3(0.95f, 5.82f, 0f);
        AddLight(lamp.transform, "LampLight", head, LightType.Spot, new Color(1f, 0.95f, 0.86f), 95f, 18f, 115f, Quaternion.Euler(90f, 0f, 0f), local: true);
        var g = Box(lamp.transform, "LampGlow", head + Vector3.down * 0.06f, new Vector3(0.5f, 0.02f, 0.16f), glow, Vector2.one, collider: false);
        g.transform.localPosition = head + Vector3.down * 0.06f;
        g.transform.localRotation = Quaternion.identity;
    }

    // =====================================================================
    // 주인공 집 (실내, z 65~73)
    // =====================================================================

    struct Home { public Transform spawn, streetStart; public StoryInteractable door; public Vector3 tv, desk; public GameObject deskWalkman, deskLetter; }

    static Home BuildHome(Dictionary<string, Material> M)
    {
        var g = Group("Home");
        Room(g, new Vector3(0f, 0f, 69f), new Vector2(10f, 8f), 3f, M["WallHome"], M["FloorWood"], M["Ceiling"]);
        // 현관문: 바깥(초록 문)과 같은 문. 생성 모델(ApartmentDoor)은 텍스처에 금이 가 있어 쓰지 않는다
        HouseBuilder.InteriorDoor(g, "InteriorDoor", new Vector3(0f, 0f, 65f), 0f, HouseBuilder.Door("Green"), HouseBuilder.SharedMaterials());
        Prop("Bed", g, new Vector3(3.1f, 0f, 71.85f), 180f);
        Prop("Desk", g, new Vector3(-4.45f, 0f, 69.6f), 90f);
        Prop("CassetteShelf", g, new Vector3(-2.6f, 0f, 72.78f), 180f);
        Box(g, "TvCabinet", new Vector3(4.65f, 0.25f, 67.4f), new Vector3(0.5f, 0.5f, 1.3f), M["DarkWood"], Vector2.one);
        var tv = Prop("CrtTv", g, new Vector3(4.6f, 0.5f, 67.4f), -90f);
        Box(g, "Rug", new Vector3(0.5f, 0.006f, 68.8f), new Vector3(3.2f, 0.012f, 2.2f), M["Rug"], new Vector2(3.2f, 2.2f), collider: false);
        Box(g, "Window", new Vector3(0.4f, 1.65f, 72.93f), new Vector3(2.2f, 1.3f, 0.04f), M["NightWindow"], new Vector2(2.2f, 1.3f), collider: false); // 창 전체에 창밖 그림 한 장
        AddLight(g, "CeilingLamp", new Vector3(0f, 2.35f, 69f), LightType.Point, new Color(1f, 0.78f, 0.52f), 4.5f, 11f);
        // 책상(상판 0.75m, 스탠드는 책상 기준 +x 0.42 → 세계 z 69.18): 뜯은 택배 상자, 대니의 편지, 카세트 플레이어
        AddLight(g, "DeskLamp", new Vector3(-4.28f, 1.12f, 69.2f), LightType.Point, new Color(1f, 0.85f, 0.6f), 1.4f, 3.5f);
        var openedBox = Prop("OpenedParcel", g, new Vector3(-4.52f, 0.75f, 70.07f), 90f); // 뜯어진 상자, 열린 쪽이 방을 본다
        openedBox.name = "OpenedParcel";
        var letter = Box(g, "Letter", new Vector3(-4.26f, 0.753f, 69.62f), new Vector3(0.21f, 0.004f, 0.29f), M["Paper"], Vector2.one, collider: false);
        letter.transform.rotation = Quaternion.Euler(0f, 78f, 0f);
        var deskWalkman = Prop("Walkman", g, new Vector3(-4.27f, 0.775f, 69.72f), 0f); // 편지 위에 올려 둔다
        deskWalkman.name = "DeskWalkman";
        deskWalkman.transform.rotation = Quaternion.Euler(-90f, 0f, 75f); // 앞면이 위를 보게 눕힌다
        AddLight(g, "TvGlow", new Vector3(4.1f, 0.85f, 67.4f), LightType.Point, new Color(0.55f, 0.7f, 1f), 1.4f, 4f);

        var spawn = Marker(g, "HomeSpawn", new Vector3(-1.6f, 0f, 68.6f), 200f);
        var door = Interactable(g, "HomeDoor", new Vector3(0f, 1f, 65.9f), new Vector3(2.2f, 2f, 1.6f), "현관문을 열고 나간다");
        var street = Marker(root, "StreetStart", new Vector3(RayHouseX, 0.15f, HouseLine - 3.6f), 90f); // 레이 집 포치 계단 앞 보도
        return new Home { spawn = spawn, streetStart = street, door = door, tv = tv.transform.position + new Vector3(0f, 0.24f, 0f),
            desk = new Vector3(-4.32f, 0.78f, 69.85f), deskWalkman = deskWalkman, deskLetter = letter };
    }

    // =====================================================================
    // 친구 집 (실내, z 65~74)
    // =====================================================================

    struct Friend { public Transform entry, outsideSpawn, letterSpot; public StoryInteractable outsideDoor, window; }

    static Friend BuildFriendRoom(Dictionary<string, Material> M)
    {
        var g = Group("FriendRoom");
        Room(g, new Vector3(102f, 0f, 69.5f), new Vector2(12f, 9f), 3f, M["WallFriend"], M["FloorWood"], M["Ceiling"]);
        HouseBuilder.InteriorDoor(g, "InteriorDoor", new Vector3(102f, 0f, 65f), 0f, HouseBuilder.Door("White"), HouseBuilder.SharedMaterials()); // 대니 집 바깥 문과 같은 흰 문
        Prop("Sofa", g, new Vector3(102.4f, 0f, 73.45f), 180f);
        Prop("Desk", g, new Vector3(107.45f, 0f, 69.2f), -90f);
        Prop("TrashPile", g, new Vector3(97.4f, 0f, 72.4f), 30f);
        Prop("CassetteShelf", g, new Vector3(96.3f, 0f, 68.4f), 90f);
        // 뒤진 흔적: 넘어진 TV, 뜯긴 상자, 흩어진 종이
        var tv = Prop("CrtTv", g, new Vector3(99.3f, 0.2f, 68.2f), 35f);
        tv.transform.rotation = Quaternion.Euler(0f, 35f, 88f);
        SitOnFloor(tv, 0f);
        var box = Prop("ParcelBox", g, new Vector3(104.6f, 0f, 72.1f), 20f);
        box.transform.rotation = Quaternion.Euler(0f, 20f, 180f); // 뒤집힌 빈 상자
        SitOnFloor(box, 0f);
        var rng = new System.Random(11);
        for (int i = 0; i < 14; i++)
        {
            var paper = Box(g, "Paper", new Vector3(98f + (float)rng.NextDouble() * 8f, 0.004f, 66.8f + (float)rng.NextDouble() * 6f),
                new Vector3(0.21f, 0.004f, 0.3f), M["Paper"], Vector2.one, collider: false);
            paper.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        }
        Box(g, "Window", new Vector3(100f, 1.65f, 73.93f), new Vector3(2.2f, 1.3f, 0.04f), M["NightWindow"], new Vector2(2.2f, 1.3f), collider: false); // 창 전체에 창밖 그림 한 장
        AddLight(g, "DeskLamp", new Vector3(107.3f, 1.15f, 68.6f), LightType.Point, new Color(1f, 0.8f, 0.55f), 1.6f, 6f);
        AddLight(g, "WindowSpill", new Vector3(100f, 1.8f, 73.2f), LightType.Point, new Color(0.45f, 0.6f, 1f), 1.2f, 6f);
        AddLight(g, "Hallway", new Vector3(102f, 2.4f, 66f), LightType.Point, new Color(1f, 0.75f, 0.5f), 1.6f, 5f);
        AddLight(g, "CeilingDim", new Vector3(102.5f, 2.35f, 70.2f), LightType.Point, new Color(1f, 0.82f, 0.62f), 3.2f, 11f);

        var entry = Marker(g, "FriendRoomEntry", new Vector3(102f, 0f, 66.1f), 0f);
        var window = Interactable(g, "FriendWindow", new Vector3(100f, 1f, 73.1f), new Vector3(2.6f, 2f, 1.4f), "창문으로 빠져나간다");
        var outsideSpawn = Marker(root, "FriendOutside", new Vector3(DannyHouseX, 0.15f, HouseLine - 3.8f), 180f); // 대니 집 포치 계단 앞 보도
        var outsideDoor = Interactable(root, "FriendDoor", new Vector3(DannyHouseX, 1.75f, HouseLine - 1.3f), new Vector3(2.6f, 2.2f, 3.2f), "문을 두드린다"); // 포치 위 + 맨 윗계단
        // 뒷창문으로 빠져나와 숨는 곳: 대니 집 뒷벽(z 21) 앞 뒷마당, 1층 뒷창 아래. 집 벽을 따라 동쪽을 본다
        var letterSpot = Marker(root, "LetterSpot", new Vector3(DannyHouseX - 3.7f, Ground, HouseLine + 9f + 1.6f), 90f);
        return new Friend { entry = entry, outsideSpawn = outsideSpawn, outsideDoor = outsideDoor, window = window, letterSpot = letterSpot };
    }

    // =====================================================================
    // 막다른 골목과 동쪽 끝
    // =====================================================================

    internal static void BuildAlleyAndEnds(Dictionary<string, Material> M)
    {
        var g = Group("Alley");
        // 막다른 뒷골목: 북쪽 x 125~133, 인도에서 안쪽으로. 양옆은 높은 판자 담장, 끝은 철망
        Box(g, "AlleyFloor", new Vector3(129f, 0.16f, 16.25f), new Vector3(8f, 0.02f, 19.5f), M["Asphalt"], new Vector2(4f, 4f), collider: false);
        WoodFence(g, new Vector3(124.8f, Ground, 6.5f), new Vector3(124.8f, Ground, BackFence), 2.6f, M);
        WoodFence(g, new Vector3(133.2f, Ground, 6.5f), new Vector3(133.2f, Ground, BackFence), 2.6f, M);
        WoodFence(g, new Vector3(124.8f, Ground, BackFence), new Vector3(133.2f, Ground, BackFence), 2.6f, M);
        Fence(g, new Vector3(124.9f, Ground, 24.5f), new Vector3(133.1f, Ground, 24.5f), 3.2f, M);
        Prop("Dumpster", g, new Vector3(126.4f, Ground, 14f), 90f);
        Prop("TrashPile", g, new Vector3(131.3f, Ground, 21.5f), -60f);
        AddLight(g, "AlleyBulb", new Vector3(132.6f, 3.4f, 21f), LightType.Point, new Color(1f, 0.7f, 0.4f), 5f, 11f);
        AddLight(g, "AlleyEntrance", new Vector3(125.6f, 3.2f, 9f), LightType.Point, new Color(0.8f, 0.85f, 1f), 3f, 9f);

        // 동쪽 끝: 공사 철망과 바리케이드로 길이 막혀 있다
        Fence(g, new Vector3(149f, 0f, -BackFence), new Vector3(149f, 0f, BackFence), 2.6f, M);
        foreach (float z in new[] { -5.2f, -2f, 1.5f, 5f }) Prop("ConstructionBarrier", g, new Vector3(147.5f, Mathf.Abs(z) > 4f ? Ground : 0f, z), 90f);
        AddLight(g, "WorkLight", new Vector3(148f, 2.4f, 0f), LightType.Point, new Color(1f, 0.6f, 0.25f), 3f, 8f);
    }

    internal static void Fence(Transform parent, Vector3 a, Vector3 b, float height, Dictionary<string, Material> M)
    {
        var f = new GameObject("Fence").transform;
        f.SetParent(parent);
        Vector3 dir = b - a;
        float length = dir.magnitude;
        f.SetPositionAndRotation((a + b) / 2f, Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir.normalized)));
        int posts = Mathf.CeilToInt(length / 2.5f);
        for (int i = 0; i <= posts; i++)
        {
            var p = Box(f, "Post", Vector3.zero, new Vector3(0.07f, height, 0.07f), M["Galvanized"], Vector2.one, collider: false);
            p.transform.localPosition = new Vector3(-length / 2f + length * i / posts, height / 2f, 0f);
        }
        var rail = Box(f, "Rail", Vector3.zero, new Vector3(length, 0.05f, 0.05f), M["Galvanized"], Vector2.one, collider: false);
        rail.transform.localPosition = new Vector3(0f, height - 0.03f, 0f);
        var mesh = Box(f, "Mesh", Vector3.zero, new Vector3(length, height, 0.01f), M["ChainLink"], new Vector2(1f, 1f), collider: false);
        mesh.transform.localPosition = new Vector3(0f, height / 2f, 0f);
        var col = f.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, height / 2f, 0f);
        col.size = new Vector3(length, height, 0.2f);
    }

    // =====================================================================
    // 인물
    // =====================================================================

    internal static PlayerController CreatePlayer(Transform spawn, out HumanoidIK ik, out Ragdoll ragdoll, out Transform walkman, out Transform aimPoint)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        var player = go.GetComponent<PlayerController>();
        // 맨손 격투라 예전 시제품 무대용 칼(WeaponPivot: 칼날·손잡이)은 지운다. 프리팹 안의 자식은 바로 못 지우므로 바깥 연결만 푼다
        if (player.weaponPivot)
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(player.weaponPivot.gameObject);
            player.weaponPivot = null;
        }
        var animator = player.animator;
        animator.runtimeAnimatorController = BaroqueCharacterSetup.EnsureIKController();
        Debug.Log(FingerRig.Apply(animator)); // 손가락 뼈: 주먹을 쥐게 한다(바로크 뼈대에는 손가락이 없어 손날이 됐다)
        ik = animator.gameObject.AddComponent<HumanoidIK>();
        ragdoll = animator.gameObject.AddComponent<Ragdoll>();

        // 전직 경찰의 맨손 격투: 잽 → 크로스 → 돌려차기. 타격 시점은 클립에서 손발이 가장 멀리 뻗는 순간(재생 시간 기준)
        player.unarmed = true;
        player.attackReach = 0.9f;
        // 회피 = 구르기(0.55초 동안 약 3.1m, 구르는 동안 무적). 동작은 Roll 상태(CombatAnimationSetup)
        player.dodgeSpeed = 7f;
        player.dodgeDuration = 0.55f;
        player.dodgeCooldown = 0.25f;
        player.combo = new[]
        {
            new PlayerController.AttackStep { animState = "Jab",   duration = 0.46f, hitTime = 0.25f, damageMultiplier = 1f, lunge = 0.25f, radius = 0.85f, hitStop = 0.03f },
            new PlayerController.AttackStep { animState = "Cross", duration = 0.5f,  hitTime = 0.27f, damageMultiplier = 1.2f, lunge = 0.3f, radius = 0.85f, hitStop = 0.04f },
            new PlayerController.AttackStep { animState = "Kick",  duration = 0.9f,  hitTime = 0.45f, damageMultiplier = 2f, lunge = 0.5f, radius = 1.1f, hitStop = 0.08f }, // 돌려차기: 발이 가장 높은 순간 0.45초
        };
        player.hurtDuration = 0.45f;

        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        // 재킷 오른쪽 주머니 속 카세트 플레이어: 평소엔 숨겨 두고(감독이 끈다), 총에 맞아 쓰러질 때 빠져나와 떨어진다
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var wm = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PropPrefabs}/Walkman.prefab"));
        wm.name = "PocketWalkman";
        wm.transform.SetParent(hips, false);
        wm.transform.position = hips.position + go.transform.right * 0.19f + go.transform.forward * 0.03f - Vector3.up * 0.05f;
        wm.transform.rotation = go.transform.rotation * Quaternion.Euler(0f, 90f, 0f);
        walkman = wm.transform;

        var aim = new GameObject("AimPoint").transform;
        aim.SetParent(chest, false);
        aim.position = chest.position + Vector3.up * 0.1f;
        aimPoint = aim;
        return player;
    }

    internal static EnemyAI CreateThug(Transform parent, string name, Vector3 position, float yaw, string character = "Enforcer")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var col = go.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.9f, 0f);
        col.height = 1.8f;
        col.radius = 0.32f;
        var agent = go.AddComponent<NavMeshAgent>();
        agent.speed = 6.3f;
        agent.acceleration = 22f;
        agent.angularSpeed = 600f;
        agent.radius = 0.32f;
        agent.height = 1.8f;
        agent.stoppingDistance = 0.9f;
        agent.enabled = false; // EnemyAI가 Start에서 켠다(내비메시 등록 뒤)
        var health = go.AddComponent<Health>();
        health.team = Team.Enemy;
        health.maxHealth = 100;
        var ai = go.AddComponent<EnemyAI>();
        ai.displayName = "조직원";
        ai.damage = 0; // 1회차 추격에서는 붙잡기만 한다(총은 붙잡은 뒤 컷신에서)
        ai.attackRange = 1.4f;

        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BaroqueCharacterSetup.CharacterRoot + $"/{character}/{character}_Model.prefab"), go.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        var anim = model.GetComponent<Animator>();
        anim.runtimeAnimatorController = BaroqueCharacterSetup.EnsureIKController();
        Debug.Log(FingerRig.Apply(anim)); // 손가락 뼈
        var loco = go.AddComponent<AgentLocomotionAnimator>();
        loco.animator = anim;
        // 걷기·달리기 클립의 발소리 이벤트를 받는다(없으면 이벤트마다 경고가 뜬다). 쫓아오는 발소리도 들린다
        var steps = model.AddComponent<PlayerAnimationEvents>();
        const string sfx = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Sfx";
        steps.footsteps = Enumerable.Range(1, 10).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfx}/Player_Footstep_{i:00}.wav")).Where(c => c != null).ToArray();
        steps.landing = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfx}/Player_Land.wav");
        steps.volume = 0.4f;
        var ik = model.AddComponent<HumanoidIK>();
        var gun = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PropPrefabs}/Pistol.prefab"), model.transform);
        gun.name = "Pistol";
        gun.SetActive(false);
        ik.heldItem = gun.transform;
        ai.body = anim;                          // 피격·주먹 동작 클립
        ai.ragdoll = model.AddComponent<Ragdoll>(); // 쓰러지면 시체가 남는다
        ai.aimIK = ik;                           // 총잡이일 때 겨누는 팔과 총구
        // 맞으면 몸이 하얗게 번쩍인다(텍스처가 있는 모델이라 1을 넘는 흰색이어야 실제로 밝아진다)
        var tint = go.AddComponent<BodyTint>();
        tint.renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        tint.flashColor = new Color(2.6f, 2.6f, 2.6f);
        tint.flashDuration = 0.07f;
        return ai;
    }

    // =====================================================================
    // 카메라·조명·환경
    // =====================================================================

    internal static CinemachineCamera VCam(Transform parent, string name, Vector3 position, Vector3 lookAt, float fov)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        var dir = lookAt - position;
        go.transform.SetPositionAndRotation(position, dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity);
        var cam = go.AddComponent<CinemachineCamera>();
        var lens = LensSettings.Default;
        lens.FieldOfView = fov;
        lens.NearClipPlane = 0.05f;
        lens.FarClipPlane = 400f;
        cam.Lens = lens;
        cam.Priority = 0;
        return cam;
    }

    internal static ParticleSystem CreateRain(Transform follow, Material mat)
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(root);
        go.transform.rotation = Quaternion.identity;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize = 0.025f;
        main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.45f);
        main.maxParticles = 9000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        var emission = ps.emission;
        emission.rateOverTime = 6000f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(40f, 0.1f, 40f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-1.2f);
        vel.y = new ParticleSystem.MinMaxCurve(-17f);
        vel.z = new ParticleSystem.MinMaxCurve(0.6f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.045f;
        r.lengthScale = 1f;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.AddComponent<FollowPosition>().target = follow;
        go.GetComponent<FollowPosition>().height = 13f;
        return ps;
    }

    internal static void SetupEnvironment(string volumePath = null)
    {
        var moon = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
        moon.name = "Moonlight";
        moon.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        moon.color = new Color(0.55f, 0.64f, 0.85f);
        moon.intensity = 0.32f;
        moon.shadows = LightShadows.Soft;

        RenderSettings.skybox = null;
        RenderSettings.sun = moon;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.11f, 0.12f, 0.16f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.016f;
        RenderSettings.fogColor = new Color(0.04f, 0.048f, 0.068f);
        RenderSettings.reflectionIntensity = 0.4f;

        string path = volumePath ?? $"{ArtRoot}/PrologueVolume.asset"; // 장면마다 따로 둔다(지우고 다시 만들기 때문)
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.85f);
        bloom.intensity.Override(1.3f);
        bloom.scatter.Override(0.72f);
        profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.95f);
        color.contrast.Override(12f);
        color.saturation.Override(-12f);
        color.colorFilter.Override(new Color(0.88f, 0.94f, 1f));
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.32f);
        vignette.smoothness.Override(0.5f);
        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.22f);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);
        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    internal static void BakeNavMesh(string path = "Assets/Game/Scenes/Prologue_NavMesh.asset")
    {
        var surface = root.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.12f;
        surface.BuildNavMesh();
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
    }

    // =====================================================================
    // 재료(텍스처·머티리얼) — 모두 코드로 만든다
    // =====================================================================

    internal static Dictionary<string, Material> CreateMaterials()
    {
        var M = new Dictionary<string, Material>();
        M["Asphalt"] = Lit("Asphalt", Tex("Asphalt", 512, AsphaltPixel), Color.white, 0.92f, smoothnessFromAlpha: true);
        M["Sidewalk"] = Lit("Sidewalk", Tex("Sidewalk", 512, SidewalkPixel), Color.white, 0.6f, smoothnessFromAlpha: true);
        M["Curb"] = Lit("Curb", null, new Color(0.42f, 0.42f, 0.43f), 0.45f);
        M["Paint"] = Lit("RoadPaint", null, new Color(0.8f, 0.8f, 0.76f), 0.55f);
        M["FacadeA"] = Facade("FacadeA", 1, new Color(0.30f, 0.27f, 0.25f), 0.38f);
        M["FacadeB"] = Facade("FacadeB", 2, new Color(0.22f, 0.24f, 0.27f), 0.3f);
        M["FacadeC"] = Facade("FacadeC", 3, new Color(0.36f, 0.31f, 0.26f), 0.45f);
        M["LampGlow"] = Unlit("LampGlow", new Color(1f, 0.96f, 0.88f) * 4f);
        M["FacadeBase"] = Lit("FacadeBase", Tex("FacadeBase", 256, FacadeBasePixel), Color.white, 0.3f);
        facadeBase = M["FacadeBase"];
        M["WallHome"] = Lit("WallHome", null, new Color(0.55f, 0.52f, 0.47f), 0.15f);
        M["WallFriend"] = Lit("WallFriend", null, new Color(0.42f, 0.47f, 0.5f), 0.15f);
        M["FloorWood"] = Lit("FloorWood", Tex("FloorWood", 512, WoodPixel), Color.white, 0.45f);
        M["Ceiling"] = Lit("Ceiling", null, new Color(0.6f, 0.6f, 0.58f), 0.05f);
        M["DarkWood"] = Lit("DarkWood", null, new Color(0.17f, 0.11f, 0.07f), 0.35f);
        M["Rug"] = Lit("Rug", Tex("Rug", 256, RugPixel), Color.white, 0.05f);
        M["NightWindow"] = Lit("NightWindow", Tex("NightWindow", 256, NightWindowPixel), Color.white, 0.45f, emission: new Color(0.8f, 0.8f, 0.8f), emissionMapFromBase: true); // 광택이 높으면 창 앞 조명이 흰 점으로 비친다
        M["Paper"] = Lit("Paper", null, new Color(0.85f, 0.84f, 0.8f), 0.1f);
        M["BuildingBack"] = Lit("BuildingBack", Tex("Concrete", 256, ConcretePixel), new Color(0.55f, 0.55f, 0.56f), 0.2f); // 건물 뒤·옆·지붕: 창문 불빛 없음
        M["Grass"] = Lit("Grass", Tex("Grass", 512, GrassPixel), Color.white, 0.35f, smoothnessFromAlpha: true);
        M["WoodFence"] = Lit("WoodFence", Tex("WoodFence", 256, FencePixel), Color.white, 0.2f);
        M["Galvanized"] = Lit("Galvanized", null, new Color(0.5f, 0.52f, 0.54f), 0.5f, metallic: 0.8f);
        M["ChainLink"] = ChainLinkMaterial();
        M["RainStreak"] = RainMaterial();
        return M;
    }

    /// <summary>
    /// URP는 머티리얼을 검사할 때마다 키워드를 자체 규칙으로 다시 계산한다. 발광은 GI 플래그에 Emissive가 있어야만 켜 두므로
    /// (BaseShaderGUI.SetMaterialKeywords) 플래그를 RealtimeEmissive로 두고, 마지막에 속성 값 기준으로 키워드를 다시 맞춰 저장한다.
    /// </summary>
    internal static void EnforceMaterialKeywords()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ArtRoot + "/Materials" }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat.shader.name != "Universal Render Pipeline/Lit") continue;
            Color e = mat.GetColor("_EmissionColor");
            if (e.maxColorComponent > 0.001f)
            {
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.EnableKeyword("_EMISSION");
            }
            if (mat.GetFloat("_SmoothnessTextureChannel") > 0.5f) mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            if (mat.GetFloat("_AlphaClip") > 0.5f) mat.EnableKeyword("_ALPHATEST_ON");
            if (mat.GetTexture("_BumpMap") != null) mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }
    }

    /// <summary>
    /// 소품 배치 검사: ① 바닥에 떠 있거나 묻혔는지 ② 건물·방 벽을 파고들었는지 ③ 다른 소품과 겹쳤는지 ④ 문 앞을 막았는지.
    /// 문제 목록을 돌려준다(없으면 "문제 없음"). 빌드 뒤 따로 불러 확인한다.
    /// </summary>
    public static string ValidatePlacement()
    {
        Physics.SyncTransforms();
        var report = new System.Text.StringBuilder();
        int issues = 0, count = 0;
        var walls = Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)
            .Where(c => c.name is "Base" or "Upper" || c.name.StartsWith("Wall_") || c.name.StartsWith("Building") || c.name == "Fence" || c.name.StartsWith("House_"))
            .ToList();
        var props = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t => PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) == t.gameObject   // 소품 안의 Model(GLB) 자식은 따로 세지 않는다
                        && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)).StartsWith(PropPrefabs)
                        && t.GetComponentInParent<PlayerController>() == null)                         // 주인공 몸에 붙은 주머니 카세트 플레이어 제외
            .ToList();
        var doors = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "InteriorDoor").ToList();
        string[] wallMounted = { "AcUnit", "CctvCamera" };
        string[] onFurniture = { "DeskWalkman", "OpenedParcel", "CrtTv" }; // 책상·장 위에 올린 것
        Bounds Solid(Transform t) => t.TryGetComponent(out BoxCollider c) ? c.bounds : B(t); // 겹침은 실제로 부딪히는 범위로
        Vector3 Contact(Transform t, Bounds b)
        {
            // 땅에 닿는 정점(가장 낮은 곳에서 3cm 안)의 가운데: 가로등은 기둥 밑동, 자동차는 바퀴 사이
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    if (w.y < b.min.y + 0.03f) { sum += w; n++; }
                }
            }
            return n > 0 ? sum / n : b.center;
        }
        Bounds B(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
        float Overlap(Bounds a, Bounds b)
        {
            float x = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float y = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            float z = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            return Mathf.Min(x, Mathf.Min(y, z));
        }

        foreach (var p in props)
        {
            count++;
            var b = B(p);
            string label = $"{p.parent.name}/{p.name} @({p.position.x:0.0}, {p.position.z:0.0})";
            bool mounted = wallMounted.Any(w => p.name.StartsWith(w));
            // ① 바닥: 소품 아래 표면(자기 자신·인물 제외)까지의 틈. 눕히거나 뒤집은 소품도 가장 낮은 정점으로 잰다
            if (!mounted && !onFurniture.Contains(p.name))
            {
                var c = Contact(p, b);
                var hits = Physics.RaycastAll(new Vector3(c.x, b.min.y + 0.4f, c.z), Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.collider.transform.IsChildOf(p) && h.collider.transform != p).OrderBy(h => h.distance).ToList();
                if (hits.Count == 0) { issues++; report.AppendLine($"[바닥 없음] {label}"); }
                else
                {
                    float gap = b.min.y - hits[0].point.y;
                    if (Mathf.Abs(gap) > 0.04f) { issues++; report.AppendLine($"[{(gap > 0 ? "떠 있음" : "묻힘")} {Mathf.Abs(gap):0.00}m] {label} (아래: {hits[0].collider.name})"); }
                }
            }

            // ② 벽 파고듦(벽에 붙는 것은 정상, 5cm 넘게 들어가면 문제)
            foreach (var w in walls)
            {
                // 직접 지은 집(몸체·포치·계단 충돌체)은 소품의 실제 충돌 범위로 비교한다(나무 수관은 그림 범위가 넓다)
                float d = Overlap(w.name.StartsWith("House_") ? Solid(p) : b, w.bounds);
                if (d > 0.05f && w.transform.parent != null) { issues++; report.AppendLine($"[벽 파고듦 {d:0.00}m] {label} ↔ {w.transform.parent.name}/{w.name}"); }
            }

            // ③ 다른 소품과 겹침
            foreach (var q in props)
            {
                if (q == p || q.GetInstanceID() < p.GetInstanceID()) continue;
                if (onFurniture.Contains(p.name) || onFurniture.Contains(q.name)) continue;
                float d = Overlap(Solid(p), Solid(q));
                if (d > 0.05f) { issues++; report.AppendLine($"[소품끼리 겹침 {d:0.00}m] {label} ↔ {q.name} @({q.position.x:0.0}, {q.position.z:0.0})"); }
            }

            // ④ 문 앞 1.3m 안을 막음
            foreach (var door in doors)
            {
                if (door == p) continue;
                var front = new Bounds(door.position + door.forward * 0.75f + Vector3.up, new Vector3(1.3f, 2f, 1.3f));
                if (Overlap(b, front) > 0.02f) { issues++; report.AppendLine($"[문 앞 막음] {label} ↔ 문 @({door.position.x:0.0}, {door.position.z:0.0})"); }
            }
        }
        return $"소품 {count}개 검사, 문제 {issues}건\n" + (issues == 0 ? "문제 없음" : report.ToString());
    }

    internal static Material Facade(string name, int seed, Color wall, float litChance)
    {
        var rng = new System.Random(seed * 97);
        // 16m x 14m를 한 장에: 가로 4칸(4m) x 세로 4층(3.5m), 칸마다 1.2m x 1.6m 창문 두 개.
        // 1층은 따로 만드는 석재 기단(FacadeBase)이 맡고, 이 텍스처는 2층(3.5m)부터 덮는다
        const int W = 1024, H = 896;
        var albedo = new Color32[W * H];
        var emission = new Color32[W * H];
        var lit = new Color[32];
        for (int i = 0; i < 32; i++)
        {
            double r = rng.NextDouble();
            lit[i] = r < litChance
                ? Color.Lerp(new Color(1f, 0.66f, 0.36f), new Color(0.8f, 0.86f, 1f), (float)(rng.NextDouble() < 0.25 ? rng.NextDouble() : 0.0)) * (0.35f + (float)rng.NextDouble() * 0.45f)
                : Color.clear;
        }
        var noise = new System.Random(seed);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int cx = x / 256, cy = y / 224, lx = x % 256, ly = y % 224;
            int pane = lx < 128 ? 0 : 1, px = lx % 128;
            bool glass = px > 26 && px < 103 && ly > 58 && ly < 160;
            bool frame = !glass && px > 21 && px < 108 && ly > 53 && ly < 165;
            bool sill = ly >= 46 && ly < 53 && px > 16 && px < 113;
            bool band = ly < 8; // 층 사이 띠
            float n = (float)noise.NextDouble() * 0.06f - 0.03f;
            Color c = wall + new Color(n, n, n);
            Color e = Color.black;
            if (frame || sill || band) c = wall * 0.6f;
            if (glass)
            {
                var l = lit[cy * 8 + cx * 2 + pane];
                bool blinds = l.a > 0 && (cy + cx + pane) % 3 == 0 && (ly / 7) % 2 == 0;
                bool curtain = l.a > 0 && (cy * 3 + cx + pane) % 4 == 1 && px < 50; // 반쯤 친 커튼
                if (l.a > 0 && !blinds && !curtain) { c = l * 0.8f; e = l; }
                else if (l.a > 0) { c = l * 0.3f; e = l * 0.25f; }
                else c = new Color(0.04f, 0.05f, 0.065f) + new Color(n, n, n) * 0.5f;
            }
            albedo[y * W + x] = c;
            emission[y * W + x] = e;
        }
        var albedoTex = SaveTexture($"{name}_Albedo", W, H, albedo, true);
        var emissionTex = SaveTexture($"{name}_Emission", W, H, emission, true);
        var mat = Lit(name, albedoTex, Color.white, 0.25f);
        mat.SetTexture("_EmissionMap", emissionTex);
        mat.SetColor("_EmissionColor", Color.white * 2.4f); // 밤거리에서 창문이 은은히 빛나 보이도록(블룸 문턱값을 살짝 넘게)
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static Color AsphaltPixel(int x, int y, int size)
    {
        float u = x / (float)size, v = y / (float)size;
        float grain = Hash(x, y) * 0.07f;
        float tone = 0.2f + grain + Fbm(u * 6f, v * 6f) * 0.05f;
        // 물웅덩이: 낮은 주파수 잡음이 높은 곳만 매끄럽게(빛이 비친다)
        float puddle = Mathf.SmoothStep(0.55f, 0.68f, Fbm(u * 2.2f + 3f, v * 2.2f + 7f));
        float smooth = Mathf.Lerp(0.55f, 1f, puddle);
        tone *= Mathf.Lerp(1f, 0.6f, puddle);
        return new Color(tone, tone, tone * 1.05f, smooth);
    }

    internal static Color GrassPixel(int x, int y, int size)
    {
        // 짧게 깎은 젖은 잔디: 잎 결 잡음 + 듬성듬성 어두운 흙. 알파 = 매끄러움(비에 젖어 살짝 반짝)
        float u = x / (float)size, v = y / (float)size;
        float blade = Hash(x, y) * 0.08f + Hash(x / 2, y / 3) * 0.06f;
        float patch = Fbm(u * 5f, v * 5f);
        float g = 0.16f + blade + patch * 0.08f;
        return new Color(g * 0.55f, g, g * 0.42f, 0.35f + patch * 0.25f);
    }

    internal static Color FencePixel(int x, int y, int size)
    {
        // 세로 판자 8장(1.2m) + 판자 사이 틈 + 가로 지지대 두 줄
        int plank = size / 8;
        bool gap = x % plank < 2;
        bool rail = Mathf.Abs(y - size * 0.22f) < 5 || Mathf.Abs(y - size * 0.78f) < 5;
        float grain = Mathf.Sin(y * 0.15f + Hash(x / plank, 7) * 30f) * 0.02f + Hash(x, y) * 0.03f;
        float t = gap ? 0.05f : 0.27f + grain + Hash(x / plank, 3) * 0.05f;
        if (rail && !gap) t *= 0.8f;
        return new Color(t * 1.15f, t * 0.88f, t * 0.62f, 1f);
    }

    internal static Color FacadeBasePixel(int x, int y, int size)
    {
        // 4m x 4m 한 장: 0.5m 높이의 어두운 걸레받이 + 1m 간격 석재 판, 판 사이 줄눈
        float u = x / (float)size * 4f, v = y / (float)size * 4f;
        bool joint = (u % 1f) < 0.02f || (v % 0.875f) < 0.025f;
        float n = Hash(x, y) * 0.05f + Fbm(u * 2f, v * 2f) * 0.06f;
        float t = v < 0.5f ? 0.11f + n * 0.5f : 0.24f + n;
        if (joint) t *= 0.6f;
        return new Color(t * 1.03f, t, t * 0.96f, 1f);
    }

    internal static Color ConcretePixel(int x, int y, int size)
    {
        float n = Hash(x, y) * 0.05f + Fbm(x / (float)size * 6f, y / (float)size * 6f) * 0.08f;
        float t = 0.2f + n;
        return new Color(t, t, t * 1.02f, 1f);
    }

    internal static Color SidewalkPixel(int x, int y, int size)
    {
        int tile = size / 4; // 2m 텍스처에 0.5m 보도블록
        bool seam = x % tile < 3 || y % tile < 3;
        float n = Hash(x, y) * 0.06f + Fbm(x / (float)size * 8f, y / (float)size * 8f) * 0.05f;
        float tone = seam ? 0.17f : 0.31f + n;
        return new Color(tone, tone * 0.99f, tone * 0.97f, seam ? 0.8f : 0.5f);
    }

    internal static Color WoodPixel(int x, int y, int size)
    {
        int plank = size / 8;
        int row = y / plank;
        bool seam = y % plank < 2 || (x + row * 97) % (size / 2) < 2;
        float grain = Mathf.Sin((x + Hash(row, 3) * 400f) * 0.05f + Fbm(x * 0.01f, y * 0.08f) * 6f) * 0.03f;
        float tone = seam ? 0.08f : 0.24f + grain + Hash(row, 9) * 0.06f;
        return new Color(tone * 1.25f, tone * 0.9f, tone * 0.6f, 1f);
    }

    internal static Color RugPixel(int x, int y, int size)
    {
        bool border = x < 18 || y < 18 || x > size - 18 || y > size - 18;
        bool stripe = (x / 22 + y / 22) % 2 == 0;
        Color a = new(0.35f, 0.12f, 0.1f), b = new(0.28f, 0.1f, 0.09f);
        return border ? new Color(0.65f, 0.55f, 0.35f) : stripe ? a : b;
    }

    internal static Color NightWindowPixel(int x, int y, int size)
    {
        // 창밖: 비 오는 밤의 흐릿한 주택가. 위는 어두운 남색 하늘, 아래는 건너편 집 실루엣, 초점이 나간 현관등 몇 개. 창살은 십자 + 테두리
        bool bar = Mathf.Abs(x - size / 2) < 4 || Mathf.Abs(y - size / 2) < 4 || x < 8 || y < 8 || x > size - 8 || y > size - 8;
        if (bar) return new Color(0.03f, 0.03f, 0.03f);
        float u = x / (float)size, v = y / (float)size;
        Color c = Color.Lerp(new Color(0.02f, 0.03f, 0.06f), new Color(0.08f, 0.1f, 0.17f), v);
        float roof = 0.44f + 0.04f * Mathf.Sin(u * 7f + 1.3f) + (Mathf.Repeat(u * 3f, 1f) < 0.5f ? 0.05f : 0f); // 지붕선(박공이 번갈아)
        float house = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((roof - v) / 0.035f));
        c = Color.Lerp(c, new Color(0.015f, 0.017f, 0.025f), house);
        foreach (var (bx, by, r, k) in new[] { (0.17f, 0.27f, 0.05f, 1f), (0.5f, 0.24f, 0.04f, 0.8f), (0.84f, 0.3f, 0.055f, 1f), (0.66f, 0.36f, 0.03f, 0.5f) })
        {
            float d2 = ((u - bx) * (u - bx) + (v - by) * (v - by)) / (r * r);
            c += new Color(1f, 0.72f, 0.42f) * (k * 0.75f * Mathf.Exp(-d2));
        }
        return c;
    }

    internal static Material ChainLinkMaterial()
    {
        const int S = 256;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            // 마름모 그물: 두 방향의 대각선이 겹치는 곳만 철사
            int a = (x + y) % 32, b = (x - y + 1024) % 32;
            bool wire = a < 3 || b < 3;
            px[y * S + x] = wire ? new Color32(150, 155, 160, 255) : new Color32(0, 0, 0, 0);
        }
        var tex = SaveTexture("ChainLink", S, S, px, true);
        var mat = Lit("ChainLink", tex, Color.white, 0.5f, metallic: 0.7f);
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cull", 0f); // 양면
        mat.SetFloat("_Surface", 0f);
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static Material RainMaterial()
    {
        const int W = 8, H = 64;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float edge = 1f - Mathf.Abs(x - (W - 1) / 2f) / (W / 2f);
            float along = Mathf.Sin(Mathf.PI * y / (H - 1));
            px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge * along));
        }
        var tex = SaveTexture("RainStreak", W, H, px, true);
        string path = $"{ArtRoot}/Materials/RainStreak.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", new Color(0.8f, 0.85f, 0.95f, 0.5f));
        mat.SetFloat("_Surface", 1f);   // 반투명
        mat.SetFloat("_Blend", 0f);     // 알파 섞기
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static Material Lit(string name, Texture2D tex, Color color, float smoothness, bool smoothnessFromAlpha = false, float metallic = 0f,
        Color? emission = null, bool emissionMapFromBase = false)
    {
        string path = $"{ArtRoot}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_SmoothnessTextureChannel", smoothnessFromAlpha ? 1f : 0f);
        if (smoothnessFromAlpha) mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        else mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        if (emission is Color e)
        {
            mat.SetColor("_EmissionColor", e);
            if (emissionMapFromBase) mat.SetTexture("_EmissionMap", tex);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static Material Unlit(string name, Color color)
    {
        string path = $"{ArtRoot}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static void SaveMaterial(Material mat)
    {
        string path = $"{ArtRoot}/Materials/{mat.name}.mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
    }

    internal static Texture2D Tex(string name, int size, System.Func<int, int, int, Color> pixel)
    {
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = pixel(x, y, size);
        return SaveTexture(name, size, size, px, true);
    }

    internal static Texture2D SaveTexture(string name, int w, int h, Color32[] px, bool sRGB)
    {
        string path = $"{ArtRoot}/Textures/{name}.png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.sRGBTexture = sRGB;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.anisoLevel = 4;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    internal static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    internal static float Fbm(float x, float y)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < 4; i++)
        {
            sum += amp * Mathf.PerlinNoise(x, y);
            x *= 2.03f; y *= 2.03f; amp *= 0.5f;
        }
        return sum / 0.9375f;
    }

    // =====================================================================
    // 기본 도구
    // =====================================================================

    internal static Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root);
        return t;
    }

    internal static void Room(Transform parent, Vector3 floorCentre, Vector2 size, float height, Material wall, Material floor, Material ceiling)
    {
        float t = 0.2f, hx = size.x / 2f, hz = size.y / 2f;
        Box(parent, "Floor", floorCentre + Vector3.down * 0.1f, new Vector3(size.x + 2 * t, 0.2f, size.y + 2 * t), floor, new Vector2(2f, 2f));
        Box(parent, "Ceiling", floorCentre + Vector3.up * (height + 0.1f), new Vector3(size.x + 2 * t, 0.2f, size.y + 2 * t), ceiling, new Vector2(2f, 2f));
        Box(parent, "Wall_N", floorCentre + new Vector3(0f, height / 2f, hz + t / 2f), new Vector3(size.x + 2 * t, height, t), wall, new Vector2(2f, 2f));
        Box(parent, "Wall_S", floorCentre + new Vector3(0f, height / 2f, -hz - t / 2f), new Vector3(size.x + 2 * t, height, t), wall, new Vector2(2f, 2f));
        Box(parent, "Wall_E", floorCentre + new Vector3(hx + t / 2f, height / 2f, 0f), new Vector3(t, height, size.y), wall, new Vector2(2f, 2f));
        Box(parent, "Wall_W", floorCentre + new Vector3(-hx - t / 2f, height / 2f, 0f), new Vector3(t, height, size.y), wall, new Vector2(2f, 2f));
        Box(parent, "Baseboard", floorCentre + new Vector3(0f, 0.05f, -hz + 0.01f), new Vector3(size.x, 0.1f, 0.02f), wall, Vector2.one, collider: false);
    }

    internal static GameObject Prop(string name, Transform parent, Vector3 position, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropPrefabs}/{name}.prefab");
        if (prefab == null) throw new System.InvalidOperationException($"소품 프리팹 없음: {name} (Game/Baroque/Rebuild Prop Prefabs 먼저 실행)");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return go;
    }

    /// <summary>돌린 소품을 위아래로만 옮겨 가장 낮은 곳이 floorY에 닿게 한다(바닥 아래 기준점인 소품을 뒤집으면 바닥에 묻힌다)</summary>
    internal static void SitOnFloor(GameObject go, float floorY)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        go.transform.position += Vector3.up * (floorY - b.min.y);
    }

    internal static Transform Marker(Transform parent, string name, Vector3 position, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent);
        t.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return t;
    }

    internal static StoryInteractable Interactable(Transform parent, string name, Vector3 centre, Vector3 size, string prompt)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = centre;
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
        col.isTrigger = true;
        var it = go.AddComponent<StoryInteractable>();
        it.prompt = prompt;
        return it;
    }

    /// <summary>조명을 만든다. local이면 position·rotation이 부모(소품) 기준, 아니면 세계 좌표.</summary>
    internal static Light AddLight(Transform parent, string name, Vector3 position, LightType type, Color color, float intensity, float range,
        float spotAngle = 0f, Quaternion? rotation = null, bool local = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (local)
        {
            go.transform.localPosition = position;
            go.transform.localRotation = rotation ?? Quaternion.identity;
        }
        else
        {
            go.transform.position = position;
            go.transform.rotation = rotation ?? Quaternion.identity;
        }
        var l = go.AddComponent<Light>();
        l.type = type;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        if (type == LightType.Spot) { l.spotAngle = spotAngle; l.innerSpotAngle = spotAngle * 0.45f; }
        l.shadows = LightShadows.None;
        return l;
    }

    /// <summary>크기에 맞춰 UV가 실제 미터 단위로 반복되는 상자(늘어난 텍스처가 생기지 않는다).</summary>
    internal static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, Material mat, Vector2 metresPerTile, bool collider = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = centre;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, metresPerTile);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        if (collider)
        {
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
        return go;
    }

    /// <summary>
    /// 건물: 거리(또는 골목)를 향한 면(fronts)에만 창문 외벽을 입히고, 뒤·옆·지붕은 불빛 없는 콘크리트로 둔다.
    /// 플레이어가 볼 일 없는 뒤쪽 창문까지 빛나면 쓸데없는 불빛이 된다.
    /// </summary>
    internal static Material facadeBase;

    internal static GameObject Building(Transform parent, string name, Vector3 centre, Vector3 size, Material facade, Material back, params Vector3[] fronts)
    {
        // 1층(3.5m)은 창문 없는 석재 기단: 문·자판기·택배함 같은 1층 소품이 창문 앞에 서는 일이 없다.
        // 위층은 거리 쪽 면에만 창문 외벽, 뒤·옆·지붕은 불빛 없는 콘크리트.
        const float baseHeight = 3.5f;
        float bottom = centre.y - size.y / 2f;
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.position = centre;
        var baseSize = new Vector3(size.x, baseHeight, size.z);
        var baseBox = Box(group, "Base", new Vector3(centre.x, bottom + baseHeight / 2f, centre.z), baseSize, facadeBase, new Vector2(4f, 4f));
        baseBox.GetComponent<MeshFilter>().sharedMesh = BoxMesh(baseSize, new Vector2(4f, 4f), fronts);
        baseBox.GetComponent<MeshRenderer>().sharedMaterials = new[] { facadeBase, back };
        float upper = size.y - baseHeight;
        var upperSize = new Vector3(size.x, upper, size.z);
        var top = Box(group, "Upper", new Vector3(centre.x, bottom + baseHeight + upper / 2f, centre.z), upperSize, facade, new Vector2(16f, 14f));
        top.GetComponent<MeshFilter>().sharedMesh = BoxMesh(upperSize, new Vector2(16f, 14f), fronts);
        top.GetComponent<MeshRenderer>().sharedMaterials = new[] { facade, back };
        return group.gameObject;
    }

    internal static Mesh BoxMesh(Vector3 size, Vector2 tile, Vector3[] fronts = null)
    {
        string key = $"{size.x:0.###}_{size.y:0.###}_{size.z:0.###}_{tile.x:0.##}_{tile.y:0.##}";
        if (fronts != null) key += "_F" + string.Join(",", fronts.Select(f => f.ToString("0")));
        if (boxMeshes.TryGetValue(key, out var cached)) return cached;
        Vector3 h = size / 2f;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        var backTris = new List<int>();
        void Face(Vector3 n, Vector3 u, Vector3 v, float uLen, float vLen)
        {
            int i = verts.Count;
            Vector3 c = Vector3.Scale(n, h);
            Vector3 du = u * (uLen / 2f), dv = v * (vLen / 2f);
            verts.Add(c - du - dv); verts.Add(c + du - dv); verts.Add(c + du + dv); verts.Add(c - du + dv);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(uLen / tile.x, 0)); uvs.Add(new Vector2(uLen / tile.x, vLen / tile.y)); uvs.Add(new Vector2(0, vLen / tile.y));
            for (int k = 0; k < 4; k++) normals.Add(n);
            bool front = fronts == null || fronts.Any(f => Vector3.Dot(f, n) > 0.9f);
            (front ? tris : backTris).AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
        var mesh = new Mesh { name = "Box_" + key };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        if (fronts == null) mesh.SetTriangles(tris, 0);
        else
        {
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris, 0);
            mesh.SetTriangles(backTris, 1);
        }
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        boxMeshes[key] = mesh;
        return mesh;
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
