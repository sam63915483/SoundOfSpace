using System.Collections;
using System.Collections.Generic;
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

    const string WaypointId = "b1_fiery_twin";

    Phase _phase = Phase.Idle;
    Ship _ship;
    CelestialBody _homeBody, _destBody, _anchorBody;
    CopShipController _cop;
    bool _scared;
    bool _busy;                              // a coroutine owns the next transition
    float _nextPoll;

    void Awake()
    {
        _ship = GetComponent<Ship>();
    }

    void Start()
    {
        // Scene-placed second ship: EndlessManager.Bootstrap only registers the
        // FIRST Ship it finds, so Tevsship must self-register or it pops on
        // every floating-origin shift.
        var endless = FindObjectOfType<EndlessManager>();
        if (endless != null) endless.RegisterPhysicsObject(transform);

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

        Vector3 lookPoint = tevNpc != null ? tevNpc.position + tevNpc.up * 0.5f : transform.position;
        if (scareClip != null) AudioSource.PlayClipAtPoint(scareClip, lookPoint, 1f);

        // Speed chosen so the full spin takes ~scareTurnSeconds regardless of angle.
        float angle = Vector3.Angle(pc.transform.forward, lookPoint - pc.transform.position);
        float degPerSec = Mathf.Max(90f, angle / Mathf.Max(0.05f, scareTurnSeconds));

        float elapsed = 0f;
        while (elapsed < scareTurnSeconds + 0.6f)
        {
            if (pc == null) break;
            if (pc.ForceLookAtSmooth(lookPoint, degPerSec)) break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        TutorialGate.ApplyState(gateWasEnabled, unlockedSnapshot);

        yield return new WaitForSeconds(0.25f);
        WorldDialogueUI.Begin("conv_b1_offer");
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
        // Hold the traffic stop still relative to the nearest planet: planets keep
        // orbiting normally, the ship just rides along with the closest one.
        bool frozen = _phase == Phase.PullOver || _phase == Phase.Interrogation ||
                      _phase == Phase.Verdict || _phase == Phase.AwaitRelease ||
                      _phase == Phase.TicketChoice;
        if (!frozen || _anchorBody == null || _ship == null) return;

        var rb = _ship.Rigidbody;
        if (rb == null) return;
        rb.velocity = _anchorBody.velocity;
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, 0.15f);
    }

    // ── Phase transitions ──

    void BeginEnRoute()
    {
        _phase = Phase.EnRoute;
        _homeBody = FindBody(homeBodyName);
        _destBody = FindBody(destBodyName);
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
        if (dHome >= dDest) BeginPullOver();
    }

    void BeginPullOver()
    {
        _phase = Phase.PullOver;
        _anchorBody = FindNearestBody();
        if (_ship != null) _ship.canFly = false;

        // Per-stop flags must start clean or a retry inherits last attempt's answers.
        SetFlag("b1_q1_pass", false);
        SetFlag("b1_q2_pass", false);
        SetFlag("b1_q3_pass", false);
        SetFlag("b1_q4_pass", false);
        SetFlag("b1_interrogation_done", false);
        SetFlag("b1_pay", false);
        SetFlag("b1_run", false);
        SetFlag("b1_released", false);

        if (copShipPrefab != null)
        {
            var go = Instantiate(copShipPrefab);
            _cop = go.AddComponent<CopShipController>();
            _cop.Init(_ship, _anchorBody, sirenClip, onArrived: () =>
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
        _anchorBody = null;
        if (_cop != null) _cop.FlyAway();
        _phase = Phase.Delivering;
    }

    void StartChase()
    {
        if (_ship != null) _ship.canFly = true;
        _anchorBody = null;
        _phase = Phase.Chase;

        if (_cop == null) { _phase = Phase.Delivering; return; }
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
            });
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
