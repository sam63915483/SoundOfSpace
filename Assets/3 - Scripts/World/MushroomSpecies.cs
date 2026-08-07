using System.Collections.Generic;
using UnityEngine;

public enum MushroomTier { Common, Uncommon, Rare }

/// <summary>
/// The product table for the mushroom economy: what each species is called on
/// the street, what tier it sits in, what it's worth, and what eating it does.
///
/// Keyed by PREFAB NAME, same as <see cref="MushroomRegistry"/> — the key is
/// stored in hotbar slots, locker slots and save files, so it must not be an
/// index into anything.
///
/// Design (docs/MUSHROOM_STRAINS.md): 23 species split 11 / 7 / 5 across
/// common / uncommon / rare — the closest integer match to Sam's 50/30/20. The
/// five rare ones are the whole Amanita genus, which is the real world's actual
/// psychoactive mushroom (fly agaric) and its actual lethal one (death cap), so
/// the tiering fell out of the art pack rather than being forced onto it.
///
/// Two things worth knowing before tuning:
///
///  • <b>Creepers.</b> RawFishTripController.StartTrip has always accepted an
///    early/late phase split, and nothing in the game used it — every trip was
///    flat. The Inkcaps set <see cref="Entry.creeperLeadIn"/>, so they do almost
///    nothing for the first N seconds and then land. No new system.
///  • <b>Negative heal.</b> The Deathcaps HURT you. That makes the two most
///    valuable strains pure product — you cannot safely sample your own stock.
///
/// Unknown keys (a prefab Sam drags into MushroomSpawner that isn't listed here)
/// fall back to a hash of the name, which is exactly what the game did before
/// this table existed. Adding a prefab can therefore never throw or produce a
/// zero-value mushroom; it just doesn't get authored values until someone adds
/// a row.
/// </summary>
public static class MushroomSpecies
{
    public struct Entry
    {
        public string key;
        public string displayName;
        public MushroomTier tier;
        public int   baseValue;      // credits per cap at 1.0x buyer multiplier
        public float tripSeconds;
        public float colour;         // 0..1 colourScale
        public float wave;           // 0..1 "world breathes"
        public float kaleido;        // 0..1 mirror-tiling
        public float creeperLeadIn;  // seconds of muted lead-in; 0 = flat trip
        public float heal;           // HP per cap; NEGATIVE means it damages you
        public int   spawnWeight;    // relative wild-spawn frequency
    }

    // Encounter weighting by tier. Species COUNT is 11/7/5 (= 48/30/22%), which
    // already matches Sam's 50/30/20 split. These weights are the separate knob
    // that makes rare actually FEEL rare: at 5/3/1 the wild mix comes out
    // ~68% / 26% / 6%. Raise Rare to 2 if 6% reads as too scarce in play.
    const int WCommon = 5, WUncommon = 3, WRare = 1;

    static Entry E(string key, string name, MushroomTier tier, int value, float trip,
                   float colour, float wave, float kaleido, float heal, float creeper = 0f)
    {
        return new Entry
        {
            key = key, displayName = name, tier = tier, baseValue = value,
            tripSeconds = trip, colour = colour, wave = wave, kaleido = kaleido,
            creeperLeadIn = creeper, heal = heal,
            spawnWeight = tier == MushroomTier.Common ? WCommon
                        : tier == MushroomTier.Uncommon ? WUncommon : WRare,
        };
    }

