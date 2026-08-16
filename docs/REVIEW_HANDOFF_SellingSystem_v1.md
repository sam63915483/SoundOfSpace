# External Review Handoff — Cassette Selling System

**Date:** 2026-08-16 · **Reviews:** `SELLING_SYSTEM_HANDOFF.md` (same date)
**For:** Claude Code, after the first fix pass on traps 1–9
**From:** external design review (Claude chat). **The reviewer had NO code access** — everything below was derived from the handoff doc plus Sam's design history. Every item marked VERIFY needs line-level confirmation against the actual code before any fix ships. Per Sam's standing process: report current behavior back before changing it, nothing destructive, and design decisions in §C need Sam's explicit go before building.

Three sections:
- **§A — the handoff doc contradicts itself.** Mostly doc edits; a few reveal undefined behavior that needs a code check.
- **§B — two new open issues** in the doc's own top-priority class (promise/grade mismatch). Treat as traps 10–11 pending verification.
- **§C — reviewer recommendations** on the doc's §11 questions. These change the design. Sam has seen them and reacted positively in review, but each one is a build only after Sam confirms it here.

---

## §A — Doc corrections (fix `SELLING_SYSTEM_HANDOFF.md`; VERIFY items also touch code)

**A1 — Population percentages don't match FalloffSkew 2.5.**
With `t = u^2.5` over 0.85–1.75: bottom quarter of the pickiness band = 0.25^(1/2.5) ≈ **57.4%** (doc says ~55%, close enough), but top third = 1 − (2/3)^(1/2.5) ≈ **15.0%**, not ~19%. 19% matches exponent 2.0 (18.4%) — likely stale from before the reskew. Fix the doc numbers; VERIFY the shipped exponent really is 2.5.

**A2 — "a hidden 53% coin flip" (§3, hint-contract rationale).**
§3 defines the flip as 50/50 (caller rolls). Either correct 53→50, or if 53% meant something else (e.g. measured share of on-hint tapes landing in the flip band pre-fix), say that explicitly.

**A3 — Trap 1's math mixes reference frames, and one sentence overclaims.**
§6 defines the texted quote as **90% of** `TruePricePerTape`; trap 1's "worth ≤ 1.123 × quote" is actually vs TruePrice. Against the number the player sees, the ceiling is 1.30 / 1.1575 / 0.9 ≈ **1.248 × quote**. Also "every successful haggle is a guaranteed underpayment" is false as stated: a counter to c × quote is clamped iff real SatMult < c × 0.9 × 1.1575, so e.g. a +10% haggle on a sat-84+ tape pays in full. Guaranteed-regardless-of-tape only above c ≈ 1.25. The trap itself stands (the counter range exceeds the ceiling); make the doc's math exact — the doc's credibility rests on it.

**A4 — Wrong cross-reference in §1.**
Design law 3 says "Three shipped examples are described in §9." §9 is the OPEN issues; the shipped examples/fixes are §8.

**A5 — Song-memory scope, plus one VERIFY.**
§3 states replaying a heard song is "blocked before taste" as a system truth; trap 6 says the delivery path has no already-heard check. Scope §3's claim to walk-ups. VERIFY and then document: **does a delivery write song memory at all?** If not, orders neither check nor burn songs — the doc (and §B2's fix) must reflect whichever is true.

**A6 — The counter path has no window — VERIFY, possibly trap-class.**
§6.2's COUNTER flow never picks a meetup window, yet §6.3 stores window/deadline and §6.6 pays `agreed × gratitude`. VERIFY in code: what deadline does a counter-agreed deal actually get, what gratitude multiplier applies, and does "untouched price" at delivery mean the counter-agreed number? If any of these are unset/defaulted, this is a live bug (a deal with no deadline, or a gratitude label promising a bonus the counter path never earns), not just a doc gap. Report findings before changing.

**A7 — "Opening thought is a slight lowball" (§4).**
Nothing in the live flows uses an opening thought — §5 has the player asking first. This is TapeOffer vocabulary leaking into the live-system section; either delete it or explicitly tag it as TapeOffer-only (trap 6's whole point is that a reader must be able to tell them apart).

**A8 — The rent framing is wrong and it poisons balance review.**
§1 and §11 Q6 read as a declining schedule ("$50 day 1, then $30/$20/$10"). The shipped system (rent revamp, Aug 15) is a **one-time haggle that sets the flat daily rate** — 50 → 30 → 20 → 10, never free, arrears linear. A reviewer balancing "$50 due on day 1" is balancing a game that doesn't exist. Rewrite both mentions.

**A9 — Starting cash is never stated.**
For a doc that declares "if it's not here, it doesn't exist," the day-1 economy can't be evaluated. State starting money (and that Tev's 3 free blanks are the only other starting asset, if true).

