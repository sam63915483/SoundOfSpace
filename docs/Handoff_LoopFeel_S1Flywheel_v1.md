# Handoff — Loop Feel: the Schedule 1 Flywheel Pass (v1)

**Date:** 2026-08-16 · **From:** external design review (Claude chat) · **For:** Claude Code
**Builds on:** the Aug 16 selling push (DealTerms, TapeOffer live, tier deals, C1–C5) — this handoff does NOT undo any of it.

> **STATUS 2026-08-17 — ALL FIVE PHASES BUILT (Sam GO'd A–E), playtest pending.**
> Commits `4cb820f6` (A), `81a253ec` (B+C), `305c0cc3` (D+E); mockups Sam approved live at `prototypes/loop-feel/` (:8083).
> Sam's locked [OPEN] answers: keep the last-paid slider tick · recap = phone message (system:wrap thread, speaks as the shuttle AI) · 1.25× request bonus stays baked into the quote · gossiper is NAMED · rejected-but-heard = +2 craving.
> Two [EXISTS] claims were false and are recorded here for honesty: (1) there was NO bond-scaled want-text cadence — Phase C *introduced* post-deal pacing (BuyerLedger.ReportTapeDeal writes nextTextAt from craving); (2) alien buyers had no locomotion at all — the wander system (AlienWander, built first at Sam's call, `e3b63f4d`+`f6889d13`) is what the ambush walks on.
> Phase E went further than "leave gratitude out of the money": `TapeDeal.Grade` lost the parameter entirely, and the parity test now asserts paid == agreed with no multiplier.
> Known co-op edges (solo-first per rule 6): the ambush walk renders host-side only (the hungry text is shared); guest sales don't feed the bought-track registry (wire carries dials, not prints) — fewer named requests for guests, nothing breaks.

## §0 — Ground rules (Sam's standing process)

1. **State current behavior first.** For every item, report what the code does today before proposing the change. If reality differs from what this doc assumes, say so and stop.
2. **Plan before build** (GDD_StoryBible_v2.md §0 rule 4). Post the build plan per phase; Sam corrects, then you build.
3. **Nothing destructive.** New system (craving) goes behind a `FeatureVault` flag. Removed copy is replaced, not deleted logic.
4. **Suites stay green.** taste/deal (2,469), rent (107), port goldens (2,024), library (97). Where a phase changes tested behavior (Phase E only), the test change is part of the plan and needs Sam's explicit GO.
5. **Old saves must load.** Any new persisted field uses the guarded-list pattern (like EvSave's 4th slot). Default values, no fresh-save requirement unless Sam approves one.
6. **Solo-first, co-op-aware.** Per the Aug 16 sequencing lock, build and test solo — but put data where the MP port expects it (world-scoped, riding the economy snapshot like bond).

**The design thesis (context for every item):** Schedule 1's engine is not simpler than ours — its customer profiles carry budgets, order schedules, addiction curves, and affinity evolution. It *feels* simple because the player never touches a formula: every number is expressed through a person's behavior, and every decision is one comparison. Our engine is already right. This pass hides the remaining exposed math behind faces, and adds the one flywheel S1 has that we don't: **craving** — demand that compounds when fed and decays when ignored, so the world pursues the player.

**Hard rule for the whole pass: no player-visible percentages, multipliers, or factor names on any selling surface.** Prices in dollars are fine. Everything else speaks in words.

---

## Phase A — Surface pass (no schema, no flags)

### A1. Satisfaction word ladder — one vocabulary, one source [BUILD]

[EXISTS] Gate constants in `AlienTaste` (LikeCertain 60, LikeMaybe 42); assorted feedback/memory/verdict lines across the sell panel, texts, and contact card, worded per-site.

[BUILD] A single function (suggested: `AlienTaste.SatWord(double s)`) mapping true satisfaction to a 5-word ladder. Suggested cuts, tunable: `<42 / 42–60 / 60–78 / 78–92 / ≥92`. Every player-facing surface that currently describes how much an alien liked a tape routes through it — verdict lines, feedback after a listen, memory lines, order-result lines, contact card. Word choice is flavored per-line ("that was *decent*, I guess") but the ladder word itself is consistent everywhere so players learn one scale.

[AUTHOR] Placeholder ladder to build against: **junk / not for me / decent / love it / MASTERPIECE**. Sam writes the final five words — report the placeholder in the plan and flag it for him.

[OPEN] The 42–60 coin-flip band: the *verdict* is the flip outcome (unchanged), but the spoken word should reflect true satisfaction ("not for me… but fine, I'll take it" on a won flip). Confirm this reading of the flip in the plan.

[TEST] A ToVerb-style offline check: every registered selling-surface string passes a no-`%`/no-factor-name scan; ladder mapping covered at the band edges.

### A2. Strip the walk-up slider to your number and their face [BUILD]

[EXISTS] C2 shipped today: anchor = Base×SatMult×Bond, slider 0.5–4.5×, bands "reworded in population terms."

[BUILD] **Keep all of C2's math and ranges — this is copy/UI only.** Remove the street-value dollar label and the population-share band text. The slider shows: buyer name/portrait, your ask in dollars, nothing else. Their reaction (ladder word, counter, final offer) is the information channel. One permitted diegetic aid: a small tick at *this buyer's* last-paid price, sourced from the ledger event log (the trap-7 fix) — that's remembered knowledge, not system knowledge.

[OPEN] Keep or drop the last-paid tick — Sam's call in the plan review.

### A3. Thin kits get a voice [BUILD]

[EXISTS] Pro-rata pays less for thinner-than-contract kits (C1); text-order overreach became "remark not refusal" (Aug 14).

[BUILD] When low module count is what's holding a price or grade down, the alien says it in-world. 3–5 line variants in the vein of "needs more machines in it." Wire wherever pro-rata shortfall or low `modulesBasis` bites.

[AUTHOR] The lines — draft them, Sam edits.

### A4. Plugins you can hear before buying [BUILD]

[EXISTS] TevShopUI panel (layout F, two tabs); TraxTapePlayer pooled world player; module presets in the TRAX library.

[BUILD] Each PLUGINS row gets a LISTEN action: plays a short loop featuring that module (that module + THUMPER, a designated preset) through TraxTapePlayer at the shop. Stops on tab switch, purchase, or panel close. Report in the plan how preset selection will work before wiring.

[AUTHOR] One demo preset per module if suitable ones don't exist.

---

## Phase B — Day recap card [BUILD]

[EXISTS] GalaxyTime day tick (24 real min); ledger event log; rent state incl. arrears and the 5-day plugin lockout; Messages stack.

[BUILD] At each day increment, one dismissible summary: tapes sold, dollars earned, rent state (paid / owed $X / N days to plugin lockout), bond-ups by name, expiring open orders, and — once Phase C exists — who's getting hungry. **Informational only: rent still moves exclusively through TevPaymentUI.** Nothing is deducted, nothing is gated.

[OPEN] Delivery vehicle: a phone message in the existing Messages thread stack (zero new UI) vs. a HUD card. Recommend phone message for v1; Sam decides.

[TEST] Recap content assembled headlessly from a scripted day's events.

---

## Phase C — Craving: the flywheel [BUILD — behind `FeatureVault.CravingSystem`]

The one genuinely new system in this handoff. S1's addiction analogue, reskinned: every alien knows the black hole is coming, so music obsession is canon. **Craving is demand, never a gate — it must not block, discount, or upgrade any sale. Bond stays the trust/price stat; craving is the hunger/frequency stat.**

[EXISTS] BuyerLedger (world-scoped, saved, rides the economy snapshot); bond-scaled want-text cadence; walk-up flow via the sell panel; TapeMemory song history.

[BUILD]
- **Stat:** `craving` 0–100 per buyer in BuyerLedger. Guarded field, defaults 0 on old saves.
- **Gain on a completed sale**, by satisfaction band (tunable starting points): masterpiece +18 · love it +12 · decent +7 · below +3. Fulfilling a named request: +4 extra. Cap 100.
- **Decay:** end of each in-game day with no purchase from that buyer: −8, floor 0. A purchase that day means no decay that day. (Mirrors S1's ~7%/day idle decay.)
- **Order frequency:** existing cadence gets a craving multiplier, lerp 1.0→2.5 across 0–100. At ≥90: guaranteed at least one order per day (S1's daily-order threshold analogue).
- **Ambush walk-ups:** craving ≥60 and ≥1 full day since their last purchase → eligible. At most one ambush per day globally. The alien approaches the player and opens the normal walk-up flow with a hungry want line ("been humming that VOLT tape all week — got anything new?"). Normal rules from there: listen, gate, ask. No special pricing.
- **UI:** contact card shows a 4-word craving ladder — suggested: *curious / interested / hooked / obsessed*. No numbers anywhere.
- **Co-op posture:** world-scoped, whole-ledger snapshot + version counter like bond (P5 pattern). Build solo; do not start MP work.

[OPEN] (1) Ambush approach behavior — reuse the existing NPC approach/pathing or a simple beeline? Report what exists first. (2) Should rejected-but-heard listens give +2 craving (they still got music)? Recommend yes, Sam decides. (3) Exact gain/decay numbers are tuning targets, not spec.

[TEST] Headless: gain-by-band, decay schedule, frequency multiplier, ambush eligibility windows, determinism across save/load. Extend the taste suite.

---

## Phase D — Requests that name your catalog [BUILD]

The discovery-becomes-demand loop: aliens sometimes ask for a *specific track of yours* they've heard about. Word-of-mouth framing dodges the already-heard refusal by construction.

[EXISTS] Want-text generation (genre-based); TapeMemory heard/sold history per buyer (sold counts as heard as of today); project shelf; reprints (renamed reprints refresh display name, trap 9); tier-aware deal flow; DealTerms contract.

[BUILD]
- **Eligible track for buyer B:** sold to ≥1 buyer other than B, NOT in B's heard list, and its project still exists on the shelf (reprint must be possible). No eligible tracks → normal genre request.
- **Frequency:** ~30% of generated orders when ≥1 track is eligible (tunable).
- **Text:** names the track — "heard *Gorp Slime* at Krib's — got a copy?" Contract records the trackId; the tier flow runs as shipped.
- **Grading:** delivering any pressing of that track = exact goods (existing lineage match by cassetteId family). A different track = the wrong-goods path exactly as it works today. No new grading rules.

[OPEN] (1) Name the other buyer in the text for flavor, or keep it vague? (2) Should fulfilling a named request pay the existing 1.25× request bonus, or is the craving +4 (Phase C) reward enough? Note the Phase E interaction and put both options in the plan.

[TEST] Eligibility query headless (sold-elsewhere, unheard-here, shelf-alive); refusal path never trips on a named request by construction.

---

## Phase E — Collapse the gratitude stack [NEEDS SAM'S EXPLICIT GO — touches Aug 16 shipped, tested behavior]

**Why:** request bonus 1.25×, gratitude +15/10/5%, and bond gains all reward the same behavior (serving regulars well). Three overlapping rewards is formula-smell; S1 pays this behavior in *relationship*, not stacked percentages. This also makes the contract perfectly clean: **the agreed number is the paid number.**

[EXISTS] Window pick (5/10/15 min) carries +15/10/5% gratitude into the money path; DealTerms/TapeDeal.Grade computes agreed × gratitude; DealTests asserts it.

[BUILD — only after GO]
- Windows remain purely scheduling (deadline). Gratitude multiplier leaves the money path entirely.
- On-time delivery: +4 bond and a spoken thanks line. Late-but-in-window nuance stays as-is.
- Want-text copy drops all % promises.
- DealTests updated: deliver-as-promised at the untouched ask pays **exactly the agreed number** — a simpler, stronger assertion. Parity count will change; that's expected and part of the plan.

[OPEN] Whether the 1.25× request bonus also folds into bond at the same time, or survives as the one remaining "serve the request" money incentive. Present both; Sam picks.

---

## Sequencing and scope guard

Suggested order: **A → B → C → D → E.** A and B are same-day-sized. C is the real build. D leans on C's testing patterns but doesn't depend on it. E waits for Sam's GO.

Do not touch `TapeValue`/`AlienTaste` core math, the DealTerms contract split, tier preferences, or the walk-up ceiling rules — today's work is the foundation this stands on. This handoff adds exactly one system and otherwise moves math *out of sight*, not out of the game. If an item seems to require a second new system, stop and report instead.
