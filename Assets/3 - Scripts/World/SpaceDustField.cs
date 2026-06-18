using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Purely-visual glowing amber "space dust" field. Auto-singleton (mirrors SpaceDustInventory).
/// A fixed pool of specks lives in a cube that follows the camera. Each speck is world-fixed
/// (parallax) and wraps to the far side of the cube when it exits, giving an endless local field
/// at constant cost. All motion is derived from camera-position-relative-to-the-black-hole, which
/// is invariant under EndlessManager floating-origin rebases, so origin shifts never cause a jump.
/// Per-speck visibility/brightness comes from a density field (BH proximity, planet avoidance,
/// spiral arms + light noise). One additive instanced billboard material, drawn in <=1023 batches.
///
/// Specks are cleared from the LOWER atmosphere only, so from a planet surface you can still look
/// up and see them streaming toward the black hole above the haze.
/// </summary>
public class SpaceDustField : MonoBehaviour
{
    public static SpaceDustField Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("SpaceDustField");
        DontDestroyOnLoad(go);
        go.AddComponent<SpaceDustField>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_glowTex != null) Destroy(_glowTex);
        if (_material != null) Destroy(_material);
        if (_mesh != null) Destroy(_mesh);
    }

    // ---- runtime refs ----
    Camera _cam;
    CelestialBody _blackHole;
    CelestialBody _homeBody; // planet whose orbital velocity the field co-moves with
    readonly List<CelestialBody> _planets = new List<CelestialBody>();
    readonly Dictionary<CelestialBody, float> _atmoRadius = new Dictionary<CelestialBody, float>();
    InputSettings _input;
    int _inputRefindCooldown;

    // ---- origin-invariant parallax state ----
    bool _hasPrevRel;
    Vector3 _prevCamRelBH;

    // ---- particle pool (local offset from camera) ----
    Vector3[] _local;
    float[] _threshold;
    float[] _sizeRand;
    float[] _phase;

    // ---- render buffers ----
    Matrix4x4[] _matrices;
    Vector4[] _colors;
    MaterialPropertyBlock _mpb;
    Mesh _mesh;
    Material _material;
    Texture2D _glowTex;
    static readonly int _ColorID = Shader.PropertyToID("_Color");
    bool _initialized;

    // ---- per-frame relevant-planet shortlist (perf: avoids dict lookups + far planets in the hot loop) ----
    Vector3[] _relPos;
    float[] _relCull, _relCull2, _relOuter2;
    int _relCount;
    float _planetFalloffInv = 1f, _bhRampInv = 1f;

    void LateUpdate()
    {
        // --- toggle (throttled re-find; default ON if InputSettings not found) ---
        if (_input == null && --_inputRefindCooldown <= 0)
        {
            _input = FindObjectOfType<InputSettings>();
            _inputRefindCooldown = 60;
        }
        if (_input != null && !_input.fxSpaceDust) return;

        // --- camera ---
        if (_cam == null) _cam = Camera.main;
        if (_cam == null && Camera.allCamerasCount > 0) _cam = Camera.allCameras[0];
        if (_cam == null) return;

        // --- bodies / black hole ---
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) return;
        if (_blackHole == null || _planets.Count == 0) RefreshBodies(bodies);
        if (_blackHole == null) return;

        EnsureInit();
        if (_material == null) return;

        Vector3 camPos = _cam.transform.position;
        Vector3 bhPos = _blackHole.Position;

        // --- genuine, origin-shift-invariant camera delta (relative to the BH) ---
        Vector3 camRelBH = camPos - bhPos;          // invariant under EndlessManager rebase
        Vector3 genuineDelta = Vector3.zero;
        if (_hasPrevRel)
        {
            genuineDelta = camRelBH - _prevCamRelBH;
            if (genuineDelta.magnitude > sanityThreshold) genuineDelta = Vector3.zero; // glitch guard
        }
        _prevCamRelBH = camRelBH;
        _hasPrevRel = true;

        float dt = Time.deltaTime;
        float L = boxSize;
        float half = L * 0.5f;
        float fadeStart = half * (1f - edgeFadeFrac);
        float t = Time.time;

        // Atmosphere wash: dim the whole field the deeper the camera sits inside an
        // atmosphere, so dust seen from a planet surface reads as hazed/washed rather
        // than punching through vibrantly. Down to atmosphereWashMin at the surface,
        // full brightness once out in clear space.
        float maxImmersion = 0f;
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            if (p == null) continue;
            float atmoR = AtmosphereRadius(p);
            float thickness = Mathf.Max(1f, atmoR - p.radius);
            float altFrac = (Vector3.Distance(camPos, p.Position) - p.radius) / thickness; // 0 surface .. 1 atmo top
            float immersion = Mathf.Clamp01(1f - altFrac);
            if (immersion > maxImmersion) maxImmersion = immersion;
        }
        float washMul = Mathf.Lerp(1f, atmosphereWashMin, maxImmersion);

        // Co-move the whole field with the home planet's orbital velocity. On that
        // planet, this cancels the orbital parallax (you orbit with it), so the only
        // residual motion is the drift toward the black hole — it reads as the dust
        // being pulled in. Other planets (different orbital speeds) show some residual
        // drift, which is fine.
        Vector3 coMoveStep = (_homeBody != null ? _homeBody.velocity : Vector3.zero) * dt;

        // Per-frame planet shortlist so the hot loop skips dictionary lookups and
        // far planets entirely (cube corner reach = half * sqrt(3)).
        BuildRelevantPlanets(camPos, half * 1.7320508f);

        int m = 0;
        for (int i = 0; i < _local.Length; i++)
        {
            Vector3 lp = _local[i];
            Vector3 wp = camPos + lp;

            // speck -> BH: one sqrt, reused for drift direction, infall accel, and density
            Vector3 toBH = bhPos - wp;
            float bhDist = toBH.magnitude;
            float d = DensityAt(wp, bhDist);

            // drift straight toward the BH (accelerating as it gets closer) + co-move + parallax
            if (bhDist > 0.001f)
            {
                float fbhDrift = 1f - Mathf.Clamp01((bhDist - bhInnerRadius) * _bhRampInv);
                float effDrift = driftSpeed * (1f + driftBHAccel * fbhDrift);
                lp += (toBH / bhDist) * (effDrift * dt);
            }
            lp += coMoveStep;      // co-move with home planet so its orbital parallax cancels there
            lp -= genuineDelta;

            // toroidal wrap into [-half, half]
            lp.x -= L * Mathf.Round(lp.x / L);
            lp.y -= L * Mathf.Round(lp.y / L);
            lp.z -= L * Mathf.Round(lp.z / L);
            _local[i] = lp;

            if (d <= _threshold[i]) continue;

            // spherical edge fade (hides cube corners + wrap pops)
            float lpMag = lp.magnitude;
            if (lpMag >= half) continue;
            float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, half, lpMag));
            if (edge <= 0.001f) continue;

            float vis = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_threshold[i], Mathf.Min(1f, _threshold[i] + 0.15f), d));
            float tw = 1f - twinkleAmount + twinkleAmount * (0.5f + 0.5f * Mathf.Sin(t * twinkleSpeed + _phase[i]));
            float b = brightness * vis * edge * tw * washMul;
            if (b <= 0.004f) continue;

            float size = glowSize * _sizeRand[i];
            // Translation + uniform scale matrix built directly (cheaper than Matrix4x4.TRS,
            // which does quaternion->matrix work for a rotation we don't use).
            Matrix4x4 mtx = Matrix4x4.identity;
            mtx.m00 = size; mtx.m11 = size; mtx.m22 = size;
            mtx.m03 = camPos.x + lp.x; mtx.m13 = camPos.y + lp.y; mtx.m23 = camPos.z + lp.z;
            _matrices[m] = mtx;
            Color col = Color.Lerp(amberWarm, amberBright, d);
            _colors[m] = new Vector4(col.r, col.g, col.b, b);

            m++;
            if (m == 1023) { Flush(m); m = 0; }
        }
        if (m > 0) Flush(m);
    }

    void Flush(int count)
    {
        _mpb.Clear();
        _mpb.SetVectorArray(_ColorID, _colors);
        Graphics.DrawMeshInstanced(_mesh, 0, _material, _matrices, count, _mpb,
            ShadowCastingMode.Off, false);
    }

    // ---- density field in [0,1] ----
    // Uses the per-frame "relevant planet" arrays (_relPos/_relCull/_relCull2/_relOuter2)
    // built in BuildRelevantPlanets, and the already-computed speck->BH distance, so the
    // hot loop avoids dictionary lookups and skips sqrt for specks far from any planet.
    float DensityAt(Vector3 wp, float bhDist)
    {
        // planet avoidance: specks are cleared from the LOWER atmosphere, fading back
        // in above it. Squared-distance early-outs keep the common cases sqrt-free.
        float fPlanet = 1f;
        for (int i = 0; i < _relCount; i++)
        {
            Vector3 dv = wp - _relPos[i];
            float sq = dv.x * dv.x + dv.y * dv.y + dv.z * dv.z;
            if (sq <= _relCull2[i]) return 0f;       // inside lower atmosphere -> no dust
            if (sq >= _relOuter2[i]) continue;       // past the fade band -> contributes 1
            float f = (Mathf.Sqrt(sq) - _relCull[i]) * _planetFalloffInv;
            if (f < fPlanet) fPlanet = f;
        }

        // black-hole proximity boost (1 near BH, 0 far). Straight radial pull.
        float fbh = 1f - Mathf.Clamp01((bhDist - bhInnerRadius) * _bhRampInv);
        float d = fPlanet * Mathf.Clamp01(baseDensity + bhBoost * fbh);
        return d < 0f ? 0f : (d > 1f ? 1f : d);
    }

    // Build the short list of planets close enough to affect any speck in the camera box,
    // with squared cull/outer radii precomputed. Called once per frame.
    void BuildRelevantPlanets(Vector3 camPos, float boxReach)
    {
        _relCount = 0;
        _planetFalloffInv = 1f / Mathf.Max(1f, planetFalloff);
        _bhRampInv = 1f / Mathf.Max(1f, bhOuterRadius - bhInnerRadius);
        for (int i = 0; i < _planets.Count && _relCount < _relPos.Length; i++)
        {
            var p = _planets[i];
            if (p == null) continue;
            float atmoR = AtmosphereRadius(p);
            float cullR = Mathf.Lerp(p.radius, atmoR, lowerAtmosphereFrac);
            float outer = cullR + planetFalloff;
            Vector3 ppos = p.Position;
            if (Vector3.Distance(camPos, ppos) - boxReach > outer) continue; // too far to touch the box
            _relPos[_relCount] = ppos;
            _relCull[_relCount] = cullR;
            _relCull2[_relCount] = cullR * cullR;
            _relOuter2[_relCount] = outer * outer;
            _relCount++;
        }
    }

    /// <summary>
    /// 1 while at/inside the nearest planet's atmosphere, then ramping to 0 as the viewer moves out
    /// from <paramref name="fadeStartFrac"/>x to <paramref name="fadeEndFrac"/>x the atmosphere
    /// RADIUS (distance from planet centre / atmosphere radius). Defaults: full inside the atmosphere
    /// (1x), fading once you leave it, gone by twice the atmosphere radius (2x). Other camera effects
    /// (e.g. speed lines) multiply by this so they hand off to the space dust as the player leaves a
    /// planet. Returns 0 when there are no planets (deep space).
    /// </summary>
    public float InAtmosphereFactor(Vector3 worldPos, float fadeStartFrac = 1f, float fadeEndFrac = 2f)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) return 0f;
        float bestFrac = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || b.isStaticAttractor) continue;
            float atmoR = Mathf.Max(1f, AtmosphereRadius(b));
            float frac = Vector3.Distance(worldPos, b.Position) / atmoR; // 1 = atmosphere edge, 2 = 2x atmo radius
            if (frac < bestFrac) bestFrac = frac;
        }
        if (bestFrac == float.MaxValue) return 0f;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStartFrac, fadeEndFrac, bestFrac));
    }

    void RefreshBodies(CelestialBody[] bodies)
    {
        _planets.Clear();
        _blackHole = null;
        _homeBody = null;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.isStaticAttractor) { if (_blackHole == null) _blackHole = b; }
            else _planets.Add(b);
            if (b.bodyName == coMoveBodyName) _homeBody = b;
        }
    }

    // ---- atmosphere radius (cached; reflection so we never touch/break the forbidden zone) ----
    float AtmosphereRadius(CelestialBody b)
    {
        if (_atmoRadius.TryGetValue(b, out float r)) return r;
        r = ComputeAtmosphereRadius(b);
        _atmoRadius[b] = r;
        return r;
    }

    float ComputeAtmosphereRadius(CelestialBody b)
    {
        float fallback = b.radius * atmosphereFallbackMultiplier;
        try
        {
            MonoBehaviour gen = null;
            var comps = b.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var c in comps)
                if (c != null && c.GetType().Name == "CelestialBodyGenerator") { gen = c; break; }
            if (gen == null) return fallback;

            object settings = GetMember(gen, "body");
            object shading = settings != null ? GetMember(settings, "shading") : null;
            object atmo = shading != null ? GetMember(shading, "atmosphereSettings") : null;
            if (atmo == null) return fallback; // body has no atmosphere settings
            object scaleObj = GetMember(atmo, "atmosphereScale");
            if (scaleObj is float scale) return (1f + scale) * b.radius;
        }
        catch { /* fall through */ }
        return fallback;
    }

    static object GetMember(object obj, string name)
    {
        if (obj == null) return null;
        var tp = obj.GetType();
        var f = tp.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) return f.GetValue(obj);
        var p = tp.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null) return p.GetValue(obj);
        return null;
    }

    // ---- init: pool + mesh + material + glow texture ----
    void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;

        _local = new Vector3[particleCount];
        _threshold = new float[particleCount];
        _sizeRand = new float[particleCount];
        _phase = new float[particleCount];
        float half = boxSize * 0.5f;
        for (int i = 0; i < particleCount; i++)
        {
            _local[i] = new Vector3(Random.Range(-half, half), Random.Range(-half, half), Random.Range(-half, half));
            _threshold[i] = Random.value;
            _sizeRand[i] = Random.Range(1f - sizeJitter, 1f + sizeJitter);
            _phase[i] = Random.Range(0f, 6.2831853f);
        }

        _matrices = new Matrix4x4[1023];
        _colors = new Vector4[1023];
        _mpb = new MaterialPropertyBlock();

        int pc = Mathf.Max(_planets.Count, 8);
        _relPos = new Vector3[pc];
        _relCull = new float[pc];
        _relCull2 = new float[pc];
        _relOuter2 = new float[pc];

        BuildMeshAndMaterial();
    }

    void BuildMeshAndMaterial()
    {
        _mesh = new Mesh { name = "SpaceDustQuad" };
        _mesh.vertices = new[]
        {
            new Vector3(-0.5f,-0.5f,0f), new Vector3(0.5f,-0.5f,0f),
            new Vector3(0.5f, 0.5f,0f), new Vector3(-0.5f, 0.5f,0f)
        };
        _mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
        _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f); // never frustum-cull the batch

        const int S = 64;
        _glowTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2((S - 1) * 0.5f, (S - 1) * 0.5f);
        float maxd = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dd = Vector2.Distance(new Vector2(x, y), c) / maxd;
                float a = Mathf.Clamp01(1f - dd);
                a = a * a * a; // soft radial falloff
                _glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _glowTex.Apply();

        // Load a REAL material asset shipped in Resources. A runtime-only material
        // (new Material(Shader.Find(...))) loses its INSTANCING_ON shader variant in
        // builds (the build's variant collector never sees instancing used on this
        // shader), so DrawMeshInstanced renders nothing in the player even though it
        // works in the Editor. A material asset in Resources forces that variant to
        // ship. (Same class of bug as the grass build issue.) Fall back to Shader.Find
        // only if the asset is somehow missing.
        Material baseMat = Resources.Load<Material>("SpaceDust");
        if (baseMat != null)
        {
            _material = new Material(baseMat);
        }
        else
        {
            Shader sh = Shader.Find("Custom/SpaceDust");
            if (sh == null) { Debug.LogError("[SpaceDustField] SpaceDust material/shader not found (Resources/SpaceDust.mat or Always Included Shaders)"); return; }
            _material = new Material(sh);
        }
        _material.hideFlags = HideFlags.HideAndDontSave;
        _material.enableInstancing = true;
        _material.mainTexture = _glowTex;
    }

    // ================= tuning (appended at END per conventions) =================
    [Header("Budget")]
    [SerializeField] int particleCount = 5000;
    [SerializeField] float boxSize = 1200f;            // L: cube side around the camera

    [Header("Motion")]
    [SerializeField] float driftSpeed = 30f;           // base m/s toward the BH (far from it)
    [SerializeField] float driftBHAccel = 5f;          // extra drift multiplier ramped in near the BH (infall accel)
    [SerializeField] string coMoveBodyName = "Humble Abode"; // field co-moves with this planet's orbit (cancels its parallax)
    [SerializeField] float sanityThreshold = 900f;     // single-frame delta guard (< EndlessManager's 1000)

    [Header("Density")]
    [SerializeField] float baseDensity = 0.35f;        // deep-space baseline ("noticeable field")
    [SerializeField] float bhBoost = 0.65f;            // extra density near the BH
    [SerializeField] float bhOuterRadius = 30000f;     // distance where BH influence begins
    [SerializeField] float bhInnerRadius = 5000f;      // ~just outside the event horizon (BH radius 4000)
    [SerializeField] float atmosphereFallbackMultiplier = 1.35f;
    [SerializeField] float lowerAtmosphereFrac = 0.5f; // clear specks below this fraction of atmo thickness
    [SerializeField] float planetFalloff = 600f;       // fade band above the cleared lower atmosphere
    [SerializeField] float atmosphereWashMin = 0.15f;  // dust brightness multiplier when deep in an atmosphere (haze wash)

    [Header("Look")]
    [SerializeField] Color amberWarm = new Color(1f, 0.55f, 0.18f);
    [SerializeField] Color amberBright = new Color(1f, 0.82f, 0.45f);
    [SerializeField] float glowSize = 3f;
    [SerializeField] float sizeJitter = 0.6f;
    [SerializeField] float brightness = 1.4f;
    [SerializeField] float twinkleAmount = 0.3f;
    [SerializeField] float twinkleSpeed = 1.5f;
    [SerializeField] float edgeFadeFrac = 0.18f;       // fraction of half-box where specks fade out
}
