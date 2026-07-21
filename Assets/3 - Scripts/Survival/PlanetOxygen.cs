using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The ecosystem → oxygen authority. Turns a planet's living-tree population
/// into a breathable-air value that the suit converter (OxygenManager) and,
/// later, saplings + bubble domes all read from.
///
/// Model (all tunables at class end):
///   surfaceO2(planet) = 100 * currentTrees / treesForFull(area)   // 0 on a dead world
///   altitudeFactor    = 1 at the ground → 0 at the atmosphere top → 0 in space
///   localForestBonus  = extra O2 from mature trees within R of a point
///   ambientO2(pos)    = clamp(surfaceO2 * altitudeFactor + localForestBonus, 0, 100)
///
/// currentTrees is the REAL planet-wide count: the seed's deterministic
/// designated-tree count (TreeSpawner, enumerated + cached) minus cells the
/// player has chopped. Only trees near the player are actually instantiated,
/// but the seed decides every tree on the planet whether rendered or not, so
/// the count is genuine — not a radius² guess. Cutting trees lowers it live;
/// (phase b) maturing planted saplings will raise it.
///
/// A planet with no trees (moons, the sun — TreeSpawner's exclude list) reads
/// 0% and behaves as vacuum. That exclude list IS the "no atmosphere" list;
/// no separate flag is needed. Trees ARE the atmosphere.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md), or it
/// never auto-creates in builds. All world reads go through CelestialBody
/// gameplay accessors + NBodySimulation.Bodies; the forbidden atmosphere/
/// shader/generation code is never touched.
/// </summary>
public class PlanetOxygen : MonoBehaviour
{
    public static PlanetOxygen Instance { get; private set; }

    // Cached ambient O2 (0..100) at the player, refreshed on a timer (never
    // per-frame) so OxygenManager's FixedUpdate + the HUD can read it cheaply.
    float _playerAmbientO2 = 100f;
    public float PlayerAmbientO2 => _playerAmbientO2;
    public float PlayerAmbientPercent => Mathf.Clamp01(_playerAmbientO2 / 100f);

    PlayerController _player;
    TreeSpawner _trees;
    float _sampleTimer;
    float _refindTimer;
    readonly HashSet<string> _loggedBodies = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PlanetOxygen");
        DontDestroyOnLoad(go);
        go.AddComponent<PlanetOxygen>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        _sampleTimer -= Time.deltaTime;
        if (_sampleTimer > 0f) return;
        _sampleTimer = sampleInterval;

        EnsureRefs();

