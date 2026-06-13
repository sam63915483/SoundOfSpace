using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum StoryStep
{
    ColdOpen = 0,
    FirstContact = 1,
    NeedsWaterFood = 2,
    NeedsShelter = 3,   // DEPRECATED (Mission 1 fork removed the build-a-cabin gate); kept for save-int stability
    Explore = 4,        // shared opening: "find the main village"
    VillageSeam = 5,    // reached the village — Mission 1 "Taken In" bolts on here

    // ── Mission 1 "Taken In" (three-way fork) ──
    MeetTevVillage   = 6,   // had the intro with Tev at the village; sent to explore
    ExploreArea      = 7,   // looking around for discoverables to report back
    Report           = 8,   // reported to Tev; the three-way fork is offered
    Branching        = 9,   // a branch was chosen (Pilot built; Build/Fish stubbed)
    PilotSchool      = 10,  // training on drones with the flight instructor
    RealFlight       = 11,  // licensed; flying the real ship to Constant Companion
    Mission1Complete = 12,  // arrived at Constant Companion → Mission 2 hand-off
}

/// <summary>
/// Persistent "brain" for the vertical slice. Code-only auto-singleton cloned from
/// DeathCutsceneController's pattern. Holds the ONLY new state that must survive a
/// death-reload: story step, Tev trust, named flags, objective progress, unlocked
/// preset questions. Pure state + persistence; gameplay-event wiring is added in Task 7.
/// </summary>
public class StoryDirector : MonoBehaviour
{
    public static StoryDirector Instance { get; private set; }

    // ---- state ----
    StoryStep _step = StoryStep.ColdOpen;
    float _tevTrust;
    readonly Dictionary<string, bool> _flags = new Dictionary<string, bool>();
    readonly HashSet<string> _activeObjectives = new HashSet<string>();
    readonly HashSet<string> _completedObjectives = new HashSet<string>();
    readonly List<string> _unlockedQuestions = new List<string>();
    bool _objectivesWired;
    string _pendingConversationId;
    string _pendingNodeId;
    bool _firstContactQueued;
    float _coldOpenTimer;
    const float FirstContactDelay = 45f;   // ~30–60s window (GDD discoverability cue)
    float _gateCheckTimer;
    const float GateCheckInterval = 0.5f;  // catch-up cadence for out-of-order gate progression

    /// <summary>Raised whenever step/trust/flags/objectives/questions change, so UI can refresh.</summary>
    public event Action OnStoryStateChanged;

    // ---- step ----
    public StoryStep CurrentStoryStep => _step;
    public void SetStoryStep(StoryStep s) { if (_step == s) return; _step = s; Changed(); }

    // ---- trust ----
    public float TevTrust => _tevTrust;
    public void AddTrust(float amount) { _tevTrust = Mathf.Max(0f, _tevTrust + amount); Changed(); }

    // ---- flags ----
    public bool GetFlag(string name) => !string.IsNullOrEmpty(name) && _flags.TryGetValue(name, out var v) && v;
    public void SetFlag(string name, bool value)
    {
        if (string.IsNullOrEmpty(name)) return;
        _flags[name] = value;
        Changed();
    }

    // ---- objectives ----
    public bool IsObjectiveActive(string id) => _activeObjectives.Contains(id);
    public bool IsObjectiveComplete(string id) => _completedObjectives.Contains(id);
    public void StartObjective(string id)
    {
        if (string.IsNullOrEmpty(id) || _completedObjectives.Contains(id)) return;
        if (_activeObjectives.Add(id)) Changed();
    }
    public void CompleteObjective(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _activeObjectives.Remove(id);
        if (_completedObjectives.Add(id)) Changed();
    }
    public IReadOnlyCollection<string> ActiveObjectives => _activeObjectives;

