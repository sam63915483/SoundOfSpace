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

    [Tooltip("How far off the shuttle the camera may glance (degrees).")]
    public float maxGlanceAngle = 40f;

    float azimuth;
    float seed;

    // Occasional glance: the camera pans part-way toward a landmark (the sun,
    // the black hole, the planet being toured or approached), then eases back.
    Transform glanceTarget;
    float glanceClock, glanceDuration, nextGlanceAt;

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
        nextGlanceAt = Time.time + Random.Range(6f, 12f);
        LateUpdate();
    }

    void PickGlance()
    {
        // Landmarks worth a look: sun, black hole, the planet under tour or
        // the one being approached. Prefer whichever is NOT where the shuttle
        // already is on screen.
        Transform best = null;
        float bestScore = -1f;
        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null) continue;
            bool landmark = b.bodyType == CelestialBody.BodyType.Sun || b.isStaticAttractor
                            || b == tour.FocusBody || b == tour.TransferTarget;
            if (!landmark) continue;
            Vector3 toB = (b.Position - transform.position).normalized;
            Vector3 toSh = (tour.transform.position - transform.position).normalized;
            float away = 1f - Vector3.Dot(toB, toSh);           // reward off-axis targets
            float size = Mathf.Clamp01(b.radius / Vector3.Distance(transform.position, b.Position) * 8f);
            float score = away * 0.6f + size + Random.value * 0.3f;
            if (score > bestScore) { bestScore = score; best = b.transform; }
        }
        glanceTarget = best;
        glanceDuration = Random.Range(5f, 9f);
        glanceClock = 0f;
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

        var lookAtShuttle = Quaternion.LookRotation((sh.position - pos).normalized, up);

        // Glances: every so often, pan up to maxGlanceAngle toward a landmark
        // (sun / black hole / a planet), ease there and back, then resume.
        if (glanceTarget == null && t >= nextGlanceAt) PickGlance();
        var rot = lookAtShuttle;
        if (glanceTarget != null)
        {
            glanceClock += Time.deltaTime;
            float w = Mathf.Sin(Mathf.Clamp01(glanceClock / glanceDuration) * Mathf.PI); // 0→1→0
            var lookAtTarget = Quaternion.LookRotation((glanceTarget.position - pos).normalized, up);
            rot = Quaternion.RotateTowards(lookAtShuttle, lookAtTarget, maxGlanceAngle * w);
            if (glanceClock >= glanceDuration)
            {
                glanceTarget = null;
                nextGlanceAt = t + Random.Range(8f, 16f);
            }
        }

        transform.SetPositionAndRotation(pos, rot);
    }
}
