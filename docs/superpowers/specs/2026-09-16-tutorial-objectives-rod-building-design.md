🟢 ACTIVE — built 2026-09-16, playtest pending (Sam places the rod, then plays).

# Tutorial box: TV objectives, the rod in the shuttle, and basic building

Revision 1 of the tutorial's actual *content*. The box itself (the generated
Humble Abode clone, the 350 m walls, the fly-in) was built 2026-09-14/15; this
is the first pass at giving the player something to do inside it.

Sam's ask, verbatim:

> in the tutorial, i think we need to use the tv screen in the shuttle to show
> objectives, then when each one is complete it gets crossed out. can you make it
> so that the fishing rod is inside the shuttle in the tutorial, and then ill move
> it and place it where we need so that the user can look at it and press f to
> pick it up. the tutorial objectives should be catch a fish, cut down a tree and
> plant a sapling, then also place a bonfire and cook a fish. well need to unvault
> building with the phone, and just make it so the only 2 things you can place is
> a bonfire and a torch.

## What already existed

Most of it. Worth writing down, because the shape of the work was "wire up and
trim", not "build":

| Piece | State before |
|---|---|
| The TV board (`OrientationObjectivesScreen`) | Built. Lists objectives on the shuttle's arm-mounted TV, animates a strike-through quad across a line when it completes, hides until the orientation film ends. Lives inside `Shuttle_Lander.prefab`, so the tutorial's shuttle already had one. |
| Catch a fish | `Bobber.cs:2649` |
| Chop a tree | `SpawnedTree.cs:220` |
| Plant a sapling | `ProgressHooks.cs:73` |
| Cook + eat a fish | `BonfireInteraction.cs:346` |
| Look-at-and-press-F rod pickup | `FishingRodPickup.cs`, one instance, a scene object in Tev's cabin |
| "Only torch and bonfire" | Already `BuildableUnlocks` level 0 — *"what you land with. Warmth and light, nothing structural."* |

Genuinely new: a **place a bonfire** objective, and the plumbing that lets the
tutorial scene run any of it.

## Decisions

Three forks, all Sam's call (2026-09-16):

1. **The build unvault reaches the whole game**, with the catalogue trimmed to
   two — not a tutorial-only special case. Cooking is core loop everywhere, so a
   bonfire you can place anywhere serves the real game.
2. **The tutorial's TV shows Sam's five lines**; the gameplay shuttle keeps its
   seven. One board component, a per-scene list.
3. **No "take the axe" line.** The shuttle prefab already seeds an axe into a
   locker on a new game, so the box has one; how you got it is not a checklist
   item.

## Design

### 1. Building: a new tier, not an unvaulting

`FeatureVault.FreeformBuilding` stays `false` — that flag means *the full
forty-blueprint system*, and the vault's promise is that flipping it back
restores that exactly. Beside it:

```
FeatureVault.BasicBuilding = true          // menu opens, two blueprints
FeatureVault.BuildMenuAvailable            // either tier — every open-guard reads this
FeatureVault.BuildCatalogueIsBasicsOnly    // trim to two — false once freeform returns
```

Three guards moved from `FreeformBuilding` to `BuildMenuAvailable`:
`BuildMenuUI.Open`, the phone's Build tile (`PlayerPhoneUI`), and the phone Build
app's list. `GrowPotRegistrar` / `DomeBuildRegistrar` deliberately keep testing
the old flag, so neither injects its entry at this tier. `TutorialSteps` keeps
testing it too — the step it gates builds a *cabin*, which this tier has no
blueprint for.

`BuildMenuUI.BasicBlueprints` is the allow-list (`Torch`, `Bonfire`), matched by
the same loose normalisation `BuildableUnlocks` uses, because several
scene-authored names carry stray spaces.

**Rows off the list are not drawn at all.** The normal Colonizer-lock treatment
draws a locked blueprint dimmed with a padlock and the level it needs, and
that's right — seeing the wall you can't build yet is the point. It is wrong
here: the level system is *itself* vaulted, so those rows can never unlock, and
the menu would be forty padlocks deep in things that are never coming. The
category tabs are built from offered rows for the same reason, and the phone
app's category cycle is skipped entirely at this tier (two blueprints in one
category — every press but the first would empty the list).

