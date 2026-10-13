using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 테이프 능력(R → E): 끼운 테이프의 장르에 따라 잠깐 힘이 생긴다. 되감기 1회분을 쓰지만 테이프는 계속 재생된다
/// (되감기 Q는 살아나는 대신 재생용 테이프를 다시는 못 듣게 만든다).
///  시티팝·신스 「네온 러시」 4초: 세상이 1/4로 느려지고 레이만 제 속도 — 뒤로 형광 잔상이 남는다.
///  재즈 「즉흥」 8초: 무적 회피가 두 배, 완벽 회피 판정이 넓고 반격이 세다.
///  소울·블루스 「그루브」 8초: 주먹 피해 1.5배, 방패를 무시한다.
///  앰비언트 「고요」 10초: 받는 피해 절반, 적을 쓰러뜨릴 때마다 체력 회복.
/// 지속 시간은 실제 시간(메뉴·되감기로 시간이 멈춘 동안은 줄지 않는다).
/// </summary>
public class TapeAbility : MonoBehaviour
{
    public const float WorldSlow = 0.25f;

    /// <summary>지금 켜진 능력(없으면 None)</summary>
    public static TapeGenre Current { get; private set; }
    public static float Remaining { get; private set; }
    public static float Duration { get; private set; }

    public static string Name(TapeGenre g) => g switch
    {
        TapeGenre.CityPop => "네온 러시",
        TapeGenre.Jazz => "즉흥",
        TapeGenre.Soul => "그루브",
        TapeGenre.Ambient => "고요",
        _ => "",
    };

    public static float Seconds(TapeGenre g) => g switch
    {
        TapeGenre.CityPop => 4f,
        TapeGenre.Jazz => 8f,
        TapeGenre.Soul => 8f,
        TapeGenre.Ambient => 10f,
        _ => 0f,
    };

    public static Color Tint(TapeGenre g) => g switch
    {
        TapeGenre.CityPop => new Color(0.3f, 1f, 0.85f),
        TapeGenre.Jazz => new Color(1f, 0.78f, 0.35f),
        TapeGenre.Soul => new Color(1f, 0.35f, 0.3f),
        TapeGenre.Ambient => new Color(0.55f, 0.75f, 1f),
        _ => Color.white,
    };

