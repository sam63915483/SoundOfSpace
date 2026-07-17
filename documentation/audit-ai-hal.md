# Audit: AI (HAL / IntentRouter / LLMService)

Read-only audit of the phone-AI companion subsystem, 2026-07-15.
Files reviewed in full:
- `Assets/3 - Scripts/AI/LLMService.cs`
- `Assets/3 - Scripts/AI/IntentRouter.cs`
- `Assets/3 - Scripts/AI/HALCommentator.cs`
- `Assets/3 - Scripts/AI/HALToolDispatcher.cs`
- `Assets/3 - Scripts/AI/FleetTelemetry.cs`
- Supporting reads: `AIMemoryExtractor.cs`, `AIMemoryStore.cs`, `AIChatScreen.cs` (call sites), `PlayerPhoneUI.cs` (call site).

---

## Summary

The LLM is disabled by three guards: `BeginPreload()` early-returns
(`LLMService.cs:137`), `Chat()` early-returns on `!IsModelAvailable`
(`LLMService.cs:337`), and `AIChatScreen` skips the whole AI-bubble flow on
`!IsModelAvailable` (`AIChatScreen.cs:1118`). Because `IsModelAvailable` requires
`_modelReady`, which is only ever set inside `EnsureModelLoadedAsync()` (which is
now only reachable from the dead paths), the model **never loads and every
downstream helper in `Chat()` is unreachable at runtime.**

Key structural finding the task framing understates: **the IntentRouter and the
HALToolDispatcher *action* paths are also dead right now.** `IntentRouter.TryAnswer`
has exactly one caller — `LLMService.Chat:357` — which is behind the disabled
guard. `HALToolDispatcher.Execute` / `ResolveTarget` are only called from
IntentRouter (dead) and `DispatchPendingToolCalls` (dead). So no HAL waypoint is
ever placed, and `HALToolDispatcher.TickProximity()` (live, called every
`HALCommentator.Update`) always no-ops on `_activeWaypoints.Count == 0`. The only
genuinely live AI code is **`HALCommentator`** (volunteered/templated lines →
HUD/voice/log) and the classes it drives (`FleetTelemetry`, `HALVoicePlayer`,
`HALLineHUD`, `HALVolunteeredLog`).

The most important defect is a **latent model-load crash path** (Bug 1): the
LLM-disabled guard is missing from `EnsureModelLoadedAsync()` and `OneShotAsync()`,
and a restored save can re-arm `dirtyForExtraction`, so `AIMemoryExtractor` can
still try to load the deleted `.gguf`.

---

## Bugs (severity, file:line, description, fix)

### Bug 1 — HIGH (latent). LLM-disabled guard missing from the extraction path; a restored save can trigger a load of the deleted model.
`LLMService.cs:520` `OneShotAsync()` → `LLMService.cs:522` `await EnsureModelLoadedAsync()`.
Neither `OneShotAsync` nor `EnsureModelLoadedAsync` (`LLMService.cs:169`) has the
`return;` guard that `BeginPreload` (line 137) and `Chat` (line 337) have.
`AIMemoryExtractor.RunAsync` (`AIMemoryExtractor.cs:41`, `:159`) calls
`OneShotAsync`, fire-and-forget from `AIChatScreen.cs:1447` on chat close, gated
only by `store.DirtyForExtraction`.
Today `RecordTurn` (the only setter of that flag) is only reached from the dead
`Chat` paths (`LLMService.cs:363/392`) and the dead `AIChatScreen.cs:1191`, so the
flag stays false in a fresh session — **except** `AIMemoryStore.Restore` copies it
straight from the save (`AIMemoryStore.cs:183`). Loading an autosave written during
the LLM era with `dirtyForExtraction=true` makes `RunAsync` call
`EnsureModelLoadedAsync`, which builds the `LLM_Runtime` GameObject and calls
`SetModel(ModelStreamingPath)` on a `.gguf` that no longer exists — exactly the
"mmap a non-existent file / stall during native VRAM teardown" crash that the
`BeginPreload` comment (lines 130-137) says the disable was meant to prevent.
**Fix:** add the same early-return at the top of `EnsureModelLoadedAsync()` (or
have it throw a caught no-op), so every model-load entry point is guarded, not just
two of three. Cheapest: `if (true) return;` mirroring `BeginPreload`, or gate on a
single shared `LlmDisabled` const.

