# Early-Game Progression Revamp — Plan

Grounded in the current codebase. File paths are real, class/event names match what's there, and reuse callouts say exactly which method to call.

## ⚠️ Open question

**The cassette/Tev trade flow — keep or replace?** Tev's existing `NPCDialogue.cs` is hardcoded for "trade cassette → get fishing rod, then call `BonusTutorial.OfferFishing()`." The new flow says Tev gives a *note* (no trade) and the rod is a pickup. **Recommended: keep the file, gate the cassette path behind a "stage" enum.** New `TevDialogue` is a state machine with stages `AwayLeftNote → ReturnedGiveAxe → AfterBuildGiveVillage → Done`. Stage 0 disables the trigger; the note pickup is a separate object.

> **Scrapped:** the day/night, sleep, and enemies-during-tutorial design from earlier drafts. The new Phase 5 is just "walk back to the cabin because Tev should be home now" — no nighttime, no enemies, no sleep mechanic. Kept the system simple and avoided the planet-rotation rabbit hole entirely.

---

## Shared infrastructure (build first — used by every phase)

### Compass HUD (Skyrim-style, top-center)

- **New file**: `Assets/3 - Scripts/UI/CompassHUD.cs`
- **Singleton**, auto-create via `RuntimeInitializeOnLoadMethod` (skip on `MainMenu`), `DontDestroyOnLoad`. Add to `MainMenuController.EnsureGameplaySingletons()` (line 473).
- **Pattern**: closely mirrors `Assets/3 - Scripts/Pickups/PickupUIManager.cs`. Differences: clamps to a horizontal strip (top, full screen width), uses *bearing angle* rather than raw screen X, never hides at proximity.
- **Public API**:
  - `void AddWaypoint(string id, Func<Vector3> worldPosProvider, string label, Sprite icon = null, Color? tint = null)` — provider-based so a moving target updates each frame
  - `void RemoveWaypoint(string id)`
  - `void SetActive(string id, bool active)` — for "highlight current objective"
- **Math**: project waypoint world position onto player's `transform.up` (planet-tangent plane), compute angle vs `Camera.main.transform.forward` projected onto same plane. Map [-90°, +90°] to screen X [0, 1].
- **Save**: list of `{id, label, sourceTag}` in a new `CompassSave` block. Waypoint *positions* recompute from sourceTag — e.g., `"FishingBank"` resolves at runtime to a scene `Transform` tagged `FishingBank`.

### Note pickup + read UI

- **New files**:
  - `Assets/3 - Scripts/Player/NoteCollection.cs` — singleton list of `string` IDs the player has read
  - `Assets/3 - Scripts/Player/NotePickup.cs` — drop-in component for scene placement. Has `string noteId`, `string title`, `[TextArea] string body`. On player trigger + F (gated on `TutorialAbility.Pickup`), opens a fullscreen panel.
  - `Assets/3 - Scripts/UI/NoteReadUI.cs` — singleton, screen-overlay canvas like `BonusTutorial`'s popup (sortingOrder ~750). Pattern: copy `BonusTutorial.BuildPopup` style. Shows title + body, "press TAB to close." Uses `DialogueTextStyling.RevealCharsTMP` for typewriter on body.
- **Save**: `NoteSave { List<string> readNoteIds }` in `SaveData`. `NoteCollection.Has(id) → bool`.

### Per-blueprint build menu lock

- **New file**: `Assets/3 - Scripts/Building/BuildMenuLock.cs` — static class wrapping a `HashSet<string>` of unlocked `displayName`s. API:
  - `static bool IsUnlocked(string name)`
  - `static void Unlock(string name)` / `UnlockAll()` / `LockAllExcept(params string[])`
- **Modify**: `BuildMenuUI.cs` line 459-481 (`RebuildVisibleCards`) — add `&& BuildMenuLock.IsUnlocked(entry.displayName)` to the visibility check.
- **Default**: `LockAllExcept("Cabin", "Torch", "Bonfire")` is called once when build tutorial starts. After village phase, call `UnlockAll()`.
- **Save**: `BuildMenuLockSave { List<string> unlockedNames }` in `SaveData`.

### Fishing rod unlock (replaces the cassette gate)

