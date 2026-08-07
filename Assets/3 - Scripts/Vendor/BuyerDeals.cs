using UnityEngine;

/// <summary>
/// Pure math for message-negotiated deals (spec §4–§5 of the 2026-08-07
/// messages-app design). No state — everything is a function of buyer
/// identity + the numbers on the table, so it can run for unstreamed buyers
/// and is trivially exercisable from a cheat key.
/// </summary>
public static class BuyerDeals
{
    public enum CounterResult { Accept, CounterBack, Refuse }

    /// A tier's representative per-cap market value: the average BaseValue of
    /// every registered species in that tier (asks name a TIER, not a species;
    /// the agreed price then holds for any species of that tier).
    public static int TierBaseValue(MushroomTier tier)
    {
        int sum = 0, n = 0;
        for (int i = 0; i < MushroomRegistry.Count; i++)
        {
            string key = MushroomRegistry.KeyAt(i);
            if (MushroomRegistry.Tier(key) != tier) continue;
            sum += MushroomRegistry.BaseValue(key); n++;
        }
        return n == 0 ? 10 : Mathf.Max(1, Mathf.RoundToInt((float)sum / n));
    }

    /// What this buyer genuinely values one cap of this tier at, right now
    /// (multiplier × taste × bond — saturation deliberately excluded: they
    /// text BECAUSE they're empty).
    public static int TruePricePerCap(string id, MushroomTier tier)
    {
        float v = TierBaseValue(tier)
                * NPCMushroomPrice.MultiplierOf(id)
                * NPCMushroomPrice.TasteOf(id, tier)
                * BuyerLedger.BondBonus(id);
        return Mathf.Max(1, Mathf.RoundToInt(v));
    }

    // ── Want generation (spec §4) ──────────────────────────────────────────

    /// Usually their favourite tier (~70%), otherwise their neutral one,
    /// never the disliked one.
    public static MushroomTier PickAskTier(string id)
    {
        var fav = NPCMushroomPrice.FavouriteTierOf(id);
        if (Random.value < 0.7f) return fav;
        var dis = NPCMushroomPrice.DislikedTierOf(id);
        for (int t = 0; t < 3; t++)
            if ((MushroomTier)t != fav && (MushroomTier)t != dis) return (MushroomTier)t;
        return fav;
    }

    /// 50–100% of their appetite, at least 2.
    public static int PickAskQty(string id)
    {
        int max = NPCMushroomPrice.AppetiteMaxOf(id);
        return Mathf.Max(2, Random.Range(Mathf.CeilToInt(max * 0.5f), max + 1));
    }

    /// Their opening offer: ~90% of their true number (they lowball —
    /// that's what countering is for).
    public static int OpeningOffer(string id, MushroomTier tier) =>
        Mathf.Max(1, Mathf.RoundToInt(TruePricePerCap(id, tier) * 0.9f));

    // ── Counter resolution (spec §4, incl. the counter-back rule) ──────────

    /// Player counters at <paramref name="ask"/> per cap. One exchange each,
    /// no loops:
    ///   within patience          → Accept at the player's number
    ///   ≤ patience × 1.25        → CounterBack (midpoint, clamped to their
    ///                              patience ceiling, never below their offer)
    ///   beyond that (outrageous) → Refuse, deal off, bond ding
    public static CounterResult ResolveCounter(string id, MushroomTier tier, int ask, out int counterBack)
    {
        counterBack = 0;
        int truePrice = TruePricePerCap(id, tier);
        float patience = NPCMushroomPrice.PatienceOf(id);
        float ceiling = truePrice * patience;
        if (ask <= ceiling) return CounterResult.Accept;
        if (ask <= ceiling * 1.25f)
        {
            int opening = OpeningOffer(id, tier);
            counterBack = Mathf.Min(Mathf.RoundToInt((opening + ask) / 2f), Mathf.FloorToInt(ceiling));
            counterBack = Mathf.Max(counterBack, opening); // never counter below their own offer
            return CounterResult.CounterBack;
        }
        return CounterResult.Refuse;
    }

    // ── Windows & gratitude (spec §4) ──────────────────────────────────────

    public static readonly int[] WindowMinutes = { 5, 10, 15 };
    public const float GraceSeconds = 60f;

    /// +15% / +10% / +5% for the 5 / 10 / 15 minute promise.
    public static float GratitudeBonus(int windowMinutes)
    {
        if (windowMinutes <= 5) return 1.15f;
        if (windowMinutes <= 10) return 1.10f;
        return 1.05f;
    }

    // ── Substitution (spec §5c, the fuzzy-fulfilment rule) ─────────────────

    /// Chance the buyer accepts a delivery that differs from the agreed order.
    /// Calibration (agreed 3 rare): 3 uncommon → 50%, 5 uncommon → 70%,
    /// 2 rare → ~87%, 5 common → 20%, any tier up → ~guaranteed.
    public static float SubstitutionChance(MushroomTier agreedTier, int agreedQty,
                                           MushroomTier offeredTier, int offeredQty)
    {
        int tierDelta = (int)offeredTier - (int)agreedTier; // + is better
        float qtyRatio = agreedQty > 0 ? (float)offeredQty / agreedQty : 1f;
        float chance = 1f
            + (tierDelta < 0 ? 0.5f * tierDelta : 0.25f * tierDelta)
            + 0.3f * Mathf.Max(0f, qtyRatio - 1f)
            - 0.4f * Mathf.Max(0f, 1f - qtyRatio);
        return Mathf.Clamp(chance, 0.05f, 1f);
    }

    /// Exact fulfilment = right tier and at least the agreed quantity.
    public static bool IsExact(MushroomTier agreedTier, int agreedQty,
                               MushroomTier offeredTier, int offeredQty) =>
        offeredTier == agreedTier && offeredQty >= agreedQty;
}
