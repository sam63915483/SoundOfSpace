using System.Collections.Generic;
using UnityEngine;

public class MushroomSpawner : MonoBehaviour
{
    [Header("Planets")]
    [Tooltip("Body names to skip (case-sensitive, matched against CelestialBody.bodyName). Mushrooms grow on every other body in NBodySimulation.Bodies.")]
    public string[] excludeBodyNames = { "Sun" };

    [Header("Mushroom Prefabs")]
    [Tooltip("Drag every mushroom prefab from Low Poly Mushrooms Pack/Prefabs/Mushrooms/ here. Each spawn picks one deterministically per cell.")]
    public GameObject[] mushroomPrefabs;

    [Header("Spawn")]
    [Tooltip("Mushrooms only exist within this distance of the player. Matches alien NPC streaming radius.")]
    public float spawnRadius = 300f;
    [Tooltip("Fallback cap when InputSettings is not assigned. The pause-menu slider overrides this at runtime.")]
    public int maxMushrooms = 20;
    [Tooltip("Optional. When assigned, the spawner reads maxMushrooms from this asset every tick — the slider drives it live.")]
    public InputSettings inputSettings;
    [Tooltip("Layers the surface raycast should hit. Should include terrain, exclude water/ship/player.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Push the mushroom this far into the ground along the planet-radial axis. Positive = into the ground. Constant — not scaled by the random per-instance scale.")]
    public float groundOffset = 0f;
    [Tooltip("Additional downward push proportional to spawn scale. Catches the residual gap that varies per prefab (where the measured mesh bottom doesn't exactly match the visible bottom). 0.04 = 4cm of extra embed per scale unit, so a 5×-scaled mushroom is pushed 20cm deeper than a 1×-scaled one. Bumps the 'floaters' down to the surface without burying the small ones.")]
    public float groundEmbedPerScale = 0.04f;

    const float BaselineRadius = 300f;

    [Header("Determinism")]
    [Tooltip("Change to reroll the whole mushroom layout. Use a different value than TreeSpawner / AlienNPCSpawner so distributions don't overlap.")]
    public int seed = 24680;
    [Tooltip("Cell size in metres. Larger = mushrooms spaced further apart.")]
    public float cellSize = 50f;
    [Range(0f, 1f)]
    [Tooltip("Probability that any given cell holds a mushroom. Higher than alien NPC density so mushrooms feel like the dominant ambient prop — sized down via cell randomness and the slope reject gives a natural-looking spread.")]
    public float mushroomSpawnChance = 0.70f;
    [Tooltip("Maximum slope (degrees from radial-up) where a mushroom may spawn. Above this, the cell is rejected — keeps mushrooms off cliffs.")]
    [Range(0f, 90f)]
    public float maxSurfaceAngle = 35f;

    [Header("Variation")]
    [Tooltip("Minimum uniform scale multiplier applied at spawn (rolled deterministically per cell).")]
    public float minScale = 1f;
    [Tooltip("Maximum uniform scale multiplier applied at spawn (rolled deterministically per cell).")]
    public float maxScale = 5f;

