# HANDOFF: Build 20 new observation dimensions (D9–D28)

**For:** a fresh Claude session with Unity MCP (coplay) + this repo.
**Date:** 2026-07-05. **Branch:** `feat/blackhole-dimensions` (stay on it).
**Read first:** CLAUDE.md (hard traps), then this file end-to-end before touching anything.

## Mission

The game has 8 working "observation dimensions" inside the black hole (D1–D8): worlds
where geometry obeys one rule — *things are only fixed while observed* (or its
inversions). The user wants **20 MORE dimensions (D9–D28)** to test-drive, so he can
pick the ones with the best vibes and fold them into the main game.

Non-negotiable bar: **each dimension must work on his first try.** He will jump in via
the dev loader, play it cold, and judge it. You verify every dimension yourself in
play mode (protocol below) BEFORE telling him it's ready. He has limited usage — do
not burn his time on broken levels or excessive round-trips. Batch work, verify
smartly, commit per milestone.

Keep each dimension SIMPLE: one landscape with a strong mood + 1–2 observation
puzzles. Variety comes from landscape/color/audio and which mechanic is remixed, not
from new engineering. No enemies that damage; failure = setback (teleport/respawn),
never death.

## Task 0 (do this first): multi-digit dev loader

`Assets/3 - Scripts/Dimensions/DimensionDevLoader.cs` currently maps Shift+D+1..8
directly. Rework it TV-remote style because keyboards only have digits 0–9:

- Hold Shift + D, type digits: they accumulate into a buffer ("1", then "7" → 17).
- **3 seconds after the last digit**, teleport to `D<number>` via
  `PortalManager.EnterInterior(sceneName)` (numbers 1–28; ignore invalid).
- Show the typed buffer on screen while it counts down (a simple `OnGUI` label or
  `InteractPromptUI.ShowOneShot("Dimension " + buffer + "…")` is fine).
