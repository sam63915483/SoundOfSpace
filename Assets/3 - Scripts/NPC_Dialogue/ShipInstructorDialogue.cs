using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Mission 1 Pilot branch — the ship-school instructor (goes on SHIPSCHOOL/Alien7, replacing
/// Alien7Vendor). Flow (user's design):
///   • First talk: a menu appears, but "Take the test" is DISABLED until you've heard the
///     briefing ("How does the test work?"). Hearing it unlocks the test option.
///   • "Take the test" → "Take the test for $20?" Yes/No. Costs $20 every attempt.
///   • Yes + can afford → deducts $20 and drops you into the VR drone (ShipPilotTest.BeginTest).
///   • Once licensed: congratulates you and points you to your real ship.
///
/// Mirrors TevDialogue's conversation shell + uses the shared PostGreetingChoicePanel.
/// </summary>
public class ShipInstructorDialogue : MonoBehaviour
{
    [Header("UI References (auto-borrowed from any NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Cost")]
    public int testCost = 20;

    [Header("Lines — greeting")]
    [TextArea(2, 5)]
    public string[] greetingLines = new[]
    {
        "So Tev sent you to learn to fly. Good — better the drones take the knocks than a real hull.",
    };

    [Header("Lines — briefing (unlocks the test)")]
    [TextArea(2, 5)]
    public string[] briefingLines = new[]
    {
        "Here's the drill. You put on the goggles and fly a scaled-down replica — flies exactly like the real ship, same stick.",
        "Take off, fly one full lap around Humble Abode, and set down back on the pad. Do that clean and you've earned your licence.",
        "Lose your nerve? Tap F twice to pull the goggles off — but that's a fail. Fly into the ground, same thing. It's $20 a go, so fly smart.",
    };

    [Header("Lines — entering the test")]
    [TextArea(2, 5)]
    public string[] enterTestLines = new[]
    {
        "Goggles on. Let's see what you've got.",
    };

    [Header("Lines — can't afford")]
    [TextArea(2, 5)]
    public string[] cantAffordLines = new[]
    {
        "Test's $20. Come back when your pockets aren't so light.",
    };

    [Header("Lines — already licensed")]
    [TextArea(2, 5)]
    public string[] licensedLines = new[]
    {
        "Ha! You already passed — licence and all.",
        "That licence is your ticket to buy a ship of your own. Want to run the course again for practice? Still $20 a go.",
    };

    [Header("Typewriter")]
    public float charDelay = 0.03f;
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    bool _playerInRange;
    bool _conversationActive;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    Coroutine _dialogueCoroutine;

    void Start()
    {
        typewriterSource = GetComponent<AudioSource>();
        if (typewriterSource == null) typewriterSource = gameObject.AddComponent<AudioSource>();
        typewriterSource.playOnAwake = false;
        typewriterSource.loop = true;
        typewriterSource.volume = typewriterVolume;

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
    }

    bool TestRunning => ShipPilotTest.Instance != null && ShipPilotTest.Instance.TestActive;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
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
        bool canPrompt = _playerInRange && !_conversationActive && !TestRunning;
        if (canPrompt)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptCachedSource)
            {
                _promptCachedSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to talk";
            }
            InteractPromptUI.Show(this, _promptCached);
        }

        if (canPrompt && InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
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

    void StartConversation()
    {
        if (_conversationActive) return;
        _conversationActive = true;
        InteractPromptUI.Clear(this);
        if (dialogueText != null) dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);
        _dialogueCoroutine = StartCoroutine(PlayDialogueSequence());
    }

    // Used when the conversation ends because the test is starting — don't re-show the
    // talk prompt (the player is about to be put into the drone).
    void EndConversationForTest()
    {
        if (_dialogueCoroutine != null) { StopCoroutine(_dialogueCoroutine); _dialogueCoroutine = null; }
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        _conversationActive = false;
        _isTyping = _skipTyping = _waitingForClick = false;
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        InteractPromptUI.Clear(this);
    }

    void StopConversation()
    {
        if (_dialogueCoroutine != null) { StopCoroutine(_dialogueCoroutine); _dialogueCoroutine = null; }
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        _conversationActive = false;
        _isTyping = _skipTyping = _waitingForClick = false;
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (_playerInRange && !TestRunning) InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
    }

    IEnumerator PlayDialogueSequence()
    {
        bool licensed = Mission1.Get(Mission1.FlagLicensed);
        yield return SpeakLines(licensed ? licensedLines : greetingLines);
        if (!_playerInRange) { StopConversation(); yield break; }

        // Menu loop — the test is always offerable (you can re-run the course for $20 any time,
        // even after you're licensed, for practice).
        while (_playerInRange)
        {
            bool briefed = Mission1.Get(Mission1.FlagInstructorBriefed) || Mission1.Get(Mission1.FlagLicensed);
            bool canAfford = PlayerWallet.Instance != null && PlayerWallet.Instance.Money >= testCost;

            var rows = new List<PostGreetingChoicePanel.Row>
            {
                new PostGreetingChoicePanel.Row($"Take the test (${testCost})", briefed && canAfford),
                new PostGreetingChoicePanel.Row(briefed ? "Remind me how the test works" : "How does the test work?", true),
                new PostGreetingChoicePanel.Row("Leave", true),
            };
            int choice = -1;
            PostGreetingChoicePanel.Instance.Show(rows, i => choice = i);
            yield return new WaitUntil(() => choice >= 0 || !_playerInRange);
            if (!_playerInRange) { StopConversation(); yield break; }

            if (choice == 0)
            {
                int confirm = -1;
                var confirmRows = new List<PostGreetingChoicePanel.Row>
                {
                    new PostGreetingChoicePanel.Row($"Yes - take the test (-${testCost})", true),
                    new PostGreetingChoicePanel.Row("No, not yet", true),
                };
                PostGreetingChoicePanel.Instance.Show(confirmRows, i => confirm = i);
                yield return new WaitUntil(() => confirm >= 0 || !_playerInRange);
                if (!_playerInRange) { StopConversation(); yield break; }

                if (confirm == 0)
                {
                    if (PlayerWallet.Instance != null && PlayerWallet.Instance.SpendMoney(testCost))
                    {
                        yield return SpeakLines(enterTestLines);
                        EndConversationForTest();
                        if (ShipPilotTest.Instance != null) ShipPilotTest.Instance.BeginTest();
                        yield break;
                    }
                    yield return SpeakLines(cantAffordLines);
                }
                // else: fall through, re-show menu
            }
            else if (choice == 1)
            {
                yield return SpeakLines(briefingLines);
                Mission1.Set(Mission1.FlagInstructorBriefed, true);
                // loop — the test option is now enabled
            }
            else
            {
                break;   // Leave
            }
        }

        StopConversation();
    }

    IEnumerator SpeakLines(string[] lines)
    {
        if (lines == null) yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!_playerInRange) yield break;
            yield return StartCoroutine(TypewriterLine(lines[i]));
            yield return StartCoroutine(WaitForPlayerClick());
            if (!_playerInRange) yield break;
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
        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);
        if (typewriterSource != null && typewriterSource.isPlaying) typewriterSource.Stop();
        _isTyping = false;
        _skipTyping = false;
    }

    IEnumerator WaitForPlayerClick()
    {
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);
    }
}
