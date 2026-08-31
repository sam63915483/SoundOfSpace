using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MENU-ONLY (MenuOrbit scene): flies the Shuttle_Lander on an endless
/// sightseeing tour — orbit Humble Abode, cruise to Icey Twin, Fiery Twin,
/// Cyclops, home again. Kinematic rb.MovePosition sweeps; ALL positions are
/// body-relative each step so rails motion and floating-origin shifts can't
/// bend the path. Added at runtime by MenuOrbitBootstrap.
///
/// v4 (Sam's spec): the lander flies HEAD-FIRST — pitched 90° so its belly
/// thrusters point backward and "shoot fire out the back" (the thruster
/// particle systems are found and kept burning). Nose turn rate is capped,
/// the horizon blends smoothly between planets, departures wait for
/// alignment with the next stop, and a clearance bubble around EVERY body
/// (sun and moons included) is enforced in orbit AND transfer — it can never
/// hit or pass through anything.
/// </summary>
public class MenuShuttleTour : MonoBehaviour
{
    [Tooltip("Seconds for one full lap around each planet.")]
    public float orbitPeriod = 45f;
    [Tooltip("Orbit radius = planet radius * this.")]
    public float orbitAltitudeMult = 2.1f;
    [Tooltip("Seconds for a planet-to-planet transfer leg.")]
    public float transferDuration = 18f;
    [Tooltip("Max attitude turn rate, degrees per second.")]
    public float maxTurnRate = 12f;

    static readonly string[] TourStops = { "Humble Abode", "Icey Twin", "Fiery Twin", "Cyclops" };

    Rigidbody rb;
    CelestialBody[] stops;
    CelestialBody[] allBodies;
    int stopIndex;

    enum Mode { Orbit, Transfer }
    Mode mode = Mode.Orbit;

    float orbitPhase, orbitStartPhase, orbitRadius;
    bool lapDone;

    Vector3 fromOffset, toOffset;       // BODY-RELATIVE endpoints (shift-proof)
    CelestialBody fromBody, toBody;
    float transferT;

    Vector3 lastPos;
    Vector3 smoothedUp = Vector3.up;    // exposed to the camera — never snaps

    public CelestialBody FocusBody { get; private set; }
    public CelestialBody NextBody => stops[(stopIndex + 1) % stops.Length];
    public float TransferBlend => mode == Mode.Transfer ? Mathf.Clamp01(transferT) : 0f;
    public CelestialBody TransferTarget => mode == Mode.Transfer ? toBody : FocusBody;
    /// Smoothly-blended "radially away from the current planet" — the camera's
    /// horizon reference. Guaranteed continuous across transfers.
    public Vector3 CurrentUp => smoothedUp;

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
        smoothedUp = (lastPos - FocusBody.Position).normalized;
        // First-frame attitude: head-first along the orbit (screen is still
        // black behind the menu fade, so this never reads as a snap).
        Vector3 tangent0 = new Vector3(-Mathf.Sin(orbitPhase), Mathf.Cos(orbitPhase), 0f);
        rb.rotation = HeadFirst(tangent0, -smoothedUp);
        transform.rotation = rb.rotation;
        Physics.SyncTransforms();

        // Engine fire: the game's own runtime-built plume rig (same system the
        // landing/liftoff uses — Sam: "in 1.6.7.7.7 the thruster fire looks
        // very good"). It anchors to the prefab's real nozzle transforms; the
        // engine bell fires along the belly, which is straight backward in
        // head-first flight. Added AFTER the bootstrap's behaviour sweep, so it
        // stays enabled. SetAltitude(150) each step = steady cruise plume
        // (no fireball ramp, no ground lights).
        _thrustFx = gameObject.AddComponent<ShuttleThrustFX>();
        _thrustFx.Initialize(transform);
        _thrustFx.Ignite();
        _thrustFx.SetAltitude(150f);
        Debug.Log("[MenuShuttleTour] ShuttleThrustFX ignited (game's own plume rig)");
    }

    ShuttleThrustFX _thrustFx;


    static Vector3 OrbitPoint(CelestialBody body, float phase, float radius)
    {
        return body.Position + new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * radius;
    }

    /// Head-first attitude: the shuttle's roof (+Y, opposite the belly
    /// thrusters) leads along the travel direction, so the thrusters point
    /// backward; its front face (+Z) is turned toward the planet.
    static Quaternion HeadFirst(Vector3 travelDir, Vector3 faceHint)
    {
        Vector3 face = Vector3.ProjectOnPlane(faceHint, travelDir);
        if (face.sqrMagnitude < 1e-4f) face = Vector3.ProjectOnPlane(Vector3.forward, travelDir);
        return Quaternion.LookRotation(face.normalized, travelDir);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 target;
        Vector3 radialUp;
        Vector3 faceHint;

        if (mode == Mode.Orbit)
        {
            orbitPhase += 2f * Mathf.PI / orbitPeriod * dt;
            target = OrbitPoint(FocusBody, orbitPhase, orbitRadius);
            radialUp = (target - FocusBody.Position).normalized;
            faceHint = -radialUp;   // front face turned toward the planet

            if (!lapDone && orbitPhase - orbitStartPhase >= 2f * Mathf.PI)
                lapDone = true;

            if (lapDone)
            {
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

            Vector3 upFrom = (target - fromBody.Position).normalized;
            Vector3 upTo = (target - toBody.Position).normalized;
            radialUp = Vector3.Slerp(upFrom, upTo, u).normalized;
            faceHint = Vector3.Slerp(-upFrom, -upTo, u).normalized;

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

        // Clearance bubble around EVERY body — moons, planets and the sun —
        // in both modes. The path bows around anything it would clip.
        foreach (var body in allBodies)
        {
            if (body == null || body.radius <= 0f || body == FocusBody) continue;
            Vector3 rel = target - body.Position;
            float clearance = body.radius * 1.5f + 40f;
            float d = rel.magnitude;
            if (d < clearance && d > 0.01f)
                target = body.Position + rel / d * clearance;
        }

        smoothedUp = Vector3.Slerp(smoothedUp, radialUp, 2f * dt).normalized;

        Vector3 vel = (target - lastPos) / dt;
        if (vel.sqrMagnitude > 0.01f)
        {
            var look = HeadFirst(vel.normalized, faceHint);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, look, maxTurnRate * dt));
        }
        rb.MovePosition(target);
        lastPos = target;

        if (_thrustFx != null) _thrustFx.SetAltitude(150f);   // steady cruise plume
    }
}
