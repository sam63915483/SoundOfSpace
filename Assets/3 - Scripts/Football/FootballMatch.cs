using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game (handoff §5): coin toss → kickoff → drives → quarters → final.
/// Owns the two teams, the fourteen slots, the ball, the clock, downs and
/// distance, the score, the play-by-play and the debug panel. Hands each snap
/// to a PlayInstance and reads its result. Sits under FieldRoot; every
/// position it touches is field space.
///
/// Decisions taken (Sam, 2026-09-18 "continue with the plan"): clock runs
/// except after incompletions, out of bounds, turnovers and scores (the real
/// rule — and the dead-ball choreography is what eats it otherwise); TD = 7,
/// no kicking game; 4 downs, no punts; the sixth defender is a QB spy, the
/// seventh a safety; a CPU QB scrambles rather than throwing away.
///
/// The NEXT play is built the instant the previous one ends, so its Setup
/// phase (the ball carried back to the centre, the huddle, the lineup) runs
/// through the dead-ball hold. Nobody — and nothing — ever waits frozen.
///
/// Debug (§8): F8 opens the panel (speed, force a play, new game, whistle);
/// [ and ] step the sim speed; F7 hides the play-by-play. Speed is the sim's
/// own multiplier, never Time.timeScale, so it is safe next to the real world.
///
/// Phase 2 hooks (§9): PlayEnded event with the result; Possession / IsOffense
/// for locking the player off the field; qbBrainOverride[teamIndex] to plug a
/// human brain into a team's QB slot with no other change.
/// </summary>
public class FootballMatch : MonoBehaviour
{
    public static FootballMatch Instance;

    public enum State { Idle, CoinToss, Kickoff, DeadBall, Play, Score, QuarterBreak, GameOver }

    [Header("Teams")]
    public FootballTeam home = FootballTeam.DefaultHome();
    public FootballTeam away = FootballTeam.DefaultAway();

    [Header("Rules")]
    public float quarterSeconds = 300f;
    public int quarters = 4;
    public int touchdownPoints = 7;
    public float firstDownYards = 10f;
    [Tooltip("Stop the clock after incompletions, out of bounds, turnovers and scores (the real rule).")]
    public bool clockStoppages = true;
    [Tooltip("Madden's accelerated clock: after a play where the clock keeps running, this much runs off during the huddle (instead of the real ~30 s).")]
    public float playClockRunoff = 25f;

    [Header("Pacing (seconds)")]
    public float coinTossHold = 3f;
    [Tooltip("Minimum dead-ball time before the snap is allowed; the lineup and ball return can take longer.")]
    public float deadBallHold = 2.2f;
    public float scoreHold = 4f;
    public float quarterBreakHold = 4f;
    public float gameOverHold = 25f;

    [Header("Look")]
    [Tooltip("Alien_Pack prefabs; each team's seven take the next seven (empty = capsules).")]
    public GameObject[] alienPrefabs = new GameObject[0];
    [Tooltip("Paint the line of scrimmage (blue) and the first-down line (orange) on the grass.")]
    public bool showFieldLines = true;

    [Header("Run")]
    public bool autoStart = true;
    public int seed = 0;                    // 0 = random every game
    [Range(0.25f, 4f)] public float simSpeed = 1f;
    public bool showPlayByPlay = true;

    /// Phase 2: a brain to use for this team's QB slot instead of the CPU.
    [NonSerialized] public IPlayerBrain[] qbBrainOverride = new IPlayerBrain[2];
    /// Phase 2: the player at QB (the red button on the sideline); null = all CPU.
    [NonSerialized] public FootballHumanQB humanQb;
    public FootballBall Ball => _ball;

    public event Action<PlayInstance.PlayResult> PlayEnded;
    /// Fired after every sim step (the broadcast recorder samples here).
    public event Action<float> Stepped;

    // ── read API (scoreboard, HUD) ─────────────────────────────────────────
    public State Current => _state;
    public int Quarter => _quarter;
    public float ClockSeconds => _clock;
    public bool ClockStopped => _clockStopped;
    public int Down => _down;
    public float ToGo => _toGo;
    public FootballTeam Possession => _possession;
    public float LineOfScrimmageZ => _losZ;
    public string BallOnText => _possession == null ? "" : Mathf.RoundToInt(FootballField.YardLineLabel(_losZ)).ToString();
    public bool IsOffense(FootballTeam t) => _possession == t && (_state == State.Play || _state == State.DeadBall);
    public IReadOnlyList<string> Log => _log;
    public PlayInstance CurrentPlay => _play;
    public IReadOnlyList<FootballPlayer> Players => _players;
    public string StatusLine => _status;
    /// Debug: a play name to call every snap (null = pick by down and distance).
    public string ForcedPlay { get => _forcedPlay; set => _forcedPlay = value; }
    public GameStats Stats => _stats;

