using UnityEngine;

/// <summary>
/// What eating a mushroom does. Lifted verbatim out of the old
/// MushroomInteraction (which ate the whole world prop on interact) so the
/// effect survives the harvest rework unchanged — the handoff's rule was "keep
/// the current eat effect values".
///
/// ONE deliberate change: the colour / breathing / kaleidoscope dials used to be
/// rolled per world instance from its spawn cell. A harvested cap is an ITEM
/// now, and carrying three floats per stack through the hotbar, the locker and
/// the save file to reproduce a mushroom that no longer exists is a lot of
/// plumbing for something the player can't see. So the dials are derived from
/// the SPECIES key instead: every red cap trips the same way, different species
/// trip differently. Same spread of effects, and it becomes knowledge the player
/// can actually learn — which is what a drug economy wants.
///
/// Heal is a flat per-cap value rather than the old scale-based 5–25: that range
/// was the payout for consuming an ENTIRE mushroom, and one mushroom now yields
/// 3–9 caps.
/// </summary>
public static class MushroomEffect
{
    public const float HealPerMushroom = 5f;    // low end of the old 5–25 range
    public const float TripDuration    = 30f;   // unchanged

    public static void Consume(string speciesKey)
    {
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.Heal(HealPerMushroom);

        GetDials(speciesKey, out float colour, out float breath, out float kaleido);

        // Constant-intensity trip: early == late, so all three dials stay at
        // their target % for the whole trip (the old MushroomInteraction shape).
        RawFishTripController.StartTrip(
            TripDuration,
            kaleido, breath,
            TripDuration,
            kaleido, breath,
            colour);
    }

    /// The species' three trip dials, 0..1. Deterministic — the same species
    /// always produces the same mix, in this session and every future one.
    public static void GetDials(string speciesKey, out float colour, out float breath, out float kaleido)
    {
        uint h = Hash(speciesKey);
        colour  = ((h)        & 0xFFFFu) / 65535f;
        breath  = ((h >>  7)  & 0xFFFFu) / 65535f;
        kaleido = ((h >> 15)  & 0xFFFFu) / 65535f;
    }

    // FNV-1a over the species key, then an avalanche so the three shifted
    // windows above don't correlate.
    static uint Hash(string s)
    {
        uint h = 2166136261u;
        if (!string.IsNullOrEmpty(s))
        {
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
        }
        h ^= h >> 16; h *= 2246822507u;
        h ^= h >> 13; h *= 3266489909u;
        h ^= h >> 16;
        return h;
    }
}