- **Modify**: `Assets/3 - Scripts/Fishing/FishingRodController.cs` — add `public bool IsUnlocked { get; private set; }` and `public void Unlock()`.
- **Modify**: `Assets/3 - Scripts/UI/Hotbar.cs` line 141 — change `IsUnlocked` predicate from `rod.npcDialogue.ConversationCompleted` to `rod.IsUnlocked`.
- **Modify**: `Assets/3 - Scripts/SaveSystem/SaveData.cs` `EquipmentSave` — add `bool fishingRodUnlocked = true;` (default `true` so existing saves see the rod as unlocked).
- **Modify**: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` `CaptureEquipment` / `ApplyEquipment` — add the unlock fields parallel to `axeUnlocked`.

### Tev's multi-stage dialogue

- **New file**: `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs` — replaces `NPCDialogue.cs` on the Alien3 GameObject for the new flow.
- **State enum**: `Stage { AwayLeftNote, ReturnedGiveAxe, AfterBuildGiveVillage, Done }`.
- **Behavior**:
  - `Awake`: if stage == `AwayLeftNote`, disable trigger collider.
  - On player trigger + F, branch on stage:
    - `ReturnedGiveAxe`: typewriter welcome-back + axe explanation; on completion call `axeController.Unlock() + pistolController.Unlock()`, set `EarlyGameProgress.tevReturnDialogueDone = true`, advance stage.
    - `AfterBuildGiveVillage`: typewriter "go to village at coords X"; on completion adds compass + map waypoint for village, advance to `Done`.
    - `Done`: random voice-line.
- **Save**: extend `NPCSave.stateString` to encode the stage (mirrors `GuitarShopNPC` pattern).
- **Pattern reuse**: typewriter via `DialogueTextStyling.RevealCharsTMP`; talk-prompt + trigger flow copy from `BonfireNPCDialogue.cs`.

### Save schema additions

```csharp
// New in SaveData.cs:
public CompassSave compass = new CompassSave();
public NoteSave notes = new NoteSave();
public BuildMenuLockSave buildMenuLock = new BuildMenuLockSave();
public EarlyGameProgressSave earlyGame = new EarlyGameProgressSave();

