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

## 4. Suggested build order (first coding session)

1. Wiring #6 (ORG_Reveal bridge) + #12 (speaker passthrough) — unblockers.
2. conv_face_down + _after with the zero-new-UI variant — first shippable
   beat, tests the whole pipeline (flags, presenter, HAL nudge).
3. conv_interview + trigger (#5) — turns on Phase 3 end-to-end.
4. conv_we_need_to_talk + pre-compute (#7) + tokens ★ — the confrontation.
5. Act 2 missions in any order (each is independent); conv_door last.
