using System.Collections.Generic;
using UnityEngine;

public class CrystalSpawner : MonoBehaviour
{
    [Header("Planets")]
    [Tooltip("Body names to skip (case-sensitive, matched against CelestialBody.bodyName). Crystals grow on every other body in NBodySimulation.Bodies.")]
    public string[] excludeBodyNames = { "Sun" };

    [Header("Crystal Prefab")]
    [Tooltip("Single crystal prefab — assign crystal_17_2 here.")]
    public GameObject crystalPrefab;

    [Header("Spawn")]
    [Tooltip("Crystals only exist within this distance of the player. Matches mushroom / alien NPC streaming radius.")]
    public float spawnRadius = 300f;
    [Tooltip("Fallback cap when InputSettings is not assigned. The pause-menu slider overrides this at runtime via inputSettings.maxCrystals.")]
    public int maxCrystals = 20;
    [Tooltip("Optional. When assigned, the spawner reads maxCrystals from this asset every tick — the GRAPHICS-tab slider drives it live.")]
    public InputSettings inputSettings;
    [Tooltip("Layers the surface raycast should hit. Should include terrain, exclude water/ship/player.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Push the crystal this far into the ground along the planet-radial axis. Positive = into the ground. Constant — not scaled by the random per-instance scale.")]
    public float groundOffset = 0f;
    [Tooltip("Additional downward push proportional to spawn scale. Catches the residual gap that varies per prefab (where the measured mesh bottom doesn't exactly match the visible bottom). 0.04 = 4cm of extra embed per scale unit. Bumps 'floaters' down without burying small ones.")]
    public float groundEmbedPerScale = 0.04f;

    [Header("Determinism")]
    [Tooltip("Change to reroll the whole crystal layout. Use a different value than TreeSpawner / MushroomSpawner / AlienNPCSpawner so distributions don't overlap.")]
    public int seed = 11357;
    [Tooltip("Cell size in metres. Larger = crystals spaced further apart.")]
    public float cellSize = 60f;
    [Range(0f, 1f)]
    [Tooltip("Probability that any given cell holds a crystal. Mid-range default — players who want them rarer can lower it.")]
    public float crystalSpawnChance = 0.35f;
    [Tooltip("Maximum slope (degrees from radial-up) where a crystal may spawn. Above this, the cell is rejected — keeps crystals off cliffs.")]
    [Range(0f, 90f)]
    public float maxSurfaceAngle = 35f;

    [Header("Variation")]
    [Tooltip("Minimum uniform scale multiplier applied at spawn (rolled deterministically per cell).")]
    public float minScale = 1f;
    [Tooltip("Maximum uniform scale multiplier applied at spawn (rolled deterministically per cell).")]
    public float maxScale = 3f;
    [Tooltip("Exponent applied to the per-cell uniform roll before lerping between minScale and maxScale. 1 = uniform (25/50/25 for 1×/2×/3×), 2 = small-biased (~50/37/13), 3 = strongly small-biased (~63/28/9). Higher = bigger crystals are rarer.")]
    [Range(1f, 5f)]
    public float scaleBiasExponent = 2f;

    [Header("Performance")]
    [Tooltip("Seconds between spawn/despawn passes.")]
    public float updateInterval = 0.25f;
    [Tooltip("Height above the planet surface where the downward raycast originates.")]
    public float surfaceRayHeight = 100f;

