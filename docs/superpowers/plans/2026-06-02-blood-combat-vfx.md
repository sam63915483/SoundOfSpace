# Blood Combat VFX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a pistol bullet hits an enemy, spray a blood burst out of the hit point toward the shooter; when an enemy dies, leave a blood pool on the ground that lingers then fades.

**Architecture:** One scene-placed `BloodFX` singleton owns all prefab references + tuning and exposes `SpawnSpray(...)` / `SpawnPool(...)`. `PistolController.TriggerShot` and `EnemyController.BeginDeath` each get a single `BloodFX.Instance?.Spawn…` call. A `BloodPool` component fades each pool out by ramping live-particle alpha after a linger delay, then self-destroys. Blood is ephemeral VFX — nothing is saved.

**Tech Stack:** Unity 2022.3, Built-in Render Pipeline, C# (`Assembly-CSharp`, no asmdefs). Piloto Studio Blood VFX Essentials (Shader Graph, Built-in target — verified, no conversion needed). Unity MCP (coplay) for compile checks + scene edits. No CLI build/test — verification is `check_compile_errors` + in-editor playtest.

**Reference spec:** `docs/superpowers/specs/2026-06-02-blood-combat-vfx-design.md`

---

## Critical conventions (from CLAUDE.md — do not violate)

- **DO NOT TOUCH** the forbidden atmosphere/celestial/shader zone. This feature lives only in `Assets/3 - Scripts/Combat/` and `Pickups/`. Read-only use of `NBodySimulation.Bodies` / `CelestialBody.Position` is fine (gameplay accessors).
- **Never hand-write `.meta` files.** Fabricated GUIDs make Unity drop the script (CS0246). Create the `.cs` with the Write tool, then let Unity generate the `.meta` on import (it auto-imports when the editor regains focus, or trigger a refresh). Then `git add` BOTH the `.cs` and its generated `.meta`.
- **`git commit -a` skips untracked files** — always `git add` new files explicitly.
- `NBodySimulation.Bodies` is null-safe (returns `Array.Empty` off the solar-system scene) — never deref `Instance.bodies` raw.
- No `EnsureGameplaySingletons` seeding needed: `BloodFX` is scene-placed, so it exists on both editor-play and build-load (the MainMenu-singleton trap does not apply).
- No save-system changes: blood is ephemeral.

## File structure

| File | Responsibility |
|---|---|
| `Assets/3 - Scripts/Combat/BloodPool.cs` (new) | Fades one spawned pool out (live-particle alpha ramp) after a linger delay, then destroys it. |
| `Assets/3 - Scripts/Combat/BloodFX.cs` (new) | Scene-placed singleton. Holds prefab refs + tuning. `SpawnSpray` / `SpawnPool`: instantiate, orient, parent under planet, disable colliders, manage lifetime. |
| `Assets/3 - Scripts/Pickups/PistolController.cs` (modify, ~line 339) | One line: spawn spray at the bullet hit point. |
| `Assets/3 - Scripts/Combat/EnemyController.cs` (modify, ~line 855) | Two lines: spawn pool at the enemy's feet on death. |
| `Assets/1.6.7.7.7.unity` (modify) | New `BloodFX` manager GameObject with prefabs assigned. |

## Default prefab choices (assigned in Task 5; swappable in Task 6)

- **Spray** = `Assets/Piloto Studio/Blood VFX Essentials/Bloody Fountains/Blood_Fountain_1.prefab` — GUID `6f3f07dfd16ddd14ea9b6bb0e9f83b40`. (Alternative if too gushy: `Blood Splashes/Sticky_Splash_1.prefab`, GUID `2072eced8024d414cbbd3b8251dd8ce3`.)
- **Pool** = `Assets/Piloto Studio/Blood VFX Essentials/Blood Splats/Sticky_Splat_1.prefab` — GUID `3c08428bc363b8745ac2bea33b68e26b`.

---

## Task 1: `BloodPool` — pool fade-out component

