using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The planet economy tables — which fish live where, and what each planet's
/// market wants. (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md, Phases 3 + 4.)
///
/// All of it is ONE readable file, <c>StreamingAssets/Economy/planet_economy.json</c>,
/// loaded here the way the dialogue trees are: read once per session in a
/// build, re-read on every query in the Editor so Sam can hand-tune a table
/// and see it without leaving Play mode. <c>Tools ▸ Economy ▸ Draft Planet
/// Tables</c> writes a first draft under the design constraints; Sam edits;
/// <c>Validate</c> checks the result still obeys them.
///
/// Per planet:
///   • <b>catch</b>      — the species that bite in its water (2 per tier on a
///                         main world, 1 per tier on a dwarf)
///   • <b>imports</b>    — off-world species its market pays extra for
///   • <b>delicacies</b> — off-world species it pays top dollar for
///
/// A fish sold at a market lands in exactly one bucket, and the bucket decides
/// the multiplier on its normal price:
///   Local (in this planet's own catch) ×1 · Imported ×1.75 · Delicacy ×3 ·
///   anything else ×0.5 — a dump price, never a refusal.
/// The words, not the numbers, are what the player sees on the boards.
/// </summary>
public static class PlanetEconomy
{
    // ── File shape (JsonUtility: arrays of plain classes, no dictionaries) ──

    [Serializable]
    public class PlanetEntry
    {
        public string body = "";
        public List<string> catchable  = new List<string>();
        public List<string> imports    = new List<string>();
        public List<string> delicacies = new List<string>();
    }

    [Serializable]
    public class Multipliers
    {
        public float local    = 1.0f;
        public float unlisted = 0.5f;

        // ── Per-planet RANGES, not flat rates (Sam, 2026-09-10: "make fish
        // vendors pay 2-4x for imports, then 5-7x for delicacy") ─────────────
        //
        // A flat 1.75x meant every market off-world paid the same, so once you
        // knew a fish was "imported" there was no reason to prefer one planet
        // over another — which is the opposite of the loop the game is built
        // around. Each body now sits at its OWN point in the band, so a route
        // is worth learning.
        //
        // The value is a deterministic hash of the body name: stable forever,
        // identical on both machines in co-op, and needs no save field.
        public float importedMin = 2.0f;
        public float importedMax = 4.0f;
        public float delicacyMin = 5.0f;
        public float delicacyMax = 7.0f;

        // Kept so an older planet_economy.json still parses. No longer read —
        // see the ranges above.
        public float imported = 1.75f;
        public float delicacy = 3.0f;
    }

    [Serializable]
    public class AppetiteRule
    {
        [Tooltip("Multiplier lost per Import/Delicacy sale: effective = bucket × (1 − step × fullness), floored at ×1.")]
        public float stepPerSale = 0.15f;
        [Tooltip("Fullness recovered per in-game (GalaxyTime) day.")]
        public int recoverPerDay = 1;
    }

    [Serializable]
    public class EconomyFile
    {
        public int version = 1;
        public string note = "";
        public Multipliers multipliers = new Multipliers();
        public AppetiteRule appetite = new AppetiteRule();
        public List<PlanetEntry> planets = new List<PlanetEntry>();
    }

    public enum Bucket { Local, Imported, Delicacy, Unlisted }

    public static string Dir  => Path.Combine(Application.streamingAssetsPath, "Economy");
    public static string File => Path.Combine(Dir, "planet_economy.json");

    static EconomyFile _data;
    static readonly Dictionary<string, PlanetEntry> _byBody = new Dictionary<string, PlanetEntry>();
    static readonly Dictionary<string, List<int>>   _catchIdx = new Dictionary<string, List<int>>();
    static DateTime _loadedStamp;
    static bool _loaded;

    public static bool Loaded => _loaded && _data != null;
    public static EconomyFile Data { get { EnsureLoaded(); return _data; } }

    /// <summary>Editor: re-read when the file changes on disk so a save in a text
    /// editor is live in Play mode. Build: read once.</summary>
    static void EnsureLoaded()
    {
#if UNITY_EDITOR
        if (_loaded && System.IO.File.Exists(File) && System.IO.File.GetLastWriteTimeUtc(File) == _loadedStamp) return;
#else
        if (_loaded) return;
#endif
        Load();
    }

    public static void Load()
    {
        _loaded = true;
        _byBody.Clear();
        _catchIdx.Clear();
        _data = null;

        if (!System.IO.File.Exists(File))
        {
            Debug.LogWarning("[PlanetEconomy] no table at " + File +
                             " — every market buys everything at ×1 and every planet fishes the full pool. " +
                             "Run Tools ▸ Economy ▸ Draft Planet Tables.");
            _data = new EconomyFile();
            return;
        }

        try
        {
            _data = JsonUtility.FromJson<EconomyFile>(System.IO.File.ReadAllText(File)) ?? new EconomyFile();
            _loadedStamp = System.IO.File.GetLastWriteTimeUtc(File);
        }
        catch (Exception e)
        {
            Debug.LogError("[PlanetEconomy] could not parse " + File + ": " + e.Message);
            _data = new EconomyFile();
            return;
        }

        foreach (var p in _data.planets)
        {
            if (p == null || string.IsNullOrEmpty(p.body)) continue;
            _byBody[p.body] = p;
            var idx = new List<int>(p.catchable.Count);
            foreach (var id in p.catchable)
            {
                int i = FishingRules.IndexOfId(id);
                if (i >= 0) idx.Add(i);
                else Debug.LogWarning($"[PlanetEconomy] {p.body}: unknown species id '{id}' in catch list — ignored.");
            }
            _catchIdx[p.body] = idx;
        }
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public static bool HasTable(string body)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(body) && _byBody.ContainsKey(body);
    }

