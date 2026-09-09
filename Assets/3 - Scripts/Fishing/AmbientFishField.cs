using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Fish you can SEE swimming in a planet's water (Sam, 2026-09-09). Purely
/// visual: nothing here touches the bite roll, the fight, the economy or the
/// save. Spec: docs/superpowers/specs/2026-09-09-ambient-fish-design.md.
///
/// <b>Shape.</b> Modelled on <see cref="SpaceDustField"/>, the house pattern for
/// "endless local field at constant cost, safe under floating origin". There are
/// NO GameObjects: a fixed pool of fish lives in a struct array and is drawn
/// with <c>Graphics.DrawMeshInstanced</c>. Nothing is instantiated, nothing is
/// destroyed, nothing allocates per frame, and there are no colliders,
/// rigidbodies or lights.
///
/// <b>Everything is PLANET-LOCAL.</b> In world space ~98% of a fish's frame
/// delta is the planet's own orbital motion — the bug documented at length in
/// <c>Bobber.PoseFishLocal</c> (the bite fish aimed itself along the ORBIT and
/// read its own speed as 85 m/s, which drove the tail at flicker rates). Local
/// positions are converted to world only when the draw matrix is built, which
/// also means EndlessManager origin rebases need no handling at all.
///
/// <b>The swim look is the bite animation's.</b> Same mouth-anchored body yaw
/// (these models have no bones, so the whole body sweeps about the nose), same
/// speed-driven tail beat, same 45-degree pitch clamp. <c>Bobber</c> itself is
/// not modified beyond two one-line <see cref="Disturb"/> calls.
///
/// <b>Species are the planet's own.</b> <c>PlanetEconomy.CatchableIndices</c> is
/// the same list the bite roll uses, and tier odds come from the same
/// <c>FishingRules.RollTier</c>, so rares are as rare to see as they are to
/// catch and the water can never drift out of sync with what bites in it.
///
/// <b>Water is found by a cached sea-bed probe.</b> See <see cref="BedAt"/> —
/// one raycast per patch of sea floor, remembered forever, which is what keeps
/// fish out of the terrain without costing a raycast per fish per frame.
/// </summary>
// DefaultExecutionOrder(300), for SpaceDustField's reason: the field must read
// the camera AFTER it is finalised for the frame — after EndlessManager's origin
// rebase (0) and CameraTransformFX (100) — or it is drawn a rebase-offset behind
// for one frame on every shift.
[DefaultExecutionOrder(300)]
public class AmbientFishField : MonoBehaviour
{
    public static AmbientFishField Instance { get; private set; }