    // ---- unlocked preset questions ----
    public IReadOnlyList<string> UnlockedQuestionIds => _unlockedQuestions;
    public bool IsQuestionUnlocked(string id) => _unlockedQuestions.Contains(id);
    public void UnlockQuestion(string id)
    {
        if (string.IsNullOrEmpty(id) || _unlockedQuestions.Contains(id)) return;
        _unlockedQuestions.Add(id);
        Changed();
    }
    public void LockQuestion(string id) { if (_unlockedQuestions.Remove(id)) Changed(); }

    // ---- pending conversation ----
    public bool HasPendingConversation => !string.IsNullOrEmpty(_pendingConversationId);
    public string PendingConversationId => _pendingConversationId;
    public string PendingNodeId => _pendingNodeId;
    public event System.Action OnPendingConversationChanged;

    /// <summary>Queue an authored AI conversation to auto-open next time the player enters the AI chat.</summary>
    public void QueueConversation(string conversationId, string nodeId = null)
    {
        _pendingConversationId = conversationId;
        _pendingNodeId = nodeId;
        OnPendingConversationChanged?.Invoke();
    }

    public void ClearPendingConversation()
    {
        _pendingConversationId = null;
        _pendingNodeId = null;
        OnPendingConversationChanged?.Invoke();
    }

    void Changed() => OnStoryStateChanged?.Invoke();

    /// <summary>Force an immediate gate reconciliation — e.g. the moment an authored
    /// conversation ends — instead of waiting up to GateCheckInterval for the catch-up
    /// timer. Lets a finished beat advance + queue the next one this frame.</summary>
    public void ReconcileGatesNow() => CheckGates();

    public void SaveTo(StoryDirectorSave s)
    {
        s.currentStoryStep = (int)_step;
        s.tevTrust = _tevTrust;
        s.flagNames.Clear(); s.flagValues.Clear();
        foreach (var kv in _flags) { s.flagNames.Add(kv.Key); s.flagValues.Add(kv.Value); }
        s.activeObjectives = new List<string>(_activeObjectives);
        s.completedObjectives = new List<string>(_completedObjectives);
        s.unlockedQuestions = new List<string>(_unlockedQuestions);
        s.pendingConversationId = _pendingConversationId ?? "";
        s.pendingNodeId = _pendingNodeId ?? "";
    }

    public void LoadFrom(StoryDirectorSave s)
    {
        if (s == null) return;
        _step = (StoryStep)s.currentStoryStep;
        _tevTrust = s.tevTrust;
        _flags.Clear();
        int n = Mathf.Min(s.flagNames?.Count ?? 0, s.flagValues?.Count ?? 0);
        for (int i = 0; i < n; i++) _flags[s.flagNames[i]] = s.flagValues[i];
        _activeObjectives.Clear();
        if (s.activeObjectives != null) foreach (var id in s.activeObjectives) _activeObjectives.Add(id);
        _completedObjectives.Clear();
        if (s.completedObjectives != null) foreach (var id in s.completedObjectives) _completedObjectives.Add(id);
        _unlockedQuestions.Clear();
        if (s.unlockedQuestions != null) _unlockedQuestions.AddRange(s.unlockedQuestions);
        _pendingConversationId = string.IsNullOrEmpty(s.pendingConversationId) ? null : s.pendingConversationId;
        _pendingNodeId = string.IsNullOrEmpty(s.pendingNodeId) ? null : s.pendingNodeId;
        // Resume the cold-open timer only if we loaded into ColdOpen; otherwise suppress first
        // contact so a later-step save can't re-queue it on an in-process load.
        _firstContactQueued = _step != StoryStep.ColdOpen;
        _coldOpenTimer = 0f;
        Changed();
    }