    [Header("Performance")]
    [Tooltip("Seconds between spawn/despawn passes. 0.25 is responsive without being wasteful.")]
    public float updateInterval = 0.25f;
    [Tooltip("Height above the planet surface where the downward raycast originates.")]
    public float surfaceRayHeight = 100f;

    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, GameObject> activeMushrooms = new Dictionary<long, GameObject>();
        public readonly Dictionary<long, int> cellPrefabIdx = new Dictionary<long, int>();
        public readonly HashSet<long> consumedCells = new HashSet<long>();
    }

    readonly List<BodyState> bodies = new List<BodyState>();
    // Pre-applied consumed cells queued by RestoreConsumedCells before bodies
    // resolve. Drained into BodyState.consumedCells at resolve. Mirrors
    // AlienNPCSpawner's pendingKilledCellsByBody pattern.
    readonly Dictionary<string, HashSet<long>> pendingConsumedCellsByBody = new Dictionary<string, HashSet<long>>();
    PlayerController player;
    Stack<GameObject>[] pools;
    // Per-prefab "minimum Y in prefab-root local space" — the lowest point of
    // the prefab's renderer hierarchy. Used to seat the model so its bottom
    // sits ON the surface regardless of where the artist put the pivot.
    //   bottomY < 0 → pivot is above the mesh base; without correction the
    //                 model sinks into the ground. We lift by |bottomY|.
    //   bottomY > 0 → pivot is below the mesh base; without correction the
    //                 model floats. We push it down by bottomY.
    //   bottomY = 0 → pivot at base, no correction needed.
    // Each entry is multiplied by the random per-instance scale before being
    // applied. Computed once at Awake from sharedMesh.bounds / localBounds —
    // no Instantiate, so no Awake side effects on the prefab's own scripts.
    float[] _prefabLocalBottomY;
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
        if (mushroomPrefabs == null || mushroomPrefabs.Length == 0)
        {
            Debug.LogWarning("[MushroomSpawner] No mushroom prefabs assigned; spawner will stay idle.");
            enabled = false;
            return;
        }
        pools = new Stack<GameObject>[mushroomPrefabs.Length];
        for (int i = 0; i < pools.Length; i++) pools[i] = new Stack<GameObject>();
        _prefabLocalBottomY = new float[mushroomPrefabs.Length];
        for (int i = 0; i < mushroomPrefabs.Length; i++)
            _prefabLocalBottomY[i] = SpawnerCubeface.ComputeLocalBottomY(mushroomPrefabs[i]);

        // Stop this spawner's surface raycast from hitting other spawners'
        // instances (tree, alien, crystal) OR a low-flying ship — see the
        // ShipLayer comment in SpawnerCubeface for the floating-mushroom bug.
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
                if (pendingConsumedCellsByBody.TryGetValue(b.bodyName, out var pending))
                {
                    foreach (var c in pending) entry.consumedCells.Add(c);
                    pendingConsumedCellsByBody.Remove(b.bodyName);
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
        for (int i = 0; i < bodies.Count; i++) n += bodies[i].activeMushrooms.Count;
        return n;
    }

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
                        long id = SpawnerCubeface.EncodeCell(face, cu, cv);
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
            SpawnMushroom(entry, c.bodySlot, SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV), prefabIdx, pos, rot, scale,
                          colourPct, breathPct, kaleidoPct);
        }

        EnforceMaxMushrooms(playerPos, effectiveMax);
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
        foreach (var kv in entry.activeMushrooms)
        {
            if (kv.Value == null) { scratchRemove.Add(kv.Key); continue; }
            if ((kv.Value.transform.position - playerPos).sqrMagnitude > limitSq)
                scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnInternal(entry, scratchRemove[i]);
    }

    void EnforceMaxMushrooms(Vector3 playerPos, int max)
    {
        while (CountActive() > max)
        {
            BodyState farthestEntry = null;
            long farthestId = 0;
            float farthestSq = -1f;
            for (int s = 0; s < bodies.Count; s++)
            {
                var entry = bodies[s];
                foreach (var kv in entry.activeMushrooms)
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

    bool CellHasMushroom(int face, int cellU, int cellV)
    {
        uint h = SpawnerCubeface.Hash(seed, face, cellU, cellV, 1);
        return (h & 0xFFFFu) / 65535f < mushroomSpawnChance;
    }

    bool TryComputeMushroomPlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                                     Vector3 playerPos, float effectiveRadius,
                                     out Vector3 pos, out Quaternion rot,
                                     out int prefabIdx, out float scale,
                                     out float colourPct, out float breathPct, out float kaleidoPct)
    {
        pos = default; rot = default; prefabIdx = 0; scale = 1f;
        colourPct = 0f; breathPct = 0f; kaleidoPct = 0f;

        uint hJU = SpawnerCubeface.Hash(seed, face, cellU, cellV, 2);
        uint hJV = SpawnerCubeface.Hash(seed, face, cellU, cellV, 3);
        uint hPI = SpawnerCubeface.Hash(seed, face, cellU, cellV, 4);
        uint hY  = SpawnerCubeface.Hash(seed, face, cellU, cellV, 5);
        uint hSC = SpawnerCubeface.Hash(seed, face, cellU, cellV, 6);

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

        if (Vector3.Angle(hit.normal, up) > maxSurfaceAngle) return false;

        float yaw = (hY & 0xFFFFu) / 65535f * 360f;
        rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);
        prefabIdx = (int)(hPI % (uint)mushroomPrefabs.Length);
        float lo = Mathf.Min(minScale, maxScale);
        float hi = Mathf.Max(minScale, maxScale);
        scale = Mathf.Lerp(lo, hi, (hSC & 0xFFFFu) / 65535f);
        // Seat the mushroom so its bottom sits on hit.point regardless of
        // where the prefab's pivot is, plus a small scale-proportional embed
        // that compensates for per-prefab measurement variance. groundOffset
        // is a constant fine-tune; groundEmbedPerScale scales with the model.
        float bottomY = (prefabIdx >= 0 && _prefabLocalBottomY != null && prefabIdx < _prefabLocalBottomY.Length)
            ? _prefabLocalBottomY[prefabIdx]
            : 0f;
        pos = hit.point - up * (bottomY * scale + groundOffset + groundEmbedPerScale * scale);

        uint hCol = SpawnerCubeface.Hash(seed, face, cellU, cellV, 7);
        uint hBr  = SpawnerCubeface.Hash(seed, face, cellU, cellV, 8);
        uint hKa  = SpawnerCubeface.Hash(seed, face, cellU, cellV, 9);
        colourPct  = (hCol & 0xFFFFu) / 65535f;
        breathPct  = (hBr  & 0xFFFFu) / 65535f;
        kaleidoPct = (hKa  & 0xFFFFu) / 65535f;
        if (SpawnExclusionZone.IsExcluded(pos)) return false;   // keep clear of the ship school etc.
        return true;
    }

    void SpawnMushroom(BodyState entry, int bodySlot, long cellId, int prefabIdx, Vector3 pos, Quaternion rot, float scale,
                      float colourPct, float breathPct, float kaleidoPct)
    {
        if (prefabIdx < 0 || prefabIdx >= mushroomPrefabs.Length) prefabIdx = 0;
        var prefab = mushroomPrefabs[prefabIdx];
        if (prefab == null) return;
        if (entry == null || entry.body == null) return;
        // Defensive lazy-init. Awake normally sets pools, but if a domain
        // reload or duplicate-component edge case leaves pools null on the
        // first frame, this rebuilds rather than throwing.
        if (pools == null) pools = new Stack<GameObject>[mushroomPrefabs.Length];
        if (pools[prefabIdx] == null) pools[prefabIdx] = new Stack<GameObject>();

        GameObject mushroom;
        var pool = pools[prefabIdx];
        if (pool.Count > 0)
        {
            mushroom = pool.Pop();
            mushroom.transform.SetPositionAndRotation(pos, rot);
            mushroom.transform.localScale = Vector3.one * scale;
            mushroom.SetActive(true);
        }
        else
        {
            mushroom = Instantiate(prefab, pos, rot);
            mushroom.transform.localScale = Vector3.one * scale;

            // First-time setup of components the prefab doesn't ship with.
            // SphereCollider trigger, sized in prefab-local space — world-space
            // radius scales with the mushroom (5× mushroom = 5× trigger).
            SphereCollider trigger = null;
            var existing = mushroom.GetComponents<SphereCollider>();
            for (int i = 0; i < existing.Length; i++)
                if (existing[i].isTrigger) { trigger = existing[i]; break; }
            if (trigger == null)
            {
                trigger = mushroom.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 1.5f;
                trigger.center = new Vector3(0f, 0.6f, 0f);
            }

            if (mushroom.GetComponent<MushroomInteraction>() == null)
                mushroom.AddComponent<MushroomInteraction>();
        }

        var interaction = mushroom.GetComponent<MushroomInteraction>();
        if (interaction != null)
        {
            interaction.spawner = this;
            interaction.bodySlot = bodySlot;
            interaction.cellId = cellId;
            interaction.mushroomScale = scale;
            interaction.colourPct = colourPct;
            interaction.breathPct = breathPct;
            interaction.kaleidoPct = kaleidoPct;
            interaction.mushroomDisplayName = PrettifyName(prefab.name);
            interaction.ClearPlayerInInteractionZone();
        }

        mushroom.transform.SetParent(entry.body.transform, true);
        SpawnerCubeface.SetLayerRecursively(mushroom, SpawnerCubeface.WorldPropLayer);
        entry.activeMushrooms[cellId] = mushroom;
        entry.cellPrefabIdx[cellId] = prefabIdx;

        var fade = mushroom.GetComponent<SpawnFade>();
        if (fade == null) fade = mushroom.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

    static string PrettifyName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "mushroom";
        string s = raw
            .Replace("_big", "")
            .Replace("_little", "")
            .Replace("_", " ")
            .Trim();
        return string.IsNullOrEmpty(s) ? "mushroom" : s;
    }

    // Called by MushroomInteraction when the player eats this mushroom. Marks
    // the cell so the streaming loop won't respawn it later (this play session).
    public void MarkCellConsumed(int bodySlot, long cellId)
    {
        if (bodySlot < 0 || bodySlot >= bodies.Count) return;
        var entry = bodies[bodySlot];
        entry.consumedCells.Add(cellId);
        entry.activeMushrooms.Remove(cellId);
        entry.cellPrefabIdx.Remove(cellId);
    }

    // ─── Save integration ────────────────────────────────────────────────

    public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, long>> GetConsumedCellsWithBody()
    {
        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            string name = entry.body != null ? entry.body.bodyName : "";
            foreach (var c in entry.consumedCells)
                yield return new System.Collections.Generic.KeyValuePair<string, long>(name, c);
        }
        foreach (var kv in pendingConsumedCellsByBody)
            foreach (var c in kv.Value)
                yield return new System.Collections.Generic.KeyValuePair<string, long>(kv.Key, c);
    }

    public void RestoreConsumedCells(System.Collections.Generic.IList<long> cells, System.Collections.Generic.IList<string> bodyNames)
    {
        for (int s = 0; s < bodies.Count; s++) bodies[s].consumedCells.Clear();
        pendingConsumedCellsByBody.Clear();
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
                match.consumedCells.Add(cells[i]);
            }
            else
            {
                if (!pendingConsumedCellsByBody.TryGetValue(name, out var set))
                {
                    set = new HashSet<long>();
                    pendingConsumedCellsByBody[name] = set;
                }
                set.Add(cells[i]);
            }
        }
    }

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

}
