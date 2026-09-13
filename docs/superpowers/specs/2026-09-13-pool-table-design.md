🟢 ACTIVE — 2026-09-13 — Pool table minigame (test build; Sam approved the design in chat)

# Pool table at Shlawg's Bar — design

## What Sam asked for

A good-looking low-poly pool table near House_03 (the bar) on Humble Abode. Balls
start racked, cue ball on the head spot. Look at the table → F prompt + green
outline. Press F: the body stays where it stood, the camera glides out of the
helmet to a third-person view behind the cue ball with a cue stick pointed at it,
tip an inch off the ball. Aim by orbiting around the cue ball. Hold left click:
a small bar fills over 2 s and the cue draws back; release strikes with that
power (a tap = a gentle nudge). F at any time glides the camera back into the
head. Single player only; this is a feasibility test — if breaking and sinking
balls feels good it becomes a real mechanic and, later, co-op.

Decisions made in chat (2026-09-13):

- **Balls are simulated by our own 2D sim in table-local space, not Unity
  rigidbodies.** Humble Abode is swept along its rail at ~85 m/s by
  `MovePosition`; every loose prop that had to look precise (beer cups, the
  parked bobber) was rewritten to *not* be a physics object. A pool ball needs
  millimetre precision. A deterministic sim also makes co-op trivial later: a
  shot is one direction + one power.
- **Aim controls are keys/stick, not the mouse** (Sam: the mouse is "too free of
  an axis"). A/D turn, W/S tilt; left stick on pad. Shift / LT = fine aim.
- **Aim guide:** faint line from the cue ball to the first thing it hits + a
  ghost ring at the contact point.
- **Free play, no rules:** pocketed balls vanish; a sunk cue ball comes back to
  the head spot; R re-racks; no turns, no fouls.
- **Camera orbits and tilts** (tilt limited ~8°–60° above the felt), small zoom
  on the wheel / D-pad up-down.

## Architecture

Four runtime files in `Assets/3 - Scripts/Pool/`, one editor builder, three
one-line edits to existing files. Nothing is saved; nothing is an
auto-singleton (the table is a scene object, so trap #1 does not apply).

```
PoolPhysics2D      pure C#, no UnityEngine — the ball sim (unit-testable headless)
PoolTable          Interactable on the prefab root — owns the sim, ball/pocket
                   visuals, rack/reset, exposes what a shot session needs
PoolShotSession    the F-mode: borrows the real camera, aim input, cue stick,
                   charge/strike, guide line, HUD; one static IsActive flag
PoolShotHUD        power bar under the reticle + one hint line (built in code)
Editor/PoolTableBuilder   Tools ▸ Pool ▸ Build Prefab / Place near Shlawg's Bar
```

Edits: `PlayerController` (look + move gates get `|| PoolShotSession.IsActive`),
`CameraTransformFX.LateUpdate` (bail while `PoolShotSession.IsActive`, next to
the SolarMap bail), `MeshCombineTool.CollectEligible` (skip `PoolTable` subtrees
so a village re-bake never swallows the table).

### PoolPhysics2D (the sim)

- Units: metres in the table's local XZ plane (sim `x` = table local X along the
  long side, sim `y` = table local Z). Scaling the prefab root scales everything
  consistently because every visual is a child positioned in local units.
- Table: playing surface 1.98 × 0.99 m (a 7-ft bar table), ball radius 0.028575 m,
  six pockets: corner capture radius 0.062, side 0.058. Cushion = the four
  rectangle edges, minus jaw gaps around each pocket.
- State per ball: position, velocity, active flag. 16 balls, index 0 = cue.
- Step: fixed internal `dt = 1/240 s`, the caller accumulates frame time (capped
  at 0.1 s). Order is fixed → deterministic for a given shot.
  1. integrate positions;
  2. friction: `v -= dir * rollingDecel * dt` (0.55 m/s²) and `v *= (1 - linearDrag*dt)`
     (0.12/s); stop below 0.012 m/s;
  3. ball–ball: for each pair with overlap, push apart equally, then apply an
     elastic impulse along the normal (restitution 0.96) if approaching;
  4. pockets: any ball whose centre is within a pocket's capture radius is
     pocketed (flag off, event fired with ball index + pocket index);
  5. cushions: outside the rectangle by more than 0 → reflect that axis with
     restitution 0.78 and clamp, *unless* inside a pocket jaw (then let it
     travel toward the pocket).
- `Strike(dir, speed)`: sets the cue ball's velocity. Max speed 9 m/s (a real
  break), min 0.6 m/s.
- `CastCueBall(dir)`: sweep a circle of ball radius along `dir` from the cue
  ball against every active ball and the cushion rectangle. Returns the contact
  centre, the hit ball index (or −1), and the hit ball's post-contact direction
  (normal from contact to its centre). Drives the aim guide.
- `Rack()`: standard triangle at the foot spot (x = +0.495), 8-ball in the middle
  of row 3, apex a solid, corners one solid one stripe; cue at the head spot
  (x = −0.495). `AllStopped` → true when every active ball is at rest.
