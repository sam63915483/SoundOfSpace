# Space free-float (2026-09-15)

🟢 ACTIVE — approved by Sam 2026-09-15.

## What changes for the player

Today the player's feet are forced to point at the nearest planet every physics
tick, everywhere. Past a planet's **atmosphere line** that lock now releases:

- Mouse/right-stick up-down pitches the whole body (no ±89° clamp — you can loop).
- Left-right yaws about your own up, as now.
- **Q / E** (pad LB / RB) roll.
- Jetpack up-thrust becomes "thrust where my head points"; directional thrust is
  already body-relative, so it follows.
- Gravity keeps pulling; only the orientation lock changes. Orbits are unchanged.

Crossing the line **outward** folds the camera's current pitch into the body over
~0.5 s so the view never jumps. Coming back **inside** any body's line, the body's
up is rotated back toward that planet over `shipUpBlendSeconds` (the existing
planet-to-planet blend), and the camera pitch resumes from level.

## The line

`AtmosphereBounds.Radius(body)` — already exists, already has hysteresis (8%
further descent to leave space) in `PlayerController.UpdateSpaceGate`. Default is
the visual haze edge `(1 + atmosphereScale) × radius`; airless bodies get
`1.5 × radius`. New per-body knob `CelestialBody.atmosphereLineMultiplier`
(0 = default) so any planet's line can be tuned without touching the forbidden
atmosphere code. Tuning is the expected follow-up.

## Priority order (highest first)

1. Cinematic up override (shuttle intro) / rider mode / flat interiors — never free.
2. Grounded — never free (standing on the shuttle roof in orbit stays as today).
3. Ship-proximity zone (within 25 m of an orbiting shuttle) — aligns to the ship's
   up as today, so boarding still works.
4. Otherwise in space → free.

## Side effects handled

- **E is the flashlight toggle.** While free-floating the toggle is suppressed
  (`PlayerController.FreeFloating`), otherwise rolling right flicks the torch.
- Camera pose is interpolated from physics snapshots by `CameraTransformFX`; the
  pending (not yet applied) pitch/roll is added at render rate exactly like yaw.
- Co-op puppets copy the full rotation (`PlanetRelativeSync`) — nothing to do.

## Files

`PlayerController.cs` (gate, input, apply, fold, blend seed), `CameraTransformFX.cs`
(pending pitch/roll), `CelestialBody.cs` + `AtmosphereBounds.cs` (knob),
`LightToggle.cs` + `PlayerFlashlight.cs` (E suppression).
