using UnityEngine;

/// <summary>
/// For throwaway TEST scenes (Planet Gallery, Tree Gallery): keeps the game's
/// auto-created HUDs off the screen.
///
/// Every auto-singleton in the project (helmet overlay, hotbar, oxygen bar,
/// phone, HAL line, ...) is created by a RuntimeInitializeOnLoadMethod that
/// only skips the MainMenu scene, so pressing Play in ANY other scene spawns
/// the lot as DontDestroyOnLoad objects and they draw over the view. Rather
/// than teach ~90 singletons about gallery scenes, this component switches
/// off every Canvas that does not belong to the scene it lives in. It runs
/// every frame in LateUpdate so a HUD that re-enables its own canvas in
/// Update loses the argument — no flicker. The scan is a few hundred objects
/// in a scene this small; it is not a gameplay component.
/// </summary>
public class GallerySceneQuiet : MonoBehaviour
{
    Canvas[] _cache;
    int _frame;

    void LateUpdate()
    {
        // Re-scan every 30 frames (singletons can appear a little after load),
        // but re-assert on the cached list every frame.
        if (_cache == null || ++_frame % 30 == 0)
            _cache = FindObjectsOfType<Canvas>(true);

        var myScene = gameObject.scene;
        for (int i = 0; i < _cache.Length; i++)
        {
            var c = _cache[i];
            if (c == null || !c.enabled) continue;
            if (c.gameObject.scene == myScene) continue;
            c.enabled = false;
        }
    }
}
