using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Streams CartoonGrass foliage (grass + plants + flowers) across a planet's
/// surface using the same deterministic cubeface-cell pattern as
/// MushroomSpawner, but decorative-only:
///   • no colliders, no interaction component;
///   • NO save integration — the layout is a pure function of `seed`, so it
///     regenerates identically every load (nothing to persist);
///   • per-cell PATCH clusters (several blades) or single SCATTER blades, for
///     a "mix of patches + light scatter" look;
///   • foliage rejected on ocean, cliffs, SpawnExclusionZones, and anywhere a
///     downward surface raycast hits a collider under a GrassBlocker (cabin).
///
/// Scene component (NOT an auto-singleton) → sidesteps the MainMenu-seeding
/// trap. Place on a GameObject under "--- Managers ---" in the gameplay scene.
/// </summary>
public class GrassSpawner : MonoBehaviour
{
    [Header("Planet scope")]
    [Tooltip("If non-empty, grass ONLY spawns on the body whose bodyName matches this (e.g. \"Humble Abode\"). Leave empty to fall back to excludeBodyNames.")]
    public string onlyBodyName = "Humble Abode";
    [Tooltip("Body names to skip when onlyBodyName is empty.")]
    public string[] excludeBodyNames = { "Sun" };

    [Header("Prefabs (weighted by category)")]
    [Tooltip("All grass prefabs EXCEPT Grass_08-1.")]
    public GameObject[] grassPrefabs;
    [Tooltip("Plant_01..13.")]
    public GameObject[] plantPrefabs;
    [Tooltip("Flower_01..06.")]
    public GameObject[] flowerPrefabs;
    [Tooltip("Relative odds a blade is grass vs plant vs flower. Grass dominates; flowers are rare accents.")]
    public float grassWeight = 1f;
    public float plantWeight = 0.22f;
    public float flowerWeight = 0.06f;

    [Header("Streaming")]
    [Tooltip("Foliage only exists within this distance of the viewer.")]
    public float spawnRadius = 120f;
    [Tooltip("Hard cap on total live blades (counts every blade in every patch).")]
    public int maxGrass = 600;
    [Tooltip("Layers the surface raycast may hit. WorldProp + Ship bits are stripped automatically in Awake.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Seconds between spawn/despawn passes.")]
    public float updateInterval = 0.25f;
    [Tooltip("Height above the surface where the downward raycast starts.")]
    public float surfaceRayHeight = 100f;

    [Header("Determinism / density")]
    [Tooltip("Reroll the whole layout. Keep unique vs other spawners.")]
    public int seed = 45678;
    [Tooltip("Cell size in metres. Smaller = denser cell grid.")]
    public float cellSize = 12f;
    [Range(0f, 1f)]
    [Tooltip("Probability a cell is a dense PATCH (cluster of blades).")]
    public float patchChance = 0.35f;
    [Range(0f, 1f)]
    [Tooltip("Probability a (non-patch) cell is a single SCATTER blade. patchChance + scatterChance must be <= 1.")]
    public float scatterChance = 0.35f;
    [Tooltip("Min blades in a patch.")]
    public int minBladesPerPatch = 4;
    [Tooltip("Max blades in a patch.")]
    public int maxBladesPerPatch = 7;
    [Tooltip("Radius (metres, on the surface) blades scatter around the cell point.")]
    public float patchRadius = 2.5f;
    [Range(0f, 90f)]
    [Tooltip("Reject foliage on slopes steeper than this (degrees from radial-up).")]
    public float maxSurfaceAngle = 40f;

    [Header("Per-blade variation")]
    public float minScale = 0.7f;
    public float maxScale = 1.5f;
    [Tooltip("Constant push into the ground along radial-up.")]
    public float groundOffset = 0f;
    [Tooltip("Extra embed proportional to scale (catches per-prefab seating variance).")]
    public float groundEmbedPerScale = 0.04f;

    [Header("Colour override (optional — for other planets like Cyclops)")]
    [Tooltip("When on, tints every blade via MaterialPropertyBlock (does NOT touch shared material assets). Leave OFF for Humble Abode's default greens.")]
    public bool overrideTint = false;
    [ColorUsage(false)] public Color topColor = new Color(0.64f, 0.81f, 0.17f);
    [ColorUsage(false)] public Color bottomColor = new Color(0.32f, 0.45f, 0.17f);

