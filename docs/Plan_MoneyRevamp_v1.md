# Money & Prices Revamp — proposal v1

**Status:** proposal, nothing implemented. Sam reads, approves or redirects.
**Date:** 2026-08-14
**Measured with:** `prototypes/shuttle-computer/test/verify-diagnostic.py` (500 aliens)

---

## The read

Sam's instinct is right and it measures. Three separate things make the money
feel inflated, and they are not the same problem.

**1. One action pays two digits.** A single tape — two minutes at the computer —
sells for $31 to $69. A blank costs $10. That is a 3x to 7x return on the only
consumable in the loop, every single time, with no failure case that costs money.

**2. The prices do not share a unit.** $10 blank, $30 water bottle, $50 rod, $100
fish bag, $200 plugin, $2000 ship. The ratios are 1 : 3 : 5 : 10 : 20 : 200 and
they do not map onto anything the player can feel. Nothing tells you what a
plugin is *worth* except its own number, so the number is arbitrary.

**3. Fishing out-earns the game's core loop.** A 50 lb rare fish is $150 — more
than double the best tape in the game. The side activity pays better than the
thing the whole pivot is about.

---

## The principle

Pick one unit and price everything against it. **The unit is a tape.**

A mid-game tape (4 modules, Type 1) sells for about $20. Every other price then
answers "how many tapes is this", and the numbers below are chosen so that the
answer is memorable rather than arbitrary.

Second rule: **one four-digit price in the whole game** — the ship. Everything
else fits in two or three digits, so the ship reads as the ceiling without
anything having to explain that it is.

---

## The formula

`Assets/3 - Scripts/Music/TapeValue.cs`

| constant | now | proposed | why |
|---|---|---|---|
| `Floor` | 10.0 | **3.0** | The "just for existing" money. At 10, a one-voice tape was already worth a blank and a half before anyone heard it. |
| `PerModule` | 8.0 | **4.0** | Halved with the floor, so arrangement stays the dominant term. |
| `TierTwoMult` | 1.5 | **1.8** | Type 2 costs 3x a Type 1 blank in the new prices, so it has to pay more than 1.5x back. |
| `SatFloor` | 0.4 | **0.35** | Widens the gap between a careless tape and a good one. |
| `SatRange` | 0.9 | **0.95** | Same. |
| `RequestBonus` | 1.25 | 1.25 | Unchanged — this is the commission premium, fixed last commit. |
| `BondMult` | 1.0–1.4 | unchanged | A ratio, so it rescales for free. |

---

## What a tape sells for

Measured across 500 aliens at satisfaction 74 (a decent hand-made tape),
Type 1, no bond.

| modules owned | now, in person | **proposed** | now, text order | **proposed order** |
|---|---|---|---|---|
| 2 (start) | $31 | **$13** | $43 | **$18** |
| 3 | $41 | **$18** | $56 | **$25** |
| 4 | $50 | **$22** | $69 | **$31** |
| 5 | $60 | **$27** | $82 | **$38** |
| 6 (maxed) | $69 | **$32** | $95 | **$44** |

Type 2 multiplies those by 1.8 — a maxed Type 2 tape is **$57** in person.

---

## The price list

| item | now | proposed | in tapes |
|---|---|---|---|
| Blank Tape Type 1 | $10 | **$5** | ¼ |
| Ship licence test | $20 | **$10** | ½ |
| Blank Tape Type 2 | $20 | **$15** | ¾ |
| Water bottle | $30 | **$15** | ¾ |
| Fishing rod | $50 | **$25** | 1¼ |
| Fish bag | $100 | **$50** | 2½ |
| Plugin — 1st | $200 | **$60** | 3 |
| Plugin — 2nd | $200 | **$90** | 4½ |
| Plugin — 3rd | $200 | **$130** | 6½ |
| Plugin — 4th | $200 | **$180** | 9 |
| Smuggling fine | $200 | **$80** | 4 |
| Smuggling payout | $500 | **$200** | 10 |
| Rent per week (currently waived) | $500 / $100 | **$150 / $40** | |
| **The ship** | $2000 | **$1000** | 50 |

Dev conveniences follow the same scale so testing feels like playing:
cheat code +$500 → **+$100**, `GravityDebugUI.debugMoneyAmount` 2000 → **500**.

### Fishing

`FishInventory.GetValue()` is `weight x (1 | 2 | 3)` by rarity. Proposed:
divide by three — `max(1, round(weight * mult / 3))`.

A 50 lb rare goes from **$150 to $50**: still a good haul, roughly one maxed
Type 2 tape, and no longer better than the loop the game is built around.

---

## Why a plugin ladder instead of four flat prices

Sam suggested $100 a plugin. A rising ladder is better, and here is the reason:

| plugin | price | your net per tape when you can first buy it | tapes to afford |
|---|---|---|---|
| 1st | $60 | $8 | **8** |
| 2nd | $90 | $13 | **7** |
| 3rd | $130 | $17 | **8** |
| 4th | $180 | $22 | **8** |

**Every plugin costs about eight tapes, all game long.** The ladder rises in step
with the income it unlocks, so the pace never sags and never trivialises. Flat
$100 gives you 13 tapes for the first — a slow, discouraging start — and 5 tapes
for the last, by which point it is not a decision.

Total plugin spend $460, against $800 now.

### Type 2's rule

Extra blank cost is $10. Extra revenue is 0.8 x the Type 1 price. So Type 2
roughly breaks even at 2 modules and pulls clearly ahead from 3 up — **Type 2
pays in proportion to how good the tape already is.** That is a rule a player can
learn by playing, which is worth more than a flat "better tape, better shell".

---

## Three things that have to move with it

**1. `TevFronting.TapeMarketValue` hardcodes the old formula.**
`Assets/3 - Scripts/Story/TevFronting.cs:152` computes `(10 + 8 * modules) * tierMult`
by hand instead of calling `TapeValue.Base`. Retune `TapeValue` and Tev's 50%
cut silently keeps charging the old prices. It should call `TapeValue.Base`
whether or not this proposal lands.

**2. The ship price is defined twice.**
`ColdCompany.ShipPrice = 2000` and the `ShopItem` asset the ship vendor sells.
Both have to change or the beat and the shop disagree.

**3. Existing saves.**
Money already banked keeps its old face value, so a mid-game save becomes about
2.4x richer overnight. Cleanest is to land this on a fresh save. If any save has
to survive it, divide stored money by 2 on load behind a save-version check.

---

## Decisions for Sam

1. **The ship at $1000?** It is deliberately the only four-digit number. Lower
   makes the endgame closer; higher and it stops being reachable by tapes alone.
2. **Type 2 at 1.8x, or 2.0x for drama?** 1.8 makes it a steady edge; 2.0 makes a
   maxed Type 2 tape $64 and a genuine event.
3. **Plugin ladder, or flat $100 as you suggested?**
4. **Fishing cut to a third** — right, or too harsh for a system with its own dex?
5. **Fresh save, or a conversion on load?**

Nothing here is written yet. Say which parts match the vision and I will build
exactly those.
