# Audit: Combat & Survival

Read-only audit of `Assets/3 - Scripts/Combat/` (15 files) and
`Assets/3 - Scripts/Survival/` (7 files). No files were modified.

## Summary

The subsystems are in good shape and, overall, follow the repo conventions well:

- **`CompareTag`** is used everywhere (no `gameObject.tag == "..."` anywhere in
  either folder).
- **Static `AllInstances`/`ActiveEnemies` lists** are correctly maintained in
  `OnEnable`/`OnDisable` (`EnemyController.s_active`,
  `AlienNPCDamageable.s_active`, `TorchAura.s_active`, `RagdollBoneRegistry`).
- **Floating-origin handling** is deliberate: live enemies + corpses are parented
  to their `CelestialBody` (so `EndlessManager.RegisterPhysicsObject` is correctly
  *not* needed), and ragdoll bones register with `RagdollBoneRegistry` for the
  origin-shift kinematic guard.
- **MainMenu-skipping auto-singletons** (`KillstreakManager`, `OxygenManager`,
  `OxygenHUD`, `VitalsHUD`, `WaterFillHUD`) are **all** mirrored in
  `MainMenuController.EnsureGameplaySingletonsAsync` — trap #1 is honored
  (verified `MainMenuController.cs:593-636`).
- Per-frame `FindObjectOfType` is generally avoided or lazy/null-guarded.

The findings below are mostly medium/low. The one worth acting on is the corpse
spawn-budget starvation (Bug 1).

## Bugs

### 1. [MEDIUM] Corpses consume the `MaxConcurrent` spawn budget for 30 s
`EnemySpawner.cs:45-47` prunes `activeEnemies` only when an entry is Unity-null:
```csharp
for (int i = activeEnemies.Count - 1; i >= 0; i--)
    if (activeEnemies[i] == null) activeEnemies.RemoveAt(i);
```
But a killed `EnemyController` is **not** destroyed at death — `BeginDeath`
(`EnemyController.cs:878`) keeps the GameObject alive as a ragdoll/script
container for `RagdollDuration = 30f` (`EnemyController.cs:187`), and only calls
`EnemySpawner.OnEnemyDestroyed` from `OnDestroy` (`EnemyController.cs:1129-1132`)
at the *final* `Destroy`. `BeginDeath` never notifies the spawner. Result: with
`maxConcurrent = 5`, killing enemies faster than the 30 s corpse lifetime starves
new spawns — dead bodies hold live spawn slots.
**Fix:** in `TrySpawn`, count only non-dying enemies against the cap
(`activeEnemies.Count(e => e != null && !e.IsDying)` — `IsDying` is already public
at `EnemyController.cs:145`), or remove from the spawner list in `BeginDeath`.

### 2. [LOW-MED] `SpitProjectile` leaks a Material (and `Shader.Find`s) on every spit
`SpitProjectile.cs:59-65` does, per projectile:
```csharp
var mat = new Material(Shader.Find("Standard"));
mat.color = ...; mr.material = mat;
```
A `new Material(...)` assigned to a renderer is **not** auto-cleaned on GameObject
destroy (only the implicit instance from the `.material` getter is), so each spit
leaks one material; `Shader.Find` is also a string lookup per shot. Spits fire on
a 5-10 s cadence per enemy, so it accumulates over a long tree-camp.
**Fix:** cache one `static Material` and reuse it (all spits share the same green).

### 3. [LOW] `WaterFillHUD` calls `FindObjectOfType` every frame while the bottle is absent
`WaterFillHUD.cs:85`:
```csharp
if (_bottle == null) _bottle = FindObjectOfType<WaterBottleController>(true);
```
Unthrottled per-frame `FindObjectOfType` for a "may-not-exist-yet" target — the
exact anti-pattern CLAUDE.md calls out ("throttle retries, see `LightLookAt`").
Compare `FallDamage.cs:107-113` and `OxygenManager.EnsureRefs` (`OxygenManager.cs:504-509`),
which both throttle to ~1-2/s. Cheap to align.
**Fix:** gate the re-find behind a ~1 s timer like `FallDamage`.

