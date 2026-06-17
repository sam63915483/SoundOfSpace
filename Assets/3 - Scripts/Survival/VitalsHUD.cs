using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Top-left vitals HUD. Reads ResourceManager percents and renders a beveled
/// card (matching the tutorial pill / Press-F prompt family) with four
/// horizontal stat rows: HEALTH, HUNGER, THIRST, SHIP POWER. Optional fifth
/// row appears when SolarPanelCharger.IsCharging is true.
///
/// Mirrors PlayerWallet's auto-creating singleton pattern. Disables the
/// legacy ResourceHUD on Start so the two HUDs don't double-render.
/// </summary>
public class VitalsHUD : MonoBehaviour
{
    public static VitalsHUD Instance { get; private set; }

    [Header("Card layout")]
    public float cardWidth = 320f;   // matches the jetpack HUD card width
    public float bottomMargin = 24f;
    public float rightMargin = 24f;

    [Header("Pulse / warning")]
    [Tooltip("Below this percent, the bar fill alpha pulses to warn the player.")]
    public float pulseThreshold = 0.25f;
    [Tooltip("Below this percent, the warningClip is played once until percent recovers.")]
    public float urgentThreshold = 0.10f;
    public float pulseFrequency = 1f;
    public AudioClip warningClip;

    // ── Palette (matches TutorialUI / InteractPromptUI) ──────────────
    static readonly Color PillBgColor    = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color PillBorderColor = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color LedColor       = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color HeaderColor    = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color LabelColor     = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TrackColor     = new Color32(0x0F, 0x19, 0x2A, 0xD9);

    // ── Internal state ──────────────────────────────────────────────
    Canvas _canvas;
    RectTransform _cardRT;
    StatRow _health, _hunger, _thirst, _suitO2, _shipPower, _shipFuel;
    GameObject _chargingRow;
    TMP_Text _chargingText;
    SolarPanelCharger _solar;
    AudioSource _audio;

    bool _hungerWarned, _thirstWarned, _healthWarned, _shipPowerWarned, _shipFuelWarned;
    bool _legacyHidden;
    bool _chargingShown;

    class StatRow
    {
        public RectTransform root;
        public RectTransform fill;
        public Image fillImage;
        public TMP_Text pct;
        public int lastPctSeen;     // change-detection so we don't allocate text strings every frame
        public Color colorA, colorB;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("VitalsHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<VitalsHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        BuildCanvas();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoadedRefresh;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedRefresh;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        DisableLegacyResourceHUD();
        if (_solar == null) _solar = FindObjectOfType<SolarPanelCharger>();
    }

    // Re-run the one-shot scene scans whenever a new scene loads. This used
    // to live in Update (so it could survive save-load reloads where this
    // persistent HUD outlasts the legacy ResourceHUD's scene), but
    // FindObjectOfType<ResourceHUD>(true) per frame was costing ~0.5 ms.
    // Re-scanning on sceneLoaded covers the same case without per-frame work.
    void OnSceneLoadedRefresh(Scene s, LoadSceneMode m)
    {
        if (s.name == "MainMenu") return;
        DisableLegacyResourceHUD();
        _solar = FindObjectOfType<SolarPanelCharger>();
    }

