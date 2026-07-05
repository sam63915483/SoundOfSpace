# Black Hole Dimensions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Four observation-based dimensions chained between the black hole and the existing Backrooms hub, per `docs/superpowers/specs/2026-07-05-blackhole-dimensions-design.md`.

**Architecture:** One shared observation core (`ObserverState` + `ObservationTracker`). Each dimension is a nearly-empty Unity scene (InteriorPlayer prefab + one `*Controller` MonoBehaviour) whose controller **builds the entire world procedurally in Awake/Start** — floors, walls, lighting, fog, portals, audio. All content is version-controlled C#; scenes carry almost no serialized state. Chaining reuses the existing `LevelPortal`/`PortalManager` plumbing untouched.

**Tech Stack:** Unity 2022.3 Built-in RP, Assembly-CSharp (no asmdefs), coplay Unity MCP for scene creation/verification. **No CLI test runner exists in this repo** — verification is `check_compile_errors` after every script task plus scripted play-mode smoke tests and a final human playthrough.

**Verified facts this plan relies on:**
- `BlackHoleCapture` lives in `Assets/1.6.7.7.7.unity` with serialized `backroomsScene: R1_Backrooms` (scene file line ~405). Changing that string is the ONLY gameplay-scene edit.
- `Assets/1 - samsPrefabs/InteriorPlayer.prefab` is the self-contained interior player rig used by R1_Backrooms — place one per new scene.
- R1_Backrooms already has NEXTLEVEL (action 0 → PoolroomsDemo) and EXIT (action 1 → ReturnToGameplay). PoolroomsDemo has **no** LevelPortal — the "stuck forever" trap already exists. Verify only; do not edit either scene.
- Editor is open with no compile errors; active scene is 1.6.7.7.7.

**Conventions that apply to every script below** (from CLAUDE.md):
- Serialized/tunable fields go at the END of each MonoBehaviour.
- Never call `FindObjectOfType`/`Camera.main` per-frame — cache, lazy-refind only when null.
- `CompareTag`, never `tag ==`. Move rigidbodies with `rb.position`/`MovePosition`, read `rb.position`.
- New `.cs` files: after creating, focus Unity (or `AssetDatabase.Refresh()` via `execute_script`) so `.meta` files generate, then `git add` BOTH the `.cs` and `.meta`.

**Git note:** `Assets/1.6.7.7.7.unity` already has uncommitted user editor tweaks. Task 10 commits it and the message must say it carries those tweaks along.

---

## File Structure

```
Assets/3 - Scripts/Dimensions/          (new folder — all new code)
  ObserverState.cs          static frustum test, per-frame cached planes
  ObservationTracker.cs     grace-window wrapper; observed-state transitions
  DimensionSceneUtil.cs     shared builders: atmosphere, lights, portals, materials, tone clips
  FlickerLight.cs           Perlin light flicker (D1 fluorescents)
  ShiftingMazeController.cs D1 — cell maze, reshuffle-on-unobserved, exit door
  WellFieldController.cs    D2 — dune terrain mesh, relocating wells, true/wrong wells
  WellTeleportTrigger.cs    D2 — wrong-well jump-in teleport
  LongDarkController.cs     D3 — inverse-observed bridge, kill volume, tablet
  WaitingFieldController.cs D4 — monolith that advances while unobserved

Assets/4 - Scenes/Dimensions/           (new folder — 4 near-empty scenes)
  D1_ShiftingHalls.unity  D2_DuneSea.unity  D3_LongDark.unity  D4_WaitingField.unity

Modified:
  Assets/1.6.7.7.7.unity      BlackHoleCapture.backroomsScene → "D1_ShiftingHalls"
  ProjectSettings/EditorBuildSettings.asset   + 4 dimension scenes (via editor script)
```

Scene contents (identical pattern, built in Task 3 and repeated per dimension):
`InteriorPlayer` prefab instance at spawn + `DimensionRoot` GameObject holding that dimension's controller. Everything else is runtime-built.

---

### Task 1: Observation core (`ObserverState`, `ObservationTracker`)

**Files:**
- Create: `Assets/3 - Scripts/Dimensions/ObserverState.cs`
- Create: `Assets/3 - Scripts/Dimensions/ObservationTracker.cs`

- [ ] **Step 1.1: Write `ObserverState.cs`**

```csharp
using UnityEngine;

/// <summary>
/// Per-frame cached "is this on screen?" test for the black-hole observation dimensions.
/// Frustum-only (no occlusion raycasts) by design: "behind a wall but in front of you"
/// counts as observed — forgiving, cheap, and turning around is always unobserved.
/// </summary>
public static class ObserverState
{
    static Camera _cam;                      // cached; Unity-null after scene swap → lazy refind
    static readonly Plane[] _planes = new Plane[6];
    static int _stampedFrame = -1;

    public static Camera Cam { get { Resolve(); return _cam; } }

    static void Resolve()
    {
        if (_cam != null) return;
        var mgr = CameraEffectsManager.Instance;   // same resolution order as BlackHoleCapture
        if (mgr != null && mgr.PlayerCamera != null) _cam = mgr.PlayerCamera;
        if (_cam == null) _cam = Camera.main;
    }

    static bool Refresh()
    {
        Resolve();
        if (_cam == null) return false;
        if (_stampedFrame != Time.frameCount)
        {
            GeometryUtility.CalculateFrustumPlanes(_cam, _planes);
            _stampedFrame = Time.frameCount;
        }
        return true;
    }

    /// <summary>True when the bounds intersect the camera frustum. No camera → false (nothing observed).</summary>
    public static bool IsObserved(Bounds b, float maxDistance = Mathf.Infinity)
    {
        if (!Refresh()) return false;
        Vector3 toB = b.center - _cam.transform.position;
        if (!float.IsInfinity(maxDistance) && toB.sqrMagnitude > maxDistance * maxDistance) return false;
        // Behind-camera early-out: centre behind the camera by more than the bounds radius
        // can never intersect the frustum — skips the 6-plane test for ~half the world.
        if (Vector3.Dot(toB, _cam.transform.forward) < -b.extents.magnitude) return false;
        return GeometryUtility.TestPlanesAABB(_planes, b);
    }
}
```

- [ ] **Step 1.2: Write `ObservationTracker.cs`**

```csharp
using UnityEngine;

/// <summary>
/// Wraps ObserverState.IsObserved with a grace window so screen-edge flicker doesn't
/// strobe state, and exposes the observed→unobserved transition the dimensions key off.
/// Plain class (not a MonoBehaviour): owners call Tick() once per frame.
/// </summary>
public class ObservationTracker
{
    readonly float _grace;
    float _lastObservedTime = float.NegativeInfinity;
    bool _wasEffectivelyObserved;

    public bool WasEverObserved { get; private set; }
    /// <summary>Seconds since last actually on screen (0 while observed).</summary>
    public float TimeUnobserved => Mathf.Max(0f, Time.time - _lastObservedTime);

    public ObservationTracker(float graceSeconds = 0.15f) { _grace = graceSeconds; }

    /// <summary>
    /// Update with current bounds. Returns effective observed state (true within grace).
    /// justLost fires true exactly once when effective state flips observed → unobserved.
    /// </summary>
    public bool Tick(Bounds b, out bool justLost, float maxDistance = Mathf.Infinity)
    {
        if (ObserverState.IsObserved(b, maxDistance))
        {
            _lastObservedTime = Time.time;
            WasEverObserved = true;
        }
        bool observed = Time.time - _lastObservedTime <= _grace;
        justLost = _wasEffectivelyObserved && !observed;
        _wasEffectivelyObserved = observed;
        return observed;
    }

    public void Reset()
    {
        _lastObservedTime = float.NegativeInfinity;
        _wasEffectivelyObserved = false;
        WasEverObserved = false;
    }
}
```

