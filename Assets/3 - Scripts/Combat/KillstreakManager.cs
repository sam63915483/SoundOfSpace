using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks the player's consecutive enemy-kill streak. Each kill increments
/// the count and resets a decay timer; when the timer expires without a new
/// kill, the streak resets to 0 and OnStreakBroken fires.
///
/// Tier windows shrink as the streak climbs — 10 s after the first kill,
/// 9 s at x2 (DOUBLE), 8 s at x3 (TRIPLE), … 1 s at x10, and a 1 s cap from
/// x11 onward (KillstreakHUD reuses the same WICKED SICK visual past the cap).
///
/// Public surface used by KillstreakHUD and SlowmoOnKill:
///   - CurrentStreak (0 idle, 1 after first kill, 2 at DOUBLE, …)
///   - DecayProgress01 (1.0 just-killed → 0.0 about-to-break)
///   - OnKillRegistered(int newStreak) — fires AFTER the increment
///   - OnStreakBroken()
///
/// Auto-creates like the other procedural singletons, skipped in MainMenu.
/// Resets on player death (ResourceManager.OnDeath) and scene reload.
/// </summary>
public class KillstreakManager : MonoBehaviour
{
    public static KillstreakManager Instance { get; private set; }

    public int CurrentStreak { get; private set; }
    public float DecayProgress01 =>
        _currentWindow > 0f ? Mathf.Clamp01(_decayTimer / _currentWindow) : 0f;

    public event System.Action<int> OnKillRegistered;
    public event System.Action OnStreakBroken;

    // Indexed by streak count (clamped to last entry past cap).
    //   idx 0: never read (we start at streak=1 after the first kill).
    //   idx 1: 10 s window from kill 1 → kill 2 (pre-popup).
    //   idx 2..10: 9, 8, 7, 6, 5, 4, 3, 2, 1 — windows at DOUBLE … LEGENDARY.
    //   idx 11+: 1 s — WICKED SICK cap.
    static readonly float[] s_windowByStreak =
    {
        /* 0  */ 0f,
        /* 1  */ 10f,
        /* 2  */ 9f,
        /* 3  */ 8f,
        /* 4  */ 7f,
        /* 5  */ 6f,
        /* 6  */ 5f,
        /* 7  */ 4f,
        /* 8  */ 3f,
        /* 9  */ 2f,
        /* 10 */ 1f,
        /* 11+ */ 1f,
    };

    float _decayTimer;
    float _currentWindow;
    ResourceManager _resources;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("KillstreakManager");
        DontDestroyOnLoad(go);
        go.AddComponent<KillstreakManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        EnemyController.OnAnyEnemyDeath += HandleKill;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        HookResourceManager();
    }

    void OnDisable()
    {
        EnemyController.OnAnyEnemyDeath -= HandleKill;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (_resources != null) _resources.OnDeath -= HandlePlayerDeath;
    }

    void Update()
    {
        // ResourceManager auto-creates after us in some load orders — hook lazily.
        if (_resources == null) HookResourceManager();

        if (CurrentStreak <= 0) return;
        _decayTimer -= Time.unscaledDeltaTime;
        if (_decayTimer <= 0f) BreakStreak();
    }

    void HandleKill()
    {
        CurrentStreak++;
        _currentWindow = WindowForStreak(CurrentStreak);
        _decayTimer = _currentWindow;
        OnKillRegistered?.Invoke(CurrentStreak);
    }

    void BreakStreak()
    {
        if (CurrentStreak <= 0) return;
        CurrentStreak = 0;
        _decayTimer = 0f;
        _currentWindow = 0f;
        OnStreakBroken?.Invoke();
    }

    void HandlePlayerDeath() => BreakStreak();

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BreakStreak();
        HookResourceManager();
    }

    void HookResourceManager()
    {
        if (_resources != null) _resources.OnDeath -= HandlePlayerDeath;
        _resources = ResourceManager.Instance;
        if (_resources != null) _resources.OnDeath += HandlePlayerDeath;
    }

    static float WindowForStreak(int streak)
    {
        int idx = Mathf.Clamp(streak, 1, s_windowByStreak.Length - 1);
        return s_windowByStreak[idx];
    }
}
