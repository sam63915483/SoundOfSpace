using UnityEngine;

// Drop-in pickup for Tev's fishing rod prop. Inherits from Interactable so
// the prompt uses the same screen-space UI as the ship hatch button etc.
//
// On F:
//   • FishingRodController.Unlock() — rod now appears in the hotbar
//   • FishingRodController.ForceEquipRod() — rod equipped to player's hand
//   • EarlyGameProgress.RodPickedUp = true — tutorial step gate
//   • This GameObject is destroyed (rod is now in the player's hand)
public class FishingRodPickup : Interactable
{
    [Tooltip("Trigger radius if no Collider is present at Start.")]
    public float fallbackTriggerRadius = 1.2f;

    void Awake()
    {
        bool anyTrigger = false;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { anyTrigger = true; break; }
        if (!anyTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = fallbackTriggerRadius;
        }
    }

    protected override bool CanInteract() =>
        TutorialGate.IsUnlocked(TutorialAbility.Pickup);

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to pick up rod";

    protected override void Interact()
    {
        base.Interact(); // fires interactEvent if the user hooked anything up

        var rod = FindObjectOfType<FishingRodController>();
        if (rod == null)
        {
            Debug.LogWarning("[FishingRodPickup] No FishingRodController found in scene.");
            return;
        }

        rod.Unlock();
        rod.ForceEquipRod();
        EarlyGameProgress.RodPickedUp = true;

        // Clear our prompt before destroying so GameUI doesn't briefly show a
        // stale message between Destroy and the next frame's owner-cleanup.
        GameUI.ClearInteractionPrompt(this);
        Destroy(gameObject);
    }
}
