using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedurally-built shop UI for the ship market. Mirrors GoodsVendorShopUI
/// line-for-line (warm copper palette, grid → detail flow, live preview rig)
/// but is bound to ShipMarketNPC and titled "SHIP MARKET". Kept as a separate
/// type rather than sharing GoodsVendorShopUI so the goods-vendor flow stays
/// untouched.
/// </summary>
public class ShipMarketShopUI : MonoBehaviour
{
    // ── Palette (warm copper/amber — same as GoodsVendorShopUI) ────────────
    static readonly Color32 C_PanelBg = new Color32(24, 18, 12, 252);
    static readonly Color32 C_CardBg  = new Color32(40, 30, 22, 255);
    static readonly Color32 C_CardHover = new Color32(58, 44, 32, 255);
    static readonly Color32 C_Divider = new Color32(95, 70, 45, 255);
    static readonly Color32 C_Accent  = new Color32(235, 165, 80, 255);
    static readonly Color32 C_Title   = new Color32(245, 200, 130, 255);
    static readonly Color32 C_Label   = new Color32(245, 235, 220, 255);
    static readonly Color32 C_Sub     = new Color32(180, 150, 120, 255);
    static readonly Color32 C_Hint    = new Color32(120, 95, 75, 255);
    static readonly Color32 C_Gold    = new Color32(255, 215, 50, 255);
    static readonly Color32 C_BtnBuy  = new Color32(60, 145, 70, 255);
    static readonly Color32 C_BtnBack = new Color32(140, 60, 60, 255);
    static readonly Color32 C_Owned   = new Color32(90, 90, 90, 255);
    static readonly Color32 C_Toast_OK   = new Color32(110, 220, 130, 255);
    static readonly Color32 C_Toast_Err  = new Color32(255, 110, 110, 255);

    [Header("Preview Rendering")]
    [SerializeField] Camera previewCamera;
    [SerializeField] Transform previewStage;
    [SerializeField] int previewLayer = 31;
    [SerializeField] int cardPreviewSize = 256;
    [SerializeField] int detailPreviewSize = 512;

    Canvas _canvas;
    GameObject _root;
    GameObject _gridView;
    GameObject _detailView;
    Transform _gridContent;
    RectTransform _detailRoot;

    RawImage _detailImage;
    TextMeshProUGUI _detailName;
    TextMeshProUGUI _detailPrice;
    TextMeshProUGUI _detailDesc;
    Button _buyButton;
    TextMeshProUGUI _buyLabel;
    TextMeshProUGUI _toastText;
    CanvasGroup _toastCG;
    Coroutine _toastCoroutine;

    ShipMarketNPC _vendor;
    ShopItem _detailItem;
    readonly Dictionary<ShopItem, RenderTexture> _previewCache = new Dictionary<ShopItem, RenderTexture>();
    readonly List<(ShopItem item, GameObject card, RawImage img, TextMeshProUGUI name, TextMeshProUGUI price, Image bg)> _cards
        = new List<(ShopItem, GameObject, RawImage, TextMeshProUGUI, TextMeshProUGUI, Image)>();
    bool _shopOpen;
    bool _builtUI;

    public bool IsOpen => _shopOpen;

    void Awake()
    {
        BuildCanvas();
        BuildPreviewRig();
    }

    void Update()
    {
        if (AIChatScreen.IsTypingActive) return;
        if (!_shopOpen) return;
        // Defensive cursor pinning — mirrors TutorialPerformanceReview's fix.
        // Without this, the first open ends up with a locked cursor because
        // another script (NPC dialogue close, tutorial-step exit, etc.) flips
        // CursorLockMode.Locked back on the same frame Open() unlocked it,
        // and the player can't click anything until they close + reopen.
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
        bool closePressed = Input.GetKeyDown(KeyCode.F)
            || TutorialGate.PadPressed(TutorialGate.PadButton.X)
            || TutorialGate.PadPressed(TutorialGate.PadButton.B);
        if (closePressed) Close();
    }

    void OnDestroy()
    {
        foreach (var kv in _previewCache)
        {
            if (kv.Value != null) { kv.Value.Release(); Destroy(kv.Value); }
        }
        _previewCache.Clear();
    }

    public void Open(ShipMarketNPC vendor)
    {
        if (_shopOpen) return;
        _vendor = vendor;
        if (!_builtUI) BuildUI();
        _shopOpen = true;
        _root.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PopulateGrid();
        ShowGrid();
    }

