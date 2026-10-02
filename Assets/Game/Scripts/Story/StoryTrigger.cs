using System;
using UnityEngine;

/// <summary>플레이어가 들어오면 한 번 Entered를 보내는 구역(예: 막다른 골목).</summary>
[RequireComponent(typeof(Collider))]
public class StoryTrigger : MonoBehaviour
{
    public event Action Entered;
    public bool Fired { get; private set; }

    void Awake() => GetComponent<Collider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if (Fired || !enabled || !other.TryGetComponent(out PlayerController _)) return;
        Fired = true;
        Entered?.Invoke();
    }
}
