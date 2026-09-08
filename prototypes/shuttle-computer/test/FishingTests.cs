// [TEST] 1-5 of docs/Handoff_FishingRevamp_Phase1_v1.md, run headlessly.
//
//   python prototypes/shuttle-computer/test/verify-fishing.py
//
// Fishing is a money loop, and every economy bug this project has shipped came
// from two places computing the same number differently. The tier roll, the
// bite-rate curve and the fight are all pure functions in FishingRules /
// FishFightSim precisely so they can be executed here rather than play-tested
// by hand across 12 species x 50 weights.

using System;
using System.Collections.Generic;

public static class FishingTests
{
    public static int Checks;
    public static int Failures;

    static void Check(bool cond, string what)
    {
        Checks++;
        if (cond) return;
        Failures++;
        Console.WriteLine("  FAIL  " + what);
    }

    const float Dt = 1f / 60f;

    // ── [TEST] 1 — the fight ─────────────────────────────────────────────────
    // A "perfect" bot (releases during runs, and whenever the bar is high) must
    // ALWAYS land. A "hold forever" bot must snap on every uncommon/rare and
    // land only commons.

    // A representative cast: half a charge, which is what an ordinary throw is
    // now that casting is a hold. Derived from the rules rather than typed — the
    // old "12 m, a typical one for the current shoot speed" stopped being either
    // typical or reachable the moment the cast became a charge.
    static readonly float CastDistance = FishingRules.CastDistanceFor(0.5f);

    static FightOutcome RunBot(FishTier tier, float stamina, float resist, uint seed,
                               bool holdForever, out float wallClock, out float maxOut)
    {
        float lost;
        return RunBot(tier, stamina, resist, seed, holdForever, out wallClock, out maxOut, out lost);
    }

    /// <paramref name="metresLost"/> is every metre the fish took BACK during the
    /// fight, added up. Against the cast distance it is the honest measure of how
    /// much of a tug of war the fight was: 0 means the reel simply hauled the fish
    /// in, and a number near the cast means you had to win the same water twice.
    /// Fight LENGTH cannot see this -- a slow steady creep and a violent
    /// back-and-forth can take exactly the same number of seconds.
    static FightOutcome RunBot(FishTier tier, float stamina, float resist, uint seed,
                               bool holdForever, out float wallClock, out float maxOut,
                               out float metresLost)
    {
        var f = new FishFightSim(tier, stamina, CastDistance, resist, seed);
        wallClock = 0f;
        maxOut = CastDistance;
        metresLost = 0f;
        float prev = f.Distance;
        for (int i = 0; i < 60 * 600; i++)   // 10 minute guard
        {
            bool holding = holdForever
                ? true
                // The skilled read: never reel into a run, and back off before
                // the bar gets dangerous.
                : (!f.IsRunning && f.TensionFraction < 0.70f);
            var outcome = f.Step(Dt, holding);
            wallClock = f.Elapsed;
            if (f.Distance > prev) metresLost += f.Distance - prev;
            prev = f.Distance;
            if (f.Distance > maxOut) maxOut = f.Distance;
            if (outcome != FightOutcome.Fighting) return outcome;
        }
        return FightOutcome.Fighting;   // timed out — a failure in itself
    }

    /// <summary>
    /// A PLAYER, not an oracle.
    ///
    /// The bots above read IsRunning and TensionFraction in the frame they
    /// change and act on them in that same frame. A person sees the rod move,
    /// decides, and lets go about a fifth of a second later. That gap is where
    /// Sam's playtest lived — "i would be reeling and as soon as they start
    /// fighting and tugging it would snap off ... it was almost impossible to
    /// catch a rare" — and a zero-latency bot cannot see it at all, which is why
    /// every check passed while the game was unplayable.
    ///
    /// Inputs are queued and played back <paramref name="reactionSeconds"/>
    /// late, so the fish gets that long to hurt you before your hand moves.
    /// </summary>
    static FightOutcome RunHumanBot(FishTier tier, float stamina, float resist, uint seed,
                                    float reactionSeconds, out float wallClock)
        => RunHumanBot(tier, stamina, resist, seed, reactionSeconds, 0.70f, CastDistance,
                       out wallClock);

    static FightOutcome RunHumanBot(FishTier tier, float stamina, float resist, uint seed,
                                    float reactionSeconds, float releaseAt, float cast,
                                    out float wallClock)
    {
        var f = new FishFightSim(tier, stamina, cast, resist, seed);
        int lag = (int)(reactionSeconds / Dt + 0.5f);
        if (lag < 1) lag = 1;
        var queued = new bool[lag];
        for (int i = 0; i < queued.Length; i++) queued[i] = true;   // starts reeling
        int qi = 0;
        wallClock = 0f;
        for (int i = 0; i < 60 * 600; i++)
        {
            bool holding = queued[qi];
            queued[qi] = !f.IsRunning && f.TensionFraction < releaseAt;  // decided NOW, acted on later
            qi = (qi + 1) % queued.Length;
            var outcome = f.Step(Dt, holding);
            wallClock = f.Elapsed;
            if (outcome != FightOutcome.Fighting) return outcome;
        }
        return FightOutcome.Fighting;
    }

