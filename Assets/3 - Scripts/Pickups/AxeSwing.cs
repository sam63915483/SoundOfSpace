using UnityEngine;

/// <summary>
/// Mouse-driven swing layer for the axe (M2 of the physics-axe spike — see
/// docs/2026-07-24-physics-axe-spike-handoff.md). Drives the "AxeSwingRig"
/// transform AxeController inserts between the AxeMotorRig and the AxePivot:
///
///   axeHoldPosition → AxeMotorRig (carry sway) → AxeSwingRig (this) → AxePivot (equip anim + rest offsets)
///
/// v3 — the wiper model (Sam's spec): hold LMB and move the mouse left/right;
/// the axe sweeps side to side across the view like a windshield wiper, with
/// momentum, and the BLADE FLIPS to face whichever way it's swinging — walk
/// up to a tree and slash left-right-left-right. Exactly three bounded
/// degrees of freedom, so it cannot get "all messed up":
///
///   1. Wiper angle (mouse X, momentum + damping, hard-clamped arc)
///   2. Small self-centering vertical wiggle (mouse Y)
///   3. Blade facing: left / neutral / right, always flipping through neutral
///
/// Release LMB: the arc springs back to centre, the blade returns to
/// neutral, the stance lean fades — back to the regular sitting point.
///
/// While a swing is held, camera mouse-look is scaled by swingLookScale via
/// the PlayerController.SwingLookScale static hook (0 = camera locked).
/// Kinematic, camera-local, framerate-independent (per-pixel impulses,
/// substepped integration). Earlier free-form models are in git history
/// (v1 unbounded arcs, v2 2D drag workspace).
/// </summary>
[DefaultExecutionOrder(10)]   // after AxeMotor (0) so BladeSweep reads a settled world pose
public class AxeSwing : MonoBehaviour
{
    [Header("The central tunable — camera vs. axe input split")]
    [Tooltip("While LMB is held: how much the camera still turns with the mouse. 0 = locked (committed), 1 = full turn (view-drag).")]
    [Range(0f, 1f)] public float swingLookScale = 0.25f;
    [Tooltip("Spike-build on-screen readout. Turn off when the verdict is in.")]
    public bool showDebugReadout = true;

    [Header("Wiper swing (mouse X)")]
    [Tooltip("Arc half-width (deg): the axe sweeps between -this and +this.")]
    public float maxSwingAngle = 60f;
    [Tooltip("Swing velocity impulse (deg/s) per unit of raw mouse X. Higher = lighter axe.")]
    public float swingSensitivity = 45f;
    [Tooltip("Exponential decay (1/s) of swing velocity — the weight. Higher = heavier, less follow-through.")]
    public float swingDamping = 6f;
    [Tooltip("Flip if mouse-right swings the axe left.")]
    public bool invertSwing = false;
    [Tooltip("Spring stiffness returning the arc to centre when LMB is released.")]
    public float returnStiffness = 120f;
    [Tooltip("Damping for the return spring.")]
    public float returnDamping = 14f;

    [Header("Vertical wiggle (mouse Y — deliberately restrictive)")]
    [Tooltip("Max up/down head tilt (deg) from mouse Y. Small on purpose; it self-centres.")]
    public float maxPitchOffset = 18f;
    [Tooltip("Pitch velocity impulse (deg/s) per unit of raw mouse Y.")]
    public float pitchSensitivity = 22f;
    [Tooltip("Self-centering spring stiffness on the vertical wiggle (always active).")]
    public float pitchCenterStiffness = 60f;
    [Tooltip("Damping (1/s) on the vertical wiggle.")]
    public float pitchDamping = 9f;

    [Header("Slash stance (while LMB held)")]
    [Tooltip("Forward lean (deg) into the slash while LMB is held — squares the axe up to the tree.")]
    public float slashPitch = 30f;
    [Tooltip("Small raise/pull-back of the rig while LMB is held (camera space).")]
    public Vector3 stancePositionOffset = new Vector3(0f, 0.02f, -0.02f);
    [Tooltip("Seconds to blend the stance lean in/out.")]
    public float stanceBlendTime = 0.12f;
    [Tooltip("Metres of sideways grip shift per degree of wiper angle — carries the axe into the swing.")]
    public float lateralShiftPerDeg = 0.0012f;

    [Header("Blade facing (the flip)")]
    [Tooltip("Roll (deg) about the handle when the blade faces fully left/right.")]
    public float bladeFaceAngle = 90f;
    [Tooltip("Swing speed (deg/s) above which the blade commits to facing the motion direction.")]
    public float flipThresholdSpeed = 120f;
    [Tooltip("How fast the blade flips (deg/s). It always passes through neutral, never the long way round.")]
    public float maxRollRate = 900f;
    [Tooltip("Local axis of the pivot the blade rolls around — the handle's long axis.")]
    public Vector3 rollAxis = Vector3.up;
    [Tooltip("Flip if the blade faces away from the swing instead of into it.")]
    public bool invertRoll = false;

