using UnityEngine;

/// <summary>
/// Mouse-driven swing layer for the axe (M2 of the physics-axe spike — see
/// docs/2026-07-24-physics-axe-spike-handoff.md). Drives the "AxeSwingRig"
/// transform AxeController inserts between the AxeMotorRig and the AxePivot:
///
///   axeHoldPosition → AxeMotorRig (carry sway) → AxeSwingRig (this) → AxePivot (equip anim + rest offsets)
///
/// Half Sword-style model (v2 — v1 integrated unbounded arc angles and swung
/// the axe off screen): the mouse drags a GRIP POINT with momentum inside a
/// clamped workspace on a plane in front of the camera, so the axe can never
/// leave the view. Orientation is DERIVED from where the grip sits in the
/// workspace (position → arc angle) plus how fast it's moving (velocity →
/// lead tilt + blade roll), so angles can't run away either. Weight comes
/// from the virtual momentum: a fast flick whips the head through the
/// workspace, a slow drag just carries it.
///
/// LMB down: grip springs to a cocked corner opposite the recent mouse
/// motion (up-right default). While held: mouse deltas are velocity impulses
/// on the grip. Release: momentum follows through, then a spring returns the
/// grip to rest. The blade auto-rolls about the handle axis so the edge
/// leads the motion.
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
    [Tooltip("Spike-build on-screen readout of swingLookScale + grip/edge speed. Turn off when the verdict is in.")]
    public bool showDebugReadout = true;

    [Header("Workspace (camera-space metres, relative to the hold position — keeps the axe ON SCREEN)")]
    [Tooltip("Lower-left grip travel limit. X is negative = toward/past screen centre for cross-body chops.")]
    public Vector2 workspaceMin = new Vector2(-0.45f, -0.30f);
    [Tooltip("Upper-right grip travel limit.")]
    public Vector2 workspaceMax = new Vector2(0.25f, 0.35f);

    [Header("Swing dynamics (the weight)")]
    [Tooltip("Grip velocity impulse (m/s) per unit of raw mouse delta. Higher = lighter axe.")]
    public float dragSensitivity = 0.9f;
    [Tooltip("Exponential decay rate (1/s) of grip velocity — the virtual damping. Higher = heavier, shorter follow-through.")]
    public float gripDamping = 8f;
    [Tooltip("Grip speed cap (m/s).")]
    public float maxGripSpeed = 12f;
    [Tooltip("Spring stiffness returning the grip to rest when LMB is not held.")]
    public float returnStiffness = 140f;
    [Tooltip("Damping for the return spring.")]
    public float returnDamping = 10f;

    [Header("Orientation mapping (bounded by construction)")]
    [Tooltip("Yaw (deg) when the grip is at the horizontal workspace edge.")]
    public float maxYawAngle = 60f;
    [Tooltip("Pitch (deg) when the grip is at the vertical workspace edge. Grip down = head chops forward/down.")]
    public float maxPitchAngle = 70f;
    [Tooltip("Extra tilt (deg) per m/s of grip velocity — the head leading the motion.")]
    public float leadTiltFactor = 3.5f;
    [Tooltip("Cap (deg) on the velocity lead tilt.")]
    public float maxLeadTilt = 25f;

    [Header("Chop stance")]
    [Tooltip("Metres the grip cocks away from the predicted swing direction on LMB down.")]
    public float cockDistance = 0.18f;
    [Tooltip("Spring stiffness pulling the grip into the stance while ready (before the swing starts).")]
    public float stanceStiffness = 140f;
    [Tooltip("Cumulative raw mouse delta that flips READY → SWINGING (integration takes over).")]
    public float swingStartThreshold = 1.5f;
    [Tooltip("Small raise/pull-back of the whole rig while LMB is held (camera space).")]
    public Vector3 stancePositionOffset = new Vector3(0.02f, 0.03f, -0.03f);
    [Tooltip("Seconds to blend the stance raise in/out.")]
    public float stanceBlendTime = 0.12f;

    [Header("Blade auto-orient")]
    [Tooltip("Local axis of the pivot the blade rolls around so the edge leads the swing — the handle's long axis.")]
    public Vector3 rollAxis = Vector3.up;
    [Tooltip("Flip if the edge trails instead of leads after checking in-game.")]
    public bool invertRoll = false;
    [Tooltip("Max roll rate (deg/s) — rate-limited so the blade never snaps.")]
    public float maxRollRate = 720f;
    [Tooltip("Grip speed (m/s) below which the blade keeps its current roll instead of chasing noise.")]
    public float rollSpeedThreshold = 1.5f;
    [Tooltip("Deg/s the blade rolls back to neutral after release, once the swing has slowed.")]
    public float rollReturnRate = 360f;

    Transform _rig;                 // AxeSwingRig
    AxeController _axe;
    BladeSweep _sweep;

    // Grip state: camera-plane metres relative to the hold position.
    Vector2 _grip, _gripVelocity;
    float _roll;                    // current blade roll (deg) about rollAxis
    float _stanceBlend;             // 0..1 raised-stance blend
    bool _holding;
    bool _swinging;                 // false = READY (stance spring), true = mouse drives the grip
    float _heldMouseAccum;          // |delta| accumulated since LMB down
    Vector2 _recentMouseDir;        // smoothed mouse direction for stance prediction

    public bool IsActive => _rig != null && (_holding || _gripVelocity.sqrMagnitude > 0.25f || _grip.sqrMagnitude > 0.0025f);
    public float GripSpeed => _gripVelocity.magnitude;   // m/s, for readouts

    public void Attach(Transform rig, AxeController axe, BladeSweep sweep)
    {
        _rig = rig;
        _axe = axe;
        _sweep = sweep;
        _grip = _gripVelocity = Vector2.zero;
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
            _holding = false;      // release: momentum follows through below
            _swinging = false;
        }

        PlayerController.SwingLookScale = _holding ? swingLookScale : 1f;

        if (_holding)
        {
            _heldMouseAccum += delta.magnitude;
            if (!_swinging && _heldMouseAccum >= swingStartThreshold) _swinging = true;

            if (_swinging)
            {
                // Mouse drags the grip: right = +x, up = +y, one-to-one.
                _gripVelocity += delta * dragSensitivity;
            }
            else
            {
                // READY: spring toward the cocked stance — opposite the predicted swing.
                // Default (mouse still): up-right ready pose, primed for a down-left chop.
                Vector2 dir = _recentMouseDir.sqrMagnitude > 0.01f ? _recentMouseDir
                                                                   : new Vector2(-0.5f, -0.7f).normalized;
                Vector2 stance = ClampToWorkspace(-dir * cockDistance);
                SpringToward(ref _grip, ref _gripVelocity, stance, stanceStiffness, 2f * Mathf.Sqrt(stanceStiffness), dt);
            }
        }

        if (_gripVelocity.magnitude > maxGripSpeed) _gripVelocity = _gripVelocity.normalized * maxGripSpeed;

        if (!_holding || _swinging)
        {
            // Integrate with exponential damping (framerate-independent), plus the
            // return spring once the button is up.
            const float maxStep = 1f / 120f;
            float remaining = Mathf.Min(dt, 0.1f);
            while (remaining > 0f)
            {
                float h = Mathf.Min(remaining, maxStep);
                if (!_holding)
                    _gripVelocity += (returnStiffness * -_grip - returnDamping * _gripVelocity) * h;
                _gripVelocity *= Mathf.Exp(-gripDamping * h);
                _grip += _gripVelocity * h;
                remaining -= h;
            }
        }

        // Hard workspace clamp: the grip cannot leave the on-screen volume.
        // Hitting an edge bleeds the outward velocity — that IS the end of the arc.
        Vector2 clamped = ClampToWorkspace(_grip);
        if (clamped.x != _grip.x) _gripVelocity.x = 0f;
        if (clamped.y != _grip.y) _gripVelocity.y = 0f;
        _grip = clamped;

        // Position → arc angles (normalized per-side so asymmetric workspaces map cleanly).
        float nx = _grip.x >= 0f ? _grip.x / Mathf.Max(0.001f, workspaceMax.x) : -_grip.x / Mathf.Min(-0.001f, workspaceMin.x);
        float ny = _grip.y >= 0f ? _grip.y / Mathf.Max(0.001f, workspaceMax.y) : -_grip.y / Mathf.Min(-0.001f, workspaceMin.y);
        float yaw = nx * maxYawAngle;
        float pitch = -ny * maxPitchAngle;                       // grip down → head chops forward/down

        // Velocity → lead tilt: the head leans into the motion.
        float yawLead = Mathf.Clamp(_gripVelocity.x * leadTiltFactor, -maxLeadTilt, maxLeadTilt);
        float pitchLead = Mathf.Clamp(-_gripVelocity.y * leadTiltFactor, -maxLeadTilt, maxLeadTilt);

        // Blade auto-orient: roll toward the motion direction so the edge leads.
        // Straight down-chop → 0° roll, pure horizontal → ±90°. Rate-limited.
        if (_gripVelocity.magnitude > rollSpeedThreshold)
        {
            float target = Mathf.Atan2(_gripVelocity.x, -_gripVelocity.y) * Mathf.Rad2Deg;
            if (invertRoll) target = -target;
            _roll = Mathf.MoveTowardsAngle(_roll, target, maxRollRate * dt);
        }
        else if (!_holding)
        {
            // Released and slowed down: ease the blade back to its neutral roll
            // so the axe settles into the regular sitting pose, not mid-twist.
            _roll = Mathf.MoveTowardsAngle(_roll, 0f, rollReturnRate * dt);
        }

        // Stance raise blend.
        float blendTarget = _holding ? 1f : 0f;
        _stanceBlend = Mathf.MoveTowards(_stanceBlend, blendTarget, dt / Mathf.Max(0.01f, stanceBlendTime));

        _rig.localRotation = Quaternion.Euler(pitch + pitchLead, yaw + yawLead, 0f) * Quaternion.AngleAxis(_roll, rollAxis.normalized);
        _rig.localPosition = new Vector3(_grip.x, _grip.y, 0f) + stancePositionOffset * _stanceBlend;

        // Blade sweep runs after the pose is final so casts see this frame's edge path.
        if (_sweep != null) _sweep.Tick(dt, IsActive);
    }

    Vector2 ClampToWorkspace(Vector2 p) => new Vector2(
        Mathf.Clamp(p.x, workspaceMin.x, workspaceMax.x),
        Mathf.Clamp(p.y, workspaceMin.y, workspaceMax.y));

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
        GUI.Label(new Rect(12, 12, 520, 22),
            $"swingLookScale {swingLookScale:0.00}   grip {GripSpeed:0.0} m/s   edge {edge:0.0} m/s" +
            (_sweep != null ? $"   (gate {_sweep.minEdgeSpeed:0.0})" : ""));
    }
}
