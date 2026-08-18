# HANDOFF: Co-op TRAX Loop + World-Save-Only — Implementation Plan

**Read the spec first:** `docs/superpowers/specs/2026-08-18-coop-trax-loop-and-world-save-design.md`
— all design decisions are locked there (Sam approved 2026-08-18). This file adds the
codebase map + concrete tasks so you can implement WITHOUT re-exploring.

Status: **nothing implemented yet.** Spec + this plan only. Branch: `feat/helmet-hud`.
Compile check without Unity: `python prototypes/shuttle-computer/test/compile-unity.py`
(grep output for `error CS`). Headless Trax/Deal test suites live under
`prototypes/shuttle-computer/test/` — run after touching engine-adjacent files.

---

## Codebase map (verified by exploration 2026-08-18 — trust this over the audit doc)

### Multiplayer layer (`Assets/3 - Scripts/Multiplayer/`, NGO 1.12.0 + Relay)
- **No base class — copy the shape of `EconomySync.cs`.** All world-sync channels are
  `CustomMessagingManager` named messages on plain MonoBehaviour singletons (no
  NetworkObject → no RPCs). Channels: WorldSync, EconomySync, EnemySync, StorageSync,
  StasisDoor, SolarState, GalaxyTimeSync (the last lives inline in `World/GalaxyTime.cs`).
- `WorldSync.cs` — `IsAuthority` (true in SP + host), `WorldReady` (gates all client
  requests), `ApplyingRemote` flag, join snapshot = host runs
  `SaveCollector.Capture("__worldsync__")` → JSON → 8 KB chunks →
  `ReliableFragmentedSequenced` → guest `SaveCollector.ApplyWorldSubset(data)`
  (SaveCollector.cs:1000). Dispatch helper at WorldSync.cs:447 shows the
  "address each client explicitly, never SendNamedMessageToAll for deltas" rule.
- `EconomySync.cs` — THE template. Host tick watches `BuyerLedger.Version`,
  `MushroomDealState.Version`, `TevFronting.Version`, `TapeMemory.Version`; 0.25 s
  coalescing floor; payload = `[Serializable] EconomyState` of the four save structs,
  JsonUtility, chunked. Client routes via static `Route*` methods gated on
  `ShouldRoute()` (= `nm.IsListening && !nm.IsServer`; returns false on host/SP so
  callers fall through to local mutation). Kinds table in EconomySync.cs:50-58
  (KindTapeSale=7, KindTapeHeard=8 already exist). After apply it calls
  `MessagesScreen.RefreshFromNetwork()` — copy that nudge idiom for UI.
- `EnemySync.cs` — template for Phase C. Host-only AI; identity via minted NetId
  stamped by a per-frame sweep of the static instance list; poses 24/batch @10 Hz
  `UnreliableSequenced`, planet-local, absolute; deaths ReliableSequenced same-frame;
  puppets have ALL colliders disabled (aliens must differ — keep theirs); handlers
  registered on EVERY machine (EnemySync.cs:172 documents the silent-drop bug);
  self-heals by re-requesting all on unknown NetId (EnemySync.cs:545).
- `StorageSync.cs` — lock-don't-arbitrate pattern (not needed for this feature, FYI).
- Player prefab NetworkBehaviours (`PlanetRelativeSync`, `NetworkPlayerIdentity`
  [netName FixedString32Bytes + netSwatch int — ghost-cursor tint source],
  `NetworkAvatarDetail`) use NetworkVariables. `PlayerRoster.All()` enumerates local
  rig + puppets — use it to fix every "nearest player" rule.
- Guest join flow: `MultiplayerSession` → scene load → `SecondPlayerArrival` (pod
  wake, `NewGameReset.ApplyGuestArrival()` wipe) → WorldSync snapshot pull (3 s
  retry) → `WorldReady=true` → EnemySync/EconomySync each pull their state.
