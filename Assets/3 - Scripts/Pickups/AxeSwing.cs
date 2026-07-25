using UnityEngine;

/// <summary>
/// Mouse-driven swing layer for the axe (M2 of the physics-axe spike — see
/// docs/2026-07-24-physics-axe-spike-handoff.md). Drives the "AxeSwingRig"
/// transform AxeController inserts between the AxeMotorRig and the AxePivot:
///
///   axeHoldPosition → AxeMotorRig (carry sway) → AxeSwingRig (this) → AxePivot (equip anim + rest offsets)
///
/// v5 — two real swings (Sam's spec, plainly): an axe swung sideways goes
/// VERTICAL → HORIZONTAL. So v5 encodes the two actual motions as
/// first-class arcs instead of one generic model:
///
///   SLASH (mouse moves sideways while LMB held): the axe lays down flat —
///   handle horizontal, pointed out toward the tree — and sweeps left↔right
///   through a wide yaw arc like a scythe, head crossing the trunk, edge
///   flipping to lead the motion. Swing rhythm = mouse left-right-left.
///
///   CHOP (mouse moves vertically): mouse up cocks the axe up and back over
///   the shoulder; mouse down drives it down and through. Edge stays on the
///   axe's natural forward-down chop face.
///
/// The mode follows whichever way the mouse is actually moving (with
/// hysteresis + smooth blending); momentum lives in a 1D swing progress per
/// mode, so a flick carries through and reversals re-cock naturally.
/// Release LMB: everything springs back to the upright carry pose.
///
/// All rotations pivot about the GRIP with (grip − R·grip) compensation —
/// the axe model carries large authoring-orientation offsets and naive rig
/// rotations orbit it around empty space.
///
/// While held, camera mouse-look is scaled by swingLookScale via the
/// PlayerController.SwingLookScale static hook (0 = camera locked).
/// Kinematic, camera-local, framerate-independent.
/// </summary>
[DefaultExecutionOrder(10)]   // after AxeMotor (0) so BladeSweep reads a settled world pose
public class AxeSwing : MonoBehaviour
{
    [Header("The central tunable — camera vs. axe input split")]
    [Tooltip("While LMB is held: how much the camera still turns with the mouse. 0 = locked (committed), 1 = full turn (view-drag).")]
    [Range(0f, 1f)] public float swingLookScale = 0.25f;
    [Tooltip("Spike-build on-screen readout. Turn off when the verdict is in.")]
    public bool showDebugReadout = true;
    [Tooltip("Controller: right-stick deflection → swing input, in mouse-units per second at full deflection. Hold RT to swing, stick sweeps the axe. A mouse flick spikes far harder than a held stick, so this needs to be generous.")]
    public float stickSwingRate = 420f;
    [Tooltip("Controller only: stick swing input multiplier while the axe is ARMED — a charged swing whips through at speed.")]
    public float armedStickBoost = 2f;

    [Header("Horizontal SLASH (axe lays flat and sweeps like a scythe)")]
    [Tooltip("How far the axe lays down for a side swing (deg pitch forward from vertical). ~90 = fully horizontal. Too high and the head dips out the bottom of the frame — the grip sits at mid-height.")]
    public float slashLayPitch = 58f;
    [Tooltip("Yaw arc half-width (deg) on the RIGHT side.")]
    public float slashYawRange = 85f;
    [Tooltip("Extra reach (deg) on the LEFT side — the arc is asymmetric, the left sweep carries further over.")]
    public float slashYawExtraLeft = 30f;
    [Tooltip("Swing-progress impulse per unit of raw mouse X. Higher = lighter, faster to cross the arc.")]
    public float slashSensitivity = 0.18f;
    [Tooltip("Exponential decay (1/s) of slash momentum. Higher = tracks the mouse more directly, less coasting/overshoot past where you stopped.")]
    public float slashDamping = 9f;
    [Tooltip("Flip if mouse-right sweeps the axe left.")]
    public bool invertSwing = false;
    [Tooltip("Sideways hand travel (m) at full RIGHT slash extent — carries the swing across the screen.")]
    public float slashHandTravel = 0.42f;
    [Tooltip("Extra hand travel (m) on the LEFT — the hold sits right-of-centre, so the left wind-up needs more distance to leave the frame like the right does.")]
    public float slashHandTravelExtraLeft = 0.25f;
    [Tooltip("Hand rise (m) while in the slash pose — keeps the laid-out axe up in frame.")]
    public float slashHandRise = 0.16f;

