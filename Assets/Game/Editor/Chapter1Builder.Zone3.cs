using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using P = PrologueBuilder;

/// <summary>
/// 1장 구역 3 「3번 부두」와 그 앞 항구 길(가게 2).
///
/// 배치(미터):
///   항구 길: 찻길 x 158~170, z 238~318. 양옆 건물(서 x 140~155, 동 x 173~188)이 z 316까지.
///   가게 2 「등대 수리점」: 서쪽 건물 1층(앞면 x 155, z 280~291), 거리(+x)를 본다. 저장 지점.
///   철조망 문: z 317.5에서 찻길·인도를 가로막는 미닫이 문 두 짝. F로 밀어 연다 → 구역 3 시작(자동 저장).
///   부두 마당: x 96~244, z 318~424(넓은 콘크리트 마당). 북쪽 안벽(z 424) 너머 바다.
///     서쪽: 컨테이너 야적장(x 100~152, z 326~412) — 잠입 길(골목이 바둑판처럼).
///     가운데: 창고 A(x 160~190, z 338~370). 동쪽: 부두 큰길(x 190~208) — 싸움 길. 창고 B(x 214~242, z 326~362).
///     북쪽: 안벽 위 갠트리 크레인(x 160~186), 3번 부두(x 196~208, z 424~468)와 정박한 화물선.
///   경계: 바다(북), 창고 줄(동·서), 항구 길 건물(남). 벽으로 막은 복도는 없다.
/// </summary>
public static partial class Chapter1Builder
{
    const float HbX0 = 96f, HbX1 = 244f, HbZ0 = 318f, HbZ1 = 424f; // 부두 마당
    const float Shop2Z0 = 280f, Shop2Z1 = 291f, Shop2DoorZ = 288.6f;
    const float GateZ = 317.5f;
    const float PierX0 = 196f, PierX1 = 208f, PierZ1 = 468f;
    const float WaterY = -1.6f;

    static void BuildHarbor(Dictionary<string, Material> M, Dictionary<string, Material> X, Transform triggers, Transform people, Transform routes, Built b)
    {
        var g = P.Group("Harbor");
        var H = HarborMaterials();
        HarborRoad(g, M, X, H, triggers, b);
        HarborGround(g, M, H);
        HarborBoundary(g, M, H);
        ContainerYard(g, H);
        Warehouses(g, M, H);
        Quay(g, M, H, b);
        HarborLights(g, M, H);
        HarborProps(g, triggers);
        HarborEnemies(g, people, routes, triggers, H, b);
        b.barrelFire = builtFire;
        b.burnBarrel = builtBarrel;

        b.zone3Start = P.Marker(triggers, "Zone3Start", new Vector3(AveX, 0.02f, GateZ + 3.5f), 0f);
        b.pier = P.Marker(triggers, "Pier3", new Vector3((PierX0 + PierX1) / 2f, 1.4f, HbZ1 + 6f), 0f);
        // 반장과 마주치는 곳: 부두 큰길·컨테이너 쪽 어느 길로 와도 안벽 앞 마당(z 400)을 지난다
        b.pierTrigger = Trigger(triggers, "PierTrigger", new Vector3(180f, 1.5f, 401f), new Vector3(120f, 3f, 4f));
        b.dawnSeat = P.Marker(triggers, "DawnSeat", new Vector3(PierX0 + 1.6f, 0.02f, PierZ1 - 3f), 0f); // 부두 끝 서쪽 가장자리(배에서 떨어져 바다가 트인다)
    }

    // =====================================================================
    // 재질
    // =====================================================================

