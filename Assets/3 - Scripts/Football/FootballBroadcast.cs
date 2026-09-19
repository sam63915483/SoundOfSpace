using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;

/// <summary>
/// The big screens (Sam, 2026-09-19): "2 big jumbo screens that show the game
/// live, stay focused on the ball and zoom in and out … then after a big play
/// show the replay and slow down right before the catch".
///
/// One broadcast camera high on the home sideline renders to a texture that
/// two jumbotron quads display (one over each end zone; positions are fields
/// on this component — move them there). It frames the ball: wide before the
/// snap, on the QB in the pocket, tighter on a runner, and on a throw it
/// fits both the ball and where it's going. After a play worth seeing it
/// plays the REPLAY on the screens: every player's pose was recorded at
/// 30 Hz (FootballPlayer.CapturePose), so a second set of "ghost" aliens on
/// their own layer re-enacts the play while the live men huddle. The replay
/// runs at full speed, drops to quarter speed a second before the key moment
/// (the catch, the hurdle, the fumble, the tackle), and comes back up.
///
/// No layers (the game's camera code owns the player camera's mask and
/// resets it — a layer trick made the aliens flicker for Sam). Instead the
/// ghosts' renderers are switched on only while THIS camera renders, and the
/// live men's switched off during a replay, in Camera.onPreCull /
/// onPostRender. No other camera ever sees a ghost or misses a live man.
///
/// FootballMatch.Boot creates one of these under FieldRoot in play mode if
/// the scene has none. Nothing here touches the sim.
///
/// Debug: if `build/football_dump.txt` exists in the project, its content is a
/// folder and the screen's picture is saved there every `dumpEvery` seconds
/// (so a session without eyes can look at the game).
/// </summary>
public class FootballBroadcast : MonoBehaviour
{
    [Header("Screens (FieldRoot-local)")]
    // Sam: centred on the 50, one on each sideline, facing the field (a
    // Quad's face is −Z; Y 90 turns that face toward −X).
    public Vector3 screenAPos = new Vector3(64f, 30f, 0f);
    public Vector3 screenAEuler = new Vector3(0f, 90f, 0f);
    public Vector3 screenBPos = new Vector3(-64f, 30f, 0f);
    public Vector3 screenBEuler = new Vector3(0f, -90f, 0f);
    public Vector2 screenSize = new Vector2(40f, 22.5f);
    public int textureWidth = 960, textureHeight = 540;

    [Header("Camera (FieldRoot-local)")]
    public float cameraX = -58f;         // the home sideline, high in the stands
    public float cameraHeight = 26f;
    [Tooltip("How much the camera slides along the sideline with the ball (0 = fixed at the 50, 1 = always level with it).")]
    public float cameraFollow = 0.55f;
    public float fovWide = 20f, fovPocket = 13f, fovRunner = 9f, fovDead = 10f;
    public float focusSmooth = 0.25f, fovSmooth = 0.4f, slideSmooth = 0.7f;

    [Header("Replay (after every play)")]
    public bool replays = true;
    public float replayDelay = 1.6f;     // live celebration first, then the card, then the replay
    [Tooltip("Speed from the throw until a second after the catch (runs: around the key moment).")]
    public float slowMoSpeed = 0.5f;
    [Tooltip("Seconds to ease into and out of the slow-mo instead of snapping to it.")]
    public float slowMoEase = 0.5f;
    public float slowMoBefore = 1.0f, slowMoAfter = 0.6f, slowMoAfterCatch = 1.0f;
    public float maxReplaySeconds = 13f;
    [Tooltip("Keep recording this long after the whistle: the hit, then the pile.")]
    public float postWhistleSeconds = 3f;
    [Tooltip("Replay picture is this much tighter than the live one (1 = same).")]
    public float replayZoom = 0.8f;
    public float slowMoFov = 8f;
    [Header("The card (what happened, slid across the screen before the replay)")]
    public float cardSlide = 0.35f, cardHold = 1.5f;

    [Header("Debug")]
    public float dumpEvery = 2f;

    const float RecordHz = 30f;
    const int MaxFrames = 30 * 40;

    struct Frame
    {
        public float t;
        public Vector3 ballPos; public Quaternion ballRot; public bool ballHeld;
        public FootballPlayer.PoseFrame[] poses;
        public Vector3 focus, second; public bool hasSecond; public float fov;   // the camera's intent, replayed as recorded
    }

    class Ghost { public Transform root, body; public FootballAlienRig rig; }

