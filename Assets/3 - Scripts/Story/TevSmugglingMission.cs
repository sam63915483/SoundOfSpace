using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// Mission B-1 "Routine Stop" — barebones spine (see docs/MISSIONS_DESIGN.md §17).
/// Lives on the Tevsship root. Phases:
///   Idle        → player walks into the ship, hits the SCARE trigger → forced 0.5s
///                 spin onto Tev + scream → offer conversation (conv_b1_offer).
///   EnRoute     → b1_accepted set; Fiery Twin waypoint added. Watches for the
///                 halfway point between Humble Abode and Fiery Twin while piloting.
///   PullOver    → ship frozen (velocity matched to nearest body), cop corvette
///                 flies in, interrogation conversation (conv_b1_stop).
///   Verdict     → sass >= 2 → conv_b1_arrest; 4/4 passes → conv_b1_clean;
///                 else conv_b1_ticket. EVERY outcome now leads to the chase —
///                 the interrogation decides how hard it is (the head start),
///                 not whether it happens (docs/B1_INTERROGATION_REWRITE.md).
///   Confrontation→ arrest/clean boarding demand; watches b1_run → chase.
///   TicketChoice→ b1_pay → SpendMoney(200) (fail = no head start); b1_run → chase.
///   Chase       → b1_hs_long/med grants free-flight seconds before pursuit,
///                 then CopShipController pursues + fires blasts until the rocket.
///   Delivering  → reach Fiery Twin → payout, done.
public class TevSmugglingMission : MonoBehaviour
{
    public enum Phase { Idle, EnRoute, PullOver, Interrogation, Verdict, Confrontation, TicketChoice, Chase, Delivering, Done }

    [Header("Scene refs")]
    public Transform tevNpc;                 // TEVONSHIP (standing Tev inside the ship)
    public GameObject copShipPrefab;         // Assets/F3_Corvette/Prefabs/ORG ship.prefab
    public AudioClip scareClip;              // short alien scream
    public AudioClip sirenClip;              // patrol siren whoop

    [Header("Tuning")]
    public float scareTurnSeconds = 0.5f;
    public float arrivalBuffer = 800f;       // arrival = within destBody.radius + this
    public int fineAmount = 200;
    public int payoutAmount = 500;
    public string homeBodyName = "Humble";
    public string destBodyName = "Fiery";
    [Tooltip("How long after scene load the parked ship stays kinematically pinned to its authored spot. Long enough to ride out startup origin shifts, then it's handed to real gravity and settles onto the terrain.")]
    public float settleDelaySeconds = 10f;
    [Tooltip("Raised this much along planet-up at release so the terrain collider can't depenetration-launch it; it falls this far and lands.")]
    public float releaseLift = 1.5f;
    [Header("Pull-over pacing")]
    [Tooltip("Sirens + lights shadow you for this long before the STOP YOUR ENGINE call.")]
    public float sirenLeadSeconds = 2.5f;
    [Tooltip("Once the stop call lands, the forced deceleration takes roughly this long to bring you to a halt.")]
    public float pullOverSlowSeconds = 4f;
    public AudioClip stopEngineClip;      // "CUT YOUR ENGINE" radio bark
    [Header("Chase — Tev's rocket")]
    [Tooltip("Hidden timer: Tev's shot comes roughly this many seconds after you start running.")]
    public float chaseSeconds = 45f;
    [Tooltip("After the countdown lands on 1, the hatch must be open within this window or Tev calls for a retry.")]
    public float hatchWindowSeconds = 2f;
    public AudioClip rocketFireClip;
    public AudioClip explosionClip;
    [Tooltip("Tev's alien speech loop — plays while his subtitle types out (same clip TevDialogue uses).")]
    public AudioClip tevSpeechLoopClip;
    [Tooltip("The scare spin locks the player's view onto THIS transform (user-placed 'lookat' object) until the offer conversation ends.")]
    public Transform scareLookTarget;
    [Tooltip("The pull-over fires this far into the trip (0.5 = midpoint, 0.25 = a quarter of the way to Fiery Twin).")]
    [Range(0.05f, 0.95f)]
    public float pullOverProgress = 0.25f;
    public AudioClip radarPingClip;       // one ping blip — repeats faster/louder as a taser shot closes
    public AudioClip taserZapClip;        // electric fry when a shot connects
    [Header("Suit translator — flat machine-TTS spoken OVER Tev's alien voice")]
    public AudioClip trSpiritClip;
    public AudioClip trIdeaClip;
    public AudioClip tr20SecClip;
    public AudioClip trLauncherClip;
    public AudioClip trHoldSteadyClip;
    public AudioClip trOpenHatchClip;
    public AudioClip trNotSoFastClip;
    public AudioClip trTooSlowClip;
    public AudioClip trBombsAwayClip;
    public AudioClip trDirectHitClip;
    [Tooltip("Parallel to the blast-warning lines: INCOMING / PROJECTILE INBOUND / WATCH OUT / DODGE / HE'S SHOOTING.")]
    public AudioClip[] trWarningClips;
    public AudioClip trThreeClip;
    public AudioClip trTwoClip;
    public AudioClip trOneClip;
    [Tooltip("Translator clips for the scare/offer conversation, parallel to OfferLines (the conv_b1_offer Tev lines, in file order).")]
    public AudioClip[] trOfferClips;
    [Tooltip("Officer radio clips for the interrogation conversations, parallel to CopConvLines.")]
    public AudioClip[] copConvClips;
    [Tooltip("Translator clips for Tev's lines inside the interrogation, parallel to TevConvLines.")]
    public AudioClip[] trConvClips;
    [Tooltip("The only two cop barks during the chase — timed into gaps where Tev isn't speaking.")]
    public AudioClip copChase1Clip;
    public AudioClip copChase2Clip;
    [Header("Chase — head start")]
    [Tooltip("Seconds the player flies free before the corvette begins pursuit. Set by how the traffic stop ended: long = he was mid-docking-approach, most committed, slowest to abort.")]
    public float headStartLong = 5f;
    [Tooltip("Medium head start: he was charging a scanner / logging a payment.")]
    public float headStartMedium = 3f;
    [Tooltip("Tev after the corvette goes down: get us to Fiery Twin, we lay low.")]
    public AudioClip trLayLowClip;
    [Header("Pull-over — engine-cut QTE")]
    [Tooltip("After CUT YOUR ENGINE lands, the player has this long to start cutting the engine (I). The window extends while the I hold is in progress, so it's forgiving.")]
    public float engineCutWindowSeconds = 3f;
    [Tooltip("Radio bark when the player ignores the engine-cut order — the chase starts immediately, no interrogation, no head start.")]
    public AudioClip copRunnerClip;
    [Tooltip("Radio scream the moment the player restarts the engine after the stop — Kolb knows exactly what's coming.")]
    public AudioClip copLethalForceClip;
    [Tooltip("Tev early in the chase: run TOWARD Fiery Twin so the flee distance is trip progress, not wasted fuel.")]
    public AudioClip trHeadForTwinClip;
    [Tooltip("Tev after the win: cracks the smuggled crate and tops the reactor up — the chase can't leave the player stranded short of Fiery Twin.")]
    public AudioClip trFuelTopUpClip;

    const string WaypointId = "b1_fiery_twin";

    // conv_b1_offer's Tev lines in file order — index-matched to trOfferClips.
    // MUST stay byte-identical to the JSON: the voiceover hook matches on text.
    static readonly string[] OfferLines =
    {
        "GRAAAAH!",
        "...Heh. Sorry. Couldn't resist. You should see your face.",
        "Anyway. Wanna help me smuggle some alien goodies?",
        "Space dust. A whole crate of it. And before you ask — it's not ILLEGAL illegal. It's more of a paperwork situation.",
        "Buyer's on Fiery Twin. I can't fly. You can. We split the take.",
        "HA! Knew it.",
        "Crate's already in the back. Don't ask how long it's been there.",
        "Take us up, keep it casual, and whatever happens out there — do NOT be weird.",
        "Sure. Sure. Take your time.",
        "Me and the crate will be right here. Being patient. And extremely legal.",
    };

