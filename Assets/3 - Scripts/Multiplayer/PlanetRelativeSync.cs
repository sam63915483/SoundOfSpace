using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// Owner-authoritative planet-local pose sync for the LAN proof test.
/// Hard rule: no world-space value ever crosses the network. Each machine's
/// world coordinates are meaningless elsewhere (independent sim start times +
/// independent floating-origin rebases). A pose relative to the home planet's
/// transform is rebase-invariant: EndlessManager shifts the planet and
/// everything on it by the same offset, so the relative pose never changes.
public class PlanetRelativeSync : NetworkBehaviour
{
    public string planetName = "Humble Abode";
    public float remoteLerpSpeed = 12f;
    public float remoteSnapDistance = 25f;

    readonly NetworkVariable<Vector3> netLocalPos = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Quaternion> netLocalRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> netPoseValid = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Spawn pose for a joining client, written by the SERVER in planet-local
    // space. NetworkVariables (not an RPC) so delivery rides the guaranteed
    // spawn-time state sync — a one-shot RPC can race the object spawn and be
    // dropped, which left the joiner frozen at the wrong place in playtest 1.
    readonly NetworkVariable<Vector3> netSpawnLocalPos = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<Quaternion> netSpawnLocalRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> netSpawnPoseSet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // The player Animator runs off exactly two parameters (PlayerController
    // line ~548): "Speed" and "Grounded". Sync them so remote avatars play
    // idle/walk/run instead of freezing in the airborne default pose.
    readonly NetworkVariable<float> netAnimSpeed = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> netAnimGrounded = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // The host stashes the despawned scene player's planet-local pose here
    // (set by MultiplayerTestUI) so its own network player spawns exactly
    // where the scene player stood.
    public static bool HasPendingHostPose;
    public static Vector3 PendingHostLocalPos;
    public static Quaternion PendingHostLocalRot;

    Transform planet;
    CelestialBody planetBody;
    EndlessManager endless;
    Rigidbody rb;
    bool subscribedToOriginUpdate;
    bool ownerPoseReady;
    bool remoteEverPlaced;
    float nextPlanetSearchTime;
    Renderer[] remoteRenderers;
    PlayerController frozenController;

    // Session-state readouts for MultiplayerTestUI's debug line.
    public bool OwnerPoseReady => ownerPoseReady;
    public bool RemotePoseValid => netPoseValid.Value;
    public bool RemotePlaced => remoteEverPlaced;
    /// Distance from planet center as this machine RENDERS this body.
    public float ShownAltitude => planet != null
        ? Vector3.Distance(planet.position, transform.position) : -1f;
    /// Distance from planet center in the pose the OWNER machine published.
    public float SyncedAltitude => netLocalPos.Value.magnitude;

