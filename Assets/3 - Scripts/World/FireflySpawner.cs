using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Streams firefly swarms onto the NIGHT side of planet surfaces (Sam,
/// 2026-09-11). Spec: docs/superpowers/specs/2026-09-11-fireflies-design.md.
///
/// Shape borrowed from <see cref="CatSpawner"/>: a deterministic hash of
/// (seed, body, cube-face cell) says where a swarm lives, swarms near the
/// player exist, swarms out of range go away, and nothing is saved because
/// nothing needs to be — the same cell is always the same swarm.
///
/// What is different:
///   • <b>Night gate.</b> A cell only gets its swarm while the sun is below the
///     horizon there — <see cref="FishingSun.SunDot"/>, the geometric sun height
///     the bite roll already uses (+1 noon, 0 horizon, −1 midnight). On below
///     <see cref="nightDotOn"/>, off above <see cref="nightDotOff"/>: two
///     thresholds so the terminator can never flicker a swarm.
///   • <b>A cell is a SWARM</b> — 3–5 bugs spread over a 16 m patch on evenly
///     spaced home points (a sunflower spiral), each drifting only a few metres
///     from its own home. That is what gives spaced-out coverage rather than
///     clumps (Sam's correction, 2026-09-11).
///   • <b>Depletion.</b> Catch every bug in a swarm and that cell stays empty
///     for <see cref="depletedMinutes"/> (session memory only).
///   • <b>Real lights on the nearest few only.</b> Every bug glows by emission
///     and halo; the <see cref="maxLitBugs"/> nearest also carry a point light
///     with a grass marker, so a passing swarm lights the ground and blades
///     under it. Everything past that is a twinkle, which is all it needs.
///   • <b>Auto-singleton</b>, no scene wiring: the bug is procedural
///     (<see cref="FireflyVisual"/>) and its materials live in Resources/. Also
///     seeded in MainMenuController.EnsureGameplaySingletons (CLAUDE.md trap #1:
///     the MainMenu early-return below means a build never auto-creates it).
///
/// Per frame it does two cheap things: refreshes the viewer position, and
/// elects the ONE <see cref="FireflyBug.Focused"/> bug the prompt/catch may
/// target (see FireflyBug for why). Everything else runs on a 0.3 s tick.
/// </summary>
public class FireflySpawner : MonoBehaviour
{
    public static FireflySpawner Instance { get; private set; }

    [Header("Planets")]
    [Tooltip("Body names to skip (matched against CelestialBody.bodyName). Static attractors (the black hole) are always skipped.")]
    public string[] excludeBodyNames = { "Sun" };

    [Header("Night")]
    [Tooltip("Sun height (dot of surface-up with the direction to the sun) BELOW which a cell may spawn its swarm. −0.05 ≈ the sun a few degrees under the horizon: dusk.")]
    public float nightDotOn = -0.05f;
    [Tooltip("Sun height ABOVE which a live swarm fades out. Kept a little above nightDotOn so the terminator never flickers a swarm on and off.")]
    public float nightDotOff = 0.02f;

    [Header("Spawn")]
    [Tooltip("Swarms only exist within this distance of the player. Small bugs — 140 m is already a faint twinkle at the edge.")]
    public float spawnRadius = 140f;
    [Tooltip("Never more than this many swarms alive at once. Density (cell size + chance) should bind before this does.")]
    public int maxSwarms = 36;
    [Tooltip("Never spawn a swarm closer than this to the camera, so one cannot pop in around your head.")]
    public float minSpawnDistance = 12f;
    [Tooltip("Layers the surface raycast may hit. Water/ship/props/player are removed automatically.")]
    public LayerMask groundMask = ~0;
    [Range(0f, 90f)] public float maxSurfaceAngle = 45f;
    public float surfaceRayHeight = 100f;

    [Header("Determinism")]
    [Tooltip("Change to reroll the whole layout. Distinct from the tree/alien/cat seeds.")]
    public int seed = 7171;
    [Tooltip("Cell size in metres. One swarm per cell at most.")]
    public float cellSize = 40f;
    [Range(0f, 1f)]
    [Tooltip("Probability a cell holds a swarm.")]
    public float swarmChance = 0.75f;

    [Header("Swarm")]
    // Spread-out coverage, not clumps (Sam, 2026-09-11): a few bugs per swarm
    // over a WIDE patch, each with its own evenly-spaced home point, and more
    // swarms. Same ~110 bugs in range as before, now ~8-10 m apart.
    public int bugsMin = 3;
    public int bugsMax = 5;
    [Tooltip("Radius of the patch a swarm covers (metres). Bugs get evenly spaced home points across it.")]
    public float swarmRadius = 16f;
    [Tooltip("How far a bug drifts from its own home point. Keep well under the spacing between homes or they bunch up again.")]
    public float wanderRadius = 3.5f;
    [Tooltip("Flight height above the ground, metres.")]
    public float heightMin = 0.4f;
    public float heightMax = 2.6f;
    public float speedMin = 0.45f;
    public float speedMax = 1.2f;
    [Tooltip("Seconds of one blink cycle, per bug.")]
    public float blinkPeriodMin = 1.8f;
    public float blinkPeriodMax = 4.5f;
    [Range(0f, 1f)]
    [Tooltip("How dim a bug gets between flashes. 0 = fully dark (real fireflies), but a bug you are about to press F on should not vanish.")]
    public float glowFloor = 0.25f;
    [Tooltip("Halo quad edge in metres. This is what makes a swarm visible from far away.")]
    public float haloSize = 0.5f;
    [Tooltip("Seconds a swarm takes to fade in / out.")]
    public float fadeSeconds = 1.6f;
    [Tooltip("Minutes an emptied (fully caught) cell stays empty.")]
    public float depletedMinutes = 8f;

    [Header("Real lights (the nearest few bugs)")]
    [Tooltip("How many bugs carry a REAL point light (the rest glow but light nothing). WHY THERE IS A CAP: a planet is ONE mesh, and in this renderer every real light makes everything it can reach get drawn once more — so each firefly light is one extra draw of the whole planet, whatever the bug looks like. 12 is the compromise; raise it and watch the FPS counter.")]
    public int maxLitBugs = 12;
    [Tooltip("Only bugs within this distance of the camera are candidates for a light. Past ~60 m a 5 m pool of light is a few pixels anyway.")]
    public float litRange = 60f;
    public float litIntensity = 1.1f;
    public float litLightRange = 5f;
    [Tooltip("Grass response of those lights. 0.5 = the lantern/torch value = same as the ground.")]
    public float litGrassStrength = 0.5f;

    [Header("Catching")]
    [Tooltip("Metres from the camera within which 'Press F to catch' can appear.")]
    public float catchRange = 3.5f;
    [Tooltip("A bug more than this many degrees off the crosshair is never the focused one.")]
    public float focusMaxAngle = 30f;

    [Header("Performance")]
    public float updateInterval = 0.3f;

    [Header("Diagnostics")]
    [Tooltip("Logs every few seconds: swarms alive, candidates, and why cells were rejected. Read the BUILD log (Player.log) when Sam plays a build.")]
    public bool debugLogging = false;
    public float debugInterval = 4f;

    /// Camera position this frame. Bugs read it for their range check instead
    /// of each asking for Camera.main.
    public Vector3 ViewerPos { get; private set; }

    // ── state ───────────────────────────────────────────────────────────

    class BodyState
    {
        public CelestialBody body;
        public CelestialBodyGenerator gen;
        public readonly Dictionary<long, FireflySwarm> active = new Dictionary<long, FireflySwarm>();
        /// cell → Time.time at which it may spawn again
        public readonly Dictionary<long, float> depleted = new Dictionary<long, float>();
    }

    struct CellCandidate
    {
        public int bodySlot, face, cellU, cellV;
        public float distSq;
    }

    readonly List<BodyState> _bodies = new List<BodyState>();
    readonly Stack<FireflyBug> _pool = new Stack<FireflyBug>();
    readonly List<FireflyBug> _allBugs = new List<FireflyBug>();
    readonly List<CellCandidate> _candidates = new List<CellCandidate>();
    readonly List<long> _scratchIds = new List<long>();
    readonly List<FireflySwarm> _scratchSwarms = new List<FireflySwarm>();
    readonly FireflyBug[] _litPick = new FireflyBug[48];
    readonly float[] _litDist = new float[48];
    static readonly System.Comparison<CellCandidate> ByDistance = (a, b) => a.distSq.CompareTo(b.distSq);

    PlayerController _player;
    float _nextPlayerSearch;
    float _tickTimer;
    bool _quiet;
    bool _vaultCleared;
    int _rayMask;

    // debug tallies
    int _dbgCandidates, _dbgSpawned, _dbgRejDay, _dbgRejRay, _dbgRejOcean, _dbgRejFar,
        _dbgRejSlope, _dbgRejExcluded, _dbgRejClose, _dbgRejDepleted;
    float _dbgTimer;

    // ── lifecycle ───────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[FireflySpawner]");
        DontDestroyOnLoad(go);
        go.AddComponent<FireflySpawner>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Rebuild(SceneManager.GetActiveScene());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (FireflyBug.Focused != null) FireflyBug.Focused = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rebuild(scene);

    /// A DontDestroyOnLoad singleton keeps its caches across a load, and every
    /// swarm, pooled bug and body it knew about died with the old scene. Start
    /// each gameplay load clean (AmbientFishField's rule).
    void Rebuild(Scene scene)
    {
        _bodies.Clear();
        _pool.Clear();
        _allBugs.Clear();
        _player = null;
        _nextPlayerSearch = 0f;
        _tickTimer = 0f;
        FireflyBug.Focused = null;
        _quiet = scene.name == "MainMenu" || FindObjectOfType<GallerySceneQuiet>() != null;
        // Same rule as every world spawner: never seat a swarm on another
        // spawner's props, the water, the ship or the player.
        _rayMask = groundMask & ~SpawnerCubeface.WorldSpawnExcludeMask;
    }

    void Update()
    {
        if (_quiet) return;

        if (!FeatureVault.Fireflies)
        {
            if (!_vaultCleared) { ClearAll(); _vaultCleared = true; }
            return;
        }
        _vaultCleared = false;

        if (!ResolveRefs()) return;

        ViewerPos = ViewerPosition();
        ElectFocused();

        _tickTimer += Time.deltaTime;
        if (_tickTimer < updateInterval) return;
        _tickTimer = 0f;
        Tick();

        if (!debugLogging) return;
        _dbgTimer += updateInterval;
        if (_dbgTimer < debugInterval) return;
        _dbgTimer = 0f;
        Debug.Log($"[Fireflies] swarms={CountActive()} bugs={_allBugs.Count} spawned={_dbgSpawned} " +
                  $"candidates={_dbgCandidates} | rejected: daylight={_dbgRejDay} depleted={_dbgRejDepleted} " +
                  $"noGround={_dbgRejRay} underwater={_dbgRejOcean} tooFar={_dbgRejFar} tooSteep={_dbgRejSlope} " +
                  $"exclusionZone={_dbgRejExcluded} tooClose={_dbgRejClose} | bodies={_bodies.Count} " +
                  $"cell={cellSize}m chance={swarmChance:0.00} sun={(FishingSun.SunTransform != null ? "ok" : "MISSING")}");
        _dbgCandidates = _dbgSpawned = _dbgRejDay = _dbgRejRay = _dbgRejOcean = _dbgRejFar =
            _dbgRejSlope = _dbgRejExcluded = _dbgRejClose = _dbgRejDepleted = 0;
    }

    bool ResolveRefs()
    {
        if (_bodies.Count == 0)
        {
            var sim = NBodySimulation.Bodies;
            if (sim == null) return false;
            for (int i = 0; i < sim.Length; i++)
            {
                var b = sim[i];
                if (b == null) continue;
                if (b.isStaticAttractor) continue;      // the black hole
                if (IsExcluded(b.bodyName)) continue;
                _bodies.Add(new BodyState { body = b, gen = b.GetComponentInChildren<CelestialBodyGenerator>() });
            }
            if (_bodies.Count == 0) return false;
        }
        if (_player == null)
        {
            if (Time.unscaledTime < _nextPlayerSearch) return false;
            _nextPlayerSearch = Time.unscaledTime + 1f;
            _player = FindObjectOfType<PlayerController>(true);
            if (_player == null) return false;
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

    Vector3 ViewerPosition()
    {
        if (_player != null && _player.Camera != null) return _player.Camera.transform.position;
        if (_player != null) return _player.transform.position;
        return transform.position;
    }

    Transform ViewerTransform()
    {
        if (_player != null && _player.Camera != null) return _player.Camera.transform;
        return _player != null ? _player.transform : null;
    }

    int CountActive()
    {
        int n = 0;
        for (int i = 0; i < _bodies.Count; i++) n += _bodies[i].active.Count;
        return n;
    }

    // ── focus (per frame) ───────────────────────────────────────────────

    /// The one bug the prompt may belong to: in catch range and nearest to
    /// the crosshair. Plain dot products over the live list; a hundred bugs
    /// is nothing.
    void ElectFocused()
    {
        Transform cam = ViewerTransform();
        if (cam == null) { FireflyBug.Focused = null; return; }

        Vector3 eye = cam.position;
        Vector3 fwd = cam.forward;
        float rangeSq = catchRange * catchRange;
        float bestCos = Mathf.Cos(focusMaxAngle * Mathf.Deg2Rad);
        FireflyBug best = null;

        for (int i = 0; i < _allBugs.Count; i++)
        {
            var b = _allBugs[i];
            if (b == null || b.Caught || b.Swarm == null || !b.Swarm.Catchable) continue;
            Vector3 to = b.transform.position - eye;
            float dSq = to.sqrMagnitude;
            if (dSq > rangeSq || dSq < 0.0001f) continue;
            float c = Vector3.Dot(to, fwd) / Mathf.Sqrt(dSq);
            if (c > bestCos) { bestCos = c; best = b; }
        }
        FireflyBug.Focused = best;
    }

    // ── tick ────────────────────────────────────────────────────────────

    void Tick()
    {
        Vector3 viewer = ViewerPos;
        float now = Time.time;

        for (int s = 0; s < _bodies.Count; s++) RetireSwarms(_bodies[s], viewer);

        if (CountActive() < maxSwarms)
        {
            _candidates.Clear();
            float prefilterMax = spawnRadius + cellSize;
            float prefilterMaxSq = prefilterMax * prefilterMax;

            for (int s = 0; s < _bodies.Count; s++)
            {
                var entry = _bodies[s];
                if (entry.body == null) continue;
                float bodyDistSq = (entry.body.Position - viewer).sqrMagnitude;
                float bodyOuter = spawnRadius + entry.body.radius + cellSize;
                if (bodyDistSq > bodyOuter * bodyOuter) continue;

                float faceUVPerCell = SpawnerCubeface.FaceUVPerCell(cellSize, entry.body.radius);
                int half = Mathf.CeilToInt(1f / Mathf.Max(0.0001f, faceUVPerCell)) + 1;

                for (int face = 0; face < 6; face++)
                    for (int cu = -half; cu <= half; cu++)
                        for (int cv = -half; cv <= half; cv++)
                        {
                            long id = SpawnerCubeface.EncodeCell(face, cu, cv);
                            if (entry.active.ContainsKey(id)) continue;
                            if (!CellHasSwarm(face, cu, cv)) continue;
                            if (!TryCellApproxPos(entry.body, face, cu, cv, faceUVPerCell, out Vector3 spherePos)) continue;
                            float dSq = (spherePos - viewer).sqrMagnitude;
                            if (dSq > prefilterMaxSq) continue;
                            if (entry.depleted.TryGetValue(id, out float until))
                            {
                                if (now < until) { _dbgRejDepleted++; continue; }
                                entry.depleted.Remove(id);
                            }
                            // Night gate on the rough position; re-checked at
                            // the exact ground hit below.
                            if (FishingSun.SunDot(spherePos, entry.body.transform) > nightDotOn) { _dbgRejDay++; continue; }
                            _candidates.Add(new CellCandidate { bodySlot = s, face = face, cellU = cu, cellV = cv, distSq = dSq });
                        }
            }

            _candidates.Sort(ByDistance);
            _dbgCandidates += _candidates.Count;

            for (int i = 0; i < _candidates.Count; i++)
            {
                if (CountActive() >= maxSwarms) break;
                var c = _candidates[i];
                var entry = _bodies[c.bodySlot];
                float faceUVPerCell = SpawnerCubeface.FaceUVPerCell(cellSize, entry.body.radius);
                if (!TryPlace(entry, c.face, c.cellU, c.cellV, faceUVPerCell, viewer, out Vector3 pos, out Quaternion rot))
                    continue;
                SpawnSwarm(entry, c.bodySlot, SpawnerCubeface.EncodeCell(c.face, c.cellU, c.cellV), c.face, c.cellU, c.cellV, pos, rot);
                _dbgSpawned++;
            }
        }

        AssignLights(viewer);
    }

    /// Fade out swarms that left the range, that the sun has risen on, or
    /// whose planet is gone.
    void RetireSwarms(BodyState entry, Vector3 viewer)
    {
        _scratchIds.Clear();
        float limit = spawnRadius * 1.1f;
        float limitSq = limit * limit;
        foreach (var kv in entry.active)
        {
            var swarm = kv.Value;
            if (swarm == null) { _scratchIds.Add(kv.Key); continue; }
            if (swarm.FadingOut) continue;
            bool gone = entry.body == null
                     || (swarm.transform.position - viewer).sqrMagnitude > limitSq
                     || FishingSun.SunDot(swarm.transform.position, entry.body.transform) > nightDotOff;
            if (gone) BeginDespawn(entry, swarm);
        }
        for (int i = 0; i < _scratchIds.Count; i++) entry.active.Remove(_scratchIds[i]);
    }

    bool CellHasSwarm(int face, int cellU, int cellV)
    {
        uint h = SpawnerCubeface.Hash(seed, face, cellU, cellV, 1);
        return (h & 0xFFFFu) / 65535f < swarmChance;
    }

    bool TryCellApproxPos(CelestialBody body, int face, int cellU, int cellV, float faceUVPerCell, out Vector3 spherePos)
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

    bool TryPlace(BodyState entry, int face, int cellU, int cellV, float faceUVPerCell,
                  Vector3 viewer, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = default;
        if (!TryCellApproxPos(entry.body, face, cellU, cellV, faceUVPerCell, out Vector3 spherePos)) return false;
        var planet = entry.body;
        Vector3 dir = (spherePos - planet.Position).normalized;

        // Ray from above the surface; the length must include that height or
        // a small body's ground is never reached (the Hearth lesson).
        Vector3 rayOrigin = planet.Position + dir * (planet.radius + surfaceRayHeight);
        if (!SpawnerCubeface.RaycastPlanetSurface(entry.gen, rayOrigin, -dir,
                                                  planet.radius * 2f + surfaceRayHeight, _rayMask, out RaycastHit hit))
        { _dbgRejRay++; return false; }

        if (entry.gen != null)
        {
            float oceanR = entry.gen.GetOceanRadius();
            // 1.5 m shore margin (2026-09-12): a swarm's bugs wander up to swarmRadius + wanderRadius
            // sideways from the hit point, so a swarm seated right at the waterline put bugs over water.
            if (oceanR > 0f && (hit.point - planet.Position).magnitude < oceanR + 1.5f) { _dbgRejOcean++; return false; }
        }

        float dSq = (hit.point - viewer).sqrMagnitude;
        if (dSq > spawnRadius * spawnRadius) { _dbgRejFar++; return false; }
        if (dSq < minSpawnDistance * minSpawnDistance) { _dbgRejClose++; return false; }

        Vector3 up = (hit.point - planet.Position).normalized;
        if (Vector3.Angle(hit.normal, up) > maxSurfaceAngle) { _dbgRejSlope++; return false; }
        if (SpawnExclusionZone.IsExcluded(hit.point)) { _dbgRejExcluded++; return false; }
        if (FishingSun.SunDot(hit.point, planet.transform) > nightDotOn) { _dbgRejDay++; return false; }

        uint hY = SpawnerCubeface.Hash(seed, face, cellU, cellV, 5);
        float yaw = (hY & 0xFFFFu) / 65535f * 360f;
        rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);
        pos = hit.point;
        return true;
    }

    // ── spawn / despawn ─────────────────────────────────────────────────

    void SpawnSwarm(BodyState entry, int bodySlot, long cellId, int face, int cellU, int cellV,
                    Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("FireflySwarm");
        root.transform.SetPositionAndRotation(pos, rot);
        SpawnerCubeface.ParentToBodyPhysicsFrame(root.transform, entry.body);
        root.layer = SpawnerCubeface.WorldPropLayer;

        var swarm = root.AddComponent<FireflySwarm>();
        swarm.Init(this, bodySlot, cellId, swarmRadius, heightMin, heightMax, fadeSeconds);
        swarm.Body = entry.body;
        float oceanFloor = entry.gen != null ? entry.gen.GetOceanRadius() : 0f;
        swarm.MinRadial = oceanFloor > 0f ? oceanFloor + 0.5f : 0f;   // bugs never dip below the water surface

        // Head-count from the cell hash so the same cell is always the same
        // size swarm; everything else about a bug is rolled live — nobody can
        // tell one flight path from another, so determinism buys nothing there.
        uint hN = SpawnerCubeface.Hash(seed, face, cellU, cellV, 6);
        int lo = Mathf.Max(1, Mathf.Min(bugsMin, bugsMax));
        int hi = Mathf.Max(lo, Mathf.Max(bugsMin, bugsMax));
        int count = lo + (int)(hN % (uint)(hi - lo + 1));

        // Home points on a sunflower spiral (golden angle, sqrt radius) — the
        // simplest layout that spaces N points evenly over a disc. Each bug
        // then wanders only wanderRadius from its own home, so the spacing
        // survives the flight.
        float spiralYaw = Random.value * Mathf.PI * 2f;
        const float GoldenAngle = 2.39996323f;
        for (int i = 0; i < count; i++)
        {
            var bug = GetBug();
            if (bug == null) break;
            bug.transform.SetParent(root.transform, false);
            float r = swarmRadius * Mathf.Sqrt((i + 0.5f) / count);
            float a = spiralYaw + i * GoldenAngle;
            var home = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var local = home + new Vector3(0f, Random.Range(heightMin, heightMax), 0f);
            bug.Spawn(swarm, local, home, wanderRadius,
                      Random.value,
                      Random.Range(blinkPeriodMin, blinkPeriodMax),
                      glowFloor,
                      Random.Range(speedMin, speedMax),
                      _rayMask);
            swarm.Bugs.Add(bug);
            _allBugs.Add(bug);
        }

        entry.active[cellId] = swarm;
    }

    void BeginDespawn(BodyState entry, FireflySwarm swarm)
    {
        if (swarm == null) return;
        swarm.BeginFadeOut(() => DestroySwarm(entry, swarm));
    }

    void DestroySwarm(BodyState entry, FireflySwarm swarm)
    {
        if (swarm == null) return;
        if (entry != null) entry.active.Remove(swarm.CellId);
        swarm.ReleaseAll();
        Destroy(swarm.gameObject);
    }

    /// Every bug caught: the cell rests, and the (now empty) swarm goes at once.
    public void OnSwarmDepleted(FireflySwarm swarm)
    {
        if (swarm == null) return;
        BodyState entry = swarm.BodySlot >= 0 && swarm.BodySlot < _bodies.Count ? _bodies[swarm.BodySlot] : null;
        if (entry != null) entry.depleted[swarm.CellId] = Time.time + depletedMinutes * 60f;
        DestroySwarm(entry, swarm);
    }

    FireflyBug GetBug()
    {
        FireflyBug bug = null;
        while (_pool.Count > 0 && bug == null) bug = _pool.Pop();   // skip anything the scene took
        if (bug != null)
        {
            bug.gameObject.SetActive(true);
            return bug;
        }

        var go = FireflyVisual.Build("Firefly", haloSize, out MeshRenderer body, out MeshRenderer halo);
        if (go == null) return null;
        SpawnerCubeface.SetLayerRecursively(go, SpawnerCubeface.WorldPropLayer);
        bug = go.AddComponent<FireflyBug>();
        bug.Bind(body, halo);
        return bug;
    }

    /// Back to the pool: inactive, unparented (so it dies with the scene, not
    /// with this DontDestroyOnLoad object), out of the live list.
    public void ReleaseBug(FireflyBug bug)
    {
        if (bug == null) return;
        bug.Release();
        _allBugs.Remove(bug);
        bug.transform.SetParent(null, false);
        bug.gameObject.SetActive(false);
        _pool.Push(bug);
    }

    /// Wipe every swarm and the pool. Used by the vault switch.
    void ClearAll()
    {
        for (int s = 0; s < _bodies.Count; s++)
        {
            var entry = _bodies[s];
            _scratchSwarms.Clear();
            foreach (var kv in entry.active) if (kv.Value != null) _scratchSwarms.Add(kv.Value);
            entry.active.Clear();
            for (int i = 0; i < _scratchSwarms.Count; i++)
            {
                _scratchSwarms[i].ReleaseAll();
                Destroy(_scratchSwarms[i].gameObject);
            }
        }
        while (_pool.Count > 0)
        {
            var b = _pool.Pop();
            if (b != null) Destroy(b.gameObject);
        }
        _allBugs.Clear();
        FireflyBug.Focused = null;
    }

    // ── real lights ─────────────────────────────────────────────────────

    /// The nearest <see cref="maxLitBugs"/> bugs within <see cref="litRange"/>
    /// get a point light; everyone else is glow only. Insertion into a tiny
    /// sorted array — no allocation, no full sort.
    void AssignLights(Vector3 viewer)
    {
        int k = Mathf.Clamp(maxLitBugs, 0, _litPick.Length);
        int picked = 0;
        float rangeSq = litRange * litRange;

        for (int i = 0; i < _allBugs.Count; i++)
        {
            var b = _allBugs[i];
            if (b == null || b.Swarm == null) continue;
            float dSq = (b.transform.position - viewer).sqrMagnitude;
            if (k == 0 || dSq > rangeSq) { b.SetLit(false, 0f, 0f, 0f); continue; }

            // Insert if it beats the current worst.
            if (picked < k || dSq < _litDist[picked - 1])
            {
                int at = picked < k ? picked : k - 1;
                // The bug being pushed out (if the array was full) loses its light.
                if (picked == k && _litPick[k - 1] != null && _litPick[k - 1] != b)
                    _litPick[k - 1].SetLit(false, 0f, 0f, 0f);
                while (at > 0 && _litDist[at - 1] > dSq)
                {
                    _litDist[at] = _litDist[at - 1];
                    _litPick[at] = _litPick[at - 1];
                    at--;
                }
                _litDist[at] = dSq;
                _litPick[at] = b;
                if (picked < k) picked++;
            }
            else
            {
                b.SetLit(false, 0f, 0f, 0f);
            }
        }

        for (int i = 0; i < picked; i++)
            if (_litPick[i] != null) _litPick[i].SetLit(true, litIntensity, litLightRange, litGrassStrength);
        for (int i = 0; i < _litPick.Length; i++) _litPick[i] = null;
    }
}