    // Officer lines across conv_b1_stop / conv_b1_clean / conv_b1_ticket /
    // conv_b1_arrest, in file order (the "..." beats are silent on purpose;
    // duplicate strings — "Mm.", the warn_q* speech — appear ONCE and share a
    // clip) — index-matched to copConvClips. MUST stay byte-identical to the
    // JSON. Run "Validate B1 voice bindings" after any edit here or in the
    // JSONs: a mismatch plays SILENT with no error.
    static readonly string[] CopConvLines =
    {
        // conv_b1_stop — open
        "UNIDENTIFIED VESSEL. CUT YOUR THRUST.",
        "This is Galactic Patrol. Our radar operators recorded your vessel crossing this corridor significantly above the posted limit.",
        "We have reasonable suspicion of a speed violation. This is a routine check. Hold your position.",
        "Officer Kolb, badge four-one-one. Forty-one years on this corridor. I have heard every excuse a mouth can produce.",
        "Four questions. Tell me the truth, or tell me you don't know. Make something up and I will find something to charge you with. I always do.",
        // q1 / q1_lie
        "First question. Do you know how fast you were going?",
        "Mm.",
        "...I'm writing that down.",
        // q2 / q2_true / q2_lie
        "Question two. This vessel. Who does it belong to?",
        "Registered to a Tev. Flight licence revoked four years ago.",
        "...So you're the driver.",
        "The registry says this hull belongs to someone named Tev.",
        "Mm. ...Writing that down.",
        // q3 / q3_lie
        "State your business on Fiery Twin.",
        "Family.",
        "Son, the surface of Fiery Twin peels paint off a hull. Nobody has family there.",
        // q4 / q4_true / q4_static / q4_lie
        "What is that sound?",
        "...Panicking.",
        "Huh. At least that's an honest answer.",
        "...Hm.",
        "Could be my end. This channel's been garbage all week.",
        "Forget it.",
        "That's not a hull tick.",
        "I've flown hulls for forty-one years. That is a voice.",
        // warn_q1..q4 (identical on purpose — one set of clips serves all four)
        "Stop.",
        "You think you're funny. Every one of you thinks you're funny. Forty-one years, and not one of you has been.",
        "That's your one. Try it again and I stop asking questions and start filling out forms.",
        "Answer the rest of them straight.",
        // done
        "Hold position. Running your answers through central.",
        // conv_b1_clean
        "Your story checks out. Somehow.",
        "All four. First time this month.",
        "No citation. Consider the speed a warning.",
        "Which leaves one small thing.",
        "If you're doing nothing wrong, you won't mind me boarding your vessel. Just to confirm you're good to go.",
        "Good. Stand by. I'm bringing my corvette across.",
        "No.",
        "Hm. That's fine. That's completely fine.",
        "I'll run a full-spectrum scan from right here instead. Sit tight — takes about thirty seconds.",
        "...Say again?",
        // conv_b1_ticket
        "Your answers were... partially satisfactory.",
        "I'm not going to tell you which ones. You know which ones.",
        "Citation issued: one (1) count of corridor speeding.",
        "The fine is $200. Payable immediately.",
        "...Received.",
        "Now. While I've got you. Standard procedure on a paid citation — I need to log your cargo manifest.",
        "Open the hold.",
        "...Refusing to pay a lawful citation.",
        "Huh. Well. That's obstruction.",
        "And obstruction means I can legally search your vessel. OPEN UP.",
        // conv_b1_arrest
        "Central's back. Doesn't matter.",
        "I told you what would happen.",
        "I'm not writing you a ticket. I'm boarding your vessel and taking you in. And then I am going to take my time with the paperwork.",
        "Cut your engine and open the hold.",
    };

    // Tev's lines across the four interrogation convs, in file order —
    // index-matched to trConvClips. MUST stay byte-identical to the JSON.
    static readonly string[] TevConvLines =
    {
        // conv_b1_stop — panic
        "okay okay okay okay.",
        "Do NOT be weird. I need ninety seconds.",
        "And listen — he's a truth guy. I know the type. Tell him the truth, or tell him you don't know. Either one.",
        "Just do NOT make something up. And do NOT be funny.",
        // conv_b1_clean — yes_tev / no_tev / gfy_tev
        "NO. NO NO NO.",
        "HE CANNOT COME ABOARD. HE CANNOT COME ABOARD—",
        "START THE ENGINE. START IT RIGHT NOW—",
        "THIRTY SEC— NO. NO NO NO.",
        "GO. GO GO GO GO—",
        "HA! Nice one!",
        "Better have the piloting skills to back that attitude up—",
        "GO! GO!",
        // conv_b1_ticket — paid_tev / refused_tev
        "WHAT.",
        "He TOOK the money. He took the money and he's STILL—",
        "GO! GO NOW!",
        "okay okay okay — let's get the FUCK out of here—",
        "GO!",
        // conv_b1_arrest — arrest_tev
        "...what did you SAY to him.",
        "WHAT DID YOU SAY TO HIM—",
        "GO!! GO!!",
    };

    Phase _phase = Phase.Idle;
    Ship _ship;
    CelestialBody _homeBody, _destBody, _anchorBody;
    CopShipController _cop;
    bool _scared;
    bool _busy;                              // a coroutine owns the next transition
    float _nextPoll;

    Vector3 _authoredLocalPos;
    Quaternion _authoredLocalRot;
    float _releaseAt;
    Vector3 _lastPinTarget;
    Vector3 _pinVelocity;
    bool _pinSampled;
    bool _decelActive;       // forced pull-over deceleration / velocity hold running
    float _decelRate;        // units/s² toward the anchor's velocity
    AudioSource _tevVoice;   // Tev's alien babble + rocket SFX during the chase
    AudioSource _trVoice;    // the suit's translator — flat 2D TTS in the player's helmet
    TextMeshProUGUI _subtitle;
    RectTransform _subtitlePanel;
    Coroutine _subtitleCo;
    bool _countdownActive;   // hatch countdown owns the subtitle — blast warnings must not stomp it

    // Hatch quick-time event: H keycap in a red ring; a white ring shrinks
    // from 3× during Tev's 3-2-1 and lands on the red ring at "1".
    enum CdResult { Success, EarlyPress, Timeout }
    CdResult _cdResult;
    GameObject _qteRoot;
    RectTransform _qteWhiteRing;
    TextMeshProUGUI _qteKeyText;
    TextMeshProUGUI _qteCaption;
    static Sprite s_ringSprite;

    void Awake()
    {
        _ship = GetComponent<Ship>();
        // Snapshot the authored planet-local pose before anything can move us.
        _authoredLocalPos = transform.localPosition;
        _authoredLocalRot = transform.localRotation;
    }

