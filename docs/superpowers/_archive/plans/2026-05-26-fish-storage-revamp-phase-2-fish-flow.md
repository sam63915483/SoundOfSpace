# Fish & Storage Revamp — Phase 2 (Fish Flow + Hold-to-Eat + Dex Revamp) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Caught fish route into the hotbar instead of `FishInventory`. Hold LMB 1s on the equipped Fish slot to eat (raw hunger + kaleidoscope trip). Fishingdex becomes read-only lifetime log (eat-raw button removed). Cook + sell panels migrate to pull from hotbar. Old saves migrate one-shot.

**Architecture:** Additive on top of Phase 1's slot model. `FishInventory` shifts role to lifetime dex log (append-only). Hotbar gets `TryAddFish` / `CountFishByTier` / `TakeFirstFishOfTier` helpers + a hold-LMB-eat tick in `Update` + a `progressRing` Image per slot view. Cook + sell flows no longer open the dex picker — `Add Fish` button takes the first fish from the hotbar of any tier (a minimal migration; Phase 4's drag-and-drop sell UI restores tier-specific selection).

**Tech Stack:** Unity 2022.3, C#, default `Assembly-CSharp`. `JsonUtility` saves. No test framework — manual Editor regression.

---

## File Structure

| File | Responsibility | Changes |
|---|---|---|
| `Assets/3 - Scripts/Fishing/FishInventory.cs` | Lifetime dex log + per-tier raw consumption helper | `AddFish` returns the new `FishEntry`; add `RawFishConsumption` static class with tier→raw values + trip params |
| `Assets/3 - Scripts/UI/Hotbar.cs` | 7-slot inventory + hold-LMB-eat + progress ring | `TryAddFish` / `CountFishByTier` / `TakeFirstFishOfTier` helpers; `_eatProgressSlot` / `_eatHeldSeconds` state; `TickEatHold` + `ConsumeEquippedFish`; `SlotVisuals.progressRing` field + builder + Refresh paint |
| `Assets/3 - Scripts/Fishing/Bobber.cs` | Catch flow | Replace direct `AddFish(...)` with: log to dex + `Hotbar.TryAddFish(entry)` + `InventoryFullPopup.Show()` on destroy |
| `Assets/3 - Scripts/Fishing/FishingdexManager.cs` | Read-only lifetime catch log | Delete eat-raw button + `OnEatRaw` method |
| `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs` | Cook panel | `OnAddFishClicked` takes first fish from hotbar (any tier); `OnRemoveFish` returns to hotbar; `OnFishSelected(entry, null)` signature retained for cancel-path |
| `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` | Sell panel | Same shape as BonfireInteraction |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | Schema | `bool migratedToHotbar` field on `FishInventorySave` |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Save migration | `MigrateFishInventoryToHotbar` + `TrySpillToStorage` helpers; one-shot call at end of `ApplyFishInventory` |

13 atomic tasks total. Each lands as a focused commit.

---

## Verification Strategy

Same as Phase 1: Unity has no test framework here. Per-task verification is:
1. **Code change** with full code shown
2. **Compile check** via `mcp__coplay-mcp__check_compile_errors` (or wait for Unity Console)
3. **Behavioral check** when meaningful (e.g. "catch a fish, confirm it lands in hotbar slot N")
4. **Commit** per task

---

## Task 1 — `FishInventory.AddFish` returns the new `FishEntry`

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishInventory.cs:62-66`

The catch flow needs the freshly-constructed `FishEntry` to also push into the hotbar. Returning it from `AddFish` is cleaner than peeking the list tail at the callsite.

- [ ] **Step 1: Change the method signature**

Replace lines 62-66:

```csharp
    public void AddFish(string fishType, int weightLbs)
    {
        fish.Add(new FishEntry(fishType, weightLbs));
        Debug.Log($"[FishInventory] Added {weightLbs}lb {fishType}. Total fish: {fish.Count}");
    }
```

with:

```csharp
    // Returns the newly-added FishEntry so the catch flow can also push it
    // into the hotbar without peeking `AllFish` tail. This list IS the
    // lifetime dex log post-Phase 2 — the entry stays here even after
    // it's been consumed/sold from the hotbar.
    public FishEntry AddFish(string fishType, int weightLbs)
    {
        var entry = new FishEntry(fishType, weightLbs);
        fish.Add(entry);
        Debug.Log($"[FishInventory] Added {weightLbs}lb {fishType}. Total fish: {fish.Count}");
        return entry;
    }
```

- [ ] **Step 2: Compile check**

Open Unity, watch Console. The existing callers (`Bobber.cs:263` and any others) pass-by-value still work — the new return value is just optional for old callers. Should be a clean compile.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishInventory.cs"
git commit -m "feat(fishing): AddFish returns the new FishEntry

Catch flow needs the entry to push into both the dex log AND the
hotbar (Phase 2). Returning it from AddFish is cleaner than peeking
the list tail at the callsite. All existing callers ignore the
return value; no callsite changes needed yet."
```

---

## Task 2 — Add `RawFishConsumption` static helper to `FishInventory.cs`

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishInventory.cs` (add new class at bottom of file)

Extract the per-tier values from `FishingdexManager.OnEatRaw:336-345` so the new hotbar-eat path uses one source of truth.

- [ ] **Step 1: Append the helper class**

Add this at the bottom of `FishInventory.cs`, after the closing brace of the `FishInventory` class:

```csharp

