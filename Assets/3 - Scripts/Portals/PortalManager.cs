using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Coordinates level-portal scene transitions between the gameplay world
/// (1.6.7.7.7) and the standalone interior scenes (Backrooms / Poolrooms).
///
/// Persistence model:
///  - Items / money / wood / dust / hotbar layout ride along automatically on the
///    DontDestroyOnLoad singletons (Hotbar, PlayerWallet, WoodInventory, SpaceDustInventory).
///  - Jetpack + equippable unlock/equip state is player-bound (the interior uses its own
///    complete InteriorPlayer instance), so it's captured here and re-applied to that player.
///  - Survival keeps running: the ResourceManager is reparented to root + DontDestroyOnLoad'd
///    on the first hop (VitalsHUD is already persistent).
///
/// Cabin-spawn-on-return — the important part:
///  We do NOT reposition the player after the return load. Doing so fought the engine:
///  on load the planets are repositioned via rigidbodies, but a CelestialBody's TRANSFORM
///  (which the cabin SpawnAnchor is parented under) doesn't update until the next physics
///  step, so reading SpawnAnchor.position right after the apply gave a stale position and
///  flung the player off near another planet.
///  Instead, on the way OUT we move the player onto the cabin SpawnAnchor BEFORE the
///  autosave (while 1.6.7.7.7 is in a stable state and SpawnAnchor is at its true position).
///  The autosave therefore stores the player at the spawn point, and the engine's own
///  (correct) load path restores them there on return — no post-load hacks, and since the
///  player is at the spawn (not on the entrance trigger) there's no bounce-back loop.
/// </summary>
public static class PortalManager
{
    public const string GameplayScene = "1.6.7.7.7";

    static EquipmentSave _carriedEquipment;
    static bool _subscribed;

    /// <summary>Travel into an interior scene, carrying inventory + jetpack.</summary>
    public static void EnterInterior(string targetScene)
    {
        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning("[PortalManager] EnterInterior called with empty targetScene.");
            return;
        }

        _carriedEquipment = SaveCollector.CaptureEquipmentState();

        // Keep survival running across the load: make the ResourceManager persistent.
        if (ResourceManager.Instance != null)
        {
            var rmGo = ResourceManager.Instance.gameObject;
            rmGo.transform.SetParent(null);
            Object.DontDestroyOnLoad(rmGo);
        }

        // From the gameplay world: move the player onto the cabin spawn point, THEN autosave,
        // so the snapshot stores the player at the spawn (not on the entrance trigger). On
        // return the engine restores them there normally — no post-load reposition needed.
        if (SceneManager.GetActiveScene().name == GameplayScene)
        {
            MovePlayerToCabinSpawn();
            if (AutosaveManager.Instance != null) AutosaveManager.Instance.Autosave();
            else Debug.LogWarning("[PortalManager] No AutosaveManager — cannot snapshot world before leaving.");
        }

        EnsureSubscribed();
        SceneManager.LoadScene(targetScene);
    }

    /// <summary>Return to the gameplay world by restoring the autosave (which already has the
    /// player at the cabin spawn point). No position override — the engine's load path is
    /// authoritative.
    ///
    /// Loader choice — the important part: we MUST go back through LoadingScreen
    /// (the async loader the main menu uses), NOT a bare SceneManager.LoadScene.
    /// The atmosphere is a depth-dependent post-process: CustomPostProcessing.OnRenderImage
    /// drives PlanetEffects, which lazily caches one EffectHolder per CelestialBodyGenerator
    /// on its FIRST render and only rebuilds a holder if its generator goes null. A bare
    /// synchronous LoadScene lets the gameplay camera render on the very next frame — mid-load,
    /// before the bodies/save state have settled — so PlanetEffects caches holders that never
    /// recover and the blue atmosphere stays gone for the rest of the session. The main-menu
    /// load works precisely because LoadingScreen async-loads with allowSceneActivation=false +
    /// settle frames behind a black cover, so the first render happens only once everything is
    /// ready. Returning from the backrooms through the same loader makes the two paths
    /// identical. (It also hides the one-frame spawn flash during the deferred save-apply.)</summary>
    public static void ReturnToGameplay()
    {
        var data = SaveSystem.LoadFromDisk(AutosaveManager.AutosaveSlotName);
        if (data != null)
            PendingLoad.ScheduleLoad(data);
        else
            Debug.LogWarning("[PortalManager] No autosave found; loading gameplay scene fresh.");

        EnsureSubscribed();
        // Mirror the main-menu load path. Singletons already exist (they're
        // DontDestroyOnLoad and survived the trip), so no preSceneSetup is
        // needed — PendingLoad still applies via sceneLoaded on activation.
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadSceneAndShow(GameplayScene);
        else
            SceneManager.LoadScene(GameplayScene);
    }

    static void MovePlayerToCabinSpawn()
    {
        var spawn = Object.FindObjectOfType<StartCabinSpawnPoint>(true);
        var player = Object.FindObjectOfType<PlayerController>(true);
        if (spawn == null || player == null) { Debug.LogWarning("[PortalManager] cabin spawn or player not found before autosave."); return; }

        Transform t = spawn.spawnTransform != null ? spawn.spawnTransform : spawn.transform;
        var rb = player.Rigidbody;
        if (rb != null)
        {
            // Keep velocity (already ~matching Humble Abode's orbit since the player was
            // standing in the cabin) so the saved player isn't drifting relative to the planet.
            rb.position = t.position;
            rb.rotation = t.rotation;
        }
        else player.transform.SetPositionAndRotation(t.position, t.rotation);
        Debug.Log("[PortalManager] player moved to cabin spawn '" + spawn.name + "' before autosave @ " + t.position.ToString("F1"));
    }

    static void EnsureSubscribed()
    {
        if (_subscribed) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        _subscribed = true;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Gameplay scene restores via the full-save PendingLoad path; MainMenu never applies.
        if (scene.name == GameplayScene || scene.name == "MainMenu") return;

        // Interior scene: re-apply carried jetpack + equippables to the interior player.
        if (_carriedEquipment != null)
            new GameObject("[PortalApplyRunner]").AddComponent<PortalApplyRunner>().Run(_carriedEquipment);
    }
}

/// <summary>Defers the equipment re-apply after an interior load (after the new player's Start()).</summary>
public class PortalApplyRunner : MonoBehaviour
{
    public void Run(EquipmentSave eq) => StartCoroutine(Co(eq));

    IEnumerator Co(EquipmentSave eq)
    {
        yield return null;                      // let all Start() run
        yield return new WaitForFixedUpdate();  // let the first physics tick settle
        try { SaveCollector.ApplyEquipmentState(eq); }
        catch (System.Exception e) { Debug.LogError("[PortalApplyRunner] apply failed: " + e); }
        Destroy(gameObject);
    }
}
