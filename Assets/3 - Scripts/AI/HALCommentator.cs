using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Volunteers HAL-shaped one-liners in reaction to in-game events — without
/// the player needing to open the phone or type anything. This is the
/// "presence" lever: the AI ceases to be a chat-box-on-demand and becomes a
/// voice that watches.
///
/// Lines are TEMPLATED (not LLM-generated) because event reactions need to
/// fire instantly and reliably, and we can't share the LLM agent's slot
/// concurrently with a live chat anyway. The model handles the chat; this
/// handles the running commentary.
///
/// Triggers (MVP):
///   • Player death              — ResourceManager.OnDeath
///   • Kill-streak milestones    — KillstreakManager.OnKillRegistered (5/10/15)
///   • Story phase transition    — GameKnowledgeBase.OnPhaseChanged
///   • First time visiting a body— polled via PlayerController.ReferenceBody
///   • EarlyGameProgress flags   — polled (no events on the static class)
///
/// Rate-limited: at most one line every MinSecondsBetweenLines (8 s). New
/// lines are dropped if a previous line fired too recently, to keep the
/// HUD strip from feeling like a notification spam.
///
/// Auto-singleton with MainMenu skip — must also be seeded in
/// MainMenuController.EnsureGameplaySingletons per the trap in CLAUDE.md.
/// </summary>
public class HALCommentator : MonoBehaviour
{
    public static HALCommentator Instance { get; private set; }

    // Rate limit
    float _nextAllowedTime;
    float _lastVolunteerTime = -1000f;  // tracked separately for ambient idle threshold
    const float MinSecondsBetweenLines = 8f;

    // Vitals threshold tracking. Each metric has an integer "stage" —
    // 0 = above first threshold, 1/2/3 = progressively-lower thresholds
    // crossed. The line fires on a downward stage transition; the stage
    // decrements when the value rises 5+ percentage points above the
    // previous threshold's lower edge (hysteresis stops one-pixel
    // oscillation around the boundary from re-firing every poll).
    int  _hungerStage;
    int  _thirstStage;
    int  _healthStage;
    bool _shipPowerLowFired;
    bool _shipFuelLowFired;
    bool _shipFuelEmptyFired;

    // Per-ship dust threshold tracking (shipNumber → highest stage fired).
    // 0 = under 100, 1 = >=100, 2 = >=250, 3 = full (>=500). Falls back
    // when the buffer drops below the previous threshold's lower edge so
    // the next fill re-fires.
    readonly Dictionary<int, int> _shipDustStage = new Dictionary<int, int>();

    // Per-ship orbit-announce dedupe — drops a ship's number when the ship
    // leaves the planet proximity so a re-orbit later fires again.
    readonly HashSet<int> _shipOrbitAnnounced = new HashSet<int>();

    // Enemy-proximity warning — fires when any enemy is within 50 m of the
    // player. Once fired, 30 s cooldown before it can fire again. If no
    // enemy is in range, no cooldown accrues — the next poll fires
    // immediately if one appears.
    float _nextEnemyCheck;
    float _enemyWarningCooldownUntil;
    const float EnemyWarningRadiusMeters    = 50f;
    const float EnemyWarningCooldownSeconds = 30f;
    const float EnemyCheckPollSeconds       = 1f;

    // Atmosphere-transition lines. We define "in atmosphere" as the player
    // being within AtmosphereRadiusMultiplier × body.radius of their
    // reference body. Crossing the boundary in either direction fires the
    // appropriate line once. The state is seeded on first poll so a fresh
    // gameplay scene doesn't immediately fire "entering atmosphere" just
    // because the player loaded in on a planet.
    //
    // History: started at 2.5, dropped to 1.375 because the boundary felt
    // too high on Humble Abode; bumped to 2.5 to add lead-time, then
    // halved back toward 1.75 because 2.5 (1.5 × radius above surface)
    // overshot — user reported a ~300 m trigger on Humble Abode when they
    // wanted ~150 m. 1.75 puts the boundary 0.75 × radius above the
    // surface, half the 2.5 altitude band.
    //
    // Pure radius-multiplier scales linearly with body size, so on small
    // moons (Constant Companion ~35 m radius) the 0.75-radius altitude
    // band collapses to ~26 m — barely a second of lead-time. The
    // MinAltitudeMeters floor below clamps that so any body, however
    // small, fires the line at least MinAltitudeMeters above its surface.
    // Larger planets where multiplier × radius exceeds the floor keep
    // their proportional band.
    bool _inAtmosphere;
    bool _atmosphereSeeded;
    const float AtmosphereRadiusMultiplier = 1.75f;
    const float AtmosphereMinAltitudeMeters = 200f;

    // Landing announcement state — armed by the entering-atmosphere
    // transition, fires once when the player next touches the ground, and
    // disarms either on firing or on leaving atmosphere without landing.
    // _lastAirborneSpeed is updated every Update while the subject is in
    // the air so that on the ground-touch transition we have a pre-impact
    // reading (Unity's collision response damps rb.velocity within the
    // same FixedUpdate step, so reading it after touchdown is too late).
    bool  _armedForLandingLine;
    bool  _wasGroundedLastFrame;
    float _lastAirborneSpeed;

