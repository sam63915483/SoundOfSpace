using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BonusTutorial : MonoBehaviour
{
    public static BonusTutorial Instance { get; private set; }

    // Idle: nothing happening.
    // OfferingPopup: yes/no popup is up.
    // Transitioning: TutorialUI is mid swing-out / swing-in between main and bonus.
    // RunningStep: bonus tutorial is active (TutorialManager is paused).
    // FinalMessage: last step done; performance review is up (or about to be).
    enum State { Idle, OfferingPopup, Transitioning, RunningStep, FinalMessage }
    State state = State.Idle;

    Canvas canvas;
    GameObject popupRoot;
    TextMeshProUGUI questionText;

    List<BonusStep> steps;
    int stepIndex = -1;
    bool advanceArmed;
    string _lastShownTip;
    string _activeHeader = "AXE / BUILDING";

    // Per-step duration tracking, mirrors TutorialManager. _currentStepElapsed
    // freezes the moment a step is satisfied so the typewriter delay doesn't
    // inflate the bonus performance-review grade.
    readonly Dictionary<string, float> _stepDurations = new Dictionary<string, float>();
    float _currentStepElapsed;

    CursorLockMode prevLockMode;
    bool prevCursorVisible;

    System.Func<List<BonusStep>> _pendingStepsFactory;

    static readonly Color32 C_CardBg     = new Color32(0x12, 0x07, 0x2C, 0xF2);
    static readonly Color32 C_BorderCool = new Color32(0x5B, 0xD8, 0xFF, 0xFF);
    static readonly Color32 C_Tip        = new Color32(0xF1, 0xF4, 0xFF, 0xFF);
    static readonly Color32 C_BtnYes     = new Color32(0x2F, 0x70, 0xC8, 0xFF);
    static readonly Color32 C_BtnYesH    = new Color32(0x4A, 0x8E, 0xE6, 0xFF);
    static readonly Color32 C_BtnNo      = new Color32(0x60, 0x40, 0x80, 0xFF);
    static readonly Color32 C_BtnNoH     = new Color32(0x80, 0x60, 0xA0, 0xFF);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("BonusTutorial");
        DontDestroyOnLoad(go);
        go.AddComponent<BonusTutorial>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void OfferAxeBuilding()
    {
        if (Instance == null) return;
        Instance.Begin(
            "Would you like to do the axe / building tutorial?",
            "AXE / BUILDING",
            () => new List<BonusStep>
            {
                new SwingAxeStep(),
                new GatherWoodStep(),
                new BuildBonfireStep(),
                new BuildCabinStep()
            });
    }

    public static void OfferFishing()
    {
        if (Instance == null) return;
        Instance.Begin(
            "Do you want to do the fishing tutorial?",
            "FISHING",
            () => new List<BonusStep>
            {
                new CastBobberStep(),
                new BonusCatchFishStep(),
                new SpinCatchStep(),
                new OpenFishingdexStep(),
                new FishingExtraInfoStep()
            });
    }

    public static void OfferPistol()
    {
        if (Instance == null) return;
        Instance.Begin(
            "Do you want to do the pistol tutorial?",
            "PISTOL",
            () => new List<BonusStep>
            {
                new EquipPistolStep(),
                new FirePistolStep(),
                new ReloadPistolStep()
            });
    }

    public static void OfferJetpack()
    {
        if (Instance == null) return;
        Instance.Begin(
            "Do you want to do the jetpack tutorial?",
            "JETPACK",
            () => new List<BonusStep>
            {
                new JetpackUpThrustStep(),
                new JetpackDownThrustStep(),
                new JetpackDirectionalThrustStep()
            });
    }

    // Triggered after the player buys / steals back the missing ship part —
    // the intro tutorial intentionally stops at "Talk to NPCs" without any
    // flight tips, and the player gets the flight tutorial as an opt-in popup
    // here once flight actually unlocks for them. Caller is whatever NPC /
    // shop / quest hands the part over.
    public static void OfferShipFlight()
    {
        if (Instance == null) return;
        Instance.Begin(
            "Do you want to do the ship flight tutorial?",
            "SHIP FLIGHT",
            () => new List<BonusStep>
            {
                new ShipPilotBonusStep(),
                new ShipUpThrustBonusStep(),
                new ShipMoveBonusStep(),
                new ShipDownThrustBonusStep(),
                new ShipRollBonusStep(),
            });
    }

    void Begin(string question, string header, System.Func<List<BonusStep>> stepsFactory)
    {
        if (state != State.Idle) return;
        _activeHeader = header;
        _pendingStepsFactory = stepsFactory;
        if (questionText != null) questionText.text = question;
        ShowOfferPopup();
    }

    void ShowOfferPopup()
    {
        state = State.OfferingPopup;
        popupRoot.SetActive(true);
        prevLockMode = Cursor.lockState;
        prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerController.isInDialogue = true;
    }

    void OnYesClicked()
    {
        popupRoot.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlayerController.isInDialogue = false;
        StartBonusSteps();
    }

    void OnNoClicked()
    {
        popupRoot.SetActive(false);
        Cursor.lockState = prevLockMode == CursorLockMode.None ? CursorLockMode.Locked : prevLockMode;
        Cursor.visible = false;
        PlayerController.isInDialogue = false;
        state = State.Idle;
    }

    void StartBonusSteps()
    {
        steps = _pendingStepsFactory != null ? _pendingStepsFactory() : new List<BonusStep>();
        _pendingStepsFactory = null;
        stepIndex = -1;
        _stepDurations.Clear();
        _currentStepElapsed = 0f;
        // Pause the main tutorial so its per-step timer doesn't tick while
        // the bonus tutorial is active — bonus tutorials live inside the
        // talk-to-NPCs window, and otherwise their playtime would inflate
        // the main tutorial's grade.
        if (TutorialManager.Instance != null) TutorialManager.Instance.Paused = true;

        // Animate the main tutorial HUD off-screen, then animate the bonus
        // HUD back on with the first bonus step's content. The user-facing
        // effect is "main panel swings off, mini panel swings on".
        state = State.Transitioning;
        if (TutorialUI.Instance != null)
        {
            TutorialUI.Instance.SwingOff(() =>
            {
                EnterStep(0);
                // EnterStep → ShowStep starts the BigEntry on its own; no
                // SwingOn needed (BigEntry handles the on-screen entrance).
                state = State.RunningStep;
            });
        }
        else
        {
            EnterStep(0);
            state = State.RunningStep;
        }
    }

    void AdvanceStep()
    {
        if (steps != null && stepIndex >= 0 && stepIndex < steps.Count) steps[stepIndex].OnExit();
        EnterStep(stepIndex + 1);
    }

    void EnterStep(int newIndex)
    {
        stepIndex = newIndex;
        advanceArmed = false;
        if (stepIndex >= steps.Count)
        {
            BeginFinalMessage();
            return;
        }
        var step = steps[stepIndex];
        step.OnEnter();
        _currentStepElapsed = 0f;
        if (state != State.Transitioning) state = State.RunningStep;
        _lastShownTip = step.Tip;
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(_lastShownTip, stepIndex + 1, steps.Count,
                $"{_activeHeader}  ·  {stepIndex + 1} / {steps.Count}");
    }

    void BeginFinalMessage()
    {
        state = State.FinalMessage;
        // Swing the bonus HUD off-screen, then open the bonus performance
        // review. After Continue, swing the main HUD back into view.
        if (TutorialUI.Instance != null)
        {
            TutorialUI.Instance.SwingOff(ShowBonusReview);
        }
        else
        {
            ShowBonusReview();
        }
    }

    void ShowBonusReview()
    {
        // Build entries in step order, skipping any whose timer wasn't
        // recorded (defensive — should never happen in normal flow).
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
        var thresholds = TutorialPerformanceReview.GradeThresholds.Mini(entries.Count);
        if (TutorialPerformanceReview.Instance != null)
        {
            TutorialPerformanceReview.Instance.Show(
                entries,
                thresholds,
                OnBonusReviewContinue,
                $"{_activeHeader} PERFORMANCE REVIEW");
        }
        else
        {
            // No review available — fall through directly to the main-tutorial restore.
            OnBonusReviewContinue();
        }
    }

    void OnBonusReviewContinue()
    {
        // The bonus review's Continue handler delegated cursor unlock to us;
        // re-lock now that the modal closed.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlayerController.isInDialogue = false;

        // If the main tutorial finished while the bonus was running, just
        // leave the HUD off and go idle.
        if (TutorialManager.Instance == null || TutorialManager.Instance.IsFinished)
        {
            state = State.Idle;
            return;
        }

        // Restore the main HUD: set its content into the (still off-screen)
        // panel, then swing it back on so the player sees it sweep into place.
        TutorialManager.Instance.Paused = false;
        TutorialManager.Instance.RestoreTutorialUI();
        // RestoreTutorialUI → ShowStep starts the BigEntry on its own; no
        // SwingOn needed (BigEntry handles the on-screen entrance).
        state = State.Idle;
    }

    void Update()
    {
        if (state != State.RunningStep) return;
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Count) return;
        var step = steps[stepIndex];
        if (!step.IsComplete) step.Tick();

        // Step duration freezes at satisfaction so the typewriter delay
        // doesn't penalize the bonus performance-review grade.
        if (!step.IsComplete) _currentStepElapsed += Time.unscaledDeltaTime;

        string tip = step.Tip;
        if (tip != _lastShownTip)
        {
            _lastShownTip = tip;
            if (TutorialUI.Instance != null) TutorialUI.Instance.SetTip(tip);
        }

        bool tipRevealing = TutorialUI.Instance != null && TutorialUI.Instance.IsTipRevealing;
        bool animating    = TutorialUI.Instance != null && TutorialUI.Instance.IsAnimating;
        bool inOtherUI    = PlayerController.isInDialogue || PlayerController.isMapOpen;

        // Instant-advance once the step is satisfied + the tip has finished
        // typing + no slide animation is in flight + no modal is occluding.
        // Audio is the acknowledgment; no TAB-to-skip prompt anymore.
        if (step.IsComplete && !tipRevealing && !animating && !inOtherUI)
        {
            string typeName = step.GetType().Name;
            if (!_stepDurations.ContainsKey(typeName))
                _stepDurations[typeName] = _currentStepElapsed;

            if (TutorialUI.Instance != null) TutorialUI.Instance.PlayCompleteSound();
            AdvanceStep();
        }
    }

    // ───── Save/Load ─────

    public string GetActiveTutorialKey()
    {
        if (state != State.RunningStep || steps == null || steps.Count == 0) return "";
        // Identify by header — set in Begin(), preserved across steps.
        if (_activeHeader == "AXE / BUILDING") return "axe-building";
        if (_activeHeader == "FISHING") return "fishing";
        if (_activeHeader == "PISTOL") return "pistol";
        if (_activeHeader == "JETPACK") return "jetpack";
        if (_activeHeader == "SHIP FLIGHT") return "ship-flight";
        return "";
    }

    public int GetStepIndex() => stepIndex;
    public bool GetAdvanceArmed() => advanceArmed;

    public List<bool> GetStepsComplete()
    {
        var list = new List<bool>();
        if (steps != null) foreach (var s in steps) list.Add(s.IsComplete);
        return list;
    }

    public void ApplySaveState(string activeKey, int idx, List<bool> stepsCompleteList, bool armed)
    {
        if (string.IsNullOrEmpty(activeKey))
        {
            state = State.Idle;
            return;
        }

        switch (activeKey)
        {
            case "axe-building":
                steps = new List<BonusStep>
                {
                    new SwingAxeStep(),
                    new GatherWoodStep(),
                    new BuildBonfireStep(),
                    new BuildCabinStep()
                };
                _activeHeader = "AXE / BUILDING";
                break;
            case "fishing":
                steps = new List<BonusStep>
                {
                    new CastBobberStep(),
                    new BonusCatchFishStep(),
                    new SpinCatchStep(),
                    new OpenFishingdexStep(),
                    new FishingExtraInfoStep()
                };
                _activeHeader = "FISHING";
                break;
            case "pistol":
                steps = new List<BonusStep>
                {
                    new EquipPistolStep(),
                    new FirePistolStep(),
                    new ReloadPistolStep()
                };
                _activeHeader = "PISTOL";
                break;
            case "jetpack":
                steps = new List<BonusStep>
                {
                    new JetpackUpThrustStep(),
                    new JetpackDownThrustStep(),
                    new JetpackDirectionalThrustStep()
                };
                _activeHeader = "JETPACK";
                break;
            case "ship-flight":
                steps = new List<BonusStep>
                {
                    new ShipPilotBonusStep(),
                    new ShipUpThrustBonusStep(),
                    new ShipMoveBonusStep(),
                    new ShipDownThrustBonusStep(),
                    new ShipRollBonusStep(),
                };
                _activeHeader = "SHIP FLIGHT";
                break;
            default:
                return;
        }

        if (TutorialManager.Instance != null) TutorialManager.Instance.Paused = true;

        if (stepsCompleteList != null)
        {
            for (int i = 0; i < steps.Count && i < stepsCompleteList.Count; i++)
                steps[i].SetComplete(stepsCompleteList[i]);
        }

        stepIndex = idx;
        if (idx < 0 || idx >= steps.Count)
        {
            state = State.Idle;
            return;
        }

        var step = steps[idx];
        step.OnEnter();
        // OnEnter resets some step state (e.g. clears _talkedTo). Re-apply saved completion.
        if (stepsCompleteList != null && idx < stepsCompleteList.Count)
            step.SetComplete(stepsCompleteList[idx]);
        advanceArmed = armed;
        state = State.RunningStep;
        _lastShownTip = step.Tip;
        if (TutorialUI.Instance != null)
            TutorialUI.Instance.ShowStep(_lastShownTip, idx + 1, steps.Count, $"{_activeHeader}  ·  {idx + 1} / {steps.Count}");
        // Complete-on-load steps will instant-advance on the next Update.
    }

    void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        HUDSceneGate.Register(canvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        BuildPopup();
        popupRoot.SetActive(false);
    }

    void BuildPopup()
    {
        var dim = NewUI("PopupDim", transform);
        Stretch(dim, 0, 0, 0, 0);
        var dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);
        popupRoot = dim.gameObject;

        var card = NewUI("Card", dim);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        // Taller than before to make room for the ORG fine-print line under
        // the question. 280 → 360.
        card.sizeDelta = new Vector2(680f, 360f);
        card.anchoredPosition = Vector2.zero;
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = C_CardBg;
        var cardShadow = card.gameObject.AddComponent<Shadow>();
        cardShadow.effectColor = new Color(0.4f, 0.15f, 0.7f, 0.65f);
        cardShadow.effectDistance = new Vector2(0f, -6f);

        var accent = NewUI("Accent", card);
        accent.anchorMin = new Vector2(0f, 1f);
        accent.anchorMax = new Vector2(1f, 1f);
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = new Vector2(0f, -2f);
        accent.sizeDelta = new Vector2(-44f, 4f);
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.color = C_BorderCool;

        questionText = NewText("Question", card,
            "Would you like to do the tutorial?",
            28f, FontStyles.Bold, C_Tip, TextAlignmentOptions.Center);
        var title = questionText;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(-50f, 100f);
        trt.anchoredPosition = new Vector2(0f, -32f);
        title.enableWordWrapping = true;

        // Fine-print disclaimer below the question — sets the comedic tone
        // (the ORG bit pays off later when the player meets ORG NPCs).
        var disclaimer = NewText("Disclaimer", card,
            "<i>By pressing Yes you agree to give up your data to ORG and whomever they decide to share it with.</i>",
            14f, FontStyles.Italic, new Color32(0xC8, 0xCC, 0xD8, 0xCC), TextAlignmentOptions.Center);
        var drt = disclaimer.rectTransform;
        drt.anchorMin = new Vector2(0f, 1f);
        drt.anchorMax = new Vector2(1f, 1f);
        drt.pivot = new Vector2(0.5f, 1f);
        drt.sizeDelta = new Vector2(-60f, 70f);
        drt.anchoredPosition = new Vector2(0f, -140f);
        disclaimer.enableWordWrapping = true;

        var noBtn = NewButton("NoBtn", card, "No", C_BtnNo, C_BtnNoH);
        var nrt = noBtn.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 0f);
        nrt.anchorMax = new Vector2(0.5f, 0f);
        nrt.pivot = new Vector2(0.5f, 0f);
        nrt.anchoredPosition = new Vector2(0f, 30f);
        nrt.sizeDelta = new Vector2(-80f, 70f);
        noBtn.onClick.AddListener(OnNoClicked);

        var yesBtn = NewButton("YesBtn", card, "Yes", C_BtnYes, C_BtnYesH);
        var yrt = yesBtn.GetComponent<RectTransform>();
        yrt.anchorMin = new Vector2(0.5f, 0f);
        yrt.anchorMax = new Vector2(1f, 0f);
        yrt.pivot = new Vector2(0.5f, 0f);
        yrt.anchoredPosition = new Vector2(0f, 30f);
        yrt.sizeDelta = new Vector2(-80f, 70f);
        yesBtn.onClick.AddListener(OnYesClicked);
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    static Button NewButton(string name, Transform parent, string label, Color32 normal, Color32 hover)
    {
        var rt = NewUI(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = normal;
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.pressedColor = new Color32((byte)(normal.r * 0.7f), (byte)(normal.g * 0.7f), (byte)(normal.b * 0.7f), 255);
        colors.selectedColor = hover;
        btn.colors = colors;

        var text = NewText("Label", rt, label, 28f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 0, 0, 0, 0);
        return btn;
    }
}