**Files:**
- Create: `Assets/3 - Scripts/Combat/BloodPool.cs`

- [ ] **Step 1: Write the component**

The pool prefab's looping system re-emits particles with a 5s lifetime, so a clean fade must scale the **live** particle set, not just `startColor` (which only affects new particles). Approach: linger → stop emission (set becomes stable, only shrinks as particles age out) → each frame multiply every live particle's alpha by the per-frame ratio so the overall fade is linear → destroy. Scaling `Particle.startColor.a` is shader-agnostic (the particle system applies it as a vertex-colour multiply, which the Piloto Unlit material honours).

Create `Assets/3 - Scripts/Combat/BloodPool.cs`:

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to a spawned blood-pool FX by BloodFX. Holds the pool at full
/// opacity for lingerSeconds, then fades it out over fadeSeconds and destroys
/// the GameObject. The pool prefab loops with a ~5s particle lifetime, so the
/// fade stops emission (freezing the live set) and ramps each live particle's
/// alpha down — this is shader-agnostic because the particle system applies
/// particle colour as a vertex multiply, independent of the material's property
/// names.
/// </summary>
public class BloodPool : MonoBehaviour
{
    ParticleSystem[] _systems;
    ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[64];
    float _linger;
    float _fade;

    public void Init(float lingerSeconds, float fadeSeconds)
    {
        _linger = Mathf.Max(0f, lingerSeconds);
        _fade   = Mathf.Max(0.05f, fadeSeconds);
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        if (_linger > 0f) yield return new WaitForSeconds(_linger);

        _systems = GetComponentsInChildren<ParticleSystem>(true);

        // Freeze births so the live set only shrinks (particles age out) — no
        // new full-alpha particles appear mid-fade.
        for (int i = 0; i < _systems.Length; i++)
            if (_systems[i] != null)
                _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);

        float elapsed = 0f;
        float prevMul = 1f;
        while (elapsed < _fade)
        {
            elapsed += Time.deltaTime;
            float mul = Mathf.Clamp01(1f - elapsed / _fade);
            // Per-frame ratio so repeated multiplies compose to a linear fade.
            float factor = prevMul > 0.0001f ? mul / prevMul : 0f;
            prevMul = mul;

            for (int i = 0; i < _systems.Length; i++)
            {
                var ps = _systems[i];
                if (ps == null) continue;
                int count = ps.particleCount;
                if (count == 0) continue;
                if (_buffer.Length < count) _buffer = new ParticleSystem.Particle[count];
                int n = ps.GetParticles(_buffer);
                for (int p = 0; p < n; p++)
                {
                    Color32 c = _buffer[p].startColor;
                    c.a = (byte)Mathf.RoundToInt(c.a * factor);
                    _buffer[p].startColor = c;
                }
                ps.SetParticles(_buffer, n);
            }
            yield return null;
        }

        Destroy(gameObject);
    }
}
```

- [ ] **Step 2: Let Unity import + generate the .meta**

In Unity MCP, call `check_compile_errors`.
Expected: no compile errors. (A `.meta` is generated for the new file on import.)

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Combat/BloodPool.cs" "Assets/3 - Scripts/Combat/BloodPool.cs.meta"
git commit -m "feat(combat): BloodPool fade-out component for blood pools"
```

---

## Task 2: `BloodFX` — scene-placed spawn manager

**Files:**
- Create: `Assets/3 - Scripts/Combat/BloodFX.cs`
- Depends on: `BloodPool` (Task 1)

- [ ] **Step 1: Write the manager**

