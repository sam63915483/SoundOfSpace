using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// AUTHORED NPC SPAWNER (2026-09-03, Sam's "place an empty and get a named NPC").
///
/// Drop this on an EMPTY GameObject parented anywhere under a planet
/// (CelestialBody). At runtime it spawns one alien there: raycast down onto the
/// terrain along the planet radial (the same seating formula the streamed
/// AlienNPCSpawner uses), parented through the planet's PHYSICS frame, on the
/// WorldProp layer, with a wave (NPCWaveAnimation), a leashed stroll
/// (AlienWander) and a talk trigger. The empty is a GENERAL AREA marker: the
/// NPC stands on whatever ground is under it, and wanders around that spot.
///
/// Pair it with an AuthoredNPCTalk (or a subclass) on the same empty to give
/// the NPC a name and dialogue. Quest logic (LostKidQuest) drives the body
/// through the public Wander/Body handles; nothing here knows about quests.
///
/// The prefab is deliberately NOT stripped of anything: same instantiate path
/// as the streamed aliens. It gets no AlienNPCDamageable -- an authored NPC is
/// not shootable (killing a quest-giver is a bug, not a feature).
/// </summary>
public class AuthoredNPCSpawner : MonoBehaviour
{
    [Header("Who")]
    [Tooltip("Display name, shown in the talk prompt.")]
    public string npcName = "Someone";
    [Tooltip("Alien prefab. Leave EMPTY to borrow one from the scene's AlienNPCSpawner (borrowPrefabIndex picks which of its 10).")]
    public GameObject prefab;
    [Tooltip("Which of the streamed spawner's prefabs to borrow when 'prefab' is empty (0-9).")]
    public int borrowPrefabIndex = 0;
    [Tooltip("Uniform scale. Streamed aliens roll 2-5; a kid is ~1.6, an adult ~3.5.")]
    public float scale = 3.5f;

    [Header("Placement")]
    [Tooltip("Spawn on Start at this empty's position. Turn OFF when a quest script decides where the NPC starts (it calls SpawnAtWorld itself).")]
    public bool autoSpawn = true;
    [Tooltip("Layers the seating raycast may hit. Default: the Body (terrain) layer.")]
    public LayerMask groundMask = 0;
    public float surfaceRayHeight = 100f;
    [Tooltip("Push into the ground along the radial (metres). Positive = deeper.")]
    public float groundOffset = 0f;
    public float groundEmbedPerScale = 0.04f;
    [Tooltip("Seconds between seating retries while the planet's terrain collider is still generating.")]
    public float retryInterval = 0.5f;

    [Header("Wander")]
    public bool wander = true;
    [Tooltip("Metres the NPC strolls from its spot.")]
    public float wanderRadius = 8f;
    public float wanderSpeed = 1.6f;
    public float wanderIdleMin = 2f;
    public float wanderIdleMax = 6f;
    [Tooltip("Base distance at which a nearby player freezes the stroll (2.5 m per scale unit is added).")]
    public float wanderPauseDistance = 6f;
    [Tooltip("Ground steeper than this (degrees from radial-up) is not walked onto.")]
    public float maxSurfaceAngle = 35f;
    [Tooltip("Periodic wave + head tracking (NPCWaveAnimation).")]
    public bool wave = true;

    [Header("Talk trigger (prefab-local, multiplied by scale)")]
    public Vector3 triggerSize = new Vector3(2.5f, 4f, 2.5f);
    public Vector3 triggerCenter = new Vector3(0f, 2f, 0f);

    public GameObject Body { get; private set; }
    public AlienWander Wander { get; private set; }
    public AuthoredNPCBody Relay { get; private set; }
    public CelestialBody Planet { get; private set; }
    public bool Spawned => Body != null;
    /// Fired once the body exists and is seated. Late subscribers should check Spawned first.
    public event Action<AuthoredNPCSpawner> OnSpawned;

    float _bottomY;
    float _seatDepth;
    float _oceanR;
    Coroutine _spawnRoutine;

    void Awake()
    {
        Planet = GetComponentInParent<CelestialBody>();
        if (Planet == null)
            Debug.LogError($"[AuthoredNPC:{npcName}] {name} is not under a CelestialBody -- parent the empty to a planet.", this);
        if (groundMask.value == 0) groundMask = LayerMask.GetMask("Body");
        // Never seat on other spawners' props or a parked ship.
        groundMask &= ~SpawnerCubeface.WorldSpawnExcludeMask;
    }

    void Start()
    {
        if (autoSpawn) SpawnAtWorld(transform.position);
    }

    GameObject ResolvePrefab()
    {
        if (prefab != null) return prefab;
        var streamed = FindObjectOfType<AlienNPCSpawner>(true);
        if (streamed != null && streamed.alienPrefabs != null && streamed.alienPrefabs.Length > 0)
        {
            int i = Mathf.Clamp(borrowPrefabIndex, 0, streamed.alienPrefabs.Length - 1);
            return streamed.alienPrefabs[i];
        }
        return null;
    }

    /// <summary>Spawn the NPC on the ground under this world point (radially). No-op if already spawned.</summary>
    public void SpawnAtWorld(Vector3 worldPos)
    {
        if (Body != null || Planet == null) return;
        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = StartCoroutine(SpawnRoutine(RadialDirWorld(worldPos)));
    }

    /// Planet-radial direction (world) through a world point.
    Vector3 RadialDirWorld(Vector3 worldPos)
    {
        Vector3 d = worldPos - Planet.transform.position;   // marker + planet transform: same (render) clock
        return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up;
    }

