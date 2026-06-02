using UnityEngine;

// Drop-in pickup for the world-prop water bottle. Inherits Interactable so
// the prompt uses the standard screen-space "Press F to ..." UI.
//
// On F:
//   • WaterBottleController.Unlock() — bottle now appears in the hotbar
//   • WaterBottleController.ForceEquipBottle() — bottle equipped to player's hand
//   • This GameObject is destroyed (bottle is now in the player's hand)
public class WaterBottlePickup : Interactable
{
    [Tooltip("Trigger radius if no Collider is present at Start.")]
    public float fallbackTriggerRadius = 12.0f;

    void Awake()
    {
        // The bottle's "Press F to pick up" prompt should fire from 10× further
        // out than the prefab's authored trigger radius. We grow any existing
        // trigger SphereCollider here (in-memory only — the prefab/scene
        // values are not mutated) and bump the fallback radius to match.
        SphereCollider existing = null;
        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (!c.isTrigger) continue;
            existing = c as SphereCollider;
            break;
        }
        if (existing != null)
        {
            existing.radius *= 10f;
        }
        else
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = fallbackTriggerRadius;
        }
    }

    protected override bool CanInteract() =>
        TutorialGate.IsUnlocked(TutorialAbility.Pickup);

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to pick up bottle";

    protected override void Interact()
    {
        base.Interact(); // fires interactEvent if hooked

        var bottle = Object.FindObjectOfType<WaterBottleController>();
        if (bottle == null)
        {
            Debug.LogWarning("[WaterBottlePickup] No WaterBottleController found in scene.");
            return;
        }

        bottle.Unlock();
        bottle.ForceEquipBottle();

        GameUI.ClearInteractionPrompt(this);
        Destroy(gameObject);
    }
}
