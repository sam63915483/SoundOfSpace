using UnityEngine;

// Tracks whether the player is committed to a "tree episode" — the window
// between first climbing onto a tree and next touching solid ground.
//
// The episode is shared by every EnemyController in the scene so the player
// can't game per-enemy detection by jumping in place or hopping between trees.
// Reset rule: ground contact AND not currently on a tree. Mid-air, in-place
// jumps, and tree-to-tree leaps all preserve the timer.
//
// Centralising the on-tree check (previously duplicated in
// EnemyController.IsPlayerOnATree, run once per enemy per FixedUpdate) also
// means SpawnedTree.AllTrees is iterated once per frame total, not once per
// enemy.
public class PlayerTreeContactTracker : MonoBehaviour
{
    public const float SpitDelaySeconds = 10f;
    const float TrunkRadius = 1.5f;
    const float OnTopVerticalThreshold = 1.5f;

    static PlayerTreeContactTracker _instance;

    PlayerController _player;
    Transform _playerT;
    float _episodeStartTime = -1f;

    public static void EnsureExists()
    {
        if (_instance != null) return;
        var go = new GameObject("PlayerTreeContactTracker");
        _instance = go.AddComponent<PlayerTreeContactTracker>();
        DontDestroyOnLoad(go);
    }

    public static bool IsActive => _instance != null && _instance._episodeStartTime >= 0f;
    public static float SecondsActive =>
        IsActive ? Time.time - _instance._episodeStartTime : 0f;
    public static bool SpitArmed => IsActive && SecondsActive >= SpitDelaySeconds;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    void OnDestroy() { if (_instance == this) _instance = null; }

    void Update()
    {
        // Player may be inactive while piloting; pass true so we still find it
        // and we never falsely "reset" the episode just because the player is
        // briefly inactive.
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        if (_player == null) { _episodeStartTime = -1f; return; }
        if (_playerT == null) _playerT = _player.transform;

        // PlayerController.transform.up is kept aligned to gravity (planet-up)
        // in FixedUpdate, so it's safe to use as the local up for the tree
        // detection projection without re-deriving from referenceBody.
        Vector3 up = _playerT.up;
        bool onTreeNow = OnAnyTree(_playerT.position, up);

        if (onTreeNow && _episodeStartTime < 0f)
            _episodeStartTime = Time.time;                  // first contact starts the timer
        else if (_player.IsOnGround && !onTreeNow)
            _episodeStartTime = -1f;                        // ground + off-tree resets
        // else: preserve (mid-air, jumping in place on tree, tree→tree leaps).
    }

    static bool OnAnyTree(Vector3 playerPos, Vector3 up)
    {
        var trees = SpawnedTree.AllTrees;
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            if (t == null || t.IsDead) continue;
            Vector3 toPlayer = playerPos - t.transform.position;
            Vector3 horiz = Vector3.ProjectOnPlane(toPlayer, up);
            float vertical = Vector3.Dot(toPlayer, up);
            if (horiz.sqrMagnitude < TrunkRadius * TrunkRadius
                && vertical > OnTopVerticalThreshold)
                return true;
        }
        return false;
    }
}
