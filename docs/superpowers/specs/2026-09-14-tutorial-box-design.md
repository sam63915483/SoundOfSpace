🟢 ACTIVE — 2026-09-14 — Tutorial box (round 4: real generated planet, 350 m box)

# Tutorial box — phase 1 design

## What Sam asked for (2026-09-14)

A **TUTORIAL** button on the main menu, under START GAME and MULTIPLAYER. It loads a
small standalone scene, not the solar system: a 200 m × 200 m × 200 m box you are
constrained in, Milky Way skybox outside, the walls see-through with 0s and 1s raining
down them, and a flat green slab for a floor.

You load in standing in the shuttle's stasis pod. No black screen, no "wake up" text.
The pod door opens about a second later. The shuttle flies in from the top of the box,
stops 100 m above the floor, and — exactly like the real game — the cockpit computer
shows the landing camera and waits for you to land it. Then you walk around the box.
ESC opens the pause menu; MAIN MENU takes you back to the menu.

That is the whole of phase 1. Lessons after landing come later.

Decisions taken with Sam's OK (defaults he approved):
- The shuttle starts **just under the box ceiling** (~190 m up) and takes ~12 s to
  descend to the 100 m hover. Nothing passes through the walls.
- Once hovering, the existing tip pill shows **one hint line**: walk to the computer,
  press F, land it.
- **Everything is live** in the tutorial exactly as in the game — hotbar, oxygen,
  phone, pause menu, fuel gauge. (Not the helmet overlay: Sam, "that's old and not
  used".) Saving is disabled.
- Digits rain on **all four walls and the ceiling**; the floor is a solid green slab.
  Colour, speed and density are material sliders.

## What it means for the game

- One new scene (`Assets/4 - Scenes/Tutorial.unity`), built by an Editor menu item so
  it can be regenerated. It never touches `1.6.7.7.7.unity`.
- The shuttle intro, hover, landing camera and manual landing are the **real game's
  code, unchanged**, running against a fake planet. If the landing feels right in the
  game it feels right here, and any later fix to the real landing lands here too.
- **The tutorial never writes a save file.** Not the stasis pod, not death, not the
  backrooms transfer slot. Nothing you do in the tutorial can touch a real world.
- Entering the tutorial resets all the carried-over state (money, pockets, fuel,
  story flags) exactly as a New Game does, so it always starts clean. Leaving it and
  starting a New Game or loading a save is unaffected: those already reset or restore
  everything.

## How it works

### The fake planet (the one trick)

The shuttle autopilot refuses to fly unless it is parented to a `CelestialBody`, and
its hover and landing measure "up" as the direction away from that body's centre. So
the tutorial scene has an empty object called **`Tutorial Ground`** at world
(0, −10000, 0) carrying a kinematic `Rigidbody` and a `CelestialBody`
(`bodyType = Planet`, `radius = 10000`, no generator, no terrain). The box, the floor
and the shuttle are its children. Over a 200 m box the "radial up" differs from
straight up by at most 0.6°, which is invisible.

There is **no `NBodySimulation`** in the scene. That matters: the player controller
switches to its built-in flat-floor gravity only when no simulation component exists.
A simulation with zero bodies would leave the player in zero-g, unable to walk.
`NBodySimulation.Bodies` returns an empty array, so every HUD and spawner that scans
bodies sees none and idles. Only the shuttle knows about the fake planet, because it
finds its parent directly.

### The box

All children of `Tutorial Ground`, all on **layer 10 (Body)** so both the player's
ground check and the shuttle's ground rays hit them:

| Part | Shape | Material |
|---|---|---|
| Floor | cube 200 × 1 × 200, top face at world y = 0 | `Green.mat` (the existing flat green) |
| 4 walls | quads 200 × 200 at x = ±100, z = ±100, with BoxColliders | `TutorialDigitRain.mat` |
| Ceiling | quad 200 × 200 at y = 200, BoxCollider | `TutorialDigitRain.mat` |

`Custom/TutorialDigitRain` is a new unlit, transparent, double-sided shader. Each
wall is a grid of cells; every column has its own speed and phase; a bright "head"
falls down the column with a fading trail; each cell draws a procedural 0 or 1 that
re-rolls now and then. Properties: `_Color`, `_Cells` (columns across a wall),
`_Speed`, `_TrailLength`, `_Flicker`, `_Alpha`. Queue = Transparent. There is no
atmosphere post-effect in this scene, so the ≤2500 / >2500 queue rules don't apply.

Lighting: `RenderSettings.skybox` = the ESO Milky Way material the gameplay scene
uses (GUID `e3d301707e23ccd4e84049a21e148e54`), fog off, one directional light angled
down with shadows, plus a dim flat ambient so the shuttle's shadow side isn't pure
black (there is no atmosphere post to light it).

### The shuttle

`Shuttle_Lander.prefab` instance, **named exactly `Shuttle_Lander`** (the autopilot
finds it by name), child of `Tutorial Ground`, parked pose = sitting on the floor
near the centre of the box. Its authored pose is the landing target: the autopilot
hovers 100 m above it and lands back onto the floor from there.

Two small, additive changes in `ShuttleAutopilot.cs`:

1. `PrepareIntroApproach(float departAltitude = 4000f, bool straightIn = false)`.
   The real intro passes nothing and is bit-identical. The tutorial passes
   `departAltitude ≈ 190 − parkedHeight` so the start sits just under the ceiling.
2. When `straightIn` is set, the transit bezier's control point is the straight
   midpoint. Without it, the flight path leans 300 m sideways on purpose (to avoid
   hairpins on real approaches) and the shuttle would leave the box.

### The player

`Player.prefab` instance with the same overrides the gameplay scene uses:
`walkableMask = 34304` (Ship | Body | ShuttleInterior) and the camera's clear flags =
Skybox. `Planet Effects.asset` is removed from the camera's post-effect list (no
planets, and an empty effect list allocates a material every frame); Bloom and FXAA
stay. The player is placed in the pod by the director, so no spawn point is needed.

