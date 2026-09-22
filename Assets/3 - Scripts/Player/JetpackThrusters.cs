using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Flames from the astronaut's jetpack nozzles (Sam, 2026-09-15).
///
/// <b>Setup — no wiring.</b> Anywhere under the player (the jetpack mesh is the
/// natural spot) add GameObjects with <c>thruster</c> anywhere in the name
/// (any case) and point each one's blue (+Z) arrow the way the flame should
/// shoot. That is the whole setup; which nozzle fires is worked out from geometry:
///
///   • <b>Linear</b> — a nozzle fires when its exhaust direction opposes the
///     thrust the pack is applying (<see cref="PlayerController.ThrustLocal"/>):
///     the two on the bottom pointing down fire on the up boost, a top one
///     pointing up fires on the down thrust, a left-pointing one fires when
///     you boost right, and so on.
///   • <b>RCS</b> — while airborne, a nozzle fires when the torque it would
///     produce about the pack (its position × its thrust) lines up with the way
///     the body is turning (<see cref="PlayerController.FreeLookRateDeg"/>).
///     Inside the atmosphere that is yaw only (mouse left/right — your body
///     stays upright, so no pitch/roll nozzles); past the atmosphere line
///     (free-float) mouse pitch and Q/E roll join in. Never on the ground.
///
/// Optional words in the name: <c>Main</c> = linear only (the big bottom
/// engines never puff for a turn); <c>RCS</c> = rotation only. Anything else
/// does both. Several nozzles may share a name.
///
/// The flame is a small procedural particle jet built at runtime (stretched
/// additive sprites, short lifetime, local simulation space so the floating
/// origin can't tear it). Emission ramps up and down rather than snapping.
///
/// <b>Look.</b> Every renderer under the <c>Backpack</c> bone (nozzle bells, the
/// pack extension) is moved onto the suit's layer at scan time, so the pieces
/// are lit by the astronaut's own reflect light like the suit, and
/// <see cref="PlayerShadowProxy"/> is rebuilt so they cast the same
/// shadows-only copy the body does.
/// Attached automatically on every gameplay scene load (same sceneLoaded hook
/// as PlayerShadowProxy, so it exists in builds that boot through the menu).
/// </summary>
[DefaultExecutionOrder(160)]
public class JetpackThrusters : MonoBehaviour
{
    [Header("Flame look")]
    [Tooltip("Particles per second from a nozzle at full burn.")]
    public float maxEmissionRate = 1800f;
    [Tooltip("Metres per second the flame particles leave the nozzle at full burn.")]
    public float flameSpeed = 9f;
    [Tooltip("Seconds a flame particle lives — with flameSpeed this sets how long the jet is (0.2 s × 9 m/s ≈ 1.8 m).")]
    public float flameLifetime = 0.2f;
    [Tooltip("Metres across a flame particle at birth (it shrinks to a quarter by the end).")]
    public float flameSize = 0.17f;
    [Tooltip("Half-angle of the jet cone, degrees.")]
    public float coneAngle = 8f;
    [Tooltip("Metres along the nozzle's +Z the flame starts — the mouth of the bell. Particles are plain round sprites drawn AT their position (no velocity stretch), so nothing is ever drawn behind this point.")]
    public float nozzleMouthOffset = 0.09f;
    public Color coreColor = new Color(1f, 0.95f, 0.75f, 1f);
    public Color tailColor = new Color(1f, 0.45f, 0.08f, 0f);

    [Header("Smoke")]
    [Tooltip("Smoke puffs per second at full burn. 0 = no smoke.")]
    public float smokeRate = 45f;
    [Tooltip("Seconds a smoke puff lingers.")]
    public float smokeLifetime = 1.1f;
    [Tooltip("Metres across a puff at birth; it grows to about 3x.")]
    public float smokeSize = 0.14f;
    public Color smokeColor = new Color(0.55f, 0.55f, 0.58f, 0.35f);

