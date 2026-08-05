using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NBodySimulation : MonoBehaviour {
    CelestialBody[] bodies;
    static NBodySimulation instance;

    void Awake () {

        bodies = FindObjectsOfType<CelestialBody> ();
        Time.fixedDeltaTime = Universe.physicsTimeStep;
        Debug.Log ("Setting fixedDeltaTime to: " + Universe.physicsTimeStep);
    }

    void FixedUpdate () {
        for (int i = 0; i < bodies.Length; i++) {
            // Static attractors (the black hole) are fixed and never integrated.
            if (bodies[i].isStaticAttractor) continue;
            if (bodies[i].coOrbitLeader != null) continue;      // follower: placed, not simulated
            // ...and they must not pull other bodies, so exclude them as a source here.
            Vector3 acceleration = CalculateAcceleration (bodies[i].Position, bodies[i], includeStaticAttractors: false);
            bodies[i].UpdateVelocity (acceleration, Universe.physicsTimeStep);
            //bodies[i].UpdateVelocity (bodies, Universe.physicsTimeStep);
        }

        for (int i = 0; i < bodies.Length; i++) {
            if (bodies[i].isStaticAttractor) continue;
            if (bodies[i].coOrbitLeader != null) continue;      // placed below instead
            bodies[i].UpdatePosition (Universe.physicsTimeStep);
        }

        // Co-orbital followers: not integrated, placed. See CelestialBody
        // .coOrbitLeader for why a pair this close can't be left to drift.
        for (int i = 0; i < bodies.Length; i++) {
            var f = bodies[i];
            if (f == null || f.coOrbitLeader == null) continue;
            var lead = f.coOrbitLeader;
            var sun = FindSun ();
            Vector3 origin = sun != null ? sun.Position : Vector3.zero;
            Vector3 r = lead.Position - origin;
            Vector3 v = lead.velocity;
            Vector3 normal = Vector3.Cross (r, v);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            var rot = Quaternion.AngleAxis (f.coOrbitAngle, normal.normalized);
            f.ApplySavedState (origin + rot * r, f.transform.rotation, rot * v);
        }
    }

    CelestialBody _sunCache;
    CelestialBody FindSun () {
        if (_sunCache != null) return _sunCache;
        foreach (var b in bodies) if (b != null && b.bodyType == CelestialBody.BodyType.Sun) { _sunCache = b; break; }
        return _sunCache;
    }

    // includeStaticAttractors: pass false for the body-on-body loop so a static
    // attractor (black hole) doesn't perturb planets. The ship/player/etc. leave
    // it true so they DO feel its pull.
    public static Vector3 CalculateAcceleration (Vector3 point, CelestialBody ignoreBody = null, bool includeStaticAttractors = true) {
        Vector3 acceleration = Vector3.zero;
        var inst = Instance;
        if (inst == null || inst.bodies == null) return acceleration;
        bool grouped = ignoreBody != null && !string.IsNullOrEmpty (ignoreBody.orbitGroup);
        foreach (var body in inst.bodies) {
            if (body == ignoreBody) continue;
            if (!includeStaticAttractors && body.isStaticAttractor) continue;
            // Co-orbital pairs don't pull on each other — see CelestialBody
            // .orbitGroup. Only applies when the accelerated thing IS a grouped
            // body; the player/ship pass ignoreBody = null and feel everything.
            if (grouped && body.orbitGroup == ignoreBody.orbitGroup) continue;
            // Shell-theorem-aware: inverse-square outside the body, linear
            // falloff to zero inside it. Without the interior case anything that
            // travels through a body (cave, tunnel) is flung by a divergent
            // 1/r². See Universe.GravityAcceleration.
            acceleration += Universe.GravityAcceleration (point, body);
        }

        return acceleration;
    }

    public static CelestialBody[] Bodies {
        get {
            var inst = Instance;
            return (inst != null && inst.bodies != null) ? inst.bodies : System.Array.Empty<CelestialBody> ();
        }
    }

    static NBodySimulation Instance {
        get {
            if (instance == null) {
                instance = FindObjectOfType<NBodySimulation> ();
            }
            return instance;
        }
    }
}