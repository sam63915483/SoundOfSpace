# Audit: Building & Vendor

Read-only audit of `Assets/3 - Scripts/Building/` and `Assets/3 - Scripts/Vendor/`.
Scope: bugs, null-deref risks, placed-building naming convention, currency/economy
math, `FindObjectOfType`/`Find` in Update loops, per-frame allocations, placement
raycast/snap math, dead/duplicated code, optimization.

## Summary

The two subsystems are generally solid: economy math is correct and guarded, the
`<prefab>_Placed` + parent-to-`CelestialBody` save contract is honoured in the
normal path, and no `FindObjectOfType`/`Find` runs in a per-frame vendor/build
loop. The headline finding is a **dead safety system**: `BuildMenuLock` is set by
the tutorial and round-tripped through saves, but `BuildMenuUI` never consults it,
so the build tutorial's "only Cabin/Torch/Bonfire" restriction does nothing. The
rest are low-severity edge cases (money spent with nothing granted when a
dependency is missing, unparented placement when no body is found) and per-frame
string/bounds allocations in vendor prompts and ghost snapping. The two shop UIs
are ~90% copy-paste (acknowledged as intentional).

Counts: 4 bugs (1 high, 3 low) · 5 redundancy/dead-code items · 6 performance items.

---

## Bugs (severity, file:line, description, fix)

### B1 — HIGH — `BuildMenuLock` is never enforced by the build menu
`Assets/3 - Scripts/Building/BuildMenuUI.cs:674` (`RebuildVisibleRows`) and
`Assets/3 - Scripts/Building/BuildMenuLock.cs:22`.

`BuildMenuLock.LockAllExcept("Cabin","Torch","Bonfire")` is called during the
build tutorial (`Assets/3 - Scripts/Tutorial/TutorialSteps.cs:835` and `:846`),
and the lock state is saved/restored (`SaveCollector`, `SaveData`, `NewGameReset`).
But `BuildMenuUI.RebuildVisibleRows` filters rows **only by category**
(`bool visible = !_filterActive || cat == _activeFilter;`, line 692) and never
calls `BuildMenuLock.IsUnlocked(...)`. The row-build loop in `AddListRow` also
does no lock check. Result: during the tutorial (and any locked state) the menu
shows **every** blueprint, not the intended three.

The stale doc comment in `BuildMenuLock.cs:10-11` even asserts
"`BuildMenuUI.RebuildVisibleCards` calls `IsUnlocked(entry.displayName)`" — that
method name (`RebuildVisibleCards`) does not exist; the method is
`RebuildVisibleRows`. This looks like a UI rewrite (cards → rows) that dropped the
lock check.

Fix: in `RebuildVisibleRows`, fold the lock into the visibility test, e.g.
`bool visible = (!_filterActive || cat == _activeFilter) && BuildMenuLock.IsUnlocked(entry.displayName);`
(the loop tuple already carries `entry`; use `_rowEntries` entry). Call
`RebuildVisibleRows()` from `Open()` so a lock change since the last open is
reflected. Then update the `BuildMenuLock` doc comment to the real method name.

### B2 — LOW — Money spent with nothing granted when a grant dependency is absent
`Assets/3 - Scripts/Vendor/Alien7Vendor.cs:302` (`GrantItem`) and
`Assets/3 - Scripts/Vendor/ShipMarketNPC.cs:474` (`GrantPartPickup`).

`Purchase` charges the wallet (`SpendMoney`) *before* dispatching the grant. If the
target controller is missing the grant silently no-ops:
- `Alien7Vendor.GrantItem`: each case does `if (_xCached != null) …`; a null
  controller means money is gone and nothing is unlocked.
- `ShipMarketNPC`: `Purchase` (line 318) treats `pickup == null` as "not holding"
  and proceeds to spend; `GrantPartPickup` (line 476) then `if (pickup == null) return;`
  after the charge — money lost, no pickup.

These are edge cases (the Player-root controllers and `PlayerPickup` should always
exist in a valid gameplay scene), so severity is low, but the order is fragile.

Fix: resolve the grant target and validate it *before* `SpendMoney`, or refund on
grant failure. Cheapest: move the null check ahead of the charge and return an
appropriate `PurchaseResult` (e.g. `InvalidItem`).

