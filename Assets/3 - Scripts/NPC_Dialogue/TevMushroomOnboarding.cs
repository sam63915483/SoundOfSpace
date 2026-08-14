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
    [Tooltip("Metres the player must be within to get the talk prompt. Set explicitly rather than derived from the trigger — Tev is the first NPC in the game and his talk range should not be an accident of how his collider was scaled. 0 falls back to deriving it from the SphereCollider.")]
    public float talkRadius = 8f;
    [Tooltip("Log one line a second describing every gate between 'player nearby' and 'prompt shown', whenever the player is within debugRadius. OFF now that the onboarding is play-verified — flip it back on if talking to him ever breaks again; it names the failing gate in one line and it is how the last three regressions were found.")]
    public bool debugLogging = false;
    [Tooltip("Metres within which debugLogging reports.")]
    public float debugRadius = 25f;
    [Tooltip("Hard backstop: seconds after this component wakes at which Tev appears regardless of the exit ramp. Covers boots where the arrival sequence never runs (Play straight into the gameplay scene, dev spawns) — without it he'd stay hidden forever there.")]
    public float fallbackSeconds = 180f;

    [Header("Deprecated behaviour")]
    [Tooltip("Disable Tev's wave/idle animation while the onboarding is live. OFF: switching it off turns him into a frozen statue that doesn't even look at you, which reads as broken rather than as 'the old waving beat is deprecated'. The handoff wanted the on-LANDING wave gone, and the 120s hidden window already achieves that — he isn't there to wave.")]
    public bool suppressWave = false;
    [Tooltip("Disable the old Mission 1 TevDialogue while the onboarding is live. The component is only disabled, never removed.")]
    public bool suppressMissionDialogue = true;
    [Tooltip("Re-enable TevDialogue once the onboarding completes. OFF while all mission/story content is on hold — turn it on to hand Tev back to the mission tree.")]
    public bool restoreMissionDialogue = false;

    [Header("Lines — first talk (fronts you three)")]
    [TextArea(2, 5)]
    public string[] firstTalkLines = new[]
    {
        "Most people knock, y'know. You parked a shuttle on my lawn.",
        "Truth be told I've been hoping someone'd land on it. Nothing much happens out here worth being sore about.",
        "Fresh off the pod, then. No money, no plan, and a suit that'll want feeding.",
        "Lucky for you there's exactly one business worth being in around here.",
        "{n} tapes. My stuff, so don't laugh. Find someone who likes it — everyone out here's got different taste, so don't be afraid to shop around.",
        "Whatever you get for 'em, half comes back to me. That's the deal and it's a generous one.",
        "And hey. Don't tape over 'em.",
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
        "Wait. All of them? Gone?",
        "I hand you the easiest money in the system and you come back with lint.",
        "…Lesson one, friend: don't fall in love with your own supply.",
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
        "No. I'm done handing you my back catalogue.",
        "You want tapes, make your own. There's a machine in that shuttle — go press something.",
    };

    [Header("Lines — outcome B: sold some, teaches the loop (completes)")]
    [TextArea(2, 5)]
    public string[] teachLines = new[]
    {
        "Not bad. You've got a buyer and you've got a price. That's a business.",
        "Alright — trade secret. You don't want to be selling my stuff forever.",
        "That shuttle you parked on my lawn has a music machine in it. Most folks never even switch the thing on.",
        "Make something. Press it to tape. Sell it. And you keep all of that, seeing as it's yours.",
        "And when you get sick of the two machines it came with, come see me. I've got all your music needs.",
    };

    [Header("Lines — after the onboarding is done")]
    [TextArea(2, 5)]
    public string[] doneLines = new[]
    {
        "Still at it? Good. Keep an eye on who's paying what.",
        "Make something people haven't heard. That's the whole trick.",
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
    bool _bootedFromLoad;

    bool _playerInRange;
    bool _conversationActive;
    bool _isTyping;
    bool _skipTyping;
    bool _waitingForClick;
    int _choice = -1;
    Coroutine _dialogueCoroutine;

    string _promptCached;
    TutorialGate.InputSource _promptCachedSource = (TutorialGate.InputSource)(-1);

    void Awake()
    {
        // MUST be Awake: PendingLoad consumes + clears Data in the sceneLoaded
        // callback, which fires after Awake but before Start (same reason
        // ShuttleExitDoor checks it there).
        _bootedFromLoad = PendingLoad.Data != null;
    }

    void Start()
    {
        _startedAt = Time.time;
        // Fallback for any load path where Data was already consumed by the time
        // we woke: the loader spawns a [SaveLoadRunner] that lives through the
        // first frames, so its presence means this boot is a load.
        if (!_bootedFromLoad && FindObjectOfType<SaveLoadRunner>() != null) _bootedFromLoad = true;
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
        // Loading a save means the arrival is long over — the only save station
        // is the stasis pod, which is past the ramp. Re-hiding him for two
        // minutes on every load is just a player wondering where Tev went.
        if (_bootedFromLoad) return true;
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

        float radius = EffectiveTalkRadius();

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

        if (debugLogging) TickDebug();

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

    // One line a second while the player is near, naming the state of every gate
    // between "player is nearby" and "Press F is on screen". Cheap, and it turns
    // "I couldn't talk to him" into an exact answer instead of a guess.
    float _nextDebugLog;
    void TickDebug()
    {
        if (Time.time < _nextDebugLog) return;
        if (_playerTf == null) return;
        float dist = Vector3.Distance(_playerTf.position, transform.position);
        if (dist > debugRadius) return;
        _nextDebugLog = Time.time + 1f;

        var ui = InteractPromptUI.Instance;
        string owner = "n/a";
        if (ui != null)
        {
            var f = typeof(InteractPromptUI).GetField("_owner",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var o = f != null ? f.GetValue(ui) as Object : null;
            owner = o != null ? o.name : "null";
        }

        Debug.Log($"[TevOnboarding] dist={dist:F1}/{EffectiveTalkRadius():F1} visible={_visible} inRange={_playerInRange}" +
                  $" gaze={InteractGaze.IsLookingAt(this)} promptVisible={InteractPromptUI.IsPromptVisible} promptOwner={owner}" +
                  $" gateEnabled={TutorialGate.IsGateEnabled} talkUnlocked={TutorialGate.IsUnlocked(TutorialAbility.TalkToNPC)}" +
                  $" inDialogue={PlayerController.isInDialogue} stage={MushroomQuest.CurrentStage}");
    }

    float EffectiveTalkRadius()
    {
        if (talkRadius > 0f) return talkRadius;
        var sc = GetComponent<SphereCollider>();
        var ls = transform.lossyScale;
        return sc != null ? sc.radius * Mathf.Max(ls.x, Mathf.Max(ls.y, ls.z)) : 5f;
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
            // Complete = the free onboarding is over, by EITHER route, and Tev
            // becomes a dealer you can work with. doneLines are vaulted; the
            // fronting loop owns every conversation from here.
            default:                          yield return RunFrontingTalk(); break;
        }
        StopConversation();
    }

    IEnumerator RunFirstTalk()
    {
        // Lead-in, up to and including the "you parked on my lawn" line.
        yield return SpeakLinesRange(firstTalkLines, 0, rentAfterLineIndex);
        if (!_playerInRange) yield break;

        // The shakedown. Skipped once settled, so the pack-full replay path
        // below doesn't let him charge rent twice.
        if (!MushroomQuest.RentSettled)
        {
            yield return RunLawnHaggle();
            if (!_playerInRange) yield break;
        }

        // The rest of the lead-in, ending on the offer of three shrooms.
        yield return SpeakFirstTalkRange(rentAfterLineIndex, int.MaxValue);
        if (!_playerInRange) yield break;

        int given = TevDemoTapes.Grant(MushroomQuest.LawnTapesOwed);
        if (given <= 0)
        {
            // Pack full: say so and leave the stage at NotMet so the whole beat
            // re-offers next talk. The haggle is skipped on the replay, so he
            // cannot re-negotiate the number upward.
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
        // His tapes, across all three demos — hotbar only, never the locker,
        // which is what preserves the deliberate stash-and-claim-you-lost-them
        // exploit the mushroom version had.
        int held = TevDemoTapes.HeldCount();
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

        // Refills stay at three regardless of the number haggled — the ladder
        // set the DEBT, not the batch size.
        int given = TevDemoTapes.Grant(MushroomQuest.BatchSize);
        if (given <= 0)
        {
            yield return SpeakLines(packFullLines);
            yield break;
        }
        MushroomQuest.Refronts++;
        yield return SpeakLines(refrontLines);
    }

    // ── The fronting loop (handoff Parts 4–5) ─────────────────────────────
    //
    // Everything Tev is from here on is PER PLAYER — own bond, own front, own
    // debt — so it all reads and writes TevFronting.Local rather than
    // StoryDirector, which is world state shared by both players.
    //
    // Three states, and which one you get is decided entirely by the data:
    //   • no front, never pitched → the pitch
    //   • no front, already pitched → the short "ready for more" greeting
    //   • debt open → he wants paying, and nothing else is on offer
    IEnumerator RunFrontingTalk()
    {
        var s = TevFronting.Local;

        // Debt first: while you owe him, there is no other conversation.
        if (TevFronting.HasDebt(s))
        {
            yield return SpeakOne(Fill(frontDebtGreetingLine, s));
            if (!_playerInRange) yield break;

            yield return AskChoice(
                new PostGreetingChoicePanel.Row("Yeah, I've got it.", true),
                new PostGreetingChoicePanel.Row("Not yet.", true));
            if (!_playerInRange) yield break;

            if (_choice == 0)
            {
                // Eating the product doesn't clear the debt — wild shrooms are
                // always harvestable, so it's a grind, never a softlock. He just
                // gets to enjoy it.
                if (PlayerWallet.Instance == null || PlayerWallet.Instance.Money <= 0)
                {
                    yield return SpeakLines(frontBrokeLines);
                    yield break;
                }
                yield return OpenPaymentPanel(s);
                yield break;
            }

            yield return SpeakOne(frontRefusedLine);
            yield break;
        }

        // No debt. Pitch once, then use the short greeting forever after.
        if (!s.pitched)
        {
            yield return SpeakLines(frontPitchLines);
            if (!_playerInRange) yield break;
            s.pitched = true;

            yield return AskChoice(
                new PostGreetingChoicePanel.Row("I'm ready, give me what you got.", true),
                new PostGreetingChoicePanel.Row("Sounds good, I'll be back soon.", true));
        }
        else
        {
            yield return SpeakOne(frontIdleGreetingLine);
            if (!_playerInRange) yield break;
            yield return AskChoice(
                new PostGreetingChoicePanel.Row("Go on then.", true),
                new PostGreetingChoicePanel.Row("Not right now.", true));
        }
        if (!_playerInRange) yield break;

        if (_choice != 0)
        {
            yield return SpeakOne(s.frontsCompleted > 0 ? frontDeclineRepeatLine : frontDeclineFirstLine);
            yield break;
        }

        // HOST ONLY rolls the front — house rules, and the strain/quantity are
        // dice. A guest asking gets the host to roll and hand the result back.
        if (!TevFronting.IssueFront(s, out string strain, out int qty, out int owed))
        {
            yield return SpeakLines(packFullLines);
            yield break;
        }

        int perCap = MushroomRegistry.BaseValue(strain);
        string line = frontIssueLine
            .Replace("{qty}", qty.ToString())
            .Replace("{strain}", MushroomRegistry.DisplayName(strain))
            .Replace("{price}", perCap.ToString())
            .Replace("{owed}", owed.ToString());
        yield return SpeakOne(line);
    }

    /// Hand off to the payment panel and wait for it to close. The panel owns
    /// the money movement; this just reports what he says about the result.
    IEnumerator OpenPaymentPanel(TevFronting.PlayerState s)
    {
        if (TevPaymentUI.Instance == null)
        {
            // No panel in the scene — pay the whole debt outright rather than
            // stranding the player with a debt they can't clear.
            int owed = s.owed;
            if (PlayerWallet.Instance != null && PlayerWallet.Instance.SpendMoney(owed))
                TevFronting.Pay(s, owed);
            yield return SpeakOne(Fill(frontPaidExactLine, s));
            yield break;
        }

        int before = s.owed;
        bool done = false;
        int paid = 0;
        TevPaymentUI.Instance.Open(s, p => { paid = p; done = true; });
        yield return new WaitUntil(() => done);
        if (!_playerInRange) yield break;

        if (paid <= 0) yield break;                       // cancelled — nothing said

        if (s.owed > 0)  yield return SpeakOne(Fill(frontPaidShortLine, s));
        else if (paid > before) yield return SpeakOne(frontPaidOverLine);
        else yield return SpeakOne(frontPaidExactLine);
    }

    /// Token swap shared by the fronting lines. {owed} is the live remaining
    /// balance, so "you're still ${owed} short" can never quote a stale number.
    string Fill(string line, TevFronting.PlayerState s) =>
        string.IsNullOrEmpty(line) ? "" : line.Replace("{owed}", (s != null ? s.owed : 0).ToString());

    /// The lawn-rent haggle. Tev opens high, folds once, then folds completely —
    /// the joke is that every "no" costs him money and he never actually minds.
    ///
    /// Branching is on `_choice == 0` (accept) rather than `!= 1`, deliberately:
    /// AskChoice leaves _choice at -1 if PostGreetingChoicePanel is missing, and
    /// -1 must fall through to the FREE outcome rather than silently billing a
    /// player who was never shown a prompt.
    /// <summary>
    /// The work-off haggle. He knows the player is broke, so the lawn is paid
    /// in labour: sell N of HIS tapes and it is settled, once.
    ///
    /// The ladder is 10 → 8 → 5 → 3 and IT NEVER REACHES FREE — the last rung
    /// has no refusal row. That inverts the old money joke: the stubborn
    /// haggler now walks away with the LIGHTEST load rather than with nothing
    /// to pay, so pushing back is rewarded without being free.
    /// </summary>
    IEnumerator RunLawnHaggle()
    {
        int[] rungs = MushroomQuest.LawnTapeRungs;
        for (int i = 0; i < rungs.Length; i++)
        {
            int n = rungs[i];
            bool last = i == rungs.Length - 1;

            yield return SpeakTapeLines(LawnDemandLines(i), n);
            if (!_playerInRange) yield break;

            if (last)
            {
                // One row. There is no way out of the last rung, and showing a
                // dead refusal row would only advertise a door that isn't there.
                yield return AskChoice(
                    new PostGreetingChoicePanel.Row($"Fine. {n} tapes.", true));
            }
            else
            {
                yield return AskChoice(
                    new PostGreetingChoicePanel.Row($"Deal. {n} tapes.", true),
                    new PostGreetingChoicePanel.Row(i == 0 ? $"{n}? Not a chance." : "Still no.", true));
            }
            if (!_playerInRange) yield break;

            if (last || _choice == 0)
            {
                // Settles the MONEY rent at zero as well, which is what keeps
                // the weekly collector permanently quiet — see MushroomQuest.
                MushroomQuest.SettleLawn(n);
                yield return SpeakTapeLines(LawnAcceptLines(i), n);
                yield break;
            }
        }
    }

    string[] LawnDemandLines(int rung)
    {
        switch (rung)
        {
            case 0:  return lawnDemandLines;
            case 1:  return lawnClimbdownLines;
            case 2:  return lawnThirdOfferLines;
            default: return lawnFinalOfferLines;
        }
    }

    string[] LawnAcceptLines(int rung)
    {
        switch (rung)
        {
            case 0:  return lawnAccept10Lines;
            case 1:  return lawnAccept8Lines;
            case 2:  return lawnAccept5Lines;
            default: return lawnAccept3Lines;
        }
    }

    /// SpeakLines with "{n}" swapped for the actual tape count, so the number
    /// he says can never drift from the number he actually books.
    IEnumerator SpeakTapeLines(string[] lines, int amount)
    {
        if (lines == null) yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!_playerInRange) yield break;
            string line = lines[i] != null ? lines[i].Replace("{n}", amount.ToString()) : string.Empty;
            yield return SpeakOne(line);
        }
    }

    /// The first-talk slice, with ONE line swapped when the player agreed to pay
    /// rent: the free-loader gets "Three shrooms, on the house", and anyone who
    /// just signed up for a weekly bill gets the same offer framed as a way to
    /// cover it. Tev has just taken money off them — handing over the shrooms
    /// without acknowledging that reads like he forgot.
    ///
    /// Done as a swap rather than a second full array so there is exactly one
    /// place to edit the other five lines.
    /// <summary>
    /// The first-talk slice after the haggle. "{n}" is swapped for the number
    /// of tapes he just booked, so the offer line always matches what actually
    /// lands in the player's hands.
    ///
    /// The old rent-conditional swap is gone: there is no "paying rent" state
    /// any more, because the lawn is worked off in tape sales rather than
    /// charged weekly. One line, one version.
    /// </summary>
    IEnumerator SpeakFirstTalkRange(int from, int to)
    {
        if (firstTalkLines == null) yield break;
        int start = Mathf.Clamp(from, 0, firstTalkLines.Length);
        int end = Mathf.Clamp(to, start, firstTalkLines.Length);
        string n = MushroomQuest.LawnTapesOwed.ToString();

        for (int i = start; i < end; i++)
        {
            if (!_playerInRange) yield break;
            string line = firstTalkLines[i] != null
                ? firstTalkLines[i].Replace("{n}", n) : string.Empty;
            yield return SpeakOne(line);
        }
    }

    /// SpeakLines over a half-open slice [from, to). Clamped, so passing
    /// int.MaxValue for `to` safely means "to the end".
    IEnumerator SpeakLinesRange(string[] lines, int from, int to)
    {
        if (lines == null) yield break;
        int start = Mathf.Clamp(from, 0, lines.Length);
        int end = Mathf.Clamp(to, start, lines.Length);
        for (int i = start; i < end; i++)
        {
            if (!_playerInRange) yield break;
            yield return SpeakOne(lines[i]);
        }
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

    // -- appended; keep new fields at the END (serialization) --
    //
    // NOTE: these are NEW keys, so they take the C# values below. The older
    // arrays above (firstTalkLines etc.) are already serialized on the scene's
    // TEV object, so editing THEIR initializers here changes nothing that ships
    // — edit those in the Inspector instead.

    [Header("Lawn rent — the haggle before he fronts you")]
    [Tooltip("Index into firstTalkLines where the rent shakedown is inserted. 1 = straight after the 'you parked a shuttle on my lawn' line. Everything from this index on is spoken AFTER the haggle resolves.")]
    [SerializeField] int rentAfterLineIndex = 1;

    // The four rungs. "{n}" is swapped for the tape count so the number he
    // says can never drift from the number he books. The counts themselves
    // live in MushroomQuest.LawnTapeRungs.

    [TextArea(2, 5)]
    public string[] lawnDemandLines = new[]
    {
        "Course, a berth like that isn't free. Prime lawn, southern exposure.",
        "And we both know you can't pay. So you'll work it off. {n} of my tapes, sold — and we're square.",
    };

    [TextArea(2, 5)]
    public string[] lawnAccept10Lines = new[]
    {
        "A man who takes the first offer. This is going to be a beautiful arrangement.",
    };

    [TextArea(2, 5)]
    public string[] lawnClimbdownLines = new[]
    {
        "Ha! Alright, alright — I was messing with ya.",
        "{n}. And I'm already regretting it.",
    };

    [TextArea(2, 5)]
    public string[] lawnAccept8Lines = new[]
    {
        "There we go. Neighbourly.",
    };

    [TextArea(2, 5)]
    public string[] lawnThirdOfferLines = new[]
    {
        "{n}, then. You're haggling a man on his own lawn, you know that?",
    };

    [TextArea(2, 5)]
    public string[] lawnAccept5Lines = new[]
    {
        "{n} it is. You'd have made a decent salesman already.",
    };

    [TextArea(2, 5)]
    public string[] lawnFinalOfferLines = new[]
    {
        "{n}. Final offer, and I'm robbing myself.",
    };

    [TextArea(2, 5)]
    public string[] lawnAccept3Lines = new[]
    {
        "Good. See? Painless.",
    };

    [Header("Rent-conditional offer line (2026-08-10)")]
    [Tooltip("Index into firstTalkLines of the 'three shrooms, on the house' offer. When the player agreed to ANY rent, firstTalkOfferRentPaid is spoken instead of this line.")]
    [SerializeField] int rentPaidOfferLineIndex = 4;

    [Tooltip("Replaces firstTalkLines[rentPaidOfferLineIndex] when the player agreed to pay rent. Leave empty to always use the normal line.")]
    [TextArea(2, 5)]
    public string firstTalkOfferRentPaid =
        "To help make money for rent, take these three shrooms, on the house. Find a buyer — everyone's got different prices and preferences, so don't be afraid to shop around.";

    // ── Fronting loop copy (2026-08-10) — ALL DRAFT, for Sam to rewrite ───
    // These are new serialized fields, so they take the values below until they
    // are edited in the Inspector. After that the Inspector wins and editing
    // this file changes nothing that ships.

    [Header("Fronting — the pitch (first time only)")]
    [TextArea(2, 5)]
    public string[] frontPitchLines = new[]
    {
        "Now. You've seen how it works — you found a buyer, you got a price.",
        "Anytime you're after a bit of cash, come see me. I'll front you the shrooms and we split it fifty-fifty.",
        "One rule: my half comes home before you get another front.",
    };

    [Tooltip("Short greeting once he's pitched and you owe nothing. The full pitch never repeats.")]
    public string frontIdleGreetingLine = "ready for more big boy?";

    [Tooltip("Turning down the FIRST pitch. Verbatim, Sam's line.")]
    public string frontDeclineFirstLine = "alright pussy";

    [Tooltip("Turning down a later offer. Verbatim, Sam's line.")]
    public string frontDeclineRepeatLine = "good things come to those who grind";

    [Header("Fronting — handing product over")]
    [Tooltip("Tokens: {qty} {strain} {price} {owed}. Saying the market price out loud is DELIBERATE — it teaches the word 'market', which is what lets the player later work out they can sell above it.")]
    [TextArea(2, 5)]
    public string frontIssueLine =
        "Splendid. {qty} {strain}, then. They go for ${price} a cap at market, so bring me back ${owed} and we're square.";

    [Header("Fronting — the debt")]
    [Tooltip("Token: {owed}.")]
    public string frontDebtGreetingLine = "Wonderful to see you. Do you have my ${owed}?";

    [Tooltip("Said when the player answers No.")]
    public string frontRefusedLine = "then get back out there and get it!";

    [Tooltip("Ridicule for turning up with a debt and nothing in your pockets — consistent with the ate-all-three joke.")]
    [TextArea(2, 5)]
    public string[] frontBrokeLines = new[]
    {
        "You said you had it. You've got lint and a nice smile.",
        "Ate them, didn't you. I can tell. You've got the look.",
        "They grow WILD out there. Go and pick some. I'll wait — I'm very good at waiting.",
    };

    [Header("Fronting — payment outcomes")]
    [Tooltip("Token: {owed} — the remaining balance AFTER the underpayment.")]
    public string frontPaidShortLine =
        "I'll take it. But you're still ${owed} short, and there's no more fronts till I'm square.";

    public string frontPaidExactLine =
        "Pleasure doing business. Come back whenever — there's plenty more where that came from.";

    public string frontPaidOverLine =
        "Well now. Over the odds, and you didn't have to. I'll remember that, friend.";
}
