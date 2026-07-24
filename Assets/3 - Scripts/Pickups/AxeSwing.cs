using UnityEngine;

/// <summary>
/// Mouse-driven swing layer for the axe (M2 of the physics-axe spike — see
/// docs/2026-07-24-physics-axe-spike-handoff.md). Drives the "AxeSwingRig"
/// transform AxeController inserts between the AxeMotorRig and the AxePivot:
///
///   axeHoldPosition → AxeMotorRig (carry sway) → AxeSwingRig (this) → AxePivot (equip anim + rest offsets)
///
/// LMB down: chop stance — the axe raises and cocks away from the recent
/// mouse-motion direction (default diagonal ready pose if the mouse is still).
/// While held: raw mouse deltas become angular impulses on a virtual-mass
/// swing arc; a fast flick is a hard hit, a slow drag is nothing. Release
/// mid-swing: the arc keeps its momentum and decays (follow-through). The
/// blade auto-rolls about the handle axis so the edge leads the motion.
///
/// While a swing is held, camera mouse-look is scaled by swingLookScale via
/// the PlayerController.SwingLookScale static hook — THE central tunable of
/// the spike (0 = camera locked/committed, 1 = camera turns with the swing).
///
/// Kinematic and camera-local throughout: no Rigidbody, framerate-independent
/// (impulses are per-pixel not per-frame; decay/integration substepped).
/// </summary>
[DefaultExecutionOrder(10)]   // after AxeMotor (0) so BladeSweep reads a settled world pose
public class AxeSwing : MonoBehaviour
{
    [Header("The central tunable — camera vs. axe input split")]
    [Tooltip("While LMB is held: how much the camera still turns with the mouse. 0 = locked (committed), 1 = full turn (view-drag). START HERE when judging the prototype.")]
    [Range(0f, 1f)] public float swingLookScale = 0.25f;
    [Tooltip("Spike-build on-screen readout of swingLookScale + edge speed. Turn off when the verdict is in.")]
    public bool showDebugReadout = true;

    [Header("Swing dynamics")]
    [Tooltip("Angular impulse (deg/s of arc velocity) per unit of raw mouse delta. Higher = lighter axe.")]
    public float swingSensitivity = 35f;
    [Tooltip("Exponential decay rate (1/s) of arc velocity — the virtual damping. Higher = heavier, shorter follow-through.")]
    public float swingDamping = 5f;
    [Tooltip("Arc soft limit (degrees) in each direction. Past this the arc stops and bleeds velocity.")]
    public float maxArcAngle = 110f;
    [Tooltip("Spring stiffness pulling the arc back to rest when LMB is not held.")]
    public float returnStiffness = 60f;
    [Tooltip("Damping for the return spring.")]
    public float returnDamping = 12f;

    [Header("Chop stance")]
    [Tooltip("Degrees the axe cocks away from the predicted swing direction on LMB down.")]
    public float cockAngle = 35f;
    [Tooltip("Spring stiffness pulling the arc into the stance pose while ready (before the swing starts).")]
    public float stanceStiffness = 140f;
    [Tooltip("Cumulative raw mouse delta that flips READY → SWINGING (integration takes over).")]
    public float swingStartThreshold = 1.5f;
    [Tooltip("Local position offset of the raised stance (camera space, blended in over stanceBlendTime).")]
    public Vector3 stancePositionOffset = new Vector3(0.03f, 0.06f, -0.04f);
    [Tooltip("Seconds to blend the stance raise in/out.")]
    public float stanceBlendTime = 0.12f;

    [Header("Blade auto-orient")]
    [Tooltip("Local axis of the pivot the blade rolls around so the edge leads the swing — the handle's long axis.")]
    public Vector3 rollAxis = Vector3.up;
    [Tooltip("Flip if the edge trails instead of leads after checking in-game.")]
    public bool invertRoll = false;
    [Tooltip("Max roll rate (deg/s) — rate-limited so the blade never snaps.")]
    public float maxRollRate = 720f;
    [Tooltip("Arc speed (deg/s) below which the blade keeps its current roll instead of chasing noise.")]
    public float rollSpeedThreshold = 60f;

    Transform _rig;                 // AxeSwingRig
    AxeController _axe;
    BladeSweep _sweep;

    // Arc state: x = pitch (deg, + = swinging down), y = yaw (deg, + = swinging right).
    Vector2 _arc, _arcVelocity;
    float _roll;                    // current blade roll (deg) about rollAxis
    float _stanceBlend;             // 0..1 raised-stance blend
    bool _holding;
    bool _swinging;                 // false = READY (stance spring), true = integrating mouse
    float _heldMouseAccum;          // |delta| accumulated since LMB down
    Vector2 _recentMouseDir;        // smoothed mouse direction for stance prediction

    public bool IsActive => _rig != null && (_holding || _arcVelocity.sqrMagnitude > 25f || _arc.sqrMagnitude > 4f);
    public float ArcSpeed => _arcVelocity.magnitude;   // deg/s, for readouts

