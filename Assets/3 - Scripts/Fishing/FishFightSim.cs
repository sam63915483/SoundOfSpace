// The fight itself, as a pure state machine. [BUILD] 1 of the fishing handoff.
//
// ZERO UnityEngine REFERENCES — same rule as FishingRules.cs. The Unity layer
// (Bobber / FishingRodController) owns input, the bobber's motion, rod bend and
// audio; this owns the numbers. Keeping them apart is what lets [TEST] 1 run
// thousands of simulated fights headlessly instead of Sam play-testing every
// tier x weight combination.
//
// ── REWRITE, 2026-09-01 (Sam's playtest) ────────────────────────────────────
// v1 made TENSION the whole game and hid the fish's stamina in a number. Sam:
// "the bobber and rod all stay still, and you're just there clicking and
// keeping the bar half filled up, a new player would have no clue what's even
// happening." He was right, and it was a design error, not a tuning one: the
// only feedback loop was a bar, and the optimal play was to hold it at 50%.
//
// So the win condition is now DISTANCE. The fish is a real point in the water:
// reeling drags it toward you, a run takes line back out, and you land it when
// it reaches the bank. Progress is a thing you SEE, with no UI at all.
//
// Stamina survives but changed job. It is no longer the win condition — it is
// how much RUN the fish has left. While it lasts the fish surges and resists;
// when it hits zero the fish is spent, the runs stop and it comes in easy.
// That gives the fight a shape (grab -> argument -> it gives up) instead of a
// flat bar-holding exercise.
//
// Tension demotes to what it should always have been: the punishment for
// greed. It is the reason you cannot simply hold the button, not the game.

using System;

public enum FightOutcome
{
    Fighting = 0,
    Landed   = 1,   // the fish reached the bank — the existing catch flow takes over
    Snapped  = 2,   // tension hit 100 — fish lost, bait lost
    SlippedOff = 3, // slack held too long — anti-stuck-state escape hatch
}

/// <summary>
/// One fish's fight. Construct on the hook with the real cast distance, Step()
/// every frame with whether the player is holding the reel, and act on the
/// outcome. <see cref="Distance"/> is what the Unity layer moves the bobber to.
/// </summary>
public class FishFightSim
{
    /// Metres from the player to the fish. THE win condition — the bobber is
    /// drawn at this distance, so the player reads progress off the water.
    public float Distance { get; private set; }
    public float StartDistance { get; private set; }

    public float Tension { get; private set; }
    /// Seconds of run left in the fish. At zero it is spent: no more runs, and
    /// it stops resisting the reel.
    public float Stamina { get; private set; }
    public float StaminaStart { get; private set; }
    public bool  IsRunning { get; private set; }
    public float Elapsed { get; private set; }

    /// True once the fish has nothing left — the Unity layer uses this for the
    /// "it gives up" tell (the rod unbends, the reel stops screaming).
    public bool IsSpent => Stamina <= 0f;

    /// <summary>
    /// How much fight is left, 1 fresh to 0 beaten. The Unity layer scales the
    /// fish's thrashing by this, which is the ONLY thing that tells the player
    /// how tired it is — there is deliberately no stamina bar.
    /// </summary>
    public float Vigour => StaminaStart > 0.0001f ? Clamp01(Stamina / StaminaStart) : 0f;

    public readonly FishTier tier;
    public readonly float basePull;

    /// How hard this fish drags back against the reel, 0-1 of your reel speed.
    /// Scales with weight inside the tier, and HALVES once the fish is spent.
    readonly float _resist;
    readonly float _runSpeed;

    readonly bool  _canRun;
    readonly float _runIntervalMin, _runIntervalMax;
    float _nextRunIn;
    float _runRemaining;
    uint  _rng;

    float _slackSeconds2;   // seconds of dead-slack line, for the escape hatch

    readonly float _reelRate, _relaxRate, _drainRate, _slackEscape, _reelSpeed;
    readonly float _landDistance;
    readonly float _tautSeconds, _slackSeconds;

    /// <summary>
    /// How tight the line is, 0 slack to 1 bar-tight. <b>This is a GATE, not a
    /// decoration.</b> See the class comment: nothing downstream of it happens
    /// until it reaches 1.
    /// </summary>
    public float LineTaut { get; private set; }

    /// Force only travels down a tight line.
    public const float TautThreshold = 0.985f;
    public bool LineIsTight => LineTaut >= TautThreshold;

