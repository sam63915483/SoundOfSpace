# HUMBLE ABODE — PITCH

*A handcrafted, fully-simulated solar system you crash-land into, fish in, build inside, fight through, and find a home in.*

Working titles: **1AUGHHH1**, internal build "Solar System 2".
Engine: Unity 2022.3. Platform target: PC first; controller-ready.
Style: third-person, low-poly handcrafted, dreamy/psychedelic at the edges.

---

## SHORT (~100 words)

You wake up on a tiny handcrafted planet after your ship breaks apart on impact. A father-figure alien named **Tev** finds you, hands you an axe, and tells you to build a cabin. So you do. You catch fish, cook them at the bonfire, drink from the bottle in your starter cabin, then start salvaging your ship one thruster at a time. Soon you're flying again, between eight fully-simulated worlds, parking your ship in orbit to harvest space dust while you go to a concert on the night side of your home planet. *Outer Wilds meets Stardew Valley with a Halo killstreak announcer.*

---

## MEDIUM (~450 words)

**Humble Abode** is a third-person, fully-simulated solar-system survival game. Eight celestial bodies — including your home planet and its moon — orbit a star under real N-body gravity. There are no loading screens between them. You can park a ship in orbit, walk to the back of it, and watch your home rotate beneath you.

The game opens with a **crash**. Your ship breaks apart on the surface of *Humble Abode*, scattering its thrusters across the landscape. You wake up in a starter cabin. There's a note on the table. There's a water bottle. Outside, there's an axe-wielding alien named **Tev** who treats you like a son.

What follows is a deliberately gentle survival loop — **catch fish, cook them at the bonfire, build a cabin, repair your ship** — that quietly hands you keys to a much larger world. Tev gives you a pistol, marks the village on your compass, and from there the game opens up: a fish vendor who pays for your catch, a goods vendor with upgrades, a ship market where you can buy entirely new vessels and salvage parts, two concert stages on opposite poles of your home planet that automatically come alive at local nightfall, and an outer system full of unexplored worlds.

The combat layer is **soft and arcade-y on purpose** — toy-like aliens come at you in waves, you fight them with melee axe swings or hitscan pistol shots, the camera dips into slowmo on every kill, and a Halo-style announcer screams **"WICKED SICK"** when your streak gets out of hand. Ragdolls collapse under local planet gravity. Torches you place radiate a 15-metre no-spawn aura.

Behind that, a longer-arc **space-dust economy** rewards you for parking ships in orbit. Mount a "space net" on a ship's hull, leave it in orbit while you go fishing, come back hours later, drain the buffer at the ship, sell the dust. Some NPCs only deal in dust.

The tone slips between **cozy and psychedelic**. Eat a raw fish, eat a mushroom, and the screen folds into a two-phase kaleidoscope trip rendered as a custom post-processing effect that survives pause. Fly to the concert stage at night; the lasers are real, the audience is alive, the music swaps per stage. The artificial-sun light on your ship can override day/night where you park it.

A complete versioned **save system** captures planetary orbital phase, every NPC kill, every placed building, every loose ship part, every space net's buffer, the player's vitals, the day/night state — so a save resumes the universe *exactly* where you left it.

Targets: Outer Wilds players who want a Stardew layer, Subnautica players who want gravity that matters, anyone who wants a cozy game with teeth.

---

## LONG (~3500 words) — Full pitch

### 1. Logline

> *Crash-land on a fully-simulated planet, build a home, fish, fight, and explore an eight-world solar system you can carry in your hand.*

### 2. The hook — what makes this stand out

There are space games and there are survival games. **Humble Abode** is the rare game that commits to both at the same scale: every celestial body is small enough to walk around, but the system is large enough to be a destination. The simulation is honest — gravity is N-body, planets attract each other, days rotate, and the save system captures all of it. You can park a ship in orbit, climb out the back hatch, and watch your home planet roll beneath you. Then you can walk to the night side, take a concert in, sell some space dust, and fly to a moon.

