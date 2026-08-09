# Character System v2 — Name + Suit Colour (verified build plan)

**Supersedes** `Handoff_CharacterSystem_NameColour_v1.md`. Same feature, same scope.
Everything below has been checked against the code on `feat/helmet-hud` (2026-08-09).
Where v1 and reality disagreed, **reality won** — those points are marked ⚠️.

---

## 0. What verification changed

| v1 said | Reality | Effect on the plan |
|---|---|---|
| "Provide Sam a placement manifest for new scene objects/prefabs" | `MainMenuController` builds the **entire menu in code** (`BuildCanvas`, `BuildButton`, `BuildCreditsPanel`). `MainMenu.unity` contains only EventSystem, MenuRoot, Cleanup, Main Camera. | ⚠️ **No manifest needed.** All UI is code. Exactly one optional inspector drag — see §5. |
| "Replace the host's own 'Player 1' label" | `NetworkPlayerSetup.OnNetworkSpawn` calls `CreateNametag()` **only in the `else` branch of `if (IsOwner)`**. You never get a tag over your own head. There is no "Player 1" string in the codebase. | ⚠️ Requirement dropped. Two real strings to replace, not three. |
| "Connection payload **or** NetworkVariables — inspect and propose" | No `ConnectionApprovalCallback` anywhere in the repo. `PlanetRelativeSync` already uses owner-write `NetworkVariable`s on the player object. | **Proposal: NetworkVariables.** Payload would mean standing up approval plumbing that doesn't exist. |
| (silent) | `NameStore.PlayerName` already exists. It *looks* like a collision, but the naming flow that wrote it was retired — `AIChatScreen.Init` force-completes first contact, so it has resolved to the literal `"Player"` for a while. | No conflict. The character name now feeds `ResolvedPlayerName` — see §7. |
| (silent) | `Suit.mat` is a **shared asset**, resolved from `Astronaut.fbx` by legacy name-search (`materialLocation: 1`, `externalObjects: {}`). | Tint per-instance only. Writing `sharedMaterial.color` would edit the asset on disk. |

### Confirmed as described in v1
- Relay + Lobby co-op, 4-digit code + password, `FeatureVault.Multiplayer` gating — all present.
- Puppet architecture — `NetworkPlayerSetup` destroys every non-network MonoBehaviour, disables all colliders, hides renderers on the owner.
- `MultiplayerSession` is a `DontDestroyOnLoad` singleton that deliberately does **not** skip MainMenu, and its own comment states it therefore needs no `EnsureGameplaySingletons` seeding.
- The two placeholder strings: `NetworkPlayerSetup.cs:78` (`$"Player {OwnerClientId + 1}"`) and `MultiplayerSession.cs:778` (`$"Player {i + 1}"`).

---

## 1. Data layer

**`CharacterProfile`** (plain `[Serializable]`, `JsonUtility`-safe — no dicts, no polymorphism):

```
string id;            // GUID, the stable key everything later hangs off
string name;
int    swatchIndex;
int    schemaVersion; // starts at 1
string createdAt;     // ISO-8601
```

**`CharacterBook`** — `List<CharacterProfile> characters` + `string lastSelectedId`.
Stored at `Application.persistentDataPath/characters.json`, i.e. beside `saves/`, **not inside it** —
characters outlive any individual world.

Growth rules, unchanged from v1 and worth restating because §8 depends on them:
unknown fields tolerated on load, `schemaVersion` gates future migration, nothing assumes the
profile is only name+colour, and **no level/hotbar/money fields are added now**.

