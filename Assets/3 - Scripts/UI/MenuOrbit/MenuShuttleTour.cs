using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): flies the Shuttle_Lander on an endless
/// sightseeing tour — one orbit of Humble Abode, transfer to Icey Twin, orbit,
/// Fiery Twin, orbit, Cyclops, orbit, back to Humble Abode, repeat. Kinematic
/// rb.MovePosition sweeps. ALL positions are computed body-relative each step,
/// so floating-origin shifts and the planets' own rail motion can't strand the
/// path. Added at runtime by MenuOrbitBootstrap — never present in gameplay.
/// </summary>
public class MenuShuttleTour : MonoBehaviour
{
    [Tooltip("Seconds for one full lap around each planet.")]
    public float orbitPeriod = 38f;
    [Tooltip("Orbit radius = planet radius * this.")]
    public float orbitAltitudeMult = 2.1f;
    [Tooltip("Seconds for a planet-to-planet transfer leg.")]
    public float transferDuration = 16f;

    static readonly string[] TourStops = { "Humble Abode", "Icey Twin", "Fiery Twin", "Cyclops" };

    Rigidbody rb;
    CelestialBody[] stops;
    int stopIndex;

    enum Mode { Orbit, Transfer }
    Mode mode = Mode.Orbit;

    // Orbit state (relative to the current body; plane = ecliptic, normal ±Z)
    float orbitPhase;          // radians
    float orbitStartPhase;
    float orbitRadius;

    // Transfer state — endpoints stored BODY-RELATIVE (origin-shift proof)
    Vector3 fromOffset, toOffset;
    CelestialBody fromBody, toBody;
    float transferT;

    Vector3 lastPos;

    public CelestialBody FocusBody { get; private set; }
    public CelestialBody NextBody => stops[(stopIndex + 1) % stops.Length];
    public float OrbitRadius => orbitRadius;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        stops = new CelestialBody[TourStops.Length];
        foreach (var b in NBodySimulation.Bodies)
            for (int i = 0; i < TourStops.Length; i++)
                if (b != null && b.bodyName == TourStops[i]) stops[i] = b;
        for (int i = 0; i < stops.Length; i++)
            if (stops[i] == null) { Debug.LogError($"[MenuShuttleTour] missing body {TourStops[i]}"); enabled = false; return; }

        stopIndex = 0;
        FocusBody = stops[0];
        orbitRadius = FocusBody.radius * orbitAltitudeMult;
        // Enter the orbit wherever we are relative to the planet right now.
        Vector3 off = transform.position - FocusBody.Position;
        orbitPhase = Mathf.Atan2(off.y, off.x);
        orbitStartPhase = orbitPhase;
        lastPos = OrbitPoint(FocusBody, orbitPhase, orbitRadius);
        rb.position = lastPos;
        transform.position = lastPos;
        Physics.SyncTransforms();
    }

    static Vector3 OrbitPoint(CelestialBody body, float phase, float radius)
    {
        // Ecliptic plane (all planets orbit in XY): basis X/Y around the body.
        return body.Position + new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * radius;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 target;

        if (mode == Mode.Orbit)
        {
            orbitPhase += 2f * Mathf.PI / orbitPeriod * dt;
            target = OrbitPoint(FocusBody, orbitPhase, orbitRadius);

            if (orbitPhase - orbitStartPhase >= 2f * Mathf.PI)
            {
                // One lap done — set up transfer to the next stop.
                fromBody = FocusBody;
                toBody = NextBody;
                stopIndex = (stopIndex + 1) % stops.Length;
                fromOffset = target - fromBody.Position;
                // Enter the next orbit on the side facing our current planet.
                Vector3 approach = (fromBody.Position - toBody.Position).normalized;
                float nextRadius = toBody.radius * orbitAltitudeMult;
                toOffset = approach * nextRadius;
                transferT = 0f;
                mode = Mode.Transfer;
            }
        }
        else
        {
            transferT += dt / transferDuration;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transferT));
            Vector3 a = fromBody.Position + fromOffset;
            Vector3 b = toBody.Position + toOffset;
            // Arc the path gently out of the ecliptic so transfers read as flight,
            // not a straight slide.
            float arc = Mathf.Sin(u * Mathf.PI) * Vector3.Distance(a, b) * 0.08f;
            target = Vector3.Lerp(a, b, u) + Vector3.forward * arc;

            if (transferT >= 1f)
            {
                FocusBody = toBody;
                orbitRadius = FocusBody.radius * orbitAltitudeMult;
                Vector3 off = target - FocusBody.Position;
                orbitPhase = Mathf.Atan2(off.y, off.x);
                orbitStartPhase = orbitPhase;
                mode = Mode.Orbit;
            }
        }

        // Nose along the motion, belly toward the planet.
        Vector3 vel = (target - lastPos) / dt;
        if (vel.sqrMagnitude > 0.01f)
        {
            Vector3 up = (target - FocusBody.Position).normalized;
            var look = Quaternion.LookRotation(vel.normalized, up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, look, 2f * dt));
        }
        rb.MovePosition(target);
        lastPos = target;
    }
}