Create `Assets/3 - Scripts/Combat/BloodFX.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Scene-placed singleton that spawns blood VFX from the Piloto Blood VFX
/// Essentials pack. Combat scripts call BloodFX.Instance?.SpawnSpray / SpawnPool
/// — absent manager = silent no-op. Lives under the gameplay scene's managers
/// organizer; scene-placed so it exists on both editor-play and build-load (no
/// EnsureGameplaySingletons seeding needed).
/// </summary>
public class BloodFX : MonoBehaviour
{
    public static BloodFX Instance { get; private set; }

    [Header("Prefabs")]
    [Tooltip("Burst spawned at the bullet hit point (a Blood Splash / Fountain). Non-looping; auto-destroyed after sprayLifetime.")]
    [SerializeField] GameObject sprayPrefab;
    [Tooltip("Pool spawned on the ground when an enemy dies (a Sticky_Splat_*).")]
    [SerializeField] GameObject poolPrefab;

    [Header("Spray (on hit)")]
    [Tooltip("Uniform scale applied to the spawned spray FX.")]
    [SerializeField] float sprayScale = 1f;
    [Tooltip("Seconds before the spray FX is destroyed.")]
    [SerializeField] float sprayLifetime = 3f;
    [Tooltip("Euler offset applied AFTER aiming the spray along the surface normal. Correct for the chosen prefab's emission axis here (e.g. if it emits along +Y rather than +Z). Tune in Play mode.")]
    [SerializeField] Vector3 sprayRotationOffset = Vector3.zero;

    [Header("Pool (on death)")]
    [Tooltip("Uniform scale applied to the spawned pool FX.")]
    [SerializeField] float poolScale = 1f;
    [Tooltip("Seconds the pool stays at full opacity before it begins fading.")]
    [SerializeField] float poolLingerSeconds = 20f;
    [Tooltip("Seconds the pool takes to fade out before it is destroyed.")]
    [SerializeField] float poolFadeSeconds = 3f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Spawn a blood burst at a bullet hit point, aimed back along the surface
    /// normal (toward the shooter).
    /// </summary>
    public void SpawnSpray(Vector3 point, Vector3 normal, Vector3 shotDir)
    {
        if (sprayPrefab == null) return;

        Vector3 dir = normal.sqrMagnitude > 0.0001f ? normal.normalized
                    : (shotDir.sqrMagnitude > 0.0001f ? -shotDir.normalized : Vector3.up);
        Quaternion rot = Quaternion.LookRotation(dir) * Quaternion.Euler(sprayRotationOffset);

        var fx = Instantiate(sprayPrefab, point, rot);
        if (!Mathf.Approximately(sprayScale, 1f)) fx.transform.localScale *= sprayScale;

        // Parent under the nearest planet so floating-origin shifts don't
        // teleport the FX during its short life.
        Transform planet = ResolveNearestPlanet(point);
        if (planet != null) fx.transform.SetParent(planet, worldPositionStays: true);

        DisableColliders(fx);
        Destroy(fx, sprayLifetime);
    }

    /// <summary>
    /// Spawn a blood pool lying flat on the surface at an enemy's feet on death.
    /// </summary>
    public void SpawnPool(Vector3 groundPoint, Vector3 up, Transform planet)
    {
        if (poolPrefab == null) return;

        Vector3 u = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, u);

        var fx = Instantiate(poolPrefab, groundPoint, rot);
        if (!Mathf.Approximately(poolScale, 1f)) fx.transform.localScale *= poolScale;
        if (planet != null) fx.transform.SetParent(planet, worldPositionStays: true);

        DisableColliders(fx);

        var pool = fx.GetComponent<BloodPool>();
        if (pool == null) pool = fx.AddComponent<BloodPool>();
        pool.Init(poolLingerSeconds, poolFadeSeconds);
    }

    static Transform ResolveNearestPlanet(Vector3 point)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        Transform nearest = null;
        float best = float.PositiveInfinity;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float d = (b.Position - point).sqrMagnitude;
            if (d < best) { best = d; nearest = b.transform; }
        }
        return nearest;
    }

    static void DisableColliders(GameObject go)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }
}
```

- [ ] **Step 2: Let Unity import + compile**

