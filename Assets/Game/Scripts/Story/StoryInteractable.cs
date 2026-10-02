using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어가 범위(트리거) 안에서 F(상호작용 키)를 누르면 Used 이벤트를 보내는 이야기용 상호작용 대상.
/// 예: 현관문 열기, 친구 집 문 두드리기. 안내 문구는 HUD가 StoryState.Nearby를 보고 그린다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StoryInteractable : MonoBehaviour
{
    public string prompt = "살펴보기";
    [Tooltip("한 번 쓰면 꺼진다")]
    public bool oneShot = true;

    public event Action Used;

    InputAction interact;
    PlayerController inside;

    void Awake() => GetComponent<Collider>().isTrigger = true;

    void Start() => interact = InputSystem.actions.FindAction("Player/Interact", true);

    void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out PlayerController player)) inside = player;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out PlayerController player) && player == inside) inside = null;
    }

    void OnDisable()
    {
        inside = null;
        if (StoryState.Nearby == this) StoryState.Nearby = null;
    }

    void Update()
    {
        // 순간 이동(문 전환·되감기)으로 범위를 벗어나면 OnTriggerExit가 오지 않는다 → 안내가 남지 않게 직접 확인한다
        if (inside && !GetComponent<Collider>().bounds.Intersects(inside.GetComponent<Collider>().bounds)) inside = null;

        bool usable = inside && !inside.IsDead && !StoryState.InputLocked && !TapeShop.Current
                      && !(inside.TryGetComponent(out Walkman w) && (w.InventoryOpen || w.Rewinding || w.PromptOpen));
        if (usable) StoryState.Nearby = this;
        else if (StoryState.Nearby == this) StoryState.Nearby = null;

        if (usable && StoryState.Nearby == this && interact.WasPressedThisFrame())
        {
            if (oneShot) enabled = false;
            Used?.Invoke();
        }
    }
}
