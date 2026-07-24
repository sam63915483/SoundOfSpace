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

    [Header("Horizontal SLASH (axe lays flat and sweeps like a scythe)")]
    [Tooltip("How far the axe lays down for a side swing (deg pitch forward from vertical). ~90 = fully horizontal, pointing at the tree.")]
    public float slashLayPitch = 78f;
    [Tooltip("Yaw arc half-width (deg): the laid-out axe sweeps between -this and +this across the view.")]
    public float slashYawRange = 65f;
    [Tooltip("Swing-progress impulse per unit of raw mouse X. Higher = lighter, faster to cross the arc.")]
    public float slashSensitivity = 0.55f;
    [Tooltip("Exponential decay (1/s) of slash momentum — the weight.")]
    public float slashDamping = 5f;
    [Tooltip("Flip if mouse-right sweeps the axe left.")]
    public bool invertSwing = false;
    [Tooltip("Sideways hand travel (m) at full slash extent — carries the swing across the screen.")]
    public float slashHandTravel = 0.28f;

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

    [Header("Blade facing (slash edge flip)")]
    [Tooltip("Roll (deg) about the handle so the edge leads a sideways sweep.")]
    public float bladeFaceAngle = 90f;
    [Tooltip("Slash momentum (progress/s) above which the edge commits to the motion direction.")]
    public float flipThresholdSpeed = 1.2f;
    [Tooltip("How fast the blade flips (deg/s). Always through neutral.")]
    public float maxRollRate = 900f;
    [Tooltip("Local axis of the pivot the blade rolls around — the handle's long axis.")]
    public Vector3 rollAxis = Vector3.up;
    [Tooltip("Flip if the edge trails instead of leads.")]
    public bool invertRoll = false;

    Transform _rig;                 // AxeSwingRig
    AxeController _axe;
    BladeSweep _sweep;

    // 1D swing progress per mode, each in [-1, +1], with momentum.
    float _slash, _slashVelocity;   // -1 = full left, +1 = full right
    float _chop, _chopVelocity;     // -1 = full cock (up/back), +1 = full drive (down/through)
    float _slashBlend;              // 0 = chop/carry pose family, 1 = laid-out slash pose
    float _roll;                    // deg — current edge facing (slash only)
    float _emaX, _emaY;             // recent |mouse| per axis, for mode dominance
    bool _holding;
    bool _slashMode;

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
        _slashBlend = _roll = _emaX = _emaY = 0f;
        _holding = _slashMode = false;
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
        Vector2 delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        bool allowed = _axe != null && _axe.PhysicsSwingAllowed;
        _holding = Input.GetMouseButton(0) && allowed;
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

        // Edge facing (slash only): lead the sweep; neutral otherwise.
        float rollTarget;
        if (_slashBlend > 0.3f && Mathf.Abs(_slashVelocity) > flipThresholdSpeed)
            rollTarget = Mathf.Sign(_slashVelocity) * bladeFaceAngle * (invertRoll ? -1f : 1f);
        else if (_slashBlend > 0.3f && _holding)
            rollTarget = _roll;                    // hold facing through the reversal
        else
            rollTarget = 0f;
        _roll = Mathf.MoveTowards(_roll, rollTarget, maxRollRate * dt);

        // SLASH pose: lay the axe flat (pitch forward), then sweep the laid axe
        // about camera-up, edge roll innermost about the handle.
        Quaternion slashRot =
            Quaternion.AngleAxis(_slash * slashYawRange, Vector3.up)
            * Quaternion.AngleAxis(slashLayPitch, Vector3.right)
            * Quaternion.AngleAxis(_roll, rollAxis.normalized);

        // CHOP pose: pitch arc through the upright rest — piecewise so _chop = 0
        // is exactly the untouched carry pose: -1 → chopCockPitch (raised/back),
        // +1 → chopDrivePitch (driven down/through).
        float chopPitch = _chop < 0f ? -_chop * chopCockPitch : _chop * chopDrivePitch;
        Quaternion chopRot = Quaternion.AngleAxis(chopPitch, Vector3.right);

        Quaternion swingRot = Quaternion.Slerp(chopRot, slashRot, _slashBlend);

        // Hand travel: carries the swing without stealing the show.
        Vector3 slashPos = new Vector3(_slash * slashHandTravel, 0.04f, 0f);
        Vector3 chopPos = new Vector3(0f, _chop < 0f ? -_chop * chopHandRise : -_chop * chopHandRise * 0.4f, 0f);
        Vector3 handPos = Vector3.Lerp(chopPos, slashPos, _slashBlend);

        // Rotate about the GRIP (holdPositionOffset), not the rig origin.
        Vector3 gripPoint = _axe != null ? _axe.holdPositionOffset : Vector3.zero;
        _rig.localRotation = swingRot;
        _rig.localPosition = handPos + (gripPoint - swingRot * gripPoint);

        // Blade sweep runs after the pose is final so casts see this frame's edge path.
        if (_sweep != null) _sweep.Tick(dt, IsActive);
    }

    void OnGUI()
    {
        if (!showDebugReadout || _rig == null) return;
        float edge = _sweep != null ? _sweep.LastEdgeSpeed : 0f;
        string mode = _holding ? (_slashMode ? "SLASH" : "CHOP") : "carry";
        GUI.Label(new Rect(12, 12, 560, 22),
            $"swingLookScale {swingLookScale:0.00}   [{mode}]   edge {edge:0.0} m/s" +
            (_sweep != null ? $"   (gate {_sweep.minEdgeSpeed:0.0})" : ""));
    }
}
