using UnityEngine;

/// <summary>
/// Drop this on a trigger collider (BoxCollider, isTrigger = true). When the player
/// walks into it, performs a level-portal scene transition via <see cref="PortalManager"/>.
///
/// Examples in this project:
///  - Cabin "BackroomsEntrance1" (in 1.6.7.7.7): EnterInterior, targetScene = "R1_Backrooms"
///  - "NEXTLEVEL" (in R1_Backrooms):              EnterInterior, targetScene = "PoolroomsDemo"
///  - "EXIT" (in R1_Backrooms):                   ReturnToGameplay
///
/// Target scenes must be in Build Settings for SceneManager.LoadScene to find them.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelPortal : MonoBehaviour
{
    public enum PortalAction { EnterInterior, ReturnToGameplay }

    [Tooltip("EnterInterior: load 'Target Scene', carrying inventory + jetpack.\nReturnToGameplay: restore the departure snapshot and return to 1.6.7.7.7.")]
    public PortalAction action = PortalAction.EnterInterior;

    [Tooltip("Scene name to load when action = EnterInterior (must be in Build Settings). e.g. 'R1_Backrooms', 'PoolroomsDemo'.")]
    public string targetScene = "R1_Backrooms";

    // Guard against the trigger firing twice in the frame before the scene unloads.
    bool _fired;

    void OnTriggerEnter(Collider other)
    {
        if (_fired) return;
        // The entering collider may be a child of the player rig, so search upward.
        if (other.GetComponentInParent<PlayerController>() == null) return;
        _fired = true;

        if (action == PortalAction.EnterInterior)
            PortalManager.EnterInterior(targetScene);
        else
            PortalManager.ReturnToGameplay();
    }
}