    void Start()
    {
        // Tevsship is authored as a CHILD of Humble Abode (like placed buildings),
        // so it rides the planet through orbits and origin shifts with no physics.
        // It must NOT be EndlessManager-registered: a registered planet-child gets
        // double-shifted on every origin shift (parent moves it AND its own entry
        // moves it) and flings ~24k out into space. EndlessManager.Bootstrap
        // registers "the first Ship it finds" — which can be this one now that
        // two ships exist — so undo that here (Start runs after AfterSceneLoad
        // but before the first origin shift), and restore the authored pose in
        // case anything already moved us.
        var endless = FindObjectOfType<EndlessManager>();
        if (endless != null) endless.UnregisterPhysicsObject(transform);
        transform.localPosition = _authoredLocalPos;
        transform.localRotation = _authoredLocalRot;
        Physics.SyncTransforms();

        // For the first settleDelaySeconds the ship stays KINEMATIC and is
        // pinned to its authored planet-local pose every physics tick (see
        // FixedUpdate) — rides the planet exactly through the startup origin
        // shifts, can't drift, can't be depenetration-launched by the planet's
        // 2M-triangle collider while everything is still settling. After that
        // it's released to real gravity (ReleaseToGravity) and just sits on
        // the terrain like any landed ship. Locked (canFly=false) until the
        // player accepts the job.
        var rb0 = _ship != null ? _ship.Rigidbody : null;
        if (rb0 != null) rb0.isKinematic = true;
        if (_ship != null) _ship.canFly = false;
        _releaseAt = Time.time + settleDelaySeconds;
        _pinSampled = false;

        // Cheap resume from a save that already has mission flags.
        if (Flag("b1_delivered")) { _phase = Phase.Done; return; }
        if (Flag("b1_accepted")) { _scared = true; BeginEnRoute(); }
    }

    // ── Entry: the SCARE trigger (TevScareTrigger forwards here) ──

    public void OnPlayerEnteredShip(PlayerController pc)
    {
        if (_phase != Phase.Idle || _busy || pc == null) return;
        if (Flag("b1_accepted")) return;

        if (!_scared)
        {
            _scared = true;
            StartCoroutine(ScareRoutine(pc));
        }
        else if (!WorldDialogueUI.IsOpen)
        {
            StartCoroutine(ConvVoiceover());
            WorldDialogueUI.Begin("conv_b1_offer");
        }
    }

    /// Layers per-line audio onto the mission's preset conversations: Tev
    /// lines get alien babble + the suit translator, officer lines get the
    /// fuzzy patrol radio — and each line's typewriter is paced to its clip
    /// so text and voice finish together (LineDelayOverride). Subscribe
    /// BEFORE Begin() or the first line's event fires unheard. Lives until
    /// the conversation closes, then cleans up after itself.
    float _babbleUntil;
    float _convLineDelay;    // per-char delay reported to WorldDialogueUI for the current line
    AudioSource _copVoice;   // officer's radio in the player's helmet (2D)

    IEnumerator ConvVoiceover()
    {
        WorldDialogueUI.OnLineShown -= OnConvLine;   // never double-subscribe
        WorldDialogueUI.OnLineShown += OnConvLine;
        WorldDialogueUI.LineDelayOverride = () => _convLineDelay;

        // Wait for the conversation to open (with a timeout so an aborted
        // Begin can't leak the subscription), then ride it until it closes.
        float openBy = Time.time + 10f;
        while (!WorldDialogueUI.IsOpen && Time.time < openBy) yield return null;
        while (WorldDialogueUI.IsOpen)
        {
            if (_tevVoice != null && _tevVoice.loop && Time.time >= _babbleUntil) StopBabble();
            yield return null;
        }

        WorldDialogueUI.OnLineShown -= OnConvLine;
        WorldDialogueUI.LineDelayOverride = null;
        _convLineDelay = 0f;
        StopBabble();
        if (_trVoice != null) _trVoice.Stop();
        if (_copVoice != null) _copVoice.Stop();
    }

    void OnConvLine(string speaker, string line)
    {
        _convLineDelay = 0f;
        if (speaker != null && speaker.StartsWith("TEV"))
        {
            StartBabble();
            _babbleUntil = Time.time + Mathf.Max(0.8f, line.Length * 0.015f + 0.4f);
            AudioClip clip = FindClip(line, OfferLines, trOfferClips);
            if (clip == null) clip = FindClip(line, TevConvLines, trConvClips);
            if (clip != null)
            {
                PlayTranslator(clip);
                _convLineDelay = clip.length * 0.92f / Mathf.Max(1, line.Length);
                _babbleUntil = Time.time + Mathf.Min(clip.length, 3.5f);
            }
        }
        else
        {
            StopBabble();
            AudioClip clip = FindClip(line, CopConvLines, copConvClips);
            if (clip != null)
            {
                PlayCopRadio(clip);
                _convLineDelay = clip.length * 0.92f / Mathf.Max(1, line.Length);
            }
        }
    }

    static AudioClip FindClip(string line, string[] table, AudioClip[] clips)
    {
        if (clips == null) return null;
        for (int i = 0; i < table.Length && i < clips.Length; i++)
            if (table[i] == line) return clips[i];
        return null;
    }

    /// Officer over the cockpit radio — 2D, one line at a time (a new line
    /// cuts the previous one, mirroring the translator).
    void PlayCopRadio(AudioClip clip)
    {
        if (_copVoice == null)
        {
            _copVoice = gameObject.AddComponent<AudioSource>();
            _copVoice.spatialBlend = 0f;
            _copVoice.volume = 0.85f;
        }
        _copVoice.Stop();
        if (clip == null) return;
        _copVoice.clip = clip;
        _copVoice.Play();
    }

    IEnumerator ScareRoutine(PlayerController pc)
    {
        _busy = true;

        // Snapshot + lock input (LockAll wipes the unlocked set, so restore the
        // exact prior state — the game may be mid-tutorial).
        bool gateWasEnabled = TutorialGate.IsGateEnabled;
        var unlockedSnapshot = new List<TutorialAbility>(TutorialGate.GetUnlocked());
        TutorialGate.LockAll();

        Vector3 LookPoint() => scareLookTarget != null ? scareLookTarget.position
                             : tevNpc != null ? tevNpc.position + tevNpc.up * 0.5f
                             : transform.position;

        if (scareClip != null) AudioSource.PlayClipAtPoint(scareClip, LookPoint(), 1f);

        // Speed chosen so the full spin takes ~scareTurnSeconds regardless of angle.
        float angle = Vector3.Angle(pc.transform.forward, LookPoint() - pc.transform.position);
        float degPerSec = Mathf.Max(90f, angle / Mathf.Max(0.05f, scareTurnSeconds));

        // GRIP the view every frame until the offer conversation is done — a
        // single spin used to snap back to the old look direction the moment
        // we stopped calling ForceLookAtSmooth. For the first ~1s the grip
        // chases at spin speed (the jittery fight reads as the scare shake —
        // keep it); after that: DEAD ZONE — inside 0.8° of the target we
        // don't touch the camera at all (look input is gate-locked, so it
        // stays put on its own = perfectly still), and outside it a
        // proportional speed (5× the remaining angle) eases it back without
        // ever oscillating. Movement + look return via ApplyState after the
        // dialogue closes.
        var camT = Camera.main != null ? Camera.main.transform : null;
        float start = Time.time;
        float openAt = start + scareTurnSeconds + 0.35f;
        bool opened = false;
        float failsafe = start + 90f;
        while (Time.time < failsafe)
        {
            if (pc == null) break;
            bool shakePhase = Time.time - start < 1f;
            float err = camT != null ? Vector3.Angle(camT.forward, LookPoint() - camT.position) : 20f;
            if (shakePhase)
                pc.ForceLookAtSmooth(LookPoint(), degPerSec);
            else if (err > 0.8f)
                pc.ForceLookAtSmooth(LookPoint(), Mathf.Max(5f, err * 5f));
            if (!opened && Time.time >= openAt)
            {
                StartCoroutine(ConvVoiceover());
                WorldDialogueUI.Begin("conv_b1_offer");
                opened = true;
            }
            else if (opened && !WorldDialogueUI.IsOpen) break;
            yield return null;
        }

        TutorialGate.ApplyState(gateWasEnabled, unlockedSnapshot);
        _busy = false;
    }

    // ── Polling state machine ──

