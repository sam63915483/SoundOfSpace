using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Interactable : MonoBehaviour {

    // Kept for backwards compatibility with prefabs that serialize a custom
    // string. Default builds the prompt from PromptGlyphs at runtime so it
    // tracks the current input source. Subclasses that compose their own
    // message (HatchButton, SunlightControlButton) override BuildInteractMessage.
    public string interactMessage = "";
    protected bool playerInInteractionZone;
    public UnityEngine.Events.UnityEvent interactEvent;
    static GameObject _cachedPlayerGO;

    // True only while the player GameObject exists AND is active. Returns
    // false during piloting (Ship.PilotShip disables the player), so the
    // prompt-refresh loop below stops asserting "Press F to interact" while
    // the player is in the cockpit.
    static bool IsPlayerActive () {
        if (_cachedPlayerGO == null) _cachedPlayerGO = GameObject.FindGameObjectWithTag ("Player");
        return _cachedPlayerGO != null && _cachedPlayerGO.activeInHierarchy;
    }

    protected virtual void Interact () {
        if (interactEvent != null) {
            interactEvent.Invoke ();
        }
    }

    protected virtual bool CanInteract () => true;

    protected virtual void Update () {
        // F keyboard OR controller X button. Intentionally NOT gated through
        // TutorialGate here — the original behavior had no ability gate (subclass
        // CanInteract() does any gating it needs), so we preserve that.
        bool interactDown = Input.GetKeyDown (KeyCode.F) ||
            TutorialGate.PadPressed (TutorialGate.PadButton.X);
        if (playerInInteractionZone && CanInteract () && interactDown) {
            Interact ();
            // Pickup-style Interactables Destroy(gameObject) inside Interact().
            // The Unity-null check bails before the re-assert below would push a
            // stale prompt for a now-destroyed owner that nothing will Clear.
            if (this == null) return;
        }
        // Re-assert ownership of the prompt every frame the player is in the
        // zone. This guarantees the prompt stays visible no matter what other
        // events fire (floating-origin shifts that briefly retrigger
        // Enter/Exit, neighbouring Interactables claiming ownership and being
        // walked away from, the legacy 3s timer expiring, etc.). The
        // IsPlayerActive() check stops the refresh while the player is
        // piloting (player GameObject inactive, OnTriggerExit doesn't fire,
        // so playerInInteractionZone stays stuck true).
        if (playerInInteractionZone && CanInteract () && IsPlayerActive ()) {
            GameUI.ShowInteractionPrompt (this, BuildInteractMessage ());
        }
    }

    protected virtual void OnTriggerEnter (Collider c) {
        if (c.CompareTag ("Player")) {
            playerInInteractionZone = true;
            ShowInteractMessage ();
        }
    }

    protected virtual void OnTriggerExit (Collider c) {
        if (c.CompareTag ("Player")) {
            playerInInteractionZone = false;
            GameUI.ClearInteractionPrompt (this);
        }
    }

    // Subclasses override to compose contextual prompts (e.g. "open hatch"
    // vs "close hatch"). Default uses the serialized custom message if set,
    // otherwise builds the standard "Press F to interact" prompt.
    //
    // Author-set strings (set in the prefab inspector — e.g. "Press F to pilot
    // ship") are passed through SubstituteInteractGlyph so the keyboard "F"
    // swaps to the controller "X" glyph live. Without this, prefab-authored
    // prompts would never update on input source change while subclasses that
    // build their string from PromptGlyphs (HatchButton, SunlightControlButton)
    // would.
    protected virtual string BuildInteractMessage () {
        if (string.IsNullOrEmpty (interactMessage) || interactMessage == "#set from script#")
            return $"Press {PromptGlyphs.Interact} to interact";
        return SubstituteInteractGlyph (interactMessage);
    }

    static readonly System.Text.RegularExpressions.Regex _orXButtonSuffix =
        new System.Text.RegularExpressions.Regex (@"\s*\(or\s+X\s+button\)");
    static readonly System.Text.RegularExpressions.Regex _interactKeyToken =
        new System.Text.RegularExpressions.Regex (@"\b[Ff]\b");

    // Replace the literal "F" interaction-key glyph in an author-set string
    // with the current input source's glyph (PromptGlyphs.Interact returns
    // either "<b>F</b>" or "<b>X</b>"). Also strips a legacy "(or X button)"
    // suffix the original prompt strings used to disambiguate inputs.
    protected static string SubstituteInteractGlyph (string s) {
        if (string.IsNullOrEmpty (s)) return s;
        s = _orXButtonSuffix.Replace (s, "");
        s = _interactKeyToken.Replace (s, PromptGlyphs.Interact);
        return s;
    }

    protected virtual void ShowInteractMessage () {
        GameUI.ShowInteractionPrompt (this, BuildInteractMessage ());
    }

    public void ForcePlayerInInteractionZone () {
        playerInInteractionZone = true;
    }

    // Force the in-zone flag false. Used by SaveSystem when it teleports the
    // player away from the ship via rb.position — Unity's OnTriggerExit doesn't
    // fire on a teleport, so the flag would otherwise remain stuck true and
    // the next interact-button press would fire Interact() against an NPC /
    // ship the player is no longer physically near.
    public void ClearPlayerInInteractionZone () {
        playerInInteractionZone = false;
        GameUI.ClearInteractionPrompt (this);
    }

}