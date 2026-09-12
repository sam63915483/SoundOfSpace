# Village bar: Shlawg, beer, cups — design

🟢 ACTIVE — built 2026-09-12 (two passes), **playtest pending**. Sam's ask: House_03
becomes a bar. Shlawg (Alien1) stands behind a counter, sells a beer for $10,
puts it in the middle of the counter, you F it into the hotbar, hold left click
to drink it, and set the empty cup down anywhere on the counter top, where it
fades out. You can carry several beers; he only refuses while a full one is
still waiting in the middle.

## Scene (House_03, Humble Abode)

| Object | Components | Set by |
|---|---|---|
| `House_03/counter` (a scaled cube, Sam's) | `BarCounter` (cupPrefab = CupGOOD, fadeMaterial = `2 - Materials/Bar/BeerCupFade.mat`) | wired 2026-09-12 |
| `House_03/BARTENDER` (empty, Sam's, blue arrow toward the counter) | `AuthoredNPCSpawner` (npcName Shlawg, wander off, autoSpawn off, **prePlacedBody = Shlawg**) + `BartenderTalk` (counter ref) | wired 2026-09-12 |
| `House_03/BARTENDER/Shlawg` | Alien1.prefab instance, world scale 3.5 — **Sam fine-tunes position/rotation in the Editor** | placed 2026-09-12 |
| `--- Player & Ship ---/Player` | `BeerCupController` (cupPrefab = CupGOOD) | wired 2026-09-12 |

No cup-spot marker: the middle of the counter and the placeable area are read
off the counter's own mesh bounds (top face = local `bounds.max.y`).

## Pre-placed NPC mode (new)

`AuthoredNPCSpawner.prePlacedBody` — when set, `Start()` adopts that scene
object instead of spawning: talk trigger box, `AuthoredNPCBody` relay,
`NPCWaveAnimation`, WorldProp layer. No ground raycast, no `NPCSeating`, no
`AlienWander` (`Wander` stays null; every caller null-checks). Use it for any
NPC that must stand exactly where it was placed (behind a counter, indoors).
The random-yaw / `faceMarker` path is only for spawned bodies.

## The loop

| Step | Who | What happens |
|---|---|---|
| Talk | `npc_shlawg.json` | `start` routes to `waiting` if `cupOnCounter`, else "What'll it be?" → "A beer. ($10)". `MoneyAtLeast 10` → `SpendMoney 10` + `Custom pourBeer`. |
| Pour | `BartenderTalk.GraphAction("pourBeer")` → `BarCounter.PourBeer()` | CupGOOD spawned at the top-face centre with a full `BeerLiquid` and a `BeerCupPickup`. Parented to the counter's PARENT (the counter is a non-uniform cube) and sized in world metres. |
| Pick up | `BeerCupPickup` | F: `BeerCupController.AddCup(100)`; equips if the hand is free. The Hotbar's BEER slot appears (or its count goes up). |
| Drink | `BeerCupController.Update` | Hold left click: cup floats up and tips (bottle's pose channel), beer drains at 35 %/s, thirst +30 per cup, burp at the bottom. |
| Set down | `BarCounter.Update` / `Interact` | Holding an EMPTY cup and the crosshair hits the counter: counter tints green (`MaterialPropertyBlock _Color`), a translucent green ghost cup follows the hit point snapped onto the top face (kept 12 cm inside the edges). F: `RemoveCurrentCup()` — the next beer pops into the hand, or the slot goes — and an empty CupGOOD is placed there. |
| Fade | `BeerCupFadeAway` | 5 s later the empty cup swaps to an instance of the Fade material carrying its own texture, alpha → 0 over 1 s, destroyed. |

## Multi-cup model

`BeerCupController` keeps `List<float>` fills; index 0 is the cup in hand.
`IsUnlocked` = count > 0 (registry adds/evicts the slot), `CupCount` is
mirrored onto the slot's count badge by `Hotbar.DetectAcquisitions`, and the
count text shows when > 1. Saved as `EquipmentSave.beerCups` (+ `beerCupEquipped`).

## Decisions taken (change any)

- $10, +30 thirst, ~3 s drink, 5 s before fade, 1 s fade, 12 cm edge margin.
- No buff / drunk effect (Grogginess is the ready-made camera if wanted).
- The Fade material is a real asset so Standard's `_ALPHABLEND_ON` variant is
  in the build (a `Shader.Find` fallback exists for the Editor only).
- Empties placed on the counter have no colliders (decorative); the full cup
  keeps its mesh collider so the crosshair resolves it.
- Counter contents are not saved; the player's cups are.
- Co-op partners do not see the cup in your hand (`HeldItemResolver` gap, same as the grapple).

## Village un-bake

`Tools ▸ Village ▸ Un-bake Village (edit houses)` / `Re-bake Village (after
editing)`. Un-baked while Sam edits houses; re-bake + `Refresh Building
Exclusion Zones` when done.