    /// <summary>
    /// Overwrite the running distance with where the bobber ACTUALLY is.
    ///
    /// The sim used to keep its own running total and the Unity layer chased it,
    /// so the two could disagree — and the landing fired off the sim's number
    /// rather than the thing the player is watching. Sam noticed exactly that:
    /// the catch did not trigger when the bobber reached him. Now reality is
    /// pushed in every frame before Step, so "the bobber is within 2 m" and "the
    /// fish is landed" are guaranteed to be the same event.
    /// </summary>
    public void SyncDistance(float actualDistance)
    {
        if (actualDistance > 0f) Distance = actualDistance;
    }

    public FishFightSim(FishTier tier, float stamina, float startDistance, float resist, uint seed)
        : this(tier, stamina, startDistance, resist, seed,
               FishingRules.ReelRate, FishingRules.RelaxRate, FishingRules.DrainRate,
               FishingRules.SlackEscapeSeconds, FishingRules.ReelSpeed,
               FishingRules.LandDistance, FishingRules.TautSeconds,
               FishingRules.SlackSeconds) { }

    public FishFightSim(FishTier tier, float stamina, float startDistance, float resist, uint seed,
                        float reelRate, float relaxRate, float drainRate, float slackEscape,
                        float reelSpeed, float landDistance, float tautSeconds, float slackSeconds)
    {
        _landDistance = landDistance > 0f ? landDistance : FishingRules.LandDistance;
        _tautSeconds  = tautSeconds  > 0.01f ? tautSeconds  : FishingRules.TautSeconds;
        _slackSeconds = slackSeconds > 0.01f ? slackSeconds : FishingRules.SlackSeconds;
        this.tier = tier;
        basePull  = FishingRules.PullForTier(tier);
        Stamina      = stamina;
        StaminaStart = stamina;
        Distance      = startDistance;
        StartDistance = startDistance;
        Tension      = 0f;
        _resist      = resist;
        _runSpeed    = FishingRules.RunSpeedForTier(tier);
        _reelRate    = reelRate;
        _relaxRate   = relaxRate;
        _drainRate   = drainRate;
        _slackEscape = slackEscape;
        _reelSpeed   = reelSpeed;
        _rng = seed == 0u ? 0x9E3779B9u : seed;

        _canRun = FishingRules.TierRuns(tier);
        FishingRules.RunIntervalForTier(tier, out _runIntervalMin, out _runIntervalMax);
        _nextRunIn = _canRun ? RandRange(_runIntervalMin, _runIntervalMax) : float.MaxValue;
    }

    /// <summary>Pull in force right now — doubled mid-run. Drives the rod bend.</summary>
    public float CurrentPull => IsRunning ? basePull * FishingRules.RunPullMultiplier : basePull;

    /// <summary>0-1 for the HUD bar. Above ~0.75 the bar shifts toward red.</summary>
    public float TensionFraction => Tension / FishingRules.TensionMax;

    /// <summary>
    /// How far the ROD is bent, 0-1. Zero until the line is tight, because a rod
    /// cannot be loaded through slack line — that ordering is the entire point
    /// of the cascade.
    ///
    /// Once the line IS tight:
    ///   - reeling  -> 0.45 rising with tension to 1.0, so the rod bends further
    ///                 the longer you hold and warns you about the snap itself
    ///   - a run    -> 0.50 on its own ("the rod would bend a bit")
    ///   - both     -> maxed, and moments from breaking
    /// </summary>
    public float RodLoad(bool reeling)
    {
        if (!LineIsTight) return 0f;
        float load = 0f;
        if (reeling)   load += 0.45f + 0.55f * TensionFraction;
        if (IsRunning) load += 0.50f;
        return load > 1f ? 1f : load;
    }

