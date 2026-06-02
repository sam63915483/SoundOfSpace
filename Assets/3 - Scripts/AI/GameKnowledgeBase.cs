using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Hand-authored canon for the phone AI. Loads StreamingAssets/AI/game_knowledge.md
// once on first access, parses into PERSONA blocks (keyed by story phase) and
// ENTRY blocks (Core / Grounding / Verbatim).
//
// Auto-singleton, mirrors AIMemoryStore / LLMService. Must also be seeded in
// MainMenuController.EnsureGameplaySingletons per the CLAUDE.md MainMenu trap.
//
// Editor: re-parses the file on demand whenever its mtime advances, so the
// author can tune lore live in Play mode.

public enum KnowledgeMode { Core, Grounding, Verbatim }

[Serializable]
public class KnowledgeEntry
{
    public string            title;
    public KnowledgeMode     mode;
    public List<string>      phases   = new List<string>(); // empty/"all" => every phase
    public List<string>      keywords = new List<string>();
    public List<string>      intents  = new List<string>();
    public string            body;
}

public class GameKnowledgeBase : MonoBehaviour
{
    public static GameKnowledgeBase Instance { get; private set; }

    const string KnowledgePathSuffix = "AI/game_knowledge.md";
    const int    DefaultMaxRetrieved = 4;

    readonly List<KnowledgeEntry>          _entries          = new List<KnowledgeEntry>();
    readonly Dictionary<string, string>    _personasByPhase  = new Dictionary<string, string>();
    // Additional StreamingAssets-relative suffixes merged in via MergeKnowledge.
    // Tracked so Reload() re-merges them after wiping _entries/_personasByPhase
    // — otherwise hot-reloading the base file in the Editor would silently
    // drop the gated ORG content from memory mid-session.
    readonly List<string>                  _mergedSources    = new List<string>();
    bool                                   _loaded;
    DateTime                               _lastLoadUtc;

    // Story phase. Advances forward only via SetStoryPhase (call from quest
    // / story event scripts when the player crosses a beat). RestoreStoryPhase
    // is for save/load and allows any direction. Defaults to Phase1.
    public StoryPhase CurrentPhase { get; private set; } = StoryPhase.Phase1_Loyal;

    public event Action<StoryPhase> OnPhaseChanged;

    // Forward-only advance. A no-op (with a warning) if asked to go backwards
    // or stay put — story phases should not regress during play.
    public void SetStoryPhase(StoryPhase next)
    {
        if ((int)next <= (int)CurrentPhase)
        {
            if ((int)next < (int)CurrentPhase)
                Debug.LogWarning($"[GameKnowledgeBase] SetStoryPhase({next}) ignored — phase only advances forward (currently {CurrentPhase}).");
            return;
        }
        CurrentPhase = next;
        Debug.Log($"[GameKnowledgeBase] Story phase → {CurrentPhase}");
        OnPhaseChanged?.Invoke(CurrentPhase);
    }

    // Save/load path — accepts any direction. Does NOT fire OnPhaseChanged
    // because subscribers shouldn't react to a load as if it were a beat.
    public void RestoreStoryPhase(StoryPhase phase) => CurrentPhase = phase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("GameKnowledgeBase");
        DontDestroyOnLoad(go);
        go.AddComponent<GameKnowledgeBase>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Public API ─────────────────────────────────────────────────

    public string GetPersona(StoryPhase phase)
    {
        EnsureLoadedAndFresh();
        var key = PhaseKey(phase);
        if (_personasByPhase.TryGetValue(key, out var p)) return p;
        // Fallback: any persona at all is better than silence.
        foreach (var kv in _personasByPhase) return kv.Value;
        return "";
    }

