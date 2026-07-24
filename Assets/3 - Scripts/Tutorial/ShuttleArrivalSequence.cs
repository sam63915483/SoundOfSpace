using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shuttle landing intro (2026-07 opening redesign). Lives ON the Shuttle_Lander
// prefab root. Supersedes BOTH the old stasis-pod crash (PodArrivalSequence)
// and the cabin wake-up: the player wakes locked inside the shuttle's stasis
// chamber, rides the descent while HAL delivers the full briefing, the shuttle
// sets down EXACTLY on its scene-authored pose (Sam places it; we fly to it),
// the stasis door flips up, and the player walks out into normal play.
//
// Invoked by IntroSequenceController on a fresh New Game (it finds this
// component and runs Play() instead of the pod + wake-up path). Passive
// otherwise — no Awake/Start side effects, so Load/warm-reload never see it.
//
// Movement recipe is the proven PodArrivalSequence one: the PLAYER (floating-
// origin anchor) is frozen kinematic and driven planet-relative in LateUpdate;
// the shuttle is a child of its planet, so driving localPosition survives both
// the orbit and origin rebases. Execution order 50: after EndlessManager's
// origin shift (0), before CameraTransformFX (100).
[DefaultExecutionOrder(50)]
public class ShuttleArrivalSequence : MonoBehaviour
{
    // Three velocity-continuous phases (each seam's sink rate matches, so the
    // motion never jumps): a long ENTRY fall while the briefing plays, a hard
    // retro-thruster BRAKE once the atmosphere is hit (fast -> crawl), then a
    // slow FINAL sink that touches down at 0 m/s — a soft landing, no clip.
    [Header("Descent profile (metres above the authored rest pose)")]
    [SerializeField] float startAltitude = 900f;    // where the fade-in reveals the shuttle
    [SerializeField] float brakeAltitude = 150f;    // atmosphere entry: the retro-burn starts here
    [SerializeField] float hoverAltitude = 12f;     // brake ends here, moving at finalSinkSpeed
    [SerializeField] float entrySpeed    = 35f;     // sink rate (m/s) at the end of the entry fall
    [SerializeField] float finalSinkSpeed = 1.5f;   // crawl rate after the brake (soft-landing sink)

    [Header("Timing (seconds)")]
    [SerializeField] float fadeInTime    = 2f;
    [SerializeField] float entryDuration = 70f;     // long spacefall; film ~45s, touchdown at entry+18 = 88s (~43s into the film)
    [SerializeField] float brakeDuration = 8f;      // 150m -> 12m, entrySpeed -> finalSinkSpeed
    [SerializeField] float finalDuration = 10f;     // 12m -> touchdown, easing to 0 m/s
    [SerializeField] float touchdownHold = 1.2f;    // beat on the ground before the door opens
    [SerializeField] float doorOpenTime  = 1.8f;
    [SerializeField] float doorOpenAngle = 115f;    // stasis door flips up-and-out around the top-front pivot

    // Capsule is radius 0.5, pivot at CENTER: y 1.02 (pod-local, ×1.2 instance
    // scale) puts the feet ~3cm above the plinth top, z 0 clears the back glass
    // and the (collider-stripped) spine pad — NO overlap at kinematic release,
    // or PhysX depenetration hurls the player out of the chamber.
    [Header("Player placement (StasisPod-group local space)")]
    [SerializeField] Vector3 standOffset = new Vector3(0f, 1.02f, 0f);

    [Header("Shake")]
    [SerializeField] float approachShake = 0.03f;   // faint entry rumble
    [SerializeField] float landingShake  = 0.12f;   // strongest during the retro-burn, easing to 0 at touchdown

    [Header("Audio (wired from the old pod sequence's assets; null = silent)")]
    [SerializeField] AudioClip ambientHumClip;
    [SerializeField] AudioClip thrusterClip;
    [SerializeField] AudioClip touchdownClip;
    [SerializeField, Range(0f, 1f)] float ambientVolume   = 0.3f;
    [SerializeField, Range(0f, 1f)] float thrusterVolume  = 0.8f;
    [SerializeField, Range(0f, 1f)] float touchdownVolume = 0.4f;
    [SerializeField] float thrusterLowPassHz = 1100f;   // engines are outside the hull — muffled

    [Header("Stasis-wake grogginess (clears fully by touchdown)")]
    [SerializeField] Material grogginessMaterial;
    [SerializeField] float grogRecoverRate = 0.025f;    // full blur -> sharp across the descent

    // HAL briefing (2026-07 orientation-film rework). Text MUST byte-match
    // HALVoiceManifest.Lines or a line plays silent, so they live in code.
    // Delivered SEQUENTIALLY (each line starts when the previous finishes —
    // no dead-air timetable), with one authored beat: a 3.5s uneasy pause
    // after the far-from-Earth line while an anxious heartbeat fades in;
    // HAL then notes "Heart rate elevated..." and the heartbeat fades back
    // out halfway through that line. The film lead-in is last (the film
    // schedules itself filmDelayAfterLeadIn seconds after it's dispatched).
    // "Approaching Humble Abode" stays physics-triggered (approachLineAltitude).
    static readonly string[] _briefing = {
        "Stasis cycle complete. Welcome back, astronaut.",
        "You have been in transit for three years, and are twenty-five trillion miles from Earth.",
        "Heart rate elevated. Vitals irregular. Within acceptable parameters.",
        "It is normal for those emerging from stasis to have difficulty recalibrating. Remember — when the mission is complete, you will be returned home.",
        "To assist with recalibration, please enjoy this orientation film. Viewing is mandatory and comforting.",
    };
    const string ReverseThrusterLine = "Engaging reverse thrusters.";
    const string ApproachLine = "Approaching Humble Abode. Begin atmospheric entry.";

    // Cold open [CANON — wording LOCKED Jul 23, GDD §8]: the recruiter's three
    // questions on the black screen, each dismissed by a click (the click IS
    // the "yes"), then "Open your eyes." — whose clicks pry the eyes open.
    // Text-only; no voice. Do not reword without Sam.
    static readonly string[] _coldOpenQuestions = {
        "Do you want your life to mean something?",
        "Would you give anything for a purpose?",
        "Even yourself?",
    };
    const string OpenYourEyesLine = "Open your eyes.";

    // ── Runtime ──────────────────────────────────────────────────────────────
    PlayerController _pc;
    Transform _playerT;
    Rigidbody _rb;
    Transform _podGrp, _doorPivot;
    Renderer _screenRenderer;
    ShuttleExitDoor _exitDoor;
    UnityEngine.Video.VideoPlayer _video;
    RenderTexture _videoRT;
    Material _filmMat, _screenOriginalMat;
    AudioSource _videoAudio;
    bool _filmScheduled, _filmStarted, _filmEnded;
    bool _approachSpoken;
    bool _released;
    Image _veil;
    Image _topLid, _botLid;
    TextMeshProUGUI _prompt;
    TextMeshProUGUI _coldOpenText;
    float _openness, _opennessTarget;
    int _wakeClicks;
    bool _wakeArmed, _wokeUp;
    Vector3 _restLocalPos;
    Vector3 _localUp;
    Quaternion _doorRestRot;
    float _altitude;
    float _shakeAmp;
    bool _flying;
    bool _active;
    bool _skip;
    bool _wasKinematic;
    AudioSource _heartbeat;
    AudioSource _wind;
    Quaternion _restLocalRot;
    float _prevSpin;
    Vector3 _worldShake;
    Canvas _canvas;
    Image _fade;
    AudioSource _ambient, _thruster, _sfx;
    GrogginessImageEffect _grog;
    ShuttleThrustFX _thrustFX;
    Camera _landingCam;
    RenderTexture _landingRT;
    Material _landingMat;
    float _landingNextAt;

