using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class BuildMenuUI : MonoBehaviour
{
    [Header("Buildables")]
    public BuildableEntry[] buildables;

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.N;

    [Header("Placement")]
    public float ghostStartDistance = 2.5f;
    public float ghostMinDistance = 1f;
    public float ghostMaxDistance = 12f;
    public float scrollSensitivity = 1.2f;
    public float rotationSensitivity = 4f;


    GameObject menuRoot;
    GameObject scannerPanel;
    TextMeshProUGUI headerWoodText;
    RawImage    previewImage;
    TextMeshProUGUI specName;
    TextMeshProUGUI specClass;
    TextMeshProUGUI specCost;
    TextMeshProUGUI specSize;
    TextMeshProUGUI specDesc;
    Button      placeBtn;
    TextMeshProUGUI placeBtnLabel;
    Image       placeBtnBg;
    BuildableEntry detailEntry;
    GameObject  _selectedRow;

    Transform listContent;              // viewport content where rows live
    bool       _filterActive;
    BuildableCategory _activeFilter;
    readonly List<Button> _tabButtons = new List<Button>();
    readonly List<BuildableCategory?> _tabFilters = new List<BuildableCategory?>(); // null = All
    readonly List<(GameObject row, BuildableCategory cat, BuildableEntry entry)> _rowEntries = new List<(GameObject, BuildableCategory, BuildableEntry)>();

    // Runtime preview rig — mirrors the FishingdexManager / GoodsVendorShopUI pattern.
    // A dedicated camera off-screen renders each prefab once into a cached RenderTexture.
    // The bounding-sphere fit math frames any size of prefab cleanly into the icon frame.
    Camera _previewCamera;
    Transform _previewStage;
    const int PreviewLayer = 31;
    // Single render size used for both the card thumbnail (150 px) and the detail
    // panel's larger icon (440 px). 384² is a comfortable middle ground — sharp on
    // the detail view, fine downscaled on cards, and the per-prefab GPU memory
    // (~580 KB ARGB32) stays reasonable across ~60 entries.
    const int PreviewSize = 384;
    readonly Dictionary<GameObject, RenderTexture> _previewCache = new Dictionary<GameObject, RenderTexture>();

    bool isOpen;
    GhostPlacement activePlacement;
    int _placementEndedFrame = -1; // frame on which the active placement finished;
                                   // used to suppress the toggle-key re-opening the menu
                                   // on the same frame the user pressed it to exit placement
    Ship _shipCached;

    // When set (by tutorial / scripted flow), the next placement's onFinished
    // re-opens the build menu instead of returning straight to gameplay.
    // Auto-cleared on consume. Paired with GhostPlacement.s_finishAfterNextPlacement
    // to deliver "place once, return to menu, press N to exit" UX.
    static bool s_reopenAfterFinish;
    public static void RequestReopenAfterPlacement() { s_reopenAfterFinish = true; }
    static BuildMenuUI s_instance;
    public static BuildMenuUI Instance => s_instance;

    // Read by TabbedPauseMenu to suppress the pause-menu ESC handler while
    // this panel is open — otherwise the same ESC keypress that the footer
    // advertises as "[ESC] CLOSE" also brings the pause menu up underneath.
    public static bool IsOpen => s_instance != null && s_instance.isOpen;

    CursorLockMode prevLockMode;
    bool prevCursorVisible;

    void Start()
    {
        s_instance = this;
        EnsureEventSystem();
        BuildUI();
    }

    void OnDisable()
    {
        if (s_instance == this) s_instance = null;
    }

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        if (activePlacement != null) return;

        // If the active placement just ended this frame (e.g., player pressed N to
        // exit), don't let that same N press immediately re-open the menu. Script
        // execution order between BuildMenuUI and GhostPlacement is undefined, so
        // we guard against both orderings.
        if (Time.frameCount == _placementEndedFrame) return;

        // Configurable keyboard key (default N) OR controller LB. NEVER open
        // while piloting the ship — LB is also the ship's roll-counter binding,
        // and players were accidentally popping the build menu mid-flight.
        // PilotedInstance is the cached static set on PilotShip / cleared on
        // exit — no FindObjectOfType per Update needed. Used to be 0.03 ms
        // FindObject + some intrinsic cost; this is O(1).
        bool piloting = Ship.PilotedInstance != null;
        bool toggleDown = Input.GetKeyDown(toggleKey) ||
            (!piloting && TutorialGate.PadPressed(TutorialGate.PadButton.LB));
        if (toggleDown && !piloting)
        {
            // Closing is always allowed; opening is gated on TutorialAbility.BuildMenu
            // so pressing N before the OpenBuildMenuStep tutorial step can't pop
            // the menu. Post-tutorial, UnlockAll makes the open path pass through.
            if (isOpen) Close();
            else if (TutorialGate.IsUnlocked(TutorialAbility.BuildMenu)) Open();
        }
        else if (isOpen && piloting)
        {
            // If the player somehow ended up piloting with the menu still open
            // (e.g. clicked Pilot Ship via mouse), close it to avoid input lock.
            Close();
        }
        else if (isOpen && TutorialGate.CancelPressed())
        {
            Close();
        }

        if (isOpen)
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            if (wood != _lastWoodSeen)
            {
                _lastWoodSeen = wood;
                if (headerWoodText != null) headerWoodText.text = "WOOD " + wood;
                if (detailEntry != null) RefreshDetailCost();
            }
        }
    }

    // Fired once each time the build menu opens. Subscribed to by tutorial
    // steps (OpenBuildMenuStep) to detect first-time use.
    public static event System.Action OnOpened;

    public void Open()
    {
        if (isOpen || menuRoot == null) return;
        isOpen = true;
        prevLockMode = Cursor.lockState;
        prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerController.isInDialogue = true;
        menuRoot.SetActive(true);
        ShowList();
        OnOpened?.Invoke();
    }

    int _lastWoodSeen = int.MinValue;

    void RefreshDetailCost()
    {
        if (specCost == null || detailEntry == null) return;
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        if (detailEntry.woodCost <= 0)
        {
            specCost.text = "FREE";
            specCost.color = CyanScannerPalette.AccentDim;
        }
        else
        {
            bool affordable = wood >= detailEntry.woodCost;
            specCost.text  = detailEntry.woodCost + " WOOD";
            specCost.color = affordable ? CyanScannerPalette.CostAfford : CyanScannerPalette.CostUnafford;
        }
        if (placeBtn != null)
            placeBtn.interactable = detailEntry.woodCost <= 0 || wood >= detailEntry.woodCost;
        if (placeBtnBg != null)
            placeBtnBg.color = placeBtn.interactable ? CyanScannerPalette.BtnPrimary : CyanScannerPalette.BtnNormalEdge;
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        menuRoot.SetActive(false);
        Cursor.lockState = prevLockMode == CursorLockMode.None ? CursorLockMode.Locked : prevLockMode;
        Cursor.visible = false;
        PlayerController.isInDialogue = false;
    }

    // Closes the menu without re-locking input — used when handing off to placement mode
    // (placement mode runs with cursor locked / look enabled / no isInDialogue gate).
    void CloseForPlacement()
    {
        if (!isOpen) return;
        isOpen = false;
        menuRoot.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlayerController.isInDialogue = false;
    }

    void ShowList() { /* single-panel layout — selection clears the spec */ }

    void ShowDetail(BuildableEntry e)
    {
        detailEntry = e;
        if (specName  != null) specName.text  = e.displayName.ToUpper();
        if (specClass != null) specClass.text = e.category.ToString().ToUpper();
        if (specSize  != null)
        {
            Vector3 size = GetCachedPrefabSize(e.prefab);
            specSize.text = $"{size.x:F1}×{size.z:F1} m";
        }
        if (specDesc  != null) specDesc.text  = e.description;

        if (previewImage != null)
        {
            if (e.icon != null) { previewImage.texture = e.icon.texture; previewImage.color = Color.white; }
            else
            {
                var rt = GetOrRenderPreview(e.prefab, PreviewSize);
                if (rt != null) { previewImage.texture = rt; previewImage.color = Color.white; }
                else            { previewImage.texture = null; previewImage.color = new Color(1, 1, 1, 0); }
            }
        }
        RefreshDetailCost();
    }

    void OnPlaceClicked()
    {
        if (detailEntry == null || detailEntry.prefab == null) return;

        var go = new GameObject("GhostPlacement_Runtime");
        activePlacement = go.AddComponent<GhostPlacement>();
        activePlacement.onFinished = () =>
        {
            activePlacement = null;
            _placementEndedFrame = Time.frameCount;
            // If a tutorial / scripted flow requested it, re-open the menu now
            // instead of leaving the player in gameplay (cabin one-shot UX).
            if (s_reopenAfterFinish)
            {
                s_reopenAfterFinish = false;
                Open();
            }
        };
        activePlacement.Begin(detailEntry, this);

        CloseForPlacement();
    }

    // ───────────────────────── UI Construction ─────────────────────────

    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    void BuildUI()
    {
        Canvas canvas = FindOrCreateCanvas();

        menuRoot = NewUIObject("BuildMenuRoot", canvas.transform);
        StretchFull(menuRoot.GetComponent<RectTransform>());
        var bg = menuRoot.AddComponent<Image>();
        bg.color = new Color32(0, 0, 0, 200);  // dim background — focus on the panel
        bg.raycastTarget = true;

        BuildScannerPanel();
        menuRoot.SetActive(false);
    }

    Canvas FindOrCreateCanvas()
    {
        var canvases = FindObjectsOfType<Canvas>();
        foreach (var c in canvases)
            if (c.name == "HUD_Canvas") return c;

        var go = new GameObject("BuildMenu_Canvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    void BuildScannerPanel()
    {
        scannerPanel = NewUIObject("ScannerPanel", menuRoot.transform);
        var prt = scannerPanel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(960, 720);
        prt.anchoredPosition = Vector2.zero;
        var bg = scannerPanel.AddComponent<Image>();
        bg.color = CyanScannerPalette.PanelBg;

        AddPanelBorder(scannerPanel.transform);

        BuildHeader(scannerPanel.transform);
        BuildTabRow(scannerPanel.transform);
        BuildBody(scannerPanel.transform);
        BuildFooter(scannerPanel.transform);

        RebuildVisibleRows();
    }

    void AddPanelBorder(Transform parent)
    {
        // Thin cyan border around the whole panel. Four edge strips so they
        // never overlap the body content.
        AddEdge(parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 1));   // top
        AddEdge(parent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 1));   // bottom
        AddEdge(parent, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(1, 0));   // left
        AddEdge(parent, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(1, 0));   // right
    }

    static void AddEdge(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeAxis)
    {
        var go = NewUIObject("Edge", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        // sizeAxis encodes "this axis is the edge thickness":
        //   (0, 1) → vertical edge (left/right): thickness=1px in X, stretches in Y
        //   (1, 0) → horizontal edge (top/bottom): stretches in X, 1px in Y
        rt.sizeDelta = new Vector2(sizeAxis.x * 0, sizeAxis.y * 0); // start with 0/0
        rt.sizeDelta = new Vector2(sizeAxis.x == 0 ? 1f : 0f, sizeAxis.y == 0 ? 1f : 0f);
        var img = go.AddComponent<Image>();
        img.color = CyanScannerPalette.PanelBorder;
        img.raycastTarget = false;
    }

    void BuildHeader(Transform parent)
    {
        var headerGo = NewUIObject("Header", parent);
        var hrt = headerGo.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1);
        hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot     = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 40);
        hrt.anchoredPosition = new Vector2(0, -10);

        var title = NewText("Title", headerGo.transform, "> BUILD MENU // BLUEPRINTS", 18,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 5;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(20, 0);
        trt.offsetMax = new Vector2(-20, 0);

        headerWoodText = NewText("Wood", headerGo.transform, "WOOD 0", 14,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        headerWoodText.characterSpacing = 3;
        var wrt = headerWoodText.rectTransform;
        wrt.anchorMin = new Vector2(0, 0);
        wrt.anchorMax = new Vector2(1, 1);
        wrt.offsetMin = new Vector2(20, 0);
        wrt.offsetMax = new Vector2(-20, 0);

        // 1px divider beneath the header
        var div = NewUIObject("HeaderDivider", parent);
        var dimg = div.AddComponent<Image>();
        dimg.color = CyanScannerPalette.PanelBorder;
        dimg.raycastTarget = false;
        var drt = div.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0, 1);
        drt.anchorMax = new Vector2(1, 1);
        drt.pivot     = new Vector2(0.5f, 1);
        drt.sizeDelta = new Vector2(0, 1);
        drt.anchoredPosition = new Vector2(0, -60);
    }

    void BuildTabRow(Transform parent)
    {
        _tabFilters.Clear();
        _tabButtons.Clear();
        _tabFilters.Add(null); // "All" tab
        var present = new HashSet<BuildableCategory>();
        if (buildables != null)
            foreach (var be in buildables) if (be != null) present.Add(be.category);
        foreach (BuildableCategory c in System.Enum.GetValues(typeof(BuildableCategory)))
            if (present.Contains(c)) _tabFilters.Add(c);

        var rowGo = NewUIObject("TabRow", parent);
        var rrt = rowGo.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0, 1);
        rrt.anchorMax = new Vector2(1, 1);
        rrt.pivot     = new Vector2(0.5f, 1);
        rrt.sizeDelta = new Vector2(0, 30);
        rrt.anchoredPosition = new Vector2(0, -68);
        var hLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
        hLayout.padding = new RectOffset(20, 20, 0, 0);
        hLayout.spacing = 4;
        hLayout.childAlignment = TextAnchor.MiddleLeft;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = true;
        hLayout.childControlWidth = true;
        hLayout.childControlHeight = true;

        for (int i = 0; i < _tabFilters.Count; i++)
        {
            var f = _tabFilters[i];
            string label = f.HasValue ? f.Value.ToString().ToUpper() : "ALL";
            var btn = NewButton("Tab_" + label, rowGo.transform, label,
                CyanScannerPalette.BtnNormal, CyanScannerPalette.BtnNormalHover, 12);
            btn.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 2;
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = Mathf.Max(56f, label.Length * 9f + 18f);
            le.preferredHeight = 26f;
            var captured = f;
            btn.onClick.AddListener(() => {
                _filterActive = captured.HasValue;
                if (captured.HasValue) _activeFilter = captured.Value;
                RebuildVisibleRows();
            });
            _tabButtons.Add(btn);
        }
        UpdateTabHighlights();
    }

    void UpdateTabHighlights()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            var f = _tabFilters[i];
            bool selected = (!_filterActive && !f.HasValue) || (_filterActive && f.HasValue && f.Value == _activeFilter);
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = selected ? CyanScannerPalette.SelectionFill : CyanScannerPalette.BtnNormal;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = selected ? CyanScannerPalette.Accent : CyanScannerPalette.AccentDim;
        }
    }

    void BuildBody(Transform parent)
    {
        var body = NewUIObject("Body", parent);
        var brt = body.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0);
        brt.anchorMax = new Vector2(1, 1);
        brt.offsetMin = new Vector2(20, 80);   // leave space for footer
        brt.offsetMax = new Vector2(-20, -110); // leave space for header + tabs

        // List column (30%)
        var listCol = NewUIObject("ListColumn", body.transform);
        var lcrt = listCol.GetComponent<RectTransform>();
        lcrt.anchorMin = new Vector2(0, 0);
        lcrt.anchorMax = new Vector2(0.30f, 1);
        lcrt.offsetMin = Vector2.zero;
        lcrt.offsetMax = new Vector2(-6, 0);
        var listBg = listCol.AddComponent<Image>();
        listBg.color = CyanScannerPalette.InnerBg;

        BuildListScroll(listCol.transform);

        // Preview column (~38%)
        var previewCol = NewUIObject("PreviewColumn", body.transform);
        var pcrt = previewCol.GetComponent<RectTransform>();
        pcrt.anchorMin = new Vector2(0.30f, 0);
        pcrt.anchorMax = new Vector2(0.68f, 1);
        pcrt.offsetMin = new Vector2(6, 0);
        pcrt.offsetMax = new Vector2(-6, 0);
        var pBg = previewCol.AddComponent<Image>();
        pBg.color = CyanScannerPalette.InnerBg;

        BuildPreviewBox(previewCol.transform);

        // Spec column (32%)
        var specCol = NewUIObject("SpecColumn", body.transform);
        var scrt = specCol.GetComponent<RectTransform>();
        scrt.anchorMin = new Vector2(0.68f, 0);
        scrt.anchorMax = new Vector2(1, 1);
        scrt.offsetMin = new Vector2(6, 0);
        scrt.offsetMax = Vector2.zero;
        var sBg = specCol.AddComponent<Image>();
        sBg.color = CyanScannerPalette.InnerBg;

        BuildSpec(specCol.transform);
    }

    void BuildListScroll(Transform parent)
    {
        var scrollGo = NewUIObject("Scroll", parent);
        var srt = scrollGo.GetComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(6, 6);
        srt.offsetMax = new Vector2(-6, -6);
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = NewUIObject("Viewport", scrollGo.transform);
        var vrt = viewportGo.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        var vpImg = viewportGo.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = NewUIObject("Content", viewportGo.transform);
        var crt = contentGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;
        var v = contentGo.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(2, 2, 2, 2);
        v.spacing = 2;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = vrt;
        scrollRect.content = crt;
        listContent = contentGo.transform;
    }

    void BuildPreviewBox(Transform parent)
    {
        // Centred square with blueprint grid + brackets + RawImage on top.
        var box = NewUIObject("PreviewBox", parent);
        var brt = box.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot     = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(320, 320);
        brt.anchoredPosition = Vector2.zero;
        var bg = box.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.19f, 0.33f, 1f); // matches PreviewColumn but slightly lighter
        bg.raycastTarget = false;

        ScannerFrame.AddBlueprintGrid(brt, 24f);
        ScannerFrame.AddBrackets(brt);

        var imgGo = NewUIObject("Preview", box.transform);
        var irt = imgGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0);
        irt.anchorMax = new Vector2(1, 1);
        irt.offsetMin = new Vector2(12, 12);
        irt.offsetMax = new Vector2(-12, -12);
        previewImage = imgGo.AddComponent<RawImage>();
        previewImage.raycastTarget = false;
        previewImage.color = new Color(1, 1, 1, 0); // hidden until a row is selected
    }

    void BuildSpec(Transform parent)
    {
        var pad = NewUIObject("SpecPad", parent);
        var prt = pad.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(12, 12);
        prt.offsetMax = new Vector2(-12, -12);

        specName  = AddSpecRow(pad.transform, 0,   "NAME",  CyanScannerPalette.Accent);
        AddSpecDivider(pad.transform, 32);
        specClass = AddSpecRow(pad.transform, 44,  "CLASS", CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 72);
        specCost  = AddSpecRow(pad.transform, 84,  "COST",  CyanScannerPalette.CostAfford);
        AddSpecDivider(pad.transform, 112);
        specSize  = AddSpecRow(pad.transform, 124, "SIZE",  CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 152);

        specDesc = NewText("Desc", pad.transform, "", 13,
            CyanScannerPalette.AccentDim, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        var drt = specDesc.rectTransform;
        drt.anchorMin = new Vector2(0, 0);
        drt.anchorMax = new Vector2(1, 1);
        drt.offsetMin = Vector2.zero;
        // 170 = 4 spec rows × ~42 px each. If you add/remove a SpecRow above,
        // bump this in lockstep — VerticalLayoutGroup is heavier than warranted
        // for 4 static rows.
        drt.offsetMax = new Vector2(0, -170);
        specDesc.enableWordWrapping = true;
    }

    TextMeshProUGUI AddSpecRow(Transform parent, float topY, string key, Color32 valueColor)
    {
        var keyTxt = NewText("Key_" + key, parent, key, 12,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        keyTxt.characterSpacing = 3;
        var krt = keyTxt.rectTransform;
        krt.anchorMin = new Vector2(0, 1);
        krt.anchorMax = new Vector2(0.5f, 1);
        krt.pivot     = new Vector2(0, 1);
        krt.sizeDelta = new Vector2(0, 24);
        krt.anchoredPosition = new Vector2(0, -topY);

        var valTxt = NewText("Val_" + key, parent, "—", 14, valueColor, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        var vrt = valTxt.rectTransform;
        vrt.anchorMin = new Vector2(0.5f, 1);
        vrt.anchorMax = new Vector2(1, 1);
        vrt.pivot     = new Vector2(1, 1);
        vrt.sizeDelta = new Vector2(0, 24);
        vrt.anchoredPosition = new Vector2(0, -topY);
        return valTxt;
    }

    void AddSpecDivider(Transform parent, float topY)
    {
        var div = NewUIObject("Divider", parent);
        var img = div.AddComponent<Image>();
        img.color = CyanScannerPalette.InnerDivider;
        img.raycastTarget = false;
        var rt = div.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(0, -topY);
    }

    void BuildFooter(Transform parent)
    {
        var footer = NewUIObject("Footer", parent);
        var frt = footer.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0);
        frt.anchorMax = new Vector2(1, 0);
        frt.pivot     = new Vector2(0.5f, 0);
        frt.sizeDelta = new Vector2(0, 60);
        frt.anchoredPosition = new Vector2(0, 0);

        var closeBtn = NewButton("CloseBtn", footer.transform, "[ESC] CLOSE",
            CyanScannerPalette.BtnNormal, CyanScannerPalette.BtnNormalHover, 14);
        closeBtn.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 2;
        var crt = closeBtn.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 0.5f);
        crt.anchorMax = new Vector2(0, 0.5f);
        crt.pivot     = new Vector2(0, 0.5f);
        crt.sizeDelta = new Vector2(220, 40);
        crt.anchoredPosition = new Vector2(24, 0);
        closeBtn.onClick.AddListener(Close);

        placeBtn = NewButton("PlaceBtn", footer.transform, "[ENTER] PLACE",
            CyanScannerPalette.BtnPrimary, CyanScannerPalette.BtnPrimaryHover, 14);
        placeBtnBg = placeBtn.GetComponent<Image>();
        placeBtnLabel = placeBtn.GetComponentInChildren<TextMeshProUGUI>();
        placeBtnLabel.characterSpacing = 2;
        placeBtnLabel.color = CyanScannerPalette.BtnPrimaryText;
        var prt = placeBtn.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 0.5f);
        prt.anchorMax = new Vector2(1, 0.5f);
        prt.pivot     = new Vector2(1, 0.5f);
        prt.sizeDelta = new Vector2(220, 40);
        prt.anchoredPosition = new Vector2(-24, 0);
        placeBtn.onClick.AddListener(OnPlaceClicked);
    }

    void RebuildVisibleRows()
    {
        if (listContent == null) return;

        // First call builds all rows; subsequent calls just toggle visibility.
        if (_rowEntries.Count == 0 && buildables != null)
        {
            foreach (var entry in buildables)
            {
                if (entry == null) continue;
                var row = AddListRow(listContent, entry);
                _rowEntries.Add((row, entry.category, entry));
            }
        }

        foreach (var (row, cat, _) in _rowEntries)
        {
            if (row == null) continue;
            bool visible = !_filterActive || cat == _activeFilter;
            if (row.activeSelf != visible) row.SetActive(visible);
        }
        UpdateTabHighlights();
    }

    GameObject AddListRow(Transform parent, BuildableEntry entry)
    {
        var row = NewUIObject("Row_" + entry.displayName, parent);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 48;
        var rowBg = row.AddComponent<Image>();
        rowBg.color = new Color32(0, 0, 0, 0);
        var btn = row.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color32(0, 0, 0, 0);
        colors.highlightedColor = CyanScannerPalette.SelectionFill;
        colors.pressedColor = CyanScannerPalette.SelectionFill;
        colors.selectedColor = CyanScannerPalette.SelectionFill;
        btn.colors = colors;
        btn.targetGraphic = rowBg;

        // Left accent stripe
        var stripe = NewUIObject("Stripe", row.transform);
        var srt = stripe.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0);
        srt.anchorMax = new Vector2(0, 1);
        srt.pivot     = new Vector2(0, 0.5f);
        srt.sizeDelta = new Vector2(2, 0);
        srt.anchoredPosition = Vector2.zero;
        var stripeImg = stripe.AddComponent<Image>();
        stripeImg.color = CyanScannerPalette.Accent;
        stripeImg.raycastTarget = false;

        // Thumbnail icon
        var iconGo = NewUIObject("Thumb", row.transform);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f);
        irt.anchorMax = new Vector2(0, 0.5f);
        irt.pivot     = new Vector2(0, 0.5f);
        irt.sizeDelta = new Vector2(40, 40);
        irt.anchoredPosition = new Vector2(6, 0);
        var iconBg = iconGo.AddComponent<Image>();
        iconBg.color = CyanScannerPalette.PanelBg;
        iconBg.raycastTarget = false;
        var thumbGo = NewUIObject("ThumbImg", iconGo.transform);
        var trt = thumbGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var thumb = thumbGo.AddComponent<RawImage>();
        thumb.raycastTarget = false;
        if (entry.icon != null) thumb.texture = entry.icon.texture;
        else
        {
            var rt = GetOrRenderPreview(entry.prefab, PreviewSize);
            if (rt != null) thumb.texture = rt;
            else { thumb.color = new Color(1, 1, 1, 0); }
        }

        // Name label
        var name = NewText("Name", row.transform, entry.displayName.ToUpper(), 13,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        name.characterSpacing = 1;
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0, 0);
        nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = new Vector2(54, 0);
        nrt.offsetMax = new Vector2(-50, 0);
        name.raycastTarget = false;

        // Cost label (right aligned)
        var cost = NewText("Cost", row.transform,
            entry.woodCost > 0 ? entry.woodCost + "W" : "FREE", 12,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        var crt2 = cost.rectTransform;
        crt2.anchorMin = new Vector2(0, 0);
        crt2.anchorMax = new Vector2(1, 1);
        crt2.offsetMin = new Vector2(0, 0);
        crt2.offsetMax = new Vector2(-6, 0);
        cost.raycastTarget = false;

        var captured = entry;
        var capturedRow = row;
        btn.onClick.AddListener(() => SelectRow(captured, capturedRow));
        return row;
    }

    void SelectRow(BuildableEntry entry, GameObject row)
    {
        // Visual selection: dim previous selection, highlight new one.
        if (_selectedRow != null)
        {
            var prevBg = _selectedRow.GetComponent<Image>();
            if (prevBg != null) prevBg.color = new Color32(0, 0, 0, 0);
            var prevName = _selectedRow.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (prevName != null) prevName.color = CyanScannerPalette.AccentDim;
        }
        _selectedRow = row;
        if (row != null)
        {
            var bg = row.GetComponent<Image>();
            if (bg != null) bg.color = CyanScannerPalette.SelectionFill;
            var name = row.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.color = CyanScannerPalette.Accent;
        }

        ShowDetail(entry);
    }

    // ───────────────────────── UI helpers ─────────────────────────

    static GameObject NewUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float fontSize, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var go = NewUIObject(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.enableWordWrapping = true;
        return t;
    }

    static Button NewButton(string name, Transform parent, string label, Color32 normal, Color32 hover, float fontSize)
    {
        var go = NewUIObject(name, parent);
        var img = go.AddComponent<Image>();
        img.color = normal;
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.pressedColor = new Color32((byte)(normal.r * 0.7f), (byte)(normal.g * 0.7f), (byte)(normal.b * 0.7f), 255);
        colors.selectedColor = hover;
        btn.colors = colors;

        var label1 = NewText("Label", go.transform, label, fontSize, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        StretchFull(label1.rectTransform);
        label1.raycastTarget = false;
        return btn;
    }

    // ───────────────────────── Preview rendering ─────────────────────────

    void EnsurePreviewRig()
    {
        if (_previewCamera != null) return;

        // Camera lives 10000 units away so it can't see live gameplay geometry, and
        // its culling mask is restricted to PreviewLayer so nothing in the world
        // bleeds in. Disabled by default — we drive it with manual Render() calls.
        var camGo = new GameObject("BuildMenu_PreviewCamera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.position = new Vector3(10000f, 10000f, 10000f);
        _previewCamera = camGo.AddComponent<Camera>();
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        _previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _previewCamera.cullingMask = 1 << PreviewLayer;
        _previewCamera.enabled = false;
        _previewCamera.allowHDR = false;
        _previewCamera.allowMSAA = false;
        _previewCamera.nearClipPlane = 0.05f;
        _previewCamera.farClipPlane = 1000f;
        _previewCamera.fieldOfView = 35f;

        var stageGo = new GameObject("BuildMenu_PreviewStage");
        stageGo.transform.SetParent(transform, false);
        stageGo.transform.position = camGo.transform.position + new Vector3(0f, 0f, 5f);
        _previewStage = stageGo.transform;

        // Two lights restricted to the preview layer so they don't pollute gameplay.
        // Warm key + dim warm fill — soft enough that prefab albedo colours read
        // clearly. The scene's directional light (planet sun) is also temporarily
        // disabled during each Render() call (see RenderPrefabPreview) so these
        // are the ONLY contributors to the preview.
        CreatePreviewLight("BuildMenu_PreviewKey",  _previewStage.position + new Vector3(-1.5f, 2.5f, -1.5f), 0.55f, 30f, new Color(1.00f, 0.82f, 0.58f)); // warm amber key
        CreatePreviewLight("BuildMenu_PreviewFill", _previewStage.position + new Vector3( 2.0f, 0.5f,  1.5f), 0.20f, 25f, new Color(1.00f, 0.90f, 0.78f)); // soft warm fill
    }

    void CreatePreviewLight(string name, Vector3 pos, float intensity, float range, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.layer = PreviewLayer;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.intensity = intensity;
        l.range = range;
        l.color = color;
        l.cullingMask = 1 << PreviewLayer;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
    }

    readonly Dictionary<GameObject, Vector3> _sizeCache = new Dictionary<GameObject, Vector3>();
    Vector3 GetCachedPrefabSize(GameObject prefab)
    {
        if (prefab == null) return Vector3.zero;
        if (_sizeCache.TryGetValue(prefab, out var s)) return s;
        // Trigger a preview render (which computes bounds) so the size is cached
        // in lockstep with the preview texture. The render is cached too, so
        // calling this is at worst a no-op after the first time.
        GetOrRenderPreview(prefab, PreviewSize);
        return _sizeCache.TryGetValue(prefab, out s) ? s : Vector3.one;
    }

    RenderTexture GetOrRenderPreview(GameObject prefab, int size)
    {
        if (prefab == null) return null;
        if (_previewCache.TryGetValue(prefab, out var cached) && cached != null) return cached;
        var rt = RenderPrefabPreview(prefab, size);
        if (rt != null) _previewCache[prefab] = rt;
        return rt;
    }

    RenderTexture RenderPrefabPreview(GameObject prefab, int size)
    {
        EnsurePreviewRig();
        if (_previewCamera == null || _previewStage == null) return null;

        // Spawn the prefab on the preview stage with a slight 3/4 rotation so flat
        // pieces (floors, roofs) read as 3D rather than as silhouettes. Strip
        // physics + scripts so nothing ticks during the single-frame render.
        var instance = Instantiate(prefab, _previewStage.position, Quaternion.Euler(20f, 30f, 0f));
        foreach (var col in instance.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
        foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        SetLayerRecursive(instance, PreviewLayer);

        // Auto-frame: bound the entire instance with a sphere, then position the
        // camera so that sphere just fills the FOV. This makes a 0.3-unit barrel
        // and a 4-unit floor both render at the same on-screen size.
        Bounds bounds;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            bounds = new Bounds(_previewStage.position, Vector3.one);
        }

        // Cache the prefab footprint in lockstep with the rendertexture. The
        // spec column reads this to show SIZE without re-instantiating the
        // prefab a second time.
        if (!_sizeCache.ContainsKey(prefab)) _sizeCache[prefab] = bounds.size;

        float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
        float fovRad = _previewCamera.fieldOfView * Mathf.Deg2Rad;
        float fitDistance = (radius / Mathf.Sin(fovRad * 0.5f)) * 1.15f; // 1.15 = small breathing-room margin
        _previewCamera.transform.position = bounds.center - _previewCamera.transform.forward * fitDistance;
        _previewCamera.transform.LookAt(bounds.center);

        var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 2;
        rt.Create();
        _previewCamera.targetTexture = rt;

        // Suppress scene-wide light contributions for the duration of the render
        // so only our two warm preview lights illuminate the prefab. Without this,
        // the planet's directional sun (cullingMask = Everything by default) and
        // the scene's ambient term wash the previews out to near-white. Save and
        // restore so gameplay rendering on the next frame is unaffected.
        var sceneDirLights = FindObjectsOfType<Light>();
        var dirPrevEnabled = new System.Collections.Generic.List<(Light l, bool wasEnabled)>(sceneDirLights.Length);
        for (int i = 0; i < sceneDirLights.Length; i++)
        {
            var l = sceneDirLights[i];
            if (l == null) continue;
            // Skip the lights we just spawned for this rig; only suppress everything else.
            if (l.gameObject.layer == PreviewLayer) continue;
            dirPrevEnabled.Add((l, l.enabled));
            l.enabled = false;
        }
        var prevAmbientMode = RenderSettings.ambientMode;
        var prevAmbientLight = RenderSettings.ambientLight;
        var prevAmbientSky = RenderSettings.ambientSkyColor;
        var prevAmbientEquator = RenderSettings.ambientEquatorColor;
        var prevAmbientGround = RenderSettings.ambientGroundColor;
        var prevAmbientIntensity = RenderSettings.ambientIntensity;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.06f, 0.05f, 0.04f, 1f); // very dim warm ambient
        RenderSettings.ambientIntensity = 1f;

        _previewCamera.Render();

        // Restore.
        RenderSettings.ambientMode = prevAmbientMode;
        RenderSettings.ambientLight = prevAmbientLight;
        RenderSettings.ambientSkyColor = prevAmbientSky;
        RenderSettings.ambientEquatorColor = prevAmbientEquator;
        RenderSettings.ambientGroundColor = prevAmbientGround;
        RenderSettings.ambientIntensity = prevAmbientIntensity;
        for (int i = 0; i < dirPrevEnabled.Count; i++)
        {
            if (dirPrevEnabled[i].l != null) dirPrevEnabled[i].l.enabled = dirPrevEnabled[i].wasEnabled;
        }

        _previewCamera.targetTexture = null;

        // DestroyImmediate (not Destroy): a follow-up render later in the same
        // frame would otherwise still see this instance on the stage.
        DestroyImmediate(instance);
        return rt;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }

    void OnDestroy()
    {
        foreach (var kv in _previewCache)
        {
            if (kv.Value == null) continue;
            kv.Value.Release();
            Destroy(kv.Value);
        }
        _previewCache.Clear();
    }
}