    void Update()
    {
        if (_phase == Phase.Done || _busy) return;
        if (Time.time < _nextPoll) return;
        _nextPoll = Time.time + 0.2f;

        // Immovable ship-HUD marker on the destination: pinned the moment the
        // player takes the stick, immune to clicks (ShipHUD swallows marker
        // input while a pin is set), cleared on arrival. The 2D compass
        // waypoint only helps on foot — this is the in-space pointer, and it
        // survives the whole chase.
        if (_phase != Phase.Idle && ShipHUD.MissionPin == null &&
            Ship.PilotedInstance == _ship)
        {
            if (_destBody == null) _destBody = FindBody(destBodyName);
            ShipHUD.MissionPin = _destBody;
        }

        switch (_phase)
        {
            case Phase.Idle:
                if (Flag("b1_accepted")) BeginEnRoute();
                break;

            case Phase.EnRoute:
                CheckMidpoint();
                break;

            case Phase.Interrogation:
                if (Flag("b1_interrogation_done") && !WorldDialogueUI.IsOpen)
                    StartCoroutine(VerdictRoutine());
                break;

            case Phase.Confrontation:
                if (!WorldDialogueUI.IsOpen && Flag("b1_run")) StartChase();
                break;

            case Phase.TicketChoice:
                if (WorldDialogueUI.IsOpen) break;
                if (Flag("b1_pay"))
                {
                    // Paying no longer ends the stop — Kolb demands the hold
                    // anyway; the $200 buys the head start (he's distracted
                    // logging the payment). If the spend bounces he notices,
                    // and the head start is gone.
                    var wallet = PlayerWallet.Instance;
                    if (wallet == null || !wallet.SpendMoney(fineAmount))
                        SetFlag("b1_hs_med", false);
                    SetFlag("b1_pay", false);   // never charge twice
                }
                if (Flag("b1_run")) StartChase();
                break;

            case Phase.Delivering:
                CheckArrival();
                break;
        }
    }

