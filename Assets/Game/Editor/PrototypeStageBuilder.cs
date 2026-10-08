using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// Stage01을 생성한다: 탁 트인 지형 필드를 일직선 경로로 잇는다 —
/// 오아시스 → 협곡 통로 → 초원 → 오르막 통로 → 폐허 고원(보스).
/// 벽 대신 자연 절벽이 플레이 공간을 둘러싼다. 여기 숫자를 조정한 뒤 다시 실행한다.
/// </summary>
public static class PrototypeStageBuilder
{
    const string Root = "Assets/Game";
    const string SceneDir = Root + "/Scenes";
    const string ScenePath = SceneDir + "/Stage01.unity";

    // 월드 공간에서 지형이 차지하는 범위.
    const float OriginX = -130f, OriginZ = -40f, SizeX = 260f, SizeZ = 440f, MaxHeight = 60f;
    const int HeightRes = 1025, AlphaRes = 512;

    // +Z 방향 계곡 단면: (z, 플레이 가능한 바닥의 절반 폭, 바닥 높이).
    static readonly Vector3[] Knots =
    {
        new(-40, 0, 2), new(-8, 0, 2), new(6, 24, 2), new(22, 50, 2), new(92, 54, 2),
        new(108, 14, 2.5f), new(146, 13, 3f), new(162, 48, 3f), new(250, 46, 3.5f),
        new(266, 12, 4f), new(298, 11, 8f), new(314, 38, 8f), new(370, 38, 8f), new(388, 0, 8f), new(400, 0, 8f),
    };

    static readonly Vector2 PondCenter = new(32f, 58f);
    const float PondRadius = 17f, WaterLevel = 1.7f;
    static readonly Vector2 BossCenter = new(PathX(342f), 342f);

    static Terrain terrain;
    static Material sandstoneMat, rockMat, barkMat, leafMat, bushMat;

    [MenuItem("Game/Build Prototype Stage")]
    static void BuildFromMenu() => Build(true);

