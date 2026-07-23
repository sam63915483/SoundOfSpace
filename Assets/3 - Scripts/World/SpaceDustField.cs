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
// DefaultExecutionOrder(300): the dust must compute its speck world positions AFTER
// the camera is finalised for the frame — after EndlessManager's origin rebase (0),
// CameraTransformFX (100) and the trailer free-cam (200). At order 0 it read the
// camera one frame stale and (on a rebase frame) in the wrong coordinate frame, so a
// detached free-cam's field was drawn a full rebase-offset behind for one frame, then
// snapped back. Reading last keeps the field locked to wherever the camera actually is.
[DefaultExecutionOrder(300)]
public class SpaceDustField : MonoBehaviour
{
    public static SpaceDustField Instance { get; private set; }

    /// When set, the dust cube centres on this transform instead of Camera.main.
    /// The trailer free-cam points it at the camera it is driving so the field
    /// follows the free-cam, then clears it on exit so it reverts to the player cam.
    public static Transform CenterOverride;

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
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_glowTex != null) Destroy(_glowTex);
        if (_material != null) Destroy(_material);
        if (_mesh != null) Destroy(_mesh);

        // The BH fades are written onto the SHARED material asset — in the
        // editor, play-mode writes PERSIST into the .mat. Quitting while
        // immersed once baked _AtmoFade 0.96 / _OceanFade 0.98 into the asset
        // and the black hole shipped invisible. Always park them at 0.
        if (_bhMaterial != null)
        {
            _bhMaterial.SetFloat(_AtmoFadeID, 0f);
            _bhMaterial.SetFloat(_OceanFadeID, 0f);
        }
    }

    // This is a DontDestroyOnLoad singleton, so its drifted particle layout and
    // cached scene refs survive a scene change. A black-hole dive leaves every
    // speck clumped/streaking toward the BH; without a reset that frozen layout
    // carries through backrooms → main menu → save reload and reappears looking
    // "stuck" (exactly the teleport-moment look). Re-seed a fresh field and drop
    // cached scene refs on every gameplay load so a reload always starts clean.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        ReseedField();
    }

    void ReseedField()
    {
        _hasPrevRel = false;          // parallax baseline — re-establish from the new camera pos
        _blackHole = null;            // force RefreshBodies to re-resolve for the loaded scene
        _homeBody = null;
        _planets.Clear();
        _bhMaterial = null;           // the cached Scingularity material is stale after a reload
        _oceanRadius.Clear();         // ocean radii belong to destroyed bodies after a reload
        if (_local != null)
        {
            float half = boxSize * 0.5f;
            for (int i = 0; i < _local.Length; i++)
                _local[i] = new Vector3(Random.Range(-half, half), Random.Range(-half, half), Random.Range(-half, half));
        }
    }

    // ---- runtime refs ----
    Camera _cam;
    CelestialBody _blackHole;
    CelestialBody _homeBody; // planet whose orbital velocity the field co-moves with
    readonly List<CelestialBody> _planets = new List<CelestialBody>();
    readonly Dictionary<CelestialBody, float> _atmoRadius = new Dictionary<CelestialBody, float>();
    // Whether a body has REAL atmosphere settings (vs the geometric fallback
    // radius). Populated by ComputeAtmosphereRadius; used to gate the black-hole
    // fade so it only dissolves the BH into bodies that actually have haze.
    readonly Dictionary<CelestialBody, bool> _hasAtmo = new Dictionary<CelestialBody, bool>();
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

    // Black-hole (Scingularity) material, found lazily from the BH body. Driven each
    // frame with the camera's atmosphere immersion so the BH lens fades into the
    // hazed sky from a planet surface (no hard circular seam through the atmosphere).
    Material _bhMaterial;
    static readonly int _AtmoFadeID = Shader.PropertyToID("_AtmoFade");
    static readonly int _OceanFadeID = Shader.PropertyToID("_OceanFade");

    // Ocean radii per body (world units; 0 = no ocean), cached like _atmoRadius.
    // Cleared in ReseedField on scene load.
    readonly Dictionary<CelestialBody, float> _oceanRadius = new Dictionary<CelestialBody, float>();

    // Per-frame ocean shortlist for the per-speck occlusion test (planets whose ocean
    // sphere can actually sit between the camera and a speck in the box).
    Vector3[] _relOceanPos;
    float[] _relOceanR2;
    int _relOceanCount;

    // HLSL-style smoothstep: 0 below edge0, 1 above edge1, smooth between.
    // (Unity's Mathf.SmoothStep has DIFFERENT semantics — it interpolates
    // between its first two args using the third as the 0..1 parameter.)
    static float EdgeSmoothstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    float OceanRadius(CelestialBody p)
    {
        if (p == null) return 0f;
        if (_oceanRadius.TryGetValue(p, out float r)) return r;
        var gen = p.GetComponentInChildren<CelestialBodyGenerator>();
        r = gen != null ? gen.GetOceanRadius() : 0f;
        _oceanRadius[p] = r;
        return r;
    }

    // ---- per-frame relevant-planet shortlist (perf: avoids dict lookups + far planets in the hot loop) ----
    Vector3[] _relPos;
    float[] _relCull, _relCull2, _relOuter2;
    int _relCount;
    float _planetFalloffInv = 1f, _bhRampInv = 1f;

    // Drives the Scingularity shader's _AtmoFade uniform. The black-hole quad is
    // a queue-3000 transparent that draws AFTER the [ImageEffectOpaque] atmosphere
    // post-process, so left alone it paints its dark lensed void straight OVER the
    // planet haze — the hard "circle" where the atmosphere should be. _AtmoFade
    // dissolves those dark pixels back into the already-hazed sky (the bright disk
    // is luminance-protected in the shader), removing the seam.
    //
    // It ramps up in TWO independent cases — the old driver only handled the first:
    //   1. the camera sits INSIDE a planet's atmosphere (immersion), and
    //   2. the black hole is seen THROUGH / behind a planet's atmosphere from
    //      OUTSIDE it (immersion ~0) — the "looking at the planet from space with
    //      the BH beyond it" case where the seam actually showed.
    // Stays 0 for a black hole alone in clear space, so its dark lens is preserved.
    void UpdateBlackHoleAtmoFade()
    {
        if (_bhMaterial == null && _blackHole != null)
        {
            var rends = _blackHole.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var mm = rends[i] != null ? rends[i].sharedMaterial : null;
                if (mm != null && mm.shader != null && mm.shader.name.Contains("Scingularity"))
                { _bhMaterial = mm; break; }
            }
        }
        if (_bhMaterial == null) return;

        // The ACTUAL render camera (not the free-cam dust centre) — the fade must
        // match what's on screen. Runtime-only SetFloat, so the shared material
        // asset is never dirtied on disk.
        Vector3 camPos = _cam.transform.position;
        Vector3 toBH = _blackHole.Position - camPos;
        float distBH = toBH.magnitude;
        Vector3 dirBH = distBH > 1e-3f ? toBH / distBH : _cam.transform.forward;

        float fade = 0f;
        float oceanFade = 0f;
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            if (p == null) continue;
            Vector3 toP = p.Position - camPos;
            float distP = toP.magnitude;
            float atmoR = AtmosphereRadius(p);

            // Ocean occlusion (drives _OceanFade — a FULL fade in the shader): the ocean
            // post-process writes no depth, so the BH's own depth kill can't hide it behind
            // water — without this the whole lensed ring shows straight through the sea.
            float oceanR = OceanRadius(p);
            if (oceanR > 0f)
            {
                if (distP < oceanR) { oceanFade = 1f; }              // camera underwater
                else if (distP < distBH && distP > 1e-3f)
                {
                    float oceanAng = Mathf.Atan2(oceanR, distP);                      // angular radius of the water disk
                    float angO = Vector3.Angle(dirBH, toP / distP) * Mathf.Deg2Rad;   // BH offset from planet centre
                    // EdgeSmoothstep, NOT Mathf.SmoothStep: Unity's SmoothStep(a,b,t)
                    // INTERPOLATES a->b (t is the 0..1 parameter). Passing the angle as
                    // t made every distant ocean planet return ~edge1 (tiny radians) so
                    // thr ~= 0.98 REGARDLESS OF DIRECTION — _OceanFade sat pinned at ~1
                    // and the black hole was erased from the sky in all of gameplay.
                    float thr = 1f - EdgeSmoothstep(oceanAng * 0.92f, oceanAng * 1.10f, angO);
                    if (thr > oceanFade) oceanFade = thr;
                }
            }

            // Case 1 — camera immersed in this atmosphere (1 at the surface, 0 at the top).
            float thickness = Mathf.Max(1f, atmoR - p.radius);
            float immersion = Mathf.Clamp01(1f - (distP - p.radius) / thickness);

            // Case 2 — BH viewed through this atmosphere from outside. Only counts
            // when the atmosphere sits BETWEEN the camera and the (far) black hole,
            // and only for bodies that actually HAVE haze (a bare rock's limb has
            // nothing to dissolve the BH's dark void into).
            float through = 0f;
            if (HasAtmosphere(p) && distP > 1e-3f && distP < distBH)
            {
                float atmoAng = Mathf.Atan2(atmoR, distP);                        // angular radius of the atmo disk
                float ang = Vector3.Angle(dirBH, toP / distP) * Mathf.Deg2Rad;    // BH offset from the planet centre
                // 1 while the BH direction sits inside the atmo disk, easing to 0
                // as it crosses the outer edge (slack leads the hard seam in).
                // EdgeSmoothstep — same Mathf.SmoothStep misuse as the ocean
                // branch above (it pinned _AtmoFade near 1 from everywhere).
                through = 1f - EdgeSmoothstep(atmoAng * 0.85f, atmoAng * 1.35f, ang);
            }

            // Immersion is CAPPED: at 1.0 the shader's luminance gate erased
            // everything but the thin bright ring from a planet surface — the
            // black hole read as "gone" from Humble Abode. At 0.7 the dark
            // lensed body keeps ~30% presence through the haze while the
            // "through the limb from space" case (through) stays full-strength.
            const float surfaceImmersionCap = 0.7f;
            float f = Mathf.Max(immersion * surfaceImmersionCap, through);
            if (f > fade) fade = f;
        }

        _bhMaterial.SetFloat(_AtmoFadeID, fade);
        _bhMaterial.SetFloat(_OceanFadeID, oceanFade);
    }

    void LateUpdate()
    {
        // --- toggle (throttled re-find; default ON if InputSettings not found) ---
        if (_input == null && --_inputRefindCooldown <= 0)
        {
            _input = FindObjectOfType<InputSettings>();
            _inputRefindCooldown = 60;
        }

        // --- camera ---
        if (_cam == null) _cam = Camera.main;
        if (_cam == null && Camera.allCamerasCount > 0) _cam = Camera.allCameras[0];
        if (_cam == null) return;

        // --- bodies / black hole ---
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0) return;
        if (_blackHole == null || _planets.Count == 0) RefreshBodies(bodies);
        if (_blackHole == null) return;

        // Black-hole atmosphere fade runs BEFORE the space-dust toggle gate below,
        // so it keeps updating even when the player turns space dust off (it used
        // to freeze there and the atmosphere seam around the BH came back).
        UpdateBlackHoleAtmoFade();

        // --- space-dust toggle: only the dust field itself is skipped when off ---
        if (_input != null && !_input.fxSpaceDust) return;

        EnsureInit();
        if (_material == null) return;

        bool freeCam = CenterOverride != null;
        Vector3 camPos = freeCam ? CenterOverride.position : _cam.transform.position;
        Vector3 bhPos = _blackHole.Position;

        // --- genuine, origin-shift-invariant camera delta (relative to the BH) ---
        // Skipped entirely for the trailer free-cam: the BH-relative parallax can't be
        // kept in phase with a detached, separately-rebased camera, so it lagged and
        // snapped. The free-cam path below uses a self-contained drift locked to the
        // camera box instead — no rebase/timing dependence, so it can't lag or snap.
        Vector3 camRelBH = camPos - bhPos;          // invariant under EndlessManager rebase
        Vector3 genuineDelta = Vector3.zero;
        if (!freeCam && _hasPrevRel)
        {
            genuineDelta = camRelBH - _prevCamRelBH;
            if (genuineDelta.magnitude > sanityThreshold) genuineDelta = Vector3.zero; // glitch guard
        }
        _prevCamRelBH = camRelBH;   // keep fresh so exiting free-cam doesn't spike the first frame
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
        // Free-cam keeps the dust at full brightness (no atmosphere haze-wash) so the
        // field stays seamless even when orbiting close to the planet.
        float washMul = (CenterOverride != null) ? 1f : Mathf.Lerp(1f, atmosphereWashMin, maxImmersion);

        // (Black-hole _AtmoFade is now driven by UpdateBlackHoleAtmoFade() above,
        //  before the dust toggle — so it works from space AND with dust off.)

        // Co-move the whole field with the home planet's orbital velocity. On that
        // planet, this cancels the orbital parallax (you orbit with it), so the only
        // residual motion is the drift toward the black hole — it reads as the dust
        // being pulled in. Other planets (different orbital speeds) show some residual
        // drift, which is fine.
        Vector3 coMoveStep = freeCam ? Vector3.zero : (_homeBody != null ? _homeBody.velocity : Vector3.zero) * dt;

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

            // Drift straight toward the BH (accelerating as it gets closer) — the
            // "dust getting sucked into the black hole" stream. toBH is camera-relative
            // so it's origin-rebase invariant. Applied in BOTH modes.
            if (bhDist > 0.001f)
            {
                float fbhDrift = 1f - Mathf.Clamp01((bhDist - bhInnerRadius) * _bhRampInv);
                float effDrift = driftSpeed * (1f + driftBHAccel * fbhDrift);
                lp += (toBH / bhDist) * (effDrift * dt);
            }
            // Camera-relative parallax + planet co-move only in normal play. For the
            // detached free-cam these can't be kept in phase with a separately-rebased
            // camera (the left-behind/snap), and the box is kept locked to the camera
            // instead — so the dust just streams toward the BH all around you.
            if (!freeCam)
            {
                lp += coMoveStep;      // co-move with home planet so its orbital parallax cancels there
                lp -= genuineDelta;
            }

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

            // Ocean occlusion: kill specks BEHIND a planet's water surface. The dust is a
            // queue-3000 additive draw AFTER the [ImageEffectOpaque] ocean composite (which
            // writes no depth), so a speck past the water would otherwise punch through it.
            // Analytic segment(camera → speck)-vs-sphere; specks IN FRONT of the water pass
            // naturally (segment ends before the sphere).
            bool behindOcean = false;
            for (int k = 0; k < _relOceanCount; k++)
            {
                Vector3 toC = _relOceanPos[k] - camPos;
                float tSeg = Mathf.Clamp01(Vector3.Dot(toC, lp) / Mathf.Max(0.0001f, lpMag * lpMag));
                Vector3 closest = lp * tSeg - toC;
                if (closest.sqrMagnitude < _relOceanR2[k]) { behindOcean = true; break; }
            }
            if (behindOcean) continue;

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
        // Trailer free-cam: render a uniform field all around the camera — skip the
        // planet-avoidance culling + black-hole density gradient that otherwise leave a
        // lopsided "cloud" (everything culled on the planet side during the descent) you
        // can fly out of. Only active while the free-cam owns the view.
        if (CenterOverride != null) return uniformDensity;

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
        _relOceanCount = 0;
        if (_relOceanPos == null) { _relOceanPos = new Vector3[8]; _relOceanR2 = new float[8]; }
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

            // Ocean shortlist for the per-speck occlusion test — only planets whose water
            // sphere can reach between the camera and a speck inside the box.
            float oceanR = OceanRadius(p);
            if (oceanR > 0f && _relOceanCount < _relOceanPos.Length
                && Vector3.Distance(camPos, ppos) - boxReach < oceanR)
            {
                _relOceanPos[_relOceanCount] = ppos;
                _relOceanR2[_relOceanCount] = oceanR * oceanR;
                _relOceanCount++;
            }
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
        _hasAtmo[b] = false;   // assume none until real settings are found
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
            if (scaleObj is float scale) { _hasAtmo[b] = true; return (1f + scale) * b.radius; }
        }
        catch { /* fall through */ }
        return fallback;
    }

    // True only for bodies with real atmosphere settings (not the fallback).
    // Ensures the radius/flag are computed+cached first.
    bool HasAtmosphere(CelestialBody b)
    {
        AtmosphereRadius(b);
        return _hasAtmo.TryGetValue(b, out bool v) && v;
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
    [SerializeField] float baseDensity = 0.5f;         // deep-space baseline ("noticeable field"). Raised 0.35→0.5: the bright Milky Way skybox washes out the additive dust, so more specks must pass threshold to read as a field everywhere (not just dense clumps).
    [SerializeField] float bhBoost = 0.65f;            // extra density near the BH
    [SerializeField] float bhOuterRadius = 30000f;     // distance where BH influence begins
    [SerializeField] float bhInnerRadius = 5000f;      // ~just outside the event horizon (BH radius 4000)
    [SerializeField] float atmosphereFallbackMultiplier = 1.35f;
    [SerializeField] float lowerAtmosphereFrac = 0.5f; // clear specks below this fraction of atmo thickness
    [SerializeField] float planetFalloff = 600f;       // fade band above the cleared lower atmosphere
    [SerializeField] float atmosphereWashMin = 0.15f;  // dust brightness multiplier when deep in an atmosphere (haze wash)
    [SerializeField] float uniformDensity = 0.85f;     // flat density used while the trailer free-cam owns the view (seamless field, no planet/BH clumping)

    [Header("Look")]
    [SerializeField] Color amberWarm = new Color(1f, 0.55f, 0.18f);
    [SerializeField] Color amberBright = new Color(1f, 0.82f, 0.45f);
    [SerializeField] float glowSize = 5f;              // Raised 3→5: finer specks vanished against the bright galaxy; bigger glows read as a field again.
    [SerializeField] float sizeJitter = 0.6f;
    [SerializeField] float brightness = 4.5f;          // Raised 2.8→4.5: additive amber needs more punch to show over the bright Milky Way skybox.
    [SerializeField] float twinkleAmount = 0.3f;
    [SerializeField] float twinkleSpeed = 1.5f;
    [SerializeField] float edgeFadeFrac = 0.18f;       // fraction of half-box where specks fade out
}
