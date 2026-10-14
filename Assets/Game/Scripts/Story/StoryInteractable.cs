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

    static bool refused;
    /// <summary>Used 처리 중에 부른다: "지금은 안 된다"(싸우는 중·아직 때가 아님) — 한 번만 쓰는 대상이어도 꺼지지 않고 다시 누를 수 있다.
    /// (2026-10-14: 경찰서 뒷문·5장 리프트처럼 막힌 채 한 번 누르면 영영 안 열려 진행이 멈췄다)</summary>
    public static void Refuse() => refused = true;

    InputAction interact;
    PlayerController inside;
    Collider own, insideCollider;
    Walkman insideWalkman;

    void Awake()
    {
        own = GetComponent<Collider>();
        own.isTrigger = true;
    }

    void Start() => interact = InputSystem.actions.FindAction("Player/Interact", true);

    void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out PlayerController player))
        {
            inside = player;
            insideCollider = other;
            insideWalkman = player.GetComponent<Walkman>();
        }
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
        if (inside && (!insideCollider || !own.bounds.Intersects(insideCollider.bounds))) inside = null;

        var w = insideWalkman;
        bool usable = inside && !inside.IsDead && !StoryState.InputLocked && !TapeShop.Current
                      && !(w && (w.InventoryOpen || w.Rewinding || w.PromptOpen));
        if (usable) StoryState.Nearby = this;
        else if (StoryState.Nearby == this) StoryState.Nearby = null;

        if (usable && StoryState.Nearby == this && interact.WasPressedThisFrame())
        {
            refused = false;
            if (oneShot) enabled = false;
            Used?.Invoke();
            if (refused && oneShot) enabled = true;
            refused = false;
        }
    }
}
