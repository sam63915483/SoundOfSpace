using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): one continuous orbiting camera around the
/// shuttle — Sam's spec: the camera's only moves are orbiting the shuttle and
/// dollying further out / closer in, always facing it, never inside it.
///
/// The pose is written EXACTLY every LateUpdate — no positional lerp. The
/// camera is a child of the player inside the moving shuttle, so a lerp
/// toward the target loses a tug-of-war against the parent dragging it every
/// physics tick (v2 twitched inside the cabin because of exactly that).
/// Smoothness lives in the orbit parameters (slow noise curves), not in lag.
/// The minimum distance is derived from the shuttle's real render bounds so
/// the camera can never start inside it.
/// </summary>
public class MenuShotDirector : MonoBehaviour
{
    public MenuShuttleTour tour;
    public Camera cam;

    [Tooltip("Closest approach; raised automatically to 1.8x the shuttle's bounds radius.")]
    public float minDistance = 45f;
    public float maxDistance = 140f;
    [Tooltip("Base orbit speed around the shuttle, degrees/second.")]
    public float orbitSpeed = 6f;

    float azimuth;
    float seed;

    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (cam == null || tour == null) { enabled = false; return; }

        // Never-inside guarantee: measure the shuttle.
        var bounds = new Bounds(tour.transform.position, Vector3.zero);
        foreach (var r in tour.GetComponentsInChildren<Renderer>())
            if (r.enabled) bounds.Encapsulate(r.bounds);
        float shuttleRadius = bounds.extents.magnitude;
        minDistance = Mathf.Max(minDistance, shuttleRadius * 1.8f);
        maxDistance = Mathf.Max(maxDistance, minDistance * 2.5f);

        seed = Random.Range(0f, 100f);
        azimuth = Random.Range(0f, 360f);
        cam.fieldOfView = 50f;
        LateUpdate();
    }

    void LateUpdate()
    {
        Transform sh = tour.transform;
        var body = tour.FocusBody;
        if (body == null) return;

        float t = Time.time;
        azimuth += orbitSpeed * (0.7f + 0.6f * Mathf.PerlinNoise(t * 0.05f, seed)) * Time.deltaTime;
        float elevation = Mathf.Lerp(-6f, 28f, Mathf.PerlinNoise(t * 0.03f, seed + 31f));
        float distance = Mathf.Lerp(minDistance, maxDistance, Mathf.PerlinNoise(t * 0.02f, seed + 62f));

        // Horizon stays level against the planet being toured.
        Vector3 up = (sh.position - body.Position).normalized;
        Vector3 refFwd = Vector3.Cross(up, Vector3.forward);
        if (refFwd.sqrMagnitude < 0.01f) refFwd = Vector3.Cross(up, Vector3.right);
        refFwd.Normalize();

        Quaternion swing = Quaternion.AngleAxis(azimuth, up)
                         * Quaternion.AngleAxis(elevation, Vector3.Cross(up, refFwd));
        Vector3 pos = sh.position + swing * refFwd * distance;

        transform.SetPositionAndRotation(pos,
            Quaternion.LookRotation((sh.position - pos).normalized, up));
    }
}
