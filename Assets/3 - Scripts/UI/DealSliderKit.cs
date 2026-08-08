using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shared PRICE + CAPS slider widget used everywhere the player haggles.
///
/// Lifted out of MessagesScreen so the in-thread counter (phone) and the
/// walk-up sell panel are literally the same code rather than two lookalikes
/// that drift apart the first time one of them is retuned. Both negotiate the
/// same thing — caps and price per cap — so they should feel identical.
///
/// The price track is a green→amber→red gradient: position on the track IS the
/// risk readout. Click anywhere to jump, drag the thumb for fine control.
///
/// ── The one rule that must not be lost ───────────────────────────────────
/// Risk is measured against the buyer's OFFER, which is public information the
/// player can see. It is NEVER measured against their hidden accept ceiling
/// (true price x patience). Leaking that ceiling would turn a negotiation into
/// a solved equation — the bands below are wording, not a promise.
/// </summary>
public static class DealSliderKit
{
    // Palette — same values MessagesScreen declares, kept here so a panel that
    // adopts the widget picks up the matching colours automatically.
    public static readonly Color TextMain   = new Color32(0xE9, 0xED, 0xF2, 0xFF);
    public static readonly Color TextDim    = new Color32(0x8B, 0x95, 0xA3, 0xFF);
    public static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    public static readonly Color OkGreen    = new Color32(0x57, 0xC4, 0x6E, 0xFF);
    public static readonly Color WarnAmber  = new Color32(0xE8, 0xA3, 0x3D, 0xFF);
    public static readonly Color WarnBg     = new Color32(0x3A, 0x2F, 0x1A, 0xFF);
    public static readonly Color DimBtnBg   = new Color32(0x1A, 0x20, 0x29, 0xFF);
    public static readonly Color BadRed     = new Color32(0xE0, 0x55, 0x55, 0xFF);
    public static readonly Color PushOrange = new Color32(0xFF, 0x9A, 0x3C, 0xFF);

    /// Dark ink used for the number printed inside the thumb.
    public static readonly Color HandleInk  = new Color32(0x0B, 0x0D, 0x12, 0xFF);

    /// <summary>
    /// Skin + metrics for one slider row.
    ///
    /// The GEOMETRY and BEHAVIOUR are shared; the colours and type scale are
    /// not, because the two screens that use this widget are lit differently —
    /// the phone is a neutral-grey OS at phone type sizes, the vendor panel is
    /// a saturated blue-teal terminal at roughly double the scale. Copying the
    /// phone's greys onto the vendor panel reads as a bug, not as consistency.
    /// </summary>
    public class Style
    {
        public float rowHeight      = 20f;
        public float sideInset      = 20f;   // total width shaved off the row
        public float captionWidth   = 36f;
        public float captionFont    = 7f;
        public float captionSpacing = 1f;
        public Color captionColor   = TextDim;
        public float trackInset     = 40f;   // gap from row's left edge to track
        public float trackHeight    = 10f;
        public Color trackColor     = DimBtnBg;
        public Color? trackOutline  = null;  // vendor panel outlines its tracks
        public float thumbSize      = 18f;
        public float thumbFont      = 7f;
        public Color thumbColor     = TextMain;
        public Color thumbInk       = HandleInk;
        public float ringOutset     = 2f;
        public Color ringColor      = new Color(0.36f, 0.78f, 1f, 0.35f);

        /// The phone's Messages thread — the original values this widget shipped with.
        public static Style Phone() => new Style();

        /// The walk-up vendor panel: same shape, roughly 2x scale, blue-teal.
        /// Colours are MushroomSellUI's own constants so the row sits in the
        /// panel rather than on top of it.
        public static Style VendorPanel() => new Style
        {
            rowHeight      = 34f,
            sideInset      = 60f,
            captionWidth   = 92f,
            captionFont    = 12f,
            captionSpacing = 2f,
            captionColor   = new Color32(127, 160, 189, 255),   // C_Dim
            trackInset     = 100f,
            trackHeight    = 16f,
            trackColor     = new Color32(8, 19, 31, 255),       // C_SlotBg
            trackOutline   = new Color32(36, 66, 95, 255),      // C_SlotEdge
            thumbSize      = 30f,
            thumbFont      = 13f,
            thumbColor     = new Color32(234, 246, 255, 255),   // C_Label
            thumbInk       = new Color32(8, 19, 31, 255),       // C_SlotBg
            ringOutset     = 3f,
            ringColor      = new Color(120f / 255f, 200f / 255f, 1f, 0.45f),
        };
    }

