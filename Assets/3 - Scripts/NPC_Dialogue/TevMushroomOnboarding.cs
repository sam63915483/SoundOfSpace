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
    [Tooltip("Seconds after the shuttle's exit ramp deploys before Tev appears outside his cabin. 0 since 2026-08-30 (Sam: no delay) — the old 120 s 'quiet looting window' was a rent-era beat. Scene-serialized: the Inspector value wins over this default.")]
    public float hiddenSeconds = 0f;
    [Tooltip("Metres the player must be within to get the talk prompt. Set explicitly rather than derived from the trigger — Tev is the first NPC in the game and his talk range should not be an accident of how his collider was scaled. 0 falls back to deriving it from the SphereCollider.")]
    public float talkRadius = 8f;
    [Tooltip("Log one line a second describing every gate between 'player nearby' and 'prompt shown', whenever the player is within debugRadius. OFF now that the onboarding is play-verified — flip it back on if talking to him ever breaks again; it names the failing gate in one line and it is how the last three regressions were found.")]
    public bool debugLogging = false;
    [Tooltip("Metres within which debugLogging reports.")]
    public float debugRadius = 25f;
    [Tooltip("Hard backstop: seconds after this component wakes at which Tev appears regardless of the exit ramp. 0 = immediately, which with hiddenSeconds 0 means he is simply always present.")]
    public float fallbackSeconds = 0f;

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
    bool _shopIntroduced;
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
        // New-flow equivalent of the stage check above: the revamped tree never
        // advances MushroomQuest.Stage, so without this a mid-game scene reload
        // (backrooms trip, warm reload) would hide a Tev the player has already
        // met for another fallbackSeconds.
        if (!FeatureVault.TevRent && StoryDirector.Instance != null
            && StoryDirector.Instance.GetFlag("tevMet")) return true;
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

    /// <summary>
    /// Who Tev is on this talk.
    ///
    /// Since the rent revamp there are only two answers: the FIRST talk (lawn →
    /// rent haggle → three free blanks) and every talk after it (nag if you owe,
    /// then the shop). The Given stage — "did you sell any of my tapes?" — went
    /// with the fronting economy it was asking about, and the new first talk
    /// jumps straight to Complete, so Given is only ever reached by an old save.
    /// It falls through to the same place, which is the right landing for it.
    /// </summary>
    IEnumerator PlaySequence()
    {
        // First-meeting revamp (2026-08-30, FeatureVault.TevRent vaulted): Tev
        // is a music-store owner, not a landlord. Two states only — the
        // first-meeting tree, then the hub. The whole rent-era flow below is
        // kept intact behind the flag, same as the fronting vault before it.
        if (!FeatureVault.TevRent)
        {
            if (!TevMet) yield return RunFirstMeeting();
            else         yield return RunMeetingHub();
            StopConversation();
            yield break;
        }

        if (MushroomQuest.CurrentStage == MushroomQuest.Stage.NotMet)
        {
            if (FeatureVault.TevLawnWorkOff) yield return RunFirstTalkWorkOff();
            else                             yield return RunFirstTalk();
        }
        else if (FeatureVault.TevFrontingEconomy)
        {
            if (MushroomQuest.CurrentStage == MushroomQuest.Stage.Given)
                yield return RunReturnTalk();
            else
                yield return RunFrontingTalk();
        }
        else
        {
            yield return RunLandlordTalk();
        }
        StopConversation();
    }

    // ── The first-meeting tree (2026-08-30 revamp) ────────────────────────
    //
    // Tev the music-store owner. Two states only: TevMet=false → the tree
    // (identical on any day), TevMet=true → the hub. Every branch converges on
    // THE PITCH. Lines live in the meet* fields at the END of this class —
    // they are NEW serialized keys, so the handoff's verbatim copy actually
    // ships (the older arrays are scene-serialized and the Inspector wins).

    /// <summary>Met flag, world-scoped via StoryDirector so it saves and syncs
    /// like every other story bit. Legacy saves — anyone who already met the
    /// rent-era Tev — read as met, so they land in the hub rather than
    /// replaying a first meeting that contradicts what their Tev already said.
    /// (Under the new flow MushroomQuest.Stage is never advanced, so the
    /// legacy clause can never trip on a fresh save.)</summary>
    static bool TevMet
    {
        get => (StoryDirector.Instance != null && StoryDirector.Instance.GetFlag("tevMet"))
               || MushroomQuest.CurrentStage != MushroomQuest.Stage.NotMet;
        set { StoryDirector.Instance?.SetFlag("tevMet", value); }
    }

    /// "TRAX owned" for the hub guard (handoff §5/§6): installed on the shared
    /// computer — world state, so a co-op partner's install counts — or this
    /// player is already carrying an unspent stick. Guards the re-pitch so Tev
    /// never sells a second stick that could do nothing.
    static bool TraxOwned =>
        TraxLibrary.IsAppInstalled
        || (Hotbar.Instance != null && Hotbar.Instance.GetResourceTotal(Hotbar.ItemId.TraxUsbStick) > 0);

    /// §7 stub: one optional prefix line keyed on the (never-set) radio
    /// impression. None = silence, which is every player today.
    IEnumerator SpeakRadioPrefix()
    {
        string line;
        switch (RadioImpression.Current)
        {
            case RadioImpression.Kind.Star:    line = meetPrefixStar; break;
            case RadioImpression.Kind.Fool:    line = meetPrefixFool; break;
            case RadioImpression.Kind.Mystery: line = meetPrefixMystery; break;
            default: line = null; break;
        }
        if (!string.IsNullOrEmpty(line)) yield return SpeakOne(line);
    }

    IEnumerator RunFirstMeeting()
    {
        yield return SpeakRadioPrefix();
        if (!_playerInRange) yield break;

        yield return SpeakOne(meetGreetingLine);
        if (!_playerInRange) yield break;

        yield return AskChoice(
            new PostGreetingChoicePanel.Row(meetOptionA, true),
            new PostGreetingChoicePanel.Row(meetOptionB, true),
            new PostGreetingChoicePanel.Row(meetOptionC, true));
        if (!_playerInRange) yield break;

        switch (_choice)
        {
            case 0:   // "I'm not lost..."
                yield return SpeakOne(meetDeepReply);
                if (!_playerInRange) yield break;
                yield return AskChoice(
                    new PostGreetingChoicePanel.Row(meetOptionA1, true),
                    new PostGreetingChoicePanel.Row(meetOptionA2, true),
                    new PostGreetingChoicePanel.Row(meetOptionA3, true));
                if (!_playerInRange) yield break;

                if (_choice == 0)
                {
                    yield return SpeakOne(meetHomeAskLine);
                    if (!_playerInRange) yield break;
                    // §4 A1: "..." is deliberately the ONLY reply on offer.
                    yield return AskChoice(new PostGreetingChoicePanel.Row(meetHomeForcedReply, true));
                    if (!_playerInRange) yield break;
                    yield return SpeakOne(meetHomeLostLine);
                }
                else if (_choice == 1)
                {
                    yield return SpeakOne(meetMusicReplyLine);
                }
                else
                {
                    yield return SpeakLines(meetDontKnowLines);
                }
                break;

            case 1:   // "So what do you sell?"
                yield return SpeakOne(meetSellReplyLine);
                break;

            default:  // "Where am I?" — also the -1 fallback if the panel is missing.
                yield return SpeakOne(meetWhereReplyLine);
                break;
        }
        if (!_playerInRange) yield break;

        yield return RunPitchChoice();
    }

    /// <summary>
    /// THE PITCH. Every branch line already ends on a version of "interested?",
    /// so this is just the YES/NO and its outcomes. TevMet is set the moment an
    /// answer lands — §4 says any exit counts, and walking off mid-tree before
    /// answering replays the tree, which is the kinder reading.
    /// </summary>
    IEnumerator RunPitchChoice()
    {
        yield return AskChoice(
            new PostGreetingChoicePanel.Row("Yes", true),
            new PostGreetingChoicePanel.Row("No", true));
        if (!_playerInRange || _choice < 0) yield break;

        TevMet = true;

        if (_choice != 0)
        {
            yield return SpeakOne(meetPitchNoLine);
            yield break;
        }

        int money = PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0;
        if (money < TraxPrice || PlayerWallet.Instance == null
            || !PlayerWallet.Instance.SpendMoney(TraxPrice))
        {
            yield return SpeakOne(meetPitchBrokeLine);
            yield break;
        }

        // Paid. The stick must actually land in the pack — a stick he pocketed
        // $20 for and couldn't hand over would be theft, so a full pack refunds.
        int leftover = Hotbar.Instance != null
            ? Hotbar.Instance.AddResource(Hotbar.ItemId.TraxUsbStick, 1) : 1;
        if (leftover > 0)
        {
            PlayerWallet.Instance.AddMoney(TraxPrice);
            yield return SpeakLines(packFullLines);
            yield break;
        }

        // The three gift blanks reuse the rent-era grant (handoff §3). Best
        // effort: the stick is the purchase, the blanks are a gift, and a gift
        // that doesn't fit is not worth failing the sale over.
        GrantStarterBlanks();
        yield return SpeakOne(meetPitchYesLine);
    }

    /// <summary>
    /// §5, minimal v1. TRAX not owned → re-pitch into the same outcomes;
    /// owned → shop row / leave row, nothing else. The festival thread hangs
    /// off this later.
    /// </summary>
    IEnumerator RunMeetingHub()
    {
        yield return SpeakRadioPrefix();
        if (!_playerInRange) yield break;

        if (!TraxOwned)
        {
            yield return SpeakOne(hubRePitchLine);
            if (!_playerInRange) yield break;
            yield return RunPitchChoice();
            yield break;
        }

        yield return AskChoice(
            new PostGreetingChoicePanel.Row("Let me see the shop", true),
            new PostGreetingChoicePanel.Row("Later, Tev", true));
        if (!_playerInRange) yield break;

        if (_choice == 0) yield return RunShop();
    }

    /// <summary>
    /// The first talk, as the rent revamp specifies it: you're parked on his
    /// lawn, he wants rent for it, you haggle him down a ladder that never
    /// reaches free, he clocks that you're broke, and he gives you three blanks
    /// to get started. From here the loop is entirely yours — buy tapes, record,
    /// sell, pay him.
    ///
    /// The blanks are GENUINELY free. The debt isn't them, it's the rent, and
    /// the rent starts ticking from this conversation.
    ///
    /// Stage goes straight to Complete: there is no middle stage any more,
    /// because there is nothing of his to come back and report on.
    /// </summary>
    IEnumerator RunFirstTalk()
    {
        // "You parked a shuttle on my lawn." Still the right opener, and still
        // the scene's copy — Sam edited it in the Inspector and that wins.
        yield return SpeakLinesRange(firstTalkLines, 0, rentAfterLineIndex);
        if (!_playerInRange) yield break;

        // Skipped once settled, so the pack-full replay below can't re-open a
        // negotiation he already won.
        if (!MushroomQuest.RentSettled)
        {
            yield return RunRentHaggle();
            if (!_playerInRange) yield break;
        }

        // He looks at you, works out you have nothing, and hands over the
        // starter blanks. The last of these lines is locked verbatim.
        yield return SpeakLines(brokeGiftLines);
        if (!_playerInRange) yield break;

        int given = GrantStarterBlanks();
        if (given <= 0)
        {
            // Pack full: say so and leave the stage at NotMet so the beat
            // replays. The haggle is skipped on the replay (RentSettled), so he
            // cannot re-negotiate the rate upward.
            yield return SpeakLines(packFullLines);
            yield break;
        }

        MushroomQuest.CurrentStage = MushroomQuest.Stage.Complete;
    }

    /// <summary>
    /// Three Blank Tape I into the hotbar. Returns how many actually fit — 0
    /// means the caller should say so rather than silently eat them.
    /// </summary>
    int GrantStarterBlanks()
    {
        if (Hotbar.Instance == null) return 0;
        int leftover = Hotbar.Instance.AddResource(Hotbar.ItemId.BlankTapeT1, StarterBlanks);
        return StarterBlanks - leftover;
    }

    /// <summary>
    /// VAULTED (FeatureVault.TevLawnWorkOff) — the first talk as it stood when
    /// the lawn was paid off in sales of HIS tapes.
    /// </summary>
    IEnumerator RunFirstTalkWorkOff()
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

        // The rest of the lead-in, ending on the offer of his tapes.
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

    // ── The landlord loop (rent revamp, 2026-08-14) ───────────────────────
    //
    // Every talk after the first. Two beats, in this order and no other:
    //
    //   1. THE DEBT, if there is one. He leads with it, escalating in tone with
    //      the size of it, and offers the payment panel. Leading with the shop
    //      instead would let a player five days deep in arrears browse a locked
    //      plugin tab with no idea why it's refusing them.
    //   2. THE SHOP. Always — including while locked out, because the BLANKS
    //      tab is never locked and that is the loop's escape hatch.

    IEnumerator RunLandlordTalk()
    {
        if (MushroomQuest.RentBalance > 0)
        {
            yield return RunRentNag();
            if (!_playerInRange) yield break;
        }

        // The full "since you're in the business now" pitch is a one-off; after
        // that he just grunts and opens the shelf.
        if (!ShopPitched)
        {
            yield return SpeakLines(shopOpenLines);
            if (!_playerInRange) yield break;
            ShopPitched = true;
        }
        else
        {
            yield return SpeakOne(OneOf(shopGreetingLines));
            if (!_playerInRange) yield break;
        }

        yield return RunShop();
    }

    /// <summary>
    /// "Where's my money." Tone tracks the number of days behind rather than the
    /// raw balance, so the escalation reads the same whether the player haggled
    /// to $50 or to $10.
    /// </summary>
    IEnumerator RunRentNag()
    {
        yield return SpeakTokens(NagLinesForDebt());
        if (!_playerInRange) yield break;

        int owed = MushroomQuest.RentBalance;
        int money = PlayerWallet.Instance != null ? PlayerWallet.Instance.Money : 0;

        // The pay row is greyed when the player is genuinely skint — the same
        // "he can tell" grammar the old return talk used. Seeing the dead row is
        // how you learn the debt is real and the game knows you can't cover it.
        yield return AskChoice(
            new PostGreetingChoicePanel.Row($"Pay him. (${owed})", money > 0),
            new PostGreetingChoicePanel.Row("Not right now.", true));
        if (!_playerInRange) yield break;

        if (_choice != 0)
        {
            yield return SpeakOne(SwapRentTokens(rentRefusedLine));
            yield break;
        }

        yield return OpenRentPayment();
    }

    /// Hand off to the payment panel and report what he says about the result.
    /// The panel owns the money movement; this only speaks.
    IEnumerator OpenRentPayment()
    {
        int before = MushroomQuest.RentBalance;

        if (TevPaymentUI.Instance == null)
        {
            // No panel — settle what the player can afford outright rather than
            // stranding them with a debt they have no way to clear.
            int paidFallback = MushroomQuest.PayRent(before);
            if (paidFallback > 0)
                yield return SpeakOne(SwapRentTokens(
                    MushroomQuest.RentBalance > 0 ? rentPaidShortLine : rentPaidFullLine));
            yield break;
        }

        bool done = false;
        int paid = 0;
        TevPaymentUI.Instance.OpenForRent(p => { paid = p; done = true; });
        yield return new WaitUntil(() => done);
        if (!_playerInRange) yield break;

        if (paid <= 0) yield break;                       // cancelled — nothing said

        // Clearing the balance lifts the plugin embargo immediately; there is no
        // cooling-off period and he says so.
        bool clearedLockout = before >= MushroomQuest.RentPerDay * MushroomQuest.LockoutDays
                              && MushroomQuest.RentBalance <= 0;

        if (MushroomQuest.RentBalance > 0) yield return SpeakOne(SwapRentTokens(rentPaidShortLine));
        else if (clearedLockout)           yield return SpeakOne(SwapRentTokens(rentLockoutClearedLine));
        else                               yield return SpeakOne(SwapRentTokens(rentPaidFullLine));
    }

    string[] NagLinesForDebt()
    {
        int days = MushroomQuest.UnpaidDays;
        if (days >= MushroomQuest.LockoutDays) return rentNagLockedLines;
        if (days >= 3) return rentNagSternLines;
        return rentNagLightLines;
    }

    /// SpeakLines with the rent tokens swapped, so no line can ever quote a
    /// stale figure.
    IEnumerator SpeakTokens(string[] lines)
    {
        if (lines == null) yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!_playerInRange) yield break;
            yield return SpeakOne(SwapRentTokens(lines[i]));
        }
    }

    /// {owed} = the live balance, {days} = days behind, {rate} = the haggled
    /// daily rate.
    string SwapRentTokens(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        return line
            .Replace("{owed}", MushroomQuest.RentBalance.ToString())
            .Replace("{days}", MushroomQuest.UnpaidDays.ToString())
            .Replace("{rate}", MushroomQuest.RentPerDay.ToString());
    }

    /// Has he given the "since you're in the business now" shop pitch? A
    /// StoryDirector flag rather than a field, so it survives a reload — hearing
    /// the full pitch again every time you load would be maddening.
    static bool ShopPitched
    {
        get => StoryDirector.Instance != null && StoryDirector.Instance.GetFlag("tevShopPitched");
        set { StoryDirector.Instance?.SetFlag("tevShopPitched", value); }
    }

    /// <summary>
    /// The rent haggle. Four rungs — $50 → $30 → $20 → $10 per day — each a real
    /// refusal with a real counter, and THE LAST ONE HAS NO WAY OUT.
    ///
    /// This is deliberately the player's first negotiation in the whole game. It
    /// tutorialises the push-your-luck instinct every alien sale later depends
    /// on: refusing is rewarded, four times, and then it isn't.
    ///
    /// Branching is on `_choice == 0` (accept) rather than `!= 1` because
    /// AskChoice leaves _choice at -1 if PostGreetingChoicePanel is missing, and
    /// -1 must fall through to the NEXT RUNG rather than silently booking a rate
    /// the player was never shown.
    /// </summary>
    IEnumerator RunRentHaggle()
    {
        int[] rungs = MushroomQuest.RentRungs;
        for (int i = 0; i < rungs.Length; i++)
        {
            int n = rungs[i];
            bool last = i == rungs.Length - 1;

            yield return SpeakRentLines(RentDemandLines(i), n);
            if (!_playerInRange) yield break;

            if (last)
            {
                // One row. There is no way off the last rung, and showing a dead
                // refusal row would only advertise a door that isn't there.
                yield return AskChoice(
                    new PostGreetingChoicePanel.Row($"Fine. ${n} a day.", true));
            }
            else
            {
                yield return AskChoice(
                    new PostGreetingChoicePanel.Row($"Deal. ${n} a day.", true),
                    new PostGreetingChoicePanel.Row(i == 0 ? $"${n}? Not a chance." : "Still no.", true));
            }
            if (!_playerInRange) yield break;

            if (last || _choice == 0)
            {
                // Starts the clock: today is marked billed, so the first charge
                // lands on the next day roll.
                MushroomQuest.SettleRent(n);
                yield return SpeakRentLines(RentAcceptLines(i), n);
                yield break;
            }
        }
    }

    string[] RentDemandLines(int rung)
    {
        switch (rung)
        {
            case 0:  return rentDemandLines;
            case 1:  return rentClimbdownLines;
            case 2:  return rentThirdOfferLines;
            default: return rentFinalOfferLines;
        }
    }

    string[] RentAcceptLines(int rung)
    {
        switch (rung)
        {
            case 0:  return rentAccept50Lines;
            case 1:  return rentAccept30Lines;
            case 2:  return rentAccept20Lines;
            default: return rentAccept10Lines;
        }
    }

    /// SpeakLines with "{n}" swapped for the daily rate on offer, so the number
    /// he says can never drift from the number he books.
    IEnumerator SpeakRentLines(string[] lines, int rate)
    {
        if (lines == null) yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!_playerInRange) yield break;
            string line = lines[i] != null ? lines[i].Replace("{n}", rate.ToString()) : string.Empty;
            yield return SpeakOne(line);
        }
    }

    /// <summary>
    /// VAULTED (FeatureVault.TevFrontingEconomy) — "did you sell any of my
    /// tapes?". Unreachable while the flag is false.
    /// </summary>
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
        // The shop opens on the FIRST visit after the onboarding, then lives on
        // a row.
        //
        // It USED to argue that a bloke on a lawn is not a storefront, and so
        // stayed inside the conversation as a list of choice rows. That was a
        // fair call when he sold two things and stopped being one at six: every
        // purchase cost a click and a spoken line, so ten blanks meant ten of
        // each, and a $180 permanent unlock got the same row as a $5 blank.
        // TevShopUI is that list as a panel; he still speaks, on the way in and
        // on the way out.
        if (!_shopIntroduced)
        {
            _shopIntroduced = true;
            yield return SpeakLines(shopOpenLines);
            if (!_playerInRange) yield break;
            yield return RunShop();
            if (!_playerInRange) yield break;
        }

        if (!s.pitched)
        {
            yield return SpeakLines(frontPitchLines);
            if (!_playerInRange) yield break;
            s.pitched = true;

            yield return AskChoice(
                new PostGreetingChoicePanel.Row("I'm ready, give me what you got.", true),
                new PostGreetingChoicePanel.Row("What are you selling?", true),
                new PostGreetingChoicePanel.Row("Sounds good, I'll be back soon.", true));
        }
        else
        {
            yield return SpeakOne(frontIdleGreetingLine);
            if (!_playerInRange) yield break;
            yield return AskChoice(
                new PostGreetingChoicePanel.Row("Go on then.", true),
                new PostGreetingChoicePanel.Row("What are you selling?", true),
                new PostGreetingChoicePanel.Row("Not right now.", true));
        }
        if (!_playerInRange) yield break;

        if (_choice == 1)
        {
            yield return RunShop();
            yield break;
        }

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

        // No strain name: a batch is spread across his catalogue, so there is no
        // single title to quote. He quotes the COUNT and his cut, and nothing
        // else — the number he expects is his market half, and he has no way of
        // knowing what the player actually took. That is the skim.
        string line = frontIssueLine
            .Replace("{qty}", qty.ToString())
            .Replace("{owed}", owed.ToString());
        yield return SpeakOne(line);
    }

    // ── The shop ─────────────────────────────────────────────────────────
    //
    // Deliberately a conversation, not a storefront panel: rows in the same
    // choice UI everything else here uses. It needs no scene wiring, it cannot
    // drift out of sync with a prefab, and it suits a man selling gear off his
    // own lawn.
    //
    // Plugins install to the COMPUTER (TraxLibrary), so in co-op one player
    // buying SIREN unlocks it for both. Blanks go to the buyer's hotbar.


    /// <summary>
    /// Hand off to the shop PANEL and wait for it to close.
    ///
    /// The catalogue moved to <see cref="TevShopUI.Stock"/> — "what does Tev
    /// sell" now has exactly one answer, and it lives with the UI that draws it
    /// rather than in the middle of his conversation script.
    ///
    /// Two of his authored lines travel with him: the plugin-bought line and the
    /// no-room line, both of which are still true of a panel. The blank-bought
    /// line does not, because it reads "One {item}" and a row now buys a stack;
    /// the panel counts for itself.
    /// </summary>
    IEnumerator RunShop()
    {
        if (TevShopUI.Instance == null) yield break;

        bool closed = false;
        TevShopUI.Instance.Open(() => closed = true, shopPluginBoughtLine, shopNoRoomLine);
        while (!closed) yield return null;
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

    [Header("Lines — the shop")]
    [TextArea(2, 5)]
    public string[] shopOpenLines = new[]
    {
        "Right. Since you're in the business now.",
        "Blanks, if you're pressing your own. And gear, if you've got the money — I know a guy who knows a guy.",
    };

    [Tooltip("Spoken after buying a rack module. {item} = the module name.")]
    [TextArea(2, 5)]
    public string shopPluginBoughtLine = "{item}. Wire it in and try not to deafen anyone.";

    // UNUSED SINCE THE SHOP BECAME A PANEL, and kept on purpose. It reads
    // "One {item}", which was true when a purchase was one tape and is wrong now
    // that a row buys a stack - so TevShopUI counts for itself instead of
    // speaking this. The field stays because it is SCENE-SERIALIZED: deleting it
    // throws away whatever Sam actually typed into the inspector, which no C#
    // default can give back. Re-word it in the scene and wire it up if the shop
    // should say something quantity-aware.
    [Tooltip("UNUSED - the shop panel writes its own quantity-aware line.")]
    [TextArea(2, 5)]
    public string shopBlankBoughtLine = "One {item}. Don't waste it.";

    [TextArea(2, 5)]
    public string shopNoRoomLine = "You've nowhere to put it, friend.";

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
        "Got a stack of my old stuff going nowhere. Anytime you're after a bit of cash, come see me and we'll split it fifty-fifty.",
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
        "Splendid. {qty} tapes, then. Half of what they're worth comes back to me, so call it ${owed}.";

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
        "Sat on them, didn't you. I can tell. You've got the look.",
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

    // ── RENT REVAMP COPY (2026-08-14) — DRAFT, for Sam to rewrite ─────────
    //
    // These are NEW serialized keys, so they ship the C# values written here
    // until somebody edits them on the scene's TEV object. After that the
    // Inspector wins and editing this file changes nothing — same trap as
    // firstTalkLines above.
    //
    // Token vocabulary:
    //   {n}     the daily rate being offered on THIS rung (haggle lines only)
    //   {owed}  the live outstanding balance
    //   {days}  days behind
    //   {rate}  the agreed daily rate

    /// Blanks he hands over on the first talk. Not a rung, not a debt — free.
    public const int StarterBlanks = 3;

    [Header("Rent haggle — $50 → $30 → $20 → $10 per day")]
    [TextArea(2, 5)]
    public string[] rentDemandLines = new[]
    {
        "Course, a berth like that isn't free. Prime lawn, southern exposure.",
        "${n} a day and we'll say no more about it.",
    };

    [TextArea(2, 5)]
    public string[] rentAccept50Lines = new[]
    {
        "A man who takes the first offer. This is going to be a beautiful arrangement.",
    };

    [TextArea(2, 5)]
    public string[] rentClimbdownLines = new[]
    {
        "Ha! Alright, alright — I was messing with ya.",
        "${n} a day. That's me being generous, mind you.",
    };

    [TextArea(2, 5)]
    public string[] rentAccept30Lines = new[]
    {
        "There we go. Neighbourly.",
    };

    [TextArea(2, 5)]
    public string[] rentThirdOfferLines = new[]
    {
        "${n}, then. You're haggling a man on his own lawn, you know that?",
    };

    [TextArea(2, 5)]
    public string[] rentAccept20Lines = new[]
    {
        "${n} it is. You'd have made a decent salesman already.",
    };

    [TextArea(2, 5)]
    public string[] rentFinalOfferLines = new[]
    {
        "${n} a day. Final offer, and I'm robbing myself.",
    };

    [TextArea(2, 5)]
    public string[] rentAccept10Lines = new[]
    {
        "Good. See? Painless. Every day, mind — I keep count.",
    };

    [Header("The gift — LAST LINE IS LOCKED VERBATIM")]
    [Tooltip("Spoken right before the three free blanks. The final line is Sam's, word for word — don't paraphrase it.")]
    [TextArea(2, 5)]
    public string[] brokeGiftLines = new[]
    {
        "Now. You've got nothing on you, have you. I can always tell.",
        "Big dreams, empty pockets. Seen it a hundred times. Here's three blanks to get you started — the rest you're buying.",
    };

    [Header("Shop — the return greeting")]
    [Tooltip("One at random once the full shop pitch has been heard. The pitch itself never repeats.")]
    [TextArea(2, 5)]
    public string[] shopGreetingLines = new[]
    {
        "Back again. Go on, have a look.",
        "Shelf's the same as it was. Help yourself.",
        "Quiet day. Quiet year, really. What do you need?",
    };

    [Header("Rent — the nag, escalating with the debt")]
    [Tooltip("1–2 days behind. He's reminding you, not threatening you.")]
    [TextArea(2, 5)]
    public string[] rentNagLightLines = new[]
    {
        "Before you start — ${owed} on the lawn. Whenever you've got it.",
    };

    [Tooltip("3–4 days behind. The joke has gone out of it.")]
    [TextArea(2, 5)]
    public string[] rentNagSternLines = new[]
    {
        "{days} days now. ${owed}.",
        "I'm not a charity, and that shuttle's not getting any smaller.",
    };

    [Tooltip("5+ days behind — the plugin lockout is live. He says so plainly.")]
    [TextArea(2, 5)]
    public string[] rentNagLockedLines = new[]
    {
        "{days} days. ${owed}. No.",
        "Blanks you can have — I'm not going to watch you starve over it. But the gear stays behind the counter till I'm square.",
    };

    [Header("Rent — payment outcomes")]
    [Tooltip("Token: {owed} — the balance REMAINING after a part payment.")]
    [TextArea(2, 5)]
    public string rentPaidShortLine =
        "I'll take it. Still ${owed} on the tab, mind.";

    [TextArea(2, 5)]
    public string rentPaidFullLine =
        "Square. Pleasure doing business with a man who pays his way.";

    [Tooltip("Paying off a debt that had the plugin tab locked. He lifts it on the spot.")]
    [TextArea(2, 5)]
    public string rentLockoutClearedLine =
        "Well now. All of it. Shelf's open again — go and spend it back with me.";

    [Tooltip("Turning him down. He doesn't push; the debt does that for him.")]
    [TextArea(2, 5)]
    public string rentRefusedLine =
        "It keeps counting either way, friend.";

    // ── FIRST-MEETING TREE COPY (2026-08-30) — §4 lines are LOCKED VERBATIM ──
    //
    // NEW serialized keys, so these C# values are what ships until they're
    // edited on the scene's TEV object — after that the Inspector wins, same
    // trap as every batch above. Everything except hubRePitchLine and the
    // three prefix stubs is Sam's copy from the handoff, word for word: do not
    // paraphrase.

    /// TRAX engine price, in credits. §4: twenty bucks.
    public const int TraxPrice = 20;

    [Header("First meeting — greeting + options")]
    [TextArea(2, 5)]
    public string meetGreetingLine = "Salutations, lost traveller!";

    public string meetOptionA = "I'm not lost. I just haven't found what I'm looking for.";
    public string meetOptionB = "So what do you sell?";
    public string meetOptionC = "Where am I?";

    [Header("First meeting — branch A")]
    [TextArea(2, 5)]
    public string meetDeepReply = "Ohhh, *deep*. Okay. So what is it you're looking for?";

    public string meetOptionA1 = "A way back home.";
    public string meetOptionA2 = "I'm interested in making music.";
    public string meetOptionA3 = "I don't know.";

    [TextArea(2, 5)]
    public string meetHomeAskLine = "Sure, easy. Where's home?";

    [Tooltip("§4 A1: the forced reply — deliberately the ONLY option at that node.")]
    public string meetHomeForcedReply = "...";

    [TextArea(2, 5)]
    public string meetHomeLostLine =
        "Ahhh. See, that right there? That's what lost sounds like. Lucky for you — lost folks make the *best* music. Interested?";

    [TextArea(2, 5)]
    public string meetMusicReplyLine =
        "Now we're talkin'! You're standing in the only music shop this side of the event horizon. TRAX engine, blank tapes, plugins when you've earned 'em. Interested in gettin' set up?";

    [Tooltip("A3, split at the handoff's (beat).")]
    [TextArea(2, 5)]
    public string[] meetDontKnowLines = new[]
    {
        "...Yeah. Honestly? Me neither. That's kinda why I'm throwin' the festival — last day, big send-off, everybody dancin' while the sky eats itself. Give folks somethin' good to hold. Between you and me though... I got no clue how it's gonna go.",
        "Anyway! Enough of that. You look like a music-maker to me. Interested?",
    };

    [Header("First meeting — branches B and C")]
    [TextArea(2, 5)]
    public string meetSellReplyLine =
        "Isn't it obvious? Anything music, baby! With that big hungry nothin' loomin' over us, music's the only business still turnin' a profit. ...Like that matters anyways. S'why I'm throwin' the festival. — You interested in gettin' set up?";

    [TextArea(2, 5)]
    public string meetWhereReplyLine =
        "You're on Humble Abode! Third planet from the sun, home to 'the aliens.' Yes, that's really what we call ourselves. No, we're not changin' it. — You interested in making music?";

    [Header("First meeting — the pitch outcomes")]
    [TextArea(2, 5)]
    public string meetPitchYesLine =
        "That's what I like to hear! TRAX music engine, twenty bucks. And 'cause I like your face — three blank demo tapes, on the house. Go make somethin' ugly.";

    [TextArea(2, 5)]
    public string meetPitchBrokeLine =
        "Twenty bucks, traveller. ...You don't *have* twenty bucks. Okay. Planet provides — check your locker, shake some pockets, hell, the fish out here practically pay you. Come back when you're rich.";

    [TextArea(2, 5)]
    public string meetPitchNoLine =
        "Ah. A shame. Well — you know where to find me. Everybody does. It's the only shop with a roof.";

    [Header("Hub — DRAFT, for Sam to rewrite")]
    [Tooltip("Spoken when TevMet but TRAX isn't owned yet, right before the same YES/NO pitch. DRAFT — the handoff specifies only 'short re-pitch'.")]
    [TextArea(2, 5)]
    public string hubRePitchLine =
        "Still here, still sellin'. TRAX engine, twenty bucks, and your career starts today. Interested?";

    [Header("Radio impression prefixes — §7 STUB, leave empty")]
    [Tooltip("Optional prefix line before Tev's greeting per radio impression. Empty = no line. Nothing sets the impression yet.")]
    [TextArea(2, 5)] public string meetPrefixStar = "";
    [TextArea(2, 5)] public string meetPrefixFool = "";
    [TextArea(2, 5)] public string meetPrefixMystery = "";
}