// Shared raw-eat consumption — same per-tier values FishingdexManager.OnEatRaw
// uses today. Phase 2's hotbar hold-LMB-eat path calls this; Phase 2 also
// removes OnEatRaw from the dex, so this becomes the single source of truth.
public static class RawFishConsumption
{
    public static void Consume(string tier)
    {
        // Per-tier table: cooked hunger value + 5 trip-effect params from
        // FishingdexManager.OnEatRaw. Raw hunger is cooked * 0.5f.
        (float cooked, float ek, float ew, float ed, float lk, float lw) = tier switch
        {
            "Rare"     => (60f, 0f, 1f,  5f, 1.0f, 0f),
            "Uncommon" => (35f, 0f, 1f, 10f, 0.4f, 0f),
            _          => (20f, 0f, 1f, 30f, 0f,   1f),   // Common (fallback)
        };
        ResourceManager.Instance?.ConsumeFood(cooked * 0.5f);
        RawFishTripController.StartTrip(30f, ek, ew, ed, lk, lw);
    }
}
```

- [ ] **Step 2: Compile check**

Console must show no errors. `ResourceManager` and `RawFishTripController` are in the global namespace — no `using` needed.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishInventory.cs"
git commit -m "feat(fishing): RawFishConsumption helper extracted from OnEatRaw

Per-tier raw hunger + trip params lifted from
FishingdexManager.OnEatRaw:336-345 verbatim. Phase 2 hotbar
hold-LMB-eat will call RawFishConsumption.Consume(tier); the dex
eat-raw path gets removed in a later task.

Values: Rare 30 raw hunger, Uncommon 17.5, Common 10. Trip duration
30s. Per-tier kaleidoscope intensities unchanged."
```

---

## Task 3 — `Hotbar.TryAddFish` / `CountFishByTier` / `TakeFirstFishOfTier`

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` (add three public methods near the other slot helpers, e.g. after `SpendResource`)

- [ ] **Step 1: Add the three methods**

Find the existing `SpendResource` method in `Hotbar.cs` (around line 233). After its closing brace and before `SetResourceTotal` (around line 252), insert:

```csharp
    // ── Phase 2: Fish slot helpers ───────────────────────────────────
    // Used by Bobber (catch flow), BonfireInteraction (cook stage),
    // FishMarketNPC (sell stage), and SaveCollector (old-save migration).

    // Try to place a fish in the first empty hotbar slot. Returns true on
    // success, false if every slot is occupied. Caller decides whether to
    // destroy (and pop InventoryFullPopup) or spill elsewhere.
    public bool TryAddFish(FishEntry entry)
    {
        if (entry == null) return false;
        for (int i = 0; i < NumSlots; i++)
        {
            if (slots[i].id != ItemId.None) continue;
            slots[i] = new Slot { id = ItemId.Fish, count = 1, fishData = entry };
            OnResourceChanged?.Invoke(ItemId.Fish);
            return true;
        }
        return false;
    }

    // Count fish of a given tier across the hotbar. Cook + sell tier-counter
    // UIs read this to show "Common: N" totals.
    public int CountFishByTier(string tier)
    {
        int n = 0;
        for (int i = 0; i < NumSlots; i++)
        {
            var s = slots[i];
            if (s.id == ItemId.Fish && s.fishData != null && s.fishData.fishType == tier) n++;
        }
        return n;
    }

    // Stage-add for cook/sell: find the first fish of the given tier, return
    // its FishEntry, and empty the source slot. Returns null if no match.
    // Pass tier == null or empty to take the first fish of ANY tier (used
    // by the simplified Phase 2 "Add Fish" buttons until Phase 4 brings
    // the drag-and-drop picker).
    public FishEntry TakeFirstFishOfTier(string tier)
    {
        for (int i = 0; i < NumSlots; i++)
        {
            var s = slots[i];
            if (s.id != ItemId.Fish || s.fishData == null) continue;
            if (!string.IsNullOrEmpty(tier) && s.fishData.fishType != tier) continue;
            var entry = s.fishData;
            slots[i] = default;
            OnResourceChanged?.Invoke(ItemId.Fish);
            return entry;
        }
        return null;
    }
```

- [ ] **Step 2: Compile check**

Console clean.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): TryAddFish + CountFishByTier + TakeFirstFishOfTier

Three helpers Phase 2 consumers need:
- TryAddFish: catch routing (Bobber) and save migration both place
  fish in the next empty slot. Returns false when hotbar is full.
- CountFishByTier: cook + sell tier-counter UIs read totals from
  the hotbar instead of FishInventory.
- TakeFirstFishOfTier: stage-add primitive for cook + sell. Null
  tier means 'any tier' — the simplified Phase 2 'Add Fish' button.

All three fire OnResourceChanged so existing UI bindings repaint."
```

---

## Task 4 — `Bobber.cs` catch flow

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/Bobber.cs:263` (the `FishInventory.Instance.AddFish` call)

- [ ] **Step 1: Read the surrounding context first**

Run:
```bash
sed -n '258,270p' "Assets/3 - Scripts/Fishing/Bobber.cs"
```

You'll see the catch handler that calls `FishInventory.Instance.AddFish(currentFishType, weight)`. Confirm the line is at 263 (it may shift by ±1).

- [ ] **Step 2: Replace the AddFish call**

Find the line:
```csharp
            FishInventory.Instance.AddFish(currentFishType, weight);
```

Replace with:
```csharp
            // Log to lifetime dex first (every fish always gets recorded).
            var entry = FishInventory.Instance.AddFish(currentFishType, weight);
            // Then try to place in hotbar. If full, show inventory-full popup
            // and discard — the dex still has the entry so it's not "lost",
            // just not in-hand.
            if (Hotbar.Instance == null || !Hotbar.Instance.TryAddFish(entry))
            {
                InventoryFullPopup.Show();
            }
