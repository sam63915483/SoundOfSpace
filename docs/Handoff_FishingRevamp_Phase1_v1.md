# Handoff — Fishing Revamp, Phase 1 (v1, Sep 1 2026)

Fishing becomes one of the game's two core money loops (with TRAX music). This phase makes the catch itself deeper without adding clicks, replaces the bare common/uncommon/rare roll with 12 named species, adds vendor bait, and makes time of day matter. Bounty fish, bounty zones, and the Floorbin/Shlorbin quest are **Phase 2 — do not build them here.**

**Process rule (CLAUDE.md rule 4):** state your build plan first — file list, which existing scripts you're touching, and any [EXISTS] claim you could not verify — and wait for Sam's go before implementing. Any [EXISTS] line below that turns out to be false gets recorded in the STATUS block, not silently worked around.

**Balance law:** fishing must earn less per real-time hour than selling tapes. You can fish anywhere, any time; TRAX needs the shuttle and creativity, so it pays better. The money revamp already cut fish prices to ⅓ for this reason — keep that spirit. Report the expected $/hour of each loop in the STATUS block (see [TEST] 5).

---

## [EXISTS] — verify before touching

- Fishing: click to cast → bobber sits on the planet water collider (one collider at water level per planet, no regional waters) → bobber shakes on bite → short click window → fish lands. Locate the cast/bobber/bite scripts and the current common/uncommon/rare roll.
- Fish likeness system: 3 fish models per rarity tier (9 total), mesh widened by weight; weights roll ~1–50 lb. Caught fish are hotbar items shown in the floating right hand.
- Fish market vendor exists and buys fish. Note the Aug 22 direction (walk-in stores with a checkout box) — if the fish market is still a UI vendor, build against whatever it is today; don't convert it in this handoff.
- Sun direction is geometric: planets orbit the sun in the n-body sim, and the lit side of a planet is whichever side faces the sun. `GalaxyTime` (24 real min = 24 in-game h) is **deliberately decoupled** from geometric day/night — do NOT use it for fishing. Something already computes a per-body day factor (grass lighting, `EclipseShadowGate` measured from the body underfoot); reuse or mirror it.
- Items: `ItemIds`, hotbar stacking via `CarriesVariant/VariantOf`, storage/locker, shop rows with affordability clamp (Tev shop pattern), money in slot 8.
- Co-op: items are personal; world spawners are deterministic. Nothing in this handoff needs new sync — fish and bait are personal hotbar items. Flag it if that turns out wrong.

---

## [BUILD] 1 — The fight (single tension bar)

After the existing hook click succeeds, enter a **fight state** instead of landing the fish immediately.

- `tension` 0–100. While LMB held: `tension += reelRate × pull(tier) × dt`, `fishStamina -= drainRate × dt`. While released: `tension -= relaxRate × dt`, stamina does not recover (keep it simple; recovery is a Phase 2 knob if the fight feels too easy).
- `tension ≥ 100` → line snaps: fish lost, bait lost, short rod-recoil anim/sound, back to idle.
- `fishStamina ≤ 0` → landed: existing catch flow (fish item, weight, hand model).
- Uncommon and rare fish **run**: at random intervals (every 2–4 s) `pull` doubles for 1–2 s with a distinct rod-jerk + reel-scream sound. Commons never run. Runs are the whole skill — the player learns to let go during a run.
- Default numbers (Sam tunes in inspector; expose all as `[SerializeField]` on a `FishingTuning` ScriptableObject):

| | reelRate | relaxRate | pull | stamina (s of reeling) | runs |
|---|---|---|---|---|---|
| common | 35/s | 45/s | 1.0 | 2.5–4 | no |
| uncommon | 35/s | 45/s | 1.4 | 5–8 | yes |
| rare | 35/s | 45/s | 1.8 | 9–14 | yes, more frequent |

- Weight nudges stamina within the tier (heavier = longer), so a 48 lb rare is a real fight and a 2 lb common is two seconds.
- **UI:** one thing only. A thin arc or bar near the reticle in the existing helmet-HUD style (amber bracket language from the gaze prompt), plus visible rod bend. Bar colour shifts toward red above ~75. No stamina bar, no fish icon, no "zone" mini-game. The fish's stamina is felt through the rod, not read off a meter.
- ESC / releasing for > 3 s with tension at 0 = fish gets off (prevents a stuck state).

## [BUILD] 2 — Sun-angle bite rate

Replace the fixed bite timer with one number computed when the bobber lands:

