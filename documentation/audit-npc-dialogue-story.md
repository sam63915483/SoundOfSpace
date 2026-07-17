# Audit: NPC Dialogue & Story

Scope: `Assets/3 - Scripts/NPC_Dialogue/` (16 files) and `Assets/3 - Scripts/Story/`
(22 files). Read-only pass, verified against source (not the audit doc). Cross-checked
`Assets/3 - Scripts/AI/TokenResolver.cs`, `UI/MainMenuController.cs`, and
`Assets/StreamingAssets/Story/*.json` where they gate correctness.

## Summary

The systems are in good shape overall. The preset branching-dialogue core
(`DialogueData` → `StoryContent` → `DialogueRunner` → `DialoguePresenter`
implementations) is clean, null-guarded, and correctly decoupled. The zero-alloc
typewriter guidance is followed everywhere: **every** typewriter path uses
`DialogueTextStyling.RevealCharsTMP` (TMP `maxVisibleCharacters`) or
`RevealCharsLegacy` (Substring for legacy `Text`) — there is **no `text += c` O(n²)
loop anywhere** in either folder. Bone manipulation in `NPCWaveAnimation` correctly
runs in `LateUpdate`. Trap #1 (MainMenu-skipping singletons) is satisfied:
`StoryDirector`, `HintTrackRunner`, `Mission2Director`, `ColdCompanyDirector` are all
seeded in `MainMenuController.EnsureGameplaySingletons` (MainMenuController.cs:658-663),
and `PostGreetingChoicePanel` intentionally skips the MainMenu early-return so it
doesn't need seeding.

The main real issues are (a) a repeated per-frame string allocation in the
hand-written NPC dialogue scripts that a sibling script already proved worth fixing,
(b) a latent shared-`dialogueText` contention bug between co-located NPCs, and (c) a
handful of dead constants/methods.

Counts: **6 bugs** (0 high, 1 medium, 5 low), **6 dead/redundant items**,
**5 performance items**.

---

## Bugs (severity, file:line, description, fix)

### BUG-1 (Medium) — Per-frame prompt string allocation in 6 NPC scripts
Files/lines:
- `NPC_Dialogue/NPCDialogue.cs:104` (also :86, :162, :321)
- `NPC_Dialogue/BonfireNPCDialogue.cs:105` (also :125)
- `NPC_Dialogue/RandomAlienDialogue.cs:99` (also :119)
- `NPC_Dialogue/GuitarShopNPC.cs:90`
- `NPC_Dialogue/TevDialogue.cs:232` (also :252)
- `NPC_Dialogue/ShipInstructorDialogue.cs:121`

