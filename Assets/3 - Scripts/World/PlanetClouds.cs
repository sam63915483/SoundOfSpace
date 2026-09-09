using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Clouds on every planet that has an atmosphere (Sam, 2026-09-09), plus their
/// soft shadows on the ground. Purely visual, toggleable in the pause menu.
///
/// ── Two pieces, both nearly free ──────────────────────────────────────────
///
/// <b>1. The clouds are ONE SPHERE SHELL per planet.</b> Not billboards: dozens
/// of overlapping see-through puffs is the classic framerate killer, because
/// every layer shades every pixel behind it again. A shell is one draw call and
/// one layer of transparency over the sky part of the screen. Shape comes from
/// a tiling noise texture sampled triplanar (no UV seams, no pinched poles), so
/// the mesh itself can stay coarse.
///
/// <b>2. The shadows are a SUN COOKIE.</b> A directional light can carry a
/// greyscale texture that multiplies its light across the whole world, and this
/// project's sun does not use one — the slot was empty. The planet's terrain
/// shaders are surface shaders (<c>#pragma surface surf Standard</c>), so they
/// take Unity's standard light attenuation and therefore the cookie, for free
/// and WITHOUT touching the forbidden Celestial/ zone. Because a cookie
/// multiplies rather than replaces, "not fully dark" is just a number:
/// <see cref="shadowDarkness"/> is the fraction of light the thickest cloud
/// removes, and at 0.45 the light and fluffy clouds Sam asked for still let
/// most of the sun through.
///
/// <b>The honest limit on the shadows:</b> the cookie is a cloud-LIKE pattern
/// drifting in step with the layer, not a true projection of the shell. Making
/// it exact would mean rendering the shell from the sun's point of view every
/// frame — a real shadow map — which costs enormously more than this whole
/// feature. For soft, partial, drifting cloud shadows, matching character is
/// what reads; matching geometry is not worth the frame time.
///
/// ── Why the clouds tint themselves ────────────────────────────────────────
/// This game has no skybox: the blue sky is a full-screen post-effect, and it
/// only runs on OPAQUE geometry. A transparent shell drawn afterwards never
/// receives it. So the shader fakes the sky tint itself. That is a deliberate
/// choice to stay entirely outside the forbidden zone.
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
        RestoreSunCookie();
        Teardown();
        if (_noiseTex != null) Destroy(_noiseTex);
        if (_cookieSrc != null) Destroy(_cookieSrc);
        if (_cookieRT != null) { _cookieRT.Release(); Destroy(_cookieRT); }
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The shells are children of CelestialBodies, so a scene load destroys
        // them along with the bodies; the cached list would be full of dead
        // references. Rebuild from scratch, as SpaceDustField does.
        RestoreSunCookie();
        Teardown();
        _quiet = scene.name != "MainMenu" && FindObjectOfType<GallerySceneQuiet>() != null;
        _nextScan = 0f;
        _reported = false;
    }

    void Teardown()
    {
        for (int i = 0; i < _shells.Count; i++)
            if (_shells[i] != null) Destroy(_shells[i].gameObject);
        _shells.Clear();
        _shellBodies.Clear();
        _shellScale.Clear();
    }

    // ── state ────────────────────────────────────────────────────────────────

    readonly List<Transform> _shells = new List<Transform>();
    readonly List<CelestialBody> _shellBodies = new List<CelestialBody>();
    // Cached at creation: the shadow check runs every frame and must not walk a
    // planet's hierarchy to re-ask for its radius (CLAUDE.md — never
    // GetComponentInChildren in an update loop).
    readonly List<float> _shellScale = new List<float>();

    Mesh _mesh;
    Material _material;
    Texture2D _noiseTex;      // raw fBm — the cloud shape
    Texture2D _cookieSrc;     // the same fBm turned into a light multiplier
    RenderTexture _cookieRT;  // _cookieSrc, scrolling, handed to the sun

    Light _sun;
    Texture _sunCookieBefore;
    float _sunCookieSizeBefore;
    bool _cookieInstalled;

    Camera _cam;
    float _nextCamSearch;
    float _nextScan;
    float _nextSunSearch;
    bool _quiet;

    InputSettings _input;
    float _nextInputSearch;

    static readonly int _CloudSunDirID = Shader.PropertyToID("_CloudSunDir");

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
        bool wanted = CloudsWanted;
        if (!wanted)
        {
            if (_shells.Count > 0) Teardown();
            RestoreSunCookie();
            return;
        }

        var cam = Cam;
        if (cam == null) return;

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 3f;
            RefreshShells();
        }

        // The sun the ocean effect uses, so clouds and water agree on where the
        // light is coming from.
        if (_sun == null && Time.time >= _nextSunSearch)
        {
            // Throttled: never a FindObjectOfType every frame just because the
            // sun has not appeared yet (the LightLookAt rule).
            _nextSunSearch = Time.time + 1f;
            var caster = FindObjectOfType<SunShadowCaster>();
            if (caster != null) _sun = caster.GetComponent<Light>();
        }
        if (_material != null && _sun != null)
            _material.SetVector(_CloudSunDirID, -_sun.transform.forward);

        UpdateSunCookie(cam.transform.position);
    }

    // ── the shells ───────────────────────────────────────────────────────────

    void RefreshShells()
    {
        // Drop shells whose planet went away.
        for (int i = _shells.Count - 1; i >= 0; i--)
            if (_shells[i] == null || _shellBodies[i] == null)
            {
                if (_shells[i] != null) Destroy(_shells[i].gameObject);
                _shells.RemoveAt(i);
                _shellBodies.RemoveAt(i);
                _shellScale.RemoveAt(i);
            }

        var bodies = NBodySimulation.Bodies;   // null-safe off the solar scene
        int withAir = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            var gen = b.GetComponentInChildren<CelestialBodyGenerator>();
            if (gen == null) continue;
            if (!HasAtmosphere(gen)) continue;
            withAir++;
            if (_shellBodies.Contains(b)) continue;
            CreateShell(b, gen);
        }

        // One line in Player.log that settles "why are there no clouds" without
        // another build: how many bodies have air, and how many actually got a
        // shell. They should match.
        if (!_reported)
        {
            _reported = true;
            Debug.Log($"[PlanetClouds] {bodies.Length} bodies, {withAir} with an atmosphere, "
                    + $"{_shells.Count} cloud shells built.");
        }
    }

    bool _reported;

    /// <summary>Read-only inspection of the generator's settings — allowed in
    /// the forbidden zone, unlike modification. A body with no atmosphere
    /// settings is airless and gets no clouds.</summary>
    static bool HasAtmosphere(CelestialBodyGenerator gen)
    {
        try
        {
            return gen.body != null
                && gen.body.shading != null
                && gen.body.shading.atmosphereSettings != null;
        }
        catch { return false; }
    }

    void CreateShell(CelestialBody body, CelestialBodyGenerator gen)
    {
        if (!EnsureAssets()) return;

        float bodyScale;
        try { bodyScale = gen.BodyScale; } catch { return; }
        if (bodyScale <= 0.01f) return;

        var go = new GameObject("CloudShell_" + body.bodyName);
        // Child of the planet, so it rides the orbit and any spin for free —
        // and, being under a body EndlessManager already shifts, it must NOT be
        // registered itself or it would be double-shifted (the bug that hit the
        // concert crowd, the menu-orbit player and the bobber).
        go.transform.SetParent(body.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * (bodyScale * (1f + cloudAltitude));
        go.layer = gameObject.layer;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = _mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        _shells.Add(go.transform);
        _shellBodies.Add(body);
        _shellScale.Add(bodyScale);
    }

    bool EnsureAssets()
    {
        if (_mesh == null) _mesh = BuildCubeSphere(shellResolution);
        if (_noiseTex == null) BuildNoise();
        if (_material == null)
        {
            // A REAL MATERIAL ASSET IN RESOURCES, not Shader.Find. This is the
            // bug that made the first build cloudless (Sam: "I just built and
            // ran it and didn't see any clouds"): a shader referenced ONLY from
            // code is not referenced by any asset, so the build strips it and
            // Shader.Find returns null in the player while working perfectly in
            // the Editor. SpaceDustField documents this exact trap and the fish
            // material already followed it — the clouds did not.
            var baseMat = Resources.Load<Material>("PlanetClouds");
            if (baseMat == null)
            {
                var sh = Shader.Find("Custom/PlanetClouds");
                if (sh == null)
                {
                    Debug.LogWarning("[PlanetClouds] Resources/PlanetClouds.mat is missing AND "
                                   + "Custom/PlanetClouds could not be found — no clouds. "
                                   + "Restore the material asset.");
                    return false;
                }
                Debug.LogWarning("[PlanetClouds] Resources/PlanetClouds.mat missing — using a "
                               + "runtime material. This works in the Editor and will draw "
                               + "NOTHING in a build.");
                _material = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            else _material = new Material(baseMat) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetTexture("_NoiseTex", _noiseTex);
            _material.SetColor("_SunColor", sunlitColour);
            _material.SetColor("_ShadowColor", shadedColour);
            _material.SetColor("_SkyTint", skyTint);
            _material.SetFloat("_SkyTintAmount", skyTintAmount);
            _material.SetFloat("_Coverage", coverage);
            _material.SetFloat("_Softness", edgeSoftness);
            _material.SetFloat("_Opacity", opacity);
            _material.SetFloat("_Scale", noiseScale);
            _material.SetFloat("_DriftSpeed", driftSpeed);
            _material.SetFloat("_NearFade", nearFade);
        }
        return _mesh != null && _material != null;
    }

    /// <summary>
    /// A cube projected onto a sphere. A UV sphere would bunch its triangles at
    /// the poles and, more to the point, its UVs pinch there — the shader
    /// sidesteps UVs entirely by sampling triplanar, and this keeps the
    /// triangles evenly sized so the silhouette stays smooth up close.
    /// </summary>
    static Mesh BuildCubeSphere(int n)
    {
        n = Mathf.Clamp(n, 4, 64);
        var dirs = new[] { Vector3.up, Vector3.down, Vector3.left,
                           Vector3.right, Vector3.forward, Vector3.back };
        int perFace = n * n;
        var verts = new Vector3[perFace * 6];
        var norms = new Vector3[perFace * 6];
        var tris = new int[(n - 1) * (n - 1) * 6 * 6];
        int v = 0, t = 0;

        for (int f = 0; f < 6; f++)
        {
            Vector3 up = dirs[f];
            Vector3 axisA = new Vector3(up.y, up.z, up.x);
            Vector3 axisB = Vector3.Cross(up, axisA);
            int start = v;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 pct = new Vector2(x, y) / (n - 1);
                    Vector3 p = up + (pct.x - 0.5f) * 2f * axisA + (pct.y - 0.5f) * 2f * axisB;
                    Vector3 s = p.normalized;
                    verts[v] = s;             // UNIT RADIUS, so localScale IS the shell radius
                    norms[v] = s;
                    if (x != n - 1 && y != n - 1)
                    {
                        int i = start + y * n + x;
                        tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + n;
                        tris[t++] = i; tris[t++] = i + 1; tris[t++] = i + n + 1;
                    }
                    v++;
                }
        }

        var m = new Mesh { name = "CloudShell", indexFormat = IndexFormat.UInt32 };
        m.vertices = verts;
        m.normals = norms;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    // ── noise ────────────────────────────────────────────────────────────────
    //
    // Generated once at runtime rather than shipped as an asset: it is a few
    // milliseconds of CPU on the first planet and it keeps the whole feature to
    // two source files with nothing to import or lose.

    void BuildNoise()
    {
        const int S = 256;
        _noiseTex = new Texture2D(S, S, TextureFormat.R8, true)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "CloudNoise" };
        _cookieSrc = new Texture2D(S, S, TextureFormat.R8, true)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "CloudCookieSrc" };

        var shape = new Color[S * S];
        var cookie = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / S, w = (float)y / S;
                float n = Fbm(u, w, 4, 4);
                shape[y * S + x] = new Color(n, n, n, 1f);

                // The SAME coverage carve the shader does, so the shadow pattern
                // is the cloud pattern rather than an unrelated blotch — then
                // inverted into a light multiplier that never reaches black.
                float a = Mathf.SmoothStep(0f, 1f,
                          Mathf.InverseLerp(coverage, coverage + edgeSoftness, n));
                float mul = Mathf.Lerp(1f, 1f - Mathf.Clamp01(shadowDarkness), a);
                cookie[y * S + x] = new Color(mul, mul, mul, mul);
            }
        _noiseTex.SetPixels(shape); _noiseTex.Apply();
        _cookieSrc.SetPixels(cookie); _cookieSrc.Apply();
    }

    /// <summary>Tiling fractal value noise. Each octave's lattice period equals
    /// its frequency, so every octave wraps exactly once across the texture and
    /// the result tiles seamlessly.</summary>
    static float Fbm(float u, float v, int baseFreq, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int freq = baseFreq;
        for (int o = 0; o < octaves; o++)
        {
            sum += ValueNoise(u * freq, v * freq, freq, o * 977) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2;
        }
        return Mathf.Clamp01(sum / Mathf.Max(norm, 1e-4f));
    }

    static float ValueNoise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float su = xf * xf * (3f - 2f * xf), sv = yf * yf * (3f - 2f * yf);
        float a = Hash(xi, yi, period, seed);
        float b = Hash(xi + 1, yi, period, seed);
        float c = Hash(xi, yi + 1, period, seed);
        float d = Hash(xi + 1, yi + 1, period, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, su), Mathf.Lerp(c, d, su), sv);
    }

    static float Hash(int x, int y, int period, int seed)
    {
        if (period > 0) { x = ((x % period) + period) % period; y = ((y % period) + period) % period; }
        int h = x * 374761393 + y * 668265263 + seed * 144665;
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    // ── the sun cookie ───────────────────────────────────────────────────────

    void UpdateSunCookie(Vector3 camPos)
    {
        if (!groundShadows || _sun == null || _sun.type != LightType.Directional)
        { RestoreSunCookie(); return; }

        // Only near a cloudy planet. A directional cookie is projected across
        // the ENTIRE world, so leaving it on would drape cloud shadows over
        // airless moons and over anything lit in open space.
        bool near = false;
        for (int i = 0; i < _shellBodies.Count; i++)
        {
            var b = _shellBodies[i];
            if (b == null) continue;
            float reach = _shellScale[i] * shadowRange;
            if ((b.transform.position - camPos).sqrMagnitude < reach * reach) { near = true; break; }
        }
        if (!near) { RestoreSunCookie(); return; }

        if (_cookieSrc == null) BuildNoise();
        if (_cookieRT == null)
        {
            _cookieRT = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
              name = "CloudCookie", hideFlags = HideFlags.HideAndDontSave };
            _cookieRT.Create();
        }

        // Scroll the pattern so the shadows drift with the layer. One 256x256
        // blit a frame — far below anything measurable.
        float o = Time.time * shadowDriftSpeed;
        Graphics.Blit(_cookieSrc, _cookieRT, Vector2.one, new Vector2(o, o * 0.6f));

        if (!_cookieInstalled)
        {
            _sunCookieBefore = _sun.cookie;
            _sunCookieSizeBefore = _sun.cookieSize;
            _cookieInstalled = true;
        }
        _sun.cookie = _cookieRT;
        _sun.cookieSize = Mathf.Max(1f, shadowPatchSize);
    }

    void RestoreSunCookie()
    {
        if (!_cookieInstalled || _sun == null) { _cookieInstalled = false; return; }
        _sun.cookie = _sunCookieBefore;
        _sun.cookieSize = _sunCookieSizeBefore;
        _cookieInstalled = false;
    }

    // ================= tuning (appended at END per conventions) =================

    [Header("Clouds")]
    [Tooltip("Master switch, independent of the player's settings toggle.")]
    [SerializeField] bool enableClouds = true;
    [Tooltip("How high the cloud layer sits, as a fraction of the planet's radius. 0.10 on a 300 m planet is about 30 m up — low enough to fly through on the way out.")]
    [SerializeField] float cloudAltitude = 0.10f;
    [Tooltip("Triangles across each face of the shell. The shape comes from the texture, so this only needs to be enough to keep the silhouette round.")]
    [SerializeField] int shellResolution = 24;
    [Tooltip("How much of the sky is cloud. Higher = clearer skies.")]
    [SerializeField] float coverage = 0.52f;
    [Tooltip("How wispy the cloud edges are.")]
    [SerializeField] float edgeSoftness = 0.22f;
    [Tooltip("How solid the clouds are at their thickest.")]
    [SerializeField] float opacity = 0.85f;
    [Tooltip("Size of the cloud shapes. Higher = smaller, busier clouds.")]
    [SerializeField] float noiseScale = 2.5f;
    [Tooltip("How fast the layer drifts.")]
    [SerializeField] float driftSpeed = 0.004f;
    [Tooltip("Metres over which cloud fades out as you fly into it, so passing through is a dissolve rather than a wall.")]
    [SerializeField] float nearFade = 18f;

    [Header("Cloud colour")]
    [SerializeField] Color sunlitColour = new Color(1f, 0.98f, 0.95f, 1f);
    [SerializeField] Color shadedColour = new Color(0.38f, 0.42f, 0.52f, 1f);
    [Tooltip("Stands in for the sky, which is a post-effect and cannot reach a transparent surface.")]
    [SerializeField] Color skyTint = new Color(0.45f, 0.62f, 0.85f, 1f);
    [SerializeField] float skyTintAmount = 0.35f;

    [Header("Ground shadows")]
    [Tooltip("Cloud shadows on the planet, via a cookie on the sun. Costs essentially nothing — it is one texture read inside lighting that already happens.")]
    [SerializeField] bool groundShadows = true;
    [Tooltip("Fraction of the sunlight the thickest cloud removes. 0.45 keeps them light and fluffy; 1 would be pitch black.")]
    [SerializeField] float shadowDarkness = 0.45f;
    [Tooltip("How large one patch of cloud shadow is on the ground, in metres.")]
    [SerializeField] float shadowPatchSize = 120f;
    [Tooltip("How fast the shadows drift across the ground.")]
    [SerializeField] float shadowDriftSpeed = 0.01f;
    [Tooltip("Shadows switch on within this many planet radii of a cloudy world. A directional cookie covers the WHOLE world, so it must not be left on out in space.")]
    [SerializeField] float shadowRange = 2.5f;
}
