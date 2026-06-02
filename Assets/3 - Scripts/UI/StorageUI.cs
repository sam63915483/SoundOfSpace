using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Singleton scanner-blueprint storage panel. Renders the active LootBox's
// 20 slots + the hotbar's 5 slots, plus a cursor follower for drag/drop.
// LMB: pick-up full / deposit / merge / swap.  RMB: pick-up one / drop one.
// Shift+LMB: quick-move to the other container. Logic lives in SlotOps;
// this class is the input/visual layer.
public class StorageUI : MonoBehaviour
{
    static StorageUI instance;
    public static StorageUI Instance => instance;

    public bool IsOpen { get; private set; }
    LootBox _active;
    SlotOps.CursorState _cursor;

    // Same-frame race guard: when F (or the Exit button) closes the panel,
    // the LootBox's Update could re-open immediately if it runs after us in
    // the same frame. LootBox.Update checks ConsumedFThisFrame and skips
    // its F-handling on the close frame.
    static int s_consumedFFrame = -1;
    public static bool ConsumedFThisFrame => s_consumedFFrame == Time.frameCount;

    const int   StorageCols = 5;
    const int   StorageRows = 4;
    const int   HotbarSlots = 7;
    const float SlotSize    = 84f;
    const float SlotGap     = 6f;
    const float PanelPad    = 32f;

    Canvas _canvas;
    GameObject _root;
    SlotView[] _storageViews = new SlotView[StorageCols * StorageRows];
    SlotView[] _hotbarViews  = new SlotView[HotbarSlots];
    // Phase 3: docked fish-bag side panel — 5 slot views populated from
    // the bagContents of the currently-open bag. null _activeBag means
    // the panel is hidden.
    SlotView[] _bagViews = new SlotView[5];
    RectTransform _bagPanel;
    Hotbar.Slot[] _activeBag;
    RectTransform _cursorRoot;
    Image _cursorIcon;
    // Phase 3 polish: cursor's fish preview RawImage. Mirrors the slot's
    // fishPreview — bound to FishingdexManager.RenderFish output via
    // FishEntry.cachedHotbarPreview. Enabled only while cursor carries Fish.
    RawImage _cursorFishPreview;
    TextMeshProUGUI _cursorCount;
    TextMeshProUGUI _taglineText;

    class SlotView
    {
        public RectTransform root;
        public Image background;
        public Image border;
        public Image itemIcon;
        public TextMeshProUGUI countText;
        // Phase 3 polish: live fish preview RawImage (same as Hotbar). Bound
        // to FishingdexManager.RenderFish output via FishEntry.cachedHotbarPreview.
        // Enabled only when slot.id == Fish; itemIcon disabled in that case.
        public RawImage fishPreview;
        public Hotbar.Slot[] container;   // resolved at every RefreshAll
        public int index;
    }

    // Per-slot click handler — forwards to StorageUI.OnSlotClicked.
    class StorageSlotClick : MonoBehaviour, IPointerClickHandler
    {
        public StorageUI owner;
        public SlotView view;
        public void OnPointerClick(PointerEventData e) { if (owner != null) owner.OnSlotClicked(view, e); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("StorageUI");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<StorageUI>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        BuildCanvas();
        HideCanvas();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    public void Open(LootBox box)
    {
        if (IsOpen || box == null) return;
        _active = box;
        IsOpen = true;
        _cursor = default;
        PlayerController.isInModalSlotUI = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Hotbar.Instance != null) Hotbar.Instance.OnStorageOpened();
        if (_taglineText != null) _taglineText.text = FormatTagline(box);
        ShowCanvas();
        RefreshAll();
        RefreshCursorVisual();
    }

