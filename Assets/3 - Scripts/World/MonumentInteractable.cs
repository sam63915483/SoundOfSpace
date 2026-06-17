using UnityEngine;

// A weathered stone monument carrying a song lyric. Walk up to it and the shared
// interaction prompt shows "Press F to play <song>"; pressing F opens a Yes/No
// confirmation (MonumentLinkPopupUI) that, on Yes, opens the song's link in the
// default browser.
//
// Reuses the engine's Interactable base for proximity (trigger collider),
// F/controller-X polling, and the shared prompt-pill UI. To add another monument:
// duplicate the prefab, swap the front-face text, and set songUrl + songLabel.
public class MonumentInteractable : Interactable {

    [Tooltip("YouTube (or any) URL opened in the default browser when the player confirms.")]
    public string songUrl = "https://www.youtube.com/watch?v=ajvk1CFIM1M";

    [Tooltip("Shown in the prompt: \"Press F to play <songLabel>\". Leave blank for a generic prompt.")]
    public string songLabel = "Not Now John";

    // Hide the prompt + block F while the confirm popup is open.
    protected override bool CanInteract () => !MonumentLinkPopupUI.IsOpen;

    protected override string BuildInteractMessage () {
        if (string.IsNullOrWhiteSpace (songLabel))
            return $"Press {PromptGlyphs.Interact} to play music";
        return $"Press {PromptGlyphs.Interact} to play {songLabel}";
    }

    protected override void Interact () {
        if (string.IsNullOrWhiteSpace (songUrl)) {
            Debug.LogWarning ($"[MonumentInteractable] '{name}' has no songUrl set.", this);
            return;
        }
        MonumentLinkPopupUI.Open (songUrl, songLabel);
    }
}
