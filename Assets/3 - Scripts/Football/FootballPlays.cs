using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A route: waypoints in ATTACK-relative yards from the receiver's alignment —
/// x = yards toward that receiver's OWN sideline (+) or the middle (−),
/// y = depth downfield from the line. `settle` routes end with the receiver
/// stopped and turned to the QB (curl, hitch, comeback) — a legitimate target
/// standing still, which the QB's read and the DB's reaction both know about.
/// </summary>
public class FootballRoute
{
    public string name;
    public Vector2[] points;
    public bool settle;

    public FootballRoute(string n, bool settle, params float[] xy)
    {
        name = n; this.settle = settle;
        points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
    }
}

/// <summary>
/// The route tree. Every route Sam asked for (2026-09-19: "comeback routes,
/// out routes, slants, posts, zig zags") plus the ones a real tree needs to
/// make those work (the clear-outs and the underneath stuff).
/// </summary>
public static class FootballRoutes
{
    public static readonly FootballRoute Go        = new FootballRoute("go", false, 0, 12, -1, 30, -1, 48);
    public static readonly FootballRoute Seam      = new FootballRoute("seam", false, 0, 8, -3, 22, -4, 45);
    public static readonly FootballRoute Fade      = new FootballRoute("fade", false, 0, 5, 3, 15, 4, 30, 4, 45);
    public static readonly FootballRoute Slant     = new FootballRoute("slant", false, 0, 2, -6, 8, -12, 14, -18, 20);
    public static readonly FootballRoute QuickOut  = new FootballRoute("quick out", false, 0, 5, 1, 6, 7, 6.5f, 12, 7);
    public static readonly FootballRoute DeepOut   = new FootballRoute("deep out", false, 0, 12, 0, 14, 8, 14.5f, 14, 15);
    public static readonly FootballRoute Dig       = new FootballRoute("dig", false, 0, 10, -1, 12, -12, 12, -24, 12.5f);
    public static readonly FootballRoute ShallowIn = new FootballRoute("shallow in", false, 0, 4, -1, 5.5f, -10, 6, -20, 6.5f);
    public static readonly FootballRoute Curl      = new FootballRoute("curl", true, 0, 10, -0.5f, 12, -2.5f, 10);
    public static readonly FootballRoute Hitch     = new FootballRoute("hitch", true, 0, 5, -0.3f, 6.5f, -1.5f, 5);
    public static readonly FootballRoute Comeback  = new FootballRoute("comeback", true, 0, 13, 0.5f, 16, 4, 13, 5.5f, 12);
    public static readonly FootballRoute Post      = new FootballRoute("post", false, 0, 12, -6, 20, -14, 34, -18, 46);
    public static readonly FootballRoute Corner    = new FootballRoute("corner", false, 0, 12, 5, 20, 10, 32, 12, 44);
    public static readonly FootballRoute Sluggo    = new FootballRoute("sluggo", false, 0, 2, -5, 7, -6, 12, -6, 30, -6, 46);
    public static readonly FootballRoute OutAndUp  = new FootballRoute("out and up", false, 0, 6, 5, 7, 6, 12, 6, 30, 6, 46);
    public static readonly FootballRoute ZigZag    = new FootballRoute("zig zag", false, 0, 4, -4, 8, 3, 13, -3, 18, -3, 32);
    public static readonly FootballRoute Wheel     = new FootballRoute("wheel", false, 4, 2, 7, 6, 7, 14, 6, 30, 6, 45);
    public static readonly FootballRoute Drag      = new FootballRoute("drag", false, 0, 2, -8, 4, -18, 5, -30, 6);
    public static readonly FootballRoute DeepCross = new FootballRoute("deep cross", false, 0, 8, -8, 14, -20, 20, -32, 24);
    public static readonly FootballRoute Flat      = new FootballRoute("flat", false, 4, 1, 9, 2, 14, 3);
    public static readonly FootballRoute Block     = new FootballRoute("block", false, 1, 3);
    public static readonly FootballRoute ClearOut  = new FootballRoute("clear out", false, 0, 12, 2, 20, 3, 40);
    /// The slot is already in motion past the QB at the snap; takes it on the
    /// run and keeps going to the far edge.
    public static readonly FootballRoute SweepPath = new FootballRoute("sweep", false, -7, -4, -14, -1, -18, 6, -18, 30);
    /// The flea flicker's sweep man: a few strides toward the edge, then the pitch back.
    public static readonly FootballRoute FlickerPath = new FootballRoute("flicker", false, -6, -4, -11, -3, -14, -2);
    /// The screen: slip out to the flat behind the line and turn round.
    // Not a settle route: a settled man is the cue for his corner to drive on him.
    public static readonly FootballRoute ScreenOut = new FootballRoute("screen", false, 4, -3, 8, -3.5f, 12, -3f);
}

