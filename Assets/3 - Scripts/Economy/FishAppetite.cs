using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What stops "one route forever": each fish market gets FULL of a species the
/// more of it you sell there. (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md §7.2.)
///
/// Per market, per species, a fullness counter. Every Import or Delicacy sale
/// adds one; one comes off per in-game (GalaxyTime) day. The price it pays is
///     bucket rate × (1 − step × fullness), floored at ×1
/// so the first few delicacy fish on a new route are the jackpot, the route
/// cools, and the player rotates. Local fish never decay — fishing at home is
/// never a dead end.
///
/// WORLD state, not player state: it is the vendor that fills up. Saved with
/// the world (parallel lists in <see cref="FishAppetiteSave"/>), reset on New
/// Game, host-authoritative in co-op. Same shape as the buyer craving counter
/// in <c>BuyerLedger</c> — the pattern is reused, not the class.
///
/// Decay is applied lazily whenever a counter is read, from the stamp of the
/// day it was last decayed to, so nothing ticks per frame and a reload does
/// not hand every vendor a free recovery.
/// </summary>
public static class FishAppetite
{
    struct Entry { public int fullness; public double dayStamp; }

    static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

    static string Key(string body, string species) => body + "|" + species;

    static double NowDay => GalaxyTime.Instance != null ? GalaxyTime.Instance.TotalDays : 0.0;

    /// <summary>Current fullness of a market for a species, after decay.</summary>
    public static int Fullness(string body, string species)
    {
        if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(species)) return 0;
        string k = Key(body, species);
        if (!_entries.TryGetValue(k, out var e)) return 0;

        int recoverPerDay = PlanetEconomy.Data.appetite.recoverPerDay;
        double now = NowDay;
        if (recoverPerDay > 0 && now > e.dayStamp)
        {
            int days = (int)System.Math.Floor(now - e.dayStamp);
            if (days > 0)
            {
                e.fullness  = Mathf.Max(0, e.fullness - days * recoverPerDay);
                e.dayStamp += days;
                if (e.fullness == 0) _entries.Remove(k);
                else                 _entries[k] = e;
            }
        }
        return e.fullness;
    }

    /// <summary>Bucket rate with this market's appetite applied. Floored at ×1:
    /// a market never pays LESS than local for something it asked for.</summary>
    public static float Apply(string body, string species, float bucketMultiplier, float stepPerSale)
    {
        int f = Fullness(body, species);
        if (f <= 0) return bucketMultiplier;
        return Mathf.Max(1f, bucketMultiplier * (1f - stepPerSale * f));
    }

    /// <summary>An Import/Delicacy sale went through: the market is one fish
    /// fuller. Call once per fish, AFTER the price was computed for it.</summary>
    public static void NoteSale(string body, string species)
    {
        if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(species)) return;
        string k = Key(body, species);
        int f = Fullness(body, species);          // decays first, so the stamp is current
        _entries[k] = new Entry { fullness = f + 1, dayStamp = _entries.TryGetValue(k, out var e) ? e.dayStamp : NowDay };
    }

    // ── Save / New Game ──────────────────────────────────────────────────────

    public static void ResetAll() => _entries.Clear();

    public static void FillSave(FishAppetiteSave s)
    {
        if (s == null) return;
        s.bodies.Clear(); s.species.Clear(); s.fullness.Clear(); s.dayStamp.Clear();
        foreach (var kv in _entries)
        {
            int bar = kv.Key.IndexOf('|');
            if (bar <= 0) continue;
            s.bodies.Add(kv.Key.Substring(0, bar));
            s.species.Add(kv.Key.Substring(bar + 1));
            s.fullness.Add(kv.Value.fullness);
            s.dayStamp.Add(kv.Value.dayStamp);
        }
    }

    public static void ApplySave(FishAppetiteSave s)
    {
        _entries.Clear();
        if (s == null || s.bodies == null) return;
        int n = Mathf.Min(Mathf.Min(s.bodies.Count, s.species.Count), s.fullness.Count);
        for (int i = 0; i < n; i++)
        {
            if (s.fullness[i] <= 0) continue;
            double stamp = i < s.dayStamp.Count ? s.dayStamp[i] : NowDay;
            _entries[Key(s.bodies[i], s.species[i])] = new Entry { fullness = s.fullness[i], dayStamp = stamp };
        }
    }
}
