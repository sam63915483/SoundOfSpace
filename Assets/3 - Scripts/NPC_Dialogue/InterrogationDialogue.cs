using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class InterrogationDialogue : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI locationText;
    public TextMeshProUGUI dialogueText;
    public GameObject choicePanel;
    public Button button1;
    public Button button2;
    public Button button3;
    public GameObject outcomePanel;
    public TextMeshProUGUI outcomeTitleText;
    public TextMeshProUGUI outcomeSubText;

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    bool _waitingForOutcomeClick;
    bool _isEnding;
    int _choiceIndex = -1;

    static readonly string[] introLines =
    {
        "Officer Vance, ORG Security Division. Sit down.",
        "A group of extremists — terrorists, to be precise — stole classified ORG intelligence files. Files that could destabilize entire systems.",
        "Those files are hidden somewhere in a distant solar system. We need them back, quietly.",
        "You have survival skills. Fishing expertise. And most importantly — no paper trail. You're perfect for this.",
        "The offer: full financial compensation and your freedom. All you have to do is retrieve what was taken."
    };

    static readonly string[] blackmailLines =
    {
        "That's... unfortunate. We were hoping to do this the easy way.",
        "[The officer places a tablet on the table. Doctored footage of you at what appears to be a rebel meeting.]",
        "We have extensive documentation of your recent activities. Treasonous activities.",
        "Comply, or we make it all very public. Your choice."
    };

    static readonly string[] prisonLines =
    {
        "[You are escorted to a cell deep in the ORG facility.]",
        "[Time passes. The fluorescent light hums. 30 minutes feel like weeks.]",
        "[A guard — not one you've seen before — steps close to the bars.]",
        "Guard: 'I can get you out. But you'd owe them everything. The dark path. But you'd be free.'"
    };

    static readonly string[] fullPitchLines =
    {
        "Of course. The rebellion calls themselves the Accord. They believe ORG is suppressing interplanetary resources.",
        "They're wrong. Or rather — they've been manipulated into believing that.",
        "The files they stole contain mission logs, personnel data, and route maps. In the wrong hands, catastrophic.",
        "You'd travel solo. No ORG branding. Blend in. Retrieve the files. Return. Simple.",
        "We'd compensate you generously. And your record — whatever's on it — goes clean."
    };

    void Start()
    {
        choicePanel.SetActive(false);
        outcomePanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        button1.onClick.AddListener(() => OnChoice(0));
        button2.onClick.AddListener(() => OnChoice(1));
        button3.onClick.AddListener(() => OnChoice(2));

        locationText.text = "Interrogation Room";
        StartCoroutine(RunDialogue());
    }

    void Update()
    {
        if (choicePanel.activeSelf) return;

        if (outcomePanel.activeSelf)
        {
            if (!_isEnding && TutorialGate.PrimaryActionPressed())
                _waitingForOutcomeClick = false;
            return;
        }

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping)             _skipTyping = true;
            else if (_waitingForClick) _waitingForClick = false;
        }
    }

    IEnumerator RunDialogue()
    {
        foreach (string line in introLines)
        {
            yield return StartCoroutine(TypewriterLine(line));
            yield return StartCoroutine(WaitForClick());
        }

        yield return StartCoroutine(ShowChoice("Yes, I'll do it.", "No. Refuse.", "Tell me more."));
        int initial = _choiceIndex;

        if (initial == 0)      yield return StartCoroutine(PathA());
        else if (initial == 1) yield return StartCoroutine(PathB());
        else                   yield return StartCoroutine(PathC());
    }

    IEnumerator PathA()
    {
        yield return StartCoroutine(TypewriterLine("Good. You leave at dawn. Don't ask questions."));
        yield return StartCoroutine(WaitForClick());
        ShowOutcome(
            "OUTCOME: Mission Accepted",
            "You agreed without questions. You begin your mission with neutral standing with ORG."
        );
    }

    IEnumerator PathB()
    {
        foreach (string line in blackmailLines)
        {
            yield return StartCoroutine(TypewriterLine(line));
            yield return StartCoroutine(WaitForClick());
        }

        yield return StartCoroutine(ShowChoice("Fine. I'll do it.", "Go to hell. I'm not doing it.", null));
        int choice = _choiceIndex;

        if (choice == 0) yield return StartCoroutine(PathB_Comply());
        else             yield return StartCoroutine(PathB_RefuseAgain());
    }

    IEnumerator PathB_Comply()
    {
        yield return StartCoroutine(TypewriterLine("Wise. You leave at dawn."));
        yield return StartCoroutine(WaitForClick());
        ShowOutcome(
            "OUTCOME: Coerced Compliance",
            "Blackmailed into accepting. You begin your mission with low trust with ORG."
        );
    }

    IEnumerator PathB_RefuseAgain()
    {
        locationText.text = "ORG Prison Cell";

        foreach (string line in prisonLines)
        {
            yield return StartCoroutine(TypewriterLine(line));
            yield return StartCoroutine(WaitForClick());
        }

        yield return StartCoroutine(ShowChoice("Deal. Get me out.", "No. I'd rather rot.", null));
        int choice = _choiceIndex;

        if (choice == 0)
        {
            ShowOutcome(
                "OUTCOME: The Dark Path",
                "You escaped prison on ORG's terms. You begin your mission aligned with ORG's darkest agenda."
            );
        }
        else
        {
            yield return StartCoroutine(TypewriterLine("[The warden — an ORG operative — enters the cell. The lights go out.]"));
            yield return StartCoroutine(WaitForClick());
            ShowOutcome(
                "YOU BROKE THE TIMELINE AND DESTROYED REALITY",
                "You chose your morals over freedom. The warden — an ORG operative — ensured you'd never leave.\n\nSPECIAL AWARD UNLOCKED.",
                isEnding: true
            );
        }
    }

    IEnumerator PathC()
    {
        foreach (string line in fullPitchLines)
        {
            yield return StartCoroutine(TypewriterLine(line));
            yield return StartCoroutine(WaitForClick());
        }

        yield return StartCoroutine(ShowChoice("Alright. I'm in.", "I understand the mission. But no.", null));
        int choice = _choiceIndex;

        if (choice == 0)
        {
            yield return StartCoroutine(TypewriterLine("Excellent. I knew you'd see reason."));
            yield return StartCoroutine(WaitForClick());
            ShowOutcome(
                "OUTCOME: Willing Ally",
                "You heard the full pitch and agreed. You begin your mission with high trust with ORG."
            );
        }
        else
        {
            // Blackmail sequence
            foreach (string line in blackmailLines)
            {
                yield return StartCoroutine(TypewriterLine(line));
                yield return StartCoroutine(WaitForClick());
            }

            yield return StartCoroutine(ShowChoice("Fine. I'll do it.", "Still no.", null));
            int postBlackmail = _choiceIndex;

            if (postBlackmail == 0)
            {
                yield return StartCoroutine(TypewriterLine("Good. You leave at dawn."));
                yield return StartCoroutine(WaitForClick());
                ShowOutcome(
                    "OUTCOME: Mission Accepted",
                    "You asked questions, but ultimately complied. You begin your mission with neutral standing with ORG."
                );
            }
            else
            {
                yield return StartCoroutine(PathB_RefuseAgain());
            }
        }
    }

    IEnumerator ShowChoice(string b1Label, string b2Label, string b3Label)
    {
        _choiceIndex = -1;

        button1.GetComponentInChildren<TextMeshProUGUI>().text = b1Label;
        button2.GetComponentInChildren<TextMeshProUGUI>().text = b2Label;

        bool hasThird = b3Label != null;
        button3.gameObject.SetActive(hasThird);
        if (hasThird)
            button3.GetComponentInChildren<TextMeshProUGUI>().text = b3Label;

        button1.interactable = true;
        button2.interactable = true;

        dialogueText.text = "";
        choicePanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        yield return new WaitUntil(() => _choiceIndex >= 0);

        choicePanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnChoice(int index) => _choiceIndex = index;

    void ShowOutcome(string title, string sub, bool isEnding = false)
    {
        _isEnding = isEnding;
        dialogueText.text = "";
        outcomeTitleText.text = title;
        outcomeSubText.text = sub;
        outcomePanel.SetActive(true);

        if (!isEnding)
            StartCoroutine(LoadAfterOutcome());
    }

    IEnumerator LoadAfterOutcome()
    {
        _waitingForOutcomeClick = true;
        yield return new WaitUntil(() => !_waitingForOutcomeClick);
        SceneManager.LoadScene("1.6.7.7.7");
    }

    IEnumerator TypewriterLine(string line)
    {
        _isTyping = true;
        _skipTyping = false;
        // Zero-allocation char reveal via TMP's maxVisibleCharacters.
        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);
        _isTyping = false;
        _skipTyping = false;
    }

    IEnumerator WaitForClick()
    {
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick);
    }
}
