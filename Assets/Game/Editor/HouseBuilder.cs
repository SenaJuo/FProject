using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 서양식 주택을 반듯한 형태로 직접 짓는다(이미지→3D 생성 집은 창틀이 녹고 텍스처가 번져서 쓰지 않는다).
/// 집 로컬 좌표: 현관 면이 z = 0에 있고 -z(거리)를 본다. 몸체는 +z 쪽으로 depth만큼, 바닥은 y = 0.
/// 부품은 재질별로 메시 하나에 모으고, 충돌체는 몸체·포치 데크·계단처럼 필요한 것만 따로 둔다.
/// </summary>
public static class HouseBuilder
{
    public enum Style { Suburban, Bungalow, Townhouse }

    public class Spec
    {
        public Style style = Style.Suburban;
        public float width = 9f, depth = 9f;
        public int floors = 2;
        public Material wall;               // 사이딩·벽돌·스투코(색을 입힌 재질)
        public float wallTile = 1.4f;       // 벽 텍스처 한 장이 덮는 길이(m)
        public Material door;               // 문짝 재질(문 텍스처)
        public Material shutter;            // 덧창 색(null이면 덧창 없음)
        public bool porch = true;
        public bool chimney;
        public float doorOffset;            // 현관문 가로 위치(집 가운데 기준)
        public int seed;
        public float litChance = 0.35f;     // 불 켜진 창의 비율
    }

    public class Materials
    {
        public Material trim, foundation, roof, glassDark, glassLit, porchWood, metal, lamp, brass;
    }

    const float FloorHeight = 3.0f;

    /// <summary>집을 짓고 루트를 돌려준다. doorFront = 현관문 바로 앞(문밖 0.6m) 세계 좌표</summary>
    public static GameObject Build(Transform parent, string name, Vector3 position, float yaw, Spec s, Materials m, out Vector3 doorFront)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var mb = new MeshBuilder();
        var rng = new System.Random(s.seed);

        bool town = s.style == Style.Townhouse;
        float plinth = town ? 1.2f : s.style == Style.Bungalow ? 0.45f : 0.6f;
        float wallH = s.floors * FloorHeight;
        float top = plinth + wallH;
        float W = s.width, D = s.depth;

        // 기초·벽·모서리 몰딩·처마 몰딩
        mb.Box(m.foundation, new Vector3(0f, plinth / 2f, D / 2f), new Vector3(W + 0.08f, plinth, D + 0.08f), Quaternion.identity, 1.5f);
        mb.Box(s.wall, new Vector3(0f, plinth + wallH / 2f, D / 2f), new Vector3(W, wallH, D), Quaternion.identity, s.wallTile);
        foreach (float cx in new[] { -W / 2f, W / 2f })
        foreach (float cz in new[] { 0f, D })
            mb.Box(m.trim, new Vector3(cx, plinth + wallH / 2f, cz), new Vector3(0.16f, wallH, 0.16f), Quaternion.identity, 1f);
        mb.Box(m.trim, new Vector3(0f, top - 0.12f, -0.02f), new Vector3(W + 0.1f, 0.24f, 0.08f), Quaternion.identity, 1f);
        for (int f = 1; f < s.floors && town; f++) // 타운하우스: 층 사이 띠
            mb.Box(m.trim, new Vector3(0f, plinth + f * FloorHeight, -0.03f), new Vector3(W, 0.14f, 0.1f), Quaternion.identity, 1f);

