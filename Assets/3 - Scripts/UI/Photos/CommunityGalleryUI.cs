using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// COMMUNITY GALLERY modal launched from the main menu — browses photos other
/// players uploaded to the community-gallery Worker (GalleryApiClient). NOT
/// an auto-singleton: MainMenuController creates one lazily on first use and
/// reuses it for the lifetime of the menu scene (mirrors the credits-panel
/// modal pattern: SetActive toggle, no DontDestroyOnLoad).
///
/// Visual language + letterboxed-cell/fullscreen-viewer idiom mirror
/// PhotoGalleryUI (the local Photos app), adapted for a remote, paginated,
/// metadata-bearing feed instead of the local photo roll.
/// </summary>
public class CommunityGalleryUI : MonoBehaviour
{
    // ── Palette (mirrors PhotoGalleryUI for visual cohesion) ────────
    static readonly Color ScreenBg   = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ButtonGrey = new Color32(0x2A, 0x40, 0x60, 0xFF);

    bool            _built;
    GameObject      _rootPanelGO;
    bool            _isOpen;
    System.Action   _onBack;

    // Grid.
    RectTransform   _gridContentRT;
    ScrollRect      _scroll;
    readonly List<Texture2D> _gridTextures = new List<Texture2D>();
    string          _nextCursor;
    bool            _loadingPage;

    // Status / error / empty / not-configured messaging.
    TextMeshProUGUI _statusLabel;
    RectTransform   _retryButtonRT;
    TextMeshProUGUI _notConfiguredLabel;

    // Fullscreen viewer.
    RectTransform     _viewerRT;
    RawImage          _viewerImage;
    AspectRatioFitter _viewerFitter;
    TextMeshProUGUI   _viewerTitle;
    TextMeshProUGUI   _viewerDesc;
    Texture2D         _viewerTexture;
    bool              _viewerOpen;

    void Update()
    {
        if (!_isOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            if (_viewerOpen) CloseViewer();
            else Close();
        }
    }

    // ── Open / close ────────────────────────────────────────────────

    /// <summary>Shows the gallery. `onBack` is invoked when the player backs
    /// all the way out (BACK button or ESC from the grid) — MainMenuController
    /// passes HideCommunityGallery to re-show the menu button column.</summary>
    public void Open(System.Action onBack)
    {
        _onBack = onBack;
        EnsureBuilt();
        _isOpen = true;
        _rootPanelGO.SetActive(true);
        CloseViewer();
        _statusLabel.gameObject.SetActive(false);
        _retryButtonRT.gameObject.SetActive(false);

        if (!GalleryConfig.IsConfigured)
        {
            ShowNotConfigured();
            return;
        }
        HideNotConfigured();
        ClearGrid();
        _nextCursor = null;
        LoadPage(null);
    }

