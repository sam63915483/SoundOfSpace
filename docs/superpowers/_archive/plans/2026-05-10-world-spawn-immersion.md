# World-Spawn Immersion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop trees / mushrooms / aliens from popping in and out, and give the player a single 100–1000 m view-distance slider that controls how far away all three spawners populate the world.

**Architecture:** Shared `SpawnFade` MonoBehaviour scale-fades any object in over ~0.4 s on appear and out over ~0.3 s before pool-return. New `InputSettings.viewDistance` field drives spawn radius live (same live-read pattern the existing `maxTrees`/`maxMushrooms`/`maxAlienNPCs` sliders use). Per-spawner cap auto-scales linearly with view distance so density stays natural at long range, floored at the player's configured base cap.

**Tech Stack:** Unity 2022.3, MonoBehaviour coroutines, no test framework (verification = Unity Editor compile + Play mode).

**Spec reference:** `docs/superpowers/specs/2026-05-10-world-spawn-immersion-design.md`

---

## File map

**New:**
- `Assets/3 - Scripts/World/SpawnFade.cs` — shared scale-fade helper.

**Modified:**
- `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs` — add `viewDistance` field + Save/Load.
- `Assets/3 - Scripts/Scripts/Game/UI/SettingsMenu.cs` — add slider field + handler + Refresh/Apply wiring.
- `Assets/3 - Scripts/World/TreeSpawner.cs` — read `effectiveRadius` from InputSettings, scale cap, add SpawnFade hooks.
- `Assets/3 - Scripts/World/MushroomSpawner.cs` — same shape as TreeSpawner.
- `Assets/3 - Scripts/World/AlienNPCSpawner.cs` — same shape as TreeSpawner.

**Scene-side setup (manual, one-time at the end):** add a `Slider` UI element to the pause-menu settings panel and drag it into `SettingsMenu.viewDistanceSlider`.

---

## Conventions used by every task

- **Compile check:** after each code change, run `mcp__coplay-mcp__check_compile_errors` (or focus the Unity Editor; it auto-compiles). Inspect Console for red errors.
- **Commit format:** match repo style (`feat(world): …` / `feat(settings): …`).
- **`git add` is always specific:** never `git add -A`.
- Working dir: `C:\123\1aughhh1`. No automated tests; user verifies in Play mode at the end.

---

## Task 1: Create `SpawnFade` shared helper

**Files:**
- Create: `Assets/3 - Scripts/World/SpawnFade.cs`

Standalone MonoBehaviour. Compiles by itself; no callers yet (the three spawners integrate it in Tasks 4–6).

- [ ] **Step 1: Create the file with this exact content.**

