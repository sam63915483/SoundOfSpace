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

    // New serialized fields are appended at the END so existing scene/prefab
    // serialization of the fields above is never reordered.
    [Header("Spray Animation")]
    [Tooltip("Seconds for the spray to grow from zero to its full size when it spawns.")]
    [SerializeField] float sprayGrowSeconds = 0.5f;
    [Tooltip("Seconds for the spray to shrink back to zero at the end of its life.")]
    [SerializeField] float sprayShrinkSeconds = 0.5f;

    [Header("Rendering")]
    [Tooltip("Built-in RP has no camera depth texture by default, so some Piloto particle layers (e.g. the depth-based blood droplets) render invisible in the Game view even though they show in the editor. Leaving this on enables DepthTextureMode.Depth on the main camera so the spray renders the same in game as in the editor.")]
    [SerializeField] bool ensureCameraDepth = true;

    [Tooltip("Metres the spray spawn point is pushed back along the surface normal, INTO the body, so the blood reads as coming from inside the enemy instead of floating a few cm off the collider surface. Keep small so it doesn't sink too deep.")]
    [SerializeField] float sprayDepthInset = 0.15f;

    [Header("Damage Splash (any player hit)")]
    [Tooltip("A random one of these plays at the enemy's body centre on EVERY player hit (gun / axe / fishing rod). Assign all the Blood Splashes prefabs.")]
    [SerializeField] GameObject[] damageSplashPrefabs;
    [Tooltip("Uniform scale applied to the spawned damage splash.")]
    [SerializeField] float damageSplashScale = 0.5f;
    [Tooltip("Seconds before the damage splash is destroyed.")]
    [SerializeField] float damageSplashLifetime = 2f;

    [Header("Death Pool / Decal")]
    [Tooltip("If the pool prefab is a blood-decal Projector, this is the height (m) above the ground it's placed so its frustum projects DOWN onto the curved terrain (conforms to the curve). Keep small so only the ground right below gets the decal.")]
    [SerializeField] float poolProjectorHeight = 0.5f;

    Camera _depthCam;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start() { ApplyCameraDepth(); }

    void Update()
    {
        // Lazily (re)apply if the main camera wasn't ready at Start or got
        // swapped. Camera.main is only queried while _depthCam is null.
        if (ensureCameraDepth && _depthCam == null) ApplyCameraDepth();
    }

    void ApplyCameraDepth()
    {
        if (!ensureCameraDepth) return;
        var cam = Camera.main;
        if (cam != null)
        {
            cam.depthTextureMode |= DepthTextureMode.Depth;
            _depthCam = cam;
        }
    }

    /// <summary>
    /// Spawn a blood burst at a bullet hit point, aimed back along the surface
    /// normal (toward the shooter). When attachTo is supplied (the hit object),
    /// the spray is parented to it so it rides with a moving enemy; otherwise it
    /// parents under the nearest planet to survive floating-origin shifts.
    /// </summary>
    public void SpawnSpray(Vector3 point, Vector3 normal, Vector3 shotDir, Transform attachTo = null)
    {
        if (sprayPrefab == null) return;

        Vector3 dir = normal.sqrMagnitude > 0.0001f ? normal.normalized
                    : (shotDir.sqrMagnitude > 0.0001f ? -shotDir.normalized : Vector3.up);
        // The Piloto blood fountains emit along their local +Y ("up"), so align
        // +Y with the surface normal to spurt the blood out of the wound toward
        // the shooter. sprayRotationOffset stays available for per-prefab tweaks.
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(sprayRotationOffset);

        // Push the spawn slightly back along the normal, into the body, so the
        // blood looks like it erupts from inside the enemy rather than off the
        // capsule collider's surface (which sits a few cm proud of the mesh).
        Vector3 spawnPos = point - dir * sprayDepthInset;
        var fx = Instantiate(sprayPrefab, spawnPos, rot);
        if (!Mathf.Approximately(sprayScale, 1f)) fx.transform.localScale *= sprayScale;

        // Simulate every particle layer in LOCAL space so the blood rides with
        // the enemy. The enemy is parented to a planet orbiting at high world
        // velocity; in World space the emitter races away from its just-emitted
        // particles, so they streak off into space and vanish (this is why the
        // droplets showed in the editor — sim paused — but not in play). Local
        // space pins them to the moving body.
        ForceLocalSimulationSpace(fx);

        // Attach to the nearest BONE of the hit enemy (not just the root) so the
        // blood rides the body through animation AND ragdoll: on death that bone
        // becomes a ragdoll bone, so the fountain falls and tumbles with the
        // corpse, still bleeding, instead of being left behind at the standing
        // spot. Falls back to the nearest planet for non-enemy hits so
        // floating-origin shifts don't teleport it.
        Transform parent = attachTo != null
            ? FindNearestDescendant(attachTo, spawnPos)
            : ResolveNearestPlanet(point);
        if (parent != null) fx.transform.SetParent(parent, worldPositionStays: true);

        DisableColliders(fx);

        // Capture the target scale AFTER parenting — SetParent rescales
        // localScale to preserve world size, so this is the correct local target
        // for the grow/shrink animation (handles non-uniformly scaled enemies).
        Vector3 targetScale = fx.transform.localScale;
        var anim = fx.GetComponent<BloodSpray>();
        if (anim == null) anim = fx.AddComponent<BloodSpray>();
        anim.Init(sprayLifetime, sprayGrowSeconds, sprayShrinkSeconds, targetScale);
    }

    /// <summary>
    /// Spawn a blood pool lying flat on the surface at an enemy's feet on death.
    /// </summary>
    public void SpawnPool(Vector3 groundPoint, Vector3 up, Transform planet)
    {
        if (poolPrefab == null) return;

        Vector3 u = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;

        // Projector path: a blood-decal Projector conforms to curved terrain.
        // Place it above the ground looking straight down (-up) so it projects
        // the decal onto whatever ground is below, following the curve.
        if (poolPrefab.GetComponent<Projector>() != null)
        {
            Vector3 projPos = groundPoint + u * poolProjectorHeight;
            var projFx = Instantiate(poolPrefab, projPos, Quaternion.LookRotation(-u));
            if (planet != null) projFx.transform.SetParent(planet, worldPositionStays: true);
            var fader = projFx.GetComponent<BloodDecalFader>();
            if (fader == null) fader = projFx.AddComponent<BloodDecalFader>();
            fader.Init(poolLingerSeconds, poolFadeSeconds, poolScale);
            return;
        }

        // Legacy particle-splat path: lie the splat flat on the local surface
        // tangent and lift it a hair off the ground to avoid z-fighting.
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, u);
        Vector3 pos = groundPoint + u * 0.03f;

        var fx = Instantiate(poolPrefab, pos, rot);
        if (!Mathf.Approximately(poolScale, 1f)) fx.transform.localScale *= poolScale;

        // Local sim space + planet parenting so the splat's particles stay put
        // on the ground as the planet orbits (World space would streak them off).
        ForceLocalSimulationSpace(fx);
        if (planet != null) fx.transform.SetParent(planet, worldPositionStays: true);

        DisableColliders(fx);

        var pool = fx.GetComponent<BloodPool>();
        if (pool == null) pool = fx.AddComponent<BloodPool>();
        pool.Init(poolLingerSeconds, poolFadeSeconds);
    }

    /// <summary>
    /// Play a random blood splash at an enemy's body centre on any player hit.
    /// Attaches to the nearest bone so it rides the body.
    /// </summary>
    public void SpawnDamageSplash(Vector3 center, Transform attachTo, float bodyScale = 1f)
    {
        if (damageSplashPrefabs == null || damageSplashPrefabs.Length == 0) return;
        var prefab = damageSplashPrefabs[Random.Range(0, damageSplashPrefabs.Length)];
        if (prefab == null) return;

        var fx = Instantiate(prefab, center, Quaternion.identity);
        // Scale by the enemy's body size so the centre splash reaches outside a
        // big enemy (the ~3x elite) instead of being swallowed inside it.
        float s = damageSplashScale * Mathf.Max(0.01f, bodyScale);
        if (!Mathf.Approximately(s, 1f)) fx.transform.localScale *= s;

        ForceLocalSimulationSpace(fx);

        Transform parent = attachTo != null
            ? FindNearestDescendant(attachTo, center)
            : ResolveNearestPlanet(center);
        if (parent != null) fx.transform.SetParent(parent, worldPositionStays: true);

        DisableColliders(fx);
        Destroy(fx, damageSplashLifetime);
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

    // Nearest transform (bone) within root's hierarchy to a world point — used
    // to attach the spray to the body part it hit so it follows animation and
    // ragdoll. Cheap enough for a per-shot event (rig is a few dozen bones).
    static Transform FindNearestDescendant(Transform root, Vector3 worldPoint)
    {
        Transform best = root;
        float bestSqr = (root.position - worldPoint).sqrMagnitude;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            float d = (t.position - worldPoint).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = t; }
        }
        return best;
    }

    static void ForceLocalSimulationSpace(GameObject go)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
        }
    }
}
