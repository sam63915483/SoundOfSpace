using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Streams space cats onto planet surfaces, the same way
/// <see cref="AlienNPCSpawner"/> streams aliens: a deterministic hash of
/// (seed, body, cubeface cell) decides where a cat lives, cats near the player
/// are instantiated, cats that fall out of range go back to a pool. Because it
/// is a hash and not a spawn list, the same cat is always in the same place —
/// which is what lets it work in co-op with no replication, and what makes it
/// survive a save/load without storing anything.
///
/// Deliberately much smaller than the alien spawner: cats cannot be killed, so
/// there are no killed cells, no corpses, no damage plumbing and no save state.
///
/// Each spawned cat gets, at runtime:
///   Animator + <see cref="CatAnimation"/>  — the pack ships clips but no controller
///   <see cref="AlienWander"/>              — proven planet-surface walking, procedural legs OFF
///   <see cref="SpaceCat"/>                 — the loafing/curious brain and F-to-interact
///   a trigger BoxCollider                  — what Interactable listens on
/// </summary>
public class CatSpawner : MonoBehaviour
{
    [Header("Planets")]
    [Tooltip("Body names to skip (matched against CelestialBody.bodyName).")]
    public string[] excludeBodyNames = { "Sun" };

    [Header("Cat Prefabs")]
    [Tooltip("The eleven IndieCat colour prefabs. Each cell picks one deterministically. Tools > Solar System > Wire Cat Spawner fills this in.")]
    public GameObject[] catPrefabs;

    [Tooltip("The Avatar sub-asset from Cat_L.fbx. REQUIRED: the clips are generic-rig, and without an avatar on the Animator none of them play.")]
    public Avatar catAvatar;

    [Header("Animation clips (from the pack's animation FBXs)")]
    public AnimationClip clipIdle;      // A_Cat_Idle  : Base
    public AnimationClip clipLook;      // A_Cat_Idle  : Look
    public AnimationClip clipStretch;   // A_Cat_Idle  : Stretching
    public AnimationClip clipSit;       // A_Cat_Rest  : Sit_Idle
    public AnimationClip clipLie;       // A_Cat_Rest  : Lie_idle
    public AnimationClip clipSleep;     // A_Cat_Rest  : Sleep_idle
    public AnimationClip clipWalk;      // A_Cat_Move  : Walk_F
    public AnimationClip clipTrot;      // A_Cat_Move  : Trot_F
    public AnimationClip clipSwim;      // A_Cat_Move  : Swim_F
    public AnimationClip clipSwimIdle;  // A_Cat_Move  : Swim_idle

    [Header("Animation lead-ins (optional, one-shot)")]
    [Tooltip("Played once before settling into the matching loop, so a cat lowers itself into a loaf instead of popping into one. Leave empty and it crossfades instead.")]
    public AnimationClip clipSitTo;     // A_Cat_Rest : Sit_to
    public AnimationClip clipLieTo;     // A_Cat_Rest : Lie_to
    public AnimationClip clipSleepTo;   // A_Cat_Rest : Sleep_to

    [Header("Spawn")]
    [Tooltip("Cats only exist within this distance of the player.")]
    public float spawnRadius = 300f;
    [Tooltip("Fallback cap when InputSettings is not assigned.")]
    public int maxCats = 6;
    [Tooltip("Optional. When assigned the effective radius follows the VIEW DISTANCE slider, so cat density holds steady at any view distance.")]
    public InputSettings inputSettings;
    [Tooltip("Layers the surface raycast should hit. Terrain only; water/ship/player are excluded automatically.")]
    public LayerMask groundMask = ~0;
    public float groundOffset = 0f;
    public float groundEmbedPerScale = 0.02f;

    const float BaselineRadius = 300f;

