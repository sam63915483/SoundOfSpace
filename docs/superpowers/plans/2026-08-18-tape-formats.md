# Tape Formats (Demo / Half / Full) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Six blank-tape kinds (Demo/Half/Full × T1/T2), song-carrying prints, bar-weighted alien evaluation of multi-genre songs, milestone-gated shop stock, song-aware text orders, and fan-progression dialogue.

**Architecture:** Every print becomes a frozen `TraxSong` (demo = one section) so there is ONE evaluation path. New pure files `TraxKind.cs`, `SongEval.cs`, `TapeCareer.cs` join the headless suites; a `kind` axis rides beside `tier` everywhere (never folded into it). Price = `TapeValue.Base × FormatMult × SatisfactionMult(bar-weighted sat)`; verdict = best section through `GateFor`.

**Tech Stack:** Unity 2022.3 C# (Assembly-CSharp), headless suites `prototypes/shuttle-computer/test/verify-{library,rent,taste,port}.py` + `npm test`, `compile-unity.py` for compile checks. Spec: `docs/superpowers/specs/2026-08-18-tape-formats-design.md`.

**Ground rules for every task:**
- After each task run `python prototypes/shuttle-computer/test/compile-unity.py` (must end `OK`) plus the suites named in the task. Never claim compilation without it.
- New `.cs` files: `git add` the source now; the `.meta` appears on Sam's next Unity import and must be committed then (note it in the commit body).
- All new save fields are APPENDED and count-guarded. `Hotbar.ItemId` values go at the END.
- The stub files `test/RentDeckStubs.cs`, `test/TraxLibraryTests.cs` (its DTO stub block), `test/SaveStubs.cs` must mirror `SaveData.cs` field-for-field — every task that touches a DTO updates its stubs in the same commit.

**File map (who owns what):**
| File | Role in this feature |
|---|---|
| `Assets/3 - Scripts/Music/TraxKind.cs` (new) | kind constants, bar caps, prices, milestones, id prefixes, FormatMult |
| `Assets/3 - Scripts/Music/SongEval.cs` (new) | pure song-vs-alien evaluation (weighted sat, best-section gate) |
| `Assets/3 - Scripts/Music/TapeCareer.cs` (new) | StoryDirector-backed TapesSold counter + unlock queries |
| `TraxSong.cs` | retuned ValueMult; OfferMult deleted |
| `TraxPrints.cs` | song-carrying Record, kind-prefixed ids, legacy-id load branch |
| `CassetteDeck.cs` / `CassetteSlot.cs` | InsertedKind beside InsertedTier |
| `TapeValue.cs` / `TapeOffer.cs` / `DealTerms.cs` | formatMult in the money path; song Listen; kind-aware Grade |
| `TapeMemory.cs` | songId-keyed heard/bought lists |
| `AlienTaste.cs` / `AlienFeedback.cs` | KindPreference trait; slice + growth line banks |
| `Hotbar.cs` / `StorageUI.cs` | four new blank ItemIds end-to-end |
| `TevShopUI.cs` | six blank rows, milestone locks |
| `ShuttleComputerUI.cs` | kind-aware print dialog + DoPrint |
| `ShuttleComputerArrangerUI.cs` | honest worth readout (OfferMult preview dies) |
| `MushroomSellUI.cs` / `TapeTrade.cs` / `NPCSellRows.cs` / `TevFronting.cs` | sell paths through the song chokepoints |
| `BuyerLedger.cs` / `BuyerMessageDirector.cs` / `BuyerTexts.cs` | askKind, songsBought, kind-aware want texts, growth trigger |
| `EconomySync.cs` | songId+kind on the KindTapeSale / KindTapeHeard wire |
| `TraxTapePlayer.cs` | song playback for held/table tapes |
| `SaveData.cs` | DTO fields (prints sections+kind, deck kind, memory songs, ledger lists) |
| `engine/song.js`, `ui/trax.js`, `test/run.js` | browser lockstep: ValueMult constants, offerMult removal |

---

### Task 1: TraxKind + retuned ValueMult + honest arranger readout (JS lockstep)

**Files:**
- Create: `Assets/3 - Scripts/Music/TraxKind.cs`
- Modify: `Assets/3 - Scripts/Music/TraxSong.cs` (ValueMult retune, delete OfferMult, TraxKind mention in the economy comment)
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerArrangerUI.cs:557-570` (readout)
- Modify: `prototypes/shuttle-computer/engine/song.js:229-246`, `prototypes/shuttle-computer/test/run.js:581-591`
- Modify: `prototypes/shuttle-computer/ui/trax.js` (any `offerMult` import/usage — grep it)
- Modify: `prototypes/shuttle-computer/test/verify-library.py:27-38` and `verify-rent.py:31-44` SOURCES (+ TraxKind.cs)

- [ ] **Step 1: Create `TraxKind.cs`** (pure, no Unity):

```csharp
/// <summary>
/// The three blank-tape FORMATS. A kind rides BESIDE tier (1/2), never inside
/// it — five separate code sites clamp tier to 1..2 and all of them stay true.
///
/// Demo presses one section; Half and Full press the whole song, capped by
/// bars. Prices and milestones are Sam's 2026-08-18 numbers (spec:
/// docs/superpowers/specs/2026-08-18-tape-formats-design.md).
///
/// PURE — compiled by the library, rent and taste suites.
/// </summary>
public static class TraxKind
{
    public const int Demo = 0;
    public const int Half = 1;
    public const int Full = 2;

    // Blank prices. Tev's shop reads these so the catalogue can never drift.
    public const int DemoT1Price = 5,  DemoT2Price = 12;
    public const int HalfT1Price = 15, HalfT2Price = 25;
    public const int FullT1Price = 22, FullT2Price = 35;

    // Career milestones: total tapes sold before Tev stocks the bigger blanks.
    public const int HalfUnlockSales = 10;
    public const int FullUnlockSales = 25;

    // What a text order QUOTES for a song it has not heard yet (the song does
    // not exist at quote time). ⚠️ PLACEHOLDER — Sam tunes.
    public const double DemoNominalMult = 1.0;
    public const double HalfNominalMult = 2.0;
    public const double FullNominalMult = 3.5;

    public static int Clamp(int kind) { return kind < Demo ? Demo : kind > Full ? Full : kind; }

    /// Longest song this blank can carry. Demo's cap is per-section anyway.
    public static int BarCap(int kind)
    {
        return kind == Full ? 100 : kind == Half ? 50 : TraxSong.SectionMaxBars;
    }

    public static int SectionCap(int kind) { return kind == Demo ? 1 : TraxSong.MaxSections; }

    public static string Label(int kind) { return kind == Full ? "FULL" : kind == Half ? "HALF" : "DEMO"; }

    /// Print-id prefix — 'd'/'h'/'f'. Legacy demo ids keep their old "t" form
    /// on load (TraxPrints.Apply) so saved hotbar tapes still resolve.
    public static char IdPrefix(int kind) { return kind == Full ? 'f' : kind == Half ? 'h' : 'd'; }

    public static double NominalMult(int kind)
    {
        return kind == Full ? FullNominalMult : kind == Half ? HalfNominalMult : DemoNominalMult;
    }

    /// The format multiplier a PRESSED tape's value carries. Demos are the
    /// baseline product (×1.0); songs grow with sections and length.
    public static double FormatMult(int kind, TraxSong song)
    {
        if (kind == Demo || song == null) return 1.0;
        return song.ValueMult();
    }
}
```

- [ ] **Step 2: Retune `TraxSong.ValueMult` and delete `OfferMult`.** In `TraxSong.cs` replace the whole `// ── economy ──` block (lines 273–295) with:

```csharp
    // ── economy ──────────────────────────────────────────────────────────
    // ⚠️ TUNING PLACEHOLDERS — Sam sets the real numbers. Option 1 (2026-08-18):
    // an alien's satisfaction with a song is the BAR-WEIGHTED mean of their
    // per-section satisfaction (SongEval), so dilution happens in the
    // satisfaction term; this multiplier is what makes a full song beat a demo
    // outright. Do not add a per-genre share multiplier back — that was the
    // old OfferMult model and it made the spoken verdict disagree with the
    // money (the promise/grade trap class).

    /// Full-track value as a multiple of the demo price for the same loop.
    /// Max Full-Length (8 sections, 100 bars) ≈ 4.9×; max Half ≈ 3.9×.
    public double ValueMult()
    {
        return 1.25 + 0.25 * (sections.Count - 1) + 0.02 * (TotalBars() - 4);
    }
```

- [ ] **Step 3: Fix the arranger readout** (the only `OfferMult` caller). In `ShuttleComputerArrangerUI.RefreshArrangerValue()` replace lines 557–570 with:

```csharp
        _arrValue.text = "FULL TRACK x" + _song.ValueMult().ToString("0.00") + " DEMO BASE";

        // Per-fan ×N previews died with OfferMult: under the weighted-sat
        // model the real number depends on the listener, and a number we
        // cannot keep is a promise we must not print. Names only.
        var sb = new System.Text.StringBuilder();
        sb.Append("SELLS TO ");
        int shown = mix.Count < 3 ? mix.Count : 3;
        for (int i = 0; i < shown; i++)
        {
            if (i > 0) sb.Append(" · ");
            Color gc = GenreColorOf(mix[i].name);
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(gc)).Append('>');
            sb.Append(mix[i].name).Append(" FANS</color>");
        }
        if (mix.Count > 3) sb.Append(" +").Append(mix.Count - 3);
        _arrOffers.text = sb.ToString();
```

