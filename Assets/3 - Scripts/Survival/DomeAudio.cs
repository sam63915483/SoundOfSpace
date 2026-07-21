using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays a force-field "membrane" whoosh whenever the player crosses a Bubble
/// Dome boundary — entering OR exiting — so the transition is audible and the
/// dome reads as a real barrier. One 2D AudioSource (this is a "you crossed it"
/// stinger, not a positional sound). The clip is loaded by Resources name
/// (DomeFX/dome_enter) so there's no inspector wiring; silent if it's absent.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md).
/// </summary>
public class DomeAudio : MonoBehaviour
{
    public static DomeAudio Instance { get; private set; }

    Transform _player;
    BubbleDome _lastDome;
    bool _initialised;
    float _refindTimer;
    AudioSource _src;
    AudioClip _clip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("DomeAudio");
        DontDestroyOnLoad(go);
        go.AddComponent<DomeAudio>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _clip = Resources.Load<AudioClip>("DomeFX/dome_enter");
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;   // 2D — a boundary-cross stinger, not positional
        _src.volume = crossVolume;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // This singleton is DontDestroyOnLoad but its state points at scene objects:
    // after a reload _lastDome is a destroyed fake-null and _initialised is stale,
    // which fired a phantom "crossed a dome" whoosh on the first frame of the new
    // scene. Reset per scene; the player ref is refound (throttled) below.
    void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _player = null;
        _lastDome = null;
        _initialised = false;
    }

    void Update()
    {
        if (_player == null)
        {
            // Throttled refind (LightLookAt pattern) — no per-frame scene scans
            // while the player doesn't exist yet (loading, cutscenes).
            _refindTimer -= Time.deltaTime;
            if (_refindTimer > 0f) return;
            _refindTimer = 0.5f;
            var pc = FindObjectOfType<PlayerController>();
            if (pc == null) return;
            _player = pc.transform;
        }

        var dome = BubbleDome.DomeContaining(_player.position);

        // Skip the first evaluation so we don't fire on load if the player happens
        // to spawn already inside a dome.
        if (!_initialised) { _lastDome = dome; _initialised = true; return; }

        if (dome != _lastDome)
        {
            // Entered (null→dome), exited (dome→null), or stepped straight from one
            // dome into another — all get the same membrane whoosh.
            if (_clip != null) _src.PlayOneShot(_clip, crossVolume);
            _lastDome = dome;
        }
    }

    [SerializeField] float crossVolume = 0.7f;
}
