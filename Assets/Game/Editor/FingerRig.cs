using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 손가락 뼈 자동 리깅. 바로크 캐릭터(22개 뼈)는 손목 뼈 하나가 손 전체를 움직여서, 주먹을 쥐어야 할 때도 손이 펴진 손날이 된다.
/// 손 모델은 손가락 다섯 개가 따로 만들어져 있으므로:
///  1) 손 정점을 손목에서 손끝 방향으로 잘라 메시 연결로 손가락을 나눈다(엄지는 손 길이 43% 지점, 나머지 넷은 60~80% 지점에서 떨어진다)
///  2) 손가락마다 마디 3개(엄지는 2개)를 표준 손 비율로 놓는다(손가락 길이 = 손 길이 × 검지 0.43·중지 0.46·약지 0.43·소지 0.35, 마디 위치 45%·75%)
///  3) 정점을 마디 뼈에 이어 붙인다(마디 경계는 부드럽게 섞어 각지지 않게)
///  4) FingerPose를 붙여 게임 중에 주먹·오므린 손으로 접는다
/// 쉼 자세(T자, 손바닥이 아래)가 바인드 자세와 같아야 한다 — 다르면 아무것도 바꾸지 않고 알린다.
/// 바꾼 메시는 원본 FBX 옆에 {메시이름}_Fingers.asset으로 저장한다.
/// </summary>
public static class FingerRig
{
    static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Little" };
    static readonly float[] LengthRatio = { 0.43f, 0.46f, 0.43f, 0.35f };
    const float PipAt = 0.45f, DipAt = 0.75f;
    // 주먹 각도(손허리손가락·첫째 마디·끝마디)와 오므린 손 각도
    static readonly float[] FistAngles = { 85f, 100f, 60f }, RelaxedAngles = { 22f, 28f, 15f };

    public static string Apply(Animator animator)
    {
        if (animator.GetComponent<FingerPose>()) return $"{animator.name}: 이미 손가락 뼈가 있다";
        var smr = animator.GetComponentInChildren<SkinnedMeshRenderer>();
        var src = smr.sharedMesh;
        var bones = smr.bones.ToList();
        var bindposes = src.bindposes.ToList();
        var toWorld = smr.transform.localToWorldMatrix;

        // 지금 자세가 바인드 자세인지: 손 뼈의 바인드포즈를 다시 계산해 비교한다
        foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
        {
            var h = animator.GetBoneTransform(hb);
            int i = bones.IndexOf(h);
            var expect = h.worldToLocalMatrix * toWorld;
            float diff = 0f;
            for (int k = 0; k < 16; k++) diff = Mathf.Max(diff, Mathf.Abs(expect[k] - bindposes[i][k]));
            if (diff > 0.002f) return $"{animator.name}: 지금 자세가 바인드 자세와 달라서({diff:0.0000}) 손가락 뼈를 만들지 않았다";
        }

        var verts = src.vertices.Select(v => (Vector3)(toWorld.MultiplyPoint3x4(v))).ToArray();
        var weights = src.boneWeights;
        var tris = src.triangles;
        // UV 이음매로 갈라진 같은 위치 정점을 하나로 본다(메시 연결 판정용)
        var weld = new int[verts.Length];
        var map = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < verts.Length; i++)
        {
            var key = Vector3Int.RoundToInt(src.vertices[i] * 10000f);
            if (!map.TryGetValue(key, out int id)) map[key] = id = i;
            weld[i] = id;
        }

        var joints = new List<FingerPose.Joint>();
        var report = new System.Text.StringBuilder($"{animator.name}: ");
        foreach (bool left in new[] { false, true })
        {
            string msg = RigHand(animator, left, smr, bones, bindposes, verts, weights, tris, weld, toWorld, joints);
            if (msg.StartsWith("실패")) return $"{animator.name}: {msg} — 바꾸지 않았다";
            report.Append(msg).Append(' ');
        }

