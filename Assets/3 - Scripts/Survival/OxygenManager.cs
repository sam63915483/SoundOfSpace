using System.Collections;
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
    bool suitDepletedHandled;
    float refindTimer;
    bool _prevShipPromptsAudible;   // §5 edge-detect for purging queued ship tips

    // True while the player is within the ship's prompt radius (25 m) or piloting
    // it — drives ship-scoped HUD visibility (e.g. OxygenHUD's hull bar). §5.
    public bool ShipPromptsAudible { get; private set; }

    // Per-ship derived data, recomputed only when the active ship changes.
    Ship derivedShip;
    Bounds shipLocalBounds;       // ship-local AABB for "inside the ship"
    bool shipLocalBoundsValid;
    Transform ejectPoint;         // child the hatch suction pulls the player toward

    const string VO_REOXY = "Re-oxygenating the hull";

    // §4 sealed-hull air tracking. hullWasFilledOnGround is true once the hull
    // has been topped up in a breathable zone; it gates the "hull sealed" prompt
    // + milestone warnings and clears when the sealed reserve is fully spent.
    bool hullWasFilledOnGround;
    readonly bool[] hullMilestoneFired = new bool[4];   // 4m, 2m, 1m, 30s
    static readonly float[] HullMilestones = { 240f, 120f, 60f, 30f };
    static readonly string[] HullMilestoneMsgs =
    {
        "4 minutes of hull air remaining.",
        "2 minutes of hull air remaining.",
        "1 minute of hull air remaining.",
        "30 seconds of hull air remaining."
    };

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

        // Hatch-suction one-shot (§ play-test request). 2D; plays a ~3s burst the
        // moment the hatch first vents in vacuum (the "getting sucked out" beat).
        // Clip loads lazily from StreamingAssets.
        _suctionSource = gameObject.AddComponent<AudioSource>();
        _suctionSource.playOnAwake = false;
        _suctionSource.loop = false;
        _suctionSource.spatialBlend = 0f;
        StartCoroutine(StreamingAudio.Load("Audio/HatchSuction.wav", AudioType.WAV, c => _suctionClip = c));

        // Low-oxygen alarm — periodic beep while the suit is draining and low.
        _alarmSource = gameObject.AddComponent<AudioSource>();
        _alarmSource.playOnAwake = false;
        _alarmSource.spatialBlend = 0f;
        StartCoroutine(StreamingAudio.Load("Audio/O2Alarm.wav", AudioType.WAV, c => _alarmClip = c));
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Hatch-suction audio ───────────────────────────────────────────────
    AudioSource _suctionSource;
    AudioClip   _suctionClip;
    Coroutine   _suctionRoutine;

    // ── Low-oxygen alarm ──────────────────────────────────────────────────
    AudioSource _alarmSource;
    AudioClip   _alarmClip;
    float _nextAlarmTime;
    const float SuitAlarmThreshold = 0.25f;   // suit O2 fraction below which the alarm beeps
    const float SuitAlarmInterval  = 1.3f;

    // Play the ~3s suction burst: full volume for the first second, then fade to
    // silence over the next two. Triggered on the hatch-vents-in-vacuum edge.
    void PlaySuctionBurst()
    {
        if (_suctionSource == null || _suctionClip == null) return;
        if (_suctionRoutine != null) StopCoroutine(_suctionRoutine);
        _suctionRoutine = StartCoroutine(SuctionBurst());
    }

    IEnumerator SuctionBurst()
    {
        _suctionSource.clip = _suctionClip;
        _suctionSource.volume = suctionVolume;
        _suctionSource.Play();
        yield return new WaitForSecondsRealtime(1f);          // full for the first second
        const float fade = 2f;
        float t = 0f;
        while (t < fade)
        {
            t += Time.unscaledDeltaTime;
            _suctionSource.volume = suctionVolume * (1f - t / fade);
            yield return null;
        }
        _suctionSource.Stop();
        _suctionSource.volume = suctionVolume;
        _suctionRoutine = null;
    }

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
        // stale), so piloting alone counts as "inside". Otherwise test the player
        // against the ship's actual bounding box — a small sphere around the
        // cockpit was wrong for a ~20m-long ship (it cut suction off at mid-ship
        // and wouldn't let you breathe hull air standing at the back).
        EnsureShipDerived(ship);
        bool insideVolume = ship != null && shipLocalBoundsValid && derivedShip == ship
            && shipLocalBounds.Contains(ship.transform.InverseTransformPoint(playerPos));
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

        // §5 proximity gate: ship-specific hull prompts only play when the player
        // is near or piloting THIS ship — otherwise they'd nag from across the
        // map (and cross-talk once multiple ships exist).
        bool shipPromptsAudible = ship != null && ship.PlayerIsNearOrPiloting();
        ShipPromptsAudible = shipPromptsAudible;

        // When the player LEAVES the ship's 25 m radius (and isn't piloting),
        // purge any queued ship-specific tips — they're about a ship the player
        // can no longer see, so they shouldn't pop up later.
        if (_prevShipPromptsAudible && !shipPromptsAudible && HALLineHUD.Instance != null)
            HALLineHUD.Instance.ClearShipScoped();
        _prevShipPromptsAudible = shipPromptsAudible;

        // Re-oxygenating edge — keep its VO. Also arms the §4 sealed-air tracking
        // + re-arms the milestone warnings (entering Refilling = hatch open in a
        // breathable zone, so this air WAS filled on the ground).
        if (hullState == HullState.Refilling && prev != HullState.Refilling)
        {
            if (shipPromptsAudible) PlayVO(VO_REOXY);
            hullWasFilledOnGround = true;
            for (int i = 0; i < hullMilestoneFired.Length; i++) hullMilestoneFired[i] = false;
        }

        // §4 sealed-hull reserve: once the hatch is sealed, that's all the air the
        // player has. It depletes whenever they're inside breathing it, so the
        // countdown is LIVE from the moment of sealing — regardless of standing in
        // breathable atmosphere (a sealed cabin is cut off from the air outside).
        // Caps the reserve at ~hullMax seconds.
        if (hullState == HullState.Sealed && insideShip && hullO2 > 0f)
            hullO2 = Mathf.Max(0f, hullO2 - hullBreathConsumeRate * dt);

        // Sealed air fully spent → disarm tracking (a future fill re-arms it).
        if (hullO2 <= 0f) hullWasFilledOnGround = false;

        // §4 "Hull sealed — m s of air remaining" LIVE countdown, fired on the
        // hatch-close (→ Sealed) edge when the hull holds ground-filled air.
        if (hullState == HullState.Sealed && prev != HullState.Sealed
            && hullWasFilledOnGround && shipPromptsAudible && HALLineHUD.Instance != null)
        {
            HALLineHUD.Instance.ShowLive(HullSealedCountdownText, voiceKey: null, shipScoped: true);
        }

        // §4 milestone warnings — fire once each as the sealed reserve drains past
        // 4m / 2m / 1m / 30s. ONLY while the hatch is CLOSED (Sealed): if the hatch
        // is open and venting (Draining), the hull dumps in seconds and all four
        // would queue up at once — that case shows the vacuum warning instead.
        if (hullWasFilledOnGround && hullState == HullState.Sealed && shipPromptsAudible)
        {
            for (int i = 0; i < HullMilestones.Length; i++)
            {
                if (!hullMilestoneFired[i] && hullO2 <= HullMilestones[i])
                {
                    hullMilestoneFired[i] = true;
                    PlayVO(HullMilestoneMsgs[i]);
                }
            }
        }

        // §6 vacuum exposure: when the hatch OPENS into vacuum (Draining — above
        // the midpoint / off-world / above the Cyclops ceiling), show "Hull exposed
        // to the vacuum of space." ONCE per exposure (no repeat). It re-arms when
        // the exposure clears (hatch closed / back in atmosphere), so the next
        // hatch-open announces again. The suction burst plays only when the cabin
        // still has pressurized air to vent (hullO2 > 0) — opening an already-empty
        // hull makes no whoosh.
        bool inVacuumExposure = hullState == HullState.Draining;
        if (inVacuumExposure && prev != HullState.Draining)
        {
            if (shipPromptsAudible && HALLineHUD.Instance != null)
                HALLineHUD.Instance.Show("Hull exposed to the vacuum of space.", shipScoped: true);
            if (shipPromptsAudible && hullO2 > 0f) PlaySuctionBurst();   // air rushing out
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
            // Low-oxygen alarm: periodic beep while the suit is draining and low.
            if (SuitPercent < SuitAlarmThreshold && _alarmSource != null && _alarmClip != null
                && Time.unscaledTime >= _nextAlarmTime)
            {
                _alarmSource.PlayOneShot(_alarmClip, 0.7f);
                _nextAlarmTime = Time.unscaledTime + SuitAlarmInterval;
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
        // Cache once; lazy-refind only when null, THROTTLED. Critical: the ship
        // doesn't exist until the player BUYS one, so FindObjectOfType<Ship>()
        // returns null for the whole early game — left per-frame it becomes the
        // interior-perf trap (per-frame FindObjectOfType on an absent target).
        // Throttle both lookups to ~2/sec instead of 50/sec (FixedUpdate rate).
        if (player != null && mainShip != null) return;

        refindTimer -= Time.fixedDeltaTime;
        if (refindTimer > 0f) return;
        refindTimer = 0.5f;

        if (player == null) player = FindObjectOfType<PlayerController>();
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

    // Where the hatch suction pulls the player: the dedicated HatchEjectPoint
    // child (out the back of the hull) if present, else the hatch transform.
    Vector3 HatchPoint(Ship ship)
        => ejectPoint != null ? ejectPoint.position
           : (ship.hatch != null ? ship.hatch.position : InteriorAnchor(ship));

    // Recompute per-ship derived data when the active ship changes: the
    // ship-local AABB (rotation-invariant, built from mesh bounds) used for the
    // "inside the ship" test, and the suction eject point child.
    void EnsureShipDerived(Ship ship)
    {
        if (ship == null || derivedShip == ship) return;
        derivedShip = ship;
        shipLocalBoundsValid = false;
        ejectPoint = FindDeepChild(ship.transform, ejectPointName);

        var filters = ship.GetComponentsInChildren<MeshFilter>();
        var w2l = ship.transform.worldToLocalMatrix;
        bool has = false;
        Bounds b = new Bounds();
        for (int fi = 0; fi < filters.Length; fi++)
        {
            var mf = filters[fi];
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;          // mesh-local
            var l2w = mf.transform.localToWorldMatrix;
            Vector3 c = mb.center, e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 localP = w2l.MultiplyPoint3x4(l2w.MultiplyPoint3x4(corner));
                if (!has) { b = new Bounds(localP, Vector3.zero); has = true; }
                else b.Encapsulate(localP);
            }
        }
        if (has)
        {
            b.Expand(interiorMargin * 2f);   // small slack so the boundary sits just outside the hull
            shipLocalBounds = b;
            shipLocalBoundsValid = true;
        }
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name == name) return c;
            var found = FindDeepChild(c, name);
            if (found != null) return found;
        }
        return null;
    }

    void KillPlayer()
    {
        // Overkill damage drives the existing death pipeline (cutscene → reload
        // newest save). playHurtClip:false — suffocation isn't an impact "ow".
        var rm = ResourceManager.Instance;
        if (rm != null) rm.TakeDamage(200f, false);
    }

    // Live text for the §4 "hull sealed" prompt — re-evaluated every frame by
    // HALLineHUD.ShowLive so the countdown ticks in real time.
    string HullSealedCountdownText()
    {
        int t = Mathf.Max(0, Mathf.CeilToInt(hullO2));
        int m = t / 60, s = t % 60;
        return $"Hull sealed — {m} minute{(m == 1 ? "" : "s")} {s} second{(s == 1 ? "" : "s")} of air remaining.";
    }

    void PlayVO(string line)
    {
        // Ship-scoped hull VO — shows the strip AND plays the canned clip via
        // HALVoicePlayer.TryPlay. shipScoped so it's purged if the player leaves
        // the ship's radius before it surfaces.
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line, shipScoped: true);
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
    [Tooltip("How fast SEALED hull air is breathed down while the player is inside in vacuum (§4). hullMax / this = seconds of sealed reserve, e.g. 300/1 = 5 min.")]
    [SerializeField] float hullBreathConsumeRate = 1.0f;

    [Header("Atmosphere (metres above surface)")]
    // Humble Abode radius = 200, atmosphereScale ~0.32-0.49 → visible atmosphere
    // ends ~65-98 m up. Top = 120 m (midpoint 60 m breathable) tracks the haze so
    // the refill cutoff lines up with what the player sees climbing out of it.
    [Tooltip("Height above Humble Abode's surface where the atmosphere ends. The lower half (<= half this) is breathable. Tune per level.")]
    [SerializeField] float atmosphereTopAltitude = 120f;
    // Cyclops radius = 500, atmosphereScale ~0.59 → atmosphere ~294 m. 600 m keeps
    // the whole Cyclops surface + atmosphere breathable (it's a checkpoint planet)
    // without making deep space near it a free breathing zone.
    [Tooltip("Altitude (m) under which Cyclops counts as breathable everywhere. Covers the surface zone, not orbit.")]
    [SerializeField] float cyclopsBreathableCeiling = 600f;

    [Header("Hatch suction (always-tug: MIN > 0)")]
    [SerializeField] float suctionForceMin = 12f;
    [SerializeField] float suctionForceMax = 60f;

    [Header("Ship interior")]
    [Tooltip("Extra metres added around the ship's bounding box when deciding 'inside the ship' (small slack so the boundary sits just outside the hull).")]
    [SerializeField] float interiorMargin = 0.5f;
    [Tooltip("Name of the child transform the hatch suction pulls the player toward (out the back). Move that GameObject in the SHIP44 prefab to tune the eject point.")]
    [SerializeField] string ejectPointName = "HatchEjectPoint";

    [Header("Hatch suction audio")]
    [Tooltip("Volume of the looping windy-suction roar while the hatch is open in vacuum.")]
    [Range(0f, 1f)]
    [SerializeField] float suctionVolume = 0.8f;

    [Header("Planet names (must match CelestialBody.bodyName)")]
    [SerializeField] string humbleAbodeName = "Humble Abode";
    [SerializeField] string cyclopsName = "Cyclops";
}
