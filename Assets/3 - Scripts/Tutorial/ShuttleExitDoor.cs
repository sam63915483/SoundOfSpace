using UnityEngine;

// The shuttle's exit door / boarding ramp. Lives on ExitDoor_Pivot — an empty
// at the door slab's BOTTOM edge — with Sam's "door" slab parented under it.
// Rotating the pivot around local X folds the door outward and down until it
// lies as a ramp. The player never controls it: it stays shut (a plain solid
// wall) until the orientation film ends, when ShuttleArrivalSequence calls
// Open(). Open Angle / Open Time are the hand-tuning knobs for the perfect
// door-to-ramp motion.
public class ShuttleExitDoor : MonoBehaviour
{
    [Tooltip("Degrees the door folds outward-down. 90 = flat horizontal; more lets the tip reach the ground.")]
    public float openAngle = 115f;

    [Tooltip("Seconds the fold takes.")]
    public float openTime = 2.5f;

    Quaternion _restRot;
    float _t;
    bool _opening;
    bool _open;

    public bool IsOpen => _open;

    void Awake()
    {
        _restRot = transform.localRotation;
    }

    public void Open()
    {
        if (_open || _opening) return;
        _opening = true;
        _t = 0f;
    }

    public void OpenInstant()
    {
        _opening = false;
        _open = true;
        transform.localRotation = _restRot * Quaternion.AngleAxis(openAngle, Vector3.right);
    }

    void Update()
    {
        if (!_opening) return;
        _t += Time.deltaTime;
        float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, openTime));
        // Ease-out with a soft settle at the end — reads as hydraulics, not a hinge flop.
        float k = 1f - Mathf.Pow(1f - u, 3f);
        transform.localRotation = _restRot * Quaternion.AngleAxis(openAngle * k, Vector3.right);
        if (u >= 1f) { _opening = false; _open = true; }
    }
}
