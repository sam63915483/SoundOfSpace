# Audit: Foundational Scripts Layer (non-forbidden)

Read-only audit of `Assets/3 - Scripts/Scripts/` **excluding** the forbidden zones
(all of `Celestial/`, `Post Processing/Planet Effects/`, `Atmosphere.cs`,
`CustomImageEffect.cs`, and any `.shader`/`.compute`/`.hlsl`). Game-core files
(`NBodySimulation`, `EndlessManager`, `GravityObject`, `Universe`, `GameSetUp`,
`CelestialBody`, `SolarSystemSpawner`, `LODHandler`, `MeshBaker`, `StarDome`, and
`Game/Controllers/`) are left to the agents covering that area.

Focus: `Script Utilities/`, plus shared/utility files under `Game/Utility`,
`Game/UI`, `Game/Interactions`, `Game/Lighting`, and the non-forbidden
`Post Processing/` (Bloom, FXAA, OceanMaskRenderer).

## Summary

The `Script Utilities/` core (ComputeHelper, MathUtility, PRNG, ShaderHelper,
TextureHelper) is Sebastian Lague's Solar-System utility library and is largely
clean and stateless — no singletons, no per-frame `Find` calls. Two real defects
live there: a dormant Z-axis dispatch bug and a dead+broken `PackFloats`.

