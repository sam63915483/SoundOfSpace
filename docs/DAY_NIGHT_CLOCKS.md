# Day/Night Clocks — Diagnostic Results & Loop-Design Notes (2026-08-31)

Planets don't spin, so **a planet's local solar day = its orbital period around
the sun** (the sky's sun direction only changes as the body revolves — see
GalaxyTime.cs header). This doc records the measured "true planet clocks", the
long-run stability check Sam asked for, and the design recommendation for the
Majora's-Mask-style loop countdown.

Measured by `scratchpad OrbitDiagnostic/OrbitVariants` editor scripts: an exact
offline mirror of `NBodySimulation.FixedUpdate` (semi-implicit Euler, dt = 0.01,
G = 0.0001, same static-attractor / orbitGroup / follower-placement rules),
stepped 4.5 game-hours from the authored scene state. A live play-mode
counterpart (`OrbitClockProbe`, in-scene under `--- Managers ---`) logs the same
numbers during play to `Logs/orbit_probe_*.csv` for cross-verification.

## True planet clocks (first laps, before decay)

| Body | Local day | Real time (1:1) | vs Humble Abode day |
|---|---|---|---|
| Fiery Twin / Icey Twin | ~264 s | ~4.4 min | ~0.35× |
| Humble Abode | ~750 s | ~12.5 min | 1× |
| Constant Companion (moon of HA) | ~746 s | ~12.4 min | ≈ 1× (follows HA) |
| Cyclops | ~2 168 s | ~36 min | ~2.9× |
| Tumbling Bean / Watchful Eye (moons of Cyclops) | ~2 156–2 180 s | ~36 min | ≈ 2.9× |

Moons share their planet's solar day (their small circle around the planet only
wobbles the sun direction). GalaxyTime's standard day (24 real min) ≈ 2 Humble
Abode days — the clock is a STANDARD, deliberately not local solar time.

## Stability: the system DOES go off the rails

4.5-h sims, double precision (so this is real dynamics, not float error):

| Config | Result |
|---|---|
| **As shipped** | Twins' orbit anomalous at **t≈57 min**; by ~3.5 h Fiery Twin has spiralled into the sun (day 264 s → 20 s), Humble Abode flung to 2–8× its radius, everything chaotic. Sun itself drifts ~600 k units. |
| Sun pinned | Twins + Cyclops rock-solid 4.5 h (±1–3 %). Humble Abode + Constant Companion still blow up at ~2 h. |
| Follower gravity muted | Sun drift 677 k → 56 k (Icey-on-rails momentum pumping confirmed as a driver). HA + CC still unstable ~3 h. |

Three stacked causes:
1. **Icey Twin is placed on rails but still pulls everything** — it pumps
   momentum into the sun with no reaction force (sun wanders, system heats up).
2. **The sun is free** and drifts/recoils; everything tuned around it degrades.
3. **Constant Companion is a FREE-simulated moon at ~0.4 of Humble Abode's Hill
   radius** — the marginal zone the satellite-lock system was invented for
   (see CelestialBody.satelliteOrbitRadius comments). It chaotically escapes
   after ~2 h and destabilizes HA.

Also note: saves capture live body state, so decay **accumulates across
sessions** — the shipped game's "day lengths" slowly change the longer a save
is played. NPC schedules cannot hang off the current free sim.

## Recommendation for the loop game (not yet implemented)

The loop design (one countdown, hand-authored NPC schedules, per-planet endings,
loops that repeat identically so knowledge is the reward) needs **deterministic
clockwork**, which free n-body fundamentally isn't. Proposal:

1. **Put every body on analytic rails** (circular orbits, sun pinned), using the
   already-play-proven follower mechanism (`ApplyPlacedState` MovePosition
   sweep — same as Icey Twin / satellite moons today). Bodies keep full mass and
   surface gravity; the player/ship feel nothing different. Day lengths become
   exact constants — and *design knobs*.
2. **Retune day lengths to clean ratios**, e.g. Humble Abode 12 min, Cyclops
   36 min (= 3 HA days), twins 4 min (= ⅓ HA day). Players can then do
   Majora-style mental time math.
3. **One absolute countdown, counted in Humble Abode days** (the home world
   defines "a day", like Earth does). 10–17 HA days ≈ 2.1–3.5 real hours —
   exactly the 2–4 h loop target. The HUD number never changes meaning.
4. **Do NOT change game speed per planet.** It breaks growth timers, buyer
   windows (unscaled time), concert/audio sync, multiplayer (two players on two
   planets), and the promise-vs-grade bug class. The *feeling* of foreign time
   comes free from different local day lengths.
5. **NPC sleep = local darkness via the existing dot-product test** (what
   EnemySpawner / sunburn / ConcertStageHub already use) — never the clock.
   Long Cyclops nights (≈1.5 HA days of dark) become schedule puzzles, twins
   strobe day/night every ~4 min: per-planet character for free.

## Verifying in play mode

`OrbitClockProbe` is in the gameplay scene: press Play and leave it running
(AFK fine). It logs `[OrbitProbe] <body>: day #N = Xs, drift ±Y%` per completed
lap, warns `OFF THE RAILS` when a body leaves its starting orbit by >25 %, and
writes `Logs/orbit_probe_<timestamp>.csv`. It auto-rebaselines after save-load
teleports. Expected if the sim mirror is right: twins ≈ 264 s days shortening
over the first hour, HA ≈ 750 s, Cyclops ≈ 2 168 s, first warnings inside
1–2 h. Delete the scene object when done measuring (or leave it; it's passive).