In Unity MCP, call `check_compile_errors`.
Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Combat/BloodFX.cs" "Assets/3 - Scripts/Combat/BloodFX.cs.meta"
git commit -m "feat(combat): BloodFX scene manager — spawn spray + pool VFX"
```

---

## Task 3: Wire the spray into `PistolController.TriggerShot`

**Files:**
- Modify: `Assets/3 - Scripts/Pickups/PistolController.cs` (inside the `if (damageable != null)` block, right after the `TakeDamage` call near line 339)

- [ ] **Step 1: Add the spray call**

Find this block in `TriggerShot` (around line 332–347):

```csharp
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                // Knockback BEFORE damage so the kill shot's direction is
                // captured (TakeDamage may trigger Die which reads the cached
                // direction for the ragdoll's backwards momentum).
                damageable.ApplyKnockback(forward, knockbackDistance, knockbackDuration);
                damageable.TakeDamage(damagePerShot);
```

Insert the spray call immediately after `damageable.TakeDamage(damagePerShot);`:

```csharp
                damageable.TakeDamage(damagePerShot);

                // Blood burst out of the entry wound, back toward the shooter.
                BloodFX.Instance?.SpawnSpray(hit.point, hit.normal, forward);
```

Do NOT add any new serialized fields to this class (all tuning lives on `BloodFX`).

- [ ] **Step 2: Compile**

In Unity MCP, call `check_compile_errors`.
Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Pickups/PistolController.cs"
git commit -m "feat(combat): spray blood on pistol hit"
```

---

## Task 4: Wire the pool into `EnemyController.BeginDeath`

**Files:**
- Modify: `Assets/3 - Scripts/Combat/EnemyController.cs` (in `BeginDeath`, right after the `up` vector is computed, ~line 855)

- [ ] **Step 1: Add the pool call**

Find this block in `BeginDeath` (around line 850–855):

```csharp
        // Capture motion-carry inputs BEFORE we touch anything else.
        Vector3 forwardDir = rb.rotation * Vector3.forward;
        Vector3 planetVel  = parentPlanet != null ? parentPlanet.velocity : Vector3.zero;
        Vector3 up         = parentPlanet != null
            ? (rb.position - parentPlanet.Position).normalized
            : Vector3.up;
```

Insert the pool spawn immediately after that `up` assignment:

```csharp
        Vector3 up         = parentPlanet != null
            ? (rb.position - parentPlanet.Position).normalized
            : Vector3.up;

        // Blood pool on the ground at the enemy's feet, oriented to the surface.
        Vector3 bloodFootPoint = rb.position - up * _scaledGroundedOffset;
        BloodFX.Instance?.SpawnPool(bloodFootPoint, up,
            parentPlanet != null ? parentPlanet.transform : null);
```

Do NOT add any new serialized fields to this class.

- [ ] **Step 2: Compile**

In Unity MCP, call `check_compile_errors`.
Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/3 - Scripts/Combat/EnemyController.cs"
git commit -m "feat(combat): blood pool on enemy death"
```

---

## Task 5: Scene setup — create the `BloodFX` manager

**Files:**
- Modify: `Assets/1.6.7.7.7.unity`

Use Unity MCP (coplay). The gameplay scene `Assets/1.6.7.7.7.unity` should already be open (it is the active scene). All steps below are MCP calls.

- [ ] **Step 1: Find the managers organizer**

Call `list_game_objects_in_hierarchy`. Locate the empty organizer that holds the other managers (named like `--- Managers ---`; if no such organizer exists, the `BloodFX` object can sit at the scene root — placement is cosmetic).

- [ ] **Step 2: Create the GameObject + add the component**

Call `create_game_object` with name `BloodFX`. Then `add_component` with component type `BloodFX` on that object. If a managers organizer was found in Step 1, `parent_game_object` the new `BloodFX` under it.

- [ ] **Step 3: Assign the prefab references**

Assign the two `[SerializeField]` prefab fields on the `BloodFX` component via `set_property`:
- `sprayPrefab` → prefab asset GUID `6f3f07dfd16ddd14ea9b6bb0e9f83b40` (`Bloody Fountains/Blood_Fountain_1.prefab`)
- `poolPrefab` → prefab asset GUID `3c08428bc363b8745ac2bea33b68e26b` (`Blood Splats/Sticky_Splat_1.prefab`)

If `set_property` cannot assign an object reference by GUID/path through MCP, fall back to assigning both fields by dragging the prefabs onto the component in the Inspector manually, then continue.

- [ ] **Step 4: Verify the component is wired**

Call `get_game_object_info` on `BloodFX`. Confirm the `BloodFX` component is present and both `sprayPrefab` and `poolPrefab` are non-null.

- [ ] **Step 5: Save the scene**

Call `save_scene`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/1.6.7.7.7.unity"
git commit -m "feat(combat): add BloodFX manager to gameplay scene"
```

