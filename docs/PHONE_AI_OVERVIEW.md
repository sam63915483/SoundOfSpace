# Phone AI App — As-Built Overview

A complete technical write-up of the in-game phone AI chat feature shipped to this Unity 2022.3 project. Written for use as a brainstorming input — paste this into a different LLM and ask for design alternatives, alternative architectures, scaling ideas, etc.

---

## 1. What the feature is

The player has a diegetic smartphone they pull up by pressing **X**. The phone has four swipeable pages — Apps, **AI Apps** (new), Vitals, Quests. On the AI Apps page there are four tiles: one functional **AI** tile and three "Coming soon" stubs (Notes / Codex / Calculator).

Tapping the AI tile opens a chat screen that overlays the entire phone screen. The player types messages and gets streamed responses from a local LLM (Qwen 2.5 1.5B Instruct, Q4_K_M quantization) running on-device via LLMUnity (a wrapper around llama.cpp).

The AI has a persistent **agent memory store** with importance-ranked eviction — after each chat session, the conversation is distilled into a bounded list of `AIMemory` entries that survive save/load. The AI's view of the player ("standing" axis, -100 to +100) also persists. The next chat fold both into the system prompt so the AI remembers what mattered.

### Persona

The AI is a **mysterious entity** inhabiting a salvaged smartphone. It does not explain its origin. Voice: formal, dry, sarcastic in small doses, smart. It knows the local solar system's eight bodies by name but does not know real-time game state — only what the player tells it. The long-term plot is "who/what is this thing and why is it on your phone."

---

## 2. Architecture at a glance

```
                    ┌──────────────────────────────┐
                    │   PlayerPhoneUI (existing)   │
                    │   ─ X opens phone            │
                    │   ─ Page nav (4 pages)       │
                    │   ─ EnterAIChat() spawns ─┐  │
                    └──────────────────────────┼──┘
                                               │
                                               ▼
                    ┌──────────────────────────────┐
                    │  AIChatScreen (MonoBehaviour)│   procedural UI built at runtime
                    │   ─ Bubbles, input field     │   overlays _screenRT full-rect
                    │   ─ Custom caret             │
                    │   ─ IsTypingActive (static)  │   ◄── global input gate
                    │   ─ Calls LLMService.Chat()  │
                    │   ─ RecordTurn on complete   │
                    │   ─ RunAsync extractor on    │
                    │     close                    │
                    └──────────────────────────────┘
                                ▲           │
                                │           ▼
                ┌───────────────┴───┐   ┌──────────────────────────┐
                │  AIMemoryStore    │   │  LLMService              │
                │   (singleton)     │   │   (singleton)            │
                │   ─ memories[]    │   │   ─ Owns LLM + LLMAgent  │
                │   ─ standing      │   │   ─ Lazy model load      │
                │   ─ recentTurns   │   │   ─ Chat(msg, onTok,     │
                │   ─ Snapshot/     │   │     onComplete)          │
                │     Restore       │   │   ─ OneShotAsync(prompt) │
                └───────────────────┘   │   ─ BuildSystemPrompt    │
                        ▲               └──────────────────────────┘
                        │                            ▲
                        │                            │
                        │               ┌────────────┴───────────┐
                        │               │  AIMemoryExtractor     │
                        │               │   (static helper)      │
                        └───────────────┤   ─ RunAsync on close  │
                                        │   ─ One-shot LLM call  │
                                        │   ─ Tolerant regex     │
                                        │   ─ Dedupes + adds     │
                                        │   ─ Standing delta     │
                                        └────────────────────────┘

                  Save/Load
                  ─────────
                  SaveData.aiState ←→ AIStateSave (List<AIMemory> + standing + recent turns)
                  SaveCollector.CaptureAIState / ApplyAIState
```

### File inventory

All new files under `Assets/3 - Scripts/AI/`:

