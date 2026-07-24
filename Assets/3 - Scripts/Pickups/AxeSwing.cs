using UnityEngine;

/// <summary>
/// Mouse-driven swing layer for the axe (M2 of the physics-axe spike — see
/// docs/2026-07-24-physics-axe-spike-handoff.md). Drives the "AxeSwingRig"
/// transform AxeController inserts between the AxeMotorRig and the AxePivot:
///
///   axeHoldPosition → AxeMotorRig (carry sway) → AxeSwingRig (this) → AxePivot (equip anim + rest offsets)
///
/// v4 — carried-hand model (v2 travel + v3 orientation discipline):
/// the mouse moves the HAND. While LMB is held the whole axe TRAVELS through
/// a big on-screen workspace with momentum — swing left/right and the entire
/// axe crosses the view (v3's fixed-base "metronome" is gone), push up to
/// raise it overhead, yank down to chop. Orientation stays disciplined:
/// the axe rides mostly upright, LEANS into its direction of travel
/// (velocity-derived, capped, self-zeroing at rest — never integrated, so it
/// cannot wind up), and the blade flips discretely: edge sideways when the
/// motion is horizontal (slash), edge forward-down when vertical (chop),
/// neutral at rest. Release LMB: a spring returns the hand to the sitting
/// point and everything settles.
///
/// All rotations pivot about the GRIP with (grip − R·grip) compensation —
/// the axe model carries large authoring-orientation offsets and naive rig
/// rotations orbit it around empty space (the discovered v3 bug).
///
/// While a swing is held, camera mouse-look is scaled by swingLookScale via
/// the PlayerController.SwingLookScale static hook (0 = camera locked).
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

    [Header("Hand workspace (camera-space metres, relative to the rest point)")]
    [Tooltip("Lower-left hand travel limit (x = left, y = down).")]
    public Vector2 workspaceMin = new Vector2(-0.50f, -0.35f);
    [Tooltip("Upper-right hand travel limit (x = right, y = up — raise for the overhead chop).")]
    public Vector2 workspaceMax = new Vector2(0.50f, 0.45f);

    [Header("Swing dynamics (the weight)")]
    [Tooltip("Hand velocity impulse (m/s) per unit of raw mouse delta. Higher = lighter axe.")]
    public float dragSensitivity = 1.1f;
    [Tooltip("Exponential decay (1/s) of hand velocity. Higher = heavier, less follow-through.")]
    public float gripDamping = 7f;
    [Tooltip("Hand speed cap (m/s).")]
    public float maxGripSpeed = 14f;
    [Tooltip("Spring stiffness returning the hand to rest when LMB is released.")]
    public float returnStiffness = 140f;
    [Tooltip("Damping for the return spring.")]
    public float returnDamping = 12f;

    [Header("Lean (orientation follows motion — derived, never integrated)")]
    [Tooltip("Degrees of lean into the travel direction per (m/s) of hand speed.")]
    public float leanFactor = 6f;
    [Tooltip("Cap (deg) on the lean. The axe stays readable — it tilts, it never tumbles.")]
    public float maxLean = 40f;
    [Tooltip("Forward pitch (deg) while LMB is held — squares the axe up for contact.")]
    public float slashPitch = 15f;
    [Tooltip("Small raise/pull-back of the rig while LMB is held (camera space).")]
    public Vector3 stancePositionOffset = new Vector3(0f, 0.02f, -0.02f);
    [Tooltip("Seconds to blend the stance lean in/out.")]
    public float stanceBlendTime = 0.12f;

    [Header("Blade facing (the flip)")]
    [Tooltip("Roll (deg) about the handle when the blade faces fully left/right for a horizontal slash. Vertical chops use neutral (edge forward-down, the axe's natural chop face).")]
    public float bladeFaceAngle = 90f;
    [Tooltip("Hand speed (m/s) above which the blade commits to the motion direction.")]
    public float flipThresholdSpeed = 1.8f;
    [Tooltip("How fast the blade flips (deg/s). Always through neutral, never the long way round.")]
    public float maxRollRate = 900f;
    [Tooltip("Local axis of the pivot the blade rolls around — the handle's long axis.")]
    public Vector3 rollAxis = Vector3.up;
    [Tooltip("Flip if the blade faces away from the swing instead of into it.")]
    public bool invertRoll = false;

    Transform _rig;                 // AxeSwingRig
    AxeController _axe;
    BladeSweep _sweep;

    Vector2 _hand, _handVelocity;   // camera-plane metres relative to rest, m/s
    float _roll;                    // deg — current blade facing
    float _stanceBlend;             // 0..1 stance blend
    bool _holding;

    public bool IsActive => _rig != null &&
        (_holding || _handVelocity.sqrMagnitude > 0.25f || _hand.sqrMagnitude > 0.0025f || Mathf.Abs(_roll) > 2f);
    public float HandSpeed => _handVelocity.magnitude;   // m/s, for readouts

    public void Attach(Transform rig, AxeController axe, BladeSweep sweep)
    {
        _rig = rig;
        _axe = axe;
        _sweep = sweep;
        _hand = _handVelocity = Vector2.zero;
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

        // Mouse moves the hand: right = +x, up = +y, one-to-one.
        if (_holding) _handVelocity += delta * dragSensitivity;
        if (_handVelocity.magnitude > maxGripSpeed) _handVelocity = _handVelocity.normalized * maxGripSpeed;

        // Integrate, substepped so 30fps and 144fps produce the same swing.
        const float maxStep = 1f / 120f;
        float remaining = Mathf.Min(dt, 0.1f);
        while (remaining > 0f)
        {
            float h = Mathf.Min(remaining, maxStep);
            if (!_holding)
                _handVelocity += (returnStiffness * -_hand - returnDamping * _handVelocity) * h;
            _handVelocity *= Mathf.Exp(-gripDamping * h);
            _hand += _handVelocity * h;
            remaining -= h;
        }

        // Hard workspace clamp — the end of your reach bleeds the velocity.
        Vector2 clamped = new Vector2(
            Mathf.Clamp(_hand.x, workspaceMin.x, workspaceMax.x),
            Mathf.Clamp(_hand.y, workspaceMin.y, workspaceMax.y));
        if (clamped.x != _hand.x) _handVelocity.x = 0f;
        if (clamped.y != _hand.y) _handVelocity.y = 0f;
        _hand = clamped;

        // Lean into the travel direction — pure function of velocity, so it
        // grows with the swing and dies to zero at rest on its own.
        float bankLean = Mathf.Clamp(_handVelocity.x * leanFactor, -maxLean, maxLean);   // sideways lean
        float pitchLean = Mathf.Clamp(-_handVelocity.y * leanFactor, -maxLean, maxLean); // down-travel → lean forward

        // Blade facing by dominant motion axis: horizontal slash → edge sideways;
        // vertical chop → neutral (the axe's natural forward-down chop face).
        float rollTarget;
        float speed = _handVelocity.magnitude;
        if (speed > flipThresholdSpeed)
        {
            bool horizontal = Mathf.Abs(_handVelocity.x) > Mathf.Abs(_handVelocity.y);
            rollTarget = horizontal ? Mathf.Sign(_handVelocity.x) * bladeFaceAngle * (invertRoll ? -1f : 1f) : 0f;
        }
        else if (!_holding) rollTarget = 0f;
        else rollTarget = _roll;   // slow moment mid-hold: keep facing until the next stroke commits
        _roll = Mathf.MoveTowards(_roll, rollTarget, maxRollRate * dt);

        _stanceBlend = Mathf.MoveTowards(_stanceBlend, _holding ? 1f : 0f, dt / Mathf.Max(0.01f, stanceBlendTime));

        // Compose. AngleAxis(+deg, forward) leans the top LEFT, so bank is negated.
        Quaternion swingRot =
            Quaternion.AngleAxis(-bankLean, Vector3.forward)
            * Quaternion.Euler(slashPitch * _stanceBlend + pitchLean, 0f, 0f)
            * Quaternion.AngleAxis(_roll, rollAxis.normalized);

        // Rotate about the GRIP (holdPositionOffset), not the rig origin — the
        // model's authoring offsets put the visual axe ~0.6m from the origin.
        Vector3 gripPoint = _axe != null ? _axe.holdPositionOffset : Vector3.zero;
        _rig.localRotation = swingRot;
        _rig.localPosition = new Vector3(_hand.x, _hand.y, 0f)
                           + stancePositionOffset * _stanceBlend
                           + (gripPoint - swingRot * gripPoint);

        // Blade sweep runs after the pose is final so casts see this frame's edge path.
        if (_sweep != null) _sweep.Tick(dt, IsActive);
    }

    void OnGUI()
    {
        if (!showDebugReadout || _rig == null) return;
        float edge = _sweep != null ? _sweep.LastEdgeSpeed : 0f;
        GUI.Label(new Rect(12, 12, 520, 22),
            $"swingLookScale {swingLookScale:0.00}   hand {HandSpeed:0.0} m/s   edge {edge:0.0} m/s" +
            (_sweep != null ? $"   (gate {_sweep.minEdgeSpeed:0.0})" : ""));
    }
}
