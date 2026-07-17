# Audit: Player & Controllers

Read-only audit (2026-07-15) of `Assets/3 - Scripts/Player/`, `Assets/3 - Scripts/Input/`, and `Assets/3 - Scripts/Scripts/Game/Controllers/` (PlayerController, Ship, InputSettings). No files were modified. The forbidden atmosphere/celestial zone was not touched.

## Summary
The subsystem is mature and heavily commented; movement, anti-tunneling, ship-proximity assist, and boost/fuel systems are internally consistent and mostly convention-compliant. The concrete defects are small: one real fuel-refuel field mix-up, a handful of unguarded null-derefs on rarely-hit paths, and a couple of convention deviations (a direct `transform.position` teleport on the player rigidbody). The larger opportunities are cleanup: dead methods, shipped diagnostic logging/light-replacement code in `Ship.Awake`, and duplicated circularize/orbit math between the player and the ship.

## Bugs

### Med — down-thrust refuel uses the wrong field
`PlayerController.cs:451`
```csharp
downThrustFuelPercent = Mathf.Clamp01(downThrustFuelPercent + Time.deltaTime / downThrustDuration);
```
The upward jetpack (`:445`) refuels via `jetpackRefuelTime` and directional thrust (`:457`) via `dirThrustRefuelTime`, but down-thrust divides by `downThrustDuration` — the *drain* time, not `downThrustRefuelTime`. As a result the serialized `downThrustRefuelTime` (`:34`) is never read (dead field), and tuning the down-thrust refuel rate in the inspector silently does nothing. Currently masked because both fields default to `2`; it only bites once they're tuned apart.
Fix: `... / downThrustRefuelTime`.

### Med — player teleport on pilot-exit writes transform, not rigidbody
`Ship.cs:1374-1375` (`StopPilotingShip`)
```csharp
pilot.transform.position = exitPoint.position;
pilot.transform.rotation = exitPoint.rotation;
```
Writes `transform.position/rotation` directly on the player's rigidbody-driven object instead of `pilot.Rigidbody.position` (+ `Physics.SyncTransforms`). This is exactly the class of bug called out in CLAUDE.md trap #3 and the ship-rebuild-pickup memory (transform lags physics; can seat the player inside a collider). `PilotShip` similarly sets `pilot.Camera.transform.*` (fine, that's the camera), but the body move should go through the rigidbody. Low-probability in practice (seat point is clear), hence Med not High.

### Low — cheat teleport dereferences a possibly-null FindObjectOfType
`Ship.cs:1035-1036` (`HandleCheats`)
```csharp
var shipHud = FindObjectOfType<ShipHUD>();
if (shipHud.LockedBody)   // NRE if no ShipHUD in scene
```
No null check. Only reachable with `Universe.cheatsEnabled` + Return pressed while piloting, so low impact, but it will NRE if a `ShipHUD` isn't present.

### Low — gravity loop assumes no null body entries
`PlayerController.cs:1182-1197`
```csharp
foreach (CelestialBody body in bodies) {
    float sqrDst = (body.Position - rb.position).sqrMagnitude; // no null guard
```
Both the player's orbit-match loop (`:1024`) and Ship's circularize loop (`Ship.cs:919`) null-check `b` before use; this gravity loop does not. If `NBodySimulation.Bodies` ever contains a null entry mid-teardown, this NREs 50×/s. Add `if (body == null) continue;` for parity.

### Low — animator used every frame without a null guard
`PlayerController.cs:467-468`
```csharp
animator.SetBool("Grounded", isGrounded);
animator.SetFloat("Speed", animationSpeedPercent);
```
`animator = GetComponentInChildren<Animator>()` in Awake (`:318`) is never null-checked. A player prefab/variant without an Animator child NREs every Update. Low because the shipping prefab has one.

### Low — unbalanced collision counter can go negative
`Ship.cs:1476` (`OnCollisionExit`)
```csharp
numCollisionTouches--;   // no `> 0` floor
```
The sibling `_nonGroundedContacts` decrement is guarded with `> 0` (`:1478`) but `numCollisionTouches` is not. A collider disabled/destroyed while in contact fires Exit without a matching Enter and drives the counter negative, so `IsLanded => numCollisionTouches > 0` reads false while actually landed (and the auto-align `numCollisionTouches == 0` gate mis-fires) until it rebalances.

### Low — Editor key overload on 'O'
`PlayerController.cs:1530` toggles `debug_playerFrozen` on `KeyCode.O`, while `:1003` uses held `O` for player orbit-match and `Ship.circularizeKey` also defaults to `O` (`Ship.cs:146`). In the Editor, pressing O simultaneously toggles the debug freeze and drives circularize. Editor-only (`HandleEditorInput` is gated on `Application.isEditor`), but confusing during playtests.

