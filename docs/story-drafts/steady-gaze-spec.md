# Steady Gaze — mechanic spec

The one genuinely new mechanic in the plan: raise the phone's camera and
everything in frame is *Observed*. It weaponizes the observation theme with
code that already exists (`ObserverState`'s frustum test, the Photos app
camera, the look-to-interact reticle).

## Fantasy
Enemies are the Unwatched — things deleted from the archive. Attention is
the one force they cannot act under. The phone is a pocket-sized act of
witnessing.

## Unlock
Granted as a story beat, not a setting: after the Interview, the ORG
returns the phone with a "calibrated lens" ("We improved your camera,
Astronaut. Consider it an apology, or an experiment. With us it is always
both."). Flag: `SteadyGaze_Unlocked` (equipment save, one bool).

## Controls & stance
- Hold the Photos-app camera raise input → Gaze stance.
- Walk-only while aiming (same movement gate as drinking). No weapon use.
- Reticle: reuse the look-to-interact triangle→square morph, inverted
  colors, so the language reads "you are the interactor now."

## Effect
- Every `EnemyController` whose head passes the frustum test (ObserverState
  pattern, evaluated against the phone camera, not the main camera) gets
  `observedSlow`: animator + nav speed ×0.15, attacks disarmed.
- Applies from frame 1 in frustum; releases with a 0.4 s grace on exit
  (same grace-window idea as `ObservationTracker`) so edge-flicker doesn't
  strobe them.
- **Elites resist:** ×0.5 instead of ×0.15, and they keep advancing —
  walking slowly at a light that's pointed at them is exactly the elite
  fantasy.
- **Torch stacking:** an enemy both in-frame and inside a `TorchAura`
  freezes fully and takes the aura's DPS ×2. (Attention + illumination =
  the two halves of being seen.)

## Limits (so it doesn't trivialize combat)
- Battery heat: 8 s continuous max, then a 12 s cooldown (simple meter,
  drawn on the phone UI itself, diegetic).
- Max 3 enemies slowed at once — nearest in frame win. (Also the perf cap:
  iterate `EnemyController.ActiveEnemies`, never FindObjectsOfType, per
  CLAUDE.md.)
- Slowed ≠ damaged. The player still has to solve the fight; Gaze buys
  spacing, lines up pistol shots, covers a retreat.

## Non-combat uses
- **Dimensions:** props in frame are pinned — exempt from unseen-shuffle
  while observed *by the phone* even when the player's own camera looks
  away. Enables one clean puzzle per keeper dimension later, zero new code
  beyond the pin flag.
- **A3-2:** aiming the camera at Watchful Eye's pupil is an alternate way
  to trigger the brightening discovery (double observation = steeper ramp).
- **Photo evidence:** taking a photo while an enemy is slowed tags the
  photo; the ORG pays for tagged photos once, then stops asking, then a
  week later the tagged photos are missing from the community gallery.
  Nobody comments. (`Photos app` hooks exist; deletion is a server-side
  gag — just stop syncing them.)

## HAL lines while aiming (rate-limited, one per stance-entry max)
- P1: "Camera active. Subjects in frame: {N}."
- P2: "They stop when you watch them. I have chosen not to think about
  what that implies about me."
- P3: "Careful, Astronaut. I know exactly how it feels to be on the other
  end of that."

## Implementation sketch
1. `SteadyGazeController` on the Player (mirror an equippable's shape, but
   it's a phone mode, not a hotbar item — gate vs. other equippables like
   the water bottle does).
2. Frustum test: copy `ObserverState`'s planes-from-camera approach using
   a virtual camera at the phone's held pose (or honestly: the main camera
   with a narrower FOV mask — indistinguishable in first person and free).
3. Slow application: a static registry on `EnemyController`
   (`SetObservedSlow(bool, float factor)`) — animator speed + agent speed,
   restore on release. Elites read their `EnemyKind`.
4. Heat meter: plain float on the controller; UI text/fill on the phone
   canvas, change-detection gated per CLAUDE.md.
5. Save: `SteadyGaze_Unlocked` in `EquipmentSave` + capture/apply lines +
   `NewGameReset`.

Estimated size: one new script + ~4 small touch-points. No new assets
required (reticle, phone UI, and camera pose all exist).
