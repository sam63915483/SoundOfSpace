# Messages App + Repeat-Buyer System — Design

**Date:** 2026-08-07 · **Branch:** feat/helmet-hud · **Status:** approved by Sam (this doc records the ratified design)

## Goal

Turn the phone's AI app into a **Messages app** and wire it into the mushroom
economy so demand comes to the player instead of only being found by wandering.
Completed sales convert buyers into **regulars** who text when they want more,
deals get negotiated in the thread (ask → accept/counter/decline, with
counter-backs), scheduled with real-minute windows (~5/~10/~15) instead of
Schedule 1's time-of-day, and fulfilled in person. Repeat business also
**teaches**: every completed deal reveals one of the buyer's hidden wants, and a
per-buyer **bond stat** makes maintained relationships literally pay better.

Design intent (Sam): the loop becomes self-preserving — the more you sell, the
more regulars text you, the more deals you can run, and the phone becomes your
memory of who wants what.

## Existing foundations (do not rebuild)

- **Hidden buyer traits** — all hash-derived from `AlienIdentity.Of()`, nothing
  stored (`NPCMushroomPrice.cs`): rate multiplier 0.75–1.35×, patience
  1.12–1.40×, favourite tier (×1.35), disliked tier (×0.72), appetite 6–24 caps.
- **Session deal state** (`MushroomDealState.cs`): appetite used/refill
  (10 min), barred timer, remembered counters, last paid/qty, tiers sold.
  Stays session-only; this feature does NOT move it into the save.
- **Sell panel** (`MushroomSellUI.cs`): in-person haggle, memo line, `onSold`
  callback with qty (via `NPCSellRows`).
- **Identity + names** (`AlienNames.cs`): stable per-buyer identity
  (`cell:slot:cellId` / `scene:name`) and derived display name.
- **Phone** (`PlayerPhoneUI.cs`): app grid, notification strip, unread badge on
  the AI tile; `AIChatScreen.cs` bubble/scroll/typing patterns to reuse.

## 1. BuyerLedger — the new persistent store

New static class `BuyerLedger` (Vendor/), **saved** (unlike MushroomDealState).
Per buyer identity:

| Field | Meaning |
|---|---|
| `dealsCompleted` | drives the hidden-want reveal schedule |
| `bond` | 0–100 relationship stat |
| `isRegular` | true once the conversion roll succeeds — they text you |
| `pendingAppointment` | agreed tier, qty, pricePerCap, windowSeconds, deadline (absolute, unscaled-clock-relative), state |
| `eventLog` | compact list of thread events (see §6) |

**Save schema:** parallel lists in `SaveData` (JsonUtility — no dicts):
identities + one list per scalar field, plus a flattened event list keyed by
buyer index. Capture/apply in `SaveCollector` at the **singleton step** of the
apply order. Reset in `NewGameReset.Apply()` (static state leaks across the
main menu otherwise — CLAUDE.md rule). Event log is capped (last ~40 events per
buyer) so saves stay bounded.

Appointment deadlines are saved as *seconds remaining*, re-anchored to
`Time.unscaledTime` on load (unscaledTime restarts per session).

## 2. Bond (relationship)

0–100, starts 0, clamped.

**Gains** (per completed in-person deal, scheduled or walk-up):
- +8 base
- +4 extra if it fulfilled a kept appointment (arrived in window, deal closed)
- +4 extra if the deal included their favourite tier
- Substituted fulfilment (§5c): all gains **halved**