### Bug 2 — LOW. Diagnostic slice reads the wrong block; end-marker no longer exists in the prompt.
`LLMService.cs:1028` `ExtractBlock(systemPrompt, "FLEET STATE (live", "RULE — SHIP VALUES")`.
The "RULE — SHIP VALUES" block was deleted from `BuildSystemPrompt` (see the comment
at `LLMService.cs:642-659` that replaced it with a one-sentence note). `ExtractBlock`
(`:1035`) falls back to `e = text.Length` when the end marker is absent, so the
"FLEET STATE" diagnostic dumps everything from FLEET STATE to the end of the prompt
(canon + lore + memories + standing), not just the fleet slice. Only affects the
debug log, and only on the dead `Chat` path. **Fix:** change the end marker to a
string that still exists after FLEET STATE (e.g. `"ESTABLISHED FACTS"` or
`"If you ever need to reference"`), or delete `LogPromptDiagnostics` with the rest
of the dead LLM code.

### Bug 3 — LOW. Stale comments: "within 10 m" vs 35 m constant.
`HALToolDispatcher.cs:50`, `:74` both say the waypoint auto-clears "within 10 m",
but `WaypointReachedRadiusMeters = 35f` (`:63`). Comment-only; behaviour is 35 m.

### Bug 4 — LOW (correctness of a dead path). `TryMissionProgress` negative gate swallows legitimate "what should I do" queries.
`IntentRouter.cs:333-337`: any message containing `"who"`, `"what is"`, `"why"`,
`"office"`, etc. returns null to defer to the LLM. With the LLM disabled there is no
fallback, so e.g. "what should I do to fix the ship" (`"fix"` is fine, but
"what should I do, who do I talk to" contains `"who"`) silently produces no answer.
This is by-design routing that assumed a live LLM behind it; it becomes a dead-end
now. Flagging for whoever re-wires the preset-dialogue replacement — the negative
gate needs a preset fallback, not `null`.

---

## Dead Code / Deletable LLM weight

Everything below is unreachable while the LLM is disabled. It splits into "safe to
delete now" and "keep, it's the intended re-enable surface." The code is *preserved
on purpose* per the `BeginPreload` comment (`LLMService.cs:136`), so deletion is a
judgment call — but the following are the pieces that are pure LLM plumbing with no
non-LLM reuse:

**Pure LLM plumbing (no live caller, deletable if the LLM path is abandoned):**
- `LLMService.EnsureModelLoadedAsync` (`:169-323`) — model construction, backend
  probe, `libraryExclusion`, `WaitUntilReady`. ~150 lines, the bulk of the file.
- `LLMService.UnloadModel` (`:154-165`), `BeginPreload` body after `return`
  (`:138-145`, already `#pragma warning disable CS0162 unreachable`).
- `OneShotAsync` (`:520-531`) — only caller is `AIMemoryExtractor` (itself only
  meaningful with a live model).
- `BuildSystemPrompt` (`:546-681`), `BuildLiveTelemetry` (`:751-813`),
  `SeedAgentHistoryIfNeededAsync` (`:689-709`), `TrimAgentHistory` (`:724-734`),
  `MarkHistoryDirty` (`:739-742`).
- Streaming text scrubbers, only called from the dead `Chat` streaming callback:
  `StripThinking`/`StripTagPair` (`:834-887`), `StripSelfPrefix` (`:906-950`),
  `ParseAndStripToolCalls`/`HidePartialToolCall`/`ResetToolCallState`
  (`:969-1005`), `BuildToolOnlyFallbackAck` (`:1049-1073`),
  `DispatchPendingToolCalls` (`:1075-1107`), `LogPromptDiagnostics`/`ExtractBlock`
  (`:1018-1042`), and the `_pendingToolCalls`/`_seenToolCallsThisStream` state
  (`:964-967`).
