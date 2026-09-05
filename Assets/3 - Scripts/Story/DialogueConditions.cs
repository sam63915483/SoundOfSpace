using System;
using UnityEngine;

/// <summary>
/// Evaluates the Condition gates on npc_*.json responses and routes. The
/// vocabulary here is mirrored by tools/dialogue-studio (vocab.json + the
/// browser player) — add a kind in all three places or the studio can't
/// preview it.
///
/// "Probe" is the escape hatch: a name the NPC script itself answers
/// (kidFollowing, traxOwned, canCarryStick…). Unknown probes read false and
/// log once, so a typo in the JSON shows up in the Console, not as a silent
/// dead branch.
/// </summary>
public static class DialogueConditions
{
    public static bool AllPass(Condition[] conditions, Func<string, bool> probe)
    {
        if (conditions == null) return true;
        for (int i = 0; i < conditions.Length; i++)
            if (conditions[i] != null && !Passes(conditions[i], probe)) return false;
        return true;
    }

    public static bool Passes(Condition c, Func<string, bool> probe)
    {
        bool r;
        var sd = StoryDirector.Instance;
        switch (c.kind)
        {
            case "Flag":
                r = sd != null && sd.GetFlag(c.arg);
                break;
            case "MoneyAtLeast":
                r = PlayerWallet.Instance != null && PlayerWallet.Instance.Money >= Mathf.RoundToInt(c.num);
                break;
            case "HasItem":
                r = Hotbar.Instance != null
                    && TryParseItem(c.arg, out var item)
                    && Hotbar.Instance.GetResourceTotal(item) >= Mathf.Max(1, Mathf.RoundToInt(c.num));
                break;
            case "CounterAtLeast":
                r = sd != null && sd.GetCounter(c.arg) >= Mathf.RoundToInt(c.num);
                break;
            case "ObjectiveDone":
                r = sd != null && sd.IsObjectiveComplete(c.arg);
                break;
            case "Probe":
                r = probe != null && probe(c.arg);
                break;
            case "Chance":   // num = percent; "50" passes half the time
                r = UnityEngine.Random.value * 100f < c.num;
                break;
            default:
                Debug.LogWarning("[Dialogue] Unknown condition kind: " + c.kind);
                r = false;
                break;
        }
        return c.negate ? !r : r;
    }

    public static bool TryParseItem(string name, out Hotbar.ItemId item)
    {
        if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out item)) return true;
        Debug.LogWarning("[Dialogue] Unknown item id: " + name);
        item = Hotbar.ItemId.None;
        return false;
    }
}
