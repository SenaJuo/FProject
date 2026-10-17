using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 경로 중간의 카세트 상점 — 재생 가능한 능력 테이프(횟수 3)의 유일한 입수처다(테이프 횟수는 듣는다고 다시 차지 않는다).
/// 두 단계로 바꾼다: ① 진열대에서 받을 테이프를 고른다 → ② 내줄 테이프를 고른다.
/// 내주는 값: 다 쓴 테이프(횟수 0) 하나, 또는 되감기용 테이프(장르 없는 테이프)의 남은 횟수 합 3 이상(예: 1회짜리 + 2회짜리, 2026-10-17 사용자 요청).
/// 진열대의 테이프는 각각 한 번만 교환할 수 있다. 상점을 둘러보는 동안 시간이 멈추고, 교환 결과는 나머지 인벤토리처럼 되감기 후에도 유지된다.
/// </summary>
public class TapeShop : MonoBehaviour
{
    [Serializable]
    public class Item
    {
        public CassetteTape tape = new();
        public bool traded;
    }

    /// <summary>교환 한 번에 필요한 값(다 쓴 테이프 하나 = 이만큼, 되감기용 테이프 = 남은 횟수)</summary>
    public const int PriceCharges = CassetteTape.MaxCharges;

    /// <summary>가게 메뉴를 열었다(이야기: 저장·주인 대사)</summary>
    public static event Action<TapeShop> Opened;
    /// <summary>교환했다: (내준 테이프 중 첫 번째, 받은 새 테이프)</summary>
    public static event Action<TapeShop, CassetteTape, CassetteTape> Traded;
    /// <summary>메뉴가 열려 있는 상점 (없으면 null).</summary>
    public static TapeShop Current { get; private set; }
    /// <summary>플레이어가 앞에 서 있는 상점 (없으면 null).</summary>
    public static TapeShop Nearby { get; private set; }

    public string shopName = "테이프 가게";
    public List<Item> stock = new();
    /// <summary>진열대에서 고른 줄</summary>
    public int Selected { get; private set; }
    /// <summary>② 내줄 테이프를 고르는 중(아니면 ① 진열대)</summary>
    public bool Offering { get; private set; }
    /// <summary>② 내줄 수 있는 내 테이프(다 쓴 테이프 + 횟수가 남은 되감기용 테이프)</summary>
    public readonly List<CassetteTape> Candidates = new();
    /// <summary>② 내주려고 고른 테이프</summary>
    public readonly List<CassetteTape> Offer = new();
    /// <summary>② 후보 목록에서 고른 줄</summary>
    public int OfferCursor { get; private set; }

    InputAction interactAction, moveAction, closeAction, confirmAction;
    Walkman walkman;
    bool navigationHeld;

    /// <summary>내줄 수 있는 테이프: 다 쓴 테이프, 또는 횟수가 남은 되감기용(능력 없는) 테이프</summary>
    public static bool Tradable(CassetteTape t) => t != null && (t.IsSpent || t.Genre == TapeGenre.None);
    /// <summary>내주는 값: 다 쓴 테이프는 그것 하나로 충분, 되감기용 테이프는 남은 횟수만큼</summary>
    public static int Value(CassetteTape t) => t.IsSpent ? PriceCharges : t.charges;
    public int OfferValue { get { int v = 0; foreach (var t in Offer) v += Value(t); return v; } }
    public bool OfferEnough => OfferValue >= PriceCharges;
    /// <summary>가진 테이프를 다 내주면 교환할 수 있는가(진열대의 '교환 가능' 표시)</summary>
    public bool CanAfford { get { if (!walkman) return false; int v = 0; foreach (var t in walkman.tapes) if (Tradable(t)) v += Value(t); return v >= PriceCharges; } }

    void Awake()
    {
        var actions = InputSystem.actions;
        interactAction = actions.FindAction("Player/Interact", true);
        moveAction = actions.FindAction("Player/Move", true);
        closeAction = actions.FindAction("Player/Crouch", true);
        confirmAction = actions.FindAction("Player/Jump", true);
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
        if (!walkman) walkman = FindAnyObjectByType<Walkman>(); // 들어서지 않고 열린 경우(시험·불러오기 직후)에도 내 테이프를 센다
        Selected = 0;
        Offering = false;
        Offer.Clear();
        navigationHeld = true;
        Time.timeScale = 0f;
        Opened?.Invoke(this);
    }

    void Close()
    {
        Current = null;
        Offering = false;
        Offer.Clear();
        StoryState.MenuClosedFrame = Time.frameCount; // 닫은 Esc로 일시정지가 바로 열리지 않게
        Time.timeScale = 1f;
    }

