# Performance & bug findings — 2026-09-05

Research only; nothing below has been changed. Sam picks, then I fix.

How this was gathered: the gameplay scene opened read-only in the Editor and
measured (every per-frame script, renderer, light, shadow caster, particle
system, canvas, collider), every runtime script scanned for per-frame waste,
the project's quality/physics/player settings read, the July static-analysis
backlog re-checked against today's code, and **your actual saved graphics
settings read from the registry** (the values the built game runs with).
Frame-time numbers come from the 2026-07-28 profiler session and are scaled;
anything marked *estimate* has not been re-profiled.

Baseline to keep in mind: the game is **CPU-bound in the field and draw-call
bound in the village**. All `Update()` scripts together cost ~0.5 ms. Poly
counts do not matter. The costs are: grass, shadows, lights multiplying draws,
UI canvas rebuilds, and the dust field.

---

## A. Your saved settings are undoing the July fix — biggest lever, no code

In July, changing four Quality settings took the GPU from 22.8 ms to 3.0 ms per
frame. But `InputSettings` re-applies **your pause-menu prefs** on every launch,
and the registry shows the built game runs with:

| Setting | You run | July fix / sane | What it costs |
|---|---|---|---|
| MSAA | **4x** | 0 or 2x | full-screen 4x on an HDR target, plus every image effect |
| Shadow cascades | **4** | 2 | the sun shadow map renders the scene 4 times instead of 2 |
| Shadow distance | **193 m** | 100–120 | more casters in the map; the village has 739 active casters |
| Grass distance | **1.81x** | 1.0–1.25 | cost scales with area: 1.81x = **3.3x the blades** of 1.0x. ~4.4 ms vs ~1.3 ms per frame (*estimate from July's 3.0 ms at 1.5x*) |
| Radial motion blur | ON | off | one extra full-screen pass every frame it's allowed to run |
| Chromatic aberration | ON | taste | one extra full-screen pass |

**What it means:** this is very likely the difference between the ~70–80 fps you
see in the field and 110+. It changes look (softer far shadows, slightly less
edge smoothing, shorter grass horizon), which is why I didn't touch it.
**Effort:** pause menu, 2 minutes. If you want it locked in, I change the code
defaults so a fresh install starts sane (`InputSettings` lines 237–241 and the
`LoadSettings` fallbacks) and add a one-time migration that resets these five
prefs. Also `PixelLightLimitFix` forces 64 pixel lights (see B2).

---

## B. Rendering structure — real fps, needs code or scene work

### B1. The village is one giant mesh that every lantern re-draws
`TOWN-VILLAGE` was mesh-combined into ONE 111,635-triangle mesh (plus a glass
mesh). In the built-in forward renderer, every per-pixel light whose range
touches a renderer's bounds re-draws that whole renderer once more. The village
bounds are touched by **21 lights** (14 lanterns at range 22, ForcePixel, on
all day) → the 111k mesh is drawn ~22 times per frame, then again per shadow
cascade. That is the village draw-call wall (3.5k draws measured in July).

Fixes, in order of bang for buck:
- **Lanterns off in daylight** (they're invisible against the sun anyway). One
  small script keyed on the sun dot, like the grass already does. Removes 14
  per-pixel lights from every daytime frame. *Estimate: village day draws −40%.*
- **Re-bake the village combine into spatial chunks** (~6–10 clusters instead
  of 1) so each lantern only re-draws the houses near it. `MeshCombineTool`
  already combines per selected cluster; this is a re-run with different
  selections. Zero visual change.
- Lantern range 22 → ~12 (visual: lanterns light a tighter pool).

### B2. Pixel light cap is forced to 64
`PixelLightLimitFix` sets `pixelLightCount = 64` (Unity default 4) to stop
torch flicker. Its comment says cost "is tied to geometry, not the cap" — that
is wrong for forward rendering: with 64 every overlapping light is a full extra
pass; with 4, lights past the first four per object drop to cheap vertex
lighting. The scene has **3,046 renderer-light overlaps** (802 renderers × the
lights touching them); 346 renderers are touched by more than 4 lights.
**Fix:** cap at 8, keep `ForcePixel` on the torches/lanterns you care about so
they never demote (that was the flicker cause). Visual: distant tube lights on
the moon tunnel go vertex-lit. *Estimate: hundreds of draws per frame near the
shuttle and village.*

### B3. A debug point light at the Sun lights the entire game twice
`Sun/Point Light (Sun)`: range 40,000 m, intensity 1, always on, touches all
802 renderers. It's the F8 toggle in `LightingDebugToolbox`. Every visible
object gets one extra ForwardAdd pass from it, on top of the directional sun.
**Fix:** press F8 in play and look; if nothing changes, delete the light. If it
does change the look (fill on day sides), set it to `Not Important` (vertex).
*Estimate: −1 draw per visible object, every frame.*

### B4. The shuttle is 237 renderers and 235 shadow casters
You start in it and return to it constantly. Every rib, trim strip and bolt is
its own draw, its own shadow-caster (×4 cascades today), and gets 7 lights of
ForwardAdd. **Fix:** combine the hull by material inside the prefab (via the
existing prefab-patch tooling, colliders untouched) → ~15 renderers; set
`Cast Shadows = Off` on the 59 tiny parts. Medium effort, zero visual change,
prefab is hand-maintained so it's done with `LoadPrefabContents`.

### B5. Shadows on things that can't cast a useful shadow
- 60 shadow casters are on **transparent/glass materials** (48 `Glass` panes,
  10 pod glass, the mirror). Glass shadows are wrong AND cost a pass per cascade.
- 236 casters are **tiny** (< 35 cm): moon-tunnel cage lights ×132, shuttle
  bits ×59, village bits ×20.
**Fix:** one editor script flips `Cast Shadows = Off` on those by rule; commit
the scene. No visible change except glass no longer casting solid shadows.

### B6. The wish-cage mirror renders the whole world a second time
`MirrorReflection` on `WishCage` renders a second full camera into a 1024 RT on
every frame the mirror is on screen. Fine as a feature, but it's a hidden
"half your fps when you look at it". **Options:** 512 RT + a layer mask that
skips grass/dust/particles, or refresh every 2nd frame. Small effort.

---

## C. CPU per-frame — measured hot spots

### C1. Grass matrix rebuild (~1.5–2 ms at 1.0x, more at your 1.81x)
`InstancedGrassRenderer.Draw()` multiplies every blade's cached local matrix by
the planet's matrix every frame (~36k–49k blades). The planet **doesn't spin**,
so its rotation never changes — only translation (orbit + origin shifts).
**Tier 1 (safe):** cache world matrices per cell, and on each frame only patch
the translation column (3 adds per blade instead of a 4×4 multiply). **Tier 2
(bigger):** pass the planet matrix to the shader as a global and `Array.Copy`
the cached local matrices — CPU cost becomes a memcpy. Tier 2 touches the grass
shader and must be verified in a real build (the documented INSTANCING_ON
stripping trap). *Estimate: −1 ms (tier 1), −1.5 ms+ (tier 2) at 1.0x.*

### C2. Space dust (~1.5 ms)
5,000 specks looped every frame with a per-speck black-hole distance, density
noise, wrap, and an ocean-occlusion loop. Specks are world-fixed, so the per-frame
work can be **amortized over 2 frames** (advance half the specks with 2·dt).
Visual: none at 60+ fps. *Estimate: −0.7 ms.* A Burst job would do better but
Burst isn't installed.

### C3. UI canvas rebuilds (~1.45 ms)
`HUD_Canvas` holds 102 graphics and `Overlay Canvas` 77. One changing element
(compass strip, vitals bar, clock text) re-batches the whole canvas each frame.
**Fix:** put the per-frame movers (compass strip, vitals, galaxy clock, FPS) on
their own nested `Canvas` components. Layout change only. *Estimate: −1 ms.*

### C4. Physics at 100 Hz
`fixedDeltaTime` is forced to 0.01 (the Time settings say 0.02). The only
reason it was 100 Hz was orbit determinism — **and the orbits are now clockwork
rails, so that reason is gone.** 50 Hz halves `PhysicsFixedUpdate` (0.85 ms)
and every `FixedUpdate` script (rider cage, fall damage, autopilot, fishing).
**Risk:** feel. The rider cage, fish tow and the 1 m/s depenetration clamp were
tuned at 10 ms steps; some constants would need re-checking. *Estimate: −0.5 to
−1 ms.* I'd do this last and you'd playtest it.

### C5. Origin-shift hitch (3 ms spike every ~26 s in flight)
`EndlessManager` re-syncs every collider on a rebase. Known, not cheap to fix;
listed so nobody re-hunts it.

---

## D. Bugs (confirmed in today's code)

1. **Dead enemies block new spawns for 30+ s.** `EnemySpawner.activeEnemies`
   only drops an enemy in `OnDestroy`, which happens after the 30 s ragdoll +
   shrink. Every corpse counts toward the concurrent cap, so after a fight the
   world goes quiet for half a minute. Fix: remove from the list on death.
2. **Fishing cast can crash on a missing main camera.**
   `FishingRodController.cs:783` uses `Camera.main.transform` unguarded; every
   other camera consumer in the project has a fallback. One-line fix.
3. **Orbit diagnostic writes a CSV every session, in builds too.**
   `OrbitClockProbe` (active in the scene) opens `orbit_probe_*.csv` under
   persistentDataPath in a build and logs all session. Gate it to Editor/cheats.
4. **Map orbit lines leak materials.** `MapOrbitLines` does `new Material` per
   line (3 sites) and destroys the line objects but never the materials → memory
   creeps every time the map rebuilds. Cache one shared material.
5. **Ship HUD edits a shared UI material asset every frame.** `ShipHUD.cs:402`
   calls `line.material.SetVector` on a `Graphic` — that's the shared asset, so
   the change bleeds to every user of that material and dirties the asset in the
   Editor. Use a per-instance copy or a MaterialPropertyBlock-style approach.
6. **One missing script** on `--- Managers ---/SolarSystemMap` (never existed
   in git). Harmless; remove.
7. `FXAATest` allocates `new Material` every frame it renders. Not on any camera
   today, so dead — delete it so it can't be re-added by accident.
8. `PixelLightLimitFix`'s cost comment is wrong (see B2) — fix the comment when
   the cap changes so it doesn't get "restored" later.

Verified NOT bugs (the July backlog's unconfirmed leads): every per-frame
`FindObjectOfType`/`Camera.main` hit (43 + 23) is null-guarded or throttled;
`StorageUI`/`FishStagingUI` icon scans are cached; `NBodySimulation` caches the
sun; the LLM load path is hard-gated; `BuildMenuLock` is dead by design.

---

## E. Build size, load time, disk

- **`StreamingAssets/LlamaLib-v2.0.5` (3.9 GB) ships in every build.**
  StreamingAssets is copied verbatim into the player. The LLM never loads
  (audit §17). Deleting the folder locally makes every daily build ~4 GB smaller
  and faster to write. It's gitignored, so nothing to commit.
- Textures reachable from the main scenes: 435 MB, of which the alien
  metallic/normal maps (106 MB at 2048²) and the blood-splash flipbooks (58 MB)
  are the trimmable part (2026-09-05 audit addendum). Market atlases already
  compressed today.
- Incremental GC is OFF. Turning it on spreads collections over frames instead
  of one hitch. Free, low risk.
- Scripting backend is Mono. IL2CPP typically makes the hot C# loops (grass,
  dust) 20–40% faster, but the Windows IL2CPP module isn't installed and builds
  take longer. Optional, later.

---

## F. Not worth touching (so it doesn't get re-asked)
Update() scripts, GC per frame (2–6 KB), poly counts, camera near/far ratio
(reversed-Z on D3D11 handles it), the 26 unused SRP materials, the null prefab
material slots (all overridden in-scene), physics collider triangle counts.

---

## Suggested order

1. **A** — your prefs (2 min, you) + I harden the defaults.
2. **B1 lanterns-by-day + B3 sun point light + B5 shadow rules + E LlamaLib** —
   one afternoon, all scripted, no feel change.
3. **D1–D5 bugs** — small, safe, compile-verified.
4. **B2 pixel light cap + B4 shuttle combine + B1 village re-chunk** — measured
   with the Frame Debugger before/after.
5. **C1 grass tier 1 + C2 dust + C3 canvas split** — the CPU pass.
6. **C4 physics 50 Hz** — last, with a playtest.
