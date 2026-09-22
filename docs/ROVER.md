🟢 ACTIVE — 2026-09-22 — the rover / ATV (built, playtest pending)

# Rover

A four-wheel ATV with two seats and a truck bed. Walk up to the driver's seat,
look at it, press **F**, drive. Built entirely from primitives and code so it
can be regenerated at any time.

## Where it is

- Prefab: `Assets/1 - samsPrefabs/Rover.prefab` (materials in `Assets/2 - Materials/Rover/`).
- Scene instances in `1.6.7.7.7.unity`: `Rover_Start` (7 m in front of the Player
  object's start on Icey Twin) and `Rover_Village` (in the village square on Humble
  Abode, 7 m from the Well toward House_05).
- Rebuild / re-place both: **Tools ▸ Solar System ▸ Rover ▸ Build Rover Prefab + Place In Scene**
  (`Editor/RoverBuilder.cs`). It replaces the two instances by name.
- Dev cheat: **Home** teleports a rover to 6 m in front of you (needs `Universe.cheatsEnabled`).

## Controls (driving)

| Key | Does |
|---|---|
| W / S | throttle / reverse (S while rolling forward = brake) |
| A / D | steer (less lock the faster you go) |
| Space (on the wheels) | tap = hop; hold = the suspension squats, release = bigger jump |
| W/A/S/D (off the wheels) | side thrusters: thrust in the rover's own frame — airborne or afloat |
| Space / Ctrl (off the wheels) | thrust up / down |
| Mouse | look around (clamped to ±150° so you can't look through your own head) |
| V | chase camera |
| F | get out (beside the driver's door). No prompt on purpose: F in, F out. |

## How it works (`Vehicles/RoverController.cs`)

- **One Rigidbody, no WheelColliders** (they assume world-down; planets don't).
  Each wheel is a SphereCast from a hardpoint along the rover's own -up; the
  measured length drives a spring + damper (`springFrequencyHz`, `dampingRatio` —
  0.32 is deliberately underdamped = bouncy), tyre grip acts at the contact
  point with a friction circle (`tyreFriction`), and a bump stop stops it
  bottoming out.
- **Everything is relative to the planet.** Planets ride orbital rails at
  ~85 m/s, so every velocity the tyres and dampers see is `rb.velocity -
  anchor.velocity`. Gravity is the anchor-frame rule from `PlayerController`
  (anchor body's gravity + `frameAcceleration`, never the raw Sun pull).
- **Floats**: each wheel is a buoyancy sphere against the anchor's
  `CelestialBodyGenerator.GetOceanRadius()` (cave interiors excluded). 1.7×
  its share of the weight when fully under, so it sits with the wheels ~60 %
  submerged; W/S paddle, A/D turn, wheels spin like paddle wheels.
- **Airborne / flipped**: torque toward gravity-up so it lands on its wheels;
  if it sits on its side or roof for 1.5 s it rights itself.
- **Thrusters** (2026-09-22, round 2): two side pods with gimballing nozzles.
  Off the wheels (fewer than two touching, or afloat) W/S/A/D thrust in the
  rover's frame, Space up, Ctrl down (`thrustAccel`, `thrustUpAccel`,
  `thrustDownAccel`). The flames are the jetpack's own recipe
  (`JetpackThrusters.BuildFlame/BuildSmoke`, now public static) plus one glow
  light; the nozzle turns to fire opposite the thrust.
- **Heavy and planted**: 1500 kg, `groundedDownforce` (extra 45 % of gravity
  while any wheel touches), rebound damping 1.5× / compression 0.8×, longer
  travel (`maxLength` 1.05) so wheels keep the ground over crests. Fenders sit
  above the wheel's top at full compression, so a loaded suspension never
  clips them.
- **Spawn settle**: the editor planet is a placeholder sphere, so the rover
  starts kinematic, rides the planet at its authored planet-local spot, and
  drops onto the first real terrain a raycast finds (max 8 s, then it just
  goes live where it is). `[Rover] settled on …` in the log confirms it.
- **Seating** mirrors `Ship.PilotShip` / `DroneController.Enter`: the player
  GameObject is switched off, the real camera goes to `DriverHead` (never a
  second camera — that loses the atmosphere/ocean post stack), every
  `Interactable`'s in-zone flag is cleared (the documented disable-trap), and
  exit puts the player at `ExitPoint` with the rover's velocity, then runs the
  same `ExitFromSpaceship` / `SnapOrientationOnExitPilot` / `SnapToCurrentPlayer`
  hand-back the ship uses.
- The engine hum is a procedural clip (`AudioClip.Create`), pitch follows
  speed + throttle. No asset needed.
- Registered with `EndlessManager` (floating origin). Not saved — a reload puts
  it back at its scene spot.
- **Survives save loads / warps**: the save loader teleports every planet to
  its saved orbit position one frame after the scene starts. The rover keeps
  its last planet-local pose each step; an impossible one-step jump in that
  pose means the planet moved, and it re-seats itself at the same local spot
  and re-settles (`[Rover] <planet> moved under me` in the log).

## Tuning knobs worth knowing

- Bounce: `springFrequencyHz` (softer = lower), `dampingRatio` (lower = bouncier), `reboundDampingScale` (higher = never pogoes), `groundedDownforce` (heavier).
- Ride height: `restLength` + `wheelRadius`; `maxLength` is how far a wheel
  reaches for ground over a crest.
- Speed / punch: `driveAccel`, `maxSpeed`; corners: `maxSteerDeg`,
  `steerAtSpeedFraction`, `lateralStiffness`, `tyreFriction` (raise the last two
  for "on rails", lower for drifty).
- Jump: `chargeSeconds`, `squatDepth`, `jumpMinSpeed`, `jumpMaxSpeed`.
- Camera: `viewStabilise` (0 = rides every bump, 1 = gimbal).

## Not done / open

- Passenger seat is decoration (one occupant; no co-op sync).
- Not in the save file.
- No dust / tyre marks / damage.
