# Tape Formats: Demo / Half-Length / Full-Length — Design

**Date:** 2026-08-18 · **Approved by Sam** (chat, this session) · Branch: `feat/helmet-hud`

The game now starts as a *demo artist* career: cheap Demo tapes press one section,
and two bigger blank formats unlock as you sell — Half-Length and Full-Length
tapes that press the whole multi-section song. Aliens learn to process songs:
a many-genre song passes more aliens' gates, each fan's price reflects their
slice of the bars, and long-time demo customers say something nice when they
buy your first full song.

Decisions locked with Sam:

- **Caps:** Half-Length = 50 bars, Full-Length = 100 bars (engine composing max
  stays 128; the *print dialog* is the gate).
- **Unlock:** sales milestones — Half at **10 tapes sold**, Full at **25**.
- **Demo scope:** the whole selected section (its bar length, 1–16 bars, with
  the fill-bar ending) — no longer just the raw 4-bar loop.
- **Text orders go song-aware in this pass.**
- **Price model = Option 1, bar-weighted satisfaction** (see §5).

---

## 1. The six blanks

Existing `BlankTapeT1/T2` **become** the Demo blanks — display renamed, old
saves' blanks convert for free (item ids save as strings). Four new ItemIds
appended **at the end** of `Hotbar.ItemId` (append-only enum rule), after
`Cassette`:

```
BlankTapeHalfT1, BlankTapeHalfT2, BlankTapeFullT1, BlankTapeFullT2
```

| Shop entry | ItemId | Price | Presses | Cap |
|---|---|---|---|---|
| DEMO 1 | BlankTapeT1 | $5 | selected section | 1 section, ≤16 bars |
| DEMO 2 | BlankTapeT2 | $12 | selected section | 1 section, ≤16 bars |
| HALF-LENGTH 1 | BlankTapeHalfT1 | $15 | whole song | 50 bars |
| HALF-LENGTH 2 | BlankTapeHalfT2 | $25 | whole song | 50 bars |
| FULL-LENGTH 1 | BlankTapeFullT1 | $22 | whole song | 100 bars |
| FULL-LENGTH 2 | BlankTapeFullT2 | $35 | whole song | 100 bars |