### B3 — LOW — Placed building not parented (breaks the `_Placed` save contract) when no body is found
`Assets/3 - Scripts/Building/GhostPlacement.cs:485-493` (`Place`).

`real.name = entry.prefab.name + "_Placed"` is always set, but the
`SetParent(parentBody.transform, …)` only runs when
`FindClosestBody(pos) != null` (line 491). If no `CelestialBody` is found
(`NBodySimulation.Bodies` empty / off the solar-system scene), the prop is left at
scene root, unparented. Per CLAUDE.md the save system finds placed buildings by
the `_Placed` suffix **and** the `CelestialBody` parent, so an unparented one
won't be persisted and won't ride orbital motion (drifts in world space). Same
gap affects the snap search, which only walks `body.transform` children
(`FindNearestSnapTarget`, line 273) — an unparented placement is invisible to
snapping too.

Fix: if no body is found, either abort the placement (refund wood, warn) or defer
placement until a body exists. At minimum log a warning so the silent
non-persistence is visible.

### B4 — LOW (documentation) — Stale/misleading comment in `BuildMenuLock`
`Assets/3 - Scripts/Building/BuildMenuLock.cs:10-11`.

Comment references `BuildMenuUI.RebuildVisibleCards` calling
`IsUnlocked(entry.displayName)`. Neither the method name nor the call exists (see
B1). The comment misrepresents the actual behaviour and hides the B1 bug. Update
once B1 is fixed.

---

## Redundancies / Dead Code

