using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 트레일러 만들기(2026-10-16 "기깔나게 영화 같은 트레일러"): 플레이 중에 장면을 차례로 불러 영화식 카메라 움직임으로 찍는다.
/// 2.39:1 검은 띠 · 자막(Noto Sans KR) · 검은 화면 전환 · 타이틀 곡 + 효과음을 섞어 Unity 내장 인코더(MediaEncoder)로 MP4.
/// 결과: QA/trailer/REWIND_trailer.mp4 (미리보기 모드는 컷마다 사진 한 장: QA/trailer/preview_*.png)
/// </summary>
public static class TrailerMaker
{
    const int W = 1920, H = 1080, Fps = 30, Rate = 48000;
    const string Out = @"C:\Users\MBC-501-12\OneDrive\Desktop\0930Project\QA\trailer";

    public class Shot
    {
        public string scene, caption, sub;
        public float seconds = 3f, fov = 40f, fadeIn, fadeOut, captionAt = 0.4f, rewind;
        public Vector3 from, to, lookFrom, lookTo;
        public Action setup;                 // 장면을 불러 둔 뒤 한 번(사람 배치 · 깨우기)
        public Action<float> each;           // 매 프레임(0~1)
        public bool black, logo;             // 검은 화면(자막만) · 마지막 로고
        public string sfx; public float sfxAt; public float sfxGain = 1f;
    }

    static List<Shot> shots;
    static int index, frame, stage;
    static float settleUntil;
    static Camera cam;
    static RenderTexture rt;
    static Texture2D frameTex;
    static MediaEncoder encoder;
    static float[] audio; static int audioCursor;
    static Text caption, sub; static Image fade, logoImg; static CanvasGroup capGroup;
    static bool preview;
    static string musicName = "title-2"; static float musicStart = 13f; // 32초쯤 잠잠했다 34초부터 커진다 → 몽타주 시작(20.7초)에 맞춘다
    public static string Status = "";

    // ---------------- 컷 구성 ----------------