Critically, the game does **not** ask the player to enjoy hard survival. The vitals are forgiving. The combat is toy-like. The fishing is generous. The cooking is fast. The intent is *agency in a small universe* — a player who wants to spend the afternoon building a torch-lit cabin on a moon should be able to do that. A player who wants to grind a killstreak should be able to do that. A player who wants to follow Tev's story should be able to do that. The systems compose; the game doesn't pick lanes for you.

### 3. Tone & aesthetic

Visually: **handcrafted low-poly**, with detailed shaders for atmosphere, ocean, and procedurally-generated planet terrain. Bright, saturated, a touch of *Untitled Goose Game* / *A Short Hike* warmth.

Tonally: cozy with psychedelic seams. Eating a raw fish or a roadside mushroom triggers a two-phase **kaleidoscope trip** — early wave, late full-kaleidoscope, a custom post-process shader that survives pause. Concerts are real concerts: lasers, blinders, fog puffs, audience members who can die, music that swaps per stage. The killstreak HUD is shamelessly Halo. The compass is shamelessly Skyrim. The audio identity is part lo-fi, part synth, part fantasy folk.

### 4. The opening 30 minutes (linear arc)

This is the most carefully-designed part of the experience. It is gentle but it teaches every system the player will use in the next 10 hours.

1. **Crash.** Cinematic intro — ship breaks apart, debris scatters, player wakes up on the surface of Humble Abode.
2. **Cabin & note.** You're inside a small wooden cabin. There's a note on the table that explains where you are and who left it. The water bottle sits in front of you. You drink. (`EarlyGameProgress.NoteRead` and `WaterBottleDrunk` flip.)
3. **Fishing rod trade.** You walk out, find the alien **Alien3** down by the water, and trade your ship's cassette tape for a fishing rod.
4. **First fish.** Cast the rod, catch the first fish. A bonus tutorial fires — *cast bobber → catch fish → spin → open Fishingdex*. You catch one of each rarity (Common, Uncommon, Rare).
5. **First meal.** Walk to the bonfire near the cabin. The procedurally-built Cook panel opens. Add fish to the pot, watch the 10-second timer, eat — hunger restored.
6. **Meet Tev.** Tev is the father-figure NPC parked outside your cabin. Until you've finished the water-bottle phase, he's grey — collider disabled. Once you drink, he activates. He greets you, *hands you an axe*, tells you to build a cabin and come back.
7. **Build menu.** A bonus tutorial fires — *swing axe → gather 60 wood → place a bonfire → place a cabin*. The build menu is keyed to **N**, ghost previews rotate with RMB, places with LMB.
8. **Return.** Walk back to Tev. He gives you a pistol, marks the village waypoint on your compass.
9. **Village.** The compass guides you to the village. A fish market vendor buys your catch. A goods vendor (Alien7) sells pistol/axe/torch upgrades. A ship market vendor sells whole ships and ship parts. Once you've shaken both vendors' hands, the early-game arc closes.

After this point the game opens up: you can repair your crashed ship part by part, fly off-world, visit the moon and the other six bodies, hunt enemies, build a torch-lit base, mount space nets on a fleet of orbiting ships, attend concerts at the poles. The story doesn't gate it; the *systems* do.

### 5. Core gameplay loop

```
            ┌──────────────────────────────────────┐
            │                                      │
            ▼                                      │
  ┌──────────────────┐         ┌────────────────────────┐
  │ EXPLORE          │ ─────▶  │ GATHER                 │
  │ (planets, NPCs,  │         │ (fish, wood, dust,     │
  │  lore, vendors)  │         │  pickups)              │
  └──────────────────┘         └────────────────────────┘
            ▲                            │
            │                            ▼
  ┌──────────────────┐         ┌────────────────────────┐
  │ SPEND            │ ◀─────  │ CRAFT / COOK / BUILD   │
  │ (vendors,        │         │ (cabins, bonfires,     │
  │  upgrades,       │         │  ship repairs)         │
  │  new ships)      │         └────────────────────────┘
  └──────────────────┘                    │
                                          ▼
                              ┌────────────────────────┐
                              │ COMBAT (optional)      │
                              │ killstreak, ragdolls,  │
                              │ slowmo, torch defense  │
                              └────────────────────────┘
```

