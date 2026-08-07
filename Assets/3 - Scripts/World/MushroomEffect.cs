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
    // 2026-08-06: the dials, the duration and the heal are now AUTHORED per
    // species in MushroomSpecies rather than hashed from the name. Species with
    // no row in that table still fall back to the old hash, so nothing breaks.
    public const float HealPerMushroom = 5f;    // legacy default; table wins
    public const float TripDuration    = 30f;   // legacy default; table wins

    // Dials during a creeper's lead-in. Not zero — "almost nothing" reads as a
    // mushroom that hasn't kicked in yet; exactly nothing reads as a dud.
    const float CreeperEarly = 0.05f;

    public static void Consume(string speciesKey)
    {
        var e = MushroomSpecies.Get(speciesKey);

        // Heal can be NEGATIVE (the Deathcaps). Routing that through TakeDamage
        // rather than a negative Heal is deliberate: TakeDamage is the game's
        // single damage choke point, so the hurt voice, the red flash and the
        // death check all fire — eating a Deathcap can actually kill you.
        if (ResourceManager.Instance != null)
        {
            if (e.heal >= 0f) ResourceManager.Instance.Heal(e.heal);
            else              ResourceManager.Instance.TakeDamage(-e.heal);
        }

        bool creeper = e.creeperLeadIn > 0.01f && e.creeperLeadIn < e.tripSeconds;

        // Flat strain  → early == late, early phase covers the whole trip.
        // Creeper      → muted for creeperLeadIn seconds, then the real dials.
        RawFishTripController.StartTrip(
            e.tripSeconds,
            creeper ? CreeperEarly : e.kaleido,
            creeper ? CreeperEarly : e.wave,
            creeper ? e.creeperLeadIn : e.tripSeconds,
            e.kaleido,
            e.wave,
            e.colour);
    }

    /// The species' three trip dials, 0..1 (the steady/late values). Read by
    /// anything that wants to describe a strain without eating it.
    public static void GetDials(string speciesKey, out float colour, out float breath, out float kaleido)
    {
        var e = MushroomSpecies.Get(speciesKey);
        colour  = e.colour;
        breath  = e.wave;
        kaleido = e.kaleido;
    }

    /// HP a single cap of this species gives. Negative = it hurts you.
    public static float HealFor(string speciesKey) => MushroomSpecies.Get(speciesKey).heal;
}
