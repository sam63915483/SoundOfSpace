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
            // ...and they must not pull other bodies, so exclude them as a source here.
            Vector3 acceleration = CalculateAcceleration (bodies[i].Position, bodies[i], includeStaticAttractors: false);
            bodies[i].UpdateVelocity (acceleration, Universe.physicsTimeStep);
            //bodies[i].UpdateVelocity (bodies, Universe.physicsTimeStep);
        }

        for (int i = 0; i < bodies.Length; i++) {
            if (bodies[i].isStaticAttractor) continue;
            bodies[i].UpdatePosition (Universe.physicsTimeStep);
        }

    }

    // includeStaticAttractors: pass false for the body-on-body loop so a static
    // attractor (black hole) doesn't perturb planets. The ship/player/etc. leave
    // it true so they DO feel its pull.
    public static Vector3 CalculateAcceleration (Vector3 point, CelestialBody ignoreBody = null, bool includeStaticAttractors = true) {
        Vector3 acceleration = Vector3.zero;
        var inst = Instance;
        if (inst == null || inst.bodies == null) return acceleration;
        foreach (var body in inst.bodies) {
            if (body == ignoreBody) continue;
            if (!includeStaticAttractors && body.isStaticAttractor) continue;
            float sqrDst = (body.Position - point).sqrMagnitude;
            Vector3 forceDir = (body.Position - point).normalized;
            acceleration += forceDir * Universe.gravitationalConstant * body.mass / sqrDst;
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