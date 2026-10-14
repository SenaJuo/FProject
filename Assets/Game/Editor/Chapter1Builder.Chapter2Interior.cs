using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using P = PrologueBuilder;

/// <summary>
/// 2장 구역 2 「경찰서 내부」 — 바깥 건물과 따로, 장면의 x 300~340에 짓는다(뒷문에서 어둡게 넘어간다).
///
/// 1층(바닥 0): 뒤 복도 z 0~5(서쪽 끝 뒷문으로 들어온다) · 당직실 x 300~322, z 5~18(문 x 312) · 사물함실 x 322~334(문 x 328)
///   · 계단실 x 334~340(계단 z 8→16, 2층 4.2m) · 북쪽 z 18~30은 유치장(막힘).
/// 2층(바닥 4.2): 복도 z 16~20(x 300~340) · 북: 강력반 x 300~326(문 x 304·314) · 증거물 보관실 x 326~340(비밀번호 문 x 333)
///   · 남: 기록실 x 300~318(잠긴 문 x 309) · 휴게실 x 318~334(문 x 326, 증거물 담당이 기다린다) · 계단 구멍 x 334~340, z 8~16.
///     기록실·휴게실은 z 6~16(그 남쪽 z 0~6은 벽 너머 창고): 2026-10-14 "빈 바닥이 많다"로 깊이 16m를 10m로 줄였다.
/// </summary>
public static partial class Chapter1Builder
{
    const float IX0 = 300f, IX1 = 340f, IZ0 = 0f, IZ1 = 30f, F2 = 4.2f, WallH = 4.2f;

    class BuiltInside
    {
        public Transform entry, stairsTop, keypadSpot, evidenceInside, recordsInside, clerkLounge, clerkKeypad, clerkInside, records, evidence, rayDesk;
        public StoryTrigger floor2Trigger;
        public StoryInteractable keypadUse, dannyBox, recordsDoorUse, terminalUse, rayDeskUse;
        public Transform evidenceDoor, recordsDoor;
        public EvidenceClerk clerk;
    }

    static Dictionary<string, Material> StationMaterials()
    {
        var S = new Dictionary<string, Material>();
        // 바깥 콘크리트 텍스처는 짙은 회색이라 칠한 벽이 검게 보였다: 밝은 페인트 질감을 따로 쓴다
        S["Wall"] = P.Lit("StationWallPaint", P.Tex("StationPaint", 256, PaintPixel), new Color(0.78f, 0.82f, 0.76f), 0.2f);
        S["WallLow"] = P.Lit("StationWallLow", null, new Color(0.26f, 0.31f, 0.3f), 0.35f); // 허리 아래 짙은 칠
        S["Floor"] = P.Lit("StationLino", P.Tex("StationLino", 256, LinoPixel), new Color(0.55f, 0.57f, 0.6f), 0.25f); // 광택을 낮춰 걸을 때 타일 줄이 반짝이지 않게
        S["Ceiling"] = P.Lit("StationCeiling", null, new Color(0.7f, 0.7f, 0.68f), 0.1f);
        S["Door"] = P.Lit("StationDoor", null, new Color(0.32f, 0.36f, 0.4f), 0.35f, metallic: 0.3f);
        S["Steel"] = P.Lit("StationSteel", null, new Color(0.25f, 0.26f, 0.27f), 0.4f, metallic: 0.6f);
        S["Fluor"] = P.Unlit("StationFluor", new Color(0.95f, 1f, 0.95f) * 2.4f);
        S["Board"] = P.Lit("StationWhiteboard", null, new Color(0.88f, 0.88f, 0.86f), 0.6f);
        S["Keypad"] = P.Unlit("StationKeypadGlow", new Color(0.4f, 1f, 0.5f) * 2f);
        return S;
    }

    /// <summary>칠한 벽: 거의 흰 바탕에 아주 옅은 얼룩(롤러 자국)</summary>
    static Color PaintPixel(int x, int y, int size)
    {
        float v = 0.9f + (P.Fbm(x * 0.03f, y * 0.03f) - 0.5f) * 0.08f;
        return new Color(v, v, v, 1f);
    }