    [Header("Audio")]
    [Tooltip("Optional. Played at the crystal's world position when it breaks. Leave null for silent break.")]
    public AudioClip crystalBreakClip;
    [Range(0f, 1f)]
    public float crystalBreakVolume = 0.7f;

    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, GameObject> activeCrystals = new Dictionary<long, GameObject>();
        public readonly HashSet<long> consumedCells = new HashSet<long>();
    }

    readonly List<BodyState> bodies = new List<BodyState>();
    // Pre-applied consumed cells keyed by bodyName, populated by
    // RestoreConsumedCells before bodies have resolved. Drained into
    // BodyState.consumedCells at resolve. Mirrors AlienNPCSpawner's
    // pendingKilledCellsByBody pattern so save/load is symmetric.
    readonly Dictionary<string, HashSet<long>> pendingConsumedCellsByBody = new Dictionary<string, HashSet<long>>();
    PlayerController player;
    Stack<GameObject> pool;
    // Lowest Y of the prefab's renderer hierarchy in prefab-root local space.
    // Used to seat the model so its bottom sits on the surface regardless of pivot.
    float _prefabLocalBottomY;
    // The prefab's authored localScale, captured at Awake. Unlike the
    // mushroom / tree prefabs (which ship at scale 1), crystal_17_2 ships at
    // ~16.987 to compensate for its tiny FBX units. We multiply the per-
    // spawn random scale by this so the visual matches what the artist
    // authored — instancing at Vector3.one*scale would shrink it ~17×.
    Vector3 _prefabBaseScale = Vector3.one;
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
        if (crystalPrefab == null)
        {
            Debug.LogWarning("[CrystalSpawner] No crystal prefab assigned; spawner will stay idle.");
            enabled = false;
            return;
        }
        pool = new Stack<GameObject>();
        _prefabLocalBottomY = SpawnerCubeface.ComputeLocalBottomY(crystalPrefab);
        _prefabBaseScale = crystalPrefab.transform.localScale;
        if (_prefabBaseScale == Vector3.zero) _prefabBaseScale = Vector3.one;

        // Defensively mask out the WorldProp layer so this spawner's surface
        // raycast can't land on another spawner's instance (tree, mushroom,
        // alien, or another crystal). Stops "mushroom spawned 8m above ground
        // because the raycast hit a crystal first" cross-contamination.
        // Also masks out the Ship layer so a low-flying ship doesn't act as a
        // false surface, leaving crystals floating in the orbit path after the
        // ship moves on. Robust against inspector misconfigurations — we
        // OR-out both bits regardless of what the serialized groundMask was.
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
                // Skip static attractors (the black hole): huge radius → the cell-scan
                // in Tick() explodes to ~250k iterations/tick when you fly close, and the
                // surface raycast hits nothing there so nothing spawns. Pure waste.
                if (b.isStaticAttractor) continue;
                if (IsExcluded(b.bodyName)) continue;
                var entry = new BodyState
                {
                    body = b,
                    gen = b.GetComponentInChildren<CelestialBodyGenerator>(),
                };
                // Drain any pre-applied consumed cells from RestoreConsumedCells
                // that were queued before this body resolved.
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
        for (int i = 0; i < bodies.Count; i++) n += bodies[i].activeCrystals.Count;
        return n;
    }

    void Tick()
    {
        Vector3 playerPos = GetViewerPosition();

        // Live-read the cap from InputSettings if assigned (pause-menu slider
        // drives this), otherwise fall back to the spawner's own field.
        int effectiveMax = inputSettings != null
            ? Mathf.Clamp(inputSettings.maxCrystals, 0, 1000)
            : maxCrystals;

        for (int s = 0; s < bodies.Count; s++) DespawnOutOfRange(bodies[s], playerPos, spawnRadius);

        if (effectiveMax <= 0)
        {
            EnforceMaxCrystals(playerPos, 0);
            return;
        }

        scratchCandidates.Clear();
        float prefilterMax = spawnRadius + cellSize;
        float prefilterMaxSq = prefilterMax * prefilterMax;

        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            if (entry.body == null) continue;
            float bodyDistSq = (entry.body.Position - playerPos).sqrMagnitude;
            float bodyOuter = spawnRadius + entry.body.radius + cellSize;
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
                        if (entry.activeCrystals.ContainsKey(id)) continue;
                        if (!CellHasCrystal(face, cu, cv)) continue;
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
            if (!TryComputeCrystalPlacement(entry, c.face, c.cellU, c.cellV, faceUVPerCell, playerPos, spawnRadius,
                                            out Vector3 pos, out Quaternion rot, out float scale))
                continue;
            SpawnCrystal(entry, c.bodySlot, SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV), pos, rot, scale);
        }

        EnforceMaxCrystals(playerPos, effectiveMax);
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
        foreach (var kv in entry.activeCrystals)
        {
            if (kv.Value == null) { scratchRemove.Add(kv.Key); continue; }
            if ((kv.Value.transform.position - playerPos).sqrMagnitude > limitSq)
                scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnInternal(entry, scratchRemove[i]);
    }

    void EnforceMaxCrystals(Vector3 playerPos, int max)
    {
        while (CountActive() > max)
        {
            BodyState farthestEntry = null;
            long farthestId = 0;
            float farthestSq = -1f;
            for (int s = 0; s < bodies.Count; s++)
            {
                var entry = bodies[s];
                foreach (var kv in entry.activeCrystals)
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

    bool CellHasCrystal(int face, int cellU, int cellV)
    {
        uint h = SpawnerCubeface.Hash(seed, face, cellU, cellV, 1);
        return (h & 0xFFFFu) / 65535f < crystalSpawnChance;
    }

    bool TryComputeCrystalPlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                                    Vector3 playerPos, float effectiveRadius,
                                    out Vector3 pos, out Quaternion rot, out float scale)
    {
        pos = default; rot = default; scale = 1f;

        uint hJU = SpawnerCubeface.Hash(seed, face, cellU, cellV, 2);
        uint hJV = SpawnerCubeface.Hash(seed, face, cellU, cellV, 3);
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
        float lo = Mathf.Min(minScale, maxScale);
        float hi = Mathf.Max(minScale, maxScale);
        // Bias toward small. Uniform t would give 25/50/25 odds for 1×/2×/3×
        // after the RoundToInt(scale) in SpawnedCrystal.Init; squaring shifts
        // that to ~50/37/13 — small crystals dominate, big ones feel rare.
        // Bump scaleBiasExponent above 2 (e.g. 3) for an even stronger skew.
        float tScale = (hSC & 0xFFFFu) / 65535f;
        tScale = Mathf.Pow(tScale, scaleBiasExponent);
        scale = Mathf.Lerp(lo, hi, tScale);
        // _prefabLocalBottomY is in prefab-local coords with the prefab's
        // authored localScale stripped out (InverseTransformPoint divides it
        // away). Since we now spawn at _prefabBaseScale * scale, multiply the
        // offset by _prefabBaseScale.y so the seating math converts the
        // mesh-bottom into world metres correctly. Without this factor the
        // crystals sit 1/17th of their visible size deep in the ground.
        float effectiveBottomY = _prefabLocalBottomY * _prefabBaseScale.y;
        pos = hit.point - up * (effectiveBottomY * scale + groundOffset + groundEmbedPerScale * scale);
        if (SpawnExclusionZone.IsExcluded(pos)) return false;   // keep clear of the ship school etc.
        return true;
    }

    void SpawnCrystal(BodyState entry, int bodySlot, long cellId, Vector3 pos, Quaternion rot, float scale)
    {
        if (crystalPrefab == null) return;
        if (entry == null || entry.body == null) return;
        if (pool == null) pool = new Stack<GameObject>();

        GameObject crystal;
        if (pool.Count > 0)
        {
            crystal = pool.Pop();
            crystal.transform.SetPositionAndRotation(pos, rot);
            crystal.transform.localScale = _prefabBaseScale * scale;
            crystal.SetActive(true);
        }
        else
        {
            crystal = Instantiate(crystalPrefab, pos, rot);
            crystal.transform.localScale = _prefabBaseScale * scale;

            // First-time setup. The crystal_17_2 prefab ships with no
            // collider, so add one that hugs the visible mesh. MUST be a
            // mesh-shaped collider, not a unit-sphere — the prefab is
            // authored at scale 16.987, so a SphereCollider with the
            // default (0, 0.5, 0) center / r=0.5 ends up ~17 m wide and
            // ~8 m underground. Two visible bugs from that:
            //   (a) Underground half overlaps the terrain mesh, and
            //       the physics solver pushes the player down through
            //       the terrain into the gap (fall-through bug).
            //   (b) MushroomSpawner / TreeSpawner / AlienNPCSpawner's
            //       surface raycasts (from 100 m above the planet) hit
            //       the top of the giant sphere ~8 m above ground and
            //       place their objects there (floating-objects bug).
            // A convex MeshCollider matching the actual mesh fixes both
            // — its bounds equal the visible bounds, so no underground
            // extent and no over-ground extent. Convex so it can interact
            // with dynamic rigidbodies without restrictions.
            var existing = crystal.GetComponentsInChildren<Collider>(true);
            if (existing == null || existing.Length == 0)
            {
                var mf = crystal.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                {
                    var mc = crystal.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                }
            }

            if (crystal.GetComponent<SpawnedCrystal>() == null)
                crystal.AddComponent<SpawnedCrystal>();
        }

        crystal.transform.SetParent(entry.body.transform, true);
        entry.activeCrystals[cellId] = crystal;

        // Set the whole hierarchy to the WorldProp layer so other spawners'
        // surface raycasts (which mask the layer out in their own Awake)
        // skip this crystal. Reapply on pool reuse — cheap, idempotent.
        SpawnerCubeface.SetLayerRecursively(crystal, SpawnerCubeface.WorldPropLayer);

        // Init AFTER SetParent. Init captures _restRotation = transform.localRotation
        // as the "neutral" pose the shake routine returns to. SetParent(planet, true)
        // re-computes localRotation to keep worldRotation constant under the planet's
        // current world orientation — so capturing _restRotation BEFORE SetParent
        // freezes the pre-parent localRotation, then the shake snaps the crystal
        // into that wrong orientation on first hit and leaves it there.
        var sc = crystal.GetComponent<SpawnedCrystal>();
        if (sc != null) sc.Init(this, bodySlot, cellId, scale);

        var fade = crystal.GetComponent<SpawnFade>();
        if (fade == null) fade = crystal.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

    // Called by SpawnedCrystal when it breaks. Marks the cell so the streaming
    // loop won't respawn it later (this play session).
    public void MarkCellMined(int bodySlot, long cellId)
    {
        if (bodySlot < 0 || bodySlot >= bodies.Count) return;
        var entry = bodies[bodySlot];
        entry.consumedCells.Add(cellId);
        entry.activeCrystals.Remove(cellId);
    }

    // ─── Save integration ────────────────────────────────────────────────

    // Streamed iterator: yields (bodyName, cellId) for every mined crystal cell.
    // Includes both already-resolved entries and the pending queue (for the
    // case where Capture runs before bodies have resolved).
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

    // Apply a list of (bodyName, cellId) pairs from a save. Bodies not yet
    // resolved get queued in pendingConsumedCellsByBody for ResolveRefs to
    // drain on first tick.
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
        if (!entry.activeCrystals.TryGetValue(cellId, out var crystal)) return;
        entry.activeCrystals.Remove(cellId);
        if (crystal == null) return;

        var fade = crystal.GetComponent<SpawnFade>();
        if (fade != null)
        {
            fade.BeginFadeOut(() => ReturnCrystalToPool(crystal));
        }
        else
        {
            ReturnCrystalToPool(crystal);
        }
    }

    void ReturnCrystalToPool(GameObject crystal)
    {
        if (crystal == null) return;
        crystal.transform.SetParent(null, true);
        crystal.SetActive(false);
        pool.Push(crystal);
    }

}
