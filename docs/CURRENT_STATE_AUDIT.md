# Current State Audit — 2026-05-27

**Branch at time of audit:** `feature/fish-storage-revamp`
**Auditor:** Claude Code (Opus 4.7, 1M context)
**Method:** Direct filesystem inspection, scene-file grep, and parallel-agent exploration of `Assets/3 - Scripts/`, `Assets/3 - Scripts/Scripts/`, `Assets/1.6.7.7.7.unity`, `Assets/MainMenu.unity`, and every top-level `Assets/*` folder. Cross-referenced against the project's `CLAUDE.md`; deviations are flagged inline.

This document has **two parts**:

1. **Part 1 — Comprehensive Game Overview**: a detailed picture of the game as it exists right now.
2. **Part 2 — Scrap Candidates**: every old/unused/abandoned thing the audit surfaced, with honest confidence levels so you can decide what to delete.

A short **Verification Notes** appendix at the bottom records what was and wasn't verifiable in this pass — so you know which claims to trust and which to spot-check yourself.

---

## Table of Contents

**Part 1**
- §1 Build Configuration and Boot Path
- §2 The Solar System
- §3 Player, Ship, Physics
- §4 Floating Origin
- §5 Save / Load System
- §6 Survival Vitals
- §7 Hotbar (7-slot inventory)
- §8 Fishing Loop
- §9 NPCs and Dialogue
- §10 Tev's Quest Arc (`EarlyGameProgress`)
- §11 Vendors (Goods, Ships, Guitar, Fish Market)
- §12 Building Loop
- §13 Combat (Enemies, Ragdolls, Killstreak)
- §14 Concert System
- §15 Map and Compass
- §16 World Streaming (Trees / Mushrooms / Aliens)
- §17 AI Companion (Phone Chat)
- §18 Camera Effects
- §19 Tutorials (Main + Bonus)
- §20 UI Layer
- §21 Cutscenes
- §22 Foundational Script Layer (`Scripts/Scripts/`)
- §23 Auto-Created Singletons (master list)

**Part 2**
- §24 Scrap Candidates — High Confidence
- §25 Scrap Candidates — Review First
- §26 Editor One-Shot Scripts
- §27 The Big Stuff (StreamingAssets, `_Archive/`, `transfer/`)
- §28 Stragglers from Removed Features
- §29 Items Flagged But NOT Recommended for Deletion

