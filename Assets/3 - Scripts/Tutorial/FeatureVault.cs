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
///
/// ⚠️ These are `static readonly`, NOT `const`, and must stay that way.
/// With `const` the compiler folds every `if (!FeatureVault.X) return;` at
/// compile time and reports the whole body as CS0162 "unreachable code" — that
/// alone was 40 of the project's 177 build warnings, drowning out real ones.
/// `static readonly` reads identically at every call site, is folded by the JIT
/// so it costs nothing at runtime, and flipping a flag still works exactly the
/// same. Do not "optimise" these back to `const`.
/// </summary>
public static class FeatureVault
{
    /// OpeningDirector's six survival beats (locker → water → wood → fire →
    /// build → village). Built and compile-verified, never play-tested.
    public static readonly bool OpeningBeats = false;

    /// The three recruiter questions on the black screen at the very start
    /// ("Do you want your life to mean something?" …). "Open your eyes." is NOT
    /// part of this — that line stays.
    public static readonly bool ColdOpenQuestions = false;

    /// HAL's five spoken lines during the descent (stasis cycle complete, three
    /// years in transit, heart rate elevated, the reassurance, the film lead-in).
    ///
    /// ON. Vaulting these was a misread of "vault the black screen and dialogue
    /// lines at the start" — that meant the cold-open QUESTIONS, not HAL's
    /// briefing on the way down. The briefing stays.
    public static readonly bool DescentBriefing = true;

    /// The tape-career SHOP GATE: Half/Full-Length blanks locked until 10/25
    /// total tapes sold, and fan orders clamped to the unlocked formats.
    /// Vaulted OFF 2026-08-18 so Sam can playtest all six blanks and song
    /// orders from a fresh save without grinding the milestones first.
    ///
    /// This gates the LOCK ONLY. TapeCareer.TapesSold still counts every sale
    /// (so flipping this back on later lands mid-career, not at zero), the
    /// locked-row UI, the "SELL N MORE" copy and Tev's restock text all come
    /// back exactly as built. Flip to true once the loop is verified.
    public static readonly bool TapeCareerGate = false;

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
    public static readonly bool SpaceDustSelling = false;

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
    public static readonly bool HelmetFrameArt = false;

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
    public static readonly bool Multiplayer = true;

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
    public static readonly bool ConcertVenue = false;

    /// Tev's ship parked outside his cabin and the ambush it triggers on entry.
    /// Vaulted 2026-08-09: a scripted jumpscare keyed to ONE player walking in
    /// is ill-defined in co-op, and not worth designing around yet.
    public static readonly bool TevCabinAmbush = false;

    /// The SHIP SCHOOL in the village (Combined_SHIPSCHOOL_0/1/2) and its
    /// instructor flow. Vaulted 2026-08-09 while the core co-op loop is built.
    public static readonly bool ShipSchool = false;

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
    public static readonly bool MushroomSelling = false;

    /// THE FREEFORM BUILDING SYSTEM — the build menu, its catalogue of
    /// structures, and the phone's Build app. Vaulted 2026-08-14, the last item
    /// of the cassette-loop plan (docs/Plan_CassetteLoop_Build_v1.md Phase 6).
    ///
    /// ⚠️ THIS GATES THE MENU AND ITS CATALOGUE, NOT THE PLACEMENT MACHINERY.
    /// That distinction is the whole reason this is safe: planting a sapling or
    /// a mushroom does NOT open the build menu. Both planters call
    /// BuildMenuUI.StartPlacementFromPhone directly off the hotbar selection and
    /// drive GhostPlacement themselves. Gate the ghost and you silently kill
    /// replanting, which the plan explicitly keeps.
    ///
    /// So with this false: the menu never opens, the Build app tile is not built,
    /// and the Grow Pot / Bubble Dome registrars never inject their entries.
    /// BuildMenuUI itself still exists and still holds `buildables`, because the
    /// planters resolve their prefabs out of that list.
    ///
    /// Kept working, per the plan: tree chopping, saplings, fishing, bonfire
    /// cooking, and mushroom planting.
    public static readonly bool FreeformBuilding = false;

