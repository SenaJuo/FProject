using System;
using UnityEngine;

/// <summary>
/// 3장 녹음실의 실험 부스: 헤드폰을 쓴 사람(녹음 대상)이 소리로 감정을 짜내는 실험을 당한다.
/// 시작(Begin) 뒤 collapseAt 초가 지나면 쓰러진다(래그돌). 되감으면 쓰러지기 전으로 돌아간다 — 레이만 그 일을 기억한다.
/// 장비를 끄면(Stop) 살아남는다. 부스 조명은 시간이 갈수록 붉게, 빠르게 깜박인다.
/// </summary>
public class ExperimentBooth : MonoBehaviour, IRewindable
{
    public Animator subject;
    public Ragdoll ragdoll;
    public Light boothLight;
    public AudioSource tone;
    [Tooltip("시작부터 쓰러질 때까지(초)")]
    public float collapseAt = 14f;

    public enum State { Idle, Running, Collapsed, Stopped }
    public State Current { get; private set; }
    public float Elapsed => t;
    public event Action Collapsed;

    float t;
    /// <summary>한 번 시작했으면 되감아도 실험은 다시 시작된다(시작 자체는 되감지 않는다 — 2장 증거물 담당처럼)</summary>
    bool everStarted;
    Vector3 subjectPos;
    Quaternion subjectRot;
    Color baseColor;
    float baseIntensity;

    void Awake()
    {
        TimeRewind.Register(this);
        GameManager.PlayerRespawned += ResetBooth;
        if (subject) { subjectPos = subject.transform.position; subjectRot = subject.transform.rotation; }
        if (boothLight) { baseColor = boothLight.color; baseIntensity = boothLight.intensity; }
    }

    void OnDestroy()
    {
        TimeRewind.Unregister(this);
        GameManager.PlayerRespawned -= ResetBooth;
    }

    public void Begin()
    {
        if (Current != State.Idle) return;
        Current = State.Running;
        everStarted = true;
        t = 0f;
        if (tone) { tone.loop = true; tone.Play(); }
    }

    /// <summary>장비를 끈다: 소리가 멎고 불이 원래대로</summary>
    public void Stop()
    {
        if (Current != State.Running) return;
        Current = State.Stopped;
        if (tone) tone.Stop();
        if (boothLight) { boothLight.color = baseColor; boothLight.intensity = baseIntensity; }
    }

    /// <summary>레이가 처음 조정실에 들어선 순간: 눈앞에서 쓰러진다(아직 모를 때)</summary>
    public void ForceCollapse()
    {
        if (Current == State.Running) Collapse();
    }

    void Update()
    {
        if (TimeRewind.PlayingBack) return;
        if (Current == State.Running)
        {
            t += Time.deltaTime;
            Pulse();
            if (t >= collapseAt) Collapse();
        }
    }

    void Pulse()
    {
        float k = Mathf.Clamp01(t / collapseAt);
        if (boothLight)
        {
            float rate = Mathf.Lerp(1.5f, 9f, k);
            float flick = 0.6f + 0.4f * Mathf.Sin(Time.time * rate * Mathf.PI * 2f);
            boothLight.color = Color.Lerp(baseColor, new Color(1f, 0.15f, 0.1f), k);
            boothLight.intensity = baseIntensity * Mathf.Lerp(1f, 1.8f, k) * flick;
        }
        if (tone)
        {
            tone.pitch = Mathf.Lerp(0.8f, 1.6f, k);
            tone.volume = Mathf.Lerp(0.15f, 0.55f, k) * GameSettings.Gain(GameSettings.Channel.Sfx);
        }
    }

    void Collapse()
    {
        Current = State.Collapsed;
        if (tone) tone.Stop();
        if (boothLight) { boothLight.color = new Color(1f, 0.15f, 0.1f); boothLight.intensity = baseIntensity * 0.9f; } // 붉게 남는다(쓰러진 모습이 보이게)
        if (ragdoll) ragdoll.Activate(subject ? subject.transform.forward * -40f : Vector3.zero);
        Collapsed?.Invoke();
    }

    /// <summary>붙잡혀 저장 지점부터 다시: 실험도 처음부터(레이는 기억한다)</summary>
    void ResetBooth()
    {
        everStarted = false;
        Restore(State.Idle, 0f);
        if (tone) tone.Stop();
    }

    void Restore(State state, float time)
    {
        if (ragdoll && ragdoll.IsActive) ragdoll.Restore();
        if (subject) subject.transform.SetPositionAndRotation(subjectPos, subjectRot);
        Current = state;
        t = time;
        if (boothLight) { boothLight.color = baseColor; boothLight.intensity = baseIntensity; }
        if (Current == State.Running && tone && !tone.isPlaying) tone.Play();
    }

    struct Snapshot { public State state; public float t; }

    public object CaptureState() => new Snapshot { state = Current, t = t };

    public void RestoreState(object saved, bool preview)
    {
        if (preview) return;
        var s = (Snapshot)saved;
        if (s.state == State.Idle && everStarted) { s.state = State.Running; s.t = 0f; } // 시작 전으로 되감겨도 실험은 그 자리에서 다시 돈다
        if (s.state == Current && Current != State.Collapsed) { t = s.t; return; }
        Restore(s.state, s.t);
    }
}
