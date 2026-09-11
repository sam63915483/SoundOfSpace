using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>The three things a space cat can pay you for a fish.</summary>
public enum CatPerkKind
{
    None       = 0,
    Luck       = 1,   // sooner bites, better tiers, bigger fish -- all three
    WeightGain = 2,   // same fish, fatter. Tier odds untouched.
    Frenzy     = 3,   // bites land fast. Odds untouched -- volume only.
}

/// <summary>
/// The cat perk slot. Feed a cat any fish, it rolls one of nine outcomes
/// (3 perks x 3 durations) and you run it for 1, 2 or 3 minutes.
///
/// <b>ONE SLOT, and that is the whole design</b> (Sam, 2026-09-10). Perks never
/// stack. Rolling again while one is running is ALLOWED, and whatever comes out
/// REPLACES what you had -- the timer is set to the new tier's full length,
/// never topped up and never added to the old one. So a running 3-minute perk
/// can be thrown away by a 1-minute one, and that risk is the point: it is the
/// only gamble in the game.
///
/// <b>Magnitude is constant; the TIER is only duration.</b> A 3-minute Luck is a
/// longer 1-minute Luck, not a stronger one.
///
/// Everything the fishing code reads from here is a MULTIPLIER that is exactly
/// 1 (or 0) with no perk running, so every call site is a no-op until a cat has
/// actually paid out. Nothing in <see cref="FishingRules"/> changed behaviour:
/// the two new parameters there both default to "no perk".
/// </summary>
public class CatPerkManager : MonoBehaviour
{
    public static CatPerkManager Instance { get; private set; }

    // ── the one slot ─────────────────────────────────────────────────────
    public CatPerkKind ActiveKind { get; private set; }
    /// 0, 1 or 2 -> 1, 2 or 3 minutes.
    public int ActiveTier { get; private set; }
    public float SecondsLeft { get; private set; }
    public float TotalSeconds { get; private set; }
    public bool HasPerk => ActiveKind != CatPerkKind.None && SecondsLeft > 0f;

    /// Fires when a perk starts, is replaced, or runs out. The HUD chip listens.
    public event Action OnChanged;

    /// How many times rarer a 3-minute perk is than a 1-minute one. The ladder
    /// is geometric, so the 2-minute sits exactly halfway up it. At 3 the split
    /// is 52.3 / 30.2 / 17.4 percent -- about six fish per 3-minute buff.
    public const float ThreeMinuteRarity = 3f;

    /// Seconds per tier. Index is ActiveTier.
    public static float DurationFor(int tier) => (Mathf.Clamp(tier, 0, 2) + 1) * 60f;

    // ── odds ─────────────────────────────────────────────────────────────

    /// Normalised chance of each duration tier. Same ladder the localhost
    /// mockup in tools/cat-perks uses, so the numbers Sam tuned there are these.
    public static void TierChances(out float t1, out float t2, out float t3)
    {
        float a = ThreeMinuteRarity;                 // 1 minute
        float b = Mathf.Sqrt(ThreeMinuteRarity);     // 2 minutes
        const float c = 1f;                          // 3 minutes
        float sum = a + b + c;
        t1 = a / sum; t2 = b / sum; t3 = c / sum;
    }

    /// <summary>
    /// Roll one of the nine outcomes. The perk itself is a flat three-way pick
    /// (Sam: any perk is fine, the duration is the prize); the tier rides the
    /// rarity ladder. Takes its randomness as arguments so the headless tests
    /// and the case-opening UI can both drive it deterministically.
    /// </summary>
    public static void Roll(float randPerk01, float randTier01,
                            out CatPerkKind kind, out int tier)
    {
        int k = Mathf.Clamp((int)(randPerk01 * 3f), 0, 2);
        kind = (CatPerkKind)(k + 1);

        TierChances(out float t1, out float t2, out float t3);
        if (randTier01 < t1)      tier = 0;
        else if (randTier01 < t1 + t2) tier = 1;
        else                      tier = 2;
    }

    /// Chance of the exact outcome that was rolled -- shown on the result card
    /// ("17.4% CHANCE / 1 IN 6") so a good roll reads as a good roll.
    public static float ChanceOf(int tier)
    {
        TierChances(out float t1, out float t2, out float t3);
        float t = tier == 0 ? t1 : tier == 1 ? t2 : t3;
        return t / 3f;
    }

    // ── granting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Install a perk, replacing whatever was running. Returns what it binned
    /// so the result screen can say "THREW AWAY +FRENZY (2:11 was left)".
    /// </summary>
    public void Grant(CatPerkKind kind, int tier,
                      out CatPerkKind replacedKind, out int replacedTier, out float replacedLeft)
    {
        replacedKind = ActiveKind;
        replacedTier = ActiveTier;
        replacedLeft = SecondsLeft;

        ActiveKind   = kind;
        ActiveTier   = Mathf.Clamp(tier, 0, 2);
        TotalSeconds = DurationFor(ActiveTier);
        SecondsLeft  = TotalSeconds;      // full length of the NEW tier, always
        OnChanged?.Invoke();
    }

