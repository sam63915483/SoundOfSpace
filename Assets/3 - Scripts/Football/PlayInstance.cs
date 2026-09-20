using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One snap — or one kickoff — from "everyone jog to your spot" to the
/// whistle (handoff §4 PlayInstance). Lines the players up, gets the ball
/// back to the line (carried, never placed by code), hands each slot its
/// brain, snaps, carries out the brains' actions (throw / handoff / kick /
/// juke / spin / hurdle), resolves catches, tackles, dives, fumbles,
/// touchdowns, out of bounds, incompletions and the pocket, and reports a
/// PlayResult. FootballMatch owns the drive logic; this never looks at downs
/// or the clock (it is told the yards to go so the celebrations know).
///
/// The Setup phase now starts the moment the previous play ends: the men who
/// were celebrating finish, whoever has the ball carries it to the centre (or
/// the kicker), a ball on the grass is fetched by the nearest man of the new
/// offense, the centre places it at the line, and only then can the snap
/// come. Nobody teleports — not even the ball (Sam).
///
/// Phase 2 hook (§9): the QB slot's brain is whatever the match hands in —
/// nothing here checks whether it's a CPU. A brain that KeepsControlWhenCarrying
/// is left alone when it takes the ball.
/// </summary>
public class PlayInstance
{
    public enum Phase { Setup, PreSnap, Live, Ended }

    public enum Outcome
    {
        Incomplete, Complete, Run, Sack, Interception, Touchdown, OutOfBounds,
        KickReturn, Touchback, KickRecoveredByKickingTeam, Whistle, Fumble
    }

    public class PlayResult
    {
        public Outcome outcome;
        public FootballPlay play;
        public bool isKickoff;
        /// Where the ball is spotted next (field z). Meaningless on a touchdown.
        public float endSpotZ;
        /// Yards gained by the OFFENSE (negative on a sack). Interceptions: 0.
        public float yards;
        /// Team in possession when the play ended.
        public FootballTeam possession;
        public FootballPlayer carrier, passer, receiver, tackler;
        public bool turnover;      // possession changed hands (INT, fumble, kick recovered)
        public bool touchdown;
        public bool firstDown;     // moved the chains (scrimmage plays only)
        /// The game clock stops on this result (incomplete, out of bounds, a
        /// change of possession, a score).
        public bool clockStops;
        public string description;
        public float returnYards;
        public PlayStats stats;
    }

    /// Counters for the soak (every new behaviour shows up here).
    public struct PlayStats
    {
        public int jukes, spins, hurdles, hurdlesClipped, dives, diveHits, fumbles, fumblesLost, wildSnaps, snapsCaught, rollouts, scrambleDrills, emotes, brokenTackles, contested, tips, stiffArms, stumbles;
        public bool officialsSpottedBall;
        public float setupSeconds;
    }

    public const float TackleRadius = 1.5f;     // an arm's reach / a dive
    /// Nobody is ever teleported into formation (Sam: it ruins the illusion).
    /// If the lineup is taking this long the play starts anyway and the
    /// stragglers run in — their brains cope with being out of position.
    public const float SetupTimeout = 16f;
    /// The ball is carried back by a player. If it STILL isn't at the line
    /// after this long (something went wrong), the officials spot it — the
    /// one place code moves the ball, and it is logged.
    public const float OfficialsSpotAfter = 45f;
    /// A man with the ball runs at this fraction of his top speed — pursuit
    /// closes from behind, which is what turns catches into 12-yard gains
    /// instead of touchdowns.
    public const float CarrierSpeed = 0.9f;
    /// Gathering the ball: the catcher's speed for a moment after the catch.
    public const float CatchDipSeconds = 0.45f;
    public const float CatchDipSpeed = 0.65f;
    /// Whoever gets to the ball first has to hold on to it (Sam, 2026-09-18):
    /// the man it was thrown to drops it 30% of the time, anyone else 50%. A
    /// drop tumbles off his hands to the grass — nobody can catch it after.
    public const float ReceiverDropChance = 0.12f;      // was 0.30 (Sam's first rule) — a third of every pass dying on a drop is where the incompletions were
    public const float DefenderDropChance = 0.60f;
    /// How long before the ball arrives the men near its landing spot go up for it.
    public const float JumpLead = 0.42f;
    /// Tackles: a lunge that misses puts the tackler on the ground and the
    /// runner keeps going; a hit puts both down.
    public const float MissedTackleChance = 0.18f;
    public const float MissedTackleBlocked = 0.45f;     // a man being blocked lunges worse
    public const float MissedTackleBehind = 0.22f;      // diving at a runner's heels
    public const float MissedTackleJuked = 0.22f;       // added while the runner is mid-juke
    public const float MissedTackleSpun = 0.22f;        // added while he's spinning off
    public const float MaxMissChance = 0.52f;           // moves and angles stack, but a tackler still gets a hand on him
    public const float TackleDownSeconds = 1.6f;
    public const float MissDownSeconds = 1.3f;
    public const float HardFallDownSeconds = 2.6f;
    public const float TackleRetry = 1.2f;
    /// Dives (Sam, 2026-09-19): a committed lunge from further out.
    public const float DiveChance = 0.45f;
    public const float DiveMinDist = 1.7f, DiveMaxDist = 2.7f, DiveReach = 1.6f;
    public const float HurdleClipChance = 0.22f;        // a clean-timed hurdle still gets caught sometimes
    public const float HurdleLateClipChance = 0.5f;     // left it too late
    public const float ClipFumbleChance = 0.3f;
    public const float BigHitFumbleChance = 0.035f;
    public const float ScoopChance = 0.40f;              // a loose ball picked up on the run rather than fallen on
    public const float WildSnapChance = 0.02f;
    public const float SnapFlightSeconds = 0.38f;
    public const float PreSnapHold = 0.9f;
    public const float LiveTimeout = 18f;
    public const float LooseBallTimeout = 6f;
    public const float KickFlightTime = 3.6f;
    public const float MinPassFlight = 1.15f;

    /// How long a pass of this length is in the air. The QB leads with the
    /// same number the ball is launched with — a mismatch had receivers
    /// overrunning the spot and trailing corners picking off the short ball.
    public static float PassFlightTime(float dist)
    {
        // Short balls are fired flat and fast (a slant is a 0.6 s throw);
        // anything past 15 m is lofted over the men in between.
        float floor = Mathf.Lerp(0.6f, MinPassFlight, Mathf.Clamp01(dist / 15f));
        return Mathf.Max(floor, dist / QBBrain_CPU.ThrowSpeed);
    }

    public Phase phase = Phase.Setup;
    /// While true the lineup can form but the play won't start (the dead-ball
    /// hold; the kickoff after a score lines up during the celebration).
    public bool holdSnap;
    public readonly PlayView view = new PlayView();
    public PlayResult result;
    public event Action<PlayResult> Ended;
    public Action<string> log;
    public FootballFormation formation;
    /// Phase 2: the QB slot the real player drives this play (null = all CPU).
    public FootballPlayer humanSlot;
    public bool HumanDriven => humanSlot != null;
    /// The player has walked into the snap spot; the snap follows a second later.
    public bool humanAtSpot;
    /// Everyone else is lined up and the ball is down: show the player his spot.
    public bool LinedUp => phase == Phase.Setup && _broke && _ballReady && _othersSet;
    public Vector3 HumanSnapSpot => _qb != null && _formation.TryGetValue(_qb, out var s) ? s : Vector3.zero;
    bool _othersSet;
    Vector3 _benchSpot;

    /// Hand this play's QB slot to the player. Called right after construction.
    public void SetHuman(FootballPlayer slot, Vector3 benchSpot)
    {
        humanSlot = slot; _benchSpot = benchSpot;
        if (_setup.TryGetValue(slot, out var mv))
        {
            // The alien jogs to the bench during the huddle; he vanishes at the break.
            mv.target = benchSpot; mv.ClearFace(); mv.hurry = true;
        }
    }
    public enum DefCall { Press, Normal, Off, Blitz }
    public DefCall defCall;
    /// True once the ball is at the line with the centre over it (or in the
    /// kicker's hands): the snap can come as soon as the hold lifts.
    public bool BallReady => _ballReady;
    public float SetupSeconds => _setupSeconds;
    public float ToGo => _toGo;

    readonly List<FootballPlayer> _players;
    readonly FootballBall _ball;
    readonly System.Random _rng;
    readonly float _toGo;
    readonly Dictionary<FootballPlayer, MoveToBrain> _setup = new Dictionary<FootballPlayer, MoveToBrain>();
    readonly Dictionary<FootballPlayer, IPlayerBrain> _live = new Dictionary<FootballPlayer, IPlayerBrain>();
    readonly Dictionary<FootballPlayer, Vector3> _formation = new Dictionary<FootballPlayer, Vector3>();
    readonly Dictionary<FootballPlayer, Vector3> _huddle = new Dictionary<FootballPlayer, Vector3>();
    float _phaseTime, _setupSeconds;
    float _pocketLife;
    bool _pocketDone;
    float _looseSince = -1f;
    FootballPlayer _lastCarrier;
    Vector3 _prevBallPos;
    float _engagedScale;
    FootballPlayer _kicker, _centre, _qb;
    FootballPlayer _interceptor;
    FootballPlayer _fumbler, _recoverer;
    FootballPlayer _pendingTackler; float _tackleEndAt = -1f;     // the fall: the play ends where the ball is when he is down
    readonly List<FootballPlayer> _wrappers = new List<FootballPlayer>();   // everyone wrapped on the carrier (a gang tackle)
    public bool isPunt;
    public const float WrapSeconds = 0.55f;           // the drag before they go down
    public const float BreakTackleChance = 0.22f;
    public const float TipChance = 0.35f;              // the loser of a contested ball gets a hand on it: it pops up, live
    public const float PartySeconds = 4.5f;            // the touchdown line-up dance before the kickoff walk
    readonly Dictionary<FootballPlayer, float> _slowUntil = new Dictionary<FootballPlayer, float>();   // jammed at the line
    bool _releasesDone;
    List<FootballPlayer> _party; Vector3 _partyCentre; float _partyDir;
    bool _qbTucked;
    // A throw (or kick) in motion: the ball leaves the hand when the arm gets there.
    bool _throwPending; Vector3 _throwTarget; FootballPlayer _throwTo; float _throwFlight;
    bool _kickPending; Vector3 _kickTarget;
    FootballPlayer _passer, _target;         // recorded at the throw (the ball forgets them on the catch)
    FootballPlayer _catcher; float _catchDipUntil;
    // Ball return (Setup).
    FootballPlayer _ballReceiver;            // the centre, or the kicker
    Vector3 _ballSpot;                       // where the ball is placed for the snap
    FootballPlayer _fetcher;
    bool _ballReady;
    int _huddleCount, _defHuddleCount;
    bool _broke;
    float _huddleSince = -1f;
    PlayStats _stats;
    /// Once everyone is in the huddle it holds this long (Sam: "like 10
    /// seconds") - the replay on the big screens plays in that window.
    public const float HuddleHold = 9f;
    public const float HuddleTimeout = 24f;
    /// The broadcast sets this while its replay plays: the huddle does not
    /// break until the replay is done (Sam: never cut a replay short).
    public bool holdBreak;
    /// Trailing late: the huddle is a two-second breather, not nine.
    public bool hurryUp;
    /// Test key: break the huddle the moment it is set (or now, if it is still gathering).
    public bool skipHuddle;
    /// Human QB: the huddle holds until he has walked in and picked a play.
    public bool humanPlayChosen;
    public bool Broke => _broke;
    public Vector3 HumanHuddleSpot => humanSlot != null && _huddle.TryGetValue(humanSlot, out var hs) ? hs : Vector3.zero;
    /// True while both sides are in their huddles (the replay window).
    public bool InHuddle => phase == Phase.Setup && !_broke && _huddleSince >= 0f;
    /// Seconds until the huddle breaks (an upper bound while men are still
    /// walking in); 0 once it has broken; −1 on a kickoff (no huddle).
    public float SecondsUntilBreak
    {
        get
        {
            if (view.isKickoff) return -1f;
            if (_broke || phase != Phase.Setup) return 0f;
            if (_huddleSince >= 0f) return Mathf.Max(0f, HuddleHold - (_phaseTime - _huddleSince));
            return Mathf.Max(0f, HuddleTimeout - _phaseTime);
        }
    }

