using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the sun lens flare as a stack of three procedural screen-space
/// UI overlays — outer corona, bright halo, and diffraction spikes — all
/// additively blended via LensFlareAdditive.shader.
///
/// Why not Unity's legacy LensFlare/FlareLayer?
///   1. The atmosphere/ocean post-processing is [ImageEffectOpaque] (see
///      CLAUDE.md "Atmosphere / ocean post-process gotcha") and consumes
///      the rendered scene fully. FlareLayer draws its flares before that
///      pass, so the atmosphere blit erases them.
///   2. We can't modify the atmosphere shaders (locked-zone in CLAUDE.md).
///   3. Built-in FlareLayer also frustum-culls flares past farClipPlane,
///      which would silently kill the sun flare at solar-system scale.
///
/// Why procedural instead of legacy lens-flare PSDs? Our UI-overlay path
/// runs *after* all post-processing, including bloom — so flare elements
/// stay at 8-bit LDR and never get the soft glow that makes high-end
/// movie/game flares feel real. Designing the flare from large soft
/// Gaussian shapes specifically for LDR additive rendering reads as glow
/// without needing bloom, and avoids the "translucent decal" look that
/// low-res PSD ghosts produce in this pipeline.
///
/// Polls CameraEffectsManager.Input.fxLensFlares each frame to honour the
/// pause-menu toggle.
/// </summary>
// DefaultExecutionOrder(300): run the flare AFTER the camera is finalised for the
// frame — after CameraTransformFX (100) and the trailer free-cam (200). In plain
// Update() it read the camera one frame stale, which is invisible for the player
// (camera only moves on mouse input) but constant for the always-rotating orbit cam,
// so the flare visibly trailed the sun. Reading last keeps it locked to the view.
[DefaultExecutionOrder(300)]
public class LensFlareRegistry : MonoBehaviour
{
    // ─── Halo (bright sun core) ──────────────────────────────────────────
    const float kHaloMaxSizePx        = 360f;
    const float kHaloMinSizePx        = 140f;
    const float kHaloMaxAlpha         = 0.85f;
    const float kHaloDotPowerForAlpha = 1.5f;
    const int   kHaloSpriteRes        = 256;
    static readonly Color kHaloColor  = new Color(1.00f, 0.95f, 0.80f, 1f);
    // Fade below this is treated as fully gone — skip rendering. Roughly
    // corresponds to the sun being ~85°+ off camera-forward.
    const float kFadeCullThreshold    = 0.004f;

    // Partial-occlusion sampling: number of rays around the sun's
    // silhouette circle (plus one at the centre = N+1 total samples).
    // More samples = finer-grained partial fade, slightly more raycast cost.
    // 12 (was 8): 1/13 steps instead of 1/9 — partial cover fades in finer
    // increments, so a planet limb sweeping the silhouette pulses less.
    const int   kOcclusionEdgeSamples = 12;
    // Temporal smoothing for the visibility fraction so per-sample steps
    // don't read as a flicker when an obstacle's edge sweeps past samples.
    // 0.25s (was 0.08s): single-sample flips at a partially-covered sun were
    // still visible as fast glow/dim pulsing — a slower glide reads as the
    // sun smoothly emerging/hiding instead.
    const float kVisibilitySmoothTime = 0.25f;
    // Raw visibility below this is treated as fully occluded (remapped to 0).
    // Kills the "thin gap in tree/cabin collider lets 1-2 silhouette samples
    // slip through" flicker without breaking the partial-occlusion feel for
    // higher visibility fractions.
    const float kVisibilityThreshold  = 0.25f;

    // ─── Outer corona (broad faint glow extending past the halo) ─────────
    const float kCoronaMaxSizePx     = 1300f;
    const float kCoronaMinSizePx     =  650f;
    const float kCoronaMaxAlpha      = 0.22f;
    const int   kCoronaSpriteRes     = 256;
    static readonly Color kCoronaColor = new Color(1.00f, 0.88f, 0.65f, 1f);

