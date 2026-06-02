# Fish & Storage Revamp — Phase 2: Fish Flow + Dex Revamp + Hold-to-Eat

**Status:** Draft — awaiting user approval
**Date:** 2026-05-26
**Branch:** `feature/fish-storage-revamp` (Phase 1 lands at tag `fish-revamp-phase-1-complete`)
**Author:** Sam McNeil with Claude

---

## 1. Why this phase exists

Phase 1 made the hotbar capable of carrying fish (slot count 5→7, `Slot.fishData` payload, save round-trip). Phase 2 is what makes that capability matter: caught fish now route into the hotbar, the player can hold LMB on a fish slot to eat it raw, and the fishingdex becomes a read-only lifetime log instead of a working inventory + eat-from button.

Three plumbing changes accompany the player-visible shift so the rest of the game loop keeps working: cook and sell panels migrate to read from the hotbar (the Phase 4 drag-and-drop sell UI is later polish; this phase keeps the existing tier-counter UI alive against the new data source), and old saves get a one-shot migration of their `FishInventory` entries into hotbar/storage.

---

## 2. Goals & non-goals

### Goals

- Caught fish land in the hotbar (next empty slot). If hotbar is full, the fish is discarded but still logged in the dex.
- `InventoryFullPopup.Show()` fires on discard so the player gets a visible cue.
- `FishInventory` becomes the lifetime dex log — append-only. Drain methods stop being called by gameplay; the list never shrinks during play.
- Fishingdex shows every fish ever caught (including eaten/cooked/sold/destroyed). Eat-raw button removed.
- Hold LMB on the equipped Fish slot for 1.0s → consume one fish: hunger restored, raw-fish trip starts, slot empties. Releasing LMB early cancels and resets the progress ring.
- Cook panel (`BonfireInteraction`) and sell panel (`FishMarketNPC`) read from hotbar via new `Hotbar.CountFishByTier` / `TakeFirstFishOfTier` / `TryAddFish` helpers. Existing tier-counter UI shape stays.
- Old saves: existing `FishInventory` entries migrate one-shot into hotbar → spill to storage → destroy. Logged in dex either way.

### Non-goals (deferred to later phases)

- Fish bag item, its 5-slot side panel, and storage right-click integration — Phase 3.
- Goods vendor selling fishing rod / water bottle / fish bag — Phase 3.
- Drag-and-drop sell UI replacement (current tier-counter sell UI continues to work via the hotbar pull-path in Phase 2) — Phase 4.
- Deleting `FishInventory`'s dormant drain methods (`RemoveSpecificFish`, `SellStaged`, `RemoveByType`, `ClearInventory`, `ReturnFish`) — Phase 5 cleanup. They're harmless to leave.

---

## 3. Confirmed design decisions

