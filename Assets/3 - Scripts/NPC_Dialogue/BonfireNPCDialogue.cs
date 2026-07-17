using UnityEngine;
using TMPro;
using System.Collections;

public class BonfireNPCDialogue : MonoBehaviour
{
    [Header("UI References (auto-pulled from existing NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("First-Time Line")]
    [TextArea(2, 5)]
    public string firstTimeLine = "Watch out for the white orbs theyll rape ya!";

    [Header("Random Voicelines (rolled on each subsequent talk)")]
    [TextArea(2, 5)]
    public string[] randomLines =
    {
        "Howdy Doody!",
        "You seen any thick latinas lately?",
        "Yo too close bro back up",
        "dayummmmmm watchu doin walkin round with all that ass babygirl!",
        "Man im thinking about some crazy things right now..."
    };

    [Header("Reward")]
    [Tooltip("AxeController to unlock after the first-time line finishes typing. Auto-found if left null.")]
    public AxeController axeController;
    [Tooltip("PistolController unlocked alongside the axe on the first-time line. Auto-found if left null.")]
    public PistolController pistolController;

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] float typewriterVolume = 0.3f;
    AudioSource typewriterSource;

    bool _playerInRange;
    bool _conversationActive;
    bool _firstTimeDone;

    // Save-system accessors: the first-time-met flag must round-trip so
    // loading a save where the player already met this NPC doesn't replay
    // the first-time axe/pistol-grant line.
    public bool FirstTimeDone => _firstTimeDone;
    public void ApplyFirstTimeDone(bool v) => _firstTimeDone = v;

    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    Coroutine _dialogueCoroutine;

    void Start()
    {
        // Auto-borrow the dialogue UI from the existing NPC if not assigned.
        if (dialogueText == null || talkPromptText == null)
        {
            var existing = FindObjectOfType<NPCDialogue>();
            if (existing != null)
            {
                if (dialogueText == null)   dialogueText   = existing.dialogueText;
                if (talkPromptText == null) talkPromptText = existing.talkPromptText;
            }
        }

        if (dialogueText != null)   dialogueText.gameObject.SetActive(false);
        if (talkPromptText != null) talkPromptText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);

        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);

        if (axeController == null) axeController = FindObjectOfType<AxeController>();
        if (pistolController == null) pistolController = FindObjectOfType<PistolController>();

        typewriterSource = GetComponent<AudioSource>();
        if (typewriterSource == null) typewriterSource = gameObject.AddComponent<AudioSource>();
        typewriterSource.playOnAwake = false;
        typewriterSource.loop = true;
        typewriterSource.volume = typewriterVolume;
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
        InteractPromptUI.Clear(this);
        if (_conversationActive) StopConversation();
    }

    // Cached prompt text — rebuilt only when the input source flips (KBM ↔
    // controller), not every frame. Avoids ~1.2 KB/frame GC while in range.
    string _promptCached;
    TutorialGate.InputSource _promptCachedSource = (TutorialGate.InputSource)(-1);

    void Update()
    {
        // Live-refresh the talk prompt glyph (F vs X) per frame.
        if (_playerInRange && !_conversationActive)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptCachedSource)
            {
                _promptCachedSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to talk";
            }
            InteractPromptUI.Show(this, _promptCached);
        }

        if (_playerInRange && !_conversationActive && InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
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

    void StartConversation()
    {
        if (_conversationActive) return;
        _conversationActive = true;
        InteractPromptUI.Clear(this);
        if (dialogueText != null)   dialogueText.gameObject.SetActive(true);
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
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);

        if (_playerInRange) ShowPrompt();
    }

    IEnumerator PlayDialogueSequence()
    {
        string line;
        bool isFirstTime = false;
        if (!_firstTimeDone)
        {
            line = firstTimeLine;
            _firstTimeDone = true;
            isFirstTime = true;
        }
        else if (randomLines != null && randomLines.Length > 0)
        {
            line = randomLines[Random.Range(0, randomLines.Length)];
        }
        else
        {
            line = "";
        }

        if (!string.IsNullOrEmpty(line))
        {
            yield return StartCoroutine(TypewriterLine(line));
            if (isFirstTime && axeController != null) axeController.Unlock();
            if (isFirstTime && pistolController != null) pistolController.Unlock();
            yield return StartCoroutine(WaitForPlayerClick());
        }

        StopConversation();

        if (isFirstTime) BonusTutorial.OfferAxeBuilding();
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

        // Zero-allocation char reveal via TMP's maxVisibleCharacters.
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
