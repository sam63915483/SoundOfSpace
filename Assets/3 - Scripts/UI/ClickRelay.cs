using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Forwards left / right clicks on a UI graphic to two delegates.
///
/// Exists so a panel that builds its slots in code doesn't need a bespoke
/// nested MonoBehaviour per panel just to catch a click (which is what
/// StorageUI.StorageSlotClick is). Pairs with <see cref="SlotDragProxy"/>:
/// that one handles hold-and-drag, this one handles the two click paths.
///
/// Unity clears <c>eligibleForClick</c> when a drag starts, so a click never
/// fires on the tail of a drag — the two components can't double-fire.
/// </summary>
public class ClickRelay : MonoBehaviour, IPointerClickHandler
{
    public System.Action onLeft;
    public System.Action onRight;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) onLeft?.Invoke();
        else if (e.button == PointerEventData.InputButton.Right) onRight?.Invoke();
    }
}