- Scene name array covers D1..D28 (fill D9+ names as you create them; guard: if the
  scene isn't in Build Settings yet, log a warning instead of loading).
- Keep: auto-singleton pattern + MainMenu early-return + it stays seeded in
  `MainMenuController.EnsureGameplaySingletonsAsync` (already done — don't duplicate).

## What already exists (all under `Assets/3 - Scripts/Dimensions/`)

**Core (reuse, don't reinvent):**
- `ObserverState.IsObserved(Bounds, maxDist)` — frame-cached frustum test. Frustum-only
  (no occlusion). "Behind you" is always unobserved.
- `ObservationTracker` — grace window (constructor arg, default 0.15s) + `Tick(bounds,
  out justLost, maxDist)` + `WasEverObserved`/`TimeUnobserved`/`Reset()`.
- `DimensionSceneUtil` — `ApplyAtmosphere` (fog/ambient/bg — call once from Update when
  `ObserverState.Cam != null`), `CreateDirectionalLight`, `CreatePortal` (LevelPortal
  trigger), `Mat`/`EmissiveMat`/`FadeMat`, `Block` (primitive on the WALKABLE layer),
  `ToneClip` (sine placeholder audio), `LoopingAudio`. Plus `FlickerLight`.
- `BeamAdditive.shader` ("Dimensions/BeamAdditive") — unlit additive fog-off glow, for
  light shafts/beams. Use for any glow-volume; Standard Fade goes BLACK in night fog.

**The 8 dimensions (read 2–3 of these as style references before writing your own):**
- `ShiftingMazeController` (D1 + re-themed as D5 via public theme colors): grid maze,
  cells reshuffle when unobserved, corner pillars (z-fighting), single vanishing exit
  door. Best template for "endless reshuffling place".
- `WellFieldController` (D2): static terrain mesh + objects that relocate when
  unobserved + gaze-reactive audio (volume by look-alignment — pure sine tones are
  NOT ear-locatable; gaze-volume is the working "audio radar" pattern).
- `LongDarkController` (D3): fixed slots, 2 tiles hop between empty slots when
  off-screen; solid only while a sliver is on screen (FULL FLAT footprint bounds —
  tall test boxes cause the look-up bounce bug); underfoot drop after grace.
- `WaitingFieldController` (D4): stalker exit — advances unseen, holds distance while
  watched, portal dormant until it reaches you.
- `FrozenSeaController` (D6): the world observes YOU (sweeping lighthouse beam that
  locks on + whiteout-teleport); relocating campfire exits w/ particle smoke plumes.
- `HallOfDoorsController` (D7): room-shell pairs, colored doors, gaze+2m+F prompt via
  `InteractPromptUI.Show/Clear` (the game's standard pill), color-sequence puzzle.
- `ProcessionController` (D8): encircling crowd (polar movement to angle-sorted ring
  slots — NEVER path through the player), blackout pulses (screen-black windows where
  everything counts as unobserved + teleport lunges), risers, escalating ambience.

**Scenes:** `Assets/4 - Scenes/Dimensions/D*.unity` — each is just an
`InteriorPlayer` prefab instance + a `DimensionRoot` GameObject with one controller.
Controllers build their ENTIRE world at runtime in Awake. Scenes carry almost nothing.

**Chain wiring:** each controller has a serialized `nextScene` string; a
`DimensionSceneUtil.CreatePortal`/LevelPortal trigger loads it. Current chain:
black hole → D1→…→D8 → `R1_Backrooms` (hub; EXIT→cabin, Poolrooms=one-way trap).
**Wire D9→D10→…→D28→`R1_Backrooms`, and leave D8→R1_Backrooms UNCHANGED** — the 20
are a test reel; the user will cherry-pick winners into the main chain later.

## Hard-won gotchas — every one of these cost a bug. Do not relearn them.

1. **Walkable layer:** the player can only stand on layers Ship/Body
   (`PlayerController.walkableMask` = 1536). `DimensionSceneUtil.Block` sets layer
   "Body". ANY hand-made GameObject with a collider the player stands on (terrain
   meshes!) needs `go.layer = DimensionSceneUtil.WalkableLayer` or the player slides
   and can't jump.
2. **`Block` positions in WORLD space.** Build compound objects at identity, THEN
   `SetPositionAndRotation` the parent. Violating this piled 40 doors at the origin.
3. **Never put a movable AudioSource (or anything you'll move) on the controller's
   own GameObject** — moving it moved the entire world once. Child objects only.
4. **Scene-serialized values beat script defaults.** Fields that existed when a scene
   was saved keep their saved values; changing the script default does nothing for
   that scene. Push changes with a SerializedObject editor script (pattern below).
   Fields added AFTER the scene was saved use script defaults — design tunables so
   defaults are right, and you rarely need scene surgery.
5. **Z-fighting:** two equal-thickness boxes sharing a face plane shimmer. Butt-join
   against slightly-fatter connectors (D1 pillars) or use asymmetric thicknesses.
6. **Standard-Fade / emission / additive materials created in code**: covered for
   builds by anchor mats in `Assets/2 - Materials/Dimensions/` (registered as
   Preloaded Assets). If you introduce a NEW shader or Standard variant, add an
   anchor mat the same way (see `MakeAnchorMats` pattern below).
7. **Observation test bounds:** keep test boxes FLAT for floor tiles (tall boxes clip
   the frustum bottom when looking up → false "observed" → bounce bug). For "any
   sliver visible counts", use the full flat footprint. For "must actually look at
   it", use a small core. Objects that relocate should prefer off-screen landing
   spots (sample up to 8 candidates, test with `ObserverState.IsObserved`).
8. **One-shot `justLost` edges get eaten** (e.g. while the player is inside an
   exemption). For anything that must eventually react, drive it from persistent
   state (`TimeUnobserved` + a "handled since last seen" flag), not the edge.
9. **PlayerController access:** cache `FindObjectOfType<PlayerController>()` with the
   `--_playerRefindCooldown <= 0` pattern (see any controller). NEVER per-frame
   without the cooldown. Move the player only via `rb.position`/`rb.velocity`.
10. **Coplay/editor workflow:** `execute_script` runs a C# file with public static
    methods (`methodName` selects; NO JObject args — it fails to compile). The
    `create_scene` MCP tool times out — create scenes via `EditorSceneManager` in an
    editor script instead. Unity console logs are read with `get_unity_logs`
    (search_term supported). `check_compile_errors` after every script batch.
11. **Stale play sessions look broken** (pink shaders, frozen logic). After a
    recompile, always exit/enter play mode before judging behavior — and tell the
    user the same if he reports something you just verified working.
12. **Do not touch** the forbidden celestial/atmosphere zones (CLAUDE.md trap #2),
    `R1_Backrooms`, `PoolroomsDemo`, or `Assets/1.6.7.7.7.unity` (no gameplay-scene
    edits are needed for this task).

## Editor-script templates (write to your scratchpad, run via coplay execute_script)

**Refresh + compile check** (run after every batch of .cs writes):
```csharp
using UnityEditor; using UnityEngine;
public class RefreshAssets { public static void Execute() { AssetDatabase.Refresh(); Debug.Log("[claude] refreshed"); } }
```

**Scene creation** (one static method per dimension; controller type must be compiled
FIRST — write all controllers, compile-check, then create scenes):
```csharp
using System.Collections.Generic;
using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public class CreateScenes
{
    public static void D9() { var root = CreateScene(1.5f); root.AddComponent<YourController>(); SaveAndRegister("D9_YourName"); }
    // ... one per dimension; playerY ~1.5 for flat floors, ~15-20 for terrain drops

    static GameObject CreateScene(float playerY)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1 - samsPrefabs/InteriorPlayer.prefab");
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.transform.position = new Vector3(0f, playerY, 0f);
        return new GameObject("DimensionRoot");
    }
    static void SaveAndRegister(string sceneName)
    {
        string path = "Assets/4 - Scenes/Dimensions/" + sceneName + ".unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool present = false; foreach (var s in list) if (s.path == path) present = true;
        if (!present) list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("[claude] " + path + " registered, total=" + list.Count);
    }
}
```

**Scene tunable surgery** (only when a saved scene's serialized value must change):
SerializedObject on the found component → `FindProperty(field).floatValue = x` →
`ApplyModifiedProperties` → `MarkSceneDirty` + `SaveScene`.

## Verification protocol (per dimension, before the user ever sees it)

1. `check_compile_errors` → clean.
2. Open the scene, `play_game`, wait ~5s, `get_unity_logs` (errors only) → EMPTY.
3. Probe via `execute_script` (play mode): player y stable (grounded, not falling),
   the dimension's key objects exist, and — for anything mechanical — force the
   mechanic and assert (e.g. rotate the player root 180°, verify the reshuffle/
   relocation actually happened by comparing positions; teleport the player into the
   exit portal region and verify the next scene loads).
4. `stop_game`. Fix anything found. Only then move on.
Batch: verify 4–5 dimensions per play session where possible (they're separate
scenes, so it's one open+play+probe cycle each — keep probes to ONE script with
multiple methods).

## The 20 dimensions (concepts — adjust freely, keep 1–2 mechanics each)

Naming: scene `D<n>_<Name>`, controller `<Name>Controller`. Chain D9→…→D28→R1_Backrooms.
Mechanic keys: [reshuffle] [relocate-unseen] [sliver-tiles] [stalker] [world-watches]
[gaze-audio] [blackout] [risers] [inverse: solid-only-unseen].

- **D9 The Red Forest** — endless dark-red pine grid, heavy fog. Trees [reshuffle]
  like D1 cells; a glowing white doe stands still while watched, walks toward the
  exit clearing while unseen — follow it [stalker, inverted: it leads].
- **D10 Salt Flats** — blinding white plain, black sky. Cracks in the ground open
  where you're NOT looking [inverse]; fall = respawn. A distant black pyramid exit.
- **D11 The Shelves** — D1-engine reskin: server-room aisles, cold blue, humming;
  exit door chance high but door despawns fast (short grace) [reshuffle].
- **D12 Mirror Lake** — glassy black water floor (walkable), starfield above AND
  below. Floating islands [relocate-unseen]; the true island hums [gaze-audio].
- **D13 The Orchard** — rows of identical white-blossom trees; one bears red fruit
  [gaze-audio hum]; picking wrong trees (walk-through trigger) scrambles you
  [relocate punishment]. Soft pink fog.
- **D14 Glacier Throat** — ice canyon corridor; ice bridges are [sliver-tiles] over
  a crevasse; blue glow below, wind audio.
- **D15 The Congregation** — cathedral interior (tall pillars, colored light shafts
  via BeamAdditive); pews reshuffle into new maze rows when unseen [reshuffle]; the
  altar door only opens while you look AWAY from it [inverse interaction].
- **D16 Neon Grid** — Tron-flat dark city blocks, emissive edge lines; buildings
  [reshuffle]; a taxi-light exit cruises the streets and parks while watched
  [stalker inverted].
- **D17 Tide Pools** — shallow water over sand, moon overhead; stepping rocks
  [sliver-tiles] across a deep channel; bioluminescent glow marks the far shore.
- **D18 The Static Field** — TV-static night meadow (use FlickerLight aggressively);
  [blackout] pulses reveal that scarecrows have moved closer [stalker crowd-lite,
  few statues, no encircling — pure dread].
- **D19 Bone Garden** — pale spires and ribcage arches on dark soil; one arch is the
  exit and [relocate-unseen]s; wrong arches teleport you [relocate punishment];
  low gaze-audio heartbeat.
- **D20 Cloud Shelf** — walking on solid cloud tops above a sunset sea; cloud
  platforms are [inverse: solid only while UNobserved] — cross by walking backwards
  (D3's old inverted rule, which was cut and is still a good trick).
- **D21 The Archive Stacks** — vertical library: spiral of shelf-ledges; ledges
  [sliver-tiles]; books whisper louder as you face the exit level [gaze-audio].
- **D22 Rust Sea** — scrapyard dunes of orange metal; a crane light sweeps
  [world-watches, reuse D6 beam logic]; exits are 3 burning barrels [relocate-unseen,
  smoke plumes].
- **D23 Wheat at Dusk** — waist-high golden field (thin stretched cubes), purple sky;
  paths part where you look (walls of wheat are [inverse] solid); find the well.
- **D24 The Waiting Room** — infinite beige office lobby (D1 reskin, chairs+plants);
  intercom chimes [gaze-audio] toward the true EXIT sign; wrong exits loop you back.
- **D25 Candle Sea** — pitch-black floor covered in thousands of candle points
  (emissive dots, cheap); candles extinguish where you look [inverse]; keep the
  darkness ahead lit by NOT looking; exit = the one candle that never goes out.
- **D26 The Ferry** — a long stone pier over black water; pier segments behind you
  crumble (despawn permanently once fully off-screen — one-way pressure); a bell
  tolls from the exit boat [gaze-audio].
- **D27 Inverted Rain** — rain falling UP over a cobblestone plaza (particles,
  gravity -1); doorways [reshuffle] around the plaza edges; only the door whose
  rain falls DOWN is real (particle tell instead of audio).
- **D28 The Long Table** — a banquet hall with a table stretching to the horizon;
  chairs rearrange when unseen [reshuffle]; walk the tabletop; candelabra risers
  [risers] block the way; exit door at the far end never moves — pure gauntlet.

## Process expectations

- Work in batches: write 4–5 controllers → compile → scenes → verify → commit
  ("feat(dimensions): D9-D13 ..."). ~4–5 commits total plus the dev-loader commit.
- Reuse `DimensionSceneUtil` and existing controller patterns aggressively; serialized
  tunables at the END of each MonoBehaviour (repo convention), theme via fields where
  a reskin is possible (see D5 using ShiftingMazeController).
- Placeholder audio = `ToneClip`/synths (see Grunt/Shriek clip gen in
  ProcessionController). Fine for this pass.
- The user tests with the dev loader and expects FIRST-TRY success. If a mechanic
  can't be self-verified (pure feel), say so explicitly when handing over.
- Update `docs/superpowers/specs/2026-07-05-blackhole-dimensions-design.md` is NOT
  needed; instead append a short section to the memory file
  `~/.claude/projects/.../memory/blackhole-dimensions.md` when done (what shipped,
  any new gotchas).
- Commit hygiene: `git add` new `.cs` AND `.meta` (focus Unity / AssetDatabase.Refresh
  first so metas exist). Push nothing; the user pushes to `soundofspace` himself.
