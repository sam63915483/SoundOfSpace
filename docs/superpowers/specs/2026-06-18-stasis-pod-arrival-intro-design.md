# Stasis-Pod Arrival Intro — Design

**Date:** 2026-06-18
**Status:** Approved (design); pending implementation plan
**Author:** Sam (with Claude)

## Summary

Add a ~30-second cinematic intro that plays on a **fresh New Game**, *before* the
existing cabin wake-up sequence. The player begins inside a stasis pod with a
window, hurtling toward the home planet **Humble Abode** from far out in the solar
system. For the first ~20 seconds they drift in and can free-look out the window at
the whole system; the final ~10 seconds are a proximity-alert countdown as the pod
accelerates and slams into Humble Abode. On impact the screen cuts to black, and the
**existing** wake-up sequence resumes unchanged (click-to-wake in the cabin, HAL
briefing).

The fiction: the pod has been in transit for three years and is waking the player
just before the crash — which dovetails with the existing wake-up line "You have
been in transit for three years. You crash-landed on this world two days ago."

## Goals

- Give the player a 30s beat to take in the real solar system as they arrive.
- Build tension with a 10s countdown to impact, then cut to black.
- Hand off seamlessly into the current wake-up flow with zero changes to that flow.
- Touch **none** of the forbidden atmosphere/planet-generation code (CLAUDE.md trap #2).

## Non-Goals

- No finished pod art. The pod interior is a rough runtime-built placeholder we
  polish in-Editor afterward.
- No HAL voice audio yet — subtitles only for now (TTS clips can come later).
- No new save state. The intro is gated by existing one-shot flags.

## Chosen Approach

**In-scene cinematic that moves the PLAYER through space, never a detached camera.**

> **REVISED 2026-06-18 after implementation.** The first attempt detached the player
> camera and flew it out on a "pod rig". It failed: this is a floating-origin game
> (`EndlessManager` recenters the world on the player's `rb.position`), and a camera
> parked at absolute world coordinates renders garbage — it showed the cabin even
> though the camera was provably ~4000 units out in space. The home planet also
> orbits fast, so anything not anchored to the player drifts. See the memory note
> `floating-origin-move-player-not-camera`.

The correct approach moves the **player** (the floating-origin anchor) and lets the
camera ride along as its normal child — so `CameraTransformFX` and the atmosphere /
planet post-effects keep working untouched. We never edit the generation/shader
code and never detach the camera.

Rejected alternatives: (a) detaching the camera (fails — see above); (b) a dedicated
cutscene scene with faked planet models + a load seam at impact (planets wouldn't be
the real ones, plus a load hitch at the cut-to-black).

## Insertion Point

`Assets/3 - Scripts/Tutorial/IntroSequenceController.cs`, in the `Start()` coroutine,
between the existing lines:

```csharp
EarlyGameProgress.IntroPlayed = true;
yield return _podArrival.Play();   // NEW — runs while the black overlay is up
yield return RunSequence();        // existing cabin wake-up, unchanged
```

The black eyelid overlay built in `IntroSequenceController.Awake()` is already up at
this point, so there is no spawn-frame flash. The pod sequence fades *out* of black
to reveal space, flies in, crashes, then fades *back* to black, handing a clean black
screen to `RunSequence()`.

Both the existing `PendingLoad.Data == null` (new-game-only) guard and the
`EarlyGameProgress.IntroPlayed` one-shot already gate this seam, so Load/respawn
never replays the pod intro. No new flag required.

## Implemented mechanics (player-move approach)

`PodArrivalSequence.Setup()`: freeze the player kinematic (`rb.isKinematic = true`,
the trick `StartCabinSpawnPoint` uses on the ship), `TutorialGate.LockAll()` then
`Unlock(MouseLook)` (free-look, no movement, via PlayerController's own look code),
place the player at `planet.Position + dir * startDistance`, and `ForceLookAt` the
planet so they start looking out the window.

Flight: in `LateUpdate` (default order 0, runs before `CameraTransformFX` at order
100) set `rb.position`/`transform.position` to `planet.Position + dir * distance(t)`
— RELATIVE to the live planet position each frame, so it survives the planet's orbit
and floating-origin rebases. Only position is driven; rotation stays owned by the
look pipeline. The placeholder pod box is a separate object positioned at eye height
each frame with a fixed orientation facing the planet, so looking around pans the
dark interior with the window/planet ahead.

Crash/teardown: teleport the player to `pc.spawnPoint` (the cabin, live position so
it survives the orbit), `pc.SetVelocity(parentBody.velocity)`, `isKinematic = false`,
destroy the pod + overlay → the normal wake-up runs. `Teardown` is idempotent and
also runs from `OnDisable`/`OnDestroy` so an interrupted run never strands the player.

## Original component sketch (superseded by the above — kept for context)

### New Component: `PodArrivalSequence`

A single self-contained scene-placed MonoBehaviour (lives on the same object as, or a
sibling of, `IntroSequenceController`), exposing `public IEnumerator Play()`. It is
referenced by `IntroSequenceController` via a serialized field (appended at the END
of that class per the serialization convention).

### Responsibilities

1. **Locate Humble Abode** — iterate `NBodySimulation.Bodies` for the body whose
   `bodyName == "Humble Abode"`; read `body.Position` (never `transform.position`).
   Null-safe: if not found, skip the cinematic and fall straight through to
   `RunSequence()` (fail safe — never block the wake-up).

2. **Build the pod rig at runtime** (mirrors how the overlay is built
   programmatically in `BuildOverlay()` — no prefab needed, nothing left in the scene):
   - An empty `PodRig` GameObject placed at
     `HumbleAbode.Position + approachOffset.normalized * startDistance`, oriented to
     look toward Humble Abode so the system spreads across the view.
   - A **placeholder pod interior**: a Unity capsule primitive, scaled up and rendered
     inside-out with a dark material, with a forward **window opening** and a simple
     frame ring. Looking around with the mouse shows the dark stasis-pod shell, with
     the solar system visible through the window. Deliberately crude; refined in-Editor.
   - **Reparent `Camera.main` under `PodRig`** and **disable the `PlayerController`
     component** so nothing else drives the camera. The mouse rotates the camera
     *locally* (clamped pitch/yaw) → look-around inside a fixed pod that flies its own
     scripted path.

3. **Own a full-screen fade** (its own overlay canvas, above the intro overlay) so the
   reveal/cut-to-black is fully self-contained. During the pod sequence the intro
   overlay underneath is hidden; at handoff it is restored to black/eyes-shut.

4. **Flight + beats (30s total, all durations serialized):**
   - **~0–20s — calm approach:** the pod drifts toward Humble Abode along an eased
     path; the player free-looks out the window. HAL **subtitle** lines play via the
     existing HAL HUD pipeline (e.g. "Stasis cycle complete." / "Approaching Humble
     Abode.").
   - **~20–30s — countdown:** a diegetic pod-console UI shows **"PROXIMITY ALERT /
     IMPACT IN 10…"** counting down; alarm beeps + rising rumble; the pod accelerates
     hard toward the planet; screen shake ramps.
   - **t = 30s — impact:** hard shake + flash, instant **cut to black**.

5. **Handoff:** restore the camera to the player head (reparent to the player,
   `localPosition = cameraLocalPos`, reset local rotation — mirroring the existing
   restore at `PlayerController.cs:1265-1269`), re-enable `PlayerController`, destroy
   the `PodRig`, ensure the intro black overlay is showing, then `return`/end the
   coroutine. `RunSequence()` proceeds exactly as today.

6. **Skip:** pressing **Esc** at any point → fast fade to black → the same handoff
   path. Guarantees a clean state regardless of when it's pressed.

## State & Safety

- New-game-only and one-shot via the existing `PendingLoad.Data == null` +
  `EarlyGameProgress.IntroPlayed` guards. No new `SaveData` field, no `NewGameReset`
  change needed.
- `StoryDirector.HoldColdOpen` and `PlayerPhoneUI.SuppressFirstNag` are already set
  true in `IntroSequenceController.Awake()` and remain held through the pod sequence,
  so no phone/first-contact beat fires mid-cinematic.
- The player body stays frozen at the cabin spawn (existing `StartCabinSpawnPoint` /
  `GameSetUp` logic). Floating origin (`EndlessManager`) does not recenter because the
  player never moves, so the camera can roam to large coordinates freely without
  disturbing world streaming.
- Single `AudioListener` preserved (we reuse the one camera). Alarm/HAL audio is 2D
  (`spatialBlend = 0`).
- `OnDestroy` / abort safety: if the sequence is torn down early (scene reload), the
  camera must be restored and `PlayerController` re-enabled so we never strand a
  detached camera. Mirror the defensive cleanup pattern already in
  `IntroSequenceController.OnDestroy`.

## Tunables (serialized, for live Editor tuning)

- `startDistance` — how far out the pod begins.
- `approachOffset` — direction from Humble Abode the pod approaches from (sets framing).
- `approachDuration` (default 20s), `countdownDuration` (default 10s).
- Initial look direction; mouse look sensitivity + pitch/yaw clamps.
- Acceleration curve, screen-shake intensity/ramp, rumble level.
- HAL line strings + subtitle timings; countdown start value.

## Forbidden-Zone Compliance

We **only** move the existing player camera's transform and reparent it temporarily.
We do **not** edit `Atmosphere.cs`, `CustomImageEffect.cs`, anything under
`Post Processing/Planet Effects/` or `Celestial/`, nor any planet/ocean/atmosphere
shader or material. `CelestialBody` is read-only here (position lookup only), which is
explicitly allowed.

## Testing

- Editor Play in `1.6.7.7.7.unity` via New Game → watch the full 30s: verify
  look-around out the window, the calm approach framing, the 10s countdown UI + alarm,
  the impact + cut-to-black, and a clean landing into the normal click-to-wake.
- Press **Esc** mid-flight → verify fast fade to black and clean handoff.
- Load an existing save → verify the pod intro is skipped entirely (and the wake-up
  too, as today).
- Iterate framing/timing/art live using MCP screenshots.