    [Header("Determinism")]
    [Tooltip("Change to reroll the whole cat layout. Kept distinct from the tree/alien seeds so the distributions don't overlap.")]
    public int seed = 4242;
    [Tooltip("Cell size in metres. Larger = cats further apart. Much larger than the alien cell — a cat should be a find, not scenery.")]
    public float cellSize = 140f;
    [Range(0f, 1f)]
    [Tooltip("Probability any given cell holds a cat.")]
    public float catSpawnChance = 0.30f;
    [Range(0f, 90f)]
    [Tooltip("Maximum slope where a cat may spawn. Cats are small, so they tolerate a bit more than the aliens do.")]
    public float maxSurfaceAngle = 40f;

    [Header("Variation")]
    [Tooltip("The pack's cats are authored roughly life-size, so this is near 1 unlike the alien toys.")]
    public float minScale = 0.85f;
    public float maxScale = 1.25f;

    [Header("Trigger Collider (added at spawn for F-to-interact)")]
    public Vector3 triggerSize = new Vector3(1.6f, 1.4f, 2.2f);
    public Vector3 triggerCenter = new Vector3(0f, 0.5f, 0f);

    [Header("Wander")]
    public bool wanderEnabled = true;
    [Tooltip("How far a cat strays from its spawn cell.")]
    public float wanderRadius = 16f;
    [Tooltip("Walk speed in m/s. A cat mooching along is slower than an alien.")]
    public float wanderSpeed = 1.1f;
    public float wanderIdleMin = 3f;
    public float wanderIdleMax = 9f;
    [Tooltip("Player closer than this freezes the stroll so petting never chases a moving target.")]
    public float wanderPauseDistance = 4f;

    [Header("Performance")]
    public float updateInterval = 0.35f;
    public float surfaceRayHeight = 100f;

    [Tooltip("Never spawn a cat closer than this to the camera. A cell can sit exactly where the player is standing, and a 1.9x cat appearing at the near plane is a full-screen flash of fur for one frame — which is what a 'something obstructed my vision too fast to see' bug looks like.")]
    public float minSpawnDistance = 14f;

    [Header("Live bisect (press the key IN THE BUILD)")]
    [Tooltip("Cycles what a cat is made of, live, and respawns them all, so ONE build tests every theory instead of one build per theory. 0 = NORMAL. 1 = NO ANIMATION (no Animator/CatAnimation, static posed mesh). 2 = NO INTERACTION (no SpaceCat/collider, so GazeHighlight can never outline a cat). 3 = NO CATS.")]
    public KeyCode bisectKey = KeyCode.F10;

    /// 0 normal · 1 no animation · 2 no interaction · 3 no cats.
    public static int BisectMode;
    static readonly string[] BisectNames =
        { "NORMAL", "NO ANIMATION", "NO INTERACTION (no outline)", "NO CATS" };

    // ── Appended 2026-09-11 (new serialized fields go at the END) ────────
    [Header("Action clips (A_Cat_Action) — found 2026-09-11")]
    public AnimationClip clipLick;      // Licking_sit   — the washing Sam asked for
    public AnimationClip clipSharpen;   // SharpensClaws
    public AnimationClip clipDig;       // Digging
    public AnimationClip clipShake;     // Shaking
    public AnimationClip clipPet;       // Pet      (standing)
    public AnimationClip clipPetSit;    // Pet_sit
    public AnimationClip clipPetLie;    // Pet_lie
    public AnimationClip clipEat;       // Eat_D    (head down — eating the fish)

    [Header("Purr")]
    [Tooltip("Seamless purr loop, played FROM the cat (3D) for ~15 s after a pet or a fish. Assets/Audio/Cats/cat_purr_loop.wav; the wiring tool assigns it.")]
    public AudioClip purrClip;
    [Range(0f, 1f)] public float purrVolume = 0.5f;

    [Header("Diagnostics")]
    [Tooltip("Logs once every few seconds saying how many cells were considered and WHY each one was rejected. Turn off once cats are reliably appearing.")]
    public bool debugLogging = false;
    public float debugInterval = 3f;

