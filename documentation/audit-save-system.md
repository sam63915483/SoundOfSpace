# Audit: Save System

Read-only audit of `Assets/3 - Scripts/SaveSystem/` (SaveCollector.cs, SaveData.cs,
NewGameReset.cs, SaveSystem.cs, SaveLoadRunner.cs, PendingLoad.cs, AutosaveManager.cs,
SaveLoadUI.cs). Cross-checked against MainMenuController.EnsureGameplaySingletons,
Ship.cs, TutorialGate.cs, TutorialManager.cs.

## Summary

The save system is in good shape overall: the documented ~17-step apply order in
`SaveCollector.Apply` matches the actual call sequence, positions are captured from
`rb.position`/`rb.rotation` in the player/ship/loose-part/enemy paths, all catch
blocks log rather than swallow, and every captured sub-object has a matching apply.
AutosaveManager is correctly seeded in `EnsureGameplaySingletons` (trap #1 satisfied).

The most material findings are: (1) `ApplyHeldItem`/`ApplyFlashlight` use
non-include-inactive `FindObjectOfType`, so a save made while piloting drops held-item
and flashlight state on load (the player GO is deactivated by the time they run);
(2) `FindMainShip` returns `ships[0]` when every ship is bought, contradicting its own
"returns null" comment and causing a bought ship to be captured/applied twice; (3)
`TutorialGate` static state is not reset by `NewGameReset`; (4) a handful of dead fields
(`ResourcesSave.shipPower`, `ShipSave.damageState`) and stale comments.

Nothing here corrupts a save file. The findings are load-time state-restore gaps and
maintenance risks.

## Bugs (severity, file:line, description, fix)

### 1. MEDIUM — Held-item & flashlight lost when loading a piloted save
`SaveCollector.cs:1569-1573` (`ApplyHeldItem`), `1639-1642` (`ApplyFlashlight`), also
`ApplyCassette` 1615-1618.
These call `Object.FindObjectOfType<PlayerPickup>()` / `<PlayerFlashlight>()` **without**
the include-inactive flag. In `Apply()`, `ApplyShipTransform` (line 846) runs
`ship.PilotShip()`, and `Ship.PilotShip()` deactivates the player GameObject
(`Scripts/Game/Controllers/Ship.cs:1299` — `pilot.gameObject.SetActive(false)`).
`ApplyHeldItem` (line 870) and `ApplyFlashlight` (line 874) run **after** that, so when
`isPiloted == true` the player is inactive and these lookups return `null` → held item
and flashlight state are silently not restored. Note the capture side is asymmetric: it
deliberately uses `FindObjectOfType<PlayerController>(true)` (line 150, comment explains
the piloting case) but the applies do not.
**Fix:** pass `true` to the `FindObjectOfType<PlayerPickup>()`/`<PlayerFlashlight>()`
calls in `ApplyHeldItem`/`ApplyFlashlight` (and the `holdPosition` deref stays guarded).
Held items while piloting are unlikely, but flashlight is plausible; low blast radius, cheap fix.

### 2. MEDIUM — `FindMainShip` returns a bought ship when no un-bought ship exists
`SaveCollector.cs:175-182`. The comment (171-174) claims "typically returns null" when
every ship is bought, but the code's final line is `return ships[0];` — it returns the
first ship even if it carries `BoughtShip`. Consequences when the scene's only ships are
bought:
- `CaptureShip` (184) writes a bought ship into `data.ship`, and `CaptureExtraShips`
  (212) writes the **same** ship into `data.extraShips` → double-capture.
- On load, `ApplyShipTransform`/`ApplyShipDamage` reposition/re-attach that ship
  (lines 845-846) but `ApplyExtraShips` (849) then destroys and respawns all
  `BoughtShip` instances, discarding the work `ApplyShipTransform` just did.
Net final state is usually correct (teardown masks it) but the per-part attachment state
written via `ApplyShipDamage` to the soon-destroyed instance is lost, and the whole
`data.ship` round-trip is wasted work. **Fix:** make `FindMainShip` return `null` (not
`ships[0]`) when every ship has a `BoughtShip`, matching the comment and the intent that
`data.ship` covers only a non-bought main ship. Verify against the actual gameplay scene
whether a non-bought main ship exists at all (see Uncertainties).

### 3. LOW — `TutorialGate` static state not reset on New Game
`NewGameReset.cs:57-115` resets BonusTutorial (107) and MapTutorial (105) but never
touches `TutorialGate` (a `static class` — `TutorialGate.cs:20-23`, fields `_unlocked`
and `_enabled` persist across the main-menu round-trip in one process). If a player loads
a save whose `tutorial.gateEnabled == true` with a restricted `unlockedAbilities` set
(`ApplyTutorial` → `TutorialGate.ApplyState`, line 956), then returns to the menu and
starts a New Game, the gate stays enabled with movement/abilities locked. Mitigated by
the fact the current opening flow never enables the gate (`TutorialManager.Awake`
comment, TutorialManager.cs:42-43), so `_enabled` is only ever true from an old save.
**Fix:** add `TutorialGate.UnlockAll();` to `NewGameReset.Apply()`.
(`TutorialManager` itself is a plain scene component — no singleton/DontDestroyOnLoad — so
it is recreated fresh on scene reload and needs no reset.)

### 4. LOW — Migration flag mutation is not persisted, re-runs every load
`SaveCollector.cs:1104-1108` (`ApplyFishInventory`) sets `s.migratedToHotbar = true` on
the in-memory loaded `SaveData`, not on disk. For a pre-Phase-2 save that is loaded
repeatedly without an intervening save, `MigrateFishInventoryToHotbar` runs on every load.
Because `ReplaceAll` reset the FishInventory first and the migration `TryAddFish` path is
idempotent-ish, this is not duplicative in practice, but it is unintended repeated work
and depends on that idempotence. Low risk; note only. No fix required if a save is written
soon after load (autosave will persist the flag).

## Save-correctness risks (apply-order, capture source, missing reset)

- **Apply order is correct.** The sequence in `Apply()` (824-882) matches the documented
  17-step comment and the CLAUDE.md ordering (bodies → tutorial → NPCs → earlyGame →
  singletons → ship → extras → dust → hotbar → storages → player → buildings → loose
  parts → enemies → held item → touch-ups). No ordering hazard found.

- **Capture-from-transform instead of rb:** `CaptureCelestialBodies` (SaveCollector.cs:123)
  captures `rotation = body.transform.rotation` while using `body.Position` and
  `body.velocity` (rb-backed accessors) for the others. Bodies spin via transform and are
  restored via `ApplySavedState`, so this is likely harmless, but it is the one spot that
  reads a transform for a saved rotation rather than the rigidbody. Worth confirming
  `CelestialBody` rotation is transform-driven (not interpolated rb rotation) — if it is
  rb-driven the transform value can lag physics. Player/ship/loose-part/enemy paths all
  correctly use `rb.position`/`rb.rotation` (see the explicit comment at 500-503).

- **NPC identity by GameObject name:** `CaptureNPCs`/`ApplyNPCs` (435-461, 959-986) key on
  `gameObject.name`. Two same-named NPCs of the same type would collide (both get the same
  saved completion state). Same class of risk as the alien-name path, which was already
  hardened (932-943). Only a problem if duplicate-named NPCs exist.

- **NewGameReset coverage vs schema:** cross-checked every `SaveData` sub-object against
  `NewGameReset.Apply`. All persistent singletons / statics are reset **except**
  `TutorialGate` (see Bug #3). Everything else the save touches is either reset explicitly
  or is a scene object recreated fresh on scene reload (ship, extraShips, npcs, buildings,
  looseParts, enemies, storages, cassette, worldFlags, per-ship space-dust nets,
  player/equipment which self-evict per the file header). GameKnowledgeBase story phase is
  intentionally not reset (documented).

- **Include-inactive asymmetry (broader than Bug #1):** `ApplyEquipment` (1162) and
  `ApplyPlayerTransform` use plain `FindObjectOfType` for player-child controllers, but
  `ApplyEquipment` runs at line 841 *before* `ApplyShipTransform` pilots the ship, so the
  player is still active there **unless** `GameSetUp` auto-pilots on Start
  (`StartCondition.InShip`) before the deferred Apply runs (the `ApplyShipTransform`
  comment at 1306-1311 confirms this can happen). In an InShip-start scene, equipment
  restore could also miss. Capture uses `(true)` for the player everywhere; the applies
  should too, for symmetry and robustness.

- **`ListSaves` uses file mtime, not the stored `isoTimestamp`.** `SaveSystem.cs:65` sorts
  and displays by `File.GetLastWriteTime`, ignoring `SaveData.isoTimestamp` written at
  capture (SaveCollector.cs:17). "Newest save" (used by death-reload / New Game autosave
  logic per NewGameReset.cs:109-114) is therefore mtime-based; copying/touching a save
  file would reorder it. Minor, but worth knowing since death-reload correctness leans on it.

## Redundancies / Dead Code

- **`ResourcesSave.shipPower`** (SaveData.cs:209) — never captured, never applied. Dead field.
- **`ShipSave.damageState`** (SaveData.cs:196; written "Full" at SaveCollector.cs:198) —
  explicitly documented as legacy; nothing reads it on load. Dead-but-intentional
  (kept for round-trip string-compare tests).
- **`ApplyShipDamage`** (1290-1299) — the prefab-swap path is gone; the method now only
  forwards attachment booleans to `ThrusterDetachOnImpact`. The `Apply()` step-7 comment
  (812) still says "synchronous prefab swap; may replace the ship," which is stale.
- **`SpaceDustSave` legacy fields** (`netShipNumbers`, `netBuffers`, `sceneShipBuffer`,
  SaveData.cs:418-420) — write path is dead (cleared at capture, 705-707); read path is a
  backward-compat fallback only. Correct as designed, but flagged for eventual removal once
  no pre-multi-net saves remain.
- **`FindMainShip` legacy branch** — see Bug #2; the whole "main ship" concept may be dead
  if the scene has no un-bought ship (Uncertainties).

## Performance / Optimization

- **Heavy `FindObjectOfType`/`FindObjectsOfType` usage in capture/apply.** Both paths call
  these dozens of times (e.g. CapturePlayer, CaptureEquipment does 6 separate
  `FindObjectOfType` calls, CaptureNPCs/ApplyNPCs do 3 `FindObjectsOfType` each). This is
  fine — save/load are one-shot, not per-frame, and the CLAUDE.md ban is on Update-loop
  usage. No action needed; noted only so it isn't "fixed" into caching that adds risk.
- **`ApplyNPCs`** (959-986) does an O(N·M) nested scan (save entries × scene NPCs) per NPC
  type. N and M are small; acceptable.
- **`DestroyImmediate` in `SaveLoadUI.rebuildList`** (line 193) — intentional (comment
  explains the need for `childCount` to drop synchronously). Correct; not a leak.
- Micro: `CaptureEquipment` re-finds `PlayerController` (570) after other finds; could
  reuse one lookup, but negligible.

## Notes & Uncertainties

- **Does a non-bought "main ship" exist in `1.6.7.7.7.unity`?** This determines whether
  Bug #2 fires and whether the entire `data.ship`/`FindMainShip` path is live or dead.
  I could not confirm from code alone — verify in the Editor. If no un-bought ship exists,
  `FindMainShip` currently returns a bought `ships[0]` and the fix in Bug #2 is required;
  if one does exist, the double-capture only happens in the all-bought edge case.
- **`CelestialBody` rotation source** — confirm rotation is transform-driven so the
  transform-rotation capture (Finding under Save-correctness) is safe. `CelestialBody.cs`
  is not in the forbidden zone for read inspection.
- **Autosave in builds** — AutosaveManager has the MainMenu early-return + is seeded in
  `EnsureGameplaySingletons` (MainMenuController.cs:579 and 750), so trap #1 is satisfied;
  no build-only autosave bug.
- All disk I/O (`SaveSystem.Save/LoadFromDisk/DeleteSave`) and both deferred runners
  (`SaveLoadRunner`, `NewGameResetRunner`) wrap work in try/catch that **logs** — no silent
  swallowing. Good.
- `PendingLoad` and `NewGameReset` both correctly unsubscribe their `sceneLoaded` hooks and
  skip the MainMenu scene; timing (1 frame + 1 FixedUpdate) is consistent between load and
  new-game paths.
