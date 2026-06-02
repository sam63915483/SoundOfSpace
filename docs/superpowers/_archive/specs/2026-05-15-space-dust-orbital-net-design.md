# Space Dust & Orbital Net — Design

**Date:** 2026-05-15
**Status:** Approved for implementation planning

## Summary

Add a new currency-flavoured resource — **Space Dust** — gathered passively by ships parked in orbit and sold to any NPC in the game (vendors + random aliens). The player buys a **square filter attachment** from the Ship Vendor to unlock dust gathering. Each ship the developer wants to be dust-capable gets a manually-placed **SpaceNet** GameObject that runs the orbit check and buffers gathered dust. The player walks up to the net on the ship (F prompt) to collect into their global dust inventory. Selling uses a shared UI with rolled-per-conversation accept % and price-per-dust, with failed rolls returning the dust to the player ("try again").

## Goals

- Give parked / orbiting ships a passive purpose beyond being a vehicle.
- Add a new universal sell-flow that touches every NPC in the game without breaking their existing dialogue.
- Surface randomness and risk/reward (variable accept %, variable price) without ever destroying the player's resource on a fail.

## Non-Goals

- Not a science / research / upgrade system. Dust is currency-flavoured; it sells for credits.
- No multi-stage refinement, no crafting recipes from dust.
- Filter is not a per-ship part / pickup. It is a single global unlock.
- No new map UI / orbit-tracking screen. Existing map / compass do not change.

## Player Loop

1. Fly to the Ship Vendor and buy the **Filter Attachment** (one-time global unlock).
2. Park a ship in space (unmanned, near a planet/moon, off the ground). Walk away.
3. The ship's SpaceNet quietly accumulates dust into a per-ship buffer (capped).
4. Return to that ship → walk up to the net → press F → dust transfers from the net buffer to the global Space Dust inventory.
5. Talk to any NPC → pick "Sell space dust" → see this conversation's rolled price and accept % → choose quantity → SELL. Success: paid in credits. Fail: dust stays, "no thanks", same NPC keeps the same roll until conversation ends.
6. Walk away / talk to a different NPC → new rolls.

## Architecture

### New Components