    static void FightChecks()
    {
        Console.WriteLine("[TEST] 1  the fight (distance model)");

        var medians = new Dictionary<FishTier, List<float>>
        {
            { FishTier.Common,   new List<float>() },
            { FishTier.Uncommon, new List<float>() },
            { FishTier.Rare,     new List<float>() },
        };
        var lostM = new Dictionary<FishTier, List<float>>
        {
            { FishTier.Common,   new List<float>() },
            { FishTier.Uncommon, new List<float>() },
            { FishTier.Rare,     new List<float>() },
        };

        int perfectLandFails = 0, holdFailsCommon = 0, timeouts = 0, runOutBreaches = 0;

        for (int si = 0; si < FishingRules.Species.Length; si++)
        {
            var sp = FishingRules.Species[si];
            for (int w = 0; w < 50; w++)
            {
                float weight = sp.weightMin + (sp.weightMax - sp.weightMin) * (w / 49f);
                float stamina = FishingRules.StaminaFor(si, weight);
                float resist  = FishingRules.ResistFor(si, weight);
                uint seed = (uint)(si * 1000 + w + 1);

                float t, mo, lost;
                var perfect = RunBot(sp.tier, stamina, resist, seed, false, out t, out mo, out lost);
                if (perfect == FightOutcome.Fighting) timeouts++;
                if (perfect != FightOutcome.Landed) perfectLandFails++;
                else { medians[sp.tier].Add(t); lostM[sp.tier].Add(lost); }
                if (mo > CastDistance * FishingRules.MaxRunOutFactor + 0.01f) runOutBreaches++;

                float t2, mo2;
                var greedy = RunBot(sp.tier, stamina, resist, seed, true, out t2, out mo2);
                if (sp.tier == FishTier.Common && greedy != FightOutcome.Landed) holdFailsCommon++;
            }
        }

        // ── Can a HUMAN land these? (2026-09-08) ────────────────────────────
        {
            const float Reaction = 0.20f;      // a fair reflex on a visible cue
            var landed = new Dictionary<FishTier, int>();
            var tried  = new Dictionary<FishTier, int>();
            var snapped = new Dictionary<FishTier, int>();
            foreach (FishTier tt in new[] { FishTier.Common, FishTier.Uncommon, FishTier.Rare })
            { landed[tt] = 0; tried[tt] = 0; snapped[tt] = 0; }

            for (int si = 0; si < FishingRules.Species.Length; si++)
            {
                var sp = FishingRules.Species[si];
                for (int w = 0; w < 20; w++)
                {
                    float weight = sp.weightMin + (sp.weightMax - sp.weightMin) * (w / 19f);
                    float t3;
                    var o = RunHumanBot(sp.tier, FishingRules.StaminaFor(si, weight),
                                        FishingRules.ResistFor(si, weight),
                                        (uint)(si * 77 + w + 3), Reaction, out t3);
                    tried[sp.tier]++;
                    if (o == FightOutcome.Landed) landed[sp.tier]++;
                    else if (o == FightOutcome.Snapped) snapped[sp.tier]++;
                }
            }
            foreach (FishTier tt in new[] { FishTier.Common, FishTier.Uncommon, FishTier.Rare })
            {
                float rate = landed[tt] / (float)tried[tt];
                Console.WriteLine("    a player with a 0.20s reaction lands " + tt + ": "
                                  + (100f * rate).ToString("F0") + "%  (snapped "
                                  + (100f * snapped[tt] / tried[tt]).ToString("F0") + "%)");
            }
            // Sam's complaint, as a contract. A rare is meant to be the hard
            // fish, not a coin toss you lose on the first push.
            Check(landed[FishTier.Rare] / (float)tried[FishTier.Rare] > 0.6f,
                  "a player with ordinary reflexes lands most RARES");
            Check(landed[FishTier.Common] / (float)tried[FishTier.Common] > 0.9f,
                  "...and nearly every COMMON");

            // ── Is there any skill in it? ───────────────────────────────────
            // A careful player landing everything is only good news if a GREEDY
            // one does not. This is the same bot pushing its luck: it holds on
            // to 85% of the bar instead of 70%, and is slower off the mark.
            // If these two numbers are the same, the fight has no gradient and
            // "let go during a push" is not really a skill.
            int greedyLanded = 0, greedyTried = 0;
            for (int si = 0; si < FishingRules.Species.Length; si++)
            {
                var sp = FishingRules.Species[si];
                if (sp.tier != FishTier.Rare) continue;
                for (int w = 0; w < 20; w++)
                {
                    float weight = sp.weightMin + (sp.weightMax - sp.weightMin) * (w / 19f);
                    float t4;
                    var o = RunHumanBot(sp.tier, FishingRules.StaminaFor(si, weight),
                                        FishingRules.ResistFor(si, weight),
                                        (uint)(si * 91 + w + 5), 0.28f, 0.85f,
                                        CastDistance, out t4);
                    greedyTried++;
                    if (o == FightOutcome.Landed) greedyLanded++;
                }
            }
            Console.WriteLine("    a GREEDY player (holds to 85%, 0.28s reaction) lands Rare: "
                              + (100f * greedyLanded / greedyTried).ToString("F0") + "%");
            Check(greedyLanded / (float)greedyTried < 0.9f,
                  "greed still costs you rares — the fight has a skill gradient");

            // ── What does charging the cast buy? ────────────────────────────
            float tapLen = 0f, fullLen = 0f; int tapN = 0, fullN = 0;
            for (int si = 0; si < FishingRules.Species.Length; si++)
            {
                var sp = FishingRules.Species[si];
                if (sp.tier != FishTier.Rare) continue;
                for (int w = 0; w < 20; w++)
                {
                    float weight = sp.weightMin + (sp.weightMax - sp.weightMin) * (w / 19f);
                    float ta, fu;
                    RunHumanBot(sp.tier, FishingRules.StaminaFor(si, weight),
                                FishingRules.ResistFor(si, weight), (uint)(si * 31 + w + 9),
                                0.20f, 0.70f, FishingRules.TapCastDistance, out ta);
                    RunHumanBot(sp.tier, FishingRules.StaminaFor(si, weight),
                                FishingRules.ResistFor(si, weight), (uint)(si * 31 + w + 9),
                                0.20f, 0.70f, FishingRules.FullCastDistance, out fu);
                    tapLen += ta; tapN++; fullLen += fu; fullN++;
                }
            }
            Console.WriteLine("    a RARE fight lasts " + (tapLen / tapN).ToString("F1")
                              + "s on a tap and " + (fullLen / fullN).ToString("F1")
                              + "s on a full charge");
            Check(fullLen / fullN > (tapLen / tapN) * 1.5f,
                  "charging the cast buys a meaningfully longer fight");
        }

        Check(timeouts == 0, "no fight ran past the 10 minute guard");
        Check(perfectLandFails == 0,
              "a skilled bot lands every tier x weight (" + perfectLandFails + " failures)");
        // ── What "you cannot just hold the button" means now ─────────────────
        // Sam halved the steady tension rate on 2026-09-01 ("this will make
        // catching fish easier but thats what i want"), so a hold-forever bot
        // CAN now brute-force a short cast. That is not a regression -- it falls
        // out of the cast-distance mechanic and is arguably the best thing in
        // the whole system:
        //
        //   lob it at your feet -> a small common -> holding works. New players
        //                          catch something on their first go.
        //   cast it long        -> the rare, heavy fish -> holding LOSES it.
        //
        // Difficulty scales with reward automatically, with no difficulty
        // setting anywhere. So the contract is no longer "holding always fails";
        // it is "holding fails where the VALUABLE fish are".
        Check(holdFailsCommon == 0,
              "hold-forever still lands every common (the beginner's fish)");
        Check(runOutBreaches == 0,
              "a run never drags the fish past the run-out cap (" + runOutBreaches + " breaches)");

        // ── The load-bearing half: a LONG cast must still punish holding ─────
        // This is where the money is (rare share goes 19% -> 37% from a 3 m lob
        // to a 20 m cast), so this is where the skill has to live. If a future
        // tuning pass makes holding viable out here, the fight stops being a
        // game and the economy loses its only brake.
        int longHoldLands = 0, longHoldTotal = 0;
        int rareLongHoldLands = 0, rareLongHoldTotal = 0;
        for (int si = 0; si < FishingRules.Species.Length; si++)
        {
            var sp = FishingRules.Species[si];
            if (sp.tier == FishTier.Common) continue;
            for (int w = 0; w < 25; w++)
            {
                float weight = sp.weightMin + (sp.weightMax - sp.weightMin) * (w / 24f);
                var sim = new FishFightSim(sp.tier, FishingRules.StaminaFor(si, weight),
                                           FishingRules.FullCastDistance,
                                           FishingRules.ResistFor(si, weight),
                                           (uint)(si * 77 + w + 1));
                FightOutcome o = FightOutcome.Fighting;
                for (int i = 0; i < 60 * 600; i++)
                {
                    o = sim.Step(Dt, true);
                    if (o != FightOutcome.Fighting) break;
                }
                bool landed = o == FightOutcome.Landed;
                longHoldTotal++;
                if (landed) longHoldLands++;
                if (sp.tier == FishTier.Rare)
                {
                    rareLongHoldTotal++;
                    if (landed) rareLongHoldLands++;
                }
            }
        }
        Check(rareLongHoldLands == 0,
              "hold-forever NEVER lands a rare on a full-charge cast (" + rareLongHoldLands
              + "/" + rareLongHoldTotal + " got through)");
        Check(longHoldLands / (float)longHoldTotal < 0.1f,
              "hold-forever loses at least 90% of the good fish on a long cast ("
              + (100f * longHoldLands / longHoldTotal).ToString("F0") + "% landed)");

        // The shape of the fight: the fish must actually be brought IN. A model
        // where distance barely moves is the v1 bug all over again.
        var probe = new FishFightSim(FishTier.Rare, 12f, CastDistance,
                                     FishingRules.ResistFor(8, 35f), 12345u);
        float startD = probe.Distance;
        for (int i = 0; i < 60; i++) probe.Step(Dt, true);      // one second of reeling
        Check(probe.Distance < startD - 0.5f,
              "one second of reeling visibly moves a rare (moved "
              + (startD - probe.Distance).ToString("F2") + "m)");

        // ── The bar fills SLOWER as the fish tires (Sam, 2026-09-01) ─────────
        // "at the start of the fish fight its a back and forth battle, but then
        // the longer the battle goes the easier it becomes to reel the fish in".
        Check(FishingRules.TensionVigourScale(1f) > FishingRules.TensionVigourScale(0.5f)
              && FishingRules.TensionVigourScale(0.5f) > FishingRules.TensionVigourScale(0f),
              "a tireder fish loads the line less");
        Check(FishingRules.TensionVigourScale(0f) < 0.5f,
              "a spent fish is genuinely forgiving to reel");
        // Reeling into a run must be much worse than reeling calmly -- that is
        // what makes "let go the instant it runs" the skill.
        Check(FishingRules.RunTensionScale >= FishingRules.SteadyTensionScale * 2f,
              "reeling into a run fills the bar at least twice as fast per unit pull");

        // A spent fish stops running and comes in easier — the "it gives up" beat.
        // Built with ZERO stamina rather than a sliver: since the cascade landed,
        // stamina only drains through a TIGHT line, so a fish with 0.001 left is
        // still not spent after one frame of reeling on slack line.
        var spent = new FishFightSim(FishTier.Rare, 0f, CastDistance,
                                     FishingRules.ResistFor(8, 35f), 999u);
        spent.Step(Dt, true);
        Check(spent.IsSpent && !spent.IsRunning, "a fish with no stamina left never runs");

        foreach (var kv in medians)
        {
            var list = kv.Value;
            if (list.Count == 0) continue;
            list.Sort();
            float med = list[list.Count / 2];
            var lost = lostM[kv.Key];
            lost.Sort();
            float medLost = lost.Count > 0 ? lost[lost.Count / 2] : 0f;
            Console.WriteLine("    median fight, " + kv.Key + ": " + med.ToString("F1") + "s"
                              + "  (min " + list[0].ToString("F1")
                              + "s, max " + list[list.Count - 1].ToString("F1") + "s)"
                              + "   tug-of-war: " + medLost.ToString("F1") + " m taken back of a "
                              + CastDistance.ToString("F0") + " m cast");
        }
    }

