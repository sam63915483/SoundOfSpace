using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Everything that FEEDS PlayerProgress but doesn't belong inside a gameplay
/// script. Two jobs:
///
///   • Build-app placements — one subscription to GhostPlacement.OnPlaced,
///     split into Tree Daddy (saplings) vs Colonizer (everything else).
///     Saplings are placed THROUGH the build system (BuildableEntry.isSapling),
///     so without this split planting a tree would wrongly also score Colonizer.
///
///   • Explorer world detection — a throttled proximity check against
///     NBodySimulation.Bodies. Polling beats a trigger volume here because the
///     bodies are procedurally generated and orbit constantly; adding trigger
///     spheres to them would mean touching the forbidden Celestial/ generation
///     code (CLAUDE.md trap #2).
///
/// Kills are NOT here — EnemyController and AlienNPCDamageable call
/// PlayerProgress directly at their death sites, because only they know whether
/// the thing that died was Elite.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1).
/// </summary>
public class ProgressHooks : MonoBehaviour
{
    public static ProgressHooks Instance { get; private set; }

    // How close (as a multiple of the body's radius) counts as "reached".
    // 1.25 = skimming the surface / low orbit.
    const float DefaultVisitMul = 1.25f;
    // The Black Hole is radius 4000 with a 2500 capture radius, so 1.25x would
    // tick "visited" while you're still deciding. 0.6x (=2400) means you're
    // inside the capture zone and genuinely committed to falling in.
    const float BlackHoleVisitMul = 0.6f;

    const float CheckInterval = 1f;   // seconds between proximity sweeps

    float _nextCheck;
    PlayerController _player;
    int _playerRefindCooldown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("ProgressHooks");
        DontDestroyOnLoad(go);
        go.AddComponent<ProgressHooks>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()  { GhostPlacement.OnPlaced += HandlePlaced; }
    void OnDisable() { GhostPlacement.OnPlaced -= HandlePlaced; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void HandlePlaced(BuildableEntry entry)
    {
        var p = PlayerProgress.Instance;
        if (p == null || entry == null) return;
        // Mushroom spores ride the sapling placement flow but are NOT trees:
        // they make no oxygen, so they must not score the Tree Daddy track
        // (and they aren't a structure either — they score nothing).
        if (entry.isMushroomSapling) return;
        if (entry.isSapling) p.AddSaplingPlanted();
        else                 p.AddStructurePlaced();
    }

    /// A completed mushroom sale — walk-up or scheduled delivery — scores
    /// GANGSTA REP. Called from both resolve paths in MushroomSellUI.
    ///
    /// Rep used to come only from violence (enemy kills up, murdering a friendly
    /// alien down). Dealing is the game now, so the track that gates vendor stock
    /// has to move when you deal. Kills still count; this is an additional source,
    /// not a replacement.
    ///
    /// Scored per SALE, not per cap — otherwise one 20-cap dump would outrank a
    /// dozen relationships, and the track is meant to reward working the route.
    public static void NotifyMushroomSale(int qty)
    {
        if (qty <= 0) return;
        PlayerProgress.Instance?.Add(ProgressTrack.GangstaRep, 1);
    }

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + CheckInterval;

        var progress = PlayerProgress.Instance;
        if (progress == null) return;

        // Whichever body is actually carrying the player right now.
        Vector3 pos;
        var ship = Ship.PilotedInstance;
        if (ship != null && ship.Rigidbody != null) pos = ship.Rigidbody.position;
        else
        {
            // Cached, lazily refound — never FindObjectOfType per frame (CLAUDE.md).
            // The 1 Hz gate already throttles this, but keep the cooldown so a
            // scene with no player doesn't scan every second forever.
            if (_player == null && --_playerRefindCooldown <= 0)
            {
                _player = FindObjectOfType<PlayerController>();
                _playerRefindCooldown = 10;
            }
            if (_player == null || _player.Rigidbody == null) return;
            pos = _player.Rigidbody.position;
        }

        // Null-safe off the solar-system scene — returns Array.Empty there.
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return;

        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null || string.IsNullOrEmpty(b.bodyName)) continue;
            if (progress.HasVisited(b.bodyName)) continue;

            float mul = b.bodyName == "Black Hole" ? BlackHoleVisitMul : DefaultVisitMul;
            float reach = b.radius * mul;
            if ((b.Position - pos).sqrMagnitude <= reach * reach)
                progress.VisitWorld(b.bodyName);
        }
    }
}
