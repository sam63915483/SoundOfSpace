using UnityEngine;
using TMPro;
using System.Collections;

public class FishCatchUI : MonoBehaviour
{
    public static FishCatchUI Instance { get; private set; }

    [Header("UI References")]
    public TextMeshProUGUI messageText;
    public CanvasGroup canvasGroup;

    [Header("Display Settings")]
    public float displayDuration = 3f;
    public float fadeDuration = 0.5f;

    private Coroutine currentDisplay;

    private static readonly Color[] spinColors = new Color[]
    {
        new Color(0.4f, 0.6f, 1f),    // blue
        new Color(1f, 0.9f, 0.2f),    // yellow
        new Color(0.8f, 0.3f, 1f),    // purple
        new Color(0.3f, 1f, 0.5f),    // green
        new Color(1f, 0.5f, 0.1f),    // orange
        new Color(1f, 0.4f, 0.7f),    // pink
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Ensure invisible at start
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (messageText != null) messageText.text = "";
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ShowFishCaught(string fishType, int weightLbs, float spinDegrees = 0f, int spinCombo = 0)
    {
        if (currentDisplay != null)
            StopCoroutine(currentDisplay);

        string msg = $"+1 {fishType} Fish! ({weightLbs} lbs)";
        bool isSpin = spinDegrees >= 10f;
        if (isSpin)
        {
            msg += $"\n+{Mathf.RoundToInt(spinDegrees)}° Spin Catch!";
            if (spinCombo >= 2)
                msg += $" x{spinCombo} COMBO!";
        }

        Color textColor = isSpin ? spinColors[Random.Range(0, spinColors.Length)] : Color.white;
        currentDisplay = StartCoroutine(DisplayMessage(msg, textColor));
    }

    IEnumerator DisplayMessage(string msg, Color color = default)
    {
        messageText.color = color == default ? Color.white : color;
        messageText.text = msg;
        canvasGroup.alpha = 1f;

        // Wait for display duration
        yield return new WaitForSeconds(displayDuration);

        // Fade out
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        canvasGroup.alpha = 0f;
        messageText.text = "";
        currentDisplay = null;
    }
}