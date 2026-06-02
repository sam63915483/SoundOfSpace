# Vertical Slice — Build Plan

> **What this document is:** A self-contained build spec for the game's first
> vertical slice: one linear story thread that runs start-to-finish, built on a
> reusable, data-driven dialogue/story backbone. It is written to be handed
> directly to an implementer (e.g. Claude Code) with no other context required.
>
> **How to read the tags:**
> - **[EXISTS]** — already built and live in the project; do not rebuild, integrate with it.
> - **[BUILD]** — new work for this slice.
> - **[AUTHOR]** — content/writing work (dialogue lines), not code.
> - **[TEST]** — a verification step that must pass before moving on.
>
> **Status:** DRAFT v1 — vertical-slice backbone + revamped opening.
> **Goal of this slice:** one complete path from spawn → ending that integrates
> the existing survival, save, and death systems. Other paths/endings are
> explicitly out of scope and bolt on later.

---

## 0. Context for the implementer (read first)

This is a single-player, Unity, solar-system survival game. The player crash-lands
with amnesia, is taken in by an alien named **Tev**, and survives (fishing,
cooking, building, mining, fighting) while a mystery unfolds. The secret premise:
every death is real, and the player merges into a new clone in a new timeline each
time — so the death counter is literally how many timelines have died to get here.

### Systems that already exist — integrate, do NOT rebuild

- **[EXISTS] Survival loop** — fishing/rod, cooking, drinking water, building,
  mining, combat. All playable today.
- **[EXISTS] `ResourceManager`** — owns health/hunger/thirst; **fires `OnDeath`
  when health hits 0.**
- **[EXISTS] Save system** — `SaveCollector → ApplyState`; `NewGameReset`;
  `totalDeaths` persists across saves (`SetTotalDeaths`). `isDead` is transient.
- **[EXISTS] `DeathCutsceneController`** — a code-built auto-singleton (seeded in
  `EnsureGameplaySingletons`) that catches `OnDeath`, plays the timeline-tree
  cutscene, then reloads the newest save so the player resumes as a new clone.
  Geometry derives purely from `totalDeaths`. **This is the architectural pattern
  to copy for the new StoryDirector singleton.**
- **[EXISTS] Phone app** — the personal AI. The LLM that used to drive it has been
  **removed**; the backbone remains. It still sends **hardcoded ambient messages**
  (low health, enemies nearby, entering/exiting atmospheres). These are NOT
  LLM-driven and should be left exactly as-is.

### Design decision driving this whole plan

The LLM is gone. **All AI and Tev dialogue is now authored** as fixed nodes with
preset player responses. This is intentional: authored dialogue is predictable,
testable, and can deliver a specific story beat on cue — which a critical path
requires and an unpredictable LLM cannot. The player only ever picks from buttons
we wrote. There is zero generative text at runtime.

### The core architectural principle

**Author content, not code.** Build four empty, story-agnostic systems first, then
pour the slice's content into them as data. Adding future paths/endings later =
authoring more data that reuses the same systems, never rewriting the engine.

---

## 1. The reusable backbone (build BEFORE any story content)

Four systems. None of them know anything about the actual story — they are empty
machines.

### 1.1 StoryDirector — the brain `[BUILD]`

- Code-built singleton, persists across deaths **exactly like
  `DeathCutsceneController`** (seed it in `EnsureGameplaySingletons`).
- Saves through the **same `SaveCollector → ApplyState` pattern** that `totalDeaths`
  already uses.
- Holds exactly three pieces of state:
  - `currentStoryStep` (int/enum — where we are on the critical path)
  - `tevTrust` (single float — **one** meter for the slice; the two-meter system is
    out of scope)
  - `flags` (dictionary of named bools — e.g. `"metTevPrivately"`, `"filesFound"`,
    `"hasWater"`, `"hasFood"`, `"hasShelter"`)
- This is the **only** new state that must survive a death-reload.