- **`GoodsVendorShopUI.cs` and `ShipMarketShopUI.cs` are ~90% identical** (palette
  block, canvas/preview-rig build, grid/detail/toast build, `RenderItem`,
  `MkText`/`MkButton`/`MakeRT`, toast/close coroutines). The header comment on
  `ShipMarketShopUI.cs:7-12` states this is deliberate ("kept as a separate type
  … so the goods-vendor flow stays untouched"). Meaningful maintenance risk: any
  fix to one (e.g. B2-style refund, preview lighting) must be mirrored by hand.
  Consider a shared base class or a generic `VendorShopUI<TVendor>` with two thin
  subclasses; the only real differences are the bound vendor type, the title
  string, the owned-state policy, and the buy-result toast messages.

- **`ShopItemKind.SpaceDustFilter = 30`** (`ShopItem.cs:17`) is explicitly
  deprecated ("Filter system removed") and kept only for save compatibility. Fine
  to keep, but it is dead in all switch dispatch (`PickupPrefabFor`, grant paths).

- **Unused palette constants** in both shop UIs: `C_CardHover` and `C_Divider`
  (`GoodsVendorShopUI.cs:19-20`, `ShipMarketShopUI.cs:19-20`) are defined but never
  referenced — button hovers use `Color.Lerp(...)` and cards use `C_CardBg`.
  Dead fields.

- **`BuildableCategory` has `Stair/Furniture/Storage/Decor`** (`BuildableEntry.cs:4`)
  but only `Floor/Wall/Roof` are snappable (`GhostPlacement.IsSnappableCategory`).
  The extra categories work as plain (`General`) placements and drive tab
  filtering, so not strictly dead — just note that snapping silently doesn't apply
  to them (by design).

- **`ShowList()` is an empty method** (`BuildMenuUI.cs:207`) kept as a layout
  no-op ("single-panel layout — selection clears the spec"). Harmless; could be
  inlined/removed.

---

## Performance / Optimization

- **P1 — Per-frame prompt string allocation in both vendor NPCs.**
  `Alien7Vendor.cs:128` and `ShipMarketNPC.cs:142`: while the player is in range
  and not talking, `Update` runs
  `InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk")` **every
  frame**. The interpolated string is allocated each frame regardless of any
  change-detection inside `Show`. Cache the string (rebuild only when the glyph /
  input source changes) or gate the `Show` call behind a "prompt not already
  shown" flag.

- **P2 — Ghost snap recomputes target bounds every frame.**
  `GhostPlacement.cs:250` (`ApplyGhostPose` → `ComputeLocalBounds(snapTarget.gameObject)`).
  `ComputeLocalBounds` does `GetComponentsInChildren<MeshFilter>(true)` plus an
  8-corner encapsulation per mesh, every frame while snapping. Cache per snap
  target (keyed by the `Transform`, invalidated when the target changes) — the
  ghost's own bounds are already computed once in `Begin`.

- **P3 — Ghost snap target search is O(children × buildables) per frame.**
  `GhostPlacement.cs:262-286` (`FindNearestSnapTarget`) iterates every `_Placed`
  child of the closest body and, for each, calls `FindBuildableByPrefabName` which
  linearly scans `menu.buildables`. Runs every frame in snap mode, and
  `FindClosestBody` (full body scan) is called more than once per frame (snap
  resolve + R-press). Build a `Dictionary<string, BuildableEntry>` name→entry once
  and reuse the body found earlier in the frame.

- **P4 — `_ghostMat` material instance leak.**
  `GhostPlacement.cs:74` creates a new `Material` per `Begin`; it is assigned to
  renderers via `r.materials = mats` (line 80) but never `Destroy`d in `Finish()`
  (only the ghost GameObject is destroyed). Each placement session leaks at least
  the shared ghost material (plus the per-renderer instanced copies). Track and
  `Destroy(_ghostMat)` in `Finish()`/`OnDestroy`.

- **P5 — Vendor previews don't suppress the scene sun (inconsistent with the build menu).**
  `BuildMenuUI.RenderPrefabPreview` (lines 1006-1039) disables all non-preview
  scene lights + neutralises ambient during the single-frame render, then
  restores. `GoodsVendorShopUI.RenderItem` / `ShipMarketShopUI.RenderItem`
  (`:577` / `:568`) do **not** — they rely on bright (2.5-intensity) preview
  lights to overpower the directional sun, which still lights layer-31 objects
  (the sun's `cullingMask` is Everything). Not a bug, but previews can wash out /
  vary with time-of-day; the build-menu approach is the more robust pattern to
  copy if the vendor previews ever look inconsistent.

- **P6 — One-time heavy calls in preview render (acceptable, noted for completeness).**
  `BuildMenuUI.RenderPrefabPreview` calls `FindObjectsOfType<Light>()` (line 1006)
  and all three preview paths do `Instantiate` + `GetComponentsInChildren` +
  `DestroyImmediate` per prefab. All are gated behind a per-prefab
  `RenderTexture` cache (`_previewCache`), so each prefab pays once. Fine as-is;
  just don't call these on a hot path.

---

## Notes & Uncertainties

- **Economy math is correct.** `PlayerWallet.SpendMoney` guards
  `amount < 0 || Money < amount` (`PlayerWallet.cs:43-48`).
  `Alien7Vendor.Purchase` checks owned/inventory-space *before* charging
  (`Alien7Vendor.cs:250-269`); `ShipMarketNPC.Purchase` checks prefab/holding
  *before* charging (`ShipMarketNPC.cs:292-332`). `SpaceDustSellUI` clamps qty to
  owned dust, rolls against the *displayed* quantity-scaled chance, then
  `Spend(qty)` + `AddMoney(qty * price)` (`SpaceDustSellUI.cs:183-205`). No
  double-spend or under/over-pay found. (The B2 order-of-operations issue is the
  only economy caveat, and only fires on a missing dependency.)

- **`BoughtShip` ship-number logic** (`ShipMarketNPC.NextShipNumber`, `:462`;
  `BoughtShip.shipNumber` default 0, `:22`) is deliberate and well-documented —
  default 0 keeps the just-added marker out of the max, and saved values override
  on load. No issue.

- **Placed-building naming is correct in the normal path**: `Place` sets
  `_Placed` and parents to the nearest `CelestialBody`
  (`GhostPlacement.cs:485-493`); bonfires placed this way also get the suffix and
  parent. Only the no-body edge case (B3) breaks the contract.

- **`SpaceDustSellUI` auto-creation** correctly does **not** early-return on
  MainMenu (comment at `:56-63`) because `RuntimeInitializeOnLoadMethod` fires
  once at process start; whether it is additionally seeded in
  `MainMenuController.EnsureGameplaySingletons` (CLAUDE.md trap #1) was not
  verified in this audit — worth a spot-check, though the `DontDestroyOnLoad`
  early creation likely makes it moot.

- I did not run the Editor (no CLI build/test per CLAUDE.md); findings are from
  static reading. B1's runtime impact is inferred from the call sites and is
  high-confidence given the grep results (no `BuildMenuLock`/`IsUnlocked(name)`
  reference anywhere in `BuildMenuUI.cs`).
