using System.Collections.Generic;
using UnityEngine;

public class TreeSpawner : MonoBehaviour
{
    [Header("Planets")]
    [Tooltip("Body names to skip (case-sensitive, matched against CelestialBody.bodyName). Trees spawn on every other body in NBodySimulation.Bodies.")]
    public string[] excludeBodyNames = { "Sun", "Constant Companion", "Watchful Eye", "Tumbling Bean" };

    [Header("Tree Prefabs")]
    [Tooltip("Drag tree prefabs here. One is enough for now; add more later for variety.")]
    public GameObject[] treePrefabs;

    [Header("Spawn")]
    [Tooltip("Trees only exist within this distance of the player.")]
    public float spawnRadius = 150f;
    [Tooltip("Fallback cap when InputSettings is not assigned. The settings-menu slider overrides this at runtime.")]
    public int maxTrees = 20;
    [Tooltip("Optional. When assigned, the spawner reads maxTrees from this asset every tick — the settings slider drives it live.")]
    public InputSettings inputSettings;
    [Tooltip("Layers the surface raycast should hit. Should include terrain, exclude water/ship/player.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Push the tree this far into the ground along the planet-radial axis. Positive = into the ground, negative = above.")]
    public float groundOffset = 0f;

    [Header("Determinism")]
    [Tooltip("Change to reroll the whole forest layout.")]
    public int seed = 12345;
    [Tooltip("Cell size in metres. Larger = trees are spaced further apart.")]
    public float cellSize = 40f;
    [Range(0f, 1f)]
    [Tooltip("Probability that any given cell holds a tree.")]
    public float treeSpawnChance = 0.7f;

    const float BaselineRadius = 150f;

    [Header("Performance")]
    [Tooltip("Seconds between spawn/despawn passes. 0.25 is responsive without being wasteful.")]
    public float updateInterval = 0.25f;
    [Tooltip("Height above the planet surface where the downward raycast originates.")]
    public float surfaceRayHeight = 100f;

