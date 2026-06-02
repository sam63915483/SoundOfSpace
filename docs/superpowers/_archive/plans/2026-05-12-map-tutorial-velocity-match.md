# Map Tutorial, Velocity-Match UI, and Jitter Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 6-step map-mode tutorial matching the existing TutorialUI styling, a "VELOCITY MATCHED / UNMATCHED" UI toast triggered by clicking planets in map view, a behavior tweak so a planet's "mark" persists when zooming to it, and a fix for the jittery camera-follow when close to a planet.

**Architecture:** Build a new `MapTutorial` singleton modeled on `BonusTutorial` but scoped to map mode — pauses on close, resumes on reopen until all 6 steps are complete. Drives the existing `TutorialUI`. Persistence via a new `MapTutorialSave` block. Block M-close while tutorial in flight. Add a `MapVelocityHud` toast that fires whenever `followed` changes in the map controller. Fix jitter by switching follow-delta tracking to interpolated `transform.position`. Modify `OnLegendClick` so the second-click "focus" doesn't clear the highlight ring, making mark persistent.

**Tech Stack:** Unity 2022.3, C# 9, TMPro, existing TutorialUI / SaveCollector / SolarSystemMapController. **No automated test harness in this Unity project** — verification is Editor playtest + `mcp__coplay-mcp__check_compile_errors`. TDD discipline doesn't apply at the unit level here; each task ends with a compile check + a clear Editor verification step.

---

## File Structure

**Create (new files):**
- `Assets/3 - Scripts/Map/MapTutorial.cs` — singleton + 6 step classes + save state hooks
- `Assets/3 - Scripts/Map/MapVelocityHud.cs` — procedural overlay for "VELOCITY MATCHED / UNMATCHED" toast