| File | Role |
|---|---|
| `LLMService.cs` | Auto-singleton MonoBehaviour. Owns `LLM` + `LLMAgent` LLMUnity components, lazy-loads the model on first chat, exposes `Chat(msg, onToken, onComplete)` and `OneShotAsync(prompt) → Task<string>`. Rebuilds the system prompt per turn from memory + standing + transcript. |
| `AIMemoryStore.cs` | Auto-singleton MonoBehaviour. In-memory `List<AIMemory>` (cap 200), standing axis (-100..+100), rolling 16-turn transcript buffer. Importance-ranked eviction with pinned + ≥90-importance floor. Jaccard-similarity dedupe on add. |
| `AIMemoryExtractor.cs` | Static helper. `RunAsync()` runs the post-conversation compaction LLM call, parses a structured response (`[importance] kind | text`), dedupes against existing memories, applies a `STANDING_DELTA`, optionally runs a consolidation pass. |
| `AIChatScreen.cs` | MonoBehaviour. Procedural chat UI built at runtime as a child of `PlayerPhoneUI._screenRT`. Bubbles, input field, custom blinking caret, multi-line input growth, scroll, focus-state management. Owns the single static gate `IsTypingActive`. |

Modified files:

| File | Change |
|---|---|
| `Assets/3 - Scripts/SaveSystem/SaveData.cs` | Added `AIMemory`, `AIMemoryKind` enum, `AIStateSave` class, `aiState` field. |
| `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` | Added `CaptureAIState` + `ApplyAIState` helpers wired into the save/load sequence. |
| `Assets/3 - Scripts/UI/MainMenuController.cs` | Seeded `AIMemoryStore`, `LLMService`, and `PlayerPhoneUI` in `EnsureGameplaySingletons` (per the CLAUDE.md MainMenu trap). |
| `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` | Page count 3→4, new AI Apps page slot, `EnterAIChat()` spawns `AIChatScreen`, full input gate when `IsTypingActive`. |
| `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` | Early-return on `IsTypingActive`. |
| `Assets/3 - Scripts/Player/PlayerFlashlight.cs` | Gated E-key on `IsTypingActive`. |
| `Assets/3 - Scripts/Camera/CameraTransformFX.cs` | WASD axis reads return 0 when typing (headbob/strafe-tilt). |
| `Assets/3 - Scripts/Camera/CameraFOVFX.cs` | Same gate for sprint-FOV. |
| `Assets/3 - Scripts/Map/SolarSystemMapController.cs` | M-key gated. |
| `Assets/3 - Scripts/Building/BuildMenuUI.cs` | N-key gated. |
| `Assets/3 - Scripts/Building/GhostPlacement.cs` | R-rotate gated. |
| `Assets/3 - Scripts/Fishing/FishingdexManager.cs` | B-key gated. |
| `Assets/3 - Scripts/UI/NoteReadUI.cs` | Tab dismiss gated. |
| `Assets/3 - Scripts/Vendor/ShipMarketShopUI.cs` | F-close gated. |
| `Assets/3 - Scripts/Vendor/GoodsVendorShopUI.cs` | Esc-close gated. |
| `Assets/3 - Scripts/UI/TabbedPauseMenu.cs` | Pause-menu Esc gated. |

Asset:

| Path | Notes |
|---|---|
| `Assets/StreamingAssets/AI/qwen2.5-1.5b-instruct-q4_k_m.gguf` | ~1.04 GB GGUF. Tracked via Git LFS (`.gitattributes` has `*.gguf filter=lfs diff=lfs merge=lfs -text`). |
| `Packages/manifest.json` | Added `"ai.undream.llm": "https://github.com/undreamai/LLMUnity.git"` dependency. |

---

## 3. Data flow — one message round-trip

