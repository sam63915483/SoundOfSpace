using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Shared, content-agnostic reading view for newspaper clippings. Built entirely
// in code (mirrors InteractPromptUI's bootstrap) so it survives builds + warm
// scene reloads with no prefab/scene serialization. One DontDestroyOnLoad
// instance; opened by any NewspaperInteractable with its own NewspaperArticleSet.
//
// Reading state (spec §3): while open we lock movement/look via
// PlayerController.isInModalSlotUI and snap the camera dead-steady by turning
// off CameraEffectsManager's master flag — both CameraFOVFX and CameraTransformFX
// run ResetIfCached() when the master is off (FOV->base, roll->0, bob->neutral).
// Everything is restored on close. The game does NOT pause (oxygen-safe zone).
public class NewspaperReaderUI : MonoBehaviour {

    public static NewspaperReaderUI Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    // Lets TabbedPauseMenu skip opening on the same Esc that closed the reader.
    int _consumedEscapeFrame = -1;
    public static bool ConsumedEscapeThisFrame =>
        Instance != null && Instance._consumedEscapeFrame == Time.frameCount;

    // ── palette ──
    static readonly Color PaperColor   = new Color(0.93f, 0.91f, 0.84f, 1f);
    static readonly Color InkColor      = new Color(0.12f, 0.11f, 0.10f, 1f);
    static readonly Color MutedColor    = new Color(0.40f, 0.37f, 0.33f, 1f);
    static readonly Color RuleColor     = new Color(0.20f, 0.18f, 0.16f, 1f);
    static readonly Color BtnColor      = new Color(0.84f, 0.82f, 0.75f, 1f);
    static readonly Color BtnDisabled   = new Color(0.84f, 0.82f, 0.75f, 0.35f);
    static readonly Color LinkColor     = new Color(0.13f, 0.27f, 0.52f, 1f);
    static readonly Color DimColor      = new Color(0f, 0f, 0f, 0.72f);

    Canvas _canvas;
    CanvasGroup _group;
    TextMeshProUGUI _header, _headline, _date, _body, _pageIndicator, _sourceLabel;
    Button _prevBtn, _nextBtn, _sourceBtn, _closeBtn;
    TextMeshProUGUI _prevArrow, _nextArrow;
    ScrollRect _scroll;

    NewspaperArticleSet _set;
    int _index;

