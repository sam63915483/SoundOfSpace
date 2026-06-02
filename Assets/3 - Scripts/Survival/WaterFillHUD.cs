using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Above-the-hotbar water-fill HUD. Polls the player's WaterBottleController
/// for its current fill (0–100) and renders it as a 10-segment "battery" bar
/// inside a beveled pill that matches the tutorial-prompt / vitals visual
/// family. Auto-creates itself on game start. Visible only while the bottle
/// is equipped OR has water in it; fades out cleanly otherwise.
/// </summary>
public class WaterFillHUD : MonoBehaviour
{
    public static WaterFillHUD Instance { get; private set; }

    [Header("Layout (1920x1080 reference)")]
    [Tooltip("Distance from the screen bottom to the bottom of the pill. Default 180 sits comfortably above the hotbar with room for the Press-F prompt above.")]
    public float bottomMargin = 180f;
    [Tooltip("Pill width. Tip text + 10 segments + percent fit comfortably at 240.")]
    public float pillWidth = 240f;
    [Tooltip("Number of fill segments. 10 → each cell = 10%.")]
    public int segmentCount = 10;

    [Header("Warning state")]
    [Tooltip("Below this percent, the bar tints amber and pulses to warn the player.")]
    public float lowThreshold = 20f;
    public float pulseFrequency = 1f;

    // ── Palette (matches TutorialUI / VitalsHUD / InteractPromptUI) ──
    static readonly Color PillBgColor     = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color PillBorderColor = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color LedColor        = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LedColorDim     = new Color32(0x5C, 0xC8, 0xFF, 0xB3);
    static readonly Color HeaderColor     = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color LabelColor      = new Color32(0xEA, 0xF6, 0xFF, 0xFF);

    static readonly Color CellOffColor    = new Color32(0x0F, 0x19, 0x2A, 0xE6);
    static readonly Color CellBorderOff   = new Color32(0x5C, 0xC8, 0xFF, 0x35);

    static readonly Color WaterA          = new Color32(0x7B, 0xE2, 0xFF, 0xFF);
    static readonly Color WaterB          = new Color32(0x4A, 0x8B, 0xFF, 0xFF);
    static readonly Color AmberA          = new Color32(0xFF, 0xC4, 0x77, 0xFF);
    static readonly Color AmberB          = new Color32(0xFF, 0x8A, 0x4C, 0xFF);

    // ── Internal state ───────────────────────────────────────────────
    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _pillRoot;
    Image _ledBar;
    Image[] _cellFills;
    Image[] _cellBorders;
    TMP_Text _pctText;

    WaterBottleController _bottle;
    int _lastShownPct = int.MinValue;
    float _currentAlpha = 0f;
    float _targetAlpha = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("WaterFillHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<WaterFillHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        if (_group != null) _group.alpha = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_bottle == null) _bottle = FindObjectOfType<WaterBottleController>(true);

        if (_bottle == null)
        {
            ApplyAlpha(0f);
            return;
        }

        float pct = _bottle.FillPercent;
        // Show ONLY while the bottle is equipped. Previously also showed if the
        // bottle held water (so the level stayed on-screen after holstering),
        // but that left the fill bar lingering after swapping to the axe.
        bool show = _bottle.IsEquipped;
        _targetAlpha = show ? 1f : 0f;
        // Smooth fade in/out so the pill doesn't pop.
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, Time.unscaledDeltaTime * 4f);
        ApplyAlpha(_currentAlpha);

        if (_currentAlpha <= 0.001f) return;

        bool low = pct > 0f && pct <= lowThreshold;
        Color a = low ? AmberA : WaterA;
        Color b = low ? AmberB : WaterB;

