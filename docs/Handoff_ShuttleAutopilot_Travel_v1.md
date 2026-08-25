# Handoff: Shuttle Autopilot Travel + Assisted Landing (v1, demo)

**Date:** Aug 25, 2026
**Branch:** feat/helmet-hud
**Scope:** DEMO. Make the parked shuttle on Humble Abode able to travel to any other planet via the computer's NAV app, with fully automated launch + transit and a player-steered assisted landing. No fuel, no economy hooks, no intro changes.

> **Protocol (CLAUDE.md rule 4):** State the build plan first — file list, which existing systems you'll touch, and the phase order — and wait for Sam's GO before writing code. Sam places all GameObjects; you script and wire after placement is confirmed.

---

## 0. Hard rules

- **DO NOT touch the intro.** Pod wake → orientation film → door unlock → lawn/Tev flow stays byte-identical. This system only becomes reachable once the player is standing in the parked shuttle after the intro.
- **DO NOT modify core systems:** n-body gravity (100 Hz), floating origin, SolarSystemSync orbits, PlanetRelativeSync, ParentToBodyPhysicsFrame. Integrate on top. If you think a core change is unavoidable, stop and write it up as `[OPEN]`.
- **Kinematic, not physics.** Transit and hover are scripted motion in the target body's local frame. Do not try to fly the shuttle with thrusters/n-body — the small ships already do that; this is the foolproof one.
- **Host-authoritative.** Shuttle state (phase, target, pose, green-light) is owned by the host and replicated. Guests see the same countdown, the same screen, the same light.
- No fuel logic anywhere yet, and no pre-built seam either (D-5).

---

## 1. Player-facing flow (what Sam wants to see)

1. Player is in the parked shuttle on Humble Abode. Walks to the ShuttleComputer, opens the **NAV** app.
2. NAV shows **all planets** as a list/grid (name + a simple icon or the body's colour). Current planet greyed/marked "YOU ARE HERE". Moons/Sun/black hole excluded for now.
3. Click a planet → it's selected (highlighted). A **TRAVEL** button becomes active.
4. Click TRAVEL → screen shows **"TAKING OFF IN 10"** and counts down 10→1 with a tick sound. Door locks at 10. Any interactable that would let you leave is disabled for the countdown.
5. At 0: shuttle lifts off vertically (smooth, ~8–12 s), then cruises to the target planet (~20–40 s, tunable), then slides into a **hold position ~100 m above the target's surface** (call this **ORBIT/HOVER**). Players ride along inside the whole time.
6. NAV screen (and the ConsoleScreen mirror) shows: **"ASSUME CONTROL — WASD to position, SPACE to land."** A **live downward camera feed** fills the app.
7. **Hover phase:** shuttle stays upright (up = away from planet centre), holds ~100 m altitude above whatever is under it, WASD moves it tangentially across the surface. Speed tunable (~15 m/s default). Q/E optional yaw. No pitch/roll input.
8. Under the shuttle a **landing-validity check** runs continuously. Valid → **green light** on the NAV screen + a physical green lamp on the console. Invalid → red.
   Valid = ground layer only, roughly flat, no water, no buildings, no trees, no NPCs, no players, no props in the footprint.
9. Player presses SPACE while green → shuttle descends and settles (~5–8 s), lands, phase returns to **PARKED**. Door unlocks. NAV goes back to the planet list with the new "YOU ARE HERE".
10. Repeat from any planet, including back to Humble Abode.

That is the whole demo. Fuel, cost, travel-time-in-days, and "you must be seated" are all **not** in this build.

---

## 2. Phase state machine

```
PARKED ──TRAVEL──▶ COUNTDOWN(10s) ──▶ LIFTOFF ──▶ TRANSIT ──▶ HOVER ──SPACE(green)──▶ LANDING ──▶ PARKED
                       │ (abort only if shuttle is EMPTY at 0 — D-1)
```

| Phase | Owner | Shuttle motion | Input | Door |
|---|---|---|---|---|
| PARKED | — | none (parented to body as today) | NAV app | open |
| COUNTDOWN | host | none | none | LOCKED at 0 only (door stays usable during the 10 s so people can get in/out; at 0: door locks, riders = whoever is inside; if none → abort per D-1) |
| LIFTOFF | host | kinematic, +up in body frame, ease-out to ~300 m | none | LOCKED |
| TRANSIT | host | kinematic interpolation from departure body frame → arrival body hover frame (see §4) | none | LOCKED |
| HOVER | host applies; input from the pilot (D-3, host OR guest) | kinematic: altitude hold ~100 m, WASD tangential | WASD, SPACE, Q/E | LOCKED |
| LANDING | host | kinematic descent along −up to ground contact + settle | none | LOCKED |

Emit `OnPhaseChanged(phase)` — HUD, audio, door, NAV, and console mirror all subscribe. Don't let each system poll.

---

## 3. What already exists (verify before building)

Tag each of these `[EXISTS]` / `[MISSING]` in your plan after checking the repo. Don't trust this list blindly — some of it is from memory.

- `[EXISTS — DO NOT REUSE]` **Intro pod descent.** It works by locking the player inside the stasis pod until touchdown. That is NOT what this system does: here the player keeps **walking around the shuttle interior normally** while it flies. Read the intro descent only to learn how it handles floating origin + body parenting; do not extend it. The free-walking rider system is new — see §4b.
- `[EXISTS]` **ShuttleComputerTerminal** (look at ConsoleScreen, press F) + app framework the TRAX app lives in. NAV is a new app tile in the same system. Respect the AppGridCols const / grid-nav packing trap from Aug 14 (a new tile must not break WireAppGridNav).
- `[EXISTS]` **ShuttleComputerWorldScreen** — the live 1:1 console mirror rendered from replicated state (Aug 18). NAV must render through this so co-op players see the same screen. The downward camera feed is the one exception: it's a RenderTexture drawn locally on both machines from the (replicated) shuttle pose, not streamed.
- `[EXISTS]` **Planet registry** — whatever SolarSystemSync / the Lague CelestialBody list uses. NAV's planet list comes from there. Filter to bodies flagged as landable planets (add a `bool landable` on the body if there's no existing flag; Sam sets it in inspector).
- `[EXISTS]` **ParentToBodyPhysicsFrame** — the Aug 16 fix for spawn clipping. The shuttle's parked state must go through the same parenting so it doesn't drift with orbit.
- `[EXISTS]` **Floating origin** — transit will cross rebases. Kinematic motion must be computed in body-relative coordinates each frame, never in cached world positions. (Aug 22 lesson: `rb.position` vs `transform.position` mismatch — pick one frame for the whole feature and write it in the header comment.)
- `[EXISTS]` **Door lock** — host-authoritative pod door from the intro. Reuse its lock/unlock path.
- `[EXISTS]` **Layers** — terrain and water are separate layers (confirmed). Still list which layers buildings, trees, NPCs, players, and props sit on before writing the blocker mask.
- `[EXISTS]` **Save** — pod is the ONLY save point (Aug 18). Parked shuttle pose must serialize as (bodyName, local position, local rotation). Never a world position.

