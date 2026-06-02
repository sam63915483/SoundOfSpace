using UnityEngine;

// Marker component. Add to a Canvas (or any ancestor of one) for two combined
// effects in ControllerUINavigator:
//   1. Controller nav skips its selectables — no auto-focus, no border, no
//      focus migration into them.
//   2. The canvas is treated as NON-MODAL — it never becomes the "top"
//      canvas in raycaster suppression. Without this, a peripheral
//      always-on-top canvas (e.g. the map's TELEPORT TO PILOT banner at
//      sortingOrder 1700) would be picked as the modal and silently
//      disable clicks on every canvas below it (e.g. the map legend).
//
// Tagged canvases still get suppressed when a TRUE modal opens above them.
public class SkipControllerNav : MonoBehaviour { }
