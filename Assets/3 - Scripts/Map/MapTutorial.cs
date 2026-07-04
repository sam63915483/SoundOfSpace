using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Map-mode tutorial. Lives as a DontDestroyOnLoad singleton like BonusTutorial.
// Drives TutorialUI through six map-specific steps. Auto-starts on first map
// open. Pauses while the map is closed, resumes on reopen until all steps are
// done. Persists across save/load via MapTutorialSave.
public class MapTutorial : MonoBehaviour
{
    public static MapTutorial Instance { get; private set; }

    enum State { NotStarted, Running, Paused, Finished }
    State state = State.NotStarted;

    List<MapTutorialStep> steps;
    int stepIndex = -1;
    string _lastShownTip;
    const string Header = "MAP";

    // SolarSystemMapController polls this to suppress M-as-close while the
    // tutorial is in flight (either actively running, or paused mid-flow).
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

    /// Fired every time the player opens the map (before any state gating), so the optional
    /// on-ask map hint tip can dismiss itself once they've actually opened it.
    public static event System.Action OnOpened;

    // Called by SolarSystemMapController.OpenMap. First call kicks off the
    // tutorial; subsequent calls resume after a Pause (map close).
    public void OnMapOpened()
    {
        OnOpened?.Invoke();
        if (state == State.Finished) return;
        // Pill moves to the LEFT for the duration of map mode so it doesn't
        // collide with the right-anchored legend.
        if (TutorialUI.Instance != null) TutorialUI.Instance.SetLeftSide(true);
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

    // Called by SolarSystemMapController.CloseMap.
    public void OnMapClosed()
    {
        if (state == State.Running)
        {
            state = State.Paused;
            if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();
        }
        // Restore the pill to its default right-side anchor so the main /
        // bonus tutorials (and any HUD that relies on the same canvas) don't
        // inherit our left-side layout when the map closes.
        if (TutorialUI.Instance != null) TutorialUI.Instance.SetLeftSide(false);
    }

    static List<MapTutorialStep> BuildSteps()
    {
        return new List<MapTutorialStep>
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
            if (TutorialUI.Instance != null)
            {
                TutorialUI.Instance.HideAll();
                // Tutorial complete — release the pill from left-side so the
                // main / bonus tutorials (or anything reusing this canvas)
                // return to their normal right-anchored layout.
                TutorialUI.Instance.SetLeftSide(false);
            }
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

        // Backspace = skip the current tip. Each press completes one step,
        // so five presses through the six-step tutorial dismisses the whole
        // thing. Only fires while the map is actually open — pressing
        // Backspace in gameplay does nothing.
        if (PlayerController.isMapOpen && Input.GetKeyDown(KeyCode.Backspace))
        {
            step.SetComplete(true);
        }

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

    // ── Save / Load ───────────────────────────────────────────────────────
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

public abstract class MapTutorialStep
{
    public abstract string Tip { get; }
    public bool IsComplete { get; protected set; }
    public void SetComplete(bool v) { IsComplete = v; }
    public virtual void OnEnter() { }
    public abstract void Tick();
    public virtual void OnExit() { }
}

// ── Concrete steps ─────────────────────────────────────────────────────────

// Step 1: hold RMB + move mouse. Completes after the camera has rotated by a
// total threshold while RMB is held.
class MapLookAroundStep : MapTutorialStep
{
    const float kRequiredRotationDeg = 60f;
    float _rotAccum;
    Quaternion _lastRot;
    bool _haveLast;
    public override string Tip =>
        TutorialGate.LastSource == TutorialGate.InputSource.Controller
            ? $"Move {PromptGlyphs.MouseLook} to look around."
            : "<b>Right click</b> on the screen and move <b>mouse</b> to look around.";
    public override void OnEnter() { _rotAccum = 0f; _haveLast = false; }
    public override void Tick()
    {
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl == null || ctrl.mapCamera == null) return;
        bool padLooking = Mathf.Abs(TutorialGate.RightStickX()) > 0.25f ||
                          Mathf.Abs(TutorialGate.RightStickY()) > 0.25f;
        if (!Input.GetMouseButton(1) && !padLooking) { _haveLast = false; return; }
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

// Step 2: WASD + Space + Ctrl all pressed at least once.
class MapMovementStep : MapTutorialStep
{
    bool _w, _a, _s, _d, _sp, _ct;
    public override string Tip
    {
        get
        {
            int done = (_w?1:0)+(_a?1:0)+(_s?1:0)+(_d?1:0)+(_sp?1:0)+(_ct?1:0);
            if (TutorialGate.LastSource == TutorialGate.InputSource.Controller)
                return $"Push {PromptGlyphs.Move} to move around, {PromptGlyphs.Jump} to go up, {PromptGlyphs.SecondaryFire} to go down.\n<b>{done}/6</b> directions used.";
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
        // Pad equivalents — mirrors MapCameraRig's bindings exactly
        // (left stick pan, A up, LT down).
        float mh = TutorialGate.MoveAxisHorizontal(TutorialAbility.Map);
        float mv = TutorialGate.MoveAxisVertical(TutorialAbility.Map);
        if (mh < -0.25f) _a = true;
        if (mh >  0.25f) _d = true;
        if (mv >  0.25f) _w = true;
        if (mv < -0.25f) _s = true;
        if (TutorialGate.PadHeld(TutorialGate.PadButton.A)) _sp = true;
        if (TutorialGate.ControllerEnabled && TutorialGate.LTValue() > TutorialGate.TriggerThreshold) _ct = true;
        if (_w && _a && _s && _d && _sp && _ct) IsComplete = true;
    }
}

// Step 3: press G to lock the cursor.
class MapLockCursorStep : MapTutorialStep
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

// Step 4: press G to unlock + click a legend planet. Order doesn't matter.
class MapUnlockAndLegendClickStep : MapTutorialStep
{
    bool _unlocked, _legendClicked;
    System.Action<bool> _lockHandler;
    System.Action<CelestialBody> _legendHandler;
    public override string Tip
    {
        get
        {
            string a = _unlocked       ? "<color=#5CC8FF>✓</color>" : "•";
            string b = _legendClicked  ? "<color=#5CC8FF>✓</color>" : "•";
            return $"Press <b>G</b> to unlock the cursor, then click a planet in the legend to <b>mark</b> it.\n{a} unlock   {b} click";
        }
    }
    public override void OnEnter()
    {
        _unlocked = false; _legendClicked = false;
        var ctrl = SolarSystemMapController.Instance;
        if (ctrl != null)
        {
            _lockHandler   = locked => { if (!locked) _unlocked = true; };
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

// Step 5: click the same legend planet again to mark+focus. The "mark" is the
// in-world highlight ring, which now persists past the 2nd click and through
// map open/close cycles via SolarSystemMapController.pendingHighlight.
class MapMarkPlanetStep : MapTutorialStep
{
    System.Action<CelestialBody> _handler;
    public override string Tip =>
        "Click the same planet again to <b>fly to it</b>.\nThe mark stays until you click again to unmark.";
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

// Step 6: look at a nearby body, LMB to match velocity, LMB off to unmatch.
class MapMatchVelocityStep : MapTutorialStep
{
    bool _matched, _unmatched;
    System.Action<CelestialBody> _matchHandler;
    System.Action _unmatchHandler;
    public override string Tip
    {
        get
        {
            string a = _matched   ? "<color=#5CC8FF>✓</color>" : "•";
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
            _matchHandler   = body => _matched = true;
            _unmatchHandler = () => { if (_matched) _unmatched = true; };
            ctrl.OnVelocityMatched   += _matchHandler;
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
            if (_matchHandler   != null) ctrl.OnVelocityMatched   -= _matchHandler;
            if (_unmatchHandler != null) ctrl.OnVelocityUnmatched -= _unmatchHandler;
        }
        _matchHandler = null; _unmatchHandler = null;
    }
}
