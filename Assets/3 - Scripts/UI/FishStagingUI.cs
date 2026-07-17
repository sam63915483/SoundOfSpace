using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Phase 4: shared drag-and-drop picker for cook + sell staging. Caller
// (BonfireInteraction / FishMarketNPC) invokes Open(title, onConfirm);
// player drags fish from hotbar (and bag via RMB side panel) into 10
// stage slots; Confirm fires the callback with the staged list +
// per-fish FishSource; Cancel returns each staged fish to its exact
// original slot (fallback chain: source slot → next-empty-in-source
// container → bag → hotbar → destroy + popup).
//
// Modeled on StorageUI. Bag side-panel logic is duplicated rather than
// extracted (Phase 4 keeps risk low; refactor candidate for Phase 5).
public class FishStagingUI : MonoBehaviour
{
    static FishStagingUI instance;
    public static FishStagingUI Instance => instance;

    public bool IsOpen { get; private set; }

    const int   StageSlots  = 10;
    const int   StageCols   = 5;
    const int   StageRows   = 2;
    const int   HotbarSlots = 7;
    const float SlotSize    = 84f;
    const float SlotGap     = 6f;
    const float PanelPad    = 32f;

    Canvas _canvas;
    GameObject _root;
    SlotView[] _stageViews = new SlotView[StageSlots];
    Hotbar.Slot[] _stageSlots = new Hotbar.Slot[StageSlots];
    FishSource[] _sources = new FishSource[StageSlots];
    SlotView[] _hotbarViews = new SlotView[HotbarSlots];
    SlotView[] _bagViews = new SlotView[5];
    RectTransform _bagPanel;
    Hotbar.Slot[] _activeBag;
    SlotOps.CursorState _cursor;
    RectTransform _cursorRoot;
    Image _cursorIcon;
    RawImage _cursorFishPreview;
    TextMeshProUGUI _cursorCount;
    TextMeshProUGUI _titleText;
    // Phase 4: snapshot cursor state at Open so Close restores whatever the
    // parent (cook/sell panel) had before. Without this, the picker locks
    // the cursor on Confirm — but the parent panel is still open and needs
    // the cursor free so the player can click Cook / Sell.
    CursorLockMode _cursorLockStateBeforeOpen;
    bool _cursorVisibleBeforeOpen;

    System.Action<List<(FishEntry fish, FishSource source)>> _onConfirm;

    class SlotView
    {
        public RectTransform root;
        public Image background;
        public Image border;
        public Image itemIcon;
        public RawImage fishPreview;
        public TextMeshProUGUI countText;
        public Hotbar.Slot[] container;
        public int index;
        public Selectable selectable;   // pad navigation (see WireSlotNav)
    }

    // ISubmitHandler makes the pad's A press act as a left-click on the
    // focused slot (the slot GO also carries a Selectable for nav); X is
    // handled in Update as right-click.
    class StagingSlotClick : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        public FishStagingUI owner;
        public SlotView view;
        public void OnPointerClick(PointerEventData e) { if (owner != null) owner.OnSlotClicked(view, e); }
        public void OnSubmit(BaseEventData e)
        {
            if (owner == null) return;
            owner.OnSlotClicked(view, new PointerEventData(EventSystem.current) {
                button = PointerEventData.InputButton.Left });
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("FishStagingUI");
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<FishStagingUI>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        BuildCanvas();
        HideCanvas();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    public void Open(string title, System.Action<List<(FishEntry fish, FishSource source)>> onConfirm)
    {
        if (IsOpen) return;
        _onConfirm = onConfirm;
        for (int k = 0; k < StageSlots; k++) { _stageSlots[k] = default; _sources[k] = default; }
        _cursor = default;
        IsOpen = true;
        PlayerController.isInModalSlotUI = true;
        _cursorLockStateBeforeOpen = Cursor.lockState;
        _cursorVisibleBeforeOpen = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Hotbar.Instance != null) Hotbar.Instance.OnStorageOpened();
        if (_titleText != null) _titleText.text = title ?? "STAGE FISH";
        ShowCanvas();
        RefreshAll();
        RefreshCursorVisual();
    }

