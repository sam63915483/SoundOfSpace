# Day 1 Redesign — Slow-Paced, Story-Anchored Intro

A redesign of the opening of the game (replacing the current Scene 1.6.7.7.7 boot-into-tutorial flow) into a single in-fiction "first day" that teaches every core mechanic through a guided fishing trip with a neighbor. Targets ~30–45 minutes of relaxed, low-stakes play instead of the current 12-minute mechanic firehose.

---

## 1. Design goals

The current intro fails because it teaches **inputs** without **intent**. Players learn to jump-boost-flashlight-map in 90 seconds and then get dumped into a sandbox with no reason to do anything. The redesign flips that: every mechanic gets introduced at the moment the player has a reason to use it, inside a narrative beat that makes its purpose obvious.

Specifically the redesign aims to:

- **Replace simultaneous teaching with sequential teaching.** One mechanic at a time, each separated by a few minutes of relaxed play.
- **Use NPC dialogue as the tutorial UI.** Move teaching out of the tip-text overlay and into the mouth of a friendly companion. The companion is the tutorial.
- **Earn each item, don't be handed them.** Axe, fishing rod, water bottle, jetpack, ship — every one of these arrives as a result of an in-fiction event, not a system unlock.
- **Make survival systems matter before they matter.** Hunger and thirst should be demonstrated as *something the friend cares about* before they ever start meaningfully draining.
- **Hold combat back until last.** Currently the player can be ambushed at night before they even know enemies exist. The new flow gates the first night behind a "now you have an axe and a torch" beat.
- **End with the ship as a reward, not a crash site.** The ship being the *final* unlock turns the existing space-flight gameplay into the moment Day 2 opens up, instead of the broken thing the player is grinding to fix.

---

## 2. Narrative frame (proposed)

The cleanest framing that makes "no ship, no tools, in a cabin" feel motivated:

> **The player crashed weeks ago.** They've been recovering in a neighbor's cabin on Humble Abode. The neighbor — a friendly older alien who lives on the planet — pulled them out of the wreck, patched them up, and has been letting them stay while they get their strength back. Today is the first day the player is well enough to leave the cabin, and the neighbor wants to ease them back in with a fishing trip.

This framing gives you:

- A narrative reason for **starting with nothing**: the player's gear was lost in the crash; their ship is wrecked somewhere they haven't been able to walk to yet.
- A built-in **guide character**: the neighbor is the tutorial.
- A natural **end-of-day reveal**: the neighbor finally takes them to the wreck site, where the existing repair-and-fly-the-ship content lives.
- A **hook for later story**: the player has memory gaps, doesn't fully remember why they were out there, doesn't know what's in their ship's hold — perfect setup for the dormant `InterrogationDialogue` and `ORGDialogue` content to surface in Act 2.

You can swap in another framing if you prefer (e.g., the player is a colonist, not a pilot), but the rest of this doc assumes the recovering-pilot version. The mechanic introduction order works either way.

---

## 3. The cast

Most of these are existing NPCs with cleaned-up dialogue and clearer roles. One or two are new.

| Role | Who | Source | Notes |
|---|---|---|---|
| **The Neighbor** (guide) | Repurposed Bonfire NPC | `BonfireNPCDialogue.cs` | Replace placeholder dialogue entirely. This is the player's primary companion for all of Day 1. |
| **The Trader** (alien) | Alien3 | `NPCDialogue.cs` | Lives in town. Cassette → fishing rod trade gets reframed as an *upgrade* the player chooses to do later, not a gate. |
| **Fish Market** | Fish Market NPC | `FishMarketNPC.cs` | Unchanged. Adds a "first sale" line for Day 1. |
| **The Mechanic** (new role) | Repurpose Guitar Shop NPC, or add new | `GuitarShopNPC.cs` | Sells the jetpack at end of Day 1. Guitar can stay as flavor or move to Day 2. |
| **The Salvager** (optional, new) | New NPC near the ship wreck | — | Brief character at the wreck site who hands the ship back to the player. Could just be the Neighbor again — see Act 6. |

The Neighbor doing most of the talking lets you delete the on-screen tip-text system almost entirely for Day 1. Inputs are taught in dialogue: *"Try walking with me — WASD or the left stick. Just take it slow."*

---

## 4. The Day 1 arc

Six acts, roughly 5–8 minutes each. Each act ends with a small, satisfying beat that closes a loop before the next one opens.

### Act 1 — Waking up (≈3 min)

**Setup.** Player spawns inside a cabin interior. No ship. No HUD bars yet. No hotbar. Cursor locked, but `TutorialGate.LockAll()` so most abilities are still gated except look + walk.

