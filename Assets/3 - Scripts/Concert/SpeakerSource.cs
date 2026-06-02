using UnityEngine;

// Inspector-friendly wrapper around an AudioSource for concert speakers.
// Auto-adds an AudioSource via [RequireComponent], configures it from the
// fields below, and (if isMusicSource) registers itself as the music source
// the ConcertAudioDirector analyzes for beat/band data.
[RequireComponent(typeof(AudioSource))]
public class SpeakerSource : MonoBehaviour
{
    [Header("Playback")]
    public AudioClip clip;
    public bool playOnStart = true;
    public bool loop = true;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Spatial")]
    [Tooltip("3D positional audio (fades with distance) vs 2D ambient.")]
    public bool spatial = true;
    [Tooltip("Distance within which the speaker plays at full volume.")]
    public float spatialMinDistance = 20f;
    [Tooltip("Beyond this distance the speaker is silent. With Linear rolloff, volume falls linearly from min→max.")]
    public float spatialMaxDistance = 300f;
    [Tooltip("Doppler pitch-shift on relative motion. Keep at 0 for music — otherwise walking around (and floating-origin shifts) makes the audio pitch warble.")]
    [Range(0f, 1f)] public float dopplerLevel = 0f;

    [Header("Director")]
    [Tooltip("If true, ConcertAudioDirector reads spectrum data from this speaker.")]
    public bool isMusicSource = true;

    AudioSource _src;
    public AudioSource Source => _src;
    public bool IsPlaying => _src != null && _src.isPlaying;

    void Awake()
    {
        _src = GetComponent<AudioSource>();
    }

    void Start()
    {
        ApplyConfig();
        if (clip != null)
        {
            _src.clip = clip;
            if (playOnStart) _src.Play();
        }
        if (isMusicSource) ConcertAudioDirector.RegisterSource(this);
    }

    void OnValidate()
    {
        // Apply tweaks live in Play mode so the user can scrub volume / spatial
        // settings without re-entering Play.
        if (_src == null) _src = GetComponent<AudioSource>();
        if (_src != null) ApplyConfig();
    }

    void ApplyConfig()
    {
        if (_src == null) return;
        _src.loop = loop;
        _src.volume = volume;
        _src.spatialBlend = spatial ? 1f : 0f;
        _src.minDistance = spatialMinDistance;
        _src.maxDistance = spatialMaxDistance;
        _src.rolloffMode = AudioRolloffMode.Linear;
        _src.dopplerLevel = dopplerLevel;
        _src.playOnAwake = false; // we control playback ourselves
    }

    public void Play()  { if (_src != null && clip != null) { _src.clip = clip; _src.Play(); } }
    public void Pause() { if (_src != null) _src.Pause(); }
    public void Stop()  { if (_src != null) _src.Stop(); }
}
