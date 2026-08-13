using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tev's 50/50 fronting loop — the player's first repeatable income.
///
/// He fronts you shrooms, you sell them wherever you like, and you owe him half
/// of MARKET value back. Not half of what you actually got: his cut is pinned to
/// <see cref="MushroomRegistry.BaseValue"/>, so selling above market pockets the
/// difference and he never knows. **That skim is the feature.** It's the reason
/// the front line reads the per-cap market price out loud — it teaches the word
/// "market", which is what lets the player later work out they can beat it.
/// Do not "fix" it.
///
/// ── Per player, and why it isn't in the buyer ledger ──────────────────────
/// BuyerLedger is SHARED world state: both players deal with the same aliens and
/// fill the same appetites. Tev is the opposite — each player has their own
/// bond, their own front and their own debt with him, and both can be carrying a
/// front at once. So this is its own table, keyed by character id, host-owned
/// and replicated whole like the ledger is.
///
/// House rules from the multiplayer work hold: the host owns every timer and
/// every dice roll, PlayerWallet stays out of the sync layer, and the save
/// schema IS the network schema.
/// </summary>
public static class TevFronting
{
    // ── Tuning ────────────────────────────────────────────────────────────

    /// Fronts start small and grow with completed cycles. Front 1 is three
    /// commons (~$15 owed), which is deliberately close to the free onboarding
    /// batch so the first paid cycle feels like the same move for real money.
    public const int BaseQty = 3;
    public const int MaxQty = 12;

    /// Tier unlocks by completed fronts. Rares are gated a long way out because
    /// a rare front is ~$390 of debt and that is not a beginner's problem.
    public const int UncommonAfterFronts = 3;
    public const int RareAfterFronts = 7;

    /// Tev's cut, as a fraction of market value. Half, hence "fifty-fifty".
    public const float TevShare = 0.5f;

    /// Bond for clearing a debt exactly.
    public const int BondPerRepayment = 6;
    /// Extra bond per 10% overpaid, and the ceiling on one payment's total.
    public const int BondPerTenPercentOver = 1;
    public const int BondMaxPerPayment = 20;
    public const int BondMax = 100;

    /// How often Tev texts you once he's a contact. ONE constant, per the
    /// handoff — slowing him down after the first hour is a one-number change.
    public static readonly Vector2 TextIntervalMinutes = new Vector2(2f, 5f);

    // ── State ─────────────────────────────────────────────────────────────

    [System.Serializable]
    public class PlayerState
    {
        public string characterId = "";
        public int bond;
        public int frontsCompleted;
        /// Species key of the open front. Empty = no front out.
        public string activeStrain = "";
        public int activeQty;
        /// What's still owed. >0 means a debt is open and blocks a new front.
        public int owed;
        public int totalRepaid;
        /// True once he's texted you and been added as a contact.
        public bool isContact;
        /// True once he's given the full pitch, so it never repeats.
        public bool pitched;
    }

    [System.Serializable]
    public class Snapshot
    {
        public List<PlayerState> players = new List<PlayerState>();
    }

    static readonly Dictionary<string, PlayerState> _byCharacter =
        new Dictionary<string, PlayerState>();

    /// Bumped on every mutation so the economy sync can notice without a hook on
    /// each one — same idea as BuyerLedger.Version.
    public static int Version { get; private set; }
    static void Touch() => Version++;

    /// The local player's id. Falls back to a fixed key when no character exists
    /// (Editor Play-direct), so the loop is still testable there.
    public static string LocalId
    {
        get
        {
            var p = CharacterStore.ActiveProfile;
            return p != null && !string.IsNullOrEmpty(p.id) ? p.id : "__local__";
        }
    }

