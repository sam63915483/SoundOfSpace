# Blood Combat VFX — Design

**Date:** 2026-06-02
**Status:** Approved (design); implementation pending
**Asset:** Piloto Studio — Blood VFX Essentials (`Assets/Piloto Studio/`)

## Goal

Add two combat-feedback effects using the newly-imported Blood VFX Essentials pack:

1. **Spray on hit** — when a pistol bullet hits an enemy, a blood burst sprays out
   of the hit point, back toward the shooter.
2. **Pool on death** — when an enemy dies, a blood pool appears on the ground where
   it fell, lingers, then fades out.

Feel target: **arcade-juicy** (punchy, readable, not cartoonish, not gore-maxed).

## Asset compatibility (verified — no conversion needed)

The pack is genuinely multi-pipeline. Confirmed, not assumed:

- All blood materials reference one shader: `UberFXSG.shadergraph`
  (GUID `7f11fe28133f1a74594785107b98f2fe`).
- That Shader Graph has all three pipeline targets, and its `m_ActiveTargets`
  includes the **Built-in target active** (`UnityEditor.Rendering.BuiltIn.ShaderGraph.BuiltInTarget`).
  Materials carry the `_BUILTIN_*` property block that target generates.
- Project has `com.unity.shadergraph` 14.0.12 installed (`Packages/manifest.json`) —
  the dependency that compiles the Built-in subshader.
- Live editor check: `hasCompilationErrors: false`; zero shader errors on the
  Piloto/blood shaders in the console.

**Conclusion:** materials render correctly in Built-in RP as-is. No conversion.
The graphs are Unlit, so blood ignores scene lighting — desired for VFX.

Only two prefab families are used (the pack also ships unrelated "blood magic"
spells, which we ignore):
- **Spray:** `Blood VFX Essentials/Blood Splashes/` or `Bloody Fountains/` (non-looping bursts, `playOnAwake: 1`).
- **Pool:** `Blood VFX Essentials/Blood Splats/Sticky_Splat_*` (has a persistent looping layer — reads as a ground pool).

## Architecture

A central scene-placed `BloodFX` manager owns all prefab references + tuning and
exposes spawn methods. The two combat scripts each get a single call line. This
mirrors the existing `ResourceManager` pattern (scene-placed, `static Instance`).

```
PistolController.TriggerShot ──► BloodFX.Instance?.SpawnSpray(point, normal, shotDir)
EnemyController.BeginDeath   ──► BloodFX.Instance?.SpawnPool(groundPoint, up, planet)
                                      │
                                      ├─ instantiate spray/pool prefab
                                      ├─ orient + parent under planet
                                      ├─ disable colliders on FX
                                      └─ pool: attach BloodPool fader
```

Absent manager → `?.` makes every call a silent no-op (graceful).

### Components

**`BloodFX.cs`** (new, `Assets/3 - Scripts/Combat/`)
- Scene-placed singleton: `static Instance` set in `Awake` (guard if already set),
  cleared in `OnDestroy`.
- Serialized config (all tunable in inspector):
  - `GameObject sprayPrefab`
  - `GameObject poolPrefab`
  - `float sprayScale` / `float sprayLifetime` (seconds before auto-destroy; ~3)
  - `float poolScale`
  - `float poolLingerSeconds` (~20) — full-opacity hold before fade starts
  - `float poolFadeSeconds` (~3) — fade duration before destroy
- `SpawnSpray(Vector3 point, Vector3 normal, Vector3 shotDir)`:
  - Instantiate `sprayPrefab` at `point`.
  - Rotation: `Quaternion.LookRotation(normal)` so the emission cone points back
    along the surface normal (toward the shooter). `shotDir` available as a
    fallback if `normal` is degenerate.
  - Parent under the planet the hit object belongs to, so floating-origin shifts
    don't teleport the FX. (Resolve via `GetComponentInParent<CelestialBody>()` on
    the hit collider; if none, leave unparented — short-lived enough to be safe.)
  - Disable all colliders on the spawned FX (mirror `PistolController` tracer).
  - `Destroy(instance, sprayLifetime)`.
