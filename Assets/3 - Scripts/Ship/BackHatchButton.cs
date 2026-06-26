using UnityEngine;

/// <summary>
/// Trigger interactable that toggles the rear hatch (BackHatch component).
/// Mirrors HatchButton's contextual-prompt pattern — shows "open" or "close"
/// based on the hatch's current state.
/// </summary>
public class BackHatchButton : Interactable
{
    [Header("Sound Effect")]
    [Tooltip("Played each time the hatch is toggled. Drop in a clip via the inspector — leave null for silent toggling.")]
    [SerializeField] AudioClip toggleClip;
    [SerializeField, Range(0f, 1f)] float toggleVolume = 0.7f;

    BackHatch _hatch;
    AudioSource _audioSource;

    BackHatch Hatch
    {
        get
        {
            // Resolve THIS button's hatch via parent walk so multiple ships
            // each get their own button → hatch binding. The legacy
            // FindObjectOfType lookup silently toggled the wrong ship's hatch.
            if (_hatch == null) _hatch = GetComponentInParent<BackHatch>();
            if (_hatch == null)
            {
                var ship = GetComponentInParent<Ship>();
                if (ship != null) _hatch = ship.GetComponentInChildren<BackHatch>(true);
            }
            return _hatch;
        }
    }

    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
    }

    // Only interactable while the hatch is closed — once it's open the prompt
    // shouldn't keep reappearing while you stand in the zone. (Suppresses both
    // the prompt and the F-press; the back hatch is opened, not toggled, here.)
    protected override bool CanInteract() => Hatch == null || !Hatch.IsOpen;

    protected override string BuildInteractMessage()
    {
        return $"Press {PromptGlyphs.Interact} to open back hatch";
    }

    protected override void Interact()
    {
        base.Interact();
        if (Hatch != null) Hatch.Toggle();
        if (toggleClip != null && _audioSource != null)
            _audioSource.PlayOneShot(toggleClip, toggleVolume);
        ShowInteractMessage();
    }

    void OnValidate()
    {
        interactMessage = "#set from script#";
    }
}
