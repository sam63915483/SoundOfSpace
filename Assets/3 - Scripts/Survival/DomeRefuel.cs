using UnityEngine;

/// <summary>
/// Refuel interaction for a bubble dome, modelled on ShipReactor. Lives on the
/// dome's status-screen object, which carries an isTrigger collider (the "walk
/// up to it" zone). When the player is in the zone, is LOOKING at the screen, and
/// has a crystal stack equipped in the hotbar, an "F to insert crystals" prompt
/// appears. Pressing F (or pad X) tops the dome's fuel toward 100% using held
/// crystals (partial fill — takes only as many as it needs, up to what you have).
/// </summary>
public class DomeRefuel : MonoBehaviour
{
    [Tooltip("Owning dome. Auto-resolved via GetComponentInParent<BubbleDome>() if null.")]
    public BubbleDome dome;

    [Header("Audio")]
    [Tooltip("Played when crystals are fed into the emitter.")]
    [SerializeField] private AudioClip feedClip;
    [SerializeField, Range(0f, 1f)] private float feedVolume = 0.7f;
    AudioSource _audio;

    bool _playerInZone;
    bool _promptShown;

    void Awake()
    {
        if (dome == null) dome = GetComponentInParent<BubbleDome>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other != null && other.CompareTag("Player")) _playerInZone = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other != null && other.CompareTag("Player")) { _playerInZone = false; HidePrompt(); }
    }

    void Update()
    {
        if (dome == null || !_playerInZone) { HidePrompt(); return; }

        // Prompt only when at least one WHOLE crystal's worth of fuel fits (so a
        // near-full tank can't eat a crystal for a sliver) AND the player is
        // holding crystals.
        bool eligible = dome.FuelPercent <= 100f - dome.FuelPerCrystal && IsPlayerHoldingCrystals();
        if (eligible)
        {
            ShowPrompt();
            // F (keyboard) OR pad X — but only if actually LOOKING at the screen
            // (mirrors ShipReactor / LootBox / ThrusterMount).
            if ((Input.GetKeyDown(KeyCode.F) || TutorialGate.PadPressed(TutorialGate.PadButton.X))
                && InteractGaze.IsLookingAt(this)) Refuel();
        }
        else
        {
            HidePrompt();
        }
    }

    static bool IsPlayerHoldingCrystals()
    {
        var hb = Hotbar.Instance;
        if (hb == null) return false;
        if (hb.GetEquippedSlotId() != Hotbar.ItemId.Crystal) return false;
        return hb.GetResourceTotal(Hotbar.ItemId.Crystal) > 0;
    }

    void Refuel()
    {
        if (dome == null) return;
        int need = dome.CrystalsToFull();
        if (need <= 0) return;

        var hb = Hotbar.Instance;
        if (hb == null) return;
        int available = hb.GetResourceTotal(Hotbar.ItemId.Crystal);
        int take = Mathf.Min(need, available);
        if (take <= 0) return;
        if (!hb.SpendResource(Hotbar.ItemId.Crystal, take)) return;

        dome.AddFuelFromCrystals(take);
        if (feedClip != null && _audio != null) _audio.PlayOneShot(feedClip, feedVolume);
        ReactorPopup.Spawn(transform.position + transform.up * 0.3f, take * dome.FuelPerCrystal);
    }

    void ShowPrompt()
    {
        if (_promptShown) return;
        GameUI.ShowInteractionPrompt(this, $"Press {PromptGlyphs.Interact} to insert crystals");
        _promptShown = true;
    }

    void HidePrompt()
    {
        if (!_promptShown) return;
        GameUI.ClearInteractionPrompt(this);
        _promptShown = false;
    }
}
