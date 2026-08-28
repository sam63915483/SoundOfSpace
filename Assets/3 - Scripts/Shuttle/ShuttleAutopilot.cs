using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Shuttle autopilot travel (2026-08-25, docs/Handoff_ShuttleAutopilot_Travel_v1.md).
// PARKED -> COUNTDOWN -> LIFTOFF -> TRANSIT -> HOVER -> LANDING -> PARKED.
//
// FRAME CONVENTION (whole feature): PHYSICS frame. All world math uses
// body.Position (rb.position) and body.Rigidbody.rotation, never the
// interpolated transform — the shuttle's colliders are part of its parent
// planet's kinematic-rigidbody compound, so everything physics sees lives in
// the rb frame. The authoritative pose is _localPos/_localRot under the
// current parent body; transform.localPosition is WRITTEN from state each
// FixedUpdate and re-written at render rate by ShuttleRenderSmoother — never
// read back.
//
// The shuttle stays a plain Rigidbody-less child of a CelestialBody for the
// ENTIRE flight (reparented from the departure body to the target body at
// transit midpoint). That keeps origin rebases free (the hierarchy rides the
// registered planet) and keeps SolarSystemSync.CarryRiders off our back (it
// exempts children of CelestialBody).
//
// Execution order -50: the kinematic pose (and Physics.SyncTransforms) must
// commit BEFORE PlayerController's FixedUpdate runs the rider movement — the
// same slot Ship.cs documents for its carry-player ordering.
[DefaultExecutionOrder(-50)]
public class ShuttleAutopilot : MonoBehaviour
{
    public enum Phase : byte { Parked = 0, Countdown = 1, Liftoff = 2, Transit = 3, Hover = 4, Landing = 5 }

    public static ShuttleAutopilot Instance { get; private set; }

    /// Guest kill-switch (StasisPodDoor.ClientDriven pattern): while true this
    /// machine never advances the state machine or simulates motion — it only
    /// renders the pose/phase ShuttleSync applies. Set by ShuttleSync.
    public static bool ClientDriven;

    /// ShuttleTravelSelfTest only: lets the flight recorder fly a crewless leg
    /// (the D-1 empty-shuttle abort would otherwise cancel at countdown 0).
    public static bool DebugSkipCrewCheck;

    /// Fired on every phase transition — door, NAV, lamp, rider frame, sync and
    /// the world screen all subscribe. Never poll.
    public event System.Action<Phase> OnPhaseChanged;

    /// Fired when a launch aborts at COUNTDOWN 0 because nobody was aboard
    /// (handoff D-1). NAV shows "NO CREW ABOARD — LAUNCH CANCELLED".
    public event System.Action OnLaunchAborted;

    // ── Tuning (constants, house style — no serialized fields on a runtime-attached component) ──
    public const float CountdownSeconds = 10f;
    const float LiftoffSeconds  = 4f;     // ignition hold — door seals, engines spool, NO motion
    // Flight profile (rebuilt again after playtest 16 — "in real space flight
    // you don't slow down like that: keep accelerating to the midpoint, then
    // slow to a stop ~100 m off the destination"): ONE continuous quadratic
    // bezier from the PAD to the arrival hover anchor. The control point sits
    // radially above the pad, so the path leaves straight up and bends over
    // toward the target — the old separate 300 m climb ENDED AT REST before
    // the transit re-accelerated sideways (the "goes up, slows down, then
    // starts moving at the destination" alien seam). The ease is smootherstep:
    // a single velocity bell (accelerate to the midpoint, decelerate to rest
    // at the anchor) with zero velocity AND acceleration at both endpoints.
    // Duration comes from an acceleration budget (smootherstep peak accel =
    // 5.77·L/T², solved for T) with a midpoint-speed ceiling.
    const float TransitAccelMax   = 25f;   // m/s² peak burn (~2.5g), smooth onset
    const float TransitPeakMax    = 450f;  // m/s ceiling at the midpoint
    const float TransitMinSeconds = 12f;
    const float TransitMaxSeconds = 50f;   // hard cap — beyond it a far leg just burns harder
    const float TransitBendMin    = 300f;  // radial rise of the bezier control point
    const float TransitBendMax    = 3000f;
    public const float HoverAltitude = 100f;  // Sam, playtest 4: back up to 100
    const float HoverMaxSpeed   = 30f;    // WASD tangential speed (playtest 4: 15 was "really slow")
    const float HoverAccel      = 14f;    // m/s² toward the input direction (heavy vehicle)
    const float HoverYawDegSec  = 40f;    // Q/E
    const float HoverAltSmooth  = 1.2f;   // SmoothDamp time for the altitude hold
    const float LandingMinSeconds = 5f;
    const float LandingMaxSeconds = 8f;
    // Deepest a gear leg may sink so every OTHER leg reaches the ground.
    // Sam's call (playtest 17): a half-buried pad reads as "settled", a
    // hovering one reads as broken — bias hard toward grounded. Legal pads
    // cap the plane-relative spread at 1.5 m, so worst residual air ≈ 0.3 m
    // on the most extreme terrain that still validates green.
    const float MaxLegBury = 1.2f;
    const float SettleSeconds   = 0.5f;
    // Rider cage held after touchdown. 2.2 s (playtest 36 — SAM'S ROOT-CAUSE
    // CALL, and the mechanics agree): the up-blend runs 0→2 s, and releasing
    // at 1.2 s put the player into the normal pipeline MID-ROTATION. The
    // body rotates about its CENTRE, so a tilt correction sweeps the feet
    // through an arc (~25 cm at 15°); while riding, the rider clamp re-seats
    // the feet every step, but after release nothing does — the continuing
    // rotation ground the capsule into the floor (or lifted it off) and
    // physics corrected it back: the "snapped down then up" residual hitch.
    // At 2.2 s the rotation is DONE (the blend ends at 2.0, the proxy then
    // holds steady gravity-up), the foot-exact seat pins the feet at the
    // FINAL orientation, and the release carries zero pending rotation. The
    // old 1.2 s spike-masking rationale is obsolete — the contact prewarm
    // and depenetration clamp defused that spike. Door still opens at 2.5 s.
    const float ReleaseSettleSeconds = 2.2f;
    const float PilotInputStaleSeconds = 0.5f;  // decay to zero on silence (guest-drop safety)

    // Ground = terrain only. Layer 10 ("Body") is the terrain layer; every cast
    // MUST pass QueryTriggerInteraction.Ignore (ocean waterlines are triggers
    // on Body — see PlayerController.IsGrounded's comment).
    public static readonly int GroundMask = 1 << 10;

    // ── Authoritative state ──────────────────────────────────────────────────
    Phase _phase = Phase.Parked;
    CelestialBody _body;              // current frame body == transform parent
    Vector3 _localPos;                // authoritative pose under _body (physics frame)
    Quaternion _localRot;
    Vector3 _prevLocalPos;            // previous fixed step, for the render smoother
    Quaternion _prevLocalRot;
    bool _poseJumped;                 // set on reparent so the smoother snaps

    float _phaseT;
    CelestialBody _departBody, _targetBody;
    Vector3 _departAnchorLocal;       // hover point over the departure pad, in depart-body local
    Vector3 _arriveAnchorLocal;       // hover point over the arrival site, in target-body local
    float _transitDuration;
    bool _reparented;
    float _upAlignVel;                // deg/s state for the transit up alignment (SmoothDamp)
    // Transit obstacle avoidance (playtest 15) — a smooth-damped DELTA of
    // world positions, so origin rebases cancel out of it.
    Vector3 _avoidOffset;
    Vector3 _avoidOffsetVel;

    // Parked pose memory — what the save system captures, and what a mid-flight
    // save falls back to (you can't reach the pod mid-flight, but belt+braces).
    CelestialBody _parkedBody;
    Vector3 _parkedLocalPos;
    Quaternion _parkedLocalRot;
    float _gearHeight = 2.5f;         // shuttle-origin height above the gear pads' contact points
    // Measured foot-bottom points in shuttle-local space (the lowest own-
    // collider surface, found by MeasureGearHeight's upward ray grid). The
    // landing casts down from these EXACT points at the target pose and
    // seats the lowest one onto the terrain — no more estimating.
    readonly List<Vector3> _feetLocal = new List<Vector3>();
    // Foot points clustered into LEGS (one lowest point per gear leg) —
    // the landing tilts to the terrain UNDER these and sinks until every
    // leg touches (playtest 17's "all 4 feet on the ground").
    readonly List<Vector3> _legsLocal = new List<Vector3>();

    // Hover state — PLANET-LOCKED (rewritten after playtest 2). The whole
    // state lives in the target body's local frame: a radial direction, a
    // held altitude, and a smoothed tangential velocity. WASD rotates the
    // radial direction around the sphere (the shuttle slides around the
    // planet, bottom always facing the core — Sam's spec); the altitude
    // springs to HoverAltitude above whatever terrain is under it. Bounded
    // by construction: no world-space integration, so it cannot drift or
    // overshoot however the body itself moves (Icey Twin is a co-orbit
    // follower that gets teleported into place every physics step — the old
    // world-velocity hover fell apart exactly there).
    Vector2 _hoverVel;                // smoothed WASD velocity (x=right, y=fwd), m/s
    float _hoverAltVel;               // SmoothDamp ref for the altitude spring
    float _hoverAlt;                  // current held altitude above ground
    Vector2 _pilotMove;               // WASD, [-1,1] each axis
    float _pilotYaw;                  // Q/E, [-1,1]
    float _pilotInputStamp = -999f;
    bool _landRequested;

    // Landing state
    Vector3 _landStartLocal;
    Vector3 _landTargetLocal;
    Quaternion _landStartLocalRot = Quaternion.identity;
    Quaternion _landTargetLocalRot = Quaternion.identity;
    float _landDuration;
    float _settleT;

    PlayerController _healPlayer;
    float _nextHealScanAt;
    float _releaseRidersAt = -1f;   // deferred post-touchdown release; -1 = idle
    EndlessManager _endless;