    public string GetCoreCanon(StoryPhase phase)
    {
        EnsureLoadedAndFresh();
        var sb = new StringBuilder();
        foreach (var e in _entries)
        {
            if (e.mode != KnowledgeMode.Core) continue;
            if (!ActiveInPhase(e.phases, phase)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(e.body);
        }
        return sb.ToString();
    }

    public List<KnowledgeEntry> Retrieve(string playerMessage, StoryPhase phase, int maxEntries = DefaultMaxRetrieved)
    {
        EnsureLoadedAndFresh();
        var msg = (playerMessage ?? "").ToLowerInvariant();
        var scored = new List<(KnowledgeEntry e, int score)>();
        foreach (var e in _entries)
        {
            if (e.mode != KnowledgeMode.Grounding) continue;
            if (!ActiveInPhase(e.phases, phase)) continue;
            int score = 0;
            foreach (var k in e.keywords)
                if (k.Length > 0 && msg.Contains(k)) score++;
            if (score > 0) scored.Add((e, score));
        }
        scored.Sort((a, b) =>
        {
            int c = b.score.CompareTo(a.score);
            if (c != 0) return c;
            return a.e.body.Length.CompareTo(b.e.body.Length); // shorter body wins ties
        });
        int take = Math.Min(maxEntries, scored.Count);
        var result = new List<KnowledgeEntry>(take);
        for (int i = 0; i < take; i++) result.Add(scored[i].e);
        return result;
    }

    public string TryVerbatim(string playerMessage, StoryPhase phase)
    {
        EnsureLoadedAndFresh();
        var msg = (playerMessage ?? "").ToLowerInvariant();
        foreach (var e in _entries)
        {
            if (e.mode != KnowledgeMode.Verbatim) continue;
            if (!ActiveInPhase(e.phases, phase)) continue;
            foreach (var intent in e.intents)
                if (intent.Length > 0 && msg.Contains(intent)) return e.body;
        }
        return null;
    }

    // For debug / inspector tooling.
    public IReadOnlyList<KnowledgeEntry>      AllEntries        => _entries;
    public IReadOnlyDictionary<string, string> PersonasByPhase  => _personasByPhase;

    // Force a reload (useful for debug commands). Re-loads the base file
    // AND every previously-merged source (in original merge order) so the
    // resulting in-memory state matches what was loaded before the reload.
    public void Reload()
    {
        var toRemerge = new List<string>(_mergedSources);
        _mergedSources.Clear();
        _loaded = false;
        EnsureLoadedAndFresh();
        foreach (var suffix in toRemerge) MergeKnowledge(suffix);
    }

    // Merges an additional knowledge file (StreamingAssets-relative suffix
    // like "AI/game_knowledge_org_reveal.md") into the in-memory entry +
    // persona tables. Idempotent — calling twice with the same suffix is
    // a no-op.
    //
    // This is the gate the AI knowledge-revamp uses to keep story-spoiler
    // content out of the LLM context until a story flag flips. The ORG
    // file's bytes do not enter the process unless and until this method
    // is called with its suffix — making it impossible for the LLM to
    // reveal content it was never given.
    public void MergeKnowledge(string streamingAssetsSuffix)
    {
        if (string.IsNullOrEmpty(streamingAssetsSuffix)) return;
        if (_mergedSources.Contains(streamingAssetsSuffix))
        {
            Debug.Log($"[GameKnowledgeBase] MergeKnowledge('{streamingAssetsSuffix}') — already merged, ignoring.");
            return;
        }
        EnsureLoadedAndFresh(); // make sure the base is loaded first

        var path = Path.Combine(Application.streamingAssetsPath, streamingAssetsSuffix);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[GameKnowledgeBase] MergeKnowledge: no file at {path} — nothing merged.");
            return;
        }

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e)
        {
            Debug.LogError($"[GameKnowledgeBase] MergeKnowledge: failed to read {path}: {e.Message}");
            return;
        }

        int beforeEntries  = _entries.Count;
        int beforePersonas = _personasByPhase.Count;

