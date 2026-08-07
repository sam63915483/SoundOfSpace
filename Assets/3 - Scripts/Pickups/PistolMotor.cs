using UnityEngine;

/// <summary>
/// Reactive carry layer for the equipped pistol — the same spring-damper model
/// as <see cref="AxeMotor"/>, applied to a "PistolMotorRig" transform that
/// PistolController inserts between pistolHoldPosition and the PistolPivot, so
/// the existing equip / recoil / reload tweens on the pivot are untouched and
/// simply ride on top of the sway.
///
/// This is a SEPARATE class rather than a subclass of AxeMotor on purpose:
/// GetComponent&lt;AxeMotor&gt;() returns derived types, so a subclass would be
/// picked up by AxeController's motor lookup and the two tools would fight over
/// one component. Separate classes also mean the pistol can be tuned to a
/// lighter, snappier feel without moving the axe's numbers.
///
/// Everything is computed in camera-local space (the rig is a child of the
/// camera) and all velocity inputs come from PlayerController's surface-relative
/// accessors, so this is floating-origin safe. No Rigidbody, no colliders.
/// </summary>
public class PistolMotor : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Global multiplier on every offset this component produces. 0 = motor off (rig sits at identity).")]
    [Range(0f, 2f)] public float intensity = 1f;

    [Header("Springs")]
    [Tooltip("Position spring stiffness (how hard the rig is pulled toward the target offset). Lower = floatier.")]
    public float positionStiffness = 150f;
    [Tooltip("Position spring damping. ~2*sqrt(stiffness) is critically damped; lower = floatier / more overshoot.")]
    public float positionDamping = 14f;
    [Tooltip("Rotation spring stiffness.")]
    public float rotationStiffness = 120f;
    [Tooltip("Rotation spring damping.")]
    public float rotationDamping = 12f;

    [Header("Camera lag")]
    [Tooltip("Degrees of rig counter-rotation per (deg/sec) of camera turn. Turning right makes the pistol lag left. This is the big 'floaty' dial.")]
    public float lagRotationFactor = 0.040f;
    [Tooltip("Metres of rig counter-translation per (deg/sec) of camera turn.")]
    public float lagPositionFactor = 0.00040f;
    [Tooltip("Camera angular velocity above this (deg/sec) is clamped — survives teleports/cuts without launching the gun.")]
    public float maxLagInput = 540f;
    [Tooltip("Low-pass responsiveness for the angular-velocity input. Higher = snappier, lower = smoother/floatier.")]
    public float lagInputSmoothing = 11f;

    [Header("Locomotion sway")]
    [Tooltip("Metres of lateral drag per (m/s) of strafe velocity.")]
    public float swayFactor = 0.013f;
    [Tooltip("Degrees of roll per (m/s) of strafe velocity.")]
    public float strafeRollFactor = 2.6f;
    [Tooltip("Metres the pistol drifts toward the player per (m/s) of forward velocity.")]
    public float forwardDriftFactor = 0.008f;
    [Tooltip("Walk-bob vertical amplitude (metres) at full stride.")]
    public float bobAmplitude = 0.010f;
    [Tooltip("Walk-bob stride frequency (radians of phase per metre travelled).")]
    public float bobFrequency = 1.5f;

    [Header("Vertical / landing")]
    [Tooltip("Metres of vertical lag per (m/s) of vertical velocity. Positive = gun dips while rising and floats up while falling.")]
    public float verticalVelocityFactor = 0.015f;
    [Tooltip("Downward velocity kick (m/s) injected into the position spring on landing, scaled by fall speed.")]
    public float landingKick = 0.9f;
    [Tooltip("Fall speed (m/s) at which the landing kick reaches full strength.")]
    public float landingReferenceSpeed = 12f;
    [Tooltip("Pitch velocity kick (deg/s) injected into the rotation spring on landing.")]
    public float landingPitchKick = 55f;

    [Header("Sprint")]
    [Tooltip("Metres the pistol drifts back toward the player while sprinting.")]
    public float sprintBackOffset = 0.07f;
    [Tooltip("Degrees of downward pitch tilt while sprinting.")]
    public float sprintPitchTilt = 12f;

    [Header("Clamps (keep the pistol on screen)")]
    [Tooltip("Maximum target offset per camera-space axis (metres).")]
    public Vector3 maxPositionOffset = new Vector3(0.14f, 0.14f, 0.12f);
    [Tooltip("Maximum target rotation offset per axis (degrees).")]
    public float maxRotationOffset = 22f;

    [Header("Rest pose")]
    [Tooltip("Base camera-space offset of the whole pistol chain from pistolHoldPosition. THIS is the 'hold the gun further out' dial — X pushes right, Z pushes away from the player. Mirrors AxeMotor.restOffset (which shoves the axe well forward so it isn't in the astronaut's face); pistolHoldPosition itself is shared with the rod/bottle, so don't move that.")]
    public Vector3 restOffset = new Vector3(0.055f, -0.01f, 0.30f);

    /// <summary>
    /// Runtime-only multiplier on top of <see cref="intensity"/>, driven by
    /// PistolController so aiming down the sights can steady the gun without
    /// stomping the player-facing intensity dial. 1 = full sway.
    /// </summary>
    [System.NonSerialized] public float SwayScale = 1f;

    Transform _rig;               // the PistolMotorRig created by PistolController on equip
    Vector3 _gripPoint;           // pivot local offset — rotations pivot around this
    PlayerController _player;
    Quaternion _prevCamRotation;
    bool _hasPrevCamRotation;
    bool _wasGrounded;
    float _prevVerticalVelocity;
    Vector3 _angularVelocitySmoothed;   // camera-local deg/s (x = pitch, y = yaw)
    float _bobPhase;

    // Spring state — rig-local position offset and Euler rotation offset.
    Vector3 _position, _positionVelocity;
    Vector3 _rotation, _rotationVelocity;   // small-angle Euler degrees

    /// <summary>
    /// Called by PistolController when the pistol is equipped and its motor rig
    /// exists. gripPoint = the pivot's local offset (holdPositionOffset) so
    /// carry rotations pivot around the gun's grip, not the rig origin.
    /// </summary>
    public void Attach(Transform rig, Vector3 gripPoint = default)
    {
        _rig = rig;
        _gripPoint = gripPoint;
        ResetPose();
    }

    /// <summary>Called by PistolController on unequip. Only detaches if it's the rig we're driving.</summary>
    public void Detach(Transform rig)
    {
        if (_rig == rig) _rig = null;
    }

    /// <summary>Snap every spring to rest and forget stale frame history (camera cuts, equips, loads).</summary>
    public void ResetPose()
    {
        _position = _positionVelocity = Vector3.zero;
        _rotation = _rotationVelocity = Vector3.zero;
        _angularVelocitySmoothed = Vector3.zero;
        _hasPrevCamRotation = false;
        _bobPhase = 0f;
        if (_rig != null)
        {
            _rig.localPosition = restOffset;
            _rig.localRotation = Quaternion.identity;
        }
    }

    void Start()
    {
        _player = GetComponent<PlayerController>();
        if (_player == null) _player = FindObjectOfType<PlayerController>();
    }

    void LateUpdate()
    {
        if (_rig == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Transform cam = _rig.parent;            // rig lives under pistolHoldPosition, itself under the Camera
        if (cam == null) return;

        // --- camera angular velocity (camera-local deg/s), clamped + low-passed ---
        Quaternion camRotation = cam.rotation;
        Vector3 angularVelocity = Vector3.zero;
        if (_hasPrevCamRotation)
        {
            (Quaternion.Inverse(_prevCamRotation) * camRotation).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x)) angularVelocity = axis * (angle / dt);
            angularVelocity = Vector3.ClampMagnitude(angularVelocity, maxLagInput);
        }
        _prevCamRotation = camRotation;
        _hasPrevCamRotation = true;
        _angularVelocitySmoothed = Vector3.Lerp(_angularVelocitySmoothed, angularVelocity,
                                                1f - Mathf.Exp(-lagInputSmoothing * dt));

        // --- player-local locomotion inputs (surface-relative, floating-origin safe) ---
        Vector3 localVelocity = Vector3.zero;
        bool grounded = false, sprinting = false;
        if (_player != null)
        {
            localVelocity = _player.transform.InverseTransformDirection(_player.SurfaceVelocity);
            grounded = _player.IsOnGround;
            sprinting = _player.IsSprinting;
        }
        float planarSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;

        // Player-facing dial × the ADS steadying multiplier.
        float amplitude = intensity * Mathf.Max(0f, SwayScale);

        // --- landing detection: kick the springs, don't move the target ---
        if (grounded && !_wasGrounded)
        {
            float fallSpeed = Mathf.Max(0f, -_prevVerticalVelocity);
            float strength = Mathf.Clamp01(fallSpeed / Mathf.Max(0.01f, landingReferenceSpeed));
            _positionVelocity.y -= landingKick * strength * amplitude;
            _rotationVelocity.x += landingPitchKick * strength * amplitude;
        }
        _wasGrounded = grounded;
        _prevVerticalVelocity = localVelocity.y;

        // --- target pose ---
        float yawRate = _angularVelocitySmoothed.y;    // + = turning right
        float pitchRate = _angularVelocitySmoothed.x;  // + = pitching down

        Vector3 targetPosition;
        targetPosition.x = -yawRate * lagPositionFactor - localVelocity.x * swayFactor;
        targetPosition.y = pitchRate * lagPositionFactor - localVelocity.y * verticalVelocityFactor;
        targetPosition.z = -Mathf.Max(0f, localVelocity.z) * forwardDriftFactor;

        Vector3 targetRotation;
        targetRotation.x = -pitchRate * lagRotationFactor;
        targetRotation.y = -yawRate * lagRotationFactor;
        targetRotation.z = localVelocity.x * strafeRollFactor;

        if (sprinting && planarSpeed > 0.5f)
        {
            targetPosition.z -= sprintBackOffset;
            targetRotation.x += sprintPitchTilt;
        }

        // Walk bob — advances with distance travelled so pace sets the rhythm.
        if (grounded && planarSpeed > 0.3f)
        {
            _bobPhase += planarSpeed * bobFrequency * dt;
            float stride = Mathf.Clamp01(planarSpeed / 6f);
            targetPosition.y += Mathf.Sin(_bobPhase * 2f) * bobAmplitude * stride;
            targetPosition.x += Mathf.Sin(_bobPhase) * bobAmplitude * 0.6f * stride;
        }

        targetPosition *= amplitude;
        targetRotation *= amplitude;

        targetPosition.x = Mathf.Clamp(targetPosition.x, -maxPositionOffset.x, maxPositionOffset.x);
        targetPosition.y = Mathf.Clamp(targetPosition.y, -maxPositionOffset.y, maxPositionOffset.y);
        targetPosition.z = Mathf.Clamp(targetPosition.z, -maxPositionOffset.z, maxPositionOffset.z);
        targetRotation.x = Mathf.Clamp(targetRotation.x, -maxRotationOffset, maxRotationOffset);
        targetRotation.y = Mathf.Clamp(targetRotation.y, -maxRotationOffset, maxRotationOffset);
        targetRotation.z = Mathf.Clamp(targetRotation.z, -maxRotationOffset, maxRotationOffset);

        // --- integrate springs: fixed substeps so 30fps and 144fps match ---
        Spring(ref _position, ref _positionVelocity, targetPosition, positionStiffness, positionDamping, dt);
        Spring(ref _rotation, ref _rotationVelocity, targetRotation, rotationStiffness, rotationDamping, dt);

        Quaternion carryRot = Quaternion.Euler(_rotation);
        _rig.localRotation = carryRot;
        _rig.localPosition = restOffset + _position + (_gripPoint - carryRot * _gripPoint);
    }

    static void Spring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float stiffness, float damping, float dt)
    {
        const float maxStep = 1f / 120f;
        dt = Mathf.Min(dt, 0.1f);   // hitch guard — a 2s freeze must not explode the spring
        while (dt > 0f)
        {
            float h = Mathf.Min(dt, maxStep);
            velocity += (stiffness * (target - value) - damping * velocity) * h;
            value += velocity * h;
            dt -= h;
        }
    }
}
