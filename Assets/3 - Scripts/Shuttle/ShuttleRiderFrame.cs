using System.Collections.Generic;
using UnityEngine;

// Rider capture/release for the autopilot flight (handoff §4b).
//
// The mechanism is the intro's proven one, extended to free walking: the local
// player's rigidbody is frozen kinematic for the flight (which silently
// disables the whole n-body/AddForce pipeline — AddForce on a kinematic body
// is a no-op), the player TRANSFORM is parented under the shuttle so both the
// fixed-step pose and the render-rate smoothing carry them for free, and
// PlayerController runs its self-contained rider-movement block
// (PlayerController.RiderMode) in shuttle space.
//
// The player must be UNREGISTERED from EndlessManager while parented — the
// planet (their new ancestor) is registered, and a registered child of a
// registered parent gets double-shifted on an origin rebase (EndlessManager's
// own doc). PlayerPickup does exactly this for held items.
//
// Dropped physics props inside the cabin are frozen kinematic + parented for
// the flight (handoff v1 call) and thawed with the PlayerPickup drop recipe on
// landing (rb.position sync + SyncTransforms — autoSyncTransforms is off).
public static class ShuttleRiderFrame
{
    class FrozenItem
    {
        public Rigidbody rb;
        public Transform prevParent;
        public bool wasKinematic;
        public RigidbodyInterpolation interpolation;
    }

    static bool s_riding;
    static PlayerController s_player;
    static RigidbodyInterpolation s_playerInterpolation;
    static readonly List<FrozenItem> s_items = new List<FrozenItem>();

    // Interior volume: the ShuttleInteriorVolume trigger box if Sam has run the
    // prefab patch; otherwise a bounds fallback computed from the Interior
    // group's renderers.
    static BoxCollider s_volume;
    static Transform s_volumeShuttle;
    static Bounds s_fallbackLocalBounds;
    static bool s_fallbackComputed;

    public static bool Riding => s_riding;

