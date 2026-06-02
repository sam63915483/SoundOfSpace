# Cleanup Pass — Design

**Date**: 2026-05-13
**Status**: Approved (scope locked in brainstorming session)
**Source**: 10 parallel domain audits → ~95 findings synthesized by theme

## Purpose

Bring the codebase into full conformance with the conventions documented in
`CLAUDE.md`, eliminate accumulated dead code, fix latent bugs surfaced by the
audit, and extract shared helpers where the same boilerplate is repeated 4+
times. The user described the goal as "as if a software developer professor
made it better with a team of agents."

## Scope summary

In scope:
- **Phase 1** — Real bugs (5 items) — landed and verified independently
- **Phase 2** — Dead-code deletion (~30 files)
- **Phase 3** — Shared-helper extraction (~700 LOC of duplication collapsed)
- **Phase 4** — Per-frame perf hotspots (CLAUDE.md compliance)
- **Phase 5** — Save-system polish + doc updates

Out of scope:
- **Phase 6** — God-file splits (deferred; pure cosmetics, high diff noise)
- Anything inside the **forbidden zone** from `CLAUDE.md` (atmosphere, planet
  generation, planet effects, celestial shading/shape/noise) — read-only
  inspection only

## Constraints

- The user works on a *copy* of the project, so we can take risks.
- `CLAUDE.md` documents fragile zones (atmosphere/planet gen) where a prior
  session destroyed an unrecoverable build. Default: do not touch even with
  permission. If a fix unambiguously requires it, ask first.
- No automated tests exist. Verification is manual in the Unity Editor.
- File moves/renames are permitted as long as `.meta` files move with their
  partner. Pinned `AssetDatabase` paths (CLAUDE.md) must be updated in the
  same commit if their target moves.

## Architecture: new shared modules

Each new helper lives next to the consumers that need it. None of them
introduce a new assembly definition (CLAUDE.md: "No `.asmdef` files").

| Helper | Path | Replaces |
|---|---|---|
| `UILayer` | `Assets/3 - Scripts/UI/UILayer.cs` | 50+ magic `sortingOrder = …` literals |
| `HudCanvasFactory` | `Assets/3 - Scripts/UI/HudCanvasFactory.cs` | ~15 copies of `AddComponent<Canvas>` + scaler + raycaster |
| `UIBuild` | `Assets/3 - Scripts/UI/UIBuild.cs` | `NewUI` / `NewText` / `Stretch` duplicated across 12 HUDs |
| `PreviewRig` | `Assets/3 - Scripts/UI/PreviewRig.cs` | RenderTexture preview camera setup in Fishingdex, GoodsVendorShopUI, ShipMarketShopUI, BuildMenuUI |
| `DialogueNPCBase` | `Assets/3 - Scripts/NPC_Dialogue/DialogueNPCBase.cs` | ~80 LOC × 8 NPC scripts |
| `HandheldEquippableBase` | `Assets/3 - Scripts/Pickups/HandheldEquippableBase.cs` | ~80 LOC × 4 pivot controllers (Axe/Pistol/Guitar/Rod) |
| `Ship.PilotedShip` static | extends `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs` | 4 HUDs' per-frame `FindObjectsOfType<Ship>` |

Each helper has one job and ≤200 LOC. Consumers depend only on the helper's
public API; the helper depends on no consumer.

## Phase 1 — Real bugs (separate batch, verify gate)

These are landed first, in one commit, then the user verifies in Unity before
phases 2–5 begin.

1. **Centralise sortingOrder via `UILayer`** (audit HUD-2/11)
   - Build the `UILayer` class up-front (constants: `Hud=830, Toast=900,
     Vendor=950, Map=970, Pause=1000, Modal=1100, SaveDialog=2000`).
   - Update `AutosaveManager`, `StoryImpactNotice`, `GoodsVendorShopUI`,
     `ShipMarketShopUI`, `MapTeleportToPilotButton`, `FlightAssistStatusHUD`
     callsites that paint over the pause menu.
   - Pause menu reads from `UILayer.Pause`; toasts that *intentionally*
     overlay it use `UILayer.Modal`.

2. **Register held items with `EndlessManager`** (audit Save-3)
   - In `SaveCollector.ApplyHeldItem`, after `pickup.ForcePickup(go)`, call
     `EndlessManager.Instance?.RegisterPhysicsObject(go.transform)` and
     register `PickupMarker` with `PickupUIManager` if the prefab carries one.
   - Mirrors `ApplyLooseParts` exactly.

