# Stasis-Pod Arrival Intro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a ~30-second stasis-pod arrival cinematic that plays on a fresh New Game before the existing cabin wake-up: fly the real player camera into Humble Abode, free-look out a pod window, 10s countdown to impact, cut to black, then hand off unchanged to the wake-up.

**Architecture:** A single self-contained scene-placed `PodArrivalSequence` MonoBehaviour exposing `IEnumerator Play()`. `IntroSequenceController.Start()` calls it between setting `EarlyGameProgress.IntroPlayed = true` and `RunSequence()`, while the black overlay is up. The sequence reuses the real player camera (which carries the atmosphere/planet post-effects), reparenting it under a runtime-built "pod rig" that flies a scripted path; the player free-looks via local camera rotation. On impact/skip it restores the camera, re-enables `PlayerController`, and returns a clean black screen.

**Tech Stack:** Unity 2022.3, Built-in Render Pipeline, C# (`Assembly-CSharp`, no asmdefs), TextMeshPro for UI. No CLI build/test — verify by compile (Console / Coplay MCP `check_compile_errors`) and Play-mode observation (New Game in `Assets/1.6.7.7.7.unity`), with MCP screenshots for framing.

**Spec:** `docs/superpowers/specs/2026-06-18-stasis-pod-arrival-intro-design.md`

> **REVISION 2026-06-18 (post-implementation).** Tasks 1–2 were built as written, but
> Task 2's camera-detach approach failed against the floating origin (see the spec's
> "REVISED" note). `PodArrivalSequence` was rewritten to **move the player, not the
> camera** (commit `5aba99f`). That rewrite folded several planned tasks together —
> **done now:** seam wiring (T1), setup/teardown + reveal (T2, rewritten), free-look
> (T3, via PlayerController), approach flight (T4), countdown + impact + cut-to-black
> (T5), HAL subtitles (T7), Esc skip + abort safety (T8). **Remaining:** audio clips
> (T6), and tuning/polish + load-path regression (T9: framing, distances, look feel,
> pod-window art). The task bodies below describe the original camera approach for
> beats already implemented differently — follow the committed code, not the old
> camera steps, for T2–T5.

---

## Conventions for every task

- **Compile check:** After editing a script, save it. In Unity, confirm the Console shows no compile errors (or run the Coplay MCP `check_compile_errors` tool). "Expected: no errors" below means exactly that.
- **Play test:** "Enter Play mode → New Game" means: open `Assets/1.6.7.7.7.unity`, press Play, and (if not already there) trigger a fresh New Game so `PendingLoad.Data == null` and `EarlyGameProgress.IntroPlayed` is reset. The pod intro only runs on a fresh New Game.
- **Serialized field convention (CLAUDE.md):** append new `[SerializeField]` fields at the END of the class block, never mid-class. This plan front-loads the full field block in Task 2 to avoid churn; later tasks add a few more, always appended.
- **Commit** after each task. New `.cs` files need `git add` of BOTH the `.cs` and its `.meta` (a Unity-generated sidecar). Use `git status` to find the `.meta`.

---

## File Structure

- **Create:** `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` — the entire cinematic (locate planet, build pod rig + camera reparent, fade canvas, look-around, flight, countdown UI, audio, impact, teardown, skip). One responsibility: play the pod arrival and restore state. Lives beside `IntroSequenceController.cs` (same folder, same "scene-placed Mission-1 intro" family).
- **Modify:** `Assets/3 - Scripts/Tutorial/IntroSequenceController.cs` — add a serialized `PodArrivalSequence` reference (appended at END) and call `Play()` in `Start()` between line 149 and 150; hide/restore the intro black overlay around the call.

No save-schema, `NewGameReset`, or forbidden-zone files are touched.

---

## Task 1: Skeleton component + wire into the intro seam (no-op)

Produces a compiling no-op that proves the seam works: the intro plays exactly as today, but now routes through `PodArrivalSequence.Play()`.

**Files:**
- Create: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`
- Modify: `Assets/3 - Scripts/Tutorial/IntroSequenceController.cs` (fields at END; `Start()` around line 149-150)

- [ ] **Step 1: Create the skeleton component**

Create `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`:

```csharp
using System.Collections;
using UnityEngine;