    // ── [TEST] 7 — rod / line load contract ──────────────────────────────────
    // This is Sam's description of a fight, written down as assertions. It has
    // been retuned three times; locking it here means the next tuning pass can
    // change the NUMBERS without silently breaking the SHAPE.

    /// <summary>
    /// Cast distance for the scripted fixtures below.
    ///
    /// It is DERIVED from ReelSpeed, not typed. Every scenario in this test
    /// reels for a fixed number of frames and then asserts something about what
    /// happened on the way -- so a tuning pass that speeds the reel up used to
    /// silently turn "the rod reaches a full bow" and "abandoning the fight
    /// loses the fish" into "it landed before we got there". That happened on
    /// 2026-09-08 when the reel doubled; four checks failed and not one of them
    /// was about a real regression. Scaling with the reel keeps the fixtures
    /// measuring the SHAPE, which is what this test exists to lock.
    /// </summary>
    static readonly float FixtureCast = 14f * (FishingRules.ReelSpeed / 6.5f);

    static void LoadContractChecks()
    {
        Console.WriteLine("[TEST] 7  the cascade: line -> rod -> fish -> bar");

        // ── Holding the reel: the LINE moves first, and NOTHING else moves
        //    until it is tight. This is the ordering Sam kept having to
        //    re-explain, so it is asserted step by step rather than trusted.
        var f = new FishFightSim(FishTier.Rare, 10f, FixtureCast, 0.4f, 4242u);
        float d0 = f.Distance;

        Check(f.LineTaut < 0.001f, "a fresh hook starts with a slack line");
        Check(Math.Abs(f.RodLoad(true)) < 0.0001f, "slack line = NO rod bend, even while reeling");

        // One frame of reeling: line begins to come tight, nothing else stirs.
        f.Step(Dt, true);
        Check(f.LineTaut > 0.001f, "reeling starts tightening the line immediately");
        Check(!f.LineIsTight, "...but it is not tight yet after one frame");
        Check(Math.Abs(f.Tension) < 0.0001f, "the BAR does not fill through a slack line");
        Check(Math.Abs(f.Distance - d0) < 0.0001f, "the FISH does not move through a slack line");
        // The rod now FADES IN with the line instead of switching on at the
        // tight threshold (Sam, 2026-09-08: "whenever you start reeling with the
        // rod it bends a little bit as the line goes from slack to tight"). One
        // frame in, the line is a few percent tight, so the rod is a few percent
        // loaded -- present in the maths, invisible on screen.
        Check(f.RodLoad(true) < 0.02f, "an almost-slack line puts almost no bend in the rod");

        // Keep reeling until it comes tight, then everything downstream starts.
        int frames = 0;
        while (!f.LineIsTight && frames < 600) { f.Step(Dt, true); frames++; }
        Check(f.LineIsTight, "the line does come tight while reeling");
        float tautAt = frames * Dt;
        Check(tautAt > 0.2f && tautAt < 1.2f,
              "coming tight takes a readable moment, not a frame (" + tautAt.ToString("F2") + "s)");

        float tBefore = f.Tension, dBefore = f.Distance;
        f.Step(Dt, true);
        Check(f.Tension > tBefore, "once tight, the BAR starts filling");
        Check(f.Distance < dBefore, "once tight, the FISH starts coming in");
        // A tight line with a fish on it and an empty bar is the fish's WEIGHT,
        // and nothing more. The rest of the rod's travel belongs to tension --
        // that is what makes the bend readable as the bar.
        Check(f.RodLoad(true) > 0.05f, "once tight, the ROD takes the fish's weight");
        Check(f.RodLoad(true) < 0.35f, "...but an empty bar is nowhere near a full bow");

        // Reeling load must RISE with tension — that ramp is what lets the rod
        // itself warn you about the snap, so the bar is only a backup.
        float prev = -1f;
        bool monotone = true, everMax = false;
        var g = new FishFightSim(FishTier.Uncommon, 6f, FixtureCast, 0.3f, 99u);
        for (int i = 0; i < 60 * 10; i++)
        {
            if (g.Step(Dt, true) != FightOutcome.Fighting) break;
            if (!g.LineIsTight) continue;
            float load = g.RodLoad(true);
            if (load < prev - 0.0001f) monotone = false;
            if (load >= 0.95f) everMax = true;
            prev = load;
        }
        Check(monotone, "reeling load never falls while tension climbs");
        Check(everMax, "reeling long enough drives the rod to a near-full bow before it snaps");

        // ── Releasing: the BAR turns around at once, the rod unloads, and the
        //    line is the SLOWEST thing to let go.
        var h = new FishFightSim(FishTier.Rare, 10f, FixtureCast, 0.4f, 77u);
        for (int i = 0; i < 200; i++) h.Step(Dt, true);
        Check(h.LineIsTight && h.TensionFraction > 0.1f, "reeled up to real tension");

        // Measured while the fish is NOT running. A run bends the rod on its own
        // (RodLoad = 0.50 with no reeling at all) and always has, so "letting go
        // unloads the rod" is a statement about the REELING share of the load.
        // Before the bolt (2026-09-08) a fresh fish could not be running this
        // early and the distinction never came up; now it can, so the fixture
        // says which moment it means instead of relying on luck.
        int settle = 0;
        while (h.IsRunning && settle < 600) { h.Step(Dt, true); settle++; }
        Check(!h.IsRunning, "settled onto a non-running frame to measure the rod");

        float tensionAtRelease = h.Tension;
        float bendAtRelease = h.RodLoad(false);
        h.Step(Dt, false);
        Check(h.Tension < tensionAtRelease, "the BAR starts emptying the moment you let go");
        // The rod comes down WITH the bar, not instead of it. It used to drop to
        // zero the instant the button came up, which is the one thing a rod that
        // is standing in for the bar must never do -- there is still a fish on
        // the end and the bar is still nearly full.
        Check(h.RodLoad(false) < bendAtRelease, "the ROD starts unloading the moment you let go");
        Check(h.RodLoad(false) > 0.2f, "...but does NOT snap straight while the bar is still loaded");
        Check(h.LineTaut > 0.9f, "...while the LINE is still nearly tight — it droops slowest");

        // ── THE ROD IS THE BAR ──────────────────────────────────────────────
        // Sam, 2026-09-08: "the rod needs to be the same as the status bar,
        // because eventually id like to remove the status bar and have it
        // feeling so good you can just judge it from the rod."
        //
        // Two things have to hold for that to be true. The bend has to MOVE WITH
        // the bar, and it has to move slowly enough to read.
        {
            var probe = new FishFightSim(FishTier.Rare, 30f, FixtureCast, 0.4f, 2468u);
            float prevBend = 0f, prevTension = 0f;
            float worstJump = 0f, worstCruise = 0f;
            bool tracksBar = true;
            int seen = 0;
            for (int i = 0; i < 60 * 60; i++)
            {
                var outcome = probe.Step(Dt, true);
                float bend = FishingRules.BendCurve(probe.RodLoad(true));
                if (probe.LineIsTight && seen > 0)
                {
                    // Same direction as the bar, every frame.
                    float dB = bend - prevBend, dT = probe.TensionFraction - prevTension;
                    if (dB * dT < -1e-6f) tracksBar = false;
                    float jump = Math.Abs(dB);
                    if (jump > worstJump) worstJump = jump;
                    if (!probe.IsRunning && jump > worstCruise) worstCruise = jump;
                }
                prevBend = bend; prevTension = probe.TensionFraction;
                seen++;
                if (outcome != FightOutcome.Fighting) break;
            }
            Check(tracksBar, "the rod bend never moves against the bar");
            // Two different speeds, deliberately. Cruising, the rod must crawl --
            // that is the part you read and plan against. Mid-push it is allowed
            // to move fast, because the BAR moves that fast during a push and the
            // rod's whole job is to be the bar. What must never happen again is
            // the old stepped load, which put 0.45 of the bow on in ONE frame
            // ("goes from a little bent to fully bent very fast").
            Check(worstCruise < 0.02f,
                  "cruising, the bend crawls (worst " + worstCruise.ToString("F4") + ")");
            Check(worstJump < 0.05f,
                  "even mid-push the bend never jumps unreadably (worst "
                  + worstJump.ToString("F4") + " of full bow)");
            Console.WriteLine("    rod-as-bar: worst single-frame bend jump "
                              + (worstJump * 100f).ToString("F2") + "% mid-push, "
                              + (worstCruise * 100f).ToString("F2") + "% cruising");
        }

        // The line must take visibly longer to droop than the bar takes to
        // notice: "the rod unbends first... then the line starts slowly drooping".
        int droopFrames = 0;
        while (h.LineTaut > 0.05f && droopFrames < 600) { h.Step(Dt, false); droopFrames++; }
        float droopTime = droopFrames * Dt;
        Check(droopTime > 0.8f, "the line droops SLOWLY (" + droopTime.ToString("F2") + "s)");
        Check(droopTime > tautAt, "drooping takes longer than tightening did");

        // ── A run: the fish moves the float first, the line comes tight, then
        //    the rod bends. And reeling INTO a run is punishing.
        var r = new FishFightSim(FishTier.Rare, 12f, FixtureCast, 0.4f, 31337u);
        int guard = 0;
        while (!r.IsRunning && guard < 60 * 30) { r.Step(Dt, false); guard++; }
        Check(r.IsRunning, "a rare does run");
        float runDist = r.Distance;
        r.Step(Dt, false);
        Check(r.Distance > runDist, "a run moves the fish AWAY first, whatever you do");

        // Tightening while a fish runs and you reel must be much faster than
        // tightening on your own.
        var slow = new FishFightSim(FishTier.Common, 5f, FixtureCast, 0.1f, 11u);
        int slowFrames = 0;
        while (!slow.LineIsTight && slowFrames < 600) { slow.Step(Dt, true); slowFrames++; }

        var fast = new FishFightSim(FishTier.Rare, 12f, FixtureCast, 0.4f, 31337u);
        guard = 0;
        while (!fast.IsRunning && guard < 60 * 30) { fast.Step(Dt, false); guard++; }
        int fastFrames = 0;
        while (!fast.LineIsTight && fastFrames < 600) { fast.Step(Dt, true); fastFrames++; }
        Check(fastFrames < slowFrames,
              "reeling into a run comes tight FASTER (" + (fastFrames * Dt).ToString("F2")
              + "s vs " + (slowFrames * Dt).ToString("F2") + "s) - which is why you must let go");

        // Vigour: full at the start, gone once the fish is spent.
        var v = new FishFightSim(FishTier.Rare, 0.05f, FixtureCast, 0.4f, 5u);
        Check(Math.Abs(v.Vigour - 1f) < 0.001f, "a fresh fish is at full vigour");
        for (int i = 0; i < 60 * 30 && !v.IsSpent; i++) v.Step(Dt, true);
        Check(v.IsSpent && v.Vigour < 0.001f, "a spent fish has no vigour left");
        v.Step(Dt, true);
        Check(!v.IsRunning, "a spent fish never runs");

        // ── A spat hook is NOT a snapped line ────────────────────────────────
        // Sam, 2026-09-01: "if you just hook the fish, then dont reel, after a
        // few seconds the bobber and line just disappear... that's not good, the
        // bobber should just go from moving to being still and staying in the
        // water." SlippedOff and Snapped are handled differently by the rod, so
        // the sim must keep them genuinely distinct: leaving the line slack must
        // produce SlippedOff, never Snapped.
        var slip = new FishFightSim(FishTier.Uncommon, 6f, FixtureCast, 0.3f, 606u);
        for (int i = 0; i < 120; i++) slip.Step(Dt, true);      // hook it and pull
        Check(slip.TensionFraction > 0.05f, "built some tension before letting go");
        FightOutcome slipOut = FightOutcome.Fighting;
        for (int i = 0; i < 60 * 30; i++)
        {
            slipOut = slip.Step(Dt, false);                     // ...then just stop
            if (slipOut != FightOutcome.Fighting) break;
        }
        Check(slipOut == FightOutcome.SlippedOff,
              "abandoning the fight loses the FISH, not the line (got " + slipOut + ")");
        Check(slip.LineTaut < 0.05f, "and the line has gone properly slack by then");

        // The escape must take a readable few seconds, not fire the instant you
        // pause -- a player catching their breath should not lose the fish.
        var pause = new FishFightSim(FishTier.Uncommon, 6f, FixtureCast, 0.3f, 909u);
        for (int i = 0; i < 120; i++) pause.Step(Dt, true);
        bool survivedAPause = true;
        for (int i = 0; i < 90; i++)                            // 1.5 s of nothing
            if (pause.Step(Dt, false) != FightOutcome.Fighting) survivedAPause = false;
        Check(survivedAPause, "a short pause does not lose the fish");

        // The bend curve: gentle below the knee, full only at the top.
        Check(Math.Abs(FishingRules.BendCurve(0f)) < 0.0001f, "no load, no bend");
        Check(Math.Abs(FishingRules.BendCurve(1f) - 1f) < 0.0001f, "full load, full bend");
        Check(Math.Abs(FishingRules.BendCurve(FishingRules.BendKnee)
                       - FishingRules.BendAtKnee) < 0.0001f,
              "the knee sits where it says it does");
        Check(FishingRules.BendCurve(0.45f) > 0.25f,
              "simply reeling gives a NOTICEABLE bend (Sam: 'it barely bends')");
        Check(FishingRules.BendCurve(0.45f) < 0.6f,
              "...but nowhere near the full bow below half load");
        bool curveMonotone = true;
        float last = -1f;
        for (float x = 0f; x <= 1.0001f; x += 0.02f)
        {
            float y = FishingRules.BendCurve(x);
            if (y < last - 0.0001f) curveMonotone = false;
            last = y;
        }
        Check(curveMonotone, "the bend curve never goes backwards");
    }

