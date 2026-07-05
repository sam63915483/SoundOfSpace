using UnityEngine;

/// <summary>
/// Per-frame cached "is this on screen?" test for the black-hole observation dimensions.
/// Frustum-only (no occlusion raycasts) by design: "behind a wall but in front of you"
/// counts as observed — forgiving, cheap, and turning around is always unobserved.
/// </summary>
public static class ObserverState
{
    static Camera _cam;                      // cached; Unity-null after scene swap → lazy refind
    static readonly Plane[] _planes = new Plane[6];
    static int _stampedFrame = -1;

    public static Camera Cam { get { Resolve(); return _cam; } }

    static void Resolve()
    {
        if (_cam != null) return;
        var mgr = CameraEffectsManager.Instance;   // same resolution order as BlackHoleCapture
        if (mgr != null && mgr.PlayerCamera != null) _cam = mgr.PlayerCamera;
        if (_cam == null) _cam = Camera.main;
    }

    static bool Refresh()
    {
        Resolve();
        if (_cam == null) return false;
        if (_stampedFrame != Time.frameCount)
        {
            GeometryUtility.CalculateFrustumPlanes(_cam, _planes);
            _stampedFrame = Time.frameCount;
        }
        return true;
    }

    /// <summary>True when the bounds intersect the camera frustum. No camera → false (nothing observed).</summary>
    public static bool IsObserved(Bounds b, float maxDistance = Mathf.Infinity)
    {
        if (!Refresh()) return false;
        Vector3 toB = b.center - _cam.transform.position;
        if (!float.IsInfinity(maxDistance) && toB.sqrMagnitude > maxDistance * maxDistance) return false;
        // Behind-camera early-out: a centre behind the camera by more than the bounds
        // radius can never intersect the frustum — skips the 6-plane test for ~half the world.
        if (Vector3.Dot(toB, _cam.transform.forward) < -b.extents.magnitude) return false;
        return GeometryUtility.TestPlanesAABB(_planes, b);
    }
}