    /// <summary>
    /// Advance one frame. <paramref name="holding"/> is the reel input.
    /// Anything but Fighting means the fight is over.
    /// </summary>
    public FightOutcome Step(float dt, bool holding)
    {
        if (dt <= 0f) return FightOutcome.Fighting;
        Elapsed += dt;

        // --- run scheduling. A spent fish has no runs left in it. ---
        if (_canRun && !IsSpent)
        {
            if (IsRunning)
            {
                _runRemaining -= dt;
                if (_runRemaining <= 0f)
                {
                    IsRunning = false;
                    _nextRunIn = RandRange(_runIntervalMin, _runIntervalMax);
                }
            }
            else
            {
                _nextRunIn -= dt;
                if (_nextRunIn <= 0f)
                {
                    IsRunning = true;
                    _runRemaining = RandRange(FishingRules.RunDurationMin,
                                              FishingRules.RunDurationMax);
                }
            }
        }
        else IsRunning = false;

        // --- the fish takes line during a run, whatever the player does ---
        if (IsRunning)
        {
            Distance += _runSpeed * dt;
            if (Distance > StartDistance * FishingRules.MaxRunOutFactor)
                Distance = StartDistance * FishingRules.MaxRunOutFactor;
            // Running costs the fish. This is what makes a fight finite even if
            // the player only ever reels in the gaps.
            Stamina -= _drainRate * dt;
        }

        // ── 1. THE LINE. First thing to move, every time. ────────────────
        // Pulling on slack line takes the slack up; it does not yet move a fish
        // or bend a rod. Sam, 2026-09-01: "when you left click to set hook, the
        // line is the first thing to start getting tight, then once its tight
        // the rod starts bending, the fish starts getting pulled in and the bar
        // starts filling."
        bool anyPull = holding || IsRunning;
        float tautSeconds = _tautSeconds;
        // Reeling INTO a run comes tight violently — which is the whole reason
        // you have to let go the instant a fish starts to move.
        if (holding && IsRunning) tautSeconds *= 0.35f;
        float tautRate = 1f / (anyPull ? tautSeconds : _slackSeconds);
        LineTaut = MoveTowards(LineTaut, anyPull ? 1f : 0f, tautRate * dt);

        // ── 2. FORCE, but only down a TIGHT line ─────────────────────────
        if (holding && LineIsTight)
        {
            // Steady reeling is half rate; reeling INTO a run is full rate on
            // top of the doubled pull. And the whole thing eases off as the fish
            // tires, so the fight starts as a back-and-forth and ends as a haul.
            float scale = (IsRunning ? FishingRules.RunTensionScale
                                     : FishingRules.SteadyTensionScale)
                        * FishingRules.TensionVigourScale(Vigour);
            Tension += _reelRate * CurrentPull * scale * dt;
            Stamina -= _drainRate * dt;

            // A spent fish stops fighting the reel, so the last stretch is
            // quick — the player feels it give up.
            float resist = IsSpent ? _resist * 0.5f : _resist;
            Distance -= _reelSpeed * (1f - resist) * dt;
            if (Distance < 0f) Distance = 0f;
            _slackSeconds2 = 0f;
        }
        else if (holding)
        {
            // Holding, but still taking up slack: nothing is being transmitted
            // yet, so the bar simply waits rather than filling or draining.
            _slackSeconds2 = 0f;
        }
        else
        {
            // Released. The bar starts coming down straight away — before the
            // line has finished drooping, which is the order Sam described.
            Tension -= _relaxRate * dt;
            if (Tension <= 0f)
            {
                Tension = 0f;
                if (LineTaut <= 0.01f) _slackSeconds2 += dt;
            }
            else _slackSeconds2 = 0f;
        }

        if (Stamina < 0f) Stamina = 0f;

        // Snap is checked BEFORE landing: a player who holds through the last
        // moment and blows past 100 in the same frame loses it. The bar is what
        // punishes greed, so the bar wins ties.
        if (Tension >= FishingRules.TensionMax)
        {
            Tension = FishingRules.TensionMax;
            return FightOutcome.Snapped;
        }
        if (Distance <= _landDistance)
        {
            Distance = _landDistance;
            return FightOutcome.Landed;
        }
        if (_slackSeconds2 >= _slackEscape)
            return FightOutcome.SlippedOff;

        return FightOutcome.Fighting;
    }

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// UnityEngine.Mathf.MoveTowards, reimplemented so this file stays free of
    /// UnityEngine and testable headlessly.
    static float MoveTowards(float current, float target, float maxDelta)
    {
        float d = target - current;
        if (d > maxDelta) return current + maxDelta;
        if (d < -maxDelta) return current - maxDelta;
        return target;
    }

    // xorshift32 — deterministic, seedable, no UnityEngine.Random. Same reason
    // TraxPrng exists: a test that cannot reproduce a failing fight is useless.
    float Rand01()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) / 16777216f;
    }

    float RandRange(float a, float b) => a + (b - a) * Rand01();
}
