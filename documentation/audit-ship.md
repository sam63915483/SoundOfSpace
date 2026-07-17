# Audit: Ship Systems

Read-only audit of the Ship subsystem, focused on `Assets\3 - Scripts\Ship\`
(crash-rebuild, reactor refuel, pickup/place, hatch) plus the shared
`Assets\3 - Scripts\Pickups\PlayerPickup.cs` that pickup/place depends on.
Scope-relevant supporting file: `Assets\3 - Scripts\Scripts\Game\Controllers\Ship.cs`
(read-only reference for the fuel/hatch/pilot accessors).

## Summary

The recently-fixed pickup/place flow (commit f1b9bde6) is sound: the drop-teleport
handling in `PlayerPickup.DropObject` (seat `rb.position`/`rb.rotation` then
`Physics.SyncTransforms()`) and the "gaze the green ghost, not the invisible mount"
change in `ThrusterMount.Update` both read as correct and well-commented. Rigidbody
writes on dynamic bodies use `rb.position`/velocity + explicit sync; per-frame
transform writes only happen on kinematic held objects, which is within convention.

Findings are mostly low-severity: one clear tutorial-gate inconsistency, a
single-flag place/drop race across overlapping mount triggers, a couple of
unguarded `GetComponent().enabled` null-deref risks, three copies of the
ghost-preview code, and some write-only state fields. No high-severity data-loss
or teleport bugs found in the reviewed files.

Counts: Bugs 6 (0 high, 2 medium, 4 low) · Redundancies/Dead code 4 · Perf notes 3.

## Bugs (severity, file:line, description, fix)

### 1. [Medium] Inconsistent TutorialAbility gate on the SpaceNet fallback install
`Assets\3 - Scripts\Ship\SpaceNetMountController.cs:128`
```
if (InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
```
Every other install path gates the interact press on `TutorialAbility.Pickup`
(`SpaceNetMount.cs:125`, `ThrusterMount.cs:117`). This distance-based fallback path
instead gates on `TutorialAbility.TalkToNPC` — almost certainly a copy-paste slip.
If the tutorial has unlocked Pickup but not TalkToNPC (or vice-versa), a player
holding a net can see the green ghost + prompt yet be unable to install (or be able
to install before the tutorial intends). Fix: change to
`TutorialGate.InteractPressed(TutorialAbility.Pickup)`.

### 2. [Medium] Place/drop suppression races across overlapping mount triggers
`Assets\3 - Scripts\Pickups\PlayerPickup.cs:80,283` + `ThrusterMount.cs:160-168`
`canPlaceRightNow` is a single shared bool on `PlayerPickup`, written independently by
every `ThrusterMount` via `SetCanPlace`. Each mount only pushes its own `canPlace`
transition, but if the player stands where two mount trigger zones overlap while
holding a Left thruster, the Right mount runs `UpdatePlacementState(false)` every
frame (wrong type) while the Left mount runs `UpdatePlacementState(true)`. The two
fight over the same flag; depending on script execution order the drop-suppression
in `PlayerPickup.Update` (line 80, `&& !canPlaceRightNow`) can flicker, so pressing
the drop key during placement occasionally drops the part instead of being
suppressed. Fix: make the flag a claim/refcount (mount registers/unregisters itself),
or have `PlayerPickup` derive "can place" from whether *any* matching mount is active
rather than a last-writer-wins bool.

### 3. [Low] Unguarded `GetComponent<Collider>().enabled` NREs if collider isn't on the root
`Assets\3 - Scripts\Pickups\PlayerPickup.cs:220`
```
obj.GetComponent<Collider>().enabled = false;
```
`PickupObject` assumes a Collider on the pickup root. `DropObject` (line 265) guards
the same access with a null check, so the code is internally inconsistent. Any
pickup prefab whose collider sits on a child (a plausible authoring mistake for the
thruster/net/dish prefabs) throws an NRE mid-pickup and leaves the item half-picked-up.
Fix: guard as `var col = obj.GetComponent<Collider>(); if (col != null) col.enabled = false;`.

### 4. [Low] Ghost-null gaze fallback reintroduces the razor-cone problem
`Assets\3 - Scripts\Ship\ThrusterMount.cs:116`
```
Component gazeTarget = _ghost != null ? (Component)_ghost.transform : this;
```
When `EnsureGhost` silently fails (`damageScript == null` at line 204, or
`GetChildForType` returns null), `_ghost` stays null and gaze falls back to the
renderer-less mount transform — exactly the ~6° cone that the f1b9bde6 fix was written
to avoid. The player then sees the "Press … to Place" prompt but can never satisfy the
gaze test, so placement silently fails. Usually harmless (ghost is normally non-null
by the time `playerInRange`), but the fallback defeats the fix in the failure case.
Fix: if `_ghost == null`, either skip the gaze requirement or don't show the prompt.

### 5. [Low] Unguarded ship Rigidbody lookup in SpawnPickup
`Assets\3 - Scripts\Ship\ThrusterDetachOnImpact.cs:190`
```
rb.velocity = GetComponent<Rigidbody>().velocity;
```
`GetComponent<Rigidbody>()` on the ship root is dereferenced without a null check. In
practice the ship root carries `Ship` + a Rigidbody so this holds, but a ship prefab
missing the body (or a future non-rigidbody carrier) NREs during the crash-detach flow,
aborting the rest of `SpawnPickup` for that part. Fix: cache the ship Rigidbody in
`Start` and null-check before use.

### 6. [Low] Duplicate SpaceNet install path — double ghost / double install if both components coexist
`Assets\3 - Scripts\Ship\SpaceNetMount.cs` + `SpaceNetMountController.cs`
Nothing prevents a ship from carrying both a per-mount `SpaceNetMount` (trigger-based)
and a root `SpaceNetMountController` (distance-based) targeting the same dormant net.
Both independently `EnsureGhost` a translucent clone at the identical mount position
(z-fighting / doubled transparency) and both can call `ReattachPart`/`SetActive(true)`
+ `Destroy(held)`. The design comments say "use one or the other," but there is no
runtime guard. Fix: have one defer if the other is present (e.g. `SpaceNetMountController`
skips a side that has a `SpaceNetMount` child), or document/enforce single-component use.

## Redundancies / Dead Code

- **Write-only state fields.** `thrustersDetached`, `dishDetached`, `solarPanelDetached`
  in `ThrusterDetachOnImpact.cs` (declared 50-52) are assigned in five places
  (154, 127/239, 135/244, 260-262, 285-287) but never read anywhere — confirmed by
  grep across the file; they are `private`. The line-154 assignment is also logically
  wrong (`thrustersDetached = (leftThrusterChild != null || rightThrusterChild != null)`
  — true merely because the refs exist), but it's inert because nothing reads it.
  Either wire them into the crash/flight logic or delete them.

- **Unused compatibility wrapper.** `ThrusterDetachOnImpact.ReattachThruster` (line 272)
  is a pass-through to `ReattachPart` "kept for compatibility," but `ThrusterMount` and
  `SpaceNetMount` both call `ReattachPart` directly; no caller of `ReattachThruster`
  exists in the codebase (grep-confirmed). Dead.

- **Three near-identical ghost implementations.** `EnsureGhost`/`SetGhostColor`/
  `DestroyGhost` + the two ghost tint colors are copy-pasted almost verbatim across
  `ThrusterMount.cs` (196-253), `SpaceNetMount.cs` (172-215), and
  `SpaceNetMountController.cs` (141-192). A shared `GhostPreview` helper (the project
  already has `GhostPlacement.MakeGhostMaterial`) would remove ~150 lines of triplicated
  strip-components/retint/destroy logic and keep the three install UXes in lockstep.

- **`ShipReassembly.UnfreezeShip`** (`ShipReassembly.cs:5`) has no code caller
  (grep-confirmed). Likely wired via a UnityEvent/animation event in a prefab, so not
  provably dead — see Notes.

## Performance / Optimization

- **Per-frame `GetComponent` in the mount Update loops.** `ThrusterMount.Update`
  (line 86 `held.GetComponent<ThrusterPickup>()`), `SpaceNetMount.Update` (106), and
  `SpaceNetMountController.Update` (86) call `GetComponent` on the held object every
  frame while a part is held. Cheap individually, but there can be many mounts per ship
  (two thruster mounts + two net mounts + the root controller) all polling every frame.
  Consider caching the held object's part component when the held object reference
  changes rather than each frame. Low priority.

- **`FindObjectOfType<PlayerPickup>` retry cadence.** `ThrusterMount.Update:78` and
  `SpaceNetMount.Update:91` call `FindObjectOfType<PlayerPickup>()` every frame while the
  reference is null (no throttle), unlike `SpaceNetMountController` which correctly
  throttles to 1s (`kFindRetryInterval`). On a ship whose PlayerPickup never resolves
  (e.g. before the player spawns) this is an unthrottled scene scan per mount per frame.
  Mirror the throttled-retry pattern from `SpaceNetMountController`. Low priority.

- **`ReactorPopup` uses `Camera.main`** (line 42) — only when `_cam` is null, so it's
  effectively cached; fine. No change needed, noted for completeness.

  (All the hot per-frame string builds — `ThrusterMount.ShowPrompt`,
  `PlayerPickup.HandleHoldToPickup`, `ShipReactor.ShowPrompt` — are already gated behind
  `_showingPrompt`/change-detection, so no per-frame string allocation was found.)

## Notes & Uncertainties

- **`ShipReassembly.UnfreezeShip` may be inspector-wired.** It sets `rb.isKinematic = false`
  and has no C# caller. It is plausibly hooked to a UnityEvent/animation event on a ship
  prefab (crash-rebuild "unfreeze" step). Cannot confirm from source alone — verify in the
  Editor before deleting. If it *is* the rebuild's unfreeze, note it bypasses the normal
  `Ship`/`ThrusterDetachOnImpact.canFly` gating (it only flips kinematic), so a ship could
  become physics-active without `canFly` being set — worth an Editor check.

- **Whether the two SpaceNet install components coexist on shipping prefabs** (Bug #6)
  can't be determined from scripts; confirm on the SHIP44 prefab in the Editor.

- **`ShipReactor` gaze target is the reactor component (`this`).** `InteractGaze.IsLookingAt(this)`
  (line 62) relies on the reactor child having renderer bounds for the gaze test. The class
  comment says the reactor child carries a trigger BoxCollider; if it has no Renderer, the
  gaze test may fall back to a narrow cone. Not verified — worth an in-Editor check that the
  refuel prompt is reliably confirmable by looking at the reactor.

- **Refuel over-fill is safe.** `ShipReactor.Refuel` computes `crystalsNeeded` with
  `Mathf.CeilToInt`, so `fuelAdded` can slightly exceed the deficit, but `Ship.RestoreFuel`
  (Ship.cs:82) clamps to `fuelMax`. No bug — the player just can't overfill. (Minor: the
  ceiling means the last crystal can be "wasted" fractionally; acceptable.)

- **`DetachAllParts` idempotency** is correctly preserved by the per-branch `activeSelf`
  guards and the `detachmentScheduled` in-flight lock (ThrusterDetachOnImpact.cs:71,111-152).
  The removal of the old `thrustersDetached || dishDetached` early-return (documented at
  lines 65-70) reads as a genuine fix for Hull/NoDish tiers, not a regression.
