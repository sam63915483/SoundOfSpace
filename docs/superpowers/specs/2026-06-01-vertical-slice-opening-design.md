# Vertical Slice — Opening Revamp + Story Backbone (Design Spec)

> **Status:** Approved design, 2026-06-01. Derived from `docs/GDD_VerticalSlice_BuildPlan.md`
> after a full codebase integration audit. This spec is the source of truth for the
> first implementation chunk; the GDD remains the source of truth for the overall slice.
>
> **Tag legend:** `[EXISTS]` integrate, don't rebuild · `[BUILD]` new code · `[AUTHOR]`
> content/writing · `[EDIT]` modify an existing file.

---

## 0. Scope

**This chunk:** the reusable story backbone (GDD §1) + the revamped opening (GDD §2) +
the phone preset-dialogue UI. **Endpoint:** free spawn → phone first-contact → water /
food / shelter soft gates → the "village reached" seam.

**Explicitly out of scope (later, on the same backbone):** Tev's first mission, the
trust-middle, the Backrooms file/reveal, the ending cutscene, the second (ORG) trust
meter, the mole mechanic.

### Hard constraints (from CLAUDE.md)
- **Do not touch** the atmosphere / celestial / planet-shader forbidden zone.
- **Do not alter** the ambient HAL channel (`HALCommentator`, `HALVolunteeredLog`,
  `HALLineHUD`) — it stays exactly as-is.
- **Do not reorder** `SaveCollector.Apply`. New capture/apply calls append at the
  documented step-6 (singleton-state) point only.
- Built-in RP, `Assembly-CSharp`, no `.asmdef`. New serialized fields go at the **end**
  of a MonoBehaviour. New `.cs` files need `git add` of both the file and its `.meta`.

---

## 1. Architecture overview

Four story-agnostic backbone systems + a phone view + an optional hint layer:

| System | Role | Pattern source |
|---|---|---|
| **StoryDirector** `[BUILD]` | persistent brain: story step, trust, flags | clone of `DeathCutsceneController` |
| **Dialogue data + DialogueRunner** `[BUILD]` | authored conversations as JSON, walked by a speaker-agnostic engine | new |
| **Effect vocabulary** `[BUILD]` | the 7 fixed story effects | GDD §1.3 |
| **Objective system + events** `[BUILD]` | goals bound to gameplay events that already fire | new, hooks existing static events |
| **PhoneDialoguePresenter + reply column** `[BUILD]` | renders dialogue in the phone; preset replies to the right of the chassis | hosts inside `AIChatScreen` |
| **Hint tracks** `[BUILD]` | optional, on-ask HUD walkthroughs reusing the tutorial pill | reuses `TutorialUI` |

Communication style matches the house pattern: **scattered static C# events**, no central bus.

---

## 2. StoryDirector — the brain `[BUILD]`

Code-only auto-singleton, shape copied verbatim from `DeathCutsceneController`:

```csharp
public static StoryDirector Instance { get; private set; }

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void AutoCreate()
{
    if (Instance != null) return;
    if (SceneManager.GetActiveScene().name == "MainMenu") return;   // trap #1
    var go = new GameObject("StoryDirector");
    DontDestroyOnLoad(go);
    go.AddComponent<StoryDirector>();
}

void Awake()
{
    if (Instance != null && Instance != this) { Destroy(gameObject); return; }
    Instance = this;
}
void OnDestroy() { if (Instance == this) Instance = null; }
```

