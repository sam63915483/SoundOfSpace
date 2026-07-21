using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A placeable structure that creates a quarantined mini-atmosphere. Anything
/// inside its radius breathes the DOME's interior O2 instead of the planet's —
/// so a dome is a safe pocket you can build on a dead 0% world.
///
/// Interior O2 (spec §3.5):
///   interiorO2 = min(100, baseInterior + perTreeInterior * matureTreesInside)
/// Default 20 + 10×trees → 8 mature trees fills it to 100%. Cutting a tree inside
/// instantly lowers it (it falls out of the live count for free).
///
/// Venting: while the interior is at 100%, the dome pumps surplus into the
/// planet's ventedReserve (PlanetOxygen), slowly raising the whole planet's
/// baseline O2 — this is how a dead world gets bootstrapped back to life. Full
/// domes stack linearly.
///
/// Placement: authored as a normal buildable (prefab carries this component +
/// the visible bubble + is named "<prefab>_Placed" and parented to a
/// CelestialBody by the placement flow), so it saves + rides floating-origin
/// like any other placed building. Persistence of ventedReserve is the pending
/// save sub-step.
/// </summary>
public class BubbleDome : MonoBehaviour
{
    static readonly List<BubbleDome> s_all = new List<BubbleDome>();
    public static IReadOnlyList<BubbleDome> AllDomes => s_all;

    CelestialBody _body;
    float _treeUnitsInside;      // mature trees ×1 + half-grown-or-better saplings ×0.5
    float _sampleTimer;
    AudioSource _hum;
    DomeShieldGrow _shield;
    bool _wasFueled = true;
    float _fuel = 100f;          // 0..100 %. Crystals are the fuel; drains over time.
    bool _fuelInit;

    /// Interior O2 (0..100). The base (20%) is only a FLOOR: if the planet's own
    /// baseline O2 outside is higher, the dome lets it in instead of trapping you
    /// lower. Mature trees inside then raise it further.
    ///   interiorO2 = min(100, max(baseInterior, outsideO2) + perTreeInterior * treesInside)
    public float InteriorO2
    {
        get
        {
            float outside = (PlanetOxygen.Instance != null && _body != null)
                ? PlanetOxygen.Instance.SurfaceO2(_body) : 0f;
            if (!HasFuel) return outside;   // no fuel → emitter offline, just the planet's own air
            float floor = Mathf.Max(baseInterior, outside);
            return Mathf.Min(100f, floor + perTreeInterior * _treeUnitsInside);
        }
    }

    /// Interior O2 BEFORE the 100% cap — the dome's raw production. Anything over
    /// 100 is the surplus it pumps to the planet (see ExcessO2). 0 when offline.
    public float RawInteriorO2
    {
        get
        {
            if (!HasFuel) return 0f;
            float outside = (PlanetOxygen.Instance != null && _body != null)
                ? PlanetOxygen.Instance.SurfaceO2(_body) : 0f;
            return Mathf.Max(baseInterior, outside) + perTreeInterior * _treeUnitsInside;
        }
    }
    /// O2 produced beyond the 100% the interior can hold — the "excess" being vented.
    public float ExcessO2 => Mathf.Max(0f, RawInteriorO2 - 100f);
    public bool IsFull => InteriorO2 >= 100f;
    public CelestialBody Body => _body;

    // ── Fuel (crystals) ────────────────────────────────────────────────────
    public bool  HasFuel        => _fuel > 0f;
    public float FuelPercent    => _fuel;           // 0..100
    public float FuelPercent01  => _fuel / 100f;
    public float FuelPerCrystal => fuelPerCrystal;
    /// Real seconds of fuel remaining at the current drain rate.
    public float SecondsOfFuelLeft => _fuel * Mathf.Max(1f, fuelSeconds) / 100f;
    /// Atmosphere O2 pumped out per minute right now (only while full AND fuelled).
    /// A base rate plus a bonus that scales with the surplus (ExcessO2) — so a dome
    /// packed with trees terraforms the planet noticeably faster than a barely-full
    /// one, and the screen's "EXCESS" number directly drives the pump speed.
    public float VentPerMinute => (HasFuel && IsFull)
        ? ventBasePerMinute + ExcessO2 * ventExcessPerMinute
        : 0f;