    public bool IsActive => _active;

    // Test hook: jumps straight to the landed/skip path (same release code).
    public void SkipNow() { _skip = true; }

    // Flip on to log a per-FixedUpdate release report to the scratchpad —
    // this is how the one-frame orbital-lag seating bug was caught.
    const bool ReleaseDiagnostics = false;

    // -- fields below appended after initial release; keep order (serialization) --

    [Header("Orientation film (plays on the pod TV; exit door unlocks after)")]
    [Tooltip("The ~60s orientation video. Plays on TVScreen; audio from the TV.")]
    public UnityEngine.Video.VideoClip orientationFilm;

    [Tooltip("Seconds after the lead-in line before the film starts (lead-in is ~7s spoken — keep the film clear of it).")]
    public float filmDelayAfterLeadIn = 8f;

    [Range(0f, 1f), Tooltip("Film audio volume (ducked to half under HAL callouts).")]
    public float filmVolume = 0.85f;

    [Tooltip("Seconds after the film ends before the exit door folds open.")]
    public float doorUnlockDelay = 2f;

    [Tooltip("'Approaching Humble Abode' fires when the descent crosses this altitude (m). Tuned to land AFTER the film has started (~57s with the default profile).")]
    public float approachLineAltitude = 550f;

    [Header("Wake-up (eyes shut; descent holds until fully open)")]
    [Tooltip("Seconds before the 'Press LMB' tip appears (clicks count from the start).")]
    public float wakePromptDelay = 10f;

    [Tooltip("Gap between 'Wake up' repeats.")]
    public float wakeLoopInterval = 1.0f;

    [Tooltip("Clicks to prise the eyes fully open.")]
    public int clicksToWake = 6;

    [Tooltip("Seconds each click's eye-opening step takes.")]
    public float unfadePerClick = 0.30f;

    [Header("Anxious heartbeat (the uneasy beat after the far-from-Earth line)")]
    [Tooltip("Nervous heartbeat loop; fades in during the pause, out during the vitals line.")]
    public AudioClip heartbeatClip;

    [Range(0f, 1f)]
    public float heartbeatVolume = 0.55f;

    [Tooltip("Seconds of uneasy silence after the far-from-Earth line.")]
    public float pauseAfterEarthLine = 3.5f;

    [Tooltip("Seconds into the recalibration/returned-home REASSURANCE line before the heartbeat starts fading out (~halfway through it).")]
    public float heartbeatOutDelay = 4.5f;

    [Header("Descent spin (unwinds as the shuttle nears the ground)")]
    [Tooltip("Full turns of yaw at start altitude; unwinds smoothly to EXACTLY the authored rest orientation at touchdown.")]
    public float spinTurns = 2f;

    [Header("Atmosphere wind (loudest between entry and the ground)")]
    [Tooltip("Looping wind/whoosh; fades in below windStartAltitude, peaks midway, silent at touchdown.")]
    public AudioClip windClip;
    public float windStartAltitude = 450f;
    [Range(0f, 1f)] public float windVolume = 0.8f;

    [Header("Grogginess breathing (pulses, clears with proximity to the ground)")]
    [Range(0f, 1f), Tooltip("Woozy base level right after waking; scales down with altitude to 0 at touchdown.")]
    public float grogMaxAfterWake = 0.55f;
    public float grogBreatheSpeed = 1.1f;   // rad/sec — worse -> better -> worse rhythm
    public float grogBreatheMax = 1.9f;     // peak multiplier of the base level

    [Range(0f, 1f), Tooltip("Heartbeat level under the opening lines, before the far-from-Earth surge.")]
    public float heartbeatQuietVolume = 0.35f;

    [Tooltip("Seconds into the far-from-Earth line before the heartbeat surges to full volume (~halfway).")]
    public float earthLineSurgeDelay = 2.5f;

    [Range(0f, 1f), Tooltip("Fraction of the ship's shake the player inherits (1 = fully locked to the cabin, 0 = rock steady).")]
    public float playerShakeFraction = 0.5f;

    [Tooltip("Seal-release + glass-slide sound when the stasis door opens.")]
    public AudioClip stasisDoorClip;

    [Header("Cold open (recruiter questions on black; wording LOCKED — GDD §8)")]
    [Tooltip("Black-screen beat before the first question appears.")]
    public float coldOpenStartDelay = 1.5f;

    [Tooltip("Seconds each line takes to fade in (clicks don't register until it's fully in).")]
    public float questionFadeIn = 0.9f;

    [Tooltip("Seconds each line takes to fade out after the click.")]
    public float questionFadeOut = 0.6f;

    [Tooltip("Dark gap between one question fading out and the next fading in.")]
    public float questionGap = 0.4f;

    // ── Entry point (called by IntroSequenceController on fresh New Game) ────
    public IEnumerator Play()
    {
        _skip = false;
        if (!Setup()) yield break;

        // Eyes-shut wake-up: "Wake up" loops and the player clicks their eyes
        // open. The shuttle HOLDS at start altitude until the eyes are fully
        // open — keep them shut for 30s and you miss nothing; the descent (and
        // the whole briefing/film schedule) only starts once you're awake.
        yield return WakeUpPhase();
        StartCoroutine(RunBriefing());   // sequential narration, in parallel with the descent
        yield return Approach();

        // Atmosphere hit: HAL calls the burn, the shuttle brakes HARD from the
        // entry fall to a crawl, then sinks the last few metres to a dead-soft
        // 0 m/s touchdown.
        if (!_skip)
        {
            Speak(ReverseThrusterLine);
            StartThruster();
            if (_thrustFX != null) _thrustFX.Ignite();   // engine flare + correction nozzles light up
            yield return Braking();
            yield return FinalDescent();
        }

        if (_skip)
        {
            // Skipped: blink to black, snap everything to the landed state, reveal.
            yield return Fade(0f, 1f, 0.15f);
            _altitude = 0f;
            transform.localPosition = _restLocalPos;
            transform.localRotation = _restLocalRot;
            SyncPlayerToPod();
            StopThruster(0f);
            if (_thrustFX != null) _thrustFX.StopImmediate();
            StopFilm();
            yield return Fade(1f, 0f, 0.4f);
        }
        else
        {
            _altitude = 0f;
            transform.localPosition = _restLocalPos;   // land EXACTLY on the authored pose
            _shakeAmp = 0f;
            if (_sfx != null && touchdownClip != null) _sfx.PlayOneShot(touchdownClip, touchdownVolume);
            StopThruster(1.5f);
            if (_thrustFX != null) _thrustFX.Shutdown();   // flames collapse with the burn
            yield return WaitUnscaled(touchdownHold);
        }

        // Hand the up-override to a blend proxy BEFORE the release: clearing it
        // outright makes the controller snap from shuttle-up to gravity-up in
        // one FixedUpdate — a visible hitch right as the door finishes. The
        // proxy eases between the two over a second and a half instead.
        StartCoroutine(BlendUpOverrideOut(1.5f));
        yield return OpenStasisDoor();
        ReleasePlayer();   // free to roam the cabin; the film keeps playing

        // The EXIT door stays locked until the film ends (the player never
        // controls it). Skip = straight to open, no film.
        if (_skip)
        {
            StopFilm();
            if (_exitDoor != null) _exitDoor.OpenInstant();
        }
        else
        {
            float safety = (orientationFilm != null ? (float)orientationFilm.length : 60f) + 90f;
            float waited = 0f;
            while (!_skip && waited < safety && !FilmFinished())
            {
                // Belt + braces: loopPointReached is the primary end signal,
                // but a stopped player counts too.
                if (_filmStarted && _video != null && !_video.isPlaying) _filmEnded = true;
                waited += Time.deltaTime;
                yield return null;
            }
            StopFilm();   // restores the green glow
            if (_skip) { if (_exitDoor != null) _exitDoor.OpenInstant(); }
            else
            {
                yield return WaitUnscaled(doorUnlockDelay);
                if (_exitDoor != null) _exitDoor.Open();
            }
        }

        Teardown();
    }