    /// Whole-game counters (the soak reads these).
    public class GameStats
    {
        public int plays, interceptions, turnoversOnDowns, sacks, completions, attempts, firstDowns, fumbles, fumblesLost;
        public int jukes, spins, hurdles, hurdlesClipped, dives, diveHits, wildSnaps, snapsCaught, rollouts, scrambleDrills, emotes, officiated, brokenTackles, contested, tips, stiffArms, stumbles;
        public float yards, longest, setupSeconds; public string longestDesc = "";
        public float liveSeconds;
        public string Summary(int plays)
            => "moves: juke " + jukes + " spin " + spins + " stiff-arm " + stiffArms + " stumble " + stumbles + " hurdle " + hurdles + " (clipped " + hurdlesClipped + ") | dives " + dives + "/" + diveHits + " hit"
             + " | fumbles " + fumbles + " (lost " + fumblesLost + ") | snaps " + snapsCaught + " caught, " + wildSnaps + " wild"
             + " | QB rollouts " + rollouts + " scrambles " + scrambleDrills + " | broke " + brokenTackles + " tackles | contested " + contested + " (tipped " + tips + ") | emotes " + emotes
             + " | dead-ball avg " + (plays > 0 ? (setupSeconds / plays).ToString("0.0") : "-") + " s" + (officiated > 0 ? " | OFFICIALS SPOTTED " + officiated : "");
    }

    // ── state ──────────────────────────────────────────────────────────────
    State _state = State.Idle;
    float _stateTime;
    int _quarter = 1;
    float _clock;
    bool _clockStopped;
    float _runoffLeft;                        // accelerated-clock seconds still to burn during this dead ball
    FootballTeam _possession;
    int _down = 1;
    float _toGo = 10f;
    float _losZ;
    FootballTeam _openingReceiver;
    bool _pendingKickoff;                     // a score with the quarter already over: kick off after the break
    bool _freshKickoff;                       // a new game: whatever kickoff was forming belongs to the old one
    bool _puntNext;                           // 4th down: kick it away
    bool _benched;
    PlayInstance _play;
    FootballPlay _lastCall;
    string _forcedPlay;                       // debug: play name or null
    string _status = "";

    readonly List<FootballPlayer> _players = new List<FootballPlayer>();
    FootballBall _ball;
    System.Random _rng;
    Material[] _teamMats;
    readonly List<string> _log = new List<string>();
    const int LogLines = 10;
    GameStats _stats = new GameStats();

