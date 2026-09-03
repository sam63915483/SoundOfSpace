using System.Collections;
using UnityEngine;

/// <summary>
/// FLOORBIN -- the frantic parent (Humble Abode). Put this next to an
/// AuthoredNPCSpawner on the PARENT empty. States, by quest flags:
///   - name not learned          -> the search speech; you learn the kid's NAME
///   - learned, kid still lost   -> waiting lines
///   - kid following you         -> "bring him here!"
///   - kid returned, not thanked -> the thank-you + the bounty-fish sighting
///   - thanked                   -> idle lines that re-state the spot
/// The reunion celebration (LostKidQuest) force-starts the thank-you.
/// Draft voice -- Sam punches up in the Inspector.
/// </summary>
public class FloorbinTalk : AuthoredNPCTalk
{
    [Header("Lines -- searching (first talk; teaches the name)")]
    [TextArea(2, 5)]
    public string[] linesSearching =
    {
        "Have you seen a kid? About yea high, big eyes, won't stop humming? My kid. SHLLORBIN.",
        "I turned around for ONE second. He wandered off toward the hills. Or the water. He wanders. That is the whole problem.",
        "If you find him, say his name. Shllorbin. He won't go with a stranger, but he'll know you've talked to me.",
        "Please. I'll make it worth your while. I know things about this lake that nobody else does.",
    };

    [Header("Lines -- waiting (name learned, kid still lost; one at random)")]
    [TextArea(2, 5)]
    public string[] linesWaiting =
    {
        "Shllorbin! SHLLORBIN! ...Nothing. Any luck out there?",
        "He likes high ground. And low ground. Honestly, he likes ground.",
        "Every minute he's out there my antennae go a little greyer.",
    };

    [Header("Lines -- the kid is following you")]
    [TextArea(2, 5)]
    public string[] linesKidBehindYou =
    {
        "Is that... is he behind you? SHLLORBIN! Bring him here, bring him HERE!",
    };

    [Header("Lines -- thank you + the bounty fish sighting")]
    [TextArea(2, 5)]
    public string[] linesThankYou =
    {
        "You brought him back. You actually brought him back.",
        "I owe you. And I pay my debts. With information, mostly, because that's what I've got.",
        "North of here, up the shore of this lake, I saw a fish. Not a fish. A FISH. Big as a shuttle. Teeth like a rockfall.",
        "I'm not saying it ate a kid once. I'm just saying nobody has seen little Grebnik since.",
        "Cast a line up there. Land that thing and the fish vendor will pay a fortune for it. Just... don't let it land YOU.",
    };

    [Header("Lines -- after the quest (one at random)")]
    [TextArea(2, 5)]
    public string[] linesAfter =
    {
        "The big fish? North, up the lake shore. You can't miss the spot. The water goes quiet there.",
        "Shllorbin is grounded. Forever. Probably.",
        "Still can't believe you found him. Still can't believe he was on a planet the whole time.",
    };

    protected override IEnumerator Conversation()
    {
        bool nameLearned = Flag(LostKidQuest.FlagNameLearned);
        bool following   = Flag(LostKidQuest.FlagFollowing);
        bool returned    = Flag(LostKidQuest.FlagReturned);
        bool thanked     = Flag(LostKidQuest.FlagSpotKnown);

        if (returned && !thanked)
        {
            yield return ThankYou();
        }
        else if (returned)
        {
            yield return Speak(OneOf(linesAfter));
        }
        else if (following)
        {
            yield return Speak(linesKidBehindYou);
        }
        else if (!nameLearned)
        {
            yield return Speak(linesSearching);
            // The name is the knowledge gate: the kid only trusts someone who has it.
            SetFlag(LostKidQuest.FlagNameLearned, true);
        }
        else
        {
            yield return Speak(OneOf(linesWaiting));
        }
    }

    IEnumerator ThankYou()
    {
        yield return Speak(linesThankYou);
        SetFlag(LostKidQuest.FlagSpotKnown, true);
        if (HALCommentator.Instance != null)
            HALCommentator.Instance.VolunteerExternal("Noted: a large fish, north along the lake shore.");
    }
}
