# CLAUDE.md

Working guidance for this repo. Kept deliberately short — it holds the **hard
traps**, the **non-negotiable conventions**, and **pointers** to the detailed docs.

**For "how does system X work / what exists right now", read these first:**
- `docs/CURRENT_STATE_AUDIT.md` — verified, system-by-system snapshot of the whole game + a scrap list (the single best map of the current state).
- `docs/GAME_OVERVIEW.md` — short / medium / long technical write-ups.
- `docs/architecture-diagram.html` — interactive Mermaid architecture view.
- `docs/superpowers/_archive/` — historical per-feature specs + plans (why/how each feature was built; not the source of truth).

Don't duplicate those here. If you change a system materially, update the audit, not this file.

---

## ⚠️ Three hard traps (read before touching anything)

### 1. MainMenu singleton trap — "works in Editor, broken in build"
The build boots in `MainMenu.unity` and loads `1.6.7.7.7.unity` on PLAY/LOAD. The Editor usually presses Play directly in the gameplay scene. `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` fires **once**, after the *first* scene loads — which in a build is MainMenu. So any auto-singleton that does `if (activeScene == "MainMenu") return;` **never auto-creates in builds**.

**Rule:** every MainMenu-skipping auto-singleton **must also be seeded in `MainMenuController.EnsureGameplaySingletons()`** (mirror an existing `if (X.Instance == null) {...}` block). After adding one, sanity-check a build. The canonical bug from breaking this: torches flickered for two days because `PixelLightLimitFix` wasn't seeded. Same applies to debug/dev tools — invisible in builds unless seeded.