---

## §B — New open issues (promise/grade class; treat as traps 10–11 after VERIFY)

**B1 — The quote's module basis is an unrecorded contract term.**
`TruePricePerTape` needs `Base = f(activeModules)`, but a want-text fires before any tape is chosen. The Aug 14 fix quotes vs **InstalledCount at quote time** — but the *agreement* doesn't record that number, and no delivery surface shows it. So a player quoted while 4 plugins were installed can deliver a 2-module sketch: a purely **objective** goods gap the contract never named, currently absorbed (silently and confusingly) by the worth clamp. This is the module-count twin of the tier gap §10 fixes and half the reason the clamp felt necessary. Fix inside the same change as §10 and §C1: the agreement records `modulesBasis` (and tier), the ORDER header shows it, and the grader scales on it.
*VERIFY: what InstalledCount was captured vs what the agreement stores today; whether any surface displays it.*

**B2 — Delivery accepts an already-heard tape.**
Both sides "agreed on a CLANG tape," but the buyer plainly meant one they haven't heard. With no memory check on the delivery path, filling an order with a burned song is a silent term violation — same class, deserves its own trap number rather than living implicitly inside trap 6. Fix: run the same `TapeMemory` check at delivery that walk-ups run (and per A5, decide + document whether deliveries write memory). Suggested behavior on a heard tape: refuse with the existing social-failure framing, appointment stays open within its window (the player brought the wrong tape, they didn't no-show) — Sam to confirm that last part.

---

## §C — Design recommendations (Sam has reviewed; confirm each before building)

**Suggested build order: C5 first** — the DealTerms object is the container that C1, C4, B1, and the §10 tier work all land inside. C2 and C3 are independent of it.

**C1 — The contract question (traps 1/2): split `worth` into objective and subjective; the agreed price is sacred against taste.**
- Objective half = `Base × tier` (module count, tier, qty) — a goods spec both parties can verify. Fair to scale on.
- Subjective half = `SatMult × PayFactor` — the buyer's private ear. They commissioned sight-unseen; that risk is theirs.
- Rule: `pay = agreed × min(1, deliveredObjective / contractObjective)`. Drop the satisfaction clamp on agreed prices entirely.
- This is the same shape as §10's "lower tier → half pay," so tier + modules + qty become ONE scaling rule, not three.
- Taste still bites — route disappointment to **bond delta and the next want-text's frequency/quote**, not the payout. Move the "isn't the track I was picturing" line to that path: a colder relationship is legible; a shorted payment reads as the game welching on a written deal.
- Requires: agreement records `modulesBasis` + `tier` (B1/§10); ORDER header shows them; save-schema guard for old saves (as already planned for §10).
- Kills traps 1 and 2 outright: the +15% gratitude pays whenever goods match, because nothing eats it anymore.

**C2 — The anchor question (trap 3): anchor on `Base × SatMult × BondMult` ("street value").**
- Honest: reflects the goods, how much the buyer *visibly enjoyed the listen*, and the relationship — all things the player already knows or just watched. Hides exactly what the game is about: this alien's PayFactor and Patience.
- Key property: `Fair / anchor = PayFactor ∈ [0.55, 3.0]`. A slider from **0.5× to ~4.5× anchor** (log-scaled; 4.5 ≈ 3.0 × 1.45 patience headroom for ceiling-probing) provably has **no unsellable buyer at the floor and no uncollectible superfan at the cap** — trap 3 dies in both directions by construction, not by tuning.
- Band copy switches to population terms: "most would take this" → "street value" → "only a devotee pays this" → "only a superfan pays this." The red zone stops meaning *impossible* and starts meaning *you'd better know who you're talking to* — the 5.4× jackpot becomes a knowledge test (design law 1).
- SatMult leaking through the anchor is intended: let the listen reaction telegraph satisfaction diegetically. Rejections already teach; enjoyment should too.

