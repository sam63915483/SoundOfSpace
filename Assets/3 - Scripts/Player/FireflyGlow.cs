using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The firefly status effect (Sam, 2026-09-11): eat a firefly and "the
/// astronaut's entire body glows for 1 minute". Two halves:
///
///   • <b>The suit itself glows.</b> An HDR firefly-orange emission written
///     into every suit slot's property block (<see cref="SuitTinter.SetEmission"/>
///     — the same per-renderer, per-slot mechanism the suit colour uses, so the
///     shared Suit.mat asset is never touched at runtime and the tint survives).
///     It breathes gently, fades in over half a second and out over the last
///     three, so the end of the minute is never a pop.
///   • <b>You become a lantern.</b> A point light rides on the player (grass
///     marker at torch parity) so the ground and blades around you light up.
///     Its culling mask leaves the suit's own layer out — the suit is already
///     emissive, and a light at your own centre lighting your own arms from
///     inside reads wrong.
///
/// ONE slot, like the cat perk: eating another while glowing sets the timer
/// back to a full minute. Never stacks, never adds. Not saved (a minute is not
/// worth the apply-order risk), cleared in NewGameReset so a DontDestroyOnLoad
/// singleton cannot carry it into a new game.
///
/// Auto-singleton; ALSO seeded in MainMenuController.EnsureGameplaySingletons
/// (CLAUDE.md trap #1 — the MainMenu early-return below means a build would
/// otherwise never create it).
/// </summary>
public class FireflyGlow : MonoBehaviour
{
    public static FireflyGlow Instance { get; private set; }

    public const float DurationSeconds = 60f;

    public float SecondsLeft { get; private set; }
    public float TotalSeconds { get; private set; }
    public bool IsActive => SecondsLeft > 0f;

    /// Fires when the glow starts, is refreshed, or runs out. The HUD chip listens.
    public event Action OnChanged;

    [Header("Suit emission")]
    [Tooltip("Emission colour. HDR: the intensity below multiplies it, and the Bloom effect picks up anything over white.")]
    public Color glowColor = new Color(1f, 0.68f, 0.22f, 1f);
    public float glowIntensity = 2.2f;
    [Range(0f, 1f)]
    [Tooltip("Bottom of the breathing cycle as a fraction of full. 1 = a steady glow.")]
    public float breatheMin = 0.72f;
    [Tooltip("Breaths per second.")]
    public float breatheHz = 0.45f;
    [Tooltip("Seconds for the glow to come up after eating.")]
    public float fadeInSeconds = 0.6f;
    [Tooltip("Seconds over which the glow dies at the end of the minute.")]
    public float fadeOutSeconds = 3f;

    [Header("Light on the player")]
    public float lightIntensity = 1.4f;
    public float lightRange = 9f;
    [Tooltip("Where the light sits in the player's own frame. The camera is at (0, 0.67, 0.28); the feet at -0.97.")]
    public Vector3 lightLocalOffset = new Vector3(0f, 0.25f, 0.05f);
    [Tooltip("Grass response. 0.5 = the lantern/torch value = same brightness as the ground.")]
    public float grassStrength = 0.5f;