```csharp
using System.Collections;
using UnityEngine;

// Shared scale-fade helper for runtime-spawned world objects (trees,
// mushrooms, aliens). Scales the object from a tiny start size up to its
// captured target on appear, and scales back down before pool-return on
// despawn. No shader/material changes — works with any prefab.
//
// Usage:
//   var fade = obj.GetComponent<SpawnFade>() ?? obj.AddComponent<SpawnFade>();
//   fade.BeginFadeIn();                       // call AFTER setting final scale
//   ...
//   fade.BeginFadeOut(() => ReturnToPool(obj)); // delays pool-return until fade ends
public class SpawnFade : MonoBehaviour
{
    [Tooltip("Seconds to grow from startMultiplier × target to full target.")]
    public float fadeInDuration = 0.4f;
    [Tooltip("Seconds to shrink from full target down to startMultiplier × target.")]
    public float fadeOutDuration = 0.3f;
    [Tooltip("Fraction of the target scale used at fade-in start and fade-out end. 0.05 is small enough to read as invisible without hitting true zero (some shaders / colliders dislike scale zero).")]
    public float startMultiplier = 0.05f;

    Coroutine _routine;
    Vector3 _targetScale;
    bool _fadingOut;

    public void BeginFadeIn()
    {
        if (_routine != null) StopCoroutine(_routine);
        _fadingOut = false;
        _targetScale = transform.localScale;
        transform.localScale = _targetScale * startMultiplier;
        if (!gameObject.activeInHierarchy) return;
        _routine = StartCoroutine(FadeInRoutine());
    }

    public void BeginFadeOut(System.Action onComplete)
    {
        if (_routine != null) StopCoroutine(_routine);
        _fadingOut = true;
        // Capture current scale as the "from" — if BeginFadeIn was in flight,
        // current scale may be mid-fade and that's the right starting point.
        if (!gameObject.activeInHierarchy)
        {
            // Object already disabled — skip the animation and invoke the
            // callback synchronously so the spawner's bookkeeping stays sane.
            onComplete?.Invoke();
            return;
        }
        _routine = StartCoroutine(FadeOutRoutine(onComplete));
    }

    IEnumerator FadeInRoutine()
    {
        Vector3 from = _targetScale * startMultiplier;
        Vector3 to = _targetScale;
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeInDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // Ease-out cubic so the grow feels organic.
            k = 1f - Mathf.Pow(1f - k, 3f);
            transform.localScale = Vector3.Lerp(from, to, k);
            yield return null;
        }
        transform.localScale = to;
        _routine = null;
    }

    IEnumerator FadeOutRoutine(System.Action onComplete)
    {
        Vector3 from = transform.localScale;
        Vector3 to = _targetScale * startMultiplier;
        if (_targetScale == Vector3.zero)
        {
            // BeginFadeIn was never called (object spawned without going through
            // the fade-in path). Use current scale as the implicit target.
            _targetScale = from;
            to = _targetScale * startMultiplier;
        }
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeOutDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            transform.localScale = Vector3.Lerp(from, to, k);
            yield return null;
        }
        transform.localScale = to;
        _routine = null;
        onComplete?.Invoke();
    }

    void OnDisable()
    {
        // If the object is disabled mid-fade (e.g., scene unload), stop the
        // coroutine and restore the target scale so a future BeginFadeIn re-uses
        // a clean baseline.
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
            if (_targetScale != Vector3.zero) transform.localScale = _targetScale;
        }
    }
}
```

- [ ] **Step 2: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 3: Commit.**

```bash
git add "Assets/3 - Scripts/World/SpawnFade.cs"
git commit -m "feat(world): add shared SpawnFade scale-fade helper"
```

---

## Task 2: Add `viewDistance` to `InputSettings`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs`

- [ ] **Step 1: Add the constant + the public field.**

Find the existing `const` block near the top (around line 9-15). Add a new constant after `defaultMaxAudienceSize`:

```csharp
	const float defaultViewDistance = 350f;
```

Find the public-field block (around line 24-31). Add `viewDistance` after `maxAudienceSize`:

```csharp
	[Range(100, 1000)] public float viewDistance = defaultViewDistance;
```

- [ ] **Step 2: Wire `LoadSettings`.**

Find `public void LoadSettings ()` (around line 60). Add a line after the `maxAudienceSize = PlayerPrefs.GetInt(...)` line:

```csharp
		viewDistance = PlayerPrefs.GetFloat (nameof (viewDistance), defaultViewDistance);
```

- [ ] **Step 3: Wire `SaveSettings`.**

Find `public void SaveSettings ()` (around line 79). Add a line after the `PlayerPrefs.SetInt (nameof (maxAudienceSize), maxAudienceSize);` line:

```csharp
		PlayerPrefs.SetFloat (nameof (viewDistance), viewDistance);
```

- [ ] **Step 4: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 5: Commit.**

```bash
git add "Assets/3 - Scripts/Scripts/Game/Controllers/InputSettings.cs"
git commit -m "feat(settings): add viewDistance field (100–1000 m, default 350)"
```

---

## Task 3: Add view-distance slider to `SettingsMenu`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/UI/SettingsMenu.cs`

Adds the C# wiring (field + handler + Refresh/Apply). The user wires the actual `Slider` UI element in the scene/prefab once the code lands.

- [ ] **Step 1: Add the field declaration.**

Find the existing slider-field block (around lines 12-18). Add after `maxAudienceSlider`:

```csharp
	public UnityEngine.UI.Slider viewDistanceSlider;
```

- [ ] **Step 2: Wire `onValueChanged` in the existing init method.**

Find the block of `slider.onValueChanged.AddListener(...)` calls (around lines 22-37). Add after the last existing one:

```csharp
		if (viewDistanceSlider != null)
			viewDistanceSlider.onValueChanged.AddListener (OnViewDistanceChanged);
```

