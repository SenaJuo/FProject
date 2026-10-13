using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using P = PrologueBuilder;

/// <summary>
/// 1장 「B면」 장면(지금은 구역 1)을 처음부터 만든다. 메뉴: Game/Build Chapter 1  (스크립트에서는 Chapter1Builder.Build())
///
/// 배치(미터, 프롤로그 거리 좌표를 그대로 쓴다):
///   대니네 거리: 프롤로그와 같다(x -20~150, 차도 z ±4, 북쪽 집 앞면 z 12, 뒷마당 담장 z 26). 대니 집 x 103, 뒷담장 쪽문 x 106.2.
///   근린공원: x 55~150, z 26~84. 쪽문 앞은 높은 생울타리로 둘러싼 입구 정원(z 26~40), 그 북쪽에 공중화장실(x 109~115, z 36.5~39.7).
///             정원에서 공원으로 나가는 통로(x 99~109)가 하나뿐이라, 여기서 화장실 뒤 조직원에게 걸린다(실전 첫 되감기).
///             통로 → 북쪽 산책길(x 106.2) → 관리 도로(z 58~62, 검은 차와 통화하는 조직원) → 동쪽 출구(x 150, 출구를 지키는 세 명).
///   큰길: 서쪽 인도 x 155~158, 차도 158~170, 동쪽 인도 170~173, 그 동쪽은 상가(가게 1 「심야 레코드」 z 59.5~68.5).
///         남쪽은 공사 철망(z -8)으로 막고, 북쪽 z 112에서 (이어서 계속).
/// 사람·트리거·경로점은 내비메시를 굽는 루트 밖에 둔다(트리거가 바닥으로 구워지지 않게).
/// </summary>
public static partial class Chapter1Builder
{
    const string ScenePath = "Assets/Game/Scenes/Chapter1.unity";
    const string NavMeshPath = "Assets/Game/Scenes/Chapter1_NavMesh.asset";
    const string VolumePath = P.ArtRoot + "/Chapter1Volume.asset";
    const float G = P.Ground;

    const float ParkX0 = P.ParkWestX, ParkX1 = 150f, ParkZ0 = P.BackFence, ParkZ1 = 84f;
    const float GateX = P.ParkGateX;
    const float PathX = 106.2f;          // 쪽문에서 북쪽으로 곧게 뻗은 산책길
    const float LaneZ = 60f;             // 공원 관리 도로 가운데(동서)
    const float ExitGap0 = 56f, ExitGap1 = 64f; // 동쪽 출구(철망이 끊긴 곳)
    const float AveW = 155f, AveRoad0 = 158f, AveRoad1 = 170f, AveE = 173f;
    const float AveX = (AveRoad0 + AveRoad1) / 2f;
    const float ShopZ0 = 59.5f, ShopZ1 = 68.5f, ShopDoorZ = 66.4f;

    [MenuItem("Game/Build Chapter 1")]
    static void BuildMenu()
    {
        if (EditorUtility.DisplayDialog("1장 만들기", $"{ScenePath}를 새로 만듭니다(기존 내용은 덮어씀).", "만들기", "취소")) Build();
    }

    // 이 장면에서 만든 것들(감독 연결용)
    class Built
    {
        public Transform zoneStart, parkExit, northEnd, shopSave;
        public StoryTrigger spotTrigger, callTrigger, plateTrigger, shopApproach, endTrigger;
        public Transform spotterStep;
        public PatrolSight spotter, caller;
        public TapePickup vanTape;
        public CombatZone exitZone;
        public EnemyAI exitGunner;
        public TapeShop shop;
        // 구역 2(시장 광장)
        public Transform zone2Start, checkpointCam, checkpointLook, gate, gateOpen, stairsFoot, roofTop, harborEnd;
        public Transform[] vanPath;
        public GameObject van;
        public CombatZone checkpointZone, roofZone;
        public StoryTrigger checkpointView, squareTrigger, harborTrigger;
        public EnemyAI sniper;
        public Bounds square;
        // 가게 2 · 구역 3(항구)
        public TapeShop shop2;
        public StoryTrigger shop2Approach, pierTrigger;
        public Transform shop2Save, zone3Start, pier, gateLeaf, ship, craneTop;
        public Vector3 gateOpenPos;
        public StoryInteractable gateUse;
        public Searchlight searchlight;
        public CombatZone dockZone, pierZone;
        public EnemyAI boss;
        public EnemyAI[] pierThugs;
        public Transform burnBarrel, dawnSeat;
        public FireFlicker barrelFire;
    }