    bool FilmFinished()
    {
        if (!_filmScheduled) return true;   // no film (clip missing / never reached the lead-in)
        return _filmStarted ? _filmEnded : false;
    }

    // ── Wake-up (eyes shut in the stasis chamber; descent holds) ─────────────
    IEnumerator WakeUpPhase()
    {
        PreloadVoices();   // warm the whole briefing bank while the eyes are shut

        // Hold the first "Wake up" until (a) the scene-load hitch storm has
        // passed (5 consecutive smooth frames) and (b) the clip is actually
        // decoded — otherwise the first calls play into frozen frames or a
        // cold cache and the player hears nothing until half-woken.
        int calm = 0; float guard = 0f;
        while (calm < 5 && guard < 8f && !_skip)
        {
            guard += Time.unscaledDeltaTime;
            calm = Time.unscaledDeltaTime < 0.1f ? calm + 1 : 0;
            yield return null;
        }
        float warm = 0f;
        while (warm < 4f && !_skip
               && !(HALVoicePlayer.Instance != null && HALVoicePlayer.Instance.IsCached(_briefing[0])))
        {
            warm += Time.unscaledDeltaTime;
            yield return null;
        }

        // Cold open: the three recruiter questions, click-to-advance. The eyes
        // stay fully shut throughout — _wakeArmed is still false, so these
        // clicks never touch the eyelids.
        yield return ColdOpenQuestions();

        // "Open your eyes." — NOW the clicks pry the eyes open (the existing
        // multi-click mechanic), HAL's "Wake up" murmur starts looping under
        // the line, and the line melts away on the first click.
        yield return ShowColdOpenLine(OpenYourEyesLine);
        _wakeArmed = true;
        var wakeLoop = StartCoroutine(WakeUpLoop());
        StartCoroutine(ShowWakePromptAfter(wakePromptDelay));

        yield return new WaitUntil(() => _wakeClicks >= 1 || _skip);
        StartCoroutine(FadeColdOpenLine(0f, questionFadeOut));
        yield return new WaitUntil(() => _wakeClicks >= clicksToWake || _skip);
        StopCoroutine(wakeLoop);
        if (_prompt != null) _prompt.gameObject.SetActive(false);
        _opennessTarget = 1f;
        yield return new WaitUntil(() => _openness >= 0.999f || _skip);
        _wokeUp = true;
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.ClearAll();
        DestroyWakeOverlay();
    }

    IEnumerator WakeUpLoop()
    {
        while (!_wokeUp && !_skip)
        {
            Speak("Wake up");
            yield return null;
            yield return new WaitWhile(() => HALLineHUD.Instance != null && !HALLineHUD.Instance.IsIdle && !_skip);
            yield return WaitUnscaled(wakeLoopInterval);
        }
    }

    // Alt-tab/pause-safe timing. WaitForSecondsRealtime runs on the OS wall
    // clock, which KEEPS COUNTING while the player is paused (fullscreen
    // alt-tab) — every scripted beat "expires" during the pause and fires the
    // instant the game resumes, while the voice clips and film (which freeze
    // with the player loop) are still mid-line: the whole briefing desyncs.
    // Accumulating CLAMPED unscaled deltas instead freezes with the player
    // loop and swallows the giant refocus hitch frame.
    static float UDT => Mathf.Min(Time.unscaledDeltaTime, 0.25f);

    IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds && !_skip) { t += UDT; yield return null; }
    }

    // ── Cold open (three questions on black; GDD §8, wording locked) ─────────
    IEnumerator ColdOpenQuestions()
    {
        float t = 0f;
        while (t < coldOpenStartDelay && !_skip) { t += UDT; yield return null; }

        foreach (var q in _coldOpenQuestions)
        {
            if (_skip) yield break;
            yield return ShowColdOpenLine(q);
            // Poll only AFTER the fade-in completes — a click while the line is
            // still materialising doesn't count, so it can't be skimmed blind.
            while (!_skip && !TutorialGate.PrimaryActionPressed()) yield return null;
            yield return FadeColdOpenLine(0f, questionFadeOut);
            t = 0f;
            while (t < questionGap && !_skip) { t += UDT; yield return null; }
        }
    }

    IEnumerator ShowColdOpenLine(string line)
    {
        if (_coldOpenText == null) yield break;
        _coldOpenText.text = line;
        _coldOpenText.alpha = 0f;
        _coldOpenText.gameObject.SetActive(true);
        yield return FadeColdOpenLine(1f, questionFadeIn);
    }

    IEnumerator FadeColdOpenLine(float target, float seconds)
    {
        if (_coldOpenText == null) yield break;
        float from = _coldOpenText.alpha, t = 0f;
        while (t < seconds && !_skip && _coldOpenText != null)
        {
            t += UDT;
            _coldOpenText.alpha = Mathf.Lerp(from, target, seconds > 0f ? t / seconds : 1f);
            yield return null;
        }
        if (_coldOpenText != null)
        {
            _coldOpenText.alpha = target;
            if (target <= 0.01f) _coldOpenText.gameObject.SetActive(false);
        }
    }

    IEnumerator ShowWakePromptAfter(float delay)
    {
        float t = 0f;
        while (t < delay && _wakeClicks < clicksToWake && !_skip) { t += UDT; yield return null; }
        if (_wakeClicks < clicksToWake && !_skip && _prompt != null)
        {
            _prompt.text = "Press " + PromptGlyphs.PrimaryAction;
            _prompt.gameObject.SetActive(true);
        }
    }

    // Preloads every scripted clip while the eyes are shut, so the first line
    // ("Stasis cycle complete...") is never swallowed by scene-load hitches or
    // first-use disk latency.
    void PreloadVoices()
    {
        var vp = HALVoicePlayer.Instance;
        if (vp == null) return;
        vp.Preload("Wake up");
        foreach (var line in _briefing) vp.Preload(line);
        vp.Preload(ApproachLine);
        vp.Preload(ReverseThrusterLine);
    }

    // Positions both lids + veil for the given openness (0 shut, 1 open),
    // with an idle woozy drift that fades as the eyes finish opening.
    void ApplyEyelids(float openness)
    {
        if (_topLid == null || _botLid == null) return;
        const float lidTopClosed = 0.56f, lidBottomClosed = 0.50f, lidOpenOvershoot = 0.06f;
        const float woozeAmp = 0.022f, woozeSpeed = 1.6f;

        float wooze = (Mathf.Sin(Time.unscaledTime * woozeSpeed) * woozeAmp
                       + Mathf.Sin(Time.unscaledTime * woozeSpeed * 2.3f + 1.7f) * woozeAmp * 0.4f) * (1f - openness);

        float topCover = Mathf.Lerp(lidTopClosed, -lidOpenOvershoot, openness) + wooze;
        float botCover = Mathf.Lerp(lidBottomClosed, -lidOpenOvershoot, openness) + wooze * 0.5f;

        var trt = _topLid.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f - topCover); trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var brt = _botLid.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(1f, botCover);
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        if (_veil != null) _veil.color = new Color(0f, 0f, 0f, Mathf.Lerp(1f, 0f, openness));
    }

    // Feathered eyelid sprite: opaque at the outer (screen) edge, soft at the
    // lash line — same construction as the cabin wake-up's.
    static Sprite MakeLidSprite(bool opaqueAtTop)
    {
        const int h = 64;
        const float feather = 0.30f;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            float fromOuter = opaqueAtTop ? v : 1f - v;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fromOuter / feather));
            tex.SetPixel(0, y, new Color(0f, 0f, 0f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
    }

    void DestroyWakeOverlay()
    {
        if (_coldOpenText != null) { Destroy(_coldOpenText.gameObject); _coldOpenText = null; }
        if (_veil != null) { Destroy(_veil.gameObject); _veil = null; }
        if (_topLid != null) { Destroy(_topLid.gameObject); _topLid = null; }
        if (_botLid != null) { Destroy(_botLid.gameObject); _botLid = null; }
        if (_prompt != null) { Destroy(_prompt.gameObject); _prompt = null; }
    }

    // Slerps the alignment target from the shuttle's up to true gravity-up,
    // then releases the override entirely. Runs across the door-open beat and
    // the first free steps; the player never feels the frame change.
    IEnumerator BlendUpOverrideOut(float seconds)
    {
        var body = GetComponentInParent<CelestialBody>();
        var proxy = new GameObject("ShuttleUpBlendProxy").transform;
        proxy.SetParent(transform, false);
        Vector3 fromUp = transform.up;
        proxy.rotation = Quaternion.FromToRotation(Vector3.up, fromUp);
        PlayerController.UpOverrideTransform = proxy;

        float t = 0f;
        while (t < seconds && _playerT != null)
        {
            t += Time.deltaTime;
            Vector3 gravityUp = body != null ? (_playerT.position - body.Position).normalized : fromUp;
            Vector3 up = Vector3.Slerp(fromUp, gravityUp, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            proxy.rotation = Quaternion.FromToRotation(Vector3.up, up);
            yield return null;
        }
        if (PlayerController.UpOverrideTransform == proxy)
            PlayerController.UpOverrideTransform = null;
        Destroy(proxy.gameObject);
    }

    bool Setup()
    {
        _pc = FindObjectOfType<PlayerController>();
        if (_pc == null) return false;
        _playerT = _pc.transform;
        _rb = _pc.Rigidbody;
        if (_rb == null) return false;

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "StasisPod") _podGrp = t;
            else if (t.name == "StasisDoor_Pivot") _doorPivot = t;
            else if (t.name == "TVScreen") _screenRenderer = t.GetComponent<Renderer>();
        }
        if (_podGrp == null) return false;
        if (_doorPivot != null) _doorRestRot = _doorPivot.localRotation;
        _exitDoor = GetComponentInChildren<ShuttleExitDoor>(true);
        _filmScheduled = _filmStarted = _filmEnded = false;
        _approachSpoken = false;
        _released = false;

        _restLocalPos = transform.localPosition;
        _restLocalRot = transform.localRotation;
        _localUp = _restLocalPos.sqrMagnitude > 0.001f ? _restLocalPos.normalized : Vector3.up;
        _altitude = startAltitude;
        _prevSpin = SpinAngle(_altitude);

        _wasKinematic = _rb.isKinematic;
        _rb.isKinematic = true;                                // no N-body gravity in the chamber
        // The controller's own FixedUpdate alignment keeps running while
        // kinematic — route it to the shuttle's up so body AND camera (which
        // snapshots the FixedUpdate rotation) agree for the whole ride.
        PlayerController.UpOverrideTransform = transform;
        TutorialGate.LockAll();
        TutorialGate.Unlock(TutorialAbility.MouseLook);        // free-look through the chamber glass
        IntroSequenceController.SuppressGroggyCameraFx = true;
        HALCommentator.SuppressAutonomous = true;
        FallDamage.Suppressed = true;
        SpeedLinesOverlay.Suppressed = true;                   // no air streaks inside the sealed chamber
        // NOTE: unlike the pod cinematic we deliberately DO NOT hide the HUD —
        // the helmet visor frame is registered with HudVisibility, and hiding
        // it reads as "no helmet on" in the stasis chamber (then it pops on at
        // release). Suit + helmet stay on for the whole ride.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _flying = true;
        _active = true;
        transform.localPosition = _restLocalPos + _localUp * _altitude;
        SyncPlayerToPod();
        _pc.ForceLookAt(_podGrp.TransformPoint(standOffset + Vector3.forward * 6f));  // face the chamber door / cabin

        BuildCanvas();
        _ambient = AddLoop(ambientHumClip, ambientVolume);
        _wind = AddLoop(windClip, 0f);   // volume driven by altitude in LateUpdate
        if (_wind != null && windClip != null) _wind.Play();
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.spatialBlend = 0f; _sfx.playOnAwake = false;

        if (grogginessMaterial != null && _pc.Camera != null)
        {
            _grog = _pc.Camera.gameObject.AddComponent<GrogginessImageEffect>();
            _grog.material = grogginessMaterial;
            _grog.intensity = 1f;                              // stasis wake: fully blurry, clears over the descent
        }

        _thrustFX = gameObject.AddComponent<ShuttleThrustFX>();
        _thrustFX.Initialize(transform);   // dark until Ignite() at the retro-burn

        StartLandingCam();   // belly camera feed on the TV until the film claims it
        return true;
    }

    // ── Landing cam (belly camera on the TV until the orientation film) ──────
    // Second-camera gotchas, solved the RearViewMirror way:
    //  • Space dust is Graphics.DrawMeshInstanced from the dust field's
    //    LateUpdate — manual Render() calls MISS those submissions, so the
    //    camera stays a real enabled camera, toggled on for one frame at
    //    ~15fps (the CCTV cadence). Enabled cameras in the render loop see
    //    the dust.
    //  • Ocean/atmosphere are post effects on the player camera — copied onto
    //    this one (OceanMaskRenderer + CustomPostProcessing, JSON field copy)
    //    with a depth texture so the water renders.
    void StartLandingCam()
    {
        if (_screenRenderer == null) return;
        Transform flare = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "EngineFlare") { flare = t; break; }

        var go = new GameObject("LandingCam");
        go.transform.SetParent(transform, true);
        go.transform.position = (flare != null ? flare.position : transform.position) - transform.up * 0.45f;
        go.transform.rotation = Quaternion.LookRotation(-transform.up, transform.forward);
        _landingCam = go.AddComponent<Camera>();
        _landingCam.enabled = false;                 // flipped on for single frames in LateUpdate
        _landingCam.fieldOfView = 78f;
        _landingCam.nearClipPlane = 0.3f;
        _landingCam.farClipPlane = 8000f;
        _landingCam.depthTextureMode = DepthTextureMode.Depth;   // ocean effect needs scene depth
        _landingCam.depth = -10f;                    // render before the main camera
        if (_pc != null && _pc.Camera != null) _landingCam.cullingMask = _pc.Camera.cullingMask;

        // Ocean + post stack, copied from the live player camera (guarded —
        // a failure just means a feed without water, never an error).
        var mainCam = _pc != null && _pc.Camera != null ? _pc.Camera : Camera.main;
        if (mainCam != null)
        {
            CopyCamEffect(mainCam, "OceanMaskRenderer");
            CopyCamEffect(mainCam, "CustomPostProcessing");
        }

        _landingRT = new RenderTexture(512, 384, 16);
        _screenOriginalMat = _screenRenderer.sharedMaterial;   // the green glow; restored on handoff
        _landingMat = new Material(Shader.Find("Unlit/Texture"));
        _landingMat.mainTexture = _landingRT;
        _landingCam.targetTexture = _landingRT;
        _screenRenderer.sharedMaterial = _landingMat;
    }

    void CopyCamEffect(Camera mainCam, string typeName)
    {
        try
        {
            var src = mainCam.GetComponent(typeName);
            if (src == null) return;
            if (_landingCam.GetComponent(typeName) != null) return;
            var dst = _landingCam.gameObject.AddComponent(src.GetType());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), dst);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ShuttleArrival] couldn't copy " + typeName + " onto the landing cam: " + e.Message);
        }
    }

    void StopLandingCam()
    {
        if (_landingCam != null) { Destroy(_landingCam.gameObject); _landingCam = null; }
        // Give the screen back (green glow) unless the film has already taken it.
        if (!_filmStarted && _screenRenderer != null && _screenOriginalMat != null)
            _screenRenderer.sharedMaterial = _screenOriginalMat;
        if (_landingMat != null) { Destroy(_landingMat); _landingMat = null; }
        if (_landingRT != null) { _landingRT.Release(); Destroy(_landingRT); _landingRT = null; }
    }

    // How much yaw the shuttle carries at a given altitude. A FUNCTION of
    // altitude (not an integration), so it unwinds to EXACTLY zero — the
    // authored rest orientation — at touchdown: fast spin up high, gently
    // slowing the closer it gets to the ground.
    float SpinAngle(float alt)
    {
        return 360f * spinTurns * Mathf.Pow(Mathf.Clamp01(alt / Mathf.Max(1f, startAltitude)), 1.7f);
    }

    // Drive shuttle + player planet-relative, after the origin shift (order 50).
    void LateUpdate()
    {
        // Landing-cam cadence: a real enabled camera flipped on for single
        // frames at ~15fps (manual Render() misses the space-dust submissions
        // — see StartLandingCam). Driven BEFORE the _flying gate so it can
        // never stick 'enabled' after touchdown.
        if (_landingCam != null)
        {
            _landingCam.transform.rotation = Quaternion.LookRotation(-transform.up, transform.forward);
            bool due = !_filmStarted && !_skip && _openness > 0.01f
                       && Time.unscaledTime >= _landingNextAt;
            if (due) _landingNextAt = Time.unscaledTime + 1f / 15f;
            _landingCam.enabled = due;
        }

        if (!_flying) return;

        // The SHUTTLE carries the shake (the cabin visibly rattles against
        // the world); the player inherits only playerShakeFraction of it in
        // SyncPlayerToPod, so there's relative motion — you feel shaken AND
        // see the ship shaking around you.
        Vector3 shakeLocal = Vector3.zero;
        if (_shakeAmp > 0f)
        {
            float ts = Time.unscaledTime * 30f;
            shakeLocal = new Vector3(Mathf.PerlinNoise(ts, 0f) - 0.5f,
                                     Mathf.PerlinNoise(0f, ts) - 0.5f,
                                     Mathf.PerlinNoise(ts, ts) - 0.5f) * (2f * _shakeAmp);
        }
        transform.localPosition = _restLocalPos + _localUp * _altitude + shakeLocal;
        _worldShake = transform.parent != null ? transform.parent.TransformVector(shakeLocal) : shakeLocal;

        // Descent spin — and co-rotate the player with the cabin so the world
        // sweeps past the windows instead of the cabin spinning around them.
        float spin = SpinAngle(_altitude);
        transform.localRotation = _restLocalRot * Quaternion.AngleAxis(spin, Vector3.up);
        float dSpin = spin - _prevSpin;
        _prevSpin = spin;
        if (Mathf.Abs(dSpin) > 0.0001f && _playerT != null && _rb != null)
        {
            Quaternion dq = Quaternion.AngleAxis(dSpin, transform.up);
            _playerT.rotation = dq * _playerT.rotation;
            _rb.rotation = _playerT.rotation;
        }

        SyncPlayerToPod();

        // Atmosphere wind: silent above windStartAltitude, peaks midway down,
        // silent again at the pads.
        if (_wind != null)
        {
            float x = Mathf.Clamp01(_altitude / Mathf.Max(1f, windStartAltitude));
            _wind.volume = windVolume * Mathf.Sin(Mathf.PI * (1f - x));
        }

        if (_thrustFX != null) _thrustFX.SetAltitude(_altitude);

        // The approach callout is physics-triggered (crossing the altitude
        // threshold during the real descent), not a scheduled timer — it lands
        // over the film with the film's audio ducked underneath.
        if (!_approachSpoken && _altitude <= approachLineAltitude)
        {
            _approachSpoken = true;
            Speak(ApproachLine);
        }
    }

    void SyncPlayerToPod()
    {
        if (_rb == null || _podGrp == null) return;
        // Pod point already carries the shuttle's FULL shake; back out part of
        // it so the player only rides playerShakeFraction of the rattle.
        Vector3 pos = _podGrp.TransformPoint(standOffset) - _worldShake * (1f - playerShakeFraction);
        _rb.position = pos;
        _playerT.position = pos;
    }

    void Update()
    {
        if (_active && Input.GetKeyDown(KeyCode.Escape)) _skip = true;

        // Eyelids: count wake clicks, ease toward the per-click open target,
        // and position the lids (with the idle woozy drift) until awake.
        if (_wakeArmed && !_wokeUp)
        {
            if (TutorialGate.PrimaryActionPressed())
            {
                _wakeClicks++;
                _opennessTarget = Mathf.Clamp01((float)_wakeClicks / clicksToWake);
            }
            float perClick = clicksToWake > 0 ? 1f / clicksToWake : 1f;
            float step = unfadePerClick > 0f ? perClick * UDT / unfadePerClick : 1f;
            _openness = Mathf.MoveTowards(_openness, _opennessTarget, step);
            ApplyEyelids(_openness);
        }

        // Grogginess BREATHES (worse -> better -> worse on a slow rhythm) and
        // its base level clears with PROXIMITY to the ground, not time — fully
        // sharp by touchdown. Held at full blur while the eyes are still shut.
        if (_grog != null && _wokeUp)
        {
            float baseLvl = grogMaxAfterWake * Mathf.Pow(Mathf.Clamp01(_altitude / Mathf.Max(1f, startAltitude)), 0.8f);
            float s = 0.5f * (1f - Mathf.Cos(Time.unscaledTime * grogBreatheSpeed));
            _grog.intensity = Mathf.Clamp01(baseLvl * (1f + s * (grogBreatheMax - 1f)));
        }

        // (Film ducking under HAL lines removed by request — the film plays at
        // constant volume; HAL talks over it.)
    }

    // ── Flight phases (Hermite: pins the sink rate at each seam, like the pod) ─
    static float Hermite(float d0, float d1, float v0, float v1, float duration, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * d0 + (u3 - 2f * u2 + u) * (duration * v0)
             + (-2f * u3 + 3f * u2) * d1 + (u3 - u2) * (duration * v1);
    }

    IEnumerator Approach()
    {
        float t = 0f;
        while (t < entryDuration && !_skip)
        {
            t += UDT;
            float u = Mathf.Clamp01(t / entryDuration);
            _altitude = Hermite(startAltitude, brakeAltitude, 0f, -entrySpeed, entryDuration, u);
            _shakeAmp = approachShake * u;   // entry rumble builds gently
            yield return null;
        }
    }

    // ── Sequential narration (runs in parallel with the descent) ─────────────
    IEnumerator RunBriefing()
    {
        yield return WaitUnscaled(2f);
        if (_skip) yield break;

        // Heartbeat runs under the WHOLE briefing: audible from the very
        // start (quiet)...
        StartHeartbeatLoop(heartbeatQuietVolume, 2f);
        yield return SpeakAndWait(_briefing[0]);   // "Stasis cycle complete..."
        if (_skip) yield break;

        // ...SURGING much louder halfway through the far-from-Earth line...
        StartCoroutine(RaiseHeartbeatAfter(earthLineSurgeDelay));
        yield return SpeakAndWait(_briefing[1]);   // "...twenty-five trillion miles from Earth."
        if (_skip) yield break;

        // Uneasy dead air: just the pounding.
        yield return WaitUnscaled(pauseAfterEarthLine);
        if (_skip) yield break;

        yield return SpeakAndWait(_briefing[2]);   // "Heart rate elevated..."
        if (_skip) yield break;

        yield return SpeakAndWait(_briefing[3]);   // reassurance, in full
        // ...settling once the reassurance has landed.
        StartCoroutine(FadeHeartbeatOutAfter(0f));
        if (_skip) yield break;
        Speak(_briefing[4]);                       // film lead-in
        ScheduleFilm();
        yield return WaitForHalIdle();
    }

    IEnumerator SpeakAndWait(string line)
    {
        Speak(line);
        yield return WaitForHalIdle();
        yield return WaitUnscaled(0.35f);   // breath, not a pause
    }

    IEnumerator WaitForHalIdle()
    {
        yield return null;   // let the HUD start processing the line
        yield return new WaitWhile(() => HALLineHUD.Instance != null && !HALLineHUD.Instance.IsIdle && !_skip);
    }

    // ── Anxious heartbeat ────────────────────────────────────────────────────
    void StartHeartbeatLoop(float targetVolume, float fadeSeconds)
    {
        if (heartbeatClip == null || _heartbeat != null) return;
        _heartbeat = gameObject.AddComponent<AudioSource>();
        _heartbeat.clip = heartbeatClip;
        _heartbeat.loop = true;
        _heartbeat.spatialBlend = 0f;
        _heartbeat.volume = 0f;
        _heartbeat.playOnAwake = false;
        _heartbeat.Play();
        StartCoroutine(FadeHeartbeatTo(targetVolume, fadeSeconds));
    }

    IEnumerator RaiseHeartbeatAfter(float delay)
    {
        yield return WaitUnscaled(delay);
        yield return FadeHeartbeatTo(heartbeatVolume, 1.5f);
    }

    IEnumerator FadeHeartbeatTo(float target, float seconds)
    {
        if (_heartbeat == null) yield break;
        float from = _heartbeat.volume, t = 0f;
        while (t < seconds && _heartbeat != null)
        {
            t += UDT;
            _heartbeat.volume = Mathf.Lerp(from, target, seconds > 0f ? t / seconds : 1f);
            yield return null;
        }
    }

    IEnumerator FadeHeartbeatOutAfter(float delay)
    {
        yield return WaitUnscaled(delay);
        yield return FadeHeartbeatTo(0f, 2.5f);
        if (_heartbeat != null) { _heartbeat.Stop(); Destroy(_heartbeat); _heartbeat = null; }
    }

    // The retro-burn: entrySpeed down to a crawl by hoverAltitude. Duration is
    // matched to the distance (2*d/(v0+v1)) so the Hermite stays monotonic —
    // the old single landing curve dove BELOW the ground mid-phase and the
    // clamp pinned the shuttle down while the shake played out the timer.
    IEnumerator Braking()
    {
        float t = 0f;
        while (t < brakeDuration && !_skip)
        {
            t += UDT;
            float u = Mathf.Clamp01(t / brakeDuration);
            _altitude = Mathf.Max(0f, Hermite(brakeAltitude, hoverAltitude, -entrySpeed, -finalSinkSpeed, brakeDuration, u));
            _shakeAmp = landingShake * Mathf.Sin(u * Mathf.PI * 0.5f);   // ramps up as the burn bites
            yield return null;
        }
    }

    // Last metres at a crawl, easing to 0 m/s AT the pads — a soft landing.
    IEnumerator FinalDescent()
    {
        float t = 0f;
        while (t < finalDuration && !_skip)
        {
            t += UDT;
            float u = Mathf.Clamp01(t / finalDuration);
            _altitude = Mathf.Max(0f, Hermite(hoverAltitude, 0f, -finalSinkSpeed, 0f, finalDuration, u));
            _shakeAmp = landingShake * (1f - u);                          // dies out with the sink rate
            yield return null;
        }
    }

    // Slides UP, moonbase-door style: the pivot IS the door's top edge, so
    // shrinking its Y scale retracts the leaf upward into its own top line —
    // nothing ever rises past the frame, no roof clipping, no occluder needed.
    IEnumerator OpenStasisDoor()
    {
        if (_doorPivot == null) yield break;
        if (!_skip && stasisDoorClip != null && _sfx != null)
            _sfx.PlayOneShot(stasisDoorClip, 0.9f);   // seal release + glass slide
        foreach (var c in _doorPivot.GetComponentsInChildren<Collider>(true))
            c.enabled = false;   // passable the moment it starts moving
        float t = 0f;
        while (t < doorOpenTime && !_skip)
        {
            t += UDT;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / doorOpenTime));
            ApplyStasisDoorState(1f - u);
            yield return null;
        }
        ApplyStasisDoorState(0f);
    }

    // s = leaf fraction (1 closed ... 0 fully retracted).
    void ApplyStasisDoorState(float s)
    {
        if (_doorPivot == null) return;
        var sc = _doorPivot.localScale;
        sc.y = Mathf.Max(0.001f, s);
        _doorPivot.localScale = sc;
        if (s <= 0.001f)
            foreach (var r in _doorPivot.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;   // hide the zero-height sliver
    }

    // ── Release: hand the player to normal play while the sequence keeps
    //    running (the film + locked exit door outlive the player's freedom).
    void ReleasePlayer()
    {
        if (_released) return;
        _released = true;
        _flying = false;
        _shakeAmp = 0f;

        transform.localPosition = _restLocalPos;   // guarantee the authored rest pose
        transform.localRotation = _restLocalRot;   // spin fully unwound

        if (_pc != null && _rb != null)
        {
            _rb.isKinematic = _wasKinematic;
            // CRITICAL, measured with the release diagnostic: the planet orbits
            // ~0.7m per frame, so the player's transform (last pinned in the
            // PREVIOUS frame's LateUpdate) is one frame BEHIND the shuttle at
            // teardown time — seating from it embeds the player in the wall
            // behind the chamber and PhysX depenetration blasts them out of the
            // ship. Seat from the shuttle's LIVE pose at this instant instead,
            // then commit into PhysX (autoSyncTransforms is off) — the
            // PlayerPickup "drop teleports super far" recipe, plus the lag fix.
            Vector3 seatPos = _podGrp != null ? _podGrp.TransformPoint(standOffset) : _playerT.position;
            _playerT.position = seatPos;
            _rb.position = seatPos;
            _rb.rotation = _playerT.rotation;
            _rb.angularVelocity = Vector3.zero;
            var body = GetComponentInParent<CelestialBody>();
            _pc.SetVelocity(body != null ? body.velocity : Vector3.zero);   // inherit the planet's orbit
            Physics.SyncTransforms();
            // Reset the camera's rotation-interpolation buffer so it doesn't
            // slerp from a pre-release snapshot (one-frame judder otherwise).
            if (CameraEffectsManager.Instance != null && CameraEffectsManager.Instance.TransformFX != null)
                CameraEffectsManager.Instance.TransformFX.SnapToCurrentPlayer();

            // Release diagnostics (leave off unless the handoff regresses):
            // records shuttle-local position + velocity + contacts post-release.
            if (ReleaseDiagnostics)
            {
                var diag = _playerT.gameObject.AddComponent<ShuttleReleaseDiag>();
                diag.Init(transform, _rb);
            }
        }
        // Only clear the override if it's still pointing at US — the normal
        // path hands it to the blend proxy first (see BlendUpOverrideOut),
        // which owns its own release.
        if (PlayerController.UpOverrideTransform == transform)
            PlayerController.UpOverrideTransform = null;
        TutorialGate.UnlockAll();
        if (_pc != null) _pc.introMoveScale = 1f;
        IntroSequenceController.SuppressGroggyCameraFx = false;
        FallDamage.Suppressed = false;
        SpeedLinesOverlay.Suppressed = false;
        if (_grog != null) { DestroyImmediate(_grog); _grog = null; }
        // NOTE: HALCommentator stays suppressed until Teardown — no ambient
        // HAL chatter over the orientation film.
    }

    // ── Final teardown (idempotent; also the abort path via OnDisable/Destroy) ─
    void Teardown()
    {
        if (!_active) return;
        _active = false;
        ReleasePlayer();
        HALCommentator.SuppressAutonomous = false;
        StopFilm();
        if (_canvas != null) Destroy(_canvas.gameObject);
        StopLandingCam();
        if (_thrustFX != null) { _thrustFX.StopImmediate(); Destroy(_thrustFX); _thrustFX = null; }
        if (_ambient != null) { _ambient.Stop(); Destroy(_ambient); }
        if (_thruster != null) { _thruster.Stop(); Destroy(_thruster.gameObject); }
        if (_wind != null) { _wind.Stop(); Destroy(_wind); _wind = null; }
        if (_heartbeat != null) { _heartbeat.Stop(); Destroy(_heartbeat); _heartbeat = null; }
        if (_sfx != null) Destroy(_sfx, 3f);   // let the touchdown thump ring out
    }

    // ── Orientation film ─────────────────────────────────────────────────────
    void ScheduleFilm()
    {
        if (_filmScheduled) return;
        _filmScheduled = true;
        StartCoroutine(StartFilmAfter(filmDelayAfterLeadIn));
    }

    IEnumerator StartFilmAfter(float delay)
    {
        yield return WaitUnscaled(delay);
        StopLandingCam();   // the film owns the screen from here; landing feed is done for good
        if (_skip || orientationFilm == null || _screenRenderer == null)
        {
            // Diagnostic breadcrumb: if the film ever silently fails to start
            // again, THIS tells us which reference was missing.
            if (!_skip)
                Debug.LogWarning("[ShuttleArrival] Orientation film NOT started: clip="
                    + (orientationFilm != null ? orientationFilm.name : "NULL")
                    + " screenRenderer=" + (_screenRenderer != null ? "ok" : "NULL"));
            _filmStarted = true; _filmEnded = true;   // nothing to wait on
            yield break;
        }

        _videoRT = new RenderTexture(1024, 576, 0);
        var go = _screenRenderer.gameObject;
        _video = go.AddComponent<UnityEngine.Video.VideoPlayer>();
        _video.playOnAwake = false;
        _video.clip = orientationFilm;
        _video.isLooping = false;
        _video.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
        _video.targetTexture = _videoRT;
        _video.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.AudioSource;
        _video.controlledAudioTrackCount = 1;
        _videoAudio = go.AddComponent<AudioSource>();
        _videoAudio.playOnAwake = false;
        _videoAudio.spatialBlend = 0.6f;               // from the TV, but clear anywhere in the cabin
        _videoAudio.rolloffMode = AudioRolloffMode.Linear;
        _videoAudio.maxDistance = 18f;
        _videoAudio.volume = filmVolume;
        _video.EnableAudioTrack(0, true);
        _video.SetTargetAudioSource(0, _videoAudio);
        _video.loopPointReached += _ => _filmEnded = true;

        // Swap the green glow for the film; restored by StopFilm.
        _screenOriginalMat = _screenRenderer.sharedMaterial;
        _filmMat = new Material(Shader.Find("Unlit/Texture"));
        _filmMat.mainTexture = _videoRT;
        _screenRenderer.material = _filmMat;

        _video.Play();
        _filmStarted = true;
    }

    void StopFilm()
    {
        _filmEnded = true;
        if (_video != null) { _video.Stop(); Destroy(_video); _video = null; }
        if (_videoAudio != null) { Destroy(_videoAudio); _videoAudio = null; }
        if (_screenRenderer != null && _screenOriginalMat != null)
            _screenRenderer.sharedMaterial = _screenOriginalMat;   // green glow returns
        if (_filmMat != null) { Destroy(_filmMat); _filmMat = null; }
        if (_videoRT != null) { _videoRT.Release(); Destroy(_videoRT); _videoRT = null; }
    }

    void OnDisable() { Teardown(); }
    void OnDestroy() { Teardown(); }

    // ── Audio / UI helpers ───────────────────────────────────────────────────
    void StartThruster()
    {
        if (thrusterClip == null) return;
        var go = new GameObject("ShuttleThruster");
        go.transform.SetParent(transform, false);
        _thruster = go.AddComponent<AudioSource>();
        _thruster.clip = thrusterClip;
        _thruster.loop = true;
        _thruster.spatialBlend = 0f;
        _thruster.volume = thrusterVolume;
        _thruster.playOnAwake = false;
        go.AddComponent<AudioLowPassFilter>().cutoffFrequency = thrusterLowPassHz;
        _thruster.Play();
    }

    void StopThruster(float fadeSeconds)
    {
        if (_thruster == null) return;
        if (fadeSeconds <= 0f) { _thruster.Stop(); Destroy(_thruster.gameObject); _thruster = null; }
        else StartCoroutine(FadeThrusterOut(fadeSeconds));
    }

    IEnumerator FadeThrusterOut(float seconds)
    {
        var src = _thruster; _thruster = null;
        if (src == null) yield break;
        float from = src.volume, t = 0f;
        while (t < seconds && src != null)
        {
            t += UDT;
            src.volume = Mathf.Lerp(from, 0f, t / seconds);
            yield return null;
        }
        if (src != null) { src.Stop(); Destroy(src.gameObject); }
    }

    AudioSource AddLoop(AudioClip clip, float vol)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip; src.loop = true; src.spatialBlend = 0f;
        src.volume = vol; src.playOnAwake = false;
        if (clip != null) src.Play();
        return src;
    }

    void BuildCanvas()
    {
        var go = new GameObject("ShuttleArrivalOverlay");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32761;
        // Reference resolution MUST be set: the scaler default is 800x600,
        // which blew the cold-open text up ~2.4x (the first question clipped
        // off both edges of the screen). 1920x1080 matches HALLineHUD.
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Fade (for skip blinks) — transparent at rest; the wake-up's veil +
        // eyelids own the initial blackout.
        _fade = MakeFullScreenImage(go.transform, "Fade", null);
        _fade.color = new Color(0f, 0f, 0f, 0f);

        _veil = MakeFullScreenImage(go.transform, "Veil", null);
        _veil.color = new Color(0f, 0f, 0f, 1f);         // eyes shut: full blackout
        _topLid = MakeFullScreenImage(go.transform, "TopLid", MakeLidSprite(true));
        _botLid = MakeFullScreenImage(go.transform, "BottomLid", MakeLidSprite(false));
        _topLid.color = Color.black;
        _botLid.color = Color.black;

        var promptGO = new GameObject("PressPrompt");
        promptGO.transform.SetParent(go.transform, false);
        _prompt = promptGO.AddComponent<TextMeshProUGUI>();
        _prompt.text = "Press " + PromptGlyphs.PrimaryAction;
        _prompt.alignment = TextAlignmentOptions.Center;
        _prompt.fontSize = 30;
        _prompt.color = new Color(1f, 1f, 1f, 0.85f);
        var prt = _prompt.rectTransform;
        prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(0f, -150f);   // below the cold-open line
        prt.sizeDelta = new Vector2(800f, 120f);
        _prompt.gameObject.SetActive(false);

        // Cold-open line: the recruiter's questions, centered on the blackout.
        var coldGO = new GameObject("ColdOpenLine");
        coldGO.transform.SetParent(go.transform, false);
        _coldOpenText = coldGO.AddComponent<TextMeshProUGUI>();
        _coldOpenText.text = "";
        _coldOpenText.alignment = TextAlignmentOptions.Center;
        _coldOpenText.fontSize = 34;
        _coldOpenText.enableWordWrapping = true;
        _coldOpenText.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        _coldOpenText.alpha = 0f;
        var crt = _coldOpenText.rectTransform;
        crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(1400f, 300f);
        _coldOpenText.gameObject.SetActive(false);

        _openness = _opennessTarget = 0f;
        ApplyEyelids(0f);
    }

    Image MakeFullScreenImage(Transform parent, string name, Sprite sprite)
    {
        var imgGO = new GameObject(name);
        imgGO.transform.SetParent(parent, false);
        var img = imgGO.AddComponent<Image>();
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Simple; }
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += UDT;
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, seconds > 0f ? t / seconds : 1f));
            yield return null;
        }
        if (_fade != null) _fade.color = new Color(0f, 0f, 0f, to);
    }

    void Speak(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (HALCommentator.Instance != null) HALCommentator.Instance.VolunteerExternal(line, true);
        else if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
    }
}