    [Header("Vertical CHOP (cock up, drive down)")]
    [Tooltip("Pitch (deg) at full cock — negative = raised up and back over the shoulder.")]
    public float chopCockPitch = -50f;
    [Tooltip("Pitch (deg) at full extension — driven down and through.")]
    public float chopDrivePitch = 80f;
    [Tooltip("Swing-progress impulse per unit of raw mouse Y.")]
    public float chopSensitivity = 0.5f;
    [Tooltip("Exponential decay (1/s) of chop momentum.")]
    public float chopDamping = 5f;
    [Tooltip("Hand rise (m) at full cock (and half of it drops at full drive).")]
    public float chopHandRise = 0.22f;

    [Header("Mode selection + return")]
    [Tooltip("How much one mouse axis must dominate the other (ratio) before the mode switches. Hysteresis against jitter.")]
    public float modeDominance = 1.4f;
    [Tooltip("How fast (1/s) the pose blends between slash and chop modes.")]
    public float modeBlendRate = 9f;
    [Tooltip("Spring stiffness returning swing progress to rest on release.")]
    public float returnStiffness = 110f;
    [Tooltip("Damping for the return spring.")]
    public float returnDamping = 13f;

    [Header("Blade facing (latched at the wind-up)")]
    [Tooltip("Roll (deg) about the handle once a wind-up latches. Wind up on the right → edge sets facing left for the swing, and STAYS there through partial swings — chop one-sided forever. Only carrying the axe to the opposite wind-up rest re-latches it the other way.")]
    public float bladeFaceAngle = 90f;
    [Tooltip("How far into the arc (0..1 of full extent) counts as reaching the wind-up rest and latches the blade.")]
    public float windupLatchPoint = 0.85f;
    [Tooltip("How fast the edge rotates when the latch changes (deg/s).")]
    public float maxRollRate = 520f;
    [Tooltip("Local axis of the pivot the blade rolls around — the handle's long axis.")]
    public Vector3 rollAxis = Vector3.up;
    [Tooltip("Flip if the edge trails instead of leads.")]
    public bool invertRoll = false;

    [Header("Wind-up arming (hits only count when armed)")]
    [Tooltip("Seconds the axe must SIT at the wind-up before it arms and the shake begins — the forced pause between swings.")]
    public float armDelay = 0.5f;
    [Tooltip("How far into the arc (0..1 of full extent) counts as FULLY pulled back for the shake to show. Tighter than the latch/arm point so the shake only plays parked at the full left/right/up positions, not while passing nearby.")]
    public float shakeFullPullPoint = 0.97f;
    [Tooltip("Seconds for the armed shake to ramp from its starting intensity to max; it holds at max after that.")]
    public float shakeRampTime = 3f;
    [Tooltip("Shake amplitude (m) the instant the axe arms.")]
    public float shakeBaseAmplitude = 0.005f;
    [Tooltip("Shake amplitude (m) at full ramp.")]
    public float shakeMaxAmplitude = 0.022f;
    [Tooltip("Shake frequency at arm (Hz-ish).")]
    public float shakeMinFrequency = 7f;
    [Tooltip("Shake frequency at full ramp.")]
    public float shakeMaxFrequency = 22f;

    [Header("Ground clearance (kinematic — no Rigidbody)")]
    [Tooltip("Push the axe up so it rests against the ground instead of clipping through it (look-at-feet, downward chops into dirt).")]
    public bool groundClearance = true;
    [Tooltip("Layers that count as ground. Zero = auto (the Body/walkable layer).")]
    public LayerMask groundMask;
    [Tooltip("Gap (m) kept between the blade and the ground surface.")]
    public float clearanceSkin = 0.05f;
    [Tooltip("Response rate (1/s) of the push-up. Exponential — slows as it arrives, so sample noise doesn't read as shaking.")]
    public float clearanceRiseResponse = 14f;
    [Tooltip("Response rate (1/s) of settling back down once clear.")]
    public float clearanceFallResponse = 4f;
    [Tooltip("Cap (m) on the lift so extreme geometry can't shove the axe into the camera.")]
    public float maxClearanceLift = 0.9f;

    Transform _rig;                 // AxeSwingRig
    AxeController _axe;
    BladeSweep _sweep;