        // Off the solar-system scene (interiors) there are no bodies — treat as
        // fully breathable so the readout never reads vacuum indoors.
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0 || _player == null)
        {
            _playerAmbientO2 = 100f;
            return;
        }

        Vector3 pos = _player.Rigidbody != null ? _player.Rigidbody.position : _player.transform.position;
        _playerAmbientO2 = AmbientO2At(pos);
    }

    void EnsureRefs()
    {
        if (_player != null && _trees != null) return;
        _refindTimer -= sampleInterval;
        if (_refindTimer > 0f) return;
        _refindTimer = 0.5f;
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        if (_trees == null) _trees = TreeSpawner.Instance;
    }

    // ── Public sampling API ───────────────────────────────────────────────

    /// Effective breathable O2 (0..100) at an arbitrary world point. Resolves
    /// the nearest body + altitude itself. Used by saplings (phase b) sampling
    /// growth at their own position.
    public float AmbientO2At(Vector3 worldPos)
    {
        // Inside a bubble dome → breathe its quarantined interior, independent of
        // the planet baseline and altitude. This is what makes a dome a safe
        // pocket (and lets saplings grow) on an otherwise-dead world.
        var dome = BubbleDome.DomeContaining(worldPos);
        if (dome != null) return dome.InteriorO2;

        CelestialBody body = NearestBody(worldPos);
        float alt = Altitude(worldPos, body);
        return AmbientO2(body, alt, worldPos);
    }

    /// Core sampler when the caller already knows the body + altitude (e.g.
    /// OxygenManager, which computes both every FixedUpdate anyway).
    public float AmbientO2(CelestialBody body, float altitude, Vector3 worldPos)
    {
        float surface = SurfaceO2(body);
        float ambient = surface * AltitudeFactor(body, altitude) + LocalForestBonus(worldPos);
        return Mathf.Clamp(ambient, 0f, 100f);
    }

    /// Planet-baseline O2 at the surface (0..100), from the living-tree count.
    /// Independent of altitude and of nearby forests.
    public float SurfaceO2(CelestialBody body)
    {
        if (body == null) return 0f;
        // Seed forest (minus chopped) PLUS the player's matured planted trees —
        // planting is how a baseline climbs back up.
        // Mature trees (seed + planted) count 1 each; saplings that are at least
        // half-grown count 0.5 each — partial O2 before they fully mature.
        float trees = (_trees != null ? _trees.CurrentTreeCount(body) : 0)
                    + SaplingGrowth.MatureCountOnBody(body)
                    + SaplingGrowth.GrowingO2EquivalentOnBody(body);

        float area = 4f * Mathf.PI * body.radius * body.radius;              // m²
        float treesForFull = treesForFullO2PerMillionSqm * (area / 1_000_000f);
        if (treesForFull < 1f) treesForFull = 1f;
        // Trees + the reserve vented by bubble domes (this is what bootstraps a
        // 0-tree dead planet back toward breathable).
        float o2 = Mathf.Clamp(100f * trees / treesForFull + GetVentedReserve(body), 0f, 100f);

        MaybeLog(body, trees, treesForFull, o2);
        return o2;
    }

    // ── Vented reserve (per planet, raised by full bubble domes) ───────────
    readonly Dictionary<string, float> _ventedReserve = new Dictionary<string, float>();

    public float GetVentedReserve(CelestialBody body)
        => body != null && _ventedReserve.TryGetValue(body.bodyName, out float v) ? v : 0f;

    /// Called by full domes each frame to pump surplus O2 into the planet.
    public void AddVentedReserve(CelestialBody body, float amount)
    {
        if (body == null || amount == 0f) return;
        _ventedReserve.TryGetValue(body.bodyName, out float v);
        _ventedReserve[body.bodyName] = Mathf.Clamp(v + amount, 0f, 100f);
    }

    /// Save/load + New Game hooks (persistence sub-step).
    public void SetVentedReserve(string bodyName, float v)
    {
        if (string.IsNullOrEmpty(bodyName)) return;
        _ventedReserve[bodyName] = Mathf.Clamp(v, 0f, 100f);
    }
    public IReadOnlyDictionary<string, float> VentedReserves => _ventedReserve;
    public void ResetForNewGame() => _ventedReserve.Clear();

    /// 1 at the ground, ramping linearly to 0 at the atmosphere top, 0 in space.
    /// Atmosphere top scales with planet size (thicker air on bigger worlds).
    public float AltitudeFactor(CelestialBody body, float altitude)
    {
        if (body == null) return 0f;
        float top = body.radius * atmosphereHeightFraction;
        if (top <= 0.0001f) return altitude <= 0f ? 1f : 0f;
        return Mathf.Clamp01(1f - altitude / top);
    }

    /// Extra O2 from mature trees within localRadius of a point (linear
    /// falloff), capped. Iterates SpawnedTree.AllTrees — already a small,
    /// streamed set (bounded by the tree spawner's maxTrees), so a linear scan
    /// is cheaper than any spatial structure and needs no new infrastructure.
    public float LocalForestBonus(Vector3 worldPos)
    {
        var all = SpawnedTree.AllTrees;
        if (all == null || all.Count == 0 || localRadius <= 0.01f) return 0f;

        float rSq = localRadius * localRadius;
        float bonus = 0f;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            if (t == null || t.IsDead) continue;
            Vector3 tp = t.transform.position;
            float dSq = (tp - worldPos).sqrMagnitude;
            if (dSq >= rSq) continue;
            float d = Mathf.Sqrt(dSq);
            bonus += perTreeBonus * (1f - d / localRadius);
            if (bonus >= localBonusCap) return localBonusCap;
        }
        return Mathf.Min(bonus, localBonusCap);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static CelestialBody NearestBody(Vector3 pos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || b.isStaticAttractor) continue;
            float d = (b.Position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = b; }
        }
        return nearest;
    }

    static float Altitude(Vector3 pos, CelestialBody body)
    {
        if (body == null) return float.MaxValue;
        return (pos - body.Position).magnitude - body.radius;
    }

    // One-shot console readout per planet so Sam can see the true tree count +
    // resulting surface O2 and tune treesForFullO2PerMillionSqm against it.
    void MaybeLog(CelestialBody body, float trees, float treesForFull, float o2)
    {
        if (!logSurfaceO2 || body == null) return;
        if (!_loggedBodies.Add(body.bodyName)) return;
        Debug.Log($"[PlanetOxygen] {body.bodyName}: {trees:0.#} live trees / {treesForFull:0} for 100% → surface O2 = {o2:0}%");
    }

    // ── Tunables (spec defaults; tune in play then bake into code) ──────────
    [Header("Sampling")]
    [Tooltip("Seconds between ambient-O2 samples at the player. Cheap; O2 doesn't change fast. 0.5–1.0 is plenty.")]
    [SerializeField] float sampleInterval = 0.5f;

    [Header("Planet baseline O2 (from tree count)")]
    [Tooltip("Live trees needed to reach 100% surface O2 per 1,000,000 m² of planet surface. LOWER = planets breathe with fewer trees. Tune so Humble Abode lands where you want it (watch the [PlanetOxygen] console line for the real count). 1500 puts ~414 sparse Humble Abode trees near 55%.")]
    [SerializeField] float treesForFullO2PerMillionSqm = 1500f;
    [Tooltip("Log each planet's true tree count + computed surface O2 once, to help tuning. Turn off for release.")]
    [SerializeField] bool logSurfaceO2 = true;

    [Header("Altitude falloff")]
    [Tooltip("Atmosphere top as a fraction of planet radius. O2 is full at the surface and fades to 0 at radius×this. (Humble Abode r=200 × 0.6 ≈ 120 m, matching the old atmosphere top.)")]
    [SerializeField] float atmosphereHeightFraction = 0.6f;

    [Header("Local forest bonus")]
    [Tooltip("Radius (m) within which nearby mature trees raise effective O2 above the planet baseline. SMALLER = the boost is more local and fades faster as you walk away from a tree.")]
    [SerializeField] float localRadius = 30f;
    [Tooltip("O2 points added per nearby tree at point-blank range (falls off linearly to 0 at localRadius). HIGHER = standing by even one tree is a noticeable jump.")]
    [SerializeField] float perTreeBonus = 10f;
    [Tooltip("Maximum total local-forest bonus, so a dense grove can't overshoot wildly.")]
    [SerializeField] float localBonusCap = 30f;
}
