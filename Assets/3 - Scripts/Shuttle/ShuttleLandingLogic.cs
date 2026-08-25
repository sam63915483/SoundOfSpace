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
}