```
dot = Dot(surfaceNormalUnderBobber, (sunPosition - bobberPosition).normalized)
```

`+1` = noon, `0` = sunrise/sundown, `−1` = midnight. Time-to-bite = `baseWait × waitMult(dot)`:

- `|dot| ≤ 0.25` (twilight band) → **0.5×** (best fishing)
- `dot < −0.25` (night) → **0.8×**
- `dot > 0.25` (day) → **1.6×**
- Lerp across the band edges (±0.1) so the multiplier never pops.

Re-evaluate `dot` each time a new bite timer starts, not just on cast — the player may sit in one spot as the terminator sweeps over them.

Also in the twilight band, bump tier weights (see [AUTHOR]) so sunset fishing is better in kind, not just faster. Chasing the terminator around the planet is an intended meta. Log the terminator's ground speed per planet in STATUS (`circumference / orbital period`) so Sam can see which planets make it walkable.

## [BUILD] 3 — Bait

- New item family: `Bait` items sold by the fish vendor. Ship **three**: Grubs (cheap, neutral), Glowworms (shifts weights toward uncommon), Voidmaggots (shifts toward rare). Stackable, personal, storable in the locker.
- Casting requires bait in the hotbar; the bait is **consumed on the bite**, not on the cast, and it is lost if the hook window is missed or the line snaps. So a botched fight costs money — that's the stake.
- Without bait: cast is refused with the gaze prompt reading "need bait". See [OPEN] 1 for the starter pack.
- Prices (Sam tunes): Grubs $1 each, Glowworms $2, Voidmaggots $4. Sold in the vendor's existing row style; no new screen.

## [AUTHOR] — species table (12 species, 3 tiers × 4)

Data-driven: one `FishSpecies` ScriptableObject per species (or one table asset), fields: `id, displayName, tier, modelIndex (0–2 within the tier's 3 models), tint (Color), weightMin, weightMax, pricePerLb, staminaMin, staminaMax`. The caught-fish item carries `speciesId` + weight; the hand model/icon uses the tier model + tint. Existing 9 models cover this — species 4 in each tier reuses a model with a different tint.

**Tier roll first, then a uniform species roll within the tier.**

| tier | base weights (day/night) | twilight weights |
|---|---|---|
| common | 45 | 38 |
| uncommon | 35 | 37 |
| rare | 20 | 25 |

> **Sam's revision, 2026-09-01.** The original table was 70/25/5 base, 62/28/10 twilight.
> Sam's call: **45/35/20** base. Twilight keeps the original's shape — rare +5, uncommon +2,
> common -7 — giving 38/37/25. Both rows are tunable on `FishingTuning` without a recompile.
> Consequence recorded so it isn't lost: at the $/lb table below this raises the average
> landed fish from ~$11 to ~$23. See the STATUS economy report for the proposed $/lb trim.

Bait shifts: Glowworms +10 uncommon (from common); Voidmaggots +8 rare, +7 uncommon (from common).

| species | tier | model | tint (start) | weight lb | $/lb |
|---|---|---|---|---|---|
| Bassk | common | 0 | olive `#6B7A3A` | 1–8 | 1.0 |
| Truttle | common | 1 | speckled tan `#B08D5A` | 1–6 | 1.0 |
| Perchik | common | 2 | striped yellow `#D8B83A` | 1–5 | 1.0 |
| Smelk | common | 1 | silver `#B8C2C8` | 1–3 | 1.2 |
| Emberbass | uncommon | 0 | ember orange `#D8632A` | 6–18 | 1.6 |
| Glimtrout | uncommon | 1 | iridescent teal `#3AA8A0` | 5–15 | 1.6 |
| Nullpike | uncommon | 2 | matte black `#2A2A30` | 8–22 | 1.5 |
| Sturgle | uncommon | 2 | armoured grey `#7C7F86` | 10–24 | 1.5 |
| Marlorb | rare | 0 | deep violet `#5A3C9E` | 20–50 | 2.0 |
| Tarpune | rare | 1 | chrome pink `#D46A9A` | 15–40 | 2.2 |
| Muskrellon | rare | 2 | mottled green `#3E6B2E` | 25–50 | 2.0 |
| Coelancer | rare | 0 | ancient blue `#2E5C8A` | 18–45 | 2.4 |

Names are placeholders in the same way the TRAX genres were — Sam renames in the asset. Tints are starting points; the point is that the four species per tier are told apart by colour at a glance.