    // ─── Diffraction spikes (per-ray breathing star) ─────────────────────
    // Each spike is its own Image so rays can change length individually
    // over time. Each entry covers two visible rays (one through θ and one
    // through θ+180°) because the ray sprite is symmetric around its centre.
    //
    // phaseDeg pairs the rays inverse-phase: when "big" rays (phase 0)
    // shrink, "small" rays (phase 180) grow, so the dominant rays swap
    // roles every half-period.
    //
    // minLength..maxLength = breath range as a fraction of kSpikesMaxSizePx.
    // thicknessPx = ray perpendicular width in pixels (constant per ray).
    // weight = baseline alpha multiplier (constant per ray).
    const float kSpikesMaxSizePx     = 420f;
    const float kSpikesMaxAlpha      = 0.75f;
    const int   kRayBeamWidthPx      = 512;
    const int   kRayBeamHeightPx     = 32;
    static readonly Color kSpikesColor = new Color(1.00f, 0.97f, 0.88f, 1f);
    struct SpikeDef {
        public float angleDeg;
        public float minLength;
        public float maxLength;
        public float thicknessPx;
        public float weight;
        public float phaseDeg;
    }
    static readonly SpikeDef[] kSpikes = new SpikeDef[] {
        // Group A — orthogonal "big" rays. Phase 0: start large, swing small.
        new SpikeDef { angleDeg =   0f, minLength = 0.30f, maxLength = 1.00f, thicknessPx = 22f, weight = 1.00f, phaseDeg =   0f },
        new SpikeDef { angleDeg =  90f, minLength = 0.30f, maxLength = 1.00f, thicknessPx = 22f, weight = 1.00f, phaseDeg =   0f },
        // Group B — diagonals + small offsets. Phase 180: start small, swing large.
        new SpikeDef { angleDeg =  45f, minLength = 0.20f, maxLength = 0.65f, thicknessPx = 18f, weight = 0.65f, phaseDeg = 180f },
        new SpikeDef { angleDeg = 135f, minLength = 0.20f, maxLength = 0.65f, thicknessPx = 18f, weight = 0.65f, phaseDeg = 180f },
        new SpikeDef { angleDeg =  22f, minLength = 0.10f, maxLength = 0.45f, thicknessPx = 14f, weight = 0.45f, phaseDeg = 180f },
        new SpikeDef { angleDeg =  68f, minLength = 0.12f, maxLength = 0.50f, thicknessPx = 14f, weight = 0.50f, phaseDeg = 180f },
        new SpikeDef { angleDeg = 112f, minLength = 0.10f, maxLength = 0.45f, thicknessPx = 14f, weight = 0.45f, phaseDeg = 180f },
        new SpikeDef { angleDeg = 158f, minLength = 0.15f, maxLength = 0.55f, thicknessPx = 14f, weight = 0.55f, phaseDeg = 180f },
    };
    // Period ≈ 2π / kSpikesBreathSpeed seconds. Lower = slower breath.
    const float kSpikesBreathSpeed       = 0.80f;
    // Alpha at breath trough as a fraction of full (1 = no alpha breathing).
    const float kSpikesBreathAlphaFloor  = 0.15f;

    // ─── Ghost orb chain (soft colored discs along sun → screen-centre) ─
    // Each orb is positioned by axisT (1 = sun, 0 = screen centre,
    // -1 = mirrored past centre). Shapes vary: Disc = soft Gaussian, Hex =
    // 6-sided aperture-style ghost. Sizes are intentionally varied so the
    // chain reads as a series of distinct optical artifacts. Alphas are low
    // — these are background detail; the halo + spikes are the main visual.
    enum OrbShape { Disc, Hex }
    struct OrbDef { public float axisT; public float size; public float alpha; public Color color; public OrbShape shape; }
    static readonly OrbDef[] kOrbs = new OrbDef[] {
        new OrbDef { axisT =  0.55f, size =  55f, alpha = 0.55f, color = new Color(1.00f, 0.85f, 0.55f, 1f), shape = OrbShape.Disc }, // tiny warm gold
        new OrbDef { axisT =  0.30f, size = 230f, alpha = 0.26f, color = new Color(0.55f, 0.85f, 1.00f, 1f), shape = OrbShape.Hex  }, // big cool blue hex
        new OrbDef { axisT =  0.10f, size =  40f, alpha = 0.65f, color = new Color(1.00f, 0.95f, 0.90f, 1f), shape = OrbShape.Disc }, // tiny bright white
        new OrbDef { axisT = -0.18f, size = 150f, alpha = 0.32f, color = new Color(1.00f, 0.50f, 0.70f, 1f), shape = OrbShape.Hex  }, // medium pink hex
        new OrbDef { axisT = -0.55f, size = 110f, alpha = 0.42f, color = new Color(0.55f, 0.75f, 1.00f, 1f), shape = OrbShape.Disc }, // medium pale blue
    };
    const int   kOrbSpriteRes        = 192;

    // Occlusion raycast mask. ~0 = all layers; we filter player/ship/sun
    // in code because their colliders sit at or near the camera origin and
    // would always be reported as hits.
    const int kOcclusionMask = ~0;