Every loop iteration deposits something in the **save state**: a fish in your inventory, a flag in the early-game progression, an enemy permanently killed, a building permanently placed, a space net's buffer growing in orbit. The simulation is the receipt.

### 6. Pillars

Three things this game is, said clearly:

#### Pillar 1 — *A solar system you can hold in your hand.*
Eight planets, real N-body gravity, no loading screens, walkable surfaces. Cabin on a moon. Ship parked in orbit. The same planet rotates under you while you stand on it.

#### Pillar 2 — *Cozy with teeth.*
Fishing, cooking, building, concerts. But also: toy-alien combat that escalates into a Halo killstreak, ragdolls, slowmo-on-kill, hitscan tracers, defended bases, a melee axe with weight.

#### Pillar 3 — *Soft systems-driven roleplay.*
You don't pick a class. You pick what you do today. The same player can be a fisherman, a builder, a hunter, a trader, a concertgoer, a moon-base explorer, a salvager. Every system saves. Every system composes.

### 7. The world

**Eight celestial bodies**, each a small handcrafted sphere with its own biome and gravity well:

| Body | Role |
|---|---|
| **Sun** | The star. Non-landable. Anchors day/night for every other body. |
| **Fiery Twin** | Hot inner planet. |
| **Icey Twin** | Cold inner planet. Sibling to Fiery Twin. |
| **Humble Abode** | The player's home. Hosts the village, the fish market, the bakery (Alien7's shop), the start cabin, the ship marketplace, and **two concert stages** on opposite poles. |
| **Constant Companion** | Humble Abode's moon. Hosts the derelict **MoonBase** — modular sci-fi panels and glass corridors, lit by an artificial sun. |
| **Cyclops** | Big mid-system planet. |
| **Tumbling Bean** | Eccentrically-tumbling rock with non-trivial orbital behaviour. |
| **Watchful Eye** | Outer planet. |

Every body is **independently rotating, independently orbiting, and independently terraformed**. The procedural planet shader handles atmospheres, oceans with shorelines, gradient-based biome painting, and per-body skyboxes.

### 8. Systems in depth

#### 8.1 Physics & gravity
N-body simulation at 100 Hz. Every `CelestialBody` attracts every other body, the player, and every ship. Player and ship align their local up to the dominant gravity well every fixed update — so you can stand on a rolling planet without snapping. Unity gravity is off; everything is computed manually. A floating-origin system (`EndlessManager`) snaps the world back to origin when the camera drifts too far, with interpolation held off for 2 frames to hide the jump.

#### 8.2 Survival vitals
Four-resource model: **hunger**, **thirst**, **health**, **ship power**. Forgiving by design. Health drains faster while hungry/thirsty. Ship power drains faster while flying. Death is freeze + respawn at the start cabin — no item loss. UIs: a compact vitals card (`VitalsHUD`) and a 4-bar HUD (`ResourceHUD`).

#### 8.3 Fishing & cooking
Cast a `Bobber` into water with the equipped rod. Fish come in three rarities (Common / Uncommon / Rare) with deterministic colour and weight per catch. The **Fishingdex** is a procedurally-built scanner UI with a list panel, a detail panel, and a live preview camera that renders each fish in real time. Cooking happens at any placed bonfire via a procedural Cook panel: add fish, 10-second timer, eat. Hunger restored: 20 / 35 / 60 by rarity. Eating raw triggers the trip effect.