**C3 — Final offer vs hard bar (trap 6): final offer. Route the panel through `TapeOffer`.**
- Design law 1 argument: the learned buyer model is the content, and ceilings are only learned by probing them. The hard bar taxes probing (lockout, −10 bond, killed appointment), so the optimal player stops experimenting. A final offer converts overreach into the most valuable datum in the game — their ceiling — plus a live decision.
- It is also **Sam's own locked Aug 13 call** ("push too far → FINAL OFFER, deliberately not close to the greedy ask, take it or leave it"), and `TapeOffer` is the only negotiation code with tests (the 113-check suite). Today the tested code is dead and the untested, harsher rule ships. Routing the panel through TapeOffer fixes both, and brings the generosity rule (+3 bond for asking under value) for free — that gives the early game an intentional play: undersell to build bond, harvest BondMult later (design law 2 arc).
- Keep a bar ONLY for pushing past a declared final offer — insult after clarity.
- If any TapeOffer rules genuinely conflict with post-Aug-13 decisions (e.g. the delivery already-heard check now interacts with B2), report the diff to Sam before wiring, per the doc's own trap-6 note.

**C4 — Closing the goods-ambiguity class (§10 + Q4): the full DealTerms field set.**
An agreement/contract must carry, and every surface must show: `genre` (blend-aware via the shared MatchesFavourite helper — also closes trap 5), `qty`, `tier`, `modulesBasis` (B1), `pricePerTape`, `window + grace`, `fresh` (not-already-heard, B2). One decision to take now while qty is always 1: **partial deliveries pro-rate** under the C1 objective rule rather than binary-failing — free to decide today, expensive to retrofit. The +60s grace stays undocumented-to-the-player (invisible asymmetry in the player's favour never breaks trust).

**C5 — Structural prevention (Q5): one object, two hard rules, one property test.**
- **Rule 1: UI never calls `TapeValue`/`TapeTrade`.** Money reaches any surface only as fields of `DealTerms` (or a `Quote` struct produced by one pure function). Want-texts, ORDER headers, chips, slider seeds, band copy — all render projections of the same object.
- **Rule 2: the grader's signature is `Grade(DealTerms, deliveredTape)`** — nothing else. No re-derivation of price at delivery time.
- **The parity test** (fits the existing headless python-runner setup): for randomized tastes × terms, delivering exactly-promised goods at an untouched ask must pay **exactly** the number every surface displayed. All three previously-shipped promise/grade bugs plus traps 1/2/3/8 are the same event — two call sites computing money independently. The object makes the second computation impossible rather than discouraged; the test makes regressions loud.
- Suggested check while building: grep for any UI-side `TapeValue`/`TapeTrade` call sites and list them for Sam before rerouting.

**C6 — Balance (Q6): no numeric rebalance needed; two doc/design actions.**
The §3/§4 numbers hang together under the *actual* rent system (A8). At the haggled $10/day: ~3 cheap sales + blank restock covers day 1 comfortably. At face-value $50/day (player never haggles): 2 plugins → Base $14, majority buyer ~$8.5, blank $5 → ~$3.5 margin → ~14 clean sales in a 24-minute day against ~10 alive buyers with memory blocking repeats, no text channel yet, fronting vaulted — uncoverable. Actions: (1) fix the doc per A8/A9; (2) Sam to decide whether a player who never realizes the rent haggle exists deserves the $50/day consequence or a nudge (dialogue hint, whiteboard line). Side observations, no change requested: blanks are ~59% of a cheap sale's revenue, so the early game is a blank-cost game as much as a taste game — reads as law 2's "easy but cheap" working as intended; the $40–$95 superfan top end vs $10 rent is a proper jackpot, contingent on C2 landing. The coin-flip band is wide for tolerant buyers (liked to distance ~7.7, flip to ~11 at Falloff 0.95), so day-1 rejection isn't the bottleneck — price × sale count is. Correct shape.

---

## Checklist (suggested order)

1. [ ] §A doc edits (A1–A4, A7, A8, A9) — no code risk
2. [ ] VERIFY pass, report back before changing: A5 (delivery memory writes?), A6 (counter-path window/gratitude), B1 (what the agreement stores vs InstalledCount), C5 grep (UI call sites into TapeValue/TapeTrade)
3. [ ] Sam confirms/strikes each §C item
4. [ ] C5 DealTerms object + parity test
5. [ ] C1 + C4 + B1 + §10 tier work as one change (they're one rule), save-schema guard included
6. [ ] B2 delivery freshness check (behavior per Sam's call on the appointment staying open)
7. [ ] C2 walk-up anchor + slider + band copy
8. [ ] C3 route panel through TapeOffer (diff report first)
9. [ ] Re-run headless suites + the new parity test; note trap 4 (co-op guest sale reporting) is untouched by all of the above and still needs its own fix
