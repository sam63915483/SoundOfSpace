using UnityEngine;

/// <summary>
/// Trigger volume on the OUTSIDE of the ship that lets the player open the
/// front hatch from outside — the lockout escape hatch. Only available when
/// BOTH the front hatch and the back hatch are closed: if either is open the
/// player can already get back in through it, and OutsideOpen would just be a
/// redundant duplicate prompt.
/// </summary>
public class OutsideHatchTrigger : Interactable
{
    Ship _ship;
    BackHatch _backHatch;

    Ship Ship
    {
        get
        {
            // Walk up to THIS trigger's ship — FindObjectOfType returns an
            // arbitrary ship in scene-order, which silently toggled the wrong
            // ship's hatch when the player had bought additional ships.
            if (_ship == null) _ship = GetComponentInParent<Ship>();
            return _ship;
        }
    }

    BackHatch BackHatchRef
    {
        get
        {
            if (_backHatch == null) _backHatch = GetComponentInParent<BackHatch>();
            if (_backHatch == null && Ship != null) _backHatch = Ship.GetComponentInChildren<BackHatch>(true);
            return _backHatch;
        }
    }

    protected override bool CanInteract()
    {
        bool frontClosed = Ship == null || !Ship.HatchOpen;
        bool backClosed  = BackHatchRef == null || !BackHatchRef.IsOpen;
        return frontClosed && backClosed;
    }

    protected override string BuildInteractMessage()
    {
        // CanInteract gates this trigger to "both hatches closed", so the
        // prompt only ever needs the "open" wording.
        return $"Press {PromptGlyphs.Interact} to open hatch";
    }

    protected override void Interact()
    {
        base.Interact();
        if (Ship != null) Ship.ToggleHatch();
        ShowInteractMessage();
    }

    protected override void OnTriggerEnter(Collider c)
    {
        if (!c.CompareTag("Player")) return;
        playerInInteractionZone = true;
        // Suppress the on-enter prompt flash if either hatch is open — the
        // base class's OnTriggerEnter would otherwise show the prompt for one
        // frame before the Update re-assertion (which IS gated on CanInteract)
        // notices and lets it clear.
        if (CanInteract()) ShowInteractMessage();
    }

    void OnValidate()
    {
        interactMessage = "#set from script#";
    }
}