    /// Crystals needed to top the tank back to 100% from its current level.
    public int CrystalsToFull()
    {
        float deficit = 100f - _fuel;
        if (deficit <= 0.01f) return 0;
        return Mathf.CeilToInt(deficit / fuelPerCrystal);
    }

    /// Feed inserted crystals into the tank (fuelPerCrystal % each), clamped to 100%.
    public void AddFuelFromCrystals(int crystals)
    {
        if (crystals <= 0) return;
        _fuel = Mathf.Min(100f, _fuel + crystals * fuelPerCrystal);
    }

    /// Set by the placement/registrar so the interior radius matches the visible bubble.
    public void SetRadius(float r) { radius = Mathf.Max(0.1f, r); }

    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() { s_all.Remove(this); }

    void Start()
    {
        // Placed domes are parented to their CelestialBody; fall back to nearest.
        _body = GetComponentInParent<CelestialBody>();
        if (_body == null) _body = NearestBody(transform.position);
        // A freshly-built dome ships with a full tank (the 20 crystals in its build
        // cost). A restored/saved dome would set _fuel before Start — respect that.
        if (!_fuelInit) { _fuel = startFuelPercent; _fuelInit = true; }
        _shield = GetComponent<DomeShieldGrow>();
        _wasFueled = HasFuel;
        RecountMatureInside();
        EnsureGeneratorHum();
        // A dome restored from a save with an empty tank must come up DARK —
        // OnEnable already started the shield grow and the hum started above, and
        // with no fueled→empty transition to catch, nothing else would stop them.
        if (!HasFuel) OnFuelStateChanged(false);
    }

    // Power the dome up (refuelled) or down (out of fuel): the shield collapses and
    // the generator hum stops when empty, and both come back when topped up.
    void OnFuelStateChanged(bool online)
    {
        if (_shield != null) _shield.SetShieldOn(online);
        if (_hum != null)
        {
            if (online) { if (!_hum.isPlaying) _hum.Play(); }
            else _hum.Pause();
        }
    }

    /// Restore a saved fuel level (call before Start via the future save hook).
    public void SetFuelPercent(float pct) { _fuel = Mathf.Clamp(pct, 0f, 100f); _fuelInit = true; }

    // Looping sci-fi generator whir at the dome centre (the emitter). Positional
    // 3D so it swells as you approach the generator and fades outside the dome.
    // Clip loaded by Resources name — no inspector wiring; silent if it's absent.
    void EnsureGeneratorHum()
    {
        if (_hum != null) return;
        var clip = Resources.Load<AudioClip>("DomeFX/dome_hum");
        if (clip == null) return;
        _hum = gameObject.AddComponent<AudioSource>();
        _hum.clip = clip;
        _hum.loop = true;
        _hum.playOnAwake = false;
        _hum.spatialBlend = 1f;                    // 3D — positional at the generator
        _hum.rolloffMode = AudioRolloffMode.Linear;
        _hum.minDistance = 2f;
        _hum.maxDistance = radius * 1.4f;          // audible across the interior, fades past the shell
        _hum.volume = humVolume;
        _hum.Play();
    }

    void Update()
    {
        // Fuel drain: a full tank (100%) lasts fuelSeconds (default 3600 = 1 hour,
        // i.e. 20 crystals). At 0 the emitter goes offline (no interior O2, no vent).
        if (_fuel > 0f)
            _fuel = Mathf.Max(0f, _fuel - (100f / Mathf.Max(1f, fuelSeconds)) * Time.deltaTime);

        // React to crossing the fuel threshold (drained to empty, or refuelled).
        bool fueled = HasFuel;
        if (fueled != _wasFueled) { _wasFueled = fueled; OnFuelStateChanged(fueled); }

        _sampleTimer -= Time.deltaTime;
        if (_sampleTimer <= 0f)
        {
            _sampleTimer = sampleInterval;
            RecountMatureInside();
        }

        // Vent surplus into the planet's atmosphere while full (and fuelled). Rate
        // = base + excess-scaled (see VentPerMinute), so fuller domes pump faster.
        if (HasFuel && IsFull && _body != null && PlanetOxygen.Instance != null)
            PlanetOxygen.Instance.AddVentedReserve(_body, (VentPerMinute / 60f) * Time.deltaTime);
    }