    public void ClearPerk()
    {
        if (ActiveKind == CatPerkKind.None && SecondsLeft <= 0f) return;
        ActiveKind = CatPerkKind.None;
        ActiveTier = 0;
        SecondsLeft = 0f;
        TotalSeconds = 0f;
        OnChanged?.Invoke();
    }

    /// Save restore. Takes the remaining seconds verbatim so a reload does not
    /// hand back a full timer.
    public void RestoreFromSave(CatPerkKind kind, int tier, float secondsLeft)
    {
        if (kind == CatPerkKind.None || secondsLeft <= 0f) { ClearPerk(); return; }
        ActiveKind   = kind;
        ActiveTier   = Mathf.Clamp(tier, 0, 2);
        TotalSeconds = DurationFor(ActiveTier);
        SecondsLeft  = Mathf.Min(secondsLeft, TotalSeconds);
        OnChanged?.Invoke();
    }

    // ── what the fishing code reads ──────────────────────────────────────
    // All four are exactly neutral with no perk running.

    /// Multiplies the rate the bite countdown drains at. >1 = bites sooner.
    /// Frenzy is the headline; Luck gives a small share of the same thing.
    public static float BiteRateMultiplier()
    {
        var m = Instance;
        if (m == null || !m.HasPerk) return 1f;
        switch (m.ActiveKind)
        {
            case CatPerkKind.Frenzy: return 2.2f;
            case CatPerkKind.Luck:   return 1.15f;
            default:                 return 1f;
        }
    }

    /// Fraction the uncommon+rare tier weights are lifted by. 0 = no change.
    /// Only Luck moves this -- Weight Gain and Frenzy deliberately never change
    /// WHAT bites.
    public static float TierLuckBonus()
    {
        var m = Instance;
        if (m == null || !m.HasPerk) return 0f;
        return m.ActiveKind == CatPerkKind.Luck ? 0.20f : 0f;
    }

    /// Multiplies the weight roll's power-curve exponent. Below 1 skews heavy.
    /// For scale: the best bait in the game (Voidmaggots) is 0.78, so Weight
    /// Gain at 0.80 is best-bait-grade size ON TOP of whatever bait is on.
    public static float WeightExponentMultiplier()
    {
        var m = Instance;
        if (m == null || !m.HasPerk) return 1f;
        switch (m.ActiveKind)
        {
            case CatPerkKind.WeightGain: return 0.80f;
            case CatPerkKind.Luck:       return 0.92f;
            default:                     return 1f;
        }
    }

    public static string DisplayName(CatPerkKind k)
    {
        switch (k)
        {
            case CatPerkKind.Luck:       return "LUCKY WHISKERS";
            case CatPerkKind.WeightGain: return "WEIGHT GAIN";
            case CatPerkKind.Frenzy:     return "FISHING FRENZY";
            default:                     return "";
        }
    }

    public static string ShortName(CatPerkKind k)
    {
        switch (k)
        {
            case CatPerkKind.Luck:       return "LUCK";
            case CatPerkKind.WeightGain: return "WEIGHT";
            case CatPerkKind.Frenzy:     return "FRENZY";
            default:                     return "";
        }
    }

    public static string Blurb(CatPerkKind k)
    {
        switch (k)
        {
            case CatPerkKind.Luck:       return "Bites come sooner, and what bites is better and bigger.";
            case CatPerkKind.WeightGain: return "Same fish, fatter. Nothing changes about what bites.";
            case CatPerkKind.Frenzy:     return "Bites land almost instantly. Your odds are untouched.";
            default:                     return "";
        }
    }

    /// Tier colours are the game's own fish-tier colours, so a 3-minute perk is
    /// gold for the same reason a rare fish is.
    public static Color TierColor(int tier)
    {
        switch (Mathf.Clamp(tier, 0, 2))
        {
            case 0:  return new Color32(0x8C, 0xAF, 0xFF, 0xFF);
            case 1:  return new Color32(0x3C, 0xDC, 0xBE, 0xFF);
            default: return new Color32(0xFF, 0xD2, 0x32, 0xFF);
        }
    }

    public static string TierLabel(int tier) => (Mathf.Clamp(tier, 0, 2) + 1) + " MIN";

    // ── lifecycle ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: this early-return means the singleton never auto-creates in a
        // BUILD, because the build's first scene is MainMenu. It is therefore
        // also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[CatPerkManager]");
        DontDestroyOnLoad(go);
        go.AddComponent<CatPerkManager>();
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
        if (ActiveKind == CatPerkKind.None) return;
        if (SecondsLeft <= 0f) return;
        // Real seconds. Pause in this game is an input block, not a timeScale
        // freeze (co-op cannot stop time), so unscaledDeltaTime would keep
        // burning the perk inside a menu. deltaTime is the honest one.
        SecondsLeft -= Time.deltaTime;
        if (SecondsLeft <= 0f)
        {
            SecondsLeft = 0f;
            ActiveKind = CatPerkKind.None;
            ActiveTier = 0;
            TotalSeconds = 0f;
            OnChanged?.Invoke();
        }
    }
}
