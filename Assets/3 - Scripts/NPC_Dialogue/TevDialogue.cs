using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Tev's NPC dialogue — Mission 1 "Taken In" (three-way fork).
// See docs/GDD_VerticalSlice_Mission1_Fork.md.
//
// Tev lives at the vendor village now: the crash → survive → trek leads here, the
// player meets him properly, he sends them to look around, then the report + the
// three-way fork happen in this same conversation. State is derived from Mission1
// flags (StoryDirector-backed, so it round-trips through saves) plus the legacy
// EarlyGameProgress.TevReturnedDialogueDone (kept so the whistle/axe-grant beat
// stays compatible).
//
// Stages (implicit, by flag combination):
//   • !FlagMetTevVillage                    → intro: took-you-in + give axe + pistol + "go look around"
//   • met, not enough seen, not reported    → small-talk ("find anything yet?")
//   • met, explored enough, no branch yet   → REPORT (branches on what was seen) + the 3-way FORK menu
//   • Pilot chosen, not complete            → "how's the flying going" + re-mark instructor waypoint
//   • Mission 1 complete                    → random done-lines
//
// UI mirrors NPCDialogue's typewriter pattern; the fork uses the shared
// PostGreetingChoicePanel. Auto-borrows dialogueText / talkPromptText from any
// scene NPCDialogue if left unassigned.
public class TevDialogue : MonoBehaviour
{
    [Header("UI References (auto-borrowed from any NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Lines — village intro (takes you in, gives axe + pistol)")]
    [TextArea(2, 5)]
    public string[] introLines = new[]
    {
        "There you are — up and walking. Wasn't sure you'd make it when I pulled you out of that wreck.",
        "Name's Tev. You're safe here in the village. Rest when you need to.",
        "Here — take my axe, and an old glock of mine. A body shouldn't wander out there empty-handed.",
        "Go have a look around the place. Then come back and tell me what you found.",
    };

    [Header("Lines — nudge: hasn't met either vendor yet")]
    [TextArea(2, 5)]
    public string[] vendorsNudgeNeitherLines = new[]
    {
        "Have a proper wander first. We've got two folks worth meeting around here —",
        "a fish vendor, and a goods vendor. Go say hello to both, then come tell me about it.",
    };

    [Header("Lines — nudge: met fish vendor, still needs the goods vendor")]
    [TextArea(2, 5)]
    public string[] findGoodsVendorLines = new[]
    {
        "Good — you found the fish vendor. There's a goods vendor too; you'll want to know them.",
        "Go track the goods vendor down, then come back to me.",
    };

    [Header("Lines — nudge: met goods vendor, still needs the fish vendor")]
    [TextArea(2, 5)]
    public string[] findFishVendorLines = new[]
    {
        "Good — you met the goods vendor. Now find our fish vendor; usually down by the water.",
        "Have a word with them, then come back to me.",
    };

    [Header("Lines — report opener (before the what-you-saw flavor)")]
    [TextArea(2, 5)]
    public string[] reportOpenerLines = new[]
    {
        "So — you had a look around. What did you make of it?",
    };

    [Header("Lines — report closer (after the flavor)")]
    [TextArea(2, 5)]
    public string[] reportClosingLines = new[]
    {
        "Good eye. You're settling in faster than most.",
        "Well — now that you've got your bearings, there's the question of what you do next.",
    };

    [Header("Lines — the fork prompt (shown right before the choice menu)")]
    [TextArea(2, 5)]
    public string[] forkPromptLines = new[]
    {
        "So. What do you want to do now?",
    };

    [Header("Lines — Pilot branch confirmed")]
    [TextArea(2, 5)]
    public string[] pilotConfirmLines = new[]
    {
        "A flier, huh? Good instinct — there's a whole sky out there.",
        "Talk to the flight instructor. They'll start you on the drones before you risk a real ship.",
        "I've marked them on your compass. Off you go.",
    };

    [Header("Lines — after Pilot chosen, while training")]
    [TextArea(2, 5)]
    public string[] pilotWaitingLines = new[]
    {
        "How's the flying coming? The instructor's waiting on you.",
        "Drones first, then the real thing. You'll get it.",
    };

