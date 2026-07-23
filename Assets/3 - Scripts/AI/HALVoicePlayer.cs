using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays HAL's pre-generated voice clips. HALLineHUD calls TryPlay(text)
/// when it shows a line — if the text has a canned clip in
/// HALVoiceManifest, the clip plays through this player's AudioSource.
/// If not, the line shows silently (chat replies, dynamic lines with
/// variables, anything not in the manifest).
///
/// Clips load lazily from StreamingAssets/AI/voice/ on first use and are
/// cached after that. First play of any given clip has ~50-100 ms of disk
/// + decode latency; subsequent plays are instant.
///
/// Auto-singleton with MainMenu skip — must also be seeded in
/// MainMenuController.EnsureGameplaySingletons per the trap in CLAUDE.md.
/// </summary>
public class HALVoicePlayer : MonoBehaviour
{
    public static HALVoicePlayer Instance { get; private set; }

    /// True while a voice clip is actively playing (used by HALLineHUD to hold a
    /// voiced tip only as long as its narration lasts).
    public bool IsPlaying => _source != null && _source.isPlaying;

    AudioSource _source;
    readonly Dictionary<string, AudioClip> _cache    = new Dictionary<string, AudioClip>();
    readonly HashSet<string>                _loading = new HashSet<string>();

    [Range(0f, 1f)] public float volume = 0.85f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HALVoicePlayer");
        DontDestroyOnLoad(go);
        go.AddComponent<HALVoicePlayer>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake  = false;
        _source.loop         = false;
        _source.spatialBlend = 0f;     // 2D — HAL is in your suit, not in the world

        // Pitch 1.0 — natural speed. Iterated 0.85 → 0.5 → 1.0; 0.5 was
        // too slow once paired with the deliberate George voice (the two
        // compounded into glacial delivery). 1.0 lets George's natural
        // cadence carry the "elderly British computer" feel without
        // additional slowdown.
        _source.pitch = 1.0f;

        // ── Computer-voice effects stack ─────────────────────────────────
        // Low-pass at 4 kHz strips the high-frequency air the natural TTS
        // adds, pushing the voice toward "speaker-through-suit / old
        // intercom" texture.
        var lp = gameObject.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency  = 4000f;
        lp.lowpassResonanceQ = 1f;

        // Subtle chorus doubles the voice on a small delay — gives that
        // "more than one source" synthetic quality HAL has. Wet/dry mixed
        // so the original line stays intelligible.
        var ch = gameObject.AddComponent<AudioChorusFilter>();
        ch.dryMix  = 0.7f;
        ch.wetMix1 = 0.35f;
        ch.wetMix2 = 0.20f;
        ch.wetMix3 = 0.10f;
        ch.delay   = 35f;     // ms
        ch.rate    = 0.6f;    // Hz
        ch.depth   = 0.05f;   // shallow modulation — too deep = warble

        // Very mild distortion for a hint of "electrical signal" grit.
        // Kept low (0.15) so words stay clear.
        var dist = gameObject.AddComponent<AudioDistortionFilter>();
        dist.distortionLevel = 0.15f;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Attempts to play voice for `line`. Returns true if the line had a
    /// canned clip and playback was started (or queued via async load),
    /// false if the line is not in the voice manifest (so caller can show
    /// it silently in the HUD).
    public bool TryPlay(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;

        // "HIDE HUD" setting mutes HAL's voice — catches every TTS path, including
        // voice-only lines that bypass the text strip (e.g. "Orbit matched."). Gated
        // on the user setting only, NOT the cinematic force, so the pod cutscene keeps
        // its narration. Lets capture clips with the HUD off stay silent.
        if (HudVisibility.UserHidden) return false;

        // Resolution order:
        //   1. Exact-text manifest entry (covers every stable line — death,
        //      killstreak, phase change, etc.).
        //   2. Per-planet atmosphere file ("atmo_enter_humble_abode.mp3").
        //      If the file isn't on disk yet, LoadAndPlay's UnityWebRequest
        //      will fail and we fall through to (3) on the NEXT TryPlay.
        //      First-play for a missing per-planet clip is silent + logged;
        //      we mark the planet-specific file as "missing" so subsequent
        //      lines of the same family skip straight to the generic clip.
        //   3. Generic pattern match (atmo_enter.mp3 / atmo_leave.mp3 etc.)
        //      so the player at least hears HAL's voice if the per-planet
        //      audio hasn't been generated yet.
        string preferredFile = null;
        if (HALVoiceManifest.Lines.TryGetValue(line, out var exactFile))
        {
            preferredFile = exactFile;
        }
        else
        {
            // Try per-planet atmosphere file first.
            string perPlanet = HALVoiceManifest.ResolvePerPlanetAtmosphere(line);
            if (!string.IsNullOrEmpty(perPlanet) && !_knownMissing.Contains(perPlanet))
                preferredFile = perPlanet;
            else
                preferredFile = HALVoiceManifest.ResolvePattern(line);
        }
        if (string.IsNullOrEmpty(preferredFile)) return false;

        float lineVol = HALVoiceManifest.VolumeFor(line);

        // Cache hit — play immediately.
        if (_cache.TryGetValue(preferredFile, out var clip) && clip != null)
        {
            PlayClip(clip, lineVol);
            return true;
        }

        // Cache miss — kick off async load. The first play of any line has
        // a small latency hit here; subsequent plays are instant. Pass the
        // original line through so LoadAndPlay can fall back to the generic
        // clip if the per-planet file is missing on disk.
        if (!_loading.Contains(preferredFile))
            StartCoroutine(LoadAndPlay(preferredFile, lineVol, line));
        return true;
    }