    public void Attach(Transform rig, AxeController axe, BladeSweep sweep)
    {
        _rig = rig;
        _axe = axe;
        _sweep = sweep;
        _arc = _arcVelocity = Vector2.zero;
        _roll = 0f;
        _stanceBlend = 0f;
        _holding = _swinging = false;
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

        // Smoothed direction memory for the stance prediction (runs even while idle).
        if (delta.sqrMagnitude > 0.0001f)
            _recentMouseDir = Vector2.Lerp(_recentMouseDir, delta.normalized, 1f - Mathf.Exp(-8f * dt));

        bool allowed = _axe != null && _axe.PhysicsSwingAllowed;
        bool lmb = Input.GetMouseButton(0) && allowed;

        if (lmb && !_holding)
        {
            _holding = true;
            _swinging = false;
            _heldMouseAccum = 0f;
        }
        else if (!lmb && _holding)
        {
            _holding = false;      // release: follow-through — velocity carries on below
            _swinging = false;
        }

        PlayerController.SwingLookScale = _holding ? swingLookScale : 1f;

        if (_holding)
        {
            _heldMouseAccum += delta.magnitude;
            if (!_swinging && _heldMouseAccum >= swingStartThreshold) _swinging = true;

            if (_swinging)
            {
                // Mouse X drives yaw, mouse Y drives pitch (screen-up = axe up = negative pitch).
                _arcVelocity.x += -delta.y * swingSensitivity;
                _arcVelocity.y +=  delta.x * swingSensitivity;
            }
            else
            {
                // READY: spring toward the cocked stance — opposite the predicted swing.
                // Default (mouse still): up-right ready pose, primed for a down-left chop.
                Vector2 dir = _recentMouseDir.sqrMagnitude > 0.01f ? _recentMouseDir
                                                                   : new Vector2(-0.5f, -0.7f).normalized;
                SpringToward(ref _arc, ref _arcVelocity, ComputeStance(dir), stanceStiffness, 2f * Mathf.Sqrt(stanceStiffness), dt);
            }
        }

        if (!_holding || _swinging)
        {
            // Integrate the arc with exponential damping (frame-rate independent),
            // plus the return spring once the button is up.
            const float maxStep = 1f / 120f;
            float remaining = Mathf.Min(dt, 0.1f);
            while (remaining > 0f)
            {
                float h = Mathf.Min(remaining, maxStep);
                if (!_holding)
                    _arcVelocity += (returnStiffness * -_arc - returnDamping * _arcVelocity) * h;
                _arcVelocity *= Mathf.Exp(-swingDamping * h);
                _arc += _arcVelocity * h;
                remaining -= h;
            }
        }

        // Soft arc limits: clamp and bleed outward velocity.
        if (Mathf.Abs(_arc.x) > maxArcAngle) { _arc.x = Mathf.Sign(_arc.x) * maxArcAngle; if (Mathf.Sign(_arcVelocity.x) == Mathf.Sign(_arc.x)) _arcVelocity.x = 0f; }
        if (Mathf.Abs(_arc.y) > maxArcAngle) { _arc.y = Mathf.Sign(_arc.y) * maxArcAngle; if (Mathf.Sign(_arcVelocity.y) == Mathf.Sign(_arc.y)) _arcVelocity.y = 0f; }

        // Blade auto-orient: roll toward the instantaneous swing direction so the
        // edge leads. atan2(yawRate, pitchRate): straight down-chop → 0° roll,
        // pure horizontal → ±90°. Rate-limited; holds its roll at low speed.
        if (_arcVelocity.magnitude > rollSpeedThreshold)
        {
            float target = Mathf.Atan2(_arcVelocity.y, _arcVelocity.x) * Mathf.Rad2Deg;
            if (invertRoll) target = -target;
            _roll = Mathf.MoveTowardsAngle(_roll, target, maxRollRate * dt);
        }

        // Stance raise blend.
        float blendTarget = _holding ? 1f : 0f;
        _stanceBlend = Mathf.MoveTowards(_stanceBlend, blendTarget, dt / Mathf.Max(0.01f, stanceBlendTime));

        _rig.localRotation = Quaternion.Euler(_arc.x, _arc.y, 0f) * Quaternion.AngleAxis(_roll, rollAxis.normalized);
        _rig.localPosition = stancePositionOffset * _stanceBlend;

        // Blade sweep runs after the pose is final so casts see this frame's edge path.
        if (_sweep != null) _sweep.Tick(dt, IsActive);
    }

    // Cock away from the predicted screen-space swing direction. dir x = right,
    // y = up. Arc x = pitch (+down), arc y = yaw (+right). Swinging down-left
    // (dir = (−,−)) should cock up-right: pitch −, yaw +  →  pitch = dir.y*cock,
    // yaw = −dir.x*cock.
    Vector2 ComputeStance(Vector2 dir) => new Vector2(dir.y * cockAngle, -dir.x * cockAngle);

    static void SpringToward(ref Vector2 value, ref Vector2 velocity, Vector2 target, float stiffness, float damping, float dt)
    {
        const float maxStep = 1f / 120f;
        dt = Mathf.Min(dt, 0.1f);
        while (dt > 0f)
        {
            float h = Mathf.Min(dt, maxStep);
            velocity += (stiffness * (target - value) - damping * velocity) * h;
            value += velocity * h;
            dt -= h;
        }
    }

    void OnGUI()
    {
        if (!showDebugReadout || _rig == null) return;
        float edge = _sweep != null ? _sweep.LastEdgeSpeed : 0f;
        GUI.Label(new Rect(12, 12, 460, 22),
            $"swingLookScale {swingLookScale:0.00}   arc {ArcSpeed:0}°/s   edge {edge:0.0} m/s" +
            (_sweep != null ? $"   (gate {_sweep.minEdgeSpeed:0.0})" : ""));
    }
}