    // ── Risk wording ─────────────────────────────────────────────────────

    /// Risk wording + colour for an ask, measured against their OFFER.
    /// Deliberately NO quantity commentary: Sam cut it — players learn that bad
    /// deals get declined by having bad deals declined, not from a caption.
    public static void RiskFor(int ask, int offer, out string text, out Color col)
    {
        float over = offer > 0 ? (float)ask / offer : 1f;
        if (ask <= offer)       { text = "their number — just send it";                col = OkGreen; }
        else if (over <= 1.10f) { text = "modest push — decent odds";                  col = OkGreen; }
        else if (over <= 1.22f) { text = "firm push — they may counter";               col = WarnAmber; }
        else if (over <= 1.38f) { text = "pushing it — likely a counter, maybe worse"; col = PushOrange; }
        else                    { text = "greedy — you might blow the deal";           col = BadRed; }
    }

    /// The price slider's range, derived from their offer. Kept here so both
    /// panels open on the same number and top out at the same ceiling.
    public static void PriceRange(int offerPerCap, out int min, out int max, out int start)
    {
        min = Mathf.Max(1, offerPerCap);
        max = Mathf.Max(min + 10, Mathf.RoundToInt(min * 1.55f));
        start = Mathf.Clamp(Mathf.RoundToInt(min * 1.1f), min, max);
    }

    // ── The widget ───────────────────────────────────────────────────────

    /// One labelled slider row: mini caption left, track + circular thumb right.
    ///
    /// Pass a track sprite (RiskGradient()) for the PRICE row; pass null for the
    /// CAPS row, which sits on a plain track because quantity carries no risk
    /// colour.
    ///
    /// The thumb's size is pinned AFTER the Slider takes ownership of the rect
    /// and is preserveAspect'd — the Slider stretched it into an oval otherwise
    /// (Sam's note, learned the hard way; don't reorder these lines).
    public static Slider BuildSliderRow(RectTransform tray, string caption, float y,
                                        int min, int max, int start,
                                        Sprite trackSprite, out TextMeshProUGUI handleLabel,
                                        Style style = null)
    {
        var st = style ?? Style.Phone();

        var row = NewUI($"SliderRow_{caption}", tray);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(-st.sideInset, st.rowHeight);
        row.anchoredPosition = new Vector2(0f, y);

        var cap = MakeText(row, caption, st.captionFont, st.captionColor, TextAlignmentOptions.MidlineLeft);
        var capRT = cap.rectTransform;
        capRT.anchorMin = new Vector2(0f, 0f); capRT.anchorMax = new Vector2(0f, 1f);
        capRT.pivot = new Vector2(0f, 0.5f);
        capRT.sizeDelta = new Vector2(st.captionWidth, 0f);
        capRT.anchoredPosition = Vector2.zero;
        cap.characterSpacing = st.captionSpacing;

        var sliderRT = NewUI("Slider", row);
        sliderRT.anchorMin = new Vector2(0f, 0f); sliderRT.anchorMax = new Vector2(1f, 1f);
        sliderRT.offsetMin = new Vector2(st.trackInset, 0f); sliderRT.offsetMax = Vector2.zero;
        var slider = sliderRT.gameObject.AddComponent<Slider>();

        var trackRT = NewUI("Track", sliderRT);
        trackRT.anchorMin = new Vector2(0f, 0.5f); trackRT.anchorMax = new Vector2(1f, 0.5f);
        trackRT.pivot = new Vector2(0.5f, 0.5f);
        trackRT.sizeDelta = new Vector2(0f, st.trackHeight);
        var track = trackRT.gameObject.AddComponent<Image>();
        if (trackSprite != null) track.sprite = trackSprite;
        else track.color = st.trackColor;
        track.raycastTarget = true;   // clicking the track jumps the thumb
        if (st.trackOutline.HasValue) OutlineRect(trackRT, st.trackOutline.Value);

        var areaRT = NewUI("HandleArea", sliderRT);
        areaRT.anchorMin = Vector2.zero; areaRT.anchorMax = Vector2.one;
        float half = st.thumbSize * 0.5f;
        areaRT.offsetMin = new Vector2(half, 0f); areaRT.offsetMax = new Vector2(-half, 0f);

        var handleRT = NewUI("Handle", areaRT);
        var handle = handleRT.gameObject.AddComponent<Image>();
        handle.sprite = HALVisuals.Disc();
        handle.color = st.thumbColor;
        handle.raycastTarget = true;
        handle.preserveAspect = true;

        var ring = NewUI("Ring", handleRT);
        ring.anchorMin = Vector2.zero; ring.anchorMax = Vector2.one;
        ring.offsetMin = new Vector2(-st.ringOutset, -st.ringOutset);
        ring.offsetMax = new Vector2(st.ringOutset, st.ringOutset);
        var ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = HALVisuals.Disc();
        ringImg.color = st.ringColor;
        ringImg.raycastTarget = false;
        ringImg.preserveAspect = true;
        ring.SetAsFirstSibling();

        handleLabel = MakeText(handleRT, "", st.thumbFont, st.thumbInk, TextAlignmentOptions.Center);
        Fill(handleLabel.rectTransform);
        handleLabel.fontStyle = FontStyles.Bold;

        slider.targetGraphic = handle;
        slider.handleRect = handleRT;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = true;
        slider.value = start;

        // Pin the thumb to a CIRCLE after the Slider has claimed the rect — its
        // axis-anchor writes leave whatever height the hierarchy implies, which
        // rendered as a tall oval spilling over the labels. Must stay after the
        // handleRect/min/max/value assignments above; don't reorder.
        handleRT.anchorMin = new Vector2(handleRT.anchorMin.x, 0.5f);
        handleRT.anchorMax = new Vector2(handleRT.anchorMax.x, 0.5f);
        handleRT.sizeDelta = new Vector2(st.thumbSize, st.thumbSize);

        return slider;
    }