**Modify (existing files):**
- `Assets/3 - Scripts/Map/SolarSystemMapController.cs` — jitter fix, M-close block during tutorial, fire `MapVelocityHud` events, second-click-keeps-mark
- `Assets/3 - Scripts/Map/MapOrbitLines.cs` — jitter fix (orbit lines anchored via interpolated position)
- `Assets/3 - Scripts/Map/MapBootstrapReal.cs` — instantiate `MapVelocityHud`
- `Assets/3 - Scripts/SaveSystem/SaveData.cs` — add `MapTutorialSave` schema
- `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — `CaptureMapTutorial` / `ApplyMapTutorial`
- `Assets/3 - Scripts/UI/MainMenuController.cs` — seed `MapTutorial` singleton in `EnsureGameplaySingletons`

---

## Spec → Task map

| Requirement | Task |
|---|---|
| Fix jitter when close to a planet in map view | Task 1 |
| 6-step tutorial matching existing TutorialUI | Task 2, 3 |
| Block M to close while tutorial in flight | Task 3 |
| Mark persists across map close | Task 4 |
| Velocity matched / unmatched UI toast | Task 5, 6 |
| Tutorial persists across save/load | Task 7 |
| `MapTutorial` singleton seeded before scene load | Task 8 |
| Verify all the above compile | Task 9 |

---

## Task 1: Fix camera-follow jitter

**Files:**
- Modify: `Assets/3 - Scripts/Map/SolarSystemMapController.cs` — switch `followed.Position` (rb.position, uninterpolated) to `followed.transform.position` (interpolated visual pose) for both initial seed and per-frame delta tracking, and for the floating-origin compensation in `OnFloatingOriginShift`.
- Modify: `Assets/3 - Scripts/Map/MapOrbitLines.cs` — update primary anchoring to `primary.transform.position` so orbit rings follow the interpolated body pose, matching the planet's renderer.

**Why the jitter:** Planet rigidbodies are stepped at 100 Hz via `rb.MovePosition` in `NBodySimulation.FixedUpdate`. With Rigidbody interpolation, the planet renderer uses the smooth `transform.position`, but `rb.position` (= `CelestialBody.Position`) only updates on physics ticks. Reading `rb.position` each frame in `LateUpdate` produces a quantized delta: 0 for most frames, full step every 1/100s. The camera follows that quantized signal while the planet renders smoothly → visible relative jitter, worst when zoomed close.

- [ ] **Step 1.1: Modify `LateUpdate` follow delta to use `transform.position`**

In `SolarSystemMapController.cs`, replace the body-tracking block at the end of `LateUpdate`:

```csharp
if (followed != null && mapCamera != null)
{
    // Use the interpolated transform pose, not the physics-step-quantized
    // rb.position — otherwise the camera delta only updates at 100 Hz while
    // the planet renders smoothly, producing visible jitter at close range.
    Vector3 cur = followed.transform.position;
    Vector3 delta = cur - followedLastPos;
    mapCamera.transform.position += delta;
    followedLastPos = cur;
}
```

And update the click-to-follow seed in the same file (the `Physics.Raycast` block in `LateUpdate`) so `followedLastPos` is seeded from `transform.position`:

```csharp
if (hitBody != null)
{
    followed = hitBody;
    followedLastPos = hitBody.transform.position;
}
```

And in `FocusOn` (already in the controller), update the line that sets `followedLastPos = body.Position` to `body.transform.position`.

- [ ] **Step 1.2: Modify floating-origin compensation**

In `OnFloatingOriginShift`, the camera shift logic is unchanged (it operates on the camera, not the followed body). But ensure `followedLastPos` is consistently in transform-space — the existing `followedLastPos += shift` is correct.

- [ ] **Step 1.3: Update `MapOrbitLines.UpdateLines` to anchor on interpolated pose**

In `MapOrbitLines.cs`, replace `Vector3 baseP = o.primary.Position;` with `Vector3 baseP = o.primary.transform.position;`. This keeps orbit rings rendered against the same interpolated planet pose the renderer uses.

- [ ] **Step 1.4: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: no errors.

- [ ] **Step 1.5: Editor verification**

Open `Assets/1.6.7.7.7.unity`, enter Play mode, press M, fly close to Humble Abode, click it to follow. Verify the planet stays rock-steady on screen as it orbits.

---

## Task 2: Build MapTutorial scaffolding + step base class

**Files:**
- Create: `Assets/3 - Scripts/Map/MapTutorial.cs`

**Pattern:** Mirror `BonusTutorial`'s shape — singleton, `State` enum, `steps` list, `EnterStep` / `AdvanceStep`, drives `TutorialUI.ShowStep` / `SetTip`. Key differences:
- No yes/no popup — auto-starts the moment the user opens the map for the first time.
- Tutorial **pauses on map close, resumes on map open** until all steps complete. After completion, never shows again (persisted via save).
- No performance-review modal at the end (the user didn't ask for one).
- Steps inherit `MapStep` (separate from `BonusStep` so we don't tangle namespaces and so save-keying stays cleanly distinct).

- [ ] **Step 2.1: Write `MapTutorial.cs` scaffolding (no concrete steps yet)**

Create `Assets/3 - Scripts/Map/MapTutorial.cs` with this content (concrete step classes added in Task 3):

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Map-mode tutorial. Lives as a DontDestroyOnLoad singleton like BonusTutorial.
// Drives TutorialUI through six map-specific steps. The tutorial only ticks
// while the map is open; closing the map pauses it, reopening resumes from
// the same step. Persists once finished via MapTutorialSave.
public class MapTutorial : MonoBehaviour
{
    public static MapTutorial Instance { get; private set; }

    enum State { NotStarted, Running, Paused, Finished }
    State state = State.NotStarted;

    List<MapStep> steps;
    int stepIndex = -1;
    string _lastShownTip;
    const string Header = "MAP";

    public bool BlockMapClose => state == State.Running || state == State.Paused;
    public bool IsFinished => state == State.Finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("MapTutorial");
        DontDestroyOnLoad(go);
        go.AddComponent<MapTutorial>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Called by SolarSystemMapController.OpenMap. Starts on first-ever open,
    // resumes on subsequent opens (until Finished).
    public void OnMapOpened()
    {
        if (state == State.Finished) return;
        if (state == State.NotStarted)
        {
            steps = BuildSteps();
            stepIndex = -1;
            EnterStep(0);
            state = State.Running;
            return;
        }
        if (state == State.Paused)
        {
            state = State.Running;
            // Re-show the current tip (TutorialUI may have been hidden during gameplay).
            if (steps != null && stepIndex >= 0 && stepIndex < steps.Count)
            {
                var step = steps[stepIndex];
                _lastShownTip = step.Tip;
                if (TutorialUI.Instance != null)
                    TutorialUI.Instance.ShowStep(_lastShownTip, stepIndex + 1, steps.Count,
                        $"{Header}  ·  {stepIndex + 1} / {steps.Count}");
            }
        }
    }

    // Called by SolarSystemMapController.CloseMap. Pauses tutorial + hides UI.
    public void OnMapClosed()
    {
        if (state == State.Running)
        {
            state = State.Paused;
            if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();
        }
    }

    static List<MapStep> BuildSteps()
    {
        return new List<MapStep>
        {
            new MapLookAroundStep(),
            new MapMovementStep(),
            new MapLockCursorStep(),
            new MapUnlockAndLegendClickStep(),
            new MapMarkPlanetStep(),
            new MapMatchVelocityStep(),
        };
    }

    void EnterStep(int newIndex)
    {
        if (steps != null && stepIndex >= 0 && stepIndex < steps.Count) steps[stepIndex].OnExit();
        stepIndex = newIndex;
        if (stepIndex >= steps.Count)
        {
            state = State.Finished;
            if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();
            return;
        }
        var step = steps[stepIndex];
        step.OnEnter();
        _lastShownTip = step.Tip;
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(_lastShownTip, stepIndex + 1, steps.Count,
                $"{Header}  ·  {stepIndex + 1} / {steps.Count}");
    }

    void Update()
    {
        if (state != State.Running) return;
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Count) return;
        var step = steps[stepIndex];
        if (!step.IsComplete) step.Tick();

        string tip = step.Tip;
        if (tip != _lastShownTip)
        {
            _lastShownTip = tip;
            if (TutorialUI.Instance != null) TutorialUI.Instance.SetTip(tip);
        }

        bool tipRevealing = TutorialUI.Instance != null && TutorialUI.Instance.IsTipRevealing;
        bool animating    = TutorialUI.Instance != null && TutorialUI.Instance.IsAnimating;

        if (step.IsComplete && !tipRevealing && !animating)
        {
            if (TutorialUI.Instance != null) TutorialUI.Instance.PlayCompleteSound();
            EnterStep(stepIndex + 1);
        }
    }

    // ── Save / Load ─────
    public int GetStepIndex() => stepIndex;
    public bool GetFinished() => state == State.Finished;

    public List<bool> GetStepsComplete()
    {
        var list = new List<bool>();
        if (steps != null) foreach (var s in steps) list.Add(s.IsComplete);
        return list;
    }

    public void ApplySaveState(bool finished, int idx, List<bool> stepsCompleteList)
    {
        if (finished)
        {
            state = State.Finished;
            steps = null;
            stepIndex = -1;
            return;
        }
        if (idx < 0)
        {
            state = State.NotStarted;
            steps = null;
            stepIndex = -1;
            return;
        }
        // Mid-tutorial save: restore steps + completion, but stay paused
        // until the user reopens the map.
        steps = BuildSteps();
        if (stepsCompleteList != null)
        {
            for (int i = 0; i < steps.Count && i < stepsCompleteList.Count; i++)
                steps[i].SetComplete(stepsCompleteList[i]);
        }
        stepIndex = Mathf.Clamp(idx, 0, steps.Count - 1);
        steps[stepIndex].OnEnter();
        if (stepsCompleteList != null && stepIndex < stepsCompleteList.Count)
            steps[stepIndex].SetComplete(stepsCompleteList[stepIndex]);
        state = State.Paused;
    }
}

public abstract class MapStep
{
    public abstract string Tip { get; }
    public bool IsComplete { get; protected set; }
    public void SetComplete(bool v) { IsComplete = v; }
    public virtual void OnEnter() { }
    public abstract void Tick();
    public virtual void OnExit() { }
}
```

