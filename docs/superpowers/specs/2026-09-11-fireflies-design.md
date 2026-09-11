🟢 ACTIVE — designed and built 2026-09-11 (Sam's idea); round 2 the same day after his first playtest (wings flap, spread-out coverage instead of clumps, popup fix, more lights). Checklist: `docs/PLAYTEST_FIREFLIES.md`.

# Fireflies — catch, hold, eat, glow

Sam, 2026-09-11: *"on the night time side of the planet when its dark and not
receiving the sun, make fireflies that fly around … a decent bobber sized bug …
emitting a yellow orange glow … a pretty decent amount of them … run up to them
and look at them and press F to pickup, then it goes into an inventory slot and
you can hold it like a fish so it appears in your right hand area, and it will
emit a glow and be like a torch, so it will also have to light up grass
properly. … hold left click while holding it, and just like a fish it will get
raised up to your mouth and do the eating motion and the cursor will also have
the little timer and once ate, make the astronauts entire body glow for 1
minute as a status effect like the cat perks."*

## The four beats

| beat | what happens | where |
|---|---|---|
| **See** | Night side of any planet: swarms of 6–10 fireflies drift 0.4–2.6 m over the ground, each blinking on its own rhythm. Fade in/out as the terminator moves. Nearest few carry a real point light (ground + grass). | `World/FireflySpawner.cs`, `World/FireflySwarm.cs`, `World/FireflyBug.cs`, `World/FireflyVisual.cs`, `World/Firefly.shader`, `Resources/FireflyBody.mat` + `FireflyHalo.mat` |
| **Catch** | Within 3.5 m, look at one → `Press F to catch firefly` → `+1 firefly`, hotbar stack (10 per slot). Inventory full → the usual popup, bug stays. | `FireflyBug` (an `Interactable`), `World/FireflyPopup.cs`, `Hotbar.ItemId.Firefly` |
| **Hold** | Select the slot: the bug sits in the right hand on the shared viewmodel rig, glowing steadily, with a warm point light (range 7 m) and a `GrassPointLight` marker at torch parity (0.5). Co-op partners see it too. | `HeldItemViewmodel.BuildFirefly`, `HeldItemResolver` |
| **Eat** | Hold fire → the existing 1 s eat ring → bug raised to the mouth + chew loop → burp → `FireflyGlow.Grant()` (60 s). | `Hotbar.TickEatHold/ConsumeEquippedFirefly`, `HeldItemViewmodel.UpdateEating` |
| **Glow** | A glow SHELL over the astronaut — a clone of each body renderer (same mesh, same bones, the PlayerShadowProxy trick) drawn additive + fresnel-rimmed in firefly orange (HDR, gentle breathing; visor stays dark) — plus a point light on the player (range 9 m, grass parity), HUD chip `FIREFLY GLOW 0:59` under the cat-perk chip. Fades in 0.6 s, out over the last 3 s. | `Player/FireflyGlow.cs`, `Player/FireflyGlowShell.shader` + `Resources/FireflyGlowShell.mat`, `UI/FireflyGlowHUD.cs` |

## Night test

`FishingSun.SunDot(worldPos, body)` — the same geometric sun height the bite
roll uses (+1 noon, 0 horizon, −1 midnight). A swarm may spawn where the dot is
below **−0.05** and despawns once it rises above **+0.02** (hysteresis so the
terminator never flickers them). Deliberately NOT GalaxyTime — that clock is
decoupled from the sky on purpose.

## Spawner shape

Modelled on `CatSpawner`: deterministic (seed, body, cube-face cell) hash decides
where a swarm lives, so it is the same place for everyone and survives reloads
with no save state. Differences:

- Auto-singleton (DDOL, seeded in `EnsureGameplaySingletons`) rather than a
  scene component — nothing to wire, nothing for Sam to save.
- A cell holds a **swarm**, not one bug. Round 2 (Sam: "my goal wasn't small
  clumps, it was spaced out good coverage"): cell 40 m, chance 0.75, radius
  140 m, cap 36 swarms of 3–5 bugs over a 16 m patch. Each bug owns a home
  point on a sunflower spiral across the patch and wanders ≤ 3.5 m from it, so
  the bugs stay ~8–10 m apart. Density is what binds, not the cap.
- A cell whose swarm has been fully caught is **depleted** for 8 minutes
  (session memory only), then refills.
- Real lights: the **12 nearest** bugs within 60 m get a `Light` (1.1 / 5 m) +
  grass marker. Everything else glows by emission + halo only. Knob `maxLitBugs`
  (0 = none). Why capped at all: the planet is one mesh and every real light is
  one more draw of it (Built-in forward ForwardAdd), whatever the bug looks
  like. The grass faked-light pool was widened 16 → 32 slots so a dozen
  fireflies and the village lanterns fit together.

## The bug

Procedural — no art pack. A stretched sphere 0.20 m long (bobber-sized) with
two wing quads that **flap in the vertex shader** (18 beats/s, ±38° about the
wing root, per-bug phase): the
head half is dark, the tail glows an HDR yellow-orange (about 4× over white so
the atmosphere post's HDR exemption keeps it bright at night and bloom picks it
up). A 0.5 m additive billboard halo makes it a twinkle from 100 m. One shader,
`SoundOfSpace/Firefly`, GPU-instanced with per-instance blink phase/period,
glow floor and fade, so a hundred bugs are a couple of draw calls. The blink
lives in the shader (`_Time`); C# mirrors the same curve for the point light.

Movement is in the swarm's planet-local frame (parented to the `CelestialBody`
through `ParentToBodyPhysicsFrame`, +Y = radial up) so orbit and floating-origin
shifts cost nothing. Each bug steers smoothly toward a target point re-picked
every 1.5–3.5 s; the target's ground height comes from one raycast per re-pick.

## Gaze / catch rules

No colliders on bugs (the gaze system's mesh-silhouette path handles them, like
the cats). To stop a swarm from double-catching or flip-flopping the prompt:
the spawner picks ONE **focused** bug per frame — nearest to the crosshair among
those in catch range — and only that bug can interact or own the prompt.
Promise/grade rule: the bug whose prompt is on screen is the bug you catch.

## Decisions (Sam can flip any of these)

- **Stack of 10.** Eight slots of single bugs would be miserable.
- **Re-eating refreshes to a full 60 s** — replaces, never adds (cat-perk rule).
- **Glow is not saved**, cleared in `NewGameReset` (cat-perk precedent). Caught
  fireflies in the hotbar are saved for free (generic slot save).
- **All planets except the Sun / static attractors.** `FeatureVault.Fireflies`.
- **The body glow is a shell, not emission — and this was round 3.** The
  first build wrote `_EmissionColor` into the suit's property blocks and
  switched `_EMISSION` on in `Astronaut Mat/Suit.mat`. It glowed for nobody:
  the astronaut's materials are EMBEDDED in `Astronaut.fbx` (`materialLocation
  = InPrefab`), the on-disk `Suit.mat` is not what the model uses, and an
  embedded material cannot have a keyword switched on. `Suit.mat` is back to
  its original state. The shell depends on no material but its own.
- No new audio: catch is silent like every other resource pickup; eating uses
  the existing chew loop + burp.
- Co-op: partners see the held bug; the body glow is local-only (v1 gap).
