using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fullscreen Photos app — thumbnail grid + fullscreen viewer over the
/// player's photo roll (PhotoLibrary). Launched from the phone's Photos
/// tile; from Task 5 onward that goes through the rotate-and-grow
/// transition (PlayerPhoneUI.OpenPhotosApp), which drives this class via
/// OpenForTransition/SetTransitionAlpha and exits via CloseForPhoneReturn.
///
/// Input gating mirrors FishingdexManager: PlayerController.isInDialogue =
/// true while open + cursor unlocked. ESC is handled HERE (viewer → grid →
/// exit-to-phone); TabbedPauseMenu skips its ESC branch while IsOpen.
///
/// Auto-singleton (mirrors SpaceDustInventory) — MUST also be seeded in
/// MainMenuController.EnsureGameplaySingletonsAsync (CLAUDE.md trap #1).
/// </summary>
public class PhotoGalleryUI : MonoBehaviour
{
    public static PhotoGalleryUI Instance { get; private set; }
    public static bool IsOpen { get; private set; }
    public static bool ConsumedEscapeThisFrame { get; private set; }

    // ── Palette (mirrors PlayerPhoneUI) ─────────────────────────────
    static readonly Color ScreenBg   = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ButtonGrey = new Color32(0x2A, 0x40, 0x60, 0xFF);

    Canvas          _canvas;
    CanvasGroup     _rootGroup;
    RectTransform   _rootRT;
    RectTransform   _gridContentRT;
    ScrollRect      _scroll;
    TextMeshProUGUI _emptyLabel;
    TextMeshProUGUI _countLabel;
    bool            _built;

    // Grid cell thumbnails — owned by us, destroyed on close.
    readonly List<Texture2D> _thumbTextures = new List<Texture2D>();

