# PHYSICS AXE SPIKE — reactive carry + mouse-driven swings (handoff)

**Status: PROTOTYPE SPIKE.** The goal is to find out if this is fun, not to ship it.
Time-box it. Nothing here is canon until Sam plays it and says so.

**Protocol (GDD_StoryBible_v2 §0):** before building anything, state your build plan
in plain terms — what you will create, what you will reuse, what you will NOT
touch — and wait for Sam's go-ahead.

---

## 1. THE IDEA (Sam's, tightened)

Replace the canned chop tween with a two-layer physical-feel system:

- **Carry layer (always on while equipped):** the axe floats ahead and to the
  right, and *reacts* — lags behind camera turns, sways with locomotion, dips on
  landing, drifts back on sprint. Floaty, alive, space-y.
- **Swing layer (while LMB held):** the axe snaps into a chop stance — blade
  rotates sideways so the edge leads the motion, oriented by whichever way the
  swing is going so **the blade always points into the swing** — and the
  player's **mouse motion drives the swing itself**. A fast flick is a hard
  hit; a slow drag is nothing. One system for both **tree chopping** and
  **melee combat**.

The current implementation (static pose + 0.1s tween + camera-cone hit search)
is what we're replacing. The visual axe and the hit test are currently
decoupled; in the new system the blade *is* the hitbox.

---

## 2. ARCHITECTURE DECISION — procedural, not Rigidbody [LOCKED for the spike]

Do **not** build this as a jointed Rigidbody viewmodel. This project's world
sim is custom (n-body gravity, floating-origin shifts, planets that rotate and
translate); a physical viewmodel will fight all three — jitter on origin
shifts, tunneling at flick speeds, gravity confusion inside moving ships.

Build it **kinematic/procedural in camera space**:

- Position: spring-damper toward a computed target pose (stiffness + damping exposed).
- Rotation: torsional spring toward a target orientation.
- Swing: mouse deltas integrated into an arc with momentum (virtual mass +
  damping); release mid-swing = follow-through that decays.
- Contact: **between-frame sweeps** (Physics.BoxCast/CapsuleCast) along the
  blade edge path — no collider on the axe itself.

Framerate-independence is mandatory (integrate with dt correctly; a 30fps and a
144fps machine must produce the same swing for the same mouse motion).
Rigidbody/joint experiments are out of scope; revisit only if procedural
proves un-fun.

---

## 3. THE CENTRAL TUNABLE — camera vs. axe input split [DECIDE BY FEEL]

While LMB is held, mouse deltas drive the axe. **What does the camera do?**

- `swingLookScale` ∈ [0..1] — 0 = camera fully locked during a swing
  (committed, readable; risk: disorienting), 1 = camera turns fully with the
  swing (risk: view-drag nausea).
- **Start at 0.25.** Expose it on the component AND as an on-screen debug
  readout in the spike build. This one slider will decide the verdict of the
  prototype — do not bury it, do not hardcode it.
- Optional flag: soft spring-return of the camera to pre-swing orientation
  after the swing ends (only meaningful at low values).

---

## 4. WHAT EXISTS — reuse, don't rebuild

- [EXISTS] `Assets/1 - samsPrefabs/Axe.prefab`, hotbar icon
  (`axe_icon.png`), equip/unequip audio clips.
- [EXISTS] `Assets/3 - Scripts/Pickups/AxeController.cs` — current axe.
  KEEP: prefab spawn + grip-pivot rig (`axeHoldPosition`,
  `holdPositionOffset`, `gripOffset`), hotbar integration, the
  equip-exclusivity handshakes (FishingRod / Guitar / WaterBottle / Pistol /
  PlayerPickup / Ship), swing + hit AudioClip hooks, and the damage numbers as
  defaults (`damagePerSwing = 1` tree chop, `enemyDamagePerSwing = 34`,
  3-hit kill on 100 HP).
  REPLACE: the canned tween (`swingForwardDuration` / `swingForwardAngle` /
  `swingAxis`) and the camera-cone hit search (`swingRange` /
  `swingConeDot`).
- [EXISTS] Tree damage pipeline — streamed trees + planted trees take integer
  chops and drop wood + saplings, feed the planet-O2 recount, and persist via
  cell IDs. Find the exact receiver `AxeController` calls today and route the
  new sweep hits into the **same entry point**. Drops, saves, and O2 must not
  change.
- [EXISTS] Enemy health + ragdolls (spider mech, alien bird). Route sweep hits
  into existing damage handling.
