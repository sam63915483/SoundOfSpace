using UnityEngine;

// Runs in the MainMenu scene. Destroys any gameplay singletons that survived a
// scene transition (PlayerWallet / TutorialUI both use DontDestroyOnLoad), so their
// HUDs don't render over the menu. Also resets timeScale and unlocks the cursor in
// case we just came back from a paused gameplay session.
public class MenuSceneCleanup : MonoBehaviour
{
    void Awake()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        foreach (var w in FindObjectsOfType<PlayerWallet>(true))
            Destroy(w.gameObject);

        foreach (var t in FindObjectsOfType<TutorialUI>(true))
            Destroy(t.gameObject);

        // The performance-review modal is DontDestroyOnLoad and survives a
        // pause-menu → main-menu transition. Without this, a review left
        // dangling in the gameplay scene draws over the menu.
        foreach (var pr in FindObjectsOfType<TutorialPerformanceReview>(true))
            Destroy(pr.gameObject);

        // PlayerWallet creates a separate HUD GameObject with its own DontDestroyOnLoad.
        var hud = GameObject.Find("WalletHUDCanvas");
        if (hud != null) Destroy(hud);
    }
}
