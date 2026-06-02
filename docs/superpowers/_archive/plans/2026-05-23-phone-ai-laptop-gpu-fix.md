# Phone AI: Laptop GPU Fallback + Mid-Stream NRE Fix

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Diagnose and fix the LLM backend running on CPU (tinyblas) instead of the RTX 4060 GPU (vulkan) in the built game, and stop the chat screen from throwing `NullReferenceException`s when the player closes the phone while the AI is still streaming a response.

**Architecture:** Two independent fixes:
1. **AIChatScreen NRE** — `OnSendClicked`'s `onToken` / `onComplete` lambdas capture `this`; if the screen is `Destroy()`'d before tokens arrive, calling `StartCoroutine` on the dead component throws. Add a destroyed-check at the top of each callback.
2. **CPU fallback** — LLMUnity's auto-selector tries GPU libs (cublas, vulkan), falls back to CPU (tinyblas) if both fail. The build picked tinyblas, so vulkan failed somewhere. LLMUnity's `[InitializeOnLoadMethod]` (which sets `libraryExclusion` to keep tinyblas out of the running) only fires in the **Editor** — in **builds** the runtime init only sets `baseLibraryPath` and leaves `libraryExclusion` empty, so tinyblas wins any race. Fix: (a) enable verbose LLMUnity logging so the next build's `Player.log` shows the exact vulkan failure, (b) set `libraryExclusion` in our own bootstrap so tinyblas is never tried when GPU is requested, (c) iterate on the root cause once the log reveals it.

**Tech Stack:** Unity 2022.3, LLMUnity v2.0.5 (`ai.undream.llm` package), LlamaLib v2.0.5 native runtime (cublas / vulkan / tinyblas DLLs in `Assets/StreamingAssets/LlamaLib-v2.0.5/win-x64/native/`), Hermes-3-Llama-3.1-8B Q4_K_M (~4.9 GB GGUF in `Assets/StreamingAssets/AI/`).

**Key files:**
- `Assets/3 - Scripts/AI/AIChatScreen.cs` — chat UI, the NRE source
- `Assets/3 - Scripts/AI/LLMService.cs` — owns the LLM component, currently sets `numGPULayers = 99` but doesn't constrain backend
- `Library/PackageCache/ai.undream.llm@2c30b44020/Runtime/LLMUnitySetup.cs` — read-only reference; shows `libraryExclusion` is only set in Editor, never in builds
- `Library/PackageCache/ai.undream.llm@2c30b44020/Runtime/LlamaLib/LlamaLib.cs` — read-only reference; selection logic at `LoadLibraries` / `TryNextLibrary`

---

## File Structure

**Modify:**
- `Assets/3 - Scripts/AI/AIChatScreen.cs` — add destroyed-checks at the top of `onToken` and `onComplete` lambdas inside `OnSendClicked` (file lines ~813-849).
- `Assets/3 - Scripts/AI/LLMService.cs` — in `EnsureModelLoadedAsync` (before the `new GameObject("LLM_Runtime")` block at line ~114), bootstrap LLMUnity debug logging + set `LlamaLib.libraryExclusion` to keep tinyblas off the candidate list when GPU is requested. Also log the selected architecture after `WaitUntilReady` so the choice is visible in `Player.log`.

**Do NOT modify:**
- Anything under `Library/PackageCache/ai.undream.llm@2c30b44020/` — that is the LLMUnity package and is regenerated from the Unity package manager. All fixes go in user code.
- `Assets/StreamingAssets/LlamaLib-v2.0.5/` — these are the native libs themselves. Don't add or remove them.

**No new files.**

---

## Phase 1 — UI NRE fix (no rebuild required, testable in Editor)

### Task 1: Guard AIChatScreen.OnSendClicked lambdas against post-destroy callbacks

**Why:** When the player closes the phone mid-response (Esc, back arrow, or close-phone hotkey), `AIChatScreen.Exit()` calls `Destroy(gameObject)`. The `onToken` / `onComplete` callbacks passed to `LLMService.Chat` still hold references to the destroyed `AIChatScreen`. When a token arrives a few seconds later (CPU model finally finishes), the callback runs `EnsureRevealLoop()` → `StartCoroutine(RevealLoop())` on a destroyed `MonoBehaviour` → `NullReferenceException`. The log fragment from the diagnosis is exactly this:
```
NullReferenceException
  at UnityEngine.MonoBehaviour.StartCoroutine(...)
  at AIChatScreen.EnsureRevealLoop ()
  at AIChatScreen+<>c__DisplayClass74_0.<OnSendClicked>b__0 (System.String tok)
```

**Files:**
- Modify: `Assets/3 - Scripts/AI/AIChatScreen.cs` (lines 813-849, inside `OnSendClicked`)

- [ ] **Step 1: Read the current `OnSendClicked` lambdas to confirm the line numbers**

Use the Read tool on `Assets/3 - Scripts/AI/AIChatScreen.cs` offset 779 limit 75. Confirm the `LLMService.Instance.Chat(msg, onToken: tok => { ... }, onComplete: full => { ... })` block matches the snippet below before editing.

- [ ] **Step 2: Add the destroyed-check at the top of `onToken`**

