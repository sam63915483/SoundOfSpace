using UnityEngine;

public class GameUI : MonoBehaviour {

    // Kept for backwards compat with scenes that wire this field, but the
    // new InteractPromptUI singleton is the visible prompt. We hide the
    // legacy text on first frame so it doesn't double-render.
    public TMPro.TMP_Text interactionInfo;

    static GameUI instance;
    bool _legacyHidden;

    void Update () {
        if (!_legacyHidden && interactionInfo != null) {
            interactionInfo.gameObject.SetActive(false);
            _legacyHidden = true;
        }
    }

    /// <summary>
    /// Sticky prompt owned by `owner`. Stays visible until the same owner
    /// calls ClearInteractionPrompt or another owner takes over.
    /// </summary>
    public static void ShowInteractionPrompt (Object owner, string info) {
        InteractPromptUI.Show(owner, info);
    }

    /// <summary>Clear the prompt iff `owner` is the current owner.</summary>
    public static void ClearInteractionPrompt (Object owner) {
        InteractPromptUI.Clear(owner);
    }

    /// <summary>
    /// Legacy one-shot prompt with 3 s auto-hide. Clears any current owner.
    /// Prefer ShowInteractionPrompt / ClearInteractionPrompt for in-zone prompts.
    /// </summary>
    public static void DisplayInteractionInfo (string info) {
        InteractPromptUI.ShowOneShot(info, 3f);
    }

    public static void CancelInteractionDisplay () {
        // No-op: ShowOneShot self-hides; sticky prompts are cleared by their owner.
    }

    static GameUI Instance {
        get {
            if (instance == null) instance = FindObjectOfType<GameUI>();
            return instance;
        }
    }
}