    /// <summary>리놀륨 바닥: 40cm 타일 두 색 교차 + 닳은 얼룩</summary>
    static Color LinoPixel(int x, int y, int size)
    {
        bool dark = ((x / 32) + (y / 32)) % 2 == 0;
        float n = P.Fbm(x * 0.05f, y * 0.05f) * 0.12f;
        float v = (dark ? 0.62f : 0.74f) + n;
        bool seam = x % 32 == 0 || y % 32 == 0;
        if (seam) v *= 0.8f;
        return new Color(v, v * 1.01f, v * 1.03f, 1f);
    }

    static void Ch2Interior(Transform parent, Dictionary<string, Material> M, Transform triggers, Transform people, Transform routes, BuiltInside b)
    {
        var S = StationMaterials();
        var g = new GameObject("StationInterior").transform;
        g.SetParent(parent);

        // ---------- 바닥·천장(층마다) ----------
        P.Box(g, "Floor1", new Vector3((IX0 + IX1) / 2f, -0.15f, (IZ0 + IZ1) / 2f), new Vector3(IX1 - IX0, 0.3f, IZ1 - IZ0), S["Floor"], new Vector2(3.2f, 3.2f));
        // 2층 바닥: 계단 구멍(x 334~340, z 8~16)을 비운다
        Slab(g, S["Floor"], S["Ceiling"], IX0, 334f, IZ0, IZ1);
        Slab(g, S["Floor"], S["Ceiling"], 334f, IX1, IZ0, 8f);
        Slab(g, S["Floor"], S["Ceiling"], 334f, IX1, 16f, IZ1);
        P.Box(g, "Roof", new Vector3((IX0 + IX1) / 2f, F2 + WallH + 0.15f, (IZ0 + IZ1) / 2f), new Vector3(IX1 - IX0, 0.3f, IZ1 - IZ0), S["Ceiling"], new Vector2(2f, 2f));

        // ---------- 1층 벽 ----------
        float y = 0f;
        WallX(g, S, IZ0, IX0, IX1, y);                                  // 남쪽 바깥(뒷문은 서쪽 끝 판으로)
        WallX(g, S, IZ1, IX0, IX1, y);
        WallZ(g, S, IX0, IZ0, IZ1, y, (2.5f, 1.4f));                     // 서쪽 바깥: 뒷문(z 2.5)
        WallZ(g, S, IX1, IZ0, IZ1, y);
        WallX(g, S, 5f, IX0, 334f, y, (312f, 1.6f), (328f, 1.6f));       // 복도 북쪽: 당직실·사물함실 문
        WallX(g, S, 18f, IX0, 334f, y);                                  // 유치장(막힘)
        WallZ(g, S, 322f, 5f, 18f, y);                                   // 당직실 | 사물함실
        WallZ(g, S, 334f, 5f, IZ1, y, (6.5f, 2.6f));                     // 계단실 입구(복도 쪽 z 5~8)

        // ---------- 2층 벽 ----------
        y = F2;
        WallX(g, S, IZ0, IX0, IX1, y);
        WallX(g, S, IZ1, IX0, IX1, y);
        WallZ(g, S, IX0, IZ0, IZ1, y);
        WallZ(g, S, IX1, IZ0, IZ1, y);
        WallX(g, S, 20f, IX0, IX1, y, (304f, 1.6f), (314f, 1.6f), (333f, 1.6f)); // 복도 북: 강력반 문 둘 · 보관실 비밀번호 문
        WallX(g, S, 16f, IX0, 334f, y, (309f, 1.6f), (326f, 1.6f));              // 복도 남: 기록실 · 휴게실 문
        WallZ(g, S, 326f, 20f, IZ1, y);                                           // 강력반 | 보관실
        WallZ(g, S, 318f, IZ0, 16f, y);                                           // 기록실 | 휴게실
        WallZ(g, S, 334f, IZ0, 8f, y);                                            // 휴게실 | 계단 위
        WallX(g, S, 6f, IX0, 334f, y);                                            // 기록실·휴게실 뒤 창고(막힘)
        // 기록실은 가운데 10 × 6.6 m만(2026-10-14 "너무 넓다": 18 × 10 m에 캐비닛 줄과 책상 하나뿐이었다). 나머지는 막힌 창고
        WallZ(g, S, 304f, 6f, 16f, y);
        WallZ(g, S, 314f, 6f, 16f, y);
        WallX(g, S, 9.4f, 304f, 314f, y);

        // ---------- 계단(1층 z 8 → 2층 z 16, 동쪽 x 334~340) ----------
        Stairs(g, S, new Vector3(337f, 0f, 8f), 6f, 8f, F2);
        // 2층 계단 가장자리 난간(구멍에 떨어지지 않게): 서쪽 면
        P.Box(g, "StairRail", new Vector3(334.05f, F2 + 0.5f, 12f), new Vector3(0.1f, 1f, 8f), S["Steel"], Vector2.one);

        // ---------- 문 ----------
        b.evidenceDoor = Door(g, S, new Vector3(333f, F2, 20f), 1.6f, "EvidenceDoor");
        b.recordsDoor = Door(g, S, new Vector3(309f, F2, 16f), 1.6f, "RecordsDoor");
        // 비밀번호 단말(문 오른쪽 벽, 복도 쪽)
        P.Box(g, "Keypad", new Vector3(334.2f, F2 + 1.3f, 19.86f), new Vector3(0.16f, 0.22f, 0.04f), S["Steel"], Vector2.one, collider: false);
        P.Box(g, "KeypadGlow", new Vector3(334.2f, F2 + 1.36f, 19.83f), new Vector3(0.1f, 0.05f, 0.01f), S["Keypad"], Vector2.one, collider: false);

        // ---------- 조명(형광등) ----------
        (float x, float z, float yy, float k)[] lamps =
        {
            (306f, 2.5f, 0f, 1f), (318f, 2.5f, 0f, 1f), (330f, 2.5f, 0f, 0.6f),  // 1층 복도(동쪽 끝은 어둡게)
            (306f, 11f, 0f, 1f), (316f, 11f, 0f, 1f), (328f, 11f, 0f, 0.8f), (337f, 12f, 0f, 0.7f),
            (306f, 18f, F2, 0.9f), (322f, 18f, F2, 0.6f), (336f, 18f, F2, 0.8f),   // 2층 복도
            (308f, 25f, F2, 1f), (319f, 25f, F2, 0.9f),                            // 강력반
            (333f, 25f, F2, 0.7f),                                                  // 보관실
            (309f, 12.6f, F2, 0.7f), (326f, 10f, F2, 0.8f),                           // 기록실 · 휴게실
        };
        foreach (var (x, z, yy, k) in lamps)
        {
            // 천장판 밑면(층 높이 - 0.32)에 붙인다 — 예전엔 2층 바닥판 속에 묻혀 보이지 않았다
            P.Box(g, "FluorPanel", new Vector3(x, yy + WallH - 0.34f, z), new Vector3(1.2f, 0.04f, 0.3f), S["Fluor"], Vector2.one, collider: false);
            var l = P.AddLight(g, "Fluor", new Vector3(x, yy + WallH - 0.55f, z),LightType.Point, new Color(0.88f, 0.95f, 0.9f), 5.5f * k, 10f);
            l.shadows = LightShadows.None;
        }

        // ---------- 1층 방 ----------
        // 뒤 복도: 자판기(숨을 곳) · 게시판
        TryProp("VendingMachine", g, new Vector3(310f, 0f, 0.7f), 0f);
        TryProp("VendingMachine", g, new Vector3(319.5f, 0f, 0.7f), 0f);
        TryProp("CrateStack", g, new Vector3(327f, 0f, 0.9f), 10f);
        P.Box(g, "NoticeBoard", new Vector3(315f, 1.6f, 4.88f), new Vector3(2.4f, 1.1f, 0.05f), S["Board"], Vector2.one, collider: false);
        // 당직실: 책상 넷 · 컴퓨터 · 의자 · 캐비닛
        foreach (var (x, z) in new[] { (304f, 9f), (309f, 9f), (304f, 14f), (309f, 14f) }) OfficeDesk(g, new Vector3(x, 0f, z), 180f);
        foreach (float z in new[] { 6f, 7.4f, 8.8f }) TryProp("FilingCabinet", g, new Vector3(321.3f, 0f, z), -90f);
        OfficeDesk(g, new Vector3(318f, 0f, 16.6f), 180f); // 당직 책상
        // 사물함실: 사물함 줄 · 테이프(되감기 보충)
        foreach (float x in new[] { 324.5f, 327.5f, 330.5f }) TryProp("LockerRow", g, new Vector3(x, 0f, 17.3f), 180f);
        TapeSpot(g, triggers, "LockerTape", new Vector3(332.8f, 0f, 6.2f), 2, 1, box: true);

        // ---------- 2층 방 ----------
        // 강력반: 책상 여덟 · 화이트보드 · 레이의 옛 책상(짐 상자)
        for (int i = 0; i < 4; i++)
        {
            OfficeDesk(g, new Vector3(304f + i * 5.5f, F2, 23.5f), 0f);
            OfficeDesk(g, new Vector3(304f + i * 5.5f, F2, 27.5f), 180f);
        }
        P.Box(g, "Whiteboard", new Vector3(313f, F2 + 1.5f, 29.9f), new Vector3(4f, 1.3f, 0.05f), S["Board"], Vector2.one, collider: false);
        b.rayDesk = P.Marker(triggers, "RayDesk", new Vector3(320.5f, F2, 27.5f), 180f);
        TryProp("ParcelBox", g, new Vector3(320.95f, F2 + 0.76f, 27.45f), 15f); // 짐 상자(명패가 든) — 컴퓨터 옆 빈자리
        b.rayDeskUse = P.Interactable(g, "RayDeskUse", new Vector3(320.5f, F2 + 1f, 26.2f), new Vector3(1.6f, 2f, 1.2f), "내 옛 책상을 본다");
        // 증거물 보관실: 선반 줄 · 작업 책상(담당이 앉는 곳) · 대니 사건 상자
        foreach (float x in new[] { 328.5f, 331.5f, 334.5f })
            foreach (float z in new[] { 24f, 28f }) TryProp("EvidenceShelf", g, new Vector3(x, F2, z), 90f);
        TryProp("OfficeDesk", g, new Vector3(338.4f, F2, 26f), -90f);
        TryProp("OfficeChair", g, new Vector3(337.4f, F2, 26f), 90f);
        P.Box(g, "CaseTable", new Vector3(338.5f, F2 + 0.45f, 22f), new Vector3(1.2f, 0.9f, 0.8f), M["DarkWood"], Vector2.one);
        TryProp("OpenedParcel", g, new Vector3(338.5f, F2 + 0.9f, 22f), 30f);
        b.dannyBox = P.Interactable(g, "DannyCaseBox", new Vector3(337.6f, F2 + 1f, 22f), new Vector3(1.4f, 2f, 1.4f), "대니 사건 상자를 연다");
        b.evidenceInside = P.Marker(triggers, "EvidenceInside", new Vector3(333f, F2 + 0.05f, 21.6f), 0f);
        // 휴게실: 소파 · 자판기 · 탁자(증거물 담당이 있다)
        TryProp("Sofa", g, new Vector3(322f, F2, 7.4f), 0f);
        TryProp("VendingMachine", g, new Vector3(333.2f, F2, 7.4f), -90f);
        // 기록실: 캐비닛 줄 · 조회 단말
        for (int i = 0; i < 4; i++) TryProp("FilingCabinet", g, new Vector3(305.3f, F2, 10.3f + i * 1.35f), 90f);
        for (int i = 0; i < 4; i++) TryProp("FilingCabinet", g, new Vector3(312.7f, F2, 10.3f + i * 1.35f), -90f);
        TryProp("OfficeDesk", g, new Vector3(309f, F2, 10.2f), 0f);
        TryProp("DeskComputer", g, new Vector3(309f, F2 + 0.76f, 10.1f), 0f);
        TryProp("OfficeChair", g, new Vector3(309f, F2, 11.6f), 180f);
        b.terminalUse = P.Interactable(g, "RecordsTerminal", new Vector3(309f, F2 + 1f, 11.8f), new Vector3(1.6f, 2f, 1.4f), "차량 임대 기록을 찾는다");
        b.recordsInside = P.Marker(triggers, "RecordsInside", new Vector3(309f, F2 + 0.05f, 14.5f), 180f);
        b.recordsDoorUse = P.Interactable(g, "RecordsDoorUse", new Vector3(309f, F2 + 1f, 17.2f), new Vector3(1.8f, 2f, 1.2f), "기록실 문을 연다");
        b.keypadUse = P.Interactable(g, "KeypadUse", new Vector3(333f, F2 + 1f, 18.9f), new Vector3(2f, 2f, 1.4f), "비밀번호를 누른다");
        b.keypadUse.oneShot = false; // 번호를 모른 채 열었다 닫아도 다시 누를 수 있게
        // 1층 뒷문: 닫힌 문짝(전에는 벽에 구멍만 뚫려 밖이 허공 — 낭떠러지처럼 보였다). F로 나가면 바깥 뒷문 앞으로 옮겨진다
        P.Box(g, "BackDoorLeaf", new Vector3(IX0 + 0.02f, 1.15f, 2.5f), new Vector3(0.06f, 2.28f, 1.36f), S["Door"], Vector2.one);
        P.Box(g, "BackDoorHandle", new Vector3(IX0 + 0.09f, 1.05f, 2.0f), new Vector3(0.05f, 0.04f, 0.16f), S["Steel"], Vector2.one, collider: false);

        // ---------- 자리 · 트리거 ----------
        b.entry = P.Marker(triggers, "StationEntry", new Vector3(IX0 + 1.4f, 0.05f, 2.5f), 90f);
        b.stairsTop = P.Marker(triggers, "StairsTop", new Vector3(337f, F2 + 0.05f, 17.5f), -90f);
        b.floor2Trigger = Trigger(triggers, "Floor2Trigger", new Vector3(337f, F2 + 1f, 18f), new Vector3(6f, 2f, 4f));
        b.clerkLounge = P.Marker(triggers, "ClerkLounge", new Vector3(326f, F2 + 0.05f, 12.5f), 0f);
        b.clerkKeypad = P.Marker(triggers, "ClerkKeypad", new Vector3(333f, F2 + 0.05f, 18.7f), 0f);
        b.clerkInside = P.Marker(triggers, "ClerkInside", new Vector3(337.4f, F2 + 0.05f, 26f), 90f);
        b.records = b.recordsDoorUse.transform;
        b.evidence = b.keypadUse.transform;

        // ---------- 사람 ----------
        // 1층: 복도 순찰 · 당직(방 안을 오간다)
        IndoorCop(people, routes, "Cop_Hall1", new Vector3(306f, 0.02f, 2.6f), 90f, new Vector3(306f, 0.02f, 2.6f), new Vector3(330f, 0.02f, 2.6f));
        IndoorCop(people, routes, "Cop_Duty", new Vector3(306f, 0.02f, 11.5f), 90f, new Vector3(306f, 0.02f, 11.5f), new Vector3(318f, 0.02f, 11.5f));
        // 2층: 복도 서쪽 순찰 · 강력반 형사 둘(하나는 오가고, 하나는 책상에서 복도 쪽을 본다)
        IndoorCop(people, routes, "Cop_Hall2", new Vector3(302f, F2 + 0.02f, 18f), 90f, new Vector3(302f, F2 + 0.02f, 18f), new Vector3(321f, F2 + 0.02f, 18f));
        IndoorCop(people, routes, "Det_Pacing", new Vector3(303f, F2 + 0.02f, 25.5f), 90f, new Vector3(303f, F2 + 0.02f, 25.5f), new Vector3(323f, F2 + 0.02f, 25.5f));
        IndoorCop(people, routes, "Det_Desk", new Vector3(314f, F2 + 0.02f, 22.2f), 180f, new Vector3(314f, F2 + 0.02f, 22.2f));
        // 증거물 담당(정해진 대로 움직인다)
        var clerkAi = P.CreateThug(people, "Clerk_Evidence", b.clerkLounge.position, 0f, "Officer");
        clerkAi.displayName = "증거물 담당";
        clerkAi.damage = 0;
        var clerk = clerkAi.gameObject.AddComponent<EvidenceClerk>();
        clerk.lounge = b.clerkLounge;
        clerk.keypadSpot = b.clerkKeypad;
        clerk.insideSpot = b.clerkInside;
        b.clerk = clerk;
    }

