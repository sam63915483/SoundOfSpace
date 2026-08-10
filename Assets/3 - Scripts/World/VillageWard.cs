using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The village is a SAFE PLACE. Aliens that wander or chase their way in burn
/// down and die, and none ever spawn inside it.
///
/// ── Why this had to exist ────────────────────────────────────────────────
/// Nothing was keeping them out. `SpawnExclusionZone` looked like it was doing
/// the job, but the fifteen zones in the scene are per-BUILDING — footprint plus
/// three metres, the largest of them under eleven metres across — while the
/// village itself is a ~177 m cluster. They stop a tree growing through a roof.
/// They were never going to stop an alien walking up the main street, and
/// EnemySpawner did not even consult them until this pass.
///
/// So the village is warded as one place rather than as a scatter of buildings.
/// The ward is measured from the buildings themselves at runtime, so moving or
/// adding houses moves the boundary with them and there is no magic number in
/// the scene to fall out of date.
///
/// ── Burn, don't vanish ───────────────────────────────────────────────────
/// Damage over about two seconds rather than an instant kill, matching the
/// sunburn and torch-aura feel the stealth revamp already established: you get
/// to watch the thing that was chasing you stagger and drop just inside the
/// lights, which reads as the village protecting you. A pop would read as a bug.
///
/// ── Multiplayer ──────────────────────────────────────────────────────────
/// Needs no authority gate, for the same reason TorchAura needs none: on a guest
/// every enemy is a puppet, and EnemyController.TakeDamage discards
/// environmental damage on a puppet outright. The host does the killing and the
/// death replicates like any other.
/// </summary>
public class VillageWard : MonoBehaviour
{
    public static VillageWard Instance { get; private set; }

    /// Where the village hierarchy lives. Matches VillageExclusionTool.
    const string VillagePath = "--- Celestial ---/Body Simulation/Humble Abode/TOWN-VILLAGE";

    /// Kills a 100 HP alien in a bit under two seconds.
    const float DamagePerSecond = 60f;

    /// Breathing room past the outermost building, so the boundary sits out in
    /// the fields rather than flush against someone's wall.
    const float Margin = 12f;

    /// Rechecked this often while the village has not been found yet — the
    /// hierarchy does not exist in MainMenu and is built during scene load.
    const float RetrySeconds = 2f;

    Transform _village;
    /// Centre held PLANET-LOCAL, because the village rides an orbiting body and
    /// a world-space centre would be metres stale within a frame.
    Vector3 _localCentre;
    float _radius;
    float _nextRetry;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1). With no village in the
        // scene it simply idles on a throttled retry.
        var go = new GameObject("VillageWard");
        DontDestroyOnLoad(go);
        go.AddComponent<VillageWard>();
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
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // The old village belonged to the scene we just left.
        _village = null;
        _radius = 0f;
        _nextRetry = 0f;
    }

    /// True when `worldPos` is inside the village. Used by EnemySpawner to keep
    /// the field from building itself in the streets, and by the burn below.
    public static bool IsPositionProtected(Vector3 worldPos)
    {
        var w = Instance;
        if (w == null || w._village == null || w._radius <= 0f) return false;
        Vector3 centre = w._village.TransformPoint(w._localCentre);
        return (worldPos - centre).sqrMagnitude < w._radius * w._radius;
    }

    void Update()
    {
        if (_village == null)
        {
            if (Time.time < _nextRetry) return;
            _nextRetry = Time.time + RetrySeconds;
            TryResolveVillage();
            if (_village == null) return;
        }

        var enemies = EnemyController.ActiveEnemies;
        if (enemies == null || enemies.Count == 0) return;

        Vector3 centre = _village.TransformPoint(_localCentre);
        float r2 = _radius * _radius;
        float dmg = DamagePerSecond * Time.deltaTime;

        // Backwards, because TakeDamage can kill and unregister mid-iteration.
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null || e.IsDying) continue;
            if ((e.transform.position - centre).sqrMagnitude > r2) continue;
            // creditPlayer:false — the village killed it, not you, so it builds
            // no killstreak and no GANGSTA REP. Same call the torches make.
            e.TakeDamage(dmg, creditPlayer: false);
        }
    }

    /// <summary>
    /// Measure the village from the BUILDING MARKERS, not from renderer bounds.
    ///
    /// VillageExclusionTool already drops a SpawnExclusionZone on every building
    /// under TOWN-VILLAGE, sized to its footprint. That is authored data saying
    /// "a house is here", and it is maintained — add a house, re-run the tool,
    /// and this boundary grows with it.
    ///
    /// Renderer bounds were the obvious alternative and are measurably worse:
    /// they include the ground and road meshes, and on a 200 m planet a
    /// settlement that wraps the surface picks up a lot of false height from
    /// curvature. Measured on the shipping scene they gave 116 m against the
    /// markers' 81 m — a third again as much countryside warded for nothing.
    ///
    /// Distance is measured in the TANGENT PLANE at the village centre, because
    /// what matters is how far across the ground the place reaches.
    ///
    /// Falls back to renderer bounds only if a village somehow has no markers at
    /// all, so a scene that never ran the tool still gets a boundary.
    /// </summary>
    void TryResolveVillage()
    {
        var go = GameObject.Find(VillagePath);
        if (go == null) return;

        var markers = go.GetComponentsInChildren<SpawnExclusionZone>(true);
        var body = go.GetComponentInParent<CelestialBody>();

        Vector3 centre;
        float radius = 0f;

        if (markers.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < markers.Length; i++)
                if (markers[i] != null) { sum += markers[i].transform.position; n++; }
            if (n == 0) return;
            centre = sum / n;

            Vector3 up = body != null ? (centre - body.Position).normalized : go.transform.up;
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null) continue;
                float flat = Vector3.ProjectOnPlane(markers[i].transform.position - centre, up).magnitude
                           + markers[i].radius;
                if (flat > radius) radius = flat;
            }
        }
        else
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = default;
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!any) { b = rends[i].bounds; any = true; } else b.Encapsulate(rends[i].bounds);
            }
            if (!any) return;
            centre = b.center;

            Vector3 up = body != null ? (centre - body.Position).normalized : go.transform.up;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                float flat = Vector3.ProjectOnPlane(rends[i].bounds.center - centre, up).magnitude
                           + Vector3.ProjectOnPlane(rends[i].bounds.extents, up).magnitude;
                if (flat > radius) radius = flat;
            }
            Debug.LogWarning("[VillageWard] No SpawnExclusionZones under the village — falling back to " +
                             "renderer bounds, which over-measure. Run Tools/Village/Refresh Building " +
                             "Exclusion Zones for a tighter boundary.");
        }

        if (radius <= 0f) return;

        _village = go.transform;
        _radius = radius + Margin;
        _localCentre = _village.InverseTransformPoint(centre);

        Debug.Log($"[VillageWard] Village warded: radius {_radius:F1} m from {markers.Length} " +
                  $"building marker(s). Aliens inside will burn.");
    }
}
