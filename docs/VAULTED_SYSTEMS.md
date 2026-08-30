# Vaulted systems

Nothing here failed and nothing was deleted. Each system was switched off on
**2026-08-09** to keep the multiplayer baseline small while world-state
replication is built. Each comes back the same way.

## How to restore any of them

1. Flip its flag in `Assets/3 - Scripts/Tutorial/FeatureVault.cs` to `true`.
2. Drag the prefab from `Assets/1 - samsPrefabs/_Vaulted/` into
   `1.6.7.7.7.unity`, parented to **`Humble Abode`**.
3. Set its **local** position from the table below (they are body-relative —
   Humble Abode orbits, so world coordinates would be wrong by the time you
   read them).
4. Save the scene.

## Why prefab-and-delete, not "set inactive"

An inactive GameObject **still loads its meshes, textures and audio** with the
scene. The concert was vaulted specifically because it is heavy on a machine, so
leaving it in the scene disabled would have kept most of the cost. Removing the
instances gives back the CPU, GPU and scene-load memory; the prefab keeps the
work.

## What is vaulted

| Object | Prefab | Parent | Local position | Flag |
|---|---|---|---|---|
| `STAGEGOOD` | `STAGEGOOD.prefab` | `Humble Abode` | `(153.5, -125.9, -92.2)` | `ConcertVenue` |
| `STAGEGOOD2` | `STAGEGOOD2.prefab` | `Humble Abode` | `(-128.6, 46.9, 176.3)` | `ConcertVenue` |
| `AudienceZone` | `AudienceZone.prefab` | `Humble Abode` | `(101.4, -127.0, -126.7)` | `ConcertVenue` |
| `AudienceZone 2` | `AudienceZone_2.prefab` | `Humble Abode` | `(-80.0, 83.3, 165.3)` | `ConcertVenue` |
| `SHIPSCHOOL` | `SHIPSCHOOL.prefab` | `Humble Abode` | `(111.1, 97.5, 138.0)` | `ShipSchool` |
| `Tevsship` | `Tevsship.prefab` | `Humble Abode` | `(146.4, -108.9, 112.3)` | `TevCabinAmbush` |
| `TEV2` | `TEV2.prefab` | `Humble Abode` | `(78.1, 55.0, 178.1)` | `VillageTev` |

Both stages carried the full rig — cone lights, strobes, lasers, blinders, fog,
haze and speakers — so `STAGEGOOD` / `STAGEGOOD2` take the whole concert with
them. Verified after vaulting: **zero** objects carrying a `Concert*`,
`Audience*` or `SpeakerSource` component remain in the scene.

## ⚠️ Vaulting `Tevsship` also vaulted the smuggling mission

`Tevsship` carries **`TevSmugglingMission`** (the B-1 interrogation-and-chase
beat) as well as `SCARE/TevScareTrigger`. Restoring the ship restores the
mission with it; they are one hierarchy. That was implicit in "remove Tev's ship
outside his cabin", but it is a bigger feature than the jumpscare alone, so it
is called out here rather than being a surprise later.

## Code-only vaults (2026-08-14, cassette-loop Phase 6)

Unlike the scene objects above, these are switched off by a flag alone — nothing
was removed from the scene, so restoring them is one `bool`.

| System | Flag | Gated at |
|---|---|---|
| Freeform building — menu, catalogue, phone Build app | `FreeformBuilding` | `BuildMenuUI.Open()`, `PlayerPhoneUI.BuildAppsPage()` |
| Grow Pot + Bubble Dome build entries | `FreeformBuilding` | `GrowPotRegistrar` / `DomeBuildRegistrar` |
| The cabin tutorial step | `FreeformBuilding` | `TutorialSteps.BuildStepList()` |
| Every level track, its toast, ceremony and phone page | `LevelSystem` | `PlayerProgress.Add()`, `PlayerPhoneUI.PageCount` |

### ⚠️ Building's vault does NOT gate placement, on purpose

`GhostPlacement` and `BuildMenuUI.StartPlacementFromPhone` stay live. Planting a
sapling or a mushroom never opens the build menu — both planters call
`StartPlacementFromPhone` directly off the hotbar selection and drive the ghost
themselves. **Gate the ghost and you silently kill replanting**, which the plan
explicitly keeps. `BuildMenuUI` also still holds its `buildables` list, because
that is where the planters resolve their prefabs from.

### ⚠️ The cabin tutorial step had to go with it

`OpenAndBuildCabinStep` waits on `BuildMenuUI.OnOpened`, which can never fire
once the menu's `Open()` early-returns. Left in the active list it is a hard
soft-lock — the tutorial stops on "Press N to open the build menu" and no key
will do it. It is re-inserted after `ChopWoodStep` by search, not by index, if
the flag ever goes back on.

## Code-only vaults (2026-08-14, the rent revamp)

`Handoff_RentRevamp_PhysicalPrint_v1.md` cut Tev down to two jobs — landlord and
music store. Everything that made him a *supplier* is vaulted, not deleted.

| System | Flag | Gated at |
|---|---|---|
| The 50/50 fronting loop — pitch, front, skim, per-player debt | `TevFrontingEconomy` | `TevMushroomOnboarding.PlaySequence()` |
| "Did you sell any of my tapes?" return talk + the refront ladder | `TevFrontingEconomy` | same switch |
| Tev's 3 demo prints (SLUDJ / CHIRP / DRIFT) | `TevFrontingEconomy` | nothing calls `TevDemoTapes.Grant` on a live path |
| The lawn work-off haggle (10 / 8 / 5 / 3 tapes) | `TevLawnWorkOff` | `TevMushroomOnboarding.PlaySequence()` → `RunFirstTalkWorkOff` |

