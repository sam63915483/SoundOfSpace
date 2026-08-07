# Messages App + Repeat-Buyer System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repurpose the phone's AI app into a Messages app wired into the mushroom economy: regulars text when hungry, deals are negotiated in-thread (counter/counter-back), scheduled with 5/10/15-minute windows, fulfilled in person (exact or fuzzy substitution), with a saved per-buyer bond stat and per-deal hidden-want reveals.

**Spec:** `docs/superpowers/specs/2026-08-07-messages-app-design.md` — read it first; every formula below is ratified there.

**Architecture:** One new saved static store (`BuyerLedger`) holds everything persistent (bond, deal counts, regular flag, open conversation/appointment, event log). A new auto-singleton (`BuyerMessageDirector`) drives timing (want-texts, deadlines, misses). Pure math lives in `BuyerDeals`; message wording in `BuyerTexts`. UI is a new `MessagesScreen` mounted the same way `AIChatScreen` is. Existing `MushroomDealState` stays session-only and untouched except where noted.

**Tech Stack:** Unity 2022.3, Built-in RP, Assembly-CSharp (no asmdefs), UGUI + TMP, JsonUtility saves. **No CLI tests exist in this project** — verification per task is: scripts compile clean in the Editor Console (or `mcp__coplay-mcp__check_compile_errors` if the Editor is connected), plus the editor play-checks written into each task. Commit after every task; `git add` new `.cs` **and** its `.meta` (CLAUDE.md commit hygiene).