- [ ] **Step 4: JS lockstep.** In `engine/song.js`: change `songValueMult` to `return 1.25 + 0.25 * (sections - 1) + 0.02 * (bars - 4);`, delete `offerMult` entirely, and rewrite the economy comment block to say satisfaction-weighting replaced share-multiplication (mirror the C# comment). Grep `ui/trax.js` for `offerMult` — if the arranger strip renders per-fan numbers, replace with the same "SELLS TO <genres>" treatment; `refreshPrintSub`'s `songValueMult` call stays.
- [ ] **Step 5: Update `test/run.js`** test `'value grows with sections and with bars; offers dilute by share'` (lines 581–591): keep the two `songValueMult` growth asserts, delete the two `offerMult` asserts, rename the test `'value grows with sections and with bars'`. Remove `offerMult` from the song.js import list if named there.
- [ ] **Step 6: Add `"TraxKind.cs"` to SOURCES** in `verify-library.py` (after `"TraxSong.cs"`) and the full path `os.path.join(SCRIPTS, "Music", "TraxKind.cs")` in `verify-rent.py` (after TraxSong line).
- [ ] **Step 7: Verify.** Run:
  - `cd prototypes/shuttle-computer && npm test` → all pass
  - `python prototypes/shuttle-computer/test/verify-library.py` → OK
  - `python prototypes/shuttle-computer/test/verify-rent.py` → OK
  - `python prototypes/shuttle-computer/test/compile-unity.py` → OK
- [ ] **Step 8: Commit** `feat(trax): TraxKind format axis + retuned song value, OfferMult dies`

### Task 2: SongEval + AlienTaste.KindPreference (pure taste layer)

**Files:**
- Create: `Assets/3 - Scripts/Music/SongEval.cs`
- Modify: `Assets/3 - Scripts/Music/AlienTaste.cs` (KindPreference block, after the tier-preference block ~line 310)
- Modify: `prototypes/shuttle-computer/test/verify-taste.py:27-43` and `verify-diagnostic.py:27-40` SOURCES (+ `"TraxSong.cs"`, `"TraxKind.cs"`, `"SongEval.cs"`)
- Test: `prototypes/shuttle-computer/test/AlienTasteTests.cs` (append checks; follow the file's existing `Check(...)`/counter style — read its head first)

- [ ] **Step 1: Write the failing tests.** Append to `AlienTasteTests.cs` (adapting to its existing assert helper names):

```csharp
    static TraxSong OneSectionSong(TraxTrack t, int bars)
    {
        var s = new TraxSong();
        s.sections.Add(new TraxSection(t, bars));
        return s;
    }

    static void SongEvalChecks()
    {
        // Demo parity: a one-section song scores EXACTLY like the bare track.
        var t = TraxTrack.Default();
        t.dials = new TraxDials(8, 2, 5, 3, 7, 1);
        string id = "alien:parity";
        double bare = AlienTaste.Satisfaction(id, SongEval.DialsOf(t));
        double song = SongEval.Satisfaction(id, OneSectionSong(t, 4));
        Check(System.Math.Abs(bare - song) < 1e-9, "demo parity: satisfaction identical");
        Check(SongEval.GateFor(id, OneSectionSong(t, 4), 1)
              == AlienTaste.GateFor(id, SongEval.DialsOf(t), bare, 1), "demo parity: gate identical");

        // Weighted sat sits between the best and worst section, pulled by bars.
        var loved = TraxTrack.Default();     // place ON a fan's ear
        string fan = "alien:weighted";
        double[] taste = AlienTaste.TastePoint(fan);
        for (int d = 0; d < AlienTaste.DialCount; d++) loved.dials = loved.dials.With(d, taste[d]);
        var hated = TraxTrack.Default();
        for (int d = 0; d < AlienTaste.DialCount; d++) hated.dials = hated.dials.With(d, taste[d] > 5 ? 0 : 10);
        var two = new TraxSong();
        two.sections.Add(new TraxSection(loved, 12));
        two.sections.Add(new TraxSection(hated, 4));
        double satLoved = AlienTaste.Satisfaction(fan, SongEval.DialsOf(loved));
        double satHated = AlienTaste.Satisfaction(fan, SongEval.DialsOf(hated));
        double w = SongEval.Satisfaction(fan, two);
        Check(w < satLoved && w > satHated, "weighted sat between extremes");
        Check(System.Math.Abs(w - (satLoved * 12 + satHated * 4) / 16.0) < 1e-9, "weighted by bars exactly");

        // Best-section gate + hint contract: a song CONTAINING the fan's
        // favourite genre is never Rejected, however much filler surrounds it.
        int gi = AlienTaste.FavouriteGenreIndex(fan);
        var onGenre = TraxTrack.Default();
        double[] centre = TraxClassifier.Genres[gi].c;
        for (int d = 0; d < AlienTaste.DialCount; d++) onGenre.dials = onGenre.dials.With(d, centre[d]);
        var mixed = new TraxSong();
        mixed.sections.Add(new TraxSection(hated, 16));
        mixed.sections.Add(new TraxSection(onGenre, 2));
        Check(SongEval.GateFor(fan, mixed, 1) != AlienTaste.Verdict.Rejected,
              "hint contract holds per-section in a song");
        Check(SongEval.MatchesFavourite(fan, mixed), "MatchesFavourite sees any section");
        double bs;
        Check(SongEval.BestSection(fan, mixed, out bs) == 1, "best section found");

        // Kind preference partitions the population deterministically.
        int nd = 0, nh = 0, nf = 0;
        for (int i = 0; i < 500; i++)
        {
            int k = AlienTaste.KindPreference("alien:" + i);
            if (k == TraxKind.Demo) nd++; else if (k == TraxKind.Half) nh++; else nf++;
        }
        Check(nd > 100 && nh > 100 && nf > 60, "kind preference spans the population");
        Check(AlienTaste.KindPreference("alien:1") == AlienTaste.KindPreference("alien:1"), "stable");
    }
```
Call `SongEvalChecks();` from the file's `Main` alongside the existing groups.

- [ ] **Step 2: Run the suite, expect compile failure** (`SongEval` missing): `python prototypes/shuttle-computer/test/verify-taste.py` — after first adding the three SOURCES entries to `verify-taste.py` and `verify-diagnostic.py`.
- [ ] **Step 3: Create `SongEval.cs`:**

```csharp
/// <summary>
/// How an alien hears a SONG — the Option 1 model (spec 2026-08-18).
///
/// SATISFACTION is the bar-weighted mean of per-section satisfaction: their
/// slice pulls it up, the filler pulls it down, and the one number then flows
/// through every existing formula (SatisfactionMult, feedback bands, reveal
/// lines) untouched. VERDICT is the best any section earns through GateFor,
/// so a song containing the alien's favourite genre is never refused — the
/// hint contract holds per-section.
///
/// A one-section song reproduces the single-track numbers EXACTLY (asserted
/// in the taste suite), which is what let every sell-path call site migrate
/// here with zero behaviour change for demos.
///
/// PURE — no Unity types; runs in verify-taste with AlienTaste.
/// </summary>
public static class SongEval
{
    /// The six dials as the taste model speaks them. A deliberate local copy
    /// of TapeTrade.DialsOf — that file imports UnityEngine and depending on
    /// it would cost this class its headless run (same reasoning as
    /// AlienTaste's private hash).
    public static double[] DialsOf(TraxTrack track)
    {
        var d = new double[AlienTaste.DialCount];
        if (track == null) return d;
        for (int i = 0; i < d.Length && i < TraxPrng.DialCount; i++) d[i] = track.dials.Get(i);
        return d;
    }

    /// 0..100, weighted by each section's share of the bars.
    public static double Satisfaction(string alienId, TraxSong song)
    {
        if (song == null || song.sections.Count == 0) return 0.0;
        double total = 0.0, bars = 0.0;
        for (int i = 0; i < song.sections.Count; i++)
        {
            TraxSection sec = song.sections[i];
            total += AlienTaste.Satisfaction(alienId, DialsOf(sec.track)) * sec.bars;
            bars += sec.bars;
        }
        return bars > 0 ? total / bars : 0.0;
    }

    /// Best verdict any section earns — each judged by the full tier-aware
    /// GateFor, so the hint contract and the shell-preference downgrade both
    /// apply per-section. Never call raw Gate from a sale path.
    public static AlienTaste.Verdict GateFor(string alienId, TraxSong song, int tapeTier)
    {
        var best = AlienTaste.Verdict.Rejected;
        if (song == null) return best;
        for (int i = 0; i < song.sections.Count; i++)
        {
            double[] dials = DialsOf(song.sections[i].track);
            double sat = AlienTaste.Satisfaction(alienId, dials);
            AlienTaste.Verdict v = AlienTaste.GateFor(alienId, dials, sat, tapeTier);
            if (v > best) best = v;
            if (best == AlienTaste.Verdict.Liked) return best;
        }
        return best;
    }

    /// Does ANY section classify under their favourite genre (primary or
    /// blend secondary)? Drives the taste-match bond and regular conversion.
    public static bool MatchesFavourite(string alienId, TraxSong song)
    {
        if (song == null) return false;
        for (int i = 0; i < song.sections.Count; i++)
            if (AlienTaste.MatchesFavourite(alienId, DialsOf(song.sections[i].track))) return true;
        return false;
    }

    /// The alien's favourite slice — feeds "the CLANG parts are great" lines
    /// and picks whose dials a rejection complains about.
    public static int BestSection(string alienId, TraxSong song, out double bestSat)
    {
        bestSat = 0.0;
        if (song == null || song.sections.Count == 0) return 0;
        int best = 0;
        for (int i = 0; i < song.sections.Count; i++)
        {
            double s = AlienTaste.Satisfaction(alienId, DialsOf(song.sections[i].track));
            if (i == 0 || s > bestSat) { bestSat = s; best = i; }
        }
        return best;
    }
}
```

- [ ] **Step 4: Add `KindPreference` to `AlienTaste.cs`** after the tier-preference block (after `TierPayFactor`, ~line 310):

```csharp
    // ── Tape-format preference (2026-08-18, tape formats) ─────────────────
    //
    // Which blank a buyer's text orders ask for once the career has unlocked
    // it. Its own salt — a shell snob is not therefore a full-length head.
    // Demo-preferring buyers keep the early game's texture; full-length fans
    // are the premium commissions (NominalMult makes their orders pay).

    public const double KindFullShare = 0.25;
    public const double KindHalfShare = 0.35;   // remainder ~40% sticks to demos

    /// TraxKind.Demo / Half / Full — what this buyer WOULD order, before the
    /// career gate clamps it (TapeTrade.PickAskKind does the clamping).
    public static int KindPreference(string id)
    {
        double u = Unit(H(id, ":kindpref"));
        if (u < KindFullShare) return TraxKind.Full;
        if (u < KindFullShare + KindHalfShare) return TraxKind.Half;
        return TraxKind.Demo;
    }
```

- [ ] **Step 5: Run the suites green:** `verify-taste.py` → all checks pass (count increases), `verify-diagnostic.py --help`-less smoke not needed (it compiles as part of its own run; run it once), `compile-unity.py` → OK.
- [ ] **Step 6: Commit** `feat(taste): SongEval weighted-satisfaction model + kind preference trait`

### Task 3: Money path — TapeValue formatMult + TapeOffer song overloads

**Files:**
- Modify: `Assets/3 - Scripts/Music/TapeValue.cs` (add overloads after `Base` line 71 and `For` line 105)
- Modify: `Assets/3 - Scripts/Music/TapeOffer.cs` (song Listen + Value overloads)
- Test: `prototypes/shuttle-computer/test/TapeOfferTests.cs` (append; match its existing style)

- [ ] **Step 1: Failing tests** (append to `TapeOfferTests.cs`, wire into its Main):

```csharp
    static void FormatMultChecks()
    {
        // FormatMult scales Base linearly and never below 1×.
        Check(System.Math.Abs(TapeValue.Base(4, 1, 2.0) - TapeValue.Base(4, 1) * 2.0) < 1e-9, "mult scales base");
        Check(System.Math.Abs(TapeValue.Base(4, 1, 0.5) - TapeValue.Base(4, 1)) < 1e-9, "mult clamps at 1");

        // Song Listen: memory keys on songId for Half/Full, dials for Demo.
        TapeMemory.Clear();
        var t = TraxTrack.Default();
        var song = new TraxSong(); song.sections.Add(new TraxSection(t, 4));
        string id = "alien:songmem";
        uint sid = song.SongId();
        TapeMemory.RememberSong(id, sid);
        double sat; AlienTaste.Verdict v;
        Check(TapeOffer.Listen(id, song, sid, TraxKind.Half, 1, true, out sat, out v)
              == TapeOffer.Reaction.AlreadyHeard, "half tape blocked by songId memory");
        Check(TapeOffer.Listen(id, song, sid, TraxKind.Demo, 1, true, out sat, out v)
              != TapeOffer.Reaction.AlreadyHeard, "demo unaffected by songId memory");
        TapeMemory.Clear();
    }
```
(This also fails until Task 4/5 add `RememberSong` — implement Steps 2–3 here, then the TapeMemory halves land in Task 5; run order below accounts for it: comment the two memory checks with `// ENABLED IN TASK 5` and enable them there, so this task stays green on its own.)

- [ ] **Step 2: TapeValue overloads** (insert after the existing `Base` and `For`):

```csharp
    /// <summary>
    /// Base with the tape-format multiplier (TraxKind.FormatMult): a demo is
    /// ×1.0, a pressed song grows with sections and length. Clamped at 1 so a
    /// malformed record can never make a song WORSE than its own demo.
    /// </summary>
    public static double Base(int activeModules, int tier, double formatMult)
    {
        return Base(activeModules, tier) * (formatMult < 1.0 ? 1.0 : formatMult);
    }

    /// Format-aware full figure — the song path's twin of For().
    public static int For(int activeModules, int tier, double formatMult, double satisfaction,
                          int bond, bool matchesRequest, double payFactor)
    {
        double v = Base(activeModules, tier, formatMult)
                 * SatisfactionMult(satisfaction)
                 * BondMult(bond)
                 * (matchesRequest ? RequestBonus : 1.0)
                 * payFactor;
        int rounded = (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
        return rounded < 1 ? 1 : rounded;
    }
```

- [ ] **Step 3: TapeOffer song overloads** (insert after the existing three-`out` Listen, and after Value):

```csharp
    /// <summary>
    /// Song-aware listen — the ONE walk-up entry for a pressed tape of any
    /// kind. Memory keys on the PRODUCT: a demo is matched by dial closeness
    /// (a re-roll is the same demo), a Half/Full song by its SongId — so an
    /// alien who bought a section's demo can still buy the full song (the
    /// growth moment), but never the same song twice.
    /// </summary>
    public static Reaction Listen(string alienId, TraxSong song, uint songId, int kind,
                                  int tapeTier, bool coinFlip,
                                  out double satisfaction, out AlienTaste.Verdict verdict)
    {
        satisfaction = SongEval.Satisfaction(alienId, song);
        verdict = AlienTaste.Verdict.Rejected;
        if (song == null || song.sections.Count == 0) return Reaction.Rejected;

        bool heard = kind == TraxKind.Demo
            ? TapeMemory.HasHeard(alienId, SongEval.DialsOf(song.sections[0].track))
            : TapeMemory.HasHeardSong(alienId, songId);
        if (heard) return Reaction.AlreadyHeard;

        verdict = SongEval.GateFor(alienId, song, tapeTier);   // GateFor rule, per-section
        if (verdict == AlienTaste.Verdict.Liked) return Reaction.Liked;
        if (verdict == AlienTaste.Verdict.CoinFlip) return coinFlip ? Reaction.Liked : Reaction.Rejected;
        return Reaction.Rejected;
    }

    /// Format-aware value — the song path's twin of Value().
    public static int Value(string alienId, int activeModules, int tier, double formatMult,
                            double satisfaction, bool matchesRequest, int bond)
    {
        return TapeValue.For(activeModules, tier, formatMult, satisfaction,
                             bond, matchesRequest,
                             AlienTaste.PayFactor(alienId)
                             * AlienTaste.TierPayFactor(alienId, tier));
    }
```
(`TapeMemory.HasHeardSong` does not exist yet — add a temporary forwarding stub is NOT allowed; instead do Task 5's TapeMemory core (`heardSongs` list + `HasHeardSong`/`RememberSong` methods only, no save fields) in THIS commit, and leave the save/DTO halves to Task 5. Keep the suites green at every commit.)

- [ ] **Step 4: Run** `verify-taste.py` (green, higher count), `compile-unity.py` OK.
- [ ] **Step 5: Commit** `feat(economy): format multiplier in the money path + song-aware Listen/Value`

### Task 4: TraxPrints carries songs; save DTOs + stubs

**Files:**
- Modify: `Assets/3 - Scripts/Music/TraxPrints.cs` (whole-file rework of Record/Register/MakeId/Capture/Apply)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs:517-528` (`TraxPrintSave` += `kind`, `sections`)
- Modify: `prototypes/shuttle-computer/test/RentDeckStubs.cs:197-207` and `test/TraxLibraryTests.cs` DTO stub block (`TraxPrintSave` += same two fields)
- Test: `prototypes/shuttle-computer/test/TraxLibraryTests.cs` (append print-song checks)

- [ ] **Step 1: Failing tests** (append to `TraxLibraryTests.cs`, following its existing print tests ~line 303):

```csharp
    static void PrintSongChecks()
    {
        TraxPrints.Clear();
        var a = TraxTrack.Default();
        var b = TraxTrack.Default(); b.dials = b.dials.With(0, 9.0);
        var song = new TraxSong();
        song.sections.Add(new TraxSection(a, 8));
        song.sections.Add(new TraxSection(b, 4));

        var rec = TraxPrints.Register("EPIC", song, TraxKind.Half, 2);
        Check(rec.kind == TraxKind.Half, "kind stored");
        Check(rec.id[0] == 'h' && rec.id[1] == '2', "kind-prefixed id");
        Check(rec.songId == song.SongId(), "songId derived");
        Check(rec.song.sections.Count == 2, "sections frozen");
        Check(ReferenceEquals(rec.track, rec.song.sections[0].track), "track aliases section 0");
        Check(rec.trackId == rec.track.TrackId(), "trackId is section-0 lineage");
        Check(System.Math.Abs(rec.FormatMult - song.ValueMult()) < 1e-9, "record carries its mult");

        // Demo register (legacy shim) is a 1-section 4-bar song, d-prefixed.
        var demo = TraxPrints.Register("LOOP", a, 1);
        Check(demo.kind == TraxKind.Demo && demo.song.sections.Count == 1
              && demo.song.sections[0].bars == 4 && demo.id[0] == 'd', "legacy shim = demo");
        Check(System.Math.Abs(demo.FormatMult - 1.0) < 1e-9, "demo mult is 1");

        // Round-trip: sections and kind survive; ids re-derive identically.
        var save = new TraxLibrarySave();
        TraxPrints.Capture(save);
        TraxPrints.Apply(save);
        var back = TraxPrints.Get(rec.id);
        Check(back != null && back.kind == TraxKind.Half && back.song.sections.Count == 2
              && back.song.sections[0].bars == 8, "song round-trips");

        // A LEGACY row (no sections, no kind) keeps its old t-prefixed id.
        var legacyRow = new TraxPrintSave { id = "ignored", name = "OLD", tier = 1, key = a.key };
        for (int d = 0; d < TraxPrng.DialCount; d++) legacyRow.dials.Add((float)a.dials.Get(d));
        for (int m = 0; m < TraxPresets.ModuleCount; m++)
        { legacyRow.preset.Add(a.preset[m]); legacyRow.variation.Add(a.variation[m]); legacyRow.active.Add(a.active[m]); }
        var legacySave = new TraxLibrarySave();
        legacySave.prints.Add(legacyRow);
        TraxPrints.Apply(legacySave);
        var old = TraxPrints.Get("t1-" + a.TrackId().ToString("x8"));
        Check(old != null && old.kind == TraxKind.Demo && old.song.sections.Count == 1,
              "legacy row loads as demo under its old id");
        TraxPrints.Clear();
    }
```
Run `verify-library.py`, expect compile errors (missing members).

- [ ] **Step 2: Rework `TraxPrints.cs`.** Record and id scheme:

```csharp
    public sealed class Record
    {
        public string id;
        public string name;          // the project name at the moment it was printed
        public int tier;             // 1 or 2
        public int kind;             // TraxKind.Demo / Half / Full
        public TraxSong song;        // frozen copy — NEVER null; a demo is one section
        public uint songId;
        public TraxTrack track;      // ALIAS of song.sections[0].track (lineage + legacy readers)
        public uint trackId;

        /// What this pressing multiplies TapeValue.Base by (1.0 for demos).
        public double FormatMult { get { return TraxKind.FormatMult(kind, song); } }
    }

    /// New prints: kind-prefixed over the SONG identity. Same song, same kind,
    /// same tier → the same pressing, and the tapes stack.
    public static string MakeId(int kind, int tier, uint songId)
    {
        return TraxKind.IdPrefix(kind).ToString() + tier + "-" + songId.ToString("x8");
    }

    /// Pre-format saves used "t{tier}-{trackId:x8}". Rows without sections
    /// keep deriving it so cassettes already in hotbars/lockers still resolve.
    static string LegacyId(int tier, uint trackId)
    {
        return "t" + tier + "-" + trackId.ToString("x8");
    }
```

Register (replaces the old one; keep a shim):

```csharp
    public static Record Register(string name, TraxSong song, int kind, int tier)
    {
        if (song == null || song.sections.Count == 0) return null;
        if (tier < 1) tier = 1;
        if (tier > 2) tier = 2;
        kind = TraxKind.Clamp(kind);

        TraxSong frozen = song.Clone();
        uint sid = frozen.SongId();
        string id = MakeId(kind, tier, sid);

        Record existing;
        if (_byId.TryGetValue(id, out existing))
        {
            string fresh = TraxLibrary.NormalizeName(name);
            if (!string.IsNullOrEmpty(fresh)) existing.name = fresh;
            return existing;
        }

        var rec = new Record
        {
            id = id,
            name = TraxLibrary.NormalizeName(name),
            tier = tier,
            kind = kind,
            song = frozen,
            songId = sid,
            track = frozen.sections[0].track,
            trackId = frozen.sections[0].track.TrackId()
        };
        _byId[id] = rec;
        return rec;
    }

    /// Legacy demo shim (Tev's stock tapes, plugin demos): one 4-bar section.
    public static Record Register(string name, TraxTrack track, int tier)
    {
        if (track == null) return null;
        return Register(name, TraxSong.FromTrack(track), TraxKind.Demo, tier);
    }
```

Capture — keep the legacy track fields (section 0) AND write the sections + kind:

```csharp
    public static void Capture(TraxLibrarySave save)
    {
        if (save == null) return;
        save.prints.Clear();
        foreach (var kv in _byId)
        {
            Record r = kv.Value;
            var row = new TraxPrintSave
            {
                id = r.id,
                name = r.name,
                tier = r.tier,
                kind = r.kind,
                key = r.track.key
            };
            for (int d = 0; d < TraxPrng.DialCount; d++) row.dials.Add((float)r.track.dials.Get(d));
            for (int m = 0; m < TraxPresets.ModuleCount; m++)
            {
                row.preset.Add(r.track.preset[m]);
                row.variation.Add(r.track.variation[m]);
                row.active.Add(r.track.active[m]);
            }
            for (int s = 0; s < r.song.sections.Count; s++)
            {
                TraxSection sec = r.song.sections[s];
                var srow = new TraxSectionSave { bars = sec.bars, key = sec.track.key };
                for (int d = 0; d < TraxPrng.DialCount; d++) srow.dials.Add((float)sec.track.dials.Get(d));
                for (int m = 0; m < TraxPresets.ModuleCount; m++)
                {
                    srow.preset.Add(sec.track.preset[m]);
                    srow.variation.Add(sec.track.variation[m]);
                    srow.active.Add(sec.track.active[m]);
                }
                row.sections.Add(srow);
            }
            save.prints.Add(row);
        }
    }
```

Apply — coerce each row's track the way it does today (extract the existing inline coercion into `static TraxTrack CoerceTrack(TraxPrintSave row)` and a section twin `CoerceSection(TraxSectionSave srow)` with identical clamping), then:

```csharp
            int tier = row.tier < 1 ? 1 : row.tier > 2 ? 2 : row.tier;
            TraxTrack t = CoerceTrack(row);

            TraxSong song = null;
            if (row.sections != null && row.sections.Count > 0)
            {
                song = new TraxSong();
                for (int s = 0; s < row.sections.Count && s < TraxSong.MaxSections; s++)
                {
                    TraxSectionSave srow = row.sections[s];
                    if (srow == null) continue;
                    song.sections.Add(new TraxSection(CoerceSection(srow), srow.bars));
                }
                if (song.sections.Count == 0) song = null;
            }

            string id;
            int kind;
            if (song == null)
            {
                // Legacy row: a pre-format demo. Its old id is what hotbar
                // slots reference, so it is preserved, not re-derived.
                kind = TraxKind.Demo;
                song = TraxSong.FromTrack(t);
                id = LegacyId(tier, t.TrackId());
            }
            else
            {
                kind = TraxKind.Clamp(row.kind);
                id = MakeId(kind, tier, song.SongId());
            }

            _byId[id] = new Record
            {
                id = id,
                name = TraxLibrary.NormalizeName(row.name),
                tier = tier,
                kind = kind,
                song = song,
                songId = song.SongId(),
                track = song.sections[0].track,
                trackId = song.sections[0].track.TrackId()
            };
```

- [ ] **Step 3: DTO + stubs.** In `SaveData.cs` `TraxPrintSave` append:

```csharp
    // 2026-08-18 tape formats: the FORMAT (TraxKind: 0 demo / 1 half / 2 full)
    // and the arrangement — one row per section, exactly the TraxProjectSave
    // precedent. Empty list = a pre-format row: loads as a demo of the legacy
    // track fields above, under its old "t"-prefixed id.
    public int kind;
    public List<TraxSectionSave> sections = new List<TraxSectionSave>();
```
Mirror in `RentDeckStubs.cs` and `TraxLibraryTests.cs` stub copies.
- [ ] **Step 4: Run green:** `verify-library.py`, `verify-rent.py`, `verify-taste.py`, `compile-unity.py`.
- [ ] **Step 5: Commit** `feat(prints): every pressing is a frozen song with a format kind`

### Task 5: TapeMemory song lists — save halves + call rules

**Files:**
- Modify: `Assets/3 - Scripts/Music/TapeMemory.cs` (song lists — the in-memory half landed in Task 3; add the missing methods + save)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs:547-560` (`TapeMemorySave` += 4 lists)
- Modify: `prototypes/shuttle-computer/test/SaveStubs.cs` (mirror)
- Test: `TapeOfferTests.cs` (enable the Task-3 commented checks) + append round-trip checks

- [ ] **Step 1: Full TapeMemory API** (in the Entry class add `public readonly List<uint> heardSongs = new List<uint>();` and `public readonly List<uint> boughtSongs = new List<uint>();`; then):

```csharp
    // ── song history by IDENTITY (2026-08-18 tape formats) ───────────────
    //
    // Half/Full pressings are remembered by SongId, not by dial closeness:
    // a full song is a DIFFERENT PRODUCT from its section's demo — an alien
    // who bought the demo can still buy the song (that is the growth moment)
    // — but the same song can never be sold to them twice. Known soft spot,
    // accepted v1: nudging one variation changes SongId, so songs lack the
    // demo path's closeness guard; revisit if playtests show farming.

    public static bool HasHeardSong(string id, uint songId)
    {
        Entry e = Get(id, false);
        return e != null && songId != 0 && e.heardSongs.Contains(songId);
    }

    public static void RememberSong(string id, uint songId)
    {
        Entry e = Get(id, true);
        if (e == null || songId == 0 || e.heardSongs.Contains(songId)) return;
        e.heardSongs.Add(songId);
        if (e.heardSongs.Count > MaxSongsRemembered) e.heardSongs.RemoveAt(0);
        Version++;
    }

    public static void RememberBoughtSong(string id, uint songId)
    {
        Entry e = Get(id, true);
        if (e == null || songId == 0 || e.boughtSongs.Contains(songId)) return;
        e.boughtSongs.Add(songId);
        if (e.boughtSongs.Count > MaxSongsRemembered) e.boughtSongs.RemoveAt(0);
        Version++;
    }

    public static bool HasBoughtSong(string id, uint songId)
    {
        Entry e = Get(id, false);
        return e != null && songId != 0 && e.boughtSongs.Contains(songId);
    }
```
Capture: skip-empty check gains `&& e.heardSongs.Count == 0 && e.boughtSongs.Count == 0`; write `heardSongCounts`/`heardSongs` and `boughtSongCounts`/`boughtSongs` exactly like `boughtCounts`/`boughtTracks` (cast to `long`). Apply: two more count-guarded cursor loops mirroring the bought-tracks block (absent on old saves).
- [ ] **Step 2: `TapeMemorySave` in SaveData.cs** append (mirror comment style of `boughtTracks`):

```csharp
    // 2026-08-18 tape formats: Half/Full pressings are remembered by SongId
    // (see TapeMemory). Count-guarded; absent on older saves.
    public List<int> heardSongCounts = new List<int>();
    public List<long> heardSongs = new List<long>();
    public List<int> boughtSongCounts = new List<int>();
    public List<long> boughtSongs = new List<long>();
```
Mirror in `SaveStubs.cs`.
- [ ] **Step 3: Tests.** Enable Task 3's commented memory checks; append a Capture→Apply round trip (RememberSong + RememberBoughtSong for two aliens, Capture, Clear, Apply, assert both survive and an old-save DTO with the lists absent loads clean).
- [ ] **Step 4: Run** `verify-taste.py` green, `compile-unity.py` OK. **Commit** `feat(memory): songs remembered by identity, saved and guarded`

### Task 6: The six blanks — Hotbar, StorageUI, stubs, dev key

**Files:**
- Modify: `Assets/3 - Scripts/UI/Hotbar.cs` (enum :15, IsResource :951, StackMax :518, swatches :1033, DisplayNameOf :1081, IconOf :1115, the dev T-key grant — grep `BlankTapeT2` in Hotbar's Update for it, and the equip toast `" II"` site ~:1852)
- Modify: `Assets/3 - Scripts/UI/StorageUI.cs` (grep `BlankTapeT1` — extend both accept lists)
- Modify: `prototypes/shuttle-computer/test/RentDeckStubs.cs:71` (enum lockstep)

- [ ] **Step 1: Enum append** (END of `ItemId`, after `Cassette`): `BlankTapeHalfT1, BlankTapeHalfT2, BlankTapeFullT1, BlankTapeFullT2` — in `Hotbar.cs` AND `RentDeckStubs.cs`.
- [ ] **Step 2: IsResource** — add the four ids to the `or` chain. **StackMax** — add `ItemId.BlankTapeHalfT1 => 20, ItemId.BlankTapeHalfT2 => 20, ItemId.BlankTapeFullT1 => 20, ItemId.BlankTapeFullT2 => 20,`.
- [ ] **Step 3: Visuals.** Swatches (near :1033):

```csharp
    static readonly Color BlankHalfT1Swatch = new Color32(0x4F, 0x6B, 0x8A, 0xFF);   // slate-blue shell
    static readonly Color BlankHalfT2Swatch = new Color32(0x9A, 0x6B, 0x3F, 0xFF);   // bronze shell
    static readonly Color BlankFullT1Swatch = new Color32(0x3F, 0x8A, 0x6B, 0xFF);   // green shell
    static readonly Color BlankFullT2Swatch = new Color32(0xA8, 0x4F, 0x8A, 0xFF);   // violet shell
```
SwatchOf cases → these; DisplayNameOf → `"HALF TAPE"`, `"HALF TAPE II"`, `"FULL TAPE"`, `"FULL TAPE II"` (and change the two existing to `"DEMO TAPE"` / `"DEMO TAPE II"`); IconOf → `CassetteSprite(<matching swatch>)` ×4.
- [ ] **Step 4: Dev key.** Find the T-key blank grant in Hotbar (`GetKeyDown(KeyCode.T)`); extend: Ctrl+T grants 5× `BlankTapeHalfT1` (+Shift → `BlankTapeHalfT2`); Ctrl+Alt+T grants the Full pair the same way. Keep the plain/Shift behaviour untouched.
- [ ] **Step 5: StorageUI** — add the four ids beside the existing two blanks in both places the grep finds.
- [ ] **Step 6: Equip toast** (~:1852): replace the `" II"`-from-`TierOf` suffix with `" · " + TraxKind.Label(rec.kind) + (rec.tier >= 2 ? " II" : "")` using `TraxPrints.Get` (it already fetches the record or tier there — match the local code).
- [ ] **Step 7: Run** `verify-rent.py` (stub enum must still compile), `compile-unity.py` OK. **Commit** `feat(items): half/full blank tapes as first-class hotbar resources`

### Task 7: CassetteDeck kind + slot prompt + deck save

**Files:**
- Modify: `Assets/3 - Scripts/Music/CassetteDeck.cs`
- Modify: `Assets/3 - Scripts/Music/CassetteSlot.cs:168-190` (prompt text)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs:563-578` (`TraxLibrarySave` += `deckInsertedKind`)
- Modify: `prototypes/shuttle-computer/test/RentDeckStubs.cs:209-216` (mirror)
- Test: `prototypes/shuttle-computer/test/RentDeckTests.cs` (append)

- [ ] **Step 1: Failing tests** (append, matching the file's existing deck tests ~:239):

```csharp
    static void DeckKindChecks()
    {
        Hotbar.Reset(); CassetteDeck.Clear();
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeFullT2, 1);
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeFullT2;
        Check(CassetteDeck.Insert(), "full T2 blank inserts");
        Check(CassetteDeck.InsertedTier == 2 && CassetteDeck.InsertedKind == TraxKind.Full, "kind+tier seated");
        Check(CassetteDeck.EjectBlank(), "ejects back");
        Check(Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.BlankTapeFullT2) == 1, "same blank returned");

        // Save round trip carries the kind; old saves (field absent = 0) load as demo.
        Hotbar.Instance.EquippedId = Hotbar.ItemId.BlankTapeHalfT1;
        Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeHalfT1, 1);
        Check(CassetteDeck.Insert(), "half T1 inserts");
        var save = new TraxLibrarySave();
        CassetteDeck.Capture(save);
        CassetteDeck.Clear();
        CassetteDeck.Apply(save);
        Check(CassetteDeck.InsertedTier == 1 && CassetteDeck.InsertedKind == TraxKind.Half, "deck kind round-trips");
        CassetteDeck.Clear();
    }
```
(The stub Hotbar's enum already grew in Task 6.) Run `verify-rent.py` → fails.
- [ ] **Step 2: CassetteDeck changes.**

```csharp
    /// Format of the seated blank (TraxKind.*). Meaningful only while
    /// InsertedTier > 0; 0/Demo otherwise.
    public static int InsertedKind { get; private set; }

    static Hotbar.ItemId BlankIdFor(int kind, int tier)
    {
        if (kind == TraxKind.Full)
            return tier >= 2 ? Hotbar.ItemId.BlankTapeFullT2 : Hotbar.ItemId.BlankTapeFullT1;
        if (kind == TraxKind.Half)
            return tier >= 2 ? Hotbar.ItemId.BlankTapeHalfT2 : Hotbar.ItemId.BlankTapeHalfT1;
        return tier >= 2 ? Hotbar.ItemId.BlankTapeT2 : Hotbar.ItemId.BlankTapeT1;
    }

    /// The blank the player is HOLDING, or false. Kind+tier travel together —
    /// a lone tier can no longer identify a blank.
    public static bool HeldBlank(out int kind, out int tier)
    {
        kind = TraxKind.Demo; tier = 0;
        if (Hotbar.Instance == null) return false;
        switch (Hotbar.Instance.GetEquippedSlotId())
        {
            case Hotbar.ItemId.BlankTapeT1:     kind = TraxKind.Demo; tier = 1; return true;
            case Hotbar.ItemId.BlankTapeT2:     kind = TraxKind.Demo; tier = 2; return true;
            case Hotbar.ItemId.BlankTapeHalfT1: kind = TraxKind.Half; tier = 1; return true;
            case Hotbar.ItemId.BlankTapeHalfT2: kind = TraxKind.Half; tier = 2; return true;
            case Hotbar.ItemId.BlankTapeFullT1: kind = TraxKind.Full; tier = 1; return true;
            case Hotbar.ItemId.BlankTapeFullT2: kind = TraxKind.Full; tier = 2; return true;
        }
        return false;
    }

    public static int HeldBlankTier() { int k, t; return HeldBlank(out k, out t) ? t : 0; }
```
`Insert()` → `if (!HeldBlank(out int kind, out int tier)) return false; ... SpendResource(BlankIdFor(kind, tier), 1) ... InsertedTier = tier; InsertedKind = kind;`. `EjectBlank()` → `AddResource(BlankIdFor(InsertedKind, InsertedTier), 1)`, clear both. `PrintTo` → zero both. `Clear` → zero both. `Capture` → `save.deckInsertedKind = InsertedKind;`. `Apply` → `InsertedKind = InsertedTier > 0 ? TraxKind.Clamp(save.deckInsertedKind) : 0;` after the tier clamp.
- [ ] **Step 3: DTO.** `TraxLibrarySave` append:

```csharp
    // 2026-08-18 tape formats: the seated blank's FORMAT (TraxKind). 0 on
    // older saves — with a tier seated that correctly reads as a Demo blank.
    public int deckInsertedKind;
```
Mirror in `RentDeckStubs.cs` `TraxLibrarySave`.
- [ ] **Step 4: CassetteSlot prompt** (:168-190): replace `HeldBlankTier() > 0` + `(held >= 2 ? " II" : "")` with `CassetteDeck.HeldBlank(out int hk, out int ht)` and prompt text `"insert " + TraxKind.Label(hk) + " TAPE" + (ht >= 2 ? " II" : "")` (match the surrounding casing/format exactly when editing).
- [ ] **Step 5: Run** `verify-rent.py` green, `compile-unity.py` OK. **Commit** `feat(deck): the slot knows its blank's format`

### Task 8: Kind-aware orders core — DealTerms/Grade + TapeTrade quote chain

**Files:**
- Modify: `Assets/3 - Scripts/Music/DealTerms.cs`
- Modify: `Assets/3 - Scripts/Music/TapeTrade.cs` (OpeningOffer/TruePrice kind chain, `Fills(TraxSong)`, `PickAskKind`, `HeldMatchingOrder`)
- Modify: `Assets/3 - Scripts/Music/TapeCareer.cs` (create — needed by PickAskKind)
- Modify: `prototypes/shuttle-computer/test/verify-taste.py` SOURCES (+ `"TapeCareer.cs"`? **No** — TapeCareer references StoryDirector, which the taste suite has no stub for. Keep `PickAskKind`'s career clamp in TapeTrade (Unity side) and the pure preference in AlienTaste, so the taste suite stays clean.)
- Modify: `prototypes/shuttle-computer/test/verify-rent.py` SOURCES (+ `os.path.join(SCRIPTS, "Music", "TapeCareer.cs")` — RentDeckStubs already stubs StoryDirector)
- Test: `test/DealTests.cs` (append), `test/RentDeckTests.cs` (TapeCareer checks)

- [ ] **Step 1: Create `TapeCareer.cs`** (StoryDirector-backed, MushroomQuest's pattern — world-scoped, saved, New-Game-reset and co-op-shared for free):

```csharp
/// <summary>
/// The tape-selling career: one world counter and the two shop milestones it
/// unlocks. StoryDirector-backed like MushroomQuest's rent, so it saves,
/// replicates and resets with zero schema.
/// </summary>
public static class TapeCareer
{
    const string KeySold = "tapesSoldTotal";

    /// Total tapes ever sold in this world (walk-ups + deliveries; both
    /// players' sales in co-op). Incremented in BuyerLedger.ReportTapeDeal —
    /// the one choke point both paths and guest routing already pass through.
    public static int TapesSold
    {
        get { return StoryDirector.Instance != null ? StoryDirector.Instance.GetCounter(KeySold) : 0; }
        set { if (StoryDirector.Instance != null) StoryDirector.Instance.SetCounter(KeySold, value < 0 ? 0 : value); }
    }

    public static bool HalfUnlocked { get { return TapesSold >= TraxKind.HalfUnlockSales; } }
    public static bool FullUnlocked { get { return TapesSold >= TraxKind.FullUnlockSales; } }

    /// The biggest format Tev currently stocks.
    public static int UnlockedKind()
    {
        return FullUnlocked ? TraxKind.Full : HalfUnlocked ? TraxKind.Half : TraxKind.Demo;
    }
}
```

- [ ] **Step 2: Failing DealTests** (append):

```csharp
    static void KindGradeChecks()
    {
        // Exact kind at the agreed price pays EXACTLY the agreed number.
        var terms = new DealTerms { buyerId = "b", genreIndex = 0, qty = 1, tapeTier = 1,
                                    modulesBasis = 4, pricePerTape = 40, windowMinutes = 10,
                                    kind = TraxKind.Full };
        var g = TapeDeal.Grade(terms, 4, deliveredModules: 4, deliveredTier: 1,
                               deliveredFormatMult: TraxKind.FullNominalMult,
                               fillsGenre: true, deliveredQty: 1, alreadyHeard: false,
                               ask: 40, substituteWorth: 999);
        Check(g.perCap == 40 && !g.thin, "exact kind pays the contract");

        // A demo delivered on a Full order pays pro-rata (1.0 / 3.5) and flags it.
        g = TapeDeal.Grade(terms, 4, 4, 1, deliveredFormatMult: 1.0,
                           fillsGenre: true, deliveredQty: 1, alreadyHeard: false,
                           ask: 40, substituteWorth: 999);
        Check(g.thin && g.kindShort, "demo on a full order is a shortfall");
        Check(g.perCap == (int)System.Math.Round(40 * (1.0 / TraxKind.FullNominalMult),
              System.MidpointRounding.AwayFromZero), "kind pro-rata is the nominal ratio");

        // A song BETTER than ordered caps at the agreed price (generosity).
        g = TapeDeal.Grade(new DealTerms { buyerId = "b", qty = 1, tapeTier = 1, modulesBasis = 4,
                                           pricePerTape = 20, kind = TraxKind.Demo },
                           4, 4, 1, deliveredFormatMult: 4.0,
                           fillsGenre: true, deliveredQty: 1, alreadyHeard: false,
                           ask: 20, substituteWorth: 999);
        Check(g.perCap == 20 && !g.thin, "over-delivery caps at the contract");

        // Quotes scale with the ordered kind.
        int demoQ = TapeDeal.TruePrice("b", 1, TraxKind.Demo, 4, 0);
        int fullQ = TapeDeal.TruePrice("b", 1, TraxKind.Full, 4, 0);
        Check(fullQ > demoQ * 3, "full orders quote at the nominal multiple");
    }
```

- [ ] **Step 3: DealTerms/TapeDeal changes.** `DealTerms` append: `public int kind;   // TraxKind ordered; 0 = legacy save = Demo` . `TruePrice`/`OpeningOffer` gain a kind overload (old signatures forward `TraxKind.Demo`):

```csharp
    public static int TruePrice(string buyerId, int tapeTier, int kind, int installedModules, int bond)
    {
        int mods = installedModules < 1 ? 1 : installedModules;
        return TapeValue.For(mods, tapeTier, TraxKind.NominalMult(kind), OrderSatisfaction, bond, true,
                             AlienTaste.PayFactor(buyerId)
                             * AlienTaste.TierPayFactor(buyerId, tapeTier));
    }
```
`Grade` gains `double deliveredFormatMult` (after `deliveredTier`) and `GradeResult` gains `public bool kindShort;`. Inside the exact-goods branch replace the two goods lines with:

```csharp
            double contractGoods = TapeValue.Base(contractMods, contractTier)
                                 * TraxKind.NominalMult(TraxKind.Clamp(terms.kind));
            double deliveredGoods = TapeValue.Base(deliveredModules, deliveredTier)
                                  * (deliveredFormatMult < 1.0 ? 1.0 : deliveredFormatMult);
```
and set `r.kindShort = deliveredFormatMult < TraxKind.NominalMult(TraxKind.Clamp(terms.kind)) - 1e-9;` alongside `r.tierShort`.
- [ ] **Step 4: TapeTrade additions.**

```csharp
    /// Does any SECTION of this pressing fill an order for the genre?
    public static bool Fills(TraxSong song, int genreIndex)
    {
        if (song == null) return false;
        for (int i = 0; i < song.sections.Count; i++)
            if (Fills(song.sections[i].track, genreIndex)) return true;
        return false;
    }

    /// The format a contact's order asks for: their derived preference,
    /// clamped to what the career has unlocked (no full-length requests while
    /// Tev doesn't even sell the blanks — the ask must always be fillable).
    public static int PickAskKind(string id)
    {
        int pref = AlienTaste.KindPreference(id);
        int unlocked = TapeCareer.UnlockedKind();
        return pref < unlocked ? pref : unlocked;
    }

    /// Right genre, at least the ordered shell AND format — the count that
    /// pays the full agreed price.
    public static int HeldMatchingOrder(int genreIndex, int minTapeTier, int minKind)
    {
        if (Hotbar.Instance == null) return 0;
        int n = 0;
        for (int i = 0; i < Hotbar.TotalSlots; i++)
        {
            Hotbar.Slot slot = Hotbar.Instance.SlotAt(i);
            if (slot.id != Hotbar.ItemId.Cassette || slot.count <= 0) continue;
            TraxPrints.Record rec = TraxPrints.Get(slot.cassetteId);
            if (rec != null && rec.tier >= minTapeTier && rec.kind >= minKind
                && Fills(rec.song, genreIndex)) n += slot.count;
        }
        return n;
    }
```
`HeldMatching` and the old `HeldMatchingTier` switch their `Fills(rec.track, ...)` call to `Fills(rec.song, ...)`. `OpeningOffer(id, genreIndex, tapeTier)` gains a kind overload forwarding into `TapeDeal.OpeningOffer(id, tapeTier, kind, ...)`; the old forms pass `TraxKind.Demo`.
- [ ] **Step 5: RentDeckTests TapeCareer check:**

```csharp
    static void CareerChecks()
    {
        StoryDirector.Reset();
        Check(TapeCareer.UnlockedKind() == TraxKind.Demo, "career starts demo-only");
        TapeCareer.TapesSold = TraxKind.HalfUnlockSales;
        Check(TapeCareer.HalfUnlocked && !TapeCareer.FullUnlocked, "half unlocks at the milestone");
        TapeCareer.TapesSold = TraxKind.FullUnlockSales;
        Check(TapeCareer.UnlockedKind() == TraxKind.Full, "full unlocks at the milestone");
    }
```
- [ ] **Step 6: Run** `verify-taste.py`, `verify-rent.py`, `compile-unity.py` all green. **Commit** `feat(orders): kind is a contract term — nominal quotes, pro-rata grades, career gate`

### Task 9: Print dialog + DoPrint press the right thing

**Files:**
- Modify: `Assets/3 - Scripts/Music/ShuttleComputerUI.cs:1102-1208`

- [ ] **Step 1: `RefreshPrintDialog`.** After `bool blocked = ...` add `int kind = CassetteDeck.InsertedKind;` and `bool overCap = false;`. Slot-state line becomes:

```csharp
            _printSlotState.text = "READY TO PRINT  —  " + TraxKind.Label(kind) + " TAPE"
                                 + (CassetteDeck.InsertedTier >= 2 ? " II" : "");
```
Replace the final two note branches (the `sections.Count > 1` honest label and the `else`) with:

```csharp
        else if (loaded && kind == TraxKind.Demo)
        {
            _printNote.text = _song != null && _song.sections.Count > 1
                ? "DEMO blank - presses only SEC " + TraxSong.SectionLabel(_sel)
                  + " (" + _song.sections[_sel].bars + " bars)"
                : "one tape, then it ejects";
            _printNote.color = InkGhost;
        }
        else if (loaded)
        {
            int cap = TraxKind.BarCap(kind);
            int bars = _song != null ? _song.TotalBars() : 4;
            overCap = bars > cap;
            _printNote.text = overCap
                ? "TRACK TOO LONG FOR THIS TAPE - " + bars + "/" + cap + " BARS"
                : "presses the FULL TRACK - " + bars + "/" + cap + " bars";
            _printNote.color = overCap ? Warn : InkGhost;
        }
```
(Keep the unsaved-changes branch above these; move it or merge its warning into the note ordering so naming > slot > eject > dirty > kind, as the current order comment prescribes.) Gate: `bool canPrint = named && loaded && !blocked && !overCap;`.
- [ ] **Step 2: `DoPrint`.** Replace the `Register` call with:

```csharp
        int kind = CassetteDeck.InsertedKind;
        TraxPrints.Record press;
        if (kind == TraxKind.Demo)
        {
            // The whole selected SECTION — its bar length, fill-bar ending —
            // not just the raw 4-bar loop (Sam, 2026-08-18).
            var demoSong = new TraxSong();
            int bars = _song != null ? _song.sections[_sel].bars : 4;
            demoSong.sections.Add(new TraxSection(_inst.Track, bars));
            press = TraxPrints.Register(_project.name, demoSong, TraxKind.Demo,
                                        CassetteDeck.InsertedTier);
        }
        else
        {
            if (_song == null || _song.TotalBars() > TraxKind.BarCap(kind))
            { Toast("TRACK TOO LONG FOR THIS TAPE"); return; }
            press = TraxPrints.Register(_project.name, _song, kind, CassetteDeck.InsertedTier);
        }
```
- [ ] **Step 3:** `compile-unity.py` OK. In-editor sanity is Sam's playtest; nothing headless covers UGUI. **Commit** `feat(print): the blank in the slot decides what gets pressed`

### Task 10: Walk-up sell path goes song-aware

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/MushroomSellUI.cs` (:351-409 numbers, :445-496 listen, :636-752 close/notify, :1352-1403 slot paint)
- Modify: `Assets/3 - Scripts/Music/AlienFeedback.cs` (slice bank)
- Modify: `Assets/3 - Scripts/Story/TevFronting.cs:148-155`
- Modify: `Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs:369` (named-request HasHeard site)
- Modify: `Assets/3 - Scripts/Multiplayer/EconomySync.cs` (songId+kind on both tape messages)
- Test: taste suite already covers the model; this task is wiring — verify by `compile-unity.py` + suites staying green.

- [ ] **Step 1: The four number properties** (:351-409):

```csharp
    double Satisfaction
    {
        get
        {
            var rec = Press;
            return rec == null ? 0.0 : SongEval.Satisfaction(_buyerId, rec.song);
        }
    }

    int Market
    {
        get
        {
            var rec = Press;
            return rec == null ? 0
                 : Mathf.Max(1, Mathf.RoundToInt((float)TapeValue.Base(rec.track.ActiveCount(), rec.tier, rec.FormatMult)));
        }
    }

    int Fair
    {
        get
        {
            var rec = Press;
            if (rec == null) return Market;
            var led = BuyerLedger.Get(_buyerId);
            return TapeOffer.Value(_buyerId, rec.track.ActiveCount(), rec.tier, rec.FormatMult,
                                   Satisfaction, false, led != null ? led.bond : 0);
        }
    }
```
`StreetValue`: change its `TapeValue.Base(...)` call to the three-arg `Base(rec.track.ActiveCount(), rec.tier, rec.FormatMult)`.
- [ ] **Step 2: `ListenOnTable`** — replace the dials/Listen block with:

```csharp
        // The dials a complaint or a demo-memory write should talk about: the
        // section this alien liked MOST (their nearest miss). One section =
        // exactly the old behaviour.
        int bestSec = SongEval.BestSection(_buyerId, rec.song, out double bestSat);
        double[] dials = TapeTrade.DialsOf(rec.song.sections[bestSec].track);
        uint variant = StableHash(_buyerId + ":" + _offerSpecies);
        _tableReaction = TapeOffer.Listen(_buyerId, rec.song, rec.songId, rec.kind, rec.tier,
                                          UnityEngine.Random.value < 0.5f,
                                          out _tableSat, out var verdict);
```
Rejection branch memory write becomes kind-aware:

```csharp
            if (verdict == AlienTaste.Verdict.Rejected
                && !EconomySync.ReportTapeHeard(_buyerId, dials, rec.songId, rec.kind))
            {
                if (rec.kind == TraxKind.Demo) TapeMemory.Remember(_buyerId, dials);
                else TapeMemory.RememberSong(_buyerId, rec.songId);
                BuyerLedger.AddCraving(_buyerId, CravingRules.GainHeardOnly);
            }
```
Liked branch appends the slice mention:

```csharp
        _listenOpinion = AlienFeedback.ForLiked(
            _tableSat, verdict == AlienTaste.Verdict.CoinFlip, variant);
        if (rec.song.sections.Count > 1 && bestSat - _tableSat > 15.0)
            _listenOpinion += " " + AlienFeedback.ForSlice(
                TraxClassifier.Classify(rec.song.sections[bestSec].track.dials).primary.name, variant);
```
- [ ] **Step 3: `AlienFeedback.ForSlice`** (new, after `ForRepeat`):

```csharp
    /// Said about a multi-genre song when one stretch clearly carried it for
    /// this listener — names the slice so the mixed verdict is legible.
    /// DRAFT lines, Sam edits.
    public static string ForSlice(string genre, uint variant)
    {
        string[] bank =
        {
            "The " + genre + " parts are the good bit.",
            "Mostly here for the " + genre + " stretch.",
            "The " + genre + " bits carry it.",
        };
        return bank[variant % (uint)bank.Length];
    }
```
- [ ] **Step 4: `CloseSale` / `NotifyTapeSold` / `CompleteScheduled`.**
  - `GenreIndexOf(rec)` (:725) becomes the dominant mix genre: `return rec == null ? 0 : rec.song.GenreMix()[0].genreIndex;` (one section = the old primary).
  - `matchedTaste` sites (:661, :1714): `bool matchedTaste = soldRec != null && SongEval.MatchesFavourite(_buyerId, soldRec.song);` (keep `soldDials` for the wire, sourced from `TapeTrade.DialsOf(soldRec.track)`).
  - `NotifyTapeSold` memory block:

```csharp
        if (rec != null)
        {
            if (rec.kind == TraxKind.Demo) TapeMemory.Remember(_buyerId, TapeTrade.DialsOf(rec.track));
            else TapeMemory.RememberSong(_buyerId, rec.songId);
            TapeMemory.RememberBought(_buyerId, rec.trackId);
            if (rec.kind != TraxKind.Demo) TapeMemory.RememberBoughtSong(_buyerId, rec.songId);
        }
```
  - The `!sentToHost` duplicate `TapeMemory.Remember(soldDials)` in both CloseSale (:709) and CompleteScheduled (:1736) becomes the same kind-aware pair (demo → dials, else → `RememberSong(soldRec.songId)`).
  - Both `EconomySync.ReportTapeSale(...)` calls pass two new args: `songId: soldRec != null ? soldRec.songId : 0, kind: soldRec != null ? soldRec.kind : 0`.
  - Both `BuyerLedger.ReportTapeDeal(...)` calls pass `kind: soldRec != null ? soldRec.kind : 0` (parameter added in Task 12).  **Defer this one line to Task 12** — leave a `// TASK12: pass kind` marker so the file compiles now.
- [ ] **Step 5: Slot paint** (:1378-1402): genre label becomes mix-aware and gains the kind:

```csharp
                var rec = TraxPrints.Get(s.cassetteId);
                string g = "";
                if (rec != null)
                {
                    var mix = rec.song.GenreMix();
                    g = mix.Count > 1 ? mix[0].name + " +" + (mix.Count - 1)
                                      : TraxClassifier.Classify(rec.track.dials).label;
                    g += "   " + TraxKind.Label(rec.kind) + " T" + rec.tier;
                }
                w.genreLbl.text = g;
```
(delete the old `+ "   T2"/"   T1"` suffix line.)
- [ ] **Step 6: TevFronting (:152) & BuyerMessageDirector (:369).** Fronting price → `TapeValue.Base(rec.track.ActiveCount(), rec.tier, rec.FormatMult)`. The named-request skip keeps dial closeness (projects are demo lineage) — no change needed beyond confirming it compiles; leave as-is.
- [ ] **Step 7: EconomySync wire.** `ReportTapeSale` signature += `uint songId = 0, int kind = 0`; after `WriteDials(w, heardDials);` add `w.WriteValueSafe(songId); w.WriteValueSafe(kind);`. `ReportTapeHeard(string buyerId, double[] dials, uint songId = 0, int kind = 0)` likewise. In `HandleTapeSale`/`HandleTapeHeard` (read side), after the dials read add `reader.ReadValueSafe(out uint songId); reader.ReadValueSafe(out int kind);` and make the host's memory write kind-aware (`kind == 0 ? Remember(dials) : RememberSong(songId)`), and pass `kind` through to `ReportTapeDeal` once Task 12 adds the parameter (leave `// TASK12` marker). Read the two handlers first and mirror their exact defensive style.
- [ ] **Step 8:** `compile-unity.py` OK, all three verify suites still green. **Commit** `feat(sell): walk-ups price and judge whole songs`

### Task 11: Tev's shop — six rows, milestone locks, unlock announcement

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/TevShopUI.cs` (:70-129 catalogue/qty, :277-326 CanAdd/BuyTapes, :419-481 PaintRow)
- Modify: `Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs` (unlock announcement in the tick)

- [ ] **Step 1: Catalogue.** `Entry` += `public int tapeKind;`. Replace the two blank rows with six (keep the plugin rows untouched):

```csharp
        new Entry { name = "DEMO 1", desc = "One section on a cheap shell.", price = TraxKind.DemoT1Price,
                    item = Hotbar.ItemId.BlankTapeT1, tapeKind = TraxKind.Demo, chip = new Color32(0x79, 0xFF, 0xD0, 0xFF) },
        new Entry { name = "DEMO 2", desc = "Doubles a tape's base value. Best with more plugins.", price = TraxKind.DemoT2Price,
                    item = Hotbar.ItemId.BlankTapeT2, tapeKind = TraxKind.Demo, chip = new Color32(0xFF, 0x4F, 0xD8, 0xFF) },
        new Entry { name = "HALF-LENGTH 1", desc = "A whole song, up to 50 bars.", price = TraxKind.HalfT1Price,
                    item = Hotbar.ItemId.BlankTapeHalfT1, tapeKind = TraxKind.Half, chip = new Color32(0x4F, 0x6B, 0x8A, 0xFF) },
        new Entry { name = "HALF-LENGTH 2", desc = "50 bars on the premium shell.", price = TraxKind.HalfT2Price,
                    item = Hotbar.ItemId.BlankTapeHalfT2, tapeKind = TraxKind.Half, chip = new Color32(0x9A, 0x6B, 0x3F, 0xFF) },
        new Entry { name = "FULL-LENGTH 1", desc = "The whole record - up to 100 bars.", price = TraxKind.FullT1Price,
                    item = Hotbar.ItemId.BlankTapeFullT1, tapeKind = TraxKind.Full, chip = new Color32(0x3F, 0x8A, 0x6B, 0xFF) },
        new Entry { name = "FULL-LENGTH 2", desc = "100 bars, premium shell. The main event.", price = TraxKind.FullT2Price,
                    item = Hotbar.ItemId.BlankTapeFullT2, tapeKind = TraxKind.Full, chip = new Color32(0xA8, 0x4F, 0x8A, 0xFF) },
```
`_qty` → `readonly int[] _qty = new int[Stock.Length];`. Add:

```csharp
    /// Visible-padlock rule (the Colonizer-level philosophy): a locked format
    /// shows its row and names the distance, it never quietly disappears.
    static bool KindLocked(in Entry e)
    {
        if (e.plugin != null) return false;
        if (e.tapeKind == TraxKind.Half) return !TapeCareer.HalfUnlocked;
        if (e.tapeKind == TraxKind.Full) return !TapeCareer.FullUnlocked;
        return false;
    }

    static int KindSalesLeft(in Entry e)
    {
        int need = e.tapeKind == TraxKind.Full ? TraxKind.FullUnlockSales : TraxKind.HalfUnlockSales;
        int left = need - TapeCareer.TapesSold;
        return left < 0 ? 0 : left;
    }
```
- [ ] **Step 2: Gate the paths.** `CanAdd(i)`: first line `if (!IsTape(e) || KindLocked(e)) return false;`. `BuyTapes(i)`: after `if (want <= 0) return;` add `if (KindLocked(e)) { SetStatus($"\"Sell {KindSalesLeft(e)} more tapes and we'll talk {e.name}s.\"", C_Err); return; }`. In `PaintRow`'s tape branch, before the quantity block:

```csharp
        if (tape && KindLocked(e))
        {
            w.stepper.gameObject.SetActive(false);
            w.price.text = $"${e.price}";
            w.buy.interactable = false;
            w.buy.targetGraphic.color = C_BuyOff;
            w.buyLabel.text = $"SELL {KindSalesLeft(e)} MORE";
            w.buyLabel.color = C_Dimmer;
            w.root.GetComponent<CanvasGroup>().alpha = 0.55f;
            return;
        }
```
(and reset `alpha = own ? 0.55f : 1f;` earlier stays — the tape path re-sets it, so ensure the unlocked tape path sets `alpha = 1f`.)
- [ ] **Step 3: Unlock announcement.** In `BuyerMessageDirector`'s per-tick method (where `SendWantText` pacing runs), add a guard-once check:

```csharp
        // Tev restocks when the career crosses a milestone — announce ONCE per
        // milestone, via a StoryDirector counter so it survives saves/co-op.
        var sd = StoryDirector.Instance;
        if (sd != null)
        {
            int unlocked = TapeCareer.UnlockedKind();
            if (unlocked > sd.GetCounter("tapesUnlockAnnounced"))
            {
                sd.SetCounter("tapesUnlockAnnounced", unlocked);
                Notify(unlocked == TraxKind.Full
                    ? "TEV: \"Full-length blanks just came in. You've earned the shelf space.\""
                    : "TEV: \"New stock - half-length blanks. Your demos are selling; time for real songs.\"");
            }
        }
```
- [ ] **Step 4:** `compile-unity.py` OK. **Commit** `feat(shop): six blanks, milestone-locked with visible padlocks`

### Task 12: Ledger + orders — askKind, songsBought, kind on the deal report, want-text copy

**Files:**
- Modify: `Assets/3 - Scripts/Vendor/BuyerLedger.cs` (Buyer fields :117, Ev/EvSave `k`, ReportTapeDeal :249, FillSave/ApplySave :520-607)
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs:111-150` (BuyerLedgerSave lists + EvSave `k`)
- Modify: `Assets/3 - Scripts/Vendor/BuyerMessageDirector.cs:310-354` (SendWantText), `:393-401` (Accept re-quote)
- Modify: `Assets/3 - Scripts/Vendor/BuyerTexts.cs` (KindWord + WantText lines)
- Modify: `Assets/3 - Scripts/Vendor/MushroomSellUI.cs` (delivery terms + preview + the two `// TASK12` markers), `Assets/3 - Scripts/Multiplayer/EconomySync.cs` (its `// TASK12` marker)
- Modify: `Assets/3 - Scripts/NPC_Dialogue/NPCSellRows.cs:84-106`

- [ ] **Step 1: Buyer fields** (append after `requestTrackId`):

```csharp
        // ── Tape formats (2026-08-18, appended for save order) ──
        // The FORMAT the open order asks for (TraxKind). 0 on old saves = Demo.
        public int askKind;
        // Completed deals that were Half/Full songs — drives the "grown from
        // demos" moment (first liked song after >=3 demo deals).
        public int songsBought;
```
`Ev` += `public int k;   // tape FORMAT + 1 on order/deal events; 0 = pre-feature = say nothing`, `EvSave` (SaveData.cs) += `public int k;`, and the Ev↔EvSave copy sites in FillSave/ApplySave carry it. `BuyerLedgerSave` += guarded lists:

```csharp
    // 2026-08-18 tape formats. Count-guarded like askTapeTier; absent on old saves.
    public List<int> askKind = new List<int>();
    public List<int> songsBought = new List<int>();
```
FillSave adds `s.askKind.Add(b.askKind); s.songsBought.Add(b.songsBought);` (+ the two `.Clear()`s); ApplySave reads both with the same `!= null && i < Count` guard pattern as `craving`.
- [ ] **Step 2: `ReportTapeDeal`** gains `int kind = 0` (last parameter) and, after the bond math and BEFORE `b.dealsCompleted++` reorders nothing — insert right after `Touch();`:

```csharp
        bool firstSong = kind > TraxKind.Demo && b.songsBought == 0 && b.dealsCompleted >= 3;
```
then after the existing `gain` computation add `if (firstSong) gain += 3;   // the growth moment pays in bond`, and after `b.bond = ...` add `if (kind > TraxKind.Demo) b.songsBought++;` and `TapeCareer.TapesSold += qty;` (the career counter's one choke point — walk-ups, deliveries and routed guest sales all land here). The event Log calls pass `k: kind + 1` (add a `k` optional param to `Log` mirroring `c`).
- [ ] **Step 3: Growth line surfaces.** In `MushroomSellUI.CloseSale` and `CompleteScheduled`, compute the predicate in the SAME pre-clear block where `satBand`/`deliveredSat` are captured (BEFORE `_offerSpecies = null` wipes `Press` — `Satisfaction` reads through it):

```csharp
        var led0 = BuyerLedger.Get(_buyerId);
        bool growth = soldRec != null && soldRec.kind > TraxKind.Demo
                   && AlienFeedback.SatBand(Satisfaction) >= 2
                   && led0 != null && led0.songsBought == 0 && led0.dealsCompleted >= 3;
```
and append to the success `SetResult` line: `+ (growth ? $"\n\"{AlienFeedback.ForGrowth(StableHash(_buyerId + \":growth\"))}\"" : "")`. Resolve both `// TASK12` markers by passing `kind:` to `ReportTapeDeal` (UI sites and the EconomySync handler).
- [ ] **Step 4: `AlienFeedback.ForGrowth`** (new bank, after `ForSlice`):

```csharp
    /// Said the first time a demo-days regular buys a full song they like —
    /// the career becoming visible in someone else's voice. DRAFT, Sam edits.
    public static string ForGrowth(uint variant)
    {
        string[] bank =
        {
            "You've come a long way from those demos. Keep up the good music.",
            "A whole song. I remember when you only had sketches. Keep going.",
            "From demo tapes to this? I'll be telling people I bought early.",
            "This is a real record. Don't stop now.",
            "I liked the demos. I like this more. Keep making them longer.",
            "Look at you - full songs now. I want the next one too.",
        };
        return bank[variant % (uint)bank.Length];
    }
```
- [ ] **Step 5: Orders.** `SendWantText`: named branch sets `b.askKind = TraxKind.Demo;` explicitly; the plain branch sets `b.askKind = TapeTrade.PickAskKind(b.id);` and quotes via `b.offerPerCap = TapeTrade.OpeningOffer(b.id, genre, b.askTapeTier, b.askKind);`, logging `k: b.askKind + 1`. `Accept`'s tier re-quote (:400) becomes `TapeTrade.OpeningOffer(b.id, b.askTier, tapeTier, b.askKind)`, and the `Scheduled` log passes `k: b.askKind + 1`. `BuyerTexts` adds:

```csharp
    // The FORMAT rides `k` as kind+1; 0 = pre-feature event, say nothing.
    static string KindWord(int k) =>
        k == TraxKind.Full + 1 ? " full-length" : k == TraxKind.Half + 1 ? " half-length" : "";
```
and each `WantText` voice line inserts `{KindWord(e.k)}` before ` {Tapes(e.b)}` (e.g. `"after {e.b} {Genre(e.tier)}{KindWord(e.k)} {Tapes(e.b)}{TierWord(e.c)}. ..."`).
- [ ] **Step 6: Delivery grading + preview.** `DeliverOrder`: `terms` gains `kind = _appt.askKind,`; the `Grade` call gains `deliveredFormatMult: delivered != null ? delivered.FormatMult : 1.0,`; `fillsGenre` uses `TapeTrade.Fills(delivered.song, _appt.askTier)`; `alreadyHeard` becomes kind-aware (demo → dials, else → `TapeMemory.HasHeardSong(_buyerId, delivered.songId)`); `substituteWorth` uses the format-aware `TapeOffer.Value(..., delivered.FormatMult, ...)`; add a kind-short thin line: when `g.kindShort` use `$"\"I ordered a {TraxKind.Label(TraxKind.Clamp(_appt.askKind)).ToLowerInvariant()}-length.\" — {_npcName} paid {{0}}, pro-rata."`. The preview band (:1221-1231) gains a matching shortNote branch: `else if (rec2.kind < _appt.askKind) shortNote = $" — a {TraxKind.Label(rec2.kind)} tape on a {TraxKind.Label(_appt.askKind)}-length order pays pro-rata";`. `NPCSellRows` (:88-99): `rightShell` becomes `TapeTrade.HeldMatchingOrder(tapeLedger.askTier, ordShell, tapeLedger.askKind)` and the label appends `TraxKind.Label(tapeLedger.askKind)` when `askKind > 0` (e.g. `$"{want}{ordTier} {TraxKind.Label(tapeLedger.askKind)}"`).
- [ ] **Step 7:** `compile-unity.py` OK; `verify-taste.py`/`verify-rent.py` green. **Commit** `feat(orders+fans): kind-aware want texts and grading, the demos-to-songs growth moment`

### Task 13: Walkman + table listen play the whole song

**Files:**
- Modify: `Assets/3 - Scripts/Music/TraxTapePlayer.cs`
- Modify: call sites — grep `TraxTapePlayer.PlayAt` and `TogglePersonal`; any that pass a print's `rec.track` route through the new record method.

- [ ] **Step 1: Add song playback.** In `TraxTapePlayer`:

```csharp
    /// <summary>
    /// Play a PRESSED TAPE — the whole song, transitions and all, looping.
    /// Every pressing goes through here now: a demo is a one-section song, so
    /// even demos gain their fill-bar ending. Raw-track callers (plugin
    /// demos) keep Play(track, seconds).
    /// </summary>
    public void PlayRecord(TraxPrints.Record rec, float seconds)
    {
        if (rec == null || rec.song == null || _engine == null) return;
        StopAutoStop();

        TraxSong song = rec.song;
        int n = song.sections.Count;
        var ps = new TraxParams[n];
        var phrases = new TraxPhrase[n];
        var tracks = new TraxTrack[n];
        var bars = new int[n];
        for (int i = 0; i < n; i++)
        {
            TraxSection sec = song.sections[i];
            ps[i] = TraxParams.Compute(sec.track.dials, sec.track.key);
            phrases[i] = TraxPhrase.Generate(sec.track, ps[i]);
            tracks[i] = sec.track;
            bars[i] = sec.bars;
        }
        Current = song.sections[0].track.Clone();
        _engine.PublishSong(ps, phrases, tracks, bars);
        _engine.SeekSong(0);
        _engine.StartSongTransport();
        if (seconds > 0f) _autoStop = StartCoroutine(StopAfter(seconds));
    }
```
`TogglePersonal` replaces `p.Play(rec.track, 0f);` with `p.PlayRecord(rec, 0f);`. Add a static `PlayRecordAt(Transform at, TraxPrints.Record rec, float seconds)` mirroring `PlayAt` but calling `PlayRecord`. `Stop()` must also end song mode — check `TraxAudioEngine.StopTransport` clears `_songMode`/`IsSongMode`; if not, call whatever the arranger's STOP path uses (read `TraxInstrument.Stop`'s engine call and mirror it).
- [ ] **Step 2: Route record call sites.** Grep `PlayAt(` — if the sell panel's table listen passes `rec.track`, switch it to `PlayRecordAt(at, rec, seconds)`. Leave `PluginDemos`/`PlayPersonalTrack` untouched.
- [ ] **Step 3:** `compile-unity.py` OK. **Commit** `feat(walkman): pressed tapes play their whole song`

### Task 14: Diagnostics, docs, final sweep

**Files:**
- Modify: `prototypes/shuttle-computer/test/TasteDiagnostic.cs` (+ song mode)
- Modify: `docs/CURRENT_STATE_AUDIT.md` (tape/economy sections), `docs/SELLING_SYSTEM_HANDOFF.md` §9 status block

- [ ] **Step 1: Diagnostic song mode.** In `TasteDiagnostic.cs` add a pass that builds three archetype songs (pure single-genre 8×12-bar Full, an even 4-genre 100-bar Full, a 2-section 50-bar Half) and prints, over 500 aliens, the DISTRIBUTION (not the mean) of verdicts and prices next to the same loop as a demo — the numbers Sam tunes `ValueMult`/`NominalMult` against. Run `python prototypes/shuttle-computer/test/verify-diagnostic.py` and paste the table into the commit body.
- [ ] **Step 2: Docs + promise audit closeout.** Audit: update the tape-economy section (six blanks, song prints, weighted-sat model, milestones, growth moment). Handoff doc §9: append a dated status entry. Read `BuyerLedger.RevealLine` and confirm its bands describe buyer TRAITS (pay factor / falloff), not tape prices — if any band quotes a dollar figure or a "worth ×N" claim, re-derive it against the format-multiplied spread (the standing rule from the falloff rebalance).
- [ ] **Step 3: Full verification sweep** — all must pass, outputs quoted in the final report:
  - `python prototypes/shuttle-computer/test/verify-port.py` (goldens byte-identical — this feature must not have touched a pattern)
  - `verify-library.py`, `verify-rent.py`, `verify-taste.py`, `verify-diagnostic.py`
  - `cd prototypes/shuttle-computer && npm test`
  - `python prototypes/shuttle-computer/test/compile-unity.py`
- [ ] **Step 4: Commit** `docs(trax): tape formats — audit + diagnostics`, then push `soundofspace/feat/helmet-hud`.

### Out of scope (deliberate, from the spec)
- MessagesScreen kind-pick chips on accept (the alien names the format; player negotiates price only).
- Song-closeness repeat guard (variation-nudge farming) — watch the playtest.
- Named requests stay demo/track-lineage.
- Guest-visible deck props replication (pre-existing gap).
- Sam-owned after merge: real prices/multiplier tuning from the diagnostic table, dialogue cut pass on the three new banks.
