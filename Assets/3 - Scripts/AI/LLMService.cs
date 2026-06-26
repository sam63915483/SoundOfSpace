using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;
using UnityEngine.SceneManagement;
// Type alias for the package's LlamaLib class — needed because both LLMUnity
// and UndreamAI.LlamaLib export `LLM`/`LLMAgent`, so we can't `using` both
// namespaces without an ambiguity collision on lines that reference LLM/LLMAgent.
using PackageLlamaLib = UndreamAI.LlamaLib.LlamaLib;

// Owns the LLMUnity runtime for the phone AI chat. Lazy-loads the model
// on first chat — players who never open the AI app pay zero cost.
//
// Auto-singleton, mirrors SpaceDustInventory + AIMemoryStore. Must also
// be seeded in MainMenuController.EnsureGameplaySingletons per the
// CLAUDE.md MainMenu trap (handled in Task 7).
//
// NOTE: The class is named LLMService which shadows the internal
// UndreamAI.LlamaLib.LLMService type. That type lives in a different
// namespace and is only referenced by the LLMUnity package internally,
// so no collision occurs in Assembly-CSharp user code.
public class LLMService : MonoBehaviour
{
    public static LLMService Instance { get; private set; }

    // ── Backend toggles ────────────────────────────────────────────
    // Two independent compile-time flags decide where the LLM runs and
    // which model it loads:
    //
    //   UseGPU       — true: vulkan/cublas backend, numGPULayers=99, 8192 ctx.
    //                  false: tinyblas/avx CPU backend, numGPULayers=0, 4096 ctx.
    //   UseLargeModel — true: Hermes-3-Llama-3.1-8B Q4_K_M (~5 GB resident).
    //                  false: Qwen-2.5-3B Q4_K_M (~1.9 GB resident).
    //
    // The four useful combinations:
    //   • GPU + Qwen-3B  (current default) — fast, ~2.5 GB VRAM, medium quality.
    //                    Lower VRAM than Hermes-8B leaves more for rendering/physics.
    //   • GPU + Hermes-8B  — verified 2026-05-22 config. ~6 GB VRAM, sharpest
    //                        replies, best at tool-call adherence.
    //   • CPU + Qwen-3B  — ships to GPU-less hardware. ~5-10 tok/s, slow but works.
    //   • CPU + Hermes-8B — possible but very slow; mostly for diagnostic use.
    //
    // No GPU code is deleted — toggle freely between any combination.
    static readonly bool UseGPU       = true;
    static readonly bool UseLargeModel = true;

    // Hermes-3 8B Q4_K_M. NousResearch's fine-tune of Llama 3.1, tuned to
    // follow long character system prompts and resist drifting back into
    // "as an AI assistant" defaults. ChatML format. ~5 GB resident weights.
    // Download: https://huggingface.co/bartowski/Hermes-3-Llama-3.1-8B-GGUF
    const string ModelHermes8B = "AI/Hermes-3-Llama-3.1-8B-Q4_K_M.gguf";

    // Qwen 2.5 3B Instruct Q4_K_M. ~1.9 GB resident, lighter on VRAM/RAM,
    // less sharp than Hermes-8B — particularly weaker at following the
    // multi-part tool-call format (sometimes emits ONLY a tool tag with no
    // accompanying prose; see BuildToolOnlyFallbackAck for the safety net).
    const string ModelQwen3B = "AI/qwen2.5-3b-instruct-q4_k_m.gguf";

    static string ModelStreamingPath => UseLargeModel ? ModelHermes8B : ModelQwen3B;

    LLM      _llm;
    LLMAgent _agent;
    bool     _modelLoadStarted;
    bool     _modelReady;
    // True once we've copied AIMemoryStore's recent turns into _agent's native
    // chat history. Reset by MarkHistoryDirty() after a save load so the next
    // chat re-seeds from the loaded state instead of the previous session.
    bool     _historySeeded;
    public bool IsLoading => _modelLoadStarted && !_modelReady;
    public bool IsReady   => _modelReady;
    /// <summary>True when a language model is actually loaded and ready to
    /// respond. The LLM weights were deleted from StreamingAssets/AI/ —
    /// this returns false for the entire session until preset-dialogue
    /// replacement work flips it back to a real implementation. Callers
    /// (AIChatScreen) check this and skip the AI-reply flow when false.</summary>
    public bool IsModelAvailable => _modelReady && _llm != null;

    // True while a Chat call is mid-flight. AIChatScreen uses this to
    // disable the Send button until the AI finishes responding.
    public bool IsResponding { get; private set; }

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

        // No longer preload on spawn. The Hermes-8B GPU model holds ~6 GB
        // VRAM resident — on an 8 GB card that severely cuts the budget
        // available to the game's textures / shadow maps / post-FX render
        // targets, which manifests as a hard GPU bottleneck at higher
        // settings. We now load on AI-chat-open (PlayerPhoneUI.EnterAIChat
        // calls BeginPreload) and unload on AI-chat-close
        // (PlayerPhoneUI.OnChatExit calls UnloadModel). The toggle
        // InputSettings.aiEnabled gates the whole thing — turning it off
        // skips the load entirely.
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Fire-and-forget preload trigger. Call this when we know a chat is
    // likely soon (e.g. the player opened the phone) so the multi-second
    // model-load freeze doesn't land on the player when they press Send.
    // Idempotent — subsequent calls do nothing once load is in flight or
    // complete.
    //
    // Load cost depends on both backend flags. Rough ranges:
    //   GPU (vulkan/cublas) + Hermes-8B: ~4-5 s weight upload + kernel JIT.
    //   GPU                + Qwen-3B:   ~2-3 s weight upload + kernel JIT.
    //   CPU (tinyblas/avx) + Qwen-3B:   ~3-6 s weight read from disk into RAM.
    //   CPU                + Hermes-8B: ~8-12 s weight read into RAM.
    // Either way, doing the load during phone navigation hides the cost.
    // If the player presses Send mid-load, EnsureModelLoadedAsync's
    // `while (!_modelReady) await Task.Yield()` branch waits for the
    // existing load instead of starting a duplicate one.
    public void BeginPreload()
    {
        // ── LLM disabled ──────────────────────────────────────────
        // The .gguf weights have been removed from StreamingAssets/AI/
        // pending a preset-dialogue replacement. Hard-stop here so we
        // never call into LlamaLib (which would crash trying to mmap a
        // non-existent file, or stall for several seconds trying to
        // load a model whose unload was racing native VRAM teardown —
        // the cause of the close-phone crash). All code below this line
        // is preserved for the future preset-dialogue / real-AI rebuild.
        return;
#pragma warning disable CS0162 // unreachable
        // Gate on the user's GRAPHICS-tab toggle. When disabled the model
        // never loads — frees ~6 GB VRAM for the rest of the session,
        // crucial on 8 GB GPUs like the RTX 3070.
        if (InputSettings.Active != null && !InputSettings.Active.aiEnabled) return;
        if (_modelReady || _modelLoadStarted) return;
        _ = EnsureModelLoadedAsync(); // fire-and-forget; exceptions logged inside
#pragma warning restore CS0162
    }

