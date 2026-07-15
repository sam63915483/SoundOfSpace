using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Code-built overlay for the ship engine state: the "HOLD I" nudge that
/// appears a few seconds after boarding a cold ship. Deliberately NO ring —
/// the shrinking-ring visual is reserved for mission quick-time events
/// (TevSmugglingMission QTE UI), so holding I to start/stop the engine in
/// normal play never looks like a QTE. One static canvas shared by every
/// ship — only the piloted one drives it (Ship.UpdateEngineUI).
public static class ShipEngineUI
{
    static GameObject s_root;
    static TextMeshProUGUI s_label;
    static TextMeshProUGUI s_cap;

    /// `cap` is the big keycap glyph — "I" for keyboard, "<" (D-pad left)
    /// when the caller detects a controller. ASCII only: the default TMP
    /// font has no arrow/D-pad glyphs.
    public static void Show(string label, string cap = "I")
    {
        Ensure();
        if (!s_root.activeSelf) s_root.SetActive(true);
        if (s_label.text != label) s_label.text = label;
        if (s_cap != null && s_cap.text != cap) s_cap.text = cap;
    }

    public static void Hide()
    {
        if (s_root != null && s_root.activeSelf) s_root.SetActive(false);
    }

    static void Ensure()
    {
        if (s_root != null) return;

        var canvasGo = new GameObject("ShipEngineUI");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 860;   // over the helmet HUD, under the chase subtitles (880) + dialogue (900)
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        s_root = new GameObject("EnginePrompt");
        s_root.transform.SetParent(canvasGo.transform, false);
        var root = s_root.AddComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.30f);
        root.sizeDelta = Vector2.zero;

        // Keycap: bordered dark square with a bold I (mission QTE style).
        var border = new GameObject("CapBorder");
        border.transform.SetParent(root, false);
        border.AddComponent<RectTransform>().sizeDelta = new Vector2(72f, 72f);
        border.AddComponent<Image>().color = new Color(0.9f, 0.94f, 1f, 0.95f);

        var cap = new GameObject("Cap");
        cap.transform.SetParent(root, false);
        cap.AddComponent<RectTransform>().sizeDelta = new Vector2(64f, 64f);
        cap.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 1f);

        var letterGo = new GameObject("Cap Glyph");
        letterGo.transform.SetParent(root, false);
        var letter = letterGo.AddComponent<TextMeshProUGUI>();
        letter.rectTransform.sizeDelta = new Vector2(72f, 72f);
        letter.text = "I";
        letter.fontSize = 42f;
        letter.fontStyle = FontStyles.Bold;
        letter.color = Color.white;
        letter.alignment = TextAlignmentOptions.Center;
        s_cap = letter;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(root, false);
        s_label = labelGo.AddComponent<TextMeshProUGUI>();
        var lrt = s_label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0f, -66f);
        lrt.sizeDelta = new Vector2(600f, 40f);
        s_label.fontSize = 24f;
        s_label.fontStyle = FontStyles.Bold;
        s_label.color = new Color(0.85f, 0.90f, 1.00f);
        s_label.alignment = TextAlignmentOptions.Center;

        Object.DontDestroyOnLoad(canvasGo);
        s_root.SetActive(false);
    }
}
