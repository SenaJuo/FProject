using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using P = PrologueBuilder;

/// <summary>
/// 2장 구역 3 「탈출」 — 기록 조회에 걸린 뒤. 일반 경찰은 정문으로 빠지고, 배신자가 부른 정리반·특공대와 싸운다.
///  ① 2층 복도(정리반 셋, 하나는 총) ② 1층 뒤 복도(정리반 셋, 하나는 총) → 뒷문으로 나간다
///  ③ 보관소 북쪽(사람 문 바로 안): 정리반 부반장(중간 보스) + 특공대 셋(방패 둘, 총 하나)
///  ④ 이기면 경찰서 2층 창가에 배신자가 선다(얼굴만, 정체는 3장).
/// 처음엔 모두 숨겨 두고 감독(Chapter2Director)이 경보와 함께 꺼낸다.
/// </summary>
public static partial class Chapter1Builder
{
    class BuiltEscape
    {
        public AlarmLights alarm;
        public CombatZone zoneUpstairs, zoneDownstairs, zoneLot;
        public GameObject[] squads;
        public StoryInteractable backDoorOut;
        public Transform outsideDoor, traitorShot;
        public GameObject traitor;
        public Light traitorLight;
        public PoliceLights[] beacons;
        public EnemyAI subBoss;
    }

    const string TraitorCharacter = "Traitor";