**Conventions that apply to every task (from CLAUDE.md):**
- Append serialized fields at END of MonoBehaviours only.
- No `FindObjectOfType`/`Camera.main` in per-frame methods — cache, lazy-refind on null, throttle retries.
- Auto-singleton = mirror `SpaceDustInventory.cs` AND seed in `MainMenuController.EnsureGameplaySingletons()` (trap #1) — Task 5 does this; do not skip it.
- New save state → `SaveData.cs` schema (JsonUtility types only), capture/apply in `SaveCollector` at the right order point, reset in `NewGameReset.Apply()`.
- Per-frame UI strings behind change-detection.

---

## File map (what exists / what's new)

| File | Status | Responsibility |
|---|---|---|
| `Assets/3 - Scripts/Vendor/BuyerLedger.cs` | **new** | Persistent per-buyer state: bond, deals, regular flag, conversation/appointment state machine, event log, reveals |
| `Assets/3 - Scripts/Vendor/BuyerDeals.cs` | **new** | Pure math: tier prices, want generation numbers, counter resolution, substitution chance, gratitude |
| `Assets/3 - Scripts/Vendor/BuyerTexts.cs` | **new** | All message wording (template variants, salted by buyer identity) |
| `Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs` | **new** | Auto-singleton ticker: hungry-detection, want-text send (cap 3, post-load stagger), deadline/miss detection, cheat keys |
| `Assets/3 - Scripts/UI/Messages/MessagesScreen.cs` | **new** | The Messages app UI: index page, thread view, reply chips, contact card |
| `Assets/3 - Scripts/NPC_Dialogue/NPCMushroomPrice.cs` | modify | Static trait accessors + bond bonus inside `PriceFor` |
| `Assets/3 - Scripts/Vendor/MushroomSellUI.cs` | modify | Report deals to ledger; bond pips in header; ledger-driven memo; scheduled-deal mode + substitution roll |
| `Assets/3 - Scripts/NPC_Dialogue/NPCSellRows.cs` | modify | "Deliver order" row when an appointment is live |
| `Assets/3 - Scripts/World/SpawnerCubeface.cs` | modify | `DecodeCell` (inverse of `EncodeCell`) |
| `Assets/3 - Scripts/World/AlienNPCSpawner.cs` | modify | `TryGetCellWorldPos` for the distance line |
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | modify | `BuyerLedgerSave` schema |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | modify | Capture/apply at the singleton step |
| `Assets/3 - Scripts/SaveSystem/NewGameReset.cs` | modify | `BuyerLedger.ResetAll()` |
| `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` | modify | AI tile → MESSAGES tile; mounts `MessagesScreen`; badge counts ledger unread |

`AIChatScreen.cs`, `MushroomDealState.cs`, `AlienNames.cs` are **not modified** (HAL chat is mounted from the Messages index unchanged).

---

### Task 1: Trait statics + bond hook in NPCMushroomPrice

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/NPCMushroomPrice.cs`

The director must compute a buyer's traits while the buyer is **unstreamed** (no component). All traits are already pure functions of the identity hash — hoist them into statics with the current default constants; instance properties delegate. (Inspector-tuned per-instance ranges die, which is fine: every streamed alien gets the component via `GetOrAdd` with defaults anyway; no scene NPC is hand-tuned today.)

- [ ] **Step 1: Add static trait accessors + constants**

Add after the `Identity` property in `NPCMushroomPrice` (append region, don't reorder fields):

```csharp
    // ── Static trait accessors (2026-08-07, Messages app) ─────────────────
    // The message director needs a buyer's traits while they're UNSTREAMED
    // (no component exists). Every trait was already a pure function of the
    // identity hash, so the math lives here now, with the tuning constants;
    // the instance properties above delegate. The serialized min/max fields
    // are LEGACY — every streamed alien gets this component via GetOrAdd
    // with defaults, so the constants below are the live values.
    public const float DefMinMultiplier = 0.75f, DefMaxMultiplier = 1.35f;
    public const float DefMinPatience   = 1.12f, DefMaxPatience   = 1.40f;
    public const float DefFavouriteBonus = 1.35f, DefDislikedPenalty = 0.72f;
    public const int   DefMinAppetite = 6, DefMaxAppetite = 24;

    public static float MultiplierOf(string id) =>
        Mathf.Lerp(DefMinMultiplier, DefMaxMultiplier, UnitOf(AlienIdentity.Hash(id + ":mult")));

    public static float PatienceOf(string id) =>
        Mathf.Lerp(DefMinPatience, DefMaxPatience, UnitOf(AlienIdentity.Hash(id + ":patience")));

    public static MushroomTier FavouriteTierOf(string id) =>
        (MushroomTier)(AlienIdentity.Hash(id + ":taste") % 3u);

    public static MushroomTier DislikedTierOf(string id)
    {
        int fav = (int)FavouriteTierOf(id);
        int step = 1 + (int)(AlienIdentity.Hash(id + ":distaste") % 2u);
        return (MushroomTier)((fav + step) % 3);
    }

    public static float TasteOf(string id, MushroomTier tier)
    {
        if (tier == FavouriteTierOf(id)) return DefFavouriteBonus;
        if (tier == DislikedTierOf(id))  return DefDislikedPenalty;
        return 1f;
    }

    public static int AppetiteMaxOf(string id) =>
        DefMinAppetite + (int)(AlienIdentity.Hash(id + ":appetite") % (uint)(DefMaxAppetite - DefMinAppetite + 1));

    static float UnitOf(uint h) => (h & 0xFFFFu) / 65535f;
```

- [ ] **Step 2: Delegate the instance properties to the statics**

Replace the bodies of `Multiplier`, `Patience`, `FavouriteTier`, `DislikedTier`, `AppetiteMax`, and `TasteFor` so they call the statics (identical values — `Hash` here and `AlienIdentity.Hash` are the same FNV-1a+avalanche):

```csharp
    public float Multiplier => MultiplierOf(Identity);
    public float Patience   => PatienceOf(Identity);
    public MushroomTier FavouriteTier => FavouriteTierOf(Identity);
    public MushroomTier DislikedTier  => DislikedTierOf(Identity);
    public float TasteFor(MushroomTier tier) => TasteOf(Identity, tier);
    public int AppetiteMax => AppetiteMaxOf(Identity);
```

(Leave the serialized fields in place — deleting mid-class fields corrupts scene serialization. Update their tooltips to say LEGACY.)

- [ ] **Step 3: Bond bonus inside PriceFor**

In `PriceFor(string speciesKey)` (currently `NPCMushroomPrice.cs:148`), multiply in the bond bonus:

```csharp
        float v = MushroomRegistry.BaseValue(speciesKey)
                  * Multiplier
                  * TasteFor(MushroomRegistry.Tier(speciesKey))
                  * saturation
                  * BuyerLedger.BondBonus(Identity);   // up to +15% at bond 100
```

This won't compile until Task 2 lands `BuyerLedger` — Tasks 1+2 are committed together.

---

### Task 2: BuyerLedger core + NewGameReset

**Files:**
- Create: `Assets/3 - Scripts/Vendor/BuyerLedger.cs`
- Modify: `Assets/3 - Scripts/SaveSystem/NewGameReset.cs:71` (next to `MushroomDealState.ResetAll()`)

- [ ] **Step 1: Write BuyerLedger.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PERSISTENT per-buyer memory for the messages/repeat-business loop — the
/// saved counterpart to session-only <see cref="MushroomDealState"/>. Keyed by
/// AlienIdentity, same as everything else about a buyer.
///
/// Owns: bond (0–100), deals completed (drives hidden-want reveals), regular
/// status, the open conversation/appointment state machine, and a bounded
/// event log that message threads are RENDERED from (events, not strings — so
/// wording can improve without stale text in saves).
///
/// Timing rules: all timestamps are Time.unscaledTime within a session and
/// are saved RELATIVE (seconds-ago / seconds-remaining), re-anchored on load.
/// </summary>
public static class BuyerLedger
{
    // ── Bond tuning (spec §2) ──────────────────────────────────────────────
    public const int BondPerDeal = 8;
    public const int BondKeptAppointment = 4;   // extra
    public const int BondFavouriteTier = 4;     // extra
    public const int BondCounterRefused = 10;   // loss
    public const int BondSubRefused = 5;        // loss
    public const float BondMaxPayBonus = 0.15f; // +15% at bond 100

    public const int MaxEventsPerBuyer = 40;
    public const int RevealCap = 5;

    public enum Convo { None = 0, AwaitingReply = 1, AwaitingCounterBack = 2, Scheduled = 3 }

    public enum EvType
    {
        WantText = 0,        // a: offerPerCap, b: qty, tier
        PlayerAccepted = 1,  // a: windowMinutes
        PlayerCountered = 2, // a: counterPerCap
        BuyerCounterBack = 3,// a: counterBackPerCap
        BuyerRefused = 4,    // (outrageous counter — deal off, bond ding)
        PlayerDeclined = 5,  // ("not now", or declining a counter-back)
        Scheduled = 6,       // a: agreedPerCap, b: qty, tier (window in PlayerAccepted)
        FulfilledExact = 7,  // a: paidPerCap, b: qty
        FulfilledSub = 8,    // a: paidPerCap, b: qty, tier: what they actually took
        SubRefused = 9,      // a: rolled chance 0-100
        Missed = 10,         // (negative text renders from this)
        WalkUpDeal = 11,     // a: paidPerCap, b: qty, tier — non-scheduled sale
    }

    [System.Serializable]
    public class Ev
    {
        public int type;
        public float at;      // Time.unscaledTime when it happened
        public int a, b, tier;
    }

    public class Buyer
    {
        public string id;
        public int bond;
        public int dealsCompleted;
        public bool isRegular;
        public int unread;
        public List<Ev> events = new List<Ev>();

        // Open conversation / appointment. Only meaningful while convo != None.
        public Convo convo;
        public int askTier;
        public int askQty;
        public int offerPerCap;       // their live offer; the AGREED price once Scheduled
        public int counterBackPerCap; // only while AwaitingCounterBack
        public int windowMinutes;     // 5 / 10 / 15 once Scheduled
        public float deadline;        // unscaledTime; Scheduled only (grace added by the director)
        public float nextTextAt;      // director pacing: earliest next want-text
    }

    static readonly Dictionary<string, Buyer> _buyers = new Dictionary<string, Buyer>();
    static float Now => Time.unscaledTime;

    // Story NPCs never become regulars — their threads belong to story systems.
    static readonly string[] ExcludedIdPrefixes = { "scene:Tev", "scene:Kolb" };

    public static bool Eligible(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < ExcludedIdPrefixes.Length; i++)
            if (id.StartsWith(ExcludedIdPrefixes[i])) return false;
        return true;
    }

    public static Buyer Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _buyers.TryGetValue(id, out var b);
        return b;
    }

    public static Buyer GetOrCreate(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!_buyers.TryGetValue(id, out var b))
        {
            b = new Buyer { id = id };
            _buyers[id] = b;
        }
        return b;
    }

    /// Every buyer that has ANY ledger state (drives the Messages index).
    public static IEnumerable<Buyer> All() => _buyers.Values;

    public static int TotalUnread()
    {
        int n = 0;
        foreach (var b in _buyers.Values) n += b.unread;
        return n;
    }

    // ── Bond ───────────────────────────────────────────────────────────────

    /// 1.0 at bond 0 → 1.15 at bond 100. Safe on unknown buyers.
    public static float BondBonus(string id)
    {
        var b = Get(id);
        return b == null ? 1f : 1f + BondMaxPayBonus * (b.bond / 100f);
    }

    /// 5-pip readout, one pip per 20 bond. The only bond display ever shown.
    public static string BondPips(string id)
    {
        var b = Get(id);
        int filled = b == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(b.bond / 20f), 0, 5);
        var sb = new System.Text.StringBuilder(5);
        for (int i = 0; i < 5; i++) sb.Append(i < filled ? '●' : '○'); // ● ○
        return sb.ToString();
    }

    // ── Deal reporting (called from MushroomSellUI.CloseSale) ──────────────

    /// <param name="keptAppointment">deal fulfilled a Scheduled appointment in-window</param>
    /// <param name="substituted">fulfilled but not exact (tier differed / qty short)</param>
    /// <returns>true if this deal converted the buyer into a regular.</returns>
    public static bool ReportDeal(string id, MushroomTier tier, int pricePerCap, int qty,
                                  bool keptAppointment, bool substituted)
    {
        var b = GetOrCreate(id);
        if (b == null) return false;

        b.dealsCompleted++;
        bool fav = tier == NPCMushroomPrice.FavouriteTierOf(id);
        int gain = BondPerDeal
                 + (keptAppointment ? BondKeptAppointment : 0)
                 + (fav ? BondFavouriteTier : 0);
        if (substituted) gain /= 2;
        b.bond = Mathf.Clamp(b.bond + gain, 0, 100);

        if (keptAppointment)
            Log(b, substituted ? EvType.FulfilledSub : EvType.FulfilledExact,
                pricePerCap, qty, (int)tier, markUnread: false);
        else if (b.isRegular)
            Log(b, EvType.WalkUpDeal, pricePerCap, qty, (int)tier, markUnread: false);

        if (keptAppointment || substituted) CloseConversation(b);

        bool converted = false;
        if (!b.isRegular && Eligible(id))
        {
            // Spec §3: guaranteed if the deal included their favourite tier,
            // otherwise 1 in 3.
            if (fav || Random.value < 1f / 3f)
            {
                b.isRegular = true;
                converted = true;
                // No fanfare now — the director texts when they next go hungry.
            }
        }
        return converted;
    }

    public static void MissedAppointment(string id)
    {
        var b = Get(id);
        if (b == null || b.convo != Convo.Scheduled) return;
        b.bond /= 2;                              // Sam's spec: halve it
        Log(b, EvType.Missed, 0, 0, b.askTier);
        CloseConversation(b);
    }

    public static void CounterRefused(string id)
    {
        var b = Get(id);
        if (b == null) return;
        b.bond = Mathf.Max(0, b.bond - BondCounterRefused);
        Log(b, EvType.BuyerRefused, 0, 0, b.askTier);
        CloseConversation(b);
    }

    public static void SubstitutionRefused(string id, int rolledPercent)
    {
        var b = Get(id);
        if (b == null) return;
        b.bond = Mathf.Max(0, b.bond - BondSubRefused);
        Log(b, EvType.SubRefused, rolledPercent, 0, b.askTier);
        CloseConversation(b);
    }

    /// In-person barred (pushed past their counter) cancels any appointment
    /// WITHOUT the missed-halving — the −10 already landed (spec §9).
    public static void CancelAppointmentQuietly(string id)
    {
        var b = Get(id);
        if (b == null || b.convo == Convo.None) return;
        CloseConversation(b);
    }

    static void CloseConversation(Buyer b)
    {
        b.convo = Convo.None;
        b.counterBackPerCap = 0;
        b.deadline = 0f;
        // nextTextAt is the director's business (it sets pacing on send).
    }

    // ── Events / thread ────────────────────────────────────────────────────

    public static void Log(Buyer b, EvType t, int a, int bb, int tier, bool markUnread = true)
    {
        if (b == null) return;
        b.events.Add(new Ev { type = (int)t, at = Now, a = a, b = bb, tier = tier });
        if (b.events.Count > MaxEventsPerBuyer) b.events.RemoveAt(0);
        // Player-authored events never count as unread; buyer-authored do.
        if (markUnread && t != EvType.PlayerAccepted && t != EvType.PlayerCountered
                       && t != EvType.PlayerDeclined && t != EvType.Scheduled)
            b.unread++;
    }

    public static void MarkRead(string id)
    {
        var b = Get(id);
        if (b != null) b.unread = 0;
    }

    // ── Hidden-want reveals (spec §6) ──────────────────────────────────────

    public static int RevealCount(string id)
    {
        var b = Get(id);
        return b == null ? 0 : Mathf.Min(b.dealsCompleted, RevealCap);
    }

    /// Worded, never numeric. Index 0-4 = the reveal unlocked by deal #1-#5.
    public static string RevealLine(string id, int index)
    {
        switch (index)
        {
            case 0: return $"takes about {NPCMushroomPrice.AppetiteMaxOf(id)} caps before they're full";
            case 1: return $"keen on {MushroomSpecies.TierName(NPCMushroomPrice.FavouriteTierOf(id)).ToLowerInvariant()}";
            case 2: return $"won't pay much for {MushroomSpecies.TierName(NPCMushroomPrice.DislikedTierOf(id)).ToLowerInvariant()}";
            case 3:
            {
                float m = NPCMushroomPrice.MultiplierOf(id);
                if (m > 1.15f) return "pays generously";
                if (m < 0.95f) return "a bit stingy";
                return "an average payer";
            }
            case 4:
                return NPCMushroomPrice.PatienceOf(id) >= 1.26f
                    ? "doesn't mind a cheeky ask"
                    : "walks fast if you push";
            default: return "";
        }
    }

    // ── Reset / save plumbing ──────────────────────────────────────────────

    /// New Game must not inherit another run's regulars (CLAUDE.md: statics
    /// leak across the main menu). Called from NewGameReset.Apply().
    public static void ResetAll() => _buyers.Clear();

    /// Serialize into parallel lists (JsonUtility — no dictionaries). Events
    /// are flattened with a per-buyer count list. Times go out RELATIVE.
    public static void FillSave(BuyerLedgerSave s)
    {
        s.ids.Clear(); s.bond.Clear(); s.deals.Clear(); s.regular.Clear();
        s.unread.Clear(); s.convo.Clear(); s.askTier.Clear(); s.askQty.Clear();
        s.offerPerCap.Clear(); s.counterBack.Clear(); s.windowMinutes.Clear();
        s.deadlineSecondsLeft.Clear(); s.eventCounts.Clear(); s.events.Clear();
        float now = Now;
        foreach (var b in _buyers.Values)
        {
            s.ids.Add(b.id);
            s.bond.Add(b.bond);
            s.deals.Add(b.dealsCompleted);
            s.regular.Add(b.isRegular);
            s.unread.Add(b.unread);
            s.convo.Add((int)b.convo);
            s.askTier.Add(b.askTier);
            s.askQty.Add(b.askQty);
            s.offerPerCap.Add(b.offerPerCap);
            s.counterBack.Add(b.counterBackPerCap);
            s.windowMinutes.Add(b.windowMinutes);
            s.deadlineSecondsLeft.Add(b.convo == Convo.Scheduled ? Mathf.Max(0f, b.deadline - now) : 0f);
            s.eventCounts.Add(b.events.Count);
            for (int i = 0; i < b.events.Count; i++)
            {
                var e = b.events[i];
                s.events.Add(new BuyerLedgerSave.EvSave
                    { type = e.type, secondsAgo = Mathf.Max(0f, now - e.at), a = e.a, b = e.b, tier = e.tier });
            }
        }
    }

    public static void ApplySave(BuyerLedgerSave s)
    {
        _buyers.Clear();
        if (s == null || s.ids == null) return;
        float now = Now;
        int evCursor = 0;
        for (int i = 0; i < s.ids.Count; i++)
        {
            var b = new Buyer
            {
                id = s.ids[i],
                bond = s.bond[i],
                dealsCompleted = s.deals[i],
                isRegular = s.regular[i],
                unread = s.unread[i],
                convo = (Convo)s.convo[i],
                askTier = s.askTier[i],
                askQty = s.askQty[i],
                offerPerCap = s.offerPerCap[i],
                counterBackPerCap = s.counterBack[i],
                windowMinutes = s.windowMinutes[i],
                deadline = (Convo)s.convo[i] == Convo.Scheduled ? now + s.deadlineSecondsLeft[i] : 0f,
            };
            int n = s.eventCounts[i];
            for (int e = 0; e < n && evCursor < s.events.Count; e++, evCursor++)
            {
                var es = s.events[evCursor];
                b.events.Add(new Ev { type = es.type, at = now - es.secondsAgo, a = es.a, b = es.b, tier = es.tier });
            }
            _buyers[b.id] = b;
        }
    }
}
```

- [ ] **Step 2: NewGameReset hook**

In `NewGameReset.Apply()` directly after `MushroomDealState.ResetAll();` (line 71):

```csharp
        BuyerLedger.ResetAll();
```

- [ ] **Step 3: Compile check** — Editor Console clean (BuyerLedgerSave lands in the same commit, next step).

- [ ] **Step 4: Save schema** — in `Assets/3 - Scripts/SaveSystem/SaveData.cs`, add to `SaveData` (append at end of field list):

```csharp
    public BuyerLedgerSave buyerLedger = new BuyerLedgerSave();
```

and add the class at file scope alongside the other `*Save` classes:

```csharp
[Serializable]
public class BuyerLedgerSave
{
    // Parallel lists keyed by index (JsonUtility can't do dictionaries) —
    // same shape as WorldPropConsumedSave. Events are flattened: buyer i owns
    // the next eventCounts[i] entries of `events`, in order.
    public List<string> ids = new List<string>();
    public List<int> bond = new List<int>();
    public List<int> deals = new List<int>();
    public List<bool> regular = new List<bool>();
    public List<int> unread = new List<int>();
    public List<int> convo = new List<int>();
    public List<int> askTier = new List<int>();
    public List<int> askQty = new List<int>();
    public List<int> offerPerCap = new List<int>();
    public List<int> counterBack = new List<int>();
    public List<int> windowMinutes = new List<int>();
    public List<float> deadlineSecondsLeft = new List<float>();
    public List<int> eventCounts = new List<int>();
    public List<EvSave> events = new List<EvSave>();

    [Serializable]
    public class EvSave { public int type; public float secondsAgo; public int a; public int b; public int tier; }
}
```

- [ ] **Step 5: SaveCollector wiring** — in `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`:
  - Capture side: next to `CaptureAlienKills(data.alienKills);` (line 48) add `BuyerLedger.FillSave(data.buyerLedger);`
  - Apply side: in the **singleton block** (the region commented `5. Notes / BuildMenuLock / … singleton state`, near the `EarlyGameProgress`/`PlayerProgress` applies around lines 945–960) add `BuyerLedger.ApplySave(data.buyerLedger);`
  - Read the inline order doc at `SaveCollector.cs:924` before placing — it must land with the other pure-singleton applies (before buildings/enemies), exact line will drift.

- [ ] **Step 6: Compile check, then commit**

```bash
git add "Assets/3 - Scripts/Vendor/BuyerLedger.cs" "Assets/3 - Scripts/Vendor/BuyerLedger.cs.meta" \
        "Assets/3 - Scripts/NPC_Dialogue/NPCMushroomPrice.cs" "Assets/3 - Scripts/SaveSystem/SaveData.cs" \
        "Assets/3 - Scripts/SaveSystem/SaveCollector.cs" "Assets/3 - Scripts/SaveSystem/NewGameReset.cs"
git commit -m "feat(messages): BuyerLedger persistent buyer store + trait statics + bond price hook"
```

---

### Task 3: Negotiation math (BuyerDeals) + wording (BuyerTexts)

**Files:**
- Create: `Assets/3 - Scripts/Vendor/BuyerDeals.cs`
- Create: `Assets/3 - Scripts/Vendor/BuyerTexts.cs`

- [ ] **Step 1: BuyerDeals.cs** — every formula from spec §4–§5, pure static, no state:

```csharp
using UnityEngine;

/// <summary>
/// Pure math for message-negotiated deals (spec §4–§5). No state — everything
/// is a function of buyer identity + the numbers on the table, so it can run
/// for unstreamed buyers and is trivially testable from a cheat key.
/// </summary>
public static class BuyerDeals
{
    public enum CounterResult { Accept, CounterBack, Refuse }

    /// A tier's representative per-cap market value: the average BaseValue of
    /// every registered species in that tier (asks name a TIER, not a species;
    /// the agreed price then holds for any species of that tier).
    public static int TierBaseValue(MushroomTier tier)
    {
        int sum = 0, n = 0;
        for (int i = 0; i < MushroomRegistry.Count; i++)
        {
            string key = MushroomRegistry.KeyAt(i);
            if (MushroomRegistry.Tier(key) != tier) continue;
            sum += MushroomRegistry.BaseValue(key); n++;
        }
        return n == 0 ? 10 : Mathf.Max(1, Mathf.RoundToInt((float)sum / n));
    }

    /// What this buyer genuinely values one cap of this tier at, right now
    /// (multiplier × taste × bond — saturation deliberately excluded: they
    /// text BECAUSE they're empty).
    public static int TruePricePerCap(string id, MushroomTier tier)
    {
        float v = TierBaseValue(tier)
                * NPCMushroomPrice.MultiplierOf(id)
                * NPCMushroomPrice.TasteOf(id, tier)
                * BuyerLedger.BondBonus(id);
        return Mathf.Max(1, Mathf.RoundToInt(v));
    }

    // ── Want generation (spec §4) ──────────────────────────────────────────

    /// Usually their favourite tier (~70%), otherwise their neutral one,
    /// never the disliked one.
    public static MushroomTier PickAskTier(string id)
    {
        var fav = NPCMushroomPrice.FavouriteTierOf(id);
        if (Random.value < 0.7f) return fav;
        var dis = NPCMushroomPrice.DislikedTierOf(id);
        for (int t = 0; t < 3; t++)
            if ((MushroomTier)t != fav && (MushroomTier)t != dis) return (MushroomTier)t;
        return fav;
    }

    /// 50–100% of their appetite, at least 2.
    public static int PickAskQty(string id)
    {
        int max = NPCMushroomPrice.AppetiteMaxOf(id);
        return Mathf.Max(2, Random.Range(Mathf.CeilToInt(max * 0.5f), max + 1));
    }

    /// Their opening offer: ~90% of their true number (they lowball —
    /// that's what countering is for).
    public static int OpeningOffer(string id, MushroomTier tier) =>
        Mathf.Max(1, Mathf.RoundToInt(TruePricePerCap(id, tier) * 0.9f));

    // ── Counter resolution (spec §4, incl. Sam's counter-back rule) ────────

    /// Player counters at `ask` per cap. One exchange each, no loops:
    ///   within patience              → Accept at the player's number
    ///   ≤ patience × 1.25            → CounterBack (midpoint, clamped to
    ///                                  their patience ceiling)
    ///   beyond that (outrageous)     → Refuse, deal off, bond ding
    public static CounterResult ResolveCounter(string id, MushroomTier tier, int ask, out int counterBack)
    {
        counterBack = 0;
        int truePrice = TruePricePerCap(id, tier);
        float patience = NPCMushroomPrice.PatienceOf(id);
        float ceiling = truePrice * patience;
        if (ask <= ceiling) return CounterResult.Accept;
        if (ask <= ceiling * 1.25f)
        {
            int opening = OpeningOffer(id, tier);
            counterBack = Mathf.Min(Mathf.RoundToInt((opening + ask) / 2f), Mathf.FloorToInt(ceiling));
            counterBack = Mathf.Max(counterBack, opening); // never counter below their own offer
            return CounterResult.CounterBack;
        }
        return CounterResult.Refuse;
    }

    // ── Windows & gratitude (spec §4) ──────────────────────────────────────

    public static readonly int[] WindowMinutes = { 5, 10, 15 };
    public const float GraceSeconds = 60f;

    /// +15% / +10% / +5% for the 5 / 10 / 15 minute promise.
    public static float GratitudeBonus(int windowMinutes)
    {
        if (windowMinutes <= 5) return 1.15f;
        if (windowMinutes <= 10) return 1.10f;
        return 1.05f;
    }

    // ── Substitution (spec §5c, Sam's fuzzy-fulfilment rule) ───────────────

    /// Chance the buyer accepts a delivery that differs from the agreed order.
    /// Calibration (agreed 3 rare): 3 uncommon → 50%, 5 uncommon → 70%,
    /// 2 rare → ~87%, 5 common → 20%, any tier up → ~guaranteed.
    public static float SubstitutionChance(MushroomTier agreedTier, int agreedQty,
                                           MushroomTier offeredTier, int offeredQty)
    {
        int tierDelta = (int)offeredTier - (int)agreedTier; // + is better
        float qtyRatio = agreedQty > 0 ? (float)offeredQty / agreedQty : 1f;
        float chance = 1f
            + (tierDelta < 0 ? 0.5f * tierDelta : 0.25f * tierDelta)
            + 0.3f * Mathf.Max(0f, qtyRatio - 1f)
            - 0.4f * Mathf.Max(0f, 1f - qtyRatio);
        return Mathf.Clamp(chance, 0.05f, 1f);
    }

    /// Exact fulfilment = right tier and at least the agreed quantity.
    public static bool IsExact(MushroomTier agreedTier, int agreedQty,
                               MushroomTier offeredTier, int offeredQty) =>
        offeredTier == agreedTier && offeredQty >= agreedQty;
}
```

- [ ] **Step 2: BuyerTexts.cs** — all wording, 3 variants per buyer-authored event, picked stably per buyer (`AlienIdentity.Hash(id + ":voice") % 3`) so each buyer keeps a consistent voice:

```csharp
using UnityEngine;

/// <summary>
/// Every line a buyer sends, rendered on demand from BuyerLedger events —
/// saves store events, never strings (spec §7). Three voice variants per
/// event, chosen by a stable per-buyer hash so Vorn always sounds like Vorn.
/// No LLM anywhere; same philosophy as HAL's templated lines.
/// </summary>
public static class BuyerTexts
{
    static int Voice(string id) => (int)(AlienIdentity.Hash(id + ":voice") % 3u);
    static string TierWord(int tier) => MushroomSpecies.TierName((MushroomTier)tier).ToLowerInvariant();

    public static string Render(string id, BuyerLedger.Ev e)
    {
        int v = Voice(id);
        switch ((BuyerLedger.EvType)e.type)
        {
            case BuyerLedger.EvType.WantText:
                return v switch
                {
                    0 => $"after {e.b} {TierWord(e.tier)} caps. I'll do {e.a} a cap if you can get here.",
                    1 => $"got room for {e.b} more {TierWord(e.tier)}. {e.a} each, come find me.",
                    _ => $"running low. {e.b} {TierWord(e.tier)} caps, {e.a} a cap — you in?",
                };
            case BuyerLedger.EvType.PlayerAccepted:
                return $"on my way — give me {e.a} minutes.";
            case BuyerLedger.EvType.PlayerCountered:
                return $"make it {e.a} a cap.";
            case BuyerLedger.EvType.BuyerCounterBack:
                return v switch
                {
                    0 => $"steep. {e.a} and we're done talking.",
                    1 => $"can't do that. {e.a}, final.",
                    _ => $"you're pushing it. {e.a}.",
                };
            case BuyerLedger.EvType.BuyerRefused:
                return v switch
                {
                    0 => "forget it. don't text me numbers like that.",
                    1 => "that's a joke. deal's off.",
                    _ => "no. we're done here.",
                };
            case BuyerLedger.EvType.PlayerDeclined:
                return "can't right now.";
            case BuyerLedger.EvType.Scheduled:
                return v switch
                {
                    0 => $"good. {e.b} {TierWord(e.tier)} at {e.a} a cap. I'll be waiting.",
                    1 => $"deal — {e.b} {TierWord(e.tier)}, {e.a} each. don't dawdle.",
                    _ => $"see you soon then. {e.b} {TierWord(e.tier)} at {e.a}.",
                };
            case BuyerLedger.EvType.FulfilledExact:
                return v switch
                {
                    0 => "pleasure doing business.",
                    1 => "exactly what I wanted. good.",
                    _ => "quality. I'll be in touch.",
                };
            case BuyerLedger.EvType.FulfilledSub:
                return $"not what we agreed... but fine. I'll take the {TierWord(e.tier)}.";
            case BuyerLedger.EvType.SubRefused:
                return v switch
                {
                    0 => "that's not what I ordered. waste of my time.",
                    1 => "no. we had a deal and this isn't it.",
                    _ => "you show up with THAT? forget it.",
                };
            case BuyerLedger.EvType.Missed:
                return v switch
                {
                    0 => "waited 20 minutes. don't bother next time.",
                    1 => "you never showed. remembering that.",
                    _ => "stood me up. nice.",
                };
            case BuyerLedger.EvType.WalkUpDeal:
                return ""; // rendered as a system line by the thread view, not a bubble
            default: return "";
        }
    }

    /// Short index-page preview for the most recent event.
    public static string Preview(string id, BuyerLedger.Ev e)
    {
        string s = Render(id, e);
        if (string.IsNullOrEmpty(s)) return "made a deal in person";
        return s.Length <= 40 ? s : s.Substring(0, 38) + "…";
    }
}
```

- [ ] **Step 3: Compile check, commit**

```bash
git add "Assets/3 - Scripts/Vendor/BuyerDeals.cs" "Assets/3 - Scripts/Vendor/BuyerDeals.cs.meta" \
        "Assets/3 - Scripts/Vendor/BuyerTexts.cs" "Assets/3 - Scripts/Vendor/BuyerTexts.cs.meta"
git commit -m "feat(messages): negotiation math + buyer text templates"
```

---### Task 4: Deal reporting + bond pips + ledger memo in the sell panel

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/MushroomSellUI.cs`

- [ ] **Step 1: Report deals to the ledger**

In `CloseSale(int pricePerCap)` (line 348), directly after the existing `MushroomDealState.RecordSale(...)` call (line 379), add:

```csharp
        // Persistent ledger: bond, deal count (reveals), regular conversion.
        // Scheduled-mode fulfilment reports through DeliverOrder instead.
        BuyerLedger.ReportDeal(_buyerId, tier, pricePerCap, qty,
                               keptAppointment: false, substituted: false);
```

(Task 6 refactors this line so scheduled deliveries pass the right flags — the signature is already correct for walk-ups.)

In `BarBuyer()` (line 391), after `MushroomDealState.Bar(_buyerId);` add:

```csharp
        BuyerLedger.CounterRefused(_buyerId);         // −10 bond, spec §2
        BuyerLedger.CancelAppointmentQuietly(_buyerId); // barred kills any appointment, no halving (spec §9)
```

- [ ] **Step 2: Bond pips in the header**

In `Open(...)` (line 209), change the header line to append pips (dim-colored, hex `7FA0BD` = the panel's existing C_Dim):

```csharp
        if (_header != null)
            _header.text = $"// {_npcName.ToUpperInvariant()}  <size=15><color=#7FA0BD>{BuyerLedger.BondPips(_buyerId)}</color></size>";
```

- [ ] **Step 3: Ledger-driven memo line**

In `Refresh()` (line 545–563), replace the taste-note block (the `HasSoldTier` gate at lines 552–560) with the reveal schedule — the deal-count gate supersedes it (spec §6):

```csharp
                // Earned notes come from the ledger's reveal schedule now:
                // one hidden want per completed deal, in fixed order.
                int reveals = BuyerLedger.RevealCount(_buyerId);
                for (int r = 1; r < reveals && r < 3; r++)   // memo line fits 2 (fav + disliked); full list lives in the contact card
                    line += " · " + BuyerLedger.RevealLine(_buyerId, r);
```

(Keep the `last`/`qty` part of the memo unchanged. `HasSoldTier` in `MushroomDealState` becomes unreferenced — leave it, it's harmless session state.)

- [ ] **Step 4: Compile check + editor play-check**

Play the gameplay scene, sell to a wandering alien, confirm: pips appear in the header, memo gains "keen on …" after the 2nd deal, and (via a `Debug.Log` you add temporarily or the ledger cheat in Task 5) `ReportDeal` fires with bond climbing.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Vendor/MushroomSellUI.cs"
git commit -m "feat(messages): sell panel reports deals to ledger; bond pips + reveal-driven memo"
```

---

### Task 5: BuyerMessageDirector + cell position helpers + singleton seeding

**Files:**
- Create: `Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs`
- Modify: `Assets/3 - Scripts/World/SpawnerCubeface.cs` (add `DecodeCell`)
- Modify: `Assets/3 - Scripts/World/AlienNPCSpawner.cs` (add `TryGetCellWorldPos`)
- Modify: `MainMenuController` — `EnsureGameplaySingletons()` (**trap #1 — mandatory**)

- [ ] **Step 1: DecodeCell** — in `SpawnerCubeface`, directly under `EncodeCell` (line 68):

```csharp
    /// Inverse of EncodeCell. Needed by the messages system to point at a
    /// buyer's home cell while they're unstreamed.
    public static void DecodeCell(long id, out int face, out int cellU, out int cellV)
    {
        const long OFFSET = 1L << 19;
        face  = (int)((id >> 40) & 0x7);
        cellU = (int)(((id >> 20) & 0xFFFFFL) - OFFSET);
        cellV = (int)((id & 0xFFFFFL) - OFFSET);
    }
```

- [ ] **Step 2: TryGetCellWorldPos** — public method on `AlienNPCSpawner` (append near `GetBodyName`, line 513). It reuses the exact same jittered math as `TryComputeCellApproxPos` (line 282) so the marker lands where the alien actually stands:

```csharp
    /// World position of a buyer's home cell (approximate sphere point, same
    /// jitter math the spawner uses) — for the Messages appointment distance
    /// line. Works while the alien is unstreamed. False if the slot/body is
    /// gone (e.g. called before ResolveRefs has run).
    public bool TryGetCellWorldPos(int bodySlot, long cellId, out Vector3 pos)
    {
        pos = default;
        if (bodySlot < 0 || bodySlot >= bodies.Count) return false;
        var entry = bodies[bodySlot];
        if (entry.body == null) return false;
        SpawnerCubeface.DecodeCell(cellId, out int face, out int cu, out int cv);
        float faceUVPerCell = cellSize / Mathf.Max(0.001f, entry.body.radius);
        return TryComputeCellApproxPos(entry.body, face, cu, cv, faceUVPerCell, out pos);
    }
```

- [ ] **Step 3: BuyerMessageDirector.cs** — auto-singleton, mirror `SpaceDustInventory.cs` shape exactly:

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The clock behind the Messages app (spec §4–§5): watches every REGULAR's
/// appetite, sends want-texts when they refill (max 3 open at once, staggered
/// after a load so the phone doesn't detonate), resolves appointment
/// deadlines into misses, and answers "where is this buyer" for the distance
/// line. All state lives in BuyerLedger — this component is pure timing.
/// Auto-singleton; ALSO seeded in MainMenuController.EnsureGameplaySingletons
/// (CLAUDE.md trap #1 — RuntimeInitializeOnLoadMethod never fires post-menu
/// in builds).
/// </summary>
public class BuyerMessageDirector : MonoBehaviour
{
    public static BuyerMessageDirector Instance { get; private set; }

    public const int MaxOpenWants = 3;
    const float TickInterval = 2f;
    const float PostLoadStaggerMin = 20f, PostLoadStaggerMax = 180f;
    const float SendDelayMin = 5f, SendDelayMax = 40f;

    float _tickTimer;
    AlienNPCSpawner _spawner;
    float _spawnerRetryAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("[BuyerMessageDirector]");
        go.AddComponent<BuyerMessageDirector>();
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

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// After any load, every buyer's appetite reads empty (session state), so
    /// every regular is instantly "hungry". Stagger their first texts over a
    /// few minutes instead of a message storm (spec §4).
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        if (s.name == "MainMenu") return;
        foreach (var b in BuyerLedger.All())
            if (b.isRegular && b.convo == BuyerLedger.Convo.None)
                b.nextTextAt = Time.unscaledTime + Random.Range(PostLoadStaggerMin, PostLoadStaggerMax);
    }

    void Update()
    {
        _tickTimer += Time.unscaledDeltaTime;
        if (_tickTimer < TickInterval) return;
        _tickTimer = 0f;
        float now = Time.unscaledTime;

        int openWants = 0;
        foreach (var b in BuyerLedger.All())
            if (b.convo == BuyerLedger.Convo.AwaitingReply || b.convo == BuyerLedger.Convo.AwaitingCounterBack)
                openWants++;

        foreach (var b in BuyerLedger.All())
        {
            // Deadline sweep — a lapsed Scheduled appointment is a miss.
            if (b.convo == BuyerLedger.Convo.Scheduled
                && now > b.deadline + BuyerDeals.GraceSeconds)
            {
                BuyerLedger.MissedAppointment(b.id);
                b.nextTextAt = now + Random.Range(300f, 600f); // sulk before texting again
                Notify($"{AlienNames.For(b.id)} is not happy.");
                continue;
            }

            // Want-texts: regular, idle, past their pacing gate, hungry, room in the queue.
            if (!b.isRegular || b.convo != BuyerLedger.Convo.None) continue;
            if (openWants >= MaxOpenWants) break;
            if (now < b.nextTextAt) continue;
            int appetite = NPCMushroomPrice.AppetiteMaxOf(b.id);
            if (MushroomDealState.SecondsUntilHungry(b.id, appetite) > 0) continue;

            SendWantText(b);
            openWants++;
        }
    }

    void SendWantText(BuyerLedger.Buyer b)
    {
        var tier = BuyerDeals.PickAskTier(b.id);
        b.askTier = (int)tier;
        b.askQty = BuyerDeals.PickAskQty(b.id);
        b.offerPerCap = BuyerDeals.OpeningOffer(b.id, tier);
        b.convo = BuyerLedger.Convo.AwaitingReply;
        BuyerLedger.Log(b, BuyerLedger.EvType.WantText, b.offerPerCap, b.askQty, b.askTier);
        Notify($"{AlienNames.For(b.id)} sent you a message");
    }

    // ── Player replies (called by MessagesScreen) ──────────────────────────

    public void Accept(BuyerLedger.Buyer b, int windowMinutes)
    {
        if (b == null || (b.convo != BuyerLedger.Convo.AwaitingReply
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack)) return;
        int agreed = b.convo == BuyerLedger.Convo.AwaitingCounterBack ? b.counterBackPerCap : b.offerPerCap;
        b.offerPerCap = agreed;
        b.windowMinutes = windowMinutes;
        b.deadline = Time.unscaledTime + windowMinutes * 60f;
        b.convo = BuyerLedger.Convo.Scheduled;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerAccepted, windowMinutes, 0, b.askTier, markUnread: false);
        BuyerLedger.Log(b, BuyerLedger.EvType.Scheduled, agreed, b.askQty, b.askTier, markUnread: false);
    }

    public void Counter(BuyerLedger.Buyer b, int askPerCap)
    {
        if (b == null || b.convo != BuyerLedger.Convo.AwaitingReply) return;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerCountered, askPerCap, 0, b.askTier, markUnread: false);
        var res = BuyerDeals.ResolveCounter(b.id, (MushroomTier)b.askTier, askPerCap, out int counterBack);
        switch (res)
        {
            case BuyerDeals.CounterResult.Accept:
                b.offerPerCap = askPerCap;
                // Stays AwaitingReply — the thread now shows the window pick.
                BuyerLedger.Log(b, BuyerLedger.EvType.BuyerCounterBack, askPerCap, 1, b.askTier); // b=1 → "fine, but don't be late" flavor
                break;
            case BuyerDeals.CounterResult.CounterBack:
                b.counterBackPerCap = counterBack;
                b.convo = BuyerLedger.Convo.AwaitingCounterBack;
                BuyerLedger.Log(b, BuyerLedger.EvType.BuyerCounterBack, counterBack, 0, b.askTier);
                Notify($"{AlienNames.For(b.id)} countered");
                break;
            case BuyerDeals.CounterResult.Refuse:
                BuyerLedger.CounterRefused(b.id);
                b.nextTextAt = Time.unscaledTime + Random.Range(300f, 600f);
                Notify($"{AlienNames.For(b.id)} is done talking");
                break;
        }
    }

    public void Decline(BuyerLedger.Buyer b)
    {
        if (b == null || (b.convo != BuyerLedger.Convo.AwaitingReply
                       && b.convo != BuyerLedger.Convo.AwaitingCounterBack)) return;
        BuyerLedger.Log(b, BuyerLedger.EvType.PlayerDeclined, 0, 0, b.askTier, markUnread: false);
        b.convo = BuyerLedger.Convo.None;
        b.counterBackPerCap = 0;
        b.nextTextAt = Time.unscaledTime + Random.Range(120f, 300f);
    }

    // ── Location (distance line) ───────────────────────────────────────────

    /// Where does this buyer live? cell:… ids decode to their spawn cell;
    /// scene:… ids resolve to the live scene object when present.
    public bool TryGetBuyerPos(string id, out Vector3 pos, out string bodyName)
    {
        pos = default; bodyName = "";
        if (string.IsNullOrEmpty(id)) return false;
        if (id.StartsWith("cell:"))
        {
            // "cell:{bodySlot}:{cellId}"
            int c1 = id.IndexOf(':'), c2 = id.IndexOf(':', c1 + 1);
            if (c2 < 0) return false;
            if (!int.TryParse(id.Substring(c1 + 1, c2 - c1 - 1), out int slot)) return false;
            if (!long.TryParse(id.Substring(c2 + 1), out long cell)) return false;
            var sp = Spawner();
            if (sp == null) return false;
            bodyName = sp.GetBodyName(slot);
            return sp.TryGetCellWorldPos(slot, cell, out pos);
        }
        // Fixed scene NPC: find by hierarchy name (throttled by the caller —
        // MessagesScreen refreshes the distance line at 1 Hz, not per frame).
        var go = GameObject.Find(id.Substring("scene:".Length));
        if (go == null) return false;
        pos = go.transform.position;
        return true;
    }

    AlienNPCSpawner Spawner()
    {
        if (_spawner != null) return _spawner;
        if (Time.unscaledTime < _spawnerRetryAt) return null;
        _spawnerRetryAt = Time.unscaledTime + 5f;   // throttled refind (CLAUDE.md)
        _spawner = FindObjectOfType<AlienNPCSpawner>();
        return _spawner;
    }

    static void Notify(string text)
    {
        var phone = PlayerPhoneUI.Instance;
        if (phone != null) phone.FlashNotification(text);
    }

    // ── Cheats (Universe.cheatsEnabled only) ───────────────────────────────
    // F6: every regular goes hungry now (and pacing gates cleared)
    // F7: fast-forward any Scheduled deadline to 5 s from now
    void LateUpdate()
    {
        if (!Universe.cheatsEnabled) return;
        if (Input.GetKeyDown(KeyCode.F6))
        {
            foreach (var b in BuyerLedger.All()) b.nextTextAt = 0f;
            MushroomDealState.ResetAll();   // empties appetite → everyone hungry
            Debug.Log("[BuyerMessageDirector] cheat: all regulars hungry");
        }
        if (Input.GetKeyDown(KeyCode.F7))
        {
            foreach (var b in BuyerLedger.All())
                if (b.convo == BuyerLedger.Convo.Scheduled)
                    b.deadline = Time.unscaledTime + 5f - BuyerDeals.GraceSeconds;
            Debug.Log("[BuyerMessageDirector] cheat: deadlines fast-forwarded");
        }
    }
}
```

> Check `PlayerPhoneUI` exposes a static `Instance` — if it doesn't, add one in `Awake` (guard pattern) rather than `FindObjectOfType` per notify. Check `MushroomDealState.ResetAll` is public (it is).

- [ ] **Step 4: Seed in EnsureGameplaySingletons (trap #1)** — find the method (`Grep "EnsureGameplaySingletons"`), mirror an existing block:

```csharp
        if (BuyerMessageDirector.Instance == null)
            new GameObject("[BuyerMessageDirector]").AddComponent<BuyerMessageDirector>();
