# World-Spawn Immersion — Design

## Goal

Two changes to the random spawners (`TreeSpawner`, `MushroomSpawner`, `AlienNPCSpawner`):

1. **Kill the visible pop-in / pop-out.** Each spawned object scale-fades in over ~0.4 s instead of appearing at full size, and scale-fades out over ~0.3 s before being returned to its pool.
2. **Player-controlled view distance, 100–1000 m, single slider for all three.** New field on `InputSettings`, new row in the pause menu's settings panel. Each spawner reads the value live (same pattern the existing `maxTrees`/`maxMushrooms`/`maxAlienNPCs` sliders already use). To keep density natural as the radius grows, each spawner's cap is multiplied by `viewDistance / baselineRadius` (linear), floored at the slider-configured base.

## Non-goals

- No LOD swap, no impostor / billboard rendering. View-distance increase relies purely on bigger spawn radius + cap, not a second visual tier.
- No changes to the deterministic cell-grid math (cube-face UV + hash-based per-cell roll). Trees / mushrooms / aliens stay rooted to the same world positions they would have today.
- No save/load schema changes. View distance is a runtime preference (PlayerPrefs), not part of `SaveData`.
- The existing `maxTrees` / `maxMushrooms` / `maxAlienNPCs` sliders keep working — they're now the per-spawner "baseline density at 150 m / 300 m / 300 m" floor.

---

## Part 1 · `SpawnFade` helper

### Component

New file: `Assets/3 - Scripts/World/SpawnFade.cs`. A small MonoBehaviour attached to each spawned object that scale-fades it in on activation and scale-fades it out before despawn.

Why scale-fade (not alpha-fade): scale modification works with any prefab regardless of its shader / material setup. Alpha-fading prefabs that use opaque shaders would require runtime material edits or per-prefab material swaps. Scale-fade is universal, cheap, and reads as "sprouting" for trees/mushrooms and as a fast pop-in for aliens.

### Public API

```csharp
public class SpawnFade : MonoBehaviour
{
    public float fadeInDuration = 0.4f;
    public float fadeOutDuration = 0.3f;

    /// <summary>
    /// Resets the object to scale * startMultiplier and animates it up to its
    /// captured target scale over fadeInDuration. Called by the spawner right
    /// after it sets the object's final transform (position / rotation / scale).
    /// </summary>
    public void BeginFadeIn();

    /// <summary>
    /// Animates the object from its current scale down to scale * startMultiplier,
    /// then invokes onComplete. The spawner uses this to delay its existing
    /// pool-return logic until after the visual shrink.
    /// </summary>
    public void BeginFadeOut(System.Action onComplete);
}
```

`startMultiplier` is `0.05f` (constant). At that fraction the object is small enough to be visually negligible; we don't go to true zero because some shaders / colliders behave oddly at exact zero scale.

### Pool re-use behaviour

When the spawner reuses a pooled object, it calls `BeginFadeIn` again. The component re-captures the current `transform.localScale` as the new target (the spawner has just set the scale to the random value for this slot) and restarts the fade. The first call to `BeginFadeIn` after activation is idempotent if the coroutine is already running — it stops the prior coroutine first.

### Fade-out interaction with despawn

The spawner's current `DespawnInternal(entry, cellId)` immediately calls `tree.SetActive(false)` and pushes to the pool. After this change, despawn flows:

1. Spawner asks SpawnFade to begin fade-out, passing a callback.
2. SpawnFade animates scale down.
3. On completion, SpawnFade invokes the callback, which is the existing pool-return code (`SetActive(false)` + push).

Edge case — fade-out interrupted by destruction: if the GameObject is destroyed mid-fade (e.g., `MarkCellMined` triggers DespawnInternal during a fade-out already in flight, or a tree is chopped), the coroutine ends via `OnDisable` cleanup; the callback is invoked synchronously to keep the spawner's bookkeeping consistent.

---

## Part 2 · View-distance slider

### `InputSettings` field

Add to `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs`:

