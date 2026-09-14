using UnityEngine;

/// <summary>
/// Marker: colliders under this object do NOT occlude the procedural lens
/// flare (LensFlareRegistry.IsSampleBlocked), even though they block the
/// player and the shuttle. For see-through panes — the tutorial box's
/// digit-rain walls and ceiling are solid to walk against but visually glass,
/// and without this the sun showed plainly through them with no flare.
/// </summary>
public class LensFlarePassThrough : MonoBehaviour { }