    // Fullscreen viewer state.
    RectTransform     _viewerRT;
    RawImage          _viewerImage;
    AspectRatioFitter _viewerFitter;
    TextMeshProUGUI   _viewerCaption;
    Texture2D         _viewerTexture;   // full-res; loaded on view, destroyed on close
    bool              _viewerOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PhotoGalleryUI");
        DontDestroyOnLoad(go);
        go.AddComponent<PhotoGalleryUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            // Belt-and-suspenders: normally only reached on quit/domain
            // reload, but never leave gates set or textures alive.
            if (IsOpen) TearDown();
            Instance = null;
        }
    }

    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) { ForceClose(); }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ConsumedEscapeThisFrame = true;
            Back();
        }
    }

    void LateUpdate() { ConsumedEscapeThisFrame = false; }

    /// <summary>ESC / back-button chain: viewer → grid → exit.</summary>
    public void Back()
    {
        if (_viewerOpen) { CloseViewer(); return; }
        // TEMP until Task 5 adds PlayerPhoneUI.BeginGalleryExit (the reverse
        // transition) — Task 5 Step 3 replaces this hard close.
        ForceClose();
    }

    // ── Open / close ────────────────────────────────────────────────

    /// <summary>Open instantly at full alpha (pre-transition launch path + fallback).</summary>
    public void Open()
    {
        OpenForTransition();
        SetTransitionAlpha(1f);
    }

    /// <summary>Everything Open does, but at alpha 0 — the phone's transition
    /// coroutine fades us in over the grown phone.</summary>
    public void OpenForTransition()
    {
        if (IsOpen) return;
        EnsureBuilt();
        IsOpen = true;
        PlayerController.isInDialogue = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _rootGroup.alpha = 0f;
        _rootGroup.blocksRaycasts = true;
        CloseViewer();
        PopulateGrid();
    }

    public void SetTransitionAlpha(float a)
    {
        if (_rootGroup != null) _rootGroup.alpha = Mathf.Clamp01(a);
    }

    /// <summary>Close where the phone takes over the screen (reverse transition):
    /// drops gates + textures but does NOT touch the cursor — the still-open
    /// phone owns the unlocked cursor.</summary>
    public void CloseForPhoneReturn()
    {
        if (!IsOpen) return;
        TearDown();
    }

    /// <summary>Hard close (scene load, death, conversation, fallback) — also
    /// re-locks the cursor like FishingdexManager.Close, except in MainMenu
    /// (same guard as PlayerPhoneUI.ForceCloseNoAnim).</summary>
    public void ForceClose()
    {
        if (!IsOpen) return;
        TearDown();
        if (SceneManager.GetActiveScene().name != "MainMenu")
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void TearDown()
    {
        CloseViewer();
        ClearGrid();
        IsOpen = false;
        PlayerController.isInDialogue = false;
        if (_rootGroup != null) { _rootGroup.alpha = 0f; _rootGroup.blocksRaycasts = false; }
    }

    // ── Grid ────────────────────────────────────────────────────────

    void PopulateGrid()
    {
        ClearGrid();
        var photos = PhotoLibrary.Instance != null
            ? PhotoLibrary.Instance.GetPhotosNewestFirst()
            : new List<PhotoLibrary.PhotoEntry>();

        _emptyLabel.gameObject.SetActive(photos.Count == 0);
        string countText = photos.Count + (photos.Count == 1 ? " PHOTO" : " PHOTOS");
        if (_countLabel.text != countText) _countLabel.text = countText;

        foreach (var p in photos)
        {
            var tex = LoadTexture(PhotoLibrary.Instance.GetThumbPath(p.id))
                   ?? LoadTexture(PhotoLibrary.Instance.GetPhotoPath(p.id)); // thumb missing → fall back
            if (tex == null) continue;
            _thumbTextures.Add(tex);
            BuildCell(p, tex);
        }
        // Snap to top via anchoredPosition — normalizedPosition would read a
        // stale content height (grid/fitter rebuild is deferred to end of frame).
        _gridContentRT.anchoredPosition = Vector2.zero;
    }

    void ClearGrid()
    {
        if (_gridContentRT != null)
            for (int i = _gridContentRT.childCount - 1; i >= 0; i--)
                Destroy(_gridContentRT.GetChild(i).gameObject);
        foreach (var t in _thumbTextures) if (t != null) Destroy(t);
        _thumbTextures.Clear();
    }

    void BuildCell(PhotoLibrary.PhotoEntry entry, Texture2D thumb)
    {
        var cell = NewUI("Cell_" + entry.id, _gridContentRT);
        var bg = cell.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.raycastTarget = true;

        var imgRT = NewUI("Thumb", cell);
        var raw = imgRT.gameObject.AddComponent<RawImage>();
        raw.texture = thumb;
        raw.raycastTarget = false;
        var fit = imgRT.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; // letterbox both orientations
        fit.aspectRatio = (float)thumb.width / Mathf.Max(1, thumb.height);

        var btn = cell.gameObject.AddComponent<Button>();
        var captured = entry;
        btn.onClick.AddListener(() => OpenViewer(captured));
    }

    static Texture2D LoadTexture(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            var bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
            return tex;
        }
        catch { return null; }
    }

    // ── Fullscreen viewer ───────────────────────────────────────────

    void OpenViewer(PhotoLibrary.PhotoEntry entry)
    {
        var tex = LoadTexture(PhotoLibrary.Instance != null ? PhotoLibrary.Instance.GetPhotoPath(entry.id) : null);
        if (tex == null) return;
        if (_viewerTexture != null) Destroy(_viewerTexture);
        _viewerTexture = tex;
        _viewerImage.texture = tex;
        _viewerFitter.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);

        string caption = entry.capturedAt;
        if (System.DateTime.TryParse(entry.capturedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            caption = dt.ToLocalTime().ToString("MMM d, yyyy - h:mm tt");
        _viewerCaption.text = caption;

        _viewerRT.gameObject.SetActive(true);
        _viewerOpen = true;
    }

    void CloseViewer()
    {
        if (!_viewerOpen && _viewerTexture == null) return;
        _viewerOpen = false;
        if (_viewerRT != null) _viewerRT.gameObject.SetActive(false);
        if (_viewerImage != null) _viewerImage.texture = null;
        if (_viewerTexture != null) { Destroy(_viewerTexture); _viewerTexture = null; }
    }

    // ── Build (once, lazily) ────────────────────────────────────────

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.PhotoGallery;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _rootRT = NewUI("Root", transform);
        Stretch(_rootRT, 0f);
        _rootGroup = _rootRT.gameObject.AddComponent<CanvasGroup>();
        _rootGroup.alpha = 0f;
        _rootGroup.blocksRaycasts = false;
        var bg = _rootRT.gameObject.AddComponent<Image>();
        bg.color = ScreenBg;
        bg.raycastTarget = true; // swallow clicks so nothing falls through

        // Header: title left, count right.
        var title = MakeText(_rootRT, "PHOTOS", 42, AccentCyan, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyles.Bold;
        var titleRT = title.rectTransform;
        titleRT.anchorMin = new Vector2(0f, 1f); titleRT.anchorMax = new Vector2(0f, 1f);
        titleRT.pivot = new Vector2(0f, 1f);
        titleRT.anchoredPosition = new Vector2(60f, -24f);
        titleRT.sizeDelta = new Vector2(420f, 50f);

        _countLabel = MakeText(_rootRT, "", 20, LabelWhite, TextAnchor.MiddleRight);
        var countRT = _countLabel.rectTransform;
        countRT.anchorMin = new Vector2(1f, 1f); countRT.anchorMax = new Vector2(1f, 1f);
        countRT.pivot = new Vector2(1f, 1f);
        countRT.anchoredPosition = new Vector2(-60f, -36f);
        countRT.sizeDelta = new Vector2(320f, 30f);

        // Footer hint.
        var hint = MakeText(_rootRT, "[ESC] BACK      [CLICK] VIEW", 16, LabelWhite, TextAnchor.MiddleRight);
        var hintRT = hint.rectTransform;
        hintRT.anchorMin = new Vector2(1f, 0f); hintRT.anchorMax = new Vector2(1f, 0f);
        hintRT.pivot = new Vector2(1f, 0f);
        hintRT.anchoredPosition = new Vector2(-60f, 16f);
        hintRT.sizeDelta = new Vector2(420f, 26f);

        // Scrollable thumbnail grid.
        var scrollRT = NewUI("Scroll", _rootRT);
        scrollRT.anchorMin = Vector2.zero; scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(60f, 56f);
        scrollRT.offsetMax = new Vector2(-60f, -86f);
        var scrollBg = scrollRT.gameObject.AddComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0.001f); // raycast surface for wheel scroll
        _scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        var viewportRT = NewUI("Viewport", scrollRT);
        Stretch(viewportRT, 0f);
        viewportRT.gameObject.AddComponent<RectMask2D>();
        _scroll.viewport = viewportRT;

        _gridContentRT = NewUI("Content", viewportRT);
        _gridContentRT.anchorMin = new Vector2(0f, 1f);
        _gridContentRT.anchorMax = new Vector2(1f, 1f);
        _gridContentRT.pivot = new Vector2(0.5f, 1f);
        _gridContentRT.offsetMin = Vector2.zero;
        _gridContentRT.offsetMax = Vector2.zero;
        var grid = _gridContentRT.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(280f, 190f);
        grid.spacing = new Vector2(16f, 16f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperCenter;
        var fitter = _gridContentRT.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll.content = _gridContentRT;

        // Empty state.
        _emptyLabel = MakeText(_rootRT, "No photos yet - press C while your phone is out to open the camera.",
                               22, LabelWhite, TextAnchor.MiddleCenter);
        _emptyLabel.enableWordWrapping = true;
        Stretch(_emptyLabel.rectTransform, 120f);
        _emptyLabel.gameObject.SetActive(false);

        // Fullscreen viewer (sibling AFTER the scroll → draws on top).
        _viewerRT = NewUI("Viewer", _rootRT);
        Stretch(_viewerRT, 0f);
        var vbg = _viewerRT.gameObject.AddComponent<Image>();
        vbg.color = ScreenBg;
        vbg.raycastTarget = true; // block grid interaction underneath

        // The AspectRatioFitter overrides its own RectTransform's offsets, so
        // the 60px inset must live on a wrapper the fitter operates inside.
        var photoFrame = NewUI("PhotoFrame", _viewerRT);
        Stretch(photoFrame, 60f);
        var photoRT = NewUI("Photo", photoFrame);
        _viewerImage = photoRT.gameObject.AddComponent<RawImage>();
        _viewerImage.raycastTarget = false;
        _viewerFitter = photoRT.gameObject.AddComponent<AspectRatioFitter>();
        _viewerFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        _viewerCaption = MakeText(_viewerRT, "", 20, LabelWhite, TextAnchor.MiddleCenter);
        var capRT = _viewerCaption.rectTransform;
        capRT.anchorMin = new Vector2(0.5f, 0f); capRT.anchorMax = new Vector2(0.5f, 0f);
        capRT.pivot = new Vector2(0.5f, 0f);
        capRT.anchoredPosition = new Vector2(0f, 16f);
        capRT.sizeDelta = new Vector2(900f, 30f);

        var backRT = NewUI("BackBtn", _viewerRT);
        backRT.anchorMin = new Vector2(0f, 1f); backRT.anchorMax = new Vector2(0f, 1f);
        backRT.pivot = new Vector2(0f, 1f);
        backRT.anchoredPosition = new Vector2(24f, -24f);
        backRT.sizeDelta = new Vector2(150f, 44f);
        var backBg = backRT.gameObject.AddComponent<Image>();
        backBg.color = ButtonGrey;
        var backLabel = MakeText(backRT, "< BACK", 18, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(backLabel.rectTransform, 0f);
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(CloseViewer);

        _viewerRT.gameObject.SetActive(false);
    }

    // ── Local UI helpers (house style: duplicated per procedural UI) ─

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    static TextMeshProUGUI MakeText(RectTransform parent, string text, float fontSize, Color color, TextAnchor anchor)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.enableWordWrapping = false;
        t.alignment = anchor switch
        {
            TextAnchor.MiddleLeft  => TextAlignmentOptions.MidlineLeft,
            TextAnchor.MiddleRight => TextAlignmentOptions.MidlineRight,
            _                      => TextAlignmentOptions.Midline,
        };
        t.raycastTarget = false;
        return t;
    }
}