    [Header("Performance")]
    [Tooltip("Max blades spawned per tick. Spreads a dense fill over several ticks so arriving on a planet doesn't hitch.")]
    public int maxSpawnsPerTick = 120;

    [Header("Grass material override")]
    [Tooltip("If set, GRASS blades render with THIS material at runtime instead of the pack's shared grass material (whose Shader Graph renders black at the planet's far-from-origin world position). Plants/flowers keep their own materials. This never modifies the shared CartoonGrass assets, so the pack's demo scene is unaffected.")]
    public Material grassMaterialOverride;

    // ── internals ─────────────────────────────────────────────────────────
    class CellInstances
    {
        public readonly List<GameObject> objs = new List<GameObject>();
        public readonly List<int> poolIdx = new List<int>();
        public Vector3 anchor;
    }

    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, CellInstances> activeCells = new Dictionary<long, CellInstances>();
    }

    readonly List<BodyState> bodies = new List<BodyState>();
    PlayerController player;

    GameObject[] _allPrefabs;     // grass ++ plants ++ flowers (pool/seat index space)
    int _grassStart, _grassCount, _plantStart, _plantCount, _flowerStart, _flowerCount;
    float[] _bottomY;             // per _allPrefabs entry
    Stack<GameObject>[] pools;    // per _allPrefabs entry

    MaterialPropertyBlock _mpb;
    static readonly int TopColorId  = Shader.PropertyToID("_TopColor");
    static readonly int BotColorId  = Shader.PropertyToID("_BottomColor");
    static readonly int TopColorId2 = Shader.PropertyToID("TopColor");
    static readonly int BotColorId2 = Shader.PropertyToID("BottomColor");

    struct CellCandidate
    {
        public int bodySlot, face, cellU, cellV, mode; // mode: 1 scatter, 2 patch
        public float distSq;
    }
    readonly List<CellCandidate> scratchCandidates = new List<CellCandidate>();
    static readonly System.Comparison<CellCandidate> CandidateByDistance =
        (a, b) => a.distSq.CompareTo(b.distSq);
    readonly List<long> scratchRemove = new List<long>();
    float tickTimer;

    void Awake()
    {
        var list = new List<GameObject>();
        _grassStart = list.Count; AddRange(list, grassPrefabs);  _grassCount  = list.Count - _grassStart;
        _plantStart = list.Count; AddRange(list, plantPrefabs);  _plantCount  = list.Count - _plantStart;
        _flowerStart = list.Count; AddRange(list, flowerPrefabs); _flowerCount = list.Count - _flowerStart;
        _allPrefabs = list.ToArray();

        if (_allPrefabs.Length == 0)
        {
            Debug.LogWarning("[GrassSpawner] No prefabs assigned; spawner idle.");
            enabled = false;
            return;
        }

        _bottomY = new float[_allPrefabs.Length];
        pools = new Stack<GameObject>[_allPrefabs.Length];
        for (int i = 0; i < _allPrefabs.Length; i++)
        {
            _bottomY[i] = SpawnerCubeface.ComputeLocalBottomY(_allPrefabs[i]);
            pools[i] = new Stack<GameObject>();
        }

        // Don't let our raycast hit other spawners' props or a low-flying ship.
        groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask;
        _mpb = new MaterialPropertyBlock();
    }

    static void AddRange(List<GameObject> dst, GameObject[] src)
    {
        if (src == null) return;
        for (int i = 0; i < src.Length; i++) if (src[i] != null) dst.Add(src[i]);
    }

    void Update()
    {
        if (!ResolveRefs()) return;
        tickTimer += Time.deltaTime;
        if (tickTimer < updateInterval) return;
        tickTimer = 0f;
        Tick();
    }

    bool ResolveRefs()
    {
        if (bodies.Count == 0)
        {
            var sim = NBodySimulation.Bodies;
            if (sim == null) return false;
            bool useOnly = !string.IsNullOrEmpty(onlyBodyName);
            for (int i = 0; i < sim.Length; i++)
            {
                var b = sim[i];
                if (b == null) continue;
                if (useOnly) { if (b.bodyName != onlyBodyName) continue; }
                else if (IsExcluded(b.bodyName)) continue;
                bodies.Add(new BodyState
                {
                    body = b,
                    gen = b.GetComponentInChildren<CelestialBodyGenerator>(),
                });
            }
            if (bodies.Count == 0) return false;
        }
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>(true);
            if (player == null) return false;
        }
        return true;
    }

    bool IsExcluded(string bodyName)
    {
        if (excludeBodyNames == null) return false;
        for (int i = 0; i < excludeBodyNames.Length; i++)
            if (excludeBodyNames[i] == bodyName) return true;
        return false;
    }

    Vector3 GetViewerPosition()
    {
        if (player != null && player.Camera != null) return player.Camera.transform.position;
        if (player != null) return player.transform.position;
        var cam = Camera.main;
        return cam != null ? cam.transform.position : transform.position;
    }

    int CountActive()
    {
        int n = 0;
        for (int s = 0; s < bodies.Count; s++)
            foreach (var kv in bodies[s].activeCells) n += kv.Value.objs.Count;
        return n;
    }

    float Hash01(int face, int cu, int cv, int salt)
        => (SpawnerCubeface.Hash(seed, face, cu, cv, salt) & 0xFFFFu) / 65535f;

    // 0 = empty, 1 = scatter, 2 = patch
    int CellMode(int face, int cu, int cv)
    {
        float r = Hash01(face, cu, cv, 1);
        if (r < patchChance) return 2;
        if (r < patchChance + scatterChance) return 1;
        return 0;
    }

    void Tick()
    {
        Vector3 viewer = GetViewerPosition();

        for (int s = 0; s < bodies.Count; s++) DespawnOutOfRange(bodies[s], viewer);

        if (maxGrass <= 0) { EnforceMax(viewer, 0); return; }

        scratchCandidates.Clear();
        float prefilter = spawnRadius + cellSize;
        float prefilterSq = prefilter * prefilter;

        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            if (entry.body == null) continue;
            float bodyOuter = spawnRadius + entry.body.radius + cellSize;
            if ((entry.body.Position - viewer).sqrMagnitude > bodyOuter * bodyOuter) continue;

            float faceUVPerCell = cellSize / Mathf.Max(0.001f, entry.body.radius);
            int half = Mathf.CeilToInt(1f / Mathf.Max(0.0001f, faceUVPerCell)) + 1;

            for (int face = 0; face < 6; face++)
            for (int cu = -half; cu <= half; cu++)
            for (int cv = -half; cv <= half; cv++)
            {
                long id = SpawnerCubeface.EncodeCell(face, cu, cv);
                if (entry.activeCells.ContainsKey(id)) continue;
                int mode = CellMode(face, cu, cv);
                if (mode == 0) continue;
                if (!TryCellAnchor(entry.body, face, cu, cv, faceUVPerCell, out Vector3 anchor)) continue;
                float dSq = (anchor - viewer).sqrMagnitude;
                if (dSq > prefilterSq) continue;
                scratchCandidates.Add(new CellCandidate
                { bodySlot = s, face = face, cellU = cu, cellV = cv, mode = mode, distSq = dSq });
            }
        }

        scratchCandidates.Sort(CandidateByDistance);

        int spawnedThisTick = 0;
        for (int i = 0; i < scratchCandidates.Count; i++)
        {
            if (CountActive() >= maxGrass) break;
            if (spawnedThisTick >= maxSpawnsPerTick) break;
            spawnedThisTick += SpawnCell(scratchCandidates[i], viewer);
        }

        EnforceMax(viewer, maxGrass);
    }

    bool TryCellAnchor(CelestialBody body, int face, int cu, int cv, float faceUVPerCell, out Vector3 anchor)
    {
        anchor = default;
        float jU = (Hash01(face, cu, cv, 3) - 0.5f) * faceUVPerCell * 0.8f;
        float jV = (Hash01(face, cu, cv, 4) - 0.5f) * faceUVPerCell * 0.8f;
        float fu = (cu + 0.5f) * faceUVPerCell + jU;
        float fv = (cv + 0.5f) * faceUVPerCell + jV;
        if (fu < -1f || fu > 1f || fv < -1f || fv > 1f) return false;
        Vector3 dir = SpawnerCubeface.FaceUVToDirection(face, fu, fv);
        if (dir.sqrMagnitude < 0.0001f) return false;
        anchor = body.Position + dir * body.radius;
        return true;
    }

    int SpawnCell(CellCandidate c, Vector3 viewer)
    {
        var entry = bodies[c.bodySlot];
        var planet = entry.body;
        if (planet == null) return 0;
        float faceUVPerCell = cellSize / Mathf.Max(0.001f, planet.radius);
        if (!TryCellAnchor(planet, c.face, c.cellU, c.cellV, faceUVPerCell, out Vector3 anchor)) return 0;

        // Tangent basis at the cell anchor for in-plane blade offsets.
        Vector3 cdir = (anchor - planet.Position).normalized;
        Vector3 tangent = Vector3.Cross(cdir, Vector3.up);
        if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.Cross(cdir, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(cdir, tangent);

        int count = 1;
        if (c.mode == 2)
        {
            int lo = Mathf.Min(minBladesPerPatch, maxBladesPerPatch);
            int hi = Mathf.Max(minBladesPerPatch, maxBladesPerPatch);
            count = lo + (int)(Hash01(c.face, c.cellU, c.cellV, 2) * (hi - lo + 1));
            count = Mathf.Clamp(count, 1, hi);
        }

        CellInstances cell = null;
        float oceanR = entry.gen != null ? entry.gen.GetOceanRadius() : 0f;

        for (int i = 0; i < count; i++)
        {
            if (CountActive() >= maxGrass) break;
            int b = 100 + i * 8;
            float offU = (Hash01(c.face, c.cellU, c.cellV, b + 0) * 2f - 1f) * patchRadius;
            float offV = (Hash01(c.face, c.cellU, c.cellV, b + 1) * 2f - 1f) * patchRadius;
            Vector3 offWorld = anchor + tangent * offU + bitangent * offV;
            Vector3 bdir = (offWorld - planet.Position).normalized;

            Vector3 rayOrigin = planet.Position + bdir * (planet.radius + surfaceRayHeight);
            if (!Physics.Raycast(rayOrigin, -bdir, out RaycastHit hit,
                                 planet.radius * 2f, groundMask, QueryTriggerInteraction.Ignore))
                continue;

            // Blocked by a building (cabin roof) — keep grass out from under it.
            if (hit.collider != null && hit.collider.GetComponentInParent<GrassBlocker>() != null)
                continue;

            if (oceanR > 0f && (hit.point - planet.Position).magnitude < oceanR) continue;

            Vector3 up = (hit.point - planet.Position).normalized;
            if (Vector3.Angle(hit.normal, up) > maxSurfaceAngle) continue;
            if ((hit.point - viewer).sqrMagnitude > spawnRadius * spawnRadius) continue;
            if (SpawnExclusionZone.IsExcluded(hit.point)) continue;

            int prefabIdx = PickPrefab(c.face, c.cellU, c.cellV, b + 2, b + 3);
            if (prefabIdx < 0) continue;
            float yaw = Hash01(c.face, c.cellU, c.cellV, b + 4) * 360f;
            float scale = Mathf.Lerp(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale),
                                     Hash01(c.face, c.cellU, c.cellV, b + 5));

            float bottomY = _bottomY[prefabIdx];
            Vector3 pos = hit.point - up * (bottomY * scale + groundOffset + groundEmbedPerScale * scale);
            Quaternion rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);

            GameObject blade = SpawnBlade(entry, prefabIdx, pos, rot, scale);
            if (blade == null) continue;
            if (cell == null) cell = new CellInstances { anchor = anchor };
            cell.objs.Add(blade);
            cell.poolIdx.Add(prefabIdx);
        }

        if (cell != null)
        {
            entry.activeCells[SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV)] = cell;
            return cell.objs.Count;
        }
        return 0;
    }

    int PickPrefab(int face, int cu, int cv, int catSalt, int idxSalt)
    {
        float wg = _grassCount  > 0 ? Mathf.Max(0f, grassWeight)  : 0f;
        float wp = _plantCount  > 0 ? Mathf.Max(0f, plantWeight)  : 0f;
        float wf = _flowerCount > 0 ? Mathf.Max(0f, flowerWeight) : 0f;
        float total = wg + wp + wf;
        if (total <= 0f) return _allPrefabs.Length > 0 ? 0 : -1;

        float r = Hash01(face, cu, cv, catSalt) * total;
        int start, cnt;
        if (r < wg)            { start = _grassStart;  cnt = _grassCount;  }
        else if (r < wg + wp)  { start = _plantStart;  cnt = _plantCount;  }
        else                   { start = _flowerStart; cnt = _flowerCount; }
        if (cnt <= 0) return 0;
        int local = (int)(Hash01(face, cu, cv, idxSalt) * cnt);
        if (local >= cnt) local = cnt - 1;
        return start + local;
    }

    GameObject SpawnBlade(BodyState entry, int prefabIdx, Vector3 pos, Quaternion rot, float scale)
    {
        var prefab = _allPrefabs[prefabIdx];
        if (prefab == null) return null;

        GameObject blade;
        var pool = pools[prefabIdx];
        if (pool.Count > 0)
        {
            blade = pool.Pop();
            blade.transform.SetPositionAndRotation(pos, rot);
            blade.transform.localScale = Vector3.one * scale;
            blade.SetActive(true);
        }
        else
        {
            blade = Instantiate(prefab, pos, rot);
            blade.transform.localScale = Vector3.one * scale;

            // GRASS blades (prefabIdx in the grass range) get the game material
            // so they don't render black. Applied once at instantiate; the
            // material persists across pool reuse. Plants/flowers (idx >=
            // _plantStart) keep their own working materials.
            if (grassMaterialOverride != null && prefabIdx < _plantStart)
            {
                var rends = blade.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < rends.Length; i++) rends[i].sharedMaterial = grassMaterialOverride;
            }
        }

        blade.transform.SetParent(entry.body.transform, true);
        SpawnerCubeface.SetLayerRecursively(blade, SpawnerCubeface.WorldPropLayer);
        ApplyTint(blade);

        var fade = blade.GetComponent<SpawnFade>();
        if (fade == null) fade = blade.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
        return blade;
    }

    void ApplyTint(GameObject blade)
    {
        if (!overrideTint) return;
        var renderers = blade.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var rend = renderers[i];
            rend.GetPropertyBlock(_mpb);
            _mpb.SetColor(TopColorId, topColor);
            _mpb.SetColor(BotColorId, bottomColor);
            _mpb.SetColor(TopColorId2, topColor);
            _mpb.SetColor(BotColorId2, bottomColor);
            rend.SetPropertyBlock(_mpb);
        }
    }

    // Distance reference for a cell. Reads a LIVE blade's current world
    // position (floating-origin safe — a position captured at spawn goes stale
    // after an origin shift). `alive` is false when every blade has been
    // destroyed/returned, so the caller can drop the dead cell.
    static Vector3 CellPosition(CellInstances cell, out bool alive)
    {
        for (int i = 0; i < cell.objs.Count; i++)
        {
            var o = cell.objs[i];
            if (o != null) { alive = true; return o.transform.position; }
        }
        alive = false;
        return cell.anchor;
    }

    void DespawnOutOfRange(BodyState entry, Vector3 viewer)
    {
        scratchRemove.Clear();
        float limit = spawnRadius * 1.05f;
        float limitSq = limit * limit;
        foreach (var kv in entry.activeCells)
        {
            Vector3 p = CellPosition(kv.Value, out bool alive);
            if (!alive || (p - viewer).sqrMagnitude > limitSq) scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnCell(entry, scratchRemove[i]);
    }

    void EnforceMax(Vector3 viewer, int max)
    {
        while (CountActive() > max)
        {
            BodyState farthestEntry = null;
            long farthestId = 0;
            float farthestSq = -1f;
            for (int s = 0; s < bodies.Count; s++)
            {
                var entry = bodies[s];
                foreach (var kv in entry.activeCells)
                {
                    Vector3 p = CellPosition(kv.Value, out _);
                    float dSq = (p - viewer).sqrMagnitude;
                    if (dSq > farthestSq) { farthestSq = dSq; farthestId = kv.Key; farthestEntry = entry; }
                }
            }
            if (farthestEntry == null) break;
            DespawnCell(farthestEntry, farthestId);
        }
    }

    void DespawnCell(BodyState entry, long cellId)
    {
        if (!entry.activeCells.TryGetValue(cellId, out var cell)) return;
        entry.activeCells.Remove(cellId);
        for (int i = 0; i < cell.objs.Count; i++)
        {
            var blade = cell.objs[i];
            int idx = cell.poolIdx[i];
            if (blade == null) continue;
            var fade = blade.GetComponent<SpawnFade>();
            if (fade != null) fade.BeginFadeOut(() => ReturnToPool(blade, idx));
            else ReturnToPool(blade, idx);
        }
    }

    void ReturnToPool(GameObject blade, int poolIdx)
    {
        if (blade == null) return;
        blade.transform.SetParent(null, true);
        blade.SetActive(false);
        if (poolIdx < 0 || poolIdx >= pools.Length) poolIdx = 0;
        pools[poolIdx].Push(blade);
    }
}