    [Header("Mining Audio")]
    [Tooltip("Sound played when a tree is fully mined (plays alongside the +N wood popup).")]
    public AudioClip treeBreakClip;
    [Range(0f, 1f)]
    [Tooltip("Volume for the tree-break sound.")]
    public float treeBreakVolume = 0.7f;

    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, GameObject> activeTrees = new Dictionary<long, GameObject>();
        public readonly HashSet<long> minedCells = new HashSet<long>();
    }

    readonly List<BodyState> bodies = new List<BodyState>();
    // Pre-applied mined cells queued by RestoreMinedCells before bodies
    // resolve. Drained into BodyState.minedCells at resolve. Mirrors
    // AlienNPCSpawner's pendingKilledCellsByBody pattern.
    readonly Dictionary<string, HashSet<long>> pendingMinedCellsByBody = new Dictionary<string, HashSet<long>>();
    PlayerController player;
    Stack<GameObject>[] pools;
    readonly List<long> scratchRemove = new List<long>();
    readonly List<CellCandidate> scratchCandidates = new List<CellCandidate>();
    static readonly System.Comparison<CellCandidate> CandidateByDistance =
        (a, b) => a.distSq.CompareTo(b.distSq);
    float tickTimer;

    struct CellCandidate
    {
        public int bodySlot;
        public int face;
        public int cellU;
        public int cellV;
        public float distSq;
    }

    void Awake()
    {
        if (treePrefabs == null || treePrefabs.Length == 0)
        {
            Debug.LogWarning("[TreeSpawner] No tree prefabs assigned; spawner will stay idle.");
            enabled = false;
            return;
        }
        pools = new Stack<GameObject>[treePrefabs.Length];
        for (int i = 0; i < pools.Length; i++) pools[i] = new Stack<GameObject>();

        // Stop this spawner's surface raycast from hitting other spawners'
        // instances (mushroom, alien, crystal) OR a low-flying ship — see the
        // ShipLayer comment in SpawnerCubeface for the floating-prop bug.
        groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask;
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
            for (int i = 0; i < sim.Length; i++)
            {
                var b = sim[i];
                if (b == null) continue;
                if (IsExcluded(b.bodyName)) continue;
                var entry = new BodyState
                {
                    body = b,
                    gen = b.GetComponentInChildren<CelestialBodyGenerator>(),
                };
                if (pendingMinedCellsByBody.TryGetValue(b.bodyName, out var pending))
                {
                    foreach (var c in pending) entry.minedCells.Add(c);
                    pendingMinedCellsByBody.Remove(b.bodyName);
                }
                bodies.Add(entry);
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
        {
            if (excludeBodyNames[i] == bodyName) return true;
        }
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
        for (int i = 0; i < bodies.Count; i++) n += bodies[i].activeTrees.Count;
        return n;
    }

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
                        long id = SpawnerCubeface.EncodeCell(face, cu, cv);
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
            SpawnTree(entry, c.bodySlot, SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV), prefabIdx, pos, rot);
        }

        EnforceMaxTrees(playerPos, effectiveMax);
    }

    bool TryComputeCellApproxPos(CelestialBody body, int face, int cellU, int cellV, float faceUVPerCell, out Vector3 spherePos)
    {
        spherePos = default;
        uint hJU = SpawnerCubeface.Hash(seed, face, cellU, cellV, 2);
        uint hJV = SpawnerCubeface.Hash(seed, face, cellU, cellV, 3);
        float jitterU = ((hJU & 0xFFFFu) / 65535f - 0.5f) * faceUVPerCell * 0.9f;
        float jitterV = ((hJV & 0xFFFFu) / 65535f - 0.5f) * faceUVPerCell * 0.9f;
        float faceU = (cellU + 0.5f) * faceUVPerCell + jitterU;
        float faceV = (cellV + 0.5f) * faceUVPerCell + jitterV;
        if (faceU < -1f || faceU > 1f || faceV < -1f || faceV > 1f) return false;
        Vector3 dir = SpawnerCubeface.FaceUVToDirection(face, faceU, faceV);
        if (dir.sqrMagnitude < 0.0001f) return false;
        spherePos = body.Position + dir * body.radius;
        return true;
    }

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

    void EnforceMaxTrees(Vector3 playerPos, int max)
    {
        while (CountActive() > max)
        {
            BodyState farthestEntry = null;
            long farthestId = 0;
            float farthestSq = -1f;
            for (int s = 0; s < bodies.Count; s++)
            {
                var entry = bodies[s];
                foreach (var kv in entry.activeTrees)
                {
                    if (kv.Value == null) continue;
                    float dSq = (kv.Value.transform.position - playerPos).sqrMagnitude;
                    if (dSq > farthestSq) { farthestSq = dSq; farthestId = kv.Key; farthestEntry = entry; }
                }
            }
            if (farthestEntry == null) break;
            DespawnInternal(farthestEntry, farthestId);
        }
    }

    bool CellHasTree(int face, int cellU, int cellV)
    {
        uint h = SpawnerCubeface.Hash(seed, face, cellU, cellV, 1);
        return (h & 0xFFFFu) / 65535f < treeSpawnChance;
    }

    bool TryComputeTreePlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                                  Vector3 playerPos, float effectiveRadius,
                                  out Vector3 pos, out Quaternion rot, out int prefabIdx)
    {
        pos = default; rot = default; prefabIdx = 0;

        uint hJU = SpawnerCubeface.Hash(seed, face, cellU, cellV, 2);
        uint hJV = SpawnerCubeface.Hash(seed, face, cellU, cellV, 3);
        uint hPI = SpawnerCubeface.Hash(seed, face, cellU, cellV, 4);
        uint hY  = SpawnerCubeface.Hash(seed, face, cellU, cellV, 5);

        float jitterU = ((hJU & 0xFFFFu) / 65535f - 0.5f) * faceUVPerCell * 0.9f;
        float jitterV = ((hJV & 0xFFFFu) / 65535f - 0.5f) * faceUVPerCell * 0.9f;
        float faceU = (cellU + 0.5f) * faceUVPerCell + jitterU;
        float faceV = (cellV + 0.5f) * faceUVPerCell + jitterV;

        if (faceU < -1f || faceU > 1f || faceV < -1f || faceV > 1f) return false;

        Vector3 dir = SpawnerCubeface.FaceUVToDirection(face, faceU, faceV);
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

    void SpawnTree(BodyState entry, int bodySlot, long cellId, int prefabIdx, Vector3 pos, Quaternion rot)
    {
        if (prefabIdx < 0 || prefabIdx >= treePrefabs.Length) prefabIdx = 0;
        var prefab = treePrefabs[prefabIdx];
        if (prefab == null) return;
        if (entry == null || entry.body == null) return;
        // Defensive lazy-init. Awake normally sets pools, but if a domain
        // reload or duplicate-component edge case leaves pools null on the
        // first frame, this rebuilds rather than throwing.
        if (pools == null) pools = new Stack<GameObject>[treePrefabs.Length];
        if (pools[prefabIdx] == null) pools[prefabIdx] = new Stack<GameObject>();

        GameObject tree;
        var pool = pools[prefabIdx];
        if (pool.Count > 0)
        {
            tree = pool.Pop();
            tree.transform.SetPositionAndRotation(pos, rot);
            tree.SetActive(true);
        }
        else
        {
            tree = Instantiate(prefab, pos, rot);
        }
        tree.transform.SetParent(entry.body.transform, true);
        SpawnerCubeface.SetLayerRecursively(tree, SpawnerCubeface.WorldPropLayer);

        var st = tree.GetComponent<SpawnedTree>();
        if (st == null) st = tree.AddComponent<SpawnedTree>();
        st.Init(this, bodySlot, cellId, prefabIdx);
        entry.activeTrees[cellId] = tree;

        var fade = tree.GetComponent<SpawnFade>();
        if (fade == null) fade = tree.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

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

    public void MarkCellMined(int bodySlot, long cellId)
    {
        if (bodySlot < 0 || bodySlot >= bodies.Count) return;
        var entry = bodies[bodySlot];
        entry.minedCells.Add(cellId);
        if (entry.activeTrees.ContainsKey(cellId)) DespawnInternal(entry, cellId);
    }

    // ─── Save integration ────────────────────────────────────────────────

    public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, long>> GetMinedCellsWithBody()
    {
        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            string name = entry.body != null ? entry.body.bodyName : "";
            foreach (var c in entry.minedCells)
                yield return new System.Collections.Generic.KeyValuePair<string, long>(name, c);
        }
        foreach (var kv in pendingMinedCellsByBody)
            foreach (var c in kv.Value)
                yield return new System.Collections.Generic.KeyValuePair<string, long>(kv.Key, c);
    }

    public void RestoreMinedCells(System.Collections.Generic.IList<long> cells, System.Collections.Generic.IList<string> bodyNames)
    {
        for (int s = 0; s < bodies.Count; s++) bodies[s].minedCells.Clear();
        pendingMinedCellsByBody.Clear();
        if (cells == null || cells.Count == 0) return;

        for (int i = 0; i < cells.Count; i++)
        {
            string name = (bodyNames != null && i < bodyNames.Count && !string.IsNullOrEmpty(bodyNames[i]))
                ? bodyNames[i]
                : "Humble Abode";

            BodyState match = null;
            for (int s = 0; s < bodies.Count; s++)
            {
                if (bodies[s].body != null && bodies[s].body.bodyName == name) { match = bodies[s]; break; }
            }
            if (match != null)
            {
                match.minedCells.Add(cells[i]);
            }
            else
            {
                if (!pendingMinedCellsByBody.TryGetValue(name, out var set))
                {
                    set = new HashSet<long>();
                    pendingMinedCellsByBody[name] = set;
                }
                set.Add(cells[i]);
            }
        }
    }

    public bool IsCellMined(int bodySlot, long cellId)
    {
        if (bodySlot < 0 || bodySlot >= bodies.Count) return false;
        return bodies[bodySlot].minedCells.Contains(cellId);
    }

}
