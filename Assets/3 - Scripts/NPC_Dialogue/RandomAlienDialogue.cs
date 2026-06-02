using UnityEngine;
using TMPro;
using System.Collections;

public class RandomAlienDialogue : MonoBehaviour
{
    [Header("UI References (auto-pulled from existing NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Random Voicelines (rolled fresh on every talk)")]
    [TextArea(2, 5)]
    public string[] randomLines =
    {
        "Oh, a human! I haven't seen one of those since the buffet on Zorblax 4.",
        "Don't tell anyone, but I'm pretty sure I'm lost.",
        "Have you tried the rocks here? Ten outta ten. No notes.",
        "I came here on vacation. The reviews lied.",
        "If you see my left antenna, please return it. No questions asked.",
        "I used to be tall. Then I met your gravity.",
        "Nobody warned me this planet had so much... outside.",
        "I sold my spaceship for a sandwich. Worth it.",
        "Tell the trees I said hi. They never write back.",
        "Hey, between us, is your sun supposed to be doing that?",
        "I'm not stuck here. You're stuck here. With me. Think about it."
    };

    [Header("Typewriter")]
    public float charDelay = 0.03f;

    [Header("Typewriter Sound")]
    [SerializeField] private AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] private float typewriterVolume = 0.3f;
    private AudioSource typewriterSource;

    bool _playerInRange;
    bool _conversationActive;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    // See Alien7Vendor — set when player picks "Leave"; gates the talk prompt
    // and F-to-talk until OnTriggerEnter clears it (player walked out + back).
    bool _suppressPromptUntilExit;
    Coroutine _dialogueCoroutine;
    NPCSellDustOption _sellDustOption;

    void Start()
    {
        if (dialogueText == null || talkPromptText == null)
        {
            var existing = FindObjectOfType<NPCDialogue>();
            if (existing != null)
            {
                if (dialogueText == null)   dialogueText   = existing.dialogueText;
                if (talkPromptText == null) talkPromptText = existing.talkPromptText;
            }
        }

        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);

        typewriterSource = GetComponent<AudioSource>();
        if (typewriterSource == null) typewriterSource = gameObject.AddComponent<AudioSource>();
        typewriterSource.playOnAwake = false;
        typewriterSource.loop = true;
        typewriterSource.volume = typewriterVolume;
    }

    // Called by AlienNPCSpawner after it adds this component at runtime, so every
    // spawned alien speaks with the spawner's shared voice clip (this component
    // isn't on a prefab, so the clip can't be Inspector-assigned per-alien).
    public void SetVoice(AudioClip clip, float volume)
    {
        typewriterLoopClip = clip;
        typewriterVolume = volume;
        if (typewriterSource != null) typewriterSource.volume = volume;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
        _suppressPromptUntilExit = false;
        ShowPrompt();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
        if (_conversationActive) StopConversation();
    }

    void Update()
    {
        if (_playerInRange && !_conversationActive && !_suppressPromptUntilExit)
        {
            InteractPromptUI.Show(this, $"Press {PromptGlyphs.Interact} to talk");
        }

        if (_playerInRange && !_conversationActive && !_suppressPromptUntilExit && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
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

    // The dialogueText/talkPromptText are shared across NPCs; another NPC nearby
    // may still want the prompt visible. Be conservative — leave it on if any
    // other RandomAlienDialogue or NPCDialogue is in range.
    bool IsAnotherNPCUsingPrompt()
    {
        var aliens = SpawnedAlienNPC.AllAliens;
        for (int i = 0; i < aliens.Count; i++)
        {
            if (aliens[i] == null || aliens[i].gameObject == gameObject) continue;
            var d = aliens[i].GetComponent<RandomAlienDialogue>();
            if (d != null && d._playerInRange) return true;
        }
        return false;
    }

    void StartConversation()
    {
        if (_conversationActive) return;
        _conversationActive = true;
        InteractPromptUI.Clear(this);
        if (dialogueText != null)   dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);

        _sellDustOption = NPCSellDustOption.GetOrAdd(this);
        // Random aliens roll wider/swingier than vendors.
        _sellDustOption.minChance = 0.15f;
        _sellDustOption.maxChance = 0.75f;
        _sellDustOption.minPricePerDust = 4;
        _sellDustOption.maxPricePerDust = 18;
        _sellDustOption.RollFresh();

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

        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (SpaceDustSellUI.Instance != null && SpaceDustSellUI.Instance.IsOpen)
            SpaceDustSellUI.Instance.Close();

        // Suppress prompt until player walks out + back in — see field comment.
        _suppressPromptUntilExit = true;
        InteractPromptUI.Clear(this);
    }

    IEnumerator PlayDialogueSequence()
    {
        string line = (randomLines != null && randomLines.Length > 0)
            ? randomLines[Random.Range(0, randomLines.Length)]
            : "";

        if (!string.IsNullOrEmpty(line))
        {
            yield return StartCoroutine(TypewriterLine(line));
            yield return StartCoroutine(WaitForPlayerClick());
        }

        if (!_playerInRange) yield break;
        ShowPostGreetingChoice();
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

    void ShowPostGreetingChoice()
    {
        bool hasDust = SpaceDustInventory.Instance != null && SpaceDustInventory.Instance.Count > 0;
        var rows = new System.Collections.Generic.List<PostGreetingChoicePanel.Row>
        {
            new PostGreetingChoicePanel.Row(hasDust ? "Sell space dust" : "Sell space dust (no dust)", hasDust),
            new PostGreetingChoicePanel.Row("Leave", true),
        };
        PostGreetingChoicePanel.Instance.Show(rows, HandleChoice);
    }

    void HandleChoice(int index)
    {
        switch (index)
        {
            case 0: OpenSellDust(); break;
            case 1: StopConversation(); break;
        }
    }

    void OpenSellDust()
    {
        if (_sellDustOption == null) { StopConversation(); return; }
        SpaceDustSellUI.Instance.Open(
            npcName: "Wandering Alien",
            acceptChance: _sellDustOption.AcceptChance,
            pricePerDust: _sellDustOption.PricePerDust,
            preferredMaxQty: _sellDustOption.PreferredMaxQty,
            onClose: ShowPostGreetingChoice
        );
    }
}
