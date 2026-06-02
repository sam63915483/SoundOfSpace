using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central dispatcher for the FIXED 7-effect vocabulary (GDD §1.3). Used by both dialogue
/// responses and objective onComplete. Each effect only mutates StoryDirector state.
/// Resist adding new kinds — that constraint is what keeps future content cheap.
/// </summary>
public static class DialogueEffects
{
    public static void Apply(IEnumerable<Effect> effects)
    {
        if (effects == null) return;
        foreach (var e in effects) Apply(e);
    }

    public static void Apply(Effect e)
    {
        if (e == null) return;
        var sd = StoryDirector.Instance;
        if (sd == null) { Debug.LogWarning("[Effects] No StoryDirector; dropping " + e.kind); return; }

        switch (e.kind)
        {
            case "SetFlag":          sd.SetFlag(e.strArg, e.boolArg); break;
            case "AdvanceStory":     sd.SetStoryStep(ParseStep(e)); break;
            case "AddTrust":         sd.AddTrust(e.numArg); break;
            case "StartObjective":   sd.StartObjective(e.strArg); break;
            case "CompleteObjective":sd.CompleteObjective(e.strArg); break;
            case "UnlockDialogue":   sd.UnlockQuestion(e.strArg); break;
            case "TriggerEnding":    Debug.Log("[Effects] TriggerEnding(" + e.strArg + ") — no-op this slice."); break;
            default:                 Debug.LogWarning("[Effects] Unknown effect kind: " + e.kind); break;
        }
    }

    // AdvanceStory accepts either a step name in strArg ("NeedsShelter") or an int in numArg.
    static StoryStep ParseStep(Effect e)
    {
        if (!string.IsNullOrEmpty(e.strArg) && System.Enum.TryParse(e.strArg, out StoryStep byName)) return byName;
        return (StoryStep)Mathf.RoundToInt(e.numArg);
    }
}
