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
///   Verdict     → count b1_q1..q4_pass: 4/4 → conv_b1_free; else conv_b1_ticket.
///   TicketChoice→ b1_pay → SpendMoney(200) (fail = chase); b1_run → chase.
///   Chase       → CopShipController pursues + fires blasts; escape or die.
///   Delivering  → reach Fiery Twin → payout, done.
public class TevSmugglingMission : MonoBehaviour
{
    public enum Phase { Idle, EnRoute, PullOver, Interrogation, Verdict, AwaitRelease, TicketChoice, Chase, Delivering, Done }

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
    public AudioClip stopEngineClip;      // "STOP YOUR ENGINE" radio bark
    [Tooltip("Chase warnings, escalating — one plays over the radio before every shot, in array order.")]
    public AudioClip[] copCalloutClips;
    [Header("Chase — Tev's rocket")]
    [Tooltip("Hidden timer: Tev's shot comes roughly this many seconds after you start running.")]
    public float chaseSeconds = 45f;
    [Tooltip("After the countdown lands on 1, the hatch must be open within this window or Tev calls for a retry.")]
    public float hatchWindowSeconds = 2f;
    public AudioClip copPursuitClip;      // cop, the moment you bolt: "so that's how you want it..."
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

    const string WaypointId = "b1_fiery_twin";

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
            WorldDialogueUI.Begin("conv_b1_offer");
        }
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

            case Phase.AwaitRelease:
                if (Flag("b1_released") && !WorldDialogueUI.IsOpen) Release();
                break;

            case Phase.TicketChoice:
                if (WorldDialogueUI.IsOpen) break;
                if (Flag("b1_pay"))
                {
                    var wallet = PlayerWallet.Instance;
                    if (wallet != null && wallet.SpendMoney(fineAmount)) Release();
                    else StartChase();   // can't pay = same as running
                }
                else if (Flag("b1_run")) StartChase();
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
                      _phase == Phase.Verdict || _phase == Phase.AwaitRelease ||
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
        SetFlag("b1_interrogation_done", false);
        SetFlag("b1_pay", false);
        SetFlag("b1_run", false);
        SetFlag("b1_released", false);

        // 1. The corvette drops in behind and shadows the ship — sirens, lights.
        //    The player still has full control; nothing is forcing them yet.
        if (copShipPrefab != null)
        {
            var go = Instantiate(copShipPrefab);
            _cop = go.AddComponent<CopShipController>();
            _cop.Init(_ship, _anchorBody, sirenClip, copCalloutClips);
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

        // 3. Controls cut; forced smooth deceleration down to a stop relative
        //    to the anchor planet (see FixedUpdate). Rate is sized so however
        //    fast they were going, the slowdown takes ~pullOverSlowSeconds.
        if (_ship != null) _ship.canFly = false;
        var rb = _ship != null ? _ship.Rigidbody : null;
        float relSpeed = rb != null && _anchorBody != null
            ? (rb.velocity - _anchorBody.velocity).magnitude : 0f;
        _decelRate = Mathf.Max(8f, relSpeed / Mathf.Max(0.5f, pullOverSlowSeconds));
        _decelActive = true;

        // 4. As the ship is rolling to a complete stop (last ~15% of the
        //    slowdown), the corvette overtakes and parks in front — its
        //    fly-in finishes right about when the ship does.
        if (rb != null && _anchorBody != null)
        {
            float stopBy = Time.time + pullOverSlowSeconds + 10f;   // safety timeout
            float trigger = Mathf.Max(4f, relSpeed * 0.15f);
            while (Time.time < stopBy &&
                   (rb.velocity - _anchorBody.velocity).magnitude > trigger)
                yield return null;
        }

        if (_cop != null)
        {
            _cop.PullInFront(() =>
            {
                WorldDialogueUI.Begin("conv_b1_stop");
                _phase = Phase.Interrogation;
            });
        }
        else
        {
            Debug.LogWarning("[TevSmuggling] copShipPrefab not assigned — skipping straight to interrogation.");
            WorldDialogueUI.Begin("conv_b1_stop");
            _phase = Phase.Interrogation;
        }
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

        if (passes >= 4)
        {
            WorldDialogueUI.Begin("conv_b1_free");
            _phase = Phase.AwaitRelease;
        }
        else
        {
            WorldDialogueUI.Begin("conv_b1_ticket");
            _phase = Phase.TicketChoice;
        }
        _busy = false;
    }

    void Release()
    {
        if (_ship != null) _ship.canFly = true;
        _decelActive = false;
        _anchorBody = null;
        if (_cop != null) _cop.FlyAway();
        _phase = Phase.Delivering;
    }

    void StartChase()
    {
        if (_ship != null) _ship.canFly = true;
        _decelActive = false;
        _anchorBody = null;
        _phase = Phase.Chase;

        if (_cop == null) { _phase = Phase.Delivering; return; }

        // This chase is scripted: you can't out-RANGE the corvette and it never
        // runs dry — it ends when Tev's rocket connects (or you eat 3 hits).
        _cop.escapeDistance = 999999f;
        _cop.maxBlasts = 999;

        _cop.StartChase(
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
            onFleeDetected: () => StartCoroutine(TevRocketRoutine()),
            pursuitClip: copPursuitClip);

        // Tev calls out every incoming shot — but never over his scripted
        // lines or the hatch countdown.
        _cop.onBlastFired = () =>
        {
            if (_subtitleCo != null || _countdownActive) return;
            ShowTevLine(BlastWarnings[UnityEngine.Random.Range(0, BlastWarnings.Length)]);
        };
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
        speaker.text = "TEV";

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

    void ShowTevLine(string line)
    {
        if (_subtitleCo != null) StopCoroutine(_subtitleCo);
        _subtitleCo = StartCoroutine(TevLineRoutine(line));
    }

    IEnumerator TevLineRoutine(string line)
    {
        EnsureSubtitleUI();
        _subtitlePanel.gameObject.SetActive(true);
        float start = Time.time;
        StartBabble();
        yield return DialogueTextStyling.RevealCharsTMP(_subtitle, line, SubtitleCharDelay, () => false);
        // Short barks ("INCOMING!") reveal in a blink — hold the babble a
        // moment so the alien voice actually registers.
        while (Time.time - start < 1.0f) yield return null;
        StopBabble();
        yield return new WaitForSeconds(2.4f);
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

        var hGo = new GameObject("H");
        hGo.transform.SetParent(root, false);
        var hText = hGo.AddComponent<TextMeshProUGUI>();
        hText.rectTransform.sizeDelta = new Vector2(96f, 96f);
        hText.text = "H";
        hText.fontSize = 56f;
        hText.fontStyle = FontStyles.Bold;
        hText.color = Color.white;
        hText.alignment = TextAlignmentOptions.Center;

        _qteRoot.SetActive(false);
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
        ShowTevLine("THAT'S THE SPIRIT! But what's your plan?! You can't outrun this ship!");

        yield return WaitUntilChaseTime(t0, 8f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("Wait, wait, wait... I've got an idea! Keep driving! Don't you DARE slow down!");

        yield return WaitUntilChaseTime(t0, 17f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("Just give me twenty seconds! I know what to do!");

        yield return WaitUntilChaseTime(t0, 27f);
        if (_phase != Phase.Chase) yield break;
        ShowTevLine("STUPID ROCKET LAUNCHER! WHERE did I put the EMERGENCY ROCKETS?!");

        yield return WaitUntilChaseTime(t0, chaseSeconds - 5f);
        if (_phase != Phase.Chase) yield break;

        // CEASE FIRE: no more taser shots from here on, and let anything
        // already in flight resolve — the player needs a calm moment to
        // actually read Tev (he doesn't speak English, the subtitle IS the
        // information).
        if (_cop != null) _cop.HoldFire(true);
        while (_phase == Phase.Chase && CopEnergyBlast.ActiveCount > 0) yield return null;
        if (_phase != Phase.Chase) yield break;

        ShowTevLine("HOLD HER STEADY! I'VE GOT A SHOT!");
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
                ShowTevLine("NOT SO FAST!");
                yield return new WaitForSeconds(2.2f);
                continue;
            }

            // Timeout: the corvette makes them pay with one shot, then Tev
            // resets once the sky is clear again.
            ShowTevLine("TOO SLOW!");
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
        ShowTevLine("BOMBS AWAYYYYY!");
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
        StartBabble();
        yield return DialogueTextStyling.RevealCharsTMP(_subtitle, "OPEN THE HATCH!", SubtitleCharDelay, () => false);
        StopBabble();
        _subtitle.maxVisibleCharacters = int.MaxValue;
        yield return new WaitForSeconds(0.6f);
        if (_phase != Phase.Chase) yield break;

        EnsureQteUI();
        _qteRoot.SetActive(true);
        _qteWhiteRing.localScale = Vector3.one * 3f;

        const string baseTxt = "OPEN THE HATCH!";
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
        ShowTevLine("HAHAHA! DIRECT HIT! That's why you always pack emergency rockets!");
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
        if (CompassHUD.Instance != null) CompassHUD.Instance.RemoveWaypoint(WaypointId);
        if (PlayerWallet.Instance != null) PlayerWallet.Instance.AddMoney(payoutAmount);
        Debug.Log("[TevSmuggling] Delivered to Fiery Twin — mission complete (barebones end).");
    }

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
