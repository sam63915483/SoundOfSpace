using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Non-invasive runtime restyle for the in-game HUDs (resource bars + boost meters).
// Mirrors TutorialUI.BuildCanvas exactly: outer cosmic glow, pulsing cyan→magenta
// border with a soft purple drop shadow, nebula-gradient background inset 3px,
// top accent strip, scattered twinkling stars. Logic scripts (ResourceHUD,
// BoostMeterUI) are left completely untouched — this only swaps sprites/colours
// and adds purely-decorative children with `LayoutElement.ignoreLayout = true`
// so existing layouts stay put.
public class GalaxyHudStyler : MonoBehaviour
{
    public enum Mode { Resource, Boost }

    [SerializeField] Mode mode = Mode.Resource;

    Image borderImage;
    bool styleApplied;

    void Awake()
    {
        if (styleApplied) return;
        styleApplied = true;

        if (mode == Mode.Resource) StyleResourceHud();
        else                       StyleBoostHud();

        StartCoroutine(BorderPulse());
    }

    // ── Resource HUD ───────────────────────────────────────────────────────

    void StyleResourceHud()
    {
        StylePanel(transform);

        // Centre the rows vertically inside the panel so they aren't crammed at the top.
        var vlg = GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.childAlignment = TextAnchor.MiddleLeft;
            // Stop the layout group from inflating each row to fill the panel —
            // otherwise centring has nothing to centre. Then nudge the rows down
            // a bit so they balance against the panel's top accent strip.
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            // Symmetric padding + tight spacing so the four rows fit comfortably
            // and the row stack sits at the panel's true vertical centre. Extra
            // horizontal padding keeps bars off the rounded border edges.
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 4f;
        }