### The director (scene object: `TutorialDirector`)

A plain `MonoBehaviour` on a `Tutorial Director` object in the scene. Serialized
knobs, all with defaults: `doorOpenDelay = 1`, `descentSeconds = 12`,
`departAltitude = 190`, `landingHint` text.

Start-up sequence (one coroutine):

1. `TutorialSession.IsActive = true` (also set by the scene-loaded hook, see below).
2. `Time.fixedDeltaTime = 0.01` (normally `NBodySimulation.Awake` does this).
3. Wait one frame + one FixedUpdate, then `NewGameReset.Apply()` — the same reset a
   New Game runs, minus the deferred seed save (that lives in the runner the menu's
   New Game button spawns, which the tutorial never creates).
4. `pilot = ShuttleAutopilot.EnsureAttached()`;
   `pilot.PrepareIntroApproach(departAltitude, straightIn: true)`.
5. Put the player in the pod: rigidbody pose = `StasisPod.TransformPoint(0, 1.02, 0)`,
   facing the pod's forward, `Physics.SyncTransforms()`. `pilot.CaptureIntroRiders()`.
6. `IntroSequenceController.ShuttleWakeActive = true` and
   `PlayerController.isInDialogue = true` while the door is shut (the pod-save
   watcher and the drift-through-closed-door pin both key off these).
7. `pilot.LaunchIntroApproach(descentSeconds)` — engines light, descent starts.
8. Wait `doorOpenDelay`, then `StasisPodDoor.OpenHold()`, `isInDialogue = false`,
   `ShuttleWakeActive = false`.
9. Wait for `pilot.CurrentPhase == Hover` → `TutorialUI.Instance.ShowStep(hint)`.
10. Wait for `Parked` → `TutorialUI.Instance.HideAll()`. Done. The ramp opens by
    itself (real touchdown code) and the player walks out onto the slab.

### `TutorialSession` (static, one file)

```
public static class TutorialSession {
    public const string SceneName = "Tutorial";
    public static bool IsActive { get; }      // active scene name == SceneName
    public static void Enter();               // main-menu entry
}
```

