using UnityEngine;

/// <summary>
/// Shared reactive carry layer for held viewmodels — the floaty feel the axe
/// and pistol have, packaged so every other equippable can opt in with one line.
///
/// Same spring-damper model as <see cref="AxeMotor"/> / <see cref="PistolMotor"/>,
/// with one structural difference: this component lives ON the rig transform it
/// drives, not on the player. Those two are player-components that drive a child
/// rig, which is why they had to be separate classes (GetComponent&lt;AxeMotor&gt;()
/// returns derived types, so a shared base would have had the axe and the pistol
/// fighting over one component). Self-driving rigs have no such collision, so
/// every remaining item shares this one class and any number can coexist.
///
/// The axe and pistol deliberately keep their own tuned copies — their feel is
/// dialled in and coupled to their swing/ADS layers. This is for everything else.
///
/// Usage:
///   var motor = ViewmodelMotor.CreateRig(holdTransform, "RodMotorRig", restOffset);
///   Instantiate(prefab, motor.transform);
///   ...
///   Destroy(motor.gameObject);   // on unequip
///
/// Everything is computed in camera-local space (the rig is a descendant of the
/// camera) and all velocity inputs come from PlayerController's surface-relative
/// accessors, so it's floating-origin safe. No Rigidbody, no colliders.
/// </summary>
public class ViewmodelMotor : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Global multiplier on every offset this component produces. 0 = motor off (rig sits at rest).")]
    [Range(0f, 2f)] public float intensity = 1f;

    [Header("Springs")]
    [Tooltip("Position spring stiffness. Lower = floatier.")]
    public float positionStiffness = 145f;
    [Tooltip("Position spring damping. ~2*sqrt(stiffness) is critically damped; lower = more overshoot.")]
    public float positionDamping = 14f;
    [Tooltip("Rotation spring stiffness.")]
    public float rotationStiffness = 115f;
    [Tooltip("Rotation spring damping.")]
    public float rotationDamping = 12f;

    [Header("Camera lag")]
    [Tooltip("Degrees of rig counter-rotation per (deg/sec) of camera turn. The main 'floaty' dial.")]
    public float lagRotationFactor = 0.038f;
    [Tooltip("Metres of rig counter-translation per (deg/sec) of camera turn.")]
    public float lagPositionFactor = 0.00038f;
    [Tooltip("Camera angular velocity above this (deg/sec) is clamped — survives teleports/cuts without launching the item.")]
    public float maxLagInput = 540f;
    [Tooltip("Low-pass responsiveness for the angular-velocity input. Higher = snappier.")]
    public float lagInputSmoothing = 11f;

    [Header("Locomotion sway")]
    [Tooltip("Metres of lateral drag per (m/s) of strafe velocity.")]
    public float swayFactor = 0.014f;
    [Tooltip("Degrees of roll per (m/s) of strafe velocity.")]
    public float strafeRollFactor = 2.5f;
    [Tooltip("Metres the item drifts toward the player per (m/s) of forward velocity.")]
    public float forwardDriftFactor = 0.009f;
    [Tooltip("Walk-bob vertical amplitude (metres) at full stride.")]
    public float bobAmplitude = 0.011f;
    [Tooltip("Walk-bob stride frequency (radians of phase per metre travelled).")]
    public float bobFrequency = 1.5f;

    [Header("Vertical / landing")]
    [Tooltip("Metres of vertical lag per (m/s) of vertical velocity.")]
    public float verticalVelocityFactor = 0.016f;
    [Tooltip("Downward velocity kick (m/s) injected into the position spring on landing, scaled by fall speed.")]
    public float landingKick = 1f;
    [Tooltip("Fall speed (m/s) at which the landing kick reaches full strength.")]
    public float landingReferenceSpeed = 12f;
    [Tooltip("Pitch velocity kick (deg/s) injected into the rotation spring on landing.")]
    public float landingPitchKick = 58f;

    [Header("Sprint")]
    [Tooltip("Metres the item drifts back toward the player while sprinting.")]
    public float sprintBackOffset = 0.08f;
    [Tooltip("Degrees of downward pitch tilt while sprinting.")]
    public float sprintPitchTilt = 11f;

    [Header("Idle float")]
    [Tooltip("Metres of slow vertical drift while standing still — makes carried items read as hovering rather than welded to the camera. 0 disables.")]
    public float idleFloatAmplitude = 0.012f;
    [Tooltip("Idle float cycles per second.")]
    public float idleFloatSpeed = 0.7f;

    [Header("Clamps")]
    [Tooltip("Maximum target offset per camera-space axis (metres).")]
    public Vector3 maxPositionOffset = new Vector3(0.15f, 0.15f, 0.13f);
    [Tooltip("Maximum target rotation offset per axis (degrees).")]
    public float maxRotationOffset = 23f;

    [Header("Rest pose")]
    [Tooltip("Base camera-space offset from the hold transform — the 'how far out is it held' dial. X pushes right, Z pushes away from you.")]
    public Vector3 restOffset = Vector3.zero;

    /// <summary>Runtime-only multiplier on top of intensity, for callers that want to steady the item (e.g. aiming).</summary>
    [System.NonSerialized] public float SwayScale = 1f;

    /// <summary>Runtime-only camera-space offset added on top of the rest pose and
    /// the springs — for a controller-driven pose like raising a bottle to drink.
    /// Added AFTER the spring integration so it never fights the carry feel.</summary>
    [System.NonSerialized] public Vector3 PoseOffset;

    /// <summary>Runtime-only extra rotation (degrees) applied with PoseOffset.</summary>
    [System.NonSerialized] public Vector3 PoseEuler;

    /// <summary>
    /// Creates a rig GameObject parented to <paramref name="holdParent"/> with a
    /// motor on it, ready to hold the item model. Destroy the rig GameObject to
    /// tear the whole thing down.
    /// </summary>
    public static ViewmodelMotor CreateRig(Transform holdParent, string name, Vector3 restOffset, Vector3 gripPoint = default)
    {
        var go = new GameObject(string.IsNullOrEmpty(name) ? "ViewmodelMotorRig" : name);
        go.transform.SetParent(holdParent, false);
        var motor = go.AddComponent<ViewmodelMotor>();
        motor.restOffset = restOffset;
        motor._gripPoint = gripPoint;
        motor.ResetPose();
        return motor;
    }

    Vector3 _gripPoint;           // local offset rotations pivot around
    PlayerController _player;
    Quaternion _prevCamRotation;
    bool _hasPrevCamRotation;
    bool _wasGrounded;
    float _prevVerticalVelocity;
    Vector3 _angularVelocitySmoothed;
    float _bobPhase;
    float _idlePhase;

    Vector3 _position, _positionVelocity;
    Vector3 _rotation, _rotationVelocity;   // small-angle Euler degrees

    // Shared across every rig — one lookup per session rather than one per item,
    // re-searched at most once a second while missing (never per-frame).
    static PlayerController s_player;
    static float s_nextPlayerSearch;

    /// <summary>Snap every spring to rest and forget stale frame history (camera cuts, equips, loads).</summary>
    public void ResetPose()
    {
        _position = _positionVelocity = Vector3.zero;
        _rotation = _rotationVelocity = Vector3.zero;
        _angularVelocitySmoothed = Vector3.zero;
        _hasPrevCamRotation = false;
        _bobPhase = 0f;
        transform.localPosition = restOffset;
        transform.localRotation = Quaternion.identity;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Transform cam = transform.parent;   // the hold transform, itself under the Camera
        if (cam == null) return;

        if (_player == null)
        {
            if (s_player == null && Time.time >= s_nextPlayerSearch)
            {
                s_nextPlayerSearch = Time.time + 1f;
                s_player = FindObjectOfType<PlayerController>();
            }
            _player = s_player;
        }

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

        // Idle float — fades in as the walk bob fades out, so a carried item
        // hovers gently when you stand still instead of locking rigid.
        if (idleFloatAmplitude > 0f)
        {
            _idlePhase += idleFloatSpeed * dt;
            float idleWeight = 1f - Mathf.Clamp01(planarSpeed / 1.5f);
            targetPosition.y += Mathf.Sin(_idlePhase * Mathf.PI * 2f) * idleFloatAmplitude * idleWeight;
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

        Quaternion carryRot = Quaternion.Euler(_rotation + PoseEuler);
        transform.localRotation = carryRot;
        transform.localPosition = restOffset + _position + PoseOffset + (_gripPoint - carryRot * _gripPoint);
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

    /// <summary>
    /// The shared right-hand hold transform — the camera-child point the pistol,
    /// axe and rod all hang off. Use this rather than a controller's own field
    /// when that field might point at a BONE: the water bottle's hold point is
    /// on the actual hand, which parks the item down by the player's side
    /// instead of up in view where every other item sits.
    /// </summary>
    public static Transform ResolveSharedHoldPoint(GameObject playerRoot, Transform fallback = null)
    {
        if (playerRoot != null)
        {
            var pistol = playerRoot.GetComponent<PistolController>();
            if (pistol != null && pistol.pistolHoldPosition != null) return pistol.pistolHoldPosition;
            var axe = playerRoot.GetComponent<AxeController>();
            if (axe != null && axe.axeHoldPosition != null) return axe.axeHoldPosition;
            var rod = playerRoot.GetComponent<FishingRodController>();
            if (rod != null && rod.rodHoldPosition != null) return rod.rodHoldPosition;
        }
        return fallback;
    }

    /// <summary>
    /// The resting local offset that the pistol (or axe) uses to reach its
    /// "sits nicely in the bottom right" spot, relative to the shared hold
    /// transform. Any item that has no hand-tuned offset of its own should build
    /// its rest pose from this rather than guessing.
    ///
    /// The hold transform's own origin is NOT that spot — it's up near the
    /// camera. The pistol gets down and to the right by stacking
    /// PistolMotor.restOffset onto PistolController.holdPositionOffset, so an
    /// item parented straight to the hold transform with a small offset ends up
    /// in the player's face. Deriving from the item that's already placed
    /// correctly is how everything lands in the same spot without a second set
    /// of magic numbers to keep in sync.
    /// </summary>
    public static Vector3 ReferenceRestOffset(GameObject playerRoot)
    {
        if (playerRoot != null)
        {
            var pistol = playerRoot.GetComponent<PistolController>();
            if (pistol != null && pistol.pistolHoldPosition != null)
            {
                var pm = playerRoot.GetComponent<PistolMotor>();
                return pistol.holdPositionOffset + (pm != null ? pm.restOffset : Vector3.zero);
            }
            var axe = playerRoot.GetComponent<AxeController>();
            if (axe != null && axe.axeHoldPosition != null)
            {
                var am = playerRoot.GetComponent<AxeMotor>();
                return axe.holdPositionOffset + (am != null ? am.restOffset : Vector3.zero);
            }
        }
        return Vector3.zero;
    }

    /// <summary>
    /// Scales an instance so its longest visible edge is roughly
    /// <paramref name="targetLongestEdge"/> metres. World props are authored at
    /// world size, which is far too big held 30cm from the eye — this is what
    /// keeps every viewmodel reading at a consistent scale. Pass 0 to skip.
    /// </summary>
    public static void NormalizeSize(GameObject instance, float targetLongestEdge)
    {
        if (instance == null || targetLongestEdge <= 0f) return;
        var rends = instance.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        Vector3 s = b.size;
        float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        if (longest <= 0.0001f) return;
        instance.transform.localScale *= targetLongestEdge / longest;
    }

    /// <summary>
    /// Held viewmodels sit centimetres from the camera, so the sun throws their
    /// silhouette onto the ground ahead as a dark blob pinned to your view that
    /// you can never walk up to. Strip shadow casting (plus physics, which a
    /// world prefab often carries) from anything spawned into a rig.
    /// </summary>
    public static void MakeViewmodel(GameObject instance)
    {
        if (instance == null) return;
        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var col in instance.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