    // New-game intro approach (2026-08-28): scripted straight-in descent to
    // the authored pad's hover point — avoidance and the depart-floor are
    // meaningless for an inbound leg and are skipped while this is set.
    bool _introApproach;
    // Thruster fire (Sam: engines on liftoff/landing/hover spurts — reuses the
    // intro's runtime-built ShuttleThrustFX plumes).
    ShuttleThrustFX _fx;
    float _landHeight;

    ShuttleLandingSensor _sensor;
    ShuttleRenderSmoother _smoother;
    ShuttleLandingCamera _landingCamera;
    ShuttleLandingCamera _transitCamera;   // en-route feed (2026-08-28, Sam's ask)
    ShuttleExitDoor _door;
    LandingLamp _lamp;
    Coroutine _upBlendOut;

    // Guest-side replication state (ShuttleSync). Pose arrives at 10 Hz and is
    // eased toward between updates; progress arrives on the pose message.
    Vector3 _remoteTargetPos;
    Quaternion _remoteTargetRot = Quaternion.identity;
    bool _hasRemoteTarget;
    float _remoteProgress;

    public Phase CurrentPhase => _phase;
    public CelestialBody CurrentBody => _body;
    public CelestialBody TargetBody => _targetBody;
    public float CountdownRemaining => _phase == Phase.Countdown ? Mathf.Max(0f, CountdownSeconds - _phaseT) : 0f;
    public float TransitProgress => ClientDriven
        ? (_phase == Phase.Transit ? _remoteProgress : 0f)
        : (_phase == Phase.Transit && _transitDuration > 0f ? Mathf.Clamp01(_phaseT / _transitDuration) : 0f);
    public bool LandingValid => _sensor != null && _sensor.Valid;
    public string LandingFailReason => _sensor != null ? _sensor.FailReason : "";
    /// Held altitude above ground during HOVER (the NAV feed's readout).
    public float CurrentGroundAltitude => _hoverAlt;
    /// Seconds into the current phase (ShuttleSync's heartbeat payload).
    public float PhaseElapsed => _phaseT;

    // Body-relative speed this fixed step (bodies don't spin, so a local-pose
    // delta is rebase-proof and orbit-proof). Drives the NAV velocity readout.
    float _speed;
    public float CurrentSpeed => _speed;

    /// Guest-side: the host's replicated landing-validity bool.
    public void ApplyRemoteValid(bool valid)
    {
        if (_sensor != null) _sensor.SetRemoteValid(valid);
        if (_lamp != null && (_phase == Phase.Hover || _phase == Phase.Landing))
            _lamp.SetPhase(_phase, valid);
    }
    public ShuttleLandingCamera LandingCamera => _landingCamera;
    public ShuttleLandingCamera TransitCamera => _transitCamera;

    /// En-route screen cam switch (left click): top cam looking where you fly
    /// vs bottom cam looking back.
    public void ToggleTransitFeed()
    {
        if (_transitCamera == null) return;
        _transitCamera.SetMode(_transitCamera.Mode == ShuttleLandingCamera.FeedMode.Up
            ? ShuttleLandingCamera.FeedMode.Down : ShuttleLandingCamera.FeedMode.Up);
    }

    /// True in any phase where the shuttle is off the ground (riders captured,
    /// door sealed, pod save unavailable).
    public bool FlightActive => _phase != Phase.Parked && _phase != Phase.Countdown;

