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

## 2. The vendor pays by the words on the board (5 min)

- [ ] Walk up to Humble Abode's market. The **board** above the stand should now
      show three groups: *Local*, *Imported — pays well*, *Delicacy — pays top*,
      with fish names under each. (Not the old "Buying today" placeholder.)
- [ ] Open the sell panel with a **local** fish staged. The card reads
      `Local | 12 lbs | $X value` and the total matches.
- [ ] **Confirm a sale**. The money you get must equal the total shown.
      This is the single most important check — if displayed ≠ paid, stop and
      tell me.
- [ ] Bring a fish the board *doesn't* list at all. Card should say **Unlisted**
      and pay half.

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
      did, plus *Sells crystals: yes/no*.
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