- [ ] **Step 3: Add the handler method.**

Find an existing `OnXxxxChanged` handler (e.g., `OnMaxTreesChanged` around line 39). Add this new handler immediately after one of the existing ones:

```csharp
	void OnViewDistanceChanged (float value) {
		if (inputSettings != null) inputSettings.viewDistance = Mathf.Clamp (value, 100f, 1000f);
	}
```

- [ ] **Step 4: Wire Refresh.**

Find the `SetValueWithoutNotify` block (around lines 72-83 — where `maxTreesSlider`, `maxAlienNPCsSlider`, etc. get their values pushed from `inputSettings`). Add after the existing block:

```csharp
		if (viewDistanceSlider != null)
			viewDistanceSlider.SetValueWithoutNotify (inputSettings.viewDistance);
```

- [ ] **Step 5: Wire Apply.**

Find the `slider.value` → `inputSettings.xxx` write block (around lines 100-110). Add after the existing block:

```csharp
		if (viewDistanceSlider != null)
			inputSettings.viewDistance = Mathf.Clamp (viewDistanceSlider.value, 100f, 1000f);
```

- [ ] **Step 6: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 7: Commit.**

```bash
git add "Assets/3 - Scripts/Scripts/Game/UI/SettingsMenu.cs"
git commit -m "feat(settings): wire view-distance slider in pause menu"
```

---

## Task 4: TreeSpawner — view distance + cap scaling + SpawnFade

**Files:**
- Modify: `Assets/3 - Scripts/World/TreeSpawner.cs`

Replaces hardcoded `spawnRadius` reads in `Tick` and `DespawnOutOfRange` with `effectiveRadius` from `inputSettings.viewDistance`. Scales the cap. Adds SpawnFade hooks to the spawn and despawn paths.

- [ ] **Step 1: Add the baseline-radius constant.**

Near the top of the class, after the existing `[Header("Determinism")]` block (around line 26), add a private const used by the cap-scaling math:

```csharp
    const float BaselineRadius = 150f;
```

(Place it before the `class BodyState` declaration. Co-locating with the existing `cellSize` / `treeSpawnChance` fields keeps it discoverable.)

- [ ] **Step 2: Replace `Tick` to read `effectiveRadius` and scale the cap.**

Find the `void Tick()` method (around line 147). Replace its body with:

```csharp
    void Tick()
    {
        Vector3 playerPos = GetViewerPosition();

        float effectiveRadius = inputSettings != null
            ? Mathf.Clamp(inputSettings.viewDistance, 100f, 1000f)
            : spawnRadius;
        int baseCap = (inputSettings != null) ? Mathf.Clamp(inputSettings.maxTrees, 1, 1000) : maxTrees;
        int effectiveMax = Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * (effectiveRadius / BaselineRadius)));

        for (int s = 0; s < bodies.Count; s++) DespawnOutOfRange(bodies[s], playerPos, effectiveRadius);

        scratchCandidates.Clear();
        float prefilterMax = effectiveRadius + cellSize;
        float prefilterMaxSq = prefilterMax * prefilterMax;

        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            if (entry.body == null) continue;
            float bodyDistSq = (entry.body.Position - playerPos).sqrMagnitude;
            float bodyOuter = effectiveRadius + entry.body.radius + cellSize;
            if (bodyDistSq > bodyOuter * bodyOuter) continue;

            float faceUVPerCell = cellSize / Mathf.Max(0.001f, entry.body.radius);
            int half = Mathf.CeilToInt(1f / Mathf.Max(0.0001f, faceUVPerCell)) + 1;

            for (int face = 0; face < 6; face++)
            {
                for (int cu = -half; cu <= half; cu++)
                {
                    for (int cv = -half; cv <= half; cv++)
                    {
                        long id = EncodeCell(face, cu, cv);
                        if (entry.minedCells.Contains(id)) continue;
                        if (entry.activeTrees.ContainsKey(id)) continue;
                        if (!CellHasTree(face, cu, cv)) continue;
                        if (!TryComputeCellApproxPos(entry.body, face, cu, cv, faceUVPerCell, out Vector3 spherePos)) continue;
                        float dSq = (spherePos - playerPos).sqrMagnitude;
                        if (dSq > prefilterMaxSq) continue;

                        scratchCandidates.Add(new CellCandidate { bodySlot = s, face = face, cellU = cu, cellV = cv, distSq = dSq });
                    }
                }
            }
        }

        scratchCandidates.Sort(CandidateByDistance);

        for (int i = 0; i < scratchCandidates.Count; i++)
        {
            if (CountActive() >= effectiveMax) break;
            var c = scratchCandidates[i];
            var entry = bodies[c.bodySlot];
            float faceUVPerCell = cellSize / Mathf.Max(0.001f, entry.body.radius);
            if (!TryComputeTreePlacement(entry, c.face, c.cellU, c.cellV, faceUVPerCell, playerPos, effectiveRadius,
                                          out Vector3 pos, out Quaternion rot, out int prefabIdx))
                continue;
            SpawnTree(entry, c.bodySlot, EncodeCell(c.face, c.cellU, c.cellV), prefabIdx, pos, rot);
        }

        EnforceMaxTrees(playerPos, effectiveMax);
    }
```