    FootballMatch _match;
    Transform _fieldRoot;
    Camera _cam;
    RenderTexture _rt;
    readonly List<Renderer> _screens = new List<Renderer>();
    readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
    Material _screenMat;

    // Live framing state.
    Vector3 _focus, _focusVel;
    float _fov = 30f, _fovVel;
    float _slideZ, _slideVel;
    float _deadSince = -1f; Vector3 _deadFocus;

    // Recording.
    readonly List<Frame> _frames = new List<Frame>();
    float _recordAcc, _recT;                  // _recT: the recording clock (sim seconds since it started)
    PlayInstance _recordingPlay, _lastLivePlay, _endedPlay; float _endedAt = -1f;
    bool _recording;
    float _keyTime = -1f; int _keyRank;
    float _throwTime = -1f, _catchTime = -1f;
    float _postWhistle = -1f;
    FootballPlayer _lastHolder; bool _wasAir;
    readonly Dictionary<FootballPlayer, bool> _wasHurdling = new Dictionary<FootballPlayer, bool>();
    readonly Dictionary<FootballPlayer, bool> _wasSpinning = new Dictionary<FootballPlayer, bool>();
    readonly Dictionary<FootballPlayer, bool> _wasDown = new Dictionary<FootballPlayer, bool>();

    // Replay playback.
    bool _replaying; float _replayT, _replayStartedAt; float _replayStart, _replayEnd, _replayKey, _slowStart, _slowEnd, _slowSpeed; float _replayDueAt = -1f;
    string _cardText; float _cardT = -1f; bool _cardDone;
    readonly List<Transform> _cards = new List<Transform>();
    readonly List<Transform> _cardSlabs = new List<Transform>();
    readonly List<TextMeshPro> _cardTexts = new List<TextMeshPro>();
    readonly List<Renderer> _screenParts = new List<Renderer>();
    List<Frame> _replayFrames;
    readonly List<Ghost> _ghosts = new List<Ghost>();
    Transform _ghostBall;
    float _clock;
    string _dumpDir; float _dumpAcc; int _dumpN;

    public bool Replaying => _replaying;

    void Start()
    {
        _match = FootballMatch.Instance;
        if (_match == null) { enabled = false; return; }
        _fieldRoot = transform.parent != null ? transform.parent : transform;
        BuildCamera();
        BuildScreens();
        _match.Stepped += OnStepped;
        _match.PlayEnded += OnPlayEnded;
        Camera.onPreCull += OnCamPreCull;
        Camera.onPostRender += OnCamPostRender;
        string flag = Path.Combine(Application.dataPath, "../build/football_dump.txt");
        if (File.Exists(flag))
        {
            _dumpDir = File.ReadAllText(flag).Trim();
            if (_dumpDir.Length > 0) { Directory.CreateDirectory(_dumpDir); Debug.Log("[Broadcast] dumping frames to " + _dumpDir); }
        }
    }

    void OnDestroy()
    {
        if (_match != null) { _match.Stepped -= OnStepped; _match.PlayEnded -= OnPlayEnded; }
        Camera.onPreCull -= OnCamPreCull;
        Camera.onPostRender -= OnCamPostRender;
        if (_rt != null) _rt.Release();
    }

    // ── building ───────────────────────────────────────────────────────────

