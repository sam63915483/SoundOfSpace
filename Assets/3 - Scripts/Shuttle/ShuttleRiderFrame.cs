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
            RiderReleaseBleed.Mark("release");
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

// MEGA-TRACKER (playtest 37, Sam's ask: "track the player relative to the
// planet and relative to the shuttle and anything else"). The key design
// realization after 10 probe logs: every earlier column was PLANET-relative
// or absolute, but the player's EYES judge motion relative to the CABIN they
// are standing in. This version records, per render frame AND per physics
// tick, the camera / player transform / player rigidbody expressed in the
// SHUTTLE'S OWN FRAME (render frame for rendered things, physics frame for
// the rigidbody), plus grounded/rider flags. Standing still, every
// cabin-relative delta must be ~0.0 cm; the pop is the row where a channel
// is not, and WHICH channel names the culprit:
//   dPly moves, dRb still  -> render-side jump (interpolation/camera path)
//   dRb moves too          -> a genuine physics kick on that tick
//   g flips 1-0-1          -> grounded loss (airborne pose/branch)
//   only dCam moves        -> camera pipeline (FX arm, hold, look)
// Cabin-frame numbers are also intrinsically origin-shift-proof (player and
// shuttle shift together). One summary log, 3 s AFTER the window closes.
// The camera HOLD from playtests 31/32 still runs here (it is behavior, not
// diagnostics). Editor/cheats gate the recording only.
[DefaultExecutionOrder(300)]   // after CameraTransformFX (100) — final poses
public class RiderReleaseBleed : MonoBehaviour
{
    struct RSample { public float t, dtMs, holdCm; public Vector3 camCab, plyCab; public bool gnd, rider; public int gc; }
    struct FSample { public float t; public Vector3 rbCab; public float velDiffCm; public bool gnd, rider; }

    PlayerController _pc;
    CelestialBody _body;
    Transform _cam;
    float _t0, _window;
    float _lastRealtime;
    float _reportAt = -1f;
    float _releaseT = -1f;
    int _lastGcCount, _gcTotal;
    float _worstDt, _worstDtT;
    readonly List<RSample> _r = new List<RSample>(700);
    readonly List<FSample> _f = new List<FSample>(500);
    static readonly List<string> s_marks = new List<string>(16);
    static RiderReleaseBleed s_active;

    Vector3 _seatPos;
    int _walkMarked;
    Vector3 _heldCamLocal;
    bool _holdArmed;
    float _lastHoldCm;

    // ── Intro watch mode (2026-08-28, the pod slide hunt) ────────────────────
    // Arms at intro start and samples the player's CABIN-relative position
    // every 0.5 s for the whole window, reporting CUMULATIVE drift from the
    // starting spot — a slow slide hides in per-frame deltas but is
    // unmissable as accumulated drift. Rows carry phase + grounded/rider/
    // dialogue flags; Marks timestamp the intro events.
    struct DSample { public float t; public Vector3 plyCab, camCab, rbCab; public byte phase; public bool gnd, rider, dlg; public float alignCm, walkCm, moveCm, yawDeg; public Vector3 recCab, netTick, netOut; public Vector3 netAlign, netMove; public int upOwner; public float upDeg; }
    bool _introMode;
    float _nextDriftAt;
    Vector3 _lastRbCab;
    readonly List<DSample> _drift = new List<DSample>(120);

