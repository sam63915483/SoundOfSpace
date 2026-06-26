using UnityEngine;

// Thruster flame VFX for the four nozzle cylinders at the bottom of the stasis pod.
// Driven by PodArrivalSequence through the descent:
//   BeginIdleBursts() — calm approach: each nozzle randomly puffs short bursts.
//   SetFullPower()     — "reverse thrusters engaged": all four fire a steady hard jet.
//   BeginDieOut()      — fired the instant the pod stops slowing (thrusters fail):
//                        flames + nozzles shrink, then settle into small random spurts
//                        that sputter on through the crash.
//
// Self-contained: builds its own additive material + soft particle texture and one
// ParticleSystem per nozzle at runtime, so nothing needs authoring in the prefab.
// Nozzles are found by name ("Cylinder", "Cylinder (1/2/3)") under the pod root.
public class PodThrustFlames : MonoBehaviour
{
    enum Phase { Off, IdleBursts, FullPower, DieOut, SmallSpurts }

    [SerializeField] float fullRate    = 220f;  // particles/sec at full power
    [SerializeField] float lifetime    = 0.32f;
    [SerializeField] float dieOutTime  = 0.8f;  // seconds for the flames + nozzles to collapse
    [SerializeField] float nozzleShrink = 0.45f; // nozzles shrink to this fraction during die-out

    Phase _phase = Phase.Off;
    Transform _pod;
    Transform[] _nozzles;
    Vector3[] _nozzleBaseScale;
    ParticleSystem[] _ps;
    Material _mat;
    Texture2D _tex;
    float _unit = 0.1f;        // world-scale reference (avg nozzle lossyScale)

    // per-nozzle random burst state (idle + spurt modes)
    float[] _timer;
    bool[] _firing;

    float _dieT;

    public void Initialize(Transform podRoot)
    {
        _pod = podRoot;

        var list = new System.Collections.Generic.List<Transform>();
        foreach (var t in podRoot.GetComponentsInChildren<Transform>(true))
            if (t.name == "Cylinder" || t.name.StartsWith("Cylinder ")) list.Add(t);
        _nozzles = list.ToArray();
        int n = _nozzles.Length;
        if (n == 0) { enabled = false; return; }

        _unit = 0f;
        foreach (var c in _nozzles)
            _unit += (c.lossyScale.x + c.lossyScale.y + c.lossyScale.z) / 3f;
        _unit /= n;
        if (_unit < 1e-4f) _unit = 0.1f;

        BuildMaterial();

        _ps = new ParticleSystem[n];
        _nozzleBaseScale = new Vector3[n];
        _timer = new float[n];
        _firing = new bool[n];
        for (int i = 0; i < n; i++)
        {
            _nozzleBaseScale[i] = _nozzles[i].localScale;
            _ps[i] = BuildFlame(i);
            _timer[i] = Random.Range(0f, 0.4f);
        }
    }

    public void BeginIdleBursts() { _phase = Phase.IdleBursts; }
    public void SetFullPower()    { _phase = Phase.FullPower; }
    public void BeginDieOut()     { if (_phase == Phase.DieOut || _phase == Phase.SmallSpurts) return; _phase = Phase.DieOut; _dieT = 0f; }
    public void StopAll()         { _phase = Phase.Off; if (_ps != null) foreach (var p in _ps) if (p != null) SetIntensity(p, 0f); }

    void LateUpdate()
    {
        if (_ps == null) return;
        float dt = Time.unscaledDeltaTime;

        // Keep each flame planted at its nozzle, firing straight down (-nozzle up).
        for (int i = 0; i < _ps.Length; i++)
        {
            var c = _nozzles[i];
            if (c == null || _ps[i] == null) continue;
            float halfH = c.lossyScale.y;                       // unity cylinder mesh half-height = 1
            _ps[i].transform.position = c.position - c.up * halfH;
            _ps[i].transform.rotation = Quaternion.LookRotation(-c.up, c.forward);
        }

        switch (_phase)
        {
            case Phase.IdleBursts:  TickRandom(dt, 0.45f, 0.15f, 0.7f, 0.06f, 0.25f); break;
            case Phase.FullPower:   for (int i = 0; i < _ps.Length; i++) SetIntensity(_ps[i], 1f); break;
            case Phase.DieOut:      TickDieOut(dt); break;
            case Phase.SmallSpurts: TickRandom(dt, 0.22f, 0.25f, 1.2f, 0.04f, 0.16f); break;
        }
    }

    // Random per-nozzle on/off bursts. power while firing, 0 while idle; fire/gap
    // durations randomised per nozzle so they sputter out of sync.
    void TickRandom(float dt, float power, float gapMin, float gapMax, float fireMin, float fireMax)
    {
        for (int i = 0; i < _ps.Length; i++)
        {
            _timer[i] -= dt;
            if (_timer[i] <= 0f)
            {
                _firing[i] = !_firing[i];
                _timer[i] = _firing[i] ? Random.Range(fireMin, fireMax) : Random.Range(gapMin, gapMax);
            }
            SetIntensity(_ps[i], _firing[i] ? power * Random.Range(0.8f, 1.1f) : 0f);
        }
    }

    void TickDieOut(float dt)
    {
        _dieT += dt / Mathf.Max(0.01f, dieOutTime);
        float k = Mathf.Clamp01(_dieT);
        float power = 1f - k;
        for (int i = 0; i < _ps.Length; i++)
        {
            SetIntensity(_ps[i], power);
            if (_nozzles[i] != null)
                _nozzles[i].localScale = _nozzleBaseScale[i] * Mathf.Lerp(1f, nozzleShrink, k);
        }
        if (k >= 1f)
        {
            _phase = Phase.SmallSpurts;
            for (int i = 0; i < _ps.Length; i++) { _firing[i] = false; _timer[i] = Random.Range(0.1f, 0.5f); }
        }
    }

    // Map a 0..1 power to emission rate, flame size and jet speed.
    void SetIntensity(ParticleSystem ps, float power)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = fullRate * power;
        var main = ps.main;
        main.startSizeMultiplier  = _unit * Mathf.Lerp(0f, 12.0f, power) + 1e-4f;
        main.startSpeedMultiplier = _unit * Mathf.Lerp(0f, 180f, power) + 1e-4f;
    }

    // ── Build (material + per-nozzle ParticleSystem) ───────────────────────────
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
                a = a * a;                       // soft falloff
                _tex.SetPixel(x, y, new Color(a, a, a, a));
            }
        _tex.Apply();
        _mat.mainTexture = _tex;
    }

    ParticleSystem BuildFlame(int i)
    {
        var go = new GameObject("ThrustFlame_" + i);
        go.transform.SetParent(_pod, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.startLifetime = lifetime;
        main.startSpeed = 1f;        // scaled via startSpeedMultiplier
        main.startSize  = 1f;        // scaled via startSizeMultiplier
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.maxParticles = 300;
        main.playOnAwake = false;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = Mathf.Max(0.001f, _unit * 0.5f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.95f, 0.65f), 0f),   // white-hot core
                new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.45f), // orange
                new GradientColorKey(new Color(0.7f, 0.12f, 0.02f), 1f),  // deep red
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var curve = new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = _mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_tex != null) Destroy(_tex);
    }
}