    [Header("Glow (one real light for the whole pack)")]
    [Tooltip("Orange point light that comes on while any nozzle burns, so the ground, the suit and the grass catch the flame. ONE light for the whole pack, on only while thrusting — every real light costs an extra draw of the planet mesh, so this is kept to a single one. Off = no light at all.")]
    public bool glowEnabled = true;
    public Color glowColor = new Color(1f, 0.55f, 0.15f);
    [Tooltip("Light intensity at full burn.")]
    public float glowIntensity = 2.2f;
    [Tooltip("Light range in metres.")]
    public float glowRange = 7f;
    [Tooltip("How much of the light reaches the (faked) grass lighting. Same dial the lanterns use.")]
    public float glowGrassStrength = 0.6f;
    [Tooltip("Random intensity flicker, as a fraction (0.15 = ±15%).")]
    [Range(0f, 0.5f)] public float glowFlicker = 0.15f;

    [Header("Response")]
    [Tooltip("How fast a flame ramps up when its thrust starts (1/s).")]
    public float attackPerSecond = 18f;
    [Tooltip("How fast a flame dies when its thrust stops (1/s).")]
    public float releasePerSecond = 9f;
    [Tooltip("A nozzle only fires when its direction agrees with the thrust at least this much (0 = any sliver of agreement, 1 = dead on). Stops every nozzle puffing a little for every input.")]
    [Range(0f, 0.9f)] public float agreementDeadzone = 0.3f;

    [Header("RCS (free-float turning)")]
    [Tooltip("Body turn rate (deg/s) at which the RCS flames reach full burn.")]
    public float rcsFullRateDeg = 90f;
    [Tooltip("Turns slower than this (deg/s) light nothing.")]
    public float rcsMinRateDeg = 4f;
    [Tooltip("Turning flames at their strongest are this fraction of a full boost flame (Sam: a third — a nudge of the mouse shouldn't look like a boost).")]
    [Range(0f, 1f)] public float rcsFlameScale = 0.33f;
    [Tooltip("Local point (player frame) the body is treated as turning about for the flame logic. Deliberately IN the pack, not at the real centre of mass: with the true waist pivot the side nozzles (38 cm behind the waist) swing the nose for a yaw, which is right but reads wrong. In the pack plane the sides only roll and yaw comes from the diagonal front/back pair, which is what a viewer expects.")]
    public Vector3 rotationPivot = new Vector3(0f, 0.05f, -0.38f);
    [Tooltip("Lever arm (metres) at which a nozzle's turning torque counts as full. Shorter levers fire proportionally less, so a nozzle a few centimetres off the pivot plane doesn't blaze for a turn it barely affects.")]
    public float leverReference = 0.25f;

    class Nozzle
    {
        public Transform t;
        public ParticleSystem ps;
        public ParticleSystem.EmissionModule emission;
        public ParticleSystem.MainModule main;
        public ParticleSystem smoke;
        public ParticleSystem.EmissionModule smokeEmission;
        public bool linear, rcs;
        public float level;
    }

