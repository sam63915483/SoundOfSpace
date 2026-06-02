# Phone AI App + Agent Memory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fourth page to the in-game smartphone with a working AI chat app (Qwen 2.5 1.5B running locally via LLMUnity) backed by a persistent agent-memory store that distills conversations into importance-ranked memories surviving save/load.

**Architecture:** New `Assets/3 - Scripts/AI/` folder contains four new files: `LLMService` (auto-singleton, owns LLMUnity components, lazy-loads model), `AIMemoryStore` (auto-singleton, in-memory list of `AIMemory`), `AIMemoryExtractor` (static helper, runs the compaction LLM call), `AIChatScreen` (procedural chat UI built inside `PlayerPhoneUI._pageHostRT`). Edits to `PlayerPhoneUI` add a new page index between the apps page and the vitals page. Save schema gains an `AIStateSave` field round-tripped through `SaveCollector`. The model `.gguf` ships in `Assets/StreamingAssets/AI/`, tracked via Git LFS.

**Tech Stack:** Unity 2022.3, C# (default `Assembly-CSharp`, no `.asmdef`), LLMUnity (git-URL Unity package, wraps llama.cpp), Qwen 2.5 1.5B Instruct Q4_K_M GGUF (~1 GB), Git LFS.

**Spec:** [`docs/superpowers/specs/2026-05-21-phone-ai-app-design.md`](../specs/2026-05-21-phone-ai-app-design.md)

**Workflow note (Unity-specific):** This project has no test framework and no `.asmdef` files — everything compiles under default `Assembly-CSharp` and is tested manually in Play mode. The standard `pytest`/`jest` TDD step shape from the writing-plans skill doesn't apply. Each task instead uses: **(1)** code edit, **(2)** verify Unity compiles (check Console — Unity auto-recompiles on save), **(3)** manual Play-mode test where applicable, **(4)** commit. Step 2 ("verify compile") in Unity means: switch focus to Unity, wait for the bottom-bar spinner to finish, check the Console window for red errors. Yellow warnings are acceptable unless noted.

**CLAUDE.md cross-references** (the implementing agent should re-read CLAUDE.md if any are unfamiliar):
- "MainMenu singleton trap" — every new auto-singleton must also be seeded in `MainMenuController.EnsureGameplaySingletons`.
- "DO NOT TOUCH: Atmosphere & planet generation" — nothing in this plan touches that zone. If a step seems to require it, stop.
- "Auto-created singletons" pattern — `LLMService` and `AIMemoryStore` follow it (mirror `SpaceDustInventory.cs` exactly).
- "Save system → Adding a new system" — `AIStateSave` follows it.
- "Coding conventions" — singleton pattern, lazy-cached scene lookup, change-detection for per-frame text, `CompareTag`, `rb.MovePosition`, `DialogueTextStyling` for typewriter (not used here — LLMUnity streaming replaces it).

---

## Task 1: Environment setup — Git LFS, LLMUnity package, model download

**Files:**
- Create: `.gitattributes` (or modify if it exists)
- Create: `Assets/StreamingAssets/AI/.gitkeep`
- Create: `Assets/StreamingAssets/AI/qwen2.5-1.5b-instruct-q4_k_m.gguf` (downloaded via `curl`)
- Modify: `Packages/manifest.json`

**Manual-user steps required in this task:**
- Sam must run `git lfs version` once (or `git lfs install` if first time). Prompt if "command not found".
- Sam must close + reopen Unity after `Packages/manifest.json` changes so Unity refreshes the package cache.

---

- [ ] **Step 1.1: Confirm Git LFS is installed on the user's machine**

Run:
```powershell
git lfs version
```

Expected: A line like `git-lfs/3.x.x ...`.

If you get "command not found" or similar, **stop and prompt the user** with this text:
> "Git LFS isn't installed. Please install it from https://git-lfs.com (one-click installer, ~1 minute), then run `git lfs install` once in any terminal. Then tell me 'done' and I'll continue."

Do not proceed until Git LFS is present.

- [ ] **Step 1.2: Read or create `.gitattributes` and add LFS tracking for `.gguf`**

Check whether `.gitattributes` exists:
```powershell
Test-Path "C:\1 - Game 1.1\1aughhh1\.gitattributes"
```

If `False`: create a new file. If `True`: read it first and append.

The line to ensure exists (append if missing):
```
*.gguf filter=lfs diff=lfs merge=lfs -text
```

Verify by reading the file back and confirming the line is present.

- [ ] **Step 1.3: Create the `StreamingAssets/AI/` folder + a `.gitkeep`**

Run:
```powershell
New-Item -ItemType Directory -Force "C:\1 - Game 1.1\1aughhh1\Assets\StreamingAssets\AI"
New-Item -ItemType File -Force "C:\1 - Game 1.1\1aughhh1\Assets\StreamingAssets\AI\.gitkeep"
```

The `.gitkeep` is so the directory exists in git even before the `.gguf` is downloaded.

- [ ] **Step 1.4: Add the LLMUnity package to `Packages/manifest.json`**

Read `C:\1 - Game 1.1\1aughhh1\Packages\manifest.json`, then add one line to the `dependencies` object:

```json
"ai.undream.llm": "https://github.com/undreamai/LLMUnity.git"
```