    // CLAUDE.md trap #1: this early-returns on MainMenu, so in a BUILD (which
    // boots in MainMenu) AfterSceneLoad fires there and never again. It is also
    // seeded in MainMenuController.EnsureGameplaySingletons — without that line
    // this feature works in the Editor and is invisible in every build.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("AmbientFishField");
        DontDestroyOnLoad(go);
        go.AddComponent<AmbientFishField>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Rebuild();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ReleaseMaterials();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A DontDestroyOnLoad singleton keeps its caches across a load: the bed
        // cache is keyed in the OLD planet's local frame and the cached bodies
        // are destroyed objects. Same class of stale-state bug SpaceDustField
        // documents. Start every gameplay load clean.
        if (scene.name == "MainMenu") { _quiet = true; return; }
        Rebuild();
    }

    void Rebuild()
    {
        _bed.Clear();
        _queued.Clear();
        _probeQueue.Clear();
        _oceanBodies = null;
        _planet = null;
        _planetT = null;
        ReleaseMaterials();
        _dex = null;
        _tiers = null;
        _cam = null;
        if (_fish != null) for (int i = 0; i < _fish.Length; i++) _fish[i].alive = false;

        // Gallery / tree-test scenes: every auto-singleton in the project spawns
        // there too (GallerySceneQuiet exists for exactly that). It only silences
        // Canvases, and this draws meshes, so opt out directly.
        _quiet = FindObjectOfType<GallerySceneQuiet>() != null;
    }

    // ── fish pool ────────────────────────────────────────────────────────────

    struct Fish
    {
        public bool alive;
        public Vector3 noseL;      // planet-local mouth point — the anchor the body sweeps about
        public Vector3 prevNoseL;
        public Vector3 dirL;       // planet-local heading, tangential to the water surface
        public float speed;        // m/s, cruising
        public float phase;        // tail-beat phase
        public float wanderSeed;
        public float steerSign;    // which way this fish turns away from shallows
        public int species;        // index into FishingRules.Species
        public int slot;           // draw-batch slot for that species
        public float halfLen;
        public Vector3 meshScale;
        public float bed;          // last known sea-bed radius under it
        public float depth;        // metres under the surface it wants to sit at
        public float scatter;      // 0..1, decays — the "something spooked me" boost
    }

    Fish[] _fish;

    // ── the sea-bed cache ────────────────────────────────────────────────────
    //
    // THE WHOLE ANSWER to "fish must only swim in water you can actually see,
    // and must never swim through the terrain" (Sam, 2026-09-09), without
    // costing a raycast per fish per frame.
    //
    // The sea floor is cut into patches. The first time a patch is needed, ONE
    // ray answers it, cast from ABOVE THE HIGHEST TERRAIN straight down the
    // radial:
    //
    //   hits terrain ABOVE the waterline -> land, a cave roof or an overhang.
    //                                       Never fish. This is the "water
    //                                       sealed inside the terrain" case.
    //   hits terrain BELOW the waterline -> open water; remember the bed radius.
    //   no hit within the probe          -> water deeper than we care about.
    //
    // Starting the ray ABOVE the terrain rather than just under the waterline is
    // load-bearing: Physics does not hit mesh backfaces, so a ray that starts
    // inside a mountain hits nothing and would classify the mountain as deep
    // water. The start radius comes from the terrain COLLIDER'S BOUNDS, so no
    // assumption is made about how high terrain goes and nothing in the
    // forbidden Celestial/ zone is read.
    //
    // Because the first thing the ray meets from above is the cave ROOF, a fish
    // is never placed in an air-filled cave below sea level either — and since
    // every fish is clamped between bed+clearance and surface-0.4, it can never
    // be inside rock.
    //
    // Terrain does not move, so an answer never goes stale: standing still costs
    // ZERO raycasts. Probes are hard-capped per frame, so arriving somewhere new
    // fills in over a moment instead of spiking.

    const float NotWater = -1f;

    readonly Dictionary<long, float> _bed = new Dictionary<long, float>(4096);
    readonly HashSet<long> _queued = new HashSet<long>();
    readonly Queue<KeyValuePair<long, Vector3>> _probeQueue = new Queue<KeyValuePair<long, Vector3>>();

    static long CellKey(Vector3 dirL, float oceanR, float cell, int level)
    {
        Vector3 p = dirL * (oceanR / cell);
        // 20-bit fields: +-524288 cells, i.e. a planet radius of thousands of km
        // at the fine cell size. Level occupies the top bits so the fine and
        // coarse grids never collide.
        long a = (Mathf.FloorToInt(p.x) + 524288) & 0xFFFFF;
        long b = (Mathf.FloorToInt(p.y) + 524288) & 0xFFFFF;
        long c = (Mathf.FloorToInt(p.z) + 524288) & 0xFFFFF;
        return ((long)level << 60) | (a << 40) | (b << 20) | c;
    }

    /// <summary>
    /// Sea-bed radius under a planet-local point. False means "no fish here":
    /// land, a roof, or a patch not probed yet (which resolves within a frame or
    /// two). Every uncertain case falls to false on purpose — the failure mode
    /// is a fish that does not spawn, never a fish inside a rock.
    /// </summary>
    bool BedAt(Vector3 posL, int level, out float bed)
    {
        bed = 0f;
        if (posL.sqrMagnitude < 1e-6f) return false;
        Vector3 dir = posL.normalized;
        float cell = level == 0 ? fineCellMetres : coarseCellMetres;
        long k = CellKey(dir, _oceanR, cell, level);
        if (_bed.TryGetValue(k, out float v))
        {
            if (v <= NotWater) return false;
            bed = v;
            return true;
        }
        if (_queued.Count < maxQueuedProbes && _queued.Add(k))
            _probeQueue.Enqueue(new KeyValuePair<long, Vector3>(k, dir));
        return false;
    }

    void DrainProbes()
    {
        int budget = maxProbesPerFrame;
        while (budget-- > 0 && _probeQueue.Count > 0)
        {
            var job = _probeQueue.Dequeue();
            _queued.Remove(job.Key);
            // Patches are only created within a ring of the player, so this
            // ceiling is generous; it exists so a session that walks an entire
            // coastline cannot grow the cache without bound. Dropping it just
            // means those patches get probed again if you go back.
            if (_bed.Count >= maxCachedPatches) _bed.Clear();
            _bed[job.Key] = Probe(job.Value);
        }
    }

    float Probe(Vector3 dirL)
    {
        if (_planetT == null) return NotWater;
        Vector3 startW = _planetT.TransformPoint(dirL * _probeStartRadius);
        Vector3 downW = _planetT.TransformDirection(-dirL);
        float len = _probeStartRadius - (_oceanR - probeDepthMetres);
        if (!Physics.Raycast(startW, downW, out RaycastHit hit, len, GroundMask,
                             QueryTriggerInteraction.Ignore))
            return _oceanR - probeDepthMetres;                 // open water, deeper than we care

        float r = (hit.point - _planetT.position).magnitude;
        if (r > _oceanR - minWaterDepth) return NotWater;      // land / roof / too shallow
        return r;
    }

    static int _groundMask = -1;
    static int GroundMask
    {
        get
        {
            if (_groundMask == -1) _groundMask = LayerMask.GetMask("Body");
            return _groundMask;
        }
    }

    // ── the planet ───────────────────────────────────────────────────────────

    CelestialBody[] _oceanBodies;
    CelestialBodyGenerator[] _oceanGens;
    float[] _oceanRadii;
    float[] _oceanProbeStart;

    CelestialBody _planet;
    Transform _planetT;
    float _oceanR;
    float _probeStartRadius;
    string _planetName;

    float _nextBodyScan;
    bool _quiet;

    Camera _cam;
    float _nextCamSearch;

    Camera Cam
    {
        get
        {
            if (_cam != null) return _cam;
            if (Time.time < _nextCamSearch) return null;
            _nextCamSearch = Time.time + 0.5f;
            _cam = Camera.main;
            return _cam;
        }
    }

    void RebuildBodyCache()
    {
        var bodies = NBodySimulation.Bodies;      // null-safe: Array.Empty off the solar scene
        var keepB = new List<CelestialBody>();
        var keepG = new List<CelestialBodyGenerator>();
        var keepR = new List<float>();
        var keepS = new List<float>();
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            var gen = b.GetComponentInChildren<CelestialBodyGenerator>();
            if (gen == null) continue;
            float r;
            try { r = gen.GetOceanRadius(); } catch { continue; }
            if (r <= 0.01f) continue;

            // No terrain collider means every probe would miss and read as deep
            // water — fish would appear over land. Skip the body entirely rather
            // than trust a probe that cannot fail.
            var col = gen.GetComponentInChildren<MeshCollider>();
            if (col == null) continue;
            Bounds bb = col.bounds;
            float maxR = (bb.center - b.transform.position).magnitude + bb.extents.magnitude;

            keepB.Add(b);
            keepG.Add(gen);
            keepR.Add(r);
            keepS.Add(maxR + 5f);
        }
        _oceanBodies = keepB.ToArray();
        _oceanGens = keepG.ToArray();
        _oceanRadii = keepR.ToArray();
        _oceanProbeStart = keepS.ToArray();
    }

    bool ResolvePlanet(Vector3 camPos)
    {
        bool stale = _oceanBodies == null || Time.time >= _nextBodyScan;
        if (!stale)
            for (int i = 0; i < _oceanBodies.Length; i++)
                if (_oceanBodies[i] == null || _oceanGens[i] == null) { stale = true; break; }
        if (stale)
        {
            _nextBodyScan = Time.time + 5f;
            RebuildBodyCache();
        }
        if (_oceanBodies.Length == 0) { _planet = null; _planetT = null; return false; }

        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < _oceanBodies.Length; i++)
        {
            float d = (_oceanBodies[i].transform.position - camPos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) { _planet = null; _planetT = null; return false; }

        var chosen = _oceanBodies[best];
        if (chosen != _planet)
        {
            // A new planet invalidates the bed cache (keys are in the old
            // planet's local frame), the species slots and the materials.
            _planet = chosen;
            _planetT = chosen.transform;
            _planetName = chosen.bodyName;
            _bed.Clear();
            _queued.Clear();
            _probeQueue.Clear();
            if (_fish != null) for (int i = 0; i < _fish.Length; i++) _fish[i].alive = false;
            BuildSpeciesSlots();
        }
        _oceanR = _oceanRadii[best];
        _probeStartRadius = _oceanProbeStart[best];
        return true;
    }

    // ── species, meshes, materials ───────────────────────────────────────────

    class TierMesh
    {
        public Mesh mesh;
        public int subCount;
        public Vector3 centre;      // mesh-space bounds centre, so the body sweeps about itself
        public float longest;       // mesh-space longest edge, for the size normalisation
        public Color[] partColours; // one per submesh, the model's own colours
    }

    FishingdexManager _dex;
    TierMesh[] _tiers;              // indexed by FishTier
    float _nextDexSearch;

    readonly List<int> _slotSpecies = new List<int>();   // slot -> species index
    readonly Dictionary<int, int> _speciesSlot = new Dictionary<int, int>();
    readonly List<int> _allowed = new List<int>();       // this planet's catch list, bounty removed
    Material[][] _slotMats;         // [slot][submesh]
    Matrix4x4[][] _slotMatrices;    // [slot][fish]
    int[] _slotCount;
    Material _baseMat;

    bool ResolveMeshes()
    {
        if (_tiers != null) return true;
        // Throttled as a WHOLE, not just the dex search: with the dex found but
        // a prefab slot empty, every failure below would otherwise re-walk three
        // prefab hierarchies every frame, forever.
        if (Time.time < _nextDexSearch) return false;
        _nextDexSearch = Time.time + 1f;
        if (_dex == null)
        {
            _dex = FishingdexManager.Instance != null
                 ? FishingdexManager.Instance
                 : FindObjectOfType<FishingdexManager>();
            if (_dex == null) return false;
        }

        var built = new TierMesh[3];
        for (int t = 0; t < 3; t++)
        {
            var prefab = _dex.PrefabForTier((FishTier)t);
            if (prefab == null) return false;
            MeshFilter mf = null;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null) { mf = f; break; }
            if (mf == null) return false;

            var tm = new TierMesh { mesh = mf.sharedMesh };
            tm.subCount = Mathf.Max(1, tm.mesh.subMeshCount);
            Vector3 size = tm.mesh.bounds.size;
            tm.centre = tm.mesh.bounds.center;
            tm.longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (tm.longest <= 0.0001f) return false;

            // The model's own per-part colours, so an ambient fish is shaded the
            // same way a caught one is. Read defensively: these materials point
            // at a URP shader this project does not have (FishSpeciesVisuals
            // documents it), so _Color may be absent and _BaseColor may be what
            // survived the import.
            tm.partColours = new Color[tm.subCount];
            var rend = mf.GetComponent<Renderer>();
            var src = rend != null ? rend.sharedMaterials : null;
            for (int s = 0; s < tm.subCount; s++)
            {
                Color c = Color.white;
                if (src != null && s < src.Length && src[s] != null)
                {
                    if (src[s].HasProperty("_Color")) c = src[s].GetColor("_Color");
                    else if (src[s].HasProperty("_BaseColor")) c = src[s].GetColor("_BaseColor");
                }
                c.a = 1f;
                tm.partColours[s] = c;
            }
            built[t] = tm;
        }
        _tiers = built;
        return true;
    }

    void BuildSpeciesSlots()
    {
        ReleaseMaterials();
        _slotSpecies.Clear();
        _speciesSlot.Clear();
        _allowed.Clear();

        var catchable = PlanetEconomy.CatchableIndices(_planetName);
        if (catchable != null)
        {
            for (int i = 0; i < catchable.Count; i++)
            {
                int sp = catchable[i];
                // A bounty fish is BountyZone's to hand out. It must never just
                // be swimming past.
                if (sp < 0 || sp >= FishingRules.Species.Length || FishingRules.IsBounty(sp)) continue;
                _allowed.Add(sp);
            }
        }
        if (_allowed.Count == 0)
            for (int i = 0; i < FishingRules.Species.Length; i++)
                if (!FishingRules.IsBounty(i)) _allowed.Add(i);   // no table: the whole pool, as elsewhere

        for (int i = 0; i < _allowed.Count; i++)
        {
            _speciesSlot[_allowed[i]] = _slotSpecies.Count;
            _slotSpecies.Add(_allowed[i]);
        }

        int slots = _slotSpecies.Count;
        _slotMats = new Material[slots][];
        _slotMatrices = new Matrix4x4[slots][];
        _slotCount = new int[slots];
        for (int i = 0; i < slots; i++) _slotMatrices[i] = new Matrix4x4[maxFish];
    }

    Material BaseMaterial()
    {
        if (_baseMat != null) return _baseMat;
        // A REAL material asset in Resources, for SpaceDustField's reason: a
        // runtime new Material(Shader.Find(...)) loses its INSTANCING_ON shader
        // variant in builds (the variant collector never sees instancing used on
        // that shader), so DrawMeshInstanced renders NOTHING in the player while
        // working perfectly in the Editor.
        _baseMat = Resources.Load<Material>("AmbientFish");
        if (_baseMat == null)
        {
            var sh = Shader.Find("Standard");
            if (sh == null) return null;
            _baseMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _baseMat.enableInstancing = true;
            Debug.LogWarning("[AmbientFishField] Resources/AmbientFish.mat missing — "
                           + "falling back to a runtime Standard material, which may draw "
                           + "nothing in a BUILD. Restore the asset.");
        }
        return _baseMat;
    }

    Material[] MaterialsForSlot(int slot)
    {
        if (_slotMats[slot] != null) return _slotMats[slot];
        var baseMat = BaseMaterial();
        if (baseMat == null) return null;

        int sp = _slotSpecies[slot];
        var tm = _tiers[(int)FishingRules.Species[sp].tier];
        Color tint = FishSpeciesVisuals.TintOf(sp);
        float glow = FishSpeciesVisuals.EmissionFor(sp) * ambientGlowScale;

        var mats = new Material[tm.subCount];
        for (int s = 0; s < tm.subCount; s++)
        {
            var m = new Material(baseMat) { hideFlags = HideFlags.HideAndDontSave };
            m.color = FishSpeciesVisuals.BlendPartColour(tm.partColours[s], tint, 0.55f);
            if (glow > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", tint * glow);
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            mats[s] = m;
        }
        _slotMats[slot] = mats;
        return mats;
    }

    void ReleaseMaterials()
    {
        if (_slotMats != null)
            for (int i = 0; i < _slotMats.Length; i++)
            {
                if (_slotMats[i] == null) continue;
                for (int s = 0; s < _slotMats[i].Length; s++)
                    if (_slotMats[i][s] != null) Destroy(_slotMats[i][s]);
                _slotMats[i] = null;
            }
        _slotMats = null;
        _slotMatrices = null;
        _slotCount = null;
    }

    // ── disturbance ──────────────────────────────────────────────────────────

    Vector3 _disturbW;
    float _disturbR;
    float _disturbUntil;

    /// <summary>
    /// "Something happened here" — fish nearby break away for a moment. A
    /// no-op when the field does not exist, so callers never need a null check.
    /// Cosmetic only: nothing about the fishing roll changes.
    /// </summary>
    public static void Disturb(Vector3 worldPos, float radius)
    {
        var f = Instance;
        if (f == null) return;
        f._disturbW = worldPos;
        f._disturbR = Mathf.Max(0.5f, radius);
        f._disturbUntil = Time.time + 0.25f;
    }

    // ── frame ────────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (_quiet || !enableField) return;
        var cam = Cam;
        if (cam == null) return;
        Vector3 camW = cam.transform.position;
        if (!ResolvePlanet(camW)) return;
        if (!ResolveMeshes()) return;
        if (_slotMatrices == null || _slotMatrices.Length == 0) return;

        Vector3 camL = _planetT.InverseTransformPoint(camW);
        float camR = camL.magnitude;
        if (camR < 1e-3f) return;
        float alt = camR - _oceanR;

        // Above the ceiling a half-metre fish is under two pixels — not a budget
        // choice, just optics. Below the floor you are deeper than the ocean
        // post lets you see anything anyway.
        if (alt > maxAltitude || alt < -maxDepthBelow)
        {
            if (_fish != null) for (int i = 0; i < _fish.Length; i++) _fish[i].alive = false;
            return;
        }

        // Coarser patches high up: flying sweeps across a lot of new sea floor,
        // and at a handful of probes a frame the fine grid cannot keep up. From
        // 200 m you cannot see the difference.
        int level = alt > coarseAltitude ? 1 : 0;
        float ring = Mathf.Lerp(nearRing, farRing, Mathf.InverseLerp(0f, 200f, Mathf.Max(alt, 0f)));

        DrainProbes();

        if (_fish == null || _fish.Length != maxFish) _fish = new Fish[maxFish];
        for (int i = 0; i < _slotCount.Length; i++) _slotCount[i] = 0;

        bool hasDisturb = Time.time < _disturbUntil;
        Vector3 disturbL = hasDisturb ? _planetT.InverseTransformPoint(_disturbW) : Vector3.zero;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        int spawnBudget = maxSpawnsPerFrame;
        int spawnTries = maxSpawnsPerFrame * 4;   // a failed try is a dict lookup, but bound it anyway

        for (int i = 0; i < _fish.Length; i++)
        {
            if (!_fish[i].alive)
            {
                if (spawnBudget <= 0 || spawnTries <= 0) continue;
                spawnTries--;
                if (TrySpawn(ref _fish[i], camL, ring, level)) spawnBudget--;
                else continue;
            }
            if (!Step(ref _fish[i], camL, ring, level, dt, hasDisturb, disturbL)) continue;
            Accumulate(ref _fish[i]);
        }

        Draw();
    }

    bool TrySpawn(ref Fish f, Vector3 camL, float ring, int level)
    {
        Vector3 up = camL.normalized;
        Vector3 t1 = Vector3.Cross(up, Vector3.up);
        if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(up, Vector3.right);
        t1.Normalize();
        Vector3 t2 = Vector3.Cross(up, t1);

        // Out in the ring, never right on top of you — a fish that pops into
        // existence at arm's length is the thing you notice.
        float d = Random.Range(0.45f, 1f) * ring;
        float a = Random.value * Mathf.PI * 2f;
        Vector3 dir = (up * _oceanR + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * d).normalized;

        if (!BedAt(dir * _oceanR, level, out float bed)) return false;   // land, roof, or not probed yet
        float water = _oceanR - bed;
        if (water < minWaterDepth) return false;

        float depth = Random.Range(0.7f, Mathf.Min(maxSwimDepth, water - 0.6f));
        if (depth <= 0.5f) return false;

        Vector3 nose = dir * (_oceanR - depth);
        Vector3 heading = Vector3.ProjectOnPlane(t1 * Mathf.Cos(a * 1.7f) + t2 * Mathf.Sin(a * 1.7f), dir);
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.ProjectOnPlane(t1, dir);

        int sp = RollSpecies(nose);
        if (sp < 0) return false;

        var tm = _tiers[(int)FishingRules.Species[sp].tier];
        float weight = FishingRules.RollWeight(sp, Random.value);
        float bodyLen = FishingRules.BodyLengthForWeight(weight);
        float girth = FishingRules.GirthFactorForWeight(weight);
        float baseScale = bodyLen / tm.longest;

        f.alive = true;
        f.noseL = nose;
        f.prevNoseL = nose;
        f.dirL = heading.normalized;
        f.speed = Random.Range(cruiseSpeedMin, cruiseSpeedMax);
        f.phase = Random.value * 10f;
        f.wanderSeed = Random.value * 100f;
        f.steerSign = Random.value < 0.5f ? -1f : 1f;
        f.species = sp;
        f.slot = _speciesSlot.TryGetValue(sp, out int s) ? s : 0;
        f.halfLen = bodyLen * 0.5f;
        // Models are authored facing -Z, so local X is width, Y is belly depth
        // and Z is length — the same axis split the held/hooked fish uses.
        f.meshScale = new Vector3(baseScale * girth,
                                  baseScale * (1f + (girth - 1f) * 0.6f),
                                  baseScale);
        f.bed = bed;
        f.depth = depth;
        f.scatter = 0f;
        return true;
    }

    int RollSpecies(Vector3 noseL)
    {
        if (_allowed.Count == 0) return -1;
        // The planet's own catch list, rolled through the same tier odds the
        // bite uses — including the sun angle, so what is swimming past at night
        // is what would bite at night.
        float dot = FishingSun.SunDot(_planetT.TransformPoint(noseL), _planetT);
        FishTier tier = FishingRules.RollTier(dot, BaitKind.None, Random.value);
        int sp = FishingRules.RollSpeciesInTier(tier, Random.value, _allowed);
        if (sp < 0 || !_speciesSlot.ContainsKey(sp)) sp = _allowed[Random.Range(0, _allowed.Count)];
        return sp;
    }

    bool Step(ref Fish f, Vector3 camL, float ring, int level, float dt,
              bool hasDisturb, Vector3 disturbL)
    {
        Vector3 up = f.noseL.normalized;

        // Leave when the field moves off them. Checked BEFORE the move so a
        // recycled fish is never drawn at a stale pose.
        if ((f.noseL - camL).sqrMagnitude > ring * ring * 1.45f) { f.alive = false; return false; }

        // ── scatter ──────────────────────────────────────────────────────────
        // You, and the bobber's splash. Two distance checks. Nothing about the
        // fishing changes — they just get out of the way.
        if (f.scatter > 0f) f.scatter = Mathf.Max(0f, f.scatter - dt / scatterSeconds);
        Vector3 flee = Vector3.zero;
        float camDist2 = (f.noseL - camL).sqrMagnitude;
        if (camDist2 < playerScatterRadius * playerScatterRadius) flee = f.noseL - camL;
        else if (hasDisturb && (f.noseL - disturbL).sqrMagnitude < _disturbR * _disturbR)
            flee = f.noseL - disturbL;
        if (flee.sqrMagnitude > 1e-4f)
        {
            f.scatter = 1f;
            Vector3 away = Vector3.ProjectOnPlane(flee, up);
            if (away.sqrMagnitude > 1e-4f)
                f.dirL = Vector3.RotateTowards(f.dirL, away.normalized, 6f * dt, 0f);
        }

        // ── meander ──────────────────────────────────────────────────────────
        float wander = Mathf.PerlinNoise(f.wanderSeed, Time.time * 0.12f) - 0.5f;
        f.dirL = Quaternion.AngleAxis(wander * wanderTurnRate * dt, up) * f.dirL;
        f.dirL = Vector3.ProjectOnPlane(f.dirL, up);
        if (f.dirL.sqrMagnitude < 1e-4f) f.dirL = Vector3.ProjectOnPlane(Vector3.forward, up);
        f.dirL.Normalize();

        // ── look ahead, turn away from the shallows ──────────────────────────
        // A dictionary lookup, not a raycast. Unknown counts as blocked, so a
        // fish waits rather than swimming into a patch nobody has probed.
        Vector3 ahead = f.noseL + f.dirL * lookAheadMetres;
        bool clear = BedAt(ahead, level, out float bedAhead) && (_oceanR - bedAhead) > minWaterDepth;
        if (!clear)
            f.dirL = (Quaternion.AngleAxis(f.steerSign * steerRate * dt, up) * f.dirL).normalized;

        float speed = f.speed * (1f + f.scatter * scatterSpeedBoost);
        f.prevNoseL = f.noseL;
        f.noseL += f.dirL * speed * dt;

        // ── depth: ride the sea bed, never through it ────────────────────────
        if (BedAt(f.noseL, level, out float bedHere)) f.bed = bedHere;
        float breathe = Mathf.Sin(Time.time * 0.3f + f.wanderSeed) * 0.4f;
        float wantR = _oceanR - Mathf.Max(0.4f, f.depth + breathe);
        float targetR = Mathf.Clamp(wantR, f.bed + bedClearance, _oceanR - surfaceClearance);
        float r = Mathf.MoveTowards(f.noseL.magnitude, targetR, climbSpeed * dt);
        f.noseL = f.noseL.normalized * r;

        f.phase += (3.5f + speed * 1.8f) * dt;
        return true;
    }

    void Accumulate(ref Fish f)
    {
        int slot = f.slot;
        if (_slotCount == null || slot >= _slotCount.Length) return;
        int n = _slotCount[slot];
        if (n >= _slotMatrices[slot].Length) return;

        Vector3 up = f.noseL.normalized;

        // Facing from the ACTUAL planet-local delta, exactly as the bite
        // animation does — that is what makes the tail beat and the pitch read
        // as swimming rather than as a transform being written.
        Vector3 swim = f.noseL - f.prevNoseL;
        if (swim.sqrMagnitude < 1e-8f) swim = f.dirL;
        swim.Normalize();

        // Sam's 45-degree law: a fish never points steeper than 45 degrees off
        // horizontal, climbing or diving.
        Vector3 horiz = Vector3.ProjectOnPlane(swim, up);
        if (horiz.sqrMagnitude > 1e-6f)
        {
            float vert = Vector3.Dot(swim, up);
            float maxVert = horiz.magnitude;
            if (Mathf.Abs(vert) > maxVert)
                swim = (horiz + up * (Mathf.Sign(vert) * maxVert)).normalized;
        }

        // THE MOUTH IS THE ANCHOR: these models cannot articulate a tail, so the
        // kick is the whole body yawing about the nose — the mouth holds the
        // line of travel and the tail does the sweeping.
        float tailDeg = tailSweepDegrees * (1f + f.scatter * 0.8f);
        Vector3 nose = Quaternion.AngleAxis(Mathf.Sin(f.phase) * tailDeg, up) * swim;

        Vector3 noseW = _planetT.TransformPoint(f.noseL);
        Vector3 noseDirW = _planetT.TransformDirection(nose);
        Vector3 upW = _planetT.TransformDirection(up);
        if (Mathf.Abs(Vector3.Dot(noseDirW, upW)) > 0.98f)
            upW = Vector3.Cross(noseDirW, Vector3.right).normalized;

        // The models face -Z, confirmed by the MOUTH marker in fish01.prefab
        // sitting at local z = -1.201.
        Quaternion rot = Quaternion.LookRotation(-noseDirW, upW);

        var tm = _tiers[(int)FishingRules.Species[f.species].tier];
        Vector3 centreW = noseW - noseDirW * f.halfLen;
        Vector3 pos = centreW - rot * Vector3.Scale(f.meshScale, tm.centre);

        _slotMatrices[slot][n] = Matrix4x4.TRS(pos, rot, f.meshScale);
        _slotCount[slot] = n + 1;
    }

    void Draw()
    {
        for (int slot = 0; slot < _slotCount.Length; slot++)
        {
            int n = _slotCount[slot];
            if (n == 0) continue;
            var mats = MaterialsForSlot(slot);
            if (mats == null) continue;
            var tm = _tiers[(int)FishingRules.Species[_slotSpecies[slot]].tier];
            for (int s = 0; s < tm.subCount && s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                // Shadows and probes off: a fish under water casting a sun
                // shadow is wrong anyway, and both are per-instance cost we do
                // not need. Opaque queue (2000) is what lets the ocean post tint
                // and swallow them with depth — above 2500 they would float
                // visibly on top of the water.
                Graphics.DrawMeshInstanced(tm.mesh, s, mats[s], _slotMatrices[slot], n, null,
                                           ShadowCastingMode.Off, false, 0, null,
                                           LightProbeUsage.Off);
            }
        }
    }

    // ================= tuning (appended at END per conventions) =================

    [Header("Field")]
    [Tooltip("Master switch. Off = the water is empty, exactly as before this existed.")]
    [SerializeField] bool enableField = true;
    [Tooltip("How many fish exist at once. Fixed on purpose — fish are not streamed at view distance, so they get no quality slider (crystals and the concert audience are fixed for the same reason).")]
    [SerializeField] int maxFish = 40;
    [Tooltip("Radius of the bubble of fish around you at the water's surface, in metres.")]
    [SerializeField] float nearRing = 30f;
    [Tooltip("Radius of that bubble at 200 m altitude — wider so they spread out under you instead of clumping.")]
    [SerializeField] float farRing = 130f;
    [Tooltip("Metres above the water past which fish are switched off. A half-metre fish is under two pixels up here.")]
    [SerializeField] float maxAltitude = 250f;
    [Tooltip("Metres below the water past which fish are switched off — deeper than the ocean post lets you see anyway.")]
    [SerializeField] float maxDepthBelow = 60f;

    [Header("Swimming")]
    [SerializeField] float cruiseSpeedMin = 0.55f;
    [SerializeField] float cruiseSpeedMax = 1.35f;
    [Tooltip("Degrees per second of lazy wander.")]
    [SerializeField] float wanderTurnRate = 55f;
    [Tooltip("Degrees per second a fish turns when the water ahead is too shallow.")]
    [SerializeField] float steerRate = 150f;
    [Tooltip("How far ahead a fish checks the sea bed, in metres.")]
    [SerializeField] float lookAheadMetres = 3f;
    [Tooltip("Deepest a fish will sit below the surface. Deeper than this and the ocean has swallowed it anyway.")]
    [SerializeField] float maxSwimDepth = 6.5f;
    [Tooltip("Metres of water a fish keeps between itself and the sea bed.")]
    [SerializeField] float bedClearance = 0.55f;
    [Tooltip("Metres a fish keeps below the surface, so it never breaks through.")]
    [SerializeField] float surfaceClearance = 0.4f;
    [Tooltip("How fast a fish rises or dives to follow the bed, in m/s.")]
    [SerializeField] float climbSpeed = 0.7f;
    [Tooltip("Degrees the body sweeps either side of its heading — the tail beat.")]
    [SerializeField] float tailSweepDegrees = 12f;

    [Header("Reacting to you")]
    [Tooltip("Metres. Swim closer than this and they break away.")]
    [SerializeField] float playerScatterRadius = 3.5f;
    [Tooltip("Seconds a spooked fish stays spooked.")]
    [SerializeField] float scatterSeconds = 1.8f;
    [Tooltip("Extra speed while fleeing, as a multiple of cruise.")]
    [SerializeField] float scatterSpeedBoost = 2.2f;

    [Header("Look")]
    [Tooltip("Rarity glow, as a fraction of the glow a caught fish has. Below 1 so a rare in the water reads as a glow, not a lamp.")]
    [SerializeField] float ambientGlowScale = 0.7f;

    [Header("Sea-bed probing")]
    [Tooltip("Patch size near the surface, in metres.")]
    [SerializeField] float fineCellMetres = 4f;
    [Tooltip("Patch size high above the water, in metres.")]
    [SerializeField] float coarseCellMetres = 16f;
    [Tooltip("Altitude at which the coarse patches take over.")]
    [SerializeField] float coarseAltitude = 40f;
    [Tooltip("How deep a probe looks before calling it open water.")]
    [SerializeField] float probeDepthMetres = 30f;
    [Tooltip("Metres of water a patch needs before a fish may swim there.")]
    [SerializeField] float minWaterDepth = 1.2f;
    [Tooltip("Hard cap on raycasts per frame. This is what stops arriving somewhere new from spiking.")]
    [SerializeField] int maxProbesPerFrame = 4;
    [Tooltip("Cap on the probe backlog. Extra patches are simply retried later.")]
    [SerializeField] int maxQueuedProbes = 256;
    [Tooltip("Ceiling on remembered sea-floor patches. Reached only by walking a very long coastline; the cache then rebuilds as you go.")]
    [SerializeField] int maxCachedPatches = 200000;
    [Tooltip("Cap on new fish per frame, so a fresh patch of sea fills in over a moment rather than all at once.")]
    [SerializeField] int maxSpawnsPerFrame = 3;
}
