using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Click-and-hold dragging for an inventory slot, as a bolt-on.
///
/// The slot systems already did left-click = whole stack and right-click = one,
/// through <see cref="SlotOps"/>. The only thing missing was picking a stack up
/// by holding and dragging it. That's what this adds — and ONLY that: both click
/// behaviours are untouched, so nothing already in muscle memory moves.
///
/// Attach to the same GameObject as the slot's raycast-target background, next
/// to whatever click handler it already has, and wire the three callbacks. The
/// host keeps owning the cursor state; this component just says when.
///
/// <b>Why OnEndDrag resolves the target itself instead of using IDropHandler:</b>
/// the relative firing order of IDropHandler.OnDrop and IEndDragHandler.OnEndDrag
/// is an implementation detail of the input module, and a "was I dropped on
/// something?" flag that depends on that order is a coin flip across Unity
/// versions. Reading <see cref="PointerEventData.pointerCurrentRaycast"/> at
/// release and finding the slot under the pointer ourselves is deterministic.
///
/// <b>Clicks after a drag:</b> Unity's PointerInputModule clears
/// <c>eligibleForClick</c> as soon as a drag begins, so IPointerClickHandler does
/// NOT fire afterwards. The host does not need to suppress a phantom click.
/// </summary>
public class SlotDragProxy : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// Pick this slot's whole stack up onto the cursor. Only called when the
    /// cursor is empty — see <see cref="canBeginDrag"/>.
    public System.Action beginDrag;

    /// Deposit the cursor into this slot (merge / swap / fill, host's choice).
    public System.Action drop;

    /// Nothing under the pointer at release — put the held stack back.
    public System.Action returnToSource;

    /// False if this slot has nothing to drag, or the cursor is already full.
    public System.Func<bool> canBeginDrag;

    bool _active;

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (canBeginDrag != null && !canBeginDrag()) return;
        _active = true;
        beginDrag?.Invoke();
    }

    // Required for the event system to route drag events at all, even though the
    // cursor follower is driven by the host's Update.
    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        if (!_active || e.button != PointerEventData.InputButton.Left) return;
        _active = false;

        var go = e.pointerCurrentRaycast.gameObject != null
            ? e.pointerCurrentRaycast.gameObject
            : e.pointerEnter;
        var target = go != null ? go.GetComponentInParent<SlotDragProxy>() : null;

        if (target != null && target.drop != null) target.drop.Invoke();
        else returnToSource?.Invoke();
    }
}
