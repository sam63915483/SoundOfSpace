using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Charges Tev's lawn rent once a galactic week.
///
/// The rate is whatever the player haggled him down to in the first-talk beat
/// (500 / 100 / free — see MushroomQuest.SettleRent). This class only collects.
///
/// It listens to GalaxyTime.OnDayChanged rather than running its own timer, so
/// the bill is tied to the same clock the player can see in the corner: if the
/// HUD says DAY 8, rent has been taken for the week.
///
/// ── Deliberately not an eviction system ──────────────────────────────────
/// A player who can't pay accrues ARREARS and gets a nagging notice. Nothing
/// repossesses the shuttle, locks them out, or fails the run. Eviction would
/// need somewhere to evict them TO, a way to earn back in, and a story beat for
/// Tev turning on them — none of which exist, and a demo that can soft-lock a
/// broke player on their first week is worse than one that just nags.
/// Arrears are collected on top of the next bill when they can afford it.
/// </summary>
public class TevRentCollector : MonoBehaviour
{
    public static TevRentCollector Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: never fires in a build — also seeded from
        // MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("TevRentCollector");
        DontDestroyOnLoad(go);
        go.AddComponent<TevRentCollector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()  { GalaxyTime.OnDayChanged += HandleDayChanged; }
    void OnDisable() { GalaxyTime.OnDayChanged -= HandleDayChanged; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void HandleDayChanged(int day)
    {
        if (!MushroomQuest.RentSettled) return;
        if (MushroomQuest.RentPerWeek <= 0) return;      // Tev waived it

        int due = MushroomQuest.RentNextDueDay;
        if (due <= 0 || day < due) return;

        // Roll the schedule forward first, so a long absence (or a save that
        // skipped several due days at once) can't bill the player twice for the
        // same week — it lands as one bill plus arrears, not a stack of them.
        int next = due;
        while (next <= day) next += GalaxyTime.DaysPerWeek;
        MushroomQuest.RentNextDueDay = next;

        Charge();
    }

    void Charge()
    {
        int owed = MushroomQuest.RentPerWeek + MushroomQuest.RentArrears;
        if (owed <= 0) return;

        var wallet = PlayerWallet.Instance;
        int have = wallet != null ? wallet.Money : 0;
        int paid = Mathf.Clamp(owed, 0, have);
        if (paid > 0 && wallet != null) wallet.SpendMoney(paid);

        int shortfall = owed - paid;
        MushroomQuest.RentArrears = shortfall;

        if (shortfall <= 0)
        {
            StoryImpactNotice.Show($"RENT PAID — {paid} credits to Tev.", 5f);
        }
        else if (paid > 0)
        {
            StoryImpactNotice.Show(
                $"RENT — paid {paid}, still owe Tev {shortfall}.", 6f);
        }
        else
        {
            StoryImpactNotice.Show(
                $"RENT OVERDUE — you owe Tev {shortfall} credits.", 6f);
        }
    }
}