### Why fronting went

The customers were **his**. `BuyerLedger` bond, threads and "songs heard" all
accrue from what a contact bought, so under fronting every early buyer's taste
was shaped by Tev's demos — the player's own career was being steered toward
someone else's sound before they had written a note. And 50/50 was mushroom
logic: caps are fungible, songs are not.

### What replaced it

The **daily money rent** from Aug 8, reactivated rather than rebuilt:
`$50 → $30 → $20 → $10` per game day, haggled on the first talk, floor never
reaches free. `MushroomQuest` owns the ledger, `TevRentCollector` bills it once
per `GalaxyTime` day, and **nothing is auto-deducted** — the player pays through
`TevPaymentUI`. Five days behind and `TevShopUI` refuses the PLUGINS tab while
BLANK TAPES stays open, so the loop can never soft-lock.

`TevFronting.cs`, `TevDemoTapes.cs` and every save field they use still compile
and still round-trip. `TevPaymentUI` keeps its `TevFronting.PlayerState`
overload as a thin wrapper over the debt-agnostic one, so restoring the flag
needs no change there either.

The lawn counters (`tevLawnTapesOwed` / `tevLawnCleared`) stay in the schema, so
a save written under either rule still loads.

### Levels are gated at one choke point

`PlayerProgress.Add` returns early. All ~50 `AddTreeFelled` / `AddEnemyKill` /
`AddStructurePlaced` call sites still compile and still run — they just score
nothing, so no track moves, no toast fires and no ceremony queues. **The save
fields are untouched**, so a vaulted run and an unvaulted one round-trip through
the same schema and un-vaulting invalidates nobody's file.

## Code-only vault (2026-08-30, the first-meeting revamp)

`Handoff_TevDialogue_FirstMeeting_v1 (1).md` retired Tev's landlord job: he is a
music-store owner who sells the TRAX engine for $20 through the new
first-meeting tree. **The entire rent system is vaulted behind
`FeatureVault.TevRent`**, not deleted.

| System | Gated at |
|---|---|
| First-talk lawn opener + the $50→$30→$20→$10 rent haggle | `TevMushroomOnboarding.PlaySequence()` — `!TevRent` routes to `RunFirstMeeting`/`RunMeetingHub` instead |
| Landlord loop: rent nag tiers, pay/refuse choice, `TevPaymentUI.OpenForRent` handoff, payment-outcome lines | same switch (`RunLandlordTalk` unreachable) |
| Daily accrual + "RENT — $N owed" notices | `TevRentCollector.HandleDayChanged` early-outs (gated at the handler, so the `EnsureGameplaySingletons` seeding path in builds is covered too) |
| The 5-day PLUGINS-tab lockout | `MushroomQuest.PluginsLocked` hard-false — the single choke point all four `TevShopUI` lockout sites read through |
| Day-recap "rent: …" line | `DayRecapDirector` passes `rentOwed = -1`, which `DayRecap.Compose` already omits |

Traps for whoever restores it:

- **`verify-rent` stubs `FeatureVault.TevRent = true` on purpose**
  (`prototypes/shuttle-computer/test/RentDeckStubs.cs`) — the suite is the
  living documentation of the rent arithmetic, so it tests the system AS BUILT,
  not the shipping flag. Don't "fix" the stub to match FeatureVault.
- The rent counters stay in `StoryDirector`'s save lists; old saves load fine.
- The new tree never advances `MushroomQuest.Stage` — `TevMet` (StoryDirector
  flag `tevMet`, plus a legacy clause reading old saves' stage) replaced it. If
  you restore rent, the stage machinery resumes exactly where the old code left
  it.
- `TevMushroomOnboarding.ShouldBeVisible` gained a `tevMet` clause mirroring
  the old stage check — keep both or a warm reload re-hides a met Tev.

What replaced rent as the money sink: nothing yet — TRAX itself is the first
purchase ($20, USB stick, installed at the shuttle computer; see
`TraxLibrary.IsAppInstalled` and the DOWNLOADING flow in `ShuttleComputerUI`).
Starting funds: $25 seeded into the shuttle locker (`LootBoxStarterItem` on
`Locker_2`, patched into `Shuttle_Lander.prefab`).

## Explicitly NOT vaulted

- **Tev himself**, at his cabin (`TEV`, 9 m away) — he carries
  `TevMushroomOnboarding`, which since 2026-08-14 is the **rent haggle + the
  shop**, and he owns daily rent collection. Only `TEV2` (203 m from the cabin,
  67 m from the village) was vaulted. The two were told apart by distance, not
  by name.
- **The blank-tape shop.** `TevShopUI`'s BLANK TAPES tab is never gated by
  anything, at any debt level. That asymmetry is the design: the ladder freezes,
  the treadmill doesn't.
- **`TevFamilyPhoto_Prop`** inside `StartCabin` — cabin dressing.
- **The village** — kept as scenery; it will be given a purpose later.
- **`Max Audience`** in the settings UI — an inactive settings row costing
  nothing. Left so the settings panel's layout and bindings are untouched.
- **`Assets/3 - Scripts/Concert/`** — the whole script folder still compiles.
  Only the scene instances were removed, so the flags and code are ready the
  moment the prefabs go back.
