🟢 ACTIVE — 2026-09-08 — Swimming v2 design (Sam-approved, build pending playtest)

# Swimming v2 — water movement that feels like water

**Problem (Sam, 2026-09-08):** movement on land and in space feels great and realistic;
the moment you enter water it feels bad. Diagnosis from `PlayerController.cs`:

- Water is a **hard switch** at chest depth (`IsHalfSubmerged`). No wading. Everything
  changes at once.
- **No water resistance, only a speed cap** (`waterMaxSpeed` 3.75). Below the cap you
  glide forever; above it you are stopped dead in one physics step — a dive into water
  is a wall, not a splash.
- **Buoyancy is a constant** (8 vs 9.81 g) — you always sink, and Space is a jetpack
  (+6) that launches you out of the water and bobs at the waterline.
- **Look direction always steers**, so a slight downward glance + W dives you.

**Sam's calls:** idle = slowly sink; dive/rise on the jetpack keys (Ctrl / Space);
physics plus a light touch (stroke rhythm, small camera sway, surface bob).

## Design

All in `PlayerController.cs` (the swim block in `HandleMovement`, the wade multipliers
in the input step, and the swim/ground gates), plus two lines in `CameraTransformFX.cs`.

### 1. Depth is a dial
`WaterDepthAt(worldPos)` → signed metres below the surface (planet: `oceanR − |p − c|`;
Poolrooms: `SurfaceY − p.y`). `depth` = capsule centre (chest); `eyeDepth` = camera.
Capsule is 2 m tall, so `immersed = clamp01((depth + 1) / 2)` goes 0 (feet touch) → 1
(head under). Every water effect scales with `immersed`, so wading, plunging and
swimming blend instead of switching.

### 2. Water resists you
Drag on the body-relative velocity, implicit (unconditionally stable):
`v *= 1 / (1 + dt · immersed · (cLin + cQuad·|v|))`.
Defaults `cLin 0.3`, `cQuad 0.8` (simulated at 50 Hz): a 15 m/s cliff dive plunges ~4.5 m and is at swim
speed in ~0.3 s; releasing the keys glides ~2.4 m to a stop; 90% of swim speed in 0.5 s; idle sink
terminal ≈ 0.5 m/s; holding Space settles the eyes ~0.38 m clear with no overshoot. Top swim speed is a knob (`swimSpeed` 4 = half walk); the thrust
that reaches it is DERIVED from the drag (`cQuad·s² + cLin·s`) so the two never fight.
The old `waterMaxSpeed` clamp is gone.

### 3. Sink, dive, surface on the jetpack keys
- Buoyancy = `g · buoyancyFraction (0.85 — was 0.96; Sam after playtest 1: "the astronaut should sink faster, the suit is heavy"; sink ≈ 1.2 m/s, Space still floats the eyes ~0.3 m clear) · immersed`, i.e. a fraction of the ACTUAL
  gravity (`_lastGravityMag`, so the 20 m/s² Poolrooms still works). Fully under, net
  is 0.15 g down → a steady sink. Rising, `immersed` falls, so buoyancy fades out — nothing
  can launch you into the air.
- **Space** = +`swimVerticalAccel` (× g/9.81), faded to zero as `depth` → `floatDepth −
  0.3`, so holding Space brings you up to a float with your head out (equilibrium ≈
  `floatDepth` 0.35 = surface at the shoulders, eyes ~0.35 m clear) and holds you there.
- **Ctrl** = −`swimVerticalAccel`. Dive.
- `IsSwimming = depth ≥ 0 && !(isGrounded && depth < standDepth 0.4)`. Replaces
  `IsHalfSubmerged()` at every gate (jump, jetpack, grip, walk MovePosition, air control).
  Standing in water up to the shoulders = wading; deeper, or feet off the bottom =
  swimming. Walk → swim hands the walk velocity into `rb.velocity` (same as the
  ground → air handoff) so there is no jolt.

### 4. Surface swim vs underwater swim
Wish direction blends on `eyeDepth` (smoothstep −0.1 → +0.15): head out = WASD along
the surface plane (⟂ gravity-up, from the body yaw); head under = full look-relative
(camera basis). Thrust is capped along the wish direction at `swimSpeed` (sprint ×
`swimSprintMul` 1.4) like the air-control block, so momentum can't stack.

### 5. Wading
Grounded with `depth < standDepth`: walk/run target speed × `1 − wadeSlowdown·w`,
jump × `1 − wadeJumpLoss·w`, `w = clamp01((depth + 1) / 1.4)` (0 at the feet, 1 at the
shoulders). Drag still applies to `rb.velocity` (a jump into the shallows is damped).

### 6. The light touch (all zeroable)
- Stroke rhythm: thrust × `1 + strokeAmplitude·sin(2π·strokeHz·t)` while swimming with
  input; phase only advances while pushing.
- Camera: `PlayerController.UpdateSwimCameraFeel()` (render rate, eased in/out)
  computes `SwimCameraRoll` — a roll sway of `swimCameraSway` degrees at the stroke
  phase while swimming — and `SwimCameraBob` — a `surfaceBobAmplitude` (0.04 m)
  rise/fall along up while floating idle at the surface. `CameraTransformFX` adds them
  to its Z roll and local camera offset, so they never fight the 50 Hz rotation
  smoothing. (No new camera module, no settings toggle: the two knobs zero it.)

### Unchanged
Splash + swim-loop audio, `IsInWater` (FallDamage), cave exclusion, the Poolrooms
drowning timer, the `Water` triggers + `WaterlineAlign`. No animation, no ledge climb,
no oxygen drain in planet oceans (possible follow-up), no visual water changes.

### Knobs (serialized, appended at the END of PlayerController per CLAUDE.md)
`swimSpeed 4`, `swimSprintMul 1.4`, `swimVerticalAccel 4`, `waterDragLinear 0.3`,
`waterDragQuadratic 0.8`, `buoyancyFraction 0.85`, `floatDepth 0.35`, `standDepth 0.4`,
`wadeSlowdown 0.6`, `wadeJumpLoss 0.6`, `strokeAmplitude 0.35`, `strokeHz 0.9`,
`swimCameraSway 1.0`, `surfaceBobAmplitude 0.04`.
The player is spawned from `NetworkPlayer.prefab` (Netcode PlayerPrefab), so new fields
take their C# defaults; tune on the prefab, not the scene. The three old fields
(`waterBuoyancyForce/SwimForce/MaxSpeed`) stay serialized but are no longer read.

### Testing
Compile via `compile-unity.py`; Sam playtests (cliff dive, swim out and back, dive to
the seabed and surface, wade ashore, Poolrooms flood).