```

- [ ] **Step 5: Compile + editor check** — with cheats on, F6 then sell to an alien once or twice until conversion lands; within ~40 s a notification strip message appears. Commit:

```bash
git add "Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs" "Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs.meta" \
        "Assets/3 - Scripts/World/SpawnerCubeface.cs" "Assets/3 - Scripts/World/AlienNPCSpawner.cs" \
        <path-to-MainMenuController>
git commit -m "feat(messages): BuyerMessageDirector auto-singleton — want-texts, deadlines, cheats"
```

---

### Task 6: Scheduled-deal mode — NPCSellRows row + MushroomSellUI delivery + substitution

**Files:**
- Modify: `Assets/3 - Scripts/NPC_Dialogue/NPCSellRows.cs`
- Modify: `Assets/3 - Scripts/Vendor/MushroomSellUI.cs`

- [ ] **Step 1: "Deliver order" row** — in `NPCSellRows.Append` (line 47), before the barred/full label logic, add a scheduled branch that outranks both (a live appointment overrides "full" — they're waiting for THIS delivery):

```csharp
        var ledger = BuyerLedger.Get(id);
        bool scheduled = ledger != null && ledger.convo == BuyerLedger.Convo.Scheduled
                         && Time.unscaledTime <= ledger.deadline + BuyerDeals.GraceSeconds;

        string label;
        if (scheduled)
            label = $"Deliver order — {ledger.askQty} {MushroomSpecies.TierName((MushroomTier)ledger.askTier).ToLowerInvariant()} @ {ledger.offerPerCap}";
        else if (barred) ...   // existing chain unchanged