    // ── construction ───────────────────────────────────────────────────────

    /// A scrimmage play. `toGo` is for the celebrations (a first down is a moment).
    public PlayInstance(List<FootballPlayer> players, FootballBall ball, FootballTeam offense, FootballTeam defense,
                        float losZ, FootballPlay play, IPlayerBrain qbBrainOverride, System.Random rng, float toGo = 10f, FootballFormation formation = null)
    {
        _players = players; _ball = ball; _rng = rng; _toGo = toGo;
        view.players = players; view.ball = ball; view.offense = offense; view.defense = defense;
        view.attackDir = offense.attackDir; view.losZ = losZ; view.play = play; view.isKickoff = false;
        view.timeSinceSnap = -1f;
        view.readDelay = play.kind == FootballPlay.Kind.JetSweep || play.kind == FootballPlay.Kind.QbDraw || play.kind == FootballPlay.Kind.FleaFlicker ? 0.8f : play.kind == FootballPlay.Kind.QbRun ? 0.55f : 0.4f;
        defCall = PickDefense(toGo, rng);
        // A screen against press or a blitz is dead on arrival; the offense calls it against a cushion.
        if (play.kind == FootballPlay.Kind.Screen && (defCall == DefCall.Press || defCall == DefCall.Blitz)) defCall = rng.Next(2) == 0 ? DefCall.Normal : DefCall.Off;
        this.formation = formation ?? FootballFormation.Pick(play, rng);
        BuildScrimmage(qbBrainOverride);
    }

    /// A kickoff by `kicking` from its own 35 — or a punt from `puntFromZ`.
    public PlayInstance(List<FootballPlayer> players, FootballBall ball, FootballTeam kicking, FootballTeam receiving, System.Random rng, float? puntFromZ = null, List<FootballPlayer> party = null, Vector3 partyCentre = default)
    {
        _party = party; _partyCentre = partyCentre;
        _players = players; _ball = ball; _rng = rng; _toGo = 10f;
        view.players = players; view.ball = ball; view.offense = kicking; view.defense = receiving;
        view.attackDir = kicking.attackDir; view.isKickoff = true; view.play = null;
        isPunt = puntFromZ.HasValue;
        view.losZ = isPunt ? puntFromZ.Value : kicking.attackDir * (35f * FootballField.MetresPerYard - FootballField.GoalLineZ);
        view.timeSinceSnap = -1f;
        BuildKickoff();
    }

    /// Attack-relative (x across, depth downfield) → field space.
    Vector3 F(float x, float depth) => new Vector3(x * view.attackDir * FootballField.MetresPerYard, 0f,
                                                   view.losZ + depth * view.attackDir * FootballField.MetresPerYard);

    void BuildScrimmage(IPlayerBrain qbOverride)
    {
        var off = view.offense; var def = view.defense;
        var qb  = _qb = Find(off, FootballRole.QB);
        var c   = _centre = Find(off, FootballRole.C);
        var ol  = new[] { Find(off, FootballRole.OL, 0), Find(off, FootballRole.OL, 1) };
        var wr  = new[] { Find(off, FootballRole.WR, 0), Find(off, FootballRole.WR, 1), Find(off, FootballRole.WR, 2) };
        var dl  = new[] { Find(def, FootballRole.DL, 0), Find(def, FootballRole.DL, 1) };
        var db  = new[] { Find(def, FootballRole.DB, 0), Find(def, FootballRole.DB, 1), Find(def, FootballRole.DB, 2) };
        var lb  = Find(def, FootballRole.LB);
        var s   = Find(def, FootballRole.S);

        // Formation (attack-relative yards): shotgun behind the centre, two
        // linemen, receivers by the formation; defense mirrors — two rushers
        // on the linemen, three men in man, a spy, a safety over the top.
        float[] wrX = formation.wrX;
        _ballSpot = F(0f, 0f);
        Spot(qb, F(0f, -4.5f));
        Spot(c, F(0f, -0.5f));
        Spot(ol[0], F(-1.6f, -0.5f)); Spot(ol[1], F(1.6f, -0.5f));
        for (int i = 0; i < 3; i++) Spot(wr[i], F(wrX[i], i == 2 ? -1.2f : -0.8f));
        // Sweeps: the sweep man is already in motion beside the QB at the snap.
        int sw = view.play.sweep;
        if (view.play.kind == FootballPlay.Kind.JetSweep || view.play.kind == FootballPlay.Kind.FleaFlicker) Spot(wr[sw], F(Mathf.Sign(wrX[sw]) * 5f, -3f));
        Spot(dl[0], F(-1.6f, 1.0f)); Spot(dl[1], F(1.6f, 1.0f));
        // The defensive call (Sam: they always lined up the same): press,
        // normal, off, or a blitz with the corners up tight.
        float dbDepth = defCall == DefCall.Press || defCall == DefCall.Blitz ? 1.3f : defCall == DefCall.Off ? 7f : 3.5f;
        float sDepth = defCall == DefCall.Off ? 15f : defCall == DefCall.Normal ? 12f : 10f;
        float shade = 0f;
        for (int i = 0; i < 3; i++)
        {
            float x = Mathf.Clamp(wrX[i], -(FootballField.HalfWidth - 1.5f), FootballField.HalfWidth - 1.5f);
            Spot(db[i], F(x, dbDepth + (i == 2 ? 1.2f : 0f)));
            shade += wrX[i];
        }
        Spot(lb, F(0f, defCall == DefCall.Blitz ? 2.5f : 4.5f));
        Spot(s, F(shade / 3f * 0.3f, sDepth));

        // Live brains.
        var play = view.play;
        var readOrder = new List<FootballPlayer>();
        foreach (int i in play.priority) readOrder.Add(wr[i]);
        _live[qb] = qbOverride ?? new QBBrain_CPU(off, readOrder, F(0f, -7f), _rng, play, wrX[2]);
        _live[c] = new CenterBrain();
        for (int i = 0; i < 2; i++) _live[ol[i]] = new OLBrain(dl[i], F(i == 0 ? -2.6f : 2.6f, -3.0f));
        for (int i = 0; i < 3; i++)
        {
            var route = new List<Vector3>();
            float sideward = Mathf.Sign(wrX[i]);                 // + = toward this receiver's own sideline
            foreach (var wp in play.routes[i].points) route.Add(OnField(F(wrX[i] + wp.x * sideward, wp.y)));
            _live[wr[i]] = new WRBrain(route, play.routes[i].settle);
        }
        for (int i = 0; i < 2; i++) _live[dl[i]] = new DLBrain(F(i == 0 ? -4.3f : 4.3f, -1.8f));
        for (int i = 0; i < 3; i++) _live[db[i]] = new DBBrain(wr[i], def, _rng);
        _live[lb] = new LBBrain { rushAfter = defCall == DefCall.Blitz ? 0.05f : 5.0f };
        _live[s] = new SafetyBrain(def, _rng);

        // The pocket (handoff §6): 2–4 s from blocking vs pass rush.
        float edge = Mathf.Clamp01(0.5f + def.passRush - off.blocking);
        _pocketLife = Mathf.Lerp(3.6f, 1.8f, edge) + ((float)_rng.NextDouble() - 0.5f) * 0.8f;
        _engagedScale = Mathf.Lerp(0.12f, 0.38f, edge);
        _ballReceiver = c;
    }

    void BuildKickoff()
    {
        var kick = view.offense; var recv = view.defense;
        _kicker = Find(kick, FootballRole.QB);
        Spot(_kicker, F(0f, isPunt ? -12f : -6f));
        float[] covX = { -22f, -14f, -6f, 6f, 14f, 22f };
        int c = 0;
        foreach (var p in _players)
        {
            if (p.team != kick || p == _kicker) continue;
            Spot(p, F(covX[Mathf.Min(c, covX.Length - 1)] * (isPunt ? 0.5f : 1f), isPunt ? -1f : -2.5f)); c++;
            _live[p] = new CoverageBrain();
        }
        // Kickoffs land around the 10; a punt goes ~40 with hang time, never past the end line.
        float kickDepth = isPunt ? 40f + ((float)_rng.NextDouble() - 0.5f) * 10f : 55f + ((float)_rng.NextDouble() - 0.5f) * 14f;
        float maxDepth = FootballField.ToYards(FootballField.GoalLineZ - view.attackDir * view.losZ) + 4f;
        kickDepth = Mathf.Min(kickDepth, maxDepth);
        _live[_kicker] = new KickerBrain(F(((float)_rng.NextDouble() - 0.5f) * 22f, kickDepth));

        // Receiving team: returner deep (DB1), a wedge at the 35, two up front.
        var returner = Find(recv, FootballRole.DB, 0);
        Spot(returner, F(0f, Mathf.Min(isPunt ? 42f : 60f, maxDepth - 2f)));
        _live[returner] = new ReturnerBrain();
        float[] blkX = { -12f, -4f, 4f, 12f, -7f, 7f };
        float[] blkD = isPunt ? new[] { 2f, 1.5f, 1.5f, 2f, 12f, 12f } : new[] { 32f, 32f, 32f, 32f, 44f, 44f };
        int b = 0;
        foreach (var p in _players)
        {
            if (p.team != recv || p == returner) continue;
            Spot(p, F(blkX[Mathf.Min(b, blkX.Length - 1)], blkD[Mathf.Min(b, blkD.Length - 1)])); b++;
            _live[p] = new ReturnBlockerBrain();
        }
        _ballReceiver = _kicker;
        _ballSpot = F(0f, -6f);
        // The touchdown party (Sam): the scorer and his men line up shoulder to
        // shoulder facing the near stands and dance before the long walk.
        if (_party != null && _party.Count > 0)
        {
            _partyDir = _partyCentre.x >= 0f ? 1f : -1f;
            float x = Mathf.Clamp(_partyCentre.x, -(FootballField.HalfWidth - 6f), FootballField.HalfWidth - 6f);
            float z = Mathf.Clamp(_partyCentre.z, -(FootballField.EndLineZ - 3f), FootballField.EndLineZ - 3f);
            for (int i = 0; i < _party.Count; i++)
            {
                var p = _party[i];
                if (!_setup.TryGetValue(p, out var mv)) continue;
                float off = (i - (_party.Count - 1) * 0.5f) * 1.3f;
                mv.target = new Vector3(x, 0f, z + off);
                mv.SetFace(mv.target + Vector3.right * (_partyDir * 20f));
            }
        }
    }

