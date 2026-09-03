// The fishing rulebook: species table, tier roll, sun-angle bite rate, and the
// fight maths. Phase 1 of docs/Handoff_FishingRevamp_Phase1_v1.md.
//
// ZERO UnityEngine REFERENCES — deliberately, and it must stay that way.
// Everything here is compiled standalone by
// prototypes/shuttle-computer/test/verify-fishing.py and executed headlessly,
// the same trick TraxLibrary/AlienTaste use. That is the only way [TEST] 1-4 in
// the handoff (fight sim, bite-rate sweep, 10k-roll species distribution, bait
// accounting) can be checked without a play session. Tints are stored as raw
// RGB bytes rather than Color for exactly this reason; FishSpeciesUnity.cs does
// the Color32 conversion on the Unity side.

using System;

public enum FishTier { Common = 0, Uncommon = 1, Rare = 2 }

public enum BaitKind { None = 0, Grubs = 1, Glowworms = 2, Voidmaggots = 3 }

/// <summary>One row of the species table. Value type; the table is static.</summary>
public struct FishSpecies
{
    public string id;
    public string displayName;
    public FishTier tier;
    /// Index into that tier's model array. Only 0 is wired today — the project
    /// has ONE fish model per tier, not three (the handoff's "9 total" claim is
    /// false; see its STATUS block). Kept so extra shapes are a pure asset
    /// assignment later, with no code change.
    public int modelIndex;
    public byte tintR, tintG, tintB;
    public float weightMin, weightMax;
    public float pricePerLb;
    /// Seconds of RUN the fish has in it. Not the win condition -- distance is
    /// (see FishFightSim). This is how long it keeps surging before it is spent.
    public float staminaMin, staminaMax;
}

public static class FishingRules
{
    // ── Species table ────────────────────────────────────────────────────────
    // Names and tints are placeholders in the same way the TRAX genres were —
    // Sam renames in place. The four species in a tier share that tier's single
    // model and are told apart by tint, name and weight range (Sam's call,
    // 2026-09-01, forced by there being 3 models rather than 9).

    public static readonly FishSpecies[] Species =
    {
        Sp("bassk",      "Bassk",      FishTier.Common,   0, 0x6B, 0x7A, 0x3A,  1f,  8f, 1.0f),
        Sp("truttle",    "Truttle",    FishTier.Common,   0, 0xB0, 0x8D, 0x5A,  1f,  6f, 1.0f),
        Sp("perchik",    "Perchik",    FishTier.Common,   0, 0xD8, 0xB8, 0x3A,  1f,  5f, 1.0f),
        Sp("smelk",      "Smelk",      FishTier.Common,   0, 0xB8, 0xC2, 0xC8,  1f,  3f, 1.2f),

        Sp("emberbass",  "Emberbass",  FishTier.Uncommon, 0, 0xD8, 0x63, 0x2A,  6f, 18f, 1.6f),
        Sp("glimtrout",  "Glimtrout",  FishTier.Uncommon, 0, 0x3A, 0xA8, 0xA0,  5f, 15f, 1.6f),
        Sp("nullpike",   "Nullpike",   FishTier.Uncommon, 0, 0x2A, 0x2A, 0x30,  8f, 22f, 1.5f),
        Sp("sturgle",    "Sturgle",    FishTier.Uncommon, 0, 0x7C, 0x7F, 0x86, 10f, 24f, 1.5f),

        Sp("marlorb",    "Marlorb",    FishTier.Rare,     0, 0x5A, 0x3C, 0x9E, 20f, 50f, 2.0f),
        Sp("tarpune",    "Tarpune",    FishTier.Rare,     0, 0xD4, 0x6A, 0x9A, 15f, 40f, 2.2f),
        Sp("muskrellon", "Muskrellon", FishTier.Rare,     0, 0x3E, 0x6B, 0x2E, 25f, 50f, 2.0f),
        Sp("coelancer",  "Coelancer",  FishTier.Rare,     0, 0x2E, 0x5C, 0x8A, 18f, 45f, 2.4f),
    };

    static FishSpecies Sp(string id, string name, FishTier tier, int model,
                          byte r, byte g, byte b, float wMin, float wMax, float perLb)
    {
        float sMin, sMax;
        StaminaRangeForTier(tier, out sMin, out sMax);
        return new FishSpecies
        {
            id = id, displayName = name, tier = tier, modelIndex = model,
            tintR = r, tintG = g, tintB = b,
            weightMin = wMin, weightMax = wMax, pricePerLb = perLb,
            staminaMin = sMin, staminaMax = sMax,
        };
    }

