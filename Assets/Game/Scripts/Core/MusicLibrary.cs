using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 음악 불러오기(2026-10-24). id = Resources/Music 아래 이름(예: "harbor_blues-1", "Title/title-1").
///  · PC · 에디터: Resources에서 바로.
///  · 웹(itch.io): 음악은 데이터 파일 밖 StreamingAssets/Music/{id}.mp3에 두고(WebBuild가 옮긴다) 처음 부를 때 받아 온다 —
///    itch.io 웹 게임은 파일 하나가 200MB를 넘으면 안 되는데, 음악 110MB가 데이터 파일을 그 위로 밀어 올렸다.
///    받는 동안은 null을 돌려준다(부르는 쪽이 다음 프레임에 다시 묻는다).
/// </summary>
public static class MusicLibrary
{
    public static AudioClip Get(string id)
    {
        id = Normalize(id);
#if UNITY_WEBGL && !UNITY_EDITOR
        if (cache.TryGetValue(id, out var clip)) return clip;
        Prefetch(id);
        return null;
#else
        return Resources.Load<AudioClip>("Music/" + id);
#endif
    }

    /// <summary>미리 받아 둔다(웹). 그 밖에서는 아무것도 하지 않는다</summary>
    public static void Prefetch(string id)
    {
        id = Normalize(id);
#if UNITY_WEBGL && !UNITY_EDITOR
        if (cache.ContainsKey(id) || loading.Contains(id)) return;
        loading.Add(id);
        Runner.StartCoroutine(Download(id));
#endif
    }

    /// <summary>"Music/Title/title-1"처럼 앞에 Music/이 붙어 와도 같은 이름으로(타이틀 장면에 그렇게 저장돼 있다)</summary>
    static string Normalize(string id) => id.StartsWith("Music/") ? id.Substring(6) : id;

#if UNITY_WEBGL && !UNITY_EDITOR
    static readonly Dictionary<string, AudioClip> cache = new();
    static readonly HashSet<string> loading = new();
    static MonoBehaviour runner;

    static MonoBehaviour Runner
    {
        get
        {
            if (runner) return runner;
            var go = new GameObject("MusicLibrary");
            Object.DontDestroyOnLoad(go);
            return runner = go.AddComponent<Holder>();
        }
    }

    class Holder : MonoBehaviour { }

    static IEnumerator Download(string id)
    {
        string url = Application.streamingAssetsPath + "/Music/" + id + ".mp3";
        using var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
        {
            var clip = DownloadHandlerAudioClip.GetContent(req);
            clip.name = id;
            cache[id] = clip;
        }
        else
        {
            Debug.LogWarning($"음악을 받지 못했다: {url} — {req.error}");
            cache[id] = null; // 다시 묻지 않는다(소리 없이 진행)
        }
        loading.Remove(id);
    }
#endif
}
