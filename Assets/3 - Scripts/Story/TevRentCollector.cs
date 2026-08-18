using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds Tev's lawn rent to the tab once per game day.
///
/// The rate is whatever the player haggled him down to on the first talk
/// ($50 / $30 / $20 / $10 per day — see MushroomQuest.RentRungs). This class
/// only bills.
///
/// It listens to GalaxyTime.OnDayChanged rather than running its own timer, so
/// the tab is tied to the same clock the player can see in the corner: if the
/// HUD says DAY 8, seven days of rent have been charged.
///
/// ── It does NOT take your money ──────────────────────────────────────────
/// The old weekly version reached into the wallet on its own. This one only
/// grows a balance; nothing is deducted until the player walks up to Tev and
/// hands it over through TevPaymentUI. That is deliberate, and it is what makes
/// the five-day plugin lockout mean something: a rich player who never visits
/// his landlord gets locked out exactly like a broke one, which an auto-
/// deducting collector could never express.
///
/// ── Still deliberately not an eviction system ────────────────────────────
/// A player who can't (or won't) pay accrues a balance, gets nagged, and loses
/// access to PLUGINS — never to blank tapes. Nothing repossesses the shuttle,
/// locks them out of the world, or fails the run. Eviction would need somewhere
/// to evict them TO, a way to earn back in, and a story beat for Tev turning on
/// them — none of which exist, and a demo that can soft-lock a broke player is
/// worse than one that just nags.
/// </summary>
public class TevRentCollector : MonoBehaviour
{
    public static TevRentCollector Instance { get; private set; }

    /// Balance at which the notice starts sounding like a threat rather than a
    /// reminder. Expressed in days so it tracks the haggled rate.
    const int SternAfterDays = 3;

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

        // ⚠️ CO-OP: ONE household, ONE bill. The clock is synced, so this fires
        // on both machines within a frame of each other — a guest that also
        // accrued would double-charge the debt until the host's snapshot
        // overwrote it, and the player would watch the number jump.
        //
        // The guest still needs to SEE the bill, so it waits for the host's
        // number to land and then reads it off. Decision path host-only,
        // rendering path both — the usual split.
        if (!WorldSync.IsAuthority) { StartCoroutine(ShowRemoteBill()); return; }

        // AccrueRentTo owns the "which days are still unbilled" question, so a
        // load that skipped several days lands as one linear charge rather than
        // a stack of separate ones — or, worse, a double bill for one day.
        int charged = MushroomQuest.AccrueRentTo(day);
        if (charged <= 0) return;

        int owed = MushroomQuest.RentBalance;
        int unpaid = MushroomQuest.UnpaidDays;

        if (unpaid >= MushroomQuest.LockoutDays)
        {
            StoryImpactNotice.Show(
                $"RENT — ${owed} owed. Tev has stopped selling you plugins.", 6f);
        }
        else if (unpaid >= SternAfterDays)
        {
            StoryImpactNotice.Show(
                $"RENT — ${owed} owed to Tev. {unpaid} days behind.", 6f);
        }
        else
        {
            StoryImpactNotice.Show($"RENT — ${owed} owed to Tev.", 5f);
        }
    }

    /// <summary>
    /// The guest's copy of the bill. The host charges on its own day roll and
    /// broadcasts within a quarter-second; two seconds is comfortably past that
    /// even on a bad relay, and the notice reads the replicated numbers rather
    /// than computing anything of its own.
    ///
    /// Says nothing if the balance is clear — a guest who joined a paid-up
    /// household shouldn't be told about a bill that never landed.
    /// </summary>
    System.Collections.IEnumerator ShowRemoteBill()
    {
        yield return new WaitForSecondsRealtime(2f);

        int owed = MushroomQuest.RentBalance;
        if (owed <= 0) yield break;

        int unpaid = MushroomQuest.UnpaidDays;
        if (unpaid >= MushroomQuest.LockoutDays)
            StoryImpactNotice.Show($"RENT — ${owed} owed. Tev has stopped selling you plugins.", 6f);
        else if (unpaid >= SternAfterDays)
            StoryImpactNotice.Show($"RENT — ${owed} owed to Tev. {unpaid} days behind.", 6f);
        else
            StoryImpactNotice.Show($"RENT — ${owed} owed to Tev.", 5f);
    }
}