**Beats:**
1. Cold open: black screen, ambient sound, fade in. Player is on a bed in the cabin.
2. A note pinned to the wall (or an audio cue from outside) — *"Gone to set up. Come find me when you're up. — [Neighbor]"*
3. Player is taught **look** (mouse / right stick) by the camera gently nudging toward the note, then **walk** (WASD / left stick) to leave the bed.
4. Open the cabin door — this teaches **interact (F / Square)** in a context where the only interactable object is the door. Currently the hatch button (step 3) is tutorialized in isolation; here the same input opens the cabin door, which feels obvious.
5. Step outside. Sun is up. Ambient music. Player sees the Neighbor a short walk away, leaning against a tree.

**Mechanics introduced:** look, walk, interact.

**HUD state:** Hide everything except a soft tip *"Find [Neighbor]"* — no bars, no hotbar.

### Act 2 — The walk to the lake (≈5 min)

**Setup.** The Neighbor greets the player by name. Brief banter (you can write it however you want, but the function is establishing tone). They invite the player on a fishing trip and start walking.

**Beats:**
1. Neighbor: *"Stay close, the path's just over here."* — the player has to follow. This naturally teaches **sprint (Shift / L3)** because the Neighbor walks at a pace slightly faster than walk-speed, so the player will discover sprint to keep up. (You can backstop this with a tip after 10s of falling behind.)
2. A small log across the path teaches **jump (Space / A)** — the Neighbor jumps it first, then says *"Your turn."*
3. Halfway to the lake, Neighbor stops at a small stand of trees and pulls an axe off their back. *"Before we fish, we'll need wood for tonight's fire. Take this — show me you can swing it."* Hands the player the axe (force-equip on hotbar slot 4, unlock `Pickup` and axe ability).
4. **Chop wood** — Neighbor sets a target of, say, 20 wood (lower than the current 60 to keep pacing tight). Trees are right there, no hunting needed. This subsumes the existing `SwingAxeStep` and `GatherWoodStep`.
5. Neighbor: *"Good. Save that for later — let's go fish."*

**Mechanics introduced:** sprint, jump, axe swing, wood gathering.

**HUD state:** Wood counter appears. Hotbar slot 4 (axe) becomes visible. Hunger/thirst still hidden.

### Act 3 — The fishing trip itself (≈10 min, the centerpiece)

**Setup.** Player and Neighbor arrive at a small lake. Neighbor sets down a pre-built bonfire (placed by you in the scene, not built by the player yet) and produces a fishing rod.

**Beats:**
1. Neighbor: *"Here's a rod — you can borrow mine for today."* Force-equip fishing rod (hotbar slot 2). This **replaces the cassette-for-rod trade as the gate**. The cassette trade still exists for Day 2 as a way to *get your own rod back / upgrade*, but Day 1 doesn't depend on the player discovering Alien3 and the cassette mechanic.
2. **Cast** (LMB / RT) — Neighbor demonstrates first by casting their own rod, then prompts the player. Subsumes `CastBobberStep`.
3. **First catch.** Wait, fish strikes, click again to reel. Subsumes `CatchFishStep`. Neighbor reacts: *"Nice one — that's a Common, worth about a buck a pound."* This drops the fish-pricing concept conversationally.
4. **FishingDex** (B / R1). Neighbor: *"Press B — there's a little book in your kit, it remembers every fish you've caught."* Subsumes `OpenFishingdexStep` and `FishingExtraInfoStep`.
5. **Three or four more catches** at a relaxed pace. Mix Commons and at least one Uncommon so the player sees variety. Neighbor narrates idle commentary between bites — this is also where you can sneak in worldbuilding ("see those lights up there? That's the Accord shipping lane...") to set up future story content.
6. **Spin catch trick.** Neighbor: *"Wanna see something? Watch."* Demonstrates a spin catch. *"Try it — jump while the line's tight, spin around mid-air."* This makes the spin-catch feel like a fun secret instead of an undocumented advanced mechanic. Subsumes `SpinCatchStep`.
7. Once the player has ~6–8 fish total, Neighbor: *"That's plenty for tonight and a few to sell. Let's head back."*

**Mechanics introduced:** cast, catch, FishingDex, fish rarity tiers, spin catch.

**HUD state:** Fishing UI visible during cast. FishingDex button hint shown after first catch.

### Act 4 — Cooking, eating, and the first night (≈8 min)

**Setup.** Walk back to the cabin / bonfire location. Sun is starting to set as they arrive.

