using System.Collections.Generic;
using UnityEngine;

// All player-body ("space suit") sounds, assigned in the Inspector on the
// Player GameObject. Three groups:
//   • equip / unequip / acquire one-shots — Hotbar calls PlayEquip/PlayUnequip
//     on every slot switch, and acquire fires when a new tool is earned.
//   • breathing — a random breath every breathMin..breathMax seconds.
//   • atmosphere wind — a loop whose volume scales with the player's speed
//     (relative to the planet) AND how deep they are in its atmosphere; pitch
//     rises with speed; silent in space.
//
// Lives on the Player GameObject, which is disabled while piloting — so
// breathing + wind pause then and the ship's own audio covers the cockpit.
public class PlayerSuitAudio : MonoBehaviour
{
    public static PlayerSuitAudio Instance { get; private set; }

    [Header("Equip / Unequip / Acquire (Hotbar one-shots)")]
    [SerializeField] private AudioClip equipClip;
    [SerializeField] private AudioClip unequipClip;
    [SerializeField] private AudioClip acquireClip;
    [SerializeField, Range(0f, 1f)] private float equipVolume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float unequipVolume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float acquireVolume = 0.85f;

    [Header("Breathing (every 15-25 s)")]
    [SerializeField] private AudioClip[] breathingClips;
    [SerializeField, Range(0f, 1f)] private float breathingVolume = 0.5f;
    [SerializeField] private float breathMinInterval = 15f;
    [SerializeField] private float breathMaxInterval = 25f;

    [Header("Atmosphere Wind")]
    [SerializeField] private AudioClip windLoopClip;
    [SerializeField, Range(0f, 1f)] private float windMaxVolume = 0.7f;
    [Tooltip("Speed (units/s, relative to the planet) at which wind reaches full volume + max pitch.")]
    [SerializeField] private float windFullSpeed = 40f;
    [SerializeField] private float windMinPitch = 0.7f;
    [SerializeField] private float windMaxPitch = 1.6f;
    [Tooltip("Atmosphere band thickness as a fraction of the nearest body's radius. Larger = wind audible higher up.")]
    [SerializeField, Range(0.05f, 2f)] private float atmosphereHeightFraction = 0.5f;
    // NOTE: keep new serialized fields appended at the END. Inserting one in the
    // middle shifts the player-build serialization layout and triggers
    // "extra field ... can't be serialized (expected ...)" build errors.
    [Tooltip("Wind stays silent below this speed (units/s, relative to the planet). It only starts once you're moving through the air this fast.")]
    [SerializeField] private float windStartSpeed = 15f;
    [Tooltip("Volume of the constant suit life-support hum (loaded from StreamingAssets/Audio/SuitAmbient.wav).")]
    [SerializeField, Range(0f, 1f)] private float lifeSupportVolume = 0.22f;

    AudioSource _oneShot;
    AudioSource _breathSrc;
    AudioSource _windSrc;
    AudioSource _lifeSupportSrc;   // constant low suit life-support hum
    PlayerController _player;
    float _nextBreathTime;

    // Extra breathing variety loaded from StreamingAssets, mixed into the random
    // pool alongside the Inspector-assigned breathingClips.
    static readonly string[] ExtraBreathFiles =
        { "SuitBreath1.wav", "SuitBreath2.wav", "SuitBreath3.wav", "SuitBreath4.wav" };
    readonly List<AudioClip> _loadedBreaths = new List<AudioClip>();

    void Awake()
    {
        Instance = this;
        _oneShot  = CreateSource("SuitOneShot", false);
        _breathSrc = CreateSource("SuitBreath", false);
        _windSrc  = CreateSource("SuitWind", true);
        if (windLoopClip != null) { _windSrc.clip = windLoopClip; _windSrc.volume = 0f; _windSrc.Play(); }

        // Constant, quiet life-support hum (helmet air recycler) — sells the
        // "sealed in a suit" feeling while on foot. Pauses with this GameObject
        // while piloting (the ship's own audio covers the cockpit then).
        _lifeSupportSrc = CreateSource("SuitLifeSupport", true);
        StartCoroutine(StreamingAudio.Load("Audio/SuitAmbient.wav", AudioType.WAV, c =>
        {
            if (c != null && _lifeSupportSrc != null)
            { _lifeSupportSrc.clip = c; _lifeSupportSrc.volume = lifeSupportVolume; _lifeSupportSrc.Play(); }
        }));

        _player = GetComponent<PlayerController>();
        if (_player == null) _player = FindObjectOfType<PlayerController>();

        for (int i = 0; i < ExtraBreathFiles.Length; i++)
            StartCoroutine(StreamingAudio.Load("Audio/" + ExtraBreathFiles[i], AudioType.WAV,
                c => { if (c != null) _loadedBreaths.Add(c); }));

        ScheduleNextBreath();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    AudioSource CreateSource(string childName, bool loop)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;   // 2D — the player's own suit
        s.volume = 1f;         // per-play volume passed to PlayOneShot / driven for wind
        return s;
    }

    // ── Called by Hotbar ─────────────────────────────────────────────
    public void PlayEquip()   { if (equipClip   != null) _oneShot?.PlayOneShot(equipClip,   equipVolume); }
    public void PlayUnequip() { if (unequipClip != null) _oneShot?.PlayOneShot(unequipClip, unequipVolume); }
    public void PlayAcquire() { if (acquireClip != null) _oneShot?.PlayOneShot(acquireClip, acquireVolume); }

    void ScheduleNextBreath()
    {
        float lo = Mathf.Max(0.1f, breathMinInterval);
        float hi = Mathf.Max(lo, breathMaxInterval);
        _nextBreathTime = Time.time + Random.Range(lo, hi);
    }

    void Update()
    {
        // Breathing — random pick from Inspector clips + the StreamingAssets extras.
        if (_breathSrc != null && Time.time >= _nextBreathTime)
        {
            int serialized = breathingClips != null ? breathingClips.Length : 0;
            int total = serialized + _loadedBreaths.Count;
            if (total > 0)
            {
                int idx = Random.Range(0, total);
                var clip = idx < serialized ? breathingClips[idx] : _loadedBreaths[idx - serialized];
                if (clip != null) _breathSrc.PlayOneShot(clip, breathingVolume);
                ScheduleNextBreath();
            }
        }

        // Atmosphere wind: speed (relative to the planet) × atmosphere density.
        if (_windSrc != null && windLoopClip != null)
        {
            if (_windSrc.clip == null) _windSrc.clip = windLoopClip;
            // Only while airborne — walking/running on the ground makes no wind,
            // however fast you move. Jumping / falling / jetpacking does.
            bool airborne = _player != null && !_player.IsOnGround;
            float atmo = AtmosphericWind.Factor(transform.position, atmosphereHeightFraction, out Vector3 bodyVel);
            Vector3 worldVel = _player != null ? _player.WorldVelocity : Vector3.zero;
            float speed = (worldVel - bodyVel).magnitude;
            // 0 below windStartSpeed, ramping to 1 at windFullSpeed.
            float speed01 = Mathf.InverseLerp(windStartSpeed, windFullSpeed, speed);
            float targetVol = airborne ? windMaxVolume * speed01 * atmo : 0f;
            _windSrc.volume = Mathf.MoveTowards(_windSrc.volume, targetVol, Time.deltaTime * 2f);
            _windSrc.pitch  = Mathf.Lerp(windMinPitch, windMaxPitch, speed01);
            if (!_windSrc.isPlaying) _windSrc.Play();
        }
    }
}
