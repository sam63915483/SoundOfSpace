# The Cassette Selling System — Full Design & Implementation Handoff

**Date:** 2026-08-16 · **Game:** Sound of Space (working title "Solar System 2"), Unity 2022.3, solo dev (Sam)
**Purpose of this document:** a complete, self-contained description of the tape-selling economy for an external design review. Everything here was verified against live code on this date (file:line references included). **If something is not in this document, assume it does not exist** — a previous external review invented a test harness that didn't exist, so please reason only from what is written here, and clearly separate "change the design" suggestions from "the code diverges from its own design" observations.

---

## 1. What the game is, in one paragraph

A first-person spherical-planet survival/economy game. The core money loop (recently pivoted from selling mushrooms): the player composes music on an in-world tracker ("TRAX", a shuttle computer), prints tracks onto physical cassettes, and sells them to alien NPCs — either by walking up to a wandering alien, or via text-message orders on the in-game phone that turn into timed meetup deals. Money pays daily rent to a landlord NPC (Tev) and buys blank cassettes and synth plugins. There is drop-in co-op (host-authoritative for world state).

Rent (corrected per review A8): a **one-time haggle with Tev sets the flat daily rate** — 50 → 30 → 20 → 10 depending on how well you haggle, never free, arrears accrue linearly, paid by hand (never auto-deducted). Starting cash is **$0** (money is a hotbar item; a new game zeroes it); Tev's fronted demo tapes are the starting inventory.

**Sam's design laws** (the yardstick for this review):
1. *"Systems should be simple so they can be fun, but intricate so that you can learn them and actually play the game."* The player's learned mental model of buyers IS the content.
2. Tolerance and payout are inversely linked: most buyers accept a wide range and pay little; rare picky buyers pay a premium when you nail their taste. Early game should be easy-but-cheap; finding a superfan is the jackpot.
3. The recurring gamebreaking bug class here is the **promise/grade mismatch**: one system displays or promises X while another grades Y. The three originally-shipped examples and their fixes are described in §8. Anything in this class outranks balance concerns.

---

## 2. The goods: tracks, genres, cassettes

