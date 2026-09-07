using UnityEngine;

// Lives on the Reactor child GameObject inside each ship prefab. The Reactor
// already has a BoxCollider with isTrigger=true. When the player walks into
// the trigger AND has a crystal stack equipped in the hotbar, an F-to-insert
// prompt appears. Pressing F drains crystals (partial fill: take all the
// player has up to the topup amount) and restores the ship's fuel.
public class ShipReactor : MonoBehaviour
{
    [Tooltip("Owning ship. Auto-resolved via GetComponentInParent<Ship>() if null. " +
             "Leave empty on the SHUTTLE's reactor — it has no Ship, and Awake falls " +
             "back to the shuttle's ShuttleFuel tank instead.")]
    public Ship ship;

    // The tank this reactor actually fills. Either the owning Ship or, on the
    // shuttle, its ShuttleFuel. Resolved in Awake and re-tried lazily, because
    // the shuttle's tank may be attached after this component wakes.
    IReactorFuel _tank;

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
        ResolveTank();
        if (glow == null) glow = GetComponent<ReactorGlow>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
    }

    /// <summary>Which tank this prop feeds: the ship it is bolted to, or — when it
    /// is the one inside the shuttle — the shuttle's own tank.</summary>
    void ResolveTank()
    {
        if (_tank != null && !_tank.Equals(null)) return;
        if (ship != null) { _tank = ship; return; }
        // Concrete type, deliberately: GetComponentInParent<T>() with an
        // INTERFACE does not find the tank here (verified — it returns null even
        // with ShuttleFuel sitting on the prefab root), so resolving through
        // IReactorFuel would leave the shuttle's reactor permanently dead.
        var shuttleTank = GetComponentInParent<ShuttleFuel>(true);
        if (shuttleTank != null) { _tank = shuttleTank; return; }
        _tank = ShuttleFuel.Instance;      // last resort: the live shuttle's tank
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
        if (!_playerInZone) { HidePrompt(); return; }
        ResolveTank();
        if (_tank == null) { HidePrompt(); return; }

        bool eligible = _tank.FuelPercent < 1f && IsPlayerHoldingCrystals();
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
        ResolveTank();
        if (_tank == null) return;
        float deficit = _tank.FuelMax - _tank.FuelPercent * _tank.FuelMax;
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
        // Co-op: the shuttle's tank is ONE shared number owned by the host, but
        // the crystals just spent were this player's own. A guest sends the
        // credit upstream instead of writing locally — a local write would be
        // wiped by the host's next heartbeat, eating the crystals.
        if (_tank is ShuttleFuel && ShuttleAutopilot.ClientDriven)
            ShuttleSync.SendAddFuel(fuelAdded);
        else
            _tank.RestoreFuel(fuelAdded);
        ReactorPopup.Spawn(transform.position + transform.up * 0.5f, fuelAdded);
        if (glow != null) glow.PingFlash();
    }

    void ShowPrompt()
    {
        // Re-assert EVERY frame rather than latching on the first one
        // (2026-09-07 playtest: look away from the reactor and back and the
        // prompt never returned; swapping hotbar items fixed it).
        //
        // InteractPromptUI is a single shared prompt with one owner. Look away
        // and any other candidate is allowed to take ownership — and once it
        // does, a latched "_promptShown = true" meant this reactor never called
        // Show again, so it could never win the prompt back. Switching items
        // made the eligibility check fail, which called HidePrompt, which reset
        // the latch — which is precisely why that worked around it.
        //
        // Show() is cheap (it just sets the owner + text; the gaze gating and
        // the actual show/hide happen in InteractPromptUI.Update), and that
        // class explicitly supports being re-asserted every frame.
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
