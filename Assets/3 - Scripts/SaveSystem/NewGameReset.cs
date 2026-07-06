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
    public static void Apply()
    {
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
            ResourceManager.Instance.ApplyState(100f, 100f, 100f); // full hunger/thirst/health
            ResourceManager.Instance.SetTotalDeaths(0);
        }
        if (OxygenManager.Instance != null) OxygenManager.Instance.ResetForNewGame();

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

        // Death reloads the newest save. New Game doesn't touch disk, so a stale
        // autosave from a previous run could be the newest file — dying early in a
        // fresh game would then reload the OLD run. Force a snapshot of this fresh
        // start so the new run owns the newest save (also covers first-ever launch
        // where no save exists yet). DeathCutsceneController relies on this.
        if (AutosaveManager.Instance != null) AutosaveManager.Instance.Autosave();
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
