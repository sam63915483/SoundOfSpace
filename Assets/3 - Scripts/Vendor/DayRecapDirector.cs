using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The day tick for the selling loop (loop-feel passes B + C): at every
/// GalaxyTime day change it
///
///   1. decays craving for every buyer who bought nothing that day and
///      clears the pacing gate for obsessed (90+) regulars — their
///      guaranteed at-least-one-order-a-day,
///   2. composes the DAY WRAP phone message (a frozen snapshot text on the
///      system:wrap pseudo-thread — informational only, rent still moves
///      exclusively through Tev's payment screen),
///   3. resets the day's running totals.
///
/// Runs one frame AFTER the day event so every other subscriber (rent
/// billing in TevRentCollector) has already updated the numbers the wrap
/// reports. Host-only: craving and the ledger ride the economy snapshot.
///
/// Auto-singleton; ALSO seeded in MainMenuController.EnsureGameplaySingletons
/// (CLAUDE.md trap #1 — RuntimeInitializeOnLoadMethod never fires post-menu
/// in builds).
/// </summary>
public class DayRecapDirector : MonoBehaviour
{
    public static DayRecapDirector Instance { get; private set; }

    /// How many "getting hungry" names the wrap message lists at most.
    const int MaxHungryNames = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        new GameObject("[DayRecapDirector]").AddComponent<DayRecapDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()  { GalaxyTime.OnDayChanged += HandleDayChanged; }
    void OnDisable() { GalaxyTime.OnDayChanged -= HandleDayChanged; }

    void HandleDayChanged(int newDay)
    {
        if (!WorldSync.IsAuthority) return;
        StartCoroutine(AfterOtherDayHandlers(newDay));
    }

    IEnumerator AfterOtherDayHandlers(int newDay)
    {
        // Let the same-event subscribers finish first — rent bills on this
        // exact event and the wrap must report the POST-billing balance.
        yield return null;

        int endedDay = Mathf.Max(1, newDay - 1);

        // ── craving decay + the obsessed guarantee (loop-feel C) ─────────
        string hungry = "";
        int hungryCount = 0;
        if (FeatureVault.CravingSystem)
        {
            foreach (var b in BuyerLedger.All())
            {
                if (!BuyerLedger.Eligible(b.id)) continue;
                // A purchase on the day that just ended skips that day's
                // decay; anyone ignored cools off.
                if (b.lastPurchaseDay < endedDay)
                    b.craving = CravingRules.AfterIdleDay(b.craving);

                // Obsessed regulars text at least once a day: clear the
                // pacing gate so the director's next tick sends theirs.
                if (b.craving >= CravingRules.GuaranteedOrderThreshold
                    && b.isRegular && b.convo == BuyerLedger.Convo.None)
                    b.nextTextAt = 0f;

                if (b.craving >= CravingRules.AmbushThreshold && hungryCount < MaxHungryNames)
                {
                    hungry += (hungryCount == 0 ? "" : ", ") + AlienNames.For(b.id);
                    hungryCount++;
                }
            }
            BuyerLedger.Touch();
        }

        // ── the wrap message (loop-feel B) ───────────────────────────────
        string bonds = "";
        for (int i = 0; i < BuyerLedger.DayBondUps.Count && i < 4; i++)
            bonds += (i == 0 ? "" : ", ") + AlienNames.For(BuyerLedger.DayBondUps[i]);

        // -1 = "no rent arrangement" — Compose omits the line entirely. With
        // rent vaulted that is always the case, and DayRecap.cs (Unity-free,
        // suite-covered) never has to know the vault exists.
        int rentOwed = FeatureVault.TevRent && MushroomQuest.RentSettled ? MushroomQuest.RentBalance : -1;
        int daysToLockout = Mathf.Max(0, MushroomQuest.LockoutDays - MushroomQuest.UnpaidDays);
        string text = DayRecap.Compose(endedDay,
                                       BuyerLedger.DayTapesSold, BuyerLedger.DayEarned,
                                       rentOwed, daysToLockout, MushroomQuest.PluginsLocked,
                                       bonds, hungry);

        var wrap = BuyerLedger.GetOrCreate(BuyerLedger.WrapThreadId);
        BuyerLedger.Log(wrap, BuyerLedger.EvType.DayRecap,
                        BuyerLedger.DayTapesSold, BuyerLedger.DayEarned, endedDay, s: text);
        BuyerLedger.ResetDayTotals();

        var phone = PlayerPhoneUI.Instance;
        if (phone != null) phone.FlashNotification($"day {endedDay} wrap");
    }
}