```

and the row add becomes `rows.Add(new PostGreetingChoicePanel.Row(label, scheduled || (!barred && !full && mushrooms > 0)));`

- [ ] **Step 2: Scheduled mode in the panel** — `MushroomSellUI`:

In `Open(...)`, after `_buyerId` is set, detect the appointment:

```csharp
        var ledger = BuyerLedger.Get(_buyerId);
        _scheduled = ledger != null && ledger.convo == BuyerLedger.Convo.Scheduled
                     && Time.unscaledTime <= ledger.deadline + BuyerDeals.GraceSeconds;
        _appt = _scheduled ? ledger : null;
```

New fields (append at end of the deal-state field block): `bool _scheduled; BuyerLedger.Buyer _appt;`

In `Refresh()`, when `_scheduled`: hide the ask input + risk text, set `_offerText` to the pinned order —

```csharp
            _offerText.text =
                $"<b>ORDER</b> — {_appt.askQty} {MushroomSpecies.TierName((MushroomTier)_appt.askTier).ToLowerInvariant()} @ <color=#FFD732>{_appt.offerPerCap}</color> a cap agreed" +
                (BuyerDeals.GratitudeBonus(_appt.windowMinutes) > 1f
                    ? $"  <size=13><color=#6EDC82>on time (+{Mathf.RoundToInt((BuyerDeals.GratitudeBonus(_appt.windowMinutes) - 1f) * 100)}%)</color></size>" : "");
