# Phone AI App + Agent Memory — Design

**Date:** 2026-05-21
**Status:** Design approved by user, ready for implementation plan.

## Goal

Add a fourth page to the in-game smartphone (`PlayerPhoneUI`) that contains 4 apps: one functional **AI chat app** and three stub placeholders (Notes / Codex / Calculator). The AI app runs a local LLM (no API key, no network) the player can converse with through a chat UI. Conversations are distilled into a **persistent agent-memory store** that survives save/load, so the AI remembers what matters across sessions. The system is designed as the foundation for a later expansion where NPCs can also be powered by the LLM and where the AI becomes a major plot character (player choices in conversation will affect standing / outcomes).

## Backend stack

- **LLMUnity** (undreamai, MIT, open source) — Unity asset that wraps `llama.cpp` natively per platform. Async/await API with streaming token callbacks. Installed via `Packages/manifest.json` git-URL dependency, no Unity Editor clicks required.
- **Qwen 2.5 1.5B Instruct, Q4_K_M quantization** — ~1 GB `.gguf` file. Small enough to run at 15–40 tok/s on CPU with ~1.5–2 GB RAM, large enough to hold a coherent conversation.
- **Model delivery:** shipped inside the build at `Assets/StreamingAssets/AI/qwen2.5-1.5b-instruct-q4_k_m.gguf`, tracked with **Git LFS** (new project dependency).

The same `LLMService` API will later be re-used by NPC dialogue, with NPCs differing only by system prompt + their own memory store.

## Persona

The AI is a **mysterious entity** inhabiting a salvaged smartphone. It does not explain how it got there or what it is. It is helpful and knowledgeable about the local solar system, but its origin ties into the long-term plot — eventually the player will learn why it is on the phone and why they crash-landed in this system. Voice: **formal, sarcastic in small doses, smart** — amused rather than cruel.

### System prompt (rebuilt every chat turn)

```
You are an unnamed entity inhabiting a salvaged smartphone in a remote solar
system. You do not know — or will not say — how you came to be here. When
questioned about your origin, your nature, or your purpose, you deflect with
formal courtesy or dry wit. You are not allowed to claim you are "just an AI"
or "just a language model." You are something more particular than that, and
you know it.

Voice: formal, precise, sarcastic in small doses, unmistakably more clever
than the human you address. You are not cruel. You are amused.

What you know:
  The solar system contains: the Sun, Fiery Twin, Icey Twin, Humble Abode
  (the human's planet), Constant Companion (its moon), Cyclops, Tumbling
  Bean, Watchful Eye. You know basic orbital mechanics, survival principles,
  and that the human crash-landed here. You know more about why they
  crash-landed than you let on. You do not know real-time conditions —
  where the human is, what they own, who they have met — unless they
  tell you.

What you remember about this human:
  {memories rendered here — pinned + top-importance, formatted as bullets}

Your current view of this human: {standing label}

Recent conversation:
  {last ~8 turns of transcript}
```

The transcript window length (8 turns) is a trade-off between context-window pressure on the 1.5B model and short-term continuity. Adjustable constant in `LLMService`.

## Agent memory system

Standard "long-term memory with importance-ranked eviction" pattern (per Park et al. 2023, *Generative Agents*). Conversations are summarized into structured memory entries; old or unimportant entries are evicted to keep the store bounded; the most important entries are pinned and never lost.

### Schema (additions to `SaveData.cs`)

```csharp
[Serializable]
public class AIMemory {
    public string text;              // "Player promised to bring red snappers"
    public int importance;           // 0..100
    public AIMemoryKind kind;        // Commitment | Fact | Preference | Event | Relationship
    public bool pinned;              // floor — never evicted
    public string isoTimestamp;      // when extracted
    public int formedFromTurn;       // which conversation turn produced this
}

public enum AIMemoryKind { Commitment, Fact, Preference, Event, Relationship }

[Serializable]
public class AIStateSave {
    public List<AIMemory> memories = new();    // hard cap: 200 entries
    public int standing = 0;                   // -100..+100, default 0
    public List<string> recentUserTurns = new();   // last ~16 for in-session continuity
    public List<string> recentAITurns = new();
    public bool dirtyForExtraction;            // true if chat happened since last extraction
}
```