abstract class BonusStep
{
    public abstract string Tip { get; }
    public bool IsComplete { get; protected set; }
    public virtual void OnEnter() { }
    public abstract void Tick();
    public virtual void OnExit() { }
    public void SetComplete(bool v) { IsComplete = v; }
}

class SwingAxeStep : BonusStep
{
    public override string Tip => $"Equip the axe (hotbar) and {PromptGlyphs.PrimaryClick} to swing.";
    public override void Tick()
    {
        var axe = Object.FindObjectOfType<AxeController>();
        if (axe != null && axe.IsEquipped && TutorialGate.FirePressed()) IsComplete = true;
    }
}

class GatherWoodStep : BonusStep
{
    const int Required = 60;
    public override string Tip
    {
        get
        {
            int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
            int shown = Mathf.Min(wood, Required);
            return $"Gather <b>{Required}</b> wood by chopping trees.\n<b>{shown}/{Required}</b> gathered.";
        }
    }
    public override void Tick()
    {
        int wood = WoodInventory.Instance != null ? WoodInventory.Instance.Wood : 0;
        if (wood >= Required) IsComplete = true;
    }
}

class BuildBonfireStep : BonusStep
{
    System.Action<BuildableEntry> handler;
    public override string Tip =>
        $"Press {PromptGlyphs.BuildMenu} to open the building menu and build a bonfire.\nUse {PromptGlyphs.PlacementRotate} to rotate placements, {PromptGlyphs.PrimaryFire} to place.";
    public override void OnEnter()
    {
        handler = e => { if (e != null && e.displayName == "Bonfire") IsComplete = true; };
        GhostPlacement.OnPlaced += handler;
    }
    public override void Tick() { }
    public override void OnExit()
    {
        if (handler != null) GhostPlacement.OnPlaced -= handler;
        handler = null;
    }
}