        // 지붕
        if (town)
        {
            mb.Box(m.trim, new Vector3(0f, top + 0.25f, D / 2f), new Vector3(W + 0.02f, 0.5f, D), Quaternion.identity, 1f);        // 파라펫
            mb.Box(m.trim, new Vector3(0f, top + 0.42f, -0.2f), new Vector3(W + 0.2f, 0.3f, 0.45f), Quaternion.identity, 1f);       // 코니스(양옆 0.1m: 옆집과 W+0.2 간격이면 딱 맞닿는다)
            mb.Box(m.trim, new Vector3(0f, top + 0.2f, -0.12f), new Vector3(W + 0.16f, 0.12f, 0.3f), Quaternion.identity, 1f);
        }
        else
        {
            float pitch = (s.style == Style.Bungalow ? 24f : 34f) * Mathf.Deg2Rad;
            float overhang = 0.45f;
            float rise = D / 2f * Mathf.Tan(pitch);
            float eaveDrop = overhang * Mathf.Tan(pitch);
            var ridge = new Vector3(0f, top + rise, D / 2f);
            var eaveFront = new Vector3(0f, top - eaveDrop, -overhang);
            var eaveBack = new Vector3(0f, top - eaveDrop, D + overhang);
            mb.Slab(m.roof, eaveFront, ridge, W + 2f * overhang, 0.14f, 1.5f);
            mb.Slab(m.roof, eaveBack, ridge, W + 2f * overhang, 0.14f, 1.5f);
            mb.Box(m.trim, ridge + Vector3.up * 0.08f, new Vector3(W + 2f * overhang, 0.12f, 0.28f), Quaternion.identity, 1f); // 용마루
            // 박공 삼각벽(양옆)
            foreach (float sx in new[] { -1f, 1f })
                mb.Triangle(s.wall, new Vector3(sx * W / 2f, top, 0f), new Vector3(sx * W / 2f, top, D), new Vector3(sx * W / 2f, top + rise, D / 2f), new Vector3(sx, 0f, 0f), s.wallTile,
                    new Vector3(sx * W / 2f, plinth, sx > 0f ? 0f : D), new Vector3(0f, 0f, sx));
            // 처마 끝 판(앞·뒤)
            mb.Box(m.trim, eaveFront + new Vector3(0f, -0.06f, 0f), new Vector3(W + 2f * overhang, 0.2f, 0.05f), Quaternion.identity, 1f);
            mb.Box(m.trim, eaveBack + new Vector3(0f, -0.06f, 0f), new Vector3(W + 2f * overhang, 0.2f, 0.05f), Quaternion.identity, 1f);
            if (s.chimney)
                mb.Box(m.foundation, new Vector3(W / 2f - 1.2f, top + rise * 0.6f, D * 0.7f), new Vector3(0.8f, rise * 1.4f + 1f, 0.8f), Quaternion.identity, 1f);
        }

        // 창문: 앞면은 층마다 일정 간격(1층은 문 자리를 비움), 옆면은 층마다 하나
        float doorX = town ? W / 2f - 1.15f : s.doorOffset;
        float winW = town ? 1.0f : 0.95f, winH = town ? 1.9f : 1.45f;
        float winHalf = WindowHalfWidth(s, winW);          // 덧창까지 포함한 창 반폭
        float edge = winHalf + 0.25f;                       // 모서리 몰딩과의 여유
        float pitchGap = 2f * winHalf + 0.6f;               // 창과 창 사이(덧창 포함) 최소 간격
        var columns = Spread(-W / 2f + edge, W / 2f - edge, pitchGap);
        // 1층 앞면은 현관(포치 지붕 폭, 포치가 없으면 문틀 + 현관등)을 피해 남은 양쪽 벽에 고르게 놓는다
        float keepOut = (!town && s.porch ? Mathf.Min(3.6f, W - 1f) / 2f + 0.25f : 0.65f + 0.35f) + 0.15f;
        // 여러 층이면 위층 창과 같은 세로줄에 맞추고, 그 줄이 하나도 안 들어가는 쪽만 새로 고르게 놓는다
        var ground = new List<float>();
        foreach (var (a, b) in new[] { (-W / 2f + edge, doorX - keepOut - winHalf), (doorX + keepOut + winHalf, W / 2f - edge) })
        {
            var fit = new List<float>();
            foreach (float x in columns) if (x >= a - 0.01f && x <= b + 0.01f) fit.Add(x);
            ground.AddRange(fit.Count > 0 && s.floors > 1 ? fit : Spread(a, b, pitchGap));
        }
        for (int f = 0; f < s.floors; f++)
        {
            float sill = plinth + f * FloorHeight + (town ? 0.7f : 0.9f);
            foreach (float x in f == 0 ? ground : columns)
                Window(mb, m, s, new Vector3(x, sill, 0f), Quaternion.identity, winW, winH, rng);
            foreach (float x in columns)
                Window(mb, m, s, new Vector3(x, sill, D), Quaternion.Euler(0f, 180f, 0f), winW, winH, rng); // 뒷면
            foreach (float sx in new[] { -1f, 1f })
                if (!town) Window(mb, m, s, new Vector3(sx * W / 2f, sill, D / 2f), Quaternion.Euler(0f, sx * -90f, 0f), winW, winH, rng);
        }