```

- [ ] **Step 3: Compile check**

Console clean.

- [ ] **Step 4: Behavioral check — catch routes to hotbar**

Open Unity, Play `1.6.7.7.7.unity`. Equip the rod (need NPC Alien3 to unlock — or use a save where it's unlocked). Cast at the fishing bank, catch a fish.

Expected:
- Hotbar's next empty slot fills with a colored fish icon (using Phase 1's placeholder crystal-shape with `fishColor` tint and weight badge).
- Press `J` to open the dex — fish appears as a card.

- [ ] **Step 5: Behavioral check — hotbar full destroys**

Fill the hotbar (equippables + 7 fish, or 5 fish + 2 equippables). Catch one more.

Expected:
- A small "Inventory Full" pill flashes above the hotbar (the existing `InventoryFullPopup`).
- Dex shows the new fish anyway.
- Hotbar doesn't change.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/Bobber.cs"
git commit -m "feat(fishing): catch routes to hotbar; dex logs every catch

Catch flow now:
  1. FishInventory.AddFish(type, weight) -> lifetime dex log
  2. Hotbar.TryAddFish(entry) -> next empty slot, or false if full
  3. If false: InventoryFullPopup.Show() and discard
     (dex still has the entry; it's a UX hint, not a data loss)

Verified in editor: caught fish appear in hotbar with fishColor
tint and weight badge; full-hotbar catches flash the popup."
```

---

## Task 5 — Hold-LMB eat in `Hotbar.cs` (state + Update tick)

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` (add new state fields near top; add `TickEatHold` + `ConsumeEquippedFish`; call them from `Update`)

- [ ] **Step 1: Add state fields**

Find the existing slot-related state in `Hotbar.cs` (e.g. `int _animatedActiveIdx = -1;` around line 67). After it, add:

```csharp
    // Phase 2: hold-LMB-eat state. _eatProgressSlot is the slot index the
    // player is currently holding LMB on (must be the equipped Fish slot).
    // _eatHeldSeconds counts up while held; consumption fires at EatHoldDuration.
    int _eatProgressSlot = -1;
    float _eatHeldSeconds = 0f;
    const float EatHoldDuration = 1.0f;
```

- [ ] **Step 2: Add the two new methods**

Find the `HandleInput` method (around line 446). After `HandleInput` closes and before `CycleSlot`, insert:

```csharp
    // Phase 2: tick once per Update when the player is holding LMB on the
    // equipped Fish slot. Releases the click or switching slots resets.
    void TickEatHold()
    {
        int eq = _equippedSlot;
        bool fishEquipped = eq >= 0 && eq < NumSlots
                         && slots[eq].id == ItemId.Fish
                         && slots[eq].fishData != null;
        if (!fishEquipped || !Input.GetMouseButton(0))
        {
            if (_eatProgressSlot != -1) { _eatProgressSlot = -1; _eatHeldSeconds = 0f; }
            return;
        }

        if (_eatProgressSlot != eq) { _eatProgressSlot = eq; _eatHeldSeconds = 0f; }
        _eatHeldSeconds += Time.deltaTime;

        if (_eatHeldSeconds >= EatHoldDuration)
        {
            ConsumeEquippedFish();
            _eatProgressSlot = -1;
            _eatHeldSeconds = 0f;
        }
    }

    void ConsumeEquippedFish()
    {
        int eq = _equippedSlot;
        if (eq < 0 || eq >= NumSlots) return;
        var slot = slots[eq];
        if (slot.id != ItemId.Fish || slot.fishData == null) return;

        RawFishConsumption.Consume(slot.fishData.fishType);
        slots[eq] = default;
        OnResourceChanged?.Invoke(ItemId.Fish);
    }
```

- [ ] **Step 3: Wire `TickEatHold` into `Update`**

Find the `Update` method (around line 103). Locate the gating block:

```csharp
        if (!piloting && !inDialogue && !phoneOpen && !PlayerController.isMapOpen) HandleInput();
        Refresh(piloting || inDialogue || phoneOpen);
```

Replace with:

```csharp
        if (!piloting && !inDialogue && !phoneOpen && !PlayerController.isMapOpen && !PlayerController.isInStorage)
        {
            HandleInput();
            TickEatHold();
        }
        else
        {
            // Any input gate active resets the hold timer so reopening doesn't
            // resume a stale progress ring.
            if (_eatProgressSlot != -1) { _eatProgressSlot = -1; _eatHeldSeconds = 0f; }
        }
        Refresh(piloting || inDialogue || phoneOpen);
```

(The added `PlayerController.isInStorage` check prevents eat-hold while the storage panel is open — the player is dragging slots, not eating.)

- [ ] **Step 4: Compile check**

Console clean. The `RawFishConsumption.Consume` call resolves to the static class added in Task 2.

- [ ] **Step 5: Behavioral check — hold eats fish**

In Play mode with a fish in hotbar (catch one via Task 4 flow or use a save with fish in hotbar after Phase 2 migration). Press the slot's number key to equip it (no visual prop in hand — Phase 2 keeps the no-hand-mesh design). Hold LMB.

Expected (without the progress ring yet, which is Task 6):
- After 1.0s of continuous LMB: slot empties, hunger HUD jumps up, kaleidoscope ramps in.
- Release before 1.0s: nothing happens.
- Re-press LMB after release: starts counting from 0 again.

If the slot doesn't empty after 1s, check Console for missing references (RawFishConsumption, ResourceManager).

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): hold-LMB 1s eats the equipped fish

TickEatHold polls Input.GetMouseButton(0) every Update; when LMB is
held with an equipped Fish slot, increments _eatHeldSeconds. At 1.0s
ConsumeEquippedFish fires — RawFishConsumption.Consume restores
hunger and starts the trip, the slot empties, OnResourceChanged
fires to repaint the HUD.

Gates: piloting / dialogue / phone / map / isInStorage all reset the
hold timer so reopening the input doesn't resume a stale progress.

Progress ring visual lands in the next task."
```