    void BuildCamera()
    {
        _rt = new RenderTexture(textureWidth, textureHeight, 24) { name = "BroadcastRT" };
        var go = new GameObject("BroadcastCam");
        go.transform.SetParent(_fieldRoot, false);
        _cam = go.AddComponent<Camera>();
        _cam.targetTexture = _rt;
        _cam.fieldOfView = fovWide;
        _cam.nearClipPlane = 0.5f; _cam.farClipPlane = 600f;
        _cam.depth = -20f;                                       // renders before the player's camera
        var listener = go.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);
        _focus = Vector3.zero; _slideZ = 0f;
        PlaceCamera();
    }

    void BuildScreens()
    {
        var sh = Shader.Find("Unlit/Texture");
        _screenMat = new Material(sh != null ? sh : Shader.Find("Standard")) { name = "Jumbotron", mainTexture = _rt };
        MakeScreen("Jumbotron A", screenAPos, screenAEuler);
        MakeScreen("Jumbotron B", screenBPos, screenBEuler);
    }

    void MakeScreen(string name, Vector3 pos, Vector3 euler)
    {
        var root = new GameObject(name);
        root.transform.SetParent(_fieldRoot, false);
        root.transform.localPosition = pos; root.transform.localRotation = Quaternion.Euler(euler);
        // The bezel, then the picture just proud of it.
        var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(bezel.GetComponent<Collider>());
        bezel.name = "Bezel";
        bezel.transform.SetParent(root.transform, false);
        bezel.transform.localScale = new Vector3(screenSize.x + 1.2f, screenSize.y + 1.2f, 0.8f);
        var bm = new Material(Shader.Find("Standard")) { color = new Color(0.05f, 0.05f, 0.06f) };
        bm.SetFloat("_Glossiness", 0.2f);
        bezel.GetComponent<Renderer>().sharedMaterial = bm;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Picture";
        quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, -0.45f);
        quad.transform.localScale = new Vector3(screenSize.x, screenSize.y, 1f);
        var r = quad.GetComponent<Renderer>();
        r.sharedMaterial = _screenMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _screens.Add(r);
        _screenParts.Add(r); _screenParts.Add(bezel.GetComponent<Renderer>());
        // The card: a black slab with the play's result on it, slid across
        // the picture before the replay.
        var card = new GameObject("Card");
        card.transform.SetParent(root.transform, false);
        card.transform.localPosition = new Vector3(0f, 0f, -0.6f);
        var slab = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(slab.GetComponent<Collider>());
        slab.name = "Slab";
        slab.transform.SetParent(card.transform, false);
        slab.transform.localScale = new Vector3(screenSize.x, screenSize.y, 1f);
        _cardSlabs.Add(slab.transform);
        var sm = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.03f, 0.03f, 0.05f) };
        var slabR = slab.GetComponent<Renderer>(); slabR.sharedMaterial = sm; slabR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _screenParts.Add(slabR);
        var ct = new GameObject("Text");
        ct.transform.SetParent(card.transform, false);
        ct.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        var ctm = ct.AddComponent<TextMeshPro>();
        ctm.text = ""; ctm.fontSize = screenSize.y * 1.6f; ctm.fontStyle = FontStyles.Bold;
        ctm.color = new Color(1f, 0.85f, 0.3f);
        ctm.alignment = TextAlignmentOptions.Center; ctm.enableWordWrapping = true;
        ctm.GetComponent<RectTransform>().sizeDelta = new Vector2(screenSize.x * 0.92f, screenSize.y * 0.9f);
        _cards.Add(card.transform); _cardTexts.Add(ctm);
        card.SetActive(false);
        // The REPLAY tag in the corner.
        var lbl = new GameObject("Tag");
        lbl.transform.SetParent(root.transform, false);
        // Top right for the viewer (local −X is his right on a −Z face; the text is flipped to read).
        lbl.transform.localPosition = new Vector3(-screenSize.x * 0.5f + screenSize.x * 0.16f, screenSize.y * 0.5f - screenSize.y * 0.09f, -0.55f);
        // TMP at identity reads from −Z, the picture's front (same as the scoreboard's labels).
        var tmp = lbl.AddComponent<TextMeshPro>();
        tmp.text = "REPLAY";
        tmp.fontSize = screenSize.y * 0.55f; tmp.color = new Color(1f, 0.25f, 0.2f); tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Right;
        tmp.enableWordWrapping = false;
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(screenSize.x * 0.3f, screenSize.y * 0.15f);
        tmp.enabled = false;
        _labels.Add(tmp);
    }

    // ── who is drawn by which camera ───────────────────────────────────────

    readonly List<Renderer> _liveRenderers = new List<Renderer>();
    readonly List<Renderer> _ghostRenderers = new List<Renderer>();
    int _liveCount = -1;

    void CollectLive()
    {
        var players = _match.Players;
        if (_liveCount == players.Count) return;
        _liveRenderers.Clear();
        foreach (var p in players) _liveRenderers.AddRange(p.GetComponentsInChildren<Renderer>(true));
        var play = _match.CurrentPlay;
        if (play != null && play.view.ball != null) _liveRenderers.AddRange(play.view.ball.GetComponentsInChildren<Renderer>(true));
        _liveCount = players.Count;
    }

    /// Only while the broadcast camera renders: ghosts on, and during a
    /// replay the live men off.
    void OnCamPreCull(Camera c)
    {
        if (c != _cam) return;
        foreach (var r in _screenParts) if (r != null) r.enabled = false;      // no screen-within-screen
        if (!_replaying) return;
        CollectLive();
        foreach (var r in _liveRenderers) if (r != null) r.enabled = false;
        foreach (var r in _ghostRenderers) if (r != null) r.enabled = true;
    }

    void OnCamPostRender(Camera c)
    {
        if (c != _cam) return;
        foreach (var r in _screenParts) if (r != null) r.enabled = true;
        if (!_replaying) return;
        foreach (var r in _liveRenderers) if (r != null) r.enabled = true;
        foreach (var r in _ghostRenderers) if (r != null) r.enabled = false;
    }

    // ── per frame ──────────────────────────────────────────────────────────

    void Update()
    {
        if (_match == null || _cam == null) return;
        float dt = Time.deltaTime;
        _clock += dt;

        if (_cardT >= 0f) TickCard(dt);
        if (_replaying) TickReplay(dt);
        else
        {
            if (_replayDueAt >= 0f && _clock >= _replayDueAt) { _replayDueAt = -1f; ShowCard(); }
            else TickLive(dt);
        }
        if (_dumpDir != null)
        {
            _dumpAcc += dt;
            if (_dumpAcc >= dumpEvery) { _dumpAcc = 0f; StartCoroutine(Dump()); }
        }
    }

    /// Where the camera wants to look right now, from the live play.
    void Intent(out Vector3 focus, out Vector3 second, out bool hasSecond, out float fov)
        => Intent(_match.CurrentPlay, out focus, out second, out hasSecond, out fov);

    /// The same for a given play (the recorder asks about the play it is
    /// recording — the match has already built the next one by the whistle).
    void Intent(PlayInstance play, out Vector3 focus, out Vector3 second, out bool hasSecond, out float fov)
    {
        second = Vector3.zero; hasSecond = false;
        if (play == null) { focus = Vector3.zero; fov = fovWide; return; }
        var v = play.view; var ball = v.ball;
        var carrier = v.Carrier;
        if (play.phase == PlayInstance.Phase.Ended)
        {
            // Hold on whoever ended it for the celebration.
            if (_deadSince < 0f) { _deadSince = _clock; _deadFocus = carrier != null ? carrier.Pos : ball.pos; }
            focus = carrier != null ? carrier.Pos : _deadFocus;
            fov = fovDead;
            return;
        }
        _deadSince = -1f;
        if (play.phase != PlayInstance.Phase.Live)
        {
            // In the huddle: in on the offense's ring. Otherwise wide on the line.
            if (play.InHuddle) { focus = new Vector3(0f, 0f, v.losZ - v.attackDir * 7.5f); fov = fovPocket; return; }
            focus = new Vector3(0f, 0f, v.losZ - v.attackDir * 3f);
            fov = fovWide;
            return;
        }
        if (v.BallAirborne && !ball.isSnap && !ball.fumbled)
        {
            focus = Vector3.Lerp(ball.pos, ball.catchPoint, 0.5f);
            second = ball.catchPoint; hasSecond = true;
            fov = FitFov(ball.pos, ball.catchPoint, 1.35f);
            return;
        }
        if (carrier != null)
        {
            bool qbPocket = carrier.role == FootballRole.QB && v.Downfield(carrier.Pos) < 0.5f && !v.qbExtending;
            focus = carrier.Pos + carrier.Vel * 0.3f;
            fov = qbPocket ? fovPocket : fovRunner;
            return;
        }
        focus = ball.pos; fov = fovPocket;
    }

    void TickLive(float dt)
    {
        // For a couple of seconds after the whistle stay on the play that just
        // ended (the celebration), not the next one's line.
        var cur = _match.CurrentPlay;
        var show = cur;
        if (_endedPlay != null && _endedPlay != cur && _endedPlay.phase == PlayInstance.Phase.Ended)
        {
            if (_endedAt < 0f) _endedAt = _clock;
            if (_clock - _endedAt < 2.5f) show = _endedPlay; else { _endedPlay = null; _endedAt = -1f; }
        }
        Intent(show, out var focus, out var second, out var hasSecond, out var fov);
        AimAt(focus, fov, dt);
        SetReplayLook(false);
    }

    /// Smooth the camera toward an intent and place it.
    void AimAt(Vector3 focus, float fov, float dt)
    {
        _focus = Vector3.SmoothDamp(_focus, focus, ref _focusVel, focusSmooth, Mathf.Infinity, dt);
        _fov = Mathf.SmoothDamp(_fov, fov, ref _fovVel, fovSmooth, Mathf.Infinity, dt);
        _slideZ = Mathf.SmoothDamp(_slideZ, focus.z * cameraFollow, ref _slideVel, slideSmooth, Mathf.Infinity, dt);
        PlaceCamera();
    }

    void PlaceCamera()
    {
        Vector3 camLocal = new Vector3(cameraX, cameraHeight, _slideZ);
        _cam.transform.localPosition = camLocal;
        Vector3 look = _focus + Vector3.up * 1.0f;
        Vector3 dir = look - camLocal;
        _cam.transform.localRotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward, Vector3.up);
        _cam.fieldOfView = _fov;
    }

    /// The vertical FOV that shows both points from where the camera sits.
    float FitFov(Vector3 a, Vector3 b, float margin)
    {
        Vector3 camLocal = new Vector3(cameraX, cameraHeight, _slideZ);
        Vector3 mid = Vector3.Lerp(a, b, 0.5f) + Vector3.up;
        float ang = Vector3.Angle(a + Vector3.up - camLocal, b + Vector3.up - camLocal);
        float aspect = (float)textureWidth / textureHeight;
        float fov = Mathf.Clamp(ang * margin / aspect + 6f, fovRunner, fovWide + 4f);
        return fov;
    }

    // ── recording ──────────────────────────────────────────────────────────

    void OnStepped(float dt)
    {
        var play = _match.CurrentPlay;
        if (play == null) return;
        if (play != _lastLivePlay && play.phase == PlayInstance.Phase.Setup && _recordingPlay != null && _recordingPlay != play) _endedPlay = _recordingPlay;
        _lastLivePlay = play;
        // A new play: start a fresh recording at its pre-snap.
        if (play != _recordingPlay && play.phase != PlayInstance.Phase.Ended && (play.phase == PlayInstance.Phase.PreSnap || play.phase == PlayInstance.Phase.Live))
        {
            _recordingPlay = play; _frames.Clear(); _recording = true; _recordAcc = 0f; _recT = 0f;
            _keyTime = -1f; _keyRank = 0; _postWhistle = -1f; _lastHolder = null; _wasAir = false; _throwTime = -1f; _catchTime = -1f;
            _wasHurdling.Clear(); _wasSpinning.Clear(); _wasDown.Clear();
        }
        if (!_recording || play != _recordingPlay) return;
        var v = play.view;
        // (Stamping frames with the last frame's time + one physics step
        // compressed the recording 2x: fast-forward replays, no slow-mo.)
        _recT += dt;
        float t = _recT;
        // Events → the key moment (rank: catch 4, hurdle/spin 3, fumble 3, tackle 2).
        var holder = v.ball.holder;
        bool air = v.BallAirborne && !v.ball.isSnap;
        if (!_wasAir && air && !v.ball.fumbled && _throwTime < 0f) _throwTime = t;                 // the ball leaves the hand
        if (_wasAir && holder != null && holder != v.ball.thrower) { Key(t, 4); _catchTime = t; }
        if (v.ball.fumbled && holder == null && _lastHolder != null) Key(t, 3);
        for (int i = 0; i < v.players.Count; i++)
        {
            var p = v.players[i];
            bool h = p.IsHurdling, sp = p.IsSpinning, d = p.IsDown;
            if (h && !(_wasHurdling.TryGetValue(p, out var wh) && wh) && holder == p) Key(t + 0.25f, 3);
            if (sp && !(_wasSpinning.TryGetValue(p, out var ws) && ws) && holder == p) Key(t + 0.25f, 3);
            if (d && !(_wasDown.TryGetValue(p, out var wd) && wd) && holder == p) Key(t, 2);
            _wasHurdling[p] = h; _wasSpinning[p] = sp; _wasDown[p] = d;
        }
        _wasAir = air; _lastHolder = holder ?? _lastHolder;

        if (play.phase == PlayInstance.Phase.Ended)
        {
            if (_postWhistle < 0f) _postWhistle = t;
            if (t - _postWhistle > postWhistleSeconds) { _recording = false; return; }
        }
        _recordAcc += dt;
        if (_recordAcc < 1f / RecordHz) return;
        _recordAcc -= 1f / RecordHz;
        if (_frames.Count >= MaxFrames) return;
        var f = new Frame { t = t, ballPos = v.ball.pos, ballRot = v.ball.transform.localRotation, ballHeld = holder != null, poses = new FootballPlayer.PoseFrame[v.players.Count] };
        for (int i = 0; i < v.players.Count; i++) f.poses[i] = v.players[i].CapturePose();
        Intent(play, out f.focus, out f.second, out f.hasSecond, out f.fov);
        _frames.Add(f);
    }

    void Key(float t, int rank)
    {
        if (rank >= _keyRank) { _keyRank = rank; _keyTime = t; }
    }

    /// Every play gets a replay (Sam: there is a huddle after every play, so
    /// why not) — and a card that says what happened, for anyone who does
    /// not know football.
    void OnPlayEnded(PlayInstance.PlayResult r)
    {
        if (!replays) return;
        _cardText = CardText(r, _match.Down);
        _replayDueAt = _clock + replayDelay;
    }

    static string CardText(PlayInstance.PlayResult r, int down)
    {
        string yd = Mathf.RoundToInt(Mathf.Abs(r.yards)).ToString();
        string sign = r.yards >= 0f ? "+" : "-";
        string extra = "";
        if (r.stats.hurdlesClipped > 0) extra = "\nCLIPPED ON THE HURDLE";
        else if (r.stats.hurdles > 0) extra = "\nHURDLE!";
        else if (r.stats.spins > 0) extra = "\nSPIN MOVE";
        else if (r.stats.jukes > 0 && r.yards >= 8f) extra = "\nJUKED HIM";
        if (r.isKickoff)
        {
            if (r.touchdown) return "KICK RETURN\nTOUCHDOWN!";
            if (r.outcome == PlayInstance.Outcome.Touchback) return "TOUCHBACK";
            return "KICK RETURNED\n" + Mathf.RoundToInt(r.returnYards) + " YARDS" + extra;
        }
        bool pass = r.passer != null && r.carrier != null && r.carrier != r.passer;
        if (r.touchdown)
        {
            if (r.turnover) return "TURNOVER - RETURNED\nFOR A TOUCHDOWN!";
            return sign + yd + " YARD " + (pass ? "TOUCHDOWN PASS" : "TOUCHDOWN RUN") + "!";
        }
        switch (r.outcome)
        {
            case PlayInstance.Outcome.Interception: return "INTERCEPTED!\n" + r.possession.shortName + " BALL";
            case PlayInstance.Outcome.Fumble: return r.turnover ? "FUMBLE!\n" + r.possession.shortName + " RECOVER" : "FUMBLE\nRECOVERED BY THE OFFENSE";
            case PlayInstance.Outcome.Sack: return "SACKED\n" + sign + yd + " YARDS";
            case PlayInstance.Outcome.Incomplete:
                return (r.description != null && r.description.Contains("broken up") ? "PASS BROKEN UP" : "INCOMPLETE") + (down >= 4 ? "\nTURNOVER ON DOWNS" : "");
        }
        string what = sign + yd + " YARD " + (pass ? "CATCH" : "RUN");
        if (r.outcome == PlayInstance.Outcome.OutOfBounds) what += "\nOUT OF BOUNDS";
        if (r.firstDown) what += "\nFIRST DOWN";
        else if (down >= 4) what += "\nSTOPPED - TURNOVER ON DOWNS";
        else if (r.yards < 0f) what += "\nSTOPPED BEHIND THE LINE";
        return what + extra;
    }

    // ── the card ───────────────────────────────────────────────────────────

    void ShowCard()
    {
        if (_frames.Count < 10) { _cardT = -1f; return; }
        _cardT = 0f; _cardDone = false;
        for (int i = 0; i < _cards.Count; i++) { _cards[i].gameObject.SetActive(true); _cardTexts[i].text = _cardText; }
    }

    /// A wipe: the slab grows in from one edge of the picture, holds with
    /// the text on it, shrinks out the other edge — never outside the screen
    /// (a slab sliding past the bezel was a black box in the air). The
    /// replay starts as it leaves.
    void TickCard(float dt)
    {
        _cardT += dt;
        float W = screenSize.x;
        float width, centre; bool showText;
        if (_cardT < cardSlide) { float k = Mathf.SmoothStep(0f, 1f, _cardT / cardSlide); width = W * k; centre = -(W - width) * 0.5f; showText = false; }
        else if (_cardT < cardSlide + cardHold) { width = W; centre = 0f; showText = true; }
        else { float k = Mathf.SmoothStep(0f, 1f, (_cardT - cardSlide - cardHold) / cardSlide); width = W * (1f - k); centre = (W - width) * 0.5f; showText = false; }
        for (int i = 0; i < _cardSlabs.Count; i++)
        {
            _cardSlabs[i].localPosition = new Vector3(centre, 0f, 0f);
            _cardSlabs[i].localScale = new Vector3(Mathf.Max(0.01f, width), screenSize.y, 1f);
            _cardTexts[i].enabled = showText;
        }
        if (!_cardDone && _cardT >= cardSlide + cardHold) { _cardDone = true; StartReplay(); }
        if (_cardT >= cardSlide * 2f + cardHold)
        {
            _cardT = -1f;
            foreach (var c in _cards) c.gameObject.SetActive(false);
        }
    }

    // ── replay ─────────────────────────────────────────────────────────────

    void StartReplay()
    {
        _replayDueAt = -1f;
        if (_frames.Count < 10) return;
        _replayFrames = new List<Frame>(_frames);
        float end = _replayFrames[_replayFrames.Count - 1].t;
        float key = _keyTime >= 0f ? Mathf.Clamp(_keyTime, 0f, end) : end - 1.5f;
        // The slow window: from the throw until a second after the catch (an
        // incomplete: until it hits the grass); a run: around the key moment.
        if (_throwTime >= 0f) { _slowStart = _throwTime; _slowEnd = _catchTime >= 0f ? _catchTime + slowMoAfterCatch : end; }
        else { _slowStart = key - slowMoBefore; _slowEnd = key + slowMoAfter; }
        _slowStart = Mathf.Clamp(_slowStart, 0f, end); _slowEnd = Mathf.Clamp(_slowEnd, _slowStart, end);
        // Fit into the huddle: start late if the play was long, and run the
        // slow-mo a little faster if it still does not fit.
        float start = Mathf.Max(0f, Mathf.Min(_slowStart - 2.5f, end - 4f));
        float slowLen = _slowEnd - _slowStart, normalLen = (end - start) - slowLen;
        _slowSpeed = slowMoSpeed;
        if (normalLen + slowLen / _slowSpeed > maxReplaySeconds)
            _slowSpeed = Mathf.Clamp(slowLen / Mathf.Max(0.5f, maxReplaySeconds - normalLen), slowMoSpeed, 1f);
        _replayStart = start; _replayEnd = end; _replayKey = key; _replayT = start; _replayStartedAt = _clock;
        EnsureGhosts();
        _replaying = true;
        SetReplayLook(true);
        // The replay camera starts from where the play started.
        _focus = _replayFrames[0].focus; _fov = _replayFrames[0].fov; _slideZ = _focus.z * cameraFollow;
        _focusVel = Vector3.zero; _fovVel = 0f; _slideVel = 0f;
    }

    void TickReplay(float dt)
    {
        // Pace it to the huddle (Sam): finish one second before the break —
        // slower to fill the time, a touch faster if it wouldn't fit, gone
        // the moment the huddle breaks. After a score (a kickoff, no huddle)
        // the old fixed budget applies.
        // Eased: full speed until the throw, down to slowMoSpeed over
        // slowMoEase, back up the same way after the catch.
        bool slow = _replayT >= _slowStart && _replayT <= _slowEnd;
        float speed = 1f;
        if (slow)
        {
            float k = Mathf.Min((_replayT - _slowStart) / slowMoEase, (_slowEnd - _replayT) / slowMoEase, 1f);
            speed = Mathf.Lerp(1f, _slowSpeed, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k)));
        }
        var cur = _match.CurrentPlay;
        float untilBreak = cur != null ? cur.SecondsUntilBreak : -1f;
        if (untilBreak >= 0f && untilBreak <= 1.0f) { EndReplay(); return; }
        float remainingReal = untilBreak >= 0f ? untilBreak - 1f : maxReplaySeconds - (_clock - _replayStartedAt);
        float remainingPlayback = PlaybackSeconds(_replayT, _replayEnd);
        float scale = remainingReal > 0.1f ? Mathf.Clamp(remainingPlayback / remainingReal, 0.5f, 3f) : 3f;
        _replayT += dt * speed * scale;
        if (_replayT >= _replayEnd) { EndReplay(); return; }
        // Find the frame pair around _replayT and interpolate positions.
        int i = 0;
        while (i < _replayFrames.Count - 2 && _replayFrames[i + 1].t < _replayT) i++;
        var a = _replayFrames[i]; var b = _replayFrames[Mathf.Min(i + 1, _replayFrames.Count - 1)];
        float k = b.t > a.t ? Mathf.Clamp01((_replayT - a.t) / (b.t - a.t)) : 0f;
        for (int g = 0; g < _ghosts.Count && g < a.poses.Length; g++)
        {
            var pa = a.poses[g]; var pb = b.poses[g];
            var pf = k < 0.5f ? pa : pb;
            pf.pos = Vector3.Lerp(pa.pos, pb.pos, k);
            pf.facing = Vector3.Slerp(pa.facing, pb.facing, k);
            pf.bodyLocalPos = Vector3.Lerp(pa.bodyLocalPos, pb.bodyLocalPos, k);
            pf.bodyLocalRot = Quaternion.Slerp(pa.bodyLocalRot, pb.bodyLocalRot, k);
            var gh = _ghosts[g];
            FootballPlayer.ApplyPose(gh.root, gh.body, gh.rig, _fieldRoot, pf);
        }
        if (_ghostBall != null)
        {
            _ghostBall.localPosition = Vector3.Lerp(a.ballPos, b.ballPos, k);
            _ghostBall.localRotation = Quaternion.Slerp(a.ballRot, b.ballRot, k);
        }
        Vector3 focus = Vector3.Lerp(a.focus, b.focus, k);
        float fov = Mathf.Lerp(a.fov, b.fov, k) * replayZoom;
        if (slow) fov = Mathf.Lerp(fov, Mathf.Min(fov, Mathf.Max(slowMoFov, fov * 0.85f)), Mathf.InverseLerp(1f, _slowSpeed, speed));      // push in with the slow-mo
        AimAt(focus, Mathf.Max(fov, 6f), dt);
    }

    /// Seconds of screen time the recording between `from` and `to` takes
    /// at the base pace (slow window at _slowSpeed, the rest at 1×).
    float PlaybackSeconds(float from, float to)
    {
        float slowA = Mathf.Max(from, _slowStart), slowB = Mathf.Min(to, _slowEnd);
        float slowLen = Mathf.Max(0f, slowB - slowA);
        return (to - from - slowLen) + slowLen / Mathf.Max(0.05f, _slowSpeed);
    }

    void EndReplay()
    {
        _replaying = false;
        SetReplayLook(false);
        foreach (var g in _ghosts) g.root.gameObject.SetActive(false);
        if (_ghostBall != null) _ghostBall.gameObject.SetActive(false);
        // Come back to the live picture from where it is now.
        Intent(out _focus, out _, out _, out _fov);
        _slideZ = _focus.z * cameraFollow; _focusVel = Vector3.zero; _fovVel = 0f; _slideVel = 0f;
    }

    void SetReplayLook(bool on)
    {
        foreach (var l in _labels) if (l != null) l.enabled = on;
        if (!on) foreach (var r in _ghostRenderers) if (r != null) r.enabled = false;
    }

    /// One ghost per live man (same alien, same tint), built on first use.
    void EnsureGhosts()
    {
        var players = _match.Players;
        while (_ghosts.Count < players.Count)
        {
            var p = players[_ghosts.Count];
            var root = new GameObject("Ghost " + p.name);
            root.transform.SetParent(_fieldRoot, false);
            var g = new Ghost { root = root.transform };
            if (p.ModelPrefab != null)
            {
                var model = Instantiate(p.ModelPrefab, root.transform);
                model.name = "Body";
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                var anim = model.GetComponent<Animator>();
                if (anim != null) anim.enabled = false;
                var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
                float h = smr != null ? smr.bounds.size.y : 0.9f;
                model.transform.localScale = Vector3.one * (FootballPlayer.Height / Mathf.Max(0.3f, h));
                if (smr != null) { var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", p.Tint); smr.SetPropertyBlock(mpb); }
                g.body = model.transform;
                g.rig = model.AddComponent<FootballAlienRig>();
                g.rig.Init(root.transform);
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(0.7f, FootballPlayer.Height * 0.5f, 0.7f);
                g.body = body.transform;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; _ghostRenderers.Add(r); }
            _ghosts.Add(g);
        }
        foreach (var g in _ghosts) g.root.gameObject.SetActive(true);
        if (_ghostBall == null)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(b.GetComponent<Collider>());
            b.name = "Ghost Ball";
            b.transform.SetParent(_fieldRoot, false);
            b.transform.localScale = new Vector3(0.2f, 0.2f, 0.34f);
            var m = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.24f, 0.12f) };
            var br = b.GetComponent<Renderer>(); br.sharedMaterial = m; br.enabled = false; _ghostRenderers.Add(br);
            _ghostBall = b.transform;
        }
        _ghostBall.gameObject.SetActive(true);
    }

    // ── debug frame dump ───────────────────────────────────────────────────

    IEnumerator Dump()
    {
        yield return new WaitForEndOfFrame();
        if (_rt == null || _dumpDir == null) yield break;
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(_dumpDir, "b" + (_dumpN++).ToString("000") + (_replaying ? "_REPLAY" : "") + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        // Every fourth one, the player's own view too.
        if (_dumpN % 4 == 0) ScreenCapture.CaptureScreenshot(Path.Combine(_dumpDir, "game" + _dumpN.ToString("000") + ".png"));
    }
}