        StyleRow("HungerRow",    "HungerLabel",    "HungerBarBG",    "HungerBarFill",
                 "HungerIcon",   GalaxyHudKit.HungerA, GalaxyHudKit.HungerB);
        StyleRow("ThirstRow",    "ThirstLabel",    "ThirstBarBG",    "ThirstBarFill",
                 "ThirstIcon",   GalaxyHudKit.ThirstA, GalaxyHudKit.ThirstB);
        StyleRow("HealthRow",    "HealthLabel",    "HealthBarBG",    "HealthBarFill",
                 "HealthIcon",   GalaxyHudKit.HealthA, GalaxyHudKit.HealthB);
        StyleRow("ShipPowerRow", "ShipPowerLabel", "ShipPowerBarBG", "ShipPowerBarFill",
                 "ShipPowerIcon", GalaxyHudKit.ShipPowA, GalaxyHudKit.ShipPowB);
    }

    void StyleRow(string rowName, string labelName, string bgName, string fillName,
                  string iconName, Color fillA, Color fillB)
    {
        var row = transform.Find(rowName);
        if (row == null) return;

        // The icon used to occupy the left of the row. With it hidden, ask the
        // row's HorizontalLayoutGroup to expand the BarBG so the bar fills the
        // row width instead of leaving empty space to the right.
        var rowHLG = row.GetComponent<HorizontalLayoutGroup>();
        if (rowHLG != null)
        {
            rowHLG.childForceExpandWidth = true;
            rowHLG.childControlWidth = true;
        }

        var bg = row.Find(bgName);
        if (bg != null)
        {
            var img = bg.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GalaxyHudKit.SlotSprite();
                img.type   = Image.Type.Sliced;
                img.color  = Color.white;
            }
            // Clip children (the fill, the label) to the slot's rounded shape.
            EnsureMask(bg.gameObject);

            var fill = bg.Find(fillName);
            if (fill != null)
            {
                var fillImg = fill.GetComponent<Image>();
                if (fillImg != null)
                {
                    fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(fillA, fillB);
                    fillImg.type   = Image.Type.Simple;
                    fillImg.color  = Color.white;
                }
            }

            var label = bg.Find(labelName);
            if (label != null) StyleLabel(label);
        }

        // The original square icons clash with the new pill-bar look — hide them.
        var icon = row.Find(iconName);
        if (icon != null) icon.gameObject.SetActive(false);
    }

    // ── Boost meters HUD ───────────────────────────────────────────────────

    void StyleBoostHud()
    {
        StylePanel(transform);

        StyleBoostBar("UpLabel",   "UpBarBG",   "UpBarFill",
                      GalaxyHudKit.UpThrustA, GalaxyHudKit.UpThrustB);
        StyleBoostBar("DownLabel", "DownBarBG", "DownBarFill",
                      GalaxyHudKit.DownA,    GalaxyHudKit.DownB);
        StyleBoostBar("DirLabel",  "DirBarBG",  "DirBarFill",
                      GalaxyHudKit.DirA,     GalaxyHudKit.DirB);

        // At cramped game-view sizes (Constant Pixel Size scaler), ResourceHUD's
        // panel can overhang the top of this one. Bring ourselves to the front
        // so UP THRUST stays visible in any window size.
        transform.SetAsLastSibling();
    }

    void StyleBoostBar(string labelName, string bgName, string fillName, Color fillA, Color fillB)
    {
        var label = transform.Find(labelName);
        if (label != null) StyleLabel(label);

        var bg = transform.Find(bgName);
        if (bg != null)
        {
            var img = bg.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GalaxyHudKit.SlotSprite();
                img.type   = Image.Type.Sliced;
                img.color  = Color.white;
            }
        }

        var fill = transform.Find(fillName);
        if (fill != null)
        {
            var fillImg = fill.GetComponent<Image>();
            if (fillImg != null)
            {
                // Pill-shaped sprite. Keeping Filled type so BoostMeterUI's
                // fillAmount animation continues to work — the left end is
                // always rounded; the right end becomes rounded as fill rises.
                fillImg.sprite = GalaxyHudKit.RoundedGradientFillSprite(fillA, fillB);
                fillImg.type = Image.Type.Filled;
                fillImg.fillMethod = Image.FillMethod.Horizontal;
                fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
                fillImg.color  = Color.white;
            }
        }
    }

    static void EnsureMask(GameObject go)
    {
        if (go.GetComponent<Mask>() == null)
        {
            var mask = go.AddComponent<Mask>();
            mask.showMaskGraphic = true;
        }
    }

    // ── Layered panel decoration (mirrors TutorialUI.BuildCanvas) ───────────

    void StylePanel(Transform panel)
    {
        // The existing panel Image is replaced by our layered decoration —
        // hide the original so it doesn't fight with the nebula background.
        var panelImg = panel.GetComponent<Image>();
        if (panelImg != null) panelImg.color = new Color(0f, 0f, 0f, 0f);

        // Layer 1 — Outer cosmic glow (extends ~28px past the panel).
        var glow = NewDecor("__GalaxyGlow", panel);
        StretchInset(glow, -28f, -28f, 28f, 28f);
        var glowImg = glow.gameObject.AddComponent<Image>();
        glowImg.sprite = GalaxyHudKit.GlowSprite();
        glowImg.type   = Image.Type.Sliced;
        glowImg.color  = GalaxyHudKit.GlowColor;
        glowImg.raycastTarget = false;
        glow.SetSiblingIndex(0);

        // Layer 2 — Pulsing cyan↔magenta border with soft purple drop shadow.
        var border = NewDecor("__GalaxyBorder", panel);
        StretchInset(border, 0f, 0f, 0f, 0f);
        borderImage = border.gameObject.AddComponent<Image>();
        borderImage.sprite = GalaxyHudKit.RoundedSprite();
        borderImage.type   = Image.Type.Sliced;
        borderImage.color  = GalaxyHudKit.BorderCool;
        borderImage.raycastTarget = false;
        var shadow = border.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.4f, 0.15f, 0.7f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -5f);
        border.SetSiblingIndex(1);

        // Layer 3 — Background nebula gradient, 3px inset to leave a thin border.
        var bg = NewDecor("__GalaxyBackground", panel);
        StretchInset(bg, 3f, 3f, -3f, -3f);
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.sprite = GalaxyHudKit.NebulaSprite();
        bgImage.type   = Image.Type.Sliced;
        bgImage.color  = Color.white;
        bgImage.raycastTarget = false;
        bg.SetSiblingIndex(2);

        // Layer 4 — Top accent strip (cyan→magenta gradient).
        var accent = NewDecor("__GalaxyAccent", panel);
        accent.anchorMin = new Vector2(0f, 1f);
        accent.anchorMax = new Vector2(1f, 1f);
        accent.pivot     = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -5f);
        accent.sizeDelta = new Vector2(-44f, 3f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.sprite = GalaxyHudKit.AccentSprite();
        accentImg.color  = Color.white;
        accentImg.raycastTarget = false;
        accent.SetSiblingIndex(3);

        // Layer 5 — Twinkling stars scattered behind the rows.
        AddStar(panel, new Vector2(0.08f, 0.40f), 4f,   0.85f, 0.0f);
        AddStar(panel, new Vector2(0.92f, 0.25f), 3f,   0.65f, 1.4f);
        AddStar(panel, new Vector2(0.88f, 0.78f), 5f,   0.95f, 2.6f);
        AddStar(panel, new Vector2(0.18f, 0.82f), 2.5f, 0.50f, 3.7f);
        AddStar(panel, new Vector2(0.55f, 0.18f), 2f,   0.60f, 4.9f);
        AddStar(panel, new Vector2(0.40f, 0.65f), 2f,   0.55f, 0.7f);
        AddStar(panel, new Vector2(0.72f, 0.55f), 3f,   0.70f, 5.6f);
        AddStar(panel, new Vector2(0.30f, 0.10f), 2.5f, 0.55f, 4.1f);
    }

    void AddStar(Transform panel, Vector2 anchor01, float size, float baseAlpha, float phase)
    {
        var star = NewDecor("__GalaxyStar", panel);
        star.anchorMin = star.anchorMax = anchor01;
        star.pivot = new Vector2(0.5f, 0.5f);
        star.anchoredPosition = Vector2.zero;
        star.sizeDelta = new Vector2(size, size);
        var img = star.gameObject.AddComponent<Image>();
        img.sprite = StarSpriteCached();
        img.color = new Color(1f, 1f, 1f, baseAlpha);
        img.raycastTarget = false;
        // Place stars after Background/Accent but before the existing rows so
        // they sit on the nebula but behind the bars and labels.
        star.SetSiblingIndex(4);
        StartCoroutine(StarTwinkle(img, baseAlpha, phase));
    }

    static Sprite cachedStar;
    static Sprite StarSpriteCached()
    {
        if (cachedStar != null) return cachedStar;
        var tex = MakeStarTexture(32);
        cachedStar = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 100f);
        cachedStar.name = "GalaxyHudStar";
        return cachedStar;
    }

    static Texture2D MakeStarTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                float angle = Mathf.Atan2(dy, dx);
                float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 6f);
                float core = Mathf.Pow(Mathf.Clamp01(1f - r), 3f);
                float arms = Mathf.Pow(Mathf.Clamp01(1f - r * 0.95f), 6f) * spike;
                float a = Mathf.Clamp01(core + arms * 0.7f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    IEnumerator StarTwinkle(Image img, float baseAlpha, float phase)
    {
        while (img != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.2f + phase) + 1f) * 0.5f;
            var c = img.color;
            c.a = Mathf.Lerp(baseAlpha * 0.25f, baseAlpha, t);
            img.color = c;
            yield return null;
        }
    }

    IEnumerator BorderPulse()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.4f) + 1f) * 0.5f;
            if (borderImage != null)
                borderImage.color = Color.Lerp(GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot, t);
            yield return null;
        }
    }

    static RectTransform NewDecor(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        // Tell any layout group on the parent to leave us alone.
        var layoutEl = go.AddComponent<LayoutElement>();
        layoutEl.ignoreLayout = true;
        return go.GetComponent<RectTransform>();
    }

    static void StretchInset(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    static void StyleLabel(Transform label)
    {
        var tmp = label.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            ApplyDefaultFont(tmp);
            tmp.color = GalaxyHudKit.LabelColor;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = 4f;
            if (label.GetComponent<Shadow>() == null)
            {
                var shadow = label.gameObject.AddComponent<Shadow>();
                shadow.effectColor = GalaxyHudKit.LabelGlow;
                shadow.effectDistance = new Vector2(0f, -1.5f);
            }
            return;
        }

        var legacy = label.GetComponent<Text>();
        if (legacy != null)
        {
            legacy.color = GalaxyHudKit.LabelColor;
            legacy.fontStyle = FontStyle.Bold;
            if (label.GetComponent<Shadow>() == null)
            {
                var shadow = label.gameObject.AddComponent<Shadow>();
                shadow.effectColor = GalaxyHudKit.LabelGlow;
                shadow.effectDistance = new Vector2(0f, -1.5f);
            }
        }
    }

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
    }
}
