using System.Collections;
using UnityEngine;

/// <summary>
/// 편집기 도구 FilmMaker의 '게임 화면' 컷용: 프레임이 다 그려진 뒤(HUD 포함) 게임 창을 그대로 떠 둔다(게임에서는 쓰지 않는다).
/// IMGUI HUD는 게임 창에만 그려져 촬영용 카메라로는 찍히지 않는다.
/// </summary>
public class FilmGrabber : MonoBehaviour
{
    public static bool Want;
    public static Texture2D Last;
    public static int LastFrame = -1;

    IEnumerator Start()
    {
        Debug.Log("[필름] FilmGrabber 시작");
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return eof;
            if (!Want) continue;
            if (Last) Destroy(Last);
            Last = ScreenCapture.CaptureScreenshotAsTexture();
            LastFrame = Time.frameCount;
        }
    }

    void OnDestroy()
    {
        Want = false;
        if (Last) Destroy(Last);
        Last = null;
    }
}
