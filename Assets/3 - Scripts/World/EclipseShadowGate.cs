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
        Vector3 sunToPlayer = _camera.position - sunPos;
        float playerDistSq = sunToPlayer.sqrMagnitude;

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

        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;

            bool exempt = b == nearest || b.transform == _sun.root || _sun.IsChildOf(b.transform);
            Vector3 sunToBody = b.transform.position - sunPos;
            // Between = on the player's side of the sun AND nearer to it than
            // the player. A body farther out than your own orbit can never
            // block your sun; a body behind the sun (negative dot) never can.
            bool canEclipse = Vector3.Dot(sunToBody, sunToPlayer) > 0f
                           && sunToBody.sqrMagnitude < playerDistSq;

            ApplyCasting(b, exempt || canEclipse);
        }
    }

    void ApplyCasting(CelestialBody body, bool casting)
    {
        _scratch.Clear();
        body.GetComponentsInChildren(true, _scratch);   // planets are a handful of face meshes
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