    public static PlayerState For(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) characterId = "__local__";
        if (!_byCharacter.TryGetValue(characterId, out var s))
        {
            s = new PlayerState { characterId = characterId };
            _byCharacter[characterId] = s;
            Touch();
        }
        return s;
    }

    public static PlayerState Local => For(LocalId);

    // ── The loop ──────────────────────────────────────────────────────────

    /// True when this player owes Tev money. Blocks new fronts.
    public static bool HasDebt(PlayerState s) => s != null && s.owed > 0;

    /// Becoming a contact: fires when the free onboarding reaches Complete, by
    /// EITHER route. Sam's call — reaching Complete by eating six free batches
    /// is a swindle, not a failure, and a broke customer is still a customer.
    public static bool ShouldBecomeContact =>
        MushroomQuest.CurrentStage == MushroomQuest.Stage.Complete;

    /// How many caps the next front carries.
    public static int NextQty(PlayerState s) =>
        Mathf.Clamp(BaseQty + (s != null ? s.frontsCompleted : 0), BaseQty, MaxQty);

    /// Which tier the next front may roll. Gated by experience so an early
    /// player can't be handed a debt they've no route to clear.
    public static MushroomTier MaxTier(PlayerState s)
    {
        int n = s != null ? s.frontsCompleted : 0;
        if (n >= RareAfterFronts) return MushroomTier.Rare;
        if (n >= UncommonAfterFronts) return MushroomTier.Uncommon;
        return MushroomTier.Common;
    }

    /// What a front of this strain and size costs the player to clear.
    /// ceil, not round: Tev never rounds in your favour.
    public static int OwedFor(string speciesKey, int qty) =>
        Mathf.Max(1, Mathf.CeilToInt(TevShare * MushroomRegistry.BaseValue(speciesKey) * qty));

    /// Roll and issue a front. HOST ONLY — the caller is responsible for that;
    /// this is where the dice are, so a guest rolling here would desync.
    /// Returns false if a debt is already open or the pack was full.
    public static bool IssueFront(PlayerState s, out string strain, out int qty, out int owed)
    {
        strain = ""; qty = 0; owed = 0;
        if (s == null || HasDebt(s)) return false;

        strain = MushroomRegistry.RandomKeyUpToTier(MaxTier(s));
        if (string.IsNullOrEmpty(strain)) return false;
        qty = NextQty(s);

        int leftover = Hotbar.Instance != null
            ? Hotbar.Instance.AddResource(Hotbar.ItemId.Mushroom, qty, strain)
            : qty;
        int given = qty - leftover;
        if (given <= 0) return false;    // no room — caller says so, state unchanged

        qty = given;
        owed = OwedFor(strain, qty);

        s.activeStrain = strain;
        s.activeQty = qty;
        s.owed = owed;
        Touch();
        return true;
    }

    /// Pay some or all of a debt. Returns the bond gained.
    ///
    /// Overpaying is rewarded because it's the only way the player can express
    /// "I did well" — Tev's cut is fixed at market, so a great run otherwise
    /// looks identical to a mediocre one from his side.
    public static int Pay(PlayerState s, int amount)
    {
        if (s == null || amount <= 0 || s.owed <= 0) return 0;

        int owedBefore = s.owed;
        int applied = Mathf.Min(amount, owedBefore);
        s.owed -= applied;
        s.totalRepaid += amount;

        int bond = 0;
        if (s.owed <= 0)
        {
            bond = BondPerRepayment;
            int over = amount - owedBefore;
            if (over > 0 && owedBefore > 0)
            {
                int tenths = Mathf.FloorToInt(over * 10f / owedBefore);
                bond += tenths * BondPerTenPercentOver;
            }
            bond = Mathf.Min(bond, BondMaxPerPayment);

            s.bond = Mathf.Clamp(s.bond + bond, 0, BondMax);
            s.frontsCompleted++;
            s.activeStrain = "";
            s.activeQty = 0;
        }
        Touch();
        return bond;
    }

    // ── Save / sync ───────────────────────────────────────────────────────

    public static Snapshot Capture()
    {
        var snap = new Snapshot();
        foreach (var kv in _byCharacter)
            if (kv.Value != null) snap.players.Add(kv.Value);
        return snap;
    }

    /// Wholesale replace, like BuyerLedger and MushroomDealState: the host is
    /// the only machine that decides any of this, so anything a guest holds that
    /// the host doesn't is stale by definition.
    public static void Apply(Snapshot snap)
    {
        _byCharacter.Clear();
        Touch();
        if (snap == null || snap.players == null) return;
        foreach (var p in snap.players)
        {
            if (p == null || string.IsNullOrEmpty(p.characterId)) continue;
            _byCharacter[p.characterId] = p;
        }
    }

    /// New Game must not inherit a debt from the run the player backed out of
    /// (CLAUDE.md: statics leak across the main menu).
    public static void ResetAll()
    {
        _byCharacter.Clear();
        Touch();
    }

    // ── World save (parallel lists — JsonUtility can't do dictionaries) ───

    public static void FillSave(TevFrontingSave save)
    {
        if (save == null) return;
        save.characterIds.Clear(); save.bond.Clear(); save.frontsCompleted.Clear();
        save.activeStrain.Clear(); save.activeQty.Clear(); save.owed.Clear();
        save.totalRepaid.Clear(); save.isContact.Clear(); save.pitched.Clear();

        foreach (var kv in _byCharacter)
        {
            var s = kv.Value;
            if (s == null) continue;
            save.characterIds.Add(s.characterId ?? "");
            save.bond.Add(s.bond);
            save.frontsCompleted.Add(s.frontsCompleted);
            save.activeStrain.Add(s.activeStrain ?? "");
            save.activeQty.Add(s.activeQty);
            save.owed.Add(s.owed);
            save.totalRepaid.Add(s.totalRepaid);
            save.isContact.Add(s.isContact);
            save.pitched.Add(s.pitched);
        }
    }

    public static void ApplySave(TevFrontingSave save)
    {
        _byCharacter.Clear();
        Touch();
        if (save == null || save.characterIds == null) return;

        int n = save.characterIds.Count;
        for (int i = 0; i < n; i++)
        {
            string id = save.characterIds[i];
            if (string.IsNullOrEmpty(id)) continue;
            _byCharacter[id] = new PlayerState
            {
                characterId     = id,
                bond            = At(save.bond, i),
                frontsCompleted = At(save.frontsCompleted, i),
                activeStrain    = At(save.activeStrain, i) ?? "",
                activeQty       = At(save.activeQty, i),
                owed            = At(save.owed, i),
                totalRepaid     = At(save.totalRepaid, i),
                isContact       = At(save.isContact, i),
                pitched         = At(save.pitched, i),
            };
        }
    }

    // Defensive index: a hand-edited or older save can have short lists, and a
    // ragged row must not throw away every row after it.
    static T At<T>(List<T> list, int i) =>
        (list != null && i >= 0 && i < list.Count) ? list[i] : default;
}
