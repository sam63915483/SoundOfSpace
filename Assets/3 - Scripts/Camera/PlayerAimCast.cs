using UnityEngine;

/// <summary>
/// Crosshair casts that never hit the player's own body.
///
/// In first person a ray from the camera starts inside the astronaut's capsule
/// and PhysX doesn't report the collider it starts in, so nothing ever noticed
/// the player was in the way. In third person (V) the camera sits metres behind
/// the head and every crosshair cast — interact gaze, pistol, axe, grapple —
/// landed on the astronaut's back. These wrappers take the nearest hit that is
/// NOT under the player root, so the crosshair still means "what the dot is on"
/// from wherever the camera happens to be.
///
/// Cheap: one NonAlloc cast into a fixed buffer; only the nearest survivor is
/// returned, so callers keep their single-hit shape.
/// </summary>
public static class PlayerAimCast
{
    static readonly RaycastHit[] _buf = new RaycastHit[32];
    static Transform _playerRoot;

    static Transform PlayerRoot()
    {
        if (_playerRoot == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            _playerRoot = go != null ? go.transform : null;
        }
        return _playerRoot;
    }

    static bool IsPlayer(Collider c, Transform root)
    {
        return root != null && c != null && c.transform.IsChildOf(root);
    }

    static bool Nearest(int count, out RaycastHit hit)
    {
        Transform root = PlayerRoot();
        hit = default;
        float best = float.MaxValue;
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            ref RaycastHit h = ref _buf[i];
            if (h.collider == null || IsPlayer(h.collider, root)) continue;
            if (h.distance < best) { best = h.distance; hit = h; any = true; }
        }
        return any;
    }

    public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction qti)
    {
        int n = Physics.RaycastNonAlloc(ray, _buf, maxDistance, layerMask, qti);
        return Nearest(n, out hit);
    }

    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction qti)
        => Raycast(new Ray(origin, direction), out hit, maxDistance, layerMask, qti);

    public static bool SphereCast(Ray ray, float radius, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction qti)
    {
        int n = Physics.SphereCastNonAlloc(ray, radius, _buf, maxDistance, layerMask, qti);
        return Nearest(n, out hit);
    }

    /// <summary>Extra reach to add to any range measured from the camera, so a
    /// third-person camera 2–6 m behind the head doesn't shorten arm's length.
    /// 0 in first person.</summary>
    public static float ExtraReach => CameraTransformFX.CameraToEyeDistance;
}