---

## Task 6 — Progress ring on `SlotVisuals`

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` (add `progressRing` field to `SlotVisuals`; build it in the slot builder; paint it in `Refresh`)

- [ ] **Step 1: Add the field to `SlotVisuals`**

Find the `SlotVisuals` inner class (around line 79). Replace it with:

```csharp
    class SlotVisuals
    {
        public RectTransform root;
        public Image glow;
        public Image border;
        public Image background;
        public Image accent;
        public Image itemIcon;
        public TextMeshProUGUI countText;
        // Phase 2: hold-LMB-eat progress ring. Image.type = Filled,
        // fillMethod = Radial360. fillAmount = 0..1, enabled only when
        // this slot is _eatProgressSlot.
        public Image progressRing;
    }
```

- [ ] **Step 2: Find the slot-view builder**

Run:
```bash
grep -n "BuildSlot\|new SlotVisuals" "Assets/3 - Scripts/UI/Hotbar.cs"
```

You're looking for the procedural function that constructs a `SlotVisuals` and parents its `Image`s. It's likely named `BuildSlot(int idx)` or similar. Read 40 lines around that function to see the existing pattern (how `itemIcon` is built — sprite, parent, anchors).

- [ ] **Step 3: Add the ring child in the builder**

After the existing block that builds `itemIcon` (a child Image of the slot root), add an additional child Image for the progress ring. Use the same anchor/pivot/sizeDelta pattern as `border` (it overlays the whole slot rectangle). Use `HotbarRoundedRing.GetSprite()` if available (already used for slot borders elsewhere in the project), with `Image.type = Filled`, `fillMethod = Radial360`, `fillOrigin = 2` (Top, so the ring fills clockwise starting from 12 o'clock), `fillClockwise = true`, `fillAmount = 0f`, color `CyanScannerPalette.Accent` (cyan), `raycastTarget = false`. Initialize `enabled = false`.

Example pattern (adjust to match the file's actual builder shape — copy the `border` build block and modify):

```csharp
            // Progress ring overlay (Phase 2 hold-LMB-eat indicator)
            var ringGo = new GameObject("ProgressRing", typeof(RectTransform));
            ringGo.transform.SetParent(sv.root, false);
            var ringRT = ringGo.GetComponent<RectTransform>();
            ringRT.anchorMin = Vector2.zero;
            ringRT.anchorMax = Vector2.one;
            ringRT.sizeDelta = Vector2.zero;
            sv.progressRing = ringGo.AddComponent<Image>();
            sv.progressRing.sprite = HotbarRoundedRing.GetSprite();
            sv.progressRing.type = Image.Type.Filled;
            sv.progressRing.fillMethod = Image.FillMethod.Radial360;
            sv.progressRing.fillOrigin = (int)Image.Origin360.Top;
            sv.progressRing.fillClockwise = true;
            sv.progressRing.fillAmount = 0f;
            sv.progressRing.color = CyanScannerPalette.Accent;
            sv.progressRing.raycastTarget = false;
            sv.progressRing.enabled = false;
```

If `HotbarRoundedRing.GetSprite()` doesn't exist or fails (Phase 2 didn't add it), substitute the slot's existing border sprite or use Unity's built-in `UISprite`. Plan-writing flagged this as a likely-OK reuse but verify during the task.

- [ ] **Step 4: Paint the ring in `Refresh`**

Find the `Refresh(bool dimmed)` method (around line 593). It iterates per slot; find the loop that updates `glow`/`itemIcon`/`countText` per slot. After those, in the same loop, add:

```csharp
            // Progress ring: shown only on the slot currently being held.
            if (sv.progressRing != null)
            {
                bool ringActive = (i == _eatProgressSlot);
                sv.progressRing.enabled = ringActive;
                sv.progressRing.fillAmount = ringActive
                    ? Mathf.Clamp01(_eatHeldSeconds / EatHoldDuration)
                    : 0f;
            }
```

(`i` is the loop index; `sv` is the per-slot `SlotVisuals`. Adjust variable names if the loop uses different ones.)

- [ ] **Step 5: Compile check + behavioral check**

Console clean. Then in Play mode, with a fish slot equipped, hold LMB.

Expected:
- A cyan ring overlay fades in around the slot icon.
- The ring fills clockwise from 12 o'clock over 1.0s.
- At 100% fill, the slot empties (fish consumed) and the ring resets to 0% (and re-disables).
- Releasing LMB mid-fill: ring immediately disappears (no fade).

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/UI/Hotbar.cs"
git commit -m "feat(hotbar): progress ring overlay for hold-LMB-eat

New SlotVisuals.progressRing Image — sliced rounded-ring sprite,
Image.type = Filled, fillMethod = Radial360, fillClockwise = true.
Per-frame paint in Refresh sets fillAmount = _eatHeldSeconds /
EatHoldDuration on the slot at _eatProgressSlot, disabled on every
other slot. Ring is the same cyan as the scanner accent palette so
it fits the existing HUD aesthetic."
```

---

