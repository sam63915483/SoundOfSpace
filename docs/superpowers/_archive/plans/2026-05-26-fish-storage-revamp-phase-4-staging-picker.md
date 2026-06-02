# Fish & Storage Revamp — Phase 4 (Cook + Sell Staging Picker) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Phase 2's "Add Fish takes first hotbar fish" placeholder in `BonfireInteraction` and `FishMarketNPC` with a shared drag-and-drop picker (`FishStagingUI`). 10 stage slots + hotbar mirror + bag side panel. Confirm commits via caller callback; Cancel returns staged fish to their exact original slots with fallback to bag → hotbar → destroy.

**Architecture:** New singleton `FishStagingUI` modeled on `StorageUI` (cursor follower, SlotOps drag/drop, RMB-on-FishBag side panel — duplicated for Phase 4 safety). New `FishSource` struct carries `(container, index)` per staged fish through the picker → cook/sell stage lists so cancel can return-to-exact-source. `PlayerController.isInStorage` renames to `isInModalSlotUI` so input gates apply to both StorageUI and the picker.

**Tech Stack:** Unity 2022.3, C#, default `Assembly-CSharp`. `JsonUtility` saves (Phase 4 adds none). No test framework — manual Editor regression.

---

## File Structure

| File | Responsibility | Changes |
|---|---|---|
| `Assets/3 - Scripts/UI/FishStagingUI.cs` | **NEW.** Picker singleton — 10 stage slots, hotbar mirror, cursor follower, bag side panel, Confirm/Cancel buttons, source tracking, return-to-source chain |
| `Assets/3 - Scripts/UI/SlotOps.cs` | New `FishSource` struct (small) |
| `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` | Rename `isInStorage` → `isInModalSlotUI` |
| `Assets/3 - Scripts/UI/StorageUI.cs` | Rename callsite `PlayerController.isInStorage = ...` → `isInModalSlotUI` |
| `Assets/3 - Scripts/UI/Hotbar.cs` | Rename callsites |
| `Assets/3 - Scripts/Camera/CameraFOVFX.cs` | Rename callsite |
| `Assets/3 - Scripts/Camera/CameraTransformFX.cs` | Rename callsite |
| `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs` | `OnAddFishClicked` opens picker; `stagedFish` gets `FishSource` tuple field; `OnRemoveFish` + bonfire-close return to source |
| `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` | Mirror of cook changes |

8 tasks total.

---

## Verification Strategy

Same as prior phases: per-task verification = code change → `mcp__coplay-mcp__check_compile_errors` → behavioral check when meaningful → commit.

---

## Task 1 — Rename `isInStorage` → `isInModalSlotUI`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` (declaration)
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs` (set/clear)
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` (read)
- Modify: `Assets/3 - Scripts/Camera/CameraFOVFX.cs` (read)
- Modify: `Assets/3 - Scripts/Camera/CameraTransformFX.cs` (read)

Pure mechanical rename — semantics don't change (any modal slot UI dims camera FX and gates hotbar input).

- [ ] **Step 1: Find the declaration**

```bash
grep -n "isInStorage" "Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs"
```

You'll find a `public static bool isInStorage;` (or similar). Rename to `isInModalSlotUI`. Update the inline comment if there is one to read "set by any modal slot UI (StorageUI, FishStagingUI, ...)".

- [ ] **Step 2: Rename all callsites**

In each of the 4 caller files, replace every `PlayerController.isInStorage` with `PlayerController.isInModalSlotUI`. Use a sed-style replace if you have many in one file:

```bash
grep -rln "isInStorage" "Assets/3 - Scripts/" | xargs -I {} sed -i 's/isInStorage/isInModalSlotUI/g' {}
```

(Or do per-file Edit replacements in your editor.)

- [ ] **Step 3: Verify no callsites remain**

```bash
grep -rn "isInStorage" "Assets/3 - Scripts/"
```

Expected: zero matches.

- [ ] **Step 4: Compile check**

`mcp__coplay-mcp__check_compile_errors` — clean.

- [ ] **Step 5: Commit**

