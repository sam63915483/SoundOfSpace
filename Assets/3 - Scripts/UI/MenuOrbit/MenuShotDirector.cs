using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): one continuous camera orbiting the shuttle.
/// Position is written exactly each frame (lerping a child of a moving rig
/// loses the parent tug-of-war — the old cabin-twitch bug); rotation is
/// rate-capped, and shot changes GLIDE by easing the orbit parameters —
/// cuts and snaps are impossible by construction.
///
/// v5 (Sam's spec) — a rotating three-way program, roughly equal thirds:
///  • PLANET shot: zoomed out, high elevation — the top-down money shot of the
///    shuttle tracing its orbit over the planet's face, look biased toward the
///    planet (capped so the shuttle stays in frame),
///  • SHUTTLE shot: close and low — the ship against space/whatever's there,
///  • LANDMARK shot: look biased toward the sun or the black hole.
/// </summary>
public class MenuShotDirector : MonoBehaviour
{
    public MenuShuttleTour tour;
    public Camera cam;

    [Tooltip("Closest approach; raised automatically to 1.5x the shuttle's bounds radius.")]
    public float minDistance = 26f;
    public float maxDistance = 65f;
    [Tooltip("Extra distance multiplier for the zoomed-out planet money shot.")]
    public float planetShotDistanceMult = 2.2f;
    [Tooltip("Base orbit speed around the shuttle, degrees/second.")]
    public float orbitSpeed = 6f;
    [Tooltip("Hard cap on how fast the camera may rotate, degrees/second.")]
    public float maxCamTurnRate = 40f;

    enum Shot { Planet, Shuttle, Landmark }
    Shot shot;
    float shotEndsAt;
    Transform landmark;
    CelestialBody sunBody;

    public string CurrentShotName => shot.ToString();

    // The Planet money shot only plays over the SUNLIT hemisphere. Threshold:
    // START a shot only DEEP in daylight (+0.3 — enough runway that the shot
    // gets its full hold before the terminator), but only BAIL once genuinely
    // into night (-0.25). The old symmetric gate made the top-down "look for
    // like a second" (Sam): a 45s orbit crosses daylight in ~22s, so a shot
    // started near the terminator bailed almost immediately.
    bool ShuttleOverDaylight(float threshold)
    {
        if (sunBody == null || tour.FocusBody == null) return true;
        Vector3 sunDir = (sunBody.Position - tour.FocusBody.Position).normalized;
        Vector3 shDir = (tour.transform.position - tour.FocusBody.Position).normalized;
        return Vector3.Dot(sunDir, shDir) > threshold;
    }

    float azimuth, seed;

    // Eased state — these glide toward each shot's targets, so a shot change
    // is a slow camera move, never a cut.
    float elevation = 10f, distance = 40f, lookWeight, fov = 58f;
    float targetElevation, targetDistance, targetLookWeight, targetFov, lookCap;
    Transform lookTarget;

    // Thirds framing (Sam's spec): the shuttle lives in the LEFT or RIGHT
    // vertical third of the screen — the middle third belongs to the menu
    // buttons — and only drifts through the middle while the eased offset
    // crosses sides between shots.
    float frameOffsetDeg, targetFrameOffsetDeg;
    int frameSide = 1;

    // 0→1 while the Planet shot runs: eases the camera onto the line through
    // the shuttle FROM the planet, so looking at the shuttle GUARANTEES the
    // planet fills the frame behind it. The elevation/look-bias version only
    // showed the planet if the orbit happened to cooperate (Sam: "the camera
    // points opposite from it and doesn't show it once").
    float planetAlign;

    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (tour == null) tour = FindObjectOfType<MenuShuttleTour>();
        if (cam == null || tour == null) { enabled = false; return; }

