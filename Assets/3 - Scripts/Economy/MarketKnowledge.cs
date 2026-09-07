using System.Collections.Generic;

/// <summary>
/// What THIS player has learned about the markets: which boards they have stood
/// in front of. (docs/Handoff_PlanetEconomy_Fuel_Fishing_v2.md §7.3.)
///
/// The rule that makes trade routes a thing you LEARN: the phone's MARKETS page
/// shows exactly what a board said, and only for boards the player has read.
/// It never tells you what you have not seen. So this is deliberately tiny — a
/// set of planet names — and PLAYER state, not world state: in co-op each
/// player carries their own (it rides the per-player save block alongside the
/// fish bag), so a route one player scouted stays theirs to share or not.
/// </summary>
public static class MarketKnowledge
{
    static readonly HashSet<string> _seen = new HashSet<string>();

    /// <summary>Fires when a new board is read, so an open MARKETS page can refresh.</summary>
    public static event System.Action Changed;

    public static bool HasSeen(string body) => !string.IsNullOrEmpty(body) && _seen.Contains(body);

    public static int SeenCount => _seen.Count;

    /// <summary>The player is close enough to read this planet's board.</summary>
    public static void NoteSeen(string body)
    {
        if (string.IsNullOrEmpty(body)) return;
        if (_seen.Add(body)) Changed?.Invoke();
    }

    public static void ResetAll()
    {
        _seen.Clear();
        Changed?.Invoke();
    }

    public static void FillSave(MarketKnowledgeSave s)
    {
        if (s == null) return;
        s.bodiesSeen.Clear();
        s.bodiesSeen.AddRange(_seen);
    }

    public static void ApplySave(MarketKnowledgeSave s)
    {
        _seen.Clear();
        if (s?.bodiesSeen != null)
            foreach (var b in s.bodiesSeen) if (!string.IsNullOrEmpty(b)) _seen.Add(b);
        Changed?.Invoke();
    }
}
