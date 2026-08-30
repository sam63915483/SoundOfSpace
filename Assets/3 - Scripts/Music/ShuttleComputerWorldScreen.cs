using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The computer as an OBJECT IN THE WORLD: a monitor you can watch over
/// somebody's shoulder, and a speaker you can hear from across the shuttle.
///
/// ── What changed and why ─────────────────────────────────────────────────
/// The world monitor used to show a freeze-frame — the UI screenshotted itself
/// on every close and pasted that onto the mesh. That was honest while the
/// computer was a single-player thing (nothing changes while nobody is using
/// it, so a frozen frame IS the live picture), and completely wrong the moment
/// two people share one machine: your partner works and the mesh shows a
/// picture from ten minutes ago.
///
/// ── The image is not streamed, it is re-rendered ─────────────────────────
/// Nothing about the picture crosses the wire. The screen state, the song, the
/// transport and both cursors are already replicated, so a machine that is NOT
/// being used can simply BUILD THE SAME UI from the same state and render it
/// into the mirror texture itself. Two machines, same inputs, same picture —
/// for the cost of a 1024×576 camera rather than a video stream.
///
/// So the canvas has two modes:
///   • THIS player has it open  → ScreenSpaceOverlay, exactly as it always was.
///     Input, layout and every existing behaviour are untouched, and the world
///     monitor is irrelevant because they are staring at the fullscreen version.
///   • A PARTNER has it open    → the canvas renders through a private camera
///     into the mirror instead. Invisible here, live on the mesh.
///
/// ── Isolating the private camera ─────────────────────────────────────────
/// Two layers of defence, the same pair AstronautPreview uses: a dedicated
/// layer (the highest unnamed one) that no other camera draws, and parking
/// 10,000 units away with a tight far clip so even a camera that ignored the
/// layer would have to be pointed at nothing to catch it.
///
/// ── The sound comes out of the machine ───────────────────────────────────
/// Playback was already shared — each machine drives its own audio engine from
/// its own identical copy of the song — but it only ran while that player had
/// the computer open, and it was 2D. Now the engine runs whenever anybody is
/// using the machine, and the source sits ON the console at 3D, so a partner's
/// beat arrives from the right direction and fades with distance.
/// </summary>
public partial class ShuttleComputerUI
{
    Camera _screenCam;
    int _screenLayer = -1;
    int _canvasLayer;                 // the canvas's own layer, restored on leaving RT mode
    bool _rtMode;                     // canvas currently rendering into the mirror
    GraphicRaycaster _raycaster;

    ShuttleComputerTerminal _terminal;
    float _nextTerminalScan;

    /// A close-frame mirror capture is in flight — DriveMachine must keep its
    /// hands off the canvas/camera until it lands. See CaptureMirrorThenHide.
    bool _capturePending;

    /// Far enough that nothing in the solar system is near it, close enough that
    /// UI floats keep their precision. Mirrors AstronautPreview's parking trick.
    static readonly Vector3 ParkPosition = new Vector3(0f, -10000f, 0f);

    /// Where the beat is full volume, and where it has faded out. Chosen so you
    /// hear your partner working from anywhere in the shuttle but not from
    /// outside it.
    const float AudioFullDistance = 3f;
    const float AudioFadeDistance = 30f;

    // ── construction ─────────────────────────────────────────────────────

    /// <summary>
    /// The private camera that draws the canvas into the mirror texture. Built
    /// once, disabled until a partner actually needs it — a camera that renders
    /// nothing still costs a culling pass every frame.
    /// </summary>
    void BuildWorldScreenCam()
    {
        _screenLayer = FindPrivateLayer();

        var go = new GameObject("ShuttleScreenCam");
        go.transform.SetParent(transform, false);
        go.transform.position = ParkPosition;
        go.layer = _screenLayer;

        _screenCam = go.AddComponent<Camera>();
        _screenCam.clearFlags      = CameraClearFlags.SolidColor;
        _screenCam.backgroundColor = Color.black;
        _screenCam.cullingMask     = 1 << _screenLayer;
        _screenCam.orthographic    = false;
        _screenCam.nearClipPlane   = 0.1f;
        _screenCam.farClipPlane    = 50f;      // the canvas sits 1 unit away
        _screenCam.depth           = -100;     // never competes for the real screen
        _screenCam.allowHDR        = false;
        _screenCam.allowMSAA       = false;
        _screenCam.useOcclusionCulling = false;
        _screenCam.targetTexture   = ScreenMirror;
        _screenCam.enabled         = false;

        // A camera with no AudioListener sibling is fine; be explicit that this
        // one must never grow one, or it would fight the player's ears.
        var stray = go.GetComponent<AudioListener>();
        if (stray != null) Destroy(stray);
    }