        // 메시 저장(같은 이름이 있으면 덮어써서 GUID를 유지)
        var mesh = Object.Instantiate(src);
        mesh.name = src.name + "_Fingers";
        mesh.boneWeights = weights;
        mesh.bindposes = bindposes.ToArray();
        string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(src)).Replace('\\', '/');
        string path = $"{dir}/{mesh.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
        else AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();

        smr.bones = bones.ToArray();
        smr.sharedMesh = mesh;
        var pose = animator.gameObject.AddComponent<FingerPose>();
        pose.joints = joints.ToArray();
        return report.ToString();
    }

    static string RigHand(Animator animator, bool left, SkinnedMeshRenderer smr, List<Transform> bones, List<Matrix4x4> bindposes,
        Vector3[] verts, BoneWeight[] weights, int[] tris, int[] weld, Matrix4x4 toWorld, List<FingerPose.Joint> joints)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var fore = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        int hi = bones.IndexOf(hand);
        Vector3 f = (hand.position - fore.position).normalized;      // 손끝 방향
        Vector3 lat = Vector3.Cross(Vector3.up, f).normalized;        // 손 옆 방향
        Vector3 down = Vector3.down;                                   // 쉼 자세(T자)의 손바닥 방향

        // 손 정점(손목 뼈 비중이 절반 이상)
        var hv = new List<int>();
        var fq = new float[verts.Length]; var lq = new float[verts.Length]; var yq = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            float w = Weight(weights[i], hi);
            if (w < 0.5f) continue;
            var d = verts[i] - hand.position;
            fq[i] = Vector3.Dot(d, f); lq[i] = Vector3.Dot(d, lat); yq[i] = d.y;
            hv.Add(i);
        }
        if (hv.Count < 100) return "실패(손 정점이 너무 적다)";
        float handLen = hv.Max(i => fq[i]);
        var isHand = new bool[verts.Length]; foreach (int i in hv) isHand[i] = true;

        // 엄지: 손 길이 43% 지점에서 잘랐을 때 옆으로 가장 멀리 떨어진 조각
        var thumbCut = Components(tris, weld, verts.Length, i => isHand[i] && fq[i] > handLen * 0.43f);
        var thumb = thumbCut.Where(c => c.Count >= 8 && c.Max(i => fq[i]) < handLen * 0.85f)
                            .OrderByDescending(c => Mathf.Abs((float)c.Average(i => lq[i]))).FirstOrDefault();
        if (thumb == null) return "실패(엄지를 찾지 못했다)";
        float thumbSign = Mathf.Sign((float)thumb.Average(i => lq[i]));
        var thumbSet = new HashSet<int>(thumb);

        // 네 손가락: 네 조각으로 갈라지는 가장 가까운 자르는 거리
        List<List<int>> fingers = null; float cut = 0f;
        for (float t = 0.58f; t <= 0.82f; t += 0.01f)
        {
            var comps = Components(tris, weld, verts.Length, i => isHand[i] && !thumbSet.Contains(i) && fq[i] > handLen * t)
                        .Where(c => c.Count >= 8).ToList();
            if (comps.Count == 4) { fingers = comps; cut = t; break; }
        }
        string how;
        if (fingers != null) how = $"손가락 분리 {cut * 100f:0}%";
        else
        {
            // 장갑처럼 손가락이 한 덩어리로 붙어 있으면: 손가락 부분(손 길이 70% 이상)을 옆 방향으로 네 줄로 나눈다
            var part = hv.Where(i => !thumbSet.Contains(i) && fq[i] > handLen * 0.7f).ToList();
            if (part.Count < 40) return "실패(손가락 부분이 너무 적다)";
            float lo = part.Min(i => lq[i]), hi2 = part.Max(i => lq[i]), w = (hi2 - lo) / 4f;
            fingers = new List<List<int>>();
            for (int k = 0; k < 4; k++)
            {
                float a = lo + w * k, b = lo + w * (k + 1);
                fingers.Add(part.Where(i => lq[i] >= a - 0.0001f && lq[i] <= b + 0.0001f).ToList());
            }
            if (fingers.Any(g => g.Count < 5)) return "실패(네 줄로 나눌 수 없다)";
            how = "붙은 손가락(장갑)을 네 줄로 나눔";
        }
        // 엄지 쪽부터 검지·중지·약지·소지
        fingers = fingers.OrderBy(c => (float)c.Average(i => lq[i]) * -thumbSign).ToList();

        var report = $"{(left ? "왼손" : "오른손")} 길이 {handLen * 100f:0.0}cm 엄지 {thumb.Count}정점 {how}";
        var newWeights = new Dictionary<int, List<(int bone, float w)>>();

        // ---- 네 손가락 ----
        var lines = new List<(float aL, float bL, float aY, float bY, float mcp, float pip, float dip, float tip)>();
        for (int k = 0; k < 4; k++)
        {
            var c = fingers[k];
            Fit(c, fq, lq, out float aL, out float bL);
            Fit(c, fq, yq, out float aY, out float bY);
            float tip = c.Max(i => fq[i]);
            float len = LengthRatio[k] * handLen;
            float mcp = tip - len, pip = mcp + PipAt * len, dip = mcp + DipAt * len;
            lines.Add((aL, bL, aY, bY, mcp, pip, dip, tip));
        }
        var segBones = new int[4, 3];
        for (int k = 0; k < 4; k++)
        {
            var L = lines[k];
            Transform parent = hand;
            float[] at = { L.mcp, L.pip, L.dip };
            for (int s = 0; s < 3; s++)
            {
                var b = NewBone($"{(left ? "Left" : "Right")}{FingerNames[k]}{s + 1}", parent, hand,
                    hand.position + f * at[s] + lat * (L.aL + L.bL * at[s]) + Vector3.up * (L.aY + L.bY * at[s]));
                segBones[k, s] = bones.Count;
                bones.Add(b);
                bindposes.Add(b.worldToLocalMatrix * toWorld);
                joints.Add(new FingerPose.Joint
                {
                    bone = b, rest = b.localRotation, axis = hand.InverseTransformDirection(Vector3.Cross(f, down).normalized),
                    relaxed = RelaxedAngles[s], fist = FistAngles[s],
                });
                parent = b;
            }
        }
        // 정점 → 가장 가까운 손가락 중심선, 마디 경계는 부드럽게
        float minMcp = lines.Min(l => l.mcp);
        foreach (int i in hv)
        {
            if (thumbSet.Contains(i) || fq[i] < minMcp - 0.012f) continue;
            int best = -1; float bestD = 0.016f;
            for (int k = 0; k < 4; k++)
            {
                float d = Mathf.Abs(lq[i] - (lines[k].aL + lines[k].bL * fq[i]));
                if (d < bestD) { bestD = d; best = k; }
            }
            if (best < 0) continue;
            var L = lines[best];
            float s = fq[i];
            if (s < L.mcp - 0.008f) continue;
            float a = Smooth(L.mcp - 0.008f, L.mcp + 0.004f, s);
            float b = Smooth(L.pip - 0.004f, L.pip + 0.004f, s);
            float c = Smooth(L.dip - 0.0035f, L.dip + 0.0035f, s);
            newWeights[i] = new List<(int, float)>
            {
                (hi, 1f - a), (segBones[best, 0], a * (1f - b)), (segBones[best, 1], a * b * (1f - c)), (segBones[best, 2], a * b * c),
            };
        }

        // ---- 엄지(마디 2개) ----
        float t0 = handLen * 0.43f;
        float tTip = thumb.Max(i => fq[i]);
        var baseC = Centroid(thumb.Where(i => fq[i] < t0 + 0.012f), verts);
        var tipC = Centroid(thumb.Where(i => fq[i] > tTip - 0.012f), verts);
        Vector3 td = (tipC - baseC).normalized;
        var t1 = NewBone($"{(left ? "Left" : "Right")}Thumb1", hand, hand, baseC);
        var t2 = NewBone($"{(left ? "Left" : "Right")}Thumb2", t1, hand, Vector3.Lerp(baseC, tipC, 0.5f));
        int t1i = bones.Count; bones.Add(t1); bindposes.Add(t1.worldToLocalMatrix * toWorld);
        int t2i = bones.Count; bones.Add(t2); bindposes.Add(t2.worldToLocalMatrix * toWorld);
        Vector3 curl = Vector3.Cross(td, down).normalized;
        // 손바닥 축으로 돌려 손가락 쪽(엄지 반대편)으로 덮는 방향
        Vector3 toward = lat * -thumbSign;
        float sign = Vector3.Dot(Quaternion.AngleAxis(10f, down) * td, toward) > Vector3.Dot(td, toward) ? 1f : -1f;
        joints.Add(new FingerPose.Joint { bone = t1, rest = t1.localRotation, axis = hand.InverseTransformDirection(curl), relaxed = 8f, fist = 5f,
                                          axis2 = hand.InverseTransformDirection(down) * sign, relaxed2 = 15f, fist2 = 70f }); // 주먹: 엄지가 네 손가락 앞을 가로질러 눕는다(실측 비교로 고른 각도)
        joints.Add(new FingerPose.Joint { bone = t2, rest = t2.localRotation, axis = hand.InverseTransformDirection(curl), relaxed = 10f, fist = 32f });
        float thumbLen = Vector3.Distance(baseC, tipC);
        foreach (int i in thumb)
        {
            float s = Vector3.Dot(verts[i] - baseC, td);
            float a = Smooth(-0.004f, 0.008f, s);
            float b = Smooth(thumbLen * 0.5f - 0.004f, thumbLen * 0.5f + 0.004f, s);
            newWeights[i] = new List<(int, float)> { (hi, 1f - a), (t1i, a * (1f - b)), (t2i, a * b) };
        }

        // 새 가중치 쓰기(큰 순서로 4개)
        foreach (var kv in newWeights)
        {
            var list = kv.Value.Where(x => x.w > 0.0001f).OrderByDescending(x => x.w).Take(4).ToList();
            float sum = list.Sum(x => x.w);
            var bw = new BoneWeight();
            for (int k = 0; k < list.Count; k++)
            {
                float w = list[k].w / sum;
                switch (k)
                {
                    case 0: bw.boneIndex0 = list[k].bone; bw.weight0 = w; break;
                    case 1: bw.boneIndex1 = list[k].bone; bw.weight1 = w; break;
                    case 2: bw.boneIndex2 = list[k].bone; bw.weight2 = w; break;
                    case 3: bw.boneIndex3 = list[k].bone; bw.weight3 = w; break;
                }
            }
            weights[kv.Key] = bw;
        }
        return report + $" 다시 이은 정점 {newWeights.Count}";
    }

    static Transform NewBone(string name, Transform parent, Transform hand, Vector3 position)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(position, hand.rotation); // 손과 같은 방향: 접는 축을 손 기준으로 쓴다
        return t;
    }

    static float Weight(BoneWeight w, int bone) =>
        (w.boneIndex0 == bone ? w.weight0 : 0f) + (w.boneIndex1 == bone ? w.weight1 : 0f) +
        (w.boneIndex2 == bone ? w.weight2 : 0f) + (w.boneIndex3 == bone ? w.weight3 : 0f);

    static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - a) / (b - a)));

    static Vector3 Centroid(IEnumerable<int> ids, Vector3[] verts)
    {
        Vector3 s = Vector3.zero; int n = 0;
        foreach (int i in ids) { s += verts[i]; n++; }
        return n > 0 ? s / n : Vector3.zero;
    }

    /// <summary>y = a + b·x 최소제곱 직선</summary>
    static void Fit(List<int> ids, float[] x, float[] y, out float a, out float b)
    {
        double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = ids.Count;
        foreach (int i in ids) { sx += x[i]; sy += y[i]; sxx += x[i] * x[i]; sxy += x[i] * y[i]; }
        double den = n * sxx - sx * sx;
        b = den > 1e-9 ? (float)((n * sxy - sx * sy) / den) : 0f;
        a = (float)((sy - b * sx) / n);
    }

    /// <summary>조건을 만족하는 정점끼리 삼각형으로 이어진 덩어리들</summary>
    static List<List<int>> Components(int[] tris, int[] weld, int count, System.Func<int, bool> inside)
    {
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int k = 0; k < tris.Length; k += 3)
        {
            int a = tris[k], b = tris[k + 1], c = tris[k + 2];
            if (!inside(a) || !inside(b) || !inside(c)) continue;
            int ra = Find(weld[a]);
            parent[Find(weld[b])] = ra;
            parent[Find(weld[c])] = Find(ra);
        }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < count; i++)
        {
            if (!inside(i)) continue;
            int r = Find(weld[i]);
            if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
            g.Add(i);
        }
        return groups.Values.ToList();
    }
}
