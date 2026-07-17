using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class NPCDialogue : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Trade Choice UI")]
    public GameObject choicePanel;
    public Button yesButton;
    public Button noButton;

    [Header("Greeting Lines")]
    [TextArea(2, 5)]
    public string[] greetingLines = { "Hi how are you!", "Looks like you had a pretty bad crash" };

    [Header("Trade Offer")]
    [TextArea(2, 5)]
    public string tradeOfferLine = "Hey young buck, I heard you playing some tunes on that ship of yours, wanna trade some music for a fishing rod?";
    public string noCassetteResponse = "Fuckoff until you actually have some music to trade!";

    [Header("Post-Trade Dialogue")]
    [TextArea(2, 5)]
    public string postTradeLine = "Here you go young buck, take my trusty fishing rod!";

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    [Header("Fishing Rod")]
    public FishingRodController fishingRodController;

    private PlayerPickup _playerPickup;
    private bool _playerInRange;
    private bool _conversationActive;
    private bool _conversationCompleted;
    private bool _rodEquipped;
    private bool _choiceMade;
    private bool _playerChoseYes;
    private bool _isTyping;
    private bool _skipTyping;
    private bool _waitingForClick;
    private Coroutine _dialogueCoroutine;

    public bool ConversationCompleted => _conversationCompleted;

    void Start()
    {
        if (dialogueText != null)   dialogueText.gameObject.SetActive(false);
        if (talkPromptText != null) talkPromptText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);
        if (choicePanel != null)    choicePanel.SetActive(false);

        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);

        if (fishingRodController == null)
            fishingRodController = FindObjectOfType<FishingRodController>();

        _playerPickup = FindObjectOfType<PlayerPickup>();

        if (yesButton != null) yesButton.onClick.AddListener(OnYesClicked);
        if (noButton != null)  noButton.onClick.AddListener(OnNoClicked);

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
        if (!_conversationCompleted)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
        if (_conversationActive) StopConversation();
    }

    // Cached prompt text — rebuilt only when the InputSource flips (KBM ↔
    // controller), not every frame. The `$"..."` interpolation otherwise
    // allocated ~1.2 KB/frame while the player stood in range (see BonfireInteraction).
    string _promptCached;
    TutorialGate.InputSource _promptCachedSource = (TutorialGate.InputSource)(-1);

    void Update()
    {
        // Live-refresh the talk prompt glyph (F vs X) while the player stands
        // in range. Show is idempotent; InteractPromptUI updates the glyph each call.
        if (_playerInRange && !_conversationActive && !_conversationCompleted)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptCachedSource)
            {
                _promptCachedSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to talk";
            }
            InteractPromptUI.Show(this, _promptCached);
        }

        if (_playerInRange && !_conversationActive && !_conversationCompleted && InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            StartConversation();
            return;
        }

        if (!_conversationActive) return;

        // Choice panel is open — don't intercept primary-action input
        if (choicePanel != null && choicePanel.activeSelf) return;

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping)
            {
                _skipTyping = true;
            }
            else if (_waitingForClick)
            {
                _waitingForClick = false;
            }
        }
    }

    void StartConversation()
    {
        if (_conversationActive || _conversationCompleted) return;
        _conversationActive = true;
        _rodEquipped = false;
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

        CloseChoicePanel();
        _conversationActive = false;
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);

        if (_playerInRange && !_conversationCompleted)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }
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

        // Use TMP's maxVisibleCharacters (zero-allocation) instead of `text += c`
        // (O(n²) string concatenation per character).
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

    bool IsHoldingCassette()
    {
        if (_playerPickup == null)
            _playerPickup = FindObjectOfType<PlayerPickup>();

        if (_playerPickup != null && _playerPickup.IsHoldingObject) return true;
        if (_playerPickup != null && _playerPickup.holdPosition != null
            && _playerPickup.holdPosition.childCount > 0) return true;

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && player.GetComponentInChildren<CassettePickup>() != null) return true;

        return false;
    }

    IEnumerator PlayDialogueSequence()
    {
        // ── Step 0: Greeting lines ───────────────────────────────
        if (greetingLines != null)
        {
            for (int i = 0; i < greetingLines.Length; i++)
            {
                if (!_playerInRange) { StopConversation(); yield break; }
                yield return StartCoroutine(TypewriterLine(greetingLines[i]));
                yield return StartCoroutine(WaitForPlayerClick());
                if (!_playerInRange) { StopConversation(); yield break; }
            }
        }

        if (!_playerInRange) { StopConversation(); yield break; }

        // ── Step 1: Trade offer ──────────────────────────────────
        yield return StartCoroutine(TypewriterLine(tradeOfferLine));
        yield return StartCoroutine(WaitForPlayerClick());
        if (!_playerInRange) { StopConversation(); yield break; }

        // ── Step 2: Show Yes/No choice ───────────────────────────
        _choiceMade     = false;
        _playerChoseYes = false;
        if (choicePanel != null)
        {
            choicePanel.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        yield return new WaitUntil(() => _choiceMade || !_playerInRange);

        CloseChoicePanel();

        if (!_playerInRange) { StopConversation(); yield break; }

        // ── Step 3: Handle choice ────────────────────────────────
        if (!_playerChoseYes)
        {
            ResetToIdle();
            yield break;
        }

        bool hasCassette = IsHoldingCassette();

        if (!hasCassette)
        {
            yield return StartCoroutine(TypewriterLine(noCassetteResponse));
            yield return StartCoroutine(WaitForPlayerClick());
            ResetToIdle();
            yield break;
        }

        // Destroy the cassette
        GameObject cassette = _playerPickup != null ? _playerPickup.GetHeldObject() : null;
        if (cassette == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                CassettePickup cp = player.GetComponentInChildren<CassettePickup>();
                if (cp != null) cassette = cp.gameObject;
            }
        }
        if (cassette != null)
        {
            if (_playerPickup != null) _playerPickup.ClearHeldObject();
            Object.Destroy(cassette);
        }

        // ── Step 4: Give rod + final line ────────────────────────
        if (!_playerInRange) { StopConversation(); yield break; }

        if (!_rodEquipped)
        {
            _rodEquipped = true;
            if (fishingRodController != null)
                fishingRodController.ForceEquipRod();
        }

        if (!string.IsNullOrEmpty(postTradeLine))
        {
            yield return StartCoroutine(TypewriterLine(postTradeLine));
            yield return StartCoroutine(WaitForPlayerClick());
            if (!_playerInRange) { StopConversation(); yield break; }
        }

        _conversationCompleted = true;
        _conversationActive    = false;
        PlayerController.isInDialogue = false;
        if (dialogueText != null)   dialogueText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);

        Debug.Log("Conversation complete. Fishing rod traded.");

        BonusTutorial.OfferFishing();
    }

    void ResetToIdle()
    {
        _conversationActive = false;
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (_playerInRange)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }
        _dialogueCoroutine = null;
    }

    void CloseChoicePanel()
    {
        if (choicePanel != null && choicePanel.activeSelf)
        {
            choicePanel.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    void OnYesClicked() { _choiceMade = true; _playerChoseYes = true; }
    void OnNoClicked()  { _choiceMade = true; _playerChoseYes = false; }

    public void ApplyCompleted(bool completed)
    {
        _conversationCompleted = completed;
        if (completed)
        {
            _conversationActive = false;
            InteractPromptUI.Clear(this);
            if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        }
    }
}