---

## 4. Transit math (kinematic, frame-safe)

Do **not** integrate velocity across frames in world space.

- On TRAVEL confirm, capture: `departBody`, `departLocalPose` (shuttle pose in departBody frame), `targetBody`.
- Each frame during TRANSIT, compute progress `t ∈ [0,1]` from elapsed/duration with an ease-in-out curve.
- Blend the shuttle's **world** pose between:
  - `A(t)` = departBody.TransformPoint(departLocalPose + up * liftoffHeight) — evaluated fresh each frame so it follows the departing planet's orbit and any origin rebase
  - `B(t)` = targetBody.TransformPoint(arrivalHoverLocal) — likewise fresh each frame
  - `pose = lerp(A, B, t)`; orientation: slerp from "up = away from departBody centre" to "up = away from targetBody centre"
- `arrivalHoverLocal` = a point 100 m above the target's surface. Pick it as: the surface point on the target facing the departure body at arrival time (raycast from targetBody centre outward along the departure→target direction), + 100 m. Good enough for a demo; hover lets the player move anyway.
- On TRANSIT end, parent the shuttle to targetBody's physics frame (same helper as PARKED) and switch to HOVER. From here everything is local to targetBody.

## 4b. Free-walking rider system (`[BUILD]` — the hard part)

Requirement: from door-lock at COUNTDOWN 0 through LANDING, players can walk, jump, use the computer, open the locker, play TRAX — everything they can do in a parked shuttle — while the shuttle itself is moving kinematically. This is a **moving-platform** problem, not a "lock the player" problem.