- Events: `BallPocketed(ball, pocket)`, `BallsCollided(a, b, speed)` (for sound
  later), `CushionHit`.

Tests: `prototypes/pool/test/` — Unity-free, compiled with the same
`dotnet csc` recipe as `verify-fishing.py`: rack has no overlaps; a straight
9 m/s break settles within 60 s with every ball inside the rectangle and no
overlaps; a head-on 1 m/s hit transfers ≥ 90% of the speed; a ball aimed at a
corner pocket is pocketed; `CastCueBall` finds the ball a straight shot hits.

### PoolTable (the scene object)

`Interactable` subclass on the prefab root. Serialized (appended at the end,
never reordered): ball transforms (16), pocket transforms (6), felt height, cue
stick transform, guide `LineRenderer`, ring `LineRenderer`, sim tunables.

- `Update`: steps the sim with `Time.deltaTime` whether or not a session is
  open (walk away mid-shot and the balls keep rolling). Then positions each
  ball: `localPosition = (x, feltY + r, y)`; rolls the mesh by
  `angle = distance / r` around `cross(up, velocity)` (table-local) so the
  numbers turn the way they should.
- Pocketed ball: 0.35 s drop animation (down into the pocket while shrinking a
  touch), then the renderer goes off. Cue ball: after the table settles, it
  reappears at the head spot (nudged along the head string if occupied).
- `CanInteract()` false while a session is active. `Interact()` opens the
  session. Prompt text "Press F to play pool".
- Re-rack (`R` inside a session, or automatically when only the cue ball is
  left): balls pop back to the rack instantly.
