# Day 1 Implementation Pack

Companion document to `day_1_redesign.md`. Contains everything Claude Code needs to actually implement the redesign:

- **Part 1.** All Neighbor dialogue, organized by act and beat.
- **Part 2.** Full `Day1Manager` state-machine spec — every act, every beat, every completion condition, every save point, plus the `NeighborCompanion` AI spec.
- **Part 3.** Storyboard for the Act 6 wreck reveal cinematic.
- **Part 4.** Integration checklist — what existing systems to disable, hook into, or leave alone.

The Neighbor's working name throughout this doc is **Tev**. The Mechanic is **Kell**. The Fish Market NPC is **Mara**. Rename freely; these are placeholders chosen for being short, easy to type, and not stepping on existing IP.

---

## Part 1 — Dialogue

Voice notes for Tev: older, dry, kind underneath. Sentence fragments. Doesn't over-explain. Treats the player like an adult who's been through something rough. Drops worldbuilding casually, never lectures. The lines below are sized for typewriter delivery — each line is one bubble.

### Act 1 — Waking up

The only "dialogue" here is a written note pinned to the cabin wall. Player reads it on Interact.

**Note (single interactable, opens a dialogue box):**

> *Hey kid —*
>
> *Feeling better today, I can tell. Don't rush. There's water on the table.*
>
> *I'm out by the path when you're ready. Bring your boots, we're going fishing.*
>
> *— Tev*

Optional ambient line if the player lingers in the cabin for >60 seconds (plays as text floating from offscreen):

> *Tev (distant): "Sometime today, kid!"*

### Act 2 — Walk to the lake

**Beat 2.1 — Greeting (Tev is leaning against a tree just outside the cabin):**

- "There you are. Come on, the day's not getting any longer."
- "How's the head?"
- "Yeah. Looked worse a week ago."
- "Path's just over here. Stay close — easy pace."

**Beat 2.2 — Sprint hint (fires after player falls >15m behind for >8 seconds):**

- "Hold the run button, kid. I'm an old man — my knees forgot how to slow down."

**Beat 2.3 — Log on the path (Tev jumps it first):**

- "Watch this." *(jumps)* "Your turn."

After player jumps:

- "There ya go. Body remembers."

**Beat 2.4 — Stand of trees, axe handoff:**

- "Hold up."
- "Before we fish, we need wood for tonight. Fire doesn't light itself."
- "Here — take this."
- "Used to be my brother's. Swing's a little heavy. You'll get used to it."
- "Pick a tree. Doesn't matter which one."
- "I'll tell ya when it's enough."

**Beat 2.5 — During chopping (random rotation, plays every 6–10 seconds):**

- "That's it. Don't break your back over it."
- "Trees here grow back. Don't worry."
- "Keep at it."
- "Other side of the trunk's easier."

**Beat 2.6 — Wood goal hit (~20 wood):**

- "That'll do."
- "Sling the axe on your back. We'll need it later."
- "Lake's just past those rocks. Come on."

### Act 3 — The fishing trip

**Beat 3.1 — Arriving at the lake:**

- "There she is."
- "Nicest spot on this side of the planet, if you ask me."
- "Nobody else does."
- "Sit yourself down. I'll get something going while you fish."

**Beat 3.2 — Rod handoff:**

- "Here's a rod. Mine — you can borrow it for the day."
- "Don't lose it in the lake. I've done that twice and the water's not gonna give it back a third time."

**Beat 3.3 — Casting tutorial:**

- "Cast it out. Hold the trigger, let it pull back, then snap. Don't muscle it."

After first cast:

- "Not bad. Now we wait."
- "Fish out here are picky."

**Beat 3.4 — First strike:**

- "There — feel that? Reel it in, quick now."

After first catch (Common):

- "Ha! That's a Common. Tasty enough fried."
- "Dollar a pound at the market. Throw it in the basket."

**Beat 3.5 — FishingDex prompt:**

- "Give that little book in your kit a peek. Press B."
- "Keeps track of every fish you pull out."
- "I've been filling mine for forty years and I'm still missing two pages."

**Beat 3.6 — Idle banter (rotates between catches, plays roughly every 30–45 seconds during fishing):**

- "See those streaks of light up there? Accord shipping lanes. Big freighters. Don't bother us much."
- "Quiet planet, this one. That's why I picked it."
- "Used to fish with my brother. He went off to the inner systems. Hasn't written in a while."
- "Uncommon and rare ones go for more. Two and three a pound. Worth the patience."
- "Wind's right today. Good day."
- "You ever see a sky this color back home?"

**Beat 3.6b — On dry stretches (no bites for >30 seconds):**

- "Patience, kid. They bite when they bite."

**Beat 3.7 — Uncommon catch:**

- "Ah — Uncommon. Look at the markings."
- "Couple bucks, that one."

**Beat 3.7b — Rare catch (if it happens):**

