using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 타이틀 카세트 데크 시험(플레이 중, Title 장면): 건반마다 눌러 음악이 알맞게 멈추고·돌고·감기는지,
/// 곡 끝에서 FF가 다음 곡으로 넘기는지, 그림 속 건반 자리를 정확히 집는지 기록한다. 감는 순간 화면도 찍는다.
/// </summary>
public static class TitleDeckTest
{
    static readonly List<string> lines = new();
    static int step;
    static float next, t0, mark;
    static string log, shot;
    static TitleScreen ts;
    static AudioSource Music => ts.transform.Find("TitleMusic").GetComponent<AudioSource>();

    public static void Begin(string logPath, string shotPath)
    {
        Application.runInBackground = true;
        log = logPath; shot = shotPath;
        lines.Clear(); step = 0;
        t0 = Time.unscaledTime; next = Time.unscaledTime + 0.5f;
        ts = Object.FindAnyObjectByType<TitleScreen>();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Note(string t) { t = $"[{Time.unscaledTime - t0:0.00}s] {t}"; lines.Add(t); Debug.Log("[데크 시험] " + t); }
    static string State() => $"재생 {ts.DeckPlaying}, 들림 {ts.MusicAudible}, 감기 {ts.DeckWinding}, 곡 {ts.DeckTrack}, 위치 {ts.DeckTime:0.0}s, 음높이 {Music.pitch:0.00}";

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !ts) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.3f;
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        switch (step++)
        {
            case 0:
                if ((float)typeof(TitleScreen).GetField("t", f).GetValue(ts) < 4.6f) { step--; return; } // 메뉴가 뜰 때까지
                // 건반 자리: 각 건반 가운데를 화면 좌표로 바꿔 집어 본다
                var cover = (Rect)typeof(TitleScreen).GetField("lastCover", f).GetValue(ts);
                var keyAt = typeof(TitleScreen).GetMethod("KeyAt", f);
                var rects = new[] { ts.keyPlay, ts.keyRew, ts.keyFf, ts.keyStop };
                var got = new List<string>();
                foreach (var r in rects)
                {
                    var p = new Vector2(cover.x + r.center.x * cover.width, cover.y + r.center.y * cover.height);
                    got.Add(keyAt.Invoke(ts, new object[] { p }).ToString());
                }
                var miss = keyAt.Invoke(ts, new object[] { new Vector2(cover.x + 0.3f * cover.width, cover.y + 0.3f * cover.height) }).ToString();
                Note($"건반 집기: {string.Join(", ", got)} (기대 Play, Rew, Ff, Stop) / 빈 곳 {miss} (기대 None)");
                Note("처음: " + State() + " (기대 재생·들림 True)");
                ts.PressKeyNow(3); // STOP
                next = Time.unscaledTime + 1.2f;
                break;
            case 1:
                Note("STOP 1.2초 뒤: " + State() + " (기대 재생 False, 들림 False)");
                mark = ts.DeckTime;
                ts.PressKeyNow(0); // PLAY
                next = Time.unscaledTime + 1.0f;
                break;
            case 2:
                Note($"PLAY 1초 뒤: {State()} (기대 재생·들림 True, 위치가 {mark:0.0}s에서 앞으로)");
                Music.time = 40f;
                mark = ts.TitleTime;
                ts.PressKeyNow(1); // REW: 타이틀을 처음으로 되감는다
                next = Time.unscaledTime + 0.8f;
                break;
            case 3:
                Note($"REW 0.8초: 되감는 중 {ts.Rewinding}, 타이틀 시간 {ts.TitleTime:0.0}s(누를 때 {mark:0.0}s), {State()} (기대 되감는 중 True, 들림 False)");
                ScreenCapture.CaptureScreenshot(shot);
                next = Time.unscaledTime + 2.3f;
                break;
            case 4:
                Note($"REW 끝: 되감는 중 {ts.Rewinding}, 타이틀 시간 {ts.TitleTime:0.0}s, {State()} (기대 False, 처음부터 다시(작은 값), 곡 0 처음 쪽, 들림)");
                next = Time.unscaledTime + 4.6f;
                break;
            case 5:
                Note($"다시 메뉴까지: 타이틀 시간 {ts.TitleTime:0.0}s");
                mark = ts.DeckTime;
                ts.PressKeyNow(2); // FF 한 번
                next = Time.unscaledTime + 1.5f;
                break;
            case 6:
                Note($"FF 끝: {State()} (기대 위치 {mark:0.0}s에서 약 14초 앞으로)");
                Music.time = Music.clip.length - 4f;
                mark = ts.DeckTrack;
                ts.PressKeyNow(2); // 곡 끝에서 FF → 다음 곡
                next = Time.unscaledTime + 1.5f;
                break;
            case 7:
                Note($"곡 끝에서 FF: {State()} (기대 곡 {(mark + 1) % ts.musicTracks.Length}, 위치 처음 쪽)");
                ts.PressKeyNow(3);
                next = Time.unscaledTime + 1.2f;
                break;
            case 8:
                Note("STOP: " + State() + " (기대 재생 False, 들림 False)");
                ts.PressKeyNow(0);
                next = Time.unscaledTime + 0.8f;
                break;
            case 9:
                Note("다시 PLAY: " + State() + " (기대 재생·들림 True)");
                EditorApplication.update -= Tick;
                System.IO.File.WriteAllLines(log, lines);
                break;
        }
    }
}