The shared gameplay/utility files are in good shape. The auto-singletons that
matter for the build trap are handled correctly: `PixelLightLimitFix` **is**
seeded in `MainMenuController.EnsureGameplaySingletons` (trap #1 satisfied), and
`LightingDebugToolbox` is intentionally *not* seeded so the debug overlay never
ships. The most impactful live finding is a per-frame `new Material` allocation +
leak in `OceanMaskRenderer`. The rest are low-severity null-guard gaps, stale
comments, and dead code.

Counts: **7 bugs** (0 high / 3 medium / 4 low), **5 redundancy/dead-code items**,
**5 performance notes**.

## Bugs (severity, file:line, description, fix)

### B1 — [Medium, latent] `Script Utilities/ComputeHelper.cs:19` — wrong axis in Z thread-group count
```csharp
int numGroupsZ = Mathf.CeilToInt (numIterationsZ / (float) threadGroupSizes.y); // .y — should be .z
```
`Run()` divides the Z iteration count by `threadGroupSizes.y` instead of `.z`.
For any 3D dispatch with a Z group size different from Y this under- or
over-dispatches thread groups. **Currently dormant** — every caller in the repo
uses 1D or 2D dispatch only (verified: `CelestialBodyGenerator`,
`AtmosphereSettings`, `CelestialBodyShading`, `CelestialBodyShape`,
`SpotsTexture`), so `numIterationsZ` defaults to 1 and the miscomputation is
harmless today. It becomes a real bug the moment someone dispatches a 3D kernel.
**Fix:** `threadGroupSizes.z`. (Note: callers are in the forbidden Celestial zone;
the fix is in ComputeHelper itself, which is non-forbidden.)

### B2 — [Low, dead + broken] `Script Utilities/ComputeHelper.cs:129-135` — `PackFloats` returns the wrong array
```csharp
public static float[] PackFloats (params float[] values) {
    float[] packed = new float[values.Length * 4];
    for (int i = 0; i < values.Length; i++) { packed[i * 4] = values[i]; }
    return values;   // returns the input, not `packed`
}
```
Allocates and fills `packed`, then returns the original `values`. The function is
broken *and* has **zero callers** in the codebase. **Fix:** return `packed`, or
delete the method.

### B3 — [Medium, perf + leak] `Post Processing/OceanMaskRenderer.cs:53` — per-frame material allocation, never released
```csharp
var mat = new Material (oceanMaskShader);   // inside RenderOceanMask, runs every post-process frame
```
`RenderOceanMask` is subscribed to `CustomPostProcessing.onPostProcessingBegin`
and is confirmed live (`CustomPostProcessing.cs:95` reads its `oceanMaskTexture`).
A fresh `Material` is created every frame and never `Destroy`ed → continuous GC
allocation plus a leaked material per frame. **Fix:** create the material once
(lazy-init a cached field) and reuse it; destroy it in `OnDestroy`. *Caution: this
file is shading-adjacent though not in the forbidden zone — flag only, verify in
Editor before changing.*

### B4 — [Low, null-deref] `Post Processing/OceanMaskRenderer.cs:29-30` — unguarded `FindObjectOfType`
```csharp
FindObjectOfType<CustomPostProcessing> ().onPostProcessingBegin -= RenderOceanMask;
FindObjectOfType<CustomPostProcessing> ().onPostProcessingBegin += RenderOceanMask;
```
Dereferenced twice with no null check; throws `NullReferenceException` if no
`CustomPostProcessing` is present. Because `Update()`→`Init()` runs every frame in
edit mode (`[ExecuteInEditMode]`) and `oceanBodies==null`, this can spam NREs in
the Editor when the camera effect isn't in the scene. **Fix:** cache the result
in a local, null-check before subscribing.

### B5 — [Low, null-deref] `Game/UI/SettingsMenu.cs:78-79` — inconsistent null-guarding in `OpenMenu`
```csharp
mouseSensitivity.text = inputSettings.mouseSensitivity + "";   // unguarded
mouseSmoothingSlider.value = inputSettings.mouseSmoothing;      // unguarded
// ...every other slider below is wrapped in `if (x != null)`
```
`mouseSensitivity` and `mouseSmoothingSlider` are used without the null checks
that every sibling slider gets. NRE if either serialized ref is left unassigned.
Low risk (they're required prefab wiring) but inconsistent. **Fix:** guard them
the same way, or document that they're mandatory.

### B6 — [Low, robustness] `Game/Lighting/SunShadowCaster.cs:9` — camera never re-acquired
```csharp
void Start () { track = Camera.main?.transform; }
void LateUpdate () { if (track) transform.LookAt (track.position); }
```
`track` is captured once in `Start`. If `Camera.main` is null at `Start` (camera
spawned later) the sun shadow light never tracks for the rest of the session; if
the main camera is later swapped out/destroyed the reference goes stale. **Fix:**
lazy-refind when `track == null` in `LateUpdate` (throttled), per the CLAUDE.md
"cache once, lazy-refind only if null" convention.

### B7 — [Low, stale comment] `Game/Lighting/PixelLightLimitFix.cs:22-26 vs 43` — doc contradicts constant
The prose comment says "bump the limit to 16," but `TargetPixelLightCount = 64`
(line 43, correctly explained in the 36-42 comment). Behavior is correct — only
the earlier comment is stale and contradicts the later one. **Fix:** update the
line 22-26 comment to 64.

## Redundancies / Dead Code

- **`ComputeHelper.PackFloats`** (`Script Utilities/ComputeHelper.cs:129`) — dead
  (0 callers) and broken (see B2).
- **`GameUI` singleton plumbing** (`Game/UI/GameUI.cs:10, 45-50`) — the `instance`
  field and `Instance` property (which does a `FindObjectOfType<GameUI>()`) are
  never referenced; all the static methods delegate straight to
  `InteractPromptUI`. `GameUI` is now just a static facade plus a one-shot
  legacy-text hider. The `Instance` accessor is dead.
- **`BloomEffect._shader`** (`Post Processing/Bloom/BloomEffect.cs:86-105`) —
  `OnEnable` does `_shader = null;` then `var shader = _shader ? _shader :
  Shader.Find("Hidden/Kino/Bloom");`. The serialized `_shader` field is always
  nulled first, so the ternary always takes the `Shader.Find` branch. The field
  and the ternary are effectively dead; behavior is "always Shader.Find."
- **`LockOnUI` normals array** (`Game/UI/LockOnUI.cs:74`) — `var norms = new
  Vector3[numIncrements * 2];` is allocated every `DrawLockOnUI` call but never
  assigned to the mesh (`mesh.normals` is never set). Pure wasted allocation.
- **`Game/Test/*` + `Game/Utility/ChanceTest.cs` + assorted `Game/Debug/*`** —
  `CamTest`, `ColourTest`, `CustomDebug`, `FPSTest`, `LODTest`, `NormalRotTest`,
  `RaySphereTest`, `ShaderMatrix`, `SunTest`, `ChanceTest`, `RandomTest` read as
  developer scratch/test scripts (several are `[ExecuteInEditMode]` with
  `Input.GetKeyDown` triggers). Not exhaustively audited; candidates for a
  cleanup pass but harmless if unused in scenes.

## Performance / Optimization

- **`LockOnUI.DrawLockOnUI`** (`Game/UI/LockOnUI.cs:73-75`) — allocates three
  arrays (`verts`, `norms`, `tris`) on every call, and this is invoked per-frame
  for each aimed/locked body. `numSegments` is effectively constant, so the arrays
  could be cached and only rebuilt on size change. The `norms` allocation is
  entirely wasted (see dead-code note). Reducing this cuts steady-state GC while
  targeting bodies.
- **`OceanMaskRenderer` per-frame `new Material`** — see B3 (biggest live perf
  item found).
- **`OceanMaskRenderer.Update`→`Init`** (`Post Processing/OceanMaskRenderer.cs:15-33`)
  — in edit mode runs `FindObjectsOfType<CelestialBodyGenerator>()` plus two
  `FindObjectOfType<CustomPostProcessing>()` every frame. Runtime is fine (guarded
  once `oceanBodies` is populated); edit-mode only.
- **`HatchButton.BuildInteractMessage`** (`Game/Interactions/HatchButton.cs:13`)
  — calls `GetComponentInParent<Ship>()` every frame the player is in the zone,
  because `Interactable.Update` re-asserts the prompt each frame (which invokes
  `BuildInteractMessage`). Minor; the parent `Ship` could be cached in `Awake`.
  Same pattern is cheap for `SunlightControlButton` (static `LebronLight.Instance`).
- **`LightingDebugToolbox`** (`Game/Lighting/LightingDebugToolbox.cs`) — draws an
  `OnGUI` panel every frame and grabs F6-F12 while in Play mode, with no
  `Universe.cheatsEnabled` / `#if UNITY_EDITOR` gate. Harmless in shipping builds
  because it is **not** seeded in `EnsureGameplaySingletons` and
  `RuntimeInitializeOnLoadMethod` won't auto-create it in a build (trap #1), but
  it's always on in Editor Play. The per-toggle `FindObjectsOfType<Light>(true)`
  is fine for a debug tool.

## Notes & Uncertainties

- **Singleton hygiene is correct** where it matters: `PixelLightLimitFix` and
  `LightingDebugToolbox` both use the canonical `Instance` guard in `Awake` and
  clear it in `OnDestroy`. `PixelLightLimitFix` is correctly seeded in
  `MainMenuController.EnsureGameplaySingletons` (line ~615) — trap #1 satisfied,
  matching the "torches flickered for two days" cautionary tale in CLAUDE.md.
  `LightingDebugToolbox` is deliberately unseeded (debug tool, editor-only).
- `ComputeHelper`, `MathUtility`, `PRNG`, `ShaderHelper`, `TextureHelper` are the
  Sebastian Lague utility library. Aside from B1/B2 they're clean and side-effect
  free. `PRNG.Value()` can return a value *fractionally* above 1.0 by design
  (`maxExclusive = 1.0000000004…`), documented in-code — noted, not a bug.
- `MathUtility.PointOfLineLineIntersection` logs an error and returns
  `Vector2.zero` on parallel lines (line 49) rather than throwing — acceptable
  guard, callers are expected to pre-check with `LinesIntersect`.
- Observed but **out of primary scope** (UI layer, other agents): a full duplicate
  `EnsureGameplaySingletons_Legacy` still exists in `MainMenuController.cs`
  (~line 687+) alongside the live async version. Flagged for whoever owns that
  file — likely dead now that the async path is the default.
- I did not exhaustively read the `Game/Test/`, `Game/Debug/` (Debug Viewer /
  Triangle library is third-party), or `Bloom/Editor/` scripts — they are
  editor/test tooling, low gameplay risk. Bloom/FXAA effects are third-party
  (Keijiro / Catlike Coding) and otherwise clean.
