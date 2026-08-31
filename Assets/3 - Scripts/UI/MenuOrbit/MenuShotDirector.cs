using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): one continuous orbiting camera around the
/// shuttle. Its only moves are orbiting the shuttle and dollying in/out; the
/// shuttle NEVER leaves the frame. Position is written exactly each frame
/// (lerping a child of a moving rig loses a tug-of-war with its parent — the
/// v2 cabin-twitch bug); rotation is rate-capped so nothing can ever snap.
///
/// v4 (Sam's spec):
///  • glances pan toward a landmark but keep the shuttle IN FRAME — the pan is
///    angle-capped and the camera dollies out + widens FOV while glancing,
///  • the camera's horizon uses the tour's smoothed up (no roll flips at
///    planet handoffs),
///  • elevation occasionally climbs to a TOP-DOWN view over the shuttle so
///    the orbited planet fills the frame below — the money shot.
/// </summary>
public class MenuShotDirector : MonoBehaviour
{
    public MenuShuttleTour tour;
    public Camera cam;

    [Tooltip("Closest approach; raised automatically to 1.5x the shuttle's bounds radius.")]
    public float minDistance = 26f;
    public float maxDistance = 65f;
    [Tooltip("Base orbit speed around the shuttle, degrees/second.")]
    public float orbitSpeed = 6f;
    [Tooltip("How far the glance may pan off the shuttle (degrees). Kept small enough that the shuttle stays in frame at the widened glance FOV.")]
    public float maxGlanceAngle = 30f;
    [Tooltip("Hard cap on how fast the camera may rotate, degrees/second.")]
    public float maxCamTurnRate = 40f;

    float azimuth;
    float seed;
    // Menu-scene-only lens: wider than gameplay so the world feels big while
    // the shuttle stays close and readable (Sam's zoom-in request). This
    // director only ever exists in MenuOrbit — gameplay FOV is untouched.
    float baseFov = 58f, glanceFov = 68f;

    Transform glanceTarget;
    float glanceClock, glanceDuration, nextGlanceAt;

    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (cam == null || tour == null) { enabled = false; return; }

        // MESH renderers only. A world-space ParticleSystemRenderer reports its
        // initial bounds at the WORLD ORIGIN — 12k units from the shuttle — and
        // one measurement with the engine flames included blew the "shuttle
        // radius" up to ~6000, parking the camera 15,000 units away. The cap is
        // belt-and-braces against any future stray renderer.
        var bounds = new Bounds(tour.transform.position, Vector3.zero);
        foreach (var r in tour.GetComponentsInChildren<Renderer>())
            if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) bounds.Encapsulate(r.bounds);
        float shuttleRadius = Mathf.Min(bounds.extents.magnitude, 25f);
        minDistance = Mathf.Max(minDistance, shuttleRadius * 1.5f);
        maxDistance = Mathf.Max(maxDistance, minDistance * 2.2f);
        Debug.Log($"[MenuShotDirector] shuttleRadius={shuttleRadius:0.0} camera range {minDistance:0.0}..{maxDistance:0.0}");

        seed = Random.Range(0f, 100f);
        azimuth = Random.Range(0f, 360f);
        cam.fieldOfView = baseFov;
        nextGlanceAt = Time.time + Random.Range(6f, 12f);
        Apply(true);
    }

    void LateUpdate() => Apply(false);

    void PickGlance()
    {
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
            float away = 1f - Vector3.Dot(toB, toSh);
            float size = Mathf.Clamp01(b.radius / Vector3.Distance(transform.position, b.Position) * 8f);
            float score = away * 0.6f + size + Random.value * 0.3f;
            if (score > bestScore) { bestScore = score; best = b.transform; }
        }
        glanceTarget = best;
        glanceDuration = Random.Range(5f, 9f);
        glanceClock = 0f;
    }

    void Apply(bool instant)
    {
        Transform sh = tour.transform;
        var body = tour.FocusBody;
        if (body == null) return;

        float t = Time.time;

        // Glance envelope first — it also drives dolly-out and FOV.
        float glanceW = 0f;
        if (glanceTarget == null && t >= nextGlanceAt) PickGlance();
        if (glanceTarget != null)
        {
            glanceClock += Time.deltaTime;
            glanceW = Mathf.Sin(Mathf.Clamp01(glanceClock / glanceDuration) * Mathf.PI); // 0→1→0
            if (glanceClock >= glanceDuration)
            {
                glanceTarget = null;
                nextGlanceAt = t + Random.Range(8f, 16f);
            }
        }

        azimuth += orbitSpeed * (0.7f + 0.6f * Mathf.PerlinNoise(t * 0.05f, seed)) * Time.deltaTime;
        // Elevation occasionally climbs toward top-down over the shuttle —
        // planet below, shuttle above it: the money shot.
        float elevation = Mathf.Lerp(-6f, 72f, Mathf.PerlinNoise(t * 0.025f, seed + 31f));
        float distance = Mathf.Lerp(minDistance, maxDistance, Mathf.PerlinNoise(t * 0.02f, seed + 62f));
        distance *= 1f + 0.25f * glanceW;   // slight pull-back while glancing so both fit

        // Horizon from the tour's smoothed up — continuous across planet
        // handoffs, so the camera can never roll-snap.
        Vector3 up = tour.CurrentUp;
        Vector3 refFwd = Vector3.Cross(up, Vector3.forward);
        if (refFwd.sqrMagnitude < 0.01f) refFwd = Vector3.Cross(up, Vector3.right);
        refFwd.Normalize();

        Quaternion swing = Quaternion.AngleAxis(azimuth, up)
                         * Quaternion.AngleAxis(elevation, Vector3.Cross(up, refFwd));
        Vector3 pos = sh.position + swing * refFwd * distance;

        var lookAtShuttle = Quaternion.LookRotation((sh.position - pos).normalized, up);
        var desired = lookAtShuttle;
        if (glanceTarget != null)
        {
            var lookAtTarget = Quaternion.LookRotation((glanceTarget.position - pos).normalized, up);
            // Angle-capped pan: at the widened glance FOV (~62° vertical,
            // ~90° horizontal at 16:9) a 30° offset keeps the shuttle safely
            // inside the frame.
            desired = Quaternion.RotateTowards(lookAtShuttle, lookAtTarget, maxGlanceAngle * glanceW);
        }
        cam.fieldOfView = Mathf.Lerp(baseFov, glanceFov, glanceW);

        if (instant)
        {
            transform.SetPositionAndRotation(pos, desired);
        }
        else
        {
            // Position exact (see header); rotation rate-capped: no snaps, ever.
            transform.position = pos;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, maxCamTurnRate * Time.deltaTime);
        }
    }
}