    static Dictionary<string, Material> HarborMaterials()
    {
        var H = new Dictionary<string, Material>();
        // 비에 젖은 부두 콘크리트(줄눈 판 6m) · 안벽 모서리 · 바다
        H["Apron"] = P.Lit("HarborApron", P.Tex("HarborApron", 512, ApronPixel), new Color(0.62f, 0.62f, 0.6f), 0.55f, smoothnessFromAlpha: true);
        H["QuayEdge"] = P.Lit("QuayEdge", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.5f, 0.49f, 0.46f), 0.3f);
        H["EdgePaint"] = P.Lit("QuayEdgePaint", null, new Color(0.85f, 0.68f, 0.12f), 0.35f);
        H["Water"] = P.Lit("HarborWater", P.Tex("HarborWater", 256, WaterPixel), new Color(0.05f, 0.08f, 0.1f), 0.95f, metallic: 0.3f);
        H["Fender"] = P.Lit("RubberFender", null, new Color(0.04f, 0.04f, 0.04f), 0.25f);
        H["Bollard"] = P.Lit("BollardIron", null, new Color(0.12f, 0.12f, 0.13f), 0.45f, metallic: 0.6f);
        // 골판 철: 컨테이너(빨강·파랑·초록·회색·주황) · 창고 벽
        var corr = P.Tex("Corrugated", 256, CorrugatedPixel);
        H["ContRed"] = P.Lit("ContainerRed", corr, new Color(0.55f, 0.16f, 0.11f), 0.35f, metallic: 0.35f);
        H["ContBlue"] = P.Lit("ContainerBlue", corr, new Color(0.13f, 0.26f, 0.45f), 0.35f, metallic: 0.35f);
        H["ContGreen"] = P.Lit("ContainerGreen", corr, new Color(0.15f, 0.33f, 0.2f), 0.35f, metallic: 0.35f);
        H["ContGrey"] = P.Lit("ContainerGrey", corr, new Color(0.45f, 0.46f, 0.47f), 0.35f, metallic: 0.35f);
        H["ContOrange"] = P.Lit("ContainerOrange", corr, new Color(0.7f, 0.33f, 0.08f), 0.35f, metallic: 0.35f);
        H["ContFrame"] = P.Lit("ContainerFrame", null, new Color(0.09f, 0.09f, 0.09f), 0.3f, metallic: 0.5f);
        H["ShedWall"] = P.Lit("ShedWall", corr, new Color(0.42f, 0.44f, 0.45f), 0.3f, metallic: 0.4f);
        H["ShedRoof"] = P.Lit("ShedRoof", corr, new Color(0.24f, 0.25f, 0.26f), 0.35f, metallic: 0.4f);
        H["ShedDark"] = P.Lit("ShedDoorDark", null, new Color(0.03f, 0.03f, 0.035f), 0.1f);
        H["CraneYellow"] = P.Lit("CraneYellow", null, new Color(0.78f, 0.55f, 0.08f), 0.4f, metallic: 0.5f);
        H["Steel"] = P.Lit("SteelDark", null, new Color(0.2f, 0.21f, 0.22f), 0.45f, metallic: 0.7f);
        H["Hull"] = P.Lit("ShipHull", null, new Color(0.12f, 0.13f, 0.14f), 0.35f, metallic: 0.3f);
        H["HullRed"] = P.Lit("ShipHullRed", null, new Color(0.38f, 0.08f, 0.06f), 0.3f, metallic: 0.2f);
        H["Sodium"] = P.Unlit("SodiumBulb", new Color(1f, 0.62f, 0.25f) * 3f);
        H["SignPier"] = Neon("SignPier3", "PIER 3", new Color(1f, 0.75f, 0.3f) * 3f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        H["SignLighthouse"] = Neon("ShopSignLighthouse", "Lighthouse", new Color(0.45f, 0.9f, 1f) * 4f, new[] { "Segoe Script" }, FontStyle.Bold);
        H["SignRepair"] = Neon("ShopSignRepair", "RADIO · TAPE REPAIR", new Color(1f, 0.62f, 0.3f) * 3.6f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        H["NoEntry"] = Neon("SignNoEntry", "AUTHORIZED ONLY", new Color(1f, 0.25f, 0.2f) * 2.6f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        return H;
    }

    /// <summary>부두 콘크리트: 6m(=192px) 판, 줄눈, 얼룩. 알파는 젖은 정도(움푹한 곳이 더 반짝인다)</summary>
    static Color ApronPixel(int x, int y, int size)
    {
        float n = P.Fbm(x * 0.03f, y * 0.03f);
        float fine = P.Hash(x, y) * 0.06f;
        float v = 0.55f + n * 0.25f + fine;
        bool joint = x % 128 < 2 || y % 128 < 2;
        if (joint) v *= 0.45f;
        float wet = Mathf.Clamp01(0.35f + (1f - n) * 0.5f + (joint ? 0.3f : 0f));
        return new Color(v, v, v * 0.98f, wet);
    }

    /// <summary>골판: 세로 골(한 골 = 32px), 녹 얼룩(아래쪽에 많다), 긁힘</summary>
    static Color CorrugatedPixel(int x, int y, int size)
    {
        float ridge = 0.78f + 0.22f * Mathf.Cos(x * Mathf.PI * 2f / 32f);
        float rust = Mathf.Clamp01(P.Fbm(x * 0.04f, y * 0.04f) * 1.6f - 0.75f + (1f - y / (float)size) * 0.25f);
        float grime = 0.9f + P.Hash(x / 3, y) * 0.1f;
        var c = new Color(ridge * grime, ridge * grime, ridge * grime);
        return Color.Lerp(c, new Color(0.55f, 0.32f, 0.18f) * ridge, rust * 0.6f);
    }

    static Color WaterPixel(int x, int y, int size)
    {
        float n = P.Fbm(x * 0.05f, y * 0.12f);
        float v = 0.75f + n * 0.25f;
        return new Color(v, v, v, 1f);
    }

    // =====================================================================
    // 항구 길 · 가게 2 · 철조망 문
    // =====================================================================

    static void HarborRoad(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built b)
    {
        var g = new GameObject("HarborRoad").transform;
        g.SetParent(parent);
        var F = new[] { M["FacadeA"], M["FacadeB"], M["FacadeC"] };
        Material back = M["BuildingBack"];
        // 서쪽: 가게 2를 사이에 둔 두 채, 동쪽: 긴 한 채(z 252~316)
        P.Building(g, "Harbor_W0", new Vector3((140f + AveW) / 2f, 6.5f, (252f + Shop2Z0) / 2f), new Vector3(AveW - 140f, 13f, Shop2Z0 - 252f), F[2], back, Vector3.right);
        P.Building(g, "Harbor_W1", new Vector3((140f + AveW) / 2f, 7.5f, (Shop2Z1 + 316f) / 2f), new Vector3(AveW - 140f, 15f, 316f - Shop2Z1), F[1], back, Vector3.right);
        P.Building(g, "Harbor_E", new Vector3((AveE + 188f) / 2f, 7.5f, 284f), new Vector3(188f - AveE, 15f, 64f), F[0], back, Vector3.left);
        BuildShop2(g, M, X, H, triggers, b);
        HarborGate(g, M, H, b);
    }

    /// <summary>가게 2 「등대 수리점」: 낡은 라디오·카세트 수리점(회사에서 도망친 노인). 1층 앞면 x 155, 거리(+x)를 본다</summary>
    static void BuildShop2(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> X, Dictionary<string, Material> H, Transform triggers, Built b)
    {
        var g = new GameObject("Shop_Lighthouse").transform;
        g.SetParent(parent);
        float depth = 9f, x0 = AveW, x1 = AveW - depth, h = 3.4f, zc = (Shop2Z0 + Shop2Z1) / 2f, wz = Shop2Z1 - Shop2Z0;
        // 위층 외벽(가게 바로 위부터)과 가게 뒤 1층
        var upperSize = new Vector3(15f, 9.5f, wz);
        var upper = P.Box(g, "ShopUpper", new Vector3(x0 - 7.5f, 3.5f + 4.75f, zc), upperSize, M["FacadeA"], new Vector2(16f, 14f));
        upper.GetComponent<MeshFilter>().sharedMesh = P.BoxMesh(upperSize, new Vector2(16f, 14f), new[] { Vector3.right });
        upper.GetComponent<MeshRenderer>().sharedMaterials = new[] { M["FacadeA"], M["BuildingBack"] };
        P.Box(g, "ShopRear", new Vector3((x1 - 0.2f + 140f) / 2f, 1.75f, zc), new Vector3(x1 - 0.2f - 140f, 3.5f, wz), M["BuildingBack"], new Vector2(2f, 2f));
        // 1층 상자
        P.Box(g, "ShopFloor", new Vector3((x0 + x1) / 2f, 0.075f, zc), new Vector3(depth, 0.15f, wz), M["FloorWood"], new Vector2(2f, 2f));
        P.Box(g, "ShopCeiling", new Vector3((x0 + x1) / 2f, h + 0.05f, zc), new Vector3(depth, 0.1f, wz), M["Ceiling"], new Vector2(2f, 2f), collider: false);
        P.Box(g, "ShopBack", new Vector3(x1 - 0.1f, h / 2f, zc), new Vector3(0.2f, h, wz), X["ShopWall"], new Vector2(2f, 2f));
        P.Box(g, "ShopSide_S", new Vector3((x0 + x1) / 2f, h / 2f, Shop2Z0 + 0.1f), new Vector3(depth, h, 0.2f), X["ShopWall"], new Vector2(2f, 2f));
        P.Box(g, "ShopSide_N", new Vector3((x0 + x1) / 2f, h / 2f, Shop2Z1 - 0.1f), new Vector3(depth, h, 0.2f), X["ShopWall"], new Vector2(2f, 2f));
        // 앞면: 아래 벽 · 진열창 · 문 · 위 벽
        float glassZ0 = Shop2Z0 + 0.6f, glassZ1 = Shop2DoorZ - 1.0f, sill = 0.6f, top = 2.75f;
        P.Box(g, "FrontSill", new Vector3(x0 + 0.1f, sill / 2f, (glassZ0 + glassZ1) / 2f), new Vector3(0.24f, sill, glassZ1 - glassZ0), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontHeader", new Vector3(x0 + 0.1f, (top + h) / 2f + 0.05f, zc), new Vector3(0.24f, h - top + 0.1f, wz + 0.2f), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_S", new Vector3(x0 + 0.1f, h / 2f, (Shop2Z0 + glassZ0) / 2f), new Vector3(0.24f, h, glassZ0 - Shop2Z0 + 0.2f), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_Mid", new Vector3(x0 + 0.1f, h / 2f, (glassZ1 + Shop2DoorZ - 0.6f) / 2f), new Vector3(0.24f, h, Shop2DoorZ - 0.6f - glassZ1), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_N", new Vector3(x0 + 0.1f, h / 2f, (Shop2DoorZ + 0.6f + Shop2Z1) / 2f), new Vector3(0.24f, h, Shop2Z1 - Shop2DoorZ - 0.6f + 0.2f), M["FacadeBase"], Vector2.one);
        var glass = P.Box(g, "ShopWindow", new Vector3(x0 + 0.06f, (sill + top) / 2f, (glassZ0 + glassZ1) / 2f), new Vector3(0.03f, top - sill, glassZ1 - glassZ0), X["Glass"], Vector2.one);
        glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        HouseBuilder.InteriorDoor(g, "ShopDoor", new Vector3(x0 + 0.22f, G, Shop2DoorZ), 90f, HouseBuilder.Door("Green"), HouseBuilder.SharedMaterials());
        // 차양·간판: 필기체 "Lighthouse"(하늘색) + "RADIO · TAPE REPAIR"(주황)
        P.Box(g, "Awning", new Vector3(x0 + 0.8f, top + 0.2f, zc), new Vector3(1.4f, 0.12f, wz - 0.4f), X["Awning"], new Vector2(1f, 1f), collider: false)
            .transform.rotation = Quaternion.Euler(0f, 0f, 12f);
        P.Box(g, "Sign", new Vector3(x0 + 0.3f, h + 0.72f, zc), new Vector3(0.16f, 1.3f, 6.4f), X["SignBoard"], Vector2.one, collider: false);
        var name = P.Box(g, "SignLighthouse", new Vector3(x0 + 0.39f, h + 0.98f, zc + 0.2f), new Vector3(0.01f, 0.8f, 3.4f), H["SignLighthouse"], Vector2.one, collider: false);
        FitUVEast(name, 3.4f, 0.8f);
        var repair = P.Box(g, "SignRepair", new Vector3(x0 + 0.39f, h + 0.38f, zc - 0.1f), new Vector3(0.01f, 0.42f, 3.6f), H["SignRepair"], Vector2.one, collider: false);
        FitUVEast(repair, 3.6f, 0.42f);
        P.AddLight(g, "SignLight", new Vector3(x0 + 0.9f, h + 0.9f, zc), LightType.Point, new Color(0.5f, 0.8f, 1f), 1.6f, 6f);

        // 실내: 수리 작업대(진열창 바로 뒤 — 거리에서 보인다), 고친 라디오·카세트, 테이프 선반, 계산대
        foreach (float z in new[] { Shop2Z0 + 1.4f, Shop2Z0 + 2.8f, Shop2Z0 + 4.2f, Shop2Z0 + 5.6f, Shop2Z0 + 7.0f })
            P.Prop("CassetteShelf", g, new Vector3(x1 + 0.2f, G, z), 90f);
        P.Prop("Desk", g, new Vector3(x0 - 1.2f, G, Shop2Z0 + 2.6f), 90f);
        TryProp("CrtTv", g, new Vector3(x0 - 1.35f, G + 0.76f, Shop2Z0 + 2.1f), 90f);
        TryProp("Walkman", g, new Vector3(x0 - 1.0f, G + 0.76f, Shop2Z0 + 3.1f), 120f);
        P.Prop("Desk", g, new Vector3(x0 - 1.2f, G, Shop2Z0 + 4.6f), 90f);
        TryProp("Turntable", g, new Vector3(x0 - 1.25f, G + 0.76f, Shop2Z0 + 4.6f), 90f);
        P.Box(g, "Counter", new Vector3(x0 - 5.6f, G + 0.5f, Shop2Z1 - 1.6f), new Vector3(2.6f, 1f, 0.7f), M["DarkWood"], Vector2.one);
        TryProp("CashRegister", g, new Vector3(x0 - 4.95f, G + 1f, Shop2Z1 - 1.62f), 180f);
        foreach (var (lx, lz) in new[] { (x0 - 1.3f, Shop2Z0 + 2.6f), (x0 - 1.3f, Shop2Z0 + 4.6f), (x0 - 5.6f, Shop2Z1 - 1.6f) })
            P.AddLight(g, "ShopSpot", new Vector3(lx, h - 0.15f, lz), LightType.Spot, new Color(1f, 0.82f, 0.6f), 5f, 4.5f, 70f, Quaternion.Euler(90f, 0f, 0f));
        P.AddLight(g, "ShopLight", new Vector3(x0 - 4f, h - 0.3f, zc), LightType.Point, new Color(1f, 0.74f, 0.48f), 3.6f, 8f);
        P.AddLight(g, "ShopSpill", new Vector3(x0 + 0.35f, 2.3f, (glassZ0 + glassZ1) / 2f), LightType.Point, new Color(1f, 0.7f, 0.45f), 1.1f, 5f);

        var shopGo = new GameObject("TapeShop_Lighthouse");
        shopGo.transform.SetParent(triggers);
        shopGo.transform.position = new Vector3(x0 + 1.3f, 1f, Shop2DoorZ);
        var sc = shopGo.AddComponent<BoxCollider>();
        sc.size = new Vector3(2.6f, 2f, 2.6f);
        sc.isTrigger = true;
        var shop = shopGo.AddComponent<TapeShop>();
        shop.shopName = "등대 수리점";
        // 곡: VARCO Sound(2026-10-08, 연주곡 12곡 중 9곡). 크기 보정 = 평균 -15dB에 맞춘 값
        shop.stock = new List<TapeShop.Item>
        {
            new() { tape = MusicTape("부두의 블루스", "harbor_blues", new[] { 1.15f, 1.11f, 1.15f }, "안개 낀 부두", "닻을 내린 밤", "새벽 첫 배") },
            new() { tape = MusicTape("등대지기의 테이프", "lighthouse", new[] { 0.95f, 0.97f, 1.12f }, "깜박이는 불빛", "파도 소리", "아무도 없는 섬") },
            new() { tape = MusicTape("심야 소울", "dock_soul", new[] { 1.07f, 0.94f, 1.0f }, "젖은 아스팔트", "크레인 아래서", "마지막 교대") },
        };
        b.shop2 = shop;
        b.shop2Approach = Trigger(triggers, "Shop2Approach", new Vector3(AveRoad0 - 1.5f, 1f, zc), new Vector3(3.2f, 2f, wz + 2f));
        b.shop2Save = P.Marker(triggers, "Shop2Save", new Vector3(AveRoad0 - 1.6f, 0.15f, Shop2DoorZ), 90f);
    }

    /// <summary>동쪽(+x)을 보는 얇은 판의 앞면 UV를 0~1로 맞춘다(왼→오 = 북→남)</summary>
    static void FitUVEast(GameObject panel, float width, float height)
    {
        var mf = panel.GetComponent<MeshFilter>();
        var mesh = Object.Instantiate(mf.sharedMesh);
        var verts = mesh.vertices;
        var normals = mesh.normals;
        var uv = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            float u = Mathf.InverseLerp(-width / 2f, width / 2f, verts[i].z); // 동쪽에서 서쪽(-x)을 보면 오른쪽이 +z
            if (normals[i].x < -0.5f) u = 1f - u;
            uv[i] = new Vector2(u, Mathf.InverseLerp(-height / 2f, height / 2f, verts[i].y));
        }
        mesh.uv = uv;
        mesh.name = panel.name + "_UV";
        mf.sharedMesh = mesh;
    }

    /// <summary>항구 철조망 문: 찻길·인도(x 155~173)를 가로막는 미닫이 두 짝 + 경비 초소. 왼짝을 F로 밀어 서쪽(건물 뒤)으로 연다</summary>
    static void HarborGate(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Built b)
    {
        var g = new GameObject("HarborGate").transform;
        g.SetParent(parent);
        float w = (AveE - AveW) / 2f, height = 2.8f;
        // 문 기둥
        foreach (float x in new[] { AveW, AveX, AveE })
            P.Box(g, "GatePost", new Vector3(x, height / 2f + 0.1f, GateZ), new Vector3(0.18f, height + 0.4f, 0.18f), M["Galvanized"], Vector2.one);
        // 미닫이 짝: 철망 틀 + 위 철조망
        Transform Leaf(string name, float x0, float x1)
        {
            var leaf = new GameObject(name).transform;
            leaf.SetParent(g);
            leaf.position = new Vector3((x0 + x1) / 2f, 0f, GateZ + 0.18f);
            float len = x1 - x0 - 0.2f;
            void Bar(Vector3 local, Vector3 size, Material m, bool col = false)
            {
                var bx = P.Box(leaf, "Bar", Vector3.zero, size, m, Vector2.one, collider: col);
                bx.transform.localPosition = local;
                GameObjectUtility.SetStaticEditorFlags(bx, 0); // 움직이는 문
            }
            Bar(new Vector3(0f, 0.15f, 0f), new Vector3(len, 0.08f, 0.06f), M["Galvanized"]);
            Bar(new Vector3(0f, height, 0f), new Vector3(len, 0.08f, 0.06f), M["Galvanized"]);
            Bar(new Vector3(-len / 2f, height / 2f + 0.08f, 0f), new Vector3(0.08f, height - 0.1f, 0.06f), M["Galvanized"]);
            Bar(new Vector3(len / 2f, height / 2f + 0.08f, 0f), new Vector3(0.08f, height - 0.1f, 0.06f), M["Galvanized"]);
            Bar(new Vector3(0f, height / 2f + 0.08f, 0f), new Vector3(len, height - 0.1f, 0.01f), M["ChainLink"]);
            Bar(new Vector3(0f, height + 0.25f, 0f), new Vector3(len, 0.35f, 0.01f), M["ChainLink"]); // 위 철조망 단
            var col = leaf.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, height / 2f + 0.2f, 0f);
            col.size = new Vector3(len, height + 0.4f, 0.25f);
            var obstacle = leaf.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.size = col.size;
            obstacle.center = col.center;
            return leaf;
        }
        var west = Leaf("GateLeaf_W", AveW, AveX);
        Leaf("GateLeaf_E", AveX, AveE);
        b.gateLeaf = west;
        b.gateOpenPos = west.position + Vector3.left * (w - 0.6f);
        b.gateUse = P.Interactable(g, "GateUse", new Vector3(AveX - w / 2f, 1.2f, GateZ - 1.2f), new Vector3(w, 2.4f, 1.6f), "철조망 문을 밀어 연다");
        // 문 위 경고판
        var sign = P.Box(g, "NoEntry", new Vector3(AveX, height + 0.75f, GateZ - 0.05f), new Vector3(3.2f, 0.5f, 0.01f), H["NoEntry"], Vector2.one, collider: false);
        FitUVSouth(sign, 3.2f, 0.5f);
        // 문 안쪽 경비 초소(불 꺼짐 — 아무도 없다)
        P.Box(g, "GuardBooth", new Vector3(AveE + 3f, 1.35f, GateZ + 4f), new Vector3(2.6f, 2.7f, 2.4f), M["FacadeBase"], new Vector2(2f, 2f));
        P.Box(g, "BoothRoof", new Vector3(AveE + 3f, 2.75f, GateZ + 4f), new Vector3(3f, 0.12f, 2.8f), H["Steel"], Vector2.one, collider: false);
        P.AddLight(g, "GateLamp", new Vector3(AveX, height + 1.6f, GateZ - 0.6f), LightType.Spot, new Color(1f, 0.62f, 0.3f), 6f, 10f, 80f, Quaternion.Euler(70f, 180f, 0f));
    }

