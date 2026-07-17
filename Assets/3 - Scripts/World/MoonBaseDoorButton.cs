using System.Collections;
using UnityEngine;

/// <summary>
/// A press-to-open button for a single <see cref="MoonBaseDoor"/> (the moon base's slide-up
/// airlock leaf). Look at it + press F: the target door slides open with the door's own
/// Open() mechanic, stays open <see cref="openSeconds"/>, then slides shut via Close().
/// Pressing again while it's open just restarts the timer, so you can't get caught in the
/// doorway. Put one on each side of the door so you can never get locked in or out.
///
/// Standalone, movable GameObject with its own trigger volume + renderer. Mirrors the
/// HatchButton / vendor interaction pattern (gaze gate + F + InteractPromptUI via Interactable).
/// </summary>
public class MoonBaseDoorButton : Interactable
{
    [Header("Door")]
    [Tooltip("The MoonBaseDoor leaf this button opens.")]
    public MoonBaseDoor targetDoor;

    [Tooltip("Seconds the door stays open before it auto-closes. Re-pressing restarts this.")]
    public float openSeconds = 5f;

    [Tooltip("Proximity radius for the interact prompt. A trigger SphereCollider is auto-added at this radius on Awake.")]
    public float interactRadius = 2.5f;

    Coroutine _closeRoutine;

    void Awake()
    {
        // Interactable needs a TRIGGER collider to detect the player entering range.
        // The button's solid collider (from its mesh) stays as the physical nub; add a
        // separate trigger sphere for interaction, exactly like MoonBasePowerSwitch does.
        bool hasTrigger = false;
        foreach (var c in GetComponents<Collider>())
            if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = interactRadius;
        }
    }

    protected override string BuildInteractMessage() => $"Press {PromptGlyphs.Interact} to open door";

    protected override void Interact()
    {
        base.Interact();
        if (targetDoor == null) return;

        // Open() is a no-op if already open/busy, so re-pressing is safe. Either way we
        // (re)start the auto-close timer, keeping the door open a fresh 5s from this press.
        targetDoor.Open();
        if (_closeRoutine != null) StopCoroutine(_closeRoutine);
        _closeRoutine = StartCoroutine(CloseAfterDelay());
        ShowInteractMessage();
    }

    IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(openSeconds);
        if (targetDoor != null) targetDoor.Close();
        _closeRoutine = null;
    }

    void OnValidate() { interactMessage = "#set from script#"; }
}
