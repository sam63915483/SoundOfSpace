using UnityEngine;

/// Spins the orientation TV's arm around the TVMount's central axis so the
/// screen swings to face the player inside the shuttle. Attach to
/// TVArm_Pivot — its local position sits exactly on the mount's vertical
/// axis, and only localRotation around local Y is ever touched, so the
/// ceiling attachment never drifts and Sam's hand-tuned arm pose is kept.
///
/// The facing math is self-correcting: each frame it measures where the
/// screen actually points (projected onto the spin plane) versus where the
/// player is, and turns the pivot by the difference — so re-posing the arm
/// or TV in the editor never needs a code change.
public class OrientationTVSpin : MonoBehaviour
{
    [Tooltip("The TV screen face; auto-found by name if left empty.")]
    public Transform screen;

    [Tooltip("Degrees per second the arm can swing.")]
    public float degreesPerSecond = 110f;

    [Tooltip("Only track the player within this many metres of the pivot.")]
    public float trackRange = 12f;

    [Tooltip("Ignore aim errors smaller than this (stops micro-jitter).")]
    public float deadzoneDegrees = 1.5f;

    [Tooltip("Tick if the TV ends up showing its back to the player.")]
    public bool flipScreenNormal = false;

    PlayerController _player;
    float _playerRetryTimer;
    Quaternion _restLocalRot;
    float _yaw;
    Quaternion _tiltRestLocalRot;
    float _tilt;
    float _yawVel;
    float _tiltVel;

    void Awake()
    {
        _restLocalRot = transform.localRotation;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (screen == null && t.name == "TVScreen") screen = t;
            if (tiltPivot == null && t.name == "TVTilt_Pivot") tiltPivot = t;
        }
        if (tiltPivot != null) _tiltRestLocalRot = tiltPivot.localRotation;
    }

    void LateUpdate()
    {
        if (screen == null) return;

        if (_player == null)
        {
            _playerRetryTimer -= Time.deltaTime;
            if (_playerRetryTimer > 0f) return;
            _playerRetryTimer = 2f;
            _player = FindObjectOfType<PlayerController>();
            if (_player == null) return;
        }

        float dt = Time.deltaTime;
        Vector3 playerPos = _player.transform.position;
        bool inRange = (playerPos - transform.position).sqrMagnitude <= trackRange * trackRange;

        // Both axes run a spring-damper instead of chasing directly: the arm
        // accelerates toward the target, lags behind, overshoots a touch and
        // settles with a swing — reads as a servo, not a perfect tracker.
        // Out of range the targets go to 0 so the springs relax to rest.

        // -- yaw around the mount axis (parent space: local Y is the axis) --
        float error = 0f;
        if (inRange)
        {
            Transform parent = transform.parent;
            Vector3 toPlayer = parent.InverseTransformPoint(playerPos) - transform.localPosition;
            toPlayer.y = 0f;
            Vector3 facing = parent.InverseTransformDirection(flipScreenNormal ? -screen.forward : screen.forward);
            facing.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f && facing.sqrMagnitude > 0.0001f)
            {
                error = Vector3.SignedAngle(facing, toPlayer, Vector3.up);
                if (Mathf.Abs(error) < deadzoneDegrees) error = 0f;
            }
        }
        _yawVel += (error * yawSpring - _yawVel * yawDamping) * dt;
        _yawVel = Mathf.Clamp(_yawVel, -degreesPerSecond, degreesPerSecond);
        _yaw += _yawVel * dt;
        transform.localRotation = Quaternion.AngleAxis(_yaw, Vector3.up) * _restLocalRot;

        // -- downtilt hinged at the wrist: lean down as the player gets under --
        if (tiltPivot != null)
        {
            float target = 0f;
            if (inRange)
            {
                float dist = Vector3.ProjectOnPlane(playerPos - tiltPivot.position, transform.up).magnitude;
                // sqrt front-loads the curve: most of the lean arrives while
                // approaching, not just in the last step.
                target = maxExtraTilt * Mathf.Sqrt(Mathf.InverseLerp(tiltStartDistance, tiltFullDistance, dist));
            }
            _tiltVel += ((target - _tilt) * tiltSpring - _tiltVel * tiltDamping) * dt;
            _tiltVel = Mathf.Clamp(_tiltVel, -tiltDegreesPerSecond, tiltDegreesPerSecond);
            _tilt = Mathf.Clamp(_tilt + _tiltVel * dt, -5f, maxExtraTilt + 8f);
            tiltPivot.localRotation = _tiltRestLocalRot * Quaternion.AngleAxis(_tilt, Vector3.right);
        }
    }

    // -- fields below appended after initial release; keep order (serialization) --

    [Tooltip("Hinge at the wrist joint that carries the TV; auto-found by name if left empty.")]
    public Transform tiltPivot;

    [Tooltip("Maximum EXTRA downward tilt (deg) on top of the authored rest tilt.")]
    public float maxExtraTilt = 42f;

    [Tooltip("Closer than this (m, horizontal) the TV starts leaning down.")]
    public float tiltStartDistance = 5f;

    [Tooltip("At this horizontal distance (m) the tilt reaches its maximum.")]
    public float tiltFullDistance = 1.4f;

    [Tooltip("Degrees per second the tilt hinge can move.")]
    public float tiltDegreesPerSecond = 80f;

    [Tooltip("Yaw spring strength (accel per degree of error). Lower = lazier.")]
    public float yawSpring = 4f;

    [Tooltip("Yaw damping. Below 2*sqrt(spring) the arm overshoots and swings.")]
    public float yawDamping = 2.2f;

    [Tooltip("Tilt spring strength.")]
    public float tiltSpring = 7f;

    [Tooltip("Tilt damping (slightly underdamped for a small nod-past).")]
    public float tiltDamping = 4.2f;
}