    void FixedUpdate()
    {
        if (_ship == null) return;
        var rb = _ship.Rigidbody;
        if (rb == null) return;

        // Parked startup pin: kinematic, glued to the authored planet-local
        // pose. Rides orbit + spin exactly; immune to origin shifts and
        // depenetration. While pinned we also measure the parked spot's TRUE
        // world velocity (orbit + spin combined — the planet's `velocity`
        // property alone understates its actual transform motion), so the
        // hand-off to real physics is seamless. After settleDelaySeconds the
        // ship is released and gravity owns it from then on.
        if (rb.isKinematic && _phase == Phase.Idle)
        {
            var parent = transform.parent;
            if (parent != null)
            {
                Vector3 target = parent.TransformPoint(_authoredLocalPos);
                if (_pinSampled)
                    _pinVelocity = (target - _lastPinTarget) / Time.fixedDeltaTime;
                _lastPinTarget = target;
                _pinSampled = true;
                rb.MovePosition(target);
                rb.MoveRotation(parent.rotation * _authoredLocalRot);
            }
            if (Time.time >= _releaseAt) ReleaseToGravity();
            return;
        }
        if (_phase == Phase.Idle) return;

        // Traffic stop: ramp down to a halt relative to the nearest planet
        // (instead of an instant velocity snap), then keep holding that match
        // through the interrogation. _decelActive stays true until the stop
        // resolves (Release / StartChase), so once the relative velocity hits
        // zero this doubles as the hold. Planets keep orbiting normally; the
        // ship just rides along with the closest one.
        bool frozen = _phase == Phase.PullOver || _phase == Phase.Interrogation ||
                      _phase == Phase.Verdict || _phase == Phase.Confrontation ||
                      _phase == Phase.TicketChoice;
        if (!frozen || !_decelActive || _anchorBody == null || rb.isKinematic) return;

        // Gravity-compensated: Ship.FixedUpdate re-adds N-body gravity every
        // tick (it runs before us), so without the +grav term the hold
        // plateaus wherever gravity matches _decelRate instead of stopping.
        float grav = NBodySimulation.CalculateAcceleration(rb.position).magnitude;
        Vector3 rel = rb.velocity - _anchorBody.velocity;
        rb.velocity = _anchorBody.velocity +
                      Vector3.MoveTowards(rel, Vector3.zero, (_decelRate + grav) * Time.fixedDeltaTime);
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, 0.15f);
    }

    // ── Phase transitions ──

    /// Hand the parked ship from the kinematic pin to real physics. Safe to
    /// call more than once — no-op if already released. Unparents from the
    /// planet (a live rigidbody can't sit under a moving planet transform —
    /// the parent's motion teleports it every frame and fights the physics),
    /// registers with the floating origin like any runtime ship, lifts a hair
    /// so the terrain collider can't depenetration-launch it, then lets
    /// Ship's own N-body gravity drop it onto the ground.
    void ReleaseToGravity()
    {
        var rb = _ship != null ? _ship.Rigidbody : null;
        if (rb == null || !rb.isKinematic) return;

        var body = GetComponentInParent<CelestialBody>();
        transform.SetParent(null, true);

        Vector3 up = body != null ? (rb.position - body.Position).normalized : transform.up;
        transform.position += up * releaseLift;
        Physics.SyncTransforms();

        rb.isKinematic = false;
        rb.maxDepenetrationVelocity = 2f;   // the 2M-tri planet collider once launched this ship at 21k/s
        rb.velocity = _pinSampled ? _pinVelocity
                    : body != null ? body.velocity : Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        var endless = FindObjectOfType<EndlessManager>();
        if (endless != null) endless.RegisterPhysicsObject(transform);
    }

    void BeginEnRoute()
    {
        _phase = Phase.EnRoute;

        _homeBody = FindBody(homeBodyName);
        _destBody = FindBody(destBodyName);

        ReleaseToGravity();   // no-op if the startup park already released it
        if (_ship != null) _ship.canFly = true;
        if (CompassHUD.Instance != null && _destBody != null)
        {
            var dest = _destBody;
            CompassHUD.Instance.AddWaypoint(WaypointId, () => dest != null ? dest.Position : Vector3.zero, "Fiery Twin");
        }
    }

    void CheckMidpoint()
    {
        if (Ship.PilotedInstance != _ship) return;
        if (_homeBody == null) _homeBody = FindBody(homeBodyName);
        if (_destBody == null) _destBody = FindBody(destBodyName);
        if (_homeBody == null || _destBody == null) return;

        float dHome = Vector3.Distance(transform.position, _homeBody.Position);
        float dDest = Vector3.Distance(transform.position, _destBody.Position);

        // Pull over at pullOverProgress of the trip (fraction of the way to
        // the destination), not the midpoint — but never while still close
        // to home's surface, so a low first-fraction can't fire on takeoff.
        if (dHome < _homeBody.radius + 600f) return;
        if (dHome / Mathf.Max(1f, dHome + dDest) >= pullOverProgress) BeginPullOver();
    }

    void BeginPullOver()
    {
        if (_busy) return;
        StartCoroutine(PullOverRoutine());
    }

    /// Staged stop: sirens + lights shadow the player from behind → "STOP
    /// YOUR ENGINE" over the radio → controls cut and the ship decelerates
    /// smoothly → as it rolls to a halt the corvette sweeps around and parks
    /// dead ahead → interrogation.
    IEnumerator PullOverRoutine()
    {
        _busy = true;
        _phase = Phase.PullOver;
        _anchorBody = FindNearestBody();

        // Per-stop flags must start clean or a retry inherits last attempt's answers.
        SetFlag("b1_q1_pass", false);
        SetFlag("b1_q2_pass", false);
        SetFlag("b1_q3_pass", false);
        SetFlag("b1_q4_pass", false);
        SetFlag("b1_sass_q1", false);
        SetFlag("b1_sass_q2", false);
        SetFlag("b1_sass_q3", false);
        SetFlag("b1_sass_q4", false);
        SetFlag("b1_interrogation_done", false);
        SetFlag("b1_pay", false);
        SetFlag("b1_run", false);
        SetFlag("b1_hs_long", false);
        SetFlag("b1_hs_med", false);
        SetFlag("b1_attitude", false);

        // 1. The corvette starts ONE continuous glide toward the ship — sirens,
        //    lights. The glide is stretched to cover the whole staging (it never
        //    visibly parks-and-waits; that stop/wait/re-approach read as janky).
        if (copShipPrefab != null)
        {
            var go = Instantiate(copShipPrefab);
            _cop = go.AddComponent<CopShipController>();
            _cop.flyInSeconds = sirenLeadSeconds + pullOverSlowSeconds + 6f;
            _cop.Init(_ship, _anchorBody, sirenClip);
            _cop.pingClip = radarPingClip;
            _cop.zapClip = taserZapClip;
        }
        yield return new WaitForSeconds(sirenLeadSeconds);

        // 2. "STOP YOUR ENGINE."
        if (_cop != null && stopEngineClip != null)
        {
            _cop.PlayRadio(stopEngineClip);
            yield return new WaitForSeconds(Mathf.Max(1f, stopEngineClip.length * 0.9f));
        }

        // 2b. Engine-cut QTE: the player must actually cut the engine (hold I
        // from the pilot seat) within engineCutWindowSeconds. Forgiving: the
        // deadline stretches while the I hold is in progress. Ignore the order
        // and Kolb calls it in — no interrogation, no head start, pursuit NOW.
        bool engineCut = _ship == null || !_ship.EngineOn;   // coasting cold already = compliant
        if (!engineCut)
        {
            SetQteKey("I", "I TO SHUT DOWN ENGINE");
            _qteRoot.SetActive(true);
            _qteWhiteRing.localScale = Vector3.one * 3f;
            float qteStart = Time.time;
            float hardCap = engineCutWindowSeconds + 2.5f;   // grace can't stall the stop forever
            while (true)
            {
                float t = Time.time - qteStart;
                if (_ship != null && !_ship.EngineOn) { engineCut = true; break; }
                if (t >= engineCutWindowSeconds && !Input.GetKey(KeyCode.I)) break;
                if (t >= hardCap) break;
                _qteWhiteRing.localScale = Vector3.one *
                    Mathf.Lerp(3f, 1f, Mathf.Clamp01(t / engineCutWindowSeconds));
                yield return null;
            }
            QteHide();
        }

        if (!engineCut)
        {
            // "WE GOT A RUNNER!" — straight to the chase from wherever the
            // corvette currently is; the engine was never cut, so the ship
            // keeps flying under the player's control.
            PlayCopRadio(copRunnerClip);
            _busy = false;
            StartChase(immediateFlee: true);
            yield break;
        }

        // 3. Controls cut; forced smooth deceleration down to a stop relative
        //    to the anchor planet (see FixedUpdate). Rate is sized so however
        //    fast they were going, the slowdown takes ~pullOverSlowSeconds.
        //    The corvette's glide redirects to the interrogation pose NOW (the
        //    hull is rotation-locked from here, so the pose is stable) — it
        //    sweeps in while the ship slows and both finish together.
        if (_ship != null) _ship.canFly = false;
        var rb = _ship != null ? _ship.Rigidbody : null;
        float relSpeed = rb != null && _anchorBody != null
            ? (rb.velocity - _anchorBody.velocity).magnitude : 0f;
        _decelRate = Mathf.Max(8f, relSpeed / Mathf.Max(0.5f, pullOverSlowSeconds));
        _decelActive = true;

        bool copParked = _cop == null;
        if (_cop != null)
        {
            _cop.flyInSeconds = Mathf.Max(2f, pullOverSlowSeconds);
            _cop.PullInFront(() => copParked = true);
        }
        else
        {
            Debug.LogWarning("[TevSmuggling] copShipPrefab not assigned — skipping straight to interrogation.");
        }

        // 4. Dialogue the moment BOTH are set: corvette parked dead ahead and
        //    the ship rolled (nearly) to its stop. No pause in between.
        float stopBy = Time.time + pullOverSlowSeconds + 12f;   // safety timeout
        float trigger = Mathf.Max(4f, relSpeed * 0.15f);
        while (Time.time < stopBy)
        {
            bool shipSlow = rb == null || _anchorBody == null ||
                            (rb.velocity - _anchorBody.velocity).magnitude <= trigger;
            if (copParked && shipSlow) break;
            yield return null;
        }

        StartCoroutine(ConvVoiceover());
        WorldDialogueUI.Begin("conv_b1_stop");
        _phase = Phase.Interrogation;
        _busy = false;
    }

    IEnumerator VerdictRoutine()
    {
        _busy = true;
        _phase = Phase.Verdict;
        yield return new WaitForSeconds(1.2f);   // "running your answers through central"

        int passes = 0;
        if (Flag("b1_q1_pass")) passes++;
        if (Flag("b1_q2_pass")) passes++;
        if (Flag("b1_q3_pass")) passes++;
        if (Flag("b1_q4_pass")) passes++;

        int sass = 0;
        if (Flag("b1_sass_q1")) sass++;
        if (Flag("b1_sass_q2")) sass++;
        if (Flag("b1_sass_q3")) sass++;
        if (Flag("b1_sass_q4")) sass++;

        StartCoroutine(ConvVoiceover());
        if (sass >= 2)
        {
            // He warned you. He does not warn twice.
            WorldDialogueUI.Begin("conv_b1_arrest");
            _phase = Phase.Confrontation;
        }
        else if (passes >= 4)
        {
            // Clean run: no fine — but he still wants inside the hold.
            WorldDialogueUI.Begin("conv_b1_clean");
            _phase = Phase.Confrontation;
        }
        else
        {
            WorldDialogueUI.Begin("conv_b1_ticket");
            _phase = Phase.TicketChoice;
        }
        _busy = false;
    }

    void StartChase(bool immediateFlee = false)
    {
        if (_ship != null) _ship.canFly = true;
        _decelActive = false;
        _anchorBody = null;
        _phase = Phase.Chase;

        if (_cop == null) { _phase = Phase.Delivering; return; }

        // The politer the stop ended, the further away Kolb is when Tev punches
        // it: he's mid-docking-approach / mid-scan / logging the payment. A
        // runner (ignored the engine-cut order) gets pursued on the spot.
        float headStart = immediateFlee ? 0f
                        : Flag("b1_hs_long") ? headStartLong
                        : Flag("b1_hs_med")  ? headStartMedium
                        : 0f;
        StartCoroutine(ChaseRoutine(headStart, immediateFlee));
    }

    IEnumerator ChaseRoutine(float headStart, bool immediateFlee)
    {
        if (!immediateFlee)
        {
            // The stop ended with the engine cut, so IGNITION is the tell:
            // the moment the player spins the engine back up, Kolb screams
            // the lethal-force warning — and how long he takes to actually
            // get rolling is the head start the stop earned (he's mid-
            // docking-approach / mid-scan / logging the payment).
            while (_phase == Phase.Chase && _ship != null && !_ship.EngineOn)
                yield return null;
            if (_phase != Phase.Chase) yield break;
            PlayCopRadio(copLethalForceClip);
            if (headStart > 0f) yield return new WaitForSeconds(headStart);
        }
        if (_phase != Phase.Chase || _cop == null) yield break;

        // This chase is scripted: you can't out-RANGE the corvette and it never
        // runs dry — it ends when Tev's rocket connects (or you eat 3 hits).
        _cop.escapeDistance = 999999f;
        _cop.maxBlasts = 999;

        // immediateFlee always: after the ignition tell (or the QTE runner
        // branch) Kolb doesn't wait to see movement — he swings straight
        // around behind the ship.
        _cop.StartChase(
            immediateFlee: true,
            onEscaped: () =>
            {
                SetFlag("b1_outlaw", true);
                _phase = Phase.Delivering;
            },
            onCaught: () =>
            {
                // Blown up — routes through the normal death path, which reloads
                // the newest save via DeathCutsceneController.
                if (ResourceManager.Instance != null) ResourceManager.Instance.TakeDamage(99999f);
            },
            onFleeDetected: () =>
            {
                StartCoroutine(TevRocketRoutine());
                StartCoroutine(CopBarkRoutine(Time.time));
            });

        // Tev calls out every incoming shot — but never over his scripted
        // lines or the hatch countdown.
        _cop.onBlastFired = () =>
        {
            if (_subtitleCo != null || _countdownActive) return;
            int i = UnityEngine.Random.Range(0, BlastWarnings.Length);
            ShowTevLine(BlastWarnings[i],
                trWarningClips != null && i < trWarningClips.Length ? trWarningClips[i] : null);
        };
    }

    /// The cop speaks exactly TWICE during the chase, in the gaps between
    /// Tev's scripted lines (waits for the subtitle to be free; skips rather
    /// than talk over the hatch countdown).
    IEnumerator CopBarkRoutine(float t0)
    {
        yield return BarkWhenClear(t0, 13f, copChase1Clip);
        yield return BarkWhenClear(t0, 32f, copChase2Clip);
    }

    IEnumerator BarkWhenClear(float t0, float at, AudioClip clip)
    {
        while (_phase == Phase.Chase && Time.time - t0 < at) yield return null;
        float giveUp = Time.time + 8f;
        while (_phase == Phase.Chase && Time.time < giveUp && (_subtitleCo != null || _countdownActive))
            yield return null;
        if (_phase != Phase.Chase || _subtitleCo != null || _countdownActive) yield break;
        PlayCopRadio(clip);
    }

    static readonly string[] BlastWarnings =
    {
        "INCOMING!",
        "PROJECTILE INBOUND!",
        "WATCHHH OUTTTT!",
        "DODGE! DODGE! DODGE!",
        "HE'S SHOOTING AT US! HE'S ACTUALLY SHOOTING AT US!",
    };

    // ── Tev's rocket-launcher finale ──

    const float SubtitleCharDelay = 0.028f;

    void EnsureTevVoice()
    {
        if (_tevVoice != null) return;
        var host = tevNpc != null ? tevNpc.gameObject : gameObject;
        _tevVoice = host.AddComponent<AudioSource>();
        _tevVoice.spatialBlend = 0.35f;   // mostly-2D: he's right behind your seat
        _tevVoice.volume = 1f;
    }

    // In-flight dialogue box for Tev's chase chatter — the player is piloting,
    // so this can't be the click-through dialogue UI, but it wears the SAME
    // look as WorldDialogueUI (dark panel, copper speaker, blue-white body)
    // so it reads as part of the mission's conversation language. Same speech
    // treatment as TevDialogue: alien babble loop while the text types out.
    void EnsureSubtitleUI()
    {
        if (_subtitle != null) return;
        var canvasGo = new GameObject("TevChaseSubtitles");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 880;   // above helmet frame (805) + condensation (838), just under the real dialogue box (900)
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _subtitlePanel = panelGo.AddComponent<RectTransform>();
        _subtitlePanel.anchorMin = new Vector2(0.14f, 0.03f);   // same footprint as WorldDialogueUI's panel
        _subtitlePanel.anchorMax = new Vector2(0.86f, 0.24f);
        _subtitlePanel.offsetMin = Vector2.zero;
        _subtitlePanel.offsetMax = Vector2.zero;
        panelGo.AddComponent<UnityEngine.UI.Image>().color = new Color(0.05f, 0.07f, 0.10f, 0.92f);

        var speakerGo = new GameObject("Speaker");
        speakerGo.transform.SetParent(panelGo.transform, false);
        var speaker = speakerGo.AddComponent<TextMeshProUGUI>();
        var srt = speaker.rectTransform;
        srt.anchorMin = new Vector2(0f, 0.68f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(24f, 0f);
        srt.offsetMax = new Vector2(-24f, -12f);
        speaker.fontSize = 26f;
        speaker.color = new Color(1.00f, 0.68f, 0.36f);   // WorldDialogueUI's copper
        speaker.alignment = TextAlignmentOptions.TopLeft;
        speaker.text = "TEV - TRANSLATING";

        var textGo = new GameObject("Body");
        textGo.transform.SetParent(panelGo.transform, false);
        _subtitle = textGo.AddComponent<TextMeshProUGUI>();
        var rt = _subtitle.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.68f);
        rt.offsetMin = new Vector2(24f, 12f);
        rt.offsetMax = new Vector2(-24f, -4f);
        _subtitle.alignment = TextAlignmentOptions.TopLeft;
        _subtitle.fontSize = 30f;
        _subtitle.color = new Color(0.85f, 0.90f, 1.00f);   // WorldDialogueUI's body color
        _subtitle.text = "";
        _subtitlePanel.gameObject.SetActive(false);
    }

    void StartBabble()
    {
        EnsureTevVoice();
        if (tevSpeechLoopClip == null) return;
        _tevVoice.clip = tevSpeechLoopClip;
        _tevVoice.loop = true;
        _tevVoice.Play();
    }

    void StopBabble()
    {
        if (_tevVoice == null || !_tevVoice.loop) return;
        _tevVoice.Stop();
        _tevVoice.loop = false;
    }

    /// The suit's real-time translator: a flat machine voice speaks the
    /// English line in the player's helmet WHILE Tev's alien voice babbles —
    /// eyes stay on the road. A new line cuts off the previous translation
    /// (matches the subtitle being replaced).
    void PlayTranslator(AudioClip clip)
    {
        if (_trVoice == null)
        {
            _trVoice = gameObject.AddComponent<AudioSource>();
            _trVoice.spatialBlend = 0f;   // in-helmet speaker
            _trVoice.volume = 1f;
        }
        _trVoice.Stop();
        if (clip == null) return;
        _trVoice.clip = clip;
        _trVoice.Play();
    }

    void ShowTevLine(string line, AudioClip translated = null)
    {
        if (_subtitleCo != null) StopCoroutine(_subtitleCo);
        _subtitleCo = StartCoroutine(TevLineRoutine(line, translated));
    }

    IEnumerator TevLineRoutine(string line, AudioClip translated)
    {
        EnsureSubtitleUI();
        _subtitlePanel.gameObject.SetActive(true);
        float start = Time.time;
        StartBabble();
        PlayTranslator(translated);
        // Sync the typewriter to the translator: pace the reveal so the last
        // character lands as the voice finishes — text and speech end together.
        float delay = translated != null
            ? Mathf.Max(SubtitleCharDelay, translated.length * 0.92f / Mathf.Max(1, line.Length))
            : SubtitleCharDelay;
        yield return DialogueTextStyling.RevealCharsTMP(_subtitle, line, delay, () => false);
        // Short barks ("INCOMING!") reveal in a blink — hold the babble a
        // moment so the alien voice actually registers.
        while (Time.time - start < 1.0f) yield return null;
        StopBabble();
        // Never drop the subtitle while the translation is still speaking.
        while (_trVoice != null && _trVoice.isPlaying) yield return null;
        yield return new WaitForSeconds(1.6f);
        HideSubtitle();
        _subtitleCo = null;
    }

    void HideSubtitle()
    {
        if (_subtitle == null) return;
        _subtitle.text = "";
        _subtitlePanel.gameObject.SetActive(false);
    }

    // ── hatch quick-time event UI ──

    void EnsureQteUI()
    {
        if (_qteRoot != null) return;
        EnsureSubtitleUI();   // owns the canvas
        Transform canvas = _subtitlePanel.parent;

        _qteRoot = new GameObject("HatchQTE");
        _qteRoot.transform.SetParent(canvas, false);
        var root = _qteRoot.AddComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.46f);
        root.sizeDelta = Vector2.zero;

        MakeRing("RedRing", root, new Color(1f, 0.25f, 0.20f, 0.95f));
        _qteWhiteRing = MakeRing("WhiteRing", root, Color.white);

        // Keycap: bordered dark square with a bold H.
        var border = new GameObject("CapBorder");
        border.transform.SetParent(root, false);
        var brt = border.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(96f, 96f);
        border.AddComponent<UnityEngine.UI.Image>().color = new Color(0.9f, 0.94f, 1f, 0.95f);

        var cap = new GameObject("Cap");
        cap.transform.SetParent(root, false);
        var crt = cap.AddComponent<RectTransform>();
        crt.sizeDelta = new Vector2(86f, 86f);
        cap.AddComponent<UnityEngine.UI.Image>().color = new Color(0.10f, 0.12f, 0.16f, 1f);

        var hGo = new GameObject("Key");
        hGo.transform.SetParent(root, false);
        _qteKeyText = hGo.AddComponent<TextMeshProUGUI>();
        _qteKeyText.rectTransform.sizeDelta = new Vector2(96f, 96f);
        _qteKeyText.text = "H";
        _qteKeyText.fontSize = 56f;
        _qteKeyText.fontStyle = FontStyles.Bold;
        _qteKeyText.color = Color.white;
        _qteKeyText.alignment = TextAlignmentOptions.Center;

        // Caption above the rings ("E TO SHUT DOWN ENGINE") — only some QTEs
        // set one; the hatch shot's context comes from Tev's subtitle instead.
        var capGo = new GameObject("Caption");
        capGo.transform.SetParent(root, false);
        _qteCaption = capGo.AddComponent<TextMeshProUGUI>();
        var caprt = _qteCaption.rectTransform;
        caprt.anchorMin = caprt.anchorMax = new Vector2(0.5f, 0.5f);
        caprt.anchoredPosition = new Vector2(0f, 132f);
        caprt.sizeDelta = new Vector2(700f, 44f);
        _qteCaption.fontSize = 30f;
        _qteCaption.fontStyle = FontStyles.Bold;
        _qteCaption.color = new Color(0.9f, 0.94f, 1f);
        _qteCaption.alignment = TextAlignmentOptions.Center;

        _qteRoot.SetActive(false);
    }

    /// The QTE keycap is shared: H for the hatch shot, E for the engine cut.
    /// caption ("" = none) sits above the rings.
    void SetQteKey(string key, string caption = "")
    {
        EnsureQteUI();
        if (_qteKeyText.text != key) _qteKeyText.text = key;
        _qteCaption.text = caption;
        _qteCaption.gameObject.SetActive(!string.IsNullOrEmpty(caption));
    }

    RectTransform MakeRing(string name, RectTransform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(170f, 170f);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = RingSprite();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static Sprite RingSprite()
    {
        if (s_ringSprite != null) return s_ringSprite;
        const int size = 256;
        float outer = 124f, inner = 106f, c = size * 0.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                // 2px soft edges so the ring doesn't look jagged.
                float a = Mathf.Clamp01((outer - d) * 0.5f) * Mathf.Clamp01((d - inner) * 0.5f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        s_ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return s_ringSprite;
    }

    void QteHide()
    {
        if (_qteRoot != null) _qteRoot.SetActive(false);
    }

    /// The hidden chase clock. Starts the moment the cop sees you run. Tev
    /// scrambles for his rocket launcher in the back, narrating over the
    /// engine noise, and at T-5s calls the shot: a 3-2-1 countdown, the player
    /// must pop the hatch (H) within hatchWindowSeconds of "1" — venting the
    /// hull — and the rocket takes the corvette out of the sky. Miss the
    /// window and he resets for another pass until you get it.
    IEnumerator TevRocketRoutine()
    {
        float t0 = Time.time;

        yield return new WaitForSeconds(1f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("THAT'S THE SPIRIT! But what's your plan?! You can't outrun this ship!", trSpiritClip);

        // Point the run at the destination: every second of fleeing should be
        // trip progress, not fuel burned in a random direction. The compass
        // waypoint is already up from BeginEnRoute.
        yield return WaitUntilChaseTime(t0, 5.5f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("HEAD FOR FIERY TWIN! FLOOR IT! I'LL HANDLE THE COP!", trHeadForTwinClip);

        yield return WaitUntilChaseTime(t0, 11f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("Wait, wait, wait... I've got an idea! Keep driving! Don't you DARE slow down!", trIdeaClip);

        yield return WaitUntilChaseTime(t0, 17f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("Just give me twenty seconds! I know what to do!", tr20SecClip);

        yield return WaitUntilChaseTime(t0, 27f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("STUPID ROCKET LAUNCHER! WHERE did I put the EMERGENCY ROCKETS?!", trLauncherClip);

        yield return WaitUntilChaseTime(t0, chaseSeconds - 5f);
        if (_phase != Phase.Chase) yield break;

        // CEASE FIRE: no more taser shots from here on, and let anything
        // already in flight resolve — the player needs a calm moment to
        // actually read Tev (he doesn't speak English, the subtitle IS the
        // information).
        if (_cop != null) _cop.HoldFire(true);
        while (_phase == Phase.Chase && CopEnergyBlast.ActiveCount > 0) yield return null;
        if (_phase != Phase.Chase) yield break;

        ShowTevLine("HOLD HER STEADY! I'VE GOT A SHOT!", trHoldSteadyClip);
        yield return new WaitForSeconds(2.6f);

        // Countdown QTE loop: white ring shrinks onto the H keycap during
        // 3-2-1, then hatchWindowSeconds to pop the hatch. Early press =
        // NOT SO FAST + restart. Timeout = one punishment shot, then again.
        while (_phase == Phase.Chase)
        {
            // Clean slate: the QTE needs the hatch CLOSED at countdown start.
            if (_ship != null && _ship.HatchOpen) _ship.SetHatchOpen(false);

            _countdownActive = true;
            yield return TevCountdown();
            _countdownActive = false;
            if (_phase != Phase.Chase) yield break;

            if (_cdResult == CdResult.Success) break;

            if (_cdResult == CdResult.EarlyPress)
            {
                if (_ship != null && _ship.HatchOpen) _ship.ToggleHatch();   // Tev slams it shut
                ShowTevLine("NOT SO FAST!", trNotSoFastClip);
                yield return new WaitForSeconds(2.2f);
                continue;
            }

            // Timeout: the corvette makes them pay with one shot, then Tev
            // resets once the sky is clear again.
            ShowTevLine("TOO SLOW!", trTooSlowClip);
            yield return new WaitForSeconds(0.9f);
            if (_cop != null) _cop.FireOneNow();
            float clearBy = Time.time + 12f;
            while (_phase == Phase.Chase && Time.time < clearBy && CopEnergyBlast.ActiveCount > 0)
                yield return null;
            if (_phase != Phase.Chase) yield break;
            yield return new WaitForSeconds(0.8f);
        }
        if (_phase != Phase.Chase) yield break;

        // FIRE. Rocket leaves from Tev at the open hatch, homes on the corvette.
        ShowTevLine("BOMBS AWAYYYYY!", trBombsAwayClip);
        yield return new WaitForSeconds(0.9f);
        EnsureTevVoice();
        if (rocketFireClip != null) _tevVoice.PlayOneShot(rocketFireClip);
        Vector3 origin = tevNpc != null ? tevNpc.position : transform.position;
        TevRocket.Spawn(origin, _ship, _cop != null ? _cop.transform : null, 320f, onHit: () =>
        {
            if (_cop != null) _cop.BlowUp(explosionClip);
            SetFlag("b1_outlaw", true);
            _phase = Phase.Delivering;
            StartCoroutine(TevCelebrate());
        });
    }

    /// "OPEN THE HATCH!" types out with babble, then the QTE runs: the white
    /// ring shrinks from 3× onto the red ring around the H keycap during the
    /// spoken 3-2-1 (one-second beats), landing exactly on "1". The result is
    /// left in _cdResult: hatch opened during the shrink = EarlyPress; during
    /// the window after "1" = Success; window expires = Timeout.
    IEnumerator TevCountdown()
    {
        if (_subtitleCo != null) { StopCoroutine(_subtitleCo); _subtitleCo = null; }
        EnsureSubtitleUI();
        _subtitlePanel.gameObject.SetActive(true);
        const string baseTxt = "I'VE GOT THE SHOT! OPEN THE HATCH IN";
        StartBabble();
        PlayTranslator(trOpenHatchClip);
        float cdDelay = trOpenHatchClip != null
            ? Mathf.Max(SubtitleCharDelay, trOpenHatchClip.length * 0.92f / baseTxt.Length)
            : SubtitleCharDelay;
        yield return DialogueTextStyling.RevealCharsTMP(_subtitle, baseTxt, cdDelay, () => false);
        StopBabble();
        _subtitle.maxVisibleCharacters = int.MaxValue;
        yield return new WaitForSeconds(0.6f);
        if (_phase != Phase.Chase) yield break;

        SetQteKey("H");
        _qteRoot.SetActive(true);
        _qteWhiteRing.localScale = Vector3.one * 3f;

        float start = Time.time;
        int said = -1;
        while (Time.time - start < 2f)   // "3" at 0s, "2" at 1s, "1" at 2s
        {
            float t = Time.time - start;
            int step = Mathf.FloorToInt(t);
            if (step > said)
            {
                said = step;
                _subtitle.text = step == 0 ? baseTxt + " 3..." : baseTxt + " 3... 2...";
                PlayTranslator(step == 0 ? trThreeClip : trTwoClip);
            }
            _qteWhiteRing.localScale = Vector3.one * Mathf.Lerp(3f, 1f, t / 2f);

            if (_ship != null && _ship.HatchOpen)
            {
                QteHide();
                _cdResult = CdResult.EarlyPress;
                yield break;
            }
            if (_phase != Phase.Chase) { QteHide(); yield break; }
            yield return null;
        }
        _subtitle.text = baseTxt + " 3... 2... 1!";
        PlayTranslator(trOneClip);
        _qteWhiteRing.localScale = Vector3.one;

        // The window: rings pulse together — NOW is the time.
        bool open = false;
        float windowEnd = Time.time + hatchWindowSeconds;
        while (Time.time < windowEnd && _phase == Phase.Chase)
        {
            _qteWhiteRing.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 24f));
            if (_ship != null && _ship.HatchOpen) { open = true; break; }
            yield return null;
        }
        QteHide();
        _cdResult = open ? CdResult.Success : CdResult.Timeout;
    }

    IEnumerator TevCelebrate()
    {
        yield return new WaitForSeconds(1.2f);
        ShowTevLine("HAHAHA! DIRECT HIT! That's why you always pack emergency rockets!", trDirectHitClip);
        // Once the cheer clears, snap back to business: destination + lay low.
        while (_subtitleCo != null) yield return null;
        yield return new WaitForSeconds(0.8f);
        ShowTevLine("Great. Now get us to Fiery Twin, ASAP — we can lay low for a bit.", trLayLowClip);
        // The chase burns most of the reactor; Tev cracks the smuggled crate
        // and tops it up so the win can't leave the player stranded short of
        // Fiery Twin.
        while (_subtitleCo != null) yield return null;
        yield return new WaitForSeconds(0.8f);
        ShowTevLine("Oh — and the buyer's not gonna miss ONE fuel crystal. Topping us up.", trFuelTopUpClip);
        if (_ship != null) _ship.RestoreFuel(_ship.fuelMax);
    }

    IEnumerator WaitUntilChaseTime(float t0, float t)
    {
        while (_phase == Phase.Chase && Time.time - t0 < t) yield return null;
    }

    void CheckArrival()
    {
        if (_destBody == null) return;
        float d = Vector3.Distance(transform.position, _destBody.Position);
        if (d > _destBody.radius + arrivalBuffer) return;

        _phase = Phase.Done;
        SetFlag("b1_delivered", true);
        ShipHUD.MissionPin = null;   // touchdown — release the pinned marker
        if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint(WaypointId);
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(payoutAmount);
        Debug.Log("[TevSmuggling] Delivered to Fiery Twin — mission complete (barebones end).");
    }

#if UNITY_EDITOR
    /// Guard against the silent-failure trap: FindClip matches JSON lines
    /// against the C# tables byte-for-byte, and a miss plays SILENT with
    /// default typewriter pacing — no exception, no warning. This walks every
    /// conv_b1_* JSON and flags any voiced line missing from its table or
    /// bound to a null clip slot. "..." beats are silent on purpose.
    [ContextMenu("Validate B1 voice bindings")]
    void ValidateVoiceBindings()
    {
        StoryContent.LoadAll();
        int problems = 0;
        foreach (var conv in StoryContent.Conversations.Values)
        {
            if (conv.id == null || !conv.id.StartsWith("conv_b1_")) continue;
            foreach (var node in conv.nodes)
            {
                bool tev = node.speaker != null &&
                           node.speaker.StartsWith("TEV", System.StringComparison.OrdinalIgnoreCase);
                if (node.lines == null) continue;
                foreach (var line in node.lines)
                {
                    if (line == "...") continue;
                    string[] table = tev ? TevConvLines : CopConvLines;
                    AudioClip[] clips = tev ? trConvClips : copConvClips;
                    int idx = System.Array.IndexOf(table, line);
                    if (tev && idx < 0)
                    {
                        idx = System.Array.IndexOf(OfferLines, line);
                        clips = trOfferClips;
                    }
                    if (idx < 0)
                    {
                        problems++;
                        Debug.LogWarning($"[TevSmuggling] UNVOICED line in {conv.id}/{node.id} — not in the {(tev ? "Tev" : "cop")} table: \"{line}\"");
                    }
                    else if (clips == null || idx >= clips.Length || clips[idx] == null)
                    {
                        problems++;
                        Debug.LogWarning($"[TevSmuggling] NULL CLIP at {(tev ? "tr" : "cop")}ConvClips[{idx}] for {conv.id}/{node.id}: \"{line}\"");
                    }
                }
            }
        }
        Debug.Log(problems == 0
            ? "[TevSmuggling] voice bindings OK — every conv_b1_* line has a clip."
            : $"[TevSmuggling] voice bindings: {problems} problem(s) — see warnings above.");
    }
#endif

    // ── Helpers ──

    static bool Flag(string name) =>
        StoryDirector.Instance != null && StoryDirector.Instance.GetFlag(name);

    static void SetFlag(string name, bool value)
    {
        if (StoryDirector.Instance != null) StoryDirector.Instance.SetFlag(name, value);
    }

    static CelestialBody FindBody(string nameContains)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        foreach (var b in bodies)
        {
            if (b == null || string.IsNullOrEmpty(b.bodyName)) continue;
            if (b.bodyName.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return b;
        }
        return null;
    }

    CelestialBody FindNearestBody()
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best = null;
        float bestSqr = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float sqr = (b.Position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = b; }
        }
        return best;
    }
}
