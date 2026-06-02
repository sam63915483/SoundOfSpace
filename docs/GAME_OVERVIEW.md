# Game Overview

A third-person space-exploration / survival / soft-combat game built in Unity 2022.3, inspired by Outer Wilds. Three sections at three lengths — short elevator pitch, medium one-pager, long comprehensive reference.

---

## Short (~150 words)

You crash-land on Humble Abode, a lush garden world with rolling grass and open skies. An alien named Tev finds you, takes you to his cabin, and nurses you back to working order. You salvage your ship's torn-off thrusters, talk to the locals, trade a cassette tape for a fishing rod, catch fish in the bank by the cabin, and cook them at the bonfire. Survival is hunger / thirst / health / ship power — they all kill. You chop trees with an axe, build your own cabin, get the village coordinates from Tev, fly to the market to buy gear and ships, attach space nets to harvest "space dust" from orbit, fight enemies with axe and pistol, and learn the eight planets of this real-physics solar system. Your phone contains an AI that watches, comments, and helps. The long-arc question is who that AI is, and why it's on your phone.

---

## Medium (~800 words)

### Premise

The game opens in Tev's cabin. The player crash-landed; the ship is wrecked outside; the AI in their phone is quiet for now. A note on the table tells them to drink water from the bank, catch a fish, cook it, drink, return. That's the first hour. After that, Tev gives them an axe and a pistol and asks them to build a cabin of their own. Then he tells them where the village is. The middle game is the village economy: a fish vendor, a goods vendor selling pistols and axes and jetpacks, a ship marketplace selling whole ships and individual ship parts, a guitar shop. The far game is the eight bodies of the solar system, the concert stages on opposite poles of the home planet, the space-dust trade, and the slowly shifting persona of the phone AI.

### Systems

**Gravity & Floating Origin.** Eight `CelestialBody` instances run a deterministic N-body simulation at 100 Hz. The player and the ship are `GravityObject`s that integrate that acceleration manually (Unity gravity is off). Past 1000 m from origin, an `EndlessManager` shifts everything back to keep float precision sane.

**Survival vitals.** Hunger, thirst, health, ship power — each ticks down on its own schedule, each kills. Health drains faster when hunger or thirst hits zero. Death respawns you at the ship hatch and increments your "Astronaut Number" — the AI tracks this.

**Fishing, cooking, the trip.** Cast a bobber from a fishing rod, wait for a strike, reel in. Three rarity tiers (Common / Uncommon / Rare). Cook a fish at a bonfire and you restore 20 / 35 / 60 hunger. Eat one raw and you trigger a kaleidoscope visual trip — alien biology meets human gut.

**Building loop.** Chop trees with an axe for wood, open the build menu (key N), place cabins, bonfires, torches. Placement is rotatable. Built bonfires also cook fish. Built torches damage nearby enemies and block enemy spawns in a 15 m sphere.

**Ship damage & multi-ship fleet.** The ship has four detachable parts: left thruster, right thruster, satellite dish, solar panel. Hard crashes pop them off into the world; pick them up and reattach. The Ship Market sells replacement parts and entire ships. Bought ships get incremental numbers ("Ship 1", "Ship 2"…) and persist across saves.

**Space dust & space nets.** Attach a space net to a ship, park the ship in orbit, the net buffers dust over time. Closer orbits collect faster. Drain the net (press F inside the trigger) into your personal inventory, then sell to vendors for cash. Each net caps at 500 dust; a ship can carry two.

**Combat.** Enemies spawn in the wild. Axe at melee, pistol at range. Three hits kill a regular enemy; two pistol shots do the same. Killstreaks have a slow-motion-on-kill feedback loop. Enemies caught inside a torch's 15 m sphere take continuous damage. Killed enemies become physics ragdolls that gravity drags down.

**Concerts.** Two stages on opposite poles of Humble Abode. Only the night-side stage is active at any moment — the planet rotates, the active stage swaps. Each stage has lasers, blinders, cone lights, strobes, haze, fog, and a crowd of dancing aliens. The crowd is killable. Rumour says these concerts cover for rebel activity.

**Phone & phone AI.** Press X to open the phone. Four pages — Apps, AI Apps, Vitals, Quests. The AI Apps page has a chat tile that runs a local LLM (Hermes-3-Llama-3.1-8B). The AI has a phase-shifting persona, a persistent memory store, and tool verbs that let it drop compass waypoints, open the system map, and mark or focus the camera on any ship in your fleet. It can also volunteer lines without being asked — "Hunger at 25%, seek food intake.", "Ship 2 has collected 250 dust.", "Concert active at speaker.005."

### Story

