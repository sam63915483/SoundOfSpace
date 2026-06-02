using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance { get; private set; }

    [Header("References")]
    public GameObject enemyPrefab;
    // Optional elite variant. When assigned, every 4th spawn (after 3 regulars)
    // uses this prefab instead, keeping a 1:3 ratio of elite:regular over the
    // session. Both share enemyPrefab's MaxConcurrent / SpawnInterval — the
    // elite doesn't double the spawn budget.
    public GameObject enemy2Prefab;
    public Transform playerOverride;

    EnemyController prefabConfig;
    PlayerController playerCtl;
    readonly List<EnemyController> activeEnemies = new List<EnemyController>();
    float timer;
    int _regularsSinceLastEnemy2;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (enemyPrefab != null)
            prefabConfig = enemyPrefab.GetComponent<EnemyController>();

        if (prefabConfig == null)
            Debug.LogError("[EnemySpawner] enemyPrefab is missing or has no EnemyController.");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (prefabConfig == null) return;
        if (playerCtl == null) playerCtl = FindObjectOfType<PlayerController>(true);

        // Prune destroyed enemies.
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
            if (activeEnemies[i] == null) activeEnemies.RemoveAt(i);

        timer += Time.deltaTime;
        if (timer < prefabConfig.SpawnInterval) return;
        timer = 0f;
        TrySpawn();
    }

    void TrySpawn()
    {
        if (activeEnemies.Count >= prefabConfig.MaxConcurrent) return;

        bool spawnElite = enemy2Prefab != null && _regularsSinceLastEnemy2 >= 3;
        GameObject targetPrefab = spawnElite ? enemy2Prefab : enemyPrefab;

        Transform player = playerOverride != null ? playerOverride
                         : (playerCtl != null ? playerCtl.transform : null);
        if (player == null) return;

        Camera cam = playerCtl != null ? playerCtl.Camera : Camera.main;
        if (cam == null) return;

        CelestialBody planet = GetNearestPlanet(player.position);
        CelestialBody sun = GetSun();
        if (planet == null || sun == null) return;

        Vector3 playerDir = (player.position - planet.Position).normalized;
        Vector3 sunDir = (sun.Position - planet.Position).normalized;
        if (Vector3.Dot(playerDir, sunDir) >= prefabConfig.DarkSideDotThreshold) return;

        float halfConeCos = Mathf.Cos(prefabConfig.ViewConeAngleDeg * 0.5f * Mathf.Deg2Rad);
        Vector3 surfaceUp = (player.position - planet.Position).normalized;
        int surfaceMask = ~((1 << 9) | (1 << 11) | (1 << 12)); // exclude Ship, Sun, FishPreview
        const float capsuleHalfHeight = 1.0f; // primitive capsule extends 1m above its centre

        for (int attempt = 0; attempt < prefabConfig.SpawnAttemptsPerTick; attempt++)
        {
            // Random tangent direction around the player's local up.
            float angle = Random.Range(0f, 360f);
            Vector3 ortho = Vector3.Cross(surfaceUp,
                Mathf.Abs(Vector3.Dot(surfaceUp, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 tangent = Quaternion.AngleAxis(angle, surfaceUp) * ortho;

            float dist = Random.Range(prefabConfig.MinSpawnDistance, prefabConfig.MaxSpawnDistance);
            Vector3 candidate = player.position + tangent * dist;

            Vector3 dirFromCenter = (candidate - planet.Position).normalized;

            // Reject if inside the camera's view cone.
            Vector3 toCandidate = (candidate - player.position).normalized;
            if (Vector3.Dot(cam.transform.forward, toCandidate) > halfConeCos) continue;

            // Keep the spawn point on the dark side of the planet.
            if (Vector3.Dot(dirFromCenter, sunDir) >= prefabConfig.DarkSideDotThreshold) continue;

            // Cast inward from above the nominal surface to find the *actual* terrain
            // height at this column, accounting for hills/mountains.
            Vector3 rayOrigin = planet.Position + dirFromCenter * (planet.radius + 100f);
            if (!Physics.Raycast(rayOrigin, -dirFromCenter, out RaycastHit hit,
                                 planet.radius * 2f, surfaceMask, QueryTriggerInteraction.Ignore))
                continue;

            // Don't spawn on top of the player.
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;

            // Don't spawn on top of trees — the raycast happily picks up a
            // SpawnedTree's collider since trees aren't on a special layer,
            // and we'd plant an enemy on top of the canopy with no chase
            // path back down.
            if (hit.collider.GetComponentInParent<SpawnedTree>() != null) continue;

            // Don't spawn underwater. Mirrors TreeSpawner / MushroomSpawner
            // / AlienNPCSpawner — if the terrain hit point is below the
            // planet's ocean radius, this column is sea floor. Falling-
            // through-water animation looked off and the enemy capsule
            // probe loop produced jittery movement on submerged terrain.
            var gen = planet.GetComponentInChildren<CelestialBodyGenerator>();
            if (gen != null)
            {
                float oceanR = gen.GetOceanRadius();
                if (oceanR > 0f && (hit.point - planet.Position).magnitude < oceanR)
                    continue;
            }

            // Sit the capsule one half-height above the hit, then add the configured
            // drop margin so it falls onto the ground naturally.
            Vector3 surfacePos = hit.point + dirFromCenter * (capsuleHalfHeight + prefabConfig.SpawnSurfaceOffset);

            // Re-confirm the view cone with the actual surface point.
            Vector3 toSurface = (surfacePos - player.position).normalized;
            if (Vector3.Dot(cam.transform.forward, toSurface) > halfConeCos) continue;

            if (LebronLight.IsPositionProtected(surfacePos)) continue;
            if (TorchAura.IsPositionProtected(surfacePos)) continue;
            // Concert speakers: no enemies within enemyBlockRadius (100m by
            // default) of any concert speaker, so audience members and the
            // concert experience aren't interrupted by combat.
            if (ConcertStageHub.IsBlockedForEnemy(surfacePos)) continue;

            Vector3 forwardLook = Vector3.ProjectOnPlane((player.position - surfacePos).normalized, dirFromCenter);
            if (forwardLook.sqrMagnitude < 0.0001f)
                forwardLook = Vector3.Cross(dirFromCenter, Vector3.right).normalized;
            Quaternion rot = Quaternion.LookRotation(forwardLook.normalized, dirFromCenter);

            GameObject go = Instantiate(targetPrefab, surfacePos, rot);
            // Parent to the planet so the enemy rides its transform — orbital
            // motion + EndlessManager origin shifts come for free, no registration.
            go.transform.SetParent(planet.transform, true);
            var ec = go.GetComponent<EnemyController>();
            if (ec != null) activeEnemies.Add(ec);
            if (spawnElite) _regularsSinceLastEnemy2 = 0;
            else            _regularsSinceLastEnemy2++;
            return;
        }
    }

    public void OnEnemyDestroyed(EnemyController e)
    {
        activeEnemies.Remove(e);
    }

    // ── Save / load API ──────────────────────────────────────────────────
    // Exposed so SaveCollector can round-trip the spawner's cooldown — saving
    // 9.9s into a 10s spawn interval and reloading shouldn't reset to 0.
    public float TimerForSave => timer;
    public int RegularsSinceEliteForSave => _regularsSinceLastEnemy2;

    // Re-instantiates a saved enemy. Resolves the right prefab from `kind`
    // (regular/elite), parents to the matching CelestialBody so it inherits
    // orbital motion + floating-origin shifts, restores health. World pos/rot
    // come from the body-relative xform in the save.
    public void SpawnFromSave(EnemySave save)
    {
        if (save == null) return;
        GameObject prefab = save.kind == "elite" && enemy2Prefab != null
            ? enemy2Prefab
            : enemyPrefab;
        if (prefab == null) return;

        CelestialBody planet = ResolveBodyByName(save.xform.bodyName);
        Vector3 worldPos;
        Quaternion worldRot;
        if (planet != null)
        {
            worldPos = planet.Position + planet.transform.rotation * save.xform.localPos;
            worldRot = planet.transform.rotation * save.xform.localRot;
        }
        else
        {
            // Legacy / out-of-range save: BodyRelativeTransform stored world-
            // absolute when bodyName == "". Re-parent to nearest planet so the
            // enemy still rides orbital motion.
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

    public void RestoreTimerState(float timerValue, int regularsSinceElite)
    {
        timer = timerValue;
        _regularsSinceLastEnemy2 = regularsSinceElite;
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
