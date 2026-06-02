using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// On-screen toast that displays "VELOCITY MATCHED — <name>" or
// "VELOCITY UNMATCHED" centered near the top of the map view. Lives on a
// self-built ScreenSpaceOverlay canvas that the SolarSystemMapController
// toggles on with the map. Each new trigger restarts the fade-in/hold/fade-out
// routine, so rapid match→unmatch cycles always show the latest state.
public class MapVelocityHud : MonoBehaviour
{
    Canvas canvas;
    CanvasGroup group;
    TextMeshProUGUI text;
    Coroutine routine;

    static readonly Color MatchedColor   = new Color(0.36f, 0.78f, 1f, 1f);
    static readonly Color UnmatchedColor = new Color(1f, 0.7f, 0.35f, 1f);

    void Awake()
    {
        Build();
        canvas.enabled = false;
    }

    public void SetVisible(bool visible)
    {
        if (canvas == null) return;
        canvas.enabled = visible;
        if (!visible)
        {
            if (group != null) group.alpha = 0f;
            if (routine != null) { StopCoroutine(routine); routine = null; }
        }
    }

    public void ShowMatched(CelestialBody body)
    {
        string label = body != null && !string.IsNullOrEmpty(body.bodyName)
            ? $"VELOCITY MATCHED — {body.bodyName.ToUpperInvariant()}"
            : "VELOCITY MATCHED";
        ShowToast(label, MatchedColor);
    }

    public void ShowUnmatched()
    {
        ShowToast("VELOCITY UNMATCHED", UnmatchedColor);
    }

    void ShowToast(string label, Color color)
    {
        if (text == null || group == null) return;
        text.text = label;
        text.color = color;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ToastRoutine());
    }

    IEnumerator ToastRoutine()
    {
        const float fadeIn  = 0.18f;
        const float hold    = 1.4f;
        const float fadeOut = 0.4f;
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }
        group.alpha = 0f;
        routine = null;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above tutorial pill (830), below pause menu (1000), so toast lands
        // on top of the map UI but never over a paused settings panel.
        canvas.sortingOrder = 850;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var rt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0.5f, 0.86f);
        rt.anchorMax = new Vector2(0.5f, 0.86f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(900f, 56f);

        text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) text.font = font;
        text.text = "";
        text.fontSize = 28f;
        text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        text.alignment = TextAlignmentOptions.Center;
        text.characterSpacing = 4f;
        text.raycastTarget = false;
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(0f, -2f);
    }
}