    // Tagline derived from the box's stable ID. Every ship in the player's
    // fleet gets a number: the original (scene's starting ship) is Ship 1,
    // the first purchased ship is Ship 2, the second is Ship 3, etc. Bay
    // index is always 1 until per-ship multi-bay layouts exist.
    //
    // BoughtShip.shipNumber starts at 1 for the first purchased ship, so
    // the display number is shipNumber + 1 to leave Ship 1 for the original.
    static string FormatTagline(LootBox box)
    {
        if (box == null) return "SHIP 1 · BAY 1";
        string id = box.BoxId ?? "";
        int slash = id.IndexOf('/');
        string prefix = slash >= 0 ? id.Substring(0, slash) : id;
        const string Bought = "BoughtShip";
        if (prefix.StartsWith(Bought))
        {
            string n = prefix.Substring(Bought.Length);
            if (int.TryParse(n, out int boughtIndex))
                return $"SHIP {boughtIndex + 1} · BAY 1";
        }
        return "SHIP 1 · BAY 1";
    }

    public void Close()
    {
        if (!IsOpen) return;
        _active = null;
        _cursor = default;
        IsOpen = false;
        PlayerController.isInModalSlotUI = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        // Phase 3: bag side panel closes with the main UI.
        if (_bagPanel != null) _bagPanel.gameObject.SetActive(false);
        _activeBag = null;
        HideCanvas();
    }

    // External entry point for close requests (F-key, Esc, trigger-exit).
    // Returns the held cursor item to its source slot first; if the source
    // is full of a different item AND there's no empty slot anywhere in
    // source, the close is blocked and the player must manually drop the
    // cursor item first. Logs a warning in that defensive case.
    public void RequestClose()
    {
        if (!IsOpen) return;
        if (_cursor.IsHeld)
        {
            bool returned = SlotOps.ReturnHeldToSource(ref _cursor);
            if (!returned)
            {
                Debug.LogWarning("[StorageUI] cannot close — cursor held and no room to return. Drop the item first.");
                return;
            }
        }
        Close();
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Escape))
        {
            // Mark F as consumed so LootBox.Update (which runs in the same
            // frame) doesn't reopen the panel via its own F-key handler.
            if (Input.GetKeyDown(KeyCode.F)) s_consumedFFrame = Time.frameCount;
            RequestClose();
            return;
        }
        if (PlayerController.isInDialogue || PlayerController.isMapOpen
            || PlayerPhoneUI.IsOpen || Ship.FindPilotedShip() != null) { RequestClose(); return; }

        // Track mouse for cursor follower.
        if (_cursorRoot != null && _cursorRoot.gameObject.activeSelf && _canvas != null)
        {
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _cursorRoot.anchoredPosition = (Vector2)Input.mousePosition / scale;
        }

