using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Being drunk (Sam, 2026-09-12). Every beer finished adds ONE to
/// <see cref="Beers"/> (capped at 10); it drains at one beer per minute, so
/// ten back to back is ten minutes of trouble tapering off. Everything below
/// is a smooth function of the live level, so it ramps, never steps:
///
///   level  | woozy blur | colour shift | sway | burps      | self-turning
///   1-2    | light      | a little     | mild | every ~25s | none
///   5      | strong     | clear        | big  | every ~14s | starts (tiny)
///   10     | full       | peak         | max  | every ~4s  | worst
///
/// The pieces, all borrowed from effects the game already has:
///   • blur + double vision — <see cref="GrogginessImageEffect"/> (the pod
///     wake-up) on the player camera, intensity from the level.
///   • colour / wavy shimmer — the mushroom trip post
///     (<see cref="RawFishTripController.Sustain"/>), held at a steady level.
///   • sway — <see cref="BeerCameraWobble"/>: Perlin roll/pitch/yaw over the
///     final camera pose.
///   • self-turning — degrees fed into <c>PlayerController.SwingCameraKick</c>,
///     the same per-frame look nudge the axe swing uses, so the view really
///     turns (and the player can fight it) instead of just visually drifting.
///   • burps — <see cref="PlayerSuitAudio.PlayBurpAfterDelay"/> on a timer.
///
/// Not saved (a buzz is minutes; not worth the apply-order risk); cleared in
/// NewGameReset. Auto-singleton, ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (CLAUDE.md trap #1).
/// </summary>
public class BeerBuzz : MonoBehaviour
{
    public static BeerBuzz Instance { get; private set; }

    public const float MaxBeers = 10f;
    public const float SecondsPerBeer = 60f;

    /// Live level, 0-10, fractional as it drains.
    public float Beers { get; private set; }
    public bool IsActive => Beers > 0.001f;
    public event Action OnChanged;

    [Header("Woozy blur (GrogginessImageEffect intensity at 10 beers)")]
    public float blurAtMax = 0.8f;
    [Header("Colour / shimmer (mushroom trip dials at 10 beers)")]
    public float colourAtMax = 0.7f;
    public float waveAtMax = 0.6f;
    [Tooltip("Kaleidoscope geometry only past this many beers (0 = never).")]
    public float kaleidoFrom = 7f;
    public float kaleidoAtMax = 0.16f;
    [Header("Sway")]
    public float swayRollAtMax = 5f;
    public float swayPitchAtMax = 2.5f;
    public float swayYawAtMax = 2.5f;
    [Header("Self-turning (degrees per second at 10 beers; starts at driftFrom beers)")]
    public float driftFrom = 4f;
    public float driftDegPerSecAtMax = 8f;
    [Header("Burps (seconds between, at 1 beer and at 10)")]
    public float burpEveryAtOne = 28f;
    public float burpEveryAtMax = 4f;

