using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Survival oxygen system. Two seconds-of-air pools: the suit (keeps the player
/// alive) and the ship hull (a buffer that depletes FIRST while the player is
/// inside, because inside-with-hull-air counts as breathing). Air comes from
/// breathable zones — the lower half of Humble Abode's atmosphere and all of
/// Cyclops. An OPEN hatch exchanges hull air with the outside: it tops the hull
/// up in a refill zone, bleeds it out above the midpoint / in space (faster the
/// higher you are). A CLOSED hatch seals the hull indefinitely. A standing
/// (non-piloting) player with the hatch open in flight is dragged toward the
/// hatch and ejected onto suit-only air.
///
/// Auto-singleton with MainMenu skip — ALSO seeded in
/// MainMenuController.EnsureGameplaySingletons (trap #1 in CLAUDE.md), or it
/// never auto-creates in builds. All world reads use CelestialBody gameplay
/// accessors via NBodySimulation.Bodies; the forbidden atmosphere/shader code
/// is never touched.
/// </summary>
public class OxygenManager : MonoBehaviour
{
    public static OxygenManager Instance { get; private set; }

    public enum HullState { Sealed, Refilling, Draining }

    // ── Pools (seconds-of-air) ───────────────────────────────────────────
    float suitO2;
    float hullO2;
    HullState hullState = HullState.Sealed;
    bool cyclopsCheckpointReached;

    // ── Public accessors (HUD + save) ────────────────────────────────────
    public float SuitO2 => suitO2;
    public float HullO2 => hullO2;
    public float SuitPercent => suitMax > 0f ? Mathf.Clamp01(suitO2 / suitMax) : 0f;
    public float HullPercent => hullMax > 0f ? Mathf.Clamp01(hullO2 / hullMax) : 0f;
    public HullState State => hullState;
    public bool PlayerOnFoot { get; private set; }
    public bool PlayerPiloting { get; private set; }
    public bool PlayerInsideShip { get; private set; }
    public bool CyclopsCheckpointReached => cyclopsCheckpointReached;

    // ── Runtime caches ───────────────────────────────────────────────────
    PlayerController player;
    Ship mainShip;
    float ajarTimer;
    bool suitDepletedHandled;
    float playerRefindTimer;

    const string VO_REOXY = "Re-oxygenating the hull";
    const string VO_AJAR  = "Hull is ajar";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("OxygenManager");
        DontDestroyOnLoad(go);
        go.AddComponent<OxygenManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        suitO2 = suitMax;
        hullO2 = hullMax;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Save hooks ───────────────────────────────────────────────────────
    public void ApplyState(float suit, float hull, bool cyclopsReached)
    {
        suitO2 = Mathf.Clamp(suit, 0f, suitMax);
        hullO2 = Mathf.Clamp(hull, 0f, hullMax);
        cyclopsCheckpointReached = cyclopsReached;
        suitDepletedHandled = false;
    }

    public void ResetForNewGame() => ApplyState(suitMax, hullMax, false);

    float Midpoint => atmosphereTopAltitude * 0.5f;

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;

        EnsureRefs();
        if (player == null) return;

        // Off the solar-system scene (backrooms / poolrooms interiors) there are
        // no celestial bodies — treat as fully breathable so the player never
        // suffocates indoors. Suit tops up, hull holds sealed, no suction.
        var bodies = NBodySimulation.Bodies;
        if (bodies == null || bodies.Length == 0)
        {
            suitO2 = Mathf.Min(suitMax, suitO2 + suitRefillRate * dt);
            hullState = HullState.Sealed;
            suitDepletedHandled = false;
            SetFootState(inside: false, piloting: false, onFoot: true);
            return;
        }

        // ── Resolve the active ship + pilot/inside state ─────────────────
        Ship piloted = Ship.PilotedInstance;
        bool piloting = Ship.AnyShipPiloted && piloted != null;
        Ship ship = piloting ? piloted : mainShip;

        Vector3 playerPos = player.Rigidbody.position;
        Vector3 shipPos = ship != null ? ship.Rigidbody.position : playerPos;

        // While piloting the player GameObject is disabled (its rb.position goes
        // stale), so piloting alone counts as "inside". Otherwise a distance
        // check against the cockpit view-point decides it.
        bool insideVolume = ship != null &&
            Vector3.Distance(playerPos, InteriorAnchor(ship)) <= interiorRadius;
        bool insideShip = piloting || insideVolume;
        bool onFoot = !insideShip;
        bool hatchOpen = ship != null && ship.HatchOpen;

        // ── Altitudes + zones ────────────────────────────────────────────
        CelestialBody shipBody = NearestBody(shipPos);
        CelestialBody playerBody = NearestBody(playerPos);
        float shipAlt = Altitude(shipPos, shipBody);
        float playerAlt = Altitude(playerPos, playerBody);

        bool shipInRefill = InRefillZone(shipBody, shipAlt);
        bool playerInRefill = InRefillZone(playerBody, playerAlt);

        // Altitude factor for hull-drain + suction: 0 at the midpoint, 1 at the
        // atmosphere top, and 1 anywhere that isn't Humble Abode (vacuum).
        float altT;
        if (shipBody != null && shipBody.bodyName == humbleAbodeName)
            altT = Mathf.Clamp01((shipAlt - Midpoint) / Mathf.Max(0.0001f, atmosphereTopAltitude - Midpoint));
        else
            altT = 1f;

        // ── 1) Hull oxygen (only changes with the hatch OPEN) ────────────
        HullState prev = hullState;
        if (ship != null && hatchOpen && shipInRefill)
        {
            hullState = HullState.Refilling;
            hullO2 = Mathf.Min(hullMax, hullO2 + hullRefillRate * dt);
        }
        else if (ship != null && hatchOpen && !shipInRefill)
        {
            hullState = HullState.Draining;
            float rate = Mathf.Lerp(hullDrainMin, hullDrainMax, altT);
            hullO2 = Mathf.Max(0f, hullO2 - rate * dt);
        }
        else
        {
            hullState = HullState.Sealed; // holds its air; never depletes
        }

        // Edge-triggered VO on hull-state ENTRY (never per-frame).
        if (hullState == HullState.Refilling && prev != HullState.Refilling)
            PlayVO(VO_REOXY);
        if (hullState == HullState.Draining && prev != HullState.Draining)
        {
            PlayVO(VO_AJAR);
            ajarTimer = hullAjarRepeat;
        }
        if (hullState == HullState.Draining)
        {
            ajarTimer -= dt;
            if (ajarTimer <= 0f) { PlayVO(VO_AJAR); ajarTimer = hullAjarRepeat; }
        }

        // ── 2) Breathing → 3) Suit oxygen ────────────────────────────────
        // Breathing if standing in breathable air OR inside a hull with air.
        // This single line yields "hull drains before the suit".
        bool breathing = playerInRefill || (insideShip && hullO2 > 0f);
        if (breathing)
        {
            suitO2 = Mathf.Min(suitMax, suitO2 + suitRefillRate * dt); // refill (sanctuary)
            suitDepletedHandled = false;
        }
        else
        {
            suitO2 = Mathf.Max(0f, suitO2 - suitDrainRate * dt);
            if (suitO2 <= 0f && !suitDepletedHandled)
            {
                suitDepletedHandled = true;
                KillPlayer();
            }
        }

        // ── 4) Hatch suction — eject a standing player out an open hatch ──
        bool suction = insideShip && !piloting && hatchOpen && ship != null
                       && !shipInRefill && hullO2 > 0f;
        if (suction && player.gameObject.activeInHierarchy)
        {
            Vector3 dir = HatchPoint(ship) - player.Rigidbody.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                float mag = Mathf.Lerp(suctionForceMin, suctionForceMax, altT); // MIN > 0
                player.Rigidbody.AddForce(dir.normalized * mag, ForceMode.Acceleration);
            }
        }

        // ── Cyclops checkpoint (autosave once on first breathable arrival) ─
        if (!cyclopsCheckpointReached && playerBody != null
            && playerBody.bodyName == cyclopsName && playerInRefill)
        {
            cyclopsCheckpointReached = true;
            if (AutosaveManager.Instance != null) AutosaveManager.Instance.Autosave();
        }

        SetFootState(insideShip, piloting, onFoot);
        if (ship != null) mainShip = ship; // keep the last-known ship after pilot exit
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    void EnsureRefs()
    {
        // Cache once; lazy-refind only when null, throttled (never hammer
        // FindObjectOfType every frame — CLAUDE.md convention).
        if (player == null)
        {
            playerRefindTimer -= Time.fixedDeltaTime;
            if (playerRefindTimer <= 0f)
            {
                player = FindObjectOfType<PlayerController>();
                playerRefindTimer = 0.5f;
            }
        }
        if (mainShip == null) mainShip = FindObjectOfType<Ship>();
    }

    bool InRefillZone(CelestialBody body, float altitude)
    {
        if (body == null) return false;
        if (body.bodyName == humbleAbodeName) return altitude <= Midpoint;
        if (body.bodyName == cyclopsName)     return altitude <= cyclopsBreathableCeiling;
        return false;
    }

    static CelestialBody NearestBody(Vector3 pos)
    {
        var bodies = NBodySimulation.Bodies;
        CelestialBody nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            float d = (b.Position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = b; }
        }
        return nearest;
    }

    static float Altitude(Vector3 pos, CelestialBody body)
    {
        if (body == null) return float.MaxValue;
        return (pos - body.Position).magnitude - body.radius;
    }

    Vector3 InteriorAnchor(Ship ship)
        => ship.camViewPoint != null ? ship.camViewPoint.position : ship.transform.position;

    Vector3 HatchPoint(Ship ship)
        => ship.hatch != null ? ship.hatch.position : InteriorAnchor(ship);

    void KillPlayer()
    {
        // Overkill damage drives the existing death pipeline (cutscene → reload
        // newest save). playHurtClip:false — suffocation isn't an impact "ow".
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(200f, false);
    }

    void PlayVO(string line)
    {
        // HALLineHUD.Show shows the strip AND plays the canned clip via
        // HALVoicePlayer.TryPlay (manifest lookup added in a later task).
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
        else if (HALVoicePlayer.Instance != null) HALVoicePlayer.Instance.TryPlay(line);
    }

    void SetFootState(bool inside, bool piloting, bool onFoot)
    {
        PlayerInsideShip = inside;
        PlayerPiloting = piloting;
        PlayerOnFoot = onFoot;
    }

    // ── Tunables (APPEND-ONLY at class end; spec defaults) ────────────────
    [Header("Pool capacities (seconds of air)")]
    [SerializeField] float suitMax = 120f;
    [SerializeField] float hullMax = 300f;

    [Header("Rates (seconds-of-air per real second)")]
    [SerializeField] float suitDrainRate  = 1.0f;
    [SerializeField] float suitRefillRate = 24.0f;
    [SerializeField] float hullRefillRate = 60.0f;
    [SerializeField] float hullDrainMin   = 5.0f;
    [SerializeField] float hullDrainMax   = 60.0f;

    [Header("Atmosphere (metres above surface)")]
    [Tooltip("Height above Humble Abode's surface where the atmosphere ends. The lower half (<= half this) is breathable. Tune per level.")]
    [SerializeField] float atmosphereTopAltitude = 600f;
    [Tooltip("Altitude (m) under which Cyclops counts as breathable everywhere. Generous by design.")]
    [SerializeField] float cyclopsBreathableCeiling = 100000f;

    [Header("Hatch suction (always-tug: MIN > 0)")]
    [SerializeField] float suctionForceMin = 12f;
    [SerializeField] float suctionForceMax = 60f;

    [Header("Ship interior")]
    [Tooltip("Radius (m) around the ship's cockpit view-point counted as 'inside the ship'. Tune to the interior size.")]
    [SerializeField] float interiorRadius = 4f;

    [Header("VO")]
    [Tooltip("Seconds between repeats of 'Hull is ajar' while still breaching.")]
    [SerializeField] float hullAjarRepeat = 8f;

    [Header("Planet names (must match CelestialBody.bodyName)")]
    [SerializeField] string humbleAbodeName = "Humble Abode";
    [SerializeField] string cyclopsName = "Cyclops";
}