```

and set `_primaryLabel.text = "DELIVER";` wiring `_primaryBtn` to `DeliverOrder()` instead of `MakeOffer()` while `_scheduled`.

- [ ] **Step 3: DeliverOrder()** — new method beside `CloseSale`:

```csharp
    /// Scheduled-deal fulfilment (spec §5). Exact = agreed tier and ≥ agreed
    /// qty, paid at agreed price × gratitude, full bond. Anything else rolls
    /// the substitution chance: accepted → their standard PriceFor (no bump,
    /// half bond); refused → −5 bond, appointment dead, ONE roll only.
    void DeliverOrder()
    {
        if (!_scheduled || _appt == null || !HasOffer) return;
        var offeredTier = MushroomRegistry.Tier(_offerSpecies);
        var agreedTier = (MushroomTier)_appt.askTier;

        if (BuyerDeals.IsExact(agreedTier, _appt.askQty, offeredTier, _offerCountN))
        {
            int perCap = Mathf.RoundToInt(_appt.offerPerCap * BuyerDeals.GratitudeBonus(_appt.windowMinutes));
            CompleteScheduled(perCap, Mathf.Min(_offerCountN, _appt.askQty), substituted: false);
            return;
        }

        float chance = BuyerDeals.SubstitutionChance(agreedTier, _appt.askQty, offeredTier, _offerCountN);
        if (UnityEngine.Random.value <= chance)
        {
            int perCap = _price != null ? _price.PriceFor(_offerSpecies) : Market;
            int qty = Mathf.Min(_offerCountN, RemainingAppetite);
            if (qty <= 0) { SetResult("\"I'm full up. Come back later.\"", C_Err); return; }
            CompleteScheduled(perCap, qty, substituted: true);
        }
        else
        {
            BuyerLedger.SubstitutionRefused(_buyerId, Mathf.RoundToInt(chance * 100));
            _scheduled = false; _appt = null;
            SetResult($"\"That's not what we agreed.\" — {_npcName} waves you off.", C_Err);
            ReturnOfferToBar();
            Refresh();
        }
    }

    void CompleteScheduled(int perCap, int qty, bool substituted)
    {
        int leftover = _offerCountN - qty;
        var tier = MushroomRegistry.Tier(_offerSpecies);
        string species = _offerSpecies;
        _offerSpecies = null; _offerCountN = 0; _stage = Stage.Open; _counter = 0; _ask = 0;
        if (leftover > 0 && Hotbar.Instance != null)
            Hotbar.Instance.AddResource(Hotbar.ItemId.Mushroom, leftover, species);
        int credits = perCap * qty;
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(credits);
        MushroomDealState.RecordSale(_buyerId, perCap, qty, tier, AppetiteMax);
        MushroomQuest.NotifySold(qty);
        BuyerLedger.ReportDeal(_buyerId, tier, perCap, qty,
                               keptAppointment: true, substituted: substituted);
        _scheduled = false; _appt = null;
        _onSold?.Invoke(qty);
        SetResult(substituted
            ? $"{_npcName} grumbled, but took {qty} for {credits}."
            : $"Order delivered. {_npcName} paid {credits} credits.", C_Ok);
        Refresh();
    }
