using UnityEngine;

/// <summary>
/// Marker placed on a building (e.g. the start cabin) whose geometry should
/// keep grass from spawning beneath it. GrassSpawner's downward surface
/// raycast checks GetComponentInParent&lt;GrassBlocker&gt;() on whatever it
/// hits; if the cast lands on a collider under a GrassBlocker (the cabin roof,
/// since the ray comes from above), that spot is rejected — so the whole
/// footprint underneath, the interior floor, and the roof itself stay bare.
///
/// Requirement for it to work: the blocker's collider must be on a layer that
/// is INCLUDED in GrassSpawner.groundMask, so the ray actually hits it.
/// </summary>
public class GrassBlocker : MonoBehaviour { }
