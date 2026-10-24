using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 진행 저장(파일): 저장 지점(1장 시작·가게·구역 시작점)에 닿을 때마다 기록하고, 타이틀의 '이어하기'로 그 자리부터 다시 시작한다.
/// 담는 것: 어느 장면의 어느 저장 지점인지, 테이프(되감기 횟수·재생 위치)와 끼운 테이프, 회복약, 주운 테이프 자리, 가게에서 바꾼 테이프.
/// 체력은 저장 지점에서 늘 가득 차므로 담지 않는다. 지나온 구역의 적은 장면 감독이 치운다.
/// </summary>
public static class SaveGame
{
    [Serializable]
    public class TapeState { public CassetteTape tape; public float position; public bool rewound; }

    [Serializable]
    public class PickupState { public string name; public bool taken; public int potions; }

    [Serializable]
    public class Data
    {
        public int version = Version;
        public string scene;
        public string checkpoint;
        public string savedAt;
        public List<TapeState> tapes = new();
        public int inserted = -1;
        public int potions;
        public List<PickupState> pickups = new();
        public List<string> traded = new(); // "가게 이름|목록 번호"
    }

    /// <summary>
    /// 저장 파일. 에디터와 빌드는 같은 폴더(LocalLow/회사/제품)를 쓰므로 에디터는 따로 둔다
    /// (2026-10-19: 에디터에서 2장까지 간 저장이 빌드의 '이어하기'에 그대로 나왔다)
    /// </summary>
    public static string FilePath => Path.Combine(Application.persistentDataPath, Application.isEditor ? "save_editor.json" : "save.json");

    /// <summary>
    /// 저장 형식 판. 2 = 2026-10-19 고친 뒤(새 게임이 저장을 새로 쓰고, 에디터는 따로 쓴다).
    /// 그 전 판 파일은 에디터 · 시험이 남긴 것일 수 있어 없는 것으로 본다(처음 켰는데 '이어하기'가 뜨고 2장부터 시작했다).
    /// </summary>
    public const int Version = 2;

    public static bool HasSave => Read() is { } d && SceneLoader.Has(d.scene); // 이 빌드에 없는 장면의 저장(체험판)은 없는 것으로

    /// <summary>타이틀에서 시작했을 때만 저장한다(에디터에서 장면을 바로 틀거나 시험 도구가 돌 때 실제 저장을 덮어쓰지 않게)</summary>
    public static bool Enabled;

    /// <summary>'이어하기'로 불러와 장면 감독이 받아 갈 저장 내용(받아 가면 비운다)</summary>
    static Data pending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void OnPlayStart() { pending = null; Enabled = false; }

    public static Data Read()
    {
        try
        {
            string json = LoadText();
            if (string.IsNullOrEmpty(json)) return null;
            var d = JsonUtility.FromJson<Data>(json);
            return d != null && d.version >= Version && !string.IsNullOrEmpty(d.scene) ? d : null;
        }
        catch (Exception e) { Debug.LogWarning("저장 파일을 읽지 못했다: " + e.Message); return null; }
    }

