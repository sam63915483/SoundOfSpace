using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum FishingdexMode { Browse, Sell, Cook }

// Procedural cyan-scanner UI for the fishingdex. Mirrors BuildMenuUI's
// construction shape — no inspector wiring for the UI side; the manager
// builds its own canvas, list, preview RawImage, spec column, and footer
// buttons at runtime. The fish preview camera + stage are also built at
// runtime (no scene refs).
public class FishingdexManager : MonoBehaviour
{
    public static FishingdexManager Instance { get; private set; }

    // Read by TabbedPauseMenu to suppress the pause-menu ESC handler while
    // the dex is open — otherwise the same ESC keypress that the footer
    // advertises as "[ESC] CLOSE" also brings up the pause menu underneath.
    public static bool IsOpen => Instance != null && Instance.isOpen;

    [Header("Fish Prefabs (Rare=fish01, Common=fish02, Uncommon=fish03)")]
    public GameObject rareFishPrefab;
    public GameObject commonFishPrefab;
    public GameObject uncommonFishPrefab;

    [Header("Preview Camera Tuning")]
    public float previewCameraFOV = 60f;
    public int   fishPreviewLayer = 31;

    // Runtime-built UI refs
    GameObject menuRoot;
    GameObject scannerPanel;
    TextMeshProUGUI headerCountText;
    Transform listContent;
    RawImage previewImage;
    TextMeshProUGUI specType;
    TextMeshProUGUI specMass;
    TextMeshProUGUI specClass;
    TextMeshProUGUI specValue;
    TextMeshProUGUI specCaught;
    TextMeshProUGUI specDesc;
    Button actionBtn;
    TextMeshProUGUI actionBtnLabel;
    Image actionBtnBg;

    // Preview rig (runtime built — no scene refs)
    Camera previewCamera;
    Transform previewStage;
    Ship _shipCached;

    bool isOpen;
    FishingdexMode currentMode = FishingdexMode.Browse;
    System.Action<FishEntry, RenderTexture> onFishAction;
    GameObject callerPanel;
    FishEntry currentDetailEntry;
    RenderTexture detailRenderTexture;