    public void Close()
    {
        if (!_shopOpen) return;
        _shopOpen = false;
        _root.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (_toastCoroutine != null) { StopCoroutine(_toastCoroutine); _toastCoroutine = null; }
        if (_toastText != null) _toastText.gameObject.SetActive(false);
        var v = _vendor;
        _vendor = null;
        if (v != null) v.OnShopClosed();
    }

    // ── Canvas + preview rig setup ───────────────────────────────────────────

    void BuildCanvas()
    {
        var canvasGO = new GameObject("ShipMarketCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Vendor;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
    }

    void BuildPreviewRig()
    {
        if (previewCamera == null)
        {
            var camGO = new GameObject("ShipMarketPreviewCamera");
            camGO.transform.SetParent(transform, false);
            camGO.transform.position = new Vector3(0f, -10000f, 0f);
            previewCamera = camGO.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.cullingMask = 1 << previewLayer;
            previewCamera.enabled = false;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;
        }
        if (previewStage == null)
        {
            var stageGO = new GameObject("ShipMarketPreviewStage");
            stageGO.transform.SetParent(transform, false);
            stageGO.transform.position = previewCamera.transform.position + new Vector3(0f, 0f, 5f);
            previewStage = stageGO.transform;
        }

        CreatePreviewLight("ShipMarketPreviewKey",  previewStage.position + new Vector3(-1.5f, 2.5f, -1.5f), 2.5f, 30f, Color.white);
        CreatePreviewLight("ShipMarketPreviewFill", previewStage.position + new Vector3( 2.0f, 0.5f,  1.5f), 1.0f, 25f, new Color(0.85f, 0.9f, 1f));
    }

    void CreatePreviewLight(string n, Vector3 pos, float intensity, float range, Color color)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.layer = previewLayer;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.intensity = intensity;
        l.range = range;
        l.color = color;
        l.cullingMask = 1 << previewLayer;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
    }

    // ── UI build ─────────────────────────────────────────────────────────────