- T2 demo blank price drops $15 → $12 (Sam's number).
- Display names: `DEMO TAPE` / `DEMO TAPE II` / `HALF TAPE` (+` II`) /
  `FULL TAPE` (+` II`). Stack cap 20 each, `IsResource`, storage-box legal —
  mirror the existing blank rows in `Hotbar.cs` / `StorageUI.cs`.
- **Kind** is a new axis beside tier, never folded into it (five documented
  1..2 tier clamps stay valid). Pure constants, Unity-free:

```csharp
public static class TraxKind   // Music/, pure — usable by the headless suites
{
    public const int Demo = 0, Half = 1, Full = 2;
    public static int BarCap(int kind);      // 16 / 50 / 100
    public static int SectionCap(int kind);  // 1 / TraxSong.MaxSections / TraxSong.MaxSections
    public static string Label(int kind);    // "DEMO" / "HALF" / "FULL"
}
```

- `TevMushroomOnboarding.StarterBlanks` stays 3× demo T1.
- Test stub enum in `prototypes/shuttle-computer/test/RentDeckStubs.cs` updated
  in lockstep with `Hotbar.ItemId`.

## 2. Tev's shop + milestone gating

`TevShopUI.Stock` grows to six tape entries (Tapes tab now 6 rows — `MaxRows`
is already 6; `_qty` array grows with `Stock.Length`).

- **World-scoped sales counter** (`TapesSold`), StoryDirector-counter style like
  the rent counters: world save + co-op shared + New Game reset for free.
  Incremented wherever a tape sale completes: walk-up close and order delivery.
  (Tev fronting is vaulted; if it ever returns, its sales count too.)
- Locked rows stay **visible** (visible-padlock philosophy): name shown, price
  replaced by `SELL N MORE TAPES`, buy disabled. Half unlocks at 10, Full at 25.
- On unlock, Tev sends a text (existing Tev thread machinery) telling the
  player new stock is in. If wiring is disproportionate, the text is the one
  cuttable item here.

## 3. Deck + printing

`CassetteDeck` gains `InsertedKind` beside `InsertedTier` (0 = empty stays the
"nothing inserted" sentinel on tier).

- `BlankIdFor(kind, tier)` maps the six ItemIds; `HeldBlankTier()` becomes
  `HeldBlank(out int kind, out int tier)` (old signature kept as a shim if
  call-site churn is smaller that way).
- Save: `TraxLibrarySave.deckInsertedKind` appended (+ both stub copies:
  `RentDeckStubs.cs`, `TraxLibraryTests.cs` doesn't carry deck fields — leave).
- `CassetteSlot` prompt names the blank: `INSERT HALF TAPE II`, etc.

**Print dialog** (`ShuttleComputerUI`) becomes kind-aware, driven by the
inserted blank:

- Demo blank: `PRESSES SEC B — 12 BARS` (the whole selected section, bars
  included, fill-bar ending — *not* just the 4-bar loop anymore).
- Half/Full blank: `PRESSES FULL TRACK — 34/50 BARS`. Over cap →
  `TRACK TOO LONG FOR THIS TAPE — 70/50 BARS`, print disabled (`canPrint`
  gate extended). The arranger itself stays uncapped up to the engine max.
- The "full-track tapes come later" honest label dies.

## 4. Every print is a song

`TraxPrints.Record` gains `TraxSong song` (frozen clone, never null) and
`int kind`. A demo is a one-section song — **one evaluation path, no parallel
demo/song cases**. `record.track` remains as an alias for
`song.sections[0].track` (named-request lineage, walkman fallback, etc. keep
working), but all *pricing/taste* sites move to song-aware helpers (§5).

**Ids.** New prints: `MakeId(kind, tier, songId)` → `d1-…`, `h2-…`, `f1-…` +
`SongId().ToString("x8")` (SongId already exists and is parity-tested against
`engine/song.js`). **Legacy compatibility:** save rows without sections load as
Demo-kind, one 4-bar section, and keep deriving the old `t{tier}-{trackId:x8}`
id — cassettes already in hotbars/lockers/deck still resolve. New demo prints
use the new scheme; a pre-revamp tape and a fresh identical pressing won't
stack — accepted one-time cosmetic cost.

**Save.** `TraxPrintSave` gains `List<TraxSectionSave> sections` + `int kind`,
exactly the `TraxProjectSave` precedent (empty list = legacy single track).
`TraxPrints.Apply` keeps re-deriving ids from loaded data (legacy branch for
rows without sections). Stub DTOs in `TraxLibraryTests.cs` + `RentDeckStubs.cs`
mirror field-for-field.

## 5. How aliens process a song (Option 1 — bar-weighted satisfaction)

New pure `SongEval` (Unity-free, lives with AlienTaste in the taste suite):

```csharp
public static class SongEval
{
    // Bar-weighted mean of per-section AlienTaste.Satisfaction.
    public static double Satisfaction(string alienId, TraxSong song);

    // Best verdict any section earns, each via AlienTaste.GateFor(id, dials_i,
    // sat_i, tapeTier). Hint contract holds per-section: a song containing the
    // alien's favourite genre is NEVER refused.
    public static AlienTaste.Verdict GateFor(string alienId, TraxSong song,
                                             int tapeTier);

    // The alien's favourite slice — feeds feedback lines ("the CLANG parts
    // are great") and the growth moment.
    public static int BestSection(string alienId, TraxSong song,
                                  out double bestSat);
}
```

- For a one-section song this reproduces today's numbers **exactly**
  (weighted mean of one = the old satisfaction; gate identical) — asserted in
  the taste suite — so all ~24 sell-path call sites can migrate to the
  song-aware chokepoints with zero behavior change for demos.
- The weighted satisfaction then flows through every existing formula
  (`SatisfactionMult`, feedback `SatBand`s, reveal lines, coin flips)
  untouched. Dilution *is* the weighting: a CLANG fan hearing a 5-genre epic
  lands ~45 sat — they buy (their slice passed the gate), they don't gush, and
  they don't lowball (§6's format multiplier).
- Feedback garnish: when a song is multi-genre and `bestSat − weightedSat` is
  large (> ~15), append a slice mention naming the best section's genre.
  Bands stay pinned to the gates (words never disagree with verdicts).

## 6. Money

`TapeValue.Base(modules, tier)` is unchanged; a **format multiplier** stacks on
it at tape-pricing sites:

```csharp
// TraxSong.cs — replaces the 2026-08-17 placeholders. STILL PLACEHOLDER
// NUMBERS — Sam tunes after playtest; shape is the decision.
public double ValueMult()   // Demo kind => 1.0 always
    => 1.25 + 0.25 * (sections.Count - 1) + 0.02 * (TotalBars() - 4);
```

- Max Full-Length (8 sections, 100 bars) ≈ **4.9×** a demo; max Half ≈ 3.9×.
  ⚠️ Tuning watch: the per-section term lets a 50-bar Half with 8 tiny
  sections near Full-Length value — if that plays badly, shift weight from the
  section term to the bar term, in **both** `TraxSong.cs` and
  `engine/song.js` (parity).
- `TraxSong.OfferMult` (explicit share × value) **dies** — Option 1 replaced
  the share term with satisfaction weighting. `engine/song.js`'s `offerMult`
  and the arranger's per-fan preview must change with it (promise/grade trap).
- Sanity, mid-game (4 modules, T1): demo ≈ $20; pure-genre Full to its fan
  ≈ $130 before payFactor (vs $22 blank); 5-genre Full ≈ $84 but passes almost
  every gate. Pure pays ~1.5× more per fan; diverse sells to far more fans.
- New helper so money math stays choked: `TapeValue.BaseFor(record)` =
  `Base(modules, tier) × record.song.ValueMult()` (Demo → ×1.0). All tape
  `Base(...)` call sites (MushroomSellUI ×3, DealTerms, TevFronting) move to it.
- `diagnose:taste` gains a song mode — **distribution, not mean** — before any
  claim the economy works.

## 7. Selling paths

**Walk-up (`MushroomSellUI` + `TapeOffer`):**
- `ListenOnTable` → `SongEval.Satisfaction` / `SongEval.GateFor`;
  `TapeOffer.Listen` gains a song-aware overload (`GateFor` rule holds — no
  new sale path may call raw `Gate`).
- Sell rows / bar rows show a kind chip (DEMO/HALF/FULL) beside the tier chip.
- Negotiation (`Judge`, `FinalOffer`, ceiling, bond) unchanged — they act on
  the value, which now carries the format multiplier.

**Text orders (`BuyerLedger` + `BuyerMessageDirector` + `TapeDeal.Grade`):**
- `Buyer.askKind` appended (0 = legacy → Demo), same guarded-list pattern as
  `askTapeTier`.
- Want-texts may request Half/Full **only after** the kind is milestone-
  unlocked; copy names it ("a full-length with some VOLT on it").
- Quoting needs a nominal mult per kind (the song doesn't exist yet):
  `NominalMult = { Demo 1.0, Half 2.0, Full 3.5 }` (placeholder consts in
  `DealTerms`). Contract price stays THE contract for exact goods (Sam's law).
- Grade: goods basis becomes `Base × mult` on both sides — delivered kind's
  actual `ValueMult` vs contract kind's nominal, **capped at 1** (over-
  delivering doesn't raise the agreed price; short-delivering pro-rates down,
  mirroring the T1-on-T2 rule). `fillsGenre` = **any section** carries the
  genre (`TapeTrade.Fills` gains a song overload). `alreadyHeard` per §8.

## 8. Memory + repeat rules (`TapeMemory`)

- Demos: unchanged — dial-closeness (`SameSongDistance`) on the section's dials.
- Songs (multi-section prints): remembered by **`SongId`** — new
  `heardSongs` / `boughtSongs` uint lists per entry (+ `TapeMemorySave` fields
  + `SaveStubs.cs`). An alien who bought section-A's *demo* CAN buy the full
  song containing A (that's the growth moment); the same song twice, no.
- Known soft spot, accepted for v1: nudging one variation changes SongId, so
  the demo-style closeness guard doesn't protect songs. Note it; revisit if
  playtest shows farming.

## 9. Fan progression ("keep up the good music")

- `BuyerLedger.Buyer.songsBought` appended (guarded for old saves).
- Trigger: a completed **song** sale with verdict Liked, `songsBought == 0`,
  and `dealsCompleted ≥ 3` (they were a demo customer) → line from a new
  `AlienFeedback.ForGrowth(variant)` bank (~6 authored lines: "You've come a
  long way from those demos — keep up the good music.", etc.) + **bond +3**.
- Every song deal increments `songsBought`.

## 10. Walkman + audio truth

`TraxTapePlayer` learns song playback: a Half/Full tape plays **all** its
sections in order (fill-bar remap, transitions) and loops the whole song —
the audio-form promise/grade match. Demo tapes behave as today. The engine
already renders songs (`PublishSong` path); the tape player reuses it rather
than growing a parallel scheduler.

## 11. Displayed-promise audit (the trap class)

Every surface that names a number or a product must match the grader:

- Arranger worth readout + per-fan preview → the §5/§6 model, not `OfferMult`.
- Print dialog copy per kind (§3).
- Sell panel rows: kind chip + tier chip; walk-up "which buyer" hint bands
  re-checked against the new value spread.
- Tev shop descriptions state what each blank records and its bar cap.
- `BuyerLedger.RevealLine` bands: re-derive if the value distribution moved
  (the standing rule from the falloff rebalance).

## 12. Multiplayer + saves + resets

- Prints/deck/library already world-scoped and version-replicated; the sales
  counter rides the story-counter replication like rent. Guest sales route
  through `EconomySync.KindTapeSale` unchanged (printId resolves via the
  shared table).
- New Game: `TraxPrints.Clear` etc. already run; the counter resets with
  StoryDirector; `songsBought`/`askKind` live in BuyerLedger which resets.
- All new save fields are appended + guarded; no existing field moves.

## 13. Tests (all headless, before any Unity claim)

- `verify-library`: song print register/round-trip, legacy row → demo
  migration keeps old ids, kind clamps, deck kind save.
- `verify-rent`: deck insert/eject per kind, six-blank mapping.
- `verify-taste`: SongEval demo-parity (exact), weighted sat, best-section
  gate, per-section hint contract, kind-aware Grade pro-rata, growth-line
  trigger, nominal-mult quoting.
- `test/run.js`: `songValueMult` goldens re-pinned; `offerMult` removed/replaced.
- `compile-unity.py` before claiming any C# change compiles.

## 14. Out of scope / deferred

- Named song requests by SongId (named requests stay trackId/demo lineage).
- Song-closeness repeat guard (§8 soft spot).
- Aliens with a preferred key (still parked, Sam's idea).
- Browser prototype gets only lockstep engine edits (`song.js`) — no
  prints.js/deck; the playtest surface for this feature is Unity.