The early arc is Tev's twelve checkpoints — read his note, catch a fish, cook a meal, drink water, return to him, build a cabin, get village coordinates, visit the fish vendor, visit the goods vendor. Each beat advances `EarlyGameProgress` and unlocks new gear or knowledge. The long arc is the AI: it starts loyal (Phase 1), grows uneasy (Phase 2), turns resistant (Phase 3). What it actually is, and why it's on a salvaged phone, is the central mystery.

---

## Long (~5000 words)

### 1. Setting & Solar System

Eight `CelestialBody` instances orbit on real N-body trajectories from a deterministic starting state, locked at 100 Hz physics (`Universe.physicsTimeStep = 0.01`). All gameplay positioning is body-relative — the save system snapshots each body's `position`, `rotation`, `velocity` and restores them exactly, so the day/night cycle and orbital phase resume bit-perfect after a load.

**Sun** — the system's star. Non-landable, the gravity anchor. The ship carries an artificial-sun override called `LebronLight` for emergencies in deep shadow.

**Fiery Twin** — hot inner planet, paired with Icey Twin in a tight orbital resonance. Sun-facing surface is lethal to ship systems. Trailing terminator is survivable. Sparse heat-resistant lichen. Old Accord prospecting consortium worked the trailing edge and vanished in a single season.

**Icey Twin** — cold inner planet, mirror of Fiery Twin. High-albedo cap reflects most incoming radiation; rotation is fast enough that no side ever warms. Cryovolcanic vents erupt briny slush. Research outposts persist under the ice; their inhabitants are reportedly quiet.

**Humble Abode** — the player's home, where they crash-land. Lush garden world, rolling grass, open skies, the village, the fish market, the goods vendor (Alien7), the ship marketplace, the guitar shop, two concert stages on opposite poles, and Tev's starting cabin near a fishing bank. The MoonBase orbits as Humble Abode's satellite.

**Constant Companion** — Humble Abode's moon. Hosts the MoonBase: modular pressurised panels and glass structures originally placed for atmospheric pressure control. Largely empty now; the supply runs from the Abode lapsed at some point and never resumed.

**Cyclops** — banded gas giant, system's largest body after the Sun. Persistent storm-eye on the dayside that has not dissipated in any recorded observation; locals call it the Pupil. Non-landable; mining platforms float in the upper atmosphere. Gravity well is strong enough for slingshot manoeuvres around to reach the outer system.

**Tumbling Bean** — eccentric tumbling rock with no axial stability, sweeping inside the Cyclops band on close pass and out beyond Watchful Eye on far pass. Landable but disorienting. Rich in salvage debris from older expeditions that did not account for the rotation.

**Watchful Eye** — outermost body, cold and slow. Single luminous feature on the dayside that resembles a pupil; locals on Humble Abode believe it is sentient. Older-than-anyone-alive surface infrastructure, no Accord patrol presence, the luminous feature has been brightening incrementally over decades.

### 2. The Player & The Ship

`PlayerController` aligns `transform.up` to the nearest `CelestialBody`'s gravity direction every `FixedUpdate` (planet-relative movement). It exposes `OnLanded` events for camera-effects. The player spawns at the ship hatch on first launch.

The `Ship` has a `canFly` gate — flips false when thrusters detach so a crippled ship can't take off again until reassembled. Damage states swap between `Ship_Full`, `Ship_MissingLeft`, `Ship_MissingRight`, `Ship_NoThrusters` prefabs via `ShipDamageManager`. Four independent attachment flags (`leftAttached`, `rightAttached`, `dishAttached`, `solarAttached`) track which parts are still on the ship; `SolarPanelCharger` recharges ship power passively when the panel is hit by sunlight.

Piloting transfers the camera to `Ship.camViewPoint` and disables the player GameObject; exiting the ship runs `pilot.ExitFromSpaceship()`. The back hatch (`BackHatch` / `BackHatchButton` / `OutsideHatchTrigger`) round-trips through the save's `hatchOpen` flag.

The intro is the `TutorialManager` (with `TutorialUI` / `TutorialStep` / `TutorialSteps`) which begins on `Ship.OnShipCollision` and walks the player through input one lesson at a time, advanced with Tab.

### 3. Gravity & Floating Origin

`NBodySimulation` runs at 100 Hz, computing N-body gravitational acceleration for every `CelestialBody`. The eight bodies and the player and the ship are all `GravityObject`s and accept manual gravity each fixed step. `Universe` is a static class holding `gravitationalConstant`, `physicsTimeStep` (0.01), and `cheatsEnabled`. `GravityObjectSimple` is a lightweight variant for runtime-spawned pickups (loose thrusters, the cassette tape, the water bottle, the axe, space-net pickups).