### 2. The bonfire objective

`OrientationObjectives.Objective.PlaceBonfire = 7`, **appended** — those are bit
positions in a persisted mask, so inserting one silently re-labels every
existing world's board. `Count` 7 → 8.

It ticks in `GhostPlacement`, at the branch that already bolts a
`BonfireInteraction` onto a freshly placed structure. That flag is what *makes*
a blueprint a bonfire, so it is the honest test: a fire placed from any
catalogue, in any scene, ticks the line.

### 3. The board renders by ROW, not by objective

`OrientationObjectivesScreen.shownObjectives` — empty means the full board in
enum order (what the gameplay shuttle wants), filled means that subset **in the
order given**.

Everything downstream indexes by row now: the strike quads, the TMP line
numbers, the painted-mask cache. That is the whole risk in this change — the
board's five lines are not in enum order (`CatchFish` 2, `ChopTree` 4,
`PlantSapling` 5, `PlaceBonfire` 7, `EatCookedFish` 3), so anything still
assuming `row == (int)enum` puts the strike-through across the wrong line. One
ordered array, `Rows`, is the single source of that mapping.

Also: `AllCompleteOf(subset)` so the board can dim itself when *its* lines are
done rather than waiting on three the box never asks for, and the axe/bottle
poll only runs on a board that lists it.

The tutorial's five, in order:

```
Catch a fish
Chop down a tree
Plant a sapling
Build a bonfire
Cook a fish on a bonfire and eat it
```

### 4. The rod

`TutorialSnapshots.SnapshotFishingRod()` → `TutorialFishingRod.prefab`, the same
mechanism already used for the player, the helmet config and the spawners. Saved
**world-scaled**: the prop hangs off a parent chain that scales it and a prefab
keeps only the source's *local* scale, so the asset's root is rewritten to the
source's `lossyScale` at identity pose. The prefab then means "the rod, actual
size" and the builder only has to place it.

