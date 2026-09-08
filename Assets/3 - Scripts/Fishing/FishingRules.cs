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
    /// A BOUNTY row (2026-09-03): never comes up in the ordinary tier roll;
    /// only a BountyZone can produce it. Shares the tier's model, scaled by its
    /// own (huge) weight range through the one size law.
    public bool bounty;
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

        // -- Bounty fish ------------------------------------------------
        // GRULABU (docs/Handoff_BountyQuest_Grulabu_v1.md): the thing Floorbin saw
        // north up the lake. Rare model, scaled by weight (140-260 lb -> ~1.8-2.2 m,
        // girth clamped) and tinted blood-red. Only BountyZone water rolls it.
        // 2.4 $/lb x ~200 lb is about the handoff's $500 bounty at the fish market.
        Bt("grulabu",    "GRULABU",    FishTier.Rare,     0, 0xC8, 0x2A, 0x2A, 140f, 260f, 2.4f, 24f, 34f),

        // -- Planet economy, 2026-09-07: the pool grows 12 -> 24 --------------
        // Four more per tier so every fishable planet can have its own table
        // (2 per tier on the four mains, 1 per tier on the six dwarfs = 42
        // slots) with each species on at most two worlds and six on exactly
        // one. Names are placeholders off the handoff's list -- Sam renames in
        // place. APPENDED, never inserted: rows are referenced by id string in
        // saves and by index only at runtime, and the tier rolls below no
        // longer assume a tier's rows sit together.
        Sp("grubbler",   "Grubbler",   FishTier.Common,   0, 0x8A, 0x6E, 0x3C,  1f,  7f, 1.0f),
        Sp("skrout",     "Skrout",     FishTier.Common,   0, 0x5E, 0x8C, 0x5A,  1f,  6f, 1.1f),
        Sp("purchlet",   "Purchlet",   FishTier.Common,   0, 0xC9, 0x9A, 0x5E,  1f,  4f, 1.2f),
        Sp("wallop",     "Wallop",     FishTier.Common,   0, 0x7A, 0x7A, 0xA8,  2f,  8f, 1.0f),

        Sp("murkfin",    "Murkfin",    FishTier.Uncommon, 0, 0x3F, 0x4A, 0x3A,  6f, 20f, 1.5f),
        Sp("flubb",      "Flubb",      FishTier.Uncommon, 0, 0xC4, 0x7A, 0xB8,  5f, 14f, 1.7f),
        Sp("knurl",      "Knurl",      FishTier.Uncommon, 0, 0x9A, 0x5C, 0x2E,  9f, 24f, 1.5f),
        Sp("tarnish",    "Tarnish",    FishTier.Uncommon, 0, 0x6E, 0x6A, 0x50,  7f, 18f, 1.6f),

        Sp("zibbet",     "Zibbet",     FishTier.Rare,     0, 0xE0, 0xC0, 0x30, 18f, 44f, 2.2f),
        Sp("ossum",      "Ossum",      FishTier.Rare,     0, 0xD8, 0xD8, 0xE8, 22f, 50f, 2.0f),
        Sp("snagg",      "Snagg",      FishTier.Rare,     0, 0x2E, 0x8A, 0x6A, 15f, 40f, 2.3f),
        Sp("vorm",       "Vorm",       FishTier.Rare,     0, 0x6A, 0x2E, 0x8A, 20f, 48f, 2.1f),
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

    static FishSpecies Bt(string id, string name, FishTier tier, int model,
                          byte r, byte g, byte b, float wMin, float wMax, float perLb,
                          float sMin, float sMax)
    {
        var s = Sp(id, name, tier, model, r, g, b, wMin, wMax, perLb);
        s.bounty = true;
        s.staminaMin = sMin;   // ~3x a rare: the longest fight in the game
        s.staminaMax = sMax;
        return s;
    }

    public static bool IsBounty(int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < Species.Length && Species[speciesIndex].bounty;

    // ── Fight tuning ─────────────────────────────────────────────────────────
    // Handoff defaults. Mirrored onto FishingTuning (a ScriptableObject) so Sam
    // can retune without a recompile; these are the fallbacks when no asset is
    // assigned, and the numbers the headless tests run against.

    // Tension gained per second of holding, x the tier's pull.
    //
    // 35 -> 48 (2026-09-01) -> 96 (2026-09-08, with the doubled reel) -> 44.
    //
    // 96 was arithmetic nobody could survive. Reeling into a run costs
    // ReelRate x basePull x RunPullMultiplier x RunTensionScale; at 96 that was
    // 96 x 1.8 x 2 x 1 = 345 a second on a rare, so the bar went from empty to
    // snapped in 0.29 SECONDS. Sam, playtesting: "i would be reeling and as soon
    // as they start fighting and tugging it would snap off ... it was almost
    // impossible to catch a rare and they would break off the first time they
    // started running." That was not bad luck, it was a number.
    //
    // 44 is picked so that steady reeling fills the bar in about 2.5s on a rare
    // and reeling straight into a push takes about 1.2s -- long enough to see it
    // coming and let go, which is the entire skill of the fight. Holding the
    // button still loses: the pushes are frequent enough now that their spikes
    // stack faster than the bar can be reeled away.
    public const float ReelRate  = 80f;
    // Shed per second while released. Sheds a full bar in ~1.8s against a ~2.5s
    // fill, so pump-and-release is a rhythm you can actually keep rather than a
    // race you lose.
    public const float RelaxRate = 95f;
    public const float DrainRate = 1f;    // stamina spent per second of holding or running
    public const float TensionMax = 100f;

    // ── Distance model (2026-09-01 rewrite) ──────────────────────────────────
    // The fight is won by bringing the fish IN, not by draining a hidden bar.

    /// Metres per second the reel gains on a fish that isn't resisting.
    /// 4.5 -> 6.5 on 2026-09-01, then 6.5 -> 13 on 2026-09-08 (Sam: "make the
    /// reel in speed 2x faster for all reeling ... and in return make fish
    /// fight harder and faster so it makes fights more back and forth").
    ///
    /// This number alone would have halved every fight, so it does not travel
    /// alone: ResistFor, RunSpeedForTier and RunIntervalForTier all rose with
    /// it. The fight is the SAME LENGTH and twice as violent -- you gain ground
    /// visibly fast, and a run takes it visibly fast back. Bobber.retrieveSpeed
    /// and Bobber.waterRetrieveSpeed doubled to match, so winding an empty lure
    /// home over land or across the water moved with it.
    public const float ReelSpeed = 13f;
    /// <summary>
    /// Land the fish once it is this close. Measured against the bobber's REAL
    /// position, not a running total — see FishFightSim.SyncDistance.
    ///
    /// Cut 2 -> 1.2 on 2026-09-08 with the charged cast. Two metres was a
    /// rounding error against a twelve metre throw and a sixth of a short one;
    /// worse, the first draft of the charge had a tap landing at 1.5 m, INSIDE
    /// the landing radius, so a tapped cast would have booked the fish the
    /// instant it bit.
    /// </summary>
    public const float LandDistance = 1.2f;
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
        // Raised twice on 2026-09-08: once for the doubled ReelSpeed, and again
        // when the cast became a charge that tops out at 10 m.
        //
        // A fight lasts, near enough, (cast distance) / (net reel speed). The
        // cast roughly halved, so every fight halved with it — a rare was landed
        // in two seconds and a common in under one, which is not a fight, it is
        // a formality. The reel stays fast because that is what Sam asked for;
        // what gives is how much of it the fish takes back.
        //
        // There is a hard ceiling here and it is worth writing down: if a fish
        // resists so hard that the reel gains less ground than its pushes take,
        // the fight NEVER ENDS. Average push loss is about 1.2 m/s on a rare, and
        // you can only hold the reel for maybe 60% of a fight, so anything past
        // about 0.84 is a stalemate. 0.78 leaves real margin.
        // 0.50/0.66/0.74/0.82 -> 0.66/0.80/0.88/0.90 -> 0.70/0.85/0.93/0.95.
        //
        // At 13 m/s the reel crosses a whole typical cast in under a second, so a
        // fight only exists at all if the fish holds against it. These numbers are
        // what produce the exchange Sam asked for — measured per reel-window
        // against per run, on a heavy fish:
        //
        //   Rare      a run takes 7.7 m, a reel window gains 3.9 m  -> it WINS
        //   Uncommon  a run takes 5.4 m, a reel window gains 7.2 m  -> it dents
        //   Common    a run takes 4.5 m, a reel window gains 15.8 m -> never
        //
        // "a rare will gain back much more ground than you reeled it in,
        //  uncommons will do the same but gain back less, commons will never".
        //
        // The old stalemate worry (past ~0.90 the reel gains less than the runs
        // take, and the fight never ends) is gone: RunTiredDurationFloor makes
        // the runs fade toward nothing, so the reel always wins eventually.
        float max = s.bounty ? 0.95f
                  : s.tier == FishTier.Rare ? 0.93f
                  : s.tier == FishTier.Uncommon ? 0.85f
                  : 0.70f;
        // The light-fish floor, 0.45 -> 0.58 -> 0.80. Resist is now the main
        // thing standing between the reel and the fish, so a light fish resisting
        // half of what a heavy one does was a light fish that barely fought at
        // all. Weight still tells, through stamina (how long it keeps running)
        // and through the last fifth of this range — it just no longer decides
        // whether there is a fight.
        return max * (0.80f + 0.20f * f);
    }

    /// <summary>
    /// Metres per second a fish takes back while it is pushing.
    ///
    /// Softened 2026-09-08 (rare 6.8 -> 4.6, uncommon 4.6 -> 3.2) and COMMONS
    /// NOW PUSH TOO, at 2.0. Sam: "commons dont fight at all and literally are
    /// just free to catch which is wrong, they should still fight but just be
    /// easier to catch ... make the fishes pushes less powerful and more short,
    /// but make them happen more often and make them happen for commons."
    ///
    /// Ground taken per push is speed x duration, and the duration more than
    /// halved at the same time — so a single push now costs you around two
    /// metres instead of ten. You lose the same water over a fight; you lose it
    /// in a dozen small shoves you can read instead of two big ones you cannot.
    /// </summary>
    public static float RunSpeedForTier(FishTier tier)
    {
        switch (tier)
        {
            case FishTier.Rare:     return 5.5f;
            case FishTier.Uncommon: return 3.9f;
            default:                return 3.2f;
        }
    }

    // ── Rod / line load curve ────────────────────────────────────────────────

    /// <summary>
    /// Maps raw load 0-1 onto how far the rod is actually bent.
    ///
    /// <b>THE ROD IS THE BAR</b> (Sam, 2026-09-08: "the rod needs to be the same
    /// as the status bar, because eventually id like to remove the status bar and
    /// have it feeling so good you can just judge it from the rod"). Load is now a
    /// near-straight readout of tension (see FishFightSim.RodLoad), so this curve
    /// exists only to keep the top of the range dramatic — it must NOT be the
    /// thing that decides what the rod is telling you.
    ///
    /// So it is now a gentle lean, not a cliff. It used to square the
    /// above-knee term, which meant most of the visible travel happened in the
    /// last fifth of the range: "the rod bending sometimes goes from a little
    /// bent to fully bent very fast ... we just need to make it not like that, so
    /// that tension builds with the rod and you can see when it starts bending
    /// too much and actually have a chance to stop reeling and relieve the
    /// tension." An exponent of 1.5 keeps a deep bow meaningful while leaving
    /// real, readable travel through the middle where the decisions are made.
    /// </summary>
    public const float BendKnee = 0.45f;     // where the curve leans up
    /// How bent the rod is AT the knee, as a fraction of the maximum. At 0.45/0.5
    /// the curve passes almost exactly through the diagonal, so "half bent" means
    /// "half way to snapping" — which is the whole point of the rod replacing the
    /// bar.
    public const float BendAtKnee = 0.5f;

    /// Shape of the curve ABOVE the knee. 1 = perfectly linear, 2 = the old
    /// cliff. 1.5 leans into the last third without hiding the middle.
    public const float BendAboveKnee = 1.5f;

    // ── What loads the rod ───────────────────────────────────────────────────

    /// <summary>
    /// Bend you get from simply having a fish on a tight line, before any
    /// tension at all — its weight in the water. The rest of the range is
    /// tension, so the rod reads as: a little bent = something is on, half bent
    /// = half way to snapping, fully bowed = let go NOW.
    /// </summary>
    public const float RodRestingLoad = 0.18f;

    /// <summary>
    /// How tight a hooked fish holds the line ON ITS OWN, with you doing
    /// nothing. Sam: "when fighting the fish the line should stay tight the
    /// entire time, unless you stop reeling AND the fish stops fighting."
    /// Before this the line headed for fully slack the instant you released,
    /// which is why it "gets droopy when it shouldn't" — a fish on the end is
    /// still a fish on the end.
    /// </summary>
    public const float FishHoldTaut = 0.85f;

    /// <summary>
    /// Seconds of you doing nothing before the fish gives up holding the line
    /// and it goes properly slack — which is also the window in which it works
    /// the hook loose (see SlackEscapeSeconds). Scaled by how much fight it has
    /// left, so a spent fish lets go at once.
    /// </summary>
    public const float FishHoldFadeSeconds = 1f;

    public static float BendCurve(float load01) => BendCurve(load01, BendKnee, BendAtKnee);

    public static float BendCurve(float load01, float knee, float atKnee)
    {
        if (load01 <= 0f) return 0f;
        if (load01 >= 1f) return 1f;
        if (knee <= 0.001f) return load01;
        if (load01 <= knee)
            return atKnee * (load01 / knee);
        float t = (load01 - knee) / (1f - knee);
        return atKnee + (1f - atKnee) * (float)Math.Pow(t, BendAboveKnee);
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

    /// Applied while reeling a fish that is NOT running. 0.5 -> 0.38: with runs
    /// interrupting so often, steady reeling had to get cheaper or there was
    /// never a window long enough to make progress in.
    public const float SteadyTensionScale = 0.38f;
    /// <summary>
    /// Applied while reeling a fish that IS running, at the top of its wind-up.
    /// 1.0 -> 0.6 — "their runs should add less tension to the rod so that we can
    /// have more frequent runs".
    ///
    /// It is still ~2.5x the steady rate once RunPullMultiplier is counted, which
    /// is the number that matters: reeling into a run fills the bar in about
    /// 0.9s while a run lasts 1-2s, so <b>reeling through a whole run snaps you
    /// and letting go promptly does not</b>. That is the mechanic Sam described:
    /// "you have to stop reeling or else the tension will break the rod".
    /// </summary>
    public const float RunTensionScale = 0.6f;

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
            // Trimmed on 2026-09-01 when runs got faster, and AGAIN on
            // 2026-09-08 for the same reason. Stamina is seconds of surging, so
            // faster and more frequent runs mean the same stamina buys the fish
            // MORE ground -- the rare median went 9.9s -> 11.5s on the doubled
            // reel alone, which is the exact opposite of what asking for a
            // faster reel is meant to achieve. Cutting it back puts the fight
            // length where it was and leaves the extra violence in place.
            // Roughly tripled on 2026-09-08. Stamina is spent both by running
            // AND by being reeled against, so at the old numbers a rare had about
            // two runs in it and a common had one — "they tire out too fast, and
            // then you can just reel them in without them fighting back". A rare
            // now has eight runs in it, an uncommon three, a common two.
            //
            // This is the dial for HOW LONG a fight is. Paired with
            // RunTiredDurationFloor it is also the shape of it: the first half is
            // a standoff and the second half is the fish handing itself over.
            case FishTier.Rare:     min = 15f;  max = 23.5f; break;
            case FishTier.Uncommon: min = 7.8f; max = 11.8f; break;
            default:                min = 5f;   max = 7f;    break;
        }
    }

    /// <summary>
    /// EVERY tier pushes now, commons included (2026-09-08). A common that never
    /// fought was, in Sam's words, "literally free to catch, which is wrong" —
    /// it just pushes weakly and runs out of fight quickly, which is what makes
    /// it the easy fish rather than the empty one.
    /// </summary>
    public static bool TierRuns(FishTier tier) => true;

    /// <summary>
    /// Seconds between pushes. Tightened hard on 2026-09-08 — a fight should be
    /// a constant argument, not two dramatic interruptions in a long haul.
    /// Rarer fish argue more often.
    /// </summary>
    /// <summary>
    /// Seconds between runs. Longer than the 0.8-2.4s of the previous pass, and
    /// that is deliberate even though the ask was "more frequent": a run now
    /// lasts 1-2s instead of half a second, so with the old gaps the fish spent
    /// 60% of the fight running and the player spent the fight watching. These
    /// gaps put the fish on the move about 40% of the time — enough that you are
    /// constantly losing and re-winning ground, with real reeling windows in
    /// between where the bar can actually become dangerous.
    /// </summary>
    public static void RunIntervalForTier(FishTier tier, out float min, out float max)
    {
        switch (tier)
        {
            case FishTier.Rare:     min = 1.25f; max = 2.1f; break;
            case FishTier.Uncommon: min = 1.6f;  max = 2.7f; break;
            default:                min = 1.5f;  max = 2.6f; break;
        }
    }

    /// <summary>
    /// How long one run lasts. 1-2s -> 0.35-0.7s -> back to 1-2s, and the round
    /// trip is the lesson: SHORT runs and CHEAP runs are different knobs, and
    /// only the second one was ever the problem.
    ///
    /// Shortening them (to stop rares snapping you) also deleted the fight —
    /// nothing took any ground back, so a 13 m/s reel simply hauled everything
    /// in. Sam: "its actually too easy to reel a fish in ... i want them to run
    /// and pull line and for you to lose ground and wait for them to stop, then
    /// try to gain it back." So the runs are long again, and what got cut
    /// instead is <see cref="RunTensionScale"/> — the thing that was actually
    /// hurting.
    /// </summary>
    public const float RunDurationMin = 1f;
    public const float RunDurationMax = 2f;

    /// How much harder the fish pulls at the top of a push. Was 2.
    public const float RunPullMultiplier = 1.6f;

    /// <summary>
    /// Seconds a push takes to come on FULL STRENGTH. The single most important
    /// number in the fight, and it did not exist before 2026-09-08.
    ///
    /// A push used to switch on between one frame and the next: full pull, full
    /// tension rate, instantly. Sam: "i would be reeling and as soon as they
    /// start fighting and tugging it would snap off ... they would break off the
    /// first time they started running." Nothing about that is reflexes — the bar
    /// went from 70% to snapped in under two tenths of a second, which is roughly
    /// how long it takes to notice anything at all.
    ///
    /// Softening the push instead was tried and is worse: it makes it survivable
    /// for a person AND ignorable for someone who just holds the button, and the
    /// headless bot went from losing every good fish to landing 47% of them.
    /// Strength cannot separate skill from stubbornness, because both eat the
    /// same spike. TIME can. Over a quarter second you can see the rod start to
    /// go, decide, and let go having taken a fraction of it; hold on and you take
    /// all of it, every time.
    /// </summary>
    public const float RunRampSeconds = 0.2f;

    /// <summary>
    /// How long a run lasts when the fish is exhausted, as a fraction of a fresh
    /// one. THE ENDGAME OF EVERY FIGHT.
    ///
    /// Sam, 2026-09-08: "instead of the fishes losing their energy after 1-3 runs
    /// and just not fighting and getting reeled in it should be a slower fight for
    /// gaining ground, then the longer the fight the less long they will run for
    /// when they run making it easier to get them reeled in."
    ///
    /// Before this, stamina was a CLIFF: full-length runs right up to the moment
    /// it hit zero, then no runs at all and a free haul to the bank. Now the runs
    /// taper — a fresh rare bolts for two seconds, a beaten one manages a third of
    /// that — so the fight hands itself over instead of switching off.
    ///
    /// It also quietly removed the stalemate ceiling that used to cap
    /// <see cref="ResistFor"/> near 0.90: the fish's runs are guaranteed to fade
    /// toward nothing, so the reel always wins in the end however hard it holds.
    /// That is what let resist go to 0.93 on a rare, which is where the long
    /// mid-fight standoff comes from.
    /// </summary>
    public const float RunTiredDurationFloor = 0.15f;

    /// <summary>How far into its wind-up a push is, 0-1, after
    /// <paramref name="secondsIntoRun"/>. Smoothstepped so it eases in rather
    /// than arriving on a corner.</summary>
    public static float RunRamp(float secondsIntoRun)
    {
        // Divide-guarded without a branch: RunRampSeconds is a const, so an
        // `if (RunRampSeconds <= 0)` early-out folds away and the compiler
        // (correctly) calls the line after it unreachable. Warning baseline
        // here is ZERO, so that matters.
        float x = secondsIntoRun / Math.Max(0.0001f, RunRampSeconds);
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// Seconds before a freshly hooked fish makes its FIRST run — the bolt.
    ///
    /// Added 2026-09-08. Runs used to be scheduled on the ordinary interval
    /// from the moment of the hook, which meant the first one was due in
    /// 1.4-2.8s on an uncommon... and the median uncommon fight lasts about
    /// two seconds. The headless sim measured the result plainly: **0.0 metres
    /// of ground taken back** at the median. The whole mid tier — the fish you
    /// catch most of — never fought at all. It was a haul with a bar on it.
    ///
    /// A real fish bolts the instant it feels the hook, so now so does this
    /// one. Short enough that every uncommon gets at least one run, long enough
    /// that the line has come tight and the player has registered the bite
    /// before it happens (lineTautSeconds is ~0.4s).
    /// </summary>
    public static void FirstRunDelayForTier(FishTier tier, out float min, out float max)
    {
        // Nudged out on 2026-09-08. At 0.3s a rare bolted before the player had
        // finished registering the bite, so the first push landed on someone who
        // was still reeling — and with the old numbers that was the whole fight
        // over. You get a moment to settle now, then it goes.
        switch (tier)
        {
            case FishTier.Rare:     min = 0.5f; max = 0.9f; break;
            case FishTier.Uncommon: min = 0.5f; max = 1.0f; break;
            // Commons are the SHORT fight, so their first push has to come early
            // or the fish is landed before it ever shoves — the exact bug the
            // bolt was invented to fix, one tier down.
            default:                min = 0.4f; max = 0.8f; break;
        }
    }

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

    // ── The charged cast (2026-09-08) ────────────────────────────────────
    //
    // Sam: "make casting cast less far, then make it so that clicking to cast
    // does a small tiny cast, but you can hold the cast button for up to 2
    // seconds to charge your cast, and it will make it cast further, or if you
    // only hold the cast for a second it will be between the far cast and short
    // cast."
    //
    // The distances live HERE rather than on the rod, for one reason: the tier
    // shift below is scored against them, so they have to be the same numbers or
    // the odds are keyed to a cast nobody can actually make. That is exactly what
    // went wrong when the ramp said 5..16 m and the rod could throw 12.

    /// <summary>
    /// Metres a TAP puts the bobber out — a plop just off the bank.
    ///
    /// Not smaller than this, and the reason is LandDistance: a fish counts as
    /// landed once it is that close, so a cast shorter than the landing radius
    /// would be over before it began. Three metres leaves a short but real
    /// fight, which is what a tap is meant to buy.
    /// </summary>
    public const float TapCastDistance = 3f;
    /// <summary>
    /// Metres a FULL two-second charge reaches.
    ///
    /// <b>This is about where a single click used to land, and that is
    /// deliberate</b> — worth reading before "helpfully" shortening it. Sam asked
    /// for casting to go less far, and it does: the ordinary cast, the click, went
    /// from about twelve metres to three. What did not shrink is the ceiling,
    /// because <b>the cast distance IS the length of the fight</b> — the fish
    /// starts there and the fight is over when it arrives. Capping the charge at
    /// 8.5 m was tried first and a rare fought for two seconds; every other dial
    /// (resist, push strength, push frequency) was pushed to its limit trying to
    /// buy that time back, and the arithmetic simply does not exist: a 13 m/s reel
    /// crossing six metres of water cannot take eight seconds.
    ///
    /// So the charge buys the water, and the water is the fight. Tap for a quick
    /// tiddler; hold the full two seconds when you want a real one.
    /// </summary>
    public const float FullCastDistance = 13f;
    /// Seconds of holding to go from a tap to a full cast.
    public const float CastChargeSeconds = 2f;

    /// <summary>Where a charge held <paramref name="charge01"/> of the way lands.
    /// Linear in DISTANCE, not in launch speed — "if you only hold the cast for a
    /// second it will be between the far cast and short cast" means halfway along
    /// the water, not halfway up the speed curve.</summary>
    public static float CastDistanceFor(float charge01)
        => Lerp(TapCastDistance, FullCastDistance, charge01 < 0f ? 0f : (charge01 > 1f ? 1f : charge01));

    /// Casts at or below this are "right in front of you" — i.e. a tap.
    public const float ShortCast = TapCastDistance;
    /// Casts at or beyond this get the full long-cast bonus — i.e. a full charge.
    public const float LongCast  = FullCastDistance;

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
        // -6 .. +5.5 on rare, half of that on uncommon.
        //
        // The top used to be +12, but it was scored against a 16 m bookend that
        // the rod could not reach: a typical 12 m cast scored 0.64 and collected
        // about +5.4, and the full +12 was theoretical. Now that a full charge
        // really is the top of the ramp, leaving it at +12 would have quietly
        // made every charged cast far richer than the game has ever been. +5.5
        // hands the same odds a good cast always gave, and hands them to the
        // player for holding the button instead of for standing somewhere.
        float rareShift = Lerp(-6f, 5.5f, f);
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

    /// <summary>Uniform species roll inside a tier. Returns an index into Species.
    /// Bounty rows are skipped: they are table entries (save/price/size) but
    /// never ordinary catches. Rows of a tier are NOT assumed to sit together --
    /// the 2026-09-07 species were appended after GRULABU, so a tier's rollable
    /// rows come in two runs.</summary>
    public static int RollSpeciesInTier(FishTier tier, float rand01)
        => RollSpeciesInTier(tier, rand01, null);

    /// <summary>
    /// Species roll inside a tier, restricted to <paramref name="allowed"/> (the
    /// planet's own table, as Species indices). Null or empty falls back to the
    /// whole pool, so a world with no table fishes exactly as it did before the
    /// planet economy.
    /// </summary>
    public static int RollSpeciesInTier(FishTier tier, float rand01, System.Collections.Generic.IList<int> allowed)
    {
        _rollScratch.Clear();
        if (allowed != null)
            for (int a = 0; a < allowed.Count; a++)
            {
                int i = allowed[a];
                if (i < 0 || i >= Species.Length) continue;
                if (Species[i].tier != tier || Species[i].bounty) continue;
                _rollScratch.Add(i);
            }
        if (_rollScratch.Count == 0)
            for (int i = 0; i < Species.Length; i++)
                if (Species[i].tier == tier && !Species[i].bounty) _rollScratch.Add(i);
        if (_rollScratch.Count == 0) return 0;

        int k = (int)(rand01 * _rollScratch.Count);
        if (k >= _rollScratch.Count) k = _rollScratch.Count - 1;
        if (k < 0) k = 0;
        return _rollScratch[k];
    }

    // Reused by every roll so a bite never allocates.
    static readonly System.Collections.Generic.List<int> _rollScratch = new System.Collections.Generic.List<int>(16);

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
