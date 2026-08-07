# Progression perks — what each track pays out

Status as of **2026-08-03**. The five-track level system (`PlayerProgress`)
tracks and displays progress; only **Colonizer** currently *grants* anything.
This file is the backlog for the rest, written down at the point the Colonizer
unlocks shipped so the intent doesn't get lost.

Sam's design calls, verbatim intent:

> "for tree daddy make them grow faster the higher your level is, for tree
> killer make the amount of wood you get increase from killing a tree the higher
> your level is. for gansta rep ill make it so you can get more items from the
> vendor shops."

---

## Built

### Colonizer → blueprints
`BuildableUnlocks.cs` — a level→blueprint table. Locked entries stay visible in
both the desktop build menu and the phone Build app, dimmed with a padlock and
the level they need. Nothing is saved: the unlock set derives from the Colonizer
score, which `PlayerProgress` already saves.

Level 0 starts with Torch + Bonfire; L1 adds Wall 1, Wall 3 and Wooden Floor 1;
the table runs to L10. Retuning is one array edit.

---

## Not built yet

### Tree Daddy → saplings grow faster
Scale `SaplingGrowth`'s growth rate by Tree Daddy level. Suggested curve:
`rate × (1 + 0.12 × level)` — L10 is roughly 2.2× planting speed, enough to feel
without trivialising the oxygen loop.

- **Where:** `Assets/3 - Scripts/Survival/SaplingGrowth.cs` (growth tick).
- **Save:** none needed — read the level live at each tick.
- **Watch out:** growth is also what feeds the tree/oxygen ecosystem, so a fast
  planter shifts planetary O2 balance. Check against the dome converter numbers
  before settling the multiplier.

### Tree Killer → more wood per tree
Scale the wood a felled tree yields by Tree Killer level. Suggested:
`base + floor(level / 2)` so the step lands every other level and stays legible
in the toast ("+4 WOOD" rather than "+3.6").

- **Where:** the wood grant in `SpawnedTree` / `AxeSwing`'s fell path.
- **Save:** none.
- **Watch out:** wood is the build-menu currency, so this compounds with the
  Colonizer unlocks. Late levels may want a cap.

### Gangsta Rep → better vendor stock
Higher rep unlocks more items / larger quantities at the vendor shops.

- **Where:** the vendor stock tables (`Assets/3 - Scripts/Vendor/`).
- **Design open question:** does *negative* rep shrink stock, or refuse service
  entirely? Rep is stored signed specifically so it can go below zero.
- **Save:** none if stock is derived from the level at open time.

### Explorer → ?
No design yet. Candidates: compass/scanner range, map markers for worlds already
reached, or a fuel discount. Explorer is discrete (one level per world, nine
total), so its perks arrive rarely and should feel correspondingly chunky.

### General level → rank + milestones
The rank name (`LevelUpCeremonyUI.RankFor`) already ships: CASTAWAY → LEGEND.
Candidates for actual grants at general 5 and 10:

- HAL's line pool shifts by rank — he's dismissive early, deferential late. This
  is templated text, so it's the cheapest big-feeling reward available.
- A HUD accent colour unlock (`HelmetHudConfig` already centralises the accent).
- A suit decal / visor etch.

---

## Notes for whoever builds these

- Perks that read the level live need **no save work at all** — that's the whole
  reason the unlock table is derived rather than stored. Prefer it.
- Every level should *say* what it gave you. `LevelUpCeremonyUI` already has a
  card type for "here's what you just unlocked" (`Kind.Blueprints`); reuse it
  rather than inventing a second popup.
- A perk that silently changes a number is worse than no perk, because the
  player can't tell it happened.
