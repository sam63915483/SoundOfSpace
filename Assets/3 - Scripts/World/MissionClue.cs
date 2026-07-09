using System.Collections;
using UnityEngine;

/// <summary>
/// A look-at + F interactable clue for "Cold Company" (Main Mission 1). Attach to a visible
/// base prop (photo wall, terminal, the file on the table, the star-chart). On interact it
/// opens the shared <see cref="NoteReadUI"/> readable (title + body, plus an optional photo),
/// and on close records the matching mission flag via <see cref="ColdCompany"/>.
///
/// Mirrors the vendor interaction pattern (gaze gate + F + InteractPromptUI). If the object has
/// no renderer (a bare empty), the gaze requirement is auto-dropped so proximity + F still work.
/// The pod file is soft-gated: it won't open until the photo wall + review station are seen.
/// See docs/GDD_VerticalSlice_Main1_ColdCompany.md §4.
/// </summary>
public class MissionClue : MonoBehaviour
{
    public enum ClueKind { PhotoWall, ReviewStation, PodFile, ScrubbedRoute, Keepsake, CensoredScan }

    [Header("Clue")]
    [Tooltip("Which clue this is. Drives the mission flag it sets (and PodFile's gate + first-lie beat).")]
    public ClueKind kind = ClueKind.PhotoWall;
    public string title = "Clue";
    [TextArea(3, 12)] public string body = "";
    [Tooltip("Optional photo shown above the text (e.g. the pod-crash still for the pod file). Leave empty for text-only.")]
    public Sprite image;

    [Header("Interaction")]
    [Tooltip("Proximity radius. A trigger SphereCollider is auto-added at this radius if the object has no collider.")]
    public float interactRadius = 3f;

    [Header("Flip-open (optional — e.g. the pod file's cover)")]
    [Tooltip("A cover/lid transform to rotate open before the readable appears. Leave null to skip.")]
    public Transform flipCover;
    [Tooltip("Local euler angles the cover rotates to when opening.")]
    public Vector3 flipOpenLocalEuler = new Vector3(-110f, 0f, 0f);
    [Tooltip("Seconds the flip-open takes.")]
    public float flipTime = 1f;

    bool _playerInRange;
    bool _open;
    bool _requireGaze;

    void Reset()
    {
        var sc = GetComponent<SphereCollider>();
        if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = interactRadius;
    }

    void Awake()
    {
        if (GetComponent<Collider>() == null)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = interactRadius;
        }
        // Only require precise look-at if there's a mesh to look at; bare empties fall back to
        // proximity + F (otherwise the gaze cone is a near-impossible pinpoint).
        _requireGaze = GetComponentInChildren<Renderer>() != null;
    }

    void OnTriggerEnter(Collider other) { if (other.CompareTag("Player")) _playerInRange = true; }
    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
    }

    void Update()
    {
        if (!_playerInRange || _open) return;
        if (NoteReadUI.Instance != null && NoteReadUI.Instance.IsOpen) return;

        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to inspect");

        if ((!_requireGaze || InteractGaze.IsLookingAt(this)) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
            Interact();
    }

    void Interact()
    {
        // The pod file is the climax — gated behind the other two clues so it lands last.
        if (kind == ClueKind.PodFile && !ColdCompany.CanOpenPodFile())
        {
            HALCommentator.Instance?.VolunteerExternal(
                "A sealed file. I'd look over the rest of the base before opening that.", true);
            return;
        }

        _open = true;
        InteractPromptUI.Clear(this);

        if (flipCover != null)
            StartCoroutine(FlipThenShow());
        else
            ShowReadable();
    }

    IEnumerator FlipThenShow()
    {
        Quaternion from = flipCover.localRotation;
        Quaternion to = Quaternion.Euler(flipOpenLocalEuler);
        float t = 0f;
        float dur = Mathf.Max(0.01f, flipTime);
        while (t < dur)
        {
            t += Time.deltaTime;
            flipCover.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
            yield return null;
        }
        flipCover.localRotation = to;
        ShowReadable();
    }

    void ShowReadable()
    {
        if (NoteReadUI.Instance != null)
            NoteReadUI.Instance.ShowNote(title, body, image, OnClosed);
        else
            OnClosed();
    }

    void OnClosed()
    {
        _open = false;
        switch (kind)
        {
            case ClueKind.PhotoWall:     ColdCompany.NotifyPhotoWall();     break;
            case ClueKind.ReviewStation: ColdCompany.NotifyReviewStation(); break;
            case ClueKind.PodFile:       ColdCompany.NotifyPodFileOpened(); break;
            case ClueKind.ScrubbedRoute: ColdCompany.NotifyScrubbedRoute(); break;
            // Keepsake / CensoredScan are optional flavor — no mission flag.
        }
    }

    void OnDisable() { InteractPromptUI.Clear(this); }
}
