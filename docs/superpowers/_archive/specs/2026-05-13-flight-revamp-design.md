# Flight Revamp Design

**Date:** 2026-05-13
**Author:** Sam (with Claude)
**Status:** Approved for implementation

---

## Goal

Make flying the ship feel natural and smooth in all three of the game's flight contexts:

1. **Orbiting a planet** — currently hard because circular orbit requires tangential velocity, which is unintuitive without a vector display.
2. **Getting to orbit** — currently jarring; even with the 4× Shift boost, fine-control thrust is too weak and boost is too punchy.
3. **Travelling between planets** — currently slow and awkward; no way to cancel relative drift to a target, so arrival is a guessing game.

Design philosophy: **Outer Wilds-style assist, not autopilot.** The player stays in control. We add two targeted assists (Match Velocity, Hold-O Circularize), one visibility upgrade (prograde/retrograde markers), and re-tune the existing boost/scale numbers.

---

## Context (current state)

Flight code lives in `Assets/3 - Scripts/Scripts/Game/Controllers/Ship.cs`. Per-ship state, gravity application via `NBodySimulation.CalculateAcceleration`, manual thrust on six axes (WASD horizontal/forward, Space up, LeftCtrl down). Rotation via mouse delta (yaw/pitch) + Q/E roll.

Already in place from earlier this session:
- `thrustStrength = 20` (legacy base value).
- `thrustPowerScale = 0.33f` — multiplier at the `AddForce` site. Default thrust ≈ 1/3 legacy strength.
- `boostMultiplier = 4f` — held LeftShift, per-axis boost.
- Three per-axis fuel pools (`_boostFuelUp`, `_boostFuelDown`, `_boostFuelDir`) mirroring the player's jetpack split (UP/DN/DIR).
- `GForceHUD` already renders speed tape + 3D thrust gimbal + segmented fuel bars; the bars auto-route to the piloted ship's boost fuel when in a cockpit.
- Map's `OnVelocityMatched` event exists but is currently cosmetic — only a tutorial step listens. The ship's physics aren't actually affected by clicking a body in the map.

What's missing: any assist for orbits, any way to cancel relative velocity, any visual representation of the velocity vector.

---

## Design

### 1. Match Velocity assist