**Beats:**
1. Neighbor: *"Light the fire — drop some of that wood you chopped."* This teaches the **build menu (N / LB)** — but in a constrained way: the only buildable available right now is the bonfire, and it's free. Subsumes `BuildBonfireStep` without forcing the player to learn the full building system on Day 1.
2. **Cook a fish.** Interact with the bonfire, stage a fish, wait 10s, eat. Hunger bar appears on the HUD *for the first time* during this beat — *"You're going to want to eat that. Out here, you forget to eat, you stop walking."* This makes the hunger system arrive as a piece of advice instead of as a silent decay clock.
3. Neighbor hands the player a **water bottle** (force-equip slot 1). *"Lake's right there. Fill it before bed — you'll thank me at noon tomorrow."* Player walks 10 paces back to the lake, refills (RMB / LT), drinks (LMB / RT). Thirst bar appears during this beat.
4. **Sundown.** Sky shifts. Neighbor's tone changes: *"There's something you should know about nighttime here."*
5. **First enemy spawn — scripted, not RNG.** Force-spawn a single enemy near the bonfire. Neighbor: *"White orbs. Hit them with the axe. Three good swings."* Player kills the enemy. Subsumes the entirely-missing combat tutorial.
6. Neighbor places a torch on the ground (TorchAura). *"This'll keep the rest off until morning. Get some sleep."*
7. Fade to black. Time skip to morning.

**Mechanics introduced:** build menu (bonfire only), cooking, hunger system, water refill/drink, thirst system, axe combat, torch aura.

**HUD state:** Hunger and thirst bars appear during this act. Health bar surfaces only if the player gets hit by the enemy.

### Act 5 — Town, money, and the jetpack (≈8 min)

**Setup.** Morning. Neighbor is already up, has packed the player's fish into a small crate. *"Town's a short walk. Time you met a few people."*

**Beats:**
1. Walk to town. This is a chance to teach **flashlight (E / Y)** if you want — have them pass through a short cave or shaded grove on the way. Or skip it and teach flashlight on Day 2; it's not load-bearing for Day 1.
2. Arrive at the **Fish Market**. Neighbor introduces the player. Player sells the fish — earns ~$50–80 depending on rarity mix. Wallet HUD appears for the first time.
3. **Player money is now visible and meaningful.** Neighbor: *"There's something I want to show you — but you'll need a bit more."* (You can have the Neighbor reveal that they want to take the player to *the place* — i.e., the wreck — and that the path is rough, so a jetpack helps.)
4. Player goes back to fish a couple more rounds **on their own**, this time without the Neighbor. This is the first moment the game stops holding their hand, and they're doing a loop they already know how to do. They earn the rest of the money they need (target: enough for the jetpack, around $150–200).
5. Return to town, find **the Mechanic** (Guitar Shop NPC repurposed, or new). *"Heard you're going up the ridge — you'll want this."* Sells a basic jetpack for the money the player now has.
6. **Jetpack tutorial — short.** Mechanic walks them out to a small obstacle course behind the shop: a few platforms at increasing heights. Teaches **boost (Space mid-air)**, **directional thrust (sprint + dir mid-air)**, **down-thrust (Ctrl / R3)**. Subsumes tutorial steps 5, 6, 7. Because the player already knows jump from Act 2, only the *air* extensions are new here.

**Mechanics introduced:** fish market sales, wallet, free-form fishing loop (the first non-handheld activity), jetpack purchase, boost / dir-thrust / down-thrust.

**HUD state:** Wallet visible. Hotbar fully populated except slot 5.

### Act 6 — The ship (≈6 min, the payoff)

**Setup.** Neighbor meets the player at the edge of town. *"Come on — there's something I should've shown you days ago."*