    // Display names are the REAL species names off the prefabs (Sam's call
    // 2026-08-06 — invented street names lost the thing that made the pack feel
    // like actual mushrooms). Where the pack ships a size pair, the smaller one
    // is prefixed "Small" so the two are still tellable apart in a slot; that
    // prefix is the only invented word in the column.
    static readonly Entry[] Table =
    {
        // ── COMMON (11) — cheap, short, mild ────────────────────────────────
        E("Champignon_little",            "Small Champignon",       MushroomTier.Common,  8,  15f, .15f, .10f, .05f,  5f),
        E("Champignon_big",               "Champignon",             MushroomTier.Common, 10,  18f, .20f, .15f, .05f,  6f),
        E("Agaricus",                     "Agaricus",               MushroomTier.Common, 10,  18f, .10f, .25f, .05f,  5f),
        // Tev fronts this one (MushroomQuest) — it's the player's reference high.
        E("Agaricales_big",               "Agaricales",             MushroomTier.Common, 12,  20f, .25f, .15f, .10f,  6f),
        E("Boletus_big",                  "Boletus",                MushroomTier.Common, 12,  20f, .10f, .30f, .00f, 10f),
        E("ImleriaBadia_little",          "Small Imleria Badia",    MushroomTier.Common,  9,  15f, .20f, .10f, .10f,  5f),
        E("ImleriaBadia_big",             "Imleria Badia",          MushroomTier.Common, 12,  20f, .30f, .15f, .10f,  6f),
        E("Leccinum_little",              "Small Leccinum",         MushroomTier.Common, 10,  18f, .15f, .20f, .05f,  6f),
        E("Leccinum_big",                 "Leccinum",               MushroomTier.Common, 13,  22f, .20f, .25f, .10f,  7f),
        E("Cantharellaceae_little",       "Small Cantharellaceae",  MushroomTier.Common, 11,  18f, .35f, .05f, .05f,  5f),
        E("Fomes",                        "Fomes",                  MushroomTier.Common, 14,  25f, .05f, .35f, .15f,  8f),

        // ── UNCOMMON (7) — each owns ONE dial, so they're learnable by feel ──
        E("Cantharellaceae_big",          "Cantharellaceae",        MushroomTier.Uncommon, 24, 35f, .75f, .10f, .10f,  8f),
        E("Lactarius_little",             "Small Lactarius",        MushroomTier.Uncommon, 22, 30f, .25f, .65f, .10f, 10f),
        E("Lactarius_big",                "Lactarius",              MushroomTier.Uncommon, 27, 40f, .30f, .75f, .15f, 12f),
        E("Macrolepiota_little",          "Small Macrolepiota",     MushroomTier.Uncommon, 24, 35f, .20f, .20f, .55f,  8f),
        E("Macrolepiota_big",             "Macrolepiota",           MushroomTier.Uncommon, 30, 45f, .30f, .25f, .65f, 10f),
        // The creepers: 20s / 25s of near-nothing, then it lands.
        E("Agaricus_Atramentarius_little","Small Atramentarius",    MushroomTier.Uncommon, 26, 35f, .40f, .55f, .70f,  8f, creeper: 20f),
        E("Agaricus_Atramentarius_big",   "Agaricus Atramentarius", MushroomTier.Uncommon, 34, 50f, .50f, .65f, .85f, 10f, creeper: 25f),

        // ── RARE (5) — the whole Amanita genus ──────────────────────────────
        E("Amanita_little",               "Small Amanita",          MushroomTier.Rare, 48,  60f, .85f, .55f, .60f, 15f),
        E("Amanita_big",                  "Amanita",                MushroomTier.Rare, 65,  80f, 1.0f, .70f, .80f, 18f),
        // Inverted on purpose: colour DRAINS instead of blowing out, so "rare"
        // doesn't just mean "every slider at max". Biggest heal in the game.
        E("Amanita_Ovoidea",              "Amanita Ovoidea",        MushroomTier.Rare, 55,  70f, .00f, .90f, .30f, 20f),
        E("Amanita_Phalloides_little",    "Small Phalloides",       MushroomTier.Rare, 70,  90f, .90f, .85f, .90f,-15f),
        E("Amanita_Phalloides_big",       "Amanita Phalloides",     MushroomTier.Rare, 90, 120f, 1.0f, 1.0f, 1.0f,-25f),
    };

    static readonly Dictionary<string, int> _byKey = Build();

    static Dictionary<string, int> Build()
    {
        var d = new Dictionary<string, int>(Table.Length);
        for (int i = 0; i < Table.Length; i++) d[Table[i].key] = i;
        return d;
    }

    public static int Count => Table.Length;
    public static Entry At(int i) => Table[Mathf.Clamp(i, 0, Table.Length - 1)];

    public static bool TryGet(string key, out Entry e)
    {
        if (!string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out int i)) { e = Table[i]; return true; }
        e = default;
        return false;
    }

    /// The authored entry, or a hash-derived stand-in for a species that isn't
    /// in the table yet. Never returns a zero-value mushroom.
    public static Entry Get(string key)
    {
        if (TryGet(key, out var e)) return e;
        return Fallback(key);
    }

    /// Pre-table behaviour, kept for unlisted prefabs: dials from a hash of the
    /// name, mid-tier value, the old flat 30s trip and flat 5 HP heal.
    static Entry Fallback(string key)
    {
        uint h = Hash(key);
        return new Entry
        {
            key = key,
            displayName = Prettify(key),
            tier = MushroomTier.Common,
            baseValue = 12,
            tripSeconds = 30f,
            colour  = ((h)       & 0xFFFFu) / 65535f,
            wave    = ((h >>  7) & 0xFFFFu) / 65535f,
            kaleido = ((h >> 15) & 0xFFFFu) / 65535f,
            creeperLeadIn = 0f,
            heal = 5f,
            spawnWeight = WCommon,
        };
    }

    public static string DisplayName(string key) => Get(key).displayName;
    public static MushroomTier Tier(string key)  => Get(key).tier;
    public static int BaseValue(string key)      => Get(key).baseValue;
    public static int SpawnWeight(string key)    => Get(key).spawnWeight;

    /// Tier colour for slot corners and the sell panel. Grey / blue / purple.
    public static Color32 TierColor(MushroomTier t) => t switch
    {
        MushroomTier.Rare     => new Color32(0xC8, 0x6B, 0xFF, 0xFF),
        MushroomTier.Uncommon => new Color32(0x4F, 0xB0, 0xFF, 0xFF),
        _                     => new Color32(0x9F, 0xB4, 0xC7, 0xFF),
    };

    public static string TierName(MushroomTier t) => t switch
    {
        MushroomTier.Rare     => "RARE",
        MushroomTier.Uncommon => "UNCOMMON",
        _                     => "COMMON",
    };

    /// "Amanita_Phalloides_big" → "Amanita Phalloides". Only used for species
    /// with no authored row; mirrors MushroomSpawner's old prettifier.
    static string Prettify(string key)
    {
        if (string.IsNullOrEmpty(key)) return "mushroom";
        string s = key.Replace("_big", "").Replace("_little", "").Replace("_", " ").Trim();
        return string.IsNullOrEmpty(s) ? "mushroom" : s;
    }

    // FNV-1a + avalanche, identical to the hash MushroomEffect used before the
    // table existed — so an unlisted species trips exactly as it always did.
    static uint Hash(string s)
    {
        uint h = 2166136261u;
        if (!string.IsNullOrEmpty(s))
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
        h ^= h >> 16; h *= 2246822507u;
        h ^= h >> 13; h *= 3266489909u;
        h ^= h >> 16;
        return h;
    }
}
