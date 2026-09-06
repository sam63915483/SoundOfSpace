using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stops planets BEHIND the sun from casting eclipses (Sam's bug hunt,
/// 2026-08-18: the fiery/icey twins passing behind the sun each threw a
/// planet-covering shadow — back to back, sun between them).
///
/// ── Why that happened at all ─────────────────────────────────────────────
/// The eclipse was never coded — it falls out of Unity's DIRECTIONAL shadow
/// mapping for free. The sun's light is directional, and a directional light
/// has no position, only an axis: Unity slides the shadow camera back along
/// that axis far enough to include EVERY caster on the line. A moon 2,000m in
/// front of the sun and a planet 20,000m behind it are the same occluder to
/// that map. So a body transiting behind the sun (aligned with the light axis
/// from the camera's point of view) eclipses you exactly as if it were in
/// front — which is geometric nonsense the player can see through.
///
/// ── The gate ─────────────────────────────────────────────────────────────
/// A body may CAST shadows only when it could physically block your sun:
/// on the player's side of the sun AND closer to the sun than the player.
/// Anything failing that gets its renderers' shadowCastingMode forced Off
/// (original modes remembered and restored — never blanket On, so a renderer
/// authored Off stays Off).
///
/// Only the body's TERRAIN mesh is gated — never the things parented under
/// the planet (shuttle, village, trees). See ApplyCasting.
///
/// The body the player is nearest to is ALWAYS exempt: standing on the day
/// side puts its centre farther from the sun than you, but its terrain
/// self-shadowing is real and load-bearing (the whole sunset tip-light pass
/// exists because of those shadows). Receiving is untouched everywhere —
/// this only gates casting.
///
/// Auto-singleton (SpaceDustInventory pattern), ALSO seeded in
/// MainMenuController.EnsureGameplaySingletonsAsync — CLAUDE.md trap #1.
/// </summary>
public class EclipseShadowGate : MonoBehaviour
{
    public static EclipseShadowGate Instance { get; private set; }

    /// Orbital geometry changes slowly; per-frame toggling buys nothing.
    const float SweepInterval = 0.5f;

    float _nextSweep;

    /// Fraction of our own orbital distance a body must clearly cross before its
    /// casting decision flips. Purely anti-chatter — see the note in Update.
    const float EclipseHysteresis = 0.02f;

    /// Last casting decision per body, for that hysteresis.
    readonly Dictionary<CelestialBody, bool> _castingState = new Dictionary<CelestialBody, bool>();
    Transform _sun;         // the SunShadowCaster light's transform (sits at the sun)
    Transform _camera;      // lazy, throttled refind — never per-frame
    readonly Dictionary<Renderer, ShadowCastingMode> _original = new Dictionary<Renderer, ShadowCastingMode>();
    static readonly List<Renderer> _scratch = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[EclipseShadowGate]");
        DontDestroyOnLoad(go);
        go.AddComponent<EclipseShadowGate>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    /// A reload rebuilds every planet renderer; keeping the old dictionary
    /// would just accumulate dead keys and stale sun/camera refs forever.
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        _original.Clear();
        _castingState.Clear();
        _sun = null;
        _camera = null;
    }

    void Update()
    {
        if (Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + SweepInterval;

        var bodies = NBodySimulation.Bodies;          // Array.Empty off the solar-system scene
        if (bodies.Length == 0) return;

        if (_sun == null)
        {
            var caster = FindObjectOfType<SunShadowCaster>();
            if (caster == null) return;
            _sun = caster.transform;
        }
        if (_camera == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _camera = cam.transform;
        }

        Vector3 sunPos = _sun.position;

        // The body the player is nearest to keeps its authored casting —
        // that's the ground underfoot and its terrain shadows.
        CelestialBody nearest = null;
        float nearestSq = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.transform.position - _camera.position).sqrMagnitude;
            if (d < nearestSq) { nearestSq = d; nearest = b; }
        }

        // ⚠️ MEASURE FROM THE BODY UNDERFOOT, NOT FROM THE CAMERA.
        //
        // This used to compare each body's distance-to-sun against the CAMERA's.
        // But walking around a planet changes your own distance to the sun by up
        // to a planet radius, while the question being asked — "is that body
        // between my world and the sun?" — is pure orbital geometry and has
        // nothing to do with where you stand on the surface.
        //
        // The consequence was ugly and hard to place: any body whose orbit sits
        // near your own distance from the sun (a moon like Constant Companion
        // spends its whole orbit there) had this test flip as the player walked.
        // Flipping it toggles that body's shadow CASTING, so its eclipse blinked
        // on and off and EVERY lit surface in the world — all the grass at once —
        // jumped in brightness and hue. The trigger was an iso-distance surface
        // draped over the terrain, which is why it reproduced at unrelated spots
        // (a village edge, an empty field) with nothing in common but standing on
        // that line.
        //
        // Referencing the body's centre makes the test depend only on orbital
        // motion, which is slow and real. Walking can no longer change it at all.
        Vector3 refPos = nearest != null ? nearest.transform.position : _camera.position;
        Vector3 sunToRef = refPos - sunPos;
        float refDist = sunToRef.magnitude;

        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;

            bool exempt = b == nearest || b.transform == _sun.root || _sun.IsChildOf(b.transform);
            Vector3 sunToBody = b.transform.position - sunPos;

            // Between = on our side of the sun AND nearer to it than we are. A
            // body farther out than our own orbit can never block our sun; a
            // body behind the sun (negative dot) never can.
            //
            // Hysteresis on the distance half: a body orbiting at almost exactly
            // our own radius would otherwise chatter across the boundary as the
            // two drift past each other, and a shadow caster switching on and off
            // repeatedly is far worse to look at than one that commits. Once
            // casting it must get CLEARLY farther out to stop; once stopped it
            // must come CLEARLY inside to resume.
            bool wasCasting = !_castingState.TryGetValue(b, out bool prev) || prev;
            float limit = refDist * (wasCasting ? 1f + EclipseHysteresis : 1f - EclipseHysteresis);
            bool canEclipse = Vector3.Dot(sunToBody, sunToRef) > 0f
                           && sunToBody.magnitude < limit;

            bool casting = exempt || canEclipse;
            _castingState[b] = casting;
            ApplyCasting(b, casting);
        }
    }

    void ApplyCasting(CelestialBody body, bool casting)
    {
        // ONLY the planet's own terrain may be gated (2026-09-06, the shuttle
        // light probe). This used to take every renderer under the body — and
        // the shuttle, the village, the trees, the NPCs all live under their
        // planet. Whenever this planet wasn't the nearest body to the camera
        // (mid-descent another world is closer — since the dwarf planets went
        // in, one of them briefly is on every approach), the whole shuttle lost
        // shadow casting and every light, even the shadowed sun, lit the cabin
        // straight through the hull until the planet became nearest again at
        // ~60% of the landing. An eclipse is cast by the terrain sphere alone.
        _scratch.Clear();
        Transform scope = null;
        var gen = body.GetComponentInChildren<CelestialBodyGenerator>(true);
        if (gen != null) scope = gen.transform;
        else
        {
            var ph = body.GetComponentInChildren<BodyPlaceholder>(true);
            if (ph != null) scope = ph.transform;
        }
        if (scope == null) return;                       // no terrain (the black hole): nothing to gate
        scope.GetComponentsInChildren(true, _scratch);   // the terrain mesh (+ its preview placeholder)
        for (int i = 0; i < _scratch.Count; i++)
        {
            Renderer r = _scratch[i];
            if (r == null) continue;

            ShadowCastingMode original;
            if (!_original.TryGetValue(r, out original))
            {
                original = r.shadowCastingMode;
                _original[r] = original;
            }

            ShadowCastingMode want = casting ? original : ShadowCastingMode.Off;
            if (r.shadowCastingMode != want) r.shadowCastingMode = want;
        }
    }
}