    bool _prevModalFlag;
    bool _hadCamFx;
    bool _prevCamFx;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate() {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("NewspaperReaderUI");
        DontDestroyOnLoad(go);
        go.AddComponent<NewspaperReaderUI>();
    }

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        SetVisible(false);
    }

    void OnDestroy() {
        if (Instance != this) return;
        if (IsOpen) ExitReadingState();
        IsOpen = false;
        Instance = null;
    }

    // ───────────────────────────── public API ─────────────────────────────
    public static void Open(NewspaperArticleSet set) {
        if (Instance == null || set == null || set.articles == null || set.articles.Count == 0) return;
        Instance.OpenInternal(set);
    }

    void OpenInternal(NewspaperArticleSet set) {
        if (IsOpen) return;
        _set = set;
        _index = 0;
        IsOpen = true;
        SetVisible(true);
        Render();
        EnterReadingState();
    }

    public void Close() {
        if (!IsOpen) return;
        IsOpen = false;
        SetVisible(false);
        ExitReadingState();
        _set = null;
    }

    void Update() {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B)) { _consumedEscapeFrame = Time.frameCount; Close(); return; }
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) || TutorialGate.PadPressed(TutorialGate.PadButton.LB)) Prev();
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) || TutorialGate.PadPressed(TutorialGate.PadButton.RB)) Next();
    }

    void Next() { if (_set == null) return; if (_index < _set.articles.Count - 1) { _index++; Render(); } }
    void Prev() { if (_set == null) return; if (_index > 0) { _index--; Render(); } }

    void OpenCurrentSource() {
        if (_set == null) return;
        var a = _set.articles[_index];
        if (!string.IsNullOrWhiteSpace(a.sourceUrl)) Application.OpenURL(a.sourceUrl);
    }

    // ───────────────────────────── rendering ─────────────────────────────
    void Render() {
        if (_set == null) return;
        int count = _set.articles.Count;
        _index = Mathf.Clamp(_index, 0, count - 1);
        var a = _set.articles[_index];

        _header.text = string.IsNullOrEmpty(_set.setTitle) ? "" : _set.setTitle.ToUpperInvariant();
        _headline.text = a.headline;
        _date.text = a.date;
        _body.text = a.body;
        _pageIndicator.text = $"{_index + 1} / {count}";
        _sourceLabel.text = string.IsNullOrEmpty(a.sourceName)
            ? "Read the real article"
            : $"Read the real article  —  {a.sourceName}";

        bool hasPrev = _index > 0;
        bool hasNext = _index < count - 1;
        _prevBtn.interactable = hasPrev;
        _nextBtn.interactable = hasNext;
        _prevArrow.color = hasPrev ? InkColor : new Color(InkColor.r, InkColor.g, InkColor.b, 0.25f);
        _nextArrow.color = hasNext ? InkColor : new Color(InkColor.r, InkColor.g, InkColor.b, 0.25f);

        Canvas.ForceUpdateCanvases();
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
    }

    // ─────────────────────────── reading state ───────────────────────────
    void EnterReadingState() {
        _prevModalFlag = PlayerController.isInModalSlotUI;
        PlayerController.isInModalSlotUI = true;

        var mgr = CameraEffectsManager.Instance;
        _hadCamFx = mgr != null && mgr.Input != null;
        if (_hadCamFx) {
            _prevCamFx = mgr.Input.cameraEffectsEnabled;
            mgr.Input.cameraEffectsEnabled = false;
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void ExitReadingState() {
        PlayerController.isInModalSlotUI = _prevModalFlag;

        var mgr = CameraEffectsManager.Instance;
        if (_hadCamFx && mgr != null && mgr.Input != null)
            mgr.Input.cameraEffectsEnabled = _prevCamFx;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void SetVisible(bool v) {
        if (_group == null) return;
        _group.alpha = v ? 1f : 0f;
        _group.interactable = v;
        _group.blocksRaycasts = v;
        if (_canvas != null) _canvas.enabled = v;
    }

    // ───────────────────────────── UI build ─────────────────────────────
    void BuildCanvas() {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 2000; // above the HUD/hotbar canvases (they were bleeding over the panel)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // Match HEIGHT only. With width in the blend, ultrawide aspect ratios
        // inflate the scale and the panel overflows the screen.
        scaler.matchWidthOrHeight = 1.0f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();

        // Dimmer (click outside the panel to close)
        var dim = NewUI("Dimmer", transform);
        Stretch(dim);
        var dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.color = DimColor;
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(Close);

        // Paper panel
        var panel = NewUI("PaperPanel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(880f, 800f);
        panel.anchoredPosition = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = PaperColor;

        const float pad = 48f;

        _header = NewText(panel, "Header", "", 18f, FontStyles.Bold, MutedColor);
        _header.alignment = TextAlignmentOptions.TopLeft;
        _header.characterSpacing = 5f;
        TopBand(_header.rectTransform, 26f, 24f, pad);

        _headline = NewText(panel, "Headline", "", 32f, FontStyles.Bold, InkColor);
        _headline.alignment = TextAlignmentOptions.TopLeft;
        _headline.enableWordWrapping = true;
        TopBand(_headline.rectTransform, 58f, 150f, pad);

        _date = NewText(panel, "Date", "", 18f, FontStyles.Italic, MutedColor);
        _date.alignment = TextAlignmentOptions.TopLeft;
        TopBand(_date.rectTransform, 214f, 22f, pad);

        var rule = NewUI("Rule", panel);
        rule.anchorMin = new Vector2(0f, 1f); rule.anchorMax = new Vector2(1f, 1f); rule.pivot = new Vector2(0.5f, 1f);
        rule.offsetMin = new Vector2(pad, -250f); rule.offsetMax = new Vector2(-pad, -248f);
        rule.gameObject.AddComponent<Image>().color = RuleColor;

        BuildBodyScroll(panel, pad);

        // Footer: prev / indicator / next
        _prevBtn = BuildArrowButton(panel, "PrevButton", "<", out _prevArrow);
        var prevRT = _prevBtn.GetComponent<RectTransform>();
        prevRT.anchorMin = new Vector2(0f, 0f); prevRT.anchorMax = new Vector2(0f, 0f); prevRT.pivot = new Vector2(0f, 0f);
        prevRT.sizeDelta = new Vector2(60f, 48f); prevRT.anchoredPosition = new Vector2(pad, 98f);
        _prevBtn.onClick.AddListener(Prev);

        _nextBtn = BuildArrowButton(panel, "NextButton", ">", out _nextArrow);
        var nextRT = _nextBtn.GetComponent<RectTransform>();
        nextRT.anchorMin = new Vector2(1f, 0f); nextRT.anchorMax = new Vector2(1f, 0f); nextRT.pivot = new Vector2(1f, 0f);
        nextRT.sizeDelta = new Vector2(60f, 48f); nextRT.anchoredPosition = new Vector2(-pad, 98f);
        _nextBtn.onClick.AddListener(Next);

        _pageIndicator = NewText(panel, "PageIndicator", "", 20f, FontStyles.Bold, MutedColor);
        _pageIndicator.alignment = TextAlignmentOptions.Center;
        var piRT = _pageIndicator.rectTransform;
        piRT.anchorMin = new Vector2(0.5f, 0f); piRT.anchorMax = new Vector2(0.5f, 0f); piRT.pivot = new Vector2(0.5f, 0f);
        piRT.sizeDelta = new Vector2(220f, 48f); piRT.anchoredPosition = new Vector2(0f, 98f);

        // Source button
        _sourceBtn = BuildTextButton(panel, "SourceButton", "", 20f, Color.white, LinkColor, out _sourceLabel);
        var sRT = _sourceBtn.GetComponent<RectTransform>();
        sRT.anchorMin = new Vector2(0f, 0f); sRT.anchorMax = new Vector2(1f, 0f); sRT.pivot = new Vector2(0.5f, 0f);
        sRT.offsetMin = new Vector2(pad, 40f); sRT.offsetMax = new Vector2(-pad, 88f);
        _sourceBtn.onClick.AddListener(OpenCurrentSource);

        // Close button (top-right)
        _closeBtn = BuildTextButton(panel, "CloseButton", "Close  (Esc)", 18f, InkColor, BtnColor, out _);
        var cRT = _closeBtn.GetComponent<RectTransform>();
        cRT.anchorMin = new Vector2(1f, 1f); cRT.anchorMax = new Vector2(1f, 1f); cRT.pivot = new Vector2(1f, 1f);
        cRT.sizeDelta = new Vector2(168f, 40f); cRT.anchoredPosition = new Vector2(-14f, -14f);
        _closeBtn.onClick.AddListener(Close);
    }

    void BuildBodyScroll(RectTransform panel, float pad) {
        var scrollRT = NewUI("BodyScroll", panel);
        scrollRT.anchorMin = Vector2.zero; scrollRT.anchorMax = Vector2.one; scrollRT.pivot = new Vector2(0.5f, 0.5f);
        scrollRT.offsetMin = new Vector2(pad, 156f);   // room for footer + source button
        scrollRT.offsetMax = new Vector2(-pad, -262f); // room for masthead + rule
        _scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 36f;

        var viewport = NewUI("Viewport", scrollRT);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.001f);
        _scroll.viewport = viewport;

        var content = NewUI("Content", viewport);
        // Stretch horizontally to the viewport and pin to top. CRITICAL: zero the
        // horizontal offsets — a default sizeDelta would make Content wider than
        // the viewport (centered), clipping body text on BOTH sides.
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(0f, 0f);
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(2, 14, 0, 24);
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.content = content;

        _body = NewText(content, "BodyText", "", 22f, FontStyles.Normal, InkColor);
        _body.alignment = TextAlignmentOptions.TopLeft;
        _body.enableWordWrapping = true;
        _body.lineSpacing = 6f;
        _body.paragraphSpacing = 14f;
    }

    // ───────────────────────────── helpers ─────────────────────────────
    static RectTransform NewUI(string name, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt) {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // top-anchored full-width band, `topY` px below the panel top edge, `height` tall
    static void TopBand(RectTransform rt, float topY, float height, float sidePad) {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(sidePad, -(topY + height));
        rt.offsetMax = new Vector2(-sidePad, -topY);
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color) {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.raycastTarget = false;
        return t;
    }

    Button BuildArrowButton(RectTransform parent, string name, string glyph, out TextMeshProUGUI arrow) {
        var rt = NewUI(name, parent);
        rt.gameObject.AddComponent<Image>().color = BtnColor;
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors; colors.disabledColor = BtnDisabled; btn.colors = colors;
        arrow = NewText(rt, "Glyph", glyph, 30f, FontStyles.Bold, InkColor);
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.richText = false; // render "<" / ">" literally, not as rich-text tags
        Stretch(arrow.rectTransform);
        return btn;
    }

    Button BuildTextButton(RectTransform parent, string name, string text, float size, Color textColor, Color bgColor, out TextMeshProUGUI label) {
        var rt = NewUI(name, parent);
        rt.gameObject.AddComponent<Image>().color = bgColor;
        var btn = rt.gameObject.AddComponent<Button>();
        label = NewText(rt, "Label", text, size, FontStyles.Bold, textColor);
        label.alignment = TextAlignmentOptions.Center;
        Stretch(label.rectTransform);
        return btn;
    }
}
