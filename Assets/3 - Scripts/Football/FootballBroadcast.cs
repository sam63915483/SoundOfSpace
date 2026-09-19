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
/// Layers: live football bodies are on FootballPlayer.LiveLayer (29), ghosts
/// on ReplayLayer (30). This camera shows one or the other; every other
/// camera is told to ignore the ghosts. Both are spare, unnamed layers.
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
    public Vector3 screenAPos = new Vector3(0f, 39f, 97f);      // over the +Z end zone, above the scoreboard
    public Vector3 screenAEuler = Vector3.zero;                  // a Quad's face is −Z: identity looks back down the field
    public Vector3 screenBPos = new Vector3(0f, 30f, -97f);
    public Vector3 screenBEuler = new Vector3(0f, 180f, 0f);
    public Vector2 screenSize = new Vector2(40f, 22.5f);
    public int textureWidth = 960, textureHeight = 540;

    [Header("Camera (FieldRoot-local)")]
    public float cameraX = -58f;         // the home sideline, high in the stands
    public float cameraHeight = 26f;
    [Tooltip("How much the camera slides along the sideline with the ball (0 = fixed at the 50, 1 = always level with it).")]
    public float cameraFollow = 0.55f;
    public float fovWide = 24f, fovPocket = 16f, fovRunner = 11f, fovDead = 12f;
    public float focusSmooth = 0.25f, fovSmooth = 0.4f, slideSmooth = 0.7f;

    [Header("Replay")]
    public bool replays = true;
    public float replayDelay = 2.0f;     // live celebration first
    public float slowMoSpeed = 0.25f;
    public float slowMoBefore = 1.0f, slowMoAfter = 0.6f;
    public float maxReplaySeconds = 9f;

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
    float _recordAcc;
    PlayInstance _recordingPlay, _lastLivePlay, _endedPlay; float _endedAt = -1f;
    bool _recording;
    float _keyTime = -1f; int _keyRank;
    float _postWhistle = -1f;
    FootballPlayer _lastHolder; bool _wasAir;
    readonly Dictionary<FootballPlayer, bool> _wasHurdling = new Dictionary<FootballPlayer, bool>();
    readonly Dictionary<FootballPlayer, bool> _wasSpinning = new Dictionary<FootballPlayer, bool>();
    readonly Dictionary<FootballPlayer, bool> _wasDown = new Dictionary<FootballPlayer, bool>();

    // Replay playback.
    bool _replaying; float _replayT; float _replayStart, _replayEnd, _replayKey; float _replayDueAt = -1f;
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
        HideGhostsFromOtherCameras();
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
        _cam.cullingMask = ~(1 << FootballPlayer.ReplayLayer);
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
        // The REPLAY tag in the corner.
        var lbl = new GameObject("Tag");
        lbl.transform.SetParent(root.transform, false);
        lbl.transform.localPosition = new Vector3(-screenSize.x * 0.5f + 1.2f, screenSize.y * 0.5f - 1.0f, -0.55f);
        lbl.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);        // TMP faces +Z; the picture faces −Z
        var tmp = lbl.AddComponent<TextMeshPro>();
        tmp.text = "REPLAY";
        tmp.fontSize = 12f; tmp.color = new Color(1f, 0.25f, 0.2f); tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = false;
        tmp.GetComponent<RectTransform>().sizeDelta = new Vector2(8f, 2f);
        tmp.enabled = false;
        _labels.Add(tmp);
    }

    /// Every other camera: never the ghosts, always the live men. (The
    /// player's camera has its own mask that didn't include the spare live
    /// layer — Sam could see the aliens on the jumbotron but not on the field.)
    void HideGhostsFromOtherCameras()
    {
        foreach (var c in Camera.allCameras)
            if (c != _cam) c.cullingMask = (c.cullingMask & ~(1 << FootballPlayer.ReplayLayer)) | (1 << FootballPlayer.LiveLayer);
    }

    // ── per frame ──────────────────────────────────────────────────────────

    void Update()
    {
        if (_match == null || _cam == null) return;
        float dt = Time.deltaTime;
        _clock += dt;
        if ((_clock % 2f) < dt) HideGhostsFromOtherCameras();       // a late camera (pause cam, photo mode)

        if (_replaying) TickReplay(dt);
        else
        {
            if (_replayDueAt >= 0f && _clock >= _replayDueAt) StartReplay();
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
            _recordingPlay = play; _frames.Clear(); _recording = true; _recordAcc = 0f;
            _keyTime = -1f; _keyRank = 0; _postWhistle = -1f; _lastHolder = null; _wasAir = false;
            _wasHurdling.Clear(); _wasSpinning.Clear(); _wasDown.Clear();
        }
        if (!_recording || play != _recordingPlay) return;
        var v = play.view;
        float t = _frames.Count > 0 ? _frames[_frames.Count - 1].t + dt : 0f;
        if (_frames.Count == 0) t = 0f;
        // Events → the key moment (rank: catch 4, hurdle/spin 3, fumble 3, tackle 2).
        var holder = v.ball.holder;
        bool air = v.BallAirborne && !v.ball.isSnap;
        if (_wasAir && holder != null && holder != v.ball.thrower) Key(t, 4);
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
            if (t - _postWhistle > 2.0f) { _recording = false; return; }
        }
        _recordAcc += dt;
        if (_recordAcc < 1f / RecordHz) return;
        _recordAcc = 0f;
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

    void OnPlayEnded(PlayInstance.PlayResult r)
    {
        if (!replays || r.isKickoff) return;
        bool worth = r.touchdown || r.firstDown || r.outcome == PlayInstance.Outcome.Interception || r.outcome == PlayInstance.Outcome.Fumble
                     || (r.outcome == PlayInstance.Outcome.Complete && r.yards >= 8f) || r.yards >= 15f
                     || r.stats.hurdles > 0 || r.stats.spins > 0 || r.outcome == PlayInstance.Outcome.Sack;
        if (!worth) return;
        _replayDueAt = _clock + replayDelay;
    }

    // ── replay ─────────────────────────────────────────────────────────────

    void StartReplay()
    {
        _replayDueAt = -1f;
        if (_frames.Count < 10) return;
        _replayFrames = new List<Frame>(_frames);
        float end = _replayFrames[_replayFrames.Count - 1].t;
        float key = _keyTime >= 0f ? Mathf.Clamp(_keyTime, 0f, end) : end - 1.5f;
        // Fit into the huddle: start late if the play was long.
        float slowCost = (slowMoBefore + slowMoAfter) * (1f / slowMoSpeed - 1f);
        float start = 0f;
        if (end - start + slowCost > maxReplaySeconds) start = Mathf.Max(0f, end + slowCost - maxReplaySeconds);
        if (key - start < 1.5f) start = Mathf.Max(0f, key - 1.5f);
        _replayStart = start; _replayEnd = end; _replayKey = key; _replayT = start;
        EnsureGhosts();
        _replaying = true;
        SetReplayLook(true);
        // The replay camera starts from where the play started.
        _focus = _replayFrames[0].focus; _fov = _replayFrames[0].fov; _slideZ = _focus.z * cameraFollow;
        _focusVel = Vector3.zero; _fovVel = 0f; _slideVel = 0f;
    }

    void TickReplay(float dt)
    {
        float speed = (_replayT >= _replayKey - slowMoBefore && _replayT <= _replayKey + slowMoAfter) ? slowMoSpeed : 1f;
        _replayT += dt * speed;
        if (_replayT >= _replayEnd + 0.4f) { EndReplay(); return; }
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
        float fov = Mathf.Lerp(a.fov, b.fov, k);
        if (speed < 1f) fov = Mathf.Min(fov, fovRunner + 2f);      // push in for the slow-mo
        AimAt(focus, fov, dt);
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
        _cam.cullingMask = on ? ~(1 << FootballPlayer.LiveLayer) : ~(1 << FootballPlayer.ReplayLayer);
        foreach (var l in _labels) if (l != null) l.enabled = on;
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
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = FootballPlayer.ReplayLayer;
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
            b.GetComponent<Renderer>().sharedMaterial = m;
            b.layer = FootballPlayer.ReplayLayer;
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
