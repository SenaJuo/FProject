using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 경로 중간의 카세트 상점. 다 쓴 테이프(횟수 0) 하나를 진열대의 새 테이프(횟수 3) 하나로 바꿔 준다 — 재생 가능한
/// 테이프의 유일한 입수처다(테이프 횟수는 듣는다고 다시 차지 않는다). 진열대의 테이프는 각각 한 번만 교환할 수 있다. 상점을
/// 둘러보는 동안 시간이 멈추고, 교환 결과는 나머지 인벤토리처럼 되감기 후에도 유지된다.
/// </summary>
public class TapeShop : MonoBehaviour
{
    [Serializable]
    public class Item
    {
        public CassetteTape tape = new();
        public bool traded;
    }

    /// <summary>가게 메뉴를 열었다(이야기: 저장·주인 대사)</summary>
    public static event Action<TapeShop> Opened;
    /// <summary>교환했다: (내준 다 쓴 테이프, 받은 새 테이프)</summary>
    public static event Action<TapeShop, CassetteTape, CassetteTape> Traded;
    /// <summary>메뉴가 열려 있는 상점 (없으면 null).</summary>
    public static TapeShop Current { get; private set; }
    /// <summary>플레이어가 앞에 서 있는 상점 (없으면 null).</summary>
    public static TapeShop Nearby { get; private set; }

    public string shopName = "테이프 가게";
    public List<Item> stock = new();
    public int Selected { get; private set; }

    InputAction interactAction, moveAction, closeAction;
    Walkman walkman;
    bool navigationHeld;

    void Awake()
    {
        var actions = InputSystem.actions;
        interactAction = actions.FindAction("Player/Interact", true);
        moveAction = actions.FindAction("Player/Move", true);
        closeAction = actions.FindAction("Player/Crouch", true);
    }

    void Start() => GetComponent<Collider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        // 지역 out 변수를 쓴다: 적이 걸어 들어와도 플레이어 참조가 지워지면 안 된다.
        if (!other.TryGetComponent(out Walkman visitor)) return;
        walkman = visitor;
        Nearby = this;
    }

    void OnTriggerExit(Collider other)
    {
        if (Nearby == this && other.GetComponent<Walkman>()) Nearby = null;
    }

    void OnDisable()
    {
        if (Current == this) Close();
        if (Nearby == this) Nearby = null;
    }

    void Update()
    {
        // 순간 이동(되감기·다시 시작)으로 범위를 벗어나면 OnTriggerExit가 오지 않는다 → 멀리서도 "F: 가게" 안내가 남지 않게 직접 확인한다
        if (Nearby == this && Current != this && walkman && !GetComponent<Collider>().bounds.Intersects(walkman.GetComponent<Collider>().bounds)) Nearby = null;
        if (Current == this) Browse();
        else if (Nearby == this && Current == null && !walkman.InventoryOpen && !walkman.Rewinding && !walkman.PromptOpen
                 && !StoryState.InputLocked && Time.timeScale > 0.99f && !walkman.GetComponent<Health>().IsDead
                 && interactAction.WasPressedThisFrame()) Open(); // 사망 화면·설명 팝업·연출 중에 열면 시간 속도가 꼬였다
    }

    void Open()
    {
        Current = this;
        Selected = 0;
        navigationHeld = true;
        Time.timeScale = 0f;
        Opened?.Invoke(this);
    }

    void Close()
    {
        Current = null;
        StoryState.MenuClosedFrame = Time.frameCount; // 닫은 Esc로 일시정지가 바로 열리지 않게
        Time.timeScale = 1f;
    }

    void Browse()
    {
        float y = moveAction.ReadValue<Vector2>().y;
        if (Mathf.Abs(y) < 0.3f) navigationHeld = false;
        else if (!navigationHeld && stock.Count > 0)
        {
            navigationHeld = true;
            Selected = (Selected + (y > 0f ? -1 : 1) + stock.Count) % stock.Count;
        }

        if (interactAction.WasPressedThisFrame() && stock.Count > 0) Trade(stock[Selected]);
        if (closeAction.WasPressedThisFrame() || PauseMenu.BackPressed) Close();
    }

    void Trade(Item item)
    {
        if (item.traded) { Say("이미 교환한 테이프다"); return; }
        var spent = walkman.TakeSpentTape();
        if (spent == null) { Say("교환할 다 쓴 테이프가 없다"); return; }

        item.traded = true;
        var fresh = item.tape.CloneFresh();
        fresh.playable = true;
        fresh.charges = CassetteTape.MaxCharges; // 저장 지점에서 받은 테이프는 처음부터 꽉 차 있다(2026-10-13 사용자 요청)
        walkman.AddTape(fresh);
        Say($"{fresh.title}");
        Traded?.Invoke(this, spent, fresh);
    }

    static void Say(string message)
    {
        if (GameManager.Instance) GameManager.Instance.Notify(message);
    }
}
