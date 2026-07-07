# MISSION INTEGRATION HANDOFF — master plan for implementation sessions

**Read this first in any session working on the story missions.** It is the
entry point; everything else in `docs/story-drafts/` is reference material
hanging off this plan. Written 2026-07-06 at the end of the design/authoring
session (branch `feat/dimension-polish`, commits `9400f439..2cb6a0f4`).

**The division of labor** (per the user): **the USER places objects in the
Unity scene** (Claude cannot reliably position things on planets — they're
parented to moving CelestialBodies and placement needs eyes). **CLAUDE does
everything else**: all code, all JSON shipping, all wiring, compile checks,
and verification scripts. Each chunk below is structured as:
*Claude does → USER places (checklist) → test → done-when.*

---

## 0. Orientation — what exists and how it fits

### The story in one paragraph
The game's motif is **observation** (HAL's eye, Cyclops's "Pupil", the
brightening Watchful Eye, the ObserverState dimensions). HAL is a fragment
of the Watchful Eye; the phone has had seven previous owners, all dead;
every Act 2 planet contract uncovers a trace of one of them. The ORG
interview flips `ORG_Reveal` → HAL Phase 3 → the data-driven "We Need to
Talk" confrontation → the finale at the Eye with three endings. Full design:
`docs/MISSIONS_DESIGN.md`. Visual map: `docs/story-flow.html` (open in a
browser). Every line of dialogue, every readable, every HAL line is already
written in this folder.