```

- [ ] **Step 4: Compile + editor check** — cheat a scheduled appointment (F6 → answer text in Task 7's UI once it exists; until then, drive it from the director in a temporary `[ContextMenu]` or accept via code), walk to the buyer, confirm the "Deliver order" row, exact delivery pays agreed × gratitude, wrong-tier delivery rolls.
  *If Task 7 isn't built yet, verification of the full path waits for it — verify compile + walk-up regression (normal selling unchanged) now.*

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/NPC_Dialogue/NPCSellRows.cs" "Assets/3 - Scripts/Vendor/MushroomSellUI.cs"
git commit -m "feat(messages): scheduled-deal delivery mode with fuzzy substitution"
```

---

### Task 7: Messages app — index page (tile repurpose)

**Files:**
- Create: `Assets/3 - Scripts/UI/Messages/MessagesScreen.cs`
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`

The phone's AI tile (built in the method around `PlayerPhoneUI.cs:2331–2379`, `btn.onClick.AddListener(EnterAIChat)`) becomes the MESSAGES tile. `MessagesScreen` is mounted exactly the way `AIChatScreen` is (instantiated under `_pageHostRT`, drives its own UI, exit callback restores the page — read `EnterAIChat`/its exit at `PlayerPhoneUI.cs:2477–2520` and mirror the mechanics).

- [ ] **Step 1: PlayerPhoneUI changes**
  - Relabel the tile text "AI" → "MESSAGES" (same tile-construction method; find the label assignment near the badge construction at line ~2360).
  - Add `EnterMessages()` beside `EnterAIChat()` (same body shape: hide page roots, instantiate a `new GameObject("MessagesScreen")` with the component under `_pageHostRT`, pass an exit callback that restores pages). Point the tile's `onClick` at `EnterMessages` instead of `EnterAIChat`. **Keep `EnterAIChat` intact** — MessagesScreen calls it for the pinned HAL row (make it `public`).
  - `UpdateAIUnreadBadge()` (line 2385): badge shows when `HALVolunteeredLog` unread **or** `BuyerLedger.TotalUnread() > 0`.
  - Gate per-frame badge string/enable behind change-detection (existing pattern in the method).

- [ ] **Step 2: MessagesScreen.cs — index page**

Build with the phone's palette (copy the four colors from `AIChatScreen.cs:49–53`) and `HudFontResolver.Apply` on every TMP label. Structure (all procedural UGUI, no prefabs — house style):

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone's Messages app (spec §7): index of contacts (pinned guide thread
/// + every buyer with ledger state), thread view with reply chips, and the
/// contact card of earned reveals. Mounted by PlayerPhoneUI.EnterMessages the
/// same way AIChatScreen is; renders bubbles from BuyerLedger events via
/// BuyerTexts — nothing here generates content.
/// </summary>
public class MessagesScreen : MonoBehaviour
{
    static readonly Color AccentCyan = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg     = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ScreenBg   = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color ButtonGrey = new Color32(0x2A, 0x40, 0x60, 0xFF);
    static readonly Color UnreadRed  = new Color32(0xFF, 0x5A, 0x5A, 0xFF);

    RectTransform _root, _indexRoot, _threadRoot, _cardRoot;
    System.Action _onExit;
    System.Action _openHalChat;
    string _openThreadId;
    float _refreshTimer;                 // index + distance line refresh at 1 Hz
    int _lastRenderedEventCount = -1;    // thread change-detection

    public void Init(RectTransform host, System.Action onExit, System.Action openHalChat)
    {
        _onExit = onExit;
        _openHalChat = openHalChat;
        _root = BuildRoot(host);
        ShowIndex();
    }
    // ... (full body in steps below)
}
```

