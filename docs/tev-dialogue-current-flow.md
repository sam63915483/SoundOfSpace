# Tev — conversation flow (Part 3 report, 2026-08-10)

> **STATUS: Sam's first round of edits is APPLIED to the scene** (TEV object at
> `--- Celestial ---/Body Simulation/Humble Abode/TEV`). The lines below are the
> CURRENT shipping text. Applied: FT-2 rewritten (the lonely read), FT-5 reworded
> to "shrooms" + a rent-conditional variant, RENT-C2 "mind you", TEACH-2 "caps"
> → "shrooms". `Complete`-stage DONE-1/2/3 are still live but are slated for
> replacement by the fronting pitch in Part 4.


Everything Tev currently says, as a tree. **Edit this file directly** — cut lines, rewrite
them, reorder them — and hand it back; I'll apply it to the scene. Every line has a stable
ID (`FT-1`, `RENT-D2`, …) so you can also just say "cut RID-2, reword TEACH-3".

---

## ⚠️ Read this before editing — where the lines actually live

Two components sit on the `TEV` GameObject:

| Component | State | What it does |
|---|---|---|
| `TevMushroomOnboarding` | **LIVE** — this is the whole Aug 4 flow below | Fronts 3 caps, rent haggle, return conditionals, teaches the loop |
| `TevDialogue` | **DORMANT** | The old Mission 1 tree (vendors nudge, pilot fork, Cold Company briefing). `suppressMissionDialogue: 1` + `restoreMissionDialogue: 0` = it is disabled on first frame and never re-enabled. ~670 lines of authored dialogue that currently cannot be reached. |

**The gotcha:** every `[TextArea]` line array is *already serialized on the scene's TEV
object*. Editing the C# initializers in `TevMushroomOnboarding.cs` changes **nothing that
ships** — Unity keeps the scene's copy. Right now the scene values and the C# defaults are
byte-identical, so nothing has drifted, but any edit has to land in the scene (Inspector, or
me editing the scene YAML). I'll do it in the scene.