    void BuildUI()
    {
        _builtUI = true;

        _root = new GameObject("ShopRoot", typeof(RectTransform));
        _root.transform.SetParent(_canvas.transform, false);
        var rootRT = (RectTransform)_root.transform;
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        var dim = new GameObject("Dim", typeof(RectTransform));
        dim.transform.SetParent(_root.transform, false);
        var dimRT = (RectTransform)dim.transform;
        dimRT.anchorMin = Vector2.zero; dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero; dimRT.offsetMax = Vector2.zero;
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color32(0, 0, 0, 160);
        dimImg.raycastTarget = true;

        var panel = MakeRT("Panel", _root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(720, 720);
        var panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = C_PanelBg;

        var accent = MakeRT("Accent", panel);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(5f, 0f);
        accent.anchoredPosition = Vector2.zero;
        accent.gameObject.AddComponent<Image>().color = C_Accent;

        var title = MkText(panel, "SHIP MARKET", 26, C_Title, FontStyles.Bold, TextAlignmentOptions.Center);
        var titleRT = title.rectTransform;
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.sizeDelta = new Vector2(-40f, 48f);
        titleRT.anchoredPosition = new Vector2(0f, -16f);
        title.characterSpacing = 6f;

        var hint = MkText(panel, $"Press {PromptGlyphs.Interact} to close", 13, C_Hint, FontStyles.Normal, TextAlignmentOptions.Center);
        var hintRT = hint.rectTransform;
        hintRT.anchorMin = new Vector2(0f, 0f);
        hintRT.anchorMax = new Vector2(1f, 0f);
        hintRT.pivot = new Vector2(0.5f, 0f);
        hintRT.sizeDelta = new Vector2(-40f, 22f);
        hintRT.anchoredPosition = new Vector2(0f, 12f);

        BuildGridView(panel);
        BuildDetailView(panel);
        BuildToast(panel);
        VendorMoneyBadge.Attach(panel);   // live balance while buying

        _root.SetActive(false);
    }

    void BuildGridView(RectTransform panel)
    {
        _gridView = new GameObject("GridView", typeof(RectTransform));
        _gridView.transform.SetParent(panel, false);
        var rt = (RectTransform)_gridView.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(20f, 40f);
        rt.offsetMax = new Vector2(-20f, -72f);

        var viewport = MakeRT("Viewport", rt);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color32(8, 6, 4, 255);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = MakeRT("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(0f, 0f);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 240);
        grid.spacing = new Vector2(14, 14);
        grid.padding = new RectOffset(16, 16, 16, 16);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = rt.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        _gridContent = content;
    }

    void BuildDetailView(RectTransform panel)
    {
        _detailView = new GameObject("DetailView", typeof(RectTransform));
        _detailView.transform.SetParent(panel, false);
        _detailRoot = (RectTransform)_detailView.transform;
        _detailRoot.anchorMin = new Vector2(0f, 0f);
        _detailRoot.anchorMax = new Vector2(1f, 1f);
        _detailRoot.offsetMin = new Vector2(40f, 40f);
        _detailRoot.offsetMax = new Vector2(-40f, -72f);

        var imgRT = MakeRT("Image", _detailRoot);
        imgRT.anchorMin = new Vector2(0.5f, 1f);
        imgRT.anchorMax = new Vector2(0.5f, 1f);
        imgRT.pivot = new Vector2(0.5f, 1f);
        imgRT.sizeDelta = new Vector2(320, 320);
        imgRT.anchoredPosition = new Vector2(0f, 0f);
        var imgBg = imgRT.gameObject.AddComponent<Image>();
        imgBg.color = new Color32(8, 6, 4, 255);
        var imgChild = MakeRT("RawImage", imgRT);
        imgChild.anchorMin = Vector2.zero; imgChild.anchorMax = Vector2.one;
        imgChild.offsetMin = new Vector2(8, 8); imgChild.offsetMax = new Vector2(-8, -8);
        _detailImage = imgChild.gameObject.AddComponent<RawImage>();
        _detailImage.color = Color.white;

        _detailName = MkText(_detailRoot, "", 28, C_Title, FontStyles.Bold, TextAlignmentOptions.Center);
        var nameRT = _detailName.rectTransform;
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0.5f, 1f);
        nameRT.sizeDelta = new Vector2(0f, 38f);
        nameRT.anchoredPosition = new Vector2(0f, -332f);

        _detailPrice = MkText(_detailRoot, "", 22, C_Gold, FontStyles.Bold, TextAlignmentOptions.Center);
        var priceRT = _detailPrice.rectTransform;
        priceRT.anchorMin = new Vector2(0f, 1f);
        priceRT.anchorMax = new Vector2(1f, 1f);
        priceRT.pivot = new Vector2(0.5f, 1f);
        priceRT.sizeDelta = new Vector2(0f, 30f);
        priceRT.anchoredPosition = new Vector2(0f, -374f);

        _detailDesc = MkText(_detailRoot, "", 16, C_Label, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        var descRT = _detailDesc.rectTransform;
        descRT.anchorMin = new Vector2(0f, 0f);
        descRT.anchorMax = new Vector2(1f, 1f);
        descRT.offsetMin = new Vector2(8f, 80f);
        descRT.offsetMax = new Vector2(-8f, -410f);
        _detailDesc.enableWordWrapping = true;

        var rowRT = MakeRT("ButtonRow", _detailRoot);
        rowRT.anchorMin = new Vector2(0f, 0f);
        rowRT.anchorMax = new Vector2(1f, 0f);
        rowRT.pivot = new Vector2(0.5f, 0f);
        rowRT.sizeDelta = new Vector2(0f, 60f);
        rowRT.anchoredPosition = new Vector2(0f, 8f);
        var hlg = rowRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        var backBtn = MkButton(rowRT, "BACK", C_BtnBack);
        backBtn.onClick.AddListener(ShowGrid);
        _buyButton = MkButton(rowRT, "BUY", C_BtnBuy);
        _buyLabel = _buyButton.GetComponentInChildren<TextMeshProUGUI>();
        _buyButton.onClick.AddListener(OnBuyClicked);

        _detailView.SetActive(false);
    }

    void BuildToast(RectTransform panel)
    {
        var go = new GameObject("Toast", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(540, 44);
        rt.anchoredPosition = new Vector2(0f, 90f);
        _toastCG = go.AddComponent<CanvasGroup>();
        _toastCG.alpha = 0f;
        _toastText = go.AddComponent<TextMeshProUGUI>();
        _toastText.fontSize = 20;
        _toastText.fontStyle = FontStyles.Bold;
        _toastText.alignment = TextAlignmentOptions.Center;
        _toastText.color = C_Toast_OK;
        DialogueTextStyling.ApplyOutline(_toastText);
        go.SetActive(false);
    }

    // ── Population + view switching ──────────────────────────────────────────

    void PopulateGrid()
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i].card != null) Destroy(_cards[i].card);
        }
        _cards.Clear();