    static void Ch2Escape(Transform parent, Transform triggers, BuiltEscape b)
    {
        var g = new GameObject("Ch2_Zone3_Escape").transform;
        g.SetParent(parent);
        var people = new GameObject("Cleaners").transform;

        // ---------- 경보: 실내 형광등·판을 붉게 ----------
        var interior = parent.Find("StationInterior");
        var alarmGo = new GameObject("StationAlarm");
        alarmGo.transform.SetParent(g);
        var alarm = alarmGo.AddComponent<AlarmLights>();
        var lights = new List<Light>();
        var panels = new List<Renderer>();
        if (interior)
            foreach (Transform t in interior)
            {
                if (t.name == "Fluor" && t.TryGetComponent(out Light l)) lights.Add(l);
                if (t.name == "FluorPanel" && t.TryGetComponent(out Renderer r)) panels.Add(r);
            }
        alarm.lights = lights.ToArray();
        alarm.panels = panels.ToArray();
        alarm.alarmPanel = P.Unlit("StationFluorAlarm", new Color(1f, 0.18f, 0.12f) * 2.2f);
        var siren = alarmGo.AddComponent<AudioSource>();
        siren.spatialBlend = 0f;
        siren.playOnAwake = false;
        siren.volume = 0f;
        alarm.siren = siren;
        b.alarm = alarm;

        // ---------- ① 2층 복도(z 16~20): 계단 쪽에서 올라온 정리반 ----------
        var a1 = Cleaner(people, "Cleaner_2F_A", new Vector3(322f, F2 + 0.02f, 18f), -90f, false);
        var a2 = Cleaner(people, "Cleaner_2F_B", new Vector3(329f, F2 + 0.02f, 17.4f), -90f, false);
        var a3 = Cleaner(people, "Cleaner_2F_Gun", new Vector3(337.5f, F2 + 0.02f, 18.6f), -90f, true);
        b.zoneUpstairs = Zone(triggers, "Zone_2F_Corridor", new Vector3(317f, F2 + 1.5f, 18f), new Vector3(34f, 3f, 4f), a1, a2, a3);

        // ---------- ② 1층 뒤 복도(z 0~5): 뒷문으로 들어온 정리반 ----------
        var d1 = Cleaner(people, "Cleaner_1F_A", new Vector3(318f, 0.02f, 2.5f), 90f, false);
        var d2 = Cleaner(people, "Cleaner_1F_B", new Vector3(310f, 0.02f, 3.4f), 90f, false);
        var d3 = Cleaner(people, "Cleaner_1F_Gun", new Vector3(303.5f, 0.02f, 1.4f), 90f, true);
        b.zoneDownstairs = Zone(triggers, "Zone_1F_Corridor", new Vector3(318f, 1.5f, 2.5f), new Vector3(36f, 3f, 5f), d1, d2, d3);
        b.backDoorOut = P.Interactable(g, "BackDoorOut", new Vector3(300.9f, 1f, 2.5f), new Vector3(1.4f, 2f, 2f), "뒷문으로 나간다");
        b.backDoorOut.oneShot = false; // 싸우는 중이라 막혀도 다시 누를 수 있게
        b.outsideDoor = P.Marker(triggers, "OutsideDoor", new Vector3(70f, 0.05f, StationZ0 - 1.6f), 180f);

        // ---------- ③ 보관소 북쪽: 부반장 + 특공대 ----------
        var boss = P.CreateThug(people, "SubBoss_Cleaner", new Vector3(70f, 0.02f, 90.5f), 0f, "Enforcer");
        SetCombat(boss, false);
        boss.displayName = "정리반 부반장";
        boss.isBoss = true;
        boss.transform.localScale = Vector3.one * 1.12f; // 한 뼘 더 크다
        boss.GetComponent<Health>().maxHealth = 260;
        boss.damage = 11;
        boss.heavyEvery = 2;
        boss.heavyDamage = 24;
        boss.windup = 0.55f;
        boss.heavyWindup = 0.9f;
        boss.attackCooldown = 1.3f;
        boss.recover = 0.5f;
        boss.staggerable = false; // 주먹 한 대로 끊기지 않는다: 완벽 회피로 빈틈을 만든다
        boss.executeEvery = 4; // 처형기: 네 번째마다 예고 없는 큰 주먹(되감으면 보인다)
        boss.executeDamage = 40;
        b.subBoss = boss;
        var s1 = ShieldCop(people, "Swat_Shield_W", new Vector3(64.5f, 0.02f, 92f), 0f);
        var s2 = ShieldCop(people, "Swat_Shield_E", new Vector3(75.5f, 0.02f, 92f), 0f);
        foreach (var s in new[] { s1, s2 }) s.displayName = "특공대";
        var sg = P.CreateThug(people, "Swat_Gunner", new Vector3(70f, 0.02f, 86f), 0f, "RiotCop");
        SetCombat(sg, true);
        sg.displayName = "특공대";
        sg.shotDamage = 16;
        b.zoneLot = Zone(triggers, "Zone_LotNorth", new Vector3(70f, 1.5f, 92f), new Vector3(40f, 3f, 16f), boss, s1, s2, sg);

        // 모두 숨겨 둔다(경보가 울리면 감독이 꺼낸다)
        var all = new List<GameObject>();
        foreach (var e in new[] { a1, a2, a3, d1, d2, d3, boss, s1, s2, sg }) { e.gameObject.SetActive(false); all.Add(e.gameObject); }
        b.squads = all.ToArray();

        // ---------- 직원 주차장 경찰차: 경보 때 경광등이 켜진다 ----------
        var beacons = new List<PoliceLights>();
        var station = parent.Find("PoliceStation");
        if (station)
            foreach (Transform car in station)
            {
                if (!car.name.StartsWith("PoliceCar")) continue;
                float top = WorldBounds(car.gameObject).max.y - car.position.y;
                var red = P.AddLight(car, "BeaconRed", new Vector3(-0.35f, top + 0.15f, 0.1f), LightType.Point, new Color(1f, 0.08f, 0.05f), 0f, 9f, local: true);
                var blue = P.AddLight(car, "BeaconBlue", new Vector3(0.35f, top + 0.15f, 0.1f), LightType.Point, new Color(0.1f, 0.25f, 1f), 0f, 9f, local: true);
                var pl = car.gameObject.AddComponent<PoliceLights>();
                pl.red = red;
                pl.blue = blue;
                pl.enabled = false;
                beacons.Add(pl);
            }
        b.beacons = beacons.ToArray();

        // ---------- ④ 배신자의 창(경찰서 뒷면 2층, 뒷문 동쪽) ----------
        TraitorWindow(g, b);
    }

    /// <summary>정리반 조직원(검은 정장 조직원 모델). 총잡이는 멀리서 겨눈다</summary>
    static EnemyAI Cleaner(Transform parent, string name, Vector3 pos, float yaw, bool gunner)
    {
        var ai = P.CreateThug(parent, name, pos, yaw, "Enforcer");
        SetCombat(ai, gunner);
        ai.displayName = "정리반";
        ai.GetComponent<Health>().maxHealth = 110;
        return ai;
    }