// Stasis-pod arrival cinematic. Plays on a fresh New Game BEFORE the cabin
// wake-up (see IntroSequenceController). Scene-placed (NOT an auto-singleton):
// drop one on an object in the gameplay scene and wire it to
// IntroSequenceController._podArrival.
//
// Reuses the real player camera (it carries the atmosphere/planet post-effects),
// reparenting it under a runtime-built pod rig that flies a scripted path toward
// Humble Abode. Restores the camera + PlayerController on impact/skip/abort.
//
// Design: docs/superpowers/specs/2026-06-18-stasis-pod-arrival-intro-design.md
public class PodArrivalSequence : MonoBehaviour
{
    // Entry point, called by IntroSequenceController.Start() while the black
    // overlay is up. No-op skeleton for now — fleshed out in later tasks.
    public IEnumerator Play()
    {
        yield break;
    }
}
```

- [ ] **Step 2: Add the serialized reference + call site in IntroSequenceController**

In `Assets/3 - Scripts/Tutorial/IntroSequenceController.cs`, append this field at the END of the class (just before the final closing brace, after `FadeOutAndCleanup`):

```csharp
    [Header("Pod arrival intro (plays before the wake-up)")]
    [SerializeField] PodArrivalSequence _podArrival;   // optional; if null, the pod intro is skipped
```

Then change the `Start()` coroutine. Current lines 149-150 are:

```csharp
        EarlyGameProgress.IntroPlayed = true;
        yield return RunSequence();
```

Replace with:

```csharp
        EarlyGameProgress.IntroPlayed = true;

        // Pod arrival cinematic runs first, under the black overlay. Hide our
        // overlay while it owns the screen (it manages its own fade), then
        // restore it (black, eyes shut) so the wake-up takes over seamlessly.
        if (_podArrival != null)
        {
            if (_canvas != null) _canvas.enabled = false;
            yield return _podArrival.Play();
            if (_canvas != null) _canvas.enabled = true;
            _openness = _opennessTarget = 0f;   // eyes shut again for the wake-up
            ApplyEyelids(0f);
        }

        yield return RunSequence();
```

- [ ] **Step 3: Compile**

Save both files. In Unity, confirm the Console shows no errors (or run Coplay MCP `check_compile_errors`).
Expected: no errors. (`_podArrival` is unassigned, which is fine — the call is null-guarded.)

- [ ] **Step 4: Play test — intro unchanged**

Enter Play mode → New Game. Because `_podArrival` is not yet assigned in the scene, the wake-up should play exactly as before (black screen → click to wake → HAL briefing).
Expected: identical to current behavior, no errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs" "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs.meta" "Assets/3 - Scripts/Tutorial/IntroSequenceController.cs"
git commit -m "feat(intro): add PodArrivalSequence skeleton + wire into wake-up seam"
```

---

## Task 2: Setup + teardown — reveal space, hold, restore cleanly

The riskiest plumbing: locate Humble Abode, reparent the player camera under a pod rig far out in space, build the fade canvas, reveal the scene for a few seconds, then restore the camera + `PlayerController` so the wake-up still works. No flight yet — a static hold proves the camera handoff is reversible.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`

- [ ] **Step 1: Replace the file with the full field block + setup/teardown + static hold**

Replace the entire contents of `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` with:

```csharp
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
        var mat = new Material(Shader.Find("Unlit/Color")) { color = podInteriorColor };
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
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_ambient != null) Destroy(_ambient);
        if (_rumble != null) Destroy(_rumble);
        if (_sfx != null) Destroy(_sfx);
    }

    // Safety net: if the sequence is interrupted (scene reload) mid-flight, never
    // strand a detached camera or a disabled PlayerController.
    void OnDisable() { Teardown(); }
    void OnDestroy() { Teardown(); }
}
```

- [ ] **Step 2: Compile**

Save. Confirm no Console errors (or Coplay MCP `check_compile_errors`).
Expected: no errors.

- [ ] **Step 3: Create the scene object + wire it up (Coplay MCP)**

In the gameplay scene `Assets/1.6.7.7.7.unity`, the `IntroSequenceController` lives on a scene object. Add the `PodArrivalSequence` component to that SAME GameObject (use Coplay MCP `add_component` with type `PodArrivalSequence` on the object that has `IntroSequenceController`), then assign that component to the controller's `_podArrival` field (Coplay MCP `set_property` on the `IntroSequenceController`'s `_podArrival` to reference the component). Save the scene (`save_scene`).

- [ ] **Step 4: Play test — reveal + clean restore**

Enter Play mode → New Game.
Expected: after the initial black, the screen fades in to show the solar system from far out (you are looking toward Humble Abode from `startDistance`). It holds ~5s, then cuts to black, and the **normal wake-up begins** (click-to-wake, HAL briefing) with the camera back at the player's head in the cabin — no offset, no leftover pod, no errors. The camera restore working is the key thing to confirm.

If the planet isn't framed, adjust `approachOffset` / `startDistance` later (Task 9). For now just confirm reveal + restore.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs" "Assets/1.6.7.7.7.unity"
git commit -m "feat(intro): pod rig setup/teardown + static space reveal"
```