    // Rejection tallies, reset each debug window.
    int _dbgCandidates, _dbgSpawned, _dbgRejUV, _dbgRejRay, _dbgRejOcean,
        _dbgRejFar, _dbgRejSlope, _dbgRejExcluded, _dbgRejTooClose;
    float _dbgTimer;

    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, GameObject> active = new Dictionary<long, GameObject>();
    }

    readonly List<BodyState> bodies = new List<BodyState>();
    PlayerController player;
    Stack<GameObject>[] pools;
    float[] _prefabLocalBottomY;
    readonly List<long> scratchRemove = new List<long>();
    readonly List<CellCandidate> scratchCandidates = new List<CellCandidate>();
    static readonly System.Comparison<CellCandidate> CandidateByDistance =
        (a, b) => a.distSq.CompareTo(b.distSq);
    float tickTimer;

    AnimationClip[] _loopClips;
    AnimationClip[] _introClips;

    struct CellCandidate
    {
        public int bodySlot, face, cellU, cellV;
        public float distSq;
    }

    void Awake()
    {
        if (catPrefabs == null || catPrefabs.Length == 0)
        {
            Debug.LogWarning("[CatSpawner] No cat prefabs assigned; spawner will stay idle. " +
                             "Run Tools > Solar System > Wire Cat Spawner.");
            enabled = false;
            return;
        }
        pools = new Stack<GameObject>[catPrefabs.Length];
        for (int i = 0; i < pools.Length; i++) pools[i] = new Stack<GameObject>();
        _prefabLocalBottomY = new float[catPrefabs.Length];
        for (int i = 0; i < catPrefabs.Length; i++)
            _prefabLocalBottomY[i] = SpawnerCubeface.ComputeLocalBottomY(catPrefabs[i]);

        BuildClipTables();

        // Same rule as every other world spawner: never let this spawner's
        // surface raycast land on another spawner's props, the water, the ship
        // or the player, or cats end up standing on thin air when those move.
        groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask;
    }

    void BuildClipTables()
    {
        int n = (int)CatAnimation.Pose.Count;
        _loopClips = new AnimationClip[n];
        _loopClips[(int)CatAnimation.Pose.Idle]     = clipIdle;
        _loopClips[(int)CatAnimation.Pose.Look]     = clipLook;
        _loopClips[(int)CatAnimation.Pose.Stretch]  = clipStretch;
        _loopClips[(int)CatAnimation.Pose.Sit]      = clipSit;
        _loopClips[(int)CatAnimation.Pose.Lie]      = clipLie;
        _loopClips[(int)CatAnimation.Pose.Sleep]    = clipSleep;
        _loopClips[(int)CatAnimation.Pose.Walk]     = clipWalk;
        _loopClips[(int)CatAnimation.Pose.Trot]     = clipTrot;
        _loopClips[(int)CatAnimation.Pose.Swim]     = clipSwim;
        _loopClips[(int)CatAnimation.Pose.SwimIdle] = clipSwimIdle;

        _loopClips[(int)CatAnimation.Pose.Lick]    = clipLick;
        _loopClips[(int)CatAnimation.Pose.Sharpen] = clipSharpen;
        _loopClips[(int)CatAnimation.Pose.Dig]     = clipDig;
        _loopClips[(int)CatAnimation.Pose.Shake]   = clipShake;
        _loopClips[(int)CatAnimation.Pose.Pet]     = clipPet;
        _loopClips[(int)CatAnimation.Pose.PetSit]  = clipPetSit;
        _loopClips[(int)CatAnimation.Pose.PetLie]  = clipPetLie;
        _loopClips[(int)CatAnimation.Pose.Eat]     = clipEat;

        _introClips = new AnimationClip[n];
        _introClips[(int)CatAnimation.Pose.Sit]   = clipSitTo;
        _introClips[(int)CatAnimation.Pose.Lie]   = clipLieTo;
        _introClips[(int)CatAnimation.Pose.Sleep] = clipSleepTo;
    }

    bool _vaultCleared;

    void Update()
    {
        // A/B switch (FeatureVault.SpaceCats). Off = despawn everything once and
        // stay idle, so a build can be tested with the cats as the ONLY changed
        // variable. Mirrors how AlienNPCSpawner handles FeatureVault.WanderingNPCs.
        if (Input.GetKeyDown(bisectKey))
        {
            BisectMode = (BisectMode + 1) % 4;
            // Everything is rebuilt from scratch, pools included, so the new
            // mode applies to every cat immediately rather than to whichever
            // ones happen to stream in next.
            ClearAllActiveCats();
            _vaultCleared = false;
            Debug.Log($"[CatSpawner] BISECT MODE {BisectMode} = {BisectNames[BisectMode]}");
        }

        if (!FeatureVault.SpaceCats || BisectMode == 3)
        {
            if (!_vaultCleared) { ClearAllActiveCats(); _vaultCleared = true; }
            return;
        }
        _vaultCleared = false;

        if (!ResolveRefs())
        {
            // The commonest silent failure is not "no cats spawned" but "the
            // spawner never even ran" — no player yet, or no bodies resolved.
            if (debugLogging)
            {
                _dbgTimer += Time.deltaTime;
                if (_dbgTimer >= debugInterval)
                {
                    _dbgTimer = 0f;
                    Debug.Log($"[CatSpawner] IDLE — bodies={bodies.Count} " +
                              $"player={(player != null ? "found" : "NOT FOUND")}. " +
                              "Nothing can spawn until both resolve.");
                }
            }
            return;
        }
        tickTimer += Time.deltaTime;
        if (tickTimer < updateInterval) return;
        tickTimer = 0f;
        Tick();

        if (!debugLogging) return;
        _dbgTimer += tickTimer + updateInterval;
        if (_dbgTimer < debugInterval) return;
        _dbgTimer = 0f;
        Debug.Log($"[CatSpawner] active={CountActive()} spawned={_dbgSpawned} " +
                  $"candidates={_dbgCandidates} | rejected: offFace={_dbgRejUV} " +
                  $"noGround={_dbgRejRay} underwater={_dbgRejOcean} tooFar={_dbgRejFar} " +
                  $"tooSteep={_dbgRejSlope} exclusionZone={_dbgRejExcluded} tooClose={_dbgRejTooClose} " +
                  $"| bodies={bodies.Count} cell={cellSize}m chance={catSpawnChance:0.00}");
        _dbgCandidates = _dbgSpawned = _dbgRejUV = _dbgRejRay = _dbgRejOcean =
            _dbgRejFar = _dbgRejSlope = _dbgRejExcluded = _dbgRejTooClose = 0;
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
                if (b.isStaticAttractor) continue;      // the black hole: nothing to stand on
                if (IsExcluded(b.bodyName)) continue;
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
        for (int i = 0; i < bodies.Count; i++) n += bodies[i].active.Count;
        return n;
    }

    void Tick()
    {
        Vector3 playerPos = GetViewerPosition();

        float effectiveRadius = (inputSettings != null)
            ? Mathf.Clamp(inputSettings.viewDistance, 100f, 1000f)
            : spawnRadius;
        int baseCap = Mathf.Max(1, maxCats);
        int effectiveMax = Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * (effectiveRadius / BaselineRadius)));

        for (int s = 0; s < bodies.Count; s++) DespawnOutOfRange(bodies[s], playerPos, effectiveRadius);

        // PERF: the cap is saturated almost all the time (the build log read
        // active=44 spawned=0 on every single tick). Every one of those ticks
        // was still sweeping ~1,000 cells -- a hash and a dictionary lookup each
        // -- to build a candidate list it then threw away on the first line of
        // the spawn loop. Nothing can spawn until something despawns, so when
        // we are full, do nothing.
        if (CountActive() >= effectiveMax) return;

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

            float faceUVPerCell = SpawnerCubeface.FaceUVPerCell(cellSize, entry.body.radius);
            int half = Mathf.CeilToInt(1f / Mathf.Max(0.0001f, faceUVPerCell)) + 1;

            for (int face = 0; face < 6; face++)
                for (int cu = -half; cu <= half; cu++)
                    for (int cv = -half; cv <= half; cv++)
                    {
                        long id = SpawnerCubeface.EncodeCell(face, cu, cv);
                        if (entry.active.ContainsKey(id)) continue;
                        if (!CellHasCat(face, cu, cv)) continue;
                        if (!TryComputeCellApproxPos(entry.body, face, cu, cv, faceUVPerCell, out Vector3 spherePos)) continue;
                        float dSq = (spherePos - playerPos).sqrMagnitude;
                        if (dSq > prefilterMaxSq) continue;
                        scratchCandidates.Add(new CellCandidate
                        { bodySlot = s, face = face, cellU = cu, cellV = cv, distSq = dSq });
                    }
        }

        scratchCandidates.Sort(CandidateByDistance);
        _dbgCandidates += scratchCandidates.Count;

        for (int i = 0; i < scratchCandidates.Count; i++)
        {
            if (CountActive() >= effectiveMax) break;
            var c = scratchCandidates[i];
            var entry = bodies[c.bodySlot];
            float faceUVPerCell = SpawnerCubeface.FaceUVPerCell(cellSize, entry.body.radius);
            if (!TryComputePlacement(entry, c.face, c.cellU, c.cellV, faceUVPerCell, playerPos, effectiveRadius,
                                     out Vector3 pos, out Quaternion rot, out int prefabIdx, out float scale))
                continue;
            SpawnCat(entry, SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV), prefabIdx, pos, rot, scale);
            _dbgSpawned++;
        }

        EnforceMax(playerPos, effectiveMax);
    }

    bool CellHasCat(int face, int cellU, int cellV)
    {
        uint h = SpawnerCubeface.Hash(seed, face, cellU, cellV, 1);
        return (h & 0xFFFFu) / 65535f < catSpawnChance;
    }

    bool TryComputeCellApproxPos(CelestialBody body, int face, int cellU, int cellV,
                                 float faceUVPerCell, out Vector3 spherePos)
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

    bool TryComputePlacement(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                             Vector3 playerPos, float effectiveRadius, out Vector3 pos,
                             out Quaternion rot, out int prefabIdx, out float scale)
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
        if (faceU < -1f || faceU > 1f || faceV < -1f || faceV > 1f) { _dbgRejUV++; return false; }

        Vector3 dir = SpawnerCubeface.FaceUVToDirection(face, faceU, faceV);
        if (dir.sqrMagnitude < 0.0001f) { _dbgRejUV++; return false; }

        var planet = entry.body;
        Vector3 spherePos = planet.Position + dir * planet.radius;
        float prefilterMax = effectiveRadius + cellSize;
        if ((spherePos - playerPos).sqrMagnitude > prefilterMax * prefilterMax) { _dbgRejFar++; return false; }

        // The ray starts surfaceRayHeight ABOVE the surface, so its length must
        // include that height — a plain 2*radius stops short of the ground on a
        // small body and those planets grow nothing at all.
        Vector3 rayOrigin = planet.Position + dir * (planet.radius + surfaceRayHeight);
        if (!SpawnerCubeface.RaycastPlanetSurface(entry.gen, rayOrigin, -dir,
                                                  planet.radius * 2f + surfaceRayHeight, groundMask, out RaycastHit hit))
        { _dbgRejRay++; return false; }

        if (entry.gen != null)
        {
            float oceanR = entry.gen.GetOceanRadius();
            if (oceanR > 0f && (hit.point - planet.Position).magnitude < oceanR) { _dbgRejOcean++; return false; }
        }

        if ((hit.point - playerPos).sqrMagnitude > effectiveRadius * effectiveRadius) { _dbgRejFar++; return false; }
        // ...and never right on top of the camera. See minSpawnDistance.
        if ((hit.point - playerPos).sqrMagnitude < minSpawnDistance * minSpawnDistance)
        { _dbgRejTooClose++; return false; }

        Vector3 up = (hit.point - planet.Position).normalized;
        if (Vector3.Angle(hit.normal, up) > maxSurfaceAngle) { _dbgRejSlope++; return false; }

        float yaw = (hY & 0xFFFFu) / 65535f * 360f;
        rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);
        prefabIdx = (int)(hPI % (uint)catPrefabs.Length);
        float lo = Mathf.Min(minScale, maxScale);
        float hi = Mathf.Max(minScale, maxScale);
        scale = Mathf.Lerp(lo, hi, (hSC & 0xFFFFu) / 65535f);

        float bottomY = (_prefabLocalBottomY != null && prefabIdx < _prefabLocalBottomY.Length)
            ? _prefabLocalBottomY[prefabIdx] : 0f;
        pos = hit.point - up * (bottomY * scale + groundOffset + groundEmbedPerScale * scale);
        if (SpawnExclusionZone.IsExcluded(pos)) { _dbgRejExcluded++; return false; }
        return true;
    }

    void SpawnCat(BodyState entry, long cellId, int prefabIdx, Vector3 pos, Quaternion rot, float scale)
    {
        if (prefabIdx < 0 || prefabIdx >= catPrefabs.Length) prefabIdx = 0;
        var prefab = catPrefabs[prefabIdx];
        if (prefab == null || entry == null || entry.body == null) return;
        if (pools == null) pools = new Stack<GameObject>[catPrefabs.Length];
        if (pools[prefabIdx] == null) pools[prefabIdx] = new Stack<GameObject>();

        GameObject cat;
        var pool = pools[prefabIdx];
        if (pool.Count > 0)
        {
            cat = pool.Pop();
            cat.transform.SetPositionAndRotation(pos, rot);
            cat.transform.localScale = Vector3.one * scale;
            cat.SetActive(true);
        }
        else
        {
            cat = Instantiate(prefab, pos, rot);
            cat.transform.localScale = Vector3.one * scale;

            // The prefabs ship a LEGACY Animation component with
            // playAutomatically on, pointing at the pack's "_forPreview" idle.
            // Left alone it drives the same bones the PlayableGraph does, and
            // the two fight — so the legacy one goes before the Animator lands.
            // (Checking only for an Animator missed this; the prefab has no
            // Animator, but it is not un-animated.)
            var legacy = cat.GetComponent<Animation>();
            if (legacy != null) Destroy(legacy);

            if (BisectMode == 1) goto skipAnimator;   // bisect: static cats

            var animator = cat.GetComponent<Animator>();
            if (animator == null) animator = cat.AddComponent<Animator>();
            // Generic rig: without the avatar the clips animate nothing at all.
            if (catAvatar != null) animator.avatar = catAvatar;
            animator.applyRootMotion = false;
            // CullUpdateTransforms: off-screen cats keep their state but stop
            // writing bones. With 44 cats and 55 bones each, that is the single
            // biggest CPU saving available on them.
            //
            // History, because this flipped twice: it was set to AlwaysAnimate
            // on 2026-09-10 on the theory that culling caused the one-frame
            // "cat flies across the screen" glitch. That theory was WRONG -- the
            // F10 bisect proved the glitch was the animation graph mutating
            // itself (see CatAnimation), which is fixed at the root. Bones are
            // local to the object, so a culled cat re-entering view shows a
            // slightly stale POSE at its correct position for one frame, which
            // is invisible; it cannot streak.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            if (cat.GetComponent<CatAnimation>() == null) cat.AddComponent<CatAnimation>();

            skipAnimator:

            // Mode 2 strips the interaction entirely: no trigger, no SpaceCat,
            // so a cat can never become InteractPromptUI's owner and
            // GazeHighlight can never build outline clones of its three skinned
            // renderers. That is the cheapest way to rule the outline in or out.
            if (BisectMode == 2) goto skipInteraction;

            BoxCollider trigger = null;
            var existing = cat.GetComponents<BoxCollider>();
            for (int i = 0; i < existing.Length; i++)
                if (existing[i].isTrigger) { trigger = existing[i]; break; }
            if (trigger == null)
            {
                trigger = cat.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = triggerSize;
                trigger.center = triggerCenter;
            }

            if (cat.GetComponent<SpaceCat>() == null) cat.AddComponent<SpaceCat>();

            skipInteraction: ;
        }

        SpawnerCubeface.ParentToBodyPhysicsFrame(cat.transform, entry.body);
        SpawnerCubeface.SetLayerRecursively(cat, SpawnerCubeface.WorldPropLayer);

        // Re-assert on EVERY spawn, not just the fresh-instantiate path above:
        // a pooled cat carries its old Animator settings, so a cat created
        // before this line existed would keep culling and keep glitching.
        var anim0 = BisectMode == 1 ? null : cat.GetComponent<Animator>();
        if (anim0 != null)
        {
            anim0.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            anim0.applyRootMotion = false;
            if (catAvatar != null && anim0.avatar != catAvatar) anim0.avatar = catAvatar;
        }
        // PERF, shadows kept: a cat is THREE skinned renderers -- the body and
        // two separate eye meshes. The eyes sit inside the head, so their shadow
        // casters draw nothing you can see, yet they cost two extra shadow-map
        // draws per cat per light. Off for the eyes only; the body still casts,
        // so the cat's shadow on the ground is exactly what it was.
        var rends = cat.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i].name.IndexOf("Eye", System.StringComparison.OrdinalIgnoreCase) >= 0)
                rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // NOTE: updateWhenOffscreen is deliberately NOT set here any more.
        // It was added for the culling theory that turned out to be wrong (the
        // glitch was in the animation graph), it recomputes bounds from actual
        // skinned vertices EVERY frame on every cat, and it only ever reached
        // the first of a cat's three renderers anyway. Unity documents it as
        // expensive; the prefab already enables it on the two eye meshes, which
        // is where it genuinely matters.

        var marker = cat.GetComponent<SpawnedCat>();
        if (marker == null) marker = cat.AddComponent<SpawnedCat>();
        marker.Init(this, cellId, prefabIdx);
        entry.active[cellId] = cat;

        var fade = cat.GetComponent<SpawnFade>();
        if (fade == null) fade = cat.AddComponent<SpawnFade>();
        fade.BeginFadeIn();

        // Wander AFTER ParentToBodyPhysicsFrame — it captures the planet-local
        // spawn position as its leash centre.
        var wander = cat.GetComponent<AlienWander>();
        if (wanderEnabled)
        {
            if (wander == null) wander = cat.AddComponent<AlienWander>();
            wander.enabled = true;
            // These rigs play real clips, so the alien procedural leg swing must
            // stay off — otherwise it hunts for bones that do not exist and warns
            // once per spawn.
            wander.ProceduralLegs = false;
            float bottomY = (_prefabLocalBottomY != null && prefabIdx < _prefabLocalBottomY.Length)
                ? _prefabLocalBottomY[prefabIdx] : 0f;
            float scaleNow = cat.transform.localScale.x;
            float seatDepth = bottomY * scaleNow + groundOffset + groundEmbedPerScale * scaleNow;
            float oceanR = entry.gen != null ? entry.gen.GetOceanRadius() : 0f;
            wander.Configure(entry.body, oceanR, groundMask, seatDepth, maxSurfaceAngle,
                             wanderRadius, wanderSpeed, wanderIdleMin, wanderIdleMax,
                             wanderPauseDistance, scaleNow);
            // Configure resets these to defaults, so set it again after.
            wander.ProceduralLegs = false;
        }
        else if (wander != null)
        {
            wander.enabled = false;
        }

        var anim = BisectMode == 1 ? null : cat.GetComponent<CatAnimation>();
        if (anim != null) anim.Build(_loopClips, _introClips);

        var brain = BisectMode == 2 ? null : cat.GetComponent<SpaceCat>();
        if (brain != null) brain.Bind(wander, anim, purrClip, purrVolume);

        // Exact feet: seat the real lowest vertex of THIS instance on the
        // terrain under it, and hand that depth to the wander so every later
        // step keeps it.
        if (NPCSeating.Reseat(cat.transform, entry.body, groundMask, scale, 0.01f, out float exactSeat)
            && wander != null && wander.enabled)
            wander.SetSeatDepth(exactSeat);
    }

    void DespawnOutOfRange(BodyState entry, Vector3 playerPos, float effectiveRadius)
    {
        scratchRemove.Clear();
        float limit = effectiveRadius * 1.05f;
        float limitSq = limit * limit;
        foreach (var kv in entry.active)
        {
            if (kv.Value == null) { scratchRemove.Add(kv.Key); continue; }
            if ((kv.Value.transform.position - playerPos).sqrMagnitude > limitSq)
                scratchRemove.Add(kv.Key);
        }
        for (int i = 0; i < scratchRemove.Count; i++) DespawnInternal(entry, scratchRemove[i]);
    }

    void EnforceMax(Vector3 playerPos, int max)
    {
        while (CountActive() > max)
        {
            BodyState farthestEntry = null;
            long farthestId = 0;
            float farthestSq = -1f;
            for (int s = 0; s < bodies.Count; s++)
                foreach (var kv in bodies[s].active)
                {
                    if (kv.Value == null) continue;
                    float dSq = (kv.Value.transform.position - playerPos).sqrMagnitude;
                    if (dSq > farthestSq) { farthestSq = dSq; farthestId = kv.Key; farthestEntry = bodies[s]; }
                }
            if (farthestEntry == null) break;
            DespawnInternal(farthestEntry, farthestId);
        }
    }

    void DespawnInternal(BodyState entry, long cellId)
    {
        if (!entry.active.TryGetValue(cellId, out var cat)) return;
        entry.active.Remove(cellId);
        if (cat == null) return;
        var marker = cat.GetComponent<SpawnedCat>();
        int idx = marker != null ? marker.PrefabIndex : 0;
        if (idx < 0 || idx >= pools.Length) idx = 0;

        var fade = cat.GetComponent<SpawnFade>();
        if (fade != null)
        {
            int captured = idx;
            fade.BeginFadeOut(() => ReturnToPool(cat, captured));
        }
        else ReturnToPool(cat, idx);
    }

    /// Wipe every live cat and the pools. Used by the vault switch so turning
    /// the cats off takes effect immediately rather than at the next despawn.
    public void ClearAllActiveCats()
    {
        for (int s = 0; s < bodies.Count; s++)
        {
            var entry = bodies[s];
            foreach (var kv in entry.active)
                if (kv.Value != null) Destroy(kv.Value);
            entry.active.Clear();
        }
        if (pools != null)
        {
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] == null) continue;
                while (pools[i].Count > 0)
                {
                    var go = pools[i].Pop();
                    if (go != null) Destroy(go);
                }
            }
        }
    }

    void ReturnToPool(GameObject cat, int poolIdx)
    {
        if (cat == null) return;
        cat.transform.SetParent(null, true);
        cat.SetActive(false);
        if (poolIdx < 0 || poolIdx >= pools.Length) poolIdx = 0;
        pools[poolIdx].Push(cat);
    }
}

/// Marker so a despawning cat knows which pool it came from.
public class SpawnedCat : MonoBehaviour
{
    public CatSpawner Owner { get; private set; }
    public long CellId { get; private set; }
    public int PrefabIndex { get; private set; }

    public void Init(CatSpawner owner, long cellId, int prefabIndex)
    {
        Owner = owner;
        CellId = cellId;
        PrefabIndex = prefabIndex;
    }
}
