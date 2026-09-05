# Dialogue Studio — design (2026-09-05)

A kept, reusable localhost tool for writing NPC dialogue, plus the Unity side
that plays what the tool saves. Sam edits a tree in the browser, hits Save,
and the game reads the same file.

## What Sam asked for

- A localhost site listing every NPC / vendor with dialogue.
- Per NPC: **START** (play the conversation like in-game, with buttons to
  satisfy flags such as "kid is following me") and **EDIT** (see the whole
  tree, edit every text box, delete whole branches, add new branches /
  questions / responses, save).
- Saving must change what the game says. No recompile.
- A note in CLAUDE.md so the tool gets reused.

## The one architectural decision

NPC dialogue today is C# coroutines (`FloorbinTalk`, `TevMushroomOnboarding`,
…) with lines in Inspector arrays. A browser can't edit control flow that
lives in C#. So dialogue moves to **data**: one JSON graph per NPC in
`Assets/StreamingAssets/Story/npc_<id>.json`, walked by one generic runner.

The phone/HAL conversations (`conv_*.json`) already use a JSON graph
(`DialogueData.cs`). The NPC graph is a **superset of that schema**, so the
same tool edits both, and JsonUtility keeps loading the old files unchanged.

**Safety rule:** every hooked C# script keeps its legacy coroutine. If the
graph file is missing or broken, it falls back to the old behaviour. Delete
the JSON and the game is exactly as before.

## Schema (superset of `Conversation`)

```
Conversation
  id            "npc_tev"                  (file id; also the graph id)
  kind          "npc" | "phone"            (badge + which presenter)
  displayName   "Tev"                      (speaker label; NPC card title)
  nodes[]       DialogueNode
  testPresets[] { name, flags[] (name=true/false), money, items[] (id:count), probes[] }

DialogueNode
  id, speaker, lines[], responses[]        (existing)
  pickRandomLine   bool                     one line at random instead of all
  routes[]         { conditions[], nextNodeId }   evaluated BEFORE lines; first match jumps
  onEnter[]        Effect[]                 fired when lines start
  nextNodeId       ""                       auto-continue after lines when there are no responses

PlayerResponse
  buttonText, nextNodeId, effects[], startHintTrack, requiresFlag, hiddenIfFlag  (existing)
  conditions[]     Condition[]              all must pass for the button to show

Condition { kind, arg, num, negate }
  Flag arg | MoneyAtLeast num | HasItem arg [num≥1] | CounterAtLeast arg num
  | ObjectiveDone arg | Probe arg          (Probe = per-NPC runtime check, e.g. kidFollowing)

Effect { kind, strArg, numArg, boolArg }  (existing class)
  existing: SetFlag AdvanceStory AddTrust StartObjective CompleteObjective UnlockDialogue TriggerEnding
  new:      AddMoney SpendMoney GiveItem TakeItem AddCounter SetCounter HalSay Custom
            (Custom = per-NPC game action, e.g. kidFollow, openShop, grantStarterBlanks)
```

Start node = node with id `start`, else `nodes[0]`. Routes on the start node
replace the old "which branch am I in" C# switches (met / not met, kid
following / returned …). A node with routes and no lines is a pure switch.

## Unity side (`Assets/3 - Scripts/Story/`)

- `DialogueData.cs` — add the new fields (append-only; JsonUtility ignores
  what a file lacks). `StoryContent.LoadAll` also loads `npc_*.json`. In the
  Editor it force-reloads on every conversation start so a Save in the browser
  is live on the next talk, no restart.
- `DialogueConditions.cs` (new) — `Passes(Condition, probe)` against
  StoryDirector / PlayerWallet / Hotbar.
- `DialogueEffects.cs` — new kinds appended to the switch; `Custom` and
  `HalSay` dispatch through an optional per-NPC callback.
- `NpcGraphWalker.cs` (new) — the coroutine walker. Takes delegates so every
  existing NPC script can drive it with its own typewriter / choice panel:
  `speak(line)`, `choose(labels) → index`, `inRange()`, `probe(name)`,
  `action(name)`. Same semantics as the browser player, hop-guarded.
- Hooks (graph first, legacy fallback):
  - `AuthoredNPCTalk` — new `graphId` field (appended). Default
    `Conversation()` runs the graph if one exists → **any new authored NPC
    gets a full tree with zero code.**
  - `FloorbinTalk` / `ShllorbinTalk` — graph first; expose probes
    `kidFollowing`, actions `kidFollow` / `kidStopFollow`.
  - `TevMushroomOnboarding` — in the live (non-rent) branch, graph
    `npc_tev` first; probes `traxOwned`, `canCarryStick`; actions
    `grantStarterBlanks`, `openShop`.
  - `RandomAlienDialogue` (Alien6), `BonfireNPCDialogue`, `Alien7Vendor`,
    `ShipMarketNPC`, `FishMarketNPC`, `NPCDialogue` (Alien3),
    `ShipInstructorDialogue`, `GuitarShopNPC` — greeting portion runs the
    graph when present; the shop / trade / test logic after it is unchanged
    (roster marks these "greeting only").

## Web tool (`tools/dialogue-studio/`)

Zero-dependency: Python stdlib server + vanilla HTML/JS. Launch with
`Dialogue Studio.bat` (runs `py -3 serve.py`, opens http://localhost:8765).

- `serve.py` — static files + API: `GET /api/roster` (roster.json merged with
  the files actually in `StreamingAssets/Story`), `GET/PUT /api/file/<name>`
  (PUT writes a timestamped backup to `backups/` first, keeps 30),
  `GET /api/vocab` (known flags / items / probes / actions for dropdowns).
- `roster.json` — per NPC: id, name, where, script, hook level, notes.
- **Roster page** — cards grouped World NPCs / Phone (HAL); START and EDIT
  per card; "New NPC" creates a template graph and shows the wiring line.
- **Editor** — left: node list + auto-laid-out SVG graph (click to select,
  orphans flagged); centre: node form (id, speaker, lines, random toggle,
  routes, on-enter effects, responses with conditions/effects/next);
  right: validation (dead links, unreachable nodes, empty buttons) and graph
  settings (displayName, presets). Delete node = relink to end; Delete
  branch = node + everything reachable only through it. Undo/redo, Ctrl+S,
  unsaved guard.
- **Player** — the walker in JS, same rules. Speaker plate, one line at a
  time (click / Space), reply buttons. Right: **state panel** auto-built from
  every flag / item / probe / counter the graph mentions: checkboxes, money,
  item counts, probe toggles. Preset buttons ("Kid following you", "Already
  met Tev, no TRAX") set the whole state in one click. Effects log shows what
  fired. State persists across Restart so second-visit flows can be checked.

## Seeds

`npc_*.json` for every hooked NPC, exported from the **scene-serialized**
values (the Inspector wins over C# defaults, e.g. Tev's meet lines), with
presets authored for the quest NPCs.

## Out of scope (v1)

Voice clips, per-line audio, co-op flag sync, a Unity-side visual editor.
The vendors' shop panels stay C#; only their spoken parts move to data.

## Testing

- `py -3 prototypes/shuttle-computer/test/compile-unity.py` must pass.
- Browser: open each seed, play every preset, save, confirm the file
  round-trips (diff = formatting only).
- Sam playtests: talk to Tev / Floorbin / Shllorbin, edit a line in the
  browser, Save, talk again in the still-running Editor → new line.
