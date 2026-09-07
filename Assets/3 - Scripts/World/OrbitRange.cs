using System;
using UnityEngine;

/// <summary>
/// "Is that planet in range, and if not, how long until it is?"
///
/// The solar system is clockwork — every planet rides an exact circular rail in
/// one plane — so this needs no simulation and no stepping. The distance between
/// two planets depends only on the angle between them:
///
///     d(Δθ)² = r₁² + r₂² − 2·r₁·r₂·cos Δθ
///
/// and that angle changes at a constant rate. So "closest they ever get" is
/// |r₁ − r₂|, "furthest" is r₁ + r₂, and the wait for a window is one
/// subtraction and one divide. That exactness is what lets the NAV app promise
/// "IN RANGE ~4 MIN" and be right, which is the whole reason riding orbits is a
/// strategy rather than a guess.
///
/// Two things the maths has to respect, both real in this scene:
///  • <b>Humble Abode orbits retrograde.</b> Its angle to a prograde planet
///    closes at ω₁ + ω₂, not ω₁ − ω₂, so it meets everything far more often
///    than its 15-minute day suggests. Directions come from each body's actual
///    angular momentum, never assumed.
///  • <b>The twins are co-orbital</b> — identical rails, fixed offset. Their
///    separation never changes, so they are in range forever or never, and the
///    "wait" question has no answer. Handled explicitly.
///
/// The offline report in <c>Editor/DistanceTableTool.cs</c> and
/// <c>docs/DISTANCE_TABLE.md</c> is the same maths over every pair at once.
/// </summary>
public static class OrbitRange
{
    /// <summary>Result of asking about a pair of planets.</summary>
    public enum Status
    {
        /// <summary>Close enough right now.</summary>
        InRange,
        /// <summary>Out of range, but their orbits will bring them together — see the wait.</summary>
        Waiting,
        /// <summary>These two never come within the range, at any point in their orbits.</summary>
        Never,
        /// <summary>Not enough information (a body missing, no Sun, a rail not resolved).</summary>
        Unknown,
    }

    const double Eps = 1e-9;

    static CelestialBody _sun;

    static CelestialBody Sun()
    {
        if (_sun != null) return _sun;
        var bodies = NBodySimulation.Bodies;
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i] != null && bodies[i].bodyName == "Sun") { _sun = bodies[i]; break; }
        return _sun;
    }

    /// <summary>Orbit radius (m) and signed angular rate (rad/s) of a body about the
    /// sun, plus its current angle. Read from live state, so a retrograde orbit
    /// reports a rate of the opposite sign without anyone having to know.</summary>
    static bool Rail(CelestialBody b, out double radius, out double omega, out double angle)
    {
        radius = omega = angle = 0.0;
        var sun = Sun();
        if (b == null || sun == null) return false;

        Vector3 p = b.Position - sun.Position;
        double x = p.x, y = p.y;
        radius = Math.Sqrt(x * x + y * y);
        if (radius < 1.0) return false;
        angle = Math.Atan2(y, x);

        // Period: its own rail, or the leader's if it is a co-orbital follower.
        float period = b.railPeriod;
        var lead = b.coOrbitLeader;
        int guard = 0;
        while (period <= 0f && lead != null && guard++ < 8) { period = lead.railPeriod; lead = lead.coOrbitLeader; }
        if (period <= 0.01f) return false;

        // Direction from the sign of angular momentum about Z — never assumed.
        Vector3 v = b.velocity;
        double lz = (double)p.x * v.y - (double)p.y * v.x;
        omega = (lz >= 0.0 ? 1.0 : -1.0) * 2.0 * Math.PI / period;
        return true;
    }

    static double WrapPi(double a)
    {
        while (a >  Math.PI) a -= 2.0 * Math.PI;
        while (a < -Math.PI) a += 2.0 * Math.PI;
        return a;
    }

    /// <summary>Closest the pair ever get, in metres. Constant for co-orbitals.</summary>
    public static float ClosestApproach(CelestialBody a, CelestialBody b)
    {
        if (!Rail(a, out double r1, out double w1, out _) ||
            !Rail(b, out double r2, out double w2, out _)) return 0f;
        if (Math.Abs(w1 - w2) < Eps) return Vector3.Distance(a.Position, b.Position);
        return (float)Math.Abs(r1 - r2);
    }

    /// <summary>
    /// How this pair stands relative to <paramref name="rangeMetres"/>, and how many
    /// seconds until the window opens (0 when already in range).
    /// </summary>
    public static Status Evaluate(CelestialBody a, CelestialBody b, float rangeMetres, out float secondsUntil)
    {
        secondsUntil = 0f;
        if (a == null || b == null) return Status.Unknown;
        if (a == b) return Status.InRange;                 // relocation: always allowed

        if (!Rail(a, out double r1, out double w1, out double t1) ||
            !Rail(b, out double r2, out double w2, out double t2)) return Status.Unknown;

        // Co-orbital: their separation is frozen, so it is settled forever.
        if (Math.Abs(w1 - w2) < Eps)
            return Vector3.Distance(a.Position, b.Position) <= rangeMetres ? Status.InRange : Status.Never;

        double lo = Math.Abs(r1 - r2), hi = r1 + r2;
        if (rangeMetres >= hi) return Status.InRange;       // never far enough apart to matter
        if (rangeMetres <= lo) return Status.Never;         // never close enough, ever

        double cosMax = (r1 * r1 + r2 * r2 - (double)rangeMetres * rangeMetres) / (2.0 * r1 * r2);
        double dMax   = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosMax)));   // half-width of the window

        double d0 = WrapPi(t1 - t2);
        double w  = w1 - w2;
        if (w < 0.0) { w = -w; d0 = -d0; }                  // normalise to a forward-turning gap

        if (Math.Abs(d0) <= dMax) return Status.InRange;

        // Turn forward until the gap re-enters the window.
        double target = d0 > dMax ? (2.0 * Math.PI - dMax) : -dMax;
        double t = (target - d0) / w;
        if (t < 0.0) t += 2.0 * Math.PI / w;                // numerical safety
        secondsUntil = (float)t;
        return Status.Waiting;
    }

    /// <summary>Short human phrasing of a wait: "~4 MIN", "&lt;1 MIN".</summary>
    public static string DescribeWait(float seconds)
    {
        if (seconds <= 60f)   return "<1 MIN";
        if (seconds < 5400f)  return "~" + Mathf.RoundToInt(seconds / 60f) + " MIN";
        return "~" + (seconds / 3600f).ToString("0.0") + " HR";
    }
}
