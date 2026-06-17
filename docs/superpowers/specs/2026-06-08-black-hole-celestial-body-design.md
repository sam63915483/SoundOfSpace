# Black Hole as a Selective-Gravity Celestial Body

**Date:** 2026-06-08
**Status:** Approved, implementing
**Branch:** feat/oxygen-atmosphere-system (current working branch)

## Goal

Turn the decorative Scingularity black hole (a screen-space shader on a quad,
currently parented to the Sun) into a first-class `CelestialBody` so the player
can:

- Look at it and left-click to select it (approach m/s readout) like any planet.
- Hold **V** to match velocity with it.
- Press **O** to orbit it.
- Have on-foot camera/gravity orientation snap to it when within its pull.
- Feel its gravity from the ship.

…while it pulls **only the player and the ship** — never the Sun or planets
(double-the-sun gravity on the planets would wreck their orbits).

## Key constraint discovered

All of those features read from one shared list — every `CelestialBody` in the
scene (`NBodySimulation.Bodies` = `FindObjectsOfType<CelestialBody>()`). So the
black hole **must** be a real `CelestialBody`. But a normal body also pulls every
other body via the mutual N-body loop. The player and ship draw gravity from that
same list, so the fix is to remove the black hole from *only* the body-on-body
loop.

## Decisions (confirmed with user)

- **Strength:** mass = **2× the Sun** (`2.25e12`). Pulls exactly twice as hard as
  the Sun at any distance; felt only by player + ship.
- **Motion:** **stationary** — fixed landmark above the Sun at world `(0,0,30000)`.
  You orbit it; it orbits nothing.

## Design

### 1. `isStaticAttractor` flag (`CelestialBody.cs`)

Append one serialized field at the end of the existing fields (preserves
serialization order per repo convention):

```csharp
public bool isStaticAttractor = false;
```

Semantics: still attracts player + ship and is fully targetable, but is **not** a
gravity source for other bodies and is **not** integrated by the sim.

### 2. `NBodySimulation.cs` — two additive changes

- `CalculateAcceleration(point, ignoreBody = null, bool includeStaticAttractors = true)`
  — when `false`, skip static-attractor bodies as gravity sources.
- `FixedUpdate` body loop: `continue` past static attractors (so the black hole
  never moves) and pass `includeStaticAttractors: false` (so planets aren't
  pulled by it).

**Safety:** every existing body has `isStaticAttractor == false` and the new
parameter defaults `true`, so all existing callers (ship `CalculateAcceleration`,
ragdolls, GravityObjectSimple) and the sun/planet orbits run identically. Only the
flagged black hole behaves differently.

### 3. Consumers — work with zero extra wiring

- **Player on foot:** its FixedUpdate loops every `NBodySimulation.Bodies`
  applying `G·m/r²` and tracks the closest-surface body as `referenceBody`. The
  black hole pulls it and becomes the orientation target when nearest.
- **Ship:** `NBodySimulation.CalculateAcceleration(rb.position)` (default `true`)
  → feels the pull.
- **Look-at / approach m/s / V / O:** `ShipHUD.FindAimedBody` (ray-sphere vs
  `radius`), relative-velocity readout, `Ship` match-velocity (`velocity`), and
  `Ship` circularize (`mass`, excluded only for `bodyType == Sun`). The black hole
  is `bodyType = Planet`, so **O works**.

### 4. Numbers

- `bodyName = "Black Hole"`, `bodyType = Planet`, `isStaticAttractor = true`,
  `initialVelocity = 0`.
- `radius = 4000`, `surfaceGravity = 14.06` → `mass = surfaceGravity·radius²/G ≈
  2.25e12` (2× Sun). Radius sets target sphere, orbit shell (~4,200–12,000 units),
  and orientation flip distance. All tunable.

### 5. Scene structure

- New top-level GameObject under `--- Celestial --- / Body Simulation` (sibling of
  Sun), at world `(0,0,30000)`, with `CelestialBody` + kinematic `Rigidbody`
  (`isKinematic = true`, `useGravity = false`). `OnValidate` names it "Black Hole".
- The existing Scingularity visual quad (shader + large-bounds mesh, scale 1500)
  becomes its **child** at local origin, renamed "Singularity Visual".
- Not parented to the Sun: as a `CelestialBody` it is origin-shifted directly by
  `EndlessManager`; re-parenting under the Sun would double-shift it.

### 6. Save / load

`CaptureCelestialBodies` / `ApplyCelestialBodies` iterate the body list and match
by `bodyName`, so the new body saves and restores automatically. Old saves lacking
it leave it at its scene position. Placed-building capture only grabs `*_Placed`
children, which the visual is not. No changes needed.

## Out of scope (YAGNI)

- **Map-click selection** — needs a collider on the Body layer; a solid collider
  would physically block the ship. In-cockpit look-at (what was requested) works
  without one. Possible follow-up.
- **Event-horizon death/damage FX** — the gravity well already makes close
  approach dangerous.

## Risks / verify in playtest

- Confirm planets' orbits are unchanged (the safety claim).
- Confirm ship feels the pull, orientation snaps on foot, O can insert an orbit.
- `OrbitDebugDisplay` (editor gizmo) may include the static attractor in its
  virtual prediction; cosmetic only.
