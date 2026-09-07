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

---

# Phase 2 — Fuel (built 2026-09-07)

**Status: built, compiles clean (0 warnings), install verified in the Editor. Not playtested.**

## The rule

**A full tank buys exactly one 15 km jump and leaves you empty when you land.**

- Tank: 100 units. One crystal = 5 units, so **20 crystals is a full tank**, the same
  conversion the manual ship uses.
- Every hop pays a flat **5 units to launch and land**, plus 6.33 per km.
- You're charged **once**, when you press TRAVEL, from the gap at that moment.
  Planets keep moving during the flight and you're never re-billed. A jump that
  starts always finishes — you can't run dry in transit.
- Picking the planet you're already on (relocating) costs the flat 5 only.

## What you'll see

- The NAV app has a **FUEL 62% · RANGE 8.6 KM** line above the planet list.
- Planets you can't afford are **dimmed**, and instead of just a distance they say
  `18.9 KM · IN RANGE ~4 MIN` — computed exactly off the orbits, not guessed.
  Ones that can never be reached at your current range say `OUT OF RANGE`.
- The TRAVEL button reads **NOT ENOUGH FUEL** on a dimmed planet, and tells you the
  shortfall if you click it anyway.
- The **Reactor** is inside the shuttle at local `(1.26, 1.67, -0.05)`. Walk up
  holding crystals, look at it, press F / pad X — same prompt as the ship's.
  **Move it wherever it looks right** — it's a bare trigger with no model yet.

## New game

Starts at **50** fuel; the intro approach burns **12** across the descent, so you
touch down on Humble Abode with about **38 — a range of ~5.2 km**. That's deliberately
not enough to go anywhere good. From Humble Abode at 5.2 km you can still reach seven
of the eight dwarfs, but only in short windows every ~6 minutes — so the first thing
the game teaches is "go get crystals".

## Crystals now grow back

They didn't before — a mined crystal was gone from that save forever. With fuel
gating travel, that meant every planet you harvested slowly became a trap. Mined
cells now regrow after **1 in-game day (24 real minutes)**, and the timer is saved,
so reloading doesn't restart it. Knob: `CrystalSpawner.respawnGameDays` (0 = back to
mined-forever).

## Where you can actually go

Full detail in `docs/DISTANCE_TABLE.md`, regenerate with
**Tools ▸ Solar System ▸ Write Distance Table**. The short version at 15 km:

- **Humble Abode is the hub.** It orbits *backwards*, so it lines up with everything
  every ~6–7 minutes. Nothing is ever more than about 3 minutes away from it.
- **The twins are the safe starter neighbourhood** — Icey↔Fiery are locked 1,029 m
  apart permanently, and both reach Puddle, Hearth, Anvil and Ember at any time.
- **Dwarf-to-dwarf is slow.** They all orbit at similar speeds, so Shard↔Pebble can
  make you wait 3 hours. Route through Humble Abode instead.
- **Cyclops is an expedition.** It can only be reached from Humble Abode (a ~76-second
  window every 8.6 min), or from Pebble or Bruise — the two waterless rocks. Nothing
  else in the system can ever reach it at 15 km.
- No planet is a dead end.

## Tuning knobs

All on the `ShuttleFuel` component on `Shuttle_Lander`:
`fuelMax` · `maxJumpKm` · `launchLandCost` · `newGameFuel` · `introApproachBurn`.
`unitsPerKm` is derived, never typed in, so "full tank = one max jump" can't drift
out of sync when you change a number.

## Co-op

One shared tank, owned by the host. Either player can feed it crystals — a guest
spends their own crystals and the credit is sent to the host, so it can't be wiped
by the next sync. Guests see the same greyed-out planets the host does.

## What to watch for in the playtest

- The Reactor trigger is invisible and 1.4×2×1.4 — it may be somewhere awkward.
- The 5.2 km starting range may feel too tight, or too generous. It's one number.
- "IN RANGE ~N MIN" should count down and be right. If a planet un-greys before or
  after it says it will, that's the orbit maths and I want to know.
- Crystal regrowth is on a 24-real-minute timer, so you won't see it in a short test.
