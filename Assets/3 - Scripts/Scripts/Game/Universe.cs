using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Universe {
    public const float gravitationalConstant = 0.0001f;

    // 0.01 = 100 Hz physics — the step the solar system was built and tuned
    // around, and now the ONLY value. NBodySimulation.Awake seeds
    // Time.fixedDeltaTime from this, and InputSettings.ApplyPhysicsRate re-pins
    // both in lockstep (they must match or the n-body sim desyncs from Unity's
    // solver and the planets run fast/slow).
    //
    // This used to be driven by a PHYSICS RATE slider (40–240 Hz). That was
    // removed: it bought no smoothness, and because NBodySimulation integrates
    // with semi-implicit Euler at exactly this step, changing it changed how
    // the ORBITS evolve. Full rationale in InputSettings.ApplyPhysicsRate.
    // A field rather than a const only so ApplyPhysicsRate can write it.
    public static float physicsTimeStep = 0.01f;

    public const bool cheatsEnabled = true;

    // Gravitational acceleration (m/s²) at `point` due to `body`.
    //
    // Outside the body this is the plain inverse-square law, unchanged from what
    // the callers used to inline.
    //
    // INSIDE the body (dst < radius) the inverse-square form is wrong and
    // dangerous: sqrDst -> 0 at the centre, so acceleration -> infinity. Anything
    // that enters a cave, shaft or through-tunnel gets flung at absurd speed (or
    // goes NaN) as it nears the core. Real gravity does the opposite. By the
    // shell theorem, the shell of mass ABOVE you cancels out and only the mass
    // enclosed beneath you pulls — so for a uniform-density sphere the
    // acceleration falls LINEARLY from surfaceGravity at the surface to exactly
    // zero at the centre. Descend a shaft and gravity fades out; pass the centre
    // and it pulls you back, so you oscillate instead of escaping.
    //
    // The two branches agree exactly at dst == radius (both give G*M/R², which
    // is surfaceGravity, since CelestialBody.RecalculateMass defines
    // mass = surfaceGravity * radius² / G).
    public static Vector3 GravityAcceleration (Vector3 point, CelestialBody body) {
        Vector3 offset = body.Position - point;
        float sqrDst = offset.sqrMagnitude;
        // Dead centre: direction is undefined and the true net pull is zero anyway.
        if (sqrDst < 1e-8f) return Vector3.zero;

        float dst = Mathf.Sqrt (sqrDst);
        Vector3 dir = offset / dst;
        float r = body.radius;

        if (r > 0f && dst < r) {
            // Interior: a = G*M*dst / R³  — linear falloff to zero at the centre.
            return dir * gravitationalConstant * body.mass * dst / (r * r * r);
        }
        return dir * gravitationalConstant * body.mass / sqrDst;
    }
}