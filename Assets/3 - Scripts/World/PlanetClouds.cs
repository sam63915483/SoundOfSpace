using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Puffy 3D clouds overhead on planets that have an atmosphere, and their
/// shadows on the ground. Purely visual, toggleable in the pause menu.
///
/// ── Pass 2: real geometry (Sam, 2026-09-09) ───────────────────────────────
/// The first attempt was a texture painted on a sphere shell, and Sam's verdict
/// was blunt and correct: "the clouds are flat ... i was hoping for actual 3d
/// puffy looking white clouds that are overhead ... i would not use them in my
/// game." A shell can never fix that — a picture on a sphere has no parallax,
/// no silhouette against itself and no volume.
///
/// So a cloud is now a CLUSTER OF ACTUAL LUMPS OF MESH, built at runtime by
/// merging a handful of low-poly spheres into a flat-bottomed blob. It looks
/// three-dimensional because it is: fly around one and its far side passes
/// behind its near side.
///
/// This is also the CHEAPER answer, which is the easy thing to miss.
/// Volumetric raymarching is unaffordable; stacked see-through billboards drown
/// in overdraw, which is the thing Sam was right to worry about from the start.
/// Opaque low-poly lumps are just meshes — a few hundred triangles each,
/// instanced, one draw call per shape, no transparency, no sorting, no
/// overdraw.
///
/// ── The shadows, and the grass regression that killed the first version ───
/// Version one lit the ground with a COOKIE on the sun: a texture multiplying
/// the sunlight everywhere. It worked, and it also wrecked the grass. The grass
/// has a hand-written lighting function that uses the sun's shadow term
/// (<c>atten</c>) TWICE — once to shade the blade, and once as the gate
/// deciding how lamps and torches light it (<c>dayFactor × atten</c>, tuned
/// over days). A cookie multiplies straight into <c>atten</c>, so it did not
/// merely darken the grass, it corrupted the lamp system. Sam: "all of the
/// grass looks dark and messed up now."
///
/// The cookie is gone. These clouds cast ORDINARY shadows through the ordinary
/// shadow map, which is both correct and self-limiting: <c>atten</c> only drops
/// where a cloud actually is, exactly as it already does under a tree — a case
/// the grass shader was written for.
/// </summary>
[DefaultExecutionOrder(250)]
public class PlanetClouds : MonoBehaviour
{
    public static PlanetClouds Instance { get; private set; }