// TEMP diagnostic (remove once the release is verified): logs the player's
// shuttle-local position, speed, and collision contacts every FixedUpdate for
// a few seconds after the stasis release, then writes a report file.
public class ShuttleReleaseDiag : MonoBehaviour
{
    Transform _shuttle;
    Rigidbody _rb;
    CelestialBody _body;
    System.Text.StringBuilder _sb = new System.Text.StringBuilder();
    float _t;
    int _contacts;

    public void Init(Transform shuttle, Rigidbody rb)
    {
        _shuttle = shuttle;
        _rb = rb;
        _body = shuttle != null ? shuttle.GetComponentInParent<CelestialBody>() : null;
        _sb.AppendLine("t=0 RELEASE localPos=" + LocalPos() + " vel=" + (_rb != null ? _rb.velocity.magnitude.ToString("F1") : "-")
            + " relVel=" + RelVel() + " kinematic=" + (_rb != null && _rb.isKinematic)
            + " interp=" + (_rb != null ? _rb.interpolation.ToString() : "-")
            + " ccd=" + (_rb != null ? _rb.collisionDetectionMode.ToString() : "-"));
    }

    string LocalPos() { return _shuttle != null ? _shuttle.InverseTransformPoint(transform.position).ToString("F2") : "?"; }
    string RelVel()
    {
        if (_rb == null) return "-";
        Vector3 v = _rb.velocity - (_body != null ? _body.velocity : Vector3.zero);
        return v.magnitude.ToString("F2");
    }

