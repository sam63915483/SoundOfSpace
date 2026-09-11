🟢 ACTIVE — built 2026-09-11, never play-tested.

# Playtest: fireflies

Night-side bugs you catch, hold as a torch, and eat for a one-minute body glow.
Spec: `docs/superpowers/specs/2026-09-11-fireflies-design.md`.

**No setup.** Nothing to wire and nothing to save in the scene — the spawner,
the glow and its HUD chip create themselves (and are seeded for builds).

---

## The rules as built (my calls — change any of them)

| | |
|---|---|
| Where | Any planet except the Sun / black hole, wherever the sun is **below the horizon**. Swarms fade out at sunrise. |
| How many | Up to 14 swarms within 140 m, 6–10 bugs each, about one swarm per 70 m of open night ground. Never over water, never within 12 m of you. |
| Stack | **10 per slot.** |
| Eat again while glowing | **Refreshes to a full 60 s.** Never adds. |
| Saved? | Caught fireflies in the hotbar: yes (generic). The glow: **no** (cleared on New Game, like cat perks). |
| Emptied swarm | Catch every bug and that spot stays empty for 8 minutes. |

---

## What to check

### 1. Seeing them
- Find the night side (fly to it, or wait — Humble Abode's day is 15 min).
  Expect drifting clusters of yellow-orange sparks 0.5–2.5 m over the ground,
  each blinking on its own rhythm, with a soft halo so they twinkle from far off.
- Walk toward one: the nearest few should light the **ground and the grass**
  under them as they pass (real lights on the 6 nearest only).
- Walk across the terminator into daylight: they fade out over ~1.5 s, no pop.
- Nothing should spawn in your face, over the ocean, or in the shuttle's landing zone.
- **Look for:** bugs flying into hillsides / underground, popping, or a swarm
  that "vanishes" as you approach.

### 2. Catching
- Within ~3.5 m, look at a bug → it holds at full glow, green rim, and
  `Press F to catch firefly`. Only ONE bug ever has the prompt.
- F → `+1 firefly` floats up, the bug is gone, the hotbar slot shows the
  glowing-bug icon. Catch another: `FIREFLY ×2`.
- Fill the hotbar and try again: the usual inventory-full popup, bug stays.

### 3. Holding
- Select the slot: the bug sits in your right hand, glowing steadily (slow
  breath, never dark), with a **warm light around you** — and the grass should
  light up the same as the ground does, like a torch. Compare against the
  flashlight and a placed torch.
- Swap slots: light goes with it. Put it away: light gone.

### 4. Eating
- Hold left click on it: the eat ring fills (1 s), the bug rises to your mouth
  and chews, then a burp — exactly the fish's motion.
- Then: your suit glows firefly-orange (look down at your arms / legs), gently
  breathing; a light around you; a chip top-left `FIREFLY GLOW 0:59` counting
  down. If a cat perk is running, the firefly chip sits **under** the cat chip.
- Last 3 seconds: the glow dies down rather than snapping off.
- Eat another mid-glow: timer resets to 1:00.

### 5. Doesn't-break checks
- New Game while glowing → no glow in the new game.
- Reload a save mid-glow → the timer just carries on (not saved, by design).
- Suit colour: pick a dark suit, eat a firefly — tint stays, glow adds on top.
- Co-op partner holding one: the other player sees the glowing bug + light in
  their hand. (The BODY glow is local-only for now.)

---

## Knobs (live, in the Inspector on the runtime objects)

- `[FireflySpawner]` — `maxSwarms`, `cellSize` / `swarmChance` (density),
  `spawnRadius`, `bugsMin/Max`, `swarmRadius`, `heightMin/Max`, `glowFloor`
  (how dark a bug goes between flashes), `haloSize`, `maxLitBugs`,
  `litIntensity`, `nightDotOn/Off`, `catchRange`. `debugLogging` prints
  swarms / candidates / why cells were rejected every 4 s — **read the build's
  `Player.log`** if you play a build.
- `HeldItemViewmodel` — `heldLightIntensity` (1.6), `heldLightRange` (7 m),
  `heldGrassStrength` (0.5 = ground parity), `heldGlowFloor`, `heldHaloSize`,
  `heldFireflyRotation`.
- `[FireflyGlow]` — `glowIntensity` (2.2), `breatheMin`, `lightIntensity`
  (1.4), `lightRange` (9 m), `fadeOutSeconds`.
- Shader look: `Resources/FireflyBody.mat` / `FireflyHalo.mat` — `_Intensity`
  (HDR glow), `_TailStart`, `_HaloAlpha`, `_HaloPower`.
- Kill switch: `FeatureVault.Fireflies` (spawner only).

If nothing ever spawns: turn on `debugLogging` and look for `sun=MISSING`
(the sun transform never resolved) or `daylight=N` (you are on the day side).
