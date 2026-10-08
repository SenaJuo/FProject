using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 타격감 연출을 한곳에서 낸다. 때리는 쪽 코드는 Impact(…) 한 번만 부르면 된다.
///  · 소리: 휘두르는 바람 소리, 세기별 타격음, 맞은 사람 신음 — Resources/Sfx/Hit/{이름}-{번호}.wav가 있으면 그것을(여러 개면 무작위), 없으면 코드로 만든 소리를 쓴다.
///    매번 음 높이를 조금씩 바꿔 같은 소리가 반복되는 느낌을 없앤다.
///  · 히트스톱: 맞는 순간 세상 전체를 거의 멈춘다(세기별 0.07/0.10/0.16초). 멈춘 동안 맞은 몸을 떨게 한다
///  · 카메라: 맞는 방향으로 짧게 흔들고, 강한 공격은 화면을 살짝 당긴다(시야각 줄이기)
///  · 번쩍임: 맞은 몸이 하얗게(BodyTint)
///  · 효과: 맞는 자리에 번쩍 빛, 퍼지는 충격 고리, 튀는 물방울(비 오는 밤)
///  · 패드 진동
///  · 마무리: 마지막 적을 쓰러뜨린 한 방은 느린 화면 + 테이프가 늘어지는 소리 + 화면이 테이프처럼 일그러진다
/// </summary>
public class HitFeel : MonoBehaviour
{
    public enum Tier { Light, Medium, Heavy }

    static readonly float[] StopSeconds = { 0.07f, 0.10f, 0.16f };
    static readonly float[] ShakeAmount = { 0.05f, 0.09f, 0.18f };
    static readonly float[] ShakeSeconds = { 0.14f, 0.18f, 0.28f };
    static readonly float[] FovKick = { 0f, 1.5f, 5f };
    static readonly int[] Droplets = { 8, 14, 26 };
    static readonly float[] Rumble = { 0.25f, 0.45f, 0.9f };
    const float StopScale = 0.03f;

    static HitFeel instance;
    static HitFeel I { get { if (!instance) instance = new GameObject("HitFeel").AddComponent<HitFeel>(); return instance; } }

    ParticleSystem flash, ring, drops;
    Coroutine timeRoutine, rumbleRoutine;
    float ownedScale = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() { instance = null; soundCache.Clear(); }

    // ---------- 바깥에서 부르는 곳 ----------

    /// <summary>주먹·발을 뻗기 시작할 때(바람 소리)</summary>
    public static void Swing(Tier tier, Vector3 at) =>
        Play(tier == Tier.Heavy ? "whoosh_heavy" : "whoosh", at, 0.55f + 0.15f * (int)tier, 0.08f);

    /// <summary>맞은 순간. dir = 때린 쪽에서 맞은 쪽으로. finisher = 마지막 적을 쓰러뜨린 한 방</summary>
    public static void Impact(Tier tier, Vector3 point, Vector3 dir, GameObject victim, bool victimIsPlayer, bool finisher = false, bool bullet = false)
    {
        var h = I;
        int t = (int)tier;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        // 소리: 타격음 + 맞은 사람 신음(적은 조금 늦게, 반 정도만)
        Play(bullet ? "impact_bullet" : tier == Tier.Light ? "impact_light" : tier == Tier.Medium ? "impact_mid" : "impact_heavy", point, 0.85f + 0.08f * t, 0.1f);
        if (victimIsPlayer || Random.value < 0.6f || tier == Tier.Heavy)
            h.StartCoroutine(h.Delayed(0.06f, () => Play(victimIsPlayer ? "grunt_player" : "grunt", point, 0.5f + 0.1f * t, 0.12f)));

        // 히트스톱 → (마무리면) 느린 화면
        h.TimeSequence(StopSeconds[t] * (victimIsPlayer ? 0.8f : 1f), finisher);
        if (victim) h.StartCoroutine(h.Jitter(victim, StopSeconds[t], 0.012f + 0.01f * t, dir));

        // 카메라
        ThirdPersonCamera.Shake(dir, ShakeAmount[t] * (victimIsPlayer ? 1.2f : 1f), ShakeSeconds[t]);
        if (FovKick[t] > 0f || finisher) ThirdPersonCamera.Kick(finisher ? 8f : FovKick[t], finisher ? 0.6f : 0.25f);

        // 효과
        h.Burst(point, dir, t, bullet);

        // 진동
        h.Vibrate(Rumble[t] * (victimIsPlayer ? 1.1f : 1f), 0.08f + 0.05f * t);

        if (finisher) h.StartCoroutine(h.TapeStretch(point));
    }