Each of these re-shows the talk prompt **every frame** while the player is in range,
building `$"Press {PromptGlyphs.Interact} to talk"` with string interpolation each
time. The interpolated string is allocated at the call site regardless of whether
`InteractPromptUI.Show` internally de-dupes. This is the exact pattern
`BonfireInteraction.cs:128-148` already fixed with a cached `_promptCached` /
`_promptCachedSource` pair (its comment: *"The previous version's `$"..."` string
interpolation allocated ~1.2 KB per frame even when nothing changed."*). With several
NPCs in a village, this is steady multi-KB/frame GC churn.

Classified as a bug because a fix already exists in a sibling file and simply wasn't
propagated. **Fix:** mirror the `BonfireInteraction` cache — store the built string
and the `TutorialGate.InputSource` it was built for; rebuild only when the source
flips (F↔X glyph swap).

### BUG-2 (Low) — Shared `dialogueText` contention between co-located NPCs
Files: `BonfireNPCDialogue.cs:58-66`, `RandomAlienDialogue.cs:49-57`,
`TevDialogue.cs:179-187`, `ShipInstructorDialogue.cs:85-93`.

All four auto-borrow the single scene `NPCDialogue.dialogueText` /
`talkPromptText` TMP object via `FindObjectOfType<NPCDialogue>()`. If two of these NPCs
are talkable/active at overlapping positions (e.g. Tev standing near a wandering alien,
or two spawned aliens), they write to the **same** TMP object and stomp each other's
lines / active-state toggles. `RandomAlienDialogue` even contains a guard method,
`IsAnotherNPCUsingPrompt()` (RandomAlienDialogue.cs:125), written to mitigate exactly
this — but it is **never called** (see DEAD-1). **Fix:** either give each NPC type its
own TMP, or actually consult a shared "who owns the dialogue surface" arbiter before
showing a line.

### BUG-3 (Low) — `InterrogationDialogue.Start` dereferences Inspector refs with no null guards
File: `NPC_Dialogue/InterrogationDialogue.cs:66-76`.
`choicePanel.SetActive(false)`, `outcomePanel.SetActive(false)`,
`button1/2/3.onClick.AddListener(...)`, and `locationText.text = ...` all run with no
`!= null` checks. Every other dialogue script in the folder guards these. If any ref is
left unassigned in the Inspector this NREs in `Start` and the whole interrogation scene
is dead. **Fix:** add null guards (or `[SerializeField]` + a Start-time validation log)
consistent with `NPCDialogue`/`ORGDialogue`.

### BUG-4 (Low) — `DialogueRunner.Start` can NRE on a malformed first node
File: `Story/DialogueRunner.cs:16`.
`_conv.nodes[0].id` — the guard checks `_conv.nodes.Length > 0` but not that
`nodes[0]` is non-null. A `conv_*.json` whose `nodes` array has a null/empty leading
element (JsonUtility will happily produce a default-constructed element, but a hand-hacked
file could differ) throws. Low likelihood, but authored-content robustness is the whole
point of the loader. **Fix:** null-check `nodes[0]` and fall back to `"end"`.

### BUG-5 (Low) — Silent conversation-id collisions / id-less files in the loader
File: `Story/DialogueData.cs:77-81`.
`Conversations[c.id] = c` overwrites on duplicate id with no warning, and a `conv_*.json`
whose parsed `id` is empty is silently dropped (line 80 requires `!string.IsNullOrEmpty(c.id)`).
A copy-paste id collision between two files loses one conversation with no diagnostic;
same for a file that forgot to set `id`. **Fix:** `Debug.LogWarning` on both an
overwrite and an empty-id drop.

### BUG-6 (Low) — Orphaned "OnShelterBuilt" objective path with no consuming gate
Files: `Story/StoryDirector.cs:335-344` (`HandleBuildingPlaced`), `CheckGates:352-390`.
Placing a Cabin still sets `hasShelter` and completes any objective whose
`completionEvent == "OnShelterBuilt"`, but `CheckGates` no longer has a shelter gate
(the `NeedsShelter` step is deprecated — StoryDirector.cs:11). If any objective in
`objectives.json` is still keyed to `OnShelterBuilt` it will complete but advance
nothing; if none is, `HandleBuildingPlaced` is pure dead wiring. Harmless today but a
trap for the next content edit. **Fix:** confirm no live objective uses `OnShelterBuilt`
and remove the handler, or document why it's retained.

---

## Redundancies / Dead Code

- **DEAD-1** — `RandomAlienDialogue.IsAnotherNPCUsingPrompt()`
  (RandomAlienDialogue.cs:125-135): fully implemented, **zero callers** (grep-confirmed).
  It's the intended mitigation for BUG-2; either wire it in or delete it.
- **DEAD-2** — `Mission1.FlagExplored` (`"m1_explored"`, Mission1.cs:18): declared,
  **never read or written** anywhere (grep-confirmed). The explore gate actually keys on
  vendor visits (`ExploredEnough()` = `VisitedFishVendor() && VisitedGoodsVendor()`,
  Mission1.cs:84) + `FlagReported`. Dead constant.
- **DEAD-3** — Trigger-discoverable machinery is vestigial for the current slice:
  `Mission1.DiscVista/DiscStructure/DiscFishing`, `AllDiscoverables`, `MarkSeen`,
  `WasSeen`, `SeenCount` (Mission1.cs:41-81) plus `Discoverable.cs`. `Discoverable`
  writes via `MarkSeen`, but the report gate never consults `SeenCount()`/`WasSeen()` —
  it uses vendor visits. The code comment (Mission1.cs:38-44) acknowledges these are
  "kept for future explore beats." Note, don't remove — but it's inert.
- **DEAD-4** — `StoryStep.NeedsShelter` (StoryDirector.cs:11): deprecated, kept only for
  save-int stability (documented). Fine as-is; flagged for completeness.
- **REDUNDANT-1** — `BonfireInteraction` legacy serialized refs (BonfireInteraction.cs:28-32,
  `commonCountText`…`cookStatusText`): `[HideInInspector]`, unused by the new flow, kept
  for scene compatibility (documented). Acceptable.
- **REDUNDANT-2** — `NPCConversationTracker` (whole file) is a 1-method static event
  relay; fine, but note it forwards a raw `MonoBehaviour` and consumers `is`-type-check
  it (`StoryDirector.HandleNpcConversation:307-311`) — brittle if an NPC type is renamed.

---

## Performance / Optimization

- **PERF-1** — See BUG-1: per-frame interpolated prompt strings across 6 scripts. The
  single highest-value fix in this audit; the cache pattern already exists in
  `BonfireInteraction.cs:128-148`.
- **PERF-2** — `NPCWaveAnimation.UpdateHeadLookAt` (NPCWaveAnimation.cs:221):
  `GameObject.FindWithTag("Player")` runs **every frame until** the player is found, then
  caches. Matches the project's "retry until non-null" convention, so acceptable — but if
  the Player tag never resolves (e.g. head-tracking NPC in a scene with no tagged player)
  it's an unbounded per-frame `FindWithTag`. Consider a throttled retry (see
  `LightLookAt.cs` per CLAUDE.md).
