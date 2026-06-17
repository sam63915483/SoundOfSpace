using UnityEngine;

// "Press F to read" on a stack of papers. Subclasses the shared Interactable so
// it reuses the exact proximity trigger, prompt-pill, and F/controller-X input
// the rest of the game uses — no parallel interaction system. Each prop points
// at its own NewspaperArticleSet, so a new table = new asset + new prop, no code.
public class NewspaperInteractable : Interactable {

    [Tooltip("The clippings shown when the player reads this stack.")]
    public NewspaperArticleSet articleSet;

    [Tooltip("Trigger radius added at runtime only if the prop has no trigger collider.")]
    public float fallbackTriggerRadius = 3.0f;

    void Awake() {
        bool hasTrigger = false;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { hasTrigger = true; break; }
        if (!hasTrigger) {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = fallbackTriggerRadius;
        }
    }

    // Hides the prompt AND blocks F while the reader is open (the base re-assert
    // loop and F-poll both gate on CanInteract).
    protected override bool CanInteract() =>
        articleSet != null && articleSet.articles != null && articleSet.articles.Count > 0
        && !NewspaperReaderUI.IsOpen;

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to read";

    protected override void Interact() {
        NewspaperReaderUI.Open(articleSet);
    }
}