    // ---- new game ----
    public void ResetForNewGame()
    {
        _step = StoryStep.ColdOpen;
        _tevTrust = 0f;
        _flags.Clear();
        _activeObjectives.Clear();
        _completedObjectives.Clear();
        _unlockedQuestions.Clear();
        _pendingConversationId = null;
        _pendingNodeId = null;
        // Non-serialized runtime gate: must reset too, or a second New Game in the same process
        // keeps _firstContactQueued=true from the prior run and first contact never re-fires.
        _firstContactQueued = false;
        _coldOpenTimer = 0f;
        _gateCheckTimer = 0f;
        Changed();
    }

    // ---- lifecycle (DeathCutsceneController pattern) ----
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return; // trap #1
        var go = new GameObject("StoryDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<StoryDirector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        StoryContent.LoadAll();
        WireGameplayEvents();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            UnwireGameplayEvents();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        WireGameplayEvents();   // re-assert defensively after any scene/domain reload
    }

    void Update()
    {
        if (SceneManager.GetActiveScene().name == "MainMenu") return;

        // First-contact discoverability timer — ColdOpen only, fires once (GDD cue).
        if (!_firstContactQueued)
        {
            if (_step != StoryStep.ColdOpen)
            {
                _firstContactQueued = true;
            }
            else
            {
                _coldOpenTimer += Time.deltaTime;
                if (_coldOpenTimer >= FirstContactDelay)
                {
                    _firstContactQueued = true;
                    SetStoryStep(StoryStep.FirstContact);
                    QueueConversation("conv_first_contact");
                    var phone = PlayerPhoneUI.Instance;
                    if (phone != null)
                    {
                        phone.FlashNotification("Incoming transmission");
                        // §3: first message ever → persistent "Press X to open your
                        // phone." prompt that stays until the player opens it.
                        phone.RequestFirstOpenNag();
                    }
                    // Surface an on-screen cue too, so the player notices with the phone CLOSED — the
                    // in-phone notification alone is invisible until they happen to open it. Reuses the
                    // existing HAL HUD strip.
                    if (HALCommentator.Instance != null)
                        HALCommentator.Instance.VolunteerExternal("Incoming transmission. Open your phone.");
                }
            }
        }

        // Catch-up gate reconciliation. Order-independent safety net: covers players who
        // complete gates out of order, before first contact, or who load mid-progress —
        // anything that left _step behind its flags. Throttled; idle at the terminal seam.
        if (_step >= StoryStep.FirstContact && _step < StoryStep.VillageSeam)
        {
            _gateCheckTimer += Time.deltaTime;
            if (_gateCheckTimer >= GateCheckInterval)
            {
                _gateCheckTimer = 0f;
                CheckGates();
            }
        }
    }

    void WireGameplayEvents()
    {
        if (_objectivesWired) return;
        _objectivesWired = true;
        ResourceManager.OnCleanWaterDrunk += HandleCleanWater;
        BonfireInteraction.OnEat          += HandleCookedFood;
        GhostPlacement.OnPlaced           += HandleBuildingPlaced;
        VillageReachTrigger.OnVillageReached += HandleVillageReached;
        NPCConversationTracker.OnConversationStarted += HandleNpcConversation;
    }

    void UnwireGameplayEvents()
    {
        if (!_objectivesWired) return;
        _objectivesWired = false;
        ResourceManager.OnCleanWaterDrunk -= HandleCleanWater;
        BonfireInteraction.OnEat          -= HandleCookedFood;
        GhostPlacement.OnPlaced           -= HandleBuildingPlaced;
        VillageReachTrigger.OnVillageReached -= HandleVillageReached;
        NPCConversationTracker.OnConversationStarted -= HandleNpcConversation;
    }

    // Vendor visits double as Mission 1's "explore" discoverables (talking to the fish
    // vendor + the goods vendor). The retired tutorial used to set these flags; we set
    // them here so the phone quest rows, HAL lines, and Tev's report gate all work again.
    void HandleNpcConversation(MonoBehaviour npc)
    {
        if (npc is FishMarketNPC) EarlyGameProgress.FishVendorVisited = true;
        else if (npc is Alien7Vendor) EarlyGameProgress.GoodsVendorVisited = true;
    }