    /// True if a world point is inside this dome's quarantined interior.
    public bool IsInside(Vector3 worldPos)
        => (worldPos - transform.position).sqrMagnitude < radius * radius;

    /// The dome containing a world point, or null. Used by PlanetOxygen so
    /// anything inside samples interior O2 instead of the planet's.
    public static BubbleDome DomeContaining(Vector3 worldPos)
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            var d = s_all[i];
            // Skip offline (fuel-empty) domes so you breathe the planet's own air
            // again once a dome dies — it stops being a sealed pocket.
            if (d != null && d.HasFuel && d.IsInside(worldPos)) return d;
        }
        return null;
    }

    void RecountMatureInside()
    {
        float n = 0f;
        float rSq = radius * radius;
        Vector3 c = transform.position;
        var trees = SpawnedTree.AllTrees;   // mature seed + matured planted trees
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            if (t != null && !t.IsDead && (t.transform.position - c).sqrMagnitude < rSq) n += 1f;
        }
        // Half-grown-or-better saplings count 0.5 — same rule the planet baseline
        // uses (SaplingGrowth.GrowingO2EquivalentOnBody), so the dome's ramp to
        // venting is smooth instead of jumping only when a tree fully matures.
        var saps = SaplingGrowth.AllSaplings;
        for (int i = 0; i < saps.Count; i++)
        {
            var s = saps[i];
            if (s == null || s.IsMature || s.Growth < SaplingGrowth.HalfGrownThreshold) continue;
            if ((s.transform.position - c).sqrMagnitude < rSq) n += SaplingGrowth.HalfGrownFraction;
        }
        _treeUnitsInside = n;
    }

    static CelestialBody NearestBody(Vector3 pos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || b.isStaticAttractor) continue;
            float d = (b.Position - pos).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = b; }
        }
        return best;
    }

    // ── Tunables (spec defaults) ───────────────────────────────────────────
    [Header("Interior")]
    [Tooltip("Interior radius (m) of the quarantined atmosphere. Match this to the visible bubble mesh.")]
    [SerializeField] float radius = 8f;
    [Tooltip("Interior O2 %% floor with zero trees inside — a bare dome is already this breathable.")]
    [SerializeField] float baseInterior = 20f;
    [Tooltip("Interior O2 %% added per mature tree inside. A dome only fits ~4 trees (spacing), so 20 → a full complement of 4 fills ANY dome to 100% (even a dead 20% planet: 20+4×20=100), which is what starts the vent. Raise toward 25 if you want fewer trees to fill it on planets that already have O2.")]
    [SerializeField] float perTreeInterior = 20f;

    [Header("Venting")]
    [Tooltip("Base planet O2 gained per minute while the dome is FULL (added to ventedReserve). The floor rate a just-full dome pumps. Multiple full domes stack.")]
    [SerializeField] float ventBasePerMinute = 5f;
    [Tooltip("Extra planet O2/min per 1%% of EXCESS interior O2 (production above 100). 0.1 → a dome +35%% over full pumps an extra 3.5%%/min on top of the base. Makes packing more trees terraform faster.")]
    [SerializeField] float ventExcessPerMinute = 0.1f;

    [Header("Fuel (crystals)")]
    [Tooltip("Fuel %% a freshly-built dome starts with. Building costs 20 crystals, which is a full tank, so 100.")]
    [SerializeField] float startFuelPercent = 100f;
    [Tooltip("Real seconds a FULL tank lasts. 3600 = 1 hour = the 20-crystal charge.")]
    [SerializeField] float fuelSeconds = 3600f;
    [Tooltip("Fuel %% restored per crystal inserted. 5 → 20 crystals = a full 100%.")]
    [SerializeField] float fuelPerCrystal = 5f;

    [Header("Performance")]
    [Tooltip("Seconds between recounting mature trees inside. Cheap; doesn't need per-frame.")]
    [SerializeField] float sampleInterval = 0.5f;

    [Header("Audio")]
    [Tooltip("Volume of the looping generator whir at the dome centre (0 = silent).")]
    [SerializeField] float humVolume = 0.35f;
}
