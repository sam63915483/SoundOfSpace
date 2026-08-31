using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): one continuous orbiting camera around the
/// shuttle — Sam's spec (2026-08-31): the camera's only moves are orbiting the
/// shuttle and dollying further out / closer in. It never cuts, never enters
/// the shuttle (hard minimum distance), and always keeps the shuttle in view.
/// Everything is shuttle-relative, so floating-origin shifts are invisible.
/// Added at runtime by MenuOrbitBootstrap.
/// </summary>
public class MenuShotDirector : MonoBehaviour
{
    public MenuShuttleTour tour;
    public Camera cam;

    [Tooltip("Closest the camera may ever get to the shuttle.")]
    public float minDistance = 30f;
    public float maxDistance = 95f;
    [Tooltip("Base orbit speed around the shuttle, degrees/second.")]
    public float orbitSpeed = 7f;

    float azimuth;          // degrees around the shuttle
    float seed;

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (cam == null || tour == null) { enabled = false; return; }
        seed = Random.Range(0f, 100f);
        azimuth = Random.Range(0f, 360f);
        cam.fieldOfView = 50f;
        Snap(true);
    }

    void LateUpdate() => Snap(false);

    void Snap(bool instant)
    {
        Transform sh = tour.transform;
        var body = tour.FocusBody;
        if (body == null) return;

        // Slowly breathing orbit: speed sways a little, elevation rolls
        // gently above/below the shuttle's horizon, distance dollies between
        // the limits on a slow noise curve. No cuts, ever.
        float t = Time.time;
        azimuth += orbitSpeed * (0.7f + 0.6f * Mathf.PerlinNoise(t * 0.05f, seed)) * Time.deltaTime;
        float elevation = Mathf.Lerp(-8f, 32f, Mathf.PerlinNoise(t * 0.03f, seed + 31f));
        float distance = Mathf.Lerp(minDistance, maxDistance, Mathf.PerlinNoise(t * 0.02f, seed + 62f));
        distance = Mathf.Max(distance, minDistance);   // hard floor — never inside

        // Stable frame: "up" is away from the planet the shuttle is touring,
        // so the horizon reads level while everything orbits.
        Vector3 up = (sh.position - body.Position).normalized;
        Vector3 refFwd = Vector3.Cross(up, Vector3.forward);
        if (refFwd.sqrMagnitude < 0.01f) refFwd = Vector3.Cross(up, Vector3.right);
        refFwd.Normalize();

        Quaternion swing = Quaternion.AngleAxis(azimuth, up) * Quaternion.AngleAxis(elevation, Vector3.Cross(up, refFwd));
        Vector3 pos = sh.position + swing * refFwd * distance;

        var rot = Quaternion.LookRotation((sh.position - pos).normalized, up);
        if (instant)
        {
            transform.SetPositionAndRotation(pos, rot);
        }
        else
        {
            // Critically damped-ish follow keeps micro-jitter out without lag
            // big enough to lose the shuttle from frame.
            transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-6f * Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }
}
