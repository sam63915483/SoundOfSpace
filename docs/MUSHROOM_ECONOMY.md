# Mushroom economy — implementation notes

Built 2026-08-04 from `Handoff_CozyLoop_Switch_MushroomSlice_v1.md` plus Sam's
answers to its five open questions and one addition (mushroom saplings).

**Status: BUILT + compiles clean in the Editor. NOT play-tested.**

---

## What a "mushroom" is now

Species identity is the **prefab name** (e.g. `Amanita_big`), not an index —
reordering `MushroomSpawner.mushroomPrefabs` can't turn a saved red cap into a
blue one. 23 species ship today, straight off that array. Adding a species is
still just dragging a prefab into it; nothing else needs wiring.

`MushroomRegistry` is the single source of truth: prefab lookup, display name,
a render-only model builder (ground drop + held item) and a cached preview
RenderTexture (hotbar + locker slots). That's what guarantees the cap on the
ground, the icon in the hotbar and the model in your hand are all the mushroom
you actually chopped — the same rule the fish follow.

Two new `Hotbar.ItemId`s: `Mushroom` and `MushroomSapling` ("spores").
`Hotbar.Slot` gained `mushroomSpecies`. Stacks are **20, species-pure** — every
add / spend / merge path (hotbar, locker drag-and-drop, quick-move, save/load)
matches on species as well as id.

## Harvesting

`SpawnedMushroom` is the tree of the mushroom world:

| | tree | mushroom |
|---|---|---|
| HP roll | 4–8 | **2–4** (half) |
| hit feedback | 0.18s / 3° single-axis shake, wooden thunk | 0.6s / 9° **two-axis wobble + squash-and-stretch**, wet squish |
| break | topple 0.7s → shrink 0.4s | topple 0.5s → shrink 0.35s |
| drops | 8–20 wood, 1–3 saplings | **3–9 caps, 0–2 spores** (same species) |

Streamed mushrooms get a **solid capsule collider** fitted to their mesh bounds
— the axe's `BladeSweep` sphere-casts with `QueryTriggerInteraction.Ignore`, so
the old trigger-only mushroom was literally unhittable. Both axe paths were
wired: `BladeSweep` (physics axe) and `AxeController.DetectSwingHit` (classic
swing). Mushrooms are swing-through for the axe's ground/wall clearance, like
trees and crystals.

The wooden hit clip is suppressed on mushroom contact — `SpawnedMushroom` plays
its own squish so a cap never sounds like a trunk.

Drops are one object per cap (not the chunked sprite-slab split logs use): nine
little species-matched models scattered round the stump reads right where a
single icon carrying "×9" would not.

**Squish SFX**: generated and wired — `Assets/Audio/Mushroom/mushroom_squish_01–03.wav`
(random per hit) + `mushroom_break.wav`. Placeholders; swap the clips on the
`MushroomSpawner` inspector when Sam records the real ones.

## Planting (Sam's addition)

Chop → 0–2 spores of that species → select the spore slot → the placement ghost
appears → plant → `MushroomGrowth` grows it back into **the same species**.

`MushroomGrowth` is a deliberate near-copy of `SaplingGrowth` rather than a
reuse of it, because a mushroom is not a tree in the one way that matters:
**it makes no oxygen**. `PlanetOxygen`, `BubbleDome` and the Tree Daddy /
Tree Killer progression tracks all count `SaplingGrowth` instances, and slipping
mushrooms into that list would inflate a planet's O2 off fungus. It *does* share
the growth model (speed scales with local ambient O2, stalls below a floor) —
that's the trees → oxygen → faster mushrooms link the handoff wants later.

Mushroom entries set `isSapling` **and** `isMushroomSapling`: the first rides the
whole existing ground-snap placement flow unchanged, the second branches the
cost (1 spore of that species), the grower, and the Tree Daddy exclusion. The
8.3m tree-spacing gate is skipped for mushrooms — caps cluster, and a no-plant
radius round every trunk would make a forest unfarmable, which is exactly where
you want a mushroom farm. The barren-rock gate still applies to both.

## Selling

Space dust selling is **VAULTED**, not deleted: `FeatureVault.SpaceDustSelling
= false`. Dust still spawns, is still collectable, still sits in the hotbar and
still saves — only the "Sell space dust" row is gone. `SpaceDustSellUI` and
`NPCSellDustOption` still compile and are still wired; flip the const and the row
comes back on every NPC at once.

All four NPC types now offer "Sell mushrooms": `RandomAlienDialogue` (wandering
aliens), `Alien7Vendor`, `ShipMarketNPC`, `FishMarketNPC`. The rows are
assembled through `NPCSellRows` rather than hard-indexed, which is what makes
that one-const flip work without four index rewrites.

`MushroomSellUI` differs from the dust panel in two deliberate ways:

- **No accept-chance gamble.** A buyer buys. The interesting decision is *who
  you walk to*, not whether the dice land.
- Stock comes from the hotbar and is **species-pure** — it sells the leftmost
  stack's species, then rolls on to the next.