    /// <summary>
    /// 경찰서 뒷면(z 106, 남쪽을 본다) 2층에 툭 튀어나온 창 하나: 바닥·천장·옆벽·따뜻한 안벽 · 창틀.
    /// 처음엔 불이 꺼져 있고 사람도 없다 — 이긴 뒤 감독이 불을 켜고 배신자를 세운다.
    /// </summary>
    static void TraitorWindow(Transform parent, BuiltEscape b)
    {
        var g = new GameObject("TraitorWindow").transform;
        g.SetParent(parent);
        const float x = 78f, floorY = 4.6f, h = 2.7f, depth = 0.9f, w = 2.6f;
        float zBack = StationZ0 - 0.02f, zFront = zBack - depth, zc = (zBack + zFront) / 2f, yc = floorY + h / 2f;
        var frame = P.Lit("TraitorWindowFrame", null, new Color(0.3f, 0.32f, 0.37f), 0.35f, metallic: 0.3f); // 건물 벽과 비슷한 회청색(검은 상자처럼 튀지 않게)
        var inner = P.Lit("TraitorOfficeWall", null, new Color(0.58f, 0.52f, 0.44f), 0.25f);
        P.Box(g, "BayFloor", new Vector3(x, floorY - 0.06f, zc), new Vector3(w, 0.12f, depth), frame, Vector2.one, collider: false);
        P.Box(g, "BayCeiling", new Vector3(x, floorY + h + 0.06f, zc), new Vector3(w, 0.12f, depth), frame, Vector2.one, collider: false);
        P.Box(g, "BaySide_W", new Vector3(x - w / 2f + 0.05f, yc, zc), new Vector3(0.1f, h, depth), frame, Vector2.one, collider: false);
        P.Box(g, "BaySide_E", new Vector3(x + w / 2f - 0.05f, yc, zc), new Vector3(0.1f, h, depth), frame, Vector2.one, collider: false);
        P.Box(g, "BayBack", new Vector3(x, yc, zBack - 0.03f), new Vector3(w - 0.2f, h, 0.04f), inner, Vector2.one, collider: false);
        // 창틀: 아래 턱(허리 높이) · 위 · 가운데에서 비켜 선 세로살 하나(얼굴을 가리지 않게)
        P.Box(g, "Sill", new Vector3(x, floorY + 0.5f, zFront), new Vector3(w, 1.0f, 0.08f), frame, Vector2.one, collider: false);
        P.Box(g, "Header", new Vector3(x, floorY + h - 0.08f, zFront), new Vector3(w, 0.16f, 0.08f), frame, Vector2.one, collider: false);
        P.Box(g, "Mullion", new Vector3(x - 0.75f, yc + 0.5f, zFront), new Vector3(0.06f, h - 1.0f, 0.08f), frame, Vector2.one, collider: false);
        // 얼굴 앞 위쪽에서 비춘다(안벽 바로 앞에 두면 벽만 하얗게 타고 얼굴은 어둡다)
        var lamp = P.AddLight(g, "TraitorLamp", new Vector3(x + 0.1f, floorY + 2.45f, zFront + 0.1f), LightType.Point, new Color(1f, 0.84f, 0.64f), 0.7f, 3.5f); // 약하게: 얼굴이 하얗게 타지 않게
        lamp.enabled = false;
        b.traitorLight = lamp;
        // 얼굴 근접: 창 앞 허공에서 살짝 올려다본다
        // 상반신 + 창틀(얼굴 초근접은 생성 텍스처의 머리 경계 얼룩이 드러난다)
        b.traitorShot = P.Marker(g, "TraitorShot", new Vector3(x - 0.3f, floorY + 1.2f, zFront - 3.2f), 0f);

        // 배신자: 걷지도 싸우지도 않는다 — 모델 + 서 있는 동작만
        string prefab = BaroqueCharacterSetup.CharacterRoot + $"/{TraitorCharacter}/{TraitorCharacter}_Model.prefab";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        var root = new GameObject("Traitor").transform;
        root.SetParent(g);
        root.SetPositionAndRotation(new Vector3(x + 0.25f, floorY, zc + 0.05f), Quaternion.Euler(0f, 180f, 0f));
        if (asset)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var anim = model.GetComponent<Animator>();
            if (anim) anim.runtimeAnimatorController = BaroqueCharacterSetup.EnsureIKController();
        }
        else Debug.LogWarning($"[2장] 배신자 모델 없음: {prefab}");
        root.gameObject.SetActive(false);
        b.traitor = root.gameObject;
    }
}
