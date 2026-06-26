using UnityEngine;

public static class TrailerCaptureShot
{
    public static string Execute()
    {
        var aimer = Object.FindObjectOfType<_TrailerSunAimer>();
        float dot = aimer != null ? aimer.lastDot : -99f;
        ScreenCapture.CaptureScreenshot("Recordings/flare_editor_aimed.png", 1);
        return "Capture requested. aimer.lastDot=" + dot.ToString("F3") +
               " (1.0 = camera perfectly on sun). File: Recordings/flare_editor_aimed.png (writes end of frame).";
    }
}