- [ ] **Step 1.3: Import + compile check**

Focus Unity (or run `AssetDatabase.Refresh()` via coplay `execute_script`), then coplay `check_compile_errors`.
Expected: no errors, and `.meta` files exist for both new `.cs` files.

- [ ] **Step 1.4: Commit**

```bash
git add "Assets/3 - Scripts/Dimensions/ObserverState.cs" "Assets/3 - Scripts/Dimensions/ObserverState.cs.meta" \
        "Assets/3 - Scripts/Dimensions/ObservationTracker.cs" "Assets/3 - Scripts/Dimensions/ObservationTracker.cs.meta" \
        "Assets/3 - Scripts/Dimensions.meta" 2>/dev/null; \
git add "Assets/3 - Scripts/Dimensions/"; git commit -m "feat(dimensions): observation core (frustum test + grace tracker)"
```

---

### Task 2: Shared scene-dressing helpers (`DimensionSceneUtil`, `FlickerLight`)

**Files:**
- Create: `Assets/3 - Scripts/Dimensions/DimensionSceneUtil.cs`
- Create: `Assets/3 - Scripts/Dimensions/FlickerLight.cs`

- [ ] **Step 2.1: Write `DimensionSceneUtil.cs`**

```csharp
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared runtime world-building helpers for the black-hole dimension controllers.
/// The dimension scenes are nearly empty; controllers build everything through these.
/// </summary>
public static class DimensionSceneUtil
{
    /// <summary>Fog + flat ambient + camera background for a dimension (no baked lighting anywhere).</summary>
    public static void ApplyAtmosphere(Color ambient, Color fog, float fogDensity, Color background)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = fogDensity > 0f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fog;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.skybox = null;                       // solid colour void
        var cam = ObserverState.Cam;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;   // scene-local interior camera
            cam.backgroundColor = background;
        }
    }

    public static Light CreateDirectionalLight(Color color, float intensity, Vector3 euler, bool shadows)
    {
        var go = new GameObject("DimensionSun");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        go.transform.rotation = Quaternion.Euler(euler);
        return l;
    }

    /// <summary>Invisible trigger box wired to the existing LevelPortal plumbing.</summary>
    public static GameObject CreatePortal(string name, Vector3 pos, Vector3 size,
        LevelPortal.PortalAction action, string targetScene, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        var p = go.AddComponent<LevelPortal>();
        p.action = action;
        p.targetScene = targetScene;
        return go;
    }

    /// <summary>Opaque tinted Standard material. (Standard ships in builds via existing material assets.)</summary>
    public static Material Mat(Color c, float smoothness = 0.1f)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    /// <summary>Emissive tinted Standard material (glows through fog; no light needed).</summary>
    public static Material EmissiveMat(Color c, float emission = 1.5f)
    {
        var m = Mat(c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * emission);
        return m;
    }

    /// <summary>Fade-mode Standard material for the dissolving bridge (alpha animatable).</summary>
    public static Material FadeMat(Color c)
    {
        var m = Mat(c);
        m.SetFloat("_Mode", 2f);                            // Fade
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        return m;
    }

    /// <summary>Primitive with material, position, scale — the basic building block.</summary>
    public static GameObject Block(PrimitiveType type, string name, Vector3 pos, Vector3 scale,
        Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>Looping sine-hum AudioClip generated in code (placeholder until generated SFX pass).</summary>
    public static AudioClip ToneClip(float frequency, float seconds = 2f, float volume = 0.5f)
    {
        int rate = 44100;
        int samples = (int)(rate * seconds);
        var clip = AudioClip.Create("tone" + frequency, samples, 1, rate, false);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * volume;
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>3D looping audio source (rolloff to maxDist) — proximity tells.</summary>
    public static AudioSource LoopingAudio(GameObject on, AudioClip clip, float maxDist, float volume = 1f)
    {
        var src = on.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.maxDistance = maxDist;
        src.volume = volume;
        src.Play();
        return src;
    }
}
```

- [ ] **Step 2.2: Write `FlickerLight.cs`**

```csharp
using UnityEngine;

/// <summary>Perlin-noise intensity flicker for the D1 fluorescent hum-light.</summary>
public class FlickerLight : MonoBehaviour
{
    Light _light;
    float _baseIntensity;
    float _seed;

    void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light != null ? _light.intensity : 1f;
        _seed = Random.value * 100f;
    }

    void Update()
    {
        if (_light == null) return;
        float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed);       // 0..1 wander
        float drop = n < dropThreshold ? dropAmount : 0f;                   // occasional hard dip
        _light.intensity = _baseIntensity * (1f - flickerDepth * n - drop);
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Tooltip("How fast the flicker noise scrolls.")]
    public float flickerSpeed = 8f;
    [Tooltip("Fraction of base intensity the smooth noise can remove (0-1).")]
    [Range(0f, 1f)] public float flickerDepth = 0.25f;
    [Tooltip("Noise below this triggers a hard fluorescent dip.")]
    [Range(0f, 1f)] public float dropThreshold = 0.12f;
    [Tooltip("Extra intensity fraction removed during a hard dip.")]
    [Range(0f, 1f)] public float dropAmount = 0.5f;
}
```

- [ ] **Step 2.3: Import + compile check**

Focus Unity / `AssetDatabase.Refresh()`, then `check_compile_errors`. Expected: clean.

- [ ] **Step 2.4: Commit**

```bash
git add "Assets/3 - Scripts/Dimensions/DimensionSceneUtil.cs"* "Assets/3 - Scripts/Dimensions/FlickerLight.cs"*
git commit -m "feat(dimensions): shared scene builders + flicker light"
```

---

### Task 3: D1 Shifting Halls — controller script

**Files:**
- Create: `Assets/3 - Scripts/Dimensions/ShiftingMazeController.cs`

Cell maze on a grid. Each cell owns its NORTH and EAST edge walls (so no wall is owned
twice). A cell whose bounds flip observed→unobserved (and isn't the player's cell)
re-rolls its seed and rebuilds instantly — off-screen, nobody sees the pop. Re-rolls
have a small chance to place the single global exit door; once the door has been SEEN,
losing sight of it past the grace window despawns it.