    void Browse()
    {
        int count = Offering ? Candidates.Count : stock.Count;
        float y = moveAction.ReadValue<Vector2>().y;
        if (Mathf.Abs(y) < 0.3f) navigationHeld = false;
        else if (!navigationHeld && count > 0)
        {
            navigationHeld = true;
            int step = y > 0f ? -1 : 1;
            if (Offering) OfferCursor = (OfferCursor + step + count) % count;
            else Selected = (Selected + step + count) % count;
        }

        bool back = closeAction.WasPressedThisFrame() || PauseMenu.BackPressed;
        if (Offering)
        {
            if (interactAction.WasPressedThisFrame() && count > 0) ToggleOffer(OfferCursor);
            if (confirmAction.WasPressedThisFrame()) ConfirmOffer();
            if (back) Back();
        }
        else
        {
            if (interactAction.WasPressedThisFrame() && stock.Count > 0) Choose(Selected);
            if (back) Close();
        }
    }

    // ---------- 화면(HUD)과 키가 함께 쓰는 동작 ----------

    /// <summary>① 진열대의 테이프를 고른다 → ② 내줄 테이프 고르기로</summary>
    public void Choose(int index)
    {
        if (index < 0 || index >= stock.Count) return;
        Selected = index;
        var item = stock[index];
        if (item.traded) { Say("이미 교환한 테이프다"); return; }
        Candidates.Clear();
        if (walkman) foreach (var t in walkman.tapes) if (Tradable(t)) Candidates.Add(t);
        if (!CanAfford) { Say("다 쓴 테이프나 되감기 3회 분량의 테이프가 필요하다"); return; }
        Offer.Clear();
        OfferCursor = 0;
        Offering = true;
        navigationHeld = true;
        // 다 쓴 테이프가 하나뿐이고 되감기용 테이프가 없으면 고를 것도 없다: 미리 골라 둔다
        if (Candidates.Count == 1) Offer.Add(Candidates[0]);
    }

    /// <summary>② 후보 줄을 고르고, 내줄 목록에 넣거나 뺀다</summary>
    public void ToggleOffer(int index)
    {
        if (!Offering || index < 0 || index >= Candidates.Count) return;
        OfferCursor = index;
        var t = Candidates[index];
        if (!Offer.Remove(t)) Offer.Add(t);
    }

    /// <summary>② 고른 테이프로 바꾼다</summary>
    public void ConfirmOffer()
    {
        if (!Offering) return;
        if (!OfferEnough) { Say($"되감기 {PriceCharges}회 분량이 필요하다"); return; }
        TradeWith(stock[Selected], new List<CassetteTape>(Offer));
    }

    /// <summary>② → ①</summary>
    public void Back()
    {
        Offering = false;
        Offer.Clear();
        navigationHeld = true;
    }

    /// <summary>시험용: 내줄 테이프를 알아서 골라 바꾼다(다 쓴 테이프 하나, 없으면 횟수 적은 되감기용 테이프부터)</summary>
    void Trade(Item item)
    {
        if (item.traded) { Say("이미 교환한 테이프다"); return; }
        var offer = new List<CassetteTape>();
        var spent = walkman.tapes.Find(t => t.IsSpent);
        if (spent != null) offer.Add(spent);
        else
        {
            var rewindOnly = walkman.tapes.FindAll(t => Tradable(t));
            rewindOnly.Sort((a, b) => a.charges.CompareTo(b.charges));
            int v = 0;
            foreach (var t in rewindOnly) { if (v >= PriceCharges) break; offer.Add(t); v += Value(t); }
            if (v < PriceCharges) { Say("교환할 다 쓴 테이프가 없다"); return; }
        }
        TradeWith(item, offer);
    }

    /// <summary>시험이 이름으로 부르는 Trade(Item)와 겹치지 않게 이름을 달리 둔다(겹치면 GetMethod가 실패했다)</summary>
    void TradeWith(Item item, List<CassetteTape> offer)
    {
        if (item.traded || offer.Count == 0) return;
        foreach (var t in offer) walkman.RemoveTape(t);
        item.traded = true;
        var fresh = item.tape.CloneFresh();
        fresh.playable = true;
        fresh.charges = CassetteTape.MaxCharges; // 저장 지점에서 받은 테이프는 처음부터 꽉 차 있다(2026-10-13 사용자 요청)
        walkman.AddTape(fresh);
        Say($"{fresh.title}");
        Offering = false;
        Offer.Clear();
        Traded?.Invoke(this, offer[0], fresh);
    }

    static void Say(string message)
    {
        if (GameManager.Instance) GameManager.Instance.Notify(message);
    }
}
