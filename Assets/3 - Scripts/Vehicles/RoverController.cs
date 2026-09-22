using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rover / ATV (2026-09-22, Sam's build-off brief): four big wheels on a
/// springy raycast suspension, two seats, a truck bed, drives anywhere on a
/// planet, floats on the ocean (the wheels are the flotation), and Space
/// loads the suspension for a hop — hold it to squat all the way down and
/// jump higher.
///
/// ONE Rigidbody, no WheelColliders (they assume a world "down"; every planet
/// here has its own). Each wheel is a SphereCast from a hardpoint along the
/// rover's own -up: spring + damper on the measured length, tyre grip at the
/// contact point, all velocities taken RELATIVE TO THE PLANET (the planets
/// ride orbital rails at ~85 m/s, so "at rest on the ground" is not zero
/// velocity). Gravity is the anchor-frame rule PlayerController uses (anchor
/// body's gravity + its frameAcceleration — never the raw Sun pull, see the
/// anchor-frame note in PlayerController.FixedUpdate).
///
/// Seating mirrors Ship.PilotShip / DroneController.Enter: the player
/// GameObject is switched OFF, the real camera is parented to the driver's
/// head anchor (never a second camera — that loses the atmosphere/ocean post
/// stack), and on exit the player is put down beside the driver's door with
/// the rover's velocity (= the planet's), the same way Ship.StopPilotingShip
/// does it.
///
/// Built by Tools ▸ Solar System ▸ Rover ▸ Build Rover Prefab (RoverBuilder).
/// </summary>
public class RoverController : MonoBehaviour
{
    /// The rover currently being driven (null = nobody driving any rover).
    public static RoverController Active { get; private set; }
    public static bool IsDriving => Active != null;

    // Wheel order everywhere: 0 = front-left, 1 = front-right, 2 = rear-left, 3 = rear-right.
    public const int WheelCount = 4;

    [Header("Wiring (set by RoverBuilder)")]
    [Tooltip("Top of each wheel's suspension travel, rover-local. FL, FR, RL, RR.")]
    public Transform[] wheelHardpoints = new Transform[WheelCount];
    [Tooltip("Visual pivot per wheel (steer yaw + spin are applied here; the tyre mesh is its child).")]
    public Transform[] wheelVisuals = new Transform[WheelCount];
    [Tooltip("Where the camera sits while driving (driver's eyes).")]
    public Transform camViewPoint;
    [Tooltip("Where the player is put down when they get out.")]
    public Transform exitPoint;
    [Tooltip("The seat interactable (its in-zone flag is re-armed on exit so the prompt comes straight back).")]
    public RoverSeat seat;
    [Tooltip("Lights that only run while someone is driving.")]
    public Light[] headlights;

    [Header("Wheels + suspension")]
    public float wheelRadius = 0.55f;
    [Tooltip("Suspension length (hardpoint → wheel centre) with no load on it.")]
    public float restLength = 0.5f;
    [Tooltip("Longest the suspension can droop to (wheel hunting for ground).")]
    public float maxLength = 1.05f;
    [Tooltip("Shortest it compresses to before the bump stop takes over.")]
    public float minLength = 0.16f;
    [Tooltip("Spring natural frequency, Hz. Lower = softer, floatier. 1.3-1.6 is an ATV.")]
    public float springFrequencyHz = 1.15f;
    [Tooltip("Damping ratio. 1 = no overshoot. 0.3 = bouncy, settles in ~2 bounces.")]
    [Range(0.05f, 1.5f)] public float dampingRatio = 0.42f;
    [Tooltip("Bump stop stiffness, as a multiple of the spring.")]
    public float bumpStopMultiplier = 6f;

    [Header("Drive")]
    [Tooltip("Forward acceleration on flat ground, m/s² (all wheels).")]
    public float driveAccel = 11f;
    public float maxSpeed = 20f;
    public float reverseMaxSpeed = 8f;
    [Tooltip("Braking (S while rolling forward), m/s².")]
    public float brakeAccel = 16f;
    [Tooltip("How fast it coasts to a stop with no throttle, 1/s.")]
    public float rollingDrag = 0.35f;
    [Tooltip("Parking brake strength when nobody is driving, 1/s.")]
    public float parkingBrake = 4f;
    [Tooltip("Steering lock at a standstill, degrees.")]
    public float maxSteerDeg = 32f;
    [Tooltip("Steering lock at top speed, as a fraction of the standstill lock (stops it flipping).")]
    [Range(0.1f, 1f)] public float steerAtSpeedFraction = 0.4f;
    [Tooltip("How quickly the wheels turn toward the stick, deg/s.")]
    public float steerRateDeg = 140f;

    [Header("Grip")]
    [Tooltip("Sideways slip → force, 1/s. Higher = more on rails.")]
    public float lateralStiffness = 14f;
    [Tooltip("Friction circle: max tyre force as a multiple of the wheel's normal load.")]
    public float tyreFriction = 1.5f;

    [Header("Air + righting")]
    [Tooltip("Torque toward gravity-up while airborne (so it lands on its wheels).")]
    public float airRightingStrength = 5f;
    public float airRightingDamping = 1.2f;
    [Tooltip("If it ends up on its side/roof for this long, it rights itself.")]
    public float selfRightAfterSeconds = 1.5f;

    [Header("Water")]
    [Tooltip("Buoyancy per fully-submerged wheel as a multiple of the wheel's share of weight. >1 floats.")]
    public float wheelBuoyancy = 1.7f;
    [Tooltip("Sideways water drag, 1/s (scaled by how submerged).")]
    public float waterDrag = 0.55f;
    [Tooltip("Up/down water drag, 1/s (kills bobbing).")]
    public float waterVerticalDrag = 3.0f;
    [Tooltip("Paddle-wheel thrust while afloat, m/s².")]
    public float waterAccel = 9f;
    public float waterMaxSpeed = 14f;
    [Tooltip("Yaw authority while afloat, rad/s².")]
    public float waterYawAccel = 1.4f;

    [Header("Jump")]
    [Tooltip("Seconds of holding Space to reach a full charge.")]
    public float chargeSeconds = 0.9f;
    [Tooltip("How far the suspension squats at full charge, metres.")]
    public float squatDepth = 0.28f;
    [Tooltip("Launch speed for a tap, m/s.")]
    public float jumpMinSpeed = 4.5f;
    [Tooltip("Launch speed at full charge, m/s.")]
    public float jumpMaxSpeed = 10f;

    [Header("Camera")]
    [Tooltip("0 = the view rolls/pitches with the chassis, 1 = fully stabilised to gravity.")]
    [Range(0f, 1f)] public float viewStabilise = 0.55f;
    public float lookYawLimit = 150f;
    public float lookPitchMin = -55f;
    public float lookPitchMax = 70f;
    public float thirdPersonDistance = 6f;
    public float thirdPersonHeight = 1.6f;

    [Header("Engine sound")]
    [Range(0f, 1f)] public float engineVolume = 0.28f;

    [Header("Weight + damping — appended 2026-09-22")]
    [Tooltip("Extra push into the ground while any wheel touches, as a fraction of gravity — the 'heavy and planted' feel; more normal load = more grip.")]
    public float groundedDownforce = 0.45f;
    [Tooltip("Damping multiplier while a wheel EXTENDS (rebound). >1 stops the rover pogoing off bumps.")]
    public float reboundDampingScale = 1.5f;
    [Tooltip("Damping multiplier while a wheel COMPRESSES.")]
    public float compressionDampingScale = 0.8f;

    // ── runtime ─────────────────────────────────────────────────────────────
    Rigidbody _rb;
    PlayerController _driver;
    Camera _cam;
    CelestialBody _anchor;
    CelestialBodyGenerator _anchorGen;
    float _anchorOceanR;             // 0 = no ocean on the anchor
    bool _anchorOceanKnown;
    EndlessManager _endless;
    // Per-wheel state (physics frame)
    readonly float[] _length = new float[WheelCount];        // current suspension length
    readonly bool[] _grounded = new bool[WheelCount];
    readonly float[] _normalForce = new float[WheelCount];
    readonly float[] _spinDeg = new float[WheelCount];
    readonly float[] _spinRate = new float[WheelCount];      // deg/s, render
    readonly float[] _submerged = new float[WheelCount];     // 0..1
    readonly RaycastHit[] _hits = new RaycastHit[8];
    float _steerDeg;                 // current front wheel angle
    float _throttle, _steerInput;
    int _groundedCount;
    bool _afloat;
    Vector3 _gravUp = Vector3.up;
    float _gravMag = 9f;
    Vector3 _groundVel;
    // Jump
    float _charge;
    bool _charging;
    float _jumpCooldown;
    // Righting
    float _tippedTime;
    // Spawn settle
    bool _settled;
    Vector3 _spawnLocal;
    bool _spawnLocalValid;
    CelestialBody _spawnAnchor;      // the body _spawnLocal is measured on (fixed for the whole settle)
    float _settleDeadline;
    int _boardFrame = -1;
    // Planet-local pose from the last physics step: the save loader teleports
    // every planet to its saved orbit position one frame after the scene
    // starts (SaveLoadRunner), and a warp does the same — a settled rover
    // would be left floating where the village USED to be. An impossible
    // one-step jump in planet-local position is how we know, and the last
    // local pose is where we put it back.
    Vector3 _lastLocalPos;
    Quaternion _lastLocalRot;
    bool _hasLastLocal;
    const float kTeleportJump = 25f;   // metres per step; driving moves < 0.5
    // Camera
    float _lookYaw, _lookPitch;
    bool _thirdPerson;
    Transform _camRig;
    // Engine
    AudioSource _engine;
    float _enginePitch = 0.6f;

    // Cast mask: everything solid except the layers that are never ground.
    int _groundMask;
    // Interactables scan cache (board/exit only)
    static readonly List<Collider> s_ownColliders = new List<Collider>();
    HashSet<Collider> _own = new HashSet<Collider>();

    public bool Settled => _settled;
    public float SpeedKmh => _rb != null ? Vector3.Dot(_rb.velocity - _groundVel, transform.forward) * 3.6f : 0f;
    public bool Occupied => _driver != null;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.angularDrag = 0.6f;
        _rb.drag = 0f;
        _rb.isKinematic = true;          // parked kinematic until the ground snap finds real terrain
        _rb.maxAngularVelocity = 12f;
        if (_rb.mass < 100f) _rb.mass = 1500f;
        // Low centre of mass (near the axles) so grip forces at the contact
        // patch can't roll it in a corner; the wide track does the rest.
        _rb.centerOfMass = new Vector3(0f, -0.35f, 0f);

        _groundMask = ~((1 << 2) | (1 << 4) | (1 << 5) | (1 << 8) | (1 << 11) | (1 << 12) | (1 << 13) | (1 << 14) | (1 << 15) | (1 << 1));
        GetComponentsInChildren(true, s_ownColliders);
        _own = new HashSet<Collider>(s_ownColliders);

        for (int i = 0; i < WheelCount; i++) _length[i] = restLength;

        SetHeadlights(false);
        BuildEngineAudio();
        _settleDeadline = Time.time + 8f;
    }

    void Start()
    {
        _endless = FindObjectOfType<EndlessManager>();
        if (_endless != null) _endless.RegisterPhysicsObject(transform);
        ElectAnchor();
        RememberSpawnLocal();
    }

    void RememberSpawnLocal()
    {
        if (_anchor == null) return;
        _spawnAnchor = _anchor;
        _spawnLocal = _anchor.transform.InverseTransformPoint(_rb.position);
        _spawnLocalValid = true;
    }

    void RecordLocalPose()
    {
        if (_anchor == null) { _hasLastLocal = false; return; }
        _lastLocalPos = _anchor.transform.InverseTransformPoint(_rb.position);
        _lastLocalRot = Quaternion.Inverse(_anchor.transform.rotation) * _rb.rotation;
        _hasLastLocal = true;
    }

    /// The planet moved under us in one step: put the rover back at its last
    /// planet-local pose on that same planet and re-settle onto the terrain.
    void RelocateToLastLocal()
    {
        var a = _anchor;
        Vector3 pos = a.transform.TransformPoint(_lastLocalPos);
        Quaternion rot = a.transform.rotation * _lastLocalRot;
        _rb.isKinematic = true;
        _rb.position = pos;
        _rb.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
        _spawnAnchor = a;
        _spawnLocal = _lastLocalPos;
        _spawnLocalValid = true;
        _settled = false;
        _hasLastLocal = false;
        _settleDeadline = Time.time + 3f;
        Physics.SyncTransforms();
        Debug.Log("[Rover] " + a.name + " moved under me (save load / warp) — re-seated at my planet-local spot");
        Settle(false);
    }

    void OnDestroy()
    {
        if (_driver != null) Exit();
        if (Active == this) Active = null;
        if (_endless != null) _endless.UnregisterPhysicsObject(transform);
    }

    // ── Board / exit (Ship.PilotShip recipe) ────────────────────────────────

    public void Board(PlayerController pc)
    {
        if (pc == null || _driver != null || Active != null) return;
        _driver = pc;
        Active = this;
        _cam = pc.Camera;
        _lookYaw = 0f; _lookPitch = 8f;
        _thirdPerson = false;
        _charge = 0f; _charging = false;
        _boardFrame = Time.frameCount;   // the F that seated us must not also read as "get out"

        if (_camRig == null)
        {
            var rig = new GameObject("RoverCameraRig");
            _camRig = rig.transform;
            _camRig.SetParent(transform, false);
        }
        Transform mount = camViewPoint != null ? camViewPoint : transform;
        _camRig.position = mount.position;
        _camRig.rotation = transform.rotation;
        _cam.transform.parent = _camRig;
        _cam.transform.localPosition = Vector3.zero;
        _cam.transform.localRotation = Quaternion.identity;
        pc.gameObject.SetActive(false);

        // Disabling the player never fires OnTriggerExit — every Interactable
        // we were standing in would keep its in-zone flag (Ship.PilotShip's
        // documented trap). Clear them all; the seat is re-armed on exit.
        var all = FindObjectsOfType<Interactable>(true);
        for (int i = 0; i < all.Length; i++) if (all[i] != null) all[i].ClearPlayerInInteractionZone();

        if (!_settled) Settle(true);
        _rb.isKinematic = false;
        SetHeadlights(true);
        Debug.Log("[Rover] boarded");
    }

    public void Exit()
    {
        if (_driver == null) return;
        var pc = _driver;
        _driver = null;
        if (Active == this) Active = null;
        SetHeadlights(false);
        _throttle = 0f; _steerInput = 0f; _charging = false; _charge = 0f;

        Transform ep = exitPoint != null ? exitPoint : transform;
        Vector3 pos = ep.position;
        // Put the feet on the ground beside the door if there is ground there.
        Vector3 up = _gravUp;
        if (Physics.SphereCast(pos + up * 1.5f, 0.3f, -up, out RaycastHit hit, 3.5f, _groundMask, QueryTriggerInteraction.Ignore)
            && !_own.Contains(hit.collider))
            pos = hit.point + up * 1.05f;
        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, up).normalized, up);

        pc.transform.SetPositionAndRotation(pos, rot);
        pc.gameObject.SetActive(true);
        var prb = pc.Rigidbody;
        if (prb != null)
        {
            prb.position = pos;
            prb.rotation = rot;
            prb.velocity = _rb.velocity;      // the planet's orbital velocity rides along, exactly like Ship
            prb.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
        pc.ExitFromSpaceship();
        pc.SnapOrientationOnExitPilot(null);
        if (_anchor != null) pc.SetReferenceBodyOnRelease(_anchor);
        pc.ForceGroundedOnRelease();
        if (CameraEffectsManager.Instance != null && CameraEffectsManager.Instance.TransformFX != null)
            CameraEffectsManager.Instance.TransformFX.SnapToCurrentPlayer();
        // Standing right beside the seat: OnTriggerEnter won't re-fire for a
        // collider that was re-enabled inside the zone, so arm it by hand.
        if (seat != null) seat.ForcePlayerInInteractionZone();
        _cam = null;
        Debug.Log("[Rover] exited");
    }

    void SetHeadlights(bool on)
    {
        if (headlights == null) return;
        for (int i = 0; i < headlights.Length; i++) if (headlights[i] != null) headlights[i].enabled = on;
    }

    // ── Per frame: input, camera, visuals ───────────────────────────────────

    void Update()
    {
        float dt = Time.deltaTime;

        if (_driver != null)
        {
            bool menu = PauseState.MenuOpen || TutorialGate.MovementInputSuppressed;
            if (!menu)
            {
                _throttle = TutorialGate.MoveAxisVertical(TutorialAbility.Move);
                _steerInput = TutorialGate.MoveAxisHorizontal(TutorialAbility.Move);

                // On the wheels: Space charges the suspension, release pops it.
                bool held = TutorialGate.JumpHeld(TutorialAbility.Jump);
                bool onWheels = _groundedCount >= 2 || _afloat;
                if (onWheels)
                {
                    if (held && _jumpCooldown <= 0f)
                    {
                        _charging = true;
                        _charge = Mathf.Min(1f, _charge + dt / Mathf.Max(0.1f, chargeSeconds));
                    }
                    else if (_charging && !held)
                    {
                        _jumpRequested = true;   // consumed in FixedUpdate
                        _charging = false;
                    }
                }
                else { _charging = false; _charge = 0f; }

                bool exitPressed = Input.GetKeyDown(KeyCode.F) || TutorialGate.PadPressed(TutorialGate.PadButton.X);
                if (exitPressed && Time.frameCount != _boardFrame) { Exit(); return; }
                if (Input.GetKeyDown(KeyCode.V)) _thirdPerson = !_thirdPerson;

                // Look: same sensitivity maths as PlayerController.
                var settings = InputSettings.Active;
                float sens = settings != null ? settings.mouseSensitivity / 10f : 0.3f;
                _lookYaw += Input.GetAxisRaw("Mouse X") * sens;
                _lookPitch -= Input.GetAxisRaw("Mouse Y") * sens;
                if (TutorialGate.ControllerEnabled)
                {
                    float gain = TutorialGate.StickLookSensitivity * 360f * Time.unscaledDeltaTime;
                    _lookYaw += TutorialGate.RightStickX() * gain;
                    _lookPitch -= TutorialGate.RightStickY() * gain * (TutorialGate.InvertLookY ? -1f : 1f);
                }
                _lookYaw = Mathf.Clamp(_lookYaw, -lookYawLimit, lookYawLimit);
                _lookPitch = Mathf.Clamp(_lookPitch, lookPitchMin, lookPitchMax);
            }
            else
            {
                _throttle = 0f; _steerInput = 0f;
            }
        }
        else
        {
            _throttle = 0f; _steerInput = 0f; _charging = false; _charge = 0f;
            // Dev: Home summons the rover to the player's feet (Universe.cheatsEnabled).
            if (Universe.cheatsEnabled && Input.GetKeyDown(KeyCode.Home) && !IsDriving) SummonToPlayer();
        }
        if (_jumpCooldown > 0f) _jumpCooldown -= dt;

        UpdateWheelVisuals(dt);
        UpdateEngineAudio(dt);
    }

    bool _jumpRequested;

    void LateUpdate()
    {
        if (_driver == null || _cam == null || _camRig == null) return;
        // Stabilised head: blend the chassis attitude toward a gravity-level
        // frame so bumps don't throw the whole view around.
        Transform mount = camViewPoint != null ? camViewPoint : transform;
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, _gravUp);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.up, _gravUp);
        Quaternion level = Quaternion.LookRotation(fwd.normalized, _gravUp);
        Quaternion rigRot = Quaternion.Slerp(transform.rotation, level, viewStabilise);
        _camRig.SetPositionAndRotation(mount.position, rigRot);

        Quaternion look = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
        if (!_thirdPerson)
        {
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = look;
        }
        else
        {
            Vector3 pivot = transform.position + _gravUp * thirdPersonHeight;
            Quaternion camRot = rigRot * Quaternion.Euler(Mathf.Max(_lookPitch, -20f) + 10f, _lookYaw, 0f);
            Vector3 back = camRot * Vector3.back;
            float dist = thirdPersonDistance;
            if (Physics.SphereCast(pivot, 0.3f, back, out RaycastHit hit, dist, _groundMask, QueryTriggerInteraction.Ignore)
                && !_own.Contains(hit.collider))
                dist = Mathf.Max(1.2f, hit.distance - 0.1f);
            _cam.transform.SetPositionAndRotation(pivot + back * dist, camRot);
        }
    }

    // ── Physics ─────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Planet teleport check FIRST, against last step's anchor — after the
        // planet has jumped away, re-electing would pick whatever body is now
        // nearest to the stranded rover and lose the planet we belong to.
        if (_settled && _anchor != null && _hasLastLocal)
        {
            Vector3 localNow = _anchor.transform.InverseTransformPoint(_rb.position);
            if ((localNow - _lastLocalPos).sqrMagnitude > kTeleportJump * kTeleportJump)
            {
                RelocateToLastLocal();
                return;
            }
        }

        ElectAnchor();
        UpdateGravityFrame();

        if (!_settled)
        {
            // Not on real terrain yet (the planet mesh is generated at runtime
            // and can sit metres off the editor's placeholder sphere). Ride the
            // planet kinematically at the authored planet-local spot and drop
            // onto the ground the moment a cast finds it. The spot is measured
            // on _spawnAnchor, never on whatever ElectAnchor says this step.
            if (!_spawnLocalValid) RememberSpawnLocal();
            else if (_spawnAnchor != null) _rb.position = _spawnAnchor.transform.TransformPoint(_spawnLocal);
            Settle(Time.time > _settleDeadline);
            return;
        }
        if (_rb.isKinematic) return;

        // Gravity: anchor frame (see header) + static attractors.
        if (_anchor != null)
        {
            _rb.AddForce(Universe.GravityAcceleration(_rb.position, _anchor) + _anchor.frameAcceleration, ForceMode.Acceleration);
            var bodies = NBodySimulation.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null || b == _anchor || !b.isStaticAttractor) continue;
                _rb.AddForce(Universe.GravityAcceleration(_rb.position, b), ForceMode.Acceleration);
            }
        }

        float mass = _rb.mass;
        float mw = mass / WheelCount;
        float omega = 2f * Mathf.PI * Mathf.Max(0.2f, springFrequencyHz);
        float k = mw * omega * omega;
        float c = 2f * dampingRatio * Mathf.Sqrt(k * mw);
        float rest = restLength - squatDepth * (_charging ? _charge : 0f);
        Vector3 up = transform.up;
        Vector3 vRel = _rb.velocity - _groundVel;
        float fwdSpeed = Vector3.Dot(vRel, transform.forward);

        // Steering: smoothed, less lock at speed.
        float speedT = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / Mathf.Max(1f, maxSpeed));
        float lock_ = maxSteerDeg * Mathf.Lerp(1f, steerAtSpeedFraction, speedT);
        _steerDeg = Mathf.MoveTowards(_steerDeg, _steerInput * lock_, steerRateDeg * dt);

        _groundedCount = 0;
        float castR = wheelRadius * 0.92f;
        for (int i = 0; i < WheelCount; i++)
        {
            var hp = wheelHardpoints[i];
            if (hp == null) { _grounded[i] = false; continue; }
            Vector3 origin = hp.position + up * castR;   // sphere bottom starts at the hardpoint
            float maxDist = maxLength + castR;
            bool hit = CastWheel(origin, -up, castR, maxDist, out RaycastHit h);
            float len = hit ? Mathf.Max(minLength * 0.5f, h.distance - castR) : maxLength;
            _grounded[i] = hit;
            _normalForce[i] = 0f;
            _length[i] = len;
            if (!hit) { _spinRate[i] = Mathf.Lerp(_spinRate[i], 0f, 0.02f); continue; }
            _groundedCount++;

            // Spring + damper along the rover's up, at the hardpoint.
            Vector3 contact = h.point;
            Vector3 vPoint = _rb.GetPointVelocity(hp.position) - _groundVel;
            float vAlong = Vector3.Dot(vPoint, up);
            float compression = rest - len;
            // Rebound (extending, vAlong > 0) is damped harder than compression:
            // that is what stops a bump launching the rover and losing traction.
            float cEff = c * (vAlong > 0f ? reboundDampingScale : compressionDampingScale);
            float f = k * compression - cEff * vAlong;
            if (len < minLength) f += k * bumpStopMultiplier * (minLength - len);
            if (f < 0f) f = 0f;
            _normalForce[i] = f;
            _rb.AddForceAtPosition(up * f, hp.position, ForceMode.Force);

            // Tyre frame on the ground plane.
            Vector3 n = h.normal;
            float steer = i < 2 ? _steerDeg : 0f;
            Vector3 wheelFwd = Quaternion.AngleAxis(steer, up) * transform.forward;
            wheelFwd = Vector3.ProjectOnPlane(wheelFwd, n);
            if (wheelFwd.sqrMagnitude < 1e-4f) continue;
            wheelFwd.Normalize();
            Vector3 wheelRight = Vector3.Cross(n, wheelFwd).normalized;

            Vector3 vc = _rb.GetPointVelocity(contact) - _groundVel;
            float vLat = Vector3.Dot(vc, wheelRight);
            float vLong = Vector3.Dot(vc, wheelFwd);
            _spinRate[i] = vLong / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg;

            // Lateral: kill slip, up to the friction circle.
            float fLat = -vLat * lateralStiffness * mw;
            // Longitudinal: drive / brake / roll.
            float fLong = 0f;
            if (_driver != null && Mathf.Abs(_throttle) > 0.05f)
            {
                bool braking = (_throttle > 0f && vLong < -0.4f) || (_throttle < 0f && vLong > 0.4f);
                if (braking) fLong = -Mathf.Sign(vLong) * brakeAccel * mw;
                else
                {
                    float cap = _throttle > 0f ? maxSpeed : reverseMaxSpeed;
                    float headroom = Mathf.Clamp01(1f - Mathf.Abs(vLong) / cap);
                    fLong = _throttle * driveAccel * mass / Mathf.Max(1, _groundedCount) * Mathf.Sqrt(headroom);
                }
            }
            else
            {
                float drag = _driver != null ? rollingDrag : parkingBrake;
                fLong = -vLong * drag * mw;
            }
            Vector3 tyre = wheelRight * fLat + wheelFwd * fLong;
            float maxF = tyreFriction * Mathf.Max(f, 0.15f * mw * _gravMag);
            if (tyre.sqrMagnitude > maxF * maxF) tyre = tyre.normalized * maxF;
            _rb.AddForceAtPosition(tyre, contact, ForceMode.Force);
        }

        // Planted: while any wheel touches, lean on the ground a bit harder than
        // gravity alone. Heavier feel, more normal load, more grip.
        if (_groundedCount > 0 && groundedDownforce > 0f)
            _rb.AddForce(-_gravUp * (_gravMag * groundedDownforce), ForceMode.Acceleration);

        UpdateWater(dt, mw);

        // Airborne: turn toward gravity-up so it lands on its wheels.
        bool airborne = _groundedCount < 2 && !_afloat;
        float tilt = Vector3.Dot(up, _gravUp);
        if (tilt < 0.35f) _tippedTime += dt; else _tippedTime = 0f;
        if (airborne || _afloat || _tippedTime > selfRightAfterSeconds)
        {
            float strength = _tippedTime > selfRightAfterSeconds ? airRightingStrength * 1.6f : (_afloat ? airRightingStrength * 0.8f : airRightingStrength);
            Vector3 axis = Vector3.Cross(up, _gravUp);
            float ang = Mathf.Asin(Mathf.Clamp(axis.magnitude, 0f, 1f));
            if (tilt < 0f) ang = Mathf.PI - ang;
            if (axis.sqrMagnitude > 1e-6f)
                _rb.AddTorque(axis.normalized * (ang * strength) - _rb.angularVelocity * airRightingDamping, ForceMode.Acceleration);
        }

        // Jump: pop the loaded suspension.
        if (_jumpRequested)
        {
            _jumpRequested = false;
            if ((_groundedCount >= 2 || _afloat) && _jumpCooldown <= 0f)
            {
                float v = Mathf.Lerp(jumpMinSpeed, jumpMaxSpeed, _charge);
                Vector3 dir = Vector3.Slerp(up, _gravUp, 0.5f);
                _rb.AddForce(dir * v, ForceMode.VelocityChange);
                _jumpCooldown = 0.35f;
            }
            _charge = 0f;
        }

        RecordLocalPose();
    }

    bool CastWheel(Vector3 origin, Vector3 dir, float radius, float maxDist, out RaycastHit best)
    {
        int n = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, maxDist, _groundMask, QueryTriggerInteraction.Ignore);
        best = default;
        float bd = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i];
            if (h.collider == null || _own.Contains(h.collider)) continue;
            if (h.distance <= 0f && h.point == Vector3.zero) continue;     // started inside → junk hit
            if (h.distance < bd) { bd = h.distance; best = h; }
        }
        return bd < float.MaxValue;
    }

    void UpdateWater(float dt, float mw)
    {
        _afloat = false;
        if (_anchor == null || _anchorOceanR <= 0f) { for (int i = 0; i < WheelCount; i++) _submerged[i] = 0f; return; }
        if (CaveVolume.IsInsideAnyCave(_rb.position)) { for (int i = 0; i < WheelCount; i++) _submerged[i] = 0f; return; }

        Vector3 up = transform.up;
        Vector3 vRel = _rb.velocity - _groundVel;
        float maxS = 0f;
        for (int i = 0; i < WheelCount; i++)
        {
            var hp = wheelHardpoints[i];
            if (hp == null) { _submerged[i] = 0f; continue; }
            Vector3 centre = hp.position - up * _length[i];
            float depth = _anchorOceanR - (centre - _anchor.Position).magnitude;   // + = centre below the surface
            float s = Mathf.Clamp01((depth + wheelRadius) / (2f * wheelRadius));
            _submerged[i] = s;
            if (s <= 0f) continue;
            maxS = Mathf.Max(maxS, s);
            // Buoyancy along gravity-up at the wheel, drag against the water.
            Vector3 buoy = _gravUp * (s * wheelBuoyancy * _gravMag * mw);
            Vector3 vp = _rb.GetPointVelocity(centre) - _groundVel;
            Vector3 vUp = _gravUp * Vector3.Dot(vp, _gravUp);
            Vector3 vLat = vp - vUp;
            Vector3 drag = -(vLat * waterDrag + vUp * waterVerticalDrag) * (s * mw);
            _rb.AddForceAtPosition(buoy + drag, centre, ForceMode.Force);
        }
        if (maxS < 0.25f) return;
        _afloat = _groundedCount == 0 || maxS > 0.6f;
        // Slow the tumbling in water.
        _rb.AddTorque(-_rb.angularVelocity * (1.5f * maxS), ForceMode.Acceleration);
        if (!_afloat || _driver == null) return;

        // Paddle-wheel drive + rudder while afloat.
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, _gravUp).normalized;
        float fs = Vector3.Dot(vRel, fwd);
        float cap = _throttle > 0f ? waterMaxSpeed : waterMaxSpeed * 0.5f;
        float headroom = Mathf.Clamp01(1f - Mathf.Abs(fs) / cap);
        _rb.AddForce(fwd * (_throttle * waterAccel * headroom * maxS), ForceMode.Acceleration);
        float yawDir = fs < -0.5f ? -1f : 1f;
        _rb.AddTorque(_gravUp * (_steerInput * waterYawAccel * yawDir * maxS), ForceMode.Acceleration);
        for (int i = 0; i < WheelCount; i++)
            _spinRate[i] = Mathf.Lerp(_spinRate[i], _throttle * 400f, 0.05f);
    }

    // ── Anchor + gravity frame ──────────────────────────────────────────────

    void ElectAnchor()
    {
        var bodies = NBodySimulation.Bodies;
        CelestialBody best = null;
        float bestDst = float.MaxValue;
        Vector3 p = _rb != null ? _rb.position : transform.position;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.Position - p).magnitude - b.radius;
            if (d < bestDst) { bestDst = d; best = b; }
        }
        if (best != _anchor)
        {
            _anchor = best;
            _anchorGen = null;
            _anchorOceanKnown = false;
            _anchorOceanR = 0f;
            _hasLastLocal = false;   // local pose was measured on the old body
        }
        if (_anchor != null && !_anchorOceanKnown)
        {
            _anchorOceanKnown = true;
            try
            {
                _anchorGen = _anchor.GetComponentInChildren<CelestialBodyGenerator>();
                _anchorOceanR = _anchorGen != null ? _anchorGen.GetOceanRadius() : 0f;
            }
            catch { _anchorOceanR = 0f; }
        }
    }

    void UpdateGravityFrame()
    {
        if (_anchor == null)
        {
            _gravUp = transform.up; _gravMag = 0f; _groundVel = Vector3.zero;
            return;
        }
        Vector3 g = Universe.GravityAcceleration(_rb.position, _anchor);
        _gravMag = g.magnitude;
        _gravUp = _gravMag > 1e-4f ? -g / _gravMag : (_rb.position - _anchor.Position).normalized;
        _groundVel = _anchor.velocity;
    }

    // ── Spawn settle / summon ───────────────────────────────────────────────

    /// Drop the rover onto the real terrain under it. `force` = give up
    /// waiting and just go dynamic where it is.
    void Settle(bool force)
    {
        if (_settled) return;
        var body = _spawnAnchor != null ? _spawnAnchor : _anchor;
        Vector3 up = body != null ? (_rb.position - body.Position).normalized : transform.up;
        float clearance = restLength + wheelRadius + 0.35f;
        bool ok = false;
        Vector3 origin = _rb.position + up * 60f;
        int n = Physics.RaycastNonAlloc(origin, -up, _hits, 200f, _groundMask, QueryTriggerInteraction.Ignore);
        float bd = float.MaxValue; RaycastHit best = default;
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i];
            if (h.collider == null || _own.Contains(h.collider)) continue;
            if (h.distance < bd) { bd = h.distance; best = h; }
        }
        if (bd < float.MaxValue)
        {
            _rb.position = best.point + up * clearance;
            ok = true;
        }
        if (!ok && !force) return;

        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        _rb.rotation = Quaternion.LookRotation(fwd.normalized, up);
        transform.SetPositionAndRotation(_rb.position, _rb.rotation);
        _rb.isKinematic = false;
        _rb.velocity = body != null ? body.velocity : Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        for (int i = 0; i < WheelCount; i++) _length[i] = restLength;
        _settled = true;
        Physics.SyncTransforms();
        if (body != null) _anchor = body;
        RecordLocalPose();
        Debug.Log($"[Rover] settled on {(body != null ? body.name : "nothing")} (terrain {(ok ? "found" : "NOT found — released anyway")})");
    }

    /// Dev cheat (Home): bring the rover to 6 m in front of the player and re-settle it.
    public void SummonToPlayer()
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc == null) return;
        ElectAnchor();
        Vector3 up = _anchor != null ? (pc.transform.position - _anchor.Position).normalized : pc.transform.up;
        Vector3 fwd = Vector3.ProjectOnPlane(pc.transform.forward, up).normalized;
        Vector3 pos = pc.transform.position + fwd * 6f + up * 3f;
        _rb.isKinematic = true;
        _rb.position = pos;
        _rb.rotation = Quaternion.LookRotation(fwd, up);
        transform.SetPositionAndRotation(pos, _rb.rotation);
        RememberSpawnLocal();
        _settled = false;
        _hasLastLocal = false;
        _settleDeadline = Time.time + 3f;
        Physics.SyncTransforms();
        Settle(false);
    }

    // ── Visuals ─────────────────────────────────────────────────────────────

    void UpdateWheelVisuals(float dt)
    {
        for (int i = 0; i < WheelCount; i++)
        {
            var vis = wheelVisuals[i];
            var hp = wheelHardpoints[i];
            if (vis == null || hp == null) continue;
            // Follow the physics length with a little render-rate smoothing so
            // a 100 Hz step never shows as a jitter.
            Vector3 target = hp.localPosition - Vector3.up * _length[i];
            vis.localPosition = Vector3.Lerp(vis.localPosition, target, 1f - Mathf.Exp(-dt * 30f));
            _spinDeg[i] = (_spinDeg[i] + _spinRate[i] * dt) % 360f;
            float steer = i < 2 ? _steerDeg : 0f;
            vis.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(_spinDeg[i], 0f, 0f);
        }
    }

    // ── Engine hum (procedural, no clip needed) ─────────────────────────────

    void BuildEngineAudio()
    {
        _engine = gameObject.AddComponent<AudioSource>();
        _engine.playOnAwake = false;
        _engine.loop = true;
        _engine.spatialBlend = 1f;
        _engine.minDistance = 3f;
        _engine.maxDistance = 45f;
        _engine.rolloffMode = AudioRolloffMode.Linear;
        _engine.volume = 0f;
        _engine.dopplerLevel = 0f;
        const int rate = 22050;
        const float baseHz = 46f;
        int samples = rate;   // exactly 1 s: baseHz is an integer → seamless loop
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)rate;
            float ph = t * baseHz * 2f * Mathf.PI;
            // A lumpy four-stroke thump: fundamental + odd harmonics, softened.
            float v = Mathf.Sin(ph) * 0.55f + Mathf.Sin(ph * 2f) * 0.25f + Mathf.Sin(ph * 3f) * 0.12f + Mathf.Sin(ph * 5f) * 0.05f;
            v += (Mathf.PerlinNoise(t * 900f, 0.37f) - 0.5f) * 0.18f;   // mechanical rasp
            data[i] = Mathf.Clamp(v, -1f, 1f) * 0.8f;
        }
        var clip = AudioClip.Create("RoverEngineHum", samples, 1, rate, false);
        clip.SetData(data, 0);
        _engine.clip = clip;
    }

    void UpdateEngineAudio(float dt)
    {
        if (_engine == null) return;
        bool on = _driver != null;
        if (on && !_engine.isPlaying) _engine.Play();
        float speedT = Mathf.Clamp01(Mathf.Abs(SpeedKmh) / (maxSpeed * 3.6f));
        float targetPitch = on ? 0.55f + speedT * 0.9f + Mathf.Abs(_throttle) * 0.25f : 0.5f;
        _enginePitch = Mathf.Lerp(_enginePitch, targetPitch, 1f - Mathf.Exp(-dt * 4f));
        _engine.pitch = _enginePitch;
        float targetVol = on ? engineVolume * (0.55f + 0.45f * Mathf.Abs(_throttle)) : 0f;
        _engine.volume = Mathf.Lerp(_engine.volume, targetVol, 1f - Mathf.Exp(-dt * 6f));
        if (!on && _engine.isPlaying && _engine.volume < 0.01f) _engine.Stop();
    }
}
