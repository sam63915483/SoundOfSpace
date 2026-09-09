using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Puffy 3D clouds over planets that have an atmosphere. Purely visual,
/// toggleable in the pause menu.
///
/// ── How this got here (Sam, 2026-09-09) ───────────────────────────────────
/// v1 was a texture painted on a sphere shell: "the clouds are flat ... i would
/// not use them in my game." A picture on a sphere has no parallax and no
/// volume, and no tuning fixes that.
///
/// v2 made each cloud a cluster of low-poly spheres — real geometry, real
/// parallax. "Those are better", but "completely solid looking ... should be
/// see through and fluffy", too low, and "if i open the map then close the map
/// the clouds disappear and appear in different formations."
///
/// v3 keeps the geometry and fixes the three real faults:
///
///   • <b>SOFT, not solid.</b> The shading is where a cloud stops being an
///     object and becomes a volume, so the work moved into the shader: alpha
///     falls away at the rim of every puff, and the 9-14 overlapping puffs
///     accumulate without depth writing, so a cloud is sheer at its fringes and
///     dense in its middle. See PlanetClouds.shader.
///
///   • <b>THE MAP BUG, which was two mistakes.</b> The field followed
///     <c>Camera.main</c>, and the solar map flies the REAL camera far above the
///     planet — so the altitude cut-off read that as "left the planet" and
///     deleted every cloud. Worse, cloud positions were random and stored, so
///     rebuilding gave a different sky. Both are gone: the field now follows the
///     PLAYER, and cloud placement is DETERMINISTIC.
///
///   • <b>Twice as high</b>, at Sam's request.
///
/// ── Deterministic placement ───────────────────────────────────────────────
/// There is no cloud pool and no per-cloud state at all. The sky is a fixed
/// lattice of cells; a hash of a cell's integer coordinates decides whether it
/// holds a cloud and, if so, its offset, size, spin and shape. Wind is one
/// global rotation of the whole lattice, a pure function of time. So the same
/// patch of sky always holds the same clouds — walk away and come back, open
/// and close the map, reload the scene, and the weather is where you left it.
/// Nothing to save, nothing to reshuffle, nothing to lose.
///
/// ── Why this is still cheap ───────────────────────────────────────────────
/// Roughly thirty clouds of ~1,400 triangles, instanced into one draw call per
/// shape. Clouds barely overlap each OTHER, so this is one or two layers of
/// transparency over part of the sky — not the dozens that make billboard cloud
/// systems expensive. Volumetric raymarching was never affordable here.
///
/// ── Do NOT put a cookie on the sun ────────────────────────────────────────
/// v1 drew ground shadows with a cookie on the directional light, and it wrecked
/// the grass. The grass has a hand-written lighting function that uses the sun's
/// shadow term <c>atten</c> TWICE — to shade the blade, and as the gate for how
/// lamps and torches light it — so a cookie, which multiplies straight into
/// <c>atten</c>, silently rewrote tuned lighting everywhere. Clouds cast
/// ordinary shadow-map shadows instead: <c>atten</c> then only drops where a
/// cloud actually is, exactly as it already does under a tree.
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
        if (!FeatureVault.PlanetClouds) return;   // vaulted 2026-09-09, Sam's call
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
        _player = null;
        _cam = null;
        _reported = false;
        _nextScan = 0f;
        _quiet = scene.name != "MainMenu" && FindObjectOfType<GallerySceneQuiet>() != null;
    }

    void ReleaseAssets()
    {
        ReleaseShapes();
        if (_material != null) Destroy(_material);
        _material = null;
    }

    void ReleaseShapes()
    {
        if (_shapes == null) return;
        for (int i = 0; i < _shapes.Length; i++)
            if (_shapes[i] != null) Destroy(_shapes[i]);
        _shapes = null;
    }

    // ── state (there is no cloud state — placement is a pure function) ───────

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

    Transform _player;
    float _nextPlayerSearch;
    Camera _cam;
    float _nextCamSearch;
    InputSettings _input;
    float _nextInputSearch;

    static readonly int _CentreID = Shader.PropertyToID("_CloudPlanetCentre");

    /// <summary>
    /// The field follows the PLAYER, never the camera. The solar map, the
    /// trailer free-cam and every cutscene move the real camera somewhere else
    /// entirely; anchoring to it is what made the clouds vanish when Sam opened
    /// the map. The camera is only a fallback for scenes with no player.
    /// </summary>
    Vector3 AnchorPosition(out bool ok)
    {
        if (_player == null && Time.time >= _nextPlayerSearch)
        {
            _nextPlayerSearch = Time.time + 1f;
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) _player = pc.transform;
        }
        if (_player != null) { ok = true; return _player.position; }

        if (_cam == null && Time.time >= _nextCamSearch)
        {
            _nextCamSearch = Time.time + 0.5f;
            _cam = Camera.main;
        }
        if (_cam != null) { ok = true; return _cam.transform.position; }
        ok = false;
        return Vector3.zero;
    }

    bool CloudsWanted
    {
        get
        {
            // Belt and braces: the vault also stops a hand-placed component in a
            // scene from drawing anything.
            if (!FeatureVault.PlanetClouds) return false;
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
        if (!EnsureAssets()) return;

        Vector3 anchorW = AnchorPosition(out bool haveAnchor);
        if (!haveAnchor) return;

        if (Time.time >= _nextScan) { _nextScan = Time.time + 2f; ResolvePlanet(anchorW); }
        if (_planetT == null || _bodyScale <= 0.01f) return;

        Vector3 anchorL = _planetT.InverseTransformPoint(anchorW);
        float anchorR = anchorL.magnitude;
        if (anchorR < 1e-3f) return;

        float shellR = _bodyScale * (1f + cloudAltitude);
        // A near-planet effect: the lattice is enumerated around wherever you
        // are on the shell, which stops meaning anything once the planet is a
        // ball in the distance rather than a floor under you.
        if (anchorR > shellR + visibleAbove) return;

        for (int i = 0; i < _batchCount.Length; i++) _batchCount[i] = 0;
        Gather(anchorL, shellR);

        _material.SetVector(_CentreID, _planetT.position);
        Draw();
    }

    void ResolvePlanet(Vector3 anchorW)
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
            float d = (b.transform.position - anchorW).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; bestScale = scale; }
        }

        _planet = best;
        _planetT = best != null ? best.transform : null;
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

    // ── the sky lattice ──────────────────────────────────────────────────────

    /// <summary>
    /// Walk the cells of the sky near the player and emit whatever clouds their
    /// hashes say are there. Nothing is stored between frames, so there is
    /// nothing that can be lost, reshuffled or fall out of sync — which is the
    /// actual fix for clouds changing formation across a map screen.
    /// </summary>
    void Gather(Vector3 anchorL, float shellR)
    {
        // Wind is ONE rotation of the whole lattice about the planet's own axis,
        // and it is a pure function of time — so it is identical on any frame,
        // in any session, and never needs to be remembered.
        float windAngle = Mathf.Repeat(windSpeed * Time.time, 360f);
        Quaternion wind = Quaternion.AngleAxis(windAngle, Vector3.up);
        Quaternion windInv = Quaternion.AngleAxis(-windAngle, Vector3.up);

        // Work in "weather space", where the lattice is fixed.
        Vector3 anchorWeather = windInv * anchorL;

        float cell = Mathf.Max(8f, cellMetres);
        int R = Mathf.Clamp(Mathf.CeilToInt(ring / cell) + 1, 1, 10);
        Vector3 ac = anchorWeather / cell;
        int ax = Mathf.FloorToInt(ac.x), ay = Mathf.FloorToInt(ac.y), az = Mathf.FloorToInt(ac.z);
        float ringSq = ring * ring;
        // Half a cell, not more: a thicker band picks cells at several radii that
        // project to the SAME patch of sky, which measured 448 shell cells where
        // there are only about a hundred places to put a cloud — and stacked them
        // on top of each other.
        float shellBand = cell * 0.5f;

        for (int dx = -R; dx <= R; dx++)
            for (int dy = -R; dy <= R; dy++)
                for (int dz = -R; dz <= R; dz++)
                {
                    int cx = ax + dx, cy = ay + dy, cz = az + dz;
                    Vector3 c = new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f) * cell;
                    float rr = c.magnitude;
                    // Only cells the cloud shell actually passes through.
                    if (rr < 1e-3f || Mathf.Abs(rr - shellR) > shellBand) continue;

                    uint h = Hash3(cx, cy, cz);
                    if (R01(h) > cloudChance) continue;

                    Vector3 dir = c / rr;
                    Vector3 t1 = Vector3.Cross(dir, Vector3.up);
                    if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(dir, Vector3.right);
                    t1.Normalize();
                    Vector3 t2 = Vector3.Cross(dir, t1);

                    h = Next(h); float ju = (R01(h) - 0.5f) * cell * 0.85f;
                    h = Next(h); float jv = (R01(h) - 0.5f) * cell * 0.85f;
                    h = Next(h); float jh = (R01(h) - 0.5f) * 2f * layerThickness;
                    h = Next(h); float size = Mathf.Lerp(sizeMin, sizeMax, R01(h));
                    h = Next(h); float spin = R01(h) * 360f;
                    h = Next(h); int shape = (int)(h % (uint)_shapes.Length);

                    Vector3 posWeather = dir * (shellR + jh) + t1 * ju + t2 * jv;

                    float distSq = (posWeather - anchorWeather).sqrMagnitude;
                    if (distSq > ringSq) continue;

                    // Grow in over the last stretch of the ring rather than
                    // popping. That edge is past the horizon on a planet this
                    // size, so it is never actually seen happening.
                    float fade = Mathf.InverseLerp(ring, ring * 0.78f, Mathf.Sqrt(distSq));
                    float k = size * Mathf.SmoothStep(0f, 1f, fade);
                    if (k < 0.05f) continue;

                    Vector3 posL = wind * posWeather;
                    Vector3 upL = wind * dir;
                    Quaternion rotL = Quaternion.AngleAxis(spin, upL)
                                    * Quaternion.FromToRotation(Vector3.up, upL);

                    Emit(shape, _planetT.TransformPoint(posL), _planetT.rotation * rotL,
                         new Vector3(k, k * flatten, k));
                }
    }

    void Emit(int shape, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (shape < 0 || shape >= _batchCount.Length) return;
        int n = _batchCount[shape];
        if (n >= _batch[shape].Length) return;
        _batch[shape][n] = Matrix4x4.TRS(pos, rot, scale);
        _batchCount[shape] = n + 1;
    }

    void Draw()
    {
        for (int s = 0; s < _shapes.Length; s++)
        {
            int n = _batchCount[s];
            if (n == 0 || _shapes[s] == null) continue;
            // Shadows ON: the shadow is cast by the cloud's own geometry through
            // the ordinary shadow map, so it lands exactly where the cloud is.
            // This is the safe way to shade the ground — unlike a sun cookie,
            // which multiplies the light everywhere and broke the grass.
            Graphics.DrawMeshInstanced(_shapes[s], 0, _material, _batch[s], n, null,
                                       ShadowCastingMode.On, false, 0, null,
                                       LightProbeUsage.Off);
        }
    }

    // ── hashing ──────────────────────────────────────────────────────────────

    static uint Hash3(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return h;
        }
    }

    static uint Next(uint h)
    {
        unchecked { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return h; }
    }

    static float R01(uint h) => (h & 0xFFFFFF) / (float)0x1000000;

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
                _batch[i] = new Matrix4x4[Mathf.Max(8, batchCapacity)];
            }
        }
        return true;
    }

    /// <summary>
    /// One cloud: 9-14 low-poly spheres merged into a flat-bottomed blob.
    /// Deliberately NOT one smooth surface — the lumpy intersections between
    /// spheres ARE the cauliflower silhouette of a cumulus, and the shader turns
    /// their overlaps into density.
    ///
    /// Three rules keep it from looking like a bag of marbles. Puffs shrink the
    /// further they sit from the middle, so a cloud has a core and a crumbling
    /// edge. None may hang below the base, because a real cumulus has a hard
    /// flat bottom where rising air hits its condensation level and a billowing
    /// top — that contrast is most of what reads as "cloud" instead of "rock".
    /// And the whole thing is stretched along one axis, because clouds are
    /// almost never as deep as they are wide.
    /// </summary>
    static Mesh BuildCloudMesh(int seed)
    {
        var rnd = new System.Random(seed);
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        int puffs = 9 + (int)(rnd.NextDouble() * 6);   // 9..14
        float stretch = Rand(1.15f, 1.75f);            // longer than it is deep
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();

        for (int p = 0; p < puffs; p++)
        {
            float t = p == 0 ? 0f : Rand(0.2f, 1f);
            float ang = Rand(0f, Mathf.PI * 2f);
            Vector3 centre = new Vector3(Mathf.Cos(ang) * t * 0.9f * stretch,
                                         Rand(0f, 0.6f) * (1f - t * 0.45f),
                                         Mathf.Sin(ang) * t * 0.9f);
            float r = Mathf.Lerp(0.58f, 0.20f, t) * Rand(0.8f, 1.2f);
            centre.y = Mathf.Max(centre.y, r * 0.3f);   // the flat base
            AppendSphere(verts, norms, tris, centre, r, 2);
        }

        // NORMALISE: the raw blob spans several units, so without this "size in
        // metres" would build clouds far larger than asked for. Rescale to
        // exactly 1 unit wide, recentre horizontally, and drop the flat base to
        // y = 0 so a cloud placed at the layer radius SITS on the layer.
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
        Vector3 origin = new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
        for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - origin) / width;

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
    [Tooltip("Height of the cloud layer as a fraction of the planet's radius. 0.20 on a 300 m planet is about 60 m up.")]
    [SerializeField] float cloudAltitude = 0.20f;
    [Tooltip("How far out clouds are drawn, in metres. On a 300 m planet the layer drops below the horizon at about 225 m, so there is nothing to gain past roughly this.")]
    [SerializeField] float ring = 280f;
    [Tooltip("Metres of random height variation within the layer, so it is not a flat ceiling.")]
    [SerializeField] float layerThickness = 14f;
    [Tooltip("Metres above the layer past which clouds stop being drawn.")]
    [SerializeField] float visibleAbove = 400f;

    [Header("How many")]
    [Tooltip("Size of one cell of sky, in metres. One cell holds at most one cloud.")]
    [SerializeField] float cellMetres = 70f;
    [Tooltip("Chance that any given cell of sky holds a cloud. THE OVERCAST DIAL: 0 is a clear sky, 1 is packed. 0.55 measures at roughly 22-30 clouds in view.")]
    [SerializeField] float cloudChance = 0.55f;
    [Tooltip("Most clouds of any ONE shape that can be drawn at once. Only a safety cap.")]
    [SerializeField] int batchCapacity = 64;

    [Header("Shape")]
    [Tooltip("Smallest cloud, in metres across.")]
    [SerializeField] float sizeMin = 26f;
    [Tooltip("Largest cloud, in metres across.")]
    [SerializeField] float sizeMax = 70f;
    [Tooltip("Vertical squash. Below 1 gives wide, flat-bottomed clouds rather than balls.")]
    [SerializeField] float flatten = 0.5f;
    [Tooltip("How many different cloud shapes are generated. Each is one draw call.")]
    [SerializeField] int shapeVariants = 5;

    [Header("Movement")]
    [Tooltip("Degrees per second the whole sky drifts around the planet. It is a pure function of time, so it never jumps.")]
    [SerializeField] float windSpeed = 0.12f;
}