```csharp
const float defaultViewDistance = 350f;

[Range(100, 1000)] public float viewDistance = defaultViewDistance;
```

Save/load wiring follows the existing pattern (PlayerPrefs).

### Pause-menu slider

Add to `Assets/3 - Scripts/Scripts/Game/UI/SettingsMenu.cs`:

```csharp
public UnityEngine.UI.Slider viewDistanceSlider;
```

In `OnEnable`, wire an `onValueChanged.AddListener(OnViewDistanceChanged)`. New handler:

```csharp
void OnViewDistanceChanged (float value) {
    if (inputSettings != null) inputSettings.viewDistance = Mathf.Clamp(value, 100f, 1000f);
}
```

In the existing `RefreshFromSettings` / `ApplyToSettings` methods, mirror the same `SetValueWithoutNotify` and `inputSettings.viewDistance = slider.value` patterns used for the other sliders.

The actual `Slider` UI element needs to be created in the pause menu's settings panel scene/prefab and dragged into the `viewDistanceSlider` field. Slider range: min 100, max 1000, whole-numbers ON (so the value reads as a clean integer). Label: "View Distance" with a numeric readout next to it (same style as the existing sliders).

### Spawner integration

Each of the three spawners gains the same two-line read in `Tick()`:

```csharp
float effectiveRadius = inputSettings != null
    ? Mathf.Clamp(inputSettings.viewDistance, 100f, 1000f)
    : spawnRadius;
```

…and uses `effectiveRadius` in place of the hard-coded `spawnRadius` field throughout the tick (cell candidate prefilter, despawn-out-of-range threshold, the final `(hit.point - playerPos).sqrMagnitude > spawnRadius * spawnRadius` placement gate).

Cap auto-scales linearly with radius. Each spawner's baseline radius is documented in code as a `const`:

| Spawner | Baseline radius | Cap formula |
|---|---|---|
| TreeSpawner | 150 m | `effectiveCap = Mathf.RoundToInt(maxTrees * (effectiveRadius / 150f))` |
| MushroomSpawner | 300 m | `effectiveCap = Mathf.RoundToInt(maxMushrooms * (effectiveRadius / 300f))` |
| AlienNPCSpawner | 300 m | `effectiveCap = Mathf.RoundToInt(maxAlienNPCs * (effectiveRadius / 300f))` |

Floor: `effectiveCap = Mathf.Max(effectiveCap, maxXxx)` — dragging the slider DOWN to 100 m never reduces visible density below the player's configured `maxTrees` value (the slider is for "see more at distance", not "see less").

### `DespawnOutOfRange` interaction

The existing despawn loop uses `spawnRadius * 1.05f` as the despawn threshold (small hysteresis to avoid flicker at the boundary). With the new slider, this becomes `effectiveRadius * 1.05f`. When the slider is dragged DOWN at runtime, objects outside the new radius will fade out via the `SpawnFade.BeginFadeOut` path — no visible pop.

---

## Part 3 · Spawn / despawn integration in each spawner

The spawner's `SpawnX` method gains one line at the end:

```csharp
var fade = tree.GetComponent<SpawnFade>();
if (fade == null) fade = tree.AddComponent<SpawnFade>();
fade.BeginFadeIn();
```

(Identical shape for the mushroom and alien spawners — change the local name as needed.)

The spawner's `DespawnInternal` method changes from immediate `SetActive(false)` + pool-push, to a fade-out-then-pool-push:

```csharp
void DespawnInternal(BodyState entry, long cellId)
{
    if (!entry.activeTrees.TryGetValue(cellId, out var tree)) return;
    entry.activeTrees.Remove(cellId);
    if (tree == null) return;
    var st = tree.GetComponent<SpawnedTree>();
    int idx = st != null ? st.PrefabIndex : 0;
    if (idx < 0 || idx >= pools.Length) idx = 0;

    var fade = tree.GetComponent<SpawnFade>();
    if (fade != null)
    {
        fade.BeginFadeOut(() => ReturnToPool(tree, idx));
    }
    else
    {
        ReturnToPool(tree, idx);
    }
}

void ReturnToPool(GameObject obj, int poolIdx)
{
    if (obj == null) return;
    obj.transform.SetParent(null, true);
    obj.SetActive(false);
    pools[poolIdx].Push(obj);
}
```