Approach (moving reference frame):
- A `ShuttleInteriorVolume` trigger defines "inside". Anyone inside at COUNTDOWN 0 becomes a **rider**.
- Riders are **parented to the shuttle transform** for the duration, and their controller runs in **shuttle-local space**: input moves them relative to the shuttle, `up` = shuttle up, gravity = a fixed local −up pull (NOT n-body during transit — inside the shuttle you should feel normal floor gravity the whole way, even between planets).
- The shuttle's kinematic pose is applied **once per FixedUpdate, before** rider movement, so riders never lag a frame behind the floor (the classic "sinks through the platform / jitters" failure). Player camera interpolation must use the parented local pose.
- Rider physics interactions (held items, dropped tapes, the physics axe) must also be in shuttle-local space or they will fly out the back the moment the shuttle moves. Simplest v1: physics items dropped inside during flight get parented to the shuttle too; the floaty held-item layer already follows the player.
- On PARKED: unparent, re-run ParentToBodyPhysicsFrame on both shuttle and riders, hand gravity back to n-body. Do this on the same frame the shuttle settles so nobody pops.
- Floating origin: because riders are children of the shuttle and the shuttle's pose is recomputed body-relative each frame (§4), an origin rebase moves the shuttle and all riders together. Verify with a forced rebase mid-transit.
- Alternative if parenting fights the existing controller too hard: **don't move the shuttle at all during TRANSIT** — hold it fixed in world space and move the two planets' *relative* placement instead. This is closer to what floating origin already does, but it touches SolarSystemSync, which is off-limits. Only propose it as `[OPEN]` if the parenting route fails.

Multiplayer riders:
- PlanetRelativeSync syncs planet-local pose. During COUNTDOWN→LANDING there is no planet to be relative to. Riders need a **shuttle-local pose sync** for those phases (same message shape, frame = shuttle instead of body). Switch frames on `OnPhaseChanged`; both sides must switch on the same replicated phase, or puppets will snap.
- Puppets of riders are parented to the shuttle on every machine.
- A player who is *not* a rider (outside at 0 — D-1) keeps normal planet-relative sync and simply watches the shuttle leave.

---

## 5. Hover + landing validity

**Altitude hold:** each FixedUpdate, raycast from shuttle down (−up in body frame) on the ground mask. Target altitude 100 m above hit point. Move toward target altitude with a critically-damped smooth (no overshoot). If no hit (over a hole), hold last altitude.

**Lateral:** WASD → tangential move in the plane perpendicular to up, relative to shuttle yaw. Speed `hoverSpeed` (default 15 m/s), acceleration smoothed so it feels like a heavy vehicle, not a cursor.

**Upright:** every frame, rotate so shuttle up = (shuttle − bodyCentre).normalized. Preserve yaw.

**Validity check (runs every 0.1 s, host, replicated as one bool):**
- Footprint = shuttle's landing-gear radius (Sam to confirm, ~6 m).
- Cast a centre ray + 8 rays in a ring at footprint radius, all along −up, max 200 m.
- **All** rays must hit, and every hit must satisfy:
  - hit collider layer ∈ ground mask (terrain only — NOT water, buildings, trees, props, NPCs, players)
  - `dot(hit.normal, up) ≥ cos(maxSlopeDeg)`, default `maxSlopeDeg = 12`
  - spread of hit distances across the 9 rays ≤ `maxFootprintHeightDelta` (default 1.5 m) — catches ridges that pass the per-ray slope test
- Additionally an OverlapSphere/Box at the landing point on the "blocker" mask (NPCs, players, buildings, trees, props, water) must return nothing.
- Result → `landingValid`. Green lamp + green screen state when true.

**SPACE pressed while valid:** enter LANDING. Descend along −up at a smoothed rate to the hit point + gear offset, hold 0.5 s, set PARKED, parent to body physics frame, unlock door. If validity flips false mid-descent (an NPC wandered under you), keep going — the demo shouldn't abort a landing.

**SPACE pressed while invalid:** red flash + a short "NO CLEAR GROUND" buzz. Nothing else.

---

## 6. NAV app spec (`[BUILD]`)

New app tile **NAV** on the ShuttleComputer, same base class as the other apps, rendered through the world-screen mirror.

Views:
1. **PLANET LIST** — all landable bodies, current one marked YOU ARE HERE and unselectable. Selected planet highlighted. Buttons: TRAVEL (enabled only when a non-current planet is selected).
2. **COUNTDOWN** — big number, "TAKING OFF IN N". No buttons.
3. **TRANSIT** — "EN ROUTE TO <PLANET>" + a simple progress bar. No buttons.
4. **HOVER** — full-screen downward camera feed (RenderTexture from a `LandingCamera` child of the shuttle, pointing −up, FOV ~70). Overlay: crosshair at centre, altitude readout, green/red border matching `landingValid`, text "WASD POSITION · SPACE LAND". No buttons.
5. **LANDING** — feed stays, text "LANDING…".

Input: while the NAV app is open in HOVER, WASD/SPACE/QE go to the shuttle, not the player. Use the existing IsTypingActive / ESC-guard mechanism the terminal joined on Aug 13 so hotkeys and pause don't leak. Closing the app during HOVER just hides the feed; shuttle keeps hovering in place (altitude hold still runs). Reopen to continue.