    [Header("Lines — Build / Fish picked (stubbed for the slice)")]
    [TextArea(2, 5)]
    public string[] notYetLines = new[]
    {
        "Let's save that one for later. Pick something you can sink your teeth into now.",
    };

    [Header("Lines — Mission 1 complete (random pick each talk)")]
    [TextArea(2, 5)]
    public string[] doneLines = new[]
    {
        "Look at you — a real pilot now.",
        "Stay sharp out there.",
        "Don't let the orbs get you.",
    };

    [Header("Reward References (auto-found if null)")]
    public AxeController axeController;
    public PistolController pistolController;

    [Header("Mission 1 — Pilot branch")]
    [Tooltip("Assign the flight-instructor NPC's transform. When Pilot is chosen, a compass waypoint points here. (Placed at CP-B.)")]
    public Transform flightInstructorWaypoint;
    [Tooltip("Tev trust granted when the player commits to the Pilot branch.")]
    public float trustOnBranchChosen = 1f;

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    [Header("Whistle — draws the player toward Tev once they've secured water or food")]
    [Tooltip("Looping whistle. Plays from Tev in 3D after the player has water OR food, and stops once they reach him for the intro. Drop your mp3 here.")]
    [SerializeField] private AudioClip whistleClip;
    [SerializeField, Range(0f, 1f)] private float whistleVolume = 0.6f;
    [SerializeField] private float whistleMaxDistance = 120f;
    private AudioSource whistleSource;

    const string InstructorWaypointId = "m1_flight_instructor";

    bool _playerInRange;
    bool _conversationActive;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    int _forkChoice = -1;
    Coroutine _dialogueCoroutine;
    Collider _selfCollider;