(Concrete step classes appended in Task 3.)

- [ ] **Step 2.2: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: errors for the 6 missing step classes (`MapLookAroundStep`, etc.) — fixed by Task 3.

---

## Task 3: Build the 6 concrete map tutorial steps + block M-close

**Files:**
- Modify: `Assets/3 - Scripts/Map/MapTutorial.cs` — append 6 step classes
- Modify: `Assets/3 - Scripts/Map/SolarSystemMapController.cs` — call `MapTutorial.Instance.OnMapOpened()` from `OpenMap`, `OnMapClosed()` from `CloseMap`, and block the M-close path when `BlockMapClose` is true.

**Step instrumentation hooks needed** in `SolarSystemMapController`:
- `public event System.Action<CelestialBody> OnVelocityMatched;`
- `public event System.Action OnVelocityUnmatched;`
- `public event System.Action<CelestialBody> OnLegendBodyClicked;`
- `public event System.Action<CelestialBody> OnLegendBodyMarked;`  // fires the moment OnLegendClick's "second click → mark+focus" branch executes
- `public event System.Action<bool> OnCursorLockChanged;` // existing SetMapCursorLocked already toggles internal state — fire event from there

These events are the cleanest way for tutorial steps to observe controller actions without each step grovelling for state.

- [ ] **Step 3.1: Add tutorial-observable events to `SolarSystemMapController.cs`**

