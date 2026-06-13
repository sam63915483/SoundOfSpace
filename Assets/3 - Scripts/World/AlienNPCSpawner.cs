using System.Collections.Generic;
using UnityEngine;

public class AlienNPCSpawner : MonoBehaviour
{
    [Header("Planets")]
    [Tooltip("Body names to skip (case-sensitive, matched against CelestialBody.bodyName). The spawner runs on every other body in NBodySimulation.Bodies.")]
    public string[] excludeBodyNames = { "Sun" };

    [Header("Alien Prefabs")]
    [Tooltip("Drag all 10 alien toy prefabs here. Each spawn picks one deterministically per cell.")]
    public GameObject[] alienPrefabs;

    [Header("Spawn")]
    [Tooltip("Aliens only exist within this distance of the player. Larger than tree radius because aliens are sparser.")]
    public float spawnRadius = 300f;
    [Tooltip("Fallback cap when InputSettings is not assigned. The pause-menu slider overrides this at runtime.")]
    public int maxAlienNPCs = 10;
    [Tooltip("Optional. When assigned, the spawner reads maxAlienNPCs from this asset every tick — the slider drives it live.")]
    public InputSettings inputSettings;
    [Tooltip("Layers the surface raycast should hit. Should include terrain, exclude water/ship/player.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Push the alien this far into the ground along the planet-radial axis. Positive = into the ground. Constant — not scaled by the random per-instance scale.")]
    public float groundOffset = 0f;
    [Tooltip("Additional downward push proportional to spawn scale. Catches the residual gap that varies per prefab (where the measured mesh bottom doesn't exactly match the visible feet). 0.04 = 4cm of extra embed per scale unit, so a 5×-scaled alien is pushed 20cm deeper than a 2×-scaled one.")]
    public float groundEmbedPerScale = 0.04f;

    const float BaselineRadius = 300f;

    [Header("Determinism")]
    [Tooltip("Change to reroll the whole alien layout. Use a different value than TreeSpawner so distributions don't overlap.")]
    public int seed = 98765;
    [Tooltip("Cell size in metres. Larger = aliens spaced further apart.")]
    public float cellSize = 50f;
    [Range(0f, 1f)]
    [Tooltip("Probability that any given cell holds an alien. Lower than tree chance → rarer per cell, but the cap still controls the visible total.")]
    public float alienSpawnChance = 0.40f;
    [Tooltip("Maximum slope (degrees from radial-up) where an alien may spawn. Above this, the cell is rejected — keeps NPCs off cliffs.")]
    [Range(0f, 90f)]
    public float maxSurfaceAngle = 35f;

    [Header("Variation")]
    [Tooltip("Minimum uniform scale multiplier applied at spawn (rolled deterministically per cell).")]
    public float minScale = 2f;
    [Tooltip("Maximum uniform scale multiplier applied at spawn (rolled deterministically per cell).")]
    public float maxScale = 5f;

    [Header("Trigger Collider (added at spawn for F-to-talk)")]
    [Tooltip("Trigger box size in *prefab-local* space. Multiplied by the random scale at spawn time.")]
    public Vector3 triggerSize = new Vector3(2.5f, 4f, 2.5f);
    [Tooltip("Trigger box centre in *prefab-local* space. Y > 0 lifts the trigger so it covers the body, not the feet.")]
    public Vector3 triggerCenter = new Vector3(0f, 2f, 0f);

    [Header("Combat Audio")]
    [Tooltip("Played when a spawned alien takes a non-fatal hit. Drag a clip here.")]
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 0.7f;
    [Tooltip("Played once when a spawned alien is killed and ragdolls.")]
    public AudioClip deathSound;
    [Range(0f, 1f)] public float deathVolume = 0.8f;

    [Header("Dialogue Voice")]
    [Tooltip("Looping voice clip played while a spawned alien's dialogue text types out (press F to talk). Same clip for every spawned alien. RandomAlienDialogue is added at runtime, so the clip is assigned HERE, not on a prefab.")]
    public AudioClip voiceClip;
    [Range(0f, 1f)] public float voiceVolume = 0.3f;