    /// <summary>
    /// A private layer, scanning DOWN from 30 — deliberately NOT 31.
    ///
    /// ⚠️ 31 is unnamed but far from unused: BuildMenuUI, FishingdexManager and
    /// DeathCutsceneController all hard-code it as their own preview layer, each
    /// with a camera whose culling mask is exactly that one bit. "Highest
    /// unnamed" is this project's convention for a private stage, so taking the
    /// top of the range means colliding with everybody who follows it.
    /// </summary>
    static int FindPrivateLayer()
    {
        for (int i = 30; i >= 8; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        return LayerMask.NameToLayer("UI");
    }

    /// <summary>
    /// Make sure no OTHER camera draws our private layer.
    ///
    /// ⚠️ A camera whose mask is EXACTLY our bit is skipped. Those are somebody
    /// else's private stage — a build preview, a fish preview, the death
    /// cutscene — and clearing the bit would leave them with a culling mask of
    /// zero, rendering nothing, permanently. The death-cutscene camera is
    /// enabled and rendering continuously, so that failure is a black screen
    /// mid-cutscene rather than a subtle one.
    ///
    /// Only run on the transition into render-to-texture mode, so the array
    /// allocation is a non-issue.
    /// </summary>
    void ExcludeLayerFromOtherCameras()
    {
        if (_screenLayer < 0) return;
        int bit = 1 << _screenLayer;
        var cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
        {
            var cam = cams[i];
            if (cam == null || cam == _screenCam) continue;
            if (cam.cullingMask == bit) continue;      // somebody else's private stage
            cam.cullingMask &= ~bit;
        }
    }

    // ── the per-frame decision ───────────────────────────────────────────

    /// <summary>
    /// Runs EVERY frame, open or not — the computer is a thing in the world
    /// whether or not this player happens to be looking at it.
    ///
    /// Decides three things: whether the UI needs to exist at all right now,
    /// where it renders, and where its sound comes from.
    /// </summary>
    void DriveMachine()
    {
        bool remoteLive = TraxSessionSync.RemoteOpen;
        // Cockpit display (playtest 24, Sam's ask): during a flight the
        // monitor stays LIVE with the NAV — countdown, en-route velocity,
        // the hover feed — even with nobody at the machine, instead of a
        // freeze-frame of whatever was last open.
        var pilot = ShuttleAutopilot.Instance;
        bool flightLive = pilot != null && pilot.CurrentPhase != ShuttleAutopilot.Phase.Parked;
        bool live = _open || remoteLive || flightLive;

        if (!live)
        {
            // A close-frame capture is still photographing the screen — tearing
            // the camera/canvas down now is exactly the race that left the
            // mirror stuck on the landing feed. One or two frames of patience.
            if (_capturePending) return;

            // Nobody is using it. The mirror keeps whatever it last showed,
            // which is exactly right — a screen with nobody at it is not
            // changing.
            //
            // ⚠️ ORDER MATTERS. LeaveRtMode puts the canvas back to a
            // fullscreen overlay at sortingOrder 1000 with its raycaster live;
            // deactivating afterwards is what stops the whole terminal UI
            // painting over the player for a frame — and its full-bleed
            // backdrop eating a click — the moment a partner walks away.
            if (_rtMode) LeaveRtMode();
            if (_screenCam != null) _screenCam.enabled = false;
            if (_canvas != null && _canvas.gameObject.activeSelf)
                _canvas.gameObject.SetActive(false);

            // And the music stops. A partner closing the computer publishes no
            // transport event — deliberately, so their own local tidy-up cannot
            // stop everyone's playback — so this is the only thing that ever
            // silences a beat left running by someone who has walked away.
            if (_inst != null) _inst.Stop();
            return;
        }

        if (_open) { if (_rtMode) LeaveRtMode(); }
        else
        {
            if (!_rtMode) EnterRtMode();
            // Flight with nobody at the machine: the cockpit display shows
            // NAV, not whatever app was left open. Never steals the view
            // from a partner who is actually using the computer.
            if (flightLive && !remoteLive && (_navView == null || !_navView.activeSelf))
                ShowNav();
        }

        // The picture has to MOVE to be worth showing: without this the monitor
        // renders a static screen with a beat coming out of it, playhead and
        // step lights frozen. Cheap — timers and a playhead position, no input.
        if (_rtMode && _traxView != null && _traxView.activeSelf)
        {
            RefreshPlayhead();
            ArrangerUpdate();
        }

        DriveAudioPosition();
    }

    /// <summary>
    /// Render the canvas into the mirror instead of onto this player's screen.
    ///
    /// The canvas moves onto the private layer and in front of the parked
    /// camera; the raycaster goes off, because a screen nobody here is looking
    /// at must not eat this player's clicks.
    /// </summary>
    void EnterRtMode()
    {
        if (_canvas == null || _screenCam == null) return;
        _rtMode = true;

        ExcludeLayerFromOtherCameras();
        // The layer only has to be on the CANVAS object — UI culling is per
        // root Canvas, and this screen has exactly one, so children created
        // later on the default layer are still drawn. (If a sub-canvas is ever
        // added, it needs the layer too.)
        _canvasLayer = _canvas.gameObject.layer;
        _canvas.gameObject.layer = _screenLayer;

        _canvas.renderMode    = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera   = _screenCam;
        _canvas.planeDistance = 1f;

        if (_raycaster == null) _raycaster = _canvas.GetComponent<GraphicRaycaster>();
        if (_raycaster != null) _raycaster.enabled = false;

        _canvas.gameObject.SetActive(true);
        _screenCam.enabled = true;
    }

    /// Back to the fullscreen overlay this player interacts with — or to
    /// nothing at all, if they simply are not using it.
    void LeaveRtMode()
    {
        _rtMode = false;
        if (_screenCam != null) _screenCam.enabled = false;
        if (_canvas == null) return;

        _canvas.gameObject.layer = _canvasLayer;  // hand the private layer back
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.worldCamera = null;
        _canvas.sortingOrder = 1000;              // above the HUD, phone and prompts

        if (_raycaster == null) _raycaster = _canvas.GetComponent<GraphicRaycaster>();
        if (_raycaster != null) _raycaster.enabled = true;
    }

    /// <summary>
    /// One last render into the mirror as the computer closes, so the mesh shows
    /// the screen as it was left rather than as it was several navigations ago.
    ///
    /// ⚠️ IT HAS TO COST A WHOLE FRAME. Flipping into render-to-texture mode and
    /// calling Render() immediately photographs empty space: Unity lays the
    /// canvas out in front of its camera during the canvas update, which has
    /// already happened for this frame, so the geometry is still sitting where
    /// the overlay left it and the parked camera sees nothing. Yielding once
    /// lets the ordinary canvas update and the ordinary camera render do it
    /// properly — the difference between a picture and a black screen.
    /// </summary>
    System.Collections.IEnumerator RenderMirrorOnce()
    {
        if (_screenCam == null || _canvas == null) yield break;

        bool wasRt = _rtMode;
        EnterRtMode();
        yield return null;                 // canvas lays out, camera renders
        if (!wasRt) LeaveRtMode();
    }

    // ── the sound of the machine ─────────────────────────────────────────

    /// <summary>
    /// Put the audio source on the console and let it fall off with distance.
    ///
    /// Followed every frame rather than parented: the shuttle is a moving,
    /// floating-origin-rebased object, and a parent would drag the instrument's
    /// GameObject through every one of those rebases for no benefit.
    /// </summary>
    void DriveAudioPosition()
    {
        if (_inst == null) return;

        var console = ConsoleTransform();
        if (console == null)
        {
            _inst.SetSpatial(false, 0f, 0f);      // no console found — 2D, as before
            return;
        }

        _inst.transform.position = console.position;
        _inst.SetSpatial(true, AudioFullDistance, AudioFadeDistance);
    }

    /// The screen mesh, so the sound comes from the thing you are looking at.
    /// Cached; re-found on a throttle when it goes away, per the ban on
    /// FindObjectOfType in a per-frame path.
    Transform ConsoleTransform()
    {
        if (_terminal != null)
            return _terminal.gazeTarget != null ? _terminal.gazeTarget : _terminal.transform;

        if (Time.unscaledTime < _nextTerminalScan) return null;
        _nextTerminalScan = Time.unscaledTime + 2f;
        _terminal = FindObjectOfType<ShuttleComputerTerminal>();
        return _terminal != null
             ? (_terminal.gazeTarget != null ? _terminal.gazeTarget : _terminal.transform)
             : null;
    }

    // ── existence ────────────────────────────────────────────────────────

    /// <summary>
    /// Build the computer without opening it.
    ///
    /// A player who has never walked up to the terminal has no UI at all, so
    /// there would be nothing to render onto the mesh and nothing to make sound
    /// when their partner starts working. TraxSessionSync calls this the moment
    /// it hears that somebody is using the machine.
    /// </summary>
    public static void EnsureExists()
    {
        if (Instance != null) return;
        // ⚠️ Never in the main menu. TraxSessionSync is DontDestroyOnLoad and a
        // stray packet arriving during a scene transition would otherwise build
        // a whole terminal there — and with no console to find, its synth would
        // play a partner's beat flat and at full volume over the menu.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;

        var go = new GameObject("ShuttleComputerUI");
        Instance = go.AddComponent<ShuttleComputerUI>();
        Instance.Build();
    }
}
