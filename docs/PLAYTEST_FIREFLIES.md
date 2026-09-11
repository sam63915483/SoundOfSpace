🟢 ACTIVE — built 2026-09-11; round 2 (wings, spacing, popup, lights) and round 3 (the body glow actually glows now — it is a shell, see §4) after Sam's playtests the same day.

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
| How many | Up to 36 swarms within 140 m, 3–5 bugs each on evenly spaced home points over a 16 m patch — roughly a bug every 8–10 m of open night ground (round 2: spread out, not clumps). Never over water, never within 12 m of you. |
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
- Wings flap fast (18 beats a second, each bug on its own beat).
- The **12 nearest** bugs within 60 m carry a real light and light the ground
  and grass under them; the rest glow but light nothing. See "Why not all of
  them" below before cranking `maxLitBugs`.
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
- Then: your whole suit glows firefly-orange — look down at your arms / legs,
  or at a mirror: an orange surface glow with brighter rims along the edges,
  gently breathing, the visor still dark; a light around you; a chip top-left
  `FIREFLY GLOW 0:59` counting down. If a cat perk is running, the firefly chip
  sits **under** the cat chip. (Round 3: the first version wrote emission into
  the suit material, which turned out to be embedded in the FBX and cannot glow —
  it is a separate glow shell over the body now.)
- Last 3 seconds: the glow dies down rather than snapping off.
- Eat another mid-glow: timer resets to 1:00.

### 5. Doesn't-break checks
- New Game while glowing → no glow in the new game.
- Reload a save mid-glow → the timer just carries on (not saved, by design).
- Suit colour: pick a dark suit, eat a firefly — tint stays, glow adds on top.
- Co-op partner holding one: the other player sees the glowing bug + light in
  their hand. (The BODY glow is local-only for now.)

---

## Why not every firefly is a real light

The cost has nothing to do with how detailed the bug is. A planet is ONE mesh,
and in this renderer every real light makes everything it can reach get drawn
one more time — so each firefly light is one extra draw of the whole planet.
Twelve lights ≈ twelve extra planet draws, on top of the village lanterns. The
knob is `maxLitBugs` on `[FireflySpawner]`: try 24 or 36 and watch the FPS
counter — if your machine eats it, keep it. If FPS drops, the next step is a
faked ground-glow under every bug (cheap, no real light), which I can build.

## Knobs (live, in the Inspector on the runtime objects)

- `[FireflySpawner]` — `maxSwarms`, `cellSize` / `swarmChance` (density),
  `spawnRadius`, `bugsMin/Max`, `swarmRadius` + `wanderRadius` (spacing),
  `heightMin/Max`, `glowFloor` (how dark a bug goes between flashes),
  `haloSize`, `maxLitBugs` / `litRange` / `litIntensity` / `litLightRange`,
  `nightDotOn/Off`, `catchRange`. `debugLogging` prints
  swarms / candidates / why cells were rejected every 4 s — **read the build's
  `Player.log`** if you play a build.
- `HeldItemViewmodel` — `heldLightIntensity` (1.6), `heldLightRange` (7 m),
  `heldGrassStrength` (0.5 = ground parity), `heldGlowFloor`, `heldHaloSize`,
  `heldFireflyRotation`.
- `[FireflyGlow]` — `breatheMin`, `darkPartsStrength` (0.7), `lightIntensity`
  (1.4), `lightRange` (9 m), `fadeOutSeconds`. How hard the body glows lives on
  `Resources/FireflyGlowShell.mat`: `_Intensity` (1.6, HDR), `_Base` (0.35 —
  how much of the surface glows vs. rim only), `_RimPower` (2.2).
- Shader look: `Resources/FireflyBody.mat` / `FireflyHalo.mat` — `_Intensity`
  (HDR glow), `_TailStart`, `_HaloAlpha`, `_HaloPower`, `_FlapHz` (18),
  `_FlapAngle` (38°).
- Kill switch: `FeatureVault.Fireflies` (spawner only).

If nothing ever spawns: turn on `debugLogging` and look for `sun=MISSING`
(the sun transform never resolved) or `daylight=N` (you are on the day side).
