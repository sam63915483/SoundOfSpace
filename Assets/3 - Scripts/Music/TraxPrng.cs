using System;

/// <summary>
/// Seeded randomness and the dial-vector hash.
///
/// PORT OF <c>prototypes/shuttle-computer/engine/prng.js</c>. That file is the
/// reference implementation; this one must agree with it to the last bit or a
/// cassette made in the browser prototype would sound different in-game.
/// <c>Assets/StreamingAssets/Trax/trax-golden.txt</c> pins it down, and
/// Tools ▸ TRAX ▸ Verify Engine Port checks it.
///
/// ── Three traps that make a JS→C# numeric port drift ──────────────────────
/// 1. <b>Math.round.</b> JavaScript rounds halves toward +Infinity
///    (Math.round(2.5) == 3). C#'s Math.Round is banker's rounding
///    (Math.Round(2.5) == 2). Every rounding here goes through
///    <see cref="JsRound"/>. Never call Math.Round in this file or its siblings.
/// 2. <b>double, not float.</b> JS Numbers are IEEE-754 doubles. If any engine
///    value narrows to float, a `rnd() &lt; probability` comparison can land on
///    the other side of the boundary and the whole pattern changes. The engine
///    is double throughout; only the audio backend narrows to float.
/// 3. <b>Integer division.</b> `Math.floor(a / b)` in JS is real division then
///    floor. In C# `a / b` on two ints truncates toward zero, which differs for
///    negatives. Cast to double first — see TraxScales.DegreeToMidi.
///
/// Only +, -, *, / and comparisons feed pattern generation, and IEEE-754
/// requires all four to be correctly rounded — so bit-exact agreement is
/// actually achievable here, not merely hoped for. (Math.Pow is NOT guaranteed
/// identical across implementations, which is why the few Pow-derived values
/// are audio-only and never touch a pattern decision.)
/// </summary>
public static class TraxPrng
{
    public const int DialCount = 6;

    // ── hashing ──────────────────────────────────────────────────────────

    /// FNV-1a, 32-bit.
    public static uint Fnv1a32(byte[] bytes)
    {
        uint h = 0x811c9dc5u;
        unchecked
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= (uint)(bytes[i] & 0xff);
                h *= 0x01000193u;
            }
        }
        return h;
    }

    /// FNV-1a over the low byte of each char — matches the JS generator's
    /// `charCodeAt(i) &amp; 0xff`. Used to hash pattern digests when verifying.
    public static uint Fnv1a32(string s)
    {
        uint h = 0x811c9dc5u;
        unchecked
        {
            for (int i = 0; i < s.Length; i++)
            {
                h ^= (uint)(s[i] & 0xff);
                h *= 0x01000193u;
            }
        }
        return h;
    }

    // ── rounding ─────────────────────────────────────────────────────────

    /// JavaScript's Math.round: halves go toward +Infinity. See trap 1 above.
    public static double JsRound(double x)
    {
        return Math.Floor(x + 0.5);
    }

    // ── mulberry32 ───────────────────────────────────────────────────────

    /// <summary>
    /// mulberry32. Small, fast, and trivially portable — which is the whole
    /// reason it was chosen over anything fancier.
    ///
    /// Everything is unsigned and unchecked so it wraps mod 2^32 exactly the way
    /// JS's ToInt32/ToUint32 coercions do. `Math.imul(a, b)` is the low 32 bits
    /// of the product, which is what unchecked uint multiplication gives.
    /// </summary>
    public sealed class Rng
    {
        uint _a;

        public Rng(uint seed) { _a = seed; }

        /// Next double in [0, 1).
        public double Next()
        {
            unchecked
            {
                _a = _a + 0x6D2B79F5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    // ── dial vector -> seed ──────────────────────────────────────────────

    /// <summary>
    /// Dials are continuous 0-10 in the UI but quantize to 0.5 steps for
    /// seeding, so nudging a knob by a hair doesn't silently reroll the pattern.
    /// Six bytes in 0..20, in fixed dial order.
    /// </summary>
    public static byte[] QuantizeDials(TraxDials d)
    {
        var outv = new byte[DialCount];
        for (int i = 0; i < DialCount; i++)
        {
            int q = (int)JsRound(d.Get(i) * 2.0);
            if (q < 0) q = 0;
            if (q > 20) q = 20;
            outv[i] = (byte)q;
        }
        return outv;
    }

    // NOTE: there is deliberately no SeedFromDials any more. Patterns seed from
    // TraxTrack.VoiceSeed (preset + variation), NOT from the dials — that
    // decoupling is what makes a dial shape a groove instead of re-rolling it.
    // QuantizeDials survives because TraxTrack.TrackId still hashes the dials.

    // ── per-voice streams ────────────────────────────────────────────────

    // Each voice draws from its own stream. When a 7th plugin unlocks later its
    // constant is simply a new entry here — every existing voice's pattern is
    // untouched, so cassettes printed before the unlock still sound the same.
    // These values are load-bearing: changing one re-rolls every cassette ever
    // made with that voice.
    public const uint VoiceKick    = 0x9e3779b1u;
    public const uint VoiceSnare   = 0x85ebca6bu;
    public const uint VoiceHat     = 0xc2b2ae35u;
    public const uint VoiceBass    = 0x27d4eb2fu;
    public const uint VoiceLead    = 0x165667b1u;
    public const uint VoiceMoss    = 0x7feb352du;
    public const uint VoiceSpindle = 0x846ca68bu;
    public const uint VoiceFill    = 0xd3a2646cu;
    /// The chord progression gets its own stream, so adding or reordering
    /// voices can never change which progression a seed selects.
    public const uint VoiceChord   = 0x5bd1e995u;

    public static uint ConstFor(TraxVoice v)
    {
        switch (v)
        {
            case TraxVoice.Kick:  return VoiceKick;
            case TraxVoice.Snare: return VoiceSnare;
            case TraxVoice.Hat:   return VoiceHat;
            case TraxVoice.Bass:  return VoiceBass;
            case TraxVoice.Lead:    return VoiceLead;
            case TraxVoice.Moss:    return VoiceMoss;
            case TraxVoice.Spindle: return VoiceSpindle;
            default: throw new ArgumentOutOfRangeException("v");
        }
    }

    public static Rng StreamFor(uint seed, TraxVoice v)
    {
        return new Rng(seed ^ ConstFor(v));
    }
}
