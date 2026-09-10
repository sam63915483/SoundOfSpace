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

    [Header("Breathing (every 10-15 s)")]
    [SerializeField] private AudioClip[] breathingClips;
    [SerializeField, Range(0f, 1f)] private float breathingVolume = 0.5f;

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
    AudioSource _jumpSrc;          // own source so the jump's pitch shift can't detune other one-shots
    AudioSource _windSrc;
    AudioSource _lifeSupportSrc;   // constant low suit life-support hum
    PlayerController _player;
    float _nextBreathTime;

    // Extra breathing variety loaded from StreamingAssets, mixed into the random
    // pool alongside the Inspector-assigned breathingClips. All are loudness-
    // normalized on load. Any of these the user deletes from StreamingAssets just
    // won't load (StreamingAudio logs a warning and skips it) — safe to prune the
    // .wav files to taste without touching code.
    // REPLACED 2026-09-10. The 13 clips that used to be here were a mixed bag —
    // Sam: "they arent the best… should really sound like an astronaut with a
    // helmet on" — and they varied by more than 20 dB between each other, which
    // no amount of runtime normalizing fully hides.
    //
    // These three are one brief (close-miked, inside a sealed visor, calm) cut
    // three ways, and all three are level-matched to -20 dBFS as FILES. Three
    // rather than one because a single clip every 10-15 s is audibly the same
    // event over and over.
    //
    // The old pool is not deleted — it is parked in Breaths/_retired/. Putting
    // it back is a matter of moving the files and listing them here again.
    static readonly string[] ExtraBreathFiles =
        { "Breaths/SuitHelmet1.wav", "Breaths/SuitHelmet2.wav", "Breaths/SuitHelmet3.wav" };
    readonly List<AudioClip> _loadedBreaths = new List<AudioClip>();
    readonly List<float> _loadedGains = new List<float>();   // per-clip loudness-normalize gain

    void Awake()
    {
        Instance = this;
        _oneShot  = CreateSource("SuitOneShot", false);
        _breathSrc = CreateSource("SuitBreath", false);
        _jumpSrc  = CreateSource("SuitJump", false);
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
                c => { if (c != null) { _loadedBreaths.Add(c); _loadedGains.Add(ComputeBreathGain(c)); } }));

        ScheduleNextBreath();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Loudness-normalize the breath clips: measure RMS and BOOST quieter clips up
    // toward a target so the faint ones become as audible as the good loud ones.
    // Boost-only (never reduces) so the clips that already sound right are
    // untouched. Capped so a near-silent clip isn't amplified into noise.
    const float BreathTargetRms = 0.14f;
    static float ComputeBreathGain(AudioClip clip)
    {
        if (clip == null || clip.samples <= 0 || clip.channels <= 0) return 1f;
        try
        {
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0) || data.Length == 0) return 1f;
            double sumSq = 0.0;
            for (int i = 0; i < data.Length; i++) { float s = data[i]; sumSq += s * s; }
            float rms = (float)System.Math.Sqrt(sumSq / data.Length);
            if (rms < 1e-5f) return 1f;
            return Mathf.Clamp(BreathTargetRms / rms, 1f, 6f);   // boost only
        }
        catch { return 1f; }
    }

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
        // Fixed 10-15s cadence (per request). This used to sit alongside
        // serialized breathMin/MaxInterval fields that it silently overrode, so
        // the Inspector advertised a 15-25s knob that did nothing — those fields
        // are gone now and this is the only cadence. Change it here.
        _nextBreathTime = Time.time + Random.Range(10f, 15f);
    }

    void Update()
    {
        // Breathing — random pick from Inspector clips + the StreamingAssets extras.
        if (_breathSrc != null && Time.time >= _nextBreathTime)
        {
            int serialized = breathingClips != null ? breathingClips.Length : 0;
            int total = serialized + _loadedBreaths.Count;
            // Never lay a breath on top of one that is still sounding — two
            // overlapping breaths are what "sped up breathing" actually is.
            if (total > 0 && !_breathSrc.isPlaying)
            {
                int idx = Random.Range(0, total);
                AudioClip clip;
                float gain;
                if (idx < serialized) { clip = breathingClips[idx]; gain = 1f; }
                else
                {
                    int li = idx - serialized;
                    clip = _loadedBreaths[li];
                    gain = li < _loadedGains.Count ? _loadedGains[li] : 1f;
                }
                if (clip != null) _breathSrc.PlayOneShot(clip, breathingVolume * gain);
            }
            // Reschedule unconditionally. It used to only reschedule when a clip
            // was actually available, so before the StreamingAssets breaths
            // finished loading this branch re-tested every single frame.
            ScheduleNextBreath();
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

    // ── Eating / burping (raw fish) ────────────────────────────────────────
    // (Appended at the END per the serialization convention in CLAUDE.md.)
    [Header("Eating / Burping (raw fish)")]
    [Tooltip("Looped chewing sound while the player holds fire to eat a raw fish. HeldItemViewmodel plays it — that's an auto-created singleton with no Inspector, so the clip is wired here on the Player instead. Assign Audio/Eating/eat_raw_fish_loop.")]
    [SerializeField] private AudioClip eatLoopClip;
    [SerializeField, Range(0f, 1f)] private float eatVolume = 0.75f;
    [Tooltip("One is picked at random and played a short delay after a raw fish is swallowed. Assign Audio/Eating/burp_01..03.")]
    [SerializeField] private AudioClip[] burpClips;
    [SerializeField, Range(0f, 1f)] private float burpVolume = 0.8f;
    [Tooltip("Random delay (seconds) between swallowing the fish and the burp.")]
    [SerializeField] private float burpDelayMin = 1f;
    [SerializeField] private float burpDelayMax = 3f;

    public AudioClip EatLoopClip => eatLoopClip;
    public float EatLoopVolume => eatVolume;

    /// Called by Hotbar the moment a raw fish is consumed. Picks a random burp
    /// and plays it after burpDelayMin..burpDelayMax seconds, so it lands as a
    /// reaction to the meal rather than on top of the last chew.
    public void PlayBurpAfterDelay()
    {
        if (burpClips == null || burpClips.Length == 0) return;
        if (!isActiveAndEnabled) return;   // the Player is disabled while piloting
        StartCoroutine(BurpRoutine());
    }

    System.Collections.IEnumerator BurpRoutine()
    {
        float lo = Mathf.Min(burpDelayMin, burpDelayMax);
        float hi = Mathf.Max(burpDelayMin, burpDelayMax);
        yield return new WaitForSeconds(Random.Range(lo, hi));

        var clip = burpClips[Random.Range(0, burpClips.Length)];
        if (clip != null) _oneShot?.PlayOneShot(clip, burpVolume);
    }

    // ── Jump effort ────────────────────────────────────────────────────────
    // (Appended at the END per the serialization convention in CLAUDE.md.)
    [Header("Jump Effort")]
    [Tooltip("Optional dedicated jump sound. LEAVE EMPTY to use the fallback: a random suit breath, pitched up and played quietly as an exertion grunt. The old PlayerController.jumpClip is dead — it was wired to a flatulence mp3.")]
    [SerializeField] private AudioClip jumpEffortClip;
    [Tooltip("Volume of the jump effort sound. 0 = SILENT, which is the shipped answer (Sam, 2026-09-10: \"i liked it better when there was no jump sound effect because you dont make a sound when you jump\"). PlayJump early-outs at 0, so this costs nothing. Raise it to bring the exhale back.")]
    [SerializeField, Range(0f, 1f)] private float jumpEffortVolume = 0f;
    [Tooltip("Random pitch range for the jump effort. Above 1 shortens a breath into a sharper exhale, which reads as effort rather than idle breathing.")]
    [SerializeField] private Vector2 jumpEffortPitch = new Vector2(1.25f, 1.45f);

    float _lastJumpSfxTime = -99f;

    /// Called by PlayerController the frame a grounded jump fires.
    ///
    /// Design note: there is no such thing as a "jump noise" a person makes — which is
    /// why the placeholder ended up being a fart. What a suited astronaut actually
    /// makes is a short involuntary EXHALE plus gear movement. So the fallback reuses
    /// the existing breath pool, pitched up (shorter + sharper = effort, not idling)
    /// and mixed low. The landing SFX carries the weight of the jump; this is only the
    /// push-off. Set jumpEffortVolume to 0 for no jump sound at all — a perfectly
    /// legitimate choice, and what a lot of shipped FPS games do.
    public void PlayJump()
    {
        if (jumpEffortVolume <= 0.001f) return;
        // Rate-limit. This used to be 0.25 s, which is FOUR exhales a second —
        // and since the fallback borrows a full idle BREATH and pitches it up,
        // moving around while hopping produced exactly what Sam described:
        // "constant sped-up breathing sounds" (2026-09-10). A person who just
        // exhaled does not do it again a quarter-second later.
        if (Time.time - _lastJumpSfxTime < jumpEffortCooldown) return;
        _lastJumpSfxTime = Time.time;

        // You just breathed. Push the idle cycle out so an ambient breath does
        // not land on top of the exertion one a moment later.
        _nextBreathTime = Mathf.Max(_nextBreathTime, Time.time + 5f);

        AudioClip clip = jumpEffortClip;
        float gain = 1f;
        if (clip == null)
        {
            // Fallback: borrow the breath pool. Prefer the StreamingAssets clips
            // (they carry loudness-normalize gains); fall back to the Inspector ones.
            if (_loadedBreaths.Count > 0)
            {
                int i = Random.Range(0, _loadedBreaths.Count);
                clip = _loadedBreaths[i];
                gain = i < _loadedGains.Count ? _loadedGains[i] : 1f;
            }
            else if (breathingClips != null && breathingClips.Length > 0)
            {
                clip = breathingClips[Random.Range(0, breathingClips.Length)];
            }
        }
        if (clip == null || _jumpSrc == null) return;

        // Pitch is a live property of the SOURCE, read continuously while the clip
        // plays — it is not captured by PlayOneShot. So the jump gets its own source
        // rather than borrowing _oneShot: setting the pitch there and restoring it on
        // the next line would just play the clip at the restored pitch, and leaving it
        // set would detune every equip/burp one-shot after it.
        // Clamped, not just randomised. jumpEffortPitch is already serialized on
        // the Player prefab at 1.25-1.45, so changing its C# default would do
        // nothing (a saved scene value always wins). At 1.4x a breath stops
        // sounding like a person and starts sounding like a tape running fast,
        // which is the other half of the "sped up" complaint. The ceiling is a
        // NEW field, so its default does apply.
        float lo = Mathf.Clamp(jumpEffortPitch.x, 1f, jumpEffortPitchCeiling);
        float hi = Mathf.Clamp(jumpEffortPitch.y, lo, jumpEffortPitchCeiling);
        _jumpSrc.pitch = Random.Range(lo, hi);
        _jumpSrc.PlayOneShot(clip, jumpEffortVolume * gain);
    }

    // ── Serialized fields APPENDED HERE (CLAUDE.md: never insert mid-class) ──
    [Header("Breathing rework (2026-09-10)")]
    [Tooltip("Seconds before another jump exhale may play. Was a hard-coded 0.25 - four a second while hopping, which is what made moving around sound like panic breathing.")]
    public float jumpEffortCooldown = 1.75f;
    [Tooltip("Hard ceiling on the jump exhale's pitch, whatever jumpEffortPitch says. Above roughly 1.2 a borrowed breath reads as sped-up tape rather than effort.")]
    public float jumpEffortPitchCeiling = 1.18f;
}