- The `UseGPU`/`UseLargeModel` flags, model-path consts, sampling-tune block —
  all inert (`:46-61`, `:267-300`).

**Now-dead but NOT LLM plumbing (would become live again the moment a caller is
wired — do not delete, but note they are currently dead):**
- All of `IntentRouter.cs` — single caller `LLMService.Chat:357` is behind the
  disable. The deterministic fact-routing (`TryShipDust`, `TryPlayerVitals`, etc.)
  and the whole mark/waypoint pipeline (`TryMarkTarget`) never run today.
- `HALToolDispatcher.Execute` and its `Handle*`/`ResolveTarget`/`TryResolveShip`
  helpers — reachable only from IntentRouter (dead) and
  `DispatchPendingToolCalls` (dead). `TickProximity` (`:77`) is live but always
  early-returns because `_activeWaypoints` is never populated.

**External dead weight (already tracked in CLAUDE.md):** the 3.96 GB
`StreamingAssets/LlamaLib-v2.0.5/` bundle and any `.gguf` — inert, gitignored.
`using LLMUnity;` / `PackageLlamaLib` are still needed to compile
`EnsureModelLoadedAsync`; they can only be dropped if that method goes.

**Recommendation:** if the plan is to replace the phone chat with preset dialogue
(not a real model), the highest-value deletion is `EnsureModelLoadedAsync` +
`OneShotAsync` + the streaming scrubbers + `AIMemoryExtractor`'s LLM call. Keep
`IntentRouter` + `HALToolDispatcher` — they are exactly the deterministic layer the
preset replacement will hang off, and they carry no LLM dependency.

---

## Redundancies

1. **Two full-scene ship scans per HAL ship-poll, duplicated across two polls.**
   `FleetTelemetry.EnumerateAllShipsWithNumbers` (`FleetTelemetry.cs:37-70`) does
   `Object.FindObjectsOfType<BoughtShip>()` **and** `Object.FindObjectsOfType<Ship>()`
   every enumeration. `HALCommentator.PollShipDust` (`HALCommentator.cs:928`) and
   `PollShipOrbit` (`:974`) each enumerate it separately, both on the 1.5 s
   `_shipPollTimer` (`:345-351`). That is 4 `FindObjectsOfType` scans + 2 iterator
   allocations every 1.5 s, iterating the same ship set twice. **Merge the two polls
   into one loop over a single enumeration** (they already share the same per-ship
   nearest-body/net work — `PollShipDust`'s `SumNetBuffers` and `PollShipOrbit`'s
   body scan both walk the same ships).

2. **`ResolveShipByNumber` vs `ResolveShip` in IntentRouter.** `IntentRouter.cs:104`
   (`ResolveShipByNumber`, used by power/fuel) and `:123` (`ResolveShip`, used by
   dust/speed/altitude) both enumerate `EnumerateAllShipsWithNumbers` to find a ship
   by number; the only difference is the offline/dish check. Power/Fuel skip the
   offline gate that dust/speed/altitude enforce — inconsistent, and two code paths
   for one lookup. Fold into one resolver with an `enforceOnline` bool.

3. **Nearest-body scan duplicated three times.** Identical "loop `NBodySimulation.Bodies`,
   min distance to centre" appears in `IntentRouter.TryShipAltitude` (`:243-249`),
   `HALCommentator.PollShipOrbit` (`:986-996`), and `FleetTelemetry.FindNearestBody`
   (`:198-212`). `HALCommentator.NearestBodyToSurface` (`:612`) is a fourth, distinct
   metric (distance-to-surface, with Sun/attractor exclusion). Could share one helper
   with a metric flag.

4. **Duplicate ship-number parsing.** `HALToolDispatcher.TryResolveShip` (`:346`) and
   `IntentRouter.ResolveShip` (`:123`) both parse "ship N" and resolve — different
   entry points, same job.

5. **`DispatchPendingToolCalls` clears state twice.** `_pendingToolCalls.Clear()` +
   `_seenToolCallsThisStream.Clear()` run at `LLMService.cs:1105-1106` and again via
   `ResetToolCallState()` at the start of the next `Chat` (`:443`). Harmless.

