// Pure landing-validity math — no UnityEngine so the logic runs under the
// headless Roslyn test harness (prototypes/shuttle-autopilot). The sensor
// component feeds it real raycast results; the tests feed it synthetic ones.
public static class ShuttleLandingLogic
{
    /// A ray that missed is encoded as distance = float.NaN.
    /// slopeDots[i] = dot(hit normal, up) for ray i (ignored for misses).
    ///
    /// Valid requires: every ray hit; every hit's slope dot >= cosMaxSlope;
    /// and (max - min) hit distance <= maxSpread — the ridge catch: nine
    /// individually-flat samples can still straddle a step.
    public static bool EvaluateRays(float[] hitDistances, float[] slopeDots, float cosMaxSlope, float maxSpread)
    {
        if (hitDistances == null || slopeDots == null) return false;
        if (hitDistances.Length == 0 || hitDistances.Length != slopeDots.Length) return false;

        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < hitDistances.Length; i++)
        {
            float d = hitDistances[i];
            if (float.IsNaN(d)) return false;          // a miss (hole / too far) is never landable
            if (slopeDots[i] < cosMaxSlope) return false;
            if (d < min) min = d;
            if (d > max) max = d;
        }
        return max - min <= maxSpread;
    }

    /// Rider ground-clamp seat correction (fix for the playtest-1 "stuck in
    /// one spot" bug). The clamp SphereCasts from castUpOffset above the feet
    /// with sphereRadius — so the sphere's bottom starts (castUpOffset −
    /// sphereRadius) above the feet, and after travelling hitDistance the
    /// floor sits at feet + castUpOffset − sphereRadius − hitDistance.
    ///
    /// Returns the along-up delta that seats the feet exactly `skin` above
    /// that floor. The v1 formula (castUpOffset − hitDistance) forgot the
    /// radius term and seated the rider at floor + radius = 0.25 m — past
    /// IsGrounded's 0.2 m reach, so grounded read false for the whole flight
    /// and the grounded-gated walk input stayed zeroed.
    public static float RiderSeatCorrection(float castUpOffset, float sphereRadius, float hitDistance, float skin)
    {
        // Feet sit (hitDistance − (castUpOffset − sphereRadius)) above the
        // floor right now; move so that height becomes exactly `skin`.
        return (castUpOffset - sphereRadius) - hitDistance + skin;
    }
}