    // ── Fight tuning ─────────────────────────────────────────────────────────
    // Handoff defaults. Mirrored onto FishingTuning (a ScriptableObject) so Sam
    // can retune without a recompile; these are the fallbacks when no asset is
    // assigned, and the numbers the headless tests run against.

    // Tension gained per second of holding, x the tier's pull.
    // Raised 35 -> 48 alongside ReelSpeed on 2026-09-01. It HAS to move with the
    // reel: a faster reel closes the distance sooner, so at 35 a player could
    // simply hold the button and brute-force a light uncommon before the line
    // ever snapped. The headless bot caught that immediately -- "hold forever"
    // started landing 47 of the 600 fish it is supposed to lose.
    public const float ReelRate  = 48f;
    public const float RelaxRate = 45f;   // tension shed per second while released
    public const float DrainRate = 1f;    // stamina spent per second of holding or running
    public const float TensionMax = 100f;

    // ── Distance model (2026-09-01 rewrite) ──────────────────────────────────
    // The fight is won by bringing the fish IN, not by draining a hidden bar.

    /// Metres per second the reel gains on a fish that isn't resisting.
    /// Raised 4.5 -> 6.5 on 2026-09-01: Sam asked for runs that take the fish
    /// noticeably further out, so the reel has to pull harder to compensate or
    /// every fight turns into a stalemate.
    public const float ReelSpeed = 6.5f;
    /// Land the fish once it is this close. Measured against the bobber's REAL
    /// position, not a running total — see FishFightSim.SyncDistance.
    public const float LandDistance = 2f;
    /// A run can never take the fish further out than this multiple of the
    /// original cast — without it, a long fight could drag on forever.
    public const float MaxRunOutFactor = 1.6f;

    /// <summary>
    /// How hard a fish drags back against the reel, as a fraction of reel speed.
    /// Scales with weight inside the species' own range, so a 48 lb rare really
    /// does feel like a different animal from a 20 lb one. Halved once the fish
    /// is spent.
    /// </summary>
    public static float ResistFor(int speciesIndex, float weightLb)
    {
        var s = Species[speciesIndex];
        float span = s.weightMax - s.weightMin;
        float f = span > 0.0001f ? (weightLb - s.weightMin) / span : 0f;
        if (f < 0f) f = 0f; else if (f > 1f) f = 1f;
        float max = s.tier == FishTier.Rare ? 0.62f
                  : s.tier == FishTier.Uncommon ? 0.42f
                  : 0.12f;
        return max * (0.45f + 0.55f * f);
    }

    /// Metres per second a running fish takes back. Commons never run.
    /// Raised on 2026-09-01 with ReelSpeed: a run should visibly lose you
    /// ground, not just slow you down.
    public static float RunSpeedForTier(FishTier tier)
    {
        switch (tier)
        {
            case FishTier.Rare:     return 3.4f;
            case FishTier.Uncommon: return 2.3f;
            default:                return 0f;
        }
    }

    // ── Rod / line load curve ────────────────────────────────────────────────

    /// <summary>
    /// Maps raw load 0-1 onto how far the rod is actually bent.
    ///
    /// Sam, 2026-09-01: "the rod should bend a tiny bit when the bar is less
    /// than half full, then when half full or more reaching the breaking point
    /// it should get to its max bend." So this is a KNEE, not a line: below the
    /// knee the rod barely moves, above it the bend runs away toward maximum.
    /// The payoff is that a deeply bent rod means something — it only ever
    /// happens near the breaking point.
    /// </summary>
    public const float BendKnee = 0.5f;      // where the curve turns up
    /// How bent the rod is AT the knee, as a fraction of the maximum.
    ///
    /// Raised 0.15 -> 0.45 on 2026-09-01. At 0.15 the rod barely moved: simply
    /// reeling is load 0.45, which sat just under the knee and produced ~5
    /// degrees of bend on a 38 degree maximum. Sam: "it barely bends at all...
    /// I just don't want it FULLY bending when the bar is less than 50% full, it
    /// should still noticeably bend though." 0.45 is that: clearly working under
    /// half load, and the full dramatic bow saved for the breaking point.
    public const float BendAtKnee = 0.45f;