**Trigger.** Hold **V** while piloting AND while a "target body" is selected (the map's `pendingHighlight` body — set by clicking a planet entry in the legend, persists across map close). If the player followed an extra ship, the followed ship is the target. `SolarSystemMapController` already exposes the followed body / ship — read from there.

**Behavior.** Each `FixedUpdate` while V is held:
1. Read the target's `velocity` (from `CelestialBody.velocity` or `Ship.Rigidbody.velocity`).
2. Compute `Vector3 relVel = rb.velocity - target.velocity`.
3. Apply a counter-acceleration: `rb.AddForce(-relVel.normalized * matchAcceleration, ForceMode.Acceleration)` capped so we don't overshoot zero (`Vector3.Max(0, magnitude - matchAcceleration * dt)` style clamp).
4. Drain `_boostFuelDir` at `boostDrainPerSec` rate. Stop matching when fuel reaches 0.

**Tuning.** `matchAcceleration = 8f` (m/s²) — at typical orbital speed deltas of ~30-80 m/s, gives a 4-10s zero. Player can release V at any time; partial cancellation is fine.

**Fail-safes.** If no target is selected, V does nothing (silent — no error log spam). If target is destroyed mid-match (e.g., a body or extra ship gone), bail out.

**HUD feedback.** The thrust gimbal's UI doesn't need to change. A small "MATCHING [Name]" status line under the speed tape would help. Optional.

**Files touched:** `Ship.cs` (V handler in `FixedUpdate`, new public `MatchVelocityTarget` accessor for HUD), `SolarSystemMapController.cs` (expose `pendingHighlight` and `followedShip` via public getters for Ship to read; some already exist via `FollowedShip`).

---

### 2. Hold-O Circularize assist

**Trigger.** Hold **O** while piloting AND while the ship is inside `circularizeRange = 3f * nearestBody.radius` of the nearest **Planet or Moon** (Sun excluded — its radius makes the trigger range cover most of the system, which would make O do unexpected things mid-cruise).

**Math.** Each `FixedUpdate` while O is held:
1. Find the nearest `CelestialBody` of type `Planet` or `Moon` (loop over `NBodySimulation.Bodies`, skip `Sun`).
2. Compute the ship-relative-to-body state: `r = rb.position - body.Position`, `v = rb.velocity - body.velocity`.
3. Decompose `v` into radial and tangential components:
   - `radialDir = r.normalized`
   - `vRadial = Vector3.Dot(v, radialDir) * radialDir`
   - `vTangential = v - vRadial`
4. We want all of `v`'s magnitude in `vTangential` (zero radial). Compute target velocity: same total magnitude, all tangential, same direction as current `vTangential`.
   - `vTargetTangential = vTangential.normalized * v.magnitude`
   - `vTarget = vTargetTangential` (plus `body.velocity` to convert back to world frame)
5. Apply thrust toward `(vTarget - rb.velocity).normalized` at `circularizeAcceleration` magnitude.
6. Drain `_boostFuelUp` at `boostDrainPerSec` rate.

**Tuning.** `circularizeAcceleration = 6f`. Takes ~2-3 seconds to convert a slightly elliptical orbit to a near-circle. `circularizeRange = 3f * radius` so it only works near planets, not in interplanetary space.

**Edge cases.** If `vTangential.magnitude < 0.1f` (almost no tangential velocity — straight up/down), pick a tangential axis arbitrarily based on the planet's "up" vector. If the ship is sitting on the surface (radius < body.radius * 1.05), do nothing — the player can't circularize at ground level.

**Files touched:** `Ship.cs` only.

---

### 3. Prograde / retrograde HUD markers

**Visual.** Two small triangle overlays on the cockpit screen, drawn by `GForceHUD` or a new sibling component. Green triangle marks the **prograde** direction (projected `rb.velocity` onto the camera plane). Red triangle marks **retrograde** (180° opposite). When the velocity vector points behind the camera, the markers clamp to the screen edge with a directional arrow.

**Coords.** Project velocity from world space onto the camera's view frustum: `Camera.WorldToViewportPoint(camPos + velDir * 100)`. Convert viewport to screen, render at that position. Hide when velocity magnitude < 1 m/s (stationary).

**Style.** Same cyan scanner palette as the rest of the cockpit HUD (`GalaxyHudKit` colors). Triangles ~24px on 1080p with a faint label ("PRO" / "RET"). Slight glow on prograde when V is held (visual cue that Match Velocity is engaged).

**Files touched:** new `Assets/3 - Scripts/Ship/VelocityMarkersHUD.cs` (auto-created singleton like `GForceHUD`), or extend `GForceHUD` directly. New file is cleaner.

---

### 4. Tuning revisions

Pure number changes in `Ship.cs` defaults; existing prefabs pick up new defaults automatically since these are existing serialized fields.

| Field | Old | New | Reason |
|---|---|---|---|
| `boostMultiplier` | 4 | **2.5** | With `thrustPowerScale = 0.33`, boost ≈ 0.83× legacy strength. Punchy without lurching. |
| `boostRefillPerSec` | 0.25 | **0.4** | ~2.5s to refill from empty. Reduces waiting between bursts. |
| `thrustRampSeconds` (new) | — | **0.15** | Input ramps 0 → full over 150ms (per-axis). Eliminates "tap feels binary" / "1-second press changes orbit by 20 m/s" abruptness. |

**Ramp implementation.** Add `Vector3 _smoothedThrusterInput` to `Ship`. In the input gather block, lerp toward raw `thrusterInput` at `1/thrustRampSeconds` rate. AddForce uses smoothed value. Keep raw `thrusterInput` available for the boost-axis detection (so boosting starts immediately on key down rather than waiting for the ramp).

`boostDrainPerSec` stays at 0.5 (2 seconds of sustained boost per pool is fine — Match Velocity covers long-duration cancellation, boost is for bursts).

---

## How sections interact

- **Short hop / fine maneuver:** smoothed thrust + new prograde marker. No assist key. Player feels in control because they can see the velocity vector and the input doesn't snap.
- **Orbit insertion:** fly up (Shift+Space for boost), release boost, hold O for ~2 seconds, release. Now orbiting.
- **Interplanetary travel:** open map, click target body to focus it. Close map, point ship at target (camera + prograde marker help), Shift+W to boost toward it. Mid-flight coast. As you approach, hold V to cancel relative drift. Release near target — you're hovering next to it.

No combination requires more than one assist key + manual thrust + Shift. No new control surfaces beyond V and O.

---

## Non-goals

- **No autopilot / cruise mode.** The player always steers.
- **No KSP-style maneuver nodes.** Predicting future orbits is too complex for this game's scope.
- **No fuel cost beyond the existing boost pools.** Match Velocity and Circularize drain existing pools, not new ones.
- **No changes to rotation handling.** Current `rotSpeed = 5` + mouse sensitivity slider is fine.
- **No multiplayer / latency considerations.** Single-player game.

---

## Risk / Open Questions

1. **Match Velocity target ambiguity.** What if both a body AND an extra ship are highlighted? Resolution: extra ship takes priority (more recent intent — you focused the ship by clicking its legend row).
2. **Circularize at the equator vs poles.** Tangential decomposition handles all latitudes correctly because we use the actual velocity vector, not assumed "horizontal". No special case needed.
3. **Floating-origin shifts during V hold.** When `EndlessManager` shifts, both `rb.velocity` and `target.velocity` shift by the same amount (delta is position-only, velocities are unaffected). Relative velocity calculation stays correct.
4. **HUD markers behind the camera.** Edge-clamping is annoying if implemented badly. Acceptable fallback: just hide markers when velocity dot camera-forward < 0.
5. **V key conflict.** `V` is currently unused in piloted state. Verified via grep — no existing binding. `O` also unused.
6. **Boost pool drain by Match Velocity.** Could feel weird if V cancels velocity AND drains DIR fuel, then you can't strafe-correct after. Mitigation: `boostRefillPerSec = 0.4` means it refills fairly quickly. Acceptable.

---

## Implementation order (rough)

1. Tuning revisions (Section 4) — smallest change, immediate feel improvement.
2. Match Velocity (Section 1) — biggest UX win.
3. Hold-O Circularize (Section 2) — second biggest UX win.
4. Prograde/Retrograde HUD (Section 3) — polish, helps the above feel intentional.

Each can ship independently; later sections build on earlier ones but don't strictly require them.

---

## Out-of-scope follow-ups (noted, not designed here)

- **Mid-cruise stabilization** — if a long boost is hard to steer, consider a "hold heading" assist. Add later if needed.
- **Match Velocity controller binding** — V is keyboard; needs a gamepad equivalent (suggest Left Bumper while not used by roll).
- **Audio cue on Match Velocity engagement** — could play a soft beep when relative velocity reaches near-zero.