- **PERF-3** — `DialogueRunner.GoToNode` (DialogueRunner.cs:29-31) allocates a fresh
  `string[]` and runs `TokenResolver.Resolve` on every line on each node visit. Fine at
  click-pace (not a hot path); noted only so it isn't moved into a loop later.
- **PERF-4** — `CopEnergyBlast.BuildArcs` (Story/CopEnergyBlast.cs:96) does
  `new Material(Shader.Find("Particles/Standard Unlit"))` per blast and never destroys it
  in `Resolve`/`OnDestroy` — a small material leak per shot. Chase can fire many blasts.
  (Combat mechanic, adjacent to scope.) **Fix:** cache one shared static arc material, or
  `Destroy(arcMat)` on resolve.
- **PERF-5** — `WorldDialogueUI` / `DialogueReplyColumn` / `PostGreetingChoicePanel`
  rebuild reply buttons by `Destroy` + re-`new GameObject` on every `Show`
  (WorldDialogueUI.cs:216-238, DialogueReplyColumn.cs:59-98, PostGreetingChoicePanel.cs:150-187).
  Low frequency (once per dialogue node), so acceptable; not worth pooling.

---

## Notes & Uncertainties

- **Voice-clip binding fragility (intentional):** `TevSmugglingMission.FindClip`
  (Story/TevSmugglingMission.cs:412-418) matches voice clips to spoken lines by **exact
  string equality** against three hardcoded tables (`OfferLines`, `CopConvLines`,
  `TevConvLines`, lines ~116-236) that must stay byte-identical to the `conv_b1_*.json`
  files. A drift plays **silent with no error**. The code documents this and points to a
  "Validate B1 voice bindings" editor tool. Working as designed, but the single most
  brittle coupling in the story code.
- **`HintTrackRunner` index safety:** `Advance`/`OnWoodChanged` read
  `_track.entries[_entryIndex]` directly (HintTrackRunner.cs:82,90). Verified safe: the
  only mutator, `AdvanceEntry` (line 72-77), stops the track before `_entryIndex` can go
  out of range, so the invariant `_track == null || _entryIndex ∈ [0,len)` holds. No bug.
- **`Mission2Director.HandleChange` early-return inside try/finally**
  (Mission2Director.cs:80-124): the `return` when `sd==null||kb==null` still runs the
  `finally` that clears `_inHandle`. Correct — re-entrancy guard is not left stuck. No bug.
- **Not deeply reviewed (adjacent combat/QTE mechanics, in the Story folder but outside
  the dialogue-system scope):** `TevSmugglingMission.cs` lines 1156-1539 (hatch-QTE UI +
  rocket routine), `CopShipController.cs`, `TevRocket.cs`. Skimmed for per-frame
  `FindObjectOfType`/`Camera.main`/`Find` — grep across `Story/*.cs` found **none**.
- **Uncertainty:** I confirmed the four story auto-singletons are seeded for trap #1, but
  did not exhaustively verify each `conv_*` id referenced by `QueueConversation` /
  `WorldDialogueUI.Begin` has a matching file in `StreamingAssets/Story/`. Present-and-
  accounted-for: `conv_first_contact`, `conv_gates`, `conv_village_arrival`,
  `conv_b1_*`, `conv_cc_first_lie`. **Not present** (guarded by `StoryContent.GetConversation
  != null`, so inert until authored): `conv_menu` exists but `conv_face_down`,
  `conv_face_down_after`, `conv_we_need_to_talk`, `conv_interview`, `conv_tev_letter`,
  `conv_trade_back` are referenced in code but not in the JSON folder yet — expected per
  the Mission 2 "inert until content copied" design, not a bug.
