# Audit: Physics & World

Read-only audit of the floating-origin / N-body core and the `World/` prop-streaming
systems. Scope: `Assets/3 - Scripts/Physics/`, `Assets/3 - Scripts/World/`, and the
non-forbidden core files under `Assets/3 - Scripts/Scripts/Game/`
(`NBodySimulation`, `EndlessManager`, `GravityObject`, `Universe`, `GameSetUp`,
`CelestialBody` runtime state). No files were modified.

## Summary

The floating-origin core (`EndlessManager`) is in excellent shape — heavily
commented, defensively null-guarded, correct use of `rb.position` as the shift
reference, and a well-reasoned two-frame interpolation / bone-kinematic pipeline.
The World prop spawners (`TreeSpawner`, `AlienNPCSpawner`, etc.) all use the
**correct** floating-origin pattern: spawned props are parented to the
`CelestialBody` transform so they ride the planet hierarchy through an origin
shift — no `RegisterPhysicsObject` needed, and none is missing. `SpaceDustField`
is deliberately origin-invariant. `BlackHoleCapture` correctly drives motion with
`rb.MovePosition`.

The real weak spot is `NBodySimulation` + `CelestialBody`: the body list is
captured once and never null-checked per-element (crash risk if a body is ever
destroyed), the gravity math has no softening (NaN risk at r=0), and there is a
chunk of dead code (`GravityObject`, the unused O(n²) `UpdateVelocity` overload).

Counts: 5 bugs (0 high / 2 medium / 3 low), 4 dead-code items, 4 perf/optimization notes.

## Bugs (severity, file:line, description, fix)

### BUG-1 (Medium) — Destroyed/stale CelestialBody crashes the whole sim
`NBodySimulation.cs:11` captures `bodies = FindObjectsOfType<CelestialBody>()`
once in `Awake`. The `FixedUpdate` loops (`:17-29`) and the static
`CalculateAcceleration` foreach (`:40-45`) then dereference each element with no
per-element null check:
- `:19` `bodies[i].isStaticAttractor`
- `:21` `CalculateAcceleration(bodies[i].Position, …)`
- `:43-44` `body.Position` inside the foreach

If any `CelestialBody` is destroyed at runtime, Unity's overloaded `==` reports
the element as null, but `.Position` / `.isStaticAttractor` throw
`MissingReferenceException` — thrown **every FixedUpdate**, which halts the entire
N-body integration (all orbits freeze). The static `Bodies` accessor (`:51-56`)
and `CalculateAcceleration` (`:39`) guard `inst.bodies == null` but never guard
individual dead elements. Also: bodies spawned *after* Awake are never added, so
they neither attract nor are integrated.
**Severity Medium** because in current play bodies persist for the session; a
save-reload that rebuilds the scene, or any future "destroy a body" feature, would
trip it.
**Fix:** add `if (bodies[i] == null) continue;` in both FixedUpdate loops and
`if (body == null) continue;` in the `CalculateAcceleration` foreach. Optionally
re-acquire the array (or expose a re-scan) when a body is added/removed.

### BUG-2 (Medium) — No gravitational softening → NaN at zero distance
`NBodySimulation.cs:43-45`:
```
float sqrDst = (body.Position - point).sqrMagnitude;
Vector3 forceDir = (body.Position - point).normalized;
acceleration += forceDir * Universe.gravitationalConstant * body.mass / sqrDst;
```
When `point` coincides with a body centre, `sqrDst == 0`: `forceDir` becomes
`Vector3.zero` (normalize of zero) and the `/ sqrDst` yields `Infinity`, so
`acceleration` becomes `NaN`. `GravityObjectSimple.FixedUpdate` (`GravityObjectSimple.cs:16-17`)
feeds that straight into `rb.AddForce`, and the ship/player consumers do the same
— a NaN velocity makes the body disappear irrecoverably. Notably
`BlackHoleCapture.cs:81` *does* guard this (`Mathf.Max(dist*dist, 1f)`) but the
core sim does not. **Severity Medium** (unlikely to be exactly centred, but the
failure is catastrophic and permanent when it happens; huge near-centre forces
also fling bodies before reaching exactly 0).
**Fix:** clamp the denominator, e.g. `float sqrDst = Mathf.Max((body.Position - point).sqrMagnitude, minSqr);`
with a small softening constant. Same fix applies to the dead
`CelestialBody.UpdateVelocity` overload (`CelestialBody.cs:36-39`) if it is ever revived.