3. **Make `PickupUIManager` an auto-create singleton** (audit HUD-10)
   - Add `[RuntimeInitializeOnLoadMethod]` factory + procedural marker prefab
     using `HudFontResolver` for the font.
   - Add to `MainMenuController.EnsureGameplaySingletons`.
   - Register the canvas with `HUDSceneGate`.
   - Likely root cause of the stale-state HUD bugs in commits 418ad9b /
     59ec15f / 812707b.

4. **Cross-controller pistol awareness** (audit Equip-1/3)
   - In each of `WaterBottleController`, `AxeController`, `GuitarController`,
     `FishingRodController`: cache `_pistolController` in `Start()` and add
     `if (_pistolController != null && _pistolController.IsEquipped) return;`
     to the `Equip*` entry. Also add `_axeController` check to
     `WaterBottleController`.
   - Pure additions; no existing logic changes.

5. **Bonfire registry singleton** (audit Build-2/4)
   - New `BonfireUIRegistry` singleton seeded in `EnsureGameplaySingletons`
     owning the cook-panel canvas reference.
   - Both `GhostPlacement.Place` and `SaveCollector.ApplyBuildings` pull from
     the registry instead of scanning the scene for a "template" bonfire.
   - The original scene bonfire registers itself into the singleton in
     `Start()`; if it's ever destroyed, the registry retains the canvas
     reference.

**Verification gate**: User opens Unity, ensures the console is clean, plays
through:
- Save → quit to main menu → load: pickup markers + held thrusters work.
- Equip pistol via vendor, then drink water — bottle should refuse.
- Place a bonfire near the original; destroy the original; new bonfire must
  still open the cook panel.
- Open pause menu while autosave toast is on screen — pause must be on top.

## Phase 2 — Dead-code deletion

Single commit per group; the four groups are independent.

**Group 2a — Runtime dead code** (4 files)
- `Assets/3 - Scripts/Fishing/FishingdexEntryUI.cs` (Cross-16, HUD-9; CLAUDE.md already flags it)
- `Assets/3 - Scripts/Player/PlayerLeash.cs` (Cross-17; referenced only by archived `Flashback1.unity`)
- `Assets/3 - Scripts/Camera/CustomLensFlare.cs` (Cross-18; only in `_Archive/1.7.unity`)
- `Assets/3 - Scripts/Camera/FlareCameraSetup.cs` (Cross-19; only `Flashback1.unity`)

**Group 2b — `ResourceHUD` retirement** (HUD-8/12)
- Delete `ResourceHUD` component instance from `1.6.7.7.7.unity` (manual scene
  edit).
- Delete `Assets/3 - Scripts/Survival/ResourceHUD.cs`.
- Delete `VitalsHUD.DisableLegacyResourceHUD` and its `Update` call.

**Group 2c — Editor one-shot cleanup** (Editor-1-5)
- Delete the 17 one-shot `Execute()` editor scripts whose output is already
  baked into the scene asset. Inventory before deletion to confirm each is
  truly redundant.
- Delete `CreateEnemyPrefab`, `ViewDistanceSliderInstaller`, `SetupTorchFire`,
  `SetupFishingdex` (with confirmation pass against current scene).