    readonly List<(GameObject row, FishEntry entry, RenderTexture rt)> _rowRegistry =
        new List<(GameObject, FishEntry, RenderTexture)>();
    GameObject _selectedRow;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ReleaseAllRowTextures();
        ReleaseDetailTexture();
    }

    void Start()
    {
        // The pre-refactor scene wired a top-level "FishingdexCanvas"
        // GameObject as the dex panel and relied on the old manager's Start
        // to SetActive(false). The new procedural manager doesn't reference
        // that GameObject, so without this cleanup it would render on top of
        // gameplay every fresh load. Destroy is safer than SetActive here —
        // the scene object can't accidentally get re-enabled by any of the
        // old wiring that may still exist on other components in the scene.
        var legacy = GameObject.Find("FishingdexCanvas");
        if (legacy != null) Destroy(legacy);

        EnsurePreviewRig();
        BuildUI();
        if (menuRoot != null) menuRoot.SetActive(false);
    }

    public static event System.Action OnFishingdexOpened;

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        bool kbToggle = Input.GetKeyDown(KeyCode.B);

        // No direct pad-open binding — RB now cycles the hotbar; pad players
        // reach the Fishingdex through the phone (D-pad up → app button).
        bool padToggle = false;
        bool padClose = isOpen && TutorialGate.PadPressed(TutorialGate.PadButton.B);
        // ESC close — also advertised on the footer button label. The
        // TabbedPauseMenu reads FishingdexManager.IsOpen and skips its own
        // ESC handler while we're open, so this is the only handler that
        // fires for an ESC press inside the dex.
        bool escClose = isOpen && Input.GetKeyDown(KeyCode.Escape);

        if (kbToggle || padToggle || padClose || escClose)
        {
            if (isOpen)
            {
                if (currentMode != FishingdexMode.Browse) CloseForCaller();
                else CloseFishingdex();
            }
            else if (!PlayerController.isInDialogue && TutorialGate.IsUnlocked(TutorialAbility.Fishingdex))
                OpenFishingdex();
        }
    }

    // ── Public open / close API ──────────────────────────────────────────

    public void OpenFishingdex()
    {
        currentMode = FishingdexMode.Browse;
        onFishAction = null;
        callerPanel  = null;
        isOpen = true;
        PlayerController.isInDialogue = true;
        OnFishingdexOpened?.Invoke();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnequipAll();
        if (menuRoot != null) menuRoot.SetActive(true);
        PopulateList();
        UpdateActionButtonForMode();
    }

    public void CloseFishingdex()
    {
        isOpen = false;
        PlayerController.isInDialogue = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        currentMode  = FishingdexMode.Browse;
        onFishAction = null;
        callerPanel  = null;
        currentDetailEntry = null;
        ClearList();
        if (menuRoot != null) menuRoot.SetActive(false);
        ReleaseDetailTexture();
    }

    public void OpenForSell(System.Action<FishEntry, RenderTexture> callback, GameObject panel)
    {
        OpenContextMode(FishingdexMode.Sell, callback, panel);
    }

    public void OpenForCook(System.Action<FishEntry, RenderTexture> callback, GameObject panel)
    {
        OpenContextMode(FishingdexMode.Cook, callback, panel);
    }

    void OpenContextMode(FishingdexMode mode, System.Action<FishEntry, RenderTexture> callback, GameObject panel)
    {
        currentMode  = mode;
        onFishAction = callback;
        callerPanel  = panel;
        if (callerPanel != null) callerPanel.SetActive(false);
        isOpen = true;
        if (menuRoot != null) menuRoot.SetActive(true);
        PopulateList();
        UpdateActionButtonForMode();
    }

    void CloseForCaller()
    {
        isOpen = false;
        currentDetailEntry = null;
        ClearList();
        if (menuRoot != null) menuRoot.SetActive(false);
        ReleaseDetailTexture();

        GameObject panel = callerPanel;
        currentMode  = FishingdexMode.Browse;
        onFishAction = null;
        callerPanel  = null;
        if (panel != null) panel.SetActive(true);
    }

    public void CloseIfContextOpen()
    {
        if (isOpen && currentMode != FishingdexMode.Browse)
            CloseForCaller();
    }

    // ── List population / selection ──────────────────────────────────────

    void PopulateList()
    {
        ClearList();
        if (FishInventory.Instance == null) return;

        var allFish = FishInventory.Instance.AllFish;
        if (headerCountText != null) headerCountText.text = allFish.Count + " ENTRIES";

        foreach (FishEntry entry in allFish)
        {
            RenderTexture rt = RenderFish(entry, 128, 128);
            var row = AddListRow(entry, rt);
            _rowRegistry.Add((row, entry, rt));
        }

        // Auto-select the first entry so the preview/spec aren't blank.
        if (_rowRegistry.Count > 0)
            SelectRow(_rowRegistry[0].entry, _rowRegistry[0].row);
        else
            ClearDetailFields();
    }

    void ClearList()
    {
        for (int i = 0; i < _rowRegistry.Count; i++)
        {
            var t = _rowRegistry[i];
            if (t.rt != null) { t.rt.Release(); Destroy(t.rt); }
            if (t.row != null) Destroy(t.row);
        }
        _rowRegistry.Clear();
        _selectedRow = null;
    }

    void SelectRow(FishEntry entry, GameObject row)
    {
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

    void ShowDetail(FishEntry entry)
    {
        currentDetailEntry = entry;
        ReleaseDetailTexture();
        detailRenderTexture = RenderFish(entry, 256, 256);
        if (previewImage != null)
        {
            previewImage.texture = detailRenderTexture;
            previewImage.color = Color.white;
        }
        if (specType   != null) specType.text   = entry.fishType.ToUpper();
        if (specMass   != null) specMass.text   = entry.weightLbs + " LB";
        if (specClass  != null) specClass.text  = GetRarityLabel(entry.fishType);
        if (specValue  != null) specValue.text  = "$" + entry.GetValue();
        if (specCaught != null) specCaught.text = "×" + CountByType(entry.fishType);
        if (specDesc   != null) specDesc.text   = GetRarityDescription(entry.fishType);
        if (actionBtn  != null) actionBtn.interactable = true;
        if (actionBtnBg != null) actionBtnBg.color = CyanScannerPalette.BtnPrimary;
    }

    void ClearDetailFields()
    {
        currentDetailEntry = null;
        if (previewImage != null) previewImage.color = new Color(1, 1, 1, 0);
        if (specType   != null) specType.text   = "—";
        if (specMass   != null) specMass.text   = "—";
        if (specClass  != null) specClass.text  = "—";
        if (specValue  != null) specValue.text  = "—";
        if (specCaught != null) specCaught.text = "—";
        if (specDesc   != null) specDesc.text   = "NO ENTRIES YET. CATCH SOMETHING.";
        if (actionBtn  != null) actionBtn.interactable = false;
        if (actionBtnBg != null) actionBtnBg.color = CyanScannerPalette.BtnNormalEdge;
    }

    int CountByType(string fishType)
    {
        if (FishInventory.Instance == null) return 0;
        int c = 0;
        foreach (var f in FishInventory.Instance.AllFish) if (f.fishType == fishType) c++;
        return c;
    }

    // Kept for external callers (BonfireInteraction, FishMarketNPC) — they
    // map to "select / clear" since the new panel doesn't have a separate
    // detail screen.
    public void ShowDetail(FishEntry entry, RenderTexture _unused)
    {
        foreach (var t in _rowRegistry)
            if (t.entry == entry) { SelectRow(entry, t.row); return; }
        ShowDetail(entry);
    }
    public void ShowList() { /* single-panel layout, no-op */ }

    // ── Action button ────────────────────────────────────────────────────

    void UpdateActionButtonForMode()
    {
        if (actionBtnLabel == null) return;
        // Phase 2: Browse mode is read-only (lifetime catch log). No eat-raw —
        // the action button only meaningfully fires during cook/sell modes.
        actionBtnLabel.text = currentMode == FishingdexMode.Browse ? "" : "[ENTER] ADD FISH";
    }

    void OnActionClicked()
    {
        if (currentDetailEntry == null) return;
        // Phase 2: Browse mode has no confirm action. Eat from the hotbar
        // (Hotbar.TickEatHold → hold LMB 1s on equipped Fish slot).
        if (currentMode == FishingdexMode.Browse) return;

        var entry  = currentDetailEntry;
        var action = onFishAction;
        RenderTexture rt = RenderFish(entry, 64, 64);
        CloseForCaller();
        action?.Invoke(entry, rt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    void UnequipAll()
    {
        var rod = FindObjectOfType<FishingRodController>();
        if (rod != null && rod.IsEquipped) rod.ForceUnequipRod();
        var guitar = FindObjectOfType<GuitarController>();
        if (guitar != null && guitar.IsEquipped) guitar.ForceUnequipGuitar();
        var bottle = FindObjectOfType<WaterBottleController>();
        if (bottle != null && bottle.IsEquipped) bottle.ForceUnequipBottle();
        var pickup = FindObjectOfType<PlayerPickup>();
        if (pickup != null) pickup.ForceDropObject();
    }

    void ReleaseDetailTexture()
    {
        if (detailRenderTexture != null)
        {
            detailRenderTexture.Release();
            Destroy(detailRenderTexture);
            detailRenderTexture = null;
        }
    }

    void ReleaseAllRowTextures()
    {
        for (int i = 0; i < _rowRegistry.Count; i++)
        {
            var t = _rowRegistry[i];
            if (t.rt != null) { t.rt.Release(); Destroy(t.rt); }
        }
    }

    GameObject GetPrefabForType(string fishType) => fishType switch
    {
        "Rare"     => rareFishPrefab,
        "Uncommon" => uncommonFishPrefab,
        _          => commonFishPrefab,
    };

    string GetRarityLabel(string fishType) => fishType switch
    {
        "Rare"     => "*** RARE ***",
        "Uncommon" => "** UNCOMMON **",
        _          => "* COMMON *",
    };

    string GetRarityDescription(string fishType) => fishType switch
    {
        "Rare"     => "A rare catch. Eating raw triggers a powerful trip.",
        "Uncommon" => "An uncommon find. Eating raw causes a mild trip.",
        _          => "A common species. Safe to eat with minor effects.",
    };

    Color32 GetStripeColorForType(string fishType) => fishType switch
    {
        "Rare"     => CyanScannerPalette.RarityRare,
        "Uncommon" => CyanScannerPalette.RarityUncommon,
        _          => CyanScannerPalette.RarityCommon,
    };

    // ── Preview rig (runtime built) ──────────────────────────────────────

    void EnsurePreviewRig()
    {
        if (previewCamera != null) return;

        var camGo = new GameObject("FishingdexPreviewCamera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.position = new Vector3(10000f, 10000f, 10000f);
        previewCamera = camGo.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << fishPreviewLayer;
        previewCamera.enabled = false;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = false;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 1000f;
        previewCamera.fieldOfView = previewCameraFOV;

        var stageGo = new GameObject("FishingdexPreviewStage");
        stageGo.transform.SetParent(transform, false);
        stageGo.transform.position = camGo.transform.position;
        previewStage = stageGo.transform;

        Vector3 camOffset = new Vector3(2.5f, 1.2f, -1.5f);
        previewCamera.transform.position = previewStage.position + camOffset;
        previewCamera.transform.LookAt(previewStage.position + new Vector3(0f, -0.1f, 0f));

        AddPreviewLight("FishPreviewKeyLight",
            previewStage.position + new Vector3(-1.5f, 3.5f, -1f),
            intensity: 3f, range: 40f, color: Color.white);
        AddPreviewLight("FishPreviewFillLight",
            previewStage.position + new Vector3(3f, 1f, 2f),
            intensity: 1.2f, range: 30f, color: new Color(0.85f, 0.9f, 1f));
    }

    void AddPreviewLight(string name, Vector3 position, float intensity, float range, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.layer = fishPreviewLayer;
        var l = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.intensity = intensity;
        l.range     = range;
        l.color     = color;
        l.cullingMask = 1 << fishPreviewLayer;
        l.shadows = LightShadows.None;
    }

    public RenderTexture RenderFish(FishEntry entry, int width, int height)
    {
        EnsurePreviewRig();
        if (previewCamera == null || previewStage == null) return null;
        GameObject prefab = GetPrefabForType(entry.fishType);
        if (prefab == null) return null;

        GameObject fishGO = Instantiate(prefab, previewStage.position, Quaternion.identity);
        fishGO.transform.localScale = new Vector3(FishEntry.GetXScaleFromWeight(entry.weightLbs), 1f, 1f);
        SetLayerRecursive(fishGO, fishPreviewLayer);
        foreach (Renderer r in fishGO.GetComponentsInChildren<Renderer>())
            r.material.color = entry.fishColor;

        RenderTexture rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
        rt.Create();
        previewCamera.fieldOfView = previewCameraFOV;
        previewCamera.enabled = true;
        previewCamera.targetTexture = rt;
        previewCamera.Render();
        previewCamera.targetTexture = null;
        previewCamera.enabled = false;
        DestroyImmediate(fishGO);
        return rt;
    }

    void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    // ── UI construction ──────────────────────────────────────────────────

    void BuildUI()
    {
        Canvas canvas = FindOrCreateCanvas();

        menuRoot = NewUIObject("FishingdexRoot", canvas.transform);
        StretchFull(menuRoot.GetComponent<RectTransform>());
        var bg = menuRoot.AddComponent<Image>();
        bg.color = new Color32(0, 0, 0, 200);
        bg.raycastTarget = true;

        BuildScannerPanel();
    }

    Canvas FindOrCreateCanvas()
    {
        var canvases = FindObjectsOfType<Canvas>();
        foreach (var c in canvases) if (c.name == "HUD_Canvas") return c;
        var go = new GameObject("Fishingdex_Canvas");
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
        BuildBody(scannerPanel.transform);
        BuildFooter(scannerPanel.transform);
    }

    void AddPanelBorder(Transform parent)
    {
        AddEdge(parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 1));
        AddEdge(parent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 1));
        AddEdge(parent, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(1, 0));
        AddEdge(parent, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(1, 0));
    }

    static void AddEdge(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeAxis)
    {
        var go = NewUIObject("Edge", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
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

        var title = NewText("Title", headerGo.transform, "> FISHINGDEX // SCAN MODE", 18,
            CyanScannerPalette.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 5;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(20, 0);
        trt.offsetMax = new Vector2(-20, 0);

        headerCountText = NewText("Count", headerGo.transform, "0 ENTRIES", 14,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        headerCountText.characterSpacing = 3;
        var crt = headerCountText.rectTransform;
        crt.anchorMin = new Vector2(0, 0);
        crt.anchorMax = new Vector2(1, 1);
        crt.offsetMin = new Vector2(20, 0);
        crt.offsetMax = new Vector2(-20, 0);

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

    void BuildBody(Transform parent)
    {
        var body = NewUIObject("Body", parent);
        var brt = body.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0);
        brt.anchorMax = new Vector2(1, 1);
        brt.offsetMin = new Vector2(20, 80);
        brt.offsetMax = new Vector2(-20, -70);

        // List column 30%
        var listCol = NewUIObject("ListColumn", body.transform);
        var lcrt = listCol.GetComponent<RectTransform>();
        lcrt.anchorMin = new Vector2(0, 0);
        lcrt.anchorMax = new Vector2(0.30f, 1);
        lcrt.offsetMin = Vector2.zero;
        lcrt.offsetMax = new Vector2(-6, 0);
        var listBg = listCol.AddComponent<Image>();
        listBg.color = CyanScannerPalette.InnerBg;
        BuildListScroll(listCol.transform);

        // Preview column ~38%
        var previewCol = NewUIObject("PreviewColumn", body.transform);
        var pcrt = previewCol.GetComponent<RectTransform>();
        pcrt.anchorMin = new Vector2(0.30f, 0);
        pcrt.anchorMax = new Vector2(0.68f, 1);
        pcrt.offsetMin = new Vector2(6, 0);
        pcrt.offsetMax = new Vector2(-6, 0);
        var pBg = previewCol.AddComponent<Image>();
        pBg.color = CyanScannerPalette.InnerBg;
        BuildPreviewBox(previewCol.transform);

        // Spec column 32%
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

    GameObject AddListRow(FishEntry entry, RenderTexture rt)
    {
        var row = NewUIObject("Row_" + entry.fishType + "_" + entry.weightLbs, listContent);
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

        var stripe = NewUIObject("Stripe", row.transform);
        var srt = stripe.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0, 0);
        srt.anchorMax = new Vector2(0, 1);
        srt.pivot     = new Vector2(0, 0.5f);
        srt.sizeDelta = new Vector2(3, 0);
        srt.anchoredPosition = Vector2.zero;
        var stripeImg = stripe.AddComponent<Image>();
        stripeImg.color = GetStripeColorForType(entry.fishType);
        stripeImg.raycastTarget = false;

        var iconGo = NewUIObject("Thumb", row.transform);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f);
        irt.anchorMax = new Vector2(0, 0.5f);
        irt.pivot     = new Vector2(0, 0.5f);
        irt.sizeDelta = new Vector2(40, 40);
        irt.anchoredPosition = new Vector2(8, 0);
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
        if (rt != null) thumb.texture = rt;

        var name = NewText("Name", row.transform,
            entry.fishType.ToUpper() + "." + entry.weightLbs.ToString("D2"), 13,
            CyanScannerPalette.AccentDim, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        name.characterSpacing = 1;
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0, 0);
        nrt.anchorMax = new Vector2(1, 1);
        nrt.offsetMin = new Vector2(56, 0);
        nrt.offsetMax = new Vector2(-8, 0);
        name.raycastTarget = false;

        var captured = entry;
        var capturedRow = row;
        btn.onClick.AddListener(() => SelectRow(captured, capturedRow));
        return row;
    }

    void BuildPreviewBox(Transform parent)
    {
        var box = NewUIObject("PreviewBox", parent);
        var brt = box.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot     = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(320, 320);
        brt.anchoredPosition = Vector2.zero;
        var bg = box.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.19f, 0.33f, 1f);
        bg.raycastTarget = false;

        // Blueprint grid + corner brackets — same treatment as the build menu's
        // preview so both panels feel like sister readouts on the same scanner.
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
        previewImage.color = new Color(1, 1, 1, 0);
    }

    void BuildSpec(Transform parent)
    {
        var pad = NewUIObject("SpecPad", parent);
        var prt = pad.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(12, 12);
        prt.offsetMax = new Vector2(-12, -12);

        specType   = AddSpecRow(pad.transform, 0,   "TYPE",   CyanScannerPalette.Accent);
        AddSpecDivider(pad.transform, 32);
        specMass   = AddSpecRow(pad.transform, 44,  "MASS",   CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 72);
        specClass  = AddSpecRow(pad.transform, 84,  "CLASS",  CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 112);
        specValue  = AddSpecRow(pad.transform, 124, "VALUE",  CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 152);
        specCaught = AddSpecRow(pad.transform, 164, "CAUGHT", CyanScannerPalette.TextBright);
        AddSpecDivider(pad.transform, 192);

        specDesc = NewText("Desc", pad.transform, "", 13,
            CyanScannerPalette.AccentDim, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        var drt = specDesc.rectTransform;
        drt.anchorMin = new Vector2(0, 0);
        drt.anchorMax = new Vector2(1, 1);
        drt.offsetMin = Vector2.zero;
        drt.offsetMax = new Vector2(0, -210);
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
        closeBtn.onClick.AddListener(() => {
            if (currentMode != FishingdexMode.Browse) CloseForCaller();
            else CloseFishingdex();
        });

        actionBtn = NewButton("ActionBtn", footer.transform, "[E] EAT RAW",
            CyanScannerPalette.BtnPrimary, CyanScannerPalette.BtnPrimaryHover, 14);
        actionBtnBg = actionBtn.GetComponent<Image>();
        actionBtnLabel = actionBtn.GetComponentInChildren<TextMeshProUGUI>();
        actionBtnLabel.characterSpacing = 2;
        actionBtnLabel.color = CyanScannerPalette.BtnPrimaryText;
        var prt = actionBtn.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 0.5f);
        prt.anchorMax = new Vector2(1, 0.5f);
        prt.pivot     = new Vector2(1, 0.5f);
        prt.sizeDelta = new Vector2(220, 40);
        prt.anchoredPosition = new Vector2(-24, 0);
        actionBtn.onClick.AddListener(OnActionClicked);
    }

    // ── UI primitives (mirror BuildMenuUI's helpers) ─────────────────────

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

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float fontSize,
                                   Color color, FontStyles style, TextAlignmentOptions align)
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
        var labelGo = NewUIObject("Label", go.transform);
        var t = labelGo.AddComponent<TextMeshProUGUI>();
        t.text = label;
        t.fontSize = fontSize;
        t.color = Color.white;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        StretchFull(labelGo.GetComponent<RectTransform>());
        t.raycastTarget = false;
        return btn;
    }
}
