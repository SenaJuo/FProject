using UnityEngine;

/// <summary>
/// 월드에 놓인 카세트: 되감기 전용(재생 불가)이며 미리 충전되어 있다. 한 번 주우면
/// 영영 사라진다 — 시간을 되감아도 돌아오지 않는다.
/// </summary>
public class TapePickup : MonoBehaviour
{
    /// <summary>아직 남아 있는 테이프 줍는 곳(HUD가 가까운 것을 표시한다)</summary>
    public static readonly System.Collections.Generic.List<TapePickup> All = new();
    /// <summary>테이프를 아직 줍지 않았다</summary>
    public bool HasTape => !tapeTaken;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public CassetteTape tape = new();
    [Tooltip("테이프와 함께 얻는 회복약 수")]
    public int potions = 1;
    public Transform visual;

    Vector3 visualBase;
    bool tapeTaken;

    void Start()
    {
        GetComponent<Collider>().isTrigger = true;
        if (visual) visualBase = visual.localPosition;
    }

    void Update()
    {
        if (visual == null) return;
        visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.12f);
        visual.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent(out Walkman walkman)) return;
        string message = "";
        if (!tapeTaken)
        {
            // 주운 테이프는 인벤토리에 넣는다(끼운 테이프에 횟수가 더해지지 않는다). 끼운 테이프를 다 쓴 상태면 바로 끼운다(AddTape)
            var found = tape.CloneFresh();
            found.playable = false;
            found.charges = Mathf.Min(found.charges, CassetteTape.MaxCharges);
            walkman.AddTape(found);
            tapeTaken = true;
            if (visual) visual.gameObject.SetActive(false);
            message = "테이프 획득";
        }
        int got = walkman.AddPotions(potions);
        potions -= got;
        var gm = GameManager.Instance;
        if (gm && message.Length > 0) gm.Notify(message);
        if (gm && got > 0) gm.Notify("회복약 획득");
        // 회복약이 가득 차 있으면 남은 것은 그 자리에 두고 간다(다시 오면 가져갈 수 있다)
        if (gm && potions > 0 && (message.Length > 0 || got > 0)) gm.Notify("회복약 가득 참");
        if (got > 0) Tutorial.Show("heal");
        if (potions <= 0) gameObject.SetActive(false);
    }
}