- Consolidate the 3 overlapping bonfire wiring scripts into one (or delete
  all three if covered by Phase 1 #5).
- Update CLAUDE.md "Pinned-path files" table with the three new entries
  flagged in Editor-7.

**Group 2d — Dead TutorialSteps** (Tutorial-2)
- Move ~25 unused `TutorialStep` subclasses to
  `Assets/3 - Scripts/Tutorial/_LegacySteps.cs` (single file).
- Update CLAUDE.md `TalkToNPCsStep` reset-on-OnEnter note (now dead code).
- Confirm with grep that no save data references the type names; if it does,
  keep them in the legacy file rather than delete.

## Phase 3 — Shared-helper extraction

Each helper is introduced in its own commit; consumers are migrated in a
follow-up commit per consumer group. Order:

1. `UILayer` (already built in Phase 1) — done.
2. `HudCanvasFactory` + `UIBuild` — migrate the 15 HUDs that build canvases
   procedurally.
3. `HudFontResolver` consolidation — replace 8 duplicated 17-line font-fallback
   blocks with a single `HudFontResolver.Apply(t)` call (audit HUD-1, Cross-12).
4. `PreviewRig` — migrate Fishingdex, GoodsVendorShopUI, ShipMarketShopUI,
   BuildMenuUI preview cameras to one helper (audit HUD-15, Cross-14).
5. `Ship.PilotedShip` static + `OnEnable`/`PilotShip`/`ExitFromSpaceship`
   maintenance — drop 4 HUDs' per-frame `FindObjectsOfType<Ship>` scans
   (audit HUD-7).
6. `DialogueNPCBase` abstract — migrate `NPCDialogue`, `BonfireNPCDialogue`,
   `GuitarShopNPC`, `FishMarketNPC`, `Alien7Vendor`, `ShipMarketNPC`,
   `TevDialogue`, `RandomAlienDialogue` (audit NPC-3, NPC-10). Carries the
   `NPCConversationTracker.NotifyStart` call so Phase 5 #5 is automatic for
   every base-class consumer.
7. `HandheldEquippableBase` abstract — migrate `AxeController`,
   `PistolController`, `GuitarController`, `FishingRodController` (audit
   Equip-9). Bottle stays separate (bone-animated, no pivot).

**Risk note**: NPC and equippable base-class extractions are the highest-risk
changes in the entire pass. Each touches inspector-visible MonoBehaviour
fields; serialized references survive a script edit only if the field names
and types are preserved. We must keep every existing public/serialized field
intact when introducing the base class.

## Phase 4 — Per-frame perf hotspots

Roughly 20 sites violate CLAUDE.md's "no FindObjectOfType / Camera.main /
unguarded string-alloc per frame" rule. All fixes follow one of three patterns:

**Pattern A — Cache + lazy refind on null**
- `Hotbar.ResolveRefs` ×6/frame → cache Player root once, GetComponent each
  controller (Equip-5).
- `EnemyController.FixedUpdate` 100 Hz find (Combat-2).
- `EnemySpawner.Update` find (Combat-10).
- `EnemyHealthBar.LateUpdate` × N enemies (Combat-4).
- `LensDirtOverlay.LateUpdate` find lights (Camera-3).
- `CameraEffectsManager.AttachModules` every frame → set flag once attached
  (Camera-5).
- `SpeedLinesOverlay.LateUpdate` camera rebind → subscribe to
  `SceneManager.sceneLoaded` (Camera-12).
- `VitalsHUD.DisableLegacyResourceHUD` becomes a no-op after Phase 2b.
- `BuildMenuUI` canvas scan on Open → cache.
- Multiple TutorialStep `Tick()` finds → move to `OnEnter()` (Tutorial-1).
- Spawner / FishingRod / Pistol / Axe `Camera.main` per event → cache
  (Cross-1/2/3).

**Pattern B — Change-detection for per-frame string interpolation**
- `ShipHUD` planetName/distance/velocity (Cross-7).
- `CassettePlayer` eject prompt (Cross-8).
- `InteractPromptUI.Show` callers — cache the formatted string per-glyph
  (NPC-12).
- `step.Tip` getters that interpolate counters (Tutorial-6).

**Pattern C — `rb.position` not `transform.position` for Rigidbody objects**
- `PlayerPickup.Update` held-object positioning (Cross-4) — set
  `isKinematic = true` on pickup, then transform-write is acceptable; OR
  use `MovePosition` from `FixedUpdate`. Decide during implementation.
- `ResourceManager` death respawn teleport (Cross-5).
- `GameSetUp.Start` (Cross-6).
- `Ship.ForceExitPilot` (Cross-6).

## Phase 5 — Save-system polish

1. **Doc + reality sync** — update CLAUDE.md `Apply` order to match
   `SaveCollector.Apply` actual call sequence; fix the "Currently NOT saved"
   self-contradiction (Save-1/13).

2. **Apply-order fix** — move `ApplyEquipment` to run **after**
   `ApplyShipDamage`/`ApplyShipTransform` so controllers caching the ship
   ref don't grab a destroyed instance (Save-5).

3. **Extra-ships symmetry** — call `ship.MarkIntroComplete()` for extras;
   document whether extras participate in `ShipDamageManager` (Save-6).

4. **Persist `BonfireNPCDialogue._firstTimeDone`** — expose
   `ApplyFirstTimeDone(bool)`, capture by GameObject name like `NPCDialogue`,
   add `firstTimeDone` to `NPCSave` (NPC-5).

5. **`NPCConversationTracker.NotifyStart` everywhere** — `ORGDialogue`,
   `InterrogationDialogue`, `BonfireInteraction` were skipping it. Folding
   into `DialogueNPCBase` (Phase 3 #6) covers most NPCs automatically;
   `BonfireInteraction` and `InterrogationDialogue` get explicit calls.

6. **Warn on unknown prefab keys** — replace silent `continue` with
   `Debug.LogWarning` in `ApplyBuildings` / `ApplyLooseParts` /
   `ApplyExtraShips` (Save-17).

7. **Hotbar eviction unequips** — when `DetectAcquisitions` evicts a slot,
   call `ForceUnequip` on its controller (Equip-10).

8. **`BonusTutorial` table-driven refactor** — collapse the (header, saveKey,
   factory) magic-string triples into a `BonusTutorialKind` enum + table
   mirroring `Hotbar.BuildRegistry` (Tutorial-3).

9. **Unregister before destroy** — `ApplyExtraShips` and `ApplyBuildings`
   should call `EndlessManager.UnregisterPhysicsObject` before destroying
   replaced instances (Save-2). `ApplyLooseParts` already does.

10. **Pre-placed alien dedup** — convert the O(N·M) `killedPrePlacedNames`
    loop in `ApplyEarlyGame` to a single-pass HashSet (Save-4).

## Risk register

| Risk | Mitigation |
|---|---|
| Phase 3 base-class extractions break inspector-serialized refs in `.unity` / `.prefab` | Keep every existing public/serialized field name and type exactly the same; move shared logic into the base, leave subclass fields alone. Manual scene-load + Console check after each migration. |
| Editor script deletions (Phase 2c) cascade into a scene rebuild someone needs later | Audit each script's output against current scene state before deletion; commit one group at a time so rollback is one git revert. |
| `ApplyEquipment` order change (Phase 5 #2) interacts with other Apply-order constraints | Make the change in isolation; manually load a save with a damaged ship + equipped axe; verify axe still equipped after load. |
| `DialogueNPCBase` migration (Phase 3 #6) changes Update-tick string allocations | Validate per-NPC behaviour by talking to each NPC type in the live scene after migration. |
| Forbidden-zone bleeding | All planned changes are explicitly outside the forbidden zone. If a Phase-4 perf fix surfaces a per-frame allocation inside it, defer rather than touch. |

## Verification plan

**After Phase 1**: User-driven scenario test (see Phase 1 section above).
Confirm console-clean before phases 2-5 begin.

**After Phases 2-5 (big-bang)**: User runs through the full golden path —
crash, salvage thrusters, talk to NPCs, trade cassette for rod, catch a fish,
sell at market, build a bonfire, chop a tree, equip axe + pistol, fight an
enemy, fly the ship, open the map, save & quit, reload, verify all state
preserved. Console must be clean. If a regression appears, the commit history
is granular enough to bisect.

## Out-of-scope (explicit non-goals)

- `Phase 6` god-file splits — pure cosmetic; deferred indefinitely.
- Anything inside the forbidden zone (atmosphere, planet generation, planet
  effects, celestial shading/shape/noise, planet/ocean shaders).
- New features.
- Behavior changes that aren't bug fixes.
- Scene reorganization beyond the explicit `ResourceHUD` removal.
- `.asmdef` introductions — explicitly forbidden by CLAUDE.md.

## Open questions for the user before implementation

1. **Phase 2d (dead TutorialSteps)** — empirically verify save fallback
   ignores missing types before deleting, or keep them in `_LegacySteps.cs`
   as a safety net? Default: keep in legacy file.

2. **Phase 4 Pattern C `PlayerPickup`** — keep transform-writes with
   `isKinematic = true`, or switch to `rb.MovePosition` from `FixedUpdate`?
   Default: kinematic + transform (less invasive).

3. **`Resources.Load` exceptions** — `KillstreakHUD` loads tier PNGs from
   `Resources/Killstreak/`, `LensFlareRegistry` loads `Flare` from Resources.
   Migrate to serialized refs, or update CLAUDE.md to document the exception?
   Default: document the exception.

These have inline defaults; user can override before implementation begins.
