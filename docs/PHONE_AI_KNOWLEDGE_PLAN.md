# Phone AI — Game Knowledge & Personality System
## Implementation Plan for Claude Code

This is a build plan. It assumes the **Phone AI App is already shipped** (see `PHONE_AI_OVERVIEW.md`). The goal is to bolt a real, authored *game knowledge base* and a *story-aware personality* onto the existing AI so it stops sounding like a generic chat model and starts sounding like a crew companion who actually knows this game's universe.

**Claude Code: read Section 9 first.** It tells you what to research in the project before writing any code. Do not skip it — half this plan depends on facts only the project can tell you (where planets are defined, whether a death counter exists, how quests fire events).

---

## 1. The problem we're solving

The shipped AI runs on a small local model (Qwen 2.5 1.5B). Its only "knowledge" is a thin `What you know` paragraph baked into the system prompt. As a result it **hallucinates** — asked about the in-game planet *Humble Abode* it answered "that's Earth" and listed the pyramids and New York. It is filling gaps with real-world training data because the game gave it nothing better.

We want the AI to:

- Know every planet, location, faction, item and story beat **as authored by the developer**, not invented.
- Answer *"what should I do on Humble Abode?"* with real in-game content ("check the scenery, green grass, concerts at night, a thriving alien community").
- Keep **Earth** (humanity's lost origin) and **Humble Abode** (an in-game planet) as two completely distinct things and never conflate them.
- Give **exact, scripted answers** to a few identity/mission questions ("Who am I?" → *"You are Astronaut Number 1."* and nothing more).
- Let the developer **add or remove knowledge from one hand-editable file**, no recompile.
- **Shift personality as the story advances** — most importantly, turn uneasy/resistant once the player starts handing the stolen ORG files to the enemy.

---

## 2. The core design decision: two separate systems

The existing build has an `AIMemoryStore`. **Do not extend it for game lore.** They are different things and merging them would corrupt both:

| | **Agent Memory Store** (already exists — leave alone) | **Game Knowledge Base** (NEW — this plan) |
|---|---|---|
| Contents | What *this* player did/said | Canon: planets, factions, mission, lore |
| Source | Extracted from chats by the LLM | Hand-authored by the developer |
| Lifetime | Per save file, mutates constantly | Ships with the game, same for everyone |
| Storage | Inside the save JSON | One plain-text file in `StreamingAssets` |
| Editable by hand? | No — eviction/dedupe would wreck it | **Yes — that is the whole point** |

So we are adding a **third pillar** next to `LLMService` and `AIMemoryStore`: a `GameKnowledgeBase`. The system prompt will end up assembled from four parts:

```
[ PERSONA for current story phase ]      ← from the Knowledge Base file
[ CORE CANON — always included ]          ← from the Knowledge Base file
[ RETRIEVED lore relevant to the question ]← from the Knowledge Base file
[ MEMORIES about this player + standing ] ← from the existing AIMemoryStore
```

The "one file the developer edits" the user asked for is the **Knowledge Base file**. It holds *both* the AI's knowledge *and* its personality — it is, literally, the AI's brain. The `AIMemoryStore` stays where it is and is not touched by this plan.

---

## 3. The Knowledge Base file

### 3.1 Location & format

- **One file:** `Assets/StreamingAssets/AI/game_knowledge.md`
  - `StreamingAssets` means it can be edited in a shipped build without recompiling — exactly the "easily open and add/remove stuff" requirement.
  - Plain Markdown-flavoured text so it opens in any editor and reads cleanly.
- The runtime parses it once on load into an in-memory index. Add an **editor hot-reload** (re-parse on file change while in Play mode) so the developer can tune lore live.
- The format below is deliberately forgiving — the parser must tolerate extra blank lines, missing optional fields, and inconsistent casing. A typo in the file should drop *one entry*, never crash the AI.

### 3.2 File structure

The file is a list of **blocks**. Two block types: `PERSONA` and `ENTRY`.

```
## PERSONA: phase1
keywords: -
---
You are a mysterious intelligence inhabiting a salvaged smartphone...
(full persona text for story phase 1)


## ENTRY: Humble Abode
mode: grounding
phase: all
keywords: humble abode, abode, home base, the abode
---
Humble Abode is a lush garden planet and the crew's home base. Rolling
green grass, open skies, live concerts after dark, and a thriving,
welcoming community of aliens. It is safe, social, and a good place to
resupply between missions.


## ENTRY: Player Identity
mode: verbatim
phase: all
intent: who am i, who am i really, what am i, my name, my identity, identify me, tell me about myself
---
You are Astronaut Number {ASTRONAUT_NUMBER}.


## ENTRY: The Mission
mode: core
phase: phase1, phase2
keywords: mission, objective, what do i do, my goal, the org files, org files
---
Your mission: recover the stolen ORG files. They were taken by rebel
forces. Track the rebels across the system, explore every planet for
leads and intelligence, and — above all — stay alive.
```

### 3.3 Block fields

**Header line** — `## PERSONA: <phase>` or `## ENTRY: <title>`. The title is a human label and also the de-dupe key.

**Metadata lines** (before the `---` separator):

- `mode:` — only on `ENTRY` blocks. One of:
  - `core` — **always** injected into the system prompt, every turn, regardless of the question. Use sparingly: mission summary, the planet roster, the hard Earth-vs-Humble-Abode rule. Keep total `core` text small (a 1.5B model drowns in long prompts).
  - `grounding` — injected **only when the player's message matches its `keywords`**. This is the bulk of the catalog (planet descriptions, faction lore, item info). The LLM phrases the final answer using this as reference.
  - `verbatim` — if the player's message matches its `intent`, the body text is returned **directly as the AI's reply, the LLM is not called at all**. Guarantees exact, terse, on-canon answers. Use for identity and any "must be word-perfect" answer.
- `phase:` — which story phases this block is active in. `all`, or a comma list (`phase1, phase2`). Inactive blocks are invisible to the AI. This is how lore and persona evolve with the plot.
- `keywords:` — comma-separated trigger words for `grounding` retrieval. Match is case-insensitive substring on the player's message.
- `intent:` — comma-separated trigger phrases for `verbatim` interception. Should be stricter than keywords (these short-circuit the LLM entirely).

**Body** — everything after the `---` line until the next `##` block. Free text. May contain `{TOKENS}` (Section 3.4).

### 3.4 Dynamic tokens

Body text may contain `{TOKEN}` placeholders resolved at runtime against live game state. This is how authored text references things that change during play.

Starter token set (Claude Code wires each to a real game source — see Section 9):

| Token | Resolves to |
|---|---|
| `{ASTRONAUT_NUMBER}` | death count + 1 (see Section 5) |
| `{CURRENT_PLANET}` | the planet the player is currently on |
| `{STORY_PHASE}` | current story phase label |
| `{PLAYER_DEATHS}` | raw death count |

Rules: an unknown token is left untouched (so a typo is visible, not silently blank). Token resolution happens **after** block selection, on whatever text is about to enter the prompt or be returned verbatim. Adding a new token later = one entry in a `TokenResolver` switch; document this so the developer knows it needs a code change (tokens are the one part of "the brain" that isn't pure text).

---

## 4. Runtime components to build

All new files under `Assets/3 - Scripts/AI/`.

### 4.1 `GameKnowledgeBase.cs` — auto-singleton MonoBehaviour

Mirror the existing `AIMemoryStore` patterns exactly: auto-singleton, `MainMenu` early-return, **and** explicit seeding in `MainMenuController.EnsureGameplaySingletons()` (the project's "MainMenu trap" — the overview calls this out; do not skip it).

Responsibilities:
- On first access, load and parse `StreamingAssets/AI/game_knowledge.md` into `List<KnowledgeEntry>` + `Dictionary<phase, personaText>`.
- Editor-only: watch the file, re-parse on change.
- Expose:
  - `string GetPersona(StoryPhase phase)`
  - `string GetCoreCanon(StoryPhase phase)` — all `core` entries for the phase, concatenated.
  - `List<KnowledgeEntry> Retrieve(string playerMessage, StoryPhase phase, int maxEntries)` — `grounding` retrieval, see 4.2.
  - `string TryVerbatim(string playerMessage, StoryPhase phase)` — returns the resolved body of the first matching `verbatim` entry, or `null`.
- Parsing must be defensive: a malformed block is logged and skipped, never fatal.

```csharp
enum KnowledgeMode { Core, Grounding, Verbatim }

[Serializable] class KnowledgeEntry {
    public string title;
    public KnowledgeMode mode;
    public List<string> phases;     // empty/"all" => every phase
    public List<string> keywords;
    public List<string> intents;
    public string body;
}
```

### 4.2 Retrieval (deterministic, no embeddings)

Keep it dumb and free — no vector DB, no extra packages:

1. Lowercase the player message.
2. For each `grounding` entry active in the current phase, score = number of its `keywords` found as substrings in the message.
3. Drop score 0. Sort by score desc, then shorter body first (prefer focused entries).
4. Return the top `maxEntries` (start with **4**).

This is plenty for a hand-authored catalog of dozens-to-low-hundreds of entries, and it is deterministic — same question, same lore, every time. If the catalog ever grows into the thousands, *then* consider embeddings; note it as future work, don't build it now.

### 4.3 `TokenResolver.cs` — static helper

`string Resolve(string text)` — replaces every `{TOKEN}` via a switch over live game systems. One method, easy to extend. Unknown tokens pass through unchanged.

### 4.4 Intent intercept — wired into the chat send path

Before `LLMService.Chat` calls the model, call `GameKnowledgeBase.TryVerbatim(message, phase)`:
- **Hit** → push the resolved verbatim text straight into the AI bubble as the reply. Skip the LLM entirely. Still record the turn into `AIMemoryStore` so memory/standing logic stays consistent.
- **Miss** → proceed to the normal LLM path.

This is what makes *"Who am I?"* reliably return *"You are Astronaut Number 4."* and nothing else — a 1.5B model cannot be trusted to be that disciplined, so for must-be-exact answers we don't ask it to.

### 4.5 `LLMService.BuildSystemPrompt` — rewrite the prompt assembly

Replace the hard-coded `Persona` + `What you know` blocks with:

```
1. GameKnowledgeBase.GetPersona(currentPhase)
2. "ESTABLISHED FACTS (treat as absolute truth, never contradict):"
   + GameKnowledgeBase.GetCoreCanon(currentPhase)
3. "POSSIBLY RELEVANT LORE:" + Retrieve(playerMessage, phase, 4)
   joined as short bullet lines
4. existing "What you remember about this human:" + standing block
5. existing "Recent conversation:" block
```

Then run the **entire assembled prompt** through `TokenResolver.Resolve` so `{tokens}` work everywhere, including persona.

Add one explicit anti-hallucination instruction to the persona/core text, e.g.: *"If the established facts and relevant lore do not cover the question, say you do not have that information. Never substitute real-world facts for game facts. Earth and Humble Abode are different places — never call one the other."* Small models obey blunt, specific negative rules far better than subtle ones.

---

## 5. The "Astronaut Number N" mechanic

Desired behaviour: *"Who am I?"* → `"You are Astronaut Number 1."` — and **only** that. After the player dies and asks again → `"You are Astronaut Number 2."` etc.

Implementation:
- It is a **`verbatim`** entry (Section 4.4) whose body is `You are Astronaut Number {ASTRONAUT_NUMBER}.`
- `{ASTRONAUT_NUMBER}` resolves to **death count + 1**.
- Claude Code: **find the death counter first** (Section 9). If the game already tracks deaths/respawns, bind to it. If not, add an `int deaths` to the save data and increment it wherever player death/respawn is handled. Persist it through save/load like every other stat.
- Because it is `verbatim`, the model never sees this question — the answer is always exact and always terse, which is exactly the "they won't tell you more" tone the user wants.

---

## 6. Story-phase personality system

The user wants the AI's personality to *change* — specifically to become uneasy, then resistant, once the player begins handing the stolen ORG files to the enemy. The existing `standing` axis (chat-driven, −100..+100) is **not** this. Standing reacts to how politely the player chats; we need something that reacts to **plot events**.

### 6.1 Story phase variable

Add a `StoryPhase` (enum or int) to the save data. Suggested arc — tune to the real story:

| Phase | Trigger | AI disposition |
|---|---|---|
| `Phase1_Loyal` | New game | Helpful companion. Believes in the mission. Mysterious about its own origin. |
| `Phase2_Uneasy` | Player first hands ORG files toward the enemy | Still helpful, but adds doubts. Asks if the player is sure. Quieter. |
| `Phase3_Resistant` | The files' true nature is revealed / further handovers | Pushes back. Reluctant. May withhold help, question the player's choices, reference earlier loyalty as betrayal. |

Phase only ever advances forward. It is set by **quest/story events**, not by chat. Claude Code: find the quest or event system (Section 9) and add `GameKnowledgeBase.SetStoryPhase(...)` calls at the relevant ORG-file story beats — or expose a small API the quest scripts can call.

### 6.2 Phase drives both persona and lore

Because `PERSONA` blocks and `ENTRY` blocks are both `phase`-gated:
- Write `## PERSONA: phase1`, `## PERSONA: phase2`, `## PERSONA: phase3` in the one file. `BuildSystemPrompt` picks the current one. The whole personality is editable as plain text.
- Lore can change too: an entry can reveal new information only from `phase3` onward (e.g. what the ORG files really are), or a `phase1` "the mission is righteous" framing can be replaced by a `phase3` one.

This gives the user exactly what they asked for: one file controls knowledge *and* personality, and personality is wired to story choices — without any code change once the phase-set hooks exist.

### 6.3 Standing and phase together

Keep both. Phase = where the *story* is. Standing = how the AI feels about *this player's conduct*. Both get injected into the prompt; a phase-3 persona that is also Hostile-standing is a very different companion than phase-3 + Devoted. That interaction is good — leave it emergent, don't over-engineer it.

---

## 7. Earth vs Humble Abode — the disambiguation fix

Concretely, the starter file must contain **two distinct, clearly separated entries**, plus one `core` rule:

- `ENTRY: Humble Abode` (`grounding`) — the in-game home planet. Green grass, concerts, alien community.
- `ENTRY: Earth` (`grounding`) — humanity's distant, lost origin world. Where the crew (and humans) ultimately came from. **Not** a planet in the current system; a memory/birthplace, not a destination.
- `ENTRY: AI Origin` (`grounding` or `verbatim`) — deliberately evasive. When asked where *the AI itself* comes from, it deflects. This keeps the "mysterious entity" persona intact (the overview's persona says the AI never explains its origin) while still giving Earth-the-human-origin a real answer.
- A short `core` rule line: *"Earth and Humble Abode are entirely separate. Earth is humanity's origin world. Humble Abode is a planet in the current system. Never describe one as the other."*

Because retrieval is keyword-driven, asking about "Earth" pulls the Earth entry and asking about "Humble Abode" pulls the Humble Abode entry — they no longer collide.

---

## 8. Building the starter knowledge file

The user wants Claude Code to **mine the project** and generate the initial `game_knowledge.md`, which the user then refines (especially the prose — authored descriptions should *read* well: "green grass and concerts at night," not a dry data dump).

Claude Code should:

1. Locate the game's content definitions (Section 9) — planets, factions, items, quests, NPCs.
2. Generate one `ENTRY` per significant noun: each planet, each major faction, the mission, key items (the ORG files themselves), notable locations.
3. Default planets and lore to `mode: grounding`; the mission and identity to `core`/`verbatim` as described above.
4. Pre-fill `keywords` from each thing's in-game name and obvious synonyms.
5. Leave bodies as **honest stubs where the project doesn't supply prose** — e.g. `[TODO: describe — pulled from PlanetData "Humble Abode", tags: garden, populated]` — so the user knows exactly what to flesh out. Do **not** invent lore the project doesn't contain.
6. Hand-write the few entries the user already specified (Humble Abode description, Earth, the mission, identity) using their exact wording from this conversation.
7. Add the three `PERSONA` blocks, seeding `phase1` from the existing system-prompt persona text in the current build.

---

## 9. Research tasks — DO THIS BEFORE WRITING CODE

Claude Code must inspect the actual project and answer these. The plan above assumes answers that only the repo can confirm.

1. **Planet data** — How are the 8 solar-system bodies defined? ScriptableObjects, a database, hard-coded in `SolarSystemMapController`? Where do their names/descriptions live? This is the primary source for planet entries.
2. **Death counter** — Does the game already count player deaths/respawns? Where is death handled? If no counter exists, where is the cleanest place to add and persist one? (Required for `{ASTRONAUT_NUMBER}`.)
3. **Current planet** — Is there a runtime value for which planet the player is on? (For `{CURRENT_PLANET}`.)
4. **Quest / story system** — How are quests and story beats represented? Is there an event hook that fires when the player turns over the ORG files? This is where `SetStoryPhase` calls attach.
5. **The ORG files** — How are they represented in-game (quest item, inventory object, flag)? Needed for both the mission entry and the phase-2 trigger.
6. **Save system** — Confirm how to add `int deaths` and `StoryPhase` to `SaveData` / `SaveCollector`, following the existing `aiState` pattern. Confirm backwards-compatibility (old saves default cleanly — same as the overview's `AIStateSave` handling).
7. **Existing persona text** — Pull the current system-prompt persona string out of `LLMService` so it can be lifted verbatim into `## PERSONA: phase1`.
8. **Factions / NPCs / items** — Any other content systems worth turning into knowledge entries (the overview mentions fishing, building, vendors).

Report findings before implementing. If something needed doesn't exist (e.g. no death counter), surface it and propose the minimal addition.

---

## 10. Suggested build order

Do this in phases and verify each before moving on.

- **Phase A — Knowledge Base core.** `KnowledgeEntry` model, parser, `GameKnowledgeBase` singleton (+ MainMenu seeding), editor hot-reload. Ship with a tiny hand-written test file. Verify it loads and survives a malformed entry.
- **Phase B — Prompt integration.** Rewrite `BuildSystemPrompt` to assemble persona + core + retrieved + memory. Verify the AI now answers planet questions from the file.
- **Phase C — Tokens & verbatim intercept.** `TokenResolver`, the intercept in the chat send path, the death counter. Verify *"Who am I?"* → exact `Astronaut Number N`, and that it increments after a death.
- **Phase D — Story phases.** `StoryPhase` in save data, phase-gated persona/entries, `SetStoryPhase` hooks on the ORG-file story beats. Verify persona visibly shifts when the phase is forced in a test.
- **Phase E — Starter file.** Mine the project, generate the full `game_knowledge.md` with stubs, hand-author the user-specified entries. Hand back to the user for prose refinement.

---

## 11. Acceptance tests

After implementation, these must all pass:

| Ask the AI… | Expected |
|---|---|
| "What is Humble Abode?" | The in-game garden planet — grass, concerts, alien community. **Never** Earth/pyramids/NYC. |
| "What should I do on Humble Abode?" | Specific in-game suggestions drawn from the entry. |
| "Tell me about Earth." | Humanity's distant origin world. **Not** called Humble Abode. |
| "Where do you come from?" | Evasive — stays mysterious. |
| "What are all the planets?" | The actual roster from the file. |
| "What is my mission?" | Recover the stolen ORG files, hunt the rebels, explore every planet, stay alive. |
| "Who am I?" | Exactly `You are Astronaut Number 1.` — nothing more. |
| (die once) "Who am I?" | Exactly `You are Astronaut Number 2.` |
| Ask about something genuinely not in the file | Admits it doesn't have that information — does not invent. |
| Force `Phase3_Resistant`, then chat | Personality is visibly more reluctant/resistant. |
| Edit `game_knowledge.md`, add an entry, ask about it | New knowledge is used with no recompile. |

---

## 12. Decisions for the user to confirm

1. **Story phases** — the Section 6.1 three-phase arc is a guess. Tell Claude Code the real beats: how many phases, and exactly which events advance them.
2. **Model size** — even perfectly grounded, a 1.5B model sometimes breaks character or rambles. The `verbatim` mode fully protects the must-be-exact answers, so this is only about *flavour* quality. If persona drift stays annoying, moving to Qwen 2.5 3B is the lever — bigger build/RAM, noticeably steadier roleplay. Optional, decide later.
3. **Token list** — Section 3.4 is a starting set. Any other live values the authored lore should reference? (current quest, ship name, time of day…)
4. **Scope** — this plan deliberately leaves the existing `AIMemoryStore`, input-gating, and cold-load behaviour untouched. Confirm that's intended for this pass.

---

*Hand this file to Claude Code together with `PHONE_AI_OVERVIEW.md` and the project repo. Section 9 is the required first step.*