[Serializable] public class CompassSave { public List<string> activeWaypointIds = new(); }
[Serializable] public class NoteSave { public List<string> readNoteIds = new(); }
[Serializable] public class BuildMenuLockSave { public List<string> unlockedNames = new(); }
[Serializable] public class EarlyGameProgressSave {
    public bool noteRead;
    public bool rodPickedUp;
    public bool firstFishCaught;
    public bool oneOfEachCaught;
    public bool firstMealEaten;
    public bool waterBottleDrunk;
    public bool returnedHome;
    public bool tevReturnedDialogueDone;
    public bool cabinBuilt;
    public bool villageCoordsGiven;
    public bool fishVendorVisited;
    public bool goodsVendorVisited;
}
// Plus EquipmentSave gets `bool fishingRodUnlocked = true;`
// Plus EquipmentSave gets `bool waterBottleUnlocked = true;`
```

`SaveCollector` gets matching `CaptureCompass/Note/Sleep/BuildMenuLock/EarlyGame` and `Apply*` methods. **Apply order**: insert *after* `ApplyTutorial` (line 333) and before `ApplyResources` — these are pure singleton-state, slot into the Step 4 group at line 335-340.

### Replace the default tutorial flow

- **Modify**: `Assets/3 - Scripts/Tutorial/TutorialSteps.cs` `BuildDefault()` (line 6) — return the new step list.
- **Keep all existing step classes intact** — type-name-based save resolution (`TutorialManager.ApplyState` at TutorialManager.cs:262-272) means old saves still load.
- **Modify**: `TutorialManager.cs` line 64 (`ShowPreCrashWarningRoutine`) — the new flow doesn't crash-land. Replace with auto-start on scene load.

---

## Execution order

1. **Save schema additions** + collector methods (1 hr) — unblocks everything else
2. **Compass HUD + singleton seeding** (2 hr)
3. **NotePickup + NoteReadUI** (2 hr)
4. **TevDialogue state machine** (2 hr)
5. **BuildMenuLock** (1 hr)
6. **Fishing rod unlock refactor** (1 hr)
7. **Phases 1-7 step classes** in order (each phase ~1-2 hr)

---

## Phase 1 — Wake in cabin, find note

**Player experience**: Black fade-in. Player on bed in cabin. Tip: "look around / move." Sees a note on a table. Picks it up — fullscreen note panel reveals Tev's letter explaining the crash, the fishing rod by the door, and how to find the fishing bank. Compass becomes active showing "FISHING BANK".

**Reuse**:
- `TutorialUI.Instance.ShowStep` for the standard tutorial HUD
- `MouseLookStep` / `MoveStep` patterns from `TutorialSteps.cs:61-110` (reuse directly; relax MoveStep to drop sprint requirement)
- `TutorialGate.Unlock(MouseLook)` then `Unlock(Move)`

**New code**:
- `WakeUpStep` — reuse `MouseLookStep` + `MoveStep` from existing TutorialSteps.cs.
- `PickUpNoteStep : TutorialStep` — `OnEnter` unlocks `TutorialAbility.Pickup`, `Tick` checks `NoteCollection.Has("tev_intro")`. Tip: `$"Press {PromptGlyphs.Interact} to read the note."`

**Scene authoring (USER places)**:
- Cabin GameObject placed on Humble Abode (parented to `CelestialBody` so it follows the planet)
- Bed GameObject inside the cabin
- NotePickup GameObject on a table inside the cabin, `noteId = "tev_intro"`, `title = "From Tev"`, body filled with letter text
- FishingRod_Pickup GameObject (rod prop with `FishingRodPickup` component) on a wall-mount or by the door
- Empty GameObject tagged `FishingBank` placed at the lake/river spot

**Save impact**:
- `EarlyGameProgressSave.noteRead` flips true via `NoteCollection.OnNoteRead`
- `CompassSave.activeWaypointIds` adds `"fishing_bank"`

**Risks**:
- Existing `MoveStep` requires sprint. Need to relax for Phase 1 (drop the `sprintSeen` requirement).

---

## Phase 2 — Pick up rod, walk to fishing bank, fishing tutorial

**Player experience**: Player picks up rod (auto-equips). Compass arrow points to fishing bank. Player walks there. Tutorial: "click to cast" → "wait for bobber, click to reel" → catch one. Then: "catch one of each kind." Then: "press B to open Fishingdex" → step ends.

**Reuse**:
- `FishingRodController.OnBobberCast` (static event)
- `FishingRodController.OnFishCaught` (static event)
- `FishingdexManager.OnFishingdexOpened` (static event)
- `FishInventory.Instance.CommonCount` / `UncommonCount` / `RareCount` for tracking distinct types
- `BonusTutorial`'s `CastBobberStep`, `CatchFishStep`, `OpenFishingdexStep`, `FishingExtraInfoStep` — copy bodies into new `TutorialStep` subclasses

**New code**:
- `Assets/3 - Scripts/Pickups/FishingRodPickup.cs` — drop-in component, OnTriggerEnter+F → calls `rod.Unlock()`, `rod.ForceEquipRod()`, sets `EarlyGameProgress.rodPickedUp = true`, destroys self.
- `PickUpRodStep`, `WalkToFishingBankStep`, `CastBobberStep`, `CatchFirstFishStep`, `CatchOneOfEachStep`, `OpenFishingdexStep` (all `TutorialStep` subclasses).
- `CatchOneOfEachStep`: `BlocksAutoSkip = true`, live tip via `SetTip` showing "Common: 1/1, Uncommon: 0/1, Rare: 0/1".

**Scene authoring (USER places)**:
- `FishingBank` tagged Transform near water (existing fishing system needs water tag)
- Optional: visible flag/marker at the bank

**Risks**:
- "Catch one of each" assumes the bank can spawn all three rarities. Verify against existing rarity logic.

---

## Phase 3 — Walk to fire, cooking tutorial, eat to restore hunger

**Player experience**: Compass repoints to "FIRE." Player walks there. Tutorial: "press F to interact with the fire" → cooking panel opens → "add a fish, then press Cook" → "press Eat" → step completes when hunger restored above 80%.

**Reuse**:
- `BonfireInteraction.cs` — fully built. Existing `OpenCookPanel`/`OnCookClicked`/`OnEatClicked` are the cook flow.
- `ResourceManager.Instance.HungerPercent`, `.ConsumeFood()`.

**New code**:
- `WalkToFireStep`, `OpenCookPanelStep` (needs new `BonfireInteraction.OnPanelOpened` static event — one-line addition), `CookAndEatStep`.

**Scene authoring (USER places)**:
- Confirm existing scene Bonfire has correct tag and `BonfireInteraction.cookPanel` + `promptText` wired (should be — pre-built per CLAUDE.md)
- Or place a copy of the bonfire prefab at the chosen tutorial spot

**Risks**:
- `OpenCookPanel` requires fish in inventory. Previous step guarantees ≥3 fish. Add fail-safe in `CookAndEatStep` for empty inventory.

---

## Phase 4 — Water bottle: pickup, refill, drink

**Player experience**: Compass repoints to a stream/lake. Tutorial: "find the water bottle near the bonfire" → pick up → "stand in water, hold RMB to fill" → "hold LMB to drink" → step completes when thirst restored.

**Reuse**:
- `WaterBottleController.cs` — fully built.
- `ResourceManager.Instance.ThirstPercent` and `DrinkWater(amount)`.

**New code**:
- Add `bool isUnlocked` and `Unlock()` to `WaterBottleController` (consistency with rod/axe/pistol).
- `Assets/3 - Scripts/Pickups/WaterBottlePickup.cs` — same shape as `FishingRodPickup`.
- `PickUpBottleStep`, `RefillBottleStep`, `DrinkBottleStep`.
  - Add public `bool IsRefilling` getter to `WaterBottleController` (one line).

**Scene authoring (USER places)**:
- WaterBottlePickup GameObject near the cabin or bonfire
- Verify fishing-bank water has `Water` tag

**Save**: New `EquipmentSave.waterBottleUnlocked` field.

---

## Phase 5 — Return home, talk to Tev, axe + pistol unlock

**Player experience**: After the water tutorial, the tip says "Tev should be home by now — head back to the cabin." Compass repoints to the cabin. Player walks back. As they approach, Tev (Alien3) is now outside the cabin (his stage flag advances when `EarlyGameProgress.waterBottleDrunk` flips true, OR when the player gets within range of the cabin — pick whichever feels best to author). Player talks to Tev: "welcome back young buck. Take this axe so you can build proper shelter. Keep the pistol too — it'll keep the orbs at bay." → axe + pistol unlock.

**Reuse**:
- `AxeController.Unlock()`, `PistolController.Unlock()`
- `BonfireNPCDialogue.cs` is the precedent for "first-time line + reward" — copy that pattern into TevDialogue's `ReturnedGiveAxe` stage
- Compass waypoint pointing at the `Cabin` tagged transform

**New code**:
- `ReturnHomeStep : TutorialStep` — sets compass waypoint to cabin, completes when player within 5m of cabin. On `OnEnter` advances `TevDialogue.CurrentStage` to `ReturnedGiveAxe` (which also enables Tev's collider).
- `TalkToTevStep : TutorialStep` — `BlocksAutoSkip = true`. Completes when `EarlyGameProgress.tevReturnedDialogueDone == true` (set by TevDialogue at end of axe-giving sequence).

**Scene authoring (USER places)**:
- Tev GameObject (Alien3) gets the new `TevDialogue` component (replacing existing `NPCDialogue`). Position him outside the cabin in his "returned" pose.
- Add a `Cabin` tag to the cabin GameObject so the compass and step can resolve it.

---

## Phase 6 — Axe tutorial → build tutorial (cabin/torch/bonfire only)

**Player experience**: After Tev hands over axe, walk through chopping (find tree, swing axe, gather 60 wood) then building (open menu, build cabin). Build menu shows ONLY cabin, torch, bonfire. After cabin placement, tip "head back to Tev when you're ready."

**Reuse**:
- Existing `BonusTutorial.OfferAxeBuilding()` step bodies (`SwingAxeStep`, `GatherWoodStep`, `BuildCabinStep`) — copy into TutorialStep subclasses
- `AxeController.cs`, `WoodInventory.Instance`, `BuildMenuUI`, `GhostPlacement.OnPlaced`

**New code**:
- `LockBuildMenuToBasicsStep.OnEnter` calls `BuildMenuLock.LockAllExcept("Cabin", "Torch", "Bonfire")`.
- `SwingAxeStep`, `GatherWoodStep`, `BuildCabinStep`, `TalkToTevAgainStep`.

**Scene authoring (USER does)**:
- Confirm trees exist near cabin (spawned by `TreeSpawner` per CLAUDE.md)

---

## Phase 7 — Tev gives village coords → travel → meet vendors

**Player experience**: Talk to Tev → "go to village at coordinates X" → village marker appears on map and compass. Player travels. Reaches village → markers appear over fish vendor and goods vendor. Player approaches each → marker disappears → step done. Tutorial finishes. `BuildMenuLock.UnlockAll()` is called.

**Reuse**:
- `FishMarketNPC.cs`, `Alien7Vendor` — both exist with full UI
- `SolarSystemMapController.Instance` — add `void AddCustomMarker(string id, Vector3 worldPos, string label)` API (one method, ~20 lines)
- `CompassHUD.AddWaypoint`

**New code**:
- `TevGiveVillageCoordsStep`, `TravelToVillageStep`, `MeetFishVendorStep`, `MeetGoodsVendorStep`, `TutorialFinaleStep` (calls `BuildMenuLock.UnlockAll()`).

**Scene authoring (USER places)**:
- "Village" GameObject hierarchy somewhere on the planet (or another body): `VillageEntry` tagged transform, FishMarketNPC, Alien7Vendor, decorations

---

## Risk summary

1. **Replacing `NPCDialogue` on Alien3** — confirm Alien3 is unique to gameplay scene `1.6.7.7.7.unity`, or namespace TevDialogue per-instance.
2. **`MoveStep` sprint requirement** — relax for Phase 1 (player shouldn't need to sprint to complete the wake-up step).
3. **Tutorial restart semantics on save** — `TutorialManager.ApplyState` resolves by step type name. Old saves load into new tutorial cleanly. Test mid-step transitions (e.g., mid-`TalkToNPCsStep` → new flow).
4. **`MainMenuController.EnsureGameplaySingletons`** — must add `CompassHUD`, `NoteReadUI`, `NoteCollection`.
