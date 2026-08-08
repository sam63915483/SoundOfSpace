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
        BuildSpawnWeights();

        // Stop this spawner's surface raycast from hitting other spawners'
        // instances (tree, alien, crystal) OR a low-flying ship — see the
        // ShipLayer comment in SpawnerCubeface for the floating-mushroom bug.
        groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask;
    }

    void Update()
    {
        if (!ResolveRefs()) return;
        // Runs on its own slow timer, NOT inside Tick() — Tick is the 4 Hz
        // streaming hot path and a sweep over every consumed cell has no
        // business in it.
        TickWildRespawn(Time.deltaTime);
        tickTimer += Time.deltaTime;
        if (tickTimer < updateInterval) return;
        tickTimer = 0f;
        Tick();
    }

    /// WILD RESPAWN — the payoff for terraforming a world.
    ///
    /// Below the threshold nothing comes back and the land stays picked clean,
    /// which is the pressure that pushes a player from foraging into farming.
    /// Above it, consumed cells trickle back at a rate that climbs with planet
    /// oxygen, so a bloomed world slowly becomes a self-sustaining farm.
    ///
    /// Respawning is just dropping the cell from the consumed set: the streaming
    /// loop rebuilds it from the seed hash on the next Tick, which is why the
    /// species and the size come back EXACTLY as they were with no per-cell
    /// record to store, and why wild density can never exceed its first-landing
    /// value — only originally-seeded cells exist to be restored.
    ///
    /// Gated on the planet-wide surface value (SurfaceO2), deliberately NOT the
    /// local proximity bonus: this is a reward for lifting a whole world, not
    /// for standing in a copse.
    void TickWildRespawn(float dt)
    {
        respawnTimer += dt;
        if (respawnTimer < wildRespawnTickSeconds) return;
        respawnTimer = 0f;

        if (!wildRespawnEnabled || PlanetOxygen.Instance == null) return;

        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            if (entry.body == null || entry.consumedCells.Count == 0) continue;

            float o2 = PlanetOxygen.Instance.SurfaceO2(entry.body) / 100f;
            if (o2 < wildRespawnThreshold) continue;

            // 0 at the threshold, full rate at 100% — so crossing the gate is a
            // trickle you have to look for, not a sudden carpet of mushrooms.
            float t = Mathf.InverseLerp(wildRespawnThreshold, 1f, o2);
            float chance = cellRespawnChanceAt100 * t;
            if (chance <= 0f) continue;

            scratchRespawn.Clear();
            foreach (long id in entry.consumedCells)
                if (UnityEngine.Random.value < chance) scratchRespawn.Add(id);

            for (int i = 0; i < scratchRespawn.Count; i++)
                entry.consumedCells.Remove(scratchRespawn[i]);
        }
    }

    float respawnTimer;
    readonly List<long> scratchRespawn = new List<long>();

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
                // Skip static attractors (the black hole): huge radius → the cell-scan
                // in Tick() explodes to ~250k iterations/tick when you fly close, and the
                // surface raycast hits nothing there so nothing spawns. Pure waste.
                if (b.isStaticAttractor) continue;
                if (IsExcluded(b.bodyName)) continue;
                if (!CanGrowMushroomsOn(b)) continue;
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

    /// A mushroom is flora: it needs soil. Moons and the Sun are barren rock, so
    /// nothing grows there — not even inside a bubble dome, which adds air but
    /// never soil. This is the SAME rule the planting ghost already enforced
    /// (GhostPlacement → TreeSpawner.CanGrowTreesOn); wild mushrooms were the
    /// one path that ignored it and seeded caps across the three moons.
    ///
    /// The primary gate is <see cref="CelestialBody.bodyType"/> rather than a
    /// name list, because the list lives on a serialized inspector field: editing
    /// this file's `excludeBodyNames` default would not touch the value already
    /// baked into the scene's MushroomSpawner, so the moons would still spawn.
    /// bodyType is authored per body and cannot drift out of sync that way.
    /// TreeSpawner's own list is then honoured on top, so a body Sam marks barren
    /// for trees stays barren for mushrooms with no second edit — and because it
    /// is only consulted when the spawner exists, resolve order can't matter.
    public static bool CanGrowMushroomsOn(CelestialBody body)
    {
        if (body == null || body.isStaticAttractor) return false;
        if (body.bodyType != CelestialBody.BodyType.Planet) return false;
        var trees = TreeSpawner.Instance;
        return trees == null || trees.CanGrowTreesOn(body);
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
        prefabIdx = PickWeightedPrefab(hPI);
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
            //
            // A mushroom is a HARVEST NODE now, not a press-F-to-eat prop, so it
            // needs a SOLID collider: the axe's BladeSweep sphere-casts with
            // QueryTriggerInteraction.Ignore and would sweep straight through a
            // trigger. Sized from the prefab's own mesh bounds in local space, so
            // the world-space hitbox scales with the instance like the model does.
            EnsureSolidCollider(mushroom);

            // The old eat-on-interact component is gone from the spawn path.
            // Defensive: strip it off anything that still carries one.
            var legacy = mushroom.GetComponent<MushroomInteraction>();
            if (legacy != null) Destroy(legacy);
        }

        mushroom.transform.SetParent(entry.body.transform, true);
        SpawnerCubeface.SetLayerRecursively(mushroom, SpawnerCubeface.WorldPropLayer);
        entry.activeMushrooms[cellId] = mushroom;
        entry.cellPrefabIdx[cellId] = prefabIdx;

        // AFTER the reparent: Init caches the rest pose + scale for the wobble,
        // and SetParent(worldPositionStays) can rewrite localScale.
        var node = mushroom.GetComponent<SpawnedMushroom>();
        if (node == null) node = mushroom.AddComponent<SpawnedMushroom>();
        node.Init(this, bodySlot, cellId, prefab.name, scale);

        var fade = mushroom.GetComponent<SpawnFade>();
        if (fade == null) fade = mushroom.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

    // ── Rarity-weighted species pick ───────────────────────────────────────
    // Cumulative spawn weights over mushroomPrefabs, from MushroomSpecies
    // (common 5 / uncommon 3 / rare 1). Built once at Awake — weights are
    // authored constants and never change at runtime.
    int[] _cumWeight;
    int _totalWeight;

    void BuildSpawnWeights()
    {
        _cumWeight = new int[mushroomPrefabs.Length];
        _totalWeight = 0;
        for (int i = 0; i < mushroomPrefabs.Length; i++)
        {
            int w = mushroomPrefabs[i] != null
                ? Mathf.Max(1, MushroomSpecies.SpawnWeight(mushroomPrefabs[i].name))
                : 1;
            _totalWeight += w;
            _cumWeight[i] = _totalWeight;
        }
    }

    /// Species for a cell, weighted by rarity. Still driven purely by the
    /// cell's own hash, so the world stays deterministic: the same cell always
    /// grows the same species, exactly as it did with the old modulo pick.
    int PickWeightedPrefab(uint cellHash)
    {
        if (_cumWeight == null || _cumWeight.Length != mushroomPrefabs.Length) BuildSpawnWeights();
        if (_totalWeight <= 0) return (int)(cellHash % (uint)mushroomPrefabs.Length);
        int roll = (int)(cellHash % (uint)_totalWeight);
        for (int i = 0; i < _cumWeight.Length; i++)
            if (roll < _cumWeight[i]) return i;
        return _cumWeight.Length - 1;
    }

    /// Give a streamed mushroom a solid (non-trigger) collider so the axe can
    /// actually hit it. Prefers whatever the prefab already ships with; otherwise
    /// fits a capsule to the mesh bounds in PREFAB-LOCAL space, so the hitbox
    /// scales with the per-instance random scale for free.
    public static void EnsureSolidColliderOn(GameObject mushroom) => EnsureSolidCollider(mushroom);

    static void EnsureSolidCollider(GameObject mushroom)
    {
        var existing = mushroom.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
            if (existing[i] != null && !existing[i].isTrigger) return;   // already solid

        // Local-space bounds of the whole renderer hierarchy.
        var filters = mushroom.GetComponentsInChildren<MeshFilter>(true);
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        Matrix4x4 worldToRoot = mushroom.transform.worldToLocalMatrix;
        for (int i = 0; i < filters.Length; i++)
        {
            var mesh = filters[i].sharedMesh;
            if (mesh == null) continue;
            var mb = mesh.bounds;
            Matrix4x4 toRoot = worldToRoot * filters[i].transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = mb.center + new Vector3(
                    ((c & 1) == 0 ? -mb.extents.x : mb.extents.x),
                    ((c & 2) == 0 ? -mb.extents.y : mb.extents.y),
                    ((c & 4) == 0 ? -mb.extents.z : mb.extents.z));
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        if (!any) return;

        var cap = mushroom.AddComponent<CapsuleCollider>();
        cap.direction = 1;                       // Y — mushrooms stand up
        cap.center = b.center;
        cap.height = Mathf.Max(0.05f, b.size.y);
        cap.radius = Mathf.Max(0.02f, Mathf.Max(b.extents.x, b.extents.z) * 0.75f);
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

    // ── Squish audio (APPENDED — keep new serialized fields at the END) ─────
    // SpawnedMushroom plays these; a planted mushroom has no spawner reference
    // and reaches them through the static Any* helpers below.

    [Header("Squish audio (mushroom chopping)")]
    [Tooltip("Wet squish one-shots played on every axe hit that doesn't fell the mushroom. One is picked at random per hit. Generated placeholders live in Assets/5 - Audio/Mushroom/.")]
    public AudioClip[] hitSquishClips;
    [Tooltip("The bigger squelch played when the mushroom finally breaks. Falls back to a random hit squish if empty.")]
    public AudioClip breakSquishClip;
    [Range(0f, 1f)]
    [Tooltip("Volume for both the hit squishes and the break squelch.")]
    public float squishVolume = 0.85f;

    /// The live spawner, for code that needs the WILD tuning without holding a
    /// reference — planted mushrooms read minScale/maxScale off this so a
    /// player-grown cap can reach the same sizes as one that grew on its own.
    public static MushroomSpawner Instance { get; private set; }

    void OnEnable() { if (Instance == null) Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    /// A size multiplier rolled from the same 1–5× band the wild mushrooms use.
    /// Falls back to the same defaults if no spawner exists yet.
    public static float RollWildScale()
    {
        float lo = Instance != null ? Mathf.Min(Instance.minScale, Instance.maxScale) : 1f;
        float hi = Instance != null ? Mathf.Max(Instance.minScale, Instance.maxScale) : 5f;
        return Random.Range(lo, hi);
    }

    static MushroomSpawner s_audioSource => Instance;

    public AudioClip RandomHitSquish()
    {
        if (hitSquishClips == null || hitSquishClips.Length == 0) return null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var c = hitSquishClips[Random.Range(0, hitSquishClips.Length)];
            if (c != null) return c;
        }
        return null;
    }

    public AudioClip BreakSquish() => breakSquishClip != null ? breakSquishClip : RandomHitSquish();

    /// For mushrooms with no spawner of their own (player-planted ones).
    public static AudioClip AnyHitSquish() => s_audioSource != null ? s_audioSource.RandomHitSquish() : null;
    public static AudioClip AnyBreakSquish() => s_audioSource != null ? s_audioSource.BreakSquish() : null;
    public static float AnySquishVolume() => s_audioSource != null ? s_audioSource.squishVolume : 0.85f;

    // -- appended; keep new fields at the END (serialization) --

    [Header("Wild respawn (terraforming payoff)")]
    [Tooltip("Master switch. OFF restores the original behaviour exactly: harvested wild cells never come back.")]
    [SerializeField] bool wildRespawnEnabled = true;

    [Tooltip("Planet-wide surface O2 (0-1) at which wild mushrooms start coming back.\n\nIMPORTANT: Humble Abode already STARTS at roughly 0.55 from its seed forest, so a threshold of 0.50 would be satisfied on a fresh save and depletion would never bite. 0.75 sits well above the starting value, so reaching it is a real terraforming project — worth roughly 150 planted trees, or a long stretch of dome venting.\n\nIf you retune the seed forest or treesForFullO2PerMillionSqm, re-check this number against the planet's actual starting O2 or the gate silently opens on day one.")]
    [SerializeField] float wildRespawnThreshold = 0.75f;

    [Tooltip("Real seconds between respawn rolls. Each consumed cell gets one roll per tick, so this and the chance below together set the regrowth rate.")]
    [SerializeField] float wildRespawnTickSeconds = 60f;

    [Tooltip("Per-cell chance per tick at 100% planet O2, scaling linearly down to 0 at the threshold. 0.01 means a fully bloomed world brings back about 1% of its picked cells a minute — visible regrowth over a session, not an instant refill.")]
    [SerializeField] float cellRespawnChanceAt100 = 0.01f;
}