**Seed in builds (trap #1):** add a block to
`MainMenuController.EnsureGameplaySingletonsAsync` next to the existing
`DeathCutsceneController` seed (~line 582):
```csharp
if (StoryDirector.Instance == null) { var go = new GameObject("StoryDirector"); DontDestroyOnLoad(go); go.AddComponent<StoryDirector>(); }
tick("story director"); yield return null;
```

### State (the only new persistent state)
- `StoryStep currentStoryStep` — enum: `ColdOpen, FirstContact, NeedsWaterFood, NeedsShelter, Explore, VillageSeam`.
- `float tevTrust` — single meter for this slice.
- Named story flags as **flat bool fields** (JsonUtility can't serialize dictionaries):
  `hasWater, hasFood, hasShelter, villageReached, metTevPrivately` (+ room to grow).
- Objective runtime state (active/complete ids) — see §5.
- Unlocked preset-question ids — see §4/§6.

### Why not reuse `EarlyGameProgress`
`EarlyGameProgress` flags (`FirstMealEaten`, `WaterBottleDrunk`, `CabinBuilt`, …) are set
by **tutorial steps**, which do not run on the free path. StoryDirector's flags are fed by
**raw gameplay events** (§5) so the self-directed player's progress registers with no
tutorial active. The two systems stay independent.

### Save / reset wiring
- `SaveData.cs` `[EDIT]`: add `StoryDirectorSave` (see §7) and a `storyDirector` field on
  the root `SaveData`. Flat fields + parallel `List<string>`/`List<bool>` for objective
  state — no dicts, no polymorphism.
- `SaveCollector.cs` `[EDIT]`: `CaptureStoryDirector` (called from `Capture`) and
  `ApplyStoryDirector` (called from `Apply` at **step 6**, the singleton-state group,
  alongside `ApplyEarlyGame`/`ApplyWorldFlags`).
- `NewGameReset.cs` `[EDIT]`: `StoryDirector.Instance?.ResetForNewGame()` in `Apply()`.
- **`[TEST]`** set `currentStoryStep = NeedsShelter`, die, confirm it is still
  `NeedsShelter` after the death-reload (mirrors the GDD backbone test).

---

## 3. Dialogue data (JSON) + DialogueRunner `[BUILD]`

JSON files under `StreamingAssets/Story/` (Claude-authorable, git-diffable, hot-editable;
no recompile to edit content). Deserialized with `JsonUtility` → only JsonUtility-safe
types.

### Schema
```
Conversation { string id; DialogueNode[] nodes; }
DialogueNode { string id; string speaker; string[] lines; PlayerResponse[] responses; }
PlayerResponse {
    string buttonText;
    string nextNodeId;        // node id, or "end"
    Effect[] effects;
    string startHintTrack;    // optional, presentation-only (see §6); "" = none
    string requiresFlag;      // optional gating: only shown if this flag is true ("" = always)
    string hiddenIfFlag;      // optional gating: hidden if this flag is true ("" = never)
}
Effect { string kind; string strArg; float numArg; bool boolArg; }
```
`requiresFlag` / `hiddenIfFlag` power the **grace branch** (hide "how do I get water?" once
`hasWater` is true) without new engine code.

### DialogueRunner — speaker-agnostic engine
Walks a conversation; emits to a `DialoguePresenter` interface:
```csharp
interface DialoguePresenter {
    void ShowLines(string speaker, string[] lines, Action onComplete);
    void ShowResponses(IReadOnlyList<PlayerResponse> responses, Action<PlayerResponse> onPick);
    void EndConversation();
}
```
Runner responsibilities: filter responses by `requiresFlag`/`hiddenIfFlag` against
StoryDirector; on pick, apply `effects[]` via the effect dispatcher, fire `startHintTrack`
if set, then go to `nextNodeId` (or end). The runner knows nothing about the phone — the
phone is one presenter; a future `TevDialoguePresenter` reuses the same runner.

**`[TEST]`** a throwaway 2-node conversation runs end-to-end and applies a `SetFlag` effect.

---

## 4. Effect vocabulary — fixed at seven `[BUILD]`

Closed set; resist additions (GDD §1.3). Dispatched centrally; each only mutates
StoryDirector state.

| `kind` | Behaviour |
|---|---|
| `SetFlag` | `strArg` = flag name, `boolArg` = value |
| `AdvanceStory` | `strArg`/`numArg` → set `currentStoryStep` |
| `AddTrust` | `tevTrust += numArg` |
| `StartObjective` | mark objective `strArg` active |
| `CompleteObjective` | mark objective `strArg` complete, run its `onComplete` |
| `UnlockDialogue` | add preset-question id `strArg` to the unlocked set |
| `TriggerEnding` | **logged no-op for now** (no ending this chunk; keeps the fork in data) |

---

## 5. Objective system + new events `[BUILD]`

```
Objective { string id; string description; string completionEvent; Effect[] onComplete; string hintTrackId; }
```
- Objectives become **active when their story step is reached** and complete **silently in
  the background regardless of story step** (GDD grace requirement). Completion conditions
  bind to events that already fire, plus the minimum new ones:

| Objective signal | Hook | Status |
|---|---|---|
| cooked food eaten | `BonfireInteraction.OnEat` (static event) | `[EXISTS]` reuse |
| clean water drunk | new `ResourceManager.OnCleanWaterDrunk` event, fired inside `DrinkWater()` | `[EDIT]` 1-line add |
| shelter built | `GhostPlacement.OnPlaced`, minimal condition = a Cabin buildable placed | `[EXISTS]` reuse |
| village reached | new `VillageReachTrigger` (one-shot `OnTriggerEnter`, Player tag) at `VillageMarker` | `[BUILD]` |

- On completion, an objective sets the matching StoryDirector flag (`hasFood`, `hasWater`,
  `hasShelter`, `villageReached`) and runs its `onComplete` effects.
- **`[TEST]`** `tevTrust` (or a flag) changes from a normal gameplay action with no story
  authored — i.e. the silent-grace path works.

### Objective display
Surface active objectives in the phone's **existing Quests page**. Verify its data source
during implementation; if it isn't cleanly feedable, fall back to a minimal objective list
on the same page. Do not build a second quest UI.

---

## 6. Phone wiring `[BUILD]`

### Remove typed input
In `AIChatScreen.cs`: delete/disable `BuildInputRow` (`TMP_InputField` + send button).
Hook the existing no-LLM `return;` placeholder so dialogue drives the chat instead.

### Reply column (`DialogueReplyColumn`)
Vertical button list anchored **to the right of the phone chassis** (chosen layout). It
always shows "what I can say right now":
- during a scripted exchange → the active node's filtered responses;
- in free time → the currently **unlocked** preset questions.
Clicking a button: posts a user bubble (`AddUserBubble`), applies the response's effects,
streams the next AI lines through the **existing** `AddAIBubble` + paced-reveal pipeline.
- **Anchoring:** lock to portrait orientation; hide the column in camera/landscape modes.

### Story-message channel
StoryDirector enqueues authored AI beats; reuse the phone's **existing unread badge /
notification strip** to signal them. Opening the AI app runs the pending node. No separate
inbox UI.

### Ambient channel
Untouched. `HALCommentator.VolunteerExternal` and the volunteered-log bubbles keep working.

---

## 7. Hint tracks — reuse the pill, on-ask only `[BUILD]`

Per-topic tracks (`water`, `food`, `fishing`, `shelter`) as **JSON data**:
```
HintTrack { string id; string objectiveId; HintEntry[] entries; }
HintEntry { string tipText; string advanceEvent; }   // advanceEvent = same gameplay events as §5
```
- `HintTrackRunner` renders entries through the **existing `TutorialUI` pill**, moved aside
  via its existing `SetLeftSide(true)`, advancing on the bound gameplay events.
- The legacy **ability gate is never engaged** — full freedom on spawn.
- A track shows **only when the player explicitly asks the AI** — the dialogue response
  carries `startHintTrack: "<id>"`. This is a **presentation-only field, intentionally
  outside the 7 story effects** (it mutates no story state), which keeps the effect set
  closed. The do-it-yourself player still completes the objective silently and never sees
  hints.
- Completing the bound objective auto-dismisses its track.

### Legacy tutorial disposition
`StartCabinSpawnPoint` / `TutorialManager` `[EDIT]`: **no force-start.** Spawn sets
StoryDirector to `ColdOpen` instead of calling `BeginTutorial()`. The gate stays fully
unlocked; `LockAll` is never called on the new path. `TutorialManager` remains in the
codebase (inert) so existing save apply/load stays harmless; its step text is the source
material for the hint-track JSON.

---

## 8. The opening flow `[AUTHOR]` + `[BUILD]`

- **Step 0 — Cold open:** spawn free in the cabin, no gate, AI silent but for existing
  ambient pings. The deliberate "okay… what now?" beat.
- **Discoverability buzz `[BUILD]`:** after ~30–60 s and/or some movement, **one** phone
  notification ping — diegetic "check the phone," no popup.
- **Step 1 — First contact `[AUTHOR]`:** opening AI node briefs the mission and unlocks
  *"How do I get clean water?"* / *"How do I find food?"*. The node **branches on the silent
  flags** (`hasWater`/`hasFood`) so a player who already did it isn't told to do it again.
- **Gate 1:** `hasWater && hasFood` → AI acknowledges → `AdvanceStory(NeedsShelter)`.
- **Step 2 — Shelter `[AUTHOR]`/`[BUILD]`:** AI prompts a base; `OnShelterBuilt` →
  `AdvanceStory(Explore)`.
- **Step 3 — Explore `[AUTHOR]`/`[BUILD]`:** AI points at the village; `OnVillageReached`
  → `AdvanceStory(VillageSeam)`.
- **Step 4 — Village seam:** fires + logs today; this is where Tev's thread bolts on later.

### Diegetic gating `[AUTHOR]`
Gate reasons must be in-fiction, not gamey ("You won't last the trip if you can't keep
yourself alive — get on your feet first"). Authored lines are placeholder-but-playable;
flagged for the human pass (GDD §6).

---

## 9. File manifest

### New `[BUILD]`
- `Assets/3 - Scripts/Story/StoryDirector.cs`
- `Assets/3 - Scripts/Story/DialogueData.cs` (schema types)
- `Assets/3 - Scripts/Story/DialogueRunner.cs`
- `Assets/3 - Scripts/Story/DialoguePresenter.cs` (interface) + `PhoneDialoguePresenter.cs`
- `Assets/3 - Scripts/Story/DialogueReplyColumn.cs`
- `Assets/3 - Scripts/Story/StoryObjective.cs` (+ objective manager, may fold into StoryDirector)
- `Assets/3 - Scripts/Story/HintTrack.cs` + `HintTrackRunner.cs`
- `Assets/3 - Scripts/Story/VillageReachTrigger.cs`
- `StreamingAssets/Story/*.json` — opening conversation, objectives, hint tracks `[AUTHOR]`

### Edits `[EDIT]`
- `ResourceManager.cs` — add `OnCleanWaterDrunk`, fire in `DrinkWater()`
- `SaveData.cs` — add `StoryDirectorSave` + root field
- `SaveCollector.cs` — `CaptureStoryDirector` / `ApplyStoryDirector` (step 6)
- `NewGameReset.cs` — reset StoryDirector
- `MainMenuController.cs` — seed StoryDirector
- `StartCabinSpawnPoint.cs` / `TutorialManager.cs` — no force-start; gate stays unlocked
- `AIChatScreen.cs` — remove input row; host presenter + reply column; hook no-LLM branch

---

## 10. Test checklist (must pass before calling the chunk done)

- [ ] StoryDirector created + seeded; survives a death-reload with `currentStoryStep` intact.
- [ ] 2-node throwaway conversation runs and applies a `SetFlag`.
- [ ] All 7 effects implemented; `TriggerEnding` is a logged no-op.
- [ ] An objective completes silently from raw gameplay (no story authored).
- [ ] Phone: typed input gone; reply column shows current responses / unlocked questions.
- [ ] Hint track shows only on-ask and dismisses on objective completion.
- [ ] Discoverability buzz fires once.
- [ ] **Full opening completed BOTH ways** (ask-the-AI and ignore-the-AI) reaches the
      village in the same state (GDD §2 acceptance test).

---

## 11. Open risks

1. **Phone discoverability** — single point of failure; the once-only buzz mitigates it.
2. **Quests-page extensibility** — unverified; minimal-list fallback if not cleanly feedable.
3. **The AI's voice** — the ~dozen authored lines are the human-owned 20% (GDD §6);
   scaffolded with placeholder-but-playable text, flagged for tuning.
4. **Reply-column anchoring** across portrait/landscape rotation — locked to portrait,
   hidden in camera/landscape.
```