    // 1D swing progress per mode, each in [-1, +1], with momentum.
    float _slash, _slashVelocity;   // -1 = full left, +1 = full right
    float _chop, _chopVelocity;     // -1 = full cock (up/back), +1 = full drive (down/through)
    float _slashBlend;              // 0 = chop/carry pose family, 1 = laid-out slash pose
    float _roll;                    // deg — current edge facing (slash only)
    float _latchedRoll;             // deg — facing committed at the last wind-up (0 = not yet latched)
    float _groundLift;              // smoothed world-up lift keeping the axe out of the ground
    bool _armed;                    // charged by a full wind-up; next in-swing contact is a hit
    bool _armedSwingInFlight;       // the charge has left the wind-up — spent when a wind-up is reached again
    bool _atWindup;                 // currently sitting at a wind-up position
    float _windupTimer;             // continuous seconds at the wind-up (gates arming)
    float _armedTime;               // shake ramp time (accumulates only while shaking)
    float _shakePhase;              // perlin scrub position for the shake
    float _emaX, _emaY;             // recent |mouse| per axis, for mode dominance
    bool _holding;
    bool _slashMode;

    public bool IsArmed => _armed;
    public float ArmedRamp => Mathf.Clamp01(_armedTime / Mathf.Max(0.01f, shakeRampTime));

    /// <summary>BladeSweep calls this when an armed contact lands — one hit per wind-up.</summary>
    public void Disarm()
    {
        _armed = false;
        _armedSwingInFlight = false;
        _armedTime = 0f;
    }

    public bool IsActive => _rig != null &&
        (_holding || Mathf.Abs(_slashVelocity) > 0.1f || Mathf.Abs(_chopVelocity) > 0.1f
                  || Mathf.Abs(_slash) > 0.05f || Mathf.Abs(_chop) > 0.05f
                  || _slashBlend > 0.02f || Mathf.Abs(_roll) > 2f);

    public void Attach(Transform rig, AxeController axe, BladeSweep sweep)
    {
        _rig = rig;
        _axe = axe;
        _sweep = sweep;
        _slash = _slashVelocity = _chop = _chopVelocity = 0f;
        _slashBlend = _roll = _latchedRoll = _emaX = _emaY = 0f;
        _armed = _armedSwingInFlight = _atWindup = false;
        _windupTimer = _armedTime = _shakePhase = _groundLift = 0f;
        _holding = _slashMode = false;
        if (sweep != null) sweep.OnHitLanded = Disarm;
    }

    public void Detach(Transform rig)
    {
        if (_rig != rig) return;
        _rig = null;
        PlayerController.SwingLookScale = 1f;
    }

    void OnDisable()
    {
        PlayerController.SwingLookScale = 1f;   // never leave the camera stuck slow
    }