- A **track** is authored on 6 dials, each 0–10: `PULSE, CRUNCH, GOO, VOID, JITTER, WARP`. Up to 6 synth **modules** (THUMPER, GLOWORM, MOSS, SIREN, SPINDLE, CAVE) can be active; each has 5 presets × 8 variations. **Presets/modules change what you hear but contribute NOTHING to taste or classification — only the 6 dials and the active-module COUNT matter commercially.**
- A **genre** is a derived label, not an axis: 10 fixed centre-points in the 6-D dial cube (`TraxClassifier.Genres`, e.g. `VOLT` "aggressive electric dance" at (8,7,4,2,5,5), `CLANG` "metallic industrial banger" at (6,8,2,5,8,9)). Classification is nearest-centre. If the runner-up centre is within 1.5 distance, the label is a **blend**: adjective + primary, e.g. **"Clangin' VOLT"** (primary VOLT, secondary CLANG).
- A **cassette (print)** freezes a track onto a physical tape item. Tapes have a **tier**: Type 1 (blank costs $5 at Tev's shop) or Type 2 (blank costs $15, shop copy says "Worth double when you sell it"; the value model applies ×2 to the base). Prints are identified by track+tier hash; records persist in saves.

---

## 3. The buyers: derived taste, no authored content

Every alien buyer's commercial personality is **derived by hashing its stable identity string** (`AlienTaste.cs` — pure C#, no Unity types, headless-testable). Nothing is stored; taste survives save/load and streaming, and is identical on both machines in co-op with nothing on the wire. Wandering aliens use `cell:{bodySlot}:{cellId}` ids (a new map cell = a new alien = a new ear); pre-placed NPCs use `scene:{name}`. There are ~10 wanderers alive at once, streamed in/out around the player.

Per-alien traits (each salted separately so traits never correlate, except where noted):

| Trait | Range / distribution | Meaning |
|---|---|---|
| `FavouriteGenreIndex` | uniform over the 10 genres | the genre whose centre their ear sits near |
| `TastePoint` | favourite genre centre ± jitter 1.8 per dial (clamped 0–10) | their exact ear, a 6-D point |
| `Falloff` (pickiness) | 0.85–1.75, **skewed tolerant**: `t = u^2.5` (as of 2026-08-16) | how fast satisfaction drops per unit distance |
| `PayFactor` | 0.55–3.0, **derived FROM falloff** (not its own hash) | what they pay relative to market; the fussy alien is by construction the premium payer |
| `Patience` | 1.05–1.45, own salt | how far over their number you can push before they stop playing along |

With the skew, ~57% of aliens sit in the bottom quarter of the pickiness band (broad, cheap) and ~15% in the top third (fussy, premium) — exact: bottom quarter = 0.25^(1/2.5) ≈ 57.4%, top third = 1 − (2/3)^(1/2.5) ≈ 15.0%.

**Tier preference (added after the review, Sam's design):** each buyer also hashes a cassette-shell preference — ~30% prefer Type 2 ("snobs"), ~30% prefer Type 1 (Type 2s cost more than they'll pay), ~40% don't care. A mismatched shell never hard-blocks on its own: it downgrades the verdict one step (Liked → CoinFlip → Rejected), discounts their pay ×0.75, and the rejection line *names it* ("I only really rate Type 2 tapes" / "Type 2s cost too much — I stick to Type 1s"). Text orders quote the buyer's preferred tier.

**Satisfaction** = `100 − 5.5 × Falloff × EuclideanDistance(trackDials, tastePoint)`, clamped 0–100.

**Verdict gate** (`AlienTaste.Gate`): ≥ 60 → Liked (certain); 42–60 → CoinFlip (caller rolls 50/50); < 42 → Rejected. The coin-flip band is deliberate: a marginal tape is worth *trying* on someone, which stops the player computing everything and never talking to anyone.

**The hint contract** (`AlienTaste.GateFor`, added 2026-08-16): every hint in the game — text orders ("after 1 CLANG tape"), ledger reveals ("has a soft spot for CLANG"), rejection lines ("I'm more of a CLANG listener") — names the alien's favourite **genre**. Therefore a tape the classifier files under that genre (as primary, or as the named half of a blend like "Clangin' VOLT") is **never refused** by that alien. Verdict only — the price still follows true satisfaction, so hitting the genre buys the sale and hitting the exact ear buys the money. Rationale: before this, a hint-following player offering a *near*-genre tape (e.g. a VOLT-classified track to a CLANG fan) failed a measured 53% of the time (26.8% outright rejections + half of a 52.5% coin-flip band), which reads as the game lying. The flip itself is 50/50; 53% was the measured total failure rate.

**Song memory** (`TapeMemory`): an alien remembers songs they've been played (dial-distance ≤ 1.5 counts as "same song"); replaying one is a social failure. Walk-ups block it before taste; deliveries also refuse a heard tape ("bring me one I haven't heard") with the **appointment left open** in its window — you brought the wrong tape, you didn't no-show. A **lost coin flip does not** write memory (only an outright taste rejection, or a completed sale, does), so bad luck doesn't permanently burn a song on that buyer.

---

## 4. Pricing (`TapeValue`, pure C#)

```
value = (Floor 6 + PerModule 4 × activeModules)      ← "Base"; ×2 if Type 2
      × SatisfactionMult (0.35 + 0.95 × sat/100)     ← 0.35..1.30
      × BondMult (1.0..1.4 over bond 0..100)
      × RequestBonus (1.25 if it matches an order)
      × PayFactor (0.55..3.0, per-alien)
```

At the starting 2 plugins, `Base` = $14 (Type 1). A tolerant cheap buyer pays roughly $8–9 for a decent tape; a bonded fussy superfan with a bullseye tape reaches $40+ before haggling. Negotiation numbers derived from `value`: the buyer's *ceiling* is `value × Patience`; pushing past a ceiling now produces a declared **final offer** (`TapeValue.FinalOffer`, deliberately below their value) — take it, or walk for a small bond sting. No lockout on this path; probing ceilings is how the game is learned.

All economy knobs are `const` in `AlienTaste.cs` / `TapeValue.cs` — deliberately not Inspector-serialized, which keeps the model headless-testable and immune to the project's scene-override trap.

---

## 5. Sale channel A — the walk-up sale (`MushroomSellUI`, the live path)

Walk up to any alien, open the sell panel (the file is named `MushroomSellUI` for legacy reasons; tapes are the live goods — mushroom selling is feature-vaulted off).

1. Player puts a tape "on the table"; the song audibly plays (the buyer pricing an unheard song would be absurd).
2. **Listen roll**: repeat check (memory) → `GateFor` verdict → coin flip if in the middle band. A rejection names the dial they most want moved ("more CRUNCH…") and their favourite genre second — rejection as lesson.
3. If liked: the player sets an **ask** on a slider and confirms. Grading: `Fair` = the full value formula above (their true number, hidden). Ask ≤ 1.02×Fair → sold at your price. ≤ Patience×Fair → they counter at ~Fair (counter persists on that buyer). Beyond → **barred**: −10 bond, 5-minute lockout, any open appointment quietly cancelled.
4. The slider is anchored on a displayed "market value" figure = `Base` only (no satisfaction/PayFactor) with range 0.5×–2× of it, and band copy from "an easy yes" through "asking exactly market value" (green) to "absurd" (red). **See §9 trap 3 — this anchor is one of the confirmed mismatches.**
5. Panel shows a memo line ("you remember: paid N for a tape") + earned reveal lines — **see §9 trap 7, currently dead on the tape path.**

## 6. Sale channel B — text orders and meetup deliveries

**The state machine** (all host-authoritative; `BuyerMessageDirector` early-returns on guests):

1. After a first deal creates a contact, buyers with sufficient bond periodically text a **want-text**: "in the mood for something VOLT. 1 of them, 18 each." The genre asked is always their true favourite (a request for something they don't like would be a lie the taste model can't back). The quoted price = 90% of `TruePricePerTape` — the value formula at fixed satisfaction 85, request bonus on, **tier hardcoded to Type 1** (see §10 — the tier feature closes this).
2. Player replies via chips (no free text): **ACCEPT** → pick the cassette **tier** ("TYPE 1 · $x / TYPE 2 · $y", the order's own tier marked) → pick a meetup window (5 min "+15%", 10 min "+10%", 15 min "+5%" gratitude labels) → deal `Scheduled` at that tier's quote. **COUNTER** → a Type 1/Type 2 toggle + one price slider; the buyer accepts (≤ their patience ceiling at that tier), counters back a number ("TAKE 23" → then the window pick), or refuses. **NOT NOW** → declined. A counter-agreed price also gets a window (the `PriceAgreed` state offers window chips), so gratitude applies on that path too.
3. The agreement stores: genre, qty (always 1 for tapes currently), **cassette tier**, **modulesBasis** (the plugin count the quote was priced against), agreed price-per-tape, window, deadline. Missing the deadline halves bond and sends a "you never showed" text.
4. **The meetup**: open the same sell panel on that buyer within the window (+60s grace). The panel enters delivery mode: ask slider seeded and anchored at the **agreed** price, an ORDER header ("1 VOLT @ 20 each agreed · on time (+15%)"), and a risk band ("exactly as agreed" / "asking over the agreed 20 — they may walk" / "wrong goods — long odds").
5. **Delivery judgment** (`DeliverOrder`): `exactGoods` = right genre (blends count) and enough tapes. Chance = `(exactGoods ? 1.0 : 0.45) × OverchargeFactor(ask, agreed)` where asking over the agreed price decays acceptance fast (+10% over → 0.8, +25% → 0.5, +50% → 0.05 floor) — deliberately a visible gamble ("agree 20, demand 30 — they may take it, they may not" is Sam's rule). Failure: −5 bond, appointment dead, 5-minute bar.
6. On success: exact goods at untouched price pays `agreed × gratitude`; any deviation pays your ask; **then the payout is clamped to `worth`** — the full value formula recomputed on the delivered tape with real satisfaction — with the line "This isn't the track I was picturing" if clamped. **See §9 traps 1–2: this clamp is the biggest confirmed break in the system.**

**Bond & regulars**: sales earn bond (+8 walk-up base, favourite-genre bonus +4, kept appointments more); bond drives BondMult, text frequency, and reveal lines (5 tiers: favourite genre → pickiness word → payer word → patience word → …). Regular status (guaranteed repeat business) converts on favourite-genre deals — guaranteed if `matchedTaste`, else a 1-in-3 roll.

**Tev integration**: rent collected daily by hand (never auto-deducted); Tev sells blanks ($5 T1 / $15 T2) and plugins; a "fronting" system (Tev fronts you demo tapes, takes a 50% cut on his `Base` valuation) exists in code but is **feature-vaulted OFF** in this build.

## 7. Persistence & co-op (what a reviewer needs to know)

- Taste is derived, never stored — save-schema-free, identical on host and guest by construction.
- Stored: the buyer ledger (bond, deals, appointment fields, event log for the message threads), song memory, prints, deal counters. All in one JSON save (JsonUtility — parallel lists, no dicts).
- Co-op: host-authoritative economy. The ledger replicates host→guest as a whole snapshot on a version bump. Guest phone replies route through three small host messages (accept/counter/decline). **Known confirmed gap: a guest's tape sale/delivery is applied only locally and then overwritten by the next host snapshot — §9 trap 4.** The walk-up accept coin flip is client-local `Random.value` (unsynced).

## 8. Recent fixes already shipped (2026-08-16, same day as this doc)

1. Vocabulary/label unification: orders and order-delivery accept a blend's second name ("Clangin' VOLT" fills a CLANG order); sell surfaces show the same full label the console shows.
2. Scheduled-deal slider no longer re-seeds at market value when a tape is placed — it stays at the agreed price (this was silently walking the player into the overcharge gamble).
3. The hint contract (`GateFor`) — on-genre tapes never refused by that genre's fan.
4. Lost coin flips no longer permanently burn the song on that buyer.
5. Population re-skewed tolerant (FalloffSkew 2.5, floor 0.85) + pay band widened to 0.55–3.0 + value floor $3→$6 — implementing design law #2.

## 9. CONFIRMED OPEN ISSUES (each traced to code; ranked)

> **STATUS UPDATE 4 (tape formats, 2026-08-18):** the goods model grew a third
> axis — **FORMAT** (`TraxKind`: Demo / Half-Length / Full-Length; blanks $5/12,
> $15/25, $22/35; Half caps a song at 50 bars, Full at 100). Every print is now
> a frozen **TraxSong** (a demo = the whole selected SECTION, bars included);
> §4's value gains `× FormatMult` (`TraxSong.ValueMult`, ~×2–×4.9 placeholder);
> §3's satisfaction for a song is the **bar-weighted mean** of per-section
> satisfaction (`SongEval`) with the verdict = best section through `GateFor`
> (hint contract holds per-section), so a multi-genre song sells to more aliens
> at a diluted price — measured distribution in `verify-diagnostic.py`. Orders
> carry `askKind` (derived preference clamped by the **TapeCareer** milestones:
> Tev stocks Half at 10 tapes sold, Full at 25) and quote/grade at the format's
> nominal multiplier, pro-rata on shortfall (`kindShort`). Song memory keys on
> SongId (buying a section's demo never blocks the full song — that first liked
> song after ≥3 demo deals triggers the `ForGrowth` line + bond). Guest wire
> carries songId+kind. Suites: taste 2654 / library 124 / rent 119, all green.
> Spec: `docs/superpowers/specs/2026-08-18-tape-formats-design.md`.
>
> **STATUS UPDATE 3 (C5 pass, `78587322`):** the structural item is done. Pure `Music/DealTerms.cs` holds the deal slip + `TapeDeal.Grade` (the single delivery grader; the panel assembles terms and computes no money), quote math lives in one core wrapped by `TapeTrade`, and the **parity test** (`test/DealTests.cs`, in `verify-taste.py` — suite now 2469 checks) enforces "delivered-as-promised pays exactly-as-displayed" across randomized buyers × terms. `TapeOffer` is no longer a ghost rulebook: the walk-up panel routes through Listen/Value/Judge, with Sam's confirmed semantics — an ask at or under the buyer's ceiling is paid at YOUR price; over it, a counter; outrageous, a final offer. Tier preference is now contact-card reveal #5.
>
> **STATUS UPDATE 2 (after the review came back — `REVIEW_HANDOFF_SellingSystem_v1`):** the review's recommendations are now largely BUILT: **C1/C4/B1** (the agreed price is the contract; tier + modulesBasis recorded on the agreement, shown on the ORDER header and appointment card, delivery pays pro-rata on the objective goods ratio `Base(deliveredMods, deliveredTier)/Base(contractMods, contractTier)` — Type 1 on a Type 2 deal is exactly half; the satisfaction clamp on exact goods is gone); **C2** (walk-up anchor is "street value" = Base × SatMult × BondMult, slider 0.5×–4.5×, population-worded bands); **C3** (final offer replaces the hard bar for over-patience asks and failed pushes; walking from a final offer costs −4 bond, no lockout; the +3 generosity bond for asking under value is wired); **B2** (deliveries refuse already-heard tapes, appointment stays open); §10's tier-aware negotiation is built, PLUS Sam's addition: per-alien **tier preference** (~30% prefer T2, ~30% prefer T1, verdict downgrade + ×0.75 pay on mismatch, named in rejection lines, orders quote the preferred tier). Doc corrections A1–A9 applied throughout. **Not done:** the full C5 `DealTerms` refactor (UI still calls TapeValue/TapeTrade directly at ~25 sites; the parity property-test therefore doesn't exist yet) — the contract fields and single grading site capture its spirit, the structural enforcement is future work. `TapeOffer.cs` remains uncalled as a module (its final-offer/generosity/freshness RULES now ship via the panel) — unify or delete in the C5 pass.
>
> **STATUS UPDATE (later the same day, after this doc was handed to the reviewer):** traps 1, 2, 3, 4, 5, 7, 8 and 9 below have been **fixed** as sketched (contract honored for exact goods with an arrangement-completeness pro-rata; walk-up bands/slider reworked honestly around the hidden pay spread, range 0.4×–6× market; guest tape sales/deliveries + song memory now routed through the host and memory rides the snapshot; blend-aware taste match in the bond path; memo reads the ledger; shop copy honest; reprints refresh the name). One extra fix found during implementation: **sold songs were never written to memory at all**, so the same track could be re-sold to the same buyer forever — sales now count as heard. Trap 6 (dead `TapeOffer` rulebook / final-offer-vs-hard-bar) is deliberately left open pending this review's answer to question 3.

These are **not speculative** — each was verified line-by-line this date. Fix sketches are the current working intent; the review is welcome to propose better ones.

**Trap 1 — GAMEBREAKING: a haggled text price can never be paid.** The phone quotes/negotiates from `TruePricePerTape` (satisfaction fixed at 85 → SatMult 1.1575); delivery clamps the payout to `worth` (real satisfaction, SatMult caps at 1.30). So `worth ≤ 1.123 × quote` *mathematically*, while the counter slider lets the buyer agree up to 1.45×. Player story: buyer agrees "26 a tape, don't be late" in writing; panel shows "exactly as agreed" in green; delivery pays 22 with "This isn't the track I was picturing", and the thread prints both numbers back at the player. Every successful haggle is a guaranteed underpayment; the counter mechanic is decorative. *Sketch: the agreed price IS the contract — drop the worth clamp for exact goods (keep it only for wrong-goods substitutions), or quote from the same figure that grades.*

**Trap 2 — GAMEBREAKING (same clamp): the advertised "+15%" gratitude bonus is unreachable.** Two surfaces promise it unconditionally (window chips, ORDER header); the clamp eats it unless satisfaction ≥ ~89 (a near-bullseye). Accepting an opening offer is underpaid below satisfaction ~73. *Dies with trap 1.*

**Trap 3 — GAMEBREAKING: the walk-up "market value" anchor is not the grading number.** Displayed market = `Base` only; the bar test uses `Fair` = Base × SatMult × BondMult × PayFactor. After the pay-band widening, for a bottom-third payer, asking "exactly market value" (labelled green) is over their patience → 5-min bar + −10 bond. For ~28% of the population the slider's own MINIMUM (0.5×market) exceeds what they'd pay, so **no slider position can complete the sale** — the buyer's counter is below the slider floor. Inverted: a superfan's Fair reaches ~5.4× market but the slider caps at 2× ("absurd", red) — **the jackpot the rebalance created cannot be collected.** *Sketch: anchor slider + band copy on `Fair` (or an honest partial), range 0.5×Fair to ~1.4×Ceiling.*

**Trap 4 — GAMEBREAKING in co-op: guest tape sales aren't reported to the host.** A `sentToHost` flag exists but is never set; there's no tape-sale message. Guest's bond/deal/reveal progress is wiped by the next host snapshot; worse, a guest-delivered appointment stays `Scheduled` on the host, whose deadline sweep then fires "you never showed" — bond halved *after* a successful, paid delivery. Song memory is also per-machine despite being documented as world state. *Sketch: add a tape-sale report message (buyer, price, qty, genre, keptAppointment, matchedTaste, heard-dials); host applies ledger + memory.*

**Trap 5 — blend fix missed the bond path.** Order-filling and the hint contract accept blends, but `matchedTaste` (drives the +4 favourite-genre bond and the *guaranteed* regular conversion) still compares the primary label only. Deliver "Clangin' VOLT" to a CLANG fan's order: sale succeeds, but no favourite bonus and regular status collapses to a 1-in-3 roll — the player did exactly what the hint asked and doesn't become their regular. *Sketch: use the same MatchesFavourite helper everywhere.*

**Trap 6 — a documented, tested rulebook that doesn't run.** `TapeOffer.cs` (pure, covered by the headless suite) documents the intended negotiation: pushing too far yields a FINAL OFFER ("take it or leave it" — a decision, not a dead end), generosity (+3 bond for asking under value), a greed band, and an already-heard check on delivery. **Nothing in the game calls it.** The shipping panel implements a harsher, different rule (instant 5-min bar; no final offer; no generosity reward; no already-heard check on deliveries). Anyone reasoning from the source (or its tests) reasons about a system that isn't running. *Sketch: either route the panel through TapeOffer (getting the friendlier, already-written design) or delete the file+tests. Reviewer opinion actively wanted: the FINAL OFFER design vs the current hard bar.*

**Trap 7 — the panel's memory line is dead on tapes.** "you remember: paid N" + two reveal lines render only if `LastPaid > 0`, which is never written on the tape path — so the panel tells a five-time regular "you've never dealt with them", and those reveals only survive on the phone contact card. *Sketch: record last-paid on tape sales, or drive the memo off the ledger, which has the data.*

**Trap 8 — the Type 2 shop copy oversells.** "Worth double when you sell it" doubles `Base`, but the buyer multiplies by SatMult × PayFactor; against the (now-majority) cheap buyers at 2 plugins the $10 shell premium returns ~$8.40. Related: text orders quote tier-1 prices (hardcoded), so a Type 2 delivered on an order collects zero premium — this is what §10 fixes.

**Trap 9 — minor: reprints keep the first pressing's name.** Print ids derive from track+tier, so renaming a project and reprinting the same track shows the old name on every sell surface (intentional for sold copies, surprising for reprints).

## 10. PLANNED (agreed with Sam, not yet implemented): tier-aware text deals

The negotiation gains the cassette tier as a first-class term:
- Want-texts quote both numbers (tier-aware pricing is exactly a ×2 on the existing quote: value is multiplicative).
- The reply flow gains a **Type 1 / Type 2 choice** (accept path: a tier pick before the window pick; counter path: a T1/T2 toggle that rescales the price slider) — so a player who only stocks Type 2s can say "I only sell higher quality — $30."
- The agreement records the tier; the appointment card and ORDER header show it.
- Delivery: agreed tier → normal; **lower tier than agreed → "this isn't a Type 2, I'll only pay half"** (half-pay, not refusal — the deal was made in good faith); higher tier than agreed → accepted at the agreed price (player's choice to be generous).
- Implementation is mapped (save-schema guard for old saves; the appointment field replicates in the existing snapshot; the guest reply message needs one more int).

## 11. Questions for the reviewer

1. **The contract question (traps 1/2):** should an agreed price ever be clamped by "what the tape is really worth to them"? Current lean: agreed price is sacred for exact goods; the clamp survives only for substituted/wrong goods. Is there a better mechanism that preserves "thin sketches shouldn't farm full price" without breaking written agreements?
2. **The anchor question (trap 3):** what should the walk-up slider anchor and band copy communicate, given the buyer's true number is deliberately hidden (learning it IS the game)? How do we keep the 3× superfan payday collectible without leaking who is a superfan before the player has learned it?
3. **Final offer vs hard bar (trap 6):** which negotiation failure state makes the better game?
4. Does the tier design (§10) close the goods-ambiguity class fully, or is there a remaining way both sides can "agree" while meaning different things (qty? genre blends? window)?
5. Any structural suggestion to prevent the promise/grade class from recurring — e.g. a single "deal terms" object that both the display and the grader must read?
6. General balance sanity check of §3/§4 numbers against design law #2 (rent $50/30/20/10 daily, blanks $5/$15, ~10 buyers alive, day = 24 real minutes).

## 12. Appendix — code map & tests (so nothing is invented)

| Area | File (all under `Assets/3 - Scripts/`) |
|---|---|
| Taste model (pure) | `Music/AlienTaste.cs` |
| Pricing (pure) | `Music/TapeValue.cs` |
| Classifier/genres (pure) | `Music/TraxClassifier.cs` |
| Negotiation math (quotes/counters) | `Music/TapeTrade.cs` |
| Dead rulebook (see trap 6) | `Music/TapeOffer.cs` |
| Song memory | `Music/TapeMemory.cs` |
| Prints/cassettes | `Music/TraxPrints.cs`, `Music/CassetteDeck.cs` |
| Sell panel (walk-up + delivery) | `Vendor/MushroomSellUI.cs` (legacy name; tapes are live) |
| Buyer ledger/bond/reveals/events | `Vendor/BuyerLedger.cs` |
| Deal rules (windows, gratitude, overcharge) | `Vendor/BuyerDeals.cs` |
| Message copy | `Vendor/BuyerTexts.cs` |
| Want-text director (host-only) | `Vendor/BuyerMessageDirector.cs` |
| Phone Messages UI | `UI/Messages/MessagesScreen.cs` |
| NPC dialogue sell rows | `Vendor/NPCSellRows.cs` |
| Tev shop / fronting (vaulted) / rent | `Vendor/TevShopUI.cs`, `Story/TevFronting.cs`, `Story/MushroomQuest.cs` |
| Co-op economy sync | `Multiplayer/EconomySync.cs` |

**Tests that actually exist** (headless C#, compiled with Unity's Roslyn by python runners in `prototypes/shuttle-computer/test/`): `verify-taste.py` → `AlienTasteTests` + `TapeOfferTests` (113 checks: taste stability/spread, gate bands, pricing shape, memory, the TapeOffer negotiation — note trap 6: TapeOffer itself is dead in-game); `verify-rent.py` → rent ledger + cassette deck (107 checks); `verify-diagnostic.py` prints price tables per module count. There is **no** play-mode/integration test harness and no CI.