    // =====================================================================
    // 부두 마당 · 경계
    // =====================================================================

    static void HarborGround(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H)
    {
        var g = new GameObject("HarborGround").transform;
        g.SetParent(parent);
        // 마당 윗면 높이 0(찻길과 같은 높이). 남쪽 끝은 항구 길 찻길(z 318)과 이어진다
        P.Box(g, "Apron", new Vector3((HbX0 + HbX1) / 2f, -0.25f, (HbZ0 + HbZ1) / 2f), new Vector3(HbX1 - HbX0, 0.5f, HbZ1 - HbZ0), H["Apron"], new Vector2(6f, 6f));
        // 노란 차선(부두 큰길) · 보행 금지선
        for (float z = HbZ0 + 4f; z < HbZ1 - 6f; z += 6f)
        {
            P.Box(g, "DockLine_W", new Vector3(190.5f, 0.004f, z), new Vector3(0.15f, 0.01f, 3.2f), H["EdgePaint"], Vector2.one, collider: false);
            P.Box(g, "DockLine_E", new Vector3(207.5f, 0.004f, z), new Vector3(0.15f, 0.01f, 3.2f), H["EdgePaint"], Vector2.one, collider: false);
        }
    }

    static void HarborBoundary(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H)
    {
        var g = new GameObject("HarborBoundary").transform;
        g.SetParent(parent);
        Material back = M["BuildingBack"];
        // 남쪽: 항구 길 양옆 건물 너머로 낮은 창고 줄(마당 쪽을 본다)
        Shed(g, H, "Shed_S_W", new Vector3((HbX0 - 12f + 140f) / 2f, 0f, 309f), new Vector3(140f - HbX0 + 12f, 9f, 18f), Vector3.forward);
        Shed(g, H, "Shed_S_E", new Vector3((188f + HbX1 + 12f) / 2f, 0f, 309f), new Vector3(HbX1 + 12f - 188f, 10f, 18f), Vector3.forward);
        // 서·동: 바다까지 이어지는 긴 창고 줄
        Shed(g, H, "Shed_W", new Vector3(HbX0 - 7f, 0f, (300f + HbZ1 + 8f) / 2f), new Vector3(14f, 11f, HbZ1 + 8f - 300f), Vector3.right);
        Shed(g, H, "Shed_E", new Vector3(HbX1 + 7f, 0f, (300f + HbZ1 + 8f) / 2f), new Vector3(14f, 12f, HbZ1 + 8f - 300f), Vector3.left);
    }

