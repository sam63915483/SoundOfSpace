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

---

# Second pass, same day — reeling, fish fight, settings

## 6. Reeling is 2x faster everywhere

All three kinds of reeling doubled:

| | before | now |
|---|---|---|
| winding an empty bobber home over land | 3.3 m/s | **6.6** |
| sliding an empty bobber back through water | 3.3 m/s | **6.6** |
| the reel handle against a fish | 6.5 m/s | **13** |

- [ ] Cast, don't wait for a bite, wind it straight back in. Over water and over
      land it should come home about twice as quickly as you remember.
- [ ] Cast onto land (or a beach) and tow the bobber back over bumps. Still snags
      on terrain the same way, just faster.

**One thing to know before you judge it:** on a *fish* the handle is twice as fast
but the fish also pulls back about twice as hard, so the net gain is more like
1.4x, not 2x. That is deliberate — a straight 2x with no change to the fish would
have halved every fight and made the whole thing a formality. If it still feels
too slow against a big rare, the number to move is `ResistFor` in
`FishingRules.cs`, not `ReelSpeed`.

## 7. Fish fight harder, faster, and more back-and-forth

Measured by the headless sim (`py -3 prototypes/shuttle-computer/test/verify-fishing.py`),
where "tug-of-war" is every metre the fish took back over the whole fight, against
a 12 m cast:

| tier | fight length | tug-of-war |
|---|---|---|
| Common | 2.1s → **1.5s** | 0 m → 0 m (commons still never run — they are the beginner's fish) |
| Uncommon | 2.8s → **4.2s** | **0 m → 7.5 m** |
| Rare | 9.9s → **9.3s** | 11.6 m → **19.4 m** |

- [ ] **Uncommons.** This is the big change. They used to land before their first
      run was ever due — the sim measured literally zero ground given back, so the
      mid tier was a haul with a bar on it. Every fish that can run now BOLTS
      shortly after the hook. An uncommon should feel like an actual fight now.
      It also takes about a second and a half longer. Tell me if that reads as
      "good, it fights" or "too slow, I catch a lot of these".
- [ ] **Rares.** Slightly shorter than before but you now lose and re-win about
      1.6 lengths of your cast over the fight. It should feel like a struggle.
- [ ] **Commons.** Noticeably quicker (2.1s → 1.5s). Grinding them should feel
      brisker.
- [ ] The bar still punishes holding: you should still lose a rare on a long cast
      if you just hold the button, and still land commons that way.
- [ ] Watch the rod. It bends harder and more often now — check it still looks
      right rather than spasming.

## 8. Settings: the "how many" sliders are gone

MAX TREES / MAX ALIEN NPCS / MAX MUSHROOMS / MAX CRYSTALS / MAX AUDIENCE have been
removed from the GRAPHICS tab. **VIEW DISTANCE is the only world knob now**, and
the tree / NPC / mushroom counts are worked out from it, so density stays the same
however far you can see:

| view distance | trees | NPCs | mushrooms | crystals | audience |
|---|---|---|---|---|---|
| 200 m (Low) | 34 | 6 | 23 | 20 | 25 |
| 350 m (Medium, default) | 60 | 10 | 40 | 20 | 25 |
| 500 m (High) | 86 | 14 | 57 | 20 | 25 |
| 800 m (Ultra) | 137 | 23 | 91 | 20 | 25 |
| 1000 m (max) | 171 | 24 | 114 | 20 | 25 |

Crystals and the concert crowd are deliberately fixed — crystals are fuel and
stream in their own 300 m bubble regardless of view distance, and the crowd stands
in a venue rather than scattered over a planet.

- [ ] Open GRAPHICS. Under WORLD there should be **one** slider: VIEW DISTANCE.
- [ ] Drag it from 200 to 800 and back. The forest should get bigger and smaller
      but never look thinner or denser — that is the whole point of the change.
- [ ] Quality presets (Low / Medium / High / Ultra) should still change the world
      the same amount they used to; they set the distance and the counts follow.
- [ ] If the old pre-tab settings panel is still reachable anywhere, its four count
      sliders should now be hidden rather than present-but-dead. Their GameObjects
      are still in the scene — safe to delete in the Editor whenever you like.
