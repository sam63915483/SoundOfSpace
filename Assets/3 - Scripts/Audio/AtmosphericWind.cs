using UnityEngine;

// Read-only helper for the wind "woosh" loops on the player + ship. Returns how
// deep a world point is inside the nearest planet/moon's atmosphere as a 0..1
// factor (1 = at/under the surface, 0 = space), plus the nearest body's velocity
// so callers can measure speed relative to the atmosphere rather than world.
//
// Does NOT touch the atmosphere generation / shader systems (those are the
// forbidden zone). It only reads CelestialBody position + radius and models the
// atmosphere as a band whose thickness is a fraction of the body's radius —
// tunable per caller. Approximate by design; it just drives a sound fade.
public static class AtmosphericWind
{
    // worldPos: point to test. heightFraction: atmosphere band thickness as a
    // fraction of the nearest body's radius (e.g. 0.5 => wind fades to silence
    // at altitude = radius * 0.5). bodyVelocity: out, the nearest body's world
    // velocity (zero if none found).
    public static float Factor(Vector3 worldPos, float heightFraction, out Vector3 bodyVelocity)
    {
        bodyVelocity = Vector3.zero;

        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0 || heightFraction <= 0f) return 0f;

        CelestialBody nearest = null;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = Vector3.Distance(worldPos, b.Position);
            if (d < nearestDist) { nearestDist = d; nearest = b; }
        }
        if (nearest == null) return 0f;

        bodyVelocity = nearest.velocity;

        float altitude = nearestDist - nearest.radius;
        if (altitude <= 0f) return 1f;                       // at / under the surface
        float band = nearest.radius * heightFraction;
        return band > 0f ? Mathf.Clamp01(1f - altitude / band) : 0f;
    }
}
