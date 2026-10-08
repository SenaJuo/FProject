using UnityEngine;

/// <summary>
/// 검수용: 플레이 중 주인공의 동작(대기·걷기·달리기·구르기·공격)을 상태별로 멈춰 세워 옆·앞에서 찍어 한 장으로 묶는다.
/// 모션이 어색한 곳(어깨·팔꿈치·옷 찢어짐)을 눈으로 확인할 때 쓴다.
/// </summary>
public static class PoseSheet
{
    public static string Shoot(string path, (string state, float t, float speed)[] only = null)
    {
        if (!Application.isPlaying) return "플레이 중에만";
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (!pc) return "플레이어 없음";
        var anim = pc.GetComponentInChildren<Animator>();
        pc.enabled = false;
        anim.applyRootMotion = false;
        // 밝은 곳에서 찍는다: 위에서 비추는 임시 조명
        var lightGo = new GameObject("PoseSheetLight");
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 8f; l.intensity = 6f;
        Vector3 root = pc.transform.position;
        lightGo.transform.position = root + Vector3.up * 2.6f + pc.transform.forward * 1.2f;

        (string state, float t, float speed)[] poses = only ?? new (string, float, float)[]
        {
            ("Idle Walk Run Blend", 0.2f, 0f), ("Idle Walk Run Blend", 0.25f, 2f), ("Idle Walk Run Blend", 0.5f, 2f),
            ("Idle Walk Run Blend", 0.25f, 6f), ("Idle Walk Run Blend", 0.5f, 6f),
            ("Roll", 0.25f, 0f), ("Roll", 0.55f, 0f), ("Roll", 0.85f, 0f), ("Roll", 0.98f, 0f),
            ("Jab", 0.25f, 0f), ("Jab", 0.45f, 0f), ("Cross", 0.25f, 0f), ("Cross", 0.45f, 0f), ("Kick", 0.3f, 0f), ("Kick", 0.5f, 0f),
        };
        var shots = new System.Collections.Generic.List<Texture2D>();
        Vector3 f = pc.transform.forward, r = pc.transform.right;
        foreach (var (state, t, speed) in poses)
        {
            anim.SetFloat("Speed", speed);
            anim.SetFloat("MotionSpeed", 1f);
            anim.Play(state, 0, t);
            anim.Update(0f);
            // 블렌드 트리는 Speed가 반영되도록 한 번 더
            anim.Play(state, 0, t);
            anim.Update(0f);
            Vector3 c = root + Vector3.up * 0.95f;
            shots.Add(QaShots.Render(c + r * 4.4f + Vector3.up * 0.2f, c, 40f, 420, 520));              // 옆
            shots.Add(QaShots.Render(c + f * 4.0f + r * 1.6f + Vector3.up * 0.3f, c, 40f, 420, 520)); // 앞 비스듬히
        }
        Object.Destroy(lightGo);
        QaShots.Sheet(path, shots.ToArray(), 6);
        pc.enabled = true;
        return $"찍음 {shots.Count}장 → {path}";
    }
}
