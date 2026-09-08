<!-- doc-status: stamped 2026-09-08 -->
# docs/ — what is current and what is history

This folder is a **trail**, not a spec. Most of it is build briefs for features that
have since been built, changed, or switched off. Every file that is no longer a
description of the game now carries a one-line stamp at the top:

| Stamp | Means |
|---|---|
| 🟢 **ACTIVE** | This is being built right now. Trust it. |
| ✅ **BUILT** | It happened. Read it for the *why*; read the code for the *what*. |
| ⛔ **SUPERSEDED** | The direction changed. Do not design from it. |
| 🗄 **HISTORICAL RECORD** | A snapshot of one day. Never a description of today. |
| ⚠️ **STALE** | Still roughly right in shape, badly out of date in detail. |
| 📖 **CANON FOR TONE** | Story bible. Voices and beats hold; systems and roadmap do not. |

**A file with no stamp is a live reference doc** — the audit, the distance table,
the playtest checklists, the setup guides, the gallery/tuning notes.

## Start here

| I want to know… | Read |
|---|---|
| what exists in the game right now | `CURRENT_STATE_AUDIT.md` |
| what has been deliberately switched off, and why | `VAULTED_SYSTEMS.md` |
| the current core loop (fish → fly → sell → fuel) | `Handoff_PlanetEconomy_Fuel_Fishing_v2.md` |
| how far apart the planets get, and when | `DISTANCE_TABLE.md` |
| how the fishing minigame actually works | `Handoff_FishingRevamp_Phase1_v1.md` |
| tone, characters, the cold open | `GDD_StoryBible_v2.md` (tone only) |
| how to change what an NPC says | `../tools/dialogue-studio/README.md` |

## Where the game is heading (2026-09-08, Sam)

The core loop is **fishing**, and then **flying somewhere else to sell the catch**
because that planet's economy pays differently. That one loop is meant to supply
the reason to fish, the reason to explore, and the reason to want fuel — and to make
story and dialogue easier to write, because the player already has a reason to be
everywhere. Everything below is deliberately parked until that loop is fun:

- **TRAX** — a mid-game money source, not the core loop. Needs a lot more work.
- **The black hole eating the system at 2:30:00**, and the four planet endings
  (Humble Abode's first) — still the plan, still parked.

`docs/superpowers/` holds per-feature specs and plans from earlier sessions;
`docs/superpowers/_archive/` is retired. `documentation/` is a one-off static-analysis
backlog from 2026-07-15 — unverified leads, not source of truth.