    public static PlanetEntry EntryFor(string body)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(body) && _byBody.TryGetValue(body, out var e) ? e : null;
    }

    /// <summary>Species indices that bite on this planet, or null if it has no
    /// table (in which case the caller should fish the whole pool).</summary>
    public static IList<int> CatchableIndices(string body)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(body) && _catchIdx.TryGetValue(body, out var l) && l.Count > 0 ? l : null;
    }

    /// <summary>Which bucket a species lands in at this planet's market.</summary>
    public static Bucket BucketFor(string body, string speciesId)
    {
        var e = EntryFor(body);
        if (e == null || string.IsNullOrEmpty(speciesId)) return Bucket.Local;   // no table: today's behaviour
        if (e.delicacies.Contains(speciesId)) return Bucket.Delicacy;
        if (e.imports.Contains(speciesId))    return Bucket.Imported;
        if (e.catchable.Contains(speciesId))  return Bucket.Local;
        return Bucket.Unlisted;
    }

    /// <summary>Raw bucket multiplier, before appetite. Body-agnostic overload
    /// for the Local/Unlisted rates, which are the same everywhere.</summary>
    public static float BaseMultiplier(Bucket b) => BaseMultiplier(b, null);

    /// <summary>
    /// Raw bucket multiplier for a specific market, before appetite.
    ///
    /// Imported and Delicacy vary PER PLANET inside their band. Where a body
    /// lands is a hash of its name, so it never moves: the price you learn on
    /// Cyclops is the price Cyclops pays, this session and every session, and
    /// on the other player's machine too. Quantised to 0.25 so the boards show
    /// round-ish numbers (3.25x) rather than 3.1847x.
    /// </summary>
    public static float BaseMultiplier(Bucket b, string body)
    {
        var m = Data.multipliers;
        switch (b)
        {
            case Bucket.Imported: return BandFor(body, m.importedMin, m.importedMax);
            case Bucket.Delicacy: return BandFor(body, m.delicacyMin, m.delicacyMax);
            case Bucket.Unlisted: return m.unlisted;
            default:              return m.local;
        }
    }

    /// <summary>The multiplier a market pays RIGHT NOW for a species: bucket rate
    /// with this vendor's current appetite applied. This is the one function
    /// both the sell card and the sale itself must use, so displayed == paid.</summary>
    public static float MultiplierNow(string body, string speciesId)
    {
        var b = BucketFor(body, speciesId);
        float baseMult = BaseMultiplier(b, body);
        if (b != Bucket.Imported && b != Bucket.Delicacy) return baseMult;   // Local and Unlisted never decay
        return FishAppetite.Apply(body, speciesId, baseMult, Data.appetite.stepPerSale);
    }

    /// <summary>
    /// Where a body sits inside a multiplier band, 0..1, from its name.
    ///
    /// FNV-1a rather than string.GetHashCode: .NET randomises string hashing per
    /// process, so GetHashCode would hand a planet a different price every time
    /// the game launched — and a different one to each player in co-op.
    /// </summary>
    static float BandFor(string body, float lo, float hi)
    {
        if (hi < lo) { float t2 = lo; lo = hi; hi = t2; }
        if (string.IsNullOrEmpty(body)) return lo;

        uint h = 2166136261u;
        for (int i = 0; i < body.Length; i++)
        {
            h ^= char.ToLowerInvariant(body[i]);
            h *= 16777619u;
        }
        float t = (h % 1000u) / 999f;
        float v = Mathf.Lerp(lo, hi, t);
        return Mathf.Round(v * 4f) / 4f;   // 0.25 steps
    }

    /// <summary>The word the boards and sell cards use for a bucket.</summary>
    public static string Word(Bucket b)
    {
        switch (b)
        {
            case Bucket.Imported: return "Imported";
            case Bucket.Delicacy: return "Delicacy";
            case Bucket.Unlisted: return "Unlisted";
            default:              return "Local";
        }
    }

    /// <summary>What this market pays per pound for a species RIGHT NOW, appetite
    /// included. Sam's call (2026-09-07 playtest): the boards, the phone and the
    /// sell panel all show real prices, not just the bucket word — you should
    /// know what you will get before you sell.</summary>
    public static float PricePerLbNow(string body, string speciesId)
    {
        int i = FishingRules.IndexOfId(speciesId);
        if (i < 0) return 0f;
        float basePerLb = FishingRules.Species[i].pricePerLb;
        return basePerLb * (HasTable(body) ? MultiplierNow(body, speciesId) : 1f);
    }

    /// <summary>"Marlorb  $6.60/lb" — the line every price list uses.</summary>
    public static string PriceLine(string body, string speciesId)
        => $"{DisplayName(speciesId)}  ${PricePerLbNow(body, speciesId):0.00}/lb";

    public static string DisplayName(string speciesId)
    {
        int i = FishingRules.IndexOfId(speciesId);
        return i >= 0 ? FishingRules.Species[i].displayName : speciesId;
    }
}
