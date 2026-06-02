using UnityEngine;

// Empty marker so tutorial steps and TevDialogue can find the village
// GameObject by component (Object.FindObjectOfType<VillageMarker>()) instead
// of relying on its name or path. The Village GameObject sits under the
// planet so it moves/rotates with the surface — anything that needs the
// village's world position should read this transform live.
public class VillageMarker : MonoBehaviour
{
}