- [ ] **Step 3: Add `effectiveRadius` to `DespawnOutOfRange`.**

Find `void DespawnOutOfRange(BodyState entry, Vector3 playerPos)` (around line 223). Add a third parameter and use it:

```csharp
    void DespawnOutOfRange(BodyState entry, Vector3 playerPos, float effectiveRadius)
    {
        scratchRemove.Clear();
        float limit = effectiveRadius * 1.05f;
        float limitSq = limit * limit;
        foreach (var kv in entry.activeTrees)
        {
            if (kv.Value == null) { scratchRemove.Add(kv.Key); continue; }
            if ((kv.Value.transform.position - playerPos).sqrMagnitude > limitSq)
                scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnInternal(entry, scratchRemove[i]);
    }
```

- [ ] **Step 4: Add `effectiveRadius` to `TryComputeTreePlacement`.**

Find the method signature (around line 265). Add an `effectiveRadius` parameter and use it in two places (the prefilter and the final placement gate):

```csharp
    bool TryComputeTreePlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                                  Vector3 playerPos, float effectiveRadius,
                                  out Vector3 pos, out Quaternion rot, out int prefabIdx)
    {
        pos = default; rot = default; prefabIdx = 0;

        uint hJU = Hash(seed, face, cellU, cellV, 2);
        uint hJV = Hash(seed, face, cellU, cellV, 3);
        uint hPI = Hash(seed, face, cellU, cellV, 4);
        uint hY  = Hash(seed, face, cellU, cellV, 5);

        float jitterU = ((hJU & 0xFFFFu) / 65535f - 0.5f) * faceUVPerCell * 0.9f;
        float jitterV = ((hJV & 0xFFFFu) / 65535f - 0.5f) * faceUVPerCell * 0.9f;
        float faceU = (cellU + 0.5f) * faceUVPerCell + jitterU;
        float faceV = (cellV + 0.5f) * faceUVPerCell + jitterV;

        if (faceU < -1f || faceU > 1f || faceV < -1f || faceV > 1f) return false;

        Vector3 dir = FaceUVToDirection(face, faceU, faceV);
        if (dir.sqrMagnitude < 0.0001f) return false;

        var planet = entry.body;
        Vector3 spherePos = planet.Position + dir * planet.radius;
        float prefilterMax = effectiveRadius + cellSize;
        if ((spherePos - playerPos).sqrMagnitude > prefilterMax * prefilterMax) return false;

        Vector3 rayOrigin = planet.Position + dir * (planet.radius + surfaceRayHeight);
        if (!Physics.Raycast(rayOrigin, -dir, out RaycastHit hit,
                             planet.radius * 2f, groundMask, QueryTriggerInteraction.Ignore))
            return false;

        if (entry.gen != null)
        {
            float oceanR = entry.gen.GetOceanRadius();
            if (oceanR > 0f && (hit.point - planet.Position).magnitude < oceanR)
                return false;
        }

        if ((hit.point - playerPos).sqrMagnitude > effectiveRadius * effectiveRadius) return false;

        Vector3 up = (hit.point - planet.Position).normalized;
        float yaw = (hY & 0xFFFFu) / 65535f * 360f;
        rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);
        pos = hit.point - up * groundOffset;
        prefabIdx = (int)(hPI % (uint)treePrefabs.Length);
        return true;
    }
```