```bash
git add -u
git commit -m "refactor: rename PlayerController.isInStorage -> isInModalSlotUI

Phase 4 prep. The flag gates camera FX dimming + hotbar input
suppression when any modal slot UI is open; it'll cover both
StorageUI (existing) and FishStagingUI (Phase 4 new). Pure
mechanical rename, no semantic change."
```

---

## Task 2 — Add `FishSource` struct to `SlotOps.cs`

**Files:**
- Modify: `Assets/3 - Scripts/UI/SlotOps.cs` (add struct near the top of the file, alongside `CursorState`)

- [ ] **Step 1: Add the struct**

After the closing `}` of `CursorState` (around line 18), insert:

```csharp
    // Phase 4: tracks where a staged fish originally came from so the
    // picker / cook / sell stage lists can return it to its exact slot
    // if the player cancels. container holds a live reference to the
    // source's Slot array (hotbar / a bag's bagContents / a LootBox's
    // slots); index is the slot position within that array.
    public struct FishSource
    {
        public Hotbar.Slot[] container;
        public int index;
        public bool IsValid => container != null && index >= 0 && index < container.Length;
    }
```

- [ ] **Step 2: Compile check**

Clean.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/SlotOps.cs"
git commit -m "feat(slots): add FishSource struct for Phase 4 source tracking

