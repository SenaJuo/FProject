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
        var found = tape.CloneFresh();
        found.playable = false;
        walkman.AddTape(found);
        int got = walkman.AddPotions(potions);
        if (GameManager.Instance)
            GameManager.Instance.ShowMessage($"테이프 획득: {found.title} — 되감기 전용 {found.charges}회" + (got > 0 ? $"\n회복약 +{got}" : ""), 2.8f);
        if (got > 0) Tutorial.Show("heal");
        gameObject.SetActive(false);
    }
}
