using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dying in co-op: wake up in the stasis pod, and the door lets you out.
///
/// ── Why the normal death path cannot run here ────────────────────────────
/// DeathCutsceneController plays a cutscene and then RELOADS THE SAVE. A scene
/// reload tears down the NetworkManager and drops the session — one player
/// dying would end the game for both. So that controller bails out while a
/// session is live (see its HandleDeath guard) and this takes over.
///
/// ResourceManager's own legacy respawn is suppressed for the same reason it
/// always is: it teleports you to the piloted ship or leaves you where you fell,
/// neither of which is "respawn in the pod".
///
/// ── What you get instead ─────────────────────────────────────────────────
/// The real ritual a joining guest already wakes into: seated in the pod, the
/// DOWNLOADING overlay, then the door opens. It then closes behind you on its
/// own — the door's steady-state rule schedules that once you are up and out.
/// No cutscene, no reload, and the session never notices.
///
/// Auto-singleton. It does NOT skip MainMenu (it early-outs there instead of
/// never being created), so it never needs seeding in EnsureGameplaySingletons
/// — the same reasoning MultiplayerSession and CharacterStore use for dodging
/// CLAUDE.md trap #1.
/// </summary>
public class MultiplayerDeathRespawn : MonoBehaviour
{
    public static MultiplayerDeathRespawn Instance { get; private set; }

    /// Health handed back on respawn. Matches ResourceManager's own legacy
    /// respawn so dying feels the same in both modes.
    const float RespawnHealth = 25f;
    const float RespawnHunger = 10f;
    const float RespawnThirst = 10f;

    /// Beat between dying and waking, so death registers as an event rather
    /// than a teleport.
    const float DeathHoldSeconds = 1.6f;

    /// True from the moment we take over a death until the player is out of the
    /// pod. Read by the suppression hook below.
    bool _handling;

    ResourceManager _subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        var go = new GameObject("MultiplayerDeathRespawn");
        DontDestroyOnLoad(go);
        go.AddComponent<MultiplayerDeathRespawn>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unsubscribe();
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _handling = false;
        // DeathCutsceneController nulls LegacyRespawnSuppressed in its OnDestroy,
        // so a scene change can wipe our chained hook - re-install on next death.
        _hookInstalled = false;
        Unsubscribe();
    }

    /// ResourceManager is SCENE-PLACED and replaced on every gameplay load, so
    /// the subscription is re-established by polling rather than once at boot —
    /// the same trap DeathCutsceneController documents.
    void Update()
    {
        var rm = ResourceManager.Instance;
        if (rm == _subscribed) return;

        Unsubscribe();
        if (rm == null) return;

        rm.OnDeath += HandleDeath;
        _subscribed = rm;
    }

    void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.OnDeath -= HandleDeath;
        _subscribed = null;
    }

    static bool SessionLive
    {
        get
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && nm.IsListening;
        }
    }

    void HandleDeath()
    {
        if (_handling) return;
        if (!SessionLive) return;   // single player keeps its cutscene
        _handling = true;
        StartCoroutine(RespawnInPod());
    }

    bool _hookInstalled;

    /// Takes ownership of the respawn away from ResourceManager.DeathSequence.
    ///
    /// Chained, not assigned, so DeathCutsceneController's own hook survives —
    /// clobbering it would leave single-player deaths double-handled.
    ///
    /// Installed LAZILY, on the first death, rather than at boot. Two reasons:
    /// DeathCutsceneController assigns its hook unconditionally in Awake and
    /// the order between two auto-singletons is not guaranteed, so installing
    /// early risks being overwritten; and chaining once instead of per-death
    /// stops the closure chain growing by one link every time somebody dies.
    void InstallSuppressionHook()
    {
        if (_hookInstalled) return;
        _hookInstalled = true;
        var previous = ResourceManager.LegacyRespawnSuppressed;
        ResourceManager.LegacyRespawnSuppressed =
            () => _handling || (previous != null && previous());
    }

    IEnumerator RespawnInPod()
    {
        InstallSuppressionHook();

        // Freeze input the same way every other scripted beat does.
        PlayerController.isInDialogue = true;

        yield return new WaitForSecondsRealtime(DeathHoldSeconds);

        // Back into the pod. Body-relative, because the planet has moved.
        SecondPlayerArrival.SeatInPod();

        // Vitals BEFORE the wake, so the HUD is already correct as it fades up.
        var rm = ResourceManager.Instance;
        if (rm != null) rm.ApplyState(RespawnHunger, RespawnThirst, RespawnHealth);

        // The genuine article — the same DOWNLOADING wake a joining guest gets,
        // not a lookalike.
        var pod = Object.FindObjectOfType<StasisPodSave>();
        if (pod != null) pod.PlayDownloadWake();

        // Let the wake play before asking for the door, so it opens onto a
        // conscious player rather than swinging while the overlay is still up.
        yield return new WaitForSecondsRealtime(2f);

        // Host-authoritative: on a client this asks the host, which is the only
        // machine allowed to move the door.
        StasisDoorSync.RequestOpen();

        PlayerController.isInDialogue = false;
        _handling = false;
        // The door closes itself: StasisPodDoor's steady-state rule schedules a
        // close for any open door with nobody in the doorway, and gives Deep
        // (you, still in the pod) the longer 5s grace to walk out.
    }
}
