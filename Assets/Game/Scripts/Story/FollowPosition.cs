using UnityEngine;

/// <summary>대상의 수평 위치만 따라간다(회전·높이는 그대로). 비 파티클을 플레이어 주변에만 뿌릴 때 쓴다.</summary>
public class FollowPosition : MonoBehaviour
{
    public Transform target;
    public float height = 14f;

    void LateUpdate()
    {
        if (!target) return;
        var p = target.position;
        transform.position = new Vector3(p.x, p.y + height, p.z);
    }
}