    public static void Build(bool confirm)
    {
        if (confirm)
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Stage01", "Stage01 씬을 새로 생성해 덮어씁니다.", "덮어쓰기", "취소")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        }

        foreach (var dir in new[] { "Scenes", "Materials", "Prefabs", "Textures" }) EnsureFolder($"{Root}/{dir}");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var rng = new Random(20260930);

        // ---------- 머티리얼 ----------
        sandstoneMat = Mat("Sandstone", new Color(0.86f, 0.74f, 0.55f));
        rockMat = Mat("Rock", new Color(0.56f, 0.50f, 0.44f));
        barkMat = Mat("PalmBark", new Color(0.45f, 0.35f, 0.24f));
        leafMat = Mat("PalmLeaf", new Color(0.24f, 0.46f, 0.16f));
        bushMat = Mat("Bush", new Color(0.30f, 0.50f, 0.20f));
        var playerMat = Mat("PlayerBody", new Color(0.08f, 0.08f, 0.09f));
        var swordMat = Mat("Sword", new Color(0.80f, 0.80f, 0.82f));
        var gruntMat = Mat("Grunt", new Color(0.62f, 0.45f, 0.28f));
        var heavyMat = Mat("Heavy", new Color(0.45f, 0.36f, 0.26f));
        var bossMat = Mat("Boss", new Color(0.30f, 0.26f, 0.22f));
        var eyeMat = Mat("EnemyEye", new Color(4f, 1.2f, 0.4f), unlit: true);
        var aoeMat = Mat("AoEWarning", new Color(0.9f, 0.2f, 0.1f), unlit: true);
        var waterMat = Transparent(Mat("Water", new Color(0.16f, 0.52f, 0.60f, 0.72f)));
        waterMat.SetFloat("_Smoothness", 0.95f);
        var barrierMat = Transparent(Mat("Barrier", new Color(0.6f, 1.4f, 1.8f, 0.28f), unlit: true));

        // ---------- 프리팹 ----------
        var playerPrefab = CreatePlayerPrefab(playerMat, swordMat);
        var gruntPrefab = CreateEnemyPrefab("Grunt", PrimitiveType.Capsule, 1f, gruntMat, eyeMat, null, (ai, hp, agent) =>
        {
            ai.displayName = "Grunt";
            hp.maxHealth = 40;
            ai.damage = 10;
            agent.speed = 3.6f;
        });
        var heavyPrefab = CreateEnemyPrefab("Heavy", PrimitiveType.Cube, 1.4f, heavyMat, eyeMat, null, (ai, hp, agent) =>
        {
            ai.displayName = "Heavy";
            hp.maxHealth = 130;
            ai.damage = 22;
            ai.windup = 0.9f;
            ai.recover = 0.9f;
            ai.attackCooldown = 1.6f;
            ai.attackRange = 2.4f;
            ai.staggerable = false;
            agent.speed = 2.4f;
            agent.stoppingDistance = 1.8f;
        });
        var bossPrefab = CreateEnemyPrefab("Boss_Goliath", PrimitiveType.Capsule, 2.6f, bossMat, eyeMat, aoeMat, (ai, hp, agent) =>
        {
            ai.displayName = "GOLIATH-01";
            ai.isBoss = true;
            hp.maxHealth = 450;
            ai.damage = 24;
            ai.windup = 0.75f;
            ai.recover = 0.8f;
            ai.attackCooldown = 1.2f;
            ai.attackRange = 3.6f;
            ai.attackAngle = 80f;
            ai.staggerable = false;
            ai.aoeEvery = 3;
            ai.aoeRadius = 6.5f;
            ai.aoeWindup = 1.2f;
            ai.aoeDamage = 32;
            agent.speed = 3.2f;
            agent.stoppingDistance = 2.8f;
        });

        // ---------- 지형 + 배경물 (Level 아래의 모든 것은 NavMesh에 베이크된다) ----------
        var level = new GameObject("Level").transform;
        terrain = CreateTerrain(level);

        float waterDiameter = (PondRadius + 5f) * 2f;
        var water = Visual(PrimitiveType.Cylinder, level, "Oasis_Water", new Vector3(PondCenter.x, WaterLevel, PondCenter.y),
            new Vector3(waterDiameter, 0.001f, waterDiameter), waterMat);
        water.shadowCastingMode = ShadowCastingMode.Off;

        var scenery = Child(level, "Scenery");
        PlaceVegetation(scenery, rng);
        PlaceRocks(scenery, rng);
        PlaceRuins(scenery, rng);

        // ---------- 캐릭터 ----------
        var player = Spawn<PlayerController>(playerPrefab, null, Ground(PathX(8f), 8f), 0f);
        var enemies = new GameObject("Enemies").transform;

        EnemyAI[] Group(Vector2 center, params GameObject[] prefabs)
        {
            var result = new EnemyAI[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / prefabs.Length;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (prefabs.Length > 1 ? 3.5f : 0f);
                result[i] = Spawn<EnemyAI>(prefabs[i], enemies, Ground(center.x + offset.x, center.y + offset.y), 180f);
            }
            return result;
        }

        // ---------- 교전 구역 & 차단벽 ----------
        var zones = new GameObject("Encounters").transform;
        var barriers = new GameObject("Barriers").transform;

        var oasis = new[]
        {
            Encounter(zones, new Vector2(PathX(40) - 18f, 40f), Group(new Vector2(PathX(40) - 18f, 40f), gruntPrefab, gruntPrefab)),
            Encounter(zones, new Vector2(46f, 86f), Group(new Vector2(46f, 86f), gruntPrefab, gruntPrefab, gruntPrefab)),
            Encounter(zones, new Vector2(PathX(98), 98f), Group(new Vector2(PathX(98), 98f), gruntPrefab, gruntPrefab, gruntPrefab)),
        };
        var grassland = new[]
        {
            Encounter(zones, new Vector2(PathX(180) - 10f, 180f), Group(new Vector2(PathX(180) - 10f, 180f), gruntPrefab, gruntPrefab, gruntPrefab)),
            Encounter(zones, new Vector2(PathX(215) + 15f, 215f), Group(new Vector2(PathX(215) + 15f, 215f), heavyPrefab, gruntPrefab, gruntPrefab)),
            Encounter(zones, new Vector2(PathX(245), 245f), Group(new Vector2(PathX(245), 245f), heavyPrefab, gruntPrefab)),
        };

        var canyonBarrier = Barrier(barriers, "Barrier_Canyon", 126f, 60f, barrierMat, false);
        canyonBarrier.requiredZones = oasis;
        canyonBarrier.objective = "오아시스의 기계 무리를 소탕하라";
        canyonBarrier.openMessage = "협곡의 결계가 해제되었다";

        var plateauBarrier = Barrier(barriers, "Barrier_Plateau", 282f, 56f, barrierMat, false);
        plateauBarrier.requiredZones = grassland;
        plateauBarrier.objective = "초원의 기계 무리를 소탕하라";
        plateauBarrier.openMessage = "고원으로 향하는 결계가 해제되었다";

        var bossLock = Barrier(barriers, "Barrier_BossLock", 304f, 70f, barrierMat, true);
        var bossZone = Encounter(zones, BossCenter, Group(BossCenter, bossPrefab), 26f);
        bossZone.zoneName = "GOLIATH-01";
        bossZone.isFinal = true;
        bossZone.closeOnEnter = new[] { bossLock };
        bossZone.checkpoint = Child(bossZone.transform, "Checkpoint");
        bossZone.checkpoint.position = Ground(PathX(318f), 318f);

        var systems = new GameObject("GameSystems");
        var gm = systems.AddComponent<GameManager>();
        gm.player = player;
        gm.progression = new[] { canyonBarrier, plateauBarrier };
        gm.finalZone = bossZone;
        gm.finalObjective = "고원의 유적에서 대형 기계를 파괴하라";
        systems.AddComponent<HUD>().player = player;
        systems.AddComponent<RewindEffect>();

        // ---------- 습득 테이프: 되감기 전용, 미리 충전됨, 다시 생기지 않는다(되감기 후에도) ----------
        var pickups = new GameObject("TapePickups").transform;
        var shellMat = Mat("TapeShell", new Color(0.12f, 0.12f, 0.13f));
        var labelMat = Mat("TapeLabel", new Color(0.92f, 0.86f, 0.70f));
        TapePickupAt(pickups, 47f, 66f, shellMat, labelMat, 1, "City Pop '86", "Summer Static", "Tokyo Taxi", "Pastel Radio");
        TapePickupAt(pickups, 10f, 50f, shellMat, labelMat, 2, "Rainy Motel", "Room 204", "Neon Vacancy", "Checkout Time");
        TapePickupAt(pickups, PathX(102f) + 6f, 102f, shellMat, labelMat, 1, "B-Side Blues", "Dust on the Needle", "Two Minute Warning", "Borrowed Time");
        TapePickupAt(pickups, PathX(204f) + 10f, 204f, shellMat, labelMat, 3, "Midnight FM", "Call-in Hour", "Static Lullaby", "Sign Off");
        TapePickupAt(pickups, PathX(292f) + 4f, 292f, shellMat, labelMat, 2, "Dead Air", "Silence", "Rewind", "Play");

        // ---------- 테이프 상점: 재생 가능한(충전 가능한) 테이프의 유일한 입수처 ----------
        var shops = new GameObject("TapeShops").transform;
        var boothMat = Mat("ShopBooth", new Color(0.20f, 0.36f, 0.40f));
        var awningMat = Mat("ShopAwning", new Color(0.85f, 0.35f, 0.45f));
        TapeShopAt(shops, PathX(140f) + 5f, 140f, "Canyon", "협곡 테이프 가게", boothMat, awningMat,
            ("Sunset Boulevard", new[] { "Palm Shade", "Cruise Control", "Last Light" }),
            ("Analog Heart", new[] { "Needle Drop", "Warm Static", "Side A Ends", "Flip Side" }),
            ("Cheap Thrills", new[] { "Coin Slot", "Arcade Glow" }));
        TapeShopAt(shops, PathX(297f) - 5f, 297f, "Plateau", "고원 테이프 가게", boothMat, awningMat,
            ("Last Stand Mix", new[] { "Deep Breath", "Heartbeat", "No Return" }),
            ("Lullaby Tape", new[] { "Hush", "Night Light", "Morning" }));

        SetupEnvironment(player.transform);
        BakeNavMesh(level.gameObject);

        EditorSceneManager.SaveScene(scene, ScenePath);
        var buildScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[PrototypeStageBuilder] Built {ScenePath}");
    }

