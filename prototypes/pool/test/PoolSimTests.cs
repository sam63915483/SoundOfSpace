// Headless checks of the pool-table ball sim (Assets/3 - Scripts/Pool/PoolPhysics2D.cs).
//
//   py -3 prototypes/pool/test/verify-pool.py
//
// Sam cannot debug ball physics by eye; the invariants that would ruin a game
// (balls leaving the table, balls sitting inside each other, a break that never
// settles) are executed here for every change to the sim.

using System;

public static class PoolSimTests
{
    static int _checks, _failures;

    static void Check(bool cond, string what)
    {
        _checks++;
        if (cond) return;
        _failures++;
        Console.WriteLine("  FAIL  " + what);
    }

    static bool NoOverlaps(PoolPhysics2D s, float slack, out string where)
    {
        where = "";
        float diam = s.BallRadius * 2f - slack;
        for (int a = 0; a < PoolPhysics2D.BallCount; a++)
            for (int b = a + 1; b < PoolPhysics2D.BallCount; b++)
            {
                if (!s.Active[a] || !s.Active[b]) continue;
                float dx = s.X[a] - s.X[b], dy = s.Y[a] - s.Y[b];
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < diam) { where = $"balls {a} and {b} are {d * 1000f:0.0} mm apart"; return false; }
            }
        return true;
    }

    static bool AllInside(PoolPhysics2D s, out string where)
    {
        where = "";
        for (int i = 0; i < PoolPhysics2D.BallCount; i++)
        {
            if (!s.Active[i]) continue;
            if (Math.Abs(s.X[i]) > s.HalfLength - s.BallRadius + 1e-3f || Math.Abs(s.Y[i]) > s.HalfWidth - s.BallRadius + 1e-3f)
            { where = $"ball {i} at ({s.X[i]:0.000},{s.Y[i]:0.000})"; return false; }
        }
        return true;
    }

    static float RunUntilStopped(PoolPhysics2D s, float maxSeconds)
    {
        float t = 0f;
        while (t < maxSeconds && !s.AllStopped) { s.Advance(1f / 60f); t += 1f / 60f; }
        return t;
    }

    public static int Main()
    {
        string w;

        // 1. rack
        var s = new PoolPhysics2D();
        Check(s.ActiveCount == 16, "rack: 16 active balls");
        Check(NoOverlaps(s, 0f, out w), "rack: no overlaps (" + w + ")");
        Check(Math.Abs(s.X[8] - (s.FootSpotX + 2f * (s.BallRadius * 2f + 0.0004f) * 0.8660254f)) < 1e-4f && Math.Abs(s.Y[8]) < 1e-5f, "rack: 8-ball centred in row 3");
        Check(Math.Abs(s.X[0] - s.HeadSpotX) < 1e-6f, "rack: cue on the head spot");

        // 2. full-power straight break settles, everything stays on the table, nothing overlaps
        int pocketed = 0, collisions = 0;
        s.BallPocketed += (b, p) => pocketed++;
        s.BallsCollided += (a, b, v) => collisions++;
        s.Strike(1f, 0.02f, 9f);
        float tBreak = RunUntilStopped(s, 60f);
        Check(s.AllStopped, "break: settles within 60 s (took " + tBreak.ToString("0.0") + " s)");
        Check(tBreak > 3f, "break: a 9 m/s break rolls for more than 3 s (" + tBreak.ToString("0.0") + " s)");
        Check(collisions > 20, "break: the rack actually scattered (" + collisions + " collisions)");
        Check(AllInside(s, out w), "break: every ball inside the cushions (" + w + ")");
        Check(NoOverlaps(s, 1e-4f, out w), "break: no ball inside another (" + w + ")");
        Console.WriteLine($"  info  break: {tBreak:0.0} s, {collisions} collisions, {pocketed} pocketed, {s.ActiveCount} left");

        // 3. head-on hit transfers the speed
        s = new PoolPhysics2D();
        for (int i = 2; i < 16; i++) s.Active[i] = false;
        s.X[0] = -0.3f; s.Y[0] = 0f; s.X[1] = 0f; s.Y[1] = 0f;
        s.Strike(1f, 0f, 1f);
        for (int f = 0; f < 30; f++) s.Advance(1f / 60f);   // 0.5 s in frame-sized steps (Advance caps a single call at 0.1 s)
        Check(s.Speed(0) < 0.15f, "head-on: cue ball nearly stops (" + s.Speed(0).ToString("0.00") + " m/s)");
        Check(s.Speed(1) > 0.75f, "head-on: object ball takes the speed (" + s.Speed(1).ToString("0.00") + " m/s)");
        Check(Math.Abs(s.VY[1]) < 0.02f, "head-on: object ball goes straight");

        // 4. straight into the +x+y corner pocket
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.X[0] = 0.4f; s.Y[0] = 0f;
        int got = -1;
        s.BallPocketed += (b, p) => got = p;
        float ddx = (s.HalfLength - s.X[0]), ddy = (s.HalfWidth - s.Y[0]);
        s.Strike(ddx, ddy, 2.5f);
        RunUntilStopped(s, 10f);
        Check(!s.Active[0] && got == 3, "corner shot: cue ball pocketed in pocket 3 (got " + got + ")");

        // 5. a ball rolling along the long rail past the side pocket is NOT swallowed
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.X[0] = -0.5f; s.Y[0] = s.HalfWidth - s.BallRadius; s.Strike(1f, 0f, 2f);
        RunUntilStopped(s, 10f);
        Check(!s.Active[0] || s.X[0] > 0f, "side pocket: rolling along the rail carries past x=0 (x=" + s.X[0].ToString("0.00") + ", active=" + s.Active[0] + ")");
        // ...but one aimed square into it from the middle IS
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.X[0] = 0f; s.Y[0] = 0f; got = -1; s.BallPocketed += (b, p) => got = p;
        s.Strike(0f, 1f, 2f);
        RunUntilStopped(s, 10f);
        Check(got == 5, "side pocket: a square shot drops in pocket 5 (got " + got + ")");

        // 6. cast finds the apex ball on a straight shot
        s = new PoolPhysics2D();
        float cx, cy, ox, oy; int hit;
        Check(s.CastCueBall(1f, 0f, out cx, out cy, out hit, out ox, out oy), "cast: returns true");
        Check(hit == 1, "cast: straight break hits the apex (ball " + hit + ")");
        Check(Math.Abs(cx - (s.X[1] - 2f * s.BallRadius)) < 1e-4f && Math.Abs(cy) < 1e-5f, "cast: contact centre one diameter short of the apex");
        Check(Math.Abs(ox - 1f) < 1e-4f, "cast: apex is sent straight on");
        // cast into an empty table finds the far cushion
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.CastCueBall(1f, 0f, out cx, out cy, out hit, out ox, out oy);
        Check(hit == -1 && Math.Abs(cx - (s.HalfLength - s.BallRadius)) < 1e-5f, "cast: empty table → far cushion");
        Check(ox < 0f, "cast: cushion reflects the x direction");

        // 7. cue respawn avoids an occupied head spot
        s = new PoolPhysics2D();
        s.Active[0] = false;
        s.X[1] = s.HeadSpotX; s.Y[1] = 0f;
        Check(s.RespawnCue(), "respawn: finds a spot");
        Check(s.Active[0] && Math.Abs(s.Y[0]) > s.BallRadius * 1.9f, "respawn: nudged off the occupied head spot (y=" + s.Y[0].ToString("0.000") + ")");

        // 8. determinism: same shot twice → same table
        var a1 = new PoolPhysics2D(); var a2 = new PoolPhysics2D();
        a1.Strike(1f, 0.013f, 8f); a2.Strike(1f, 0.013f, 8f);
        RunUntilStopped(a1, 60f); RunUntilStopped(a2, 60f);
        bool same = true;
        for (int i = 0; i < 16; i++) if (a1.X[i] != a2.X[i] || a1.Y[i] != a2.Y[i] || a1.Active[i] != a2.Active[i]) same = false;
        Check(same, "determinism: identical shots give identical tables");

        Console.WriteLine(_failures == 0 ? $"PASS  {_checks} checks" : $"FAIL  {_failures} of {_checks} checks");
        return _failures == 0 ? 0 : 1;
    }
}
