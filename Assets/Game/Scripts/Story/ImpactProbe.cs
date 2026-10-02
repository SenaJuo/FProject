using System;
using UnityEngine;

/// <summary>떨어뜨린 물건이 처음 무언가에 부딪히는 순간을 알리고 소리를 낸다(바닥에 떨어지는 카세트 플레이어).</summary>
public class ImpactProbe : MonoBehaviour
{
    public AudioClip sound;
    public float minSpeed = 0.5f;
    public event Action<Vector3> FirstImpact;
    public bool HasHit { get; private set; }

    void OnCollisionEnter(Collision collision)
    {
        if (HasHit || collision.relativeVelocity.magnitude < minSpeed) return;
        // 쓰러지는 주인공의 몸에 먼저 닿은 것은 무시하고 땅·벽에 닿을 때만 센다
        if (collision.rigidbody && !collision.rigidbody.isKinematic) return;
        HasHit = true;
        if (sound) ProceduralSfx.PlayAt(sound, transform.position, 1f, 0.3f);
        FirstImpact?.Invoke(collision.GetContact(0).point);
    }
}