**Losses:**
- Missed appointment: **bond is halved** (Sam's spec) + negative text
- Failed message counter (they refuse outright) or pushing past their
  in-person counter: **−10**
- Substitution refused at meetup: **−5** (showed up, but wasted their time)

**Effect on price:** multiplier `1 + 0.15 × (bond/100)` applied inside
`NPCMushroomPrice.PriceFor()` — up to **+15% at bond 100**. Stacks with the
gratitude bump, so a maxed regular on a 5-minute promise pays ~+30% over base.
Never printed as a number.

**Display:** 5 pips (each pip = 20 bond) next to the buyer's name in the
Messages index **and** in the sell panel header. Pips are the only readout.

## 3. Becoming a regular

Rolled once per completed deal with a non-regular:
- **Guaranteed** if the deal included their favourite tier
- otherwise **1/3 chance**

On success: no immediate fanfare. When their appetite next refills
(`MushroomDealState.SecondsUntilHungry` → 0), they send their first text and
appear in the Messages index. Regular status is permanent (bond can crater, but
they keep texting — the punishment for neglect is the halved bond, not
silence).

## 4. The want-text and negotiation

**Trigger:** a lightweight ticker (new auto-singleton `BuyerMessageDirector`,
seeded in `EnsureGameplaySingletons` per trap #1) polls regulars' appetite
state. When a regular refills and has no open conversation, they text after a
small random delay. **At most 3 open want-texts at once**; extras queue.
After a save load, appetite resets to hungry for everyone, so first texts are
staggered over ~3 minutes to avoid a message storm.

**The ask** — generated entirely from their existing hidden traits:
- **Tier:** usually their favourite (~70%), otherwise their neutral tier;
  never their disliked tier.
- **Quantity:** 50–100% of `AppetiteMax`.
- **Opening offer:** ~90% of their true `PriceFor` for that tier (including
  bond bonus) — they lowball slightly, which is what countering is for.

Example: *"Vorn: after 8 rare caps. I'll do 84 a cap if you can get here."*

**Reply chips (one exchange, then settled):**
1. **Accept** → pick window: **~5 / ~10 / ~15 min** → appointment set.
2. **Counter (price per cap)** → resolved against their true price × patience:
   - counter ≤ true × patience → **accepted** ("fine, but don't be late")
   - ≤ true × patience × 1.25 → **counter-back**: they come back with a price
     between their opening offer and your ask (clamped to their patience
     ceiling). You may Accept their counter-back or Decline (deal off, no bond
     ding — a polite stand-off).
   - beyond that (outrageous) → **refused outright**, deal off, **−10 bond**.
   - One counter each, no loops.
3. **Not now** → free; they text again next refill.

**Gratitude bump by promised window:** 5 min → **+15%**, 10 min → **+10%**,
15 min → **+5%** on the agreed per-cap price, paid only if fulfilled in window.

## 5. The meetup

**a. Waiting.** The buyer holds at their home spot (their spawn cell —
streamed aliens are cell-anchored, so "where you met them" is always findable)
for exactly the promised window plus a 60 s grace. The thread shows a live
distance/direction line while an appointment is pending.

**b. Exact fulfilment.** Interact with them in-window → sell panel opens in
**scheduled-deal mode**: the order pinned at the top (tier, qty, agreed price
incl. gratitude bump), player loads caps, confirm, paid. Full bond gains.

**c. Substitution (Sam's fuzzy-fulfilment rule).** Showing up with different
caps than agreed still lets you try. Acceptance is a single roll:

```
chance = 1.0
       − 0.5 × tiersBelowAgreed          (per tier below)
       + 0.25 × tiersAboveAgreed         (per tier above)
       + 0.3 × max(0, qtyRatio − 1)      (extra quantity sweetens)
       − 0.4 × max(0, 1 − qtyRatio)      (shortfall sours)
clamped to [0.05, 1.0]
```

Calibration (agreed 3 rare): 3 uncommon → 50%, 5 uncommon → 70%,
2 rare → ~87%, 5 common → 20%, any tier up → ~guaranteed.

- **Accepted:** transaction at their standard `PriceFor(offeredSpecies)` —
  NOT the agreed price, and no gratitude bump. Bond gains **halved**.
- **Refused:** −5 bond, appointment closed, disappointed text. One roll per
  appointment.

**d. Missed.** Window + grace lapses with no completed deal → bond **halved**,
appointment closed, negative text (*"waited 20 minutes. don't bother next
time."*). Showing up but failing a substitution roll is a −5, not a miss.

## 6. Hidden-want reveals

Each completed deal (any kind) reveals one want, in fixed order, worded and
never numeric (Sam's standing rule: raw multipliers are never printed):

| Deal # | Reveal | Example wording |
|---|---|---|
| 1 | appetite | "takes about 20 caps before they're full" |
| 2 | favourite tier | "keen on rare" |
| 3 | disliked tier | "won't pay much for common" |
| 4 | rate | "pays generously" / "average payer" / "a bit stingy" |
| 5 | patience | "doesn't mind a cheeky ask" / "walks fast if you push" |

Shown in the buyer's **contact card** (tap their name in the thread) and
echoed on the sell panel memo line. Derived from `dealsCompleted` — nothing
extra saved. The existing `HasSoldTier` taste-note gate is superseded by this
schedule (memo line reads from the ledger instead).

Rate/patience wordings are 3-bucket / 2-bucket cuts of the hidden values.

## 7. Messages app UI

The AI tile becomes **MESSAGES** (same slot, unread badge preserved).

**Index page:** one row per contact — name, bond pips, unread dot,
last-message preview, relative timestamp. Sorted most-recent-first. **Pinned
guide thread on top**: currently HAL, later renamed/reskinned to **Frump**
(the player's guide/boss) — the pin is generic under the hood so that swap is
content, not code. Frump's future messages/videos post into this thread.

**Thread view:** bubbles with timestamps (buyer left, player right), reply
chips docked at the bottom when a decision is open (Accept / Counter / Not
now; window pick; counter-back accept/decline), pending-appointment card with
live distance, contact-card access from the header. Reuses the phone's visual
language and AIChatScreen's proven patterns (sticky-at-bottom scroll, wrapped
bubble sizing, typing guard for any input). Message text is rendered from the
**event log** via templates (several variants per event, salted by buyer
identity) — saves store events, not strings, so wording can improve later
without stale saves. No LLM anywhere.

**HAL/guide thread:** the existing AI chat (preset dialogue presenter) is
mounted as the pinned thread's content. `AIChatScreen`'s HAL-specific chrome
stays; buyer threads are the new, cleaner `MessagesScreen` implementation.

**Notifications:** incoming texts use the existing phone notification strip +
tile unread badge (`NotifyOnPhone` path), with a short SFX.

## 8. Sell panel changes

- Bond pips next to the buyer's name in the header.
- Memo line sources earned notes from the ledger's reveal schedule (§6).
- **Scheduled-deal mode**: order summary pinned, agreed price locked (no
  haggle stage), substitution roll on confirm when the offer differs.
- `RecordSale` path additionally reports the deal to `BuyerLedger`
  (deal count, bond gains, conversion roll, appointment resolution).

## 9. Edge cases & guards

- **Buyer despawned at meetup:** cells are deterministic; walking into range
  restreams them. The appointment stores the cell's body + position for the
  distance line so it works while they're unstreamed.
- **Scene reload / backrooms trip mid-appointment:** deadlines are wall-clock
  (unscaled) within a session and re-anchored on save load; a trip that
  overruns the window is a miss — that's the pressure working as intended.
- **Player pushes past counter in person while an appointment is open:** the
  5-min barred state cancels the appointment, but *without* the missed-
  appointment halving — the −10 for pushing past the counter already landed,
  and double-punishing one mistake reads as a bug.
- **Fixed scene NPCs (non-streamed buyers):** same systems apply; their home
  spot is their scene position.
- **New Game:** `BuyerLedger.ResetAll()` from `NewGameReset.Apply()`.
- **Tev/Kolb story NPCs:** excluded from regular conversion (story dialogue
  owns their threads later if ever needed).

## 10. Testing

Editor-driven (no CLI tests in this project): cheat hooks under
`Universe.cheatsEnabled` — force a regular conversion, force-refill appetite,
fast-forward an appointment deadline — so the whole loop can be exercised in
minutes instead of tens of minutes. Compile-check via Console; play-test
script: walk-up sale → conversion → first text → counter → counter-back →
schedule 5 min → exact fulfil → verify bond pips/reveals → miss one → verify
halving + negative text → save/load mid-appointment.