1. Player presses X → `PlayerPhoneUI.Toggle()` slides phone up.
2. Player navigates to AI Apps page, taps the AI tile → `PlayerPhoneUI.EnterAIChat()`.
3. `EnterAIChat` creates a `GameObject("AIChatScreen")` as a child of `_screenRT` with `LayoutElement.ignoreLayout = true` so it overlays the entire phone screen. Adds the `AIChatScreen` MonoBehaviour. Calls `Init(OnChatExit)`.
4. `AIChatScreen.Init` builds UI (header, scroll viewport, input row) on its own GameObject, then restores any prior session's transcript via `AIMemoryStore.RecentUserTurns/RecentAITurns`.
5. Player clicks input field → TMP_InputField fires `onSelect` → `IsTypingActive = true`, placeholder hidden, custom caret begins blinking.
6. Player types — text goes into the input field. `ResizeInputRowToFit()` runs in Update, growing the row upward as text wraps.
7. Player presses Enter → `OnSendClicked()` fires (also wired to send-button click).
8. `OnSendClicked` pushes a user bubble, clears input, spawns an empty AI bubble with a "typing dots" animation, kicks off `LLMService.Chat(msg, onToken, onComplete)`, and starts a `ReactivateInputNextFrame` coroutine to keep the field focused for the next message.
9. `LLMService.Chat` (first call only) lazy-loads the model:
   - Creates `LLM_Runtime` GameObject **inactive**, adds `LLM` component, calls `SetModel("AI/qwen2.5-1.5b-instruct-q4_k_m.gguf")`, then `SetActive(true)`. (Critical: the LLM component's `Awake` validates the model path immediately on enable — setting the model **after** AddComponent would fail.)
   - Same trick for `LLMAgent_PhoneAI`.
   - Awaits `_llm.WaitUntilReady()` — first time, blocks ~5–30 s while model mmaps into RAM (~1.5 GB resident).
10. Once ready, `LLMService` builds the system prompt:
    - Persona block
    - "What you know" (solar system + lore)
    - "What you remember about this human:" + `AIMemoryStore.RenderForSystemPrompt(30)` — top 30 memories pinned-first, importance-desc
    - "Your current view of this human: {StandingLabel}"
    - "Recent conversation:" + last 8 turns
11. `LLMService._agent.systemPrompt = prompt` (rebuilt per turn), then `await _agent.Chat(userMessage, tok => onToken(tok), () => {})`.
12. LLMUnity's `Chat` callback fires with the **cumulative response so far** on each token. `AIChatScreen.OnSendClicked` mirrors the cumulative string into the AI bubble's text (does NOT append — important: appending was producing exponentially-repeating garbage).
13. When `Chat` returns the full response, `onComplete` fires. `AIChatScreen` records the turn into `AIMemoryStore.RecordTurn(userMsg, aiReply)`.
14. Player closes the chat (back arrow, Esc, dot-nav, or phone close) → `AIChatScreen.Exit()`:
    - Clears `IsTypingActive`, stops coroutines
    - If `AIMemoryStore.DirtyForExtraction`, fires-and-forgets `AIMemoryExtractor.RunAsync()` — runs in background, player can keep playing
    - Calls `_onExitCallback` (which restores the page-host pages in `PlayerPhoneUI.OnChatExit`)
    - `Destroy(gameObject)` — chat UI is fully torn down

### Extraction pass (post-close)

`AIMemoryExtractor.RunAsync`:
1. Reads `AIMemoryStore.RecentUserTurns/RecentAITurns`, renders as a transcript.
2. Builds an extraction prompt: "Extract 0 to 5 facts worth remembering. Format: `[importance] kind | text`. Append `STANDING_DELTA: +N`."
3. Calls `LLMService.OneShotAsync(extractionPrompt)` (which uses `addToHistory: false` so it doesn't pollute the chat's LLMAgent context).
4. Parses each line with a tolerant regex. Importance clamped to [0,100]; kind defaults to `Fact` if unparseable; empty text skipped.
5. Each parsed memory deduped against existing via Jaccard similarity on word sets (threshold 0.7). New ones added; duplicates with higher importance replace existing.
6. Standing delta clamped to [-20, 20], applied via `AdjustStanding` (which clamps cumulative standing to [-100, 100]).
7. `MarkExtracted()` clears the dirty flag.
8. If the store has overflowed the budget AND all entries are floor-protected (pinned or importance ≥ 90), runs a `ConsolidateAsync` pass: takes the 10 oldest unpinned <90 memories, asks the LLM to summarize them into one importance-75 memory.

---

## 4. Memory system in detail

### Schema

```csharp
[Serializable] class AIMemory {
    public string text;             // e.g. "Player promised to bring red snappers"
    public int    importance;       // 0..100
    public AIMemoryKind kind;       // Commitment | Fact | Preference | Event | Relationship
    public bool   pinned;           // floor — never evicted
    public string isoTimestamp;
    public int    formedFromTurn;
}

[Serializable] class AIStateSave {
    public List<AIMemory> memories;          // hard cap: 200
    public int    standing;                  // -100..+100
    public List<string> recentUserTurns;     // last 16 raw user lines
    public List<string> recentAITurns;       // last 16 raw AI lines
    public bool   dirtyForExtraction;
    public int    totalTurns;
}
```

### Eviction algorithm

When `memories.Count > 200` after an `AddMemory` call:
1. Never evict entries with `pinned == true` OR `importance >= 90`.
2. From the rest, sort by `(importance asc, isoTimestamp asc)` — lowest-importance oldest first.
3. Drop the bottom entries until under the cap.
4. **Floor overflow escape hatch**: if even the unpinned `<90` pool exceeds the cap, the extractor runs `ConsolidateAsync` to collapse 10 oldest into one summary memory at importance 75. This is rare — it only happens when the player has had many high-stakes conversations.

### Standing → label mapping

| Range | Label rendered in system prompt |
|---|---|
| ≤ -50 | Hostile |
| -49..-10 | Wary |
| -9..+9 | Neutral |
| +10..+49 | Trusting |
| ≥ +50 | Devoted |

V1 has no gameplay consequence — only changes how the AI talks. Designed so future NPC AI can gate quest outcomes on it.

---

## 5. Save/load

`SaveData.aiState` is captured/applied in `SaveCollector` alongside the existing systems. `ApplyAIState` runs in the "touch-up singletons" group (after `ApplyHeldItem`, alongside `ApplyCassette`/`ApplyFlashlight`/etc.) since it has no dependencies on world state.

Backwards compatibility: pre-feature saves have no `aiState` key in JSON. `JsonUtility` fills it with the default empty `AIStateSave`. `AIMemoryStore.Restore(null)` and `Restore(emptyState)` both behave correctly — load proceeds with a blank AI memory.

Per the CLAUDE.md "MainMenu trap": both `AIMemoryStore` and `LLMService` use the standard auto-singleton pattern with a `MainMenu` early-return, AND are explicitly seeded in `MainMenuController.EnsureGameplaySingletons()` so they exist before `SaveCollector.Apply` runs on load.

---

## 6. The input-gating saga (worth flagging for redesign)

The biggest source of bugs in this feature was input leakage — while the player was typing in the chat input field, every conceivable input handler in the game tried to interpret typed letters as gameplay commands:

| Letter | Originally did |
|---|---|
| W A S D | Move the player + activate head-bob, strafe-tilt, sprint-FOV |
| Space | Jump |
| Shift | Sprint |
| E | Toggle flashlight |
| F | Close shops / pick up items |
| R | Rotate building ghost / rotate phone landscape |
| M | Open solar-system map |
| N | Open build menu |
| B | Open fishingdex |
| C | Open phone camera |
| X | Close the phone (!) |
| Esc | Open pause menu underneath chat |
| Tab | Dismiss read-note panel |
| 1-5 | Equip hotbar items |
| Enter | (Wanted — sends the message) |

The fix was a **single static gate** `AIChatScreen.IsTypingActive`, plus an early-return `if (AIChatScreen.IsTypingActive) return;` at the top of every offending handler's `Update`/`LateUpdate`. For axis-based effects (head-bob, FOV), we replaced `Input.GetAxisRaw("Horizontal")` with `typing ? 0f : Input.GetAxisRaw("Horizontal")`.

**Smell:** this required touching ~13 unrelated files. A cleaner architecture would have been a project-wide "input gate" abstraction that everyone consults by default. But retrofitting that into an existing codebase with 50+ scripts reading `Input.GetKey` directly would have been a much larger change.

For the brainstorm: **think about how a from-scratch design could centralize input gating.** Options include:
- Wrapping all input reads in a `GameInput` static (like `TutorialGate` already does for tutorial-locked abilities)
- Using Unity's new Input System with InputActions you can enable/disable as a group
- An event-driven design where the chat input "claims" the keyboard via an `IInputCapture` interface

---

## 7. LLMUnity-specific quirks (lessons learned)

LLMUnity is a Unity asset wrapping llama.cpp. It works, but a few non-obvious things bit us:

1. **`LLM.Awake` validates the model path synchronously.** If you call `AddComponent<LLM>()` then `llm.SetModel(path)`, Awake has already validated the empty model field and set `failed = true`. Subsequent `WaitUntilReady()` throws "LLM failed to start". **Fix**: create the GameObject `inactive`, AddComponent, SetModel, then `SetActive(true)` so Awake runs with a valid model.

2. **`LLMAgent.Chat` callback delivers cumulative text, not deltas.** Each callback invocation passes the FULL response so far. A naive `accumulator.Append(token)` produces exponentially-growing garbage (`"II'M I'M QUITE I'M QUITE THE…"`). **Fix**: mirror the latest cumulative string into the bubble (no append).

3. **`LLMCharacter` is deprecated in v3.** Use `LLMAgent` instead. Its API: `systemPrompt` (not `prompt`), `Chat(message, onPartial, onComplete) → Task<string>` (the return value IS the full response), `addToHistory` bool param (set false for one-shot calls like the extractor so they don't pollute conversation history).

4. **First chat cold-load is 5–30 seconds.** Subsequent chats start streaming within ~500 ms. RAM resident is ~1.5–2 GB. Players who never open the AI app pay zero cost (model is lazy-loaded on first call).

5. **No native streaming-token-by-token API.** LLMUnity does deliver via the callback, but token chunks are typically multi-character (model dependent — Qwen often emits 2–5 chars per chunk).

6. **LlamaLib native binary is auto-downloaded on first project import.** ~30 MB extracted to `%AppData%/Roaming/LLMUnity/cache/`. Don't ship without first opening the project once and letting it download.

---

## 8. TMP_InputField caret saga (also worth flagging)

The placeholder/text field's blinking cursor was the single hardest visual bug. TMP_InputField creates its caret programmatically on first focus, parents it as the **first sibling** of the text component's parent — which puts it **under** everything we draw (placeholder, text). The default caret is 1px wide in transparent white, invisible against dark backgrounds.

We tried: explicit `caretWidth`, `customCaretColor`, `caretColor`, `caretBlinkRate`. None worked reliably — caret was either invisible or hidden behind the placeholder.

**Final fix**: replaced TMP's caret entirely with our own. A 2px wide cyan `Image` parented to TextArea, anchored top-left, positioned each frame via `TMP.textInfo.lineInfo[lastLine].length` for the X coordinate. Blinks via `Time.unscaledDeltaTime` modulo timer. Height = `fontSize * 0.8` to match letter cap-height.

For the brainstorm: **TMP_InputField has many sharp edges in procedurally-built UIs.** A from-scratch UI might use a custom input field component or just always pair TMP_InputField with a fixed prefab template.

---

## 9. Performance budget

| Resource | Cost |
|---|---|
| Idle (model not loaded) | ~0 |
| First-chat cold-load freeze | 5–30 s, single time per launch |
| Resident RAM once loaded | ~1.5–2 GB |
| Subsequent chat first-token latency | ~50–500 ms |
| Streaming throughput on CPU | 15–40 tokens/sec |
| Background extraction call (post-close) | ~1–3 s, doesn't block player |
| Save file growth | ~30 KB worst case (200 × ~150 B memories + transcript) |
| Build size impact | +1.04 GB (model GGUF) + ~30 MB (LlamaLib native) |

---

## 10. Known issues and limitations

1. **First-chat cold-load is visible.** The player waits 5–30 s seeing only a "..." typing indicator with no "loading model" message. Could be improved with an explicit progress bar.
2. **Qwen 2.5 1.5B is small.** Responses are coherent but not deep. Roleplay can be inconsistent (the AI sometimes breaks the "mysterious entity" persona and admits to being an AI despite the system prompt instructions).
3. **Memory extraction quality varies.** The 1.5B model isn't great at the structured format — many extracted lines are dropped by the tolerant regex. We accept this because the alternative is dependency on a larger model.
4. **No streaming visual smoothing.** Tokens arrive in chunks of 2–5 chars; the bubble text "jumps" rather than appearing character-by-character.
5. **Standing axis has no gameplay consequence yet.** Only changes how the AI talks. Wiring it to quest gates is on the roadmap.
6. **No "AI is thinking" indicator beyond the typing dots.** Players might wonder if it's stuck.
7. **No abort/cancel on a long response.** Once `Chat` is awaited, you wait for it.
8. **The 13-file input-gating retrofit is fragile.** Any new input handler added to the project must also consult `AIChatScreen.IsTypingActive`, or it'll leak input through the chat.
9. **Chat is single-thread to the LLM.** Two simultaneous chats (e.g. AI app + future NPC AI) would queue or conflict.
10. **No multi-language / localization support.** English only.

---

## 11. Where this is going (roadmap hooks)

The spec was deliberately scoped to "AI chat app on the phone." The architecture was designed so the following can be added without rewriting:

- **NPC AI dialogue**: same `LLMService` infrastructure, different `systemPrompt` per NPC, separate `AIMemoryStore`s keyed by NPC ID.
- **Standing → quest gating**: NPCs can read `AIMemoryStore.Standing` to choose dialogue branches.
- **AI memory inspector UI**: dev tool to see what the AI remembers.
- **Player-visible AI app icons** (Notes, Codex, Calculator are stubs — wire them up later).
- **Voice synthesis**: pipe responses through a TTS like piper or Coqui for spoken AI.
- **RAG over game lore**: feed the LLM a vector store of lore documents.

---

## 12. Things I'd reconsider if starting from scratch

Open questions worth brainstorming on:

1. **Should the AI be cloud or local?** We chose local for offline + no API key + no recurring cost. Tradeoff: 1 GB build, 1.5 GB RAM, lower quality, cold-load wait. A cloud option (player provides their API key) would be smaller, faster, smarter — but adds friction and dependency.
2. **Is one shared `AIMemoryStore` right?** Currently one store serves the phone AI. Adding NPCs means multiple stores. Maybe a unified `AIMemorySystem` indexed by character ID is cleaner.
3. **Is the "mysterious entity" persona load-bearing?** A 1.5B model isn't great at consistent characterization. Maybe a simpler "helpful assistant" persona would yield cleaner gameplay.
4. **Is importance-ranked eviction the right algorithm?** Could try recency-weighted, semantic clustering, or even let the player manually pin memories.
5. **Should extraction be LLM-driven at all?** A heuristic extractor (regex for "I promise…", "my name is…", etc.) would be deterministic and free.
6. **Is the diegetic-phone framing the right UI?** Could be a heads-up display, a wristwatch, a radio earpiece. Each has different player-action affordances.
7. **The "standing" axis is fully derived from extracted memories.** Could be an explicit gameplay variable affected by player actions outside the chat.
8. **Input gating could be reframed entirely.** Instead of every system consulting a global gate, a focused UI could *claim* keyboard input via an event the rest of the game listens for.

---

## Reference: file paths to read for full context

If sharing this with another assistant who can read files:

- Design spec: `docs/superpowers/specs/2026-05-21-phone-ai-app-design.md`
- Implementation plan: `docs/superpowers/plans/2026-05-21-phone-ai-app-plan.md`
- Source files: `Assets/3 - Scripts/AI/{LLMService,AIMemoryStore,AIMemoryExtractor,AIChatScreen}.cs`
- Save schema: `Assets/3 - Scripts/SaveSystem/SaveData.cs` (search `AIMemory`, `AIStateSave`, `aiState`)
- Save wiring: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (search `CaptureAIState`, `ApplyAIState`)
- Phone host: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (search `EnterAIChat`, `BuildAIAppsPage`, `BuildAITile`)
- Project-wide conventions: `CLAUDE.md` at project root.

Commit range for the entire feature: `git log --oneline 361bbe2..HEAD` (everything after the last revert before this work started).
