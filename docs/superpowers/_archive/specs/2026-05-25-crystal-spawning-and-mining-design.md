# Crystal spawning & mining — slice 1 (axe-mineable)

**Date:** 2026-05-25
**Status:** Approved
**Implements:** A new procedurally-streamed `crystal` resource that spawns on every celestial body except the Sun, scales 1×–3×, drops more crystal the bigger it is, and is mineable with the existing axe. A later slice will swap the axe trigger for a dedicated pickaxe.

## Goals

- Treat the crystal as a third "ambient world resource" alongside trees (wood) and mushrooms (trip).
- Reuse the proven mushroom streaming pattern (per-cell deterministic seed, radius-gated spawn/despawn, body-parented, consumed-cell tracking).
- Reuse the proven tree mining pattern (HP-based, multi-swing, break animation, +N popup, inventory deposit).
- Round-trip through the save system from the start, matching wood / space-dust.

## Non-goals

- No pickaxe yet. Axe is the temporary mining tool; switching it later is one diff to `AxeController.DetectSwingHit` plus the equivalent block in `PickaxeController.cs`.
- No HUD counter for crystals yet (deferred to the pickaxe slice).
- No vendor / use-of-crystal yet (collect-only).

## Architecture

Five units:

| Unit | File | Responsibility |
|---|---|---|
| `CrystalSpawner` | `Assets/3 - Scripts/World/CrystalSpawner.cs` | Per-cell deterministic streamed spawn of `crystal_17_2` instances on every non-Sun body within `spawnRadius` of the player, capped by `inputSettings.maxCrystals` (or fallback `maxCrystals`). |
| `SpawnedCrystal` | `Assets/3 - Scripts/World/SpawnedCrystal.cs` | Runtime component attached to each spawned crystal. HP / scale / yield, static `AllCrystals` list, `TakeDamage(int)`, shake-on-hit, break + shrink-and-fade on death, awards crystals to `CrystalInventory`, fires `MarkCellMined` on the spawner. |
| `CrystalInventory` | `Assets/3 - Scripts/Player/CrystalInventory.cs` | Auto-singleton mirroring `WoodInventory`. `Count`, `OnChanged`, `Add`/`Spend`/`SetCount`. Seeded in `MainMenuController.EnsureGameplaySingletons` per the MainMenu-singleton-trap rule. |
| `CrystalPopup` | `Assets/3 - Scripts/World/CrystalPopup.cs` | "+N" floating text on break, mirrors `WoodPopup`. |
| Save support | `SaveData.cs` + `SaveCollector.cs` | `CrystalSave { int count }`. Capture / apply in the singleton-state block alongside wood / fish / equipment. |

Plus two touches:

- `AxeController.DetectSwingHit` — append a parallel block for `SpawnedCrystal` after the existing tree block (raycast then cone fallback iterating `SpawnedCrystal.AllCrystals`). Add `ApplyHit(SpawnedCrystal)` overload.
- `Assets/1.6.7.7.7.unity` — add `CrystalSpawner` GameObject under `--- Managers ---`, wire the `crystal_17_2` prefab into its `crystalPrefab` field.

## Spawner config

Initial inspector values (tunable later):

| Field | Value | Notes |
|---|---|---|
| `excludeBodyNames` | `["Sun"]` | Same exclusion list pattern as MushroomSpawner. |
| `crystalPrefab` | `crystal_17_2.prefab` | Single prefab — unlike mushrooms which pick one of many per cell. |
| `spawnRadius` | 300 m | Match mushrooms/aliens for consistency. |
| `maxCrystals` | 12 | Lower than mushrooms (20) since they're rarer. Spawner-local fallback only — no `InputSettings` field or pause-menu slider in this slice. The wiring to `inputSettings.maxCrystals` happens whenever the pause-menu slider is added (deferred, not blocking the pickaxe). |
| `cellSize` | 80 m | Larger than mushrooms (50) so crystals spread out further. |
| `seed` | 11357 | New, distinct from mushroom 24680 / tree / alien seeds. |
| `crystalSpawnChance` | 0.18 | Rare end of the agreed 10-25% window. |
| `maxSurfaceAngle` | 35° | Match mushrooms; reject cliffs. |
| `minScale` / `maxScale` | 1.0 / 3.0 | Per design. |
| `groundOffset`, `groundEmbedPerScale` | 0 / 0.04 | Same fields as MushroomSpawner; tune in editor after first play. |