Add the event declarations near the top of the class:

```csharp
public event System.Action<CelestialBody> OnVelocityMatched;   // map LMB hit a body
public event System.Action OnVelocityUnmatched;                // map LMB cleared followed
public event System.Action<CelestialBody> OnLegendBodyClicked; // legend entry clicked (any state)
public event System.Action<CelestialBody> OnLegendBodyMarked;  // second click on same body (focus + persist)
public event System.Action<bool> OnCursorLockChanged;          // G toggled
```

Fire them at the matching state changes:

In `SetMapCursorLocked`:
```csharp
void SetMapCursorLocked(bool locked)
{
    mapCursorLocked = locked;
    Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
    Cursor.visible = !locked;
    if (legendUI != null) legendUI.SyncCursorLockHint(locked);
    OnCursorLockChanged?.Invoke(locked);
}
```

In the `Physics.Raycast` follow block inside `LateUpdate`:
```csharp
if (hitBody != null)
{
    if (followed != hitBody)
    {
        followed = hitBody;
        followedLastPos = hitBody.transform.position;
        OnVelocityMatched?.Invoke(hitBody);
    }
}
else
{
    if (followed != null) OnVelocityUnmatched?.Invoke();
    followed = null;
}
```
(Mirror the `else` branch of the outer if-Raycast for the no-hit case.)

In `OnLegendClick`:
```csharp
public void OnLegendClick(CelestialBody body)
{
    if (body == null) return;
    OnLegendBodyClicked?.Invoke(body);
    if (pendingHighlight == body)
    {
        // Second click: focus camera but KEEP the mark (ring persists).
        // This is the "marking a planet will persist" behavior from the
        // map tutorial. The old "clear ring + focus" path is gone.
        FocusOn(body);
        OnLegendBodyMarked?.Invoke(body);
        return;
    }
    EnsureHighlightRing();
    pendingHighlight = body;
    highlightRing.target = body;
    highlightRing.gameObject.SetActive(true);
    if (legendUI != null) legendUI.SetSelected(body);
}
```

- [ ] **Step 3.2: Block M-close while tutorial is running/paused**

In `Update`, modify the M-key handling at line ~74:

```csharp
if (TutorialGate.GetKeyDown(toggleKey, TutorialAbility.Map) ||
    TutorialGate.MapTogglePressed(TutorialAbility.Map))
{
    if (!isOpen && Time.timeScale == 0f) return;
    // Block close while the map tutorial is in flight — must finish all 6
    // tips before M is honored as a close gesture.
    if (isOpen && MapTutorial.Instance != null && MapTutorial.Instance.BlockMapClose) return;
    if (isOpen) CloseMap(); else OpenMap();
}
```

- [ ] **Step 3.3: Notify MapTutorial on open / close**

In `OpenMap()`, at the end (after the legend syncs):

```csharp
if (MapTutorial.Instance != null) MapTutorial.Instance.OnMapOpened();
```

In `CloseMap()`, before the early `followed = null;` line:

```csharp
if (MapTutorial.Instance != null) MapTutorial.Instance.OnMapClosed();
```

- [ ] **Step 3.4: Append the 6 concrete step classes to `MapTutorial.cs`**

Append at the bottom of `MapTutorial.cs`:

```csharp
// Step 1: hold RMB and move the mouse — completes once the user has
// accumulated a noticeable amount of rotation while RMB is held.
class MapLookAroundStep : MapStep
{
    const float kRequiredRotationDeg = 60f;
    float _rotAccum;
    Quaternion _lastRot;
    bool _haveLast;
    public override string Tip =>
        "Right click on the screen and move mouse to look around.";
    public override void OnEnter() { _rotAccum = 0f; _haveLast = false; }
    public override void Tick()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl == null || ctrl.mapCamera == null) return;
        if (!Input.GetMouseButton(1)) { _haveLast = false; return; }
        var rot = ctrl.mapCamera.transform.rotation;
        if (_haveLast)
        {
            _rotAccum += Quaternion.Angle(_lastRot, rot);
            if (_rotAccum >= kRequiredRotationDeg) IsComplete = true;
        }
        _lastRot = rot;
        _haveLast = true;
    }
}

// Step 2: press W, A, S, D, Space, and Ctrl at least once each.
class MapMovementStep : MapStep
{
    bool _w, _a, _s, _d, _sp, _ct;
    public override string Tip
    {
        get
        {
            // Show progress so the player knows which keys remain.
            int done = (_w?1:0)+(_a?1:0)+(_s?1:0)+(_d?1:0)+(_sp?1:0)+(_ct?1:0);
            return $"Use <b>WASD</b> to move around, <b>Space</b> to go up, <b>Ctrl</b> to go down.\n<b>{done}/6</b> keys pressed.";
        }
    }
    public override void OnEnter() { _w = _a = _s = _d = _sp = _ct = false; }
    public override void Tick()
    {
        if (Input.GetKey(KeyCode.W)) _w = true;
        if (Input.GetKey(KeyCode.A)) _a = true;
        if (Input.GetKey(KeyCode.S)) _s = true;
        if (Input.GetKey(KeyCode.D)) _d = true;
        if (Input.GetKey(KeyCode.Space)) _sp = true;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) _ct = true;
        if (_w && _a && _s && _d && _sp && _ct) IsComplete = true;
    }
}

// Step 3: press G to lock the cursor.
class MapLockCursorStep : MapStep
{
    System.Action<bool> _handler;
    public override string Tip => "Press <b>G</b> to lock the cursor.";
    public override void OnEnter()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            _handler = locked => { if (locked) IsComplete = true; };
            ctrl.OnCursorLockChanged += _handler;
        }
    }
    public override void Tick() { }
    public override void OnExit()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null && _handler != null) ctrl.OnCursorLockChanged -= _handler;
        _handler = null;
    }
}

// Step 4: press G to unlock + click a planet in the legend.
class MapUnlockAndLegendClickStep : MapStep
{
    bool _unlocked, _legendClicked;
    System.Action<bool> _lockHandler;
    System.Action<CelestialBody> _legendHandler;
    public override string Tip
    {
        get
        {
            string a = _unlocked ? "<color=#5CC8FF>✓</color>" : "•";
            string b = _legendClicked ? "<color=#5CC8FF>✓</color>" : "•";
            return $"Press <b>G</b> to unlock the cursor, then click a planet in the legend.\n{a} unlock   {b} click";
        }
    }
    public override void OnEnter()
    {
        _unlocked = false; _legendClicked = false;
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            _lockHandler = locked => { if (!locked) _unlocked = true; };
            _legendHandler = body => _legendClicked = true;
            ctrl.OnCursorLockChanged += _lockHandler;
            ctrl.OnLegendBodyClicked += _legendHandler;
        }
    }
    public override void Tick()
    {
        if (_unlocked && _legendClicked) IsComplete = true;
    }
    public override void OnExit()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            if (_lockHandler   != null) ctrl.OnCursorLockChanged -= _lockHandler;
            if (_legendHandler != null) ctrl.OnLegendBodyClicked -= _legendHandler;
        }
        _lockHandler = null; _legendHandler = null;
    }
}

// Step 5: click the same legend planet again to mark+lock-on. The mark
// persists across map close (the highlight ring lives on
// SolarSystemMapController.pendingHighlight, which already survives
// OpenMap/CloseMap cycles).
class MapMarkPlanetStep : MapStep
{
    System.Action<CelestialBody> _handler;
    public override string Tip =>
        "Click the same planet again to <b>mark</b> it.\nMarking a planet persists when you close the map.";
    public override void OnEnter()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            _handler = body => IsComplete = true;
            ctrl.OnLegendBodyMarked += _handler;
        }
    }
    public override void Tick() { }
    public override void OnExit()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null && _handler != null) ctrl.OnLegendBodyMarked -= _handler;
        _handler = null;
    }
}

// Step 6: in-world LMB on a body to match velocity, then LMB off to unmatch.
class MapMatchVelocityStep : MapStep
{
    bool _matched, _unmatched;
    System.Action<CelestialBody> _matchHandler;
    System.Action _unmatchHandler;
    public override string Tip
    {
        get
        {
            string a = _matched ? "<color=#5CC8FF>✓</color>" : "•";
            string b = _unmatched ? "<color=#5CC8FF>✓</color>" : "•";
            return $"Look at a nearby planet or moon. <b>Left click</b> on it to match velocity.\nClick again off the planet to unmatch.\n{a} match   {b} unmatch";
        }
    }
    public override void OnEnter()
    {
        _matched = false; _unmatched = false;
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            _matchHandler = body => _matched = true;
            _unmatchHandler = () => { if (_matched) _unmatched = true; };
            ctrl.OnVelocityMatched += _matchHandler;
            ctrl.OnVelocityUnmatched += _unmatchHandler;
        }
    }
    public override void Tick()
    {
        if (_matched && _unmatched) IsComplete = true;
    }
    public override void OnExit()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            if (_matchHandler   != null) ctrl.OnVelocityMatched -= _matchHandler;
            if (_unmatchHandler != null) ctrl.OnVelocityUnmatched -= _unmatchHandler;
        }
        _matchHandler = null; _unmatchHandler = null;
    }
}
```

