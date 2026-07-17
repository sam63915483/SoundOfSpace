using UnityEngine;

/// <summary>
/// Put this on a parent GameObject whose children have the renderers/colliders
/// (e.g. a wall piece with a "side A" and "side B" child). Clicking any child in
/// the Scene view will then select THIS parent instead of the child, so you can
/// copy/move the whole piece as one. Editor-only behaviour; does nothing at runtime.
/// (To still drill into a child, click the piece a second time.)
/// </summary>
[SelectionBase]
public class SelectionRoot : MonoBehaviour { }