    /// <summary>골판 창고: 벽(골판) + 지붕(골판, 살짝 내민 처마) + 정면 아래 어두운 셔터 문들</summary>
    static GameObject Shed(Transform parent, Dictionary<string, Material> H, string name, Vector3 floorCentre, Vector3 size, Vector3 front)
    {
        var s = new GameObject(name).transform;
        s.SetParent(parent);
        s.position = floorCentre;
        P.Box(s, "Walls", floorCentre + Vector3.up * size.y / 2f, size, H["ShedWall"], new Vector2(4f, 4f));
        P.Box(s, "Roof", floorCentre + Vector3.up * (size.y + 0.15f), size + new Vector3(0.8f, 0f, 0.8f) - Vector3.up * (size.y - 0.3f), H["ShedRoof"], new Vector2(4f, 4f), collider: false);
        // 정면 셔터(12m마다 하나): 판 하나로 칠한다
        Vector3 along = Vector3.Cross(Vector3.up, front).normalized;
        float len = Mathf.Abs(Vector3.Dot(size, along)), half = Mathf.Abs(Vector3.Dot(size, front)) / 2f;
        for (float t = -len / 2f + 6f; t <= len / 2f - 4f; t += 12f)
        {
            Vector3 c = floorCentre + along * t + front * (half + 0.03f) + Vector3.up * 2.6f;
            var door = P.Box(s, "ShedDoor", c, new Vector3(Mathf.Abs(along.x) * 5f + Mathf.Abs(front.x) * 0.04f, 5.2f, Mathf.Abs(along.z) * 5f + Mathf.Abs(front.z) * 0.04f), H["ContGrey"], new Vector2(2f, 2f), collider: false);
            door.GetComponent<MeshRenderer>().sharedMaterial = H["ShedDark"];
        }
        return s.gameObject;
    }

    // =====================================================================
    // 컨테이너 야적장(잠입 길)
    // =====================================================================

