# AI Tutorial / Dialogue Revamp — Comprehensive Review (2026-06-02)

> **Purpose of this document.** A self-contained record of the work done in the
> 2026-06-02 session on the phone-AI tutorial revamp and dialogue options, written so an
> external reviewer (with no repo access) can compare it against the original spec/plan.
> It includes the original design intent, every change made, the architecture, the full
> authored content, design rationale, a coverage audit, and verification status.
>
> **Project:** "Solar System 2" — Unity 2022.3, Built-in Render Pipeline. Single
> `Assembly-CSharp`. Gameplay scene `Assets/1.6.7.7.7.unity`; launcher `MainMenu.unity`.
> The game **boots in MainMenu and loads the gameplay scene on PLAY/LOAD**, which matters
> for singleton seeding (see "Trap notes").
>
> **Branch/build note:** all of this sits on top of the prior "vertical-slice opening"
> work (~26 `feat(story)` commits, originally on `feature/fish-storage-revamp`; the current
> working tree's baseline commit `1f46982 "Sound of Space — clean baseline"` is a squashed
> snapshot that contains it). The 2026-06-02 changes below are **not yet committed**.
> All iteration is in the Unity Editor (no CLI build/test); the user playtests the **built
> player**, so every change requires a rebuild to see.

---

## 1. What this session was supposed to do

This session was a continuation of the **vertical-slice opening** (spawn → "village reached"
seam), specifically: (a) make the story **backbone solid before building further**, and
(b) **finish integrating the old forced tutorial into the new optional, on-ask tutorial
tips** delivered through the phone AI.

### 1.1 Original design intent (verbatim from the source docs)

**GDD `docs/GDD_VerticalSlice_BuildPlan.md` §2 — "The revamped opening":**

> **Design goal:** delete the forced step-by-step tutorial. Spawn the player free, let them
> hit "what now?", and make the AI the source of direction. Guidance is **always optional**;
> completion is **always** a gameplay event. "Ask the AI" and "explore on your own" converge
> on the identical objective fire — so the self-directed player costs **zero** extra build.
> … this is the **Subnautica PDA model**.

Five steps / three soft gates: **Step 0 Cold open** → **Step 1 First contact** (phone briefs
mission, unlocks "how do I get water/food") → **Gate 1** water+food → **Step 2 Shelter** →
**Step 3 Explore** (find village) → **Step 4 Village/Tev handoff (seam)**.