- [ ] **Step 3.5: Add public Instance accessor on SolarSystemMapController**

The controller has `Instance` already (line 5). Make sure it's still `public static SolarSystemMapController Instance { get; private set; }` — no change needed.

- [ ] **Step 3.6: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: no errors.

- [ ] **Step 3.7: Editor verification**

Enter Play, press M for the first time. Expect:
- "// MAP · 1/6 — Right click on the screen and move mouse to look around." in the top-right pill.
- Hold RMB, drag → after ~60° of rotation, audio chimes and step 2 appears.
- Press W,A,S,D,Space,Ctrl one at a time, watch counter increment.
- Press G — step 3 completes.
- Press G + click a legend entry — step 4 completes.
- Click the same legend entry — step 5 completes (camera focuses, ring stays).
- LMB on a planet in world → step 6 partial; click empty → step 6 completes.
- Verify M is blocked from closing during all of the above.

---

## Task 4: Velocity-matched UI toast component

**Files:**
- Create: `Assets/3 - Scripts/Map/MapVelocityHud.cs`

**Behavior:** A procedural ScreenSpaceOverlay canvas showing one centered line of text. Fades in over 0.18s when triggered, holds for 1.4s, fades out over 0.4s. New triggers reset the timer. Visible only while map is open.

- [ ] **Step 4.1: Create `MapVelocityHud.cs`**

```csharp
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Brief on-screen toast that displays "VELOCITY MATCHED — <name>" /
// "VELOCITY UNMATCHED" in map mode. Lives on a self-built ScreenSpaceOverlay
// canvas that the SolarSystemMapController enables on open and disables on
// close (so it never leaks into gameplay).
public class MapVelocityHud : MonoBehaviour
{
    Canvas canvas;
    CanvasGroup group;
    TextMeshProUGUI text;
    Coroutine routine;

    static readonly Color MatchedColor   = new Color(0.36f, 0.78f, 1f, 1f);
    static readonly Color UnmatchedColor = new Color(1f, 0.7f, 0.35f, 1f);

    void Awake()
    {
        Build();
        canvas.enabled = false;
    }

    public void SetVisible(bool visible)
    {
        if (canvas == null) return;
        canvas.enabled = visible;
        if (!visible && group != null) group.alpha = 0f;
    }

    public void ShowMatched(CelestialBody body)
    {
        string label = body != null && !string.IsNullOrEmpty(body.bodyName)
            ? $"VELOCITY MATCHED — {body.bodyName.ToUpperInvariant()}"
            : "VELOCITY MATCHED";
        ShowToast(label, MatchedColor);
    }

    public void ShowUnmatched()
    {
        ShowToast("VELOCITY UNMATCHED", UnmatchedColor);
    }

    void ShowToast(string label, Color color)
    {
        if (text == null || group == null) return;
        text.text = label;
        text.color = color;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ToastRoutine());
    }

    IEnumerator ToastRoutine()
    {
        const float fadeIn = 0.18f;
        const float hold   = 1.4f;
        const float fadeOut = 0.4f;
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(t / fadeIn);
            yield return null;
        }
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(t / fadeOut);
            yield return null;
        }
        group.alpha = 0f;
        routine = null;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above tutorial pill (830) so toast wins. Below pause menu (1000).
        canvas.sortingOrder = 850;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var rt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = new Vector2(0.5f, 0.86f);
        rt.anchorMax = new Vector2(0.5f, 0.86f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(800f, 56f);

        text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) text.font = font;
        text.text = "";
        text.fontSize = 28f;
        text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        text.alignment = TextAlignmentOptions.Center;
        text.characterSpacing = 4f;
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(0f, -2f);
    }
}
```

