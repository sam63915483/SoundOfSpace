using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// Trailer dev tool: press J and the black hole's lens grows faster and faster
// until it envelops the whole view — for capturing the "it swallows everything"
// trailer shot. Press J again to snap it back to its real size (for retakes).
//
// HOW IT WORKS — the Scingularity shader's apparent event-horizon radius is driven
// purely by the visual quad's WORLD SCALE (see Scingularity 1.2.shader vert():
// output.radius = |ObjectToWorld·(1,1,1) − ObjectToWorld·(0,0,0)| · 1/√3). The
// accretion disk is measured in units of that same radius, so growing the quad's
// transform grows the lens AND the disk together — one scale does it all. This is
// purely cosmetic: gravity uses GravityObject mass, the spawners skip the static
// attractor, and the CelestialBody.radius field is untouched — nothing physical
// changes, so it's safe to fire mid-save and reset.
//
// AUTO-TUNED ENVELOP — at toggle-on we measure the camera→black-hole distance and
// pick a target radius of coverage× that distance (so the dark core fills the sky
// no matter where you're standing), then grow EXPONENTIALLY (scale ∝ factor^(t/T))
// so it accelerates and reaches full envelop in growDuration seconds. Distant black
// hole or point-blank, the clip lands the same.
//
// Build-safe via the auto-singleton + EnsureGameplaySingletons seed (CLAUDE.md
// trap #1) — mirrors TrailerFreeCam. Runs in LateUpdate (order 300, after the pod
// sequence / free-cam at 200) so nothing fights the scale.
// ─────────────────────────────────────────────────────────────────────────────
[DefaultExecutionOrder(300)]
public class TrailerBlackHoleGrow : MonoBehaviour
{
    public static TrailerBlackHoleGrow Instance { get; private set; }

    [Header("Toggle")]
    [SerializeField] KeyCode toggleKey = KeyCode.J;

    [Header("Growth")]
    [Tooltip("Seconds from J-press to full envelop. The growth is exponential (faster and faster), reaching the target exactly here.")]
    [SerializeField] float growDuration = 10f;
    [Tooltip("Target lens radius as a multiple of the camera→black-hole distance. >1 guarantees the dark core fills the whole sky at the end.")]
    [SerializeField] float coverage = 2.5f;
    [Tooltip("Safety cap on how many times the visual is allowed to grow, in case the black hole is extremely far. Prevents a degenerate scale.")]
    [SerializeField] float maxScaleMultiplier = 5_000_000f;

    // ── Runtime ──────────────────────────────────────────────────────────────
    Transform _visual;            // the Scingularity "Singularity Visual" quad
    Vector3 _origScale;           // cached to reset on toggle-off
    bool _growing;
    float _t;                     // elapsed grow time (unscaled)
    float _factorPerSecondLog;    // ln(totalFactor) / growDuration — the exponential rate

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;   // seeded by EnsureGameplaySingletons in builds
        var go = new GameObject("TrailerBlackHoleGrow");
        DontDestroyOnLoad(go);
        go.AddComponent<TrailerBlackHoleGrow>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!Input.GetKeyDown(toggleKey)) return;
        if (_growing) ResetGrow();
        else StartGrow();
    }

    void StartGrow()
    {
        if (!EnsureVisual()) { Debug.LogWarning("[TrailerBlackHoleGrow] No Scingularity black-hole visual found in scene."); return; }

        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[TrailerBlackHoleGrow] No main camera."); return; }

        // Current world radius ≈ the uniform world scale (matches the shader's 1/√3
        // cube-diagonal). Target radius = coverage × distance to the hole's centre.
        float currentRadius = Mathf.Max(0.0001f, _visual.lossyScale.magnitude * 0.57735f);
        float dist = Vector3.Distance(cam.transform.position, _visual.position);
        float targetRadius = Mathf.Max(currentRadius, dist * coverage);

        float totalFactor = Mathf.Clamp(targetRadius / currentRadius, 1.0001f, maxScaleMultiplier);
        _factorPerSecondLog = Mathf.Log(totalFactor) / Mathf.Max(0.01f, growDuration);

        _origScale = _visual.localScale;
        _t = 0f;
        _growing = true;
    }

    void ResetGrow()
    {
        _growing = false;
        if (_visual != null) _visual.localScale = _origScale;
    }

    void LateUpdate()
    {
        if (!_growing) return;
        if (_visual == null) { _growing = false; return; }

        _t += Time.unscaledDeltaTime;
        float mult = Mathf.Exp(_factorPerSecondLog * Mathf.Min(_t, growDuration));   // exponential, holds at the target after growDuration
        _visual.localScale = _origScale * mult;
    }

    // Find the Scingularity visual quad once (by shader name — robust against the
    // exact hierarchy names). Cached; re-finds if the cache went null on a reload.
    bool EnsureVisual()
    {
        if (_visual != null) return true;

        var renderers = FindObjectsOfType<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m != null && m.shader != null && m.shader.name == "problemecium/Scingularity 1.2")
                {
                    _visual = r.transform;
                    return true;
                }
            }
        }
        return false;
    }
}