**Also:** some lines are `[TextArea]` fields (Inspector-editable, marked **⬛ serialized**)
and some are hardcoded string literals in the coroutine (**🔒 hardcoded** — I have to touch
C# to change those). The two questions in the return talk and all four answer rows are
hardcoded.

---

## 0. Before he'll talk at all

Tev's renderers *and colliders* are switched off until one of these is true:

- The onboarding has already started (`stage != NotMet`) — then he's permanently visible
- This boot is a save-load
- The shuttle exit ramp has been down for **120 s** (`hiddenSeconds`)
- **180 s** since the component woke (`fallbackSeconds`, backstop for Editor Play / dev spawns)

Then: within **8 m** (`talkRadius`), looking at him, press Interact.

---

## 1. STAGE `NotMet` — the first talk

### 1a. Opener (⬛ `firstTalkLines[0]`)

- **FT-1** — "Most people knock, y'know. You parked a shuttle on my lawn."

### 1b. → THE RENT HAGGLE (inserted here by `rentAfterLineIndex: 1`; skipped if already settled)

**Demand** (⬛ `rentDemandLines`) — `{rent}` is substituted live from `rentHighPerWeek: 500`:

- **RENT-D1** — "Course, a berth like that isn't free. Prime lawn, southern exposure."
- **RENT-D2** — "{rent} a week and we'll say no more about it."

**Choice 1** (🔒 hardcoded rows, both always selectable):

- ▸ **"Fine. 500 a week."** → rent = 500/wk → **RENT-AH1** — "Well now. A man who pays his way. Don't see many of those out here." → *haggle ends*
- ▸ **"Not a chance."** → climbdown ↓

**Climbdown** (⬛ `rentClimbdownLines`) — `{rent}` = `rentLowPerWeek: 100`:

- **RENT-C1** — "Ha! Alright, alright — I was messing with ya."
- **RENT-C2** — "{rent} a week. That's me being generous, mind you."

**Choice 2** (🔒 hardcoded):

- ▸ **"100 a week. Done."** → rent = 100/wk → **RENT-AL1** — "There we go. Neighbourly." → *haggle ends*
- ▸ **"Still no."** → rent = **0 (free)** ↓
  - **RENT-W1** — "You drive a hard bargain for a man with nothing in his pockets."
  - **RENT-W2** — "I'm busting your balls. Park it wherever. Costs me nothing."

> Settling writes `RentPerWeek`, sets `RentSettled`, and schedules the first bill **one full
> galactic week out** so you're never charged on landing day. `TevRentCollector` bills weekly
> after that, accrues arrears if you can't pay, and never evicts.

### 1c. Rest of the opener (⬛ `firstTalkLines[1..5]`)

- **FT-2** — "Truth be told I've been hoping someone'd land on it. Nothing much happens out here worth being sore about."
- **FT-3** — "Fresh off the pod, then. No money, no plan, and a suit that'll want feeding."
- **FT-4** — "Lucky for you there's exactly one business worth being in around here."
- **FT-5** — *rent-conditional, one of two:*
  - *(rent waived)* "Three shrooms, on the house. Find a buyer — everyone's got different prices and preferences, so don't be afraid to shop around."
  - *(paying any rent)* "To help make money for rent, take these three shrooms, on the house. Find a buyer — everyone's got different prices and preferences, so don't be afraid to shop around."
- **FT-6** — "And hey. Don't eat the inventory."

### 1d. Hand over 3 caps

`GrantBatch()` → 3 × `MushroomRegistry.KeyForSeed(0)` into the hotbar. Then stage → `Given`.

**If the pack is full**, nothing is granted, stage stays `NotMet`, and the *whole beat replays*
next talk (rent is skipped the second time, so he can't double-charge):

- **PF-1** (⬛ `packFullLines`) — "Your hands are full, friend. Make some room and come see me again."

---

## 2. STAGE `Given` — every talk after, until he's done

Two questions with **greyed-out rows** — both options always *shown*, only the true one
clickable. Seeing the greyed row is how you learn he can tell you're lying.

**Q1** (🔒 hardcoded) — "So — did you sell any?"
- ▸ "Yeah, I sold some." — *enabled only if `SoldCount >= 1`*
- ▸ "No, not yet." — always enabled

**Q2** (🔒 hardcoded) — "Got any left?"
- ▸ "Yeah, still got some on me." — *enabled only if holding ≥ 1*
- ▸ "Nope. All gone." — *enabled only if holding 0*

Outcomes branch on the **truth** (live hotbar + sale counter), not on which row was clicked.

### Outcome A — still holding some (⬛ `stillHoldingLines`) · stage unchanged

- **HOLD-1** — "Then get back out there. It's about the only thing folks still spend on —"
- **HOLD-2** — "nobody's saving for a future with that thing hanging up there."

### Outcome B — sold ≥ 1, holding none (⬛ `teachLines`) · **stage → Complete**

- **TEACH-1** — "Not bad. You've got a buyer and you've got a price. That's a business."
- **TEACH-2** — "Alright — trade secret. Those shrooms grow wild around here, and they like oxygen."
- **TEACH-3** — "More trees, richer air, faster shrooms. You want product? Start planting."
- **TEACH-4** — "Chop one and you'll get spores off it. Put them in the ground and the same cap comes back."

### Outcome C — sold none, holding none (ate them, or stashed them in the locker)

Ridicule (⬛ `ridiculeLines`):

- **RID-1** — "Wait. All three? Gone?"
- **RID-2** — "I hand you the easiest money in the system and you come back with lint."
- **RID-3** — "…Lesson one: never eat the product."

Then, if he's fronted fewer than **5** extra batches (`MaxRefronts`) — refront (⬛ `refrontLines`):

- **REF-1** — "Here. Three more. I must be getting soft."
- **REF-2** — "Sell them this time. I'm counting."

Otherwise he's out of patience (⬛ `outOfPatienceLines`) · **stage → Complete**:

- **OOP-1** — "No. I'm done handing you groceries."
- **OOP-2** — "They grow wild out there — go find your own. Take the axe to them, they come apart easy enough."

> **The intentional exploit:** the "holding" check reads the **hotbar only**, never the
> shuttle locker. Stash his caps in the locker, tell him they're gone, get 3 more — five
> times, then the tap shuts off. Documented as deliberate in `MushroomQuest.cs`.

---

## 3. STAGE `Complete` — one random line per talk (⬛ `doneLines`)

- **DONE-1** — "Still at it? Good. Keep an eye on who's paying what."
- **DONE-2** — "Plant more than you pick. That's the whole trick."
- **DONE-3** — "Quiet day. Quiet year, really."

---

## Decisions (Sam, 2026-08-10)

1. **DONE-1/2/3 are vaulted.** The `Complete` stage becomes the fronting pitch and the start
   of the dealer loop. Nothing else about the onboarding changes.
2. **The refront ladder stays exactly as-is.** The whole current onboarding is the first,
   free sale and is not being touched — the fronting loop is new functionality bolted on
   *after* `Complete`.
3. **Both paths to `Complete` get the pitch**, including the out-of-patience one. Sam: "the
   fail isn't a fail, it's more of a swindle — you take advantage of his kindness and he gets
   pissy and stops giving you shrooms." He's a dealer; a broke customer is still a customer.
4. **`TevDialogue` (the dormant Mission 1 tree) stays vaulted.** Sam will ask for it back if
   and when he wants it. ~670 lines of authored dialogue, unreachable by design.