### 4. [LOW] Living enemies steer around (and count) dead corpses
`EnemyController.cs:506-518` (separation) iterates `s_active`, which still contains
`_dying` corpses (they stay enabled for 30 s, so `OnDisable` hasn't removed them).
Living enemies therefore treat corpses as steering obstacles. Minor pathing
artifact only. Same list is iterated by `TorchAura.Update` (`TorchAura.cs:39-45`),
but there `TakeDamage` early-returns on `_dying`, so no damage is wasted.
**Fix (optional):** `if (other == null || other == this || other.IsDying) continue;`

### 5. [LOW] Suit-suffocation death can't re-fire after God Mode
`OxygenManager.cs:404-408` latches `suitDepletedHandled = true` and calls
`KillPlayer()`, which routes through `ResourceManager.TakeDamage` — but
`ResourceManager.cs:163` early-returns under `GravityDebugUI.GodMode`. If the suit
hits 0 while God Mode is on, the kill is swallowed and `suitDepletedHandled` stays
latched; toggling God Mode off later (suit still 0, still not breathing) never
retries the kill until breathing resets the flag. Debug-only edge case.

### 6. [LOW/DOC] `FallDamage` ring-buffer comment miscalculates coverage
`FallDamage.cs:82-86`: "32 slots covers 0.32 s at the default 0.02 s timestep."
32 × 0.02 = **0.64 s**, not 0.32 s. Behavior is correct (buffer is deliberately
larger than the 0.25 s `impactWindow`); only the comment is wrong.

## Redundancies / Dead Code

- **`ResourceHUD.cs` is legacy/superseded.** `VitalsHUD.DisableLegacyResourceHUD`
  (`VitalsHUD.cs:326-359`) actively finds and disables it on every scene load. It
  still runs `Awake/Start/Update` until disabled. Notably its `PulseBar`
  (`ResourceHUD.cs:242-258`) writes `img.color` every frame with **no**
  change-detection (unlike `VitalsHUD.UpdateStat`, which guards with
  `Mathf.Approximately`) — moot only because the component is force-disabled.
  Candidate for removal once confirmed unreferenced by scenes.
- **`VitalsHUD` charging-row leftovers.** `BuildChargingRow` (`VitalsHUD.cs:534-561`)
  is never called (comment: "kept for reference"). Fields `_chargingRow`,
  `_chargingText`, `_chargingShown`, and `_solar` (assigned in `Start`/`OnSceneLoadedRefresh`
  but never read after the charging row was removed) and `_legacyHidden` (set,
  never read) are dead. `static Color LedColor` (`VitalsHUD.cs:35`) is unused (the
  LED uses `HelmetHudPalette.Accent`).
- **`VitalsHUD.LedColorDim`/`LedColor`** in `WaterFillHUD` *are* used
  (`LedPulseRoutine`); only the `VitalsHUD` copy is dead — noted to avoid confusion.

## Performance / Optimization

- **`AlienNPCDamageable.PlayOneShot2D` allocates a GameObject + AudioSource per hit**
  (`AlienNPCDamageable.cs:148-158`) — fires on every bullet/axe hit and every
  death. In a firefight this is a steady stream of GO allocations + `Destroy`
  churn. Consider a small pooled 2D one-shot source (or a single shared 2D source
  with `PlayOneShot`).
- **`SpitProjectile` material/shader per shot** — see Bug 2.
- **`WaterFillHUD` per-frame `FindObjectOfType`** — see Bug 3.
- **Per-hit allocations that are acceptable (low frequency, noted for completeness):**
  `BloodFX.FindNearestDescendant` (`BloodFX.cs:215-241`) does
  `GetComponentsInChildren<Transform>(true)` per hit; `EnemyRagdollBuilder` /
  `AlienRagdollBuilder` allocate dictionaries/lists at death. These run on
  hit/death events, not in hot loops — fine as-is.
- **`EnemyController.s_hitBuffer` is `RaycastHit[8]`** (`EnemyController.cs:197`),
  shared and reused (good, zero-alloc). Theoretical edge: if 8 closer non-ground
  colliders fill the buffer, `RaycastNonAlloc` could clip the real ground hit.
  Very unlikely given the layer mask; noted only for completeness.
- **Good patterns already in place:** `VitalsHUD.UpdateStat` and
  `WaterFillHUD.Update` gate string allocation behind whole-percent change
  detection; `EnemyController` caches `_sharedPlayer` statically across the whole
  fleet (`EnemyController.cs:296-301`) instead of per-enemy `FindObjectOfType`.

## Notes & Uncertainties

- **`EnemyController._scaledGroundedOffset`** is cached in `Awake`
  (`EnemyController.cs:240`) from `transform.localScale.y`, but the enemy is
  `SetParent`ed to the planet *after* `Instantiate` (`EnemySpawner.cs:154`) with
  `worldPositionStays: true`. If a planet's transform scale is ever ≠ 1, parenting
  rewrites the child `localScale` and the cached ground offset would be stale.
  Planets appear to be unit-scaled, so this is latent, not observed — flagging in
  case a scaled body is ever introduced.
- **Two independent enemy lists coexist by design:** `EnemyController.ActiveEnemies`
  (all enemies, drives torch/separation) vs `EnemySpawner.activeEnemies` (spawner's
  own instances, drives the cap + save). Bug 1 stems from the spawner list not
  dropping dying enemies; the global list is fine.
- **Death path is single-count safe:** `ResourceManager.DeathSequence`
  (`ResourceManager.cs:95-131`) sets `isDead = true` on its first synchronous line,
  so the `Update` guard at `ResourceManager.cs:87` can't double-increment
  `totalDeaths`.
- **`SpitProjectile` and both ragdoll builders** correctly avoid
  `RegisterPhysicsObject` by parenting to the planet (spit) or registering bones
  with `RagdollBoneRegistry` (ragdolls) — consistent with the codebase's
  floating-origin contract.
