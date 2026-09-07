/// <summary>
/// A tank a <see cref="ShipReactor"/> can pour crystals into.
///
/// The manual ship and the shuttle both take crystals through the same reactor
/// prop and the same F-to-insert prompt, but they burn what they hold in
/// completely different ways — the ship drains per second of held thrust, the
/// shuttle is billed per kilometre the moment you press TRAVEL. This interface
/// is deliberately only the REFUELLING half, which is the part they share; how
/// a tank empties is each vehicle's own business.
///
/// Implemented by <c>Ship</c> (the manual flyer, pre-existing behaviour) and
/// <c>ShuttleFuel</c> (planet economy, 2026-09-07).
/// </summary>
public interface IReactorFuel
{
    /// <summary>Size of the tank, in fuel units.</summary>
    float FuelMax { get; }

    /// <summary>How full it is right now, 0–1.</summary>
    float FuelPercent { get; }

    /// <summary>Pour in <paramref name="amount"/> units, clamped at the top.</summary>
    void RestoreFuel(float amount);
}