## Task 7 — Remove eat-raw button from `FishingdexManager.cs`

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishingdexManager.cs:320-347` (the eat-raw button click handler + `OnEatRaw` method)

The button itself is built somewhere in the procedural dex UI builder. Find it via grep.

- [ ] **Step 1: Find the eat-raw button declaration**

Run:
```bash
grep -n "EatRaw\|Eat Raw\|eatBtn\|eatRaw" "Assets/3 - Scripts/Fishing/FishingdexManager.cs"
```

Identify:
- The button creation site (likely something like `MkButton(..., "Eat Raw", ...)` or `Build...Btn(...)`).
- The `.onClick.AddListener(OnEatRaw)` or `OnDetailEat` wiring.
- The click handler around line 320 (already known from spec) that branches to `OnEatRaw` when `currentMode == FishingdexMode.Browse`.

- [ ] **Step 2: Remove the eat-raw button creation**

Delete the lines that build the button and wire its click handler. If the button is referenced as a field on the class (e.g. `Button eatRawBtn`), delete the field declaration too.

- [ ] **Step 3: Remove `OnEatRaw` and update the click-routing**

Delete the `OnEatRaw()` method (lines 333-347).

In the click-routing block at lines 320-331:

```csharp
        if (currentDetailEntry == null) return;
        if (currentMode == FishingdexMode.Browse) OnEatRaw();
        else
        {
            // ... cook/sell branch
        }
```

Replace with — since the only Browse-mode action was eat-raw, the entire condition can be replaced with: dex Browse mode no longer has a confirm action. Whatever block this code lives in (probably a detail-panel "Confirm" button handler), the Browse-mode branch should now early-return:

```csharp
        if (currentDetailEntry == null) return;
        if (currentMode == FishingdexMode.Browse) return;   // Browse is read-only post-Phase 2
        // ... cook/sell branch unchanged
```

- [ ] **Step 4: Compile check**

Console clean. Any remaining references to `OnEatRaw` (other than the ones we just edited) would surface here. Grep one more time to be sure:

```bash
grep -rn "OnEatRaw" "Assets/3 - Scripts/"
```

Expected: zero matches.

- [ ] **Step 5: Behavioral check — dex is read-only**

Open Unity, Play. Press `J` to open the dex (or trigger it via the existing path). Browse-mode dex:
- No "Eat Raw" button anywhere on the detail panel.
- Fish cards still show preview camera + weight + tier + color swatch.
- Clicking a fish card still opens the detail panel.
- Clicking the detail panel's confirm/eat-area does nothing (or returns to grid).

If a cook or sell flow is mid-flight (`currentMode != Browse`), the dex still opens with a "Select" / confirm action — that path is untouched in this task and gets removed in Tasks 8/9.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishingdexManager.cs"
git commit -m "feat(fishing): dex is read-only — eat-raw button removed

Phase 2: the fishingdex becomes a lifetime catch log. Players eat
fish from the hotbar (hold LMB) instead of from the dex's detail
panel. OnEatRaw deleted; its raw-hunger + trip-params live in
RawFishConsumption.Consume (extracted in Task 2). Browse-mode
confirm action is now a no-op."
```

---

## Task 8 — `BonfireInteraction` cook panel pulls from hotbar

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs:241-264` (the cook-stage callbacks)

The current flow: `OnAddFishClicked` opens the fishingdex as a picker. With Phase 2 the dex is read-only — cook needs its own pull path.

For Phase 2 the simplest migration is: clicking "Add Fish" pulls the **first fish of any tier** from the hotbar and stages it. The cook UI's scroll list already displays per-fish tier/weight rows, so loss of player choice is minor; Phase 4's drag-and-drop polish brings back tier-specific picking.

- [ ] **Step 1: Replace `OnAddFishClicked`**

Find lines 241-245:

```csharp
    void OnAddFishClicked()
    {
        if (isCooking || foodReady) return;
        FishingdexManager.Instance?.OpenForCook(OnFishSelected, cookPanel);
    }
```

Replace with:

```csharp
    void OnAddFishClicked()
    {
        if (isCooking || foodReady) return;
        // Phase 2: pull the first fish (any tier) directly from the hotbar.
        // Phase 4's drag-and-drop sell UI will replace this with a picker.
        var entry = Hotbar.Instance?.TakeFirstFishOfTier(null);
        if (entry == null) return;   // hotbar has no fish — nothing staged
        OnFishSelected(entry, null);
    }
```

- [ ] **Step 2: Make `OnFishSelected` defensive about null RenderTexture**

The dex-picker passed a preview `RenderTexture` for the scroll-list thumbnail. The new hotbar path passes `null`. Make the scroll-row builder tolerate null.

Find the existing `OnFishSelected` (line 247):

```csharp
    void OnFishSelected(FishEntry entry, RenderTexture rt)
    {
        if (isCooking || foodReady) return;
        FishInventory.Instance?.RemoveSpecificFish(entry);
        stagedFish.Add((entry, rt));
        RefreshUI();
    }
```

Replace with:

```csharp
    void OnFishSelected(FishEntry entry, RenderTexture rt)
    {
        if (isCooking || foodReady) return;
        // Phase 2: caller already extracted the fish from the hotbar (or
        // dex during the transition). Stage it; rt may be null.
        stagedFish.Add((entry, rt));
        RefreshUI();
    }
```

The `FishInventory.RemoveSpecificFish` call is removed — fish are already out of the hotbar by the time we get here.

- [ ] **Step 3: Update `OnRemoveFish` to return to hotbar**

Find lines 255-264:

```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        FishInventory.Instance?.ReturnFish(f);
        ReleaseRT(rt);
        RefreshUI();
    }
