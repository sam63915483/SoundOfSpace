using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Resets all DontDestroyOnLoad gameplay singletons + static progress state to
// their fresh-game defaults when starting a New Game.
//
// Why this exists: the Load path runs SaveCollector.Apply, which overwrites
// every system from the save. New Game has no such pass — it just seeds the
// singletons (MainMenuController.EnsureGameplaySingletons) and loads the scene.
// Because those singletons are DontDestroyOnLoad (and EarlyGameProgress /
// NoteCollection / BuildMenuLock are static), the previous *unsaved* session's
// hotbar, money, dust, fish dex, vitals and story progress survive the trip
// back through the main menu and leak into the new game. Equippables self-evict
// (Hotbar.DetectAcquisitions clears items whose fresh controller is locked) —
// everything else needs this explicit reset.
//
// Single source of truth: mirror the SaveData schema. Every system the save
// system captures/applies should also be reset here.
//
// The phone AI's conversation memory + volunteered-line transcript (AIMemoryStore,
// HALVolunteeredLog) ARE reset below — otherwise a previous run's chat (e.g.
// "Fishing rod acquired.") bleeds into the new game. Still NOT reset (separate
// subsystem, intentionally-persistent knowledge merge): AIStoryController /
// GameKnowledgeBase story phase.
public static class NewGameReset
{
    static bool _subscribed;

    // Called from MainMenuController's New Game button before the gameplay scene
    // loads. Mirrors PendingLoad.ScheduleLoad's sceneLoaded hook so the reset
    // runs with the same proven timing as a save Apply.
    public static void Schedule()
    {
        if (_subscribed) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        _subscribed = true;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        Unsubscribe();
        new GameObject("[NewGameResetRunner]").AddComponent<NewGameResetRunner>();
    }

    static void Unsubscribe()
    {
        if (!_subscribed) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _subscribed = false;
    }

    // Runs after Start + the first FixedUpdate (the same deferral SaveLoadRunner
    // uses for Apply) so every singleton exists and nothing re-inits over the
    // reset. Each Instance is null-guarded — order doesn't matter here.
    /// <summary>
    /// Clears a JOINING GUEST'S personal state, and nothing else.
    ///
    /// A guest never loads a save - no PendingLoad, so SaveCollector.Apply never
    /// runs - and every inventory here lives on a DontDestroyOnLoad singleton.
    /// So without this, a guest who quit a session carrying a full hotbar walked
    /// back into the NEXT session still carrying it, having never saved. Exactly
    /// the leak CLAUDE.md warns about for New Game, arriving through a door that
    /// did not exist when that warning was written.
    ///
    /// ── Deliberately NOT the full New Game reset ─────────────────────────
    /// Apply() below also clears story progress, HAL, the tutorial and the
    /// galactic clock, and it AUTOSAVES at the end. A guest must inherit the
    /// host's world state, not wipe its own copy of it, and must certainly not
    /// write a save file for somebody else's world. This resets only what
    /// belongs to the player: what they are carrying and how they feel.
    ///
    /// Vitals go to full because arriving is a fresh start, matching the pod
    /// wake the guest sees.
    ///
    /// Since 2026-08-18 this is the FALLBACK rather than the whole story: if the
    /// world already knows this character, SecondPlayerArrival hands their
    /// belongings back immediately afterwards from their PlayerBlockSave. This
    /// still runs first, so a returning player is restored onto a clean slate
    /// rather than on top of the last session's leftovers, and a first-time
    /// visitor keeps the clean slate they always got.
    /// </summary>
    public static void ApplyGuestArrival()
    {
        // A world this character has never played in means a blank board — the
        // mask is world progress now, not a character trophy.
        OrientationObjectives.ResetForNewWorld();
        if (Hotbar.Instance != null) Hotbar.Instance.ResetForNewGame();
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.SetMoney(0);
        if (WoodInventory.Instance != null) WoodInventory.Instance.SetWood(0);
        if (CrystalInventory.Instance != null) CrystalInventory.Instance.SetCount(0);
        if (SpaceDustInventory.Instance != null)
        {
            SpaceDustInventory.Instance.SetCount(0);
            SpaceDustInventory.Instance.SetFilterUnlocked(false);
        }
        if (FishInventory.Instance != null) FishInventory.Instance.ClearInventory();
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ApplyState(100f, 100f, 100f);
            ResourceManager.Instance.SetTotalDeaths(0);
        }

