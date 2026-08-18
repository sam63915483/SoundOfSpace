# Co-op TRAX Loop + World-Save-Only Design

**Date:** 2026-08-18
**Goal:** the whole revamped music loop works in co-op — shared TRAX editor with live
partner cursors, synced Tev/plugins/rent, synced messages/customers, visible walking
customers — and the save system collapses to world-saves-only (character keeps just
name + colour), savable at the stasis pod by host **or** guest.

Decisions locked with Sam 2026-08-18:
1. **Editor conflicts:** free-for-all, last write wins, **shared playback** (play/stop/
   playhead replicated so both hear the same thing).
2. **Walking customers:** full sync — guests see the aliens stroll and get ambushed.
3. **World save:** per-character personal blocks keyed by `CharacterProfile.id`
   (same shape TevFronting already uses), saver fetches the partner's live personal
   snapshot over the network at save time.
4. **Save points:** stasis pod becomes the ONLY save trigger; pause-menu SAVE GAME,
   the AutosaveManager timer, and the portal/oxygen one-shot autosaves are removed.

---

## What already syncs (verified, zero work)

`EconomySync` already replicates `BuyerLedgerSave` + `MushroomDealState.Snapshot` +
`TevFrontingSave` + `TapeMemorySave` on watched version counters, and routes guest
replies/sales to the host (`KindReply`, `KindTapeSale`, `KindTapeHeard`, `KindMarkRead`).
The Messages app is a pure view over BuyerLedger and already has
`MessagesScreen.RefreshFromNetwork()`. `GalaxyTime` broadcasts the clock.
`TapeCareer.TapesSold` counts correctly for guest sales because the only increment
point is `BuyerLedger.ReportTapeDeal`, which runs on the host for routed sales.
The join snapshot (`SaveCollector.ApplyWorldSubset`) already carries `traxLibrary`
(projects + plugins + prints), `CassetteDeck` fields, and `tapeMemory`.

So: messages, customers, deals, Tev fronting, tape memory, career counting, and the
clock are done. The work is the four phases below.

---

## Phase A — TraxSync: shelf, plugins, prints, deck, rent, story flags

New `Assets/3 - Scripts/Multiplayer/TraxSync.cs`, channel `"TraxSync"`, copying the
`EconomySync` shape exactly (auto-created singleton, registered handler on every
machine, host tick vs client tick, 8 KB JSON chunking, 0.25 s coalescing floor,
`WorldSync.WorldReady` gate, 3 s snapshot-request retry).

**Watched state (host → clients, whole snapshot on version change):**
- `TraxLibrary.Version` — already exists, pre-wired for this.
- `CassetteDeck` — **add a `Version` counter** bumped in Insert/EjectBlank/PrintTo/
  TakeEjected/Clear/Apply.
- `StoryDirector` — **add a `Version` counter** bumped on every flag/counter write.
  This carries rent (`tevRentPerWeek`, `tevRentArrears`, `tevRentNextDueDay`,
  `tevRentSettled`), TapeCareer (`tapesSoldTotal`, `tapesUnlockAnnounced`), and all
  other story flags — story state is world state and should be shared anyway.

Payload: `[Serializable] class TraxState { TraxLibrarySave trax; int deckTier;
int deckKind; string deckEjectedId; StoryDirectorSave story; }` — the save schema IS
the network schema, per the standing rule.

**Client → host routes** (static `Route*` methods returning bool, `ShouldRoute()`
= listening && !server; caller falls through to local mutation when false):
- `RouteProjectSave(name, songJson)` / `RouteProjectDelete(id)` — shelf writes.
- `RoutePluginInstall(module)` — guest pays with own wallet locally (money is
  personal, unchanged), host applies `TraxLibrary.Install` so both own it.
- `RouteDeckInsert(tier, kind)` / `RouteDeckEject()` / `RouteDeckTakeEjected(printId)`
  / `RoutePrint(projectId, kindInt)` — the deck is host-authoritative (the class
  comment already promises this). Host validates slot state; a rejected insert just
  means the next snapshot shows the real slot contents. Blank item add/remove stays
  local on the acting player's hotbar (items are personal).
- `RouteRentPay(amount)` — guest spends own money locally; host applies the balance
  reduction only. Split `MushroomQuest.PayRent` into wallet-spend (local) and
  `ApplyRentPayment(amount)` (balance math, host-side) so the host never touches a
  wallet for a routed payment.

**Join snapshot:** add `storyDirector` to `ApplyWorldSubset` (it is currently missing,
so a guest today joins with stale rent/career/story flags).

**UI nudges:** after apply, bump redraw counters for `TevShopUI` (plugin lock state),
`ShuttleComputerUI` shelf (`_shelfVersionShown` already exists), and the deck visual
(`CassetteDeck.OnChanged` fires from `Apply`).

---

## Phase B — TraxSessionSync: shared editor + live cursors + shared playback

New `Assets/3 - Scripts/Multiplayer/TraxSessionSync.cs`, channel `"TraxSession"`.
This is session state, not world state — nothing here is saved.