The Unity `==` overload for `Object` returns true when an Object has been destroyed (even though the C# reference is still non-null), so `this == null` is the correct guard. Combined with `!isActiveAndEnabled`, this no-ops both destroyed-but-not-yet-collected and disabled cases (the latter shouldn't happen in practice but is free insurance).

Edit `Assets/3 - Scripts/AI/AIChatScreen.cs` — find this block (around line 813):
```csharp
        LLMService.Instance.Chat(msg,
            onToken: tok =>
            {
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                if (aiLabel != null && _streamingLabel != aiLabel)
```

Replace with:
```csharp
        LLMService.Instance.Chat(msg,
            onToken: tok =>
            {
                // If the chat screen was destroyed (player closed the phone
                // before the response arrived) the captured `this` is a dead
                // Object. Unity's overloaded `==` treats destroyed Objects as
                // == null, so this is the canonical guard.
                if (this == null || !isActiveAndEnabled) return;
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                if (aiLabel != null && _streamingLabel != aiLabel)
```

- [ ] **Step 3: Add the same destroyed-check at the top of `onComplete`**

Find the `onComplete` lambda a few lines later (around line 828):
```csharp
            onComplete: full =>
            {
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                // Prefer the last streamed text; fall back to `full` if
```

Replace with:
```csharp
            onComplete: full =>
            {
                // Same destroyed-check as onToken. The final completion
                // callback also tries StartCoroutine via EnsureRevealLoop,
                // so it must guard too.
                if (this == null || !isActiveAndEnabled) return;
                if (_typingDotsRoutine != null) { StopCoroutine(_typingDotsRoutine); _typingDotsRoutine = null; }
                // Prefer the last streamed text; fall back to `full` if
```

- [ ] **Step 4: Verify the change compiles in the Editor**

Switch focus to the Unity Editor. Wait for the compilation indicator (bottom-right spinner) to finish. Open the Console (`Window > General > Console`). Expected: no compile errors. If there are errors, read them — most likely cause is a typo in the inserted lines.

- [ ] **Step 5: Manual reproduction test in Play mode**

In the Editor:
1. Press Play. Wait for the gameplay scene to load.
2. Open the phone (`P` by default — check `InputSettings` if remapped).
3. Open the AI app (the eye-icon page).
4. Type a question like "what should I do next?" and press Enter.
5. **Immediately** press the back arrow / Esc / close the phone before the AI replies (or while it's mid-reveal).
6. Wait ~10 seconds for the model response to arrive in the background.

Expected: no `NullReferenceException` in the Console. Before the fix, you would see the stack trace from the bug description. With the fix, the late token is silently dropped.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/AI/AIChatScreen.cs"
git commit -m "$(cat <<'EOF'
fix: guard AIChatScreen token callbacks against post-destroy NRE

Closing the phone mid-response left lambdas holding a reference to the
destroyed AIChatScreen; the next streamed token called StartCoroutine on
a dead MonoBehaviour and threw NRE every frame until the stream ended.
Add a `this == null || !isActiveAndEnabled` early-return at the top of
onToken and onComplete so late tokens silently drop instead of crashing.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2 — Diagnose the CPU fallback

### Task 2: Enable verbose LLMUnity logging in the build

**Why:** `LLMUnitySetup.DebugMode` is set per-PlayerPref by `LLMUnitySetup.LoadPlayerPrefs()` (called from `[InitializeOnLoadMethod]` in editor only). In builds, the runtime init at `LLMUnitySetup.InitializeOnLoad()` only calls `InitializeOnLoadCommon()`, which doesn't touch `DebugMode`. The default is `DebugModeType.All` which logs everything, **but** `LlamaLib.Debug(level)` is only called from inside `LLM.CreateLib()` based on the current `DebugMode` value — and that value is `DebugModeType.All` by default in the static field. We need to make sure the verbose `Trying ... / Successfully loaded ... / Failed to load library ...` lines from `TryNextLibrary` actually print, and that we can correlate them to `LLMService` startup in `Player.log`.

Looking at `LlamaLib.TryNextLibrary` (LlamaLib.cs:838 and 850), the `Trying ...` and `Successfully loaded: ...` lines only print when `debugLevelGlobal > 0`. The 850-line `Failed to load library {library}: {ex.Message}` ALSO needs `debugLevelGlobal > 0`. `LLM.CreateLib` sets `debugLevel = 1` for `DebugModeType.All` and `debugLevel = 5` for `DebugModeType.Debug`. Default in builds: `DebugMode = DebugModeType.All` (static initializer in `LLMUnitySetup.cs:169`), so `debugLevel = 1`. **The tries/fails SHOULD already log**, which matches the diagnosis's quoted log line.

So the work here is not to enable logging — it's already on — but to add **our own** marker logs that bookend the LLM startup so we can find the LLMUnity log block easily in `Player.log` (which is a wall of text) and assert what backend was chosen.

**Files:**
- Modify: `Assets/3 - Scripts/AI/LLMService.cs` (`EnsureModelLoadedAsync`, around line 99-175)

- [ ] **Step 1: Read the current `EnsureModelLoadedAsync` to confirm location**

Use Read on `Assets/3 - Scripts/AI/LLMService.cs` offset 99 limit 80. Confirm the structure matches what the edits below expect.

- [ ] **Step 2: Add a startup-marker log before LLM construction**

Find this block in `EnsureModelLoadedAsync` (around line 109):
```csharp
        // Create the LLM GameObject INACTIVE so its Awake doesnt fire
        // until we have configured SetModel. Otherwise LLM.Awake runs
        // StartServiceAsync immediately, validates the empty `model`
        // field, logs "No model file provided!" and sets `failed=true` —
        // any subsequent WaitUntilReady then throws "LLM failed to start".
        var llmGO = new GameObject("LLM_Runtime");
```

Insert above it:
```csharp
        // === LLM BACKEND DIAGNOSTIC MARKER (start) ===
        // Bookends the LLMUnity initialization block in Player.log so the
        // 'Trying ... / Successfully loaded ...' lines from LlamaLib can
        // be located quickly. If the architecture printed in the matching
        // END marker below is anything other than vulkan or cublas, the
        // game is running the model on CPU and the build needs the GPU
        // dependency investigation in the plan's Task 4.
        Debug.Log($"[LLMService] === BACKEND PROBE START === platform={Application.platform}, " +
                  $"streamingAssetsPath={Application.streamingAssetsPath}, " +
                  $"sysGPU={SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                  $"sysVRAM={SystemInfo.graphicsMemorySize}MB");

        // Create the LLM GameObject INACTIVE so its Awake doesnt fire
```

- [ ] **Step 3: Add the end-marker log that prints the architecture LLMUnity actually selected**

Find this block (around line 172):
```csharp
        // WaitUntilReady() is the official async "wait for started" API:
        //   public async Task WaitUntilReady() { while (!started && !failed)
        //       await Task.Yield(); if (failed) LogError(..., true); }
        await _llm.WaitUntilReady();

        _modelReady = true;
    }
```

Replace with:
```csharp
        // WaitUntilReady() is the official async "wait for started" API:
        //   public async Task WaitUntilReady() { while (!started && !failed)
        //       await Task.Yield(); if (failed) LogError(..., true); }
        await _llm.WaitUntilReady();

        // === LLM BACKEND DIAGNOSTIC MARKER (end) ===
        // LLM.architecture is the filename stem of the native lib that
        // LlamaLib picked. Anything containing 'tinyblas', 'avx', or 'noavx'
        // means CPU inference — bad on a GPU-equipped machine. 'vulkan' or
        // 'cublas' means GPU.
        string arch = _llm != null ? _llm.architecture : "<null>";
        bool onGPU = arch != null && (arch.Contains("vulkan") || arch.Contains("cublas"));
        if (onGPU)
            Debug.Log($"[LLMService] === BACKEND PROBE END === arch={arch} (GPU). All good.");
        else
            Debug.LogWarning($"[LLMService] === BACKEND PROBE END === arch={arch} (CPU FALLBACK). " +
                             $"This run will be very slow on the 8B model. " +
                             $"Search Player.log above for 'Trying ' and 'Failed to load library' to see " +
                             $"why the GPU lib was rejected. See docs/superpowers/plans/2026-05-23-phone-ai-laptop-gpu-fix.md Task 4.");

        _modelReady = true;
    }
```

Note: `LLM.architecture` is a public field on the LLMUnity `LLM` component (verified in `LlamaLib.cs:849` where it's set after a successful load). Accessible from `LLMService` since both are in the same `LLMUnity` namespace via `using LLMUnity;` at the top of the file.

- [ ] **Step 4: Verify the change compiles in the Editor**

Switch to the Editor and let it compile. Expected: no errors. If `_llm.architecture` is private or has a different name, the compiler will say so; in that case, drop it to `_llm != null ? _llm.GetType().ToString() : "<null>"` as a fallback and grep the LLMUnity package source for the right accessor.

- [ ] **Step 5: Sanity-check the markers print in Editor Play mode**

1. Press Play in the Editor.
2. Open the phone, open AI, send a message.
3. In the Editor Console, search (use the Console search box) for `BACKEND PROBE`.

Expected: two lines, START and END. In the Editor on the desktop machine that ran the original build successfully, END should show a GPU architecture. The Editor uses the same library selection as a build, so this also tells us if vulkan works in this Unity install at all.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/AI/LLMService.cs"
git commit -m "$(cat <<'EOF'
diag: log LLM backend choice + GPU info around model load

Adds === BACKEND PROBE START === / === BACKEND PROBE END === markers
around the LLM init so Player.log on the laptop build shows exactly
which native library LLMUnity selected, alongside Unity's reported GPU.
If the END marker reports a CPU arch (tinyblas/avx/noavx), the warning
links back to the laptop-GPU-fix plan for next steps.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 3: Constrain the candidate library list so tinyblas can never win when GPU is requested

**Why:** `LLMUnitySetup.cs:266` sets `LlamaLib.libraryExclusion = new List<string>(){"tinyblas"}` (when CUBLAS=true) or `{"cublas"}` (when CUBLAS=false) — but only inside `[InitializeOnLoadMethod]`, which runs in the Editor only. In builds the runtime init at line 272-276 does NOT set `libraryExclusion`, so the list stays at its static default `new List<string>()`. That means in a build with CUBLAS=false (the project default per `LLMUnitySetup.cs:171`), every shipped library is a candidate — and if vulkan fails, the loader silently falls all the way to tinyblas.

We can fix this from user code by setting `LlamaLib.libraryExclusion` ourselves before `LLM.Awake` runs. With `{"tinyblas", "noavx", "avx", "avx2", "avx512"}` excluded, the candidate list becomes just `{cublas, vulkan}` (cublas isn't shipped at build time when CUBLAS=false, so FindLibrary throws and we end up at vulkan). If vulkan also fails to load, the LLM fails to start outright — which is the **correct** failure mode: better to show an error than silently run at 3 tokens/sec.

**Files:**
- Modify: `Assets/3 - Scripts/AI/LLMService.cs` (`EnsureModelLoadedAsync`, just below the new diagnostic-start marker)

- [ ] **Step 1: Add the exclusion list before the LLM GameObject is created**

In `Assets/3 - Scripts/AI/LLMService.cs`, find the new diagnostic-start log line you added in Task 2, Step 2 (the one starting `"[LLMService] === BACKEND PROBE START ==="`). Insert this block immediately after that log:

```csharp
        // Force the LLMUnity loader to skip every CPU backend. With these
        // excluded, the candidate list becomes [cublas, vulkan] only — and
        // if both fail, the LLM start will fail loudly instead of silently
        // demoting to a 3 tok/sec CPU fallback (which is what produced the
        // "AI takes forever" laptop build report).
        //
        // Why we have to do this in user code: LLMUnitySetup only sets
        // libraryExclusion in [InitializeOnLoadMethod] (Editor-only). In
        // builds the runtime init leaves the list empty, so any shipped
        // CPU lib is a viable fallback.
        //
        // NOTE: This is a `static` field on LlamaLib, so the assignment
        // sticks for the lifetime of the process — fine, the singleton
        // only loads once per session.
        LLMUnity.LlamaLib.libraryExclusion = new System.Collections.Generic.List<string>
        {
            "tinyblas",
            "noavx",
            "avx",      // matches avx, avx2, avx512 (Contains-substring match in LlamaLib.cs:764)
        };
        Debug.Log($"[LLMService] libraryExclusion set to: {string.Join(", ", LLMUnity.LlamaLib.libraryExclusion)}");
```

Verify the insertion location: the file should now read, in order:
1. The `=== BACKEND PROBE START ===` log
2. The new `libraryExclusion` assignment + log
3. The existing `var llmGO = new GameObject("LLM_Runtime");` line

- [ ] **Step 2: Verify the substring-match semantics by reading the package source**

Use the Read tool on `Library/PackageCache/ai.undream.llm@2c30b44020/Runtime/LlamaLib/LlamaLib.cs` offset 760 limit 14. Confirm that the exclusion loop uses `libraryLower.Contains(exclusionKeyword)` (so "avx" alone matches "avx2" and "avx512"). If the package source has changed and uses exact-match, expand the list to `{"tinyblas", "noavx", "avx", "avx2", "avx512"}`.

- [ ] **Step 3: Verify in Editor that the LLM still starts and the backend marker reports vulkan**

In Unity Editor:
1. Press Play.
2. Open the phone, open AI, send any message.
3. In the Console, search for `BACKEND PROBE END`.

Expected: `arch=llamalib_win-x64_vulkan` (or similar). If you instead see "LLM failed to start" or an exception from LlamaLib, vulkan is genuinely broken on this machine and Task 4 (root-cause investigation) is required immediately. Don't proceed to commit until you understand which case you're in.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/AI/LLMService.cs"
git commit -m "$(cat <<'EOF'
fix: exclude CPU backends from LLMUnity's library candidate list

LLMUnitySetup only populates LlamaLib.libraryExclusion in
[InitializeOnLoadMethod] (Editor-only). In builds the exclusion list is
empty, so when both GPU libs (cublas+vulkan) fail to load, the auto-
selector silently demotes to tinyblas/avx CPU inference — that's what
produced the "AI takes forever" report on the laptop build.

Set the exclusion list ourselves in EnsureModelLoadedAsync before the
LLM component is constructed, with all CPU variants excluded. Result:
if vulkan also fails, the LLM fails loudly instead of running the 8B
model at ~3 tok/sec.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 3 — Rebuild and root-cause the vulkan failure (laptop)

### Task 4: Rebuild and capture Player.log from the laptop build

**Why:** With Phase 2's diagnostic markers in place, the next build's `Player.log` will tell us whether vulkan now succeeds (i.e. the editor-vs-build `libraryExclusion` gap was the entire problem) or whether vulkan is genuinely failing to load on the laptop (which means we need a deeper fix: shipping VC++ redistributables, switching to CUBLAS, or downgrading to partial GPU offload).

**Files:** None — this is a build + log capture task.

- [ ] **Step 1: Make a Unity build**

In the Unity Editor on the laptop:
1. `File > Build Settings...`
2. Confirm `MainMenu.unity` is enabled and is the first scene, then `1.6.7.7.7.unity` is enabled second. `1.8.unity` and the cinematic scenes should remain disabled (per CLAUDE.md).
3. Click `Build And Run`. Pick the existing build output folder so it overwrites in place (default: wherever `Solar System 2.exe` lives).

Expected: Unity compiles, builds, and launches the .exe.

- [ ] **Step 2: Trigger an LLM startup in the running build**

In the running game:
1. Click PLAY on the main menu.
2. Once loaded, open the phone, open the AI app, and send any message.
3. Wait for either a response, or an obvious failure (e.g. error popup, the chat showing "[The entity is silent.]").
4. Close the game.

The LLM auto-loads on scene entry via `LLMService.Awake → BeginPreload`, so even before you open the AI app, the load attempt has fired and logged.

- [ ] **Step 3: Capture Player.log**

Player.log lives at `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Solar System 2\Player.log`. Either:
- Open it in Notepad and copy the lines from `=== BACKEND PROBE START ===` to `=== BACKEND PROBE END ===` (plus a few hundred lines after, in case of errors), OR
- Copy the file aside: `copy "%USERPROFILE%\AppData\LocalLow\DefaultCompany\Solar System 2\Player.log" .\player_log_capture.txt` (in PowerShell: `Copy-Item "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\Solar System 2\Player.log" .\player_log_capture.txt`).

- [ ] **Step 4: Identify the failure mode**

Look at the lines between the START and END markers. Match what you see against the decision tree below — each branch links to a specific Task 5 sub-fix.

| What `Player.log` shows | Diagnosis | Next task |
|---|---|---|
| END marker says `arch=llamalib_win-x64_vulkan` and AI responses are fast (~30 tok/s on Hermes 8B) | Phase 2's exclusion list was the whole fix. | **Done — skip to Task 6.** |
| `Failed to load library llamalib_win-x64_vulkan.dll: ...vulkan-1.dll...not found` | vulkan-1.dll missing from build output | **Task 5a** |
| `Failed to load library llamalib_win-x64_vulkan.dll: ...not a valid Win32 application` or any LoadLibrary system error | vulkan-1.dll present but wrong arch / missing VC++ runtime | **Task 5b** |
| Vulkan loaded, then `LLMService_Supports_GPU` returned false / `is_gpu_library && !LLMService_Supports_GPU() ... continue` | GPU detected as missing by LlamaLib — driver-level issue | **Task 5c** |
| Vulkan loaded but `CreateLLMWithFallback` threw on first CreateLLM, then "Failed LLMService construction with all available libraries" | VRAM exhaustion / model too big for GPU | **Task 5d** |
| END marker says `arch=llamalib_win-x64_vulkan` but the LLM `failed=true` later in WaitUntilReady | Server process failed to launch — likely missing server.exe dependency | **Task 5e** |

- [ ] **Step 5: Commit the log capture for reference**

Save `player_log_capture.txt` to `docs/superpowers/plans/notes/2026-05-23-laptop-player-log.txt` (create the `notes/` folder if it doesn't exist) so the failure trace is preserved alongside the plan.

```bash
mkdir -p "docs/superpowers/plans/notes"
mv player_log_capture.txt "docs/superpowers/plans/notes/2026-05-23-laptop-player-log.txt"
git add "docs/superpowers/plans/notes/2026-05-23-laptop-player-log.txt"
git commit -m "$(cat <<'EOF'
diag: capture laptop Player.log for LLM backend probe

Saved the BACKEND PROBE block from the laptop build's Player.log for
reference. See plan Task 4 / Task 5 for the decision tree on how to
read it.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 5: Apply the targeted fix based on Task 4's diagnosis

Only run the sub-task that matches your Task 4 finding. The others are alternatives.

#### Task 5a: Ensure vulkan-1.dll is included in the build

- [ ] **Step 1: Confirm vulkan-1.dll is in StreamingAssets**

Verify the file exists in the project: `Assets/StreamingAssets/LlamaLib-v2.0.5/win-x64/native/vulkan-1.dll`. Use the Glob tool with pattern `Assets/StreamingAssets/LlamaLib-v2.0.5/win-x64/native/vulkan-1.dll` — if the result is empty, the file was deleted at some point and needs to be restored.

If missing: restore by re-importing the LLMUnity package (`Window > Package Manager > LLM for Unity > Re-import`). LLMUnity's downloader will repopulate StreamingAssets.

- [ ] **Step 2: Confirm vulkan-1.dll made it into the build output**

After Task 4's build, navigate to the build output folder. The file should be at `Solar System 2_Data\StreamingAssets\LlamaLib-v2.0.5\win-x64\native\vulkan-1.dll`. If it's in the project but not the build output, Unity excluded it during build — check whether the meta file marks it as a Plugin (which excludes it from StreamingAssets) instead of a normal asset.

To check: open `Assets/StreamingAssets/LlamaLib-v2.0.5/win-x64/native/vulkan-1.dll.meta` in a text editor and confirm `PluginImporter:` is not the top key. If it is, the file is being imported as a Plugin and StreamingAssets won't ship it — re-import as a normal binary asset.

- [ ] **Step 3: Rebuild + re-test (Task 4 Steps 1-3)**

Re-run the build, re-trigger the LLM startup, capture Player.log again. The vulkan load should now succeed.

- [ ] **Step 4: Commit any meta-file fix**

```bash
git add "Assets/StreamingAssets/LlamaLib-v2.0.5/win-x64/native/vulkan-1.dll.meta"
git commit -m "$(cat <<'EOF'
fix: ship vulkan-1.dll into the Windows build's StreamingAssets

LLMUnity's vulkan native lib depends on vulkan-1.dll at runtime; without
it the build silently fell back to tinyblas CPU inference.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

#### Task 5b: Install Visual C++ runtime / verify arch

- [ ] **Step 1: Install Microsoft Visual C++ Redistributable 2015-2022 (x64) on the laptop**

Download from `https://aka.ms/vs/17/release/vc_redist.x64.exe` and run. This installs the MSVC runtime that vulkan-1.dll and the llamalib natives link against.

- [ ] **Step 2: Re-run the build**

Launch the existing `Solar System 2.exe` again — no rebuild required, this is a runtime dependency. Trigger LLM startup. Check Player.log.

Expected: vulkan now loads successfully and the BACKEND PROBE END marker reports `arch=llamalib_win-x64_vulkan`.

- [ ] **Step 3: Note the dependency in CLAUDE.md**

Add a note under the Project overview's Unity workflow section so future-you knows builds require the MSVC redist on target machines. Edit `CLAUDE.md` and add this line after the "Save files" line in the Unity workflow section:

```markdown
- The phone AI's GPU inference path (vulkan native lib) requires the Microsoft Visual C++ Redistributable 2015-2022 x64 on the target machine. If a build silently falls back to CPU inference, install `vc_redist.x64.exe` and check Player.log for "BACKEND PROBE END".
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "$(cat <<'EOF'
docs: note VC++ redist requirement for phone AI GPU path

The vulkan native lib needs the Microsoft VC++ Redistributable 2015-2022
x64 to load. Without it, builds silently fall back to CPU inference and
the AI runs at ~3 tok/sec on the 8B model.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

#### Task 5c: GPU not detected by LlamaLib — switch to CUBLAS

- [ ] **Step 1: Install CUDA Toolkit 12.x runtime on the laptop**

Download from `https://developer.nvidia.com/cuda-12-4-1-download-archive` (or whichever 12.x version is current). The full toolkit is overkill — only the runtime libraries (cudart64_12.dll, cublasLt64_12.dll, cublas64_12.dll) are needed by LLMUnity's cublas backend. Install with default options.

- [ ] **Step 2: Enable CUBLAS in LLMUnity PlayerPrefs**

In the Unity Editor: open the Project Settings or the LLMUnity inspector and call `LLMUnitySetup.SetCUBLAS(true)`. The cleanest way: add a one-shot Editor script.

Create file `Assets/Editor/EnableCUBLAS.cs`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using LLMUnity;

public static class EnableCUBLAS
{
    [MenuItem("Tools/LLM/Enable CUBLAS (GPU via CUDA)")]
    public static void Enable()
    {
        LLMUnitySetup.SetCUBLAS(true);
        UnityEngine.Debug.Log("CUBLAS enabled — rebuild to ship cublas DLLs.");
    }

    [MenuItem("Tools/LLM/Disable CUBLAS (fall back to vulkan)")]
    public static void Disable()
    {
        LLMUnitySetup.SetCUBLAS(false);
        UnityEngine.Debug.Log("CUBLAS disabled — rebuild to ship tinyblas+vulkan.");
    }
}
#endif
```

Then in Unity: `Tools > LLM > Enable CUBLAS (GPU via CUDA)`.

- [ ] **Step 3: Update the exclusion list in LLMService**

With CUBLAS=true, the LLMUnity build process ships cublas DLLs but ALSO ships tinyblas. We want tinyblas excluded so cublas is the only option. Our Task 3 fix already excludes tinyblas — so no change needed. **Verify** by reading `LLMService.cs` around the libraryExclusion block and confirming "tinyblas" is in the list.

- [ ] **Step 4: Rebuild + re-test**

Re-run Task 4 Steps 1-3. The BACKEND PROBE END marker should now report `arch=llamalib_win-x64_cublas`.

- [ ] **Step 5: Commit the Editor helper**

```bash
git add "Assets/Editor/EnableCUBLAS.cs"
git commit -m "$(cat <<'EOF'
tooling: menu helpers to toggle LLMUnity CUBLAS preference

LLMUnitySetup.SetCUBLAS is the only way to swap between cublas (CUDA)
and tinyblas+vulkan, and it's hidden in PlayerPrefs. Expose it as
Tools menu items so the choice is discoverable and a future "why is
the AI on CPU" investigation can flip it from the menu.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

#### Task 5d: VRAM exhaustion — reduce numGPULayers

- [ ] **Step 1: Calculate VRAM headroom**

Hermes-3 8B Q4_K_M is ~4.9 GB on disk and uses ~5 GB resident weights + ~1 GB KV cache at 8192 ctx = ~6 GB total. The laptop's RTX 4060 has 8 GB VRAM — should fit, but Windows desktop compositor + other GPU users eat ~1-2 GB, leaving 6-7 GB free. If the build's first inference call OOMs the GPU, vulkan throws and we fall back.

Open Task Manager > Performance > GPU and note VRAM in use before launching the game. If it's already over 2 GB before launch, you're at the edge.

- [ ] **Step 2: Reduce numGPULayers from 99 to 32**

Edit `Assets/3 - Scripts/AI/LLMService.cs`. Find:

```csharp
        _llm.numGPULayers = 99;
```

Replace with:

```csharp
        // 8B model has 32 layers; offload 32 (full) fits in 8 GB VRAM only
        // with no other GPU pressure. If the build OOMs the GPU, drop to
        // 24 (~75% offload, last 8 layers run on CPU). Below 16 the gain
        // over pure CPU is minimal.
        _llm.numGPULayers = 32;
```

- [ ] **Step 3: Rebuild + re-test**

Re-run Task 4 Steps 1-3. If vulkan now loads successfully, perfect. If it still fails, drop further to 24 and rebuild again.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/AI/LLMService.cs"
git commit -m "$(cat <<'EOF'
fix: cap numGPULayers at 32 for Hermes-3 8B on 8GB VRAM cards

99 was 'as many as the model has' — same as 32 for this model — but
made debugging harder. Stating the actual layer count up-front so a
future drop-to-partial-offload is one line away if the build OOMs the
GPU on a more contested laptop card.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

#### Task 5e: Server executable launch failure

- [ ] **Step 1: Identify which server.exe LLMUnity is trying to launch**

In `Library/PackageCache/ai.undream.llm@2c30b44020/Runtime/LLM.cs` around line 514-516 there's a hardcoded server filename: `llamalib_win-x64_server.exe`. Confirm this file exists in the build output's `Solar System 2_Data\StreamingAssets\LlamaLib-v2.0.5\win-x64\native\`.

- [ ] **Step 2: Try launching the server.exe manually from PowerShell**

```powershell
cd "C:\path\to\Solar System 2_Data\StreamingAssets\LlamaLib-v2.0.5\win-x64\native"
.\llamalib_win-x64_server.exe --help
```

Expected: a help message. If you instead get "This app can't run on your PC" or "missing DLL", you have a redist issue (see Task 5b) or a 32-vs-64-bit mismatch.

- [ ] **Step 3: Apply the appropriate fix**

Based on what `.exe --help` shows: install VC++ redist (Task 5b), reinstall the LLMUnity package (Task 5a Step 1), or report the underlying error to the LLMUnity project. Document the resolution in the commit message.

---

## Phase 4 — Verify and harden

### Task 6: Verify Hermes-3 8B runs at GPU speed on the laptop build

**Why:** With the right backend selected, the laptop's RTX 4060 should hit 30+ tokens/sec on Hermes-3 8B Q4_K_M. The diagnosis cited "30+ tok/s" as the target. Anything under 10 tok/s means the model is still on CPU or partial-offload.

**Files:** None — this is a performance test.

- [ ] **Step 1: Time a fresh response**

Launch the build. Open the AI app. Send a question that produces a ~50-word response, e.g. "Give me a quick status report on my situation right now."

Use a stopwatch (or screen-record) to measure: time from pressing Enter to the response landing fully (ignoring the paced-reveal slowdown — the reveal coroutine is locally throttled at 40 chars/sec, so you'll see the response appear smoothly; what you're measuring is the underlying stream completion).

Easier: check `Player.log` for the `[LLMService] Chat returned. tokens streamed=N, full.Length=M` line. Divide N by the elapsed time.

Expected on RTX 4060 vulkan: 30-50 tok/s. Expected on CPU fallback: 2-5 tok/s.

- [ ] **Step 2: Confirm the BACKEND PROBE END marker matches expectations**

In Player.log, the END marker should now report a GPU architecture. If it still says tinyblas/avx, Phase 3 failed — go back and re-diagnose.

- [ ] **Step 3: Sanity-check the NRE fix still applies in the build**

Send a message, immediately back-arrow out of the AI screen, close the phone, wait 30 seconds. Check Player.log for `NullReferenceException` — there should be none referencing `AIChatScreen.EnsureRevealLoop`.

- [ ] **Step 4: Document the verified working configuration**

Edit `CLAUDE.md` to add a note in the Coding conventions section (or under Project overview) about the verified phone-AI backend on this machine:

```markdown
- **Phone AI backend (verified on laptop, 2026-05-23):** Hermes-3-Llama-3.1-8B Q4_K_M running on RTX 4060 8GB via [vulkan | cublas]. CPU fallback is explicitly disabled by `LlamaLib.libraryExclusion = {"tinyblas", "noavx", "avx"}` set in `LLMService.EnsureModelLoadedAsync`. If the LLM fails to start on a build, check `Player.log` for the `=== BACKEND PROBE END ===` marker and follow `docs/superpowers/plans/2026-05-23-phone-ai-laptop-gpu-fix.md` Task 4's decision tree.
```

(Pick `vulkan` or `cublas` based on Task 4's findings, and remove the bracketed alternative.)

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "$(cat <<'EOF'
docs: record verified phone-AI GPU backend in CLAUDE.md

After resolving the silent CPU fallback, document which backend the
laptop build actually uses and point future debugging at the diagnostic
markers in LLMService.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

### Task 7: Optional — add a UI affordance for the backend-failure case

**Why:** With CPU fallbacks disabled by Task 3, if vulkan ever fails on a new target machine, `_llm.WaitUntilReady` will throw and the AI chat will be silently broken. A user-visible message is friendlier than `[The entity is silent.]`.

This is optional polish — only do it if you intend to ship the build to other people.

**Files:**
- Modify: `Assets/3 - Scripts/AI/LLMService.cs` (`EnsureModelLoadedAsync`)

- [ ] **Step 1: Wrap WaitUntilReady in a try/catch and set a static error message**

Add to `LLMService` (anywhere convenient, e.g. just under the `IsResponding` property at line 49):

```csharp
    // Non-empty when the LLM failed to start. AIChatScreen surfaces this
    // to the player as an in-bubble error so they understand the AI isn't
    // responding because of a backend problem, not because the model just
    // happens to be slow.
    public static string BackendStartupError { get; private set; }
```

Then change the WaitUntilReady block in `EnsureModelLoadedAsync` from:

```csharp
        await _llm.WaitUntilReady();
```

to:

```csharp
        try
        {
            await _llm.WaitUntilReady();
        }
        catch (Exception e)
        {
            BackendStartupError =
                "AI failed to start. The GPU backend (vulkan/cublas) did not load. " +
                "See Player.log for the '=== BACKEND PROBE END ===' marker. " +
                $"Underlying error: {e.GetType().Name}: {e.Message}";
            Debug.LogError("[LLMService] " + BackendStartupError);
            throw;
        }
```

- [ ] **Step 2: Display the error in AIChatScreen on first send when the backend is broken**

In `Assets/3 - Scripts/AI/AIChatScreen.cs`, at the top of `OnSendClicked` (line 779), after the `if (LLMService.Instance.IsResponding) return;` check, insert:

```csharp
        // Backend failed to start — surface that as a bubble so the player
        // knows the AI isn't just slow, it's actively broken on this build.
        if (!string.IsNullOrEmpty(LLMService.BackendStartupError))
        {
            var errLabel = AddAIBubble("");
            StartPacedReveal(errLabel, LLMService.BackendStartupError);
            return;
        }
```

- [ ] **Step 3: Verify in Editor**

Temporarily force the error by changing `libraryExclusion` in `LLMService.cs` to also exclude `"vulkan"` and `"cublas"`:

```csharp
        LLMUnity.LlamaLib.libraryExclusion = new System.Collections.Generic.List<string>
        {
            "tinyblas", "noavx", "avx", "vulkan", "cublas",
        };
```

Press Play, open AI, send a message. Expected: the error bubble appears. Then revert the temporary `vulkan, cublas` additions.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/AI/LLMService.cs" "Assets/3 - Scripts/AI/AIChatScreen.cs"
git commit -m "$(cat <<'EOF'
feat: surface LLM backend startup failure to the chat UI

With CPU fallbacks excluded, a vulkan/cublas failure now means the AI
won't respond at all. Catch the WaitUntilReady exception and stash a
user-facing message in LLMService.BackendStartupError; AIChatScreen
shows it on first send so the player understands the AI is broken, not
slow.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-review notes

**Spec coverage check (against the diagnosis):**
- ✅ Root cause #1 (CPU fallback) — Task 2 (logging), Task 3 (exclude CPU libs), Task 4-5 (rebuild + targeted fix per failure mode)
- ✅ Root cause #2 (NRE in AIChatScreen) — Task 1
- ✅ Diagnosis's "What to do for this build as-shipped" — partially: switching the in-component model isn't a runnable option since the LLMService hardcodes the model path. The plan instead rebuilds from the project source, which is the more durable answer. The "keep the screen open" advice is a workaround the player can use today without any code changes.
- ✅ Diagnosis's "rebuild with Vulkan/CUDA" recommendation — covered by Tasks 3-5
- ✅ Diagnosis's "guard streaming callbacks with `if (this == null || !isActiveAndEnabled)`" — covered by Task 1 verbatim

**Type consistency:**
- `LlamaLib.libraryExclusion` is `static List<string>` (confirmed at LlamaLib.cs:642) — Task 3 assigns to it via the same fully-qualified name.
- `LLM.architecture` referenced in Task 2 — confirmed at LlamaLib.cs:849 where it's set in `TryNextLibrary`. (Note: the field actually lives on the LlamaLib instance, exposed via the LLM component — if compile errors say it's not a member of LLM, switch the access to `_llm.llmlib.architecture` or whatever the public path is. Task 2 Step 4's note covers this fallback.)
- `LLMService.BackendStartupError` introduced in Task 7 — referenced from `AIChatScreen.OnSendClicked` in the same task. Consistent.

**Placeholder scan:** No "TBD", no "implement appropriate error handling". Every code change shows the exact code. The decision tree in Task 4 has concrete next-task pointers.

**File-path correctness:** All `Assets/3 - Scripts/AI/...` paths confirmed via Glob; `Library/PackageCache/ai.undream.llm@2c30b44020/` is read-only reference, never edited; `docs/superpowers/plans/notes/` is a new folder created by Task 4 with mkdir.