Index page contents, top to bottom inside a `ScrollRect` (mirror `AIChatScreen`'s scroll construction):
1. Header row: "MESSAGES" + back arrow → `_onExit`.
2. **Pinned guide row**: name from `NameStore.ResolvedAIName` (falls back "Assistant"; becomes Frump later), subtitle "guide", tap → `_openHalChat` (which runs the untouched `EnterAIChat` flow after tearing this screen down — pass through `PlayerPhoneUI`).
3. One row per `BuyerLedger.All()` buyer having any events, sorted by most recent event time desc: **name** (`AlienNames.For(id)`), **bond pips** (`BuyerLedger.BondPips(id)`, dim), **unread dot** (`UnreadRed` disc, visible when `b.unread > 0`), **preview** (`BuyerTexts.Preview` of last event, dim, 1 line), **relative time** ("2m", "1h" from `Time.unscaledTime - lastEv.at`).
4. Empty state: "no messages yet — regulars will text you when they want more."

Row construction: one helper `RectTransform Row(RectTransform parent, float height)` + `TextMeshProUGUI Txt(...)` — copy the shapes used in `MushroomSellUI`'s builders rather than inventing new ones. Index refreshes on a 1 Hz timer with change-detection (rebuild only when any buyer's event count or unread changed).

- [ ] **Step 3: Compile + editor check** — open phone → MESSAGES tile → index shows pinned guide + any buyers you've dealt with; tap guide row → old HAL chat opens and exits cleanly back.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/UI/Messages/MessagesScreen.cs" "Assets/3 - Scripts/UI/Messages/MessagesScreen.cs.meta" \
        "Assets/3 - Scripts/UI/Messages.meta" "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m "feat(messages): MESSAGES tile + index page; HAL mounted as pinned guide thread"