Sale price = `pricePerLb × weight`, rounded. Keep the vendor's existing per-fish sell interaction; it just reads the species now.

## [INTEGRATE]

- Vendor stock: add the three baits to the fish market's sell rows. Vendor buy side reads `speciesId` for price.
- Save: caught fish already serialize as items — add `speciesId` and make old saves that only carry a tier load as species 0 of that tier.
- Gaze prompts: "cast", "need bait", and the fight state's "reel [hold]" through the existing `ToVerb()` path.
- Audio: rod creak scaling with tension, reel click while holding, run scream, snap. Respect the loudness pass — fishing sounds sit under speech level.
- HUD: bar hides completely outside the fight state.

## [TEST]

1. Headless fight sim (like `DealTests`): for each tier × 50 weights, a "perfect" bot (release during runs) must always land; a "hold forever" bot must snap on every uncommon/rare and land only commons. Report median fight length per tier.
2. Bite-rate sweep: dot from −1 to 1 in 0.05 steps → waitMult monotone in each region, continuous at band edges.
3. Species roll: 10,000 rolls per (bait × light band) → tier frequencies within ±2% of the table.
4. Bait consumed exactly once per bite; never on cast; lost on miss and on snap.
5. Economy report in STATUS: expected $/hour fishing at twilight with Grubs vs Voidmaggots (bait cost subtracted), against the tape medians already recorded (DEMO $15, HALF $31, FULL $73). Fishing at its best should land around 50–70% of a steady demo-tape loop. If it doesn't, propose price/lb changes — don't apply them.
6. Editor play: one cast per tier lands; snap once on purpose; walk into the twilight band and watch bites speed up.

## [OPEN] — Sam decides at plan review

1. Starter bait: 10 Grubs in the shuttle locker on a fresh save, or the vendor gives a free pack on first talk? (Default if unanswered: 10 Grubs in the locker.)
2. Rod upgrades (higher line limit + faster reel, bought from the vendor) — Phase 2 or now? (Default: Phase 2.)
3. Should a missed hook window shake the bobber again after a moment, or always cost the bait? (Default: costs the bait — consistent with [BUILD] 3.)
4. Does the vendor pay a small size-record bonus (first fish of a species over X lb)? (Default: no, Phase 2 with bounties.)

## Phase 2 (recorded so nothing here paints us into a corner)

Bounty fish: unique named individuals with their own models, spawned only when the bobber lands inside a hand-placed sphere collider on the water; flat bounty-bite chance before the tier roll; posted on the vendor's board, reward = money + something unbuyable; spots revealed by NPC side quests (first: Floorbin/Shlorbin), and **known spots persist on the character save**, not the world. Leave a `speciesId`-shaped slot for bounty ids and keep the bite roll in one function so a pre-roll hook is trivial to add.

## STATUS (Claude Code fills in)

### Pre-build verification pass (2026-09-01)

**[EXISTS] claims — VERIFIED:**
- Cast -> bobber on the water collider -> shake on bite -> click window -> fish lands. `Fishing/Bobber.cs`,
  `Fishing/FishingRodController.cs`. One caveat: the current roll is **40/30/30** common/uncommon/rare
  (`Bobber.FishingRoutine`), not a 5%-rare curve. The new tier table is a large nerf to rare frequency --
  intended, but flagging it because it changes the felt hit-rate a lot.
- Weight roll 1–50 lb with a low bias (`Bobber.GenerateFishWeight`); mesh widened by weight
  (`FishEntry.GetXScaleFromWeight`). Caught fish are hotbar items (`Hotbar.ItemId.Fish` + `FishEntry fishData`).
- Fish market vendor buys fish (`Fishing/FishMarketNPC.cs`). It is still a **UI vendor** driven by
  `PostGreetingChoicePanel` rows + `NPCSellRows`, NOT a walk-in store. Building against that, per the handoff.
- Sun geometry is reachable: `SunShadowCaster` transform, via the `World/EclipseShadowGate.cs` pattern.
  `GalaxyTime` correctly left out of it.
- Items / hotbar / money slot 8 / locker: all as described. `CarriesVariant` currently covers mushrooms and
  cassettes only -- bait will need adding there if bait stacks by kind.
- Shop rows with affordability clamp: `Vendor/TevShopUI.cs` (`Entry` / `Stock`) is the pattern to mirror.