---

## Task 3: Free-look out the window

Enable mouse look-around inside the pod during the hold.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` (`Play()`)

- [ ] **Step 1: Turn look on for the hold**

In `Play()`, the Task-2 hold currently reads:

```csharp
        Setup();
        yield return Fade(1f, 0f, fadeInTime);   // reveal the scene

        // Task 2 placeholder: hold in space so we can verify the reveal + restore.
        // Replaced by the flight + countdown in Tasks 4-5.
        yield return new WaitForSecondsRealtime(5f);
```

Change the hold block to enable look:

```csharp
        Setup();
        _lookActive = true;                       // free-look out the window
        yield return Fade(1f, 0f, fadeInTime);    // reveal the scene

        // Task 3 hold: look around while stationary (flight added in Task 4).
        yield return new WaitForSecondsRealtime(8f);
```

- [ ] **Step 2: Compile**

Save. Expected: no errors.

- [ ] **Step 3: Play test — look around**

Enter Play mode → New Game.
Expected: during the ~8s hold you can move the mouse to look around inside the dark pod (you see the box interior walls as you pan), with the solar system visible out the open front (window). Yaw is limited to ±120°, pitch ±75°. Then cut to black → wake-up as before.

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs"
git commit -m "feat(intro): free-look inside the pod"
```

---

## Task 4: Calm approach flight

Replace the static hold with the ~20s eased drift toward Humble Abode. The position is recomputed each frame from the planet's live `Position`, so the pod co-moves with the planet's orbit and stays aimed at it.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` (`Play()` + new `Approach()` coroutine)

- [ ] **Step 1: Add the approach coroutine**

Add this method to the class (after `Play()`):

```csharp
    // Eased drift from startDistance down to arrivalDistance over approachDuration.
    // Position is recomputed from the live planet Position each frame so the pod
    // tracks Humble Abode's orbital motion and keeps it framed in the window.
    IEnumerator Approach()
    {
        int lineIdx = 0;
        Vector3 dir = approachOffset.sqrMagnitude > 0.0001f ? approachOffset.normalized : -Vector3.forward;
        float t = 0f;
        while (t < approachDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float k = approachEase.Evaluate(Mathf.Clamp01(t / approachDuration));
            float dist = Mathf.Lerp(startDistance, arrivalDistance, k);
            PlacePod(dir, dist);

            // Fire scheduled HAL subtitle lines (wired in Task 7; harmless now).
            while (lineIdx < approachLines.Length && lineIdx < approachLineTimes.Length
                   && t >= approachLineTimes[lineIdx])
            {
                Speak(approachLines[lineIdx]);
                lineIdx++;
            }
            yield return null;
        }
    }

    // Re-aims the pod at the (moving) planet and places it `dist` away along `dir`.
    void PlacePod(Vector3 dir, float dist)
    {
        if (_podRig == null || _target == null) return;
        Vector3 pos = _target.Position + dir * dist;
        _podRig.transform.position = pos;
        _podRig.transform.rotation = Quaternion.LookRotation(_target.Position - pos, Vector3.up);
    }
