using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent (typeof (Rigidbody))]
public class CelestialBody : GravityObject {

    public enum BodyType { Planet, Moon, Sun }
    public BodyType bodyType;
    public float radius;
    public float surfaceGravity;
    public Vector3 initialVelocity;
    public string bodyName = "Unnamed";
    // When true this body still attracts the player and ship and is fully
    // targetable, but is NOT a gravity source for other celestial bodies and is
    // not integrated by the N-body sim (it stays where placed). Used for the
    // black hole, which must not perturb the sun/planet orbits. See
    // NBodySimulation.FixedUpdate / CalculateAcceleration.
    public bool isStaticAttractor = false;
    /// Bodies sharing a non-empty orbitGroup do NOT pull on each other.
    ///
    /// This exists for the twins. Two worlds that read as a close pair is a
    /// look, and the sun makes it physically impossible here: a pair only stays
    /// bound well inside its Hill radius, that radius grows with distance from
    /// the sun, and a compact solar system means nothing is far from the sun.
    /// At 6020 out, a 998-wide pair of radius-300 planets needs surface gravity
    /// ~133 to hold together — unwalkable. Left interacting at that spacing they
    /// eject each other past 1,000,000 within the hour (measured).
    ///
    /// So the twins are placed CO-ORBITALLY — same orbital radius, offset along
    /// the orbit — and simply don't feel each other. Same radius means identical
    /// angular speed, so they hold their spacing exactly, forever, with no drift
    /// to soak-test. The player and the ship still feel BOTH at full strength;
    /// only the body-on-body loop skips the pair (CalculateAcceleration only
    /// applies the skip when the thing being accelerated is itself a grouped
    /// body, and the player passes ignoreBody = null).
    public string orbitGroup = "";

    /// Set on a FOLLOWER to lock it beside a leader forever.
    ///
    /// orbitGroup alone (no mutual gravity, same orbital radius) keeps the twins
    /// paired in theory, but they are still perturbed INDEPENDENTLY by the sun's
    /// own wobble and by passing planets, and at ~1000 apart it only takes a
    /// little accumulated difference to close the gap — measured, they overlap
    /// after about an hour. Slaving removes the whole failure mode instead of
    /// making it slower: the follower is not integrated at all, it is placed each
    /// step at the leader's own orbital radius, rotated by a fixed angle about
    /// the orbit normal. Identical radius and identical speed, by construction,
    /// permanently. It still has full mass and full surface gravity — it just
    /// doesn't get its own opinion about where to be.
    public CelestialBody coOrbitLeader;
    /// Angle (degrees) around the orbit by which this follower trails its leader.
    /// Captured from the authored placement, so moving either in the editor and
    /// re-running the rescale keeps whatever spacing was set.
    public float coOrbitAngle;

    /// > 0 turns coOrbitLeader into a SATELLITE lock instead of a co-orbital one:
    /// this body circles its leader at exactly this radius, in the leader's own
    /// orbital plane, forever.
    ///
    /// Same reasoning as the twins, different constraint. A real moon has to sit
    /// inside its planet's Hill radius, and that radius shrinks in proportion to
    /// the planet's distance from the sun — while the planet's RADIUS doesn't
    /// shrink at all. Squeeze the system and the moon gets crushed between a
    /// surface that stays the same size and a Hill radius that keeps closing:
    /// below about 0.47x the original spacing there is no orbit that is both
    /// stable and clear of the ground. Placing the moon instead of simulating it
    /// removes the Hill limit entirely, so the only remaining constraint is
    /// geometric — don't hit the surface — and the system can be as tight as it
    /// looks good at. The moon keeps full mass and full surface gravity.
    public float satelliteOrbitRadius = 0f;
    /// Seconds for one lap around the leader. Kept from the moon's real orbital
    /// period at the time of the rescale, so it doesn't visibly change speed.
    public float satellitePeriod = 0f;

    /// PINNED: not integrated by the n-body sim (never moves on its own) but
    /// still a full gravity source for the player/ship. Used by the SUN in the
    /// clockwork solar system (2026-08-31): a free sun gets momentum pumped into
    /// it by rails-placed bodies (they pull with no reaction force) and wanders
    /// hundreds of thousands of units within hours, dragging every orbit into
    /// chaos — measured, see docs/DAY_NIGHT_CLOCKS.md. Unlike isStaticAttractor
    /// it DOES act on other celestial bodies (moot once everything is on rails,
    /// but correct if a free body is ever added back).
    public bool isPinned = false;