```

---

### Task 8: Messages app — thread view, reply chips, contact card

**Files:**
- Modify: `Assets/3 - Scripts/UI/Messages/MessagesScreen.cs`

- [ ] **Step 1: Thread view**

Tapping a buyer row → `ShowThread(id)`: hides `_indexRoot`, builds `_threadRoot`:
- Header: back arrow (→ `ShowIndex()`, calls `BuyerLedger.MarkRead(id)`), buyer name + pips; name is a button → `ShowCard(id)`.
- Scrollable bubble list rendered from `BuyerLedger.Get(id).events` via `BuyerTexts.Render`: buyer events left-aligned (TileBg bubble), player events (`PlayerAccepted`, `PlayerCountered`, `PlayerDeclined`) right-aligned (ButtonGrey bubble), `WalkUpDeal`/`Scheduled`/`FulfilledExact|Sub` also render a small centered dim system line ("deal: 6 rare @ 92 — paid 552"). Copy the wrapped-bubble sizing approach from `AIChatScreen` (`BubbleEntry` struct + LayoutElement resize in Update, `AIChatScreen.cs:96–118`), including sticky-at-bottom scroll.
- Rebuild bubbles only when `events.Count` changes (`_lastRenderedEventCount`).
- **Appointment card** (when `convo == Scheduled`): pinned panel under the header — "MEETUP — {qty} {tier} @ {price} · {mm:ss} left · on {bodyName}, ≈{distance} m". Distance from `BuyerMessageDirector.Instance.TryGetBuyerPos(id, out pos, out body)` vs `PlayerController` position, refreshed on the 1 Hz timer only. If `TryGetBuyerPos` fails, omit the distance clause.

- [ ] **Step 2: Reply chips**

Docked row at the bottom, contents by `convo` state:
- `AwaitingReply`, no accepted-counter pending: **[Accept] [Counter] [Not now]**
  - Accept → replaced by window chips **[~5 min] [~10 min] [~15 min] [back]** → `BuyerMessageDirector.Instance.Accept(b, minutes)`
  - Counter → a small inline `TMP_InputField` (numeric) pre-filled with `offerPerCap + 10%`, [Send] → `Counter(b, value)`. While focused, set the same input-capture guard AIChatScreen uses (`IsTypingActive` is AIChatScreen-static — add a matching public static `MessagesScreen.IsTypingActive` and OR it into the two places `AIChatScreen.IsTypingActive` is checked in `PlayerPhoneUI.cs:1371` and `PlayerController`).
  - Not now → `Decline(b)`.
- `AwaitingCounterBack`: **[Take {counterBack}] [Decline]** — Take → window chips → `Accept`; Decline → `Decline(b)`.
- `Scheduled` / `None`: no chips (card or nothing).

After every chip action, force a thread rebuild (event count changed).

- [ ] **Step 3: Contact card** — `ShowCard(id)`: name, pips, "deals: N", then one line per unlocked reveal (`BuyerLedger.RevealCount` / `RevealLine`), locked slots shown as "— deal again to learn more —" (dim). Back → thread.

- [ ] **Step 4: Full-loop editor check** (cheats on): sell until converted (watch for guaranteed conversion on favourite tier) → F6 → notification → open Messages → thread → Counter modestly → accept/counter-back path → pick ~5 min → appointment card counts down with distance → walk over → "Deliver order" row → exact delivery → bond pips climb, FulfilledExact bubble appears. Then run one miss (F7) → bond halves, negative text, notification.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/UI/Messages/MessagesScreen.cs" "Assets/3 - Scripts/UI/PlayerPhoneUI.cs" \
        <PlayerController.cs if the typing guard touched it>
git commit -m "feat(messages): thread view, reply chips, appointment card, contact card"
```

---

### Task 9: Save round-trip + polish pass

**Files:** touched only as fixes demand.

- [ ] **Step 1: Save/load verification** — with a regular, an unread text, and a Scheduled appointment live: save (stasis pod or autosave), quit to menu, load. Verify: contact list intact, bond/pips intact, unread intact, appointment deadline resumed (± seconds), no message storm (staggered), thread history renders.
- [ ] **Step 2: New Game verification** — New Game from menu: Messages index empty, no ghost regulars.
- [ ] **Step 3: Regression sweep** — normal walk-up selling unchanged for non-regulars; HAL chat unaffected; phone camera/photos/build apps unaffected; Esc/pad-B backs out of every new screen (mirror `MushroomSellUI.Update`'s Esc handling in MessagesScreen).
- [ ] **Step 4: Update docs** — add a §Messages section to `docs/CURRENT_STATE_AUDIT.md` (CLAUDE.md: material system changes update the audit) and a pointer in `docs/MUSHROOM_ECONOMY.md`.
- [ ] **Step 5: Final commit + push to `soundofspace`** (canonical remote, branch feat/helmet-hud).

```bash
git add -A "Assets/3 - Scripts" docs
git commit -m "feat(messages): save round-trip fixes + audit update"
git push soundofspace feat/helmet-hud
```

---

## Self-review notes (already applied)

- **Spec coverage:** §1→T2, §2→T2/T4, §3→T2(ReportDeal), §4→T3/T5/T8, §5→T5/T6, §6→T2/T4/T8, §7→T7/T8, §8→T4/T6, §9→T2(CancelAppointmentQuietly, Eligible)/T5(stagger), §10→per-task checks + T9.
- **Known judgment calls an executor may adjust with reason:** the `BuyerCounterBack` event doubling as the "fine, but don't be late" acceptance line (b=1 flag) is a wording shortcut — if it renders confusingly, add a distinct `BuyerAgreed` event type (append to the enum, never renumber, saves carry ints).
- **Anything ambiguous:** resolve against the spec first, then match the nearest existing pattern in the file being edited.
