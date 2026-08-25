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

    /// Fired on every phase transition — door, NAV, lamp, rider frame, sync and
    /// the world screen all subscribe. Never poll.
    public event System.Action<Phase> OnPhaseChanged;

    /// Fired when a launch aborts at COUNTDOWN 0 because nobody was aboard
    /// (handoff D-1). NAV shows "NO CREW ABOARD — LAUNCH CANCELLED".
    public event System.Action OnLaunchAborted;

    // ── Tuning (constants, house style — no serialized fields on a runtime-attached component) ──
    public const float CountdownSeconds = 10f;
    const float LiftoffHeight   = 300f;   // metres above the parked pose
    const float LiftoffSeconds  = 10f;
    const float TransitCruiseSpeed = 400f; // m/s used to derive duration…
    const float TransitMinSeconds  = 20f;  // …clamped into this window
    const float TransitMaxSeconds  = 40f;
    public const float HoverAltitude = 100f;
    const float HoverMaxSpeed   = 15f;    // WASD tangential speed (m/s)
    const float HoverAccel      = 8f;     // m/s² toward the input direction (heavy vehicle)
    const float HoverYawDegSec  = 40f;    // Q/E
    const float HoverAltSmooth  = 1.2f;   // SmoothDamp time for the altitude hold
    const float LandingMinSeconds = 5f;
    const float LandingMaxSeconds = 8f;
    const float SettleSeconds   = 0.5f;
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

    // Parked pose memory — what the save system captures, and what a mid-flight
    // save falls back to (you can't reach the pod mid-flight, but belt+braces).
    CelestialBody _parkedBody;
    Vector3 _parkedLocalPos;
    Quaternion _parkedLocalRot;
    float _gearHeight = 2.5f;         // shuttle-origin height above ground when parked

    // Hover state
    Vector3 _hoverVelLocal;           // tangential velocity in body-local space
    float _hoverAltVel;               // SmoothDamp ref for the altitude hold
    float _hoverAlt;                  // current held altitude above ground
    Vector2 _pilotMove;               // WASD, [-1,1] each axis
    float _pilotYaw;                  // Q/E, [-1,1]
    float _pilotInputStamp = -999f;
    bool _landRequested;

    // Landing state
    Vector3 _landStartLocal;
    Vector3 _landTargetLocal;
    float _landDuration;
    float _settleT;

    ShuttleLandingSensor _sensor;
    ShuttleRenderSmoother _smoother;
    ShuttleLandingCamera _landingCamera;
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
    /// Held altitude above ground during HOVER (the NAV feed's readout).
    public float CurrentGroundAltitude => _hoverAlt;
    /// Seconds into the current phase (ShuttleSync's heartbeat payload).
    public float PhaseElapsed => _phaseT;

    /// Guest-side: the host's replicated landing-validity bool.
    public void ApplyRemoteValid(bool valid)
    {
        if (_sensor != null) _sensor.SetRemoteValid(valid);
        if (_lamp != null && (_phase == Phase.Hover || _phase == Phase.Landing))
            _lamp.SetPhase(_phase, valid);
    }
    public ShuttleLandingCamera LandingCamera => _landingCamera;

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
    }

    void Start()
    {
        _body = GetComponentInParent<CelestialBody>();
        _localPos = transform.localPosition;
        _localRot = transform.localRotation;
        _prevLocalPos = _localPos;
        _prevLocalRot = _localRot;
        RememberParkedPose();
        MeasureGearHeight();

        _door = GetComponentInChildren<ShuttleExitDoor>(true);
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

    // Gear height = distance from the shuttle origin to the ground under it at
    // the parked pose. Used to seat the landing so the pads (not the origin)
    // touch down. Falls back to 2.5 m over a hole.
    void MeasureGearHeight()
    {
        if (_body == null) return;
        Vector3 worldPos = FrameWorldPos(_body, _localPos);
        Vector3 up = (worldPos - _body.Position).normalized;
        if (Physics.Raycast(worldPos + up * 2f, -up, out RaycastHit hit, 30f, GroundMask, QueryTriggerInteraction.Ignore))
            _gearHeight = hit.distance - 2f;
        _gearHeight = Mathf.Clamp(_gearHeight, 0.5f, 10f);
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
        if (!CanLandOn(target) || target == _body) return false;
        _targetBody = target;
        SetPhase(Phase.Countdown);
        return true;
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
                break;

            case Phase.Liftoff:
                if (_door != null) _door.CloseForFlight();
                ShuttleRiderFrame.CaptureRiders(this);
                _departBody = _body;
                _departAnchorLocal = _localPos + _localPos.normalized * LiftoffHeight;
                if (!ClientDriven) ComputeArrivalAnchor();
                break;

            case Phase.Transit:
                _reparented = false;
                float dist = Vector3.Distance(FrameWorldPos(_departBody, _departAnchorLocal),
                                              FrameWorldPos(_targetBody, _arriveAnchorLocal));
                _transitDuration = Mathf.Clamp(dist / TransitCruiseSpeed, TransitMinSeconds, TransitMaxSeconds);
                break;

            case Phase.Hover:
                _hoverVelLocal = Vector3.zero;
                _hoverAltVel = 0f;
                _hoverAlt = HoverAltitude;
                _landRequested = false;
                if (_sensor != null) _sensor.SetActive(true);
                if (_landingCamera == null) _landingCamera = ShuttleLandingCamera.Create(this);
                break;

            case Phase.Landing:
                break;

            case Phase.Parked:
                if (prev != Phase.Countdown)   // a real landing, not an abort
                {
                    RememberParkedPose();
                    ShuttleRiderFrame.ReleaseRiders(this);
                    if (_door != null) _door.ReopenAfterFlight();
                    if (_upBlendOut != null) StopCoroutine(_upBlendOut);
                }
                if (_sensor != null) _sensor.SetActive(false);
                if (_landingCamera != null) { _landingCamera.Teardown(); _landingCamera = null; }
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
        if (Physics.Raycast(origin, -dir, out RaycastHit hit, _targetBody.radius * 2f, GroundMask, QueryTriggerInteraction.Ignore))
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

        // A reparent this step leaves _prevLocal* in the OLD body's frame —
        // lerping across frames is garbage, so collapse the smoothing span.
        if (_poseJumped)
        {
            _prevLocalPos = _localPos;
            _prevLocalRot = _localRot;
        }

        if (_phase != Phase.Parked)
        {
            transform.localPosition = _localPos;
            transform.localRotation = _localRot;
            // autoSyncTransforms is off project-wide — commit the moved shuttle
            // colliders (part of the planet rb's compound) before the riders'
            // ground/wall queries this step.
            Physics.SyncTransforms();
        }

        if (_lamp != null && (_phase == Phase.Hover || _phase == Phase.Landing))
            _lamp.SetPhase(_phase, LandingValid);
    }

    void TickCountdown()
    {
        if (_phaseT < CountdownSeconds) return;

        // D-1: leave with whoever is inside; abort (only) if NOBODY is.
        if (!ShuttleRiderFrame.AnyoneInside(this))
        {
            SetPhase(Phase.Parked);
            OnLaunchAborted?.Invoke();
            return;
        }
        SetPhase(Phase.Liftoff);
    }

    void TickLiftoff()
    {
        float u = Mathf.Clamp01(_phaseT / LiftoffSeconds);
        // Smooth vertical rise: 0 -> LiftoffHeight, at rest at both ends.
        float alt = Mathf.SmoothStep(0f, LiftoffHeight, u);
        Vector3 localUp = _parkedLocalPos.sqrMagnitude > 0.001f ? _parkedLocalPos.normalized : Vector3.up;
        _localPos = _parkedLocalPos + localUp * alt;
        if (u >= 1f) SetPhase(Phase.Transit);
    }

    void TickTransit()
    {
        float u = Mathf.Clamp01(_phaseT / _transitDuration);
        float ease = u * u * (3f - 2f * u);   // smoothstep: velocity-continuous at both seams

        // Both anchors evaluated FRESH every step so the path tracks the
        // orbiting bodies and origin rebases (handoff §4 — never cache world).
        Vector3 aWorld = FrameWorldPos(_departBody, _departAnchorLocal);
        Vector3 bWorld = FrameWorldPos(_targetBody, _arriveAnchorLocal);
        Vector3 posW = Vector3.Lerp(aWorld, bWorld, ease);

        Vector3 upA = (aWorld - _departBody.Position).normalized;
        Vector3 upB = (bWorld - _targetBody.Position).normalized;
        Vector3 upW = Vector3.Slerp(upA, upB, ease).normalized;

        // Reparent to the arrival frame at the midpoint. Local pose is
        // recomputed from the same world pose, so nothing moves.
        if (!_reparented && ease >= 0.5f)
        {
            _reparented = true;
            transform.SetParent(_targetBody.transform, false);
            _body = _targetBody;
            _poseJumped = true;
        }

        Quaternion curW = FrameWorldRot(_body, _localRot);
        Quaternion rotW = Quaternion.FromToRotation(curW * Vector3.up, upW) * curW;

        _localPos = FrameLocalPos(_body, posW);
        _localRot = FrameLocalRot(_body, rotW);

        if (u >= 1f) SetPhase(Phase.Hover);
    }

    void TickHover()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 worldPos = WorldPos;
        Vector3 up = (worldPos - _body.Position).normalized;

        // Pilot input decays to zero on silence (guest pilot dropped, NAV closed).
        Vector2 move = Time.unscaledTime - _pilotInputStamp <= PilotInputStaleSeconds ? _pilotMove : Vector2.zero;
        float yawIn = Time.unscaledTime - _pilotInputStamp <= PilotInputStaleSeconds ? _pilotYaw : 0f;

        // Upright: shuttle up = radial out, yaw preserved (incremental).
        Quaternion curW = FrameWorldRot(_body, _localRot);
        Quaternion uprightW = Quaternion.FromToRotation(curW * Vector3.up, up) * curW;
        if (Mathf.Abs(yawIn) > 0.001f)
            uprightW = Quaternion.AngleAxis(yawIn * HoverYawDegSec * dt, up) * uprightW;

        // Tangential move relative to shuttle yaw, heavy-vehicle smoothing.
        Vector3 fwd = Vector3.ProjectOnPlane(uprightW * Vector3.forward, up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(uprightW * Vector3.right, up).normalized;
        Vector3 right = Vector3.Cross(up, fwd);
        Vector3 wishVel = (fwd * move.y + right * move.x) * HoverMaxSpeed;
        Vector3 curVel = FrameWorldRot(_body, Quaternion.identity) * _hoverVelLocal;
        curVel = Vector3.MoveTowards(curVel, wishVel, HoverAccel * dt);
        _hoverVelLocal = Quaternion.Inverse(FrameWorldRot(_body, Quaternion.identity)) * curVel;

        worldPos += curVel * dt;

        // Altitude hold: ray straight down on the ground mask; hold the last
        // altitude over a hole.
        float groundDist;
        if (Physics.Raycast(worldPos + up * 5f, -up, out RaycastHit hit, 400f, GroundMask, QueryTriggerInteraction.Ignore))
            groundDist = hit.distance - 5f;
        else
            groundDist = _hoverAlt;
        _hoverAlt = Mathf.SmoothDamp(groundDist, HoverAltitude, ref _hoverAltVel, HoverAltSmooth, Mathf.Infinity, dt);
        worldPos += up * (_hoverAlt - groundDist);

        _localPos = FrameLocalPos(_body, worldPos);
        _localRot = FrameLocalRot(_body, uprightW);
    }

    void BeginLanding()
    {
        Vector3 worldPos = WorldPos;
        Vector3 up = UpFromBody;
        if (!Physics.Raycast(worldPos + up * 5f, -up, out RaycastHit hit, 400f, GroundMask, QueryTriggerInteraction.Ignore))
            return;   // no ground below — refuse (validity should already be red)
        _landStartLocal = _localPos;
        _landTargetLocal = FrameLocalPos(_body, hit.point + up * _gearHeight);
        float height = Vector3.Distance(_landStartLocal, _landTargetLocal);
        _landDuration = Mathf.Clamp(height / 15f, LandingMinSeconds, LandingMaxSeconds);
        _settleT = 0f;
        SetPhase(Phase.Landing);
    }

    void TickLanding()
    {
        float u = Mathf.Clamp01(_phaseT / _landDuration);
        float ease = u * u * (3f - 2f * u);   // at rest at both ends — a soft set-down
        _localPos = Vector3.Lerp(_landStartLocal, _landTargetLocal, ease);

        // Stay upright through the descent.
        Vector3 up = UpFromBody;
        Quaternion curW = FrameWorldRot(_body, _localRot);
        Quaternion uprightW = Quaternion.FromToRotation(curW * Vector3.up, up) * curW;
        _localRot = FrameLocalRot(_body, uprightW);

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

    IEnumerator BlendUpOverrideOut(float seconds)
    {
        var pc = FindObjectOfType<PlayerController>();
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
        if (PlayerController.UpOverrideTransform == proxy)
            PlayerController.UpOverrideTransform = null;
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
