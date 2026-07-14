using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Code-built overlay for the ship engine state: the "HOLD E" nudge that
/// appears a few seconds after boarding a cold ship, and the ring that
/// closes onto the E keycap while the hold is in progress (ignition or
/// shutdown). One static canvas shared by every ship — only the piloted
/// one drives it (Ship.UpdateEngineUI).
public static class ShipEngineUI
{
    static GameObject s_root;
    static TextMeshProUGUI s_label;
    static RectTransform s_ring;
    static Sprite s_ringSprite;

    /// Ring shrinks 2.2× → 1× onto the keycap as the hold completes — the
    /// same visual language as the mission QTEs, so "ring meets cap" always
    /// reads as "the key is doing something".
    public static void Show(string label, float holdProgress01)
    {
        Ensure();
        if (!s_root.activeSelf) s_root.SetActive(true);
        if (s_label.text != label) s_label.text = label;
        bool holding = holdProgress01 > 0f;
        if (s_ring.gameObject.activeSelf != holding) s_ring.gameObject.SetActive(holding);
        s_ring.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(holdProgress01));
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

        s_ring = MakeRing(root);

        // Keycap: bordered dark square with a bold E (mission QTE style).
        var border = new GameObject("CapBorder");
        border.transform.SetParent(root, false);
        border.AddComponent<RectTransform>().sizeDelta = new Vector2(72f, 72f);
        border.AddComponent<Image>().color = new Color(0.9f, 0.94f, 1f, 0.95f);

        var cap = new GameObject("Cap");
        cap.transform.SetParent(root, false);
        cap.AddComponent<RectTransform>().sizeDelta = new Vector2(64f, 64f);
        cap.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 1f);

        var letterGo = new GameObject("E");
        letterGo.transform.SetParent(root, false);
        var letter = letterGo.AddComponent<TextMeshProUGUI>();
        letter.rectTransform.sizeDelta = new Vector2(72f, 72f);
        letter.text = "E";
        letter.fontSize = 42f;
        letter.fontStyle = FontStyles.Bold;
        letter.color = Color.white;
        letter.alignment = TextAlignmentOptions.Center;

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

    static RectTransform MakeRing(RectTransform parent)
    {
        var go = new GameObject("HoldRing");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(130f, 130f);
        var img = go.AddComponent<Image>();
        img.sprite = RingSprite();
        img.color = Color.white;
        img.raycastTarget = false;
        return rt;
    }

    static Sprite RingSprite()
    {
        if (s_ringSprite != null) return s_ringSprite;
        const int size = 256;
        float outer = 124f, inner = 106f, c = size * 0.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01((outer - d) * 0.5f) * Mathf.Clamp01((d - inner) * 0.5f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        s_ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return s_ringSprite;
    }
}