- [ ] **Step 3.1: Write `ShiftingMazeController.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// D1 "Shifting Halls": endless corridor maze where any cell that leaves your view
/// reshuffles its walls. Exit doors appear on reshuffles and vanish if you look away
/// after spotting one. Builds the whole world (floor, ceiling, light, fog) at runtime.
/// </summary>
public class ShiftingMazeController : MonoBehaviour
{
    class Cell
    {
        public Vector2Int coord;
        public int seed;
        public readonly List<GameObject> walls = new List<GameObject>();
        public readonly ObservationTracker tracker = new ObservationTracker();
    }

    readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
    readonly Stack<GameObject> _wallPool = new Stack<GameObject>();
    readonly List<Vector2Int> _toDespawn = new List<Vector2Int>();

    Material _wallMat, _floorMat, _ceilMat;
    Transform _root;
    GameObject _floor, _ceiling, _lamp;
    GameObject _door;                        // single global exit door (built lazily, SetActive toggled)
    Vector2Int _doorCell;
    ObservationTracker _doorTracker;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _wallMat  = DimensionSceneUtil.Mat(new Color(0.75f, 0.70f, 0.50f));
        _floorMat = DimensionSceneUtil.Mat(new Color(0.45f, 0.42f, 0.30f));
        _ceilMat  = DimensionSceneUtil.Mat(new Color(0.80f, 0.80f, 0.75f));

        // Static planes: the floor can never despawn (spec safety rule). They follow the
        // camera in grid-snapped steps so tiling never visibly swims.
        _floor   = DimensionSceneUtil.Block(PrimitiveType.Cube, "Floor",
            new Vector3(0f, -0.25f, 0f), new Vector3(220f, 0.5f, 220f), _floorMat, _root);
        _ceiling = DimensionSceneUtil.Block(PrimitiveType.Cube, "Ceiling",
            new Vector3(0f, wallHeight + 0.25f, 0f), new Vector3(220f, 0.5f, 220f), _ceilMat, _root);

        // One warm flickering lamp that follows the player — no per-cell lights.
        _lamp = new GameObject("HallLamp");
        _lamp.transform.SetParent(_root, false);
        var l = _lamp.AddComponent<Light>();
        l.type = LightType.Point; l.range = 20f; l.intensity = 1.4f;
        l.color = new Color(1f, 0.93f, 0.75f);
        _lamp.AddComponent<FlickerLight>();

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(120f, 2f, 0.06f), 60f, 1f);
    }

    void Update()
    {
        var cam = ObserverState.Cam;
        if (cam == null) return;
        if (!_atmosApplied)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.28f, 0.26f, 0.18f),
                fog: new Color(0.55f, 0.50f, 0.33f), fogDensity: 0.045f,
                background: new Color(0.35f, 0.32f, 0.20f));
            _atmosApplied = true;
        }

        Vector3 p = cam.transform.position;
        var playerCell = new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(p.z / cellSize));

        // Floor/ceiling/lamp follow in snapped steps.
        Vector3 snapped = new Vector3(playerCell.x * cellSize, 0f, playerCell.y * cellSize);
        _floor.transform.position   = snapped + Vector3.up * -0.25f;
        _ceiling.transform.position = snapped + Vector3.up * (wallHeight + 0.25f);
        _lamp.transform.position    = p + Vector3.up * 1.2f;

        // Spawn window around the player; despawn what's left behind.
        for (int dx = -radiusCells; dx <= radiusCells; dx++)
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                var c = new Vector2Int(playerCell.x + dx, playerCell.y + dz);
                if (!_cells.ContainsKey(c)) SpawnCell(c);
            }
        _toDespawn.Clear();
        foreach (var kv in _cells)
            if (Mathf.Max(Mathf.Abs(kv.Key.x - playerCell.x), Mathf.Abs(kv.Key.y - playerCell.y)) > radiusCells + 1)
                _toDespawn.Add(kv.Key);
        foreach (var c in _toDespawn) DespawnCell(c);

        // The rule: a cell you stop observing reshuffles (never the one you're standing in).
        foreach (var kv in _cells)
        {
            Cell cell = kv.Value;
            cell.tracker.Tick(CellBounds(cell.coord), out bool justLost, observeMaxDistance);
            if (justLost && cell.coord != playerCell) Reroll(cell);
        }

        // Exit door: once seen, look away past the grace window and it's gone.
        if (_door != null && _door.activeSelf)
        {
            var b = new Bounds(_door.transform.position + Vector3.up * 1.5f, new Vector3(2.5f, 3.5f, 2.5f));
            _doorTracker.Tick(b, out bool doorLost, float.PositiveInfinity);
            if (_doorTracker.WasEverObserved && doorLost) DespawnDoor();
        }
    }

    Bounds CellBounds(Vector2Int c) =>
        new Bounds(new Vector3(c.x * cellSize, wallHeight * 0.5f, c.y * cellSize),
                   new Vector3(cellSize, wallHeight, cellSize));

    void SpawnCell(Vector2Int coord)
    {
        var cell = new Cell { coord = coord, seed = Hash(coord.x, coord.y, worldSeed) };
        _cells[coord] = cell;
        BuildWalls(cell);
    }

    void DespawnCell(Vector2Int coord)
    {
        var cell = _cells[coord];
        ReturnWalls(cell);
        if (_door != null && _door.activeSelf && _doorCell == coord) DespawnDoor();
        _cells.Remove(coord);
    }

    void Reroll(Cell cell)
    {
        cell.seed = unchecked(cell.seed * 7919 + 12345);
        BuildWalls(cell);
        cell.tracker.Reset();
        // A fresh layout may carry the exit — but only one door exists at a time.
        if ((_door == null || !_door.activeSelf) && Rand01(cell.seed, 7) < exitDoorChance)
            SpawnDoor(cell.coord, cell.seed);
    }

    void BuildWalls(Cell cell)
    {
        ReturnWalls(cell);
        float s = cellSize, cx = cell.coord.x * s, cz = cell.coord.y * s;
        // North + East edges only — each wall has exactly one owner cell, and density
        // stays walkable (≤2 walls per owned pair). Dead ends self-heal: look away and
        // the blocking cell reshuffles.
        if (Rand01(cell.seed, 1) < wallChance)
            PlaceWall(cell, new Vector3(cx, wallHeight * 0.5f, cz + s * 0.5f), new Vector3(s + wallThickness, wallHeight, wallThickness));
        if (Rand01(cell.seed, 2) < wallChance)
            PlaceWall(cell, new Vector3(cx + s * 0.5f, wallHeight * 0.5f, cz), new Vector3(wallThickness, wallHeight, s + wallThickness));
    }

    void PlaceWall(Cell cell, Vector3 pos, Vector3 scale)
    {
        GameObject w = _wallPool.Count > 0 ? _wallPool.Pop() : NewWall();
        w.SetActive(true);
        w.transform.position = pos;
        w.transform.localScale = scale;
        cell.walls.Add(w);
    }

    GameObject NewWall() =>
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Wall", Vector3.zero, Vector3.one, _wallMat, _root);

    void ReturnWalls(Cell cell)
    {
        foreach (var w in cell.walls) { w.SetActive(false); _wallPool.Push(w); }
        cell.walls.Clear();
    }

    void SpawnDoor(Vector2Int coord, int seed)
    {
        if (_door == null) BuildDoor();
        _doorCell = coord;
        _doorTracker.Reset();
        float yaw = 90f * (Mathf.Abs(Hash(coord.x, coord.y, seed)) % 4);
        _door.transform.SetPositionAndRotation(
            new Vector3(coord.x * cellSize, 0f, coord.y * cellSize), Quaternion.Euler(0f, yaw, 0f));
        _door.SetActive(true);
    }

    void DespawnDoor() { _door.SetActive(false); _doorTracker.Reset(); }

    void BuildDoor()
    {
        _doorTracker = new ObservationTracker();
        _door = new GameObject("ExitDoor");
        _door.transform.SetParent(_root, false);
        var frame = DimensionSceneUtil.Mat(new Color(0.1f, 0.1f, 0.12f));
        var glow  = DimensionSceneUtil.EmissiveMat(new Color(0.75f, 0.95f, 1f), 2.5f);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostL",  new Vector3(-0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frame, _door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "PostR",  new Vector3( 0.8f, 1.5f, 0f), new Vector3(0.3f, 3f, 0.3f), frame, _door.transform);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 3.05f, 0f),   new Vector3(1.9f, 0.3f, 0.3f), frame, _door.transform);
        var pane = DimensionSceneUtil.Block(PrimitiveType.Cube, "Glow", new Vector3(0f, 1.5f, 0f), new Vector3(1.3f, 2.9f, 0.05f), glow, _door.transform);
        Object.Destroy(pane.GetComponent<Collider>());      // walk THROUGH the light
        DimensionSceneUtil.CreatePortal("ToD2", _door.transform.position + Vector3.up * 1.5f,
            new Vector3(1.3f, 2.9f, 0.6f), LevelPortal.PortalAction.EnterInterior, nextScene, _door.transform);
    }

    static int Hash(int x, int y, int seed) =>
        unchecked(x * 73856093 ^ y * 19349663 ^ seed * 83492791);

    static float Rand01(int seed, int salt) =>
        (Mathf.Abs(unchecked(seed * (int)2654435761 + salt * 40503)) % 10000) / 10000f;

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Maze")]
    [Tooltip("Cell size in metres (corridor pitch).")]
    public float cellSize = 6f;
    public float wallHeight = 4f;
    public float wallThickness = 0.4f;
    [Tooltip("Cells kept alive in each direction around the player.")]
    public int radiusCells = 6;
    [Tooltip("Chance each owned edge (N and E per cell) carries a wall.")]
    [Range(0f, 1f)] public float wallChance = 0.55f;
    [Tooltip("Deterministic world seed (cells reshuffle away from it over time).")]
    public int worldSeed = 1337;
    [Tooltip("Beyond this distance a cell counts as unobserved even on screen (fog hides it anyway).")]
    public float observeMaxDistance = 80f;

    [Header("Exit")]
    [Tooltip("Chance a reshuffled cell contains the exit door (only one exists at a time).")]
    [Range(0f, 1f)] public float exitDoorChance = 0.04f;
    [Tooltip("Scene the exit door leads to.")]
    public string nextScene = "D2_DuneSea";
}
```