- `SpawnPool(Vector3 groundPoint, Vector3 up, Transform planet)`:
  - Instantiate `poolPrefab` at `groundPoint`, oriented flat to the surface
    (`Quaternion.FromToRotation(Vector3.up, up)` or `LookRotation` with `up`).
  - Parent under `planet`.
  - Disable colliders.
  - Attach `BloodPool` and init it with `poolLingerSeconds` + `poolFadeSeconds`.

**`BloodPool.cs`** (new, `Assets/3 - Scripts/Combat/`)
- Caches all child `ParticleSystem`s in `Awake`/`Init`.
- After `lingerSeconds`, fades over `fadeSeconds` by lerping each system's
  `main.startColor` alpha → 0. The particle system applies alpha as a vertex-color
  multiply, which the Piloto material honors — so this is shader-agnostic and
  doesn't depend on a specific material property name.
- On fade complete, `Destroy(gameObject)`.
- This bound on lifetime is what keeps a planet full of kills from accumulating
  unbounded particle cost.

### Edits to existing scripts (additive, one line each)

**`PistolController.TriggerShot`** (`Assets/3 - Scripts/Pickups/PistolController.cs`, ~line 339)
After `damageable.TakeDamage(damagePerShot);`, inside the `if (damageable != null)` block:
```csharp
BloodFX.Instance?.SpawnSpray(hit.point, hit.normal, forward);
```
Fires for any living `IDamageable` hit — enemies and alien NPCs both bleed, which
matches "shoot an enemy" and keeps the path uniform.

**`EnemyController.BeginDeath`** (`Assets/3 - Scripts/Combat/EnemyController.cs`, ~line 853)
After `up` is computed:
```csharp
Vector3 footPoint = rb.position - up * _scaledGroundedOffset;
BloodFX.Instance?.SpawnPool(footPoint, up, parentPlanet != null ? parentPlanet.transform : null);
```
`BeginDeath` is the single death entry point for every enemy, so every kill pools
regardless of what dealt the killing blow.

### Scene setup (via Unity MCP)

- Create GameObject `BloodFX` under `--- Managers ---` in `1.6.7.7.7.unity`.
- Add the `BloodFX` component.
- Assign default prefabs: a mid-size splash from `Blood Splashes/` for `sprayPrefab`,
  a `Sticky_Splat_*` for `poolPrefab`. Tune scales for arcade-juicy feel.
- Save the scene.

## Non-goals / explicit scope boundaries

- **No save-system changes.** Blood is ephemeral VFX (sprays ~3s, pools fade ~23s).
  Nothing persists across save/load. No `SaveData` / `SaveCollector` / `NewGameReset` work.
- **No `EnsureGameplaySingletons` seeding** — `BloodFX` is scene-placed, so it
  exists on both editor-play and build-load. The MainMenu-singleton trap does not apply.
- **Spray is pistol-only** for now. The `SpawnSpray` helper is generic, so adding
  melee (axe) or bobber blood later is a single call at the relevant hit site.
- **No gore/intensity settings toggle.** Out of scope; can hook into the camera-FX
  toggle system later if wanted.
- **DO NOT TOUCH** the forbidden atmosphere/celestial/shader zone (CLAUDE.md trap #2).
  This feature lives entirely in `Assets/3 - Scripts/Combat/` + `Pickups/`.

## Risks & mitigations

- **Floating-origin teleport of FX:** mitigated by parenting spray/pool under the
  planet `CelestialBody` transform (same idea as the tracer parenting to camera).
- **Particle accumulation:** mitigated by `sprayLifetime` auto-destroy + `BloodPool`
  linger-then-fade-then-destroy.
- **Fade mechanism fragility across the Piloto material:** mitigated by fading via
  particle `startColor` alpha (vertex multiply) rather than a named material property.
- **Pool orientation on a curved planet:** pool is oriented to local planet-up at
  the death point, so it lies flat on the surface tangent.

## Files

New:
- `Assets/3 - Scripts/Combat/BloodFX.cs`
- `Assets/3 - Scripts/Combat/BloodPool.cs`

Edited:
- `Assets/3 - Scripts/Pickups/PistolController.cs` (1 line)
- `Assets/3 - Scripts/Combat/EnemyController.cs` (~2 lines)

Scene:
- `Assets/1.6.7.7.7.unity` (new `BloodFX` manager object)
