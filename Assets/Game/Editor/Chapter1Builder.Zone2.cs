using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using P = PrologueBuilder;

/// <summary>
/// 1장 구역 2 「시장 광장」. 큰길 북쪽 끝이 그대로 넓은 광장으로 열린다(벽 대신 건물로 둘러싼 뻥 뚫린 마당).
///
/// 배치(미터):
///   광장: x 126~202, z 166~238. 가운데로 큰길 찻길(x 158~170)이 그대로 지나 북쪽 항구 길(z 238~300)로 이어진다.
///   검문소(입구, z 168~182): 순찰차 두 대·바리케이드·투광등, 방패 경찰 둘 + 손전등 경찰 하나. 조직의 검은 승합차가 그냥 통과한다(컷신).
///   서쪽 반: 밤 시장 노점 네 줄(줄무늬 차양, 뒤 방수포는 사람 키를 넘는 엄폐물). 줄 사이 통로와 가로 통로 둘.
///   동쪽 반: 짐 부리는 곳(트럭·포장마차·팔레트·드럼통). 저격수가 내려다보는 넓은 마당.
///   동쪽 건물(x 202~216, z 192~238, 높이 9m) 옥상: 저격수(경찰). 남쪽 골목(z 188~192)의 지그재그 철계단으로 올라간다.
///   북쪽 항구 길 z 262에서 (이어서 계속).
/// </summary>
public static partial class Chapter1Builder
{
    const float SqX0 = 126f, SqX1 = 202f, SqZ0 = 166f, SqZ1 = 238f;
    const float RoofY = 9f;
    const float BldX0 = 202f, BldX1 = 216f;          // 저격수 건물(동쪽)
    const float AlleyZ0 = 188f, AlleyZ1 = 192f;       // 계단 골목

    static void BuildMarketSquare(Dictionary<string, Material> M, Dictionary<string, Material> X, Transform triggers, Transform people, Transform routes, Built b)
    {
        var g = P.Group("MarketSquare");
        var Z = Zone2Materials();
        Zone2Ground(g, M, Z);
        Zone2Buildings(g, M, X, Z);
        Checkpoint(g, M, Z, triggers, people, routes, b);
        Market(g, M, Z, people, routes);
        LoadingArea(g, triggers);
        SniperBuilding(g, M, Z, triggers, people, b);
        Zone2Lights(g, M, Z);

        // 구역 2 시작(검문소를 엿보는 자리, 세워 둔 차 뒤) — 자동 저장
        P.Prop("Sedan", g, new Vector3(AveRoad0 + 1.5f, 0f, 136f), 0f);
        b.zone2Start = P.Marker(triggers, "Zone2Start", new Vector3(AveW + 1.6f, G, 133.5f), 0f);
        b.checkpointView = b.endTrigger;
        b.checkpointCam = P.Marker(triggers, "CheckpointCam", new Vector3(AveRoad0 + 2.4f, 2.4f, 128.5f), 0f); // 레이 오른쪽 위: 레이는 왼쪽 앞, 검문소·승합차는 가운데(가로등 기둥은 비껴간다)
        b.checkpointLook = P.Marker(triggers, "CheckpointLook", new Vector3(AveX, 1.4f, 172f), 0f);
        b.squareTrigger = Trigger(triggers, "SquareTrigger", new Vector3((SqX0 + SqX1) / 2f, 1.5f, 187f), new Vector3(SqX1 - SqX0, 3f, 4f));
        b.square = new Bounds(new Vector3((SqX0 + SqX1) / 2f, 6f, 211f), new Vector3(SqX1 - SqX0 + 16f, 16f, 50f)); // 검문소 북쪽~광장 끝(옥상 포함)
        b.harborEnd = P.Marker(triggers, "HarborEnd", new Vector3(AveX, 1.4f, 262f), 0f);
        b.harborTrigger = Trigger(triggers, "HarborTrigger", new Vector3(AveX, 1.5f, 262f), new Vector3(AveE - AveW + 2f, 3f, 4f));
    }

    // =====================================================================
    // 재질
    // =====================================================================