    /// Four 1px edge strips — the vendor panel outlines its rects this way
    /// rather than using a sprite, so a track dropped into it matches.
    static void OutlineRect(RectTransform target, Color color)
    {
        Edge(target, color, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));   // top
        Edge(target, color, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f));   // bottom
        Edge(target, color, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f));   // left
        Edge(target, color, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f));   // right
    }

    static void Edge(RectTransform parent, Color color, Vector2 aMin, Vector2 aMax, Vector2 size)
    {
        var rt = NewUI("Edge", parent);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size.x, size.y);
        rt.anchoredPosition = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    // ── procedural sprites ───────────────────────────────────────────────

    static Sprite s_gradient;

    /// Horizontal green→amber→red gradient for the price slider's risk track.
    public static Sprite RiskGradient()
    {
        if (s_gradient != null) return s_gradient;
        const int W = 256;
        var tex = new Texture2D(W, 1, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color g = new Color32(0x2A, 0x54, 0x36, 0xFF);
        Color a = new Color32(0x5C, 0x4A, 0x28, 0xFF);
        Color r = new Color32(0x5C, 0x2A, 0x2A, 0xFF);
        for (int x = 0; x < W; x++)
        {
            float t = (float)x / (W - 1);
            tex.SetPixel(x, 0, t < 0.55f ? Color.Lerp(g, a, t / 0.55f)
                                         : Color.Lerp(a, r, (t - 0.55f) / 0.45f));
        }
        tex.Apply();
        s_gradient = Sprite.Create(tex, new Rect(0, 0, W, 1), new Vector2(0.5f, 0.5f), 100f);
        return s_gradient;
    }

    static readonly Dictionary<int, Sprite> s_rounded = new Dictionary<int, Sprite>();

    /// Rounded-rect sliced sprite, cached per radius. Used for the SEND / BACK
    /// buttons under the sliders.
    public static Sprite Rounded(int radius)
    {
        if (s_rounded.TryGetValue(radius, out var cached) && cached != null) return cached;
        int size = radius * 2 + 8;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = Mathf.Clamp(x, radius, size - 1 - radius);
            float cy = Mathf.Clamp(y, radius, size - 1 - radius);
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            float alpha = Mathf.Clamp01(radius - d + 1f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                               0, SpriteMeshType.FullRect,
                               new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
        s_rounded[radius] = sp;
        return sp;
    }

    // ── build helpers (self-contained so adopters need none of their own) ──

    public static RectTransform NewUI(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    public static TextMeshProUGUI MakeText(RectTransform parent, string text, float size,
                                           Color color, TextAlignmentOptions align)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        HudFontResolver.Apply(t);
        return t;
    }
}