`Enter()` mirrors `MainMenuController.EnterGameplay`: destroys the menu-seeded
`CameraEffectsManager (menu)`, then
`LoadingScreen.Instance.LoadSceneAndShow("Tutorial", preSceneSetup: EnsureGameplaySingletonsAsync)`
(fallback: seed sync + `SceneManager.LoadScene`). The singleton seeding is what makes
everything live in a build, same as PLAY (CLAUDE.md trap #1). In the Editor, pressing
Play directly in the Tutorial scene works too: the auto-singletons create themselves
because the active scene isn't MainMenu.

### Fences (the codebase treats "not MainMenu" as the real game)

Verified: there is **no periodic autosave** and **no SAVE GAME pause button** any more
(both removed 2026-08-18). The only file writers left are gated:

| Writer | Gate |
|---|---|
| `StasisPodSave.Update` — valve press then seal = UPLOAD | force `download: true` when `TutorialSession.IsActive` — the ritual animation plays, no file is written |
| `DeathCutsceneController` — reloads the newest save into `1.6.7.7.7` | when `TutorialSession.IsActive`, take the existing "no save on disk" branch: in-place respawn, no scene load |
| `NewGameReset.SeedSaveWhenLanded` | never runs: only the menu's New Game button spawns its runner |
| `PortalManager` transfer slot | unreachable: no portals in the scene |

Other fences:
- `TabbedPauseMenu`: hide the MULTIPLAYER row while `TutorialSession.IsActive`
  (single-player only). RESUME / SETTINGS / MAIN MENU work as-is. MAIN MENU is a
  plain `LoadScene("MainMenu")`; `MenuSceneCleanup` then clears the surviving HUDs
  exactly as after a normal game.
- `MainMenuController`: TUTORIAL row between MULTIPLAYER and CHARACTERS; the button
  column grows by one row (68 + 16 px) and shifts down by half of that so the top
  edge stays clear of the character chip.
- Build settings: `Assets/4 - Scenes/Tutorial.unity` added, enabled. The builder does
  this via `EditorBuildSettings.scenes`.

### The scene builder (Editor only)

`Tools ▸ Solar System ▸ Build Tutorial Scene` (`Assets/3 - Scripts/Editor/TutorialSceneBuilder.cs`),
modelled on `PlanetGalleryBuilder`: creates the scene **additively**, populates it,
saves it, closes it, restores the previously active scene. The open gameplay scene is
never touched. Rebuilding wipes and re-creates the scene; hand-placed extras will be
lost, so later tutorial content should be added either through the builder or after
we stop regenerating. Creates `TutorialDigitRain.mat` next to the scene if missing.

## Files

New:
- `Assets/3 - Scripts/Tutorial/TutorialSession.cs`
- `Assets/3 - Scripts/Tutorial/TutorialDirector.cs`
- `Assets/3 - Scripts/Editor/TutorialSceneBuilder.cs`
- `Assets/Shaders/TutorialDigitRain.shader`
- `Assets/4 - Scenes/Tutorial.unity` + `TutorialDigitRain.mat` (generated)

Edited (all small, additive):
- `Shuttle/ShuttleAutopilot.cs` — optional intro parameters + straight-in bend
- `UI/MainMenuController.cs` — TUTORIAL row
- `UI/TabbedPauseMenu.cs` — hide MULTIPLAYER in the tutorial
- `Tutorial/StasisPodSave.cs` — force download in the tutorial
- `Cutscenes/DeathCutsceneController.cs` — in-place respawn in the tutorial
- `ProjectSettings/EditorBuildSettings.asset` — via the builder

## Testing (Sam runs the playtests)

1. Editor: run the builder, open the Tutorial scene, press Play. Expect: in the pod,
   shuttle already descending, door opens after ~1 s, hover at 100 m with the NAV feed
   on the console and the hint pill, F → WASD/Q/E → SPACE lands it, ramp opens, walk
   on the green slab, walls rain digits, can't leave the box. ESC → MAIN MENU works.
2. Main menu: TUTORIAL button → loading screen → same as above.
3. Then START GAME → New Game and a Load: both behave exactly as before.
4. Check `saves/` has no new file after a tutorial run that used the pod valve.
5. Build sanity: TUTORIAL from the built exe (trap #1).

## Out of scope (phase 2+)

Lessons after landing, guided prompts beyond the one hint, a "restart tutorial"
option, controller glyph review, anything about the digit look beyond the sliders.

## Round 2 (2026-09-14, after Sam's first playtest)

Sam's notes: no crosshair; a lens flare stuck on screen with no sun; the compass /
boost / vitals were the old floating-card versions; digits too big; gravity too heavy;
top-left shuttle text (in the real game too); wants a real sun above and off to the
side, and grass / trees / crystals / cats / mushrooms on the slab.

What changed and why:

- **A real Sun + an n-body simulation.** The lens flare, the grass, the cats and the
  player's gravity all find bodies through `NBodySimulation.Bodies`, so the scene now
  has a `Body Simulation` with BOTH bodies **pinned** (nothing orbits or falls). The
  Sun is a `CelestialBody` (Sun) 25 km up at 55° elevation with the gameplay scene's
  directional light + `SunShadowCaster`, the warm point light the grass shader reads,
  and an emissive ball. Gravity now comes from the fake planet like a real one
  (`surfaceGravity = 8`, Humble Abode's number) instead of the 20 m/s² flat fallback.
  Consequence: `PlanetOxygen` treats the slab as a real planet; the director vents a
  55 % reserve so the slab breathes like Humble Abode at the start.
- **Flare through glass.** The flare's occlusion raycast hits every collider; the
  panes now carry `LensFlarePassThrough`, which `LensFlareRegistry.IsSampleBlocked`
  skips. Two real bugs fixed on the way: `LensFlareRegistry` had no `OnDisable`, so
  switching it off froze the last frame's images on screen (the stuck flare), and
  `CameraEffectsManager` found no `SettingsMenu` in the tutorial so **every** camera
  effect was off; both it and `TabbedPauseMenu` now fall back to `InputSettings.Active`.
- **Crosshair + helmet layout.** `CrosshairReticle` lives on a scene object (`Dot`) in
  the gameplay scene and is never seeded; the builder now creates it. The compass /
  boost / vitals clusters seat onto the helmet art only when a scene `HelmetHudConfig`
  exists; `Tools ▸ Solar System ▸ Snapshot HelmetHudConfig Prefab` copies the gameplay
  scene's into `Assets/1 - samsPrefabs/HelmetHudConfig.prefab`, which the builder
  instantiates. Re-run the snapshot after tuning the config in the gameplay scene.
- **Top-left shuttle text** was `ShuttleAutopilot.OnGUI`, drawn whenever cheats are on
  (always). Now opt-in: `ShuttleAutopilot.DebugOverlay`, F12 during a flight.
- **Props.** Trees, crystals and mushrooms are placed once at load by
  `TutorialPropField` (same prefabs, weights, scales and post-setup as the spawners,
  null owner) because the live spawners scan the whole 10 km sphere every tick.
  Grass (`InstancedGrassRenderer`, streams around the player) and cats (`CatSpawner`,
  stops scanning at its cap of 5) run live. The floor is now a MeshCollider named
  `Terrain Mesh`, which is what the grass seats on.
- **Digits**: 320 columns per wall (~0.6 m cells), longer trail.

## Round 4 (2026-09-14) — a real planet instead of the slab

Sam: "the green slab is earth … I want an atmosphere because the sky looks like nighttime
yet the sun is shining right at me … take Humble Abode's planet generation and blow it up
to 3× Cyclops … expand the box to 350 m … one big planet that's static and the user is
confined within a box on top of it."

**What changed**
- The fake 10 km body, the green slab, `TutorialPropField` and the vented oxygen are gone.
- The planet is a **Humble Abode clone at radius 1500 m** (Cyclops is 500): the builder
  clones HA's Shape / Shading / Atmosphere / Ocean / terrain material into
  `Assets/5 - External Imports/Celestial Body/Solar System/Tutorial Earth/` exactly the way
  `PlanetGalleryBuilder` makes dwarf planets (data copies; the generation CODE is untouched).
  Same seed → HA's exact terrain, 7.5× larger; scaling is uniform so slopes are identical,
  features are 7.5× bigger, and the mesh is 7.8 m per triangle instead of 1 m.
- The scene carries what the gameplay scene carries for a generated body: `SolarSystemSpawner`
  (300/100/50; collider = LOD0 for bodies ≥150 m) + `LODHandler` (without it the mesh is never
  assigned), `NBodySimulation` (both bodies pinned), the Sun with `SunShadowCaster`, a
  `waterline` trigger at r = 1500 (ocean level 1 → sea level = radius), and the player camera's
  full post stack (Planet Effects = atmosphere + ocean). Ambient is black like the game.
- **Named "Humble Abode"** on purpose: the grass renderer, the suit's O2 refill zone and a few
  other systems are keyed on that name, so everything behaves as on the real planet.
- **Flat spot**: at build time the builder samples the shape's height compute (read-only) on a
  5×5 grid across the box footprint for 3000 candidate directions, keeps the flattest one that
  is ≥8 m above sea level everywhere, rotates the planet so that spot faces +Y and places the
  planet so the spot's surface is world y = 0. The box stays axis-aligned at the origin.
  First build: 30 m height spread across the box, centre 17 m above sea level.
- **Box 350 m**: walls 350 × 450 (y −100…350) with a second digit material whose `_Aspect`
  keeps the glyph proportions; ceiling at 350. Descent starts at 330 m, 14 s.
- **Spawners = the gameplay scene's, snapshotted** (`Tools ▸ Solar System ▸ Snapshot Tutorial
  Spawner Prefabs` → `Assets/1 - samsPrefabs/TutorialSpawners/`): TreeSpawner, CrystalSpawner,
  MushroomSpawner, CatSpawner (cap 8 in the box), and the GrassSpawner object with the live
  `InstancedGrassRenderer` (baked blob cleared; it streams). On a 1500 m body the spawners'
  whole-sphere scans are ~50 k cells per tick — fine. Fireflies stay quiet (no night).
- Oxygen: trees are seeded by density, so the planet-baseline O2 comes out ~54 % like HA
  without any reserve.
- `Tools ▸ Solar System ▸ Snapshot ALL Tutorial Prefabs` refreshes helmet config + player +
  spawners in one go (opens the gameplay scene additively, never saves it).
- Grass diagnostic: 15 s after touchdown the director logs how many grass cells are streaming.
- `LetterboxBars` no longer fires during the pod phase of the intro / tutorial (the movement
  pin reused the dialogue flag): no black bars over the fade-in, in either.