- [ ] **Step 4.2: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: no errors.

---

## Task 5: Wire MapVelocityHud into the map bootstrap + controller

**Files:**
- Modify: `Assets/3 - Scripts/Map/MapBootstrapReal.cs` — spawn the hud
- Modify: `Assets/3 - Scripts/Map/SolarSystemMapController.cs` — toggle hud visibility + drive on match/unmatch

- [ ] **Step 5.1: Add field + spawn block in `MapBootstrapReal.cs`**

In the controller, add the public field:

```csharp
public MapVelocityHud velocityHud;
```

In `MapBootstrapReal.Build`, after the orbit-lines block:

```csharp
var hudGO = new GameObject("MapVelocityHud");
if (uiSection != null) hudGO.transform.SetParent(uiSection, worldPositionStays: false);
var hud = hudGO.AddComponent<MapVelocityHud>();
controller.velocityHud = hud;
```

- [ ] **Step 5.2: Toggle visibility on map open / close**

In `OpenMap()`:
```csharp
if (velocityHud != null) velocityHud.SetVisible(true);
```

In `CloseMap()`:
```csharp
if (velocityHud != null) velocityHud.SetVisible(false);
```

- [ ] **Step 5.3: Drive the hud from the existing raycast block**

In the `Physics.Raycast` follow block in `LateUpdate` (already modified in Task 3), when firing `OnVelocityMatched` / `OnVelocityUnmatched`, also call:

```csharp
if (hitBody != null && followed != hitBody)
{
    followed = hitBody;
    followedLastPos = hitBody.transform.position;
    OnVelocityMatched?.Invoke(hitBody);
    if (velocityHud != null) velocityHud.ShowMatched(hitBody);
}
else if (hitBody == null)
{
    if (followed != null)
    {
        OnVelocityUnmatched?.Invoke();
        if (velocityHud != null) velocityHud.ShowUnmatched();
    }
    followed = null;
}
```

- [ ] **Step 5.4: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: no errors.

- [ ] **Step 5.5: Editor verification**

In Play mode, open the map, click a planet in world — toast `VELOCITY MATCHED — HUMBLE ABODE` fades in. Click empty space — `VELOCITY UNMATCHED` fades in. Close map → toast hidden.

---

## Task 6: Wire velocity-hud + tutorial events to MapTutorial step 6

No new code needed beyond Task 3 + Task 5 — the step's `OnVelocityMatched` / `OnVelocityUnmatched` event subscriptions already complete via the controller events.

- [ ] **Step 6.1: Sanity check**

Verify in Play mode that step 6 of the tutorial completes after one match + one unmatch, and that the velocity HUD toast continues to work *after* the tutorial finishes.

---

## Task 7: Save persistence for MapTutorial state

**Files:**
- Modify: `Assets/3 - Scripts/SaveSystem/SaveData.cs` — add `MapTutorialSave`
- Modify: `Assets/3 - Scripts/SaveSystem/SaveCollector.cs` — `CaptureMapTutorial` / `ApplyMapTutorial`

- [ ] **Step 7.1: Add `MapTutorialSave` schema**

In `SaveData.cs`, in the top class add the field:

```csharp
public MapTutorialSave mapTutorial = new MapTutorialSave();
```

At the bottom of the file (near `BonusTutorialSave`), add:

```csharp
[Serializable]
public class MapTutorialSave
{
    public bool finished;
    public int stepIndex = -1;
    public List<bool> stepsComplete = new List<bool>();
}
```

- [ ] **Step 7.2: Capture in `SaveCollector.Capture`**

Add to the Capture flow:

```csharp
CaptureMapTutorial(data.mapTutorial);
```

And the static method:

```csharp
static void CaptureMapTutorial(MapTutorialSave s)
{
    var t = MapTutorial.Instance;
    if (t == null) return;
    s.finished = t.GetFinished();
    s.stepIndex = t.GetStepIndex();
    s.stepsComplete = t.GetStepsComplete();
}
```

- [ ] **Step 7.3: Apply in `SaveCollector.Apply`**

Add to the Apply flow (after `ApplyBonusTutorial`):

```csharp
ApplyMapTutorial(data.mapTutorial);
```