    /// THE LEVEL SYSTEM — the general level, Colonizer, Tree Killer, Tree Daddy,
    /// Gangsta Rep, their phone page, the level-up toast, the grand ceremony and
    /// every level-gated unlock. Vaulted 2026-08-14 with the building system,
    /// same plan item: "nothing in the new loop may reference levels."
    ///
    /// Gated at ONE choke point — PlayerProgress.Add — rather than at the ~50
    /// call sites that award score. Every AddTreeFelled / AddEnemyKill /
    /// AddStructurePlaced still compiles and still runs; it just scores nothing,
    /// so no track ever moves, no toast fires and no ceremony queues. The save
    /// fields are untouched, so a vaulted run and an unvaulted one round-trip
    /// through the same schema.
    ///
    /// The phone drops from two pages to one. PageCount is computed from this
    /// flag, so the dots, the wrap and the arrows all follow automatically.
    public static readonly bool LevelSystem = false;

    /// TEV'S FRONTING ECONOMY — the repeatable 50/50 front, the skim quote, the
    /// per-player debt ledger, and the three demo tapes (SLUDJ / CHIRP / DRIFT)
    /// he used to hand over. Vaulted 2026-08-14 per the rent revamp handoff.
    ///
    /// Why: the customers were HIS. BuyerLedger bond, threads and "songs heard"
    /// all accrue from what a contact bought, so under fronting every early
    /// buyer's taste was shaped by Tev's demos and the player's own career was
    /// being steered toward someone else's sound. 50/50 was mushroom logic —
    /// caps are fungible, songs are not.
    ///
    /// Gates the DIALOGUE PATH only. TevFronting.cs, TevDemoTapes.cs and every
    /// save field they use still compile and still round-trip; flip this true
    /// and RunFrontingTalk is reachable again exactly as it was.
    public static readonly bool TevFrontingEconomy = false;

    /// THE LAWN WORK-OFF HAGGLE — "sell 10 / 8 / 5 / 3 of my tapes and we're
    /// square", the one-off debt it created, and MushroomQuest.SettleLawn.
    /// Vaulted 2026-08-14 with the fronting economy it depended on: the tapes
    /// being worked off were Tev's demos.
    ///
    /// Replaced by the DAILY MONEY RENT haggle ($50 → $30 → $20 → $10), which
    /// is the reactivated Aug 8 system rather than anything new. The lawn
    /// counters (tevLawnTapesOwed / tevLawnCleared) are left in the schema so a
    /// save written under either rule still loads.
    public static readonly bool TevLawnWorkOff = false;

    /// Tev's presence IN THE VILLAGE. Vaulted 2026-08-09.
    ///
    /// ⚠️ Tev HIMSELF is not vaulted — he still lives at his cabin and still
    /// owns rent collection and the mushroom onboarding, both of which are core
    /// loop. This flag covers only his village appearance.
    public static readonly bool VillageTev = false;

    /// TEV'S RENT — the daily lawn rent: the first-talk haggle ($50 → $30 →
    /// $20 → $10), TevRentCollector's daily accrual, arrears, the rent nag,
    /// TevPaymentUI's rent entry, the day-recap rent line and the 5-day
    /// PLUGINS-tab lockout. Vaulted 2026-08-30 per the first-meeting revamp
    /// (docs/Handoff_TevDialogue_FirstMeeting_v1 (1).md): Tev is no longer a
    /// landlord — he's a music-store owner who sells TRAX for $20.
    ///
    /// Gates BEHAVIOR at the same choke points the fronting vault used:
    /// PlaySequence routing, MushroomQuest.PluginsLocked (hard false while
    /// vaulted, which silences every TevShopUI lockout site at once) and
    /// TevRentCollector's accrual. The rent counters stay in the schema so a
    /// save written under either rule still loads.
    public static readonly bool TevRent = false;

    /// CRAVING — the demand flywheel (loop-feel pass C, 2026-08-17, Sam GO'd
    /// the whole handoff). Per-buyer 0..100 hunger: feeds on good sales,
    /// decays when ignored, drives want-text frequency, the guaranteed daily
    /// order at 90+, the ambush walk-up at 60+, and the contact-card ladder
    /// word (curious / interested / hooked / obsessed).
    ///
    /// Gates BEHAVIOR only, never data: the craving field still saves and
    /// loads with this off (it just never changes), so flipping the flag
    /// either way is safe mid-save. It must NEVER touch price or block a
    /// sale — demand, not a gate.
    public static readonly bool CravingSystem = true;
}
