# Dialogue Studio

A local website for writing the game's NPC dialogue. Edit a talk as a tree in
the browser, hit Save, and the game reads the same file on the next talk.

## Run it

Double-click **`Dialogue Studio.bat`** (or `py -3 tools/dialogue-studio/serve.py`).
It opens http://localhost:8765. Ctrl+C in the console window stops it.
Nothing to install — Python's standard library only.

## What it does

- **Roster** — every NPC / vendor / phone conversation that has a dialogue file,
  with **▶ Start** and **✎ Edit**. `＋ New NPC` creates a fresh file.
- **Edit** — the whole tree as a graph (click a node), a form for the node's
  lines / replies / routes / effects, delete a node or a whole branch, undo,
  `Ctrl+S` saves. Checks panel lists broken links and unreachable nodes.
- **Start** — plays the talk PHOSPHOR-style with pretend game state on the
  right: flags, money, items, and "game checks" (probes) you can flip. Presets
  set a whole situation in one click ("Kid is following you"). The log shows
  every route taken and every effect fired.

## Where the files are

`Assets/StreamingAssets/Story/`

- `npc_<id>.json` — one world NPC's whole talk (Tev, Floorbin, …).
- `conv_<id>.json` — one phone / HAL conversation.

Every Save first copies the old file to `tools/dialogue-studio/backups/`
(30 kept per file, gitignored). Deleting an `npc_*.json` makes that NPC fall
back to its old hard-coded C# conversation.

## How a talk plays (same in the game and in the browser)

1. Start at the node called `start` (else the first node).
2. Try the node's **routes** in order; the first whose conditions all pass jumps
   to its node (this node's lines are skipped). Routes on `start` pick "which
   version of the talk" — met / not met, kid following, etc.
3. Fire the node's **when-it-starts effects**.
4. Speak the **lines** one by one (or one at random).
5. Show the **replies** whose conditions pass. None visible → follow **then**
   (blank = end).
6. Picking a reply fires its effects and goes to its target.

Conditions: flag set · money ≥ · carrying item · counter ≥ · objective done ·
game check (probe) · random chance. Effects: set flag · give/take money ·
give/take item · counters · HAL says · game action (custom) · story effects.

**Probes and game actions** are the escape hatch for things only the NPC's C#
can know or do (is the kid following me? make the kid follow me). Each NPC
lists its own in the roster card. Adding a new one is one `case` line in that
NPC's `GraphProbe` / `GraphAction` (or the `Probe` / `Action` lambda) plus an
entry in `vocab.json` so the browser player can pretend it.

## Wiring a new NPC in Unity

Authored NPCs (`AuthoredNPCTalk`) need no code: create `npc_<id>.json` here,
then either set the component's `graphId` to `npc_<id>` or name the NPC so
that `npc_` + lower-cased name matches (Floorbin → `npc_floorbin`).

Other NPC scripts call `StoryContent.GetNpcGraph("npc_x")` and, when it
returns a graph, yield `new NpcGraphWalker { Speak = …, InRange = … }.Run(graph)`
instead of their hard-coded lines. See `Alien7Vendor.PlayDialogueSequence` for
the smallest example and `TevMushroomOnboarding.RunGraph` for one with probes
and actions.

## Keep in lockstep

`Assets/3 - Scripts/Story/DialogueData.cs` (schema), `DialogueConditions.cs`,
`DialogueEffects.cs`, `NpcGraphWalker.cs` ↔ `vocab.json` + the player in `app.js`.
A kind added on one side and not the other either can't be previewed or
silently does nothing in-game.