/// Where the three receivers line up (attack-relative x in yards; + = right).
public class FootballFormation
{
    public string name;
    public float[] wrX;
    public FootballFormation(string n, float a, float b, float c) { name = n; wrX = new[] { a, b, c }; }

    public static readonly FootballFormation Spread     = new FootballFormation("spread", -16f, 16f, 8f);
    public static readonly FootballFormation SpreadLeft = new FootballFormation("spread left", -16f, 16f, -8f);
    public static readonly FootballFormation TripsRight = new FootballFormation("trips right", 6f, 17f, 11.5f);
    public static readonly FootballFormation TripsLeft  = new FootballFormation("trips left", -17f, -6f, -11.5f);
    public static readonly FootballFormation Tight      = new FootballFormation("tight", -10f, 10f, 4.5f);
    public static readonly FootballFormation[] All = { Spread, SpreadLeft, TripsRight, TripsLeft, Tight };

    /// Runs need the slot beside the QB; a sweep motion from a bunch looks wrong.
    public static FootballFormation Pick(FootballPlay play, System.Random rng)
    {
        if (play.kind != FootballPlay.Kind.Pass && play.kind != FootballPlay.Kind.Rollout && play.kind != FootballPlay.Kind.FleaFlicker)
            return play.kind == FootballPlay.Kind.QbRun || play.kind == FootballPlay.Kind.Screen ? Tight : rng.Next(2) == 0 ? Spread : SpreadLeft;
        double r = rng.NextDouble();
        if (r < 0.30) return Spread;
        if (r < 0.50) return SpreadLeft;
        if (r < 0.68) return TripsRight;
        if (r < 0.86) return TripsLeft;
        return Tight;
    }
}

/// <summary>
/// The play table: a concept is one route per receiver plus the QB's read
/// order, weighted by down and distance. Rollout plays send the QB to the
/// slot's side by design; pass plays let him roll a style. No play editor;
/// add concepts here.
/// </summary>
public class FootballPlay
{
    public enum Kind { Pass, Rollout, JetSweep, QbDraw, QbRun, FleaFlicker, Screen }

    public string name;
    public Kind kind;
    /// Routes for WR1, WR2, WR3 (index 0..2).
    public FootballRoute[] routes;
    /// Receiver indices in the order the QB reads them.
    public int[] priority;
    /// Selection weights: short (≤3 to go), medium (4–7), long (8+).
    public float wShort, wMedium, wLong;
    /// Red zone (inside the 12) multiplier.
    public float wRedZone = 1f;
    /// JetSweep / FleaFlicker: which receiver takes the handoff (2 = the slot; 0 = the far wideout on an end around).
    public int sweep = 2;

    static FootballPlay[] _all;
    public static IReadOnlyList<FootballPlay> All => _all ?? (_all = Build());

    public enum Mood { Normal, Desperate, KillClock }