**Presence.** When a player opens/closes `ShuttleComputerUI`, they send a presence
event: `{ open, viewId (projects|arranger), projectId }`. Each machine knows whether
the partner is on the computer and which project they have open.

**Cursors.** While open, each client (and the host) publishes its cursor at ~12 Hz,
`UnreliableSequenced`: normalized canvas position (0–1 x/y — the canvas is a fixed
virtual layout so normalized coords land on the same widgets), `viewId`, and a
click-flash flag. Receiver renders a **ghost cursor**: small pointer sprite tinted
by the partner's suit swatch (`NetworkPlayerIdentity.netSwatch`) with their name in
6 pt under it, drawn on a top-sorted layer of the local computer canvas — only when
both players are in the same view of the same project; otherwise a one-line status
chip ("SAM is browsing the shelf"). Host relays client cursor to other clients
(2-player today, but keep the relay shape).

**Song co-editing (last write wins).** The working song `_song` in
`ShuttleComputerArrangerUI` becomes session-shared while ≥2 players have the same
project open:
- Every local mutation already funnels through a small number of choke points
  (`TraxInstrument.SetTrack` is THE track choke point; section add/remove/resize and
  song-level params in the arranger partial). Bump a local `_songRev` at those points.
- A coalescing tick (0.25 s floor) sends the **whole song** as JSON
  (`TraxSectionSave` list — reuse the save schema) to the host; host stamps it as
  session truth and rebroadcasts to everyone **except the reporter** (EconomySync
  skip-the-reporter rule; convergence is guaranteed by the next writer's full
  snapshot, and echoing back would fight the knob under your own cursor).
- Inbound apply sets an `ApplyingRemote` flag so apply never re-reports, rebuilds the
  arranger strip, and pushes changed tracks through `TraxInstrument.SetTrack` so a
  song playing on this machine updates live.
- The song is tiny (≤8 sections × ≤16 bars of pattern/param data) — whole-song JSON
  at these rates is well under the 8 KB chunk size in practice; chunk anyway.

**Shared playback.** Transport events (`play(sectionIndex, songMode)`, `stop`) are
reliable and relayed to all; each machine drives its own local `TraxAudioEngine`
from the shared song. A 2 s unreliable playhead correction (`sectionIndex, step`)
snaps a machine that drifted >1 bar, GalaxyTime-nudge style. Either player can
press play/stop — last press wins, both hear it.

**Save from the session** routes through Phase A's `RouteProjectSave`.

**Explicit non-goals:** no per-widget locks, no operational transforms, no text-field
co-editing (the save-name field stays local until submitted), no spectator mirror
upgrades (the world-screen `ScreenMirror` snapshot behavior is unchanged).

---

## Phase C — AlienSync: walking customers both players can see

New `Assets/3 - Scripts/Multiplayer/AlienSync.cs`, channel `"AlienSync"`, cloning the
`EnemySync` architecture (host-only sim, 10 Hz pose batches, `UnreliableSequenced`
absolute planet-local poses, reliable spawn/despawn events, self-healing re-request
on unknown id) with these deliberate differences:

- **Identity is the deterministic alien id** (`cell:{BodySlot}:{CellId}` /
  `scene:{name}` via `AlienIdentity`), not a minted NetId — it's stable, already the
  BuyerLedger key, and lets a guest map a pose to the right buyer.
- **In co-op, guests stop simulating/spawning wild aliens.** `AlienNPCSpawner`
  becomes host-driven: on the host it spawns around **every** player via
  `PlayerRoster.All()` (fixing the "nearest player = only player" trap in the
  streamer just like the ambush sweep below); guests spawn puppets on command.
- **Alien puppets KEEP their colliders and `Interactable`s** (unlike enemy puppets) —
  guests must be able to gaze, talk, sell, and play tapes to them. Only the
  brain is stripped: `AlienWander` disabled (pose comes off the wire), no local
  spawn/despawn decisions. The sell/listen flows already route through
  `EconomySync`, so interaction just works once the body is there.
- **Walk animation**: `AlienWander`'s leg swing is procedural off velocity; the
  puppet derives velocity from successive poses and reuses the same swing code, so
  walks look right without syncing animation state.
- **Ambush sweep** (`BuyerMessageDirector.AmbushSweep`): scan aliens within 60 m of
  **any** player (roster), approach that player; the approach target travels as a
  player clientId in the pose stream so the guest sees the alien bearing down on
  whoever it's ambushing.

**Not synced:** placed/scene aliens that never move don't need poses (they exist
identically on both machines); only wander/approach movers stream.

---

## Phase D — World-save-only + pod-only saving + guest saves

### D1. Character save shrinks to identity
`CharacterProfile` keeps `id`, `name`, `swatchIndex` (+ `schemaVersion`, `createdAt`,
and `CharacterBook.lastSelectedId` as launcher bookkeeping). `orientationMask` moves
into the world save's per-character block (below). Delete the two-tier dirty
plumbing: `CharacterStore.MarkDirty/SaveIfDirty/_dirty` and the `SaveIfDirty()` call
in `StasisPodSave`. `CharacterStore.Migrate` reads old files fine (extra field
ignored on load after the field is removed; keep `schemaVersion` bump to 3).
**`id` must survive** — `TevFronting.LocalId` and the new player blocks key on it.