    /// > 0 puts this body on a CLOCKWORK RAIL: an exact circular orbit around
    /// the pinned sun, one lap per this many seconds, placed analytically each
    /// step (same play-proven MovePosition sweep as satellite moons). This is
    /// the planet day-length DESIGN KNOB — planets don't spin, so local solar
    /// day == this period exactly. The free n-body version of this system
    /// destabilized measurably within the hour and catastrophically within 3.5
    /// (twins spiralled INTO the sun; see docs/DAY_NIGHT_CLOCKS.md), which a
    /// 2.5-hour Majora-style loop can't live with. The orbit plane and
    /// direction come from the body's current sun-relative position and
    /// velocity, so a loaded save resumes cleanly from wherever it was.
    public float railPeriod = 0f;

    // Rail runtime state (owned by NBodySimulation). The phase advances in
    // DOUBLE and the position is computed analytically from a fixed basis each
    // step, so neither radius nor period can drift by float accumulation —
    // measured, the incremental rotate-by-quaternion version crept +1.6% radius
    // over 7 hours. railLastRel is SUN-RELATIVE so floating-origin shifts don't
    // false-trigger the rebase; an external teleport (save load) rebases the
    // rail from wherever the body now is.
    [System.NonSerialized] public bool railInit;
    [System.NonSerialized] public double railPhase, railRadius, railOmega;
    [System.NonSerialized] public Vector3 railU, railW, railLastRel;
    [System.NonSerialized] public float satellitePhase;


    public Vector3 velocity { get; private set; }
    public float mass { get; private set; }
    Rigidbody rb;

    void Awake () {

        rb = GetComponent<Rigidbody> ();
        velocity = initialVelocity;
        RecalculateMass ();
    }

    public void UpdateVelocity (CelestialBody[] allBodies, float timeStep) {
        foreach (var otherBody in allBodies) {
            if (otherBody != this) {
                float sqrDst = (otherBody.rb.position - rb.position).sqrMagnitude;
                Vector3 forceDir = (otherBody.rb.position - rb.position).normalized;

                Vector3 acceleration = forceDir * Universe.gravitationalConstant * otherBody.mass / sqrDst;
                velocity += acceleration * timeStep;
            }
        }
    }

    public void UpdateVelocity (Vector3 acceleration, float timeStep) {
        velocity += acceleration * timeStep;
    }

    public void UpdatePosition (float timeStep) {
        rb.MovePosition (rb.position + velocity * timeStep);

    }

    void OnValidate () {
        RecalculateMass ();
        if (GetComponentInChildren<CelestialBodyGenerator> ()) {
            GetComponentInChildren<CelestialBodyGenerator> ().transform.localScale = Vector3.one * radius;
        }
        gameObject.name = bodyName;
    }

    public void RecalculateMass () {
        mass = surfaceGravity * radius * radius / Universe.gravitationalConstant;
        Rigidbody.mass = mass;
    }

    public Rigidbody Rigidbody {
        get {
            if (!rb) {
                rb = GetComponent<Rigidbody> ();
            }
            return rb;
        }
    }

    public Vector3 Position {
        get {
            return rb.position;
        }
    }

    // Restore exact orbital state from a save. Sets both rb and transform so
    // the next physics step uses the right values without a one-frame drift.
    public void ApplySavedState (Vector3 worldPos, Quaternion worldRot, Vector3 worldVel) {
        if (rb == null) rb = GetComponent<Rigidbody> ();
        rb.position = worldPos;
        rb.rotation = worldRot;
        transform.position = worldPos;
        transform.rotation = worldRot;
        velocity = worldVel;
    }

    // Per-step placement for co-orbit/satellite FOLLOWERS (2026-08-27, Sam's
    // call after the Icey Twin landings): a kinematic MovePosition SWEEP, not
    // a teleport. The old rb.position write gave the follower's surface ZERO
    // contact velocity, so a physics player standing on Icey Twin was swept
    // ~2.7 m through the step-frozen terrain every tick at orbital speed and
    // PhysX ejected them — the planet was simply unwalkable. MovePosition
    // carries contact velocity exactly like every SIMULATED body's
    // UpdatePosition already does, and with Interpolate the follower also
    // renders smoothly for the first time. The teleport survives only for
    // genuinely large jumps (first placement after a load/warp), where a
    // sweep would plough through the world.
    public void ApplyPlacedState (Vector3 worldPos, Vector3 worldVel) {
        if (rb == null) rb = GetComponent<Rigidbody> ();
        if ((worldPos - rb.position).sqrMagnitude > 50f * 50f) {
            rb.position = worldPos;
            transform.position = worldPos;
            velocity = worldVel;
        } else {
            // velocity = the ACTUAL sweep this step will perform, not the
            // leader-derived estimate (2026-08-27, playtest 14): the estimate
            // differs from the true sweep by one step of orbital curvature
            // (~3 cm/s on the twins), and everything that velocity-matches the
            // ground — the player's grounded grip above all — inherited that
            // bias as a permanent visible slide on Icey Twin.
            velocity = (worldPos - rb.position) / Universe.physicsTimeStep;
            rb.MovePosition (worldPos);
        }
    }
}