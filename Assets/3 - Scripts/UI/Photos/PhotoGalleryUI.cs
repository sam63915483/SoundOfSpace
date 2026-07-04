using System.Collections;
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
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            ConsumedEscapeThisFrame = true;
            Back();
        }
    }

    void LateUpdate() { ConsumedEscapeThisFrame = false; }

    /// <summary>ESC / back-button chain: viewer → grid → exit to phone.</summary>
    public void Back()
    {
        if (_uploadModalRT != null && _uploadModalRT.gameObject.activeSelf) { CloseUploadModal(); return; }
        if (_viewerOpen) { CloseViewer(); return; }
        if (PlayerPhoneUI.Instance != null) PlayerPhoneUI.Instance.BeginGalleryExit();
        else ForceClose(); // no phone to return to — just drop the gates
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

        _viewerEntry = entry;
        _viewerRT.gameObject.SetActive(true);
        _viewerOpen = true;
        RefreshUploadButton();
    }

    void CloseViewer()
    {
        if (!_viewerOpen && _viewerTexture == null) return;
        _viewerOpen = false;
        CloseUploadModal();
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

        var uploadRT = NewUI("UploadBtn", _viewerRT);
        uploadRT.anchorMin = new Vector2(1f, 1f); uploadRT.anchorMax = new Vector2(1f, 1f);
        uploadRT.pivot = new Vector2(1f, 1f);
        uploadRT.anchoredPosition = new Vector2(-24f, -24f);
        uploadRT.sizeDelta = new Vector2(150f, 44f);
        var uploadBg = uploadRT.gameObject.AddComponent<Image>();
        uploadBg.color = ButtonGrey;
        _uploadButtonLabel = MakeText(uploadRT, "UPLOAD", 18, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(_uploadButtonLabel.rectTransform, 0f);
        _uploadButton = uploadRT.gameObject.AddComponent<Button>();
        _uploadButton.onClick.AddListener(OpenUploadModal);

        BuildUploadModal();

        _viewerRT.gameObject.SetActive(false);
    }

    // ── Upload modal (Plan B, Task 2) ───────────────────────────────

    void BuildUploadModal()
    {
        // Dim backdrop + centered card, child of the viewer so it tears
        // down with it. Hidden until OpenUploadModal.
        _uploadModalRT = NewUI("UploadModal", _viewerRT);
        Stretch(_uploadModalRT, 0f);
        var modalBackdrop = _uploadModalRT.gameObject.AddComponent<Image>();
        modalBackdrop.color = new Color(0f, 0f, 0f, 0.75f);
        modalBackdrop.raycastTarget = true; // block clicks to the viewer underneath

        var cardRT = NewUI("Card", _uploadModalRT);
        cardRT.anchorMin = new Vector2(0.5f, 0.5f); cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(600f, 420f);
        var cardBg = cardRT.gameObject.AddComponent<Image>();
        cardBg.color = ScreenBg;
        cardBg.raycastTarget = true;

        var modalTitle = MakeText(cardRT, "SHARE TO COMMUNITY", 24, AccentCyan, TextAnchor.MiddleCenter);
        modalTitle.fontStyle = FontStyles.Bold;
        var modalTitleRT = modalTitle.rectTransform;
        modalTitleRT.anchorMin = new Vector2(0f, 1f); modalTitleRT.anchorMax = new Vector2(1f, 1f);
        modalTitleRT.pivot = new Vector2(0.5f, 1f);
        modalTitleRT.anchoredPosition = new Vector2(0f, -20f);
        modalTitleRT.sizeDelta = new Vector2(0f, 34f);

        var titleFieldLabel = MakeText(cardRT, "Title", 16, LabelWhite, TextAnchor.MiddleLeft);
        var titleFieldLabelRT = titleFieldLabel.rectTransform;
        titleFieldLabelRT.anchorMin = new Vector2(0f, 1f); titleFieldLabelRT.anchorMax = new Vector2(0f, 1f);
        titleFieldLabelRT.pivot = new Vector2(0f, 1f);
        titleFieldLabelRT.anchoredPosition = new Vector2(30f, -70f);
        titleFieldLabelRT.sizeDelta = new Vector2(200f, 24f);

        _uploadTitleInput = BuildInputField("TitleInput", cardRT, new Vector2(30f, -96f), new Vector2(540f, 40f),
            false, "A title for your photo...");

        var descFieldLabel = MakeText(cardRT, "Description", 16, LabelWhite, TextAnchor.MiddleLeft);
        var descFieldLabelRT = descFieldLabel.rectTransform;
        descFieldLabelRT.anchorMin = new Vector2(0f, 1f); descFieldLabelRT.anchorMax = new Vector2(0f, 1f);
        descFieldLabelRT.pivot = new Vector2(0f, 1f);
        descFieldLabelRT.anchoredPosition = new Vector2(30f, -150f);
        descFieldLabelRT.sizeDelta = new Vector2(200f, 24f);

        _uploadDescInput = BuildInputField("DescInput", cardRT, new Vector2(30f, -176f), new Vector2(540f, 110f),
            true, "Say something about it (optional)...");

        _uploadStatusLabel = MakeText(cardRT, "", 15, LabelWhite, TextAnchor.MiddleCenter);
        var statusRT = _uploadStatusLabel.rectTransform;
        statusRT.anchorMin = new Vector2(0f, 0f); statusRT.anchorMax = new Vector2(1f, 0f);
        statusRT.pivot = new Vector2(0.5f, 0f);
        statusRT.anchoredPosition = new Vector2(0f, 66f);
        statusRT.sizeDelta = new Vector2(-60f, 26f);
        _uploadStatusLabel.enableWordWrapping = true;

        var cancelRT = NewUI("CancelBtn", cardRT);
        cancelRT.anchorMin = new Vector2(0f, 0f); cancelRT.anchorMax = new Vector2(0f, 0f);
        cancelRT.pivot = new Vector2(0f, 0f);
        cancelRT.anchoredPosition = new Vector2(30f, 24f);
        cancelRT.sizeDelta = new Vector2(150f, 44f);
        var cancelBg = cancelRT.gameObject.AddComponent<Image>();
        cancelBg.color = ButtonGrey;
        var cancelLabel = MakeText(cancelRT, "CANCEL", 18, LabelWhite, TextAnchor.MiddleCenter);
        Stretch(cancelLabel.rectTransform, 0f);
        var cancelBtn = cancelRT.gameObject.AddComponent<Button>();
        cancelBtn.onClick.AddListener(CloseUploadModal);

        var submitRT = NewUI("SubmitBtn", cardRT);
        submitRT.anchorMin = new Vector2(1f, 0f); submitRT.anchorMax = new Vector2(1f, 0f);
        submitRT.pivot = new Vector2(1f, 0f);
        submitRT.anchoredPosition = new Vector2(-30f, 24f);
        submitRT.sizeDelta = new Vector2(150f, 44f);
        var submitBg = submitRT.gameObject.AddComponent<Image>();
        submitBg.color = AccentCyan;
        var submitLabel = MakeText(submitRT, "SUBMIT", 18, ScreenBg, TextAnchor.MiddleCenter);
        Stretch(submitLabel.rectTransform, 0f);
        _uploadSubmitBtn = submitRT.gameObject.AddComponent<Button>();
        _uploadSubmitBtn.onClick.AddListener(SubmitUpload);

        _uploadModalRT.gameObject.SetActive(false);
    }

    void OpenUploadModal()
    {
        if (_viewerEntry == null || !GalleryConfig.IsConfigured) return;
        if (_viewerEntry.uploaded) return; // already up
        _uploadTitleInput.text = "";
        _uploadDescInput.text = "";
        _uploadStatusLabel.text = "";
        SetUploadInteractable(true);
        _uploadModalRT.gameObject.SetActive(true);
    }

    void CloseUploadModal()
    {
        if (_uploadRoutine != null) { StopCoroutine(_uploadRoutine); _uploadRoutine = null; }
        if (_uploadModalRT != null) _uploadModalRT.gameObject.SetActive(false);
    }

    void SubmitUpload()
    {
        string title = (_uploadTitleInput.text ?? "").Trim();
        if (title.Length == 0) { _uploadStatusLabel.text = "Title is required."; return; }
        if (title.Length > 100) title = title.Substring(0, 100);
        string desc = (_uploadDescInput.text ?? "").Trim();
        if (desc.Length > 500) desc = desc.Substring(0, 500);
        if (_viewerTexture == null) { _uploadStatusLabel.text = "No image loaded."; return; }

        SetUploadInteractable(false);
        _uploadStatusLabel.text = "Uploading…";
        var id = _viewerEntry.id;
        _uploadRoutine = StartCoroutine(GalleryApiClient.Upload(_viewerTexture, title, desc, (ok, remoteId, err) =>
        {
            _uploadRoutine = null;
            if (ok)
            {
                if (PhotoLibrary.Instance != null) PhotoLibrary.Instance.MarkUploaded(id, title);
                if (_viewerEntry != null && _viewerEntry.id == id) _viewerEntry.uploaded = true;
                _uploadStatusLabel.text = "Uploaded! It'll appear after review.";
                RefreshUploadButton();
                StartCoroutine(CloseModalAfter(1.2f));
            }
            else
            {
                _uploadStatusLabel.text = err ?? "Upload failed.";
                SetUploadInteractable(true);
            }
        }));
    }

    IEnumerator CloseModalAfter(float sec)
    {
        float t = 0f; while (t < sec) { t += Time.unscaledDeltaTime; yield return null; }
        CloseUploadModal();
    }

    void SetUploadInteractable(bool on)
    {
        if (_uploadTitleInput != null) _uploadTitleInput.interactable = on;
        if (_uploadDescInput != null)  _uploadDescInput.interactable = on;
        if (_uploadSubmitBtn != null)  _uploadSubmitBtn.interactable = on;
    }

    void RefreshUploadButton()
    {
        if (_uploadButton == null) return;
        bool show = GalleryConfig.IsConfigured;
        _uploadButton.gameObject.SetActive(show);
        if (show && _uploadButtonLabel != null)
        {
            bool up = _viewerEntry != null && _viewerEntry.uploaded;
            _uploadButtonLabel.text = up ? "UPLOADED ✓" : "UPLOAD";
            _uploadButton.interactable = !up;
        }
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

    // TMP_InputField requires a Text Area child (RectMask2D) with a Text
    // component + optional placeholder wired via textComponent/placeholder/
    // textViewport — mirrors the working AIChatScreen/SaveLoadUI pattern.
    static TMP_InputField BuildInputField(string name, RectTransform parent, Vector2 anchoredPos, Vector2 size,
                                           bool multiline, string placeholderText)
    {
        var fieldRT = NewUI(name, parent);
        fieldRT.anchorMin = new Vector2(0f, 1f); fieldRT.anchorMax = new Vector2(0f, 1f);
        fieldRT.pivot = new Vector2(0f, 1f);
        fieldRT.anchoredPosition = anchoredPos;
        fieldRT.sizeDelta = size;
        var fieldBg = fieldRT.gameObject.AddComponent<Image>();
        fieldBg.color = TileBg;
        fieldBg.raycastTarget = true;
        var input = fieldRT.gameObject.AddComponent<TMP_InputField>();

        var textAreaRT = NewUI("Text Area", fieldRT);
        textAreaRT.anchorMin = Vector2.zero; textAreaRT.anchorMax = Vector2.one;
        textAreaRT.offsetMin = new Vector2(10f, 6f); textAreaRT.offsetMax = new Vector2(-10f, -6f);
        textAreaRT.gameObject.AddComponent<RectMask2D>();

        var textRT = NewUI("Text", textAreaRT);
        Stretch(textRT, 0f);
        var textComp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(textComp);
        textComp.fontSize = 18;
        textComp.color = LabelWhite;
        textComp.alignment = TextAlignmentOptions.TopLeft;
        textComp.enableWordWrapping = multiline;
        textComp.raycastTarget = false;

        var placeRT = NewUI("Placeholder", textAreaRT);
        Stretch(placeRT, 0f);
        var placeComp = placeRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(placeComp);
        placeComp.fontSize = 18;
        placeComp.color = new Color(LabelWhite.r, LabelWhite.g, LabelWhite.b, 0.4f);
        placeComp.alignment = TextAlignmentOptions.TopLeft;
        placeComp.text = placeholderText;
        placeComp.enableWordWrapping = multiline;
        placeComp.raycastTarget = false;

        input.textComponent = textComp;
        input.placeholder = placeComp;
        input.textViewport = textAreaRT;
        input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        input.characterLimit = multiline ? 500 : 100;
        return input;
    }

    // ── Upload state (Plan B, Task 2) — appended per house rule ──────
    PhotoLibrary.PhotoEntry _viewerEntry;
    RectTransform     _uploadModalRT;
    TMP_InputField    _uploadTitleInput;
    TMP_InputField    _uploadDescInput;
    Button            _uploadSubmitBtn;
    TextMeshProUGUI   _uploadStatusLabel;
    Button            _uploadButton;
    TextMeshProUGUI   _uploadButtonLabel;
    Coroutine         _uploadRoutine;
}