        if (_vendor == null || _vendor.Inventory == null) return;
        foreach (var item in _vendor.Inventory)
        {
            if (item == null) continue;
            var card = BuildCard(item);
            _cards.Add(card);
        }
    }

    (ShopItem item, GameObject card, RawImage img, TextMeshProUGUI name, TextMeshProUGUI price, Image bg) BuildCard(ShopItem item)
    {
        var cardGO = new GameObject("Card_" + item.displayName, typeof(RectTransform));
        cardGO.transform.SetParent(_gridContent, false);
        var bg = cardGO.AddComponent<Image>();
        bg.color = C_CardBg;
        var btn = cardGO.AddComponent<Button>();
        var captured = item;
        btn.onClick.AddListener(() => ShowDetail(captured));

        var imgWrap = MakeRT("Image", (RectTransform)cardGO.transform);
        imgWrap.anchorMin = new Vector2(0.5f, 1f);
        imgWrap.anchorMax = new Vector2(0.5f, 1f);
        imgWrap.pivot = new Vector2(0.5f, 1f);
        imgWrap.sizeDelta = new Vector2(160, 160);
        imgWrap.anchoredPosition = new Vector2(0f, -10f);
        var imgBg = imgWrap.gameObject.AddComponent<Image>();
        imgBg.color = new Color32(8, 6, 4, 255);
        var rawWrap = MakeRT("Raw", imgWrap);
        rawWrap.anchorMin = Vector2.zero; rawWrap.anchorMax = Vector2.one;
        rawWrap.offsetMin = new Vector2(4, 4); rawWrap.offsetMax = new Vector2(-4, -4);
        var raw = rawWrap.gameObject.AddComponent<RawImage>();
        raw.texture = GetOrRenderPreview(item, cardPreviewSize);

        var name = MkText((RectTransform)cardGO.transform, item.displayName, 16, C_Label, FontStyles.Bold, TextAlignmentOptions.Center);
        var nameRT = name.rectTransform;
        nameRT.anchorMin = new Vector2(0f, 0f);
        nameRT.anchorMax = new Vector2(1f, 0f);
        nameRT.pivot = new Vector2(0.5f, 0f);
        nameRT.sizeDelta = new Vector2(-12f, 24f);
        nameRT.anchoredPosition = new Vector2(0f, 32f);

        var price = MkText((RectTransform)cardGO.transform, "$" + item.price, 18, C_Gold, FontStyles.Bold, TextAlignmentOptions.Center);
        var priceRT = price.rectTransform;
        priceRT.anchorMin = new Vector2(0f, 0f);
        priceRT.anchorMax = new Vector2(1f, 0f);
        priceRT.pivot = new Vector2(0.5f, 0f);
        priceRT.sizeDelta = new Vector2(-12f, 26f);
        priceRT.anchoredPosition = new Vector2(0f, 6f);

        ApplyOwnedState(item, raw, name, price, bg, btn);
        return (item, cardGO, raw, name, price, bg);
    }

    // Greys out items reported as already owned by the vendor (e.g. the one-time SpaceDustFilter).
    // Ships and parts return false from IsAlreadyOwned and remain buyable as always.
    void ApplyOwnedState(ShopItem item, RawImage raw, TextMeshProUGUI name, TextMeshProUGUI price, Image bg, Button btn)
    {
        bool owned = _vendor != null && _vendor.IsAlreadyOwned(item.kind);
        bg.color = owned ? C_Owned : C_CardBg;
        if (raw != null) raw.color = owned ? new Color(0.6f, 0.6f, 0.6f, 1f) : Color.white;
        if (price != null)
        {
            price.text = owned ? "OWNED" : ("$" + item.price);
            price.color = owned ? C_Sub : C_Gold;
        }
        if (btn != null) btn.interactable = !owned;
    }

    void RefreshCardOwnedState(ShopItem item)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].item != item) continue;
            var btn = _cards[i].card.GetComponent<Button>();
            ApplyOwnedState(item, _cards[i].img, _cards[i].name, _cards[i].price, _cards[i].bg, btn);
            return;
        }
    }

    void ShowGrid()
    {
        if (_gridView != null) _gridView.SetActive(true);
        if (_detailView != null) _detailView.SetActive(false);
    }

    void ShowDetail(ShopItem item)
    {
        _detailItem = item;
        _detailImage.texture = GetOrRenderPreview(item, detailPreviewSize);
        _detailName.text = item.displayName;
        _detailPrice.text = "$" + item.price;
        _detailDesc.text = item.description;
        _buyButton.interactable = true;
        if (_buyLabel != null) _buyLabel.text = "BUY";
        if (_gridView != null) _gridView.SetActive(false);
        if (_detailView != null) _detailView.SetActive(true);
    }

    void OnBuyClicked()
    {
        if (_vendor == null || _detailItem == null) return;
        var result = _vendor.Purchase(_detailItem);
        bool isPart =
            _detailItem.kind == ShopItemKind.PartLeftThruster ||
            _detailItem.kind == ShopItemKind.PartRightThruster ||
            _detailItem.kind == ShopItemKind.PartDish ||
            _detailItem.kind == ShopItemKind.PartSolarPanel;
        switch (result)
        {
            case ShipMarketNPC.PurchaseResult.Success:
                if (isPart) ShowToast("Part equipped — install it on your ship.", C_Toast_OK);
                else        ShowToast("Ship purchased! It's parked behind the vendor.", C_Toast_OK);
                RefreshCardOwnedState(_detailItem);
                // Auto-close so the player can step away with their new ship/part.
                StartCoroutine(CloseAfterDelay(1.2f));
                break;
            case ShipMarketNPC.PurchaseResult.NotEnoughMoney:
                ShowToast("Not enough money!", C_Toast_Err);
                break;
            case ShipMarketNPC.PurchaseResult.AlreadyHoldingItem:
                ShowToast("You're already holding something — drop it first.", C_Toast_Err);
                break;
            case ShipMarketNPC.PurchaseResult.NoSpawnPrefab:
                ShowToast("Stock unavailable (vendor misconfigured).", C_Toast_Err);
                break;
            case ShipMarketNPC.PurchaseResult.InvalidItem:
                ShowToast("Item unavailable.", C_Toast_Err);
                break;
        }
    }

    IEnumerator CloseAfterDelay(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (_shopOpen) Close();
    }

    void ShowToast(string message, Color32 color)
    {
        if (_toastText == null) return;
        if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
        _toastText.text = message;
        _toastText.color = color;
        _toastText.gameObject.SetActive(true);
        _toastCoroutine = StartCoroutine(ToastFade());
    }

    IEnumerator ToastFade()
    {
        _toastCG.alpha = 1f;
        yield return new WaitForSecondsRealtime(1.2f);
        float elapsed = 0f;
        const float fade = 0.4f;
        while (elapsed < fade)
        {
            _toastCG.alpha = Mathf.Lerp(1f, 0f, elapsed / fade);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        _toastCG.alpha = 0f;
        _toastText.gameObject.SetActive(false);
        _toastCoroutine = null;
    }

    // ── Runtime preview rendering (mirrors GoodsVendorShopUI.RenderItem) ─────

    RenderTexture GetOrRenderPreview(ShopItem item, int size)
    {
        if (item == null || item.previewPrefab == null) return null;
        if (_previewCache.TryGetValue(item, out var cached) && cached != null) return cached;
        var rt = RenderItem(item, size, size);
        if (rt != null) _previewCache[item] = rt;
        return rt;
    }

    RenderTexture RenderItem(ShopItem item, int w, int h)
    {
        if (previewCamera == null || previewStage == null) return null;
        if (item.previewPrefab == null) return null;

        var instance = Instantiate(item.previewPrefab, previewStage.position, Quaternion.Euler(item.previewRotationEuler));
        var rb = instance.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        foreach (var col in instance.GetComponentsInChildren<Collider>()) col.enabled = false;
        SetLayerRecursive(instance, previewLayer);

        Bounds bounds;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            bounds = new Bounds(previewStage.position, Vector3.one);
        }

        previewCamera.fieldOfView = item.previewCameraFov;
        float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
        float fovRad = item.previewCameraFov * Mathf.Deg2Rad;
        float fitDistance = radius / Mathf.Sin(fovRad * 0.5f);
        fitDistance *= Mathf.Max(0.1f, item.previewCameraDistance);
        previewCamera.transform.position = bounds.center - previewCamera.transform.forward * fitDistance;
        previewCamera.transform.LookAt(bounds.center);

        var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 2;
        rt.Create();
        var prev = previewCamera.targetTexture;
        previewCamera.targetTexture = rt;
        previewCamera.Render();
        previewCamera.targetTexture = prev;

        DestroyImmediate(instance);
        return rt;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }

    // ── UI helpers ───────────────────────────────────────────────────────────

    static RectTransform MakeRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static TextMeshProUGUI MkText(Transform parent, string text, int size, Color32 color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    static Button MkButton(Transform parent, string label, Color32 color)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.15f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.15f);
        colors.disabledColor = C_Owned;
        btn.colors = colors;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lblRT = (RectTransform)lblGO.transform;
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 22;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color = Color.white;
        lbl.enableWordWrapping = false;
        lbl.raycastTarget = false;
        return btn;
    }
}