### 2. DO NOT TOUCH — atmosphere & procedural planet generation
A past session broke a working build editing here (killed the planet's directional light, removed the top grass layer; unrecoverable). The code is fragile and shader-coupled. **Read-only inspection is fine; modification is not. If a task seems to need changes here, stop and ask.**

Forbidden zone:
- `Assets/3 - Scripts/Scripts/Game/Atmosphere.cs`, `CustomImageEffect.cs`
- `Assets/3 - Scripts/Scripts/Post Processing/Planet Effects/`
- All of `Assets/3 - Scripts/Scripts/Celestial/` (generators, settings, `Shape/*`, `Shading/*`, `NoiseSettings/*`, `Texture Gen/*`)
- Any `.shader`/`.compute`/`.hlsl` under those, and `Assets/2 - Materials/` planet/ocean/atmosphere materials.

**`CelestialBody.cs` is NOT forbidden** — it has gameplay accessors (`Position`, `Rigidbody`, `velocity`, `bodyName`) + the `ApplySavedState` save hook. The forbidden part is the *generation/shading* code, not runtime physics state.

**Atmosphere-vanishes-on-warm-reload trap (fixed, don't re-break):** the blue atmosphere disappeared after returning from the backrooms — or any gameplay-scene reload within one process run. `AtmosphereSettings` (a persistent ScriptableObject) gates `SetProperties()` — which applies all scattering uniforms + the baked `_BakedOpticalDepth` texture — behind a non-serialized `settingsUpToDate` flag that's set `true` on first load and never reset at runtime. On a warm reload, `PlanetEffects` builds fresh atmosphere materials but `SetProperties` is skipped, so they render nothing. (A main-menu round-trip masked it: `LoadScene`'s `UnloadUnusedAssets` reclaimed the asset, resetting the flag.) Fix lives **outside** the forbidden zone: `Assets/3 - Scripts/Camera/AtmosphereReloadFix.cs` resets the flag on every non-MainMenu sceneLoaded via reflection. If atmosphere/ocean post-process ever vanishes after a scene transition again, suspect a persistent "baked/up-to-date" flag not being reset — not the camera/depth/generators.

### 3. Save system apply-order is fragile
`SaveCollector.Apply(data)` runs a strict, documented ~17-step order (inline in the file). Bodies → tutorial → NPCs → earlyGame → singletons → ship → player → buildings → loose parts → enemies → held item → touch-ups. Breaking the order causes regressions. **Always capture from `rb.position`/`rb.rotation`, never `transform.position`** (transform lags physics; saving it respawns objects inside colliders). Body-relative positions survive orbital motion. Full schema + "adding a new system to the save" recipe: see the audit (§5) and the inline docs in `SaveCollector.cs`.

---

## Project facts

- **Unity 2022.3, Built-in Render Pipeline (NOT URP).** URP-authored asset packs render magenta — convert their materials to Standard; never install URP.
- **Scenes:** active gameplay = `Assets/1.6.7.7.7.unity`; launcher = `Assets/MainMenu.unity`; both in build settings. Cinematics (`Cutscene`/`Flashback`/`Flashback1` under `Assets/4 - Scenes/`) are disabled in build settings but **referenced by code via `SceneManager.LoadScene` — do not delete or rename.** `1.8.unity` is WIP.
- **No CLI build/test** — all iteration is in the Editor; scripts compile on save, check the Console. Built exe is `Solar System 2.exe` (not used for dev).
- **Save files:** `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\*.json`. One bounded `autosave` slot.
- **No `.asmdef` files** — everything is `Assembly-CSharp`; moving scripts across folders never breaks compilation.
- **No `Resources.Load` in user code** except the TMP font and `Resources/HotbarIcons/` + `Resources/Flares/`.
- **Phone AI is now preset dialogue, NOT an LLM.** Player-facing AI is the deterministic preset branching-dialogue system (`Assets/3 - Scripts/Story/` + `StreamingAssets/Story/conv_*.json`) plus the **HAL** companion (`AI/HALCommentator`, `AI/IntentRouter`) — templated lines + a hand-written intent router. `LLMService.cs` still compiles and is still seeded, but `BeginPreload()` early-returns and **no `.gguf` model loads**; the 3.96 GB `StreamingAssets/LlamaLib-v2.0.5/` bundle is now inert dead weight (gitignored; safe to delete locally). See audit §17. Edit the `conv_*.json` / `hinttracks.json` / `objectives.json` files to change what the AI says — no recompile.
- **Big re-importable third-party packs are gitignored** (`Backrooms/`, `Poolrooms_Lvl37/`, LlamaLib, `*.gguf`). Re-import locally; don't commit them.
- **Commit hygiene:** new files need `git add` — `git commit -a` skips untracked files (this repo accumulated a backlog of never-added feature files from exactly that). Add new `.cs` **and** its `.meta`.

---

## Coding conventions (match these; each fixes a real bug class)

- **Append serialized fields at the END of a MonoBehaviour**, never mid-class — reordering corrupts existing scene/prefab serialization.
- **Singleton:** `Instance` guard in `Awake` (`if (Instance != null && Instance != this) { Destroy(gameObject); return; }`), clear in `OnDestroy`.
- **Never call `FindObjectOfType` / `FindObjectsOfType` / `Camera.main` / `GameObject.Find` in `Update`/`LateUpdate`/`FixedUpdate`.** Cache once, lazy-refind only if null. For "may never appear" targets, throttle retries (see `LightLookAt.cs`).
- **Iterate live instances via a static `AllInstances` list** maintained in `OnEnable`/`OnDisable` (e.g. `EnemyController.ActiveEnemies`, `SpawnedTree.AllTrees`). For celestial bodies use `NBodySimulation.Bodies` (null-safe — returns `Array.Empty` off the solar-system scene; never deref `Instance.bodies` raw).
- **Typewriter dialogue:** use `DialogueTextStyling.RevealCharsTMP/Legacy` (zero-alloc via `maxVisibleCharacters`). Never `text += c` in a loop (O(n²)).
- **`CompareTag(...)`**, never `gameObject.tag == "..."`.
- **Rigidbodies:** write with `rb.MovePosition`/`rb.MoveRotation` in `FixedUpdate`; never assign `transform.position` on a body. Read with `rb.position`.
- **Per-frame UI strings:** gate `text.text = $"..."` behind change-detection.
- **Runtime-spawned physics objects must call `EndlessManager.RegisterPhysicsObject(transform)`** or floating origin desyncs them.
- **NPC bone manipulation must run in `LateUpdate`** (the Animator overwrites `Update`/`OnAnimatorIK`). Don't animate the right arm to "reach" held items — that experiment was ripped out; items float at their hold position.
- **Transparent queue gotcha:** materials must have render queue ≤ 2500 to be hidden behind atmosphere/ocean (`[ImageEffectOpaque]`). See `sFuture Modules Pro/Materials/Glass_EarlyQueue.shader`.

---

## Common extension recipes

- **New equippable** (canonical example: `PistolController`): mirror its shape (`IsEquipped`/`IsUnlocked`/`ForceEquipX`/`ForceUnequipX`/`Unlock`, mutual-exclusion vs the other controllers) → attach to Player → add `ItemId` enum value + one row in `Hotbar.BuildRegistry()` → add `EquipmentSave` fields + capture/apply in `SaveCollector` → wire the granting NPC to call `Unlock()`. The Hotbar is table-driven — **do not add parallel switch cases.**
- **New auto-singleton:** mirror `SpaceDustInventory.cs` exactly (`RuntimeInitializeOnLoadMethod(AfterSceneLoad)` + MainMenu early-return + `Instance` pattern), and add it to `EnsureGameplaySingletons` (trap #1).
- **New save state:** schema in `SaveData.cs` (only `JsonUtility` types — no dicts/polymorphism) → `CaptureFoo`/`ApplyFoo` in `SaveCollector` at the right order point → **reset it in `NewGameReset.Apply()`** if it lives in a `DontDestroyOnLoad` singleton or `static` (New Game runs no `Apply`, so persistent state leaks across the main menu otherwise).
- **New placed building:** instance must be named `<prefab>_Placed` and parented to a `CelestialBody` — that exact suffix is how the save system finds/restores it.
- **New camera effect:** `InputSettings.fx*` flag + PlayerPrefs lines → `TabbedPauseMenu` CAMERA tab `ToggleDef` → a module that polls the flag → child-spawn block in `CameraEffectsManager.AttachModules`.

---

## Layout notes

- User code: `Assets/3 - Scripts/` by feature (Building, Camera, Combat, Concert, Cutscenes, Editor, Effects, Fishing, Map, NPC_Dialogue, Physics, Pickups, Player, Portals, SaveSystem, Ship, Survival, Tutorial, UI, Vendor, World, AI, Audio).
- Foundational layer: `Assets/3 - Scripts/Scripts/` — **do not reorganize.** Core gameplay (`PlayerController`, `Ship`, `InputSettings` are under `Game/Controllers/`; `NBodySimulation`, `EndlessManager`, `GravityObject`, `Universe`, `CelestialBody`, `GameSetUp` under `Game/`) plus the forbidden `Celestial/` + `Post Processing/Planet Effects/` (trap #2) and shared `Script Utilities/`.
- Other top-level `Assets/` paths and third-party packs have **sacred internal paths** (GUID-referenced) — moving an asset is fine only if its `.meta` moves with it.
- Active-scene hierarchy groups siblings under empty `--- Section ---` objects (`--- Managers ---`, `--- Celestial ---`, `--- Player & Ship ---`, etc.). Auto-singletons are `DontDestroyOnLoad` and live outside these at runtime. (Note: some section names in the audit didn't grep cleanly — verify in the Editor before relying on an exact organizer name.)

---

## Working notes

- Match existing patterns rather than inventing new ones; guard scene-object references with null checks rather than redesigning.
- `Universe.cheatsEnabled` toggles dev shortcuts (`CheatCodes.cs`); **F9** toggles `EarlyGameProgress.ORG_Reveal` (gates the AI's story-aware knowledge file; disabled in builds by default).
- When unsure whether to save state: would the player notice it resetting on reload? Gameplay progress → save; UI-only state → usually don't.
- Auto-singletons, the full NPC roster, the save schema, vendor/economy/combat/concert details, and the scrap list all live in `docs/CURRENT_STATE_AUDIT.md`. Go there before asking "does X exist?"