    public static float BendCurve(float load01) => BendCurve(load01, BendKnee, BendAtKnee);

    public static float BendCurve(float load01, float knee, float atKnee)
    {
        if (load01 <= 0f) return 0f;
        if (load01 >= 1f) return 1f;
        if (knee <= 0.001f) return load01;
        if (load01 <= knee)
            return atKnee * (load01 / knee);
        float t = (load01 - knee) / (1f - knee);
        return atKnee + (1f - atKnee) * t * t;
    }

    // ── Line tightness ───────────────────────────────────────────────────────
    // Seconds to come tight, and seconds to fall slack again. The asymmetry is
    // deliberate and Sam-specified: tightening is a quick, readable event; going
    // slack is a slow release you watch happen. Getting these the same, or the
    // droop too fast, is what made the line read as a binary switch.
    public const float TautSeconds  = 0.45f;
    public const float SlackSeconds = 1.2f;

    // ── How fast the bar fills (Sam, 2026-09-01) ─────────────────────────────
    //
    // Three separate asks, and together they give the fight the arc he wanted:
    // dangerous and stuttery at the start, forgiving once the fish is worn down.
    //
    // 1. "make it so that the status bar fills 2x slower"  -> SteadyTensionScale
    // 2. "keep the same fill up speed when the fish is fighting and pulling and
    //    your left clicking to reel at the same time"      -> RunTensionScale 1.0
    //    Reeling into a run is therefore FOUR times worse than reeling calmly
    //    (2x from the doubled pull, 2x from skipping the steady discount), which
    //    is what makes "let go the instant it runs" the whole skill.
    // 3. "the more tired the fish is, the slower the bar fills"
    //                                                      -> TensionVigourScale

    /// Applied while reeling a fish that is NOT running.
    public const float SteadyTensionScale = 0.5f;
    /// Applied while reeling a fish that IS running — unchanged from before.
    public const float RunTensionScale = 1f;

    /// <summary>
    /// Tension multiplier from how much fight the fish has left. A fresh fish
    /// loads the line hard; a worn-out one barely does, so the endgame is a calm
    /// reel-in rather than another knife-edge.
    /// </summary>
    public static float TensionVigourScale(float vigour)
    {
        if (vigour < 0f) vigour = 0f; else if (vigour > 1f) vigour = 1f;
        return Lerp(0.3f, 1f, vigour);
    }

    /// The handoff's "release for > 3s at zero tension and the fish gets off".
    public const float SlackEscapeSeconds = 3f;

    public static float PullForTier(FishTier tier)
    {
        switch (tier)
        {
            case FishTier.Rare:     return 1.8f;
            case FishTier.Uncommon: return 1.4f;
            default:                return 1.0f;
        }
    }

    /// <summary>
    /// Seconds of RUN a tier's fish has in it, before the weight nudge. Since
    /// the 2026-09-01 rewrite this is not the win condition -- bringing the fish
    /// in is. Stamina is what the fish spends surging and resisting, and when it
    /// hits zero the fish is spent: the runs stop and it comes in easy.
    ///
    /// Commons keep the shortened 1.6-2.6 range from the v1 pass. That deviated
    /// from the handoff's 2.5-4 because a hold-forever player snaps a common at
    /// 100/(35 x 1.0) = 2.857s, so [TEST] 1's "land every common" was impossible
    /// at 2.5-4; the handoff's own prose ("a 2 lb common is two seconds") agreed.
    /// Still true, and the test still enforces it.
    /// </summary>
    public static void StaminaRangeForTier(FishTier tier, out float min, out float max)
    {
        switch (tier)
        {
            // Trimmed on 2026-09-01 when runs got faster. Bigger runs cost the
            // fish more ground per second, so the same stamina bought a LONGER
            // fight (rare median went 17.5s -> 21.2s) -- the opposite of what
            // asking for a faster reel was meant to achieve.
            case FishTier.Rare:     min = 7f;  max = 11f;  break;
            case FishTier.Uncommon: min = 4.2f; max = 6.5f; break;
            default:                min = 1.6f; max = 2.6f; break;
        }
    }