    // ── runtime ─────────────────────────────────────────────────────────
    readonly List<SuitTinter.SuitSlot> _slots = new List<SuitTinter.SuitSlot>();
    Transform _player;
    Light _light;
    float _elapsed;
    float _nextPlayerSearch;
    float _nextSlotScan;
    bool _visualsOn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        // Trap #1: also seeded in MainMenuController.EnsureGameplaySingletons.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("[FireflyGlow]");
        DontDestroyOnLoad(go);
        go.AddComponent<FireflyGlow>();
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

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The player rig, its renderers and the light child all died with the
        // old scene. Keep the timer (a reload mid-glow simply carries on), drop
        // every cached reference.
        _player = null;
        _light = null;
        _slots.Clear();
        _visualsOn = false;
        _nextPlayerSearch = 0f;
        _nextSlotScan = 0f;
    }

    // ── granting ────────────────────────────────────────────────────────

    /// Start (or restart) the minute. Always a full minute, never topped up.
    public void Grant()
    {
        bool wasActive = IsActive;
        TotalSeconds = DurationSeconds;
        SecondsLeft = DurationSeconds;
        // A refresh mid-glow should not re-run the fade-in from black.
        if (!wasActive) _elapsed = 0f;
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        bool had = IsActive;
        SecondsLeft = 0f;
        TotalSeconds = 0f;
        _elapsed = 0f;
        SetVisuals(0f);
        if (had) OnChanged?.Invoke();
    }

    // ── per frame ───────────────────────────────────────────────────────

    void Update()
    {
        if (!IsActive)
        {
            if (_visualsOn) SetVisuals(0f);
            return;
        }

        // Real seconds. Pause here is an input block, not a timeScale freeze
        // (co-op cannot stop time), so deltaTime is the honest clock — the
        // same call CatPerkManager makes.
        float dt = Time.deltaTime;
        _elapsed += dt;
        SecondsLeft -= dt;
        if (SecondsLeft <= 0f)
        {
            SecondsLeft = 0f;
            TotalSeconds = 0f;
            SetVisuals(0f);
            OnChanged?.Invoke();
            return;
        }

        float envelope = Mathf.Min(1f, _elapsed / Mathf.Max(0.01f, fadeInSeconds))
                       * Mathf.Min(1f, SecondsLeft / Mathf.Max(0.01f, fadeOutSeconds));
        float breath = 0.5f + 0.5f * Mathf.Sin(Time.time * breatheHz * Mathf.PI * 2f);
        float level = envelope * Mathf.Lerp(breatheMin, 1f, breath);
        SetVisuals(level);
    }

    /// Push one glow level (0 = off) to the suit slots and the light.
    void SetVisuals(float level)
    {
        if (level <= 0f)
        {
            if (_visualsOn)
            {
                for (int i = 0; i < _slots.Count; i++) SuitTinter.SetEmission(_slots[i], Color.black);
                if (_light != null) _light.enabled = false;
                _visualsOn = false;
            }
            return;
        }

        if (!ResolvePlayer()) return;
        _visualsOn = true;

        // The suit model is attached to the rig a little after it spawns, and
        // can be swapped, so the slot list is re-collected on a slow clock
        // while the glow runs — not once, and never per frame.
        if (Time.unscaledTime >= _nextSlotScan)
        {
            _nextSlotScan = Time.unscaledTime + 2f;
            SuitTinter.CollectSuitSlots(_player, _slots);
        }

        Color e = glowColor * (glowIntensity * level);
        e.a = 1f;
        for (int i = 0; i < _slots.Count; i++) SuitTinter.SetEmission(_slots[i], e);

        EnsureLight();
        if (_light != null)
        {
            _light.enabled = true;
            _light.intensity = lightIntensity * level;
            _light.range = lightRange;
            _light.color = glowColor;
        }
    }

    bool ResolvePlayer()
    {
        if (_player != null) return true;
        if (Time.unscaledTime < _nextPlayerSearch) return false;
        _nextPlayerSearch = Time.unscaledTime + 0.5f;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go == null) return false;
        _player = go.transform;
        _nextSlotScan = 0f;
        return true;
    }

    void EnsureLight()
    {
        if (_light != null || _player == null) return;
        var go = new GameObject("FireflyGlowLight");
        go.transform.SetParent(_player, false);
        go.transform.localPosition = lightLocalOffset;
        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.shadows = LightShadows.None;
        _light.color = glowColor;
        _light.range = lightRange;
        _light.intensity = 0f;
        // Light the world, not the suit: the suit glows by emission, and a
        // point light at the astronaut's own centre would light the inner
        // faces of the arms from inside. The reflect layer is the suit's.
        int reflect = LayerMask.NameToLayer("PlayerReflect");
        if (reflect >= 0) _light.cullingMask = ~(1 << reflect);
        // Grass never receives real additive lights; the marker feeds this one
        // into the grass shader. GrassLightAutoMarker would mark it anyway on
        // its 3 s sweep, at its own strength — marking it here first wins.
        var marker = go.AddComponent<GrassPointLight>();
        marker.grassStrength = grassStrength;
    }
}
