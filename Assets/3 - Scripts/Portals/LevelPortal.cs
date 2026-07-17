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
            PortalManager.EnterInterior(ResolveChainTarget(targetScene));
        else
            PortalManager.ReturnToGameplay();
    }

    // ── Dimension-chain routing ──
    // Exit portals inside the D1..D28 dimensions carry a scene-BAKED target string from
    // whenever that dimension was built — D8's still said "R1_Backrooms" from the era when
    // only 8 dimensions existed, which skipped D9–D28 entirely. Rather than hand-editing
    // ~20 scene files, route here: while the CURRENT scene is part of the canonical chain
    // (DimensionDevLoader.Scenes), any portal that targets another chain scene or the
    // backrooms is redirected to the canonical NEXT dimension; the last dimension (D28)
    // goes to R1_Backrooms — which chains to PoolroomsDemo via its own NEXTLEVEL portal,
    // keeping the backrooms second-last. Portals OUTSIDE the chain (the cabin backrooms
    // entrance, R1's own portals) are untouched.
    static string ResolveChainTarget(string requested)
    {
        var chain = DimensionDevLoader.Scenes;
        string current = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        int idx = System.Array.IndexOf(chain, current);
        if (idx < 0) return requested;                       // not inside a chain dimension
        bool requestedIsChainy = requested == "R1_Backrooms" || System.Array.IndexOf(chain, requested) >= 0;
        if (!requestedIsChainy) return requested;            // deliberate off-chain portal
        // Walk to the next dimension that ACTUALLY EXISTS — 9 of the 28 planned levels
        // were written off and have no scene (D10, D14, D17, D19-21, D26-28). The canon
        // list keeps their slots (Shift+D numbering + future builds), so skip anything
        // not loadable from Build Settings. Past the last real dimension → the backrooms.
        for (int i = idx + 1; i < chain.Length; i++)
            if (Application.CanStreamedLevelBeLoaded(chain[i])) return chain[i];
        return "R1_Backrooms";
    }
}