        Debug.Log("[NewGameReset] Guest arrival: personal inventory and vitals cleared.");
    }

    public static void Apply()
    {
        // TutorialGate is pure statics, so its state SURVIVES a return to the
        // main menu. Load a save whose gate was locked, back out, hit New Game,
        // and the fresh run inherits that lock — the player can't interact with
        // anything and there is no in-game way to recover. A new game always
        // starts with everything unlocked (the tutorial re-locks if it runs).
        TutorialGate.UnlockAll();
        // Same class of leak: the ramp's "when did it open" stamp is static.
        ShuttleExitDoor.ResetOpenedStamp();
        // Shuttle-travel rider statics (RiderMode etc.) — a run abandoned
        // mid-flight must not leave the next run's player frozen kinematic.
        // (The shuttle's own pose is scene-authored, so a fresh scene load
        // already parks it on Humble Abode; no pose reset needed.)
        ShuttleRiderFrame.ResetStatics();

        // Same class of leak: buyer bans and remembered counter-offers are pure
        // statics, so a New Game would otherwise start with an alien still
        // refusing to deal because of something the PREVIOUS run did.
        MushroomDealState.ResetAll();
        // Same again for the PERSISTENT buyer state (bond, regulars, message
        // threads) — statics leak across the main menu and New Game runs no
        // Apply, so a fresh run would inherit the old run's regulars.
        BuyerLedger.ResetAll();
        TevFronting.ResetAll();   // a debt must not survive into a New Game
        // Static shelf + installed plugins: New Game runs no Apply, so without
        // this the last world's projects and bought modules leak into the next.
        TraxLibrary.Clear();
        TraxPrints.Clear();       // and the pressings made from them
        CassetteDeck.Clear();     // and whatever was left in the machine
        TapeMemory.Clear();       // and who remembers hearing what
        TevTextDirector.ResetAll();

        if (Hotbar.Instance != null) Hotbar.Instance.ResetForNewGame();
        // The whiteboard is world progress now (2026-08-18) rather than a
        // character trophy, so a new world means a fresh board — even for a
        // character who has crossed every line off before.
        OrientationObjectives.ResetForNewWorld();
        // A world nobody has ever played in remembers nobody's belongings.
        // Without this the last world's blocks would ride into the first save
        // of this one, handing a fresh start somebody else's pockets.
        SaveCollector.ForgetPersonalBlocks();
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.SetMoney(0);
        if (WoodInventory.Instance != null) WoodInventory.Instance.SetWood(0);
        if (CrystalInventory.Instance != null) CrystalInventory.Instance.SetCount(0);
        if (SpaceDustInventory.Instance != null)
        {
            SpaceDustInventory.Instance.SetCount(0);
            SpaceDustInventory.Instance.SetFilterUnlocked(false);
        }
        if (FishInventory.Instance != null) FishInventory.Instance.ClearInventory();
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.ApplyState(100f, 100f, 100f); // full hunger/thirst/health
            ResourceManager.Instance.SetTotalDeaths(0);
        }
        if (OxygenManager.Instance != null) OxygenManager.Instance.ResetForNewGame();
        if (PlanetOxygen.Instance != null) PlanetOxygen.Instance.ResetForNewGame();
        // The clock lives on a DontDestroyOnLoad singleton and New Game runs no
        // Apply, so without this the previous run's date survives the main menu
        // and a "new" game opens on Day 9 with rent already overdue.
        if (GalaxyTime.Instance != null) GalaxyTime.Instance.ResetForNewGame();

        EarlyGameProgress.ResetAll();
        // §3: re-arm the first-message "Press X to open your phone." nag for a
        // fresh game (the static persists across the main-menu round-trip otherwise).
        PlayerPhoneUI.HasEverOpened = false;
        NoteCollection.ApplySaveState(System.Array.Empty<string>());
        // Mission 1 fork (GDD §2): no shelter-building before the village, and the
        // Build branch is stubbed for the slice — so building stays fully locked for
        // the whole slice. LockAllExcept() with no args = nothing is buildable. The
        // Build branch will UnlockAll() when it's built out later.
        BuildMenuLock.LockAllExcept();

        // Progression lives in a DontDestroyOnLoad singleton, so without this a
        // New Game inherits the previous run's levels and visited worlds (New
        // Game runs no Apply — CLAUDE.md).
        if (PlayerProgress.Instance != null) PlayerProgress.Instance.ResetAll();

        // The jetpack is standard kit now (Sam, 2026-08-03) — you start with it
        // rather than buying it from Alien7. Done here as well as on the
        // prefab's default because the PLAYER IN THE SCENE carries its own
        // serialised copy of that flag, and a changed C# default never reaches
        // an object that already exists.
        var playerForJetpack = Object.FindObjectOfType<PlayerController>();
        if (playerForJetpack != null) playerForJetpack.UnlockJetpack();

        if (StoryDirector.Instance != null) StoryDirector.Instance.ResetForNewGame();

        // Phone AI: wipe conversation memory + the volunteered-line transcript so a
        // previous in-process run's chat history doesn't replay in the new game's AI app.
        if (AIMemoryStore.Instance != null) AIMemoryStore.Instance.Restore(null);
        if (HALVolunteeredLog.Instance != null) HALVolunteeredLog.Instance.Clear();
        // Forget visited-body / streak-milestone dedupe so HAL's first-visit
        // lines fire again in the new run.
        if (HALCommentator.Instance != null) HALCommentator.Instance.ResetForNewGame();
        // Names + first-contact flag are static (NameStore, mirrors the
        // EarlyGameProgress pattern) — without this a new game reuses the
        // previous run's player/AI names and skips the first-contact naming UX.
        NameStore.PlayerName = "";
        NameStore.AIName = "";
        NameStore.FirstContactComplete = false;

        if (CompassHUD.Instance != null) CompassHUD.Instance.ClearAll();
        // idx = -1 → NotStarted, so the map tutorial fires again on first open.
        if (MapTutorial.Instance != null) MapTutorial.Instance.ApplySaveState(false, -1, null);
        // null key → Idle (no bonus tutorial running).
        if (BonusTutorial.Instance != null) BonusTutorial.Instance.ApplySaveState(null, 0, null, false);

        // Each new game claims the next free "stasis pod N" slot so runs never
        // overwrite each other's pod saves (death respawn targets this slot too).
        StasisPodSave.ActiveSlotName = StasisPodSave.NextFreeSlotName();

        // Death reloads the newest save. New Game doesn't touch disk, so a stale
        // file from a previous run could be the newest one — dying early in a
        // fresh game would then reload the OLD run. Force a snapshot of this
        // fresh start so the new run owns the newest save (also covers
        // first-ever launch where no save exists yet). DeathCutsceneController
        // relies on this.
        //
        // Written to THIS RUN'S POD SLOT, claimed a few lines above, rather than
        // to the old shared autosave slot: the pod is the only save point now,
        // and death respawn targets ActiveSlotName first. Sending the seed save
        // anywhere else would leave the pod slot empty until the first upload,
        // which is exactly the window this exists to cover.
        SaveSystem.Save(StasisPodSave.ActiveSlotName);
    }
}

// Throwaway runner that defers the reset one frame + one FixedUpdate so all
// Start() and the first physics tick complete first — identical timing to
// SaveLoadRunner so the reset can't be clobbered by scene/singleton init.
public class NewGameResetRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        try { NewGameReset.Apply(); }
        catch (System.Exception e) { Debug.LogError($"[NewGameReset] Apply failed: {e}"); }
        Destroy(gameObject);
    }
}