The mushroom and alien spawner's despawn paths get the same shape (rename to `ReturnMushroomToPool` etc., adjust pool index lookup, but identical structure).

### Race with `MarkCellMined`

`TreeSpawner.MarkCellMined` currently calls `DespawnInternal` immediately when a tree is chopped. With fade-out, the tree would shrink briefly before disappearing — which is fine visually (it reads as "the tree falls / is consumed"). No code change needed beyond routing through the new fade path.

### Save/load

No changes. Active spawned objects are not part of `SaveData`; they regenerate from the deterministic seed on load. `viewDistance` is a player preference, stored in PlayerPrefs (not the save file).

---

## Coding-convention compliance (per `CLAUDE.md`)

- **Lazy-cached refs:** the spawners already cache `player`/`inputSettings`. No new `FindObjectOfType` calls per frame.
- **No per-frame allocation:** SpawnFade's coroutine yields each frame but doesn't allocate; it modifies `transform.localScale` directly.
- **Singleton / save patterns:** no new singletons. No save schema changes.
- **No forbidden zone touched:** atmosphere / planet generation untouched. Spawn raycasts continue to hit the planet's terrain layer.

## File-change summary

**New:**
- `Assets/3 - Scripts/World/SpawnFade.cs`

**Modified:**
- `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` — add `viewDistance` field + PlayerPrefs save/load.
- `Assets/3 - Scripts/Scripts/Game/UI/SettingsMenu.cs` — add `viewDistanceSlider` field + handler + Refresh/Apply wiring.
- `Assets/3 - Scripts/World/TreeSpawner.cs` — read `effectiveRadius` from `inputSettings.viewDistance`, scale cap, add SpawnFade on spawn, route despawn through fade-out.
- `Assets/3 - Scripts/World/MushroomSpawner.cs` — same shape as TreeSpawner.
- `Assets/3 - Scripts/World/AlienNPCSpawner.cs` — same shape as TreeSpawner. (One nuance: aliens have a NavMeshAgent that might object to scale-zero. SpawnFade uses 0.05f minimum, which avoids true zero — but verify in Play mode that nav doesn't break during the 0.4 s grow-in. If it does, fade-in for aliens can be skipped and only fade-out kept.)

**Scene-side setup (manual, one-time):**
- Add a `Slider` UI element to the pause menu's settings panel, range 100–1000, whole numbers, with a label "View Distance" and a numeric readout. Drag it into `SettingsMenu.viewDistanceSlider`.

## Risks / open questions

- **Alien spawn-time scale animation:** an alien's NavMeshAgent / AI might do something weird while the transform is at 0.05× scale (e.g., AI radius gets confused, navmesh detection misses). Mitigation: keep the fade-in fast (0.4 s) and verify in Play mode. If broken, disable the in-fade for aliens and keep only the out-fade.
- **Performance at view distance 1000 m on Tree spawn:** baseline density × `(1000 / 150) ≈ 6.67×`. With `maxTrees = 20` default, effective cap at 1000 m is ~133 trees. Each tree is full-prefab (no LOD), so this is the most expensive setting. Acceptable on a modern GPU; if needed, the user dials the slider back. Document the trade-off in the slider tooltip.
- **Slider live-drag:** while the user is dragging the slider, the spawners may rapidly add/remove objects every tick (0.25 s) as the cap changes. SpawnFade smooths this — but for very large drag distances (100 → 1000 instantly), there'd be a wave of pop-in. Acceptable; the typical use is "set once and play."
- **Mushroom random scale interacting with SpawnFade:** mushroom spawner randomizes scale 1–5× per cell. SpawnFade captures the post-random scale at `BeginFadeIn`, so the fade animates from `0.05 × randomScale` to `randomScale`. Correct behaviour.
