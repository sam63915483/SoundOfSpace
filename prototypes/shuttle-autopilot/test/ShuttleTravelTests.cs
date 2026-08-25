using System;

// Headless tests for the landing-validity core (handoff §8). The Unity-side
// sensor feeds EvaluateRays real raycasts; these feed it the synthetic cases.
// Ocean/blocker/player checks are Physics-query territory and stay in-editor.
public static class ShuttleTravelTests
{
    static int _failures;

    const int Rays = 9;
    static readonly float CosMax = (float)Math.Cos(12.0 * Math.PI / 180.0);
    const float MaxSpread = 1.5f;

    static float[] Fill(float value)
    {
        var a = new float[Rays];
        for (int i = 0; i < Rays; i++) a[i] = value;
        return a;
    }

    static void Check(string name, bool got, bool want)
    {
        if (got == want) { Console.WriteLine("  ok - " + name); return; }
        _failures++;
        Console.WriteLine("  FAIL - " + name + " (got " + got + ", want " + want + ")");
    }

    public static int Main()
    {
        Console.WriteLine("shuttle landing validity:");

        // Flat ground, all rays hit at the same distance -> green.
        Check("flat ground is valid",
            ShuttleLandingLogic.EvaluateRays(Fill(100f), Fill(1f), CosMax, MaxSpread), true);

        // A uniform 20 degree slope: every ray hits, every normal fails the
        // 12 degree limit -> red.
        float dot20 = (float)Math.Cos(20.0 * Math.PI / 180.0);
        Check("20 degree slope is invalid",
            ShuttleLandingLogic.EvaluateRays(Fill(100f), Fill(dot20), CosMax, MaxSpread), false);

        // 11 degrees passes the limit.
        float dot11 = (float)Math.Cos(11.0 * Math.PI / 180.0);
        Check("11 degree slope is valid",
            ShuttleLandingLogic.EvaluateRays(Fill(100f), Fill(dot11), CosMax, MaxSpread), true);

        // ONE ray failing the slope kills it (a rock at the footprint edge).
        var oneSteep = Fill(1f);
        oneSteep[7] = dot20;
        Check("one steep ray is invalid",
            ShuttleLandingLogic.EvaluateRays(Fill(100f), oneSteep, CosMax, MaxSpread), false);

        // A miss (hole under one leg / nothing within range) is never landable.
        var oneMiss = Fill(100f);
        oneMiss[3] = float.NaN;
        Check("one missing ray is invalid",
            ShuttleLandingLogic.EvaluateRays(oneMiss, Fill(1f), CosMax, MaxSpread), false);

        // The ridge catch: per-ray slopes all flat, but a 3 m step across the
        // footprint (two terraces) -> red even though every ray passes alone.
        var ridge = Fill(100f);
        for (int i = 5; i < Rays; i++) ridge[i] = 103f;
        Check("3 m ridge is invalid",
            ShuttleLandingLogic.EvaluateRays(ridge, Fill(1f), CosMax, MaxSpread), false);

        // Exactly at the spread limit still counts (<=, not <).
        var edge = Fill(100f);
        edge[0] = 101.5f;
        Check("spread exactly 1.5 m is valid",
            ShuttleLandingLogic.EvaluateRays(edge, Fill(1f), CosMax, MaxSpread), true);

        // Degenerate inputs are red, never a crash.
        Check("null arrays are invalid",
            ShuttleLandingLogic.EvaluateRays(null, null, CosMax, MaxSpread), false);
        Check("empty arrays are invalid",
            ShuttleLandingLogic.EvaluateRays(new float[0], new float[0], CosMax, MaxSpread), false);
        Check("mismatched lengths are invalid",
            ShuttleLandingLogic.EvaluateRays(new float[3], new float[2], CosMax, MaxSpread), false);

        Console.WriteLine(_failures == 0 ? "ALL PASS" : _failures + " FAILURE(S)");
        return _failures == 0 ? 0 : 1;
    }
}