    public static void BeginIntroWatch(PlayerController pc, CelestialBody body, float seconds)
    {
        BeginWindow(pc, body, seconds);
        if (s_active != null)
        {
            s_active._introMode = true;
            s_active._nextDriftAt = 0f;
            s_active._drift.Clear();
            // Zero the per-stage writer accumulators — each drift row carries
            // their cumulative values, so slope-matching names the writer.
            PlayerController.DbgRiderAlignCm = 0f;
            PlayerController.DbgRiderWalkCm = 0f;
            PlayerController.DbgRiderMoveCm = 0f;
            PlayerController.DbgRiderYawDeg = 0f;
            PlayerController.DbgRiderNetTick = Vector3.zero;
            PlayerController.DbgRiderNetOut = Vector3.zero;
            PlayerController.DbgRiderNetAlign = Vector3.zero;
            PlayerController.DbgRiderNetMove = Vector3.zero;
        }
    }

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
        b._lastRealtime = Time.realtimeSinceStartup;
        b._reportAt = -1f;
        b._releaseT = -1f;
        b._holdArmed = false;
        b._lastGcCount = System.GC.CollectionCount(0);
        b._gcTotal = 0;
        b._worstDt = 0f;
        b._worstDtT = 0f;
        b._r.Clear();
        b._f.Clear();
        b._introMode = false;
        s_marks.Clear();
        s_active = b;
        b.enabled = true;
    }

    /// Timestamp a discrete event into the current window (no-op without one).
    public static void Mark(string label)
    {
        if (s_active == null || !s_active.enabled || s_active._reportAt >= 0f) return;
        float t = Time.time - s_active._t0;
        s_marks.Add("t+" + (t * 1000f).ToString("0") + "ms " + label);
        if (label == "release")
        {
            s_active._releaseT = t;
            if (s_active._pc != null && s_active._body != null)
                s_active._seatPos = s_active._pc.transform.position - s_active._body.transform.position;
            if (s_active._cam != null && s_active._body != null)
            {
                s_active._heldCamLocal = s_active._body.transform.InverseTransformPoint(s_active._cam.position);
                s_active._holdArmed = true;
            }
            s_active._walkMarked = 0;
        }
    }

    // Physics-tick channel: the rigidbody in the shuttle's PHYSICS frame.
    // Order 300 puts this after the autopilot (-50), NBodySim (-10) and the
    // player (0) — the settled state of the tick.
    void FixedUpdate()
    {
        if (_reportAt >= 0f || _pc == null || _pc.Rigidbody == null) return;
        float since = Time.time - _t0;
        if (since > _window) return;
        var ap = ShuttleAutopilot.Instance;
        if (ap == null) return;
        ap.GetWorldPose(out Vector3 sw, out Quaternion sr);
        Vector3 bodyVel = _body != null ? _body.velocity : Vector3.zero;
        Vector3 rbCab = Quaternion.Inverse(sr) * (_pc.Rigidbody.position - sw);
        _lastRbCab = rbCab;
        if (!_introMode && _f.Count < 480)
            _f.Add(new FSample {
                t = since,
                rbCab = rbCab,
                velDiffCm = (_pc.Rigidbody.velocity - bodyVel).magnitude * 100f,
                gnd = _pc.GroundedNow,
                rider = PlayerController.RiderMode,
            });
    }

    void LateUpdate()
    {
        if (_reportAt > 0f)
        {
            if (Time.time >= _reportAt) { Report(); enabled = false; }
            return;
        }
        float since = Time.time - _t0;
        float dtMs = (Time.realtimeSinceStartup - _lastRealtime) * 1000f;
        _lastRealtime = Time.realtimeSinceStartup;

        // Camera hold across the release seam (playtests 31/32) — behavior.
        _lastHoldCm = 0f;
        if (_holdArmed && _releaseT >= 0f && _cam != null && _body != null)
        {
            float u = (since - _releaseT) / 1.2f;
            if (u >= 0f && u < 1f)
            {
                Vector3 held = _body.transform.TransformPoint(_heldCamLocal);
                if ((held - _cam.position).sqrMagnitude < 25f)
                {
                    float w = 1f - Mathf.SmoothStep(0.5f, 1f, u);
                    Vector3 before = _cam.position;
                    _cam.position = Vector3.Lerp(_cam.position, held, w);
                    _lastHoldCm = (_cam.position - before).magnitude * 100f;
                }
                else _holdArmed = false;
            }
            else if (u >= 1f) _holdArmed = false;
        }

        bool probe = Application.isEditor || Universe.cheatsEnabled;
        if (probe)
        {
            // Walk-out breadcrumbs (planet-relative so orbit never counts).
            if (_releaseT >= 0f && _walkMarked < 3 && _pc != null && _body != null)
            {
                float walked = ((_pc.transform.position - _body.transform.position) - _seatPos).magnitude;
                if (_walkMarked == 0 && walked > 1.5f) { _walkMarked = 1; Mark("walked-1.5m"); }
                else if (_walkMarked == 1 && walked > 5f) { _walkMarked = 2; Mark("walked-5m"); }
                else if (_walkMarked == 2 && walked > 10f) { _walkMarked = 3; Mark("walked-10m"); }
            }

            int gcNow = System.GC.CollectionCount(0);
            int gcD = gcNow - _lastGcCount;
            _lastGcCount = gcNow;
            _gcTotal += gcD;
            if (dtMs > _worstDt) { _worstDt = dtMs; _worstDtT = since; }

            var ap = ShuttleAutopilot.Instance;
            if (ap != null && _pc != null && _cam != null)
            {
                Transform shT = ap.transform;
                if (_introMode)
                {
                    if (since >= _nextDriftAt && _drift.Count < 110)
                    {
                        _nextDriftAt = since + 0.5f;
                        _drift.Add(new DSample {
                            t = since,
                            plyCab = shT.InverseTransformPoint(_pc.transform.position),
                            camCab = shT.InverseTransformPoint(_cam.position),
                            rbCab = _lastRbCab,
                            phase = (byte)ap.CurrentPhase,
                            gnd = _pc.GroundedNow,
                            rider = PlayerController.RiderMode,
                            dlg = PlayerController.isInDialogue,
                            alignCm = PlayerController.DbgRiderAlignCm,
                            walkCm = PlayerController.DbgRiderWalkCm,
                            moveCm = PlayerController.DbgRiderMoveCm,
                            yawDeg = PlayerController.DbgRiderYawDeg,
                            recCab = _pc.RiderRecordLocalPos,
                            netTick = PlayerController.DbgRiderNetTick,
                            netOut = PlayerController.DbgRiderNetOut,
                            netAlign = PlayerController.DbgRiderNetAlign,
                            netMove = PlayerController.DbgRiderNetMove,
                            upOwner = PlayerController.DbgRiderUpOwner,
                            upDeg = PlayerController.DbgRiderUpOffAxisDeg,
                        });
                    }
                }
                else if (_r.Count < 680)
                {
                    _r.Add(new RSample {
                        t = since,
                        dtMs = dtMs,
                        holdCm = _lastHoldCm,
                        camCab = shT.InverseTransformPoint(_cam.position),
                        plyCab = shT.InverseTransformPoint(_pc.transform.position),
                        gnd = _pc.GroundedNow,
                        rider = PlayerController.RiderMode,
                        gc = gcD,
                    });
                }
            }
        }

        if (since >= _window) _reportAt = Time.time + 3f;   // report OUTSIDE the window
    }

    void ReportIntro()
    {
        if (_drift.Count < 2) return;
        var b0 = _drift[0];
        var sb = new System.Text.StringBuilder(8192);
        sb.Append("[IntroWatch v5] ").Append(_drift.Count).Append(" samples / ").Append(_window)
          .Append("s.  Events: ").Append(s_marks.Count > 0 ? string.Join(", ", s_marks) : "none").AppendLine();
        sb.AppendLine("cumulative CABIN-relative drift from the start pose (cm);");
        sb.AppendLine("ph 0=Parked 1=Countdown 2=Liftoff 3=Transit 4=Hover 5=Landing; g=grounded r=riding d=dialogue");
        sb.AppendLine("nA/nM = NET cm written by the yaw+up-align stage / the move+wall+seat stage (nA+nM = the pipeline's whole output); ov = up owner (0 none, 1 shuttle/own proxy, 3 FOREIGN); ang = chosen-up angle off cabin-up (deg); wk = walk intent");
        for (int i = 1; i < _drift.Count; i++)
        {
            var s = _drift[i];
            Vector3 dp = s.plyCab - b0.plyCab;
            sb.Append("  t+").Append(s.t.ToString("00.0"))
              .Append("s ply ").Append((dp.magnitude * 100f).ToString("0.0"))
              .Append(" (Y ").Append((dp.y * 100f).ToString("+0.0;-0.0"))
              .Append(") nA ").Append((s.netAlign.magnitude * 100f).ToString("0.0"))
              .Append(" nM ").Append((s.netMove.magnitude * 100f).ToString("0.0"))
              .Append(" ot ").Append((s.netOut.magnitude * 100f).ToString("0.0"))
              .Append(" ov").Append(s.upOwner)
              .Append(" ang ").Append(s.upDeg.ToString("0.00"))
              .Append(" wk ").Append(s.walkCm.ToString("0.0"))
              .Append(" ph").Append(s.phase)
              .Append(" g").Append(s.gnd ? "1" : "0")
              .Append(" d").Append(s.dlg ? "1" : "0")
              .AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    void Report()
    {
        if (!(Application.isEditor || Universe.cheatsEnabled)) return;
        if (_introMode) { ReportIntro(); return; }
        if (_r.Count < 5) return;
        var sb = new System.Text.StringBuilder(8192);
        sb.Append("[MegaTracker] ").Append(_r.Count).Append(" render frames / ")
          .Append(_f.Count).Append(" physics ticks / ").Append(_window)
          .Append("s from touchdown.  Events: ")
          .Append(s_marks.Count > 0 ? string.Join(", ", s_marks) : "none").AppendLine();
        sb.Append("worst frame time ").Append(_worstDt.ToString("0.0")).Append("ms at t+")
          .Append((_worstDtT * 1000f).ToString("0")).Append("ms  GC in window: ")
          .Append(_gcTotal).AppendLine();
        sb.AppendLine("cols: cam/ply = CABIN-relative movement per frame in cm (~0 when standing still);");
        sb.AppendLine("plyY = cabin-vertical part; hold = camera-hold applied; g=grounded r=riding");

        // Worst cabin-relative player jump anywhere in the window.
        int worstI = 1;
        float worstD = 0f;
        for (int i = 1; i < _r.Count; i++)
        {
            float d = (_r[i].plyCab - _r[i - 1].plyCab).magnitude;
            if (d > worstD) { worstD = d; worstI = i; }
        }
        sb.Append("worst cabin-relative player jump ").Append((worstD * 100f).ToString("0.0"))
          .Append("cm at t+").Append((_r[worstI].t * 1000f).ToString("0")).AppendLine("ms");

        if (_releaseT >= 0f)
        {
            sb.AppendLine("RENDER frames, release-0.25s to release+1.0s:");
            PrintRRange(sb, _releaseT - 0.25f, _releaseT + 1.0f);
            if (_r[worstI].t < _releaseT - 0.3f || _r[worstI].t > _releaseT + 1.1f)
            {
                sb.AppendLine("RENDER frames around the worst player jump:");
                PrintRRange(sb, _r[worstI].t - 0.12f, _r[worstI].t + 0.12f);
            }
            sb.AppendLine("PHYSICS ticks, release-0.2s to release+0.6s (dRbY = cabin-vertical rb step cm; vd = |rbVel-bodyVel| cm/s):");
            PrintFRange(sb, _releaseT - 0.2f, _releaseT + 0.6f);
        }
        else
        {
            sb.AppendLine("RENDER frames around the worst player jump:");
            PrintRRange(sb, _r[worstI].t - 0.15f, _r[worstI].t + 0.15f);
        }
        Debug.Log(sb.ToString());
    }

    void PrintRRange(System.Text.StringBuilder sb, float tMin, float tMax)
    {
        for (int i = 1; i < _r.Count; i++)
        {
            var s = _r[i];
            if (s.t < tMin || s.t > tMax) continue;
            var p = _r[i - 1];
            float dCam = (s.camCab - p.camCab).magnitude * 100f;
            float dPly = (s.plyCab - p.plyCab).magnitude * 100f;
            float dPlyY = (s.plyCab.y - p.plyCab.y) * 100f;
            bool isRel = _releaseT >= 0f && p.t < _releaseT && s.t >= _releaseT;
            sb.Append(isRel ? "> " : "  ")
              .Append("t+").Append((s.t * 1000f).ToString("0000"))
              .Append(" dt").Append(s.dtMs.ToString("00.0"))
              .Append(" cam ").Append(dCam.ToString("0.0"))
              .Append(" ply ").Append(dPly.ToString("0.0"))
              .Append(" plyY ").Append(dPlyY.ToString("+0.0;-0.0"))
              .Append(" hold ").Append(s.holdCm.ToString("0.0"))
              .Append(" g").Append(s.gnd ? "1" : "0")
              .Append(" r").Append(s.rider ? "1" : "0")
              .AppendLine(s.gc > 0 ? "  <-- GC" : "");
        }
    }

    void PrintFRange(System.Text.StringBuilder sb, float tMin, float tMax)
    {
        for (int i = 1; i < _f.Count; i++)
        {
            var s = _f[i];
            if (s.t < tMin || s.t > tMax) continue;
            var p = _f[i - 1];
            float dRb = (s.rbCab - p.rbCab).magnitude * 100f;
            float dRbY = (s.rbCab.y - p.rbCab.y) * 100f;
            bool isRel = _releaseT >= 0f && p.t < _releaseT && s.t >= _releaseT;
            sb.Append(isRel ? "> " : "  ")
              .Append("T+").Append((s.t * 1000f).ToString("0000"))
              .Append(" dRb ").Append(dRb.ToString("0.0"))
              .Append(" dRbY ").Append(dRbY.ToString("+0.0;-0.0"))
              .Append(" vd ").Append(s.velDiffCm.ToString("0"))
              .Append(" g").Append(s.gnd ? "1" : "0")
              .Append(" r").Append(s.rider ? "1" : "0")
              .AppendLine();
        }
    }
}