    void Update()
    {

        if (ResourceManager.Instance == null) return;

        // Ship power + fuel rows only matter while the player is actually
        // piloting a ship. The VLG + ContentSizeFitter on the card auto-shrinks
        // the panel when the row deactivates, so the rest stays compact.
        bool showShipPower = Ship.AnyShipPiloted;
        if (_shipPower != null && _shipPower.root != null
            && _shipPower.root.gameObject.activeSelf != showShipPower)
        {
            _shipPower.root.gameObject.SetActive(showShipPower);
        }
        if (_shipFuel != null && _shipFuel.root != null
            && _shipFuel.root.gameObject.activeSelf != showShipPower)
        {
            _shipFuel.root.gameObject.SetActive(showShipPower);
        }

        var piloted     = Ship.PilotedInstance;
        float health    = ResourceManager.Instance.HealthPercent;
        float hunger    = ResourceManager.Instance.HungerPercent;
        float thirst    = ResourceManager.Instance.ThirstPercent;
        float shipPower = piloted != null ? piloted.PowerPercent : 0f;
        float shipFuel  = piloted != null ? piloted.FuelPercent  : 0f;

        UpdateStat(_health, health);
        UpdateStat(_hunger, hunger);
        UpdateStat(_thirst, thirst);
        // §2: suit O2 (player vital, always shown). Falls back to full if the
        // oxygen manager isn't up yet so the bar never reads empty pre-init.
        UpdateStat(_suitO2, OxygenManager.Instance != null ? OxygenManager.Instance.SuitPercent : 1f);
        if (showShipPower)
        {
            UpdateStat(_shipPower, shipPower);
            UpdateStat(_shipFuel,  shipFuel);
        }

        CheckWarning(health, ref _healthWarned);
        CheckWarning(hunger, ref _hungerWarned);
        CheckWarning(thirst, ref _thirstWarned);
        if (showShipPower)
        {
            CheckWarning(shipPower, ref _shipPowerWarned);
            CheckWarning(shipFuel,  ref _shipFuelWarned);
        }
        else { _shipPowerWarned = false; _shipFuelWarned = false; }

        // Charging row visibility toggle.
        bool charging = _solar != null && _solar.IsCharging;
        if (charging != _chargingShown && _chargingRow != null)
        {
            _chargingShown = charging;
            _chargingRow.SetActive(charging);
        }
    }