    // ScreenSpaceCamera canvas plane distance in metres. Flare quads render
    // at this depth in 3D space, so any opaque world geometry between the
    // camera and this plane (cockpit hull when piloting, foreground
    // obstacles, the ship's body when standing right next to it) writes
    // depth at a nearer distance and occludes the flare via the depth
    // buffer. Same trick SpeedLinesOverlay uses to keep speed lines out
    // of the cockpit. Window meshes don't write depth, so the flare
    // remains visible through them.
    const float kCanvasPlaneDistance = 5f;

    // Inside a ship the hull walls sit well beyond 5m, so they fail to depth-
    // occlude the flare — it bleeds through far walls when on foot (piloting
    // works only because the cockpit is within 5m, and the hull meshes have no
    // colliders so the backup raycast can't catch them either). When the player
    // is inside a ship, push the plane out past the whole interior so every
    // depth-writing hull surface hides the flare, while the depth-less cockpit
    // window / open hatch still show it. ~40m clears the ship's ~28m diagonal.
    const float kInteriorPlaneDistance = 40f;

    // Additive UI shader name (LensFlareAdditive.shader).
    const string kAdditiveShaderName = "UI/LensFlareAdditive";

    // ─── Runtime state ───────────────────────────────────────────────────
    Canvas _canvas;
    Sprite _haloSprite;
    Sprite _coronaSprite;
    Sprite _rayBeamSprite;   // single horizontal ray, shared by all spike Images
    Sprite _orbSprite;
    Sprite _hexSprite;
    Material _additiveMat;

    CelestialBody _sunBody;
    Image _sunImage;       RectTransform _sunRT;
    Image _coronaImage;    RectTransform _coronaRT;
    Image[] _spikeImages;  RectTransform[] _spikeRTs;
    Image[] _orbImages;    RectTransform[] _orbRTs;

    readonly RaycastHit[] _hitBuf = new RaycastHit[16];
    // Ocean radii per body (0 = no ocean), lazily cached. The ocean is an
    // [ImageEffectOpaque] post-process with NO collider, so the raycast occlusion
    // can't see it — without the analytic sphere test below, the flare pierces
    // straight through water. Destroyed-body keys after a scene reload simply
    // never match again (tiny, bounded leak — same tradeoff as _sunBody).
    readonly System.Collections.Generic.Dictionary<CelestialBody, float> _oceanRadii =
        new System.Collections.Generic.Dictionary<CelestialBody, float>();
    bool _setupComplete;
    // Smoothed occlusion visibility (0 = fully blocked, 1 = fully clear).
    float _smoothedVisibility = 1f;
    float _visibilityVelocity;
    // The occlusion test fires 9 raycasts/frame while the sun is on-screen.
    // Throttle the recompute to every 3rd frame and reuse the cached fraction
    // in between — the SmoothDamp below keeps the fade continuous, so the
    // visual is unchanged while the per-frame raycast cost drops ~3×.
    int _occlFrameCounter;
    float _cachedRawVis = 1f;

    void LateUpdate()
    {
        if (!_setupComplete) Setup();
        if (!_setupComplete) return;

        // Camera: prefer CameraEffectsManager's tracked PlayerCamera, fall
        // back to Camera.main. The manager singleton can be null for a few
        // frames around scene transitions.
        Camera cam = null;
        var mgr = CameraEffectsManager.Instance;
        if (mgr != null && mgr.PlayerCamera != null) cam = mgr.PlayerCamera;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // ScreenSpaceCamera canvas needs a worldCamera to render through.
        // Rebind every frame is cheap and survives camera/scene swaps
        // (entering the ship, scene reloads, etc.).
        EnsureCanvasCameraBound(cam);
        UpdateCanvasPlaneForShipInterior();

        // Toggle: respect the user setting if InputSettings is reachable,
        // otherwise default ON.
        bool toggleOn = true;
        if (mgr != null && mgr.Input != null)
            toggleOn = mgr.MasterEnabled && mgr.Input.fxLensFlares;

        if (!toggleOn) { HideFlare(); return; }

        UpdateSun(cam);
    }