### BUG-3 (Low) — `CalculateAcceleration` ignores `ignoreBody` when it's destroyed
`NBodySimulation.cs:41` `if (body == ignoreBody) continue;` — fine when both are
live, but this is the same class as BUG-1: the guard is by reference identity and
does not protect against `body` being a destroyed object; the subsequent
`body.Position` still throws. Folded into BUG-1's fix (null-check the element).

### BUG-4 (Low) — Static-attractor exclusion missing from the dead O(n²) overload
`CelestialBody.cs:33-43` `UpdateVelocity(CelestialBody[] allBodies, …)` sums
gravity from every other body with **no `isStaticAttractor` check**, so if it were
ever re-enabled (it is currently dead — see DEAD-2) the black hole would perturb
the planet/sun orbits, which the live path at `NBodySimulation.cs:33-42` was
specifically written to prevent. Latent correctness trap. **Fix:** delete the
overload (preferred, see DEAD-2), or add `if (otherBody.isStaticAttractor) continue;`.

### BUG-5 (Low) — `GameSetUp.OnBody` writes `transform.position` on physics bodies
`GameSetUp.cs:36-39` sets `player.transform.position` and `ship.transform.position`
directly (rather than `rb.position`) during `StartCondition.OnBody`. This violates
the repo convention (CLAUDE.md: "never assign `transform.position` on a body") and
can leave the physics position lagging the transform for one step, risking a
depenetration pop on spawn. It only runs once at scene start (and only for the
`OnBody` start mode, which the shipping start likely doesn't use), so impact is
low, but it is the same bug pattern the save system is careful to avoid.
**Fix:** set via `rb.position` (and call `Physics.SyncTransforms()` if needed), or
route through the same helper the save/`SetVelocity` path uses.

## Redundancies / Dead Code

- **DEAD-1 — `GravityObject.cs:5-9` is an entirely empty class.** It exists only
  as the base type of `CelestialBody`. Harmless, but pure scaffolding; the whole
  file could be removed and `CelestialBody` derive from `MonoBehaviour` directly.
- **DEAD-2 — `CelestialBody.UpdateVelocity(CelestialBody[], float)` (`:33-43`)** is
  never called. The live sim uses the acceleration-based overload
  (`NBodySimulation.cs:22`); the old all-pairs call is commented out at
  `NBodySimulation.cs:23`. Dead, and carries BUG-4's latent trap. Delete both the
  overload and the `:23` comment.
- **DEAD-3 — `NBodySimulation.cs:23`** commented-out
  `//bodies[i].UpdateVelocity (bodies, Universe.physicsTimeStep);` — stale.
- **DEAD-4 — `BlackHoleCapture` spin knobs** (`spinSpeedMax`, `spinStartRadius`,
  `swayMax`, `:335-339`, `:229`) are serialized but intentionally never applied
  (documented in-code as disabled to avoid strobing). Not a defect — flagged only
  so a future reader knows they're inert. Leave as-is per their own comments.

## Performance / Optimization

- **PERF-1 — N-body is O(n²) per FixedUpdate but cheap in practice.** Each body
  calls `CalculateAcceleration` which loops all bodies (`NBodySimulation.cs:17-24`).
  With the handful of bodies in this solar system this is negligible, and there are
  **no per-frame allocations** in the hot path (`foreach` over a plain array, no
  closures). No action needed unless the body count grows a lot.
- **PERF-2 — `NBodySimulation.Instance` can call `FindObjectOfType` from a static
  hot path.** `CalculateAcceleration` (`:38`) reads `Instance` every call, and
  every `GravityObjectSimple`/ship/player calls it each FixedUpdate. While
  `instance` is cached this is free, but if the sim is ever absent (e.g. off the
  solar scene) each call runs `FindObjectOfType` (`:60-62`). Minor; consider
  caching a "searched and empty" sentinel if this ever shows up in a profile.
- **PERF-3 — `MoonBaseDoor.ComputeAxis` re-finds the moon every animation frame.**
  `MoonBaseDoor.cs:93` calls `ColdCompany.FindMoon()` inside `ApplyState`, which
  runs every frame of the open/close coroutine (`:69`). Only a ~1.2s window per
  cycle, but the moon body could be cached once in `Start`. Low priority.
- **PERF-4 — Spawner `GetViewerPosition` fallback uses `Camera.main`.**
  `TreeSpawner.cs:159`, `AlienNPCSpawner.cs:202`, `CrystalSpawner.cs:185`,
  `GrassSpawner.cs:221`, `MushroomSpawner.cs:177` all call `Camera.main` — but only
  as a fallback when `player`/`player.Camera` is null, and only inside `Tick()`
  which is throttled to `updateInterval` (0.25s), not per-frame. Acceptable; noted
  for completeness. All other `FindObjectOfType`/`Camera.main`/`FindGameObjectWithTag`
  uses in `World/` (mirror, popups, grass, dust) are correctly lazy-cached and/or
  throttled (`MirrorFacePlayer.cs:30-33` throttles to 1s; popups cache `Camera.main`).

## Notes & Uncertainties

- **Floating-origin correctness is solid.** `EndlessManager.UpdateFloatingOrigin`
  (`EndlessManager.cs:142-269`) uses `playerRigidbody.position` (not the interpolated
  transform) as the shift reference, shifts each body via `rb.position -= offset`
  then syncs the transform, disables interpolation across the shift with a
  two-frame restore, kinematicizes ragdoll bones for the shift + one physics step,
  and calls `Physics.SyncTransforms()` — all correct and well-justified given
  `autoSyncTransforms = false`. Bodies keep their `velocity` across a shift (only
  position is rebased), so orbits are preserved. No issues found here.
- **No missing `RegisterPhysicsObject` calls were found.** Every runtime-spawned
  World prop (`TreeSpawner.cs:366`, `AlienNPCSpawner.cs:482`, and the crystal/
  mushroom spawners by the same pattern) parents the instance under
  `entry.body.transform`, so it rides the already-registered planet through origin
  shifts. Props have no independent world-space Rigidbody, so registration would be
  redundant. Pooled props are reparented to root but are `SetActive(false)` while
  pooled and repositioned on next spawn, so a shift can't desync them.
- **Dead-alien ragdolls** become Rigidbodies on death but stay parented under the
  planet and are handled by `EndlessManager`'s `RagdollBoneRegistry` kinematic guard
  (`:184-204`) — verified consistent with the spawner parenting. I did not open
  `AlienNPCDamageable`/`RagdollBoneRegistry` (outside scope) to confirm bones are
  registered there; the EndlessManager side is correct.
- **`CelestialBody.ApplySavedState` (`:84-91`)** correctly sets both `rb` and
  `transform` position/rotation to avoid a one-frame drift on save-load. Good.
- I did **not** inspect the forbidden generation/shading code; `SpaceDustField`'s
  atmosphere-radius reflection (`SpaceDustField.cs:467-507`) reads that data
  read-only via reflection, which is the sanctioned way and looked correct.
- **Uncertainty:** whether any code path actually destroys a `CelestialBody` at
  runtime (BUG-1's trigger). I found no destroy call in scope, but the save/reload
  and black-hole/backrooms transitions were not fully traced. Even if none exists
  today, the per-element null guard is cheap insurance against a hard-to-diagnose
  future crash.