        // MESH renderers only: a world-space ParticleSystemRenderer reports its
        // initial bounds at the world origin (12k away) and once measured the
        // "shuttle radius" as ~6000. Cap is belt-and-braces.
        var bounds = new Bounds(tour.transform.position, Vector3.zero);
        foreach (var r in tour.GetComponentsInChildren<Renderer>())
            if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) bounds.Encapsulate(r.bounds);
        float shuttleRadius = Mathf.Min(bounds.extents.magnitude, 25f);
        minDistance = Mathf.Max(minDistance, shuttleRadius * 1.5f);
        maxDistance = Mathf.Max(maxDistance, minDistance * 2.2f);
        Debug.Log($"[MenuShotDirector] shuttleRadius={shuttleRadius:0.0} camera range {minDistance:0.0}..{maxDistance:0.0}");

        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) sunBody = b;

        seed = Random.Range(0f, 100f);
        azimuth = Random.Range(0f, 360f);
        // The menu always OPENS on the money shot: the tour starts over Humble
        // Abode's sunlit side, so the first thing seen is shuttle + planet.
        StartShot(Shot.Planet);
        planetAlign = 1f;
        // Open ON the shot's pose (screen is still black behind the menu fade).
        elevation = targetElevation; distance = targetDistance;
        lookWeight = targetLookWeight; fov = targetFov;
        Apply(true);
    }

    void StartShot(Shot s)
    {
        shot = s;
        // The money shot holds longest — it was over before the slow elevation
        // climb even composed the frame.
        shotEndsAt = Time.time + (s == Shot.Planet ? Random.Range(14f, 20f) : Random.Range(9f, 15f));
        // Alternate the screen side most of the time so the shuttle spends its
        // life in the outer thirds, crossing the button column only in passing.
        if (Random.value < 0.75f) frameSide = -frameSide;
        targetFrameOffsetDeg = frameSide * Random.Range(15f, 21f);
        switch (s)
        {
            case Shot.Planet:   // the money shot: top-down over the orbited planet
                targetElevation = Random.Range(66f, 80f);   // truly overhead → planet directly beyond the shuttle
                targetDistance = maxDistance * planetShotDistanceMult;
                lookTarget = tour.FocusBody != null ? tour.FocusBody.transform : null;
                targetLookWeight = 1f; lookCap = 30f;
                targetFov = 58f;
                break;
            case Shot.Shuttle:  // the ship against space / whatever drifts by
                targetElevation = Random.Range(-10f, 25f);
                targetDistance = Random.Range(minDistance, minDistance * 1.6f);
                lookTarget = null;
                targetLookWeight = 0f; lookCap = 0f;
                targetFov = 58f;
                break;
            default:            // Landmark: sun or black hole
                landmark = PickLandmark();
                targetElevation = Random.Range(0f, 30f);
                targetDistance = Random.Range(minDistance * 1.3f, maxDistance);
                lookTarget = landmark;
                targetLookWeight = 1f; lookCap = 30f;
                targetFov = 66f;
                break;
        }
    }

    Transform PickLandmark()
    {
        Transform sun = null, hole = null;
        foreach (var b in NBodySimulation.Bodies)
        {
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) sun = b.transform;
            if (b.isStaticAttractor) hole = b.transform;
        }
        if (sun == null) return hole;
        if (hole == null) return sun;
        return Random.value < 0.5f ? sun : hole;
    }

    void NextShot()
    {
        // Rotate through all three with a shuffle-ish pick: never repeat, and
        // don't start a Planet shot over the night side.
        Shot next;
        int guard = 0;
        do { next = (Shot)Random.Range(0, 3); }
        while ((next == shot || (next == Shot.Planet && !ShuttleOverDaylight(0.3f))) && ++guard < 12);
        if (next == Shot.Planet && !ShuttleOverDaylight(0.3f)) next = Shot.Shuttle;
        StartShot(next);
    }

    void LateUpdate() => Apply(false);

    void Apply(bool instant)
    {
        Transform sh = tour.transform;
        var body = tour.FocusBody;
        if (body == null) return;

        float t = Time.time;
        if (!instant && t >= shotEndsAt) NextShot();
        // Refresh the planet shot's target if the tour moved on mid-shot, and
        // bail out of it the moment the shuttle crosses into night — the
        // start-only gate left 81% of planet-shot frames over the dark side
        // (tracker data): a 45s orbit outruns a 9-15s shot.
        if (shot == Shot.Planet && tour.FocusBody != null) lookTarget = tour.FocusBody.transform;
        if (!instant && shot == Shot.Planet && !ShuttleOverDaylight(-0.25f)) StartShot(Shot.Shuttle);

        float dt = instant ? 0f : Time.deltaTime;
        // Glide every parameter — shot changes are camera MOVES, not cuts.
        // The Planet shot climbs faster so the top-down composes within ~4s.
        elevation = Mathf.MoveTowards(elevation, targetElevation, (shot == Shot.Planet ? 17f : 9f) * dt);
        distance = Mathf.MoveTowards(distance, targetDistance, 18f * dt);
        lookWeight = Mathf.MoveTowards(lookWeight, targetLookWeight, 0.35f * dt);
        fov = Mathf.MoveTowards(fov, targetFov, 5f * dt);

        azimuth += orbitSpeed * (0.7f + 0.6f * Mathf.PerlinNoise(t * 0.05f, seed)) * dt;
        float elevWobble = (Mathf.PerlinNoise(t * 0.06f, seed + 47f) - 0.5f) * 6f;

        Vector3 up = tour.CurrentUp;   // smoothed by the tour — never snaps
        Vector3 refFwd = Vector3.Cross(up, Vector3.forward);
        if (refFwd.sqrMagnitude < 0.01f) refFwd = Vector3.Cross(up, Vector3.right);
        refFwd.Normalize();

        // Axis is Cross(refFwd, up), NOT Cross(up, refFwd): with the flipped
        // axis, positive elevation pushed the camera BELOW the shuttle (planet
        // side, looking up at it with empty space behind) — the exact mirror of
        // the top-down money shot, which is what Sam kept seeing. With this
        // axis, +elevation genuinely climbs radially ABOVE the shuttle so the
        // orbited planet fills the frame beyond it.
        Quaternion swing = Quaternion.AngleAxis(azimuth, up)
                         * Quaternion.AngleAxis(elevation + elevWobble, Vector3.Cross(refFwd, up));
        Vector3 pos = sh.position + swing * refFwd * distance;

        // Planet shot: ease onto the guaranteed-framing position — camera on
        // the anti-planet side of the shuttle (slight lateral drift for life).
        planetAlign = Mathf.MoveTowards(planetAlign, shot == Shot.Planet ? 1f : 0f, 0.4f * dt);
        if (planetAlign > 0.001f && body != null)
        {
            float drift = Mathf.Sin(t * 0.35f + seed);
            Vector3 lateral = Vector3.Cross(up, Vector3.forward).normalized * (10f * drift);
            Vector3 alignedPos = sh.position + up * distance + lateral;
            pos = Vector3.Lerp(pos, alignedPos, Mathf.SmoothStep(0f, 1f, planetAlign));
        }

        var lookAtShuttle = Quaternion.LookRotation((sh.position - pos).normalized, up);
        var desired = lookAtShuttle;
        if (lookTarget != null && lookWeight > 0.001f)
        {
            var lookAtTarget = Quaternion.LookRotation((lookTarget.position - pos).normalized, up);
            desired = Quaternion.RotateTowards(lookAtShuttle, lookAtTarget, lookCap * lookWeight);
        }

        // Thirds framing: yaw the final look so the shuttle rides an outer
        // third. Eases between sides (briefly crossing the middle), and backs
        // off while glancing so the combined offset can't push the shuttle out
        // of frame.
        frameOffsetDeg = Mathf.MoveTowards(frameOffsetDeg, targetFrameOffsetDeg, 5f * dt);
        float effOffset = frameOffsetDeg * (1f - 0.7f * lookWeight);
        desired = Quaternion.AngleAxis(effOffset, desired * Vector3.up) * desired;

        cam.fieldOfView = fov;

        if (instant)
        {
            transform.SetPositionAndRotation(pos, desired);
        }
        else
        {
            transform.position = pos;   // exact — see header
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, maxCamTurnRate * Time.deltaTime);
        }
    }
}
