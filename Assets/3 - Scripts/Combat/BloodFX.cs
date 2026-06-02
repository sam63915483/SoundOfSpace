using UnityEngine;

/// <summary>
/// Scene-placed singleton that spawns blood VFX from the Piloto Blood VFX
/// Essentials pack. Combat scripts call BloodFX.Instance?.SpawnSpray / SpawnPool
/// — absent manager = silent no-op. Lives under the gameplay scene's managers
/// organizer; scene-placed so it exists on both editor-play and build-load (no
/// EnsureGameplaySingletons seeding needed).
/// </summary>
public class BloodFX : MonoBehaviour
{
    public static BloodFX Instance { get; private set; }

    [Header("Prefabs")]
    [Tooltip("Burst spawned at the bullet hit point (a Blood Splash / Fountain). Non-looping; auto-destroyed after sprayLifetime.")]
    [SerializeField] GameObject sprayPrefab;
    [Tooltip("Pool spawned on the ground when an enemy dies (a Sticky_Splat_*).")]
    [SerializeField] GameObject poolPrefab;

    [Header("Spray (on hit)")]
    [Tooltip("Uniform scale applied to the spawned spray FX.")]
    [SerializeField] float sprayScale = 1f;
    [Tooltip("Seconds before the spray FX is destroyed.")]
    [SerializeField] float sprayLifetime = 3f;
    [Tooltip("Euler offset applied AFTER aiming the spray along the surface normal. Correct for the chosen prefab's emission axis here (e.g. if it emits along +Y rather than +Z). Tune in Play mode.")]
    [SerializeField] Vector3 sprayRotationOffset = Vector3.zero;

    [Header("Pool (on death)")]
    [Tooltip("Uniform scale applied to the spawned pool FX.")]
    [SerializeField] float poolScale = 1f;
    [Tooltip("Seconds the pool stays at full opacity before it begins fading.")]
    [SerializeField] float poolLingerSeconds = 20f;
    [Tooltip("Seconds the pool takes to fade out before it is destroyed.")]
    [SerializeField] float poolFadeSeconds = 3f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Spawn a blood burst at a bullet hit point, aimed back along the surface
    /// normal (toward the shooter).
    /// </summary>
    public void SpawnSpray(Vector3 point, Vector3 normal, Vector3 shotDir)
    {
        if (sprayPrefab == null) return;

        Vector3 dir = normal.sqrMagnitude > 0.0001f ? normal.normalized
                    : (shotDir.sqrMagnitude > 0.0001f ? -shotDir.normalized : Vector3.up);
        Quaternion rot = Quaternion.LookRotation(dir) * Quaternion.Euler(sprayRotationOffset);

        var fx = Instantiate(sprayPrefab, point, rot);
        if (!Mathf.Approximately(sprayScale, 1f)) fx.transform.localScale *= sprayScale;

        // Parent under the nearest planet so floating-origin shifts don't
        // teleport the FX during its short life.
        Transform planet = ResolveNearestPlanet(point);
        if (planet != null) fx.transform.SetParent(planet, worldPositionStays: true);

        DisableColliders(fx);
        Destroy(fx, sprayLifetime);
    }

    /// <summary>
    /// Spawn a blood pool lying flat on the surface at an enemy's feet on death.
    /// </summary>
    public void SpawnPool(Vector3 groundPoint, Vector3 up, Transform planet)
    {
        if (poolPrefab == null) return;

        Vector3 u = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, u);

        var fx = Instantiate(poolPrefab, groundPoint, rot);
        if (!Mathf.Approximately(poolScale, 1f)) fx.transform.localScale *= poolScale;
        if (planet != null) fx.transform.SetParent(planet, worldPositionStays: true);

        DisableColliders(fx);

        var pool = fx.GetComponent<BloodPool>();
        if (pool == null) pool = fx.AddComponent<BloodPool>();
        pool.Init(poolLingerSeconds, poolFadeSeconds);
    }

    static Transform ResolveNearestPlanet(Vector3 point)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        Transform nearest = null;
        float best = float.PositiveInfinity;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float d = (b.Position - point).sqrMagnitude;
            if (d < best) { best = d; nearest = b.transform; }
        }
        return nearest;
    }

    static void DisableColliders(GameObject go)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }
}