        try
        {
            Parse(text); // Parse appends to _entries and _personasByPhase (no clear)
            _mergedSources.Add(streamingAssetsSuffix);
            Debug.Log($"[GameKnowledgeBase] Merged {streamingAssetsSuffix}: +{_entries.Count - beforeEntries} entries, +{_personasByPhase.Count - beforePersonas} personas (now {_entries.Count} / {_personasByPhase.Count} total).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameKnowledgeBase] MergeKnowledge: parse failure on {path}: {e.Message}\n{e.StackTrace}");
        }
    }

    // ── Load + parse ───────────────────────────────────────────────

    void EnsureLoadedAndFresh()
    {
        var path = Path.Combine(Application.streamingAssetsPath, KnowledgePathSuffix);
#if UNITY_EDITOR
        // In the editor, re-parse whenever the file is newer than our last
        // load. Authors can tune lore live in Play mode.
        if (_loaded && File.Exists(path))
        {
            var t = File.GetLastWriteTimeUtc(path);
            if (t > _lastLoadUtc) _loaded = false;
        }
#endif
        if (_loaded) return;
        Load(path);
    }

    void Load(string path)
    {
        _entries.Clear();
        _personasByPhase.Clear();
        _loaded = true; // Mark loaded even on failure so we don't thrash File.Exists.

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[GameKnowledgeBase] No knowledge file at {path} — AI will run with empty knowledge base.");
            return;
        }

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e)
        {
            Debug.LogError($"[GameKnowledgeBase] Failed to read {path}: {e.Message}");
            return;
        }

        try
        {
            Parse(text);
            _lastLoadUtc = File.GetLastWriteTimeUtc(path);
            Debug.Log($"[GameKnowledgeBase] Loaded {_entries.Count} entries, {_personasByPhase.Count} personas from {path}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameKnowledgeBase] Parse failure on {path}: {e.Message}\n{e.StackTrace}");
        }
    }

    void Parse(string text)
    {
        var lines = text.Split('\n');

        string currentHeader = null;
        var meta = new Dictionary<string, string>();
        var body = new StringBuilder();
        bool inBody = false;

        void Flush()
        {
            if (currentHeader != null) ProcessBlock(currentHeader, meta, body.ToString());
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // File comment — single-hash lines are stripped at any position
            // (pre-amble, between blocks, even inside a body). Lets authors
            // use section dividers and inline editorial notes without
            // polluting the prompt. Headers use "## " so they're unaffected.
            if (line.StartsWith("# ") || line == "#") continue;

            if (IsHeaderLine(line, out var headerBody))
            {
                Flush();
                currentHeader = headerBody;
                meta = new Dictionary<string, string>();
                body = new StringBuilder();
                inBody = false;
                continue;
            }

            if (currentHeader == null) continue; // pre-amble (no header yet)

            if (!inBody)
            {
                var trimmed = line.Trim();
                if (trimmed == "---") { inBody = true; continue; }
                if (trimmed.Length == 0) continue;
                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    var key = trimmed.Substring(0, colon).Trim().ToLowerInvariant();
                    var val = trimmed.Substring(colon + 1).Trim();
                    meta[key] = val;
                }
                // else: unrecognized metadata line, ignore silently
            }
            else
            {
                body.Append(line).Append('\n');
            }
        }
        Flush();
    }

    static bool IsHeaderLine(string line, out string headerBody)
    {
        headerBody = null;
        if (!line.StartsWith("## ")) return false;
        var rest = line.Substring(3);
        if (rest.StartsWith("PERSONA:", StringComparison.OrdinalIgnoreCase) ||
            rest.StartsWith("ENTRY:",   StringComparison.OrdinalIgnoreCase))
        {
            headerBody = rest;
            return true;
        }
        return false;
    }

    void ProcessBlock(string header, Dictionary<string, string> meta, string rawBody)
    {
        var body = rawBody.TrimEnd('\n', '\r', ' ', '\t');

        int colonIdx = header.IndexOf(':');
        if (colonIdx <= 0)
        {
            Debug.LogWarning($"[GameKnowledgeBase] Malformed block header (no ':'): '{header}' — skipped.");
            return;
        }
        var kind  = header.Substring(0, colonIdx).Trim().ToUpperInvariant();
        var title = header.Substring(colonIdx + 1).Trim();

        if (kind == "PERSONA")
        {
            if (string.IsNullOrEmpty(title))
            {
                Debug.LogWarning("[GameKnowledgeBase] PERSONA block missing phase title — skipped.");
                return;
            }
            _personasByPhase[title.ToLowerInvariant()] = body;
            return;
        }

        if (kind != "ENTRY")
        {
            Debug.LogWarning($"[GameKnowledgeBase] Unknown block kind '{kind}' in '{header}' — skipped.");
            return;
        }

        // ── ENTRY parsing ─────────────────────────────────────
        var modeStr = GetMeta(meta, "mode", "grounding").ToLowerInvariant();
        KnowledgeMode mode;
        switch (modeStr)
        {
            case "core":      mode = KnowledgeMode.Core;      break;
            case "grounding": mode = KnowledgeMode.Grounding; break;
            case "verbatim":  mode = KnowledgeMode.Verbatim;  break;
            default:
                Debug.LogWarning($"[GameKnowledgeBase] Unknown mode '{modeStr}' on ENTRY '{title}' — skipped.");
                return;
        }

        var entry = new KnowledgeEntry
        {
            title    = title,
            mode     = mode,
            phases   = ParseList(GetMeta(meta, "phase",    "all")),
            keywords = ParseList(GetMeta(meta, "keywords", "")),
            intents  = ParseList(GetMeta(meta, "intent",   "")),
            body     = body,
        };

        if (entry.mode == KnowledgeMode.Verbatim && entry.intents.Count == 0)
            Debug.LogWarning($"[GameKnowledgeBase] Verbatim ENTRY '{title}' has no `intent:` — will never fire.");
        if (entry.mode == KnowledgeMode.Grounding && entry.keywords.Count == 0)
            Debug.LogWarning($"[GameKnowledgeBase] Grounding ENTRY '{title}' has no `keywords:` — will never be retrieved.");

        _entries.Add(entry);
    }

    // ── Helpers ────────────────────────────────────────────────────

    static string GetMeta(Dictionary<string, string> meta, string key, string fallback)
        => meta.TryGetValue(key, out var v) ? v : fallback;

    static List<string> ParseList(string raw)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "-") return list;
        foreach (var part in raw.Split(','))
        {
            var t = part.Trim().ToLowerInvariant();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    static bool ActiveInPhase(List<string> phases, StoryPhase phase)
    {
        if (phases == null || phases.Count == 0) return true;
        if (phases.Contains("all")) return true;
        return phases.Contains(PhaseKey(phase));
    }

    public static string PhaseKey(StoryPhase phase)
    {
        switch (phase)
        {
            case StoryPhase.Phase1_Loyal:     return "phase1";
            case StoryPhase.Phase2_Uneasy:    return "phase2";
            case StoryPhase.Phase3_Resistant: return "phase3";
            default:                          return "phase1";
        }
    }
}