**Appendix**
- §A Verification Notes (what was/wasn't checked)

---

# PART 1 — Comprehensive Game Overview

## §1 Build Configuration and Boot Path

The shipped game contains **exactly two scenes** in build settings (verified against `ProjectSettings/EditorBuildSettings.asset`):

| # | Scene | Status |
|---|---|---|
| 0 | `Assets/MainMenu.unity` | **enabled** (launcher) |
| 1 | `Assets/1.6.7.7.7.unity` | **enabled** (gameplay) |
| 2 | `Assets/4 - Scenes/Cutscene.unity` | disabled (referenced from code) |
| 3 | `Assets/4 - Scenes/Flashback.unity` | disabled (referenced from code) |
| 4 | `Assets/4 - Scenes/Flashback1.unity` | disabled (referenced from code) |
| 5 | `Assets/1.8.unity` | disabled (WIP — 15.3 MB on disk) |

**Boot flow:** the build launches `MainMenu`, which displays branding + PLAY / LOAD / NEW GAME buttons. Any of those buttons calls `MainMenuController.EnsureGameplaySingletons()` (seeds `DontDestroyOnLoad` singletons before transition) and then `SceneManager.LoadScene("1.6.7.7.7")`. The Editor usually bypasses this and presses Play directly in the gameplay scene — which is exactly the conditions for the **MainMenu singleton trap** (see CLAUDE.md): a singleton that auto-creates via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` and skips MainMenu must also be seeded in `EnsureGameplaySingletons`, otherwise it works in Editor and is silently broken in the build.

The MainMenu scene is small — root contains `EventSystem`, `MenuRoot` (with `MainMenuPanel` + `SaveLoadPanel` children), `Cleanup` (carrying `MenuSceneCleanup`), and `Main Camera`. No gameplay content; no disabled GameObjects flagged.

Cinematic scenes (`Cutscene`, `Flashback`, `Flashback1`) are disabled in build settings but **must not be deleted** — `CutsceneController.cs`, `InterrogationDialogue.cs`, and `FlashbackManager.cs` hard-reference them by name via `SceneManager.LoadScene()`.

## §2 The Solar System

Eight `CelestialBody` instances live under the `--- Celestial ---` section (verified — section organizer found at line 63380 of the scene file; all 8 body names grep-confirmed in the file):

| Body | Role |
|---|---|
| Sun | Star — non-landable; anchors the artificial-sun override (`LebronLight`) reference |
| Fiery Twin | Hot inner planet |
| Icey Twin | Cold inner planet |
| Humble Abode | Player's home — village, fish market, bakery, start cabin, ship marketplace, two concert stages |
| Constant Companion | Humble Abode's moon — hosts the `MoonBase` (sFuture Modules Pro panels + glass) |
| Cyclops | Big mid-system planet |
| Tumbling Bean | Eccentric tumbling rock |
| Watchful Eye | Outer planet |

Bodies orbit each other under `NBodySimulation` at a fixed 100 Hz physics tick (`Universe.physicsTimeStep = 0.01f`). Both `PlayerController` and `Ship` extend `GravityObject` and apply gravity manually — Unity's built-in gravity is off (`rb.useGravity = false`). Orbital state is **deterministic given starting state**, and the save system snapshots/restores per-body position+rotation+velocity so day/night and orbital phase resume exactly.

**Generation code lives in `Assets/3 - Scripts/Scripts/Celestial/`** — this is the procedural planet pipeline and is forbidden territory per CLAUDE.md (see §22).

## §3 Player, Ship, Physics

**`PlayerController.cs`** (at `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` — note this is in `Controllers/`, not directly under `Game/`) aligns `transform.up` to the nearest body's gravity direction every `FixedUpdate`, giving planet-relative third-person movement. It resets `rb.position` to `spawnPoint` in `Start()` — the save system works around this by deferring `Apply` by 1 frame + 1 `WaitForFixedUpdate`. Exposes `OnLanded` (single-frame land transition for camera FX). Gates equippable / input via `isInDialogue` and `isInModalSlotUI` (the latter was renamed from `isInStorage` in Phase 4 to cover both StorageUI and the fish-staging picker).

**`Ship.cs`** (at `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`) — the single pilotable craft. Caches `targetRot`/`smoothedRot` from the scene-default rotation in `Awake()` and drives `rb.MoveRotation(smoothedRot)` in `FixedUpdate` whenever the ship has zero collision contacts. Saved rotations would snap back unless `Ship.SyncRotationToTransform()` is called after the rotation is applied — `SaveCollector.ApplyShipTransform` does this. `Ship.canFly` gates flight input; `Ship.ForceExitPilot()` exists for the save system to undo `GameSetUp.Start()`'s auto-pilot when the saved state was not piloting.

**The single ship prefab is `SHIP44`**. The old prefab-swap damage system (`Ship_Full`, `Ship_MissingLeft`, `Ship_MissingRight`, `Ship_NoThrusters` + `ShipDamageManager`) has been removed — **verified**: no `Ship_*.prefab` files exist; `ShipDamageManager` is only referenced as a documented comment in `SaveCollector.cs:193` and `SaveCollector.cs:1242` (intentional, explains the removal in the apply-order code).

**Per-part attachment** is tracked by `ThrusterDetachOnImpact` on the ship root, with four parts: `leftAttached`, `rightAttached`, `dishAttached`, `solarAttached`. Each is a serialized child-GameObject toggle. Hard impacts spawn `ThrusterPickup` instances (left/right thruster, satellite dish, solar panel) — these are loose `GravityObjectSimple` rigidbodies with `CollisionDetectionMode.ContinuousDynamic`, `RigidbodyInterpolation.Interpolate`, `useGravity=false`, registered with `EndlessManager.RegisterPhysicsObject(transform)`. Reattachment goes through `PlayerPickup` + `ThrusterMount` + `ShipReassembly`. `PickupMarker` + `PickupUIManager` drive the hover-text and carry visuals.

**Ship HUD modules:** `BoostMeterUI` (up/down/dir thrust), `FlightAssistStatusHUD`, `GForceHUD`, `ShipNameHUD`, `VelocityMarkersHUD`, `DustPopup` (when a SpaceNet drains), `ReactorPopup`, `CrashWarningFader`.

**Intro tutorial:** `TutorialManager` + `TutorialUI` + `TutorialStep` (under `Assets/3 - Scripts/Tutorial/`) begin on `Ship.OnShipCollision` against a `CelestialBody` and step through input lessons defined in `TutorialSteps.cs`. Player presses **Tab** to advance. The old wake-up overlay and 5-min countdown are gone.

**`GameSetUp.cs`** (NOT under `Scripts/Game/` directly per the Glob; it's likely under `Scripts/Scripts/Game/` — needs verification) runs on scene load and auto-pilots the ship; the save system undoes this if the saved state was not piloting.

## §4 Floating Origin

`EndlessManager` shifts all registered physics objects when the camera drifts past `distanceThreshold` (default 1000 units) from world origin. Uses a **two-stage interpolation pipeline** — interpolation is restored 2 `LateUpdate`s after the shift so both `prevPhysicsPos` and `currentPhysicsPos` in Unity's interpolation buffer are post-shift before rendering resumes. Fires `PostFloatingOriginUpdate` after each shift.

Every runtime-spawned physics object **must** call `EndlessManager.RegisterPhysicsObject(transform)` or floating-origin desyncs it. Current registrants include: bobber, thruster pickups, cassette pickups, axe, water bottle, space net pickups, extra purchased ships, ragdoll bones.

## §5 Save / Load System

Lives in `Assets/3 - Scripts/SaveSystem/` (7 files):

- `SaveSystem.cs` — static API (`Save(name)`, `LoadFromDisk(name)`, `Apply(data)`, `ListSaves()`, `DeleteSave(name)`, `GenerateName()`). Reads/writes `Application.persistentDataPath/saves/*.json` via `JsonUtility`.
- `SaveData.cs` — full `[Serializable]` schema (~25 field groups including player / ship / resources / wallet / wood / fishInventory / tutorial / NPCs / buildings / looseParts / cassette / equipment / worldFlags / bonusTutorial / mapTutorial / celestialBodies / alienKills / earlyGame / notes / buildMenuLock / compass / enemies + spawn-cooldown / extraShips / spaceDust).
- `SaveCollector.cs` — `Capture(name)` walks the scene → `SaveData`; `Apply(data)` walks the scene and pushes state back through a strict **17-step apply order** (documented inline in the file). Breaking that order causes regressions.
- `SaveLoadUI.cs` — procedural save/load panel. `sortingOrder = 2000`, `overrideSorting = true`, `RectMask2D` for clipping (NOT Unity's `Mask` — empirically unreliable).
- `PendingLoad.cs` — bridge that schedules a load pre-scene-load and hands the data to `SaveLoadRunner` once `SceneManager.sceneLoaded` fires.
- `SaveLoadRunner.cs` — throwaway MonoBehaviour that defers `SaveSystem.Apply` by 1 frame + 1 `WaitForFixedUpdate` so all `Start()` and the first physics tick complete first.
- `AutosaveManager.cs` — periodic autosave (default 5 min, configurable 1–30 via pause menu) into a dedicated `"autosave"` slot that overwrites every tick.

**Body-relative positions** survive orbital motion: capture is `inv = Quaternion.Inverse(body.rotation); localPos = inv * (worldPos - body.Position); localRot = inv * worldRot; relVel = inv * (worldVel - body.velocity)`. Threshold `kBodyAttachThreshold = 5f * body.radius` — past that, position is saved world-absolute (`bodyName = ""`).

**Always capture from `rb.position`/`rb.rotation` — NOT `transform.position`/`transform.rotation`.** Transform values are the interpolated visual pose and lag physics. Loose thrusters previously respawned slightly inside the terrain collider, where the solver depenetrated them violently.

**Currently NOT saved:** transient combat state (knockback velocity/timer, charge phase, mid-spit attack, ragdolls, tree-episode timer), tree state (chopped trees can respawn — wood count IS saved), map UI state, `TalkToNPCsStep` internal counter, time-in-step counters using real-time timers, ship piloting handoff (a save taken while piloting puts the player at `pilot.spawnPoint` on load), killstreak (intentional), concert state (rebuilds from speakers on scene load).

## §6 Survival Vitals

`ResourceManager` (auto-singleton, `Assets/3 - Scripts/Survival/ResourceManager.cs`) tracks **hunger, thirst, health, ship power**. Health drains faster while hungry/thirsty; ship power drains faster while flying. Death triggers freeze-and-respawn. `WaterBottleController` and food restore values.

Events: `OnHealthDropped(float)` fires **only from discrete `TakeDamage`** (not passive drain) — this is consumed by `CombatFX` for the damage vignette pulse. `OnDeath` fires on death.

HUD: `ResourceHUD` is the legacy 4-row bar stack; `VitalsHUD` is the newer compact vitals card; `WaterFillHUD` shows the water-bottle fill percent when drinking.

## §7 Hotbar (7-slot inventory)

`Hotbar.cs` (auto-singleton, `Assets/3 - Scripts/UI/Hotbar.cs`) is a **7-slot inventory** keyed 1–7 (bumped from 5 in the current `fish-storage-revamp` branch's Phase 1). The hotbar is **table-driven** — internally an `Entry[] _registry` of `(ItemId, DisplayName, IsUnlocked, IsEquipped, ForceEquip, ForceUnequip)` rows; every method (`DetectAcquisitions`, `GetEquipped`, `UnequipAll`, `Equip`, `ItemName`) iterates that array.

**Equippables coexist with resource stacks and per-fish slots.** Equippables: `WaterBottleController`, `FishingRodController`, `GuitarController`, `AxeController`, `PistolController` — all live as sibling MonoBehaviours on the **Player root**. Resource stacks: Wood, Crystal, SpaceDust. Per-fish: one fish per slot, carrying a `FishEntry fishData` payload. Items without a controller in the equippable registry (Wood / Crystal / SpaceDust / Fish / FishBag) are "select-only": pressing the slot's number key sets `_equippedSlot` without spawning anything; `IsSelectOnly(ItemId)` is the predicate gating that path in `ToggleSlot` / `CycleSlot` / `GetEquipped` / `UnequipAll`.

**Important:** slot-active detection uses `_equippedSlot` (the index), not `slot.id == equipped` — the id comparison would falsely highlight every Fish slot at once.

### Fish flow (Phase 2)
Caught fish route into the next empty hotbar slot via `Bobber.cs` → `Hotbar.TryAddFish(entry)`. If full, `InventoryFullPopup.Show()` fires and the catch is destroyed (still logged in the dex). Hold LMB on the equipped Fish slot for 1.0s to eat raw — `Hotbar.TickEatHold` runs in `Update`, paints a screen-center cyan progress ring (`_centerProgressRing`), and on completion fires `RawFishConsumption.Consume(tier)` which restores hunger and starts a `RawFishTripController` kaleidoscope trip. Fish slots use a `RawImage fishPreview` showing a live render from `FishingdexManager.RenderFish(entry, 64, 64)`, cached on `FishEntry.cachedHotbarPreview`.

### Fish bag (Phase 3)
Single-instance non-stackable hotbar item from `Alien7Vendor` (`ShopItemKind.FishBag`, $100). Carries a 5-slot `Hotbar.Slot[] bagContents` payload that travels with the bag through drag/drop. When the bag is present in any hotbar slot, `Bobber.cs` catches route into the bag's first empty internal slot **before** falling back to `TryAddFish`. Right-click the bag slot to toggle a 5-slot vertical side panel docked to the right of the main storage panel. Single-instance enforcement: `Hotbar.HasFishBagAnywhere()` scans hotbar + every `StorageRegistry.All` LootBox.

**Bag icon resolution:** `Hotbar.ResolveFishBagSprite(bagContents)` — `fishingbag.png` (empty) vs `fishingbagfish.png` (≥1 fish), scaled 1.3× in all three slot painters (hotbar, storage, picker). Icon sprites live at `Assets/Resources/HotbarIcons/`.

### Cook + sell staging picker (Phase 4)
`FishStagingUI.cs` (auto-singleton, seeded in `EnsureGameplaySingletons`) is the shared drag-and-drop picker that `BonfireInteraction` and `FishMarketNPC` open via their "Add Fish" buttons. Layout: 10 stage slots (2×5) + 7-slot hotbar mirror + 5-slot bag side panel (RMB toggle) + Confirm + Cancel (Enter / Esc keybinds).

`Open(string title, Action<List<(FishEntry, FishSource)>> onConfirm)` is the caller API. Cancel returns each staged fish via `FishStagingUI.TryReturnTo` — the chain is exact source slot → next-empty-in-source-container → bag → hotbar → destroy+popup. `FishSource { Hotbar.Slot[] container; int index; }` (top-level struct in `SlotOps.cs`) carries origin through the picker AND into the cook/sell stage lists.

The picker snapshots cursor lockState at Open and restores on Close so the parent cook/sell panel keeps its unlocked cursor. The picker does NOT gate on `PlayerController.isInDialogue` (cook + sell both set it true) — only on map / phone / piloted state.

## §8 Fishing Loop

The full loop:

1. Player trades a **cassette tape** (`CassettePickup` → `CassettePlayer`) to **Alien3** (`NPCDialogue` on Alien3) in exchange for the fishing rod.
2. Post-trade dialogue plays, `NPCDialogue.ConversationCompleted` flips, and `FishingRodController.ForceEquipRod()` auto-equips the rod into `autoEquipRodAtIndex`.
3. The rod (`F-Rod.prefab` / `fishing_rod.prefab`) spawns at the camera's `HoldPosAll` anchor and a casting `Bobber.prefab` is fired into water on click.
4. `Bobber.cs` handles physics/buoyancy, then `OnCatch()` populates a `FishEntry` and routes through bag → hotbar fallback → InventoryFullPopup.
5. Caught fish are logged in `FishInventory` (the **lifetime dex**, append-only via `AddFish`).
6. `FishingdexManager` displays every fish ever caught (consumed, sold, destroyed). The dex's eat-raw button was removed in Phase 2 — `RawFishConsumption.Consume(tier)` in `FishInventory.cs` is the single source of truth.
7. Fish are sold via `FishMarketNPC` (Common/Uncommon/Rare staged columns + total + sell button) using `FishStagingUI` to pick which fish to sell.
8. Fish are cooked at any placed bonfire via `BonfireInteraction.cs`. Cook panel: three rarity rows, add-button per row (opens `FishStagingUI`), 10-second cook timer, eat button. Hunger restored: 20 (common) / 35 (uncommon) / 60 (rare).

**Bonus tutorial.** `BonusTutorial.OfferFishing()` — invoked from the fishing flow; sequence CastBobber → CatchFish → SpinCatch → OpenFishingdex → FishingExtraInfo. A bonus tutorial pauses the main `TutorialManager`.

## §9 NPCs and Dialogue

The NPC roster lives in `Assets/3 - Scripts/NPC_Dialogue/`. Pre-placed NPCs are children of celestial-body GameObjects (their parent in the scene hierarchy is generally `Humble Abode`, at line 31278 of the scene file — note: direct grep for `m_Name: Alien3` / `m_Name: TEV` at the root returns no hits; they're nested under planets, which is why a flat scene grep doesn't find them).

| GameObject | Script | Role |
|---|---|---|
| Alien3 | `NPCDialogue` | Trades cassette → fishing rod |
| Alien4 | (fish market vendor) | Fish sell counter (via `FishMarketNPC`) |
| Alien6 | `RandomAlienDialogue` | Random small-talk atmosphere NPC |
| Alien7 | `Alien7Vendor` | Goods merchant — pistol, axe, jetpack, fishing rod, water bottle, fish bag |
| TEV / Alien10 | `TevDialogue` | Father-figure quest-giver; grants axe + pistol; gives village coords |
| BonfireNPC | `BonfireNPCDialogue` + `BonfireInteraction` | Cabin start; tells player to cook fish |
| ShipMarket / Toy1 | `ShipMarketNPC` | Sells whole ships and individual ship parts |
| GuitarShopNPC | `GuitarShopNPC` | Sells the guitar |
| ORG / Interrogation | `ORGDialogue`, `InterrogationDialogue` | Story/cinematic NPCs |
| Streamed aliens | `SpawnedAlienNPC` + `AlienNPCDamageable` | Ambient population spawned by `AlienNPCSpawner` (verified — `AlienNPCSpawner` GameObject found at line 31742 of scene) |

Story-impactful NPCs are tagged via `AlienNPCDamageable.isStoryImpactful` — when killed, their GameObject name is recorded in `AlienKillsSave.killedPrePlacedNames` so save/load reflects the kill.

**Shared dialogue infrastructure:**
- `NPCConversationTracker` fires `OnConversationStarted` when any dialogue starts (used by `TalkToNPCsStep`).
- `PostGreetingChoicePanel` is the shared 2-option Buy / Leave panel used by vendors.
- `DialogueTextStyling` provides `RevealCharsTMP(...)` and `RevealCharsLegacy(...)` — **zero-allocation typewriter** using TMP's `maxVisibleCharacters` instead of `text += c` (which is O(n²) string concat).
- `NPCWaveAnimation` computes arms-at-sides bone rotations geometrically at runtime and enforces them in **`LateUpdate`** so they win over the Animator. Any future NPC bone-manipulation script must use `LateUpdate`.

## §10 Tev's Quest Arc (`EarlyGameProgress`)

`TevDialogue` (on TEV/Alien10 at Humble Abode) drives the early-game story arc. State is derived directly from `EarlyGameProgress` flags (static class, persisted via `EarlyGameProgressSave`):

```
Phase 1: NoteRead (read the starting note in StartCabin)
Phase 2: RodPickedUp → FirstFishCaught → OneOfEachCaught
Phase 3: FirstMealEaten
Phase 4: WaterBottleDrunk           ← Tev's collider becomes interactable here
Phase 5: ReturnedHome → TevReturnedDialogueDone (Tev gives axe + pistol unlocks; auto-equips axe)
Phase 6: CabinBuilt                 ← player must build a cabin via BuildMenuUI
Phase 7: VillageCoordsGiven (Tev adds village waypoint to compass)
Phase 8: FishVendorVisited, GoodsVendorVisited
```

Story-arc placeholder: `ORG_Reveal` (no phase assigned yet; flipped by F9 cheat key / future story trigger; gates the AI's ORG-aware knowledge file).

**Adding a new flag** = one new field in `EarlyGameProgress.cs` + one matching field in `EarlyGameProgressSave` + capture/apply lines in `SaveCollector`.

## §11 Vendors

### Alien7Vendor (Goods)
Canonical goods merchant. Trigger + F-prompt + typewriter mirrors `BonfireNPCDialogue`; left-click after the greeting opens `GoodsVendorShopUI`. Procedural UI build (warm copper palette to differ from FishMarket's teal). Item images render live via a runtime preview camera (same shape as `FishingdexManager.SetupPreviewCamera`); cached per-item.

Current inventory: pistol, axe, jetpack, fishing rod ($50), water bottle ($30), fish bag ($100). The last three are Phase 3 additions providing alt-path acquisition for early-game players who skip the NPC arc.

**Adding a new sellable item:** (1) right-click → Create → Game → Shop Item, fill fields, assign `previewPrefab`; (2) add a new `ShopItemKind` enum value; (3) add cases to `Alien7Vendor.IsAlreadyOwned` and `GrantItem` that call the relevant controller's `Unlock`/`ForceEquipX`; (4) drop the asset into the vendor's `inventory` array.

### ShipMarketNPC (Ships + Parts)
At Humble Abode/ShipMarket. Opens `ShipMarketShopUI`. Inventory comes from `ShopItem` assets (one `ShopItemKind` per slot).

- **Whole ship** (`ShipFull`, `ShipNoDish`, `ShipHull` — stored in `ExtraShipSave.tier`): spawns `shipPrefab` 30 m behind the vendor, 3 m up, oriented upright. Tagged `BoughtShip`. Each spawned ship gets a monotonically increasing `shipNumber` (first bought = Ship 1, never re-numbered across save/load).
- **Ship part** (`PartLeftThruster`, `PartRightThruster`, `PartDish`, `PartSolarPanel`, `SpaceNetLeft`, `SpaceNetRight`): spawns the corresponding pickup prefab in front of the vendor and auto-equips via `PlayerPickup`.
- **Goods**: routed through the standard hotbar `Unlock` flow.

### Fish Market (FishMarketNPC)
Runs the sell flow with `SellPanel` (Common/Uncommon/Rare staged columns + total + sell button). Stages fish via `FishStagingUI`.

### Guitar Shop (GuitarShopNPC)
Sells the guitar with a choice panel.

### Space Dust sub-panel
`SpaceDustSellUI` is exposed by some NPCs (`NPCSellDustOption`) and drains the player's space-dust into cash.

### Space dust & space nets economy
- `SpaceDustInventory` (auto-singleton) tracks `Count` and `HasFilter` (one-time unlock).
- `SpaceNet` (on a child of a `Ship`): when the owning ship is parked in orbit (not piloted, not landed, and within `body.radius * 1.05 .. body.radius * 5` of the nearest body), the net buffers `dustPerSecond` × altitude multiplier. Cap is `bufferCapacity` (default 500). Press **F** standing in the net's trigger to drain into `SpaceDustInventory`.
- `SpaceNetMount` + `SpaceNetMountController` + `SpaceNetPickup` handle mount/detach.
- Saved per ship as `SpaceNetSave { shipNumber, netIndex, buffer, attached }` inside `SpaceDustSave.nets`. `shipNumber=0` = scene's original ship; non-zero = a `BoughtShip.shipNumber`.

## §12 Building Loop

`BuildMenuUI` (key **N**) lists `BuildableEntry` recipes; `GhostPlacement` shows the rotatable preview; LMB places, RMB rotates. Placed instances are renamed `<prefab>_Placed` and parented to the planet's `CelestialBody` transform — **this exact suffix is what the save system uses to find/destroy/restore placed buildings**, so don't change the convention. `GhostPlacement.OnPlaced` fires after placement (used by `BuildBonfireStep` / `BuildCabinStep`). Bonfires placed via building automatically get a `BonfireInteraction` component (the save system rebuilds this on load by copying refs from another bonfire).

**`BuildMenuLock`** gates which blueprints show in the menu. `BuildMenuLockSave.isLockingActive=false` shows everything; when true, only entries whose `displayName` is in `unlockedNames` appear. The tutorial / Tev story flow unlocks blueprints progressively (e.g. Cabin unlocks after `WaterBottleDrunk`).

## §13 Combat

`EnemyController` (per-enemy) + `EnemySpawner` (singleton) + `EnemyHealthBar` UI. Damage sources:
- `AxeController.ApplyHit(EnemyController, Vector3)` — melee, 34 dmg, 3 hits to kill.
- `PistolController.TriggerShot()` — hitscan, 50 dmg, 2 shots to kill.

Both call `enemy.TakeDamage(float)` + `enemy.ApplyKnockback(direction, distance, duration)`. `EnemyController` maintains a static `ActiveEnemies` list — iterate that, not `FindObjectsOfType`. Each prefab declares its `EnemyKind` (`Regular` / `Elite`) via a serialized field — Toy10 = Regular, Toy3 = Elite (these prefabs come from the Cursed_Toys_II pack).

**Saved:** kind + planet-relative position + current health for active enemies (re-instantiated through `EnemySpawner.SpawnFromSave`), spawner's `enemySpawnTimer` + `enemyRegularsSinceElite` (so save-cycling can't reset spawn timing).

**NPC contact damage:** Enemies damage `AlienNPCDamageable` on contact (`npcContactDamage`, 3 hits at default), so an enemy stuck on an NPC bites through instead of permanently blocking the lane to the player.

**Spit attack.** `SpitProjectile` is the enemy ranged attack. When the player climbs a tree, enemies switch to spit-standoff locomotion (back up to `spitStandoff{Min,Max}`) but only **arm** spit cycles after 10s of continuous tree-episode time. The episode state lives on `PlayerTreeContactTracker` (auto-singleton, created on first `EnemyController.Start`) — jumping in place / tree-to-tree preserves the timer.

**Ragdolls.** `EnemyRagdollBuilder` (enemy prefabs) and `AlienRagdollBuilder` (NPC prefabs) construct ragdolls at runtime on death. `RagdollGravity` per-body applies gravity to detached bones; `RagdollBoneRegistry` maps bones for floating-origin re-registration. Ragdoll state is **not saved**.

**Torch Aura.** Every `Torch` placed via the build menu (and pre-placed scene torches) carries a `TorchAura` component: 15 m sphere where enemy spawning is blocked and any enemy inside takes `damagePerSecond` 20 dmg/s. Multi-instance — the spawner checks every active torch on every spawn attempt.

**Killstreak.** `KillstreakManager` (auto-singleton) tracks consecutive enemy kills with a decay timer that shrinks per tier (10s at x1, dropping to 1s at x11+). `KillstreakHUD` displays the banner (DOUBLE / TRIPLE / … / WICKED SICK). `SlowmoOnKill` (camera-FX module) listens to `EnemyController.OnAnyEnemyDeath` and applies a brief time-scale drop. Streak resets on death and scene reload (intentional).

## §14 Concert System

`ConcertStageHub` (auto-singleton, scripts in `Assets/3 - Scripts/Concert/`) discovers every `SpeakerSource` in the scene and binds each to a `CelestialBody`. Per-stage state:

- **Night gating.** A stage runs only when `dot(stageDir_from_body_centre, sun_direction) < nightDotThreshold` (default 0.05) — i.e., on the night side. Two stages on opposite poles (`STAGEGOOD`, `STAGEGOOD2`) swap on/off automatically with the planet's rotation.
- **Auto-cloning.** If a speaker has no `AudienceZone` sibling, the hub clones the existing one + its `AudienceSpawner` and places the clone `clonedZoneInFront` metres in front. Duplicating a stage in the editor "just works."
- **LebronLight override.** If a `LebronLight` (the ship's artificial sun) is within `lebronLightOverrideRadius` of a stage's speaker, the stage is forced into DAY mode regardless of planet rotation.
- **No-enemy zone.** Static `IsBlockedForEnemy(Vector3)` returns true for points within `enemyBlockRadius` of any speaker. `EnemySpawner` calls this on every spawn. Enemies inside take `enemyDamagePerSecond` (default 40) — independent of whether the stage is currently active.

Stage props live under `STAGEGOOD` / `STAGEGOOD2` in Humble Abode: stage geometry, truss, lasers ×4 (`ConcertLaser`), blinders (`ConcertBlinder`), haze (`ConcertHaze`), fog puffs (`ConcertFogPuff`), cone lights (`ConcertConeLight` + `ConcertConeBeam` shader), strobes (`ConcertStrobeLight`), speakers (`SpeakerSource`), `ConcertLightProgram` runs chase patterns, `ConcertAudioDirector` swaps tracks. Custom shaders: `ConcertAdditive`, `ConcertConeBeam`, `ConcertGlowSphere`. Audience members (`AudienceMember` + `AudienceMemberDeathWatcher`) spawn from the audience zone and are killable.

## §15 Map and Compass

`SolarSystemMapController` (auto-singleton, key **M**) opens a top-down system map. Supporting scripts: `MapCameraRig`, `MapHighlightRing`, `MapLegendUI`, `MapOrbitLines`, `MapVelocityHud`, `MapTeleportToPilotButton`. `MapBootstrapReal` builds the representation from live `CelestialBody` data. `MapTutorial` (6-step linear tutorial, persists via `MapTutorialSave`) fires on first map open.

`CompassHUD` (auto-singleton) is a Skyrim-style top-center strip. Waypoints register via `AddWaypointByTag(tag)` — the tag resolves a scene `Transform` whose world position is queried every frame. Tag-based waypoints persist via `CompassSave.waypoints`; dynamic `Func<Vector3>` waypoints do not. Used by tutorial steps and by `TevDialogue` (village waypoint).

## §16 World Streaming

Three streaming spawners run from `--- Managers ---`:

| Spawner | Per-cell prefab | Streaming radius | Cap (InputSettings) |
|---|---|---|---|
| `TreeSpawner` | tree prefab(s) | varies per body | `inputSettings.maxTrees` |
| `MushroomSpawner` | one of `mushroomPrefabs[]` (deterministic per cell) | 300 m | `inputSettings.maxMushrooms` |
| `AlienNPCSpawner` | one of `alienPrefabs[]` (deterministic per cell) | 300 m | `inputSettings.maxAlienNPCs` |

Per-cell deterministic seed reproduces world layout across runs. `consumedCells` tracks cells the player has destroyed/picked-up so they don't repopulate. The pause menu exposes a slider per cap; spawners poll `inputSettings.maxX` every tick so changes are live. `SpawnedTree` (chopping yields wood via `WoodInventory`) and `SpawnedAlienNPC` are the per-instance marker components. `WoodPopup` shows "+1" on chop.

**Mushrooms can be eaten** (`MushroomInteraction.Eat`) and trigger a `RawFishTripController.StartTrip` envelope — eating mushrooms is the primary way to acquire/intensify the trip effect.

**A new `CrystalSpawner.cs` and `SpawnerCubeface.cs` are in the modified-file list** (git status), suggesting a Phase-on-current-branch addition of crystal harvesting + a cubeface-based spawn distribution system. The `CrystalInventory.cs` script under `Player/` confirms a third resource type is being added alongside wood + space dust.

## §17 AI Companion (Phone Chat)

The phone-AI feature uses a local `llama.cpp` backend (LlamaLib, 3.96 GB shipped under `Assets/StreamingAssets/LlamaLib-v2.0.5/`). Scripts in `Assets/3 - Scripts/AI/`:

- `LLMService.cs` — the backend driver. **Default is CPU + Qwen-2.5-3B-Instruct Q4_K_M** (4096 ctx), gated by `static readonly bool UseGPU = false;` at the top of the file. Flip to true to revert to **GPU + Hermes-3-Llama-3.1-8B Q4_K_M** (vulkan backend, `numGPULayers=99`, `libraryExclusion={tinyblas,noavx,avx}`, 8192 ctx).
- `AIChatScreen.cs` — procedural chat UI built inside the phone's `_pageHostRT` with message display + typing indicators.
- `AIMemoryStore.cs` — long-term memory singleton (cap 200 memories), rolling buffer of chat turns. Seeded in `EnsureGameplaySingletons` for the MainMenu trap.
- `AIMemoryExtractor.cs` — post-conversation compaction using LLM to distill transcript into structured `AIMemory` entries.
- `GameKnowledgeBase.cs` — loads `StreamingAssets/AI/game_knowledge.md`, parses into PERSONA and ENTRY blocks, hot-reloads in Editor. Merges `game_knowledge_org_reveal.md` when `ORG_Reveal` flips.
- `AIStoryController.cs` — polls `EarlyGameProgress.ORG_Reveal`; merges the gated knowledge file + advances `StoryPhase` on rising edge.
- `FleetTelemetry.cs` — builds the FLEET STATE block for the system prompt (enumerates scene ships with location, motion, attachments, dust buffers).

The repo also has `Assets/3 - Scripts/AI/HALCommentator.cs` (in the git-status modified list) — purpose not directly verified by this audit; appears to be a HAL-9000-style commentary system.

**Knowledge gating** is documented in `docs/superpowers/plans/2026-05-23-ai-companion-knowledge-gating-and-org-placeholder.md`. The CPU/GPU backend swap is documented in `docs/superpowers/plans/2026-05-23-phone-ai-laptop-gpu-fix.md`. On the CPU path, the `Player.log` `[LLMService] === BACKEND PROBE END ===` line should contain `tinyblas`/`avx`/`noavx`; on the GPU path, `vulkan` or `cublas`.

The shipped game StreamingAssets includes `game_knowledge.md` (19.2 KB), `game_knowledge_org_reveal.md` (6.8 KB), the `voice/` folder (76 MP3 voice lines), and **LlamaLib-v2.0.5/** (3,962.8 MB of multi-platform native binaries — by far the biggest single thing in the project). Earlier-iteration model files (`qwen2.5-1.5b-instruct-q4_k_m.gguf`, `qwen2.5-3b-instruct-q4_k_m.gguf.meta`, `Hermes-3-Llama-3.1-8B-Q4_K_M.gguf.meta`) are marked as deleted in `git status` — leftover `.meta` stubs may still need cleanup (verify; the asset-folder agent didn't flag any).

## §18 Camera Effects

`CameraEffectsManager` (`Assets/3 - Scripts/Camera/CameraEffectsManager.cs`) is a procedural singleton (auto-created + seeded in `EnsureGameplaySingletons`) that owns the camera-effects modules:

| Module | Driver |
|---|---|
| `CameraTransformFX` | headbob, strafe tilt, landing dip, death tilt |
| `CameraFOVFX` | sprint/jetpack/ship-boost FOV stack |
| `CombatFX` | damage flash/vignette/shake/death tilt |
| `VignetteOverlay` | multi-driver: baseline, damage pulse, low-health pulse, dialogue focus, death dim |
| `DamageFlashOverlay` | red flash on hit |
| `LetterboxBars` | cinematic bars |
| `SpeedLinesOverlay` | motion streaks (jetpack/sprint/ship boost) |
| `FilmGrainOverlay` | grain |
| `ChromaticAberrationOverlay` (+ `ChromaticAberrationEffect` shader) | RGB fringing |
| `LensDirtOverlay` | lens dust |
| `MoodColorGrade` | global color grade |
| `AnamorphicStreaks` | lens flare streaks |
| `LensFlareRegistry` (+ `LensFlareAdditive` shader) | centralized flare sources |
| `SlowmoOnKill` | time-scale dip on enemy kill |
| `RadialMotionBlurEffect` (+ shader) | radial blur |
| `CameraShake` | trauma-driven shake |

All toggles + intensity sliders live on `InputSettings.fx*` (per-effect on/off plus a master `cameraEffectsEnabled` kill-switch) and are exposed in the `TabbedPauseMenu` **CAMERA** tab. Each module polls those flags every frame so the menu is live-tunable.

**Atmosphere/ocean post-process gotcha.** `CustomPostProcessing.OnRenderImage` is `[ImageEffectOpaque]` — atmosphere/ocean run *after* opaque geometry but *before* transparent. Anything in the Transparent queue (≥ 2500) draws on top of the atmosphere/water and "shows through" them, even when on the far side of a planet. **To be hidden behind atmosphere/water, a material's render queue must be ≤ 2500.** See `Assets/sFuture Modules Pro/Materials/Glass_EarlyQueue.shader` (used by the moon base glass) for the working pattern.

## §19 Tutorials

`TutorialManager` + `TutorialUI` + `TutorialStep` (under `Assets/3 - Scripts/Tutorial/`). Begins on `Ship.OnShipCollision` against a `CelestialBody` and steps through input lessons (defined in `TutorialSteps.cs`); player presses **Tab** to advance.

**Bonus tutorials** run separately from `TutorialManager`:
- `BonusTutorial.OfferAxeBuilding()` — invoked by axe-NPC; SwingAxe → GatherWood (60) → BuildBonfire → BuildCabin
- `BonusTutorial.OfferFishing()` — invoked from fishing flow; CastBobber → CatchFish → SpinCatch → OpenFishingdex → FishingExtraInfo

A bonus tutorial pauses the main `TutorialManager`. State is identified by `_activeHeader` (`"AXE / BUILDING"` / `"FISHING"`) — when adding a new bonus tutorial, add a key to `BonusTutorial.GetActiveTutorialKey()` AND a factory case in `BonusTutorial.ApplySaveState()` so it persists across save/load.

**`_LegacySteps.cs`** (verified present at `Assets/3 - Scripts/Tutorial/_LegacySteps.cs`) is a single file containing legacy `TutorialStep` subclasses (e.g. `PostCrashExamStep`, `StandUpStep`) that are **no longer instantiated** by `BuildDefault()` or `BonusTutorial`, **but** are kept because `TutorialManager.ApplyState` resolves saved tutorial progress by type name. The file header explicitly says "Do NOT rename or delete these types — old save files reference them by name." **This is not orphan code** despite the `_Legacy` prefix.

## §20 UI Layer

Major scripts in `Assets/3 - Scripts/UI/`:
- `Hotbar.cs` (covered in §7)
- `FishStagingUI.cs` (covered in §7)
- `MainMenuController.cs` — launcher; owns `EnsureGameplaySingletons()`.
- `TabbedPauseMenu.cs` — pause menu with tabs (CAMERA / SETTINGS / AUDIO / VIDEO / GRAPHICS), camera FX toggles, streaming cap sliders, map/save/load access.
- `CompassHUD.cs` (covered in §15).
- `KillstreakHUD.cs`, `InteractPromptUI.cs`, `NoteReadUI.cs`, `AutoAlignToggleUI.cs`, `ControllerUINavigator.cs`, `HUDSceneGate.cs`, `MenuSceneCleanup.cs`.
- `ScannerFrame.cs` + `CyanScannerPalette.cs` — shared "cyan scanner" visual language used by FishingdexManager, BuildMenuUI, GoodsVendorShopUI.
- `GalaxyHudKit.cs` / `GalaxyHudStyler.cs` / `GalaxyPauseMenuStyler.cs` — galaxy-themed styling.
- `UIPanelSprites.cs` — shared rounded-rect panel sprites.
- `UILayer.cs` — canvas sorting-order constants (tutorial 500, FX 800–820, pause 1000, save/load 2000).
- `HudFontResolver.cs` — TMP font resolution.
- `SkipControllerNav.cs`, `StoryImpactNotice.cs`.

A new `PlayerPhoneUI.cs` is in the modified-file list (git status) — likely the in-world phone surface that hosts `AIChatScreen`.

## §21 Cutscenes

`Assets/3 - Scripts/Cutscenes/` — `CutsceneController.cs` and `FlashbackManager.cs`. Scenes:
- `Assets/4 - Scenes/Cutscene.unity` — referenced from `CutsceneController.cs`, `InterrogationDialogue.cs`, `Editor/CreateCutsceneScene.cs`
- `Assets/4 - Scenes/Flashback.unity` — referenced from `FlashbackManager.cs`
- `Assets/4 - Scenes/Flashback1.unity` — `FlashbackManager.cs` default `nextSceneName`

All three are disabled in build settings but actively referenced by code. Do not delete.

## §22 Foundational Script Layer (`Scripts/Scripts/`)

This is the foundational layer under `Assets/3 - Scripts/Scripts/`. Inventoried via Glob; selected highlights:

### `Scripts/Celestial/` — FORBIDDEN ZONE (read-only)
Procedural planet generation. **Verified inventory**:

- Top level: `CameraUtility.cs`, `CelestialBodyGenerator.cs`, `CelestialBodySettings.cs`, `Normalizer.cs`, `OceanSettings.cs`, `SphereMesh.cs`
- `Editor/` — `GeneratorEditor.cs`, `TextureCombinerEditor.cs`, `TextureEditor.cs`
- `Effects/` — `AtmosphereEffect.cs`, `AtmosphereSettings.cs`, `OceanEffect.cs`, `PostProcessingEffect.cs`
- `NoiseSettings/` — `CraterSettings.cs`, `RidgeNoiseSettings.cs`, `SimpleNoiseSettings.cs`
- `SceneCam/` — `SceneCamManager.cs`, `Editor/SceneCamEditor.cs`
- `Shading/` — `AlienShading.cs`, `ColourHelper.cs`, `EarthShading.cs`, `MoonShading.cs`, `TestShading.cs`
- `Shape/` — `AlienShape.cs`, `CelestialBodyShape.cs`, `EarthShape.cs`, `MoatShape.cs`, `MoonShape.cs`, `ShatteredShape.cs`
- `Test/` — `RayStepTest.cs`
- `Texture Gen/` — `NoiseGenerator.cs`, `SpotsTexture.cs`, `TextureCombiner.cs`, `TextureGenerator.cs`, `TextureViewer.cs`

**This entire folder is forbidden territory** per CLAUDE.md. A previous session broke a working build by editing here — killed the directional light and removed the top grass layer; damage unrecoverable. Read-only inspection is fine; modification is not. Shaders, .compute, .hlsl, planet/ocean materials in `Assets/2 - Materials/` are all part of the same forbidden bundle.

### `Scripts/Game/` — core gameplay (mixed)
**Verified files at this level**: `Atmosphere.cs` (FORBIDDEN — wires atmosphere to camera effects), `BodyPlaceholder.cs`, `CustomImageEffect.cs` (FORBIDDEN — image-effect base for atmosphere/ocean hooks), `GravityObject.cs`, `LODHandler.cs`, `MeshBaker.cs`, `NBodySimulation.cs`, `OrbitDebugDisplay.cs`, `PlanetTest.cs` (test), `SolarSystemSpawner.cs`, `StarDome.cs`, `StarTest.cs` (test).

**Subfolders verified:**
- `Controllers/` — **`PlayerController.cs`, `Ship.cs`, `InputSettings.cs`**. (CLAUDE.md groups these under "Scripts/Game/" but they actually live in `Controllers/`. Worth correcting the doc.)
- `Interactions/`
- `Lighting/`
- `UI/` — `GameUI.cs`, `SettingsMenu.cs`, `LockOnUI.cs`. (`SettingsMenu` is what hard-codes `SceneManager.LoadScene("MainMenu")` for the "return to main menu" button.)
- `Utility/`
- `Test/`
- `Debug/` — contains a fully-bundled **Triangle.NET** library at `Debug/Debug Viewer/Libaries/Triangle/` (~50+ files including Geometry/, IO/, Logging/, Meshing/Algorithm/, Meshing/Data/). Note the folder typo `Libaries` (not `Libraries`). The library is computational-geometry support for whatever uses `Debug Viewer/Visualizer.cs` — but the visualizer is dev-only and its actual current usage is unclear (good candidate for the user to verify; if the visualizer isn't actively used, the bundled library is dead weight).

### `Scripts/Post Processing/`
Per CLAUDE.md, `Planet Effects/` subfolder is forbidden (couples to the planet pipeline); `Bloom/`, `FXAA/`, `OceanMaskRenderer.cs` are fine. Not deeply re-inventoried this pass — trust CLAUDE.md.

### `Scripts/Script Utilities/`
**Verified inventory**: `ComputeHelper.cs`, `MathUtility.cs`, `PRNG.cs`, `ShaderHelper.cs`, `TextureHelper.cs`. Five files. All actively used by both forbidden Celestial code and gameplay code.

## §23 Auto-Created Singletons (master list)

Per CLAUDE.md, the following all auto-spawn via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` and `DontDestroyOnLoad` themselves. Most skip creation when the active scene is `MainMenu` — and **every one of those must also be seeded in `MainMenuController.EnsureGameplaySingletons()`** or it'll be missing in builds (the MainMenu singleton trap).

| Singleton | File |
|---|---|
| `PlayerWallet` | `Player/PlayerWallet.cs` |
| `WoodInventory` | `Player/WoodInventory.cs` |
| `SpaceDustInventory` | `Player/SpaceDustInventory.cs` |
| `CrystalInventory` | `Player/CrystalInventory.cs` (new on this branch) |
| `ResourceManager` | `Survival/ResourceManager.cs` |
| `Hotbar` | `UI/Hotbar.cs` |
| `FishStagingUI` | `UI/FishStagingUI.cs` |
| `TutorialUI` | `Tutorial/TutorialUI.cs` |
| `BonusTutorial` | `Tutorial/BonusTutorial.cs` |
| `CompassHUD` | `UI/CompassHUD.cs` |
| `CameraEffectsManager` | `Camera/CameraEffectsManager.cs` |
| `KillstreakManager` | `Combat/KillstreakManager.cs` |
| `PlayerTreeContactTracker` | `Combat/PlayerTreeContactTracker.cs` |
| `ConcertStageHub` | `Concert/ConcertStageHub.cs` |
| `RawFishTripController` | `Effects/RawFishTripController.cs` |
| `AutosaveManager` | `SaveSystem/AutosaveManager.cs` |
| `NoteCollection` | `Player/NoteCollection.cs` |
| `GameKnowledgeBase` | `AI/GameKnowledgeBase.cs` |
| `AIStoryController` | `AI/AIStoryController.cs` |
| `AIMemoryStore` | `AI/AIMemoryStore.cs` |
| `LLMService` | `AI/LLMService.cs` |

**Scene-bound (not auto-spawned but live in `--- Managers ---`):** `NBodySimulation`, `EndlessManager`, `EnemySpawner`, `TreeSpawner`, `MushroomSpawner`, `AlienNPCSpawner`, `CrystalSpawner` (new), `FishingdexManager`, `PickupUIManager`, `SolarSystemMapController`, `MapTutorial`, `NPCConversationTracker`, `TutorialManager`.

---

# PART 2 — Scrap Candidates

Organized by confidence so you can act with appropriate caution. Every claim here is backed by something I verified this pass — not just CLAUDE.md restatement.

## §24 Scrap Candidates — High Confidence

These are safe to delete. Re-verifiable if you ever change your mind (git history preserves them).

### Orphan `.meta` files in `StreamingAssets/AI/`
Git status shows three deleted files: `Hermes-3-Llama-3.1-8B-Q4_K_M.gguf.meta`, `qwen2.5-1.5b-instruct-q4_k_m.gguf` (the file), `qwen2.5-1.5b-instruct-q4_k_m.gguf.meta`, `qwen2.5-3b-instruct-q4_k_m.gguf.meta`. These are already marked as deleted in git but may have leftover stubs in the working tree — confirm with `git status`, then `git rm` if any remain. The current default backend is the Qwen-2.5-3B (CPU) model + Hermes-3-Llama-3.1-8B (GPU); the 1.5B model is obsolete.

### `Assets/3 - Scripts/JitterDiagnostic.cs`
**Verified contents** (file header):
> TEMPORARY DIAGNOSTIC — delete this file once the jitter is found.

The file even has `const bool EnableAutoCreate = false;` as a hard-off gate. It's not even running. Author intent is explicit. Safe delete. (If you ever need jitter diagnostics again, recover from git history — the comments explaining what each field means are valuable enough to keep accessible.)

### `Assets/4 - Scenes/_Archive/Planet.unity` (63.6 MB)
The scene with the anomalously huge size (4× the size of any other archived scene) — flagged by the asset-folder audit. Almost certainly a one-off planet-generation test scene from an early iteration. If you can't remember what it's for, it's safe to delete. 63 MB recovered.

### `Assets/4 - Scenes/_Archive/Solar System.unity` (14.0 MB)
Similar to above — an early archived snapshot. If you have the 1.0–1.7 progression archived, this one is likely a parallel branch or even-older snapshot. Safe delete if you don't recognize it.

## §25 Scrap Candidates — Review First

These look unused but warrant a 60-second sanity check before deletion.

### `Assets/4 - Scenes/_Archive/` (416 MB total, 44 files including 22 `.unity` + 22 `.meta`)
Full version history from 1.0 through 1.7 with descriptive commit-message-style names. Useful as **historical reference** but functionally dead. Decision is purely emotional: keep them as a souvenir of the dev journey, or delete to reclaim ~400 MB.

**Bonus tip:** if you're not sure which ones to keep, the audit found one interesting label: `1.6.5 "goateddddd"` — at least keep that one as a milestone.

### `Assets/transfer/` (73.6 MB)
Confirmed dumping-ground folder. Contents inventoried:

| Item | Size |
|---|---|
| `SolarPanel` | 36.5 MB |
| `audio` | 33.3 MB |
| `Acronym Studio - Wooden Houses Free` | 3.1 MB |
| `Bonfire.prefab` | 0.46 MB |
| `WoodFriction.physicMaterial` | <1 KB |

`SolarPanel` (36 MB!) is the biggest item. The active solar panel is part of the ship via `SolarPanelCharger.cs` + a child of SHIP44; this `transfer/SolarPanel` is likely a legacy model from before the current ship-parts system. `Acronym Studio - Wooden Houses Free` is a third-party pack that was extracted to `transfer/` instead of imported properly — evaluate whether you actually use any houses from it (a grep of script references for "Wooden Houses" or "Acronym" returned nothing in the audit). `Bonfire.prefab` is suspicious — the active bonfire system uses prefabs from a different location, so this is probably stale. `WoodFriction.physicMaterial` is orphan unless something references it.

**Action:** open in editor, decide per-item. Likely 60+ MB recoverable.

### `Assets/3 - Scripts/Editor/EnableFrameTimingStats.cs`
**Verified active**: `[InitializeOnLoad]` that turns on `PlayerSettings.enableFrameTimingStats` so the FPSOverlay's "gpu" line works. This is a one-line idempotent setup tool — NOT recommended for deletion. Listed in this section because git status shows it as untracked (you may have just added it and not committed yet — `git add` it).

### `Assets/3 - Scripts/PerfBootstrap.cs`
**Verified active**: `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` that strips Debug.Log stack-trace capture to prevent ~7 KB/log allocation. Fixes a TMP-lazy-init stutter. **Keep — this is doing real work**. Listed because git status shows it untracked (commit it).

### `Assets/1.8.unity` (15.3 MB, disabled in build settings)
The WIP scene per CLAUDE.md. If you've completely abandoned the WIP, delete it; if you still plan to return to it, leave it. The asset-folder agent confirmed no scripts reference it.

### `Assets/3 - Scripts/Editor/LensFlare*.cs` (four files)
`LensFlareProbe.cs`, `LensFlareForceUpdate.cs`, `LensFlareDeepProbe.cs`, `LensFlareForceToggleOn.cs`. Four lens-flare debug/diagnostic utilities. That's a lot. Most are likely one-shot probes you wrote while debugging a single issue and never touched again. The active runtime system is `LensFlareRegistry.cs` (in `Camera/`) and `LensFlareAdditive.shader` — the Editor probes are not in the runtime path. **Recommend**: keep `LensFlareProbe.cs` (the simplest one — useful for future debugging), delete the other three.

### `Assets/3 - Scripts/Editor/RunSetupConcertStageLights.cs`
`SetupConcertStageLights.cs` is the actual setup script; `Run...` is presumably the menu-item wrapper. If `Setup...` already has its own `[MenuItem]` attribute, the `Run...` wrapper is redundant. Quick check before deleting.

### `Assets/3 - Scripts/Editor/VerifyConcertSetup.cs`
Verification utilities are usually one-shot. If the concert setup is stable now (it should be — the system is fully built per §14), this is probably dead weight. Open it; if it's clearly a one-time check, delete.

### `Assets/3 - Scripts/Editor/SunRayProbe.cs`
Similar story — likely a one-shot probe for a specific debugging session.

### `Assets/3 - Scripts/Editor/InspectBeamMaterials.cs`
Likely a one-shot inspection utility for the concert beam materials. If the beam materials are stable, dead weight.

### `Assets/Materials/` (top-level, distinct from `Assets/2 - Materials/`)
Found via asset audit: 4 actual materials (Fishingline, New Material, red, white) plus subfolders `HotbarIcons/`, `SpaceNet/`, `Torch/`. `New Material.mat` is the classic Unity "I didn't rename this" leftover — safe delete after confirming nothing references it. `red.mat`/`white.mat` might be debug shading — check.

### `Assets/4 - Scenes/_Archive/` `.meta` files for missing scenes
The 44 files in `_Archive/` are 22 `.unity` + 22 `.meta`. If you delete any of the `.unity` files, also delete the matching `.meta`.

## §26 Editor One-Shot Scripts

The `Editor/` folder is a mix of permanent reusable tools and one-shot fix-it scripts that ran once during a migration and were never needed again. Verified file list (29 scripts):

**Likely-permanent (keep):**
- `BuildWindowsPlayer.cs` — build pipeline
- `CreateCutsceneScene.cs` — cutscene scene scaffolder
- `HotbarIconRenderer.cs` — runtime preview camera (used by vendor UIs)
- `LoadGameplaySceneInPlayMode.cs` — dev shortcut to skip MainMenu
- `EnableFrameTimingStats.cs` — keeps PlayerSettings flag on (idempotent)
- `PlayModeDiagnostic.cs` — diagnostic reporting in play mode
- `LensFlareProbe.cs` — single representative lens-flare debug tool

**Likely one-shot (probably done with):**
- `AddAxeAndJetpackShopItems.cs` — added once during vendor setup
- `AddBonfireMeshColliders.cs` — collider fix-up pass
- `AssignMushroomPrefabs.cs` — prefab assignment pass
- `AttachTorchFireToBonfire.cs` — fire prefab wiring
- `BuildToy3EnemyPrefab.cs` + `BuildToyEnemyPrefab.cs` — enemy prefab construction passes
- `FixCursedToysMaterials.cs` — material import fix for the Cursed_Toys_II pack
- `FixFishingRodPrefab.cs` — rod prefab fix (NOTE: this file references hardcoded paths to `Assets/1 - samsPrefabs/F-Rod.prefab` and `Assets/5 - External Imports/fishing-rod/source/.../fishingrod.obj`; if you delete the script, the paths it references stop mattering)
- `FixUpKitPrefabs.cs` — kit prefab fixes
- `PopulateBuildablesFromKit.cs` — buildable population pass
- `RebuildTorchFire.cs` — torch fire rebuild pass
- `SetupConcertStageLights.cs` + `RunSetupConcertStageLights.cs` — concert stage initial setup
- `SetupToyRagdoll.cs` — ragdoll setup
- `WireUpAlien7Vendor.cs` — vendor wiring pass
- `ViewDistanceSliderCleanup.cs` — UI cleanup
- `VerifyConcertSetup.cs` — verification utility
- `SunRayProbe.cs` — one-off probe
- `InspectBeamMaterials.cs` — one-off material inspection
- `LensFlareForceToggleOn.cs`, `LensFlareForceUpdate.cs`, `LensFlareDeepProbe.cs` — duplicative debug tools

**Verdict:** ~20 of the 29 editor scripts can probably go. The risk is low because Editor scripts are excluded from builds; the cost of deletion is zero if you ever need one again (git history). My recommendation is to bulk-delete the "likely one-shot" list above, run a sanity Editor open, and see if anything errors.

## §27 The Big Stuff

### `Assets/StreamingAssets/LlamaLib-v2.0.5/` — 3.96 GB
This is **94% of your StreamingAssets** and gigantic. It's the multi-platform native binary bundle for `llama.cpp` inference (ARM64 + x64 variants for CPU and GPU backends). It is **actively used** by `LLMService.cs` for on-device AI inference (no remote API dependency).

You probably can't shrink this much because the runtime needs the right backend variant for the user's platform. Possible mitigation: at build time, strip the platforms you don't ship for (e.g. if you're only targeting Windows x64, strip macOS/Linux/ARM variants). LLM-for-Unity (the LlamaLib distribution) may have a build-time stripping option — worth investigating if disk size is a concern. **Otherwise: keep as-is.**

### `Assets/4 - Scenes/_Archive/` — 416 MB
Covered in §25. Decision: keep as souvenir or delete to reclaim 400 MB.

### `Assets/transfer/` — 73.6 MB
Covered in §25. Decision: per-item review, likely 60+ MB recoverable.

### `Assets/StreamingAssets/AI/voice/` — 76 MP3 voice lines
Active. Used by the AI companion for voice responses. Keep.

## §28 Stragglers from Removed Features

CLAUDE.md mentions a few features that were ripped out. Verified what's left:

### `ShipDamageManager` (removed)
**Verified**: 1 file matches — `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` lines 193 and 1242, both in **comments only** that document the removal. These are intentional documentation in the save apply-order code; they shouldn't be cleaned up unless you want to reduce historical cruft. **Not a real straggler.**

### `Ship_Full`, `Ship_MissingLeft`, `Ship_MissingRight`, `Ship_NoThrusters` prefabs (removed)
**Verified**: no `Ship_*.prefab` files anywhere in `Assets/**`. Clean.

### `ArmRaiseAnimator` (the failed "arm reaches for item" experiment per CLAUDE.md)
**Verified**: zero references anywhere in the project. Clean.

### `damageState` field in `ShipSave`
SaveCollector.cs:191 writes `"Full"` to a legacy field that "nothing reads it on load." This is a deliberate backwards-compat write — could be cleaned up by removing the field entirely from `ShipSave` and `SaveCollector`, but be aware: old save files will fail to deserialize the now-missing field silently (JsonUtility ignores unknown fields, so it'd be fine to remove). **Low priority; cleanup if you want, but it's harmless as-is.**

## §29 Items Flagged But NOT Recommended for Deletion

These came up in the audit as "looks suspicious" but turned out to have legitimate reasons to exist:

- **`Assets/3 - Scripts/Tutorial/_LegacySteps.cs`** — the file header explicitly says save files reference the types by name; deletion would corrupt old saves. KEEP.
- **`Assets/3 - Scripts/PerfBootstrap.cs`** — fixes a real TMP-stutter perf issue. KEEP.
- **`Assets/3 - Scripts/Editor/EnableFrameTimingStats.cs`** — keeps the FPS overlay's GPU readout working. KEEP.
- **Disabled cinematic scenes** (`Cutscene.unity`, `Flashback.unity`, `Flashback1.unity`) — referenced by code via `SceneManager.LoadScene` even though disabled in build settings. KEEP.
- **`Resources/HotbarIcons/`** — actively referenced by `Hotbar.ResolveFishBagSprite` and other UI; cannot be moved without code changes. KEEP.
- **`Resources/Flares/`** — active lens flare assets. KEEP.
- **`Assets/3 - Scripts/Scripts/Game/PlanetTest.cs` and `StarTest.cs`** — sound like test code, but I didn't verify whether they're actually wired up. **Spot-check before deleting** — could be live editor harnesses.

---

# Appendix

## §A Verification Notes

What this audit verified directly (via Read/Glob/Grep):

- ✅ Build-settings scene list (`EditorBuildSettings.asset`)
- ✅ All 8 celestial body names present in the scene file
- ✅ Section organizers found by direct grep: `--- Debug ---` (line 26879), `--- UI ---` (34650), `--- Celestial ---` (63380), `--- Player & Ship ---` (63899), `--- Managers ---` (94547). **`--- World ---`, `--- Lighting ---`, `--- NPCs ---` were NOT found by direct grep** — either named differently in the actual scene or they don't exist as root sections, despite CLAUDE.md claiming the convention. **You may want to update the CLAUDE.md hierarchy convention or rename the scene organizers to match.**
- ✅ Total GameObjects in `1.6.7.7.7.unity`: 735. Disabled: 23.
- ✅ Top-level Assets folder sizes — `StreamingAssets` 3990 MB (mostly LlamaLib), `_Archive/` 416 MB, `transfer/` 73.6 MB
- ✅ `Ship_*.prefab` legacy variants do not exist (CLAUDE.md claim confirmed)
- ✅ `ShipDamageManager` only in `SaveCollector.cs` comments (CLAUDE.md claim confirmed — they're documentation, not stragglers)
- ✅ `ArmRaiseAnimator` not present (CLAUDE.md claim confirmed)
- ✅ `_LegacySteps.cs` exists and contains the protected legacy types
- ✅ `Assets/3 - Scripts/Scripts/Game/Controllers/{PlayerController, Ship, InputSettings}.cs` paths (CLAUDE.md's grouping of these under `Scripts/Game/` is slightly misleading — they're in the `Controllers/` subfolder)
- ✅ Foundational layer file enumeration for Celestial/, Game/, Script Utilities/, Game/UI/, Game/Controllers/
- ✅ Editor scripts full list (29 files)
- ✅ `EnableFrameTimingStats.cs` is a real `[InitializeOnLoad]` tool, not dead code
- ✅ `JitterDiagnostic.cs` is self-marked as deletable
- ✅ `PerfBootstrap.cs` is a real perf-optimization hook, not dead code

What this audit did NOT verify (don't fully trust):

- ❌ The pre-placed NPC GameObjects (Alien3, Alien4, Alien6, Alien7, TEV, BonfireNPC, etc.) — direct grep for `m_Name: Alien3` etc. at the scene root level returned **only `AlienNPCSpawner` (line 31742)**. The pre-placed NPCs are presumably nested under their host planet (Humble Abode is at line 31278). Without a hierarchical walk of the scene YAML, I can't 100% confirm each NPC exists in the scene. CLAUDE.md asserts they do; the script side definitely expects them. Recommend opening the scene in the Editor and visually verifying the NPC roster matches the table in §9.
- ❌ Names of the 23 disabled GameObjects — partial list obtained earlier (~19 of 23) included things like `TradeChoicePanel`, `GuitarChoicePanel`, `ThirstRow`, `EatButton`, `HeaderSep`, several `Text` instances, `Settings`, `Planet Name`, `Relative velocity x/y`, etc. — mostly UI / dialogue elements. **None looked like dead content; they're inactive states of active panels.**
- ❌ Deep orphan check via GUID cross-reference for every script in `Assets/3 - Scripts/` — too many files to do exhaustively. Focused on the suspicious ones; everything else assumed live.
- ❌ Third-party pack actual contents — the asset-supervisor agent partially failed; the prior round's assessment (sFuture Modules Pro, Cursed_Toys_II, Lens Flares, Low Poly Survival/Pistol/Nature/Mushrooms/Medieval packs all USED; F3_Corvette and DuNguyn likely UNUSED; Studio Nik/iPoly3D/Floreswa/Stylized Crystal unclear) is the best snapshot I have, but it wasn't fully verified by GUID grep. Spot-check before deletion.
- ❌ `Assets/Editor/` (top-level, distinct from script-folder Editor) contents — not enumerated this pass.
- ❌ Current state of `Game/Debug/Debug Viewer/Visualizer.cs` actual usage — bundling all of Triangle.NET for an unused visualizer would be a meaningful cleanup, but I didn't confirm whether it's actively used.

## §B Recommended Next Steps

1. **Quick wins** (15 minutes, ~500 MB recovered): Delete `JitterDiagnostic.cs`, `Planet.unity` from `_Archive/`, `Solar System.unity` from `_Archive/`, orphan `.meta` files in `StreamingAssets/AI/`.
2. **Editor cleanup** (30 minutes): Delete the ~20 likely-one-shot editor scripts listed in §26. Open the Editor afterward to confirm no compile errors.
3. **`transfer/` triage** (15 minutes): Open the folder, decide what to keep, delete the rest. Likely 60+ MB.
4. **`_Archive/` decision** (5 minutes — purely emotional): keep or delete the 400 MB of historical snapshots.
5. **Scene hierarchy sanity check** (10 minutes): open `1.6.7.7.7.unity` in Editor and verify that the root section organizers are what CLAUDE.md says (`--- World ---`, `--- Lighting ---`, `--- NPCs ---` were not findable in this audit). If they don't exist, update CLAUDE.md or add them; if they do exist under different names, update CLAUDE.md.
6. **Third-party pack audit** (30+ minutes): for each pack with "unclear" or "likely unused" status, do a real GUID grep before deletion.