    public static FootballPlay Pick(int down, float toGo, float yardsToGoal, System.Random rng, FootballPlay last = null, Mood mood = Mood.Normal)
    {
        var list = All;
        float total = 0f;
        var w = new float[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            float x = toGo <= 3f ? p.wShort : toGo <= 7f ? p.wMedium : p.wLong;
            if (yardsToGoal <= 12f) x *= p.wRedZone;
            // No deep shots from the shadow of the goal line; nothing slow on
            // 4th and long; never the same call twice running.
            if (p.IsDeepShot && yardsToGoal < 25f) x *= 0.2f;
            if (down == 4 && toGo > 7f && p.kind != Kind.Pass && p.kind != Kind.Rollout && p.kind != Kind.Screen) x *= 0.3f;
            if (p.kind == Kind.FleaFlicker && yardsToGoal < 35f) x *= 0.4f;         // needs room for the deep shot
            if (p == last) x *= 0.15f;
            // Trailing late: shots and passes, no slow stuff. Leading late: the ground and the clock.
            if (mood == Mood.Desperate) { if (p.IsDeepShot) x *= 2.5f; if (p.kind == Kind.JetSweep || p.kind == Kind.QbDraw || p.kind == Kind.QbRun) x *= 0.2f; }
            else if (mood == Mood.KillClock) { if (p.kind == Kind.JetSweep || p.kind == Kind.QbDraw || p.kind == Kind.QbRun) x *= 3.5f; if (p.IsDeepShot) x *= 0.3f; }
            w[i] = x; total += x;
        }
        float r = (float)rng.NextDouble() * total;
        for (int i = 0; i < list.Count; i++) { r -= w[i]; if (r <= 0f) return list[i]; }
        return list[0];
    }

    /// A play whose first read goes 25+ yards deep.
    public bool IsDeepShot
    {
        get
        {
            if (priority == null || priority.Length == 0) return false;
            var r = routes[priority[0]];
            return r.points[r.points.Length - 1].y >= 25f;
        }
    }

    public static FootballPlay ByName(string n)
    {
        foreach (var p in All) if (p.name == n) return p;
        return null;
    }

    static FootballPlay Sweep(string name, int who, float s, float m, float l)
    {
        var routes = new[] { FootballRoutes.Block, FootballRoutes.Block, FootballRoutes.Block };
        routes[who] = FootballRoutes.SweepPath;
        return new FootballPlay { name = name, kind = Kind.JetSweep, routes = routes, priority = new[] { who }, wShort = s, wMedium = m, wLong = l, sweep = who };
    }

    static FootballPlay P(string name, Kind kind, FootballRoute a, FootballRoute b, FootballRoute c, int[] prio, float s, float m, float l, float red = 1f)
        => new FootballPlay { name = name, kind = kind, routes = new[] { a, b, c }, priority = prio, wShort = s, wMedium = m, wLong = l, wRedZone = red };