**Beats:**
1. They walk / jetpack up the ridge to a place the player hasn't been. Use this stretch to introduce the **map (M / View)** — Neighbor: *"Pull up your map. See that marker? That's where we're going."* You drop a single quest marker on the map for this one beat. Subsumes `MapStep`.
2. They arrive at the **wreck site** — the ship, currently in `MissingLeft`/`MissingRight`/etc. damage state, with the four loose parts scattered nearby. The wreck has been here the whole time; the player just couldn't have known.
3. Neighbor: *"This is yours. I dragged you out of it. Figured today you were ready to see it."* Beat of recognition / amnesia / whatever you want to do narratively.
4. **Repair the ship.** Walk up to each of the four parts, pick them up, snap them into their mounts. The existing ghost-preview UI handles the spatial part. This is currently tutorial step 10 — but here it has weight because the ship matters now. Subsumes `RepairShipStep`.
5. Once the ship is `Full`, Neighbor: *"Go on. Try her."* Player enters pilot seat (teaches `EnterPilot`).
6. **Flight tutorial.** Neighbor stays on the ground. *"Hold space to lift. WASD to fly. Q and E to roll. Take her once around the lake and come back."* Subsumes `ShipUpThrustStep`, `ShipMoveStep`, `ShipDownThrustStep`, `ShipRollStep` in a single guided flight. Player flies a loop, lands.
7. **Lebron Light reveal.** Neighbor: *"One more thing — that dome on top. Hit the second red button when night falls and nothing'll come near you. Costs power, so don't leave it on."* Gives the system a name ("safety dome", "ward field", whatever you call it diegetically — drop the "Lebron" placeholder) and explains its purpose in one line.
8. Neighbor steps back. *"Day's yours now. Come back when you want to."*

**Mechanics introduced:** map, ship repair, pilot mode, ship flight, ship roll, Lebron Light (renamed and explained).

**HUD state:** Ship power bar surfaces during pilot mode.

**End of Day 1.** Tutorial UI fully dismisses. The sandbox is now open. The player has used every mechanic, has a full hotbar, has a working ship, has met the people in the world, and has a relationship with the Neighbor that future story beats can lean on.

---

## 5. Mechanic introduction order — comparison table

| Mechanic | Currently introduced | New introduction |
|---|---|---|
| Look / Walk | Tutorial steps 1–2 | Act 1 — leaving the bed |
| Interact | Tutorial step 3 | Act 1 — cabin door |
| Sprint | Tutorial step 2 | Act 2 — keeping up with Neighbor |
| Jump | Tutorial step 4 | Act 2 — log on the path |
| Axe + wood | Bonus tutorial (gated by finding Bonfire NPC) | Act 2 — Neighbor hands axe |
| Fishing rod + cast | Bonus tutorial (gated by cassette trade) | Act 3 — Neighbor lends rod |
| FishingDex | Bonus tutorial | Act 3 — first catch |
| Spin catch | Bonus tutorial | Act 3 — Neighbor demonstrates |
| Build menu | Bonus tutorial | Act 4 — placing the bonfire |
| Cooking / hunger | Never explicitly taught | Act 4 — eating the first fish |
| Water bottle / thirst | Never explicitly taught | Act 4 — Neighbor hands bottle |
| Combat | Never explicitly taught | Act 4 — scripted first enemy |
| Torch aura | Never explicitly taught | Act 4 — Neighbor places torch |
| Flashlight | Tutorial step 8 | Act 5 (optional) or Day 2 |
| Fish market | Never explicitly taught | Act 5 — first sale |
| Wallet | Never explicitly taught | Act 5 — first sale |
| Jetpack (boost / dir / down) | Tutorial steps 5–7 | Act 5 — Mechanic obstacle course |
| Map | Tutorial step 9 | Act 6 — Neighbor's marker |
| Ship repair | Tutorial step 10 | Act 6 — wreck site |
| Pilot ship | Tutorial step 13 | Act 6 — first flight |
| Ship flight controls | Tutorial steps 14–17 | Act 6 — lake loop |
| Lebron Light | Tutorial step 11 (unexplained) | Act 6 — Neighbor's final line |
| Talk to 3 NPCs | Tutorial step 12 (silent hunt) | Distributed across acts — Neighbor, Fish Market, Mechanic |

Two existing things that should *not* be in Day 1: **the cassette-for-rod trade** (move to Day 2 as an upgrade path / second rod / better rod) and **the guitar shop** (pure flavor, leave it for whenever the player wanders into town a second time). Cutting these from Day 1 is what creates room for the slower pacing.

---

## 6. Implementation notes

### What to add

- **A cabin interior scene or sub-area** with a bed, a note, a door. Can be a small interior bolted onto the existing 1.6.7.7.7 scene rather than a separate Unity scene.
- **Day1Manager.cs** — a single state machine that drives act transitions. Replaces `TutorialManager.cs` for first-playthrough flow. Holds an enum for the current act, listens for the completion event of each act's beat, advances. Writes its current act to the save file so Day 1 is resumable.
- **NeighborCompanion.cs** — a follower AI that walks to scripted waypoints, plays scripted dialogue at scripted moments, and waits for the player at each beat. This is the *hardest* new piece of code in the redesign and is worth scoping carefully. Minimum-viable version: a simple navmesh follower with a queue of `(waypoint, dialogue line, beat completion condition)` entries.
- **Cleaned-up Bonfire NPC dialogue.** Strip the placeholder lines entirely and replace with the Day 1 dialogue beats.
- **Scripted first enemy spawn.** Either disable `EnemySpawner` for Day 1 entirely and trigger a single hand-placed enemy, or add a `Day1ScriptedSpawn` flag to `EnemySpawner` that suppresses RNG spawns until Day 1 is complete.
- **Lebron Light renaming pass.** Pick a real name. Update tip text, button label, in-fiction name.