    /// Commons never run. Uncommons run every 2-4s; rares more often, 1.5-3s.
    /// A run doubles pull for 1-2s — the whole skill of the fight is letting go
    /// during one.
    public static bool TierRuns(FishTier tier) => tier != FishTier.Common;

    public static void RunIntervalForTier(FishTier tier, out float min, out float max)
    {
        if (tier == FishTier.Rare) { min = 1.5f; max = 3f; }
        else                       { min = 2f;   max = 4f; }
    }

    public const float RunDurationMin = 1f;
    public const float RunDurationMax = 2f;
    public const float RunPullMultiplier = 2f;

    // ── Fish size on screen ──────────────────────────────────────────────────
    // ONE law for how big a fish LOOKS, used by the hooked fish on the line and
    // the fish held in hand, so the two can never disagree. Cube-root of
    // weight, because that is how mass actually scales with length -- and it is
    // what finally makes a 1 lb and a 50 lb fish read as different animals
    // (Sam, 2026-09-02: "a 50 lb rare looks the same weight as a 5 pound
    // common"). 1 lb ~ 0.34 m, 8 lb ~ 0.68 m, 27 lb ~ 1.02 m, 50 lb ~ 1.25 m:
    // a genuine beast without tipping into fake-looking.
    public const float BodyLenPerCubeRootLb = 0.34f;

    public static float BodyLengthForWeight(float weightLb)
    {
        if (weightLb < 0.25f) weightLb = 0.25f;
        return BodyLenPerCubeRootLb * (float)Math.Pow(weightLb, 1.0 / 3.0);
    }

    /// <summary>
    /// Girth multiplier by weight -- the SECOND half of "looks its weight"
    /// (Sam, 2026-09-02: the 45 lb rare "doesn't look near as fat or big as
    /// it should" on the line). Applied identically to the fish in hand and
    /// the fish in the water, so the shape never changes between them: width
    /// gets the full factor, belly depth 60% of it, length none (length is
    /// BodyLengthForWeight's job). 1 lb ~ 0.82 (slim), 8 lb ~ 0.98,
    /// 45 lb ~ 1.79 (a proper slab).
    /// </summary>
    public static float GirthFactorForWeight(float weightLb)
    {
        float g = 0.8f + 0.022f * weightLb;
        if (g < 0.8f) g = 0.8f;
        if (g > 1.9f) g = 1.9f;
        return g;
    }

    // ── Sun-angle bite rate ──────────────────────────────────────────────────
    // dot = Dot(surface normal under the bobber, direction to the sun).
    // +1 noon, 0 sunrise/sundown, -1 midnight.

    public const float TwilightEdge = 0.25f;   // |dot| inside this is full twilight
    public const float BandBlend    = 0.10f;   // lerp width either side, so nothing pops

    // Sam, 2026-09-02: "I wait like 40 seconds for a bite and that's too long."
    // The old day rate (1.6x) stacked with the old no-bait penalty (1.6x) into
    // exactly that. The redesign: EVERY band bites at a reasonable clip and the
    // bands differ mostly in WHAT bites (see TierWeights) -- day is the
    // common-fish grind, twilight is still the golden hour, night sits between.
    public const float WaitMultTwilight = 0.5f;   // best fishing
    public const float WaitMultNight    = 0.75f;
    public const float WaitMultDay      = 0.85f;

    /// <summary>
    /// Time-to-bite multiplier. Continuous everywhere and monotone within each
    /// region — [TEST] 2 sweeps dot from -1 to 1 and checks exactly that.
    /// </summary>
    public static float WaitMultiplier(float dot)
    {
        if (dot >= 0f)
        {
            // twilight -> day as the sun climbs
            float t = Ramp(dot, TwilightEdge - BandBlend, TwilightEdge + BandBlend);
            return Lerp(WaitMultTwilight, WaitMultDay, t);
        }
        // twilight -> night as the sun sinks
        float u = Ramp(-dot, TwilightEdge - BandBlend, TwilightEdge + BandBlend);
        return Lerp(WaitMultTwilight, WaitMultNight, u);
    }

    /// <summary>
    /// How "twilight" the light is, 1 inside the band and 0 well outside it.
    /// Blends the tier weights so sunset fishing is better in KIND, not just
    /// faster — chasing the terminator is an intended meta.
    /// </summary>
    public static float TwilightFactor(float dot)
    {
        float a = dot < 0f ? -dot : dot;
        return 1f - Ramp(a, TwilightEdge - BandBlend, TwilightEdge + BandBlend);
    }

