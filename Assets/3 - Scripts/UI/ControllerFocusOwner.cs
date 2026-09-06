using UnityEngine;

// Marker component (2026-09-06 controller pass 2). Put it on the panel
// GameObject that a dialogue option list toggles on/off (PostGreetingChoicePanel
// "Panel", WorldDialogueUI root). Two effects:
//
//   1. ControllerUINavigator leaves the EventSystem selection alone while it
//      sits under an owner — no migration to a "topmost" canvas, no clearing
//      when the row is mid fade-in (the owner re-selects its own rows).
//   2. PadCursor stays INACTIVE while any owner is active, so dialogue
//      options keep the highlight-box navigation (Sam's call: the cursor is
//      for mouse-style screens; option lists stay stick + A).
//
// Active count is kept in OnEnable/OnDisable so the check is O(1) per frame.
public class ControllerFocusOwner : MonoBehaviour
{
    static int s_active;

    public static bool AnyActive => s_active > 0;

    public static bool Owns(GameObject go) =>
        go != null && go.GetComponentInParent<ControllerFocusOwner>() != null;

    void OnEnable()  { s_active++; }
    void OnDisable() { s_active = Mathf.Max(0, s_active - 1); }
}
