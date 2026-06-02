using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ORGDialogue : MonoBehaviour
{
    [Header("Ship")]
    public ShipFlyIn shipFlyIn;

    [Header("UI References")]
    public TextMeshProUGUI dialogueText;
    public GameObject choicePanel;
    public Button yesButton;
    public Button noButton;
    public GameObject noLicensePopup;   // small popup that fades after 1s

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Timing")]
    public float fadeDelay = 5f;

    bool _choiceMade;
    bool _choseYes;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;

    void Start()
    {
        if (dialogueText != null)   dialogueText.gameObject.SetActive(false);
        if (choicePanel != null)    choicePanel.SetActive(false);
        if (noLicensePopup != null) noLicensePopup.SetActive(false);

        if (yesButton != null) yesButton.onClick.AddListener(OnYesClicked);
        if (noButton != null)  noButton.onClick.AddListener(OnNoClicked);

        if (shipFlyIn != null)
            shipFlyIn.onArrived += StartSequence;
    }

    void OnDestroy()
    {
        if (shipFlyIn != null)
            shipFlyIn.onArrived -= StartSequence;
    }

    void Update()
    {
        if (choicePanel != null && choicePanel.activeSelf) return;

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping)             _skipTyping = true;
            else if (_waitingForClick) _waitingForClick = false;
        }
    }

    void StartSequence() => StartCoroutine(DialogueSequence());

    IEnumerator DialogueSequence()
    {
        dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;

        yield return StartCoroutine(TypewriterLine("HALT CITIZEN, PROVIDE IDENTIFICATION"));
        yield return StartCoroutine(WaitForClick());

        // Show YES / NO
        _choiceMade = false;
        _choseYes   = false;
        choicePanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        yield return new WaitUntil(() => _choiceMade);

        if (_choseYes)
        {
            // Show popup, grey out YES, wait for NO
            if (noLicensePopup != null) noLicensePopup.SetActive(true);
            yield return new WaitForSeconds(1f);
            if (noLicensePopup != null) noLicensePopup.SetActive(false);

            // Grey out YES button
            if (yesButton != null)
            {
                yesButton.interactable = false;
                var img = yesButton.GetComponent<Image>();
                if (img != null) img.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            }

            // Wait for NO
            _choiceMade = false;
            yield return new WaitUntil(() => _choiceMade);
        }

        // Close panel
        choicePanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Re-enable player movement immediately after NO
        dialogueText.gameObject.SetActive(false);
        PlayerController.isInDialogue = false;

        // Hidden 5s timer
        yield return new WaitForSeconds(fadeDelay);

        // Final warning
        dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;
        yield return StartCoroutine(TypewriterLine("STOP RESISTING OR WE WILL TAKE FORCEFUL MEASURES"));
        yield return new WaitForSeconds(1.5f);

        PlayerController.isInDialogue = false;
        if (FlashbackManager.Instance != null)
            FlashbackManager.Instance.TriggerCutscene();
    }

    IEnumerator TypewriterLine(string line)
    {
        _isTyping   = true;
        _skipTyping = false;
        // Zero-allocation char reveal via TMP's maxVisibleCharacters.
        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);
        _isTyping   = false;
        _skipTyping = false;
    }

    IEnumerator WaitForClick()
    {
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick);
    }

    void OnYesClicked() { _choiceMade = true; _choseYes = true; }
    void OnNoClicked()  { _choiceMade = true; _choseYes = false; }
}