    Transform _rig;                 // AxeSwingRig
    AxeController _axe;
    BladeSweep _sweep;

    float _wiper, _wiperVelocity;   // deg, deg/s — the side-to-side arc
    float _pitch, _pitchVelocity;   // deg, deg/s — the vertical wiggle
    float _roll;                    // deg — current blade facing
    float _stanceBlend;             // 0..1 slash-stance blend
    bool _holding;

    public bool IsActive => _rig != null &&
        (_holding || Mathf.Abs(_wiperVelocity) > 30f || Mathf.Abs(_wiper) > 2f || Mathf.Abs(_roll) > 2f);
    public float SwingSpeed => Mathf.Abs(_wiperVelocity);   // deg/s, for readouts

    public void Attach(Transform rig, AxeController axe, BladeSweep sweep)
    {
        _rig = rig;
        _axe = axe;
        _sweep = sweep;
        _wiper = _wiperVelocity = 0f;
        _pitch = _pitchVelocity = 0f;
        _roll = 0f;
        _stanceBlend = 0f;
        _holding = false;
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

        if (_holding)
        {
            float x = invertSwing ? -delta.x : delta.x;
            _wiperVelocity += x * swingSensitivity;
            _pitchVelocity += -delta.y * pitchSensitivity;   // mouse up = head up (pitch is +down)
        }

        // Integrate, substepped so 30fps and 144fps produce the same swing.
        const float maxStep = 1f / 120f;
        float remaining = Mathf.Min(dt, 0.1f);
        while (remaining > 0f)
        {
            float h = Mathf.Min(remaining, maxStep);

            if (!_holding)
                _wiperVelocity += (returnStiffness * -_wiper - returnDamping * _wiperVelocity) * h;
            _wiperVelocity *= Mathf.Exp(-swingDamping * h);
            _wiper += _wiperVelocity * h;

            _pitchVelocity += (pitchCenterStiffness * -_pitch) * h;   // always self-centres
            _pitchVelocity *= Mathf.Exp(-pitchDamping * h);
            _pitch += _pitchVelocity * h;

            remaining -= h;
        }

        // Hard arc limits — hitting the end of the arc bleeds the velocity.
        if (Mathf.Abs(_wiper) > maxSwingAngle)
        {
            _wiper = Mathf.Sign(_wiper) * maxSwingAngle;
            if (Mathf.Sign(_wiperVelocity) == Mathf.Sign(_wiper)) _wiperVelocity = 0f;
        }
        if (Mathf.Abs(_pitch) > maxPitchOffset)
        {
            _pitch = Mathf.Sign(_pitch) * maxPitchOffset;
            if (Mathf.Sign(_pitchVelocity) == Mathf.Sign(_pitch)) _pitchVelocity = 0f;
        }

        // Blade facing: commit left/right with the swing, hold through the
        // reversal dead-zone, return to neutral at rest. Linear MoveTowards so
        // the flip always passes through neutral — never the long way round.
        float rollTarget;
        if (Mathf.Abs(_wiperVelocity) > flipThresholdSpeed)
            rollTarget = Mathf.Sign(_wiperVelocity) * bladeFaceAngle * (invertRoll ? -1f : 1f);
        else if (!_holding)
            rollTarget = 0f;
        else
            rollTarget = _roll;   // between strokes: hold facing until the next stroke commits
        _roll = Mathf.MoveTowards(_roll, rollTarget, maxRollRate * dt);

        // Slash-stance lean.
        _stanceBlend = Mathf.MoveTowards(_stanceBlend, _holding ? 1f : 0f, dt / Mathf.Max(0.01f, stanceBlendTime));

        // Compose: wiper tilt in the view plane (about camera forward), then the
        // stance lean + vertical wiggle, then the blade roll about the handle.
        _rig.localRotation =
            Quaternion.AngleAxis(-_wiper, Vector3.forward)
            * Quaternion.Euler(slashPitch * _stanceBlend + _pitch, 0f, 0f)
            * Quaternion.AngleAxis(_roll, rollAxis.normalized);
        _rig.localPosition = new Vector3(_wiper * lateralShiftPerDeg, 0f, 0f) + stancePositionOffset * _stanceBlend;

        // Blade sweep runs after the pose is final so casts see this frame's edge path.
        if (_sweep != null) _sweep.Tick(dt, IsActive);
    }

    void OnGUI()
    {
        if (!showDebugReadout || _rig == null) return;
        float edge = _sweep != null ? _sweep.LastEdgeSpeed : 0f;
        GUI.Label(new Rect(12, 12, 520, 22),
            $"swingLookScale {swingLookScale:0.00}   swing {SwingSpeed:0}°/s   edge {edge:0.0} m/s" +
            (_sweep != null ? $"   (gate {_sweep.minEdgeSpeed:0.0})" : ""));
    }
}