Note: `Rand01` casts the multiplier through `(int)` because `2654435761` overflows Int32 — the
`unchecked` cast is intentional hash mixing.

- [ ] **Step 3.2: Import + compile check**

Focus Unity / `AssetDatabase.Refresh()`, then `check_compile_errors`. Expected: clean.

- [ ] **Step 3.3: Commit**

```bash
git add "Assets/3 - Scripts/Dimensions/ShiftingMazeController.cs"*
git commit -m "feat(dimensions): D1 Shifting Halls controller (reshuffle-on-unobserved maze + vanishing exit door)"
```

---

### Task 4: D1 scene + build settings + smoke test

**Files:**
- Create: `Assets/4 - Scenes/Dimensions/D1_ShiftingHalls.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset` (via editor script — never hand-edit)

- [ ] **Step 4.1: Create the scene** (coplay MCP)

1. `create_scene` → `Assets/4 - Scenes/Dimensions/D1_ShiftingHalls.unity`. If the tool can't create the folder, run `execute_script`: `AssetDatabase.CreateFolder("Assets/4 - Scenes", "Dimensions");` first.
2. `place_asset_in_scene` → `Assets/1 - samsPrefabs/InteriorPlayer.prefab` at position `(0, 1.5, 0)`.
3. `create_game_object` → name `DimensionRoot` at origin, then `add_component` → `ShiftingMazeController` on it.
4. Delete any default template objects (`Main Camera`, `Directional Light`) via `delete_game_object` — InteriorPlayer carries the camera; the controller builds lighting.
5. `save_scene`.

- [ ] **Step 4.2: Add scene to Build Settings** (coplay `execute_script`)

```csharp
using UnityEditor;
using System.Collections.Generic;
var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
string p = "Assets/4 - Scenes/Dimensions/D1_ShiftingHalls.unity";
if (!list.Exists(s => s.path == p)) list.Add(new EditorBuildSettingsScene(p, true));
EditorBuildSettings.scenes = list.ToArray();
Debug.Log("[plan] build settings now has " + list.Count + " scenes");
```

- [ ] **Step 4.3: Play-mode smoke test** (coplay MCP)

1. `play_game`, wait ~5 s.
2. `get_unity_logs` — expected: NO errors/exceptions (a NullReference from ObserverState/camera resolution would show here).
3. `list_game_objects_in_hierarchy` nameFilter `Wall` — expected: dozens of active pooled walls.
4. `execute_script` (play mode): log one far-behind wall position, rotate the player root 180°, wait a second, log again — positions must differ (reshuffle proof).
5. `stop_game`.

- [ ] **Step 4.4: Commit**

```bash
git add "Assets/4 - Scenes/Dimensions/" "ProjectSettings/EditorBuildSettings.asset"
git commit -m "feat(dimensions): D1 Shifting Halls scene, wired into build settings"
```

---

### Task 5: D2 Dune Sea — controller scripts

**Files:**
- Create: `Assets/3 - Scripts/Dimensions/WellFieldController.cs`
- Create: `Assets/3 - Scripts/Dimensions/WellTeleportTrigger.cs`

Static procedural dune terrain (deterministic Perlin height function — relocation never
needs raycasts). ~8 primitive-built stone wells orbit the player; any well that flips
observed→unobserved relocates to an off-screen spot. Well 0 is the TRUE well (looping
hum + faint interior glow) and holds the portal to D3; the rest teleport you to a random
distant dune.

- [ ] **Step 5.1: Write `WellTeleportTrigger.cs`**

```csharp
using UnityEngine;

/// <summary>Wrong-well punishment: jumping in teleports you to a random distant dune.</summary>
public class WellTeleportTrigger : MonoBehaviour
{
    [HideInInspector] public WellFieldController owner;
    bool _cooling;

    void OnTriggerEnter(Collider other)
    {
        if (_cooling || owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _cooling = true;
        owner.TeleportPlayerToRandomDune();
        Invoke(nameof(EndCooldown), 1f);     // debounce multi-collider player rigs
    }

    void EndCooldown() { _cooling = false; }
}
```

- [ ] **Step 5.2: Write `WellFieldController.cs`**