        // 현관문(문틀 + 문짝 + 문턱)
        float doorBase = plinth;
        const float doorW = 0.98f, doorH = 2.1f, jamb = 0.16f, jambD = 0.14f;
        mb.Box(m.trim, new Vector3(doorX - doorW / 2f - jamb / 2f, doorBase + doorH / 2f, -jambD / 2f), new Vector3(jamb, doorH, jambD), Quaternion.identity, 1f);
        mb.Box(m.trim, new Vector3(doorX + doorW / 2f + jamb / 2f, doorBase + doorH / 2f, -jambD / 2f), new Vector3(jamb, doorH, jambD), Quaternion.identity, 1f);
        mb.Box(m.trim, new Vector3(doorX, doorBase + doorH + 0.11f, -jambD / 2f - 0.01f), new Vector3(doorW + 2f * jamb + 0.12f, 0.22f, jambD + 0.02f), Quaternion.identity, 1f);
        mb.Box(s.door, new Vector3(doorX, doorBase + doorH / 2f, -0.03f), new Vector3(doorW, doorH, 0.05f), Quaternion.identity, 0f); // 0 = 면마다 텍스처 한 장
        mb.Box(m.foundation, new Vector3(doorX, doorBase + 0.015f, -0.08f), new Vector3(doorW + 2f * jamb, 0.03f, 0.16f), Quaternion.identity, 1f); // 문턱
        if (m.brass)
        {
            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(knob.GetComponent<Collider>());
            knob.name = "DoorKnob";
            knob.transform.SetParent(root.transform, false);
            knob.transform.localPosition = new Vector3(doorX + doorW / 2f - 0.11f, doorBase + 0.98f, -0.1f);
            knob.transform.localScale = new Vector3(0.065f, 0.065f, 0.05f);
            knob.GetComponent<MeshRenderer>().sharedMaterial = m.brass;
            mb.Box(m.brass, new Vector3(doorX + doorW / 2f - 0.11f, doorBase + 0.98f, -0.065f), new Vector3(0.025f, 0.025f, 0.03f), Quaternion.identity, 1f); // 손잡이 목
        }

        var colliders = new List<(Vector3 c, Vector3 size)>();
        colliders.Add((new Vector3(0f, (top + 0.4f) / 2f, D / 2f), new Vector3(W + 0.1f, top + 0.4f, D + 0.1f)));