    // ── [TEST] 2 — bite-rate sweep ───────────────────────────────────────────
    // dot from -1 to 1 in 0.05 steps: monotone in each region, continuous at
    // the band edges.

    static void BiteRateChecks()
    {
        Console.WriteLine("[TEST] 2  sun-angle bite rate");

        float prev = FishingRules.WaitMultiplier(-1f);
        float maxJump = 0f;
        for (float dot = -1f; dot <= 1.0001f; dot += 0.05f)
        {
            float m = FishingRules.WaitMultiplier(dot);
            float jump = Math.Abs(m - prev);
            if (jump > maxJump) maxJump = jump;
            prev = m;
        }
        // 0.05 of dot inside the 0.2-wide blend moves the day ramp by
        // (1.6-0.5) * 0.05/0.2 = 0.275 at most. Anything larger is a pop.
        Check(maxJump <= 0.2751f, "no discontinuity across the sweep (max step "
                                  + maxJump.ToString("F4") + ")");

        // Sam, 2026-09-02: every band bites at a reasonable clip now -- the old
        // 1.6x day rate stacked with the old 1.6x no-bait penalty into his
        // ~40 s midday waits. The bands differ in WHAT bites (TierWeights).
        Check(Math.Abs(FishingRules.WaitMultiplier(0f) - 0.5f) < 0.0001f,
              "sunrise/sundown is the 0.5x twilight rate");
        Check(Math.Abs(FishingRules.WaitMultiplier(1f) - 0.85f) < 0.0001f,
              "noon is the 0.85x day rate (fast bites, common fish)");
        Check(Math.Abs(FishingRules.WaitMultiplier(-1f) - 0.75f) < 0.0001f,
              "midnight is the 0.75x night rate");
        // The regression this build exists to prevent: a bare hook at noon must
        // never wait ~40 s again. Base wait tops out at 14 s (FishingTuning).
        Check(14f * FishingRules.WaitMultiplier(1f)
                  * FishingRules.BaitWaitMultiplier(BaitKind.None) < 15f,
              "worst-case bare-hook noon wait stays under 15s");
        Check(FishingRules.WaitMultiplier(0.15f) < FishingRules.WaitMultiplier(0.35f),
              "day side is monotone increasing across the blend");
        Check(FishingRules.WaitMultiplier(-0.15f) < FishingRules.WaitMultiplier(-0.35f),
              "night side is monotone increasing across the blend");
        Check(FishingRules.WaitMultiplier(0.5f) > FishingRules.WaitMultiplier(-0.5f),
              "daytime fishing is slower than night fishing");

        // Twilight really is the best band.
        float best = FishingRules.WaitMultiplier(0f);
        bool anyBetter = false;
        for (float dot = -1f; dot <= 1.0001f; dot += 0.05f)
            if (FishingRules.WaitMultiplier(dot) < best - 0.0001f) anyBetter = true;
        Check(!anyBetter, "nothing beats the twilight band");
    }