### 1.2 Dialogue data — content separated from code `[BUILD]`

- Author conversations as **ScriptableObjects or JSON assets**, never hardcoded C#.
- A conversation is a small graph of nodes:
  - **DialogueNode**: `id`, `speaker` (AI | Tev), `lines[]` (shown in sequence),
    `responses[]`.
  - **PlayerResponse**: `buttonText`, `nextNodeId` (or `"end"`), `effects[]`.
- A **DialogueRunner** `[BUILD]` displays a node's lines, shows the response
  buttons, and applies the chosen response's effects. One runner serves both the
  AI and Tev — they're just different `speaker` values.

### 1.3 The effect vocabulary — small and FIXED `[BUILD]`

Nodes/responses may only fire from this closed set. **Resist adding to it** — the
constraint is what keeps future content cheap:

- `SetFlag(name, value)`
- `AdvanceStory(step)`
- `AddTrust(amount)`
- `StartObjective(id)`
- `CompleteObjective(id)`
- `UnlockDialogue(id)` — makes a new preset AI question available in the phone
- `TriggerEnding(id)`

Because the vocabulary is fixed, a future branch is just a response with a
different `AdvanceStory`/`SetFlag` target — no new engine code.

### 1.4 Objective system — and the Forest-style trust model `[BUILD]`

- **Objective**: `id`, `description` (shown in phone), `completionCondition`,
  `onComplete` effects.
- **Key design choice:** wire completion conditions to **gameplay events that
  already fire**, not bespoke scripted setpieces. You already have `OnDeath`; add a
  small set of new events and have objectives subscribe to them:
  - `OnCleanWaterDrunk`
  - `OnCookedFoodEaten`
  - `OnShelterBuilt` (define minimal condition — e.g. N placed structure pieces, or
    one enclosed buildable)
  - `OnVillageReached` (trigger volume at the village)
  - (later) `OnResourceDelivered`, `OnItemCrafted`, `OnBackroomsEntered`,
    `OnAtmosphereExited`
- This makes "earn Tev's trust" complete through normal survival/exploration, so
  the self-directed player progresses with **zero bespoke missions to build.**

### 1.5 Phone app wiring `[BUILD]`

- **[EXISTS]** Keep the ambient channel (health/enemies/atmosphere) untouched.
- **[BUILD]** Add a **story-message channel** — authored AI texts arrive here,
  driven by StoryDirector.
- **[BUILD]** Add a **"talk to AI" screen** — shows whichever preset questions are
  currently unlocked.
- Tev uses the **same DialogueRunner**, triggered by proximity or a "call Tev"
  option.
- Net: one dialogue engine, two speakers, three phone surfaces.

### ✅ Backbone checklist (all must pass before writing story)

- [ ] `[BUILD]` StoryDirector singleton created + seeded in `EnsureGameplaySingletons`
- [ ] `[BUILD]` StoryDirector state saves/loads via `SaveCollector → ApplyState`
- [ ] `[TEST]` Set `currentStoryStep = 3`, die, confirm it is still `3` in the cabin after the death-reload
- [ ] `[BUILD]` Dialogue data format (ScriptableObject/JSON) + DialogueRunner
- [ ] `[TEST]` A throwaway 2-node conversation runs and applies an effect (e.g. `SetFlag`)
- [ ] `[BUILD]` Fixed effect vocabulary implemented (the 7 effects above)
- [ ] `[BUILD]` Objective system + new gameplay events wired
- [ ] `[TEST]` `tevTrust` rises from a normal gameplay action with no story authored
- [ ] `[BUILD]` Phone story-message channel + "talk to AI" screen

---

## 2. The revamped opening — the slice's front half

**Design goal:** delete the forced step-by-step tutorial. Spawn the player free,
let them hit "what now?", and make the AI the source of direction. Guidance is
**always optional**; completion is **always** a gameplay event. "Ask the AI" and
"explore on your own" converge on the identical objective fire — so the
self-directed player costs **zero** extra build.

