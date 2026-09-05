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
    // Where (shuttle-local) the player stood at capture — the safety-net
    // re-seat spot if they ever slip below the cabin floor mid-flight.
    static Vector3 s_playerCaptureLocal;
    static bool s_hasCaptureLocal;

    public static bool TryGetPlayerCaptureLocal(out Vector3 local)
    {
        local = s_playerCaptureLocal;
        return s_hasCaptureLocal;
    }
    static readonly List<FrozenItem> s_items = new List<FrozenItem>();

    // Interior volume: the ShuttleInteriorVolume trigger box if Sam has run the
    // prefab patch; otherwise a bounds fallback computed from the Interior
    // group's renderers.
    static BoxCollider s_volume;
    static Transform s_volumeShuttle;
    static Bounds s_fallbackLocalBounds;
    static bool s_fallbackComputed;

    public static bool Riding => s_riding;

    // ── Prefetched scene refs (playtest 17) ─────────────────────────────────
    // The capture frame (door close) ran FindObjectsOfType<Rigidbody> plus
    // several FindObjectOfType scans, and the release frame ran two more — a
    // deterministic multi-ms frame spike at exactly the two handover moments
    // the landing hitch was felt. Everything is prefetched at COUNTDOWN
    // (a button-press moment) instead; the finds below are cold fallbacks.
    static PlayerController s_pcCache;
    static EndlessManager s_endlessCache;
    static readonly List<Rigidbody> s_rbCandidates = new List<Rigidbody>();

    public static void Prefetch()
    {
        s_pcCache = Object.FindObjectOfType<PlayerController>();
        s_endlessCache = Object.FindObjectOfType<EndlessManager>();
        s_rbCandidates.Clear();
        foreach (var rb in Object.FindObjectsOfType<Rigidbody>())
        {
            if (rb == null || rb.isKinematic) continue;
            if (rb.GetComponent<CelestialBody>() != null) continue;
            if (rb.GetComponentInParent<Ship>() != null) continue;
            if (rb.GetComponentInParent<PlayerController>() != null) continue;
            s_rbCandidates.Add(rb);
        }
    }

    static PlayerController CachedPc()
        => s_pcCache != null ? s_pcCache : (s_pcCache = Object.FindObjectOfType<PlayerController>());

    static EndlessManager CachedEndless()
        => s_endlessCache != null ? s_endlessCache : (s_endlessCache = Object.FindObjectOfType<EndlessManager>());

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
        s_hasCaptureLocal = false;
        s_pcCache = null;
        s_endlessCache = null;
        s_rbCandidates.Clear();
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
        var pc = CachedPc();
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

        var pc = CachedPc();
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

            var endless = CachedEndless();
            if (endless != null) endless.UnregisterPhysicsObject(pc.transform);

            pilot.BlendRiderUpIn(1.2f);   // ease planet-up -> shuttle-up (no door-close snap)
            PlayerController.RiderMode = true;
            PlayerController.RiderPlatform = pilot.transform;
            FallDamage.Suppressed = true;
            s_playerCaptureLocal = pc.transform.localPosition;
            s_hasCaptureLocal = true;
        }

        CaptureLooseItems(pilot);
        s_riding = true;
    }

    static void CaptureLooseItems(ShuttleAutopilot pilot)
    {
        s_items.Clear();
        // Prefetched at countdown — a rigidbody spawned during the 10 s count
        // is missed (it just gets left on the pad), which beats the guaranteed
        // whole-scene scan spike on the door-close frame.
        var endless = CachedEndless();
        foreach (var rb in s_rbCandidates)
        {
            if (rb == null || rb.isKinematic) continue;
            if (s_player != null && rb == s_player.Rigidbody) continue;
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
        var endless = CachedEndless();
        if (endless != null) endless.UnregisterPhysicsObject(rb.transform);
        s_items.Add(item);
    }

    /// Playtest 25 — PRE-PAY the release's one-time physics cost UNDER MOTION.
    /// The probe cornered the felt pop to a single ~24 ms frame ~0.1 s after
    /// release: the freshly-dynamic capsule's FIRST CONTACT with the planet's
    /// 2M-triangle mesh collider (kinematic bodies keep no contact pairs, so
    /// the pair-creation + midphase work all lands in one frame). Moving WHEN
    /// the release fired moved the pop with it — causation proven — so now
    /// the rigidbody goes dynamic at DESCENT START instead: the contact pairs
    /// get built while the whole screen is moving, the rider cage still owns
    /// the pose (RiderFixedTick overwrites position and zeroes the integrator
    /// every step while dynamic), and the actual release then changes nothing
    /// PhysX has to pay for.
    public static void PrewarmPhysicalRelease()
    {
        if (s_player == null) return;
        var rb = s_player.Rigidbody;
        if (rb == null || !rb.isKinematic) return;
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ── Release (LANDING → PARKED) ───────────────────────────────────────────
    public static void ReleaseRiders(ShuttleAutopilot pilot)
    {
        // Release on EVIDENCE, not bookkeeping: playtest 6 had s_riding
        // cleared mid-flight by an outside caller, so the early-return here
        // left the player parented+kinematic inside the parked shuttle — and
        // whatever unfroze them later never gave them the planet's orbital
        // velocity (the "landed, then launched into space a second later").
        var evidencePc = s_player != null ? s_player : CachedPc();
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
        var endless = CachedEndless();

        if (s_player != null)
        {
            var pc = s_player;
            var rb = pc.Rigidbody;
            PlayerController.RiderMode = false;
            PlayerController.RiderPlatform = null;

            // ═══ RENDER-POSE SEAT (playtest 41 — the philosophy inversion
            // that ends the frame wars). Fourteen tracker logs proved the
            // bookkeeping frames (planet rb pose, MovePosition target,
            // render pose, rider local, PhysX shape poses) cannot be
            // reconciled blind: every "authoritative" compose painted its
            // error either onto the screen (a one-step render flash) or
            // into the floor (burial + 1 cm/tick crawl-out). So stop
            // reconstructing and seat the player WHERE THEY ARE ALREADY
            // DRAWN: the interpolation then seeds at the pose the previous
            // frame rendered — zero visual snap BY CONSTRUCTION — and
            // physics adapts to the render instead of the reverse: a
            // vertical foot trim against the colliders PhysX actually has
            // puts the feet flush (no burial, no hover, no clamp crawl),
            // and velocity (= bodyVel) carries the body in lockstep with
            // the floor's sweep. The residual is a sub-metre ALONG-FLOOR
            // offset from the bookkeeping's idea of the spot — flat cabin
            // floor, no penetration, invisible. (Playtest 6's objection to
            // render-pose seating predates the foot trim, the depenetration
            // clamp and the contact prewarm, which together cover it.)
            Vector3 seat = pc.transform.position;      // parented RENDER pose
            Quaternion seatRot = pc.transform.rotation;

            if (pc.feet != null)
            {
                Vector3 upAxis = seatRot * Vector3.up;
                Vector3 toFeet = pc.feet.position - pc.transform.position;
                Vector3 castOrigin = seat + toFeet + upAxis * 1.0f;
                if (Physics.SphereCast(castOrigin, 0.25f, -upAxis, out RaycastHit footHit, 4f,
                        ShuttleAutopilot.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    float corrAmt = ShuttleLandingLogic.RiderSeatCorrection(1.0f, 0.25f, footHit.distance, 0.02f);
                    // The render pose's vertical error vs the PhysX floor can
                    // be tens of cm (the render lag's vertical component) —
                    // generous cap, vertical only. Beyond it the cast missed
                    // the real floor (open doorway) — don't apply.
                    if (Mathf.Abs(corrAmt) < 1.0f) seat += upAxis * corrAmt;
                }
            }

            pc.transform.SetParent(null, true);
            pc.transform.SetPositionAndRotation(seat, seatRot);
            rb.isKinematic = false;
            rb.interpolation = s_playerInterpolation;
            rb.position = seat;    // interp seeds at the drawn pose = flush
            rb.rotation = seatRot;
            rb.angularVelocity = Vector3.zero;
            pc.SetVelocity(bodyVel);               // inherit the new planet's orbit
            // Hand the controller the LANDING planet before physics resumes —
            // its reference body froze at departure for the whole flight, and
            // one stale step poisoned FallDamage (~100 m/s phantom impact),
            // the grip and the twin exception (playtest 13). The load-grace
            // covers the transition frames the same way save-loads use it.
            pc.SetReferenceBodyOnRelease(body);
            pc.ForceGroundedOnRelease();   // they WERE standing — no airborne-pose frame
            FallDamage.LoadGraceUntil = Mathf.Max(FallDamage.LoadGraceUntil, Time.unscaledTime + 2f);
            Physics.SyncTransforms();
            if (endless != null) endless.RegisterPhysicsObject(pc.transform);
            // NO SnapToCurrentPlayer here (playtest 10's release hitch): the
            // intro needed it because its release TELEPORTS the seat; ours
            // seats the player exactly where they already stand, so resetting
            // the camera interpolation buffer only added a visible blip.

            FallDamage.Suppressed = false;
            // The up blend-out normally started at TOUCHDOWN (SetPhase Parked)
            // and has already finished — only start one here if some edge path
            // released with the override still parked on the shuttle itself.
            if (PlayerController.UpOverrideTransform == pilot.transform)
                pilot.BlendRiderUpOut(1.5f);
            RiderReleaseBleed.ArmHold();
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

// Camera HOLD across the rider-release seam (playtests 31/32, 2026-08-28).
// The one-frame view dip at the physical release is the render-path switch
// itself (parent compose -> rigidbody interpolation) and survived every
// bookkeeping fix, so the camera is held at its pre-release pose relative to
// the RENDERED planet and bled out over HoldSeconds. Absolute lerp-to-target
// (cannot compound), no rb/transform/interp writes, aborts if the held pose
// is more than 5 m away, and is finished before walking is possible.
//
// This component used to carry the landing MegaTracker and the IntroWatch
// forensic recorders that solved the landing pop and the cabin slide; they
// were retired 2026-09-05 once both fixes were Sam-confirmed. The laws they
// produced are recorded in docs/CURRENT_STATE_AUDIT.md (§34 + the 2026-08-28
// addendum).
[DefaultExecutionOrder(300)]   // after CameraTransformFX (100) — final poses
public class RiderReleaseBleed : MonoBehaviour
{
    const float HoldSeconds = 1.2f;

    PlayerController _pc;
    CelestialBody _body;
    Transform _cam;
    float _t0, _window;
    float _releaseT = -1f;
    Vector3 _heldCamLocal;
    bool _holdArmed;
    static RiderReleaseBleed s_active;

    /// Open a handover window at touchdown; the hold can only be armed inside it.
    public static void BeginWindow(PlayerController pc, CelestialBody body, float seconds)
    {
        if (pc == null) return;
        var b = pc.GetComponent<RiderReleaseBleed>();
        if (b == null) b = pc.gameObject.AddComponent<RiderReleaseBleed>();
        b._pc = pc;
        b._body = body;
        b._cam = pc.Camera != null ? pc.Camera.transform : null;
        b._t0 = Time.time;
        b._window = seconds;
        b._releaseT = -1f;
        b._holdArmed = false;
        s_active = b;
        b.enabled = true;
    }

    /// Called by ShuttleRiderFrame at the physical release: capture the camera
    /// relative to the planet and start the hold. No-op outside a window.
    public static void ArmHold()
    {
        var b = s_active;
        if (b == null || !b.enabled) return;
        b._releaseT = Time.time - b._t0;
        if (b._cam != null && b._body != null)
        {
            b._heldCamLocal = b._body.transform.InverseTransformPoint(b._cam.position);
            b._holdArmed = true;
        }
    }

    void LateUpdate()
    {
        float since = Time.time - _t0;
        if (_holdArmed && _releaseT >= 0f && _cam != null && _body != null)
        {
            float u = (since - _releaseT) / HoldSeconds;
            if (u >= 0f && u < 1f)
            {
                Vector3 held = _body.transform.TransformPoint(_heldCamLocal);
                if ((held - _cam.position).sqrMagnitude < 25f)
                {
                    float w = 1f - Mathf.SmoothStep(0.5f, 1f, u);
                    _cam.position = Vector3.Lerp(_cam.position, held, w);
                }
                else _holdArmed = false;
            }
            else if (u >= 1f) _holdArmed = false;
        }
        if (since >= _window && !_holdArmed) enabled = false;
    }
}