    // ── Tier roll ────────────────────────────────────────────────────────────
    // Sam's revision 2026-09-02: the light bands now differ in KIND, not just
    // rate. Day is the common-fish grind ("more bites but only more common
    // bites"); night boosts uncommons and rares almost as well as twilight
    // ("at night have it boost the chance to catch uncommons and rares and
    // same with sunset sunrise"); twilight stays the golden hour. The old
    // single 45/35/20 base meant midnight fished identically to noon.

    public const float DayCommon   = 58f, DayUncommon   = 28f, DayRare   = 14f;
    public const float TwiCommon   = 38f, TwiUncommon   = 37f, TwiRare   = 25f;
    public const float NightCommon = 40f, NightUncommon = 36f, NightRare = 24f;

    /// <summary>
    /// How much longer you wait for a bite because of your bait.
    ///
    /// BAIT IS OPTIONAL (Sam, 2026-09-01). Fishing bare-handed works; it is just
    /// slower and skews common. Bait is an upgrade you choose, not a gate you
    /// maintain -- the mandatory version read as a chore rather than a choice.
    /// </summary>
    public static float BaitWaitMultiplier(BaitKind bait)
    {
        switch (bait)
        {
            case BaitKind.Voidmaggots: return 0.75f;
            case BaitKind.Glowworms:   return 0.85f;
            case BaitKind.Grubs:       return 1.0f;
            // No bait: a mild penalty, not a drought. 1.6x stacked with the old
            // day rate into ~40 s waits (Sam: "that's too long... I should wait
            // much less time but just get common fishes"). The bare hook's real
            // cost lives in TierWeights -- it fishes COMMON, not slow.
            default:                   return 1.15f;
        }
    }

    /// <summary>
    /// Tier weights for a given light angle and bait.
    ///
    /// Every bait shift moves weight between the tiers WITHOUT closing any tier
    /// off. Fishing with no bait still lands rares -- they are simply rare
    /// (Sam's call: "you should still be able to get a rare, just have it be
    /// rare"). Common is floored at 0 so an extreme future bait can never
    /// produce a negative weight.
    ///
    ///   none         -8 rare, -7 uncommon  -> back into common
    ///   Grubs         neutral (the baseline table)
    ///   Glowworms    +10 uncommon          <- out of common
    ///   Voidmaggots  +8 rare, +7 uncommon  <- out of common
    /// </summary>
    public static void TierWeights(float dot, BaitKind bait,
                                   out float common, out float uncommon, out float rare)
    {
        // Three bands, blended with the same ramps as the bite rate so nothing
        // pops as the terminator sweeps over a sitting bobber. Continuous
        // through dot = 0 by construction (both sides start at the twilight
        // table).
        if (dot >= 0f)
        {
            float t = Ramp(dot, TwilightEdge - BandBlend, TwilightEdge + BandBlend);
            common   = Lerp(TwiCommon,   DayCommon,   t);
            uncommon = Lerp(TwiUncommon, DayUncommon, t);
            rare     = Lerp(TwiRare,     DayRare,     t);
        }
        else
        {
            float t = Ramp(-dot, TwilightEdge - BandBlend, TwilightEdge + BandBlend);
            common   = Lerp(TwiCommon,   NightCommon,   t);
            uncommon = Lerp(TwiUncommon, NightUncommon, t);
            rare     = Lerp(TwiRare,     NightRare,     t);
        }

        if (bait == BaitKind.None)
        {
            // Move weight the OTHER way, but never to zero: a bare hook can
            // still turn up something extraordinary, which is the whole reason
            // to keep casting before you can afford bait.
            //
            // PROPORTIONAL, not flat (Sam, 2026-09-02: "using no bait and
            // fishing at night should result in 1-2 rare fish, 2-3 uncommon
            // and around 4 common, not just all commons"). The old flat
            // -8 rare / -7 uncommon gutted the good bands hardest -- night's
            // rare 24 fell to 16 while day's 14 fell to 6 -- which is why his
            // night session came back nearly all commons. Keeping 75% of rare
            // and 85% of uncommon lands night/no-bait at ~18/31/51, his exact
            // numbers, while day stays the common grind.
            float offUncommon = uncommon * 0.15f;
            float offRare     = rare     * 0.25f;
            uncommon -= offUncommon;
            rare     -= offRare;
            common   += offUncommon + offRare;
            return;
        }

        float toUncommon = 0f, toRare = 0f;
        if (bait == BaitKind.Glowworms)   { toUncommon = 10f; }
        if (bait == BaitKind.Voidmaggots) { toUncommon = 7f; toRare = 8f; }

        float take = toUncommon + toRare;
        if (take > common) take = common;         // never overdraw the common pool
        if (take > 0f)
        {
            float scale = take / (toUncommon + toRare);
            toUncommon *= scale;
            toRare     *= scale;
        }
        common   -= (toUncommon + toRare);
        uncommon += toUncommon;
        rare     += toRare;
        if (common < 0f) common = 0f;
    }