    static List<Shot> Build()
    {
        Vector3 V(float x, float y, float z) => new(x, y, z);
        var list = new List<Shot>
        {
            // 1. 밤거리 — 대니의 집으로 천천히
            new Shot { scene = "Prologue", seconds = 5.5f, fadeIn = 1.2f, from = V(96, 1.6f, -9), to = V(101, 1.7f, 1), lookFrom = V(103, 2.6f, 12), lookTo = V(103, 2.2f, 12), fov = 38,
                       caption = "그날 밤,", sub = "친구가 죽었다.", captionAt = 1.4f },
            // 2. 대니의 방 — 쓰러진 친구와 조직원
            new Shot { scene = "Prologue", seconds = 4.2f, from = V(96.2f, 0.9f, 67.5f), to = V(97.4f, 0.8f, 68.4f), lookFrom = V(101, 0.4f, 70.5f), lookTo = V(101.5f, 0.6f, 70.8f), fov = 34,
                       setup = () => Show("Danny_Body", "Thug_Room_A", "Thug_Room_B") },
            // 3. 레이 — 얼굴 가까이(찌푸림)
            new Shot { scene = "Chapter1", seconds = 4.5f, fadeIn = 0.4f, fov = 22,
                       setup = () => Face(0.8f, new Vector3(0.35f, 0.04f, 0.95f), new Vector3(0.12f, 0.02f, 0.72f)),
                       caption = "남은 건", sub = "카세트 플레이어 하나.", captionAt = 1.0f },
            // 4. 되감기 — 대니의 집 앞에서 세상이 거꾸로(뒤로 빠지는 카메라)
            new Shot { scene = "Prologue", seconds = 4.0f, rewind = 1f, from = V(100.6f, 1.75f, 2.2f), to = V(99.5f, 2.2f, -2.5f), lookFrom = V(103, 1.5f, 11), lookTo = V(103, 1.6f, 11), fov = 42,
                       setup = () => Place(V(101.5f, 0.02f, 4f), V(103, 0, 12)),
                       caption = "시간을 되감는다.", captionAt = 0.8f, sfx = "World/TapeRewind", sfxAt = 0f },
            // 5. 검은 화면
            new Shot { black = true, seconds = 2.2f, caption = "단,", sub = "5초씩.", captionAt = 0.2f, sfx = "World/TapeStop", sfxAt = 0.05f },
            // 6. 1장 야시장 — 경찰 바리케이드
            new Shot { scene = "Chapter1", seconds = 3.0f, from = V(164, 1.2f, 136.5f), to = V(164, 1.3f, 139.5f), lookFrom = V(164, 2.4f, 151), lookTo = V(164, 2.5f, 151), fov = 38, // 간판이 위 검은 띠에 잘리지 않게
                       sfx = "Hit/impact_mid-1", sfxAt = 0f, sfxGain = 0.6f },
            // 7. 1장 부두 — 반장과 부하들에게 둘러싸인 레이
            new Shot { scene = "Chapter1", seconds = 3.6f, from = V(195.2f, 1.6f, 322.2f), to = V(195.9f, 1.6f, 323.3f), lookFrom = V(198.2f, 1.55f, 334), lookTo = V(198.3f, 1.5f, 334), fov = 32,
                       setup = () =>
                       {
                           Place(V(197.2f, 0.02f, 326.0f), V(197.2f, 0, 334));
                           Pose("Boss_Cleaner", V(197.2f, 0.02f, 327.6f), V(197.2f, 0, 320));
                           Pose("Thug_Pier_A", V(196.1f, 0.02f, 327.2f), V(197.2f, 0, 325.5f));
                           Pose("Thug_Pier_B", V(198.4f, 0.02f, 327.3f), V(197.2f, 0, 325.5f));
                       },
                       sfx = "Hit/impact_heavy-1", sfxAt = 3.0f },
            // 8. 2장 — 경찰서 주차장의 탐조등
            new Shot { scene = "Chapter2", seconds = 3.4f, from = V(30, 3.5f, 20), to = V(38, 4.3f, 23), lookFrom = V(78, 8, 50), lookTo = V(74, 5, 46), fov = 42,
                       caption = "경찰은 이미", sub = "그들의 편이었다.", captionAt = 0.5f },
            // 9. 3장 — 형광등 복도, 문틈의 실험체
            new Shot { scene = "Chapter3", seconds = 3.2f, from = V(308, 1.6f, 15), to = V(312, 1.6f, 15), lookFrom = V(330, 1.4f, 15), lookTo = V(330, 1.4f, 15), fov = 50,
                       setup = () => { Wake("Subject_A", "Subject_B", "Subject_C"); Lights("KeyLight"); } },
            // 10. 4장 — 정상의 파트너
            new Shot { scene = "Chapter4", seconds = 3.0f, from = V(-5.5f, 21.7f, 44.5f), to = V(-4.6f, 21.6f, 46), lookFrom = V(-1, 21.5f, 51), lookTo = V(-1, 21.6f, 51), fov = 34 },
            // 11. 4장 — 송신탑 올려다보기 → 저격
            new Shot { scene = "Chapter4", seconds = 2.8f, from = V(4, 21.5f, 38), to = V(4.5f, 21.3f, 39), lookFrom = V(16, 30, 59), lookTo = V(16, 52, 59), fov = 52,
                       sfx = "World/SniperShot", sfxAt = 2.55f },
            // 12. 5장 — 0번과 유리 관람석
            new Shot { scene = "Chapter5", seconds = 4.0f, from = V(420, 1.6f, 1), to = V(420, 1.5f, 6), lookFrom = V(420, 2.2f, 14), lookTo = V(420, 3.4f, 22), fov = 38,
                       caption = "윗선이 지켜보는 가운데", sub = "모든 걸 되돌릴 테이프가 있다.", captionAt = 0.6f },
            // 13. 레이 — 결심
            new Shot { scene = "Chapter1", seconds = 4.2f, fov = 22, fadeOut = 0.6f,
                       setup = () => Face(1f, new Vector3(-0.28f, 0f, 1.0f), new Vector3(-0.08f, 0.01f, 0.85f)),
                       caption = "죽어도 되감으면 된다.", sub = "— 단, 테이프가 남아 있다면.", captionAt = 0.5f },
            // 14. 로고
            new Shot { black = true, logo = true, seconds = 5.0f, sfx = "World/TapeRewind", sfxAt = 0.0f, sfxGain = 0.6f, sub = "카세트 누아르 액션", captionAt = 1.6f },
        };
        return list;
    }

