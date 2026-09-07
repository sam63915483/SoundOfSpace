# Planet Economy — Phase 1: vendor prefabs (how to place them)

Built 2026-09-07. Companion to `Handoff_PlanetEconomy_Fuel_Fishing_v2.md`.
**Status: built, compiles clean (0 warnings), prefabs verified in the Editor. Not playtested.**

---

## The two prefabs

| Prefab | Path | What it is |
|---|---|---|
| **FishMarket** | `Assets/1 - samsPrefabs/Vendors/FishMarket.prefab` | Market stall + the fish vendor alien + board. Buys fish, sells bait, takes the GRULABU bounty. |
| **GoodsVendor** | `Assets/1 - samsPrefabs/Vendors/GoodsVendor.prefab` | Bakery-style stall + the goods vendor alien + board. Sells supplies (crystals arrive in Phase 2). |

Both were built **from your live Humble Abode vendors**, so a market on a dwarf
planet behaves identically to the one you already have — same greeting, same
typewriter and sale sounds, same bounty lines and $500 reward, same trigger size.

---

## Placing one (about 20 seconds each)

1. Drag the prefab into the Hierarchy **onto the planet** you want it on — it must
   end up as a child of that planet's GameObject under `Body Simulation`.
   That parenting is how the vendor knows which planet it trades on. Nothing else
   to set; there are no per-instance fields to fill in.
2. Move it roughly where you want it (near the water for a fish market).
3. With it still selected, hit **Ctrl+G** — or **Tools ▸ Vendors ▸ Snap to Ground**.
   It drops onto the surface and stands upright on that planet's "up".
4. Nudge and rotate it until you like it. Rotating it is safe; the snap only
   fixes which way is up, and keeps whatever direction you had it facing.

### Where they're needed

**Ten fishable planets** — the four mains plus six dwarfs:

> Humble Abode · Icey Twin · Fiery Twin · Cyclops · Puddle · Hearth · Anvil · Ember · Slag · Shard

**Pebble and Bruise have no water**, so they get no fish market. They're also the
two stepping stones to Cyclops, which makes them the natural spots for your first
couple of goods vendors.

Humble Abode already has both vendors, so that's **9 fish markets** to place.

---

## Gotchas

- **Save the scene.** Building the prefabs also added a `VendorSite` marker to
  your two existing Humble Abode stands so they join the economy alongside the new
  ones. That's an unsaved scene change right now — Ctrl+S to keep it. (Harmless to
  lose; re-running the menu item redoes it.)
- **Snapping on a planet whose terrain isn't built yet** falls back to the planet's
  ideal sphere and logs a warning. You'll get the right neighbourhood and the right
  orientation, just not the exact ground height — nudge it down. Humble Abode is
  fine because its mesh is in the scene.
- **The board is a placeholder.** It reads "Buying today: Local / Imported /
  Delicacy" on every stand. Phase 4 fills it with that planet's real buy list.
  It sits just above the stall canopy — move it wherever reads best, the prefab
  only owes you a sane starting point.
- **Don't mesh-combine a vendor stand.** The Humble Abode one is combined, which
  is why the prefab was built from the original pack asset instead of the scene
  object — combined meshes live inside the scene file, not as assets, so a prefab
  built from one would reference meshes that don't exist anywhere else.
- The stall prefab from the asset pack has one **missing nested prefab** reference
  (`Fish_market_with_objects`). It's an empty transform that renders nothing and
  it's been that way since the pack was imported — not something this change
  introduced.

---

## What changed in code

- **`Vendor/VendorSite.cs`** (new) — on each stand's root. Answers "which planet am
  I on?" by reading the parent hierarchy. Everything in Phases 3 and 4 keys off it.
- **`Vendor/VendorBoard.cs`** (new) — the world-space sign. Renders only within 22 m
  so ten of them cost nothing at range.
- **`Fishing/FishMarketNPC.cs`** — two changes:
  - Its three HUD references (talk prompt, dialogue line, sell panel) used to be
    dragged in by hand in the scene. A prefab can't hold a reference to a scene
    object, so it now finds them itself at runtime — the same contract
    `Alien7Vendor` already used. Hand-wired instances keep what they were given.
  - The sell panel is **one shared HUD panel** used by every market in the system.
    It used to be built in `Start`, which with ten vendors would have stacked ten
    copies of the widgets into it. It's now built once by whoever opens it first,
    with the two buttons re-pointed at whichever vendor you're standing in front of.
- **`Editor/VendorPrefabBuilder.cs`** (new) — `Tools ▸ Vendors ▸ Build Vendor Prefabs`.
  Re-run it any time you change the Humble Abode vendors and want the change to
  reach every planet.
- **`Editor/VendorPlacement.cs`** (new) — `Tools ▸ Vendors ▸ Snap to Ground` (Ctrl+G).

## Not in Phase 1 (coming next)

- Crystals in the goods vendor's stock — lands with Phase 2 (fuel), since crystals
  are the fuel currency.
- Per-planet fish tables, buy lists, delicacies, appetite — Phases 3 and 4. Until
  then every market buys everything at the normal price, which is exactly today's
  behaviour, so you can place all nine now and they'll work.