```

Replace with:

```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        // Phase 2: return the fish to the hotbar instead of FishInventory.
        // Hotbar should always have room since we just emptied a slot to
        // get this fish in the first place; the defensive branch covers
        // the impossible case of slots being filled in between.
        if (Hotbar.Instance != null && !Hotbar.Instance.TryAddFish(f))
        {
            InventoryFullPopup.Show();   // very unlikely; we just freed a slot
        }
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }
```

- [ ] **Step 4: Compile check**

Console clean.

- [ ] **Step 5: Behavioral check — cook panel works with hotbar**

In Play with fish in the hotbar:
1. Place a bonfire, open the cook panel.
2. Click "Add Fish" — first hotbar fish disappears from its slot, appears in the cook scroll list (row shows tier/weight; thumbnail blank is OK).
3. Click "Add Fish" two more times — next two fish stage.
4. Click "Cook" — 10s timer.
5. Click "Eat" — hunger HUD jumps.

Then test cancel:
1. Stage 2 fish.
2. Use the per-row remove button (if the UI has one) to remove one — fish returns to hotbar.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs"
git commit -m "feat(cook): Add Fish pulls from hotbar instead of dex picker

Phase 2: OnAddFishClicked calls Hotbar.TakeFirstFishOfTier(null) to
grab the first fish of any tier and stage it. OnFishSelected no
longer drains FishInventory (it's just the dex log now). OnRemoveFish
returns the staged fish to the hotbar via TryAddFish on cancel.

Phase 4's drag-and-drop sell UI will replace this with a picker
that lets the player choose specific fish; Phase 2's simpler 'next
fish' behavior keeps the cook loop playable in the interim."
```

---

## Task 9 — `FishMarketNPC` sell panel pulls from hotbar

**Files:**
- Modify: `Assets/3 - Scripts/Fishing/FishMarketNPC.cs:306-327` (sell-stage callbacks)

Same shape as Task 8. Three small edits.

- [ ] **Step 1: Replace `OnAddFishClicked`**

Find lines 306-309:

```csharp
    void OnAddFishClicked()
    {
        FishingdexManager.Instance?.OpenForSell(OnFishSelected, sellPanel);
    }
```

Replace with:

```csharp
    void OnAddFishClicked()
    {
        // Phase 2: pull the first fish (any tier) from the hotbar. Phase 4
        // brings the drag-and-drop picker.
        var entry = Hotbar.Instance?.TakeFirstFishOfTier(null);
        if (entry == null) return;
        OnFishSelected(entry, null);
    }
```

- [ ] **Step 2: Update `OnFishSelected`**

Find lines 311-316:

```csharp
    void OnFishSelected(FishEntry entry, RenderTexture rt)
    {
        FishInventory.Instance?.RemoveSpecificFish(entry);
        stagedFish.Add((entry, rt));
        RefreshUI();
    }
```

Replace with:

```csharp
    void OnFishSelected(FishEntry entry, RenderTexture rt)
    {
        // Phase 2: caller extracted from hotbar; rt may be null.
        stagedFish.Add((entry, rt));
        RefreshUI();
    }
```

- [ ] **Step 3: Update `OnRemoveFish`**

Find lines 318-327:

```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        FishInventory.Instance?.ReturnFish(f);
        ReleaseRT(rt);
        RefreshUI();
    }
```

Replace with:

```csharp
    void OnRemoveFish(FishEntry entry)
    {
        int idx = stagedFish.FindIndex(x => x.fish == entry);
        if (idx < 0) return;
        var (f, rt) = stagedFish[idx];
        stagedFish.RemoveAt(idx);
        // Phase 2: return to hotbar instead of FishInventory.
        if (Hotbar.Instance != null && !Hotbar.Instance.TryAddFish(f))
        {
            InventoryFullPopup.Show();
        }
        if (rt != null) ReleaseRT(rt);
        RefreshUI();
    }
```

- [ ] **Step 4: Compile check**

Console clean.

- [ ] **Step 5: Behavioral check — sell panel works**

At FishMarketNPC:
1. With fish in hotbar, open the sell panel.
2. Click "Add Fish" — first hotbar fish stages; money preview updates.
3. Stage 2 more.
4. Click "Confirm Sale" — money jumps by the total; staged fish are gone.

Cancel test:
1. Stage 2 fish.
2. Close the panel (back/cancel button) — staged fish return to hotbar.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Fishing/FishMarketNPC.cs"
git commit -m "feat(sell): Add Fish pulls from hotbar instead of dex picker

Same migration as cook panel (Task 8). Phase 2 sell continues to
use the tier-counter UI but reads fish from the hotbar. Phase 4
replaces with drag-and-drop."
```

---

## Task 10 — `SaveData.FishInventorySave.migratedToHotbar` field

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs:198-209` (FishInventorySave class)

- [ ] **Step 1: Add the field**

Find the `FishInventorySave` definition:

```csharp
[Serializable]
public class FishInventorySave
{
    [Serializable]
    public class Entry
    {
        public string fishType;
        public int weightLbs;
        public Color fishColor;
    }
    public List<Entry> fish = new List<Entry>();
}
```

Replace with:

```csharp
[Serializable]
public class FishInventorySave
{
    [Serializable]
    public class Entry
    {
        public string fishType;
        public int weightLbs;
        public Color fishColor;
    }
    public List<Entry> fish = new List<Entry>();
    // Phase 2: true once existing FishInventory entries have been pushed
    // into hotbar/storage on load. JsonUtility defaults to false on old
    // saves missing this field — exactly the right trigger for the one-shot
    // migration in SaveCollector.MigrateFishInventoryToHotbar.
    public bool migratedToHotbar;
}
```