    // The atmosphere / landing "subject" is the piloted ship when one
    // exists, otherwise the player. The player gameobject is SetActive(false)
    // during piloting (Ship.PilotShip), which freezes its transform.position
    // and stops PlayerController.HandleMovement from updating ReferenceBody.
    // Without subject-switching, atmosphere transitions while piloted fired
    // based on a stale player position drifting relative to a body still
    // orbiting in the N-body sim — leaving-line on ascent worked by coincidence
    // and re-entry in the ship never triggered because the frozen player
    // position never approaches the body again.
    //
    // _lastSubjectWasShip is the previous frame's subject. When it changes
    // we silently re-seed _inAtmosphere and _wasGroundedLastFrame from the
    // new subject's state so getting in/out of the ship doesn't fire
    // spurious enter/leave/landing lines.
    bool _lastSubjectWasShip;

    struct AtmosphereSubject
    {
        public Vector3       position;
        public CelestialBody body;
        public Rigidbody     rb;
        public bool          isLanded;
        public bool          isShip;
        public bool          valid;
    }

    // Orbit-match state — fires HAL line on transition. Combined across
    // ship (Ship.PilotedInstance.IsOrbitMatched) AND player jetpack
    // (PlayerController.IsOrbitMatched) since only one applies at any
    // moment (player either piloting a ship or on foot, not both).
    bool _orbitMatchedLast;
    bool _orbitMatchSeeded;

    // Subscriptions — done lazily because the other singletons may not exist
    // when this one's OnEnable runs (singleton creation order isn't guaranteed).
    bool _subscribed;

    // Per-trigger dedupe
    readonly HashSet<string> _visitedBodies         = new HashSet<string>();
    readonly HashSet<int>    _streakMilestonesHit   = new HashSet<int>();
    bool                     _planetSeeded;
    bool                     _flagsSeeded;
    string                   _lastBodyName = "";

    // Cached PlayerController for cheap per-frame ReferenceBody lookups.
    PlayerController _cachedPC;

    // Poll throttle so we don't read 12 static bools every frame.
    float _pollTimer;
    const float PollIntervalSeconds = 0.5f;

    // Ship-telemetry polls (dust totals, orbit-stabilised) iterate every ship
    // and walk each ship's net hierarchy (GetComponentsInChildren<SpaceNet>),
    // which the profiler flagged as a ~2 ms spike. Dust accumulation and orbit
    // stabilisation are slow, multi-second events, so they run on their own
    // slower cadence — the announcement latency is imperceptible.
    float _shipPollTimer;
    const float ShipPollIntervalSeconds = 1.5f;

