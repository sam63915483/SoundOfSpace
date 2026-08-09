using Unity.Netcode;
using UnityEngine;

/// Owner-authoritative planet-local pose sync (multiplayer LAN test).
///
/// The spawned network player is a pure PUPPET on every machine — the real,
/// scene-placed player rig is NEVER destroyed or modified. On the owner's
/// machine the puppet is invisible and publishes the real player's planet-
/// local pose + animation state; on remote machines it renders that pose.
/// (v8: the old design despawned the real player and promoted the clone,
/// which killed the camera post stack / skybox and left systems holding dead
/// references — the "everything breaks when you press HOST" bugs.)
///
/// Hard rule: no world-space value crosses the network. World coordinates are
/// machine-specific (independent sim start times + independent floating-origin
/// rebases); a pose relative to the home planet's transform is rebase-
/// invariant because EndlessManager shifts the planet and everything on it by
/// the same offset.
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
    // spawn-time state sync — a one-shot RPC can race the object spawn.
    readonly NetworkVariable<Vector3> netSpawnLocalPos = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<Quaternion> netSpawnLocalRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> netSpawnPoseSet = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // The player Animator runs off exactly two parameters (PlayerController
    // ~line 548): "Speed" and "Grounded". Grounded defaults false, which is
    // the airborne pose — unfed remote animators look like they're floating.
    readonly NetworkVariable<float> netAnimSpeed = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> netAnimGrounded = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ── Flashlight ───────────────────────────────────────────────────────
    // The puppet has no flashlight of its own — NetworkPlayerSetup strips every
    // MonoBehaviour off it — so the beam has to be rebuilt on the remote side
    // and driven from here.
    //
    // Pose is sent PLANET-LOCAL for the same reason the body is: world space is
    // meaningless across machines whose floating origins rebase independently.
    // Position as well as rotation, because a beam that starts at the wrong
    // place lights the wrong things even when it points the right way.
    readonly NetworkVariable<bool> netLightOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Vector3> netLightLocalPos = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Quaternion> netLightLocalRot = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    PlayerFlashlight ownerFlashlight;   // owner side
    Light puppetLight;                  // remote side

    // Remote beam shape. Narrower than the real lamp on purpose — see the note
    // in ApplyRemoteFlashlight.
    const float RemoteSpotAngle  = 55f;
    const float RemoteInnerAngle = 22f;
    const float RemoteMaxRange   = 90f;

    Transform planet;
    CelestialBody planetBody;
    EndlessManager endless;
    bool subscribedToOriginUpdate;
    bool ownerPoseReady;
    bool remoteEverPlaced;
    float nextSearchTime;
    Renderer[] remoteRenderers;
    Animator puppetAnimator;

    // Owner side: the REAL scene player being mirrored.
    PlayerController realPlayer;
    Animator realAnimator;

    // The remote avatar's smoothed pose LIVES in planet-local space across
    // frames. NEVER seed smoothing from the avatar's previous world position:
    // the planet moves ~1.6 m per frame, so a stale world pos re-expressed in
    // the fresh planet frame starts every frame behind — the lerp settles into
    // a permanent multi-meter trail opposite the planet's motion (measured
    // 6.3 m; read as sunk-in-ground / floating depending on where you stand).
    Vector3 smoothedLocalPos;
    Quaternion smoothedLocalRot = Quaternion.identity;

    // Session-state readouts for MultiplayerTestUI's debug line.
    public bool OwnerPoseReady => ownerPoseReady;
    public bool RemotePoseValid => netPoseValid.Value;
    public bool RemotePlaced => remoteEverPlaced;
    /// Distance from planet center as this machine RENDERS this puppet.
    public float ShownAltitude => planet != null
        ? Vector3.Distance(planet.position, transform.position) : -1f;
    /// Distance from planet center in the pose the OWNER machine published.
    public float SyncedAltitude => netLocalPos.Value.magnitude;

    void Awake()
    {
        puppetAnimator = GetComponentInChildren<Animator>(true);
    }

    public override void OnNetworkSpawn()
    {
        TryResolveRefs();

        if (IsOwner)
        {
            // Host: the real player already stands wherever it stands — publish
            // immediately. Joining client: hold briefly so the arrival sequence
            // can seat them in the stasis pod first, otherwise the host watches
            // them pop from the default spawn into the pod.
            //
            // FAILS OPEN, deliberately. This used to wait forever for an
            // external system to set a flag; when that system was removed the
            // client published nothing and was permanently invisible to
            // everyone else, with no error anywhere. Now the hold expires and
            // the player becomes visible regardless — a small cosmetic pop is
            // an acceptable worst case, an invisible player is not.
            ownerPoseReady = IsServer;
            ownerHoldUntil = Time.unscaledTime + OwnerHoldSeconds;
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
        bool needPlanet = planet == null;
        bool needEndless = endless == null;
        bool needPlayer = IsOwner && realPlayer == null;
        if (!needPlanet && !needEndless && !needPlayer) return;
        if (Time.unscaledTime < nextSearchTime) return;
        nextSearchTime = Time.unscaledTime + 0.5f;

        if (needPlanet)
        {
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
        if (needEndless)
            endless = FindObjectOfType<EndlessManager>();
        if (needPlayer)
        {
            foreach (var pc in FindObjectsOfType<PlayerController>(true))
            {
                if (pc.GetComponent<NetworkObject>() == null)
                {
                    realPlayer = pc;
                    realAnimator = pc.GetComponentInChildren<Animator>(true);
                    break;
                }
            }
        }
    }

    /// Grace period a joining client holds before publishing, waiting to be
    /// seated by SecondPlayerArrival. A backstop, not a dependency.
    const float OwnerHoldSeconds = 12f;
    float ownerHoldUntil;

    /// Called by the arrival sequence once the local player has been placed.
    /// Releases the publish hold immediately instead of waiting it out.
    public void MarkOwnerPlaced()
    {
        if (IsOwner) ownerPoseReady = true;
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner || ownerPoseReady) return;
        TryResolveRefs();
        if (planet == null || realPlayer == null) return;
        // Either the arrival released us (MarkOwnerPlaced) or the hold ran out.
        if (Time.unscaledTime < ownerHoldUntil) return;
        ownerPoseReady = true;
    }

    void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsOwner) { OwnerPublish(); return; }

        TryResolveRefs();

        // Place AFTER EndlessManager's origin update (its event fires at the
        // end of its LateUpdate every frame) so a rebase can never leave the
        // avatar one frame stale. Fallback to plain LateUpdate placement in
        // scenes without an EndlessManager.
        if (!subscribedToOriginUpdate && endless != null)
        {
            endless.PostFloatingOriginUpdate += PlaceRemote;
            subscribedToOriginUpdate = true;
        }
        if (!subscribedToOriginUpdate)
            PlaceRemote();
    }

    // WYSIWYG sampling at render time: the real player's RENDERED pose
    // measured against the planet's RENDERED transform — the exact on-screen
    // body↔terrain relationship. Physics copies and interpolation copies of
    // these positions disagree by up to a physics tick of planet motion;
    // sampling and display both purely in render space cancels every such
    // mismatch.
    void OwnerPublish()
    {
        TryResolveRefs();
        if (planet == null || realPlayer == null || !ownerPoseReady) return;

        // NEVER publish while paused.
        //
        // At timeScale 0 physics stops, so the player's rigidbody is frozen in
        // world space — but SolarSystemSync keeps applying the host's orbit
        // corrections on unscaled time, which MOVES THE PLANET out from under
        // them. Our pose is expressed relative to that planet, so it drifts
        // further every frame the menu is open and the other player watches us
        // slide off into nowhere. Unpausing snaps it back, which is exactly the
        // reported symptom.
        //
        // A paused player isn't moving, so freezing the broadcast loses nothing.
        if (Time.timeScale == 0f) return;

        Transform rt = realPlayer.transform;
        netLocalPos.Value = planet.InverseTransformPoint(rt.position);
        netLocalRot.Value = Quaternion.Inverse(planet.rotation) * rt.rotation;
        if (realAnimator != null)
        {
            netAnimSpeed.Value = realAnimator.GetFloat("Speed");
            netAnimGrounded.Value = realAnimator.GetBool("Grounded");
        }
        if (!netPoseValid.Value) netPoseValid.Value = true;

        PublishFlashlight();

        // Keep the invisible puppet riding the real player so its transform is
        // never somewhere absurd for anything that might glance at it.
        transform.SetPositionAndRotation(rt.position, rt.rotation);
    }

    /// Owner side: mirror our own flashlight's on/off state and where it is
    /// actually pointing. NetworkVariables only send on change, so a light left
    /// off costs nothing.
    void PublishFlashlight()
    {
        if (ownerFlashlight == null)
        {
            ownerFlashlight = realPlayer != null
                ? realPlayer.GetComponentInChildren<PlayerFlashlight>(true) : null;
            if (ownerFlashlight == null) return;
        }

        var lamp = ownerFlashlight.flashlight;
        bool on = lamp != null && lamp.enabled && lamp.intensity > 0f;
        if (netLightOn.Value != on) netLightOn.Value = on;
        if (!on || lamp == null) return;

        Transform lt = lamp.transform;
        netLightLocalPos.Value = planet.InverseTransformPoint(lt.position);
        netLightLocalRot.Value = Quaternion.Inverse(planet.rotation) * lt.rotation;
    }

    /// Remote side: build a matching spot light once, then drive it. Built from
    /// the LOCAL player's own flashlight settings so both machines agree on cone
    /// angle, range and colour without sending any of it over the wire.
    void ApplyRemoteFlashlight()
    {
        bool on = netLightOn.Value;

        if (puppetLight == null)
        {
            if (!on) return;   // don't build anything until it's actually used

            var go = new GameObject("RemoteFlashlight");
            go.transform.SetParent(transform, false);
            puppetLight = go.AddComponent<Light>();
            puppetLight.type = LightType.Spot;

            // A CLEAN CONE, deliberately not a carbon copy of the local lamp.
            //
            // The first version mirrored the real flashlight exactly — a ~150°
            // spot with a procedural cookie. That looks right to its owner, who
            // sits at the light's origin and never sees the cone side-on. Viewed
            // from outside, which only happens in multiplayer, the cookie
            // projection at that angle degenerates and throws streaks out to the
            // left and right and a bright band along the ground — the artifacts
            // Sam saw. They were always there; nobody had ever been in a position
            // to look at one before.
            //
            // So the remote beam takes its COLOUR and BRIGHTNESS from the real
            // lamp (so it matches, and follows any retune) but uses a sane cone
            // and no cookie.
            var local = FindObjectOfType<PlayerFlashlight>();
            var src = local != null ? local.flashlight : null;

            puppetLight.spotAngle = RemoteSpotAngle;
            puppetLight.innerSpotAngle = RemoteInnerAngle;
            puppetLight.cookie = null;
            // Shadows off: several spot lights with shadows is a real cost, and
            // a shadow-casting beam from a body that is only a puppet buys
            // nothing.
            puppetLight.shadows = LightShadows.None;
            puppetLight.color = src != null ? src.color : Color.white;
            puppetLight.intensity = src != null ? Mathf.Max(0.25f, src.intensity) : 1f;
            puppetLight.range = src != null ? Mathf.Min(src.range, RemoteMaxRange) : RemoteMaxRange;
            // Built-in RP demotes lights to vertex when there are many — same
            // reason PlayerFlashlight forces this on its own lamp.
            puppetLight.renderMode = LightRenderMode.ForcePixel;
        }

        puppetLight.enabled = on;
        if (!on || planet == null) return;

        // Position and aim in world space from the planet-local values, so a
        // floating-origin rebase can't leave the beam behind.
        puppetLight.transform.SetPositionAndRotation(
            planet.TransformPoint(netLightLocalPos.Value),
            planet.rotation * netLightLocalRot.Value);
    }

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

        if (puppetAnimator != null)
        {
            puppetAnimator.SetFloat("Speed", netAnimSpeed.Value);
            puppetAnimator.SetBool("Grounded", netAnimGrounded.Value);
        }

        // Driven here rather than in LateUpdate so the beam is placed in the
        // same pass as the body — a frame of disagreement reads as the light
        // lagging behind the player carrying it.
        ApplyRemoteFlashlight();
    }

    void SetRemoteVisible(bool visible)
    {
        // Re-collect each call (rare — visibility transitions only) so late
        // additions like the runtime-created nametag are included.
        remoteRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in remoteRenderers)
            if (r != null) r.enabled = visible;
    }

    void TeleportRealPlayer(Vector3 localPos, Quaternion localRot)
    {
        Vector3 worldPos = planet.TransformPoint(localPos);
        Quaternion worldRot = planet.rotation * localRot;
        var prb = realPlayer.GetComponent<Rigidbody>();
        if (prb != null)
        {
            prb.position = worldPos;
            prb.rotation = worldRot;
            // Planet-matched velocity: zero RELATIVE velocity. A zero WORLD
            // velocity strands the player while the planet flies on at ~99 m/s
            // — the "launched into space on session start" bug.
            prb.velocity = planetBody != null ? planetBody.velocity : Vector3.zero;
            prb.angularVelocity = Vector3.zero;
        }
        realPlayer.transform.SetPositionAndRotation(worldPos, worldRot);
        Physics.SyncTransforms();
        Debug.Log($"[MP] Local player teleported to planet-local {localPos} (joiner spawn beside host)");
    }

    /// DEAD — nothing calls this, and nothing should.
    ///
    /// This dropped a joiner a few metres above the host. SecondPlayerArrival
    /// owns placement now (guests wake in the stasis pod), and wiring this back
    /// up would fight it and win, which is exactly the bug that put guests on
    /// top of the host. Kept only so the intent is on record; delete it once
    /// the co-op world sync lands and this file is revisited.
    public void ServerSetSpawnPose(Vector3 localPos, Quaternion localRot)
    {
        if (!IsServer) return;
        netSpawnLocalPos.Value = localPos;
        netSpawnLocalRot.Value = localRot;
        netSpawnPoseSet.Value = true;
    }

    /// Current planet-local pose of the REAL player on this machine. Used
    /// host-side to compute the joiner's spawn pose.
    public bool TryGetCurrentLocalPose(out Vector3 localPos, out Quaternion localRot)
    {
        localPos = default;
        localRot = Quaternion.identity;
        TryResolveRefs();
        if (planet == null || realPlayer == null) return false;
        Transform rt = realPlayer.transform;
        localPos = planet.InverseTransformPoint(rt.position);
        localRot = Quaternion.Inverse(planet.rotation) * rt.rotation;
        return true;
    }
}
