# Flags, Tokens & Wiring Map

The complete `StoryDirector` flag registry for this content package, the new
`TokenResolver` tokens, and — most importantly — the wiring map: exactly what
small piece of code each conversation/objective needs before its JSON can
move from `docs/story-drafts/` into `StreamingAssets/Story/`.

---

## 1. Flag registry (proposed `Mission2.cs`, mirroring `Mission1.cs`)

| Flag | Set by | Read by |
|---|---|---|
| `FaceDown_Accepted` / `FaceDown_Refused` / `FaceDown_Done` | conv_face_down / _after | Interview q3 routing, We-Need-to-Talk "Not yet" variant, ending line variant |
| `M2_MoonDelivered` | obj_moon_delivery | Act-2-count gate |
| `M2_FieryClaims` | obj_fiery_claims | Act-2-count gate |
| `M2_IceyVisited`, `M2_LedgerHeld` | conv_iceytwin | conv_rebels ledger branch, Interview ledger branch |
| `Ledger_ToORG` / `Ledger_KeptFromORG` / `Ledger_Delivered` | Interview / obj_ledger | conv_door handover_org gate |
| `M2_BeanSalvage` | obj_bean_salvage | Act-2-count gate |
| `M2_PupilReading` | obj_pupil_reading | Act-2-count gate |
| `M2_RebelMet`, `M2_RebelContact`, `M2_CoverSetDone` | conv_rebels / obj_cover_set | conv_door handover gate; Interview trigger condition |
| `M2_ShadowRescue` | obj_lights_on | House Calls unlock |
| `Interview_DeniedName`, `Interview_LiedAboutHAL`, `Interview_Done` | conv_interview | HAL post-interview lines; We-Need-to-Talk "heard" branch |
| `ORG_Reveal` | conv_interview | **mirror code** (see wiring #6) |
| `CassetteSix_Heard`, `PaleOne_Seen`, `Owners_AllFound` | objectives | Quests-page flavor; HAL lines |
| `Talk_HasKills`, `Talk_CleanHands`, `Talk_ManyDeaths` | **pre-computed by trigger code** (wiring #7) | conv_we_need_to_talk routing |
| `Talk_Agreed`, `AtTheDoor` | conv / obj | finale trigger |
| `Ending_Release` / `Ending_Stay` / `Ending_Handover` | conv_door | post-game world state; **must reset in `NewGameReset.Apply()`** |

Per CLAUDE.md: anything the player would notice resetting → also mirror into
the save (StoryDirector flags already persist; the pre-computed `Talk_*`
flags are recomputed at trigger time so they need no save entry).

## 2. New TokenResolver tokens ★

| Token | Source |
|---|---|
| `{DEATHS}` | `ResourceManager` TotalDeaths |
| `{KILLED_NAMES}` | `AlienKillsSave.killedPrePlacedNames`, joined "Marn. Ulo. Sett." — display-name map wanted so it's names, not GameObject names |
| `{HOURS_PLAYED}` | accumulate `Time.unscaledDeltaTime` in a saved counter (new small save field) |

## 3. Wiring map — one item per conversation/mission

1. **conv_face_down** — trigger: on Phase 2 entry, HAL volunteers a nudge
   line; conversation becomes available in the phone presenter (same
   surfacing as existing convs). **conv_face_down_after** — new
   `FaceDownSpot` check: a script that watches for (phone "placed" action +
   player ≥ X m away + 60 s timer). Simplest implementation: a hotbar/phone
   action "Place phone" enabled while `FaceDown_Accepted`, spawning a
   pickup-like phone prop; on retrieval, run conv_face_down_after.
   *Alternative zero-new-UI version: any 60 s period with the phone closed
   inside a marked cave/far-side trigger volume counts.*
2. **conv_iceytwin** — outpost interior scene (dimension-builder kit);
   `Discoverable`-style trigger at the inner door starts the conversation.
   Presenter must display speakers `Board`/`Three` (name passthrough check).
3. **conv_rebels** — dancer-note `Discoverable` in the active
   `AudienceZone` sets a breadcrumb flag; trigger volume at the north stage
   during its *active* window (query `ConcertStageHub`) starts the conv.
4. **obj_cover_set** — stage trigger + "hold position with guitar equipped
   for 90 s while stage active" watcher; fire `OnCoverSetPlayed`.
5. **conv_interview** — trigger: `(M2_RebelContact || firstDimensionReturn)
   && count(M2_* flags) >= 3` → ORG NPCs collect the player on next village
   visit (reuse the Interrogation cinematic scaffolding).
6. **ORG_Reveal mirror** — small watcher: when StoryDirector flag
   `ORG_Reveal` flips true → `EarlyGameProgress.ORG_Reveal = true` +
   `GameKnowledgeBase.SetStoryPhase(Resistant)`. (Dialogue effects can only
   touch StoryDirector; this is the one bridge needed.)
7. **conv_we_need_to_talk** — trigger: Phase 3 + next phone open. Trigger
   code FIRST pre-computes: `Talk_HasKills` (killedPrePlacedNames non-empty),
   `Talk_CleanHands` (empty), `Talk_ManyDeaths` (TotalDeaths ≥ 5) — then
   starts the conversation.
8. **conv_door** — trigger volume at the Watchful Eye site (requires
   `Talk_Agreed`). `TriggerEnding` currently logs; implement a switch:
   Release → HAL silent + spawner gate + Eye emission fade; Stay → phase-3
   voice + TorchAura calm-variant; Handover → faction audio-mix swap.
   All three: set flag + autosave.
9. **Objectives/hint events** — each `completionEvent`/`advanceEvent` name
   in the drafts (`OnMoonCrateDelivered`, `OnClaimBeaconsCollected`, …) is a
   new gameplay event to raise from its mission script; they follow the
   existing `OnVillageReached` pattern (`VillageReachTrigger.cs`).
10. **Delivery crate & claim beacons** — `GravityObjectSimple` +
    `PlayerPickup` + `EndlessManager.RegisterPhysicsObject` per CLAUDE.md;
    destination trigger raises the completion event.
11. **Six's cassette** — Alien3 trade-back dialogue branch (fish check OR
    guitar-played flag) grants a `CassettePickup` variant; `CassettePlayer`
    completion raises `OnCassetteSixPlayed`.
12. **Presenter speaker check** — verify `PhoneDialoguePresenter` /
    `DialoguePresenter` renders arbitrary `speaker` strings as the name
    label. If hardcoded to AI/Tev, add a passthrough (one small change,
    listed here so it isn't discovered at integration time).

## 4. Build order — STATUS 2026-07-06 (first slice IMPLEMENTED, compile-verified)

**DONE (in `Assets/3 - Scripts/Story/`, seeded in both MainMenuController
paths, zero compile errors):**
- `Mission2.cs` — the full flag registry from §1 as typed constants, plus
  `Act2MissionCount()`, `NotifyDimensionReturn()`, `PrecomputeTalkFlags()`
  (wiring #7), and `KilledNamesJoined()` (the `{KILLED_NAMES}` data source).
- `Mission2Director.cs` — auto-singleton wiring hub: Phase 1→2 gate
  (dimension return OR 3 Act-2 missions), the Face Down offer queue, the
  **ORG_Reveal bridge** (wiring #6 — mirrors the StoryDirector flag onto
  `EarlyGameProgress.ORG_Reveal`; `AIStoryController` merges the knowledge
  file on its own poll; phase → Resistant), and the `conv_we_need_to_talk`
  queue with Talk_* precompute. **Inert until the conv JSONs ship** — every
  queue is guarded on `StoryContent.GetConversation(...) != null`.
- `FaceDownSpot.cs` — scene-placeable trigger (zero-new-UI variant): stand
  inside with the phone closed for 60 s → queues `conv_face_down_after`
  silently. Needs placement in 1–3 locations (cave, moon far side).

**Wiring #12 ANSWERED:** `PhoneDialoguePresenter.ShowLines` **ignores the
speaker string** — every line renders as an AI chat bubble (shipped "Tev"
lines already do). Nothing breaks with new speakers, but NPC-delivered
conversations (tev_letter, trade_back, interview, iceytwin, rebels, offers)
render wrong on the phone. The right fix is a `WorldDialoguePresenter`
implementing the same `DialoguePresenter` interface over a world-space/
overlay panel with a speaker name label — the engine (`DialogueRunner`) is
already presenter-agnostic.

**Remaining, in order:**
1. Copy `conv_face_down.json` + `conv_face_down_after.json` into
   `StreamingAssets/Story/` and place 1–3 `FaceDownSpot`s → the first beat
   is live end-to-end (Mission2Director offers it at Phase 2).
2. To test without playing to Phase 2: F9/dev-flip a flag or call
   `GameKnowledgeBase.Instance.SetStoryPhase(StoryPhase.Phase2_Uneasy)`.
3. `WorldDialoguePresenter` (see above) → unlocks all NPC-giver convs.
4. conv_interview + collect-the-player trigger (#5) — Phase 3 end-to-end
   (the bridge is already waiting for the flag).
5. `{DEATHS}`/`{KILLED_NAMES}`/`{HOURS_PLAYED}` tokens in TokenResolver
   (data sources ready on Mission2).
6. Act 2 missions in any order; conv_door last.
7. Call `Mission2.NotifyDimensionReturn()` from the dimension-return path
   (PortalManager/BlackHoleCapture exit) so the Phase 1→2 gate's first
   condition works.