    [Header("Health Bar")]
    [Tooltip("World-space metres above the alien's feet where the health bar appears. Same height for every alien regardless of its random scale.")]
    public float healthBarWorldHeight = 2.5f;

    [Header("Hit Box (live, while alive)")]
    [Tooltip("World-space height of the capsule that registers pistol/axe shots. Bigger = easier to hit. Same size on every alien.")]
    public float hitBoxWorldHeight = 2.5f;
    [Tooltip("World-space radius of the hit capsule. Bigger = easier to hit (forgives off-centre aim).")]
    public float hitBoxWorldRadius = 0.9f;
    [Tooltip("Scales the live-alien nudge from each pistol/axe hit. Pistol passes 1.5m and axe passes 3m; 0.2 means the alien lurches 0.3m or 0.6m respectively. Lower = subtler, higher = bigger flinch.")]
    [Range(0f, 1f)] public float liveKnockbackScale = 0.2f;

    [Header("Performance")]
    [Tooltip("Seconds between spawn/despawn passes. 0.25 is responsive without being wasteful.")]
    public float updateInterval = 0.25f;
    [Tooltip("Height above the planet surface where the downward raycast originates.")]
    public float surfaceRayHeight = 100f;

    // Per-body streaming + kill state. Slot index is the position in `bodies`.
    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, GameObject> activeAliens = new Dictionary<long, GameObject>();
        public readonly HashSet<long> killedCells = new HashSet<long>();
    }

    readonly List<BodyState> bodies = new List<BodyState>();
    // Pre-applied killed cells keyed by bodyName — populated by RestoreKilledCells
    // before bodies have resolved. Drained into BodyState.killedCells at resolve.
    readonly Dictionary<string, HashSet<long>> pendingKilledCellsByBody = new Dictionary<string, HashSet<long>>();
    PlayerController player;
    Stack<GameObject>[] pools;
    // Per-prefab "minimum Y in prefab-root local space" — the lowest renderer
    // point of the prefab. Used so the spawn formula seats the model with its
    // bottom on the surface regardless of whether the artist authored the
    // pivot at the feet, hips, or somewhere else. See MushroomSpawner for the
    // full rationale and math; same approach, same helper shape.
    float[] _prefabLocalBottomY;
    readonly HashSet<string> killedPrePlacedNames = new HashSet<string>();
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
        if (alienPrefabs == null || alienPrefabs.Length == 0)
        {
            Debug.LogWarning("[AlienNPCSpawner] No alien prefabs assigned; spawner will stay idle.");
            enabled = false;
            return;
        }
        pools = new Stack<GameObject>[alienPrefabs.Length];
        for (int i = 0; i < pools.Length; i++) pools[i] = new Stack<GameObject>();
        _prefabLocalBottomY = new float[alienPrefabs.Length];
        for (int i = 0; i < alienPrefabs.Length; i++)
            _prefabLocalBottomY[i] = SpawnerCubeface.ComputeLocalBottomY(alienPrefabs[i]);

        // Stop this spawner's surface raycast from hitting other spawners'
        // instances (tree, mushroom, crystal) OR a low-flying ship — see the
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
                if (pendingKilledCellsByBody.TryGetValue(b.bodyName, out var pending))
                {
                    foreach (var c in pending) entry.killedCells.Add(c);
                    pendingKilledCellsByBody.Remove(b.bodyName);
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
        for (int i = 0; i < bodies.Count; i++) n += bodies[i].activeAliens.Count;
        return n;
    }

    void Tick()
    {
        Vector3 playerPos = GetViewerPosition();

        float effectiveRadius = (inputSettings != null)
            ? Mathf.Clamp(inputSettings.viewDistance, 100f, 1000f)
            : spawnRadius;
        int baseCap = (inputSettings != null)
            ? Mathf.Clamp(inputSettings.maxAlienNPCs, 1, 1000)
            : maxAlienNPCs;
        int effectiveMax = Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * (effectiveRadius / BaselineRadius)));

        // Despawn first so freed cells/budget are immediately reusable below.
        for (int s = 0; s < bodies.Count; s++) DespawnOutOfRange(bodies[s], playerPos, effectiveRadius);

        scratchCandidates.Clear();
        float prefilterMax = effectiveRadius + cellSize;
        float prefilterMaxSq = prefilterMax * prefilterMax;

        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            if (entry.body == null) continue;
            // Cheap distance prune: if the player is far outside this planet's
            // spawn range, skip its whole cell sweep.
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
                        if (entry.killedCells.Contains(id)) continue;
                        if (entry.activeAliens.ContainsKey(id)) continue;
                        if (!CellHasAlien(face, cu, cv)) continue;
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
            if (!TryComputeAlienPlacement(entry, c.face, c.cellU, c.cellV, faceUVPerCell, playerPos, effectiveRadius,
                                          out Vector3 pos, out Quaternion rot, out int prefabIdx, out float scale))
                continue;
            SpawnAlien(entry, c.bodySlot, SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV), prefabIdx, pos, rot, scale);
        }

        EnforceMaxAliens(playerPos, effectiveMax);
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
        foreach (var kv in entry.activeAliens)
        {
            if (kv.Value == null) { scratchRemove.Add(kv.Key); continue; }
            if ((kv.Value.transform.position - playerPos).sqrMagnitude > limitSq)
                scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnInternal(entry, scratchRemove[i]);
    }

    void EnforceMaxAliens(Vector3 playerPos, int max)
    {
        while (CountActive() > max)
        {
            BodyState farthestEntry = null;
            long farthestId = 0;
            float farthestSq = -1f;
            for (int s = 0; s < bodies.Count; s++)
            {
                var entry = bodies[s];
                foreach (var kv in entry.activeAliens)
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

    bool CellHasAlien(int face, int cellU, int cellV)
    {
        uint h = SpawnerCubeface.Hash(seed, face, cellU, cellV, 1);
        return (h & 0xFFFFu) / 65535f < alienSpawnChance;
    }

    bool TryComputeAlienPlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                                   Vector3 playerPos, float effectiveRadius, out Vector3 pos, out Quaternion rot,
                                   out int prefabIdx, out float scale)
    {
        pos = default; rot = default; prefabIdx = 0; scale = 1f;

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

        // Reject cliffs — humanoids on near-vertical surfaces look broken.
        if (Vector3.Angle(hit.normal, up) > maxSurfaceAngle) return false;

        float yaw = (hY & 0xFFFFu) / 65535f * 360f;
        rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);
        prefabIdx = (int)(hPI % (uint)alienPrefabs.Length);
        float lo = Mathf.Min(minScale, maxScale);
        float hi = Mathf.Max(minScale, maxScale);
        scale = Mathf.Lerp(lo, hi, (hSC & 0xFFFFu) / 65535f);
        // Seat the NPC so the lowest point of its mesh sits on hit.point,
        // plus a small scale-proportional embed for per-prefab variance.
        // groundOffset is a constant fine-tune; groundEmbedPerScale scales
        // with the model size.
        float bottomY = (prefabIdx >= 0 && _prefabLocalBottomY != null && prefabIdx < _prefabLocalBottomY.Length)
            ? _prefabLocalBottomY[prefabIdx]
            : 0f;
        pos = hit.point - up * (bottomY * scale + groundOffset + groundEmbedPerScale * scale);
        if (SpawnExclusionZone.IsExcluded(pos)) return false;   // keep clear of the ship school etc.
        return true;
    }

    void SpawnAlien(BodyState entry, int bodySlot, long cellId, int prefabIdx, Vector3 pos, Quaternion rot, float scale)
    {
        if (prefabIdx < 0 || prefabIdx >= alienPrefabs.Length) prefabIdx = 0;
        var prefab = alienPrefabs[prefabIdx];
        if (prefab == null) return;
        if (entry == null || entry.body == null) return;
        // Defensive lazy-init. Awake normally sets pools, but if a domain
        // reload or duplicate-component edge case leaves pools null on the
        // first frame, this rebuilds rather than throwing.
        if (pools == null) pools = new Stack<GameObject>[alienPrefabs.Length];
        if (pools[prefabIdx] == null) pools[prefabIdx] = new Stack<GameObject>();

        GameObject alien;
        var pool = pools[prefabIdx];
        if (pool.Count > 0)
        {
            alien = pool.Pop();
            alien.transform.SetPositionAndRotation(pos, rot);
            alien.transform.localScale = Vector3.one * scale;
            alien.SetActive(true);
        }
        else
        {
            alien = Instantiate(prefab, pos, rot);
            alien.transform.localScale = Vector3.one * scale;

            // First-time setup of components that the prefab doesn't ship with.
            if (alien.GetComponent<NPCWaveAnimation>() == null)
                alien.AddComponent<NPCWaveAnimation>();

            BoxCollider trigger = null;
            var existingCols = alien.GetComponents<BoxCollider>();
            for (int i = 0; i < existingCols.Length; i++)
            {
                if (existingCols[i].isTrigger) { trigger = existingCols[i]; break; }
            }
            if (trigger == null)
            {
                trigger = alien.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = triggerSize;
                trigger.center = triggerCenter;
            }

            if (alien.GetComponent<RandomAlienDialogue>() == null)
                alien.AddComponent<RandomAlienDialogue>();

            if (alien.GetComponent<AlienNPCDamageable>() == null)
                alien.AddComponent<AlienNPCDamageable>();
        }

        // Copy combat audio refs onto the damageable every spawn — handles
        // both the fresh-instantiate path (component just added with default
        // null clips) and the pool path (clips might have been changed in
        // the inspector since the prefab was last reused).
        var dmg = alien.GetComponent<AlienNPCDamageable>();
        if (dmg != null)
        {
            dmg.hitSound = hitSound;
            dmg.hitVolume = hitVolume;
            dmg.deathSound = deathSound;
            dmg.deathVolume = deathVolume;
            dmg.healthBarWorldHeight = healthBarWorldHeight;
            dmg.liveColliderWorldHeight = hitBoxWorldHeight;
            dmg.liveColliderWorldRadius = hitBoxWorldRadius;
            dmg.liveKnockbackScale = liveKnockbackScale;
            // Rebuild the bar position AND the live capsule from the new
            // values in case they changed after Awake (or after pool reuse).
            dmg.RefreshHealthBarPosition();
            dmg.RefreshLiveCollider();
        }

        // Inject the dialogue voice the same way — RandomAlienDialogue is added
        // at runtime, so its clip can't be set on a prefab; it comes from the
        // spawner's voiceClip every spawn (handles fresh + pooled aliens).
        var dlg = alien.GetComponent<RandomAlienDialogue>();
        if (dlg != null) dlg.SetVoice(voiceClip, voiceVolume);

        alien.transform.SetParent(entry.body.transform, true);
        SpawnerCubeface.SetLayerRecursively(alien, SpawnerCubeface.WorldPropLayer);

        var marker = alien.GetComponent<SpawnedAlienNPC>();
        if (marker == null) marker = alien.AddComponent<SpawnedAlienNPC>();
        marker.Init(this, bodySlot, cellId, prefabIdx);
        entry.activeAliens[cellId] = alien;

        var fade = alien.GetComponent<SpawnFade>();
        if (fade == null) fade = alien.AddComponent<SpawnFade>();
        fade.BeginFadeIn();
    }

    public void MarkCellKilled(int bodySlot, long cellId)
    {
        if (bodySlot < 0 || bodySlot >= bodies.Count) return;
        var entry = bodies[bodySlot];
        entry.killedCells.Add(cellId);
        // Remove from active so the streaming loop doesn't try to despawn the
        // corpse via the pool path. The corpse owns its own Destroy(corpseLifetime)
        // schedule from AlienNPCDamageable.Die.
        entry.activeAliens.Remove(cellId);
    }

    public void MarkPrePlacedKilled(string npcName)
    {
        if (!string.IsNullOrEmpty(npcName)) killedPrePlacedNames.Add(npcName);
    }

    // Called by SpawnedAlienNPC at OnEnable so it can resolve its body name
    // for the save system without holding a hot reference to BodyState.
    public string GetBodyName(int bodySlot)
    {
        if (bodySlot < 0 || bodySlot >= bodies.Count) return "";
        var b = bodies[bodySlot].body;
        return b != null ? b.bodyName : "";
    }

    // ─── Save integration ────────────────────────────────────────────────

    // Streamed iterator: yields (bodyName, cellId) for every killed cell.
    public IEnumerable<KeyValuePair<string, long>> GetKilledCellsWithBody()
    {
        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            string name = entry.body != null ? entry.body.bodyName : "";
            foreach (var c in entry.killedCells)
                yield return new KeyValuePair<string, long>(name, c);
        }
        // Pending entries that haven't yet attached to a resolved body still
        // count — surface them so a save/load round-trip is lossless even if
        // the body resolved late (e.g. captured pre-resolve, restored after).
        foreach (var kv in pendingKilledCellsByBody)
            foreach (var c in kv.Value)
                yield return new KeyValuePair<string, long>(kv.Key, c);
    }

    public IEnumerable<string> GetKilledPrePlacedNames() => killedPrePlacedNames;

    // Apply a list of (bodyName, cellId) pairs. Bodies not yet resolved get
    // queued in pendingKilledCellsByBody for ResolveRefs to drain.
    public void RestoreKilledCells(IList<long> cells, IList<string> bodyNames)
    {
        for (int s = 0; s < bodies.Count; s++) bodies[s].killedCells.Clear();
        pendingKilledCellsByBody.Clear();
        if (cells == null || cells.Count == 0) return;

        for (int i = 0; i < cells.Count; i++)
        {
            // Legacy saves omit bodyNames — those cells were all on Humble Abode.
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
                match.killedCells.Add(cells[i]);
            }
            else
            {
                if (!pendingKilledCellsByBody.TryGetValue(name, out var set))
                {
                    set = new HashSet<long>();
                    pendingKilledCellsByBody[name] = set;
                }
                set.Add(cells[i]);
            }
        }
    }

    public void RestoreKilledPrePlacedNames(IList<string> names)
    {
        killedPrePlacedNames.Clear();
        if (names == null) return;
        for (int i = 0; i < names.Count; i++)
            if (!string.IsNullOrEmpty(names[i])) killedPrePlacedNames.Add(names[i]);
    }

    // Wipes any currently-streamed aliens. Used by the save apply path: the
    // 1-frame defer before Apply means the spawner may have ticked once and
    // produced live aliens in cells the save says are dead — clearing first
    // and letting the streaming loop repopulate (skipping killedCells) is
    // simpler than reconciling cell-by-cell.
    public void ClearAllActiveAliens()
    {
        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            foreach (var kv in entry.activeAliens)
                if (kv.Value != null) Destroy(kv.Value);
            entry.activeAliens.Clear();
        }
        if (pools != null)
        {
            for (int i = 0; i < pools.Length; i++)
            {
                while (pools[i].Count > 0)
                {
                    var go = pools[i].Pop();
                    if (go != null) Destroy(go);
                }
            }
        }
    }

    void DespawnInternal(BodyState entry, long cellId)
    {
        if (!entry.activeAliens.TryGetValue(cellId, out var alien)) return;
        entry.activeAliens.Remove(cellId);
        if (alien == null) return;
        var marker = alien.GetComponent<SpawnedAlienNPC>();
        int idx = marker != null ? marker.PrefabIndex : 0;
        if (idx < 0 || idx >= pools.Length) idx = 0;

        var fade = alien.GetComponent<SpawnFade>();
        if (fade != null)
        {
            int capturedIdx = idx;
            fade.BeginFadeOut(() => ReturnAlienToPool(alien, capturedIdx));
        }
        else
        {
            ReturnAlienToPool(alien, idx);
        }
    }

    void ReturnAlienToPool(GameObject alien, int poolIdx)
    {
        if (alien == null) return;
        alien.transform.SetParent(null, true);
        alien.SetActive(false);
        if (poolIdx < 0 || poolIdx >= pools.Length) poolIdx = 0;
        pools[poolIdx].Push(alien);
    }

}