---

## Task 6: In-editor verification + tuning

**Files:** none (verification only; any tuning edits the `BloodFX` component values in `Assets/1.6.7.7.7.unity`).

There are no automated tests for this project — verification is a playtest in the editor.

- [ ] **Step 1: Enter Play mode and reach an enemy**

Call `play_game` (or have the user press Play). Use cheats if available (`Universe.cheatsEnabled`) to equip the pistol / spawn an enemy quickly, or play to where enemies spawn.

- [ ] **Step 2: Verify spray-on-hit**

Shoot an enemy (not a kill shot). Confirm:
- A blood burst appears at the bullet impact point (NOT magenta — confirms the Built-in shader path).
- The burst is aimed back toward the player, not into the enemy.
- It disappears within ~`sprayLifetime` (3s).

If the burst points the wrong way (e.g. the fountain emits along +Y, not +Z), set `sprayRotationOffset` on the `BloodFX` component to correct it (commonly `(90, 0, 0)` or `(-90, 0, 0)`). If `Blood_Fountain_1` reads as too much of a vertical gush, swap `sprayPrefab` to `Sticky_Splash_1.prefab` (GUID `2072eced8024d414cbbd3b8251dd8ce3`).

- [ ] **Step 3: Verify pool-on-death**

Kill an enemy. Confirm:
- A blood pool appears on the ground at the body's feet, lying flat on the surface.
- It holds, then fades out smoothly and disappears after ~`poolLingerSeconds + poolFadeSeconds` (~23s).
- No errors in the console (`get_unity_logs` with `show_errors: true`).

If the pool floats, clips into the ground, or is the wrong size, tune `poolScale` and (if needed) nudge the spawn by adjusting feel via `poolScale` — the orientation is already surface-aligned.

- [ ] **Step 4: Verify no floating-origin teleport**

After a kill, walk a long distance (triggering a floating-origin shift) while a pool is still visible. Confirm the pool stays put on the ground (it is parented under the planet, so it should ride the shift cleanly).

- [ ] **Step 5: Exit Play mode; persist any tuning**

Call `stop_game`. If you changed any `BloodFX` values during the playtest, re-apply them to the component in edit mode (Play-mode changes are discarded), `save_scene`, and commit:

```bash
git add "Assets/1.6.7.7.7.unity"
git commit -m "tune(combat): blood VFX feel after playtest"
```

---

## Self-review notes (already reconciled)

- **Spec coverage:** spray-on-hit (Task 3 + `SpawnSpray`), pool-on-death (Task 4 + `SpawnPool`), arcade-juicy defaults + tuning (Tasks 5–6), back-toward-shooter direction (`SpawnSpray` aims along `hit.normal`), linger-then-fade pool (`BloodPool`), pistol-only trigger (only `PistolController` calls `SpawnSpray`), floating-origin safety (planet parenting + Task 6 Step 4), no save changes (none in plan), no MainMenu trap (scene-placed). All covered.
- **Type consistency:** `BloodFX.SpawnSpray(Vector3, Vector3, Vector3)` and `BloodFX.SpawnPool(Vector3, Vector3, Transform)` signatures match their call sites in Tasks 3–4. `BloodPool.Init(float, float)` matches the call in `SpawnPool`. `Instance` is the singleton property used by both call sites.
- **No placeholders:** every code step shows complete code; every verification step states the exact MCP call / expected result.