Carries (container, index) so a staged fish remembers where it was
dragged from. The picker (FishStagingUI) and the cook/sell stage
lists both use it to return-to-source on cancel."
```

---

## Task 3 — Create `FishStagingUI.cs`

**Files:**
- Create: `Assets/3 - Scripts/UI/FishStagingUI.cs`

This is the largest single task. The file mirrors `StorageUI.cs`'s shape — same canvas / scaler / cursor follower / SlotOps drag-drop / bag side panel pattern, with 10 stage slots instead of 5×4 storage grid + Open/Confirm/Cancel callback API.

- [ ] **Step 1: Read the existing `StorageUI.cs` end-to-end**

Before writing the new file, skim `Assets/3 - Scripts/UI/StorageUI.cs` to understand the procedural Canvas build, slot view structure, cursor follower, RMB-on-FishBag intercept, and bag side panel. The new file uses the same `NewRT` / `Stretch` / `AddOutline` helpers (re-declared locally since they're private statics in StorageUI — duplication acknowledged in Phase 4 spec; refactor candidate for Phase 5).

- [ ] **Step 2: Write the full file**

Create `Assets/3 - Scripts/UI/FishStagingUI.cs` with:

```csharp
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
    }

    class StagingSlotClick : MonoBehaviour, IPointerClickHandler
    {
        public FishStagingUI owner;
        public SlotView view;
        public void OnPointerClick(PointerEventData e) { if (owner != null) owner.OnSlotClicked(view, e); }
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
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Hotbar.Instance != null) Hotbar.Instance.OnStorageOpened();   // reuse: same intent
        if (_titleText != null) _titleText.text = title ?? "STAGE FISH";
        ShowCanvas();
        RefreshAll();
        RefreshCursorVisual();
    }

    public void RequestClose()
    {
        if (!IsOpen) return;
        // Same return-to-source path Cancel uses.
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
        // Also flush held cursor (defensive — shouldn't be held when clicking Confirm).
        if (_cursor.IsHeld) SlotOps.ReturnHeldToSource(ref _cursor);
        var cb = _onConfirm; _onConfirm = null;
        Close();
        cb?.Invoke(list);
    }

    void OnCancelClicked()
    {
        // Return any cursor-held item first.
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
    public static bool TryReturnTo(FishEntry fe, FishSource src)
    {
        if (fe == null) return true;
        // 1. Exact source slot if still empty
        if (src.IsValid && src.container[src.index].id == Hotbar.ItemId.None)
        {
            src.container[src.index] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = fe };
            return true;
        }
        // 2. Next-empty in source container
        if (src.IsValid)
        {
            for (int i = 0; i < src.container.Length; i++)
            {
                if (src.container[i].id != Hotbar.ItemId.None) continue;
                src.container[i] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = fe };
                return true;
            }
        }
        // 3. Bag
        if (Hotbar.Instance != null && Hotbar.Instance.TryAddFishToBag(fe)) return true;
        // 4. Hotbar
        if (Hotbar.Instance != null && Hotbar.Instance.TryAddFish(fe)) return true;
        return false;
    }

    void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        PlayerController.isInModalSlotUI = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (_bagPanel != null) _bagPanel.gameObject.SetActive(false);
        _activeBag = null;
        _cursor = default;
        HideCanvas();
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            OnCancelClicked();
            return;
        }
        if (PlayerController.isInDialogue || PlayerController.isMapOpen
            || PlayerPhoneUI.IsOpen || Ship.FindPilotedShip() != null)
        {
            OnCancelClicked();
            return;
        }
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
        if (view == null || view.container == null) return;
        var hbSlots = Hotbar.Instance != null ? Hotbar.Instance.RawSlotsRef() : null;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

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

        // Snapshot cursor source BEFORE the SlotOps call so we know where
        // the fish came from when we capture _sources[k] for stage slots.
        var srcContainer = _cursor.sourceContainer;
        var srcIndex = _cursor.sourceIndex;

        if (e.button == PointerEventData.InputButton.Left)
        {
            if (shift)
            {
                // Quick-move: to the stage grid (from hotbar/bag) OR back to hotbar (from stage).
                bool fromStage = ReferenceEquals(view.container, _stageSlots);
                var dest = fromStage ? hbSlots : _stageSlots;
                if (dest != null)
                {
                    // Capture source BEFORE the move so we know per-stage-slot origin.
                    var s = view.container[view.index];
                    SlotOps.HandleQuickMove(view.container, view.index, dest);
                    if (!fromStage)
                    {
                        // Find where it landed in stage and record source.
                        for (int k = 0; k < StageSlots; k++)
                        {
                            if (_stageSlots[k].id == Hotbar.ItemId.Fish && _stageSlots[k].fishData == s.fishData)
                            { _sources[k] = new FishSource { container = view.container, index = view.index }; break; }
                        }
                    }
                    else
                    {
                        // Stage → hotbar quick-move: clear sources for slots that emptied.
                        for (int k = 0; k < StageSlots; k++)
                            if (_stageSlots[k].id == Hotbar.ItemId.None) _sources[k] = default;
                    }
                }
            }
            else
            {
                SlotOps.HandleLeftClick(view.container, view.index, ref _cursor);
                CaptureSourceAfterClick(view, srcContainer, srcIndex);
            }
        }
        else if (e.button == PointerEventData.InputButton.Right)
        {
            SlotOps.HandleRightClick(view.container, view.index, ref _cursor);
            CaptureSourceAfterClick(view, srcContainer, srcIndex);
        }

        RefreshAll();
        RefreshCursorVisual();
    }

    // After a LMB/RMB drop into a stage slot, record where the cursor's
    // fish originally came from. If the click happened on a non-stage
    // slot (hotbar mirror or bag panel), nothing to record.
    void CaptureSourceAfterClick(SlotView view, Hotbar.Slot[] cursorSrcContainerBefore, int cursorSrcIndexBefore)
    {
        if (!ReferenceEquals(view.container, _stageSlots)) return;
        // The cursor before the click is what we want — that's where the
        // fish was BEFORE landing in the stage. If the cursor wasn't held
        // (cursorSrcContainerBefore == null), the click was a pickup from
        // stage; clear that source.
        if (cursorSrcContainerBefore == null)
        {
            // Pickup from stage — source data goes onto the cursor, no
            // need to remember it on a now-empty stage slot.
            _sources[view.index] = default;
            return;
        }
        // Drop into stage — capture origin.
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
            _cursorIcon.enabled = false; _cursorIcon.sprite = null;
        }
        else
        {
            var sprite = ResolveIcon(_cursor.id);
            _cursorIcon.enabled = sprite != null;
            _cursorIcon.sprite = sprite;
            if (_cursorFishPreview != null) { _cursorFishPreview.enabled = false; _cursorFishPreview.texture = null; }
        }
        _cursorCount.enabled = false;   // fish are 1-per-slot; no count badge ever shown on stage cursor
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
        bool standardIcon = !empty && !isFish && ResolveIcon(s.id) != null;
        v.itemIcon.enabled = standardIcon;
        v.itemIcon.sprite  = standardIcon ? ResolveIcon(s.id) : null;
        v.itemIcon.color   = Color.white;

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
            { v.fishPreview.enabled = false; v.fishPreview.texture = null; }
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

    static Sprite ResolveIcon(Hotbar.ItemId id)
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

    // ── Procedural UI build ──────────────────────────────────────────

    void BuildCanvas()
    {
        var canvasGo = new GameObject("FishStagingCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 910;   // above StorageUI (900); independent modal
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
        // Top header (30) + stage grid + label (16) + hotbar row (SlotSize) +
        // bottom button row (44) + padding.
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

        // Header
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

        // Stage grid label
        var stageLabel = NewRT("StageLabel", panel);
        stageLabel.anchorMin = new Vector2(0f, 1f);
        stageLabel.anchorMax = new Vector2(1f, 1f);
        stageLabel.pivot = new Vector2(0.5f, 1f);
        stageLabel.sizeDelta = new Vector2(-PanelPad * 2, 16f);
        stageLabel.anchoredPosition = new Vector2(0f, -(PanelPad + 40f));
        var sLbl = NewText(stageLabel, "STAGE  ·  10 SLOTS", TextAlignmentOptions.Center, 10f);
        sLbl.color = CyanScannerPalette.Accent;
        sLbl.characterSpacing = 3f;

        // Stage grid
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

        // Hotbar row pushed up to leave room for button row at the bottom.
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

        // Button row at the bottom
        BuildButtonRow(panel);

        // Cursor follower
        BuildCursorFollower(canvasGo.transform);

        // Bag side panel
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

        BuildButton(row, "CONFIRM  [ ENTER ]", new Vector2(-105f, 0f), OnConfirmClicked, new Color32(0x42, 0x88, 0x55, 0xFF));
        BuildButton(row, "CANCEL  [ ESC ]",    new Vector2( 105f, 0f), OnCancelClicked,  new Color32(0x88, 0x44, 0x44, 0xFF));
    }

    void BuildButton(RectTransform parent, string label, Vector2 anchored, UnityEngine.Events.UnityAction onClick, Color32 fill)
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
    }

    void BuildBagSidePanel(RectTransform mainPanel)
    {
        const float SidePanelW = SlotSize + 24f;
        const float SidePanelH = 5 * SlotSize + 4 * SlotGap + 56f;
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
```

- [ ] **Step 3: Compile check**

Expect clean — file mirrors StorageUI's patterns and references only existing public APIs (`SlotOps`, `Hotbar`, `FishingdexManager`, `HUDSceneGate`, `CyanScannerPalette`, etc.).

- [ ] **Step 4: Behavioral smoke test (no caller yet)**

The picker is auto-created at scene load but `IsOpen = false`. No way to open it yet (callers come in Tasks 4-5). Confirm via Console that there's no error spam from BuildCanvas. The GameObject "FishStagingUI" should exist under DontDestroyOnLoad with its Canvas component present but inactive.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/FishStagingUI.cs"
git commit -m "feat(ui): FishStagingUI singleton — shared cook/sell staging picker

10 stage slots, 7-slot hotbar mirror, 5-slot bag side panel (RMB
toggle, duplicated from StorageUI for safety), cursor follower with
live fish preview, Confirm + Cancel buttons.

Open(title, onConfirm) is the caller-facing API. Confirm gathers
(FishEntry, FishSource) pairs and fires the callback. Cancel returns
each staged fish via TryReturnTo: exact source slot -> next-empty-
in-source-container -> bag -> hotbar -> destroy + InventoryFullPopup.

Cursor source tracking captured at drop time (snapshot
_cursor.sourceContainer / sourceIndex BEFORE the SlotOps call) so
the picker knows where to send each fish back on cancel.

Callers (BonfireInteraction, FishMarketNPC) wire up in subsequent
tasks. PlayerController.isInModalSlotUI flag (renamed in Task 1)
gates hotbar input and camera FX while the picker is open."
```

---

## Task 4 — `BonfireInteraction` migration to picker

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs:241-264`

- [ ] **Step 1: Find the existing Phase 2 OnAddFishClicked / OnFishSelected / OnRemoveFish**

```bash
grep -n "OnAddFishClicked\|OnFishSelected\|OnRemoveFish\|stagedFish" "Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs"
```

Note the field declaration of `stagedFish` (somewhere near the top). Find the cook + return paths.

- [ ] **Step 2: Grow `stagedFish` tuple to carry FishSource**

Find the field declaration. It's currently:
```csharp
List<(FishEntry fish, RenderTexture rt)> stagedFish = new List<(FishEntry fish, RenderTexture rt)>();
```

Replace with:
```csharp
List<(FishEntry fish, RenderTexture rt, FishSource source)> stagedFish = new List<(FishEntry fish, RenderTexture rt, FishSource source)>();
```

- [ ] **Step 3: Replace OnAddFishClicked**

```csharp
    void OnAddFishClicked()
    {
        if (isCooking || foodReady) return;
        if (FishStagingUI.Instance == null) return;
        FishStagingUI.Instance.Open("COOK FISH", entries =>
        {
            foreach (var (fish, source) in entries)
                stagedFish.Add((fish, null, source));
            RefreshUI();
        });
    }
```

- [ ] **Step 4: OnFishSelected**

Delete the old `OnFishSelected(FishEntry, RenderTexture)` method — the picker doesn't use the dex callback shape. If anything else references it, leave it but have it `return;` immediately, or remove the references.

- [ ] **Step 5: Update OnRemoveFish to return-to-source**

Replace:
```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        if (Hotbar.Instance != null && !Hotbar.Instance.TryAddFish(f))
        {
            InventoryFullPopup.Show();
        }
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }
```

with:
```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt, src) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        // Phase 4: return to original source first; fall back through bag/hotbar.
        if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show();
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }
```

- [ ] **Step 6: Bonfire close-with-staged-fish path**

Find wherever the bonfire panel closes (probably an `OnPanelClose` or similar). If staged fish exist, return each one via `FishStagingUI.TryReturnTo`. Pseudocode pattern:
```csharp
    void OnPanelClose()
    {
        // Return any staged fish to source.
        for (int i = stagedFish.Count - 1; i >= 0; i--)
        {
            var (f, rt, src) = stagedFish[i];
            if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show();
            if (rt != null) ReleaseRT(rt);
        }
        stagedFish.Clear();
        // ... existing close logic
    }
