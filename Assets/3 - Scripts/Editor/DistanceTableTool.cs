using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes <c>docs/DISTANCE_TABLE.md</c> — how far apart every pair of planets gets,
/// and how much of the time they are inside a given jump range.
/// (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phase 0 #1.)
///
/// This is EXACT, not sampled. Every planet rides a circular rail in the XY plane
/// (the system is clockwork — see the audit), so the distance between two of them
/// depends only on the angle between them:
///
///     d(Δθ)² = r₁² + r₂² − 2·r₁·r₂·cos Δθ
///
/// and Δθ advances linearly at |ω₁ − ω₂|. So the closest and furthest they ever
/// get are just |r₁ − r₂| and r₁ + r₂, the time they spend inside a range X is a
/// single arccos, and "how long until they are in range" needs no simulation at
/// all. Stepping the rails forward — as the handoff originally proposed — would
/// also have used the wrong clock: what matters is how long two planets take to
/// lap EACH OTHER (the synodic period), not their own day length. Humble Abode
/// and Cyclops take 8.6 minutes; Puddle and Hearth take nearly four hours.
///
/// Two details the maths has to respect:
///  • <b>Humble Abode orbits retrograde</b>, so its angle to a prograde planet
///    closes at ω₁ + ω₂ rather than ω₁ − ω₂ — it meets everything far more often
///    than its 900 s period suggests.
///  • <b>The twins are co-orbital</b> (same radius, fixed angular offset, one
///    listed as the other's <c>coOrbitLeader</c>), so their separation never
///    changes. That makes Icey↔Fiery a permanent short hop.
/// </summary>
public static class DistanceTableTool
{
    const string OutPath = "docs/DISTANCE_TABLE.md";

    /// <summary>Jump ranges to report on. The middle one is the design target.</summary>
    static readonly float[] Ranges = { 5000f, 8000f, 15000f };

    class Rail
    {
        public string name;
        public float  radius;       // metres from the sun
        public double omega;        // rad/s, SIGNED (sign = orbit direction)
        public float  bodyRadius;
        public Vector2 pos;         // current XY position relative to the sun
        public bool   water;
        public bool   market;
    }