    // ── [TEST] 3 — species roll ──────────────────────────────────────────────
    // 10,000 rolls per (bait x light band): tier frequencies within +/-2% of the
    // table, and species uniform inside a tier.

    static void SpeciesRollChecks()
    {
        Console.WriteLine("[TEST] 3  species roll");

        float[] bands = { 1f, 0f, -1f };            // day, twilight, night
        string[] bandNames = { "day", "twilight", "night" };
        BaitKind[] baits = { BaitKind.None, BaitKind.Grubs, BaitKind.Glowworms, BaitKind.Voidmaggots };

        for (int b = 0; b < bands.Length; b++)
        {
            for (int k = 0; k < baits.Length; k++)
            {
                // Explicit cast distance: the roll now includes a cast-distance
                // shift, so the expectation has to carry the same shift or the
                // test is comparing against a table nothing rolls against.
                // A half charge — the typical throw. Derived, never typed.
                float TestCast = FishingRules.CastDistanceFor(0.5f);
                float c, u, r;
                FishingRules.TierWeights(bands[b], baits[k], out c, out u, out r);
                FishingRules.ApplyCastShift(TestCast, ref c, ref u, ref r);
                float total = c + u + r;

                const int N = 10000;
                int[] hits = new int[3];
                var rng = new Xs((uint)(b * 97 + k * 13 + 1));
                for (int i = 0; i < N; i++)
                    hits[(int)FishingRules.RollTier(bands[b], baits[k], TestCast, rng.Next01())]++;

                string tag = bandNames[b] + " x " + baits[k];
                CheckPct(hits[0] / (float)N, c / total, tag + " common");
                CheckPct(hits[1] / (float)N, u / total, tag + " uncommon");
                CheckPct(hits[2] / (float)N, r / total, tag + " rare");
            }
        }

        // GRUBS are the neutral baseline the table is written against -- not
        // "no bait", which is now a NEGATIVE shift (bait is optional; fishing
        // bare-handed is worse, not impossible).
        float c0, u0, r0;
        FishingRules.TierWeights(0f, BaitKind.Grubs, out c0, out u0, out r0);
        Check(Math.Abs(c0 - 38f) < 0.001f && Math.Abs(u0 - 37f) < 0.001f && Math.Abs(r0 - 25f) < 0.001f,
              "twilight table is 38/37/25 on the neutral bait");
        float cd, ud, rd;
        FishingRules.TierWeights(1f, BaitKind.Grubs, out cd, out ud, out rd);
        Check(Math.Abs(cd - 58f) < 0.001f && Math.Abs(ud - 28f) < 0.001f && Math.Abs(rd - 14f) < 0.001f,
              "day table is Sam's common-grind 58/28/14 on the neutral bait");
        float cn, un, rn2;
        FishingRules.TierWeights(-1f, BaitKind.Grubs, out cn, out un, out rn2);
        Check(Math.Abs(cn - 40f) < 0.001f && Math.Abs(un - 36f) < 0.001f && Math.Abs(rn2 - 24f) < 0.001f,
              "night table is 40/36/24 -- night boosts uncommons and rares");
        Check(rd < r0, "twilight really does raise the rare rate");
        // Sam, 2026-09-02: "at night have it boost the chance to catch
        // uncommons and rares" -- night must beat day on quality, and day must
        // beat everything on commons.
        Check(rn2 > rd && un > ud, "night out-fishes day for uncommons AND rares");
        Check(cd > c0 && cd > cn,  "day is the common-fish band");

        // Bait shifts point the right way.
        float cG, uG, rG, cV, uV, rV;
        FishingRules.TierWeights(0f, BaitKind.Glowworms,   out cG, out uG, out rG);
        FishingRules.TierWeights(0f, BaitKind.Voidmaggots, out cV, out uV, out rV);
        Check(uG > u0 && rG == r0, "Glowworms shift toward uncommon only");
        Check(rV > r0 && uV > u0,  "Voidmaggots shift toward rare and uncommon");
        Check(cG < c0 && cV < c0,  "both baits draw from the common pool");

        // ── BAIT IS OPTIONAL (Sam, 2026-09-01) ──────────────────────────────
        // No bait must be WORSE than the neutral bait in every way, and yet
        // must never close a tier off: "you should still be able to get a rare,
        // just have it be rare." A regression that zeroed the bare-hook rare
        // rate would be invisible in play for hours, so it is asserted here.
        float cN, uN, rN;
        FishingRules.TierWeights(0f, BaitKind.None, out cN, out uN, out rN);
        Check(rN > 0f,  "a bare hook can STILL land a rare");
        Check(uN > 0f,  "a bare hook can still land an uncommon");
        Check(rN < r0,  "no bait is worse for rares than the neutral bait");
        Check(uN < u0,  "no bait is worse for uncommons than the neutral bait");
        Check(cN > c0,  "no bait skews toward commons");
        Check(FishingRules.BaitWaitMultiplier(BaitKind.None)
                > FishingRules.BaitWaitMultiplier(BaitKind.Grubs),
              "no bait waits longer for a bite than Grubs");
        Check(FishingRules.BaitWaitMultiplier(BaitKind.Voidmaggots)
                < FishingRules.BaitWaitMultiplier(BaitKind.Glowworms)
              && FishingRules.BaitWaitMultiplier(BaitKind.Glowworms)
                < FishingRules.BaitWaitMultiplier(BaitKind.Grubs),
              "better bait bites faster, in price order");

        // The genuine WORST CASE the game can produce: no bait, daylight, and a
        // lob at your feet. Even that must leave a real chance of a rare -- Sam
        // was explicit that a bare hook should still be able to land one, just
        // rarely. A regression stacking these three into zero would be invisible
        // in play for hours, which is exactly why it is asserted.
        foreach (float band in new[] { 1f, 0f, -1f })
        {
            float bc, bu, br;
            FishingRules.TierWeights(band, BaitKind.None, out bc, out bu, out br);
            FishingRules.ApplyCastShift(FishingRules.TapCastDistance, ref bc, ref bu, ref br);
            float share = br / (bc + bu + br);
            Check(share > 0.02f,
                  "worst case (no bait, short cast, band " + band
                  + ") still lands rares: " + (100f * share).ToString("F1") + "%");
        }

        // ── Sam's night spec, 2026-09-02, pinned ────────────────────────────
        // "using no bait and fishing at night should result in 1-2 rare fish,
        // 2-3 uncommon and around 4 common" -- i.e. roughly 18/31/51 out of 8.
        //
        // Measured at a TYPICAL cast, which is a half charge. It used to say
        // "8 m", which stopped meaning "typical" the moment the cast became a
        // charge that tops out at 8.5 -- an 8 m cast went from an ordinary throw
        // to very nearly the longest one available, and this pin started
        // reporting 29% rares. Asking the rules what a typical cast is keeps the
        // spec pinned to the thing Sam described rather than to a number.
        {
            float nb_c, nb_u, nb_r;
            FishingRules.TierWeights(-1f, BaitKind.None, out nb_c, out nb_u, out nb_r);
            FishingRules.ApplyCastShift(FishingRules.CastDistanceFor(0.5f),
                                        ref nb_c, ref nb_u, ref nb_r);
            float tot = nb_c + nb_u + nb_r;
            float rShare = nb_r / tot, uShare = nb_u / tot, cShare = nb_c / tot;
            Check(rShare > 0.14f && rShare < 0.23f,
                  "night/no-bait rares land 1-2 in 8 (" + (100f * rShare).ToString("F0") + "%)");
            Check(uShare > 0.25f && uShare < 0.38f,
                  "night/no-bait uncommons land 2-3 in 8 (" + (100f * uShare).ToString("F0") + "%)");
            Check(cShare > 0.42f && cShare < 0.60f,
                  "night/no-bait commons land ~4 in 8 (" + (100f * cShare).ToString("F0") + "%)");
        }

        // ── Bait buys SIZE as well as rarity (Sam, 2026-09-02) ──────────────
        // Same species, same cast: the median weight must climb with bait
        // quality, and a bare hook must run lighter than the neutral bait.
        {
            var wrng = new Xs(4242u);
            float wNone = 0f, wGrub = 0f, wVoid = 0f;
            const int WN = 4000;
            for (int i = 0; i < WN; i++)
            {
                float roll = wrng.Next01();
                wNone += FishingRules.RollWeight(0, roll, 10f, BaitKind.None);
                wGrub += FishingRules.RollWeight(0, roll, 10f, BaitKind.Grubs);
                wVoid += FishingRules.RollWeight(0, roll, 10f, BaitKind.Voidmaggots);
            }
            Check(wVoid > wGrub && wGrub > wNone,
                  "better bait pulls heavier fish (avg lb none "
                  + (wNone / WN).ToString("F2") + " < grubs " + (wGrub / WN).ToString("F2")
                  + " < voidmaggots " + (wVoid / WN).ToString("F2") + ")");
            Check(FishingRules.RollWeight(0, 0.5f, 10f)
                      == FishingRules.RollWeight(0, 0.5f, 10f, BaitKind.Grubs),
                  "the bait-less overload is the neutral-bait roll");
        }

        // Species uniform inside a tier.
        for (int t = 0; t < 3; t++)
        {
            var tier = (FishTier)t;
            var counts = new Dictionary<int, int>();
            var rng = new Xs((uint)(500 + t));
            const int N = 12000;
            for (int i = 0; i < N; i++)
            {
                int si = FishingRules.RollSpeciesInTier(tier, rng.Next01());
                counts.TryGetValue(si, out int had);
                counts[si] = had + 1;
            }
            // Derived, never typed. The pool grew 12 -> 24 species on
            // 2026-09-07 (two per tier on each main world, one on each dwarf)
            // and this test kept asserting the old four, so it sat red for a
            // day while claiming the species roll was broken. It was not.
            int expected = 0;
            for (int s = 0; s < FishingRules.Species.Length; s++)
                if (FishingRules.Species[s].tier == tier && !FishingRules.Species[s].bounty)
                    expected++;
            Check(counts.Count == expected,
                  tier + " rolls all " + expected + " of its species (got " + counts.Count + ")");
            float share = 1f / expected;
            bool uniform = true;
            foreach (var kv in counts)
                if (Math.Abs(kv.Value / (float)N - share) > 0.02f) uniform = false;
            Check(uniform, tier + " species roll is uniform within +/-2%");
        }

        // Bounty row: in the table, never in the ordinary roll (2026-09-03).
        {
            int gru = FishingRules.IndexOfId("grulabu");
            Check(gru >= 0 && FishingRules.IsBounty(gru), "GRULABU is in the table as a bounty row");
            bool leaked = false;
            var brng = new Xs(777);
            for (int i = 0; i < 20000; i++)
                if (FishingRules.IsBounty(FishingRules.RollSpeciesInTier(FishTier.Rare, brng.Next01()))) leaked = true;
            Check(!leaked, "the bounty never comes up in the ordinary rare roll");
            Check(gru < 0 || FishingRules.PriceOf(gru, 200f) >= 400,
                  "GRULABU at 200 lb is worth a bounty ($" + (gru < 0 ? 0 : FishingRules.PriceOf(gru, 200f)) + ")");
        }

        // Legacy saves land on species 0 of their tier.
        Check(FishingRules.Species[FishingRules.MigrateLegacyTier("Common")].tier == FishTier.Common,
              "legacy 'Common' migrates to a common species");
        Check(FishingRules.Species[FishingRules.MigrateLegacyTier("Rare")].tier == FishTier.Rare,
              "legacy 'Rare' migrates to a rare species");
        Check(FishingRules.Species[FishingRules.MigrateLegacyTier("nonsense")].tier == FishTier.Common,
              "an unknown legacy tier falls back to common, not a crash");
    }

