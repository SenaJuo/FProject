using UnityEngine;

/// <summary>
/// 레이의 표정(2026-10-16 "표정이 없어 감정 없는 사이코패스 같다"). FaceRig가 얼굴에 넣은 표정 모양 8개를 상황에 맞게 섞는다.
///  · 깜빡임: 2~6초마다(가끔 두 번), 쓰러지면 눈을 감는다
///  · 싸움: 적이 깨어 있거나 막 주먹을 주고받았으면 미간을 찌푸리고 입을 다문다
///  · 맞음: 0.7초 동안 아파하는 얼굴(눈썹 안쪽이 올라가고 눈을 찡그리고 입꼬리가 내려간다)
///  · 체력이 적으면 지친 얼굴, 되감는 동안은 눈을 질끈
///  · 레이가 말하는 자막이 떠 있으면 입이 움직인다
/// 모두 실제 시간으로 움직인다(히트스톱 · 슬로모션에도 깜빡임이 멈추지 않게).
/// </summary>
public class FaceExpression : MonoBehaviour
{
    enum S { BlinkL, BlinkR, BrowDown, BrowSad, Squint, Frown, Press, Open }
    static readonly string[] Names = { "Blink_L", "Blink_R", "BrowDown", "BrowSadInner", "Squint", "MouthFrown", "MouthPress", "MouthOpen" };

    SkinnedMeshRenderer smr;
    readonly int[] index = new int[8];
    readonly float[] current = new float[8], target = new float[8];
    PlayerController player;
    Health health;
    Walkman walkman;
    float nextBlink, blinkStart = -9f, painUntil = -9f, fightUntil = -9f;
    bool doubleBlink;

    void Awake()
    {
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.sharedMesh && r.sharedMesh.GetBlendShapeIndex(Names[0]) >= 0) { smr = r; break; }
        if (!smr) { enabled = false; return; }
        for (int i = 0; i < Names.Length; i++) index[i] = smr.sharedMesh.GetBlendShapeIndex(Names[i]);
        player = GetComponentInParent<PlayerController>();
        health = GetComponentInParent<Health>();
        walkman = GetComponentInParent<Walkman>();
        if (health) health.Damaged += OnDamaged;
        nextBlink = Time.unscaledTime + Random.Range(1.5f, 4f);
    }

    void OnDestroy() { if (health) health.Damaged -= OnDamaged; }

    void OnDamaged(int amount, Vector3 from)
    {
        painUntil = Time.unscaledTime + 0.7f;
        fightUntil = Time.unscaledTime + 5f;
        blinkStart = Time.unscaledTime; // 맞는 순간 눈을 질끈
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime;
        System.Array.Clear(target, 0, target.Length);
        bool dead = player && player.IsDead;

        // 싸움 기운: 가까운 적이 깨어 있으면
        if (player && !dead)
            foreach (var e in EnemyAI.All)
                if (e && e.IsActive && !e.Health.IsDead && (e.transform.position - player.transform.position).sqrMagnitude < 14f * 14f) { fightUntil = Mathf.Max(fightUntil, now + 1.5f); break; }
        if (now < fightUntil) { target[(int)S.BrowDown] = 0.75f; target[(int)S.Press] = 0.55f; target[(int)S.Squint] = 0.3f; }

        // 지친 얼굴(체력 35% 아래)
        if (health && !dead && health.Current < health.Max * 0.35f) { Max(S.BrowSad, 0.45f); Max(S.Frown, 0.35f); }

        // 아픔
        if (now < painUntil)
        {
            float k = Mathf.Clamp01((painUntil - now) / 0.7f);
            Max(S.BrowSad, k); Max(S.Squint, 0.85f * k); Max(S.Frown, 0.7f * k);
            target[(int)S.BrowDown] *= 1f - k;
        }

        // 되감는 동안: 눈을 질끈
        if (walkman && walkman.Rewinding) { Max(S.Squint, 0.9f); Max(S.BrowDown, 0.5f); }

        // 말하기: 레이 자막이 떠 있는 동안 입을 여닫는다(음절처럼 불규칙하게)
        if (StoryState.LineVisible && StoryState.Speaker == "레이" && !dead)
        {
            float syl = Mathf.PerlinNoise(now * 7.5f, 0.37f);
            Max(S.Open, Mathf.Clamp01((syl - 0.3f) * 1.6f) * 0.7f);
        }

        // 깜빡임
        float blink = 0f;
        if (dead) blink = 1f;
        else
        {
            if (now >= nextBlink)
            {
                blinkStart = now;
                doubleBlink = Random.value < 0.15f;
                nextBlink = now + Random.Range(2.2f, 6f);
            }
            float t = now - blinkStart;
            blink = BlinkCurve(t);
            if (doubleBlink && t > 0.22f) blink = Mathf.Max(blink, BlinkCurve(t - 0.22f));
        }
        target[(int)S.BlinkL] = target[(int)S.BlinkR] = blink;

        // 섞기: 깜빡임은 곧바로, 나머지 표정은 부드럽게
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < 8; i++)
        {
            if (index[i] < 0) continue;
            bool fast = i <= (int)S.BlinkR || i == (int)S.Open;
            current[i] = fast ? target[i] : Mathf.MoveTowards(current[i], target[i], dt * (target[i] > current[i] ? 6f : 2.5f));
            smr.SetBlendShapeWeight(index[i], current[i] * 100f);
        }
    }

    void Max(S s, float v) => target[(int)s] = Mathf.Max(target[(int)s], v);

    /// <summary>감는 데 0.06초, 뜨는 데 0.1초</summary>
    static float BlinkCurve(float t)
    {
        if (t < 0f || t > 0.16f) return 0f;
        return t < 0.06f ? t / 0.06f : 1f - (t - 0.06f) / 0.1f;
    }
}