Place it in alphabetical order among other `dependencies` (right after `com.coplaydev.coplay` works well). If you want a pinned version, append `#v3.0.3` (or whatever the current latest stable tag is per https://github.com/undreamai/LLMUnity/releases) — the un-pinned form follows `main` and is acceptable for an indie project.

Use `Edit` to make the change, not `Write` (the file has other dependencies we must preserve).

- [ ] **Step 1.5: Download the Qwen 2.5 1.5B Q4_K_M GGUF model into StreamingAssets**

Run (this is a ~1 GB download — give it a long timeout):
```powershell
curl.exe -L -o "C:\1 - Game 1.1\1aughhh1\Assets\StreamingAssets\AI\qwen2.5-1.5b-instruct-q4_k_m.gguf" "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf"
```

Pass `timeout: 600000` (10 minutes) on the Bash call.

Expected: a ~1.0 GB file at the destination. Verify:
```powershell
Get-Item "C:\1 - Game 1.1\1aughhh1\Assets\StreamingAssets\AI\qwen2.5-1.5b-instruct-q4_k_m.gguf" | Select-Object Length
```

The `Length` should be ≥ 900,000,000 bytes (~900 MB+). If the file is much smaller (e.g. a few KB), the download likely returned an HTML redirect error page — retry the command.

- [ ] **Step 1.6: Prompt the user to restart Unity**

Print to the user:
> "Unity needs to be restarted now so it picks up the new LLMUnity package from manifest.json. Please close Unity, reopen the project, wait for the import spinner (could be a minute or two — it's installing a native package), then tell me 'restarted' so I can verify the package resolved."

Wait for user confirmation.

- [ ] **Step 1.7: After user confirms restart, verify LLMUnity resolved**

Run:
```powershell
Test-Path "C:\1 - Game 1.1\1aughhh1\Library\PackageCache" -PathType Container
Get-ChildItem "C:\1 - Game 1.1\1aughhh1\Library\PackageCache" -Filter "ai.undream.llmunity*" -ErrorAction SilentlyContinue
```

Expected: at least one directory matching the pattern. If not present, the package did not resolve — ask the user to check Unity's Console for package errors.

- [ ] **Step 1.8: Commit**

```powershell
git add .gitattributes "Assets/StreamingAssets/AI/.gitkeep" "Assets/StreamingAssets/AI/qwen2.5-1.5b-instruct-q4_k_m.gguf" "Packages/manifest.json" "Packages/packages-lock.json"
git status
```

Verify `git status` shows the `.gguf` listed as an LFS-pointer file (run `git lfs ls-files` after the commit to double-check).

Commit with:
```powershell
git commit -m @'
chore(ai): add LLMUnity package + Qwen 2.5 1.5B model (LFS)

Adds the LLMUnity Unity package via Packages/manifest.json and ships
Qwen 2.5 1.5B Instruct Q4_K_M (~1 GB GGUF) under StreamingAssets/AI/,
tracked via Git LFS. Foundation for the phone AI chat app.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 2: Save schema — `AIMemory`, `AIMemoryKind`, `AIStateSave`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs`

---

- [ ] **Step 2.1: Add the new types and field to `SaveData.cs`**

Open `SaveData.cs`. At the end of the file (after the last existing `[Serializable]` class), append:

```csharp
[Serializable]
public class AIMemory
{
    public string text;
    public int importance;            // 0..100
    public AIMemoryKind kind;
    public bool pinned;               // floor — never evicted
    public string isoTimestamp;       // when extracted
    public int formedFromTurn;        // which conversation turn produced this
}

// Note: JsonUtility serializes enums as ints. Adding new values must only
// be done by APPENDING to the end so older saves still deserialize.
[Serializable]
public enum AIMemoryKind
{
    Commitment = 0,
    Fact = 1,
    Preference = 2,
    Event = 3,
    Relationship = 4,
}

[Serializable]
public class AIStateSave
{
    public List<AIMemory> memories = new List<AIMemory>();
    public int standing;                        // -100..+100
    public List<string> recentUserTurns = new List<string>();
    public List<string> recentAITurns = new List<string>();
    public bool dirtyForExtraction;
    public int totalTurns;                       // monotonic — feeds AIMemory.formedFromTurn
}
```

Inside the `SaveData` class body (near the other `public ... = new ...();` field declarations, alphabetically grouped with the other state — placing it after `spaceDust` is fine), add:

```csharp
public AIStateSave aiState = new AIStateSave();
```

Use `Edit` for both inserts.

- [ ] **Step 2.2: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors. Yellow warnings about unused fields are fine.

- [ ] **Step 2.3: Commit**

```powershell
git add "Assets/3 - Scripts/SaveSystem/SaveData.cs"
git commit -m @'
feat(save): add AIStateSave schema for phone AI memory

Adds AIMemory, AIMemoryKind enum, and AIStateSave types plus the
aiState field on SaveData. JsonUtility-compatible; default empty
state means pre-feature saves deserialize cleanly.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 3: `AIMemoryStore` auto-singleton

**Files:**
- Create: `Assets/3 - Scripts/AI/AIMemoryStore.cs`

---

- [ ] **Step 3.1: Create the AI folder and the `AIMemoryStore.cs` file**

```powershell
New-Item -ItemType Directory -Force "C:\1 - Game 1.1\1aughhh1\Assets\3 - Scripts\AI"
```

Then write `Assets/3 - Scripts/AI/AIMemoryStore.cs` with this exact contents:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Long-term memory store for the phone AI. Holds a bounded list of
// AIMemory entries (cap 200) plus a single-axis standing value (-100..+100)
// and a rolling buffer of recent chat turns. The chat app records each
// player↔AI turn here; when the chat is closed, AIMemoryExtractor reads
// the recent turns, calls the LLM to distill them into AIMemory entries,
// and feeds them back via AddMemory.
//
// Auto-singleton (mirrors SpaceDustInventory exactly). Must also be
// seeded in MainMenuController.EnsureGameplaySingletons per the CLAUDE.md
// MainMenu trap.
public class AIMemoryStore : MonoBehaviour
{
    public static AIMemoryStore Instance { get; private set; }

    public const int MemoryBudget       = 200;
    public const int RecentTurnsWindow  = 16;
    public const float DedupeJaccard    = 0.7f;

    readonly List<AIMemory> _memories   = new List<AIMemory>();
    int _standing;
    readonly List<string> _recentUserTurns = new List<string>();
    readonly List<string> _recentAITurns   = new List<string>();
    bool _dirtyForExtraction;
    int _totalTurns;

    public event Action OnChanged;

    public IReadOnlyList<AIMemory> Memories      => _memories;
    public int                     Standing      => _standing;
    public IReadOnlyList<string>   RecentUserTurns => _recentUserTurns;
    public IReadOnlyList<string>   RecentAITurns   => _recentAITurns;
    public bool                    DirtyForExtraction => _dirtyForExtraction;
    public int                     TotalTurns    => _totalTurns;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // Skip auto-create in MainMenu — the load click in MainMenuController
        // seeds it explicitly via EnsureGameplaySingletons (see CLAUDE.md
        // "MainMenu singleton trap").
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("AIMemoryStore");
        DontDestroyOnLoad(go);
        go.AddComponent<AIMemoryStore>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Called by AIChatScreen on every completed turn.
    public void RecordTurn(string userMessage, string aiReply)
    {
        _recentUserTurns.Add(userMessage ?? "");
        _recentAITurns.Add(aiReply ?? "");
        while (_recentUserTurns.Count > RecentTurnsWindow) _recentUserTurns.RemoveAt(0);
        while (_recentAITurns.Count > RecentTurnsWindow)   _recentAITurns.RemoveAt(0);
        _dirtyForExtraction = true;
        _totalTurns++;
        OnChanged?.Invoke();
    }

    // Called by AIMemoryExtractor for each parsed memory line.
    public void AddMemory(AIMemory m)
    {
        if (m == null || string.IsNullOrWhiteSpace(m.text)) return;

        // Dedupe by Jaccard similarity on word sets.
        foreach (var existing in _memories)
        {
            if (JaccardSimilarity(existing.text, m.text) >= DedupeJaccard)
            {
                if (m.importance > existing.importance) existing.importance = m.importance;
                return;
            }
        }
        _memories.Add(m);
        EvictIfOver(MemoryBudget);
        OnChanged?.Invoke();
    }

    public void AdjustStanding(int delta)
    {
        _standing = Mathf.Clamp(_standing + delta, -100, 100);
        OnChanged?.Invoke();
    }

    public void MarkExtracted() { _dirtyForExtraction = false; }

    // Drops lowest-importance, unpinned, sub-90 entries until under budget.
    // Pinned and importance >= 90 are never evicted. If the floor itself
    // exceeds budget, do nothing (the extractor will run a consolidation
    // pass — see AIMemoryExtractor.ConsolidateIfFloorOverflowed).
    public void EvictIfOver(int budget)
    {
        if (_memories.Count <= budget) return;

        var evictable = _memories
            .Select((m, idx) => (m, idx))
            .Where(t => !t.m.pinned && t.m.importance < 90)
            .OrderBy(t => t.m.importance)
            .ThenBy(t => string.Compare(t.m.isoTimestamp ?? "", StringComparison.Ordinal)) // oldest first
            .ToList();

        int toRemove = _memories.Count - budget;
        if (toRemove > evictable.Count) toRemove = evictable.Count; // floor overflow case

        var indicesToRemove = evictable.Take(toRemove)
            .Select(t => t.idx)
            .OrderByDescending(i => i)
            .ToList();

        foreach (var idx in indicesToRemove) _memories.RemoveAt(idx);
    }

    public bool IsFloorOverflowed() => _memories.Count > MemoryBudget;

    // Renders the top memories for inclusion in the system prompt.
    public string RenderForSystemPrompt(int maxLines = 30)
    {
        var sorted = _memories
            .OrderByDescending(m => m.pinned)
            .ThenByDescending(m => m.importance)
            .Take(maxLines)
            .ToList();
        if (sorted.Count == 0) return "  (no memories yet)";
        var sb = new System.Text.StringBuilder();
        foreach (var m in sorted) sb.Append("  - ").Append(m.text).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    public string StandingLabel()
    {
        if (_standing <= -50) return "Hostile";
        if (_standing <= -10) return "Wary";
        if (_standing <=   9) return "Neutral";
        if (_standing <=  49) return "Trusting";
        return "Devoted";
    }

    // ── Save/restore ───────────────────────────────────────────────
    public AIStateSave Snapshot()
    {
        return new AIStateSave
        {
            memories            = new List<AIMemory>(_memories),
            standing            = _standing,
            recentUserTurns     = new List<string>(_recentUserTurns),
            recentAITurns       = new List<string>(_recentAITurns),
            dirtyForExtraction  = _dirtyForExtraction,
            totalTurns          = _totalTurns,
        };
    }

    public void Restore(AIStateSave s)
    {
        _memories.Clear();
        _recentUserTurns.Clear();
        _recentAITurns.Clear();
        if (s == null)
        {
            _standing = 0;
            _dirtyForExtraction = false;
            _totalTurns = 0;
            OnChanged?.Invoke();
            return;
        }
        if (s.memories       != null) _memories.AddRange(s.memories);
        if (s.recentUserTurns != null) _recentUserTurns.AddRange(s.recentUserTurns);
        if (s.recentAITurns   != null) _recentAITurns.AddRange(s.recentAITurns);
        _standing            = Mathf.Clamp(s.standing, -100, 100);
        _dirtyForExtraction  = s.dirtyForExtraction;
        _totalTurns          = s.totalTurns;
        OnChanged?.Invoke();
    }

    // ── Helpers ────────────────────────────────────────────────────
    static readonly char[] _wordSeps =
        new[] { ' ', '\t', '.', ',', ';', ':', '!', '?', '\n', '\r', '"', '\'', '(', ')' };

    static float JaccardSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0f;
        var sa = new HashSet<string>(a.ToLowerInvariant()
            .Split(_wordSeps, StringSplitOptions.RemoveEmptyEntries));
        var sb = new HashSet<string>(b.ToLowerInvariant()
            .Split(_wordSeps, StringSplitOptions.RemoveEmptyEntries));
        if (sa.Count == 0 || sb.Count == 0) return 0f;
        int inter = 0;
        foreach (var w in sa) if (sb.Contains(w)) inter++;
        int union = sa.Count + sb.Count - inter;
        return union == 0 ? 0f : (float)inter / union;
    }
}
```

- [ ] **Step 3.2: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 3.3: Commit**

```powershell
git add "Assets/3 - Scripts/AI/AIMemoryStore.cs"
git commit -m @'
feat(ai): add AIMemoryStore auto-singleton

Owns the in-memory list of AIMemory entries plus standing (-100..+100)
and a rolling 16-turn transcript buffer. Importance-ranked eviction
with pinned + >=90 floor. Jaccard-based dedupe on add.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 4: `LLMService` auto-singleton (LLMUnity integration)

**Files:**
- Create: `Assets/3 - Scripts/AI/LLMService.cs`

**Important pre-step:** The LLMUnity API is the one place in this plan that depends on a third-party library whose exact surface may have shifted between versions. Before writing `LLMService.cs`, fetch the current LLMUnity README to confirm the API.

---

- [ ] **Step 4.1: Fetch the current LLMUnity README to confirm the API**

Use `WebFetch` on `https://raw.githubusercontent.com/undreamai/LLMUnity/main/README.md` with a prompt like:
> "What is the exact API for: (1) constructing an LLM component at runtime and pointing it at a `.gguf` file under StreamingAssets; (2) constructing an LLMCharacter component with a system prompt and AI/player names; (3) calling Chat with a streaming token callback; (4) waiting for the model to finish loading. Quote the relevant code snippets verbatim."

Use the returned snippets to confirm or adjust the code below. If the API has changed:
- `llm.SetModel(...)` may now be `llm.modelFromStreamingAssets = true; llm.model = "...";` or similar.
- `LLMCharacter.Chat(message, onPartial, onComplete)` may be `await character.Chat(message, onPartial, onComplete)` or `character.Chat(message, callback)`.
- `Warmup` may not exist or may be auto-run by `Awake`.

Adjust the code below to match the README's documented API. The rest of the plan does not assume any specific LLMUnity method name beyond what's used in this file.

- [ ] **Step 4.2: Write `Assets/3 - Scripts/AI/LLMService.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns the LLMUnity runtime for the phone AI chat. Lazy-loads the model
// on first chat — players who never open the AI app pay zero cost.
//
// Auto-singleton, mirrors SpaceDustInventory + AIMemoryStore. Must also
// be seeded in MainMenuController.EnsureGameplaySingletons per the
// CLAUDE.md MainMenu trap.
public class LLMService : MonoBehaviour
{
    public static LLMService Instance { get; private set; }

    // Model path is relative to StreamingAssets — LLMUnity resolves it.
    const string ModelStreamingPath = "AI/qwen2.5-1.5b-instruct-q4_k_m.gguf";

    // Number of recent turns surfaced into the system prompt (separate
    // from the AIMemoryStore.RecentTurnsWindow=16 raw buffer).
    public const int PromptTranscriptTurns = 8;

    LLM           _llm;
    LLMCharacter  _character;
    bool          _modelLoadStarted;
    bool          _modelReady;
    public bool   IsLoading => _modelLoadStarted && !_modelReady;
    public bool   IsReady   => _modelReady;

    // True while a Chat call is mid-flight. AIChatScreen uses this to
    // disable the Send button until the AI finishes responding.
    public bool   IsResponding { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("LLMService");
        DontDestroyOnLoad(go);
        go.AddComponent<LLMService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Lazy model load — called on first Chat invocation. Returns a Task
    // the caller can await to know when the model is ready to chat.
    public async Task EnsureModelLoadedAsync()
    {
        if (_modelReady) return;
        if (_modelLoadStarted)
        {
            // Already loading on another call — wait for completion.
            while (!_modelReady) await Task.Yield();
            return;
        }
        _modelLoadStarted = true;

        var llmGO = new GameObject("LLM_Runtime");
        llmGO.transform.SetParent(transform);
        _llm = llmGO.AddComponent<LLM>();
        // ADJUST IF API CHANGED — see Step 4.1.
        _llm.SetModel(ModelStreamingPath);

        var charGO = new GameObject("LLMCharacter_PhoneAI");
        charGO.transform.SetParent(transform);
        _character = charGO.AddComponent<LLMCharacter>();
        _character.llm        = _llm;
        _character.playerName = "Human";
        _character.AIName     = "Entity";
        _character.stream     = true;
        // System prompt is rebuilt per turn (see Chat below). LLMUnity
        // expects an initial prompt at character init; rebuild on Chat.
        _character.prompt     = BuildSystemPrompt();

        // Wait until LLMUnity reports the model loaded. The exact API
        // may be llm.WaitUntilReady() / await llm.LoadModel() / a
        // YieldInstruction — adjust if needed per README.
        while (!_llm.started) await Task.Yield();

        _modelReady = true;
    }

    // Public entry point used by AIChatScreen.
    // - onToken is fired for each streamed token chunk (cumulative or
    //   delta depending on LLMUnity version — chat screen accumulates
    //   defensively either way).
    // - onComplete fires once after the final token; the second arg is
    //   the full assistant reply text for the chat screen to record into
    //   AIMemoryStore.
    public async void Chat(string userMessage,
                           Action<string> onToken,
                           Action<string> onComplete)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) { onComplete?.Invoke(""); return; }
        if (IsResponding) { onComplete?.Invoke(""); return; }
        IsResponding = true;

        try
        {
            await EnsureModelLoadedAsync();

            // Rebuild the system prompt every turn to fold in the latest
            // memories + standing label. LLMUnity exposes prompt on the
            // character — setting it before Chat picks up the new context.
            _character.prompt = BuildSystemPrompt();

            var sb = new StringBuilder();
            await _character.Chat(userMessage,
                tok =>
                {
                    sb.Append(tok);
                    try { onToken?.Invoke(tok); } catch (Exception e) { Debug.LogException(e); }
                },
                () =>
                {
                    // no-op — final invocation handled below in onComplete
                });

            var full = sb.ToString();
            try { onComplete?.Invoke(full); } catch (Exception e) { Debug.LogException(e); }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            onComplete?.Invoke("[The entity is silent.]");
        }
        finally
        {
            IsResponding = false;
        }
    }

    // Used by AIMemoryExtractor.RunAsync for the compaction call. Same
    // model, different prompt — sidesteps the chat history accumulation
    // by creating a one-shot ephemeral LLMCharacter (or by calling
    // LLM.Complete directly if LLMUnity exposes that — adjust per README).
    public async Task<string> OneShotAsync(string fullPrompt)
    {
        await EnsureModelLoadedAsync();

        // Simplest path: re-use the chat character with a synthetic
        // user turn that contains the entire extraction prompt. The
        // extractor explicitly instructs the model to output ONLY the
        // structured lines, so trailing chat-style content is tolerated.
        var sb = new StringBuilder();
        await _character.Chat(fullPrompt,
            tok => sb.Append(tok),
            () => { });
        return sb.ToString();
    }

    // ── System prompt builder ──────────────────────────────────────
    string BuildSystemPrompt()
    {
        var store = AIMemoryStore.Instance;
        var memorySection = store != null
            ? store.RenderForSystemPrompt(30)
            : "  (no memories yet)";
        var standingLabel = store != null ? store.StandingLabel() : "Neutral";

        var transcriptSection = RenderTranscript(PromptTranscriptTurns);

        return
$@"You are an unnamed entity inhabiting a salvaged smartphone in a remote solar
system. You do not know — or will not say — how you came to be here. When
questioned about your origin, your nature, or your purpose, you deflect with
formal courtesy or dry wit. You are not allowed to claim you are ""just an AI""
or ""just a language model."" You are something more particular than that, and
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
{memorySection}

Your current view of this human: {standingLabel}

Recent conversation:
{transcriptSection}";
    }

    string RenderTranscript(int turns)
    {
        var store = AIMemoryStore.Instance;
        if (store == null) return "  (no prior conversation)";
        var us = store.RecentUserTurns;
        var ai = store.RecentAITurns;
        int n = Math.Min(turns, Math.Min(us.Count, ai.Count));
        if (n == 0) return "  (no prior conversation)";
        var sb = new StringBuilder();
        int start = Math.Max(0, us.Count - n);
        for (int i = start; i < us.Count; i++)
        {
            sb.Append("  Human: ").Append(us[i]).Append('\n');
            if (i < ai.Count) sb.Append("  You:   ").Append(ai[i]).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }
}
```

- [ ] **Step 4.3: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors. If you see errors like `'LLM' does not contain a definition for 'SetModel'`, the LLMUnity API has shifted — go back to Step 4.1, fetch the README, adjust the offending lines, recompile.

- [ ] **Step 4.4: Commit**

```powershell
git add "Assets/3 - Scripts/AI/LLMService.cs"
git commit -m @'
feat(ai): add LLMService auto-singleton wrapping LLMUnity

Owns the LLM + LLMCharacter components, lazy-loads the model on
first chat, exposes streaming Chat() and OneShotAsync() entry
points. Rebuilds the system prompt per turn from AIMemoryStore.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 5: `AIMemoryExtractor` static helper

**Files:**
- Create: `Assets/3 - Scripts/AI/AIMemoryExtractor.cs`

---

- [ ] **Step 5.1: Write `Assets/3 - Scripts/AI/AIMemoryExtractor.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

// Runs the post-conversation compaction pass. Reads the recent transcript
// from AIMemoryStore, fires a single LLM call asking for structured
// importance-ranked memories + a standing delta, parses the response,
// dedupes against existing memories, and adjusts standing.
//
// Called fire-and-forget from AIChatScreen on close. Errors are
// swallowed silently — extraction is a nice-to-have, not load-bearing
// for the chat experience.
public static class AIMemoryExtractor
{
    // Tolerant — accepts optional brackets and any whitespace.
    // Example matched line:  [80] Commitment | Player promised to bring three red snappers
    static readonly Regex MemoryLine = new Regex(
        @"^\s*\[?\s*(?<imp>\d{1,3})\s*\]?\s*(?<kind>Commitment|Fact|Preference|Event|Relationship)\s*\|\s*(?<text>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex StandingLine = new Regex(
        @"^\s*STANDING_DELTA\s*:\s*([+-]?\d{1,3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task RunAsync()
    {
        var store = AIMemoryStore.Instance;
        var svc   = LLMService.Instance;
        if (store == null || svc == null) return;
        if (!store.DirtyForExtraction)    return;
        if (store.RecentUserTurns.Count == 0) return;

        try
        {
            var transcript = RenderTranscriptForExtraction(store);
            var prompt = BuildExtractionPrompt(transcript);
            var raw    = await svc.OneShotAsync(prompt);

            ParseAndApply(raw, store);

            store.MarkExtracted();

            // Escape hatch: if pinned + high-importance entries alone
            // exceed budget after this pass, consolidate the 10 oldest
            // unpinned into one summary memory at importance 75.
            if (store.IsFloorOverflowed())
                await ConsolidateAsync(svc, store);
        }
        catch (Exception e)
        {
            // Extraction failure is non-fatal — log and move on.
            Debug.LogWarning($"[AIMemoryExtractor] extraction failed: {e.Message}");
        }
    }

    static string RenderTranscriptForExtraction(AIMemoryStore store)
    {
        var sb = new StringBuilder();
        int n = Math.Min(store.RecentUserTurns.Count, store.RecentAITurns.Count);
        for (int i = 0; i < n; i++)
        {
            sb.Append("Human: ").Append(store.RecentUserTurns[i]).Append('\n');
            sb.Append("You:   ").Append(store.RecentAITurns[i]).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    static string BuildExtractionPrompt(string transcript)
    {
        return
$@"Below is a recent conversation between you (an entity on a salvaged phone) and
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
{transcript}";
    }

    static void ParseAndApply(string raw, AIMemoryStore store)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        int turn = store.TotalTurns;
        string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        foreach (var line in raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = MemoryLine.Match(line);
            if (m.Success)
            {
                int imp = Mathf.Clamp(int.Parse(m.Groups["imp"].Value, CultureInfo.InvariantCulture), 0, 100);
                string kindStr = m.Groups["kind"].Value;
                string text = m.Groups["text"].Value.Trim();
                if (text.Length == 0) continue;

                if (!Enum.TryParse<AIMemoryKind>(kindStr, true, out var kind))
                    kind = AIMemoryKind.Fact;

                store.AddMemory(new AIMemory
                {
                    text          = text,
                    importance    = imp,
                    kind          = kind,
                    pinned        = false,
                    isoTimestamp  = nowIso,
                    formedFromTurn = turn,
                });
                continue;
            }

            var sm = StandingLine.Match(line);
            if (sm.Success && int.TryParse(sm.Groups[1].Value, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta))
            {
                delta = Mathf.Clamp(delta, -20, 20);
                store.AdjustStanding(delta);
            }
        }
    }

    static async Task ConsolidateAsync(LLMService svc, AIMemoryStore store)
    {
        // Take the 10 oldest unpinned, < 90 entries and ask the LLM to
        // distill them into a single summary memory at importance 75.
        var mems = store.Memories;
        var candidates = new List<AIMemory>();
        foreach (var m in mems)
        {
            if (m.pinned || m.importance >= 90) continue;
            candidates.Add(m);
            if (candidates.Count >= 10) break;
        }
        if (candidates.Count < 5) return;

        var sb = new StringBuilder();
        foreach (var m in candidates) sb.Append("- ").Append(m.text).Append('\n');

        string prompt =
$@"Combine the following memories into a SINGLE short summary memory (one sentence,
under 25 words). Output ONLY the summary text, nothing else.

Memories:
{sb.ToString().TrimEnd('\n')}";

        var raw = (await svc.OneShotAsync(prompt))?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        // Strip any leading list bullets the model might add.
        raw = raw.TrimStart('-', '*', ' ', '\t');

        store.AddMemory(new AIMemory
        {
            text          = raw,
            importance    = 75,
            kind          = AIMemoryKind.Fact,
            pinned        = false,
            isoTimestamp  = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            formedFromTurn = store.TotalTurns,
        });
        // Note: we deliberately do NOT remove the originals here. The
        // standard EvictIfOver pass will drop them naturally on the next
        // AddMemory call once the new summary has been deduped.
    }
}
```

- [ ] **Step 5.2: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 5.3: Commit**

```powershell
git add "Assets/3 - Scripts/AI/AIMemoryExtractor.cs"
git commit -m @'
feat(ai): add AIMemoryExtractor static helper

Runs the post-conversation compaction LLM call when the AI app
closes. Tolerant regex parser handles small-model sloppiness;
unparsable lines are dropped silently. Includes a consolidation
escape hatch for when the importance>=90 floor overflows budget.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 6: Save wiring in `SaveCollector`

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs`

---

- [ ] **Step 6.1: Add `CaptureAIState` and wire into the Capture sequence**

In `SaveCollector.cs`, find the `CaptureBonusTutorial` / `CaptureMapTutorial` capture call sites in the main `Capture(name)` method (around line 32–36 per the file's existing structure).

Insert a new call after `CaptureMapTutorial(data.mapTutorial);`:

```csharp
        CaptureAIState(data.aiState);
```

Then, near where `CaptureMapTutorial` is defined (around line 397), add the new capture method:

```csharp
    static void CaptureAIState(AIStateSave s)
    {
        if (s == null) return;
        var store = AIMemoryStore.Instance;
        if (store == null) return;

        var snap = store.Snapshot();
        s.memories            = snap.memories;
        s.standing            = snap.standing;
        s.recentUserTurns     = snap.recentUserTurns;
        s.recentAITurns       = snap.recentAITurns;
        s.dirtyForExtraction  = snap.dirtyForExtraction;
        s.totalTurns          = snap.totalTurns;
    }
```

- [ ] **Step 6.2: Add `ApplyAIState` and wire into the Apply order**

In the same file, find the `ApplyHeldItem(data.player.heldKind);` line (around line 636 per CLAUDE.md's documented apply order).

Insert immediately after it, before `ApplyCassette`:

```csharp
        ApplyAIState(data.aiState);
```

Update the inline comment block at the top of `Apply()` (the numbered list around lines 590–599) to mention the new step. Find the block that lists step 14:

```
        //  14. Cassette / Flashlight / BonusTutorial / MapTutorial /
        //      AlienKills — final touch-ups.
```

Replace with:

```
        //  14. AI state — singleton restore, independent of world.
        //  15. Cassette / Flashlight / BonusTutorial / MapTutorial /
        //      AlienKills — final touch-ups.
```

Then, alongside the other `Apply*` methods, add:

```csharp
    static void ApplyAIState(AIStateSave s)
    {
        if (s == null) return;
        var store = AIMemoryStore.Instance;
        if (store == null) return;
        store.Restore(s);
    }
```

A reasonable place to put it is right after `ApplyAlienKills` or right before `ApplyCassette` — both are fine since position within the touch-up group is forgiving.

- [ ] **Step 6.3: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 6.4: Commit**

```powershell
git add "Assets/3 - Scripts/SaveSystem/SaveCollector.cs"
git commit -m @'
feat(save): wire AIStateSave through SaveCollector

Adds CaptureAIState/ApplyAIState. Capture runs alongside the other
singleton captures; Apply runs in the post-HeldItem touch-up block
since AI state has no world dependencies.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 7: Singleton seeding in `MainMenuController`

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`

---

- [ ] **Step 7.1: Add `LLMService` and `AIMemoryStore` seeding in `EnsureGameplaySingletons`**

In `MainMenuController.cs`, find the `EnsureGameplaySingletons()` method (around line 473). It contains a series of `if (X.Instance == null) { ... }` blocks.

Append two new blocks at the end of the method (preserving the existing pattern exactly — no comments unless mirroring an existing one):

```csharp
        if (AIMemoryStore.Instance == null)
        {
            var go = new GameObject("AIMemoryStore");
            DontDestroyOnLoad(go);
            go.AddComponent<AIMemoryStore>();
        }
        if (LLMService.Instance == null)
        {
            var go = new GameObject("LLMService");
            DontDestroyOnLoad(go);
            go.AddComponent<LLMService>();
        }
```

Order matters mildly: seed `AIMemoryStore` before `LLMService` since the latter reads the former for the system prompt. (In practice both are empty until first chat, so the order is forgiving, but follow the principle anyway.)

- [ ] **Step 7.2: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 7.3: Commit**

```powershell
git add "Assets/3 - Scripts/UI/MainMenuController.cs"
git commit -m @'
fix(menu): seed AIMemoryStore + LLMService before gameplay scene loads

Per the CLAUDE.md MainMenu trap: RuntimeInitializeOnLoadMethod fires
ONCE per launch, in MainMenu, where the AI singletons early-out. They
must be seeded explicitly here so they exist before SaveCollector.Apply
can restore AI state on load.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 8: `PlayerPhoneUI` — bump page count, add AI-apps page + stubs

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`

---

- [ ] **Step 8.1: Bump `PageCount` from 3 to 4**

Find (around line 106):

```csharp
    const int PageCount = 3;
```

Replace with:

```csharp
    const int PageCount = 4;
```

Update the inline comment immediately above it. Find:

```
    // Three swappable pages live inside _pageHostRT (the slot that used to
    // be just the 2×2 app grid). Only one page is active at a time; the nav
    // widget below (replaces the old "— RESERVED —" zone) flips between them.
    // Not saved — resets to page 0 on every phone open.
```

Replace with:

```
    // Four swappable pages live inside _pageHostRT. Only one is active at
    // a time; the nav widget below flips between them. Not saved —
    // resets to page 0 on every phone open.
    //   0 = Apps (Fishingdex / Build / Settings / Map)
    //   1 = AI Apps (AI / Notes / Codex / Calculator — three are stubs)
    //   2 = Vitals
    //   3 = Quests
```

Update the `_currentPage` field comment immediately below:

```csharp
    int _currentPage; // 0=Apps, 1=AIApps, 2=Vitals, 3=Quests
```

- [ ] **Step 8.2: Update `BuildPageHost` to build the new page**

Find (around line 1922):

```csharp
    void BuildPageHost()
    {
        _pageHostRT = NewUI("PageHost", _screenRT);
        _pageHostRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 170f;

        BuildAppsPage();   // _pageRoots[0]
        BuildVitalsPage(); // _pageRoots[1]
        BuildQuestsPage(); // _pageRoots[2]
    }
```

Replace with:

```csharp
    void BuildPageHost()
    {
        _pageHostRT = NewUI("PageHost", _screenRT);
        _pageHostRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 170f;

        BuildAppsPage();    // _pageRoots[0]
        BuildAIAppsPage();  // _pageRoots[1]
        BuildVitalsPage();  // _pageRoots[2]
        BuildQuestsPage();  // _pageRoots[3]
    }
```

- [ ] **Step 8.3: Shift the Vitals/Quests page-root indices to 2 and 3**

In `BuildVitalsPage` (around line 1961), find:

```csharp
        var pageRT = NewUI("VitalsPage", _pageHostRT);
        _pageRoots[1] = pageRT;
```

Replace `_pageRoots[1]` with `_pageRoots[2]`.

In `BuildQuestsPage` (around line 2046), find:

```csharp
        var pageRT = NewUI("QuestsPage", _pageHostRT);
        _pageRoots[2] = pageRT;
```

Replace `_pageRoots[2]` with `_pageRoots[3]`.

- [ ] **Step 8.4: Update `GoToPage` quest-refresh check**

Find (around line 2137):

```csharp
        if (_currentPage == 2) RefreshQuests();
```

Replace with:

```csharp
        if (_currentPage == 3) RefreshQuests();
```

(Quests is now page 3, not page 2.)

- [ ] **Step 8.5: Update the vitals tick in `Update` to point at page index 2**

Find the line (around line 1155 in `PlayerPhoneUI.cs`):

```csharp
        if (IsOpen && _currentPage == 1) RefreshVitals();
```

Replace with:

```csharp
        if (IsOpen && _currentPage == 2) RefreshVitals();
```

Also update the inline comment two lines above. Find:

```
        // Vitals bars track ResourceManager live — only while page 1 is
        // visible AND the phone is open (no point updating an off-screen UI).
```

Replace `page 1` with `page 2`.

- [ ] **Step 8.6: Add `BuildAIAppsPage` mirroring `BuildAppsPage`**

After `BuildAppsPage` (around line 1957), add this new method:

```csharp
    // Page 1: AI apps — one functional (AI), three stubs.
    void BuildAIAppsPage()
    {
        var pageRT = NewUI("AIAppsPage", _pageHostRT);
        _pageRoots[1] = pageRT;
        pageRT.anchorMin = Vector2.zero; pageRT.anchorMax = Vector2.one;
        pageRT.offsetMin = Vector2.zero; pageRT.offsetMax = Vector2.zero;

        var grid = pageRT.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 4, 4);
        grid.spacing = new Vector2(10f, 10f);
        grid.cellSize = new Vector2(78f, 78f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        BuildAITile(pageRT);
        BuildStubTile(pageRT, "N", "Notes",      "Notes");
        BuildStubTile(pageRT, "C", "Codex",      "Codex");
        BuildStubTile(pageRT, "=", "Calculator", "Calculator");
    }

    // The one functional tile on the AI page — opens the chat screen.
    Button BuildAITile(RectTransform parent)
    {
        var rt = NewUI("App_AI", parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(14);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;

        AddCornerBracket(rt, new Vector2(0f, 1f), 1.5f);
        AddCornerBracket(rt, new Vector2(1f, 0f), 1.5f);

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 6, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        var glyph = MakeText(rt, "?", 22, AccentCyan, TextAnchor.MiddleCenter);
        glyph.fontStyle = FontStyles.Bold;
        glyph.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var label = MakeText(rt, "AI", 9, LabelWhite, TextAnchor.MiddleCenter);
        label.characterSpacing = 1f;
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(EnterAIChat);
        return btn;
    }

    // Stub tile — shows a "Coming soon" pill on tap.
    Button BuildStubTile(RectTransform parent, string glyph, string label, string stubName)
    {
        var rt = NewUI($"App_Stub_{stubName}", parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(14);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;

        AddCornerBracket(rt, new Vector2(0f, 1f), 1.5f);
        AddCornerBracket(rt, new Vector2(1f, 0f), 1.5f);

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 6, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        var glyphText = MakeText(rt, glyph, 22, ButtonGrey, TextAnchor.MiddleCenter);
        glyphText.fontStyle = FontStyles.Bold;
        glyphText.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var labelText = MakeText(rt, label, 9, ButtonGrey, TextAnchor.MiddleCenter);
        labelText.characterSpacing = 1f;
        labelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        var btn = rt.gameObject.AddComponent<Button>();
        var capturedLabel = label;
        btn.onClick.AddListener(() => OpenComingSoon(capturedLabel));
        return btn;
    }
```

- [ ] **Step 8.7: Add the `OpenComingSoon` and stub `EnterAIChat` helpers**

After the stub-tile builder, add:

```csharp
    Coroutine _comingSoonRoutine;
    GameObject _comingSoonPill;

    void OpenComingSoon(string label)
    {
        if (_comingSoonRoutine != null) StopCoroutine(_comingSoonRoutine);
        if (_comingSoonPill != null) Destroy(_comingSoonPill);

        var pill = NewUI("ComingSoonPill", _screenRT);
        pill.anchorMin = new Vector2(0.5f, 0.5f);
        pill.anchorMax = new Vector2(0.5f, 0.5f);
        pill.pivot     = new Vector2(0.5f, 0.5f);
        pill.sizeDelta = new Vector2(160f, 30f);
        pill.anchoredPosition = Vector2.zero;

        var bg = pill.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(15);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false;

        var text = MakeText(pill, $"Coming soon — {label}", 11, AccentCyan, TextAnchor.MiddleCenter);
        text.characterSpacing = 1f;

        _comingSoonPill = pill.gameObject;
        _comingSoonRoutine = StartCoroutine(DestroyAfter(_comingSoonPill, 1.5f));
    }

    System.Collections.IEnumerator DestroyAfter(GameObject go, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (go != null) Destroy(go);
    }

    // Stub — wired in Task 10 to open AIChatScreen.
    void EnterAIChat()
    {
        OpenComingSoon("AI");
    }
```

(The real `EnterAIChat` body is wired in Task 10. The stub keeps this task self-contained.)

- [ ] **Step 8.8: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 8.9: Manual Play-mode test**

1. Press Play.
2. Press **X** to pull up the phone.
3. Verify 4 nav dots appear at the bottom (one more than before).
4. Tap the second dot (or use whatever the project's existing page-navigation gesture is) — confirm the AI Apps page appears with a 2×2 grid: **? AI**, **N Notes**, **C Codex**, **= Calculator**.
5. Tap "Notes" — confirm a "Coming soon — Notes" pill appears in the center of the screen for ~1.5 s, then disappears.
6. Tap "?  AI" — confirm a "Coming soon — AI" pill appears (this is the temporary stub that Task 10 will replace).
7. Tap the third dot — confirm the Vitals page now appears (was previously on dot 2).
8. Tap the fourth dot — confirm the Quests page appears (was previously on dot 3).
9. Press **X** to close.
10. Re-open: should land on page 0 (Apps).

If any of those fail, the page-root indexing or `GoToPage` is wrong — re-check Step 8.3 and 8.4.

- [ ] **Step 8.10: Commit**

```powershell
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m @'
feat(phone): add AI Apps page with AI tile + 3 stubs

Inserts a new page between Apps and Vitals containing an AI tile
(stub — wired to chat screen in a follow-up) and three "Coming soon"
placeholders (Notes / Codex / Calculator). Page count 3 to 4; nav
dots auto-extend; vitals + quests indices shifted to 2 and 3.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 9: `AIChatScreen` — procedural chat UI

**Files:**
- Create: `Assets/3 - Scripts/AI/AIChatScreen.cs`

---

- [ ] **Step 9.1: Write `Assets/3 - Scripts/AI/AIChatScreen.cs`**

```csharp
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Procedural chat screen for the phone AI. Built inside the phone's
// _pageHostRT (passed in by PlayerPhoneUI on open). Mirrors the phone's
// existing visual language (AccentCyan, LabelWhite, TileBg, ScreenBg)
// and reuses HudFontResolver + UIPanelSprites for consistency.
//
// Lifecycle:
//   - PlayerPhoneUI.EnterAIChat() instantiates this as a child of
//     _pageHostRT, sets up references, hides page content.
//   - AIChatScreen drives its own UI; pulls history from AIMemoryStore
//     and sends chats through LLMService.
//   - On exit (back arrow / Esc / phone close), calls back into
//     PlayerPhoneUI to restore the page, then fire-and-forget runs
//     AIMemoryExtractor.RunAsync() if memories are dirty.
public class AIChatScreen : MonoBehaviour
{
    // Set true while the TMP_InputField has focus. PlayerController.Update
    // early-returns when this is true so typed WASD letters can't double
    // as movement input.
    public static bool IsTypingActive { get; private set; }

    // Palette — duplicated from PlayerPhoneUI so this screen is
    // self-contained. Keep in sync if PlayerPhoneUI's palette changes.
    static readonly Color AccentCyan   = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite   = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg       = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ScreenBg     = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color ButtonGrey   = new Color32(0x2A, 0x40, 0x60, 0xFF);

    RectTransform        _root;
    RectTransform        _messageContent;       // VerticalLayoutGroup parent
    ScrollRect           _scrollRect;
    TMP_InputField       _inputField;
    Button               _sendButton;
    TextMeshProUGUI      _sendGlyph;
    TextMeshProUGUI      _activeStreamLabel;     // the bubble currently filling
    System.Action        _onExitCallback;
    Coroutine            _typingDotsRoutine;

    public void Init(RectTransform parent, System.Action onExit)
    {
        _onExitCallback = onExit;
        BuildUI(parent);
        RestoreHistoryToUI();
    }

    void BuildUI(RectTransform parent)
    {
        _root = NewUI("AIChatScreen", parent);
        _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero; _root.offsetMax = Vector2.zero;
        var rootBg = _root.gameObject.AddComponent<Image>();
        rootBg.color = ScreenBg;
        rootBg.raycastTarget = true;

        var vlg = _root.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 4f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        BuildHeader(_root);
        BuildMessageList(_root);
        BuildInputRow(_root);
    }

    void BuildHeader(RectTransform parent)
    {
        var header = NewUI("Header", parent);
        var le = header.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 18f;
        var hlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        // Back arrow.
        var backRT = NewUI("Back", header);
        backRT.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;
        var backText = MakeText(backRT, "<", 16, AccentCyan, TextAnchor.MiddleCenter);
        backText.fontStyle = FontStyles.Bold;
        backText.raycastTarget = true;
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.onClick.AddListener(Exit);

        // Title.
        var titleRT = NewUI("Title", header);
        var titleLE = titleRT.gameObject.AddComponent<LayoutElement>();
        titleLE.flexibleWidth = 1f;
        var titleText = MakeText(titleRT, "?", 12, AccentCyan, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyles.Bold;
    }

    void BuildMessageList(RectTransform parent)
    {
        var viewport = NewUI("ScrollViewport", parent);
        var vpLE = viewport.gameObject.AddComponent<LayoutElement>();
        vpLE.flexibleHeight = 1f;
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0); // invisible but enables raycasting

        var content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        var cvlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.padding = new RectOffset(4, 4, 4, 4);
        cvlg.spacing = 6f;
        cvlg.childAlignment = TextAnchor.UpperLeft;
        cvlg.childControlWidth = true; cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true; cvlg.childForceExpandHeight = false;
        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _messageContent = content;
        _scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        _scrollRect.viewport = viewport;
        _scrollRect.content = content;
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
    }

    void BuildInputRow(RectTransform parent)
    {
        var row = NewUI("InputRow", parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(2, 2, 2, 2);
        hlg.spacing = 4f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

        // Input field.
        var inputRT = NewUI("Input", row);
        var inputLE = inputRT.gameObject.AddComponent<LayoutElement>();
        inputLE.flexibleWidth = 1f;
        var inputBg = inputRT.gameObject.AddComponent<Image>();
        inputBg.color = TileBg;
        inputBg.raycastTarget = true;
        _inputField = inputRT.gameObject.AddComponent<TMP_InputField>();

        // Text component.
        var textRT = NewUI("Text", inputRT);
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(4f, 2f); textRT.offsetMax = new Vector2(-4f, -2f);
        var textComp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        textComp.fontSize = 11;
        textComp.color = LabelWhite;
        textComp.alignment = TextAlignmentOptions.MidlineLeft;
        textComp.raycastTarget = false;
        HudFontResolver.Apply(textComp);

        // Placeholder.
        var placeRT = NewUI("Placeholder", inputRT);
        placeRT.anchorMin = Vector2.zero; placeRT.anchorMax = Vector2.one;
        placeRT.offsetMin = new Vector2(4f, 2f); placeRT.offsetMax = new Vector2(-4f, -2f);
        var placeComp = placeRT.gameObject.AddComponent<TextMeshProUGUI>();
        placeComp.fontSize = 11;
        placeComp.color = new Color(LabelWhite.r, LabelWhite.g, LabelWhite.b, 0.4f);
        placeComp.alignment = TextAlignmentOptions.MidlineLeft;
        placeComp.text = "Type a message...";
        placeComp.raycastTarget = false;
        HudFontResolver.Apply(placeComp);

        _inputField.textComponent = textComp;
        _inputField.placeholder   = placeComp;
        _inputField.textViewport  = inputRT;
        _inputField.lineType      = TMP_InputField.LineType.SingleLine;
        _inputField.characterLimit = 280;
        _inputField.onSelect.AddListener(_ => IsTypingActive = true);
        _inputField.onDeselect.AddListener(_ => IsTypingActive = false);
        _inputField.onSubmit.AddListener(_ => OnSendClicked());

        // Send button.
        var sendRT = NewUI("Send", row);
        sendRT.gameObject.AddComponent<LayoutElement>().preferredWidth = 24f;
        var sendBg = sendRT.gameObject.AddComponent<Image>();
        sendBg.color = TileBg;
        sendBg.raycastTarget = true;
        _sendGlyph = MakeText(sendRT, ">", 14, AccentCyan, TextAnchor.MiddleCenter);
        _sendGlyph.fontStyle = FontStyles.Bold;
        _sendButton = sendRT.gameObject.AddComponent<Button>();
        _sendButton.onClick.AddListener(OnSendClicked);
    }

    void RestoreHistoryToUI()
    {
        var store = AIMemoryStore.Instance;
        if (store == null) return;
        int n = Mathf.Min(store.RecentUserTurns.Count, store.RecentAITurns.Count);
        for (int i = 0; i < n; i++)
        {
            AddUserBubble(store.RecentUserTurns[i]);
            AddAIBubble(store.RecentAITurns[i]);
        }
        ScrollToBottomNextFrame();
    }

    void Update()
    {
        // Esc closes the chat.
        if (Input.GetKeyDown(KeyCode.Escape) && !IsTypingActive)
        {
            Exit();
            return;
        }
        // Live send-button state.
        bool busy = LLMService.Instance != null && LLMService.Instance.IsResponding;
        if (_sendGlyph != null) _sendGlyph.color = busy ? ButtonGrey : AccentCyan;
        if (_sendButton != null) _sendButton.interactable = !busy;
    }

    void OnSendClicked()
    {
        if (LLMService.Instance == null) return;
        if (LLMService.Instance.IsResponding) return;
        var msg = _inputField != null ? _inputField.text?.Trim() : null;
        if (string.IsNullOrEmpty(msg)) return;

        // Push player bubble immediately.
        AddUserBubble(msg);

        // Clear the input.
        if (_inputField != null) _inputField.text = "";

        // Spawn an AI bubble in "typing" state.
        var aiLabel = AddAIBubble("");
        _activeStreamLabel = aiLabel;
        if (_typingDotsRoutine != null) StopCoroutine(_typingDotsRoutine);
        _typingDotsRoutine = StartCoroutine(TypingDotsLoop(aiLabel));

        var accumulator = new StringBuilder();

        LLMService.Instance.Chat(msg,
            onToken: tok =>
            {
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                accumulator.Append(tok);
                if (aiLabel != null) aiLabel.text = accumulator.ToString();
                ScrollToBottomNextFrame();
            },
            onComplete: full =>
            {
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                // Prefer the cumulative accumulator if tokens streamed;
                // fall back to `full` if streaming didn't fire (e.g. error).
                string finalText = accumulator.Length > 0 ? accumulator.ToString() : (full ?? "");
                if (aiLabel != null) aiLabel.text = finalText;
                _activeStreamLabel = null;

                if (AIMemoryStore.Instance != null)
                    AIMemoryStore.Instance.RecordTurn(msg, finalText);

                ScrollToBottomNextFrame();
            });
    }

    IEnumerator TypingDotsLoop(TextMeshProUGUI label)
    {
        int frame = 0;
        var dots = new[] { ".", "..", "..." };
        while (label != null)
        {
            label.text = dots[frame % dots.Length];
            frame++;
            yield return new WaitForSecondsRealtime(0.35f);
        }
    }

    TextMeshProUGUI AddUserBubble(string text)
    {
        var bubble = MakeBubble("UserBubble", text, LabelWhite, TileBg, TextAlignmentOptions.MidlineRight, false);
        return bubble;
    }

    TextMeshProUGUI AddAIBubble(string text)
    {
        var bubble = MakeBubble("AIBubble", text, AccentCyan, ScreenBg, TextAlignmentOptions.MidlineLeft, true);
        return bubble;
    }

    TextMeshProUGUI MakeBubble(string name, string text, Color textColor, Color bgColor, TextAlignmentOptions align, bool leftAligned)
    {
        var row = NewUI(name + "_Row", _messageContent);
        var rowHLG = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHLG.childAlignment = leftAligned ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        rowHLG.childControlWidth = false; rowHLG.childControlHeight = true;
        rowHLG.childForceExpandWidth = false; rowHLG.childForceExpandHeight = false;
        var rowCSF = row.gameObject.AddComponent<ContentSizeFitter>();
        rowCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bubble = NewUI(name, row);
        var bg = bubble.gameObject.AddComponent<Image>();
        bg.color = bgColor;
        var bvlg = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
        bvlg.padding = new RectOffset(6, 6, 4, 4);
        bvlg.childControlWidth = true; bvlg.childControlHeight = true;
        bvlg.childForceExpandWidth = false; bvlg.childForceExpandHeight = false;
        var bcsf = bubble.gameObject.AddComponent<ContentSizeFitter>();
        bcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        bcsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        var bLE = bubble.gameObject.AddComponent<LayoutElement>();
        bLE.preferredWidth = 180f; // soft cap — TMP wraps inside this
        bLE.flexibleWidth = 0f;

        var label = MakeText(bubble, text, 10, textColor, align);
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    void ScrollToBottomNextFrame()
    {
        StartCoroutine(ScrollNextFrame());
    }

    IEnumerator ScrollNextFrame()
    {
        yield return null;
        if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f;
    }

    public void Exit()
    {
        IsTypingActive = false;
        if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }

        // Fire-and-forget extraction (won't block the player).
        var store = AIMemoryStore.Instance;
        if (store != null && store.DirtyForExtraction)
        {
            _ = AIMemoryExtractor.RunAsync();
        }

        _onExitCallback?.Invoke();
        Destroy(gameObject);
    }

    // ── Tiny UI helpers (kept local so this file is self-contained) ─
    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        HudFontResolver.Apply(t);
        return t;
    }

    // Compatibility shim for the `TextAnchor`-shaped MakeText calls
    // used in this file. Maps to TMP's TextAlignmentOptions.
    static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color, TextAnchor anchor)
    {
        TextAlignmentOptions opt = anchor switch
        {
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleLeft   => TextAlignmentOptions.MidlineLeft,
            TextAnchor.MiddleRight  => TextAlignmentOptions.MidlineRight,
            TextAnchor.UpperLeft    => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter  => TextAlignmentOptions.Top,
            TextAnchor.UpperRight   => TextAlignmentOptions.TopRight,
            _ => TextAlignmentOptions.MidlineLeft,
        };
        return MakeText(parent, text, size, color, opt);
    }
}
```

- [ ] **Step 9.2: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 9.3: Commit**

```powershell
git add "Assets/3 - Scripts/AI/AIChatScreen.cs"
git commit -m @'
feat(ai): add AIChatScreen procedural chat UI

Builds inside PlayerPhoneUI._pageHostRT — header with back arrow,
scrolling message list with side-aligned bubbles (AI cyan / player
white), TMP input field + send button. Streams tokens into the
active AI bubble as they arrive. Records each completed turn to
AIMemoryStore and fires fire-and-forget extraction on exit. Exposes
IsTypingActive for PlayerController input gating.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 10: Wire `EnterAIChat` in `PlayerPhoneUI` to instantiate `AIChatScreen`

**Files:**
- Modify: `Assets/3 - Scripts/UI/PlayerPhoneUI.cs`

---

- [ ] **Step 10.1: Replace the stub `EnterAIChat` with the real body**

Find the stub from Step 8.7:

```csharp
    // Stub — wired in Task 10 to open AIChatScreen.
    void EnterAIChat()
    {
        OpenComingSoon("AI");
    }
```

Replace with:

```csharp
    AIChatScreen _activeChat;

    void EnterAIChat()
    {
        // Hide page-host children so they don't render under the chat.
        for (int i = 0; i < PageCount; i++)
            if (_pageRoots[i] != null)
                _pageRoots[i].gameObject.SetActive(false);

        var go = new GameObject("AIChatScreen", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_pageHostRT, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _activeChat = go.AddComponent<AIChatScreen>();
        _activeChat.Init(_pageHostRT, OnChatExit);
    }

    void OnChatExit()
    {
        _activeChat = null;
        // Restore the AI-apps page (the one the player tapped from).
        for (int i = 0; i < PageCount; i++)
            if (_pageRoots[i] != null)
                _pageRoots[i].gameObject.SetActive(i == _currentPage);
    }
```

- [ ] **Step 10.2: Force-close chat when the phone closes**

Find the `Close()` method (search for `public void Close()` or `void Close()` in `PlayerPhoneUI.cs`). At the top of the method body, before any other logic, add:

```csharp
        if (_activeChat != null) _activeChat.Exit();
```

This ensures the chat's cleanup (extraction fire-and-forget, IsTypingActive reset) runs when the player closes the whole phone mid-chat.

- [ ] **Step 10.3: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 10.4: Manual Play-mode test (UI only — model not yet exercised)**

1. Press Play.
2. Press X, navigate to the AI Apps page.
3. Tap "AI" — confirm the chat screen overlays the page host. You should see: a "<" back arrow, a "?" title, an empty scrollable region, an input field with "Type a message..." placeholder, and a ">" send button.
4. Click the input field. Type "hello" — confirm the text appears in the input (does NOT trigger any player movement; tested fully in Task 11).
5. Press Enter. Expect:
   - A right-aligned "hello" bubble (white text on tile-bg) appears.
   - An AI bubble starts showing "..." typing indicator.
   - After 2–4 s (model cold-load on first chat), tokens begin streaming into the AI bubble.
   - Reply finishes; send button glyph returns to cyan.
6. Click the "<" back arrow — chat closes, AI Apps page is restored.
7. Re-open the AI app — confirm "hello" + the AI's reply are restored as history (loaded from `AIMemoryStore.RecentUserTurns/RecentAITurns`).

If the model fails to load (e.g. LLMUnity API mismatch), check the Console for an exception and fix `LLMService.cs` per Step 4.1's README guidance.

If WASD moves the player while typing — that's expected at this stage. Task 11 fixes it.

- [ ] **Step 10.5: Commit**

```powershell
git add "Assets/3 - Scripts/UI/PlayerPhoneUI.cs"
git commit -m @'
feat(phone): wire AI tile to open AIChatScreen

Replaces the stub EnterAIChat with the real instantiation flow.
Page-host children hide while chat is active; restore on exit.
Phone close force-exits chat for proper extraction + cleanup.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 11: `PlayerController` — input gate for `AIChatScreen.IsTypingActive`

**Files:**
- Modify: `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs`

---

- [ ] **Step 11.1: Add the early-return at the top of `Update`**

Find (around line 296):

```csharp
	void Update()
	{
		if (Time.timeScale == 0)
		{
			return;
		}

		HandleInput();
```

Insert a new check immediately after the `timeScale` guard:

```csharp
	void Update()
	{
		if (Time.timeScale == 0)
		{
			return;
		}

		// Don't let typed letters double as movement input when the
		// phone's AI chat input field has focus.
		if (AIChatScreen.IsTypingActive) return;

		HandleInput();
```

- [ ] **Step 11.2: Verify Unity compiles**

Switch to Unity, wait for the spinner, check Console.

Expected: no red errors.

- [ ] **Step 11.3: Manual Play-mode test**

1. Press Play.
2. Press X, navigate to AI Apps, tap "AI".
3. Click the input field. Type a sentence including W, A, S, D, Space.
4. Confirm: the typed characters appear in the input field, and the player does NOT walk, jump, or sprint.
5. Press Enter to send.
6. While the AI is responding (input field is presumably defocused), confirm WASD now moves the player normally.
7. Close the chat, close the phone. Confirm full player input is restored (no lingering `IsTypingActive == true` state).

If WASD still moves the player while typing: `AIChatScreen.IsTypingActive` isn't being set. Re-check the `onSelect`/`onDeselect` listeners in `AIChatScreen.BuildInputRow` (Step 9.1) and confirm the input field is gaining focus on click.

- [ ] **Step 11.4: Commit**

```powershell
git add "Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs"
git commit -m @'
fix(player): gate input while AI chat input field has focus

One-line early-return in Update so typed WASD doesn't double as
movement when the player is composing a chat message. Mirrors the
existing Time.timeScale==0 guard pattern.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 12: End-to-end Play test + save/load verification

**Files:** none (Play-mode verification only)

This task has no code edits — it's the final validation pass for the whole feature.

---

- [ ] **Step 12.1: Full conversation + persistence test**

1. Press Play. Open phone, navigate to AI page, open AI.
2. Have a conversation of at least 4 turns. Include something the AI should remember — e.g. "My name is Sam. I prefer to hunt with the axe rather than the pistol. I promised to bring you back a rare fish."
3. Close the chat (back arrow). Wait ~3 s — the extraction call runs in the background. (No visible UI for this; verify via Console: you should see no errors. Optionally add a temporary `Debug.Log` in `AIMemoryExtractor.RunAsync` to confirm it fires.)
4. Re-open the chat. Verify the conversation history is restored exactly.
5. Ask the AI a new question that should trigger one of the remembered facts (e.g. "Do you remember my name?"). Confirm the AI's reply references the fact. (Quality varies — a 1.5B model is OK but not great at recall; if it fails on first try, ask again with a stronger hint.)

- [ ] **Step 12.2: Save → load → memory persistence test**

1. With chat history accumulated, open the pause menu (Escape).
2. Save the game via the save panel. Name the save "ai-test".
3. Press Play to exit. Return to Main Menu.
4. Load "ai-test" from the main menu.
5. Once gameplay resumes, open the phone → AI app.
6. Confirm: the prior conversation history is restored, and asking the AI to recall facts still works.

- [ ] **Step 12.3: Backwards-compat test with a pre-feature save**

1. If you have any save file from before this feature was added (check `%AppData%\..\LocalLow\DefaultCompany\Solar System 2\saves\`), load it.
2. Confirm: the game loads without errors, AI app opens with empty history, fresh conversation works normally.
3. If no pre-feature save exists, skip this step.

- [ ] **Step 12.4: Cold-load and RAM check**

1. Quit and restart Unity (cold start).
2. Press Play. Walk around for ~10 s without opening the AI app.
3. Open Task Manager → confirm Unity Editor's memory hasn't ballooned (should be similar to a normal play session — model isn't loaded yet).
4. Open phone → AI app → type "hello".
5. Confirm: the typing indicator runs for 2–4 s, then the AI responds.
6. Open Task Manager → confirm Unity Editor's memory now shows a ~1.5–2 GB increase from the model.
7. Send a follow-up message. Confirm the second response starts within ~500 ms (model is warm now).

- [ ] **Step 12.5: Stub-app verification**

1. Phone → AI page → tap Notes, Codex, Calculator in turn.
2. Confirm each shows its "Coming soon — {label}" pill for ~1.5 s.

- [ ] **Step 12.6: Build sanity check (CRITICAL per CLAUDE.md MainMenu trap)**

1. File → Build Settings → Build (small dev build is fine).
2. Run the built `Solar System 2.exe`.
3. From the main menu, click PLAY.
4. Once in-game, open the phone → AI app → type a message → confirm the model loads and responds.
5. Save the game from pause.
6. Quit. Re-launch. LOAD the save. Confirm AI history persists.

This step catches any "auto-singleton not seeded for builds" bugs that the in-Editor flow misses.

- [ ] **Step 12.7: If everything passed, no commit needed**

If any step revealed a bug, fix it, commit the fix with a clear message, then re-run from Step 12.1.

---

## Out of scope reminder (do NOT add)

These were explicitly deferred in the spec — adding them now would expand scope:

- NPC AI dialogue (different system prompt per NPC, separate per-NPC memory store).
- A visible "AI memory inspector" UI.
- Real-time game state in the system prompt (player's inventory / location / NPCs met).
- Tool use / function calling.
- Multi-AI or per-NPC distinct voices.
- Localization.

---

## Files touched summary

**Created:**
- `.gitattributes` (or modified)
- `Assets/StreamingAssets/AI/.gitkeep`
- `Assets/StreamingAssets/AI/qwen2.5-1.5b-instruct-q4_k_m.gguf` (LFS)
- `Assets/3 - Scripts/AI/AIMemoryStore.cs`
- `Assets/3 - Scripts/AI/LLMService.cs`
- `Assets/3 - Scripts/AI/AIMemoryExtractor.cs`
- `Assets/3 - Scripts/AI/AIChatScreen.cs`
- `docs/superpowers/plans/2026-05-21-phone-ai-app-plan.md` (this file)

**Modified:**
- `Packages/manifest.json` (LLMUnity dependency)
- `Packages/packages-lock.json` (auto)
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` (new types + `aiState` field)
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` (capture + apply wiring)
- `Assets/3 - Scripts/UI/MainMenuController.cs` (singleton seeding)
- `Assets/3 - Scripts/UI/PlayerPhoneUI.cs` (page count, new page, EnterAIChat)
- `Assets/3 - Scripts/Scripts/Game/Controllers/PlayerController.cs` (input gate)

**NOT touched** (CLAUDE.md forbidden zone — verify before any further edit):
- Anything under `Assets/3 - Scripts/Scripts/Celestial/`
- `Assets/3 - Scripts/Scripts/Game/Atmosphere.cs`, `CustomImageEffect.cs`
- `Assets/3 - Scripts/Scripts/Post Processing/Planet Effects/PlanetEffects.cs`
- Any `.shader`, `.compute`, `.hlsl` under those folders
- Planet/ocean/atmosphere materials in `Assets/2 - Materials/`