- [ ] **Step 5: Hook SpawnFade into `SpawnTree`.**

Find `void SpawnTree(...)` (around line 312). Append the fade-in call at the very end of the method, after the existing line `entry.activeTrees[cellId] = tree;`:

```csharp
        var fade = tree.GetComponent<SpawnFade>();
        if (fade == null) fade = tree.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
```

- [ ] **Step 6: Route `DespawnInternal` through a fade-out.**

Find `void DespawnInternal(BodyState entry, long cellId)` (around line 338). Replace its body so it triggers the fade and only returns to pool when the fade completes:

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
            int capturedIdx = idx;
            fade.BeginFadeOut(() => ReturnTreeToPool(tree, capturedIdx));
        }
        else
        {
            ReturnTreeToPool(tree, idx);
        }
    }

    void ReturnTreeToPool(GameObject tree, int poolIdx)
    {
        if (tree == null) return;
        tree.transform.SetParent(null, true);
        tree.SetActive(false);
        if (poolIdx < 0 || poolIdx >= pools.Length) poolIdx = 0;
        pools[poolIdx].Push(tree);
    }
```

- [ ] **Step 7: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

If you get "no overload for `DespawnOutOfRange`/`TryComputeTreePlacement` matches" — verify all call sites pass `effectiveRadius` (only `Tick` calls them in this file).

- [ ] **Step 8: Commit.**

```bash
git add "Assets/3 - Scripts/World/TreeSpawner.cs"
git commit -m "feat(world): tree spawner reads viewDistance, scales cap, fades in/out"
```

---

## Task 5: MushroomSpawner — view distance + cap scaling + SpawnFade

**Files:**
- Modify: `Assets/3 - Scripts/World/MushroomSpawner.cs`

Same pattern as Task 4 but baseline radius is 300 m and the cap scales off `maxMushrooms`.

- [ ] **Step 1: Add the baseline-radius constant.**

Near the top of the class, after the existing `[Header("Determinism")]` block (around line 27), add:

```csharp
    const float BaselineRadius = 300f;
```

- [ ] **Step 2: Replace `Tick` to use `effectiveRadius`.**

Find `void Tick()` (around line 145). Replace its body with:

```csharp
    void Tick()
    {
        Vector3 playerPos = GetViewerPosition();

        float effectiveRadius = inputSettings != null
            ? Mathf.Clamp(inputSettings.viewDistance, 100f, 1000f)
            : spawnRadius;
        int baseCap = (inputSettings != null) ? Mathf.Clamp(inputSettings.maxMushrooms, 0, 1000) : maxMushrooms;
        int effectiveMax = Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * (effectiveRadius / BaselineRadius)));

        for (int s = 0; s < bodies.Count; s++) DespawnOutOfRange(bodies[s], playerPos, effectiveRadius);

        if (effectiveMax <= 0)
        {
            EnforceMaxMushrooms(playerPos, 0);
            return;
        }

        scratchCandidates.Clear();
        float prefilterMax = effectiveRadius + cellSize;
        float prefilterMaxSq = prefilterMax * prefilterMax;

        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            if (entry.body == null) continue;
            float bodyDistSq = (entry.body.Position - playerPos).sqrMagnitude;
            float bodyOuter = effectiveRadius + entry.body.radius + cellSize;
            if (bodyDistSq > bodyOuter * bodyOuter) continue;

            float faceUVPerCell = cellSize / Mathf.Max(0.001f, entry.body.radius);
            int half = Mathf.CeilToInt(1f / Mathf.Max(0.0001f, faceUVPerCell)) + 1;

            for (int face = 0; face < 6; face++)
            {
                for (int cu = -half; cu <= half; cu++)
                {
                    for (int cv = -half; cv <= half; cv++)
                    {
                        long id = EncodeCell(face, cu, cv);
                        if (entry.consumedCells.Contains(id)) continue;
                        if (entry.activeMushrooms.ContainsKey(id)) continue;
                        if (!CellHasMushroom(face, cu, cv)) continue;
                        if (!TryComputeCellApproxPos(entry.body, face, cu, cv, faceUVPerCell, out Vector3 spherePos)) continue;
                        float dSq = (spherePos - playerPos).sqrMagnitude;
                        if (dSq > prefilterMaxSq) continue;

                        scratchCandidates.Add(new CellCandidate { bodySlot = s, face = face, cellU = cu, cellV = cv, distSq = dSq });
                    }
                }
            }
        }

        scratchCandidates.Sort(CandidateByDistance);

        for (int i = 0; i < scratchCandidates.Count; i++)
        {
            if (CountActive() >= effectiveMax) break;
            var c = scratchCandidates[i];
            var entry = bodies[c.bodySlot];
            float faceUVPerCell = cellSize / Mathf.Max(0.001f, entry.body.radius);
            if (!TryComputeMushroomPlacement(entry, c.face, c.cellU, c.cellV, faceUVPerCell, playerPos, effectiveRadius,
                                              out Vector3 pos, out Quaternion rot,
                                              out int prefabIdx, out float scale,
                                              out float colourPct, out float breathPct, out float kaleidoPct))
                continue;
            SpawnMushroom(entry, c.bodySlot, EncodeCell(c.face, c.cellU, c.cellV), prefabIdx, pos, rot, scale,
                          colourPct, breathPct, kaleidoPct);
        }

        EnforceMaxMushrooms(playerPos, effectiveMax);
    }
