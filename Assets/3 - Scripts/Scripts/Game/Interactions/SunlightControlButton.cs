using UnityEngine;

public class SunlightControlButton : Interactable
{
    protected override bool CanInteract() => TutorialGate.IsUnlocked(TutorialAbility.InteractSunlight);

    protected override string BuildInteractMessage()
    {
        bool active = LebronLight.Instance != null && LebronLight.Instance.IsActive;
        string action = active ? "deactivate" : "activate";
        return $"Press {PromptGlyphs.Interact} to {action} lebron light";
    }

    protected override void Interact()
    {
        base.Interact();
        if (LebronLight.Instance != null) LebronLight.Instance.Toggle();
        ShowInteractMessage();
    }

    void OnValidate()
    {
        interactMessage = "#set from script#";
    }
}