        RefreshAll();
        RefreshCursorVisual();
    }

    void OnSlotClicked(SlotView view, PointerEventData e)
    {
        if (view == null || view.container == null || _active == null) return;
        var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Phase 3: RMB on a FishBag slot opens (or toggles) the side panel
        // showing its 5 internal slots. Intercept BEFORE the standard RMB
        // pickup-one path. Skip if cursor is holding something — that's a
        // drop intent, not a panel-open intent.
        if (e.button == PointerEventData.InputButton.Right
            && !_cursor.IsHeld
            && view.index >= 0 && view.index < view.container.Length
            && view.container[view.index].id == Hotbar.ItemId.FishBag)
        {
            var bag = view.container[view.index].bagContents;
            if (bag != null) ToggleBagPanel(bag);
            RefreshAll();
            return;
        }

        if (e.button == PointerEventData.InputButton.Left)
        {
            if (shift)
            {
                // Quick-move to the OTHER container.
                bool isStorage = ReferenceEquals(view.container, _active.Slots);
                var dest = isStorage ? hbSlots : _active.Slots;
                if (dest != null) SlotOps.HandleQuickMove(view.container, view.index, dest);
            }
            else
            {
                SlotOps.HandleLeftClick(view.container, view.index, ref _cursor);
            }
        }
        else if (e.button == PointerEventData.InputButton.Right)
        {
            SlotOps.HandleRightClick(view.container, view.index, ref _cursor);
        }

        RefreshAll();
        RefreshCursorVisual();
    }

    void ToggleBagPanel(Hotbar.Slot[] bag)
    {
        if (_bagPanel == null) return;
        if (ReferenceEquals(_activeBag, bag) && _bagPanel.gameObject.activeSelf)
        {
            _activeBag = null;
            _bagPanel.gameObject.SetActive(false);
        }
        else
        {
            _activeBag = bag;
            _bagPanel.gameObject.SetActive(true);
        }
    }

    void RefreshAll()
    {
        if (_active == null) return;
        var bs = _active.Slots;
        for (int i = 0; i < _storageViews.Length; i++)
        {
            var v = _storageViews[i];
            PaintSlot(v, i < bs.Length ? bs[i] : default);
            v.container = bs;
            v.index = i;
        }
        var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
        for (int i = 0; i < _hotbarViews.Length; i++)
        {
            var v = _hotbarViews[i];
            PaintSlot(v, hbSlots != null && i < hbSlots.Length ? hbSlots[i] : default);
            v.container = hbSlots;
            v.index = i;
        }

        // Phase 3: paint bag side panel slots when open.
        if (_bagPanel != null && _bagPanel.gameObject.activeSelf && _activeBag != null)
        {
            for (int k = 0; k < _bagViews.Length; k++)
            {
                var v = _bagViews[k];
                PaintSlot(v, k < _activeBag.Length ? _activeBag[k] : default);
                v.container = _activeBag;
                v.index = k;
            }
        }
    }

    void RefreshCursorVisual()
    {
        if (_cursorRoot == null) return;
        if (!_cursor.IsHeld) { _cursorRoot.gameObject.SetActive(false); return; }
        _cursorRoot.gameObject.SetActive(true);

        bool isFish = _cursor.id == Hotbar.ItemId.Fish && _cursor.fishData != null;
        if (isFish)
        {
            // Use the cached preview (or render now if first display).
            var fe = _cursor.fishData;
            if (fe.cachedHotbarPreview == null && FishingdexManager.Instance != null)
                fe.cachedHotbarPreview = FishingdexManager.Instance.RenderFish(fe, 64, 64);
            if (_cursorFishPreview != null)
            {
                _cursorFishPreview.texture = fe.cachedHotbarPreview;
                _cursorFishPreview.enabled = fe.cachedHotbarPreview != null;
            }
            _cursorIcon.enabled = false;
            _cursorIcon.sprite = null;
        }
        else
        {
            var sprite = ResolveIcon(_cursor.id);
            _cursorIcon.enabled = sprite != null;
            _cursorIcon.sprite = sprite;
            if (_cursorFishPreview != null)
            {
                _cursorFishPreview.enabled = false;
                _cursorFishPreview.texture = null;
            }
        }

        if (IsStackable(_cursor.id) && _cursor.count > 1)
        {
            _cursorCount.enabled = true;
            _cursorCount.text = _cursor.count.ToString();
        }
        else _cursorCount.enabled = false;
    }

    void PaintSlot(SlotView v, Hotbar.Slot s)
    {
        if (v == null) return;
        bool empty = s.id == Hotbar.ItemId.None || s.count <= 0;
        bool isFish = !empty && s.id == Hotbar.ItemId.Fish && s.fishData != null;
        v.background.color = empty
            ? new Color32(0x0C, 0x1A, 0x32, 0x66)
            : (Color)CyanScannerPalette.InnerBg;
        v.border.color = empty
            ? new Color32(0x1C, 0x3A, 0x5C, 0x80)
            : (Color)CyanScannerPalette.PanelBorder;
        // Phase 4 polish: FishBag picks empty vs full sprite from bagContents.
        // Other items use the static ResolveIcon path.
        Sprite resolvedSprite = null;
        if (!empty && !isFish)
        {
            resolvedSprite = s.id == Hotbar.ItemId.FishBag
                ? Hotbar.ResolveFishBagSprite(s.bagContents)
                : ResolveIcon(s.id);
        }
        bool standardIcon = resolvedSprite != null;
        v.itemIcon.enabled = standardIcon;
        v.itemIcon.sprite  = standardIcon ? resolvedSprite : null;
        v.itemIcon.color = Color.white;
        // FishBag art reads slightly small at the default icon size — scale 1.3x.
        float iconScale = (s.id == Hotbar.ItemId.FishBag) ? 1.3f : 1f;
        if (!Mathf.Approximately(v.itemIcon.rectTransform.localScale.x, iconScale))
            v.itemIcon.rectTransform.localScale = new Vector3(iconScale, iconScale, 1f);

        // Phase 3 polish: live fish preview via FishingdexManager.RenderFish.
        // Cached per FishEntry on FishEntry.cachedHotbarPreview so we render
        // once per fish per session, not per frame.
        if (v.fishPreview != null)
        {
            if (isFish)
            {
                var fe = s.fishData;
                if (fe.cachedHotbarPreview == null && FishingdexManager.Instance != null)
                    fe.cachedHotbarPreview = FishingdexManager.Instance.RenderFish(fe, 64, 64);
                v.fishPreview.texture = fe.cachedHotbarPreview;
                v.fishPreview.enabled = fe.cachedHotbarPreview != null;
            }
            else if (v.fishPreview.enabled)
            {
                v.fishPreview.enabled = false;
                v.fishPreview.texture = null;
            }
        }

        // Count badge: stackable items show count, fish shows weight, others empty.
        if (isFish)
        {
            v.countText.enabled = true;
            v.countText.text = s.fishData.weightLbs + " lb";
        }
        else
        {
            v.countText.enabled = !empty && IsStackable(s.id);
            if (v.countText.enabled) v.countText.text = s.count.ToString();
        }
    }

    static bool IsStackable(Hotbar.ItemId id) =>
        id == Hotbar.ItemId.Wood || id == Hotbar.ItemId.Crystal || id == Hotbar.ItemId.SpaceDust;

    static Sprite ResolveIcon(Hotbar.ItemId id)
    {
        switch (id)
        {
            case Hotbar.ItemId.Wood:      return Resources.Load<Sprite>("HotbarIcons/TransparentWoodLog");
            case Hotbar.ItemId.Crystal:   return Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
            case Hotbar.ItemId.SpaceDust: return Resources.Load<Sprite>("HotbarIcons/TransparentSpaceDust");
            // Fish: handled by RawImage fishPreview in PaintSlot — return null
            // here so the standard itemIcon Image stays disabled.
            case Hotbar.ItemId.Fish:      return null;
            // FishBag: no custom art; PaintSlot tints the placeholder green
            // via the bag-specific swatch path. For storage, just use the
            // crystal shape — single-instance constraint makes the slot
            // unambiguous; the side panel is the real interaction.
            case Hotbar.ItemId.FishBag:   return Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
        }
        switch (id)
        {
            case Hotbar.ItemId.WaterBottle: { var c = Object.FindObjectOfType<WaterBottleController>(true); return c != null ? c.hotbarIcon : null; }
            case Hotbar.ItemId.FishingRod:  { var c = Object.FindObjectOfType<FishingRodController>(true);  return c != null ? c.hotbarIcon : null; }
            case Hotbar.ItemId.Guitar:      { var c = Object.FindObjectOfType<GuitarController>(true);      return c != null ? c.hotbarIcon : null; }
            case Hotbar.ItemId.Axe:         { var c = Object.FindObjectOfType<AxeController>(true);         return c != null ? c.hotbarIcon : null; }
            case Hotbar.ItemId.Pistol:      { var c = Object.FindObjectOfType<PistolController>(true);      return c != null ? c.hotbarIcon : null; }
        }
        return null;
    }

    void ShowCanvas() { if (_root != null) _root.SetActive(true); }
    void HideCanvas() { if (_root != null) _root.SetActive(false); }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("StorageCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 900;
        HUDSceneGate.Register(_canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var blocker = NewRT("Blocker", canvasGo.transform);
        Stretch(blocker, 0, 0, 0, 0);
        var blockerImg = blocker.gameObject.AddComponent<Image>();
        blockerImg.color = new Color(0f, 0f, 0f, 0.5f);
        blockerImg.raycastTarget = true;

        float gridW  = StorageCols * SlotSize + (StorageCols - 1) * SlotGap;
        float gridH  = StorageRows * SlotSize + (StorageRows - 1) * SlotGap;
        float hotbarRowW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
        // Panel width is the larger of the storage grid and the hotbar row,
        // plus padding. With 7 hotbar tiles at 84px the hotbar row (624px)
        // is wider than the 5×4 storage grid (420px) and would otherwise
        // overflow the panel.
        float panelW = Mathf.Max(gridW, hotbarRowW) + PanelPad * 2;
        // Extra 56 px reserved at the bottom for the Exit button + spacing.
        float panelH = gridH + SlotSize + PanelPad * 2 + 130f + 56f;

        var panel = NewRT("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(panelW, panelH);
        var panelBg = panel.gameObject.AddComponent<Image>();
        panelBg.color = CyanScannerPalette.PanelBg;
        panelBg.raycastTarget = true;

        AddOutline(panel, CyanScannerPalette.PanelBorder);
        ScannerFrame.AddBrackets(panel, length: 14f, thickness: 2f);
        ScannerFrame.AddBlueprintGrid(panel, gridSpacing: 24f);

        var header = NewRT("Header", panel);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(-PanelPad * 2, 28f);
        header.anchoredPosition = new Vector2(0f, -PanelPad * 0.6f);
        var titleText = NewText(header, "Cargo Hold", TextAlignmentOptions.MidlineLeft, 18f);
        titleText.color = CyanScannerPalette.TextBright;
        titleText.characterSpacing = 4f;
        titleText.fontStyle = FontStyles.UpperCase;
        _taglineText = NewText(header, "SHIP 1 · BAY 1", TextAlignmentOptions.MidlineRight, 11f);
        _taglineText.color = CyanScannerPalette.TextMuted;
        _taglineText.characterSpacing = 2f;
        _taglineText.fontStyle = FontStyles.UpperCase;

        var storageLabel = NewRT("StorageLabel", panel);
        storageLabel.anchorMin = new Vector2(0f, 1f);
        storageLabel.anchorMax = new Vector2(1f, 1f);
        storageLabel.pivot = new Vector2(0.5f, 1f);
        storageLabel.sizeDelta = new Vector2(-PanelPad * 2, 16f);
        storageLabel.anchoredPosition = new Vector2(0f, -(PanelPad + 40f));
        var sLabel = NewText(storageLabel, "STORAGE  ·  20 SLOTS", TextAlignmentOptions.Center, 10f);
        sLabel.color = CyanScannerPalette.Accent;
        sLabel.characterSpacing = 3f;

        var storageGrid = NewRT("StorageGrid", panel);
        storageGrid.anchorMin = new Vector2(0.5f, 1f);
        storageGrid.anchorMax = new Vector2(0.5f, 1f);
        storageGrid.pivot = new Vector2(0.5f, 1f);
        storageGrid.sizeDelta = new Vector2(gridW, gridH);
        storageGrid.anchoredPosition = new Vector2(0f, -(PanelPad + 64f));
        for (int r = 0; r < StorageRows; r++)
        for (int c = 0; c < StorageCols; c++)
        {
            int i = r * StorageCols + c;
            float x = -gridW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
            float y = -r * (SlotSize + SlotGap) - SlotSize * 0.5f + gridH * 0.5f;
            _storageViews[i] = BuildSlotView(storageGrid, "S" + i, new Vector2(x, y));
        }

        // Hotbar row + label are pushed up by ExitArea so the Exit button
        // fits below them along the panel's bottom edge.
        const float ExitArea = 56f;
        var hotbarLabel = NewRT("HotbarLabel", panel);
        hotbarLabel.anchorMin = new Vector2(0f, 0f);
        hotbarLabel.anchorMax = new Vector2(1f, 0f);
        hotbarLabel.pivot = new Vector2(0.5f, 0f);
        hotbarLabel.sizeDelta = new Vector2(-PanelPad * 2, 16f);
        hotbarLabel.anchoredPosition = new Vector2(0f, PanelPad + SlotSize + 8f + ExitArea);
        var hLabel = NewText(hotbarLabel, "HOTBAR", TextAlignmentOptions.Center, 10f);
        hLabel.color = CyanScannerPalette.Accent;
        hLabel.characterSpacing = 3f;

        var hotbarRow = NewRT("HotbarRow", panel);
        hotbarRow.anchorMin = new Vector2(0.5f, 0f);
        hotbarRow.anchorMax = new Vector2(0.5f, 0f);
        hotbarRow.pivot = new Vector2(0.5f, 0f);
        hotbarRow.sizeDelta = new Vector2(hotbarRowW, SlotSize);
        hotbarRow.anchoredPosition = new Vector2(0f, PanelPad + ExitArea);
        for (int c = 0; c < HotbarSlots; c++)
        {
            float x = -hotbarRowW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
            // y = 0: slot anchor is at row center, slot pivot is at slot
            // center, so anchored y=0 puts the slot exactly inside the row.
            // (Previous SlotSize*0.5f pushed each slot 42 px above the row,
            //  which made the HOTBAR label overlap the slot tops.)
            _hotbarViews[c] = BuildSlotView(hotbarRow, "H" + c, new Vector2(x, 0f));
        }

        // Exit button below the hotbar row.
        BuildExitButton(panel);

        // Cursor follower — sits above all slots, raycast disabled.
        BuildCursorFollower(canvasGo.transform);

        // Phase 3: docked fish-bag side panel. Hidden by default; shown via
        // RMB on a FishBag slot.
        BuildBagSidePanel(panel);

        _root = canvasGo;
    }

    void BuildBagSidePanel(RectTransform mainPanel)
    {
        const float SidePanelW = SlotSize + 24f;                            // 1-column width + side padding
        const float SidePanelH = 5 * SlotSize + 4 * SlotGap + 56f;          // 5 stacked slots + label area

        var side = NewRT("BagSidePanel", mainPanel);
        side.anchorMin = new Vector2(1f, 1f);
        side.anchorMax = new Vector2(1f, 1f);
        side.pivot = new Vector2(0f, 1f);   // top-left of side panel attaches to top-right of main
        side.anchoredPosition = new Vector2(16f, 0f);
        side.sizeDelta = new Vector2(SidePanelW, SidePanelH);

        var bg = side.gameObject.AddComponent<Image>();
        bg.color = CyanScannerPalette.PanelBg;
        bg.raycastTarget = true;
        AddOutline(side, CyanScannerPalette.PanelBorder);
        ScannerFrame.AddBrackets(side, length: 12f, thickness: 2f);

        var label = NewRT("BagLabel", side);
        label.anchorMin = new Vector2(0f, 1f);
        label.anchorMax = new Vector2(1f, 1f);
        label.pivot = new Vector2(0.5f, 1f);
        label.sizeDelta = new Vector2(-12f, 18f);
        label.anchoredPosition = new Vector2(0f, -10f);
        var lTxt = NewText(label, "FISH BAG · 5", TextAlignmentOptions.Center, 10f);
        lTxt.color = CyanScannerPalette.Accent;
        lTxt.characterSpacing = 3f;

        // 5 slot tiles, stacked vertically below the label. Slot anchor is
        // panel-center; positive y = up. First slot sits just below the
        // label area (56px reserved at top); each subsequent slot drops
        // by SlotSize + SlotGap.
        float topSlotY = SidePanelH * 0.5f - 56f - SlotSize * 0.5f;
        for (int k = 0; k < 5; k++)
        {
            float y = topSlotY - k * (SlotSize + SlotGap);
            _bagViews[k] = BuildSlotView(side, "B" + k, new Vector2(0f, y));
        }

        _bagPanel = side;
        _bagPanel.gameObject.SetActive(false);
    }

    void BuildExitButton(RectTransform parent)
    {
        var rt = NewRT("ExitButton", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(180f, 36f);
        rt.anchoredPosition = new Vector2(0f, PanelPad);

        // Square fill — no rounded sprite, hard-edged rectangle to match
        // the scanner-blueprint panel's overall right-angle aesthetic.
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = CyanScannerPalette.BtnNormal;
        bg.raycastTarget = true;

        // Square cyan outline made of 4 thin strips (same pattern AddOutline
        // uses on the main panel).
        AddOutline(rt, CyanScannerPalette.Accent);

        var label = NewText(rt, "CLOSE  [ F ]", TextAlignmentOptions.Center, 12f);
        label.color = CyanScannerPalette.Accent;
        label.characterSpacing = 4f;
        label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;

        // Hover tint + click → RequestClose.
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color32(0xCC, 0xEC, 0xFF, 0xFF);
        colors.pressedColor     = new Color32(0x88, 0xC4, 0xDC, 0xFF);
        colors.selectedColor    = Color.white;
        colors.colorMultiplier  = 1f;
        btn.colors = colors;
        btn.targetGraphic = bg;
        btn.onClick.AddListener(RequestClose);
    }

    void BuildCursorFollower(Transform parent)
    {
        var cur = NewRT("CursorItem", parent);
        cur.anchorMin = cur.anchorMax = new Vector2(0f, 0f);
        cur.pivot = new Vector2(0.5f, 0.5f);
        cur.sizeDelta = new Vector2(64f, 64f);
        _cursorRoot = cur;

        var bg = NewRT("__Bg", cur);
        Stretch(bg, 0, 0, 0, 0);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = GalaxyHudKit.RoundedSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color32(0x14, 0x30, 0x55, 0xF2);
        bgImg.raycastTarget = false;

        var border = NewRT("__Border", cur);
        Stretch(border, 0, 0, 0, 0);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.color = CyanScannerPalette.Accent;
        borderImg.sprite = HotbarRoundedRing.GetSprite();
        borderImg.type = Image.Type.Sliced;
        borderImg.raycastTarget = false;

        var ico = NewRT("__Icon", cur);
        ico.anchorMin = ico.anchorMax = new Vector2(0.5f, 0.5f);
        ico.pivot = new Vector2(0.5f, 0.5f);
        ico.sizeDelta = new Vector2(44f, 44f);
        _cursorIcon = ico.gameObject.AddComponent<Image>();
        _cursorIcon.preserveAspect = true;
        _cursorIcon.raycastTarget = false;

        // Phase 3 polish: cursor's fish preview RawImage. Sits at the same
        // anchor + size as _cursorIcon; toggled instead of it when cursor
        // carries a Fish.
        var fp = NewRT("__FishPreview", cur);
        fp.anchorMin = fp.anchorMax = new Vector2(0.5f, 0.5f);
        fp.pivot = new Vector2(0.5f, 0.5f);
        fp.sizeDelta = new Vector2(44f, 44f);
        _cursorFishPreview = fp.gameObject.AddComponent<RawImage>();
        _cursorFishPreview.raycastTarget = false;
        _cursorFishPreview.enabled = false;

        var cnt = NewRT("__Count", cur);
        cnt.anchorMin = cnt.anchorMax = new Vector2(1f, 0f);
        cnt.pivot = new Vector2(1f, 0f);
        cnt.anchoredPosition = new Vector2(-4f, 2f);
        cnt.sizeDelta = new Vector2(40f, 18f);
        _cursorCount = cnt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_cursorCount);
        _cursorCount.fontSize = 14f;
        _cursorCount.fontStyle = FontStyles.Bold;
        _cursorCount.alignment = TextAlignmentOptions.BottomRight;
        _cursorCount.color = Color.white;
        _cursorCount.raycastTarget = false;
        var drop = cnt.gameObject.AddComponent<Shadow>();
        drop.effectColor = new Color(0f, 0f, 0f, 0.9f);
        drop.effectDistance = new Vector2(0f, -1.5f);

        cur.gameObject.SetActive(false);
    }

    SlotView BuildSlotView(RectTransform parent, string name, Vector2 pos)
    {
        var v = new SlotView();
        var rt = NewRT(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(SlotSize, SlotSize);
        rt.anchoredPosition = pos;
        v.root = rt;

        var bgRt = NewRT("__Bg", rt);
        Stretch(bgRt, 0, 0, 0, 0);
        v.background = bgRt.gameObject.AddComponent<Image>();
        // Rounded fill — sprite is white, tinted by color. Without a sprite
        // the Image renders as a hard rectangle that bleeds outside the
        // rounded border ring.
        v.background.sprite = GalaxyHudKit.RoundedSprite();
        v.background.type = Image.Type.Sliced;
        v.background.color = CyanScannerPalette.InnerBg;
        v.background.raycastTarget = true;
        // Click handler attached to the background — receives LMB/RMB/Shift.
        var click = bgRt.gameObject.AddComponent<StorageSlotClick>();
        click.owner = this;
        click.view = v;

        var borderRt = NewRT("__Border", rt);
        Stretch(borderRt, 0, 0, 0, 0);
        v.border = borderRt.gameObject.AddComponent<Image>();
        v.border.color = CyanScannerPalette.PanelBorder;
        v.border.sprite = HotbarRoundedRing.GetSprite();
        v.border.type = Image.Type.Sliced;
        v.border.raycastTarget = false;

        var iconRt = NewRT("__Icon", rt);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(56f, 56f);
        v.itemIcon = iconRt.gameObject.AddComponent<Image>();
        v.itemIcon.preserveAspect = true;
        v.itemIcon.raycastTarget = false;

        // Phase 3 polish: live fish preview RawImage (same shape as Hotbar
        // slot's fishPreview). Bound to FishingdexManager.RenderFish output
        // in PaintSlot; replaces the placeholder crystal sprite the storage
        // UI used for fish in Phase 1.
        var fpRt = NewRT("__FishPreview", rt);
        fpRt.anchorMin = fpRt.anchorMax = new Vector2(0.5f, 0.5f);
        fpRt.pivot = new Vector2(0.5f, 0.5f);
        fpRt.sizeDelta = new Vector2(56f, 56f);
        v.fishPreview = fpRt.gameObject.AddComponent<RawImage>();
        v.fishPreview.raycastTarget = false;
        v.fishPreview.enabled = false;

        var countRt = NewRT("__Count", rt);
        countRt.anchorMin = countRt.anchorMax = new Vector2(1f, 0f);
        countRt.pivot = new Vector2(1f, 0f);
        countRt.anchoredPosition = new Vector2(-6f, 4f);
        countRt.sizeDelta = new Vector2(40f, 20f);
        v.countText = countRt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(v.countText);
        v.countText.fontSize = 15f;
        v.countText.fontStyle = FontStyles.Bold;
        v.countText.alignment = TextAlignmentOptions.BottomRight;
        v.countText.color = Color.white;
        v.countText.raycastTarget = false;
        var drop = countRt.gameObject.AddComponent<Shadow>();
        drop.effectColor = new Color(0f, 0f, 0f, 0.9f);
        drop.effectDistance = new Vector2(0f, -1.5f);

        return v;
    }

    void AddOutline(RectTransform parent, Color32 color)
    {
        void Strip(string n, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 off)
        {
            var rt = NewRT(n, parent);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = off;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }
        Strip("EdgeT", new Vector2(0,1), new Vector2(1,1), new Vector2(0,1), new Vector2(0, -0.5f));
        Strip("EdgeB", new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(0,  0.5f));
        Strip("EdgeL", new Vector2(0,0), new Vector2(0,1), new Vector2(1,0), new Vector2( 0.5f, 0));
        Strip("EdgeR", new Vector2(1,0), new Vector2(1,1), new Vector2(1,0), new Vector2(-0.5f, 0));
    }

    static RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(right, top);
    }

    static TextMeshProUGUI NewText(RectTransform parent, string text, TextAlignmentOptions anchor, float size)
    {
        var rt = NewRT("Text", parent);
        Stretch(rt, 0, 0, 0, 0);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text; t.fontSize = size; t.alignment = anchor; t.raycastTarget = false;
        return t;
    }
}