**Its pose survives a rebuild.** Where the rod sits inside the shuttle is
hand-placed, eyeballed work, and *Build Tutorial Scene* wipes the scene. So
`CaptureRodPose()` reads the rod out of the tutorial scene as it stands on disk
(opening it additively read-only if it isn't open, never saving) *before* the
wipe, in the shuttle's local space, and puts it back after. First build only, it
rests on the bunk — measured from the mattress renderer, not hardcoded.

### 5. The tutorial scene had no HUD

The blocker nobody would have guessed from the ask. `Tutorial.unity` shipped with
one canvas holding a crosshair. `BuildMenuUI` is a **scene object** in
`1.6.7.7.7.unity` — it cannot be auto-created, because its blueprint catalogue is
Inspector-authored prefab references — and the bonfire's cook panel is another.
Without them, three of the five objectives are unreachable, and so is planting a
sapling (`SaplingPlanter` resolves its prefab out of `BuildMenuUI.buildables`).

`BuildMenu`, `CookPanel` and `BonfirePromptText` are all direct children of
`HUD_Canvas`, 21 children and 110 descendants — so one snapshot
(`TutorialHUDCanvas.prefab`) carries every cross-reference between them intact,
and brings the fish-catch card, the pickup/place prompts, the resource readout
and the fade overlay along with it.

**The bonfire UI owner.** The cook panel is shared: the first
`BonfireInteraction` with a `cookPanel` assigned builds the panel's contents in
its `Start` and publishes the refs through `BonfireUIRegistry`; a bonfire placed
from the build menu is handed its `cookPanel` *after* `Start` and so never builds
anything itself. The box has no bonfire to be that first one, so it gets an owner
with no world presence — a bare GameObject on the UI root carrying the same
component, wired to the canvas's own panel and prompt. No collider, so it never
goes in range and never prompts. **This is not a tutorial-only code path**: the
arrangement is exactly the gameplay scene's, with the source fire's world half
left off.

## The chain, and whether it closes

One tree drops 8–20 wood (`SpawnedTree.woodReward`) and 1–3 saplings. A bonfire
costs 15 wood, a torch 5. So: chop one tree → usually enough for the fire, and a
sapling in hand → plant it, place the fire, cook the fish. Occasionally a second
tree. That reads as a chain rather than a grind, so no numbers were touched.

`NewGameReset.Apply()` calls `TutorialGate.UnlockAll()`, which the tutorial runs,
so the rod pickup and the N key are both live in the box.

## Two bugs the first build run surfaced

Both found by running the builder and reading its output, not by reasoning:

- **The rod grew 1.2× on every rebuild** (1.000 → 1.200 → 1.440 → …). The
  shuttle is scaled 1.2, and the pose capture stored `lossyScale` (world) and
  set it back as `localScale` (parent-relative), so the shuttle's scale was
  re-applied each pass. It was also 20% oversized on the very first build for
  the same reason. `SetWorldScale` divides the parent's scale out. Round-trip
  verified: an off-axis position with a three-axis rotation came back 0.2 mm and
  0.000° off.
- **The fish line read "Catch a fish (rod's in Tev's cabin)"** — true in the
  gameplay scene, a wild goose chase in a box with no cabin. Hence
  `lineOverrides`, a positional per-board string array (blank = the shared
  label), so a board can reword a line without a code change.

## Files

| File | Change |
|---|---|
| `Tutorial/FeatureVault.cs` | `BasicBuilding` + the two derived properties |
| `Building/BuildMenuUI.cs` | open guard, `BasicBlueprints`, `IsBlueprintOffered`, row + tab filtering |
| `UI/PhoneApps/PhoneBuildApp.cs` | row filter, category cycle skipped at the basics tier |
| `UI/PlayerPhoneUI.cs` | Build tile present at either tier |
| `Tutorial/OrientationObjectives.cs` | `PlaceBonfire`, `Count` 8, `AllCompleteOf` |
| `Building/GhostPlacement.cs` | ticks `PlaceBonfire` |
| `Tutorial/OrientationObjectivesScreen.cs` | `shownObjectives` + row-indexed rendering |
| `Editor/TutorialSnapshots.cs` | rod + HUD canvas snapshots, `NormalizeRoot` |
| `Editor/TutorialSceneBuilder.cs` | rod placement + pose preservation + `SetWorldScale`, HUD canvas + `StripOrphanedHudPanels`, bonfire UI owner, the five lines + per-line overrides |

## Known gaps / what to watch in the playtest

- ~~**Null-ref spam from the borrowed HUD.**~~ **This happened, and it was worse
  than predicted — fixed 2026-09-16.** Not null-refs: five panels were authored
  ACTIVE and drew straight onto the screen. They are normally switched off in
  the `Start()` of whatever owns them (`FishMarketNPC.Start` hides `SellPanel`
  and `TalkPrompt`; the NPC dialogue scripts hide `DialogueText`), and the box
  has no vendors and no NPCs, so nobody ever hid them. Sam loaded the tutorial
  to a fish-vendor panel and a TMP placeholder reading "New Text".
  `StripOrphanedHudPanels` now switches off `SellPanel`, `EarningsText`,
  `DialogueText`, `TalkPrompt` and `CassetteText`, and LOGS every child left on,
  so the next one arrives in the console instead of on screen.
  **The general lesson: borrowing a canvas borrows its authored ACTIVE state,
  and in the source scene that state is a lie maintained at runtime by objects
  the borrowing scene hasn't got.** Checking for null refs was the wrong check.
- **The box now has HUD it never had** — `ResourceHUD` (wood / sapling counts,
  which "place a bonfire, 15 wood" needs) and `BoostMeters`. Both are the real
  gameplay HUD, not leftovers, and neither is a helmet cluster (those are
  `VitalsHUD` / `GForceHUD` / `CompassHUD`, all auto-singletons). Add them to
  `OrphanedHudPanels` if the box should stay barer than the game.
- **A placed bonfire's buttons drive the UI owner, not itself.** That is how
  placed bonfires have always worked in the gameplay scene (only the first
  bonfire's instance fields are bound to the panel), so the tutorial matches
  shipping behaviour. It works; it is worth knowing before anyone "fixes" it.
- Nothing ends the tutorial when the five lines are done. The board dims. A
  finish beat is the obvious revision 2.