    [MenuItem("Tools/Solar System/Write Distance Table")]
    public static void Write()
    {
        var rails = CollectRails();
        if (rails.Count < 2)
        {
            Debug.LogError("[DistanceTable] Need at least two railed planets in the open scene. " +
                           "Open Assets/1.6.7.7.7.unity first.");
            return;
        }
        rails.Sort((a, b) => a.radius.CompareTo(b.radius));

        var sb = new StringBuilder();
        Header(sb, rails);
        PairTable(sb, rails);
        Reachability(sb, rails);
        GapCheck(sb, rails);

        var full = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, OutPath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
        System.IO.File.WriteAllText(full, sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log($"[DistanceTable] wrote {OutPath} ({rails.Count} planets, " +
                  $"{rails.Count * (rails.Count - 1) / 2} pairs).");
    }

    /// <summary>Every landable planet, with its rail resolved from live scene state.</summary>
    static List<Rail> CollectRails()
    {
        var all = UnityEngine.Object.FindObjectsOfType<CelestialBody>(true);
        CelestialBody sun = null;
        foreach (var b in all) if (b.bodyName == "Sun") sun = b;
        if (sun == null) return new List<Rail>();

        var sites = UnityEngine.Object.FindObjectsOfType<VendorSite>(true);
        var outp  = new List<Rail>();

        foreach (var b in all)
        {
            if (b == sun) continue;
            if (b.bodyType != CelestialBody.BodyType.Planet) continue;

            // Period: its own rail, or its co-orbit leader's (the twins).
            float period = b.railPeriod;
            var lead = b.coOrbitLeader;
            int guard = 0;
            while (period <= 0f && lead != null && guard++ < 8) { period = lead.railPeriod; lead = lead.coOrbitLeader; }
            if (period <= 0f) continue;                    // not on a rail — moons, etc.

            Vector3 p = b.transform.position - sun.transform.position;
            Vector3 v = b.velocity;
            // Sign of the orbit's angular momentum about Z tells us which way it goes.
            float lz   = p.x * v.y - p.y * v.x;
            double sgn = lz >= 0f ? 1.0 : -1.0;

            bool market = false;
            foreach (var s in sites)
                if (s.kind == VendorSite.VendorKind.FishMarket &&
                    s.GetComponentInParent<CelestialBody>() == b) market = true;

            outp.Add(new Rail
            {
                name       = b.bodyName,
                radius     = new Vector2(p.x, p.y).magnitude,
                pos        = new Vector2(p.x, p.y),
                omega      = sgn * 2.0 * Math.PI / period,
                bodyRadius = b.radius,
                water      = b.transform.Find("waterline") != null,
                market     = market,
            });
        }
        return outp;
    }

    // ── The maths ─────────────────────────────────────────────────────────────

    /// <summary>Two planets that share an orbit (the twins) hold a fixed angular
    /// offset forever, so their separation is a constant — NOT the |r1-r2| ... r1+r2
    /// sweep every other pair gets. Reporting the sweep for them would claim the
    /// twins can be 12 km apart when in truth they never leave each other's side.</summary>
    static bool CoOrbital(Rail a, Rail b) => Math.Abs(a.omega - b.omega) < 1e-9;

    static float FixedSeparation(Rail a, Rail b) => (a.pos - b.pos).magnitude;

    static float MinDist(Rail a, Rail b) =>
        CoOrbital(a, b) ? FixedSeparation(a, b) : Mathf.Abs(a.radius - b.radius);

    static float MaxDist(Rail a, Rail b) =>
        CoOrbital(a, b) ? FixedSeparation(a, b) : a.radius + b.radius;

    /// <summary>Seconds for the two to return to the same relative angle. Infinity
    /// when they share an orbit and never change their separation.</summary>
    static double Synodic(Rail a, Rail b)
    {
        double rel = Math.Abs(a.omega - b.omega);
        return rel < 1e-9 ? double.PositiveInfinity : 2.0 * Math.PI / rel;
    }

    /// <summary>Half-angle over which the pair sits within <paramref name="range"/>,
    /// in radians. 0 = never in range, π = always.</summary>
    static double InRangeHalfAngle(Rail a, Rail b, float range)
    {
        if (CoOrbital(a, b)) return FixedSeparation(a, b) <= range ? Math.PI : 0.0;
        if (range >= MaxDist(a, b)) return Math.PI;
        if (range <= MinDist(a, b)) return 0.0;
        double cos = ((double)a.radius * a.radius + (double)b.radius * b.radius - (double)range * range)
                   / (2.0 * a.radius * b.radius);
        return Math.Acos(Mathf.Clamp((float)cos, -1f, 1f));
    }

    /// <summary>Fraction of every cycle the pair spends inside <paramref name="range"/>.</summary>
    static double InRangeFraction(Rail a, Rail b, float range) => InRangeHalfAngle(a, b, range) / Math.PI;

    static string Mins(double seconds)
    {
        if (double.IsInfinity(seconds)) return "always";
        if (seconds < 90.0) return $"{seconds:F0} s";
        return $"{seconds / 60.0:F1} min";
    }

    // ── Report ────────────────────────────────────────────────────────────────

    static void Header(StringBuilder sb, List<Rail> rails)
    {
        sb.AppendLine("# Distance table — how far apart the planets get");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by `Tools ▸ Solar System ▸ Write Distance Table`");
        sb.AppendLine("(`Assets/3 - Scripts/Editor/DistanceTableTool.cs`). Re-run it after changing any orbit.");
        sb.AppendLine();
        sb.AppendLine("Exact, not sampled: every planet rides a circular rail in one plane, so distance");
        sb.AppendLine("is a closed-form function of the angle between two of them. Distances are metres");
        sb.AppendLine("(the game's \"km\" readout is these numbers ÷ 1000).");
        sb.AppendLine();
        sb.AppendLine("## The planets");
        sb.AppendLine();
        sb.AppendLine("| Planet | Orbit radius | Day (period) | Direction | Water | Fish market |");
        sb.AppendLine("|---|---:|---:|---|---|---|");
        foreach (var r in rails)
        {
            double t = 2.0 * Math.PI / Math.Abs(r.omega);
            sb.AppendLine($"| {r.name} | {r.radius:N0} | {t / 60.0:F1} min | " +
                          $"{(r.omega >= 0 ? "**retrograde**" : "prograde")} | " +
                          $"{(r.water ? "yes" : "—")} | {(r.market ? "yes" : "—")} |");
        }
        sb.AppendLine();
    }

    static void PairTable(StringBuilder sb, List<Rail> rails)
    {
        sb.AppendLine("## Every pair");
        sb.AppendLine();
        sb.AppendLine("`Closest`/`Furthest` are the true extremes. `Cycle` is how long the pair takes to");
        sb.AppendLine("lap each other — the clock that matters for waiting, not either planet's day.");
        sb.AppendLine("`In range` is the share of each cycle spent within the jump range, and `Worst wait`");
        sb.AppendLine("is the longest you could ever be stuck waiting for the window to come round.");
        sb.AppendLine();

        foreach (float range in Ranges)
        {
            sb.AppendLine($"### At a {range / 1000f:F0} km jump range");
            sb.AppendLine();
            sb.AppendLine("| A | B | Closest | Furthest | Cycle | In range | Worst wait |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            for (int i = 0; i < rails.Count; i++)
            for (int j = i + 1; j < rails.Count; j++)
            {
                var a = rails[i]; var b = rails[j];
                double syn  = Synodic(a, b);
                double frac = InRangeFraction(a, b, range);

                string inRange, wait;
                if (double.IsInfinity(syn))
                {
                    // Co-orbital: separation never changes, so it is in or out forever.
                    bool ok = FixedSeparation(a, b) <= range;
                    inRange = ok ? "**always**" : "**never**";
                    wait    = ok ? "—" : "forever";
                }
                else if (frac <= 0.0)      { inRange = "**never**"; wait = "forever"; }
                else if (frac >= 1.0)      { inRange = "**always**"; wait = "—"; }
                else                       { inRange = $"{frac * 100.0:F0}%  ({Mins(frac * syn)})";
                                             wait    = Mins((1.0 - frac) * syn); }

                sb.AppendLine($"| {a.name} | {b.name} | {MinDist(a, b):N0} | {MaxDist(a, b):N0} | " +
                              $"{Mins(syn)} | {inRange} | {wait} |");
            }
            sb.AppendLine();
        }
    }

    static void Reachability(StringBuilder sb, List<Rail> rails)
    {
        sb.AppendLine("## Where you can get to, from each planet");
        sb.AppendLine();
        sb.AppendLine("At the design range of 15 km. **always** = the hop is open whenever you want it;");
        sb.AppendLine("a percentage = you have to wait for the window.");
        sb.AppendLine();
        foreach (var a in rails)
        {
            var always = new List<string>();
            var some   = new List<string>();
            var never  = new List<string>();
            foreach (var b in rails)
            {
                if (b == a) continue;
                double frac = InRangeFraction(a, b, 15000f);
                if (frac >= 1.0)    always.Add(b.name);
                else if (frac <= 0) never.Add(b.name);
                else                some.Add($"{b.name} ({frac * 100.0:F0}%)");
            }
            sb.AppendLine($"**{a.name}**");
            sb.AppendLine($"- always: {(always.Count > 0 ? string.Join(", ", always) : "—")}");
            sb.AppendLine($"- sometimes: {(some.Count > 0 ? string.Join(", ", some) : "—")}");
            sb.AppendLine($"- never: {(never.Count > 0 ? string.Join(", ", never) : "—")}");
            sb.AppendLine();
        }
    }

    static void GapCheck(StringBuilder sb, List<Rail> rails)
    {
        sb.AppendLine("## Gap check");
        sb.AppendLine();
        sb.AppendLine("Every planet needs at least one neighbour it can actually reach, or landing there");
        sb.AppendLine("is a one-way trip. Worst neighbour = the closest planet it can ever get to.");
        sb.AppendLine();
        sb.AppendLine("| Planet | Nearest reachable | Its closest approach | Reachable at 15 km? |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (var a in rails)
        {
            string best = "—"; float bestMin = float.MaxValue;
            foreach (var b in rails)
            {
                if (b == a) continue;
                float m = MinDist(a, b);
                if (m < bestMin) { bestMin = m; best = b.name; }
            }
            int reach = 0;
            foreach (var b in rails)
            {
                if (b == a) continue;
                double frac = InRangeFraction(a, b, 15000f);
                if (frac > 0.0) reach++;
            }
            sb.AppendLine($"| {a.name} | {best} | {bestMin:N0} | {(reach > 0 ? $"yes — {reach} planet(s)" : "**NO — DEAD END**")} |");
        }
        sb.AppendLine();
    }
}