> **Reference:** this is the Subnautica PDA model. Replaying Subnautica's first
> ten minutes is the best spec for pacing and tone this opening can have.

### The shape: three soft gates over five steps

- **Step 0 — Cold open / pre-contact.** Spawn in the cabin, full freedom, no
  instruction. AI silent except existing ambient pings. Player hits the deliberate
  "okay… what now?" beat.
- **Step 1 — First contact.** Player opens phone → AI briefs the mission, states
  first step: adapt — get clean water and food. Unlocks preset questions
  *"How do I get clean water?"* / *"How do I find food?"*; walking off and doing it
  yourself is the implicit third option.
- **Gate 1 — Water + food.** Objectives `OnCleanWaterDrunk` + `OnCookedFoodEaten`.
  Both flags true → AI acknowledges, advances step.
- **Step 2 — Shelter.** AI: you need a base to survive the journey. Objective
  `OnShelterBuilt`.
- **Step 3 — First real phase.** Shelter built → AI: explore Humble Abode, find the
  main village, look for clues. Objective `OnVillageReached`.
- **Step 4 — Village / Tev handoff.** Reaching the village triggers Tev's first
  mission. **This is the seam where the opening connects to the rest of the slice
  spine (§3).**

### The three risks to nail (these are where it breaks)

1. **Phone discoverability — the single point of failure.** A no-tutorial start
   fails fatally if the player never realizes the phone is the source of direction.
   **Fix `[BUILD]`:** after the player has moved around for a bit (or after
   ~30–60s), the phone buzzes once with a visible "new message / incoming
   transmission" cue. One diegetic ping teaches "check the phone" with no popup.
