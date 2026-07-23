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

    void Awake()
    {
        _restLocalRot = transform.localRotation;
        if (screen == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "TVScreen") { screen = t; break; }
        }
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

        Vector3 playerPos = _player.transform.position;
        if ((playerPos - transform.position).sqrMagnitude > trackRange * trackRange) return;

        // Work in the parent's space: local Y is the mount's spin axis there.
        Transform parent = transform.parent;
        Vector3 toPlayer = parent.InverseTransformPoint(playerPos) - transform.localPosition;
        toPlayer.y = 0f;
        Vector3 facing = parent.InverseTransformDirection(flipScreenNormal ? -screen.forward : screen.forward);
        facing.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f || facing.sqrMagnitude < 0.0001f) return;

        float error = Vector3.SignedAngle(facing, toPlayer, Vector3.up);
        if (Mathf.Abs(error) < deadzoneDegrees) return;

        _yaw += Mathf.Clamp(error, -degreesPerSecond * Time.deltaTime, degreesPerSecond * Time.deltaTime);
        transform.localRotation = Quaternion.AngleAxis(_yaw, Vector3.up) * _restLocalRot;
    }
}