Three named risks: **(1) phone discoverability** (single point of failure — fix with a buzz
cue after ~30–60s), **(2) grace for the self-directed player** (flags set silently; a player
who did it already isn't told to do it again), **(3)** the trust middle (later).

**Design spec `docs/superpowers/specs/2026-06-01-vertical-slice-opening-design.md` §7 —
"Hint tracks — reuse the pill, on-ask only":**

> Per-topic tracks (`water`, `food`, `fishing`, `shelter`) as **JSON data**:
> `HintTrack { string id; string objectiveId; HintEntry[] entries; }`
> `HintEntry { string tipText; string advanceEvent; }`
> - `HintTrackRunner` renders entries through the **existing `TutorialUI` pill**, moved aside
>   via `SetLeftSide(true)`, advancing on the bound gameplay events.
> - The legacy **ability gate is never engaged** — full freedom on spawn.
> - A track shows **only when the player explicitly asks the AI** — the dialogue response
>   carries `startHintTrack: "<id>"`. This is a **presentation-only field, intentionally
>   outside the 7 story effects**.
> - Completing the bound objective auto-dismisses its track.
> - **Legacy tutorial disposition:** no force-start; `TutorialManager` stays in the codebase
>   **inert**; its step text is the **source material for the hint-track JSON.**

So "finish integrating the old tutorial" = port the remaining legacy tutorial steps into
on-ask hint-track JSON, where they apply to the pre-village free-roam.

### 1.2 Verbal requirements added this session

1. **Make the backbone solid** before progressing (this surfaced three real bugs — §2).
2. **Tev whistle:** once the player completes the water **or** food task, Tev starts
   whistling to draw the player to him (he hands over the axe on talk). Provide an inspector
   slot for the mp3.
3. **Wood-gated shelter hint:** when the player asks how to build a shelter, if they have
   **0 wood in the hotbar** the first tip must be "gather 50 wood from trees"; once they have
   50 wood it changes to "open the build menu and place a Cabin."
4. **Flashlight + map askable options**, appearing **once the cabin is built**:
   - Flashlight: HUD tips teaching the **new** cycle — press **E** → 50%, **E** again → 100%,
     **E** again → off. (The old tutorial text was wrong; it described a scrollwheel.)
   - Map: a single tip "open the phone and open the Map app," because the map already has its
     own tutorial on open.
5. **Audit the rest:** report any other mechanics worth making askable. (User pre-cleared:
   ship, gun, and axe-as-weapon are taught by the goods-vendor's own yes/no tutorial prompt
   when purchased, so they're out of scope.)

---

## 2. Backbone hardening (3 real bugs found + fixed)

All three were the **same root-cause family**: persistent (`DontDestroyOnLoad` / `static`)
runtime state that survives a return to the main menu and a subsequent **New Game** within
the same process — a class of bug this codebase is explicitly prone to (New Game runs no
save-`Apply` pass, so persistent singletons leak the prior run's state). **Rebuilding masked
all three**, because a fresh process resets the static/singleton state — which is why they
only reproduced on a second in-process New Game.

### 2.1 First-contact timer leak → "no dialogue options on a 2nd New Game"
**File:** `Assets/3 - Scripts/Story/StoryDirector.cs`

`StoryDirector` is a `DontDestroyOnLoad` singleton. `ResetForNewGame()` cleared step/flags/
objectives but **not** the non-serialized runtime fields `_firstContactQueued` and
`_coldOpenTimer`. Once a run progressed past `ColdOpen`, `_firstContactQueued` latched `true`;
on the next in-process New Game the 45-second first-contact timer in `Update()` was gated by
`if (_firstContactQueued) return;` and never ran again → first contact never queued → the
phone opened to the ambient cold-opener with **zero reply buttons**.

**Fix:** reset `_firstContactQueued`/`_coldOpenTimer`/`_gateCheckTimer` in `ResetForNewGame()`,
and set them correctly in `LoadFrom()` (`_firstContactQueued = _step != StoryStep.ColdOpen`).

### 2.2 Stale AI chat/transcript leak across New Game
**Files:** `Assets/3 - Scripts/SaveSystem/NewGameReset.cs`,
`Assets/3 - Scripts/AI/HALVolunteeredLog.cs`

The phone AI's chat history (`AIMemoryStore` recent turns) and the volunteered-line transcript
(`HALVolunteeredLog`, e.g. "Fishing rod acquired." lines emitted by `HALCommentator`) are both
`DontDestroyOnLoad` and were **deliberately skipped** by `NewGameReset` ("no clean reset API
yet"). Result: acquisitions from two games ago replayed in a fresh game's AI app.

**Fix:** added `HALVolunteeredLog.Clear()`, and wired both into `NewGameReset.Apply()`:
```csharp
if (AIMemoryStore.Instance != null) AIMemoryStore.Instance.Restore(null); // wipes turns/memories/standing
if (HALVolunteeredLog.Instance != null) HALVolunteeredLog.Instance.Clear();
```
(`HALCommentator`'s flag-trackers self-heal — they're edge-triggered on `LastSeen`, and
`EarlyGameProgress.ResetAll()` already runs on New Game — so re-acquisitions re-announce
correctly with no extra fix.)

### 2.3 Order-dependent gate machine → out-of-order players stall
**File:** `Assets/3 - Scripts/Story/StoryDirector.cs`

The original `CheckGates()` had **no `ColdOpen` case** and advanced **at most one step per
gameplay event**, assuming the player acts *after* being prompted. In a free Subnautica-style
opening a player can complete water+food+shelter in any order, or before first contact —
leaving `_step` behind the flags and stalling progression.

**Fix:** `CheckGates()` is now **idempotent and cascading** — a `do/while` loop that
re-evaluates from current flags and advances through **every** already-satisfied gate in one
pass. A throttled catch-up call runs in `Update()` (every `GateCheckInterval = 0.5s`, only
between `FirstContact` and the terminal `VillageSeam`) so any divergence reconciles regardless
of how it arose (out-of-order, pre-contact, or loading mid-progress). The per-event calls
remain for instant response on the common path. The **first-contact intro is protected** from
being skipped:
```csharp
void CheckGates()
{
    // Never skip the AI's first-contact intro; gate prompts may be overwritten freely
    // (an overwrite only happens when the newer gate is already satisfied → older prompt obsolete).
    if (_pendingConversationId == "conv_first_contact") return;
    bool advanced;
    do {
        advanced = false;
        switch (_step) {
            case StoryStep.FirstContact:
            case StoryStep.NeedsWaterFood:
                if (GetFlag("hasWater") && GetFlag("hasFood")) {
                    SetStoryStep(StoryStep.NeedsShelter); StartObjective("obj_shelter");
                    QueueConversation("conv_gates", "gate_shelter"); advanced = true;
                } break;
            case StoryStep.NeedsShelter:
                if (GetFlag("hasShelter")) {
                    SetStoryStep(StoryStep.Explore); StartObjective("obj_village");
                    QueueConversation("conv_gates", "gate_explore"); advanced = true;
                } break;
            case StoryStep.Explore:
                if (GetFlag("villageReached")) { SetStoryStep(StoryStep.VillageSeam); advanced = true; } break;
        }
    } while (advanced);
}
```

---

## 3. Feature: Tev's whistle (draws the player to the axe)

**File:** `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs`

`TevDialogue` is a scene MonoBehaviour on Tev's GameObject; it grants the axe on first talk
and derives all state from `EarlyGameProgress` flags. Added:

- **Serialized inspector fields** (the mp3 slot the user requested), appended after the
  existing typewriter-sound fields:
  ```csharp
  [Header("Whistle — draws the player to Tev once they've secured water or food")]
  [SerializeField] private AudioClip whistleClip;
  [SerializeField, Range(0f,1f)] private float whistleVolume = 0.6f;
  [SerializeField] private float whistleMaxDistance = 120f;
  private AudioSource whistleSource;
  ```
- A **dedicated 3D AudioSource** built in `Start()` (`spatialBlend = 1`, linear rolloff,
  `loop = true`) — separate from the 2D typewriter source so they never fight, and 3D so the
  whistle gives the player a *direction* to walk.
- `UpdateWhistle()` called from `Update()`, and an explicit stop in `StartConversation()`:
  ```csharp
  var sd = StoryDirector.Instance;
  bool waterOrFood = sd != null && (sd.GetFlag("hasWater") || sd.GetFlag("hasFood"));
  bool shouldWhistle = whistleClip != null && !_conversationActive
      && !EarlyGameProgress.TevReturnedDialogueDone   // stops once the player gets the axe
      && waterOrFood;                                 // starts after water OR food task
  ```

### 3.1 Bug found + fixed during playtest (important)
The whistle initially keyed off `EarlyGameProgress.WaterBottleDrunk` / `FirstMealEaten`. Those
flags are written **only in `TutorialSteps.cs`** — the forced tutorial that the revamp
**disabled** — so they never flip on the free-spawn path and the whistle never fired.
**Fix:** key off the `StoryDirector` flags `hasWater` / `hasFood`, which the new path sets via
`ResourceManager.OnCleanWaterDrunk` / `BonfireInteraction.OnEat`.
**Generalizable trap:** on the free-spawn opening, the legacy `EarlyGameProgress` water/food/
etc. flags are dead — use `StoryDirector` flags or the gameplay events, never those.

---

## 4. Feature: wood-gated shelter hint

The shelter hint track now leads with a "gather 50 wood" step that only shows when the player
is short, advances when they reach the target, and is skipped if they already have enough.

**Data schema** (`Assets/3 - Scripts/Story/DialogueData.cs`) — appended one field to `HintEntry`:
```csharp
// advanceEvent advances on a named gameplay event; gatherWoodTarget (>0) instead makes the
// entry a wood-gather gate that advances once WoodInventory.Wood reaches it (and is skipped on
// sight if the player already holds that much). Leave one of the two empty/0.
[Serializable] public class HintEntry { public string tipText = ""; public string advanceEvent = ""; public int gatherWoodTarget = 0; }
```

**Runner** (`Assets/3 - Scripts/Story/HintTrackRunner.cs`):
- `ShowCurrent()` skips a satisfied wood-gate on sight (`WoodInventory.Instance.Wood >= target`
  → `AdvanceEntry()`), so asking with ≥50 wood jumps straight to "place a Cabin".
- New `OnWoodChanged()` handler (subscribed to `WoodInventory.OnChanged`) advances the wood
  gate the instant the total reaches the target.
- Extracted a shared `AdvanceEntry()` from the old `Advance(firedEvent)`.

`WoodInventory.Wood` is **hotbar-derived** (`Hotbar.GetResourceTotal(ItemId.Wood)`), which is
exactly "wood in your hotbar," and `OnChanged` fires through `Hotbar.OnResourceChanged` — so
chopping trees (which adds wood to the hotbar) reliably triggers it.

**Authored content** — the `shelter` track in `hinttracks.json`:
```json
{ "id": "shelter", "objectiveId": "obj_shelter", "entries": [
  { "tipText": "Chop trees with your axe to gather 50 wood.", "gatherWoodTarget": 50 },
  { "tipText": "Open the build menu and place a Cabin to make a shelter.", "advanceEvent": "OnShelterBuilt" }
] }
```
The AI's spoken shelter lines in `conv_gates.json` (`gs_help`) and `conv_menu.json`
(`m_shelter`) were reworded so they no longer contradict the tip ("chop trees for wood, then
place a Cabin…").

---

## 5. Feature: flashlight + map askable tips (appear once the cabin is built)

### 5.1 New events
- **`PlayerFlashlight.OnToggled`** (`Assets/3 - Scripts/Player/PlayerFlashlight.cs`) — a
  `static event System.Action` fired in `CycleMode()` (which runs once per **E** press, after
  the mode changes). The flashlight's real logic is a 3-mode cycle `Off → Half(50%) → Full(100%)
  → Off`; firing on every press lets a 3-tip track advance in lockstep with the player.
- **`MapTutorial.OnOpened`** (`Assets/3 - Scripts/Map/MapTutorial.cs`) — a `static event`
  fired at the very top of `OnMapOpened()` (before any state gating, so it fires even after
  the map tutorial is "Finished"). `OnMapOpened()` is already invoked by
  `SolarSystemMapController` whenever the map opens.

### 5.2 Runner wiring
`HintTrackRunner.WireAdvance()` now also subscribes/unsubscribes both events, with handlers:
```csharp
void A_Flashlight() => Advance("OnFlashlightToggled");
void A_MapOpened()  => Advance("OnMapOpened");
```

### 5.3 Authored hint tracks (`hinttracks.json`)
```json
{ "id": "flashlight", "entries": [
  { "tipText": "Press E to switch on your flashlight - it starts at half brightness.", "advanceEvent": "OnFlashlightToggled" },
  { "tipText": "Press E again for full brightness.", "advanceEvent": "OnFlashlightToggled" },
  { "tipText": "Press E once more to switch it off. That's the full cycle.", "advanceEvent": "OnFlashlightToggled" }
] },
{ "id": "map", "entries": [
  { "tipText": "Open your phone and launch the Map app.", "advanceEvent": "OnMapOpened" }
] }
```
The flashlight track dismisses after the 3rd press (advancing past the last entry); the map
track dismisses the moment the player opens the map — handing off to the map's own tutorial.
Neither needs an `objectiveId` (they're not bound to a story objective).

### 5.4 Menu options (`conv_menu.json`)
Three options added to the free-time menu node `m0`, all gated `requiresFlag: "hasShelter"`
(so they appear only once the cabin is built), plus their target nodes. The flashlight/map
options start their hint tracks via the presentation-only `startHintTrack` field on the
confirm response (mirroring the existing water/food/shelter pattern). Full current
`conv_menu.json` is in §7.3.

> **Note on the "find the village" option:** the user described the post-cabin menu as
> "village + flashlight + map." The village beat already existed as the one-shot queued
> `conv_gates/gate_explore`; I additionally added a re-askable **"How do I find the village?"**
> menu option (`requiresFlag hasShelter`, `hiddenIfFlag villageReached`) so the menu matches
> that described state and the guidance is re-reachable. This was a small initiative beyond
> the literal ask — flagged for approval (user approved).

---

## 6. How the system works (architecture, for the reviewer)

**Story state — `StoryDirector` (DontDestroyOnLoad singleton):** holds `StoryStep`
(`ColdOpen→FirstContact→NeedsWaterFood→NeedsShelter→Explore→VillageSeam`), a string→bool flag
dict, active/completed objective sets, unlocked-question list, and a single "pending
conversation" channel. Persists via `SaveCollector` (`StoryDirectorSave`), resets via
`NewGameReset`. Auto-creates after scene load (skipping MainMenu) **and** is seeded in
`MainMenuController.EnsureGameplaySingletons()` (required because builds boot in MainMenu —
the "MainMenu singleton trap").

**Content — `StoryContent` (static loader):** at startup reads `StreamingAssets/Story/*.json`
via `JsonUtility`: `conv_*.json` → `Conversation`, `objectives.json` → objectives,
`hinttracks.json` → hint tracks. (Confirmed in the build log: `[Story] Loaded 3 conversations,
4 objectives, 3 hint tracks` — now 5 hint tracks after this session.)

**Dialogue — `DialogueRunner` + `DialoguePresenter`:** a speaker-agnostic runner walks a
`Conversation` (nodes of `lines[]` + `responses[]`) through a presenter interface. The phone
implements it as `PhoneDialoguePresenter` (AI lines stream as chat bubbles; player
`responses` render as buttons in `DialogueReplyColumn`, anchored to the **right** of the phone
chassis — typed input was removed). Responses are **flag-filtered** (`requiresFlag` /
`hiddenIfFlag` against `StoryDirector` flags) before display.

**Effects — fixed 7-effect vocabulary (`DialogueEffects`):** `SetFlag`, `AdvanceStory`,
`AddTrust`, `StartObjective`, `CompleteObjective`, `UnlockDialogue`, `TriggerEnding`. Each only
mutates `StoryDirector` state. `startHintTrack` is intentionally **outside** this set (it's
presentation-only).

**Objectives — bound to existing gameplay events:** `StoryDirector` subscribes to
`ResourceManager.OnCleanWaterDrunk`, `BonfireInteraction.OnEat`, `GhostPlacement.OnPlaced`,
`VillageReachTrigger.OnVillageReached`. Each handler sets the matching flag, completes any
objective whose `completionEvent` matches, and calls `CheckGates()`. The **do-it-yourself
player and the ask-the-AI player converge on the identical event fire** (the spec's core
"zero extra build" property).

**Hint tracks — `HintTrackRunner` (singleton):** renders one track at a time through the
**existing `TutorialUI` pill** (moved aside via `SetLeftSide`), advancing on the same gameplay
events (plus, now, wood-threshold, flashlight-toggle, and map-open). A track auto-dismisses
when it runs out of entries **or** when its bound objective completes (`CompleteByEvent` →
`StopTrack`). Tracks start only on an explicit `startHintTrack` from a chosen response — the
self-directed player never sees a pill.

**Opening flow:** `ColdOpen` → after `FirstContactDelay = 45s` the phone buzzes
(`PlayerPhoneUI.FlashNotification` + a `HALCommentator.VolunteerExternal` on-screen line) and
`conv_first_contact` is queued (discoverability risk #1). The first-contact node **branches on
silent flags** so a prepared player gets a "grace" acknowledgment instead of being told to do
what they've done (risk #2). Gates advance as in §2.3.

---

## 7. Full current authored content (`Assets/StreamingAssets/Story/`)

### 7.1 `objectives.json` (unchanged this session)
```json
{ "objectives": [
  { "id": "obj_water",   "description": "Drink clean water", "completionEvent": "OnCleanWaterDrunk", "hintTrackId": "water",   "onComplete": [] },
  { "id": "obj_food",    "description": "Cook and eat food", "completionEvent": "OnCookedFoodEaten", "hintTrackId": "food",    "onComplete": [] },
  { "id": "obj_shelter", "description": "Build a shelter",   "completionEvent": "OnShelterBuilt",    "hintTrackId": "shelter", "onComplete": [] },
  { "id": "obj_village", "description": "Reach the village", "completionEvent": "OnVillageReached",  "hintTrackId": "",        "onComplete": [] }
] }
```

### 7.2 `hinttracks.json` (water/food unchanged; shelter extended; flashlight/map new)
```json
{ "tracks": [
  { "id": "water", "objectiveId": "obj_water", "entries": [
    { "tipText": "There's a water bottle near the fire. Press F to pick it up.", "advanceEvent": "OnBottlePickedUp" },
    { "tipText": "Stand in the water and hold right-click to fill your bottle.", "advanceEvent": "OnBottleFilled" },
    { "tipText": "Hold left-click to drink from the bottle.", "advanceEvent": "OnCleanWaterDrunk" }
  ] },
  { "id": "food", "objectiveId": "obj_food", "entries": [
    { "tipText": "Pick up the fishing rod, then cast at the bank.", "advanceEvent": "OnBobberCast" },
    { "tipText": "Reel in a fish when the bobber wiggles.", "advanceEvent": "OnFishCaught" },
    { "tipText": "Cook your fish at a fire, then eat it.", "advanceEvent": "OnCookedFoodEaten" }
  ] },
  { "id": "shelter", "objectiveId": "obj_shelter", "entries": [
    { "tipText": "Chop trees with your axe to gather 50 wood.", "gatherWoodTarget": 50 },
    { "tipText": "Open the build menu and place a Cabin to make a shelter.", "advanceEvent": "OnShelterBuilt" }
  ] },
  { "id": "flashlight", "entries": [
    { "tipText": "Press E to switch on your flashlight - it starts at half brightness.", "advanceEvent": "OnFlashlightToggled" },
    { "tipText": "Press E again for full brightness.", "advanceEvent": "OnFlashlightToggled" },
    { "tipText": "Press E once more to switch it off. That's the full cycle.", "advanceEvent": "OnFlashlightToggled" }
  ] },
  { "id": "map", "entries": [
    { "tipText": "Open your phone and launch the Map app.", "advanceEvent": "OnMapOpened" }
  ] }
] }
```

### 7.3 `conv_menu.json` (free-time "talk to AI" menu — flashlight/map/village added)
```json
{ "id": "conv_menu", "nodes": [
  { "id": "m0", "speaker": "AI", "lines": [ "What do you need?" ], "responses": [
    { "buttonText": "How do I get clean water?", "nextNodeId": "m_water",   "hiddenIfFlag": "hasWater" },
    { "buttonText": "How do I find food?",       "nextNodeId": "m_food",    "hiddenIfFlag": "hasFood" },
    { "buttonText": "How do I build a shelter?", "nextNodeId": "m_shelter", "hiddenIfFlag": "hasShelter" },
    { "buttonText": "How do I find the village?","nextNodeId": "m_village", "requiresFlag": "hasShelter", "hiddenIfFlag": "villageReached" },
    { "buttonText": "How do I use the flashlight?","nextNodeId": "m_flashlight", "requiresFlag": "hasShelter" },
    { "buttonText": "How do I use the map?",     "nextNodeId": "m_map",     "requiresFlag": "hasShelter" },
    { "buttonText": "Nothing right now.",        "nextNodeId": "end" }
  ] },
  { "id": "m_water",   "speaker": "AI", "lines": [ "Water: grab a bottle, fill it at the bank, drink. Tips are on your HUD." ],
    "responses": [ { "buttonText": "Thanks.", "nextNodeId": "m0", "effects": [ { "kind": "StartObjective", "strArg": "obj_water" } ], "startHintTrack": "water" } ] },
  { "id": "m_food",    "speaker": "AI", "lines": [ "Food: catch a fish, cook it at a fire, then eat. Tips are on your HUD." ],
    "responses": [ { "buttonText": "Thanks.", "nextNodeId": "m0", "effects": [ { "kind": "StartObjective", "strArg": "obj_food" } ], "startHintTrack": "food" } ] },
  { "id": "m_shelter", "speaker": "AI", "lines": [ "Shelter: chop trees for wood, then place a Cabin from the build menu. Tips are on your HUD." ],
    "responses": [ { "buttonText": "Thanks.", "nextNodeId": "m0", "effects": [ { "kind": "StartObjective", "strArg": "obj_shelter" } ], "startHintTrack": "shelter" } ] },
  { "id": "m_village", "speaker": "AI", "lines": [ "Head out into Humble Abode and look for the main village - that's where the answers are." ],
    "responses": [ { "buttonText": "On it.", "nextNodeId": "m0" } ] },
  { "id": "m_flashlight", "speaker": "AI", "lines": [ "Your flashlight cycles with E: once for half brightness, again for full, once more to switch it off. I'll put the steps on your HUD." ],
    "responses": [ { "buttonText": "Got it.", "nextNodeId": "m0", "startHintTrack": "flashlight" } ] },
  { "id": "m_map", "speaker": "AI", "lines": [ "Open your phone and launch the Map app - it'll walk you through the rest from there. Tip's on your HUD." ],
    "responses": [ { "buttonText": "Got it.", "nextNodeId": "m0", "startHintTrack": "map" } ] }
] }
```

### 7.4 `conv_first_contact.json` (unchanged) and `conv_gates.json` (gs_help reworded)
`conv_first_contact` briefs the mission and branches on `hasWater`/`hasFood` (with a "grace"
node for the prepared player). `conv_gates` holds `gate_shelter` / `gate_explore` (the queued
post-gate acknowledgements). The only edit this session: `conv_gates/gs_help` line →
`"You'll need wood first - chop trees with your axe, then place a Cabin from the build menu.
Tips are on your HUD."`

---

## 8. Tutorial-coverage audit (answering "what else should be askable?")

Cross-referenced every step in the legacy `_LegacySteps.cs` / `TutorialSteps.cs` against the
current free-roam.

**Covered by on-ask tips (water / food / shelter / flashlight / map):** water bottle pickup/
fill/drink; rod pickup, cast, reel, cook-at-fire, eat; wood chop + build menu + place cabin;
flashlight cycle; map open.

**Vendor- / purchase-gated → handled by their own tutorial prompts (correctly out of scope):**
ship (pilot, up/down/directional thrust, roll, repair, hatch buttons), gun, axe-as-weapon,
**and the jetpack** — verified `PlayerController.jetpackUnlocked = false` by default, bought
from Alien7; all three thrust types are suppressed until then. (This corrected an earlier
assumption that jetpack/movement was an early-game gap — it is not.)

**Self-evident → intentionally skipped:** mouse-look, WASD, sprint, jump, talk-to-NPC (Tev's
own interact prompt "Press E to talk" already shows).

**Genuine remaining gaps (flagged to user; not yet actioned):**
1. **Axe-swing input** — the wood tip says "chop trees with your axe" but never states
   *left-click to swing*. One-line tip tweak available on request.
2. **"Find the village" has no direction** — the prompt says "look for the village" but there
   is currently no compass waypoint on the new path (the old flow had Tev give coords). This
   is a **village-seam design decision**, not a tip gap.

**Conclusion:** tutorial integration for the **pre-village free-roam is functionally complete**
— every mechanic the player uses before the village that isn't vendor-gated is now askable.

---

## 9. Verification status

- **Compilation:** clean after every change (verified via the Unity MCP `check_compile_errors`;
  no CLI build/test exists for this project).
- **JSON validity:** all five `StreamingAssets/Story/*.json` files parse (validated).
- **Runtime load confirmed earlier in the build log** (`[Story] Loaded …`). New tracks/options
  **not yet runtime-verified in a fresh build** — the user playtests the built player and must
  rebuild.
- **Items the user should eyeball next build:** (a) Tev whistles right after drinking water;
  (b) flashlight tips advance on E (depends on the Flashlight ability being unlocked on the
  free-spawn path — believed true per the "abilities unlocked" spawn change, worth confirming);
  (c) map tip dismisses on map open; (d) shelter tip shows "gather 50 wood" then flips to "place
  a Cabin"; (e) second in-process New Game now shows dialogue and a clean transcript.

---

## 10. Files touched this session

**C# (code):**
- `Assets/3 - Scripts/Story/StoryDirector.cs` — New-Game/Load timer resets; idempotent
  cascading `CheckGates()` + throttled catch-up in `Update()`.
- `Assets/3 - Scripts/Story/HintTrackRunner.cs` — wood-gate skip/advance, `AdvanceEntry`
  refactor, `OnWoodChanged`, flashlight + map event subscriptions/handlers.
- `Assets/3 - Scripts/Story/DialogueData.cs` — `HintEntry.gatherWoodTarget`.
- `Assets/3 - Scripts/SaveSystem/NewGameReset.cs` — reset AI memory + volunteered log.
- `Assets/3 - Scripts/AI/HALVolunteeredLog.cs` — added `Clear()`.
- `Assets/3 - Scripts/NPC_Dialogue/TevDialogue.cs` — whistle clip/source/logic.
- `Assets/3 - Scripts/Player/PlayerFlashlight.cs` — `OnToggled` event.
- `Assets/3 - Scripts/Map/MapTutorial.cs` — `OnOpened` event.

**Content (JSON, `Assets/StreamingAssets/Story/`):**
- `hinttracks.json` — shelter wood-gate; new flashlight + map tracks.
- `conv_menu.json` — flashlight/map/village options + nodes.
- `conv_gates.json` — reworded `gs_help` line.

**Not committed yet.** No new files (all edits to tracked files), so a commit is
straightforward when approved.

---

## 11. Suggested questions for the external reviewer

1. Does the on-ask hint model still match spec §7 after adding **non-objective** tracks
   (flashlight/map have no `objectiveId`)? They dismiss by running out of entries / on
   map-open rather than by objective completion — is that an acceptable extension?
2. Is gating flashlight/map behind `hasShelter` the right trigger, or should the flashlight be
   askable earlier (it's useful in the dark before a cabin exists)?
3. Is the `gatherWoodTarget` extension to `HintEntry` an acceptable widening of the data model,
   or should wood-gating be expressed differently (e.g. a generic numeric-threshold advance)?
4. The `CheckGates` "first-contact intro is protected, gate prompts overwrite freely" rule —
   any edge case where overwriting a queued gate prompt loses needed information?
5. Anything in the GDD's "three risks" (phone discoverability, grace, trust-middle) that this
   work weakened or left unaddressed before Tev's first mission?
```

This is `docs/AI_Tutorial_Revamp_Review_2026-06-02.md`. Hand it to the web Claude alongside
the spec/GDD/plan for the fullest comparison.