    /// <summary>2층 바닥판(윗면 F2) + 아래층 천장(아랫면)</summary>
    static void Slab(Transform g, Material floor, Material ceiling, float x0, float x1, float z0, float z1)
    {
        P.Box(g, "Floor2", new Vector3((x0 + x1) / 2f, F2 - 0.15f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.3f, z1 - z0), floor, new Vector2(3.2f, 3.2f));
        // 아래층에서 올려다보면 바닥 타일이 보였다: 밑면에 천장판을 덧댄다
        P.Box(g, "Ceiling1", new Vector3((x0 + x1) / 2f, F2 - 0.31f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.02f, z1 - z0), ceiling, new Vector2(2f, 2f), collider: false);
    }

    /// <summary>동서로 놓인 벽(z 고정). holes = (가운데 x, 폭) 문 구멍(높이 2.3m)</summary>
    static void WallX(Transform g, Dictionary<string, Material> S, float z, float x0, float x1, float y, params (float c, float w)[] holes)
    {
        var cuts = new List<(float a, float b)>();
        foreach (var (c, w) in holes) cuts.Add((c - w / 2f, c + w / 2f));
        cuts.Sort((p, q) => p.a.CompareTo(q.a));
        float cur = x0;
        foreach (var (a, bb) in cuts) { WallPiece(g, S, new Vector3((cur + a) / 2f, y, z), new Vector3(a - cur, 0f, 0.2f)); Lintel(g, S, new Vector3((a + bb) / 2f, y, z), new Vector3(bb - a, 0f, 0.2f)); cur = bb; }
        WallPiece(g, S, new Vector3((cur + x1) / 2f, y, z), new Vector3(x1 - cur, 0f, 0.2f));
    }

