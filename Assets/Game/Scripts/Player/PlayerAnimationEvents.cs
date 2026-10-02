using UnityEngine;

/// <summary>
/// Starter Assets 이동 클립에 박혀 있는 발소리 / 착지 이벤트를 받는다. Animator와 같은
/// GameObject에 붙어 있어야 한다. 블렌드에서 비중이 가장 큰 클립만 소리를 내고,
/// 시간을 되감는 동안에는 아무 소리도 내지 않는다.
/// </summary>
public class PlayerAnimationEvents : MonoBehaviour
{
    public AudioClip[] footsteps;
    public AudioClip landing;
    [Range(0f, 1f)] public float volume = 0.5f;

    void OnFootstep(AnimationEvent animationEvent)
    {
        if (!Audible(animationEvent) || footsteps == null || footsteps.Length == 0) return;
        AudioSource.PlayClipAtPoint(footsteps[Random.Range(0, footsteps.Length)], transform.position, volume);
    }

    void OnLand(AnimationEvent animationEvent)
    {
        if (Audible(animationEvent) && landing) AudioSource.PlayClipAtPoint(landing, transform.position, volume);
    }

    static bool Audible(AnimationEvent animationEvent) =>
        animationEvent.animatorClipInfo.weight > 0.5f && !TimeRewind.PlayingBack;
}
