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
        // Gaze gate (#2): F only fires while the player is actually looking at
        // this object, matching the prompt the gaze gate (#1) shows.
        if (playerInInteractionZone && CanInteract () && interactDown && InteractGaze.IsLookingAt (this)) {
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
        else if (playerInInteractionZone) {
            // In the zone but currently NOT interactable (e.g. the hatch is
            // already open → CanInteract false, or the player is piloting).
            // Actively clear our prompt: just ceasing to re-assert isn't enough,
            // because the continuous gaze gate keeps re-displaying the last owner
            // until it's explicitly cleared. Owner-scoped, so it won't touch
            // another interactable's prompt.
            GameUI.ClearInteractionPrompt (this);
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

    // When false, this interactable skips the look-to-interact gaze gate and
    // behaves like the classic radius-only prompt — the prompt + F work the
    // moment the player is in the trigger zone, no aiming required. Used by
    // "Press F to pilot" (you want it the instant you're in the cockpit).
    // (Appended at the end per the serialization convention in CLAUDE.md.)
    [Tooltip("Uncheck to ignore the look-at requirement — prompt + F work on radius alone (e.g. Press F to pilot).")]
    public bool requireGazeToInteract = true;

    [Tooltip("Optional: what the player must look at to interact. Leave null to use this object's own bounds. Set to a larger mesh (e.g. the hatch itself) when the interactable script lives on a small child button.")]
    public Transform gazeTarget;

    [Tooltip("If true, the look-at raycast ignores occluders (e.g. the hull) and only tests the gaze target's own colliders. Used to open the hatch from outside while standing under the closed ship.")]
    public bool gazeThroughWalls = false;

}