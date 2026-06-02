using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }
    public bool Paused { get; set; }

    [Header("References")]
    public Ship ship;

    [Header("Pre-Crash Warning")]
    [Tooltip("Seconds after the scene starts before the HUD swings in showing the impact-imminent warning. Replaced by the post-crash tutorial when the ship actually crashes.")]
    public float preCrashWarningDelay = 5f;
    [TextArea(2, 5)]
    public string preCrashHeader = "<color=#FF7A40>⚠ WARNING · IMPACT IMMINENT</color>";
    [TextArea(2, 6)]
    public string preCrashTip = "Impact imminent — please assume crash positions!\nIt is recommended that you follow our post-crash tips to ensure a wonderful recovery!";

    Coroutine _preCrashCoroutine;

    List<TutorialStep> steps;
    int index = -1;
    bool tutorialStarted;
    bool tutorialFinished;
    string _lastShownTip;     // last value pushed to TutorialUI; tracked so we can
                              // re-push when input source changes (controller ↔ keyboard).

    // Duration tracking for the post-tutorial performance review. Accumulator
    // (not wall-clock) so a Paused window — e.g. when BonusTutorial takes over
    // for an axe / fishing offer — doesn't inflate the active step's time.
    readonly Dictionary<string, float> _stepDurations = new Dictionary<string, float>();
    float _currentStepElapsed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        steps = TutorialSteps.BuildDefault();

        // Opening revamp: do not lock abilities on spawn. The gate stays fully unlocked unless a
        // legacy tutorial is explicitly started (which the new flow never does).
    }

    void Start()
    {
        // Opening revamp: the forced tutorial is retired. Do NOT arm the crash-to-start
        // trigger or schedule the "Impact imminent / assume crash positions" pre-crash
        // warning — the phone-AI drives the opening now. (Leaving these on showed the old
        // warning uncancelled, since BeginTutorial — which used to cancel it — never runs.)
    }

    IEnumerator ShowPreCrashWarningRoutine()
    {
        yield return new WaitForSecondsRealtime(preCrashWarningDelay);
        if (tutorialStarted || tutorialFinished) yield break;
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(preCrashTip, 0, 0, preCrashHeader);
    }

    void OnDestroy()
    {
        if (ship != null) ship.OnShipCollision -= HandleShipCollision;
    }

    void HandleShipCollision(Collision col)
    {
        if (tutorialStarted) return;
        if (col.gameObject.GetComponentInParent<CelestialBody>() == null) return;
        BeginTutorial();
    }

    public void BeginTutorial()
    {
        if (tutorialStarted) return;
        tutorialStarted = true;
        if (_preCrashCoroutine != null)
        {
            StopCoroutine(_preCrashCoroutine);
            _preCrashCoroutine = null;
        }
        if (ship != null) ship.OnShipCollision -= HandleShipCollision;
        TutorialGate.LockAll();
        // Mouse-look / move / jump were taught by the first two tutorial steps
        // (WakeUpLookStep + WakeUpWalkStep). Those steps have been removed, so
        // unlock the abilities up-front instead — otherwise the player would
        // be unable to move toward the first remaining step (ReadNoteStep).
        TutorialGate.Unlock(TutorialAbility.MouseLook);
        TutorialGate.Unlock(TutorialAbility.Move);
        TutorialGate.Unlock(TutorialAbility.Jump);
        ShowStep(0);
    }

    void ShowStep(int newIndex)
    {
        if (index >= 0 && index < steps.Count) steps[index].OnExit();
        index = newIndex;

        if (index >= steps.Count)
        {
            FinishTutorial();
            return;
        }

        steps[index].OnEnter();
        _currentStepElapsed = 0f;
        _lastShownTip = steps[index].Tip;
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(_lastShownTip, index + 1, steps.Count);
    }

    void Update()
    {
        if (!tutorialStarted || tutorialFinished || Paused) return;
        if (index < 0 || index >= steps.Count) return;

        // Backspace skips just the CURRENT step (not the whole tutorial) so
        // playtesting doesn't have to repeat every action. ShowStep handles
        // the OnExit call on the outgoing step internally. If we run past the
        // last step, ShowStep falls through to FinishTutorial.
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            ShowStep(index + 1);
            return;
        }

        var step = steps[index];

        if (!step.IsComplete) step.Tick();

        // Step duration freezes the moment the player satisfies the step.
        // Without this, the typewriter delay (which is purely UI) would
        // inflate the per-step time used by the post-tutorial grade.
        if (!step.IsComplete) _currentStepElapsed += Time.unscaledDeltaTime;

        // Live-refresh the visible tip so glyphs update when the player switches
        // between keyboard and controller. Re-read the property each frame and
        // only push to the UI when the rendered string has actually changed.
        string tip = step.Tip;
        if (tip != _lastShownTip)
        {
            _lastShownTip = tip;
            if (TutorialUI.Instance != null) TutorialUI.Instance.SetTip(tip);
        }

        bool tipRevealing = TutorialUI.Instance != null && TutorialUI.Instance.IsTipRevealing;
        bool animating    = TutorialUI.Instance != null && TutorialUI.Instance.IsAnimating;

        // Block advance whenever the player is in any modal UI — dialogue,
        // cook panel, fishingdex, build menu, map, death screen — UNLESS the
        // step opted into AdvancesDuringModalUI (e.g. open-cook-panel, whose
        // follow-up instructs actions inside the panel that just opened).
        bool inOtherUI = PlayerController.isInDialogue || PlayerController.isMapOpen;
        bool blockingModal = inOtherUI && !step.AdvancesDuringModalUI;

        // Instant-advance: as soon as the step is satisfied, the tip has
        // finished typing, no slide/big-intro animation is in flight, and no
        // modal is in the way → play the completion sound and jump to the
        // next step. No TAB-to-skip prompt; the audio is the acknowledgment.
        if (step.IsComplete && !tipRevealing && !animating && !blockingModal)
        {
            string typeName = step.GetType().Name;
            if (!_stepDurations.ContainsKey(typeName))
                _stepDurations[typeName] = _currentStepElapsed;

            if (TutorialUI.Instance != null) TutorialUI.Instance.PlayCompleteSound();
            ShowStep(index + 1);
        }
    }

    void FinishTutorial()
    {
        tutorialFinished = true;
        TutorialGate.UnlockAll();
        if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();

        // Post-tutorial performance review. Build an ordered list of
        // (display-name, seconds) pairs in the order the player saw the steps,
        // skipping any whose timer wasn't recorded (e.g. Backspace skip).
        var entries = new List<TutorialPerformanceReview.Entry>();
        if (steps != null)
        {
            foreach (var s in steps)
            {
                if (s == null) continue;
                string typeName = s.GetType().Name;
                if (!_stepDurations.TryGetValue(typeName, out float seconds)) continue;
                entries.Add(new TutorialPerformanceReview.Entry
                {
                    label = TutorialPerformanceReview.PrettifyTypeName(typeName),
                    seconds = seconds,
                });
            }
        }
        if (TutorialPerformanceReview.Instance != null)
            TutorialPerformanceReview.Instance.Show(entries);
    }

    public void RestoreTutorialUI()
    {
        if (!tutorialStarted || tutorialFinished) return;
        if (index < 0 || index >= steps.Count) return;
        var step = steps[index];
        _lastShownTip = step.Tip;
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(_lastShownTip, index + 1, steps.Count);
        // If step is already complete on restore, the next Update tick will
        // instant-advance — no MarkComplete sub-line is shown anymore.
    }

    // ───── Save/Load ─────

    public bool IsStarted => tutorialStarted;
    public bool IsFinished => tutorialFinished;
    public int CurrentStepIndex => index;

    public string CurrentStepTypeName
    {
        get
        {
            if (steps == null || index < 0 || index >= steps.Count) return "";
            return steps[index].GetType().Name;
        }
    }

    public List<bool> GetStepCompletionFlags()
    {
        var flags = new List<bool>();
        if (steps != null)
            foreach (var s in steps) flags.Add(s.IsComplete);
        return flags;
    }

    public void ApplyState(bool started, bool finished, int currentIndex, List<bool> stepsComplete, string currentStepTypeName = "")
    {
        // Prefer resolving the saved step by type name — if the steps list has
        // been reordered or had members added/removed (e.g. removing the jetpack
        // tutorial steps from the intro), the int index is stale but the type
        // name still maps unambiguously.
        if (!string.IsNullOrEmpty(currentStepTypeName) && steps != null)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] != null && steps[i].GetType().Name == currentStepTypeName)
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        tutorialStarted = started;
        tutorialFinished = finished;
        if (steps != null && stepsComplete != null)
        {
            for (int i = 0; i < steps.Count && i < stepsComplete.Count; i++)
                steps[i].SetComplete(stepsComplete[i]);
        }

        // Suppress the auto-trigger on ship collision once we know the tutorial is past start.
        if (started && ship != null) ship.OnShipCollision -= HandleShipCollision;

        if (finished)
        {
            index = (steps != null) ? steps.Count : 0;
            if (TutorialUI.Instance != null) TutorialUI.Instance.HideAll();
            return;
        }

        if (started && currentIndex >= 0 && steps != null && currentIndex < steps.Count)
        {
            index = currentIndex;
            // OnEnter wires up listeners (e.g. HatchStep, LebronLightStep) and resets
            // internal trackers. We re-apply the saved IsComplete after, since OnEnter
            // alone never sets IsComplete=true.
            bool wasComplete = steps[index].IsComplete;
            steps[index].OnEnter();
            _currentStepElapsed = 0f;
            steps[index].SetComplete(wasComplete);
            _lastShownTip = steps[index].Tip;
            if (TutorialUI.Instance != null)
                TutorialUI.Instance.ShowStep(_lastShownTip, index + 1, steps.Count);
            // Complete-on-load steps will instant-advance on the next Update.
        }
        else
        {
            index = -1;
        }
    }
}