    /// <summary>남북으로 놓인 벽(x 고정). holes = (가운데 z, 폭)</summary>
    static void WallZ(Transform g, Dictionary<string, Material> S, float x, float z0, float z1, float y, params (float c, float w)[] holes)
    {
        var cuts = new List<(float a, float b)>();
        foreach (var (c, w) in holes) cuts.Add((c - w / 2f, c + w / 2f));
        cuts.Sort((p, q) => p.a.CompareTo(q.a));
        float cur = z0;
        foreach (var (a, bb) in cuts) { WallPiece(g, S, new Vector3(x, y, (cur + a) / 2f), new Vector3(0.2f, 0f, a - cur)); Lintel(g, S, new Vector3(x, y, (a + bb) / 2f), new Vector3(0.2f, 0f, bb - a)); cur = bb; }
        WallPiece(g, S, new Vector3(x, y, (cur + z1) / 2f), new Vector3(0.2f, 0f, z1 - cur));
    }

    static void WallPiece(Transform g, Dictionary<string, Material> S, Vector3 foot, Vector3 size)
    {
        if (size.x < 0.05f || size.z < 0.05f) return;
        // 허리(1.1m) 아래 짙은 칠 + 위 밝은 칠
        P.Box(g, "WallLow", foot + Vector3.up * 0.55f, new Vector3(size.x, 1.1f, size.z), S["WallLow"], new Vector2(2f, 2f));
        P.Box(g, "Wall", foot + Vector3.up * (1.1f + (WallH - 1.1f) / 2f), new Vector3(size.x, WallH - 1.1f, size.z), S["Wall"], new Vector2(3f, 3f));
    }

