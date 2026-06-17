using UnityEngine;

/// <summary>
/// Mission 1 Pilot branch — the VR-piloted test "ship" (a scaled-down ship44 stripped to a
/// dumb flyable body). It is NOT a Ship (never counts as one in saves/AI/HUD), but its flight
/// is a faithful copy of Ship's so the test is LITERAL practice: same input, same thrust model,
/// same per-axis Shift boost with the SAME limited boost fuel, the same GForceHUD boost meter,
/// the same Rigidbody mass/drag (so it isn't knocked around despite being small), and the same
/// V (match-velocity) / O (circularize) flight assists. Default tunables mirror the SHIP44
/// prefab; CopyShipTunables overwrites them from a live ship if one exists.
///
/// Entry is only via the instructor + paid test (ShipPilotTest.BeginTest → Enter). While flying,
/// F is the "take the VR goggles off" abort: first F shows "Press F again to stop the test" for
/// a few seconds; a second F within that window fires OnExitConfirmed (a fail).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    /// <summary>The drone the player is currently piloting (for the boost HUD). Null when none.</summary>
    public static DroneController Active { get; private set; }

    [Header("Mimic")]
    [Tooltip("Optional but recommended: assign the SHIP44 prefab. The drone copies its exact flight values + Rigidbody at runtime, so it always matches the real ship regardless of serialized drift.")]
    public Ship mimicShip;

    [Header("Mounts")]
    [Tooltip("Where the player camera sits while flying. Defaults to this transform.")]
    public Transform camViewPoint;

    [Header("Handling — defaults mirror SHIP44; copied from a live ship at entry if present")]
    public float thrustStrength = 22f;
    public float thrustPowerScale = 0.33f;
    public float thrustRampSeconds = 0.15f;
    public float boostMultiplier = 2.5f;
    public float rotSpeed = 5f;
    public float rollSpeed = 30f;
    public float rotSmoothSpeed = 10f;

    [Header("Rigidbody (match the ship so it isn't knocked around / flipped by the mouse)")]
    public float mass = 10000f;
    public float linearDrag = 0f;
    public float angularDrag = 10f;

    [Header("Boost fuel (limited — short spurts, like the ship)")]
    public float boostFuelMax = 1f;
    public float boostDrainPerSec = 0.5f;
    public float boostRefillPerSec = 0.4f;

    [Header("Match Velocity (V)")]
    public KeyCode matchVelocityKey = KeyCode.V;
    public float matchVelocityBoostSpeedThreshold = 10f;
    public float matchVelocityMatchedSpeed = 0.5f;

    [Header("Circularize (O)")]
    public KeyCode circularizeKey = KeyCode.O;
    public float circularizeBoostNeedThreshold = 8f;
    public float orbitMatchedRadialSpeed = 0.5f;
    public float circularizeRangeMul = 3f;

    [Header("Crash")]
    [Tooltip("Relative impact speed (m/s) that counts as a crash. A gentle settle/landing is well below this.")]
    public float crashSpeed = 14f;

    [Header("Exit (VR goggles) confirmation")]
    [Tooltip("Seconds the 'press F again' confirmation stays armed.")]
    public float exitConfirmWindow = 3f;
    [TextArea(1, 2)]
    public string exitConfirmText = "Press F again to stop the test";

    [Header("Input")]
    [Tooltip("Reused for mouse sensitivity so the drone feels like the ship. Auto-found / copied from the ship if null.")]
    public InputSettings inputSettings;

    [Header("Lights (only lit while piloting)")]
    [Tooltip("Lights on the drone that should be OFF while it sits parked on the launchpad and only switch ON once the player is actually piloting it — so the idle test drone doesn't cast a light + shadow over the school. Leave empty to auto-collect every Light in the drone's children (the school's own torches are siblings, not children, so they're never touched).")]
    public Light[] pilotLights;

    public bool IsPiloted => _isPiloted;
    public Rigidbody Body => _rb;
    public System.Action OnEnter;
    public System.Action OnExit;
    /// <summary>Fired when the player confirms the two-press F abort (goggles off).</summary>
    public System.Action OnExitConfirmed;
    /// <summary>Fired on a hard collision while piloted — a crash.</summary>
    public System.Action OnCrashed;

    public float UpBoostFuelPercent   => boostFuelMax > 0f ? _boostFuelUp   / boostFuelMax : 0f;
    public float DownBoostFuelPercent => boostFuelMax > 0f ? _boostFuelDown / boostFuelMax : 0f;
    public float DirBoostFuelPercent  => boostFuelMax > 0f ? _boostFuelDir  / boostFuelMax : 0f;

    // Flight-assist state (read by GForceHUD arrows + FlightAssistStatusHUD), mirroring Ship.
    public Vector3 MatchAssistInput { get; private set; }
    public Vector3 CircularizeAssistInput { get; private set; }
    public bool IsMatchingVelocity { get; private set; }
    public bool IsVelocityMatched { get; private set; }
    public bool IsCircularizing { get; private set; }
    public bool IsOrbitMatched { get; private set; }

    Rigidbody _rb;
    PlayerController _pilot;
    bool _isPiloted;

    Vector3 _thrusterInput;
    Vector3 _smoothedThrusterInput;
    Quaternion _targetRot;
    Quaternion _smoothedRot;

    float _boostFuelUp, _boostFuelDown, _boostFuelDir;
    bool _boostUpDepleted, _boostDownDepleted, _boostDirDepleted;
    int _groundTouches;   // >0 while resting on something — like the ship, we stop steering then so it settles flat

    bool _exitArmed;
    float _exitArmedUntil;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;        // gravity applied manually (n-body), exactly like the ship
        _rb.isKinematic = true;        // parked until the test puts the player in
        _rb.mass = mass;
        _rb.drag = linearDrag;
        _rb.angularDrag = angularDrag;
        _targetRot = _smoothedRot = transform.rotation;
        _boostFuelUp = _boostFuelDown = _boostFuelDir = boostFuelMax;

        if (inputSettings == null) inputSettings = FindObjectOfType<InputSettings>();

        EnsureCollider();

        CachePilotLights();
        SetPilotLights(false);   // parked on the pad → dark until the test puts the player in

        // Floating-origin: register so it stays positioned as the world re-centres (CLAUDE.md).
        var endless = FindObjectOfType<EndlessManager>();
        if (endless != null) endless.RegisterPhysicsObject(transform);
    }

    // Collect the drone's own lights once (children only — the school's torches
    // sit alongside the drone, not under it, so they're untouched). Honours a
    // hand-assigned list if one was wired in the inspector.
    void CachePilotLights()
    {
        if (pilotLights == null || pilotLights.Length == 0)
            pilotLights = GetComponentsInChildren<Light>(true);
    }

    void SetPilotLights(bool on)
    {
        if (pilotLights == null) return;
        for (int i = 0; i < pilotLights.Length; i++)
            if (pilotLights[i] != null) pilotLights[i].enabled = on;
    }

    void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null) return;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;

        Bounds world = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) world.Encapsulate(rends[i].bounds);

        Vector3 lossy = transform.lossyScale;
        Vector3 safeLossy = new Vector3(
            Mathf.Approximately(lossy.x, 0f) ? 1f : lossy.x,
            Mathf.Approximately(lossy.y, 0f) ? 1f : lossy.y,
            Mathf.Approximately(lossy.z, 0f) ? 1f : lossy.z);

        var bc = gameObject.AddComponent<BoxCollider>();
        bc.center = transform.InverseTransformPoint(world.center);
        bc.size = new Vector3(world.size.x / safeLossy.x, world.size.y / safeLossy.y, world.size.z / safeLossy.z);

        var grip = new PhysicMaterial("DroneGrip")
        {
            dynamicFriction = 1f,
            staticFriction = 1f,
            frictionCombine = PhysicMaterialCombine.Maximum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        bc.sharedMaterial = grip;
    }

    void CopyShipTunables()
    {
        var ship = mimicShip != null ? mimicShip : FindObjectOfType<Ship>();
        if (ship == null) return;
        thrustStrength    = ship.thrustStrength;
        thrustPowerScale  = ship.thrustPowerScale;
        thrustRampSeconds = ship.thrustRampSeconds;
        boostMultiplier   = ship.boostMultiplier;
        rotSpeed          = ship.rotSpeed;
        rollSpeed         = ship.rollSpeed;
        rotSmoothSpeed    = ship.rotSmoothSpeed;
        boostFuelMax      = ship.boostFuelMax;
        boostDrainPerSec  = ship.boostDrainPerSec;
        boostRefillPerSec = ship.boostRefillPerSec;
        matchVelocityKey  = ship.matchVelocityKey;
        matchVelocityBoostSpeedThreshold = ship.matchVelocityBoostSpeedThreshold;
        matchVelocityMatchedSpeed = ship.matchVelocityMatchedSpeed;
        circularizeKey    = ship.circularizeKey;
        circularizeBoostNeedThreshold = ship.circularizeBoostNeedThreshold;
        orbitMatchedRadialSpeed = ship.orbitMatchedRadialSpeed;
        circularizeRangeMul = ship.circularizeRangeMul;
        if (ship.inputSettings != null) inputSettings = ship.inputSettings;
        var srb = ship.GetComponent<Rigidbody>();
        if (srb != null) { _rb.mass = srb.mass; _rb.drag = srb.drag; _rb.angularDrag = srb.angularDrag; }
    }

    // ── entry / exit (driven by ShipPilotTest) ────────────────────────────
    public void Enter(PlayerController pilot)
    {
        if (_isPiloted || pilot == null) return;
        CopyShipTunables();
        _pilot = pilot;
        _isPiloted = true;
        Active = this;
        _rb.isKinematic = false;
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _targetRot = _smoothedRot = transform.rotation;
        _smoothedThrusterInput = Vector3.zero;
        _thrusterInput = Vector3.zero;
        _boostFuelUp = _boostFuelDown = _boostFuelDir = boostFuelMax;
        _boostUpDepleted = _boostDownDepleted = _boostDirDepleted = false;
        DisarmExit();

        var cam = _pilot.Camera;
        Transform mount = camViewPoint != null ? camViewPoint : transform;
        cam.transform.parent = mount;
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        _pilot.gameObject.SetActive(false);

        SetPilotLights(true);    // goggles on — the drone's headlight/glow comes alive

        OnEnter?.Invoke();
    }

    public void Exit(Vector3 pos, Quaternion rot)
    {
        if (!_isPiloted) return;
        _isPiloted = false;
        if (Active == this) Active = null;
        DisarmExit();
        _rb.isKinematic = true;
        SetPilotLights(false);   // back on the pad — lights off again

        if (_pilot != null)
        {
            _pilot.transform.position = pos;
            _pilot.transform.rotation = rot;
            _pilot.gameObject.SetActive(true);
            if (_pilot.Rigidbody != null) _pilot.Rigidbody.velocity = Vector3.zero;
            _pilot.ExitFromSpaceship();
            _pilot.SnapOrientationOnExitPilot(null);
            if (CameraEffectsManager.Instance != null && CameraEffectsManager.Instance.TransformFX != null)
                CameraEffectsManager.Instance.TransformFX.SnapToCurrentPlayer();
        }
        _pilot = null;
        OnExit?.Invoke();
    }

    // ── per-frame ──────────────────────────────────────────────────────────
    void Update()
    {
        if (!_isPiloted) return;

        if (TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            if (_exitArmed && Time.unscaledTime <= _exitArmedUntil)
            {
                DisarmExit();
                OnExitConfirmed?.Invoke();
                return;
            }
            ArmExit();
        }
        else if (_exitArmed && Time.unscaledTime > _exitArmedUntil)
        {
            DisarmExit();
        }

        ReadFlightInput();
    }

    void ArmExit()
    {
        _exitArmed = true;
        _exitArmedUntil = Time.unscaledTime + Mathf.Max(0.5f, exitConfirmWindow);
        InteractPromptUI.Show(this, exitConfirmText);
    }

    void DisarmExit()
    {
        if (!_exitArmed) return;
        _exitArmed = false;
        InteractPromptUI.Clear(this);
    }

    void ReadFlightInput()
    {
        if (PlayerController.isMapOpen || AIChatScreen.IsTypingActive)
        {
            _thrusterInput = Vector3.zero;
            _smoothedThrusterInput = Vector3.MoveTowards(_smoothedThrusterInput, Vector3.zero,
                thrustRampSeconds > 0.001f ? Time.deltaTime / thrustRampSeconds : 1f);
            return;
        }

        float thrustInputX = TutorialGate.GetAxisRaw("Horizontal", TutorialAbility.ShipMove);
        float thrustInputZ = TutorialGate.GetAxisRaw("Vertical",   TutorialAbility.ShipMove);
        int thrustInputY = 0;
        if (TutorialGate.JumpHeld(TutorialAbility.ShipUpThrust))         thrustInputY++;
        if (TutorialGate.DownThrustHeld(TutorialAbility.ShipDownThrust)) thrustInputY--;
        _thrusterInput = new Vector3(thrustInputX, thrustInputY, thrustInputZ);

        float rampRate = thrustRampSeconds > 0.001f ? Time.deltaTime / thrustRampSeconds : 1f;
        _smoothedThrusterInput = Vector3.MoveTowards(_smoothedThrusterInput, _thrusterInput, rampRate);

        float sens = inputSettings != null ? inputSettings.mouseSensitivity : 100f;
        float yawInput   = TutorialGate.GetAxisRaw("Mouse X", TutorialAbility.ShipMouseLook) * rotSpeed * sens / 100f;
        float pitchInput = TutorialGate.GetAxisRaw("Mouse Y", TutorialAbility.ShipMouseLook) * rotSpeed * sens / 100f;
        if (TutorialGate.ControllerEnabled && TutorialGate.IsUnlocked(TutorialAbility.ShipMouseLook))
        {
            const float kShipStickDegreesPerSecond = 360f;
            float gain = TutorialGate.ShipStickLookSensitivity * kShipStickDegreesPerSecond * Time.unscaledDeltaTime;
            yawInput   += TutorialGate.RightStickX() * gain;
            pitchInput += TutorialGate.RightStickY() * gain * (TutorialGate.InvertLookY ? -1f : 1f);
        }

        int rollDir = 0;
        if (TutorialGate.RollLeftHeld(TutorialAbility.ShipRoll))  rollDir--;
        if (TutorialGate.RollRightHeld(TutorialAbility.ShipRoll)) rollDir++;
        float rollInput = rollDir * rollSpeed * Time.deltaTime;

        // Only steer while airborne (like the ship's numCollisionTouches gate). While resting
        // on the pad we leave rotation to physics so the body settles flat instead of being
        // held on an edge and sliding off.
        if (_groundTouches == 0)
        {
            var yaw   = Quaternion.AngleAxis(yawInput, transform.up);
            var pitch = Quaternion.AngleAxis(-pitchInput, transform.right);
            var roll  = Quaternion.AngleAxis(-rollInput, transform.forward);
            _targetRot = yaw * pitch * roll * _targetRot;
            _smoothedRot = Quaternion.Slerp(transform.rotation, _targetRot, Time.deltaTime * rotSmoothSpeed);
        }
        else
        {
            _targetRot = _smoothedRot = transform.rotation;
        }
    }

    void FixedUpdate()
    {
        if (!_isPiloted) return;
        float dt = Time.fixedDeltaTime;

        // Gravity (n-body) — always, exactly like the ship.
        _rb.AddForce(NBodySimulation.CalculateAcceleration(_rb.position), ForceMode.Acceleration);

        if (!PlayerController.isMapOpen)
        {
            // ── Manual thrust with per-axis Shift boost (limited fuel) ──
            bool boostKey = Input.GetKey(KeyCode.LeftShift);
            bool boostUp   = boostKey && _thrusterInput.y > 0.01f  && _boostFuelUp   > 0f && !_boostUpDepleted;
            bool boostDown = boostKey && _thrusterInput.y < -0.01f && _boostFuelDown > 0f && !_boostDownDepleted;
            bool boostDir  = boostKey && (Mathf.Abs(_thrusterInput.x) > 0.01f || Mathf.Abs(_thrusterInput.z) > 0.01f) && _boostFuelDir > 0f && !_boostDirDepleted;

            float scaleX = thrustPowerScale, scaleY = thrustPowerScale, scaleZ = thrustPowerScale;
            if (boostUp || boostDown) scaleY = thrustPowerScale * boostMultiplier;
            if (boostDir) { scaleX = thrustPowerScale * boostMultiplier; scaleZ = thrustPowerScale * boostMultiplier; }

            Vector3 scaledLocal = new Vector3(_smoothedThrusterInput.x * scaleX, _smoothedThrusterInput.y * scaleY, _smoothedThrusterInput.z * scaleZ);
            // TransformDirection (rotation only), NOT TransformVector — the latter applies scale,
            // which on this 0.1x drone would shrink thrust to a tenth.
            _rb.AddForce(transform.TransformDirection(scaledLocal) * thrustStrength, ForceMode.Acceleration);

            // ── Flight assists (V / O) — same logic as the ship ──
            bool typing = AIChatScreen.IsTypingActive;
            bool vHeld = !typing && Input.GetKey(matchVelocityKey);
            bool oHeld = !typing && Input.GetKey(circularizeKey);
            IsMatchingVelocity = false; IsCircularizing = false;
            MatchAssistInput = Vector3.zero; CircularizeAssistInput = Vector3.zero;

            // V — match velocity to the map's followed ship / highlighted body.
            float matchRelMag = 0f;
            if (vHeld)
            {
                Vector3 targetVel = Vector3.zero;
                bool hasTarget = false;
                var map = SolarSystemMapController.Instance;
                if (map != null)
                {
                    if (map.FollowedShip != null)
                    {
                        var trb = map.FollowedShip.GetComponent<Rigidbody>();
                        if (trb != null) { targetVel = trb.velocity; hasTarget = true; }
                    }
                    else if (map.PendingHighlight != null)
                    {
                        targetVel = map.PendingHighlight.velocity;
                        hasTarget = true;
                    }
                }
                if (hasTarget)
                {
                    Vector3 relVel = _rb.velocity - targetVel;
                    matchRelMag = relVel.magnitude;
                    if (matchRelMag > 0.01f)
                    {
                        Vector3 thrustLocal = transform.InverseTransformDirection(-relVel.normalized);
                        bool useBoost = matchRelMag > matchVelocityBoostSpeedThreshold && _boostFuelDir > 0f && !_boostDirDepleted;
                        float axisScale = useBoost ? thrustPowerScale * boostMultiplier : thrustPowerScale;
                        Vector3 force = transform.TransformDirection(thrustLocal * axisScale) * thrustStrength;
                        float plannedDV = force.magnitude * dt;
                        if (plannedDV > matchRelMag) force *= matchRelMag / plannedDV;
                        _rb.AddForce(force, ForceMode.Acceleration);
                        if (useBoost) _boostFuelDir = Mathf.Clamp(_boostFuelDir - boostDrainPerSec * dt, 0f, boostFuelMax);
                        MatchAssistInput = thrustLocal;
                        IsMatchingVelocity = true;
                    }
                }
            }
            IsVelocityMatched = vHeld && matchRelMag > 0f && matchRelMag < matchVelocityMatchedSpeed;

            // O — circularize the orbit around the nearest planet/moon.
            float orbitRadialMag = 0f; bool orbitInRange = false;
            if (oHeld)
            {
                CelestialBody best = null;
                float bestSqr = float.MaxValue;
                var bodies = NBodySimulation.Bodies;
                if (bodies != null)
                {
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        var b = bodies[i];
                        if (b == null || b.bodyType == CelestialBody.BodyType.Sun) continue;
                        float dsq = (b.Position - _rb.position).sqrMagnitude;
                        if (dsq < bestSqr) { bestSqr = dsq; best = b; }
                    }
                }
                if (best != null)
                {
                    float maxRange = circularizeRangeMul * best.radius;
                    Vector3 r = _rb.position - best.Position;
                    float rMag = r.magnitude;
                    if (rMag <= maxRange && rMag > best.radius * 1.05f)
                    {
                        orbitInRange = true;
                        Vector3 v = _rb.velocity - best.velocity;
                        Vector3 radialDir = r / rMag;
                        Vector3 vRadial = Vector3.Dot(v, radialDir) * radialDir;
                        Vector3 vTang = v - vRadial;
                        orbitRadialMag = vRadial.magnitude;
                        float vCirc = Mathf.Sqrt(Universe.gravitationalConstant * best.mass / rMag);
                        Vector3 tangDir = vTang.sqrMagnitude > 0.01f ? vTang.normalized : Vector3.Cross(radialDir, Vector3.up).normalized;
                        if (tangDir.sqrMagnitude < 0.01f) tangDir = Vector3.Cross(radialDir, Vector3.right).normalized;
                        Vector3 vTargetWorld = tangDir * vCirc + best.velocity;
                        Vector3 needed = vTargetWorld - _rb.velocity;
                        float needMag = needed.magnitude;
                        if (needMag > 0.01f)
                        {
                            Vector3 thrustLocal = transform.InverseTransformDirection(needed.normalized);
                            bool useBoost = needMag > circularizeBoostNeedThreshold && _boostFuelUp > 0f && !_boostUpDepleted;
                            float axisScale = useBoost ? thrustPowerScale * boostMultiplier : thrustPowerScale;
                            Vector3 force = transform.TransformDirection(thrustLocal * axisScale) * thrustStrength;
                            float plannedDV = force.magnitude * dt;
                            if (plannedDV > needMag) force *= needMag / plannedDV;
                            _rb.AddForce(force, ForceMode.Acceleration);
                            if (useBoost) _boostFuelUp = Mathf.Clamp(_boostFuelUp - boostDrainPerSec * dt, 0f, boostFuelMax);
                            CircularizeAssistInput = thrustLocal;
                            IsCircularizing = true;
                        }
                    }
                }
            }
            IsOrbitMatched = oHeld && orbitInRange && orbitRadialMag < orbitMatchedRadialSpeed;

            // Fuel bookkeeping — drain active manual-boost axes, refill idle ones; deplete-lockout.
            _boostFuelUp   = Mathf.Clamp(_boostFuelUp   + (boostUp   ? -boostDrainPerSec : boostRefillPerSec) * dt, 0f, boostFuelMax);
            _boostFuelDown = Mathf.Clamp(_boostFuelDown + (boostDown ? -boostDrainPerSec : boostRefillPerSec) * dt, 0f, boostFuelMax);
            _boostFuelDir  = Mathf.Clamp(_boostFuelDir  + (boostDir  ? -boostDrainPerSec : boostRefillPerSec) * dt, 0f, boostFuelMax);
            if (_boostFuelUp   <= 0f)           _boostUpDepleted   = true;
            if (_boostFuelDown <= 0f)           _boostDownDepleted = true;
            if (_boostFuelDir  <= 0f)           _boostDirDepleted  = true;
            if (_boostFuelUp   >= boostFuelMax) _boostUpDepleted   = false;
            if (_boostFuelDown >= boostFuelMax) _boostDownDepleted = false;
            if (_boostFuelDir  >= boostFuelMax) _boostDirDepleted  = false;
        }
        else
        {
            // Map open — idle refill of all pools.
            _boostFuelUp   = Mathf.Clamp(_boostFuelUp   + boostRefillPerSec * dt, 0f, boostFuelMax);
            _boostFuelDown = Mathf.Clamp(_boostFuelDown + boostRefillPerSec * dt, 0f, boostFuelMax);
            _boostFuelDir  = Mathf.Clamp(_boostFuelDir  + boostRefillPerSec * dt, 0f, boostFuelMax);
        }

        if (_groundTouches == 0) _rb.MoveRotation(_smoothedRot);
    }

    void OnCollisionEnter(Collision c)
    {
        _groundTouches++;
        if (!_isPiloted) return;
        if (c.relativeVelocity.magnitude >= crashSpeed) OnCrashed?.Invoke();
    }

    void OnCollisionExit(Collision c)
    {
        if (_groundTouches > 0) _groundTouches--;
    }

    void OnDisable()
    {
        DisarmExit();
        if (Active == this) Active = null;
    }
}
