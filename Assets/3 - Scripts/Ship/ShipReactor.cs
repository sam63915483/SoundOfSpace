using UnityEngine;

// Lives on the Reactor child GameObject inside each ship prefab. The Reactor
// already has a BoxCollider with isTrigger=true. When the player walks into
// the trigger AND has a crystal stack equipped in the hotbar, an F-to-insert
// prompt appears. Pressing F drains crystals (partial fill: take all the
// player has up to the topup amount) and restores the ship's fuel.
public class ShipReactor : MonoBehaviour
{
    [Tooltip("Owning ship. Auto-resolved via GetComponentInParent<Ship>() if null.")]
    public Ship ship;

    [Tooltip("Fuel units added per crystal. With Ship.fuelMax=100 and this=5, 20 crystals fill a full tank, 10 fill half.")]
    public float fuelPerCrystal = 5f;

    [Tooltip("Optional. Auto-resolved via GetComponent<ReactorGlow>() in Awake if null.")]
    public ReactorGlow glow;

    [Header("Audio")]
    [Tooltip("Played when crystals are fed into the reactor.")]
    [SerializeField] private AudioClip feedClip;
    [SerializeField, Range(0f, 1f)] private float feedVolume = 0.7f;
    AudioSource _audio;

    bool _playerInZone;
    bool _promptShown;

    void Awake()
    {
        if (ship == null) ship = GetComponentInParent<Ship>();
        if (glow == null) glow = GetComponent<ReactorGlow>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (!other.CompareTag("Player")) return;
        _playerInZone = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other == null) return;
        if (!other.CompareTag("Player")) return;
        _playerInZone = false;
        HidePrompt();
    }

    void Update()
    {
        if (ship == null || !_playerInZone) { HidePrompt(); return; }

        bool eligible = ship.FuelPercent < 1f && IsPlayerHoldingCrystals();
        if (eligible)
        {
            ShowPrompt();
            // F (keyboard) OR pad X — mirror the LootBox / ThrusterMount interact path.
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
        if (ship == null) return;
        float deficit = ship.fuelMax - ship.FuelPercent * ship.fuelMax;
        if (deficit <= 0f) return;
        int crystalsNeeded = Mathf.CeilToInt(deficit / fuelPerCrystal);
        if (crystalsNeeded <= 0) return;
        var hb = Hotbar.Instance;
        if (hb == null) return;
        int available = hb.GetResourceTotal(Hotbar.ItemId.Crystal);
        int take = Mathf.Min(crystalsNeeded, available);
        if (take <= 0) return;
        if (!hb.SpendResource(Hotbar.ItemId.Crystal, take)) return;
        if (feedClip != null && _audio != null) _audio.PlayOneShot(feedClip, feedVolume);
        float fuelAdded = take * fuelPerCrystal;
        ship.RestoreFuel(fuelAdded);
        ReactorPopup.Spawn(transform.position + transform.up * 0.5f, fuelAdded);
        if (glow != null) glow.PingFlash();
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