    Animator animator;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>(true);
    }

    public override void OnNetworkSpawn()
    {
        TryResolveRefs();

        if (IsOwner)
        {
            if (IsServer)
            {
                if (HasPendingHostPose && planet != null)
                {
                    PlaceOwner(PendingHostLocalPos, PendingHostLocalRot);
                    HasPendingHostPose = false;
                }
                // If the planet wasn't resolvable yet, FixedUpdate consumes the
                // pending pose as soon as it is.
                ownerPoseReady = true;
            }
            else
            {
                // Joining client: the raw spawn position came from the server's
                // world space and is meaningless here. Fully freeze — physics
                // AND input — until the server's planet-local spawn pose
                // arrives (a kinematic body still moves via MovePosition, so
                // the controller must be disabled too or inputs drive it).
                if (rb != null) rb.isKinematic = true;
                frozenController = GetComponent<PlayerController>();
                if (frozenController != null) frozenController.enabled = false;
                Debug.Log("[MP] Own player frozen, waiting for spawn pose from host");
            }
        }
        else
        {
            // Remote avatar: hidden until the first valid pose arrives.
            SetRemoteVisible(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
    }

    public override void OnDestroy()
    {
        Unsubscribe();
        base.OnDestroy();
    }

    void Unsubscribe()
    {
        if (subscribedToOriginUpdate && endless != null)
            endless.PostFloatingOriginUpdate -= PlaceRemote;
        subscribedToOriginUpdate = false;
    }

    void TryResolveRefs()
    {
        if (planet == null && Time.unscaledTime >= nextPlanetSearchTime)
        {
            nextPlanetSearchTime = Time.unscaledTime + 0.5f;
            foreach (var b in NBodySimulation.Bodies)
            {
                if (b != null && b.bodyName == planetName)
                {
                    planet = b.transform;
                    planetBody = b;
                    break;
                }
            }
        }
        if (endless == null)
            endless = FindObjectOfType<EndlessManager>();
    }

    void Update()
    {
        // Joining client: poll for the server-written spawn pose, then unfreeze.
        if (!IsSpawned || !IsOwner || IsServer || ownerPoseReady) return;
        TryResolveRefs();
        if (planet == null || !netSpawnPoseSet.Value) return;

        PlaceOwner(netSpawnLocalPos.Value, netSpawnLocalRot.Value);
        if (frozenController != null) frozenController.enabled = true;
        ownerPoseReady = true;
        Debug.Log($"[MP] Own player placed at planet-local {netSpawnLocalPos.Value} and unfrozen");
    }

    void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsOwner) { OwnerPublish(); return; }
        TryResolveRefs();

        // Place AFTER EndlessManager's origin update (its event fires at the end
        // of its LateUpdate every frame) so a rebase can never leave the avatar
        // one frame stale. Fallback to plain LateUpdate placement in scenes
        // without an EndlessManager.
        if (!subscribedToOriginUpdate && endless != null)
        {
            endless.PostFloatingOriginUpdate += PlaceRemote;
            subscribedToOriginUpdate = true;
        }
        if (!subscribedToOriginUpdate)
            PlaceRemote();
    }

    // WYSIWYG sampling, at render time: the player's RENDERED pose measured
    // against the planet's RENDERED transform — the exact on-screen
    // relationship between body and terrain. Physics copies, interpolation
    // copies, and collider-sync copies of these positions disagree by up to a
    // physics tick of planet motion (~2 m at this orbit speed); sampling and
    // display both purely in render space makes every such mismatch cancel.
    void OwnerPublish()
    {
        TryResolveRefs();
        if (planet == null) return;

        if (IsServer && HasPendingHostPose)
        {
            PlaceOwner(PendingHostLocalPos, PendingHostLocalRot);
            HasPendingHostPose = false;
        }
        if (!ownerPoseReady) return;

        netLocalPos.Value = planet.InverseTransformPoint(transform.position);
        netLocalRot.Value = Quaternion.Inverse(planet.rotation) * transform.rotation;
        if (animator != null)
        {
            netAnimSpeed.Value = animator.GetFloat("Speed");
            netAnimGrounded.Value = animator.GetBool("Grounded");
        }
        if (!netPoseValid.Value) netPoseValid.Value = true;
    }

    // The avatar's smoothed pose LIVES in planet-local space across frames
    // (fields below). NEVER seed the smoothing from the avatar's previous
    // world position: the planet moves ~1.6 m per frame, so a stale world pos
    // re-expressed in the fresh planet frame starts every frame ~1.6 m behind
    // — the lerp then settles into a permanent multi-meter trail opposite the
    // planet's motion (measured 6.3 m; read as sunk-in-ground on one side of
    // the planet, floating on the other). Keeping state in local space makes
    // the avatar effectively planet-parented; planet motion cannot leak in.
    Vector3 smoothedLocalPos;
    Quaternion smoothedLocalRot = Quaternion.identity;

    void PlaceRemote()
    {
        if (this == null || !IsSpawned || IsOwner) return;
        if (planet == null || !netPoseValid.Value) return;

        Vector3 targetLocal = netLocalPos.Value;
        Quaternion targetLocalRot = netLocalRot.Value;

        if (!remoteEverPlaced ||
            (smoothedLocalPos - targetLocal).sqrMagnitude > remoteSnapDistance * remoteSnapDistance)
        {
            smoothedLocalPos = targetLocal;
            smoothedLocalRot = targetLocalRot;
            if (!remoteEverPlaced)
                Debug.Log($"[MP] Remote player {OwnerClientId + 1} now visible at planet-local {targetLocal}");
            remoteEverPlaced = true;
            SetRemoteVisible(true);
        }
        else
        {
            float t = 1f - Mathf.Exp(-remoteLerpSpeed * Time.deltaTime);
            smoothedLocalPos = Vector3.Lerp(smoothedLocalPos, targetLocal, t);
            smoothedLocalRot = Quaternion.Slerp(smoothedLocalRot, targetLocalRot, t);
        }

        transform.SetPositionAndRotation(
            planet.TransformPoint(smoothedLocalPos), planet.rotation * smoothedLocalRot);

        if (animator != null)
        {
            animator.SetFloat("Speed", netAnimSpeed.Value);
            animator.SetBool("Grounded", netAnimGrounded.Value);
        }
    }

    void SetRemoteVisible(bool visible)
    {
        // Re-collect each call (rare — visibility transitions only) so late
        // additions like the runtime-created nametag are included.
        remoteRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in remoteRenderers)
            if (r != null) r.enabled = visible;
    }

    /// Server-side: set the planet-local spawn pose for this (client-owned)
    /// player. The owning client polls netSpawnPoseSet and places itself.
    public void ServerSetSpawnPose(Vector3 localPos, Quaternion localRot)
    {
        if (!IsServer) return;
        netSpawnLocalPos.Value = localPos;
        netSpawnLocalRot.Value = localRot;
        netSpawnPoseSet.Value = true;
    }

    void PlaceOwner(Vector3 localPos, Quaternion localRot)
    {
        // One-shot spawn placement; sub-meter frame imprecision here is fine —
        // the body settles onto the ground physically right after.
        Vector3 worldPos = planet.TransformPoint(localPos);
        Quaternion worldRot = planet.rotation * localRot;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.position = worldPos;
            rb.rotation = worldRot;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.SetPositionAndRotation(worldPos, worldRot);
        Physics.SyncTransforms();

        // Runtime-spawned physics object: the floating origin must shift it.
        TryResolveRefs();
        if (endless != null) endless.RegisterPhysicsObject(transform);
    }

    /// Current planet-local pose of this (owned) player. Used host-side to
    /// compute the joiner's spawn pose.
    public bool TryGetCurrentLocalPose(out Vector3 localPos, out Quaternion localRot)
    {
        localPos = default;
        localRot = Quaternion.identity;
        if (planet == null) return false;
        // Render space, matching OwnerPublish.
        localPos = planet.InverseTransformPoint(transform.position);
        localRot = Quaternion.Inverse(planet.rotation) * transform.rotation;
        return true;
    }
}