## Redundancies / Dead Code

- **`PlayerController.cs:34` `downThrustRefuelTime`** — declared, never read (see Bug #1).
- **`Ship.cs:1013` `GetInputAxis` and `Ship.cs:1021` `GatedAxis`** — both private helpers are defined but called nowhere in the codebase. Dead.
- **`Ship.cs:305-323`, `:388`, `:559-568`** — shipped diagnostic logging: `[Ship.Awake] headlight=...`, a "DIAGNOSTIC" full-component enumeration string-built per ship, and per-frame `Debug.LogWarning` headlight-shadow watchdogs while piloting. Debug noise that runs in builds.
- **`Ship.cs:326-389`** — the "REPLACEMENT — destroy the original Spot Light … spawn a fresh runtime Light" block is, by its own comments, a diagnostic workaround ("rules out any hidden corruption"). It snapshots ~15 light fields and creates a new GameObject on *every* ship Awake. If the root cause is settled it should be reduced to just configuring the existing light.
- **Duplicated circularize/orbit math** — `PlayerController.cs:1004-1077` and `Ship.cs:909-969` are near-identical (nearest-non-Sun body selection, `vCirc = sqrt(GM/r)`, tangential-dir fallback via `Cross(radialDir, up)` then `right`, single-tick overshoot clamp). Worth extracting to a shared static helper. Note they use divergent range constants for the *same* mechanic: player `rangeMul = 9` (`:1016`) vs ship `circularizeRangeMul = 3` default (`Ship.cs:144`).
- **Inventory facades** — `CrystalInventory`, `WoodInventory`, `SpaceDustInventory` are three ~80-line near-identical Hotbar facades (a fourth pattern instance is `PlayerWallet`). Minor inconsistency: `CrystalInventory.cs:63` and `WoodInventory.cs:64` `Debug.Log` on every add (log spam during dust/wood farming); `SpaceDustInventory.Add` does not. All four are correctly seeded in `MainMenuController.EnsureGameplaySingletons` (trap #1 verified OK).

## Performance / Optimization Opportunities

- **`Ship.RelativeVelocity` (`Ship.cs:1602-1621`) loops all celestial bodies on every property GET.** Multiple speed-driven FX (SpeedLinesOverlay, RadialMotionBlurEffect, per the comment) read it each frame, so it's O(bodies × readers) per frame with no caching. Cache the nearest-body result once per FixedUpdate.
- **`FindObjectsOfType` scans that could use the repo's `AllInstances` convention.** `PlayerController.FindNearestShipInRange` (`:1376`) runs `FindObjectsOfType<Ship>()` every 0.2s (allocates an array each time); `Ship.PilotShip` runs `FindObjectsOfType<Ship>()` + `FindObjectsOfType<Interactable>(true)` on each board. Per CLAUDE.md, iterating a static `Ship.AllInstances`/`Interactable.AllInstances` list would avoid both the scan and the GC alloc. The 0.2s throttle is reasonable, so this is a nice-to-have, not urgent.
- No per-frame `text +=` / string building found in scope (good). No `FindObjectOfType`/`Camera.main`/`GameObject.Find` in Update/LateUpdate/FixedUpdate except the cheat path (`Ship.cs:1035`), which is gated behind a keypress — acceptable.
- `PlayerController.Update` fill-rect writes (`:460-462`) and the many `new Vector3(...)` in the movement path are value types (no GC), fine. The fill-rect `localScale` assignments are unconditional each frame but cheap.

## Notes & Uncertainties

- **Direct `transform.rotation` / `transform.Rotate` on the player rigidbody** (`PlayerController.cs:1281` gravity-up align, `:736` yaw, `:673` `ForceLookAt`) deviate from the "write rotation with `rb.MoveRotation`" convention. This is long-standing, works with `RigidbodyInterpolation.Interpolate`, and the smoothing comments describe deliberate tuning — I did **not** flag it as a bug, only as a convention deviation to be aware of if orientation jitter is ever chased again.
- **`_hasGravitySim` is cached once in Awake** (`PlayerController.cs:314`). Correct for the current scenes (sim exists at load), but if an `NBodySimulation` were ever spawned after the player, the flat-gravity fallback would stay engaged. Not a bug today; noted as an assumption.
- **Bug #1 severity is currently masked** by both down-thrust fields defaulting to `2s`; verify the intended refuel rate before "fixing" in case the drain-time coupling was an accepted shortcut.
- I did not run the Editor/compile (no CLI build per CLAUDE.md); findings are from source reading. Line numbers are against the files as of this branch (`feat/helmet-hud`).
