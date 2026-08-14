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

    /// DROP-IN MULTIPLAYER — the menu MULTIPLAYER button, the "play together?"
    /// prompt after picking a save, the lobby (4-digit code + password over
    /// Unity Relay), and the second player's stasis-pod arrival.
    ///
    /// This is the lever Sam asked for so Phase A can be tested without any
    /// interference from Phase B. Set it FALSE and:
    ///   • the MULTIPLAYER button never appears in the main menu,
    ///   • picking a save goes straight into the game with no prompt,
    ///   • no Unity Services connection is ever attempted,
    ///   • MultiplayerSession never auto-creates.
    /// The game boots and plays exactly as it did before any of this existed.
    ///
    /// The old raw HOST/JOIN/IP overlay that used to sit on the NetworkManager
    /// is gone — this is the only way in now.
    public const bool Multiplayer = true;

    /// The CONCERT VENUE — the stage, both AudienceZones, Max Audience, the
    /// strobe rig, cone beams and the audience spawner.
    ///
    /// Vaulted 2026-08-09 at Sam's request: "the concert is pretty heavy on a
    /// machine", and multiplayer is being built on a deliberately small
    /// baseline. Nothing failed and nothing is deleted.
    ///
    /// ⚠️ The objects are REMOVED FROM THE SCENE rather than merely disabled.
    /// An inactive GameObject still loads its meshes, textures and audio, which
    /// defeats vaulting something for performance. The hierarchy is preserved
    /// as a prefab in Assets/1 - samsPrefabs/_Vaulted/ — see
    /// docs/VAULTED_SYSTEMS.md for how to put it back.
    public const bool ConcertVenue = false;

    /// Tev's ship parked outside his cabin and the ambush it triggers on entry.
    /// Vaulted 2026-08-09: a scripted jumpscare keyed to ONE player walking in
    /// is ill-defined in co-op, and not worth designing around yet.
    public const bool TevCabinAmbush = false;

    /// The SHIP SCHOOL in the village (Combined_SHIPSCHOOL_0/1/2) and its
    /// instructor flow. Vaulted 2026-08-09 while the core co-op loop is built.
    public const bool ShipSchool = false;

    /// SELLING MUSHROOMS to NPCs. Vaulted 2026-08-14 for the cassette pivot,
    /// which is Sam's call from the Phase 6 plan: "aliens do not buy mushrooms
    /// anymore, but just vault it so if we wanna bring it back later its easy."
    ///
    /// This gates the SELL OPTION ONLY, exactly like SpaceDustSelling above.
    /// Finding, chopping, replanting and reharvesting mushrooms all still work,
    /// and so does eating them. The species registry, the grow pots and every
    /// save field are untouched.
    ///
    /// It also frees the SELL PANEL, which now serves tapes: rebuilding that
    /// 1350-line screen for cassettes would have been the expensive way to get
    /// a worse version of something that already works.
    public const bool MushroomSelling = false;

    /// Tev's presence IN THE VILLAGE. Vaulted 2026-08-09.
    ///
    /// ⚠️ Tev HIMSELF is not vaulted — he still lives at his cabin and still
    /// owns rent collection and the mushroom onboarding, both of which are core
    /// loop. This flag covers only his village appearance.
    public const bool VillageTev = false;
}