    static FootballPlay[] Build()
    {
        return new[]
        {
            // ── quick game ──
            P("Slants",      Kind.Pass, FootballRoutes.Slant,    FootballRoutes.Slant,     FootballRoutes.Flat,     new[] { 0, 1, 2 }, 3f, 3f, 1.2f, 1.6f),
            P("Quick Outs",  Kind.Pass, FootballRoutes.QuickOut, FootballRoutes.QuickOut,  FootballRoutes.Slant,    new[] { 0, 1, 2 }, 3f, 2f, 0.8f, 1.4f),
            P("Curl Flat",   Kind.Pass, FootballRoutes.Curl,     FootballRoutes.Curl,      FootballRoutes.Flat,     new[] { 0, 1, 2 }, 2f, 3f, 1.5f),
            P("Mesh",        Kind.Pass, FootballRoutes.Drag,     FootballRoutes.Drag,      FootballRoutes.Curl,     new[] { 0, 1, 2 }, 2.5f, 2.5f, 1f),
            P("Levels",      Kind.Pass, FootballRoutes.Dig,      FootballRoutes.ShallowIn, FootballRoutes.Drag,     new[] { 1, 0, 2 }, 2f, 2.5f, 1.2f),
            P("Fades",       Kind.Pass, FootballRoutes.Fade,     FootballRoutes.Fade,      FootballRoutes.QuickOut, new[] { 1, 0, 2 }, 0.4f, 0.4f, 0.4f, 6f),
            // ── intermediate ──
            P("Smash",       Kind.Pass, FootballRoutes.Hitch,    FootballRoutes.Hitch,     FootballRoutes.Corner,   new[] { 2, 0, 1 }, 1.5f, 2.5f, 2f),
            P("Flood",       Kind.Pass, FootballRoutes.Drag,     FootballRoutes.Go,        FootballRoutes.DeepOut,  new[] { 2, 0, 1 }, 1.2f, 2.5f, 2f),
            P("Comebacks",   Kind.Pass, FootballRoutes.Comeback, FootballRoutes.Comeback,  FootballRoutes.Dig,      new[] { 1, 0, 2 }, 0.8f, 2f, 2.5f),
            P("Dagger",      Kind.Pass, FootballRoutes.Dig,      FootballRoutes.Go,        FootballRoutes.Seam,     new[] { 0, 2, 1 }, 1f, 2f, 2.5f),
            P("Deep Cross",  Kind.Pass, FootballRoutes.DeepCross,FootballRoutes.Comeback,  FootballRoutes.Wheel,    new[] { 0, 2, 1 }, 0.8f, 1.8f, 2.2f),
            // ── shots ──
            P("Verticals",   Kind.Pass, FootballRoutes.Go,       FootballRoutes.Go,        FootballRoutes.Seam,     new[] { 2, 0, 1 }, 0.5f, 1f, 2.5f),
            P("Post Wheel",  Kind.Pass, FootballRoutes.Slant,    FootballRoutes.Post,      FootballRoutes.Wheel,    new[] { 1, 2, 0 }, 0.8f, 1.5f, 2.5f),
            P("Corner Post", Kind.Pass, FootballRoutes.Corner,   FootballRoutes.Post,      FootballRoutes.Flat,     new[] { 0, 1, 2 }, 0.7f, 1.5f, 2.5f),
            P("Sluggo",      Kind.Pass, FootballRoutes.Sluggo,   FootballRoutes.Hitch,     FootballRoutes.QuickOut, new[] { 0, 1, 2 }, 0.6f, 1f, 2.5f),
            P("Double Moves",Kind.Pass, FootballRoutes.OutAndUp, FootballRoutes.ZigZag,    FootballRoutes.Drag,     new[] { 0, 1, 2 }, 0.6f, 1.2f, 2.5f),
            // ── the QB on the move (designed: he rolls to the slot's side) ──
            P("Roll Flood",  Kind.Rollout, FootballRoutes.DeepCross, FootballRoutes.Go,     FootballRoutes.DeepOut,  new[] { 2, 1, 0 }, 1.5f, 2.5f, 2f),
            P("Boot",        Kind.Rollout, FootballRoutes.Post,     FootballRoutes.Comeback, FootballRoutes.Flat,    new[] { 2, 0, 1 }, 2f, 2f, 1.5f),
            // ── runs ──
            Sweep("Jet Sweep", 2, 4.5f, 3f, 1f),
            P("QB Draw",     Kind.QbDraw,   FootballRoutes.ClearOut, FootballRoutes.ClearOut, FootballRoutes.Seam,     new int[0], 3.5f, 2f, 0.6f),
            P("QB Power",    Kind.QbRun,    FootballRoutes.Block,    FootballRoutes.Block,    FootballRoutes.Block,    new int[0], 4f, 2f, 0.5f, 1.5f),
            // ── trickery (Sam: end arounds, flea flickers, screens) ──
            Sweep("End Around", 0, 2f, 1.5f, 0.6f),
            P("Flea Flicker", Kind.FleaFlicker, FootballRoutes.Go,      FootballRoutes.Post,     FootballRoutes.FlickerPath, new[] { 0, 1 }, 1.2f, 2f, 2.6f),
            P("Screen",      Kind.Screen,   FootballRoutes.ClearOut, FootballRoutes.ClearOut, FootballRoutes.ScreenOut, new[] { 2 }, 1.5f, 2f, 2f, 0.6f),
        };
    }
}
