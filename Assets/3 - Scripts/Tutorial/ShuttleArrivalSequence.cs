using System.Collections;
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
    [Header("Descent profile (metres above the authored rest pose)")]
    [SerializeField] float startAltitude = 900f;    // where the fade-in reveals the shuttle
    [SerializeField] float lowAltitude   = 60f;     // handoff to the retro-thruster landing
    [SerializeField] float seamSpeed     = 25f;     // sink rate (m/s) at the approach -> landing seam

    [Header("Timing (seconds)")]
    [SerializeField] float fadeInTime       = 2f;
    [SerializeField] float approachDuration = 55f;  // covers the briefing schedule (last line at 49s)
    [SerializeField] float landingDuration  = 10f;  // retro-burn: lowAltitude -> touchdown, easing to 0 m/s
    [SerializeField] float touchdownHold    = 1.2f; // beat on the ground before the door opens
    [SerializeField] float doorOpenTime     = 1.8f;
    [SerializeField] float doorOpenAngle    = 115f; // stasis door flips up-and-out around the top-front pivot

    [Header("Player placement (StasisPod-group local space)")]
    [SerializeField] Vector3 standOffset = new Vector3(0f, 1.25f, -0.05f);

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

    // HAL briefing — identical lines + schedule to the pod descent. Text MUST
    // byte-match HALVoiceManifest.Lines or a line plays silent, so they live in
    // code. (See PodArrivalSequence for the original.)
    static readonly string[] _briefing = {
        "Stasis cycle complete. Welcome back, astronaut.",
        "You have been in transit for three years, and are twenty-five trillion miles from Earth.",
        "Memory loss is expected after stasis of this length. It will not affect the mission.",
        "Heart rate elevated. Vitals irregular. Do not worry, memories will return with time.",
        "It is normal for those emerging from stasis to have difficulty recalibrating. Remember — when the mission is complete, you will be returned home.",
        "Approaching Humble Abode. Begin atmospheric entry.",
    };
    static readonly float[] _briefingTimes = { 2f, 12f, 22f, 32f, 42f, 49f };
    const string ReverseThrusterLine = "Engaging reverse thrusters.";

    // ── Runtime ──────────────────────────────────────────────────────────────
    PlayerController _pc;
    Transform _playerT;
    Rigidbody _rb;
    Transform _podGrp, _doorPivot;
    Vector3 _restLocalPos;
    Vector3 _localUp;
    Quaternion _doorRestRot;
    float _altitude;
    float _shakeAmp;
    bool _flying;
    bool _active;
    bool _skip;
    bool _wasKinematic;
    int _briefingIndex;
    Canvas _canvas;
    Image _fade;
    AudioSource _ambient, _thruster, _sfx;
    GrogginessImageEffect _grog;

    public bool IsActive => _active;

    // ── Entry point (called by IntroSequenceController on fresh New Game) ────
    public IEnumerator Play()
    {
        _skip = false;
        if (!Setup()) yield break;

        yield return Fade(1f, 0f, fadeInTime);
        yield return Approach();
        yield return FlushRemainingBriefing();

        // Retro-burn landing: HAL calls it, the muffled burn spins up, and the
        // shuttle eases from the seam sink rate to a dead-soft touchdown.
        if (!_skip)
        {
            Speak(ReverseThrusterLine);
            StartThruster();
            yield return Landing();
        }

        if (_skip)
        {
            // Skipped: blink to black, snap everything to the landed state, reveal.
            yield return Fade(0f, 1f, 0.15f);
            _altitude = 0f;
            transform.localPosition = _restLocalPos;
            SyncPlayerToPod();
            StopThruster(0f);
            yield return Fade(1f, 0f, 0.4f);
        }
        else
        {
            _altitude = 0f;
            transform.localPosition = _restLocalPos;   // land EXACTLY on the authored pose
            _shakeAmp = 0f;
            if (_sfx != null && touchdownClip != null) _sfx.PlayOneShot(touchdownClip, touchdownVolume);
            StopThruster(1.5f);
            yield return new WaitForSecondsRealtime(touchdownHold);
        }

        yield return OpenStasisDoor();
        Teardown();   // release the player into normal play (door stays open)
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
        }
        if (_podGrp == null) return false;
        if (_doorPivot != null) _doorRestRot = _doorPivot.localRotation;

        _restLocalPos = transform.localPosition;
        _localUp = _restLocalPos.sqrMagnitude > 0.001f ? _restLocalPos.normalized : Vector3.up;
        _altitude = startAltitude;
        _briefingIndex = 0;

        _wasKinematic = _rb.isKinematic;
        _rb.isKinematic = true;                                // no N-body gravity in the chamber
        TutorialGate.LockAll();
        TutorialGate.Unlock(TutorialAbility.MouseLook);        // free-look through the chamber glass
        IntroSequenceController.SuppressGroggyCameraFx = true;
        HALCommentator.SuppressAutonomous = true;
        FallDamage.Suppressed = true;
        HudVisibility.SetForceHidden(true);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _flying = true;
        _active = true;
        transform.localPosition = _restLocalPos + _localUp * _altitude;
        SyncPlayerToPod();
        _pc.ForceLookAt(_podGrp.TransformPoint(standOffset + Vector3.forward * 6f));  // face the chamber door / cabin

        BuildCanvas();
        _ambient = AddLoop(ambientHumClip, ambientVolume);
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.spatialBlend = 0f; _sfx.playOnAwake = false;

        if (grogginessMaterial != null && _pc.Camera != null)
        {
            _grog = _pc.Camera.gameObject.AddComponent<GrogginessImageEffect>();
            _grog.material = grogginessMaterial;
            _grog.intensity = 1f;                              // stasis wake: fully blurry, clears over the descent
        }
        return true;
    }

    // Drive shuttle + player planet-relative, after the origin shift (order 50).
    void LateUpdate()
    {
        if (!_flying) return;
        transform.localPosition = _restLocalPos + _localUp * _altitude;
        SyncPlayerToPod();
    }

    void SyncPlayerToPod()
    {
        if (_rb == null || _podGrp == null) return;

        // Keep the player upright RELATIVE TO THE SHUTTLE. The kinematic freeze
        // also freezes PlayerController's own gravity-up alignment (its
        // FixedUpdate early-returns), so without this the body keeps its spawn
        // orientation and clips out of the chamber as the shuttle moves. Same
        // FromToRotation pattern the controller uses for gravity-up; mouse-look
        // yaw/pitch ride on top untouched.
        Quaternion upFix = Quaternion.FromToRotation(_playerT.up, transform.up);
        _playerT.rotation = upFix * _playerT.rotation;
        _rb.rotation = _playerT.rotation;

        Vector3 pos = _podGrp.TransformPoint(standOffset);
        if (_shakeAmp > 0f)
        {
            float ts = Time.unscaledTime * 30f;
            pos += new Vector3(Mathf.PerlinNoise(ts, 0f) - 0.5f,
                               Mathf.PerlinNoise(0f, ts) - 0.5f,
                               Mathf.PerlinNoise(ts, ts) - 0.5f) * (2f * _shakeAmp);
        }
        _rb.position = pos;
        _playerT.position = pos;
    }

    void Update()
    {
        if (_active && Input.GetKeyDown(KeyCode.Escape)) _skip = true;
        if (_grog != null)
            _grog.intensity = Mathf.MoveTowards(_grog.intensity, 0f, grogRecoverRate * Time.unscaledDeltaTime);
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
        while (t < approachDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / approachDuration);
            _altitude = Hermite(startAltitude, lowAltitude, 0f, -seamSpeed, approachDuration, u);
            _shakeAmp = approachShake * u;   // entry rumble builds gently
            while (_briefingIndex < _briefing.Length && t >= _briefingTimes[_briefingIndex])
            {
                Speak(_briefing[_briefingIndex]);
                _briefingIndex++;
            }
            yield return null;
        }
    }

    IEnumerator FlushRemainingBriefing()
    {
        while (_briefingIndex < _briefing.Length && !_skip)
        {
            Speak(_briefing[_briefingIndex]);
            _briefingIndex++;
            yield return new WaitForSecondsRealtime(4f);
        }
    }

    IEnumerator Landing()
    {
        float t = 0f;
        while (t < landingDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / landingDuration);
            _altitude = Mathf.Max(0f, Hermite(lowAltitude, 0f, -seamSpeed, 0f, landingDuration, u));
            // Burn hardest mid-descent, dying to nothing right at the pads.
            _shakeAmp = landingShake * Mathf.Sin(u * Mathf.PI);
            yield return null;
        }
    }

    IEnumerator OpenStasisDoor()
    {
        if (_doorPivot == null) yield break;
        float t = 0f;
        while (t < doorOpenTime && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / doorOpenTime));
            _doorPivot.localRotation = _doorRestRot * Quaternion.AngleAxis(-doorOpenAngle * u, Vector3.right);
            yield return null;
        }
        _doorPivot.localRotation = _doorRestRot * Quaternion.AngleAxis(-doorOpenAngle, Vector3.right);
    }

    // ── Teardown: hand the player to normal play (idempotent) ────────────────
    void Teardown()
    {
        if (!_active) return;
        _active = false;
        _flying = false;
        _shakeAmp = 0f;

        transform.localPosition = _restLocalPos;   // guarantee the authored rest pose

        if (_pc != null && _rb != null)
        {
            _rb.isKinematic = _wasKinematic;
            var body = GetComponentInParent<CelestialBody>();
            _pc.SetVelocity(body != null ? body.velocity : Vector3.zero);   // inherit the planet's orbit
        }
        TutorialGate.UnlockAll();
        if (_pc != null) _pc.introMoveScale = 1f;
        IntroSequenceController.SuppressGroggyCameraFx = false;
        HALCommentator.SuppressAutonomous = false;
        FallDamage.Suppressed = false;
        HudVisibility.SetForceHidden(false);

        if (_grog != null) { DestroyImmediate(_grog); _grog = null; }
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_ambient != null) { _ambient.Stop(); Destroy(_ambient); }
        if (_thruster != null) { _thruster.Stop(); Destroy(_thruster.gameObject); }
        if (_sfx != null) Destroy(_sfx, 3f);   // let the touchdown thump ring out
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
            t += Time.unscaledDeltaTime;
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
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var imgGO = new GameObject("Fade");
        imgGO.transform.SetParent(go.transform, false);
        _fade = imgGO.AddComponent<Image>();
        _fade.raycastTarget = false;
        _fade.color = new Color(0f, 0f, 0f, 1f);   // start black
        var rt = _fade.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
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