    static float Min(float a, float b) => a < b ? a : b;

    // ── Cast distance (Sam, 2026-09-01) ──────────────────────────────────────
    // "the further away you cast, the higher chance you have for catching a more
    // rare and bigger fish, vs doing small casts right in front of you will get
    // smaller more common fish."
    //
    // This is the best kind of knob: it costs no UI, it is discovered by playing,
    // and it makes the cast itself a decision instead of a formality. A long cast
    // is also a longer fight, because the fight starts at the real distance --
    // so distance buys you better fish AND charges you for them.

    /// Casts at or below this are "right in front of you".
    public const float ShortCast = 5f;
    /// Casts at or beyond this get the full long-cast bonus.
    public const float LongCast  = 16f;

    /// <summary>0 for a cast at your feet, 1 for a full-length one.</summary>
    public static float CastFactor(float castDistance)
        => Ramp(castDistance, ShortCast, LongCast);

    /// <summary>
    /// Tier weight shift from the cast. At a short cast this is NEGATIVE -- a
    /// lob at your feet really does catch worse fish -- and it never closes a
    /// tier off, same rule as bait.
    /// </summary>
    public static void ApplyCastShift(float castDistance,
                                      ref float common, ref float uncommon, ref float rare)
    {
        float f = CastFactor(castDistance);
        // -6 .. +12 on rare, half of that on uncommon.
        float rareShift = Lerp(-6f, 12f, f);
        float uncShift  = rareShift * 0.5f;

        if (rareShift >= 0f)
        {
            float take = rareShift + uncShift;
            if (take > common) take = common;
            float scale = (rareShift + uncShift) > 0.0001f ? take / (rareShift + uncShift) : 0f;
            rareShift *= scale;
            uncShift  *= scale;
        }
        else
        {
            // Never drain a tier to nothing: a short cast can still, rarely,
            // turn up something good.
            float maxRare = rare * 0.8f;
            float maxUnc  = uncommon * 0.8f;
            if (-rareShift > maxRare) rareShift = -maxRare;
            if (-uncShift  > maxUnc)  uncShift  = -maxUnc;
        }

        rare     += rareShift;
        uncommon += uncShift;
        common   -= (rareShift + uncShift);
        if (common < 0f) common = 0f;
        if (rare < 0f) rare = 0f;
        if (uncommon < 0f) uncommon = 0f;
    }

    /// <summary>
    /// Tier roll, including the cast-distance shift. <paramref name="rand01"/>
    /// is a uniform [0,1).
    /// </summary>
    public static FishTier RollTier(float dot, BaitKind bait, float castDistance, float rand01)
    {
        float c, u, r;
        TierWeights(dot, bait, out c, out u, out r);
        ApplyCastShift(castDistance, ref c, ref u, ref r);
        float total = c + u + r;
        if (total <= 0f) return FishTier.Common;
        float pick = rand01 * total;
        if (pick < c) return FishTier.Common;
        if (pick < c + u) return FishTier.Uncommon;
        return FishTier.Rare;
    }

    /// <summary>Tier roll with no cast bias — kept for the bait/light tests.</summary>
    public static FishTier RollTier(float dot, BaitKind bait, float rand01)
        => RollTier(dot, bait, Lerp(ShortCast, LongCast, 0.5f), rand01);

    /// <summary>Uniform species roll inside a tier. Returns an index into Species.</summary>
    public static int RollSpeciesInTier(FishTier tier, float rand01)
    {
        int first = -1, count = 0;
        for (int i = 0; i < Species.Length; i++)
        {
            if (Species[i].tier != tier) continue;
            if (first < 0) first = i;
            count++;
        }
        if (first < 0) return 0;
        int k = (int)(rand01 * count);
        if (k >= count) k = count - 1;
        if (k < 0) k = 0;
        return first + k;
    }

