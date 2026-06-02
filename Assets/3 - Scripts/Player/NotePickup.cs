using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drop-in pickup for an in-world note. Inherits from Interactable so it uses
// the same screen-space prompt UI as the ship hatch button, NPC dialogues, etc.
//
// On F (or controller X), opens NoteReadUI with this note's title + body and
// marks the note as read. The pickup stays in the world so the player can
// re-read it; MarkRead is idempotent.
//
// Also spawns a small world-space "paper" visual at runtime so the player can
// physically see the note lying on the table — paper-cream background, title
// at the top, body preview below.
public class NotePickup : Interactable
{
    [Tooltip("Unique ID for this note. Used as the key for NoteCollection.MarkRead and for tutorial steps that gate on Has(id). Tev's intro note should be 'tev_intro'.")]
    public string noteId = "tev_intro";

    [Tooltip("Title shown at the top of the note panel.")]
    public string title = "From Tev";

    [TextArea(5, 20)]
    [Tooltip("Body of the note. Shown via typewriter. Supports basic TextMesh Pro rich text tags (<b>, <i>, etc.).")]
    public string body = "Hey kid,\n\nFigured I'd be back before you woke up — turns out I'm not. Stuck out a couple days longer than planned, sorry about that.\n\nIf you're hungry, I left my fishing rod by the door. Head out to the bank and reel something up — it'll tide you over. There's a fire and a water bottle out by the bank too.\n\nI'll be home soon.\n\n— Tev";

    [Tooltip("Trigger radius if no Collider is present at Start. Ignored when a trigger collider already exists on this GameObject.")]
    public float fallbackTriggerRadius = 1.5f;

    [Header("Paper Visual")]
    [Tooltip("World-space size of the paper visual (X = width, Y = height in world units).")]
    public Vector2 paperSize = new Vector2(0.30f, 0.40f);
    [Tooltip("Lift the paper a tiny amount above the table surface to avoid z-fighting.")]
    public float paperLift = 0.01f;

    void Awake()
    {
        // Ensure at least one trigger collider exists so OnTriggerEnter fires.
        // Mirrors the convention used by NPCDialogue / BonfireInteraction etc.
        // where the user is expected to set up a trigger but we add one as a
        // safety net.
        bool anyTrigger = false;
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.isTrigger) { anyTrigger = true; break; }
        if (!anyTrigger)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = fallbackTriggerRadius;
        }

        if (transform.Find("__PaperVisual") == null) BuildPaperVisual();
    }

    void BuildPaperVisual()
    {
        var paperGO = new GameObject("__PaperVisual");
        paperGO.transform.SetParent(transform, false);
        // Lay flat: rotate so the canvas's +Z (outward normal) points up in world.
        paperGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        paperGO.transform.localPosition = new Vector3(0f, paperLift, 0f);
        const float worldPerCanvasUnit = 0.01f;
        paperGO.transform.localScale = Vector3.one * worldPerCanvasUnit;

        var canvas = paperGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        paperGO.AddComponent<CanvasScaler>();
        paperGO.AddComponent<GraphicRaycaster>();

        var canvasRT = paperGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = paperSize / worldPerCanvasUnit;
        canvasRT.pivot = new Vector2(0.5f, 0.5f);

        // Paper background — warm cream with a subtle outer shadow.
        var bgGO = new GameObject("__Bg", typeof(RectTransform));
        bgGO.transform.SetParent(paperGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.96f, 0.92f, 0.80f, 1f);
        bgImg.raycastTarget = false;
        var bgShadow = bgGO.AddComponent<Shadow>();
        bgShadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        bgShadow.effectDistance = new Vector2(2f, -2f);

        // Title at the top of the paper.
        var titleGO = new GameObject("__Title", typeof(RectTransform));
        titleGO.transform.SetParent(paperGO.transform, false);
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -3f);
        titleRT.sizeDelta = new Vector2(-6f, 5f);
        var titleText = titleGO.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(titleText);
        titleText.text = title;
        titleText.fontSize = 3.5f;
        titleText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        titleText.characterSpacing = 4f;
        titleText.color = new Color(0.18f, 0.12f, 0.08f, 1f);
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.raycastTarget = false;
        titleText.enableWordWrapping = false;

        // Thin divider under the title.
        var divGO = new GameObject("__Divider", typeof(RectTransform));
        divGO.transform.SetParent(paperGO.transform, false);
        var divRT = divGO.GetComponent<RectTransform>();
        divRT.anchorMin = new Vector2(0f, 1f);
        divRT.anchorMax = new Vector2(1f, 1f);
        divRT.pivot = new Vector2(0.5f, 1f);
        divRT.anchoredPosition = new Vector2(0f, -8.5f);
        divRT.sizeDelta = new Vector2(-10f, 0.4f);
        var divImg = divGO.AddComponent<Image>();
        divImg.color = new Color(0.25f, 0.18f, 0.10f, 0.6f);
        divImg.raycastTarget = false;

        // Body — full text, wrapped, in a small handwriting-feel font weight.
        var bodyGO = new GameObject("__Body", typeof(RectTransform));
        bodyGO.transform.SetParent(paperGO.transform, false);
        var bodyRT = bodyGO.GetComponent<RectTransform>();
        bodyRT.anchorMin = Vector2.zero;
        bodyRT.anchorMax = Vector2.one;
        bodyRT.offsetMin = new Vector2(3f, 3f);
        bodyRT.offsetMax = new Vector2(-3f, -10f);
        var bodyText = bodyGO.AddComponent<TextMeshProUGUI>();
        ApplyDefaultFont(bodyText);
        bodyText.text = body;
        bodyText.fontSize = 2.3f;
        bodyText.color = new Color(0.20f, 0.14f, 0.10f, 1f);
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.lineSpacing = 0f;
        bodyText.enableWordWrapping = true;
        bodyText.raycastTarget = false;
        // Crop ellipses if the body overflows the paper.
        bodyText.overflowMode = TextOverflowModes.Ellipsis;
    }

    static TMP_FontAsset _paperFont;
    static bool _paperFontResolved;

    static void ApplyDefaultFont(TextMeshProUGUI t)
    {
        if (!_paperFontResolved)
        {
            _paperFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _paperFontResolved = true;
        }
        if (_paperFont != null) t.font = _paperFont;
    }

    // Suppress the prompt and the F-key while NoteReadUI is open — pressing F
    // again while reading shouldn't re-open the panel, and the prompt doesn't
    // need to be visible behind the fullscreen note.
    protected override bool CanInteract()
    {
        if (NoteReadUI.Instance != null && NoteReadUI.Instance.IsOpen) return false;
        return TutorialGate.IsUnlocked(TutorialAbility.Pickup);
    }

    protected override string BuildInteractMessage() =>
        $"Press {PromptGlyphs.Interact} to read";

    protected override void Interact()
    {
        base.Interact(); // fires interactEvent if the user hooked anything up
        if (NoteReadUI.Instance == null)
        {
            Debug.LogWarning("[NotePickup] No NoteReadUI singleton found — cannot open note.");
            return;
        }
        NoteCollection.MarkRead(noteId);
        NoteReadUI.Instance.ShowNote(title, body, OnNoteClosed);
    }

    void OnNoteClosed()
    {
        // Once the note panel closes, re-show the interaction prompt if the
        // player is still standing in the trigger zone. Interactable.Update
        // will re-assert this anyway next frame, but doing it immediately
        // avoids a one-frame gap.
        if (playerInInteractionZone) ShowInteractMessage();
    }
}