    bool _panel;
    GUIStyle _logStyle;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Boot();
        if (autoStart) NewGame();
    }

    /// Squads + ball, once. Start does this; the headless soak calls it directly.
    public void Boot()
    {
        if (_ball != null) return;
        home.index = 0; away.index = 1;
        BuildSquads();
        // Shed the two debug overlays every scene otherwise gets. The HUD itself
        // is the gameplay one: the auto-created VitalsHUD / CompassHUD / Hotbar
        // (VitalsHUD switches the legacy ResourceHUD bars off) plus HUD_Canvas.
        if (Application.isPlaying)
        {
            foreach (var t in new[] { "LightingDebugToolbox", "PerfTrace" })
            {
                var type = System.Type.GetType(t);
                if (type == null) continue;
                foreach (var c in FindObjectsOfType(type, true)) if (c is Component comp) Destroy(comp.gameObject);
            }
        }
        // The big screens + broadcast camera (play mode only; the soak has no cameras).
        if (Application.isPlaying && FindObjectOfType<FootballBroadcast>() == null)
        {
            var go = new GameObject("Broadcast");
            go.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
            go.AddComponent<FootballBroadcast>();
        }
        // The QB button (Phase 2), play mode only.
        if (Application.isPlaying && FindObjectOfType<FootballHumanQB>() == null)
        {
            var go = new GameObject("HumanQB");
            go.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
            go.AddComponent<FootballHumanQB>();
        }
    }

    /// Run the game forward without rendering (the soak test, or a skip-ahead).
    public void Simulate(float seconds, float dt = 1f / 60f)
    {
        Boot();
        if (_state == State.Idle) NewGame();
        for (float t = 0f; t < seconds && _state != State.Idle; t += dt) Step(dt);
    }

    // ── squads ─────────────────────────────────────────────────────────────

    const int SquadSize = 7;

    void BuildSquads()
    {
        var fieldRoot = transform.parent != null ? transform.parent : transform;
        _teamMats = new Material[2];
        foreach (var team in new[] { home, away })
        {
            var mat = new Material(Shader.Find("Standard")) { name = team.shortName, color = team.color };
            mat.SetFloat("_Glossiness", 0.3f);
            _teamMats[team.index] = mat;
            // Seven a side, both ways: QB/LB, C/S, two OL/DL, three WR/DB.
            Add(team, FootballRole.QB, 0, FootballRole.LB, 0);
            Add(team, FootballRole.C, 0, FootballRole.S, 0);
            Add(team, FootballRole.OL, 0, FootballRole.DL, 0);
            Add(team, FootballRole.OL, 1, FootballRole.DL, 1);
            Add(team, FootballRole.WR, 0, FootballRole.DB, 0);
            Add(team, FootballRole.WR, 1, FootballRole.DB, 1);
            Add(team, FootballRole.WR, 2, FootballRole.DB, 2);
        }
        var ballGo = new GameObject("Ball");
        _ball = ballGo.AddComponent<FootballBall>();
        _ball.Init(fieldRoot);
        _ball.Place(Vector3.zero);
        Bench();

        void Add(FootballTeam t, FootballRole off, int oi, FootballRole def, int di)
        {
            var go = new GameObject(t.shortName);
            var p = go.AddComponent<FootballPlayer>();
            GameObject model = null;
            if (alienPrefabs != null && alienPrefabs.Length > 0)
                model = alienPrefabs[(t.index * SquadSize + _players.Count % SquadSize) % alienPrefabs.Length];
            p.Init(fieldRoot, t, off, oi, def, di, _teamMats[t.index], model);
            _players.Add(p);
        }
        BuildFieldLines(fieldRoot);
    }

    // ── the TV lines ───────────────────────────────────────────────────────

    Transform _losLine, _firstLine;
    readonly Transform[] _downMarkers = new Transform[2];
    public Transform DownMarker(int i) => _downMarkers[i];
    readonly TMPro.TextMeshPro[] _downDigits = new TMPro.TextMeshPro[4];
    public Transform LosLine => _losLine;
    public Transform FirstLine => _firstLine;

    void BuildFieldLines(Transform fieldRoot)
    {
        _losLine = MakeLine(fieldRoot, "LineOfScrimmage", new Color(0.2f, 0.5f, 1f));
        _firstLine = MakeLine(fieldRoot, "FirstDownLine", new Color(1f, 0.55f, 0.05f));
        for (int i = 0; i < 2; i++) _downMarkers[i] = MakeDownMarker(fieldRoot, i);
        ShowFieldLines(false);
    }

    static Transform MakeLine(Transform fieldRoot, string name, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = go.GetComponent<Collider>();
        if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
        go.name = name;
        go.transform.SetParent(fieldRoot, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(FootballField.Width + 1f, 0.28f, 1f);
        var sh = Shader.Find("Unlit/Color");
        var mat = new Material(sh != null ? sh : Shader.Find("Standard")) { color = c };
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    /// The down marker (Sam): a stick with a sign on each sideline at the
    /// line of scrimmage, showing the down.
    Transform MakeDownMarker(Transform fieldRoot, int side)
    {
        var root = new GameObject("DownMarker" + (side == 0 ? "Left" : "Right"));
        root.transform.SetParent(fieldRoot, false);
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Kill(pole.GetComponent<Collider>());
        pole.transform.SetParent(root.transform, false);
        pole.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        pole.transform.localScale = new Vector3(0.07f, 1.1f, 0.07f);
        var pm = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.9f, 0.9f) };
        pole.GetComponent<Renderer>().sharedMaterial = pm;
        var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Kill(sign.GetComponent<Collider>());
        sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        sign.transform.localScale = new Vector3(0.9f, 0.9f, 0.1f);
        var sm = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.45f, 0.05f) };
        sm.SetFloat("_Glossiness", 0.2f);
        sign.GetComponent<Renderer>().sharedMaterial = sm;
        // A digit on each face so it reads from either end of the field.
        for (int f = 0; f < 2; f++)
        {
            var tgo = new GameObject("Digit");
            tgo.transform.SetParent(sign.transform, false);
            tgo.transform.localPosition = new Vector3(0f, 0f, f == 0 ? -0.6f : 0.6f);
            tgo.transform.localRotation = Quaternion.Euler(0f, f == 0 ? 0f : 180f, 0f);
            tgo.transform.localScale = new Vector3(1f / 0.9f, 1f / 0.9f, 1f / 0.1f);
            var tmp = tgo.AddComponent<TMPro.TextMeshPro>();
            tmp.text = "1"; tmp.fontSize = 7f; tmp.fontStyle = TMPro.FontStyles.Bold; tmp.color = Color.black;
            tmp.alignment = TMPro.TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 1f);
            _downDigits[side * 2 + f] = tmp;
        }
        root.SetActive(false);
        return root.transform;
    }

    static void Kill(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    void ShowFieldLines(bool on)
    {
        if (_losLine == null) return;
        bool show = on && showFieldLines && _possession != null;
        _losLine.gameObject.SetActive(show);
        _firstLine.gameObject.SetActive(show);
        for (int i = 0; i < 2; i++)
        {
            if (_downMarkers[i] == null) continue;
            _downMarkers[i].gameObject.SetActive(show);
            if (!show) continue;
            float x = (i == 0 ? -1f : 1f) * (FootballField.HalfWidth + 1.4f);
            _downMarkers[i].localPosition = new Vector3(x, 0f, _losZ);
            // Signs face across the field toward the stands' far side: the pole's
            // faces are along ±Z (down the field), which is how the stands read them.
        }
        string d = _down.ToString();
        foreach (var t in _downDigits) if (t != null && t.text != d) t.text = d;
        if (!show) return;
        float y = 0.045f;
        _losLine.localPosition = new Vector3(0f, y, _losZ);
        float firstZ = _losZ + _possession.attackDir * FootballField.Yards(_toGo);
        bool goalToGo = YardsToGoal() <= _toGo + 0.01f;
        _firstLine.gameObject.SetActive(!goalToGo);
        _firstLine.localPosition = new Vector3(0f, y, firstZ);
    }

    /// Everyone to their sideline.
    void Bench()
    {
        int h = 0, a = 0;
        foreach (var p in _players)
        {
            bool isHome = p.team == home;
            int i = isHome ? h++ : a++;
            float x = (isHome ? -1f : 1f) * (FootballField.HalfWidth + 5f);
            p.Teleport(new Vector3(x, 0f, -12f + i * 4f), Vector3.right * (isHome ? 1f : -1f));
            p.brain = null;
        }
    }

    // ── game flow ──────────────────────────────────────────────────────────

    public void NewGame()
    {
        _rng = new System.Random(seed != 0 ? seed : Environment.TickCount);
        home.score = away.score = 0;
        home.attackDir = 1; away.attackDir = -1;
        _quarter = 1; _clock = quarterSeconds; _clockStopped = true;
        _stats = new GameStats();
        _possession = null; _lastCall = null; _pendingKickoff = false; _freshKickoff = true; _puntNext = false;
        // _play is left alone: whatever lineup was forming keeps walking (TickHeld) until the kickoff replaces it.
        _log.Clear();
        if (!_benched) { Bench(); _benched = true; }     // only ever at boot — nobody teleports mid-session
        Say(home.name + " vs " + away.name);
        Enter(State.CoinToss);
    }

    void Enter(State s)
    {
        _state = s; _stateTime = 0f;
        switch (s)
        {
            case State.CoinToss:
            {
                bool homeWins = _rng.Next(2) == 0;
                _openingReceiver = homeWins ? home : away;
                Say("Coin toss: " + _openingReceiver.name + " win it and will receive");
                _status = "COIN TOSS";
                break;
            }
            case State.Kickoff:
            {
                _pendingKickoff = false;
                ShowFieldLines(false);
                if (_freshKickoff || _play == null || !_play.view.isKickoff || _play.phase == PlayInstance.Phase.Ended) PrepareKickoff();
                _freshKickoff = false;
                _play.holdSnap = false;
                _status = "KICKOFF";
                break;
            }
            case State.DeadBall:
                _status = _puntNext ? DownText() + " — punt" : DownText();
                ShowFieldLines(true);
                if (_puntNext) { if (_play == null || !_play.isPunt || _play.phase == PlayInstance.Phase.Ended) PreparePunt(); }
                else if (_play == null || _play.phase == PlayInstance.Phase.Ended || _play.view.isKickoff) PrepareScrimmage();
                break;
            case State.Play:
            {
                if (_puntNext) { if (_play == null || !_play.isPunt || _play.phase == PlayInstance.Phase.Ended) PreparePunt(); }
                else if (_play == null || _play.phase == PlayInstance.Phase.Ended || _play.view.isKickoff) PrepareScrimmage();
                _play.holdSnap = false;
                _status = DownText() + " — " + (_play.isPunt ? "punt" : _play.view.play.name);
                break;
            }
            case State.Score:
                _status = "TOUCHDOWN";
                ShowFieldLines(false);
                // Line up for the kickoff while the score shows: both teams
                // start the long walk now instead of after the hold, and the
                // scorer brings the ball to his kicker.
                if (_clock > 0f)
                {
                    _possession = Other(_possession);       // the scorer kicks; possession = receiver
                    PrepareKickoff();
                    _play.holdSnap = true;
                }
                break;
            case State.QuarterBreak:
                _status = _quarter == 2 ? "HALFTIME" : "END OF Q" + _quarter;
                break;
            case State.GameOver:
                _status = "FINAL";
                DumpSummary();
                break;
        }
    }

    void Update()
    {
        HandleKeys();
        if (_state == State.Idle) return;
        // Sub-step so 4× never lets a ball skip a chest.
        float total = Mathf.Min(Time.deltaTime, 0.1f) * simSpeed;
        while (total > 0f)
        {
            float dt = Mathf.Min(total, 1f / 60f);
            total -= dt;
            Step(dt);
        }
    }

    void Step(float dt)
    {
        _stateTime += dt;
        if (humanQb != null) humanQb.SyncSlot(dt);
        StepInner(dt);
        Stepped?.Invoke(dt);
    }

    void StepInner(float dt)
    {
        switch (_state)
        {
            case State.CoinToss:
                TickHeld(dt);
                if (_stateTime >= coinTossHold)
                {
                    _possession = _openingReceiver;   // the receiver; the kicker is the other
                    Enter(State.Kickoff);
                }
                break;
            case State.Kickoff:
                _play.Tick(dt);
                break;
            case State.Play:
                _play.Tick(dt);
                if (_play.phase == PlayInstance.Phase.Live) { _clockStopped = false; _stats.liveSeconds += dt; }
                if (!_clockStopped) RunClock(dt);
                break;
            case State.DeadBall:
                // The next play's lineup and ball return are already running.
                // The clock burns the accelerated runoff (fast) instead of real time.
                _play.Tick(dt);
                if (!_clockStopped && _runoffLeft > 0f) { float burn = Mathf.Min(_runoffLeft, dt * 6f); _runoffLeft -= burn; RunClock(burn); }
                if (_clock <= 0f) { EndQuarter(); break; }
                if (_stateTime >= deadBallHold) Enter(State.Play);
                break;
            case State.Score:
                // The kickoff lineup (if prepared) walks into place; nothing snaps.
                TickHeld(dt);
                if (_stateTime >= scoreHold)
                {
                    if (_clock <= 0f) EndQuarter();                                    // _pendingKickoff carries it over
                    else Enter(State.Kickoff);                                         // lineup already prepared
                }
                break;
            case State.QuarterBreak:
                TickHeld(dt);
                if (_stateTime >= quarterBreakHold) StartQuarter();
                break;
            case State.GameOver:
                TickHeld(dt);
                if (_stateTime >= gameOverHold) NewGame();
                break;
        }
    }

    /// A hold state: whatever play exists keeps its bodies moving — a lineup
    /// walking in with the snap held, or an ended play's men getting up.
    void TickHeld(float dt)
    {
        if (_play == null) return;
        if (_play.phase != PlayInstance.Phase.Ended) { _play.holdSnap = true; _play.Tick(dt); }
        else _play.TickDead(dt);
    }

    /// The kickoff play: kicking team = whoever isn't in possession.
    List<FootballPlayer> _party; Vector3 _partyCentre;

    /// Backspace: skip the wait — a running replay is cut, a kickoff / punt ends as a
    /// touchback, a huddle breaks now. One press per thing.
    public void Skip()
    {
        if (_broadcast == null) _broadcast = FindObjectOfType<FootballBroadcast>();
        if (_broadcast != null && _broadcast.Replaying) { _broadcast.SkipReplay(); return; }
        if (_play == null || _play.phase == PlayInstance.Phase.Ended) return;
        // The player is waiting to play QB and this isn't his team's ball: hand it to them.
        if (humanQb != null && humanQb.Active)
        {
            var willHave = _play.view.isKickoff ? _play.view.defense : _play.view.offense;
            if (willHave != away) { SkipToHuman(); return; }
        }
        if (_play.view.isKickoff) _play.SkipKickoff();
        else if (_play.phase == PlayInstance.Phase.Setup) _play.skipHuddle = true;
    }
    FootballBroadcast _broadcast;

    /// Test key: throw the current play away and give the human's team a 1st & 10 at their 25.
    void SkipToHuman()
    {
        _play.Ended -= OnPlayEnded; _play.Ended -= OnKickoffEnded;
        _play.Abort();
        _possession = away; _puntNext = false; _pendingKickoff = false;
        _losZ = away.attackDir * (25f * FootballField.MetresPerYard - FootballField.GoalLineZ);
        FirstDown();
        Say("Skipped — " + away.shortName + " ball at the 25");
        Enter(State.DeadBall);
    }

    void PrepareKickoff()
    {
        var kicking = Other(_possession);
        SetSides(kicking);
        RestoreHumanSlot();
        _play = new PlayInstance(_players, _ball, kicking, _possession, _rng, null, _party, _partyCentre);
        _party = null;
        _play.log = Say;
        _play.holdSnap = true;
        _play.Ended += OnKickoffEnded;
    }

    /// 4th down: go for it, or punt? (No field goals.) Go when it's short,
    /// when a punt would gain little, or when trailing late.
    bool ShouldPunt()
    {
        if (_down != 4 || _forcedPlay != null) return false;
        float ytg = YardsToGoal();
        int diff = _possession.score - Other(_possession).score;
        bool late = _quarter >= 4 && _clock < 240f;
        if (late && diff < 0) return false;                       // trailing in the 4th: go
        if (_toGo <= 2f) return false;
        if (ytg <= 38f && _toGo <= 6f) return false;              // in range of a shot, no FG to fall back on
        if (ytg <= 25f) return false;                             // too close to give it away
        return true;
    }

    /// The away QB slot back to its alien (kickoffs, punts, defense).
    void RestoreHumanSlot()
    {
        foreach (var p in _players)
            if (p.humanDriven) p.SetHumanDriven(false, humanQb != null ? humanQb.BenchSpot : p.Pos);
    }

    void PreparePunt()
    {
        SetSides(_possession);
        RestoreHumanSlot();
        _play = new PlayInstance(_players, _ball, _possession, Other(_possession), _rng, _losZ);
        _play.log = Say;
        _play.holdSnap = true;
        _play.Ended += OnKickoffEnded;
        Say(_possession.shortName + " punt");
    }

    /// The next scrimmage play, built as soon as the last one ends so the
    /// lineup (and the ball return) run through the dead-ball hold. The call
    /// knows the situation: hurry-up when trailing late, the ground when
    /// leading late, the deep shot when desperate.
    void PrepareScrimmage()
    {
        SetSides(_possession);
        float ytg = YardsToGoal();
        var play = _forcedPlay != null ? FootballPlay.ByName(_forcedPlay) : null;
        int diff = _possession.score - Other(_possession).score;
        bool late = _quarter >= 4 && _clock < 180f;
        var mood = late && diff < 0 ? FootballPlay.Mood.Desperate : late && diff > 0 ? FootballPlay.Mood.KillClock : FootballPlay.Mood.Normal;
        bool human = humanQb != null && humanQb.Active && _possession == away;
        if (play == null) play = FootballPlay.Pick(_down, _toGo, ytg, _rng, _lastCall, mood);
        // The player at QB gets pass calls only: the trick plays (sweep, screen,
        // flea flicker, designed runs) hand the ball off by brain, and he has no brain.
        for (int i = 0; human && play.kind != FootballPlay.Kind.Pass && play.kind != FootballPlay.Kind.Rollout && i < 12; i++)
            play = FootballPlay.Pick(_down, _toGo, ytg, _rng, _lastCall, mood);
        _lastCall = play;
        var brain = human ? humanQb.Brain : qbBrainOverride[_possession.index];
        if (human) humanQb.Brain.Reset();
        _play = new PlayInstance(_players, _ball, _possession, Other(_possession), _losZ, play, brain, _rng, _toGo);
        var awayQb = _play.view.FindRole(away, away == _possession ? FootballRole.QB : FootballRole.LB);
        if (human) _play.SetHuman(awayQb, humanQb.BenchSpot);
        else if (awayQb != null && awayQb.humanDriven) awayQb.SetHumanDriven(false, humanQb != null ? humanQb.BenchSpot : awayQb.Pos);
        _play.hurryUp = mood == FootballPlay.Mood.Desperate;          // no huddle: straight to the line
        _play.log = Say;
        _play.holdSnap = true;
        _play.Ended += OnPlayEnded;
    }

    void RunClock(float dt)
    {
        _clock = Mathf.Max(0f, _clock - dt);
    }

    void Absorb(PlayInstance.PlayResult r)
    {
        var s = r.stats;
        _stats.jukes += s.jukes; _stats.spins += s.spins; _stats.hurdles += s.hurdles; _stats.hurdlesClipped += s.hurdlesClipped;
        _stats.dives += s.dives; _stats.diveHits += s.diveHits; _stats.fumbles += s.fumbles; _stats.fumblesLost += s.fumblesLost;
        _stats.wildSnaps += s.wildSnaps; _stats.snapsCaught += s.snapsCaught; _stats.rollouts += s.rollouts; _stats.scrambleDrills += s.scrambleDrills;
        _stats.emotes += s.emotes; _stats.setupSeconds += s.setupSeconds;
        _stats.brokenTackles += s.brokenTackles; _stats.contested += s.contested; _stats.tips += s.tips; _stats.stiffArms += s.stiffArms; _stats.stumbles += s.stumbles;
        if (s.officialsSpottedBall) _stats.officiated++;
        if (clockStoppages && r.clockStops) _clockStopped = true;
        _runoffLeft = _clockStopped ? 0f : playClockRunoff;
    }

    void OnKickoffEnded(PlayInstance.PlayResult r)
    {
        _play.Ended -= OnKickoffEnded;
        PlayEnded?.Invoke(r);
        Absorb(r);
        _puntNext = false;
        if (r.isKickoff && _play.isPunt) _stats.plays++;
        Say(r.description);
        if (r.touchdown)
        {
            Party(r);
            r.possession.score += touchdownPoints;
            _possession = r.possession;
            _pendingKickoff = true;
            Say(ScoreText());
            Enter(State.Score);
            return;
        }
        _possession = r.possession;
        _losZ = r.endSpotZ;
        FirstDown();
        Enter(State.DeadBall);
    }

    /// The scorer and the four nearest teammates line up and dance in the end zone.
    void Party(PlayInstance.PlayResult r)
    {
        if (r.carrier == null) return;
        var list = new List<FootballPlayer> { r.carrier };
        var mates = new List<FootballPlayer>();
        foreach (var p in _players) if (p != r.carrier && p.team == r.carrier.team) mates.Add(p);
        mates.Sort((a, b) => Vector3.Distance(a.Pos, r.carrier.Pos).CompareTo(Vector3.Distance(b.Pos, r.carrier.Pos)));
        for (int i = 0; i < mates.Count && i < 4; i++) list.Add(mates[i]);
        _party = list; _partyCentre = r.carrier.Pos;
    }

    void OnPlayEnded(PlayInstance.PlayResult r)
    {
        _play.Ended -= OnPlayEnded;
        PlayEnded?.Invoke(r);
        Absorb(r);
        _stats.plays++;
        if (r.outcome == PlayInstance.Outcome.Sack) _stats.sacks++;
        if (r.passer != null && r.play != null && (r.play.kind == FootballPlay.Kind.Pass || r.play.kind == FootballPlay.Kind.Rollout)) _stats.attempts++;
        if (r.outcome == PlayInstance.Outcome.Complete || (r.touchdown && r.passer != null && r.carrier != r.passer && !r.turnover)) _stats.completions++;
        if (!r.turnover) _stats.yards += r.yards;
        if (r.yards > _stats.longest) { _stats.longest = r.yards; _stats.longestDesc = r.description; }

        string prefix = DownText() + ": ";
        if (r.touchdown)
        {
            Say(prefix + r.description);
            Party(r);
            r.possession.score += touchdownPoints;
            _possession = r.possession;
            _pendingKickoff = true;
            Say(ScoreText());
            Enter(State.Score);
            return;
        }
        if (r.turnover)
        {
            if (r.outcome == PlayInstance.Outcome.Interception) _stats.interceptions++;
            Say(prefix + r.description);
            _possession = r.possession;
            _losZ = r.endSpotZ;
            FirstDown();
            Enter(State.DeadBall);
            return;
        }
        Say(prefix + r.description);
        float gain = FootballField.ToYards((r.endSpotZ - _losZ) * _possession.attackDir);
        _losZ = r.endSpotZ;
        if (gain >= _toGo - 0.01f)
        {
            _stats.firstDowns++;
            FirstDown();
        }
        else
        {
            _down++;
            _toGo -= gain;
            if (_down > 4)
            {
                _stats.turnoversOnDowns++;
                Say("Turnover on downs — " + Other(_possession).name + " take over at the " + BallOnText);
                _possession = Other(_possession);
                _clockStopped = clockStoppages;
                FirstDown();
            }
        }
        _puntNext = ShouldPunt();
        Enter(State.DeadBall);
    }

    void FirstDown()
    {
        _down = 1;
        _toGo = Mathf.Min(firstDownYards, YardsToGoal());
    }

    float YardsToGoal() => FootballField.ToYards(FootballField.GoalLineZ - _possession.attackDir * _losZ);

    void EndQuarter()
    {
        if (_quarter >= quarters)
        {
            Enter(State.GameOver);
            return;
        }
        Enter(State.QuarterBreak);
    }

    void StartQuarter()
    {
        _quarter++;
        _clock = quarterSeconds;
        _clockStopped = true;
        // Teams change ends: flip attack directions, mirror the ball.
        home.attackDir = -home.attackDir; away.attackDir = -away.attackDir;
        _losZ = -_losZ;
        // The lineup that was forming aimed at the old end: build the right
        // one now so nobody stands still, and everyone walks to the new spots.
        if (_quarter == 3)
        {
            // Second half: whoever received the opening kick now kicks.
            _possession = Other(_openingReceiver);
            Say("Second half — " + _openingReceiver.name + " kick off");
            PrepareKickoff();
            Enter(State.Kickoff);
            return;
        }
        Say("Start of Q" + _quarter);
        if (_pendingKickoff) { _possession = Other(_possession); PrepareKickoff(); Enter(State.Kickoff); return; }
        if (_possession == null) { _possession = _openingReceiver; PrepareKickoff(); Enter(State.Kickoff); return; }
        PrepareScrimmage();
        Enter(State.DeadBall);
    }

    void SetSides(FootballTeam offense)
    {
        foreach (var p in _players) p.SetSide(p.team == offense);
    }

    FootballTeam Other(FootballTeam t) => t == home ? away : home;

    // ── text ───────────────────────────────────────────────────────────────

    string DownText()
    {
        if (_possession == null) return "";
        string d = _down == 1 ? "1st" : _down == 2 ? "2nd" : _down == 3 ? "3rd" : "4th";
        bool goal = YardsToGoal() <= _toGo + 0.01f;
        return _possession.shortName + " " + d + " & " + (goal ? "Goal" : Mathf.CeilToInt(_toGo).ToString()) + " at the " + BallOnText;
    }

    string ScoreText() => home.shortName + " " + home.score + " — " + away.shortName + " " + away.score;

    public void Say(string line)
    {
        string stamp = "Q" + _quarter + " " + ClockText() + "  ";
        _log.Add(stamp + line);
        if (_log.Count > LogLines) _log.RemoveAt(0);
        Debug.Log("[Football] " + stamp + line);
    }

    public string ClockText()
    {
        int s = Mathf.CeilToInt(_clock);
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    void DumpSummary()
    {
        string winner = home.score == away.score ? "Tie game" : (home.score > away.score ? home.name : away.name) + " win";
        Say("FINAL — " + ScoreText() + " — " + winner);
        var s = _stats;
        Say("Plays " + s.plays + " | yds/play " + (s.plays > 0 ? (s.yards / s.plays).ToString("0.0") : "-")
            + " | pass " + s.completions + "/" + s.attempts + " | INT " + s.interceptions + " | sacks " + s.sacks
            + " | 1st downs " + s.firstDowns + " | on downs " + s.turnoversOnDowns + " | live " + s.liveSeconds.ToString("0") + " s");
        Say(s.Summary(s.plays));
        Say("Longest: " + s.longestDesc);
    }

    // ── debug (§8) ─────────────────────────────────────────────────────────

    void HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.F8)) _panel = !_panel;
        if (Input.GetKeyDown(KeyCode.F7)) showPlayByPlay = !showPlayByPlay;
        if (Input.GetKeyDown(KeyCode.Backspace)) Skip();     // N is the build menu in the main game
        if (Input.GetKeyDown(KeyCode.LeftBracket))  simSpeed = Mathf.Max(0.25f, simSpeed * 0.5f);
        if (Input.GetKeyDown(KeyCode.RightBracket)) simSpeed = Mathf.Min(4f, simSpeed * 2f);
    }

    void OnGUI()
    {
        if (_logStyle == null)
        {
            _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _logStyle.normal.textColor = Color.white;
        }
        if (showPlayByPlay && _state != State.Idle)
        {
            GUI.Box(new Rect(8, 8, 620, 24 + 20 * (_log.Count + 1)), "");
            GUI.Label(new Rect(16, 10, 620, 22), "<b>" + ScoreText() + "   Q" + _quarter + " " + ClockText() + (_clockStopped ? " ■" : "") + "   " + _status + "   x" + simSpeed + "</b>", _logStyle);
            for (int i = 0; i < _log.Count; i++)
                GUI.Label(new Rect(16, 32 + 20 * i, 620, 22), _log[i], _logStyle);
        }
        if (!_panel) return;
        float w = 300f, x = Screen.width - w - 8f, y = 8f;
        int n = FootballPlay.All.Count;
        int rows = (n + 1) / 2;
        GUI.Box(new Rect(x, y, w, 210 + rows * 20), "Football debug (F8)");
        y += 26;
        GUI.Label(new Rect(x + 8, y, w, 20), "Speed  [ ]"); y += 20;
        float bx = x + 8;
        foreach (float s in new[] { 0.25f, 0.5f, 1f, 2f, 4f })
        {
            if (GUI.Button(new Rect(bx, y, 40, 22), "x" + s)) simSpeed = s;
            bx += 43;
        }
        y += 30;
        GUI.Label(new Rect(x + 8, y, w, 20), "Force play"); y += 20;
        if (GUI.Toggle(new Rect(x + 8, y, w - 16, 20), _forcedPlay == null, "Auto (down & distance)")) _forcedPlay = null;
        y += 20;
        float y0 = y;
        for (int i = 0; i < n; i++)
        {
            var p = FootballPlay.All[i];
            float cx = x + 8 + (i < rows ? 0 : w / 2f);
            float cy = y0 + 20 * (i < rows ? i : i - rows);
            if (GUI.Toggle(new Rect(cx, cy, w / 2f - 12, 20), _forcedPlay == p.name, p.name)) _forcedPlay = p.name;
        }
        y = y0 + rows * 20 + 8;
        if (GUI.Button(new Rect(x + 8, y, w - 16, 24), "Whistle (end this play)") && _play != null && _play.phase != PlayInstance.Phase.Ended)
            _play.Whistle();
        y += 28;
        if (GUI.Button(new Rect(x + 8, y, w - 16, 24), "New game")) NewGame();
        y += 28;
        if (GUI.Button(new Rect(x + 8, y, w - 16, 24), "Flip possession here") && _possession != null && (_state == State.DeadBall))
        {
            _possession = Other(_possession); FirstDown(); Say("Debug: possession flipped");
            PrepareScrimmage();
        }
        y += 28;
        if (GUI.Button(new Rect(x + 8, y, w - 16, 24), "End quarter") && _state == State.DeadBall) { _clock = 0f; }
    }
}