And the static method:

```csharp
static void ApplyMapTutorial(MapTutorialSave s)
{
    var t = MapTutorial.Instance;
    if (t == null || s == null) return;
    t.ApplySaveState(s.finished, s.stepIndex, s.stepsComplete);
}
```

- [ ] **Step 7.4: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: no errors.

- [ ] **Step 7.5: Editor verification — save / load**

In Play mode: open map, get through tip 3, close map, open pause menu, save. Quit Play. Re-enter, load that save. Open map → tutorial resumes at tip 3 (or wherever you left off). Verify completed steps stay completed.

---

## Task 8: Seed MapTutorial in MainMenuController.EnsureGameplaySingletons

**Files:**
- Modify: `Assets/3 - Scripts/UI/MainMenuController.cs`

`MapTutorial` auto-creates via `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`, but when loading FROM the main menu, `SaveCollector.Apply` runs against `MapTutorial.Instance` 1 frame + 1 fixed-update after `sceneLoaded`. The auto-create runs at AfterSceneLoad, which is before that — so it should be fine. But to be safe (and per the CLAUDE.md convention that persistent singletons used by load are seeded in `EnsureGameplaySingletons`), pre-seed.

- [ ] **Step 8.1: Add seed line**

In `MainMenuController.EnsureGameplaySingletons`, locate the block that seeds `BonusTutorial`:

```csharp
if (BonusTutorial.Instance == null)
{
    var go = new GameObject("BonusTutorial");
    DontDestroyOnLoad(go);
    go.AddComponent<BonusTutorial>();
}
```

Add immediately after:

```csharp
if (MapTutorial.Instance == null)
{
    var go = new GameObject("MapTutorial");
    DontDestroyOnLoad(go);
    go.AddComponent<MapTutorial>();
}
```

- [ ] **Step 8.2: Compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: no errors.

---

## Task 9: Final compile + editor smoke test

- [ ] **Step 9.1: Final compile check**

```
mcp__coplay-mcp__check_compile_errors
```

Expected: clean.

- [ ] **Step 9.2: Run-through in Editor**

Hit Play. From a fresh state (no prior save):
1. Press M — tutorial tip 1 appears in top-right.
2. RMB-drag → tip 1 completes (chime), tip 2 appears.
3. WASD + Space + Ctrl → tip 2 completes, tip 3 appears.
4. G → tip 3 completes, tip 4 appears.
5. G + click a legend entry → tip 4 completes, tip 5 appears.
6. Click same legend entry → tip 5 completes (ring stays, camera focuses), tip 6 appears.
7. LMB on planet + LMB off → tip 6 completes, UI hides.
8. M closes the map normally now that tutorial is done.
9. While tutorial was running, M was a no-op (block worked).
10. Open map again — no tutorial reappears (Finished).
11. Save game from pause menu, quit Play, restart, load — map opens without tutorial.
12. Verify the camera no longer jitters when close to a planet.
13. Verify "VELOCITY MATCHED — <name>" and "VELOCITY UNMATCHED" toasts continue to appear on click after the tutorial is done.

---

## Self-Review Checklist

- [x] Jitter fix → Task 1.
- [x] Tip 1: look around with RMB → Task 3 (`MapLookAroundStep`).
- [x] Tip 2: WASD + Space + Ctrl → Task 3 (`MapMovementStep`).
- [x] Tip 3: press G to lock cursor → Task 3 (`MapLockCursorStep`).
- [x] Tip 4: press G to unlock + click a legend planet → Task 3 (`MapUnlockAndLegendClickStep`).
- [x] Tip 5: click same planet again to mark, persistent → Task 3 (`MapMarkPlanetStep`) + Task 3 second-click semantic change.
- [x] Tip 6: match velocity + unmatch → Task 3 (`MapMatchVelocityStep`).
- [x] Block M during tutorial → Task 3.2.
- [x] Velocity toast UI → Task 4 + Task 5.
- [x] Tutorial persists across save/load → Task 7.
- [x] Singleton seeded before scene load → Task 8.
- [x] UI matches existing TutorialUI (reuses `TutorialUI.ShowStep` / `SetTip` / pill styling) → Task 2.
- [x] No placeholders, every code block is concrete.
- [x] Type consistency: `MapTutorial.Instance` / `MapStep` / `MapVelocityHud.ShowMatched/ShowUnmatched` / event names match across tasks.
