using System.Collections;
using UnityEngine;
using TMPro;

public class CrashWarningFader : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public TextMeshProUGUI warningText;
    public float fadeInTime = 0.5f;
    public float fadeOutTime = 1.5f;

    private Coroutine currentFade;

    void Start()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
        else
            Debug.LogError("CrashWarningFader: CanvasGroup not assigned!");
    }

    public void ShowCountdownWarning(int totalSeconds)
    {
        // The on-screen "Catastrophic damage detected" countdown was removed —
        // ship detachment still runs on the timer in ThrusterDetachOnImpact, but
        // no UI text is shown. Keep the canvas hidden in case a previous fade is
        // still in progress.
        if (currentFade != null)
        {
            StopCoroutine(currentFade);
            currentFade = null;
        }
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (warningText != null) warningText.text = string.Empty;
    }

}