```csharp
using UnityEngine;

/// <summary>
/// D2 "Dune Sea": static procedural dunes; wells relocate whenever they leave your view.
/// One true well (audio + glow tells) exits to D3 — the others dump you somewhere else.
/// </summary>
public class WellFieldController : MonoBehaviour
{
    Transform _root;
    Transform[] _wells;
    ObservationTracker[] _trackers;
    Material _sandMat, _stoneMat, _holeMat;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        _sandMat  = DimensionSceneUtil.Mat(new Color(0.85f, 0.72f, 0.45f), 0.05f);
        _stoneMat = DimensionSceneUtil.Mat(new Color(0.45f, 0.42f, 0.38f), 0.05f);
        _holeMat  = DimensionSceneUtil.Mat(new Color(0.02f, 0.02f, 0.03f), 0f);

        BuildTerrain();
        DimensionSceneUtil.CreateDirectionalLight(new Color(1f, 0.95f, 0.8f), 1.25f, new Vector3(55f, 40f, 0f), true);

        _wells = new Transform[wellCount];
        _trackers = new ObservationTracker[wellCount];
        for (int i = 0; i < wellCount; i++)
        {
            _wells[i] = BuildWell(i == 0).transform;
            _trackers[i] = new ObservationTracker();
            PlaceWell(i, Vector3.zero, initial: true);
        }
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.55f, 0.48f, 0.36f),
                fog: new Color(0.88f, 0.78f, 0.55f), fogDensity: 0.012f,
                background: new Color(0.92f, 0.84f, 0.62f));
            _atmosApplied = true;
        }
        var cam = ObserverState.Cam;
        if (cam == null) return;

        for (int i = 0; i < wellCount; i++)
        {
            var b = new Bounds(_wells[i].position + Vector3.up * 0.6f, new Vector3(3.5f, 2.5f, 3.5f));
            _trackers[i].Tick(b, out bool justLost, float.PositiveInfinity);
            if (justLost) PlaceWell(i, cam.transform.position, initial: false);
        }
    }

    /// <summary>Deterministic dune height — same function shapes the mesh and re-seats wells.</summary>
    public float DuneHeight(float x, float z)
    {
        float f1 = 1f / 70f, f2 = 1f / 23f;
        return Mathf.PerlinNoise(x * f1 + 31.7f, z * f1 + 12.3f) * duneAmplitude
             + Mathf.PerlinNoise(x * f2 + 71.1f, z * f2 + 47.9f) * (duneAmplitude * 0.25f);
    }

    void BuildTerrain()
    {
        int n = 128;
        float half = terrainSize * 0.5f, step = terrainSize / n;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        for (int z = 0, i = 0; z <= n; z++)
            for (int x = 0; x <= n; x++, i++)
            {
                float wx = x * step - half, wz = z * step - half;
                verts[i] = new Vector3(wx, DuneHeight(wx, wz), wz);
                uvs[i] = new Vector2(x / (float)n, z / (float)n);
            }
        var tris = new int[n * n * 6];
        for (int z = 0, t = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
            }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.triangles = tris; mesh.uv = uvs;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var go = new GameObject("Dunes");
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _sandMat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    GameObject BuildWell(bool isTrue)
    {
        var well = new GameObject(isTrue ? "Well_TRUE" : "Well");
        well.transform.SetParent(_root, false);
        // Octagon rim of stone blocks — low enough to hop over into the mouth.
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            var block = DimensionSceneUtil.Block(PrimitiveType.Cube, "Rim",
                new Vector3(Mathf.Cos(a) * 1.3f, 0.45f, Mathf.Sin(a) * 1.3f),
                new Vector3(1.05f, 0.9f, 0.5f), _stoneMat, well.transform);
            block.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
        }
        // The "hole": a black disc just below the rim.
        var disc = DimensionSceneUtil.Block(PrimitiveType.Cylinder, "Hole",
            new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.03f, 2.2f), _holeMat, well.transform);
        Object.Destroy(disc.GetComponent<Collider>());

        if (isTrue)
        {
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(well.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.range = 6f; l.intensity = 1.6f;
            l.color = new Color(1f, 0.75f, 0.35f);
            DimensionSceneUtil.LoopingAudio(well, DimensionSceneUtil.ToneClip(528f, 2f, 0.35f), 130f, 1f);
            DimensionSceneUtil.CreatePortal("ToD3", well.transform.position + Vector3.up * 0.5f,
                new Vector3(1.7f, 0.8f, 1.7f), LevelPortal.PortalAction.EnterInterior, nextScene, well.transform);
        }
        else
        {
            var trig = new GameObject("WrongWell");
            trig.transform.SetParent(well.transform, false);
            trig.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(1.7f, 0.8f, 1.7f);
            trig.AddComponent<WellTeleportTrigger>().owner = this;
        }
        return well;
    }

    void PlaceWell(int i, Vector3 aroundPos, bool initial)
    {
        // Sample until the spot is off-screen (max 8 tries — relocations must not pop
        // into view). Initial placement scatters around the origin instead.
        Vector3 best = _wells[i].position;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = Random.Range(relocateMinDistance, relocateMaxDistance);
            Vector3 c = initial ? Vector3.zero : aroundPos;
            float x = c.x + Mathf.Cos(a) * d, z = c.z + Mathf.Sin(a) * d;
            x = Mathf.Clamp(x, -terrainSize * 0.45f, terrainSize * 0.45f);
            z = Mathf.Clamp(z, -terrainSize * 0.45f, terrainSize * 0.45f);
            best = new Vector3(x, DuneHeight(x, z), z);
            if (initial || !ObserverState.IsObserved(new Bounds(best + Vector3.up, Vector3.one * 4f)))
                break;
        }
        _wells[i].position = best;
        _trackers[i].Reset();
    }

    /// <summary>Wrong-well punishment: dumped on a random distant dune, no damage.</summary>
    public void TeleportPlayerToRandomDune()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        float a = Random.value * Mathf.PI * 2f;
        float d = Random.Range(120f, 200f);
        Vector3 p = _player.Rigidbody.position;
        float x = Mathf.Clamp(p.x + Mathf.Cos(a) * d, -terrainSize * 0.45f, terrainSize * 0.45f);
        float z = Mathf.Clamp(p.z + Mathf.Sin(a) * d, -terrainSize * 0.45f, terrainSize * 0.45f);
        _player.Rigidbody.position = new Vector3(x, DuneHeight(x, z) + 2f, z);
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Terrain")]
    [Tooltip("Square dune mesh edge length (metres). Static — never reshuffles.")]
    public float terrainSize = 800f;
    [Tooltip("Main dune height (metres).")]
    public float duneAmplitude = 8f;

    [Header("Wells")]
    [Tooltip("Total wells including the one true well.")]
    public int wellCount = 8;
    [Tooltip("Min distance from the player a relocating well may land.")]
    public float relocateMinDistance = 40f;
    [Tooltip("Max distance from the player a relocating well may land.")]
    public float relocateMaxDistance = 150f;
    [Tooltip("Scene the true well leads to.")]
    public string nextScene = "D3_LongDark";
}
```

- [ ] **Step 5.3: Import + compile check**

Focus Unity / `AssetDatabase.Refresh()`, then `check_compile_errors`. Expected: clean.

- [ ] **Step 5.4: Commit**

```bash
git add "Assets/3 - Scripts/Dimensions/WellFieldController.cs"* "Assets/3 - Scripts/Dimensions/WellTeleportTrigger.cs"*
git commit -m "feat(dimensions): D2 Dune Sea controller (relocating wells, true-well exit)"
```

---

### Task 6: D2 scene + build settings + smoke test

**Files:**
- Create: `Assets/4 - Scenes/Dimensions/D2_DuneSea.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset` (via editor script)

- [ ] **Step 6.1: Create the scene** (coplay MCP — same recipe as Task 4)

1. `create_scene` → `Assets/4 - Scenes/Dimensions/D2_DuneSea.unity`.
2. `place_asset_in_scene` → `Assets/1 - samsPrefabs/InteriorPlayer.prefab` at `(0, 20, 0)` — drops onto the dunes on load (dune height maxes ~10 m).
3. `create_game_object` `DimensionRoot`, `add_component` `WellFieldController`.
4. Delete default `Main Camera` / `Directional Light` if the template added them.
5. `save_scene`.

- [ ] **Step 6.2: Add to Build Settings** (coplay `execute_script` — same snippet as Step 4.2 with `D2_DuneSea.unity`)

- [ ] **Step 6.3: Play-mode smoke test**

