/// <summary>
/// STUB — the seam for the future radio-interview system, nothing more
/// (first-meeting revamp handoff §7, 2026-08-30).
///
/// The idea it reserves space for: an early radio interview lets the player
/// present themselves to the planet, and every NPC's greeting can then carry
/// one optional prefix line coloured by what they heard. None = the player
/// never gave an impression = no prefix line anywhere.
///
/// DELIBERATELY NOT BUILT: the interview itself, anything that ever SETS
/// Current, and save/load of it. When the interview lands, give Current a real
/// setter, persist it through StoryDirector (world-scoped is probably wrong —
/// the impression belongs to a player), and reset it in NewGameReset.Apply if
/// it ends up static like this.
/// </summary>
public static class RadioImpression
{
    public enum Kind { None, Star, Fool, Mystery }

    /// What the planet thinks of the player. Nothing sets this yet, so it is
    /// None for every player in every save — which every greeting must treat
    /// as "say no prefix line at all".
    public static Kind Current => Kind.None;
}
