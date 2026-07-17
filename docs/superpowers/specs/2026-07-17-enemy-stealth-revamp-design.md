# Enemy Stealth Revamp — Design (2026-07-17)

Replaces the always-chasing, wave-spawned enemies with a **pre-spawned, docile field**
you sneak through on the dark side. Inspired by The Forest / Sons of the Forest: enemies
that feel aware and outplayable. Playtest is the real judge of feel; numbers below are
starting values to tune.

## The loop
Walk onto the dark side → 10–20 enemies are already there, wandering, unaware → pick
between their view cones → linger in a cone (walking) 2s or sprint through one → they
investigate, then scream + chase → break line of sight (hill / house / moon base) → they
lose you, sniff around your last spot 10s, then wander off. Lead a chasing pack into the
sunlit terminator → they burn (~3.5s).

## State machine (`EnemyController`)
`Docile → Investigating → Chasing → Searching → Docile`, plus `Dying` (existing) + sun `Burning`.

- **Docile** (cheap tick): slow wander within a leash of spawn point, idle, look-around. Silent.
- **Investigating**: on seeing you *walking*, stop, face you, creep 1–2 steps, head/body-track while the 2s meter fills. Silent.
- **Chasing**: existing chase locomotion. **Scream only here, only while it can see you.**
- **Searching**: lost sight → go to last-seen pos, look around **10s**, play **sniff** clip
  (regular vs. brute clip differ), no scream. Re-see → Chasing. Timeout → Docile (re-leash here).
- **Burning**: on the lit side → ~3.5s → death (environmental, no killstreak credit).

Getting shot (`TakeDamage`) force-alerts → Chasing regardless of vision.

## Detection (`EnemyVision`, new component)
- View cone: **15 m** range, **±30° (60° total)**, around the enemy's forward.
- **LOS raycast** (throttled ~0.15s) from eye toward player; terrain / buildings / moon-base
  walls block it (layers 9 Ship / 11 Sun / 12 FishPreview excluded). Self-hit avoided by
  offsetting the ray origin forward.
- Walking in cone+LOS → 2s suspicion meter (`Suspicion01` 0→1). Sprinting in cone+LOS → meter = 1 instantly.
- Meter decays (~1.5/s) when not seeing. `IsAlerted = Suspicion01 >= 1`.
- Exposes: `CanSeePlayerNow`, `Suspicion01`, `IsAlerted`, `LastSeenPlayerPos`, `ForceAlert()`.
- **No 25 m chain-aggro** (cut per Sam) — each enemy detects on its own.
- **Debug cones**: static red translucent sector mesh (Sprites/Default, unlit so it shows in
  the dark), no collider (never blocks LOS), toggled by `EnemyVision.ShowDebugCones` (true for now).

## Player feedback (`EnemyDetectionHUD`, new auto-singleton)
Polls `EnemyVision.AllInstances` for the most-alarmed one that can see you; shows a red
directional marker toward it + a vignette whose intensity = that suspicion. Empties when you
break LOS. Auto-created (RuntimeInitializeOnLoadMethod + MainMenu skip) and seeded in
`MainMenuController.EnsureGameplaySingletons` (trap #1). Standalone canvas for now; can move
into the helmet HUD later.

## Spawn / population director (`EnemySpawner` rework)
- Maintain **10–20** enemies within **30–120 m**, dark side only, **3:1 regular:elite**.
- **No pop-in**: spawn points must be terrain-occluded from the camera (LOS raycast camera→point
  blocked) — the curved planet + hills make this easy. Fill toward the target (not one-per-10s).
- **Cull** when the player crosses to the lit side or an enemy passes ~150 m (`despawnDistance`).
- Spawn enemies **Docile**. Keep spawning only while the player is actually on a dark side.

## Kept / cut
- **Keep**: ground locomotion, separation, tree-avoidance, **tree-top spit** (still cheeses),
  elite charge, ragdoll death, planet-parenting / floating origin, save/load.
- **Cut**: timer/wave cadence, always-chase, docile screaming (`spawnLoop` now gated to Chasing).
- **YAGNI (later)**: 25 m chain-aggro, flashlight-affects-detection, saving mid-chase AI state
  (reloads reset to Docile).

## New/changed files
- New: `EnemyVision.cs`, `EnemyDetectionHUD.cs`.
- Changed: `EnemyController.cs` (state machine + wander + search + sunburn + audio gating),
  `EnemySpawner.cs` (population director), `PlayerController.cs` (`IsSprinting`),
  `MainMenuController.cs` (seed the HUD).
- Wiring: `EnemyVision` added to `Enemy_Toy10` + `Enemy_Toy3` prefabs; two sniff SFX generated
  and assigned; spawner config.