### What to disable / gate for Day 1

- The current `TutorialManager` 18-step flow — disable for first playthrough; Day1Manager replaces it. Keep the existing tutorial as a fallback / debug skip.
- `EnemySpawner` — suppress RNG-based spawns until Act 4's scripted spawn fires. After that, normal spawning resumes.
- `Alien3` cassette trade — still available, but gated behind a "Day 1 complete" world flag, OR just left where it is and the player can stumble onto it if they wander into town early.
- Ship physics on first frame — the ship is at the wreck site, not in space. No crash sequence runs on `NEW GAME`. The `Ship.PilotShip()` default `startCondition` should switch to `OnPlanet` (or a new `InCabin`) for Day 1.
- Loose ship parts — pre-place them around the wreck site instead of being spawned by `ThrusterDetachOnImpact`. The detach-on-impact system can stay for crashes during normal play; it just doesn't fire at game start anymore.

### What to leave alone

- The existing wood/axe loop, fishing loop, fish market, building system, jetpack physics, ship flight, NBody simulation, Lebron Light AOE math, enemy combat math. **None of the underlying mechanics need to change.** Only the order of introduction and the connective tissue.
- `InterrogationDialogue.cs`, `ORGDialogue.cs`, the Cutscene / Flashback scenes. Leave them dormant for Day 1. They're perfect Act-2-of-the-game material — the player has now established a relationship with the Neighbor, knows their way around, has a working ship, and the ORG arrival event finally has narrative weight when it triggers.

### Save / resume

- `Day1Manager` should save the current act and intra-act beat to disk every time it advances, via the existing `AutosaveManager`. A player who quits in the middle of Day 1 should resume from the start of the current beat, not from the cabin. This is more generous than the current save model and worth the extra few hours of work.

### Skip path (for friends, dev testing, replays)

- `Day1Manager.SkipDay1()` — sets the world flag, force-completes every beat, equips the player with full Day 1 loot, and teleports them outside the cabin in the post-Day-1 state. Bind to a debug key. Anyone replaying the game on a save where Day 1 is already complete should never see it again.

---

## 7. Hooks left behind for Act 2 / future story

The Day 1 design deliberately leaves several threads dangling that the existing dormant content can later pick up:

- **The cassette in the ship.** Still there, untouched on Day 1. When the player goes back to the ship on Day 2 and finds it, the trade with Alien3 becomes the first self-directed quest.
- **The wreck.** Day 1 reveals the wreck but doesn't explain *why* the player crashed. That's the question `InterrogationDialogue` is built to answer.
- **The Neighbor.** They've been kind. They know more than they're saying. Future content can lean on this.
- **The Accord / ORG mention.** A single throwaway line during Act 3 fishing about distant shipping lanes plants the faction names in the player's head. When `ShipFlyIn.onArrived` eventually fires for the first time on, say, Day 3, the player already has a frame of reference.
- **Memory gaps.** If you went with the recovering-pilot framing, the player's amnesia is the spine of any future story you bolt on. They don't remember where they were going. They don't remember what's in the hold. They don't remember who hired them. All of that is `InterrogationDialogue` material.

---

## 8. What to ask web-Claude / the next AI to do with this

If you're handing this off to another assistant for next steps, three useful asks in order:

1. **Write the actual dialogue for the Neighbor across all six acts.** That's the single biggest piece of content this design needs. Aim for ~40–60 lines total, consistent voice, no placeholder content.
2. **Spec the `Day1Manager` state machine in detail** — every act, every beat, every completion condition, every save point. A code-shaped doc you can hand to yourself to implement against.
3. **Storyboard Act 6** — the wreck reveal — as a short cinematic sequence using the existing `FlashbackManager` / fade overlay infrastructure. This is the emotional peak of Day 1 and is worth a little extra polish.

---

That's the plan. Every mechanic the game already has finds a home; nothing is cut; pacing stretches from 12 minutes of input-spam to ~45 minutes of guided play with a companion; and Act 2 of the game inherits a player who actually knows what they're doing and cares about a character.