    PlayerController player;
    Health health;
    Animator body;
    // 되돌릴 원래 값
    float iFrames, counterWindow, counterMultiplier, dodgeDistance, animSpeed;
    int attackPower, deadAtStart;
    // 화면: 장르 색으로 살짝 물들고 가장자리가 갈라진다
    Volume volume;
    ColorAdjustments color;
    // 네온 러시 잔상
    static Material ghostMaterial;
    readonly List<(GameObject go, MeshRenderer r, float born)> ghosts = new();
    readonly Queue<GameObject> pool = new();
    float nextGhost;
    MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        player = GetComponent<PlayerController>();
        health = GetComponent<Health>();
        body = player ? player.animator : GetComponentInChildren<Animator>();
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        color = profile.Add<ColorAdjustments>(true);
        color.saturation.Override(-25f);
        color.postExposure.Override(0.5f); // 색 필터가 화면을 어둡게 하니 그만큼 밝힌다
        color.colorFilter.Override(Color.white);
        profile.Add<ChromaticAberration>(true).intensity.Override(0.7f);
        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 90f;
        volume.weight = 0f;
        volume.sharedProfile = profile;
        GameManager.PlayerRespawned += EndNow;
    }

    void OnDestroy()
    {
        GameManager.PlayerRespawned -= EndNow;
        EndNow();
    }

    /// <summary>능력을 켠다(되감기 횟수는 부르는 쪽(Walkman)이 줄인다)</summary>
    public void Activate(TapeGenre genre)
    {
        if (genre == TapeGenre.None) return;
        EndNow();
        Current = genre;
        Duration = Remaining = Seconds(genre);
        color.colorFilter.Override(Color.Lerp(Color.white, Tint(genre), 0.55f));
        switch (genre)
        {
            case TapeGenre.CityPop:
                animSpeed = body ? body.speed : 1f;
                break;
            case TapeGenre.Jazz:
                iFrames = player.dodgeIFrames; counterWindow = player.counterWindow; counterMultiplier = player.counterDamageMultiplier; dodgeDistance = player.dodgeDistance;
                player.dodgeIFrames = iFrames * 2f;
                player.counterWindow = counterWindow * 1.6f;
                player.counterDamageMultiplier = counterMultiplier * 1.5f;
                player.dodgeDistance = dodgeDistance * 1.15f;
                break;
            case TapeGenre.Soul:
                attackPower = player.attackPower;
                player.attackPower = Mathf.RoundToInt(attackPower * 1.5f);
                break;
            case TapeGenre.Ambient:
                health.damageTakenScale = 0.5f;
                deadAtStart = DeadEnemies();
                break;
        }
        var whoosh = Resources.Load<AudioClip>("Sfx/Hit/whoosh_heavy-2");
        if (whoosh) ProceduralSfx.PlayAt(whoosh, transform.position, 0.9f, 0f, genre == TapeGenre.CityPop ? 0.7f : 1f);
    }

    /// <summary>바로 끈다(되감기·쓰러짐·장면 이동)</summary>
    public void EndNow()
    {
        if (Current == TapeGenre.None) return;
        switch (Current)
        {
            case TapeGenre.CityPop:
                PlayerController.SelfTime = 1f;
                if (body) body.speed = animSpeed;
                if (Mathf.Approximately(Time.timeScale, WorldSlow)) Time.timeScale = 1f;
                break;
            case TapeGenre.Jazz:
                player.dodgeIFrames = iFrames; player.counterWindow = counterWindow; player.counterDamageMultiplier = counterMultiplier; player.dodgeDistance = dodgeDistance;
                break;
            case TapeGenre.Soul:
                player.attackPower = attackPower;
                break;
            case TapeGenre.Ambient:
                health.damageTakenScale = 1f;
                break;
        }
        Current = TapeGenre.None;
        Remaining = 0f;
    }

    void Update()
    {
        float target = Current != TapeGenre.None ? 1f : 0f;
        volume.weight = Mathf.MoveTowards(volume.weight, target, Time.unscaledDeltaTime / 0.25f);
        FadeGhosts();
        if (Current == TapeGenre.None) return;

        // 메뉴·되감기·사망 화면이 시간을 멈춘 동안은 줄지 않는다
        bool frozen = TimeRewind.PlayingBack || Time.timeScale <= 0.001f || StoryState.MenuOpen;
        if (frozen) return;
        if (health.IsDead) { EndNow(); return; }
        Remaining -= Time.unscaledDeltaTime;

        switch (Current)
        {
            case TapeGenre.CityPop:
                // 다른 장치(타격 멈춤 등)가 잠깐 바꿨어도 다시 느리게: 세상은 1/4, 레이는 그대로
                Time.timeScale = WorldSlow;
                PlayerController.SelfTime = 1f / WorldSlow;
                if (body) body.speed = animSpeed / WorldSlow;
                if (Time.unscaledTime >= nextGhost) { nextGhost = Time.unscaledTime + 0.05f; SpawnGhost(); }
                break;
            case TapeGenre.Ambient:
                int dead = DeadEnemies();
                if (dead > deadAtStart) { health.Heal(8 * (dead - deadAtStart)); deadAtStart = dead; }
                break;
        }
        if (Remaining <= 0f) EndNow();
    }

    static int DeadEnemies()
    {
        int n = 0;
        foreach (var e in EnemyAI.All) if (e && e.Health != null && e.Health.IsDead) n++;
        return n;
    }

    // ---------- 네온 러시 잔상 ----------

    void SpawnGhost()
    {
        if (!ghostMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            ghostMaterial = new Material(shader) { name = "NeonGhost", renderQueue = 3000 };
            ghostMaterial.SetFloat("_Surface", 1f);
            ghostMaterial.SetFloat("_Blend", 2f); // 더하기: 겹칠수록 빛난다
            ghostMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
            ghostMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            ghostMaterial.SetFloat("_ZWrite", 0f);
            ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghostMaterial.SetOverrideTag("RenderType", "Transparent");
        }
        block ??= new MaterialPropertyBlock();
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
            var go = pool.Count > 0 ? pool.Dequeue() : NewGhost();
            var mf = go.GetComponent<MeshFilter>();
            if (!mf.sharedMesh) mf.sharedMesh = new Mesh { name = "Ghost" };
            smr.BakeMesh(mf.sharedMesh, true);
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.SetActive(true);
            ghosts.Add((go, go.GetComponent<MeshRenderer>(), Time.unscaledTime));
        }
    }

    GameObject NewGhost()
    {
        var go = new GameObject("NeonGhost") { hideFlags = HideFlags.DontSave };
        go.AddComponent<MeshFilter>();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = ghostMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    void FadeGhosts()
    {
        const float life = 0.4f;
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            var (go, r, born) = ghosts[i];
            float age = Time.unscaledTime - born;
            if (age >= life || !go) { if (go) { go.SetActive(false); pool.Enqueue(go); } ghosts.RemoveAt(i); continue; }
            // 무지개처럼 색이 흘러가며 빠르게 사라진다
            float hue = Mathf.Repeat(born * 1.7f, 1f);
            var c = Color.HSVToRGB(Mathf.Lerp(0.42f, 0.92f, hue), 0.8f, 1f) * (1f - age / life) * 0.45f;
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }
    }
}