- "Well I'll be. That's a Rare."
- "Good eye. Or good luck. Either works."

**Beat 3.8 — Spin catch demonstration (Tev casts, gets a strike, jumps + spins mid-air):**

- "You wanna see something dumb? Watch."

*(Tev does it.)*

- "Trick my brother taught me. Jump while the line's tight. Spin yourself around in the air. Then catch."
- "Doesn't do anything special. Just feels good."
- "Try it."

When player nails their first spin catch:

- "There ya go!"
- "See? Pointless. Wonderful."

**Beat 3.9 — Wrap up (after player has 6+ fish total):**

- "Right. That's plenty for tonight, plus some to sell."
- "Let's head back before the sun starts thinking about setting."

### Act 4 — Cooking and first night

**Beat 4.1 — At the cabin firepit (build menu intro):**

- "Drop some of that wood you chopped right there."
- "Pull up your build menu. There's a bonfire in there — it's free."

After bonfire placed:

- "Good. Now stick a fish on it."
- "Ten minutes, give or take. Don't poke it. Just leave it."

**Beat 4.2 — First cooked fish eaten (hunger bar appears for the first time during this beat):**

- "Better, right?"
- "Listen — out here, you forget to eat, you stop walking. There's no nice way to say it."
- "Keep an eye on that bar in the corner."

**Beat 4.3 — Water bottle handoff:**

- "Take this."
- "Lake's right there. Fill it before bed."
- "Right trigger to fill. Left to drink. Don't drink it all at once — you'll just be peeing in the bushes all night."
- "Same deal as the food. You stop drinking, you stop walking."

After first refill + drink:

- "Good. That's two things you don't have to think about anymore."