---

## Performance / Optimization

*(All in currently-dead code paths unless noted — flagged for when they re-activate.)*

1. **Per-token regex construction (would be hot if chat re-enabled).**
   `StripSelfPrefix` (`LLMService.cs:906`) does `new Regex(...)` twice
   (`prefixRegex` `:914`, `labelRegex` `:925`) on **every** `onToken` call. Streaming
   delivers cumulative text token-by-token, so this allocates 2 uncompiled regexes
   per token per reply. Cache them keyed by `NameStore.ResolvedAIName` (only the
   name-dependent one needs rebuilding on name change).

2. **O(n²) streaming scrubbers.** `StripThinking` (`:834`), `ParseAndStripToolCalls`
   (`:969`), `HidePartialToolCall` (`:991`), `StripSelfPrefix` (`:906`) all run over
   the **cumulative** streamed string on every `onToken`. Each is O(current length),
   summed over the stream → O(n²) in total reply length. Inherent to the
   cumulative-callback design; if re-enabled, prefer operating on the final string
   plus a cheap tail-only partial-tag check for the live bubble.

3. **`EnumerateAllShipsWithNumbers` allocations (live via HAL).** As in Redundancy 1
   — the `FindObjectsOfType` double-scan + `Array.Sort` + iterator allocation fire
   every 1.5 s from HAL. Not per-frame, but it is the only recurring GC pressure in
   the live AI subsystem. Cache the ship list, invalidating on ship
   buy/destroy/scene-load rather than re-scanning on a timer.

4. **IntentRouter regex compilation inconsistency.** `ShipPowerIntent` (`:55`),
   `ShipFuelIntent` (`:59`), `AmbiguousShipResourceIntent` (`:66`) omit
   `RegexOptions.Compiled` while `DustRegex`/`SpeedRegex`/`AltitudeRegex`/mark regexes
   include it. Per-chat cadence, so negligible, but inconsistent. `ContainsWord`
   (`:542`) builds `Regex.IsMatch` from an interpolated pattern each call — fine at
   per-chat rate, relies on .NET's 15-entry static regex cache.

5. **`HALCommentator.Update` still calls `FindObjectOfType<PlayerController>` on the
   lazy-refind paths.** `_cachedPC`/`_pcForEnemy` are cached and only re-found when
   null (`:418`, `:564`, `:654`, `:786`), and nulled on scene load (`:258-259`) —
   this follows the CLAUDE.md rule correctly. No action; noted as verified-clean.

6. **`_reachedRadiusSq` is a mutable static computed once** (`HALToolDispatcher.cs:64`).
   Fine, but could be `const`/`static readonly` for intent clarity.

---

## Notes & Uncertainties

- **The subsystem does more than "templated lines + intent router" describes.** The
  intent router and every LLM tool action are currently unreachable. If the intent
  was for IntentRouter to answer factual questions *today* (independent of the LLM),
  that is **not** happening — it only runs from the disabled `Chat`. Worth confirming
  with the user whether the phone chat is meant to answer anything at all right now,
  or is intentionally silent (`AIChatScreen.cs:1118-1122` just records the user's
  message and returns). This is the single most important thing to verify against
  design intent.
- **HALCommentator is well-guarded and looks healthy.** Singleton pattern, MainMenu
  skip + (per CLAUDE.md trap 1) needs seeding in
  `MainMenuController.EnsureGameplaySingletons` — I did not verify that seeding
  exists; recommend a quick check since HAL is a MainMenu-skipping auto-singleton.
- **Bug 1 severity depends on whether any shipped autosave has
  `dirtyForExtraction=true`.** If the flag was always cleared before the LLM was
  disabled, the crash path is only reachable via a legacy save. Either way the guard
  gap is real and cheap to close; I could not inspect live save JSON to confirm
  field state.
- I did not exhaustively audit `AIMemoryStore`, `AIStoryController`,
  `GameKnowledgeBase`, `TokenResolver`, `NameStore`, or the HUD/voice classes beyond
  their call relationships to the three target files — they were read only where they
  touch HAL/IntentRouter/LLMService.