    /// <summary>BACK button / ESC from the grid — hides the panel, frees all
    /// downloaded textures, and hands control back to the caller.</summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        // In-flight List/LoadImage fetches guard on _isOpen so they can't leak
        // textures, but they'd still run to completion — stop them outright.
        // A killed List coroutine never runs its callback, so _loadingPage
        // must be cleared here or the next Open() refuses to load anything.
        StopAllCoroutines();
        _loadingPage = false;
        CloseViewer();
        ClearGrid();
        _nextCursor = null;
        if (_rootPanelGO != null) _rootPanelGO.SetActive(false);
        _onBack?.Invoke();
    }

    void ShowNotConfigured()
    {
        _notConfiguredLabel.gameObject.SetActive(true);
        _scroll.gameObject.SetActive(false);
        _statusLabel.gameObject.SetActive(false);
        _retryButtonRT.gameObject.SetActive(false);
    }

    void HideNotConfigured()
    {
        _notConfiguredLabel.gameObject.SetActive(false);
        _scroll.gameObject.SetActive(true);
    }

    // ── Paging ──────────────────────────────────────────────────────

    void LoadPage(string cursor)
    {
        if (_loadingPage) return;
        _loadingPage = true;
        _retryButtonRT.gameObject.SetActive(false);

        bool firstPage = string.IsNullOrEmpty(cursor);
        if (firstPage)
        {
            ClearGrid();
            _statusLabel.gameObject.SetActive(true);
            _statusLabel.text = "Loading...";
        }

        StartCoroutine(GalleryApiClient.List(cursor, 20, (ok, resp, err) =>
        {
            _loadingPage = false;
            if (!_isOpen) return; // closed while the request was in flight

            if (!ok)
            {
                _statusLabel.gameObject.SetActive(true);
                _statusLabel.text = "Couldn't reach the community gallery.";
                _retryButtonRT.gameObject.SetActive(true);
                return;
            }

            _nextCursor = resp.nextCursor;

            if (firstPage && (resp.items == null || resp.items.Length == 0))
            {
                _statusLabel.gameObject.SetActive(true);
                _statusLabel.text = "No photos yet - be the first to share one!";
                return;
            }
            _statusLabel.gameObject.SetActive(false);

            if (resp.items != null)
                foreach (var item in resp.items)
                    BuildCell(item);

            if (firstPage) _gridContentRT.anchoredPosition = Vector2.zero;
        }));
    }

    void OnScrollChanged(Vector2 pos)
    {
        if (!_isOpen || _viewerOpen) return;
        if (_loadingPage || string.IsNullOrEmpty(_nextCursor)) return;
        if (_scroll.verticalNormalizedPosition < 0.15f)
            LoadPage(_nextCursor);
    }

    // ── Grid ────────────────────────────────────────────────────────

    void BuildCell(GalleryApiClient.RemotePhoto item)
    {
        var cell = NewUI("Cell_" + item.id, _gridContentRT);
        var bg = cell.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.raycastTarget = true;

        var imgRT = NewUI("Thumb", cell);
        var raw = imgRT.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.color = new Color(1f, 1f, 1f, 0f); // hidden until the download lands
        var fit = imgRT.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; // letterbox both orientations
        fit.aspectRatio = 1f;

        var btn = cell.gameObject.AddComponent<Button>();
        var captured = item;
        btn.onClick.AddListener(() => OpenViewer(captured));

        StartCoroutine(GalleryApiClient.LoadImage(item.id, item.imageUrl, (ok, tex, err) =>
        {
            if (!_isOpen) { if (ok && tex != null) Destroy(tex); return; } // gallery closed mid-flight
            if (!ok || tex == null) return; // leave the cell blank — rest of the grid still works
            if (raw == null) { Destroy(tex); return; } // cell was cleared (grid reload) before this landed
            _gridTextures.Add(tex);
            raw.texture = tex;
            raw.color = Color.white;
            fit.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);
        }));
    }

    void ClearGrid()
    {
        if (_gridContentRT != null)
            for (int i = _gridContentRT.childCount - 1; i >= 0; i--)
                Destroy(_gridContentRT.GetChild(i).gameObject);
        foreach (var t in _gridTextures) if (t != null) Destroy(t);
        _gridTextures.Clear();
    }

    // ── Fullscreen viewer ───────────────────────────────────────────

    void OpenViewer(GalleryApiClient.RemotePhoto item)
    {
        CloseViewer();
        _viewerTitle.text = string.IsNullOrEmpty(item.title) ? "Untitled" : item.title;
        _viewerDesc.text = item.description ?? "";
        _viewerImage.texture = null;
        _viewerImage.color = new Color(1f, 1f, 1f, 0f);
        _viewerFitter.aspectRatio = 1f;
        _viewerRT.gameObject.SetActive(true);
        _viewerOpen = true;
        LockNavBehindViewer(true);

        // Same imageUrl serves the grid thumb and the full view — GalleryApiClient
        // disk-caches by id, so this is typically an instant cache hit.
        StartCoroutine(GalleryApiClient.LoadImage(item.id, item.imageUrl, (ok, tex, err) =>
        {
            if (!_isOpen || !_viewerOpen) { if (ok && tex != null) Destroy(tex); return; }
            if (!ok || tex == null) return; // viewer stays open with title/description, image blank
            if (_viewerTexture != null) Destroy(_viewerTexture);
            _viewerTexture = tex;
            _viewerImage.texture = tex;
            _viewerImage.color = Color.white;
            _viewerFitter.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);
        }));
    }

    void CloseViewer()
    {
        if (!_viewerOpen && _viewerTexture == null) return;
        _viewerOpen = false;
        LockNavBehindViewer(false);
        if (_viewerRT != null) _viewerRT.gameObject.SetActive(false);
        if (_viewerImage != null) _viewerImage.texture = null;
        if (_viewerTexture != null) { Destroy(_viewerTexture); _viewerTexture = null; }
    }

    // Selectables we disabled while the fullscreen viewer is open, so pad
    // navigation can't wander onto the thumbnail grid behind it (same canvas,
    // so ControllerUINavigator's per-canvas suppression can't isolate the
    // viewer). The viewer's own BACK button stays enabled and receives focus.
    readonly List<UnityEngine.UI.Selectable> _navLockedBehindViewer
        = new List<UnityEngine.UI.Selectable>();

    void LockNavBehindViewer(bool locked)
    {
        if (locked)
        {
            _navLockedBehindViewer.Clear();
            if (_rootPanelGO == null) return;
            var sels = _rootPanelGO.GetComponentsInChildren<UnityEngine.UI.Selectable>(false);
            foreach (var s in sels)
            {
                if (_viewerRT != null && s.transform.IsChildOf(_viewerRT)) continue;
                if (!s.enabled) continue;
                s.enabled = false;
                _navLockedBehindViewer.Add(s);
            }
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null &&
                (_viewerRT == null || !es.currentSelectedGameObject.transform.IsChildOf(_viewerRT)))
                es.SetSelectedGameObject(null);
        }
        else
        {
            foreach (var s in _navLockedBehindViewer) if (s != null) s.enabled = true;
            _navLockedBehindViewer.Clear();
        }
    }

    // ── Build (once, lazily) ────────────────────────────────────────

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 200; // same modal tier as MainMenuController's credits panel
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // This is a NESTED canvas (the GameObject lives under the main-menu
        // canvas), so AddComponent<Canvas> gave us a default zero-size
        // RectTransform centred on the parent. Left as-is, every child
        // (stretched to it) collapses to a point. Stretch our own rect to
        // fill the parent canvas so the gallery envelops the whole screen,
        // exactly like the root-canvas Photos app.
        var selfRT = transform as RectTransform;
        if (selfRT != null)
        {
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = Vector2.zero;
            selfRT.offsetMax = Vector2.zero;
            selfRT.localScale = Vector3.one;
            selfRT.anchoredPosition = Vector2.zero;
        }

        _rootPanelGO = NewUI("Root", transform).gameObject;
        var rootRT = (RectTransform)_rootPanelGO.transform;
        Stretch(rootRT, 0f);
        var bg = _rootPanelGO.AddComponent<Image>();
        bg.color = ScreenBg;
        bg.raycastTarget = true; // swallow clicks so nothing falls through to the menu

        // Header: title left, back button right.
        var title = MakeText(rootRT, "COMMUNITY GALLERY", 42, AccentCyan, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyles.Bold;
        var titleRT = title.rectTransform;
        titleRT.anchorMin = new Vector2(0f, 1f); titleRT.anchorMax = new Vector2(0f, 1f);
        titleRT.pivot = new Vector2(0f, 1f);
        titleRT.anchoredPosition = new Vector2(60f, -24f);
        titleRT.sizeDelta = new Vector2(700f, 50f);

        var backRT = NewUI("BackBtn", rootRT);
        backRT.anchorMin = new Vector2(1f, 1f); backRT.anchorMax = new Vector2(1f, 1f);
        backRT.pivot = new Vector2(1f, 1f);
        backRT.anchoredPosition = new Vector2(-60f, -24f);
        backRT.sizeDelta = new Vector2(150f, 44f);
        var backBg = backRT.gameObject.AddComponent<Image>();
        backBg.color = ButtonGrey;
        var backLabel = MakeText(backRT, "< BACK", 18, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(backLabel.rectTransform, 0f);
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(Close);

        // Footer hint.
        var hint = MakeText(rootRT, "[ESC] BACK      [CLICK] VIEW", 16, LabelWhite, TextAnchor.MiddleRight);
        var hintRT = hint.rectTransform;
        hintRT.anchorMin = new Vector2(1f, 0f); hintRT.anchorMax = new Vector2(1f, 0f);
        hintRT.pivot = new Vector2(1f, 0f);
        hintRT.anchoredPosition = new Vector2(-60f, 16f);
        hintRT.sizeDelta = new Vector2(420f, 26f);

        // Scrollable thumbnail grid.
        var scrollRT = NewUI("Scroll", rootRT);
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
        _scroll.onValueChanged.AddListener(OnScrollChanged);

        // Status / error / empty label (centered over the grid area).
        _statusLabel = MakeText(rootRT, "", 22, LabelWhite, TextAnchor.MiddleCenter);
        _statusLabel.enableWordWrapping = true;
        Stretch(_statusLabel.rectTransform, 120f);
        _statusLabel.gameObject.SetActive(false);

        // Retry button (shown under the status label on failure).
        _retryButtonRT = NewUI("RetryBtn", rootRT);
        _retryButtonRT.anchorMin = new Vector2(0.5f, 0.5f); _retryButtonRT.anchorMax = new Vector2(0.5f, 0.5f);
        _retryButtonRT.pivot = new Vector2(0.5f, 0.5f);
        _retryButtonRT.anchoredPosition = new Vector2(0f, -60f);
        _retryButtonRT.sizeDelta = new Vector2(200f, 52f);
        var retryBg = _retryButtonRT.gameObject.AddComponent<Image>();
        retryBg.color = ButtonGrey;
        var retryLabel = MakeText(_retryButtonRT, "RETRY", 20, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(retryLabel.rectTransform, 0f);
        var retryBtn = _retryButtonRT.gameObject.AddComponent<Button>();
        retryBtn.onClick.AddListener(() => LoadPage(null));
        _retryButtonRT.gameObject.SetActive(false);

        // Not-configured message (dev hasn't deployed the server yet).
        _notConfiguredLabel = MakeText(rootRT, "Community gallery isn't set up yet.", 24, LabelWhite, TextAnchor.MiddleCenter);
        _notConfiguredLabel.enableWordWrapping = true;
        Stretch(_notConfiguredLabel.rectTransform, 120f);
        _notConfiguredLabel.gameObject.SetActive(false);

        BuildViewer(rootRT);

        _rootPanelGO.SetActive(false); // hidden until Open()
    }

    void BuildViewer(RectTransform parent)
    {
        _viewerRT = NewUI("Viewer", parent);
        Stretch(_viewerRT, 0f);
        var vbg = _viewerRT.gameObject.AddComponent<Image>();
        vbg.color = ScreenBg;
        vbg.raycastTarget = true; // block grid interaction underneath

        _viewerTitle = MakeText(_viewerRT, "", 30, AccentCyan, TextAnchor.MiddleCenter);
        _viewerTitle.fontStyle = FontStyles.Bold;
        _viewerTitle.enableWordWrapping = true;
        var vTitleRT = _viewerTitle.rectTransform;
        vTitleRT.anchorMin = new Vector2(0.5f, 1f); vTitleRT.anchorMax = new Vector2(0.5f, 1f);
        vTitleRT.pivot = new Vector2(0.5f, 1f);
        vTitleRT.anchoredPosition = new Vector2(0f, -24f);
        vTitleRT.sizeDelta = new Vector2(1000f, 50f);

        // The AspectRatioFitter overrides its own RectTransform's offsets, so
        // the inset must live on a wrapper the fitter operates inside.
        var photoFrame = NewUI("PhotoFrame", _viewerRT);
        photoFrame.anchorMin = Vector2.zero; photoFrame.anchorMax = Vector2.one;
        photoFrame.offsetMin = new Vector2(80f, 150f);
        photoFrame.offsetMax = new Vector2(-80f, -90f);
        var photoRT = NewUI("Photo", photoFrame);
        _viewerImage = photoRT.gameObject.AddComponent<RawImage>();
        _viewerImage.raycastTarget = false;
        _viewerFitter = photoRT.gameObject.AddComponent<AspectRatioFitter>();
        _viewerFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        _viewerDesc = MakeText(_viewerRT, "", 18, LabelWhite, TextAnchor.MiddleCenter);
        _viewerDesc.enableWordWrapping = true;
        var vDescRT = _viewerDesc.rectTransform;
        vDescRT.anchorMin = new Vector2(0.5f, 0f); vDescRT.anchorMax = new Vector2(0.5f, 0f);
        vDescRT.pivot = new Vector2(0.5f, 0f);
        vDescRT.anchoredPosition = new Vector2(0f, 50f);
        vDescRT.sizeDelta = new Vector2(1000f, 60f);

        var viewerBackRT = NewUI("ViewerBackBtn", _viewerRT);
        viewerBackRT.anchorMin = new Vector2(0f, 0f); viewerBackRT.anchorMax = new Vector2(0f, 0f);
        viewerBackRT.pivot = new Vector2(0f, 0f);
        viewerBackRT.anchoredPosition = new Vector2(24f, 24f);
        viewerBackRT.sizeDelta = new Vector2(190f, 44f);
        var viewerBackBg = viewerBackRT.gameObject.AddComponent<Image>();
        viewerBackBg.color = ButtonGrey;
        var viewerBackLabel = MakeText(viewerBackRT, "< BACK TO GRID", 16, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(viewerBackLabel.rectTransform, 0f);
        var viewerBackBtn = viewerBackRT.gameObject.AddComponent<Button>();
        viewerBackBtn.onClick.AddListener(CloseViewer);

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