    // External close (e.g. caller's parent panel forcing teardown). Same path
    // as Cancel — returns staged fish to source.
    public void RequestClose()
    {
        if (!IsOpen) return;
        OnCancelClicked();
    }

    void OnConfirmClicked()
    {
        var list = new List<(FishEntry, FishSource)>();
        for (int k = 0; k < StageSlots; k++)
        {
            if (_stageSlots[k].id != Hotbar.ItemId.Fish) continue;
            if (_stageSlots[k].fishData == null) continue;
            list.Add((_stageSlots[k].fishData, _sources[k]));
            _stageSlots[k] = default;
            _sources[k] = default;
        }
        if (_cursor.IsHeld) SlotOps.ReturnHeldToSource(ref _cursor);
        var cb = _onConfirm; _onConfirm = null;
        Close();
        cb?.Invoke(list);
    }

    void OnCancelClicked()
    {
        if (_cursor.IsHeld) SlotOps.ReturnHeldToSource(ref _cursor);
        for (int k = 0; k < StageSlots; k++)
        {
            if (_stageSlots[k].id != Hotbar.ItemId.Fish) continue;
            if (_stageSlots[k].fishData == null) { _stageSlots[k] = default; _sources[k] = default; continue; }
            bool placed = TryReturnTo(_stageSlots[k].fishData, _sources[k]);
            if (!placed) InventoryFullPopup.Show();
            _stageSlots[k] = default;
            _sources[k] = default;
        }
        _onConfirm = null;
        Close();
    }

