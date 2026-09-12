using UnityEngine;

/// <summary>
/// "Press F to pick up the beer" on a cup the bartender poured. Added at
/// runtime by <see cref="BarCounter.PourBeer"/> — never placed by hand.
///
/// On F: the cup joins the player's <see cref="BeerCupController"/> (the
/// Hotbar shows the BEER slot / count next frame), goes into the hand if the
/// hand was free, and the world cup is destroyed. Mirrors WaterBottlePickup.
/// </summary>
public class BeerCupPickup : Interactable
{
    [Tooltip("Radius of the trigger added at Awake if the cup has no trigger collider.")]
    public float triggerRadius = 3f;

    [System.NonSerialized] public BarCounter counter;

    void Awake()
    {
        bool hasTrigger = false;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = triggerRadius;
        }
    }

    protected override bool CanInteract() =>
        TutorialGate.IsUnlocked(TutorialAbility.Pickup);

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to pick up the beer";

    protected override void Interact()
    {
        base.Interact();

        var cup = Object.FindObjectOfType<BeerCupController>();
        if (cup == null)
        {
            Debug.LogWarning("[BeerCupPickup] No BeerCupController on the Player.");
            return;
        }

        float fill = 100f;
        var liquid = GetComponentInChildren<BeerLiquid>(true);
        if (liquid != null) fill = liquid.Fill * 100f;

        cup.AddBeer(fill);
        if (!cup.IsEquipped) cup.EquipBeer();   // stays a no-op if another tool is in hand

        if (counter != null) counter.NotifyCupTaken(this);
        GameUI.ClearInteractionPrompt(this);   // clear BEFORE Destroy or a stale prompt lingers
        Destroy(gameObject);
    }
}