```

- [ ] **Step 3: Add `effectiveRadius` parameter to `DespawnOutOfRange`.**

Find `void DespawnOutOfRange(BodyState entry, Vector3 playerPos)` (around line 234). Replace with:

```csharp
    void DespawnOutOfRange(BodyState entry, Vector3 playerPos, float effectiveRadius)
    {
        scratchRemove.Clear();
        float limit = effectiveRadius * 1.05f;
        float limitSq = limit * limit;
        foreach (var kv in entry.activeMushrooms)
        {
            if (kv.Value == null) { scratchRemove.Add(kv.Key); continue; }
            if ((kv.Value.transform.position - playerPos).sqrMagnitude > limitSq)
                scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnInternal(entry, scratchRemove[i]);
    }
```

- [ ] **Step 4: Add `effectiveRadius` parameter to `TryComputeMushroomPlacement`.**

Find `bool TryComputeMushroomPlacement(...)` (around line 276). Update the signature and replace the two `spawnRadius`-using lines:

```csharp
    bool TryComputeMushroomPlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                                     Vector3 playerPos, float effectiveRadius,
                                     out Vector3 pos, out Quaternion rot,
                                     out int prefabIdx, out float scale,
                                     out float colourPct, out float breathPct, out float kaleidoPct)
```

Inside the method body, find these two lines and update them:

Old:
```csharp
        float prefilterMax = spawnRadius + cellSize;
```
New:
```csharp
        float prefilterMax = effectiveRadius + cellSize;
```

Old:
```csharp
        if ((hit.point - playerPos).sqrMagnitude > spawnRadius * spawnRadius) return false;
```
New:
```csharp
        if ((hit.point - playerPos).sqrMagnitude > effectiveRadius * effectiveRadius) return false;
```

Leave everything else in the method unchanged.

- [ ] **Step 5: Hook SpawnFade into `SpawnMushroom`.**

Find `void SpawnMushroom(...)` (around line 340). At the very end of the method, after `entry.cellPrefabIdx[cellId] = prefabIdx;`, append:

```csharp
        var fade = mushroom.GetComponent<SpawnFade>();
        if (fade == null) fade = mushroom.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
```

- [ ] **Step 6: Route `DespawnInternal` through fade-out.**

Find `void DespawnInternal(BodyState entry, long cellId)` (around line 421). Replace its body with:

```csharp
    void DespawnInternal(BodyState entry, long cellId)
    {
        if (!entry.activeMushrooms.TryGetValue(cellId, out var mushroom)) return;
        entry.activeMushrooms.Remove(cellId);
        entry.cellPrefabIdx.TryGetValue(cellId, out int idx);
        entry.cellPrefabIdx.Remove(cellId);
        if (mushroom == null) return;
        if (idx < 0 || idx >= pools.Length) idx = 0;

        var fade = mushroom.GetComponent<SpawnFade>();
        if (fade != null)
        {
            int capturedIdx = idx;
            fade.BeginFadeOut(() => ReturnMushroomToPool(mushroom, capturedIdx));
        }
        else
        {
            ReturnMushroomToPool(mushroom, idx);
        }
    }

    void ReturnMushroomToPool(GameObject mushroom, int poolIdx)
    {
        if (mushroom == null) return;
        mushroom.transform.SetParent(null, true);
        mushroom.SetActive(false);
        if (poolIdx < 0 || poolIdx >= pools.Length) poolIdx = 0;
        pools[poolIdx].Push(mushroom);
    }