**Price is per-alien and stable** (Schedule 1 rule). `NPCMushroomPrice` derives
it from a hash of the alien's identity — spawn cell for streamed aliens, scene
name otherwise — into **12–29 credits**, centred on Sam's ~20 target. Derived,
never stored: a wandering alien despawned at 300m and restreamed later still
quotes the same number, with nothing to persist.

## Eating

Moved off the world prop and onto the hotbar: select a mushroom, hold fire, same
progress ring and raise-to-mouth pose the raw fish uses.

The heal + 30s trip are unchanged. **One deliberate change**: the colour /
breathing / kaleidoscope dials used to be rolled per world instance from its
spawn cell. A harvested cap is an item now, and carrying three floats per stack
through the hotbar, the locker and the save file to reproduce a mushroom that no
longer exists is a lot of plumbing for something the player can't see. So
`MushroomEffect` derives them from the **species key** instead: every Amanita
trips the same way, different species trip differently. Same spread of effects,
and it becomes knowledge the player can learn — which is what a drug economy
wants. Heal is a flat 5 HP per cap rather than the old 5–25, because that range
was the payout for eating an ENTIRE mushroom and one mushroom is now 3–9 caps.

`MushroomInteraction` is kept but deprecated and no longer attached (handoff's
"disable it — don't delete assets" rule).

## Tev's onboarding

`TevMushroomOnboarding`, on `Humble Abode/TEV2` beside the existing
`TevDialogue`. It **disables** the deprecated on-landing behaviour (the wave,
and the Mission-1 dialogue tied to on-hold story content) rather than deleting
it — `restoreMissionDialogue` hands Tev back to the mission tree when story
resumes.

Hidden for **120s** from the moment the shuttle's exit ramp deploys
(`ShuttleExitDoor.OpenedAtTime`), then he's just standing outside his cabin,
idle and interactable. He appears whether or not the player left the shuttle.
Once past the first talk he's permanently present, so a mid-game scene reload
can't make him vanish for another two minutes.

State lives in `MushroomQuest` over new **StoryDirector counters** (`GetCounter`
/ `SetCounter` / `AddCounter`, persisted in `StoryDirectorSave`) — flags can't
count.

- **First talk** → fronts 3 caps (`Agaricales_big`, deterministic).
- **Return talk** → two questions with greyed-out options.
  `PostGreetingChoicePanel` already supported visible-but-unselectable rows, so
  the handoff's "[BUILD if missing]" wasn't needed. Outcomes branch on the
  **truth** (live inventory + sale count), not on which row was clicked — the
  greying already guarantees they agree:
  - holding some → sent back out, stage stays Given
  - sold ≥1, holding none → teaches the loop (trees → O2 → faster shrooms →
    plant your spores), **Complete**
  - sold 0, holding none → ridicule, then **fronts another 3** — up to 5 extra
    batches, then he's done and points you at the wild ones

### The intentional exploit

`MushroomQuest.HeldCount` reads the **hotbar only, never the locker**. That is
Sam's spec: stash his caps in the shuttle locker, tell him you lost them, get
another three. A real exploit with a hard ceiling — five free batches (15 caps)
and the tap shuts off. A player who finds it is rewarded for being clever
exactly as far as the designer allows, and no further. Don't "fix" it.

## Save schema

- `HotbarSlotSave.mushroomSpecies` — hotbar, fish-bag contents and locker slots.
  Part of a stack's identity; losing it merges two species on load.
- `SaveData.plantedMushrooms` (`PlantedMushroomSave`) — body-local, species by
  key, `growth >= 1` restores as a mature choppable mushroom. Captured/applied
  right after the saplings step.
- `StoryDirectorSave.counterNames/counterValues`.

All JsonUtility-safe and empty-by-default, so old saves load correctly.

## Play-test checklist

- [ ] Locker holds axe + water bottle on first open *(verified in-scene: two
      `LootBoxStarterItem`s on `Shuttle_Lander/Interior/Locker_2` — Axe + WaterBottle)*
- [ ] Ramp opens → Tev absent exactly 120s → appears at his cabin; no wave, no
      old Mission-1 dialogue
- [ ] Mushroom chop: ~half a tree's effort, squish per hit, wobble reads rubbery,
      topple + shrink, 3–9 species-matched drops, spin/bob, walk-over pickup
- [ ] 0–2 spores drop; planting one grows the SAME species back
- [ ] Stacks cap at 20; two species never merge; locker in/out works; held +
      hotbar visuals match the species
- [ ] Eating a held mushroom heals and trips
- [ ] Every NPC offers "Sell mushrooms"; no NPC offers space dust; prices differ
      per alien and are the same on a second visit
- [ ] Tev: every grey-state combination; ate-all path fronts a new batch 5 times
      then passes; locker-stash exploit works and then stops
- [ ] Selling advances Tev's sold count

## Known trade-offs to watch in play

- **23 species × 20-per-stack × 7 hotbar slots.** Species purity means a player
  chopping indiscriminately fills the bar fast. If that bites, the fix is fewer
  species in the spawner array, not merging stacks.
- Mushrooms now have solid colliders, so the player can bump into (and stand on)
  a 5×-scaled cap. Trees already behave this way; if it reads badly, shrink
  `maxScale` on the spawner rather than removing the collider (the axe needs it).