    // ---------------- 시작 ----------------

    public static void Make(bool previewOnly)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 중에만");
        preview = previewOnly;
        Directory.CreateDirectory(Out);
        shots = Build();
        index = -1; stage = -1;
        Status = "소리 불러오는 중";
        SetupCamera();
        Preload(); settleUntil = Time.realtimeSinceStartup + 30f;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    const int LogoLayer = 31, OverlayLayer = 30;
    static Camera uiCam;

    static RenderTexture uiRt; static Texture2D uiTex;
    static Color32[] scenePx, uiPx;

    /// <summary>장면 카메라(후처리 · 로고 빛 번짐)와 덧그림 카메라(검은 띠 · 자막 · 전환, 후처리 없음)를 따로 그려 CPU에서 겹친다.
    /// 한 카메라에 다 넣으면 되감기 효과가 자막과 검은 띠까지 휘게 했고, 덧그림 카메라를 같은 화면에 '지우지 않고' 그리면 URP가 바탕을 파랗게 덮었다</summary>
    static void SetupCamera()
    {
        rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        frameTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var go = new GameObject("TrailerCamera"); UnityEngine.Object.DontDestroyOnLoad(go);
        cam = go.AddComponent<Camera>(); cam.enabled = false; cam.nearClipPlane = 0.03f; cam.farClipPlane = 600f; cam.allowHDR = true; cam.targetTexture = rt;
        var add = go.AddComponent<UniversalAdditionalCameraData>(); add.renderPostProcessing = true; add.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var ugo = new GameObject("TrailerOverlayCamera"); UnityEngine.Object.DontDestroyOnLoad(ugo); ugo.transform.position = new Vector3(0, -2000, 0);
        uiCam = ugo.AddComponent<Camera>(); uiCam.enabled = false; uiCam.nearClipPlane = 0.01f; uiCam.farClipPlane = 10f; uiCam.allowHDR = false; uiCam.allowMSAA = false;
        uiRt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); uiTex = new Texture2D(W, H, TextureFormat.RGBA32, false); // 깊이 버퍼가 없으면 URP가 아무것도 그리지 않았다
        uiCam.clearFlags = CameraClearFlags.SolidColor; uiCam.backgroundColor = new Color(0, 0, 0, 0); uiCam.cullingMask = 1 << OverlayLayer; uiCam.targetTexture = uiRt;
        var uadd = ugo.AddComponent<UniversalAdditionalCameraData>(); uadd.renderPostProcessing = false; uadd.antialiasing = AntialiasingMode.None;