```

- [ ] **Step 7: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

- [ ] **Step 8: Commit.**

```bash
git add "Assets/3 - Scripts/World/MushroomSpawner.cs"
git commit -m "feat(world): mushroom spawner reads viewDistance, scales cap, fades in/out"
```

---

## Task 6: AlienNPCSpawner — view distance + cap scaling + SpawnFade

**Files:**
- Modify: `Assets/3 - Scripts/World/AlienNPCSpawner.cs`

Same shape as Tasks 4 and 5. Aliens use `maxAlienNPCs` as the baseline cap with `BaselineRadius = 300f`.

- [ ] **Step 1: Open the file and find the existing tick/despawn/placement/spawn methods.**

Locate (via `Grep` if needed):
- `void Tick()`
- `void DespawnOutOfRange(BodyState entry, Vector3 playerPos)`
- The placement method (its name will be `TryComputeAlienPlacement` or similar — confirm via grep)
- `void SpawnAlien(...)` (or whatever the spawner-internal method is named)
- `void DespawnInternal(BodyState entry, long cellId)`

- [ ] **Step 2: Add the baseline-radius constant.**

Near the top of the class, after the existing `[Header("Determinism")]` block, add:

```csharp
    const float BaselineRadius = 300f;
```

- [ ] **Step 3: Update `Tick` to use `effectiveRadius` and scale the cap.**

Apply the same edits as Task 5 Step 2 but with:
- Read from `inputSettings.maxAlienNPCs` instead of `inputSettings.maxMushrooms`
- Use `effectiveRadius` everywhere `spawnRadius` is referenced
- Pass `effectiveRadius` to `DespawnOutOfRange` and the placement method
- Compute `effectiveMax = Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * (effectiveRadius / BaselineRadius)));`

Read the current `Tick()` body first to see exactly which calls need the new parameter — the structure is parallel to MushroomSpawner.

- [ ] **Step 4: Update `DespawnOutOfRange` signature + body.**

Add `float effectiveRadius` parameter; replace `spawnRadius * 1.05f` with `effectiveRadius * 1.05f`. Same as Task 5 Step 3.

- [ ] **Step 5: Update the placement method's signature + body.**

Add `float effectiveRadius` parameter; replace the `prefilterMax = spawnRadius + cellSize` line and the `> spawnRadius * spawnRadius` gate with `effectiveRadius`. Same as Task 5 Step 4.

- [ ] **Step 6: Hook SpawnFade into the alien-spawn method.**

At the end of the alien-spawn method (after `entry.activeAliens[cellId] = alien;` or similar), append:

```csharp
        var fade = alien.GetComponent<SpawnFade>();
        if (fade == null) fade = alien.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
