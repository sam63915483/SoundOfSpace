using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central dispatcher for dialogue effects, used by phone conversations
/// (DialogueRunner), objective onComplete, and the world-NPC graphs
/// (NpcGraphWalker).
///
/// Story vocabulary (GDD §1.3, mutates StoryDirector only):
///   SetFlag strArg boolArg · AdvanceStory · AddTrust numArg · StartObjective ·
///   CompleteObjective · UnlockDialogue · TriggerEnding
/// Game vocabulary (added 2026-09-05 for the NPC graphs — Dialogue Studio
/// mirrors it in vocab.json; keep them in lockstep):
///   AddMoney numArg · SpendMoney numArg · GiveItem strArg numArg ·
///   TakeItem strArg numArg · AddCounter strArg numArg · SetCounter strArg numArg ·
///   HalSay strArg · Custom strArg  (the hosting NPC's own action, e.g. kidFollow)
/// Add a kind here AND in the studio, or the browser preview can't show it.
/// </summary>
public static class DialogueEffects
{
    public static void Apply(IEnumerable<Effect> effects) => Apply(effects, null);

    public static void Apply(IEnumerable<Effect> effects, Func<string, bool> customAction)
    {
        if (effects == null) return;
        foreach (var e in effects) Apply(e, customAction);
    }

    public static void Apply(Effect e) => Apply(e, null);

    public static void Apply(Effect e, Func<string, bool> customAction)
    {
        if (e == null) return;
        var sd = StoryDirector.Instance;

        switch (e.kind)
        {
            // ── story (StoryDirector) ──────────────────────────────────────
            case "SetFlag":           if (NeedSd(sd, e)) sd.SetFlag(e.strArg, e.boolArg); break;
            case "AdvanceStory":      if (NeedSd(sd, e)) sd.SetStoryStep(ParseStep(e)); break;
            case "AddTrust":          if (NeedSd(sd, e)) sd.AddTrust(e.numArg); break;
            case "StartObjective":    if (NeedSd(sd, e)) sd.StartObjective(e.strArg); break;
            case "CompleteObjective": if (NeedSd(sd, e)) sd.CompleteObjective(e.strArg); break;
            case "UnlockDialogue":    if (NeedSd(sd, e)) sd.UnlockQuestion(e.strArg); break;
            case "TriggerEnding":     Debug.Log("[Effects] TriggerEnding(" + e.strArg + ") — no-op this slice."); break;
            case "AddCounter":        if (NeedSd(sd, e)) sd.AddCounter(e.strArg, Mathf.RoundToInt(e.numArg)); break;
            case "SetCounter":        if (NeedSd(sd, e)) sd.SetCounter(e.strArg, Mathf.RoundToInt(e.numArg)); break;

            // ── game ──────────────────────────────────────────────────────
            case "AddMoney":
                if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(Mathf.RoundToInt(e.numArg));
                else Debug.LogWarning("[Effects] AddMoney: no PlayerWallet.");
                break;
            case "SpendMoney":
                if (PlayerWallet.Instance == null) Debug.LogWarning("[Effects] SpendMoney: no PlayerWallet.");
                else if (!PlayerWallet.Instance.SpendMoney(Mathf.RoundToInt(e.numArg)))
                    Debug.LogWarning($"[Effects] SpendMoney {e.numArg}: not enough money — gate the response with MoneyAtLeast.");
                break;
            case "GiveItem":
                if (Hotbar.Instance != null && DialogueConditions.TryParseItem(e.strArg, out var give))
                {
                    int left = Hotbar.Instance.AddResource(give, Mathf.Max(1, Mathf.RoundToInt(e.numArg)));
                    if (left > 0) Debug.LogWarning($"[Effects] GiveItem {e.strArg}: {left} did not fit in the hotbar.");
                }
                break;
            case "TakeItem":
                if (Hotbar.Instance != null && DialogueConditions.TryParseItem(e.strArg, out var take)
                    && !Hotbar.Instance.SpendResource(take, Mathf.Max(1, Mathf.RoundToInt(e.numArg))))
                    Debug.LogWarning($"[Effects] TakeItem {e.strArg}: player did not have enough — gate with HasItem.");
                break;
            case "HalSay":
                if (HALCommentator.Instance != null && !string.IsNullOrEmpty(e.strArg))
                    HALCommentator.Instance.VolunteerExternal(TokenResolver.Resolve(e.strArg));
                break;
            case "Custom":
                if (customAction == null || !customAction(e.strArg))
                    Debug.LogWarning("[Effects] Custom action not handled by this NPC: " + e.strArg);
                break;

            default: Debug.LogWarning("[Effects] Unknown effect kind: " + e.kind); break;
        }
    }

    static bool NeedSd(StoryDirector sd, Effect e)
    {
        if (sd != null) return true;
        Debug.LogWarning("[Effects] No StoryDirector; dropping " + e.kind);
        return false;
    }

    // AdvanceStory accepts either a step name in strArg ("NeedsShelter") or an int in numArg.
    static StoryStep ParseStep(Effect e)
    {
        if (!string.IsNullOrEmpty(e.strArg) && System.Enum.TryParse(e.strArg, out StoryStep byName)) return byName;
        return (StoryStep)Mathf.RoundToInt(e.numArg);
    }
}
