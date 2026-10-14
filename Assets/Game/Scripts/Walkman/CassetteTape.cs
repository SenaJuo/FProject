using System;
using UnityEngine;

/// <summary>테이프 장르: 끼운 테이프의 장르가 R → E로 쓰는 능력을 정한다(장르 없는 테이프는 되감기만)</summary>
public enum TapeGenre { None, Jazz, Soul, CityPop, Ambient }

[Serializable]
public class CassetteTape
{
    public const int MaxCharges = 3;

    public string title = "Untitled";
    public string[] trackTitles = { "Track 1", "Track 2", "Track 3" };
    [Tooltip("곡 음원 이름: Resources/Music/{musicId}-{곡 번호(1부터)}. 비우면 소리 없이 테이프만 돈다")]
    public string musicId;
    [Tooltip("곡마다 소리 크기 보정(곡끼리 크기를 맞춘다). 비우면 1")]
    public float[] trackGains;
    [Tooltip("곡 하나의 길이(초). 테이프 끝에 닿으면 처음부터 다시 돈다")]
    public float trackLength = 40f;
    [Tooltip("가게에서 바꾼 테이프(와 주인공 자신의 테이프)는 재생 가능. 필드에서 주운 테이프는 되감기 전용")]
    public bool playable = true;
    [Tooltip("되감기·능력 횟수(최대 3). 듣는다고 다시 차지 않는다(2026-10-14) — 다 쓰면 가게에서 바꾼다")]
    public int charges;

    // 런타임 상태 — 에셋과 함께 저장되지 않는다.
    [NonSerialized] public float position;
    [NonSerialized] public bool rewound;

    public float Length => trackTitles.Length * trackLength;
    public int CompletedTracks => Mathf.FloorToInt(position / trackLength + 0.0001f);
    public int CurrentTrack => Mathf.Min(CompletedTracks, trackTitles.Length - 1);
    public float TrackProgress => position >= Length ? 1f : position % trackLength / trackLength;
    /// <summary>재생용 테이프는 되감기·능력을 쓰든 횟수를 다 쓰든 노래가 계속 흐른다(2026-10-14 사용자 요청).
    /// 이야기에서 '재생 불가'로 정한 테이프(프롤로그의 믹스테이프, rewound)만 소리가 나지 않는다</summary>
    public bool CanPlay => playable && !rewound;
    /// <summary>다 쓴 상태: 남은 횟수가 없다(다시 차지 않는다). 테이프 가게에서 새 테이프로 바꿔 준다.</summary>
    public bool IsSpent => charges == 0;

    /// <summary>곡 음원 이름으로 정해지는 장르(이미 지은 장면의 테이프에도 그대로 적용된다)</summary>
    public TapeGenre Genre => GenreOf(musicId);

    public static TapeGenre GenreOf(string musicId) => musicId switch
    {
        "rain_jazz" or "harbor_neon" => TapeGenre.Jazz,
        "dock_soul" or "harbor_blues" or "alley_radio" => TapeGenre.Soul,
        "citypop_1" or "late_radio" or "night_drive" => TapeGenre.CityPop,
        "lighthouse" or "seven_hope" => TapeGenre.Ambient,
        _ => TapeGenre.None,
    };

    public CassetteTape CloneFresh() => new()
    {
        title = title,
        trackTitles = (string[])trackTitles.Clone(),
        musicId = musicId,
        trackGains = trackGains == null ? null : (float[])trackGains.Clone(),
        trackLength = trackLength,
        playable = playable,
        charges = charges,
    };
}
