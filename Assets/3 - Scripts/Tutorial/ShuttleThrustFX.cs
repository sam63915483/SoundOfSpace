using UnityEngine;

// Retro-thruster fire + light show for the shuttle landing (2026-07). Runtime-
// built (own additive material, soft particle texture, ParticleSystems, lights)
// so the hand-maintained Shuttle_Lander prefab needs NO authoring — everything
// anchors to transforms found by name. Three plume tiers:
//   Nozzle_1..4 (bottom ring)   — STABILIZERS: random long spurts (1-3s on,
//                                 1-3s off, per nozzle) for the ENTIRE descent,
//                                 long visible jets.
//   ThrusterNozzle*_1..4 (pods) — correction bursts: quick out-of-sync blasts,
//                                 lit from "Engaging reverse thrusters."
//   EngineFlare / EngineBell    — the BIG central plume (wide cone), lit at the
//                                 retro-burn, ramping to a massive fireball
//                                 near the ground.
// Plus a downward spot light + hull-glow + per-pod flicker lights so the
// ground visibly lights up on approach.
//
// Driven by ShuttleArrivalSequence:
//   Initialize(root)  — build everything; STABILIZERS begin immediately
//   Ignite()          — at "Engaging reverse thrusters." (engine + pods)
//   SetAltitude(m)    — every LateUpdate; drives plume power + ground light
//   Shutdown()        — touchdown: everything collapses over ~0.8s
//   StopImmediate()   — skip path / teardown
public class ShuttleThrustFX : MonoBehaviour
{
    [Header("Engine plume (the big one; 2x-size rework 2026-07-24)")]
    public float engineRate      = 500f;
    public float engineLifetime  = 0.85f;
    public float engineConeAngle = 21f;
    public float engineSizeMul   = 11f;    // 2x the original 5.5
    public float engineSpeedMul  = 45f;
    [Tooltip("Extra plume power as the ground nears: 1 at fireballStartAlt, up to this multiplier at 0m.")]
    public float fireballBoost   = 2.4f;
    public float fireballStartAlt = 60f;

    [Header("Stabilizer nozzles (bottom ring; whole descent)")]
    public float stabRate     = 220f;
    public float stabLifetime = 0.55f;
    public float stabSizeMul  = 3.2f;
    public float stabSpeedMul = 40f;
    public Vector2 stabFireSeconds = new Vector2(1f, 3f);   // spurt length
    public Vector2 stabGapSeconds  = new Vector2(1f, 3f);   // silence between spurts

    [Header("Pod correction nozzles (quick bursts after ignition)")]
    public float podRate     = 170f;
    public float podLifetime = 0.35f;
    public float podSizeMul  = 2.2f;
    public float podSpeedMul = 28f;

    [Header("Lights")]
    public float spotRange       = 160f;
    public float spotAngle       = 62f;
    public float spotMaxIntensity = 14f;
    [Tooltip("Ground starts catching light below this altitude; full blaze near 0.")]
    public float groundLightAlt  = 90f;
    public float glowMaxIntensity = 6f;
    public float podLightIntensity = 4f;

    [Header("Shutdown")]
    public float dieOutTime = 0.8f;

    Transform _root;
    Transform _engineAnchor;
    Transform[] _stabNozzles;
    Transform[] _podNozzles;
    ParticleSystem _enginePS;
    ParticleSystem[] _stabPS;
    ParticleSystem[] _podPS;
    Light _spot, _glow;
    Light[] _podLights;
    Transform[] _podLightAnchors;
    Material _mat;
    Texture2D _tex;
    float _unit = 1f;
    float _altitude = 9999f;

    bool _stabOn;      // stabilizers live (from Initialize until shutdown completes)
    bool _ignited;     // engine + pod bursts live (from Ignite())
    bool _dying;       // Shutdown() called: everything fades over dieOutTime
    float _dieT;

    float[] _stabTimer; bool[] _stabFiring;
    float[] _podTimer;  bool[] _podFiring;

    enum PlumeKind { Engine, Stab, Pod }

