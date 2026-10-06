using UnityEngine;

/// <summary>
/// 월드에 놓인 카세트: 되감기 전용(재생 불가)이며 미리 충전되어 있다. 한 번 주우면
/// 영영 사라진다 — 시간을 되감아도 돌아오지 않는다.
/// </summary>
public class TapePickup : MonoBehaviour
{
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
            var found = tape.CloneFresh();
            found.playable = false;
            walkman.AddTape(found);
            tapeTaken = true;
            if (visual) visual.gameObject.SetActive(false);
            message = $"테이프 획득: {found.title} — 되감기 전용 {found.charges}회";
        }
        int got = walkman.AddPotions(potions);
        potions -= got;
        if (got > 0) message += (message.Length > 0 ? "\n" : "") + $"회복약 +{got}";
        // 회복약이 가득 차 있으면 남은 것은 그 자리에 두고 간다(다시 오면 가져갈 수 있다)
        if (potions > 0 && message.Length > 0) message += $"\n회복약이 가득 차 {potions}개는 두고 간다";
        if (message.Length > 0 && GameManager.Instance) GameManager.Instance.ShowMessage(message, 2.8f);
        if (got > 0) Tutorial.Show("heal");
        if (potions <= 0) gameObject.SetActive(false);
    }
}
