using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class GuitarShopNPC : MonoBehaviour
{
    [Header("UI References")]
    public Text talkPromptText;
    public Text dialogueText;
    public GameObject choicePanel;
    public Button leftButton;
    public Button rightButton;
    public Text leftButtonText;
    public Text rightButtonText;

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    [Header("References")]
    public GuitarController guitarController;
    public FishingRodController fishingRodController;

    private enum State { Idle, OfferQuestion, PriceQuestion, Done }
    private State _state = State.Idle;
    private bool _playerInRange;
    private bool _isTyping;
    private bool _skipTyping;
    private bool _waitingForClick;
    private Coroutine _dialogueCoroutine;

    void Start()
    {
        if (choicePanel != null)   choicePanel.SetActive(false);
        if (dialogueText != null)  dialogueText.gameObject.SetActive(false);
        if (talkPromptText != null) talkPromptText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);

        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);

        if (guitarController == null)
            guitarController = FindObjectOfType<GuitarController>();

        if (fishingRodController == null)
            fishingRodController = FindObjectOfType<FishingRodController>();

        if (leftButton != null)  leftButton.onClick.AddListener(OnLeftButtonClicked);
        if (rightButton != null) rightButton.onClick.AddListener(OnRightButtonClicked);

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
        if (_state != State.Done)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
        CloseChoicePanel();
        StopActiveDialogue();
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        PlayerController.isInDialogue = false;
        if (_state != State.Done)
            _state = State.Idle;
    }

    void Update()
    {
        // Live-refresh the talk prompt glyph (F vs X) per frame.
        if (_playerInRange && _state == State.Idle)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }

        if (_playerInRange && _state == State.Idle && InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
        {
            BeginConversation();
            return;
        }

        if (_state == State.Idle || _state == State.Done) return;

        // When choice panel is open, don't intercept primary-action input
        if (choicePanel != null && choicePanel.activeSelf) return;

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping)
                _skipTyping = true;
            else if (_waitingForClick)
                _waitingForClick = false;
        }
    }

    void BeginConversation()
    {
        _state = State.OfferQuestion;
        InteractPromptUI.Clear(this);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);

        if (fishingRodController == null)
            fishingRodController = FindObjectOfType<FishingRodController>();
        if (fishingRodController != null)
            fishingRodController.ForceUnequipRod();

        if (dialogueText != null) dialogueText.gameObject.SetActive(true);
        _dialogueCoroutine = StartCoroutine(ShowLineWithChoices("Hey there want to buy a guitar?", "Yes", "No"));
    }

    void OnLeftButtonClicked()
    {
        if (_state == State.OfferQuestion)
        {
            CloseChoicePanel();
            _state = State.PriceQuestion;
            StopActiveDialogue();
            _dialogueCoroutine = StartCoroutine(ShowLineWithChoices("200 sounds fair whadda ya tink?", "Offer 100$", "Agree to 200$"));
        }
        else if (_state == State.PriceQuestion)
        {
            CloseChoicePanel();
            StopActiveDialogue();
            if (Random.value >= 0.5f)
                _dialogueCoroutine = StartCoroutine(ShowThenFinalize("Here you go, may you take good care of her", 100));
            else
                _dialogueCoroutine = StartCoroutine(ShowThenEnd("Hell nah man get outta here"));
        }
    }

    void OnRightButtonClicked()
    {
        if (_state == State.OfferQuestion)
        {
            CloseChoicePanel();
            StopActiveDialogue();
            _dialogueCoroutine = StartCoroutine(ShowThenEnd("Toodles!"));
        }
        else if (_state == State.PriceQuestion)
        {
            CloseChoicePanel();
            StopActiveDialogue();
            _dialogueCoroutine = StartCoroutine(ShowThenFinalize("Here you go, may you take good care of her", 200));
        }
    }

    IEnumerator ShowLineWithChoices(string text, string leftText, string rightText)
    {
        yield return StartCoroutine(TypewriterLine(text));
        OpenChoicePanel(leftText, rightText);
    }

    IEnumerator ShowThenFinalize(string text, int price)
    {
        if (PlayerWallet.Instance != null && PlayerWallet.Instance.Money < price)
        {
            _dialogueCoroutine = StartCoroutine(ShowThenEnd("fuckoff broke bitch get ur money up"));
            yield break;
        }

        yield return StartCoroutine(TypewriterLine(text));
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);

        if (dialogueText != null) dialogueText.gameObject.SetActive(false);

        if (PlayerWallet.Instance != null)
            PlayerWallet.Instance.AddMoney(-price);

        if (guitarController != null)
            guitarController.ForceEquipGuitar();

        PlayerController.isInDialogue = false;
        _state = State.Done;
        _dialogueCoroutine = null;
    }

    IEnumerator ShowThenEnd(string text, bool permanent = false)
    {
        yield return StartCoroutine(TypewriterLine(text));
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);

        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        _state = permanent ? State.Done : State.Idle;
        PlayerController.isInDialogue = false;
        if (_playerInRange && _state == State.Idle)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }
        _dialogueCoroutine = null;
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

        // Legacy UI Text doesn't have maxVisibleCharacters, so use Substring per
        // char (one allocation per char) instead of `text += c` (O(n²) total).
        yield return DialogueTextStyling.RevealCharsLegacy(dialogueText, line, charDelay, () => _skipTyping);

        if (typewriterSource != null && typewriterSource.isPlaying)
            typewriterSource.Stop();

        _isTyping = false;
        _skipTyping = false;
    }

    void OpenChoicePanel(string leftText, string rightText)
    {
        if (choicePanel == null) return;
        if (leftButtonText != null)  leftButtonText.text = leftText;
        if (rightButtonText != null) rightButtonText.text = rightText;
        choicePanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CloseChoicePanel()
    {
        if (choicePanel != null && choicePanel.activeSelf)
        {
            choicePanel.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void StopActiveDialogue()
    {
        if (_dialogueCoroutine != null)
        {
            StopCoroutine(_dialogueCoroutine);
            _dialogueCoroutine = null;
        }
        if (typewriterSource != null && typewriterSource.isPlaying)
            typewriterSource.Stop();
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
    }

    public string GetStateString() => _state.ToString();

    public void ApplyStateFromString(string stateString)
    {
        if (System.Enum.TryParse<State>(stateString, out var s))
        {
            // Mid-conversation states won't survive a save/load cleanly — collapse to Idle
            // unless the trade was already permanently Done.
            _state = (s == State.Done) ? State.Done : State.Idle;
        }
        if (_state == State.Done) InteractPromptUI.Clear(this);
    }
}