class BuildCabinStep : BonusStep
{
    System.Action<BuildableEntry> handler;
    public override string Tip => "Place a cabin.";
    public override void OnEnter()
    {
        handler = e => { if (e != null && e.displayName == "Cabin") IsComplete = true; };
        GhostPlacement.OnPlaced += handler;
    }
    public override void Tick() { }
    public override void OnExit()
    {
        if (handler != null) GhostPlacement.OnPlaced -= handler;
        handler = null;
    }
}

class CastBobberStep : BonusStep
{
    System.Action handler;
    public override string Tip => $"{PromptGlyphs.PrimaryClickCap} to cast the bobber.";
    public override void OnEnter()
    {
        handler = () => IsComplete = true;
        FishingRodController.OnBobberCast += handler;
    }
    public override void Tick() { }
    public override void OnExit()
    {
        if (handler != null) FishingRodController.OnBobberCast -= handler;
        handler = null;
    }
}

class BonusCatchFishStep : BonusStep
{
    System.Action<float> handler;
    public override string Tip => $"Wait for the bobber to move, then {PromptGlyphs.PrimaryClick} to reel it in.\nYou have to be quick!";
    public override void OnEnter()
    {
        handler = spin => IsComplete = true;
        FishingRodController.OnFishCaught += handler;
    }
    public override void Tick() { }
    public override void OnExit()
    {
        if (handler != null) FishingRodController.OnFishCaught -= handler;
        handler = null;
    }
}

