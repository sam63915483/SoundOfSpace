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
            .ThenBy(t => t.m.isoTimestamp ?? "") // oldest first
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