    public void Initialize(Transform shuttleRoot)
    {
        _root = shuttleRoot;

        var stabs = new System.Collections.Generic.List<Transform>();
        var podNoz = new System.Collections.Generic.List<Transform>();
        var pods = new System.Collections.Generic.List<Transform>();
        Transform bell = null, flare = null;
        foreach (var t in shuttleRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("Nozzle_")) stabs.Add(t);
            else if (t.name.StartsWith("ThrusterNozzle")) podNoz.Add(t);
            else if (t.name.StartsWith("ThrusterPod_")) pods.Add(t);
            else if (t.name == "EngineBell") bell = t;
            else if (t.name == "EngineFlare") flare = t;
        }
        _engineAnchor = flare != null ? flare : (bell != null ? bell : shuttleRoot);
        _stabNozzles = stabs.ToArray();
        _podNozzles = podNoz.ToArray();
        _podLightAnchors = pods.ToArray();

        var ls = _engineAnchor.lossyScale;
        _unit = Mathf.Max(0.05f, (ls.x + ls.y + ls.z) / 3f);

        BuildMaterial();

        _enginePS = BuildPlume("EngineFlame", _engineAnchor.position, PlumeKind.Engine);

        _stabPS = new ParticleSystem[_stabNozzles.Length];
        _stabTimer = new float[_stabNozzles.Length];
        _stabFiring = new bool[_stabNozzles.Length];
        for (int i = 0; i < _stabNozzles.Length; i++)
        {
            _stabPS[i] = BuildPlume("StabFlame_" + i, _stabNozzles[i].position, PlumeKind.Stab);
            _stabTimer[i] = Random.Range(0f, stabGapSeconds.y);   // desynced from frame one
        }

        _podPS = new ParticleSystem[_podNozzles.Length];
        _podTimer = new float[_podNozzles.Length];
        _podFiring = new bool[_podNozzles.Length];
        for (int i = 0; i < _podNozzles.Length; i++)
        {
            _podPS[i] = BuildPlume("PodFlame_" + i, _podNozzles[i].position, PlumeKind.Pod);
            _podTimer[i] = Random.Range(0f, 0.35f);
        }