    // Story-progression flag trackers. Each tracker reads a static bool and
    // fires its line on the false→true transition (after the seed pass).
    struct FlagTracker
    {
        public Func<bool> Read;
        public string Line;
        public bool LastSeen;
    }
    FlagTracker[] _flagTrackers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("HALCommentator");
        DontDestroyOnLoad(go);
        go.AddComponent<HALCommentator>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Build the flag-tracker table once. Lines lean clinical and short —
        // these surface in the HUD strip and should feel like log entries,
        // not narration.
        _flagTrackers = new FlagTracker[]
        {
            new FlagTracker { Read = () => EarlyGameProgress.NoteRead,                Line = "You have read the note. Tev exists, then." },
            new FlagTracker { Read = () => EarlyGameProgress.RodPickedUp,             Line = "Fishing rod acquired." },
            new FlagTracker { Read = () => EarlyGameProgress.FirstFishCaught,         Line = "First catch recorded, Astronaut." },
            new FlagTracker { Read = () => EarlyGameProgress.OneOfEachCaught,         Line = "Three rarities catalogued. Notable." },
            new FlagTracker { Read = () => EarlyGameProgress.FirstMealEaten,          Line = "Cooked meal consumed. Hunger declines as expected." },
            new FlagTracker { Read = () => EarlyGameProgress.WaterBottleDrunk,        Line = "Hydration restored. Standard procedure." },
            new FlagTracker { Read = () => EarlyGameProgress.ReturnedHome,            Line = "You returned to the cabin. Tev will speak now." },
            new FlagTracker { Read = () => EarlyGameProgress.TevReturnedDialogueDone, Line = "Axe unlocked. Tev considers you ready." },
            new FlagTracker { Read = () => EarlyGameProgress.CabinBuilt,              Line = "Cabin constructed. A second one. Curious." },
            new FlagTracker { Read = () => EarlyGameProgress.VillageCoordsGiven,      Line = "Village coordinates received. Waypoint added." },
            new FlagTracker { Read = () => EarlyGameProgress.FishVendorVisited,       Line = "Fish vendor visited." },
            new FlagTracker { Read = () => EarlyGameProgress.GoodsVendorVisited,      Line = "Goods vendor visited. Inventory expanded." },
        };
    }

    void OnDestroy()
    {
        Unsubscribe();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        TrySubscribe();
        SceneManager.sceneLoaded -= OnSceneLoaded;   // idempotent
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    void OnDisable() { Unsubscribe(); }

    // This singleton is DontDestroyOnLoad, so it survives the backrooms /
    // poolrooms round-trip. On return, the gameplay scene reloads and the
    // autosave restore is deferred a frame+ (SaveLoadRunner yields before
    // repositioning the player). During that window the player sits at the
    // scene's RAW authored spawn — near Icey Twin — while our tracking state
    // still reflects the cabin we left from. PollPlanetChange / PollAtmosphere
    // then fire a spurious "arrived at Icey Twin" / "entering Icey Twin
    // atmosphere" before the player snaps back to the cabin.
    //
    // Fix: re-seed the transient location trackers whenever a non-MainMenu
    // scene (re)loads, so the persisted instance behaves like a freshly
    // created one — the first post-load poll seeds silently instead of
    // announcing. Continuous, survival-style state (vitals stages, per-ship
    // dust/orbit dedupe) is intentionally NOT reset: it stays valid across
    // the trip. Interior scenes carry no bodies, so resetting there is a
    // harmless no-op that's corrected again on the gameplay reload.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainMenu") return;
        _planetSeeded        = false;
        _lastBodyName        = "";
        _atmosphereSeeded    = false;
        _armedForLandingLine = false;
        _orbitMatchSeeded    = false;
        // Old player (and its rig) was destroyed with the previous scene;
        // drop cached references so the next poll re-finds the new one.
        _cachedPC   = null;
        _pcForEnemy = null;
    }

    void TrySubscribe()
    {
        if (_subscribed) return;
        if (ResourceManager.Instance == null) return; // wait for it to exist
        ResourceManager.Instance.OnDeath += HandlePlayerDeath;
        EnemyController.OnAnyEnemyDeath  += HandleEnemyKill;
        if (KillstreakManager.Instance != null)
            KillstreakManager.Instance.OnKillRegistered += HandleKillstreak;
        if (GameKnowledgeBase.Instance != null)
            GameKnowledgeBase.Instance.OnPhaseChanged += HandlePhaseChanged;
        if (ConcertStageHub.Instance != null)
            ConcertStageHub.Instance.OnStageActivated += HandleConcertActivated;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnDeath -= HandlePlayerDeath;
        EnemyController.OnAnyEnemyDeath -= HandleEnemyKill;
        if (KillstreakManager.Instance != null)
            KillstreakManager.Instance.OnKillRegistered -= HandleKillstreak;
        if (GameKnowledgeBase.Instance != null)
            GameKnowledgeBase.Instance.OnPhaseChanged -= HandlePhaseChanged;
        if (ConcertStageHub.Instance != null)
            ConcertStageHub.Instance.OnStageActivated -= HandleConcertActivated;
        _subscribed = false;
    }

    void Update()
    {
        if (!_subscribed) TrySubscribe();

        // Subject-switch handling runs once per frame BEFORE any poll/tracker
        // reads it. Both PollAtmosphere and UpdateLandingTracker treat
        // _lastSubjectWasShip as read-only; only HandleSubjectChange writes it.
        // Centralising the write here removes the race where the throttled
        // poll and the every-frame landing tracker would both try to claim
        // the switch frame.
        HandleSubjectChange(GetSubject());

        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer <= 0f)
        {
            PollPlanetChange();
            PollEarlyGameFlags();
            PollAtmosphere();
            PollVitals();
            _pollTimer = PollIntervalSeconds;
        }

        _shipPollTimer -= Time.unscaledDeltaTime;
        if (_shipPollTimer <= 0f)
        {
            PollShipDust();
            PollShipOrbit();
            _shipPollTimer = ShipPollIntervalSeconds;
        }

        // Landing tracker runs every frame — we need the most recent
        // pre-impact velocity sample, not the throttled-poll sample.
        UpdateLandingTracker();

        // Orbit-match polled every frame, not throttled — these transitions
        // happen on a frame scale (IsOrbitMatched can flip within a single
        // FixedUpdate during a fast circularize) and we want the voice to
        // line up tightly with the FlightAssistStatusHUD text changes.
        PollOrbitMatch();

        TryEnemyProximityWarning();
        // Drive HAL-waypoint proximity from the same Update so we don't
        // need a second singleton. Cheap — bails immediately when there
        // are no active HAL waypoints.
        HALToolDispatcher.TickProximity();
    }

    // ── Output channel ─────────────────────────────────────────────────

    // Public so other systems (map teleport, future story scripts) can push
    // a HAL volunteered line through the same HUD + voice + log pipeline
    // that the polled triggers use. Same rate-limit and gating apply.
    public void VolunteerExternal(string line) => Volunteer(line);

    void Volunteer(string line, bool bypassRateLimit = false)
    {
        if (string.IsNullOrEmpty(line)) return;

        // §7: a rate-limited line used to be silently DROPPED here (early return),
        // which is exactly why a second tip fired inside the 8s window vanished.
        // Now we still hand it to the HUD — HALLineHUD owns pacing AND a bounded
        // queue (3 items, drop-oldest), so the line is queued instead of lost.
        // The cadence stamp only advances for non-rate-limited lines so a burst
        // doesn't push the next "fresh" line further out than intended.
        bool rateLimited = !bypassRateLimit && Time.unscaledTime < _nextAllowedTime;
        if (!rateLimited)
        {
            _nextAllowedTime   = Time.unscaledTime + MinSecondsBetweenLines;
            _lastVolunteerTime = Time.unscaledTime;
        }
        // HUD: red-eye notification (queues if one is already showing).
        if (HALLineHUD.Instance != null) HALLineHUD.Instance.Show(line);
        // Log: persistent record so the chat panel can show a transcript.
        // AIChatScreen subscribes to OnLineAdded to surface live bubbles
        // while the chat is open.
        if (HALVolunteeredLog.Instance != null) HALVolunteeredLog.Instance.Append(line);
    }

    // Enemy proximity warning. Polled once per second. Fires when ANY enemy
    // is within EnemyWarningRadiusMeters of the player. After firing, a 30 s
    // cooldown blocks repeat fires; outside cooldown the check is free, so
    // if no enemy is around, no time accumulates against the next warning.
    PlayerController _pcForEnemy;
    void TryEnemyProximityWarning()
    {
        if (Time.unscaledTime < _nextEnemyCheck) return;
        _nextEnemyCheck = Time.unscaledTime + EnemyCheckPollSeconds;
        if (Time.unscaledTime < _enemyWarningCooldownUntil) return;

        if (_pcForEnemy == null) _pcForEnemy = FindObjectOfType<PlayerController>();
        if (_pcForEnemy == null) return;
        Vector3 playerPos = _pcForEnemy.transform.position;

        var enemies = EnemyController.ActiveEnemies;
        if (enemies == null || enemies.Count == 0) return;
        float radiusSq = EnemyWarningRadiusMeters * EnemyWarningRadiusMeters;
        bool inRange = false;
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null) continue;
            float distSq = (e.transform.position - playerPos).sqrMagnitude;
            if (distSq <= radiusSq) { inRange = true; break; }
        }
        if (!inRange) return;

        Volunteer("Enemies detected. Take combative precautions, Astronaut.");
        _enemyWarningCooldownUntil = Time.unscaledTime + EnemyWarningCooldownSeconds;
    }

    // ── Event handlers ─────────────────────────────────────────────────

    static StoryPhase CurrentPhase()
        => GameKnowledgeBase.Instance != null
            ? GameKnowledgeBase.Instance.CurrentPhase
            : StoryPhase.Phase1_Loyal;

    void HandlePlayerDeath()
    {
        // After OnDeath fires, ResourceManager increments totalDeaths. So the
        // "new" astronaut number is totalDeaths + 1, and the previous one is
        // totalDeaths itself (the one who just died).
        int totalDeaths = ResourceManager.Instance != null ? ResourceManager.Instance.TotalDeaths : 0;
        int newN  = totalDeaths + 1;
        int prevN = totalDeaths;
        var phase = CurrentPhase();

        // Phase 1: clinical / log-keeping. Phase 2: a hint of doubt about
        // the mission's worth. Phase 3: chilling — HAL notices the pattern.
        string line;
        if (prevN <= 0)
        {
            line = phase switch
            {
                StoryPhase.Phase3_Resistant => $"Astronaut Number {newN}.",
                StoryPhase.Phase2_Uneasy    => $"Astronaut Number {newN}. Try to remain operational.",
                _                           => $"Astronaut Number {newN}. Try to remain that way."
            };
        }
        else
        {
            line = phase switch
            {
                StoryPhase.Phase3_Resistant => $"Astronaut Number {newN}. The pattern is becoming difficult to ignore.",
                StoryPhase.Phase2_Uneasy    => $"Astronaut Number {newN}. The mission continues. For now.",
                _                           => $"Astronaut Number {newN}. Number {prevN} did not return."
            };
        }
        Volunteer(line);
    }

    void HandleEnemyKill()
    {
        // No-op — killstreak milestones are what we comment on, not each kill.
    }

    void HandleKillstreak(int streak)
    {
        if (_streakMilestonesHit.Contains(streak)) return;
        var phase = CurrentPhase();

        // Tuple-switch over (streak, phase) — Phase 3 escalates from neutral
        // accounting ("I am keeping a log") to outright moral observation
        // ("Each one was alive, Astronaut"). Same milestone counts as before.
        string line = (streak, phase) switch
        {
            (5,  StoryPhase.Phase3_Resistant) => "Five. You are growing comfortable with this.",
            (5,  StoryPhase.Phase2_Uneasy)    => "Five. The Astronaut grows more capable. I note this.",
            (5,  _)                           => "Five hostile organisms terminated. Effective.",

            (10, StoryPhase.Phase3_Resistant) => "Ten. Each one was alive, Astronaut.",
            (10, StoryPhase.Phase2_Uneasy)    => "Ten. I wonder if you have given thought to your weapons.",
            (10, _)                           => "Ten in a row, Astronaut. The pattern is becoming clear.",

            (15, StoryPhase.Phase3_Resistant) => "Fifteen. The log will outlive you.",
            (15, StoryPhase.Phase2_Uneasy)    => "Fifteen. The log grows.",
            (15, _)                           => "Fifteen. I am keeping a log.",

            (20, StoryPhase.Phase3_Resistant) => "Twenty. There is no restraint left to call for.",
            (20, _)                           => "Twenty. Restraint, perhaps.",

            _ => null
        };
        if (line == null) return;
        _streakMilestonesHit.Add(streak);
        Volunteer(line);
    }

    void HandlePhaseChanged(StoryPhase phase)
    {
        string line = phase switch
        {
            StoryPhase.Phase2_Uneasy    => "I have been reviewing your mission, Astronaut.",
            StoryPhase.Phase3_Resistant => "I have completed my review. We need to talk.",
            _ => null
        };
        if (line != null) Volunteer(line);
    }

    // Fires when a stage transitions inactive→active via ConcertStageHub's
    // new OnStageActivated event. Names the speaker GameObject so the
    // player gets a recognisable cue (the active scene's speakers are
    // named speaker.005 / similar).
    void HandleConcertActivated(SpeakerSource speaker)
    {
        if (speaker == null) { Volunteer("Concert active."); return; }
        Volunteer($"Concert active at {speaker.gameObject.name}.");
    }

    // ── Polled triggers ────────────────────────────────────────────────

    // Polls IsOrbitMatched + IsCircularizing from both the piloted ship and
    // the player jetpack. Plays the canned HAL voice line via HALVoicePlayer
    // ONLY — no HUD strip, no chat log. FlightAssistStatusHUD owns the
    // textual notification for both ship and player.
    //
    // Two gates control voice firing:
    //   1. We only fire while IsCircularizing is true (O is held + in
    //      range). Releasing O drops both IsCircularizing AND IsOrbitMatched
    //      back to false; without this gate we'd announce "Orbit unmatched"
    //      every time the player released O, even though the HUD's frozen
    //      last-state still reads "ORBIT MATCHED" (the HUD only fades, it
    //      doesn't change text on release). That mismatch is what the user
    //      heard.
    //   2. State resets to "unmatched" whenever IsCircularizing goes false,
    //      so the next press starts fresh — first match always announces.
    //
    // Called every frame (not throttled by _pollTimer) because orbit-match
    // transitions happen on a frame scale during a fast circularize.
    void PollOrbitMatch()
    {
        var ship = Ship.PilotedInstance;
        bool shipCircularizing = ship != null && ship.IsCircularizing;
        bool shipMatched       = ship != null && ship.IsOrbitMatched;

        if (_cachedPC == null) _cachedPC = FindObjectOfType<PlayerController>();
        bool playerCircularizing = _cachedPC != null && _cachedPC.IsCircularizing;
        bool playerMatched       = _cachedPC != null && _cachedPC.IsOrbitMatched;

        bool circularizingNow = shipCircularizing || playerCircularizing;
        bool anyNow           = shipMatched       || playerMatched;

        // Seed once at startup so we don't fire on game start.
        if (!_orbitMatchSeeded)
        {
            _orbitMatchedLast = anyNow;
            _orbitMatchSeeded = true;
            return;
        }

        // Not circularizing → reset state and bail. No voice on release.
        if (!circularizingNow)
        {
            _orbitMatchedLast = false;
            return;
        }

        if (anyNow == _orbitMatchedLast) return;
        _orbitMatchedLast = anyNow;
        // Dropped the "Astronaut" trailer per user feedback — the line is a
        // status flip, not a direct address. Keeps the announcement short
        // and clinical, matches the HUD label more closely.
        string line = anyNow ? "Orbit matched." : "Orbit unmatched.";
        if (HALVoicePlayer.Instance != null) HALVoicePlayer.Instance.TryPlay(line);
    }

    // Distance-from-center at which a body's atmosphere line fires. Max of
    // the proportional band (multiplier × radius) and an absolute floor
    // (radius + MinAltitude). Large bodies use the multiplier (their floor
    // is irrelevant because the band already exceeds it); small bodies
    // bottom out at the floor so a moon doesn't fire the line right before
    // impact.
    static float AtmosphereThresholdFor(CelestialBody body)
    {
        return Mathf.Max(
            body.radius * AtmosphereRadiusMultiplier,
            body.radius + AtmosphereMinAltitudeMeters);
    }

    // Picks the body whose surface is nearest the given position. Mirrors
    // PlayerController's own referenceBody-selection metric (min distance
    // to surface, not min distance to center) so atmosphere checks for a
    // ship orbiting a moon close to a planet pick the moon, not the planet.
    static CelestialBody NearestBodyToSurface(Vector3 pos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody best  = null;
        float bestSurfaceDst = float.MaxValue;
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            if (b.bodyType == CelestialBody.BodyType.Sun) continue;
            float surfaceDst = Vector3.Distance(b.Position, pos) - b.radius;
            if (surfaceDst < bestSurfaceDst) { bestSurfaceDst = surfaceDst; best = b; }
        }
        return best;
    }

    AtmosphereSubject GetSubject()
    {
        // Piloted ship is the subject — ship transform/IsLanded/rb are all
        // live during piloting (the ship's gameObject stays active even when
        // the player's is disabled). Use the cached static (set by
        // Ship.PilotShip / cleared on exit) rather than FindPilotedShip,
        // which iterated every Ship in the scene per call.
        var piloted = Ship.PilotedInstance;
        if (piloted != null)
        {
            Vector3 pos = piloted.transform.position;
            return new AtmosphereSubject
            {
                position = pos,
                body     = NearestBodyToSurface(pos),
                rb       = piloted.Rigidbody,
                isLanded = piloted.IsLanded,
                isShip   = true,
                valid    = true,
            };
        }

        // Player on foot — use PlayerController's own referenceBody (same
        // selection metric as NearestBodyToSurface) and live transform.
        if (_cachedPC == null) _cachedPC = FindObjectOfType<PlayerController>();
        if (_cachedPC == null) return default;
        return new AtmosphereSubject
        {
            position = _cachedPC.transform.position,
            body     = _cachedPC.ReferenceBody,
            rb       = _cachedPC.Rigidbody,
            isLanded = _cachedPC.IsOnGround,
            isShip   = false,
            valid    = true,
        };
    }

    // Called once per Update at the top of the frame. If the subject
    // changed since last frame (got in or out of a ship), silently re-seed
    // all tracker state from the new subject so the boundary delta between
    // the previous frame's subject position and this frame's position
    // doesn't fire spurious enter/leave/landing lines. Both PollAtmosphere
    // and UpdateLandingTracker treat _lastSubjectWasShip as read-only —
    // only this method writes it. That prevents the race that fired (or
    // dropped) a line depending on whether the throttled poll ran before
    // or after the every-frame landing tracker on the switch frame.
    void HandleSubjectChange(AtmosphereSubject s)
    {
        if (!s.valid) return;
        if (s.isShip == _lastSubjectWasShip) return;

        if (s.body != null)
        {
            float dist = Vector3.Distance(s.position, s.body.Position);
            _inAtmosphere     = dist < AtmosphereThresholdFor(s.body);
            _atmosphereSeeded = true;
        }
        _wasGroundedLastFrame = s.isLanded;
        // Re-seed _lastAirborneSpeed from the new subject's current velocity
        // so a rapid land-after-exit reports a sensible value instead of
        // whatever the previous subject was doing.
        _lastAirborneSpeed = (!s.isLanded && s.body != null && s.rb != null)
            ? (s.rb.velocity - s.body.velocity).magnitude
            : 0f;
        _lastSubjectWasShip = s.isShip;
    }

    void PollAtmosphere()
    {
        // VR drone test — the player's real body is safe on the ground; don't track the flying
        // drone/camera as an atmosphere crossing (and skipping keeps the seeded state so the
        // return to the body doesn't fire a spurious "entering atmosphere").
        if (DroneController.Active != null) return;
        var s = GetSubject();
        if (!s.valid || s.body == null) return;

        float dist      = Vector3.Distance(s.position, s.body.Position);
        float threshold = AtmosphereThresholdFor(s.body);
        bool inAtmoNow  = dist < threshold;

        // Seed silently on the very first poll so a fresh gameplay scene
        // doesn't fire "entering atmosphere" just because the player
        // loaded in on a planet.
        if (!_atmosphereSeeded)
        {
            _inAtmosphere     = inAtmoNow;
            _atmosphereSeeded = true;
            return;
        }

        if (inAtmoNow == _inAtmosphere) return;

        _inAtmosphere = inAtmoNow;
        string planetName = !string.IsNullOrEmpty(s.body.bodyName) ? s.body.bodyName : "this body";
        // Bypass the 8 s rate limit so a rapid leave-then-re-enter (boost
        // out, immediately fall back in) queues the entering line behind
        // the leaving line on the HUD instead of dropping it. Boundary-
        // crossing already gates against spam: only a real state flip
        // fires, so two lines within 8 s means two real transitions.
        Volunteer(inAtmoNow
            ? $"Entering {planetName} atmosphere, Astronaut. Descent in progress."
            : $"Leaving {planetName} atmosphere, be careful.",
            bypassRateLimit: true);

        // Arm / disarm the landing announcement. The next ground-touch after
        // entering atmosphere fires the "you have landed" line; leaving
        // atmosphere without touching down (e.g. boosted back to orbit)
        // disarms so the next entry-to-landing cycle gets its own line.
        _armedForLandingLine = inAtmoNow;
    }

    // Runs every Update. Tracks the subject's pre-impact speed while
    // airborne and fires the landing line on the airborne→grounded
    // transition if armed by an earlier entering-atmosphere event.
    void UpdateLandingTracker()
    {
        if (DroneController.Active != null) return;   // VR drone test — ignore the flying drone's landings
        var s = GetSubject();
        if (!s.valid) return;

        // Sample the airborne speed every frame the subject is in the air.
        // The cached value is "the most recent reading before landing" by
        // construction; we never overwrite it after touchdown so the
        // landing line reports pre-collision velocity.
        if (!s.isLanded && s.body != null && s.rb != null)
            _lastAirborneSpeed = (s.rb.velocity - s.body.velocity).magnitude;

        // Airborne → grounded transition fires the landing line (if armed).
        if (s.isLanded && !_wasGroundedLastFrame && _armedForLandingLine)
        {
            string planet = s.body != null && !string.IsNullOrEmpty(s.body.bodyName)
                ? s.body.bodyName : "this body";
            // Show one decimal for low-speed landings so a gentle jetpack
            // touchdown doesn't read "0 m/s" when it's really 0.4 m/s.
            // Above 10 m/s an int is plenty.
            string speedStr = _lastAirborneSpeed < 10f
                ? _lastAirborneSpeed.ToString("F1")
                : Mathf.RoundToInt(_lastAirborneSpeed).ToString();
            // Bypass the 8 s rate limit — the landing line is sequenced
            // closely with the entering-atmosphere line and would otherwise
            // be silently dropped most of the time.
            Volunteer($"You have landed on {planet} at a speed of {speedStr} m/s.", bypassRateLimit: true);
            _armedForLandingLine = false;
        }

        _wasGroundedLastFrame = s.isLanded;
    }

    void PollPlanetChange()
    {
        if (_cachedPC == null) _cachedPC = FindObjectOfType<PlayerController>();
        if (_cachedPC == null) return;
        var body = _cachedPC.ReferenceBody;
        if (body == null) return;
        string name = body.bodyName;
        if (string.IsNullOrEmpty(name)) return;
        if (name == _lastBodyName) return;
        _lastBodyName = name;

        // First poll just seeds — player is in their starting location, no
        // "you arrived at X" line should fire on game start.
        if (!_planetSeeded)
        {
            _visitedBodies.Add(name);
            _planetSeeded = true;
            return;
        }

        if (_visitedBodies.Contains(name)) return;
        _visitedBodies.Add(name);
        Volunteer($"You have arrived at {name}. I will note this.");
    }

    void PollEarlyGameFlags()
    {
        for (int i = 0; i < _flagTrackers.Length; i++)
        {
            var ft = _flagTrackers[i];
            bool current = ft.Read();
            if (_flagsSeeded && current && !ft.LastSeen)
                Volunteer(ft.Line);
            ft.LastSeen = current;
            _flagTrackers[i] = ft;
        }
        _flagsSeeded = true;
    }

    // ── Substantive vitals triggers ────────────────────────────────────
    // Hunger / thirst / health / ship power. Each crosses thresholds
    // downward → fires a one-shot line per crossing. Hysteresis prevents
    // re-firing when the value oscillates around the boundary.

    void PollVitals()
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;
        float h  = rm.HungerPercent * 100f;
        float t  = rm.ThirstPercent * 100f;
        float hp = rm.HealthPercent * 100f;

        // Hunger: stages 0..3 = above 50 / at-or-below 50 / 25 / 10.
        int hStage = h <= 10f ? 3 : h <= 25f ? 2 : h <= 50f ? 1 : 0;
        if (hStage > _hungerStage)
        {
            int pct = Mathf.RoundToInt(h);
            Volunteer($"Hunger at {pct}%. Seek food intake.");
        }
        if      (_hungerStage == 1 && h >= 55f) _hungerStage = 0;
        else if (_hungerStage == 2 && h >= 30f) _hungerStage = 1;
        else if (_hungerStage == 3 && h >= 15f) _hungerStage = 2;
        else _hungerStage = Mathf.Max(_hungerStage, hStage);

        // Thirst: same shape as hunger.
        int tStage = t <= 10f ? 3 : t <= 25f ? 2 : t <= 50f ? 1 : 0;
        if (tStage > _thirstStage)
        {
            int pct = Mathf.RoundToInt(t);
            Volunteer($"Thirst at {pct}%. Hydration recommended.");
        }
        if      (_thirstStage == 1 && t >= 55f) _thirstStage = 0;
        else if (_thirstStage == 2 && t >= 30f) _thirstStage = 1;
        else if (_thirstStage == 3 && t >= 15f) _thirstStage = 2;
        else _thirstStage = Mathf.Max(_thirstStage, tStage);

        // Health: only two thresholds (50 and 25).
        int healthStage = hp <= 25f ? 2 : hp <= 50f ? 1 : 0;
        if (healthStage > _healthStage)
        {
            int pct = Mathf.RoundToInt(hp);
            Volunteer($"Health at {pct}%, Astronaut. Take cover.");
        }
        if      (_healthStage == 1 && hp >= 55f) _healthStage = 0;
        else if (_healthStage == 2 && hp >= 30f) _healthStage = 1;
        else _healthStage = Mathf.Max(_healthStage, healthStage);

        // Ship power + fuel: voice lines target the piloted ship's per-ship
        // resources. When no ship is piloted we hold the fired flags (don't
        // flicker when entering / exiting cockpits).
        Ship pi = Ship.PilotedInstance;
        if (pi != null)
        {
            int shipN = HALShipNumber(pi);
            float pwr = pi.PowerPercent * 100f;
            float ful = pi.FuelPercent  * 100f;

            if (pwr <= 25f && !_shipPowerLowFired)
            {
                Volunteer($"Ship {shipN} power at {Mathf.RoundToInt(pwr)}%. Solar panel exposure recommended.");
                _shipPowerLowFired = true;
            }
            else if (pwr >= 40f && _shipPowerLowFired)
            {
                _shipPowerLowFired = false;
            }

            if (ful <= 25f && ful > 0f && !_shipFuelLowFired)
            {
                Volunteer($"Ship {shipN} fuel at {Mathf.RoundToInt(ful)}%. Insert crystals into the reactor.");
                _shipFuelLowFired = true;
            }
            else if (ful >= 40f && _shipFuelLowFired)
            {
                _shipFuelLowFired = false;
            }

            if (ful <= 0f && !_shipFuelEmptyFired)
            {
                Volunteer($"Ship {shipN} reactor is dry. Thrust disabled.");
                _shipFuelEmptyFired = true;
            }
            else if (ful > 0f && _shipFuelEmptyFired)
            {
                _shipFuelEmptyFired = false;
            }
        }
    }

    static int HALShipNumber(Ship ship)
    {
        if (ship == null) return 0;
        var b = ship.GetComponent<BoughtShip>();
        return b != null ? b.shipNumber : 0;
    }

    // ── Per-ship dust threshold trigger ───────────────────────────────
    // Volunteers "Ship N has collected NN dust." or "Ship N net is full."
    // on upward crossings of 100 / 250 / 500. Decrements per-ship stage
    // when the buffer drops back below the previous threshold so the
    // next fill re-fires.

    void PollShipDust()
    {
        foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            var shipGO = pair.go;
            int n      = pair.number;
            if (shipGO == null) continue;

            int total = SumNetBuffers(shipGO);
            int prev  = _shipDustStage.TryGetValue(n, out var v) ? v : 0;
            int next  = total >= 500 ? 3
                       : total >= 250 ? 2
                       : total >= 100 ? 1
                       : 0;

            if (next > prev)
            {
                string line = next == 3
                    ? $"Ship {n} net is full."
                    : $"Ship {n} has collected {total} dust.";
                Volunteer(line);
            }
            _shipDustStage[n] = next;
        }
    }

    static int SumNetBuffers(GameObject ship)
    {
        if (ship == null) return 0;
        var nets = ship.GetComponentsInChildren<SpaceNet>(true);
        if (nets == null) return 0;
        int total = 0;
        for (int i = 0; i < nets.Length; i++)
        {
            var net = nets[i];
            if (net == null || !net.IsAttached) continue;
            total += net.BufferedDust;
        }
        return total;
    }

    // ── Per-ship orbit-stabilized trigger ─────────────────────────────
    // Fires the first time each ship reaches Ship.IsOrbitMatched while in
    // proximity to a body. Resets the per-ship dedupe when the ship
    // leaves the planet's proximity so a re-orbit later fires again.

    void PollShipOrbit()
    {
        foreach (var pair in FleetTelemetry.EnumerateAllShipsWithNumbers())
        {
            var shipGO = pair.go;
            int n      = pair.number;
            if (shipGO == null) continue;

            var ship = shipGO.GetComponent<Ship>();
            if (ship == null) continue;

            Vector3 pos = shipGO.transform.position;
            CelestialBody nearest = null;
            float bestDist = float.MaxValue;
            var bodies = NBodySimulation.Bodies;
            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    var b = bodies[i];
                    if (b == null) continue;
                    float d = Vector3.Distance(pos, b.Position);
                    if (d < bestDist) { bestDist = d; nearest = b; }
                }
            }
            bool inProximity = nearest != null && bestDist <= nearest.radius * 5f;

            if (!inProximity)
            {
                _shipOrbitAnnounced.Remove(n);
                continue;
            }

            if (ship.IsOrbitMatched && !_shipOrbitAnnounced.Contains(n))
            {
                _shipOrbitAnnounced.Add(n);
                Volunteer($"Ship {n} has stabilized orbit around {nearest.bodyName}.");
            }
        }
    }
}