    static void Lintel(Transform g, Dictionary<string, Material> S, Vector3 foot, Vector3 size)
    {
        const float doorH = 2.3f;
        P.Box(g, "Lintel", foot + Vector3.up * (doorH + (WallH - doorH) / 2f), new Vector3(size.x, WallH - doorH, size.z), S["Wall"], new Vector2(3f, 3f));
    }

    /// <summary>계단: 가로 폭 width, 길이 length(+z로 오른다). 보이는 단 + 걷는 경사판(보이지 않는 충돌체)</summary>
    static void Stairs(Transform g, Dictionary<string, Material> S, Vector3 foot, float width, float length, float rise)
    {
        int steps = Mathf.RoundToInt(rise / 0.175f);
        float run = length / steps, h = rise / steps;
        // 단마다 바닥까지 채운 덩어리(보이는 계단). 걷는 것은 아래 경사판
        for (int i = 0; i < steps; i++)
        {
            float top = h * (i + 1);
            P.Box(g, "Step", foot + new Vector3(0f, top / 2f, run * (i + 0.5f)), new Vector3(width, top, run), S["Floor"], Vector2.one, collider: false);
        }
        var ramp = new GameObject("StairRamp");
        ramp.transform.SetParent(g);
        float slopeLen = Mathf.Sqrt(length * length + rise * rise);
        ramp.transform.position = foot + new Vector3(0f, rise / 2f, length / 2f);
        ramp.transform.rotation = Quaternion.Euler(-Mathf.Atan2(rise, length) * Mathf.Rad2Deg, 0f, 0f);
        var col = ramp.AddComponent<BoxCollider>();
        col.size = new Vector3(width, 0.1f, slopeLen);
        GameObjectUtility.SetStaticEditorFlags(ramp, StaticEditorFlags.NavigationStatic);
        // 위 끝과 2층 바닥 사이 틈을 막는 판
        P.Box(g, "StairTopPlate", foot + new Vector3(0f, rise - 0.05f, length + 0.25f), new Vector3(width, 0.1f, 0.5f), S["Floor"], Vector2.one);
    }