    /// <summary>잠깐 느린 화면(완벽 회피 등): scale 배속으로 seconds(실제 초) 동안, 끝에 0.2초 걸쳐 돌아온다</summary>
    public static void SlowMotion(float scale, float seconds) => I.StartSlow(scale, seconds);

    void StartSlow(float scale, float seconds)
    {
        if (timeRoutine != null) StopCoroutine(timeRoutine);
        timeRoutine = StartCoroutine(SlowRoutine(scale, seconds));
    }

    IEnumerator SlowRoutine(float scale, float seconds)
    {
        if (Time.timeScale < 0.99f && !Mathf.Approximately(Time.timeScale, ownedScale)) yield break; // 메뉴·되감기가 시간을 쥐고 있다
        Set(scale);
        yield return new WaitForSecondsRealtime(seconds);
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / 0.2f) { if (!Owned()) yield break; Set(Mathf.Lerp(scale, 1f, k)); yield return null; }
        if (Owned()) Time.timeScale = 1f;
        ownedScale = -1f;
        timeRoutine = null;
    }

    /// <summary>예전 방식 호출(공격 판정 코드의 hitStop 값)</summary>
    public static void HitStop(float seconds) => I.TimeSequence(seconds, false);

    // ---------- 시간 ----------

    void TimeSequence(float stop, bool finisher)
    {
        if (timeRoutine != null) StopCoroutine(timeRoutine);
        timeRoutine = StartCoroutine(TimeRoutine(stop, finisher));
    }

    IEnumerator TimeRoutine(float stop, bool finisher)
    {
        // 되감기·메뉴·사망 화면이 시간을 쥐고 있으면 건드리지 않는다
        if (Time.timeScale < 0.99f && !Mathf.Approximately(Time.timeScale, ownedScale)) yield break;
        Set(StopScale);
        yield return new WaitForSecondsRealtime(stop);
        if (finisher)
        {
            // 멈춘 사이 인벤토리·되감기가 시간을 가져갔으면 슬로모션으로 덮어쓰지 않는다
            if (!Owned()) { ownedScale = -1f; timeRoutine = null; yield break; }
            Set(0.25f);
            yield return new WaitForSecondsRealtime(0.55f);
            for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / 0.25f) { if (!Owned()) yield break; Set(Mathf.Lerp(0.25f, 1f, k)); yield return null; }
        }
        if (Owned()) Time.timeScale = 1f;
        ownedScale = -1f;
        timeRoutine = null;
    }

    void Set(float s) { Time.timeScale = s; ownedScale = s; }

    /// <summary>
    /// 일시정지: 타격 멈춤·마무리 슬로모션이 시간을 쥐고 있으면 끝낸다(true). 메뉴를 닫을 때 정상 속도로 돌아가야 하기 때문 —
    /// 그대로 두면 이 코루틴은 "남이 시간을 가져갔다"며 손을 떼고, 메뉴는 멈춤 속도를 되살려 게임이 느린 채로 굳었다
    /// </summary>
    public static bool ReleaseTime()
    {
        if (!instance || instance.timeRoutine == null || !instance.Owned()) return false;
        instance.StopCoroutine(instance.timeRoutine);
        instance.timeRoutine = null;
        instance.ownedScale = -1f;
        return true;
    }
    bool Owned() => Mathf.Approximately(Time.timeScale, ownedScale);

    IEnumerator Delayed(float seconds, System.Action action)
    {
        yield return new WaitForSecondsRealtime(seconds);
        action();
    }

    /// <summary>멈춘 동안 맞은 몸(모델)만 맞은 방향으로 잘게 떤다</summary>
    IEnumerator Jitter(GameObject victim, float seconds, float amount, Vector3 dir)
    {
        var anim = victim.GetComponentInChildren<Animator>();
        if (!anim) yield break;
        var t = anim.transform;
        // 떨림이 겹치면(두 발이 거의 동시에 맞음) 처음 떨기 전 자리를 함께 쓴다 — 떨린 자리를 "제자리"로 기억해 몸이 조금씩 밀려나지 않게
        if (jitters.TryGetValue(t, out var running)) jitters[t] = (running.rest, running.count + 1);
        else jitters[t] = (t.localPosition, 1);
        Vector3 rest = jitters[t].rest;
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until && t)
        {
            float k = (until - Time.unscaledTime) / seconds;
            Vector3 world = dir * (Mathf.Sin(Time.unscaledTime * 120f) * amount * k) + Random.insideUnitSphere * amount * 0.4f * k;
            t.localPosition = rest + t.parent.InverseTransformVector(world);
            yield return null;
        }
        if (t) t.localPosition = rest;
        if (jitters.TryGetValue(t, out var left))
        {
            if (left.count <= 1) jitters.Remove(t);
            else jitters[t] = (left.rest, left.count - 1);
        }
    }

    static readonly Dictionary<Transform, (Vector3 rest, int count)> jitters = new();

    IEnumerator TapeStretch(Vector3 at)
    {
        Play("tape_stretch", at, 0.7f, 0f, spatial: 0f);
        var fx = FindAnyObjectByType<RewindEffect>();
        if (!fx) yield break;
        // 테이프가 늘어지듯 화면이 잠깐 바랬다가 돌아온다
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / 0.9f)
        {
            if (fx.Weight > 0.5f) yield break; // 진짜 되감기가 시작됐으면 손대지 않는다
            fx.SetWeight(Mathf.Sin(k * Mathf.PI) * 0.35f);
            yield return null;
        }
        fx.SetWeight(0f);
    }

    // ---------- 진동 ----------

    void Vibrate(float strength, float seconds)
    {
        var pad = Gamepad.current;
        if (pad == null) return;
        if (rumbleRoutine != null) StopCoroutine(rumbleRoutine);
        rumbleRoutine = StartCoroutine(RumbleRoutine(pad, strength, seconds));
    }

    IEnumerator RumbleRoutine(Gamepad pad, float strength, float seconds)
    {
        pad.SetMotorSpeeds(strength * 0.8f, strength);
        yield return new WaitForSecondsRealtime(seconds);
        pad.SetMotorSpeeds(0f, 0f);
        rumbleRoutine = null;
    }

    void OnDisable() { Gamepad.current?.SetMotorSpeeds(0f, 0f); }

    // ---------- 효과 ----------

    void Burst(Vector3 point, Vector3 dir, int tier, bool bullet)
    {
        if (!flash) Build();
        var e = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = false };
        e.startSize = 0.35f + 0.18f * tier; e.startLifetime = 0.07f + 0.02f * tier;
        e.startColor = bullet ? new Color(1f, 0.55f, 0.45f, 1f) : new Color(1f, 0.93f, 0.75f, 1f);
        flash.Emit(e, 1);
        if (tier > 0)
        {
            var r = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = false, startSize = 0.25f, startLifetime = 0.16f + 0.04f * tier };
            r.startColor = new Color(1f, 1f, 1f, 0.6f + 0.2f * tier);
            ring.Emit(r, 1);
        }
        for (int i = 0; i < Droplets[tier]; i++)
        {
            Vector3 v = (dir + Random.insideUnitSphere * 0.75f + Vector3.up * 0.25f).normalized * Random.Range(2.5f, 4.5f + tier * 1.5f);
            var d = new ParticleSystem.EmitParams { position = point + Random.insideUnitSphere * 0.05f, velocity = v, applyShapeToPosition = false };
            d.startSize = Random.Range(0.015f, 0.035f); d.startLifetime = Random.Range(0.25f, 0.45f);
            d.startColor = new Color(0.85f, 0.92f, 1f, 0.9f);
            drops.Emit(d, 1);
        }
    }

    void Build()
    {
        var soft = Texture(64, (x, y, r) => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
        var ringTex = Texture(128, (x, y, r) => Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.12f));
        flash = Make("HitFlash", soft, true, sizeOverLife: AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
        ring = Make("HitRing", ringTex, true, sizeOverLife: AnimationCurve.Linear(0f, 0.3f, 1f, 4.5f), fade: true);
        drops = Make("HitDrops", soft, false, fade: true, gravity: 1.2f, stretch: true);
    }

    ParticleSystem Make(string name, Texture2D tex, bool additive, AnimationCurve sizeOverLife = null, bool fade = false, float gravity = 0f, bool stretch = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.useUnscaledTime = true;          // 히트스톱 중에도 번쩍임은 보인다
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.maxParticles = 300;
        main.gravityModifier = gravity;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        if (sizeOverLife != null) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLife); }
        if (fade)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Surface", 1f);                  // 반투명
        mat.SetFloat("_Blend", additive ? 2f : 0f);    // 더하기 / 알파
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", additive ? (int)UnityEngine.Rendering.BlendMode.One : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        r.sharedMaterial = mat;
        if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f; }
        ps.Play();
        return ps;
    }

    static Texture2D Texture(int size, System.Func<int, int, float, float> alpha)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
            px[y * size + x] = new Color(1f, 1f, 1f, alpha(x, y, r));
        }
        t.SetPixels32(px); t.Apply();
        return t;
    }

    // ---------- 소리 ----------

    static readonly Dictionary<string, AudioClip[]> soundCache = new();
    static AudioClip[] loadedHit;

    /// <summary>Resources/Sfx/Hit의 "{이름}-{번호}" 소리 중 하나(없으면 null). 회복약처럼 다른 곳에서 쓰는 짧은 소리용</summary>
    public static AudioClip Clip(string name)
    {
        var clips = Clips(name);
        return clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;
    }

    static AudioClip[] Clips(string name)
    {
        if (!soundCache.TryGetValue(name, out var clips))
        {
            loadedHit ??= Resources.LoadAll<AudioClip>("Sfx/Hit"); // 폴더 전체는 한 번만 읽는다(첫 타격마다 끊기지 않게)
            var loaded = loadedHit;
            var list = new List<AudioClip>();
            // 파일 이름 = "{이름}-{번호}" (예: whoosh-2). 앞부분이 정확히 같은 것만(whoosh가 whoosh_heavy를 집지 않게)
            foreach (var c in loaded) { int dash = c.name.LastIndexOf('-'); if ((dash > 0 ? c.name.Substring(0, dash) : c.name) == name) list.Add(c); }
            if (list.Count == 0) { var p = ProceduralSfx.Hit(name); if (p) list.Add(p); }
            soundCache[name] = clips = list.ToArray();
        }
        return clips;
    }

    /// <summary>이름에 맞는 소리 하나를 무작위로 골라 음 높이를 조금 바꿔 재생한다</summary>
    static void Play(string name, Vector3 at, float volume, float pitchJitter, float spatial = 0.5f)
    {
        var clips = Clips(name);
        if (clips.Length == 0) return;
        ProceduralSfx.PlayAt(clips[Random.Range(0, clips.Length)], at, volume, spatial, 1f + Random.Range(-pitchJitter, pitchJitter));
    }
}