- Wire conventions: JsonUtility save structs for bulk ("the save schema IS the
  network schema"); FastBufferWriter loose floats otherwise; quantise to bytes for
  0-1 meters; sizeHint = `string.Length * 4 + N`.

### TRAX loop (`Assets/3 - Scripts/Music/`, `Vendor/`, `Story/`, `World/`)
- `ShuttleComputerUI.cs` (1717 lines, code-built ScreenSpaceOverlay canvas, sortingOrder
  1000, real OS cursor, `PlayerController.isInModalSlotUI` gate, world screen gets a
  RenderTexture snapshot `ScreenMirror` on close). Partials:
  `ShuttleComputerProjectsUI.cs` (shelf, `_shelfVersionShown`),
  `ShuttleComputerArrangerUI.cs` (owns working `TraxSong _song`, `_sel`, `DoPrint()`
  ~line 1197). Doc comment says "entirely client-local" — Phase B changes that.
- Engine (UNITY-FREE, headless-compiled — never add UnityEngine refs):
  `TraxSong/TraxTrack/TraxParams/TraxLibrary/TraxPrints/TraxKind/SongEval/
  CravingRules/DealTerms/TapeValue/TapeCareer/TraxClassifier` etc.
  `TraxLibrary.Version` already exists and its comment invites the watcher.
  `TraxLibrary` also owns `_installed` plugins (world-shared by design;
  gates editing only, never playback). `TraxPrints.MakeId` derives print ids from
  songId so pressings converge across machines.
- `TraxInstrument.SetTrack` is the single mutation choke point for live audio;
  `TraxAudioEngine` = OnAudioFilterRead DSP with immutable Snapshot publish;
  `TraxTapePlayer` = separate pooled world playback (alien listens — leave alone).
- `CassetteDeck.cs` — static world state: `InsertedTier/InsertedKind/EjectedPrintId`,
  `OnChanged` event, comment says "host-authoritative for v1" but NOTHING implements
  it. Needs a Version counter. Props: `CassetteSlot.cs` (Interactable).
- Rent: `Story/MushroomQuest.cs` (misnamed; landlord state as StoryDirector counters:
  tevRentPerWeek/tevRentArrears/tevRentNextDueDay/tevRentSettled; `PayRent(amount)`
  calls PlayerWallet — split per spec). Billing: `Story/TevRentCollector.cs` on
  `GalaxyTime.OnDayChanged` (never deducts). UI: `Vendor/TevPaymentUI.cs`, dialogue
  `NPC_Dialogue/TevMushroomOnboarding.cs`. Plugin lock:
  `TevShopUI.PluginsLocked => MushroomQuest.PluginsLocked` (UnpaidDays >= 5); blanks
  never lock.
- Shop: `Vendor/TevShopUI.cs` `Stock[]` (SIREN 60/MOSS 90/SPINDLE 130/CAVE 180,
  ladder order load-bearing). `BuyPlugin` → wallet spend + `TraxLibrary.Install`.
- Career: `TapeCareer` counters live in StoryDirector (`tapesSoldTotal`); ONLY
  increment site is `BuyerLedger.ReportTapeDeal` (host-side for routed sales — so
  co-op counting already works). `FeatureVault.TapeCareerGate=false` (vaulted for
  playtest) — leave.
- Walking customers: `Music/CravingRules.cs` (pure),
  `BuyerMessageDirector.AmbushSweep/FinishAmbush` (host-only, 1/day, 60 m scan
  around *the* player — the nearest-player trap), `World/AlienWander.cs`
  (planet-local stroll + BeginApproach; procedural leg swing off velocity; comment
  admits "local theatre"), `World/AlienNPCSpawner.cs` (deterministic seed 98765,
  cell 50 m, radius 300 m, max 10, cell math in `SpawnerCubeface.cs`). Alien
  identity `AlienIdentity.Of` → `cell:{BodySlot}:{CellId}` / `scene:{name}` =
  BuyerLedger key — MUST stay the sync key.
- `GalaxyTime` already syncs (host absolute minutes every 5 s, snap >10 min else 25%
  nudge) and is seeded in `EnsureGameplaySingletons` (CLAUDE.md trap #1 — any new
  MainMenu-skipping singleton needs seeding too; the Sync singletons deliberately do
  NOT skip MainMenu so they dodge the trap — copy that).

### Save system (`Assets/3 - Scripts/SaveSystem/`, `Character/`, `Tutorial/`)
- `SaveData.cs` (812 lines) + `SaveCollector.cs` (2303: Capture line 12, Apply line
  1046 with documented step order, ApplyWorldSubset line 1000, ApplyHotbar AFTER
  legacy totals line 1128, hotbar-wins-over-wallet ordering).
- Character save = `characters.json` beside saves/: `CharacterProfile { id, name,
  swatchIndex, schemaVersion(2), createdAt, orientationMask }` — that's ALL.
  `CharacterStore.cs` statics `ActiveProfile/ActiveName/ActiveSwatch`; two-tier
  dirty flush `MarkDirty/SaveIfDirty` (delete per spec D1; the SaveIfDirty call is
  StasisPodSave.cs:240). Consumers of `id`: `TevFronting.LocalId`
  (TevFronting.cs:91), NetworkPlayerIdentity, NameStore.ResolvedPlayerName (prefers
  character name; world `nameStore.playerName` is vestigial).
  `OrientationObjectives` reads/writes orientationMask (moves to player block, D2).
- `StasisPodSave.cs` — ritual at line ~218: TutorialGate → `SaveSystem.Save
  (ActiveSlotName)` → OrientationObjectives.Complete → CharacterStore.SaveIfDirty →
  re-lock. `ActiveSlotName` static; boot-window download-wake never saves. NO
  host/guest gate exists on ANY save path today.
- Extra save paths to REMOVE (D5): `AutosaveManager.cs` timer (line 94) + one-shots
  in `PortalManager.cs:61`, `OxygenManager.cs:524`; `TabbedPauseMenu.cs:458` SAVE
  GAME button; legacy `Scripts/Game/UI/SettingsMenu.cs:175,180`. KEEP + retarget
  `NewGameReset.Apply` line ~207 forced autosave → write `ActiveSlotName` instead.
- `NewGameReset.Apply()` (line 102) resets everything incl. TraxLibrary/Prints/
  Deck/TapeMemory/GalaxyTime; `ApplyGuestArrival()` (line 81) = personal-only wipe,
  its comment names it as what changes when characters carry belongings (D2 does
  exactly that — guest applies own block instead when present).
- Personal fields currently top-level in SaveData (mirror into PlayerBlockSave, D2):
  hotbar (money = slot 7 via PlayerWallet view), equipment, player, resources,
  oxygen, fishInventory, wood, crystal, spaceDust(+filter). `progress` (levels) is
  world + vaulted — untouched. `tevFronting` already characterId-keyed — unchanged.

---

## Task list (build order A → D → C → B, per spec)

### Phase A — TraxSync (~1 session, low risk)
1. `CassetteDeck`: add `public static int Version` bumped in Insert/EjectBlank/
   PrintTo/TakeEjected/Clear/Apply (bump inside the store, not call sites).
2. `StoryDirector`: add Version counter in its flag/counter setters.
3. New `Multiplayer/TraxSync.cs` per spec Phase A (copy EconomySync scaffold:
   AutoCreate, register-on-all, HostTick version watch + 0.25 s floor + chunked
   TraxState snapshot, ClientTick WorldReady gate + 3 s request retry).
4. Routes + call-site wiring: `if (TraxSync.RouteX(...)) return;` at:
   ShuttleComputer save/delete + DoPrint, TevShopUI.BuyPlugin (spend stays local),
   CassetteSlot insert/eject/take, TevPaymentUI rent pay (split
   MushroomQuest.PayRent → local spend + host `ApplyRentPayment`).
5. Add `storyDirector` to `ApplyWorldSubset`.
6. UI nudges after apply: shelf counter, TevShopUI lock state, CassetteDeck.OnChanged.
7. Compile + headless suites. Commit.

### Phase D — Save rework (~1 session, medium)
1. `PlayerBlockSave` + `SaveData.playerBlocks` (END of class — serialization trap).
2. Capture: build local block, upsert by characterId; keep legacy mirror fields.
3. Apply: prefer own block → legacy fallback → fresh-personal defaults; same order
   as today's steps, just sourced from the block.
4. ApplyWorldSubset: apply guest's own block when present, else ApplyGuestArrival.
5. `Multiplayer/PersonalSync.cs`: KindRequestBlock/KindBlock (+ request-world-save
   kinds for D4). Guest pod save: send own block with request → host Captures,
   inserts guest block, returns full JSON chunked → guest writes to own slot.
   3 s timeout → save with last-known block. Host pod save: request guest block,
   3 s timeout, write.
6. StasisPodSave: route through the above when `nm.IsListening`.
7. Character shrink: remove orientationMask (→ block) + dirty plumbing; schemaVersion
   3; OrientationObjectives reads/writes the block via a small adapter.
8. Remove extra save paths (D5 list above); retarget NewGameReset initial write.
9. Compile; manual check old-save load path logic by reading, not guessing. Commit.

### Phase C — AlienSync (~1 session, medium)
1. `Multiplayer/AlienSync.cs` cloned from EnemySync; key = AlienIdentity string
   (FixedString or plain string write) not minted uint.
2. Host: AlienNPCSpawner spawns around all `PlayerRoster.All()`; stream spawn/
   despawn + 10 Hz planet-local poses for MOVING aliens only.
3. Guest: suppress local wild spawning in co-op; build puppets that KEEP colliders +
   Interactable, disable AlienWander sim, derive leg swing from wire velocity.
4. AmbushSweep: roster-wide scan; approach target clientId on the wire.
5. Compile. Commit.

### Phase B — TraxSessionSync (~1-2 sessions, highest novelty — LAST)
Per spec Phase B: presence events, 12 Hz normalized-cursor ghost (tint =
netSwatch, name label), whole-song LWW sync on `_songRev` bump at the SetTrack/
arranger choke points (0.25 s coalesce, host rebroadcast skip-reporter,
ApplyingRemote guard, rebuild strip + SetTrack on apply), reliable play/stop relay +
2 s playhead nudge. Non-goals listed in spec — don't exceed them.

### After all phases
- Update `docs/CURRENT_STATE_AUDIT.md` (multiplayer + save sections).
- Sam playtests on two machines. Then finishing-a-development-branch → push
  `soundofspace` remote (canonical), default branch `main`.

## Traps (cost real playtests before — do not relearn)
- Never SendNamedMessageToAll for deltas; skip the reporter; `when !IsServer` guards;
  ApplyingRemote flag; absolute values; planet-local coords only; register handlers
  on every machine; NetworkVariable = subscribe AND read once on spawn.
- "Nearest player" silently = "only player" — roster-audit every rule you touch.
- Engine files stay Unity-free; serialized fields append at END; new .cs needs
  `git add` including `.meta` (repo has a history of losing untracked files).
- Untracked .meta files from the last session are still uncommitted (SongEval,
  TapeCareer, TraxKind, EclipseShadowGate, GrassLightAutoMarker) — commit them with
  your first commit.
