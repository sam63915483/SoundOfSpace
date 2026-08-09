using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The Galactic Standard Time readout, top-right.
///
/// Deliberately the smallest card in the HUD family: two lines, one accent bar,
/// no meters. It reads the clock, it doesn't editorialise about it.
///
///     ▎ 14:07
///     ▎ GST · DAY 3
///
/// Built in code on its own ScreenSpaceOverlay canvas like every other HUD here
/// (there is no shared HUD canvas), styled from the same pill palette
/// VitalsHUD / WaterFillHUD / CompassHUD use so it reads as one family, and
/// tinted from HelmetHudPalette so it follows the helmet accent when that's
/// retuned.
///
/// Top-right already belongs to the tutorial/hint pill (TutorialUI), so this
/// takes the corner and TutorialUI's topMargin was raised to sit underneath.
/// If this HUD is ever removed, drop that margin back.
/// </summary>
public class GalaxyTimeHUD : MonoBehaviour
{
    public static GalaxyTimeHUD Instance { get; private set; }

    /// Height this card occupies from the top of the screen, in 1920x1080
    /// reference units, including its top margin. TutorialUI reads this to park
    /// its pill below the clock instead of on top of it — so if the card's size
    /// or margin changes, that stays correct without a second edit.
    public const float ReservedTopHeight = 62f;

    const float Margin = 20f;
    const float CardWidth = 132f;

    // Shared pill palette — the same constants VitalsHUD/WaterFillHUD declare.
    // Glass rather than the near-opaque PillBgColor: the world should read
    // through the card, which is what the rest of the UI does.
    static readonly Color GlassColor      = new Color32(0x0A, 0x18, 0x28, 0x66);
    static readonly Color ScanlineColor   = new Color32(0x5B, 0xD8, 0xFF, 0x1E);
    static readonly Color PillBgColor     = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color PillBorderColor = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color HeaderColor     = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color LabelColor      = new Color32(0xEA, 0xF6, 0xFF, 0xFF);

    TextMeshProUGUI _clockText;
    TextMeshProUGUI _dayText;
    Image _led;

    // Change-detection so the per-frame text assignment doesn't allocate a new
    // string 60x a second for a value that changes once a real second.
    int _lastMinuteShown = -1;
    int _lastDayShown = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: never fires in a build (first scene is MainMenu), so this is
        // ALSO seeded from MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("GalaxyTimeHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<GalaxyTimeHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        HelmetHudPalette.OnAccentChanged -= ApplyAccent;
    }

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UILayer.Hud;
        HUDSceneGate.Register(canvas);

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        // Opt in to the shared hide switch so the CAMERA-tab "HIDE HUD" toggle
        // and cinematic force-hides take the clock with everything else.
        HudVisibility.RegisterHideable(canvas);

        var card = NewUI("Card", transform);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 1f);
        card.anchoredPosition = new Vector2(-Margin, -Margin);
        card.sizeDelta = new Vector2(CardWidth, 0f);
        // Same parallax multiplier the other clusters use, so it drifts in sync.
        HelmetSway.Register(card, 0.85f);

        // Glass, not a panel. The rest of the game's UI reads as a translucent
        // scanned surface — a solid beveled slab looked like a leftover from an
        // older UI generation sitting on top of the game.
        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = GlassColor;
        bg.raycastTarget = false;

        // Scanlines across the glass, matching the DOWNLOADING overlay and the
        // scanner screens.
        var scanRT = NewUI("Scanlines", card);
        Stretch(scanRT);
        var scan = scanRT.gameObject.AddComponent<RawImage>();
        scan.texture = ScanlineTexture();
        scan.uvRect = new Rect(0f, 0f, 1f, 14f);   // tile vertically
        scan.color = ScanlineColor;
        scan.raycastTarget = false;
        scanRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        var border = NewUI("Border", card);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = PillBorderColor;
        borderImg.raycastTarget = false;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        var led = NewUI("Led", card);
        led.anchorMin = new Vector2(0f, 0f);
        led.anchorMax = new Vector2(0f, 1f);
        led.pivot = new Vector2(0f, 0.5f);
        led.anchoredPosition = new Vector2(9f, 0f);
        led.sizeDelta = new Vector2(3f, -14f);
        led.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _led = led.gameObject.AddComponent<Image>();
        _led.color = HelmetHudPalette.Accent;
        _led.raycastTarget = false;

        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;  vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.spacing = 1f;
        vlg.padding = new RectOffset(22, 12, 8, 8);

        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _clockText = NewText(card, "Clock", "--:--", 17f, FontStyles.Bold, LabelColor);
        _clockText.characterSpacing = 2f;
        _clockText.gameObject.AddComponent<LayoutElement>().preferredHeight = 21f;

        _dayText = NewText(card, "Day", "GST", 9f, FontStyles.Bold, HeaderColor);
        _dayText.characterSpacing = 3f;
        _dayText.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

        HelmetHudPalette.OnAccentChanged += ApplyAccent;
        HudBootFX.Play(group, card);
    }

    void ApplyAccent()
    {
        if (_led != null) _led.color = HelmetHudPalette.Accent;
    }

    void Update()
    {
        var t = GalaxyTime.Instance;
        if (t == null) return;

        int minute = t.Minute;
        if (minute != _lastMinuteShown)
        {
            _lastMinuteShown = minute;
            if (_clockText != null) _clockText.text = t.ClockString;
        }

        int day = t.Day;
        if (day != _lastDayShown)
        {
            _lastDayShown = day;
            if (_dayText != null) _dayText.text = $"GST · DAY {day}";
        }
    }

    /// A 1x4 strip — one bright row, three clear — tiled by the RawImage's
    /// uvRect to make scanlines at any card height. Cached statically; every
    /// clock shares the one texture.
    static Texture2D s_scanlines;
    static Texture2D ScanlineTexture()
    {
        if (s_scanlines != null) return s_scanlines;
        var tex = new Texture2D(1, 4, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };
        tex.SetPixel(0, 0, Color.white);
        tex.SetPixel(0, 1, Color.clear);
        tex.SetPixel(0, 2, Color.clear);
        tex.SetPixel(0, 3, Color.clear);
        tex.Apply();
        s_scanlines = tex;
        return tex;
    }

    // ── Local copies of the shared build helpers (each HUD declares its own) ──

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text,
                                   float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }
}