    static Dictionary<string, Material> Zone2Materials()
    {
        var Z = new Dictionary<string, Material>();
        Z["Plaza"] = P.Lit("PlazaStone", P.Tex("PlazaStone", 256, PaverPixel), Color.white, 0.62f, smoothnessFromAlpha: true); // 비에 젖은 돌바닥
        Z["CanvasRed"] = Canvas("CanvasRed", new Color(0.62f, 0.12f, 0.1f));
        Z["CanvasBlue"] = Canvas("CanvasBlue", new Color(0.13f, 0.26f, 0.55f));
        Z["CanvasGreen"] = Canvas("CanvasGreen", new Color(0.12f, 0.4f, 0.22f));
        Z["CanvasOrange"] = Canvas("CanvasOrange", new Color(0.78f, 0.38f, 0.08f));
        Z["Tarp"] = P.Lit("TarpBlue", null, new Color(0.11f, 0.2f, 0.38f), 0.45f);
        Z["TarpGrey"] = P.Lit("TarpGrey", null, new Color(0.25f, 0.27f, 0.28f), 0.35f);
        Z["StallTable"] = P.Lit("StallTable", P.Tex("FloorWood", 512, P.WoodPixel), new Color(0.55f, 0.45f, 0.36f), 0.3f);
        Z["Bulb"] = P.Unlit("MarketBulb", new Color(1f, 0.78f, 0.45f) * 3.2f);
        Z["Cable"] = P.Lit("Cable", null, new Color(0.03f, 0.03f, 0.03f), 0.3f);
        Z["Grating"] = P.Lit("StairGrating", null, new Color(0.24f, 0.25f, 0.26f), 0.35f, metallic: 0.7f);
        Z["Parapet"] = P.Lit("Parapet", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.48f, 0.47f, 0.46f), 0.2f);
        Z["RedLens"] = P.Lit("PoliceRedLens", null, new Color(0.3f, 0.02f, 0.02f), 0.6f, emission: new Color(1f, 0.08f, 0.05f) * 0.3f);
        Z["BlueLens"] = P.Lit("PoliceBlueLens", null, new Color(0.02f, 0.05f, 0.3f), 0.6f, emission: new Color(0.1f, 0.25f, 1f) * 0.3f);
        Z["FloodLens"] = P.Unlit("FloodLens", new Color(1f, 0.98f, 0.92f) * 5f);
        // 시장 입구 간판: 가로로 긴 분홍빛 네온 "NIGHT MARKET"
        Z["SignNightMarket"] = Neon("SignNightMarket", "NIGHT MARKET", new Color(1f, 0.42f, 0.55f) * 4.2f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        Z["SignArrow"] = Neon("SignHarbor", "HARBOR  →", new Color(0.4f, 0.85f, 1f) * 3.2f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        return Z;
    }

    /// <summary>젖은 돌바닥: 60cm 네모 판석(줄눈은 짙고 거칠게, 판석은 매끄럽게 — 알파 = 매끄러움)</summary>
    static Color PaverPixel(int x, int y, int size)
    {
        int tile = size / 4;
        int tx = x / tile, ty = y / tile;
        bool grout = (x % tile) < 3 || (y % tile) < 3;
        float tone = 0.78f + 0.14f * P.Hash(tx, ty) + 0.06f * Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
        float v = grout ? 0.32f : 0.48f * tone;
        float wet = grout ? 0.25f : 0.55f + 0.3f * Mathf.PerlinNoise(x * 0.02f + 3f, y * 0.02f);
        return new Color(v, v * 0.98f, v * 0.95f, wet);
    }

    /// <summary>노점 차양 천: 넓은 색 줄무늬와 흰 줄(빗물에 바랜 얼룩 조금)</summary>
    static Material Canvas(string name, Color colour)
    {
        var tex = P.Tex(name, 128, (x, y, s) =>
        {
            bool stripe = (x / 16) % 2 == 0;
            float stain = 0.9f + 0.1f * Mathf.PerlinNoise(x * 0.07f, y * 0.07f);
            var c = stripe ? colour : new Color(0.86f, 0.84f, 0.78f);
            return c * stain;
        });
        return P.Lit(name, tex, Color.white, 0.25f);
    }

    // =====================================================================
    // 바닥·건물
    // =====================================================================

    static void Zone2Ground(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> Z)
    {
        const float z0 = 160f, z1 = SqZ1;
        P.Box(g, "PlazaW", new Vector3((SqX0 + AveRoad0) / 2f, 0.075f, (z0 + z1) / 2f), new Vector3(AveRoad0 - SqX0, 0.15f, z1 - z0), Z["Plaza"], new Vector2(2.4f, 2.4f));
        P.Box(g, "PlazaE", new Vector3((AveRoad1 + BldX1) / 2f, 0.075f, (z0 + z1) / 2f), new Vector3(BldX1 - AveRoad1, 0.15f, z1 - z0), Z["Plaza"], new Vector2(2.4f, 2.4f));
        // 큰길 찻길이 광장을 지나 항구 길로 이어진다
        const float r0 = 160f, r1 = 300f;
        P.Box(g, "SquareRoad", new Vector3(AveX, -0.25f, (r0 + r1) / 2f), new Vector3(AveRoad1 - AveRoad0, 0.5f, r1 - r0), M["Asphalt"], new Vector2(4f, 4f));
        P.Box(g, "SquareCurb_W", new Vector3(AveRoad0 + 0.1f, 0.08f, (r0 + r1) / 2f), new Vector3(0.2f, 0.16f, r1 - r0), M["Curb"], Vector2.one, collider: false);
        P.Box(g, "SquareCurb_E", new Vector3(AveRoad1 - 0.1f, 0.08f, (r0 + r1) / 2f), new Vector3(0.2f, 0.16f, r1 - r0), M["Curb"], Vector2.one, collider: false);
        for (float z = r0 + 3f; z < r1; z += 6f)
            P.Box(g, "LaneDash", new Vector3(AveX, 0.004f, z), new Vector3(0.12f, 0.01f, 3f), M["Paint"], Vector2.one, collider: false);
        P.Box(g, "HarborWalk_W", new Vector3((AveW + AveRoad0) / 2f, 0.075f, (z1 + r1) / 2f), new Vector3(AveRoad0 - AveW, 0.15f, r1 - z1), M["Sidewalk"], new Vector2(2f, 2f));
        P.Box(g, "HarborWalk_E", new Vector3((AveRoad1 + AveE) / 2f, 0.075f, (z1 + r1) / 2f), new Vector3(AveE - AveRoad1, 0.15f, r1 - z1), M["Sidewalk"], new Vector2(2f, 2f));
    }

    static void Zone2Buildings(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> Z)
    {
        var F = new[] { M["FacadeA"], M["FacadeB"], M["FacadeC"] };
        Material back = M["BuildingBack"];
        // 남쪽(큰길에서 들어오는 입구 양옆): 광장을 본다
        P.Building(g, "Square_SW", new Vector3((112f + AveW) / 2f, 7f, 161f), new Vector3(AveW - 112f, 14f, 10f), F[1], back, Vector3.forward);
        P.Building(g, "Square_SE", new Vector3((AveE + BldX1) / 2f, 7.5f, 161f), new Vector3(BldX1 - AveE, 15f, 10f), F[2], back, Vector3.forward);
        ShutterRow(g, new Vector3(SqX0, G, 166.03f), new Vector3(AveW, G, 166.03f), X["Shutter"]);
        ShutterRow(g, new Vector3(AveE, G, 166.03f), new Vector3(BldX0, G, 166.03f), X["Shutter"]);
        // 서쪽 줄: 높이를 달리한 세 채
        float[][] west = { new[] { 166f, 188f, 15f }, new[] { 188f, 214f, 12f }, new[] { 214f, SqZ1, 17f } };
        for (int i = 0; i < west.Length; i++)
        {
            float a = west[i][0], c = west[i][1], h = west[i][2];
            P.Building(g, $"Square_W{i}", new Vector3(119f, h / 2f, (a + c) / 2f), new Vector3(14f, h, c - a), F[i % 3], back, Vector3.right);
            ShutterRow(g, new Vector3(SqX0 + 0.03f, G, a), new Vector3(SqX0 + 0.03f, G, c), X["Shutter"]);
        }
        // 북쪽 줄(항구 길 입구 양옆)
        P.Building(g, "Square_NW", new Vector3((112f + AveW) / 2f, 8f, 245f), new Vector3(AveW - 112f, 16f, 14f), F[0], back, Vector3.back);
        P.Building(g, "Square_NE", new Vector3((AveE + BldX1) / 2f, 7f, 245f), new Vector3(BldX1 - AveE, 14f, 14f), F[1], back, Vector3.back);
        ShutterRow(g, new Vector3(SqX0, G, SqZ1 - 0.03f), new Vector3(AveW, G, SqZ1 - 0.03f), X["Shutter"]);
        ShutterRow(g, new Vector3(AveE, G, SqZ1 - 0.03f), new Vector3(BldX0, G, SqZ1 - 0.03f), X["Shutter"]);
        // 동쪽: 골목 남쪽의 높은 건물(저격수 건물은 SniperBuilding)
        P.Building(g, "Square_E0", new Vector3((BldX0 + BldX1) / 2f, 7.5f, (166f + AlleyZ0) / 2f), new Vector3(BldX1 - BldX0, 15f, AlleyZ0 - 166f), F[0], back, Vector3.left);
        ShutterRow(g, new Vector3(BldX0 - 0.03f, G, 166f), new Vector3(BldX0 - 0.03f, G, AlleyZ0), X["Shutter"]);
        // 항구 길 양옆과 끝
        P.Building(g, "Harbor_W", new Vector3((140f + AveW) / 2f, 6.5f, 276f), new Vector3(AveW - 140f, 13f, 48f), F[2], back, Vector3.right);
        P.Building(g, "Harbor_E", new Vector3((AveE + 188f) / 2f, 7.5f, 276f), new Vector3(188f - AveE, 15f, 48f), F[0], back, Vector3.left);
        P.Building(g, "Harbor_End", new Vector3(AveX, 9f, 306f), new Vector3(48f, 18f, 12f), F[1], back, Vector3.back);
        // 항구 쪽 화살표 간판(항구 길 들머리 오른쪽 건물 벽)
        var arrow = P.Box(g, "HarborSign", new Vector3(AveE + 0.08f, 4.2f, 256f), new Vector3(0.06f, 0.75f, 3f), Z["SignArrow"], Vector2.one, collider: false);
        arrow.GetComponent<MeshFilter>().sharedMesh = P.BoxMesh(new Vector3(0.06f, 0.75f, 3f), new Vector2(3f, 0.75f), new[] { Vector3.left });
        arrow.GetComponent<MeshRenderer>().sharedMaterials = new[] { Z["SignArrow"], M["BuildingBack"] };
    }

    /// <summary>1층 셔터 줄: a→b 선을 따라 가게 폭(약 6m)마다 셔터 하나(사이는 기둥). 건물 앞면에 붙인다</summary>
    static void ShutterRow(Transform parent, Vector3 a, Vector3 b, Material mat)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 6f));
        float w = len / n;
        bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
        for (int i = 0; i < n; i++)
        {
            Vector3 c = a + d.normalized * (w * (i + 0.5f)) + Vector3.up * 1.35f;
            var size = alongX ? new Vector3(w - 1.2f, 2.7f, 0.06f) : new Vector3(0.06f, 2.7f, w - 1.2f);
            P.Box(parent, "Shutter", c, size, mat, new Vector2(w - 1.2f, 2.7f), collider: false);
        }
    }

    // =====================================================================
    // 검문소
    // =====================================================================

    static void Checkpoint(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> Z, Transform triggers, Transform people, Transform routes, Built b)
    {
        var cp = new GameObject("Checkpoint").transform;
        cp.SetParent(g);
        // 순찰차 두 대: 길을 반쯤 막고 비스듬히 섰다(가운데 승합차 길은 비운다)
        PoliceCar(cp, new Vector3(160.2f, 0f, 175f), 70f, Z);
        PoliceCar(cp, new Vector3(168.1f, 0f, 176.5f), -105f, Z);
        // 바리케이드: 인도 양쪽과 찻길 가운데(문). 가운데 것은 승합차가 지날 때 옆으로 치운다
        TryProp("PoliceBarricade", cp, new Vector3(AveW + 1.6f, G, 170.6f), 0f);
        TryProp("PoliceBarricade", cp, new Vector3(AveE - 1.6f, G, 170.6f), 0f);
        var gate = TryProp("PoliceBarricade", cp, new Vector3(AveX, 0f, 170.4f), 0f);
        if (gate)
        {
            gate.name = "Gate";
            gate.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true; // 움직이는 문: 내비메시에 구멍을 내지 않는다
            foreach (var t in gate.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            b.gate = gate.transform;
        }
        b.gateOpen = P.Marker(triggers, "GateOpen", new Vector3(AveX - 2.3f, 0f, 171.6f), -70f);
        foreach (var (x, z) in new[] { (158.9f, 169.6f), (160.6f, 169.2f), (167.4f, 169.2f), (169.1f, 169.6f), (156.4f, 168.6f), (171.6f, 168.6f) })
            TryProp("TrafficCone", cp, new Vector3(x, x < AveRoad0 || x > AveRoad1 ? G : 0f, z), x * 37f);
        // 투광등 둘: 들어오는 큰길(남쪽)을 비춘다
        FloodLight(cp, new Vector3(AveW + 0.8f, G, 178.2f), 200f, Z);
        FloodLight(cp, new Vector3(AveE - 0.8f, G, 178.2f), 160f, Z);

        // 방패 경찰 둘(찻길 양옆, 남쪽을 본다) + 손전등 경찰 하나(검문소 뒤를 오간다)
        var s1 = ShieldCop(people, "Cop_Shield_A", new Vector3(161.2f, 0f, 179.5f), 180f);
        var s2 = ShieldCop(people, "Cop_Shield_B", new Vector3(166.8f, 0f, 179.5f), 180f);
        var torch = PatrolAs("RiotCop", people, routes, "Cop_Torch", new Vector3(156.6f, G, 181.5f), 90f,
            new[] { new Vector3(156.6f, G, 181.5f), new Vector3(171.4f, G, 181.5f) });
        torch.GetComponent<EnemyAI>().displayName = "경찰"; // Enemy 속성은 플레이 중 Awake에서야 채워진다
        torch.spottedLine = "거기 누구야!";

        var zoneGo = new GameObject("CheckpointZone");
        zoneGo.transform.SetParent(triggers);
        zoneGo.transform.position = new Vector3(AveX, 1.5f, 175.5f);
        var col = zoneGo.AddComponent<BoxCollider>();
        col.size = new Vector3(AveE - AveW + 6f, 3f, 13f);
        col.isTrigger = true;
        var zone = zoneGo.AddComponent<CombatZone>();
        zone.enemies = new[] { s1, s2 };
        zone.saveOnEnter = false;
        b.checkpointZone = zone;

        // 검은 승합차(대니 집 앞에서 본 조직의 차): 처음에는 꺼 둔다(내비메시에 구워지지 않게). 컷신에서 검문소를 지나간다
        var van = TryProp("BlackVan", g, new Vector3(AveX, 0f, 92f), 0f);
        if (van)
        {
            van.name = "OrgVan";
            foreach (var t in van.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            P.AddLight(van.transform, "Headlight_L", new Vector3(-0.7f, 0.85f, 2.75f), LightType.Spot, new Color(1f, 0.95f, 0.82f), 18f, 22f, 55f, Quaternion.Euler(6f, 0f, 0f), local: true);
            P.AddLight(van.transform, "Headlight_R", new Vector3(0.7f, 0.85f, 2.75f), LightType.Spot, new Color(1f, 0.95f, 0.82f), 18f, 22f, 55f, Quaternion.Euler(6f, 0f, 0f), local: true);
            P.AddLight(van.transform, "TailGlow", new Vector3(0f, 0.9f, -2.9f), LightType.Point, new Color(1f, 0.1f, 0.06f), 1.2f, 3f, local: true);
            var src = van.AddComponent<AudioSource>(); // 엔진 소리(감독이 튼다)
            src.spatialBlend = 0.35f; // 녹음 자체가 다가왔다 멀어지는 소리라 거리 감쇠는 약하게
            src.minDistance = 4f;
            src.maxDistance = 45f;
            src.loop = true;
            src.playOnAwake = false;
            van.SetActive(false);
            b.van = van;
        }
        b.vanPath = new[]
        {
            P.Marker(triggers, "Van_0", new Vector3(AveX + 0.4f, 0f, 96f), 0f),
            P.Marker(triggers, "Van_Stop", new Vector3(AveX, 0f, 165.2f), 0f),
            P.Marker(triggers, "Van_2", new Vector3(AveX, 0f, 250f), 0f),
            P.Marker(triggers, "Van_End", new Vector3(AveX, 0f, 298f), 0f),
        };
    }

    static void PoliceCar(Transform parent, Vector3 pos, float yaw, Dictionary<string, Material> Z)
    {
        var car = TryProp("PoliceCar", parent, pos, yaw);
        if (!car) return;
        var bounds = WorldBounds(car);
        float top = bounds.max.y - pos.y;
        // 지붕 경광등: 빨강·파랑 불빛이 번갈아 깜빡인다(모델의 경광등 바로 위에 빛만 얹는다)
        var red = P.AddLight(car.transform, "BeaconRed", new Vector3(-0.35f, top + 0.15f, 0.1f), LightType.Point, new Color(1f, 0.08f, 0.05f), 5f, 9f, local: true);
        var blue = P.AddLight(car.transform, "BeaconBlue", new Vector3(0.35f, top + 0.15f, 0.1f), LightType.Point, new Color(0.1f, 0.25f, 1f), 5f, 9f, local: true);
        var lights = car.AddComponent<PoliceLights>();
        lights.red = red;
        lights.blue = blue;
    }

    static void FloodLight(Transform parent, Vector3 pos, float yaw, Dictionary<string, Material> Z)
    {
        var f = TryProp("FloodLight", parent, pos, yaw);
        if (!f) return;
        var spot = P.AddLight(f.transform, "Flood", new Vector3(0f, 2.05f, 0.25f), LightType.Spot, new Color(1f, 0.97f, 0.9f), 55f, 28f, 62f, Quaternion.Euler(14f, 0f, 0f), local: true);
        spot.shadows = LightShadows.Soft;
    }

    /// <summary>방패 경찰: 진압 경찰 모델 + 왼손에 방패(정면 공격을 막는다, ShieldGuard)</summary>
    static EnemyAI ShieldCop(Transform parent, string name, Vector3 pos, float yaw)
    {
        var ai = P.CreateThug(parent, name, pos, yaw, "RiotCop");
        SetCombat(ai, false);
        ai.displayName = "방패 경찰";
        ai.damage = 10;
        ai.windup = 0.7f;
        ai.attackCooldown = 1.7f;
        ai.GetComponent<Health>().maxHealth = 120;
        var shield = TryProp("RiotShield", ai.transform, Vector3.zero, 0f);
        if (shield)
        {
            shield.name = "Shield";
            shield.transform.localPosition = new Vector3(-0.16f, 0.62f, 0.42f);
            shield.transform.localRotation = Quaternion.Euler(0f, 8f, 0f); // 몸 왼쪽 앞을 가린다
            foreach (var t in shield.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            // 왼팔뚝에 끼운 방패: 쥐는 자세와 방패 위치는 ShieldGuard가 매 프레임 팔뚝을 따라 맞춘다
            var guard = ai.gameObject.AddComponent<ShieldGuard>();
            guard.shield = shield.transform;
            guard.ik = ai.aimIK;
        }
        return ai;
    }

    // =====================================================================
    // 밤 시장(서쪽 반)
    // =====================================================================

    static void Market(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> Z, Transform people, Transform routes)
    {
        var mk = new GameObject("NightMarket").transform;
        mk.SetParent(g);
        var canvases = new[] { Z["CanvasRed"], Z["CanvasBlue"], Z["CanvasGreen"], Z["CanvasOrange"] };
        // 노점 네 줄: 마주 보는 두 줄씩(통로 4m), 두 쌍 사이는 짐 나르는 뒷길
        var rows = new (float x, float yaw)[] { (130.2f, 90f), (137.8f, -90f), (145.2f, 90f), (152.8f, -90f) };
        int k = 0;
        foreach (var (x, yaw) in rows)
        {
            for (float z = 185.5f; z < 234f; z += 3.3f)
            {
                if ((z > 200.5f && z < 207.5f) || (z > 218.5f && z < 225.5f)) continue; // 가로 통로 둘
                Stall(mk, new Vector3(x, G, z), yaw, canvases[(k * 7 + (int)z) % canvases.Length], Z, k);
                k++;
            }
        }
        // 노점 사이 통로 위 전구 줄
        foreach (float x in new[] { 134f, 149f })
            for (float z = 186f; z < 232f; z += 11.5f)
                BulbString(mk, new Vector3(x, 3.1f, z), new Vector3(x, 3.1f, z + 10f), Z, light: true);

        // 시장을 도는 조직원 둘(손전등): 통로를 오간다
        PatrolAs("Enforcer", people, routes, "Thug_Market_A", new Vector3(134f, G, 188f), 0f,
            new[] { new Vector3(134f, G, 188f), new Vector3(134f, G, 216f), new Vector3(141.5f, G, 222f), new Vector3(141.5f, G, 204f) });
        PatrolAs("Enforcer", people, routes, "Thug_Market_B", new Vector3(149f, G, 230f), 180f,
            new[] { new Vector3(149f, G, 230f), new Vector3(149f, G, 209f), new Vector3(156.3f, G, 204f), new Vector3(156.3f, G, 228f) });
    }

    /// <summary>
    /// 노점 하나(폭 3m, 깊이 2m, 앞 = 로컬 +z): 네 기둥, 앞쪽 판매대, 비스듬한 줄무늬 차양, 뒤 방수포(2.1m, 사람이 숨는 엄폐물).
    /// 판매대 위에는 사과 상자·나무 상자(바로크 소품이 있으면). 두 칸마다 차양 아래 전구 하나.
    /// </summary>
    static void Stall(Transform parent, Vector3 pos, float yaw, Material canvas, Dictionary<string, Material> Z, int index)
    {
        var s = new GameObject("Stall").transform;
        s.SetParent(parent, false);
        s.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        GameObject Part(string n, Vector3 c, Vector3 size, Material m, bool col)
        {
            var p = P.Box(s, n, Vector3.zero, size, m, new Vector2(1f, 1f), collider: col);
            p.transform.localPosition = c;
            p.transform.localRotation = Quaternion.identity;
            return p;
        }
        foreach (float x in new[] { -1.45f, 1.45f })
        {
            Part("Post", new Vector3(x, 1.2f, 0.95f), new Vector3(0.06f, 2.4f, 0.06f), Mat("Galvanized"), false);
            Part("Post", new Vector3(x, 1.25f, -0.95f), new Vector3(0.06f, 2.5f, 0.06f), Mat("Galvanized"), false);
        }
        Part("Counter", new Vector3(0f, 0.85f, 0.5f), new Vector3(2.8f, 0.06f, 0.9f), Z["StallTable"], true);
        Part("CounterSkirt", new Vector3(0f, 0.42f, 0.94f), new Vector3(2.8f, 0.8f, 0.03f), canvas, false);
        var back = Part("BackTarp", new Vector3(0f, 1.1f, -0.97f), new Vector3(2.95f, 2.05f, 0.04f), index % 3 == 0 ? Z["TarpGrey"] : Z["Tarp"], true);
        var roof = Part("Canopy", new Vector3(0f, 2.42f, 0.05f), new Vector3(3.2f, 0.04f, 2.4f), canvas, false);
        roof.transform.localRotation = Quaternion.Euler(-9f, 0f, 0f); // 앞쪽이 낮다(빗물이 앞으로 흐른다)
        Part("Valance", new Vector3(0f, 2.18f, 1.24f), new Vector3(3.2f, 0.28f, 0.02f), canvas, false);
        // 장사를 접은 밤: 판매대 물건은 방수포로 덮고 끈으로 묶어 두었다(사과 상자 생성물은 세 번 모두 과일 덩어리로 뭉개져 쓰지 않는다)
        float w = 1.1f + 0.5f * ((index * 37) % 3) / 2f;
        Part("CoveredGoods", new Vector3(-0.35f + 0.3f * (index % 2), 1.06f, 0.5f), new Vector3(w, 0.36f, 0.7f), index % 3 == 1 ? Z["TarpGrey"] : Z["Tarp"], false);
        foreach (float sx in new[] { -0.3f, 0.3f })
            Part("Strap", new Vector3(-0.35f + 0.3f * (index % 2) + sx * w, 1.06f, 0.5f), new Vector3(0.025f, 0.37f, 0.71f), Z["Cable"], false);
        // 뒤쪽에는 빈 나무 상자 두 개
        var crate = TryProp("CrateStack", s, Vector3.zero, 0f);
        if (crate)
        {
            crate.transform.localPosition = new Vector3(index % 2 == 0 ? -0.9f : 0.9f, 0f, -0.4f);
            crate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            crate.transform.localScale = Vector3.one * 0.85f;
        }
        if (index % 2 == 0)
        {
            var bulb = Part("Bulb", new Vector3(0f, 2.15f, 0.3f), new Vector3(0.09f, 0.12f, 0.09f), Z["Bulb"], false);
            P.AddLight(s, "StallLight", new Vector3(0f, 2.05f, 0.4f), LightType.Point, new Color(1f, 0.74f, 0.42f), 2.4f, 5.5f, local: true);
        }
    }

    /// <summary>a→b 사이에 늘어진 전선과 전구(전구 1m마다). light면 가운데에 은은한 점광원 하나</summary>
    static void BulbString(Transform parent, Vector3 a, Vector3 b, Dictionary<string, Material> Z, bool light)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(a, b)));
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (0.5f * 4f * t * (1f - t)); // 늘어진 전선
            P.Box(parent, "MarketBulb", p, new Vector3(0.07f, 0.09f, 0.07f), Z["Bulb"], Vector2.one, collider: false);
        }
        if (light) P.AddLight(parent, "StringLight", Vector3.Lerp(a, b, 0.5f) + Vector3.down * 0.6f, LightType.Point, new Color(1f, 0.76f, 0.46f), 6f, 10f);
    }

    // =====================================================================
    // 짐 부리는 곳(동쪽 반)
    // =====================================================================

    static void LoadingArea(Transform g, Transform triggers)
    {
        var la = new GameObject("LoadingArea").transform;
        la.SetParent(g);
        // 트럭(큰 엄폐물): 찻길 옆에 북쪽을 보고 섰다
        TryProp("BoxTruck", la, new Vector3(175.6f, G, 197f), 0f);
        TryProp("HandCart", la, new Vector3(178.6f, G, 192.2f), -60f);
        TryProp("CrateStack", la, new Vector3(178.8f, G, 202.6f), 90f);
        TryProp("CrateStack", la, new Vector3(179.6f, G, 205.4f), 15f);
        // 포장마차 다섯 대: 광장 동쪽 벽을 따라 서쪽(광장)을 본다
        for (int i = 0; i < 5; i++)
            TryProp("FoodCart", la, new Vector3(196.8f, G, 172.5f + i * 2.6f), -90f + (i % 2 == 0 ? 0f : 4f));
        TryProp("GasCylinders", la, new Vector3(199.6f, G, 186.3f), -90f);
        TryProp("StoolStack", la, new Vector3(193.4f, G, 171.2f), 30f);
        TryProp("StoolStack", la, new Vector3(193.8f, G, 185.4f), -20f);
        // 팔레트 더미(사람 키 엄폐물): 마당 여기저기
        foreach (var (x, z, yaw) in new[] { (183.5f, 209f, 0f), (185f, 210.6f, 90f), (190.5f, 219f, 10f), (176.5f, 221.5f, -15f), (194.5f, 202.5f, 0f), (188f, 230.5f, 90f) })
            TryProp("PalletBoxes", la, new Vector3(x, G, z), yaw);
        TryProp("WaterDrums", la, new Vector3(195.5f, G, 227.5f), 30f);
        TryProp("WaterDrums", la, new Vector3(174.5f, G, 230f), -40f);
        TryProp("GasCylinders", la, new Vector3(192f, G, 208f), 0f);
        TryProp("HandCart", la, new Vector3(181.5f, G, 224f), 120f);
        TryProp("CrateStack", la, new Vector3(199.4f, G, 232.5f), -90f);
        // 트럭 뒤에 떨어진 상자: 테이프·회복약
        TapeSpot(la, triggers, "MarketTape", new Vector3(176.4f, G, 191.6f), 2, 1, box: true);
    }

    // =====================================================================
    // 저격수 건물(동쪽): 남쪽 골목의 지그재그 철계단 → 옥상
    // =====================================================================

    static void SniperBuilding(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> Z, Transform triggers, Transform people, Built b)
    {
        var sb = new GameObject("SniperBuilding").transform;
        sb.SetParent(g);
        P.Building(sb, "Sniper_Bldg", new Vector3((BldX0 + BldX1) / 2f, RoofY / 2f, (AlleyZ1 + SqZ1) / 2f), new Vector3(BldX1 - BldX0, RoofY, SqZ1 - AlleyZ1), M["FacadeB"], M["BuildingBack"], Vector3.left);
        ShutterRow(sb, new Vector3(BldX0 - 0.03f, G, AlleyZ1), new Vector3(BldX0 - 0.03f, G, SqZ1), Mat("Shutter"));
        // 골목(바닥은 광장 돌바닥이 이어진다)의 막다른 뒷벽
        P.Box(sb, "AlleyBackWall", new Vector3(BldX1 + 0.15f, RoofY / 2f, (AlleyZ0 + AlleyZ1) / 2f), new Vector3(0.3f, RoofY, AlleyZ1 - AlleyZ0), M["BuildingBack"], new Vector2(2f, 2f));
        P.AddLight(sb, "AlleyLight", new Vector3(BldX0 + 1.2f, 3.2f, (AlleyZ0 + AlleyZ1) / 2f), LightType.Point, new Color(1f, 0.8f, 0.55f), 10f, 10f);
        P.AddLight(sb, "LandingLight", new Vector3(BldX1 - 0.6f, 7.4f, (AlleyZ0 + AlleyZ1) / 2f), LightType.Point, new Color(1f, 0.8f, 0.55f), 10f, 10f);
        P.AddLight(sb, "StairTopLight", new Vector3(BldX0 + 1.5f, RoofY + 2.4f, AlleyZ1 + 0.4f), LightType.Point, new Color(1f, 0.82f, 0.6f), 12f, 12f);

        // 계단 1: 골목 남쪽(z 188.2~189.8)을 따라 동쪽으로 올라간다(지면 → 4.6m)
        const float mid = 4.6f, runX0 = 203.2f, runX1 = 214.4f;
        Flight(sb, Z, new Vector3(runX0, G, 189f), new Vector3(runX1, mid, 189f), 1.6f);
        P.Box(sb, "Landing", new Vector3((runX1 + BldX1) / 2f, mid - 0.1f, (AlleyZ0 + AlleyZ1) / 2f), new Vector3(BldX1 - runX1, 0.2f, AlleyZ1 - AlleyZ0 - 0.2f), Z["Grating"], Vector2.one);
        // 계단 2: 골목 북쪽(z 190.2~191.8)을 따라 서쪽으로 올라간다(4.6m → 옥상 9m)
        Flight(sb, Z, new Vector3(runX1, mid, 191f), new Vector3(runX0, RoofY, 191f), 1.6f);
        P.Box(sb, "TopLanding", new Vector3((BldX0 + runX0) / 2f + 0.1f, RoofY - 0.1f, 191.2f), new Vector3(runX0 - BldX0 + 0.2f, 0.2f, 2.4f), Z["Grating"], Vector2.one);
        // 두 계단 사이 칸막이 벽(아래 계단에서 위 계단으로 넘어가거나 떨어지지 않게). 꺾이는 층계참 쪽은 비운다
        P.Box(sb, "StairDivider", new Vector3((runX0 + runX1) / 2f - 0.6f, (RoofY + 1f) / 2f, 190f), new Vector3(runX1 - runX0 + 1.2f, RoofY + 1f, 0.15f), M["BuildingBack"], new Vector2(2f, 2f));
        b.stairsFoot = P.Marker(triggers, "StairsFoot", new Vector3(runX0 - 0.6f, G, 189f), 90f);
        b.roofTop = P.Marker(triggers, "RoofTop", new Vector3(BldX0 + 1.2f, RoofY, 193.5f), 0f);

        // 옥상 난간(서쪽은 광장을 내려다보는 쪽). 계단 꼭대기 자리는 비운다
        P.Box(sb, "Parapet_W", new Vector3(BldX0 + 0.12f, RoofY + 0.45f, (AlleyZ1 + 0.6f + SqZ1) / 2f), new Vector3(0.25f, 0.9f, SqZ1 - AlleyZ1 - 0.6f), Z["Parapet"], new Vector2(2f, 2f));
        P.Box(sb, "Parapet_N", new Vector3((BldX0 + BldX1) / 2f, RoofY + 0.45f, SqZ1 - 0.12f), new Vector3(BldX1 - BldX0, 0.9f, 0.25f), Z["Parapet"], new Vector2(2f, 2f));
        P.Box(sb, "Parapet_E", new Vector3(BldX1 - 0.12f, RoofY + 0.45f, (AlleyZ1 + SqZ1) / 2f), new Vector3(0.25f, 0.9f, SqZ1 - AlleyZ1), Z["Parapet"], new Vector2(2f, 2f));
        P.Box(sb, "Parapet_S", new Vector3((BldX0 + 3.4f + BldX1) / 2f, RoofY + 0.45f, AlleyZ1 + 0.12f), new Vector3(BldX1 - BldX0 - 3.4f, 0.9f, 0.25f), Z["Parapet"], new Vector2(2f, 2f));
        // 옥상 엄폐물: 계단실 오두막·물탱크(사람 키를 넘는다), 실외기들
        P.Box(sb, "StairHut", new Vector3(210.5f, RoofY + 1.4f, 201f), new Vector3(3.2f, 2.8f, 3.4f), M["BuildingBack"], new Vector2(2f, 2f));
        P.Box(sb, "WaterTank", new Vector3(207f, RoofY + 1.15f, 207.5f), new Vector3(2.2f, 2.3f, 2.2f), Z["Parapet"], new Vector2(2f, 2f));
        // 옥상 등: 계단실 문 위 전구, 저격수 뒤쪽의 희미한 작업등(실루엣이 보이게)
        P.Box(sb, "HutBulb", new Vector3(208.85f, RoofY + 2.45f, 201f), new Vector3(0.08f, 0.12f, 0.08f), Z["Bulb"], Vector2.one, collider: false);
        P.AddLight(sb, "HutLight", new Vector3(208.5f, RoofY + 2.3f, 201f), LightType.Point, new Color(1f, 0.8f, 0.55f), 16f, 15f);
        P.AddLight(sb, "SniperBackLight", new Vector3(BldX0 + 3.2f, RoofY + 2.2f, 216f), LightType.Point, new Color(0.75f, 0.82f, 1f), 8f, 9f);
        P.AddLight(sb, "RoofNorthLight", new Vector3(211f, RoofY + 2.2f, 228f), LightType.Point, new Color(1f, 0.8f, 0.55f), 12f, 13f);
        foreach (var (x, z) in new[] { (212.5f, 196f), (205f, 199.5f), (213f, 222f), (209.5f, 228f) })
            TryProp("AcUnit", sb, new Vector3(x, RoofY, z), x * 11f);
        TapeSpot(sb, triggers, "RoofTape", new Vector3(214f, RoofY, 234f), 1, 1, box: false);

        // 저격수(진압 경찰): 서쪽 난간 바로 뒤에서 광장을 겨눈다. 움직이지 않고, 가까이 붙어 치면 겨누기가 끊긴다
        var sniper = P.CreateThug(people, "Cop_Sniper", new Vector3(BldX0 + 0.85f, RoofY, 214f), -90f, "RiotCop");
        SetCombat(sniper, true);
        sniper.displayName = "저격수";
        sniper.holdPosition = true;
        sniper.aimInterruptible = true;
        sniper.awarenessArc = 100f; // 조준경으로 광장만 본다: 계단에서 옆으로 다가가면 돌아서지만, 뒤로 돌아가면 몰래 제압할 수 있다
        sniper.shootRange = 70f;
        sniper.aimTime = 2.2f;
        sniper.shotDamage = 45;
        sniper.shotCooldown = 3.2f;
        sniper.laserWidth = 3f;
        sniper.shotSound = "SniperShot";
        sniper.shotVolume = 1.2f;
        sniper.GetComponent<Health>().maxHealth = 70;
        var ik = sniper.aimIK;
        if (ik.heldItem) Object.DestroyImmediate(ik.heldItem.gameObject); // 권총 대신 저격총
        var hold = new GameObject("RifleHold").transform;
        hold.SetParent(ik.transform, false);
        var rifle = TryProp("SniperRifle", hold, Vector3.zero, 0f);
        if (rifle)
        {
            rifle.transform.localPosition = new Vector3(0f, -0.08f, 0.28f); // 손잡이(방아쇠 자리)가 손에 오게 앞으로 민다
            rifle.transform.localRotation = Quaternion.identity;
            foreach (var t in rifle.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
        ik.heldItem = hold;
        sniper.muzzleDistance = 0.88f;
        b.sniper = sniper;

        // 옥상 경비(근접): 옥상에 올라오면 덤빈다
        var guard = Fighter(people, "Thug_Roof", new Vector3(211.5f, RoofY, 197.5f), 200f, false);
        var zoneGo = new GameObject("RoofZone");
        zoneGo.transform.SetParent(triggers);
        zoneGo.transform.position = new Vector3((BldX0 + BldX1) / 2f, RoofY + 1.5f, (AlleyZ1 + SqZ1) / 2f);
        var col = zoneGo.AddComponent<BoxCollider>();
        col.size = new Vector3(BldX1 - BldX0, 3f, SqZ1 - AlleyZ1);
        col.isTrigger = true;
        var zone = zoneGo.AddComponent<CombatZone>();
        zone.enemies = new[] { guard };
        zone.saveOnEnter = false;
        b.roofZone = zone;
    }

    /// <summary>
    /// 철계단 한 줄: from(아래 끝 바닥) → to(위 끝 바닥), 폭 width. 걷는 판은 보이지 않는 경사판(충돌체), 보이는 것은 디딤판(약 0.25m 높이마다)과 옆판.
    /// </summary>
    static void Flight(Transform parent, Dictionary<string, Material> Z, Vector3 from, Vector3 to, float width)
    {
        var f = new GameObject("StairFlight").transform;
        f.SetParent(parent);
        Vector3 d = to - from;
        float run = new Vector2(d.x, d.z).magnitude, rise = d.y;
        float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        var dirFlat = new Vector3(d.x, 0f, d.z).normalized;
        f.SetPositionAndRotation((from + to) / 2f, Quaternion.LookRotation(dirFlat, Vector3.up));
        // 걷는 경사판(보이지 않는다)
        var ramp = new GameObject("Ramp");
        ramp.transform.SetParent(f, false);
        ramp.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
        var rc = ramp.AddComponent<BoxCollider>();
        float len = Mathf.Sqrt(run * run + rise * rise);
        rc.size = new Vector3(width, 0.12f, len + 0.2f);
        rc.center = new Vector3(0f, -0.06f, 0f);
        GameObjectUtility.SetStaticEditorFlags(ramp, StaticEditorFlags.NavigationStatic);
        // 디딤판
        int steps = Mathf.Max(2, Mathf.RoundToInt(rise / 0.25f));
        for (int i = 0; i < steps; i++)
        {
            float t = (i + 0.5f) / steps;
            var tread = P.Box(f, "Tread", Vector3.zero, new Vector3(width, 0.05f, run / steps + 0.04f), Z["Grating"], Vector2.one, collider: false);
            tread.transform.localPosition = new Vector3(0f, -rise / 2f + rise * (i + 1f) / steps - 0.03f, -run / 2f + run * t);
            tread.transform.localRotation = Quaternion.identity;
        }
        // 옆판(경사진 철판) 두 장
        foreach (float x in new[] { -width / 2f - 0.03f, width / 2f + 0.03f })
        {
            var stringer = P.Box(f, "Stringer", Vector3.zero, new Vector3(0.05f, 0.28f, len), Z["Grating"], Vector2.one, collider: false);
            stringer.transform.localPosition = new Vector3(x, -0.1f, 0f);
            stringer.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
        }
    }

    // =====================================================================
    // 조명·간판
    // =====================================================================

    static void Zone2Lights(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> Z)
    {
        var lg = new GameObject("SquareLights").transform;
        lg.SetParent(g);
        // 광장 둘레 가로등
        for (float z = 176f; z < 236f; z += 19f)
        {
            ParkLamp(lg, new Vector3(SqX0 + 1.2f, G, z), 0f, M);              // 서쪽 벽 앞(머리가 광장 쪽 +x)
            ParkLamp(lg, new Vector3(BldX0 - 1.2f, G, z + 9f), 180f, M);       // 동쪽 벽 앞
        }
        foreach (float z in new[] { 196f, 222f })
        {
            ParkLamp(lg, new Vector3(AveRoad0 - 0.45f, G, z), 0f, M);
            ParkLamp(lg, new Vector3(AveRoad1 + 0.45f, G, z + 6f), 180f, M);
        }
        foreach (float z in new[] { 250f, 276f })
        {
            ParkLamp(lg, new Vector3(AveRoad0 - 0.45f, G, z), 0f, M);
            ParkLamp(lg, new Vector3(AveRoad1 + 0.45f, G, z + 12f), 180f, M);
        }
        // 포장마차 앞 전구 줄
        BulbString(lg, new Vector3(194.5f, 2.9f, 171f), new Vector3(194.5f, 2.9f, 185f), Z, light: true);
        // 짐 마당 가운데 가로등 둘과 팔레트 더미 위 전구 줄(어두우면 엄폐물 윤곽이 안 보인다)
        ParkLamp(lg, new Vector3(187.5f, G, 199f), 180f, M);
        ParkLamp(lg, new Vector3(181.5f, G, 222.5f), 0f, M);
        BulbString(lg, new Vector3(176f, 3.4f, 213f), new Vector3(196f, 3.4f, 213f), Z, light: true);
        BulbString(lg, new Vector3(176f, 3.4f, 228f), new Vector3(196f, 3.4f, 228f), Z, light: true);

        // 시장 입구 아치 간판: 찻길 위를 가로지르는 철 기둥 둘 + 네온 "NIGHT MARKET"
        const float signZ = 184f, signY = 5.3f;
        foreach (float x in new[] { AveRoad0 - 0.4f, AveRoad1 + 0.4f })
            P.Box(lg, "ArchPost", new Vector3(x, (G + signY + 0.6f) / 2f, signZ), new Vector3(0.22f, signY + 0.6f - G, 0.22f), Mat("Galvanized"), Vector2.one);
        P.Box(lg, "ArchBeam", new Vector3(AveX, signY + 0.7f, signZ), new Vector3(AveRoad1 - AveRoad0 + 1.2f, 0.18f, 0.18f), Mat("Galvanized"), Vector2.one, collider: false);
        var sign = P.Box(lg, "NightMarketSign", new Vector3(AveX, signY, signZ), new Vector3(8f, 1.6f, 0.08f), Z["SignNightMarket"], Vector2.one, collider: false);
        // 남쪽(들어오는 쪽)과 북쪽 양면에 글자가 보이게: 앞면만 글자, 뒷면은 같은 판을 하나 더(뒤집어) 붙인다
        sign.GetComponent<MeshFilter>().sharedMesh = P.BoxMesh(new Vector3(8f, 1.6f, 0.08f), new Vector2(8f, 1.6f), new[] { Vector3.back });
        sign.GetComponent<MeshRenderer>().sharedMaterials = new[] { Z["SignNightMarket"], Mat("SignBoard") };
        P.AddLight(lg, "SignGlow", new Vector3(AveX, signY - 0.6f, signZ - 1.2f), LightType.Point, new Color(1f, 0.45f, 0.6f), 3f, 9f);
    }
}
