#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TestMergeKnowledge
{
    [MenuItem("Tools/LLM/Test MergeKnowledge (dry-run)")]
    public static void Run()
    {
        var kb = GameKnowledgeBase.Instance;
        if (kb == null) { Debug.LogError("Run in Play mode — GameKnowledgeBase singleton not present."); return; }

        int e0 = kb.AllEntries.Count;
        int p0 = kb.PersonasByPhase.Count;
        Debug.Log($"[TestMergeKnowledge] BEFORE: {e0} entries, {p0} personas");

        // Will warn-no-file the first time (we haven't created the ORG file
        // yet, in this task). Confirms the warn-and-return path works.
        kb.MergeKnowledge("AI/game_knowledge_org_reveal.md");

        int e1 = kb.AllEntries.Count;
        int p1 = kb.PersonasByPhase.Count;
        Debug.Log($"[TestMergeKnowledge] AFTER : {e1} entries, {p1} personas");
    }
}
#endif