- [ ] **Step 2: Compile check**

Console clean. No callsites reference this field yet — Task 11 wires it.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs"
git commit -m "feat(save): FishInventorySave.migratedToHotbar one-shot flag

Phase 2 needs a way to know whether a save's FishInventory entries
have already been pushed into hotbar/storage. The flag defaults to
false on old saves (correct: migration not yet run) and gets set to
true in ApplyFishInventory after migration. Persists on next save."
```

---

## Task 11 — `SaveCollector` migration helpers + `ApplyFishInventory` pass

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (add migration helpers + extend `ApplyFishInventory`)

- [ ] **Step 1: Locate `ApplyFishInventory`**

Run:
```bash
grep -n "ApplyFishInventory\|MigrateFish" "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
```

Find the method around line 968 (per Phase 1's spec investigation). It currently restores FishInventory contents from save:

```csharp
    static void ApplyFishInventory(FishInventorySave s)
    {
        if (FishInventory.Instance == null) return;
        var list = new List<FishEntry>();
        foreach (var e in s.fish)
        {
            var fe = new FishEntry(e.fishType, e.weightLbs);
            fe.fishColor = e.fishColor;
            list.Add(fe);
        }
        FishInventory.Instance.ReplaceAll(list);
    }
```

- [ ] **Step 2: Append the migration post-pass**

Replace with:

```csharp
    static void ApplyFishInventory(FishInventorySave s)
    {
        if (FishInventory.Instance == null) return;
        var list = new List<FishEntry>();
        foreach (var e in s.fish)
        {
            var fe = new FishEntry(e.fishType, e.weightLbs);
            fe.fishColor = e.fishColor;
            list.Add(fe);
        }
        FishInventory.Instance.ReplaceAll(list);

        // Phase 2: one-shot migration of existing FishInventory entries
        // into hotbar / storage. Old saves load with migratedToHotbar=false
        // (JsonUtility default) — that triggers the push. New saves (post
        // Phase 2) have it true after the first save so we don't re-run.
        if (!s.migratedToHotbar)
        {
            MigrateFishInventoryToHotbar(list);
            s.migratedToHotbar = true;
        }
    }

    // Push existing FishInventory entries into the hotbar; spill to storage
    // when hotbar is full; destroy (still logged in dex) when storage is
    // also full. Called once per save's lifetime, gated by
    // FishInventorySave.migratedToHotbar.
    static void MigrateFishInventoryToHotbar(List<FishEntry> entries)
    {
        if (Hotbar.Instance == null) return;
        int placedHotbar = 0, placedStorage = 0, destroyed = 0;
        foreach (var entry in entries)
        {
            if (Hotbar.Instance.TryAddFish(entry)) { placedHotbar++; continue; }
            if (TrySpillToStorage(entry))         { placedStorage++; continue; }
            destroyed++;
        }
        UnityEngine.Debug.Log($"[FishMigration] hotbar={placedHotbar} storage={placedStorage} destroyed={destroyed}");
    }

    static bool TrySpillToStorage(FishEntry entry)
    {
        var live = StorageRegistry.All;
        for (int i = 0; i < live.Count; i++)
        {
            var box = live[i];
            if (box == null) continue;
            var slots = box.Slots;
            for (int j = 0; j < slots.Length; j++)
            {
                if (slots[j].id != Hotbar.ItemId.None) continue;
                slots[j] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = entry };
                return true;
            }
        }
        return false;
    }
```

- [ ] **Step 3: Compile check**

Console clean.

- [ ] **Step 4: Behavioral check — fresh save migration**

1. With a pre-Phase 2 save in your save folder (or create one by checking out commit `1c6e88c` temporarily, catching fish, saving, returning to HEAD):
2. Boot Unity. Load the save.
3. Watch Console for `[FishMigration] hotbar=N storage=M destroyed=K`.
4. Open the hotbar — fish from the old save should be in slots.
5. Open the dex (`J`) — every fish from the old save still appears (dex unchanged).
6. Save again, reload — Console should NOT show `[FishMigration]` this time (flag is true). Fish stay where you put them.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m "feat(save): one-shot FishInventory -> hotbar migration

Old saves loading post-Phase 2 walk through FishInventory entries
and push each into hotbar (TryAddFish) -> storage spill
(TrySpillToStorage) -> destroyed (with dex still holding the log
entry). Console logs counts for diagnostic visibility.

Gated by FishInventorySave.migratedToHotbar; flag set true after
migration so subsequent loads skip the pass. The migration is
idempotent (hotbar/storage are reset from save data first, so
re-running produces the same result), but the flag avoids the
unnecessary work."
```

---

## Task 12 — Full manual regression pass

**Files:** None (verification only)

Exercise the Phase 2 acceptance criteria from the spec (§6). Pass means all 9 scenarios behave as described. Fail any → STOP, diagnose, fix, re-run before continuing to Task 13.

- [ ] **Step 1: Catch routes to hotbar**

Play. Equip rod. Cast, catch. Fish appears in next empty hotbar slot. Dex shows it.

- [ ] **Step 2: Hotbar full destroys + popup**

Fill all 7 slots with fish. Catch one more. `InventoryFullPopup` flashes; new catch does NOT appear in hotbar; dex shows it.

- [ ] **Step 3: Hold-LMB-eat completes**

Press the number key of a Fish slot. Hold LMB. Cyan ring fills clockwise over 1s. At 1s: slot empties, hunger up, kaleidoscope trip starts.

- [ ] **Step 4: Hold-LMB-eat cancels**

Hold LMB on a Fish slot for ~0.5s, then release. Ring disappears; slot still full. Hunger unchanged.