    public static void Build()
    {
        P.boxMeshes.Clear();
        beamCone = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        P.root = new GameObject("Chapter1").transform;
        var M = P.CreateMaterials();
        var X = ExtraMaterials();
        var b = new Built();

        P.BuildStreet(M, chapterOne: true);
        P.BuildAlleyAndEnds(M);
        var triggers = new GameObject("Triggers").transform;     // 내비메시 밖
        var people = new GameObject("Thugs").transform;
        var routes = new GameObject("PatrolRoutes").transform;

        BuildPark(M, X, triggers, b);
        BuildAvenue(M, X, triggers, b);
        BuildShop(M, X, triggers, b);

        // ---------- 플레이어: 프롤로그가 끝난 자리(대니 집 뒷마당, 뒷창 아래) ----------
        b.zoneStart = P.Marker(triggers, "Zone1Start", new Vector3(PrologueBuilder.DannyHouseX - 3.7f, G, P.HouseLine + 9f + 1.6f), 90f);
        var player = P.CreatePlayer(b.zoneStart, out _, out _, out var pocketWalkman, out _);

        BuildThugs(people, routes, triggers, b);
        BuildMarketSquare(M, X, triggers, people, routes, b);
        BuildHarbor(M, X, triggers, people, routes, b);

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

        var d = systems.AddComponent<Chapter1Director>();
        d.player = player;
        d.pocketWalkman = pocketWalkman;
        d.brain = brain;
        d.gameplayCamera = gameplay;
        d.gameplayRig = rig;
        d.dynamicA = dynA;
        // 비 없음(2026-10-13 사용자 요청): 빗소리를 연결하지 않는다
        d.zoneStart = b.zoneStart;
        d.parkExit = b.parkExit;
        d.spotTrigger = b.spotTrigger;
        d.spotter = b.spotter;
        d.spotterStep = b.spotterStep;
        d.callTrigger = b.callTrigger;
        d.caller = b.caller;
        d.plateTrigger = b.plateTrigger;
        d.vanTape = b.vanTape;
        d.exitZone = b.exitZone;
        d.exitGunner = b.exitGunner;
        d.shop = b.shop;
        d.shopApproach = b.shopApproach;
        d.shopSave = b.shopSave;
        d.northEnd = b.northEnd;
        d.endTrigger = b.endTrigger;
        d.zone2Start = b.zone2Start;
        d.checkpointView = b.checkpointView;
        d.checkpointCam = b.checkpointCam;
        d.checkpointLook = b.checkpointLook;
        d.van = b.van;
        d.vanPath = b.vanPath;
        d.gate = b.gate;
        d.gateOpen = b.gateOpen;
        d.checkpointZone = b.checkpointZone;
        d.squareTrigger = b.squareTrigger;
        d.square = b.square;
        d.sniper = b.sniper;
        d.stairsFoot = b.stairsFoot;
        d.roofTop = b.roofTop;
        d.roofZone = b.roofZone;
        d.harborTrigger = b.harborTrigger;
        d.harborEnd = b.harborEnd;
        d.shop2 = b.shop2;
        d.shop2Approach = b.shop2Approach;
        d.shop2Save = b.shop2Save;
        d.gateLeaf = b.gateLeaf;
        d.gateOpenPos = b.gateOpenPos;
        d.gateUse = b.gateUse;
        d.zone3Start = b.zone3Start;
        d.pier = b.pier;
        d.pierTrigger = b.pierTrigger;
        d.searchlight = b.searchlight;
        d.dockZone = b.dockZone;
        d.pierZone = b.pierZone;
        d.boss = b.boss;
        d.bossBrain = b.boss ? b.boss.GetComponent<BossCleaner>() : null;
        d.pierThugs = b.pierThugs;
        d.burnBarrel = b.burnBarrel;
        d.dawnSeat = b.dawnSeat;
        d.barrelFire = b.barrelFire;
        d.dynamicB = P.VCam(cams, "Shot_Dynamic_B", Vector3.zero, Vector3.forward, 40f);

        P.SetupEnvironment(VolumePath);
        var skyCycle = SetupHarborSky(mainCam); // 그라데이션 하늘·수평선 실루엣(장면 전체), 새벽 연출용
        skyCycle.transform.SetParent(systems.transform);
        d.skyCycle = skyCycle;
        // 1장은 숨어 다니는 구간이 넓다: 프롤로그보다 주변광을 조금 올려 어둠 속 길과 생울타리 윤곽이 보이게 한다
        RenderSettings.ambientLight = new Color(0.4f, 0.41f, 0.48f); // 너무 어둡다(0.135 → 0.2 → 0.3 → 0.4, BrightnessPass)
        P.BakeNavMesh(NavMeshPath);
        P.EnforceMaterialKeywords();

        EditorSceneManager.SaveScene(scene, ScenePath);
        // 빌드 순서: 프롤로그(0) → 1장(1)
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        int prologue = list.FindIndex(s => s.path.EndsWith("/Prologue.unity"));
        list.Insert(prologue >= 0 ? prologue + 1 : list.Count, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[1장] {ScenePath} 생성 완료");
    }

    // =====================================================================
    // 재질(이 장면에서만 쓰는 것)
    // =====================================================================

    static Dictionary<string, Material> ExtraMaterials()
    {
        var X = new Dictionary<string, Material>();
        X["ShopWall"] = P.Lit("ShopWall", null, new Color(0.42f, 0.30f, 0.22f), 0.2f);
        X["Awning"] = P.Lit("Awning", null, new Color(0.30f, 0.07f, 0.06f), 0.35f);
        X["SignBoard"] = P.Lit("SignBoard", null, new Color(0.06f, 0.06f, 0.07f), 0.4f);
        X["Shutter"] = P.Lit("Shutter", P.Tex("Shutter", 256, ShutterPixel), new Color(0.62f, 0.63f, 0.64f), 0.35f, metallic: 0.55f);
        X["Roof"] = P.Lit("PavilionRoof", null, new Color(0.17f, 0.15f, 0.14f), 0.25f);
        X["FlashLens"] = P.Unlit("FlashLens", new Color(1f, 0.93f, 0.78f) * 3f);
        X["BollardGlow"] = P.Unlit("BollardGlow", new Color(1f, 0.85f, 0.62f) * 2.2f);
        X["Frosted"] = P.Lit("FrostedGlass", null, new Color(0.22f, 0.25f, 0.25f), 0.6f, emission: new Color(0.62f, 0.66f, 0.55f) * 0.07f); // 안쪽 형광등이 희미하게 비치는 정도(세면 하얀 판처럼 뜬다)
        X["WcTile"] = P.Lit("WcTile", P.Tex("WcTile", 256, TilePixel), new Color(0.86f, 0.87f, 0.86f), 0.5f);     // 화장실 통로 흰 타일(광택이 세면 형광등에 하얗게 탄다)
        X["WcPlaster"] = P.Lit("WcPlaster", P.Tex("Concrete", 256, P.ConcretePixel), new Color(0.78f, 0.76f, 0.70f), 0.15f); // 화장실 바깥벽: 밝은 회백색(밤에 검은 상자처럼 보이지 않게)
        X["WcTileDark"] = P.Lit("WcTileDark", P.Tex("WcTile", 256, TilePixel), new Color(0.40f, 0.43f, 0.47f), 0.6f); // 바깥 벽 아랫단
        X["Fluor"] = P.Unlit("FluorTube", new Color(0.9f, 0.97f, 1f) * 1.6f);
        X["HallDark"] = P.Lit("HallDark", null, new Color(0.02f, 0.02f, 0.025f), 0.1f);
        X["PictoMan"] = Pictogram("PictoMan", false, new Color(0.12f, 0.36f, 0.86f));
        X["PictoWoman"] = Pictogram("PictoWoman", true, new Color(0.86f, 0.16f, 0.22f));
        X["WindowGlow"] = P.Unlit("ShopWindowGlow", new Color(1f, 0.72f, 0.42f) * 1.6f);
        X["Glass"] = Glass();
        // 간판: 필기체 "Midnight"(분홍빛 주황 네온) + 각진 대문자 "RECORDS"(하늘색 네온). 한글 간판은 어색하다는 의견으로 영어 네온으로
        X["SignMidnight"] = Neon("ShopSignMidnight", "Midnight", new Color(1f, 0.36f, 0.42f) * 4.4f, new[] { "Segoe Script" }, FontStyle.Bold);
        X["SignRecords"] = Neon("ShopSignRecords", "RECORDS", new Color(0.35f, 0.88f, 1f) * 3.8f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        X["SignOpen"] = Neon("ShopSignOpen", "OPEN 24 HRS", new Color(0.45f, 0.85f, 1f) * 3.2f, new[] { "Bahnschrift", "Arial" }, FontStyle.Bold);
        return X;
    }

    /// <summary>흰 타일(한 장 64px, 회색 줄눈 3px)</summary>
    static Color TilePixel(int x, int y, int size)
    {
        int t = size / 4;
        bool grout = (x % t) < 3 || (y % t) < 3;
        float n = 0.96f + 0.04f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
        float v = grout ? 0.6f : 0.93f * n;
        return new Color(v, v, v, 1f);
    }

    /// <summary>
    /// 화장실 남녀 그림 표시판: 흰 바탕(회색 테두리)에 사람 모양(남자 = 몸통과 바지, 여자 = 치마). 글자는 없다.
    /// 가장자리는 4×4 표본으로 부드럽게 그린다. 밤에도 읽히게 약하게 스스로 빛난다
    /// </summary>
    static Material Pictogram(string name, bool female, Color figure)
    {
        const int S = 256;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cover = 0f;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (Figure(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f, female)) cover += 1f / 16f;
                bool border = x < 10 || y < 10 || x >= S - 10 || y >= S - 10;
                Color bg = border ? new Color(0.55f, 0.57f, 0.6f) : new Color(0.96f, 0.96f, 0.95f);
                px[y * S + x] = Color.Lerp(bg, figure, cover);
            }
        var tex = P.SaveTexture(name, S, S, px, true);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex));
        return P.Lit(name, tex, Color.white, 0.3f, emission: Color.white * 0.32f, emissionMapFromBase: true);
    }

    /// <summary>표시판 사람 모양(256칸, 아래가 y = 0): 머리 원, 양팔, 몸통·두 다리(남자) 또는 치마·두 다리(여자)</summary>
    static bool Figure(float x, float y, bool female)
    {
        if ((x - 128f) * (x - 128f) + (y - 198f) * (y - 198f) < 26f * 26f) return true;          // 머리
        if (Capsule(x, y, 90f, 160f, 82f, 104f, 9f) || Capsule(x, y, 166f, 160f, 174f, 104f, 9f)) return true; // 팔
        if (!female)
        {
            if (RoundRect(x, y, 100f, 98f, 156f, 166f, 10f)) return true;                              // 몸통
            return RoundRect(x, y, 103f, 28f, 124f, 104f, 6f) || RoundRect(x, y, 132f, 28f, 153f, 104f, 6f); // 다리
        }
        if (y >= 82f && y <= 166f)
        {
            float half = Mathf.Lerp(26f, 48f, (166f - y) / 84f);                                        // 치마: 위가 좁다
            if (Mathf.Abs(x - 128f) <= half) return true;
        }
        return RoundRect(x, y, 108f, 28f, 124f, 88f, 6f) || RoundRect(x, y, 132f, 28f, 148f, 88f, 6f);
    }

    static bool Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
        float dx = x - (ax + vx * t), dy = y - (ay + vy * t);
        return dx * dx + dy * dy <= r * r;
    }

    static bool RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;
        float cx = Mathf.Clamp(x, x0 + r, x1 - r), cy = Mathf.Clamp(y, y0 + r, y1 - r);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    }

    static Color ShutterPixel(int x, int y, int size)
    {
        // 가게 철제 셔터: 가로 골(16px마다 어두운 홈)과 약한 얼룩
        float groove = (y % 16) < 2 ? 0.55f : (y % 16) < 4 ? 0.8f : 1f;
        float n = 0.92f + 0.08f * Mathf.PerlinNoise(x * 0.05f, y * 0.02f);
        float v = groove * n;
        return new Color(v, v, v, 1f);
    }

    /// <summary>가게 진열창 유리: 비쳐 보이는 투명 재질</summary>
    static Material Glass()
    {
        string path = $"{P.ArtRoot}/Materials/ShopGlass.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetFloat("_Surface", 1f);   // 투명
        mat.SetFloat("_Blend", 0f);     // 알파 섞기
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetColor("_BaseColor", new Color(0.55f, 0.62f, 0.66f, 0.16f));
        mat.SetFloat("_Smoothness", 0.95f);
        mat.SetFloat("_Metallic", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 네온 간판: 윈도우 한글 글꼴(맑은 고딕)로 글자를 그려 구운 그림을 발광 지도로 쓴다.
    /// 장면에 글꼴 파일이 없어도 되고, 블룸으로 네온처럼 번진다.
    /// </summary>
    static Material Neon(string name, string text, Color glow, string[] fonts = null, FontStyle style = FontStyle.Bold)
    {
        var tex = BakeText(name, text, 1024, 256, fonts, style);
        return P.Lit(name, tex, new Color(0.05f, 0.05f, 0.05f), 0.4f, emission: glow, emissionMapFromBase: true);
    }

    static Texture2D BakeText(string name, string text, int w, int h, string[] fonts = null, FontStyle style = FontStyle.Bold)
    {
        const int layer = 31;
        var font = Font.CreateDynamicFontFromOSFont(fonts ?? new[] { "Malgun Gothic", "맑은 고딕" }, 160);
        font.RequestCharactersInTexture(text, 160, style);
        var go = new GameObject("TextBake") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
        go.transform.position = new Vector3(0f, -900f, 0f);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 160;
        tm.fontStyle = style;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        tm.text = text;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = font.material;
        var size = mr.bounds.size;

        var camGo = new GameObject("TextBakeCam") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << layer;
            cam.transform.SetPositionAndRotation(go.transform.position + Vector3.back * 10f, Quaternion.identity);
            float aspect = (float)w / h;
            // 글자 둘레에 여백(가로 8%, 세로 20%)을 둔다
            cam.orthographicSize = Mathf.Max(size.y * 0.6f, size.x / aspect * 0.54f);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 20f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels32();
            Object.DestroyImmediate(read);
            // 흰 글자 → 밝기를 알파로도 넣는다
            for (int i = 0; i < px.Length; i++) { byte v = (byte)Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b)); px[i] = new Color32(v, v, v, 255); }
            var tex = P.SaveTexture(name, w, h, px, true);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex));
        }
        finally
        {
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(go);
        }
    }

    // =====================================================================
    // 근린공원
    // =====================================================================

    static void BuildPark(Dictionary<string, Material> M, Dictionary<string, Material> X, Transform triggers, Built b)
    {
        var g = P.Group("Park");
        // 땅(잔디)과 길
        P.Box(g, "ParkLawn", new Vector3((ParkX0 + AveW) / 2f, 0.075f, (ParkZ0 + ParkZ1) / 2f), new Vector3(AveW - ParkX0, 0.15f, ParkZ1 - ParkZ0), M["Grass"], new Vector2(3f, 3f));
        P.Box(g, "Path_North", new Vector3(PathX, G + 0.01f, (ParkZ0 + LaneZ - 2f) / 2f), new Vector3(2.4f, 0.02f, LaneZ - 2f - ParkZ0), M["Sidewalk"], new Vector2(2f, 2f), collider: false);
        P.Box(g, "ServiceLane", new Vector3((92f + ParkX1) / 2f, G + 0.012f, LaneZ), new Vector3(ParkX1 - 92f, 0.02f, 4f), M["Asphalt"], new Vector2(4f, 4f), collider: false);
        P.Box(g, "Path_Exit", new Vector3((ParkX1 + AveW) / 2f, G + 0.01f, LaneZ), new Vector3(AveW - ParkX1, 0.02f, 4f), M["Sidewalk"], new Vector2(2f, 2f), collider: false);
        P.Box(g, "Path_Lawn", new Vector3(122f, G + 0.01f, 46f), new Vector3(30f, 0.02f, 2.2f), M["Sidewalk"], new Vector2(2f, 2f), collider: false);

        // ---------- 쪽문(대니 집 뒷담장) ----------
        foreach (float dx in new[] { -1.2f, 1.2f })
            P.Box(g, "GatePost", new Vector3(GateX + dx, G + 1.05f, ParkZ0), new Vector3(0.14f, 2.1f, 0.14f), M["DarkWood"], Vector2.one);
        var leaf = P.Box(g, "GateLeaf", Vector3.zero, new Vector3(2.2f, 1.8f, 0.06f), M["WoodFence"], new Vector2(1.2f, 1.8f), collider: false);
        leaf.transform.SetPositionAndRotation(new Vector3(GateX + 1.2f, G + 0.95f, ParkZ0 + 1.1f), Quaternion.Euler(0f, 90f, 0f)); // 공원 쪽으로 활짝 열려 있다

        // ---------- 입구 정원: 높은 생울타리 벽(통로는 화장실 서쪽 하나) ----------
        // 담장(z 26)에 맞닿게 시작한다: 틈이 0.6m보다 좁아야 레이가 빠져나가지 못한다
        for (float z = 27.2f; z < 38.5f; z += 2.2f) TallHedge(g, new Vector3(98.5f, G, z), 90f);   // 서쪽 벽(z 26.1~39.3)
        for (float z = 27.2f; z < 38.5f; z += 2.2f) TallHedge(g, new Vector3(118.5f, G, z), 90f);  // 동쪽 벽(z 26.1~39.3)
        TallHedge(g, new Vector3(116.1f, G, 38.1f), 0f);  // 화장실 동쪽 벽 ~ 동쪽 생울타리 사이를 막는다
        TallHedge(g, new Vector3(117.6f, G, 38.6f), 0f);
        // 숨을 곳(정원 안): 되감은 뒤 화장실 모퉁이 조직원을 피해 숨는다
        TallHedge(g, new Vector3(103.2f, G, 30.2f), 0f);
        TallHedge(g, new Vector3(111.0f, G, 31.4f), 0f);
        TallHedge(g, new Vector3(101.1f, G, 34.0f), 90f);
        TallHedge(g, new Vector3(116.2f, G, 33.4f), 0f); // 여자 입구 앞을 막지 않게 동쪽으로

        // ---------- 공중화장실(x 109~115, z 36.5~39.7): 남녀 입구 둘, 입구 위 남녀 그림 표시. 자판기는 큰길 버스 정류장 옆으로 옮겼다 ----------
        Restroom(g, M, X);

        // ---------- 산책길 옆 숨을 곳과 벤치 ----------
        TallHedge(g, new Vector3(108.6f, G, 45.6f), 90f);
        TallHedge(g, new Vector3(108.6f, G, 51.4f), 90f);
        TallHedge(g, new Vector3(103.8f, G, 54.6f), 0f);
        Bench(g, new Vector3(104.4f, G, 48.6f), 90f, M);
        TallHedge(g, new Vector3(121.5f, G, 63.4f), 0f);   // 관리 도로 북쪽: 도로 순찰을 피해 숨는 곳
        TallHedge(g, new Vector3(129.5f, G, 63.4f), 0f);
        TallHedge(g, new Vector3(125.5f, G, 56.6f), 0f);   // 관리 도로 남쪽(잔디밭 순찰 쪽도 가린다)
        Bench(g, new Vector3(117.5f, G, 44.6f), 0f, M);
        Bench(g, new Vector3(126.5f, G, 44.6f), 0f, M);

        // 공원 쓰레기통(벤치 곁, 길 갈림목)
        foreach (var (bx, bz, by) in new[] { (105.5f, 50.3f, 0f), (119.1f, 43.9f, 30f), (85.8f, 57.0f, 0f), (128.4f, 43.9f, -20f), (139.6f, 62.6f, 90f) })
            TryProp("ParkBin", g, new Vector3(bx, G, bz), by);

        // ---------- 정자(서쪽) ----------
        Pavilion(g, new Vector3(84f, G, 60f), M, X);
        Bench(g, new Vector3(84f, G, 58.6f), 0f, M);

        // ---------- 정원·쪽문·출구 조명(어두워서 길과 숨을 곳이 안 보이던 곳) ----------
        ParkLamp(g, new Vector3(110.4f, G, 27.4f), 180f, M);            // 쪽문 옆: 정원 길을 비춘다
        Bollard(g, new Vector3(104.8f, G, 29.6f), X);
        Bollard(g, new Vector3(107.6f, G, 33.2f), X);
        Bollard(g, new Vector3(100.4f, G, 37.6f), X);
        ParkLamp(g, new Vector3(147.6f, G, 53.8f), 90f, M);             // 공원 출구
        ParkLamp(g, new Vector3(137.5f, G, 57.4f), 90f, M);

        // ---------- 가로등 ----------
        foreach (var (x, z, yaw) in new[] { (104.6f, 42f, 0f), (104.6f, 56.5f, 0f), (116f, 62.6f, -90f), (132f, 62.6f, -90f), (96f, 57.4f, 90f), (121f, 48.4f, -90f), (88f, 64f, -90f) })
            ParkLamp(g, new Vector3(x, G, z), yaw, M);

        // ---------- 경계 ----------
        // 서쪽: 판자 담장 너머 나무들(걸어서 못 가는 곳에만 나무를 둔다: 나무 수관 속으로 카메라가 들어가 화면이 새카매지는 일을 막는다)
        P.WoodFence(g, new Vector3(ParkX0, G, ParkZ0), new Vector3(ParkX0, G, ParkZ1 + 0.5f), 2.4f, M);
        for (float z = 30f; z < ParkZ1; z += 8f) P.Prop("StreetTree", g, new Vector3(ParkX0 - 3f, G, z), z * 41f % 360f);
        // 북쪽: 공원을 바라보는 아파트 동들, 사이는 판자 담장
        float[][] blocks = { new[] { 56f, 76f, 15f }, new[] { 78f, 100f, 18f }, new[] { 102f, 122f, 14f }, new[] { 124f, 140f, 16f } };
        var facades = new[] { M["FacadeA"], M["FacadeB"], M["FacadeC"] };
        for (int i = 0; i < blocks.Length; i++)
        {
            float x0 = blocks[i][0], x1 = blocks[i][1], h = blocks[i][2];
            P.Building(g, $"Apartment_{i}", new Vector3((x0 + x1) / 2f, h / 2f, ParkZ1 + 6.5f), new Vector3(x1 - x0, h, 12f), facades[i % 3], M["BuildingBack"], Vector3.back);
            if (i + 1 < blocks.Length) P.WoodFence(g, new Vector3(x1, G, ParkZ1 + 0.6f), new Vector3(blocks[i + 1][0], G, ParkZ1 + 0.6f), 2.4f, M);
        }
        P.WoodFence(g, new Vector3(ParkX0, G, ParkZ1 + 0.6f), new Vector3(56f, G, ParkZ1 + 0.6f), 2.4f, M);
        P.WoodFence(g, new Vector3(140f, G, ParkZ1 + 0.6f), new Vector3(AveW, G, ParkZ1 + 0.6f), 2.4f, M);
        // 동쪽: 철망. 관리 도로가 나가는 출구만 끊겨 있다
        P.Fence(g, new Vector3(ParkX1, G, ParkZ0), new Vector3(ParkX1, G, ExitGap0), 2.6f, M);
        P.Fence(g, new Vector3(ParkX1, G, ExitGap1), new Vector3(ParkX1, G, ParkZ1 + 0.6f), 2.6f, M);
        foreach (float z in new[] { ExitGap0 - 0.25f, ExitGap1 + 0.25f })
            P.Box(g, "ExitPillar", new Vector3(ParkX1, G + 1.3f, z), new Vector3(0.5f, 2.6f, 0.5f), M["BuildingBack"], Vector2.one);
        // 공원 철망 밖 ~ 큰길 인도 사이 잔디 띠(대니네 거리 담장 끝까지)
        P.WoodFence(g, new Vector3(150f, G, ParkZ0), new Vector3(AveW, G, ParkZ0), 2f, M);

        // ---------- 공원 출구의 검은 차(출구를 막고 있다) ----------
        P.Prop("Sedan", g, new Vector3(146.2f, G, LaneZ), 0f);
        // ---------- 관리 도로의 검은 차(통화하는 조직원), 차 옆 상자에 상표 없는 테이프 ----------
        P.Prop("Sedan", g, new Vector3(100f, G, LaneZ + 0.2f), -90f); // 앞이 서쪽, 뒤(번호판)는 동쪽(산책길 쪽)

        // 트리거·표시
        b.parkExit = P.Marker(triggers, "ParkExit", new Vector3(ParkX1 + 0.8f, 1.4f, LaneZ), 90f);
        b.spotTrigger = Trigger(triggers, "SpotTrigger", new Vector3(104f, 1f, 36.6f), new Vector3(9.6f, 2f, 2.4f));
        b.spotterStep = P.Marker(triggers, "SpotterStep", new Vector3(108.3f, G, 40.5f), 225f);
        b.callTrigger = Trigger(triggers, "CallTrigger", new Vector3(103.5f, 1f, LaneZ - 1f), new Vector3(9f, 2f, 9f));
        b.plateTrigger = Trigger(triggers, "PlateTrigger", new Vector3(104.4f, 1f, LaneZ + 0.2f), new Vector3(2.6f, 2f, 4.4f));

        // 테이프 줍는 곳(회복약도 함께): 이야기상 꼭 지나는 차 옆 상자 + 찾아가면 있는 곳 셋
        b.vanTape = TapeSpot(g, triggers, "VanTape", new Vector3(105.2f, G, LaneZ - 2.4f), 2, 1, box: true); // 산책길(x 106.2) 바로 옆: 길잡이만 따라가도 줍는다
        TapeSpot(g, triggers, "PavilionTape", new Vector3(84.3f, G + 0.47f, 58.6f), 1, 1, box: false);   // 정자 벤치 위(정자 순찰 곁)
        TapeSpot(g, triggers, "RestroomTape", new Vector3(114.6f, G, 41.3f), 1, 1, box: true);           // 화장실 뒤(그 조직원이 서 있던 곳 옆)
    }

    /// <summary>
    /// 테이프 줍는 곳: 떠서 도는 카세트와 은은한 불빛. 다가가면 되감기 전용 테이프(charges회)와 회복약(potions개)을 얻는다.
    /// box면 바닥에 놓인 상자 위, 아니면 그 자리(벤치 위 등) 바로 위에 뜬다
    /// </summary>
    static TapePickup TapeSpot(Transform art, Transform triggers, string name, Vector3 floor, int charges, int potions, bool box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(triggers);
        go.transform.position = floor + Vector3.up * (box ? 0.3f : 0.12f);
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(1.2f, 1.6f, 1.2f);
        col.isTrigger = true;
        if (box)
        {
            var parcel = P.Prop("ParcelBox", art, floor, 15f);
            parcel.transform.localScale = Vector3.one * 1.6f;
        }
        var cassette = P.Prop("CassetteTape", go.transform, go.transform.position + Vector3.up * 0.15f, 0f);
        cassette.transform.localScale = Vector3.one * 2.6f;
        foreach (var t in cassette.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); // 위아래로 떠다닌다
        P.AddLight(go.transform, "TapeGlint", new Vector3(0f, 0.5f, 0f), LightType.Point, new Color(1f, 0.8f, 0.5f), 0.9f, 2f, local: true);
        if (potions > 0)
        {
            // 회복약: 테이프 옆에 놓인 작은 빨간 구급상자(주우면 테이프와 함께 사라진다)
            var kit = TryProp("MedKit", go.transform, floor + new Vector3(0.46f, 0f, 0.08f), 200f); // 상자(반폭 0.28m) 옆
            if (kit)
            {
                // 생성 모델에서 흰 십자가 흰 띠로 뭉개졌다: 뚜껑 위에 흰 십자를 붙여 한눈에 구급상자로 보이게
                var kb = WorldBounds(kit);
                var top = new Vector3(kb.center.x, kb.max.y + 0.002f, kb.center.z);
                foreach (var size in new[] { new Vector3(0.1f, 0.004f, 0.032f), new Vector3(0.032f, 0.004f, 0.1f) })
                {
                    var bar = P.Box(kit.transform, "FirstAidCross", top, size, X_FirstAid(), Vector2.one, collider: false);
                    bar.transform.SetPositionAndRotation(top, kit.transform.rotation);
                }
                foreach (var t in kit.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
        }
        var pickup = go.AddComponent<TapePickup>();
        pickup.tape = new CassetteTape { title = "상표 없는 테이프", trackTitles = new[] { "—", "—", "—" }, charges = charges, playable = false };
        pickup.potions = potions;
        pickup.visual = cassette.transform;
        return pickup;
    }

    /// <summary>
    /// 공원 공중화장실(x 109~115, z 36.5~39.7, 높이 3m). 정원(남쪽)을 보는 면에 남자(서쪽)·여자(동쪽) 입구가 하나씩.
    /// 공원 화장실처럼 문짝 없이 뚫린 입구 → 흰 타일 통로(안쪽에서 꺾여 들어가 속은 보이지 않는다), 통로마다 차가운 형광등.
    /// 입구 위에는 글자 없이 남녀 그림 표시판만 붙인다. 바깥 벽 아랫단은 짙은 타일, 처마와 홈통.
    /// </summary>
    static void Restroom(Transform g, Dictionary<string, Material> M, Dictionary<string, Material> X)
    {
        const float x0 = 109f, x1 = 115f, zf = 36.5f, zb = 39.7f, h = 3f;
        const float hall = 1.6f, doorW = 1.1f, doorH = 2.25f;
        const float menX = 110.25f, womenX = 113.75f;
        float zi = zf + hall;
        var wall = X["WcPlaster"];
        // 통로 안쪽 벽부터 뒤까지 한 덩어리
        P.Box(g, "Restroom_Back", new Vector3((x0 + x1) / 2f, G + h / 2f, (zi + zb) / 2f), new Vector3(x1 - x0, h, zb - zi), wall, new Vector2(2f, 2f));
        // 앞쪽: 서쪽 기둥 | 남자 입구 | 가운데 | 여자 입구 | 동쪽 기둥
        void Front(string n, float a, float c)
        {
            P.Box(g, n, new Vector3((a + c) / 2f, G + h / 2f, (zf + zi) / 2f), new Vector3(c - a, h, zi - zf), wall, new Vector2(2f, 2f));
            P.Box(g, n + "_Wainscot", new Vector3((a + c) / 2f, G + 0.5f, zf - 0.012f), new Vector3(c - a, 1f, 0.02f), X["WcTileDark"], new Vector2(0.6f, 0.6f), collider: false);
        }
        Front("Restroom_PierW", x0, menX - doorW / 2f);
        Front("Restroom_Middle", menX + doorW / 2f, womenX - doorW / 2f);
        Front("Restroom_PierE", womenX + doorW / 2f, x1);
        foreach (var (cx, female) in new[] { (menX, false), (womenX, true) })
        {
            P.Box(g, "Restroom_Lintel", new Vector3(cx, G + (doorH + h) / 2f, (zf + zi) / 2f), new Vector3(doorW, h - doorH, zi - zf), wall, new Vector2(2f, 2f));
            // 통로: 흰 타일 바닥·벽, 안쪽 끝 한쪽은 꺾여 들어가는 어두운 틈(안은 보이지 않는다)
            P.Box(g, "Hall_Floor", new Vector3(cx, G + 0.012f, (zf + zi) / 2f), new Vector3(doorW, 0.02f, zi - zf), X["WcTile"], new Vector2(0.6f, 0.6f), collider: false);
            P.Box(g, "Hall_Back", new Vector3(cx, G + doorH / 2f, zi - 0.015f), new Vector3(doorW, doorH, 0.02f), X["WcTile"], new Vector2(0.6f, 0.6f), collider: false);
            foreach (float side in new[] { -1f, 1f })
                P.Box(g, "Hall_Side", new Vector3(cx + side * (doorW / 2f - 0.015f), G + doorH / 2f, (zf + zi) / 2f), new Vector3(0.02f, doorH, zi - zf), X["WcTile"], new Vector2(0.6f, 0.6f), collider: false);
            float turn = female ? 1f : -1f; // 바깥 기둥 쪽으로 꺾여 들어간다
            P.Box(g, "Hall_Turn", new Vector3(cx + turn * (doorW / 2f - 0.03f), G + 1.05f, zi - 0.45f), new Vector3(0.02f, 2.1f, 0.7f), X["HallDark"], Vector2.one, collider: false);
            P.Box(g, "Hall_Tube", new Vector3(cx, G + doorH - 0.04f, zi - 0.55f), new Vector3(0.6f, 0.04f, 0.08f), X["Fluor"], Vector2.one, collider: false);
            P.AddLight(g, "Hall_Fluorescent", new Vector3(cx, G + 2.05f, zi - 0.6f), LightType.Point, new Color(0.86f, 0.94f, 1f), 0.75f, 3.6f); // 좁은 타일 통로라 약하게(1.9에서는 하얗게 탔다)
            // 입구 위 남녀 그림 표시(글자 없음)
            var plate = P.Box(g, female ? "Picto_Women" : "Picto_Men", new Vector3(cx, G + doorH + 0.36f, zf - 0.02f), new Vector3(0.44f, 0.44f, 0.02f), female ? X["PictoWoman"] : X["PictoMan"], Vector2.one, collider: false);
            FitUVSouth(plate, 0.44f, 0.44f);
        }
        // 지붕(처마), 처마 테, 홈통, 가운데 벽등, 서쪽 높은 간유리 창
        var wc = new Vector3((x0 + x1) / 2f, G + h / 2f, (zf + zb) / 2f);
        P.Box(g, "RestroomRoof", wc + Vector3.up * 1.6f, new Vector3(6.6f, 0.2f, 3.8f), X["Roof"], new Vector2(2f, 2f));
        P.Box(g, "RoofTrim", wc + new Vector3(0f, 1.49f, -1.9f), new Vector3(6.64f, 0.12f, 0.06f), Mat("Galvanized"), Vector2.one, collider: false);
        P.Box(g, "Downpipe", new Vector3(x1 - 0.1f, G + 1.5f, zf - 0.08f), new Vector3(0.09f, 3f, 0.09f), Mat("Galvanized"), Vector2.one, collider: false);
        HouseBuilder.WallLamp(g, "RestroomLamp", new Vector3((x0 + x1) / 2f, G + 2.5f, zf), 0f, HouseBuilder.SharedMaterials());
        P.AddLight(g, "RestroomLampLight", new Vector3((x0 + x1) / 2f, G + 2.3f, zf - 0.6f), LightType.Point, new Color(1f, 0.86f, 0.62f), 2.4f, 7f);
        P.Box(g, "RestroomWindow_W", new Vector3(x0 - 0.03f, G + 2.35f, 38.6f), new Vector3(0.04f, 0.36f, 1.6f), X["Frosted"], Vector2.one, collider: false);
    }

    /// <summary>사람 키를 넘는 생울타리(덤불을 위로 늘린다: 서 있는 레이의 머리까지 가린다)</summary>
    static GameObject TallHedge(Transform parent, Vector3 pos, float yaw)
    {
        var h = P.Prop("Hedge", parent, pos, yaw);
        h.transform.localScale = new Vector3(1.04f, 1.62f, 1.15f);
        return h;
    }

    /// <summary>산책길 낮은 기둥등(무릎 높이): 길과 생울타리 아래쪽을 은은하게 비춘다</summary>
    static void Bollard(Transform parent, Vector3 pos, Dictionary<string, Material> X)
    {
        P.Box(parent, "Bollard", pos + Vector3.up * 0.42f, new Vector3(0.16f, 0.84f, 0.16f), Mat("Galvanized"), Vector2.one);
        P.Box(parent, "BollardCap", pos + Vector3.up * 0.8f, new Vector3(0.17f, 0.07f, 0.17f), X["BollardGlow"], Vector2.one, collider: false);
        P.AddLight(parent, "BollardLight", pos + Vector3.up * 0.85f, LightType.Point, new Color(1f, 0.82f, 0.58f), 1.5f, 4.5f);
    }

    static void ParkLamp(Transform parent, Vector3 pos, float yaw, Dictionary<string, Material> M)
    {
        var lamp = P.Prop("StreetLampLed", parent, pos, yaw);
        var head = new Vector3(0.95f, 5.82f, 0f);
        P.AddLight(lamp.transform, "LampLight", head, LightType.Spot, new Color(1f, 0.93f, 0.82f), 70f, 16f, 110f, Quaternion.Euler(90f, 0f, 0f), local: true);
        var glow = P.Box(lamp.transform, "LampGlow", Vector3.zero, new Vector3(0.5f, 0.02f, 0.16f), M["LampGlow"], Vector2.one, collider: false);
        glow.transform.localPosition = head + Vector3.down * 0.06f;
        glow.transform.localRotation = Quaternion.identity;
    }

    static void Bench(Transform parent, Vector3 pos, float yaw, Dictionary<string, Material> M)
    {
        var bench = new GameObject("Bench").transform;
        bench.SetParent(parent, false);
        bench.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        void Part(string n, Vector3 c, Vector3 s, Material m, bool col = false)
        {
            var p = P.Box(bench, n, Vector3.zero, s, m, new Vector2(1f, 1f), collider: col);
            p.transform.localPosition = c;
            p.transform.localRotation = Quaternion.identity;
        }
        // 앉는 판 세 줄 + 등받이 두 줄, 양끝 철제 다리
        for (int i = 0; i < 3; i++) Part("Slat", new Vector3(0f, 0.45f, -0.16f + i * 0.14f), new Vector3(1.8f, 0.04f, 0.11f), M["FloorWood"]);
        for (int i = 0; i < 2; i++) Part("Back", new Vector3(0f, 0.62f + i * 0.16f, 0.15f), new Vector3(1.8f, 0.11f, 0.03f), M["FloorWood"]);
        foreach (float x in new[] { -0.78f, 0.78f })
        {
            Part("Leg", new Vector3(x, 0.22f, -0.12f), new Vector3(0.05f, 0.44f, 0.05f), M["Galvanized"]);
            Part("Leg", new Vector3(x, 0.44f, 0.16f), new Vector3(0.05f, 0.88f, 0.05f), M["Galvanized"]);
            Part("Rail", new Vector3(x, 0.43f, 0.01f), new Vector3(0.05f, 0.04f, 0.38f), M["Galvanized"]);
        }
        var c = bench.gameObject.AddComponent<BoxCollider>();
        c.center = new Vector3(0f, 0.45f, 0f);
        c.size = new Vector3(1.85f, 0.9f, 0.5f);
        GameObjectUtility.SetStaticEditorFlags(bench.gameObject, StaticEditorFlags.NavigationStatic);
    }

    static void Pavilion(Transform parent, Vector3 pos, Dictionary<string, Material> M, Dictionary<string, Material> X)
    {
        var p = new GameObject("Pavilion").transform;
        p.SetParent(parent, false);
        p.position = pos;
        foreach (float x in new[] { -2.3f, 2.3f })
            foreach (float z in new[] { -2.3f, 2.3f })
                P.Box(p, "Post", pos + new Vector3(x, 1.4f, z), new Vector3(0.22f, 2.8f, 0.22f), M["DarkWood"], Vector2.one);
        P.Box(p, "Beam", pos + new Vector3(0f, 2.86f, 0f), new Vector3(5f, 0.16f, 5f), M["DarkWood"], Vector2.one, collider: false);
        P.Box(p, "Roof", pos + new Vector3(0f, 3.06f, 0f), new Vector3(6f, 0.24f, 6f), X["Roof"], new Vector2(2f, 2f), collider: false);
        P.Box(p, "Deck", pos + new Vector3(0f, 0.06f, 0f), new Vector3(5.2f, 0.12f, 5.2f), M["FloorWood"], new Vector2(1.5f, 1.5f), collider: false);
        P.AddLight(p, "PavilionLight", pos + new Vector3(0f, 2.6f, 0f), LightType.Point, new Color(1f, 0.82f, 0.56f), 2.2f, 7f);
    }

    // =====================================================================
    // 큰길
    // =====================================================================

    static void BuildAvenue(Dictionary<string, Material> M, Dictionary<string, Material> X, Transform triggers, Built b)
    {
        var g = P.Group("Avenue");
        const float z0 = -40f, z1 = 160f, zc = (z0 + z1) / 2f, len = z1 - z0;
        P.Box(g, "AveRoad", new Vector3(AveX, -0.25f, zc), new Vector3(AveRoad1 - AveRoad0, 0.5f, len), M["Asphalt"], new Vector2(4f, 4f));
        P.Box(g, "AveSidewalk_W", new Vector3((AveW + AveRoad0) / 2f, 0.075f, zc), new Vector3(AveRoad0 - AveW, 0.15f, len), M["Sidewalk"], new Vector2(2f, 2f));
        P.Box(g, "AveSidewalk_E", new Vector3((AveRoad1 + AveE) / 2f, 0.075f, zc), new Vector3(AveE - AveRoad1, 0.15f, len), M["Sidewalk"], new Vector2(2f, 2f));
        P.Box(g, "AveCurb_W", new Vector3(AveRoad0 + 0.1f, 0.08f, zc), new Vector3(0.2f, 0.16f, len), M["Curb"], Vector2.one, collider: false);
        P.Box(g, "AveCurb_E", new Vector3(AveRoad1 - 0.1f, 0.08f, zc), new Vector3(0.2f, 0.16f, len), M["Curb"], Vector2.one, collider: false);
        for (float z = z0 + 3f; z < z1; z += 6f)
            P.Box(g, "LaneDash", new Vector3(AveX, 0.004f, z), new Vector3(0.12f, 0.01f, 3f), M["Paint"], Vector2.one, collider: false);
        // 횡단보도(공원 출구 앞)
        for (float x = AveRoad0 + 0.6f; x < AveRoad1 - 0.4f; x += 1.1f)
            P.Box(g, "Crosswalk", new Vector3(x, 0.005f, LaneZ), new Vector3(0.55f, 0.01f, 3.4f), M["Paint"], Vector2.one, collider: false);

        // 가로등: 서쪽 인도는 머리가 +x, 동쪽은 -x로 차도를 본다(기둥이 연석 쪽)
        for (float z = -30f; z < 156f; z += 24f)
        {
            AveLamp(g, new Vector3(AveRoad0 - 0.4f + 0.95f, 0.15f, z), 0f, M);
            AveLamp(g, new Vector3(AveRoad1 + 0.4f - 0.95f, 0.15f, z + 12f), 180f, M);
        }
        P.Prop("PedestrianLight", g, new Vector3(AveRoad0 - 0.5f, 0.15f, LaneZ + 2.4f), 90f);
        P.Prop("PedestrianLight", g, new Vector3(AveRoad1 + 0.5f, 0.15f, LaneZ - 2.4f), -90f);
        P.Prop("BusStop", g, new Vector3(AveW + 1.2f, 0.15f, 76f), 90f);
        P.Prop("VendingMachine", g, new Vector3(AveW + 0.7f, 0.15f, 80.6f), 90f); // 공원 화장실 앞에서 옮겼다: 정류장 옆 자판기
        TryProp("ParkBin", g, new Vector3(AveW + 0.6f, 0.15f, 82.4f), 0f);
        P.AddLight(g, "VendingGlow", new Vector3(AveW + 1.5f, 1.35f, 80.6f), LightType.Point, new Color(0.75f, 0.85f, 1f), 1.6f, 4.5f);
        TapeSpot(g, triggers, "BusStopTape", new Vector3(AveW + 1.0f, 0.15f, 73.2f), 1, 1, box: true); // 정류장 옆에 버려진 상자
        P.Prop("FireHydrant", g, new Vector3(AveRoad1 + 0.5f, 0.15f, 54f), 90f);
        P.Prop("RecyclingBins", g, new Vector3(AveE - 0.4f, 0.15f, 70.6f), -90f);
        P.Prop("Sedan", g, new Vector3(AveRoad1 - 1.5f, 0f, 40f), 180f);
        P.Prop("Sedan", g, new Vector3(AveRoad0 + 1.5f, 0f, 92f), 0f);

        // 동쪽 상가(가게 1은 BuildShop). 1층은 셔터를 내린 가게들
        var facades = new[] { M["FacadeA"], M["FacadeB"], M["FacadeC"] };
        float[][] east = { new[] { -40f, -12f, 14f }, new[] { -10f, 20f, 18f }, new[] { 22f, 44f, 12f }, new[] { 46f, ShopZ0, 15f }, new[] { ShopZ1, 96f, 20f }, new[] { 98f, 126f, 13f }, new[] { 128f, 160f, 17f } };
        for (int i = 0; i < east.Length; i++)
        {
            float a = east[i][0], c = east[i][1], h = east[i][2];
            P.Building(g, $"Shops_E{i}", new Vector3(AveE + 8f, h / 2f, (a + c) / 2f), new Vector3(16f, h, c - a), facades[i % 3], M["BuildingBack"], Vector3.left);
            Shutters(g, a, c, X);
        }
        // 서쪽: 공원 북쪽 너머와 대니네 거리 남쪽 너머
        P.Building(g, "Block_W0", new Vector3(147.5f, 8f, 98f), new Vector3(15f, 16f, 24f), M["FacadeB"], M["BuildingBack"], Vector3.right);
        P.Building(g, "Block_W1", new Vector3(147.5f, 10f, 136f), new Vector3(15f, 20f, 48f), M["FacadeC"], M["BuildingBack"], Vector3.right);
        P.Building(g, "Block_W2", new Vector3(147.5f, 7f, -34f), new Vector3(15f, 14f, 12f), M["FacadeA"], M["BuildingBack"], Vector3.right);
        // 북쪽 끝은 시장 광장(구역 2)으로 이어진다(BuildMarketSquare)
        // 남쪽: 도로 공사로 막혀 있다
        P.Fence(g, new Vector3(AveW, 0f, -8f), new Vector3(AveE, 0f, -8f), 2.6f, M);
        foreach (float x in new[] { 159.5f, 162.6f, 165.7f, 168.8f }) P.Prop("ConstructionBarrier", g, new Vector3(x, 0f, -6.6f), 0f);
        P.AddLight(g, "WorkLight_Ave", new Vector3(AveX, 2.4f, -7f), LightType.Point, new Color(1f, 0.6f, 0.25f), 3f, 9f);

        b.northEnd = P.Marker(triggers, "NorthEnd", new Vector3(AveX, 1.4f, 128f), 0f);
        b.endTrigger = Trigger(triggers, "EndTrigger", new Vector3(AveX - 1.5f, 1.5f, 128f), new Vector3(AveE - AveW + 2f, 3f, 4f)); // 검문소가 보이는 자리(구역 2 시작)
    }

    static void AveLamp(Transform parent, Vector3 pos, float yaw, Dictionary<string, Material> M) => ParkLamp(parent, pos, yaw, M);

    /// <summary>상가 1층: 셔터 내린 가게 앞면(가게 폭마다 셔터 하나, 사이는 돌기둥)</summary>
    static void Shutters(Transform parent, float z0, float z1, Dictionary<string, Material> X)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / 6f));
        float w = (z1 - z0) / n;
        for (int i = 0; i < n; i++)
        {
            float zc = z0 + w * (i + 0.5f);
            P.Box(parent, "Shutter", new Vector3(AveE - 0.03f, G + 1.35f, zc), new Vector3(0.06f, 2.7f, w - 1.2f), X["Shutter"], new Vector2(w - 1.2f, 2.7f), collider: false);
        }
    }

    // =====================================================================
    // 가게 1 「심야 레코드」: 진열창 너머로 실내가 보인다
    // =====================================================================

    static void BuildShop(Dictionary<string, Material> M, Dictionary<string, Material> X, Transform triggers, Built b)
    {
        var g = P.Group("Shop_MidnightRecords");
        float depth = 9f, x0 = AveE, x1 = AveE + depth, h = 3.4f, zc = (ShopZ0 + ShopZ1) / 2f, wz = ShopZ1 - ShopZ0;
        // 위층(2~4층): 창문 외벽이 가게 바로 위에서 시작한다. 가게 상자 뒤쪽 1층은 막힌 벽
        var upperSize = new Vector3(16f, 12f, wz);
        var upper = P.Box(g, "ShopUpper", new Vector3(x0 + 8f, 3.5f + 6f, zc), upperSize, M["FacadeC"], new Vector2(16f, 14f));
        upper.GetComponent<MeshFilter>().sharedMesh = P.BoxMesh(upperSize, new Vector2(16f, 14f), new[] { Vector3.left });
        upper.GetComponent<MeshRenderer>().sharedMaterials = new[] { M["FacadeC"], M["BuildingBack"] };
        P.Box(g, "ShopRear", new Vector3((x1 + 0.2f + x0 + 16f) / 2f, 1.75f, zc), new Vector3(x0 + 16f - x1 - 0.2f, 3.5f, wz), M["BuildingBack"], new Vector2(2f, 2f));
        // 1층 가게 상자: 바닥·천장·뒤·옆벽
        P.Box(g, "ShopFloor", new Vector3((x0 + x1) / 2f, 0.075f, zc), new Vector3(depth, 0.15f, wz), M["FloorWood"], new Vector2(2f, 2f));
        P.Box(g, "ShopCeiling", new Vector3((x0 + x1) / 2f, h + 0.05f, zc), new Vector3(depth, 0.1f, wz), M["Ceiling"], new Vector2(2f, 2f), collider: false);
        P.Box(g, "ShopBack", new Vector3(x1 + 0.1f, h / 2f, zc), new Vector3(0.2f, h, wz), X["ShopWall"], new Vector2(2f, 2f));
        // 옆벽은 이웃 건물 면(z = ShopZ0/ShopZ1)보다 안쪽에: 같은 자리에 면이 겹치면 깜박인다
        P.Box(g, "ShopSide_S", new Vector3((x0 + x1) / 2f, h / 2f, ShopZ0 + 0.1f), new Vector3(depth, h, 0.2f), X["ShopWall"], new Vector2(2f, 2f));
        P.Box(g, "ShopSide_N", new Vector3((x0 + x1) / 2f, h / 2f, ShopZ1 - 0.1f), new Vector3(depth, h, 0.2f), X["ShopWall"], new Vector2(2f, 2f));
        // 앞면(x = 173): 아래 벽 · 진열창 · 문 · 위 벽
        float glassZ0 = ShopZ0 + 0.6f, glassZ1 = ShopDoorZ - 1.0f, sill = 0.6f, top = 2.75f;
        P.Box(g, "FrontSill", new Vector3(x0 - 0.1f, sill / 2f, (glassZ0 + glassZ1) / 2f), new Vector3(0.24f, sill, glassZ1 - glassZ0), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontHeader", new Vector3(x0 - 0.1f, (top + h) / 2f + 0.05f, zc), new Vector3(0.24f, h - top + 0.1f, wz + 0.2f), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_S", new Vector3(x0 - 0.1f, h / 2f, (ShopZ0 + glassZ0) / 2f), new Vector3(0.24f, h, glassZ0 - ShopZ0 + 0.2f), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_Mid", new Vector3(x0 - 0.1f, h / 2f, (glassZ1 + ShopDoorZ - 0.6f) / 2f), new Vector3(0.24f, h, ShopDoorZ - 0.6f - glassZ1), M["FacadeBase"], Vector2.one);
        P.Box(g, "FrontPier_N", new Vector3(x0 - 0.1f, h / 2f, (ShopDoorZ + 0.6f + ShopZ1) / 2f), new Vector3(0.24f, h, ShopZ1 - ShopDoorZ - 0.6f + 0.2f), M["FacadeBase"], Vector2.one);
        var glass = P.Box(g, "ShopWindow", new Vector3(x0 - 0.06f, (sill + top) / 2f, (glassZ0 + glassZ1) / 2f), new Vector3(0.03f, top - sill, glassZ1 - glassZ0), X["Glass"], Vector2.one);
        glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        // 문: 유리문(위쪽 절반 유리) — 닫혀 있다. 들어가지 않고 문 앞에서 F로 가게를 본다
        // 짙은 빨간 문(차양과 같은 색 계열): 검은 문은 밤에 뻥 뚫린 구멍처럼 보였다
        var door = HouseBuilder.InteriorDoor(g, "ShopDoor", new Vector3(x0 - 0.22f, G, ShopDoorZ), -90f, HouseBuilder.Door("Red"), HouseBuilder.SharedMaterials());
        // 차양과 간판
        P.Box(g, "Awning", new Vector3(x0 - 0.8f, top + 0.2f, zc), new Vector3(1.4f, 0.12f, wz - 0.4f), X["Awning"], new Vector2(1f, 1f), collider: false)
            .transform.rotation = Quaternion.Euler(0f, 0f, -12f);
        // 간판: 검은 판 위 두 줄 네온 — 필기체 "Midnight"(위), 각진 대문자 "RECORDS"(아래)
        P.Box(g, "Sign", new Vector3(x0 - 0.3f, h + 0.72f, zc), new Vector3(0.16f, 1.3f, 5.4f), X["SignBoard"], Vector2.one, collider: false);
        var midnight = P.Box(g, "SignMidnight", new Vector3(x0 - 0.39f, h + 0.98f, zc - 0.2f), new Vector3(0.01f, 0.8f, 3.2f), X["SignMidnight"], Vector2.one, collider: false);
        FitUV(midnight, 3.2f, 0.8f);
        var records = P.Box(g, "SignRecords", new Vector3(x0 - 0.39f, h + 0.38f, zc + 0.35f), new Vector3(0.01f, 0.5f, 2.0f), X["SignRecords"], Vector2.one, collider: false);
        FitUV(records, 2.0f, 0.5f);
        var open = P.Box(g, "SignOpen", new Vector3(x0 - 0.1f, 2.2f, ShopDoorZ - 1.75f), new Vector3(0.01f, 0.32f, 1.3f), X["SignOpen"], Vector2.one, collider: false);
        FitUV(open, 1.3f, 0.32f);
        P.AddLight(g, "SignLight", new Vector3(x0 - 0.9f, h + 0.9f, zc), LightType.Point, new Color(1f, 0.55f, 0.3f), 1.8f, 6f); // 간판 앞, 사람 머리보다 높이(가까이 선 레이가 하얗게 타지 않게)

        // 실내: 테이프 진열장, 계산대, 안쪽 방 입구(구슬발), 따뜻한 조명
        foreach (float z in new[] { ShopZ0 + 1.2f, ShopZ0 + 2.6f, ShopZ0 + 4.0f, ShopZ0 + 5.4f })
            P.Prop("CassetteShelf", g, new Vector3(x1 - 0.2f, G, z), -90f);
        foreach (float x in new[] { x0 + 2.4f, x0 + 3.8f, x0 + 5.2f })
            P.Prop("CassetteShelf", g, new Vector3(x, G, ShopZ0 + 0.42f), 0f);
        P.Box(g, "Counter", new Vector3(x0 + 5.6f, G + 0.5f, ShopZ1 - 1.6f), new Vector3(2.6f, 1f, 0.7f), M["DarkWood"], Vector2.one);
        // 계산대 위: 턴테이블과 옛날 계산기(가게 안쪽을 본다). 바로크 소품이 없으면 예전 텔레비전
        bool counterProps = TryProp("Turntable", g, new Vector3(x0 + 6.25f, G + 1f, ShopZ1 - 1.6f), 180f) != null;
        if (counterProps) TryProp("CashRegister", g, new Vector3(x0 + 4.95f, G + 1f, ShopZ1 - 1.62f), 180f);
        else P.Prop("CrtTv", g, new Vector3(x0 + 6.2f, G + 1f, ShopZ1 - 1.6f), -90f);
        // 가운데 낮은 진열대 둘: 레코드 상자를 진열창 쪽을 보게 올려 둔다. 창 밑에도 하나
        foreach (float tz in new[] { ShopZ0 + 2.4f, ShopZ0 + 5.0f })
        {
            P.Box(g, "RecordTable", new Vector3(x0 + 3.0f, G + 0.36f, tz), new Vector3(0.9f, 0.72f, 1.9f), M["DarkWood"], Vector2.one);
            // 레코드가 꽂힌 옆면(+x)이 진열창(-x) 쪽을 보게 돌린다(반대쪽 면에 찢긴 듯한 흠이 있어 안쪽으로 숨긴다)
            TryProp("RecordCrate", g, new Vector3(x0 + 3.0f, G + 0.72f, tz - 0.45f), 180f);
            TryProp("RecordCrate", g, new Vector3(x0 + 3.0f, G + 0.72f, tz + 0.45f), 180f);
        }
        TryProp("RecordCrate", g, new Vector3(x0 + 0.75f, G, ShopZ0 + 1.6f), 180f);
        // 진열대·계산대 위 천장 조명(아래로 비추는 레일 조명): 어두운 가게 안에서 레코드 상자와 계산대가 진열창 너머로 보이게
        foreach (var (lx, lz) in new[] { (x0 + 3.0f, ShopZ0 + 2.4f), (x0 + 3.0f, ShopZ0 + 5.0f), (x0 + 5.6f, ShopZ1 - 1.6f) })
            P.AddLight(g, "ShopSpot", new Vector3(lx, h - 0.15f, lz), LightType.Spot, new Color(1f, 0.8f, 0.55f), 5.5f, 4.5f, 70f, Quaternion.Euler(90f, 0f, 0f));
        P.Box(g, "BackRoomDoorway", new Vector3(x1 - 0.02f, 1.1f, ShopZ1 - 1.4f), new Vector3(0.04f, 2.2f, 1.1f), M["DarkWood"], Vector2.one, collider: false);
        P.AddLight(g, "ShopLight_A", new Vector3(x0 + 3f, h - 0.3f, zc - 1.5f), LightType.Point, new Color(1f, 0.74f, 0.45f), 4.2f, 8f);
        P.AddLight(g, "ShopLight_B", new Vector3(x0 + 6.5f, h - 0.3f, zc + 1.8f), LightType.Point, new Color(1f, 0.7f, 0.42f), 3.4f, 7f);
        P.AddLight(g, "ShopSpill", new Vector3(x0 - 0.35f, 2.3f, (glassZ0 + glassZ1) / 2f), LightType.Point, new Color(1f, 0.68f, 0.4f), 1.1f, 5f); // 진열창 바로 앞 위쪽: 인도로 새어 나오는 불빛

        // 가게(교환 메뉴)와 다가가면 저장
        var shopGo = new GameObject("TapeShop_MidnightRecords");
        shopGo.transform.SetParent(triggers);
        shopGo.transform.position = new Vector3(x0 - 1.3f, 1f, ShopDoorZ);
        var sc = shopGo.AddComponent<BoxCollider>();
        sc.size = new Vector3(2.6f, 2f, 2.6f);
        sc.isTrigger = true;
        var shop = shopGo.AddComponent<TapeShop>();
        shop.shopName = "심야 레코드";
        shop.stock = new List<TapeShop.Item>
        {
            // 곡 크기 보정: 앞 2분 평균 크기를 -15dB로 맞춘 값(곡 고를 때 잰 값: -14.1 -14.3 -13.7 / -15.1 -16.4 -16.3 / -14.1 -14.6 -14.1 dB)
            new() { tape = MusicTape("심야 라디오 B면", "late_radio", new[] { 0.90f, 0.92f, 0.86f }, "비 오는 고가도로", "새벽 세 시", "꺼지지 않는 간판") },
            new() { tape = MusicTape("빗소리 재즈", "rain_jazz", new[] { 1.01f, 1.18f, 1.16f }, "젖은 트럼펫", "골목의 블루스", "마지막 손님") },
            new() { tape = MusicTape("시티팝 모음 1", "citypop_1", new[] { 0.90f, 0.95f, 0.90f }, "네온 드라이브", "한밤의 정류장", "다시 봄") },
        };
        b.shop = shop;
        b.shopApproach = Trigger(triggers, "ShopApproach", new Vector3(AveRoad1 + 1.5f, 1f, zc), new Vector3(3.2f, 2f, wz + 2f));
        b.shopSave = P.Marker(triggers, "ShopSave", new Vector3(AveRoad1 + 1.6f, 0.15f, ShopDoorZ), -90f);
    }

    /// <summary>남쪽(-z)을 보는 얇은 판의 앞면 UV를 0~1로 맞춘다</summary>
    static void FitUVSouth(GameObject panel, float width, float height)
    {
        var mf = panel.GetComponent<MeshFilter>();
        var mesh = Object.Instantiate(mf.sharedMesh);
        var verts = mesh.vertices;
        var uv = new Vector2[verts.Length];
        // -z를 보면(남쪽에서 북쪽을 보면) 오른쪽이 +x
        for (int i = 0; i < verts.Length; i++) uv[i] = new Vector2(Mathf.InverseLerp(-width / 2f, width / 2f, verts[i].x), Mathf.InverseLerp(-height / 2f, height / 2f, verts[i].y));
        mesh.uv = uv;
        mesh.name = panel.name + "_UV";
        mf.sharedMesh = mesh;
    }

    /// <summary>
    /// 가게의 재생용 테이프. 곡 음원은 Resources/Music/{musicId}-{번호}(VARCO Sound로 만든 곡, mp3 그대로 스트리밍).
    /// 한 곡 길이 = min(음원 길이, 2분): 한 곡이 끝날 때마다 되감기 1회가 쌓인다(음원이 없으면 40초)
    /// </summary>
    static CassetteTape MusicTape(string title, string musicId, float[] gains, params string[] tracks)
    {
        float length = 0f;
        for (int i = 0; i < tracks.Length; i++)
        {
            var clip = Resources.Load<AudioClip>($"Music/{musicId}-{i + 1}");
            if (clip) length = length <= 0f ? clip.length : Mathf.Min(length, clip.length);
        }
        // 곡이 3~4분이라 그대로 두면 충전이 너무 느리다: 한 곡은 최대 2분(끝에서 소리를 줄여 끊는다)
        return new CassetteTape { title = title, musicId = musicId, trackTitles = tracks, trackGains = gains, trackLength = length > 1f ? Mathf.Min(120f, Mathf.Floor(length * 10f) / 10f) : 40f };
    }

    /// <summary>판의 앞면 UV를 0~1로 맞춘다(간판 그림 한 장이 판 전체를 덮게)</summary>
    static void FitUV(GameObject panel, float width, float height)
    {
        var mf = panel.GetComponent<MeshFilter>();
        var mesh = Object.Instantiate(mf.sharedMesh);
        var verts = mesh.vertices;
        var normals = mesh.normals;
        var uv = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            // -x를 보는 면(거리 쪽)만 의미가 있다: z → u(남→북이 왼→오), y → v
            float u = Mathf.InverseLerp(width / 2f, -width / 2f, verts[i].z);
            if (normals[i].x > 0.5f) u = 1f - u;
            uv[i] = new Vector2(u, Mathf.InverseLerp(-height / 2f, height / 2f, verts[i].y));
        }
        mesh.uv = uv;
        mesh.name = panel.name + "_UV";
        mf.sharedMesh = mesh;
    }

    // =====================================================================
    // 조직원
    // =====================================================================

    static void BuildThugs(Transform people, Transform routes, Transform triggers, Built b)
    {
        // 화장실 뒤의 조직원: 처음에는 건물 뒤에 서 있다(감독이 붙잡아 둔다). 되감은 뒤에는 모퉁이를 오가며 순찰
        b.spotter = Patrol(people, routes, "Thug_Spotter", new Vector3(112f, G, 41.4f), 10f,
            new Vector3(112f, G, 41.4f), new Vector3(108.4f, G, 41.6f), new Vector3(106.6f, G, 40.3f));
        // 관리 도로의 검은 차 옆: 등을 돌리고 통화한다. 통화를 마치면 정자 쪽으로 걸어간다
        b.caller = Patrol(people, routes, "Thug_Caller", new Vector3(96.4f, G, LaneZ + 2.2f), -90f,
            new Vector3(90f, G, LaneZ + 1.6f), new Vector3(86f, G, 54f), new Vector3(90f, G, LaneZ + 1.6f), new Vector3(97f, G, LaneZ + 2.2f));
        // 가운데 잔디밭을 도는 순찰: 산책길(x 106.2)을 옆에서 본다
        Patrol(people, routes, "Thug_Lawn", new Vector3(113f, G, 48f), 90f,
            new Vector3(113f, G, 47.6f), new Vector3(128f, G, 47.6f), new Vector3(128f, G, 54.4f), new Vector3(113f, G, 54.4f));
        // 관리 도로를 오가는 순찰: 출구로 가는 지름길(관리 도로)에 서 있다 — 숨어서 때를 보거나, 등 뒤에서 제압하거나,
        // 잔디밭 길로 돌아가거나(거기엔 잔디밭 순찰). 그냥 뛰어 지나가면 들킨다
        Patrol(people, routes, "Thug_Lane", new Vector3(134f, G, LaneZ - 0.4f), -90f,
            new Vector3(134f, G, LaneZ - 0.4f), new Vector3(121f, G, LaneZ - 0.4f)); // 서쪽 끝은 통화 엿듣는 자리(x 103~106)에서 15m 넘게(시야 11m)
        // 정자 둘레
        Patrol(people, routes, "Thug_Pavilion", new Vector3(78f, G, 52f), 0f,
            new Vector3(78f, G, 52f), new Vector3(78f, G, 68f), new Vector3(90f, G, 68f), new Vector3(90f, G, 52f));

        // 공원 출구를 지키는 세 명: 둘은 출구 앞을 손전등으로 짧게 오가며 지키고(등 뒤로 몰래 제압할 틈이 생긴다), 총잡이는 출구 옆에서 망을 본다
        var e1 = Patrol(people, routes, "Thug_Exit_A", new Vector3(142.8f, G, 56.6f), -90f,
            new Vector3(142.8f, G, 56.6f), new Vector3(137.2f, G, 55.4f)).GetComponent<EnemyAI>();
        var e2 = Patrol(people, routes, "Thug_Exit_B", new Vector3(143.0f, G, 63.6f), -100f,
            new Vector3(143.0f, G, 63.6f), new Vector3(137.4f, G, 65.0f)).GetComponent<EnemyAI>();
        var gun = Fighter(people, "Thug_Exit_Gunner", new Vector3(147.8f, G, 64.6f), -120f, true);
        b.exitGunner = gun;
        var zoneGo = new GameObject("ExitZone");
        zoneGo.transform.SetParent(triggers);
        // 구역은 출구 틈(철망이 끊긴 곳)만: 경비 둘은 그 앞 공원에서 순찰하므로 등 뒤로 몰래 다가갈 틈이 실제로 생긴다
        zoneGo.transform.position = new Vector3(ParkX1 - 1.25f, 1.5f, LaneZ);
        var zc = zoneGo.AddComponent<BoxCollider>();
        zc.size = new Vector3(7.5f, 3f, 12f);
        zc.isTrigger = true;
        var zone = zoneGo.AddComponent<CombatZone>();
        zone.enemies = new[] { e1, e2, gun };
        zone.saveOnEnter = false; // 1장: 저장은 가게와 구역 시작점에서만
        b.exitZone = zone;
    }

    static void SetCombat(EnemyAI ai, bool gunner)
    {
        // 1장 조직원: 프롤로그 대니 집(연습 수준)보다 조금 세다
        ai.displayName = "조직원";
        ai.damage = 8;
        ai.attackRange = 1.5f;
        ai.windup = 0.62f;
        ai.recover = 0.6f;
        ai.attackCooldown = 1.5f;
        ai.heavyEvery = gunner ? 0 : 3; // 세 번째 주먹마다 붉게 오래 예고하는 강타: 피할 이유
        ai.heavyDamage = 20;
        ai.heavyWindup = 0.95f;
        if (gunner)
        {
            ai.ranged = true;
            ai.shootRange = 12f;
            ai.aimTime = 1.3f;
            ai.shotDamage = 18;
            ai.shotCooldown = 2.6f;
            if (ai.aimIK && ai.aimIK.heldItem) ai.aimIK.heldItem.gameObject.SetActive(true);
        }
    }

    static EnemyAI Fighter(Transform parent, string name, Vector3 pos, float yaw, bool gunner)
    {
        var ai = P.CreateThug(parent, name, pos, yaw);
        SetCombat(ai, gunner);
        return ai;
    }

    static PatrolSight Patrol(Transform parent, Transform routes, string name, Vector3 pos, float yaw, params Vector3[] route) =>
        PatrolAs("Enforcer", parent, routes, name, pos, yaw, route);

    /// <summary>손전등 순찰. character = 바로크 캐릭터 폴더 이름(조직원 Enforcer, 진압 경찰 RiotCop)</summary>
    static PatrolSight PatrolAs(string character, Transform parent, Transform routes, string name, Vector3 pos, float yaw, Vector3[] route)
    {
        var ai = P.CreateThug(parent, name, pos, yaw, character);
        SetCombat(ai, false);
        var ik = ai.aimIK;
        // 총 대신 손전등을 든다
        if (ik.heldItem) Object.DestroyImmediate(ik.heldItem.gameObject);
        var torch = new GameObject("Flashlight").transform;
        torch.SetParent(ai.body.transform, false);
        var model = TryProp("Flashlight", torch, Vector3.zero, 0f);
        if (model)
        {
            // 바로크 손전등(바닥 가운데가 기준점): 몸통 축이 손에 오게 반 높이만큼 내리고, 렌즈(+z 끝)가 z 0.12에 오게
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            torch.rotation = Quaternion.identity; // 재는 동안만 세계 축과 맞춘다(순찰이 비스듬히 서 있으면 경계 상자가 틀어진다)
            var wb = WorldBounds(model);
            Vector3 c = wb.center - torch.position;
            model.transform.localPosition = new Vector3(-c.x, -c.y, 0.12f - (wb.max.z - torch.position.z));
            torch.localRotation = Quaternion.identity;
            foreach (var t in model.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
        else
        {
            var bodyBox = P.Box(torch, "Body", Vector3.zero, new Vector3(0.045f, 0.045f, 0.22f), Mat("Galvanized"), Vector2.one, collider: false);
            bodyBox.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            bodyBox.transform.localRotation = Quaternion.identity;
            GameObjectUtility.SetStaticEditorFlags(bodyBox, 0);
        }
        var lens = LensDisc(torch); // 둥근 렌즈(네모 상자는 손전등 앞에 하얀 네모가 따로 떠 "등이 둘"처럼 보였다)
        GameObjectUtility.SetStaticEditorFlags(lens, 0);
        var light = P.AddLight(torch, "Beam", new Vector3(0f, 0f, 0.15f), LightType.Spot, new Color(1f, 0.95f, 0.84f), 26f, 14f, 48f, Quaternion.identity, local: true);
        light.shadows = LightShadows.Soft;
        // 빗속에 보이는 빛줄기(가짜 볼륨 원뿔): 어두운 실루엣이라도 어느 쪽을 비추는지 한눈에 읽히게
        var cone = new GameObject("BeamCone");
        cone.transform.SetParent(torch, false);
        cone.transform.localPosition = new Vector3(0f, 0f, 0.14f);
        cone.AddComponent<MeshFilter>().sharedMesh = BeamConeMesh();
        var cr = cone.AddComponent<MeshRenderer>();
        cr.sharedMaterial = BeamMaterial();
        cr.shadowCastingMode = ShadowCastingMode.Off;
        cr.receiveShadows = false;
        ik.heldItem = torch;
        ik.heldOffset = new Vector3(0f, 0.02f, 0.07f); // 손전등 몸통이 주먹 안에 쥐어지고 머리만 앞으로 나오게(총 자리 기본값은 손등 위에 떠 보였다)

        var sight = ai.gameObject.AddComponent<PatrolSight>();
        sight.flashlight = light;
        var target = new GameObject("BeamTarget").transform;
        target.SetParent(ai.transform, false);
        target.localPosition = new Vector3(0f, 0.3f, 6f);
        sight.beamTarget = target;
        var points = new Transform[route.Length];
        var folder = new GameObject(name + "_Route").transform;
        folder.SetParent(routes);
        for (int i = 0; i < route.Length; i++) points[i] = P.Marker(folder, $"P{i}", route[i], 0f);
        sight.route = points;
        HoldPoseSetup.Equip(ai); // 손전등을 쥔 자세(블렌더 자세 레이어) — 싸우면 바닥에 떨어뜨린다
        return sight;
    }

    static Mesh beamCone;

    /// <summary>손전등 렌즈: 지름 4.4cm 납작한 원반(+z를 본다). 손전등 머리 안쪽에 살짝 들어가게</summary>
    public static GameObject LensDisc(Transform torch)
    {
        var old = torch.Find("Lens");
        if (old) Object.DestroyImmediate(old.gameObject);
        var lens = new GameObject("Lens");
        lens.transform.SetParent(torch, false);
        lens.transform.localPosition = new Vector3(0f, 0f, 0.121f);
        lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 원기둥 축(y)을 손전등 축(z)으로
        lens.transform.localScale = new Vector3(0.044f, 0.002f, 0.044f);
        lens.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var r = lens.AddComponent<MeshRenderer>();
        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Prologue/Materials/FlashLens.mat");
        r.shadowCastingMode = ShadowCastingMode.Off;
        return lens;
    }

    /// <summary>손전등 빛줄기 원뿔: 꼭짓점이 렌즈, 길이 7m, 끝 반지름은 손전등 빛 각도(48°)에 맞춘다. v는 렌즈(0)→끝(1)</summary>
    static Mesh BeamConeMesh()
    {
        if (beamCone) return beamCone;
        const int seg = 18;
        const float length = 7f;
        float r1 = Mathf.Tan(24f * Mathf.Deg2Rad) * length * 0.8f, r0 = 0.03f;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            verts.Add(dir * r0); uvs.Add(new Vector2(i / (float)seg, 0f));
            verts.Add(dir * r1 + Vector3.forward * length); uvs.Add(new Vector2(i / (float)seg, 1f));
        }
        for (int i = 0; i < seg; i++)
        {
            int k = i * 2;
            tris.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
        }
        beamCone = new Mesh { name = "FlashlightBeamCone" };
        beamCone.SetVertices(verts);
        beamCone.SetUVs(0, uvs);
        beamCone.SetTriangles(tris, 0);
        beamCone.RecalculateNormals();
        beamCone.RecalculateBounds();
        return beamCone;
    }

    /// <summary>빛줄기 재질: 더하기 혼합(어두운 곳에서만 은은하게 보인다), 렌즈 쪽이 진하고 끝으로 갈수록 사라진다</summary>
    static Material BeamMaterial()
    {
        string path = $"{P.ArtRoot}/Materials/FlashlightBeam.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        var px = new Color32[4 * 128];
        for (int y = 0; y < 128; y++)
        {
            float v = y / 127f;
            byte a = (byte)(255f * Mathf.Pow(1f - v, 1.6f) * Mathf.SmoothStep(0f, 1f, v * 8f));
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, a);
        }
        var tex = P.SaveTexture("FlashlightBeam", 4, 128, px, true);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex)));
        mat.SetColor("_BaseColor", new Color(1f, 0.93f, 0.78f, 0.09f)); // 0.16 이상은 바닥과 만나는 선이 딱딱한 쐐기처럼 보였다
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f); // 더하기
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f); // 안쪽에서도 보인다
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>바로크 소품 프리팹이 있으면 놓고, 아직 없으면 null(만들어지기 전에도 장면을 지을 수 있게)</summary>
    static GameObject TryProp(string name, Transform parent, Vector3 position, float yaw)
    {
        if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{P.PropPrefabs}/{name}.prefab")) return null;
        return P.Prop(name, parent, position, yaw);
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material X_FirstAid() => P.Lit("FirstAidWhite", null, new Color(0.95f, 0.95f, 0.93f), 0.4f, emission: new Color(1f, 1f, 1f) * 0.25f);

    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{P.ArtRoot}/Materials/{name}.mat");

    static StoryTrigger Trigger(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = centre;
        var c = go.AddComponent<BoxCollider>();
        c.size = size;
        c.isTrigger = true;
        return go.AddComponent<StoryTrigger>();
    }
}