```

Locate the actual close method by grep and add the return-to-source loop before its existing teardown.

- [ ] **Step 7: Compile check**

Clean.

- [ ] **Step 8: Behavioral check**

Place a bonfire, open cook panel, click Add Fish. The picker opens. Drag a fish from hotbar to stage slot, click Confirm. Picker closes. The fish appears in the cook scroll list. Click Cook → eat → hunger up.

Then test cancel paths from acceptance criteria §6.

- [ ] **Step 9: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs"
git commit -m "feat(cook): migrate Add Fish to FishStagingUI picker

OnAddFishClicked opens FishStagingUI.Instance.Open with a callback
that drains the picker's staged entries into the local stagedFish
list. stagedFish tuple grows by one field (FishSource) so cancel
paths can return-to-exact-source via FishStagingUI.TryReturnTo.

Replaces the Phase 2 placeholder that took the first hotbar fish
of any tier."
```

---

## Task 5 — `FishMarketNPC` migration to picker

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishMarketNPC.cs:306-327`

Mirror of Task 4. Same shape.

- [ ] **Step 1: Grow stagedFish tuple**

```csharp
List<(FishEntry fish, RenderTexture rt, FishSource source)> stagedFish = new List<(FishEntry fish, RenderTexture rt, FishSource source)>();
```

- [ ] **Step 2: Replace OnAddFishClicked**

```csharp
    void OnAddFishClicked()
    {
        if (FishStagingUI.Instance == null) return;
        FishStagingUI.Instance.Open("SELL FISH", entries =>
        {
            foreach (var (fish, source) in entries)
                stagedFish.Add((fish, null, source));
            RefreshUI();
        });
    }
