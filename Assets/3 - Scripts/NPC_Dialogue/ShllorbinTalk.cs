using System.Collections;
using UnityEngine;

/// <summary>
/// SHLLORBIN -- the lost kid. Put this next to an AuthoredNPCSpawner on the KID
/// empty (autoSpawn is switched off by LostKidQuest, which decides where he
/// starts: lost spot, behind the player, or home, from the save flags).
///   - name not learned -> scared of a stranger; nothing happens
///   - name learned     -> he recognises it ("tamed"); offer "Follow me" -> follows
///   - following        -> "are we there yet"; can be told to wait / keep going
///   - returned         -> home lines
/// Draft voice -- Sam punches up in the Inspector.
/// </summary>
public class ShllorbinTalk : AuthoredNPCTalk
{
    [Header("Lines -- scared (you don't know his name)")]
    [TextArea(2, 5)]
    public string[] linesScared =
    {
        "...Who are you? Dad says don't talk to strangers. ESPECIALLY tall ones.",
        "I'm not lost. I'm exploring. Go away.",
    };

    [Header("Lines -- you say his name (tamed)")]
    [TextArea(2, 5)]
    public string[] linesRecognise =
    {
        "How do you know my name?! ...Did Dad send you? Is he mad? He's mad, isn't he.",
        "I only went to look at the sparkly rocks. Then the rocks were everywhere and none of them were home.",
    };

    [Header("Lines -- agrees to follow")]
    [TextArea(2, 5)]
    public string[] linesFollow =
    {
        "Okay. Okay! Don't walk too fast. My legs are little.",
    };

    [Header("Lines -- while following (one at random)")]
    [TextArea(2, 5)]
    public string[] linesFollowing =
    {
        "Are we there yet?",
        "Is it much further? I told you my legs were little.",
        "If Dad asks, I was NEVER near the water.",
    };

    [Header("Lines -- told to wait")]
    [TextArea(2, 5)]
    public string[] linesWait =
    {
        "Fine. I'll wait here. I'm good at waiting. I'm not good at waiting.",
    };

    [Header("Lines -- home (one at random)")]
    [TextArea(2, 5)]
    public string[] linesHome =
    {
        "Dad says I'm grounded. Grounded on a PLANET. That's just... standing.",
        "Thanks for finding me. Next time I'll get lost somewhere closer.",
    };

    [Header("Choice labels")]
    public string choiceUseName = "Shllorbin? Your dad Floorbin sent me.";
    public string choiceFollow = "Follow me. I'll take you home.";
    public string choiceNotNow = "Not right now.";
    public string choiceKeepGoing = "Keep following me.";
    public string choiceWaitHere = "Wait here for a bit.";
    public string choiceLeave = "Leave";

    LostKidQuest _quest;

    protected override void Awake()
    {
        base.Awake();
        _quest = FindObjectOfType<LostKidQuest>(true);
    }

    // Dialogue Studio graph hooks (npc_shllorbin.json): the kid's live
    // follow state is a Probe, the follow verbs are Custom effects.
    protected override bool GraphProbe(string name) =>
        name == "kidFollowing" && _quest != null && _quest.IsFollowing;

    protected override bool GraphAction(string name)
    {
        switch (name)
        {
            case "kidFollow":     if (_quest != null) _quest.BeginFollow(); return true;
            case "kidStopFollow": if (_quest != null) _quest.StopFollow();  return true;
        }
        return false;
    }

    protected override IEnumerator Conversation()
    {
        var g = Graph;
        if (g != null) { yield return RunGraph(g); yield break; }

        if (_quest == null) { yield return Speak(linesScared); yield break; }

        if (Flag(LostKidQuest.FlagReturned))
        {
            yield return Speak(OneOf(linesHome));
            yield break;
        }

        if (_quest.IsFollowing)
        {
            yield return Speak(OneOf(linesFollowing));
            yield return Choose(choiceKeepGoing, choiceWaitHere);
            if (LastChoice == 1)
            {
                _quest.StopFollow();
                yield return Speak(linesWait);
            }
            yield break;
        }

        if (!Flag(LostKidQuest.FlagNameLearned))
        {
            // Stranger: he won't come. The gate is learning the name from Floorbin.
            yield return Speak(linesScared);
            yield return Choose(choiceLeave);
            yield break;
        }

        // Tamed by the name.
        yield return Choose(choiceUseName, choiceLeave);
        if (LastChoice != 0) yield break;
        yield return Speak(linesRecognise);
        yield return Choose(choiceFollow, choiceNotNow);
        if (LastChoice != 0) yield break;
        yield return Speak(linesFollow);
        _quest.BeginFollow();
    }
}