2. **Grace for the self-directed player `[BUILD]`.** A player may fish/drink/build
   *before* opening the phone. The objective flags should set **silently in the
   background** regardless of story step. First-contact dialogue then **checks those
   flags** and branches its acknowledgment (e.g. *"I see you've already secured food
   and water. Good. That makes this easier."*) and skips ahead to what's undone.
   Trivial now, annoying to retrofit — bake it in.
3. **Make gates diegetic, not gamey `[AUTHOR]`.** The "AI won't help find the files
   until you've survived" gate needs an in-fiction reason or it reads as an
   arbitrary locked door. Give the AI a practical motive (*"You won't last the trip
   to those files if you can't keep yourself alive. Get on your feet first."*). Same
   for the shelter gate (a foothold before a journey).

### ✅ Opening checklist

- [ ] `[BUILD]` Tutorial removed; player spawns free in the cabin at Step 0
- [ ] `[BUILD]` Phone "buzz once" discoverability cue (timer/movement-triggered)
- [ ] `[BUILD]` Objective flags set silently in background regardless of story step
- [ ] `[AUTHOR]` First-contact dialogue with flag-checking grace branch
- [ ] `[AUTHOR]` Preset questions: clean water how-to, food how-to
- [ ] `[BUILD]` Gate 1 advance logic (water + food flags → Step 2)
- [ ] `[AUTHOR]` Shelter prompt + how-to dialogue; `[BUILD]` `OnShelterBuilt` → Step 3
- [ ] `[AUTHOR]` Explore-Humble-Abode prompt; `[BUILD]` `OnVillageReached` → Step 4
- [ ] `[TEST]` Full opening completed BOTH ways (ask-the-AI path and ignore-the-AI path) reaches the village in the same state

---

## 3. The full slice spine — one path, one ending

Seven beats, fully linear, one outcome. This is the whole game minus the branches.
Beats 1–3 below overlap the opening (§2); beats 4–7 are the back half.

1. **First contact** (= Step 1). AI comes online, soft open, no agenda yet.
2. **The agenda surfaces.** Over a couple of check-ins the AI mentions *files*
   somewhere in this system it needs recovered — framed benevolently (they'll help
   you understand what happened / get home). Points you at Tev.
3. **The trust middle (Forest-style).** Tev gives tasks that map onto existing
   systems (gather, build, explore, mine, fight); each completion calls `AddTrust`.
   AI nudges via preset questions. This is the bulk of playtime and nearly none of
   the build cost.
4. **The eerie thread.** As the player inevitably dies and sees the timeline
   cutscene, the AI's authored lines escalate — it calls you "Astronaut Number N,"
   dramatic irony now, horror later. Reads off existing `totalDeaths`; costs
   nothing extra.
5. **Reveal of the location** (gated: `tevTrust >= threshold`). New dialogue
   unlocks; the files' location surfaces — the Backrooms.
6. **The Backrooms dive — the Forest cave.** Descend, navigate past the persistent
   bodies of past selves (lore + dread delivered environmentally), find the files.
   Reading them is the reveal: the truth about the timelines, the player's
   condition, what N has meant.
7. **One ending.** Final AI conversation → one ending cutscene (reuse the
   seam-hiding/async-load techniques from `DeathCutsceneController`). Pick the
   simplest *satisfying win*. **Structure the final conversation as a choice node
   that currently has one real option**, so the fork point physically exists in the
   data and future endings are sibling nodes, not a rebuilt finale.

### ✅ Spine checklist

- [ ] `[AUTHOR]` Beat 2 — AI introduces the files
- [ ] `[BUILD]` Tev first mission at the village + `AddTrust` on completion
- [ ] `[AUTHOR]` Beat 3 — Tev trust-middle task dialogue
- [ ] `[AUTHOR]` Beat 4 — AI death-escalation lines keyed to `totalDeaths`
- [ ] `[BUILD]` `tevTrust >= threshold` gate → unlock location reveal
- [ ] `[BUILD]` File object placed in the Backrooms + read/reveal trigger
- [ ] `[AUTHOR]` Beat 6 — the reveal text (timelines / condition / meaning of N)
- [ ] `[BUILD]` Ending cutscene (reuse death-cutscene techniques)
- [ ] `[BUILD]` Final choice node with one live option (fork pre-built for later)

---

## 4. Build order (do in this sequence)

**Phase A — the empty machine.** Backbone §1, in order, each passing its `[TEST]`
before the next. **Write no real story until all of §1's checklist passes** —
story stacked on a leaky backbone is the worst debugging there is.

**Phase B — pour in the opening.** §2: removal of the tutorial, discoverability
ping, grace branch, then author beats for Steps 1–3. Get spawn → village running.

**Phase C — pour in the back half.** §3 beats 4–7: Tev mission, trust gate,
Backrooms file, reveal, single ending.

**Phase D — integrate + harden.** Wire AI death-escalation to `totalDeaths`.
Playtest the full thread start-to-finish. Then the edge pass: complete the opening
both ways; skip/repeat-death testing on the new ending cutscene; confirm
StoryDirector state survives deaths at every step.

---

## 5. Out of scope for this slice (bolts on later — do NOT build now)

- Second trust meter (ORG) — a future second float.
- The tell-the-AI / mole mechanic — future dialogue data, same effect vocabulary.
- Additional endings — sibling nodes off the §3 beat-7 choice already pre-built.
- Bespoke per-mission content beyond the one Tev thread.

> The design's whole payoff: **nothing built for this slice is thrown away when the
> branches arrive.** They are authored as data on top of the same backbone.

---

## 6. The one thing the implementer cannot do for you

Killing the LLM means the AI's entire personality now lives in the authored lines.
The systems above are the easy 80% an AI assistant can scaffold quickly. The dozen
or so lines that carry the AI's voice — the briefing, the "you're a survivor"
grace beat, the eerie death-escalation edge — are the 20% that *is* the game, and
they must be written and judged by a human playtesting the feel. Scaffold the
machine fast; write the voice slowly.

---

*End of v1. Next after the slice ships: open §5 and add the branches as data.*
