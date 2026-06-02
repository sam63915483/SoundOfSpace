using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HatchButton : Interactable {

    protected override bool CanInteract () => TutorialGate.IsUnlocked (TutorialAbility.InteractHatch);

    protected override string BuildInteractMessage () {
        // Bind to THIS button's ship — multiple ships in the scene means
        // FindObjectOfType<Ship>() can return the wrong instance and show
        // a stale prompt ("close" when this ship's hatch is actually closed).
        Ship ship = GetComponentInParent<Ship> ();
        string action = (ship != null && ship.HatchOpen) ? "close" : "open";
        return $"Press {PromptGlyphs.Interact} to {action} hatch";
    }

    protected override void Interact () {
        base.Interact ();
        ShowInteractMessage ();
    }

    void OnValidate () {
        interactMessage = "#set from script#";
    }
}