    // ── Bootstrap (runtime-attach; the hand-maintained prefab is never regenerated) ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => TryAttach();
        TryAttach();
    }

    /// Save-load entry point: the apply order may run before this scene's
    /// sceneLoaded hook — attach on demand. ApplyParkedPose is Start-order
    /// safe (it initialises the frame fields itself).
    public static ShuttleAutopilot EnsureAttached()
    {
        TryAttach();
        return Instance;
    }

    static void TryAttach()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = GameObject.Find("Shuttle_Lander");
        if (go == null) return;
        if (go.GetComponent<ShuttleAutopilot>() == null) go.AddComponent<ShuttleAutopilot>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // (No threshold restore — the 3.5 km threshold is session-permanent,
        // playtest 19; a scene reload re-creates EndlessManager at its
        // serialized default anyway.)
    }

    void Start()
    {
        _body = GetComponentInParent<CelestialBody>();
        _localPos = transform.localPosition;
        _localRot = transform.localRotation;
        _prevLocalPos = _localPos;
        _prevLocalRot = _localRot;
        RememberParkedPose();

        _door = GetComponentInChildren<ShuttleExitDoor>(true);
        MeasureGearHeight();   // after _door — it excludes the door's colliders
        _sensor = gameObject.GetComponent<ShuttleLandingSensor>();
        if (_sensor == null) _sensor = gameObject.AddComponent<ShuttleLandingSensor>();
        _smoother = gameObject.GetComponent<ShuttleRenderSmoother>();
        if (_smoother == null) _smoother = gameObject.AddComponent<ShuttleRenderSmoother>();
        _smoother.Init(this);
        var lampT = FindDeepChild(transform, "LandingLamp");
        if (lampT != null)
        {
            _lamp = lampT.GetComponent<LandingLamp>();
            if (_lamp == null) _lamp = lampT.gameObject.AddComponent<LandingLamp>();
        }
    }

    void RememberParkedPose()
    {
        _parkedBody = _body;
        _parkedLocalPos = _localPos;
        _parkedLocalRot = _localRot;
    }

    // Gear geometry, measured from the shuttle's own colliders with an
    // UPWARD ray grid — pose-independent, and exact where the earlier
    // estimates both failed (playtest 16's "still floats a foot or two"):
    // pose-raycasts inherit any float in whatever pose they're measured at
    // (ApplyParkedPose restores landing clearance + tilt), and collider AABB
    // corners overestimate splayed gear legs by up to ~0.7 m. The grid casts
    // up from below the hull and keeps the lowest own-collider hit points —
    // the actual foot bottoms, in shuttle-local space. _gearHeight is the
    // origin's height above them; the landing then seats those exact points
    // onto the terrain (see BeginLanding's foot refinement).
    void MeasureGearHeight()
    {
        _feetLocal.Clear();

        Bounds worldB = default;
        bool hasB = false;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (c == null || !c.enabled || c.isTrigger) continue;
            // The exit door folds down past the gear when open — never the
            // contact point.
            if (_door != null && c.transform.IsChildOf(_door.transform)) continue;
            if (!hasB) { worldB = c.bounds; hasB = true; }
            else worldB.Encapsulate(c.bounds);
        }
        if (!hasB) return;   // keep the 2.5 m default

        // Scan region: the combined collider AABB in shuttle-local space
        // (conservative is fine here — it only sizes the grid).
        Vector3 lmin = Vector3.one * float.MaxValue, lmax = Vector3.one * float.MinValue;
        for (int ci = 0; ci < 8; ci++)
        {
            Vector3 corner = worldB.center + Vector3.Scale(worldB.extents,
                new Vector3((ci & 1) == 0 ? -1f : 1f, (ci & 2) == 0 ? -1f : 1f, (ci & 4) == 0 ? -1f : 1f));
            Vector3 lp = transform.InverseTransformPoint(corner);
            lmin = Vector3.Min(lmin, lp);
            lmax = Vector3.Max(lmax, lp);
        }

        // Upward rays hit the downward-facing surfaces (foot pads, hull
        // underside). Starting below the terrain is fine: mesh raycasts
        // ignore backfaces, and non-shuttle hits are filtered out anyway.
        const int GridN = 24;
        float lowest = float.MaxValue;
        var samples = new List<Vector3>(128);
        for (int ix = 0; ix < GridN; ix++)
        for (int iz = 0; iz < GridN; iz++)
        {
            Vector3 lo = new Vector3(
                Mathf.Lerp(lmin.x, lmax.x, ix / (GridN - 1f)),
                lmin.y - 1f,
                Mathf.Lerp(lmin.z, lmax.z, iz / (GridN - 1f)));
            int n = Physics.RaycastNonAlloc(transform.TransformPoint(lo), transform.up,
                s_groundHits, (lmax.y - lmin.y) + 2f, GroundMask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            Vector3 bestP = default;
            for (int i = 0; i < n; i++)
            {
                var h = s_groundHits[i];
                if (h.collider == null || !h.collider.transform.IsChildOf(transform)) continue;
                if (_door != null && h.collider.transform.IsChildOf(_door.transform)) continue;
                if (h.distance < bestD) { bestD = h.distance; bestP = h.point; }
            }
            if (bestD == float.MaxValue) continue;
            Vector3 lp = transform.InverseTransformPoint(bestP);
            samples.Add(lp);
            if (lp.y < lowest) lowest = lp.y;
        }
        if (lowest == float.MaxValue) return;   // keep the 2.5 m default

        _gearHeight = Mathf.Clamp(-lowest, 0.5f, 10f);
        // The foot-bottom band: every sampled point within 20 cm of the
        // lowest — the surfaces that actually meet the ground.
        foreach (var lp in samples)
            if (lp.y <= lowest + 0.2f && _feetLocal.Count < 48)
                _feetLocal.Add(lp);

        // Cluster the band into LEGS (greedy xz grouping), keeping each
        // leg's lowest point — its true contact point.
        _legsLocal.Clear();
        foreach (var f in _feetLocal)
        {
            int found = -1;
            for (int i = 0; i < _legsLocal.Count; i++)
            {
                Vector3 d = _legsLocal[i] - f;
                if (d.x * d.x + d.z * d.z < 1.2f * 1.2f) { found = i; break; }
            }
            if (found < 0) { if (_legsLocal.Count < 8) _legsLocal.Add(f); }
            else if (f.y < _legsLocal[found].y) _legsLocal[found] = f;
        }
    }

    // ── Frame helpers (physics frame — see header) ───────────────────────────
    static Vector3 FrameWorldPos(CelestialBody body, Vector3 local)
    {
        var rb = body.Rigidbody;
        return rb != null ? body.Position + rb.rotation * local : body.transform.TransformPoint(local);
    }

    static Vector3 FrameLocalPos(CelestialBody body, Vector3 world)
    {
        var rb = body.Rigidbody;
        return rb != null ? Quaternion.Inverse(rb.rotation) * (world - body.Position)
                          : body.transform.InverseTransformPoint(world);
    }

    static Quaternion FrameLocalRot(CelestialBody body, Quaternion world)
    {
        var rb = body.Rigidbody;
        return rb != null ? Quaternion.Inverse(rb.rotation) * world
                          : Quaternion.Inverse(body.transform.rotation) * world;
    }

    static Quaternion FrameWorldRot(CelestialBody body, Quaternion local)
    {
        var rb = body.Rigidbody;
        return rb != null ? rb.rotation * local : body.transform.rotation * local;
    }

    Vector3 WorldPos => FrameWorldPos(_body, _localPos);
    Quaternion WorldRot => FrameWorldRot(_body, _localRot);
    Vector3 UpFromBody => (WorldPos - _body.Position).normalized;

    // ── Ground raycast that ignores the shuttle itself ───────────────────
    // ⚠️ THE WHOLE PREFAB IS ON LAYER 10 ("Body") — THE TERRAIN LAYER. A
    // plain masked ray from anywhere near the shuttle hits our own hull/gear
    // first. That was the flight-recorder-caught runaway: the altitude hold
    // measured "ground" 0.3 m below its origin (our own roof), chased 80 m
    // above a roof that rises with the shuttle, and accelerated away — and
    // the validity rays read our own curved skirt as unlandable SLOPE.
    // Every autopilot/sensor ground query MUST go through this.
    // 64, not 16: a downward ray from the shuttle's centre passes through
    // DOZENS of the shuttle's own colliders before reaching terrain, and
    // RaycastNonAlloc returns an UNSORTED buffer — at 16 the self-hits
    // crowded the real ground hit out entirely and the sensor read
    // "NO GROUND" over perfectly good terrain (recorder lap of Icey Twin:
    // 443 consecutive NO GROUND samples at 100 m altitude).
    static readonly RaycastHit[] s_groundHits = new RaycastHit[64];
    public bool GroundRay(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, s_groundHits, maxDist, GroundMask, QueryTriggerInteraction.Ignore);
        float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = s_groundHits[i];
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;   // our own hull
            if (h.distance < bestD) { bestD = h.distance; best = h; }
        }
        return bestD != float.MaxValue;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// Every body the NAV app lists. bodyType gate, mirroring
    /// MushroomSpawner.CanGrowMushroomsOn's reasoning (never a name list).
    public static bool CanLandOn(CelestialBody body)
    {
        if (body == null || body.isStaticAttractor) return false;
        return body.bodyType == CelestialBody.BodyType.Planet;
    }

    public static List<CelestialBody> LandablePlanets()
    {
        var list = new List<CelestialBody>();
        foreach (var b in NBodySimulation.Bodies)
            if (CanLandOn(b)) list.Add(b);
        return list;
    }

    /// TRAVEL pressed on the NAV app (or the debug key). Host-only; returns
    /// false if the request is invalid right now.
    public bool RequestTravel(CelestialBody target)
    {
        if (ClientDriven) return false;
        if (_phase != Phase.Parked) return false;
        // target == _body is ALLOWED (playtest 33, Sam's ask): same-planet
        // relocation — countdown, rise to hover altitude, fly to a new spot,
        // land. Same crew/door/capture flow; just no transit leg.
        if (!CanLandOn(target)) return false;
        _targetBody = target;
        SetPhase(Phase.Countdown);
        return true;
    }

    // ── Thruster fire ────────────────────────────────────────────────────────
    void EnsureFx()
    {
        if (_fx == null)
        {
            _fx = GetComponent<ShuttleThrustFX>();
            if (_fx == null) _fx = gameObject.AddComponent<ShuttleThrustFX>();
        }
        if (!_fx.Initialized) _fx.Initialize(transform);
    }

    // ── New-game intro approach (2026-08-28) ─────────────────────────────────
    // The travel system IS the intro now (Sam's call): the player wakes in the
    // stasis pod of the shuttle already inbound; the intro controller runs the
    // eyelid wake, then this flies a 30 s approach into the normal HOVER →
    // player-lands flow. Prepare places the shuttle 4 km above its authored
    // pad UNDER the intro's blackout; Launch fires on the player's first click.
    public void PrepareIntroApproach()
    {
        if (_body == null) return;
        _targetBody = _body;
        _departBody = _body;
        Vector3 upL = _localPos.sqrMagnitude > 1f ? _localPos.normalized : Vector3.up;
        _arriveAnchorLocal = _localPos + upL * HoverAltitude;
        // 4 km, not 15 km (2026-08-28, Sam: the long inbound leg swung the
        // player right past the sun — "i dont wanna force them to fly so
        // close to the sun right away"). Same 30 s flight over a quarter of
        // the distance = a slower, closer, planet-local approach.
        _departAnchorLocal = upL * (_localPos.magnitude + 4000f);
        _introApproach = true;
        _localPos = _departAnchorLocal;
        _prevLocalPos = _localPos;
        _prevLocalRot = _localRot;
        _poseJumped = true;
        transform.localPosition = _localPos;
        transform.localRotation = _localRot;
        Physics.SyncTransforms();
        // The flight prep that Countdown/Liftoff would normally do:
        if (_healPlayer == null) _healPlayer = FindObjectOfType<PlayerController>();
        if (_endless == null) _endless = FindObjectOfType<EndlessManager>();
        ShuttleRiderFrame.Prefetch();
        if (_endless != null && _endless.distanceThreshold < 3500f) _endless.distanceThreshold = 3500f;
        if (_healPlayer != null && _healPlayer.Camera != null && _healPlayer.Camera.farClipPlane < 30000f)
            _healPlayer.Camera.farClipPlane = 30000f;
        if (_door == null) _door = GetComponentInChildren<ShuttleExitDoor>(true);
        if (_door != null) _door.CloseForFlight();
        EnsureFx();
        _fx.SetSpurts(true);
        ShuttleComputerUI.EnsureBuilt();   // cockpit monitor live for the approach
    }

    /// Rider-cage the player (called by the intro after podding them).
    public void CaptureIntroRiders() { ShuttleRiderFrame.CaptureRiders(this); }

    /// First wake click: the engines light and the approach begins.
    public void LaunchIntroApproach(float seconds)
    {
        if (_phase != Phase.Parked) return;
        SetPhase(Phase.Transit);
        _transitDuration = Mathf.Max(5f, seconds);
        if (_fx != null) _fx.SetEngine(true);
    }

    /// NAV's SKIP button (playtest 35): jump the countdown to zero — the
    /// crew check and liftoff then run exactly as if the timer expired.
    public void SkipCountdown()
    {
        if (ClientDriven) return;
        if (_phase == Phase.Countdown) _phaseT = CountdownSeconds;
    }

    public bool RequestTravelByName(string bodyName)
    {
        // Guest: forward the click to the host, which validates and answers
        // with the replicated COUNTDOWN phase. Optimistically true.
        if (ClientDriven) { ShuttleSync.SendTravelRequest(bodyName); return true; }
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == bodyName) return RequestTravel(b);
        return false;
    }

    /// Continuous pilot input while hovering (from NAV, the debug reader, or a
    /// replicated guest-pilot stream). Absolute values; decays to zero if not
    /// re-supplied within PilotInputStaleSeconds.
    public void SetPilotInput(Vector2 move, float yawAxis)
    {
        // Guest pilot: input streams to the host (~30 Hz, unreliable absolute)
        // and the host applies it to its kinematic hover.
        if (ClientDriven) { ShuttleSync.SendPilotInput(move, yawAxis); return; }
        _pilotMove = Vector2.ClampMagnitude(move, 1f);
        _pilotYaw = Mathf.Clamp(yawAxis, -1f, 1f);
        _pilotInputStamp = Time.unscaledTime;
    }

    /// SPACE while hovering. Only lands when the validity check is green;
    /// returns whether the landing actually started (NAV flashes red if not).
    public bool RequestLand()
    {
        if (_phase != Phase.Hover) return false;
        // Guest: pre-check against the replicated bool for the instant red
        // flash, then a reliable one-shot to the host (a dropped land press is
        // not self-correcting — TraxSessionSync's transport lesson).
        if (ClientDriven)
        {
            if (!LandingValid) return false;
            ShuttleSync.SendLandRequest();
            return true;
        }
        if (!LandingValid || _sensor == null) return false;
        BeginLanding();
        return true;
    }

    // ── Guest-side appliers (called by ShuttleSync only) ─────────────────────
    public void ApplyRemotePhase(Phase phase, string targetBodyName, float phaseElapsed)
    {
        if (!ClientDriven) return;
        if (!string.IsNullOrEmpty(targetBodyName))
        {
            foreach (var b in NBodySimulation.Bodies)
                if (b != null && b.bodyName == targetBodyName) { _targetBody = b; break; }
        }
        if (phase != _phase)
        {
            // Rider capture/release and the door track phase changes on every
            // machine — SetPhase runs the same side effects, minus simulation.
            SetPhase(phase);
        }
        _phaseT = phaseElapsed;
    }

    public void ApplyRemotePose(string frameBodyName, Vector3 localPos, Quaternion localRot, float transitProgress)
    {
        if (!ClientDriven) return;
        _remoteProgress = transitProgress;
        CelestialBody frame = null;
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == frameBodyName) { frame = b; break; }
        if (frame == null) return;
        bool snap = false;
        if (frame != _body)
        {
            transform.SetParent(frame.transform, false);
            _body = frame;
            _poseJumped = true;
            snap = true;   // never ease across a frame switch
        }
        _remoteTargetPos = localPos;
        _remoteTargetRot = localRot;
        _hasRemoteTarget = true;
        if (snap || (_localPos - localPos).sqrMagnitude > 25f * 25f)
        {
            _localPos = localPos;
            _localRot = localRot;
            _prevLocalPos = localPos;
            _prevLocalRot = localRot;
        }
    }

    /// Authoritative physics-frame WORLD pose — for seating riders on release.
    /// Never read transform for this: at release time it still holds last
    /// frame's render pose, and on a co-orbit follower (teleported ~2.7 m per
    /// tick, no interpolation) that one-tick error seats the player inside
    /// the floor or a wall (the playtest-6 Icey Twin launch/clip-through).
    public void GetWorldPose(out Vector3 pos, out Quaternion rot)
    {
        pos = WorldPos;
        rot = WorldRot;
    }

    /// Host-side pose read for ShuttleSync's 10 Hz stream.
    public void GetPoseForSync(out string bodyName, out Vector3 localPos, out Quaternion localRot)
    {
        bodyName = _body != null ? _body.bodyName : "";
        localPos = _localPos;
        localRot = _localRot;
    }

    // ── State machine ────────────────────────────────────────────────────────
    void SetPhase(Phase next)
    {
        var prev = _phase;
        _phase = next;
        _phaseT = 0f;

        switch (next)
        {
            case Phase.Countdown:
                // Door stays usable for the whole 10 s (people can get in/out);
                // riders are decided at 0.
                // Prefetch every scene scan the flight will need (playtest 17):
                // the capture frame (door close) ran FindObjectsOfType<Rigidbody>
                // + several FindObjectOfType calls, and the release frame ran
                // two more — a deterministic frame spike at exactly the moments
                // the landing hitch was felt. The countdown button-press is
                // where a one-off cost is invisible.
                if (_healPlayer == null) _healPlayer = FindObjectOfType<PlayerController>();
                if (_endless == null) _endless = FindObjectOfType<EndlessManager>();
                ShuttleRiderFrame.Prefetch();
                break;

            case Phase.Liftoff:
                if (_door != null) _door.CloseForFlight();
                ShuttleRiderFrame.CaptureRiders(this);
                // Engines light with the ignition hold (Sam: thruster fire on
                // every liftoff, not just the intro) — spurts + main plume.
                EnsureFx();
                _fx.SetSpurts(true);
                _fx.SetEngine(true);
                _fx.SetAltitude(30f);
                // The cockpit monitor must be able to light up during the
                // flight even if the player never opened the terminal.
                ShuttleComputerUI.EnsureBuilt();
                // En-route feed (2026-08-28): top cam looking where we fly;
                // the NAV screen draws it behind the EN ROUTE status.
                if (_transitCamera == null)
                    _transitCamera = ShuttleLandingCamera.Create(this, ShuttleLandingCamera.FeedMode.Up);
                // Origin rebases cost a 1-2 frame global stutter (the
                // interpolation strip/restore machinery), and at cruise the
                // rider crosses the 1000 m threshold every couple of seconds —
                // playtest 10's "hitches while flying". Widen the threshold
                // for the flight (5 km is still comfortably inside float
                // precision) and restore it on landing.
                if (_endless == null) _endless = FindObjectOfType<EndlessManager>();
                // 3.5 km (playtest 14: 12 km = mm-level float jitter, 1 km =
                // rebase spam), now PERMANENT (playtest 19): every restore of
                // the 1 km threshold was really SCHEDULING a catch-up rebase,
                // and wherever the restore went, the pop followed it. At
                // 3.5 km the ambient rebase cadence just drops ~3.5x and each
                // shift fires wherever it naturally lands, usually mid-motion.
                if (_endless != null && _endless.distanceThreshold < 3500f)
                    _endless.distanceThreshold = 3500f;
                // Flight horizon, now PERMANENT (playtest 17): the ocean post
                // effect is capped by scene depth, so a planet beyond the far
                // plane loses its water first (playtest 11). It used to be
                // extended for the flight and RESTORED on landing — which is
                // exactly when you stand on a far planet looking back at
                // Humble Abode with no ocean ("far away the planet has no
                // water"). 30 km ran through every flight of playtests 11-16
                // with no depth artifacts, so keep it for the whole session.
                if (_healPlayer == null) _healPlayer = FindObjectOfType<PlayerController>();
                if (_healPlayer != null && _healPlayer.Camera != null
                    && _healPlayer.Camera.farClipPlane < 30000f)
                    _healPlayer.Camera.farClipPlane = 30000f;
                _departBody = _body;
                _departAnchorLocal = _localPos;   // the PAD — the bezier starts here
                // Same-planet relocation has no transit leg — no anchor needed
                // (and the facing-direction math degenerates for body==body).
                if (!ClientDriven && _targetBody != _body) ComputeArrivalAnchor();
                break;

            case Phase.Transit:
                _reparented = false;
                _upAlignVel = 0f;
                _avoidOffset = Vector3.zero;
                _avoidOffsetVel = Vector3.zero;
                // The INTRO enters Transit directly (no Liftoff), so the
                // en-route feed camera must also be created here — the
                // normal path already has one and this is a no-op.
                if (_transitCamera == null)
                    _transitCamera = ShuttleLandingCamera.Create(this, ShuttleLandingCamera.FeedMode.Up);
                Vector3 aW = FrameWorldPos(_departBody, _departAnchorLocal);
                Vector3 bW = FrameWorldPos(_targetBody, _arriveAnchorLocal);
                float arcLen = BezierLength(aW, BendControl(aW, bW), bW);
                float tBurn  = Mathf.Sqrt(5.77f * arcLen / TransitAccelMax);
                float tSpeed = 1.875f * arcLen / TransitPeakMax;
                _transitDuration = Mathf.Clamp(Mathf.Max(tBurn, tSpeed), TransitMinSeconds, TransitMaxSeconds);
                break;

            case Phase.Hover:
                _hoverVel = Vector2.zero;
                _hoverAltVel = 0f;
                _hoverAlt = HoverAltitude;
                _landRequested = false;
                // Hover: main engine off, stabiliser SPURTS keep firing (Sam:
                // "little spurts so it looks like you're being held up").
                if (_fx != null) { _fx.SetEngine(false); _fx.SetSpurts(true); }
                if (_sensor != null) _sensor.SetActive(true);
                if (_landingCamera == null) _landingCamera = ShuttleLandingCamera.Create(this);
                // The hover feed takes the screen over; free the en-route cam.
                if (_transitCamera != null) { _transitCamera.TeardownDeferred(2f); _transitCamera = null; }
                break;

            case Phase.Landing:
                // THE landing pop, finally probe-diagnosed (playtest 19's
                // [ReleaseProbe]: a ~1000 m camera step at touchdown+1.6 s):
                // an origin rebase strips rigidbody interpolation for ~3
                // frames — a visible lurch that scales with orbital speed
                // (~1.4 m on a 135 m/s body) — and restoring the 1000 m
                // threshold at descent start RESET THE REBASE CLOCK, so on a
                // fast planet (1000 m / 135 m/s ≈ 7.4 s ≈ descent+door time)
                // the next shift landed in the post-touchdown stillness on
                // EVERY landing. Instead: spend one forced shift NOW, masked
                // by the descent motion, and leave the threshold at 3500 —
                // the next natural shift is then ~26 s away, far past the
                // walk-out, and fires while the player is moving normally.
                if (_endless != null) _endless.ForceOriginShift();
                RiderReleaseBleed.Mark("forced-rebase-at-descent");
                // Pre-pay the release's contact-generation cost here too —
                // the descent is the designated spend-it-under-motion moment.
                ShuttleRiderFrame.PrewarmPhysicalRelease();
                break;

            case Phase.Parked:
                if (prev != Phase.Countdown)   // a real landing, not an abort
                {
                    // Commit the authoritative pose into the transform + PhysX
                    // BEFORE anything reads it — the per-step write below is
                    // skipped once the phase is Parked, and the transform still
                    // holds last frame's render pose at this moment.
                    transform.localPosition = _localPos;
                    transform.localRotation = _localRot;
                    Physics.SyncTransforms();
                    RememberParkedPose();
                    // DEFERRED release (playtest 7): becoming a physics body on
                    // the exact touchdown tick was the violent moment — the
                    // cabin stays a rider-mode safe cage for a beat while the
                    // world settles (the door takes 2.5 s to fold open anyway,
                    // so the handover is invisible). The deferral is cancelled
                    // if a new launch starts first — riders just keep riding.
                    _releaseRidersAt = Time.fixedTime + ReleaseSettleSeconds;
                    if (_door != null) _door.ReopenAfterFlight();
                    // Handover window watcher: carries the camera-side
                    // interpolation-warmup bridge at the release (all builds)
                    // and the frame recorder (editor/cheats, reports 3 s
                    // after the window closes).
                    RiderReleaseBleed.BeginWindow(_healPlayer, _body, 8f);
                    // Start the up re-orientation NOW (playtest 14): waiting
                    // for the physical release made the player visibly rotate
                    // upright seconds AFTER the door opened. Blending during
                    // the door's own fold-open finishes before they can step
                    // out — and the release then has no rotation event at all.
                    BlendRiderUpOut(2f);
                }
                if (_sensor != null) _sensor.SetActive(false);
                // Deferred teardown (playtest 19): destroying the feed camera
                // + releasing its RenderTexture ON the touchdown frame stacked
                // straight onto the door-open moment. Disable now, free later.
                if (_landingCamera != null) { _landingCamera.TeardownDeferred(6f); _landingCamera = null; }
                if (_transitCamera != null) { _transitCamera.TeardownDeferred(6f); _transitCamera = null; }
                // NOTE: neither the rebase threshold nor farClipPlane is ever
                // restored (playtests 17/19): every "restore on landing" was
                // really scheduling a visible catch-up event into the
                // post-touchdown stillness. Threshold stays 3.5 km; the one
                // pending shift was spent at descent start, masked by motion.
                _introApproach = false;
                if (_fx != null) _fx.Shutdown();   // engines collapse at touchdown
                _targetBody = null;
                break;
        }

        if (next == Phase.Hover || next == Phase.Landing)
        {
            if (_sensor != null) _sensor.SetActive(true);
        }

        if (_lamp != null) _lamp.SetPhase(next, LandingValid);
        OnPhaseChanged?.Invoke(next);
    }

    // Arrival hover anchor: the surface point on the target facing the
    // departure body right now, +100 m (handoff §4 — good enough for a demo;
    // the hover phase lets the pilot move anyway).
    void ComputeArrivalAnchor()
    {
        Vector3 dir = (_departBody.Position - _targetBody.Position).normalized;
        Vector3 origin = _targetBody.Position + dir * (_targetBody.radius * 1.5f + 200f);
        Vector3 surface;
        if (GroundRay(origin, -dir, _targetBody.radius * 2f, out RaycastHit hit))
            surface = hit.point;
        else
            surface = _targetBody.Position + dir * _targetBody.radius;
        Vector3 hoverWorld = surface + dir * HoverAltitude;
        _arriveAnchorLocal = FrameLocalPos(_targetBody, hoverWorld);
    }

    void Update()
    {
        DebugKeys();
    }

    void FixedUpdate()
    {
        if (_body == null) return;

        // Deferred post-touchdown release (see SetPhase Parked). Runs on every
        // machine; cancelled by a new launch (phase left Parked first).
        if (_releaseRidersAt >= 0f)
        {
            if (_phase != Phase.Parked) _releaseRidersAt = -1f;   // relaunched — keep riding
            else if (Time.fixedTime >= _releaseRidersAt)
            {
                _releaseRidersAt = -1f;
                ShuttleRiderFrame.ReleaseRiders(this);
                // No threshold restore here or ANYWHERE post-landing
                // (playtest 19): restoring the 1 km threshold IS scheduling a
                // catch-up rebase, and it popped wherever it was put. The
                // threshold stays at 3.5 km for the session; the one pending
                // shift is spent at descent start, masked by motion.
            }
        }

        _prevLocalPos = _localPos;
        _prevLocalRot = _localRot;

        // The timer advances on EVERY machine so a guest's countdown ticks
        // smoothly between the host's 2 s phase heartbeats (which re-sync it).
        _phaseT += Time.fixedDeltaTime;
        if (!ClientDriven)
        {
            switch (_phase)
            {
                case Phase.Countdown: TickCountdown(); break;
                case Phase.Liftoff:   TickLiftoff();   break;
                case Phase.Transit:   TickTransit();   break;
                case Phase.Hover:     TickHover();     break;
                case Phase.Landing:   TickLanding();   break;
            }
        }
        else if (_hasRemoteTarget && _phase != Phase.Parked)
        {
            // Ease toward the host's 10 Hz pose — absolute values, so a
            // dropped packet self-corrects on the next one.
            float t = 1f - Mathf.Exp(-12f * Time.fixedDeltaTime);
            _localPos = Vector3.Lerp(_localPos, _remoteTargetPos, t);
            _localRot = Quaternion.Slerp(_localRot, _remoteTargetRot, t);
        }

        // Self-heal (playtest 6): if some outside system clears the rider
        // statics mid-flight, the player is left kinematic+parented while the
        // NORMAL movement path runs — walk-lock now, a zero-orbital-velocity
        // fling at landing. Re-assert every step; the [RiderMode] transition
        // log names the culprit.
        if (!ClientDriven && _phase != Phase.Parked && _phase != Phase.Countdown)
        {
            if (_healPlayer == null && Time.unscaledTime >= _nextHealScanAt)
            {
                _nextHealScanAt = Time.unscaledTime + 1f;
                _healPlayer = FindObjectOfType<PlayerController>();
            }
            if (_healPlayer != null && !PlayerController.RiderMode
                && _healPlayer.transform.IsChildOf(transform))
            {
                Debug.LogWarning("[ShuttleAutopilot] rider state was cleared mid-flight — re-asserting");
                PlayerController.RiderMode = true;
                PlayerController.RiderPlatform = transform;
                PlayerController.UpOverrideTransform = transform;
            }

            // Cabin safety net (playtest 9): a rider must never end up below
            // the cabin — a missed floor clamp during heavy arrival pose
            // changes occasionally dropped players through onto the planet.
            // Re-seat at the capture spot and log what we saw.
            if (_healPlayer != null && PlayerController.RiderMode
                && _healPlayer.transform.IsChildOf(transform)
                && ShuttleRiderFrame.TryGetPlayerCaptureLocal(out Vector3 capLocal)
                && _healPlayer.transform.localPosition.y < capLocal.y - 2f)
            {
                Debug.LogWarning("[ShuttleAutopilot] rider slipped below the cabin at local "
                    + _healPlayer.transform.localPosition.ToString("F2") + " (phase " + _phase + ") — re-seating");
                _healPlayer.RiderReseat(transform.TransformPoint(capLocal));
            }
        }

        // A reparent this step leaves _prevLocal* in the OLD body's frame —
        // lerping across frames is garbage, so collapse the smoothing span.
        if (_poseJumped)
        {
            _prevLocalPos = _localPos;
            _prevLocalRot = _localRot;
        }

        if (_phase != Phase.Parked)
        {
            _speed = _poseJumped ? _speed
                : (_localPos - _prevLocalPos).magnitude / Mathf.Max(0.0001f, Time.fixedDeltaTime);
            transform.localPosition = _localPos;
            transform.localRotation = _localRot;
            // autoSyncTransforms is off project-wide — commit the moved shuttle
            // colliders (part of the planet rb's compound) before the riders'
            // ground/wall queries this step.
            Physics.SyncTransforms();
        }
        else
        {
            _speed = 0f;
        }

        if (_lamp != null && (_phase == Phase.Hover || _phase == Phase.Landing))
            _lamp.SetPhase(_phase, LandingValid);
    }

    void TickCountdown()
    {
        if (_phaseT < CountdownSeconds) return;

        // D-1: leave with whoever is inside; abort (only) if NOBODY is.
        if (!ShuttleRiderFrame.AnyoneInside(this) && !DebugSkipCrewCheck)
        {
            SetPhase(Phase.Parked);
            OnLaunchAborted?.Invoke();
            return;
        }
        SetPhase(Phase.Liftoff);
    }

    void TickLiftoff()
    {
        // Ignition hold — NO motion (playtest 16). The old separate 300 m
        // vertical rise ended AT REST before the transit re-accelerated
        // toward the target — the "goes up, slows down, then starts moving
        // in the direction of the destination" alien seam. The whole journey
        // is now one continuous bezier flown by TickTransit, which leaves
        // the pad radially anyway; this phase just seals the door (the fold
        // takes ~2.5 s) and lets the engines spool.
        //
        // Same-planet relocation (playtest 33): no transit leg — straight to
        // HOVER, whose altitude spring flies the shuttle smoothly up from
        // the pad to hover altitude; the pilot then relocates with WASD and
        // lands wherever the light goes green.
        if (_phaseT >= LiftoffSeconds)
            SetPhase(_targetBody == _body ? Phase.Hover : Phase.Transit);
    }

    // The flight curve: quadratic bezier pad → (radially above the pad) →
    // arrival anchor. See the flight-profile constants for why.
    Vector3 BendControl(Vector3 aWorld, Vector3 bWorld)
    {
        Vector3 upA = (aWorld - _departBody.Position).normalized;
        Vector3 dirT = (bWorld - aWorld).normalized;
        float bend = Mathf.Clamp(0.15f * Vector3.Distance(aWorld, bWorld), TransitBendMin, TransitBendMax);
        // Far-side departures: a target BELOW the pad's horizon would fold
        // the bezier into a hairpin (a derivative cusp — the shuttle would
        // climb, stop mid-air and reverse, and the arc-length map spikes).
        // Lean the climb direction toward the target's side of the sky just
        // enough to turn the hairpin into an up-and-around arc; targets in
        // the open sky keep the pure radial rocket departure.
        float facing = Vector3.Dot(dirT, upA);
        float lean = Mathf.InverseLerp(0.3f, -0.8f, facing) * 0.6f;
        if (lean > 0f)
        {
            Vector3 tan = dirT - upA * facing;
            Vector3 tanDir = tan.sqrMagnitude > 0.01f ? tan.normalized : StablePerpendicular(upA);
            return aWorld + Vector3.Slerp(upA, tanDir, lean) * bend;
        }
        return aWorld + upA * bend;
    }

    static Vector3 QuadBezier(Vector3 a, Vector3 c, Vector3 b, float t)
    {
        float m = 1f - t;
        return m * m * a + 2f * m * t * c + t * t * b;
    }

    // 16-segment cumulative arc-length table, rebuilt per call — the
    // endpoints orbit, so the curve is different every step.
    static readonly float[] s_arcLen = new float[17];
    static float BezierLength(Vector3 a, Vector3 c, Vector3 b)
    {
        Vector3 prev = a;
        float total = 0f;
        s_arcLen[0] = 0f;
        for (int i = 1; i <= 16; i++)
        {
            Vector3 p = QuadBezier(a, c, b, i / 16f);
            total += Vector3.Distance(prev, p);
            s_arcLen[i] = total;
            prev = p;
        }
        return total;
    }

    // Map an ARC-LENGTH fraction to the bezier parameter — the raw parameter
    // moves ~2:1 faster over the bent part, which would corrupt the speed
    // profile the ease was built for. The inverse is interpolated with C1
    // Hermite (Catmull-Rom) across the table: plain linear interpolation has
    // slope kinks at the 16 segment seams that surfaced as ~20% world-speed
    // steps mid-flight (caught by the profile sim before it shipped).
    static float BezierArcParam(Vector3 a, Vector3 c, Vector3 b, float frac)
    {
        float total = BezierLength(a, c, b);
        if (total < 0.01f) return frac;
        float target = Mathf.Clamp(frac, 0f, 1f) * total;
        for (int i = 1; i <= 16; i++)
        {
            if (s_arcLen[i] < target) continue;
            float L0 = s_arcLen[i - 1], L1 = s_arcLen[i];
            float h = L1 - L0;
            if (h < 0.0001f) return i / 16f;
            float t0 = (i - 1) / 16f, t1 = i / 16f;
            float m0 = ArcSlope(i - 1), m1 = ArcSlope(i);
            float x = (target - L0) / h;
            float x2 = x * x, x3 = x2 * x;
            return (2f * x3 - 3f * x2 + 1f) * t0 + (x3 - 2f * x2 + x) * h * m0
                 + (-2f * x3 + 3f * x2) * t1 + (x3 - x2) * h * m1;
        }
        return 1f;
    }

    // dt/dL at table node i (central difference over the neighbours).
    static float ArcSlope(int i)
    {
        int lo = i > 0 ? i - 1 : 0, hi = i < 16 ? i + 1 : 16;
        float dL = s_arcLen[hi] - s_arcLen[lo];
        return dL > 0.0001f ? ((hi - lo) / 16f) / dL : 0f;
    }

    void TickTransit()
    {
        float u = Mathf.Clamp01(_phaseT / _transitDuration);
        // Smootherstep: one velocity bell — accelerate to the midpoint,
        // decelerate to rest at the anchor — zero accel at both endpoints.
        float ease = u * u * u * (u * (6f * u - 15f) + 10f);

        // Everything evaluated FRESH every step so the path tracks the
        // orbiting bodies and origin rebases (handoff §4 — never cache world).
        Vector3 aWorld = FrameWorldPos(_departBody, _departAnchorLocal);
        Vector3 bWorld = FrameWorldPos(_targetBody, _arriveAnchorLocal);
        Vector3 c1 = BendControl(aWorld, bWorld);
        float s = BezierArcParam(aWorld, c1, bWorld, ease);
        Vector3 posW = ApplyTransitAvoidance(QuadBezier(aWorld, c1, bWorld, s), aWorld, bWorld, ease);

        Vector3 upA = (aWorld - _departBody.Position).normalized;
        Vector3 upB = (bWorld - _targetBody.Position).normalized;
        // ROCKET ATTITUDE (playtest 42, Sam's spec — no more UFO gliding):
        // the thrust axis is the ship's UP. Rise radially off the pad, pitch
        // the nose (ship top) onto the destination for the main burn, flip
        // 180° around a stable side axis at the coast, retro-burn tail-first,
        // then settle to the arrival radial for the hover. The SmoothDamp
        // alignment below chases this target, so every attitude change ramps.
        Vector3 travelDir = (bWorld - aWorld).normalized;
        Vector3 flipSide = Vector3.Cross(travelDir, upB);
        flipSide = flipSide.sqrMagnitude < 0.01f ? StablePerpendicular(travelDir) : flipSide.normalized;
        Vector3 upW;
        if (ease < 0.12f)      upW = Vector3.Slerp(upA, travelDir, Mathf.SmoothStep(0f, 1f, ease / 0.12f));
        else if (ease < 0.45f) upW = travelDir;
        else if (ease < 0.60f) upW = Quaternion.AngleAxis(180f * Mathf.SmoothStep(0f, 1f, (ease - 0.45f) / 0.15f), flipSide) * travelDir;
        else if (ease < 0.90f) upW = -travelDir;
        else                   upW = Vector3.Slerp(-travelDir, upB, Mathf.SmoothStep(0f, 1f, (ease - 0.90f) / 0.10f));

        // Engine fire follows the burns: main burn nose-first, silent coast
        // through the flip, retro burn tail-first into the hover.
        if (_fx != null)
        {
            _fx.SetEngine(ease < 0.45f || (ease >= 0.60f && ease < 0.92f));
            _fx.SetAltitude(150f);
        }

        // Reparent to the arrival frame at the midpoint.
        if (!_reparented && ease >= 0.5f)
        {
            _reparented = true;
            // ⚠️ Convert the stored ROTATION through its WORLD value first
            // (playtest 13 — Sam called it: not floating origin). _localRot
            // was relative to the DEPART body; reading it under the arrival
            // body's different authored rotation really did rotate the whole
            // shuttle + rider by the difference, at the exact midpoint.
            // Position was always immune (recomputed fresh from the anchors).
            Quaternion worldRotAtSwitch = FrameWorldRot(_body, _localRot);
            transform.SetParent(_targetBody.transform, false);
            _body = _targetBody;
            _localRot = FrameLocalRot(_body, worldRotAtSwitch);
            _poseJumped = true;
        }

        Quaternion curW = FrameWorldRot(_body, _localRot);
        // C1-continuous up alignment (playtest 15's "choppy when it rotates
        // after takeoff"): playtest 13's fixed 45°/s RotateTowards jumped from
        // rest to full rate on the first transit step and stopped dead on
        // convergence — both angular-velocity cliffs read as hitches, worst on
        // tilted-pad departures (biggest initial error). SmoothDamp ramps the
        // rate up from zero and eases it out; the hover's own upright catches
        // any residual at the far seam.
        Vector3 curUp = curW * Vector3.up;
        float misDeg = Vector3.Angle(curUp, upW);
        float easedDeg = Mathf.SmoothDamp(misDeg, 0f, ref _upAlignVel, 1.2f, Mathf.Infinity, Time.fixedDeltaTime);
        Vector3 alignedUp = Vector3.RotateTowards(curUp, upW, Mathf.Max(0f, misDeg - easedDeg) * Mathf.Deg2Rad, 0f);
        Quaternion rotW = Quaternion.FromToRotation(curUp, alignedUp) * curW;

        _localPos = FrameLocalPos(_body, posW);
        _localRot = FrameLocalRot(_body, rotW);

        if (u >= 1f) SetPhase(Phase.Hover);
    }

    // ── Transit obstacle avoidance (playtest 15) ─────────────────────────────
    // The straight A→B lerp happily flew through moons, other planets and the
    // sun. Analytic, evaluated fresh from live body positions every step like
    // the rest of the path math (never integrated — rebase- and orbit-proof):
    //  - every body except the departure planet contributes a LATERAL push
    //    that keeps the path outside its safe sphere (radius*1.35 + 250 m),
    //    faded out for the target planet on final approach (its arrival
    //    anchor sits inside its own safe sphere by design). The summed push
    //    is smooth-damped so entering/leaving an influence region is a
    //    swerve, not a kink.
    //  - the departure planet instead gets a hard radial altitude floor at
    //    the liftoff-top shell: a pad on its far side sends the base path
    //    straight through the planet, and a lateral push can't help while
    //    the start anchor itself sits deep inside the safe sphere.
    Vector3 ApplyTransitAvoidance(Vector3 posW, Vector3 aWorld, Vector3 bWorld, float ease)
    {
        // Intro approach: a scripted straight-in descent to the authored pad
        // — avoidance and the depart floor are meaningless inbound.
        if (_introApproach) return posW;

        Vector3 travel = bWorld - aWorld;
        float travelLen = travel.magnitude;
        if (travelLen < 1f) return posW;
        Vector3 travelDir = travel / travelLen;

        Vector3 wanted = Vector3.zero;
        foreach (var obstacle in NBodySimulation.Bodies)
        {
            if (obstacle == null || obstacle == _departBody) continue;
            float w = obstacle == _targetBody ? 1f - Mathf.SmoothStep(0.55f, 0.8f, ease) : 1f;
            if (w <= 0f) continue;
            float safeR = obstacle.radius * 1.35f + 250f;
            Vector3 toPos = posW - obstacle.Position;
            float along = Vector3.Dot(toPos, travelDir);
            if (Mathf.Abs(along) >= safeR) continue;
            Vector3 lateral = toPos - travelDir * along;
            float lat = lateral.magnitude;
            float needed = Mathf.Sqrt(safeR * safeR - along * along);
            if (lat >= needed) continue;
            // Dead-centre pass: no meaningful lateral direction — pick a
            // stable perpendicular so the detour side can't flip mid-flight.
            Vector3 latDir = lat > 1f ? lateral / lat : StablePerpendicular(travelDir);
            wanted += latDir * ((needed - lat) * w);
        }

        _avoidOffset = Vector3.SmoothDamp(_avoidOffset, wanted, ref _avoidOffsetVel, 0.6f, Mathf.Infinity, Time.fixedDeltaTime);
        Vector3 result = posW + _avoidOffset;

        if (_departBody != null)
        {
            Vector3 toDep = result - _departBody.Position;
            float d = toDep.magnitude;
            // The path now STARTS on the pad, so the floor ramps up from pad
            // altitude to pad+250 m over the first ~1/6 of the leg — by then
            // the bezier's own radial climb is far above it. The floor only
            // binds when a far-side pad sends the base path back around (or
            // through) the departure planet.
            float minR = (aWorld - _departBody.Position).magnitude - 5f + Mathf.Min(ease * 6f, 1f) * 250f;
            if (d > 1f && d < minR)
                result = _departBody.Position + toDep * (minR / d);
        }
        return result;
    }

    static Vector3 StablePerpendicular(Vector3 dir)
    {
        Vector3 p = Vector3.Cross(dir, Vector3.up);
        if (p.sqrMagnitude < 0.01f) p = Vector3.Cross(dir, Vector3.right);
        return p.normalized;
    }

    // Planet-locked hover — every quantity below is in the target body's LOCAL
    // frame (bodies never spin, so local directions are stable). See the
    // _hoverVel field comment for why.
    void TickHover()
    {
        float dt = Time.fixedDeltaTime;
        float r = _localPos.magnitude;
        if (r < 1f) return;   // degenerate — never true in practice
        Vector3 radial = _localPos / r;

        // Pilot input decays to zero on silence (guest pilot dropped, NAV closed).
        bool fresh = Time.unscaledTime - _pilotInputStamp <= PilotInputStaleSeconds;
        Vector2 move = fresh ? _pilotMove : Vector2.zero;
        float yawIn = fresh ? _pilotYaw : 0f;

        // Heavy-vehicle smoothing on the tangential velocity.
        _hoverVel = Vector2.MoveTowards(_hoverVel, move * HoverMaxSpeed, HoverAccel * dt);

        // Yaw frame on the sphere's surface, from the shuttle's own heading.
        Vector3 fwd = Vector3.ProjectOnPlane(_localRot * Vector3.forward, radial).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(_localRot * Vector3.right, radial).normalized;
        Vector3 right = Vector3.Cross(radial, fwd);   // up × fwd = right (Unity handedness)

        // Slide AROUND the planet: the tangential velocity becomes a rotation
        // of the radial direction about the core, so the shuttle orbits the
        // surface instead of integrating a straight line off into space.
        Vector3 tangential = fwd * _hoverVel.y + right * _hoverVel.x;
        float speed = tangential.magnitude;
        if (speed > 0.001f)
        {
            Vector3 axis = Vector3.Cross(radial, tangential / speed);
            float angDeg = (speed * dt / r) * Mathf.Rad2Deg;
            radial = (Quaternion.AngleAxis(angDeg, axis) * radial).normalized;
        }

        // Altitude spring to HoverAltitude above the terrain under the new
        // spot (radial raycast in world — colliders live in the rb frame).
        Vector3 upW = FrameWorldRot(_body, Quaternion.identity) * radial;
        Vector3 castFromW = FrameWorldPos(_body, radial * r) + upW * 5f;
        float terrainR;
        if (GroundRay(castFromW, -upW, 400f, out RaycastHit hit))
            terrainR = r - (hit.distance - 5f);
        else
            terrainR = r - _hoverAlt;   // no ground under us — hold the current shell
        // Ease the MEASURED altitude toward the target — this both converges
        // to 80 m and low-passes terrain bumps sliding underneath at 15 m/s.
        float measuredAlt = r - terrainR;
        _hoverAlt = Mathf.SmoothDamp(measuredAlt, HoverAltitude, ref _hoverAltVel, HoverAltSmooth, Mathf.Infinity, dt);
        _localPos = radial * (terrainR + _hoverAlt);
        if (_fx != null) _fx.SetAltitude(_hoverAlt);   // spurt/light power

        // Bottom always faces the core: up -> radial, yaw preserved + Q/E.
        // Rate-limited (playtest 15): the transit's eased alignment can end a
        // few degrees short, and the old one-step FromToRotation snapped that
        // residual at the hover seam. 60°/s catches it in a blink and is
        // invisible in steady state (the radial drifts ~2°/s at full stick).
        Vector3 hoverUp = _localRot * Vector3.up;
        Vector3 hoverUpTo = Vector3.RotateTowards(hoverUp, radial, 60f * Mathf.Deg2Rad * dt, 0f);
        Quaternion upright = Quaternion.FromToRotation(hoverUp, hoverUpTo) * _localRot;
        if (Mathf.Abs(yawIn) > 0.001f)
            upright = Quaternion.AngleAxis(yawIn * HoverYawDegSec * dt, radial) * upright;
        _localRot = upright;
    }

    void BeginLanding()
    {
        Vector3 worldPos = WorldPos;
        Vector3 up = UpFromBody;
        if (!GroundRay(worldPos + up * 5f, -up, 400f, out RaycastHit hit))
            return;   // no ground below — refuse (validity should already be red)

        // Land CONFORMING to the hillside (playtest 4: demanding flat ground
        // made green spots a treasure hunt). The sensor's fitted plane says
        // which way the slope faces; the shuttle tilts to it during the
        // descent and the gear settles along the surface normal.
        Vector3 n = _sensor != null && _sensor.Valid ? _sensor.PlaneNormal : up;
        if (n.sqrMagnitude < 0.5f || Vector3.Dot(n, up) < 0.7f) n = up;   // sanity — never land sideways

        // With measured feet the seat is refined exactly below — the bump
        // clearance heuristic only survives as the no-feet fallback
        // (playtest 10's half-bump compromise).
        float clearance = _feetLocal.Count > 0
            ? 0.02f
            : (_sensor != null && _sensor.Valid ? _sensor.MaxAboveDeviation * 0.5f + 0.05f : 0.1f);

        // Seat on the FITTED PLANE, not the raw centre hit (playtest 15): the
        // centre ray can land in a dip or on a bump inside the footprint, and
        // that deviation went straight into the parked height — floating on a
        // bump, gear-in-ground over a dip. The shuttle tilts to the plane, so
        // the plane is where the pads actually rest.
        Vector3 touch = hit.point;
        if (_sensor != null && _sensor.Valid && !float.IsNaN(_sensor.CenterDeviation))
            touch -= n * _sensor.CenterDeviation;

        _landStartLocal = _localPos;
        _landStartLocalRot = _localRot;
        Quaternion curW = FrameWorldRot(_body, _localRot);
        Quaternion tgtRotW = Quaternion.FromToRotation(curW * Vector3.up, n) * curW;
        _landTargetLocalRot = FrameLocalRot(_body, tgtRotW);

        Vector3 tgtW = touch + n * (_gearHeight + clearance);

        // ALL-FEET SEAT (playtest 17 — "1 leg touching, 3 floating"): the
        // sensor's 9-ray plane spans the whole 12 m footprint, but what the
        // eye judges is the terrain under the four gear legs. Two passes:
        //   1. cast down from each leg at the guess pose, least-squares fit
        //      the CONTACT plane through those terrain points, and retilt
        //      the shuttle to it — the legs now agree with the ground;
        //   2. recast and SINK until every leg touches (bury capped so freak
        //      terrain can't swallow a leg — a slightly buried pad reads as
        //      "settled", a hovering one reads as broken).
        if (_legsLocal.Count >= 3)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int hits = 0;
                for (int i = 0; i < _legsLocal.Count && i < 8; i++)
                {
                    Vector3 footW = tgtW + tgtRotW * _legsLocal[i];
                    if (GroundRay(footW + n * 2f, -n, 30f, out RaycastHit fh))
                    {
                        s_legHits[hits] = fh.point;
                        s_legGaps[hits] = fh.distance - 2f;
                        hits++;
                    }
                }
                if (hits < 3) break;
                if (pass == 0)
                {
                    Vector3 fitN = FitPlaneNormal(s_legHits, hits, n);
                    if (Vector3.Dot(fitN, up) >= 0.82f)   // sanity: ≤ ~35° tilt
                    {
                        tgtRotW = Quaternion.FromToRotation(n, fitN) * tgtRotW;
                        n = fitN;
                        _landTargetLocalRot = FrameLocalRot(_body, tgtRotW);
                    }
                }
                else
                {
                    float minGap = float.MaxValue, maxGap = float.MinValue;
                    for (int i = 0; i < hits; i++)
                    {
                        if (s_legGaps[i] < minGap) minGap = s_legGaps[i];
                        if (s_legGaps[i] > maxGap) maxGap = s_legGaps[i];
                    }
                    tgtW -= n * (Mathf.Min(maxGap, minGap + MaxLegBury) + 0.02f);
                }
            }
        }
        else if (_feetLocal.Count > 0)
        {
            // Legs not identified — old rigid lowest-touch seat.
            float minGap = float.MaxValue;
            for (int i = 0; i < _feetLocal.Count; i++)
            {
                Vector3 footW = tgtW + tgtRotW * _feetLocal[i];
                if (GroundRay(footW + n * 2f, -n, 30f, out RaycastHit fh))
                    minGap = Mathf.Min(minGap, fh.distance - 2f);
            }
            if (minGap != float.MaxValue)
                tgtW -= n * (minGap - 0.02f);
        }

        _landTargetLocal = FrameLocalPos(_body, tgtW);
        float height = Vector3.Distance(_landStartLocal, _landTargetLocal);
        _landHeight = height;
        _landDuration = Mathf.Clamp(height / 15f, LandingMinSeconds, LandingMaxSeconds);
        _settleT = 0f;
        SetPhase(Phase.Landing);
    }

    // Least-squares plane through up-to-8 leg contact points, expressed as a
    // normal near refN (solves the 2x2 normal equations for the height field
    // y = a·x + b·z in the tangent frame around refN; normal ∝ refN − a·tx − b·tz).
    static readonly Vector3[] s_legHits = new Vector3[8];
    static readonly float[] s_legGaps = new float[8];
    static Vector3 FitPlaneNormal(Vector3[] pts, int count, Vector3 refN)
    {
        Vector3 c = Vector3.zero;
        for (int i = 0; i < count; i++) c += pts[i];
        c /= count;
        Vector3 tx = Vector3.Cross(refN, Mathf.Abs(refN.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 tz = Vector3.Cross(refN, tx);
        float sxx = 0f, sxz = 0f, szz = 0f, sxy = 0f, szy = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 d = pts[i] - c;
            float x = Vector3.Dot(d, tx), z = Vector3.Dot(d, tz), y = Vector3.Dot(d, refN);
            sxx += x * x; sxz += x * z; szz += z * z; sxy += x * y; szy += z * y;
        }
        float det = sxx * szz - sxz * sxz;
        if (Mathf.Abs(det) < 0.001f) return refN;
        float a = (sxy * szz - szy * sxz) / det;
        float b = (szy * sxx - sxy * sxz) / det;
        return (refN - tx * a - tz * b).normalized;
    }

    // Fall-then-brake set-down (playtest 42, Sam's spec: "lets you fall down,
    // then applies the reverse thrust to make your impact soft"): velocity
    // ramps up linearly over the first 55% of the descent (free fall), then a
    // smoothstep retro-burn kills it to zero at touchdown. Area-normalised so
    // ease(1) = 1.
    static float LandingEase(float u)
    {
        const float k = 0.55f;   // free-fall fraction
        const float vp = 2f;     // normalised peak sink rate
        if (u <= k) return 0.5f * vp * u * u / k;
        float t = (u - k) / (1f - k);
        float integ = t - t * t * t + 0.5f * t * t * t * t;   // ∫(1 − smoothstep)
        return 0.5f * vp * k + vp * (1f - k) * integ;
    }

    void TickLanding()
    {
        float u = Mathf.Clamp01(_phaseT / _landDuration);
        float ease = LandingEase(u);
        // Engines: silent free fall, then the retro-burn — plume power ramps
        // to the fireball as the ground nears (SetAltitude drives it).
        if (_fx != null)
        {
            _fx.SetEngine(u >= 0.5f);
            _fx.SetAltitude(Mathf.Max(0f, (1f - ease) * _landHeight));
        }
        _localPos = Vector3.Lerp(_landStartLocal, _landTargetLocal, ease);
        // Ease from hover-upright into the surface-conforming tilt.
        _localRot = Quaternion.Slerp(_landStartLocalRot, _landTargetLocalRot, ease);

        if (u >= 1f)
        {
            _settleT += Time.fixedDeltaTime;
            if (_settleT >= SettleSeconds) SetPhase(Phase.Parked);
        }
    }

    // ── Render smoothing handshake ───────────────────────────────────────────
    public bool GetSmoothingPose(out Vector3 prevPos, out Quaternion prevRot, out Vector3 curPos, out Quaternion curRot, out bool jumped)
    {
        prevPos = _prevLocalPos; prevRot = _prevLocalRot;
        curPos = _localPos; curRot = _localRot;
        jumped = _poseJumped;
        _poseJumped = false;
        return _phase != Phase.Parked;
    }

    /// UpOverrideTransform blend-out on release (intro's proven proxy recipe).
    public void BlendRiderUpOut(float seconds)
    {
        if (_upBlendOut != null) StopCoroutine(_upBlendOut);
        _upBlendOut = StartCoroutine(BlendUpOverrideOut(seconds));
    }

    /// Blend INTO the ride (playtest 14: handing UpOverrideTransform straight
    /// to the shuttle at door-close snapped the player from planet-up to the
    /// parked shuttle's tilted up in one step). Proxy eases from the player's
    /// current up to the LIVE shuttle up, then hands over to the shuttle.
    public void BlendRiderUpIn(float seconds)
    {
        if (_upBlendOut != null) StopCoroutine(_upBlendOut);
        _upBlendOut = StartCoroutine(BlendUpOverrideIn(seconds));
    }

    IEnumerator BlendUpOverrideIn(float seconds)
    {
        var pc = _healPlayer != null ? _healPlayer : (_healPlayer = FindObjectOfType<PlayerController>());
        var proxy = new GameObject("ShuttleTravelUpBlendProxy").transform;
        proxy.SetParent(transform, false);
        Vector3 fromUp = pc != null ? pc.transform.up : transform.up;
        proxy.rotation = Quaternion.FromToRotation(Vector3.up, fromUp);
        PlayerController.UpOverrideTransform = proxy;

        float t = 0f;
        while (t < seconds && pc != null)
        {
            t += Time.deltaTime;
            // Live shuttle up — it may already be moving (liftoff starts).
            Vector3 upNow = Vector3.Slerp(fromUp, transform.up, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            proxy.rotation = Quaternion.FromToRotation(Vector3.up, upNow);
            yield return null;
        }
        if (PlayerController.UpOverrideTransform == proxy)
            PlayerController.UpOverrideTransform = transform;   // the ride owns the up from here
        Destroy(proxy.gameObject);
        _upBlendOut = null;
    }

    IEnumerator BlendUpOverrideOut(float seconds)
    {
        var pc = _healPlayer != null ? _healPlayer : (_healPlayer = FindObjectOfType<PlayerController>());
        var body = _body;
        var proxy = new GameObject("ShuttleTravelUpBlendProxy").transform;
        proxy.SetParent(transform, false);
        Vector3 fromUp = transform.up;
        proxy.rotation = Quaternion.FromToRotation(Vector3.up, fromUp);
        PlayerController.UpOverrideTransform = proxy;

        float t = 0f;
        while (t < seconds && pc != null)
        {
            t += Time.deltaTime;
            Vector3 gravityUp = body != null ? (pc.transform.position - body.Position).normalized : fromUp;
            Vector3 upNow = Vector3.Slerp(fromUp, gravityUp, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            proxy.rotation = Quaternion.FromToRotation(Vector3.up, upNow);
            yield return null;
        }
        // HOLD gravity-up until the physical release (playtest 15's landing
        // double-hitch): nulling the override here, with ~0.5 s of rider cage
        // still to run, handed the up back to RiderPlatform — the TILTED
        // parked shuttle — so the freshly-uprighted player snapped back to
        // shuttle-up, then snapped upright AGAIN when the release reseeded
        // the gravity blend. Track live gravity-up until RiderMode ends.
        while (pc != null && PlayerController.RiderMode
               && PlayerController.UpOverrideTransform == proxy
               && _phase == Phase.Parked)
        {
            Vector3 gUp = body != null ? (pc.transform.position - body.Position).normalized : proxy.up;
            proxy.rotation = Quaternion.FromToRotation(Vector3.up, gUp);
            yield return null;
        }
        if (PlayerController.UpOverrideTransform == proxy)
        {
            // Relaunched while holding (release deferral cancelled): the ride
            // owns the up again. Otherwise released — clear it.
            PlayerController.UpOverrideTransform = (_phase != Phase.Parked && PlayerController.RiderMode)
                ? transform : null;
        }
        RiderReleaseBleed.Mark("upblend-end");
        Destroy(proxy.gameObject);
        _upBlendOut = null;
    }

    // ── Save handshake ───────────────────────────────────────────────────────
    public void GetParkedPose(out string bodyName, out Vector3 localPos, out Quaternion localRot)
    {
        if (_phase == Phase.Parked && _body != null)
        {
            bodyName = _body.bodyName; localPos = _localPos; localRot = _localRot;
        }
        else
        {
            bodyName = _parkedBody != null ? _parkedBody.bodyName : "";
            localPos = _parkedLocalPos; localRot = _parkedLocalRot;
        }
    }

    public void ApplyParkedPose(string bodyName, Vector3 localPos, Quaternion localRot)
    {
        CelestialBody target = null;
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == bodyName) { target = b; break; }
        if (target == null) return;
        if (_phase != Phase.Parked) SetPhase(Phase.Parked);
        transform.SetParent(target.transform, false);
        _body = target;
        _localPos = localPos;
        _localRot = localRot;
        _prevLocalPos = localPos;
        _prevLocalRot = localRot;
        transform.localPosition = localPos;
        transform.localRotation = localRot;
        Physics.SyncTransforms();
        RememberParkedPose();
        MeasureGearHeight();
        _poseJumped = true;
    }

    // ── Debug (editor / cheats; the phase-2 rider spike + no-UI travel legs) ──
    void DebugKeys()
    {
        if (ClientDriven) return;
        if (!Application.isEditor && !Universe.cheatsEnabled) return;

        // F6: full leg to the next landable planet (no NAV needed).
        if (Input.GetKeyDown(KeyCode.F6) && _phase == Phase.Parked)
        {
            var planets = LandablePlanets();
            int idx = planets.IndexOf(_body);
            for (int i = 1; i <= planets.Count; i++)
            {
                var candidate = planets[(idx + i + planets.Count) % planets.Count];
                if (candidate != _body) { RequestTravel(candidate); break; }
            }
        }

        // Alt+WASD / Alt+Q/E / Alt+Space: pilot the hover without the NAV app
        // (player movement still reads plain WASD — Alt keeps them separate).
        if (_phase == Phase.Hover && Input.GetKey(KeyCode.LeftAlt))
        {
            Vector2 move = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            float yaw = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            SetPilotInput(move, yaw);
            if (Input.GetKeyDown(KeyCode.Space)) RequestLand();
        }
    }

    // Cheats/editor-only playtest overlay: names the exact gate behind any
    // "can't walk" or "can't land" moment so a bug report is one screenshot.
    float _nextOverlayBuildAt;
    string _overlayText = "";

    void OnGUI()
    {
        if (!Application.isEditor && !Universe.cheatsEnabled) return;
        if (_phase == Phase.Parked && !PlayerController.RiderMode) return;

        // Rebuild at 4 Hz, not per OnGUI call (playtest 23 GC hygiene: this
        // string build ran twice per frame in the editor for the whole
        // flight — steady garbage feeding the GC spikes the probe hunts).
        if (Time.unscaledTime < _nextOverlayBuildAt)
        {
            DrawOverlay();
            return;
        }
        _nextOverlayBuildAt = Time.unscaledTime + 0.25f;

        string valid = _sensor != null
            ? (_sensor.Valid ? "GREEN" : "RED " + _sensor.FailReason)
            : "-";
        string text =
            "SHUTTLE  phase " + _phase +
            "  vel " + Mathf.RoundToInt(_speed) + " m/s" +
            "  alt " + Mathf.RoundToInt(_hoverAlt) + " m" +
            "  landing " + valid + "\n" +
            "RIDER  grounded " + (PlayerController.DbgRiderGrounded ? "YES" : "NO") +
            "  vert " + PlayerController.DbgRiderVertVel.ToString("0.0") +
            "  walk " + PlayerController.DbgRiderWalkSpeed.ToString("0.0") + "\n" +
            "GATES  modal " + (PlayerController.isInModalSlotUI ? "ON" : "off") +
            "  uiFocus " + (TutorialGate.UISelectionActive() ? "ON" : "off") +
            "  menu " + (PauseState.MenuOpen ? "ON" : "off") +
            "  typing " + (AIChatScreen.IsTypingActive ? "ON" : "off");
        _overlayText = text;
        DrawOverlay();
    }

    void DrawOverlay()
    {
        GUI.color = Color.black;
        GUI.Label(new Rect(13f, 13f, 900f, 70f), _overlayText);
        GUI.color = Color.white;
        GUI.Label(new Rect(12f, 12f, 900f, 70f), _overlayText);
    }

    static Transform FindDeepChild(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var found = FindDeepChild(t.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