    void UpdateStat(StatRow row, float percent)
    {
        if (row == null) return;
        // Bar fill: drive via localScale.x so the gradient sprite doesn't squash.
        var s = row.fill.localScale;
        row.fill.localScale = new Vector3(Mathf.Clamp01(percent), s.y, s.z);

        // Pulse alpha when low.
        if (row.fillImage != null)
        {
            float a;
            if (percent < pulseThreshold)
            {
                float t = (Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
                a = Mathf.Lerp(0.35f, 1f, t);
            }
            else
            {
                a = 1f;
            }
            var c = row.fillImage.color;
            if (!Mathf.Approximately(c.a, a))
            {
                c.a = a;
                row.fillImage.color = c;
            }
        }

        // Percent text — only update on whole-percent change to avoid per-frame string alloc.
        int pctInt = Mathf.RoundToInt(Mathf.Clamp01(percent) * 100f);
        if (pctInt != row.lastPctSeen)
        {
            row.lastPctSeen = pctInt;
            if (row.pct != null) row.pct.text = $"{pctInt}%";
        }
    }

    void CheckWarning(float percent, ref bool warned)
    {
        if (percent < urgentThreshold && !warned)
        {
            warned = true;
            if (warningClip != null && _audio != null)
                _audio.PlayOneShot(warningClip);
        }
        else if (percent >= urgentThreshold && warned)
        {
            warned = false;
        }
    }

    void DisableLegacyResourceHUD()
    {
        var legacy = FindObjectOfType<ResourceHUD>(true);
        if (legacy == null) return;
        if (!legacy.gameObject.activeInHierarchy) return; // already disabled — nothing to do
        _legacyHidden = true;

        // Disable each scene-bound element the legacy HUD drives. The bars,
        // labels, and charging indicator may live on separate GameObjects
        // from the ResourceHUD component itself, so disabling the script
        // alone isn't enough — kill each visual element directly.
        DisableSafe(legacy.hungerBarFill);
        DisableSafe(legacy.thirstBarFill);
        DisableSafe(legacy.healthBarFill);
        DisableSafe(legacy.shipPowerBarFill);
        DisableSafe(legacy.hungerBarImage);
        DisableSafe(legacy.thirstBarImage);
        DisableSafe(legacy.healthBarImage);
        DisableSafe(legacy.shipPowerBarImage);
        DisableSafe(legacy.hungerLabel);
        DisableSafe(legacy.thirstLabel);
        DisableSafe(legacy.healthLabel);
        DisableSafe(legacy.shipPowerLabel);
        DisableSafe(legacy.chargingLabel);

        // Disable only the ResourceHUD GameObject itself (and its bar children).
        // Do NOT walk up to the parent Canvas — in this scene the legacy bars
        // share HUD_Canvas with DialogueText, the talk prompt, the choice
        // panel, and other gameplay UI; killing the canvas hides all of them.
        legacy.gameObject.SetActive(false);
    }

    static void DisableSafe(Component c)
    {
        if (c != null) c.gameObject.SetActive(false);
    }

    // ── Build canvas ─────────────────────────────────────────────────

    void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 830; // above LetterboxBars (820) — stays visible during dialogue / cook UI
        _canvas = canvas;
        HUDSceneGate.Register(canvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Card root anchored bottom-right.
        var card = NewUI("Card", transform);
        card.anchorMin = new Vector2(1f, 0f);
        card.anchorMax = new Vector2(1f, 0f);
        card.pivot = new Vector2(1f, 0f);
        card.anchoredPosition = new Vector2(-rightMargin, bottomMargin);
        card.sizeDelta = new Vector2(cardWidth, 0f);
        _cardRT = card;

        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = PillBgColor;
        bg.raycastTarget = false;

        var border = NewUI("Border", card);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = PillBorderColor;
        borderImg.raycastTarget = false;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // LED accent bar on the left.
        var led = NewUI("Led", card);
        led.anchorMin = new Vector2(0f, 0f);
        led.anchorMax = new Vector2(0f, 1f);
        led.pivot = new Vector2(0f, 0.5f);
        led.anchoredPosition = new Vector2(9f, 0f);
        led.sizeDelta = new Vector2(3f, -20f);
        led.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var ledImg = led.gameObject.AddComponent<Image>();
        ledImg.color = LedColor;
        ledImg.raycastTarget = false;

        // Vertical layout for header + stat rows.
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 5f;
        vlg.padding = new RectOffset(26, 20, 16, 16);

        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header
        var headerGO = NewText(card, "Header", "// VITALS", 12f, FontStyles.Bold, HeaderColor);
        headerGO.alignment = TextAlignmentOptions.MidlineLeft;
        headerGO.characterSpacing = 6f;
        var headerLE = headerGO.gameObject.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 18f;

        _health    = BuildStatRow(card, "HEALTH",     new Color32(0xFF, 0x6B, 0x9F, 0xFF), new Color32(0xE6, 0x39, 0x52, 0xFF));
        _hunger    = BuildStatRow(card, "HUNGER",     new Color32(0xFF, 0xC4, 0x77, 0xFF), new Color32(0xFF, 0x8A, 0x4C, 0xFF));
        _thirst    = BuildStatRow(card, "THIRST",     new Color32(0x7B, 0xE2, 0xFF, 0xFF), new Color32(0x4A, 0x8B, 0xFF, 0xFF));
        // §2: suit O2 moved here from the standalone OxygenHUD. A player vital,
        // so it always shows (the ship rows below toggle with piloting).
        _suitO2    = BuildStatRow(card, "SUIT O2",    new Color32(0x5C, 0xC8, 0xFF, 0xFF), new Color32(0x2A, 0x9B, 0xE6, 0xFF));
        _shipPower = BuildStatRow(card, "SHIP POWER", new Color32(0xB8, 0x8C, 0xFF, 0xFF), new Color32(0xC9, 0x4F, 0xFF, 0xFF));
        _shipFuel  = BuildStatRow(card, "SHIP FUEL",  new Color32(0x8C, 0xE6, 0xFF, 0xFF), new Color32(0x4A, 0xB7, 0xFF, 0xFF));

        // Charging row (hidden by default).
        _chargingRow = BuildChargingRow(card);
        _chargingRow.SetActive(false);
    }

    StatRow BuildStatRow(RectTransform parent, string labelText, Color colorA, Color colorB)
    {
        var row = NewUI(labelText + "Row", parent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 20f;
        var rowHL = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHL.childAlignment = TextAnchor.MiddleLeft;
        rowHL.childControlWidth = true;
        rowHL.childControlHeight = true;
        rowHL.childForceExpandWidth = false;
        rowHL.childForceExpandHeight = false;
        rowHL.spacing = 10f;
        rowHL.padding = new RectOffset(0, 0, 0, 0);

        // Label
        var lbl = NewText(row, "Label", labelText, 13f, FontStyles.Bold, LabelColor);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 2f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 102f;
        lblLE.preferredHeight = 18f;

        // Bar (track + fill)
        var track = NewUI("Track", row);
        var trackLE = track.gameObject.AddComponent<LayoutElement>();
        trackLE.preferredWidth = 130f;
        trackLE.preferredHeight = 12f;
        trackLE.flexibleWidth = 1f;
        var trackImg = track.gameObject.AddComponent<Image>();
        trackImg.color = TrackColor;
        trackImg.raycastTarget = false;

        var fill = NewUI("Fill", track);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = GetHorizontalGradient(colorA, colorB);
        fillImg.type = Image.Type.Simple;
        fillImg.color = Color.white; // gradient comes from the sprite; this multiplier left at white
        fillImg.raycastTarget = false;
        // Drive the fill via localScale.x so the gradient doesn't squash.
        fill.localScale = new Vector3(1f, 1f, 1f);

        // Soft glow under the fill.
        var glow = fill.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(colorA.r, colorA.g, colorA.b, 0.55f);
        glow.effectDistance = new Vector2(0f, 0f);

        // Percent text on the right.
        var pct = NewText(row, "Pct", "0%", 13f, FontStyles.Bold, LabelColor);
        pct.alignment = TextAlignmentOptions.MidlineRight;
        var pctLE = pct.gameObject.AddComponent<LayoutElement>();
        pctLE.preferredWidth = 42f;
        pctLE.preferredHeight = 18f;

        return new StatRow
        {
            root = row,
            fill = fill,
            fillImage = fillImg,
            pct = pct,
            lastPctSeen = -1,
            colorA = colorA,
            colorB = colorB
        };
    }

    GameObject BuildChargingRow(RectTransform parent)
    {
        var row = NewUI("ChargingRow", parent);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 14f;
        var rowHL = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHL.childAlignment = TextAnchor.MiddleLeft;
        rowHL.childControlWidth = false;
        rowHL.childControlHeight = true;
        rowHL.spacing = 8f;

        var dot = NewUI("Dot", row);
        var dotLE = dot.gameObject.AddComponent<LayoutElement>();
        dotLE.preferredWidth = 8f;
        dotLE.preferredHeight = 8f;
        var dotImg = dot.gameObject.AddComponent<Image>();
        dotImg.color = new Color32(0x88, 0xDC, 0xAA, 0xFF);
        dotImg.raycastTarget = false;

        var txt = NewText(row, "Text", "CHARGING", 9f, FontStyles.Bold, new Color32(0x88, 0xDC, 0xAA, 0xFF));
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.characterSpacing = 3f;
        var txtLE = txt.gameObject.AddComponent<LayoutElement>();
        txtLE.preferredHeight = 12f;
        _chargingText = txt;

        return row.gameObject;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color)
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

    // ── Horizontal gradient sprite (cached per colour pair) ──────────

    static System.Collections.Generic.Dictionary<long, Sprite> _gradients =
        new System.Collections.Generic.Dictionary<long, Sprite>();

    static Sprite GetHorizontalGradient(Color a, Color b)
    {
        long key = ((long)EncodeColor(a) << 32) | (uint)EncodeColor(b);
        if (_gradients.TryGetValue(key, out var s)) return s;
        const int W = 64, H = 4;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[W * H];
        for (int x = 0; x < W; x++)
        {
            float t = (float)x / (W - 1);
            Color c = Color.Lerp(a, b, t);
            for (int y = 0; y < H; y++) pixels[y * W + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, 0.5f), 100f);
        spr.name = "VitalsBarGradient";
        _gradients[key] = spr;
        return spr;
    }

    static int EncodeColor(Color c)
    {
        int r = Mathf.RoundToInt(c.r * 255f) & 0xFF;
        int g = Mathf.RoundToInt(c.g * 255f) & 0xFF;
        int b = Mathf.RoundToInt(c.b * 255f) & 0xFF;
        int a = Mathf.RoundToInt(c.a * 255f) & 0xFF;
        return (r << 24) | (g << 16) | (b << 8) | a;
    }
}