Physical: Sam places a `LandingLamp` (small emissive mesh) on the console; script sets emission green/red/off by phase.

`[AUTHOR]` Placeholder text only. No lore, no HAL/Frump lines. Sam will write voice later.

---

## 7. Multiplayer (`[INTEGRATE]`)

- Replicate: `phase`, `targetBodyName`, `countdownRemaining`, `transitProgress`, `landingValid`, and shuttle local pose in the current body frame at a sane rate (shuttle is big and slow — 10 Hz with interpolation is plenty).
- Only the **host** runs the state machine and validity checks. Guests apply replicated pose and render the feed locally.
- NAV app screen state already travels via the Aug 18 reconciled screen-state sync — extend that schema with NAV's view + selection. Don't build a second sync.
- Steering in HOVER: first NAV opener owns it, host or guest (D-3). Guest pilot sends input to host; host applies.
- All players inside the shuttle at COUNTDOWN 0 are riders. Players outside are left behind (D-1).

---

## 8. Tests (`[TEST]`)

Headless where possible (Unity's Roslyn suites pattern):
- Validity check unit tests with synthetic hits: flat ground → valid; 20° slope → invalid; one ray on water → invalid; ridge (height delta 3 m, all slopes fine) → invalid; NPC in overlap → invalid.
- Transit blend: A and B evaluated fresh — simulate a body moving between frames and assert the shuttle tracks it (i.e. no cached world positions).
- Save round-trip: park on Cyclops, save, reload → shuttle on Cyclops at the same local pose, door open, phase PARKED.

Editor/play checklist for Sam (write to `docs/PLAYTEST_ShuttleAutopilot_v1.md`):
- Humble Abode → Cyclops → twins → back. Four legs, no drift, no origin-rebase pop.
- Try to land on: water, Tev's house, a tree, a hill, an NPC. All red. Flat field green.
- Co-op: guest inside for a full leg, sees countdown/feed/green; both exit on the new planet in the right place.

---

## 9. Decisions (answered by Sam, Aug 25) — build to these

- **D-1 Leaving crew behind.** At COUNTDOWN 0 the shuttle leaves with whoever is inside. Players outside are left on the planet (co-op split is intended). **Exception:** if NOBODY is inside at 0, the launch aborts — the shuttle never takes off empty. Screen: "NO CREW ABOARD — LAUNCH CANCELLED", back to PLANET LIST with the selection kept. Countdown does NOT pause or abort for the person who pressed TRAVEL walking out; only the empty-shuttle case aborts.
- **D-2 Layers.** Terrain and water are separate layers. Ground mask = terrain layer only. Water goes in the blocker mask.
- **D-3 Who steers.** Whoever opens the NAV app on the shuttle computer first during HOVER owns the landing (host or guest). Ownership = a replicated `pilotClientId`, set on first NAV open in HOVER, cleared on PARKED. If the pilot closes the app, ownership stays with them until they leave the computer, then frees. Guest pilot: input goes to the host as a small WASD/SPACE/QE message each FixedUpdate; host applies it to the kinematic hover. Only the pilot's NAV shows the "WASD POSITION · SPACE LAND" prompt; everyone else's shows "PILOT: <name>" over the same feed.
- **D-4 Other ships.** Only the shuttle flies. Anything parked next to it stays on the planet. Riders = things inside the ShuttleInteriorVolume only.
- **D-5 Fuel.** Not in this build and no seam required. Sam will design fuel later; don't pre-build for it.
- **D-6 Levels.** Level system is vaulted (FeatureVault.LevelSystem). Do not touch Explorer track or any progression on landing.
- **D-7 Gravity inside.** Normal floor gravity the whole flight, including between planets — the shuttle is a "safe cage". No weightlessness.

## 10. Phase order

1. Plan → GO from Sam.
2. **Rider system first** (§4b): shuttle moves on a debug key in a straight line 200 m with the player walking inside. If walking inside a moving shuttle isn't solid, nothing else matters.
3. `ShuttleAutopilot` state machine, PARKED↔LIFTOFF↔TRANSIT↔HOVER↔LANDING kinematic motion, single-player, debug key to trigger a leg without the app.
4. Validity check + green lamp + LandingCamera RenderTexture.
5. NAV app (all 5 views) through the world-screen mirror, input capture, door lock.
6. Multiplayer replication (incl. shuttle-local rider sync).
7. Tests + playtest doc. Sam eyeballs everything in editor.

Ship 2–4 before 5 so the mechanic can be felt before the UI is polished.
