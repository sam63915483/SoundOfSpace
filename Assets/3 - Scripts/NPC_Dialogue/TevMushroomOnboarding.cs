using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Tev's mushroom onboarding — the entry point of the cozy economy loop
/// (docs/Handoff_CozyLoop_Switch_MushroomSlice_v1.md §2).
///
/// Drop this on the Tev GameObject beside the existing <see cref="TevDialogue"/>.
/// It takes the conversation over while the onboarding is live and, per the
/// handoff, DISABLES rather than deletes the deprecated on-landing behaviour
/// (the wave, and the Mission 1 dialogue tree that's tied to on-hold story
/// content). Nothing is destroyed: flip <see cref="restoreMissionDialogue"/> and
/// TevDialogue comes back the moment the onboarding finishes.
///
/// Timing (§2.1): Tev is HIDDEN for <see cref="hiddenSeconds"/> from the moment
/// the shuttle's exit ramp deploys, so the player gets a quiet window to loot
/// the locker and chop trees with no NPC pressure. Then he's just standing
/// outside his cabin, idle and interactable. No forced tutorial, no waypoint.
///
/// Conversation (§2.3): first talk fronts three mushrooms. Every talk after
/// that asks two questions with GREYED-OUT options — the panel already supports
/// visible-but-unselectable rows, so nothing new was needed there — and branches
/// on what's true, not on what the player claims:
///   • still holding some     → sends you back out
///   • sold some, holding none→ teaches the loop, done
///   • sold none, holding none→ ridicules you and fronts another three, up to
///                              MushroomQuest.MaxRefronts times, then he's done
///                              fronting and points you at the wild ones
/// </summary>
public class TevMushroomOnboarding : MonoBehaviour
{
    [Header("UI References (auto-borrowed from any NPCDialogue if left empty)")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI talkPromptText;

    [Header("Timing")]
    [Tooltip("Seconds after the shuttle's exit ramp deploys before Tev appears outside his cabin. Handoff §2.1 = 120. He appears whether or not the player has left the shuttle.")]
    public float hiddenSeconds = 120f;
    [Tooltip("Metres the player must be within to get the talk prompt. 0 = derive it from this object's own trigger SphereCollider (scale included). Set a value to override.")]
    public float talkRadius = 0f;
    [Tooltip("Hard backstop: seconds after this component wakes at which Tev appears regardless of the exit ramp. Covers boots where the arrival sequence never runs (Play straight into the gameplay scene, dev spawns) — without it he'd stay hidden forever there.")]
    public float fallbackSeconds = 180f;

    [Header("Deprecated behaviour")]
    [Tooltip("Disable Tev's wave animation while the onboarding is live. The component is only disabled, never removed.")]
    public bool suppressWave = true;
    [Tooltip("Disable the old Mission 1 TevDialogue while the onboarding is live. The component is only disabled, never removed.")]
    public bool suppressMissionDialogue = true;
    [Tooltip("Re-enable TevDialogue once the onboarding completes. OFF while all mission/story content is on hold — turn it on to hand Tev back to the mission tree.")]
    public bool restoreMissionDialogue = false;

    [Header("Lines — first talk (fronts you three)")]
    [TextArea(2, 5)]
    public string[] firstTalkLines = new[]
    {
        "Most people knock, y'know. You parked a shuttle on my lawn.",
        "Relax — I'm not sore about it. Nothing much happens out here worth being sore about.",
        "Fresh off the pod, then. No money, no plan, and a suit that'll want feeding.",
        "Lucky for you there's exactly one business worth being in around here.",
        "Three caps, on the house. Find a buyer — anyone out here will take them, and they'll all quote you different.",
        "And hey. Don't eat the inventory.",
    };

    [Header("Lines — pack too full to take the batch")]
    [TextArea(2, 5)]
    public string[] packFullLines = new[]
    {
        "Your hands are full, friend. Make some room and come see me again.",
    };

    [Header("Lines — outcome A: still holding some")]
    [TextArea(2, 5)]
    public string[] stillHoldingLines = new[]
    {
        "Then get back out there. It's about the only thing folks still spend on —",
        "nobody's saving for a future with that thing hanging up there.",
    };

    [Header("Lines — outcome C: ate/lost the lot, gets another batch")]
    [TextArea(2, 5)]
    public string[] ridiculeLines = new[]
    {
        "Wait. All three? Gone?",
        "I hand you the easiest money in the system and you come back with lint.",
        "…Lesson one: never eat the product.",
    };

    [Header("Lines — handing over a REFRONT batch")]
    [TextArea(2, 5)]
    public string[] refrontLines = new[]
    {
        "Here. Three more. I must be getting soft.",
        "Sell them this time. I'm counting.",
    };

    [Header("Lines — out of patience (no more free batches)")]
    [TextArea(2, 5)]
    public string[] outOfPatienceLines = new[]
    {
        "No. I'm done handing you groceries.",
        "They grow wild out there — go find your own. Take the axe to them, they come apart easy enough.",
    };

    [Header("Lines — outcome B: sold some, teaches the loop (completes)")]
    [TextArea(2, 5)]
    public string[] teachLines = new[]
    {
        "Not bad. You've got a buyer and you've got a price. That's a business.",
        "Alright — trade secret. Those caps grow wild around here, and they like oxygen.",
        "More trees, richer air, faster shrooms. You want product? Start planting.",
        "Chop one and you'll get spores off it. Put them in the ground and the same cap comes back.",
    };

    [Header("Lines — after the onboarding is done")]
    [TextArea(2, 5)]
    public string[] doneLines = new[]
    {
        "Still at it? Good. Keep an eye on who's paying what.",
        "Plant more than you pick. That's the whole trick.",
        "Quiet day. Quiet year, really.",
    };

    [Header("Typewriter")]
    public float charDelay = 0.03f;
    [SerializeField] AudioClip typewriterLoopClip;
    [SerializeField, Range(0, 1)] float typewriterVolume = 0.3f;

    AudioSource _typewriterSource;
    TevDialogue _missionDialogue;
    NPCWaveAnimation _wave;
    Renderer[] _renderers;
    Collider[] _colliders;
    bool _visible = true;

    Transform _playerTf;
    float _nextPlayerSearch;
    float _startedAt;

    bool _playerInRange;
    bool _conversationActive;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    int _choice = -1;
    Coroutine _dialogueCoroutine;

    string _promptCached;
    TutorialGate.InputSource _promptCachedSource = (TutorialGate.InputSource)(-1);

    void Start()
    {
        _startedAt = Time.time;
        if (dialogueText == null || talkPromptText == null)
        {
            var existing = FindObjectOfType<NPCDialogue>();
            if (existing != null)
            {
                if (dialogueText == null) dialogueText = existing.dialogueText;
                if (talkPromptText == null) talkPromptText = existing.talkPromptText;
            }
        }
        DialogueTextStyling.ApplyOutline(dialogueText);
        DialogueTextStyling.ApplyOutline(talkPromptText);
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);

        _typewriterSource = gameObject.AddComponent<AudioSource>();
        _typewriterSource.playOnAwake = false;
        _typewriterSource.loop = true;
        _typewriterSource.volume = typewriterVolume;

        _missionDialogue = GetComponent<TevDialogue>();
        _wave = GetComponent<NPCWaveAnimation>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);

