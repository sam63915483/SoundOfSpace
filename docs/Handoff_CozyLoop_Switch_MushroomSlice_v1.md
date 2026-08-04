# HANDOFF — Direction Switch + First Slice: Mushroom Economy Onboarding
Date: Aug 4, 2026
Status: **IMPLEMENTED 2026-08-04** — see `docs/MUSHROOM_ECONOMY.md` for what was
built, the decisions taken, and the play-test checklist. This file is kept as the
original brief. Direction change (§0) still stands.

**Sam's answers to §5, all implemented:**
1. Ate-all-3 → Tev ridicules you and **fronts another 3**, up to **5 extra
   batches**, then he passes and tells you to find wild ones. Stashing his caps
   in the shuttle locker and claiming you lost them works — that gap is
   **intentional**, and the 5-batch ceiling is what bounds it.
2. First buyer: **every NPC**. Space dust selling is **vaulted**
   (`FeatureVault.SpaceDustSelling`) — dust is still collectable and still in the
   hotbar, NPCs just don't buy it. Mushroom price is **per-alien and stable**,
   ~20 credits (12–29 band), Schedule 1 style.
3. Price: ~20 credits/mushroom.
4. Eat effect: kept (now derived per species — see the economy doc for why).
5. Squish SFX: generated placeholders, wired to `MushroomSpawner`. Mushrooms
   wobble harder and looser than trees on every hit.

**Plus one addition beyond this brief:** breaking a mushroom drops **0–2
mushroom saplings** ("spores") of that species, plantable like tree saplings —
the same mushroom grows back.

---

## 0. DIRECTION CHANGE — read before anything else

- The game is refocusing on a single core loop: **harvest/grow mushrooms → sell → upgrade → expand** (Schedule 1-style progression). Cultivation ties in later: trees raise oxygen, oxygen raises mushroom growth speed and yield (that link is a LATER phase — do not build it in this slice).
- All mission/story content (notes loop, B-1, endgame, liminal rooms) is **ON HOLD**. Do not extend it, do not build toward it, do not delete it.
- `GDD_StoryBible_v2.md` remains the reference for tone, world, and character voice. Where its roadmap conflicts with this document, **this document wins**.
- Per Bible §0 rule 4: **state your full build plan for this handoff and wait for Sam's confirmation before implementing anything.**

---

## 1. UNCHANGED — do not touch [EXISTS]

- Pod/shuttle intro sequence (HAL lines, orientation film, door-unlock flow). Sam will revise it himself later.
- Shuttle as safe zone: locker storage + stasis pod save station.
- World layout: shuttle lands beside Tev's cabin; village is a 3–4 min walk away.
- Tree chopping (physics axe), log drops (spin/bob, walk-over pickup), floating right-hand holdable system, fish-style "item looks like the thing you caught" representation, locker storage, dialogue system (StoryDirector + authored nodes), existing sell interaction (space dust).

[TEST] Verify the locker contains the **axe** and **water bottle** on first open. If not, add them to the starting loadout.

---

## 2. TEV ONBOARDING REWORK

### 2.1 Timing & visibility [BUILD]
- Tev's current on-landing behavior (waving outside, plus any dialogue tied to old/removed content) is **deprecated**. Disable it — don't delete assets.
- Tev is **hidden for 120 seconds** starting the moment the shuttle exit door unlocks/opens. This window lets the player loot the locker and chop trees with no NPC pressure. No forced tutorial.
- At T+120s, Tev appears outside his cabin, idle, interactable. (If the player is still inside the shuttle at T+120s, he appears anyway.)
- First dialogue triggers on player interact.

### 2.2 Quest state [BUILD] [INTEGRATE]
Track in StoryDirector:
- `stage`: NotMet → GivenMushrooms → Complete
- `tevSoldCount` (0–3): how many of Tev's mushrooms the player has sold
- `heldCount`: live inventory query for the given species
- (eaten count is derivable: 3 − sold − held)

On first talk: give the player **3 mushrooms of one species** as inventory items (same item type as chopped drops, §3 — normal stacking rules apply).

### 2.3 Dialogue tree [AUTHOR] — draft all lines for Sam's review; suggested lines below are starting points, Tev's voice per the Bible (dry, shady, busted-license pilot)

**First talk (stage NotMet → GivenMushrooms):**
1. Opener about the landing. Suggested:
   - A: "Most people knock, y'know. You parked a shuttle on my lawn."
   - B: "Hell of a landing. Real gentle. That used to be my front yard."
2. Sizes the player up → segue to the hustle. Suggested beat: fresh off the pod, no money, no plan → "around here there's exactly one business worth being in."
3. Hands over 3 mushrooms. Suggested: "Three caps, on the house. Find a buyer. And hey — don't eat the inventory."

