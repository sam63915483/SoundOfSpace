using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Stasis-pod arrival cinematic. Plays on a fresh New Game BEFORE the cabin
// wake-up (see IntroSequenceController). Scene-placed (NOT an auto-singleton).
//
// Reuses the real player camera (it carries the atmosphere/planet post-effects),
// reparenting it under a runtime-built pod rig that flies a scripted path toward
// Humble Abode. Restores the camera + PlayerController on impact/skip/abort.
//
// Design: docs/superpowers/specs/2026-06-18-stasis-pod-arrival-intro-design.md
public class PodArrivalSequence : MonoBehaviour
{
    // ── Tunables (serialized; appended at END per convention) ───────────────
    [Header("Target")]
    [SerializeField] string targetBodyName = "Humble Abode";

    [Header("Approach")]
    [SerializeField] float startDistance = 4000f;       // how far out the pod begins
    [SerializeField] Vector3 approachOffset = new Vector3(0.3f, 0.6f, -1f); // dir from planet the pod approaches from (normalized at runtime)
    [SerializeField] float arrivalDistance = 60f;       // distance from planet at end of the calm approach
    [SerializeField] float impactDistance = 8f;         // distance at the moment of impact (planet fills view)

    [Header("Timing (seconds)")]
    [SerializeField] float fadeInTime   = 2f;           // black -> scene reveal
    [SerializeField] float approachDuration  = 20f;     // calm drift
    [SerializeField] float countdownDuration = 10f;     // proximity-alert countdown
    [SerializeField] float impactFadeTime = 0.12f;      // cut to black on impact
    [SerializeField] float skipFadeTime   = 0.4f;       // fade on skip

    [Header("Look (free-look out the window)")]
    [SerializeField] float lookSensitivity = 2f;
    [SerializeField] float yawClamp   = 120f;           // +/- degrees around the window
    [SerializeField] float pitchClamp = 75f;
    [SerializeField] Vector2 initialLook = Vector2.zero; // (yaw, pitch) at start; 0,0 = straight out the window

    [Header("Pod interior placeholder (box open at the front = window)")]
    [SerializeField] float podWidth  = 4f;
    [SerializeField] float podHeight = 3f;
    [SerializeField] float podDepth  = 4f;
    [SerializeField] float podWallThickness = 0.15f;
    [SerializeField] Color podInteriorColor = new Color(0.05f, 0.05f, 0.06f, 1f);