### D2. Per-character personal blocks in the world save
New `[Serializable] PlayerBlockSave { string characterId; string characterName;
HotbarSave hotbar; EquipmentSave equipment; PlayerSave player; ResourceSave vitals;
OxygenSave oxygen; FishInventorySave fish; int wood; int crystal; int spaceDust;
bool dustFilter; int orientationMask; }` and `SaveData.playerBlocks :
List<PlayerBlockSave>` (JsonUtility-safe, append-only schema position).

- **Capture:** `SaveCollector.Capture` builds the local player's block from the same
  sources the legacy fields use, upserts it into `playerBlocks` by characterId, and
  **keeps writing the legacy top-level fields** as a mirror of the local player
  (backward compat + zero-risk single-player path; hotbar-wins ordering unchanged).
- **Apply:** `SaveCollector.Apply` looks up the block for
  `CharacterStore.ActiveProfile.id`; found → apply block (personal steps read from
  the block instead of the legacy fields); missing → fall back to legacy fields
  (old files), or fresh-start personal defaults when neither exists (new character
  entering an existing world — spawn like a guest arrival: reset hotbar/vitals,
  keep world).
- `ApplyWorldSubset` (guest join) additionally applies the guest's own block when
  the snapshot contains one — replacing today's unconditional
  `NewGameReset.ApplyGuestArrival` wipe (which remains the no-block fallback).
- `tevFronting` already keys by characterId — unchanged, now consistent.

### D3. PersonalSync: live partner fetch at save time
Small channel `"PersonalSync"`: `KindRequestBlock` (any → any via host relay) and
`KindBlock { characterId, json }` back. On pod save in co-op, the saving machine
requests every other player's block, waits up to 3 s, then writes; a missing reply
degrades to that player's last-known block (from load/join) rather than blocking
the save. Single player skips all of it.

### D4. Guest saving at the pod
The pod ritual (`StasisPodSave.Ritual`) on a **guest** must not trust its local
world capture (guest-side enemies are puppets, some state is host-only). Flow:
guest pod-save → `KindRequestWorldSave` to host → host runs the normal
`SaveCollector.Capture` (which now upserts the host's block), attaches the guest's
block (host requests it via PersonalSync, or the guest sends it with the request —
choose: **guest attaches its own block to the request**, one round trip), host
returns the full SaveData JSON chunked → guest writes it to its own disk under the
guest's own `ActiveSlotName`. Host pod-save is the mirror image without the
round-trip to itself. Both machines can therefore hold their own copy of the world.

### D5. Pod is the only save point
Remove: the pause-menu SAVE GAME button (`TabbedPauseMenu`), `AutosaveManager`'s
periodic timer + its one-shot calls (`PortalManager`, `OxygenManager`), and the
legacy `SettingsMenu` save hooks. Keep: `NewGameReset`'s single initial write
(retargeted from `"autosave"` to `StasisPodSave.ActiveSlotName` so a brand-new world
has a valid slot for death-reload before the first pod save). Death reload
(`DeathCutsceneController` → `ActiveSlotName`) is unchanged.

### D6. Levels
Nothing to do — `progress` is already world-saved and the level system is vaulted;
leave the schema round-tripping as is.

---

## Build order & risk

A (low risk, proven pattern) → D (medium: schema migration, but legacy mirror keeps
old saves loading) → C (medium: EnemySync clone with collider/interaction deltas) →
B (highest novelty: cursors + LWW song sync; land it last so a playtest of A/C/D is
possible even if B needs iteration).

Compile-check every phase with `python prototypes/shuttle-computer/test/compile-unity.py`;
run the headless suites (Trax/Deal tests) after A and B since TraxLibrary/DealTerms
are in the no-Unity assembly. Playtest is Sam's, two machines.

## Traps to respect (from the code, not theory)
- Never `SendNamedMessageToAll` for a delta; address clients explicitly, skip the
  reporter (rebroadcast-storm lesson).
- Guard every host→client kind `when !IsServer`; set an `ApplyingRemote` flag during
  apply so applies never re-report.
- No world-space coordinate on the wire — planet-local only (floating origin).
- Absolute values, not deltas, so dropped packets self-correct.
- Register message handlers on every machine, not just senders.
- New serialized fields go at the END of SaveData classes; playerBlocks is additive.
- `TraxLibrary`/`TraxPrints`/engine files must stay Unity-free (test suite compiles
  them headless) — sync code lives in `Multiplayer/`, never in the engine files.
- Alien ids must keep matching `BuyerLedger` keys or every bond orphans.
- `StoryDirector.Version` bump must live inside the store's write path, not at call
  sites (the "watched, not hooked" EconomySync lesson).
