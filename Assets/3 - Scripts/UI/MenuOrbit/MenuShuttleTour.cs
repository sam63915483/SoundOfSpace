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
    // Diagnostic chatter switch. static readonly (never const) so the guarded
    // bodies don't become CS0162 unreachable code — see FeatureVault.cs.
    // Flip to true when investigating menu shuttle path spikes again.
    static readonly bool Verbose = false;

    [Tooltip("Seconds for one full lap around each planet.")]
    public float orbitPeriod = 45f;
    // Per-stop orbit altitude (radius multiplier), chosen so NO orbit ever
    // intersects any body's clearance bubble — that intersection was the "UFO
    // jerk": at a uniform 2.1x, the orbit around Fiery passed inside Icey's
    // bubble every lap, and Humble Abode's orbit sat in Constant Companion's
    // crossing zone, so the hard clamp shoved the shuttle sideways at every
    // conjunction. HA 1.7x (340: inside CC's 472-orbit minus its 115 bubble),
    // twins 1.6x (480: partner at 1000 minus its 490 bubble), Cyclops 2.1x
    // (1050: clear of both moons).
    static readonly float[] StopAltMult = { 1.7f, 1.6f, 1.6f, 2.1f };
    [Tooltip("Cruise speed between planets, units/second. Leg duration = distance / this (clamped 12-55s) — constant TIME made the long Cyclops leg scream along at 1600 u/s, and at that speed every moon-skirt graze was a violent yank.")]
    public float cruiseSpeed = 350f;
    float transferDuration = 18f;   // derived per leg from cruiseSpeed
    [Tooltip("Max attitude turn rate, degrees per second.")]
    public float maxTurnRate = 12f;

    static readonly string[] TourStops = { "Humble Abode", "Icey Twin", "Fiery Twin", "Cyclops" };

    Rigidbody rb;
    CelestialBody[] stops;
    CelestialBody[] allBodies;
    CelestialBody sun;
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
    float _lastSpikeLog;

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
        foreach (var b in allBodies)
            if (b != null && b.bodyType == CelestialBody.BodyType.Sun) sun = b;
        stops = new CelestialBody[TourStops.Length];
        foreach (var b in allBodies)
            for (int i = 0; i < TourStops.Length; i++)
                if (b != null && b.bodyName == TourStops[i]) stops[i] = b;
        for (int i = 0; i < stops.Length; i++)
            if (stops[i] == null) { Debug.LogError($"[MenuShuttleTour] missing body {TourStops[i]}"); enabled = false; return; }

        stopIndex = 0;
        FocusBody = stops[0];
        orbitRadius = FocusBody.radius * StopAltMult[0];
        Vector3 off = transform.position - FocusBody.Position;
        orbitPhase = Mathf.Atan2(off.y, off.x);
        orbitStartPhase = orbitPhase;
        lastPos = OrbitPoint(FocusBody, orbitPhase, orbitRadius);
        rb.position = lastPos;
        transform.position = lastPos;
        smoothedUp = (lastPos - FocusBody.Position).normalized;
        // First-frame attitude: head-first along the orbit (the menu's flat
        // nebula still covers the 3D view here, so this never reads as a snap).
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
        Debug.Log("[MenuShuttleTour] v7 (cruise-speed + post-avoidance arrival + high twin arc) — ShuttleThrustFX ignited");
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
        bool arriving = false;

        if (mode == Mode.Orbit)
        {
            orbitPhase += 2f * Mathf.PI / orbitPeriod * dt;
            // Glide the inherited arrival radius onto the designed one.
            orbitRadius = Mathf.MoveTowards(orbitRadius, FocusBody.radius * StopAltMult[stopIndex], 25f * dt);
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
                    // Entry point biased toward the SUNLIT side of the target,
                    // so each new orbit begins over daylight (dark sides read
                    // wrong from up close — Sam's review).
                    Vector3 approach = (fromBody.Position - toBody.Position).normalized;
                    Vector3 sunward = sun != null ? (sun.Position - toBody.Position).normalized : approach;
                    Vector3 entryDir = Vector3.Slerp(approach, sunward, 0.5f).normalized;
                    toOffset = entryDir * toBody.radius * StopAltMult[stopIndex];
                    transferDuration = Mathf.Clamp(
                        Vector3.Distance(target, toBody.Position + toOffset) / cruiseSpeed, 12f, 55f);
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
            // Short hops arc well OUT of the orbital plane: the twins sit 1000
            // apart with ~490-unit bubbles each — a flat path threads a 20-unit
            // gap. Long cruises stay nearly flat.
            float dist = Vector3.Distance(a, b);
            // 0.35 on the shortest hops: the twin-to-twin leg at that fraction
            // clears BOTH 490-unit bubbles in z entirely (594u closest 3D
            // approach), so avoidance never engages and the leg is push-free.
            float arcFrac = Mathf.Lerp(0.35f, 0.06f, Mathf.Clamp01(dist / 5000f));
            float arc = Mathf.Sin(u * Mathf.PI) * dist * arcFrac;
            target = Vector3.Lerp(a, b, u) + Vector3.forward * arc;

            Vector3 upFrom = (target - fromBody.Position).normalized;
            Vector3 upTo = (target - toBody.Position).normalized;
            radialUp = Vector3.Slerp(upFrom, upTo, u).normalized;
            faceHint = Vector3.Slerp(-upFrom, -upTo, u).normalized;

            arriving = transferT >= 1f;   // finalized AFTER the avoidance pass
        }

        // SOFT clearance around EVERY body — moons, planets and the sun. The
        // exclusion is ORBIT-ONLY: during a transfer even the (still-focused)
        // departed planet must repel, because the moving twins can drag the
        // path back across it — the tracker caught the shuttle sinking to
        // clearance 0.61 inside Icey exactly this way. The push ramps in
        // progressively from the outer skirt (v1 "soft" math saturated to a
        // hard clamp the moment the bubble was crossed).
        foreach (var body in allBodies)
        {
            if (body == null || body.radius <= 0f) continue;
            if (mode == Mode.Orbit && body == FocusBody) continue;   // engineered-safe circle
            Vector3 rel = target - body.Position;
            float clearance = body.radius * 1.5f + 40f;
            float soft = clearance * 1.35f;
            float d = rel.magnitude;
            if (d < soft && d > 0.01f)
            {
                // 0 at the skirt edge → 1 well inside the bubble; minD rises
                // continuously toward full clearance as the path presses in.
                float k = 1f - Mathf.Clamp01((d - clearance * 0.8f) / (soft - clearance * 0.8f));
                float minD = Mathf.Lerp(d, clearance, Mathf.SmoothStep(0f, 1f, k));
                // The DEPARTED planet's repulsion fades in over the first 30%
                // of the transfer: the exit orbit (480) sits just inside its
                // own bubble (490), and switching its exclusion off instantly
                // shoved the shuttle 7.4 units in one step (tracker: 75k u/s2).
                float w = (mode == Mode.Transfer && body == fromBody)
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transferT / 0.3f)) : 1f;
                if (minD > d) target = body.Position + rel / d * (d + (minD - d) * w);
            }
        }

        // Arrival is finalized from the POST-avoidance target: the entry point
        // sits just inside the new planet's bubble, so the push holds the real
        // position a few units out — inheriting the pre-push radius snapped the
        // shuttle ~7.5u on the arrival frame (tracker: the deterministic
        // 75287 u/s2 spike, to the decimal, every twin arrival).
        if (arriving)
        {
            FocusBody = toBody;
            Vector3 arrOff = target - FocusBody.Position;
            orbitRadius = arrOff.magnitude;
            orbitPhase = Mathf.Atan2(arrOff.y, arrOff.x);
            orbitStartPhase = orbitPhase;
            mode = Mode.Orbit;
        }

        smoothedUp = Vector3.Slerp(smoothedUp, radialUp, 2f * dt).normalized;

        Vector3 vel = (target - lastPos) / dt;
        if (vel.sqrMagnitude > 0.01f)
        {
            var look = HeadFirst(vel.normalized, faceHint);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, look, maxTurnRate * dt));
        }
        // Spike forensics: any single-step displacement far beyond cruise speed
        // gets logged with full context (throttled). Cruise ≈ 65 u/s ≈ 0.65/step.
        float stepLen = (target - lastPos).magnitude;
        if (stepLen > 2f && Time.time - _lastSpikeLog > 1f)
        {
            _lastSpikeLog = Time.time;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[MenuTour SPIKE] step={stepLen:0.00}u mode={mode} transferT={transferT:0.000} orbitR={orbitRadius:0.0} focus={FocusBody.bodyName}");
            foreach (var b in allBodies)
                if (b != null && b.radius > 0f && Vector3.Distance(target, b.Position) < b.radius * 3f)
                    sb.Append($" | {b.bodyName} d={Vector3.Distance(target, b.Position):0.0}");
            if (Verbose) Debug.LogWarning(sb.ToString());
        }

        rb.MovePosition(target);
        lastPos = target;

        if (_thrustFx != null) _thrustFx.SetAltitude(150f);   // steady cruise plume
    }
}
