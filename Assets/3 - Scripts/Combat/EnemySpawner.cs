using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Population director for the stealth revamp. Instead of timed waves, it maintains a
/// standing field of docile enemies on the DARK side around the player: spawns fill toward
/// <see cref="populationTarget"/> at 30–120 m, always terrain-occluded from the camera so
/// nothing pops in, keeping a 3:1 regular:elite ratio. Spawns only while the player is
/// actually on a dark side (so we don't pay for enemies you're not near); enemies self-despawn
/// at range and burn in sunlight (see EnemyController). Docile is the enemy's default state.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance { get; private set; }

    [Header("References")]
    public GameObject enemyPrefab;
    public GameObject enemy2Prefab;   // elite; every 4th spawn (3 regular : 1 elite)
    public Transform playerOverride;

    [Header("Population")]
    [Tooltip("Fallback target if no planet is resolved. Normally the target scales with planet size below.")]
    public int populationTarget = 22;
    [Tooltip("Enemies scale with planet size: target = planetRadius × this, clamped. r50 moon → ~6, r200 planet → ~24.")]
    public float enemiesPerRadius = 0.12f;
    public int minPopulation = 5;
    public int maxPopulation = 33;
    [Tooltip("Max spawn distance is also capped to planetRadius × this, so a small moon isn't ringed to its far side.")]
    public float spawnRingRadiusFraction = 1.5f;
    [Tooltip("Max spawned per check tick, so filling the field doesn't spike.")]
    public int maxSpawnsPerTick = 3;
    [Tooltip("Seconds between population checks.")]
    public float spawnCheckInterval = 0.5f;

    [Header("Placement")]
    public float minSpawnDistance = 30f;
    public float maxSpawnDistance = 120f;
    [Tooltip("Dot(playerDir, sunDir) must be below this for the player to count as 'on the dark side'.")]
    public float darkSideDotThreshold = -0.05f;
    [Tooltip("Camera FOV cone (deg) that candidates must avoid (belt-and-braces with the occlusion test).")]
    public float viewConeAngleDeg = 75f;
    public float spawnSurfaceOffset = 3f;
    public int spawnAttemptsPerTick = 18;
    [Tooltip("New spawns must land at least this far from every existing enemy — keeps the field spread out instead of clumped.")]
    public float minEnemySpacing = 15f;

    PlayerController playerCtl;
    readonly List<EnemyController> activeEnemies = new List<EnemyController>();
    float _spawnTimer;
    int _regularsSinceLastEnemy2;

    const float CapsuleHalfHeight = 1.0f;
    static readonly int SurfaceMask = ~((1 << 9) | (1 << 11) | (1 << 12)); // exclude Ship, Sun, FishPreview

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (enemyPrefab == null || enemyPrefab.GetComponent<EnemyController>() == null)
        {
            Debug.LogError("[EnemySpawner] enemyPrefab is missing or has no EnemyController.");
            // Hard stop (mirrors the old prefabConfig null-out): a prefab without an
            // EnemyController would spawn UNBOUNDED — instances only count toward the
            // population target via their EnemyController, so the fill loop never sees them.
            enemyPrefab = null;
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (enemyPrefab == null) return;
        if (playerCtl == null) playerCtl = FindObjectOfType<PlayerController>(true);

        for (int i = activeEnemies.Count - 1; i >= 0; i--)
            if (activeEnemies[i] == null) activeEnemies.RemoveAt(i);

        _spawnTimer += Time.deltaTime;
        if (_spawnTimer < spawnCheckInterval) return;
        _spawnTimer = 0f;

        // Target scales with the planet you're on — a small moon gets far fewer than a big planet.
        Transform p = playerOverride != null ? playerOverride : (playerCtl != null ? playerCtl.transform : null);
        CelestialBody homePlanet = p != null ? GetNearestPlanet(p.position) : null;
        int target = homePlanet != null ? EffectiveTarget(homePlanet.radius) : populationTarget;

        if (activeEnemies.Count >= target) return;

        int budget = maxSpawnsPerTick;
        while (budget-- > 0 && activeEnemies.Count < target)
            if (!TrySpawn()) break;   // stop early if we couldn't place one this tick
    }

    int EffectiveTarget(float planetRadius)
        => Mathf.Clamp(Mathf.RoundToInt(planetRadius * enemiesPerRadius), minPopulation, maxPopulation);

    // Returns true if an enemy was placed.
    bool TrySpawn()
    {
        Transform player = playerOverride != null ? playerOverride
                         : (playerCtl != null ? playerCtl.transform : null);
        if (player == null) return false;
        Camera cam = playerCtl != null ? playerCtl.Camera : Camera.main;
        if (cam == null) return false;

        CelestialBody planet = GetNearestPlanet(player.position);
        CelestialBody sun = GetSun();
        if (planet == null || sun == null) return false;

        Vector3 playerDir = (player.position - planet.Position).normalized;
        Vector3 sunDir = (sun.Position - planet.Position).normalized;
        // Only populate while the player is actually on the dark side.
        if (Vector3.Dot(playerDir, sunDir) >= darkSideDotThreshold) return false;

        bool spawnElite = enemy2Prefab != null && _regularsSinceLastEnemy2 >= 3;
        GameObject targetPrefab = spawnElite ? enemy2Prefab : enemyPrefab;

        float halfConeCos = Mathf.Cos(viewConeAngleDeg * 0.5f * Mathf.Deg2Rad);
        Vector3 surfaceUp = playerDir;
        Vector3 camPos = cam.transform.position;

        // Cap the spawn ring to the planet so a small moon isn't ringed out to 120 m (its far side).
        float effMax = Mathf.Min(maxSpawnDistance, planet.radius * spawnRingRadiusFraction);
        float effMin = Mathf.Min(minSpawnDistance, effMax * 0.5f);

        // Ocean radius is a per-planet constant — resolve ONCE per spawn call, not once per
        // attempt (GetComponentInChildren is a recursive hierarchy walk; 18 attempts × walk
        // was pure waste).
        float oceanRadius = 0f;
        var oceanGen = planet.GetComponentInChildren<CelestialBodyGenerator>();
        if (oceanGen != null) oceanRadius = oceanGen.GetOceanRadius();

        for (int attempt = 0; attempt < spawnAttemptsPerTick; attempt++)
        {
            Vector3 ortho = Vector3.Cross(surfaceUp,
                Mathf.Abs(Vector3.Dot(surfaceUp, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 tangent = Quaternion.AngleAxis(Random.Range(0f, 360f), surfaceUp) * ortho;
            float dist = Random.Range(effMin, effMax);
            Vector3 candidate = player.position + tangent * dist;
            Vector3 dirFromCenter = (candidate - planet.Position).normalized;

            // Keep the spawn on the dark side.
            if (Vector3.Dot(dirFromCenter, sunDir) >= darkSideDotThreshold) continue;

            // Cheap pre-filter: reject candidates roughly in front of the camera.
            Vector3 toCandidate = (candidate - player.position).normalized;
            if (Vector3.Dot(cam.transform.forward, toCandidate) > halfConeCos) continue;

            // Find the real terrain height at this column.
            Vector3 rayOrigin = planet.Position + dirFromCenter * (planet.radius + 100f);
            if (!Physics.Raycast(rayOrigin, -dirFromCenter, out RaycastHit hit,
                                 planet.radius * 2f, SurfaceMask, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (hit.collider.GetComponentInParent<SpawnedTree>() != null) continue;

            // No underwater spawns.
            if (oceanRadius > 0f && (hit.point - planet.Position).magnitude < oceanRadius) continue;

            Vector3 surfacePos = hit.point + dirFromCenter * (CapsuleHalfHeight + spawnSurfaceOffset);

            // NO POP-IN: the spawn point must be occluded from the camera (a hill / the horizon
            // between it and the player). If the camera has a clear line to it, skip.
            if (!Physics.Linecast(camPos, surfacePos, SurfaceMask, QueryTriggerInteraction.Ignore))
                continue;

            // Spacing: don't drop a new enemy on top of an existing one — the field
            // should read as scattered individuals, not a clump.
            bool tooClose = false;
            float spacingSqr = minEnemySpacing * minEnemySpacing;
            for (int e = 0; e < activeEnemies.Count; e++)
            {
                var ex = activeEnemies[e];
                if (ex == null) continue;
                if ((ex.transform.position - surfacePos).sqrMagnitude < spacingSqr) { tooClose = true; break; }
            }
            if (tooClose) continue;

            if (LebronLight.IsPositionProtected(surfacePos)) continue;
            if (TorchAura.IsPositionProtected(surfacePos)) continue;
            if (ConcertStageHub.IsBlockedForEnemy(surfacePos)) continue;

            Vector3 forwardLook = Vector3.ProjectOnPlane((player.position - surfacePos).normalized, dirFromCenter);
            if (forwardLook.sqrMagnitude < 0.0001f)
                forwardLook = Vector3.Cross(dirFromCenter, Vector3.right).normalized;
            Quaternion rot = Quaternion.LookRotation(forwardLook.normalized, dirFromCenter);

            GameObject go = Instantiate(targetPrefab, surfacePos, rot);
            go.transform.SetParent(planet.transform, true);   // ride orbital motion + floating origin
            var ec = go.GetComponent<EnemyController>();
            if (ec != null) activeEnemies.Add(ec);
            if (spawnElite) _regularsSinceLastEnemy2 = 0;
            else            _regularsSinceLastEnemy2++;
            return true;
        }
        return false;
    }

    public void OnEnemyDestroyed(EnemyController e) => activeEnemies.Remove(e);

    // ── Save / load API (unchanged signatures — SaveCollector depends on these) ──
    public float TimerForSave => _spawnTimer;
    public int RegularsSinceEliteForSave => _regularsSinceLastEnemy2;

    public void RestoreTimerState(float timerValue, int regularsSinceElite)
    {
        _spawnTimer = timerValue;
        _regularsSinceLastEnemy2 = regularsSinceElite;
    }

    public void SpawnFromSave(EnemySave save)
    {
        if (save == null) return;
        GameObject prefab = save.kind == "elite" && enemy2Prefab != null ? enemy2Prefab : enemyPrefab;
        if (prefab == null) return;

        CelestialBody planet = ResolveBodyByName(save.xform.bodyName);
        Vector3 worldPos; Quaternion worldRot;
        if (planet != null)
        {
            worldPos = planet.Position + planet.transform.rotation * save.xform.localPos;
            worldRot = planet.transform.rotation * save.xform.localRot;
        }
        else
        {
            worldPos = save.xform.localPos;
            worldRot = save.xform.localRot;
            planet = GetNearestPlanet(worldPos);
            if (planet == null) return;
        }

        GameObject go = Instantiate(prefab, worldPos, worldRot);
        go.transform.SetParent(planet.transform, true);
        var ec = go.GetComponent<EnemyController>();
        if (ec != null)
        {
            ec.SetHealthOnLoad(save.health);
            activeEnemies.Add(ec);
        }
    }

    static CelestialBody ResolveBodyByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        foreach (var b in bodies)
            if (b != null && b.bodyName == name) return b;
        return null;
    }

    static CelestialBody GetNearestPlanet(Vector3 from)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody nearest = null;
        float minDist = float.PositiveInfinity;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) continue;
            float d = (b.Position - from).magnitude - b.radius;
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    static CelestialBody GetSun()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        foreach (var b in bodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) return b;
        return null;
    }
}