class SpinCatchStep : BonusStep
{
    System.Action<float> handler;
    public override string Tip =>
        $"You can perform <b>skilled catches</b> by jumping after the bobber starts to move,\nspin around, then {PromptGlyphs.PrimaryClick} to reel in. You must be very fast!";
    public override void OnEnter()
    {
        handler = spin => { if (spin >= 10f) IsComplete = true; };
        FishingRodController.OnFishCaught += handler;
    }
    public override void Tick() { }
    public override void OnExit()
    {
        if (handler != null) FishingRodController.OnFishCaught -= handler;
        handler = null;
    }
}

class OpenFishingdexStep : BonusStep
{
    System.Action handler;
    public override string Tip => $"Press {PromptGlyphs.Fishingdex} to open the FishingDex to see what kinds of fish you caught and their stats.";
    public override void OnEnter()
    {
        handler = () => IsComplete = true;
        FishingdexManager.OnFishingdexOpened += handler;
    }
    public override void Tick() { }
    public override void OnExit()
    {
        if (handler != null) FishingdexManager.OnFishingdexOpened -= handler;
        handler = null;
    }
}

class FishingExtraInfoStep : BonusStep
{
    float _enterTime;
    public override string Tip =>
        "You can <b>cook and eat fish</b> at bonfires.\nYou can also <b>sell fish</b> at the fish market.";
    public override void OnEnter() { _enterTime = Time.unscaledTime; }
    public override void Tick()
    {
        if (Time.unscaledTime - _enterTime >= 5f) IsComplete = true;
    }
}