1. `play_game`, wait ~5 s; `get_unity_logs` → no errors; player must NOT be falling forever (log its y via `execute_script` twice — stable means it landed on the MeshCollider).
2. `list_game_objects_in_hierarchy` nameFilter `Well` → 8 wells, one named `Well_TRUE`.
3. `stop_game`.

- [ ] **Step 6.4: Commit**

```bash
git add "Assets/4 - Scenes/Dimensions/" "ProjectSettings/EditorBuildSettings.asset"
git commit -m "feat(dimensions): D2 Dune Sea scene"
```

---

### Task 7: D3 Long Dark — controller script

**Files:**
- Create: `Assets/3 - Scripts/Dimensions/LongDarkController.cs`

The rule inverts: bridge segments are SOLID while unobserved and dissolve (fade + drop
collider after a short delay) while observed. Cross by walking backwards toward the
beacon. Fall → kill volume → respawn at the start platform. Start/end platforms always
solid. A carved tablet teaches the inversion.

- [ ] **Step 7.1: Write `LongDarkController.cs`**

```csharp
using UnityEngine;

/// <summary>
/// D3 "Long Dark": a void crossing where the bridge is only solid while UNobserved.
/// Observed segments fade out and drop their collider after a short delay; unobserved
/// segments are instantly solid again. Falling respawns you at the start platform.
/// </summary>
public class LongDarkController : MonoBehaviour
{
    class Segment
    {
        public Transform tf;
        public Renderer rend;
        public Collider col;
        public MaterialPropertyBlock mpb;
        public ObservationTracker tracker = new ObservationTracker();
        public float alpha = 1f;
        public float observedSince = -1f;    // Time.time when it became observed; -1 while unobserved
    }

    Segment[] _segments;
    Transform _root;
    Vector3 _respawnPoint;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _atmosApplied;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _root = transform;
        var platMat   = DimensionSceneUtil.Mat(new Color(0.15f, 0.15f, 0.2f));
        var bridgeMat = DimensionSceneUtil.FadeMat(new Color(0.55f, 0.6f, 0.75f, 1f));
        var beaconMat = DimensionSceneUtil.EmissiveMat(new Color(1f, 0.85f, 0.4f), 3f);

        // Start + end platforms (always solid).
        DimensionSceneUtil.Block(PrimitiveType.Cube, "StartPlatform",
            new Vector3(0f, -0.5f, 0f), new Vector3(14f, 1f, 14f), platMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "EndPlatform",
            new Vector3(0f, -0.5f, bridgeLength + 14f), new Vector3(14f, 1f, 14f), platMat, _root);
        _respawnPoint = new Vector3(0f, 1.5f, 0f);

        // Beacon tower on the far platform — the light you walk backwards toward.
        DimensionSceneUtil.Block(PrimitiveType.Cube, "BeaconTower",
            new Vector3(0f, 6f, bridgeLength + 17f), new Vector3(2f, 12f, 2f), platMat, _root);
        DimensionSceneUtil.Block(PrimitiveType.Sphere, "BeaconLightBall",
            new Vector3(0f, 13f, bridgeLength + 17f), Vector3.one * 2.5f, beaconMat, _root);
        var lightGo = new GameObject("BeaconLight");
        lightGo.transform.SetParent(_root, false);
        lightGo.transform.position = new Vector3(0f, 13f, bridgeLength + 17f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 60f; l.intensity = 2.5f;
        l.color = new Color(1f, 0.85f, 0.4f);

        // The bridge: segments from the start platform edge to the end platform.
        int count = Mathf.CeilToInt(bridgeLength / segmentLength);
        _segments = new Segment[count];
        for (int i = 0; i < count; i++)
        {
            float z = 7f + segmentLength * 0.5f + i * segmentLength;
            var go = DimensionSceneUtil.Block(PrimitiveType.Cube, "BridgeSeg" + i,
                new Vector3(0f, -0.15f, z), new Vector3(2.4f, 0.3f, segmentLength * 0.98f), bridgeMat, _root);
            _segments[i] = new Segment
            {
                tf = go.transform,
                rend = go.GetComponent<Renderer>(),
                col = go.GetComponent<Collider>(),
                mpb = new MaterialPropertyBlock(),
            };
        }

        // Kill volume — falling into the void respawns you at the start.
        var kill = new GameObject("KillVolume");
        kill.transform.SetParent(_root, false);
        kill.transform.position = new Vector3(0f, -30f, bridgeLength * 0.5f);
        var kb = kill.AddComponent<BoxCollider>();
        kb.isTrigger = true; kb.size = new Vector3(400f, 10f, 400f);
        kill.AddComponent<LongDarkKillVolume>().owner = this;

        // Teaching tablet at the start platform.
        BuildTablet(new Vector3(2.5f, 1.2f, 4f));

        // Exit portal on the far platform.
        DimensionSceneUtil.CreatePortal("ToD4", new Vector3(0f, 1.5f, bridgeLength + 13f),
            new Vector3(10f, 3f, 2f), LevelPortal.PortalAction.EnterInterior, nextScene, _root);

        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(55f, 2f, 0.08f), 500f, 1f);
    }

    void BuildTablet(Vector3 pos)
    {
        var slab = DimensionSceneUtil.Block(PrimitiveType.Cube, "Tablet",
            pos, new Vector3(2.4f, 1.6f, 0.15f), DimensionSceneUtil.Mat(new Color(0.25f, 0.25f, 0.3f)), _root);
        slab.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // face the spawn
        var textGo = new GameObject("TabletText");
        textGo.transform.SetParent(slab.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.51f);
        textGo.transform.localScale = Vector3.one * 0.05f;
        var tmp = textGo.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "IT HOLDS ONLY\nWHAT YOU CANNOT SEE";
        tmp.fontSize = 60f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.9f, 0.9f, 1f);
        var rt = tmp.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(44f, 28f);
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.06f, 0.06f, 0.1f),
                fog: Color.black, fogDensity: 0.004f,
                background: new Color(0.01f, 0.01f, 0.03f));
            _atmosApplied = true;
        }

        // INVERTED rule: observed → dissolve + (after a beat) drop collider; unobserved → solid.
        foreach (var s in _segments)
        {
            var b = new Bounds(s.tf.position, new Vector3(3f, 1.5f, segmentLength + 0.5f));
            bool observed = s.tracker.Tick(b, out _, float.PositiveInfinity);

            if (observed && s.observedSince < 0f) s.observedSince = Time.time;
            if (!observed) s.observedSince = -1f;

            float targetAlpha = observed ? 0.08f : 1f;               // ghost hint, never fully invisible
            s.alpha = Mathf.MoveTowards(s.alpha, targetAlpha, Time.deltaTime / 0.25f);
            s.mpb.SetColor(ColorId, new Color(0.55f, 0.6f, 0.75f, s.alpha));
            s.rend.SetPropertyBlock(s.mpb);

            // Collider drops only after the delay — a quick glance isn't instant doom.
            s.col.enabled = !(observed && Time.time - s.observedSince > colliderDropDelay);
        }
    }

    /// <summary>Kill-volume respawn: back to the start platform, velocity zeroed. No damage.</summary>
    public void RespawnPlayer()
    {
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;
        _player.Rigidbody.position = _respawnPoint;
        _player.Rigidbody.velocity = Vector3.zero;
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Bridge")]
    [Tooltip("Gap between the platforms (metres).")]
    public float bridgeLength = 150f;
    [Tooltip("Length of one solid-while-unseen segment.")]
    public float segmentLength = 3f;
    [Tooltip("Seconds a segment stays walkable after you start looking at it.")]
    public float colliderDropDelay = 0.2f;

    [Header("Exit")]
    [Tooltip("Scene the beacon platform leads to.")]
    public string nextScene = "D4_WaitingField";
}

/// <summary>Void-fall trigger that hands the player back to the start platform.</summary>
public class LongDarkKillVolume : MonoBehaviour
{
    [HideInInspector] public LongDarkController owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner == null) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;
        owner.RespawnPlayer();
    }
}
```