| Decision | Choice |
|---|---|
| Cook + sell during Phase 2 | Patch them to pull from hotbar via new helpers. Keep the existing tier-counter UI; Phase 4 replaces it with drag-and-drop. |
| Old saves migration | Migrate `FishInventory` → hotbar (next-empty), spill to storage if hotbar full, destroy any further overflow. Dex unchanged. |
| `FishInventory` post-Phase-2 role | Lifetime dex log — append-only. Drain methods stay present-but-unused (dead code, defer cleanup). |
| Catch destination | Hotbar only (storage isn't an automatic destination — Phase 3+ via fish bag). |
| Cook + sell sources | Hotbar only (storage requires manual drag-out via existing storage UI). |
| Raw-eat hunger + trip params | Reuse `FishingdexManager.OnEatRaw`'s existing values verbatim, factored into a shared helper. |
| Progress ring | New `Image` element added to `SlotVisuals`, using `Image.fillAmount` 0→1 as `_eatHeldSeconds / 1.0f`. |

---

## 4. Architecture changes

### 4.1 Catch flow (`Bobber.cs`)

Today (`Bobber.cs:263`):
```csharp
FishInventory.Instance.AddFish(currentFishType, weight);
```

After:
```csharp
// Always log to dex first.
FishInventory.Instance.AddFish(currentFishType, weight);
// Try to place in hotbar; pop the just-logged entry back as a runtime FishEntry.
var entry = FishInventory.Instance.AllFish[FishInventory.Instance.AllFish.Count - 1];
if (Hotbar.Instance == null || !Hotbar.Instance.TryAddFish(entry))
{
    InventoryFullPopup.Show();
}
```

Alternative cleaner shape: `FishInventory.AddFish` could return the freshly-constructed `FishEntry` so the caller doesn't have to peek the list tail. Plan-writing decides which.

### 4.2 Hotbar helpers (`Hotbar.cs`)

Three new public methods on the `Hotbar` singleton:

```csharp
// Phase 2: caught-fish routing.
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

// Cook + sell tier counters.
public int CountFishByTier(string tier)
{
    int n = 0;
    for (int i = 0; i < NumSlots; i++)
        if (slots[i].id == ItemId.Fish && slots[i].fishData != null && slots[i].fishData.fishType == tier) n++;
    return n;
}

// Cook + sell stage-add. Returns the entry and clears the source slot.
public FishEntry TakeFirstFishOfTier(string tier)
{
    for (int i = 0; i < NumSlots; i++)
    {
        if (slots[i].id != ItemId.Fish || slots[i].fishData == null) continue;
        if (slots[i].fishData.fishType != tier) continue;
        var entry = slots[i].fishData;
        slots[i] = default;
        OnResourceChanged?.Invoke(ItemId.Fish);
        return entry;
    }
    return null;
}
```

### 4.3 Hold-LMB eat (`Hotbar.cs`)

New state fields:
```csharp
int _eatProgressSlot = -1;
float _eatHeldSeconds = 0f;
const float EatHoldDuration = 1.0f;
```

New logic in `Update` (called from `HandleInput` or alongside it; runs only when not piloting / not in dialogue / not phone):
```csharp
void TickEatHold()
{
    int eq = _equippedSlot;
    bool isFishEquipped = eq >= 0 && eq < NumSlots
                       && slots[eq].id == ItemId.Fish
                       && slots[eq].fishData != null;
    if (!isFishEquipped || !Input.GetMouseButton(0))
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

    int hunger = RawFishHunger(slot.fishData.fishType);
    ResourceManager.Instance?.ConsumeFood(hunger);
    RawFishTripController.StartTrip(/* shared params, see §4.6 */);

    slots[eq] = default;
    OnResourceChanged?.Invoke(ItemId.Fish);
}
```

Reset paths needed: dialogue-enter / phone-open / map-open / pilot-enter / scene-reload / save-load all must zero `_eatProgressSlot` so a stale progress doesn't fire after the player exits and re-enters input.

### 4.4 Progress ring (`Hotbar.SlotVisuals` + Refresh)

`SlotVisuals` gets one new field:
```csharp
public Image progressRing;   // null until built; uses fillAmount 0..1
```

`BuildSlotView` (the procedural builder) gets one new sub-element — a circular ring overlay on top of the icon, `fillAmount = 0`, `enabled = false` by default. Geometry choice: re-use `HotbarRoundedRing.GetSprite()` (already used for slot borders) but with `Image.type = Filled, fillMethod = Radial360`.

`Refresh` paints it:
```csharp
foreach slot view at index i:
    bool active = (i == _eatProgressSlot);
    sv.progressRing.enabled  = active;
    sv.progressRing.fillAmount = active ? Mathf.Clamp01(_eatHeldSeconds / EatHoldDuration) : 0f;
```

### 4.5 Fishingdex (`FishingdexManager.cs`)

Three changes:
1. Delete the eat-raw button creation in `BuildDetailPanel` (or wherever the button is wired).
2. Delete the `OnEatRaw` method.
3. The dex was already iterating `FishInventory.AllFish` to display cards. Now that list is the lifetime log instead of active inventory — display logic is unchanged. **No new "has been eaten" status indicator** is in scope for Phase 2; every entry just appears.

### 4.6 Shared raw-eat helper (`FishInventory.cs`)

Extract the per-tier values from `FishingdexManager.OnEatRaw:336-345` into a public static helper. Current code:
```csharp
(float cooked, float ek, float ew, float ed, float lk, float lw) = entry.fishType switch
{
    "Rare"     => (60f, 0f, 1f,  5f, 1.0f, 0f),
    "Uncommon" => (35f, 0f, 1f, 10f, 0.4f, 0f),
    _          => (20f, 0f, 1f, 30f, 0f,   1f),
};
float raw = cooked * 0.5f;
ResourceManager.ConsumeFood(raw);
RawFishTripController.StartTrip(30f, ek, ew, ed, lk, lw);
```

Extracted to `RawFishConsumption.Consume(string tier)`:
```csharp
public static class RawFishConsumption
{
    public static void Consume(string tier)
    {
        (float cooked, float ek, float ew, float ed, float lk, float lw) = tier switch
        {
            "Rare"     => (60f, 0f, 1f,  5f, 1.0f, 0f),
            "Uncommon" => (35f, 0f, 1f, 10f, 0.4f, 0f),
            _          => (20f, 0f, 1f, 30f, 0f,   1f),
        };
        ResourceManager.Instance?.ConsumeFood(cooked * 0.5f);
        RawFishTripController.StartTrip(30f, ek, ew, ed, lk, lw);
    }
}
```

Pure extraction — same hunger restored (raw = cooked × 0.5, so 30 / 17.5 / 10 for Rare / Uncommon / Common), same trip duration (30s), same per-tier early/late kaleidoscope intensities.

### 4.7 Cook panel (`BonfireInteraction.cs`)

Three line-level changes:
- `GetCommonCount` / `GetUncommonCount` / `GetRareCount` (or equivalent) → call `Hotbar.Instance.CountFishByTier(tier)`.
- `OnAddFishClicked` finds-an-entry-by-tier flow → call `Hotbar.Instance.TakeFirstFishOfTier(currentStageTier)`. If null, no-op.
- Cancel-path `ReturnFish(entry)` → `Hotbar.Instance.TryAddFish(entry)`. If hotbar is full (impossible in normal flow since we just took from it, but defensive), spill to storage or destroy + popup.

### 4.8 Sell panel (`FishMarketNPC.cs`)

Identical shape to §4.7. Different file, same three lines.

### 4.9 Save migration (`SaveData.cs` + `SaveCollector.cs`)

Schema additive change in `SaveData.cs`:
```csharp
[Serializable]
public class FishInventorySave
{
    [Serializable] public class Entry { ... }
    public List<Entry> fish = new List<Entry>();
    // Phase 2 — true once existing entries have been migrated into hotbar/storage.
    // JsonUtility defaults to false on old saves, which is the correct trigger.
    public bool migratedToHotbar;
}
```

`SaveCollector.ApplyFishInventory` post-restore:
```csharp
if (!s.migratedToHotbar)
{
    MigrateFishInventoryToHotbar(list);
    s.migratedToHotbar = true;   // mutates the in-memory SaveData; persists to disk on next save
}
```

Helper:
```csharp
static void MigrateFishInventoryToHotbar(List<FishEntry> entries)
{
    if (Hotbar.Instance == null) return;
    int destroyed = 0;
    foreach (var entry in entries)
    {
        if (Hotbar.Instance.TryAddFish(entry)) continue;
        if (TrySpillToStorage(entry)) continue;
        destroyed++;
    }
    if (destroyed > 0)
        Debug.Log($"[FishMigration] {destroyed} fish destroyed — no inventory or storage space.");
}

static bool TrySpillToStorage(FishEntry entry)
{
    foreach (var box in StorageRegistry.All)
    {
        if (box == null) continue;
        var slots = box.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].id != Hotbar.ItemId.None) continue;
            slots[i] = new Hotbar.Slot { id = Hotbar.ItemId.Fish, count = 1, fishData = entry };
            return true;
        }
    }
    return false;
}
```

**Ordering:** `ApplyFishInventory` must run after `ApplyHotbar` (so we know which slots are full of equippables/resources) AND after `ApplyStorages` (so spill targets the correct restored storage state). The existing apply order already satisfies this; no reordering needed.

**Idempotency:** if the player loads, doesn't save, and reloads, migration runs again on the same `FishInventory` entries — but the hotbar gets reset from save data first (which is empty in old saves), so the same slot fills happen. No double-counting.

---

## 5. Backward compatibility

- **Old saves with active FishInventory entries:** load triggers one-shot migration. Up to 7 fish land in hotbar (skipping any occupied equippable slots); next entries spill to storage if any LootBox has empty slots; rest destroyed. Dex unchanged (FishInventory contents are preserved as the lifetime log).
- **Old saves with empty FishInventory:** no migration work. New catches go to hotbar normally.
- **New saves (post-Phase-2):** `migratedToHotbar = true` from first save. Subsequent loads skip migration.
- **Pre-Phase-1 saves:** no `hotbar` field — Phase 1 already handles that gracefully (loads as empty). Migration runs and fills the now-empty hotbar.

---

## 6. Acceptance criteria

After Phase 2 lands, manual Editor regression on a fresh play session:

1. **Catch routes to hotbar.** Fishing rod casts a bobber, lands a fish — fish appears in next empty hotbar slot (with fishColor tint + weight badge), dex (`J`) shows it.
2. **Hotbar full destroys + popup + dex still logs.** Fill all 7 slots with fish (or rod + 6 fish), catch one more — `InventoryFullPopup` flashes above hotbar, the new catch doesn't appear in hotbar, dex shows it.
3. **Hold-LMB-eat from equipped Fish slot.** Press the number key of a Fish slot, hold LMB — progress ring fills clockwise around the slot icon over 1.0 second. Release at 0.5s → ring resets, slot still full. Hold full 1.0s → slot empties, hunger HUD jumps by the tier's raw-hunger value, kaleidoscope ramps in.
4. **Eat cancels on input gate.** Mid-hold (~0.5s), open the pause menu or trigger an NPC dialogue. Ring resets, slot still full.
5. **Dex revamp.** Open dex — every fish ever caught (including ones now consumed or destroyed) is shown. **No eat-raw button** anywhere.
6. **Cook flow works.** Place a bonfire, open cook panel — tier counters match hotbar tiers. Click "+" on Common — first Common fish disappears from hotbar, appears in cook stage. Cancel the cook — fish returns to hotbar (first empty slot). Cook + eat — hunger up.
7. **Sell flow works.** At FishMarketNPC, open sell panel — tier counters match hotbar. Stage some fish, sell — money increases. Cancel before selling — fish returns to hotbar.
8. **Save migration (old save).** Load a save from before Phase 2 that has 3+ fish in FishInventory. After load: those fish are in the hotbar (first available slots), dex shows them too. Save + reload — fish stay in hotbar slots (already migrated, no re-migrate), dex unchanged.
9. **Save migration (new save).** Save mid-Phase-2 session, reload — fish stay exactly where they were in hotbar/storage. Dex still shows the lifetime log.

---

## 7. Files touched (estimate)

| File | Change |
|---|---|
| `Assets/3 - Scripts/Fishing/Bobber.cs` | catch flow: `AddFish` to dex + `TryAddFish` to hotbar + popup on destroy |
| `Assets/3 - Scripts/UI/Hotbar.cs` | new `TryAddFish` / `CountFishByTier` / `TakeFirstFishOfTier`; hold-LMB eat handler + ring state; `SlotVisuals.progressRing` + Refresh paint |
| `Assets/3 - Scripts/Fishing/FishingdexManager.cs` | remove eat-raw button + `OnEatRaw` handler |
| `Assets/3 - Scripts/Fishing/FishInventory.cs` | comment update; add `RawFishConsumption` static helper (extract values from current `OnEatRaw`) |
| `Assets/3 - Scripts/NPC_Dialogue/BonfireInteraction.cs` | cook panel reads via `CountFishByTier` / `TakeFirstFishOfTier` / `TryAddFish` |
| `Assets/3 - Scripts/Fishing/FishMarketNPC.cs` | same migration as BonfireInteraction |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | `bool migratedToHotbar` field on FishInventorySave |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | `MigrateFishInventoryToHotbar` + `TrySpillToStorage` helpers; one-shot call in `ApplyFishInventory` |

---

## 8. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Hold-LMB-eat fires during equipped-slot UI navigation (e.g. clicking on the storage panel while a fish is equipped) | `Hotbar.TickEatHold` already gated by the existing `piloting / inDialogue / phoneOpen / mapOpen` checks in `Update`. Add `PlayerController.isInStorage` to the same gate. |
| Migration re-runs and double-fills hotbar if save flag breaks | Migration is idempotent against a fresh load: hotbar is reset from save's hotbarSlots (empty in pre-Phase-2 saves) before migration runs. Even repeated migration produces the same end state. |
| Cook/sell return-path needs a slot but hotbar is full | Reverse-of-the-take should always have room because the take just emptied a slot. Defensive `TryAddFish → spill → destroy + popup` handles the impossible case. |
| Progress ring lingers across scene reload | New ring `Image` is rebuilt by `BuildUI` which runs in `Awake` per scene load. State variables `_eatProgressSlot`/`_eatHeldSeconds` reset at top of `Awake`. |
| Existing tutorial scripts reference `FishInventory.AddFish` for tutorial gate triggers | `FirstFishCaught` / `OneOfEachCaught` flags in `EarlyGameProgress` fire from `Bobber` catch flow today; Bobber still calls `AddFish` post-Phase-2 (for dex log), so tutorial triggers are unaffected. |

---

## 9. What comes next

After this spec is approved, `writing-plans` skill turns it into an atomic step-by-step plan at `docs/superpowers/plans/2026-05-26-fish-storage-revamp-phase-2-fish-flow.md`. Each plan step is a small commit on `feature/fish-storage-revamp`. Once Phase 2 lands and regression passes, Phase 3 (vendor expansion + fish bag) starts its own brainstorm.