class EquipPistolStep : BonusStep
{
    public override string Tip => "Equip the pistol from the hotbar.";
    public override void Tick()
    {
        var pistol = Object.FindObjectOfType<PistolController>();
        if (pistol != null && pistol.IsEquipped) IsComplete = true;
    }
}

class FirePistolStep : BonusStep
{
    const int Required = 10;
    int _startCount;
    PistolController _pistol;
    public override string Tip
    {
        get
        {
            int fired = 0;
            if (_pistol != null) fired = Mathf.Clamp(_pistol.ShotsFiredCount - _startCount, 0, Required);
            return $"{PromptGlyphs.PrimaryClickCap} to fire.\n<b>{fired}/{Required}</b> shots fired.";
        }
    }
    public override void OnEnter()
    {
        _pistol = Object.FindObjectOfType<PistolController>();
        _startCount = _pistol != null ? _pistol.ShotsFiredCount : 0;
    }
    public override void Tick()
    {
        if (_pistol == null) _pistol = Object.FindObjectOfType<PistolController>();
        if (_pistol == null) return;
        if (_pistol.ShotsFiredCount - _startCount >= Required) IsComplete = true;
    }
}

class ReloadPistolStep : BonusStep
{
    public override string Tip => "Press <b>R</b> to reload.";
    public override void Tick()
    {
        if (Input.GetKeyDown(KeyCode.R)) IsComplete = true;
    }
}