    void FixedUpdate()
    {
        _t += Time.fixedDeltaTime;
        _sb.AppendLine("t=" + _t.ToString("F2") + " localPos=" + LocalPos() + " relVel=" + RelVel());
        if (_t >= 4f) Finish();
    }

    void OnCollisionEnter(Collision c) { Contact("ENTER", c); }
    void OnCollisionStay(Collision c) { if (_contacts < 40) Contact("stay", c); }

    void Contact(string kind, Collision c)
    {
        _contacts++;
        if (_contacts > 60) return;
        var p = c.contactCount > 0 ? c.GetContact(0) : default;
        _sb.AppendLine("  contact(" + kind + ") " + c.collider.name + " sep=" + p.separation.ToString("F3")
            + " normal=" + p.normal.ToString("F2") + " impulse=" + c.impulse.magnitude.ToString("F1"));
    }

    void Finish()
    {
        try
        {
            System.IO.File.WriteAllText(@"C:\Users\Sammc\AppData\Local\Temp\claude\C--Users-Sammc-Desktop-1ass-1aughhh1\5a149854-1a50-4389-8a70-027cf0eae613\scratchpad\release_diag.txt", _sb.ToString());
        }
        catch { }
        Destroy(this);
    }

    void OnDestroy()
    {
        if (_t < 4f) Finish();
    }
}