        // Amber pulse when low — modulate alpha of lit cells.
        float pulse = 1f;
        if (low)
        {
            float t = (Mathf.Sin(Time.unscaledTime * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
            pulse = Mathf.Lerp(0.45f, 1f, t);
        }

        // Light cells from left to right. Round-half-up so 5% lights 1 cell,
        // 100% lights all 10, and the user always sees at least one cell when
        // there's any water at all.
        int lit = pct > 0f ? Mathf.Clamp(Mathf.CeilToInt(pct / 100f * segmentCount), 1, segmentCount) : 0;
        for (int i = 0; i < _cellFills.Length; i++)
        {
            bool on = i < lit;
            // Each cell's fill is a gradient sampled at its horizontal position
            // so the lit row reads as a continuous gradient across the segments.
            float u = (i + 0.5f) / segmentCount;
            Color cellColor = Color.Lerp(a, b, u);
            cellColor.a = on ? pulse : 0f;
            _cellFills[i].color = cellColor;
            _cellBorders[i].color = on
                ? new Color(cellColor.r, cellColor.g, cellColor.b, 0.95f)
                : CellBorderOff;
        }

        // Percent text — only update on whole-percent change to avoid per-frame string alloc.
        int pctInt = Mathf.Clamp(Mathf.RoundToInt(pct), 0, 100);
        if (pctInt != _lastShownPct)
        {
            _lastShownPct = pctInt;
            if (_pctText != null) _pctText.text = $"{pctInt}%";
        }
        if (_pctText != null)
        {
            Color textCol = low ? AmberA : WaterA;
            textCol.a = low ? pulse : 1f;
            _pctText.color = textCol;
        }
    }

    void ApplyAlpha(float a)
    {
        if (_group == null) return;
        _group.alpha = a;
    }

    // ── Build canvas ─────────────────────────────────────────────────

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 22; // above hotbar (20), below tutorial (500) and vitals (25)
        HUDSceneGate.Register(_canvas);

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Pill root — bottom-center anchored, raised above the hotbar.
        var pill = NewUI("Pill", transform);
        pill.anchorMin = new Vector2(0.5f, 0f);
        pill.anchorMax = new Vector2(0.5f, 0f);
        pill.pivot = new Vector2(0.5f, 0f);
        pill.anchoredPosition = new Vector2(0f, bottomMargin);
        pill.sizeDelta = new Vector2(pillWidth, 0f);
        _pillRoot = pill;

        var bg = pill.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = PillBgColor;
        bg.raycastTarget = false;

        var border = NewUI("Border", pill);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = PillBorderColor;
        borderImg.raycastTarget = false;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // LED accent bar on the left, pulses cyan.
        var led = NewUI("Led", pill);
        led.anchorMin = new Vector2(0f, 0f);
        led.anchorMax = new Vector2(0f, 1f);
        led.pivot = new Vector2(0f, 0.5f);
        led.anchoredPosition = new Vector2(9f, 0f);
        led.sizeDelta = new Vector2(3f, -16f);
        led.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _ledBar = led.gameObject.AddComponent<Image>();
        _ledBar.color = LedColor;
        _ledBar.raycastTarget = false;

        // Inner row: label · segments · percent.
        var hl = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.spacing = 8f;
        hl.padding = new RectOffset(22, 14, 10, 10);

        var fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Label "WATER"
        var lbl = NewText(pill, "Label", "WATER", 10f, FontStyles.Bold, HeaderColor);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.characterSpacing = 4f;
        var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
        lblLE.preferredWidth = 48f;
        lblLE.preferredHeight = 14f;
        lblLE.flexibleWidth = 0f;

        // Segment row (flex grow, fills middle).
        var segRow = NewUI("Segments", pill);
        var segLE = segRow.gameObject.AddComponent<LayoutElement>();
        segLE.preferredHeight = 12f;
        segLE.flexibleWidth = 1f;
        var segHL = segRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        segHL.childAlignment = TextAnchor.MiddleLeft;
        segHL.childControlWidth = true;
        segHL.childControlHeight = true;
        segHL.childForceExpandWidth = true;
        segHL.childForceExpandHeight = true;
        segHL.spacing = 2f;

        _cellFills = new Image[segmentCount];
        _cellBorders = new Image[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            var cell = NewUI($"Cell{i}", segRow);
            var cellLE = cell.gameObject.AddComponent<LayoutElement>();
            cellLE.preferredHeight = 12f;
            cellLE.flexibleWidth = 1f;
            // Cell off-state (dark background).
            var off = cell.gameObject.AddComponent<Image>();
            off.color = CellOffColor;
            off.raycastTarget = false;

            // Border ring — sits on top of the off background; tints to the
            // water/amber color when the cell is lit.
            var ring = NewUI("Border", cell);
            Stretch(ring);
            var ringImg = ring.gameObject.AddComponent<Image>();
            ringImg.color = CellBorderOff;
            ringImg.raycastTarget = false;
            // 1-px sliced outline by reusing the beveled outline sprite (it
            // renders as a thin ring at small sizes too).
            ringImg.sprite = UIPanelSprites.GetBeveledOutline();
            ringImg.type = Image.Type.Sliced;
            _cellBorders[i] = ringImg;

            // Filled overlay — visible only when lit; tinted with a gradient
            // sample across the segments so the row reads as one bar.
            var fill = NewUI("Fill", cell);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(1, 1);
            fill.offsetMax = new Vector2(-1, -1);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = new Color(0f, 0f, 0f, 0f);
            fillImg.raycastTarget = false;
            // Subtle glow when lit.
            var glow = fill.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(WaterA.r, WaterA.g, WaterA.b, 0.5f);
            glow.effectDistance = new Vector2(0f, 0f);
            _cellFills[i] = fillImg;
        }

        // Percent text on the right.
        _pctText = NewText(pill, "Pct", "0%", 12f, FontStyles.Bold, WaterA);
        _pctText.alignment = TextAlignmentOptions.MidlineRight;
        var pctLE = _pctText.gameObject.AddComponent<LayoutElement>();
        pctLE.preferredWidth = 40f;
        pctLE.preferredHeight = 14f;
        pctLE.flexibleWidth = 0f;

        StartCoroutine(LedPulseRoutine());
    }

    System.Collections.IEnumerator LedPulseRoutine()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.6f) + 1f) * 0.5f;
            if (_ledBar != null)
                _ledBar.color = Color.Lerp(LedColorDim, LedColor, t);
            yield return null;
        }
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
}
