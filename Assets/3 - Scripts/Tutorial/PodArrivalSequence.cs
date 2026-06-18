using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Stasis-pod arrival cinematic. Plays on a fresh New Game BEFORE the cabin
// wake-up (see IntroSequenceController). Scene-placed (NOT an auto-singleton).
//
// Approach: we move the PLAYER (the floating-origin anchor), never a detached
// camera. The player is frozen kinematic (no N-body gravity), movement is locked
// but free-look is left on, and each LateUpdate we set the player's position
// RELATIVE to the live planet position so it survives the planet's orbit and the
// floating-origin rebases. The camera rides along as the player's normal child
// (so CameraTransformFX + the atmosphere effects keep working). On impact we
// teleport the player to the cabin spawn and hand off to the wake-up.
//
// Design: docs/superpowers/specs/2026-06-18-stasis-pod-arrival-intro-design.md
public class PodArrivalSequence : MonoBehaviour
{
    // ── Tunables (serialized; appended at END per convention) ───────────────
    [Header("Target")]
    [SerializeField] string targetBodyName = "Humble Abode";

    [Header("Approach (distances are from the planet CENTER)")]
    [SerializeField] float startDistance   = 4000f;  // where the pod begins (far enough to take in the system)
    [SerializeField] Vector3 approachOffset = new Vector3(0.3f, 0.6f, -1f); // direction from the planet the pod approaches from
    [SerializeField] float arrivalDistance = 600f;   // distance at the end of the calm approach (planet looms large)
    [SerializeField] float impactDistance  = 210f;   // distance at impact (just at the surface; planet radius ~200)

    [Header("Timing (seconds)")]
    [SerializeField] float fadeInTime   = 2f;        // black -> scene reveal
    [SerializeField] float approachDuration  = 20f;  // calm drift
    [SerializeField] float countdownDuration = 10f;  // proximity-alert countdown
    [SerializeField] float impactFadeTime = 0.12f;   // cut to black on impact
    [SerializeField] float skipFadeTime   = 0.4f;    // fade on skip

