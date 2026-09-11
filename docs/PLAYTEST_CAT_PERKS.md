🟢 ACTIVE — built 2026-09-10, never play-tested.

# Playtest: space cats & fishing perks

Feed a wandering cat any fish, it rolls you a timed fishing buff out of a
CS:GO-style case. Design mockup and odds lab: `tools/cat-perks/` → localhost:8767.

---

## ⚠️ One-time setup before this works at all

The spawner needs 14 asset references, and an Animator with no Avatar plays
**nothing and reports nothing** — so this is wired by a tool, not by hand.

1. Open `Assets/1.6.7.7.7.unity`.
2. **Tools ▸ Solar System ▸ Wire Cat Spawner.**
   It creates a `CatSpawner` object, finds the 11 cat prefabs, the `Cat_L`
   avatar and 13 clips, and logs what it found. A warning here means the pack
   moved — read the list of what was missing.
3. Drag the scene's `InputSettings` asset onto the spawner's **Input Settings**
   slot (optional — without it cat density ignores the VIEW DISTANCE slider).
4. **Save the scene.** The tool deliberately does not save for you.

---

## The rules as built (your calls, 2026-09-10)

| | |
|---|---|
| Perks stack? | **No.** One slot, ever. |
| Re-roll while one is running? | **Yes**, and the new perk **replaces** it — timer set to the new tier's full length, never topped up. |
| Confirmation before overwriting? | **No.** The picker states what's running and how long is left, then lets you do it. |
| Tiers | **Duration only.** A 3-min Luck is a longer 1-min Luck, same strength. |
| Cat cooldown | **None**, per your call. Add one if it turns out abusable. |

Odds (3× ladder): 1-min **52%**, 2-min **30%**, 3-min **17%** — about 6 fish per
3-minute buff.

---

## Checklist

### The cats exist and behave
- [ ] Cats appear on planet surfaces, roughly one per 140 m cell, max 6 at once.
- [ ] They are **not magenta**. (If they are, the material repoint failed.)
- [ ] They loaf: sit, lie down, sleep. That should be most of what they do.
- [ ] They stroll sometimes, and occasionally trot (faster gait).
- [ ] Walk animation only plays while actually moving — no moonwalking, no
      sliding, no frozen cat gliding across the ground.
- [ ] Walk **up to** one: it stops, sits, and watches you.
- [ ] Cats stay on their patch, don't walk into the sea, don't spawn on cliffs.
- [ ] Fly away and back: cats are in the **same places** (deterministic hash).

### The trade
- [ ] Look at a cat → outline + "Press F to pet the cat".
- [ ] F → "Meow! Spare a fish?" with Yes / No.
- [ ] **No** does nothing. **Yes** opens your fish pockets.
- [ ] ⚠️ The fish list must **not** be visible before you answer Yes.
- [ ] Any fish works, any species. The fish is consumed exactly once.
- [ ] Empty pockets reads as "Your pockets are empty", not a broken menu.

### The case (option B — vertical tag drop)
- [ ] Tags scroll down, decelerate, land slightly off-centre.
- [ ] The landed tag is the one the result screen then names.
- [ ] Result names the perk, the duration, and the % chance.
- [ ] Esc can't skip a reel in flight (that would eat the fish for nothing).

### The one slot
- [ ] HUD chip top-left shows the perk and counts down.
- [ ] Roll again with one running → picker warns what's running + time left.
- [ ] New perk **replaces** the old; chip shows the new one at full time.
- [ ] Result says **THREW AWAY / SWAPPED / TRADED UP FROM** with the seconds lost.
- [ ] Chip disappears when the timer hits zero.
- [ ] Never two chips.

### The perks actually do something
- [ ] **FISHING FRENZY** — bites arrive obviously faster. Tier mix unchanged.
- [ ] **WEIGHT GAIN** — same species, noticeably fatter. Tier mix unchanged.
- [ ] **LUCKY WHISKERS** — sooner bites, more uncommons/rares, bigger.
- [ ] With **no** perk running, fishing feels exactly as it did before.

### Doesn't break anything
- [ ] Main menu → New Game: no perk carried over from the last run.
- [ ] Player can move/look again after closing the cat UI (cursor re-locks).
- [ ] Cat despawning while you stand in its trigger leaves **no stuck prompt**.
- [ ] Console stays clean.

---

## Known / deliberate

- **Not saved.** A perk lasts three minutes at most, and adding it to the save
  meant touching the fragile apply-order for very little. Reloading drops it.
- **No sound yet.** `CatPerkTradeUI` has `meowClip` / `tickClip` / `winClip`
  fields, unassigned. The tick is a big part of the case feel — worth a pass in
  Audio Studio.
- **Cats don't swim yet.** The pack has `Swim_F` / `Swim_idle` and both are
  already wired into the clip table and pose enum, but `AlienWander` refuses to
  step into water deeper than a wade, so nothing drives them. Enabling it means
  an opt-in swim mode on the wander.
- **Outline is the game's amber**, not green — it's the shared `GazeHighlight`
  colour every interactable uses.
- One pack warning is unfixable from here: *"A polygon of Mesh 'Cat.L' is
  self-intersecting and has been discarded"* is a flaw in the vendor's model.
  It fires on import only and costs one polygon.
