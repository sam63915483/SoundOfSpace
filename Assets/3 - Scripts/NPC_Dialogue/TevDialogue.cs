using System.Collections;
using TMPro;
using UnityEngine;

// Tev's NPC dialogue. State derived directly from EarlyGameProgress flags
// so saves don't need a separate Tev field — the flags already round-trip.
//
// Stages (implicit, by flag combination):
//   • Not WaterBottleDrunk         → trigger collider disabled, can't talk
//   • TevReturnedDialogueDone == false → "welcome back" + give axe + pistol
//   • !CabinBuilt                  → small-talk lines while waiting for cabin
//   • VillageCoordsGiven == false  → "go to the village" + add village waypoint
//   • else                         → random small-talk lines
//
// UI-wise this mirrors NPCDialogue's typewriter pattern (talk prompt above,
// dialogue text in front of player; use TutorialGate.PrimaryActionPressed()
// to advance through lines). Auto-borrows the dialogueText / talkPromptText
// references from the existing scene NPCDialogue if not assigned in inspector.
public class TevDialogue : MonoBehaviour
{
    [Header("UI References (auto-borrowed from any NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Lines — first encounter (gives axe)")]
    [TextArea(2, 5)]
    public string[] returnedLines = new[]
    {
        "Hey kid, glad to see you up and around!",
        "I left a tool you can have. Take this axe — chop wood, and you can build a real shelter.",
        "Build yourself a cabin — when you're done, come find me.",
    };

    [Header("Lines — waiting for cabin to be built (random pick each talk)")]
    [TextArea(2, 5)]
    public string[] waitingForCabinLines = new[]
    {
        "How's the cabin coming?",
        "Take your time. Wood's free as long as your axe holds up.",
        "Once you've got walls up, come find me.",
    };

    [Header("Lines — gives village coordinates")]
    [TextArea(2, 5)]
    public string[] villageLines = new[]
    {
        "Nice work on the cabin!",
        "There's a village not far from here. They've got a fish vendor and a goods vendor — worth seeing.",
        "I've marked it on your compass. Head that way when you're ready.",
        "Take my trusty glock, incase theres any opps on the wrong block.",
    };

    [Header("Lines — after village given (random pick each talk)")]
    [TextArea(2, 5)]
    public string[] doneLines = new[]
    {
        "How's the catch?",
        "Stay sharp out there.",
        "Don't let the orbs get you.",
    };

    [Header("Reward References (auto-found if null)")]
    public AxeController axeController;
    public PistolController pistolController;

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    [Header("Whistle — draws the player to Tev once they've secured water or food")]
    [Tooltip("Looping whistle. Plays from Tev in 3D after the player completes the water OR food task, and stops once they reach Tev and collect the axe. Drop your mp3 here.")]
    [SerializeField] private AudioClip whistleClip;
    [SerializeField, Range(0f, 1f)] private float whistleVolume = 0.6f;
    [SerializeField] private float whistleMaxDistance = 120f;
    private AudioSource whistleSource;

    bool _playerInRange;
    bool _conversationActive;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
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

        // Force the first-encounter lines here: the scene instance has the old
        // serialized strings, which would otherwise override the code default.
        returnedLines = new[]
        {
            "Great to see you up and around, I was starting to think you didnt pull through!",
            "Take my trusty axe, you need it to survive more than I do, I just ask that some day you repay the favour",
        };

        _selfCollider = GetComponent<Collider>();
        UpdateInteractability();
    }

    // Tev's collider is enabled only after the player has finished the water
    // bottle phase — that's our "Tev is now home" trigger. Before that he's
    // present in the world (waving, head-tracking) but not interactable.
    void UpdateInteractability()
    {
        // Tev is always talkable now — the old water-bottle gate was tutorial-era.
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

    void ShowPrompt()
    {
        InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
    }

    void HidePrompt()
    {
        InteractPromptUI.Clear(this);
    }

    // Tev whistles to draw the player in once they've secured water or food, and stops
    // once they reach him and collect the axe (TevReturnedDialogueDone). Derived entirely
    // from persisted flags, so it re-evaluates correctly after a save/reload. Purely an
    // audio cue — gates nothing.
    void UpdateWhistle()
    {
        if (whistleSource == null) return;
        // Key off the StoryDirector task flags, NOT EarlyGameProgress.WaterBottleDrunk/
        // FirstMealEaten — those are only set by the (now-disabled) forced tutorial, so they
        // never flip on the free-spawn opening. hasWater/hasFood are what the new path sets.
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
        _conversationActive = false;
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (_playerInRange) ShowPrompt();
    }

    IEnumerator PlayDialogueSequence()
    {
        string[] lines;
        bool grantAxe = false;
        bool grantVillage = false;

        // Branch on EarlyGameProgress flags. No explicit stage enum —
        // the flag combinations ARE the stage.
        if (!EarlyGameProgress.TevReturnedDialogueDone)
        {
            lines = returnedLines;
            grantAxe = true;
        }
        else if (!EarlyGameProgress.CabinBuilt)
        {
            lines = OneRandomLine(waitingForCabinLines);
        }
        else if (!EarlyGameProgress.VillageCoordsGiven)
        {
            lines = villageLines;
            grantVillage = true;
        }
        else
        {
            lines = OneRandomLine(doneLines);
        }

        if (lines != null)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (!_playerInRange) { StopConversation(); yield break; }
                yield return StartCoroutine(TypewriterLine(lines[i]));
                yield return StartCoroutine(WaitForPlayerClick());
                if (!_playerInRange) { StopConversation(); yield break; }
            }
        }

        if (grantAxe)
        {
            if (axeController != null)
            {
                // Unlock only — the axe drops into the next open hotbar slot via
                // Hotbar.DetectAcquisitions. Do NOT auto-equip; the player ends the
                // dialogue (left-click) and equips it manually.
                axeController.Unlock();
            }
            EarlyGameProgress.TevReturnedDialogueDone = true;
        }
        if (grantVillage)
        {
            EarlyGameProgress.VillageCoordsGiven = true;
            // Tev hands the player a "glock" alongside the village coordinates.
            if (pistolController != null) pistolController.Unlock();
            // The Phase 7 TravelToVillageStep adds its own compass waypoint
            // when it activates — no waypoint registered here so the compass
            // doesn't carry a stale "Village" pip during the brief gap before
            // the next step takes over. (Step OnEnter / OnExit owns waypoints.)
        }

        StopConversation();
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