#### 8.4 The trip effect
`RawFishTripController` is the dreamy underbelly of the cozy loop. Eat a raw fish or a mushroom and a custom post-process shader (`RawFishTrip.shader`) appends itself to every camera, runs on unscaled time (so opening menus doesn't pause it), and crossfades through a two-phase envelope — early wave, late kaleidoscope. Intensity, hue, and phase are tunable per call.

#### 8.5 Building
Build menu on **N**. Ghost preview shows the placement, rotatable with RMB, placed with LMB. Placed buildings parent to whatever celestial body they land on, so they rotate with it. A separate `BuildMenuLock` system gates recipes — early game only shows bonfire; cabin and torch unlock as Tev's arc progresses. Every placed torch grants a 15-metre no-spawn aura and damages enemies inside.

#### 8.6 Combat
`EnemyController` instances chase, melee, and spit at the player. Two enemy kinds — Regular (`Toy10` prefab) and Elite (`Toy3 elite`) — with a spawner that paces difficulty using a cooldown plus an elite counter. Weapons:

- **Axe**: 34 damage melee, 3 hits to kill a Regular. Cone hit detection.
- **Pistol**: 50 damage hitscan, 2 shots to kill. Custom tracer — a `LineRenderer` whose head + tail are explicitly animated frame-by-frame for accurate fast motion. Magazine reload with slide-back animation. Ammo HUD ties into the wallet card.

Ranged enemies fire `SpitProjectile`s, but only after the player has spent 10 continuous seconds in a tree-episode (tracked by `PlayerTreeContactTracker`) — so casual tree jumping doesn't trigger the standoff loop.

Death produces a **runtime-built ragdoll**. Bones inherit local planet gravity via `RagdollGravity` and re-register with the floating-origin manager. Story-impactful NPCs (`AlienNPCDamageable.isStoryImpactful`) record their kill in the save so the dead body doesn't rejoin the population on reload.

A **killstreak** layer (`KillstreakManager`) tracks consecutive kills with a decay timer that shrinks per tier — 10 s at x1, dropping to 1 s past x11. `KillstreakHUD` shows the tier banner (DOUBLE / TRIPLE / OVERKILL / KILLTACULAR / KILLTROCITY / KILLIMANJARO / KILLTASTROPHE / KILLPOCALYPSE / KILLIONAIRE / WICKED SICK). Every kill briefly slows time via `SlowmoOnKill`.

#### 8.7 Streaming world content
Three independent spawners stream content per cell using a deterministic seed:

- **TreeSpawner** — choppable trees, drop wood.
- **MushroomSpawner** — edible mushrooms, deterministic per cell, trip vector.
- **AlienNPCSpawner** — ambient alien population, killable, with isStoryImpactful flag for pre-placed scene aliens.

All three poll a shared `InputSettings` for caps so the pause menu sliders drive density live. Cells the player has consumed (chopped, killed, ate) are tracked so they don't repopulate.

#### 8.8 NPCs & vendors
Eight named NPCs gate the story and economy:

- **Alien3** — cassette → fishing rod trade.
- **Alien4** — fish market vendor (Common/Uncommon/Rare sell columns).
- **Alien6** — ambient small-talk.
- **Alien7** — goods vendor (pistol, axe, torch upgrades). Procedural shop UI with a copper palette.
- **TEV** — father-figure quest-giver. Unlocks axe + pistol, gives the village waypoint, gates build-menu unlocks.
- **BonfireNPC** — early-game cooking tutor.
- **GuitarShopNPC** — sells the guitar (a non-combat equippable for player vibe).
- **ShipMarketNPC** — sells whole ships AND individual ship parts. A whole-ship purchase spawns a tagged `BoughtShip` 30 m behind the vendor; a part purchase spawns a pickup the player auto-equips.

Dialogue uses a shared typewriter helper (`DialogueTextStyling.RevealCharsTMP`) for zero per-character allocation.

#### 8.9 Ship damage & repair
The ship has four independent parts — left thruster, right thruster, dish, solar panel — and the damage manager swaps between four prefabs (Full / MissingLeft / MissingRight / NoThrusters) based on which parts are attached. Crashes can detach thrusters into the world as physics-active pickups. The player picks them up and reattaches them via mounts. The dish powers comms. The solar panel charges ship power. Every loose part is fully physics-simulated, registered with the floating-origin system, and round-trips through the save with its angular velocity intact.

#### 8.10 Space dust & space nets
A long-arc economy loop. Mount a `SpaceNet` on a ship. Park the ship in orbit (not piloted, not landed, altitude `radius × 1.05 .. radius × 5`). The net accumulates dust over time, faster the lower the orbit. Press F at the net's trigger collider to drain its buffer into `SpaceDustInventory`. Each owned ship can carry multiple nets, each tracked independently. The player can buy entire ships and use them as orbital harvest platforms. Some NPCs (`NPCSellDustOption` exposes `SpaceDustSellUI`) only deal in dust.

#### 8.11 Concert system
The crown jewel of the design. Two stages sit on opposite poles of Humble Abode (`STAGEGOOD` and `STAGEGOOD2`). `ConcertStageHub` discovers every `SpeakerSource` in the scene and binds it to its planet. A stage runs only when its speaker is on the night side — `dot(stageDirection, sunDirection) < 0.05` — so opposite-pole stages automatically swap on/off with the local day. Stages have full rig: cone lights, lasers, blinders, fog puffs, haze, strobes, and a `ConcertLightProgram` that chases patterns synced to the music director.

The audience: live `AudienceMember` instances spawned by `AudienceSpawner` from an `AudienceZone` (which auto-clones for newly-duplicated stages — "duplicate STAGEGOOD → STAGEGOOD2 and it just works"). Audience members can die.

Two clever overlays:

- **Lebron Light override** — if the ship's artificial-sun light (`LebronLight`) is within range of a stage's speaker, the stage is forced into DAY and shuts down. The artificial sun beats the natural one.
- **No-enemy zone** — every speaker projects a 150 m exclusion sphere that `EnemySpawner` checks on every spawn, and any enemy inside takes 40 dmg/s. So a concert is mechanically safe.

#### 8.12 Map & compass
**Compass** (`CompassHUD`) — Skyrim-style top-center strip with tag-anchored waypoints. Tutorial steps register waypoints, and so does Tev when he marks the village.

**Map** (`SolarSystemMapController`, **M**) — a top-down orbital map rebuilt from live `CelestialBody` data. Orbit lines, highlighted bodies, a "teleport to pilot" button. First map open triggers a 6-step `MapTutorial` that persists across saves.

#### 8.13 Camera-effects pipeline
~15 modular effects, each individually toggleable in the pause menu's CAMERA tab, each persisting in PlayerPrefs:

- Headbob, FOV stack, landing dip, death tilt
- Vignette (multi-driver: baseline / damage pulse / low-health / dialogue focus / death dim)
- Damage flash
- Letterbox bars
- Speed lines (jetpack / sprint / boost)
- Film grain
- Chromatic aberration
- Lens dirt
- Mood colour grade
- Anamorphic streaks
- Lens flare registry
- Radial motion blur
- Camera shake
- Slowmo on kill

A master kill-switch disables all of them.

### 9. Save system

A versioned JSON save in `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\`. Every gameplay-relevant state round-trips:

- Player pose, jetpack fuel, ship-thruster fuel, held item, flashlight state
- Ship pose, damage state, attachment state, pilot state, hatch state
- Resources (hunger / thirst / health / ship power)
- Wallet money, wood count, fish inventory (with weight + colour per fish), space dust count + filter unlock + per-ship net buffers
- Tutorial state (started / finished / current step), bonus tutorial state, map tutorial state
- All NPC dialogue completion flags
- All placed buildings (key, parent body, local pose)
- All loose ship parts (kind, body-relative pose, angular velocity)
- All celestial body positions / rotations / velocities (so orbital phase is exact)
- All killed NPCs (streamed-cell IDs + pre-placed names)
- All 12 early-game progress flags
- All read notes
- All compass waypoints
- All active enemies (kind, body-relative pose, health) plus the spawner's cooldown and elite counter
- All purchased extra ships (preserving "Ship 1, Ship 2..." labels across save/load)
- World flags (artificial sun active)

The load path uses a **1-frame + 1-FixedUpdate defer** so `Start()` and the first physics tick can't clobber loaded values. Autosave runs every N minutes (1–30, configurable) to a dedicated overwriting slot.

Saving from the pause menu is one click and a typed name; loading from the main menu shows a procedural scroll panel sorted by timestamp.

### 10. Performance & technical foundations

- **Unity 2022.3**, no `.asmdef` files (everything compiles under `Assembly-CSharp` — moving scripts never breaks builds).
- **Custom shader pipeline** for atmosphere, ocean, and procedural planet shading.
- **Compute-shader-driven terrain mesh generation** with multiple noise layers per body.
- **Floating origin** for arbitrarily-far flight without precision loss.
- **Deterministic procedural content** for trees, mushrooms, alien NPCs — every save reproduces the same world layout.
- **Auto-singleton pattern** for all global managers — no missing-reference exceptions on scene load.
- **Procedurally-built UI** — fishing dex, cook panel, sell panel, vendor shop UIs, save/load panels are all built from code so styling stays in one place.
- **Save defer + Apply order** is fully documented inline in `SaveCollector.cs` and survives system additions cleanly.

### 11. Comparable titles

| Game | What we share | Where we differ |
|---|---|---|
| **Outer Wilds** | Small handcrafted planets, fully-simulated solar system, no loading screens | We add a survival + combat + economy layer; tone is cozier; no time loop |
| **Stardew Valley** | Cozy progression, NPC relationships, cooking, fishing | We're 3D + spaceflight + combat; faster moment-to-moment |
| **Subnautica** | Survival on an alien world, base-building, crafted exploration | We have a real solar system instead of one ocean |
| **A Short Hike** | Warm low-poly art, gentle exploration, character-driven | We add many more systems; same warmth |
| **Halo (announcer)** | Killstreak voice, slowmo on kill, soft-arcade combat feel | Explicit homage — used proudly |

### 12. Target audience

- Outer Wilds players who finished it and want a *bigger* version with progression to come back to.
- Stardew / Animal Crossing players who'd like a third-person 3D version with light combat.
- Subnautica players who want gravity that matters and combat that's actually fun.
- Streamers / content creators — the killstreak HUD and concert system are screen-shareable hooks.
- Solo players who want a long-tail save they can drop in and out of.

### 13. Scope, milestones, and what's done

**Currently working** (in the active gameplay scene, save-persistent):

✅ Eight celestial bodies, full N-body simulation, floating origin
✅ Player, ship, ship damage / repair / piloting
✅ Five equippables (water bottle, fishing rod, guitar, axe, pistol)
✅ Hotbar, hunger/thirst/health/ship-power vitals
✅ Fishing, Fishingdex, fish market, bonfire cooking
✅ Trees, wood, axe-chopping, wood popup
✅ Mushrooms, raw-fish/mushroom trip post-process
✅ Build menu with ghost placement + lock progression
✅ Eight named NPCs with dialogue typewriters
✅ Combat (regular + elite enemies, melee + hitscan, ragdolls)
✅ Killstreak HUD with full tier system + slowmo
✅ Torch aura no-enemy zones
✅ Two concert stages, audience zones, lighting rigs, night gating
✅ Compass HUD + solar system map + map tutorial
✅ Notes & lore collection
✅ Three vendors (goods, fish, ship market) with purchasable items + ships + parts
✅ Space dust economy + space nets + multiple ownable ships
✅ Versioned JSON save + autosave + main-menu / pause-menu UI
✅ Full camera-effects pipeline with per-effect toggles
✅ Tutorial + bonus tutorial systems
✅ Tev story arc gating all of the above

**Roadmap candidates** (post-vertical-slice):

- Off-world story content (currently most quest density is on Humble Abode)
- Bigger combat encounters / boss-tier enemies on outer worlds
- Music score expansion + concert track variety
- Multiplayer co-op (architecture supports it — singletons are clean, saves are file-based)
- Additional ship tiers / customisation
- Photo mode
- Console port

### 14. The pitch in one line, again

> *Crash into a tiny solar system. Build a home there.*

Everything else is something the player gets to choose.
