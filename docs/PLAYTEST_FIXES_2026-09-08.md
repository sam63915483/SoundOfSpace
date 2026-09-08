<!-- doc-status: stamped 2026-09-08 -->
> 🟢 **ACTIVE — playtest checklist for the 2026-09-08 tuning pass.**

# Playtest — fuel curve, fish markets, world saves (2026-09-08)

Everything below compiles clean (0 warnings, all three assemblies). None of it has been
played. Full reasoning is in `docs/CURRENT_STATE_AUDIT.md`, addendum 2026-09-08.

**One thing to do in the Editor before playing:** run
**`Tools ▸ Economy ▸ Validate Planet Tables`** with `1.6.7.7.7.unity` open. It should say
`planet_economy.json is CLEAN — every rule holds.` If it complains, stop and send me the
message — the import lists were regenerated outside Unity and the validator is the check.

---

## 1. Fuel — does a short hop feel cheap and a long one feel scary?

Range is back to **15 km**. What changed is the *shape* of the cost, not the reach.

| hop | costs you | in crystals |
|---|---|---|
| 2 km | 9% of a tank | ~2 |
| 5 km | 21% | ~4 |
| 10 km | 55% | ~11 |
| 15 km | 100% | 20 |

- [ ] Hop to a close neighbour (NAV shows it at 1–3 km). It should cost well under a
      quarter tank — you should be able to do this several times on one fill.
- [ ] Hop to something 9–12 km away. It should hurt, and the NAV tile should tell you
      the cost before you commit.
- [ ] **Wait for a window.** Pick a planet sitting at ~10 km, do something else, come
      back when NAV shows it at ~3 km, then jump. It should now cost roughly a sixth of
      what it would have. This is the behaviour the whole change is for — if waiting
      doesn't feel worth it, tell me and I'll raise the exponent.
- [ ] NAV should show **"IN RANGE in ~N min"** on tiles you can't afford yet. At 22.5 km
      this line effectively never appeared. If you still never see it, something's wrong.
- [ ] `RANGE X.X KM` on the NAV header and the reactor screen should match what you can
      actually afford — if a tile reads 8.0 KM and RANGE says 8.6, the jump must go.

**Design consequence to feel for:** Cyclops should be hard to reach again — only from
Pebble, Bruise or Humble Abode, and only in a window. If you can hop to it straight from
the Twins, the range didn't take.

## 2. Fish markets — does route knowledge matter now?

Each planet's Import list is now drawn from what its *orbital neighbours* catch, and is
4–6 species instead of 9–10. Delicacies are unchanged.

- [ ] Fish at home, fly **next door**, sell. Most of what you're carrying should still
      read **Imported** or **Delicacy**. (Target: ~86% of the time.)
- [ ] Fish at home, fly somewhere **far**, sell. Now most of it should read **Unlisted**
      and pay the 0.5× dump price. (Target: only ~35% premium.) This is the point — a
      badly chosen destination should be a real mistake.
- [ ] Delicacy runs should still exist and still be the big payday. Every species has at
      least one delicacy buyer somewhere it isn't caught.
- [ ] The vendor board and the phone MARKETS page should show shorter lists. Check they
      still lay out properly and nothing overflows.

**If short hops now feel unreliable**, that's the number to tell me — I can raise the
import cap from 6 without going back to the old "everywhere pays a premium" problem.

## 3. Saves — a save is a world, not a character

- [ ] Play a bit (money, fish, some gear), save at the pod, quit to menu.
- [ ] **Make a second character** (different name, different suit colour), then load that
      same save. **You should arrive with all your stuff.** Before this change you'd have
      arrived with empty pockets, no money and a blank hotbar.
- [ ] Save again as the second character, quit, load again as the first. Still all there.
- [ ] Sanity: the astronaut's **name and suit colour should follow the character**, not
      the world. That part is meant to stay per-character.
- [ ] Start a **New Game** — pockets should be empty and the orientation board blank.

## 4. Crystals on Low quality

- [ ] Set graphics to **Low** in the pause menu. You should see the same number of
      crystals on the ground as on Medium (it used to be half). Nothing else about Low
      should change.

## 5. Nothing else broke

- [ ] Grass looks the same walking around at night with the torch. (A 601-line grass
      diagnostic was deleted; it never rendered anything, but it did ship in builds.)
- [ ] Torches, lanterns and the shuttle's lights still light the grass.