    IEnumerator SpawnRoutine(Vector3 dirW)
    {
        var pf = ResolvePrefab();
        if (pf == null)
        {
            Debug.LogError($"[AuthoredNPC:{npcName}] no prefab assigned and no AlienNPCSpawner to borrow from.", this);
            yield break;
        }
        _bottomY = SpawnerCubeface.ComputeLocalBottomY(pf);
        _seatDepth = _bottomY * scale + groundOffset + groundEmbedPerScale * scale;
        var gen = Planet.GetComponentInChildren<CelestialBodyGenerator>();
        _oceanR = gen != null ? gen.GetOceanRadius() : 0f;

        // The planet's terrain collider is generated at runtime -- keep trying
        // until the radial raycast finds ground (a few seconds after load).
        Vector3 pos = default, up = default;
        int tries = 0;
        while (!TryFindGround(dirW, out pos, out up))
        {
            if (++tries > 240) { Debug.LogError($"[AuthoredNPC:{npcName}] no ground under {name} after {tries} tries.", this); yield break; }
            yield return new WaitForSeconds(retryInterval);
        }

        float yaw = UnityEngine.Random.Range(0f, 360f);
        Quaternion rot = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);
        var go = Instantiate(pf, pos, rot);
        go.name = npcName;
        go.transform.localScale = Vector3.one * scale;

        if (wave && go.GetComponent<NPCWaveAnimation>() == null) go.AddComponent<NPCWaveAnimation>();

        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = triggerSize;
        trigger.center = triggerCenter;

        // No solid collider, deliberately -- the pre-placed NPCs (Tev, Alien3)
        // are trigger-only too. During dialogue the player is PINNED every
        // physics tick, so overlapping a solid NPC collider became a push/pin
        // fight (the slide-and-jitter Sam saw). The crosshair resolves the
        // silhouette of a collider-less NPC on its own (InteractGaze).

        SpawnerCubeface.ParentToBodyPhysicsFrame(go.transform, Planet);
        SpawnerCubeface.SetLayerRecursively(go, SpawnerCubeface.WorldPropLayer);

        Relay = go.AddComponent<AuthoredNPCBody>();
        Relay.Owner = this;

        Wander = go.AddComponent<AlienWander>();
        if (wander)
        {
            Wander.Configure(Planet, _oceanR, groundMask, _seatDepth, maxSurfaceAngle,
                             wanderRadius, wanderSpeed, wanderIdleMin, wanderIdleMax,
                             wanderPauseDistance + 2.5f * scale, scale);
        }
        else
        {
            // Zero leash + effectively infinite idle: stands still, but the
            // approach/follow machinery is still available to quest scripts.
            Wander.Configure(Planet, _oceanR, groundMask, _seatDepth, maxSurfaceAngle,
                             0f, wanderSpeed, 9999f, 9999f, wanderPauseDistance + 2.5f * scale, scale);
        }

        var fade = go.AddComponent<SpawnFade>();
        fade.BeginFadeIn();

        Body = go;
        _spawnRoutine = null;
        Debug.Log($"[AuthoredNPC:{npcName}] spawned on {Planet.bodyName} at local {go.transform.localPosition}.");
        OnSpawned?.Invoke(this);
    }

    /// <summary>
    /// Ground under a radial direction: world seat position (feet formula applied)
    /// and the radial up. Raycasts resolve against the PHYSICS pose of the planet.
    /// </summary>
    public bool TryFindGround(Vector3 dirW, out Vector3 seatPos, out Vector3 up)
    {
        seatPos = default; up = dirW;
        if (Planet == null) return false;
        Vector3 origin = Planet.Position + dirW * (Planet.radius + surfaceRayHeight);
        if (!Physics.Raycast(origin, -dirW, out RaycastHit hit, Planet.radius * 2f,
                             groundMask, QueryTriggerInteraction.Ignore))
            return false;
        up = (hit.point - Planet.Position).normalized;
        seatPos = hit.point - up * _seatDepth;
        return true;
    }

    /// <summary>
    /// Re-seat the spawned body on the ground under a world point (a follower that
    /// fell too far behind, a returned kid appearing at home). Planet-local write
    /// through the wander so its state stays coherent.
    /// </summary>
    public bool TeleportNear(Vector3 worldPos)
    {
        if (Body == null || Planet == null) return false;
        if (!TryFindGround(RadialDirWorld(worldPos), out Vector3 seat, out Vector3 up)) return false;
        var rb = Planet.Rigidbody;
        Quaternion inv = rb != null ? Quaternion.Inverse(rb.rotation) : Quaternion.Inverse(Planet.transform.rotation);
        Vector3 origin = rb != null ? rb.position : Planet.transform.position;
        Vector3 local = inv * (seat - origin);
        Wander.TeleportLocal(local);
        Body.transform.localRotation = inv * Quaternion.FromToRotation(Vector3.up, up);
        return true;
    }

    /// <summary>World position a given number of metres from a body, along the tangent plane.</summary>
    public static Vector3 Beside(Transform body, float metres)
    {
        Vector3 up = body.up;
        Vector3 side = Vector3.ProjectOnPlane(body.right, up);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.ProjectOnPlane(body.forward, up);
        return body.position + side.normalized * metres;
    }

    void OnDestroy()
    {
        if (Body != null) Destroy(Body);
    }
}