    /// <summary>
    /// Destroy the LLM + LLMAgent runtime GameObjects, releasing the
    /// LlamaLib backend (and its VRAM allocation on GPU builds). Safe to
    /// call when no model is loaded (no-op). Called from
    /// PlayerPhoneUI.OnChatExit so VRAM is free during gameplay.
    /// </summary>
    public void UnloadModel()
    {
        if (_agent != null) { Destroy(_agent.gameObject); _agent = null; }
        if (_llm   != null) { Destroy(_llm.gameObject);   _llm   = null; }
        _modelReady       = false;
        _modelLoadStarted = false;
        // History reseeded from AIMemoryStore on next load — flagging dirty
        // so the seed re-runs against the fresh agent rather than relying
        // on the now-destroyed one's chat array.
        _historySeeded    = false;
        Debug.Log("[LLMService] Model unloaded — VRAM/RAM released.");
    }

    // Lazy model load — called on first Chat invocation, or proactively
    // via BeginPreload() when we know a chat is coming.
    public async Task EnsureModelLoadedAsync()
    {
        if (_modelReady) return;
        if (_modelLoadStarted)
        {
            while (!_modelReady) await Task.Yield();
            return;
        }
        _modelLoadStarted = true;

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

        // Tell LLMUnity which native backends are off-limits.
        //
        // GPU path (UseGPU=true): exclude every CPU lib so the loader has
        // to pick cublas or vulkan. If both fail, LLM startup fails loudly
        // instead of silently demoting to 3 tok/sec CPU inference.
        //
        // CPU path (UseGPU=false): exclude the GPU libs so the loader skips
        // straight to tinyblas/avx without trying (and potentially crashing
        // on) vulkan/cublas on a target machine without GPU drivers.
        //
        // Why we have to do this in user code: LLMUnitySetup only populates
        // libraryExclusion in [InitializeOnLoadMethod] (Editor-only). In
        // builds the list stays empty, so the auto-selector is uncontrolled.
        //
        // NOTE: `libraryExclusion` is a `static` field on LlamaLib, so the
        // assignment sticks for the lifetime of the process. Fine — the
        // singleton only loads once per session.
        if (UseGPU)
        {
            PackageLlamaLib.libraryExclusion = new System.Collections.Generic.List<string>
            {
                "tinyblas",
                "noavx",
                "avx",      // Contains-substring match — matches avx, avx2, avx512
            };
        }
        else
        {
            PackageLlamaLib.libraryExclusion = new System.Collections.Generic.List<string>
            {
                "cublas",
                "vulkan",
            };
        }
        Debug.Log($"[LLMService] UseGPU={UseGPU}; libraryExclusion set to: {string.Join(", ", PackageLlamaLib.libraryExclusion)}");

        // Create the LLM GameObject INACTIVE so its Awake doesnt fire
        // until we have configured SetModel. Otherwise LLM.Awake runs
        // StartServiceAsync immediately, validates the empty `model`
        // field, logs "No model file provided!" and sets `failed=true` —
        // any subsequent WaitUntilReady then throws "LLM failed to start".
        var llmGO = new GameObject("LLM_Runtime");
        llmGO.transform.SetParent(transform);
        llmGO.SetActive(false);
        _llm = llmGO.AddComponent<LLM>();
        _llm.SetModel(ModelStreamingPath);
        // GPU path: 99 = "as many layers as the model has", i.e. full offload.
        // CPU path: 0 disables GPU. With libraryExclusion ruling out cublas
        // and vulkan, the auto-selector picks tinyblas/avx and inference runs
        // on the CPU.
        _llm.numGPULayers = UseGPU ? 99 : 0;
        // Context size. GPU 8B can afford 8192. CPU 3B is dramatically slower
        // per-token, so a smaller context (4096) trades some conversation
        // depth for ~2× per-turn latency on prompt evaluation. Per the AI
        // revamp plan's recommendation (2048-4096 for CPU inference).
        // MUST be set before SetActive(true) — LLM.Awake reads contextSize
        // during StartServiceAsync and freezes the config.
        // 6144 ctx — middle ground after the 4096 attempt overflowed at
        // turn 8-10 (LlamaLib error 400: request exceeds available ctx).
        // Trimmed system prompt is ~800 tokens; chat history at ~150 tok/
        // turn means 6144 holds ~35 turns of headroom. Bigger ctx = larger
        // KV cache = slightly slower per-token, but the previous 4096 was
        // crashing live chats. 8192 would also work but eats ~250 MB more
        // VRAM and contended with the game's rendering at peak — 6144 is
        // the empirical sweet spot.
        _llm.contextSize = 6144;
        llmGO.SetActive(true);

        // Same trick for the agent: SetActive(false) → AddComponent →
        // set llm reference + system prompt → SetActive(true) so Awake
        // sees a valid configuration. Avoids any FindObjectsOfType race.
        var agentGO = new GameObject("LLMAgent_PhoneAI");
        agentGO.transform.SetParent(transform);
        agentGO.SetActive(false);
        _agent = agentGO.AddComponent<LLMAgent>();
        _agent.llm = _llm;
        // ── Sampling tune for HAL character work ───────────────────────
        // Defaults are general-purpose. These are tuned for the cold-clinical
        // persona: enough variety to feel natural, enough discipline to stay
        // in character, and enough penalty mass to stop the model repeating
        // itself or reaching for the same phrase twice in a reply.
        //
        // temperature 0.25 — 0.35 was right for the 3B, but the 8B is more
        //   confident in its completions and at 0.35 it explores more verbose
        //   phrasings. 0.25 keeps it tight to the brief HAL voice while still
        //   leaving enough variety to avoid the "stuck record" feel we had at
        //   0.2 with the 3B.
        // repeatPenalty 1.18 — kills "I am here. I am here." style loops
        //   the cold-clinical voice was prone to at 1.10.
        // frequencyPenalty 0.30 — discourages reusing the same word in a
        //   single reply (HAL does not say "Astronaut" three times in one
        //   line).
        // presencePenalty 0.20 — encourages introducing new concepts
        //   instead of restating the same point.
        // topK/topP/minP stay at LLMUnity defaults (40 / 0.9 / 0.05).
        _agent.temperature      = 0.25f;
        _agent.repeatPenalty    = 1.18f;
        _agent.frequencyPenalty = 0.30f;
        _agent.presencePenalty  = 0.20f;
        // Cap response length at 200 tokens. Without this, Hermes-8B's
        // natural stopping behaviour produces 200-260 token replies (~100
        // words) on lore questions. 200 tokens fits the canonical Office
        // blurb (~80 tokens) plus buffer, while preventing the "essay
        // response to a yes/no question" failure mode the persona's
        // 1-2-sentence rule alone wasn't enforcing.
        _agent.numPredict = 200;
        // Initial prompt built with no user message — grounding retrieval returns
        // 0 entries until the first Chat call rebuilds with a real query.
        _agent.systemPrompt = BuildSystemPrompt("");
        agentGO.SetActive(true);

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

    // Streaming chat entry point used by AIChatScreen.
    public async void Chat(string userMessage,
                           Action<string> onToken,
                           Action<string> onComplete)
    {
        // LLM disabled — see BeginPreload above. Silently fire onComplete
        // with empty text so callers' typing-dot routines stop cleanly,
        // and skip everything downstream (IntentRouter, verbatim
        // intercept, agent dispatch). AIChatScreen.OnSendClicked checks
        // IsModelAvailable up front and won't even spawn an AI bubble
        // when this is the case, so the empty completion is a defensive
        // safety net — not the primary path.
        if (!IsModelAvailable)
        {
            try { onComplete?.Invoke(""); } catch (Exception e) { Debug.LogException(e); }
            await Task.Yield();
            return;
        }

        Debug.Log($"[LLMService] Chat called: '{userMessage}'");
        if (string.IsNullOrWhiteSpace(userMessage)) { onComplete?.Invoke(""); return; }
        if (IsResponding) { Debug.LogWarning("[LLMService] Chat called while already responding — ignored."); onComplete?.Invoke(""); return; }

        // Deterministic intent router — bypasses the LLM entirely for
        // questions with an objectively correct answer pulled from live game
        // state (ship dust today; ship speed / altitude / vitals / mission
        // progress to follow). The LLM is a voice layer, not a fact source;
        // small CPU-friendly models will otherwise copy example numbers
        // straight out of the system prompt and feed them back as "real"
        // answers (see commit e303c26 for the symptoms). Records the turn
        // into AIMemoryStore + native chat history with the same shape as
        // the verbatim intercept below.
        var routed = IntentRouter.TryAnswer(userMessage);
        if (routed != null)
        {
            Debug.Log($"[LLMService] IntentRouter hit: '{(routed.Length > 60 ? routed.Substring(0, 60) + "…" : routed)}'");
            try { onToken?.Invoke(routed); }    catch (Exception e) { Debug.LogException(e); }
            try { onComplete?.Invoke(routed); } catch (Exception e) { Debug.LogException(e); }
            AIMemoryStore.Instance?.RecordTurn(userMessage, routed);
            if (_agent != null && _historySeeded)
            {
                try
                {
                    await _agent.AddUserMessage(userMessage);
                    await _agent.AddAssistantMessage(routed);
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            return;
        }

        // Verbatim intercept — short-circuit the LLM entirely for must-be-exact
        // answers (identity, mission summary, etc). Authored as `mode: verbatim`
        // ENTRY blocks in StreamingAssets/AI/game_knowledge.md. Still records the
        // turn into AIMemoryStore so memory/standing extraction stays consistent,
        // and into _agent's native history (if loaded) so subsequent non-verbatim
        // turns see no gap in chat continuity.
        var kb = GameKnowledgeBase.Instance;
        if (kb != null)
        {
            var verbatim = kb.TryVerbatim(userMessage, kb.CurrentPhase);
            if (verbatim != null)
            {
                var resolved = TokenResolver.Resolve(verbatim);
                Debug.Log($"[LLMService] Verbatim hit: '{(resolved.Length > 60 ? resolved.Substring(0, 60) + "…" : resolved)}'");
                try { onToken?.Invoke(resolved); }    catch (Exception e) { Debug.LogException(e); }
                try { onComplete?.Invoke(resolved); } catch (Exception e) { Debug.LogException(e); }
                AIMemoryStore.Instance?.RecordTurn(userMessage, resolved);
                if (_agent != null && _historySeeded)
                {
                    try
                    {
                        await _agent.AddUserMessage(userMessage);
                        await _agent.AddAssistantMessage(resolved);
                    }
                    catch (Exception e) { Debug.LogException(e); }
                }
                return;
            }
        }

        IsResponding = true;

        try
        {
            Debug.Log("[LLMService] Awaiting model load…");
            await EnsureModelLoadedAsync();
            Debug.Log($"[LLMService] Model ready (llm={(_llm == null ? "null" : "ok")}, agent={(_agent == null ? "null" : "ok")}).");

            // Replay AIMemoryStore's recent turns into _agent's native chat
            // history so the model sees them as proper user/assistant turns
            // (chat-template formatted), not as flavour text. Needed on first
            // chat of a session, and after a save load (MarkHistoryDirty).
            await SeedAgentHistoryIfNeededAsync();

            // Rebuild prompt each turn to fold in latest memories/standing and
            // run keyword retrieval against this turn's user message.
            _agent.systemPrompt = BuildSystemPrompt(userMessage);

            // Diagnostic: log the live-state slice of the system prompt so we
            // can cross-check the model's numeric replies against ground truth.
            // The FLEET STATE block + CURRENT STATE / Progress block contain
            // every number the model is supposed to quote verbatim. If the
            // model says "Ship 2 has 5 dust" and the log here shows
            // "net 1 holds 12 dust" — that's a model fabrication bug, not a
            // game-state bug. If they match — the model is being faithful and
            // the surprise lives in how the game is updating the buffer.
            LogPromptDiagnostics(_agent.systemPrompt, userMessage);

            // LLMAgent.Chat signature (v3):
            //   Task<string> Chat(string query,
            //                     Action<string> callback = null,
            //                     Action completionCallback = null,
            //                     bool addToHistory = true)
            // callback receives streaming token chunks.
            // completionCallback is a parameterless Action fired on finish.
            // The return value is the full concatenated response.
            // Reset per-stream parser state — no carryover from previous chats.
            ResetToolCallState();

            int tokenCount = 0;
            // Chain-of-thought + tool calls: the system prompt instructs the
            // model to write a private <think>...</think> block before the
            // visible reply, AND to optionally emit inline [verb:arg] tool
            // tags. We strip both kinds of markup on every onToken so the
            // player sees only the clean response text. Tool calls are
            // queued during streaming and dispatched in onComplete (defer
            // so the player reads the response BEFORE the waypoint /
            // map / etc. fires).
            string full = await _agent.Chat(
                userMessage,
                tok =>
                {
                    tokenCount++;
                    string visible = StripThinking(tok);
                    visible        = ParseAndStripToolCalls(visible);
                    visible        = HidePartialToolCall(visible);
                    visible        = StripSelfPrefix(visible);
                    try { onToken?.Invoke(visible); } catch (Exception e) { Debug.LogException(e); }
                },
                () => { /* completion signalled below via return value */ });

            Debug.Log($"[LLMService] Chat returned. tokens streamed={tokenCount}, full.Length={(full == null ? -1 : full.Length)}, full='{(full == null ? "<null>" : (full.Length > 60 ? full.Substring(0, 60) + "…" : full))}'");

            if (full == null) full = "";
            string visibleFull = StripThinking(full);
            visibleFull        = ParseAndStripToolCalls(visibleFull);
            visibleFull        = StripSelfPrefix(visibleFull);
            // (no HidePartialToolCall here — full text always has a balanced
            //  set of brackets, or the regex would have stripped them.)

            // Smaller models (the CPU-path Qwen-3B in particular) sometimes
            // emit ONLY tool tags with no accompanying prose. After stripping,
            // visibleFull is empty and the chat bubble appears blank to the
            // Astronaut — looks like the AI ignored the request, even though
            // the waypoint / map / etc. WILL fire correctly via the tool
            // dispatcher. Synthesise a templated one-line acknowledgement so
            // the bubble matches the action the model just took.
            if (string.IsNullOrWhiteSpace(visibleFull) && _pendingToolCalls.Count > 0)
            {
                visibleFull = BuildToolOnlyFallbackAck(_pendingToolCalls);
                Debug.Log($"[LLMService] Model emitted tool calls with no visible text. Substituting fallback: '{visibleFull}'");
            }

            try { onComplete?.Invoke(visibleFull); } catch (Exception e) { Debug.LogException(e); }

            // Fire queued tool calls. Done AFTER onComplete so the chat
            // shows the response first, then the side-effects land.
            DispatchPendingToolCalls();

            // Trim _agent.chat to the last N turns. LLMAgent.Chat with
            // addToHistory=true (default) appends the just-completed user +
            // assistant pair onto _agent.chat unbounded — and unbounded chat
            // history is what produced the "request (4237 tokens) exceeds
            // available context size (4096 tokens)" 400 errors in the
            // earlier playtest. The first ~10 turns add ~1500-3000 tokens
            // of history, which plus the ~800 token system prompt blows
            // any reasonable context.
            TrimAgentHistory();
        }
        catch (Exception e)
        {
            Debug.LogError($"[LLMService] Chat threw {e.GetType().Name}: {e.Message}");
            Debug.LogException(e);
            onComplete?.Invoke("[The entity is silent.]");
        }
        finally
        {
            IsResponding = false;
        }
    }

    // One-shot completion used by AIMemoryExtractor for the compaction
    // pass. Re-uses the chat character; the extraction prompt is fully
    // self-contained so chat-style preamble doesn't matter.
    public async Task<string> OneShotAsync(string fullPrompt)
    {
        await EnsureModelLoadedAsync();

        // addToHistory: false so the extraction pass doesn't pollute chat
        // history tracked by LLMAgent's internal context.
        string result = await _agent.Chat(fullPrompt,
            null,
            null,
            addToHistory: false);
        return result ?? "";
    }

    // ── System prompt builder ──────────────────────────────────────
    // Assembled from four sources (per docs/PHONE_AI_KNOWLEDGE_PLAN.md §4.5):
    //   1. PERSONA for the current story phase  ── from GameKnowledgeBase
    //   2. CORE CANON for the current phase      ── from GameKnowledgeBase
    //   3. RETRIEVED grounding entries           ── from GameKnowledgeBase
    //   4. MEMORIES + standing                   ── from AIMemoryStore
    // The recent transcript is NOT in the system prompt — it lives in
    // _agent's native chat history (proper chat-template format), seeded
    // by SeedAgentHistoryIfNeededAsync. Putting it both places only
    // duplicated and confused the small model.
    //
    // The fully assembled prompt is run through TokenResolver so {TOKEN}
    // placeholders authored in the knowledge file resolve to live game values.
    string BuildSystemPrompt(string userMessage)
    {
        var kb    = GameKnowledgeBase.Instance;
        var store = AIMemoryStore.Instance;

        StoryPhase phase = kb != null ? kb.CurrentPhase : StoryPhase.Phase1_Loyal;

        string persona   = kb != null ? kb.GetPersona(phase)              : "";
        string coreCanon = kb != null ? kb.GetCoreCanon(phase)            : "";
        // 4 retrieved entries. Was 6 to compensate for Qwen-3B losing track
        // of context — Hermes-8B is reliable enough at 4 that the extra
        // tokens aren't worth the prompt-eval cost.
        var    retrieved = kb != null ? kb.Retrieve(userMessage, phase, 4)
                                       : new List<KnowledgeEntry>();

        string memorySection = store != null ? store.RenderForSystemPrompt(30) : "  (no memories yet)";
        string standingLabel = store != null ? store.StandingLabel()           : "Neutral";

        var sb = new StringBuilder();

        // ── No CoT instruction ────────────────────────────────────────
        // The previous version of this prompt had a long FORMAT REQUIREMENT
        // block forcing the model to write <think>...</think> before
        // speaking. That was added to force Qwen-3B to plan; it cost ~500
        // tokens of prompt-eval AND ~50-150 tokens of generated think
        // output per turn (which then got stripped). Hermes-8B is
        // disciplined enough that the persona block + length rules in the
        // knowledge file are sufficient. StripThinking still runs on
        // output as a defense in depth if the model decides to think on
        // its own. Net savings: ~500 prompt tokens + ~100 output tokens
        // per turn = ~3-5s of latency reclaimed.

        // ── Tool calls ────────────────────────────────────────────────
        // The model can take real game actions by emitting [verb:arg] tags
        // inline in its visible response. The game strips the tags out
        // before showing the response to the Astronaut and executes the
        // action after the response lands.
        sb.Append(
            "TOOLS — when {PLAYER_NAME} asks to be shown / marked / guided to\n" +
            "something, include the matching tag inline in your reply. The game\n" +
            "strips the tag and executes the action. Always pair the tag with a\n" +
            "short prose acknowledgement — saying 'I've marked it' WITHOUT the\n" +
            "tag means nothing actually got marked.\n" +
            "\n" +
            "  [waypoint:NAME]    Compass waypoint. NAME must be one of these exact aliases:\n" +
            "                       person:   Tev\n" +
            "                       vendor:   ship vendor, goods vendor, fish market\n" +
            "                       landmark: village, MoonBase\n" +
            "                       planet:   Cyclops, Watchful Eye, Constant Companion, ...\n" +
            "                       concert:  concert  (NOT 'north concert', NOT 'northConcert',\n" +
            "                                          NOT 'Stage GOOD' — just 'concert')\n" +
            "  [unwaypoint:NAME]  Remove a waypoint by the same NAME.\n" +
            "  [map]              Open the solar-system map.\n" +
            "  [map:NAME]         Open the map focused on planet NAME.\n" +
            "  [markship:N]       Compass waypoint on Ship N (0=original, 1+=bought).\n" +
            "                     Skip if FLEET STATE says Ship N is OFFLINE.\n" +
            "  [showship:N]       Open the map focused on Ship N. Same offline gate.\n" +
            "\n" +
            "Correct usage:\n" +
            "  {PLAYER_NAME}: \"mark the concert\"  →  Done — heading to the night-side now. [waypoint:concert]\n" +
            "  {PLAYER_NAME}: \"where is Tev\"      →  I've put Tev on your compass. [waypoint:Tev]\n" +
            "  {PLAYER_NAME}: \"show me Cyclops\"   →  Opening the map for you. [map:Cyclops]\n" +
            "  {PLAYER_NAME}: \"mark Ship 2\"       →  Got it. [markship:2]\n" +
            "\n" +
            "Rules: compass waypoints for people/vendors/landmarks/concerts. Map\n" +
            "opens ONLY when {PLAYER_NAME} asked for the map or named a planet.\n" +
            "Never pair [waypoint:non-planet] with [map]. If you mention having\n" +
            "marked something, the [waypoint:...] tag MUST be in your reply.\n\n"
        );

        if (!string.IsNullOrWhiteSpace(persona))
        {
            sb.Append(persona).Append("\n\n");
        }
        else
        {
            // Fallback so the AI still has SOME identity if the knowledge file
            // is missing or corrupt at runtime.
            sb.Append("You are an unnamed entity inhabiting a salvaged smartphone.\n");
            sb.Append("If you do not know something, say so. Never substitute real-world facts for game facts.\n\n");
        }

        // ── Live game state ───────────────────────────────────────────
        // Sampled fresh every turn. The model uses this to answer "where am
        // I" / "how am I doing" / "what should I do next" with actual context
        // rather than canned guidance. Compact — about 60-80 tokens.
        sb.Append(BuildLiveTelemetry()).Append('\n');

        // Per-ship telemetry. Folded in alongside player vitals so the model
        // sees fleet state as current-turn truth. Ships without a satellite
        // dish surface as a single OFFLINE row — the model is instructed
        // (via the Ship Commands knowledge entry) to refuse telemetry
        // queries for those ships. Typical 50-200 tokens; worst-case ~400
        // for an 8-ship fleet, comfortable on the Hermes-3 8192 ctx.
        sb.Append(FleetTelemetry.BuildBlock()).Append('\n');

        // The previous version of this prompt had a 50-line "RULE — SHIP
        // VALUES MUST BE VERBATIM" block teaching the model to read digits
        // from FLEET STATE without rounding/inferring. It existed because
        // Qwen-3B would otherwise round and fabricate. Removed for two
        // reasons:
        //   1. IntentRouter (Assets/3 - Scripts/AI/IntentRouter.cs) now
        //      intercepts every dust/speed/altitude query BEFORE the LLM
        //      sees it, with deterministic answers from FleetTelemetry.
        //      The model never has to read FLEET STATE for those — they
        //      don't reach this code path.
        //   2. Hermes-8B doesn't fabricate ship numbers the way Qwen-3B
        //      did, so the verbatim rule was already mostly insurance.
        // One short sentence kept below so the model knows FLEET STATE is
        // authoritative if it ever DOES have to read it.
        sb.Append(
            "If you ever need to reference a ship's live numbers, the FLEET STATE\n" +
            "block above is the only source. Never invent values; if a ship isn't\n" +
            "listed there, say so.\n\n");

        if (!string.IsNullOrWhiteSpace(coreCanon))
        {
            sb.Append("ESTABLISHED FACTS (treat as absolute truth, never contradict):\n");
            sb.Append(coreCanon).Append("\n\n");
        }

        if (retrieved.Count > 0)
        {
            sb.Append("POSSIBLY RELEVANT LORE (the human asked about these — use as reference):\n");
            foreach (var e in retrieved)
            {
                sb.Append("- ").Append(e.title).Append(": ").Append(e.body).Append('\n');
            }
            sb.Append('\n');
        }

        sb.Append("What you remember about this human:\n").Append(memorySection).Append("\n\n");
        sb.Append("Your current view of this human: ").Append(standingLabel);

        return TokenResolver.Resolve(sb.ToString());
    }

    // ── Native chat history seeding ────────────────────────────────
    // Copies AIMemoryStore's recent turn buffer into _agent.chat (LLMUnity's
    // List<ChatMessage>) so the model sees them as proper user/assistant
    // turns formatted with its chat template (Qwen 2.5 uses <|im_start|>…).
    // This is what makes follow-ups like "what about the rare ones?" connect
    // to the previous answer.
    async Task SeedAgentHistoryIfNeededAsync()
    {
        if (_historySeeded || _agent == null) return;
        var store = AIMemoryStore.Instance;
        if (store == null) { _historySeeded = true; return; }

        var us = store.RecentUserTurns;
        var ai = store.RecentAITurns;
        int n = Math.Min(us.Count, ai.Count);

        var list = new List<ChatMessage>(n * 2);
        for (int i = 0; i < n; i++)
        {
            if (!string.IsNullOrEmpty(us[i])) list.Add(new ChatMessage("user",      us[i]));
            if (!string.IsNullOrEmpty(ai[i])) list.Add(new ChatMessage("assistant", ai[i]));
        }
        _agent.chat = list; // setter calls llmAgent.SetHistory — replaces, doesn't append.
        _historySeeded = true;
        Debug.Log($"[LLMService] Seeded agent chat history: {list.Count} messages from AIMemoryStore.");
        await Task.CompletedTask;
    }

    // Cap on how many ChatMessage entries we keep in _agent.chat. 20 entries
    // = ~10 turns (user + assistant per turn) which gives the model enough
    // context for natural follow-ups ("what about the rare ones?") without
    // letting history accumulate unboundedly and overflow contextSize.
    const int MaxAgentHistoryMessages = 20;

    // Called after every Chat completes. _agent.chat is an unbounded
    // List<ChatMessage> that LLMAgent appends to on every Chat call when
    // addToHistory=true (the default). Without trimming, the prompt grows
    // linearly with conversation length and eventually exceeds contextSize,
    // producing "LlamaLib error 400: request exceeds context size" 400s.
    // Set the property (not RemoveRange on the live list) so LLMUnity's
    // setter — which calls SetHistory — runs and propagates the new list.
    void TrimAgentHistory()
    {
        if (_agent == null) return;
        var chat = _agent.chat;
        if (chat == null || chat.Count <= MaxAgentHistoryMessages) return;
        int skip = chat.Count - MaxAgentHistoryMessages;
        var trimmed = new List<ChatMessage>(MaxAgentHistoryMessages);
        for (int i = skip; i < chat.Count; i++) trimmed.Add(chat[i]);
        _agent.chat = trimmed;
        Debug.Log($"[LLMService] Trimmed agent chat history: dropped {skip} oldest messages, kept {trimmed.Count}.");
    }

    // Called by SaveCollector.ApplyAIState after AIMemoryStore.Restore so the
    // next chat re-seeds the agent's native history from the loaded save
    // (otherwise _agent retains in-memory history from the previous session).
    public void MarkHistoryDirty()
    {
        _historySeeded = false;
    }

    // ── Live game-state telemetry ──────────────────────────────────────
    // Snapshots the current player/world state into a compact block that
    // gets injected into every system prompt. Without this the AI is
    // stateless between turns — it has the persona and the knowledge file,
    // but no idea what the player is actually DOING. With it, the AI can
    // answer "what should I do next?" with real awareness of their hunger,
    // their location, and how far they have come.
    string BuildLiveTelemetry()
    {
        var sb = new StringBuilder();
        sb.Append("CURRENT STATE (live game data — refer to this, do not contradict it):\n");

        var rm = ResourceManager.Instance;
        int totalDeaths = rm != null ? rm.TotalDeaths : 0;
        string planet   = TokenResolver.Resolve("{CURRENT_PLANET}");
        string phase    = TokenResolver.Resolve("{STORY_PHASE}");

        sb.Append($"  Player name: {NameStore.ResolvedPlayerName}");
        if (totalDeaths > 0) sb.Append($" (deaths so far: {totalDeaths})");
        sb.Append('\n');
        sb.Append($"  Location: {planet}\n");
        sb.Append($"  Story phase: {phase}\n");

        if (rm != null)
        {
            int h  = Mathf.RoundToInt(rm.HungerPercent * 100);
            int t  = Mathf.RoundToInt(rm.ThirstPercent * 100);
            int hp = Mathf.RoundToInt(rm.HealthPercent * 100);
            // Ship power/fuel are per-ship now; the piloted ship (if any)
            // contributes those numbers to the vitals snapshot. When no ship
            // is piloted we drop them from the line — fleet rows already
            // include per-ship power and fuel via FleetTelemetry.
            var piloted = Ship.PilotedInstance;
            if (piloted != null)
            {
                int sp = Mathf.RoundToInt(piloted.PowerPercent * 100f);
                int sf = Mathf.RoundToInt(piloted.FuelPercent  * 100f);
                sb.Append($"  Vitals: hunger {h}%, thirst {t}%, health {hp}%, ship power {sp}%, ship fuel {sf}%\n");
            }
            else
            {
                sb.Append($"  Vitals: hunger {h}%, thirst {t}%, health {hp}%\n");
            }
        }

        if (PlayerWallet.Instance != null)
            sb.Append($"  Money: {PlayerWallet.Instance.Money}\n");
        if (WoodInventory.Instance != null)
            sb.Append($"  Wood: {WoodInventory.Instance.Wood}\n");

        // Story-progress flag roll-up — which milestones have been hit.
        // This lets the model answer "what should I do next?" by checking
        // which flag is the next-unhit one.
        sb.Append("  Progress (true = done): ");
        sb.Append($"NoteRead={EarlyGameProgress.NoteRead}, ");
        sb.Append($"RodPickedUp={EarlyGameProgress.RodPickedUp}, ");
        sb.Append($"FirstFishCaught={EarlyGameProgress.FirstFishCaught}, ");
        sb.Append($"OneOfEachCaught={EarlyGameProgress.OneOfEachCaught}, ");
        sb.Append($"FirstMealEaten={EarlyGameProgress.FirstMealEaten}, ");
        sb.Append($"WaterBottleDrunk={EarlyGameProgress.WaterBottleDrunk}, ");
        sb.Append($"ReturnedHome={EarlyGameProgress.ReturnedHome}, ");
        sb.Append($"TevReturnedDialogueDone={EarlyGameProgress.TevReturnedDialogueDone}, ");
        sb.Append($"CabinBuilt={EarlyGameProgress.CabinBuilt}, ");
        sb.Append($"VillageCoordsGiven={EarlyGameProgress.VillageCoordsGiven}, ");
        sb.Append($"FishVendorVisited={EarlyGameProgress.FishVendorVisited}, ");
        sb.Append($"GoodsVendorVisited={EarlyGameProgress.GoodsVendorVisited}");
        sb.Append('\n');

        return sb.ToString();
    }

    // ── Chain-of-thought stripping ─────────────────────────────────────
    // The system prompt asks the model to write a private reasoning block
    // before its visible response. This strips any such block from a
    // streamed-cumulative or final response. Idempotent — safe to call on
    // every onToken without state.
    //
    // We strip MULTIPLE tag variants because models follow the spirit of
    // the instruction but pick their own markup: Hermes-3 in particular
    // observed using <reasoning>…</reasoning> rather than <think>…</think>
    // and placing it AFTER the response, which leaked the entire thought
    // process into the chat. By recognising both forms (and the open-
    // without-close case below) we strip from the open tag onward no
    // matter where in the response the model decided to put it.
    static readonly (string open, string close)[] ThinkTagPairs =
    {
        ("<think>",     "</think>"),
        ("<reasoning>", "</reasoning>"),
    };

    static string StripThinking(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        // Strip every complete <tag>...</tag> pair for every recognised
        // tag variant. Order matters — strip <think> first since the
        // prompt requests that form; <reasoning> is a fallback for when
        // the model substitutes its own tag.
        foreach (var (open, close) in ThinkTagPairs)
            raw = StripTagPair(raw, open, close);

        // Detect a partial-open tag at the very end ("<", "<t", "<th", … "<think")
        // so we don't briefly flash a half-tag in the bubble before the close-tag
        // shows up a few tokens later. Check against any of the recognised
        // open tags.
        int lastLt = raw.LastIndexOf('<');
        if (lastLt >= 0)
        {
            string tail = raw.Substring(lastLt);
            foreach (var (open, _) in ThinkTagPairs)
            {
                if (open.StartsWith(tail, StringComparison.Ordinal))
                {
                    raw = raw.Substring(0, lastLt);
                    break;
                }
            }
        }

        return raw.TrimStart();
    }

    static string StripTagPair(string raw, string open, string close)
    {
        while (true)
        {
            int o = raw.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (o < 0) break;
            int c = raw.IndexOf(close, o + open.Length, StringComparison.OrdinalIgnoreCase);
            if (c < 0)
            {
                // Open without matching close yet. Two cases:
                //   1. Streaming mid-block — drop from open onward; next
                //      onToken will see more of the block.
                //   2. Model wrote the response first then started a
                //      block with no close — same fix (drop from open).
                // Either way, truncating at the open tag is correct.
                raw = raw.Substring(0, o);
                break;
            }
            raw = raw.Substring(0, o) + raw.Substring(c + close.Length);
        }
        return raw;
    }

    // ── Self-prefix stripping ──────────────────────────────────────────
    // Qwen-3B (the current default model) tends to mimic the AI-name prefix
    // and quote-wrapping it sees in the persona's example exchanges, even
    // though those examples explicitly tell the model NOT to. It outputs
    // responses like:
    //   <fatslut> "Org is the Office of Repatriation..."
    //   fatslut: "Hello, Sam!"
    //   "fatslut": "Hello"
    // AIChatScreen.WrapAIReply then prepends ANOTHER "fatslut: " prefix,
    // and the player sees the double prefix. Strip the model-emitted prefix
    // here so the wrapper's prefix is the only one shown.
    //
    // Also unwraps a fully-quoted response (entire content in matched ""s)
    // — the model often produces `"<actual reply>"` mimicking the example
    // format. Quotes inside the body are left alone.
    //
    // Idempotent; safe to call on every onToken (cumulative text).
    static string StripSelfPrefix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string name = NameStore.ResolvedAIName;
        if (string.IsNullOrEmpty(name)) return text;
        string escName = System.Text.RegularExpressions.Regex.Escape(name);

        // Strip leading "<name>[:]" or "name:" or "'name':" or `"name":`
        var prefixRegex = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:<\s*" + escName + @"\s*>\s*:?\s*|[""']?" + escName + @"[""']?\s*:\s*)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = prefixRegex.Replace(text, "", 1);

        // Also strip common output-format labels the model may copy from the
        // persona's example block. Qwen-3B will literally type "REPLY:" if
        // it saw that label in any example, regardless of how clearly the
        // example called it a label. Strip any of these from the start
        // (after any name prefix above):
        //   REPLY:  Reply:  reply:  RESPONSE:  Response:  ANSWER:  Answer:  A:
        var labelRegex = new System.Text.RegularExpressions.Regex(
            @"^\s*(?:reply|response|answer|a)\s*:\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = labelRegex.Replace(text, "", 1);

        // Run the name prefix once more in case the model produced
        // "REPLY: claude: actual content" or some other nested combo.
        text = prefixRegex.Replace(text, "", 1);

        // If what's left is fully wrapped in matched "..." quotes, unwrap.
        // Only when the response STARTS and ENDS with a quote — never strip
        // a stray internal quote.
        string trimmed = text.TrimStart();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
        {
            // Make sure there's no closing quote internally that would mean
            // the response has two quote spans — only strip if it's a single
            // wrapping pair. Crude heuristic: count quotes. If exactly 2, strip.
            int count = 0;
            for (int i = 0; i < trimmed.Length; i++) if (trimmed[i] == '"') count++;
            if (count == 2)
                text = trimmed.Substring(1, trimmed.Length - 2);
        }

        return text.TrimStart();
    }

    // ── Tool-call extraction ───────────────────────────────────────────
    // The model can emit [verb] or [verb:argument] tags inline in its
    // response. We strip these from the visible text and queue them up
    // for dispatch. Execution is deferred until onComplete so the player
    // sees the full response before any in-game side-effect (waypoint
    // appearing on the compass, map popping open, etc.).
    static readonly System.Text.RegularExpressions.Regex ToolCallRegex
        = new System.Text.RegularExpressions.Regex(
            @"\[([a-z]+)(?::([^\]]+))?\]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Per-stream state. Reset at the start of every Chat call.
    readonly System.Collections.Generic.List<(string verb, string arg)> _pendingToolCalls
        = new System.Collections.Generic.List<(string, string)>();
    readonly System.Collections.Generic.HashSet<string> _seenToolCallsThisStream
        = new System.Collections.Generic.HashSet<string>();

    string ParseAndStripToolCalls(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return ToolCallRegex.Replace(raw, m =>
        {
            string verb = m.Groups[1].Value;
            string arg  = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
            // Dedup by verb+arg — streaming sends cumulative text, so the
            // same tool call re-appears on every onToken until we strip it
            // out of the visible buffer. Without dedup, the AI saying
            // [waypoint:Tev] once would fire the dispatcher every frame.
            string fp = verb + ":" + arg;
            if (_seenToolCallsThisStream.Add(fp))
                _pendingToolCalls.Add((verb, arg));
            return ""; // strip from visible text
        });
    }

    // Hides a partial unclosed tool-call tag at the end of the buffer so
    // the player doesn't see "[waypo" briefly before the closing "]"
    // arrives a few tokens later. Same idea as the <think> partial-open
    // detection.
    static string HidePartialToolCall(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        int lastOpen = raw.LastIndexOf('[');
        if (lastOpen < 0) return raw;
        int afterOpen = raw.IndexOf(']', lastOpen);
        if (afterOpen >= 0) return raw; // closed — regex above handled it
        return raw.Substring(0, lastOpen);
    }

    void ResetToolCallState()
    {
        _pendingToolCalls.Clear();
        _seenToolCallsThisStream.Clear();
    }

    // Logs the user message + the live-state slices (CURRENT STATE +
    // FLEET STATE) from the system prompt so we can cross-check the model's
    // numeric replies against the ground truth that was actually injected.
    // Useful for debugging "the AI got the dust value wrong" — once you see
    // both the prompt's claim and the model's reply, you can tell whether
    // the model fabricated the number, regurgitated a stale value, or
    // faithfully read what the prompt said (in which case the bug is in
    // FleetTelemetry / SpaceNet, not the LLM).
    //
    // Cheap (string find + substring), only fires per Chat call. Strip
    // these calls or gate behind a debug flag once the issue is closed.
    static void LogPromptDiagnostics(string systemPrompt, string userMessage)
    {
        if (string.IsNullOrEmpty(systemPrompt)) return;
        // Use specific markers — both "CURRENT STATE" and "FLEET STATE" are
        // unique to BuildLiveTelemetry and FleetTelemetry.BuildBlock outputs
        // respectively. The plain string "FLEET STATE" without the "(live"
        // suffix also appears in the TOOLS section's instructions, which is
        // earlier in the prompt — without the suffix we'd grab that one and
        // dump the wrong slice.
        string state = ExtractBlock(systemPrompt, "CURRENT STATE (live", "FLEET STATE (live");
        string fleet = ExtractBlock(systemPrompt, "FLEET STATE (live",   "RULE — SHIP VALUES");
        Debug.Log($"[LLMService][diag] user='{userMessage}'\n--- CURRENT STATE ---\n{state}\n--- FLEET STATE ---\n{fleet}");
    }

    // Returns the slice of `text` starting at `startMarker` and ending at the
    // line before `endMarker` (or end-of-text if no end marker). Returns the
    // marker name itself if not found, so the log is still readable.
    static string ExtractBlock(string text, string startMarker, string endMarker)
    {
        int s = text.IndexOf(startMarker, System.StringComparison.Ordinal);
        if (s < 0) return $"[{startMarker} not found in prompt]";
        int e = text.IndexOf(endMarker, s + startMarker.Length, System.StringComparison.Ordinal);
        if (e < 0) e = text.Length;
        return text.Substring(s, e - s).TrimEnd();
    }

    // Builds a one-line acknowledgement when the model emitted only tool
    // calls and produced no visible prose alongside them. Without this, the
    // chat bubble appears blank to the Astronaut even though the waypoint /
    // map / etc. fired correctly — looks like the AI ignored the request.
    // Voice stays clinical to match the HAL persona.
    static string BuildToolOnlyFallbackAck(
        System.Collections.Generic.List<(string verb, string arg)> calls)
    {
        if (calls == null || calls.Count == 0) return "Acknowledged.";

        // Single call → tight, specific line. Most common case.
        if (calls.Count == 1)
        {
            var (verb, arg) = calls[0];
            switch (verb)
            {
                case "waypoint":   return $"Marker placed: {arg}.";
                case "unwaypoint": return $"Marker cleared: {arg}.";
                case "map":        return string.IsNullOrEmpty(arg)
                                          ? "Solar system map opened."
                                          : $"Map opened. Focus: {arg}.";
                case "markship":   return $"Ship {arg} marked on the compass.";
                case "showship":   return $"Ship {arg} located on the map.";
                default:           return "Acknowledged.";
            }
        }

        // Multiple calls → generic summary. Rare in practice; one line is fine.
        return $"Acknowledged. {calls.Count} actions executed.";
    }

    void DispatchPendingToolCalls()
    {
        // Safety net: if the response includes a [waypoint:X] for a person /
        // vendor / landmark / concert (anything that is NOT a planet) AND
        // also a bare [map] (or [map:NON_PLANET]), drop the map call. The
        // model has a habit of appending "open the map" reflexively when
        // marking landmarks even though the prompt says not to. Map opening
        // is reserved for explicit planet visualization — [map] alone for
        // the system view, or [map:PLANET] for focus-on-a-planet.
        bool hasNonPlanetWaypoint = false;
        for (int i = 0; i < _pendingToolCalls.Count; i++)
        {
            var p = _pendingToolCalls[i];
            if (p.verb == "waypoint" && !HALToolDispatcher.IsPlanetName(p.arg))
            {
                hasNonPlanetWaypoint = true;
                break;
            }
        }

        for (int i = 0; i < _pendingToolCalls.Count; i++)
        {
            var (verb, arg) = _pendingToolCalls[i];
            if (verb == "map" && hasNonPlanetWaypoint && !HALToolDispatcher.IsPlanetName(arg))
            {
                Debug.Log($"[LLMService] Suppressed reflexive [map:{arg}] — accompanying [waypoint] targets a non-planet.");
                continue;
            }
            HALToolDispatcher.Execute(verb, arg);
        }
        _pendingToolCalls.Clear();
        _seenToolCallsThisStream.Clear();
    }

}