```

- [ ] **Step 2: Add a no-op `Speak` stub (real impl in Task 7)**

Add this method to the class (so `Approach()` compiles now; Task 7 fills it in):

```csharp
    // Sends a HAL subtitle line via the existing HUD pipeline. Filled in Task 7.
    void Speak(string line) { }
```

- [ ] **Step 3: Use the approach in `Play()`**

Replace the Task-3 hold block:

```csharp
        Setup();
        _lookActive = true;                       // free-look out the window
        yield return Fade(1f, 0f, fadeInTime);    // reveal the scene

        // Task 3 hold: look around while stationary (flight added in Task 4).
        yield return new WaitForSecondsRealtime(8f);

        yield return Fade(0f, 1f, impactFadeTime);
        Teardown();
```

with:

```csharp
        Setup();
        _lookActive = true;                       // free-look out the window
        yield return Fade(1f, 0f, fadeInTime);    // reveal the scene

        yield return Approach();                   // ~20s calm drift toward the planet

        // Countdown + impact added in Task 5.
        yield return Fade(0f, 1f, impactFadeTime);
        Teardown();
```

- [ ] **Step 4: Compile**

Save. Expected: no errors.

- [ ] **Step 5: Play test — drift in**

Enter Play mode → New Game.
Expected: over ~20s the pod visibly drifts closer to Humble Abode (it grows in the window) while you can still look around; then cut to black → wake-up. No errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs"
git commit -m "feat(intro): calm approach flight toward Humble Abode"
```

---

## Task 5: Countdown + impact

Add the 10s proximity-alert countdown: console readout, accelerating descent, ramping screen shake, impact flash, cut to black.

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` (`Play()` + new `Countdown()` coroutine)

- [ ] **Step 1: Add the countdown coroutine**

Add this method to the class (after `Approach()`):

```csharp
    // 10s proximity-alert countdown: console readout + alarm beeps + rising
    // rumble + accelerating descent from arrivalDistance toward impactDistance,
    // with screen shake ramping to impact.
    IEnumerator Countdown()
    {
        Vector3 dir = approachOffset.sqrMagnitude > 0.0001f ? approachOffset.normalized : -Vector3.forward;
        if (_rumble != null && rumbleClip != null) _rumble.Play();

        float t = 0f;
        int lastWhole = -1;
        while (t < countdownDuration && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / countdownDuration);
            float accel = countdownAccel.Evaluate(k);

            PlacePod(dir, Mathf.Lerp(arrivalDistance, impactDistance, accel));
            _shakeAmp = shakeMaxAmplitude * shakeRamp.Evaluate(k);
            if (_rumble != null) _rumble.volume = rumbleVolume * k;

            int remaining = Mathf.CeilToInt(countdownStart * (1f - k));
            if (_console != null) _console.text = remaining > 0 ? $"PROXIMITY ALERT\nIMPACT IN {remaining}" : "PROXIMITY ALERT";
            if (remaining != lastWhole)                 // one alarm beep per tick
            {
                lastWhole = remaining;
                if (_sfx != null && alarmBeepClip != null) _sfx.PlayOneShot(alarmBeepClip, alarmVolume);
            }
            yield return null;
        }

        // Impact.
        if (_sfx != null && impactBoomClip != null) _sfx.PlayOneShot(impactBoomClip, impactVolume);
        if (_console != null) _console.text = "";
        yield return Fade(0f, 1f, impactFadeTime);      // hard cut to black
    }
```

- [ ] **Step 2: Use the countdown in `Play()`**

Replace:

```csharp
        yield return Approach();                   // ~20s calm drift toward the planet

        // Countdown + impact added in Task 5.
        yield return Fade(0f, 1f, impactFadeTime);
        Teardown();
```

with:

```csharp
        yield return Approach();                   // ~20s calm drift toward the planet
        if (!_skip) yield return Countdown();       // 10s countdown -> impact -> black
        if (_skip)  yield return Fade(0f, 1f, skipFadeTime);

        Teardown();
```

- [ ] **Step 3: Compile**

Save. Expected: no errors.

- [ ] **Step 4: Play test — countdown to impact**

Enter Play mode → New Game.
Expected: after the calm approach, a red "PROXIMITY ALERT / IMPACT IN 10…9…" console readout counts down while the pod accelerates into Humble Abode and the view shakes harder; at 0 it cuts to black, then the normal wake-up plays. (Audio is silent until Task 6 wires clips — that's fine.) No errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs"
git commit -m "feat(intro): proximity countdown, accel, shake, impact cut-to-black"
```