    /// <summary>여닫이 문짝(경첩은 서쪽 끝). 감독이 돌려 연다. 닫혀 있으면 길을 막는다(내비메시 구멍)</summary>
    static Transform Door(Transform g, Dictionary<string, Material> S, Vector3 centreFoot, float width, string name)
    {
        var hinge = new GameObject(name).transform;
        hinge.SetParent(g);
        hinge.position = centreFoot + Vector3.left * (width / 2f);
        var leaf = P.Box(hinge, "Leaf", centreFoot + Vector3.up * 1.15f, new Vector3(width - 0.05f, 2.28f, 0.06f), S["Door"], Vector2.one);
        GameObjectUtility.SetStaticEditorFlags(leaf, 0);
        var handle = P.Box(hinge, "Handle", centreFoot + new Vector3(width / 2f - 0.15f, 1.05f, -0.06f), new Vector3(0.12f, 0.03f, 0.03f), S["Steel"], Vector2.one, collider: false);
        GameObjectUtility.SetStaticEditorFlags(handle, 0);
        var obstacle = leaf.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.size = new Vector3(width - 0.05f, 2.28f, 0.3f);
        return hinge;
    }

    /// <summary>책상 + 컴퓨터 + 의자 한 벌(책상 앞면이 yaw 쪽)</summary>
    static void OfficeDesk(Transform g, Vector3 at, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        TryProp("OfficeDesk", g, at, yaw); // 현대식 철제 책상(옛 나무 책상 "Desk"는 주인공 집·전파사 전용)
        TryProp("DeskComputer", g, at + rot * new Vector3(0.2f, 0.76f, -0.1f), yaw); // 화면이 의자(앞) 쪽
        TryProp("OfficeChair", g, at + rot * new Vector3(0f, 0f, 0.9f), yaw + 180f);
    }

    /// <summary>실내 경찰(손전등 없음): 순찰·제자리 감시. 싸우지 않는다 — 들키면 시간이 멈춘다</summary>
    static PatrolSight IndoorCop(Transform people, Transform routes, string name, Vector3 pos, float yaw, params Vector3[] route)
    {
        var ai = P.CreateThug(people, name, pos, yaw, "Officer");
        SetCombat(ai, false);
        ai.displayName = "경찰";
        ai.damage = 0;
        var sight = ai.gameObject.AddComponent<PatrolSight>();
        sight.viewDistance = 10f;
        var folder = new GameObject(name + "_Route").transform;
        folder.SetParent(routes);
        var points = new Transform[route.Length];
        for (int i = 0; i < route.Length; i++) points[i] = P.Marker(folder, $"P{i}", route[i], 0f);
        sight.route = points;
        return sight;
    }
}