- [ ] **Step 7.2: Import + compile check**

Focus Unity / `AssetDatabase.Refresh()`, then `check_compile_errors`. Expected: clean.
(TMPro namespace is available — the project already uses TextMeshPro.)

- [ ] **Step 7.3: Commit**

```bash
git add "Assets/3 - Scripts/Dimensions/LongDarkController.cs"*
git commit -m "feat(dimensions): D3 Long Dark controller (inverse-observed bridge + kill volume)"
```

---

### Task 8: D3 scene + build settings + smoke test

**Files:**
- Create: `Assets/4 - Scenes/Dimensions/D3_LongDark.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset` (via editor script)

- [ ] **Step 8.1: Create the scene** (same recipe)

1. `create_scene` → `Assets/4 - Scenes/Dimensions/D3_LongDark.unity`.
2. `place_asset_in_scene` → InteriorPlayer at `(0, 1.5, 0)` (on the start platform).
3. `create_game_object` `DimensionRoot`, `add_component` `LongDarkController`.
4. Delete template camera/light. `save_scene`.

- [ ] **Step 8.2: Add to Build Settings** (same snippet, `D3_LongDark.unity`)

- [ ] **Step 8.3: Play-mode smoke test**

1. `play_game`; `get_unity_logs` → no errors.
2. `execute_script` (play mode): confirm the inversion —
   - find `BridgeSeg5`, log `collider.enabled` while the camera looks AT it (rotate player root to face +z): expect `false` after ~0.4 s;
   - rotate the player root 180° away, wait, log again: expect `true`.
3. `execute_script`: set player rigidbody position to `(0, -28, 75)` (inside the kill volume) → next frame player y should be back near 1.5 (respawn works).
4. `stop_game`.

- [ ] **Step 8.4: Commit**

```bash
git add "Assets/4 - Scenes/Dimensions/" "ProjectSettings/EditorBuildSettings.asset"
git commit -m "feat(dimensions): D3 Long Dark scene"
```

---

### Task 9: D4 Waiting Field — controller script

**Files:**
- Create: `Assets/3 - Scripts/Dimensions/WaitingFieldController.cs`