    readonly List<Nozzle> _nozzles = new List<Nozzle>();
    PlayerController _pc;
    Light _glow;
    GrassPointLight _glowMarker;
    float _glowLevel;
    float _nextScan;
    static Material _flameMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        var pc = FindObjectOfType<PlayerController>(true);
        if (pc == null) return;
        if (pc.GetComponent<JetpackThrusters>() == null) pc.gameObject.AddComponent<JetpackThrusters>();
    }

    void Awake() { _pc = GetComponent<PlayerController>(); }
    void Start() { Rescan(); }
    void OnDestroy() { Clear(); }

    /// <summary>Find every "Thruster*" transform under the player and (re)build
    /// its flame. Call again after adding nozzles in Play mode.</summary>
    [ContextMenu("Rescan nozzles")]
    public void Rescan()
    {
        Clear();
        MatchPackPiecesToSuit();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform || t.name.StartsWith("~")) continue;
            if (t.name.IndexOf("thruster", StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool main = t.name.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0;
            bool rcsOnly = t.name.IndexOf("RCS", StringComparison.OrdinalIgnoreCase) >= 0;
            var n = new Nozzle { t = t, linear = !rcsOnly, rcs = !main };
            n.ps = BuildFlame(t);
            n.emission = n.ps.emission;
            n.main = n.ps.main;
            n.smoke = BuildSmoke(n.ps.transform);
            if (n.smoke != null) n.smokeEmission = n.smoke.emission;
            _nozzles.Add(n);
        }
    }

    /// <summary>Put every hand-placed renderer under the Backpack bone on the
    /// suit body's layer (they're authored on Default in the prefab, the suit's
    /// layer is a scene override), then rebuild the body's shadow proxies so the
    /// new pieces cast shadows too. Skips proxies/flames ("~" names).</summary>
    void MatchPackPiecesToSuit()
    {
        SkinnedMeshRenderer body = null;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (!smr.name.StartsWith("~")) { body = smr; break; }
        if (body == null) return;
        int suitLayer = body.gameObject.layer;
        bool changed = false;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Backpack") continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("~") || r is ParticleSystemRenderer) continue;
                if (r.gameObject.layer == suitLayer) continue;
                r.gameObject.layer = suitLayer;
                changed = true;
            }
        }
        if (changed)
        {
            var proxy = GetComponent<PlayerShadowProxy>();
            if (proxy != null) proxy.Rebuild();
        }
    }

    void Clear()
    {
        foreach (var n in _nozzles)
            if (n.ps != null) Destroy(n.ps.gameObject);
        _nozzles.Clear();
        if (_glow != null) { Destroy(_glow.gameObject); _glow = null; _glowMarker = null; }
        _glowLevel = 0f;
    }

    void LateUpdate()
    {
        if (_nozzles.Count == 0)
        {
            // Nozzles may not exist yet (or Sam is adding them live) — look again now and then.
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; Rescan(); }
            return;
        }
        if (_pc == null) return;
        SyncSmokeSpace();
        // Turning nozzles only make sense off the ground (on the ground the
        // mouse turns you with your feet, not the pack).
        bool airborne = !_pc.IsOnGround && !_pc.IsSwimming;

        Vector3 thrust = _pc.ThrustLocal;
        float thrustMag = thrust.magnitude;
        Vector3 thrustDir = thrustMag > 0.01f ? thrust / thrustMag : Vector3.zero;

        Vector3 omega = airborne ? _pc.FreeLookRateDeg : Vector3.zero;
        float omegaMag = omega.magnitude;
        Vector3 omegaDir = omegaMag > rcsMinRateDeg ? omega / omegaMag : Vector3.zero;
        float omegaStrength = Mathf.Clamp01((omegaMag - rcsMinRateDeg) / Mathf.Max(1f, rcsFullRateDeg - rcsMinRateDeg)) * rcsFlameScale;

        float dt = Time.deltaTime;
        Vector3 pivotLocal = rotationPivot;
        float peak = 0f;

        for (int i = 0; i < _nozzles.Count; i++)
        {
            var n = _nozzles[i];
            if (n.t == null || n.ps == null) continue;

            // Exhaust direction in the player's frame; the thrust it gives is the opposite.
            Vector3 exhaust = transform.InverseTransformDirection(n.t.forward);
            float target = 0f;

            if (n.linear && thrustMag > 0.01f)
                target = Agreement(Vector3.Dot(-exhaust, thrustDir));

            if (n.rcs && omegaDir != Vector3.zero)
            {
                // Torque per unit thrust = lever x force; NOT normalised, so a
                // nozzle with a 6 cm lever scores a quarter of one with 25 cm.
                Vector3 r = transform.InverseTransformPoint(n.t.position) - pivotLocal;
                Vector3 torque = Vector3.Cross(r, -exhaust);
                float score = Mathf.Clamp01(Vector3.Dot(torque, omegaDir) / Mathf.Max(0.01f, leverReference));
                target = Mathf.Max(target, Agreement(score) * omegaStrength);
            }

            float k = 1f - Mathf.Exp(-dt * (target > n.level ? attackPerSecond : releasePerSecond));
            n.level = Mathf.Lerp(n.level, target, k);
            if (n.level < 0.005f) n.level = 0f;

            n.emission.rateOverTime = n.level * maxEmissionRate;
            n.main.startSpeed = flameSpeed * (0.5f + 0.5f * n.level);
            if (n.smoke != null) n.smokeEmission.rateOverTime = n.level * smokeRate;
            if (n.level > peak) peak = n.level;
        }
        UpdateGlow(peak, dt);
    }

    // The pack light follows the brightest nozzle. It sits at the pack pivot
    // (between the nozzles) rather than on any one bell so the same light
    // serves every direction of burn. Same recipe as FireflyGlow: a point
    // light that ignores the suit's own layer (a light inside the pack would
    // light the arms from the inside) plus the GrassPointLight marker.
    void UpdateGlow(float peak, float dt)
    {
        if (!glowEnabled) { if (_glow != null && _glow.enabled) _glow.enabled = false; return; }
        if (_glow == null)
        {
            var go = new GameObject("~jetpack glow");
            go.transform.SetParent(transform, false);
            _glow = go.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.shadows = LightShadows.None;
            _glow.intensity = 0f;
            _glow.enabled = false;
            int reflect = LayerMask.NameToLayer("PlayerReflect");
            if (reflect >= 0) _glow.cullingMask = ~(1 << reflect);
            _glowMarker = go.AddComponent<GrassPointLight>();
        }
        _glow.transform.localPosition = rotationPivot;
        _glowLevel = Mathf.Lerp(_glowLevel, peak, 1f - Mathf.Exp(-dt * (peak > _glowLevel ? attackPerSecond : releasePerSecond)));
        if (_glowLevel < 0.01f)
        {
            _glowLevel = 0f;
            if (_glow.enabled) _glow.enabled = false;
            return;
        }
        float flick = 1f + glowFlicker * (Mathf.PerlinNoise(Time.time * 23f, 0.37f) * 2f - 1f);
        _glow.color = glowColor;
        _glow.range = glowRange;
        _glow.intensity = glowIntensity * _glowLevel * flick;
        if (_glowMarker != null) _glowMarker.grassStrength = glowGrassStrength;
        if (!_glow.enabled) _glow.enabled = true;
    }

    float Agreement(float dot)
    {
        if (dot <= agreementDeadzone) return 0f;
        return Mathf.InverseLerp(agreementDeadzone, 1f, dot);
    }

    ParticleSystem BuildFlame(Transform nozzle)
        => BuildFlame(nozzle, flameLifetime, flameSpeed, flameSize, coneAngle, nozzleMouthOffset, coreColor, tailColor);

    /// <summary>The jetpack's flame, reusable by anything with a nozzle — the
    /// rover's side thrusters are built with this exact call (2026-09-22).
    /// Emits along the nozzle's +Z; the caller drives emission.rateOverTime.</summary>
    public static ParticleSystem BuildFlame(Transform nozzle, float flameLifetime, float flameSpeed, float flameSize,
                                            float coneAngle, float nozzleMouthOffset, Color coreColor, Color tailColor)
    {
        var go = new GameObject("~flame " + nozzle.name);
        go.transform.SetParent(nozzle, false);
        go.layer = 0;   // Default: unlit, and visible to every camera the body is
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(flameLifetime * 0.6f, flameLifetime);
        main.startSpeed = flameSpeed;
        main.startSize = new ParticleSystem.MinMaxCurve(flameSize * 0.6f, flameSize);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;   // floating-origin safe
        main.scalingMode = ParticleSystemScalingMode.Local;           // metres, whatever the rig's scale
        main.maxParticles = 600;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;   // emits along the nozzle's +Z
        shape.angle = coneAngle;
        shape.radius = 0.02f;
        shape.radiusThickness = 1f;
        shape.position = Vector3.forward * nozzleMouthOffset;   // born at the mouth, not inside the bell

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(coreColor, 0f), new GradientColorKey(tailColor, 0.55f), new GradientColorKey(tailColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.35f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.25f)));

        // Plain billboards, not velocity-stretched streaks: a streak is drawn
        // centred on its particle, so its tail reached back through the bell
        // and out the far side — which read as fire from the neighbouring
        // nozzle when two bells sit close together facing opposite ways.
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = FlameMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    // Slow grey puffs that hang in the AIR — simulated in the reference
    // planet's frame (custom simulation space = the body's transform), not the
    // world's. The planets are on moving rails, so world-space puffs were left
    // behind in inertial space and streaked off into the sky the moment they
    // were born. In the planet's frame they sit still and drift, and they trail
    // behind a moving astronaut because the emitter moves and they don't.
    // The space is re-pointed every frame in LateUpdate (SyncSmokeSpace) as the
    // player crosses from one body's influence to the next.
    ParticleSystem BuildSmoke(Transform flame)
        => BuildSmoke(flame, SmokeSpace(), smokeRate, smokeLifetime, smokeSize, smokeColor, flameSpeed, coneAngle, nozzleMouthOffset);

    /// <summary>The jetpack's smoke puffs, simulated in <paramref name="smokeSpace"/>
    /// (the reference planet's transform — see the note above).</summary>
    public static ParticleSystem BuildSmoke(Transform flame, Transform smokeSpace, float smokeRate, float smokeLifetime,
                                            float smokeSize, Color smokeColor, float flameSpeed, float coneAngle, float nozzleMouthOffset)
    {
        if (smokeRate <= 0f) return null;
        var go = new GameObject("~smoke");
        go.transform.SetParent(flame, false);
        go.layer = 0;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime * 0.7f, smokeLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(flameSpeed * 0.25f, flameSpeed * 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeSize * 0.7f, smokeSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = smokeColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Custom;
        main.customSimulationSpace = smokeSpace;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle + 8f;
        shape.radius = 0.03f;
        shape.position = Vector3.forward * (nozzleMouthOffset + 0.25f);   // starts where the flame thins out

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 3f)));

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        var lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.25f;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = SmokeMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingFudge = 10f;   // draw behind the flame

        ps.Play();
        return ps;
    }

    Transform _smokeSpace;

    /// <summary>The frame smoke lives in: the reference planet, else the player
    /// itself (deep space / no body yet — puffs then simply ride along).</summary>
    Transform SmokeSpace()
    {
        var body = _pc != null ? _pc.ReferenceBody : null;
        return body != null ? body.transform : transform;
    }

    void SyncSmokeSpace()
    {
        Transform want = SmokeSpace();
        if (want == _smokeSpace) return;
        _smokeSpace = want;
        for (int i = 0; i < _nozzles.Count; i++)
        {
            var n = _nozzles[i];
            if (n.smoke == null) continue;
            var m = n.smoke.main;
            m.customSimulationSpace = want;
            n.smoke.Clear();   // old puffs were in the old frame; drop them rather than teleport them
        }
    }

    static Material _smokeMat;
    static Material SmokeMaterial()
    {
        if (_smokeMat != null) return _smokeMat;
        // Alpha-blended, always in builds; the same soft glow texture makes a fine puff.
        _smokeMat = new Material(Shader.Find("Sprites/Default")) { name = "JetpackSmoke" };
        _smokeMat.mainTexture = GlowTexture();
        return _smokeMat;
    }

    // Same shader ladder the death cutscene / concert beams use; Sprites/Default
    // is always in builds so the flame never goes magenta, just less glowy.
    static Material FlameMaterial()
    {
        if (_flameMat != null) return _flameMat;
        Shader sh = Shader.Find("Particles/Additive")
                 ?? Shader.Find("Legacy Shaders/Particles/Additive")
                 ?? Shader.Find("Mobile/Particles/Additive")
                 ?? Shader.Find("Sprites/Default");
        _flameMat = new Material(sh) { name = "JetpackFlame" };
        if (_flameMat.HasProperty("_TintColor")) _flameMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        _flameMat.mainTexture = GlowTexture();
        return _flameMat;
    }

    static Texture2D GlowTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "JetpackFlameGlow", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }
}