- Gaze: the root has a solid `BoxCollider` over the whole table (the player
  can't walk through it, and it is what the crosshair SphereCast hits); the
  Interactable trigger is a `SphereCollider` (isTrigger, r = 3.5) added in
  `Awake` if absent, like `BeerCupPickup`.

### PoolShotSession (the F-mode)

`[DefaultExecutionOrder(210)]` like `SolarMap`; a component that lives on the
table root (one per table), `static bool IsActive`.

States: `Closed → Entering → Aiming ⇄ Charging → Striking → Rolling → Aiming …
→ Exiting → Closed`.

**Camera.** Borrow the real camera exactly the way `SolarMap.Setup/Teardown`
does: cache parent + head-local pose, `SetParent(null)`, register with
`EndlessManager`, `CameraTransformFX.enabled = false` plus the static bail in
its `LateUpdate`, `HudVisibility.SetForceHidden(true)`, show the astronaut body
renderers (you can see yourself standing at the table). Player input:
`PlayerController` look + move gates get the `PoolShotSession.IsActive` term;
physics keeps the player standing on the planet.

Every `LateUpdate` the pose is re-derived from live anchors, never integrated:

- shot pose (table-local): `target = cueBall + (0, r, 0)`;
  `dir = (cos pitch · sin yaw, sin pitch, cos pitch · cos yaw)`;
  `pos = target + dir * distance`; look at `target + up * 0.03`. Converted
  with `table.TransformPoint / TransformDirection`, so the orbit and floating
  origin are invisible.
- head pose: `headAnchor.TransformPoint(headLocalPos)` etc., live.
- Entering/Exiting: 0.9 s, position lerp + rotation slerp with a smoothstep;
  F mid-flight reverses from the current `t` (same trick as the map).
- Initial yaw on entry: from the head toward the cue ball, so you arrive looking
  the way you walked up; the first entry after a rack faces the rack.
- Defaults: distance 1.0 m, pitch 22°, pitch clamp 8°–60°, yaw rate 70°/s with a
  0.25 s ramp from 20°/s so a tap nudges ~1°, fine-aim multiplier 0.25, zoom
  0.6–1.6 m.

**Input** (all ignored while `PauseState.MenuOpen`):

| action | keyboard | pad |
|---|---|---|
| turn / tilt | A D / W S | left stick |
| fine aim | hold Shift | hold LT |
| zoom | wheel | D-pad up/down |
| charge + strike | hold / release LMB | hold / release RT |
| cancel a charge | Esc or right click while holding | B |
| re-rack | R | Y |
| leave | F | X |

The F that opened the session is consumed (frame guard) so it cannot also close
it. The mouse does nothing else while at the table.

**Cue stick.** Positioned in table-local space, not parented to the camera, so
tilting the camera never lifts the cue off the felt: it lies along `-aimDir`
from the cue ball, tip 2.5 cm off the ball at rest, raised 4° at the butt.
Charging pulls the tip back `charge01 * 0.22 m` over the 2 s hold. Release:
the tip lunges to the ball over 0.07 s (ease-in), `Strike()` fires at contact,
the cue holds for 0.1 s then fades out. While Rolling the cue and guide are
hidden; when `AllStopped` the camera re-targets the (possibly respawned) cue
ball keeping its yaw, the cue fades back in.

**Power.** `charge01 = min(1, held / 2 s)`; speed = `lerp(0.6, 9.0, charge01²)`
(squared so the low end is fine-grained, like the rod's charge).

**Aim guide.** `CastCueBall` each frame while Aiming/Charging: a `LineRenderer`
(local space, 4 mm wide, HUD amber at 45% alpha) from the cue ball to the
contact centre, a 24-segment ring of ball radius at the contact centre, and a
short 12 cm stub from the hit ball in its post-contact direction.

**HUD.** `PoolShotHUD` on its own overlay canvas: a 190 × 6 bar 86 px below the
crosshair in the `FishingTensionHUD` bracket style, amber, only visible while
charging (fills over 2 s; fills to red-ish above 85%); one dim hint line at the
bottom: `A D / W S aim · hold LMB shoot · R re-rack · F leave` with pad glyphs
when `TutorialGate.LastSource` is the controller.

**Teardown / safety.** Exiting restores everything cached in Setup (the
`SolarMap.Teardown` list). If the camera, player or table vanishes mid-session
(scene reload, death), `Teardown(abort: true)` runs from `OnDisable`/`OnDestroy`.
Death while at the table: `PlayerController` death path already reloads the
scene, which destroys the session component.

### Visuals — the prefab

Built by `Tools ▸ Pool ▸ Build Pool Table Prefab` (`Editor/PoolTableBuilder.cs`,
kept in the repo this time). Flat-shaded low poly: every face has its own
vertices, no smoothing; Standard shader, smoothness ≤ 0.25. Sizes are in metres.

- **Bed**: green felt quad (1.98 × 0.99) at y = 0.80 (table height).
- **Cushions**: six felt-covered prisms with a bevelled nose and angled jaw
  ends at every pocket — the thing that reads "pool table" instead of "box".
  Cross-section: 0.05 wide, 0.038 tall, nose bevel 0.012.
- **Rails**: dark walnut frame around the cushions, 0.11 wide, 0.06 tall, with
  a small chamfer on the top outside edge; 18 white diamond sights (3 per half
  rail) as thin quads laid on the rail top.
- **Pockets**: six dark cylinders (12 sides) sunk into the rail corners/sides
  with a black disc floor 0.06 below the felt so a ball can visually drop.
- **Apron**: dark box under the rails, inset 0.02, 0.16 tall.
- **Legs**: four tapered square legs (0.12 → 0.09 over 0.64 m) with a 0.02 foot
  block, inset 0.18 from each corner.
- **Balls**: UV spheres (18 × 12 segments, smooth normals — balls should look
  round) with a generated 256 × 128 texture each: solids = colour with a white
  number disc on two sides; stripes = white with a coloured equatorial band and
  the disc on the band; cue = plain white; 8 = black. Digits drawn from a 3 × 5
  bitmap font into the texture. Colours: yellow, blue, red, purple, orange,
  green, maroon, black, then the same for 9–15 as stripes.
- **Cue stick**: 1.45 m lathe (10 sides), 12 mm tip → 30 mm butt; maple shaft,
  dark wrap on the back third, black ferrule ring, blue tip.
- Materials in `Assets/2 - Materials/Pool/`, meshes and textures in
  `Assets/1 - samsPrefabs/Pool/`, prefab `Assets/1 - samsPrefabs/Pool/PoolTable.prefab`.
- Colliders: root `BoxCollider` covering rails + bed; the trigger sphere is
  added at runtime. Balls have no colliders (GazeHighlight would filter them
  anyway).

**Placement.** `Tools ▸ Pool ▸ Place Pool Table near Shlawg's Bar` instantiates
the prefab under `--- Celestial ---/Body Simulation/Humble Abode/TOWN-VILLAGE`
about 7 m from House_03 on the door side, stands it on the local up (the
`VendorPlacement.Snap` recipe: raycast toward the core, fall back to the sphere
radius), long axis facing the house. Sam nudges it and saves. Village stays
baked (the table is a new sibling; the exclusion line keeps future re-bakes off
it). `Tools ▸ Village ▸ Refresh Building Exclusion Zones` afterwards is optional.

## Out of scope (deliberately)

Rules / turns / fouls, english (spin), jump/massé, ball-in-hand placement,
sounds, co-op sync, saving table state, an NPC opponent. The sim is built so the
first four are additive later; co-op is "send (yaw, power) to the other player".

## Playtest checklist for Sam

1. Walk to the table: outline + "Press F to play pool" only when looking at it.
2. F: camera glides to behind the cue ball in ~1 s; body stays put; HUD hidden.
3. A/D orbit, W/S tilt, Shift slows; the guide line moves with the aim.
4. Tap LMB: soft nudge. Hold 2 s: cue draws right back, full-power break.
5. Balls spread, bounce off cushions, no ball leaves the table, no ball sits
   inside another, everything comes to rest within ~20 s.
6. Sink a ball: it drops into the pocket and is gone. Sink the cue ball: it
   comes back at the head spot once everything stops.
7. R re-racks. F glides back into the helmet; walking + looking work again;
   HUD back.
8. Walk away mid-roll: the balls keep going; come back, F works again.
9. Turn the village rebake on/off — the table must survive `Re-bake Village`.
