/// <summary>
/// Features that are BUILT but deliberately switched off, so the opening stays
/// stripped back to what's known to work while the cave is being finished.
///
/// Nothing here is deleted — flip a flag back to true and the feature returns
/// exactly as it was. Keep it that way: these are things Sam asked to hold, not
/// things that failed.
///
/// Vaulted 2026-08-03 at Sam's request, to be unlocked for playtesting once the
/// caves are done.
/// </summary>
public static class FeatureVault
{
    /// OpeningDirector's six survival beats (locker → water → wood → fire →
    /// build → village). Built and compile-verified, never play-tested.
    public const bool OpeningBeats = false;

    /// The three recruiter questions on the black screen at the very start
    /// ("Do you want your life to mean something?" …). "Open your eyes." is NOT
    /// part of this — that line stays.
    public const bool ColdOpenQuestions = false;

    /// HAL's five spoken lines during the descent (stasis cycle complete, three
    /// years in transit, heart rate elevated, the reassurance, the film lead-in).
    ///
    /// ON. Vaulting these was a misread of "vault the black screen and dialogue
    /// lines at the start" — that meant the cold-open QUESTIONS, not HAL's
    /// briefing on the way down. The briefing stays.
    public const bool DescentBriefing = true;

    /// SELLING space dust to NPCs. Vaulted 2026-08-04 at Sam's request while the
    /// mushroom economy is the focus — "vault space dust for right now, make it
    /// so you can still get it and collect it and have it in your hotbar, but
    /// just make it so npcs dont buy it."
    ///
    /// So this gates the SELL OPTION ONLY. The dust field, the pickup, the
    /// SpaceDustInventory, the hotbar slot and the save/load of all of it are
    /// untouched and still work. SpaceDustSellUI and NPCSellDustOption are still
    /// compiled and still wired — flip this to true and the "Sell space dust"
    /// row comes back on every NPC exactly as it was.
    public const bool SpaceDustSelling = false;

    /// The astronaut HELMET FRAME ART — the painted shell, its visor glass, and
    /// the settings toggle that used to switch them on. Vaulted 2026-08-06:
    /// "i dont really care for the astronaut helmet image overlay, i play with
    /// it turned off but i still like the ui".
    ///
    /// This gates the PICTURE ONLY. Everything Sam likes is untouched and still
    /// runs: the three clusters still render, still seat onto their perspective
    /// quads (that seating never depended on this flag — only the frame canvas
    /// did), still sway, and still do the HudIdleSweep dim-and-wipe.
    ///
    /// Two things deliberately do NOT follow it:
    ///   • Low-O2 condensation, which used to be gated by the helmet toggle as
    ///     well. It's functional feedback with its own setting, so it was
    ///     decoupled rather than vaulted with the art.
    ///   • HelmetHudConfig + its texture must stay assigned in the scene. The
    ///     cluster seating reads the painted quad corners from it, so clearing
    ///     the texture would strand all three clusters, not just hide the shell.
    ///
    /// Flip to true and the helmet comes back exactly as it was, except the
    /// quads have since been widened (clusters sit further out than the painting).
    public const bool HelmetFrameArt = false;
}