**Return talk (stage GivenMushrooms, any state):**
Two conditional questions with greyed-out options. Greyed = **visible but unselectable, dimmed**. If the dialogue UI doesn't support disabled options yet, build that capability. [BUILD if missing]

- Q1 — "So — did you sell any?"
  - [Yes] enabled iff `tevSoldCount ≥ 1`, else greyed
  - [No] always enabled
- Q2 — "Got any left?"
  - [Yes] enabled iff `heldCount ≥ 1`, else greyed
  - [No] enabled iff `heldCount == 0`, else greyed
  - (Exactly one of these is ever selectable; always show both.)

**Outcomes:**
- **A) `heldCount ≥ 1`** → Tev sends the player back out. Suggested line (replaces Sam's placeholder — he wants it worded well):
  - A: "Then get back out there. It's about the only thing folks still spend on — nobody's saving for a future with that thing hanging up there."
  - B: "Sell 'em, man. That's the one thing people around here still buy. Hard to blame them — look up."
  - Stage stays GivenMushrooms.
- **B) `heldCount == 0` and `tevSoldCount ≥ 1`** → advance to Complete. Tev teaches the loop. Suggested: "Not bad. Alright, trade secret — those caps grow wild around here, and they like oxygen. More trees, richer air, faster shrooms. You want product? Start planting."
- **C) `heldCount == 0` and `tevSoldCount == 0` (ate all three)** → ridicule dialogue. Suggested: "Wait. You ate them? All three? I hand you the easiest money in the system and you had it as a snack. …Lesson one: never eat the product." — then proceed to the same teach line and advance to Complete. **[OPEN — Sam confirm: does the ate-everything path still advance like this, or should Tev front a second batch so a real sale must happen before advancing?]**

[OPEN] **First buyer:** who buys these 3 mushrooms? Confirm an NPC with a working sell interaction exists within reach of spawn (villager? the existing dust-buying alien?). The slice is uncompletable without one — if none exists near spawn, flag it in the build plan.

---

## 3. MUSHROOM HARVEST REWORK [BUILD] — the big one

Current behavior: world mushrooms are eaten instantly on interact. That is replaced entirely. Mushrooms become **harvest nodes that mirror trees end-to-end**:

- Chopped with the axe. Break threshold = **50% of a tree's** (half the chop effort).
- Hit feedback: a weird **squish** sound per hit (distinct from wood). Use any placeholder squish for now — Sam will supply/record the final sound. [OPEN: placeholder OK?]
- On break: the mushroom **topples like a felled tree, then shrinks away**.
- Drops **3–9** (random) pickup items that **spin and bob on the ground exactly like wood logs**, collected by walking over them.
- **Species likeness everywhere**: the ground pickup, the hotbar icon, and the held model must all visually match the specific mushroom that was chopped — same approach as caught fish.
- Held mushrooms use the floating right-hand holdable system like other hotbar items.
- **Stacking: 20 per stack, species-pure.** Different species never merge into one stack.
- Mushrooms move in and out of **locker storage** like any other item.
- **Eating stays possible** — now as a hotbar/held use action instead of instant world-consume. Keep the current eat effect values. [OPEN: confirm current effect carries over unchanged]

[INTEGRATE] Tev's 3 gifted mushrooms are this same item type/species system — no special-case item.
[INTEGRATE] Hook mushrooms into the existing sell flow used for space dust. Price: TBD by Sam. [OPEN]

---

## 4. TEST CHECKLIST [TEST]

- Shuttle door opens → Tev absent for exactly 120s → appears at his cabin; old waving/old dialogue never triggers.
- Axe + water bottle lootable and usable before Tev appears; trees choppable in the window.
- Mushroom chop: ~half a tree's effort, squish audio on hit, topple + shrink on break, 3–9 species-matched drops, spin/bob, walk-over pickup.
- Stacks cap at 20; two different species never merge; locker in/out works; held + hotbar visuals match species.
- Eating a held mushroom works and applies the existing effect.
- Dialogue: every Q1/Q2 grey-state correct across all combinations of sold/held; outcome A on any held > 0; outcome B (advance + oxygen teach) only at held 0 + sold ≥ 1; outcome C (ridicule) at held 0 + sold 0.
- Selling a mushroom to the buyer increments `tevSoldCount` and removes the item.

---

## 5. OPEN ITEMS SUMMARY [OPEN]

1. Ate-all-3 path: ridicule → still advance, or second batch first? (§2.3 C)
2. First buyer near spawn — who, and does their sell interaction exist? (§2.3)
3. Mushroom sale price. (§3)
4. Eat effect: keep current values as-is? (§3)
5. Squish SFX: placeholder acceptable until Sam records the real one? (§3)

Remember rule 4: post the build plan first. Wait for Sam.