        BuildLights();
        SetAllDark();
        _stabOn = true;   // stabilizing thrust runs the whole way down
    }

    public void Ignite()
    {
        _ignited = true;
    }

    public void SetAltitude(float altitude) { _altitude = altitude; }

    public void Shutdown()
    {
        if (_dying) return;
        _dying = true;
        _dieT = 0f;
    }

    public void StopImmediate()
    {
        _stabOn = false;
        _ignited = false;
        _dying = false;
        SetAllDark();
    }

    void SetAllDark()
    {
        SetPlume(_enginePS, 0f, PlumeKind.Engine);
        if (_stabPS != null) foreach (var p in _stabPS) SetPlume(p, 0f, PlumeKind.Stab);
        if (_podPS != null) foreach (var p in _podPS) SetPlume(p, 0f, PlumeKind.Pod);
        if (_spot != null) _spot.intensity = 0f;
        if (_glow != null) _glow.intensity = 0f;
        if (_podLights != null) foreach (var l in _podLights) if (l != null) l.intensity = 0f;
    }

    void LateUpdate()
    {
        if (_root == null || _enginePS == null) return;

        Vector3 down = -_root.up;
        Quaternion aim = Quaternion.LookRotation(down, _root.forward);
        _enginePS.transform.position = _engineAnchor.position;
        _enginePS.transform.rotation = aim;
        for (int i = 0; i < _stabPS.Length; i++)
        {
            if (_stabNozzles[i] == null || _stabPS[i] == null) continue;
            _stabPS[i].transform.position = _stabNozzles[i].position;
            _stabPS[i].transform.rotation = aim;
        }
        for (int i = 0; i < _podPS.Length; i++)
        {
            if (_podNozzles[i] == null || _podPS[i] == null) continue;
            _podPS[i].transform.position = _podNozzles[i].position;
            _podPS[i].transform.rotation = aim;
        }

        if (!_stabOn && !_ignited) return;

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
        float power = 1f;
        if (_dying)
        {
            _dieT += dt / Mathf.Max(0.01f, dieOutTime);
            power = 1f - Mathf.Clamp01(_dieT);
            if (_dieT >= 1f) { StopImmediate(); return; }
        }

        // ── Stabilizers: long random spurts, whole descent ──
        if (_stabOn)
        {
            for (int i = 0; i < _stabPS.Length; i++)
            {
                _stabTimer[i] -= dt;
                if (_stabTimer[i] <= 0f)
                {
                    _stabFiring[i] = !_stabFiring[i];
                    _stabTimer[i] = _stabFiring[i]
                        ? Random.Range(stabFireSeconds.x, stabFireSeconds.y)
                        : Random.Range(stabGapSeconds.x, stabGapSeconds.y);
                }
                SetPlume(_stabPS[i], _stabFiring[i] ? power * Random.Range(0.9f, 1.1f) : 0f, PlumeKind.Stab);
            }
        }

        if (_ignited)
        {
            // ── Engine plume: steady burn, swelling into a fireball near ground ──
            float proximity = 1f - Mathf.Clamp01(_altitude / Mathf.Max(1f, fireballStartAlt));
            float enginePower = power * Mathf.Lerp(1f, fireballBoost, proximity * proximity);
            float surge = 0.88f + 0.24f * Mathf.PerlinNoise(Time.unscaledTime * 2.3f, 0.37f);
            SetPlume(_enginePS, enginePower * surge, PlumeKind.Engine);

            // ── Pod correction nozzles: quick hard bursts, out of sync ──
            for (int i = 0; i < _podPS.Length; i++)
            {
                _podTimer[i] -= dt;
                if (_podTimer[i] <= 0f)
                {
                    _podFiring[i] = !_podFiring[i];
                    _podTimer[i] = _podFiring[i] ? Random.Range(0.10f, 0.35f) : Random.Range(0.15f, 0.65f);
                }
                SetPlume(_podPS[i], _podFiring[i] ? power * Random.Range(0.85f, 1.15f) : 0f, PlumeKind.Pod);
            }

            // ── Lights: flicker + ground illumination with proximity ──
            float groundK = 1f - Mathf.Clamp01(_altitude / Mathf.Max(1f, groundLightAlt));
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(Time.unscaledTime * 9f, 4.2f);
            if (_spot != null)
            {
                _spot.intensity = spotMaxIntensity * enginePower * flick * Mathf.Lerp(0.25f, 1f, groundK);
                _spot.transform.position = _engineAnchor.position;
                _spot.transform.rotation = aim;
            }
            if (_glow != null)
            {
                _glow.intensity = glowMaxIntensity * enginePower * flick;
                _glow.transform.position = _engineAnchor.position + down * (0.5f * _unit);
            }
            if (_podLights != null)
            {
                bool anyFiring = false;
                for (int j = 0; j < _podFiring.Length; j++) if (_podFiring[j]) { anyFiring = true; break; }
                for (int i = 0; i < _podLights.Length; i++)
                {
                    if (_podLights[i] == null) continue;
                    float podFlick = 0.6f + 0.4f * Mathf.PerlinNoise(Time.unscaledTime * 11f, i * 1.7f);
                    _podLights[i].intensity = anyFiring ? podLightIntensity * power * podFlick
                                                        : podLightIntensity * power * 0.15f;
                }
            }
        }
    }

    void SetPlume(ParticleSystem ps, float power, PlumeKind kind)
    {
        if (ps == null) return;
        float rate, sizeMul, speedMul;
        switch (kind)
        {
            case PlumeKind.Engine:
                rate = engineRate;
                // grows sub-linearly to full, then keeps swelling with fireball power
                sizeMul  = engineSizeMul * Mathf.Clamp01(power * 0.6f + 0.4f * Mathf.Clamp01(power)) * Mathf.Max(1f, power * 0.75f);
                speedMul = engineSpeedMul * Mathf.Clamp01(power);
                break;
            case PlumeKind.Stab:
                rate = stabRate;
                sizeMul  = stabSizeMul * Mathf.Clamp01(power);
                speedMul = stabSpeedMul * Mathf.Clamp01(power);
                break;
            default:
                rate = podRate;
                sizeMul  = podSizeMul * Mathf.Clamp01(power);
                speedMul = podSpeedMul * Mathf.Clamp01(power);
                break;
        }
        var em = ps.emission;
        em.rateOverTime = rate * Mathf.Clamp01(power);
        var main = ps.main;
        main.startSizeMultiplier  = _unit * sizeMul + 1e-4f;
        main.startSpeedMultiplier = _unit * speedMul + 1e-4f;
    }

    // ── Construction ─────────────────────────────────────────────────────────
    void BuildMaterial()
    {
        Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh == null) sh = Shader.Find("Particles/Additive");
        if (sh == null) sh = Shader.Find("Mobile/Particles/Additive");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        _mat = new Material(sh);

        const int S = 64;
        _tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 c = new Vector2((S - 1) * 0.5f, (S - 1) * 0.5f);
        float maxR = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / maxR;
                float a = Mathf.Clamp01(1f - d);
                a = a * a;
                _tex.SetPixel(x, y, new Color(a, a, a, a));
            }
        _tex.Apply();
        _mat.mainTexture = _tex;
    }

    ParticleSystem BuildPlume(string name, Vector3 pos, PlumeKind kind)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, true);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.startLifetime = kind == PlumeKind.Engine ? engineLifetime
                           : kind == PlumeKind.Stab ? stabLifetime : podLifetime;
        main.startSpeed = 1f;
        main.startSize  = 1f;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.maxParticles = kind == PlumeKind.Engine ? 900 : 300;
        main.playOnAwake = false;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = kind == PlumeKind.Engine ? engineConeAngle : 10f;
        shape.radius = Mathf.Max(0.001f, _unit * (kind == PlumeKind.Engine ? 0.55f : 0.12f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.97f, 0.72f), 0f),
                new GradientColorKey(new Color(1f, 0.50f, 0.10f), 0.40f),
                new GradientColorKey(new Color(0.65f, 0.10f, 0.02f), 1f),
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var curve = new AnimationCurve(
            new Keyframe(0f, 0.45f), new Keyframe(0.3f, 1f), new Keyframe(1f, kind == PlumeKind.Engine ? 0.35f : 0.15f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = _mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    void BuildLights()
    {
        _spot = NewLight("ThrustSpot", LightType.Spot, _engineAnchor);
        _spot.range = spotRange;
        _spot.spotAngle = spotAngle;
        _spot.color = new Color(1f, 0.62f, 0.25f);
        _spot.transform.rotation = Quaternion.LookRotation(-_root.up, _root.forward);

        _glow = NewLight("ThrustGlow", LightType.Point, _engineAnchor);
        _glow.range = 14f * _unit;
        _glow.color = new Color(1f, 0.55f, 0.2f);

        _podLights = new Light[_podLightAnchors.Length];
        for (int i = 0; i < _podLightAnchors.Length; i++)
        {
            _podLights[i] = NewLight("PodThrustLight_" + i, LightType.Point, _podLightAnchors[i]);
            _podLights[i].range = 5f * _unit;
            _podLights[i].color = new Color(1f, 0.58f, 0.22f);
        }
    }

    Light NewLight(string name, LightType type, Transform anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(anchor, false);
        var l = go.AddComponent<Light>();
        l.type = type;
        l.intensity = 0f;
        l.shadows = LightShadows.None;
        return l;
    }

    void OnDestroy()
    {
        if (_enginePS != null) Destroy(_enginePS.gameObject);
        if (_stabPS != null) foreach (var p in _stabPS) if (p != null) Destroy(p.gameObject);
        if (_podPS != null) foreach (var p in _podPS) if (p != null) Destroy(p.gameObject);
        if (_spot != null) Destroy(_spot.gameObject);
        if (_glow != null) Destroy(_glow.gameObject);
        if (_podLights != null) foreach (var l in _podLights) if (l != null) Destroy(l.gameObject);
        if (_mat != null) Destroy(_mat);
        if (_tex != null) Destroy(_tex);
    }
}
