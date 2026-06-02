using UnityEngine;
using TMPro;

/// <summary>
/// Shared registry for the scene's bonfire cook-panel + prompt-text refs.
/// The "source" bonfire in the gameplay scene populates these on Start;
/// runtime-placed bonfires (build menu and save-load round-trip) read
/// from here instead of scanning the scene for another bonfire to copy.
///
/// The static survives source-bonfire destruction because the refs point
/// to the HUD Canvas, which lives independently in the scene.
///
/// On scene/domain reload, the static is cleared so a stale reference
/// from a prior play session can't leak into a fresh scene.
/// </summary>
public static class BonfireUIRegistry
{
    public static GameObject CookPanel;
    public static TextMeshProUGUI PromptText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        CookPanel = null;
        PromptText = null;
    }
}