    void OnDestroy()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_additiveMat != null) Destroy(_additiveMat);
    }

    void Setup()
    {
        BuildCanvas();
        EnsureAdditiveMaterial();
        _haloSprite    = BuildHaloSprite();
        _coronaSprite  = BuildCoronaSprite();
        _rayBeamSprite = BuildRayBeamSprite();
        _orbSprite     = BuildOrbSprite();
        _hexSprite     = BuildHexSprite();
        FindSun();
        EnsureFlareLayers();
        _setupComplete = _canvas != null && _haloSprite != null;
    }

    // Load the additive UI shader and build a shared material from it. The
    // halo/corona/spikes layers all use this so their bright pixels glow
    // additively over the sky instead of alpha-blending as flat discs.
    void EnsureAdditiveMaterial()
    {
        if (_additiveMat != null) return;
        Shader sh = Shader.Find(kAdditiveShaderName);
        if (sh == null)
        {
            Debug.LogWarning("[LensFlareRegistry] Shader '" + kAdditiveShaderName + "' not found. If running a build, add LensFlareAdditive.shader to Project Settings > Graphics > Always Included Shaders.");
            return;
        }
        _additiveMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, name = "LensFlareAdditive (runtime)" };
    }

    void BuildCanvas()
    {
        if (_canvas != null) return;
        var go = new GameObject("[LensFlareOverlayCanvas]");
        DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        // ScreenSpaceCamera (not Overlay) so opaque 3D geometry between the
        // camera and the canvas plane occludes the flare via the depth
        // buffer — this is what hides the flare behind the ship's hull
        // when piloting and lets it remain visible through the window
        // (window meshes are transparent and don't write depth). Same
        // approach SpeedLinesOverlay uses.
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.planeDistance = kCanvasPlaneDistance;
        // ScreenSpaceOverlay HUD always renders on top of ScreenSpaceCamera
        // canvases regardless of sortingOrder, so HUD still sits over the
        // flare. sortingOrder controls order vs OTHER ScreenSpaceCamera
        // canvases (speed lines, etc.); 10 keeps the flare behind those.
        _canvas.sortingOrder = 10;
        go.AddComponent<CanvasScaler>();
        // worldCamera is bound lazily in EnsureCanvasCameraBound once the
        // player camera is available (it may be null for a frame or two
        // around scene transitions).
        // No GraphicRaycaster — flares should be click-through.
    }

    void EnsureCanvasCameraBound(Camera cam)
    {
        if (_canvas == null || cam == null) return;
        if (_canvas.worldCamera == cam) return;
        _canvas.worldCamera = cam;
    }

    // See kInteriorPlaneDistance. Reuses OxygenManager's already-computed
    // "inside the ship" flag (true while piloting too); falls back to pilot
    // state if the oxygen manager isn't present (e.g. a scene without it).
    void UpdateCanvasPlaneForShipInterior()
    {
        if (_canvas == null) return;
        bool insideShip = OxygenManager.Instance != null
            ? OxygenManager.Instance.PlayerInsideShip
            : Ship.PilotedInstance != null;
        float want = insideShip ? kInteriorPlaneDistance : kCanvasPlaneDistance;
        if (!Mathf.Approximately(_canvas.planeDistance, want))
            _canvas.planeDistance = want;
    }

    // ─── Procedural sprite builders ──────────────────────────────────────

    // Halo: bright Gaussian core with a thin bloom ring at r ≈ 0.75. Pure
    // white — colour comes from Image.color so we can tint per-frame.
    static Sprite BuildHaloSprite()
    {
        int res = kHaloSpriteRes;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "LensFlareHaloSprite",
        };
        float cx = (res - 1) * 0.5f, cy = (res - 1) * 0.5f, maxR = (res - 1) * 0.5f;
        var pixels = new Color32[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - cx) / maxR;
                float dy = (y - cy) / maxR;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 1f) { pixels[y * res + x] = new Color32(0, 0, 0, 0); continue; }
                float core = Mathf.Exp(-(r * r) / (2f * 0.30f * 0.30f));
                float dRing = r - 0.75f;
                float ring = Mathf.Exp(-(dRing * dRing) / (2f * 0.06f * 0.06f)) * 0.18f;
                float edgeFade = 1f - r * r;
                float v = Mathf.Clamp01((core + ring) * edgeFade);
                byte b = (byte)(v * 255f);
                pixels[y * res + x] = new Color32(255, 255, 255, b);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }

    // Outer corona: very soft broad Gaussian, much bigger than the halo,
    // very low alpha. Reads as the sky brightening around the sun.
    static Sprite BuildCoronaSprite()
    {
        int res = kCoronaSpriteRes;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "LensFlareCoronaSprite",
        };
        float cx = (res - 1) * 0.5f, cy = (res - 1) * 0.5f, maxR = (res - 1) * 0.5f;
        var pixels = new Color32[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - cx) / maxR;
                float dy = (y - cy) / maxR;
                float r2 = dx * dx + dy * dy;
                if (r2 > 1f) { pixels[y * res + x] = new Color32(0, 0, 0, 0); continue; }
                // Broad inner Gaussian + slower outer falloff for the long tail.
                float inner = Mathf.Exp(-r2 / (2f * 0.28f * 0.28f));
                float outer = Mathf.Exp(-r2 / (2f * 0.55f * 0.55f)) * 0.55f;
                float edgeFade = 1f - r2;
                float v = Mathf.Clamp01((inner + outer) * edgeFade);
                byte b = (byte)(v * 255f);
                pixels[y * res + x] = new Color32(255, 255, 255, b);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }

    // Single horizontal "ray beam" sprite reused by every diffraction-spike
    // Image. Each spike Image rotates this beam to its angle and stretches
    // it to its current breath length, so per-ray breathing is just a
    // RectTransform tweak — no texture re-baking.
    //
    // Long Gaussian in U (length axis) tapers the ray cleanly at both ends.
    // Tight Gaussian in V (thickness axis) keeps the rendered ray thin even
    // when the Image's vertical size is generous.
    static Sprite BuildRayBeamSprite()
    {
        int w = kRayBeamWidthPx;
        int h = kRayBeamHeightPx;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "LensFlareRayBeamSprite",
        };
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float halfW = (w - 1) * 0.5f, halfH = (h - 1) * 0.5f;
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = (x - cx) / halfW;   // length axis  [-1, 1]
                float v = (y - cy) / halfH;   // thickness axis [-1, 1]
                float longProf = Mathf.Exp(-(u * u) / (2f * 0.50f * 0.50f));
                float thinProf = Mathf.Exp(-(v * v) / (2f * 0.20f * 0.20f));
                float edge = Mathf.Max(0f, 1f - u * u);
                float val = Mathf.Clamp01(longProf * thinProf * edge);
                byte a = (byte)(val * 255f);
                pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }

    // Ghost orb: soft Gaussian disc with a slightly tighter bright core,
    // so when tinted with a colour the orb reads as a small glowing bead
    // rather than a uniform fuzzy circle.
    static Sprite BuildOrbSprite()
    {
        int res = kOrbSpriteRes;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "LensFlareOrbSprite",
        };
        float cx = (res - 1) * 0.5f, cy = (res - 1) * 0.5f, maxR = (res - 1) * 0.5f;
        var pixels = new Color32[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - cx) / maxR;
                float dy = (y - cy) / maxR;
                float r2 = dx * dx + dy * dy;
                if (r2 > 1f) { pixels[y * res + x] = new Color32(0, 0, 0, 0); continue; }
                float wide   = Mathf.Exp(-r2 / (2f * 0.40f * 0.40f));
                float bright = Mathf.Exp(-r2 / (2f * 0.18f * 0.18f)) * 0.65f;
                float edgeFade = 1f - r2;
                float v = Mathf.Clamp01((wide + bright) * edgeFade);
                byte a = (byte)(v * 255f);
                pixels[y * res + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }

    // Flat-top hexagon ghost: signed-distance hex with a smooth feathered
    // edge, a wide inner Gaussian, and a tighter bright core. Reads as a
    // proper aperture-blade artifact rather than a flat hex shape.
    static Sprite BuildHexSprite()
    {
        int res = kOrbSpriteRes;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "LensFlareHexSprite",
        };
        float cx = (res - 1) * 0.5f, cy = (res - 1) * 0.5f, maxR = (res - 1) * 0.5f;
        const float kApothem  = 0.82f;   // hexagon inscribed-circle radius in [0,1]
        const float kFeather  = 0.07f;   // edge softness
        var pixels = new Color32[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - cx) / maxR;
                float dy = (y - cy) / maxR;
                float r2 = dx * dx + dy * dy;
                if (r2 > 1f) { pixels[y * res + x] = new Color32(0, 0, 0, 0); continue; }
                // Flat-top hexagon edge distances (3 unique edge normals by symmetry).
                float e1 = Mathf.Abs(dy);
                float e2 = Mathf.Abs(dy * 0.5f + dx * 0.8660254f);
                float e3 = Mathf.Abs(dy * 0.5f - dx * 0.8660254f);
                float q  = Mathf.Max(e1, Mathf.Max(e2, e3));
                float edge = Mathf.Clamp01((kApothem - q) / kFeather);
                float wide   = Mathf.Exp(-r2 / (2f * 0.45f * 0.45f));
                float bright = Mathf.Exp(-r2 / (2f * 0.18f * 0.18f)) * 0.55f;
                float v = edge * (0.45f + 0.55f * wide + bright);
                byte a = (byte)(Mathf.Clamp01(v) * 255f);
                pixels[y * res + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }

    void FindSun()
    {
        if (_sunBody != null) return;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) { _sunBody = b; break; }
        }
    }

    // Returns 0..1 — fraction of sample points around the sun's silhouette
    // (plus one at the centre) that aren't blocked by world geometry.
    // 0 = fully occluded, 1 = fully clear, intermediate = partial cover.
    float ComputeOcclusionVisibility(Vector3 camPos, Vector3 sunPos, float sunRadius, Vector3 toSunDir, Vector3 camUp)
    {
        int total = kOcclusionEdgeSamples + 1;
        int clearCount = 0;

        // Centre sample.
        if (!IsSampleBlocked(camPos, sunPos)) clearCount++;

        // Edge samples on the great-circle perpendicular to the view
        // direction. At solar-system distances this is indistinguishable
        // from the true silhouette circle and is cheaper to compute.
        Vector3 right = Vector3.Cross(toSunDir, camUp);
        if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(toSunDir, Vector3.up);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, toSunDir).normalized;
        for (int i = 0; i < kOcclusionEdgeSamples; i++)
        {
            float a = (i / (float)kOcclusionEdgeSamples) * Mathf.PI * 2f;
            Vector3 sample = sunPos + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * sunRadius;
            if (!IsSampleBlocked(camPos, sample)) clearCount++;
        }
        return clearCount / (float)total;
    }

    // True if the ray from origin to target hits anything that isn't the
    // sun itself or the player's own collider. Ship hits within the canvas
    // plane distance are handled by the depth-buffer occlusion (the canvas
    // is in ScreenSpaceCamera mode), so we ignore those here to avoid
    // double-counting; ship hits farther than that (e.g. looking past a
    // ship 20m away toward the sun) still count as real obstructions.
    bool IsSampleBlocked(Vector3 origin, Vector3 target)
    {
        Vector3 d = target - origin;
        float distance = d.magnitude;
        if (distance < 0.001f) return false;
        Vector3 dir = d / distance;
        int n = Physics.RaycastNonAlloc(origin, dir, _hitBuf, distance, kOcclusionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = _hitBuf[i];
            var go = hit.collider != null ? hit.collider.gameObject : null;
            if (go == null) continue;
            if (go.CompareTag("Player")) continue;
            if (go.GetComponentInParent<PlayerController>() != null) continue;
            // Ship: ignore only when the hit is close enough that the
            // depth-occlusion already covers it (cockpit hull when
            // piloting, ship's own bulk when standing right next to it).
            // Use the canvas's CURRENT plane distance — while inside a ship
            // it's pushed to kInteriorPlaneDistance, and comparing against
            // the on-foot 5m here double-counted hull pieces 5-40m away
            // (raycast block + depth occlusion), so the flare pulsed as the
            // ship rotated even with the sun steady in the window.
            float depthCovered = _canvas != null ? _canvas.planeDistance : kCanvasPlaneDistance;
            if (go.GetComponentInParent<Ship>() != null && hit.distance < depthCovered) continue;
            var hitBody = go.GetComponentInParent<CelestialBody>();
            if (hitBody != null && hitBody == _sunBody) continue;
            return true;
        }
        // No collider blocked the ray — but the OCEANS have no colliders at all
        // (post-process water). Analytic segment-vs-sphere against each body's
        // ocean radius so water occludes the sun like terrain does.
        return OceanBlocks(origin, target);
    }

    float OceanRadiusOf(CelestialBody b)
    {
        if (_oceanRadii.TryGetValue(b, out float r)) return r;
        var gen = b.GetComponentInChildren<CelestialBodyGenerator>();
        r = gen != null ? gen.GetOceanRadius() : 0f;
        _oceanRadii[b] = r;
        return r;
    }

    // 1 above water; 0.5 the moment the camera submerges; 0 by ~2.5m down.
    float UnderwaterFlareDim(Vector3 camPos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return 1f;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || b == _sunBody) continue;
            float r = OceanRadiusOf(b);
            if (r <= 0f) continue;
            float depth = r - (b.Position - camPos).magnitude;
            if (depth > 0f) return 0.5f * (1f - Mathf.Clamp01(depth / 2.5f));
        }
        return 1f;
    }

    bool OceanBlocks(Vector3 origin, Vector3 target)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return false;
        Vector3 seg = target - origin;
        float len2 = seg.sqrMagnitude;
        if (len2 < 1e-6f) return false;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || b == _sunBody) continue;
            float r = OceanRadiusOf(b);
            if (r <= 0f) continue;
            Vector3 toC = b.Position - origin;

            // Camera IS inside this ocean: shallow = the sun still reads
            // through the surface; a few metres down it's gone. (The plain
            // segment test always hits from inside, so the flare used to pop
            // off the instant the player's head touched the water.)
            // Camera inside this ocean: the segment test always hits from
            // inside — submersion is handled by UnderwaterFlareDim (50% at
            // the waterline, gone with depth), so skip this body here.
            if (r - toC.magnitude > 0f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(toC, seg) / len2);
            Vector3 closest = seg * t - toC;
            if (closest.sqrMagnitude < r * r) return true;
        }
        return false;
    }

    // Create the flare-layer Images. Sibling order matters (earlier = drawn
    // first / behind): corona at the back, then the ghost orbs along the
    // axis, then the bright halo, then spikes on top so the cross stays
    // visible over everything.
    void EnsureFlareLayers()
    {
        if (_canvas == null || _sunBody == null) return;
        if (_sunImage != null) return;

        _coronaImage = CreateFlareLayer("Flare_Corona", _coronaSprite, kCoronaColor, out _coronaRT);

        _orbImages = new Image[kOrbs.Length];
        _orbRTs    = new RectTransform[kOrbs.Length];
        for (int i = 0; i < kOrbs.Length; i++)
        {
            Sprite s = (kOrbs[i].shape == OrbShape.Hex) ? _hexSprite : _orbSprite;
            _orbImages[i] = CreateFlareLayer("Flare_Orb_" + i, s, kOrbs[i].color, out _orbRTs[i]);
        }

        _sunImage    = CreateFlareLayer("Flare_Halo",   _haloSprite,   kHaloColor,   out _sunRT);

        _spikeImages = new Image[kSpikes.Length];
        _spikeRTs    = new RectTransform[kSpikes.Length];
        for (int i = 0; i < kSpikes.Length; i++)
            _spikeImages[i] = CreateFlareLayer("Flare_Spike_" + i, _rayBeamSprite, kSpikesColor, out _spikeRTs[i]);
    }

    Image CreateFlareLayer(string name, Sprite sprite, Color color, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        // maskable=false keeps any IMaterialModifier (Mask / RectMask2D) from
        // wrapping our additive material with a stencil-clipping clone that
        // silently drops the blend state.
        img.maskable = false;
        img.color = color;
        if (_additiveMat != null) img.material = _additiveMat;
        return img;
    }

    void HideFlare()
    {
        if (_coronaImage != null) _coronaImage.enabled = false;
        if (_sunImage != null)    _sunImage.enabled    = false;
        if (_spikeImages != null)
        {
            for (int i = 0; i < _spikeImages.Length; i++)
                if (_spikeImages[i] != null) _spikeImages[i].enabled = false;
        }
        if (_orbImages != null)
        {
            for (int i = 0; i < _orbImages.Length; i++)
                if (_orbImages[i] != null) _orbImages[i].enabled = false;
        }
    }

    void UpdateSun(Camera cam)
    {
        if (_sunBody == null) { FindSun(); EnsureFlareLayers(); }
        // No valid sun this frame → clear the flare instead of bailing with it
        // still on screen. Entering an interior (backrooms/poolrooms) unloads the
        // gameplay scene, destroying the sun: _sunBody becomes a destroyed-object
        // null and FindSun finds nothing (NBodySimulation.Bodies is empty there),
        // so without HideFlare the last-rendered halo froze on screen for the
        // whole visit. Hiding here also covers the sun simply being unavailable.
        if (_sunImage == null || _sunBody == null) { HideFlare(); return; }

        Vector3 sunPos = _sunBody.Position;
        Vector3 toSun = sunPos - cam.transform.position;
        Vector3 dir = toSun.normalized;
        float dot = Vector3.Dot(cam.transform.forward, dir);

        Vector3 sp = cam.WorldToScreenPoint(sunPos);
        // Behind the camera → no flare. Past screen bounds is NOT a hard
        // cull anymore — the fade factor below smoothly shrinks and dims
        // every element as the sun moves toward / past the edge of view,
        // so things don't pop off the screen when you turn away.
        bool inFront = sp.z > 0f;

        // Occlusion: sample N rays around the sun's silhouette circle plus
        // one at the centre. The unblocked fraction (0..1) becomes a
        // multiplier on fade, so an obstacle covering, say, 30% of the sun
        // dims the flare by ~30% instead of binary-toggling it off. A small
        // SmoothDamp on the fraction prevents flicker when an obstacle's
        // edge sweeps across sample points between frames.
        if (!inFront)
        {
            _cachedRawVis = 0f;            // sun behind camera → fade out immediately
        }
        else if (--_occlFrameCounter <= 0)
        {
            _occlFrameCounter = 3;
            _cachedRawVis = ComputeOcclusionVisibility(cam.transform.position, sunPos, _sunBody.radius, dir, cam.transform.up);
        }
        float rawVis = _cachedRawVis;
        _smoothedVisibility = Mathf.SmoothDamp(_smoothedVisibility, rawVis, ref _visibilityVelocity, kVisibilitySmoothTime);
        // Remap: below kVisibilityThreshold → 0, then linear ramp to 1.0 so
        // partial occlusion still feels proportional. Without this, 1-2
        // silhouette samples leaking through a gap in a tree/cabin collider
        // produces a faint persistent flare even when the sun is fully
        // hidden visually.
        float visibility = Mathf.InverseLerp(kVisibilityThreshold, 1f, _smoothedVisibility);

        // Submersion: dim to 50% the instant the camera goes under, then fade
        // to nothing with depth (matches the black hole / space dust rule).
        visibility *= UnderwaterFlareDim(cam.transform.position);

        float align = Mathf.Clamp01(dot);
        float fade  = Mathf.Pow(align, kHaloDotPowerForAlpha) * visibility;

        // Hide once the sun is behind the camera or the combined fade
        // (off-axis × occlusion) is below the cull threshold.
        if (!inFront || fade < kFadeCullThreshold) { HideFlare(); return; }

        Vector2 sunAnchored = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f);

        // Outer corona (back). Size lerps on fade (not align) so the corona
        // gracefully shrinks as the sun nears the camera's edge of view.
        _coronaImage.enabled = true;
        _coronaRT.anchoredPosition = sunAnchored;
        float coronaSize = Mathf.Lerp(kCoronaMinSizePx, kCoronaMaxSizePx, fade);
        _coronaRT.sizeDelta = new Vector2(coronaSize, coronaSize);
        var cc = kCoronaColor; cc.a = fade * kCoronaMaxAlpha;
        _coronaImage.color = cc;

        // Ghost orbs along the sun→screen-centre axis. Each orb's axisT
        // value (1 = sun, 0 = centre, -1 = mirrored) places it; size scales
        // with `fade` so the whole chain shrinks together as you turn away.
        if (_orbImages != null)
        {
            Vector2 centerPx = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 axisFromCenterToSun = new Vector2(sp.x, sp.y) - centerPx;
            float orbSizeMul = Mathf.Lerp(0.30f, 1.0f, fade);
            for (int i = 0; i < kOrbs.Length; i++)
            {
                var def = kOrbs[i];
                Vector2 pos = centerPx + axisFromCenterToSun * def.axisT;
                _orbRTs[i].anchoredPosition = new Vector2(pos.x - centerPx.x, pos.y - centerPx.y);
                float sz = def.size * orbSizeMul;
                _orbRTs[i].sizeDelta = new Vector2(sz, sz);
                var oc = def.color; oc.a = fade * def.alpha;
                _orbImages[i].color = oc;
                _orbImages[i].enabled = true;
            }
        }

        // Bright halo (middle).
        _sunImage.enabled = true;
        _sunRT.anchoredPosition = sunAnchored;
        float haloSize = Mathf.Lerp(kHaloMinSizePx, kHaloMaxSizePx, fade);
        _sunRT.sizeDelta = new Vector2(haloSize, haloSize);
        var hc = kHaloColor; hc.a = fade * kHaloMaxAlpha;
        _sunImage.color = hc;

        // Diffraction spikes (front). Each ray is its own Image so the
        // breath can change individual ray lengths over time. Big rays
        // (phase 0) and small rays (phase 180°) are paired inverse-phase,
        // so when the big ones shrink the small ones grow — a slow role
        // swap on the kSpikesBreathSpeed period.
        if (_spikeImages != null)
        {
            // Use the camera's LOCAL Euler-Z (= the roll the CameraTransformFX
            // module writes via localRotation = Euler(pitch, 0, roll)). World
            // eulerAngles.z does NOT equal local roll when pitch/yaw are
            // non-zero — that's what was making the spikes jump around
            // during strafe.
            float camRoll = cam.transform.localEulerAngles.z;
            float t = Time.time * kSpikesBreathSpeed;
            for (int i = 0; i < kSpikes.Length; i++)
            {
                var def = kSpikes[i];
                // breath ∈ [0, 1]; 0 = ray at its minLength + alpha floor,
                // 1 = ray at its maxLength + full alpha.
                float breath = 0.5f * (1f + Mathf.Sin(t + def.phaseDeg * Mathf.Deg2Rad));
                float lenRatio    = Mathf.Lerp(def.minLength, def.maxLength, breath);
                float breathAlpha = Mathf.Lerp(kSpikesBreathAlphaFloor, 1f, breath);

                _spikeRTs[i].anchoredPosition = sunAnchored;
                _spikeRTs[i].localRotation = Quaternion.Euler(0f, 0f, def.angleDeg + camRoll);
                float lenPx   = lenRatio * kSpikesMaxSizePx * fade;
                float thickPx = def.thicknessPx * fade;
                _spikeRTs[i].sizeDelta = new Vector2(lenPx, thickPx);

                var c = kSpikesColor;
                c.a = fade * kSpikesMaxAlpha * def.weight * breathAlpha;
                _spikeImages[i].color = c;
                _spikeImages[i].enabled = true;
            }
        }
    }
}