    [Header("Shake / impact")]
    [SerializeField] float shakeMaxAmplitude = 1.2f; // peak camera shake at impact (world units)
    [SerializeField] AnimationCurve shakeRamp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio (optional; null = silent)")]
    [SerializeField] AudioClip ambientHumClip;
    [SerializeField] AudioClip alarmBeepClip;
    [SerializeField] AudioClip rumbleClip;
    [SerializeField] AudioClip impactBoomClip;
    [SerializeField, Range(0f, 1f)] float ambientVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] float alarmVolume   = 0.7f;
    [SerializeField, Range(0f, 1f)] float rumbleVolume  = 0.8f;
    [SerializeField, Range(0f, 1f)] float impactVolume  = 1f;

    [Header("HAL subtitles during approach")]
    [SerializeField] string[] approachLines = {
        "Stasis cycle complete. Welcome back, astronaut.",
        "Approaching Humble Abode. Begin atmospheric entry."
    };
    [SerializeField] float[] approachLineTimes = { 2f, 11f };

    [Header("Countdown")]
    [SerializeField] int countdownStart = 10;

    // Speeds (m/s, inward) that pin the velocity at the two seams so the flight is
    // velocity-continuous — no dead-stop-then-restart. The approach starts at rest
    // and eases up to seamSpeed by the time reentry begins; reentry then brakes
    // BELOW seamSpeed (atmospheric drag) before gravity wins and it accelerates to
    // impactSpeed — a real crash, not a gentle ease-in. Keep impactSpeed > seamSpeed.
    [Header("Flight speeds")]
    [SerializeField] float seamSpeed   = 80f;   // inward speed at the approach -> reentry handoff
    [SerializeField] float impactSpeed = 130f;  // inward speed at the moment of impact

    [Header("Post-crash")]
    [SerializeField] float postCrashBlackHold = 3f;  // seconds the screen stays black after impact before the cabin teleport + wake-up

    [Header("Pod visual")]
    [SerializeField] GameObject podPrefab;   // dark-industrial stasis pod (StasisPod.prefab); null = no pod

    [Header("Grogginess (stasis wake — same blur + double-vision as the cabin wake-up)")]
    [SerializeField] Material grogginessMaterial;        // the Hidden/Grogginess material (share IntroSequenceController's)
    [SerializeField] float grogRecoverRate  = 0.07f;     // intensity units/sec easing from full blur down to the floor
    [SerializeField] float grogFloor        = 0.45f;     // woozy level held through the descent (never fully clears)
    [SerializeField] float breatheGrogMax   = 2f;        // peak multiplier of the base woozy level (the double-vision pulse)
    [SerializeField] float breatheGrogSpeed = 1.1f;      // breathing rhythm (rad/sec) — worse -> better -> worse

    [Header("Heartbeat (rising panic as impact nears)")]
    [SerializeField] AudioClip heartbeatClip;            // the regular heartbeat loop (same clip the cabin uses)
    [SerializeField, Range(0f, 1f)] float heartbeatApproachVolume = 0.75f; // beat level during the drift (clearly audible)
    [SerializeField, Range(0f, 1f)] float heartbeatImpactVolume   = 1f;    // beat level at impact (loud)
    [SerializeField] float heartbeatFadeIn      = 3.5f;  // seconds to fade the beat in at wake
    [SerializeField] float heartbeatStartPitch  = 1f;    // beat speed at wake (calm)
    [SerializeField] float heartbeatImpactPitch = 1.55f; // beat speed at impact (faster = racing)

    // ── Runtime ─────────────────────────────────────────────────────────────
    CelestialBody _target;
    PlayerController _pc;
    Transform _player;
    Rigidbody _rb;
    Camera _cam;
    GameObject _podInstance;      // spawned StasisPod, positioned/oriented each LateUpdate
    GrogginessImageEffect _grog;  // stasis-wake blur on the camera; eased + breathing
    float _grogIntensity = 1f;    // 1 = full blur at wake, eased down to grogFloor
    float _grogTarget;
    bool _wasKinematic;
    Vector3 _returnPos;          // fallback cabin pose captured before we move the player
    Quaternion _returnRot;

    Canvas _canvas;
    Image _fade;
    TextMeshProUGUI _console;

    AudioSource _ambient, _rumble, _sfx;
    AudioSource _heart;   // heartbeat loop — volume + pitch ramp up toward impact (panic)

    Vector3 _dir;                // unit direction from the planet to the pod
    float _curDistance;          // current distance from the planet centre (driven by the phase coroutines)
    bool _flying;                // while true, LateUpdate drives the player position
    float _shakeAmp;
    bool _skip;
    bool _active;                // true once set up; guards teardown idempotency

    // ── Entry point ──────────────────────────────────────────────────────────
    public IEnumerator Play()
    {
        _skip = false;
        if (!Locate()) yield break;     // no target -> fall straight through to the wake-up
        if (!Setup()) yield break;      // couldn't acquire the player -> bail cleanly

        yield return Fade(1f, 0f, fadeInTime);   // reveal the scene
        yield return Approach();                 // calm drift toward the planet
        if (!_skip)
        {
            yield return Countdown();                                  // countdown -> impact boom -> cut to black
            yield return new WaitForSecondsRealtime(postCrashBlackHold); // hold black while the crash rings out (player still in the pod)
        }
        if (_skip)  yield return Fade(0f, 1f, skipFadeTime);

        Teardown();   // teleport to the cabin under the black screen, then the wake-up takes over
    }

    // ── Locate / setup ───────────────────────────────────────────────────────
    bool Locate()
    {
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == targetBodyName) { _target = b; break; }
        return _target != null;
    }

    bool Setup()
    {
        _pc = FindObjectOfType<PlayerController>();
        if (_pc == null) return false;
        _player = _pc.transform;
        _rb  = _pc.Rigidbody;
        _cam = _pc.Camera;
        if (_rb == null || _cam == null) return false;

        _returnPos = _player.position;   // cabin pose, used only as a fallback on teardown
        _returnRot = _player.rotation;

        _wasKinematic = _rb.isKinematic;
        _rb.isKinematic = true;                                   // freeze N-body gravity while in the pod
        TutorialGate.LockAll();                                   // no movement
        TutorialGate.Unlock(TutorialAbility.MouseLook);           // but free-look out the window
        IntroSequenceController.SuppressGroggyCameraFx = true;    // mute strafe tilt / sprint FOV
        HALCommentator.SuppressAutonomous = true;                 // no atmosphere/arrival narration while we teleport the player
        FallDamage.Suppressed = true;                             // descent speed must not deal damage at the cabin
        HudVisibility.SetForceHidden(true);                       // hide compass/vitals/jetpack/wallet for a clean cinematic (countdown + HAL lines stay)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _dir = approachOffset.sqrMagnitude > 0.0001f ? approachOffset.normalized : Vector3.up;
        _curDistance = startDistance;
        Vector3 startPos = _target.Position + _dir * _curDistance;
        _rb.position = startPos;
        _player.position = startPos;
        _pc.ForceLookAt(_target.Position);                       // start looking out the window at the planet

        BuildCanvas();
        StartAudio();
        if (podPrefab != null) _podInstance = Instantiate(podPrefab);

        // Stasis-wake grogginess: come to very blurry, ease down to a woozy floor,
        // then breathe (worse/better) — the same effect the cabin wake-up uses.
        if (grogginessMaterial != null && _cam != null)
        {
            _grog = _cam.gameObject.AddComponent<GrogginessImageEffect>();
            _grog.material = grogginessMaterial;
            _grogIntensity = 1f;          // fully blurry at wake
            _grogTarget    = grogFloor;   // ease toward the woozy floor; it breathes there
            _grog.intensity = 1f;
        }

        _flying = true;
        _active = true;
        return true;
    }

    // Drive the player position relative to the LIVE planet position every frame,
    // BEFORE CameraTransformFX (execution order 100) places the camera. We only set
    // POSITION — look (rotation) stays owned by the player's own input pipeline.
    void LateUpdate()
    {
        if (!_flying || _target == null || _rb == null) return;

        Vector3 pos = _target.Position + _dir * _curDistance;
        if (_shakeAmp > 0f)
        {
            float ts = Time.unscaledTime * 30f;
            Vector3 n = new Vector3(Mathf.PerlinNoise(ts, 0f) - 0.5f,
                                    Mathf.PerlinNoise(0f, ts) - 0.5f,
                                    Mathf.PerlinNoise(ts, ts) - 0.5f) * (2f * _shakeAmp);
            pos += n;
        }
        _rb.position = pos;
        _player.position = pos;

        // Centre the pod on the camera (the eye) and orient it off the CONSTANT
        // descent direction _dir: +Y (ceiling/space window) points away from the
        // planet, so the floor window faces it. _dir never changes during flight,
        // so the pod is rock-stable — unlike the old per-frame LookRotation that
        // spun wildly once we were metres from the planet. Shake stays positional.
        if (_podInstance != null && _cam != null)
        {
            _podInstance.transform.position = _cam.transform.position;
            _podInstance.transform.rotation = Quaternion.FromToRotation(Vector3.up, _dir);
        }
    }

    // ── Flight phases ─────────────────────────────────────────────────────────
    // Distance is driven by a cubic Hermite per phase so we can pin the inward
    // SPEED at each end. The approach ends at -seamSpeed and the reentry begins at
    // -seamSpeed (matched), so the motion is velocity-continuous across the seam —
    // it never decelerates to a stop and re-accelerates. (Inward = distance
    // decreasing, so the rates are negative.)
    static float HermiteDistance(float d0, float d1, float v0, float v1, float duration, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        float h00 = 2f * u3 - 3f * u2 + 1f;
        float h10 = u3 - 2f * u2 + u;
        float h01 = -2f * u3 + 3f * u2;
        float h11 = u3 - u2;
        return h00 * d0 + h10 * (duration * v0) + h01 * d1 + h11 * (duration * v1);
    }

    IEnumerator Approach()
    {
        int li = 0;
        float t = 0f;
        while (t < approachDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / approachDuration);
            // Start from rest, ease up to seamSpeed by the handoff.
            _curDistance = HermiteDistance(startDistance, arrivalDistance, 0f, -seamSpeed, approachDuration, u);
            if (_heart != null)
            {
                _heart.volume = Mathf.Lerp(0f, heartbeatApproachVolume, Mathf.Clamp01(t / heartbeatFadeIn));
                // Subtle quickening over the drift (up to 1/4 of the way to the impact rate).
                _heart.pitch  = Mathf.Lerp(heartbeatStartPitch, Mathf.Lerp(heartbeatStartPitch, heartbeatImpactPitch, 0.25f), u);
            }
            while (li < approachLines.Length && li < approachLineTimes.Length && t >= approachLineTimes[li])
            {
                Speak(approachLines[li]);
                li++;
            }
            yield return null;
        }
    }

    IEnumerator Countdown()
    {
        if (_rumble != null && rumbleClip != null) _rumble.Play();

        float t = 0f;
        int last = -1;
        while (t < countdownDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / countdownDuration);
            // Enter at seamSpeed (continuous), brake below it mid-reentry, then
            // accelerate to impactSpeed — the brake fails and it crashes.
            _curDistance = HermiteDistance(arrivalDistance, impactDistance, -seamSpeed, -impactSpeed, countdownDuration, k);
            _shakeAmp = shakeMaxAmplitude * shakeRamp.Evaluate(k);
            if (_rumble != null) _rumble.volume = rumbleVolume * k;
            // Heart races toward impact — louder AND faster (pitch) the closer we get.
            if (_heart != null)
            {
                _heart.volume = Mathf.Lerp(heartbeatApproachVolume, heartbeatImpactVolume, k);
                _heart.pitch  = Mathf.Lerp(Mathf.Lerp(heartbeatStartPitch, heartbeatImpactPitch, 0.25f), heartbeatImpactPitch, k);
            }

            int rem = Mathf.CeilToInt(countdownStart * (1f - k));
            if (_console != null) _console.text = rem > 0 ? $"PROXIMITY ALERT\nIMPACT IN {rem}" : "PROXIMITY ALERT";
            if (rem != last)
            {
                last = rem;
                if (_sfx != null && alarmBeepClip != null) _sfx.PlayOneShot(alarmBeepClip, alarmVolume);
            }
            yield return null;
        }

        if (_sfx != null && impactBoomClip != null) _sfx.PlayOneShot(impactBoomClip, impactVolume);
        if (_console != null) _console.text = "";
        yield return Fade(0f, 1f, impactFadeTime);   // hard cut to black
    }

    // ── Teardown (idempotent; also runs on abort) ────────────────────────────
    void Teardown()
    {
        if (!_active) return;
        _active = false; _flying = false; _shakeAmp = 0f;

        if (_pc != null && _rb != null)
        {
            // Relocate to the cabin spawn (live position survives the planet's orbit),
            // inheriting the cabin body's orbital velocity — the GameSetUp pattern.
            Transform sp = _pc.spawnPoint;
            Vector3 dstPos = sp != null ? sp.position : _returnPos;
            Quaternion dstRot = sp != null ? sp.rotation : _returnRot;
            _rb.position = dstPos; _player.position = dstPos;
            _rb.rotation = dstRot; _player.rotation = dstRot;
            _rb.isKinematic = _wasKinematic;
            CelestialBody body = sp != null ? sp.GetComponentInParent<CelestialBody>() : null;
            _pc.SetVelocity(body != null ? body.velocity : Vector3.zero);
        }
        IntroSequenceController.SuppressGroggyCameraFx = false;
        HALCommentator.SuppressAutonomous = false;
        FallDamage.Suppressed = false;
        HudVisibility.SetForceHidden(false);

        // DestroyImmediate (not Destroy): the cabin wake-up attaches its OWN
        // GrogginessImageEffect on the same frame, and the component is
        // [DisallowMultipleComponent] — a deferred Destroy would still be present
        // and block the wake-up's AddComponent.
        if (_grog != null) { DestroyImmediate(_grog); _grog = null; }
        if (_podInstance != null) Destroy(_podInstance);
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_ambient != null) { _ambient.Stop(); Destroy(_ambient); }
        if (_rumble != null) { _rumble.Stop(); Destroy(_rumble); }
        if (_heart != null) { _heart.Stop(); Destroy(_heart); }
        if (_sfx != null) { _sfx.Stop(); Destroy(_sfx); }
    }

    void OnDisable() { Teardown(); }
    void OnDestroy() { Teardown(); }

    void Update()
    {
        if (_active && Input.GetKeyDown(KeyCode.Escape)) _skip = true;

        // Ease the stasis grogginess from full blur toward the woozy floor, then
        // hold it there with the breathing pulse (head-throb / double vision).
        if (_grog != null)
        {
            _grogIntensity = Mathf.MoveTowards(_grogIntensity, _grogTarget, grogRecoverRate * Time.unscaledDeltaTime);
            _grog.intensity = BreathingGrog(_grogIntensity);
        }
    }

    // Base woozy level pulsed up to breatheGrogMax× and back on a slow breathing
    // rhythm (worse -> better -> worse) — mirrors IntroSequenceController.
    float BreathingGrog(float baseLevel)
    {
        float s = 0.5f * (1f - Mathf.Cos(Time.unscaledTime * breatheGrogSpeed)); // 0..1
        return Mathf.Clamp01(baseLevel * (1f + s * (breatheGrogMax - 1f)));
    }

    // ── UI / audio / fade ─────────────────────────────────────────────────────
    void BuildCanvas()
    {
        var go = new GameObject("PodArrivalOverlay");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32761;                  // one above the intro overlay (32760)
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        _fade = NewImage(go.transform, "Fade");
        _fade.color = new Color(0f, 0f, 0f, 1f);       // start black (reveal from here)

        var ct = new GameObject("Console");
        ct.transform.SetParent(go.transform, false);
        _console = ct.AddComponent<TextMeshProUGUI>();
        _console.alignment = TextAlignmentOptions.Center;
        _console.fontSize = 54;
        _console.color = new Color(1f, 0.25f, 0.2f, 1f);
        var rt = _console.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.82f); rt.anchorMax = new Vector2(0.5f, 0.82f);  // near the TOP (clear of the bottom HUD)
        rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(1200f, 200f);
        _console.text = "";
    }

    Image NewImage(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    void StartAudio()
    {
        _ambient = AddLoop(ambientHumClip, ambientVolume, true);
        _rumble  = AddLoop(rumbleClip, 0f, false);
        _heart = AddLoop(heartbeatClip, 0f, true);           // fades in at wake, ramps louder + faster toward impact
        if (_heart != null) _heart.pitch = heartbeatStartPitch;
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.spatialBlend = 0f; _sfx.playOnAwake = false;
    }

    AudioSource AddLoop(AudioClip clip, float vol, bool play)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip; src.loop = true; src.spatialBlend = 0f;
        src.volume = vol; src.playOnAwake = false;
        if (play && clip != null) src.Play();
        return src;
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float tt = 0f;
        while (tt < seconds)
        {
            tt += Time.unscaledDeltaTime;
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, seconds > 0f ? tt / seconds : 1f));
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