```

- [ ] **Step 3: Update OnRemoveFish**

Same shape as cook — destructure 3-tuple, use `FishStagingUI.TryReturnTo`:
```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt, src) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        if (!FishStagingUI.TryReturnTo(f, src)) InventoryFullPopup.Show();
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }
```

- [ ] **Step 4: Sell-panel-close return-to-source**

Find the close path and add the same return loop pattern as Task 4 step 6.

- [ ] **Step 5: Compile check**

Clean.

- [ ] **Step 6: Behavioral check**

Walk to FishMarketNPC, open sell, click Add Fish → picker → stage → Confirm → money preview updates → Confirm Sale → money up.

- [ ] **Step 7: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishMarketNPC.cs"
git commit -m "feat(sell): migrate Add Fish to FishStagingUI picker

Mirror of cook migration. Sell panel uses the shared picker; cancel
paths return-to-source via FishStagingUI.TryReturnTo."
```

---

## Task 6 — Manual regression pass

**Files:** None (verification).

Run all 10 acceptance scenarios from spec §6. STOP on any failure.

- [ ] **Step 1-10:** see spec §6 — cook picker opens, drag flows, bag side panel, confirm cook, cancel mid-stage, bonfire-close-returns, sell flow, source-occupied edge, all-full destroy edge.

