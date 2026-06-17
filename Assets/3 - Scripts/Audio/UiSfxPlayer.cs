using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared UI sound player: button hover + click SFX for every menu (main menu,
/// save/load, pause menu) plus the pause-menu ambience loop. Clips load from
/// StreamingAssets/Audio/ so it works in BOTH the MainMenu scene and gameplay
/// without serialized refs.
///
/// Auto-creates in the first scene that loads and does NOT skip MainMenu (the
/// menu needs button SFX too), so it never hits the "seed in
/// EnsureGameplaySingletons" trap — it simply exists everywhere via
/// DontDestroyOnLoad. Call <see cref="Attach"/> once per button at build time.
/// </summary>
public class UiSfxPlayer : MonoBehaviour
{
    public static UiSfxPlayer Instance { get; private set; }

    AudioSource _sfx;        // 2D one-shots (hover/click)
    AudioSource _ambience;   // 2D looping pause ambience
    AudioClip _hover, _click, _pauseAmbience;

    const float HoverVolume    = 0.6f;
    const float ClickVolume    = 0.75f;
    const float AmbienceVolume = 0.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate() => Ensure();

    static UiSfxPlayer Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("UiSfxPlayer");
        DontDestroyOnLoad(go);
        return go.AddComponent<UiSfxPlayer>();   // Awake sets Instance
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0f;
        _sfx.ignoreListenerPause = true;   // hover/click still play while paused

        _ambience = gameObject.AddComponent<AudioSource>();
        _ambience.playOnAwake = false;
        _ambience.loop = true;
        _ambience.spatialBlend = 0f;
        _ambience.ignoreListenerPause = true;
        _ambience.volume = AmbienceVolume;

        StartCoroutine(StreamingAudio.Load("Audio/UIHover.wav",       AudioType.WAV,  c => _hover = c));
        StartCoroutine(StreamingAudio.Load("Audio/UIClick.wav",       AudioType.WAV,  c => _click = c));
        // Pause menu gets its own faster / spacier track, distinct from the slow
        // cinematic main-menu ambience.
        StartCoroutine(StreamingAudio.Load("Audio/PauseAmbience.mp3", AudioType.MPEG, c => _pauseAmbience = c));
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void Hover()
    {
        var p = Ensure();
        if (p._sfx != null && p._hover != null) p._sfx.PlayOneShot(p._hover, HoverVolume);
    }

    public static void Click()
    {
        var p = Ensure();
        if (p._sfx != null && p._click != null) p._sfx.PlayOneShot(p._click, ClickVolume);
    }

    /// <summary>Wire hover (PointerEnter) + click SFX onto a button. Call once at build.</summary>
    public static void Attach(Button btn)
    {
        if (btn == null) return;
        btn.onClick.AddListener(Click);
        var trigger = btn.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btn.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ => Hover());
        trigger.triggers.Add(entry);
    }

    /// <summary>Start the looping menu ambience (called when the pause menu opens).</summary>
    public static void StartPauseAmbience()
    {
        var p = Ensure();
        if (p._ambience == null || p._pauseAmbience == null) return;
        if (p._ambience.isPlaying) return;
        p._ambience.clip = p._pauseAmbience;
        p._ambience.Play();
    }

    /// <summary>Stop the pause ambience (called when the pause menu closes).</summary>
    public static void StopPauseAmbience()
    {
        if (Instance != null && Instance._ambience != null && Instance._ambience.isPlaying)
            Instance._ambience.Stop();
    }
}