class JetpackUpThrustStep : BonusStep
{
    PlayerController player;
    public override string Tip => $"While airborne, press {PromptGlyphs.Jump} again to boost upward.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.Jump);
        TutorialGate.Unlock(TutorialAbility.Boost);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        bool jumpDown = Input.GetKeyDown(KeyCode.Space) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.A);
        if (jumpDown && !player.IsOnGround) IsComplete = true;
    }
}

class JetpackDownThrustStep : BonusStep
{
    PlayerController player;
    public override string Tip => $"Press {PromptGlyphs.DownThrust} mid-air to thrust downward.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.DownThrust);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        bool downThrustDown = Input.GetKeyDown(KeyCode.LeftControl) ||
            TutorialGate.PadPressed(TutorialGate.PadButton.R3);
        if (downThrustDown && !player.IsOnGround) IsComplete = true;
    }
}

class JetpackDirectionalThrustStep : BonusStep
{
    PlayerController player;
    public override string Tip => $"Jump, then hold {PromptGlyphs.DirThrustHold} to thrust in that direction.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.DirectionalThrust);
        player = Object.FindObjectOfType<PlayerController>();
    }
    public override void Tick()
    {
        if (player == null) player = Object.FindObjectOfType<PlayerController>();
        if (player == null) return;
        bool dirThrustHeld = Input.GetKey(KeyCode.LeftShift) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.L3);
        bool anyWasd = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                       Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        if (!player.IsOnGround && dirThrustHeld && anyWasd) IsComplete = true;
    }
}