    // ---------- 지형 ----------

    static float PathX(float z) => 16f * Mathf.Sin(z / 55f);

    static void Profile(float z, out float halfWidth, out float floor)
    {
        int i = 1;
        while (i < Knots.Length - 1 && z > Knots[i].x) i++;
        Vector3 a = Knots[i - 1], b = Knots[i];
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.x, b.x, z));
        halfWidth = Mathf.Lerp(a.y, b.y, t);
        floor = Mathf.Lerp(a.z, b.z, t);
    }

    /// <summary>계곡 경계를 넘어선 부호 있는 거리: 플레이 가능한 바닥 안쪽이면 음수, 언덕 쪽이면 양수이다.</summary>
    static float Edge(float x, float z)
    {
        Profile(z, out float halfWidth, out _);
        float wobble = Mathf.Min(14f, halfWidth * 0.5f) * (Mathf.PerlinNoise(x * 0.04f + 3.1f, z * 0.04f + 7.7f) - 0.5f) * 2f;
        return Mathf.Abs(x - PathX(z)) - halfWidth + wobble;
    }

    static float Height(float x, float z)
    {
        Profile(z, out _, out float floor);
        float rolling = (Mathf.PerlinNoise(x * 0.025f + 20f, z * 0.025f + 20f) - 0.5f) * 2.4f;
        rolling *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 32f, Vector2.Distance(new Vector2(x, z), BossCenter)));
        float h = floor + rolling;

        float edge = Edge(x, z);
        if (edge > 0f)
        {
            float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.02f + 50f, z * 0.02f + 50f) * 2f - 1f);
            h += 16f * (1f - Mathf.Exp(-edge / 5f)) + edge * 0.12f + ridge * 8f * Mathf.Clamp01(edge / 15f);
        }

        float pond = Vector2.Distance(new Vector2(x, z), PondCenter);
        h = Mathf.Lerp(h, WaterLevel - 1.2f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PondRadius + 7f, PondRadius - 5f, pond)));
        return Mathf.Clamp(h, 0f, MaxHeight - 1f);
    }

    static Terrain CreateTerrain(Transform parent)
    {
        var sand = Layer("Sand", NoiseTexture("Sand", new Color(0.80f, 0.70f, 0.52f), new Color(0.91f, 0.82f, 0.63f), 0.04f, 0.12f, 0.1f), 5f);
        var grass = Layer("Grass", NoiseTexture("Grass", new Color(0.24f, 0.38f, 0.13f), new Color(0.42f, 0.56f, 0.22f), 0.06f, 0.3f, 0.1f), 4f);
        var rock = Layer("Rock", NoiseTexture("Rock", new Color(0.44f, 0.39f, 0.34f), new Color(0.68f, 0.61f, 0.52f), 0.025f, 0.15f, 0.15f), 9f);
        var wet = Layer("WetSand", NoiseTexture("WetSand", new Color(0.50f, 0.42f, 0.30f), new Color(0.61f, 0.53f, 0.39f), 0.05f, 0.1f, 0.45f), 5f);

        string path = $"{SceneDir}/Stage01_Terrain.asset";
        AssetDatabase.DeleteAsset(path);
        var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = AlphaRes };
        data.size = new Vector3(SizeX, MaxHeight, SizeZ);
        // 먼저 에셋으로 저장한다: 그래야 이후에 만든 splat 텍스처가 하위 에셋으로 저장되고, 아니면 칠한 내용이 사라진다.
        AssetDatabase.CreateAsset(data, path);

        var heights = new float[HeightRes, HeightRes];
        for (int iz = 0; iz < HeightRes; iz++)
        for (int ix = 0; ix < HeightRes; ix++)
            heights[iz, ix] = Height(OriginX + ix / (HeightRes - 1f) * SizeX, OriginZ + iz / (HeightRes - 1f) * SizeZ) / MaxHeight;
        data.SetHeights(0, 0, heights);

        data.terrainLayers = new[] { sand, grass, rock, wet };
        data.SetAlphamaps(0, 0, PaintSplat(data));
        EditorUtility.SetDirty(data);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(OriginX, 0f, OriginZ);
        var t = go.GetComponent<Terrain>();
        t.materialTemplate = Mat("TerrainLit", Color.white, shader: "Universal Render Pipeline/Terrain/Lit");
        t.heightmapPixelError = 3f;
        t.basemapDistance = 300f;
        return t;
    }

    static float[,,] PaintSplat(TerrainData data)
    {
        int res = data.alphamapResolution;
        var maps = new float[res, res, 4]; // 모래, 풀, 바위, 젖은 모래
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float nx = (x + 0.5f) / res, nz = (y + 0.5f) / res;
            float wx = OriginX + nx * SizeX, wz = OriginZ + nz * SizeZ;
            float h = data.GetInterpolatedHeight(nx, nz);

            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 38f, data.GetSteepness(nx, nz)));
            float shore = Mathf.InverseLerp(PondRadius + 14f, PondRadius + 4f, Vector2.Distance(new Vector2(wx, wz), PondCenter));
            float wet = shore * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WaterLevel + 0.8f, WaterLevel + 0.1f, h));
            float patch = Mathf.PerlinNoise(wx * 0.06f + 5f, wz * 0.06f + 9f);
            float grass = Mathf.Clamp01((Lushness(wx, wz) + patch - 0.9f) * 3f);

            float open = 1f - rock;
            float wetW = wet * open;
            float grassW = grass * open * (1f - wet);
            maps[y, x, 0] = open - wetW - grassW;
            maps[y, x, 1] = grassW;
            maps[y, x, 2] = rock;
            maps[y, x, 3] = wetW;
        }
        return maps;
    }

    /// <summary>0..1 — 땅이 얼마나 푸른지 나타낸다: 오아시스 주변 고리와 초원 필드 전체가 해당한다.</summary>
    static float Lushness(float x, float z)
    {
        float oasis = Mathf.InverseLerp(PondRadius + 24f, PondRadius + 4f, Vector2.Distance(new Vector2(x, z), PondCenter));
        float grassland = Mathf.InverseLerp(140f, 175f, z) * Mathf.InverseLerp(280f, 245f, z) * 0.95f;
        float plateau = Mathf.InverseLerp(300f, 330f, z) * 0.45f;
        return Mathf.Max(oasis, Mathf.Max(grassland, plateau));
    }

    static Vector3 Ground(float x, float z) => new(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y, z);

    static float Slope(float x, float z)
    {
        var p = terrain.transform.position;
        return terrain.terrainData.GetSteepness((x - p.x) / SizeX, (z - p.z) / SizeZ);
    }

    // ---------- 배경물 ----------

    static void PlaceVegetation(Transform parent, Random rng)
    {
        var palms = Child(parent, "Palms");
        var bushes = Child(parent, "Bushes");

        // 오아시스를 감싸는 야자수 고리.
        for (int i = 0; i < 28; i++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = PondRadius + 1f + (float)rng.NextDouble() * 12f;
            float x = PondCenter.x + Mathf.Cos(angle) * r, z = PondCenter.y + Mathf.Sin(angle) * r;
            if (Edge(x, z) < -2f) PalmTree(palms, Ground(x, z), rng);
        }

        // 흩어진 야자수. 오아시스 필드에서 더 빽빽하다.
        Scatter(rng, 18, 5f, 100f, (x, z) => Edge(x, z) < -6f && !NearPond(x, z, 3f) && Mathf.Abs(x - PathX(z)) > 5f, p => PalmTree(palms, p, rng));
        Scatter(rng, 8, 160f, 255f, (x, z) => Edge(x, z) < -5f && Mathf.Abs(x - PathX(z)) > 6f, p => PalmTree(palms, p, rng));
        Scatter(rng, 5, 316f, 368f, (x, z) => Edge(x, z) is < -2f and > -8f, p => PalmTree(palms, p, rng));

        Scatter(rng, 30, 20f, 100f, (x, z) => Edge(x, z) < -3f && Lushness(x, z) > 0.4f && !NearPond(x, z, 1f), p => Bush(bushes, p, rng));
        Scatter(rng, 30, 160f, 260f, (x, z) => Edge(x, z) < -3f, p => Bush(bushes, p, rng));
    }

    static void PlaceRocks(Transform parent, Random rng)
    {
        var rocks = Child(parent, "Rocks");
        // 언덕 기슭을 따라 놓인 바위가 경계를 부드럽게 한다.
        Scatter(rng, 90, 0f, 385f, (x, z) => Edge(x, z) is > -3f and < 2f, p => Rock(rocks, p, 2f + (float)rng.NextDouble() * 5f, rng));
        // 엄폐용으로 필드 중간에 둔 바위 몇 개.
        Scatter(rng, 24, 10f, 370f, (x, z) => Edge(x, z) < -8f && !NearPond(x, z, 4f) && Slope(x, z) < 15f,
            p => Rock(rocks, p, 0.8f + (float)rng.NextDouble() * 1.8f, rng));
    }

    static void PlaceRuins(Transform parent, Random rng)
    {
        var ruins = Child(parent, "Ruins");

        // 오아시스 건너편 기슭의 사암 마을.
        Building(ruins, 50f, 46f, new Vector3(8f, 9f, 10f), 4f);
        Building(ruins, 55f, 60f, new Vector3(7f, 12f, 8f), -6f);
        Building(ruins, 49f, 73f, new Vector3(9f, 7f, 9f), 10f);
        Building(ruins, 59f, 53f, new Vector3(4f, 17f, 4f), 0f);
        Building(ruins, 44f, 63f, new Vector3(1.2f, 4f, 10f), 5f);

        // 협곡 입구를 감싸는 랜드마크 아치.
        Arch(ruins, 106f, 9f, 12f);

        // 초원을 가로지르는 쓰러진 열주.
        for (float z = 186f; z <= 240f; z += 12f)
        for (int side = -1; side <= 1; side += 2)
        {
            float x = PathX(z) + side * 14f;
            if (rng.NextDouble() < 0.25) continue;
            var column = Block(ruins, "Column", Vector3.zero, new Vector3(1.4f, 2f + (float)rng.NextDouble() * 6f, 1.4f), sandstoneMat);
            column.transform.position = Ground(x, z) + Vector3.up * (column.transform.localScale.y / 2f - 0.3f);
        }

        // 보스 아레나를 둘러싼 부서진 기둥 고리.
        var arena = Child(ruins, "BossArena");
        var disk = Visual(PrimitiveType.Cylinder, arena, "Dais", Ground(BossCenter.x, BossCenter.y) + Vector3.up * 0.02f, new Vector3(40f, 0.04f, 40f), sandstoneMat);
        disk.shadowCastingMode = ShadowCastingMode.Off;
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2f / 10f;
            float x = BossCenter.x + Mathf.Cos(angle) * 24f, z = BossCenter.y + Mathf.Sin(angle) * 24f;
            float h = i % 3 == 0 ? 3f : 7f + (float)rng.NextDouble() * 4f;
            var pillar = Block(arena, "Pillar", Vector3.zero, new Vector3(2.2f, h, 2.2f), sandstoneMat);
            pillar.transform.position = Ground(x, z) + Vector3.up * (h / 2f - 0.3f);
        }
    }

    static void Scatter(Random rng, int count, float zMin, float zMax, Func<float, float, bool> accept, Action<Vector3> place)
    {
        for (int placed = 0, tries = 0; placed < count && tries < count * 40; tries++)
        {
            float z = Mathf.Lerp(zMin, zMax, (float)rng.NextDouble());
            float x = PathX(z) + ((float)rng.NextDouble() * 2f - 1f) * 70f;
            if (!accept(x, z)) continue;
            place(Ground(x, z));
            placed++;
        }
    }

    static bool NearPond(float x, float z, float margin) => Vector2.Distance(new Vector2(x, z), PondCenter) < PondRadius + margin;

    static void PalmTree(Transform parent, Vector3 basePosition, Random rng)
    {
        var tree = Child(parent, "Palm");
        tree.SetPositionAndRotation(basePosition, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));

        const int segments = 6;
        float height = 6f + (float)rng.NextDouble() * 4f;
        float bend = 10f + (float)rng.NextDouble() * 18f;
        float segment = height / segments;
        Vector3 tip = Vector3.zero, dir = Vector3.up;
        for (int i = 0; i < segments; i++)
        {
            float t = i / (segments - 1f);
            dir = Quaternion.Euler(bend / segments * (0.5f + t), 0f, 0f) * dir;
            float radius = Mathf.Lerp(0.26f, 0.15f, t);
            var trunk = Visual(PrimitiveType.Cylinder, tree, "Trunk", tip + dir * (segment / 2f), new Vector3(radius * 2f, segment * 0.53f, radius * 2f), barkMat);
            trunk.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            tip += dir * segment;
        }

        int fronds = 7 + rng.Next(3);
        for (int i = 0; i < fronds; i++)
        {
            var frond = Child(tree, "Frond");
            frond.localPosition = tip;
            frond.localRotation = Quaternion.Euler(0f, i * 360f / fronds + (float)rng.NextDouble() * 20f, 0f)
                                  * Quaternion.Euler(15f + (float)rng.NextDouble() * 25f, 0f, 0f);
            Visual(PrimitiveType.Cube, frond, "Leaf", new Vector3(0f, 0f, 1.5f), new Vector3(0.8f, 0.05f, 3f), leafMat);
            var droop = Child(frond, "Droop");
            droop.localPosition = new Vector3(0f, 0f, 2.9f);
            droop.localRotation = Quaternion.Euler(35f, 0f, 0f);
            Visual(PrimitiveType.Cube, droop, "Leaf", new Vector3(0f, 0f, 0.9f), new Vector3(0.55f, 0.05f, 1.8f), leafMat);
        }
        Visual(PrimitiveType.Sphere, tree, "Coconuts", tip + Vector3.down * 0.25f, Vector3.one * 0.6f, barkMat);

        var collider = tree.gameObject.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1.5f, 0f);
        collider.height = 3f;
        collider.radius = 0.3f;
    }

    static void Bush(Transform parent, Vector3 position, Random rng)
    {
        var bush = Child(parent, "Bush");
        bush.position = position;
        int blobs = 2 + rng.Next(3);
        for (int i = 0; i < blobs; i++)
        {
            float s = 0.8f + (float)rng.NextDouble() * 0.9f;
            var offset = new Vector3((float)rng.NextDouble() - 0.5f, s * 0.3f, (float)rng.NextDouble() - 0.5f) * 1.2f;
            Visual(PrimitiveType.Sphere, bush, "Blob", offset, new Vector3(s, s * 0.7f, s), bushMat);
        }
    }

    static void Rock(Transform parent, Vector3 position, float size, Random rng)
    {
        var go = GameObject.CreatePrimitive(rng.NextDouble() < 0.6 ? PrimitiveType.Sphere : PrimitiveType.Cube);
        go.name = "Rock";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        go.transform.SetParent(parent, false);
        float Jitter(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        go.transform.localScale = new Vector3(size * Jitter(0.8f, 1.4f), size * Jitter(0.45f, 0.9f), size * Jitter(0.8f, 1.4f));
        go.transform.SetPositionAndRotation(position + Vector3.up * (go.transform.localScale.y * 0.25f),
            Quaternion.Euler(Jitter(-15f, 15f), Jitter(0f, 360f), Jitter(-15f, 15f)));
        go.GetComponent<Renderer>().sharedMaterial = rockMat;
        go.isStatic = true;
    }

    static void Building(Transform parent, float x, float z, Vector3 size, float yaw)
    {
        var go = Block(parent, "Building", Vector3.zero, size, sandstoneMat);
        go.transform.SetPositionAndRotation(Ground(x, z) + Vector3.up * (size.y / 2f - 0.5f), Quaternion.Euler(0f, yaw, 0f));
    }

    static void Arch(Transform parent, float z, float halfSpan, float height)
    {
        var arch = Child(parent, "CanyonArch");
        float cx = PathX(z);
        for (int side = -1; side <= 1; side += 2)
        {
            var pillar = Block(arch, "Pillar", Vector3.zero, new Vector3(3f, height + 2f, 3f), sandstoneMat);
            pillar.transform.position = Ground(cx + side * halfSpan, z) + Vector3.up * (height / 2f);
        }
        float top = Mathf.Max(Ground(cx - halfSpan, z).y, Ground(cx + halfSpan, z).y) + height + 1f;
        Block(arch, "Lintel", new Vector3(cx, top, z), new Vector3(halfSpan * 2f + 4f, 2.5f, 3.4f), sandstoneMat);
    }

    static ZoneGate Barrier(Transform parent, string name, float z, float width, Material mat, bool startOpen)
    {
        const float height = 12f;
        var center = Ground(PathX(z), z);
        var go = Block(parent, name, center + Vector3.up * 3f, new Vector3(width, height, 0.6f), mat);
        go.isStatic = false;
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var gate = go.AddComponent<ZoneGate>();
        gate.startOpen = startOpen;
        gate.sinkDepth = height + 2f;
        return gate;
    }

    /// <param name="id">오브젝트 이름에 쓰는 영어 ID (한글 이름은 문제를 일으킬 수 있어 쓰지 않는다)</param>
    /// <param name="displayName">게임 화면에 보이는 가게 이름</param>
    static void TapeShopAt(Transform parent, float x, float z, string id, string displayName, Material booth, Material awning,
        params (string title, string[] tracks)[] items)
    {
        var root = new GameObject($"Shop_{id}").transform;
        root.SetParent(parent, false);
        var toRoad = new Vector3(PathX(z) - x, 0f, 0f);
        root.SetPositionAndRotation(Ground(x, z), Quaternion.LookRotation(toRoad.sqrMagnitude > 0.01f ? toRoad.normalized : Vector3.back));

        Block(root, "Booth", new Vector3(0f, 1.5f, -0.6f), new Vector3(3.4f, 3f, 1.6f), booth);
        Block(root, "Counter", new Vector3(0f, 0.55f, 0.6f), new Vector3(3.4f, 1.1f, 0.8f), booth);
        Block(root, "Awning", new Vector3(0f, 3.1f, 0.5f), new Vector3(3.8f, 0.12f, 2.2f), awning)
            .transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);

        var sign = new GameObject("Sign").AddComponent<TextMesh>();
        sign.transform.SetParent(root, false);
        sign.transform.localPosition = new Vector3(0f, 2.35f, 0.22f);
        sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TextMesh는 -Z 쪽에서 읽히므로 도로를 향하게 한다
        sign.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sign.GetComponent<MeshRenderer>().sharedMaterial = sign.font.material;
        sign.text = "CASSETTES";
        sign.fontSize = 64;
        sign.characterSize = 0.05f;
        sign.anchor = TextAnchor.MiddleCenter;
        sign.color = new Color(1f, 0.55f, 0.75f);

        var neon = new GameObject("Neon").AddComponent<Light>();
        neon.transform.SetParent(root, false);
        neon.transform.localPosition = new Vector3(0f, 2.6f, 1.6f);
        neon.type = LightType.Point;
        neon.range = 7f;
        neon.intensity = 2.5f;
        neon.color = new Color(1f, 0.45f, 0.7f);

        var trigger = root.gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1f, 1.8f);
        trigger.radius = 2.5f;

        var shop = root.gameObject.AddComponent<TapeShop>();
        shop.shopName = displayName;
        foreach (var (title, tracks) in items)
            shop.stock.Add(new TapeShop.Item { tape = new CassetteTape { title = title, trackTitles = tracks } });
    }

    static void TapePickupAt(Transform parent, float x, float z, Material shell, Material label, int charges, string title, params string[] tracks)
    {
        var go = new GameObject($"Tape_{title}");
        go.transform.SetParent(parent, false);
        go.transform.position = Ground(x, z) + Vector3.up * 0.9f;
        var trigger = go.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.3f;

        var cassette = Child(go.transform, "Cassette");
        Visual(PrimitiveType.Cube, cassette, "Shell", Vector3.zero, new Vector3(0.5f, 0.32f, 0.07f), shell);
        Visual(PrimitiveType.Cube, cassette, "Label", new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.13f, 0.075f), label);
        for (int side = -1; side <= 1; side += 2)
            Visual(PrimitiveType.Cylinder, cassette, "Reel", new Vector3(side * 0.1f, -0.06f, 0f), new Vector3(0.08f, 0.04f, 0.08f), label)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(go.transform, false);
        glow.type = LightType.Point;
        glow.range = 3f;
        glow.intensity = 2f;
        glow.color = new Color(1f, 0.8f, 0.45f);

        var pickup = go.AddComponent<TapePickup>();
        pickup.visual = cassette;
        pickup.tape = new CassetteTape { title = title, trackTitles = tracks, playable = false, charges = charges };
    }

    static CombatZone Encounter(Transform parent, Vector2 center, EnemyAI[] enemies, float radius = 16f)
    {
        var go = new GameObject("Encounter");
        go.transform.SetParent(parent, false);
        go.transform.position = Ground(center.x, center.y) + Vector3.up;
        var sphere = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = radius;
        var zone = go.AddComponent<CombatZone>();
        zone.enemies = enemies;
        return zone;
    }

    // ---------- 프리팹 ----------

    // 바로크로 만든 주인공(휴머노이드). BaroqueCharacterSetup이 Starter Assets 걷기·달리기 애니메이터를 붙여 둔다
    const string ProtagonistPrefabPath = BaroqueCharacterSetup.CharacterRoot + "/Protagonist/Protagonist_Model.prefab";
    const string StarterSfxPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Sfx";

    static GameObject CreatePlayerPrefab(Material guard, Material sword)
    {
        var root = new GameObject("Player") { tag = "Player" };
        var controller = root.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0, 0.93f, 0);
        controller.stepOffset = 0.25f;
        var health = root.AddComponent<Health>();
        health.team = Team.Player;
        health.invulnerableAfterHit = 0.6f;

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ProtagonistPrefabPath);
        if (modelAsset == null) throw new InvalidOperationException($"주인공 모델 프리팹이 없음: {ProtagonistPrefabPath} (Game/Baroque/Rebuild Character Prefabs 먼저 실행)");
        var model = Object.Instantiate(modelAsset, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        // 모델에 조작 스크립트(예: 예전 Kyle의 ThirdPersonController)가 섞여 있으면 혼자 걸어가고 애니메이터 값을 덮어쓴다.
        // 어떤 모델로 바꾸든 안전하도록 애니메이터만 남기고 모두 떼어 낸다.
        StripToAnimator(model);
        HeadphoneBandSetup.Attach(model); // 목에 걸린 헤드폰의 밴드(생성 모델에는 귀 덮개만 있다)
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.tag = "Untagged"; // "Player" 태그는 몸통에만
        var animator = model.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var events = model.AddComponent<PlayerAnimationEvents>();
        events.footsteps = Enumerable.Range(1, 10)
            .Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"{StarterSfxPath}/Player_Footstep_{i:00}.wav"))
            .Where(clip => clip != null).ToArray();
        events.landing = AssetDatabase.LoadAssetAtPath<AudioClip>($"{StarterSfxPath}/Player_Land.wav");

        var pivot = Child(model.transform, "WeaponPivot");
        pivot.localPosition = new Vector3(0, 1.25f, 0);
        Visual(PrimitiveType.Cube, pivot, "Blade", new Vector3(0, 0, 0.8f), new Vector3(0.05f, 0.16f, 1.3f), sword);
        Visual(PrimitiveType.Cube, pivot, "Guard", new Vector3(0, 0, 0.12f), new Vector3(0.08f, 0.36f, 0.06f), guard);

        var tint = root.AddComponent<BodyTint>();
        tint.renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        tint.flashColor = new Color(2.5f, 2.5f, 2.5f); // 텍스처가 있는 모델이라 HDR 흰색(1 초과)이어야 실제로 밝게 번쩍인다
        var player = root.AddComponent<PlayerController>();
        player.model = model.transform;
        player.weaponPivot = pivot;
        player.animator = animator;
        // 주인공 자신의 믹스테이프: 재생용이라 들을수록 되감기 횟수가 충전된다
        root.AddComponent<Walkman>().tapes.Add(new CassetteTape
        {
            title = "Walkman Mix Vol.1",
            trackTitles = new[] { "Night Drive", "Neon Rain", "Last Call" },
        });
        return SavePrefab(root);
    }

    /// <summary>
    /// Transform과 Animator를 뺀 모든 부품을 제거한다. 다른 부품이 필요로 하는 부품(RequireComponent)은
    /// 지울 수 없으므로, 아무도 필요로 하지 않는 부품부터 여러 차례에 걸쳐 지운다.
    /// </summary>
    static void StripToAnimator(GameObject go)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            var leftovers = go.GetComponents<Component>().Where(c => c is not Transform && c is not Animator).ToList();
            if (leftovers.Count == 0) return;
            foreach (var component in leftovers)
            {
                bool neededByAnother = go.GetComponents<Component>().Any(other =>
                    other != null && other != component && RequiredTypes(other.GetType()).Any(t => t.IsInstanceOfType(component)));
                if (!neededByAnother) Object.DestroyImmediate(component);
            }
        }
        throw new InvalidOperationException($"{go.name}: 부품을 모두 제거하지 못했다");
    }

    static IEnumerable<Type> RequiredTypes(Type type) =>
        type.GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
            .SelectMany(r => new[] { r.m_Type0, r.m_Type1, r.m_Type2 }).Where(t => t != null);

    static GameObject CreateEnemyPrefab(string name, PrimitiveType shape, float scale, Material body, Material eye,
        Material aoe, Action<EnemyAI, Health, NavMeshAgent> configure)
    {
        var root = new GameObject(name);
        var collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0, 1, 0);
        collider.height = 2f;
        collider.radius = 0.5f;

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.angularSpeed = 540f;
        agent.acceleration = 24f;
        agent.stoppingDistance = 1.2f;
        agent.enabled = false; // NavMesh가 등록된 뒤 EnemyAI가 Start에서 켠다

        var health = root.AddComponent<Health>();
        health.team = Team.Enemy;

        var bodyScale = shape == PrimitiveType.Cube ? new Vector3(1f, 2f, 1f) : Vector3.one;
        var bodyRenderer = Visual(shape, root.transform, "Body", new Vector3(0, 1, 0), bodyScale, body);
        Visual(PrimitiveType.Sphere, root.transform, "Eye", new Vector3(0, 1.5f, 0.45f), Vector3.one * 0.22f, eye);
        root.AddComponent<BodyTint>().renderers = new[] { bodyRenderer };

        var ai = root.AddComponent<EnemyAI>();
        if (aoe)
            ai.aoeIndicator = Visual(PrimitiveType.Cylinder, root.transform, "AoEIndicator", new Vector3(0, 0.03f, 0), new Vector3(1, 0.005f, 1), aoe).transform;

        root.transform.localScale = Vector3.one * scale;
        configure(ai, health, agent);
        return SavePrefab(root);
    }

    static GameObject SavePrefab(GameObject go)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{go.name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static T Spawn<T>(GameObject prefab, Transform parent, Vector3 position, float yaw) where T : Component
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        return go.GetComponent<T>();
    }

    // ---------- 환경 ----------

    static void SetupEnvironment(Transform player)
    {
        var sun = Object.FindAnyObjectByType<Light>();
        sun.transform.rotation = Quaternion.Euler(48f, -40f, 0f);
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.Soft;

        var sky = Mat("Sky", Color.white, shader: "Skybox/Procedural");
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_AtmosphereThickness", 0.75f);
        sky.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.85f));
        sky.SetColor("_GroundColor", new Color(0.55f, 0.50f, 0.45f));
        sky.SetFloat("_Exposure", 1.25f);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;

        // 옅은 대기 연무를 깔아 먼 능선이 안개 벽 대신 푸르게 흐려지게 한다.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.0035f;
        RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.88f);
        DynamicGI.UpdateEnvironment();

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if (profile)
        {
            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        var cam = Object.FindAnyObjectByType<Camera>();
        cam.farClipPlane = 800f;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        cam.transform.SetPositionAndRotation(player.position + new Vector3(0, 3f, -5.5f), Quaternion.Euler(15f, 0f, 0f));
        cam.gameObject.AddComponent<ThirdPersonCamera>().target = player;
    }

    static void BakeNavMesh(GameObject level)
    {
        var surface = level.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.3f;
        surface.BuildNavMesh();

        string path = $"{SceneDir}/Stage01_NavMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
    }

    // ---------- 에셋 ----------

    static Material Mat(string name, Color color, bool unlit = false, string shader = null)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(shader ?? (unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")));
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (!unlit && shader == null) mat.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material Transparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    /// <summary>타일링 가능한 value-noise 텍스처로, 한 번 생성해 에셋으로 캐시한다.</summary>
    static Texture2D NoiseTexture(string name, Color dark, Color light, float frequency, float speckle, float smoothness)
    {
        string path = $"{Root}/Textures/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing) return existing;

        const int size = 256;
        var rng = new Random(name.GetHashCode());
        float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = 0.55f * Tileable(x, y, size, frequency, ox, oy)
                    + 0.30f * Tileable(x, y, size, frequency * 2f, ox + 17f, oy + 31f)
                    + 0.15f * Tileable(x, y, size, frequency * 4f, ox + 43f, oy + 7f);
            Color c = Color.Lerp(dark, light, Mathf.SmoothStep(0.25f, 0.75f, n));
            c *= 1f + ((float)rng.NextDouble() - 0.5f) * speckle;
            c.a = smoothness;
            pixels[y * size + x] = c;
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        tex.SetPixels(pixels);
        tex.Apply(true);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    /// <summary>네 가지 랩 오프셋에 걸쳐 섞은 Perlin 노이즈로, 마주 보는 가장자리가 서로 맞물린다.</summary>
    static float Tileable(float x, float y, float size, float frequency, float ox, float oy)
    {
        float Sample(float px, float py) => Mathf.PerlinNoise(ox + px * frequency, oy + py * frequency);
        float u = x / size, v = y / size;
        return Sample(x, y) * (1 - u) * (1 - v) + Sample(x - size, y) * u * (1 - v)
             + Sample(x, y - size) * (1 - u) * v + Sample(x - size, y - size) * u * v;
    }

    static TerrainLayer Layer(string name, Texture2D texture, float tile)
    {
        string path = $"{Root}/Textures/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = texture;
        layer.tileSize = new Vector2(tile, tile);
        EditorUtility.SetDirty(layer);
        return layer;
    }

    // ---------- 헬퍼 ----------

    static GameObject Block(Transform parent, string name, Vector3 position, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    static Renderer Visual(PrimitiveType type, Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = mat;
        return renderer;
    }

    static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