---

## Task 6: Audio clips

Generate/assign the ambient hum, alarm beep, rumble, and impact boom. Clips are optional in code (null = silent), so this task only assigns assets.

**Files:**
- Modify: scene `Assets/1.6.7.7.7.unity` (component field assignments only)
- Create: audio assets under `Assets/` (via Coplay MCP `generate_sfx`)

- [ ] **Step 1: Generate the SFX (Coplay MCP)**

Use Coplay MCP `generate_sfx` for four clips (per the memory note, SFX generation may report a timeout but still lands the asset — re-list files to confirm):
- Ambient: "low continuous sci-fi stasis pod hum, steady, loopable" → e.g. `Assets/Audio/Intro/pod_hum.wav`
- Alarm: "short urgent cockpit warning beep, single" → `Assets/Audio/Intro/pod_alarm_beep.wav`
- Rumble: "deep building atmospheric re-entry rumble, loopable" → `Assets/Audio/Intro/pod_rumble.wav`
- Impact: "violent crash impact boom with debris, one-shot" → `Assets/Audio/Intro/pod_impact.wav`

- [ ] **Step 2: Assign clips to the component**

On the `PodArrivalSequence` component in the scene, assign (Coplay MCP `set_property`, or the Inspector):
- `ambientHumClip` → `pod_hum`
- `alarmBeepClip` → `pod_alarm_beep`
- `rumbleClip` → `pod_rumble`
- `impactBoomClip` → `pod_impact`

Save the scene.

- [ ] **Step 3: Play test — audio**

Enter Play mode → New Game.
Expected: hum during the approach; a beep on each countdown tick; rumble rising through the countdown; a boom at impact. Levels are rough — tune the `*Volume` fields in Task 9. No errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Audio/Intro" "Assets/1.6.7.7.7.unity"
git commit -m "feat(intro): pod arrival SFX (hum/alarm/rumble/impact)"
```

---

## Task 7: HAL subtitle lines

Wire the `Speak()` stub to the existing HAL HUD pipeline so the approach lines display as subtitles (text only, matching the spec).

**Files:**
- Modify: `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs` (`Speak()`)

- [ ] **Step 1: Implement `Speak()`**

Replace the stub:

```csharp
    // Sends a HAL subtitle line via the existing HUD pipeline. Filled in Task 7.
    void Speak(string line) { }
```

with (mirrors `IntroSequenceController.SpeakRaw` — uses HAL if present, else the HUD directly):

```csharp
    // Sends a HAL subtitle line via the existing HUD pipeline (text only; no TTS
    // clip is required). Mirrors IntroSequenceController.SpeakRaw.
    void Speak(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (HALCommentator.Instance != null) HALCommentator.Instance.VolunteerExternal(line, true);
        else if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
    }
```

- [ ] **Step 2: Compile**

Save. Expected: no errors.

- [ ] **Step 3: Play test — subtitles**

Enter Play mode → New Game.
Expected: during the approach, the HAL subtitle line appears at ~2s ("Stasis cycle complete…") and again at ~11s ("Approaching Humble Abode…"). No errors. (Adjust strings/times via `approachLines` / `approachLineTimes` in Task 9.)

- [ ] **Step 4: Commit**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs"
git commit -m "feat(intro): HAL subtitle lines during the approach"
```

---

## Task 8: Skip + abort safety verification

The skip key (Esc) and abort safety (`OnDisable`/`OnDestroy` → `Teardown`) were built in earlier tasks. This task verifies them — no new code unless a check fails.

**Files:**
- (Verification only) `Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs`

- [ ] **Step 1: Play test — skip during approach**

Enter Play mode → New Game. During the calm approach, press **Esc**.
Expected: the screen fades to black over `skipFadeTime` and the normal wake-up begins immediately — camera restored to the player head, no leftover pod, no errors.

- [ ] **Step 2: Play test — skip during countdown**

