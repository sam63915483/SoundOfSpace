using UnityEngine;

/// <summary>
/// The rover's driver seat: look at it, press F, and you're driving
/// (RoverController.Board). Lives on the seat cushion, which carries both the
/// solid collider the crosshair cast hits and a trigger sphere for the
/// "in range" zone — the same two-collider shape PoolTable / MoonBaseDoorButton use.
/// </summary>
public class RoverSeat : Interactable
{
    [Tooltip("The rover this seat belongs to (set by RoverBuilder; found in the parents if empty).")]
    public RoverController rover;
    [Tooltip("How close the player has to be, metres.")]
    public float interactRadius = 3.2f;

    void Awake()
    {
        if (rover == null) rover = GetComponentInParent<RoverController>();
        bool hasTrigger = false;
        foreach (var c in GetComponents<Collider>()) if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = interactRadius;
        }
    }

    protected override bool CanInteract() => rover != null && !rover.Occupied && !RoverController.IsDriving;

    protected override string BuildInteractMessage() => $"Press {PromptGlyphs.Interact} to drive";

    protected override void Interact()
    {
        base.Interact();
        if (rover == null) return;
        var pc = FindObjectOfType<PlayerController>();
        if (pc == null) return;
        GameUI.ClearInteractionPrompt(this);
        rover.Board(pc);
    }

    void OnValidate() { interactMessage = "#set from script#"; }
}
