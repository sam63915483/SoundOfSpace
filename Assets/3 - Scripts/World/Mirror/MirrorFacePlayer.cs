using UnityEngine;

// Turns the cage mirror on only when the player is within range, and yaws it
// (around the cage's vertical axis only — no pitch/roll) to face the player, so
// walking up to the cage makes your reflection appear "inside" it. Beyond range
// the mirror surface + its reflection render are switched off (also saves the
// per-frame reflection render cost).
[RequireComponent(typeof(MirrorReflection))]
[RequireComponent(typeof(Renderer))]
public class MirrorFacePlayer : MonoBehaviour {

    [Tooltip("The mirror activates + faces the player only within this distance (metres).")]
    public float activationRange = 30f;

    MirrorReflection _mirror;
    Renderer _rend;
    Transform _player;
    float _nextFindTime;

    void Awake() {
        _mirror = GetComponent<MirrorReflection>();
        _rend = GetComponent<Renderer>();
        SetActive(false); // off until the player is near
    }

    void LateUpdate() {
        if (_player == null) {
            // "may never appear" target — throttle the re-find, never search in
            // Update unconditionally.
            if (Time.time >= _nextFindTime) {
                _nextFindTime = Time.time + 1f;
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) _player = go.transform;
            }
            if (_player == null) { SetActive(false); return; }
        }

        float dist = Vector3.Distance(transform.position, _player.position);
        bool on = dist <= activationRange;
        SetActive(on);
        if (!on) return;

        // Yaw to face the player around the cage's up axis only (stays vertical).
        Vector3 up = transform.parent != null ? transform.parent.up : Vector3.up;
        Vector3 flat = Vector3.ProjectOnPlane(_player.position - transform.position, up);
        if (flat.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(flat.normalized, up);
    }

    void SetActive(bool on) {
        if (_rend != null && _rend.enabled != on) _rend.enabled = on;
        if (_mirror != null && _mirror.enabled != on) _mirror.enabled = on;
    }
}