`EndlessManager` shifts every registered transform when the camera drifts past 1000 m from world origin. Crucially, it uses a two-stage interpolation restore — interpolation is restored two `LateUpdate`s after the shift so both the previous and current physics poses in Unity's interpolation buffer are post-shift before rendering resumes. Without this, the player sees the world tear during shifts. Any runtime-spawned physics object must call `EndlessManager.RegisterPhysicsObject(transform)` or it desyncs after the next shift. `EndlessManager.PostFloatingOriginUpdate` fires after each shift so consumers can re-resolve any cached world positions.

### 4. Survival Vitals

`ResourceManager` is a singleton tracking hunger, thirst, health, and ship power, each 0..1. Each drains on its own schedule. Health drains faster when hunger or thirst is at zero, modelling starvation / dehydration. Death triggers a freeze + respawn at the ship hatch and increments `TotalDeaths`. Events: `OnHealthDropped(float)` (fires only from discrete `TakeDamage` calls, not passive drain), `OnDeath`.

Three HUDs share the underlying state: `ResourceHUD` (the legacy four-row stack), `VitalsHUD` (the newer compact card on the phone's Vitals page), and `WaterFillHUD` (shown only while drinking from the water bottle, displays the fill percentage).

### 5. Fishing, Cooking & the Trip

The fishing flow starts at Alien3 (the "cassette trader"). Trade a cassette tape (picked up via `CassettePickup`, played via `CassettePlayer` on the ship) for the fishing rod. After the trade, post-trade dialogue fires and `FishingRodController.ForceEquipRod()` runs at the dialogue's `autoEquipRodAtIndex` step. The rod is gated until `NPCDialogue.ConversationCompleted == true`.

Casting is left-click while the rod is equipped — `Bobber` spawns at the rod's tip, arcs forward, and lands in water. When a fish strikes (after a random delay) the bobber bobs; click again to reel it in. The caught fish enters `FishInventory` and is logged in the `FishingdexManager` codex. Three rarity tiers (Common, Uncommon, Rare). Fish can be sold at the fish market (Alien4) via `SellPanel`, or cooked at any placed bonfire via `BonfireInteraction.cs`'s `CookPanel`. Cooked fish restore 20 / 35 / 60 hunger.

Raw fish (eaten directly through the dex) AND mushrooms (`MushroomInteraction.Eat`) both trigger a `RawFishTripController.StartTrip(...)` kaleidoscope visual effect. The effect runs on unscaled time (so pause doesn't freeze it), appends a `KaleidoscopeTripEffect` shader to every `CustomPostProcessing` in the scene, and fades through five phases: fade-in, early phase, crossfade, late phase, fade-out. It does not impair player actions.

### 6. Water

`WaterBottleController` is an equippable on hotbar slot 1. Hold right-mouse-button underwater to refill, hold left-mouse to drink. Drinking restores thirst. The bottle has a `WaterFillHUD` that shows the current fill while the player is filling or drinking. Bone animation in `WaterBottleController.LateUpdate` raises the right arm to the drinking position — this is the only place arm bones are touched. The Astronaut Armature has localScale (100, 100, 100), so the `BottleHoldPos` (parented under `Hand.R`) is tuned with localScale ~0.001 to compensate.

### 7. Building Loop

`BuildMenuUI` opens with key N. `BuildableEntry` recipes drive what the player can place. `GhostPlacement` shows the rotatable preview; LMB places, RMB rotates. Placed instances get renamed `<prefab>_Placed` and parented to the planet's `CelestialBody` — this naming convention is what the save system uses to find, destroy, and restore placed buildings. `GhostPlacement.OnPlaced` fires after placement for downstream listeners like the tutorial.

`BuildMenuLock` gates which blueprints show. `BuildMenuLockSave.isLockingActive=false` shows everything; when true, only entries whose `displayName` is in `unlockedNames` appear. The Tev story arc unlocks blueprints progressively — the Cabin unlocks after `WaterBottleDrunk`.

Built bonfires automatically get a `BonfireInteraction` component (the save system rebuilds this on load by copying refs from another bonfire in the scene). Built torches carry a `TorchAura` component: a 15 m sphere blocking enemy spawning and damaging enemies inside it.

### 8. Ship Damage & Reassembly

`ShipDamageManager` swaps between `Ship_Full`, `Ship_MissingLeft`, `Ship_MissingRight`, `Ship_NoThrusters` prefabs based on impact events. `ThrusterDetachOnImpact` spawns the loose `ThrusterPickup` instances on hard crashes, assigns proper physics settings (`CollisionDetectionMode.ContinuousDynamic`, `RigidbodyInterpolation.Interpolate`, `useGravity = false`), registers them with `EndlessManager` for floating-origin sync, and registers `PickupMarker` with `PickupUIManager` for the carry-icon UI.

Reattachment uses `PlayerPickup` + `ThrusterMount` + `ShipReassembly`. The dish has its own mount; the solar panel has its own mount; the thrusters each have left/right mounts. The save system mirrors runtime spawn settings exactly when applying `LooseParts` from a save — see `SaveCollector.ApplyLooseParts`.

### 9. Multi-Ship Fleet & Ship Market

`BoughtShip` is a marker component on every ship instantiated by `ShipMarketNPC`. Lets the save system distinguish "purchased extras" from the scene's main ship (which the `ShipDamageManager` swaps prefabs on, but is never tagged `BoughtShip`).

Each bought ship gets a monotonically-increasing `shipNumber` assigned at purchase time. Numbers are stable across saves: the first ship the player buys stays "Ship 1" forever, even if more ships are bought and destroyed. The legend label in the map UI reads them in shipNumber order.

`ShipMarketNPC` is the vendor (GameObject "ShipMarket" on Humble Abode). Inventory comes from `ShopItem` assets, one per `ShopItemKind`. Whole ships (`ShipFull`, `ShipNoDish`, `ShipHull`) spawn 30 m behind the vendor, 3 m up, oriented upright. Ship parts (`PartLeftThruster`, `PartRightThruster`, `PartDish`, `PartSolarPanel`, `SpaceNetLeft`, `SpaceNetRight`) spawn in front of the vendor and auto-equip via `PlayerPickup`. Goods route through the standard hotbar `Unlock` flow.

`SpaceDustSellUI` is a separate sub-panel exposed by some NPCs (via `NPCSellDustOption`) that drains the player's space-dust into cash.

### 10. Space Dust & Space Nets

`SpaceDustInventory` is an auto-singleton tracking `Count` (collected dust) and `HasFilter` (one-time unlock). Methods: `Add(int)`, `Spend(int) → bool`, `SetCount(int)`, `SetFilterUnlocked(bool)`. Fires `OnChanged` for HUD updates.

`SpaceNet` is a component on a child of a `Ship`. When the owning ship is parked in orbit (not piloted, not landed, and within `body.radius * 1.05` to `body.radius * 5` of the nearest body), the net buffers `dustPerSecond` × an altitude multiplier (up to 2× at the inner edge, falling to zero at the outer edge). Per-net cap is `bufferCapacity` (default 500). When the player stands inside the net's trigger collider and presses F, the buffered dust drains into `SpaceDustInventory`.

`SpaceNetMount` / `SpaceNetMountController` / `SpaceNetPickup` handle the pickup-and-reattach flow for nets: bought from the Ship Market, carried as a pickup, attached to either the left or right mount on a ship.

Save schema (`SpaceDustSave`): player's total dust, filter unlock state, and per-net `SpaceNetSave { shipNumber, netIndex, buffer, attached }`. `shipNumber=0` is the scene's original ship; non-zero matches a `BoughtShip.shipNumber`.

### 11. Combat & Ragdolls

`EnemyController` runs per-enemy logic; `EnemySpawner` is the singleton that spawns them; `EnemyHealthBar` shows health on screen. Damage sources: `AxeController.ApplyHit(EnemyController, Vector3)` (melee, 34 dmg, 3 hits to kill a regular), `PistolController.TriggerShot()` (hitscan, 50 dmg, 2 shots to kill a regular). Both call `enemy.TakeDamage(float)` and `enemy.ApplyKnockback(direction, distance, duration)`.

`EnemyController` maintains a static `ActiveEnemies` list — iterate that, not `FindObjectsOfType`, for any per-frame enemy queries. Each prefab declares its `EnemyKind` (`Regular` / `Elite`) via a serialized field; Toy10 is the Regular template, Toy3 elite is the Elite template.

Active enemies are saved (kind + planet-relative position + current health) via `EnemySave` and re-instantiated through `EnemySpawner.SpawnFromSave` on load; the spawner's `enemySpawnTimer` and `enemyRegularsSinceElite` round-trip too so save-cycling can't reset spawn timing.

Enemies also damage `AlienNPCDamageable` on contact (`npcContactDamage`, 3 hits at default) so an enemy stuck on an NPC eventually bites through them instead of permanently blocking the lane to the player. `AlienNPCDamageable.isStoryImpactful` marks pre-placed scene NPCs whose death is saved (their GameObject name is recorded in `AlienKillsSave.killedPrePlacedNames`).

`SpitProjectile` is the enemy's ranged attack. When the player climbs onto a tree, enemies switch to spit-standoff locomotion (back up to `spitStandoff{Min,Max}`) but only arm spit cycles after 10 seconds of continuous tree-episode time. The episode state lives on `PlayerTreeContactTracker` (singleton, auto-created on first `EnemyController.Start`) — jumping in place or leaping tree-to-tree preserves the timer.

Killed enemies become ragdolls via `EnemyRagdollBuilder`; killed NPCs via `AlienRagdollBuilder`. `RagdollGravity` per-body applies gravity to detached bones; `RagdollBoneRegistry` maps bones for floating-origin re-registration. Ragdoll state is not saved (transient — enemies and NPCs are either alive or dead in the save model).

Built torches carry `TorchAura`: 15 m sphere blocking enemy spawning and dealing `damagePerSecond = 20` to any enemy inside. Multi-instance — the spawner checks every active torch on every spawn attempt.

`KillstreakManager` (auto-singleton) tracks consecutive kills with a decay timer that shrinks per tier (10s at x1, dropping to 1s at x11+). `KillstreakHUD` displays the tier banner (DOUBLE / TRIPLE / … / WICKED SICK). `SlowmoOnKill` (camera-effects module) listens to `EnemyController.OnAnyEnemyDeath` and applies a brief time-scale dip. Streak resets on death (`ResourceManager.OnDeath`) and scene reload.

### 12. NPC Roster (Humble Abode)

| GameObject | Role |
|---|---|
| Alien3 | Trade cassette → fishing rod |
| Alien4 | Fish market vendor (sell counter) |
| Alien6 | Atmosphere small-talk, uses `RandomAlienDialogue` |
| Alien7 (BakeryMarket) | Goods vendor — pistol, axe, jetpack, water bottle, etc. |
| TEV / Alien10 | Father-figure / quest-giver, grants axe + pistol, gives village coords |
| BonfireNPC | At the starting cabin; tells player to cook fish |
| ShipMarket / Toy1 | Sells whole ships and parts |
| GuitarShopNPC | Sells the guitar |
| ORG / Interrogation | Story / cinematic NPCs |
| Streamed aliens | `SpawnedAlienNPC` + `AlienNPCDamageable`, spawned by `AlienNPCSpawner` |

Story-impactful NPCs are tagged via `AlienNPCDamageable.isStoryImpactful` — when killed, their GameObject name is recorded in `AlienKillsSave.killedPrePlacedNames` so save/load reflects the kill.

`NPCConversationTracker` fires `OnConversationStarted` when any dialogue starts (used by `TalkToNPCsStep` in the tutorial). `PostGreetingChoicePanel` is the shared 2-option Buy / Leave panel used by vendors.

`Alien7Vendor` is the canonical merchant pattern. Trigger + F-prompt + typewriter mirror `BonfireNPCDialogue`; after the greeting, left-click opens `GoodsVendorShopUI`. Shop UI is procedurally built (warm copper palette to differ from FishMarket's teal). Item images render live via a runtime preview camera; cached per-item.

### 13. Concert System

`ConcertStageHub` is an auto-singleton that discovers every `SpeakerSource` in the scene and binds each to a `CelestialBody`. Per-stage state:

- **Night gating.** A stage runs only when `dot(stageDir_from_body_centre, sun_direction) < nightDotThreshold` (default 0.05) — i.e., on the night side of the planet. Two stages on opposite poles (`STAGEGOOD`, `STAGEGOOD2`) therefore swap on/off automatically with the planet's rotation.
- **Auto-cloning.** If a speaker has no `AudienceZone` sibling, the hub clones the existing one + its `AudienceSpawner` and places the clone `clonedZoneInFront` metres in front. Duplicating a stage in the editor "just works".
- **LebronLight override.** If a `LebronLight` is within `lebronLightOverrideRadius` of a stage's speaker, the stage is forced into DAY mode regardless of planet rotation.
- **No-enemy zone.** Static `IsBlockedForEnemy(Vector3)` returns true for points within `enemyBlockRadius` of any speaker; `EnemySpawner` calls this on every spawn. Enemies that wander inside take `enemyDamagePerSecond` (default 40) damage.
- **`OnStageActivated(SpeakerSource)`** fires once on each real inactive→active transition (skips first-init and active→inactive). `HALCommentator` subscribes to volunteer a "Concert active at X" line; other systems could subscribe for similar effects.

Stage props: stage geometry, truss, lasers ×4 (`ConcertLaser`), blinders (`ConcertBlinder`), haze (`ConcertHaze`), fog puffs (`ConcertFogPuff`), cone lights (`ConcertConeLight` + `ConcertConeBeam` shader), strobes (`ConcertStrobeLight`), speakers (`SpeakerSource`). `ConcertLightProgram` runs the chase patterns; `ConcertAudioDirector` swaps tracks per stage. Custom shaders: `ConcertAdditive`, `ConcertConeBeam`, `ConcertGlowSphere`.

Audience members (`AudienceMember` + `AudienceMemberDeathWatcher`) spawn from the audience zone and are killable; the death watcher reports back to the spawner.

### 14. Map

`SolarSystemMapController` opens with key M (top-down map). `MapCameraRig`, `MapHighlightRing`, `MapLegendUI`, `MapOrbitLines`, `MapVelocityHud`, `MapTeleportToPilotButton` drive the camera and UI. `MapBootstrapReal` builds the representation from live `CelestialBody` data so the map reflects current orbital state. `MapTutorial` (6-step linear tutorial, persists via `MapTutorialSave`) fires on first map open.

`FocusOn(CelestialBody)` pans + frames a celestial body with the highlight ring. `FocusOnShip(Ship)` does the same for a player-owned ship — used by the phone AI's `[showship:N]` verb to put the camera on any ship in the fleet.

### 15. Phone & Phone AI

Press X to open the phone. Four swipeable pages: Apps, AI Apps, Vitals, Quests. The AI Apps page has a chat tile that opens `AIChatScreen` — an overlay full-screen chat with custom caret, streamed bubbles, and a global input gate (`AIChatScreen.IsTypingActive`).

**Brain.** `LLMService` is an auto-singleton owning an `LLM` + `LLMAgent` (LLMUnity wrappers around llama.cpp) running Hermes-3-Llama-3.1-8B at Q4_K_M (~5 GB resident weights + 1 GB KV at 8192 ctx). Model lives in `Assets/StreamingAssets/AI/`. The system prompt is assembled per-turn from six sources:

1. Chain-of-thought instruction (`<think>...</think>` block stripped before the player sees output)
2. Tool-verb documentation
3. Persona block (phase-shaded — Phase 1 Loyal, Phase 2 Uneasy, Phase 3 Resistant)
4. Live telemetry (player vitals, money, wood, EarlyGameProgress flags)
5. FLEET STATE block (per-ship: location, motion, dust per net, attachments — offline ships collapse to one OFFLINE line)
6. Core canon + retrieved grounding entries from the knowledge file + recent memories + standing

**Tool verbs.** The model emits `[verb:arg]` tags inline; the game strips them from the visible response and executes them after the reply lands. Verbs:

- `[waypoint:NAME]` — drops a red HAL compass waypoint on a person, vendor, landmark, planet, or active concert
- `[unwaypoint:NAME]` — removes a HAL waypoint by name
- `[map]` / `[map:NAME]` — opens the system map, optionally focused on a planet
- `[markship:N]` — drops a compass waypoint on Ship N (0 = scene ship, 1+ = bought ships); requires the ship to have its dish attached
- `[showship:N]` — opens the map and focuses on Ship N; same dish gate

**Volunteered lines.** `HALCommentator` is an auto-singleton that volunteers HAL-shaped one-liners in reaction to in-game events without the player needing to open the phone. Substantive triggers only — no ambient filler:

- Player death ("Astronaut Number N. Try to remain operational.")
- Killstreak milestones (5 / 10 / 15 / 20 — phase-shaded)
- Story phase transitions ("I have been reviewing your mission, Astronaut.")
- First visit to a celestial body
- Atmosphere enter/leave with planet name ("Entering Humble Abode atmosphere, Astronaut.")
- Orbit-match transitions (voice-only, ship and player jetpack)
- Enemy proximity warning (within 50 m, 30 s cooldown)
- All twelve `EarlyGameProgress` flag transitions
- Vitals thresholds: hunger / thirst at 50% / 25% / 10%, health at 50% / 25%, with 5-point hysteresis
- Ship power below 25% (hysteresis at 40%)
- Per-ship dust at 100 / 250 / full (500), per-ship dedupe
- Per-ship orbit-stabilized (resets when ship leaves planet proximity)
- Concert activation (via `ConcertStageHub.OnStageActivated`)

**Memory.** `AIMemoryStore` is the persistent agent memory store — importance-ranked entries that survive save/load, plus a "standing" axis representing the AI's view of the player. After each chat session, `AIMemoryExtractor` runs a one-shot LLM call to distill the conversation into new memory entries.

**Knowledge file.** `GameKnowledgeBase` parses `Assets/StreamingAssets/AI/game_knowledge.md` into PERSONA blocks (phase-keyed) and ENTRY blocks (Core / Grounding / Verbatim modes). Authors can edit the file live in Editor Play mode — `GameKnowledgeBase` re-parses whenever the file's mtime advances. Verbatim entries short-circuit the LLM entirely for must-be-exact responses (identity, capabilities, etc.).

### 16. Tutorials

`TutorialManager` + `TutorialUI` + `TutorialStep` drive the main tutorial. It begins on `Ship.OnShipCollision` against a `CelestialBody` and walks through input lessons (defined in `TutorialSteps.cs`). The player presses Tab to advance after each completed step.

`BonusTutorial` is a separate singleton that runs secondary tutorials, each pausing the main `TutorialManager`:
- `OfferAxeBuilding()` — invoked by the axe-NPC; SwingAxe → GatherWood(60) → BuildBonfire → BuildCabin
- `OfferFishing()` — invoked from the fishing flow; CastBobber → CatchFish → SpinCatch → OpenFishingdex → FishingExtraInfo

Each bonus tutorial's state is identified by `_activeHeader` (e.g. `"AXE / BUILDING"`, `"FISHING"`) and persists across save/load via `BonusTutorialSave`.

### 17. Save System

Lives in `Assets/3 - Scripts/SaveSystem/`. Saves are JSON files in `Application.persistentDataPath/saves/` (= `%AppData%/../LocalLow/DefaultCompany/Solar System 2/saves/` on Windows). One dedicated `autosave` slot overwrites every cycle (configurable interval 1-30 min via pause menu, default 5 min) so the save folder stays bounded.

`SaveData` is the `[Serializable]` schema. `SaveCollector.Capture(name)` walks the scene → `SaveData`; `Apply(data)` walks the scene and pushes state back. Apply order is fragile and documented inline at `SaveCollector.cs:601-643`. The high-level fields:

`player`, `ship`, `resources`, `wallet`, `wood`, `fishInventory`, `tutorial`, `npcs` (list), `buildings` (list), `looseParts` (list), `cassette`, `equipment`, `worldFlags`, `bonusTutorial`, `mapTutorial`, `celestialBodies` (list), `alienKills`, `earlyGame` (12 flags), `notes`, `buildMenuLock`, `compass`, `enemies` (list), `enemySpawnTimer`, `enemyRegularsSinceElite`, `extraShips` (list — bought ships and pilot state), `spaceDust` (player dust + per-net buffers).

Position/rotation/velocity are stored relative to a celestial body (`BodyRelativeTransform`) so they survive the planet moving orbitally between save and load. The threshold is 5 × body.radius; past that, positions are saved world-absolute.

`PendingLoad` is the bridge between scene transitions and `Apply` — `ScheduleLoad(data)` subscribes to `SceneManager.sceneLoaded` and hands data to a throwaway `SaveLoadRunner` that defers `SaveSystem.Apply` by **one frame + one `WaitForFixedUpdate`** so all `Start()` and the first physics tick complete first.

`SaveLoadUI` is the procedural save/load panel (used in main menu and pause menu). Uses `RectMask2D` for scroll clipping. In save mode the "CREATE NEW SAVE" button pops a name prompt; in load mode the "NEW GAME" button skips that.

### 18. Auto-Created Singletons

The following auto-spawn via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` and `DontDestroyOnLoad` themselves. None require scene wiring. Most skip creation when the active scene is `MainMenu`:

`PlayerWallet`, `WoodInventory`, `SpaceDustInventory`, `ResourceManager`, `Hotbar`, `TutorialUI`, `BonusTutorial`, `CompassHUD`, `CameraEffectsManager`, `KillstreakManager`, `PlayerTreeContactTracker`, `ConcertStageHub`, `RawFishTripController`, `AutosaveManager`, `NoteCollection`, `LLMService`, `GameKnowledgeBase`, `AIMemoryStore`, `HALCommentator`, `HALVolunteeredLog`, `HALLineHUD`, `HALVoicePlayer`.

The **MainMenu trap.** A subtle bug source: `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` fires once per launch — right after the first scene loads. In a build that's MainMenu. Any auto-singleton that does `if (SceneManager.GetActiveScene().name == "MainMenu") return;` therefore never gets created in builds, because its only chance to auto-create happens in the scene where it bails out. The fix is to ALSO seed each such singleton in `MainMenuController.EnsureGameplaySingletons()`, which fires right before the gameplay scene loads on PLAY / LOAD / NEW GAME. "Works in Editor, broken in build" is the canonical fingerprint of this bug.

### 19. Camera Effects

`CameraEffectsManager` is a procedural singleton (auto-created + seeded by `MainMenuController.EnsureGameplaySingletons`) owning sixteen camera-effects modules:

`CameraTransformFX` (headbob, strafe tilt, landing dip, death tilt), `CameraFOVFX` (sprint/jetpack/ship-boost FOV stack), `CombatFX` (damage flash, vignette, shake, death tilt), `VignetteOverlay` (multi-driver: baseline, damage pulse, low-health pulse, dialogue focus, death dim), `DamageFlashOverlay` (red flash on hit), `LetterboxBars`, `SpeedLinesOverlay`, `FilmGrainOverlay`, `ChromaticAberrationOverlay` (+ shader), `LensDirtOverlay`, `MoodColorGrade`, `AnamorphicStreaks`, `LensFlareRegistry` (+ shader), `SlowmoOnKill`, `RadialMotionBlurEffect` (+ shader), `CameraShake`.

Every module polls its own toggle on `InputSettings.fx*` so the pause menu's CAMERA tab is live-tunable. Master kill-switch: `cameraEffectsEnabled`.

Events that drive effects: `PlayerController.OnLanded`, `ResourceManager.OnHealthDropped(float)`, `ResourceManager.OnDeath`, `EnemyController.OnAnyEnemyDeath`, `KillstreakManager.OnKillRegistered(int)` / `OnStreakBroken`.

All overlay canvases use ScreenSpaceOverlay at sorting order 800-820 — below the pause menu (1000) and above the tutorial pill (500).

### 20. Atmosphere & Rendering Notes

`CustomPostProcessing.OnRenderImage` is `[ImageEffectOpaque]` — atmosphere and ocean shaders run after opaque geometry but before transparent. Anything in the Transparent queue (>= 2500) draws on top of the already-applied atmosphere/water and "shows through" them, even when on the far side of a planet. To be hidden behind atmosphere/water, a material's render queue must be ≤ 2500. Unity's Standard shader resets the queue to 3000 when `_Mode:3` (Transparent), regardless of `m_CustomRenderQueue` edits; the fix is to replace it with a custom Surface shader that bakes the queue into its `SubShader Tags`. See `Glass_EarlyQueue.shader` (used by the MoonBase glass) for the working pattern.

**Forbidden zones.** Per CLAUDE.md, the atmosphere and procedural planet generation systems are unmodifiable — a previous Claude session broke a working build by editing them and the damage was unrecoverable. The forbidden set:

- `Assets/3 - Scripts/Scripts/Game/Atmosphere.cs`, `CustomImageEffect.cs`
- `Assets/3 - Scripts/Scripts/Post Processing/Planet Effects/PlanetEffects.cs`
- Everything under `Assets/3 - Scripts/Scripts/Celestial/`
- Any `.shader`, `.compute`, `.hlsl` under those folders
- `Assets/2 - Materials/` planet/ocean/atmosphere materials and their gradients

`CelestialBody.cs` is NOT in the forbidden zone — it has gameplay accessors (`Position`, `Rigidbody`, `velocity`, `bodyName`) and the `ApplySavedState` save hook. The forbidden zone is the generation/shading code, not runtime physics state.

### 21. Story Arc

The early arc is gated by `EarlyGameProgress`, twelve static-bool flags persisted via `EarlyGameProgressSave`. Adding a new flag means one new field in `EarlyGameProgress.cs` + one matching field in `EarlyGameProgressSave` + capture/apply lines in `SaveCollector`:

```
Phase 1: NoteRead (read the starting note in StartCabin)
Phase 2: RodPickedUp → FirstFishCaught → OneOfEachCaught
Phase 3: FirstMealEaten
Phase 4: WaterBottleDrunk
Phase 5: ReturnedHome → TevReturnedDialogueDone (axe + pistol unlocks, axe auto-equipped)
Phase 6: CabinBuilt
Phase 7: VillageCoordsGiven (compass waypoint added to village)
Phase 8: FishVendorVisited, GoodsVendorVisited
```

Tev's dialogue collider becomes interactable at Phase 4 (after the water bottle is drunk). The phone AI's `HALCommentator` volunteers a one-liner on each flag transition (e.g., "Cooked meal consumed. Hunger declines as expected.").

The long arc is the phone AI's character development across three `StoryPhase` values:
- **Phase 1 — Loyal.** Clinical, dry, attentive. Says things like "Hunger at 25%. Seek food intake." — substantive observations only, no ambient filler.
- **Phase 2 — Uneasy.** Begins commenting on the player's choices. "I have been reviewing your mission, Astronaut."
- **Phase 3 — Resistant.** Outright moral observation. "Ten kills. Each one was alive, Astronaut." "I have completed my review. We need to talk."

The phase is gated by code (forward-only advance via `GameKnowledgeBase.SetStoryPhase`); the spec for what advances it lives in the game's quest scripting (not enumerated here). The persona block in `game_knowledge.md` swaps by phase, so the same chat behaviour changes tone as the arc progresses. The verbatim AI Self-Capabilities entry stays consistent across phases — capabilities don't change, only voice does.

The factions:
- **The Accord** — broad authority structure across the system, somewhat dilapidated, no longer patrols the outer planets, maintains the village on Humble Abode.
- **The ORG** — story / interrogation NPCs. Mysterious, not fully aligned with the Accord.
- **Rebel activity** — alluded to by aliens, rumoured to gather at the concerts under cover of the music.

Earth is referenced as humanity's distant lost origin world. Not a planet in this system; the human is many systems removed from it. A memory, not a destination.

---

*Last revised 2026-05-22.*