    // CLAUDE.md trap #1: skips MainMenu, so it must also be seeded in
    // MainMenuController.EnsureGameplaySingletons or there are no clouds in a
    // build even though the Editor looks right.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PlanetClouds");
        DontDestroyOnLoad(go);
        go.AddComponent<PlanetClouds>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        _quiet = FindObjectOfType<GallerySceneQuiet>() != null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ReleaseAssets();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _planet = null;
        _planetT = null;
        _reported = false;
        _nextScan = 0f;
        KillAll();
        _quiet = scene.name != "MainMenu" && FindObjectOfType<GallerySceneQuiet>() != null;
    }

    void ReleaseAssets()
    {
        if (_shapes != null)
            for (int i = 0; i < _shapes.Length; i++)
                if (_shapes[i] != null) Destroy(_shapes[i]);
        _shapes = null;
        if (_material != null) Destroy(_material);
        _material = null;
    }

    // ── a cloud ──────────────────────────────────────────────────────────────

    struct Cloud
    {
        public bool alive;
        public Vector3 posL;      // planet-local centre
        public Quaternion rotL;   // planet-local orientation, upright on the sphere
        public float size;        // world size, metres across
        public float grow;        // 0..1 scale-in, so nothing snaps into existence
        public int shape;         // which generated blob
    }

    Cloud[] _clouds;
    Mesh[] _shapes;
    Material _material;
    Matrix4x4[][] _batch;
    int[] _batchCount;

    CelestialBody _planet;
    Transform _planetT;
    float _bodyScale;
    float _nextScan;
    bool _reported;
    bool _quiet;

    Camera _cam;
    float _nextCamSearch;
    InputSettings _input;
    float _nextInputSearch;

    static readonly int _CentreID = Shader.PropertyToID("_CloudPlanetCentre");

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

    bool CloudsWanted
    {
        get
        {
            if (_quiet || !enableClouds) return false;
            if (_input == null && Time.time >= _nextInputSearch)
            {
                _nextInputSearch = Time.time + 1f;
                _input = FindObjectOfType<InputSettings>();
            }
            return _input == null || _input.fxClouds;   // no settings yet: show them
        }
    }

    // ── frame ────────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (!CloudsWanted) return;
        var cam = Cam;
        if (cam == null) return;
        if (!EnsureAssets()) return;

        Vector3 camW = cam.transform.position;
        if (Time.time >= _nextScan) { _nextScan = Time.time + 2f; ResolvePlanet(camW); }
        if (_planetT == null || _bodyScale <= 0.01f) return;

        Vector3 camL = _planetT.InverseTransformPoint(camW);
        float camR = camL.magnitude;
        if (camR < 1e-3f) return;

        float shellR = _bodyScale * (1f + cloudAltitude);

        // Clouds are a NEAR-PLANET effect: the ring is centred on wherever you
        // are, projected down onto the layer, which stops making sense once the
        // planet is a ball in the distance rather than a floor under you.
        //
        // (An earlier worry that opaque clouds would spoil the atmosphere seen
        // from orbit does NOT hold up: they sit ~30 m above a 300 m planet, so
        // from any distance where you can see the whole planet they move the
        // scattering endpoint by well under one percent.)
        if (camR > shellR + visibleAbove) { KillAll(); return; }

        if (_clouds == null || _clouds.Length != maxClouds) _clouds = new Cloud[maxClouds];
        for (int i = 0; i < _batchCount.Length; i++) _batchCount[i] = 0;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        // Wind: a slow rotation about one axis, shared by the whole layer, so
        // the sky moves as one weather system rather than as loose confetti.
        Vector3 up = camL.normalized;
        Vector3 windAxis = Vector3.Cross(up, Vector3.up);
        if (windAxis.sqrMagnitude < 1e-4f) windAxis = Vector3.Cross(up, Vector3.right);
        windAxis.Normalize();

        int spawnBudget = 2;
        for (int i = 0; i < _clouds.Length; i++)
        {
            if (!_clouds[i].alive)
            {
                if (spawnBudget <= 0) continue;
                Spawn(ref _clouds[i], camL, shellR);
                spawnBudget--;
                continue;
            }
            Step(ref _clouds[i], camL, windAxis, dt);
            if (_clouds[i].alive) Accumulate(ref _clouds[i]);
        }

        _material.SetVector(_CentreID, _planetT.position);
        Draw();
    }

    void KillAll()
    {
        if (_clouds == null) return;
        for (int i = 0; i < _clouds.Length; i++) _clouds[i].alive = false;
    }

    void ResolvePlanet(Vector3 camW)
    {
        var bodies = NBodySimulation.Bodies;   // null-safe off the solar scene
        CelestialBody best = null;
        float bestD = float.MaxValue, bestScale = 0f;
        int withAir = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            var gen = b.GetComponentInChildren<CelestialBodyGenerator>();
            if (gen == null || !HasAtmosphere(gen)) continue;
            withAir++;
            float scale;
            try { scale = gen.BodyScale; } catch { continue; }
            if (scale <= 0.01f) continue;
            float d = (b.transform.position - camW).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; bestScale = scale; }
        }

        if (best != _planet)
        {
            _planet = best;
            _planetT = best != null ? best.transform : null;
            KillAll();
        }
        _bodyScale = bestScale;

        if (!_reported)
        {
            _reported = true;
            Debug.Log($"[PlanetClouds] {bodies.Length} bodies, {withAir} with an atmosphere; "
                    + (best != null
                        ? $"clouds on '{best.bodyName}' (radius {bestScale:F0} m, layer "
                          + $"{bestScale * cloudAltitude:F0} m up)."
                        : "no cloudy planet nearby."));
        }
    }

    /// <summary>Read-only inspection of the generator's settings — allowed in
    /// the forbidden zone, unlike modification.</summary>
    static bool HasAtmosphere(CelestialBodyGenerator gen)
    {
        try
        {
            return gen.body != null && gen.body.shading != null
                && gen.body.shading.atmosphereSettings != null;
        }
        catch { return false; }
    }

    // ── movement ─────────────────────────────────────────────────────────────

    void Spawn(ref Cloud c, Vector3 camL, float shellR)
    {
        Vector3 up = camL.normalized;
        Vector3 t1 = Vector3.Cross(up, Vector3.up);
        if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(up, Vector3.right);
        t1.Normalize();
        Vector3 t2 = Vector3.Cross(up, t1);

        // sqrt for a distribution that is uniform in AREA — a straight random
        // radius crowds a disc's middle, the same mistake the fish field made.
        // Weighted outward so new clouds arrive far off, where growing in is
        // invisible.
        float d = ring * Mathf.Sqrt(Random.Range(0.35f, 1f));
        float a = Random.value * Mathf.PI * 2f;
        Vector3 dir = (up * shellR + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * d).normalized;

        float jitter = Random.Range(-layerThickness, layerThickness);
        c.posL = dir * (shellR + jitter);
        // Upright on the sphere, spun randomly about its own vertical so a
        // handful of shapes never reads as repeats.
        c.rotL = Quaternion.AngleAxis(Random.value * 360f, dir)
               * Quaternion.FromToRotation(Vector3.up, dir);
        c.size = Random.Range(sizeMin, sizeMax);
        c.grow = 0f;
        c.shape = Random.Range(0, _shapes.Length);
        c.alive = true;
    }

    void Step(ref Cloud c, Vector3 camL, Vector3 windAxis, float dt)
    {
        // Drift as a rotation about the planet, so the layer stays on its shell
        // however far it travels.
        Quaternion spin = Quaternion.AngleAxis(windSpeed * dt, windAxis);
        c.posL = spin * c.posL;
        c.rotL = spin * c.rotL;

        c.grow = Mathf.Min(1f, c.grow + dt / Mathf.Max(0.1f, growSeconds));

        float cull = ring * 1.35f;
        if ((c.posL - camL).sqrMagnitude > cull * cull) c.alive = false;
    }

    void Accumulate(ref Cloud c)
    {
        int s = c.shape;
        if (s < 0 || s >= _batchCount.Length) return;
        int n = _batchCount[s];
        if (n >= _batch[s].Length) return;

        Vector3 posW = _planetT.TransformPoint(c.posL);
        Quaternion rotW = _planetT.rotation * c.rotL;
        // Grow in rather than pop in. At the distance they arrive this is
        // imperceptible, and it costs one multiply.
        float k = c.size * Mathf.SmoothStep(0.15f, 1f, c.grow);
        _batch[s][n] = Matrix4x4.TRS(posW, rotW, new Vector3(k, k * flatten, k));
        _batchCount[s] = n + 1;
    }

    void Draw()
    {
        for (int s = 0; s < _shapes.Length; s++)
        {
            int n = _batchCount[s];
            if (n == 0 || _shapes[s] == null) continue;
            // Shadows ON: this is the whole point of using real geometry. The
            // shadow is cast by the actual cloud through the ordinary shadow
            // map, so it lands exactly where the cloud is — unlike the sun
            // cookie in version one, which multiplied the light everywhere and
            // broke the grass.
            Graphics.DrawMeshInstanced(_shapes[s], 0, _material, _batch[s], n, null,
                                       ShadowCastingMode.On, true, 0, null,
                                       LightProbeUsage.Off);
        }
    }

    // ── the cloud shapes ─────────────────────────────────────────────────────

    bool EnsureAssets()
    {
        if (_material == null)
        {
            // A REAL MATERIAL ASSET IN RESOURCES, not Shader.Find. A shader
            // referenced only from code is referenced by no asset, so the build
            // strips it and Shader.Find returns null in the player while working
            // in the Editor — which is exactly why the first build had no clouds
            // at all. SpaceDustField documents the same trap.
            var baseMat = Resources.Load<Material>("PlanetClouds");
            if (baseMat == null)
            {
                var sh = Shader.Find("Custom/PlanetClouds");
                if (sh == null)
                {
                    Debug.LogWarning("[PlanetClouds] Resources/PlanetClouds.mat is missing AND "
                                   + "Custom/PlanetClouds could not be found — no clouds.");
                    return false;
                }
                Debug.LogWarning("[PlanetClouds] Resources/PlanetClouds.mat missing — using a "
                               + "runtime material. Works in the Editor, draws NOTHING in a build.");
                _material = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            else _material = new Material(baseMat) { hideFlags = HideFlags.HideAndDontSave };
            _material.enableInstancing = true;
        }

        int want = Mathf.Clamp(shapeVariants, 1, 12);
        if (_shapes == null || _shapes.Length != want)
        {
            ReleaseShapes();
            _shapes = new Mesh[want];
            _batch = new Matrix4x4[want][];
            _batchCount = new int[want];
            for (int i = 0; i < want; i++)
            {
                _shapes[i] = BuildCloudMesh(i * 7919 + 13);
                _batch[i] = new Matrix4x4[Mathf.Max(1, maxClouds)];
            }
        }
        return true;
    }

    void ReleaseShapes()
    {
        if (_shapes == null) return;
        for (int i = 0; i < _shapes.Length; i++)
            if (_shapes[i] != null) Destroy(_shapes[i]);
        _shapes = null;
    }

    /// <summary>
    /// One cloud: a handful of low-poly spheres merged into a flat-bottomed
    /// blob. Deliberately NOT one smooth surface — the lumpy intersections
    /// between spheres ARE the cauliflower silhouette of a cumulus, and they
    /// come free with the geometry.
    ///
    /// Two rules keep it from looking like a bag of marbles. Puffs get smaller
    /// the further they sit from the middle, so the shape has a core and a
    /// crumbling edge rather than being uniform. And none of them may hang below
    /// the base, because a real cumulus has a hard flat bottom where the rising
    /// air hits its condensation level, and a billowing top — that contrast is
    /// most of what makes a shape read as "cloud" instead of "rock".
    /// </summary>
    static Mesh BuildCloudMesh(int seed)
    {
        var rnd = new System.Random(seed);
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        int puffs = 7 + (int)(rnd.NextDouble() * 5);   // 7..11
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        for (int p = 0; p < puffs; p++)
        {
            // The first puff is the core; the rest ring it and climb a little.
            float t = p == 0 ? 0f : Rand(0.25f, 1f);
            float ang = Rand(0f, Mathf.PI * 2f);
            Vector3 centre = new Vector3(Mathf.Cos(ang) * t * 0.85f,
                                         Rand(0f, 0.55f) * (1f - t * 0.5f),
                                         Mathf.Sin(ang) * t * 0.85f);
            // Smaller toward the edges — this is what stops it reading as a pile
            // of equal balls.
            float r = Mathf.Lerp(0.55f, 0.24f, t) * Rand(0.85f, 1.15f);
            centre.y = Mathf.Max(centre.y, r * 0.35f);   // the flat base
            AppendSphere(verts, norms, tris, centre, r, 2);
        }

        // NORMALISE: the raw blob spans roughly 2.8 units across, so without
        // this "size in metres" would build clouds about three times the
        // requested width. Rescale so the mesh is exactly 1 unit wide, recentre
        // it horizontally, and drop its flat base onto y = 0 so a cloud placed
        // at the layer radius SITS on the layer.
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue;
        for (int i = 0; i < verts.Count; i++)
        {
            var w = verts[i];
            if (w.x < minX) minX = w.x; if (w.x > maxX) maxX = w.x;
            if (w.z < minZ) minZ = w.z; if (w.z > maxZ) maxZ = w.z;
            if (w.y < minY) minY = w.y;
        }
        float width = Mathf.Max(maxX - minX, maxZ - minZ);
        if (width < 1e-4f) width = 1f;
        Vector3 centreXZ = new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
        for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - centreXZ) / width;

        var m = new Mesh { name = "Cloud" + seed, indexFormat = IndexFormat.UInt32 };
        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A subdivided octahedron. Normals are analytic — the direction
    /// from that puff's own centre — so every puff stays perfectly round and the
    /// creases where two puffs meet stay crisp, which is what reads as billowing
    /// rather than as one melted lump.</summary>
    static void AppendSphere(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                             Vector3 centre, float radius, int subdiv)
    {
        Vector3[] p =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };
        int[][] faces =
        {
            new[]{0,4,3}, new[]{0,3,5}, new[]{0,5,2}, new[]{0,2,4},
            new[]{1,3,4}, new[]{1,5,3}, new[]{1,2,5}, new[]{1,4,2}
        };
        for (int f = 0; f < faces.Length; f++)
            Subdivide(verts, norms, tris, centre, radius, subdiv,
                      p[faces[f][0]], p[faces[f][1]], p[faces[f][2]]);
    }

    static void Subdivide(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                          Vector3 centre, float radius, int depth,
                          Vector3 a, Vector3 b, Vector3 c)
    {
        if (depth <= 0)
        {
            int i0 = verts.Count;
            verts.Add(centre + a * radius); norms.Add(a);
            verts.Add(centre + b * radius); norms.Add(b);
            verts.Add(centre + c * radius); norms.Add(c);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            return;
        }
        Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
        Subdivide(verts, norms, tris, centre, radius, depth - 1, a, ab, ca);
        Subdivide(verts, norms, tris, centre, radius, depth - 1, ab, b, bc);
        Subdivide(verts, norms, tris, centre, radius, depth - 1, ca, bc, c);
        Subdivide(verts, norms, tris, centre, radius, depth - 1, ab, bc, ca);
    }

    // ================= tuning (appended at END per conventions) =================

    [Header("Clouds")]
    [Tooltip("Master switch, independent of the player's settings toggle.")]
    [SerializeField] bool enableClouds = true;
    [Tooltip("How many clouds exist around you at once.")]
    [SerializeField] int maxClouds = 26;
    [Tooltip("How far out clouds are kept, in metres.")]
    [SerializeField] float ring = 500f;
    [Tooltip("Height of the cloud layer as a fraction of the planet's radius. 0.10 on a 300 m planet is about 30 m up — low enough to fly through.")]
    [SerializeField] float cloudAltitude = 0.10f;
    [Tooltip("Metres of random height variation within the layer, so it is not a perfect ceiling.")]
    [SerializeField] float layerThickness = 8f;
    [Tooltip("Metres above the cloud layer past which clouds stop being drawn. They are a near-planet effect; you keep them all the way up through the layer and well beyond it.")]
    [SerializeField] float visibleAbove = 400f;

    [Header("Shape")]
    [Tooltip("Smallest cloud, in metres across.")]
    [SerializeField] float sizeMin = 22f;
    [Tooltip("Largest cloud, in metres across.")]
    [SerializeField] float sizeMax = 55f;
    [Tooltip("Vertical squash. Below 1 gives wide, flat-bottomed clouds rather than balls.")]
    [SerializeField] float flatten = 0.55f;
    [Tooltip("How many different cloud shapes are generated. Each is one draw call.")]
    [SerializeField] int shapeVariants = 5;

    [Header("Movement")]
    [Tooltip("Degrees per second the whole layer drifts around the planet.")]
    [SerializeField] float windSpeed = 0.15f;
    [Tooltip("Seconds a new cloud takes to grow to full size, so none of them pop in.")]
    [SerializeField] float growSeconds = 2.5f;
}