    void HandleCleanWater()    { SetFlag("hasWater", true);  CompleteByEvent("OnCleanWaterDrunk"); CheckGates(); }
    void HandleCookedFood()    { SetFlag("hasFood", true);   CompleteByEvent("OnCookedFoodEaten"); CheckGates(); }

    void HandleVillageReached()
    {
        bool first = !GetFlag("villageReached");
        SetFlag("villageReached", true);
        CompleteByEvent("OnVillageReached");
        CheckGates();

        // Recognise the arrival: a fresh AI transmission + a queued village-arrival
        // conversation that supersedes the free-time menu until the player answers it.
        if (first)
        {
            QueueConversation("conv_village_arrival");
            var phone = PlayerPhoneUI.Instance;
            if (phone != null) phone.FlashNotification("Incoming transmission");
            if (HALCommentator.Instance != null)
                HALCommentator.Instance.VolunteerExternal("You've reached the village. Open your phone.");
        }
    }

    void HandleBuildingPlaced(BuildableEntry entry)
    {
        if (entry == null) return;
        bool isCabin = (entry.displayName != null && entry.displayName.Equals("Cabin", System.StringComparison.OrdinalIgnoreCase))
                       || (entry.prefab != null && entry.prefab.name.IndexOf("Cabin", System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (!isCabin) return;
        SetFlag("hasShelter", true);
        CompleteByEvent("OnShelterBuilt");
        CheckGates();
    }

    // Advance the opening's soft gates as their flags fill in. Idempotent and
    // order-independent: re-evaluates from the current flags and CASCADES through every
    // already-satisfied gate in one call, so a player who does several things at once (or
    // before being prompted) is never left a step behind. Safe to call from any gameplay
    // event or on the catch-up timer. Each transition queues the next authored beat
    // (shown the next time the player opens the phone), whether they asked the AI or not.
    void CheckGates()
    {
        // Never skip the AI's first-contact intro: let the player see it before the gates
        // cascade (a prepared player still gets the briefing, which has its own "already
        // handled the basics" branch). Gate prompts, by contrast, may be overwritten freely —
        // an overwrite only happens when the newer gate is ALSO satisfied, i.e. the older
        // prompt is already obsolete.
        if (_pendingConversationId == "conv_first_contact") return;

        bool advanced;
        do
        {
            advanced = false;
            switch (_step)
            {
                case StoryStep.FirstContact:
                case StoryStep.NeedsWaterFood:
                    // Mission 1 fork (GDD §2): no shelter-building before the village.
                    // Once the player has secured water + food, send them straight out to
                    // find the village — the old NeedsShelter "build a cabin" gate is gone
                    // (building is now the optional, deferred Build branch).
                    if (GetFlag("hasWater") && GetFlag("hasFood"))
                    {
                        SetStoryStep(StoryStep.Explore);
                        StartObjective("obj_village");
                        QueueConversation("conv_gates", "gate_explore");
                        advanced = true;
                    }
                    break;
                case StoryStep.Explore:
                    if (GetFlag("villageReached"))
                    {
                        SetStoryStep(StoryStep.VillageSeam);   // seam: Mission 1 "Taken In" bolts on here
                        advanced = true;
                    }
                    break;
            }
        } while (advanced);
    }

    void CompleteByEvent(string eventName)
    {
        foreach (var kv in StoryContent.Objectives)
        {
            var obj = kv.Value;
            if (obj.completionEvent != eventName) continue;
            if (IsObjectiveComplete(obj.id)) continue;
            CompleteObjective(obj.id);
            DialogueEffects.Apply(obj.onComplete);
            if (!string.IsNullOrEmpty(obj.hintTrackId) && HintTrackRunner.Instance != null)
                HintTrackRunner.Instance.StopTrack(obj.hintTrackId);
        }
    }
}
