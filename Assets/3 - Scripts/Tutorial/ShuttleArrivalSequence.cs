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
    [SerializeField] float entryDuration = 52f;     // covers the briefing schedule (last line at 49s)
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

    // Test hook: jumps straight to the landed/skip path (same release code).
    public void SkipNow() { _skip = true; }

    // Flip on to log a per-FixedUpdate release report to the scratchpad —
    // this is how the one-frame orbital-lag seating bug was caught.
    const bool ReleaseDiagnostics = false;

    // ── Entry point (called by IntroSequenceController on fresh New Game) ────
    public IEnumerator Play()
    {
        _skip = false;
        if (!Setup()) yield break;

        yield return Fade(1f, 0f, fadeInTime);
        yield return Approach();
        yield return FlushRemainingBriefing();

        // Atmosphere hit: HAL calls the burn, the shuttle brakes HARD from the
        // entry fall to a crawl, then sinks the last few metres to a dead-soft
        // 0 m/s touchdown.
        if (!_skip)
        {
            Speak(ReverseThrusterLine);
            StartThruster();
            yield return Braking();
            yield return FinalDescent();
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

        // Hand the up-override to a blend proxy BEFORE teardown: clearing it
        // outright makes the controller snap from shuttle-up to gravity-up in
        // one FixedUpdate — a visible hitch right as the door finishes. The
        // proxy eases between the two over a second and a half instead.
        StartCoroutine(BlendUpOverrideOut(1.5f));
        yield return OpenStasisDoor();
        Teardown();   // release the player into normal play (door stays open)
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
        }
        if (_podGrp == null) return false;
        if (_doorPivot != null) _doorRestRot = _doorPivot.localRotation;

        _restLocalPos = transform.localPosition;
        _localUp = _restLocalPos.sqrMagnitude > 0.001f ? _restLocalPos.normalized : Vector3.up;
        _altitude = startAltitude;
        _briefingIndex = 0;

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
        while (t < entryDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / entryDuration);
            _altitude = Hermite(startAltitude, brakeAltitude, 0f, -entrySpeed, entryDuration, u);
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

    // The retro-burn: entrySpeed down to a crawl by hoverAltitude. Duration is
    // matched to the distance (2*d/(v0+v1)) so the Hermite stays monotonic —
    // the old single landing curve dove BELOW the ground mid-phase and the
    // clamp pinned the shuttle down while the shake played out the timer.
    IEnumerator Braking()
    {
        float t = 0f;
        while (t < brakeDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
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
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / finalDuration);
            _altitude = Mathf.Max(0f, Hermite(hoverAltitude, 0f, -finalSinkSpeed, 0f, finalDuration, u));
            _shakeAmp = landingShake * (1f - u);                          // dies out with the sink rate
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
        HALCommentator.SuppressAutonomous = false;
        FallDamage.Suppressed = false;
        SpeedLinesOverlay.Suppressed = false;

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
