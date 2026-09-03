using UnityEngine;

/// <summary>
/// The Unity side of the species table: tint conversion and model lookup.
///
/// <see cref="FishingRules"/> stores tints as raw RGB bytes so it can stay free
/// of UnityEngine and be executed headlessly by verify-fishing.py. This is the
/// one place that turns those bytes into a Color.
///
/// <b>Model reality check.</b> The handoff assumed 3 models per tier (9 total).
/// The project has THREE MODELS TOTAL, one per tier
/// (FishingdexManager.commonFishPrefab / uncommonFishPrefab / rareFishPrefab,
/// Floreswa fish01-03). Sam's call on 2026-09-01 was tint-only: the four species
/// in a tier share their tier's model and are told apart by colour, name and
/// size. modelIndex is still authored per species and still routed through this
/// lookup, so dropping in real extra shapes later is an asset assignment with
/// no code change.
/// </summary>
public static class FishSpeciesVisuals
{
    public static Color TintOf(int speciesIndex)
    {
        if (speciesIndex < 0 || speciesIndex >= FishingRules.Species.Length) return Color.white;
        var s = FishingRules.Species[speciesIndex];
        return new Color32(s.tintR, s.tintG, s.tintB, 0xFF);
    }

    public static Color TintOf(FishEntry entry) =>
        entry == null ? Color.white : TintOf(entry.ResolveSpecies());

    /// <summary>
    /// The prefab for a species. Falls back to the tier model whenever the
    /// requested modelIndex has no asset behind it — which today is every index
    /// above 0, and is exactly why this returns a sensible fish instead of null.
    /// </summary>
    public static GameObject PrefabFor(int speciesIndex, FishingdexManager dex)
    {
        if (dex == null) return null;
        if (speciesIndex < 0 || speciesIndex >= FishingRules.Species.Length) return null;
        return dex.PrefabForTier(FishingRules.Species[speciesIndex].tier);
    }
}