        if (town)
        {
            // 높은 계단(스툽) + 철제 난간: 1.2m를 0.2m 계단 6개로
            int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float h = plinth - i * 0.2f;
                var c = new Vector3(doorX, h / 2f, -0.6f - i * 0.32f);
                mb.Box(m.foundation, c, new Vector3(1.6f, h, 0.32f), Quaternion.identity, 1f);
                colliders.Add((c, new Vector3(1.6f, h, 0.32f)));
            }
            var landing = new Vector3(doorX, plinth / 2f, -0.3f);
            mb.Box(m.foundation, landing, new Vector3(1.6f, plinth, 0.6f), Quaternion.identity, 1f);
            colliders.Add((landing, new Vector3(1.6f, plinth, 0.6f)));
            foreach (float sx in new[] { -0.78f, 0.78f })
            {
                mb.Slab(m.metal, new Vector3(doorX + sx, 0.95f, -0.6f - steps * 0.32f), new Vector3(doorX + sx, plinth + 0.95f, -0.3f), 0.04f, 0.04f, 1f);
                for (int i = 0; i <= steps; i += 2)
                {
                    float h = plinth - i * 0.2f;
                    mb.Box(m.metal, new Vector3(doorX + sx, h + 0.47f, -0.45f - i * 0.32f), new Vector3(0.03f, 0.95f, 0.03f), Quaternion.identity, 1f);
                }
            }
            Lamp(mb, m, root.transform, new Vector3(doorX - 0.85f, plinth + 2.0f, -0.08f));
            doorFront = root.transform.TransformPoint(new Vector3(doorX, plinth, -0.6f));
        }
        else if (s.porch)
        {
            // 포치: 데크 + 계단(0.15m씩) + 기둥 + 난간 + 지붕
            float pw = Mathf.Min(3.6f, W - 1f), pd = 2.2f;
            var deck = new Vector3(doorX, plinth / 2f, -pd / 2f);
            mb.Box(m.porchWood, deck, new Vector3(pw, plinth, pd), Quaternion.identity, 1f);
            colliders.Add((deck, new Vector3(pw, plinth, pd)));
            int steps = Mathf.CeilToInt(plinth / 0.15f) - 1;
            for (int i = 0; i < steps; i++)
            {
                float h = plinth - (i + 1) * 0.15f;
                var c = new Vector3(doorX, h / 2f, -pd - 0.15f - i * 0.3f);
                mb.Box(m.porchWood, c, new Vector3(1.5f, h, 0.3f), Quaternion.identity, 1f);
                colliders.Add((c, new Vector3(1.5f, h, 0.3f)));
            }
            float postH = Mathf.Min(2.5f, wallH - 0.75f); // 단층집은 처마보다 낮게
            foreach (float sx in new[] { -pw / 2f + 0.1f, pw / 2f - 0.1f })
            {
                var post = new Vector3(doorX + sx, plinth + postH / 2f, -pd + 0.1f);
                mb.Box(m.trim, post, new Vector3(0.16f, postH, 0.16f), Quaternion.identity, 1f);
                colliders.Add((post, new Vector3(0.16f, postH, 0.16f)));
                // 앞 난간(계단 쪽은 비운다): 기둥에서 계단 옆까지
                float inner = sx < 0 ? -0.8f : 0.8f;
                float x0 = doorX + sx, x1 = doorX + inner;
                mb.Box(m.trim, new Vector3((x0 + x1) / 2f, plinth + 0.9f, -pd + 0.1f), new Vector3(Mathf.Abs(x1 - x0), 0.07f, 0.07f), Quaternion.identity, 1f);
                for (float bx = Mathf.Min(x0, x1) + 0.12f; bx < Mathf.Max(x0, x1) - 0.05f; bx += 0.14f)
                    mb.Box(m.trim, new Vector3(bx, plinth + 0.45f, -pd + 0.1f), new Vector3(0.04f, 0.9f, 0.04f), Quaternion.identity, 1f);
                // 옆 난간
                mb.Box(m.trim, new Vector3(doorX + sx, plinth + 0.9f, -pd / 2f), new Vector3(0.07f, 0.07f, pd - 0.2f), Quaternion.identity, 1f);
                for (float bz = -pd + 0.25f; bz < -0.1f; bz += 0.14f)
                    mb.Box(m.trim, new Vector3(doorX + sx, plinth + 0.45f, bz), new Vector3(0.04f, 0.9f, 0.04f), Quaternion.identity, 1f);
            }
            // 포치 지붕(살짝 앞으로 기운 판)
            mb.Slab(m.roof, new Vector3(doorX, plinth + postH + 0.05f, -pd - 0.3f), new Vector3(doorX, plinth + postH + 0.45f, 0f), pw + 0.5f, 0.12f, 1.5f);
            mb.Box(m.trim, new Vector3(doorX, plinth + postH, -pd + 0.1f), new Vector3(pw + 0.1f, 0.2f, 0.16f), Quaternion.identity, 1f);
            Lamp(mb, m, root.transform, new Vector3(doorX + 0.85f, plinth + 1.95f, -0.08f));
            doorFront = root.transform.TransformPoint(new Vector3(doorX, plinth, -0.7f));
        }
        else
        {
            // 포치 없이 짧은 계단만
            for (int i = 0; i < 3; i++)
            {
                float h = plinth - i * 0.15f;
                var c = new Vector3(doorX, h / 2f, -0.25f - i * 0.3f);
                mb.Box(m.foundation, c, new Vector3(1.4f, h, 0.3f), Quaternion.identity, 1f);
                colliders.Add((c, new Vector3(1.4f, h, 0.3f)));
            }
            Lamp(mb, m, root.transform, new Vector3(doorX + 0.8f, plinth + 1.95f, -0.08f));
            doorFront = root.transform.TransformPoint(new Vector3(doorX, plinth, -0.5f));
        }

        // 재질별 메시 하나로 합치고, 충돌체를 붙인다
        var mesh = mb.Build(name, out var mats);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = mats;
        foreach (var (c, size) in colliders)
        {
            var bc = root.AddComponent<BoxCollider>();
            bc.center = c;
            bc.size = size;
        }
        return root;
    }

    /// <summary>a~b 구간에 간격 gap 이상으로 창 중심을 고르게 놓는다(구간이 음수면 없음, 짧으면 가운데 하나)</summary>
    static List<float> Spread(float a, float b, float gap)
    {
        var xs = new List<float>();
        if (b < a - 0.01f) return xs;
        int n = Mathf.FloorToInt((b - a) / gap) + 1;
        for (int i = 0; i < n; i++) xs.Add(n == 1 ? (a + b) / 2f : Mathf.Lerp(a, b, i / (float)(n - 1)));
        return xs;
    }

    static float WindowHalfWidth(Spec s, float w) => w / 2f + 0.09f + (s.shutter ? 0.51f : 0.18f);

    static void Window(MeshBuilder mb, Materials m, Spec s, Vector3 sillCentre, Quaternion rot, float w, float h, System.Random rng)
    {
        // 벽면(z = 0)에서 바깥(-z)으로 튀어나온 창틀 안쪽에 유리가 들어앉은 모양
        Vector3 P(float x, float y, float z) => sillCentre + rot * new Vector3(x, y, z);
        bool lit = rng.NextDouble() < s.litChance;
        // 창마다 방 사진(773×1001)의 다른 부분을 창 비율대로 잘라 쓰고 절반은 좌우를 뒤집는다(같은 방이 반복돼 보이지 않게)
        const float photoAspect = 773f / 1001f;
        float vPerU = photoAspect * h / w;                        // 같은 비율을 유지할 때 uv 높이 / uv 폭
        float rw = Mathf.Lerp(0.5f, Mathf.Min(0.85f, 1f / vPerU), (float)rng.NextDouble());
        float rh = rw * vPerU;
        var crop = new Rect((float)rng.NextDouble() * (1f - rw), (float)rng.NextDouble() * (1f - rh), rw, rh);
        if (rng.NextDouble() < 0.5) crop = new Rect(crop.xMax, crop.y, -crop.width, crop.height);
        mb.Box(lit ? m.glassLit : m.glassDark, P(0f, h / 2f, -0.03f), new Vector3(w, h, 0.02f), rot, 0f, crop);
        const float t = 0.09f, d = 0.12f;
        mb.Box(m.trim, P(0f, h + t / 2f, -d / 2f), new Vector3(w + 2f * t, t, d), rot, 1f);         // 위
        mb.Box(m.trim, P(0f, -t / 2f, -d / 2f), new Vector3(w + 2f * t, t, d), rot, 1f);            // 아래
        mb.Box(m.trim, P(-w / 2f - t / 2f, h / 2f, -d / 2f), new Vector3(t, h, d), rot, 1f);       // 왼쪽
        mb.Box(m.trim, P(w / 2f + t / 2f, h / 2f, -d / 2f), new Vector3(t, h, d), rot, 1f);        // 오른쪽
        mb.Box(m.trim, P(0f, h / 2f, -0.05f), new Vector3(0.045f, h, 0.05f), rot, 1f);              // 세로 창살
        mb.Box(m.trim, P(0f, h * 0.55f, -0.05f), new Vector3(w, 0.045f, 0.05f), rot, 1f);           // 가로 창살
        mb.Box(m.trim, P(0f, -t - 0.03f, -0.1f), new Vector3(w + 0.36f, 0.06f, 0.2f), rot, 1f);     // 창턱
        mb.Box(m.trim, P(0f, h + t + 0.06f, -0.08f), new Vector3(w + 0.34f, 0.1f, 0.16f), rot, 1f); // 위 장식
        if (s.shutter)
            foreach (float sx in new[] { -1f, 1f })
                mb.Box(s.shutter, P(sx * (w / 2f + t + 0.27f), h / 2f, -0.03f), new Vector3(0.48f, h + 0.1f, 0.04f), rot, 0.5f);
    }

    /// <summary>벽에 다는 등 하나(현관등과 같은 모양·불빛). position = 벽 바깥 면, yaw = 등이 바깥을 보는 방향(로컬 -z가 바깥)</summary>
    public static GameObject WallLamp(Transform parent, string name, Vector3 position, float yaw, Materials m)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var mb = new MeshBuilder();
        Lamp(mb, m, root.transform, Vector3.zero);
        root.AddComponent<MeshFilter>().sharedMesh = mb.Build(name, out var mats);
        root.AddComponent<MeshRenderer>().sharedMaterials = mats;
        return root;
    }

    static void Lamp(MeshBuilder mb, Materials m, Transform root, Vector3 local)
    {
        mb.Box(m.metal, local + new Vector3(0f, 0f, -0.04f), new Vector3(0.18f, 0.3f, 0.12f), Quaternion.identity, 1f);
        mb.Box(m.lamp, local + new Vector3(0f, 0f, -0.11f), new Vector3(0.12f, 0.2f, 0.02f), Quaternion.identity, 0f);
        var go = new GameObject("PorchLight");
        go.transform.SetParent(root, false);
        go.transform.localPosition = local + new Vector3(0f, 0.1f, -0.5f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.76f, 0.48f);
        l.intensity = 2.6f;
        l.range = 6.5f;
        l.shadows = LightShadows.None;
    }

    // ---------- 실내 현관문 ----------

    /// <summary>
    /// 방 안에서 본 현관문(문틀 + 문짝 + 문고리). position = 벽 안쪽 면의 바닥, 로컬 +z = 방 쪽.
    /// 바깥 현관문과 같은 문 재질을 써서 안팎이 같은 문으로 보이게 한다.
    /// </summary>
    public static GameObject InteriorDoor(Transform parent, string name, Vector3 position, float yaw, Material door, Materials m)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var mb = new MeshBuilder();
        const float doorW = 0.98f, doorH = 2.1f, jamb = 0.12f, jambD = 0.12f;
        mb.Box(m.trim, new Vector3(-doorW / 2f - jamb / 2f, doorH / 2f, jambD / 2f), new Vector3(jamb, doorH, jambD), Quaternion.identity, 1f);
        mb.Box(m.trim, new Vector3(doorW / 2f + jamb / 2f, doorH / 2f, jambD / 2f), new Vector3(jamb, doorH, jambD), Quaternion.identity, 1f);
        mb.Box(m.trim, new Vector3(0f, doorH + 0.08f, jambD / 2f), new Vector3(doorW + 2f * jamb + 0.08f, 0.16f, jambD), Quaternion.identity, 1f);
        mb.Box(door, new Vector3(0f, doorH / 2f, 0.03f), new Vector3(doorW, doorH, 0.05f), Quaternion.identity, 0f);
        mb.Box(m.brass, new Vector3(-doorW / 2f + 0.11f, 0.98f, 0.07f), new Vector3(0.025f, 0.025f, 0.03f), Quaternion.identity, 1f); // 손잡이 목
        var mesh = mb.Build(name, out var mats);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = mats;
        var bc = root.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, (doorH + 0.16f) / 2f, jambD / 2f);
        bc.size = new Vector3(doorW + 2f * jamb + 0.08f, doorH + 0.16f, jambD);
        var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(knob.GetComponent<Collider>());
        knob.name = "DoorKnob";
        knob.transform.SetParent(root.transform, false);
        knob.transform.localPosition = new Vector3(-doorW / 2f + 0.11f, 0.98f, 0.1f);
        knob.transform.localScale = new Vector3(0.065f, 0.065f, 0.05f);
        knob.GetComponent<MeshRenderer>().sharedMaterial = m.brass;
        return root;
    }

    // ---------- 재질 ----------

    const string TexDir = "Assets/Game/Art/Prologue/Textures/House";
    const string MatDir = "Assets/Game/Art/Prologue/Materials/House";

    /// <summary>벽 색(흰 사이딩에 곱하는 색). 벽돌은 텍스처 색 그대로</summary>
    public static readonly Dictionary<string, Color> WallColors = new()
    {
        { "White", new Color(0.93f, 0.92f, 0.89f) },
        { "BlueGrey", new Color(0.60f, 0.67f, 0.74f) },
        { "Sage", new Color(0.66f, 0.72f, 0.61f) },
        { "Beige", new Color(0.88f, 0.81f, 0.68f) },
        { "Navy", new Color(0.30f, 0.37f, 0.50f) },
        { "Butter", new Color(0.93f, 0.86f, 0.62f) },
    };

    public static readonly Dictionary<string, Color> DoorColors = new()
    {
        { "Red", new Color(0.62f, 0.14f, 0.12f) },
        { "Navy", new Color(0.17f, 0.23f, 0.38f) },
        { "Black", new Color(0.14f, 0.14f, 0.15f) },
        { "White", new Color(0.92f, 0.91f, 0.88f) },
    };

    public static readonly Dictionary<string, Color> ShutterColors = new()
    {
        { "Black", new Color(0.09f, 0.1f, 0.11f) },
        { "Navy", new Color(0.13f, 0.18f, 0.3f) },
        { "Green", new Color(0.13f, 0.24f, 0.17f) },
        { "White", new Color(0.9f, 0.89f, 0.86f) },
    };

    static Materials shared;
    static readonly Dictionary<string, Material> cache = new();

    /// <summary>모든 집이 함께 쓰는 재질(몰딩·기초·지붕·유리 등)</summary>
    public static Materials SharedMaterials()
    {
        if (shared != null && shared.trim != null) return shared;
        PrepareTextures();
        var window = Tex("HouseWindowLit");
        shared = new Materials
        {
            trim = Mat("HouseTrim", null, null, new Color(0.88f, 0.87f, 0.83f), 0.35f),
            foundation = Mat("HouseFoundation", Tex("HouseConcrete"), null, new Color(0.78f, 0.78f, 0.77f), 0.2f),
            roof = Mat("HouseRoof", Tex("HouseShingles"), Tex("HouseShingles_Height"), Color.white, 0.25f, normalScale: 0.8f),
            // 불 꺼진 창: 같은 방 사진을 어둡게(커튼 너머 어두운 방), 유리라 매끈하게
            glassDark = Mat("HouseGlassDark", window, null, new Color(0.11f, 0.11f, 0.12f), 0.68f, emission: new Color(0.045f, 0.045f, 0.055f)),
            glassLit = Mat("HouseGlassLit", window, null, new Color(0.35f, 0.33f, 0.3f), 0.92f, emission: new Color(1.05f, 0.9f, 0.72f)),
            porchWood = Mat("HousePorchWood", Tex("HouseSiding"), Tex("HouseSiding_Height"), new Color(0.47f, 0.45f, 0.42f), 0.25f, normalScale: 0.6f),
            metal = Mat("HouseIron", null, null, new Color(0.07f, 0.07f, 0.075f), 0.5f, metallic: 0.6f),
            lamp = Mat("HouseLampGlass", null, null, new Color(1f, 0.85f, 0.6f), 0.6f, emission: new Color(2.2f, 1.6f, 0.9f)),
            brass = Mat("HouseBrass", null, null, new Color(0.78f, 0.62f, 0.32f), 0.72f, metallic: 1f),
        };
        // 불 꺼진 유리가 하늘(반사 프로브)을 비춰 푸르게 빛나 보이지 않게: 주변 반사는 끄고 조명 하이라이트만 남긴다
        shared.glassDark.SetFloat("_EnvironmentReflections", 0f);
        shared.glassDark.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        UnityEditor.EditorUtility.SetDirty(shared.glassDark);
        return shared;
    }

    public static Material Wall(string key)
    {
        if (key == "Brick") return Cached("HouseWall_Brick", () => Mat("HouseWall_Brick", Tex("HouseBrick"), Tex("HouseBrick_Height"), Color.white, 0.18f, normalScale: 0.9f));
        return Cached("HouseWall_" + key, () => Mat("HouseWall_" + key, Tex("HouseSiding"), Tex("HouseSiding_Height"), WallColors[key], 0.3f, normalScale: 0.7f));
    }

    public static Material Door(string key)
    {
        if (key == "Green") return Cached("HouseDoor_Green", () => Mat("HouseDoor_Green", Tex("HouseDoor"), null, Color.white, 0.45f));
        return Cached("HouseDoor_" + key, () => Mat("HouseDoor_" + key, Tex("HouseDoorNeutral"), null, DoorColors[key], 0.45f));
    }

    public static Material Shutter(string key) =>
        Cached("HouseShutter_" + key, () => Mat("HouseShutter_" + key, Tex("HouseSiding"), Tex("HouseSiding_Height"), ShutterColors[key], 0.3f, normalScale: 0.5f));

    static Material Cached(string name, System.Func<Material> make)
    {
        if (!cache.TryGetValue(name, out var m) || m == null) cache[name] = m = make();
        return m;
    }

    static Texture2D Tex(string name) => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

    /// <summary>텍스처 가져오기 설정: 반복 재질은 Repeat, 문·창은 Clamp, _Height는 회색조→노멀맵</summary>
    static void PrepareTextures()
    {
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool height = file.EndsWith("_Height");
            bool single = file.StartsWith("HouseDoor") || file.StartsWith("HouseWindow");
            var type = height ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default;
            var wrap = single ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (imp.textureType == type && imp.wrapMode == wrap && imp.anisoLevel == 8 && imp.convertToNormalmap == height) continue;
            imp.textureType = type;
            imp.convertToNormalmap = height;
            if (height) imp.heightmapScale = 0.06f;
            imp.wrapMode = wrap;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
        }
    }

    static Material Mat(string name, Texture2D tex, Texture2D normal, Color color, float smoothness, float metallic = 0f, Color? emission = null, float normalScale = 1f)
    {
        if (!UnityEditor.AssetDatabase.IsValidFolder(MatDir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Art/Prologue/Materials", "House");
        string path = $"{MatDir}/{name}.mat";
        var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            UnityEditor.AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", normalScale);
        if (normal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
        if (emission is Color e)
        {
            mat.SetColor("_EmissionColor", e);
            mat.SetTexture("_EmissionMap", tex);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // 이 플래그가 없으면 URP가 _EMISSION을 지운다
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        UnityEditor.EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------- 메시 모으기 ----------

    class MeshBuilder
    {
        readonly Dictionary<Material, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri)> parts = new();

        (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri) Part(Material mat)
        {
            if (!parts.TryGetValue(mat, out var p)) parts[mat] = p = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
            return p;
        }

        /// <summary>회전된 상자. tile = 텍스처 한 장이 덮는 길이(m), 0이면 면마다 텍스처 한 장을 꽉 채운다
        /// (uvRect를 주면 한 장 대신 그 부분만; 폭이 음수면 좌우 반전)</summary>
        public void Box(Material mat, Vector3 centre, Vector3 size, Quaternion rot, float tile, Rect uvRect = default)
        {
            if (uvRect.width == 0f) uvRect = new Rect(0f, 0f, 1f, 1f);
            var p = Part(mat);
            Vector3 h = size / 2f;
            void Face(Vector3 n, Vector3 u, Vector3 v, float uLen, float vLen)
            {
                int i = p.v.Count;
                Vector3 c = Vector3.Scale(n, h);
                Vector3 du = u * (uLen / 2f), dv = v * (vLen / 2f);
                foreach (var corner in new[] { c - du - dv, c + du - dv, c + du + dv, c - du + dv })
                    p.v.Add(centre + rot * corner);
                if (tile > 0f)
                {
                    float su = uLen / tile, sv = vLen / tile;
                    p.uv.Add(new Vector2(0, 0)); p.uv.Add(new Vector2(su, 0)); p.uv.Add(new Vector2(su, sv)); p.uv.Add(new Vector2(0, sv));
                }
                else
                {
                    p.uv.Add(new Vector2(uvRect.xMin, uvRect.yMin)); p.uv.Add(new Vector2(uvRect.xMax, uvRect.yMin));
                    p.uv.Add(new Vector2(uvRect.xMax, uvRect.yMax)); p.uv.Add(new Vector2(uvRect.xMin, uvRect.yMax));
                }
                for (int k = 0; k < 4; k++) p.n.Add(rot * n);
                p.tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
        }

        /// <summary>두 점(a → b, 판의 중심선)을 잇는 기울어진 판. width는 중심선에 수직인 가로 폭</summary>
        public void Slab(Material mat, Vector3 a, Vector3 b, float width, float thickness, float tile)
        {
            Vector3 dir = b - a;
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Box(mat, (a + b) / 2f, new Vector3(width, thickness, dir.magnitude), rot, tile);
        }

        /// <summary>삼각형 면. uvOrigin·uAxis는 이어지는 벽 상자 면의 UV 원점·가로 방향과 맞춰야 무늬가 끊기지 않는다</summary>
        public void Triangle(Material mat, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, float tile, Vector3 uvOrigin, Vector3 uAxis)
        {
            var p = Part(mat);
            int i = p.v.Count;
            p.v.Add(a); p.v.Add(b); p.v.Add(c);
            foreach (var q in new[] { a, b, c }) p.uv.Add(new Vector2(Vector3.Dot(q - uvOrigin, uAxis) / tile, (q.y - uvOrigin.y) / tile));
            for (int k = 0; k < 3; k++) p.n.Add(normal);
            // 상자 면과 같은 규칙: Cross(둘째-첫째, 셋째-첫째)가 법선 쪽을 향하는 순서가 앞면
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f) p.tri.AddRange(new[] { i, i + 1, i + 2 });
            else p.tri.AddRange(new[] { i, i + 2, i + 1 });
        }

        public Mesh Build(string name, out Material[] mats)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var subs = new List<List<int>>();
            mats = new Material[parts.Count];
            int k = 0;
            foreach (var kv in parts)
            {
                int offset = v.Count;
                v.AddRange(kv.Value.v); n.AddRange(kv.Value.n); uv.AddRange(kv.Value.uv);
                var t = new List<int>(kv.Value.tri.Count);
                foreach (int idx in kv.Value.tri) t.Add(idx + offset);
                subs.Add(t);
                mats[k++] = kv.Key;
            }
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