- [EXISTS] `PlayerController` mouse look. The new system needs the RAW
  per-frame mouse delta *before* camera smoothing, plus the ability to scale
  camera rotation by `swingLookScale` while swinging. Add one small public
  hook; do not fork input handling.

---

## 5. BUILD — three milestones. STOP after each for Sam's playtest.

### M1 — Reactive carry (no gameplay change)
- [BUILD] `AxeMotor.cs` — computes a target pose from camera + rest offsets,
  then springs toward it. Inputs: camera angular velocity (lag), player
  velocity (sway/tilt), vertical velocity (rise on jump, dip on landing),
  sprint state (drift back + tilt). Clamp maximum offsets so the axe can
  never block the view or leave the right side of the screen.
- [TEST] Walk / sprint / jump / hard 180s on a planet surface AND inside the
  shuttle. Verify zero jitter across a floating-origin shift and on a
  rotating planet.
- Acceptance: the OLD click-tween still fires on top of the carry layer in
  M1. The carry layer alone already replaces "the animations are really bad"
  — it is worth keeping even if M2 is scrapped.

### M2 — Mouse-driven swing + trees
- [BUILD] `AxeSwing.cs` — LMB down: enter chop stance (raise + cock away from
  the current/last mouse-motion direction; if the mouse is stationary,
  default diagonal ready pose). While held: integrate mouse deltas into the
  swing arc with momentum. Release mid-swing: follow-through decays.
  **Blade auto-orient:** rate-limited slerp of the blade normal toward the
  instantaneous swing velocity so the edge leads; never allow the edge to
  point at the camera.
- [BUILD] `BladeSweep.cs` — 2–3 sample points along the edge, between-frame
  casts. A hit registers **only above `minEdgeSpeed`** — resting the blade on
  a tree does nothing. Trees keep INTEGER chops: swing speed gates whether a
  chop *counts*, it does not scale fractional damage (preserves readable
  N-hits-to-fell). Enemies may scale damage by speed tier if trivial, else
  flat `enemyDamagePerSwing` per valid hit.
- [INTEGRATE] Tree hits → existing chop receiver. Wood + sapling drops,
  planet-O2 recount, and persistence must behave identically to today.
- [BUILD] Feel hooks only, no polish pass: 30–60 ms hit-stop on connect,
  existing `hitClip` pitch scaled by edge speed, small camera micro-shake
  scaled by edge speed.
- [TEST] Fell several tutorial-grove trees. Verify: drops identical, O2
  recount fires, sub-threshold drags do zero damage, whiff vs. hit reads
  instantly, save/load mid-equip leaves no stranded state.

### M3 — Combat
- [INTEGRATE] Same sweep vs. enemy colliders → existing damage. Replace the
  forward-axis knockback slide with an impulse along blade velocity at the
  contact point; on kill, apply that impulse to the ragdoll.
- [TEST] Kill a spider mech and an alien bird with melee only. 3-hit budget
  holds at medium swings; no self-hits; no hitting through walls (line-of-
  sight check from shoulder to contact point before applying damage).

**Fallback flag:** keep the classic click-chop behind a serialized
`useClassicSwing` bool (default off in dev builds). Insurance + future
accessibility option.

---

## 6. SCOPE GUARDRAILS — [DO NOT]

No stamina system. No blocking/parry. No enemy AI changes. No new enemy
animations. No gamepad mapping (right-stick swing is a later question — log
it, don't build it). No changes to guns, fishing, or the hotbar beyond the
equip hook. No polish beyond the three feel hooks in M2. No Rigidbody/joint
experiments.

---

## 7. OPEN QUESTIONS FOR SAM (never self-answer — bible §0)

1. `swingLookScale` starting feel — committed/locked (0–0.3) or loose (0.5+)?
2. Trees: confirm integer chops with a speed gate, or do you want speed to
   scale tree damage?
3. Should a too-slow drag do anything at all (scrape sound only)?
4. Should other tools (fishing rod, guitar) inherit the M1 carry layer later?
   (Write `AxeMotor` axe-only but not axe-hardcoded.)
5. The HAL line that teaches the swing diegetically — Sam authors this later;
   not part of the spike.

---

## 8. DEFINITION OF DONE

One editor session where Sam can: feel the carry sway (M1), fell a tree with a
flick (M2), kill one enemy with melee (M3), tune `swingLookScale` and the
spring constants live in the inspector, and answer the only question that
matters: **is this fun?** If no — keep M1, park M2/M3 behind the flag, and the
spike still paid for itself.