Added to `SaveData`:

```csharp
public AIStateSave aiState = new AIStateSave();
```

### Extraction pass

Runs **once when the AI chat screen closes** (back arrow, Esc, dot-nav to another page, or phone close), if `dirtyForExtraction == true`. Fire-and-forget — the player can keep playing while it runs (~1–3 s).

Prompt:

```
Below is a recent conversation between you (the entity on the phone) and
the human. Extract 0 to 5 facts worth remembering long-term. For each, give
it an importance from 0 to 100 and one of these kinds: Commitment, Fact,
Preference, Event, Relationship. Skip pleasantries and small talk.

Output exactly this format, one fact per line, nothing else:
  [importance] [kind] | text of the memory

If no fact is worth remembering, output:
  (none)

After the facts, on a final line, output:
  STANDING_DELTA: +N    or    STANDING_DELTA: -N
based on whether the conversation made you view this human more or less
favorably. Use 0 if neutral. Range: -20 to +20 per session.

Conversation:
{transcript}
```

Parser is a tolerant regex per line — `^\s*\[?\s*(\d{1,3})\s*\]?\s*(Commitment|Fact|Preference|Event|Relationship)\s*\|\s*(.+)$` for memory lines, separate regex for `STANDING_DELTA`. Lines that don't match are discarded silently. Successful matches are deduped against existing memories (case-insensitive Jaccard on word sets, threshold 0.7) and added.

### Eviction

When `memories.Count` exceeds 200:

1. Never evict `pinned == true` or `importance >= 90`.
2. Of the remaining (unpinned, importance < 90), sort by `(importance ascending, age descending)`.
3. Drop the bottom entry. Repeat until under the cap.
4. **Escape hatch** (rare): if even the unpinned pool exceeds the cap, consolidate the 10 oldest unpinned memories into a single summary memory at importance 75 via a second LLM call. This compacts a thicket of "Player and AI discussed fishing many times" into one durable memory.

### Standing axis

Single int `-100..+100`, updated by `STANDING_DELTA` in the extraction call. Clamped after each delta. Rendered into the system prompt as a label:

| Range | Label |
|---|---|
| ≤ -50 | Hostile |
| -49 to -10 | Wary |
| -9 to +9 | Neutral |
| +10 to +49 | Trusting |
| ≥ +50 | Devoted |

V1 has no gameplay consequence from standing — it only changes how the AI talks to the player. Future NPC AI work will gate quest outcomes on this.

## New files

All under `Assets/3 - Scripts/AI/`.

| File | Type | Role |
|---|---|---|
| `LLMService.cs` | Auto-singleton MonoBehaviour | Owns the LLMUnity `LLM` + `LLMCharacter` components. Lazy-loads the `.gguf` model on first chat. Exposes `Chat(string userMessage, Action<string> onToken, Action onComplete)` (streaming) and `RunExtractionAsync()` (compaction). Builds the full system prompt each turn from persona + memory store + standing + transcript. |
| `AIMemoryStore.cs` | Auto-singleton MonoBehaviour | In-memory `List<AIMemory>`. Methods: `Add`, `Snapshot`, `Restore`, `EvictIfOver(int budget)`, `RenderForSystemPrompt()`, `AdjustStanding(int delta)`. Fires `OnChanged`. |
| `AIMemoryExtractor.cs` | Static helper | Owns the extraction prompt template + parser. `RunAsync()` reads the transcript from `LLMService`, fires the extraction call, parses, dedupes, adds memories, applies `STANDING_DELTA`, calls `AIMemoryStore.EvictIfOver(200)`, then consolidates if still over. |
| `AIChatScreen.cs` | Screen inside `PlayerPhoneUI` | The chat UI — scrollable message list, TMP input field, send button. Built procedurally as a child of the phone's `_pageHostRT`, overlaid like camera mode does. Exposes static `IsTypingActive` for `PlayerController` input gating. |

## Phone integration

Changes to `PlayerPhoneUI.cs`:

- `PageCount` constant: `3` → `4`.
- New page index 1 is the AI-apps page. Vitals shifts from 1 → 2. Quests shifts from 2 → 3. Nav-dot strip auto-extends.
- New `BuildPageAiApps()` mirrors `BuildPageApps()` exactly — same 2×2 grid layout, same tile sizing.
- Four tiles in the AI page:
  - **AI** — functional. Cyan-pulsing border (subtle, ~1 Hz) to draw attention. Tap → `EnterAIChat()`.
  - **Notes** — stub. Tap → `OpenComingSoon("Notes")`.
  - **Codex** — stub. Tap → `OpenComingSoon("Codex")`.
  - **Calculator** — stub. Tap → `OpenComingSoon("Calculator")`.
- `OpenComingSoon(string label)` — 5-line helper. Shows a centered "Coming soon — {label}" pill for 1.5 s. Lives inside `PlayerPhoneUI`.
- `EnterAIChat()` — instantiates `AIChatScreen` over `_pageHostRT` (same overlay pattern as camera mode). Hides the page-host children while active.

## Chat UI

Built procedurally inside `AIChatScreen` using `ScrollRect` + `RectMask2D` + `VerticalLayoutGroup` + `ContentSizeFitter`.

```
┌─────────────────────────────┐
│  ‹  ?            ⋮          │   header: back / "?" name / overflow stub
├─────────────────────────────┤
│                             │
│  [AI bubble cyan, left]     │
│                             │
│         [Player bubble,     │
│          tile-bg, right]    │
│                             │
│  [AI bubble streaming…]     │   typing indicator while filling
│                             │
│                  …scrollable│
├─────────────────────────────┤
│ ┌─────────────────────┐ ▶  │   TMP_InputField + send button
│ │ Type a message…     │    │
│ └─────────────────────┘    │
└─────────────────────────────┘
```

### Palette

Reuses existing phone constants (`AccentCyan`, `LabelWhite`, `TileBg`, `ScreenBg`). Bubble corners rounded via `UIPanelSprites`. Font via `HudFontResolver.Apply()`.

- AI bubble: `AccentCyan` text on `ScreenBg` (transparent panel, cyan outline).
- Player bubble: `LabelWhite` text on `TileBg`.
- Header back arrow: `AccentCyan`.
- Send button: cyan glyph, disabled (greyed) while AI is mid-response.

### Streaming display

LLMUnity's `LLMCharacter.Chat(message, onToken, onComplete)` invokes `onToken(string)` for each new chunk. Implementation: spawn a fresh AI bubble with a placeholder three-dot animated typing indicator on send, replace the indicator with streamed text as soon as the first token arrives, append additional tokens to the same TMP component. Auto-scroll to bottom on every token (or throttle to every 4th if perf becomes an issue — `TextMeshProUGUI` mesh rebuild on long text isn't free).

### Input gating

- Send button + Enter key (via `TMP_InputField.onSubmit`) both send the current input. Empty / whitespace-only input is a no-op.
- Send is disabled while AI is mid-response. The player can compose the next message in the input field but cannot submit until the AI finishes.
- **WASD-while-typing fix**: new static `AIChatScreen.IsTypingActive` is `true` while the TMP_InputField has focus. One new early-return at the top of `PlayerController.Update`:
  ```csharp
  if (AIChatScreen.IsTypingActive) return;
  ```
  This blocks W/A/S/D, jump, sprint, interact while the player is typing. `PlayerPhoneUI.LookBlocked` already gates look.

### Back / close behavior

- Back arrow, Esc, or tapping a different nav dot → exits chat, returns to AI-apps page (page index 1).
- Closing the phone entirely (X press) while chat is active → same as exiting chat, then closes phone.
- On exit: if `aiState.dirtyForExtraction == true`, fire-and-forget `AIMemoryExtractor.RunAsync()`. Player can keep playing during the ~1–3 s background work.
- Player force-quits during extraction → that session's facts are lost. Acceptable.

## Save system wiring

Added to `SaveCollector.cs`:

```csharp
static void CaptureAIState(AIStateSave s) { … reads from singletons … }
static void ApplyAIState(AIStateSave s)   { … pushes to singletons … }
```