    // ── Cast distance shapes the catch (Sam, 2026-09-01) ─────────────────────
    // "the further away you cast, the higher chance you have for catching a more
    // rare and bigger fish, vs doing small casts right in front of you will get
    // smaller more common fish."

    static void CastDistanceChecks()
    {
        Console.WriteLine("[TEST] 6  cast distance");

        Check(Math.Abs(FishingRules.CastFactor(FishingRules.ShortCast)) < 0.001f,
              "a cast at your feet is factor 0");
        Check(Math.Abs(FishingRules.CastFactor(FishingRules.LongCast) - 1f) < 0.001f,
              "a full-length cast is factor 1");
        Check(FishingRules.CastFactor(FishingRules.TapCastDistance * 0.5f) == 0f
              && FishingRules.CastFactor(FishingRules.FullCastDistance * 4f) == 1f,
              "the cast factor clamps at both ends");

        // Rare rate must rise monotonically with distance, and a short cast must
        // be strictly worse than a long one.
        float prevRare = -1f;
        bool monotone = true;
        for (float d = FishingRules.TapCastDistance; d <= FishingRules.FullCastDistance + 0.001f; d += 0.25f)
        {
            float c, u, r;
            FishingRules.TierWeights(0f, BaitKind.Grubs, out c, out u, out r);
            FishingRules.ApplyCastShift(d, ref c, ref u, ref r);
            float share = r / (c + u + r);
            if (share < prevRare - 0.0001f) monotone = false;
            prevRare = share;
        }
        Check(monotone, "rare share never falls as the cast gets longer");

        // Tap versus a full two-second charge — the two casts a player can
        // actually make. Typing 3 and 20 meant this was comparing a throw at
        // your feet against one nobody could reach.
        float sc, su, sr, lc, lu, lr;
        FishingRules.TierWeights(0f, BaitKind.Grubs, out sc, out su, out sr);
        FishingRules.ApplyCastShift(FishingRules.TapCastDistance, ref sc, ref su, ref sr);
        FishingRules.TierWeights(0f, BaitKind.Grubs, out lc, out lu, out lr);
        FishingRules.ApplyCastShift(FishingRules.FullCastDistance, ref lc, ref lu, ref lr);

        float shortShare = sr / (sc + su + sr);
        float longShare  = lr / (lc + lu + lr);
        Check(longShare > shortShare * 1.5f,
              "a long cast is meaningfully rarer than a short one ("
              + (100f * shortShare).ToString("F1") + "% -> "
              + (100f * longShare).ToString("F1") + "%)");
        Check(shortShare > 0.02f,
              "even a lob at your feet can still turn up a rare ("
              + (100f * shortShare).ToString("F1") + "%)");
        Console.WriteLine("    rare share: 3m cast " + (100f * shortShare).ToString("F1")
                          + "%  ->  20m cast " + (100f * longShare).ToString("F1") + "%");

        // ...and it must land BIGGER fish, not just rarer ones.
        for (int si = 0; si < FishingRules.Species.Length; si += 4)
        {
            if (FishingRules.Species[si].bounty) continue;
            float meanShort = 0f, meanLong = 0f;
            const int S = 4000;
            for (int k = 0; k < S; k++)
            {
                float x = (k + 0.5f) / S;
                meanShort += FishingRules.RollWeight(si, x, 3f);
                meanLong  += FishingRules.RollWeight(si, x, 20f);
            }
            meanShort /= S; meanLong /= S;
            Check(meanLong > meanShort * 1.15f,
                  FishingRules.Species[si].displayName + " is heavier on a long cast ("
                  + meanShort.ToString("F1") + "lb -> " + meanLong.ToString("F1") + "lb)");
        }

        // A weight roll can never escape the species' own range, whatever the cast.
        bool inRange = true;
        for (int si = 0; si < FishingRules.Species.Length; si++)
        {
            var sp = FishingRules.Species[si];
            foreach (float d in new[] { 0f, 3f, 12f, 20f, 100f })
                foreach (float x in new[] { 0f, 0.5f, 1f })
                {
                    float w = FishingRules.RollWeight(si, x, d);
                    if (w < sp.weightMin - 0.001f || w > sp.weightMax + 0.001f) inRange = false;
                }
        }
        Check(inRange, "weight stays inside the species range at every cast distance");
    }

