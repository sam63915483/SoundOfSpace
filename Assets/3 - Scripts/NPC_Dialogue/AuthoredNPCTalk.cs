using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Dialogue for an authored NPC. Lives on the same empty as the
/// AuthoredNPCSpawner; the spawned body relays range/gaze back here.
///
/// Out of the box: walk up, press Interact, the NPC speaks <c>lines</c>
/// (typewriter, click to advance) and that is the conversation. For a quest
/// NPC, subclass it and override <see cref="Conversation"/> -- the helpers
/// <see cref="Speak(string[])"/> and <see cref="Choose"/> give you typewriter
/// lines and the shared choice panel, exactly the TevDialogue pattern. Lines
/// are public string arrays so Sam edits the voice in the Inspector without a
/// recompile.
///
/// UI: borrows the shared dialogue TMP from any NPCDialogue in the scene
/// (the PHOSPHOR box restyles that one TMP for every NPC), like Tev does.
/// </summary>
public class AuthoredNPCTalk : MonoBehaviour
{
    [Header("Lines (default conversation: just says these)")]
    [TextArea(2, 5)]
    public string[] lines = { "Hello there." };

    [Header("Typewriter")]
    public float charDelay = 0.03f;
    [SerializeField] AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] float typewriterVolume = 0.3f;

    [Header("UI (auto-borrowed from NPCDialogue if empty)")]
    public TextMeshProUGUI dialogueText;

    [Tooltip("A conversation started by script (ForceStart) keeps going while the player is within this many metres, even outside the talk trigger.")]
    public float forcedTalkRange = 18f;

    protected AuthoredNPCSpawner Spawner { get; private set; }
    public bool IsTalking => _active;
    public string NpcName => Spawner != null ? Spawner.npcName : name;

    AudioSource _typeSource;
    bool _active, _isTyping, _skipTyping, _waitingForClick, _forced;
    int _choice = -1;
    Coroutine _routine;
    string _promptCached;
    TutorialGate.InputSource _promptSource = (TutorialGate.InputSource)(-1);

    protected virtual void Awake()
    {
        Spawner = GetComponent<AuthoredNPCSpawner>();
        if (Spawner == null)
            Debug.LogError($"[AuthoredNPCTalk] {name} has no AuthoredNPCSpawner on the same object.", this);
    }

    protected virtual void Start()
    {
        _typeSource = gameObject.AddComponent<AudioSource>();
        _typeSource.playOnAwake = false;
        _typeSource.loop = true;
        _typeSource.volume = typewriterVolume;

        if (dialogueText == null)
        {
            var existing = FindObjectOfType<NPCDialogue>();
            if (existing != null) dialogueText = existing.dialogueText;
        }
        DialogueTextStyling.ApplyOutline(dialogueText);
    }

    /// In range for the CURRENT conversation: the talk trigger normally, a
    /// generous distance for a script-started one (the player may be a few
    /// metres off watching the reunion).
    bool InRange
    {
        get
        {
            if (Spawner == null || Spawner.Relay == null) return false;
            if (_forced)
            {
                var p = LocalPlayer();
                return p != null && (p.position - Spawner.Body.transform.position).sqrMagnitude
                                    <= forcedTalkRange * forcedTalkRange;
            }
            return Spawner.Relay.PlayerInRange;
        }
    }

    protected static Transform LocalPlayer()
    {
        var all = PlayerRoster.All();
        for (int i = 0; i < all.Count; i++)
            if (all[i].IsLocal && all[i].Transform != null) return all[i].Transform;
        var go = GameObject.FindWithTag("Player");
        return go != null ? go.transform : null;
    }

    protected virtual void Update()
    {
        if (Spawner == null || Spawner.Relay == null) return;
        bool inRange = Spawner.Relay.PlayerInRange;

        if (inRange && !_active)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptSource)
            {
                _promptSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to talk to {NpcName}";
            }
            InteractPromptUI.Show(this, _promptCached);

            if (InteractGaze.IsLookingAt(Spawner.Relay)
                && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
            {
                StartConversation(false);
                return;
            }
        }
        else if (!inRange && !_active)
        {
            InteractPromptUI.Clear(this);
        }

        if (!_active) return;

        if (!InRange) { StopConversation(); return; }

        if (TutorialGate.PrimaryActionPressed())
        {
            if (_isTyping) _skipTyping = true;
            else if (_waitingForClick) _waitingForClick = false;
        }
    }

    /// <summary>Start the conversation from script (a quest beat), player looking or not.</summary>
    public void ForceStart()
    {
        if (_active || Spawner == null || Spawner.Body == null) return;
        StartConversation(true);
    }

    void StartConversation(bool forced)
    {
        if (_active) return;
        _active = true;
        _forced = forced;
        InteractPromptUI.Clear(this);
        if (dialogueText != null) dialogueText.gameObject.SetActive(true);
        PlayerController.isInDialogue = true;
        NPCConversationTracker.NotifyStart(this);
        if (Spawner.Wander != null) Spawner.Wander.Hold = true;
        _routine = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return Conversation();
        StopConversation();
    }

    protected void StopConversation()
    {
        if (!_active) return;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        if (_typeSource != null && _typeSource.isPlaying) _typeSource.Stop();
        _active = false;
        _forced = false;
        _isTyping = _skipTyping = _waitingForClick = false;
        _choice = -1;
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
        if (Spawner != null && Spawner.Wander != null) Spawner.Wander.Hold = false;
        OnConversationEnded();
    }

    /// <summary>Hook for subclasses (fires on every end, including walk-aways).</summary>
    protected virtual void OnConversationEnded() { }

    /// <summary>Override for quest NPCs. Default: speak <c>lines</c>.</summary>
    protected virtual IEnumerator Conversation()
    {
        yield return Speak(lines);
    }

    // -- helpers for subclasses -------------------------------------------

    protected IEnumerator Speak(string[] ls)
    {
        if (ls == null) yield break;
        for (int i = 0; i < ls.Length; i++)
        {
            if (!InRange) yield break;
            yield return Speak(ls[i]);
        }
    }

    protected IEnumerator Speak(string line)
    {
        if (dialogueText == null || string.IsNullOrEmpty(line)) yield break;
        _isTyping = true;
        _skipTyping = false;
        if (typewriterLoopClip != null && _typeSource != null)
        {
            _typeSource.clip = typewriterLoopClip;
            _typeSource.volume = typewriterVolume;
            _typeSource.Play();
        }
        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);
        if (_typeSource != null && _typeSource.isPlaying) _typeSource.Stop();
        _isTyping = false;
        _skipTyping = false;

        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !InRange);
    }

    protected static string OneOf(string[] pool)
    {
        if (pool == null || pool.Length == 0) return "...";
        return pool[Random.Range(0, pool.Length)];
    }

    /// <summary>
    /// Show the shared choice panel with these labels. Yields until the player
    /// picks (result in <see cref="LastChoice"/>) or walks away (-1).
    /// </summary>
    protected IEnumerator Choose(params string[] labels)
    {
        _choice = -1;
        if (PostGreetingChoicePanel.Instance == null) yield break;
        var rows = new List<PostGreetingChoicePanel.Row>(labels.Length);
        for (int i = 0; i < labels.Length; i++) rows.Add(new PostGreetingChoicePanel.Row(labels[i], true));
        PostGreetingChoicePanel.Instance.Show(rows, i => _choice = i);
        yield return new WaitUntil(() => _choice >= 0 || !InRange);
        if (PostGreetingChoicePanel.Instance.IsVisible) PostGreetingChoicePanel.Instance.Hide();
    }

    protected int LastChoice => _choice;

    // -- story flags (StoryDirector-backed, persisted in the world save) --
    protected static bool Flag(string flagName) =>
        StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(flagName);
    protected static void SetFlag(string flagName, bool value)
    {
        if (StoryDirector.Instance != null) StoryDirector.Instance.SetFlag(flagName, value);
    }

    void OnDisable()
    {
        if (_active) StopConversation();
        InteractPromptUI.Clear(this);
    }
}