    // runtime
    Camera _cam;
    GrogginessImageEffect _grog;
    BeerCameraWobble _wobble;
    PlayerController _player;
    float _nextCamSearch, _nextPlayerSearch;
    float _nextBurp;
    float _driftT;
    bool _blurWritten;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[BeerBuzz]");
        DontDestroyOnLoad(go);
        go.AddComponent<BeerBuzz>();
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
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // Camera, its effect components and the player died with the old scene.
        _cam = null; _grog = null; _wobble = null; _player = null;
        _nextCamSearch = 0f; _nextPlayerSearch = 0f;
        _blurWritten = false;
    }

    // ── level ─────────────────────────────────────────────────────

    /// <summary>One more beer finished.</summary>
    public void AddBeer()
    {
        Beers = Mathf.Min(MaxBeers, Beers + 1f);
        if (_nextBurp <= 0f) _nextBurp = Time.time + 2f;
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        bool had = IsActive;
        Beers = 0f;
        _nextBurp = 0f;
        Apply(0f);
        if (had) OnChanged?.Invoke();
    }

    // ── per frame ─────────────────────────────────────────────────

    void Update()
    {
        if (Beers > 0f)
        {
            Beers = Mathf.Max(0f, Beers - Time.deltaTime / SecondsPerBeer);
            if (Beers <= 0f) { _nextBurp = 0f; OnChanged?.Invoke(); }
        }
        Apply(Beers);
    }

    void Apply(float level)
    {
        float t = Mathf.Clamp01(level / MaxBeers);
        bool on = level > 0.001f;

        // ── blur + double vision
        var cam = Cam();
        if (cam != null)
        {
            if (on)
            {
                if (_grog == null)
                {
                    _grog = cam.GetComponent<GrogginessImageEffect>();
                    if (_grog == null) _grog = cam.gameObject.AddComponent<GrogginessImageEffect>();
                }
                if (_grog.material == null) _grog.material = FindGrogMaterial();
                _grog.intensity = blurAtMax * t;
                _blurWritten = true;
            }
            else if (_blurWritten && _grog != null)
            {
                _grog.intensity = 0f;   // hand the component back (the black hole / intro may use it)
                _blurWritten = false;
            }

            // ── sway
            if (on || _wobble != null)
            {
                if (_wobble == null) _wobble = cam.GetComponent<BeerCameraWobble>() ?? cam.gameObject.AddComponent<BeerCameraWobble>();
                _wobble.Amount = t;
                _wobble.MaxRollDeg = swayRollAtMax;
                _wobble.MaxPitchDeg = swayPitchAtMax;
                _wobble.MaxYawDeg = swayYawAtMax;
            }
        }

        // ── colour + shimmer (max with any running mushroom trip)
        float kaleido = kaleidoFrom > 0f && level > kaleidoFrom
            ? kaleidoAtMax * Mathf.InverseLerp(kaleidoFrom, MaxBeers, level) : 0f;
        RawFishTripController.Sustain(colourAtMax * t, waveAtMax * t, kaleido);

        if (!on) return;

        // ── self-turning: the view wanders on its own past driftFrom beers
        if (level > driftFrom)
        {
            var pc = Player();
            if (pc != null && pc.isActiveAndEnabled)
            {
                float k = Mathf.InverseLerp(driftFrom, MaxBeers, level);
                _driftT += Time.deltaTime * 0.25f;
                float yaw   = (Mathf.PerlinNoise(_driftT, 11.1f) - 0.5f) * 2f;
                float pitch = (Mathf.PerlinNoise(23.7f, _driftT * 0.9f) - 0.5f) * 2f;
                float dps = driftDegPerSecAtMax * k;
                PlayerController.SwingCameraKick += new Vector2(yaw * dps, pitch * dps * 0.5f) * Time.deltaTime;
            }
        }

        // ── burps
        if (_nextBurp <= 0f) _nextBurp = Time.time + BurpInterval(level);
        if (Time.time >= _nextBurp)
        {
            PlayerSuitAudio.Instance?.PlayBurpAfterDelay();
            _nextBurp = Time.time + BurpInterval(level);
        }
    }

    float BurpInterval(float level)
    {
        float t = Mathf.InverseLerp(1f, MaxBeers, Mathf.Max(1f, level));
        float every = Mathf.Lerp(burpEveryAtOne, burpEveryAtMax, t);
        return every * UnityEngine.Random.Range(0.7f, 1.3f);
    }

    // ── lookups (throttled; never in a hot loop) ──────────────────

    Camera Cam()
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        if (Time.unscaledTime < _nextCamSearch) return null;
        _nextCamSearch = Time.unscaledTime + 1f;
        var c = Camera.main;
        if (c != _cam) { _cam = c; _grog = null; _wobble = null; }
        return _cam;
    }

    PlayerController Player()
    {
        if (_player != null) return _player;
        if (Time.unscaledTime < _nextPlayerSearch) return null;
        _nextPlayerSearch = Time.unscaledTime + 1f;
        _player = FindObjectOfType<PlayerController>();
        return _player;
    }

    static Material FindGrogMaterial()
    {
        var intro = FindObjectOfType<IntroSequenceController>(true);
        if (intro != null && intro.GrogginessMaterial != null) return intro.GrogginessMaterial;
        Debug.LogWarning("[BeerBuzz] no Grogginess material found (IntroSequenceController) — no blur.");
        return null;
    }
}