    static void CheckPct(float got, float want, string what)
    {
        Checks++;
        if (Math.Abs(got - want) <= 0.02f) return;
        Failures++;
        Console.WriteLine("  FAIL  " + what + ": got " + (got * 100f).ToString("F1")
                          + "%, want " + (want * 100f).ToString("F1") + "%");
    }

    // ── [TEST] 5 — economy report ────────────────────────────────────────────
    // Not a pass/fail: the handoff asks for the number and for a PROPOSAL if it
    // is out of line, explicitly not for an applied price change.

    static void EconomyReport()
    {
        Console.WriteLine("[TEST] 5  economy");

        foreach (var bait in new[] { BaitKind.Grubs, BaitKind.Voidmaggots })
        {
            float c, u, r;
            FishingRules.TierWeights(0f, bait, out c, out u, out r);   // twilight, the best band
            float total = c + u + r;

            float ev = 0f;
            for (int i = 0; i < FishingRules.Species.Length; i++)
            {
                var sp = FishingRules.Species[i];
                if (sp.bounty) continue;   // never in the ordinary roll
                float tierW = sp.tier == FishTier.Common ? c : sp.tier == FishTier.Uncommon ? u : r;
                float pTier = tierW / total;
                // Mean of the power-biased weight roll, sampled.
                float meanW = 0f;
                const int S = 2000;
                for (int k = 0; k < S; k++) meanW += FishingRules.RollWeight(i, (k + 0.5f) / S);
                meanW /= S;
                ev += pTier * 0.25f * FishingRules.PriceOf(i, meanW);
            }

            int baitCost = bait == BaitKind.Voidmaggots ? 4 : 1;
            Console.WriteLine("    twilight x " + bait + ": $" + ev.ToString("F2")
                              + " per landed fish, minus $" + baitCost + " bait = $"
                              + (ev - baitCost).ToString("F2") + " net");
        }
        Console.WriteLine("    (tape medians for comparison: DEMO $15, HALF $31, FULL $73)");
    }

    struct Xs
    {
        uint s;
        public Xs(uint seed) { s = seed == 0u ? 0x9E3779B9u : seed; }
        public float Next01()
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("fishing rules");
        Console.WriteLine();
        FightChecks();
        LoadContractChecks();
        BiteRateChecks();
        SpeciesRollChecks();
        CastDistanceChecks();
        EconomyReport();
        Console.WriteLine();
        Console.WriteLine(Failures == 0
            ? "PASS  " + Checks + " checks"
            : "FAIL  " + Failures + " of " + Checks + " checks");
        return Failures == 0 ? 0 : 1;
    }
}