        ApplySuppression();
        InteractPromptUI.Clear(this);
    }

    /// The deprecated on-landing behaviour is switched OFF, not removed.
    void ApplySuppression()
    {
        bool onboardingLive = MushroomQuest.CurrentStage != MushroomQuest.Stage.Complete;
        if (_wave != null && suppressWave && _wave.enabled != !onboardingLive)
            _wave.enabled = !onboardingLive;
        if (_missionDialogue != null && suppressMissionDialogue)
        {
            bool wantMission = !onboardingLive && restoreMissionDialogue;
            if (_missionDialogue.enabled != wantMission) _missionDialogue.enabled = wantMission;
        }
    }

    // ── Hidden window ──────────────────────────────────────────────────────

    /// Tev only exists once the ramp has been down for hiddenSeconds. Before the
    /// ramp deploys at all he's hidden too — the player is still inside the pod.
    /// Once the onboarding is past its first talk he's permanently present, so a
    /// mid-game scene reload can't make him vanish for another two minutes.
    ///
    /// The backstop matters: the ramp beat doesn't run on every boot (pressing
    /// Play straight into the gameplay scene in the Editor, a dev spawn, a
    /// future flow that skips the arrival). Keying visibility PURELY off the
    /// door would leave Tev hidden forever on those, which reads to the player
    /// as an NPC that refuses to exist. So there's a hard ceiling either way.
    bool ShouldBeVisible()
    {
        if (MushroomQuest.CurrentStage != MushroomQuest.Stage.NotMet) return true;
        if (ShuttleExitDoor.HasOpened && Time.time - ShuttleExitDoor.OpenedAtTime >= hiddenSeconds)
            return true;
        return Time.time - _startedAt >= fallbackSeconds;
    }

    void SetVisible(bool on)
    {
        if (_visible == on) return;
        _visible = on;
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = on;
        if (_colliders != null)
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = on;
        if (!on)
        {
            _playerInRange = false;
            InteractPromptUI.Clear(this);
            if (_conversationActive) StopConversation();
        }
    }

    // ── Interaction ────────────────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        if (!_visible || !other.CompareTag("Player")) return;
        _playerInRange = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        InteractPromptUI.Clear(this);
        if (_conversationActive) StopConversation();
    }

    /// Distance fallback for "player is close enough to talk".
    ///
    /// This component TOGGLES Tev's colliders to hide him for the first two
    /// minutes, and a trigger that gets disabled while the player is standing
    /// inside it can miss its OnTriggerEnter when it comes back — which reads to
    /// the player as an NPC who simply refuses to talk. So range is also derived
    /// straight from distance every frame; the trigger callbacks stay as the
    /// fast path. Whichever says "in range" wins.
    void UpdateProximity()
    {
        if (_playerTf == null)
        {
            if (Time.time < _nextPlayerSearch) return;
            _nextPlayerSearch = Time.time + 1f;
            var pc = FindObjectOfType<PlayerController>();
            if (pc == null) return;
            _playerTf = pc.transform;
        }

        float radius = talkRadius;
        if (radius <= 0f)
        {
            // Derive from the authored trigger so this matches whatever range the
            // NPC was set up with, scale included.
            var sc = GetComponent<SphereCollider>();
            var ls = transform.lossyScale;
            radius = sc != null
                ? sc.radius * Mathf.Max(ls.x, Mathf.Max(ls.y, ls.z))
                : 5f;
        }

        bool near = (_playerTf.position - transform.position).sqrMagnitude <= radius * radius;
        if (near) _playerInRange = true;
        else if (_playerInRange && !_conversationActive)
        {
            _playerInRange = false;
            InteractPromptUI.Clear(this);
        }
        else if (!near && _conversationActive)
        {
            // Walked off mid-conversation.
            _playerInRange = false;
            StopConversation();
        }
    }

    void Update()
    {
        SetVisible(ShouldBeVisible());
        ApplySuppression();
        if (!_visible) return;

        UpdateProximity();

        if (_playerInRange && !_conversationActive)
        {
            var src = TutorialGate.LastSource;
            if (_promptCached == null || src != _promptCachedSource)
            {
                _promptCachedSource = src;
                _promptCached = $"Press {PromptGlyphs.Interact} to talk";
            }
            InteractPromptUI.Show(this, _promptCached);

            if (InteractGaze.IsLookingAt(this) && TutorialGate.InteractPressed(TutorialAbility.TalkToNPC))
            {
                StartConversation();
                return;
            }
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
        _dialogueCoroutine = StartCoroutine(PlaySequence());
    }

    void StopConversation()
    {
        if (_dialogueCoroutine != null)
        {
            StopCoroutine(_dialogueCoroutine);
            _dialogueCoroutine = null;
        }
        if (PostGreetingChoicePanel.Instance != null && PostGreetingChoicePanel.Instance.IsVisible)
            PostGreetingChoicePanel.Instance.Hide();
        _conversationActive = false;
        _isTyping = false;
        _skipTyping = false;
        _waitingForClick = false;
        _choice = -1;
        if (_typewriterSource != null && _typewriterSource.isPlaying) _typewriterSource.Stop();
        PlayerController.isInDialogue = false;
        if (dialogueText != null) dialogueText.gameObject.SetActive(false);
    }

    // ── The conversation ───────────────────────────────────────────────────

    IEnumerator PlaySequence()
    {
        switch (MushroomQuest.CurrentStage)
        {
            case MushroomQuest.Stage.NotMet:  yield return RunFirstTalk(); break;
            case MushroomQuest.Stage.Given:   yield return RunReturnTalk(); break;
            default:                          yield return SpeakOne(OneOf(doneLines)); break;
        }
        StopConversation();
    }

    IEnumerator RunFirstTalk()
    {
        yield return SpeakLines(firstTalkLines);
        if (!_playerInRange) yield break;

        int given = MushroomQuest.GrantBatch();
        if (given <= 0)
        {
            // Pack full (or no mushroom species resolved yet): say so and leave
            // the stage at NotMet so the whole beat re-offers next talk.
            yield return SpeakLines(packFullLines);
            yield break;
        }

        MushroomQuest.SoldCount = 0;
        MushroomQuest.CurrentStage = MushroomQuest.Stage.Given;
    }

    IEnumerator RunReturnTalk()
    {
        // Q1 — "So — did you sell any?"  [Yes] needs a real sale; [No] always open.
        int sold = MushroomQuest.SoldCount;
        yield return SpeakOne("So — did you sell any?");
        if (!_playerInRange) yield break;
        yield return AskChoice(
            new PostGreetingChoicePanel.Row("Yeah, I sold some.", sold >= 1),
            new PostGreetingChoicePanel.Row("No, not yet.", true));
        if (!_playerInRange) yield break;

        // Q2 — "Got any left?"  Exactly one of these is ever selectable; both are
        // always SHOWN, because seeing the greyed one is how the player learns he
        // can tell. (The panel dims disabled rows and refuses their input.)
        int held = MushroomQuest.HeldCount;
        yield return SpeakOne("Got any left?");
        if (!_playerInRange) yield break;
        yield return AskChoice(
            new PostGreetingChoicePanel.Row("Yeah, still got some on me.", held >= 1),
            new PostGreetingChoicePanel.Row("Nope. All gone.", held == 0));
        if (!_playerInRange) yield break;

        // Outcomes branch on the TRUTH (live inventory + sale count), not on
        // which row got clicked — the greying already guarantees they agree.
        if (held >= 1)
        {
            yield return SpeakLines(stillHoldingLines);   // stage stays Given
            yield break;
        }

        if (sold >= 1)
        {
            yield return SpeakLines(teachLines);
            MushroomQuest.CurrentStage = MushroomQuest.Stage.Complete;
            yield break;
        }

        // Held nothing, sold nothing. Ate them — or "lost" them into the locker.
        yield return SpeakLines(ridiculeLines);
        if (!_playerInRange) yield break;

        if (!MushroomQuest.CanRefront)
        {
            yield return SpeakLines(outOfPatienceLines);
            MushroomQuest.CurrentStage = MushroomQuest.Stage.Complete;
            yield break;
        }

        int given = MushroomQuest.GrantBatch();
        if (given <= 0)
        {
            yield return SpeakLines(packFullLines);
            yield break;
        }
        MushroomQuest.Refronts++;
        yield return SpeakLines(refrontLines);
    }

    /// Show a two-row choice and wait. Disabled rows are visible but dimmed and
    /// unselectable (PostGreetingChoicePanel handles that natively).
    IEnumerator AskChoice(params PostGreetingChoicePanel.Row[] rows)
    {
        if (PostGreetingChoicePanel.Instance == null) yield break;
        _choice = -1;
        var list = new List<PostGreetingChoicePanel.Row>(rows);
        PostGreetingChoicePanel.Instance.Show(list, i => _choice = i);
        yield return new WaitUntil(() => _choice >= 0 || !_playerInRange);
    }

    // ── line helpers (same shape as TevDialogue) ───────────────────────────

    IEnumerator SpeakLines(string[] lines)
    {
        if (lines == null) yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!_playerInRange) yield break;
            yield return SpeakOne(lines[i]);
        }
    }

    IEnumerator SpeakOne(string line)
    {
        yield return TypewriterLine(line);
        yield return WaitForPlayerClick();
    }

    static string OneOf(string[] pool) =>
        (pool == null || pool.Length == 0) ? "..." : pool[Random.Range(0, pool.Length)];

    IEnumerator TypewriterLine(string line)
    {
        if (dialogueText == null) yield break;
        _isTyping = true;
        _skipTyping = false;

        if (typewriterLoopClip != null && _typewriterSource != null)
        {
            _typewriterSource.clip = typewriterLoopClip;
            _typewriterSource.volume = typewriterVolume;
            _typewriterSource.Play();
        }

        yield return DialogueTextStyling.RevealCharsTMP(dialogueText, line, charDelay, () => _skipTyping);

        if (_typewriterSource != null && _typewriterSource.isPlaying) _typewriterSource.Stop();
        _isTyping = false;
        _skipTyping = false;
    }

    IEnumerator WaitForPlayerClick()
    {
        _waitingForClick = true;
        yield return new WaitUntil(() => !_waitingForClick || !_playerInRange);
    }
}
