using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Centralized scene-aware gate for HUD canvases that live as
/// DontDestroyOnLoad singletons. Each HUD registers its Canvas at build time
/// and we toggle them in lockstep based on whether the active scene is the
/// main menu — so HUDs stay hidden when the player exits gameplay back to the
/// menu, and they don't flash on-screen during the menu→gameplay transition.
///
/// The list is pruned of destroyed canvases on every callback, so registered
/// canvases that die never leak. There is no Unregister API — HUDs are
/// effectively permanent for the lifetime of the process.
/// </summary>
public static class HUDSceneGate
{
    const string MainMenuSceneName = "MainMenu";

    static readonly List<Canvas> _canvases = new List<Canvas>();
    static bool _subscribed;

    public static void Register(Canvas canvas)
    {
        if (canvas == null) return;
        if (!_subscribed)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _subscribed = true;
        }
        if (!_canvases.Contains(canvas)) _canvases.Add(canvas);
        canvas.enabled = !IsMainMenu(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool hide = IsMainMenu(scene);
        for (int i = _canvases.Count - 1; i >= 0; i--)
        {
            var c = _canvases[i];
            if (c == null) { _canvases.RemoveAt(i); continue; }
            c.enabled = !hide;
        }
    }

    static bool IsMainMenu(Scene scene) => scene.name == MainMenuSceneName;
}