        Canvas MakeCanvas(string n, Camera c, int layer)
        {
            var g = new GameObject(n); UnityEngine.Object.DontDestroyOnLoad(g); g.layer = layer;
            var cv = g.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = c; cv.planeDistance = 1f;
            var sc = g.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(W, H);
            return cv;
        }
        var over = MakeCanvas("TrailerCanvas", uiCam, OverlayLayer);
        var logoCv = MakeCanvas("TrailerLogoCanvas", cam, LogoLayer);
        GameObject Child(Transform parent, string n) { var g = new GameObject(n); g.transform.SetParent(parent, false); g.layer = parent.gameObject.layer; return g; }
        Image Rect01(string n, Vector2 min, Vector2 max, Color c) { var im = Child(over.transform, n).AddComponent<Image>(); im.color = c; var r = im.rectTransform; r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero; return im; }
        fade = Rect01("Fade", Vector2.zero, Vector2.one, new Color(0, 0, 0, 0));
        float bar = (1f - (W / 2.39f) / H) / 2f; // 2.39:1
        Rect01("BarTop", new Vector2(0, 1 - bar), Vector2.one, Color.black);
        Rect01("BarBottom", Vector2.zero, new Vector2(1, bar), Color.black);
        var cap = Child(over.transform, "Captions");
        capGroup = cap.AddComponent<CanvasGroup>();
        var cr = cap.AddComponent<RectTransform>(); cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
        Text T(string n, float y, int size, Color c, FontStyle st)
        {
            var g = Child(cap.transform, n); var t = g.AddComponent<Text>();
            t.font = UiTheme.Font ? UiTheme.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = size; t.color = c; t.fontStyle = st; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; var r = t.rectTransform; r.anchorMin = new Vector2(0, y); r.anchorMax = new Vector2(1, y); r.sizeDelta = new Vector2(0, size * 1.6f);
            var sh = g.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }
        caption = T("Caption", 0.235f, 34, new Color(0.93f, 0.90f, 0.84f), FontStyle.Normal);
        sub = T("Sub", 0.185f, 46, new Color(0.96f, 0.72f, 0.30f), FontStyle.Bold);
        // 로고: 장면 카메라 쪽(빛 번짐을 받는다)
        logoImg = Child(logoCv.transform, "Logo").AddComponent<Image>(); logoImg.preserveAspect = true;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Title/TitleLogo.png");
        if (tex) logoImg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        var lr = logoImg.rectTransform; lr.anchorMin = new Vector2(0.25f, 0.42f); lr.anchorMax = new Vector2(0.75f, 0.68f); lr.offsetMin = lr.offsetMax = Vector2.zero;
        logoImg.color = new Color(1, 1, 1, 0);
    }

    static void NextShot()
    {
        index++;
        if (index >= shots.Count) { Finish(); return; }
        var s = shots[index];
        Status = $"컷 {index + 1}/{shots.Count}";
        frame = 0;
        if (s.black) { stage = 2; return; }
        if (SceneManager.GetActiveScene().name != s.scene) { SceneManager.LoadScene(s.scene); stage = 1; settleUntil = Time.realtimeSinceStartup + 4.5f; }
        else { stage = 1; settleUntil = Time.realtimeSinceStartup + 0.6f; }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Abort("플레이가 멈춤"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (stage == -1)
        {
            if (!AudioReady && Time.realtimeSinceStartup < settleUntil) return;
            if (!preview) BeginEncoder();
            NextShot();
            return;
        }
        var s = shots[index];
        if (stage == 1)
        {
            if (Time.realtimeSinceStartup < settleUntil) return;
            Calm();
            s.setup?.Invoke();
            Time.captureFramerate = Fps;
            stage = 2; lastFrame = -1;
            return;
        }
        if (stage != 2) return;
        if (Time.frameCount == lastFrame) return; // 게임 한 프레임에 한 장
        lastFrame = Time.frameCount;
        int total = Mathf.RoundToInt(s.seconds * Fps);
        float t = frame / (float)Mathf.Max(1, total - 1);
        Render(s, t, frame / (float)Fps);
        frame++;
        if (frame == Mathf.RoundToInt(total * 0.6f)) // 컷마다 한 장 남긴다(확인용). 미리보기면 거기서 다음 컷으로
        {
            File.WriteAllBytes(Path.Combine(Out, $"preview_{index + 1:00}.jpg"), frameTex.EncodeToJPG(85));
            if (preview) frame = total;
        }
        if (frame >= total) { Time.captureFramerate = 0; stage = 0; NextShot(); }
    }
    static int lastFrame;

    static void Render(Shot s, float t, float sec)
    {
        float e = Mathf.SmoothStep(0f, 1f, t);
        // 화면 전환: 들어올 때 · 나갈 때 검게
        float a = 0f;
        if (s.fadeIn > 0f) a = Mathf.Max(a, 1f - Mathf.Clamp01(sec / s.fadeIn));
        if (s.fadeOut > 0f) a = Mathf.Max(a, Mathf.Clamp01((sec - (s.seconds - s.fadeOut)) / s.fadeOut));
        fade.color = new Color(0, 0, 0, a);
        // 자막
        caption.text = s.caption ?? ""; sub.text = s.sub ?? "";
        float ca = Mathf.Clamp01((sec - s.captionAt) / 0.5f) * Mathf.Clamp01((s.seconds - 0.25f - sec) / 0.4f);
        capGroup.alpha = ca;
        if (s.logo)
        {
            float la = Mathf.Clamp01((sec - 0.4f) / 1.0f) * Mathf.Clamp01((s.seconds - sec) / 0.8f);
            logoImg.color = new Color(1, 1, 1, la);
            caption.text = ""; sub.fontSize = 30;
        }
        else { logoImg.color = new Color(1, 1, 1, 0); sub.fontSize = 46; }
        if (!s.black)
        {
            cam.transform.position = Vector3.Lerp(s.from, s.to, e);
            cam.transform.LookAt(Vector3.Lerp(s.lookFrom, s.lookTo, e));
            cam.fieldOfView = s.fov;
        }
        // 되감기 화면 효과
        var fx = UnityEngine.Object.FindAnyObjectByType<RewindEffect>();
        if (fx) fx.SetWeight(s.rewind > 0f ? s.rewind * (0.75f + 0.25f * Mathf.Sin(sec * 9f)) : 0f);
        s.each?.Invoke(t);
        cam.clearFlags = s.black ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox; cam.backgroundColor = Color.black;
        cam.cullingMask = s.black ? 1 << LogoLayer : ~((1 << 5) | (1 << OverlayLayer) | (1 << LogoLayer));
        cam.Render();
        RenderTexture.active = rt; frameTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        uiCam.Render();
        RenderTexture.active = uiRt; uiTex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
        // 덧그림은 투명 바탕 위에 그려져 색이 이미 알파를 곱한 값이다 → 결과 = 덧그림 + 장면 × (1 - 알파)
        scenePx = frameTex.GetPixels32(); uiPx = uiTex.GetPixels32();
        for (int i = 0; i < scenePx.Length; i++)
        {
            var u = uiPx[i]; if (u.a == 0) continue;
            int k = 255 - u.a; var c = scenePx[i];
            scenePx[i] = new Color32((byte)Mathf.Min(255, u.r + c.r * k / 255), (byte)Mathf.Min(255, u.g + c.g * k / 255), (byte)Mathf.Min(255, u.b + c.b * k / 255), 255);
        }
        frameTex.SetPixels32(scenePx);
        if (!preview) { encoder.AddFrame(frameTex); PushAudio(); }
    }

    // ---------------- 장면 손질 ----------------

    /// <summary>게임 쪽 연출이 끼어들지 않게: 대사 · 목표 · 화면 덮개 지우고, 레이는 다치지 않게</summary>
    static void Calm()
    {
        StoryState.ClearLine(); StoryState.Title = null; StoryState.Fade = 0f; StoryState.Prompt = null;
        var p = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (p) p.GetComponent<Health>().Invulnerable = true;
        foreach (var ps in PatrolSight.All) ps.CanDetect = false;
    }

    static void Show(params string[] names)
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (names.Contains(t.name)) t.gameObject.SetActive(true);
    }

    static void Wake(params string[] names) => Wake(names, false);
    static void Wake(string name, bool fight) => Wake(new[] { name }, fight);
    static void Wake(string[] names, bool fight)
    {
        foreach (var e in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!names.Contains(e.name)) continue;
            e.gameObject.SetActive(true);
            var ps = e.GetComponent<PatrolSight>(); if (ps) { ps.Holding = false; ps.Staged = false; }
            if (fight) e.Activate();
        }
    }

    static void Place(Vector3 at, Vector3 lookAt)
    {
        var p = UnityEngine.Object.FindAnyObjectByType<PlayerController>(); if (!p) return;
        p.Teleport(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(lookAt - at, Vector3.up)));
    }

    /// <summary>적을 그 자리에 세워 둔다(AI를 꺼서 움직이지 않게)</summary>
    static void Pose(string name, Vector3 at, Vector3 lookAt)
    {
        var e = UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == name); if (!e) return;
        e.gameObject.SetActive(true);
        var ps = e.GetComponent<PatrolSight>(); if (ps) ps.enabled = false;
        e.enabled = false;
        var ag = e.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag) ag.enabled = false;
        e.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(lookAt - at, Vector3.up)));
    }

    static void Lights(string name)
    {
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (l.name == name) l.enabled = true;
    }

    /// <summary>레이 얼굴: 찌푸린 표정(표정 컴포넌트를 끄고 직접 맞춘다). 카메라 자리는 머리 기준(옆, 위, 앞 m)</summary>
    static void Face(float browDown, Vector3 from, Vector3 to)
    {
        var p = UnityEngine.Object.FindAnyObjectByType<PlayerController>(); if (!p) return;
        var fe = p.GetComponentInChildren<FaceExpression>(); if (fe) fe.enabled = false;
        var smr = p.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("Blink_L") >= 0);
        if (smr)
        {
            void Set(string n, float v) { int i = smr.sharedMesh.GetBlendShapeIndex(n); if (i >= 0) smr.SetBlendShapeWeight(i, v * 100f); }
            Set("BrowDown", browDown); Set("MouthPress", 0.6f); Set("Squint", 0.3f);
        }
        var head = p.animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 c = head.position + Vector3.up * 0.06f, f = p.transform.forward, rr = p.transform.right;
        Vector3 W(Vector3 o) => c + rr * o.x + Vector3.up * o.y + f * o.z;
        var s = shots[index];
        s.from = W(from); s.to = W(to); s.lookFrom = c + Vector3.up * 0.01f; s.lookTo = c;
    }

    // ---------------- 소리 ----------------

    static void BeginEncoder()
    {
        string path = Path.Combine(Out, "REWIND_trailer.mp4");
        if (File.Exists(path)) File.Delete(path);
        var v = new VideoTrackAttributes { frameRate = new MediaRational(Fps), width = W, height = H, includeAlpha = false, bitRateMode = VideoBitrateMode.High };
        var au = new AudioTrackAttributes { sampleRate = new MediaRational(Rate), channelCount = 2, language = "ko" };
        encoder = new MediaEncoder(path, v, au);
        audio = MixAudio(); audioCursor = 0;
    }

    static void PushAudio()
    {
        int n = Rate / Fps * 2;
        using var buf = new NativeArray<float>(n, Allocator.Temp);
        var arr = new float[n];
        for (int i = 0; i < n; i++) arr[i] = audioCursor + i < audio.Length ? audio[audioCursor + i] : 0f;
        audioCursor += n;
        buf.CopyFrom(arr);
        encoder.AddSamples(buf);
    }

    /// <summary>음악(타이틀 곡 한 구간) + 컷마다 효과음. 48kHz 스테레오</summary>
    static float[] MixAudio()
    {
        float total = shots.Sum(s => s.seconds);
        int len = Mathf.CeilToInt(total * Rate) * 2;
        var mix = new float[len];
        var music = Load($"Music/Title/{musicName}");
        if (music != null)
        {
            int start = Mathf.RoundToInt(musicStart * Rate) * 2;
            for (int i = 0; i < len && start + i < music.Length; i++)
            {
                float tsec = i / 2f / Rate;
                float env = Mathf.Clamp01(tsec / 2.5f) * Mathf.Clamp01((total - tsec) / 3.5f);
                mix[i] += music[start + i] * 0.8f * env;
            }
        }
        float at = 0f;
        foreach (var s in shots)
        {
            if (!string.IsNullOrEmpty(s.sfx))
            {
                var fx = Load("Sfx/" + s.sfx);
                if (fx != null)
                {
                    int o = Mathf.RoundToInt((at + s.sfxAt) * Rate) * 2;
                    for (int i = 0; i < fx.Length && o + i < len; i++) mix[o + i] += fx[i] * 0.9f * s.sfxGain;
                }
            }
            at += s.seconds;
        }
        float peak = mix.Max(Mathf.Abs);
        if (peak > 0.98f) for (int i = 0; i < len; i++) mix[i] *= 0.98f / peak;
        return mix;
    }

    /// <summary>클립을 48kHz 스테레오 float로. 플레이 중에만(편집 모드에서는 음악 불러오기가 끝나지 않는다).
    /// 음악은 미리 '불러올 때 풀기 · PCM'으로 바꿔 두고, 끝나면 .meta를 git으로 되돌린다</summary>
    static float[] Load(string resource)
    {
        var clip = Resources.Load<AudioClip>(resource);
        if (!clip) { Debug.LogWarning("[트레일러] 소리 없음 " + resource); return null; }
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) { Debug.LogWarning("[트레일러] 소리 못 읽음 " + resource + " " + clip.loadState); return null; }
        int ch = clip.channels, freq = clip.frequency;
        int outLen = Mathf.FloorToInt(clip.samples * (Rate / (float)freq));
        var res = new float[outLen * 2];
        for (int i = 0; i < outLen; i++)
        {
            float src = i * (freq / (float)Rate); int i0 = Mathf.Min((int)src, clip.samples - 1), i1 = Mathf.Min(i0 + 1, clip.samples - 1); float f = src - i0;
            for (int c = 0; c < 2; c++)
            {
                int sc = Mathf.Min(c, ch - 1);
                res[i * 2 + c] = Mathf.Lerp(data[i0 * ch + sc], data[i1 * ch + sc], f);
            }
        }
        return res;
    }

    static List<AudioClip> pending;
    /// <summary>쓸 소리를 모두 불러오기 시작한다</summary>
    static void Preload()
    {
        pending = new List<AudioClip>();
        foreach (var n in new[] { $"Music/Title/{musicName}" }.Concat(shots.Where(x => !string.IsNullOrEmpty(x.sfx)).Select(x => "Sfx/" + x.sfx)))
        {
            var c = Resources.Load<AudioClip>(n); if (!c) continue;
            c.LoadAudioData(); pending.Add(c);
        }
    }
    static bool AudioReady => pending.All(c => c.loadState != AudioDataLoadState.Loading && c.loadState != AudioDataLoadState.Unloaded);

    /// <summary>음악 고르기용: 곡의 초마다 소리 크기(0~1)</summary>
    public static string Envelope(string name)
    {
        var c = Resources.Load<AudioClip>("Music/Title/" + name); if (c.loadState != AudioDataLoadState.Loaded) { c.LoadAudioData(); return "불러오는 중 " + c.loadState; }
        var d = Load("Music/Title/" + name); if (d == null) return "없음";
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s * Rate * 2 < d.Length; s += 2)
        {
            double sum = 0; int n = 0;
            for (int i = s * Rate * 2; i < Math.Min(d.Length, (s + 2) * Rate * 2); i += 16) { sum += d[i] * d[i]; n++; }
            sb.Append(Math.Sqrt(sum / Math.Max(1, n)).ToString("0.00")).Append(' ');
        }
        return sb.ToString();
    }

    // ---------------- 끝 ----------------

    static void Finish()
    {
        EditorApplication.update -= Tick;
        Time.captureFramerate = 0;
        if (encoder != null) { encoder.Dispose(); encoder = null; }
        Cleanup();
        Status = preview ? "미리보기 끝" : "끝";
        Debug.Log("[트레일러] " + Status);
    }

    static void Abort(string why) { EditorApplication.update -= Tick; Time.captureFramerate = 0; if (encoder != null) { encoder.Dispose(); encoder = null; } Status = "중단: " + why; }

    static void Cleanup()
    {
        if (cam) UnityEngine.Object.Destroy(cam.gameObject);
        if (uiCam) UnityEngine.Object.Destroy(uiCam.gameObject);
        foreach (var n in new[] { "TrailerCanvas", "TrailerLogoCanvas" }) { var cv = GameObject.Find(n); if (cv) UnityEngine.Object.Destroy(cv); }
        if (rt) rt.Release();
        if (uiRt) uiRt.Release();
    }
}