**Beat 4.4 — Sundown (Tev's tone shifts, more deliberate):**

- "Alright."
- "There's something you should know about nighttime here."
- "Couple hours after sundown, things come out of the dark."
- "White, about yay big. Float weird. We call 'em orbs."
- "They're dumb. But they hit hard."
- "Don't run from them. They're slow, you're not."
- "Just swing the axe. Three good hits and they pop."

**Beat 4.5 — First scripted enemy spawn:**

- "Here we go."
- "You see it? Easy now. Wait for it…"
- "Now — swing!"

After kill:

- "There. Not so bad."
- "Three hits, like I said."

**Beat 4.6 — Torch placement and turning in for the night:**

- "I'm putting this down. Keeps the rest of 'em off until morning."
- "They don't like the light."
- "Get some sleep, kid. Long day tomorrow."
- "We're going to town."

*(Fade to black, time skip to morning.)*

### Act 5 — Town and the jetpack

**Beat 5.1 — Morning, outside the cabin:**

- "Up."
- "Fish are packed. Let's move."

**Beat 5.2 — Walking to town (occasional banter):**

- "Town's a half-mile through the pass."
- "You'll like it. Or you won't. It's a town."

**Beat 5.3 — At the fish market:**

*(Tev to Mara:)* "Mara — got a new face for ya. Kid here's selling."

*(Tev to player:)* "Stage your fish, she'll quote you, you say yes. Easy."

**Mara — Fish Market NPC, first time only:**

- "Welcome to my humble shop, traveller. Tev tells me you've got something for me."
- "Stage 'em up. I'll quote fair. I don't haggle and I don't cheat — Tev wouldn't bring you to me if I did."

After first sale:

- "Nice haul. Come back any time."

**Beat 5.4 — Tev after first sale:**

- "Look at that. Real money in your pocket."
- "Feels different, doesn't it?"
- "There's something I want to show you up the ridge. But the climb's rough."
- "Few more trips out to the lake should cover what we need."

**Beat 5.5 — Player returns with enough money:**

- "Good haul. Come on."
- "There's a guy I want you to meet."

**Beat 5.6 — At the Mechanic (Kell):**

*(Tev:)* "Kell. This is the kid I was telling you about. Sell him a basic jet, will you?"

**Kell — Mechanic NPC:**

- "Heard you're going up the ridge. You picked a hell of a first walk."
- "Basic jet's all you need. Two-fifty."
- *(after purchase)* "Take her out back. I'll show you the controls."

**Beat 5.7 — Jetpack obstacle course (Kell teaches):**

- "Jump first. Then hit jump again mid-air. That's the boost. Try it on that platform."

After boost:

- "Good. Now — hold a direction with sprint while you're in the air. Directional thrust. Get yourself across to that one."

After directional thrust:

- "Last one. Ctrl mid-air drops you fast. Useful when you wanna get down and don't have time to fall."

After down-thrust:

- "Good. Don't break it. Off ya go."

**Beat 5.8 — Tev meets the player back at the shop:**

- "Got the jet? Good."
- "Tomorrow we go up the ridge."
- "Tonight, eat something."

*(Optional: skip the time-skip and go up the ridge immediately. Set by a `Day1Config` toggle. Default = same day if total session time < 35 minutes, otherwise overnight skip to keep pacing.)*

### Act 6 — The wreck

**Beat 6.1 — At the foot of the ridge:**

- "Pull up your map. Press M. See the marker? That's where we're going."
- "Save your jet fuel. The ridge has steps cut into it."

**Beat 6.2 — Climbing (occasional lines, every ~30s):**

- "Almost there."
- "I've been up here once a week since I pulled you out. Just to make sure it was still there."

**Beat 6.3 — Cresting the ridge, ship in view:**

- "Stop here."

*(Beat. Camera reframes to a wide shot of the wreck. Hold for ~3 seconds.)*

- "This is yours."
- "I dragged you out of it. Three weeks back, give or take."
- "You came down hard. I thought you were dead until you weren't."
- "I figured today you were ready to see it."

**Beat 6.4 — FLASHBACK TRIGGERS HERE.** *(See Part 3 for storyboard.)* Cinematic plays. Returns to gameplay.

**Beat 6.5 — Tev after flashback:**

- "Whatever you were doing out there, kid — that's your business. I don't ask."
- "But it's still your ship. And ships are meant to fly."

**Beat 6.6 — Repair instructions:**

- "Four pieces broke loose in the crash. They're scattered around — won't be hard to find."
- "Pick 'em up, walk 'em to the mounts, snap 'em in. Outline'll show you where each one goes."

After all four parts attached:

- "There she is."

**Beat 6.7 — First flight:**

- "Try her."
- "Pilot seat. Press F. She's yours."

In cockpit:

- "Hold space. She'll lift."
- "WASD. Easy on it. She's not as broken as she looks but she's not new either."
- "Q and E for roll. Try it."
- "Take her around the lake. Come back when you're done."

After flight:

- "Welcome back."

**Beat 6.8 — The halo (renamed Lebron Light):**

- "One more thing."
- "See that dome rig on top? Hit the second red button when night falls."
- "Projects a halo around the ship. Orbs won't come near it."
- "Costs power. Don't leave it on all night."

**Beat 6.9 — Handoff to sandbox:**

- "Alright."
- "Day's yours now."
- "Lake's there. Town's there. Sky's everywhere."
- "Come back when you want to. I'll be here."
- "Go on."

*(Tutorial UI fully dismisses. Day1Manager state goes to Complete.)*

---

## Part 2 — Day1Manager spec

This is the implementation contract for Claude Code. Written so it can be turned directly into C# without further design decisions.

### 2.1 Class architecture

```
Day1Manager (singleton MonoBehaviour)
├── Day1State (serializable struct — what gets saved)
│   ├── Day1Act currentAct
│   ├── int currentBeat
│   ├── Dictionary<string, int> counters    // wood gathered, fish caught, money earned, etc.
│   └── HashSet<string> flags               // beat-completion flags
├── Day1ActHandler (abstract base, one subclass per act)
│   ├── OnEnter()
│   ├── Tick()                              // called every frame while active
│   ├── AdvanceBeat()
│   ├── OnExit()
│   └── BeatCount { get; }
└── NeighborCompanion (separate MonoBehaviour — see 2.7)
```

`Day1Manager` does not subclass or replace `TutorialManager`. They coexist:
- `Day1Manager` runs only on **first playthrough** (controlled by save flag `day1_complete`).
- If `day1_complete == true`, `Day1Manager` is dormant and the existing `TutorialManager` flow is used as the fallback / replay tutorial.
- The 18-step `TutorialSteps` array is preserved as-is for the fallback path.

### 2.2 State enum and beat enums

```csharp
public enum Day1Act {
    NotStarted = 0,
    Act1_Waking = 1,
    Act2_Walk = 2,
    Act3_Fishing = 3,
    Act4_NightOne = 4,
    Act5_Town = 5,
    Act6_Wreck = 6,
    Complete = 7
}

public enum Act1Beat { OnBed, ReadNote, AtDoor, OutsideCabin, ApproachedNeighbor, Done }
public enum Act2Beat { Greeting, Walking, JumpedLog, AxeHandoff, Chopping, WoodGoalHit, Done }
public enum Act3Beat { ArrivedAtLake, RodHandoff, FirstCast, FirstCatch, OpenedDex, IdleFishing, SpinCatchDemo, SpinCatchAttempted, FishGoalHit, Done }
public enum Act4Beat { ReturnedToCabin, BonfirePlaced, FirstFishCooked, FirstFishEaten, WaterBottleHandoff, FirstDrink, Sundown, FirstEnemySpawn, FirstEnemyKill, TorchPlaced, SleptThroughNight, Done }
public enum Act5Beat { Morning, ArrivedInTown, FirstSaleStaged, FirstSaleConfirmed, FirstWalletReveal, FreeFishingPhase, MoneyGoalHit, ReturnedToTev, AtMechanic, JetpackPurchased, BoostTaught, DirThrustTaught, DownThrustTaught, JetpackComplete, Done }
public enum Act6Beat { AtRidgeBase, MapOpened, ClimbingRidge, CrestedRidge, TevReveal, FlashbackPlaying, FlashbackComplete, RepairTaught, ShipFull, EnteredPilot, ShipFlightTaught, FlightLoopComplete, HaloTaught, FinalLine, Done }
```

### 2.3 Per-act state machine

Each row: beat → entry conditions → completion condition → on-complete actions.

#### Act 1 — Waking up

| Beat | Entry action | Completion condition | On complete |
|---|---|---|---|
| `OnBed` | Lock all `TutorialGate` abilities except `MouseLook`, `Move`. Spawn player on bed. Cursor locked. HUD hidden. Camera nudges toward note (one-shot scripted look). | Player walks within 1m of note. | Unlock `Interact`. |
| `ReadNote` | Note becomes interactable, glows softly. | Player presses Interact on note. Dialogue box plays. Box closes on second Interact. | — |
| `AtDoor` | Door becomes interactable. | Player interacts with door. | Door opens. Trigger scene transition to outside (no load — just teleport + fade). |
| `OutsideCabin` | Tev visible ahead. Soft tip text: *"Find Tev."* (Only on-screen tip in Day 1.) | Player walks within 5m of Tev. | Hide tip. |
| `ApproachedNeighbor` | Tev plays Beat 2.1 dialogue. | Dialogue completes. | Transition to Act 2. |

**Failsafes:** if player stays on bed >120s, Tev shouts the ambient line. If player stands still for >120s after that, dialogue plays automatically. No hard force-advance — Day 1 should never feel like it's pushing.

#### Act 2 — Walk to the lake

| Beat | Entry action | Completion condition | On complete |
|---|---|---|---|
| `Greeting` | Tev plays 2.1 dialogue. | Dialogue completes. | Unlock `Move`, `Sprint`, `Jump`. Tev begins walking toward log waypoint. |
| `Walking` | Tev follows scripted waypoint path at 1.4× walk speed (forces sprint discovery). Sprint hint plays if player is >15m behind for >8s. | Tev reaches log waypoint AND player is within 10m. | — |
| `JumpedLog` | Tev jumps log. Plays "Watch this." Waits at far side. | Player jumps the log (any jump within 5m of log trigger). | Plays "There ya go." |
| `AxeHandoff` | Tev walks to tree-stand waypoint. Plays 2.4 dialogue. Spawns axe in player hotbar slot 4, force-equips. | Dialogue completes. | Unlock `Pickup`, axe ability. Set wood target = 20. |
| `Chopping` | Wood counter UI appears: `0 / 20`. Tev plays 2.5 banter on a 6–10s rotation. | `WoodInventory.Count >= 20`. | Plays "That'll do." |
| `WoodGoalHit` | Tev walks to lake waypoint. | Tev arrives at lake AND player is within 15m. | Transition to Act 3. |

#### Act 3 — Fishing

| Beat | Entry action | Completion condition | On complete |
|---|---|---|---|
| `ArrivedAtLake` | Tev plays 3.1. Tev sits on a rock. | Dialogue completes. | — |
| `RodHandoff` | Plays 3.2. Spawns fishing rod in slot 2, force-equips. | Dialogue completes. | Unlock fishing-rod ability. |
| `FirstCast` | Plays 3.3 ("Cast it out…"). | Player completes one full cast (bobber lands in water). | Plays "Not bad. Now we wait." |
| `FirstCatch` | Wait for first strike. | Player successfully reels in any fish. | Plays 3.4 dialogue. |
| `OpenedDex` | Plays 3.5 prompt. Tip text: *"Press B."* | Player opens FishingDex once. | Hide tip. |
| `IdleFishing` | Tev plays 3.6 banter on 30–45s rotation, weighted to avoid repeats. Fish counter UI: `X / 6`. | Player has caught 4+ fish total. | — |
| `SpinCatchDemo` | Tev casts. Spawns scripted strike on Tev's line within 5s. Tev does the spin catch animation. Plays 3.8 dialogue. | Animation completes. | — |
| `SpinCatchAttempted` | No tip. Tev's line: *"Try it."* | Player either (a) successfully spin-catches, or (b) attempts and fails 3 times, or (c) catches 2 more fish without trying. | If (a), plays "There ya go!" If (b) or (c), plays "Eh, takes practice. Maybe later." Always advances. |
| `FishGoalHit` | — | `FishingDex.TotalCaught >= 6`. | Tev plays 3.9. Walks to cabin waypoint. |
| `Done` | Tev arrives at cabin. | Tev arrives AND player within 15m. | Transition to Act 4. |

#### Act 4 — Night one

| Beat | Entry action | Completion condition | On complete |
|---|---|---|---|
| `ReturnedToCabin` | Plays 4.1. | Dialogue completes. | Unlock `BuildMenu`, but only Bonfire entry visible (filter on `BuildableEntry.day1Allowed` flag). |
| `BonfirePlaced` | Tip: *"Press N for build menu."* | Player places one bonfire within 10m of cabin. | Hide tip. Plays "Good. Now stick a fish on it." |
| `FirstFishCooked` | Bonfire interaction unlocked. | Player stages and cooks one fish. | — |
| `FirstFishEaten` | — | Player eats the cooked fish. **Hunger bar UI fades in for the first time during this beat.** | Plays 4.2. Hunger decay rate set to normal. |
| `WaterBottleHandoff` | Plays 4.3. Spawns water bottle in slot 1, force-equips. | Dialogue completes. | Unlock water-bottle ability. **Thirst bar UI fades in.** |
| `FirstDrink` | — | Player fills bottle once AND drinks once. | Plays "Good. That's two things you don't have to think about anymore." Thirst decay rate set to normal. |
| `Sundown` | Force time-of-day to ~7pm. Sky shifts. Tev walks closer. Plays 4.4. | Dialogue completes. | — |
| `FirstEnemySpawn` | Spawn one enemy at scripted position 15m from bonfire. Plays 4.5 ("Here we go…"). `EnemySpawner` RNG remains suppressed. | Enemy is alive AND player has line of sight. | — |
| `FirstEnemyKill` | Plays prompt lines as enemy approaches. | Enemy dies. | Plays "There. Not so bad." |
| `TorchPlaced` | Tev walks to torch position. Places torch (TorchAura). Plays 4.6. | Dialogue completes. | — |
| `SleptThroughNight` | Bed becomes interactable. Tip: *"Sleep."* | Player interacts with bed. | Fade to black 2s, hold 1s, fade in. Force time-of-day to morning. |
| `Done` | — | Fade in completes. | Transition to Act 5. |

**Note:** `EnemySpawner.RNGEnabled = false` for all of Act 4 except after `FirstEnemyKill` if the player lingers — at that point, RNG spawns can resume but are gated by the torch (TorchAura suppression already exists in code).

#### Act 5 — Town and jetpack

| Beat | Entry action | Completion condition | On complete |
|---|---|---|---|
| `Morning` | Tev outside, plays 5.1. | Dialogue completes. | — |
| `ArrivedInTown` | Tev walks to town waypoint at 1.2× walk speed. Plays 5.2 banter. | Tev + player both at town entrance. | — |
| `FirstSaleStaged` | Tev plays 5.3 to Mara, then to player. | Player opens Mara's sell panel. | — |
| `FirstSaleConfirmed` | Mara plays first-time line. | Player confirms first sale. | **Wallet UI fades in.** |
| `FirstWalletReveal` | Tev plays 5.4. | Dialogue completes. | Set money goal = 200 (jetpack price 250 minus expected ~50 from first sale). |
| `FreeFishingPhase` | Tev says: *"I'll be at the bar. Come find me when you're ready."* Tev walks to a sittable bench in town and idles there. | `PlayerWallet.Money >= 250`. | — |
| `MoneyGoalHit` | — | Money threshold reached. | Tip: *"Go find Tev."* |
| `ReturnedToTev` | — | Player walks within 5m of Tev. | Plays 5.5. |
| `AtMechanic` | Tev walks to Kell. Plays 5.6 intro. | Player interacts with Kell. | — |
| `JetpackPurchased` | Kell sells jetpack for 250. | Money deducted, jetpack added. | Unlock `Boost`, but lock `DirectionalThrust` and `DownThrust` until taught. |
| `BoostTaught` | Kell walks to obstacle course. First platform highlights. | Player boosts onto first platform. | Unlock `DirectionalThrust`. |
| `DirThrustTaught` | Second platform highlights. | Player reaches second platform via dir-thrust. | Unlock `DownThrust`. |
| `DownThrustTaught` | Third platform highlights. | Player uses down-thrust to descend from a high point. | — |
| `JetpackComplete` | Kell plays "Off ya go." Tev meets player at shop entrance. Plays 5.8. | Dialogue completes. | — |
| `Done` | If session time < 35min, skip to Act 6 same day. Else fade to black, time skip to next morning, fade in. | — | Transition to Act 6. |

#### Act 6 — The wreck

| Beat | Entry action | Completion condition | On complete |
|---|---|---|---|
| `AtRidgeBase` | Tev plays 6.1. | Dialogue completes. | Unlock `Map`. |
| `MapOpened` | Drop a single quest marker on the map at the wreck site. | Player opens map AND closes map. | Hide tip. |
| `ClimbingRidge` | Tev walks the ridge path at 1.0× walk speed. Plays 6.2 banter every ~30s. | Tev + player at ridge crest waypoint. | — |
| `CrestedRidge` | Camera does a scripted reframe — wide shot of the wreck. Hold 3 seconds. Lock player input during shot. Plays 6.3. | Dialogue completes. | — |
| `TevReveal` | — | "I figured today you were ready to see it." line plays. | Trigger flashback (see 6.4 below and Part 3). |
| `FlashbackPlaying` | `FlashbackManager.TriggerCutscene("crash_memory")`. Player input fully locked. | Cutscene scene unloads, returns to 1.6.7.7.7. | — |
| `FlashbackComplete` | Camera returns to player. Tev plays 6.5. | Dialogue completes. | — |
| `RepairTaught` | Plays 6.6. Loose ship parts already pre-placed in scene at scripted positions around the wreck. Ghost-preview UI active. | All 4 parts attached AND `ShipDamageManager.State == Full`. | — |
| `ShipFull` | Plays "There she is." | — | Auto-advance after dialogue. |
| `EnteredPilot` | Plays 6.7 first lines. | Player enters pilot seat. | Unlock `EnterPilot`, `ShipMouseLook`. |
| `ShipFlightTaught` | Plays cockpit lines as each control is needed. Unlocks happen sequentially: ShipUpThrust on first space-hold, ShipMove after 3s of lift, ShipRoll after 30s of flight, ShipDownThrust available throughout. | Player has flown >15s, used roll at least once, and landed back within 30m of wreck. | Plays "Welcome back." |
| `FlightLoopComplete` | Plays 6.8 (halo / Lebron Light reveal). Renames the system label in HUD from "Lebron Light" to "Halo Field". | Dialogue completes. | Unlock `InteractSunlight`. |
| `HaloTaught` | — | — | Auto-advance. |
| `FinalLine` | Plays 6.9. | Dialogue completes. | — |
| `Done` | — | — | Set save flag `day1_complete = true`. Hide all Day 1 UI. `EnemySpawner.RNGEnabled = true`. `Alien3.tradeUnlocked = true`. Re-enable normal `TutorialGate.UnlockAll()`. Fire `Day1Manager.OnComplete` event. |

### 2.4 Save schema

`Day1State` is appended to the existing save file as a sub-object. `AutosaveManager` is called on every beat completion (not every frame).

```csharp
[Serializable]
public struct Day1State {
    public Day1Act currentAct;       // current act enum
    public int currentBeat;          // index into the act's beat enum
    public int woodGathered;
    public int fishCaught;
    public int moneyEarned;
    public bool spinCatchSuccess;    // for branching post-Day-1 flavor
    public string[] completedFlags;  // freeform flags, e.g. "act3_dex_opened"
    public float sessionTimeSec;     // total Day 1 play time (for same-day vs overnight branching in Act 5)
}
```

**Resume logic:** on load, if `day1_complete == false`, `Day1Manager` reads `currentAct` and `currentBeat`, calls the appropriate handler's `OnEnter()` for that beat, and continues. Beat handlers are responsible for being safely re-enterable (idempotent setup).

### 2.5 Skip / debug

- `Day1Manager.SkipDay1()` — sets `day1_complete = true`, force-grants player full Day 1 loot (axe, fishing rod, water bottle, jetpack, ship at the wreck location), teleports player to outside the cabin, sets time to morning of Day 2. Bound to a debug key (suggested: F12 in editor builds only).
- `Day1Manager.JumpToAct(Day1Act act)` — debug-only. Sets state to `(act, 0)` and calls handler's `OnEnter`. Useful for testing individual acts.
- `Day1Manager.JumpToBeat(int beat)` — debug-only. Same act, jump to specific beat.
- All three should be wrapped in `#if UNITY_EDITOR || DEBUG_BUILD`.

### 2.6 Disabled / gated systems during Day 1

While `Day1Manager.IsActive == true`:

| System | Behavior |
|---|---|
| `TutorialManager` | Disabled. Day1Manager is the tutorial. |
| `TutorialGate` | Used by Day1Manager — ability unlocks happen as listed in 2.3. Initial state: all locked except MouseLook + Move. |
| `EnemySpawner` (RNG) | Suppressed until Act 4 `FirstEnemySpawn`. Re-enabled at Act 4 `Done`. |
| `Alien3.cassetteTrade` | Gated behind `day1_complete`. Cassette trade still works mechanically; Alien3 just plays a "come back later, kid, you've got plenty on your plate" line if approached during Day 1. |
| `GuitarShopNPC` | Same — plays a "not today, friend" line during Day 1 if approached. (Or move guitar shop entirely off the Day 1 path so it isn't encountered.) |
| `ThrusterDetachOnImpact` | Disabled at scene start. The wreck is pre-built in the `MissingLeft + MissingRight + MissingDish + MissingPanel` state with parts pre-placed nearby. |
| `Ship.PilotShip()` initial state | `startCondition = OnPlanetWreck` (new enum value). Ship spawns at wreck position, kinematic, in the broken state. Player spawns in cabin, not in ship. |
| `LebronLight` label | All UI references updated to "Halo Field". |
| Hunger / Thirst decay | Set to 0 until their respective Act 4 reveal beats. Then normal rates. |
| Ship power decay | N/A during Day 1 (ship not flown until Act 6). |

### 2.7 NeighborCompanion spec

Tev's AI is the second-largest piece of new code after Day1Manager. Spec'd here to be implementation-ready.

```csharp
public class NeighborCompanion : MonoBehaviour {
    public enum Mode { Idle, Following, Leading, Sitting, Performing }

    [Serializable]
    public struct Waypoint {
        public Vector3 position;
        public float arrivalRadius;       // how close Tev needs to get
        public bool waitForPlayer;        // pause at waypoint until player is within X meters
        public float waitForPlayerRadius; // X
        public string onArrivalDialogueKey; // dialogue line to play when arrived
        public string onArrivalAnimation;   // optional animation (sit, lean, gesture)
    }

    public Queue<Waypoint> waypointQueue;
    public Mode currentMode;
    public float walkSpeed = 2.0f;
    public float leadingSpeed = 2.8f;     // slightly faster than player walk to force sprint
    public float followingDistance = 4f;  // when in Following mode

    public void EnqueueWaypoint(Waypoint w);
    public void ClearWaypoints();
    public void PlayDialogue(string key);   // looks up key in DialogueDatabase, types out via existing typewriter
    public void PlayAnimation(string key);
    public void SetMode(Mode m);

    public event Action<Waypoint> OnWaypointReached;
    public event Action<string> OnDialogueComplete;
}
```

**Behavior per mode:**

- **Idle** — Stand still, occasional idle animation. Used when Tev is waiting for the player to do something (chop wood, fish, etc.).
- **Following** — Follow player, stay within `followingDistance`. Used in Act 5 when player is doing free-form fishing trips.
- **Leading** — Walk toward next waypoint at `leadingSpeed`. Pause if `waitForPlayer` and player is outside radius. Used during walk segments.
- **Sitting** — Tied to a sittable position (rock, bench). Plays sit animation. Stands up when next waypoint is enqueued.
- **Performing** — Plays a scripted animation sequence (the spin-catch demo, placing the torch, etc.). Player input is not blocked, but Tev ignores all other state until performance completes.

**Dialogue system:** Tev uses the existing typewriter dialogue UI (same one current NPCs use). `DialogueDatabase` is a new ScriptableObject keyed by string IDs (e.g. `"act2_beat1_greeting_line1"`), each entry holding the line text + delivery speed + optional animation hook.

**Pathfinding:** if Humble Abode has a navmesh, use it. If not, straight-line steering with simple obstacle avoidance is fine — the waypoint paths are pre-authored, so terrain won't be a surprise.

---

## Part 3 — Act 6 storyboard (the wreck reveal cinematic)

Built on the existing `FlashbackManager.TriggerCutscene()` infrastructure. The cinematic is a separate scene — `FlashbackCrash.unity` — that loads on trigger and unloads back to `1.6.7.7.7` on completion.

**Total runtime:** 18–22 seconds. Short and punchy. Doesn't try to tell the whole story — just plants the question.

### Shot list

| # | Duration | Camera | Visual | Audio | Text overlay |
|---|---|---|---|---|---|
| 1 | 0.5s | Black | — | Low rumble fades in | — |
| 2 | 2.0s | First-person, looking out cockpit window | Stars. Static. Calm. | Steady ship hum. | — |
| 3 | 1.5s | Same FP angle | Red light pulses on. A second one. A third. | Hum cuts to alarm — single tone, then layered. | — |
| 4 | 1.5s | FP, but camera shakes hard | View tilts violently. A planet (Humble Abode) swings into frame, getting bigger. | Alarm at peak. Glass cracks. | — |
| 5 | 1.0s | External wide, behind the ship | Ship in silhouette against the planet, trailing fire. | Wind/atmospheric entry roar. | — |
| 6 | 0.8s | FP again | Planet surface rushing up. Trees visible. | Roar peaks. | — |
| 7 | 0.2s | — | **Flash to white.** | Hard impact sound. Then silence. | — |
| 8 | 1.5s | Black, lingering | — | Ringing tone. | — |
| 9 | 2.5s | Low ground angle, blurry, looking up | A figure (Tev's silhouette) leans into frame. We can't see his face clearly — he's backlit by the sun. | Muffled voice (Tev): *"…still alive in there?"* (filtered, like heard through water) | — |
| 10 | 2.0s | Same angle, focus pull | Tev's silhouette, hand reaching down toward camera. | Muffled: *"Come on. Up. Up, kid."* | — |
| 11 | 1.5s | Black | — | Heart rate slowing. | "*Three weeks ago.*" (small, lower-third, fades in and out) |
| 12 | 1.5s | — | Fade up to gameplay camera at the wreck site. Tev is standing next to the player exactly where he was before the cinematic. | Ambient daytime audio returns. | — |

### Implementation notes

- **Reuse `Cutscene.unity` as a template** — it already has the fade overlay infrastructure. Duplicate, rename to `FlashbackCrash.unity`, replace placeholder content.
- **The ship for shots 2–6 can be a low-poly stand-in** — the cockpit interior doesn't need to match the actual ship asset perfectly. The whole cinematic is sub-25 seconds and the player will be focused on the action, not asset fidelity.
- **The figure in shots 9–10 is a silhouette** — no need to fully model Tev for the cinematic. A backlit billboard sprite is enough and arguably better (more dreamlike, more memorable).
- **Audio is doing most of the work.** Good sound design (alarms, atmospheric entry roar, the shift to muffled water-filtered voice for Tev) will sell this on a small budget. Worth the time.
- **The text overlay in shot 11** ("Three weeks ago.") is the only diegetic text in the cinematic. It anchors the player in time so the return to gameplay isn't disorienting.
- **Trigger on `FlashbackManager.TriggerCutscene("crash_memory")`** — this string key should be set up in `FlashbackManager` to load `FlashbackCrash.unity`. On scene exit, `FlashbackManager` fires `OnCutsceneComplete` which `Day1Manager` listens for to advance the `FlashbackComplete` beat.
- **Player state must be preserved** across the cutscene. `Day1Manager` should serialize player position, rotation, and inventory before the cutscene loads, and restore on return. The existing scene-load flow already does most of this, but verify.

### Why this works as a story setup

Three things land in 20 seconds that will pay off later:

1. **The alarms before the crash.** Something was wrong before the player hit the planet. Not just an accident — a system failure, a chase, something. Question planted: *what was it?*
2. **The muffled voice asking "still alive in there?"** Tev knew there was someone in the wreck before he opened it. He wasn't just walking by. Question planted: *was Tev expecting you?*
3. **The "Three weeks ago" overlay.** Time-skip context. The player has missing time. Question planted: *what else has happened in three weeks?*

None of these need to be answered in Day 1. They're hooks for the existing dormant `InterrogationDialogue` and `ORGDialogue` content to grab onto whenever you wire them in.

---

## Part 4 — Integration checklist

A bullet list to hand to Claude Code as the implementation order:

1. **Add the cabin interior** to scene `1.6.7.7.7`. Bed, note (interactable), door (interactable). Position somewhere that doesn't conflict with the existing wreck-site placement.
2. **Pre-place the wreck** at a scripted position on the ridge. Set `ShipDamageManager.State` to all-missing on scene load via `Day1Manager`. Pre-place the four loose parts within 30m.
3. **Update `Ship.PilotShip()`** to support a new `OnPlanetWreck` start condition. Default `GameSetUp.startCondition` for new games becomes `OnPlanetWreck`.
4. **Disable `ThrusterDetachOnImpact`** during Day 1 (it's pre-resolved). Re-enable after Day 1 complete.
5. **Implement `Day1Manager`** per Part 2. Singleton, save-aware, beat-driven.
6. **Implement `NeighborCompanion`** per 2.7. Waypoint queue, four modes, dialogue hooks.
7. **Build `DialogueDatabase`** ScriptableObject. Populate with all lines from Part 1.
8. **Update `BonfireNPCDialogue.cs`** — strip placeholder content, replace with the Tev role. (Or replace the script entirely with `NeighborCompanion`-driven dialogue.)
9. **Add `Mara` (Fish Market)** first-time line. Existing `FishMarketNPC.cs` can be extended with a one-shot intro.
10. **Add `Kell` (Mechanic)** as a new NPC. Sells jetpack for 250. Has the obstacle-course tutorial flow.
11. **Build the jetpack obstacle course** — three platforms behind the Mechanic shop at heights 4m, 8m (5m horizontal), and 6m (with a drop).
12. **Disable `EnemySpawner` RNG** during Day 1 except the scripted spawn. Add `EnemySpawner.RNGEnabled` flag.
13. **Build `FlashbackCrash.unity`** per Part 3 storyboard. Wire to `FlashbackManager.TriggerCutscene("crash_memory")`.
14. **Rename "Lebron Light" → "Halo Field"** in all UI strings. Code class names can stay.
15. **Add `BuildableEntry.day1Allowed` flag** so the build menu only shows the bonfire during Act 4. Set on all other entries to `false`; Day1Manager flips them all back to `true` on `Done`.
16. **Add `day1_complete` save flag** to the save schema. Default `false` for new games. `Day1Manager` checks this on Awake — if `true`, dormant.
17. **Add debug controls** (`SkipDay1`, `JumpToAct`, `JumpToBeat`) per 2.5. Editor-only.
18. **Test the full flow end-to-end.** Then test resume from each beat.

---

That's the full pack. The original `day_1_redesign.md` is the *design* document (the why and the what); this one is the *implementation* document (the how). Hand both to Claude Code in plan mode with `ultrathink` and start with item 5 from the integration checklist — `Day1Manager` is the spine that everything else hangs off of.
