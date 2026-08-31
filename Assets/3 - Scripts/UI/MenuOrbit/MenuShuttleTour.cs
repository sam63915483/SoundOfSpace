using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): flies the Shuttle_Lander on an endless
/// sightseeing tour — orbit Humble Abode, cruise to Icey Twin, Fiery Twin,
/// Cyclops, home again. Kinematic rb.MovePosition sweeps; ALL positions are
/// body-relative each step so rails motion and floating-origin shifts can't
/// bend the path. Added at runtime by MenuOrbitBootstrap.
///
/// v3 (Sam's review): the shuttle is a vehicle, not a UFO —
///  • departs an orbit only when its travel direction already points at the
///    next planet (no right-angle exits),
///  • nose turn rate hard-capped (slow, ship-like),
///  • "up" blends smoothly between planets during a transfer (no flips),
///  • transfers steer AROUND every celestial body (clearance bubble), so it
///    never flies through a planet or the sun.
/// </summary>
public class MenuShuttleTour : MonoBehaviour
{
    [Tooltip("Seconds for one full lap around each planet.")]
    public float orbitPeriod = 45f;
    [Tooltip("Orbit radius = planet radius * this.")]
    public float orbitAltitudeMult = 2.1f;
    [Tooltip("Seconds for a planet-to-planet transfer leg.")]
    public float transferDuration = 18f;
    [Tooltip("Max nose turn rate, degrees per second.")]
    public float maxTurnRate = 9f;

    static readonly string[] TourStops = { "Humble Abode", "Icey Twin", "Fiery Twin", "Cyclops" };

    Rigidbody rb;
    CelestialBody[] stops;
    CelestialBody[] allBodies;
    int stopIndex;

    enum Mode { Orbit, Transfer }
    Mode mode = Mode.Orbit;

    float orbitPhase, orbitStartPhase, orbitRadius;
    bool lapDone;                       // full lap complete, waiting for alignment

    Vector3 fromOffset, toOffset;       // BODY-RELATIVE endpoints (shift-proof)
    CelestialBody fromBody, toBody;
    float transferT;

    Vector3 lastPos;

    public CelestialBody FocusBody { get; private set; }
    public CelestialBody NextBody => stops[(stopIndex + 1) % stops.Length];
    /// 0→1 while transferring (used by the camera to blend its horizon).
    public float TransferBlend => mode == Mode.Transfer ? Mathf.Clamp01(transferT) : 0f;
    public CelestialBody TransferTarget => mode == Mode.Transfer ? toBody : FocusBody;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        allBodies = NBodySimulation.Bodies;
        stops = new CelestialBody[TourStops.Length];
        foreach (var b in allBodies)
            for (int i = 0; i < TourStops.Length; i++)
                if (b != null && b.bodyName == TourStops[i]) stops[i] = b;
        for (int i = 0; i < stops.Length; i++)
            if (stops[i] == null) { Debug.LogError($"[MenuShuttleTour] missing body {TourStops[i]}"); enabled = false; return; }

        stopIndex = 0;
        FocusBody = stops[0];
        orbitRadius = FocusBody.radius * orbitAltitudeMult;
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
        return body.Position + new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * radius;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 target;
        Vector3 up;

        if (mode == Mode.Orbit)
        {
            orbitPhase += 2f * Mathf.PI / orbitPeriod * dt;
            target = OrbitPoint(FocusBody, orbitPhase, orbitRadius);
            up = (target - FocusBody.Position).normalized;

            if (!lapDone && orbitPhase - orbitStartPhase >= 2f * Mathf.PI)
                lapDone = true;

            if (lapDone)
            {
                // Depart only when already travelling toward the next stop —
                // the exit is a gentle peel-off, not a right-angle yank.
                Vector3 tangent = new Vector3(-Mathf.Sin(orbitPhase), Mathf.Cos(orbitPhase), 0f);
                Vector3 toNext = (NextBody.Position - target).normalized;
                if (Vector3.Dot(tangent, toNext) > 0.85f)
                {
                    fromBody = FocusBody;
                    toBody = NextBody;
                    stopIndex = (stopIndex + 1) % stops.Length;
                    fromOffset = target - fromBody.Position;
                    Vector3 approach = (fromBody.Position - toBody.Position).normalized;
                    toOffset = approach * toBody.radius * orbitAltitudeMult;
                    transferT = 0f;
                    lapDone = false;
                    mode = Mode.Transfer;
                }
            }
        }
        else
        {
            transferT += dt / transferDuration;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transferT));
            Vector3 a = fromBody.Position + fromOffset;
            Vector3 b = toBody.Position + toOffset;
            float arc = Mathf.Sin(u * Mathf.PI) * Vector3.Distance(a, b) * 0.06f;
            target = Vector3.Lerp(a, b, u) + Vector3.forward * arc;

            // Clearance bubble: never inside 1.5x any body's radius (+margin).
            foreach (var body in allBodies)
            {
                if (body == null || body.radius <= 0f) continue;
                Vector3 rel = target - body.Position;
                float clearance = body.radius * 1.5f + 40f;
                float d = rel.magnitude;
                if (d < clearance && d > 0.01f)
                    target = body.Position + rel / d * clearance;
            }

            // Horizon rolls smoothly from the old planet's up to the new one's.
            Vector3 upFrom = (target - fromBody.Position).normalized;
            Vector3 upTo = (target - toBody.Position).normalized;
            up = Vector3.Slerp(upFrom, upTo, u).normalized;

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

        Vector3 vel = (target - lastPos) / dt;
        if (vel.sqrMagnitude > 0.01f)
        {
            var look = Quaternion.LookRotation(vel.normalized, up);
            // Hard cap on turn rate: ship-like, never UFO-flippy.
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, look, maxTurnRate * dt));
        }
        rb.MovePosition(target);
        lastPos = target;
    }
}
