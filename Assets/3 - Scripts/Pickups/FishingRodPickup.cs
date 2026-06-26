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
    [Tooltip("Pickup zone radius in WORLD METRES. The rod prefab is scaled down, so this is converted to local units using the collider's scale (in-memory only — prefab/scene values are not mutated).")]
    public float pickupWorldRadius = 5f;

    void Awake()
    {
        SphereCollider existing = null;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { existing = c as SphereCollider; if (existing != null) break; }

        if (existing == null)
        {
            existing = gameObject.AddComponent<SphereCollider>();
            existing.isTrigger = true;
        }

        // Convert the desired world radius into the collider's local space so the
        // zone is actually `pickupWorldRadius` metres regardless of prefab scale.
        Vector3 ls = existing.transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        if (scale < 1e-4f) scale = 1f;
        existing.radius = Mathf.Max(existing.radius, pickupWorldRadius / scale);
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