The hashing / cell-encoding / FaceUVToDirection / consumed-cell tracking are unchanged from MushroomSpawner — copied verbatim with `mushroomPrefabs[]` collapsed to a single `crystalPrefab`.

## SpawnedCrystal behavior

```csharp
void Init(CrystalSpawner s, int slot, long id, float scale)
{
    spawner = s; bodySlot = slot; cellId = id;
    _scaleStep = Mathf.RoundToInt(scale);            // 1, 2, or 3
    hp        = Random.Range(2, 5) * _scaleStep;     // 2-4 for 1×, 4-8 for 2×, 6-12 for 3×
    yield     = _scaleStep * 2;                      // 2 / 4 / 6
}

public void TakeDamage(int amount)
{
    if (dead) return;
    hp -= amount;
    if (hp <= 0) Break();
    else PlayShake();      // same shake routine shape as SpawnedTree
}

void Break()
{
    dead = true;
    SetCollidersEnabled(false);
    CrystalInventory.Instance?.Add(yield);
    CrystalPopup.Spawn(transform.position + transform.up * 1.5f, yield);
    StartCoroutine(ShrinkAndFade());   // 0.5s scale → 0, then MarkCellMined + Destroy
    // Optional break sound — wired through the spawner's clip slot if assigned, same as treeBreakClip.
}
```

`AllCrystals` static list maintained via `OnEnable` / `OnDisable` (mirrors `SpawnedTree.AllTrees` / `EnemyController.ActiveEnemies` per CLAUDE.md's "static AllInstances list" idiom).

The shrink animation replaces the tree's fall-and-shrink — crystals don't topple, they shatter and vanish. Simpler routine: scale lerps from current → zero over 0.4s, then `Mine()` (calls `spawner.MarkCellMined`) and `Destroy`.

## AxeController hook

Two changes in `DetectSwingHit`:

1. After the raycast check for `SpawnedTree`, an identical block looking up `SpawnedCrystal` via `GetComponentInParent<SpawnedCrystal>()` on the same hit. If found and not dead → `ApplyHit(crystal)` and return.
2. In the cone fallback after the tree loop, append an analogous loop over `SpawnedCrystal.AllCrystals`. Track best target alongside `bestTree` / `bestDamageable` in a new `bestCrystal` local.
3. New overload `void ApplyHit(SpawnedCrystal c)` calling `c.TakeDamage(damagePerSwing)` and the hit sound — exact parallel of the existing tree overload.

When the pickaxe lands later, this block moves to `PickaxeController.cs` (a copy of `AxeController.cs` minus combat damage). The axe block is removed at that point.

## Save support

- `SaveData.cs`: add `public CrystalSave crystal = new CrystalSave();` and `[Serializable] public class CrystalSave { public int count; }`.
- `SaveCollector.cs`:
  ```csharp
  static void CaptureCrystals(CrystalSave s)
      => s.count = CrystalInventory.Instance != null ? CrystalInventory.Instance.Count : 0;

  static void ApplyCrystals(CrystalSave s)
  {
      if (CrystalInventory.Instance != null) CrystalInventory.Instance.SetCount(s.count);
  }
  ```
- Wire into `Capture(name)` and `Apply(data)` next to `ApplyWood` (singleton-state block, step 6 in the apply order documented in CLAUDE.md).

The schema bump is additive — old saves without a `crystal` field will deserialize `crystal.count = 0` (default).

## Singletons & MainMenu trap

`CrystalInventory` uses the same `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` pattern with the `MainMenu` early-return, AND is added to `MainMenuController.EnsureGameplaySingletons()`. This is the critical step the CLAUDE.md "MainMenu singleton trap" warning calls out: without seeding, the singleton never auto-creates in builds (since builds start in MainMenu, where the auto-create early-returns).

## Verification (Unity MCP)

After implementation:
1. `check_compile_errors` — no new compile errors.
2. `play_game` — enter play mode on `1.6.7.7.7.unity`.
3. Observe at least a few crystals spawn on Humble Abode within the spawn radius (`get_unity_logs` for any spawner warnings).
4. Equip axe, left-click a crystal, watch it shake / break, confirm `[CrystalInventory] +N` log line.
5. `stop_game`.

## What this slice does NOT do

- No pickaxe.
- No HUD counter.
- No vendor / recipe / use of crystal beyond accumulation.
- No save-format version bump — purely additive.
- No effect on TreeSpawner, MushroomSpawner, AlienNPCSpawner.
