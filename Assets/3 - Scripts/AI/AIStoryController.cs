using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bridges story flags (EarlyGameProgress.ORG_Reveal) to AI knowledge state
// (GameKnowledgeBase). When the ORG_Reveal flag flips true — either via the
// cheat key, the future story trigger, or a save load — this controller
// merges game_knowledge_org_reveal.md into GameKnowledgeBase and advances
// StoryPhase to Phase 2 (Uneasy). The advance is one-way; subsequent story
// beats will call SetStoryPhase(Phase3_Resistant) directly.
//
// Auto-singleton, mirrors LLMService / GameKnowledgeBase. Seeded in
// MainMenuController.EnsureGameplaySingletons per CLAUDE.md's MainMenu trap.
//
// See docs/AI_Companion_Revamp_Plan.md §7 and
// docs/superpowers/plans/2026-05-23-ai-companion-knowledge-gating-and-org-placeholder.md.
public class AIStoryController : MonoBehaviour
{
    public static AIStoryController Instance { get; private set; }

    const string ORGKnowledgeSuffix = "AI/game_knowledge_org_reveal.md";

    // True once we've merged the ORG knowledge file in. Tracks rising edges
    // — flag stays true within a session even if EarlyGameProgress.ORG_Reveal
    // is somehow flipped back to false (the cheat key toggles, which we
    // ignore on the falling edge — once revealed, the knowledge stays
    // available).
    bool _orgKnowledgeMerged;

    // Fires once per session, the first time ORG_Reveal flips true (or on
    // Awake if a save with the flag set is loaded). Future systems (HAL
    // commentator, sabotage hooks) subscribe here.
    public event Action OnORGRevealed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        if (Instance != null) return;
        var go = new GameObject("AIStoryController");
        DontDestroyOnLoad(go);
        go.AddComponent<AIStoryController>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // Rising-edge detection. EarlyGameProgress is a static class — no
        // event, no observer pattern; polling is the simplest hook. One
        // bool compare per frame is free.
        if (!_orgKnowledgeMerged && EarlyGameProgress.ORG_Reveal)
        {
            RevealORG();
        }
    }

    void RevealORG()
    {
        _orgKnowledgeMerged = true;
        var kb = GameKnowledgeBase.Instance;
        if (kb == null)
        {
            Debug.LogWarning("[AIStoryController] RevealORG fired but GameKnowledgeBase.Instance is null — will retry on next Update.");
            _orgKnowledgeMerged = false; // retry
            return;
        }

        kb.MergeKnowledge(ORGKnowledgeSuffix);

        // Advance the story phase so Phase 2 persona starts being used by
        // BuildSystemPrompt. SetStoryPhase only advances forward, so calling
        // this on a save already at Phase 3 is a no-op (with a warn). Future
        // story beats call kb.SetStoryPhase(Phase3_Resistant) directly when
        // the player crosses the next arc beat.
        kb.SetStoryPhase(StoryPhase.Phase2_Uneasy);

        Debug.Log("[AIStoryController] ORG_Reveal handled: merged gated knowledge + advanced to Phase 2.");

        try { OnORGRevealed?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