    void LateUpdate()
    {
        if (_rig == null) { PlayerController.SwingLookScale = 1f; return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Raw per-frame mouse delta — deliberately NOT the smoothed camera path.
        // Controller: the right stick is a rate input, so convert deflection to
        // an equivalent per-frame delta; RT is the pad's "LMB held".
        Vector2 delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        if (TutorialGate.ControllerEnabled)
        {
            // Charged boost (pad only): an armed swing whips twice as fast.
            float rate = stickSwingRate * (_armed ? armedStickBoost : 1f);
            delta += new Vector2(TutorialGate.RightStickX(), TutorialGate.RightStickY()) * (rate * dt);
        }

        bool allowed = _axe != null && _axe.PhysicsSwingAllowed;
        _holding = TutorialGate.FireHeld() && allowed;
        PlayerController.SwingLookScale = _holding ? swingLookScale : 1f;

        // Mode: follow whichever axis the player is actually moving (EMA + hysteresis).
        float emaDecay = Mathf.Exp(-4f * dt);
        _emaX = _emaX * emaDecay + Mathf.Abs(delta.x);
        _emaY = _emaY * emaDecay + Mathf.Abs(delta.y);
        if (_holding)
        {
            if (_emaX > _emaY * modeDominance) _slashMode = true;
            else if (_emaY > _emaX * modeDominance) _slashMode = false;
            // between: keep the current mode
        }

        if (_holding)
        {
            if (_slashMode) _slashVelocity += (invertSwing ? -delta.x : delta.x) * slashSensitivity;
            else            _chopVelocity  += -delta.y * chopSensitivity;   // mouse up = cock up (progress toward -1)
        }

        // Integrate both progress values, substepped. The inactive mode always
        // springs home so mode switches start from a clean pose.
        const float maxStep = 1f / 120f;
        float remaining = Mathf.Min(dt, 0.1f);
        while (remaining > 0f)
        {
            float h = Mathf.Min(remaining, maxStep);

            bool slashDriven = _holding && _slashMode;
            if (!slashDriven) _slashVelocity += (returnStiffness * -_slash - returnDamping * _slashVelocity) * h;
            _slashVelocity *= Mathf.Exp(-slashDamping * h);
            _slash += _slashVelocity * h;

            bool chopDriven = _holding && !_slashMode;
            if (!chopDriven) _chopVelocity += (returnStiffness * -_chop - returnDamping * _chopVelocity) * h;
            _chopVelocity *= Mathf.Exp(-chopDamping * h);
            _chop += _chopVelocity * h;

            remaining -= h;
        }

        // Arc ends bleed momentum.
        if (Mathf.Abs(_slash) > 1f) { _slash = Mathf.Sign(_slash); if (Mathf.Sign(_slashVelocity) == _slash) _slashVelocity = 0f; }
        if (Mathf.Abs(_chop) > 1f)  { _chop = Mathf.Sign(_chop);  if (Mathf.Sign(_chopVelocity) == _chop)  _chopVelocity = 0f; }

        // Pose blend: laid-out slash family vs upright chop/carry family.
        float blendTarget = _holding && _slashMode ? 1f : 0f;
        _slashBlend = Mathf.MoveTowards(_slashBlend, blendTarget, modeBlendRate * dt);

        // Edge facing: LATCHED at the wind-up. Reaching an arc extent commits
        // the blade to swing off that side (right wind-up → edge faces left);
        // partial swings never flip it — you can keep winding up on one side
        // and chopping like a real tree-feller. Only reaching the OPPOSITE
        // extent re-latches. Neutral until the first wind-up, and on release.
        if (_slashBlend > 0.05f)
        {
            if (Mathf.Abs(_slash) >= windupLatchPoint)
                _latchedRoll = -Mathf.Sign(_slash) * bladeFaceAngle * (invertRoll ? -1f : 1f);
        }
        else
        {
            _latchedRoll = 0f;   // left slash mode / released: forget the side
        }
        float rollTarget = _slashBlend > 0.05f ? _latchedRoll : 0f;
        _roll = Mathf.MoveTowards(_roll, rollTarget, maxRollRate * dt);

        // SLASH pose: lay the axe flat (pitch forward), then sweep the laid axe
        // about camera-up, edge roll innermost about the handle.
        float slashYaw = _slash * (_slash < 0f ? slashYawRange + slashYawExtraLeft : slashYawRange);
        Quaternion slashRot =
            Quaternion.AngleAxis(slashYaw, Vector3.up)
            * Quaternion.AngleAxis(slashLayPitch, Vector3.right)
            * Quaternion.AngleAxis(_roll, rollAxis.normalized);

        // CHOP pose: pitch arc through the upright rest — piecewise so _chop = 0
        // is exactly the untouched carry pose: -1 → chopCockPitch (raised/back),
        // +1 → chopDrivePitch (driven down/through).
        float chopPitch = _chop < 0f ? -_chop * chopCockPitch : _chop * chopDrivePitch;
        Quaternion chopRot = Quaternion.AngleAxis(chopPitch, Vector3.right);

        Quaternion swingRot = Quaternion.Slerp(chopRot, slashRot, _slashBlend);

        // Wind-up arming. The axe must SIT at a full wind-up (either slash
        // side; chop is COCK-UP ONLY — down is the strike) for armDelay before
        // it arms and the shake begins — the forced pause between swings.
        // Arming persists through the swing (and a miss); it's cleared by
        // landing a hit or releasing LMB. Hits only count once the swing has
        // LEFT the wind-up — a charged axe parked at the wind-up can't damage
        // anything, so walking it into a tree does nothing (multi-hit exploit).
        bool wasAtWindup = _atWindup;
        _atWindup = _holding && (_slashMode ? Mathf.Abs(_slash) >= windupLatchPoint
                                            : _chop <= -windupLatchPoint);

        // A charge is spent by the swing it powers: leaving the wind-up marks
        // the charge in-flight (contact still counts), and re-reaching ANY
        // wind-up ends that swing — disarm and start the whole charge-up over.
        // Every swing pays the 0.5s pause + ramp; no free max-speed reversals.
        if (_armed && !_atWindup) _armedSwingInFlight = true;
        if (_armed && _armedSwingInFlight && _atWindup && !wasAtWindup)
        {
            _armed = false;
            _armedSwingInFlight = false;
            _windupTimer = 0f;
        }

        _windupTimer = _atWindup ? _windupTimer + dt : 0f;
        if (_atWindup && _windupTimer >= armDelay) _armed = true;
        if (!_holding) { _armed = false; _armedSwingInFlight = false; }

        // Shake = the "ready" indicator: plays only while armed AND parked at
        // the FULL pull (tighter than the latch point — passing near the
        // wind-up must not flicker the shake). Starting the swing stops it
        // instantly; returning fully back while still armed resumes the ramp.
        bool atFullPull = _holding && (_slashMode ? Mathf.Abs(_slash) >= shakeFullPullPoint
                                                  : _chop <= -shakeFullPullPoint);
        Vector3 shakeOffset = Vector3.zero;
        if (_armed && atFullPull)
        {
            _armedTime += dt;
            float ramp = ArmedRamp;
            float amplitude = Mathf.Lerp(shakeBaseAmplitude, shakeMaxAmplitude, ramp);
            _shakePhase += Mathf.Lerp(shakeMinFrequency, shakeMaxFrequency, ramp) * dt;
            shakeOffset = new Vector3(
                Mathf.PerlinNoise(_shakePhase, 0.31f) - 0.5f,
                Mathf.PerlinNoise(0.73f, _shakePhase) - 0.5f,
                0f) * (2f * amplitude);
        }
        if (!_armed) _armedTime = 0f;

        // Hand travel: carries the swing without stealing the show.
        float slashTravel = _slash < 0f ? slashHandTravel + slashHandTravelExtraLeft : slashHandTravel;
        Vector3 slashPos = new Vector3(_slash * slashTravel, slashHandRise, 0f);
        Vector3 chopPos = new Vector3(0f, _chop < 0f ? -_chop * chopHandRise : -_chop * chopHandRise * 0.4f, 0f);
        Vector3 handPos = Vector3.Lerp(chopPos, slashPos, _slashBlend) + shakeOffset;

        // Rotate about the GRIP (holdPositionOffset), not the rig origin.
        Vector3 gripPoint = _axe != null ? _axe.holdPositionOffset : Vector3.zero;
        _rig.localRotation = swingRot;
        _rig.localPosition = handPos + (gripPoint - swingRot * gripPoint);

        // Ground clearance: probe down (gravity-up) from the blade samples and
        // lift the rig so the axe rests against the ground instead of clipping.
        // Recomputed from the unlifted pose each frame — no feedback build-up.
        if (groundClearance) ApplyGroundClearance(dt);

        // Blade sweep runs after the pose is final so casts see this frame's
        // edge path. Hits require armed AND mid-swing (left the wind-up).
        if (_sweep != null) _sweep.Tick(dt, _armed && !_atWindup);
    }

    void ApplyGroundClearance(float dt)
    {
        if (_sweep == null) return;
        Transform blade = _sweep.Blade;
        Vector3[] samples = _sweep.SampleLocalPoints;
        if (blade == null || samples == null || samples.Length == 0) return;

        if (groundMask == 0) groundMask = LayerMask.GetMask("Body");   // walkable/terrain layer
        Vector3 up = _axe != null ? _axe.transform.up : Vector3.up;    // gravity-aligned on the planet
        float radius = _sweep.SampleRadius;
        const float probe = 1.5f;

        // How far below the ground surface (plus skin) the deepest sample sits.
        float needed = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            Vector3 p = blade.TransformPoint(samples[i]);
            if (!Physics.Raycast(p + up * probe, -up, out RaycastHit hit, probe + 0.6f, groundMask, QueryTriggerInteraction.Ignore))
                continue;
            float below = (probe - hit.distance) + clearanceSkin + radius;
            if (below > needed) needed = below;
        }
        needed = Mathf.Min(needed, maxClearanceLift);

        // Exponential smoothing — velocity dies out as it reaches the target,
        // so millimetre noise in the probe results can't turn into shaking.
        // Fast response pushing up, gentle settling down.
        float response = needed > _groundLift ? clearanceRiseResponse : clearanceFallResponse;
        _groundLift = Mathf.Lerp(_groundLift, needed, 1f - Mathf.Exp(-response * dt));
        if (_groundLift > 0.0001f) _rig.position += up * _groundLift;
    }

    void OnGUI()
    {
        if (!showDebugReadout || _rig == null) return;
        float edge = _sweep != null ? _sweep.LastEdgeSpeed : 0f;
        string mode = _holding ? (_slashMode ? "SLASH" : "CHOP") : "carry";
        string armed = _armed ? (_atWindup ? $"ARMED {ArmedRamp * 100f:0}%" : "ARMED — swing!")
                              : (_atWindup ? $"winding {Mathf.Clamp01(_windupTimer / Mathf.Max(0.01f, armDelay)) * 100f:0}%" : "unarmed");
        GUI.Label(new Rect(12, 12, 560, 22),
            $"swingLookScale {swingLookScale:0.00}   [{mode}]   {armed}   edge {edge:0.0} m/s");
    }
}