Enter Play mode → New Game. Wait for the countdown, then press **Esc** mid-countdown.
Expected: same clean fade-to-black + wake-up handoff. The countdown loop exits on `_skip`. No errors.

- [ ] **Step 3: Play test — abort safety**

Enter Play mode → New Game, then (while the pod cinematic is playing) press **Stop** in the Editor.
Expected: no errors logged on exit (the `OnDisable`/`OnDestroy` → `Teardown` restores the camera parent and re-enables `PlayerController` even on interruption).

- [ ] **Step 4: If any check failed, fix and re-test**

If skip didn't break a loop: confirm every `while` in `Approach()`/`Countdown()` has `&& !_skip` and `Play()` has the `if (_skip) yield return Fade(...)` branch. If the camera wasn't restored on Stop: confirm `Teardown()` is guarded by `_active` and called from both `OnDisable` and `OnDestroy`. Re-run Steps 1-3.

- [ ] **Step 5: Commit (only if code changed)**

```bash
git add "Assets/3 - Scripts/Tutorial/PodArrivalSequence.cs"
git commit -m "fix(intro): pod skip / abort-safety corrections"
```

---

## Task 9: Load-path regression + framing/timing tuning

Confirm the intro is new-game-only, then dial in the framing, durations, and levels live in the Editor.

**Files:**
- (Verification + Inspector tuning) scene `Assets/1.6.7.7.7.unity`, `PodArrivalSequence` component

- [ ] **Step 1: Load-path regression**

Load an existing save (not New Game).
Expected: NO pod intro and NO wake-up — `IntroSequenceController` disables itself when `PendingLoad.Data != null` (so `Play()` is never called), and you spawn into normal gameplay. No errors.

- [ ] **Step 2: Replay guard**

From the main menu, start a New Game (pod intro plays), return to the main menu, then start another New Game in the same process run.
Expected: the pod intro plays again on each fresh New Game (gated by `EarlyGameProgress.IntroPlayed`, which `NewGameReset` resets to false). No errors.

- [ ] **Step 3: Tune framing**

Enter Play mode → New Game and judge the opening framing (use Coplay MCP `capture_scene_object` / screenshots). Adjust on the `PodArrivalSequence` component:
- `approachOffset` (direction the pod approaches from — sets how much of the system is in view),
- `startDistance` (how far out it begins; larger = more of the system visible),
- `arrivalDistance` / `impactDistance` (how close before/at impact).
Re-test until the whole solar system reads on approach and Humble Abode fills the window at impact.

- [ ] **Step 4: Tune timing + levels**

Adjust `approachDuration` (≈20), `countdownDuration` (≈10), `fadeInTime`, `shakeMaxAmplitude`, the `*Volume` fields, and `approachLineTimes` so the subtitles land during the calm phase. Confirm total ≈30s.

- [ ] **Step 5: Commit the tuned scene**

```bash
git add "Assets/1.6.7.7.7.unity"
git commit -m "tune(intro): pod arrival framing, timing, and audio levels"
```

- [ ] **Step 6: Final end-to-end pass**

Enter Play mode → New Game and watch the whole thing once: reveal → ~20s look-around approach → 10s countdown with alarm/rumble/shake → impact boom + cut to black → normal cabin wake-up. Confirm it feels like ~30s and hands off cleanly.

---

## Self-Review notes (addressed)

- **Spec coverage:** in-scene camera reuse (T2), look-around (T3), 30s = 20s approach + 10s countdown (T4/T5), pod-HUD countdown + alarm/rumble (T5/T6), HAL subtitles (T7), Esc skip (T8), new-game-only via existing guards + no save changes (T1/T9), forbidden-zone untouched (no such files in any task). All covered.
- **Placeholder scan:** every code step contains complete code; the only intentional stub (`Speak`) is introduced compiling in T4 and implemented in T7, called out explicitly.
- **Type consistency:** `Play`, `Locate`, `Setup`, `Teardown`, `Approach`, `Countdown`, `PlacePod`, `Speak`, `Fade`, `ApplyLook`, fields (`_skip`, `_active`, `_lookActive`, `_shakeAmp`, `_seatLocalPos`, `_target`, `_podRig`, `_cam`, `_console`, `_rumble`, `_sfx`) are used consistently across tasks.
```