| Type | File | Responsibility |
|---|---|---|
| Singleton resource | `SpaceDustInventory.cs` | Tracks `playerDust` count + `hasFilter` unlock. Auto-creates via `RuntimeInitializeOnLoadMethod` like `WoodInventory`. Save-system integrated. |
| Per-ship MonoBehaviour | `SpaceNet.cs` | Runs orbit check, accumulates `_buffer` over time, exposes F-prompt trigger, transfers buffer → inventory on collect. Author drops this GameObject on each ship that should gather dust. |
| UI panel | `SpaceDustSellUI.cs` | Procedural sell panel: quantity slider, rolled price/unit, rolled accept %, total preview, SELL/CANCEL. Singleton, built on first open. |
| Per-NPC dialogue helper | `NPCSellDustOption.cs` | Holds the rolled `acceptChance` + `pricePerDust` for a single conversation. One instance per NPC instance. Provides "open sell UI" + "roll fresh values" methods. Used by all 4 NPC sites. |
| HUD addition | (edit `PlayerWallet.cs`'s wallet card) | New "DUST" row appears on the wallet card when `count > 0` OR `hasFilter == true`. |

### Modified Files

- `Vendor/ShipMarketNPC.cs` + `ShipMarketShopUI.cs` — add `ShopItemKind.SpaceDustFilter` handling. Granting calls `SpaceDustInventory.SetFilterUnlocked(true)`.
- `Vendor/ShopItem.cs` (`ShopItemKind` enum) — add `SpaceDustFilter` value.
- `Vendor/Alien7Vendor.cs`, `Vendor/ShipMarketNPC.cs`, `Fishing/FishMarketNPC.cs`, `NPC_Dialogue/RandomAlienDialogue.cs` — extend their post-greeting flow with the numbered choice panel.
- `SaveSystem/SaveData.cs`, `SaveSystem/SaveCollector.cs` — `SpaceDustSave` schema + capture/apply.

### Orbit Detection (in `SpaceNet.Update`)

A SpaceNet's ship is "in orbit" iff **all** of:
- `SpaceDustInventory.HasFilter == true` — without the filter the net is decorative.
- The owning `Ship.IsPiloted == false` — the player is not flying it.
- `Ship.numCollisionTouches == 0` — the ship is not landed / sitting on anything.
- Distance from the ship to the nearest `CelestialBody.Position` is between `body.radius * 1.05f` (just above surface) and `body.radius * 5f` (within the body's gravitational neighbourhood — same threshold the save system uses for body-relative attachment).

If all hold, accumulate `dustPerSecond * Time.deltaTime` into `_buffer`, clamped to `bufferCapacity`.

### F-prompt Collection

- The SpaceNet's `Awake` auto-adds a trigger `SphereCollider` (radius = `collectionRadius`, default 2.5m) at the net's position, if one isn't already on the GameObject. Mirrors how `Alien7Vendor` works with its `BoxCollider` trigger.
- `OnTriggerEnter/Exit` track `_playerInRange`. While in range AND `_buffer >= 1` → `InteractPromptUI.Show(this, "Press F to collect <N> space dust")`.
- On F press (via `TutorialGate.InteractPressed(TutorialAbility.TalkToNPC)` — re-using the same ability so it's already unlocked by tutorial) → transfer floor int from `_buffer` to `SpaceDustInventory`, spawn a small "+N dust" popup at the net's position (re-use `WoodPopup` text style).

### Filter Purchase

Ship Vendor sells a new `ShopItem` with `kind = SpaceDustFilter`, displayName e.g. "Space Dust Filter", price tuned by author (target ~600 credits per the existing ship-vendor price band). The filter is one-time: `ShipMarketNPC.IsAlreadyOwned(SpaceDustFilter)` returns `SpaceDustInventory.HasFilter`, so the card greys out once bought (matches how Alien7Vendor handles `axeUnlocked` / `pistolUnlocked`). `ShipMarketNPC.Purchase` gets a `SpaceDustFilter` case that debits money via `PlayerWallet.SpendMoney`, then calls `SpaceDustInventory.SetFilterUnlocked(true)`.

### Sell Flow (per-NPC menu)

All four NPC types (FishMarketNPC, Alien7Vendor, ShipMarketNPC, RandomAlienDialogue) get a small post-greeting choice panel inserted between "greeting finishes typing" and their current next-step.

Choice panel renderer: a small procedurally-built panel anchored under the dialogueText, listing numbered text rows. Player presses the digit key OR clicks the row.

| NPC | Choice rows |
|---|---|
| Fish vendor | `1. Sell fish`, `2. Sell space dust`, `3. Leave` |
| Goods vendor (Alien7) | `1. Open shop`, `2. Sell space dust`, `3. Leave` |
| Ship vendor | `1. Open shop`, `2. Sell space dust`, `3. Leave` |
| Random alien | `1. Sell space dust`, `2. Leave` |

If `SpaceDustInventory.Count == 0`, the "Sell space dust" row is greyed and disabled with the suffix `(no dust)`.

### Sell UI behaviour

Shared `SpaceDustSellUI` panel (single instance, procedural like the existing shop UIs):

- Header: NPC display name + line "WILL BUY DUST"
- Rolled values shown big: `<P> credits / dust`, `<A>%  ACCEPT CHANCE`
- Quantity row: slider (1 → player's current dust count, default = full balance) + numeric input box.
- Preview line: `Payout if accepted: <qty × P> credits`
- Buttons: `SELL` (rolls), `CANCEL` (closes UI, returns to NPC choice panel).
- On SELL:
  - Roll `Random.value < A/100`.
  - **Success:** add `qty × P` to `PlayerWallet`, subtract `qty` from `SpaceDustInventory`, toast `"+<credits> credits"`, slider re-clamps to new remaining dust. UI stays open.
  - **Fail:** dust untouched, brief refusal line in the panel (`"Hmm, not today."` from a small array of variants per NPC type), UI stays open. The rolled price + chance stay the same — only conversation-end rerolls.
- Closing the UI returns to the NPC's choice panel so the player can leave cleanly.

### Rolled values per NPC

Stored on each `NPCSellDustOption` component (one auto-attached per NPC instance). Rolled when the NPC's conversation **starts**, persists until the conversation **ends** (player walks out of range OR closes everything).

Recommended ranges (tune in inspector per-type):

| NPC type | Accept % range | Price / dust range |
|---|---|---|
| Vendors (Fish / Goods / Ship) | 55 – 85 % | 3 – 7 credits |
| Random alien | 15 – 75 % | 4 – 18 credits |

Vendors = the safe pick. Random aliens = the jackpot pick.

### HUD

`PlayerWallet`'s card already supports per-row toggling (it handles MONEY + optional AMMO). Add a third optional row "DUST" that appears when either `SpaceDustInventory.Count > 0` OR `HasFilter == true`. Card height grows from `CardHeight2Row` → `CardHeight3Row` (same pattern as the pistol-equipped path); when both AMMO and DUST are present, grow to a new `CardHeight4Row`. Change-detected text update each frame.

### Save System

New `SpaceDustSave`:

```csharp
[Serializable]
public class SpaceDustSave
{
    public int playerDust;
    public bool hasFilter;
    public List<int> netShipNumbers = new();   // ordered: shipNumber per buffer entry
    public List<int> netBuffers     = new();   // dust count per ship (floor-int of float buffer)
    public int sceneShipBuffer;                // sentinel for the scene's main (non-bought) ship
}
```

- Captured in `SaveCollector.CaptureSpaceDust` between extra-ships capture and held-item capture.
- Applied in `SaveCollector.ApplySpaceDust`, run **after** `ApplyExtraShips` so the ship instances exist when we look up their `SpaceNet`.
- The scene's original ship uses the `sceneShipBuffer` sentinel; bought ships use the `netShipNumbers` / `netBuffers` parallel-array pattern (matching the dictionary-flattening style noted in CLAUDE.md).
- `SpaceDustInventory` is added to `MainMenuController.EnsureGameplaySingletons` so it exists before save Apply runs on load.

## Error / Edge Cases

- **Player has no dust and tries to open sell UI** — row is disabled in the menu, so this can't happen via UI. If forced, the SELL button stays disabled with the slider clamped to 0.
- **NPC walks out of range mid-conversation** — current dialogue code already handles `StopConversation`. We just need to also force-close `SpaceDustSellUI` if it's open for that NPC, and reroll the NPC's values for the next encounter.
- **Net's owning Ship is destroyed (damage swap)** — the `SpaceNet` gets destroyed with it. On the next ship instance (e.g. `Ship_NoThrusters` swap-in), the author re-adds a SpaceNet. The save system stores buffers by `BoughtShip.shipNumber` (stable across damage swaps), so on reload the buffer is restored to whichever ship has the matching shipNumber.
- **Filter purchase when already owned** — `IsAlreadyOwned` returns true, card is greyed in shop UI.
- **Player loads save with `hasFilter = true` but no SpaceNet on any ship in the scene** — fine, the filter is unlocked globally; until a ship has a SpaceNet, no accumulation happens.
- **Concurrent F-presses (player at net while inside an NPC trigger)** — InteractPromptUI is owner-keyed (`this` ref); only one prompt at a time. Existing pattern.

## Testing Plan

Manual checklist (no automated tests in this Unity project):

1. **Filter unlock** — buy from ship vendor; verify HUD shows DUST row at 0.
2. **Accumulation** — park a ship in space unmanned, walk away, wait, return — buffer count visible in F prompt.
3. **Buffer cap** — set `bufferCapacity` low in inspector, verify accumulation stops at cap.
4. **Collection** — walk to net, F, dust transfers to inventory, popup plays.
5. **Vendors sell flow** — for each of FishMarket, Alien7, ShipMarket: greet, see choice panel, pick "Sell space dust", confirm rolled values match the vendor ranges, sell partial / full / failed rolls.
6. **Random NPC sell flow** — same on a RandomAlienDialogue NPC, confirm wider ranges.
7. **Sell-fail behaviour** — dust untouched, refusal line shown, can re-attempt.
8. **Walk away mid-sell** — UI closes, NPC's roll resets next encounter.
9. **Existing flows untouched** — fish vendor "Sell fish" path still works, ship/goods vendor "Open shop" still works.
10. **Save round-trip** — save with dust + buffer + filter unlocked, reload, all three persist.
11. **Save without filter** — confirm `hasFilter = false` saved & restored.
12. **Bought ship save** — buy a ship, gather dust in its net, save, reload, dust still in that specific ship's net buffer.
13. **Tutorial gate** — F-to-collect uses `TalkToNPC` ability which is unlocked by tutorial; verify net F prompt works after tutorial completion.

## Open / Tunable Values

These are inspector-level tweaks, NOT design questions:

- `SpaceNet.dustPerSecond` (default 0.1)
- `SpaceNet.bufferCapacity` (default 500)
- `SpaceNet.collectionRadius` (default 2.5)
- Filter price at ship vendor (target 600)
- Accept-% and price ranges per NPC type (table above)
- Refusal-line variants per NPC type

## Build / Author Setup (what the user does manually)

1. Make a `SpaceNet` prefab/GameObject (mesh, visual look — the "square filter" aesthetic).
2. Drop one on each ship prefab / variant they want dust-capable. The `SpaceNet` script auto-adds a trigger collider at runtime, so positioning is purely visual.
3. (Optional) Tune `dustPerSecond` per-ship if some ships should be better gatherers than others.
4. Wire a `ShopItem` asset for the filter (right-click → Create → Game → Shop Item) and drop into the Ship Vendor's `inventory[]`. Set `kind = SpaceDustFilter`, displayName "Space Dust Filter", price 600.

Everything else is code + procedural UI; no other Inspector wiring required.