    /// <summary>지금 상태를 저장 지점 이름과 함께 기록한다</summary>
    public static void Write(string checkpoint, Walkman walkman)
    {
        if (!Enabled) return;
        var d = new Data { scene = SceneManager.GetActiveScene().name, checkpoint = checkpoint, savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        if (walkman)
        {
            foreach (var t in walkman.tapes) d.tapes.Add(new TapeState { tape = t, position = t.position, rewound = t.rewound });
            d.inserted = walkman.tapes.IndexOf(walkman.Inserted);
            d.potions = walkman.potions;
        }
        foreach (var p in UnityEngine.Object.FindObjectsByType<TapePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            d.pickups.Add(new PickupState { name = p.name, taken = !p.HasTape, potions = p.gameObject.activeSelf ? p.potions : 0 });
        foreach (var s in UnityEngine.Object.FindObjectsByType<TapeShop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            for (int i = 0; i < s.stock.Count; i++) if (s.stock[i].traded) d.traded.Add($"{s.shopName}|{i}");
        try { StoreText(JsonUtility.ToJson(d, true)); }
        catch (Exception e) { Debug.LogWarning("저장하지 못했다: " + e.Message); }
    }

    /// <summary>타이틀 '새 게임': 예전 저장을 지우고 첫 장면부터 새로 기록한다(프롤로그에는 저장 지점이 없어, 예전 저장이 남아 '이어하기'가 엉뚱한 장으로 갔다)</summary>
    public static void StartNew(string firstScene)
    {
        Enabled = true;
        pending = null;
        try { StoreText(JsonUtility.ToJson(new Data { scene = firstScene, checkpoint = "start", savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") }, true)); }
        catch (Exception e) { Debug.LogWarning("저장하지 못했다: " + e.Message); }
    }

    /// <summary>타이틀 '이어하기': 저장된 장면을 열고, 장면 감독이 저장 지점부터 시작하게 넘겨준다</summary>
    public static bool Continue()
    {
        var d = Read();
        if (d == null || string.IsNullOrEmpty(d.scene) || !SceneLoader.Has(d.scene)) return false;
        pending = d;
        Enabled = true;
        SceneLoader.Load(d.scene);
        return true;
    }

    /// <summary>
    /// 장이 끝나 다음 장으로 넘어갈 때: 테이프·회복약을 그대로 들고 간다(다음 장 감독이 TakePending으로 받는다).
    /// 타이틀에서 시작한 게임이면 다음 장 첫 저장 지점으로 파일에도 기록한다.
    /// </summary>
    public static void CarryOver(string nextScene, string checkpoint, Walkman walkman)
    {
        var d = new Data { scene = nextScene, checkpoint = checkpoint, savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        if (walkman)
        {
            foreach (var t in walkman.tapes) d.tapes.Add(new TapeState { tape = t, position = t.position, rewound = t.rewound });
            d.inserted = walkman.tapes.IndexOf(walkman.Inserted);
            d.potions = walkman.potions;
        }
        pending = d;
        if (!Enabled) return;
        try { StoreText(JsonUtility.ToJson(d, true)); }
        catch (Exception e) { Debug.LogWarning("저장하지 못했다: " + e.Message); }
    }

    /// <summary>장면 감독이 시작할 때 부른다: 이어하기로 왔으면 저장 내용을, 아니면 null</summary>
    public static Data TakePending(string scene)
    {
        if (pending == null || pending.scene != scene) return null;
        var d = pending;
        pending = null;
        return d;
    }

    /// <summary>테이프·회복약·주운 자리·가게를 저장 때 상태로 되돌린다</summary>
    public static void ApplyInventory(Data d, Walkman walkman)
    {
        if (walkman)
        {
            walkman.tapes.Clear();
            foreach (var s in d.tapes)
            {
                var t = s.tape;
                t.position = s.position;
                t.rewound = s.rewound;
                walkman.tapes.Add(t);
            }
            walkman.potions = d.potions;
            walkman.EquipIndex(d.inserted);
        }
        var byName = new Dictionary<string, PickupState>();
        foreach (var p in d.pickups) byName[p.name] = p;
        foreach (var p in UnityEngine.Object.FindObjectsByType<TapePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (byName.TryGetValue(p.name, out var s)) p.RestoreState(s.taken, s.potions);
        foreach (var shop in UnityEngine.Object.FindObjectsByType<TapeShop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            for (int i = 0; i < shop.stock.Count; i++) shop.stock[i].traded = d.traded.Contains($"{shop.shopName}|{i}");
    }

    public static void Delete()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PlayerPrefs.DeleteKey(WebKey);
        PlayerPrefs.Save();
#else
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
#endif
    }

    // ---------- 저장 자리 ----------
    // 웹(itch.io, 2026-10-24): 브라우저에서는 파일을 써도 바로 디스크(IndexedDB)에 남지 않는다 → PlayerPrefs에 둔다(Save가 곧바로 남긴다)
    const string WebKey = "savegame";

    static string LoadText()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return PlayerPrefs.GetString(WebKey, "");
#else
        return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
#endif
    }

    static void StoreText(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PlayerPrefs.SetString(WebKey, json);
        PlayerPrefs.Save();
#else
        File.WriteAllText(FilePath, json);
#endif
    }
}
