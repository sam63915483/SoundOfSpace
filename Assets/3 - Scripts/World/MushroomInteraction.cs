using UnityEngine;

// Runtime-attached to every spawned mushroom by MushroomSpawner. Subclasses
// Interactable so it inherits the "Press F to ___" prompt + trigger plumbing.
// On eat: heals proportionally to the mushroom's spawn-time scale and starts a
// raw-fish-style trip with this mushroom's deterministic mix of colour /
// breathing / kaleidoscope intensities.
public class MushroomInteraction : Interactable
{
    public string mushroomDisplayName = "mushroom";
    public float mushroomScale = 1f;
    public float colourPct = 0f;
    public float breathPct = 0f;
    public float kaleidoPct = 0f;

    [HideInInspector] public MushroomSpawner spawner;
    [HideInInspector] public int bodySlot;
    [HideInInspector] public long cellId;

    const float MinHealHealth = 5f;   // 1× mushroom
    const float MaxHealHealth = 25f;  // 5× mushroom
    const float TripDuration = 30f;

    protected override string BuildInteractMessage()
    {
        return $"Press {PromptGlyphs.Interact} to eat {mushroomDisplayName}";
    }

    protected override void Interact()
    {
        float t = Mathf.Clamp01((mushroomScale - 1f) / 4f);
        float heal = Mathf.Lerp(MinHealHealth, MaxHealHealth, t);
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.Heal(heal);

        // Constant-intensity trip: early == late, so all three dials stay at
        // their target % for the whole trip. Colour scale is independent of the
        // other two so the per-mushroom mix actually varies.
        RawFishTripController.StartTrip(
            TripDuration,
            kaleidoPct, breathPct,
            TripDuration,
            kaleidoPct, breathPct,
            colourPct);

        if (spawner != null) spawner.MarkCellConsumed(bodySlot, cellId);

        ClearPlayerInInteractionZone();
        Destroy(gameObject);
    }
}