    [Header("Shake / impact")]
    [SerializeField] float shakeMaxAmplitude = 1.2f;    // peak camera shake at impact (local units)
    [SerializeField] AnimationCurve shakeRamp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] AnimationCurve approachEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] AnimationCurve countdownAccel = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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
    [SerializeField] float[] approachLineTimes = { 2f, 11f };   // seconds into the approach to send each line

    [Header("Countdown")]
    [SerializeField] int countdownStart = 10;

    // ── Runtime ─────────────────────────────────────────────────────────────
    CelestialBody _target;
    Camera _cam;
    Transform _camOrigParent;
    Vector3 _camOrigLocalPos;
    Quaternion _camOrigLocalRot;
    PlayerController _pc;
    bool _pcWasEnabled;

    GameObject _podRig;
    Material _podMaterial;
    Vector3 _seatLocalPos;

    Canvas _canvas;
    Image _fade;
    TextMeshProUGUI _console;

    AudioSource _ambient, _rumble, _sfx;

    float _yaw, _pitch;
    bool _lookActive;
    float _shakeAmp;
    bool _skip;
    bool _active;       // true once set up; guards teardown idempotency

    // ── Entry point ──────────────────────────────────────────────────────────
    public IEnumerator Play()
    {
        _skip = false;                  // reset in case a previous run set it (replay path)
        if (!Locate()) yield break;     // no target -> fall straight through to the wake-up

        Setup();
        yield return Fade(1f, 0f, fadeInTime);   // reveal the scene

        // Task 2 placeholder: hold in space so we can verify the reveal + restore.
        // Replaced by the flight + countdown in Tasks 4-5.
        yield return new WaitForSecondsRealtime(5f);

        yield return Fade(0f, 1f, impactFadeTime);
        Teardown();
    }

    // ── Setup / locate ─────────────────────────────────────────────────────
    bool Locate()
    {
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == targetBodyName) { _target = b; break; }
        return _target != null;
    }

    void Setup()
    {
        _pc  = FindObjectOfType<PlayerController>();
        _cam = _pc != null ? _pc.Camera : Camera.main;
        if (_cam == null) return;   // no camera to drive — bail before touching anything; Play() degrades to a black beat and Teardown stays a no-op (_active never set)

        // Reuse the player camera: detach it from the player and put it under a
        // pod rig. Capture its exact original parent/local pose to restore later.
        _camOrigParent   = _cam.transform.parent;
        _camOrigLocalPos = _cam.transform.localPosition;
        _camOrigLocalRot = _cam.transform.localRotation;

        if (_pc != null) { _pcWasEnabled = _pc.enabled; _pc.enabled = false; }
        IntroSequenceController.SuppressGroggyCameraFx = true;   // mute camera-FX modules during the cinematic
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Vector3 dir = approachOffset.sqrMagnitude > 0.0001f ? approachOffset.normalized : -Vector3.forward;
        Vector3 startPos = _target.Position + dir * startDistance;

        _podRig = new GameObject("PodArrivalRig");
        _podRig.transform.position = startPos;
        _podRig.transform.rotation = Quaternion.LookRotation(_target.Position - startPos, Vector3.up);

        BuildPodInterior(_podRig.transform);

        _seatLocalPos = new Vector3(0f, 0f, podDepth * 0.25f);  // near the open front (window)
        _cam.transform.SetParent(_podRig.transform, false);
        _cam.transform.localPosition = _seatLocalPos;
        _yaw = initialLook.x; _pitch = initialLook.y;
        ApplyLook();

        BuildCanvas();
        StartAudio();
        _active = true;
    }

    // Five dark slabs forming a box open at the front (+Z). The open front is the
    // "window"; looking around shows the dark stasis-pod interior. Placeholder art.
    void BuildPodInterior(Transform parent)
    {
        _podMaterial = new Material(Shader.Find("Unlit/Color")) { color = podInteriorColor };
        var mat = _podMaterial;
        float W = podWidth, H = podHeight, D = podDepth, t = podWallThickness;
        MakeWall(parent, mat, "Back",   new Vector3(0, 0, -D / 2f), new Vector3(W, H, t));
        MakeWall(parent, mat, "Top",    new Vector3(0, H / 2f, 0),  new Vector3(W, t, D));
        MakeWall(parent, mat, "Bottom", new Vector3(0, -H / 2f, 0), new Vector3(W, t, D));
        MakeWall(parent, mat, "Left",   new Vector3(-W / 2f, 0, 0), new Vector3(t, H, D));
        MakeWall(parent, mat, "Right",  new Vector3(W / 2f, 0, 0),  new Vector3(t, H, D));
    }

    void MakeWall(Transform parent, Material mat, string name, Vector3 localPos, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Pod_" + name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);                 // cosmetic only
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void BuildCanvas()
    {
        var go = new GameObject("PodArrivalOverlay");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32761;                  // one above the intro overlay (32760)
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        _fade = NewImage(go.transform, "Fade");
        _fade.color = new Color(0f, 0f, 0f, 1f);       // start black (we reveal from here)

        var ct = new GameObject("Console");
        ct.transform.SetParent(go.transform, false);   // last child -> on top of the fade
        _console = ct.AddComponent<TextMeshProUGUI>();
        _console.alignment = TextAlignmentOptions.Center;
        _console.fontSize = 54;
        _console.color = new Color(1f, 0.25f, 0.2f, 1f);
        var rt = _console.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.18f); rt.anchorMax = new Vector2(0.5f, 0.18f);
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
        _rumble  = AddLoop(rumbleClip, 0f, true);          // faded in during the countdown
        _sfx = gameObject.AddComponent<AudioSource>();      // one-shots (alarm/boom)
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

    // ── Per-frame look + skip ────────────────────────────────────────────────
    void Update()
    {
        if (!_active) return;
        if (Input.GetKeyDown(KeyCode.Escape)) _skip = true;

        if (_lookActive)
        {
            _yaw   = Mathf.Clamp(_yaw   + Input.GetAxis("Mouse X") * lookSensitivity, -yawClamp, yawClamp);
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -pitchClamp, pitchClamp);
            ApplyLook();
        }
    }

    void ApplyLook()
    {
        if (_cam == null) return;
        _cam.transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 shake = _shakeAmp > 0f
            ? new Vector3(Mathf.PerlinNoise(Time.unscaledTime * 25f, 0f) - 0.5f,
                          Mathf.PerlinNoise(0f, Time.unscaledTime * 25f) - 0.5f, 0f) * (2f * _shakeAmp)
            : Vector3.zero;
        _cam.transform.localPosition = _seatLocalPos + shake;
    }

    // ── Fades ─────────────────────────────────────────────────────────────
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

    // ── Teardown (idempotent; also runs on abort) ────────────────────────────
    void Teardown()
    {
        if (!_active) return;
        _active = false; _lookActive = false; _shakeAmp = 0f;

        if (_cam != null)
        {
            _cam.transform.SetParent(_camOrigParent, false);
            _cam.transform.localPosition = _camOrigLocalPos;
            _cam.transform.localRotation = _camOrigLocalRot;
        }
        if (_pc != null) _pc.enabled = _pcWasEnabled;
        IntroSequenceController.SuppressGroggyCameraFx = false;

        if (_podRig != null) Destroy(_podRig);
        if (_podMaterial != null) Destroy(_podMaterial);
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_ambient != null) { _ambient.Stop(); Destroy(_ambient); }
        if (_rumble != null) { _rumble.Stop(); Destroy(_rumble); }
        if (_sfx != null) { _sfx.Stop(); Destroy(_sfx); }
    }

    // Safety net: if the sequence is interrupted (scene reload) mid-flight, never
    // strand a detached camera or a disabled PlayerController.
    void OnDisable() { Teardown(); }
    void OnDestroy() { Teardown(); }
}
