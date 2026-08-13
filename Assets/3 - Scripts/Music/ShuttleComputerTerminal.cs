using UnityEngine;

/// <summary>
/// Look at the shuttle's ConsoleScreen, press F, and the computer opens.
///
/// Lives on a child trigger volume of ConsoleScreen rather than on the screen
/// itself: Interactable needs an isTrigger collider for its zone test, and
/// ConsoleScreen's own BoxCollider is solid geometry the player can walk into.
/// `gazeTarget` points back at the screen mesh so the look-at test uses the
/// thing you can actually see, not the invisible volume around it.
///
/// Add it with Tools ▸ TRAX ▸ Add Computer Terminal To Shuttle Prefab, which
/// patches the prefab via LoadPrefabContents — the Shuttle_Lander prefab is
/// hand-maintained and must never be regenerated.
/// </summary>
public class ShuttleComputerTerminal : Interactable
{
    protected override string BuildInteractMessage()
    {
        return "Press " + PromptGlyphs.Interact + " to use the computer";
    }

    protected override bool CanInteract()
    {
        // While the screen is up it owns the input; showing "press F to use the
        // computer" over the top of the open computer would be nonsense.
        return !ShuttleComputerUI.IsOpen;
    }

    protected override void Interact()
    {
        // The screen closes on F too. Without this, the F that closed it would
        // be read again here in the same frame and reopen it immediately.
        if (ShuttleComputerUI.FConsumedThisFrame) return;

        ShuttleComputerUI.Open();
    }
}
