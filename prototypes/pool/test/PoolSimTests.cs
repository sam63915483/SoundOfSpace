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

        // ── game state (PoolGameState) ──────────────────────────────────────
        // stillOnTable helper: everything up except the listed balls
        bool[] Table(params int[] gone)
        {
            var on = new bool[PoolPhysics2D.BallCount];
            for (int i = 0; i < on.Length; i++) on[i] = true;
            foreach (int gb in gone) on[gb] = false;
            return on;
        }

        // 9. fresh state
        var g = new PoolGameState();
        Check(!g.HasOccupant && !g.BreakTaken && !g.GameOver, "game: fresh = nobody, no break, not over");
        Check(g.SunkBy(0).Count == 0 && g.GroupOf(0) == PoolGameState.Group.None, "game: fresh = empty tray, no group");

        // 10. cue ball never listed, never decides
        g.TryClaim(0); g.OnStrike(0);
        Check(g.OnPocketed(0, 0, false, Table(0)) == PoolGameState.Verdict.None, "game: cue ball → None");
        Check(g.SunkBy(0).Count == 0 && g.GroupOf(0) == PoolGameState.Group.None, "game: cue ball not listed, no group");

        // 11. break sink is listed but decides nothing
        g = new PoolGameState(); g.TryClaim(0);
        Check(!g.BreakTaken, "game: break not taken before first strike");
        g.OnStrike(0);
        Check(g.BreakTaken, "game: break taken after first strike");
        g.OnPocketed(0, 3, true, Table(3));
        Check(g.SunkBy(0).Count == 1 && g.SunkBy(0)[0] == 3, "game: break sink listed");
        Check(g.GroupOf(0) == PoolGameState.Group.None, "game: break sink decides nothing");

        // 12. first post-break sink assigns; second player gets the opposite
        g = new PoolGameState(); g.TryClaim(0); g.Release(0); g.TryClaim(7); g.Release(7); g.TryClaim(0); g.OnStrike(0);
        g.OnPocketed(0, 3, false, Table(3));
        Check(g.GroupOf(0) == PoolGameState.Group.Solids, "game: post-break solid → shooter Solids");
        Check(g.GroupOf(7) == PoolGameState.Group.Stripes, "game: other known player → Stripes");

        // 13. stripe first
        g = new PoolGameState(); g.TryClaim(0); g.Release(0); g.TryClaim(7); g.OnStrike(7);
        g.OnPocketed(7, 12, false, Table(12));
        Check(g.GroupOf(7) == PoolGameState.Group.Stripes && g.GroupOf(0) == PoolGameState.Group.Solids, "game: post-break stripe → shooter Stripes, other Solids");

        // 14. 8 with no group → spot it, not listed, not over; next sink assigns
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0);
        Check(g.OnPocketed(0, 8, false, Table(8)) == PoolGameState.Verdict.Spot8, "game: 8 with no group → Spot8");
        Check(g.SunkBy(0).Count == 0 && !g.GameOver, "game: spotted 8 not listed, game goes on");
        g.OnPocketed(0, 5, false, Table(5));
        Check(g.GroupOf(0) == PoolGameState.Group.Solids, "game: sink after a spotted 8 still assigns");

        // 15. group never changes once set
        g.OnPocketed(0, 11, false, Table(5, 11));
        Check(g.GroupOf(0) == PoolGameState.Group.Solids && g.SunkBy(0).Count == 2 && g.SunkBy(0)[1] == 11, "game: wrong-group ball listed, group unchanged");

        // 16. claims
        g = new PoolGameState();
        Check(g.TryClaim(1), "claim: A claims an empty table");
        Check(!g.CanClaim(2) && !g.TryClaim(2), "claim: B refused while A holds it");
        Check(g.TryClaim(1), "claim: A re-claims fine");
        g.Release(2);
        Check(g.HasOccupant && g.Occupant == 1, "claim: B's release does nothing");
        g.Release(1);
        Check(!g.HasOccupant && g.TryClaim(2), "claim: after A releases, B claims");

        // 17. reset clears everything
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0); g.OnPocketed(0, 3, false, Table(3));
        g.Reset();
        Check(!g.HasOccupant && !g.BreakTaken && !g.GameOver && g.SunkBy(0).Count == 0 && g.GroupOf(0) == PoolGameState.Group.None, "game: reset clears all");

        // 18. order preserved
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0);
        g.OnPocketed(0, 3, false, Table(3)); g.OnPocketed(0, 11, false, Table(3, 11)); g.OnPocketed(0, 5, false, Table(3, 11, 5));
        Check(g.SunkBy(0).Count == 3 && g.SunkBy(0)[0] == 3 && g.SunkBy(0)[1] == 11 && g.SunkBy(0)[2] == 5, "game: tray order preserved");

        // 19. 8 as Solids with a solid left → Lose; later pockets ignored
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0); g.OnPocketed(0, 3, false, Table(3));
        Check(g.OnPocketed(0, 8, false, Table(3, 8)) == PoolGameState.Verdict.Lose, "game: 8 with solids left → Lose");
        Check(g.GameOver && g.SunkBy(0).Count == 2 && g.SunkBy(0)[1] == 8, "game: lose = over, 8 listed");
        Check(g.OnPocketed(0, 4, false, Table(3, 8, 4)) == PoolGameState.Verdict.None && g.SunkBy(0).Count == 2, "game: pockets after game over ignored");

        // 20. 8 as Solids with all solids gone → Win
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0);
        for (int b = 1; b <= 7; b++) g.OnPocketed(0, b, false, Table(1, 2, 3, 4, 5, 6, 7));
        Check(g.OnPocketed(0, 8, false, Table(1, 2, 3, 4, 5, 6, 7, 8)) == PoolGameState.Verdict.Win, "game: 8 with solids cleared → Win");

        // 21. it is YOUR group that counts
        g = new PoolGameState(); g.TryClaim(0); g.OnStrike(0); g.OnPocketed(0, 9, false, Table(9));
        Check(g.GroupOf(0) == PoolGameState.Group.Stripes, "game: stripes shooter");
        Check(g.OnPocketed(0, 8, false, Table(9, 1, 2, 3, 4, 5, 6, 7, 8)) == PoolGameState.Verdict.Lose, "game: Stripes sinks 8 with stripes left → Lose even with all solids gone");
        Check(PoolGameState.GroupBallsLeft(PoolGameState.Group.Stripes, Table(9, 1, 2, 3, 4, 5, 6, 7, 8)) == 6, "game: GroupBallsLeft counts 6 stripes");

        // ── respot + ball in hand ───────────────────────────────────────────
        // 22. respot the 8 on a clear foot spot
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        Check(s.Respot(8), "respot: finds the foot spot");
        Check(s.Active[8] && Math.Abs(s.X[8] - s.FootSpotX) < 1e-6f && Math.Abs(s.Y[8]) < 1e-6f, "respot: 8 sits on the foot spot");

        // 23. respot with the foot spot blocked → nudged, no overlap
        s = new PoolPhysics2D();
        for (int i = 1; i < 16; i++) s.Active[i] = false;
        s.Active[1] = true; s.X[1] = s.FootSpotX; s.Y[1] = 0f;
        Check(s.Respot(8), "respot: finds a spot when blocked");
        Check(s.X[8] > s.FootSpotX + s.BallRadius * 1.9f || Math.Abs(s.Y[8]) > s.BallRadius * 1.9f, "respot: moved off the blocked spot");
        Check(NoOverlaps(s, 0f, out w), "respot: no overlap (" + w + ")");

        // 24. ball in hand: lift deactivates, blocked spot refused, clear spot placed
        s = new PoolPhysics2D();
        s.LiftCue();
        Check(!s.Active[0], "hand: cue inactive while lifted");
        Check(!s.CanPlaceCue(s.X[1], s.Y[1]), "hand: refuses a spot on top of ball 1");
        float kx = s.KitchenMaxX - 0.1f, ky = 0.1f;
        Check(s.CanPlaceCue(kx, ky), "hand: accepts a clear kitchen spot");
        Check(s.PlaceCue(kx, ky) && s.Active[0] && Math.Abs(s.X[0] - kx) < 1e-6f && Math.Abs(s.Y[0] - ky) < 1e-6f, "hand: placed and active");
        Check(!s.PlaceCue(s.X[1], s.Y[1]), "hand: placing on a ball is refused");
        Check(Math.Abs(s.KitchenMaxX - s.HeadSpotX) < 1e-6f, "hand: kitchen ends at the head string");

        // 25. SettleNow lands exactly where waiting would
        var f1 = new PoolPhysics2D(); var f2 = new PoolPhysics2D();
        f1.Strike(1f, 0.013f, 8f); f2.Strike(1f, 0.013f, 8f);
        RunUntilStopped(f1, 60f); f2.SettleNow();
        bool sameFF = f2.AllStopped;
        for (int i = 0; i < 16; i++) if (f1.X[i] != f2.X[i] || f1.Y[i] != f2.Y[i] || f1.Active[i] != f2.Active[i]) sameFF = false;
        Check(sameFF, "settle: SettleNow == waiting it out");

        Console.WriteLine(_failures == 0 ? $"PASS  {_checks} checks" : $"FAIL  {_failures} of {_checks} checks");
        return _failures == 0 ? 0 : 1;
    }
}