    // Tracks per-planet (or other family-specific) clip files that came
    // back missing from disk. We don't want to retry the same 404'd
    // UnityWebRequest every chat turn — once a file's missing, fall
    // straight to the generic family clip on subsequent TryPlay calls.
    readonly HashSet<string> _knownMissing = new HashSet<string>();

    /// Immediately silence any playing/queued narration. Called when HAL tips are
    /// cleared (quit-to-menu, or the "HIDE HUD" setting being switched on mid-line).
    public void Stop()
    {
        if (_source != null) _source.Stop();
    }

    /// Warms the clip cache for `line` WITHOUT playing it. Call ahead of a
    /// scripted sequence (the shuttle intro preloads its briefing during the
    /// wake-up phase) so the first play is never swallowed by scene-load
    /// hitches or first-use disk latency.
    public void Preload(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (!HALVoiceManifest.Lines.TryGetValue(line, out var file)) return;
        if (_cache.ContainsKey(file) || _loading.Contains(file)) return;
        StartCoroutine(LoadOnly(file));
    }

    IEnumerator LoadOnly(string file)
    {
        _loading.Add(file);
        string path = Path.Combine(Application.streamingAssetsPath, "AI", "voice", file);
        string url  = "file://" + path.Replace('\\', '/');
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip != null) _cache[file] = clip;
            }
        }
        _loading.Remove(file);
    }

    void PlayClip(AudioClip clip, float lineVol)
    {
        if (_source == null || clip == null) return;
        _source.Stop();
        _source.clip   = clip;
        _source.volume = volume * lineVol;
        _source.Play();
    }

    IEnumerator LoadAndPlay(string file, float lineVol, string originalLine = null)
    {
        _loading.Add(file);

        // StreamingAssets on Windows is a real filesystem path; "file://"
        // prefix is required for UnityWebRequest to treat it as a local
        // URL rather than a remote URL.
        string path = Path.Combine(Application.streamingAssetsPath, "AI", "voice", file);
        string url  = "file://" + path.Replace('\\', '/');

        bool succeeded = false;
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip != null)
                {
                    _cache[file] = clip;
                    PlayClip(clip, lineVol);
                    succeeded = true;
                }
            }
            else
            {
                Debug.LogWarning($"[HALVoicePlayer] Failed to load voice clip '{file}': {req.error}");
            }
        }

        _loading.Remove(file);

        // If we just failed loading a per-planet atmosphere clip, mark it
        // missing and try the generic fallback so the player doesn't get
        // dead silence on a line that DOES have a generic clip available.
        if (!succeeded && originalLine != null)
        {
            _knownMissing.Add(file);
            string generic = HALVoiceManifest.ResolvePattern(originalLine);
            if (!string.IsNullOrEmpty(generic) && generic != file)
            {
                if (_cache.TryGetValue(generic, out var cachedGeneric) && cachedGeneric != null)
                {
                    PlayClip(cachedGeneric, lineVol);
                }
                else if (!_loading.Contains(generic))
                {
                    // Recursive fallback — pass null for originalLine to avoid loop.
                    yield return StartCoroutine(LoadAndPlay(generic, lineVol, null));
                }
            }
        }
    }
}