### What is ALREADY CODED (compile- and type-verified, committed)
| File | What it does |
|---|---|
| `Assets/3 - Scripts/Story/Mission2.cs` | Flag registry (constants match the conv JSONs exactly) + `Act2MissionCount()` + `PrecomputeTalkFlags()` + `KilledNamesJoined()` + `NotifyDimensionReturn()` |
| `Assets/3 - Scripts/Story/Mission2Director.cs` | Auto-singleton wiring hub: Phase 1→2 gate, Face Down offer queue, **ORG_Reveal bridge** (StoryDirector flag → `EarlyGameProgress.ORG_Reveal` + `SetStoryPhase(Phase3_Resistant)`), We-Need-to-Talk queue. **Inert until conv JSONs exist in StreamingAssets/Story/** — every queue checks `StoryContent.GetConversation(...) != null`. Seeded in BOTH MainMenuController paths (trap #1). |
| `Assets/3 - Scripts/Story/FaceDownSpot.cs` | Scene-placeable trigger: player inside + phone closed 60 s → silently queues `conv_face_down_after` |
| `Assets/3 - Scripts/Story/WorldDialogueUI.cs` | World-surface presenter: `WorldDialogueUI.Begin("conv_id")` from any script → bottom panel, speaker label, typewriter, reply buttons. **Never eyeballed in play mode — needs a visual pass in Chunk 0.** |
| `DialogueRunner.cs` (modified) | Now token-resolves authored lines (`{PLAYER_DEATHS}`, `{KILLED_NAMES}`, ...) |
| `TokenResolver.cs` (modified) | `{KILLED_NAMES}` case added, verified resolving live |

### Key architecture facts a fresh session must know
1. **Conversations** live as `conv_*.json` in `Assets/StreamingAssets/Story/`
   (`StoryContent.LoadAll()` eats the whole dir). The DRAFTS are in
   `docs/story-drafts/` — **shipping a conversation = copying its file over**.
   Never copy one whose trigger isn't wired (it's inert but shadowable).
2. **Two presenters**: the phone (`PhoneDialoguePresenter`, via
   `StoryDirector.QueueConversation("id")` — auto-opens on next AI-chat
   entry, survives saves) renders everything as AI bubbles and IGNORES the
   speaker string — use it only for HAL beats. NPC/world scenes use
   `WorldDialogueUI.Begin("id")` which shows the speaker name.
3. **Flags** are StoryDirector's dictionary — saved automatically, cleared
   by New Game automatically. Dialogue sets them via `SetFlag` effects;
   code reads/writes via `Mission2.Get/Set(Mission2.Flag...)`.
4. **Objectives/hints**: merge entries from `objectives_draft.json` /
   `hinttracks_draft.json` into the real `objectives.json` / `hinttracks.json`
   (they're keyed lists — merge, don't replace). Completion events like
   `OnMoonCrateDelivered` are new — each mission's wiring code must raise
   them via the `StoryDirector.CompleteByEvent` path (see wiring notes,
   `flags-and-tokens.md` §3; the pattern is `VillageReachTrigger`).
5. **CLAUDE.md traps apply**: new auto-singletons must ALSO be seeded in
   `MainMenuController.EnsureGameplaySingletons` (both blocks — see how
   `Mission2Director` was added, commit `cfc345ae`). New `.cs` files need
   hand-written `.meta` files with unique GUIDs, and `git add` both.
   Editor is the only build/test loop; use coplay MCP `check_compile_errors`
   after every code change (works when the editor is open).
6. **Never touch** the forbidden zone (atmosphere/celestial generation) —
   nothing in this plan needs it.

### The reusable placement unit (build FIRST, in Chunk 1)
Almost every mission needs "walk up to a thing, press F, a conversation
plays." Claude writes ONE component for this, `ConversationPrompt.cs`:
trigger sphere + gaze/F prompt (mirror `Discoverable.cs` + the
`BonfireNPCDialogue` prompt pattern) + optional `requiresFlag`/`hiddenIfFlag`
gates + `WorldDialogueUI.Begin(conversationId)` + one-shot or repeatable.
After that, **the user's placement job is almost always: create empty
GameObject → add `ConversationPrompt` → set the conversation id + flags in
the Inspector → position it.** That one component covers the giver hooks,
the rebel meeting, the interview fallback, Marlo's rescue talk, and Three's
outpost conversation.

### How the user should test any phase-gated thing
In the Editor, play mode, then via coplay `execute_script` (Claude runs it)
or a temporary cheat: `GameKnowledgeBase.Instance.SetStoryPhase(StoryPhase.Phase2_Uneasy)`
(forward-only!) and `StoryDirector.Instance.SetFlag("...", true)`. Claude
should write a tiny `Mission2DebugWindow` editor tool in Chunk 0 so the user
can flip flags/phases from a menu instead of asking Claude every time.

---

## 1. The chunks

Sized so each is one comfortable session (or half a session). Order matters
for 0→3; after that, chunks 4–8 are independent and can go in any order the
user feels like. **Each chunk ends with: compile check, play test, commit.**

---

### CHUNK 0 — Sanity + tooling (no placement; ~small)
**Goal:** the pipeline is visibly working and the user can drive it.
- **Claude:** verify branch state; write `Mission2DebugWindow.cs` (an
  `EditorWindow` under a "Story" menu: buttons to set phase, toggle each
  Mission2 flag, queue any conversation, and start any conversation via
  WorldDialogueUI); ship `conv_menu`-based test — enter play mode via coplay
  `play_game`, call `WorldDialogueUI.Begin("conv_menu")`, capture screenshot.
- **USER:** look at the WorldDialogueUI panel; give layout feedback
  (Claude tunes colors/sizes/anchors from it).
- **Done when:** debug window opens, a conversation renders on the world
  presenter, and the user has approved (or Claude has iterated) the look.

### CHUNK 1 — "Face Down" live end-to-end (~small; the emotional proof-of-concept)
**Goal:** the game's best beat is playable.
- **Claude:** copy `conv_face_down.json` + `conv_face_down_after.json` into
  `StreamingAssets/Story/`; write `ConversationPrompt.cs` (the reusable
  unit, see §0); verify Face Down offer queues at Phase 2 via debug window.
- **USER places:**
  - [ ] 1–3 empty GameObjects named `FaceDownSpot`, each: add
    `FaceDownSpot` component (SphereCollider auto-added as trigger, radius
    8 — enlarge if the spot is a big cave). Locations: a cave interior on
    Humble Abode; optionally the far side of Constant Companion. **Parent
    each under its planet's CelestialBody object** so it moves with the
    planet. Save scene.
- **Test:** debug-set Phase 2 → open phone → HAL asks → go to spot → phone
  closed 60 s → open phone → "Thank you."
- **Done when:** the full beat plays, `FaceDown_Done` shows true in the
  debug window, and it survives a save/load.

### CHUNK 2 — Moon delivery "Cold Delivery" (~medium; establishes the delivery pattern)
**Goal:** first Act 2 contract; the crate + notes patterns exist for reuse.
- **Claude:** ship `conv_moon_offer.json`; write `DeliveryCrate.cs`
  (pickup-carryable: mirror `SpaceNetPickup`/`PlayerPickup` flow,
  `GravityObjectSimple`, `EndlessManager.RegisterPhysicsObject`) and
  `DeliveryTarget.cs` (trigger: crate inside → raise `OnMoonCrateDelivered`
  through the StoryDirector event path). Check how `NoteCollection` note
  pickups work (read the code first!) and prep 3 note assets with the
  Keeper's text from `notes-and-logs.md`. Merge `obj_moon_delivery` +
  `hint_moon_delivery` into the live objectives/hints JSON. Wire the giver:
  `ConversationPrompt` config for Alien7 (user places).
- **USER places:**
  - [ ] `ConversationPrompt` GO beside Alien7's stall → conversation id
    `conv_moon_offer`, hiddenIfFlag `M2_MoonOfferTaken`.
  - [ ] The crate prop (Claude preps the prefab; user drops it beside the
    stall and links it to the prompt if Claude's wiring needs it).
  - [ ] `DeliveryTarget` trigger just inside the MoonBase door.
  - [ ] 3 note pickups inside MoonBase: galley table, a bunk, the airlock.
  - [ ] Optional now / later: the Keeper's suit prop on the moon's far side
    (a `Discoverable` with the Owner-4 HAL line from `hal-lines.md`).
- **Test:** take offer → fly crate up → deliver → objective completes,
  `M2_MoonDelivered` true → read all 3 notes.
- **Done when:** full loop + save/load mid-mission works (crate carried
  across a save is the risky bit — verify).

### CHUNK 3 — Tev's letter + the Icey outpost (~large; the showpiece)
**Goal:** A2-3 "Quiet Neighbors" — the written-boards scene.
- **Claude:** ship `conv_tev_letter.json` + `conv_iceytwin.json`; build the
  under-ice outpost interior AS CODE using the dimension kit
  (`DimensionSceneUtil` builders — same pattern as the `D*Controller`s;
  read 2–3 of them first). Decide with user: separate scene entered via
  `PortalManager.EnterInterior` (like dimensions) vs. a built-in-place
  interior under Icey Twin's body. RECOMMENDATION: separate scene — the kit
  and respawn/portal flow already exist. Wire: `OnIceyOutpostEntered`
  event, boards as props with the standalone texts, the conversation
  trigger inside. Tev giver hookup: `ConversationPrompt` near Tev gated on
  Phase ≥2 (`conv_tev_letter`).
- **USER places:**
  - [ ] Tev's `ConversationPrompt` (id `conv_tev_letter`, hiddenIfFlag
    `Tev_LetterGiven`).
  - [ ] The outpost ENTRANCE on Icey Twin: a vent-mouth prop + portal
    trigger (Claude preps; user positions on the surface, parented to the
    Icey Twin body).
  - [ ] Eyeball pass on the generated interior; feedback → Claude tunes.
- **Test:** letter → fly → enter → boards scene with Three → ledger flag →
  HAL interjection → exit.
- **Done when:** `M2_LedgerHeld` true and the scene FEELS right (this one
  is worth iterating on).

### CHUNK 4 — Fiery Twin claims (~medium)
- **Claude:** ship `conv_claims_offer.json`; write `HazardZone.cs` (trigger
  volume draining ship power/vitals at a configurable rate — reusable);
  claim-beacon pickup (reuse pickup pattern); camp log notes (3, from
  notes-and-logs.md); `OnClaimBeaconsCollected` when all three held; merge
  objective+hints.
- **USER places:** ShipMarket `ConversationPrompt`; the camp (props: tents/
  crates from existing packs, 3 beacons, 3 notes) on the terminator line;
  a `HazardZone` covering the dayside approach. All parented to Fiery Twin.
- **Test/done:** full loop; power drain feels dangerous but fair.

### CHUNK 5 — Bean Run + Five's cassette (~small-medium)
- **Claude:** ship `conv_bean_offer.json`; wreck salvage = existing loose
  ship-part pickups spawned as props (no new system); a `CassettePickup`
  variant for Five's tape + audio file (generate via coplay TTS or user
  records; transcript in notes-and-logs.md); `OnBeanSalvageSold` hook on
  the ShipMarket sell flow.
- **USER places:** 2–3 wreck prop clusters on Tumbling Bean; the cassette
  in one cockpit; ShipMarket prompt.

### CHUNK 6 — Rebels + Cover Set (~medium-large)
- **Claude:** ship `conv_rebels.json`; dancer-note `Discoverable` in the
  audience zone; north-stage `ConversationPrompt` active-window gate (query
  `ConcertStageHub`); `CoverSetController` (90 s stage-hold watcher +
  the staging beats from `staging-scripts.md` §2 — blinders, ENCORE banner,
  `OnCoverSetPlayed`). The crate-silhouette beat can be a v2 polish.
- **USER places:** the dancer-note trigger in the crowd; Pell (an alien NPC
  prop/character near the north stage) + prompt; the stage trigger volume.

### CHUNK 7 — Lights On (~small)
- **Claude:** ship both `conv_lights_on_*.json`; stranded-ship setup
  (a ship prop with `SolarPanelCharger` state; check whether LebronLight
  counts as a charge source — one-line gate to widen if not);
  `OnShadowRescueDone`.
- **USER places:** Marlo's ship inside Cyclops's shadow cone (Claude
  computes a helper position; user fine-tunes), her `ConversationPrompt`.

### CHUNK 8 — The Interview + Phase 3 (~medium; the arc's hinge)
- **Claude:** ship `conv_interview.json`; the collect trigger
  (`(M2_RebelContact || M2_DimensionReturned) && Act2MissionCount() >= 3` →
  on next village visit, route the player to the ORG scene — reuse the
  Interrogation cinematic scaffolding, READ IT FIRST); staging per
  `staging-scripts.md` §3 (phone on table, the one silent eye-blink via
  `HALVisuals` if the player lies, ORG_Reveal on phone PICKUP not dialogue
  end). The bridge in Mission2Director then does the rest automatically.
- **USER places:** the interview room set-dress if the existing
  interrogation space needs it; otherwise nothing.
- **Done when:** exiting the interview under open sky yields HAL Phase 3
  and the "We need to talk" queue arms (visible in debug window).

### CHUNK 9 — "We Need to Talk" (~small; pure payoff)
- **Claude:** ship `conv_we_need_to_talk.json`. Mission2Director already
  precomputes + queues it. Verify each branch with debug flags (kills/no
  kills/many deaths/FaceDown done/lied-at-interview).
- **USER:** play it once each way; feel check.

### CHUNK 10 — Trade Back + predecessor sweep (~medium)
- **Claude:** ship `conv_trade_back.json`; precompute `TradeBack_HasFish`
  (FishInventory check) / `TradeBack_HasGuitar` before Begin; Six's
  cassette item + audio; `OnCassetteSixPlayed`; the `obj_owners` quest-page
  rows; `Discoverable`s for One's grave + Keeper's suit + Seven's file
  (given by Pell post-Cover-Set).
- **USER places:** Alien3's cassette-drawer prop (visible day one!), One's
  grave by the fishing bank, the remaining trace props.

### CHUNK 11 — Into the Pupil (~medium)
- **Claude:** instrument item + plant-trigger + the transponder readout
  beat (`OnPupilInstrumentPlanted`); giver = follow-up letter from Three
  (a note item) or the researchers directly.
- **USER places:** the floating platform prop at altitude, parented to
  Cyclops (non-landable planet stays non-landable — the platform is the
  exception), instrument mount point.

### CHUNK 12 — Watchful Eye finale (~large; build LAST)
- **Claude:** ship `conv_door.json`; the site controller (approach gating,
  phone-leaves-hotbar beat, Eye pupil `ObserverState` brightening for
  A3-2, `TriggerEnding` switch implementation: Release/Stay/Handover
  world-state changes per `staging-scripts.md` §4 + ending flags +
  autosave + `NewGameReset` audit).
- **USER places:** the surface site (Claude generates structure via the
  dimension kit where possible; user positions the site + Six's kneeling
  suit + the door), then iterate together.

### CHUNK 13 — Polish sweep (parallel/ongoing)
- HAL line pack → `HALCommentator` (dimension exits, predecessor lines,
  phase-shaded variants) — pure code+strings.
- Ambient lines → `RandomAlienDialogue` sets + vendor barks (flag-gated).
- Voice clips: generate via coplay TTS (George voice id — see the
  `coplay-mcp-asset-gen-gotchas` memory) + `HALVoiceManifest` entries.
- `Mission2.NotifyDimensionReturn()` call from the dimension-return path.
- OPTIONAL (own chunk): Steady Gaze per `steady-gaze-spec.md`.

---

## 2. USER placement cheat-sheet (read once, applies everywhere)

1. **Parent everything to its planet.** Scene objects live under their
   `CelestialBody` GameObject (e.g. under `Humble Abode`) or they get left
   behind by orbital motion. The hierarchy groups things under
   `--- Section ---` organizers — put mission props under a new
   `--- Missions ---` organizer per planet if you like tidiness.
2. **Triggers**: empty GameObject → add the component Claude names → the
   collider is auto-added/configured (`Reset()` does it) → scale radius to
   taste → position → save scene.
3. **The player tag check is `CompareTag("Player")`** — triggers only react
   to the player.
4. **Physics props Claude preps** already handle `GravityObjectSimple` +
   `EndlessManager` registration — you just position them.
5. **Buildings only**: the `<prefab>_Placed` naming + CelestialBody parent
   convention is how saves find them. Mission props are NOT buildings —
   they're scene objects, saved state lives in flags instead.
6. After placing: tell Claude what you named things and roughly where —
   Claude wires references via editor scripts (`execute_script`) rather
   than asking you to drag references, whenever possible.
7. **Save the scene** (Ctrl+S) before Claude runs play-mode tests.

## 3. Session-start prompt (copy-paste for a fresh session)

> Read `docs/story-drafts/INTEGRATION_HANDOFF.md` and the status table at
> the bottom of it, then start CHUNK <N>. You do all code/JSON/wiring and
> compile-verify with coplay; give me a placement checklist when it's my
> turn, then continue after I confirm. Commit at the end of the chunk and
> update the status table.

## 4. Gotchas register (things that already bit or nearly bit)

- `StoryContent.LoadAll` reads ALL of `StreamingAssets/Story/` — draft
  JSONs stay in docs/ until their trigger exists.
- `QueueConversation` holds ONE pending conversation — Mission2Director
  guards on `HasPendingConversation`; any new queuing code must too.
- The phone presenter ignores `speaker` — HAL beats only. World scenes:
  `WorldDialogueUI`.
- `SetStoryPhase` is FORWARD-ONLY — testing a later phase means New Game
  or reload to go back. The debug window should warn about this.
- Tev's existing `TevDialogue` and vendors' own dialogue systems are
  UNTOUCHED — mission conversations run beside them via `ConversationPrompt`
  placed nearby, not inside their scripts (first integration can stay
  non-invasive; folding into vendor UIs is a later nicety).
- New `.cs` = hand-write `.meta` (unique GUID) + `git add` both.
- WorldDialogueUI pauses nothing — the world keeps running during
  conversations (enemies!). If that's a problem in practice, the fix is a
  movement/input gate, not Time.timeScale (HAL trip effects run on
  unscaled time for a reason).
- JSON edits: JsonUtility — no comments, no trailing commas. Validate with
  PowerShell `ConvertFrom-Json` before shipping.

## 5. STATUS TABLE (update at the end of every session)

| Chunk | Status | Notes |
|---|---|---|
| 0 — Sanity + debug window | NOT STARTED | WorldDialogueUI never seen in play mode |
| 1 — Face Down | NOT STARTED | JSONs ready in drafts; FaceDownSpot.cs shipped |
| 2 — Moon delivery | NOT STARTED | |
| 3 — Tev letter + Icey outpost | NOT STARTED | |
| 4 — Fiery claims | NOT STARTED | |
| 5 — Bean run | NOT STARTED | |
| 6 — Rebels + Cover Set | NOT STARTED | |
| 7 — Lights On | NOT STARTED | |
| 8 — Interview + Phase 3 | NOT STARTED | Bridge code already live |
| 9 — We Need to Talk | NOT STARTED | Queue code already live |
| 10 — Trade Back + predecessors | NOT STARTED | |
| 11 — Into the Pupil | NOT STARTED | |
| 12 — Watchful Eye finale | NOT STARTED | Build last |
| 13 — Polish sweep | NOT STARTED | Can interleave anytime |

**Already done before Chunk 0** (this session): all design docs, all
dialogue/readables/HAL lines authored, story-flow diagram, Mission2 flag
registry, Mission2Director (phase gates + ORG bridge + queues), FaceDownSpot,
WorldDialogueUI, DialogueRunner token resolution, `{KILLED_NAMES}` token,
MainMenuController seeding. All compile-verified; nothing play-tested.