// Ship-flight bonus steps. Mirror the legacy PilotShipStep / Ship*Step classes
// in TutorialSteps.cs but inherit BonusStep so BonusTutorial.Update can drive
// them. Lazy ship lookup tolerates the instance being destroyed and respawned.
static class BonusShipFinder
{
    public static Ship Get(ref Ship cached)
    {
        if (cached == null) cached = Object.FindObjectOfType<Ship>();
        return cached;
    }
}

class ShipPilotBonusStep : BonusStep
{
    Ship ship;
    public override string Tip => $"Press {PromptGlyphs.Interact} in the pilot seat to fly the ship.";
    public override void OnEnter()
    {
        TutorialGate.Unlock(TutorialAbility.EnterPilot);
        TutorialGate.Unlock(TutorialAbility.ShipMouseLook);
        ship = null;
    }
    public override void Tick()
    {
        var s = BonusShipFinder.Get(ref ship);
        if (s != null && s.IsPiloted) IsComplete = true;
    }
}

class ShipUpThrustBonusStep : BonusStep
{
    Ship ship;
    public override string Tip => $"Hold {PromptGlyphs.Jump} for upward thrust.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipUpThrust); ship = null; }
    public override void Tick()
    {
        var s = BonusShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool up = Input.GetKey(KeyCode.Space) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.A);
        if (up) IsComplete = true;
    }
}

class ShipMoveBonusStep : BonusStep
{
    Ship ship;
    public override string Tip => $"Use {PromptGlyphs.Move} to fly the ship.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipMove); ship = null; }
    public override void Tick()
    {
        var s = BonusShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool anyMove = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                       Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        if (TutorialGate.ControllerEnabled)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            if (h * h + v * v > TutorialGate.StickDeadzone * TutorialGate.StickDeadzone) anyMove = true;
        }
        if (anyMove) IsComplete = true;
    }
}

class ShipDownThrustBonusStep : BonusStep
{
    Ship ship;
    public override string Tip => $"Hold {PromptGlyphs.DownThrust} for downward thrust.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipDownThrust); ship = null; }
    public override void Tick()
    {
        var s = BonusShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool down = Input.GetKey(KeyCode.LeftControl) ||
            TutorialGate.PadHeld(TutorialGate.PadButton.R3);
        if (down) IsComplete = true;
    }
}

class ShipRollBonusStep : BonusStep
{
    Ship ship;
    public override string Tip => $"Press {PromptGlyphs.RollLeft} / {PromptGlyphs.RollRight} to roll.";
    public override void OnEnter() { TutorialGate.Unlock(TutorialAbility.ShipRoll); ship = null; }
    public override void Tick()
    {
        var s = BonusShipFinder.Get(ref ship);
        if (s == null || !s.IsPiloted) return;
        bool rollKey = Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E);
        bool rollPad = TutorialGate.PadPressed(TutorialGate.PadButton.LB)
                    || TutorialGate.PadPressed(TutorialGate.PadButton.RB);
        if (rollKey || rollPad) IsComplete = true;
    }
}
