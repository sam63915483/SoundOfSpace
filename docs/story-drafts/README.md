# Story Drafts — drop-in content package

> **Implementing? Start at [`INTEGRATION_HANDOFF.md`](INTEGRATION_HANDOFF.md)**
> — the master chunked plan (who places what, who wires what, status table).

Companion to `docs/MISSIONS_DESIGN.md` (Parts 1–2). This folder contains
**production-format content**: conversation JSONs in the exact
`DialogueData.cs` schema, objective/hint-track entries, all readable
notes/logs/cassette transcripts, and the full HAL line pack.

**These are drafts.** They live in `docs/` deliberately — `StoryContent.LoadAll()`
reads everything in `StreamingAssets/Story/`, so don't copy a `conv_*.json`
over until its trigger is wired (an unwired conversation is inert but an
id collision would shadow a live one).

## Contents

| File | What |
|---|---|
| `conv_face_down.json` / `conv_face_down_after.json` | N-1 "Face Down" — HAL's private request + the pickup scene |
| `conv_iceytwin.json` | A2-3 "Quiet Neighbors" — the written-boards scene, Three, the ledger |
| `conv_rebels.json` | A2-6 "After the Encore" — Pell, sound-as-cover, Cover Set setup |
| `conv_interview.json` | A2-7 "The Interview" — full ORG scene, `ORG_Reveal` payoff |
| `conv_we_need_to_talk.json` | A3-1 — HAL's data-driven confrontation, all branches |
| `conv_door.json` | A3-3 finale — the Eye, three endings |
| `objectives_draft.json` | Objective entries for every Part 1 + Part 2 mission |
| `hinttracks_draft.json` | Hint tracks for the missions that want them |
| `conv_moon_offer.json` / `conv_claims_offer.json` / `conv_bean_offer.json` | Act 2 giver hooks — Alien7 and ShipMarket contracts |
| `conv_lights_on_offer.json` / `conv_lights_on_rescue.json` | N-2 "Lights On" — Alien7's panic + Marlo in the shadow cone |
| `conv_tev_letter.json` | Starts A2-3 (the letter + rules), plus Tev's post-Interview "how many" scene and One's note |
| `conv_trade_back.json` | Alien3 — Six's cassette back (fish or a song) |
| `staging-scripts.md` | Beat-by-beat direction for Face Down, Cover Set, the Interview, the Door — using shipped camera/concert systems |
| `steady-gaze-spec.md` | The phone-camera Observed mechanic — full spec + implementation sketch |
| `ambient-lines.md` | RandomAlienDialogue sets + vendor barks, flag-gated per act and per ending |
| `notes-and-logs.md` | Full text of every readable: predecessor notes, cassettes, boards, ORG file, grave markers |
| `hal-lines.md` | Complete HAL line pack — phases, dimensions, mission beats, reactive lines |
| `flags-and-tokens.md` | Flag registry, new TokenResolver tokens, and the wiring map (what code each conv needs) |

## Known engine constraints honored here

- **JsonUtility**: no comments, no trailing commas, no dicts. Extra fields are ignored.
- **Effects** use only the fixed 7-kind vocabulary (`DialogueEffects.cs`):
  `SetFlag`, `AdvanceStory`, `AddTrust`, `StartObjective`, `CompleteObjective`,
  `UnlockDialogue`, `TriggerEnding` (currently a logged no-op — the endings
  land there on purpose so wiring is one switch statement later).
- **Flag gating is response-level only** (`requiresFlag`/`hiddenIfFlag`).
  Where a *node* needs to vary by flag, two identically-labeled responses
  gate to different nodes (see `conv_we_need_to_talk.json`, "Not yet.").
- **Speakers**: existing content uses `"AI"` and `"Tev"`. These drafts add
  `"Board"`, `"Three"`, `"Pell"`, `"Interrogator"`. Verify
  `PhoneDialoguePresenter`/`DialoguePresenter` displays arbitrary speaker
  strings (name label passthrough) before wiring — if it special-cases
  AI/Tev, that's the one presenter change this package needs.
- **Dynamic data in dialogue** rides `{TOKENS}`. New tokens required
  (`{DEATHS}`, `{KILLED_NAMES}`, `{HOURS_PLAYED}`) are specified in
  `flags-and-tokens.md` with their data sources; each is a few lines in
  `TokenResolver.cs`.
- Conversations that fire from **game state** (not NPC interaction) need a
  small trigger script each — the pattern is `Discoverable.cs` /
  `VillageReachTrigger.cs`. The wiring map lists every trigger.