    FootballPlayer Find(FootballTeam t, FootballRole r, int idx = 0) => view.FindRole(t, r, idx);

    static DefCall PickDefense(float toGo, System.Random rng)
    {
        double r = rng.NextDouble();
        if (toGo <= 3f) return r < 0.40 ? DefCall.Press : r < 0.70 ? DefCall.Normal : DefCall.Blitz;
        if (toGo <= 7f) return r < 0.45 ? DefCall.Normal : r < 0.65 ? DefCall.Off : r < 0.85 ? DefCall.Press : DefCall.Blitz;
        return r < 0.45 ? DefCall.Off : r < 0.75 ? DefCall.Normal : r < 0.85 ? DefCall.Press : DefCall.Blitz;
    }

    /// Clamp a route point inside the field (a go route from the 10 used to
    /// run out the back of the end zone).
    static Vector3 OnField(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, -(FootballField.HalfWidth - 1.5f), FootballField.HalfWidth - 1.5f);
        p.z = Mathf.Clamp(p.z, -(FootballField.EndLineZ - 1.5f), FootballField.EndLineZ - 1.5f);
        return p;
    }

    void Spot(FootballPlayer p, Vector3 fieldPos, bool huddles = true)
    {
        if (p == null) return;
        _formation[p] = fieldPos;
        p.speedScale = 1f;
        p.SetHighlight(false);
        p.settled = false;
        // First the huddle - both sides: a ring facing the man in the middle
        // (the QB calling it; the LB for the defense), 7 yd behind the ball
        // and 6 yd past it - then the formation.
        Vector3 huddle, centre;
        if (view.isKickoff || !huddles) { huddle = fieldPos; centre = fieldPos; }
        else if (p.team == view.offense)
        {
            centre = F(0f, -7.5f);
            if (p.role == FootballRole.QB) huddle = centre;
            else { float ang = (_huddleCount++ * 60f + 30f) * Mathf.Deg2Rad; huddle = F(Mathf.Sin(ang) * 1.7f, -7.5f + Mathf.Cos(ang) * 1.4f); }
        }
        else
        {
            centre = F(0f, 6.5f);
            if (p.role == FootballRole.LB) huddle = centre;
            else { float ang = (_defHuddleCount++ * 60f) * Mathf.Deg2Rad; huddle = F(Mathf.Sin(ang) * 1.7f, 6.5f + Mathf.Cos(ang) * 1.4f); }
        }
        _huddle[p] = huddle;
        var mv = new MoveToBrain(huddle);
        if (view.isKickoff || !huddles) mv.SetFace(LineFacePoint(p, fieldPos));
        else if (huddle != centre) mv.SetFace(centre);
        else mv.SetFace(fieldPos);           // the caller faces the line, men round him
        p.brain = _setup[p] = mv;
    }

    // ── ticking ────────────────────────────────────────────────────────────

    public void Tick(float dt)
    {
        if (phase == Phase.Ended) return;
        _phaseTime += dt;
        switch (phase)
        {
            case Phase.Setup:
            {
                _setupSeconds += dt;
                if (_party != null)
                {
                    bool over = _setupSeconds > PartySeconds;
                    foreach (var p in _party)
                    {
                        if (!_setup.TryGetValue(p, out var mv)) continue;
                        if (over) { mv.target = _formation[p]; mv.SetFace(LineFacePoint(p, _formation[p])); continue; }
                        if (mv.Arrived(p) && !p.IsEmoting) p.Emote(EmoteKind.Dance, PartySeconds - _setupSeconds + 0.2f);
                    }
                    if (over) _party = null;
                }
                TickBallReturn(dt);
                bool all = true;
                foreach (var kv in _setup)
                {
                    var p = kv.Key;
                    p.Tick(view, dt);
                    bool here = kv.Value.Arrived(p);
                    if (p == humanSlot)
                    {
                        // The alien QB walks out; once he is at the bench (or the
                        // huddle breaks) the slot becomes the player.
                        if (!p.humanDriven && (here || _broke)) p.SetHumanDriven(true, _benchSpot);
                        continue;
                    }
                    if (!here || p.IsEmoting) all = false;
                    p.SetStance(here ? StanceFor(p) : Stance.None);
                }
                _othersSet = all;
                // The huddle: once everyone is in it, it HOLDS (the play call, the
                // replay on the screens), then breaks. Or it's taken too long.
                if (!_broke && !view.isKickoff)
                {
                    if (all && _huddleSince < 0f) _huddleSince = _phaseTime;
                    bool held = _huddleSince >= 0f && _phaseTime - _huddleSince >= (hurryUp ? 2f : HuddleHold) && (!holdBreak || hurryUp);
                    // The human's huddle waits for his call and breaks the moment he makes it.
                    if (HumanDriven ? humanPlayChosen : (held || _phaseTime > HuddleTimeout || skipHuddle))
                    {
                        _broke = true; all = false; _phaseTime = 0f;
                        foreach (var kv in _formation)
                            if (_setup.TryGetValue(kv.Key, out var mv) && kv.Key != _ball.holder && kv.Key != _fetcher) { mv.target = kv.Value; mv.SetFace(LineFacePoint(kv.Key, kv.Value)); }
                        log?.Invoke(view.offense.shortName + " break the huddle");
                    }
                }
                if (view.isKickoff) _broke = true;
                // Face the line once there. Never snapped into place: a
                // straggler keeps running and joins the play late. The ball
                // has to be there, though — no snap without a ball.
                if (_broke && !holdSnap && _ballReady && (all || _phaseTime > SetupTimeout) && (!HumanDriven || humanAtSpot))
                {
                    foreach (var p in _players)
                    {
                        bool onOffense = p.team == view.offense;
                        if (p == humanSlot) { if (_live.TryGetValue(p, out var hb)) p.brain = hb; continue; }
                        if (_setup.TryGetValue(p, out var mv) && mv.Arrived(p))
                            p.Face(Vector3.forward * (onOffense ? view.attackDir : -view.attackDir));
                        if (_live.TryGetValue(p, out var brain)) p.brain = brain;
                        p.SetHold(HoldStyle.None);
                    }
                    if (!view.isKickoff)
                    {
                        _centre.SetHold(HoldStyle.SnapStance);
                        _centre.SetBallWorld(_ball.pos);
                        _centre.Face(Vector3.forward * view.attackDir);
                        if (!HumanDriven) _qb.SetHold(HoldStyle.ReadyHands);
                    }
                    else _kicker.SetHold(HoldStyle.TwoHands);
                    _stats.setupSeconds = _setupSeconds;
                    phase = Phase.PreSnap; _phaseTime = 0f;
                }
                _ball.Tick(dt);
                break;
            }
            case Phase.PreSnap:
                _ball.Tick(dt);
                if (_phaseTime >= PreSnapHold) Snap();
                break;
            case Phase.Live:
                TickLive(dt);
                break;
        }
    }

    /// A point well past a lineup spot in the direction that man should face
    /// there: offense at the defense, defense at the line, kick coverage
    /// downfield, the return team at the kicker.
    Vector3 LineFacePoint(FootballPlayer p, Vector3 spot)
    {
        float dir = p.team == view.offense ? view.attackDir : -view.attackDir;
        return spot + Vector3.forward * (dir * 30f);
    }

    /// How a man waits: leaning in in the huddle, then his position's stance
    /// at the line (linemen down, defenders crouched, receivers ready).
    Stance StanceFor(FootballPlayer p)
    {
        if (!_broke && !view.isKickoff)
            return (p.role == FootballRole.QB && p.team == view.offense) || (p.role == FootballRole.LB && p.team == view.defense) ? Stance.None : Stance.Huddle;
        if (view.isKickoff) return p.team == view.offense ? Stance.Ready : Stance.Crouch;
        switch (p.role)
        {
            case FootballRole.OL: case FootballRole.DL: case FootballRole.C: return Stance.Lineman;
            case FootballRole.DB: case FootballRole.LB: case FootballRole.S: return Stance.Crouch;
            case FootballRole.WR: return Stance.Ready;
            default: return Stance.None;
        }
    }

    /// The dead-ball choreography (Sam, 2026-09-19 — "like Madden"): whoever
    /// ended with the ball brings it to the centre; a ball on the grass is
    /// picked up by the nearest man of the offense and brought back; the
    /// centre carries it to the line and puts it down. On a kickoff the
    /// kicker is the man who gets it, and keeps it in hand.
    void TickBallReturn(float dt)
    {
        if (_ballReady || _ballReceiver == null) return;
        var holder = _ball.holder;

        // Last resort: the officials spot it (logged — a soak that shows this
        // has found a bug in the return).
        if (_setupSeconds > OfficialsSpotAfter)
        {
            if (holder != null) holder.SetHold(HoldStyle.None);
            if (view.isKickoff) { _ball.Hold(_kicker); _kicker.SetHold(HoldStyle.TwoHands); }
            else _ball.Place(_ballSpot);
            _fetcher = null;
            _ballReady = true;
            _stats.officialsSpottedBall = true;
            log?.Invoke("The officials spot the ball");
            return;
        }

        if (holder != null && holder.humanDriven)
        {
            // The player: set it down where he stands; the nearest man fetches it.
            holder.SetHold(HoldStyle.None);
            _ball.Place(holder.Pos + holder.Facing * 0.6f);
            return;
        }
        if (holder == _ballReceiver)
        {
            // The centre has it: into the huddle with it, then to the line
            // after the break, and set it down (the kicker keeps his).
            if (view.isKickoff) { _ballReady = true; return; }
            var mv = _setup[holder];
            mv.target = _broke ? _formation[holder] : _huddle[holder]; mv.stopShort = 0f;
            holder.SetHold(HoldStyle.Tucked);
            if (_broke) mv.SetFace(LineFacePoint(holder, _formation[holder]));
            if (_broke && Vector3.Distance(holder.Pos, _formation[holder]) < 0.9f)
            {
                holder.SetHold(HoldStyle.None);
                _ball.Place(_ballSpot);
                holder.Face(Vector3.forward * view.attackDir);
                _ballReady = true;
            }
            return;
        }
        if (holder != null)
        {
            // Somebody else has it: after his moment, bring it to the centre.
            holder.SetHold(HoldStyle.Tucked);
            var mv = _setup[holder];
            mv.target = _ballReceiver.Pos; mv.stopShort = 0.9f; mv.hurry = Vector3.Distance(holder.Pos, _ballReceiver.Pos) > 20f;
            if (!holder.IsEmoting && Vector3.Distance(holder.Pos, _ballReceiver.Pos) < 1.6f)
            {
                holder.SetHold(HoldStyle.None);
                holder.ReachFor(_ballReceiver.Chest);
                _ball.Hold(_ballReceiver);
                _ballReceiver.SetHold(HoldStyle.Tucked);
                mv.target = _broke || view.isKickoff ? _formation[holder] : _huddle[holder]; mv.stopShort = 0f; mv.hurry = false;
                if (!_broke && !view.isKickoff) mv.SetFace(holder.team == view.offense ? F(0f, -7.5f) : F(0f, 6.5f));
                else mv.SetFace(LineFacePoint(holder, _formation[holder]));
                _fetcher = null;
            }
            return;
        }
        // On the grass (or still bouncing).
        if (_ball.state == FootballBall.State.Airborne) return;
        if (!view.isKickoff && Vector3.Distance(_ball.pos, _ballSpot) < 0.6f && Vector3.Distance(_ballReceiver.Pos, _ballSpot) < 1.4f)
        {
            _ballReady = true;                                   // already where it belongs
            return;
        }
        if (_fetcher == null || _fetcher.IsDown)
        {
            // The nearest man of the offense who isn't the centre (Sam:
            // "whoever is on offense and closest to the ball").
            FootballPlayer best = null; float bd = float.MaxValue;
            foreach (var p in _players)
            {
                if (p.team != view.offense || p == _ballReceiver || p.IsDown) continue;
                float d = Vector3.Distance(p.Pos, _ball.pos);
                if (d < bd) { bd = d; best = p; }
            }
            _fetcher = best ?? _ballReceiver;
        }
        var fm = _setup[_fetcher];
        fm.target = _ball.pos; fm.stopShort = 0f; fm.hurry = Vector3.Distance(_fetcher.Pos, _ball.pos) > 20f;
        float dist = Vector3.Distance(_fetcher.Pos, _ball.pos);
        if (dist < 2.2f) _fetcher.ReachFor(_ball.pos);
        if (dist < 0.9f && !_fetcher.IsEmoting)
        {
            _ball.Hold(_fetcher);
            _fetcher.SetHold(HoldStyle.Tucked);
            _fetcher = null;
        }
    }

    void Snap()
    {
        phase = Phase.Live; _phaseTime = 0f;
        view.snapped = true; view.timeSinceSnap = 0f;
        foreach (var p in _players) p.SetStance(Stance.None);
        if (view.isKickoff)
        {
            _lastCarrier = _ball.holder;
            if (_ball.holder != null) _ball.holder.SetHighlight(true);
            return;
        }
        // The centre fires it back to the QB's hands. A wild one every so often.
        Vector3 hands = HumanDriven ? _qb.BallHoldPoint() : _qb.Pos + Vector3.up * 1.05f + _qb.Facing * 0.35f;
        bool wild = _rng.NextDouble() < WildSnapChance;
        float sigma = wild ? 1.3f : 0.10f;
        hands += new Vector3(Gauss() * sigma, Gauss() * sigma * 0.5f, Gauss() * sigma * 0.4f);
        if (wild) { _stats.wildSnaps++; }
        _ball.Snap(hands, SnapFlightSeconds, _qb, _centre);
        _centre.SetHold(HoldStyle.None);
        _lastCarrier = null;
        log?.Invoke((view.play != null ? view.play.name : "Play") + " (" + formation.name + ") vs " + defCall.ToString().ToLower() + " — snap" + (wild ? " — it's high and wide!" : ""));
    }

    void TickLive(float dt)
    {
        view.timeSinceSnap += dt;
        var carrier = view.Carrier;

        // Pocket clock: when it runs out, one rusher comes free.
        if (!view.isKickoff && !_pocketDone && view.timeSinceSnap > _pocketLife
            && (view.play.kind == FootballPlay.Kind.Pass || view.play.kind == FootballPlay.Kind.Rollout))
        {
            _pocketDone = true; view.pocketCollapsed = true;
            var dl = Find(view.defense, FootballRole.DL, _rng.Next(2));
            if (dl != null && dl.brain is DLBrain d) d.free = true;
            if (view.Carrier != null && view.Carrier.role == FootballRole.QB) log?.Invoke("Pressure — " + (dl != null ? dl.Label : "a rusher") + " breaks free");
        }

        // While the snap is in the air the line still fires: block as if the QB had it.
        BlockSlowdown(carrier ?? (view.SnapInFlight ? _qb : null));
        PressReleases();
        foreach (var kv in _slowUntil) if (view.timeSinceSnap < kv.Value) kv.Key.speedScale = Mathf.Min(kv.Key.speedScale, 0.4f);

        // Hands up for a ball that's coming: the man it's for, and whoever is
        // closest to where it comes down. The arms point at it and the catch is
        // tested against those arms (FootballPlayer.ArmGap).
        if (_ball.state == FootballBall.State.Airborne && !_ball.dropped && !_ball.fumbled)
        {
            float leftAir = _ball.catchTime - _ball.airTime;
            if (leftAir < 0.8f)
            {
                var wr = _ball.intendedReceiver;
                if (wr != null && Vector3.Distance(wr.Pos, _ball.pos) < 4f) wr.ReachFor(_ball.pos);
                if (!_ball.isSnap)
                {
                    var d = view.Nearest(_ball.catchPoint, view.defense);
                    if (d != null && Vector3.Distance(d.Pos, _ball.pos) < 3.5f) d.ReachFor(_ball.pos);
                }
            }
        }
        else if (_ball.state == FootballBall.State.Loose || (_ball.state == FootballBall.State.Airborne && _ball.fumbled))
        {
            var n = view.Nearest(_ball.pos, view.defense); var m = view.Nearest(_ball.pos, view.offense);
            if (n != null && Vector3.Distance(n.Pos, _ball.pos) < 2.5f) n.ReachFor(_ball.pos);
            if (m != null && Vector3.Distance(m.Pos, _ball.pos) < 2.5f) m.ReachFor(_ball.pos);
        }

        // The QB's brain says whether he is extending the play (receivers adjust).
        if (!HumanDriven && _qb != null && _qb.brain is QBBrain_CPU qbb)
        {
            bool ext = qbb.Extending && carrier == _qb;
            if (ext && !view.qbExtending) { if (qbb.style == QBBrain_CPU.Style.Rollout) _stats.rollouts++; else _stats.scrambleDrills++; }
            view.qbExtending = ext; view.qbExtendSide = qbb.ExtendSide;
        }

        // Brains → movement, actions collected.
        _prevBallPos = _ball.pos;
        for (int i = 0; i < _players.Count; i++)
        {
            var p = _players[i];
            var o = p.Tick(view, dt);
            if (o.say != null) log?.Invoke(o.say);
            if (o.action != BrainAction.None) DoAction(p, o);
        }
        // Blocking is CONTACT (Sam: the rush was sliding through the line a
        // foot at a time): an engaged rusher is held out at arm's length from
        // his blocker, so he has to work round him or wait to shed.
        foreach (var kv in _engaged)
        {
            var d = kv.Key; var b = kv.Value.blocker;
            if (d.IsDown || b.IsDown) continue;
            Vector3 sep = d.Pos - b.Pos; sep.y = 0f;
            float dist = sep.magnitude;
            if (dist < BlockContact && dist > 0.01f) d.Nudge(b.Pos + sep / dist * BlockContact);
        }
        // The arm has come through: the ball leaves the hand now.
        var holder = _ball.holder;
        if (_throwPending && holder != null && holder.ThrowReleased)
        {
            _throwPending = false;
            if (_throwTo != null) ReleaseThrow(holder, _throwTarget, _throwTo, _throwFlight);
        }
        else if (_throwPending && (holder == null || !holder.IsThrowing)) _throwPending = false;   // sacked mid-motion
        if (_kickPending && holder != null && holder.KickReleased) { _kickPending = false; ReleaseKick(holder, _kickTarget); }
        _ball.Tick(dt);

        ResolveBall();
        if (phase == Phase.Ended) return;

        carrier = view.Carrier;
        if (carrier != _lastCarrier)
        {
            if (_lastCarrier != null) { _lastCarrier.SetHighlight(false); _lastCarrier.SetHold(HoldStyle.None); }
            if (carrier != null)
            {
                carrier.SetHighlight(true);
                // The QB holds it in two hands until he tucks it; a catch is
                // gathered in two hands then tucked (below); a runner tucks.
                bool qbHolding = carrier == _qb && !view.isKickoff && !_qbTucked;
                carrier.SetHold(qbHolding || carrier == _catcher ? HoldStyle.TwoHands : HoldStyle.Tucked);
            }
            _lastCarrier = carrier;
        }
        if (carrier != null && carrier == _catcher && carrier.Hold == HoldStyle.TwoHands && view.timeSinceSnap >= _catchDipUntil && carrier != _qb)
            carrier.SetHold(HoldStyle.Tucked);

        if (carrier != null)
        {
            // Touchdown: the carrier's own goal line.
            if (carrier.Pos.z * carrier.team.attackDir >= FootballField.GoalLineZ)
            {
                End(Outcome.Touchdown, carrier.Pos.z, carrier.team, carrier);
                return;
            }
            if (Mathf.Abs(carrier.Pos.x) > FootballField.HalfWidth)
            {
                End(Outcome.OutOfBounds, carrier.Pos.z, carrier.team, carrier);
                return;
            }
            if (view.snapped && (!view.isKickoff || carrier != _kicker))
            {
                // Tackled: he falls forward for a moment, then the ball is
                // spotted where it IS (Sam: not where he was first touched).
                if (_tackleEndAt >= 0f)
                {
                    TickWrap(carrier);
                    if (_tackleEndAt >= 0f && view.timeSinceSnap >= _tackleEndAt) { FinishTackle(carrier); return; }
                }
                else if (TickTackles(carrier)) return;
            }
        }

        if (view.timeSinceSnap > LiveTimeout)
        {
            if (carrier != null) End(Outcome.Whistle, carrier.Pos.z, carrier.team, carrier);
            else End(Outcome.Incomplete, view.losZ, view.offense, null);
        }
    }

    /// Every opponent on the carrier: a standing lunge inside arm's reach, or
    /// a DIVE from further out. Dives are committed — the carrier can hurdle
    /// one, and a clipped hurdle is a tumble with the ball loose sometimes.
    /// Returns true if the play ended.
    bool TickTackles(FootballPlayer carrier)
    {
        float ts = view.timeSinceSnap;
        for (int i = 0; i < _players.Count; i++)
        {
            var d = _players[i];
            if (d.team == carrier.team || d.IsDown) continue;
            float dist = Vector3.Distance(d.Pos, carrier.Pos);

            if (d.IsDiving)
            {
                if (_diveResolved.Contains(d)) continue;
                float ph = d.DivePhase;
                if (ph < 0.3f || ph > 0.85f) continue;
                if (dist >= DiveReach) continue;
                _diveResolved.Add(d);
                if (carrier.IsHurdling)
                {
                    float hp = carrier.HurdlePhase;
                    float clip = hp < 0.12f || hp > 0.9f ? HurdleLateClipChance : HurdleClipChance;
                    if (_rng.NextDouble() >= clip)
                    {
                        log?.Invoke(carrier.team.shortName + " " + carrier.Label + " HURDLES " + d.Label + "!");
                        continue;                                        // clean: the diver hits the grass alone
                    }
                    // Clipped: a tumble, and the ball may come out.
                    _stats.hurdlesClipped++; _stats.diveHits++;
                    d.FallDown(MissDownSeconds);
                    carrier.HardFall(HardFallDownSeconds);
                    if (_rng.NextDouble() < ClipFumbleChance)
                    {
                        log?.Invoke(carrier.team.shortName + " " + carrier.Label + " is clipped mid-hurdle — goes down hard and the BALL IS LOOSE!");
                        Fumble(carrier, d);
                        return false;
                    }
                    log?.Invoke(carrier.team.shortName + " " + carrier.Label + " is clipped mid-hurdle and goes down hard");
                    EndTackle(carrier, d);
                    return true;
                }
                // A dive that connects: he has him by the legs.
                _stats.diveHits++;
                if (_rng.NextDouble() < BigHitFumbleChance)
                {
                    d.FallDown(TackleDownSeconds);
                    carrier.FallDown(TackleDownSeconds);
                    log?.Invoke(d.team.shortName + " " + d.Label + " lays him out — the ball comes loose!");
                    Fumble(carrier, d);
                    return false;
                }
                d.FallDown(TackleDownSeconds);
                EndTackle(carrier, d);
                return true;
            }

            if (_tackleRetry.TryGetValue(d, out float until) && ts < until) continue;
            // A blocked man only gets an arm out; you can run past him.
            bool blocked = d.speedScale < 0.5f;
            float reach = blocked ? TackleRadius * 0.55f : TackleRadius;
            Vector3 toD = d.Pos - carrier.Pos; toD.y = 0f;

            if (dist >= reach)
            {
                // Too far for a lunge: dive? Only a free man closing at speed.
                if (blocked || dist < DiveMinDist || dist > DiveMaxDist) continue;
                if (_diveConsider.TryGetValue(d, out float next) && ts < next) continue;
                _diveConsider[d] = ts + 0.22f;
                // Closing speed: how fast the gap shrinks (+ = he's getting there).
                float closing = Vector3.Dot(carrier.Vel - d.Vel, toD.normalized);
                if (closing < 2.5f) continue;
                if (_rng.NextDouble() >= DiveChance) continue;
                Vector3 aim = (carrier.Pos + carrier.Vel * 0.25f) - d.Pos; aim.y = 0f;
                d.StartDive(aim);
                _tackleRetry[d] = ts + 1.6f;
                _stats.dives++;
                continue;
            }
            bool fromBehind = carrier.Vel.sqrMagnitude > 4f && Vector3.Dot(toD.normalized, carrier.Vel.normalized) < -0.6f;
            float miss = blocked ? MissedTackleBlocked : fromBehind ? MissedTackleBehind : MissedTackleChance;
            if (carrier.IsJuking && carrier.JukePhase < 0.7f) miss += MissedTackleJuked;
            if (carrier.IsSpinning) miss += MissedTackleSpun;
            bool stiff = carrier.IsStiffArming && Vector3.Dot(toD.normalized, carrier.StiffArmDir) > 0.5f;
            if (stiff) { miss += 0.3f; d.Nudge(d.Pos + toD.normalized * 0.5f); }
            miss = Mathf.Min(miss, stiff ? 0.7f : MaxMissChance);
            if (_rng.NextDouble() < miss)
            {
                d.FallDown(MissDownSeconds);                                                    // a missed lunge: forward, past him
                _tackleRetry[d] = ts + TackleRetry;
                // He got a hand on him: a stumble sometimes.
                if (!stiff && _rng.NextDouble() < 0.4) { carrier.StartStumble(); _stats.stumbles++; }
                string how = stiff ? " stiff-arms " : carrier.IsSpinning ? " spins out of " : carrier.IsJuking ? " jukes past " : " misses the tackle by ";
                log?.Invoke(stiff || carrier.IsSpinning || carrier.IsJuking
                    ? carrier.team.shortName + " " + carrier.Label + how + d.Label + "!"
                    : d.team.shortName + " " + d.Label + " misses the tackle!");
                continue;
            }
            // Hurdling into a standing man: a big hit, the ball can come out.
            if (carrier.IsHurdling || (fromBehind == false && carrier.Vel.magnitude > 6f && _rng.NextDouble() < BigHitFumbleChance * 0.6f))
            {
                if (_rng.NextDouble() < BigHitFumbleChance * 2f)
                {
                    d.FallDown(TackleDownSeconds); carrier.FallDown(TackleDownSeconds);
                    log?.Invoke(d.team.shortName + " " + d.Label + " meets him in the air — the ball pops out!");
                    Fumble(carrier, d);
                    return false;
                }
            }
            // The wrap: arms round him, dragged for a stride, then down.
            EndTackle(carrier, d);
            return true;
        }
        return false;
    }

    /// The hit: the tackler wraps him, the two of them drag for a stride
    /// (the runner driving, forward progress), anyone else arriving piles on,
    /// and then they all go down — unless the runner breaks it.
    void EndTackle(FootballPlayer carrier, FootballPlayer d)
    {
        _pendingTackler = d;
        _wrappers.Clear(); _wrappers.Add(d);
        d.wrapping = carrier;
        _tackleEndAt = view.timeSinceSnap + WrapSeconds;
    }

    /// During the wrap: the carrier is slowed and the wrappers ride him;
    /// a second man within reach joins; a strong runner may break it.
    void TickWrap(FootballPlayer carrier)
    {
        carrier.speedScale = Mathf.Min(carrier.speedScale, 0.4f);
        for (int i = 0; i < _players.Count; i++)
        {
            var d = _players[i];
            if (d.team == carrier.team || d.IsDown || _wrappers.Contains(d)) continue;
            if (Vector3.Distance(d.Pos, carrier.Pos) < 1.4f) { _wrappers.Add(d); d.wrapping = carrier; log?.Invoke(d.team.shortName + " " + d.Label + " piles on"); }
        }
        foreach (var w in _wrappers)
        {
            Vector3 behind = carrier.Pos - carrier.Facing * 0.85f;
            w.Nudge(Vector3.Lerp(w.Pos, behind, 0.35f));
            w.speedScale = 0f;
        }
        // Breaking it: one man on him, a strong runner, a roll of the dice.
        if (_wrappers.Count == 1 && view.timeSinceSnap > _tackleEndAt - WrapSeconds * 0.5f && _rng.NextDouble() < BreakTackleChance * (0.6f + 0.8f * carrier.team.speed) / 20f)
        {
            var d = _wrappers[0];
            d.wrapping = null; d.FallDown(MissDownSeconds);
            _tackleRetry[d] = view.timeSinceSnap + TackleRetry;
            _wrappers.Clear(); _pendingTackler = null; _tackleEndAt = -1f;
            _stats.brokenTackles++;
            log?.Invoke(carrier.team.shortName + " " + carrier.Label + " BREAKS THE TACKLE of " + d.Label + "!");
        }
    }

    void FinishTackle(FootballPlayer carrier)
    {
        var d = _pendingTackler; _pendingTackler = null; _tackleEndAt = -1f;
        if (d != null) carrier.FallDown(TackleDownSeconds, d.Pos); else carrier.FallDown(TackleDownSeconds);
        foreach (var w in _wrappers) { w.wrapping = null; w.FallDown(TackleDownSeconds); }      // the tackler goes down forward, onto him
        _wrappers.Clear();
        float spotZ = _ball.holder == carrier ? _ball.pos.z : carrier.Pos.z;
        bool sack = !view.isKickoff && carrier.role == FootballRole.QB && carrier.team == view.offense
                    && (spotZ - view.losZ) * view.attackDir < 0f && !(view.play.kind == FootballPlay.Kind.QbDraw && view.timeSinceSnap > 1.5f)
                    && view.play.kind != FootballPlay.Kind.QbRun;
        End(sack ? Outcome.Sack : (view.isKickoff ? Outcome.KickReturn : Outcome.Run), spotZ, carrier.team, carrier, d);
    }

    /// The ball comes out of `carrier`'s arms, knocked by `by`. Live on the ground.
    void Fumble(FootballPlayer carrier, FootballPlayer by)
    {
        _stats.fumbles++;
        _fumbler = carrier;
        Vector3 knock = carrier.Pos - by.Pos; knock.y = 0f;
        knock = (knock.sqrMagnitude > 0.01f ? knock.normalized : carrier.Facing) * 2.0f
                + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * 2.5f;
        carrier.SetHold(HoldStyle.None);
        carrier.SetHighlight(false);
        foreach (var w in _wrappers) w.wrapping = null;
        _wrappers.Clear(); _pendingTackler = null; _tackleEndAt = -1f;
        _ball.pos = carrier.BallHoldPoint();
        _ball.Fumble(carrier.Vel, knock);
        _looseSince = -1f;
        _lastCarrier = null;
    }

    /// Debug: blow the play dead where it stands.
    public void Whistle()
    {
        if (phase == Phase.Ended) return;
        var carrier = view.Carrier;
        if (view.isKickoff) { End(Outcome.Touchback, 0f, view.defense, null); return; }
        if (carrier != null && view.snapped) End(Outcome.Whistle, carrier.Pos.z, carrier.team, carrier);
        else End(Outcome.Incomplete, view.losZ, view.offense, null);
    }

    /// A corner in his face at the snap: the receiver's release. He wins it
    /// (a swim past him, the corner jammed for a beat) or loses it (jammed
    /// himself, the route a step late). Decided once, in the first half second.
    void PressReleases()
    {
        if (_releasesDone || view.isKickoff || view.timeSinceSnap < 0.15f) return;
        _releasesDone = true;
        for (int i = 0; i < 3; i++)
        {
            var wr = Find(view.offense, FootballRole.WR, i);
            var db = Find(view.defense, FootballRole.DB, i);
            if (wr == null || db == null) continue;
            Vector3 rel = db.Pos - wr.Pos; rel.y = 0f;
            if (rel.magnitude > 2.4f || view.Downfield(db.Pos) < view.Downfield(wr.Pos)) continue;
            float win = 0.5f + 0.35f * (wr.team.speed - db.team.coverage);
            if (_rng.NextDouble() < win)
            {
                float side = Vector3.Dot(rel, Vector3.right) > 0f ? -1f : 1f;
                wr.StartJuke(Vector3.right * side);
                _slowUntil[db] = view.timeSinceSnap + 0.4f;
                log?.Invoke(wr.team.shortName + " " + wr.Label + " swims past the press");
            }
            else
            {
                _slowUntil[wr] = view.timeSinceSnap + 0.45f;
                log?.Invoke(db.team.shortName + " " + db.Label + " jams " + wr.Label + " at the line");
            }
        }
    }

    /// The whole blocking model (handoff §6), no contact: a defender who runs
    /// into a blocker is ENGAGED — held to a shove — until he sheds him, which
    /// takes 1–2 s depending on the blocking stat (longer for a lineman on a
    /// lineman). Once shed, that blocker can't hold him again this play. The
    /// pocket timer's freed rusher ignores it all. Sticky, with hysteresis, so
    /// a blocker doesn't have to be geometrically perfect every frame.
    void BlockSlowdown(FootballPlayer carrier)
    {
        for (int i = 0; i < _players.Count; i++) { _players[i].speedScale = 1f; _players[i].blocking = false; }
        if (carrier == null) return;
        float ts = view.timeSinceSnap;
        carrier.speedScale = carrier == _catcher && ts < _catchDipUntil ? CatchDipSpeed
                           : carrier.role == FootballRole.QB ? 0.97f : CarrierSpeed;   // a scrambling QB isn't slowed by the ball
        for (int i = 0; i < _players.Count; i++)
        {
            var d = _players[i];
            if (d.team == carrier.team) continue;
            if (d.brain is DLBrain dl && dl.free) { _engaged.Remove(d); continue; }
            Vector3 toCarrier = carrier.Pos - d.Pos; toCarrier.y = 0f;
            if (toCarrier.sqrMagnitude < 0.01f) continue;
            toCarrier.Normalize();

            // Still on his current blocker?
            if (_engaged.TryGetValue(d, out var e))
            {
                float dist = Vector3.Distance(e.blocker.Pos, d.Pos);
                // Still held: close, not shed, and the blocker still BETWEEN
                // him and the ball. Once he is round the man he is free (he
                // used to crawl on for a stride after getting past).
                Vector3 toBlk = e.blocker.Pos - d.Pos; toBlk.y = 0f;
                bool inFront = toBlk.sqrMagnitude < 0.01f || Vector3.Dot(toBlk.normalized, toCarrier) > -0.15f;
                if (dist < EngageKeep && ts < e.until && e.blocker != carrier && !e.blocker.IsDown && inFront)
                {
                    d.speedScale = e.scale; d.blocking = true; e.blocker.blocking = true;
                    e.blocker.FaceHint(d.Pos - e.blocker.Pos); d.FaceHint(e.blocker.Pos - d.Pos);
                    continue;
                }
                _shed.Add((d, e.blocker));
                _engaged.Remove(d);
            }
            // A new blocker in his way?
            for (int j = 0; j < _players.Count; j++)
            {
                var b = _players[j];
                if (b.team != carrier.team || b == carrier || b.IsDown) continue;
                if (_shed.Contains((d, b))) continue;
                Vector3 toB = b.Pos - d.Pos; toB.y = 0f;
                float dist = toB.magnitude;
                if (dist > EngageStart || dist < 0.01f) continue;
                if (Vector3.Dot(toB / dist, toCarrier) < 0.1f) continue;
                bool line = d.role == FootballRole.DL && (b.role == FootballRole.OL || b.role == FootballRole.C);
                // Linemen lock up for a couple of seconds; a receiver blocking
                // downfield only gets a shove in — pursuit has to be able to close.
                float hold = line ? Mathf.Lerp(0.9f, 2.0f, b.team.blocking) * 1.6f + ((float)_rng.NextDouble() - 0.5f) * 0.4f
                                  : Mathf.Lerp(0.45f, 0.9f, b.team.blocking) + ((float)_rng.NextDouble() - 0.5f) * 0.2f;
                var eng = new Engagement { blocker = b, until = ts + hold, scale = line ? _engagedScale : 0.5f };
                _engaged[d] = eng;
                d.speedScale = eng.scale; d.blocking = true; b.blocking = true;
                break;
            }
        }
    }

    struct Engagement { public FootballPlayer blocker; public float until; public float scale; }
    const float EngageStart = 1.6f, EngageKeep = 2.1f;
    const float BlockContact = 1.0f;      // a blocker and his man never get closer than this while engaged
    readonly Dictionary<FootballPlayer, Engagement> _engaged = new Dictionary<FootballPlayer, Engagement>();
    readonly Dictionary<FootballPlayer, float> _tackleRetry = new Dictionary<FootballPlayer, float>();
    readonly Dictionary<FootballPlayer, float> _diveConsider = new Dictionary<FootballPlayer, float>();
    readonly HashSet<FootballPlayer> _diveResolved = new HashSet<FootballPlayer>();
    readonly HashSet<(FootballPlayer, FootballPlayer)> _shed = new HashSet<(FootballPlayer, FootballPlayer)>();

    void DoAction(FootballPlayer p, BrainOutput o)
    {
        if (o.action == BrainAction.Dive) { if (!p.IsDiving && !p.IsDown) { p.StartDive(o.target); _stats.dives++; } return; }
        if (_ball.holder != p) return;      // only the man with the ball acts on it
        switch (o.action)
        {
            case BrainAction.Throw:
            {
                if (o.targetPlayer == null || _throwPending) return;
                bool pitch = view.play != null && view.play.kind == FootballPlay.Kind.FleaFlicker && p.role == FootballRole.WR;
                if (p.HasRig && !pitch && !p.humanDriven)
                {
                    // Wind up; ReleaseThrow fires when the arm comes through.
                    _throwPending = true; _throwTarget = o.target; _throwTo = o.targetPlayer; _throwFlight = o.flight;
                    Vector3 dir = o.target - p.Pos; dir.y = 0f;
                    p.BeginThrow(dir);
                    return;
                }
                ReleaseThrow(p, o.target, o.targetPlayer, o.flight);
                break;
            }
            case BrainAction.Handoff:
            {
                var to = o.targetPlayer;
                if (to == null) return;
                if (to != p)
                {
                    p.SetHold(HoldStyle.None);
                    _ball.Hold(to);
                    log?.Invoke(p.team.shortName + " " + p.Label + " hands to " + to.Label);
                }
                else if (p == _qb) _qbTucked = true;
                to.SetHold(HoldStyle.Tucked);
                view.handoffTime = view.timeSinceSnap;
                TakePossession(to);
                break;
            }
            case BrainAction.Kick:
            {
                if (_kickPending) return;
                if (p.HasRig) { _kickPending = true; _kickTarget = o.target; p.BeginKick(); return; }
                ReleaseKick(p, o.target);
                break;
            }
            case BrainAction.Juke:
                if (!p.IsJuking) { p.StartJuke(o.target); _stats.jukes++; }
                break;
            case BrainAction.Spin:
                if (!p.IsSpinning) { p.StartSpin(); _stats.spins++; }
                break;
            case BrainAction.Hurdle:
                if (!p.IsHurdling) { p.StartHurdle(); _stats.hurdles++; }
                break;
            case BrainAction.StiffArm:
                if (!p.IsStiffArming) { p.StartStiffArm(o.target); _stats.stiffArms++; }
                break;
        }
    }

    void ReleaseThrow(FootballPlayer p, Vector3 target, FootballPlayer to, float flightOverride = 0f)
    {
        // Flight time: never flatter than a 3 m apex, so the ball goes over
        // the men between the passer and the catch (1.2 s ≈ an NFL
        // 15-yarder); longer throws take dist / speed.
        float dist = Vector3.Distance(_ball.pos, target);
        float flight = flightOverride > 0f ? flightOverride : PassFlightTime(dist);      // the human's throw comes with its own arc
        _ball.Launch(target, flight, to, false);
        _passer = p; _target = to;
        if (Mathf.Abs(target.x) > FootballField.HalfWidth) _thrownAway = true;
        p.SetHighlight(false);
        p.SetHold(HoldStyle.None);
        log?.Invoke(p.team.shortName + " " + p.Label + " throws to " + to.Label);
    }

    void ReleaseKick(FootballPlayer p, Vector3 target)
    {
        _ball.Launch(target, KickFlightTime, null, true);
        p.SetHighlight(false);
        p.SetHold(HoldStyle.None);
        log?.Invoke("Kickoff — " + p.team.shortName + " boots it");
    }

    /// The slot now has the ball: give it the carrier brain unless its brain
    /// drives the ball itself (Phase 2 human QB, the kicker).
    void TakePossession(FootballPlayer p)
    {
        if (p.brain != null && p.brain.KeepsControlWhenCarrying) return;
        List<Vector3> opening = (p.brain as WRBrain)?.Remaining();
        if (view.play != null && view.play.kind == FootballPlay.Kind.FleaFlicker && p.role == FootballRole.WR && p.roleIndex == view.play.sweep)
        {
            p.brain = new FleaFlickerBrain(_qb, opening ?? new List<Vector3>(), _rng, p.team.speed);
            return;
        }
        // A receiver's remaining route is for a sweep; a catch downfield just runs.
        if (!(view.play != null && (view.play.kind == FootballPlay.Kind.JetSweep || view.play.kind == FootballPlay.Kind.Screen) && p.role == FootballRole.WR)) opening = null;
        p.brain = new BallCarrierBrain(opening, _rng, p.team.speed);
    }

    void ResolveBall()
    {
        switch (_ball.state)
        {
            case FootballBall.State.Airborne:
            {
                if (_ball.fumbled && LooseBallOut(true)) return;
                if (_ball.dropped || _ball.fumbled) return;     // falling to the grass; nobody can have it in the air
                if (_ball.airTime < (_ball.isSnap ? 0.05f : 0.3f)) return;
                // Go up for it: the intended man and the nearest defender to
                // the spot, just before it gets there.
                float left = _ball.catchTime - _ball.airTime;
                if (!_ball.isKick && !_ball.isSnap && left > 0.02f)
                {
                    // Judged on where the ball WILL be a jump's rise from now, not on
                    // standing at the catch spot (Sam: a wide-open man let a high ball
                    // sail over him because he was still a step short of the spot).
                    float ahead = Mathf.Min(0.28f, left);
                    Vector3 future = _ball.pos + _ball.vel * ahead + Vector3.down * (0.5f * FootballBall.Gravity * ahead * ahead);
                    var wr = _ball.intendedReceiver;
                    PlayTheBall(wr, future, ahead, left);
                    var db = view.Nearest(future, view.defense);
                    PlayTheBall(db, future, ahead, left);
                    foreach (var q in _players)
                        if (q != wr && q != db && q.team == view.offense && Vector3.Distance(q.Pos, future) < 3f) PlayTheBall(q, future, ahead, left);
                }

                // The ball has to touch someone's arms (Sam). Whoever it
                // came closest to, if it came within reach at all. Two men on
                // it is a CONTESTED ball: the better position usually wins, and
                // the loser may get a hand in and tip it up — live.
                FootballPlayer best = null; float bd = 0f;
                FootballPlayer second = null; float sd = 0f;
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (p == _ball.thrower || p.IsDown) continue;
                    bool lowHands = true;
                    if (_ball.isSnap)
                    {
                        if (p != _ball.intendedReceiver) continue;          // only the QB fields a snap
                        lowHands = false;
                    }
                    else if (!_ball.isKick)
                    {
                        // Linemen are ineligible; a rusher only gets a hand on a
                        // ball that's had time to clear the line. The man it
                        // was thrown to is stretched out for it; everyone else
                        // is reacting.
                        if (p.role == FootballRole.OL || p.role == FootballRole.C) continue;
                        if (p.role == FootballRole.DL && _ball.airTime < 0.5f) continue;
                        lowHands = p != _ball.intendedReceiver;
                    }
                    float gap = p == humanSlot ? _ball.TouchDistance(p.BallHoldPoint(), _prevBallPos) - 1.6f : p.ArmGap(_ball, _prevBallPos, lowHands);
                    if (gap <= 0f && gap < bd) { second = best; sd = bd; bd = gap; best = p; }
                    else if (gap <= 0f && gap < sd) { sd = gap; second = p; }
                }
                if (best == null) return;
                bool kick = _ball.isKick;
                if (!kick && !_ball.isSnap && second != null && second.team != best.team)
                {
                    // Contested: position decides most of it, luck the rest.
                    _stats.contested++;
                    float edge = Mathf.Clamp01(0.62f + (sd - bd) * 0.5f);
                    if (_rng.NextDouble() > edge) { var t = best; best = second; second = t; }
                    if (_rng.NextDouble() < TipChance)
                    {
                        // The other man gets a hand in: the ball pops up, anyone's.
                        _stats.tips++;
                        Vector3 up = _ball.pos;
                        Vector3 to = up + new Vector3(((float)_rng.NextDouble() - 0.5f) * 4f, 0f, ((float)_rng.NextDouble() - 0.5f) * 4f);
                        _ball.Launch(to, 0.9f, null, false);
                        _ball.thrower = null;
                        log?.Invoke("Tipped by " + second.team.shortName + " " + second.Label + " — the ball is up for grabs!");
                        return;
                    }
                    log?.Invoke(best.team.shortName + " " + best.Label + " wins the contested ball over " + second.Label);
                }
                var wasIntendedTeam = kick ? view.defense : view.offense;

                if (_ball.isSnap)
                {
                    // Caught the snap: two hands, eyes up. His brain stays.
                    _stats.snapsCaught++;
                    _ball.Hold(best);
                    best.SetHold(HoldStyle.TwoHands);
                    return;
                }

                // The pitch back on a flea flicker: the QB has it again, brain intact.
                if (!kick && best == _qb && view.play != null && view.play.kind == FootballPlay.Kind.FleaFlicker)
                {
                    _ball.Hold(best);
                    best.SetHold(HoldStyle.TwoHands);
                    view.handoffTime = -10f;                              // the defense sees a pass again
                    _passer = null; _target = null;
                    log?.Invoke(best.team.shortName + " QB takes the pitch — looking deep");
                    return;
                }
                // Got a hand on it — does he hold it?
                if (!kick)
                {
                    float dropChance = _ball.intendedReceiver == null ? 0.25f : best == _ball.intendedReceiver ? ReceiverDropChance : DefenderDropChance;
                    if (best.team != wasIntendedTeam && best.IsDiving) dropChance = 0.8f;      // laid out for it: a swat, rarely a pick
                    if (_rng.NextDouble() < dropChance)
                    {
                        Vector3 side = new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * 3f;
                        _ball.Drop(side);
                        log?.Invoke(best.team.shortName + " " + best.Label +
                                    (best.team == wasIntendedTeam ? " drops it" : " gets a hand on it — knocked down"));
                        _breakupBy = best.team == wasIntendedTeam ? null : best;
                        return;                                   // it falls; the ground ends the play
                    }
                }
                _ball.Hold(best);
                _catcher = best; _catchDipUntil = view.timeSinceSnap + CatchDipSeconds;
                if (kick)
                {
                    // Fielded in his own end zone: touchback.
                    if (best.team == view.defense && best.Pos.z * best.team.attackDir <= -FootballField.GoalLineZ)
                    {
                        End(Outcome.Touchback, 0f, best.team, best);
                        return;
                    }
                    if (best.team == view.offense) { log?.Invoke(best.team.shortName + " recovers its own kick!"); TakePossession(best); return; }
                    log?.Invoke(best.team.shortName + " " + best.Label + " fields the kick");
                    TakePossession(best);
                    return;
                }
                if (best.team != wasIntendedTeam)
                {
                    log?.Invoke("INTERCEPTED by " + best.team.shortName + " " + best.Label + "!");
                    _interceptor = best;
                }
                else log?.Invoke(best.team.shortName + " " + best.Label + " makes the catch");
                TakePossession(best);
                return;
            }
            case FootballBall.State.Loose:
            {
                if (_looseSince < 0f) _looseSince = view.timeSinceSnap;
                bool fumble = _ball.fumbled || (!_ball.isKick && !view.isKickoff);
                if (_ball.isSnap) _badSnap = true;
                if (LooseBallOut(fumble)) return;
                // Rolling into the end zone = touchback (kicks) / dead (fumbles).
                if (_ball.pos.z * view.defense.attackDir <= -FootballField.GoalLineZ && !fumble)
                {
                    End(Outcome.Touchback, 0f, view.defense, null);
                    return;
                }
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (p.IsDown && !fumble) continue;
                    if (Vector3.Distance(p.Pos, _ball.pos) >= 1.0f) continue;
                    if (fumble)
                    {
                        // A fumble: fall on it (dead there) or scoop and run.
                        _recoverer = p;
                        bool scoop = !p.IsDown && _rng.NextDouble() < ScoopChance;
                        _ball.Hold(p);
                        p.SetHold(HoldStyle.Tucked);
                        if (p.team != view.offense) _stats.fumblesLost++;
                        if (scoop)
                        {
                            log?.Invoke(p.team.shortName + " " + p.Label + " scoops it up and runs!");
                            _looseSince = -1f;
                            TakePossession(p);
                            return;
                        }
                        p.FallDown(1.4f);
                        log?.Invoke(p.team.shortName + " " + p.Label + " falls on the ball");
                        End(Outcome.Fumble, p.Pos.z, p.team, p);
                        return;
                    }
                    _ball.Hold(p);
                    p.SetHold(HoldStyle.Tucked);
                    if (p.team == view.offense) log?.Invoke(p.team.shortName + " " + p.Label + " falls on the kick!");
                    else log?.Invoke(p.team.shortName + " " + p.Label + " scoops up the kick");
                    TakePossession(p);
                    return;
                }
                if (view.timeSinceSnap - _looseSince > LooseBallTimeout)
                {
                    if (fumble) { End(Outcome.Fumble, _ball.pos.z, _fumbler != null ? _fumbler.team : view.offense, null); return; }
                    End(Outcome.KickReturn, _ball.pos.z, view.defense, null);
                }
                return;
            }
            case FootballBall.State.Dead:
            {
                if (!view.snapped) return;
                if (view.isKickoff) { End(Outcome.Touchback, 0f, view.defense, null); return; }
                End(Outcome.Incomplete, view.losZ, view.offense, null);
                return;
            }
        }
    }
    FootballPlayer _breakupBy;

    /// A ball on the ground (or a fumble still bouncing) that crosses a
    /// sideline or an end line is dead there — nobody can pick it up from
    /// out of bounds (Sam saw the defense do exactly that). A fumble goes
    /// back to the team that lost it at the spot; a kick out of bounds is
    /// the receiving team's ball at their 40.
    bool LooseBallOut(bool fumble)
    {
        bool outside = Mathf.Abs(_ball.pos.x) > FootballField.HalfWidth || Mathf.Abs(_ball.pos.z) > FootballField.EndLineZ;
        if (!outside) return false;
        if (fumble)
        {
            var team = _fumbler != null ? _fumbler.team : view.offense;
            log?.Invoke("Ball out of bounds — " + team.shortName + " keep it");
            End(Outcome.Fumble, _ball.pos.z, team, null);
            return true;
        }
        float z40 = view.defense.attackDir * (40f * FootballField.MetresPerYard - FootballField.GoalLineZ);
        log?.Invoke("Kickoff out of bounds — " + view.defense.shortName + " ball at the 40");
        _ball.Place(new Vector3(0f, 0f, z40));
        End(Outcome.KickReturn, z40, view.defense, null);
        return true;
    }
    bool _badSnap, _thrownAway;

    /// After the whistle nobody freezes: bodies coast to a stop, the fallen
    /// get up, a ball in the air comes down. FootballMatch calls this if it
    /// ever holds an ended play instead of building the next one.
    public void TickDead(float dt)
    {
        if (phase != Phase.Ended) return;
        for (int i = 0; i < _players.Count; i++) _players[i].Tick(view, dt);
        _ball.Tick(dt);
    }

    /// Go up or lay out for a ball in the air. `future` is where the ball will
    /// be in `ahead` seconds (a jump's rise). Standing hands reach about 2.2 m;
    /// a jump adds 0.7. A ball coming down short or wide of him with no time
    /// left to run there gets a dive (the dive's reach is tested by ArmGap on
    /// the posed shoulders like any other catch).
    void PlayTheBall(FootballPlayer p, Vector3 future, float ahead, float left)
    {
        if (p == null || p.IsDown || p.IsJumping || p.IsDiving || p.IsHurdling) return;
        if (p.role == FootballRole.OL || p.role == FootballRole.C || p.role == FootballRole.DL) return;
        Vector3 me = p.Pos + p.Vel * ahead;
        float h = new Vector2(future.x - me.x, future.z - me.z).magnitude;
        if (future.y > 2.0f && future.y < 3.1f && h < 1.4f) { p.Jump(); return; }
        if (left < 0.5f && future.y < 1.4f && h > 1.4f && h < 2.9f) { p.StartDive(future - p.Pos); _stats.dives++; }
    }

    /// Human QB: the play he picked in the huddle. Receivers, their routes and
    /// the men covering them are laid out again for it; the huddle then breaks.
    public void ChoosePlay(FootballPlay play, FootballFormation form)
    {
        if (view.isKickoff || _broke) return;
        view.play = play; formation = form ?? FootballFormation.Pick(play, _rng);
        view.readDelay = 0.4f;
        var off = view.offense; var def = view.defense;
        var wr = new[] { Find(off, FootballRole.WR, 0), Find(off, FootballRole.WR, 1), Find(off, FootballRole.WR, 2) };
        var db = new[] { Find(def, FootballRole.DB, 0), Find(def, FootballRole.DB, 1), Find(def, FootballRole.DB, 2) };
        var s = Find(def, FootballRole.S);
        float[] wrX = formation.wrX;
        float dbDepth = defCall == DefCall.Press || defCall == DefCall.Blitz ? 1.3f : defCall == DefCall.Off ? 7f : 3.5f;
        float sDepth = defCall == DefCall.Off ? 15f : defCall == DefCall.Normal ? 12f : 10f;
        float shade = 0f;
        for (int i = 0; i < 3; i++)
        {
            if (wr[i] != null) _formation[wr[i]] = F(wrX[i], i == 2 ? -1.2f : -0.8f);
            float x = Mathf.Clamp(wrX[i], -(FootballField.HalfWidth - 1.5f), FootballField.HalfWidth - 1.5f);
            if (db[i] != null) _formation[db[i]] = F(x, dbDepth + (i == 2 ? 1.2f : 0f));
            shade += wrX[i];
            var route = new List<Vector3>();
            float sideward = Mathf.Sign(wrX[i]);
            foreach (var wp in play.routes[i].points) route.Add(OnField(F(wrX[i] + wp.x * sideward, wp.y)));
            if (wr[i] != null) _live[wr[i]] = new WRBrain(route, play.routes[i].settle);
            if (db[i] != null) _live[db[i]] = new DBBrain(wr[i], def, _rng);
        }
        if (s != null) _formation[s] = F(shade / 3f * 0.3f, sDepth);
        humanPlayChosen = true;
        log?.Invoke(off.shortName + " call " + play.name + " (" + formation.name + ")");
    }

    /// Test key: throw the play away with no result (the match re-spots the ball).
    public void Abort()
    {
        if (phase == Phase.Ended) return;
        phase = Phase.Ended;
        view.snapped = false; view.qbExtending = false;
        _throwPending = false; _kickPending = false;
        foreach (var p in _players)
        {
            p.SetHighlight(false); p.speedScale = 1f; p.brain = null; p.settled = false; p.SetStance(Stance.None); p.wrapping = null;
            if (p.IsThrowing) p.CancelThrow();
            if (p != _ball.holder) p.SetHold(HoldStyle.None);
        }
        if (_ball.holder != null) _ball.holder.SetHold(HoldStyle.Tucked);
        if (_ball.state == FootballBall.State.Airborne) _ball.Place(new Vector3(_ball.pos.x, 0f, _ball.pos.z));
        log?.Invoke("Play skipped");
    }

    /// Test key: a kickoff or punt ends now as a touchback (25 / 20).
    public void SkipKickoff()
    {
        if (!view.isKickoff || phase == Phase.Ended) return;
        if (_ball.state == FootballBall.State.Airborne) _ball.Place(new Vector3(_ball.pos.x, 0f, _ball.pos.z));
        log?.Invoke((isPunt ? "Punt" : "Kickoff") + " skipped");
        End(Outcome.Touchback, 0f, view.defense, null);
    }

    void End(Outcome outcome, float spotZ, FootballTeam possession, FootballPlayer carrier, FootballPlayer tackler = null)
    {
        phase = Phase.Ended;
        view.snapped = false;
        view.qbExtending = false;
        _throwPending = false; _kickPending = false;
        foreach (var p in _players)
        {
            p.SetHighlight(false); p.speedScale = 1f; p.brain = null; p.settled = false; p.SetStance(Stance.None); p.wrapping = null;
            if (p.IsThrowing) p.CancelThrow();
            if (p != _ball.holder) p.SetHold(HoldStyle.None);
        }
        if (_ball.holder != null) _ball.holder.SetHold(HoldStyle.Tucked);      // he carries it back

        var r = new PlayResult
        {
            outcome = outcome, play = view.play, isKickoff = view.isKickoff, possession = possession,
            carrier = carrier, passer = _passer, receiver = _target, tackler = tackler, stats = _stats,
        };
        // Clamp the spot inside the goal lines (no safeties in Phase 1).
        float gl = FootballField.GoalLineZ - 0.5f;
        spotZ = Mathf.Clamp(spotZ, -gl, gl);

        if (outcome == Outcome.Touchdown)
        {
            r.touchdown = true;
            r.turnover = possession != view.offense && !view.isKickoff;
            r.endSpotZ = spotZ;
            r.yards = view.isKickoff ? 0f : (possession == view.offense ? FootballField.ToYards((spotZ - view.losZ) * view.attackDir) : 0f);
            r.description = "TOUCHDOWN " + possession.name + "!" + (carrier != null ? " (" + carrier.Label + ")" : "");
            r.clockStops = true;
        }
        else if (view.isKickoff)
        {
            r.turnover = possession == view.offense;     // kicking team kept it
            r.clockStops = true;
            if (outcome == Outcome.Touchback)
            {
                r.endSpotZ = view.defense.attackDir * (25f * FootballField.MetresPerYard - FootballField.GoalLineZ);
                r.description = (isPunt ? "Punt touchback — " : "Touchback — ") + view.defense.shortName + " ball at the " + (isPunt ? "20" : "25");
                if (isPunt) r.endSpotZ = view.defense.attackDir * (20f * FootballField.MetresPerYard - FootballField.GoalLineZ);
                r.possession = view.defense;
            }
            else if (possession == view.offense)
            {
                r.endSpotZ = spotZ;
                r.description = view.offense.shortName + " recovers the kickoff at the " + YardLine(spotZ);
                r.outcome = Outcome.KickRecoveredByKickingTeam;
            }
            else
            {
                r.endSpotZ = spotZ;
                float ret = FootballField.ToYards((spotZ - (_ball.landPoint.z)) * view.defense.attackDir);
                r.returnYards = Mathf.Max(0f, ret);
                r.description = (isPunt ? "Punt returned to the " : "Kick returned to the ") + YardLine(spotZ) + (tackler != null ? " (" + tackler.Label + " on the tackle)" : "");
            }
        }
        else if (outcome == Outcome.Fumble || (_fumbler != null && possession != view.offense))
        {
            r.outcome = Outcome.Fumble;
            r.turnover = possession != view.offense;
            r.endSpotZ = spotZ;
            r.yards = r.turnover ? 0f : FootballField.ToYards((spotZ - view.losZ) * view.attackDir);
            r.clockStops = r.turnover;
            string who = _fumbler != null ? _fumbler.Label : "the carrier";
            r.description = r.turnover
                ? "FUMBLE" + (_badSnap ? " on a bad snap" : " by " + who) + " — " + possession.shortName + " recover at the " + YardLine(spotZ)
                : (_badSnap ? "Bad snap, " : "Fumble by " + who + ", ") + possession.shortName + " recover their own ball at the " + YardLine(spotZ);
            r.firstDown = !r.turnover && r.yards >= _toGo - 0.01f;
        }
        else if (_interceptor != null && possession == view.defense)
        {
            r.outcome = Outcome.Interception;
            r.turnover = true;
            r.clockStops = true;
            r.endSpotZ = spotZ;
            r.yards = 0f;
            r.description = "Intercepted by " + possession.shortName + " " + _interceptor.Label + ", down at the " + YardLine(spotZ);
        }
        else
        {
            r.endSpotZ = outcome == Outcome.Incomplete ? view.losZ : spotZ;
            r.yards = FootballField.ToYards((r.endSpotZ - view.losZ) * view.attackDir);
            r.firstDown = r.yards >= _toGo - 0.01f && outcome != Outcome.Incomplete;
            r.clockStops = outcome == Outcome.Incomplete || outcome == Outcome.OutOfBounds;
            string yd = r.yards >= 0f ? "+" + r.yards.ToString("0") : r.yards.ToString("0");
            switch (outcome)
            {
                case Outcome.Incomplete: r.description = (_thrownAway ? "Thrown away" : "Incomplete" + (r.receiver != null ? " to " + r.receiver.Label : "")) + (_breakupBy != null ? " (broken up by " + _breakupBy.Label + ")" : ""); break;
                case Outcome.Sack:       r.description = "SACKED for " + yd + (tackler != null ? " by " + tackler.Label : ""); break;
                case Outcome.OutOfBounds: r.description = (carrier != null ? carrier.Label : "Runner") + " out of bounds, " + yd; break;
                case Outcome.Whistle:    r.description = "Whistle — dead ball, " + yd; break;
                default:
                    bool pass = _passer != null && carrier != null && carrier != _passer;
                    r.outcome = pass ? Outcome.Complete : Outcome.Run;
                    r.description = (pass ? "Complete to " : "Run by ") + (carrier != null ? carrier.Label : "?") + ", " + yd
                                    + (tackler != null ? " (tackled by " + tackler.Label + ")" : "");
                    break;
            }
        }
        if (_ball.state == FootballBall.State.Loose) _ball.Place(_ball.pos);   // a rolling ball stops where it is
        Celebrate(r, carrier, tackler);
        r.stats = _stats;
        result = r;
        Ended?.Invoke(r);
    }

    /// Madden moments (Sam, 2026-09-19): the man who made the play gets one.
    void Celebrate(PlayResult r, FootballPlayer carrier, FootballPlayer tackler)
    {
        // A tackled man queues his (FootballPlayer.Emote): he plays it as he gets up.
        void E(FootballPlayer p, EmoteKind k, float s) { if (p == null) return; p.Emote(k, s); _stats.emotes++; }
        if (r.touchdown && carrier != null)
        {
            // The scorer isn't down (he crossed the line running): arms up, or a point.
            int pick = _rng.Next(4);
            E(carrier, pick == 0 ? EmoteKind.Point : pick == 1 ? EmoteKind.Bow : pick == 2 ? EmoteKind.ChestThump : EmoteKind.ArmsUp, 2.6f);
            foreach (var p in _players)
                if (p != carrier && p.team == carrier.team && Vector3.Distance(p.Pos, carrier.Pos) < 18f)
                    E(p, _rng.Next(2) == 0 ? EmoteKind.ArmsUp : EmoteKind.Flex, 2.0f);
            return;
        }
        if (r.isKickoff) return;
        switch (r.outcome)
        {
            case Outcome.Interception:
                E(_interceptor, _rng.Next(2) == 0 ? EmoteKind.ArmsUp : EmoteKind.Point, 2.4f);
                E(_passer, EmoteKind.Dejected, 2.2f);
                break;
            case Outcome.Fumble:
                if (r.turnover) { E(_recoverer, EmoteKind.ArmsUp, 2.4f); E(_fumbler, EmoteKind.Dejected, 2.2f); }
                break;
            case Outcome.Sack:
                E(tackler, _rng.Next(2) == 0 ? EmoteKind.Flex : EmoteKind.ChestThump, 2.2f);
                break;
            case Outcome.Incomplete:
            {
                // The corner on the man it was thrown to: arms crossed. Every time (Sam).
                var db = _breakupBy ?? (_target != null ? view.Nearest(_target.Pos, view.defense) : null);
                if (db != null && !_thrownAway) E(db, EmoteKind.NoFlyZone, 2.8f);
                if (_target != null && !_thrownAway && _rng.Next(2) == 0) E(_target, EmoteKind.Dejected, 1.4f);
                break;
            }
            case Outcome.Complete:
            case Outcome.Run:
            case Outcome.OutOfBounds:
                // Tackled men are on the ground: the celebration is the getting
                // up. Out of bounds or a first down on his feet: the signal.
                if (r.firstDown) E(carrier, r.yards >= 20f ? EmoteKind.ChestThump : EmoteKind.FirstDown, 1.9f);
                else if (r.yards < 0f) E(tackler, EmoteKind.ChestThump, 1.8f);
                break;
        }
    }

    float Gauss()
    {
        double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    static string YardLine(float z) => Mathf.RoundToInt(FootballField.YardLineLabel(z)).ToString();
}
