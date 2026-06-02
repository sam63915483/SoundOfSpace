using System.Text.RegularExpressions;
using UnityEngine;

// Replaces {TOKEN} placeholders in authored knowledge text with live game values.
// Unknown tokens pass through unchanged so a typo is visible, not silently blank.
//
// Adding a new token = one new case in ResolveOne. Tokens are the one part of
// "the brain" (the game_knowledge.md file) that isn't pure text and requires
// a code change to extend.
public static class TokenResolver
{
    static readonly Regex TokenRegex = new Regex(@"\{([A-Z][A-Z0-9_]*)\}", RegexOptions.Compiled);

    // Cached lookups. Once per chat turn is not per-frame, but the lookups are
    // cheap when cached and Unity's overloaded `== null` handles destroyed objects.
    static PlayerController _cachedPC;

    public static string Resolve(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return TokenRegex.Replace(text, m =>
        {
            var resolved = ResolveOne(m.Groups[1].Value);
            return resolved ?? m.Value;
        });
    }

    static string ResolveOne(string token)
    {
        switch (token)
        {
            case "ASTRONAUT_NUMBER":
                return (GetPlayerDeaths() + 1).ToString();

            case "PLAYER_DEATHS":
                return GetPlayerDeaths().ToString();

            case "CURRENT_PLANET":
                return GetCurrentPlanet();

            case "STORY_PHASE":
                return GetStoryPhaseLabel();

            // Player's chosen name. Defaults to "Player" if first-contact
            // never ran or the player declined to set one.
            case "PLAYER_NAME":
                return NameStore.ResolvedPlayerName;

            // AI's chosen name. Defaults to "Assistant" if the player
            // declined to name the AI during first-contact.
            case "AI_NAME":
                return NameStore.ResolvedAIName;

            default:
                return null; // pass through unchanged
        }
    }

    // ── Lookups ──────────────────────────────────────────────────

    static int GetPlayerDeaths()
    {
        return ResourceManager.Instance != null ? ResourceManager.Instance.TotalDeaths : 0;
    }

    static string GetCurrentPlanet()
    {
        if (_cachedPC == null) _cachedPC = Object.FindObjectOfType<PlayerController>();
        if (_cachedPC == null) return "Deep Space";
        var body = _cachedPC.ReferenceBody;
        return body != null && !string.IsNullOrEmpty(body.bodyName) ? body.bodyName : "Deep Space";
    }

    static string GetStoryPhaseLabel()
    {
        var kb = GameKnowledgeBase.Instance;
        if (kb == null) return "Loyal";
        switch (kb.CurrentPhase)
        {
            case StoryPhase.Phase1_Loyal:     return "Loyal";
            case StoryPhase.Phase2_Uneasy:    return "Uneasy";
            case StoryPhase.Phase3_Resistant: return "Resistant";
            default:                          return "Loyal";
        }
    }
}