    static void ContainerYard(Transform parent, Dictionary<string, Material> H)
    {
        var g = new GameObject("ContainerYard").transform;
        g.SetParent(parent);
        var rnd = new System.Random(3);
        string[] colors = { "ContRed", "ContBlue", "ContGreen", "ContGrey", "ContOrange", "ContBlue", "ContRed" };
        const float cw = 2.44f, ch = 2.59f, cl = 12.19f;
        // 블록: 컨테이너 3개 폭(7.32m) × 1개 길이(12.19m, 남북 방향), 블록 사이 골목 3.2m(가로) · 3.8m(세로)
        float[] bx = { 101f, 111.5f, 122f, 132.5f, 143f };
        float[] bz = { 328f, 344f, 360f, 376f, 392f };
        for (int i = 0; i < bx.Length; i++)
        for (int j = 0; j < bz.Length; j++)
        {
            // 몇 칸은 비워 작은 마당을 만든다(순찰이 돌고, 숨을 곳 사이의 쉼표)
            if ((i == 2 && j == 2) || (i == 4 && j == 0) || (i == 0 && j == 4)) continue;
            for (int k = 0; k < 3; k++)
            {
                if (rnd.NextDouble() < 0.12) continue; // 군데군데 빠진 자리(숨을 틈)
                int stack = 1 + (int)(rnd.NextDouble() * 2.6);
                for (int s = 0; s < stack; s++)
                {
                    var mat = H[colors[rnd.Next(colors.Length)]];
                    float jitter = (float)(rnd.NextDouble() - 0.5) * 0.25f;
                    Container(g, H, new Vector3(bx[i] + cw / 2f + k * cw + jitter * 0.3f, s * ch, bz[j] + cl / 2f + jitter), false, mat);
                }
            }
        }
        // 빈 마당 하나에 가로로 놓인 컨테이너 둘(골목 모양을 깬다)
        Container(g, H, new Vector3(128f, 0f, 366f), true, H["ContOrange"]);
        Container(g, H, new Vector3(128f, 0f, 370.5f), true, H["ContGrey"]);
    }

