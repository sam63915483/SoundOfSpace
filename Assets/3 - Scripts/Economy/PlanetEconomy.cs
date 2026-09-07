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
        public float imported = 1.75f;
        public float delicacy = 3.0f;
        public float unlisted = 0.5f;
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

    /// <summary>Raw bucket multiplier, before appetite.</summary>
    public static float BaseMultiplier(Bucket b)
    {
        var m = Data.multipliers;
        switch (b)
        {
            case Bucket.Imported: return m.imported;
            case Bucket.Delicacy: return m.delicacy;
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
        float baseMult = BaseMultiplier(b);
        if (b != Bucket.Imported && b != Bucket.Delicacy) return baseMult;   // Local and Unlisted never decay
        return FishAppetite.Apply(body, speciesId, baseMult, Data.appetite.stepPerSale);
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

    public static string DisplayName(string speciesId)
    {
        int i = FishingRules.IndexOfId(speciesId);
        return i >= 0 ? FishingRules.Species[i].displayName : speciesId;
    }
}
