using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's money — now a THIN VIEW over hotbar slot 8
/// (<see cref="Hotbar.MoneySlotIndex"/>), not a number of its own.
///
/// Money became a real item (2026-08-10): the stack count in that slot IS the
/// balance, so it can be dragged into a locker, split with the scroll wheel, and
/// handed to Tev like anything else. This class survives unchanged in shape on
/// purpose — roughly fifteen callers (vendors, the fish market, the guitar shop,
/// Tev's rent collector, the smuggling payout, cheats, HAL's status readout,
/// SaveCollector) still say PlayerWallet.Instance.AddMoney(...) and never learn
/// where it went. One representation, no second number to drift out of sync,
/// and no dupes.
///
/// The old top-left MONEY/AMMO HUD chips are long gone; the balance shows in
/// vendor screens via VendorMoneyBadge and now in the hotbar itself.
///
/// ── Boot-order note ──────────────────────────────────────────────────────
/// Both this and the Hotbar auto-create via RuntimeInitializeOnLoadMethod, and
/// the order between two such methods is undefined. So a write that lands
/// before the Hotbar exists is buffered and flushed the moment it appears —
/// otherwise SaveCollector applying `s.money` on a fast load could silently
/// write the balance into the void.
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    public static PlayerWallet Instance { get; private set; }

    // Only used before the Hotbar exists. Once it does, the slot is the truth
    // and this is never read again.
    int _pending;
    bool _hasPending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        GameObject go = new GameObject("PlayerWallet");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerWallet>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!_hasPending) return;
        var hb = Hotbar.Instance;
        if (hb == null) return;
        hb.SetMoney(_pending);
        _hasPending = false;
    }

    public int Money
    {
        get
        {
            var hb = Hotbar.Instance;
            if (hb == null) return _hasPending ? _pending : 0;
            // A buffered write that hasn't flushed yet is still the newest
            // value — reading around it would report a stale balance for a frame.
            return _hasPending ? _pending : hb.Money;
        }
    }

    public void AddMoney(int amount)
    {
        SetMoney(Money + amount);
        Debug.Log($"[PlayerWallet] +${amount}. Total: ${Money}");
    }

    /// Returns false and changes nothing if the player can't cover it — every
    /// purchase path relies on that, so it must stay all-or-nothing.
    public bool SpendMoney(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        SetMoney(Money - amount);
        return true;
    }

    public void SetMoney(int amount)
    {
        int v = Mathf.Max(0, amount);
        var hb = Hotbar.Instance;
        if (hb == null) { _pending = v; _hasPending = true; return; }
        hb.SetMoney(v);
        _hasPending = false;
    }
}