```

Adjust `alien` to whatever the local variable name actually is.

- [ ] **Step 7: Route `DespawnInternal` through fade-out.**

Same shape as Task 5 Step 6 — replace the body to invoke `BeginFadeOut` and pool-return on completion. Helper method name: `ReturnAlienToPool`.

- [ ] **Step 8: Compile check.**

Run `mcp__coplay-mcp__check_compile_errors`. Expected: `No compile errors`.

If aliens use a NavMeshAgent, scale-fade should be safe at 0.05× start (the agent's pathing doesn't depend on transform scale). If you see warnings during fade-in about agent placement, defer fade-in to next frame by yielding once before starting the coroutine — but try the straight version first.

- [ ] **Step 9: Commit.**

```bash
git add "Assets/3 - Scripts/World/AlienNPCSpawner.cs"
git commit -m "feat(world): alien spawner reads viewDistance, scales cap, fades in/out"
```

---

## Task 7: Verification + scene-side slider wiring

**Files:** none modified by code.

User-driven Editor playtest, ~5 minutes. Also includes the one-time scene-setup step for the slider.

- [ ] **Step 1: Wire the slider UI in the pause menu.**

Open the pause menu's settings panel prefab (find it via the existing `SettingsMenu` component — drag-to-find on the inspector field will reveal which scene/prefab carries it). Add a new `UnityEngine.UI.Slider` element styled like the existing density sliders:
- Min: `100`, Max: `1000`, Whole Numbers: `On` (so the value rounds to integers).
- Label text: `View Distance` + a numeric readout next to it.
- Drag the slider into the `SettingsMenu` component's `viewDistanceSlider` field.

Save the scene/prefab.

- [ ] **Step 2: Play-mode test — fade-in.**

Enter Play mode. Move the player toward a region with trees / mushrooms / aliens. As you approach the spawn boundary, the new objects should grow from ~5% scale up to full size over ~0.4 s — no instant pop.

- [ ] **Step 3: Play-mode test — fade-out.**

Walk away from a marked tree / mushroom / alien. As you cross the despawn threshold (`effectiveRadius * 1.05`), the object should shrink to ~5% scale over ~0.3 s before disappearing — no instant disappear.

- [ ] **Step 4: Play-mode test — slider.**

Open the pause menu → Settings. Drag the View Distance slider:
- At 100 m: only objects very close to the player remain. Cap unchanged (floored at baseline).
- At 350 m (default): close to today's behavior for mushrooms/aliens, much bigger than today for trees.
- At 1000 m: objects visible far out toward the horizon. Cap ~6.7× higher for trees.

Confirm the slider's value persists across pause-menu close/reopen.

- [ ] **Step 5: Play-mode test — chop a tree.**

Equip the axe, chop a tree. The tree should shrink (fade-out) before disappearing, but the wood +1 popup still fires correctly. `MarkCellMined` still marks the cell as gone (you shouldn't see it respawn next time you walk through).

- [ ] **Step 6: Save / load round-trip.**

Save from the pause menu → return to main menu → load. The view distance slider value should persist (PlayerPrefs); world spawn behaviour matches what it was before save.

- [ ] **Step 7: No commit.**

Verification only — nothing to commit. If anything fails, file as a follow-up and fix.

---

## Self-review

**1. Spec coverage:**
- SpawnFade scale-fade helper (Spec Part 1): Task 1. ✓
- InputSettings.viewDistance field + PlayerPrefs (Spec Part 2 first half): Task 2. ✓
- Pause-menu slider C# wiring (Spec Part 2 second half): Task 3. ✓
- TreeSpawner integration (Spec Part 3): Task 4. ✓
- MushroomSpawner integration: Task 5. ✓
- AlienNPCSpawner integration: Task 6. ✓
- Cap auto-scales linearly with floor: each spawner task computes `effectiveMax = Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * (effectiveRadius / BaselineRadius)))`. ✓
- Spec's hardcoded radii (Trees=150, Mushrooms=300, Aliens=300) match the `BaselineRadius` consts in Tasks 4/5/6. ✓
- Scene-side slider wiring: Task 7 Step 1.
- MarkCellMined routes through fade-out: Task 4 covers it (the new `DespawnInternal` is the single despawn path). Verified by Task 7 Step 5.

**2. Placeholder scan:** None. Every step has concrete code or commands.

**3. Type consistency:**
- `SpawnFade.BeginFadeIn()` / `BeginFadeOut(System.Action)` — used identically across all three spawners (Tasks 4/5/6).
- `BaselineRadius` constant used per-spawner with different value (150 / 300 / 300) — matches spec.
- `effectiveRadius` (float) and `effectiveMax` (int) — consistent names across all three Tick methods.
- `inputSettings.viewDistance` (float) — declared in Task 2, read in Tasks 4/5/6.
- `Mathf.Clamp(inputSettings.viewDistance, 100f, 1000f)` used identically in all three spawners.

One note: Task 6 (AlienNPCSpawner) is written at a slightly higher level because the alien spawner's internal method names depend on what's actually in that file (I haven't read it cover-to-cover). The implementer should `Grep` for the relevant method names in their first step. The transformation pattern is identical to Task 5.