Inverted Weeping Angel with no enemy: a monolith door 200 m away that only advances
while UNobserved (frozen solid the moment it's on screen). A low rumble scales with
proximity. Its doorway leads to R1_Backrooms. Walking the 200 m yourself is a valid
impatient-player strategy — both paths are allowed.

- [ ] **Step 9.1: Write `WaitingFieldController.cs`**

```csharp
using UnityEngine;

/// <summary>
/// D4 "Waiting Field": endless quiet grassland; the exit is a monolith door that only
/// moves toward you while you are NOT looking at it. Walk through its doorway to leave.
/// </summary>
public class WaitingFieldController : MonoBehaviour
{
    Transform _root;
    Rigidbody _monolithRb;
    Transform _monolith;
    ObservationTracker _tracker = new ObservationTracker();
    AudioSource _rumble;
    PlayerController _player;
    int _playerRefindCooldown;
    bool _observedNow = true;               // start frozen until proven unobserved
    bool _atmosApplied;

    void Awake()
    {
        _root = transform;
        var groundMat = DimensionSceneUtil.Mat(new Color(0.18f, 0.26f, 0.16f), 0.05f);
        var stoneMat  = DimensionSceneUtil.Mat(new Color(0.08f, 0.07f, 0.09f), 0.3f);
        var glowMat   = DimensionSceneUtil.EmissiveMat(new Color(0.9f, 0.55f, 0.25f), 2f);

        DimensionSceneUtil.Block(PrimitiveType.Cube, "Ground",
            new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f), groundMat, _root);
        DimensionSceneUtil.CreateDirectionalLight(new Color(0.75f, 0.65f, 0.8f), 0.7f, new Vector3(20f, -30f, 0f), true);

        // Monolith: two side slabs + lintel forming a walk-through doorway, on a kinematic RB.
        var mono = new GameObject("Monolith");
        mono.transform.SetParent(_root, false);
        _monolith = mono.transform;
        _monolithRb = mono.AddComponent<Rigidbody>();
        _monolithRb.isKinematic = true;
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SlabL", new Vector3(-1.65f, 4.5f, 0f), new Vector3(1.9f, 9f, 1.5f), stoneMat, _monolith);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "SlabR", new Vector3( 1.65f, 4.5f, 0f), new Vector3(1.9f, 9f, 1.5f), stoneMat, _monolith);
        DimensionSceneUtil.Block(PrimitiveType.Cube, "Lintel", new Vector3(0f, 7.5f, 0f), new Vector3(5.2f, 3f, 1.5f), stoneMat, _monolith);
        var glow = DimensionSceneUtil.Block(PrimitiveType.Cube, "DoorGlow", new Vector3(0f, 3f, 0f), new Vector3(1.35f, 5.9f, 0.1f), glowMat, _monolith);
        Object.Destroy(glow.GetComponent<Collider>());
        DimensionSceneUtil.CreatePortal("ToBackrooms", Vector3.zero, Vector3.one,
            LevelPortal.PortalAction.EnterInterior, nextScene, _monolith).transform.localPosition = new Vector3(0f, 3f, 0f);
        var portalBox = _monolith.Find("ToBackrooms").GetComponent<BoxCollider>();
        portalBox.size = new Vector3(1.3f, 5.8f, 1.2f);

        // Spawn far away in a random direction, doorway facing the origin.
        float a = Random.value * Mathf.PI * 2f;
        _monolith.position = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnDistance;
        _monolith.rotation = Quaternion.LookRotation(-_monolith.position.normalized, Vector3.up);

        _rumble = DimensionSceneUtil.LoopingAudio(mono, DimensionSceneUtil.ToneClip(38f, 2f, 0.6f), 260f, 1f);
        DimensionSceneUtil.LoopingAudio(gameObject, DimensionSceneUtil.ToneClip(180f, 3f, 0.03f), 900f, 1f); // faint wind bed
    }

    void Update()
    {
        if (!_atmosApplied && ObserverState.Cam != null)
        {
            DimensionSceneUtil.ApplyAtmosphere(
                ambient: new Color(0.3f, 0.26f, 0.34f),
                fog: new Color(0.45f, 0.38f, 0.5f), fogDensity: 0.006f,
                background: new Color(0.35f, 0.28f, 0.42f));
            _atmosApplied = true;
        }
        // Whole-monolith bounds (roughly its 5×9×1.5 body wherever it currently stands).
        var b = new Bounds(_monolith.position + Vector3.up * 4.5f, new Vector3(6f, 10f, 3f));
        _observedNow = _tracker.Tick(b, out _, float.PositiveInfinity);
    }

    void FixedUpdate()
    {
        if (_observedNow) return;                            // frozen the moment you look
        if (_player == null && --_playerRefindCooldown <= 0)
        {
            _player = FindObjectOfType<PlayerController>();
            _playerRefindCooldown = 60;
        }
        if (_player == null || _player.Rigidbody == null) return;

        Vector3 target = _player.Rigidbody.position; target.y = 0f;
        Vector3 pos = _monolithRb.position; pos.y = 0f;
        Vector3 to = target - pos;
        if (to.magnitude <= stopDistance) return;            // arrived — waiting at your back

        Vector3 step = to.normalized * advanceSpeed * Time.fixedDeltaTime;
        if (step.magnitude > to.magnitude - stopDistance) step = to.normalized * (to.magnitude - stopDistance);
        _monolithRb.MovePosition(_monolithRb.position + step);
        _monolithRb.MoveRotation(Quaternion.LookRotation(to.normalized, Vector3.up));
    }

    // ================= tuning (appended at END per repo conventions) =================
    [Header("Monolith")]
    [Tooltip("How far away the door starts.")]
    public float spawnDistance = 200f;
    [Tooltip("Metres per second it advances while unobserved.")]
    public float advanceSpeed = 8f;
    [Tooltip("It stops this close and waits for you to turn around.")]
    public float stopDistance = 3f;

    [Header("Exit")]
    [Tooltip("Scene the doorway leads to — the Backrooms hub.")]
    public string nextScene = "R1_Backrooms";
}
```

- [ ] **Step 9.2: Import + compile check** — `AssetDatabase.Refresh()` + `check_compile_errors`. Expected: clean.

- [ ] **Step 9.3: Commit**

```bash
git add "Assets/3 - Scripts/Dimensions/WaitingFieldController.cs"*
git commit -m "feat(dimensions): D4 Waiting Field controller (monolith advances while unobserved)"
```

---

### Task 10: D4 scene + build settings + smoke test

**Files:**
- Create: `Assets/4 - Scenes/Dimensions/D4_WaitingField.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset` (via editor script)

- [ ] **Step 10.1: Create the scene** — same recipe: `create_scene` `D4_WaitingField.unity`, InteriorPlayer at `(0, 1.5, 0)`, `DimensionRoot` + `WaitingFieldController`, delete template camera/light, `save_scene`.

- [ ] **Step 10.2: Add to Build Settings** (same snippet, `D4_WaitingField.unity`)

- [ ] **Step 10.3: Play-mode smoke test**

1. `play_game`; `get_unity_logs` → no errors.
2. `execute_script`: log monolith distance-to-player; face the player root AWAY from the monolith; wait 3 s; log distance again → must have DECREASED by ~20 m.
3. `execute_script`: face the player root AT the monolith; wait 2 s; distance must be unchanged (frozen).
4. `stop_game`.

- [ ] **Step 10.4: Commit**

```bash
git add "Assets/4 - Scenes/Dimensions/" "ProjectSettings/EditorBuildSettings.asset"
git commit -m "feat(dimensions): D4 Waiting Field scene"
```

---

### Task 11: Wire the black hole into the chain + verify hub/trap

**Files:**
- Modify: `Assets/1.6.7.7.7.unity` — ONLY `BlackHoleCapture.backroomsScene` ("R1_Backrooms" → "D1_ShiftingHalls")

- [ ] **Step 11.1: Point the black hole at D1** (coplay MCP)

1. Confirm the gameplay scene is the active scene (`get_unity_editor_state`); `open_scene` `Assets/1.6.7.7.7.unity` if not.
2. `list_game_objects_in_hierarchy` componentFilter `BlackHoleCapture` → note the object path (expected: the Scingularity/black-hole body).
3. `set_property` on that object's `BlackHoleCapture.backroomsScene` → `D1_ShiftingHalls`.
4. `save_scene`.

- [ ] **Step 11.2: Verify the hub and the trap (read-only)**

```bash
# EXIT (action 1) + NEXTLEVEL (action 0 → PoolroomsDemo) exist in the Backrooms:
grep -A 4 "fa8cf5c80062c364d97d6aa50197d17a" "Assets/Backrooms/Scenes/R1_Backrooms.unity" | grep -E "action|targetScene"
# PoolroomsDemo has NO LevelPortal (trap intact — expect no output):
grep "fa8cf5c80062c364d97d6aa50197d17a" "Assets/Poolrooms_Lvl37/Scenes/PoolroomsDemo.unity"
```

- [ ] **Step 11.3: Chain-integrity check** (coplay `execute_script`, edit mode)

```csharp
using UnityEditor;
using System.Linq;
var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToArray();
string[] chain = { "D1_ShiftingHalls", "D2_DuneSea", "D3_LongDark", "D4_WaitingField", "R1_Backrooms", "PoolroomsDemo" };
foreach (var c in chain)
    Debug.Log("[chain] " + c + (scenes.Contains(c) ? " OK" : " MISSING FROM BUILD SETTINGS"));
```
Expected: six `OK` lines.

- [ ] **Step 11.4: Commit** (the gameplay scene carries the user's pre-existing editor tweaks — say so)

```bash
git add "Assets/1.6.7.7.7.unity"
git commit -m "feat(dimensions): black hole now leads to D1 Shifting Halls (scene also carries prior editor tweaks)"
```

---

### Task 12: End-to-end verification

- [ ] **Step 12.1: Automated spot-check of the full transition** (coplay MCP)

1. Open `D1_ShiftingHalls`, `play_game`, `execute_script`: move the player rigidbody into the exit-door portal position after forcing a door spawn (`execute_script` can call the controller's private spawn via reflection or temporarily set `exitDoorChance = 1` on the component, then rotate the camera to trigger reshuffles). Confirm the scene load to `D2_DuneSea` fires and `get_unity_logs` shows the PortalManager hop with no errors.
2. `stop_game`.

- [ ] **Step 12.2: Human playthrough (hand to the user)** — checklist:

- [ ] Fly into the black hole → white flash → D1 corridors (inventory/hotbar intact).
- [ ] D1: turning 180° visibly changes the maze; a glowing door appears eventually; looking away from a seen door removes it; walking through loads D2.
- [ ] D2: wells relocate when unseen; wrong well teleports you far away; the humming glowing well loads D3.
- [ ] D3: looking at the bridge dissolves it; walking backwards crosses it; falling respawns at the start tablet; far platform loads D4.
- [ ] D4: monolith frozen while watched, closer every time you look back; doorway loads R1_Backrooms.
- [ ] Backrooms EXIT returns to the cabin on Humble Abode with inventory intact; NEXTLEVEL leads to the Poolrooms, which has no way out.
- [ ] No console errors across the whole run.

- [ ] **Step 12.3: Fix anything the playthrough surfaces, then final commit + push to `soundofspace`** (the canonical remote) once the user confirms it plays well.

---

### Task 13 (OPTIONAL polish — only if time remains today)

- Coplay `generate_or_edit_images` → tiling wall/sand textures, assigned to the controllers' materials via serialized `Material` overrides (create real `.mat` assets — remember the build-variant-stripping trap with code-made materials if a Windows build test is ever done: the Editor test is unaffected).
- Coplay `generate_sfx` / `generate_music` → replace the placeholder tone hums (fluorescent buzz, desert wind, void drone, distant rumble). Memory gotcha: SFX generation may report timeout but still land.
- A `GrogginessImageEffect` pulse or vignette blip when a maze reshuffle happens close behind you.
