# Playtest — Planet economy (species tables + trade routes)

Built 2026-09-07 while you were in the shower. Everything below compiles clean
and was checked in the Editor, **but none of it has been played.** Work top to
bottom; the early items are the ones everything else depends on.

**Before you start:** save the scene (Ctrl+S) — the MARKETS tile and the
economy wiring are in it.

---

## 1. Fish are different on each planet (5 min)

- [ ] Fish at **Humble Abode**. Over a handful of catches you should only ever see
      these six: **Skrout, Truttle, Knurl, Emberbass, Coelancer, Snagg**.
      Anything else biting there = the table isn't being read.
- [ ] Fly to **Icey Twin** (always in range from HA's neighbours; from HA itself it's
      in range ~58% of the time). Fish. You should only see **Wallop, Bassk,
      Nullpike, Flubb, Zibbet, Muskrellon**.
- [ ] A dwarf (**Puddle** is nearest the twins) should give exactly three species:
      **Perchik, Murkfin, Coelancer**.
- [ ] Cast distance should still decide rarity — short casts common, long casts
      rare — within the planet's list.

## 2. The new sell panel — Two Shelves (5 min)

The old sell UI is gone. Talking to any fish vendor opens the panel you picked
from the mockups: **YOUR BAG on the left, ON THE COUNTER on the right.**

- [ ] Every fish you're carrying (hotbar slots and fish bags) shows on the left
      with its weight, the bucket word (LOCAL / IMPORTED / DELICACY / UNLISTED)
      in colour, the $/lb this market pays, and the price it'd get, in gold.
- [ ] **Click a fish** — it moves to the counter. **Click it on the counter** — it
      goes back to the exact slot it came from. Try both a few times.
- [ ] The COUNTER total at the bottom updates live; **Sell the counter** greys
      out with nothing staged and shows the count when there is.
- [ ] **Confirm a sale.** Money received must equal the COUNTER total. This is the
      single most important check — if displayed ≠ paid, stop and tell me.
- [ ] Press **FISH PRICE INDEX** — the whole panel swaps to a page listing what
      this vendor pays for, per lb right now, grouped Delicacy → Imported →
      Local, with "N IN YOUR BAG" beside anything you're carrying and a
      "fresh / cooling −15%" note on off-world fish. **BACK** (or B / Esc)
      returns to the shelves with the counter untouched.
- [ ] Close the panel with fish still on the counter — they must return to your
      bag, nothing lost.
- [ ] Try the GRULABU (if you have it): clicking it should refuse with the
      bounty line, not stage it.
- [ ] Controller: the cards are buttons — stick moves between them, A clicks,
      B backs out (index first, then the panel).

## 3. A real trade route (10 min) — the point of the whole thing

The twins are the tutorial route: **1 km apart, always in range, each craves the
other's fish.**

- [ ] Catch a **Marlorb** or **Snagg** on **Fiery Twin**.
- [ ] Hop to **Icey Twin** (a 1 km jump costs ~11 fuel). Its board should list
      Marlorb / Snagg / Tarpune under **Delicacy**.
- [ ] Stage it. The card should read `Delicacy | … | $X` at **3×** what Fiery's own
      market would have paid. Sell it.
- [ ] Sell a **second** one of the same species right after. Its price should be
      **15% lower** than the first (appetite). A third, 15% lower again. It never
      drops below the Local price.

## 4. The phone remembers only what you've seen (3 min)

- [ ] Open the phone. There's a new **`$` MARKETS** tile.
- [ ] Every planet with a market is listed. Ones whose board you haven't stood at
      show **`??`** — you should have Humble Abode and whichever twin(s) you visited,
      nothing else.
- [ ] Pick a visited planet: the right pane shows the same three groups the board
      did, each fish with its current $/lb, plus *Sells crystals: yes/no*.
- [ ] Top-right reads e.g. **2/10 VISITED**.
- [ ] Walk within ~20 m of a new market's board, open the phone: that planet's `??`
      is gone and its list is there.

## 5. It survives a save (3 min)

- [ ] After selling a couple of delicacies, save at the pod, reload. The next sale
      of that species should still be at the cooled price (appetite saved), and the
      MARKETS page should still know the planets you'd visited.
- [ ] **New Game**: MARKETS is all `??` and prices are fresh.

## 6. Things I'd like your eye on (no right answer)

- Is **×3 for a delicacy** exciting, or does it make local fishing feel pointless?
- Does **15% per sale** cool a route too fast (three fish and it's over) or too slow?
- Do the placeholder names read okay? Rename freely in `FishingRules.cs` — the
  ids stay, only `displayName` changes.
- Dwarfs now list **2 delicacies** instead of the 1 in the handoff. Needed so all
  24 species have a buyer; say if it makes dwarfs feel too generous.

---

## If something's wrong

- Tell me **which planet**, **which fish**, and **what the card said vs what you
  got**. That's enough for me to find it.
- The whole table is `Assets/StreamingAssets/Economy/planet_economy.json` — you
  can change any list by hand, and in the Editor it reloads live during Play.
  **Tools ▸ Economy ▸ Validate Planet Tables** tells you if you broke a rule.

## Known gaps (not bugs)

- Co-op: appetite isn't replicated to guests yet — a guest's card can be a few
  seconds stale. Single-player is unaffected.
- Goods vendors / buying crystals: still deferred, as you chose.
- Still 3 fish meshes. New species differ by colour and name only.