**[EXISTS] claim — FALSE:**
- **"3 fish models per rarity tier (9 total)" is wrong. There are 3 fish models TOTAL, one per tier.**
  `FishingdexManager.cs:22-25` exposes exactly `commonFishPrefab` / `uncommonFishPrefab` / `rareFishPrefab`,
  mapped 1:1 by `GetPrefabForType`. The assets are Floreswa `fish01/02/03`; no other fish model is wired
  anywhere in the project. Consequence: the species table's `modelIndex (0-2 within the tier's 3 models)`
  is not buildable as written.
  **Sam's call (2026-09-01): tint only.** All 4 species in a tier share that tier's single model and are told
  apart by tint, name and weight range. `FishEntry.fishColor` is currently `Random.ColorHSV` per fish, so the
  species tint drops straight into that field. `modelIndex` is still authored on the species asset and still
  read through a per-tier model array (one entry each today), so adding real shapes later is an asset
  assignment with no code change.

**Sam's decisions at plan review (2026-09-01):**
- Species models: tint only (above).
- Spin-catch combo (jump + spin mid-air during the strike, rising-pitch catch sound,
  `FishingRodController.UpdateSpinTracking`): **kept, measured pre-fight.** Spin accumulates during the strike
  window exactly as today and is banked at the moment of the hook; the combo sound/bonus fires on LANDING
  instead of on hooking. The trick itself is unchanged for the player.
- Sequencing: **line-render bug fixes first**, handed over for playtest, then Phase 1 on a clean base.
- Tier odds: **45/35/20** base (Sam's revision 2026-09-01, replacing the handoff's 70/25/5),
  38/37/25 twilight. Raises the average landed fish from ~$11 to ~$23 at the handoff's $/lb table.
- [OPEN] 1-4: defaults taken as written (10 Grubs in the shuttle locker; rod upgrades Phase 2; a missed hook
  window costs the bait; no size-record bonus).

### Pre-Phase-1 bug fix: fishing line jitter + tip gap (2026-09-01) -- SAM-CONFIRMED FIXED

Sam playtested 2026-09-01: "the fishing rod is back to working good."

Sam's report: the line jitters while strafing left/right with the bobber out, and never quite reaches the rod
tip. Both are one root cause, introduced when the strafe head-tilt was added.

The line was drawn in `FishingRodController.Update()`, reading `lineAttachPoint.position`. The rod tip's real
pose for the frame is not settled until two LateUpdates later: `ViewmodelMotor` (was order 0) sways the rig,
and `CameraTransformFX` (order 100) applies the strafe head-tilt to the camera's local rotation. So the line
was drawn from the tip's PREVIOUS-frame pose. Strafing drives both the head tilt and the rig's own
`strafeRollFactor` (2.5 deg per m/s), so the tip swung centimetres per frame -> jitter on A/D flicks, and a
constant visible gap at the tip whenever the player was turning at all.

Files touched:
- `Assets/3 - Scripts/Fishing/FishingRodController.cs` -- line draw moved out of `Update` onto
  `Application.onBeforeRender` (fires after every LateUpdate, immediately before rendering; ordering-proof
  against `TrailerFreeCam` at 200 and `KillShotCam` at 250). Added a `lineTipOffset` trim dial, appended at the
  END of the class per the serialization convention.
- `Assets/3 - Scripts/Pickups/ViewmodelMotor.cs` -- `[DefaultExecutionOrder(150)]`, so the rig solves against
  the finalised camera rather than last frame's. Matches `PlayerFlashlight` (150) / `LensFlareRegistry` (300).
  **This also affects the water bottle, guitar and generic held-item rigs** (the axe and pistol have their own
  motor classes and are untouched) -- their sway should feel marginally tighter; worth a glance.
- `Assets/3 - Scripts/Fishing/Bobber.cs` -- latent bug found while in there: the bobber bobbed along
  `Vector3.up` in the PLANET's local space, i.e. sideways through the water everywhere except the planet's
  north pole. Now bobs along the surface normal (`localPosition.normalized`).

Compile: `python prototypes/shuttle-computer/test/compile-unity.py` -> PASS, 0 warnings, all three assemblies.

Residual tip gap, if any, after this fix is a prefab-marker issue, not a timing one: `RodTip` in
`fishing_rod.prefab` sits at local (-0.0333, 1.9906, -0.0481) and is hand-placed, so it may be a centimetre or
two short of the mesh's real tip. Dial it out with `lineTipOffset` in Play mode.

### Phase 1 build (2026-09-01) -- BUILT + TESTED HEADLESSLY, NOT PLAYED

**Architecture.** The rulebook is UNITY-FREE on purpose, the same trick TraxLibrary
uses: `FishingRules.cs` and `FishFightSim.cs` have zero UnityEngine references, so
`prototypes/shuttle-computer/test/verify-fishing.py` compiles them standalone with
Roslyn and EXECUTES them. That is the only reason [TEST] 1-4 are real tests rather
than a play session. Species tints are stored as raw RGB bytes for the same reason;
`FishSpeciesVisuals` does the Color conversion Unity-side. **If anyone adds
`using UnityEngine;` to either file, verify-fishing.py fails loudly.**

**New files**
- `Assets/3 - Scripts/Fishing/FishingRules.cs` -- species table, tier roll, bait
  shifts, sun-angle wait curve, weight/price/stamina. Unity-free.
- `Assets/3 - Scripts/Fishing/FishFightSim.cs` -- the fight state machine
  (tension / stamina / runs), seeded xorshift so a failing fight reproduces.
  Unity-free.
- `Assets/3 - Scripts/Fishing/FishingTuning.cs` -- every knob, as a
  ScriptableObject. **Works with no asset assigned**: creating and wiring a
  .asset needs the Editor, and a null tuning would null-ref the whole loop, so
  `FishingTuning.Active` falls back to built-in defaults matching the tested
  table. Create one via Assets > Create > Fishing > Fishing Tuning and drop it on
  the Bobber prefab to get inspector knobs; nothing breaks either way.
- `Assets/3 - Scripts/Fishing/FishingBait.cs` -- the three baits, vendor prices,
  and "best bait held" resolution.
- `Assets/3 - Scripts/Fishing/FishingSun.cs` -- the geometric sun dot, measured
  off SunShadowCaster (EclipseShadowGate's source). Cached, never a per-frame
  FindObjectOfType. Deliberately NOT GalaxyTime.
- `Assets/3 - Scripts/Fishing/FishSpeciesVisuals.cs` -- tint + tier-model bridge.
- `Assets/3 - Scripts/Fishing/FishingTensionHUD.cs` -- the one bar, helmet-HUD
  bracket language, hidden outside the fight.
- `prototypes/shuttle-computer/test/FishingTests.cs` + `verify-fishing.py`.

**Files modified**
- `Fishing/Bobber.cs` -- sun-angle bite timer re-read on every new timer; species
  roll on the bite; bait consumed on the bite; hook opens a FIGHT instead of
  landing the fish. Bob direction fixed to the surface normal (pre-existing bug).
- `Fishing/FishingRodController.cs` -- held reel input, cast refused without bait,
  snap recoil + sound, gaze prompts, spin banked at the hook.
- `Fishing/FishInventory.cs` -- `FishEntry.speciesId`, species constructor,
  `ResolveSpecies()` legacy migration, price via the species table.
- `Fishing/FishMarketNPC.cs` -- three bait buy rows with an affordability clamp;
  fish cards named and tinted by species. Sell pricing needed NO change: it
  already went through `FishEntry.GetValue()`, which now reads the species.
- `Fishing/FishingdexManager.cs` -- species names + tints; `PrefabForTier`.
- `UI/Hotbar.cs` -- three bait ItemIds (appended), swatches, names, stack cap 50.
- `UI/MainMenuController.cs` -- seeds FishingTensionHUD (CLAUDE.md trap #1).
- `SaveSystem/SaveData.cs` + `SaveCollector.cs` -- `speciesId` through all 5
  capture sites and 3 apply sites. Bait needed no save work: hotbar slots already
  round-trip ItemId by name.
- `SaveSystem/NewGameReset.cs` -- starter bait.

**Test results**

`python prototypes/shuttle-computer/test/verify-fishing.py` -> **PASS, 63 checks.**
`python prototypes/shuttle-computer/test/compile-unity.py` -> PASS, 0 warnings,
all three assemblies (the zero-warning baseline holds).

1. **Fight.** 12 species x 50 weights x 2 bots. A skilled bot (releases during
   runs, backs off above 70% tension) lands 100%; a hold-forever bot snaps every
   uncommon and rare and lands every common. Median WALL-CLOCK fights:
   **common 2.2s** (1.6-3.1), **uncommon 12.8s** (9.2-16.8), **rare 27.5s**
   (20.6-33.9).
2. **Bite rate.** Sweep -1..1 in 0.05 steps: continuous (max step 0.275, exactly
   the blend's slope), monotone in each region, twilight is the global best.
3. **Species roll.** 10,000 rolls x 3 light bands x 4 baits, every tier frequency
   within +/-2%. Species uniform within a tier. Grubs verified neutral;
   Glowworms move only uncommon; Voidmaggots move rare and uncommon; both draw
   from common. Legacy tier -> species migration checked, including a garbage
   tier string falling back to common rather than throwing.
4. **Bait accounting.** Guarded structurally (the consume is inside a Unity
   coroutine and cannot be executed headlessly): exactly one `FishingBait.Consume`
   call in Bobber.cs, before the strike window opens, and no bait reference
   anywhere in the cast path. A future edit moving it into `CastBobber` fails the
   script.

**Terminator ground speed** (`2 pi r / railPeriod`, from the live scene):

| body | radius | day (railPeriod) | terminator |
|---|---|---|---|
| Humble Abode | 200 | 900 s | **1.40 m/s** |
| Cyclops | 500 | 1200 s | **2.62 m/s** |
| Fiery Twin | 300 | 600 s | **3.14 m/s** |
| Icey Twin | 300 | railPeriod 0 | not on the clockwork rail -- no fixed day |

All three rail planets are walkable: Humble Abode's terminator is a stroll, the
Fiery Twin's needs a jog. Chasing the twilight band is a real, playable meta.

**ECONOMY -- THE BALANCE LAW IS BROKEN, and the fix is Sam's call.**

At twilight with Grubs a landed fish averages **$23.11 net**; with Voidmaggots
**$26.12 net**. That is already more than a whole DEMO tape ($15 median) *per
fish*. Two compounding causes: Sam's 45/35/20 raised rare from 1-in-20 to 1-in-5,
and rares average ~$70 against a common's ~$3.35.

Rough $/hour, twilight, Grubs, at the shipped `baseWaitMin/Max` of 6-22s: a cycle
is ~2s cast + ~7s wait + ~12s fight + ~2s land = ~23s, so ~155 fish/hour before
vendor trips, ~100/hour with them -> **roughly $2,300/hour**. A demo-tape loop at
$15 a tape and a few minutes a tape is order-of-magnitude $200/hour. Fishing is
therefore about **10x the tape loop**, where the handoff wants 50-70% OF it.

Per [TEST] 5 these are PROPOSED, not applied:
1. **Cut rare $/lb hardest** -- rare 2.0-2.4 -> ~0.6, uncommon 1.5-1.6 -> ~0.9,
   common unchanged. Keeps Sam's odds and the feel of a rare being a prize while
   removing most of the income. Roughly a 3x cut.
2. **Raise `baseWaitMin/Max`** (6-22s -> ~20-60s). Fishing becomes something you
   do while waiting, not a money printer. Cheapest single lever; costs nothing
   but patience.
3. Do both at half strength.

Not applied because the handoff explicitly says propose, don't apply -- and
because which lever to pull is a feel decision, not a maths one.

**Deviations from the handoff, all deliberate**
- **Common stamina 2.5-4s -> 1.6-2.6s.** Required for the handoff's OWN [TEST] 1
  to pass: a hold-forever player snaps a common at 100/(35 x 1.0) = 2.857s, so a
  2.5-4s common could not "always land" for that bot. The handoff's prose ("a 2 lb
  common is two seconds") contradicts its own table; the prose won. Uncommon and
  rare are untouched.
- **Starter bait goes in the HOTBAR, not the shuttle locker** ([OPEN] 1's
  default). Without bait you cannot cast at all, so bait the player fails to find
  is not a slow start -- it is the whole feature silently missing. Which loot box
  is "the shuttle locker" is a scene fact this pass could not verify in the
  Editor. One line in NewGameReset to move it once Sam names the box.
- **`FishingTuning` is optional, not required** (reason above).
- Fight tuning is a `staminaScale` multiplier on the whole table rather than 12
  hand-edited stamina numbers, so shortening every fight is one slider.

**Known risk not covered by any test:** the rare fight at ~28s median may simply
be too long to be fun. The sim proves it is winnable and fair; it cannot tell us
whether it drags. `FishingTuning.staminaScale` is the dial -- 0.5 halves every
fight without touching the species table.

- Not played / not seen in editor: ALL of the above. No play mode was entered
  (Sam runs the playtests). Specifically unverified: how the tension bar reads on
  screen, whether the bar sits where it should under the reticle, whether the
  snap recoil looks right, whether the vendor's bait rows are legible, and
  whether the FishingTensionHUD seeding actually fires in a build.