    public static int IndexOfId(string id)
    {
        for (int i = 0; i < Species.Length; i++)
            if (Species[i].id == id) return i;
        return -1;
    }

    /// <summary>
    /// Old saves carry only a tier string ("Common"/"Uncommon"/"Rare") with no
    /// species. They load as species 0 of that tier, per [INTEGRATE].
    /// </summary>
    public static int MigrateLegacyTier(string legacyTier)
    {
        FishTier t = FishTier.Common;
        if (legacyTier == "Rare") t = FishTier.Rare;
        else if (legacyTier == "Uncommon") t = FishTier.Uncommon;
        return RollSpeciesInTier(t, 0f);
    }

    // ── Weight and price ─────────────────────────────────────────────────────

    /// <summary>
    /// Weight inside the species' own range, biased low so a table-topper stays
    /// an event. Mirrors the old GenerateFishWeight's power curve.
    /// </summary>
    public static float RollWeight(int speciesIndex, float rand01)
        => RollWeight(speciesIndex, rand01, Lerp(ShortCast, LongCast, 0.5f));

    /// <summary>
    /// How bait bends the WEIGHT curve (Sam, 2026-09-02: "using bait gets your
    /// odds better for catching uncommon and rares and catching bigger ones as
    /// well"). Multiplies the power-curve exponent: below 1 skews heavy, above
    /// 1 skews light -- so good bait pulls bigger fish of whatever bites, and a
    /// bare hook runs slightly small.
    /// </summary>
    public static float BaitWeightFactor(BaitKind bait)
    {
        switch (bait)
        {
            case BaitKind.Voidmaggots: return 0.78f;
            case BaitKind.Glowworms:   return 0.90f;
            case BaitKind.Grubs:       return 1.0f;
            default:                   return 1.15f;
        }
    }

    /// <summary>
    /// Weight roll, with LONGER CASTS SKEWING HEAVIER. The exponent on the
    /// power curve is what does it: above 1 crushes the roll toward the light
    /// end, below 1 pushes it toward the heavy end. So the same species really
    /// is bigger out in the deep water.
    /// </summary>
    public static float RollWeight(int speciesIndex, float rand01, float castDistance)
        => RollWeight(speciesIndex, rand01, castDistance, BaitKind.Grubs);

    /// <summary>Weight roll with the bait's size bonus folded in.</summary>
    public static float RollWeight(int speciesIndex, float rand01, float castDistance,
                                   BaitKind bait)
    {
        var s = Species[speciesIndex];
        float exponent = Lerp(2.2f, 0.9f, CastFactor(castDistance)) * BaitWeightFactor(bait);
        float t = (float)Math.Pow(rand01, exponent);
        return s.weightMin + (s.weightMax - s.weightMin) * t;
    }

    /// <summary>Sale price = pricePerLb x weight, rounded, minimum $1.</summary>
    public static int PriceOf(int speciesIndex, float weightLb)
    {
        float v = Species[speciesIndex].pricePerLb * weightLb;
        int r = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return r < 1 ? 1 : r;
    }

    /// <summary>
    /// Stamina for this catch: the tier's range, positioned by where the weight
    /// falls inside the species' own weight range. Heavier = longer fight.
    /// </summary>
    public static float StaminaFor(int speciesIndex, float weightLb)
    {
        var s = Species[speciesIndex];
        float span = s.weightMax - s.weightMin;
        float f = span > 0.0001f ? (weightLb - s.weightMin) / span : 0f;
        if (f < 0f) f = 0f; else if (f > 1f) f = 1f;
        return Lerp(s.staminaMin, s.staminaMax, f);
    }

    // ── Small helpers (no UnityEngine.Mathf here by design) ───────────────────

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// 0 below lo, 1 above hi, linear between. Continuous by construction.
    static float Ramp(float v, float lo, float hi)
    {
        if (hi - lo < 0.000001f) return v >= hi ? 1f : 0f;
        float t = (v - lo) / (hi - lo);
        if (t < 0f) return 0f;
        if (t > 1f) return 1f;
        return t;
    }
}
