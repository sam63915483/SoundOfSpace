# Village bar: bartender, beer, cup — design

🟢 ACTIVE — built 2026-09-12, **playtest pending**. Sam's ask: turn one village
house into a bar, a bartender behind a counter you can talk to and buy a beer
from, the beer appears on the counter, F picks it up into the hotbar, hold left
click to raise it to your mouth and drink it down, and when it is empty you can
set the cup back on the counter. The village was un-baked first so the houses
can be resized.

## What Sam places in the scene (by hand)

1. **The counter.** Any prop under the planet, inside the chosen house
   (`CartoonMedievalTown/Prefabs/Props/BarCounter_01..03` match the village
   style). Add **`BarCounter`** to it. Optional child empty **`CupSpot`** where
   a cup should stand, dragged into the `cupSpot` field; without it the cup
   stands on the counter's pivot.
2. **The bartender.** An empty parented under the planet, behind the counter,
   blue arrow pointing across the counter. Components:
   `AuthoredNPCSpawner` (`npcName` = `Bartender`, `borrowPrefabIndex` = any
   0-9, `scale` ≈ 3.5, **`wander` = off**, **`faceMarker` = on**) +
   **`BartenderTalk`** (drag the counter into `counter`, or leave empty and
   it finds the nearest one).

`npcName` `Bartender` → the talk reads `StreamingAssets/Story/npc_bartender.json`
(Dialogue Studio edits it live in the Editor).

## The loop

| Step | Who | What happens |
|---|---|---|
| Talk | `npc_bartender.json` | "What'll it be?" → "A beer. ($10)". `MoneyAtLeast 10` gate → `SpendMoney 10` + `Custom pourBeer`. |
| Pour | `BartenderTalk.GraphAction("pourBeer")` → `BarCounter.PourBeer()` | Any cup on the spot is removed; `Cup.prefab` is spawned there with a `BeerLiquid` (full) and a `BeerCupPickup`. |
| Pick up | `BeerCupPickup` (Interactable) | Look at it, F: `BeerCupController.Unlock()` + `SetFill(100)` + equip. The Hotbar's registry adds the BEER slot next frame. World cup destroyed. |
| Drink | `BeerCupController.Update` | Hold left click: the cup floats up and tips (the bottle's `ViewmodelMotor` pose channel), the beer column shrinks at 35 %/s (~3 s), thirst +30 per cup. Empties → burp. |
| Empty | `BeerCupController` | Left click on an empty cup shows a one-shot hint. The slot stays. |
| Set down | `BarCounter` (Interactable) | Look at the counter while holding an EMPTY cup, F: `Lock()` (unequip + hotbar evicts the slot), an empty `Cup.prefab` stands on the spot until the next pour. |

The bartender's `start` node routes on three probes before greeting:
`cupOnCounter` → "Your beer's right there", `cupEmptyInHand` → "Set it down and
I'll pour another", `holdingCup` → "Finish that one first". So there is never
more than one cup in play.

## The pieces

- `Pickups/BeerCupController.cs` — on the SCENE Player (added component, like
  every equippable). Clone of `WaterBottleController` minus the water refill.
  Public: `hotbarIcon`, `cupPrefab`, `IsEquipped`, `IsUnlocked`, `FillPercent`,
  `IsEmpty`, `Unlock()`, `Lock()`, `SetFill()`, `ForceEquipCup()`,
  `ForceUnequipCup()`. Knobs: held size/rotation, liquid interior numbers,
  consume rate, thirst, drink pose (raise offset, tilt, bob).
- `Pickups/BeerLiquid.cs` + `Pickups/BeerCupArt.cs` — the beer is two
  primitives (amber column + cream foam disc) built inside the cup mesh at the
  interior numbers measured from `Cup.fbx` (bore axis x≈0.005, floor y≈0.035,
  full surface y≈0.19, inner wall r≈0.07). `SetFill(0-1)` shrinks the column
  from the top. Same visual on the counter and in the hand. `BuildIcon()` draws
  a pixel tankard for the hotbar so no sprite asset can go missing in a build.
- `Pickups/BeerCupPickup.cs` — `Interactable`, added at runtime by the counter.
- `World/BarCounter.cs` — `Interactable`; `All` list; `PourBeer()`,
  `HasFullCup`, `HasEmptyCup`, `NotifyCupTaken()`.
- `NPC_Dialogue/BartenderTalk.cs` — `AuthoredNPCTalk` subclass: 3 probes, 1 action.
- `World/AuthoredNPCSpawner.cs` — new `faceMarker` (appended at the end):
  spawn facing the marker's forward instead of a random yaw.
- `UI/Hotbar.cs` — `ItemId.BeerCup` (= 28, appended), field, `ResolveRefs`,
  `RegistryNeedsRebuild`, `BuildRegistry` row "BEER". `FishStagingUI` /
  `StorageUI` icon switches.
- Save: `EquipmentSave.beerCupEquipped / beerCupUnlocked / beerCupFill`,
  captured/applied beside the grapple in `SaveCollector`. A half-drunk cup
  survives a reload. **The counter itself is not saved** — a paid-for beer left
  standing on the bar is gone after a reload (it is $10; not worth the
  apply-order risk).
- Dialogue Studio: `vocab.json` probes/actions + `roster.json` card.

## Decisions taken without asking (change any of them)

- Price **$10**, thirst **+30**, drink time **~3 s**. All in the JSON / Inspector.
- Beer has **no buff and no drunk effect**. `GrogginessImageEffect` (the
  intro's blur + double vision) is a ready-made drunk camera if wanted later —
  a 60 s wobble driven off a `FireflyGlow`-style timer would be ~1 file.
- The cup is the FantasyVillage **`Cup.prefab`** (same pack as the houses);
  `FullCup.prefab` is unused because its beer is baked into the mesh and could
  not drain.
- The cup floats on the viewmodel motor like the bottle. **No arm animation** —
  CLAUDE.md records that as a ripped-out experiment.
- Co-op: partners do not see the cup in your hands (`HeldItemResolver` case
  not added — same gap the grapple gun has).

## Village un-bake

`Tools ▸ Village ▸ Un-bake Village (edit houses)` re-enables every house
renderer and deletes the welded copy; `Re-bake Village (after editing)` combines
it again. The un-bake was run this session; the scene needs saving. After
moving or scaling houses also run `Tools ▸ Village ▸ Refresh Building Exclusion
Zones`, and re-bake before a perf pass — un-baked the village costs its old
draw calls again (it was 2 draw groups).
