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
/// <b>Species are the planet's own.</b> <c>PlanetEconomy.CatchableIndices</c> is
/// the same list the bite roll uses, and tier odds come from the same
/// <c>FishingRules.RollTier</c>, so rares are as rare to see as they are to
/// catch and the water can never drift out of sync with what bites in it.
///
/// ── MOTION (pass 2) ───────────────────────────────────────────────────────
/// Verified headlessly rather than by eye: the model is pure maths, so it was
/// ported to Python and measured against synthetic banks, a rolling sea bed and
/// an obstacle the probe grid cannot see, the way FishingRules is swept by
/// verify-fishing.py. Depth is a critically damped spring (<c>MoveTowards</c>
/// was bang-bang and snapped the pitch 27.4 deg in a single frame); the sea bed
/// is bilinearly interpolated on a cube-face grid (a raw per-patch read jumped
/// 83.8 cm within a centimetre of travel); the facing comes from a LOW-PASSED
/// velocity, which is Bobber's line and the half that was missed first time;
/// and the depth constraint is the shallowest bed ALONG THE PATH AHEAD, so a
/// fish rises before the ground arrives instead of ploughing into a bank.
///
/// ── LOOK AND DENSITY (pass 3) ─────────────────────────────────────────────
/// Sam's second playtest:
///
///   • <b>"They don't get less visible the deeper they are."</b> Pass 1 leaned
///     on the ocean post-effect to fade them and that was simply wrong — the
///     fish are drawn with shadow casting off, which in this pipeline is the
///     same list the camera depth texture is built from, so the ocean effect
///     never even knows they are there. The fade is now done in the fish's own
///     shader (<c>Custom/AmbientFish</c>), per fish, from how much water sits
///     between the camera and it. The rarity glow fades on the SAME curve,
///     which is the half Sam actually noticed.
///
///   • <b>Too dense, and denser still in small water.</b> Two causes. The
///     spawn radius was uniform in RADIUS, which piles fish toward the middle
///     of a disc — it needs a square root to be uniform in AREA. And the count
///     was fixed, so a pond and an open ocean got the same number of fish; the
///     population is now driven by how much of the ring actually IS water,
///     which the patch cache already knows.
///
///   • <b>"I shouldn't be able to see new fish spawning in."</b> Fish now fade
///     up from invisible and fade out again at the edge of the field, using the
///     same per-instance fade the depth uses — so recycling is free of pops.
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
        _level = 0;
        _whiskerCursor = 0;
        _waterFrac = 1f;
        _nextWaterScan = 0f;
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
        public Vector3 dirL;       // planet-local heading, tangential to the water surface
        public Vector3 velL;       // LOW-PASSED velocity; the facing is built from this, not
                                   // from the raw frame delta. This is Bobber's line, and
                                   // leaving it out is half of why pass 1 looked jerky.
        public float radialVel;    // owned by the depth spring, so vertical speed is continuous
        public float speed;        // m/s, cruising
        public float phase;        // tail-beat phase
        public float wanderSeed;
        public float steerSign;    // which way this fish turns away from shallows
        public float avoid;        // 0..1 whisker avoidance, decays. STEERING ONLY.
        public float born;         // 0..1 fade-up since spawning, so nothing ever pops in
        public int species;        // index into FishingRules.Species
        public int tier;           // draw batch — every species of a tier shares one mesh
        public float bodyLen;
        public float halfLen;
        public Vector3 meshScale;
        public float depth;        // metres under the surface it would like to sit at
        public float scatter;      // 0..1, decays — the "something spooked me" boost
    }

    Fish[] _fish;
    int _whiskerCursor;
    float _waterFrac = 1f;
    float _nextWaterScan;

    // ── the sea-bed cache ────────────────────────────────────────────────────
    //
    // THE WHOLE ANSWER to "fish must only swim in water you can actually see,
    // and must never swim through the terrain" (Sam, 2026-09-09), without
    // costing a raycast per fish per frame.
    //
    // The sea floor is cut into patches on a CUBE-FACE grid, so the four patches
    // around any point are real neighbours and can be interpolated between.
    //
    // The first time a patch is needed, ONE ray answers it, cast from ABOVE THE
    // HIGHEST TERRAIN straight down the radial at the patch CENTRE:
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
    // is never placed in an air-filled cave below sea level either.
    //
    // Terrain does not move, so an answer never goes stale: standing still costs
    // ZERO raycasts. Probes are hard-capped per frame, so arriving somewhere new
    // fills in over a moment instead of spiking.

    const float NotWater = -1f;

    readonly Dictionary<long, float> _bed = new Dictionary<long, float>(4096);
    readonly HashSet<long> _queued = new HashSet<long>();
    readonly Queue<KeyValuePair<long, Vector3>> _probeQueue = new Queue<KeyValuePair<long, Vector3>>();

    /// <summary>Which cube face a direction belongs to, and its grid coordinates
    /// on that face. One grid unit is about one patch at the water's surface.</summary>
    static void CubeCell(Vector3 dir, float scale, out int face, out float fu, out float fv)
    {
        float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y), az = Mathf.Abs(dir.z);
        float u, v, m;
        if (ax >= ay && ax >= az) { face = dir.x >= 0f ? 0 : 1; m = ax; u = dir.y; v = dir.z; }
        else if (ay >= az) { face = dir.y >= 0f ? 2 : 3; m = ay; u = dir.x; v = dir.z; }
        else { face = dir.z >= 0f ? 4 : 5; m = az; u = dir.x; v = dir.y; }
        if (m < 1e-6f) m = 1e-6f;
        fu = (u / m) * scale;
        fv = (v / m) * scale;
    }

    /// <summary>The direction of a patch CENTRE — where its probe is cast, which
    /// is what makes interpolating between patches meaningful.</summary>
    static Vector3 CubeDir(int face, float fu, float fv, float scale)
    {
        float u = fu / scale, v = fv / scale;
        switch (face)
        {
            case 0: return new Vector3(1f, u, v).normalized;
            case 1: return new Vector3(-1f, u, v).normalized;
            case 2: return new Vector3(u, 1f, v).normalized;
            case 3: return new Vector3(u, -1f, v).normalized;
            case 4: return new Vector3(u, v, 1f).normalized;
            default: return new Vector3(u, v, -1f).normalized;
        }
    }

    static long CellKey(int face, int i, int j, int level)
    {
        long a = (i + 524288) & 0xFFFFF;
        long b = (j + 524288) & 0xFFFFF;
        return ((long)level << 43) | ((long)face << 40) | (a << 20) | b;
    }

    float CellScale(int level) => _oceanR / (level == 0 ? fineCellMetres : coarseCellMetres);

    /// <summary>
    /// One patch's bed radius. <c>NotWater</c> is returned as the WATERLINE
    /// rather than as a failure: that way land pulls the interpolated sea bed
    /// smoothly up toward the surface as a fish approaches the shore — the fish
    /// rises, then the turn test fires — instead of the bed vanishing at the
    /// boundary and leaving a cliff in the constraint.
    /// </summary>
    bool PatchBed(int face, int i, int j, int level, float scale, out float bed)
    {
        long k = CellKey(face, i, j, level);
        if (_bed.TryGetValue(k, out float v))
        {
            bed = v <= NotWater ? _oceanR : v;
            return true;
        }
        if (_queued.Count < maxQueuedProbes && _queued.Add(k))
            _probeQueue.Enqueue(new KeyValuePair<long, Vector3>(
                k, CubeDir(face, i + 0.5f, j + 0.5f, scale)));
        bed = 0f;
        return false;
    }

    /// <summary>
    /// Sea-bed radius under a planet-local point, BILINEARLY INTERPOLATED
    /// between the four surrounding patch centres. Continuous in position, which
    /// is what stops the depth target stepping every time a fish crosses a patch
    /// boundary — the direct cause of the 27-degree pitch snaps in pass 1.
    ///
    /// False means "nothing around here is probed yet"; the caller then holds
    /// its depth rather than descending into ground it cannot see.
    /// </summary>
    bool BedSmooth(Vector3 posL, int level, out float bed)
    {
        bed = 0f;
        if (posL.sqrMagnitude < 1e-6f) return false;
        float scale = CellScale(level);
        CubeCell(posL.normalized, scale, out int face, out float fu, out float fv);

        float qu = fu - 0.5f, qv = fv - 0.5f;
        int i0 = Mathf.FloorToInt(qu), j0 = Mathf.FloorToInt(qv);
        float tu = qu - i0, tv = qv - j0;

        float sum = 0f, wsum = 0f;
        for (int di = 0; di <= 1; di++)
            for (int dj = 0; dj <= 1; dj++)
            {
                float w = (di == 0 ? 1f - tu : tu) * (dj == 0 ? 1f - tv : tv);
                if (w <= 0f) continue;
                if (!PatchBed(face, i0 + di, j0 + dj, level, scale, out float b)) continue;
                sum += b * w;
                wsum += w;
            }
        if (wsum <= 1e-4f) return false;
        bed = sum / wsum;
        return true;
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
        if (r > _oceanR - minSwimWater) return NotWater;        // land / roof / too thin to swim in
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
    int _level;

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
            // planet's local frame) and the species list.
            _planet = chosen;
            _planetT = chosen.transform;
            _planetName = chosen.bodyName;
            _bed.Clear();
            _queued.Clear();
            _probeQueue.Clear();
            _waterFrac = 1f;
            _nextWaterScan = 0f;
            if (_fish != null) for (int i = 0; i < _fish.Length; i++) _fish[i].alive = false;
            BuildSpeciesList();
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

    readonly List<int> _allowed = new List<int>();       // this planet's catch list, bounty removed

    // Batching is by TIER, not by species: the species tint and the fade are now
    // per-INSTANCE (Custom/AmbientFish), so every fish sharing a tier's mesh goes
    // out in one call per submesh however many species are on screen. Pass 1/2
    // needed the stock Standard shader, which has no per-instance colour, so it
    // needed a material and a draw call per species.
    Material[][] _tierMats;         // [tier][submesh]
    Matrix4x4[][] _tierMatrices;    // [tier][fish]
    Vector4[][] _tierTints;         // [tier][fish]  rgb = species tint
    Vector4[][] _tierFades;         // [tier][fish]  x = fade, y = glow
    MaterialPropertyBlock[] _tierMpb;
    int[] _tierCount;
    Material _baseMat;

    static readonly int _SpeciesTintID = Shader.PropertyToID("_SpeciesTint");
    static readonly int _FadeGlowID = Shader.PropertyToID("_FadeGlow");
    static readonly int _PartColorID = Shader.PropertyToID("_PartColor");
    static readonly int _DeepColorID = Shader.PropertyToID("_DeepColor");

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
        AllocateBatches();
        return true;
    }

    void AllocateBatches()
    {
        ReleaseMaterials();
        _tierMats = new Material[3][];
        _tierMatrices = new Matrix4x4[3][];
        _tierTints = new Vector4[3][];
        _tierFades = new Vector4[3][];
        _tierMpb = new MaterialPropertyBlock[3];
        _tierCount = new int[3];
        for (int t = 0; t < 3; t++)
        {
            _tierMatrices[t] = new Matrix4x4[maxFish];
            _tierTints[t] = new Vector4[maxFish];
            _tierFades[t] = new Vector4[maxFish];
            _tierMpb[t] = new MaterialPropertyBlock();
        }
    }

    void BuildSpeciesList()
    {
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
            var sh = Shader.Find("Custom/AmbientFish");
            if (sh == null) return null;
            _baseMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _baseMat.enableInstancing = true;
            Debug.LogWarning("[AmbientFishField] Resources/AmbientFish.mat missing — "
                           + "falling back to a runtime material, which may draw nothing "
                           + "in a BUILD. Restore the asset.");
        }
        return _baseMat;
    }

    Material[] MaterialsForTier(int tier)
    {
        if (_tierMats[tier] != null) return _tierMats[tier];
        var baseMat = BaseMaterial();
        if (baseMat == null) return null;

        var tm = _tiers[tier];
        var mats = new Material[tm.subCount];
        for (int s = 0; s < tm.subCount; s++)
        {
            var m = new Material(baseMat) { hideFlags = HideFlags.HideAndDontSave };
            m.enableInstancing = true;
            m.SetColor(_PartColorID, tm.partColours[s]);
            m.SetColor(_DeepColorID, deepWaterColour);
            mats[s] = m;
        }
        _tierMats[tier] = mats;
        return mats;
    }

    void ReleaseMaterials()
    {
        if (_tierMats != null)
            for (int i = 0; i < _tierMats.Length; i++)
            {
                if (_tierMats[i] == null) continue;
                for (int s = 0; s < _tierMats[i].Length; s++)
                    if (_tierMats[i][s] != null) Destroy(_tierMats[i][s]);
                _tierMats[i] = null;
            }
        _tierMats = null;
        _tierMatrices = null;
        _tierTints = null;
        _tierFades = null;
        _tierMpb = null;
        _tierCount = null;
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
        if (_tierMatrices == null) return;

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
        // 200 m you cannot see the difference. Hysteresis so hovering at the
        // threshold cannot flip the whole sea bed back and forth every frame.
        if (_level == 0 && alt > coarseAltitude) _level = 1;
        else if (_level == 1 && alt < coarseAltitude * 0.75f) _level = 0;
        int level = _level;
        float ring = Mathf.Lerp(nearRing, farRing, Mathf.InverseLerp(0f, 200f, Mathf.Max(alt, 0f)));

        DrainProbes();

        if (_fish == null || _fish.Length != maxFish)
        {
            _fish = new Fish[maxFish];
            AllocateBatches();
        }
        for (int i = 0; i < _tierCount.Length; i++) _tierCount[i] = 0;

        // How much of the ring is actually water. THIS is what stops a pond
        // getting an ocean's worth of fish crammed into it (Sam, playtest 2:
        // "if I'm in a spot with very little water it gets super dense").
        UpdateWaterFraction(camL, ring, level);
        int target = Mathf.Clamp(Mathf.RoundToInt(maxFish * _waterFrac), 0, maxFish);

        bool hasDisturb = Time.time < _disturbUntil;
        Vector3 disturbL = hasDisturb ? _planetT.InverseTransformPoint(_disturbW) : Vector3.zero;
        bool camUnder = alt < 0f;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        int spawnBudget = maxSpawnsPerFrame;
        int spawnTries = maxSpawnsPerFrame * 4;

        CastWhiskers();

        int aliveNow = 0;
        for (int i = 0; i < _fish.Length; i++) if (_fish[i].alive) aliveNow++;

        for (int i = 0; i < _fish.Length; i++)
        {
            if (!_fish[i].alive)
            {
                if (aliveNow >= target || spawnBudget <= 0 || spawnTries <= 0) continue;
                spawnTries--;
                if (TrySpawn(ref _fish[i], camL, ring, level)) { spawnBudget--; aliveNow++; }
                else continue;
            }
            if (!Step(ref _fish[i], camL, ring, level, dt, hasDisturb, disturbL)) continue;
            Accumulate(ref _fish[i], camL, ring, camUnder);
        }

        Draw();
    }

    /// <summary>
    /// The fraction of the ring around you that is swimmable water, from the
    /// patch cache — no extra raycasts, just lookups. The live population is
    /// scaled by it, so a small pond holds a few fish and open sea holds the
    /// full pool, instead of both holding the same number.
    /// </summary>
    void UpdateWaterFraction(Vector3 camL, float ring, int level)
    {
        if (Time.time < _nextWaterScan) return;
        _nextWaterScan = Time.time + 0.5f;

        Vector3 up = camL.normalized;
        Vector3 t1 = Vector3.Cross(up, Vector3.up);
        if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(up, Vector3.right);
        t1.Normalize();
        Vector3 t2 = Vector3.Cross(up, t1);

        const int N = 24;
        const float Golden = 2.39996323f;   // golden angle: an even spiral, no clumping
        int water = 0, known = 0;
        for (int i = 0; i < N; i++)
        {
            float d = ring * Mathf.Sqrt((i + 0.5f) / N);   // sqrt => uniform in AREA
            float a = i * Golden;
            Vector3 dir = (up * _oceanR + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * d).normalized;
            if (!BedSmooth(dir * _oceanR, level, out float bed)) continue;
            known++;
            if (_oceanR - bed >= minSwimWater + 0.4f) water++;
        }
        if (known == 0) return;
        // Eased, so walking past a headland does not visibly cull the shoal.
        _waterFrac = Mathf.Lerp(_waterFrac, (float)water / known, 0.35f);
    }

    /// <summary>
    /// The backstop Sam asked about — his "sphere collider, like a roomba", done
    /// predictively instead of reactively. The patch grid is a height field: it
    /// cannot see a boulder, a spire that fell between probe points, or a prop
    /// that is not on the terrain layer, and a fish measurably swam a metre into
    /// one. So each fish casts ONE short ray along its heading, round-robin —
    /// a couple of rays a frame for the whole pool, against 2,400 a second for
    /// the naive "ray per fish per frame", and with no rigidbodies or contacts.
    ///
    /// A hit only raises <c>avoid</c>, which only steers. It must NEVER reach
    /// the depth target: sparse, chunky ray data driving the continuous depth
    /// constraint spiked the rendered pitch to 71-84 degrees per frame in
    /// testing — worse than the bug pass 2 existed to fix.
    /// </summary>
    void CastWhiskers()
    {
        if (_fish == null || _fish.Length == 0 || whiskerRaysPerFrame <= 0) return;
        int mask = GroundMask;
        if (mask == 0) return;
        for (int n = 0; n < whiskerRaysPerFrame; n++)
        {
            _whiskerCursor = (_whiskerCursor + 1) % _fish.Length;
            int i = _whiskerCursor;
            if (!_fish[i].alive) continue;
            Vector3 originW = _planetT.TransformPoint(_fish[i].noseL);
            Vector3 dirW = _planetT.TransformDirection(_fish[i].dirL);
            if (dirW.sqrMagnitude < 1e-6f) continue;
            if (Physics.Raycast(originW, dirW.normalized, whiskerLength, mask,
                                QueryTriggerInteraction.Ignore))
                _fish[i].avoid = 1f;
        }
    }

    bool TrySpawn(ref Fish f, Vector3 camL, float ring, int level)
    {
        Vector3 up = camL.normalized;
        Vector3 t1 = Vector3.Cross(up, Vector3.up);
        if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(up, Vector3.right);
        t1.Normalize();
        Vector3 t2 = Vector3.Cross(up, t1);

        // SQUARE ROOT, not a straight random radius. A disc has more area near
        // its rim, so a uniform random radius piles fish toward the middle —
        // which is exactly Sam's "very dense around me". sqrt spreads them
        // evenly over the area instead. They also start well out and fade up,
        // so recycling is never something you catch happening.
        float u = Random.Range(spawnInnerFrac * spawnInnerFrac, 1f);
        float d = ring * Mathf.Sqrt(u);
        float a = Random.value * Mathf.PI * 2f;
        Vector3 dir = (up * _oceanR + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * d).normalized;

        if (!BedSmooth(dir * _oceanR, level, out float bed)) return false;   // nothing probed here yet
        float water = _oceanR - bed;
        if (water < minSwimWater + 0.4f) return false;

        float depth = Random.Range(0.7f, Mathf.Min(maxSwimDepth, water - bedClearance - 0.2f));
        if (depth <= 0.5f) return false;

        Vector3 nose = dir * (_oceanR - depth);
        Vector3 heading = Vector3.ProjectOnPlane(t1 * Mathf.Cos(a * 1.7f) + t2 * Mathf.Sin(a * 1.7f), dir);
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.ProjectOnPlane(t1, dir);
        heading.Normalize();

        int sp = RollSpecies(nose);
        if (sp < 0) return false;

        int tier = (int)FishingRules.Species[sp].tier;
        var tm = _tiers[tier];
        float weight = FishingRules.RollWeight(sp, Random.value);
        float bodyLen = FishingRules.BodyLengthForWeight(weight);
        float girth = FishingRules.GirthFactorForWeight(weight);
        float baseScale = bodyLen / tm.longest;

        f.alive = true;
        f.noseL = nose;
        f.dirL = heading;
        f.speed = Random.Range(cruiseSpeedMin, cruiseSpeedMax);
        f.velL = heading * f.speed;      // so the very first frame's facing is already right
        f.radialVel = 0f;
        f.phase = Random.value * 10f;
        f.wanderSeed = Random.value * 100f;
        f.steerSign = Random.value < 0.5f ? -1f : 1f;
        f.avoid = 0f;
        f.born = 0f;
        f.species = sp;
        f.tier = tier;
        f.bodyLen = bodyLen;
        f.halfLen = bodyLen * 0.5f;
        // Models are authored facing -Z, so local X is width, Y is belly depth
        // and Z is length — the same axis split the held/hooked fish uses.
        f.meshScale = new Vector3(baseScale * girth,
                                  baseScale * (1f + (girth - 1f) * 0.6f),
                                  baseScale);
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
        if (sp < 0) sp = _allowed[Random.Range(0, _allowed.Count)];
        return sp;
    }

    /// <summary>
    /// The SHALLOWEST sea bed along a fish's path — ahead of it, under it, and
    /// one body length BEHIND it (the tail is what clips a ridge the nose has
    /// already cleared). The heart of the fix for swimming through banks: pass 1
    /// constrained depth by the bed underneath only, so a fish reacted to ground
    /// it was already inside.
    /// </summary>
    float ShallowestAround(Vector3 noseL, Vector3 dirL, float bodyLen, int level, out bool known)
    {
        known = false;
        float shallow = 0f;
        for (int k = -1; k <= depthLookSteps; k++)
        {
            float t = k < 0 ? -bodyLen : depthLookAhead * k / depthLookSteps;
            if (!BedSmooth(noseL + dirL * t, level, out float b)) continue;
            if (!known || b > shallow) { shallow = b; known = true; }
        }
        return shallow;
    }

    bool Step(ref Fish f, Vector3 camL, float ring, int level, float dt,
              bool hasDisturb, Vector3 disturbL)
    {
        Vector3 up = f.noseL.normalized;

        // Leave when the field moves off them. The fade at the rim means they
        // are already invisible by the time this fires.
        if ((f.noseL - camL).sqrMagnitude > ring * ring * 1.45f) { f.alive = false; return false; }

        f.born = Mathf.Min(1f, f.born + dt / Mathf.Max(0.05f, fadeInSeconds));

        // ── scatter ──────────────────────────────────────────────────────────
        // You, and the bobber's splash. Two distance checks. Nothing about the
        // fishing changes — they just get out of the way.
        if (f.scatter > 0f) f.scatter = Mathf.Max(0f, f.scatter - dt / scatterSeconds);
        if (f.avoid > 0f) f.avoid = Mathf.Max(0f, f.avoid - dt * avoidDecay);
        Vector3 flee = Vector3.zero;
        if ((f.noseL - camL).sqrMagnitude < playerScatterRadius * playerScatterRadius)
            flee = f.noseL - camL;
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

        // ── veer off the shallows, PROPORTIONALLY ────────────────────────────
        Vector3 aheadTurn = f.noseL + f.dirL * turnLookAhead;
        float blocked;
        if (BedSmooth(aheadTurn, level, out float bedTurn))
        {
            float waterAhead = _oceanR - bedTurn;
            blocked = Mathf.Clamp01(Mathf.InverseLerp(turnStartWater, minSwimWater, waterAhead));
        }
        else blocked = 1f;   // unprobed: treat as blocked and hold, never swim on blind

        float steer = Mathf.Max(blocked, f.avoid);
        if (steer > 0.01f)
        {
            // Turn toward the DEEPER side rather than always the same way, so a
            // fish follows the run of the shore instead of pinballing off it.
            Vector3 side = Vector3.Cross(up, f.dirL);
            bool okR = BedSmooth(f.noseL + (f.dirL * 0.7f + side) * turnLookAhead, level, out float bedR);
            bool okL = BedSmooth(f.noseL + (f.dirL * 0.7f - side) * turnLookAhead, level, out float bedL);
            if (okR && okL) f.steerSign = bedR < bedL ? 1f : -1f;
            f.dirL = (Quaternion.AngleAxis(f.steerSign * steerRate * steer * dt, up) * f.dirL).normalized;
        }

        // Ease off while turning away or climbing — a fish nosing up a bank
        // slows down, and it also lets the climb keep up with the ground.
        float speed = f.speed * (1f + f.scatter * scatterSpeedBoost) * (1f - 0.5f * steer);

        Vector3 prevNose = f.noseL;
        f.noseL += f.dirL * speed * dt;

        // ── depth: a SPRING, not a step ──────────────────────────────────────
        float shallowest = ShallowestAround(f.noseL, f.dirL, f.bodyLen, level, out bool bedKnown);

        // No "extra clearance on steep ground" here on purpose: it was tried and
        // MEASURED, and it bought almost nothing (1.07 m -> 0.99 m of
        // penetration) while making the motion notably less smooth (worst pitch
        // change 7.9 -> 15.5 deg/frame). Finer patches did the job instead.
        float clearance = bedClearance;

        float breathe = Mathf.Sin(Time.time * 0.3f + f.wanderSeed) * 0.4f;
        float wantR = _oceanR - Mathf.Max(0.4f, f.depth + breathe);
        float ceilR = _oceanR - surfaceClearance;
        // Mathf.Clamp does NOT sort its bounds: called with min > max it returns
        // the MIN. Land contributes the waterline itself to the interpolated bed
        // (that is what makes the shore a smooth ramp rather than a cliff), so
        // approaching a beach the floor would exceed the ceiling and the clamp
        // would hand back a radius ABOVE THE WATER — a fish rising out of the
        // sea for the frames before it turned away. The floor is capped first.
        float floorR = Mathf.Min(shallowest + clearance, ceilR);
        float targetR = bedKnown
            ? Mathf.Clamp(wantR, floorR, ceilR)
            : f.noseL.magnitude;      // nothing probed: hold this depth, do not dive blind
        float r = Mathf.SmoothDamp(f.noseL.magnitude, targetR, ref f.radialVel,
                                   depthSmoothTime, maxClimbSpeed, dt);
        f.noseL = f.noseL.normalized * r;

        // ── the facing velocity, LOW-PASSED ──────────────────────────────────
        // Bobber does exactly this and says why: "facing comes from the
        // follower's own integrated velocity, smooth by construction". Pass 1
        // used the raw one-frame delta, so every hitch in the path became a
        // visible snap in the fish.
        Vector3 instVel = (f.noseL - prevNose) / Mathf.Max(dt, 1e-4f);
        f.velL = Vector3.Lerp(f.velL, instVel, 1f - Mathf.Exp(-facingSmoothing * dt));

        f.phase += (3.5f + speed * 1.8f) * dt;
        return true;
    }

    /// <summary>
    /// How lost in the water a fish is, 0..1. Sam, playtest 2: "they don't seem
    /// to get less visible the deeper they are ... a foot underwater or 10 feet
    /// underwater they look the same."
    ///
    /// Pass 1 relied on the ocean post-effect for this and it never applied: the
    /// fish are drawn with shadow casting off, which in this pipeline is the
    /// same list the camera depth texture is built from, so the ocean effect
    /// does not know they exist. Owning the fade outright is both correct and
    /// predictable — and it is the only way the rarity GLOW can fade too, which
    /// is the part that stood out most.
    ///
    /// Above the surface it is the fish's depth that buries it (plus a little
    /// for the slant of a long look across the water); underwater it is plain
    /// distance. The spawn ramp and the rim fade ride the same value, so fish
    /// never pop in or out.
    /// </summary>
    float FadeFor(ref Fish f, Vector3 camL, float ring, bool camUnder)
    {
        float depth = Mathf.Max(0f, _oceanR - f.noseL.magnitude);
        float dist = Vector3.Distance(f.noseL, camL);
        float water = camUnder ? dist : depth + dist * slantFade;
        float depthFade = 1f - Mathf.Exp(-water / Mathf.Max(0.5f, fadeDistance));

        // Fade out again at the rim of the field, so a recycled fish leaves as
        // quietly as it arrived.
        float edge = Mathf.Clamp01(Mathf.InverseLerp(ring * 1.15f, ring * 0.9f, dist));
        float visible = Mathf.Clamp01(f.born) * edge * (1f - depthFade);
        return Mathf.Clamp01(1f - visible);
    }

    void Accumulate(ref Fish f, Vector3 camL, float ring, bool camUnder)
    {
        int tier = f.tier;
        if (_tierCount == null || tier < 0 || tier >= _tierCount.Length) return;
        int n = _tierCount[tier];
        if (n >= _tierMatrices[tier].Length) return;

        Vector3 up = f.noseL.normalized;

        Vector3 swim = f.velL;
        if (swim.sqrMagnitude < 1e-6f) swim = f.dirL;
        swim.Normalize();

        // Sam's 45-degree law: a fish never points steeper than 45 degrees off
        // horizontal, climbing or diving. With the spring bounding the vertical
        // speed this almost never binds now, so it no longer reads as a kink.
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

        var tm = _tiers[tier];
        Vector3 centreW = noseW - noseDirW * f.halfLen;
        Vector3 pos = centreW - rot * Vector3.Scale(f.meshScale, tm.centre);

        Color tint = FishSpeciesVisuals.TintOf(f.species);
        float glow = FishSpeciesVisuals.EmissionFor(f.species) * ambientGlowScale;

        _tierMatrices[tier][n] = Matrix4x4.TRS(pos, rot, f.meshScale);
        _tierTints[tier][n] = new Vector4(tint.r, tint.g, tint.b, 1f);
        _tierFades[tier][n] = new Vector4(FadeFor(ref f, camL, ring, camUnder), glow, 0f, 0f);
        _tierCount[tier] = n + 1;
    }

    void Draw()
    {
        for (int tier = 0; tier < _tierCount.Length; tier++)
        {
            int n = _tierCount[tier];
            if (n == 0) continue;
            var mats = MaterialsForTier(tier);
            if (mats == null) continue;
            var tm = _tiers[tier];

            var mpb = _tierMpb[tier];
            mpb.Clear();
            mpb.SetVectorArray(_SpeciesTintID, _tierTints[tier]);
            mpb.SetVectorArray(_FadeGlowID, _tierFades[tier]);

            for (int s = 0; s < tm.subCount && s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                // Shadows and probes off: a fish under water casting a sun
                // shadow is wrong anyway, and both are per-instance cost we do
                // not need. Opaque queue (2000) keeps them out of the
                // transparent queue and its sorting.
                Graphics.DrawMeshInstanced(tm.mesh, s, mats[s], _tierMatrices[tier], n, mpb,
                                           ShadowCastingMode.Off, false, 0, null,
                                           LightProbeUsage.Off);
            }
        }
    }

    // ================= tuning (appended at END per conventions) =================

    [Header("Field")]
    [Tooltip("Master switch. Off = the water is empty, exactly as before this existed.")]
    [SerializeField] bool enableField = true;
    [Tooltip("Most fish that can exist at once, on wide open water. In smaller water the live count is scaled down by how much of the area around you actually IS water, so a pond never gets crammed.")]
    [SerializeField] int maxFish = 22;
    [Tooltip("Radius of the bubble of fish around you at the water's surface, in metres.")]
    [SerializeField] float nearRing = 45f;
    [Tooltip("Radius of that bubble at 200 m altitude — wider so they spread out under you instead of clumping.")]
    [SerializeField] float farRing = 150f;
    [Tooltip("Fish never spawn closer than this fraction of the ring, so they arrive well out and fade up rather than appearing beside you.")]
    [SerializeField] float spawnInnerFrac = 0.6f;
    [Tooltip("Metres above the water past which fish are switched off. A half-metre fish is under two pixels up here.")]
    [SerializeField] float maxAltitude = 250f;
    [Tooltip("Metres below the water past which fish are switched off.")]
    [SerializeField] float maxDepthBelow = 60f;

    [Header("Visibility in water")]
    [Tooltip("Metres of water that hides a fish by about two thirds. Lower = murkier water, fish disappear sooner with depth.")]
    [SerializeField] float fadeDistance = 5f;
    [Tooltip("How much horizontal distance counts as water when you are above the surface — a long flat look across a lake passes through more water than the depth alone.")]
    [SerializeField] float slantFade = 0.18f;
    [Tooltip("The colour fish fade into. Should read as this planet's deep water.")]
    [SerializeField] Color deepWaterColour = new Color(0.05f, 0.14f, 0.20f, 1f);
    [Tooltip("Seconds a newly placed fish takes to fade up. Stops you ever seeing one appear.")]
    [SerializeField] float fadeInSeconds = 2.5f;
    [Tooltip("Rarity glow, as a fraction of the glow a caught fish has. It fades with depth on the same curve as the body.")]
    [SerializeField] float ambientGlowScale = 0.7f;

    [Header("Swimming")]
    [SerializeField] float cruiseSpeedMin = 0.55f;
    [SerializeField] float cruiseSpeedMax = 1.35f;
    [Tooltip("Degrees per second of lazy wander.")]
    [SerializeField] float wanderTurnRate = 55f;
    [Tooltip("Degrees per second a fish turns at FULL avoidance. It ramps in, so this is not a snap.")]
    [SerializeField] float steerRate = 150f;
    [Tooltip("Deepest a fish will sit below the surface.")]
    [SerializeField] float maxSwimDepth = 6.5f;
    [Tooltip("Metres of water a fish keeps between itself and the sea bed.")]
    [SerializeField] float bedClearance = 0.55f;
    [Tooltip("Metres a fish keeps below the surface, so it never breaks through.")]
    [SerializeField] float surfaceClearance = 0.4f;
    [Tooltip("Degrees the body sweeps either side of its heading — the tail beat.")]
    [SerializeField] float tailSweepDegrees = 12f;

    [Header("Following the bottom")]
    [Tooltip("How far along its own path a fish reads the sea bed to decide its depth. THIS is what stops it swimming into banks — it starts rising before the ground arrives.")]
    [SerializeField] float depthLookAhead = 5f;
    [Tooltip("How many points along that path are sampled.")]
    [SerializeField] int depthLookSteps = 3;
    [Tooltip("Spring response for depth changes, in seconds. Larger = lazier. This being a spring rather than a fixed climb rate is what removed the pitch snapping.")]
    [SerializeField] float depthSmoothTime = 0.55f;
    [Tooltip("Ceiling on how fast a fish rises or dives, m/s.")]
    [SerializeField] float maxClimbSpeed = 1.6f;
    [Tooltip("How hard the rendered facing is smoothed. Higher = snappier, lower = floatier.")]
    [SerializeField] float facingSmoothing = 8f;

    [Header("Turning away from the shore")]
    [Tooltip("How far ahead a fish looks when deciding to veer off, in metres.")]
    [SerializeField] float turnLookAhead = 5f;
    [Tooltip("Water this thin is unswimmable — full avoidance.")]
    [SerializeField] float minSwimWater = 1.5f;
    [Tooltip("Water this thin starts the fish curving away. Between this and the minimum, avoidance ramps in.")]
    [SerializeField] float turnStartWater = 3f;

    [Header("Whisker (the roomba backstop)")]
    [Tooltip("Raycasts per frame shared across the whole pool, round-robin. Steering only — it never touches depth.")]
    [SerializeField] int whiskerRaysPerFrame = 3;
    [Tooltip("How far ahead the whisker ray reaches, in metres.")]
    [SerializeField] float whiskerLength = 2.6f;
    [Tooltip("How fast whisker avoidance fades once the way is clear, per second.")]
    [SerializeField] float avoidDecay = 2.2f;

    [Header("Reacting to you")]
    [Tooltip("Metres. Swim closer than this and they break away.")]
    [SerializeField] float playerScatterRadius = 3.5f;
    [Tooltip("Seconds a spooked fish stays spooked.")]
    [SerializeField] float scatterSeconds = 1.8f;
    [Tooltip("Extra speed while fleeing, as a multiple of cruise.")]
    [SerializeField] float scatterSpeedBoost = 2.2f;

    [Header("Sea-bed probing")]
    [Tooltip("Patch size near the surface, in metres. THE number for fish clipping terrain: 4 m measured 0.20 m of penetration on realistic ground, 2.5 m measured zero. Smaller costs more probes when you move.")]
    [SerializeField] float fineCellMetres = 2.5f;
    [Tooltip("Patch size high above the water, in metres.")]
    [SerializeField] float coarseCellMetres = 16f;
    [Tooltip("Altitude at which the coarse patches take over.")]
    [SerializeField] float coarseAltitude = 40f;
    [Tooltip("How deep a probe looks before calling it open water.")]
    [SerializeField] float probeDepthMetres = 30f;
    [Tooltip("Hard cap on sea-bed raycasts per frame. This is what stops arriving somewhere new from spiking.")]
    [SerializeField] int maxProbesPerFrame = 6;
    [Tooltip("Cap on the probe backlog. Extra patches are simply retried later.")]
    [SerializeField] int maxQueuedProbes = 256;
    [Tooltip("Ceiling on remembered sea-floor patches. Reached only by walking a very long coastline; the cache then rebuilds as you go.")]
    [SerializeField] int maxCachedPatches = 200000;
    [Tooltip("Cap on new fish per frame, so a fresh patch of sea fills in over a moment rather than all at once.")]
    [SerializeField] int maxSpawnsPerFrame = 2;
}