    void Start()
    {
        typewriterSource = GetComponent<AudioSource>();
        if (typewriterSource == null) typewriterSource = gameObject.AddComponent<AudioSource>();
        typewriterSource.playOnAwake = false;
        typewriterSource.loop = true;
        typewriterSource.volume = typewriterVolume;

        // Dedicated 3D source for the draw-the-player whistle (separate from the 2D
        // typewriter source so the two never fight over clip/volume).
        whistleSource = gameObject.AddComponent<AudioSource>();
        whistleSource.playOnAwake = false;
        whistleSource.loop = true;
        whistleSource.clip = whistleClip;
        whistleSource.volume = whistleVolume;
        whistleSource.spatialBlend = 1f;                 // 3D — gives the player a direction to walk
        whistleSource.rolloffMode = AudioRolloffMode.Linear;
        whistleSource.minDistance = 5f;
        whistleSource.maxDistance = whistleMaxDistance;

        // Auto-borrow dialogue UI from any NPCDialogue if our inspector
        // refs are empty — same trick BonfireNPCDialogue uses.
        if (dialogueText == null || talkPromptText == null)
        {
            var existing = FindObjectOfType<NPCDialogue>();
            if (existing != null)
            {
                if (dialogueText == null) dialogueText = existing.dialogueText;
                if (talkPromptText == null) talkPromptText = existing.talkPromptText;
            }
        }

        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);

        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);

        if (axeController == null) axeController = FindObjectOfType<AxeController>();
        if (pistolController == null) pistolController = FindObjectOfType<PistolController>();

        _selfCollider = GetComponent<Collider>();
        UpdateInteractability();
    }

    // Tev is always talkable now — the old water-bottle gate was tutorial-era.
    void UpdateInteractability()
    {
        bool canInteract = true;
        if (_selfCollider != null && _selfCollider.enabled != canInteract)
            _selfCollider.enabled = canInteract;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
        ShowPrompt();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        HidePrompt();
        if (_conversationActive) StopConversation();
    }

    void Update()
    {
        UpdateInteractability();
        UpdateWhistle();

        if (_playerInRange && !_conversationActive)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }

        if (_playerInRange && !_conversationActive
            && InteractGaze.IsLookingAt(this)
            && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            StartConversation();
            return;
        }

        if (!_conversationActive) return;

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping) _skipTyping = true;
            else if (_waitingForClick) _waitingForClick = false;
        }
    }

    void ShowPrompt() => InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
    void HidePrompt() => InteractPromptUI.Clear(this);

    // Tev whistles to draw the player toward the village once they've secured water
    // or food, and stops once they reach him for the intro. Derived from persisted
    // flags, so it re-evaluates correctly after a save/reload. Purely an audio cue.
    void UpdateWhistle()
    {
        if (whistleSource == null) return;
        var sd = StoryDirector.Instance;
        bool waterOrFood = sd != null && (sd.GetFlag("hasWater") || sd.GetFlag("hasFood"));
        bool shouldWhistle =
            whistleClip != null
            && !_conversationActive
            && !EarlyGameProgress.TevReturnedDialogueDone
            && waterOrFood;

        if (shouldWhistle && !whistleSource.isPlaying)
        {
            if (whistleSource.clip == null) whistleSource.clip = whistleClip;
            whistleSource.volume = whistleVolume;
            whistleSource.Play();
        }
        else if (!shouldWhistle && whistleSource.isPlaying)
        {
            whistleSource.Stop();
        }
    }

    void StartConversation()
    {
        if (_conversationActive) return;
        _conversationActive = true;
        if (whistleSource != null && whistleSource.isPlaying) whistleSource.Stop();
        HidePrompt();
        if (dialogueText != null) dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);
        _dialogueCoroutine = StartCoroutine(PlayDialogueSequence());
    }

    void StopConversation()
    {
        if (_dialogueCoroutine != null)
        {
            StopCoroutine(_dialogueCoroutine);
            _dialogueCoroutine = null;
        }
        // Hide the fork panel if it was left open (e.g. player walked away mid-choice).
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        _conversationActive = false;
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
        _forkChoice = -1;
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (_playerInRange) ShowPrompt();
    }

    IEnumerator PlayDialogueSequence()
    {
        bool met          = Mission1.Get(Mission1.FlagMetTevVillage);
        bool reported     = Mission1.Get(Mission1.FlagReported);
        bool pilotStarted = Mission1.Get(Mission1.FlagPilotStarted);
        bool complete     = Mission1.Get(Mission1.FlagComplete);

        if (!met)
        {
            yield return RunIntro();
        }
        else if (complete || pilotStarted)
        {
            // Pilot already chosen (or mission done) — light small-talk only.
            yield return SpeakLines(OneRandomLine(complete ? doneLines : pilotWaitingLines));
            if (!complete && pilotStarted) AddInstructorWaypoint();
        }
        else if (Mission1.ExploredEnough())
        {
            yield return RunReportAndFork(reported);
        }
        else
        {
            // Met him, but hasn't met both vendors yet — point them at the missing one(s).
            yield return RunExploreNudge();
        }

        StopConversation();
    }

    // GDD §4 / user spec: if the player hasn't met either vendor, send them to both;
    // if they've met one, name the one they still need to find.
    IEnumerator RunExploreNudge()
    {
        if (Mission1.VisitedFishVendor() && !Mission1.VisitedGoodsVendor())
            yield return SpeakLines(findGoodsVendorLines);
        else if (Mission1.VisitedGoodsVendor() && !Mission1.VisitedFishVendor())
            yield return SpeakLines(findFishVendorLines);
        else
            yield return SpeakLines(vendorsNudgeNeitherLines);
    }

    IEnumerator RunIntro()
    {
        yield return SpeakLines(introLines);
        if (!_playerInRange) yield break;

        if (axeController != null) axeController.Unlock();
        if (pistolController != null) pistolController.Unlock();
        EarlyGameProgress.TevReturnedDialogueDone = true;

        Mission1.Set(Mission1.FlagMetTevVillage, true);
        var sd = StoryDirector.Instance;
        if (sd != null)
        {
            sd.SetStoryStep(StoryStep.ExploreArea);
            sd.StartObjective("obj_explore");
        }
    }

    IEnumerator RunReportAndFork(bool alreadyReported)
    {
        if (!alreadyReported)
        {
            // First report — opener, branch-on-what-you-saw flavor, closer.
            yield return SpeakLines(reportOpenerLines);
            if (!_playerInRange) yield break;

            foreach (var line in BuildReportFlavor())
            {
                yield return SpeakOne(line);
                if (!_playerInRange) yield break;
            }

            yield return SpeakLines(reportClosingLines);
            if (!_playerInRange) yield break;

            Mission1.Set(Mission1.FlagReported, true);
            var sd = StoryDirector.Instance;
            if (sd != null)
            {
                sd.SetStoryStep(StoryStep.Report);
                sd.CompleteObjective("obj_explore");
            }
        }

        // Offer (or re-offer) the three-way fork.
        yield return SpeakLines(forkPromptLines);
        if (!_playerInRange) yield break;

        yield return ShowForkAndRoute();
    }

    // Light branch on who the player met (GDD §4). By the time the report opens both
    // vendors are visited, so this just colours the beat with what they found.
    List<string> BuildReportFlavor()
    {
        var lines = new List<string>();
        if (Mission1.VisitedFishVendor())
            lines.Add("Met our fish vendor — good. They'll take whatever you haul out of the water, fair price.");
        if (Mission1.VisitedGoodsVendor())
            lines.Add("And the goods vendor. Handy sort — keeps us in the odds and ends you can't fish out of a lake.");
        if (lines.Count == 0)
            lines.Add("Quiet little place, isn't it. Suits me fine.");
        return lines;
    }

    IEnumerator ShowForkAndRoute()
    {
        if (PostGreetingChoicePanel.Instance == null) yield break;

        _forkChoice = -1;
        var rows = new List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row("Learn to pilot a ship", true),
            new PostGreetingChoicePanel.Row("Build your own cabin", true),
            new PostGreetingChoicePanel.Row("Go on a fishing trip", true),
            new PostGreetingChoicePanel.Row("Give me a minute to think.", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, i => _forkChoice = i);

        yield return new WaitUntil(() => _forkChoice >= 0 || !_playerInRange);
        if (!_playerInRange) yield break;

        switch (_forkChoice)
        {
            case 0: // Pilot
                yield return CommitPilotBranch();
                break;
            case 1: // Build (disabled — defensive)
            case 2: // Fish  (disabled — defensive)
                yield return SpeakLines(notYetLines);
                break;
            default: // "Give me a minute" — end without choosing; fork re-offers next talk.
                break;
        }
    }

    IEnumerator CommitPilotBranch()
    {
        Mission1.SetBranch(Mission1.Branch.Pilot);
        Mission1.Set(Mission1.FlagPilotStarted, true);

        var sd = StoryDirector.Instance;
        if (sd != null)
        {
            sd.SetStoryStep(StoryStep.PilotSchool);
            sd.AddTrust(trustOnBranchChosen);
            sd.StartObjective("obj_pilot_school");
        }

        AddInstructorWaypoint();
        yield return SpeakLines(pilotConfirmLines);
    }

    void AddInstructorWaypoint()
    {
        if (flightInstructorWaypoint == null || CompassHUD.Instance == null) return;
        if (CompassHUD.Instance.HasWaypoint(InstructorWaypointId)) return;
        var target = flightInstructorWaypoint;
        CompassHUD.Instance.AddWaypoint(InstructorWaypointId, () => target.position, "Flight Instructor");
    }

    // ── line helpers ──
    IEnumerator SpeakLines(string[] lines)
    {
        if (lines == null) yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!_playerInRange) { StopConversation(); yield break; }
            yield return SpeakOne(lines[i]);
            if (!_playerInRange) { StopConversation(); yield break; }
        }
    }

    IEnumerator SpeakOne(string line)
    {
        yield return StartCoroutine(TypewriterLine(line));
        yield return StartCoroutine(WaitForPlayerClick());
    }

    static string[] OneRandomLine(string[] pool)
    {
        if (pool == null || pool.Length == 0) return new[] { "..." };
        return new[] { pool[Random.Range(0, pool.Length)] };
    }

    IEnumerator TypewriterLine(string line)
    {
        if (dialogueText == null) yield break;
        _isTyping = true;
        _skipTyping = false;

        if (typewriterLoopClip != null && typewriterSource != null)
        {
            typewriterSource.clip = typewriterLoopClip;
            typewriterSource.volume = typewriterVolume;
            typewriterSource.Play();
        }

        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);

        if (typewriterSource != null && typewriterSource.isPlaying)
            typewriterSource.Stop();

        _isTyping = false;
        _skipTyping = false;
    }

    IEnumerator WaitForPlayerClick()
    {
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);
    }
}