**`CharacterStore`** — the runtime singleton. Mirrors `MultiplayerSession` exactly:
`RuntimeInitializeOnLoadMethod(AfterSceneLoad)`, `DontDestroyOnLoad`, `Instance` guard in `Awake`,
cleared in `OnDestroy`, and — like `MultiplayerSession` — **deliberately does not skip MainMenu**,
because the menu is where it does its job. That also means it does **not** need seeding in
`EnsureGameplaySingletons` (trap #1 is satisfied by not skipping, not by seeding).
Not gated by `FeatureVault.Multiplayer` — solo uses it.

Exposes `Active` (the selected `CharacterProfile`), `All`, `Create/Rename/Recolour/Delete`, `Select(id)`, `Save()`.

---

## 2. Swatch palette (proposed — 10 entries)

Index syncs over the network, never RGB. Index 0 is the default and matches today's
`Suit.mat` `_Color` (0.8 grey). Indices 1–4 reproduce the existing
`NetworkPlayerSetup.ClientColors` so the current multiplayer look survives the change.

| # | Name | Hex | Note |
|---|---|---|---|
| 0 | Standard | `#CCCCCC` | default; today's suit |
| 1 | Mission | `#F2732E` | = ClientColors[0] orange |
| 2 | Signal | `#4DB3FF` | = ClientColors[1] blue |
| 3 | Bio | `#66E666` | = ClientColors[2] green |
| 4 | Nebula | `#E666E6` | = ClientColors[3] magenta |
| 5 | Solar | `#FFD23F` | |
| 6 | Rust | `#D94A3D` | |
| 7 | Violet | `#8B5CF6` | |
| 8 | Ice | `#5BD8FF` | = menu AccentCool |
| 9 | Graphite | `#5A6070` | dark option, still reads in daylight |

Lives in one static `SuitPalette` class. **Never renumber** — the index is persisted and networked.
Appending is safe; a profile referencing an out-of-range index clamps to 0.

**The visor is free.** `Astronaut.fbx` carries two materials: `Suit.mat` (`_Color` 0.8 grey,
Standard shader, **no texture** — so a flat tint reads cleanly) and `Suit Dark.mat` (0.1376 grey).
The visor is the dark one. We tint only the renderer slot bound to `Suit`, so "visor stays black"
requires no special-casing.

---

## 3. Menu UI

All built in code inside `MainMenuController`, matching its existing idiom
(`NewUI`, `BuildButton`, `ApplyDefaultFont`, the `creditsPanel` SetActive-toggle pattern,
`UiSfxPlayer.Attach` for hover/click SFX, and hiding `mainMenuButtonsRoot` while a modal is open
— the same reachability guard `OnCredits` and `OnCommunityGallery` already use).

New files: `CharacterStore.cs`, `CharacterProfile.cs`, `SuitPalette.cs`, `CharacterUI.cs`,
`SuitTinter.cs`, `NetworkPlayerIdentity.cs`. Modified: `MainMenuController.cs`,
`NetworkPlayerSetup.cs`, `MultiplayerSession.cs`.

Screens: no-characters popup, create/edit, list, picker.

**Flow: "remembered identity" (Sam's pick, from the three mockup options).** The menu never asks who
you are — it uses your last character until you deliberately change it:

```
MAIN MENU  ── "PLAYING AS ZIB ▾" chip under the title ──► quick picker
  ├─ START GAME   ──► save select ──► world          (no character step at all)
  ├─ MULTIPLAYER  ──► host / join                    (no character step at all)
  └─ CHARACTERS   ──► list ──► view / edit / add / delete
```

The only interruption is owning **zero** characters, which routes through the popup → create screen
and then continues to wherever you were going. Rationale: you should not re-pick your identity every
time you load a game.

**Navigation is one callback, owned by the entry point.** Screens move between each other by calling
each other's `Build*` methods; only `CloseAll` fires the caller's `onClosed`. The first draft gave
each screen its own "what next" callback and nesting create inside the popup silently overwrote the
continuation — creating your first character closed the UI and did nothing. Worth preserving.

Defaults adopted from v1 §6: 16-char cap trimmed, empty/whitespace rejected, duplicate names allowed,
delete behind a confirm, deleting the last character re-triggers the popup, no cap on count, list scrolls.

The solo path has a single clean insertion point: `OfferMultiplayerThen(EnterGameplay)` is already
"the single waist both save paths converge on". The character gate goes in front of
`OpenSaveSelectionPanel()`; nothing about `SaveLoadUI`, `PendingLoad` or `NewGameReset` changes.

---

## 4. Multiplayer identity sync

New `NetworkPlayerIdentity : NetworkBehaviour` on `NetworkPlayer.prefab`, alongside
`PlanetRelativeSync`.

> ⚠️ **It must be a `NetworkBehaviour`.** `NetworkPlayerSetup.OnNetworkSpawn` destroys every
> component that is not a `NetworkObject`/`NetworkBehaviour`. A plain MonoBehaviour would be
> deleted the instant it spawned.

Two owner-write NetworkVariables, mirroring `PlanetRelativeSync`'s permission pattern:

```
NetworkVariable<FixedString32Bytes> netName  (Everyone read, Owner write)
NetworkVariable<int>                netSwatch (Everyone read, Owner write)
```

`FixedString32Bytes` because NGO cannot serialise `string` in a NetworkVariable — 16 visible chars
fits comfortably. Owner writes once in `OnNetworkSpawn` from `CharacterStore.Active`; everyone else
subscribes via `OnValueChanged` **and reads the current value once on spawn** (late joiners get the
value in the spawn snapshot, not as a change event — reading only the callback is the classic
late-join bug here).

Ordering: `NetworkPlayerSetup` currently builds the nametag in its own `OnNetworkSpawn`. Simplest
fix is to move nametag creation into `NetworkPlayerIdentity` so text and colour have one owner,
leaving `NetworkPlayerSetup` to do only the stripping it does today.

`SuitTinter` applies the colour via **`MaterialPropertyBlock`** on the renderer whose shared
material is `Suit` — per-instance, zero material instantiation, and no risk of writing to the
shared asset. Applied on the local player too, so you see your own colour on your arms.

Roster (`MultiplayerSession.RefreshRoster`): lobby player names come from the Lobby service, so the
character name goes up as a lobby player data field at create/join time and `RefreshRoster` reads it
instead of `$"Player {i + 1}"`. "You" and "(host)" suffixes stay.

Only these sync pieces sit behind `FeatureVault.Multiplayer`; the character system itself does not.

---

## 5. The 3D preview (Sam's pick — spinnable)

`AstronautPreview` builds a private rig in code — the model, a camera, and three directional
lights — parks it 10,000 units from the origin, and renders it to a RenderTexture shown in a
`RawImage`. **Drag it to spin**: horizontal drag is yaw and is yours to keep, vertical drag is pitch
clamped to ±22–28° and eases back to level so it can never be left stuck at a bad angle. It drifts
slowly when untouched, which advertises that it is 3D without needing a label.

Two layers of isolation so it can never leak into the menu or vice versa: a dedicated layer (the
highest unnamed one) that only the preview camera renders and only its lights light, plus the park
distance with a 50-unit far clip as a fallback if all 24 user layers are taken.

The camera is fitted to the model's **actual renderer bounds**, so FBX import scale cannot break
the framing. Root motion is disabled on the Animator, or the default clip walks it out of frame.

**The one authored dependency** — `MainMenu.unity` has no astronaut and `CLAUDE.md` bans
`Resources.Load` outside three whitelisted folders — is a `[SerializeField] GameObject
astronautPreviewPrefab` appended at the **end** of `MainMenuController` (per the
append-serialized-fields-at-the-end convention). It has been assigned to `Astronaut.fbx` already,
via an additive scene load so the open gameplay scene was never disturbed. A null reference is
tolerated: the create screen falls back to a flat colour plate and everything else still works.

---

## 6. Acceptance tests

v1 §8's list, adopted as written, with two corrections:
- Test 3's "zero `Player 2` strings" now has an exact definition: `NetworkPlayerSetup.cs:78` and
  `MultiplayerSession.cs:778` are the only two sources.
- Add: **delete the character you are currently selected as**, then START GAME — must not launch
  with a null `Active`.
- Add: **host and guest pick the same swatch** — both suits tint correctly and independently
  (catches a shared-material regression).

---

## 7. `NameStore` — resolved, no collision

Sam: *"HAL doesn't ask your name, that's old code. It should use your character's name."* Confirmed
in the source — `AIChatScreen.Init` (line 198) says the naming flow was **retired** when typed input
was removed, and force-sets `FirstContactComplete = true`. The name-capture branches at lines 758
and 779 are unreachable, so `NameStore.PlayerName` is never written at runtime and has been
resolving to the literal `"Player"` ever since.

So there was nothing to reconcile. `NameStore.ResolvedPlayerName` now checks
`CharacterStore.ActiveName` first and falls back to the old field.

Resolving at *read* time rather than copying into `PlayerName` at load time is deliberate: a
character is cross-save and `PlayerName` is per-save, so a loading world would otherwise restore a
stale name over the current one — and preventing that would mean threading a new step into
`SaveCollector`'s fragile 17-step apply order. The field and its save round-trip are untouched, so
old saves load exactly as before. Side effect: HAL now uses your real name instead of "Player".

---

## 8. Why this is the right first slice for the Terraria goal

The end goal is levels, money, hotbar and upgrades travelling between worlds. That is a much
larger change than name+colour, because those systems are currently **owned by the save**:
`SaveCollector` captures them per-world and `NewGameReset` wipes them for a new game. Making them
character-scoped means `SaveCollector` must *stop* owning them and `NewGameReset` must *stop*
clearing them — an invasive, regression-prone edit to the fragile 17-step apply order (trap #3).

Doing identity first is correct sequencing: it is small, it is not load-bearing for anything
existing, and it establishes the `id` that the later transfer hangs off. Ship it, play it, then do
the grind transfer as its own handoff with its own save-migration pass.

One thing to decide before that later handoff, not now: money and levels crossing worlds is a
**balance decision**, not just a technical one. A level-20 character walking into a fresh world
skips the whole opening. Terraria accepts that deliberately. Worth being deliberate about it too.

---

## 9. Not in scope

Unchanged from v1 §7: no profile grind fields, no world-state sync, no changes to core systems,
the stasis-pod save flow, or Phase A economy tunables.

---

## 10. Build log — what actually shipped (2026-08-09)

Compiles clean. Data layer verified by a 23-check pure-logic smoke test (JsonUtility round-trip,
missing/unknown-field tolerance, sanitize, palette clamping, legacy-colour parity, wire-size).
**Not yet play-tested** — that is Sam's next step.

**New** — `Assets/3 - Scripts/Character/`
| File | Role |
|---|---|
| `CharacterProfile.cs` | `CharacterProfile` + `CharacterBook`, sanitize, 16-char cap |
| `SuitPalette.cs` | the 10 swatches; never renumber |
| `CharacterStore.cs` | singleton, `characters.json`, mutations, local-player tint |
| `SuitTinter.cs` | MaterialPropertyBlock tinting, visor-safe |
| `AstronautPreview.cs` | the spinnable 3D rig + `AstronautPreviewDrag` |
| `CharacterUI.cs` | popup / create / edit / list / picker / delete-confirm |

**New** — `Assets/3 - Scripts/Multiplayer/NetworkPlayerIdentity.cs`, added to
`NetworkPlayer.prefab`.

**Modified**
- `MainMenuController.cs` — CHARACTERS button, "PLAYING AS" chip, `RequireCharacter` gate on
  START GAME / MULTIPLAYER, button column grown to 6 rows (488px — it clips otherwise), serialized
  astronaut field.
- `NetworkPlayerSetup.cs` — the hard-coded `"Player {N}"` nametag and `ClientColors` **deleted**;
  it is back to only stripping the puppet.
- `MultiplayerSession.cs` — character name published as lobby player data at create/join;
  `RefreshRoster` reads it instead of `$"Player {i + 1}"`.
- `PlanetRelativeSync.cs` — the "Remote player N" debug log now names the character.
- `NameStore.cs` — `ResolvedPlayerName` prefers the active character.
- `MainMenu.unity` — astronaut prefab reference assigned.

**Zero `Player N` placeholder strings remain** (verified by grep across `Assets/`).

### Two bugs caught in self-review, before playtest
1. **Lost continuation.** Nested screens each carried their own callback, so creating your first
   character from the popup overwrote the "…and then open save select" continuation and did
   nothing. Fixed by the single entry-point-owned `_onClosed` (§3).
2. **Stuck hidden menu.** The chip used `RequireCharacter`, whose callback only runs on success —
   so cancelling the create screen left the main-menu buttons permanently hidden. Fixed with a
   separate `OpenCreate(onClosed)` that always fires.

### Watch-list for the playtest
- The Animator plays the FBX's default clip in the preview. If that reads oddly, it is one line in
  `AstronautPreview.Construct`.
- Local-player tint uses throttled retries (20 × 0.25s) waiting for the `Player`-tagged object;
  a guest repositioned late by `SecondPlayerArrival` should still be caught, but worth confirming.
- Late join / rejoin: identity reads the spawn snapshot *and* subscribes to changes, which is the
  fix for the classic "joined too late to see the name" bug — but it is untested over a real relay.
