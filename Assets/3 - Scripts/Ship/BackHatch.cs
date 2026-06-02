using UnityEngine;

/// <summary>
/// Animated rear hatch on Ship_Full. Rotates around a custom pivot offset
/// (so the hinge can be off the prefab's origin) and exposes inspector fields
/// for live tuning in Play mode — change pivot offset, rotation axis, or angle
/// at runtime and the hatch animates to match each frame, then copy the
/// values back into the inspector outside Play mode.
///
/// pivotOffset and rotationAxis are interpreted in the hatch's REST local
/// frame (its local pose at Awake), so values stay intuitive regardless of
/// how the GameObject is parented or pre-rotated in the scene.
/// </summary>
public class BackHatch : MonoBehaviour
{
    [Header("Hinge")]
    [Tooltip("Local-space offset (in this transform's REST frame) from the GameObject's origin to the hinge point. The hatch rotates around (rest position + this offset). Tune in Play mode by watching the yellow gizmo move.")]
    public Vector3 pivotOffset = Vector3.zero;
    [Tooltip("Local-space axis (in this transform's REST frame) the hatch rotates around. (1,0,0) = swings around the hinge's X axis.")]
    public Vector3 rotationAxis = Vector3.right;
    [Tooltip("Degrees the hatch rotates from closed → fully open. Negative flips the swing direction.")]
    public float openAngle = 90f;

    [Header("Animation")]
    [Tooltip("Lerp speed toward the target angle. Higher = snappier; ~4 feels mechanical, ~12 feels instant.")]
    public float openSpeed = 4f;

    [Header("State")]
    [Tooltip("Toggle in Play mode (or via SetOpen at runtime) to animate the hatch open/closed.")]
    public bool isOpen = false;

    Vector3 _basePos;
    Quaternion _baseRot;
    float _currentAngle;

    public bool IsOpen => isOpen;
    public void SetOpen(bool open) { isOpen = open; }
    public void Toggle() { isOpen = !isOpen; }

    void Awake()
    {
        _basePos = transform.localPosition;
        _baseRot = transform.localRotation;
        _currentAngle = 0f;
    }

    void Update()
    {
        float targetAngle = isOpen ? openAngle : 0f;
        _currentAngle = Mathf.Lerp(_currentAngle, targetAngle, Mathf.Clamp01(openSpeed * Time.deltaTime));
        // Snap when close so live-edited values land on a clean number.
        if (Mathf.Abs(_currentAngle - targetAngle) < 0.01f) _currentAngle = targetAngle;
        ApplyPose(_currentAngle);
    }

    void ApplyPose(float angle)
    {
        Vector3 axisLocal = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis : Vector3.right;
        // Express axis & pivot in the parent's local space (= the rest local
        // frame rotated by _baseRot). The hinge is then a fixed point in the
        // PARENT's frame, which is what a real hinge does.
        Vector3 axisParent = (_baseRot * axisLocal).normalized;
        Quaternion rot = Quaternion.AngleAxis(angle, axisParent);

        Vector3 pivotParent = _basePos + (_baseRot * pivotOffset);
        Vector3 newPos = pivotParent + rot * (_basePos - pivotParent);
        Quaternion newRot = rot * _baseRot;

        transform.localPosition = newPos;
        transform.localRotation = newRot;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Show the hinge as a yellow sphere and the rotation axis as a cyan
        // line so the user can see where the pivot is while tuning.
        Vector3 baseLocalPos = Application.isPlaying ? _basePos : transform.localPosition;
        Quaternion baseLocalRot = Application.isPlaying ? _baseRot : transform.localRotation;
        Transform parent = transform.parent;

        Vector3 worldPivot = parent != null
            ? parent.TransformPoint(baseLocalPos + baseLocalRot * pivotOffset)
            : baseLocalPos + baseLocalRot * pivotOffset;

        float gizmoSize = HandleSize(worldPivot, 0.06f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(worldPivot, gizmoSize);

        Vector3 axisLocal = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.right;
        Vector3 worldAxis = parent != null
            ? parent.TransformDirection(baseLocalRot * axisLocal)
            : baseLocalRot * axisLocal;
        float lineLen = gizmoSize * 8f;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(worldPivot - worldAxis * lineLen, worldPivot + worldAxis * lineLen);
    }

    static float HandleSize(Vector3 worldPos, float scale)
    {
        var view = UnityEditor.SceneView.lastActiveSceneView;
        if (view == null || view.camera == null) return scale;
        var cam = view.camera;
        float dist = Vector3.Distance(cam.transform.position, worldPos);
        return Mathf.Max(0.01f, dist * scale * 0.1f);
    }
#endif
}