    // Statics survive scene loads and the main menu — a run that ended
    // mid-flight would otherwise leak RiderMode into the next run and the
    // player would load with no movement pipeline (the ShuttleExitDoor
    // OpenedAtTime lesson). A ride never survives a scene load, so reset on
    // EVERY load; NewGameReset calls ResetStatics too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookRunReset()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => ResetStatics();
    }

    public static void ResetStatics()
    {
        s_riding = false;
        s_player = null;
        s_items.Clear();
        s_volume = null;
        s_volumeShuttle = null;
        s_fallbackComputed = false;
        PlayerController.RiderMode = false;
        PlayerController.RiderPlatform = null;
        ShuttleAutopilot.ClientDriven = false;
        // Belt + braces for the stuck-up-override class: a shuttle-owned
        // override (the shuttle itself or its blend proxy) must never survive
        // a scene load. The intro re-sets its own after load, so this is safe.
        var over = PlayerController.UpOverrideTransform;
        if (over != null && (over.name == "ShuttleTravelUpBlendProxy" || over.GetComponent<ShuttleAutopilot>() != null))
            PlayerController.UpOverrideTransform = null;
    }

    // ── Occupancy ────────────────────────────────────────────────────────────
    public static bool AnyoneInside(ShuttleAutopilot pilot)
    {
        var pc = Object.FindObjectOfType<PlayerController>();
        if (pc != null && IsInside(pilot, pc.transform.position)) return true;

        // Remote players: puppets never have enabled colliders, so a trigger
        // could not see them — test their replicated positions directly.
        var puppets = PlanetRelativeSync.AllPuppets;
        for (int i = 0; i < puppets.Count; i++)
        {
            var p = puppets[i];
            if (p == null || p.IsOwner) continue;   // owner's own puppet mirrors the real player
            if (IsInside(pilot, p.transform.position)) return true;
        }
        return false;
    }

    public static bool IsInside(ShuttleAutopilot pilot, Vector3 worldPos)
    {
        ResolveVolume(pilot);

        // The stasis pod counts as aboard REGARDLESS of the volume — it's its
        // own top-level group outside the Interior box, and a player standing
        // in it at countdown 0 was launching uncaptured (flight-recorder run 2:
        // physically bulldozed along by the moving hull, never parented).
        if (s_pod != null && (worldPos - s_pod.position).sqrMagnitude <= 3.5f * 3.5f)
            return true;

        if (s_volume != null)
        {
            Vector3 local = s_volume.transform.InverseTransformPoint(worldPos) - s_volume.center;
            Vector3 half = s_volume.size * 0.55f;   // 10% slack so a jump mid-count doesn't strand you
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        // Fallback: local-space AABB of the cabin groups.
        Vector3 lp = pilot.transform.InverseTransformPoint(worldPos);
        return s_fallbackLocalBounds.Contains(lp);
    }

    static Transform s_pod;

    static void ResolveVolume(ShuttleAutopilot pilot)
    {
        if (s_volumeShuttle == pilot.transform && (s_volume != null || s_fallbackComputed)) return;
        s_volumeShuttle = pilot.transform;
        s_volume = null;
        s_pod = null;
        foreach (var t in pilot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ShuttleInteriorVolume" && s_volume == null)
                s_volume = t.GetComponent<BoxCollider>();
            if (t.name == "StasisPod" && s_pod == null)
                s_pod = t;
        }
        if (s_volume != null || s_fallbackComputed) return;

        // Fallback bounds: FULL renderer bounds (corners, not centres — run 2
        // proved centre-only bounds miss real cabin space) of the Interior AND
        // StasisPod groups, in shuttle-local space, grown for wall clearance.
        s_fallbackComputed = true;
        var b = new Bounds(Vector3.zero, Vector3.one * 4f);
        bool first = true;
        foreach (var t in pilot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Interior" && t.name != "StasisPod") continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                Bounds wb = r.bounds;
                for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    Vector3 corner = wb.center + Vector3.Scale(wb.extents, new Vector3(cx, cy, cz));
                    Vector3 lp = pilot.transform.InverseTransformPoint(corner);
                    if (first) { b = new Bounds(lp, Vector3.zero); first = false; }
                    else b.Encapsulate(lp);
                }
            }
        }
        b.Expand(1.5f);
        s_fallbackLocalBounds = b;
    }

    // ── Capture (COUNTDOWN 0 → LIFTOFF) ─────────────────────────────────────
    public static void CaptureRiders(ShuttleAutopilot pilot)
    {
        if (s_riding) return;

        var pc = Object.FindObjectOfType<PlayerController>();
        if (pc != null && IsInside(pilot, pc.transform.position))
        {
            s_player = pc;
            var rb = pc.Rigidbody;
            s_playerInterpolation = rb.interpolation;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;                     // no n-body gravity in the cabin (intro recipe)
            pc.transform.SetParent(pilot.transform, true);

            var endless = Object.FindObjectOfType<EndlessManager>();
            if (endless != null) endless.UnregisterPhysicsObject(pc.transform);

            PlayerController.UpOverrideTransform = pilot.transform;
            PlayerController.RiderMode = true;
            PlayerController.RiderPlatform = pilot.transform;
            FallDamage.Suppressed = true;
        }

        CaptureLooseItems(pilot);
        s_riding = true;
    }

    static void CaptureLooseItems(ShuttleAutopilot pilot)
    {
        s_items.Clear();
        foreach (var rb in Object.FindObjectsOfType<Rigidbody>())
        {
            if (rb == null || rb.isKinematic) continue;
            if (s_player != null && rb == s_player.Rigidbody) continue;
            if (rb.GetComponent<CelestialBody>() != null) continue;
            if (rb.GetComponentInParent<Ship>() != null) continue;
            if (rb.GetComponentInParent<PlayerController>() != null) continue;
            if (!IsInside(pilot, rb.position)) continue;

            var item = new FrozenItem
            {
                rb = rb,
                prevParent = rb.transform.parent,
                wasKinematic = rb.isKinematic,
                interpolation = rb.interpolation,
            };
            rb.interpolation = RigidbodyInterpolation.None;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.transform.SetParent(pilot.transform, true);
            var endless = Object.FindObjectOfType<EndlessManager>();
            if (endless != null) endless.UnregisterPhysicsObject(rb.transform);
            s_items.Add(item);
        }
    }

    /// An item dropped INSIDE the cabin mid-flight (PlayerPickup calls this
    /// from DropObject when RiderMode is on): freeze it onto the shuttle so it
    /// doesn't fly out the back the moment the frame moves. Settled onto the
    /// floor with a short local raycast so it doesn't hover where it was let go.
    public static void AdoptDroppedItem(Rigidbody rb)
    {
        if (!s_riding || rb == null) return;
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) return;

        var item = new FrozenItem
        {
            rb = rb,
            prevParent = null,   // dropped items normally live at scene root
            wasKinematic = false,
            interpolation = rb.interpolation,
        };
        rb.interpolation = RigidbodyInterpolation.None;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        Vector3 up = pilot.transform.up;
        if (Physics.Raycast(rb.transform.position + up * 0.2f, -up, out RaycastHit hit, 4f,
                            ShuttleAutopilot.GroundMask, QueryTriggerInteraction.Ignore))
            rb.transform.position = hit.point + up * 0.15f;

        rb.transform.SetParent(pilot.transform, true);
        var endless = Object.FindObjectOfType<EndlessManager>();
        if (endless != null) endless.UnregisterPhysicsObject(rb.transform);
        s_items.Add(item);
    }

    // ── Release (LANDING → PARKED) ───────────────────────────────────────────
    public static void ReleaseRiders(ShuttleAutopilot pilot)
    {
        // Release on EVIDENCE, not bookkeeping: playtest 6 had s_riding
        // cleared mid-flight by an outside caller, so the early-return here
        // left the player parented+kinematic inside the parked shuttle — and
        // whatever unfroze them later never gave them the planet's orbital
        // velocity (the "landed, then launched into space a second later").
        var evidencePc = s_player != null ? s_player : Object.FindObjectOfType<PlayerController>();
        bool playerRiding = evidencePc != null
            && (PlayerController.RiderMode || evidencePc.transform.IsChildOf(pilot.transform));
        if (!s_riding && !playerRiding) return;
        if (s_player == null && playerRiding)
        {
            s_player = evidencePc;
            s_playerInterpolation = RigidbodyInterpolation.Interpolate;   // the player's standard mode
        }
        s_riding = false;

        var body = pilot.CurrentBody;
        Vector3 bodyVel = body != null ? body.velocity : Vector3.zero;
        var endless = Object.FindObjectOfType<EndlessManager>();

        if (s_player != null)
        {
            var pc = s_player;
            var rb = pc.Rigidbody;
            PlayerController.RiderMode = false;
            PlayerController.RiderPlatform = null;

            // Seat from AUTHORITATIVE state, not the transform: the player's
            // shuttle-LOCAL pose (exact — locals are never stale) composed
            // with the autopilot's physics-frame shuttle pose. Reading
            // pc.transform.position here grabbed last frame's RENDER pose —
            // harmless on simulated planets, but a co-orbit follower (Icey)
            // teleports ~2.7 m per tick, so the stale seat landed inside the
            // floor/walls and PhysX blasted the player out (playtest 6).
            Vector3 seatLocal = pc.transform.localPosition;
            Quaternion seatLocalRot = pc.transform.localRotation;
            pilot.GetWorldPose(out Vector3 shuttleW, out Quaternion shuttleR);
            Vector3 seat = shuttleW + shuttleR * seatLocal;
            Quaternion seatRot = shuttleR * seatLocalRot;

            pc.transform.SetParent(null, true);
            pc.transform.SetPositionAndRotation(seat, seatRot);
            rb.isKinematic = false;
            rb.interpolation = s_playerInterpolation;
            rb.position = seat;
            rb.rotation = seatRot;
            rb.angularVelocity = Vector3.zero;
            pc.SetVelocity(bodyVel);               // inherit the new planet's orbit
            Physics.SyncTransforms();
            if (endless != null) endless.RegisterPhysicsObject(pc.transform);
            if (CameraEffectsManager.Instance != null && CameraEffectsManager.Instance.TransformFX != null)
                CameraEffectsManager.Instance.TransformFX.SnapToCurrentPlayer();

            FallDamage.Suppressed = false;
            pilot.BlendRiderUpOut(1.5f);           // never snap the up-frame (intro proxy recipe)
            s_player = null;
        }

        foreach (var item in s_items)
        {
            if (item.rb == null) continue;
            // Picked back up mid-flight (PlayerPickup reparents it to the hold
            // position) — the pickup system owns it now; don't thaw it.
            if (!item.rb.transform.IsChildOf(pilot.transform)) continue;
            item.rb.transform.SetParent(item.prevParent, true);
            item.rb.isKinematic = item.wasKinematic;
            item.rb.interpolation = item.interpolation;
            if (!item.rb.isKinematic)
            {
                item.rb.position = item.rb.transform.position;
                item.rb.rotation = item.rb.transform.rotation;
                item.rb.velocity = bodyVel;
                item.rb.angularVelocity = Vector3.zero;
            }
            if (endless != null) endless.RegisterPhysicsObject(item.rb.transform);
        }
        if (s_items.Count > 0) Physics.SyncTransforms();
        s_items.Clear();
    }
}