    /// <summary>컨테이너 한 개(40ft): 골판 상자 + 네 모서리 기둥(어두운 쇠) — 밤에도 윤곽이 읽힌다. alongX면 길이가 x 방향</summary>
    static void Container(Transform parent, Dictionary<string, Material> H, Vector3 floorCentre, bool alongX, Material mat)
    {
        const float cw = 2.44f, ch = 2.59f, cl = 12.19f;
        Vector3 size = alongX ? new Vector3(cl, ch, cw) : new Vector3(cw, ch, cl);
        var box = P.Box(parent, "Container", floorCentre + Vector3.up * ch / 2f, size - new Vector3(0.08f, 0f, 0.08f), mat, new Vector2(2.6f, 2.6f));
        var c = box.GetComponent<BoxCollider>();
        c.size = size;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sz = -1; sz <= 1; sz += 2)
            P.Box(box.transform, "Corner", floorCentre + new Vector3(sx * (size.x / 2f - 0.06f), ch / 2f, sz * (size.z / 2f - 0.06f)), new Vector3(0.14f, ch, 0.14f), H["ContFrame"], Vector2.one, collider: false);
    }

    // =====================================================================
    // 창고 A·B
    // =====================================================================

    static void Warehouses(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H)
    {
        var g = new GameObject("Warehouses").transform;
        g.SetParent(parent);
        // 창고 A: 들어서면 정면을 막아 길을 둘로 나눈다(왼쪽 컨테이너 / 오른쪽 부두 큰길)
        Shed(g, H, "Warehouse_A", new Vector3(175f, 0f, 354f), new Vector3(30f, 10f, 32f), Vector3.left);
        // 창고 B: 부두 큰길 동쪽. 큰 문이 열려 있다(안에 회사 테이프 상자) — 열린 문은 다음 단계에서 판다
        Shed(g, H, "Warehouse_B", new Vector3(228f, 0f, 344f), new Vector3(28f, 11f, 36f), Vector3.left);
    }

    // =====================================================================
    // 안벽 · 크레인 · 3번 부두 · 배 · 바다
    // =====================================================================

    static void Quay(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H, Built b)
    {
        var g = new GameObject("Quay").transform;
        g.SetParent(parent);
        // 바다: 카메라를 따라 출렁이는 거스트너 파도(OceanSurface). 마당보다 1.6m 낮다
        var sea = new GameObject("Ocean");
        sea.transform.SetParent(g);
        sea.transform.position = new Vector3(170f, WaterY, HbZ1 + 40f);
        sea.AddComponent<MeshFilter>();
        var seaR = sea.AddComponent<MeshRenderer>();
        seaR.sharedMaterial = WaterMaterial();
        seaR.shadowCastingMode = ShadowCastingMode.Off;
        sea.AddComponent<OceanSurface>().minZ = HbZ1 + 0.2f;
        // 안벽 아래 어두운 물(바다 판이 카메라를 따라 멀어져도 안벽 밑이 비어 보이지 않게)
        P.Box(g, "WaterUnder", new Vector3(170f, WaterY - 0.6f, 620f), new Vector3(800f, 0.1f, 400f), H["Water"], new Vector2(12f, 12f), collider: false);
        // 안벽 앞면(마당 끝에서 물까지) · 노란 모서리 줄 · 고무 방충재
        P.Box(g, "QuayFace", new Vector3((HbX0 + HbX1) / 2f, (WaterY - 0.6f) / 2f - 0.25f, HbZ1 + 0.3f), new Vector3(HbX1 - HbX0, -WaterY + 0.6f, 0.6f), H["QuayEdge"], new Vector2(4f, 4f));
        P.Box(g, "QuayStripe", new Vector3((HbX0 + HbX1) / 2f, 0.02f, HbZ1 - 0.2f), new Vector3(HbX1 - HbX0, 0.04f, 0.3f), H["EdgePaint"], Vector2.one, collider: false);
        for (float x = HbX0 + 6f; x < HbX1 - 3f; x += 12f)
        {
            if (x > PierX0 - 2f && x < PierX1 + 2f) continue;
            P.Box(g, "Fender", new Vector3(x, -0.7f, HbZ1 + 0.75f), new Vector3(1.4f, 1.1f, 0.4f), H["Fender"], Vector2.one, collider: false);
            Bollard(g, H, new Vector3(x + 6f, 0f, HbZ1 - 0.9f));
        }
        // 물에 빠지지 않게: 안벽 끝 보이지 않는 벽(점프로 넘지 못할 높이)
        InvisibleWall(g, "QuayStop_W", new Vector3((HbX0 + PierX0) / 2f, 1.5f, HbZ1 + 0.2f), new Vector3(PierX0 - HbX0, 3f, 0.4f));
        InvisibleWall(g, "QuayStop_E", new Vector3((PierX1 + HbX1) / 2f, 1.5f, HbZ1 + 0.2f), new Vector3(HbX1 - PierX1, 3f, 0.4f));

        // 3번 부두: 바다로 뻗은 콘크리트 판(마당과 같은 높이) + 기둥 + 끝·옆 막이
        float pz = (HbZ1 + PierZ1) / 2f, pl = PierZ1 - HbZ1;
        P.Box(g, "Pier3", new Vector3((PierX0 + PierX1) / 2f, -0.25f, pz), new Vector3(PierX1 - PierX0, 0.5f, pl), H["Apron"], new Vector2(6f, 6f));
        for (float z = HbZ1 + 4f; z < PierZ1; z += 8f)
            foreach (float x in new[] { PierX0 + 0.6f, PierX1 - 0.6f })
                P.Box(g, "PierPile", new Vector3(x, (WaterY - 3f) / 2f - 0.5f, z), new Vector3(0.7f, -WaterY + 2f, 0.7f), H["QuayEdge"], Vector2.one, collider: false);
        P.Box(g, "PierStripe_W", new Vector3(PierX0 + 0.2f, 0.02f, pz), new Vector3(0.3f, 0.04f, pl), H["EdgePaint"], Vector2.one, collider: false);
        P.Box(g, "PierStripe_E", new Vector3(PierX1 - 0.2f, 0.02f, pz), new Vector3(0.3f, 0.04f, pl), H["EdgePaint"], Vector2.one, collider: false);
        for (float z = HbZ1 + 6f; z < PierZ1 - 2f; z += 10f)
        {
            Bollard(g, H, new Vector3(PierX0 + 0.7f, 0f, z));
            Bollard(g, H, new Vector3(PierX1 - 0.7f, 0f, z + 5f));
        }
        InvisibleWall(g, "PierStop_W", new Vector3(PierX0 - 0.2f, 1.5f, pz), new Vector3(0.4f, 3f, pl));
        InvisibleWall(g, "PierStop_E", new Vector3(PierX1 + 0.2f, 1.5f, pz), new Vector3(0.4f, 3f, pl));
        InvisibleWall(g, "PierStop_N", new Vector3((PierX0 + PierX1) / 2f, 1.5f, PierZ1 + 0.2f), new Vector3(PierX1 - PierX0, 3f, 0.4f));
        var sign = P.Box(g, "SignPier3", new Vector3((PierX0 + PierX1) / 2f, 5.6f, HbZ1 + 0.4f), new Vector3(3.6f, 0.9f, 0.01f), H["SignPier"], Vector2.one, collider: false);
        FitUVSouth(sign, 3.6f, 0.9f);
        foreach (float x in new[] { PierX0 + 0.3f, PierX1 - 0.3f })
            P.Box(g, "SignPost", new Vector3(x, 3f, HbZ1 + 0.4f), new Vector3(0.14f, 6f, 0.14f), H["Steel"], Vector2.one);
        P.Box(g, "SignBeam", new Vector3((PierX0 + PierX1) / 2f, 6.1f, HbZ1 + 0.4f), new Vector3(PierX1 - PierX0, 0.14f, 0.14f), H["Steel"], Vector2.one, collider: false);

        // 정박한 화물선(Blender로 만든 모델: Assets/Game/Art/Harbor/CargoShip.fbx, 원점 = 용골 가운데). 길이 64m, 갑판 9m
        float sx = PierX1 + 8.5f, sz = (HbZ1 + 6f + PierZ1 + 10f) / 2f;
        var shipAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Harbor/CargoShip.fbx");
        GameObject shipGo;
        if (shipAsset)
        {
            shipGo = (GameObject)PrefabUtility.InstantiatePrefab(shipAsset, g);
            shipGo.name = "ShipHull";
            shipGo.transform.SetPositionAndRotation(new Vector3(sx, WaterY - 3.2f, sz), Quaternion.identity); // 3.2m 잠긴다(붉은 아랫부분이 물에)
            foreach (var r in shipGo.GetComponentsInChildren<MeshRenderer>()) { r.shadowCastingMode = ShadowCastingMode.On; GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic); }
            var col = shipGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 4.5f, 0f);
            col.size = new Vector3(13f, 9f, 60f);
        }
        else
        {
            float sl = PierZ1 + 10f - HbZ1 - 6f;
            shipGo = P.Box(g, "ShipHull", new Vector3(sx, WaterY + 2.6f, sz), new Vector3(13f, 7f, sl), H["Hull"], new Vector2(4f, 4f));
        }
        // 갑판 작업등 몇 개(밤에 배 윤곽이 보이게)
        foreach (float dz in new[] { -20f, 0f, 18f })
            P.AddLight(g, "ShipDeckLamp", new Vector3(sx - 6.8f, WaterY - 3.2f + 12f, sz + dz), LightType.Point, new Color(1f, 0.75f, 0.45f), 6f, 14f);
        b.ship = g.Find("ShipHull");

        // 갠트리 크레인: 안벽 위 다리 넷 + 가로대 + 바다로 내민 붐
        Crane(g, H, new Vector3(173f, 0f, HbZ1 - 7f), b);
    }

    /// <summary>바다 재질: 어두운 청록, 아주 매끄럽게(하늘·불빛이 비친다), 잔물결 노멀맵</summary>
    static Material WaterMaterial()
    {
        var mat = P.Lit("HarborSea", null, new Color(0.02f, 0.06f, 0.075f), 0.94f);
        string path = $"{P.ArtRoot}/Textures/SeaRipples.png";
        const int n = 256;
        var hgt = new float[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            // 타일링되는 잔물결 높이: 여러 방향 사인 + 잡음
            float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
            hgt[y * n + x] = Mathf.Sin(u * 3 + v * 2) * 0.35f + Mathf.Sin(u * -5 + v * 4 + 1.3f) * 0.25f + Mathf.Sin(u * 9 - v * 7 + 2.1f) * 0.15f
                             + Mathf.Sin(u * 14 + v * 11 + 0.4f) * 0.08f;
        }
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = hgt[y * n + (x + 1) % n] - hgt[y * n + (x + n - 1) % n];
            float dy = hgt[((y + 1) % n) * n + x] - hgt[((y + n - 1) % n) * n + x];
            var nrm = new Vector3(-dx * 2.2f, -dy * 2.2f, 1f).normalized;
            px[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
        }
        P.SaveTexture("SeaRipples", n, n, px, false);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.NormalMap;
        ti.SaveAndReimport();
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        mat.SetFloat("_BumpScale", 0.6f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_EnvironmentReflections", 1f);
        mat.SetFloat("_SpecularHighlights", 1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 하늘: 그라데이션·별·수평선 도시 실루엣·새벽 해(Game/HarborSky). 장면 전체 하늘로 쓰고, 하늘만 비추는 반사 탐침으로 바다에 하늘이 비치게 한다.
    /// 새벽 장면에서 SkyCycle이 밤 → 새벽으로 바꾼다
    /// </summary>
    internal static SkyCycle SetupHarborSky(Camera cam)
    {
        string path = $"{P.ArtRoot}/Materials/HarborSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null) { sky = new Material(Shader.Find("Game/HarborSky")); AssetDatabase.CreateAsset(sky, path); }
        sky.SetVector("_SunDir", new Vector4(0.35f, 0.05f, 1f, 0f));
        sky.SetFloat("_Dawn", 0f);
        sky.SetFloat("_Stars", 0f); // 비 오는 밤: 별 대신 구름
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.fogColor = new Color(0.05f, 0.055f, 0.08f); // 수평선 색과 맞춰 먼 바다가 하늘로 녹아들게
        cam.clearFlags = CameraClearFlags.Skybox;
        var go = new GameObject("SkyCycle");
        var cycle = go.AddComponent<SkyCycle>();
        cycle.sky = sky;
        cycle.moon = RenderSettings.sun;
        // 하늘만 담는 실시간 반사 탐침(컬링 마스크 없음 → 하늘만 그려 싸다)
        var probe = new GameObject("SkyReflection").AddComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(170f, 10f, 420f);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.cullingMask = 0;
        probe.size = new Vector3(2000f, 400f, 2000f);
        probe.resolution = 128;
        probe.importance = 0;
        cycle.probe = probe;
        return cycle;
    }

    static void Bollard(Transform parent, Dictionary<string, Material> H, Vector3 at)
    {
        P.Box(parent, "Bollard", at + Vector3.up * 0.35f, new Vector3(0.45f, 0.7f, 0.45f), H["Bollard"], Vector2.one);
        P.Box(parent, "BollardCap", at + Vector3.up * 0.74f, new Vector3(0.62f, 0.1f, 0.62f), H["Bollard"], Vector2.one, collider: false);
    }

    static void InvisibleWall(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>갠트리 크레인(높이 26m): 다리 넷(x로 16m, z로 10m 간격), 위 가로대, 바다 쪽으로 40m 내민 붐, 운전실. 탐조등 자리를 남긴다</summary>
    static void Crane(Transform parent, Dictionary<string, Material> H, Vector3 c, Built b)
    {
        var g = new GameObject("GantryCrane").transform;
        g.SetParent(parent);
        const float hx = 8f, hz = 5f, top = 22f;
        foreach (float sx in new[] { -hx, hx })
        foreach (float sz in new[] { -hz, hz })
        {
            P.Box(g, "Leg", c + new Vector3(sx, top / 2f, sz), new Vector3(1.1f, top, 1.1f), H["CraneYellow"], new Vector2(2f, 2f));
            P.Box(g, "Bogie", c + new Vector3(sx, 0.5f, sz), new Vector3(1.8f, 1f, 3f), H["Steel"], Vector2.one);
        }
        foreach (float sz in new[] { -hz, hz })
        {
            P.Box(g, "Beam_X", c + new Vector3(0f, top + 0.8f, sz), new Vector3(hx * 2f + 1.2f, 1.6f, 1.2f), H["CraneYellow"], new Vector2(2f, 2f), collider: false);
            P.Box(g, "Brace_X", c + new Vector3(0f, 7f, sz), new Vector3(hx * 2f, 0.6f, 0.6f), H["CraneYellow"], Vector2.one, collider: false);
        }
        foreach (float sx in new[] { -hx, hx })
            P.Box(g, "Beam_Z", c + new Vector3(sx, top + 0.8f, 0f), new Vector3(1.2f, 1.6f, hz * 2f + 1.2f), H["CraneYellow"], new Vector2(2f, 2f), collider: false);
        // 붐: 바다 쪽(+z)으로 내민 두 가닥 + 뒤로 짧게
        foreach (float sx in new[] { -2.2f, 2.2f })
            P.Box(g, "Boom", c + new Vector3(sx, top + 2.4f, 14f), new Vector3(1f, 1.6f, 52f), H["CraneYellow"], new Vector2(2f, 2f), collider: false);
        P.Box(g, "Cab", c + new Vector3(0f, top + 1.2f, hz + 2.5f), new Vector3(3.4f, 2.4f, 3f), H["Steel"], Vector2.one, collider: false);
        // 탐조등: 크레인 남쪽 가로대 바깥(다리 기둥 충돌체에서 떨어지게)
        b.craneTop = P.Marker(g, "SearchlightMount", c + new Vector3(-hx - 1.6f, top + 0.6f, -hz - 1.6f), 0f);
        P.Box(g, "SearchlightHousing", b.craneTop.position + Vector3.up * 0.1f, new Vector3(1.1f, 0.9f, 1.1f), H["Steel"], Vector2.one, collider: false);
    }

    // =====================================================================
    // 소품(바로크) — 엄폐물·분위기. 프리팹이 아직 없으면 건너뛴다
    // =====================================================================

    static FireFlicker builtFire;
    static Transform builtBarrel;

    static void HarborProps(Transform parent, Transform triggers)
    {
        var g = new GameObject("HarborProps").transform;
        g.SetParent(parent);
        (string name, Vector3 at, float yaw)[] props =
        {
            // 들어서자마자: 창고 A 남쪽에 세워 둔 지게차
            ("Forklift", new Vector3(166f, 0f, 331f), -20f),
            ("PalletStack", new Vector3(170.5f, 0f, 332.5f), 10f),
            // 컨테이너 골목 속 숨을 곳
            ("SteelDrums", new Vector3(131f, 0f, 342.2f), 30f),
            ("PalletStack", new Vector3(110f, 0f, 390f), -15f),
            ("NetPile", new Vector3(141.6f, 0f, 358f), 60f),
            ("SteelDrums", new Vector3(120.4f, 0f, 374f), -40f),
            // 부두 큰길 싸움터(엄폐물)
            ("Forklift", new Vector3(200f, 0f, 381f), 35f),
            ("PalletStack", new Vector3(194.5f, 0f, 390f), 0f),
            ("PalletStack", new Vector3(214f, 0f, 386f), 20f),
            ("SteelDrums", new Vector3(206.5f, 0f, 396f), 10f),
            ("SteelDrums", new Vector3(220f, 0f, 377f), -25f),
            ("CrateStack", new Vector3(226f, 0f, 392f), 90f),
            ("PalletBoxes", new Vector3(192.5f, 0f, 375f), 0f),
            // 3번 부두 앞(반장과 싸우는 곳)
            ("BurnBarrel", new Vector3(201f, 0f, 413.5f), 0f),
            ("SteelDrums", new Vector3(189.5f, 0f, 409f), 15f),
            ("PalletStack", new Vector3(224f, 0f, 408f), -10f),
            ("NetPile", new Vector3(227f, 0f, 418f), 0f),
            ("NetPile", new Vector3(183.5f, 0f, 412f), 90f),
            ("RopeCoil", new Vector3(187f, 0f, 421.6f), 0f),
            ("RopeCoil", new Vector3(216.5f, 0f, 421.4f), 140f),
            ("RopeCoil", new Vector3(PierX0 + 1.4f, 0f, 446f), 60f),
        };
        foreach (var (name, at, yaw) in props) TryProp(name, g, at, yaw);
        // 드럼통 불(반장이 테이프를 던지려는 곳): 불빛 + 불티
        var fire = P.AddLight(g, "BarrelFire", new Vector3(201f, 1.3f, 413.5f), LightType.Point, new Color(1f, 0.52f, 0.2f), 7f, 11f);
        builtFire = fire.gameObject.AddComponent<FireFlicker>();
        builtFire.baseIntensity = 7f;
        fire.shadows = LightShadows.Soft;
        builtBarrel = P.Marker(triggers, "BurnBarrel", new Vector3(201f, 0f, 413.5f), 0f);
    }

    // =====================================================================
    // 적: 컨테이너 순찰 · 탐조등 · 부두 큰길 무리 · 3번 부두 앞
    // =====================================================================

    static void HarborEnemies(Transform parent, Transform people, Transform routes, Transform triggers, Dictionary<string, Material> H, Built b)
    {
        // 컨테이너 골목(세로 골목 x 109.9/120.4/130.9/141.4, 가로 골목 z 342.1/358.1/374.1/390.1)
        Patrol(people, routes, "Thug_Yard_A", new Vector3(120.4f, 0.02f, 334f), 0f,
            new Vector3(120.4f, 0.02f, 334f), new Vector3(120.4f, 0.02f, 400f));
        Patrol(people, routes, "Thug_Yard_B", new Vector3(146f, 0.02f, 358.1f), -90f,
            new Vector3(146f, 0.02f, 358.1f), new Vector3(104f, 0.02f, 358.1f));
        Patrol(people, routes, "Thug_Yard_C", new Vector3(141.4f, 0.02f, 398f), 180f,
            new Vector3(141.4f, 0.02f, 398f), new Vector3(141.4f, 0.02f, 376f), new Vector3(130.9f, 0.02f, 374.1f), new Vector3(141.4f, 0.02f, 376f));
        // 창고 A 서쪽 트인 길(곧장 가는 길)을 지키는 순찰
        Patrol(people, routes, "Thug_WestLane", new Vector3(155.5f, 0.02f, 336f), 0f,
            new Vector3(155.5f, 0.02f, 336f), new Vector3(155.5f, 0.02f, 396f));
        // 부두 큰길 들머리 순찰
        Patrol(people, routes, "Thug_Road", new Vector3(199f, 0.02f, 334f), 0f,
            new Vector3(199f, 0.02f, 334f), new Vector3(199f, 0.02f, 366f));

        // 크레인 탐조등: 컨테이너 야적장과 서쪽 트인 길을 천천히 훑는다
        var sl = new GameObject("CraneSearchlight");
        sl.transform.SetParent(parent);
        sl.transform.position = b.craneTop.position;
        var light = P.AddLight(sl.transform, "Beam", Vector3.zero, LightType.Spot, new Color(0.85f, 0.92f, 1f), 900f, 120f, 12f, local: true);
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
        search.path = new[]
        {
            new Vector3(106f, 0f, 336f), new Vector3(150f, 0f, 336f), new Vector3(156f, 0f, 366f),
            new Vector3(150f, 0f, 398f), new Vector3(106f, 0f, 398f), new Vector3(118f, 0f, 366f),
        };
        b.searchlight = search;

        // 부두 큰길 싸움터: 조직원 셋 + 팔레트 위 총잡이. 들어서거나 들키면 싸움
        var f1 = Fighter(people, "Thug_Dock_A", new Vector3(203f, 0.02f, 386f), 180f, false);
        var f2 = Fighter(people, "Thug_Dock_B", new Vector3(210f, 0.02f, 392f), 200f, false);
        var f3 = Fighter(people, "Thug_Dock_C", new Vector3(196f, 0.02f, 396f), 160f, false);
        var gun = Fighter(people, "Thug_Dock_Gunner", new Vector3(218f, 0.02f, 398f), 210f, true);
        gun.holdPosition = true;
        b.dockZone = Zone(triggers, "DockZone", new Vector3(208f, 1.5f, 384f), new Vector3(34f, 3f, 26f), f1, f2, f3, gun);

        // 3번 부두 앞: 정리반 반장 + 곁의 부하 둘 + 2단계에 부르는 부하 둘(배 옆에 숨어 있다)
        var p1 = Fighter(people, "Thug_Pier_A", new Vector3(195f, 0.02f, 416f), 180f, false);
        var p2 = Fighter(people, "Thug_Pier_B", new Vector3(210f, 0.02f, 417f), 180f, false);
        var r1 = Fighter(people, "Thug_Pier_C", new Vector3(PierX0 + 2f, 0.02f, HbZ1 + 8f), 180f, false);
        var r2 = Fighter(people, "Thug_Pier_D", new Vector3(PierX1 - 2f, 0.02f, HbZ1 + 10f), 180f, false);
        r1.gameObject.SetActive(false);
        r2.gameObject.SetActive(false);
        var boss = P.CreateThug(people, "Boss_Cleaner", new Vector3(201f, 0.02f, 417.5f), 180f, "Cleaner");
        SetCombat(boss, false);
        boss.displayName = "정리반 반장";
        boss.isBoss = true;
        boss.GetComponent<Health>().maxHealth = 320; // EnemyAI.Health는 실행 중에만 채워진다
        boss.damage = 12;
        boss.heavyEvery = 2;
        boss.heavyDamage = 24;
        boss.windup = 0.5f;
        boss.heavyWindup = 0.85f;
        boss.attackCooldown = 1.15f;
        boss.recover = 0.45f;
        boss.staggerable = false; // 주먹 한 대로 끊기지 않는다(완벽 회피로 만든 빈틈에 몰아친다)
        boss.shootRange = 16f;
        boss.aimTime = 1.0f;
        boss.shotDamage = 14;
        boss.shotCooldown = 2.2f;
        var brain = boss.gameObject.AddComponent<BossCleaner>();
        brain.reinforcements = new[] { r1, r2 };
        brain.coverPoint = P.Marker(triggers, "BossCover", new Vector3(188.2f, 0.02f, 407.4f), 120f);
        // 왼손에 대니의 테이프
        var hand = boss.body.GetBoneTransform(HumanBodyBones.LeftHand);
        var tape = P.Prop("CassetteTape", hand, hand.position, 0f);
        tape.transform.localPosition = new Vector3(0f, 0.08f, 0.02f);
        tape.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        tape.transform.localScale = Vector3.one * 1.2f;
        foreach (var t in tape.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        brain.heldTape = tape.transform;
        b.boss = boss;
        b.pierThugs = new[] { p1, p2 };
        b.pierZone = Zone(triggers, "PierZone", new Vector3(202f, 1.5f, 413f), new Vector3(44f, 3f, 18f), boss, p1, p2);

        // 테이프: 컨테이너 깊숙한 곳(잠입 길의 보상) · 창고 B 앞(회복약과 함께)
        TapeSpot(parent, triggers, "YardTape", new Vector3(125.6f, 0f, 362.2f), 2, 1, box: true);
        TapeSpot(parent, triggers, "WarehouseTape", new Vector3(212.6f, 0f, 340f), 1, 1, box: true);
    }

    static CombatZone Zone(Transform triggers, string name, Vector3 centre, Vector3 size, params EnemyAI[] enemies)
    {
        var go = new GameObject(name);
        go.transform.SetParent(triggers);
        go.transform.position = centre;
        var c = go.AddComponent<BoxCollider>();
        c.size = size;
        c.isTrigger = true;
        var zone = go.AddComponent<CombatZone>();
        zone.enemies = enemies;
        zone.saveOnEnter = false;
        return zone;
    }

    // =====================================================================
    // 조명
    // =====================================================================

    static void HarborLights(Transform parent, Dictionary<string, Material> M, Dictionary<string, Material> H)
    {
        var g = new GameObject("HarborLights").transform;
        g.SetParent(parent);
        // 높은 투광등 기둥(나트륨 주황): 넓은 마당을 군데군데 웅덩이처럼 밝힌다 — 사이사이는 어둡게(숨을 곳)
        var poles = new[]
        {
            new Vector3(158f, 0f, 334f), new Vector3(200f, 0f, 330f), new Vector3(156.5f, 0f, 392f),
            new Vector3(199f, 0f, 380f), new Vector3(232f, 0f, 400f), new Vector3(120f, 0f, 418f), new Vector3(214f, 0f, 418f),
        };
        // 그림자는 넷만(가운데 길목): 다 켜면 그림자 지도가 모자라 해상도가 떨어진다
        for (int i = 0; i < poles.Length; i++) FloodPole(g, H, poles[i], i < 4);
        // 컨테이너 골목 안쪽은 작은 작업등 몇 개만
        foreach (var p in new[] { new Vector3(118.5f, 2.9f, 342f), new Vector3(140f, 2.9f, 374f), new Vector3(108f, 2.9f, 390f) })
            P.AddLight(g, "AisleLamp", p, LightType.Point, new Color(1f, 0.66f, 0.32f), 6f, 12f);
    }

    static void FloodPole(Transform parent, Dictionary<string, Material> H, Vector3 at, bool shadows)
    {
        const float hgt = 12f;
        P.Box(parent, "FloodPole", at + Vector3.up * hgt / 2f, new Vector3(0.35f, hgt, 0.35f), H["Steel"], Vector2.one);
        P.Box(parent, "FloodHead", at + Vector3.up * (hgt + 0.2f), new Vector3(1.4f, 0.35f, 0.6f), H["Steel"], Vector2.one, collider: false);
        P.Box(parent, "FloodGlow", at + Vector3.up * (hgt + 0.02f), new Vector3(1.2f, 0.04f, 0.45f), H["Sodium"], Vector2.one, collider: false);
        // 12m 위에서 비춘다: 빛 세기는 거리 제곱으로 줄어서(12m → 1/144) 낮은 가로등보다 훨씬 세게
        var l = P.AddLight(parent, "Flood", at + Vector3.up * (hgt - 0.2f), LightType.Spot, new Color(1f, 0.64f, 0.3f), 320f, 40f, 120f, Quaternion.Euler(90f, 0f, 0f));
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }
}
