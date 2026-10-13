using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 진행 저장 시험(플레이 중 아무 장면에서): 저장 지점 넷(가게 1·구역 2·가게 2·구역 3)마다 저장 파일을 만들어 '이어하기'를 하고,
///  ① 그 자리·그 단계에서 시작하는가 ② 테이프·회복약·주운 자리·가게 교환이 되살아나는가 ③ 지나온 구역의 적이 치워졌는가
///  ④ 이어서 저장 지점을 지나면 파일에 다시 기록되는가 를 본다. 원래 저장 파일은 시험 전에 옮겨 두었다가 되돌린다.
/// </summary>
public static class SaveTest
{
    static readonly List<string> lines = new();
    static string log, backup;
    static int index, stage;
    static float since;
    static readonly (string id, string point, string phase, float behindZ)[] Cases =
    {
        ("c1_shop1", "shopSave", "Avenue", 70f),
        ("c1_zone2", "zone2Start", "CheckpointScene", 128f),
        ("c1_shop2", "shop2Save", "Harbor", 275f),
        ("c1_zone3", "zone3Start", "Docks", 300f),
    };

    static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    public static void Begin(string logPath)
    {
        Application.runInBackground = true;
        log = logPath; lines.Clear(); index = 0; stage = 0; since = Time.realtimeSinceStartup;
        backup = SavePath + ".testbackup";
        if (File.Exists(SavePath)) File.Copy(SavePath, backup, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { lines.Add(t); Debug.Log("[저장 시험] " + t); }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Done(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        float t = Time.realtimeSinceStartup - since;
        var c = Cases[index];
        if (stage == 0)
        {
            var d = new SaveGame.Data { scene = "Chapter1", checkpoint = c.id, inserted = 1, potions = 3 };
            d.tapes.Add(new SaveGame.TapeState { tape = new CassetteTape { title = "시험 테이프 A", playable = false, charges = 2 } });
            d.tapes.Add(new SaveGame.TapeState { tape = new CassetteTape { title = "시험 테이프 B", playable = true, charges = 1, musicId = "lighthouse" }, position = 30f });
            d.pickups.Add(new SaveGame.PickupState { name = "BusStopTape", taken = true, potions = 0 });
            d.traded.Add("심야 레코드|0");
            File.WriteAllText(SavePath, JsonUtility.ToJson(d, true));
            Note($"── 이어하기: {c.id}");
            SaveGame.Continue();
            stage = 1; since = Time.realtimeSinceStartup;
            return;
        }
        if (stage == 1 && t > 4.5f)
        {
            var dir = Object.FindAnyObjectByType<Chapter1Director>();
            var p = Object.FindAnyObjectByType<PlayerController>();
            var w = p ? p.GetComponent<Walkman>() : null;
            var pointField = typeof(Chapter1Director).GetField(c.point);
            var point = pointField != null ? pointField.GetValue(dir) as Transform : null;
            float dist = point && p ? Vector3.Distance(Flat(point.position), Flat(p.transform.position)) : -1f;
            int behind = 0;
            foreach (var e in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) if (e.transform.position.z < c.behindZ) behind++;
            GameObject bus = null;
            foreach (var pk in Object.FindObjectsByType<TapePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (pk.name == "BusStopTape") bus = pk.gameObject;
            bool traded = false;
            foreach (var s in Object.FindObjectsByType<TapeShop>(FindObjectsSortMode.None)) if (s.shopName == "심야 레코드") traded = s.stock[0].traded;
            string tapes = w ? string.Join(", ", w.tapes.ConvertAll(x => $"{x.title}({x.charges}회, {x.position:0}초)")) : "-";
            Note($"   단계 {dir.CurrentPhase}(기대 {c.phase}), 저장 지점까지 {dist:0.0}m(기대 ~0, 컷신이면 더), 조작 잠김 {StoryState.ControlLocked}");
            Note($"   테이프 [{tapes}], 끼운 것 {(w && w.Inserted != null ? w.Inserted.title : "-")}(기대 시험 테이프 B), 회복약 {(w ? w.potions : -1)}(기대 3)");
            Note($"   주운 정류장 테이프 사라짐 {(bus ? !bus.activeSelf : false)}(기대 True), 가게 1 교환 기록 {traded}(기대 True), 지나온 구역의 적 {behind}명(기대 0)");
            var saved = SaveGame.Read();
            Note($"   다시 기록된 저장: {(saved != null ? saved.checkpoint : "없음")}, 테이프 {(saved != null ? saved.tapes.Count : 0)}개");
            stage = 0; index++; since = Time.realtimeSinceStartup;
            if (index >= Cases.Length) Done();
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static void Done()
    {
        EditorApplication.update -= Tick;
        if (backup != null)
        {
            if (File.Exists(backup)) { File.Copy(backup, SavePath, true); File.Delete(backup); Note("원래 저장 파일을 되돌림"); }
            else if (File.Exists(SavePath)) { File.Delete(SavePath); Note("시험 저장 파일을 지움(원래 저장 없음)"); }
            backup = null;
        }
        Note("시험 끝");
        if (log != null) File.WriteAllLines(log, lines);
        log = null;
    }
}