    // Phase 4 return-to-source chain. Tries exact source first; falls back
    // through (next-empty-in-source-container → bag → hotbar). Returns
    // false only when nothing fit anywhere — caller pops InventoryFullPopup.
    // Public so cook/sell panels can use it for their own cancel paths.
    public static bool TryReturnTo(FishEntry fe, FishSource src)
    {
        if (fe == null) return true;
        if (src.IsValid && src.container[src.index].id == Hotbar.ItemId.None)
        {
            src.container[src.index] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = fe };
            return true;
        }
        if (src.IsValid)
        {
            for (int i = 0; i < src.container.Length; i++)
            {
                if (src.container[i].id != Hotbar.ItemId.None) continue;
                src.container[i] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = fe };
                return true;
            }
        }
        if (Hotbar.Instance != null && Hotbar.Instance.TryAddFishToBag(fe)) return true;
        if (Hotbar.Instance != null && Hotbar.Instance.TryAddFish(fe)) return true;
        return false;
    }

    void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        PlayerController.isInModalSlotUI = false;
        // Phase 4: restore cursor to whatever the parent (cook/sell panel)
        // had before Open — they're still up and need the cursor free.
        Cursor.lockState = _cursorLockStateBeforeOpen;
        Cursor.visible = _cursorVisibleBeforeOpen;
        if (_bagPanel != null) _bagPanel.gameObject.SetActive(false);
        _activeBag = null;
        _cursor = default;
        HideCanvas();
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
        {
            OnCancelClicked();
            return;
        }
        // Pad: Y confirms (A stays reserved for clicking whichever slot
        // button the nav focus is on).
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
            || TutorialGate.PadPressed(TutorialGate.PadButton.Y))
        {
            OnConfirmClicked();
            return;
        }
        // NOTE: NOT gating on PlayerController.isInDialogue — the picker is
        // invoked from BonfireInteraction / FishMarketNPC which set isInDialogue
        // true while their panel is open, so the picker MUST coexist with it.
        // Gating on dialogue here would auto-cancel the picker on the first
        // Update tick after Open.
        if (PlayerController.isMapOpen || PlayerPhoneUI.IsOpen
            || Ship.AnyShipPiloted)
        {
            OnCancelClicked();
            return;
        }
        // Pad: X on the focused slot = right-click (pick one / fish-bag panel).
        if (TutorialGate.PadPressed(TutorialGate.PadButton.X))
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            var click = go != null ? go.GetComponent<StagingSlotClick>() : null;
            if (click != null)
                OnSlotClicked(click.view, new PointerEventData(es) {
                    button = PointerEventData.InputButton.Right });
        }

        WireSlotNav();

        // Cursor follower: mouse on KBM; on pad, snap to the focused slot.
        if (_cursorRoot != null && _cursorRoot.gameObject.activeSelf && _canvas != null)
        {
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            Vector2 screen = Input.mousePosition;
            if (TutorialGate.LastSource == TutorialGate.InputSource.Controller)
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go != null && go.GetComponent<StagingSlotClick>() != null)
                    screen = RectTransformUtility.WorldToScreenPoint(null, go.transform.position)
                             + new Vector2(28f, -28f);
            }
            _cursorRoot.anchoredPosition = screen / scale;
        }
        RefreshAll();
        RefreshCursorVisual();
    }

    // Explicit pad-navigation wiring — mirrors StorageUI.WireSlotNav (see
    // that comment for the why). Stage grid is 5×2; down from the hotbar
    // reaches CONFIRM (left half) / CANCEL (right half), which Y/B also
    // trigger directly.
    void WireSlotNav()
    {
        bool bagOpen = _bagPanel != null && _bagPanel.gameObject.activeSelf;

        for (int r = 0; r < StageRows; r++)
        for (int c = 0; c < StageCols; c++)
        {
            int i = r * StageCols + c;
            var sel = SlotSel(_stageViews[i]);
            if (sel == null) continue;
            sel.navigation = new Navigation {
                mode          = Navigation.Mode.Explicit,
                selectOnUp    = r > 0 ? SlotSel(_stageViews[i - StageCols]) : null,
                selectOnDown  = r < StageRows - 1 ? SlotSel(_stageViews[i + StageCols])
                                                  : SlotSel(_hotbarViews[Mathf.Min(c, HotbarSlots - 1)]),
                selectOnLeft  = c > 0 ? SlotSel(_stageViews[i - 1]) : null,
                selectOnRight = c < StageCols - 1 ? SlotSel(_stageViews[i + 1])
                              : (bagOpen ? SlotSel(_bagViews[Mathf.Min(r, _bagViews.Length - 1)]) : null),
            };
        }

        for (int c = 0; c < HotbarSlots; c++)
        {
            var sel = SlotSel(_hotbarViews[c]);
            if (sel == null) continue;
            sel.navigation = new Navigation {
                mode          = Navigation.Mode.Explicit,
                selectOnUp    = SlotSel(_stageViews[(StageRows - 1) * StageCols + Mathf.Min(c, StageCols - 1)]),
                selectOnDown  = c <= HotbarSlots / 2 ? (Selectable)_confirmBtn : (Selectable)_cancelBtn,
                selectOnLeft  = c > 0 ? SlotSel(_hotbarViews[c - 1]) : null,
                selectOnRight = c < HotbarSlots - 1 ? SlotSel(_hotbarViews[c + 1])
                              : (bagOpen ? SlotSel(_bagViews[_bagViews.Length - 1]) : null),
            };
        }

        if (_confirmBtn != null)
            _confirmBtn.navigation = new Navigation {
                mode          = Navigation.Mode.Explicit,
                selectOnUp    = SlotSel(_hotbarViews[2]),
                selectOnRight = _cancelBtn,
            };
        if (_cancelBtn != null)
            _cancelBtn.navigation = new Navigation {
                mode          = Navigation.Mode.Explicit,
                selectOnUp    = SlotSel(_hotbarViews[4]),
                selectOnLeft  = _confirmBtn,
            };

        for (int k = 0; k < _bagViews.Length; k++)
        {
            var sel = SlotSel(_bagViews[k]);
            if (sel == null) continue;
            sel.navigation = new Navigation {
                mode          = Navigation.Mode.Explicit,
                selectOnUp    = k > 0 ? SlotSel(_bagViews[k - 1]) : null,
                selectOnDown  = k < _bagViews.Length - 1 ? SlotSel(_bagViews[k + 1]) : null,
                selectOnLeft  = SlotSel(_stageViews[Mathf.Min(k, StageRows - 1) * StageCols + StageCols - 1]),
            };
        }
    }

    static Selectable SlotSel(SlotView v) => v != null ? v.selectable : null;

    void OnSlotClicked(SlotView view, PointerEventData e)
    {
        if (view == null || view.container == null) return;
        var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                  || TutorialGate.PadHeld(TutorialGate.PadButton.LB);   // pad: LB = shift-transfer

        // RMB on FishBag opens the side panel (mirror StorageUI behavior).
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

        // Snapshot cursor source BEFORE the SlotOps call so we know where the
        // fish came from when capturing _sources[k] for a stage slot drop.
        var srcContainerBefore = _cursor.sourceContainer;
        var srcIndexBefore = _cursor.sourceIndex;

        if (e.button == PointerEventData.InputButton.Left)
        {
            if (shift)
            {
                bool fromStage = ReferenceEquals(view.container, _stageSlots);
                var dest = fromStage ? hbSlots : _stageSlots;
                if (dest != null)
                {
                    var s = view.container[view.index];
                    SlotOps.HandleQuickMove(view.container, view.index, dest);
                    if (!fromStage)
                    {
                        // Source was hotbar/bag/storage; find where fish landed
                        // in stage and record source.
                        for (int k = 0; k < StageSlots; k++)
                        {
                            if (_stageSlots[k].id == Hotbar.ItemId.Fish
                                && ReferenceEquals(_stageSlots[k].fishData, s.fishData))
                            {
                                _sources[k] = new FishSource { container = view.container, index = view.index };
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Stage → hotbar quick-move: clear sources for emptied slots.
                        for (int k = 0; k < StageSlots; k++)
                            if (_stageSlots[k].id == Hotbar.ItemId.None) _sources[k] = default;
                    }
                }
            }
            else
            {
                SlotOps.HandleLeftClick(view.container, view.index, ref _cursor);
                CaptureSourceAfterClick(view, srcContainerBefore, srcIndexBefore);
            }
        }
        else if (e.button == PointerEventData.InputButton.Right)
        {
            SlotOps.HandleRightClick(view.container, view.index, ref _cursor);
            CaptureSourceAfterClick(view, srcContainerBefore, srcIndexBefore);
        }

        RefreshAll();
        RefreshCursorVisual();
    }

    void CaptureSourceAfterClick(SlotView view, Hotbar.Slot[] cursorSrcContainerBefore, int cursorSrcIndexBefore)
    {
        if (!ReferenceEquals(view.container, _stageSlots)) return;
        if (cursorSrcContainerBefore == null)
        {
            // Pickup from stage — source data went onto the cursor; clear
            // the now-empty stage slot's source record.
            _sources[view.index] = default;
            return;
        }
        if (_stageSlots[view.index].id == Hotbar.ItemId.Fish)
        {
            _sources[view.index] = new FishSource
            {
                container = cursorSrcContainerBefore,
                index = cursorSrcIndexBefore,
            };
        }
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
        for (int i = 0; i < _stageViews.Length; i++)
        {
            var v = _stageViews[i];
            PaintSlot(v, _stageSlots[i]);
            v.container = _stageSlots;
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
        _cursorCount.enabled = false;
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
        v.itemIcon.color   = Color.white;
        // FishBag art reads slightly small at the default icon size — scale 1.3x.
        float iconScale = (s.id == Hotbar.ItemId.FishBag) ? 1.3f : 1f;
        if (!Mathf.Approximately(v.itemIcon.rectTransform.localScale.x, iconScale))
            v.itemIcon.rectTransform.localScale = new Vector3(iconScale, iconScale, 1f);

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

    // Icon sprites are session-stable (Resources assets + controller.hotbarIcon).
    // PaintSlot runs every frame per populated slot while the panel is open, so the
    // uncached path did a Resources.Load + a full-scene FindObjectOfType per tool
    // slot per frame. Cache each id's sprite once it resolves non-null.
    static readonly Dictionary<Hotbar.ItemId, Sprite> _iconCache = new Dictionary<Hotbar.ItemId, Sprite>();
    static Sprite ResolveIcon(Hotbar.ItemId id)
    {
        if (_iconCache.TryGetValue(id, out var cached) && cached != null) return cached;
        var result = ResolveIconUncached(id);
        if (result != null) _iconCache[id] = result;
        return result;
    }

    static Sprite ResolveIconUncached(Hotbar.ItemId id)
    {
        switch (id)
        {
            case Hotbar.ItemId.Wood:      return Resources.Load<Sprite>("HotbarIcons/TransparentWoodLog");
            case Hotbar.ItemId.Crystal:   return Resources.Load<Sprite>("HotbarIcons/TransparentCrystalShards");
            case Hotbar.ItemId.SpaceDust: return Resources.Load<Sprite>("HotbarIcons/TransparentSpaceDust");
            case Hotbar.ItemId.Fish:      return null;
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

    // ── Procedural UI build ─────────────────────────────────────────

    void BuildCanvas()
    {
        var canvasGo = new GameObject("FishStagingCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 910;
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

        float stageGridW = StageCols * SlotSize + (StageCols - 1) * SlotGap;
        float stageGridH = StageRows * SlotSize + (StageRows - 1) * SlotGap;
        float hotbarRowW = HotbarSlots * SlotSize + (HotbarSlots - 1) * SlotGap;
        float panelW = Mathf.Max(stageGridW, hotbarRowW) + PanelPad * 2;
        float panelH = stageGridH + SlotSize + PanelPad * 2 + 130f + 56f;

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
        _titleText = NewText(header, "STAGE FISH", TextAlignmentOptions.MidlineLeft, 18f);
        _titleText.color = CyanScannerPalette.TextBright;
        _titleText.characterSpacing = 4f;
        _titleText.fontStyle = FontStyles.UpperCase;

        var stageLabel = NewRT("StageLabel", panel);
        stageLabel.anchorMin = new Vector2(0f, 1f);
        stageLabel.anchorMax = new Vector2(1f, 1f);
        stageLabel.pivot = new Vector2(0.5f, 1f);
        stageLabel.sizeDelta = new Vector2(-PanelPad * 2, 16f);
        stageLabel.anchoredPosition = new Vector2(0f, -(PanelPad + 40f));
        var sLbl = NewText(stageLabel, "STAGE  ·  10 SLOTS", TextAlignmentOptions.Center, 10f);
        sLbl.color = CyanScannerPalette.Accent;
        sLbl.characterSpacing = 3f;

        var stageGrid = NewRT("StageGrid", panel);
        stageGrid.anchorMin = new Vector2(0.5f, 1f);
        stageGrid.anchorMax = new Vector2(0.5f, 1f);
        stageGrid.pivot = new Vector2(0.5f, 1f);
        stageGrid.sizeDelta = new Vector2(stageGridW, stageGridH);
        stageGrid.anchoredPosition = new Vector2(0f, -(PanelPad + 64f));
        for (int r = 0; r < StageRows; r++)
        for (int c = 0; c < StageCols; c++)
        {
            int i = r * StageCols + c;
            float x = -stageGridW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
            float y = -r * (SlotSize + SlotGap) - SlotSize * 0.5f + stageGridH * 0.5f;
            _stageViews[i] = BuildSlotView(stageGrid, "S" + i, new Vector2(x, y));
        }

        const float ButtonArea = 56f;
        var hotbarLabel = NewRT("HotbarLabel", panel);
        hotbarLabel.anchorMin = new Vector2(0f, 0f);
        hotbarLabel.anchorMax = new Vector2(1f, 0f);
        hotbarLabel.pivot = new Vector2(0.5f, 0f);
        hotbarLabel.sizeDelta = new Vector2(-PanelPad * 2, 16f);
        hotbarLabel.anchoredPosition = new Vector2(0f, PanelPad + SlotSize + 8f + ButtonArea);
        var hLbl = NewText(hotbarLabel, "HOTBAR", TextAlignmentOptions.Center, 10f);
        hLbl.color = CyanScannerPalette.Accent;
        hLbl.characterSpacing = 3f;

        var hotbarRow = NewRT("HotbarRow", panel);
        hotbarRow.anchorMin = new Vector2(0.5f, 0f);
        hotbarRow.anchorMax = new Vector2(0.5f, 0f);
        hotbarRow.pivot = new Vector2(0.5f, 0f);
        hotbarRow.sizeDelta = new Vector2(hotbarRowW, SlotSize);
        hotbarRow.anchoredPosition = new Vector2(0f, PanelPad + ButtonArea);
        for (int c = 0; c < HotbarSlots; c++)
        {
            float x = -hotbarRowW * 0.5f + c * (SlotSize + SlotGap) + SlotSize * 0.5f;
            _hotbarViews[c] = BuildSlotView(hotbarRow, "H" + c, new Vector2(x, 0f));
        }

        BuildButtonRow(panel);
        BuildCursorFollower(canvasGo.transform);
        BuildBagSidePanel(panel);

        _root = canvasGo;
    }

    void BuildButtonRow(RectTransform parent)
    {
        var row = NewRT("ButtonRow", parent);
        row.anchorMin = new Vector2(0.5f, 0f);
        row.anchorMax = new Vector2(0.5f, 0f);
        row.pivot = new Vector2(0.5f, 0f);
        row.sizeDelta = new Vector2(400f, 40f);
        row.anchoredPosition = new Vector2(0f, PanelPad);

        _confirmBtn = BuildButton(row, "CONFIRM  [ ENTER ]", new Vector2(-105f, 0f), OnConfirmClicked, new Color32(0x42, 0x88, 0x55, 0xFF));
        _cancelBtn  = BuildButton(row, "CANCEL  [ ESC ]",    new Vector2( 105f, 0f), OnCancelClicked,  new Color32(0x88, 0x44, 0x44, 0xFF));
    }

    Button _confirmBtn;   // pad navigation targets below the hotbar row
    Button _cancelBtn;

    Button BuildButton(RectTransform parent, string label, Vector2 anchored, UnityEngine.Events.UnityAction onClick, Color32 fill)
    {
        var rt = NewRT("Btn_" + label, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(180f, 36f);
        rt.anchoredPosition = anchored;
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = fill;
        bg.raycastTarget = true;
        AddOutline(rt, CyanScannerPalette.Accent);
        var lbl = NewText(rt, label, TextAlignmentOptions.Center, 12f);
        lbl.color = Color.white;
        lbl.characterSpacing = 4f;
        lbl.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    void BuildBagSidePanel(RectTransform mainPanel)
    {
        const float SidePanelW = SlotSize + 24f;
        // Match the main picker panel's height exactly so the two panels
        // visually align top + bottom. 5 slots fit comfortably inside.
        float SidePanelH = mainPanel.sizeDelta.y;
        var side = NewRT("BagSidePanel", mainPanel);
        side.anchorMin = new Vector2(1f, 1f);
        side.anchorMax = new Vector2(1f, 1f);
        side.pivot = new Vector2(0f, 1f);
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

        float topY = SidePanelH * 0.5f - 56f - SlotSize * 0.5f;
        for (int k = 0; k < 5; k++)
        {
            float y = topY - k * (SlotSize + SlotGap);
            _bagViews[k] = BuildSlotView(side, "B" + k, new Vector2(0f, y));
        }
        _bagPanel = side;
        _bagPanel.gameObject.SetActive(false);
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
        _cursorCount.alignment = TextAlignmentOptions.BottomRight;
        _cursorCount.color = Color.white;
        _cursorCount.raycastTarget = false;
        _cursorCount.enabled = false;

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
        v.background.sprite = GalaxyHudKit.RoundedSprite();
        v.background.type = Image.Type.Sliced;
        v.background.color = CyanScannerPalette.InnerBg;
        v.background.raycastTarget = true;
        var click = bgRt.gameObject.AddComponent<StagingSlotClick>();
        click.owner = this;
        click.view = v;
        // Selectable (no visual transition — the navigator's focus ring is
        // the highlight) so pad navigation can walk the slots and Submit
        // reaches StagingSlotClick.OnSubmit.
        var sel = bgRt.gameObject.AddComponent<Selectable>();
        sel.transition = Selectable.Transition.None;
        sel.targetGraphic = v.background;
        v.selectable = sel;

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

    static void AddOutline(RectTransform parent, Color32 color)
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