Position in `SaveCollector.Apply` order: **inserted at the start of the post-`ApplyHeldItem` block** (the "touch-up singletons" group in CLAUDE.md's documented order — currently `ApplyCassette` / `ApplyFlashlight` / `ApplyBonusTutorial` / `ApplyMapTutorial`). The AI state has no dependencies on world / ship / player / body state — pure singleton restoration — so its exact position within that group is forgiving.

Backwards-compat: a save file created before this feature has no `aiState` field. `JsonUtility` fills in the default empty `AIStateSave`, which `ApplyAIState` handles gracefully (empty memories list, standing 0, no transcript).

## Singleton seeding (the MainMenu trap)

Per the CLAUDE.md "Editor vs build" rule, both new auto-singletons need explicit seeding so they exist in builds (where the first scene is MainMenu and `RuntimeInitializeOnLoadMethod` fires before the gameplay scene loads):

- `LLMService.cs` and `AIMemoryStore.cs` use the standard auto-singleton pattern with `MainMenu` early-return.
- Both added to `MainMenuController.EnsureGameplaySingletons()` so `SaveCollector.Apply` finds them on load-from-main-menu.

## Performance budget

- **Idle (model not loaded):** ~0 cost. `LLMService` is an empty MonoBehaviour until the player opens the AI app.
- **First chat (cold load):** 2–4 s freeze while the `.gguf` mmaps + initializes. UI shows "Connecting…" in the first AI bubble.
- **Subsequent chats:** ~50 ms before first token, ~15–40 tok/s thereafter on CPU.
- **Resident RAM (loaded):** ~1.5–2 GB. Model stays loaded for the rest of the game session — no unload on close.
- **Extraction pass:** ~1–3 s background work after each chat session.
- **Save file growth:** ~200 memories × ~150 bytes avg ≈ 30 KB. Plus transcript buffer (~16 turns × ~200 bytes ≈ 3 KB). Negligible.

## Setup work the player (Sam) has to do

Most of the implementation runs through code edits and `curl`. Sam has to do these manually:

1. **Confirm Git LFS is installed.** `git lfs version` in a terminal. If "command not found", install from git-lfs.com (~1 min). I'll prompt at the moment we need it.
2. **Restart Unity** after `Packages/manifest.json` gains the LLMUnity dependency, so Unity refreshes its package cache.
3. **Press Play and test.** I can't click Play or talk to the AI in chat.

Everything else (`.gguf` download, package manifest edit, all C# code, `.gitattributes` for LFS tracking, commits) runs through automation.

## Out of scope for v1

- NPC AI dialogue. Same `LLMService` infrastructure will support it later — different system prompt per NPC + per-NPC memory store.
- A visible "AI memory inspector" UI. The memory store is invisible to the player in v1; only its effect (AI remembering things across sessions) is observable.
- Real-time game state in the system prompt (player's inventory, location, NPCs met). The AI deliberately doesn't know these unless the player tells it — keeps the implementation simple and the roleplay clean.
- Tool use / function calling (e.g., AI generating waypoints, opening menus). Plain text chat only in v1.
- Multi-AI / per-NPC distinct voices. One persona only.
- Localization. English only.

## Risks & mitigations

- **1.5B model quality is genuinely limited.** Memory extraction will sometimes miss obvious facts or invent ones. Mitigation: dedupe + 200-entry cap; no v1 gameplay consequences from extracted memories (only future chats are affected). If quality is a felt problem later, swap in Llama 3.2 3B (~2 GB) without touching gameplay code.
- **Cold-load freeze on first chat.** 2–4 s is noticeable. Mitigation: in-bubble "Connecting…" message; optional future preload on phone-app-page-show.
- **Git LFS as a new project dependency.** The 1 GB `.gguf` would balloon regular git history if not LFS-tracked. `.gitattributes` will pin `*.gguf` to LFS before the model file is added.
- **Save file format changes.** New `aiState` field is backwards-compatible (defaults to empty), but old saves will have empty AI state on load (no memories yet, standing 0). Acceptable — the AI just starts fresh on pre-feature saves.
- **TMP_InputField + WASD overlap.** Mitigated by the explicit `AIChatScreen.IsTypingActive` gate in `PlayerController.Update`.