---

## Task 7 — Wrap-up: CLAUDE.md + tag

- [ ] **Step 1: Update CLAUDE.md**

Add a paragraph to the Cook/Sell or Fish flow section noting the picker:

> **Cook/sell staging picker (Phase 4):** `FishStagingUI` (singleton, auto-created) is the drag-and-drop picker `BonfireInteraction` and `FishMarketNPC` use to stage fish. `Open(title, onConfirm)` opens the 10-slot picker; Confirm fires `onConfirm(List<(FishEntry, FishSource)>)` and closes. Cancel returns each staged fish via `FishStagingUI.TryReturnTo` (exact source slot → next-empty-in-source-container → bag → hotbar → destroy). Cook/sell stage lists carry `FishSource` per entry so their own cancel paths use the same return chain.

Also rename references to `PlayerController.isInStorage` in the docs to `isInModalSlotUI`.

- [ ] **Step 2: Tag**

```bash
git add CLAUDE.md
git commit -m "docs: CLAUDE.md notes for Phase 4 picker"
git tag fish-revamp-phase-4-complete
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 3: Hand off**

> Fish/storage revamp complete. 4 tagged phases on `feature/fish-storage-revamp`. Ready to PR / merge / brainstorm cleanup.

---

## Self-Review

**Spec coverage:**
- ✓ Shared picker → Task 3 (FishStagingUI singleton)
- ✓ 10 stage slots → Task 3 (StageSlots const = 10)
- ✓ Drag from hotbar/bag → Task 3 (hotbar mirror + bag side panel)
- ✓ Confirm callback → Task 3 (OnConfirmClicked + Open API)
- ✓ Cancel return-to-source → Task 3 (OnCancelClicked + TryReturnTo)
- ✓ FishSource struct → Task 2
- ✓ Cook migration → Task 4
- ✓ Sell migration → Task 5
- ✓ isInStorage rename → Task 1
- ✓ All acceptance criteria covered → Task 6

**Placeholder scan:** No TBDs. Task 4 step 6 says "find the actual close method by grep" — acceptable because the implementer can locate it; the pattern code is given. Task 5 step 4 same.

**Type consistency:**
- `FishSource` (Task 2) ↔ used in Tasks 3 (FishStagingUI + TryReturnTo), 4 (cook stagedFish tuple), 5 (sell stagedFish tuple) ✓
- `FishStagingUI.Open(string, Action<List<(FishEntry, FishSource)>>)` (Task 3) ↔ called from Tasks 4 + 5 ✓
- `FishStagingUI.TryReturnTo(FishEntry, FishSource) → bool` (Task 3) ↔ called from Tasks 4 + 5 ✓
- `PlayerController.isInModalSlotUI` (Task 1) ↔ set/clear in Task 3 (FishStagingUI Open/Close) ✓

**Scope:** 7 tasks, FishStagingUI is the bulk. Single PR.

No fixes needed.
