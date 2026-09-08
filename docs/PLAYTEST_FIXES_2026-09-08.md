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

---

# Third pass, same day — the rod IS the bar, and the cast is a charge

## 9. Casting is now hold-to-charge

- **Tap** the button: the bobber plops out about **3 m**.
- **Hold 1 second**: about **8 m** — halfway.
- **Hold 2 seconds**: about **13 m**, roughly where a single click used to land.

There is no charge meter on purpose — **the rod draws further back the longer you
hold**, which is the same information without adding UI.

- [ ] Tap, half-hold and full-hold. The rod should draw back progressively while
      you hold and fling forward when you let go — one continuous motion, not a
      yank backwards followed by a throw.
- [ ] The cast should feel shorter *by default*, because the click-cast went from
      ~12 m to ~3 m. The **maximum** did not shrink much, and that is deliberate:
      the fish starts at the cast distance, so the cast IS the length of the
      fight. I tried capping it at 8.5 m first and a rare was over in two seconds.
- [ ] Charging should be worth it. **A rare fight lasts ~1.2s on a tap and ~6.5s
      on a full charge**, and a full charge also rolls better fish.
- [ ] If the distances are off, the knob is `bobberShootSpeed` on the rod in the
      scene (full charge, currently 20) and `bobberShootSpeedTap` (tap, 10). Range
      goes as speed *squared*.

## 10. The rod is the tension bar

Bend is now **the fish's weight plus the bar, and nothing else**. It used to be a
step function — the moment you pressed reel it jumped to 45% load, and the moment
a run started it added another 50% — so it snapped between three fixed poses.
That was the "little bent to fully bent very fast".

- [ ] Reel with **no fish on**. The line should tighten and the rod should take a
      small, honest bend as it does — growing with the tightness, not popping on.
- [ ] Reel with a fish on. The bend should climb **with the bar**, smoothly. Watch
      the two together: they should agree at every moment.
- [ ] **Let go mid-fight.** The rod should come down *with* the bar — not snap
      straight. There is still a fish on the end.
- [ ] The rod now springs back faster than it loads. It was the other way round
      (load 12, release 7) which is backwards for a real rod and backwards from
      what its own tooltip claimed.
- [ ] **When you're ready to try it:** uncheck **`showTensionBar`** on the
      FishingTuning asset and fight by the rod alone. That's the whole point of
      this pass. Measured: the bend never moves more than **1.8% of full bow per
      frame while cruising** and **4.1% mid-push**, so you always get ~half a
      second of warning.

## 11. The line stops lying

Two places it jumped, both fixed:
- **At the bite.** A fish that took a *moving* lure was hooked on a bar-tight
  line, but the fight started its line at zero — so the line popped tight→slack
  the instant you hooked up.
- **At the end of the fight.** The bookkeeping was skipped entirely during a
  fight, so the value froze at whatever it was when the fish bit, and the line
  snapped to that the frame the fight ended.

And a fish now **holds the line tight by itself**. Releasing the reel used to send
the line straight for fully slack while there was still a fish on the end.

- [ ] Hook a fish, then stop reeling for a second. The line should stay mostly
      tight, then visibly droop over about a second as the fish gives up — and if
      you let it droop all the way, that's when you lose the fish. **The droop is
      now the warning.**
- [ ] Start reeling again mid-droop; it should come straight back tight.

## 12. Fish fights — your playtest notes

> "commons dont fight at all and literally are just free to catch ... rares were
> extremely hard to catch, i would be reeling and as soon as they start fighting
> and tugging it would snap off"

Both were arithmetic, not luck.

**The snap.** Reeling into a run cost `ReelRate × pull × 2 × 1.0` — at the
`ReelRate` of 96 I'd set that morning, **345 tension a second on a rare, so the
bar went from empty to snapped in 0.29 seconds.** No reflex covers that.

**The fix is time, not strength.** Softening the push far enough for a human to
survive also made it soft enough to ignore — the hold-the-button bot went from
losing everything to landing 47%. So each push now **winds up over a quarter
second** instead of arriving at full strength in one frame. React and you take a
fraction of it; hold on and you take all of it. And **commons push too** now.

| | before | now |
|---|---|---|
| push length | 1–2s | **0.35–0.7s** |
| push every | 1.1–2.8s | **0.8–2.4s** (rarer fish more often) |
| commons push? | never | **yes** |
| tension reeling into a push | 4× steady, instantly | 4× steady, **over 0.25s** |

Measured with a bot that has a **0.20s reaction time** — the old bots reacted in
the same frame, which is exactly why every check passed while the game was
unplayable:

| | lands |
|---|---|
| careful player (lets go at 70% of the bar) | Common 100%, Uncommon 100%, **Rare 100%** |
| greedy player (holds to 85%, 0.28s reaction) | **Rare 59%** |
| just holding the button | loses ~90% of the good fish |

- [ ] **Commons should now fight** — a shove or two before they give up — but
      still be easy.
- [ ] **Rares should be catchable.** If one still snaps on its first push, tell me
      and I'll lengthen `RunRampSeconds` (currently 0.25s).
- [ ] Fights at a typical cast: common ~1.9s, uncommon ~2.3s, rare ~3.6s (a big
      one up to ~18s). Full-charge casts are much longer.