- [ ] **Step 5: Input gates reset hold**

Hold LMB on a Fish slot for ~0.5s. While still holding, press Esc/M/Tab to open pause menu / map / dialogue / phone. Close it. Ring should NOT have advanced. Hold from 0 again works.

- [ ] **Step 6: Dex is read-only**

Open dex (`J`). No "Eat Raw" button visible. Every fish ever caught (including ones consumed) appears as a card.

- [ ] **Step 7: Cook works**

Place a bonfire. Open cook panel. Click "Add Fish" — first hotbar fish stages. Click cook, eat. Hunger up.

- [ ] **Step 8: Sell works**

Walk to FishMarketNPC. Open sell. "Add Fish" stages from hotbar. Confirm sale; money up.

- [ ] **Step 9: Save migration (old save)**

Use a pre-Phase 2 save with fish in `FishInventory`. Load it. Console logs `[FishMigration]`. Fish in hotbar slots. Dex unchanged. Save + reload: no re-migration (Console silent), fish stay in same slots.

If all 9 pass, proceed to Task 13. If any fail, fix the root cause and re-test.

---

## Task 13 — Wrap-up: CLAUDE.md + tag

**Files:**
- Modify: `CLAUDE.md` (update "Hotbar & equippables" + "Currency, fish & market" sections to reflect new fish flow)
- Tag: `fish-revamp-phase-2-complete`

- [ ] **Step 1: Update CLAUDE.md hotbar section**

Find the Hotbar paragraph (likely line ~166). Append a sentence about fish behavior:

> Caught fish route into the next empty hotbar slot via `Bobber.cs` → `Hotbar.TryAddFish(entry)`; if full, `InventoryFullPopup.Show()` fires and the catch is destroyed (still logged in the dex). Hold LMB on the equipped Fish slot for 1.0s to eat raw (hunger + `RawFishTripController` trip); see `Hotbar.TickEatHold` and `RawFishConsumption.Consume`.

- [ ] **Step 2: Update CLAUDE.md fishing/cooking section**

Find the "Currency, fish & market" section. Update:
- "Cooking happens at any placed bonfire via `BonfireInteraction.cs`" — note that the cook panel's `Add Fish` button now pulls the first hotbar fish (any tier) directly; pre-Phase 2 it opened the dex picker.
- "Raw eating triggers a kaleidoscope trip." — update to reference `RawFishConsumption.Consume(tier)` in `FishInventory.cs` (single source of truth). The `FishingdexManager.OnEatRaw` path is removed.
- `FishingdexManager` — note that it's now a read-only lifetime catch log. The dex displays every fish ever caught, including consumed ones.

(Exact wording can be tightened during the task — the substance is: dex read-only, fish in hotbar, raw eating via hold-LMB, cook/sell take from hotbar.)

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for Phase 2 fish flow

- Hotbar section: catch routing + hold-LMB-eat
- Fishing section: dex is read-only, cook/sell pull from hotbar
- RawFishConsumption.Consume is the single source for raw-eat values
  (replaces FishingdexManager.OnEatRaw)"
```

- [ ] **Step 4: Tag the phase boundary**

```bash
git tag fish-revamp-phase-2-complete
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 5: Hand off**

Tell the user:

> Phase 2 done. 13 atomic commits + cherry-pick on `feature/fish-storage-revamp`; all manual regression passing; tagged `fish-revamp-phase-2-complete`. Ready to brainstorm Phase 3 (goods vendor + fish bag) whenever you are.

---

## Self-Review

**Spec coverage** (against spec §2 goals):
- ✓ Catch routes to hotbar → Task 4
- ✓ InventoryFullPopup on destroy → Task 4 (Bobber edit)
- ✓ FishInventory becomes lifetime log (append-only) → No active change needed; cook/sell migration (Tasks 8/9) stop draining it
- ✓ Dex shows every fish, no eat-raw → Task 7
- ✓ Hold-LMB-1s eats from equipped slot → Tasks 5 (logic) + 6 (ring)
- ✓ Cook/sell pull from hotbar → Tasks 8 + 9
- ✓ Old saves migrate → Tasks 10 + 11
- ✓ migratedToHotbar flag → Task 10

**Placeholder scan:** No TODOs, no TBDs. Two soft-uncertainty points: (1) Task 6 mentions `HotbarRoundedRing.GetSprite()` may not exist — task body says "substitute the slot's existing border sprite or use Unity's built-in `UISprite`" if so; (2) Task 7 says "delete the button creation site" with a grep step — the exact code is in the file but I haven't read the full builder. Both are concrete actions ("read this file, find this pattern, delete it").

**Type consistency:** `RawFishConsumption.Consume(string tier)` (Task 2) ↔ called from `Hotbar.ConsumeEquippedFish` (Task 5) with `slot.fishData.fishType`. `Hotbar.TryAddFish(FishEntry)` (Task 3) ↔ called from `Bobber` (Task 4), `BonfireInteraction.OnRemoveFish` (Task 8), `FishMarketNPC.OnRemoveFish` (Task 9), `SaveCollector.MigrateFishInventoryToHotbar` (Task 11). `Hotbar.TakeFirstFishOfTier(string)` (Task 3) ↔ called from `BonfireInteraction.OnAddFishClicked` and `FishMarketNPC.OnAddFishClicked` (Tasks 8/9), both pass `null` for "any tier". `FishInventorySave.migratedToHotbar` (Task 10) ↔ checked + set in `ApplyFishInventory` (Task 11). All consistent.

**Scope check:** 13 tasks, all under 15 minutes each. Single phase, single PR.

No fixes needed.
