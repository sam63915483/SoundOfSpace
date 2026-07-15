using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering; // GraphicsFormat for ImageConversion.EncodeArrayToJPG
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Diegetic smartphone HUD. Pulls up on X (both on-foot and while piloting).
/// Four-app launcher (Fishingdex / Build / Settings / Map) plus reserved
/// space for future widgets and notifications. Slides up from below, sits
/// to the left of the hotbar.
///
/// Auto-singleton pattern (mirror VitalsHUD). MUST also be seeded in
/// MainMenuController.EnsureGameplaySingletons because builds start in
/// MainMenu where the RuntimeInitializeOnLoadMethod early-outs — see top
/// of CLAUDE.md for the full trap explanation.
/// </summary>
public class PlayerPhoneUI : MonoBehaviour
{
    public static PlayerPhoneUI Instance { get; private set; }

    // True while the phone is shown OR currently animating to/from shown.
    // PlayerController look read and TabbedPauseMenu ESC handler both gate
    // on this. Stays true through the close animation so look-around stays
    // blocked until the phone is fully gone.
    public static bool IsOpen { get; private set; }

    // §3 first-contact forcing function: false until the player has opened the
    // phone at least once. While false, the first incoming message shows a
    // PERSISTENT "Press X to open your phone." prompt that does not fade until
    // the phone is opened. Persisted via SaveCollector; reset by NewGameReset.
    public static bool HasEverOpened;

    // Set true by the Mission 1 wake-up intro to hold the first-open nag back
    // until a minute after control returns, so it doesn't pop during the cold
    // open. Reset to false when the menu loads (OnSceneLoaded) so an aborted
    // intro never leaves a later Load permanently muted.
    public static bool SuppressFirstNag;

    // Set true on the frame the phone consumed an Escape press to close
    // itself. Cleared in LateUpdate. TabbedPauseMenu reads this and skips
    // its own ESC-opens-pause branch on the same frame, so "ESC closes
    // phone" doesn't simultaneously open the pause menu.
    public static bool ConsumedEscapeThisFrame { get; private set; }

    // Camera mode — distinct from regular phone-open. While in camera mode
    // the phone is rotated 90° clockwise, cursor is relocked for free look,
    // movement is allowed, and the screen shows a live world feed from a
    // Camera parented to the player's main camera.
    public static bool IsCameraMode { get; private set; }

    // PlayerController look gate keys on this — look is blocked when the
    // phone is open AND we're on the home screen, but free when in camera
    // mode (the whole point of camera mode is aiming the lens).
    public static bool LookBlocked => IsOpen && !IsCameraMode;

    /// <summary>The phone chassis RectTransform (animates on/off screen). Used to anchor preset-reply UI.</summary>
    public RectTransform PhoneChassisRect => _phoneRT;

    // ── Layout constants ────────────────────────────────────────────
    // 4:3 landscape tablet (FNAF security-cam style): flips up from the
    // player's chest on a mechanical arm instead of sliding in. Height keeps
    // the original 440 so every internal layout constant still fits; width
    // is 4/3 of it.
    const float PhoneWidth     = 586f;
    const float PhoneHeight    = 440f;
    // Overall on-screen scale of the phone — easier to apply at the root
    // than to bump every internal pixel-size constant. (Was 1.5 as a
    // portrait handset; the 4:3 chest tablet reads too big past ~1.05.)
    const float PhoneScale     = 1.05f;
    const float SlideDuration  = 0.25f;   // legacy pacing constant (gallery waits key off animation end)
    // FNAF2-style flip: the monitor hinges around its OWN bottom edge,
    // rotating up from past-edge-on (you glimpse its back for a frame) to
    // face-on with a springy overshoot, while the hinge line rises from
    // below the screen. Fully opaque throughout — the flip is the reveal.
    const float FlipOpenDuration  = 0.30f;
    const float FlipCloseDuration = 0.20f;
    const float FlipClosedAngle   = 100f;  // X-rotation when stowed (past edge-on, lying against the chest)

    // ── Palette (mirrors VitalsHUD / AutoAlignToggleUI) ─────────────
    static readonly Color ChassisBg     = new Color32(0x0A, 0x18, 0x28, 0xFF);
    static readonly Color ChassisBorder = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color ScreenBg      = new Color32(0x06, 0x0F, 0x1A, 0xFF);
    static readonly Color AccentCyan    = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color LabelWhite    = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color TileBg        = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color ButtonGrey    = new Color32(0x2A, 0x40, 0x60, 0xFF);

    public enum AppKind { Fishingdex, Build, Settings, Map, Photos }

    // ── Runtime UI refs ─────────────────────────────────────────────
    Canvas        _canvas;
    CanvasGroup   _phoneGroup;
    RectTransform _phoneRT;
    RectTransform _screenRT;
    RectMask2D    _screenMask;

    // Current hinge X-rotation of the flip (FlipClosedAngle when stowed).
    float _flipAngle = FlipClosedAngle;

    // Mount arm: a CHILD of the chassis hanging below its bottom edge, so
    // arm + tablet sweep as one rigid assembly around the hinge at the
    // arm's base (the chest). Inherits the chassis CanvasGroup and, on the
    // camera-space canvas, real perspective. Art from HelmetHudConfig.
    RectTransform _armRT;
    RawImage      _armImage;
    float         _nextArmTexFind;
    const float ArmLocalHeight  = 400f;   // local units; ~370 hangs below the chassis
    const float ArmLocalOverlap = 30f;    // clamp bracket grips over the bottom bezel
    const float ArmAspect       = 0.7467f; // phone_arm.png width/height
    // R key cycles portrait → landscape (90° CW) → portrait. Always reset
    // to portrait on Open so re-opening lands in the default orientation.
    bool          _isLandscape;

    // Hint label "Press C for camera, press R to rotate" — fades in/out 3
    // times over 2s, then waits 10s, repeats forever. Timer runs on
    // absolute Time.unscaledTime so it keeps advancing while the phone is
    // closed (so the popup feels random rather than predictably 10s after
    // every open). Parented to the canvas root (sibling of _phoneRT) so
    // it doesn't fade with the phone's CanvasGroup and doesn't rotate
    // with the chassis.
    RectTransform   _hintRT;
    CanvasGroup     _hintGroup;
    TextMeshProUGUI _hintLabel;
    const float     HintMargin            = 14f;
    const float     HintInterval          = 10f;
    // 4 s split across HintBreatheCycles × 2 half-cycles = ~0.667 s per
    // fade-in or fade-out. Earlier 2 s felt too fast to read.
    const float     HintAnimDuration      = 4f;
    const int       HintBreatheCycles     = 3;
    float           _hintNextFireUnscaledTime;
    bool            _hintShowing;
    Coroutine       _hintRoutine;
    RectTransform _statusBarRT;
    RectTransform _notificationStripRT;
    RectTransform _appGridRT;
    RectTransform _reservedZoneRT;
    Button        _putAwayBtn;
    Button[]      _appButtons = new Button[6];

    // ── Home-screen page navigation ─────────────────────────────────
    // Four swappable pages live inside _pageHostRT. Only one is active at
    // a time; the nav widget below flips between them. Not saved —
    // resets to page 0 on every phone open.
    //   0 = Apps (Fishingdex / Build / Settings / Map)
    //   1 = AI Apps (AI / Notes / Codex / Calculator — three are stubs)
    //   2 = Vitals
    //   3 = Quests
    const int PageCount = 3;
    RectTransform _pageHostRT;
    RectTransform[] _pageRoots = new RectTransform[PageCount];
    int _currentPage; // 0=Apps (incl. AI), 1=Vitals, 2=Quests

    // Nav widget visuals.
    Image[] _navDots = new Image[PageCount];
    UnityEngine.UI.Shadow[] _navDotGlows = new UnityEngine.UI.Shadow[PageCount];

    // Vitals page state — bars + change-detected percent so we don't
    // reassign anchorMax every frame when nothing moved.
    RectTransform[] _vitalFills = new RectTransform[4];
    int[] _lastVitalPct = new int[] { -1, -1, -1, -1 };

    // Quests page state — sliding window of 5 visible rows over the
    // 12-entry _quests table. Cached at build time, refreshed on open
    // and on entering the page.
    const int VisibleQuestRows = 5;
    struct QuestRowUI { public Image Dot; public TextMeshProUGUI Label; }
    QuestRowUI[] _questRowUI = new QuestRowUI[VisibleQuestRows];

    struct QuestRow { public System.Func<bool> Read; public string Label; }
    static readonly QuestRow[] _quests = new QuestRow[]
    {
        new QuestRow{ Read = () => EarlyGameProgress.NoteRead,               Label = "Read the note" },
        new QuestRow{ Read = () => EarlyGameProgress.RodPickedUp,            Label = "Pick up the fishing rod" },
        new QuestRow{ Read = () => EarlyGameProgress.FirstFishCaught,        Label = "Catch your first fish" },
        new QuestRow{ Read = () => EarlyGameProgress.OneOfEachCaught,        Label = "Catch one of each fish" },
        new QuestRow{ Read = () => EarlyGameProgress.FirstMealEaten,         Label = "Cook and eat a meal" },
        new QuestRow{ Read = () => EarlyGameProgress.WaterBottleDrunk,       Label = "Drink from the bottle" },
        new QuestRow{ Read = () => EarlyGameProgress.ReturnedHome,           Label = "Return home" },
        new QuestRow{ Read = () => EarlyGameProgress.TevReturnedDialogueDone,Label = "Speak to Tev" },
        new QuestRow{ Read = () => EarlyGameProgress.CabinBuilt,             Label = "Build a cabin" },
        new QuestRow{ Read = () => EarlyGameProgress.VillageCoordsGiven,     Label = "Get village coordinates" },
        new QuestRow{ Read = () => EarlyGameProgress.FishVendorVisited,      Label = "Visit the fish vendor" },
        new QuestRow{ Read = () => EarlyGameProgress.GoodsVendorVisited,     Label = "Visit the goods vendor" },
    };

    // Status bar refs
    TextMeshProUGUI _timeText;
    TextMeshProUGUI _batteryText;
    RectTransform   _batteryFill;
    int            _batteryPct;
    int            _lastShownMinute = -1;

    // Camera mode runtime objects — created on first EnterCameraMode and
    // reused thereafter. The Camera component is parented to the player's
    // main camera with a small leftward offset (where a phone's lens would
    // sit) so it automatically follows player look (yaw + pitch). Its view
    // is rendered into _phoneCameraRT and displayed on _cameraView, which
    // covers the screen interior while we're in camera mode.
    RenderTexture _phoneCameraRT;           // landscape RT — matches the main camera's render output
    Image         _cameraBackdrop;          // opaque black behind _cameraView, so home content doesn't show through if the RT has any alpha holes
    RawImage      _cameraView;
    RawImage      _capturedView;            // sits on top of _cameraView; shows the most recent snap for ~3s before shrinking away
    RectTransform _capturedRT;
    Texture2D     _capturedTex;             // owned by us, destroyed when no longer displayed
    Coroutine     _capturedCoroutine;
    Camera        _mainCamRef;              // lazy-cached Camera.main for the per-frame manual render
    // RT is now sized to match Screen.width / Screen.height each time
    // EnsureCameraRig runs, so Blit from the back buffer is a 1:1 copy
    // regardless of ultrawide / 4K / windowed resolution.

    // iPhone-style shutter button — shown only in camera mode, anchored to
    // the bottom-center of the phone screen. Color: white for Photo mode,
    // red for Video mode. Inner solid disc shrinks to 50% on snap (photo)
    // or while recording (video). _isCapturing / _isRecording gates input
    // so the player can't double-trigger or switch modes mid-action.
    public enum CameraType { Photo, Video }
    CameraType    _cameraType = CameraType.Photo;
    RectTransform _shutterRoot;
    RectTransform _shutterInnerRT;
    Image         _shutterInner;
    Image         _shutterOuter;
    bool          _isCapturing;

    // Video-mode recording state. Frames are streamed directly into an
    // AVI Motion-JPEG container — a real playable video file with no
    // external deps. AsyncGPUReadback avoids stalling the render thread;
    // the encode + AVI append happens on the main thread in the readback
    // callback (Unity's ImageConversion is main-thread-only). Timer
    // counts up at the top of the phone screen during recording.
    bool                   _isRecording;
    float                  _recordingStartTime;
    string                 _videoPath;
    PhoneAviMjpegWriter    _aviWriter;
    int                    _videoCropX, _videoCropW, _videoCropH;
    float                  _videoNextFrameTime;
    const float            VideoFrameRate    = 15f; // recorded video frame rate
    const int              VideoJpegQuality  = 60;  // 0..100, 60 = visibly fine, small files
    RectTransform    _recordingTimerRT;
    TextMeshProUGUI  _recordingTimer;
    Image            _recordingDot;

    static readonly Color ShutterColorPhoto = Color.white;
    static readonly Color ShutterColorVideo = new Color(0.92f, 0.22f, 0.22f, 1f);

    // "Cannot move while using phone" toast — shown above the phone for 2s
    // (full brightness) then fades over 0.5s when the player triggers an
    // auto-close by pressing a movement key. Sits above the chassis as a
    // child of the phone, so it slides + fades with the phone naturally
    // when the close animation runs.
    TextMeshProUGUI _warningText;
    CanvasGroup _warningGroup;
    float _warningShownAt = -100f;
    const float WarningHoldSeconds = 2.0f;
    const float WarningFadeSeconds = 0.5f;

    // Public hooks for future systems to drop content into reserved zones
    // without touching the layout code.
    public RectTransform NotificationStripRoot => _notificationStripRT;
    public RectTransform ReservedZoneRoot      => _reservedZoneRT;

    public void SetNotificationText(string text)
    {
        if (_notificationStripRT == null) return;
        var labels = _notificationStripRT.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length > 0) labels[0].text = text;
    }

    /// <summary>Briefly surface a notification on the phone's strip + flag the AI app unread.
    /// Used by StoryDirector's discoverability buzz.</summary>
    public void FlashNotification(string text)
    {
        SetNotificationText(text);
        if (_aiUnreadBadge != null) _aiUnreadBadge.enabled = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("PlayerPhoneUI");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerPhoneUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _batteryPct = Random.Range(20, 96); // 20..95
        BuildCanvas();
    }

    void OnDestroy()
    {
        DetachCaptureCmd();
        if (Instance == this) Instance = null;
        if (_phoneCam != null) Destroy(_phoneCam.gameObject);   // root object, not a child
    }

    void OnEnable()
    {
        NPCConversationTracker.OnConversationStarted += OnConversationStarted;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySubscribeResourceManager();
    }

    void OnDisable()
    {
        NPCConversationTracker.OnConversationStarted -= OnConversationStarted;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnDeath -= ForceCloseNoAnim;
    }

    // ResourceManager is also an auto-singleton; it may not exist when we
    // first OnEnable (singleton creation order isn't guaranteed). Re-try
    // on the next frame via Update if needed.
    bool _subscribedToResourceManager;
    void TrySubscribeResourceManager()
    {
        if (_subscribedToResourceManager) return;
        if (ResourceManager.Instance == null) return;
        ResourceManager.Instance.OnDeath += ForceCloseNoAnim;
        _subscribedToResourceManager = true;
    }

    void OnConversationStarted(MonoBehaviour npc)
    {
        ForceCloseNoAnim();
        // ForceCloseNoAnim releases isInDialogue when it interrupts the
        // gallery/transition — but this handler runs precisely because a
        // conversation is starting, so re-assert the conversation's gate.
        PlayerController.isInDialogue = true;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Abort-safe: returning to the menu clears any intro nag suppression so a
        // later Load isn't left permanently muted.
        if (scene.name == "MainMenu") SuppressFirstNag = false;
        ForceCloseNoAnim();
    }

    void ForceCloseNoAnim()
    {
        if (IsCameraMode) ExitCameraMode();
        ClosePhoneApp();
        if (_animCoroutine != null) { StopCoroutine(_animCoroutine); _animCoroutine = null; }
        if (_rotateCoroutine != null) { StopCoroutine(_rotateCoroutine); _rotateCoroutine = null; }
        bool wasInGalleryTransition = _inGalleryTransition;
        if (_galleryTransition != null) { StopCoroutine(_galleryTransition); _galleryTransition = null; }
        _inGalleryTransition = false;
        if (PhotoGalleryUI.Instance != null) PhotoGalleryUI.Instance.ForceClose();
        // If we interrupted a transition, the gate may be transition-owned
        // (gallery already closed) — release it or the player stays frozen.
        // NOTE: nothing in the NPC interact chain checks isInDialogue, so a
        // conversation CAN start over the gallery/transition; this release
        // would clobber its gate, which is why OnConversationStarted
        // re-asserts it right after calling us.
        if (wasInGalleryTransition) PlayerController.isInDialogue = false;
        // The gallery tween may have left the chassis rotated/oversized.
        if (_phoneRT != null)
        {
            _phoneRT.localRotation = Quaternion.identity;
            _phoneRT.localScale = new Vector3(PhoneScale, PhoneScale, 1f);
        }
        if (_screenMask != null) _screenMask.enabled = true;
        HideHintNow();
        _isAnimating = false;
        IsOpen = false;
        _flipAngle = FlipClosedAngle;   // next Open starts from the stowed pose
        // A force-close can interrupt a mid-flip tween — make sure we're
        // back on the overlay canvas with the flip camera off.
        if (_canvas != null) { _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 800; }
        if (_phoneCam != null) _phoneCam.enabled = false;
        if (_phoneRT    != null) _phoneRT.anchoredPosition = new Vector2(_phoneRT.anchoredPosition.x, OffScreenY);
        if (_phoneGroup != null) { _phoneGroup.alpha = 0f; _phoneGroup.blocksRaycasts = false; }
        // AnimatePhone's close path restores nav events; force-close must too,
        // or they stay disabled with no UI open.
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null) es.sendNavigationEvents = true;
        // Skip cursor lock when we're in MainMenu — this method is invoked
        // by the sceneLoaded callback on EVERY scene load (including the
        // gameplay → MainMenu return), and locking the cursor in the menu
        // strands the player with no way to click buttons. The lock is the
        // right default during gameplay (NPC dialogue triggers this path
        // to close the phone and resume mouse-look), so we only suppress
        // when the new scene is the menu.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu")
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    void LateUpdate()
    {
        ConsumedEscapeThisFrame = false;
        // The phone RT is filled by a CommandBuffer on the main camera —
        // LateUpdate runs after the camera renders, so the RT has the
        // current frame by the time we tick the recording loop.
        TickVideoRecording();
    }

    // ── Public API ──────────────────────────────────────────────────

    public void Open()
    {
        if (_isAnimating && _animatingToOpen) return; // already opening
        // §3: opening the phone the first time satisfies the forcing function —
        // record it and dismiss the persistent nag prompt for good.
        if (!HasEverOpened) { HasEverOpened = true; HideOpenNag(); }
        // Always land on page 0 (apps) when the phone opens — never resume
        // mid-flipped from a prior session. Also refreshes quests so page 2
        // is ready if the player flips to it.
        GoToPage(0);
        RefreshQuests();
        // Always reset to portrait on open — landscape never carries across
        // close/reopen, per design. Snap instantly so the slide-in plays in
        // the final portrait orientation (no rotation tween off-screen).
        if (_rotateCoroutine != null) { StopCoroutine(_rotateCoroutine); _rotateCoroutine = null; }
        _isLandscape = false;
        ApplyOrientation(animate: false);
        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimatePhone(true));
    }

    public void Close()
    {
        if (_activeChat != null) _activeChat.Exit();
        ClosePhoneApp();   // reopening always lands on the home screen
        if (_isAnimating && !_animatingToOpen) return; // already closing
        // Always drop out of camera mode on close so re-opening the phone
        // lands on the home screen, never resumes inside camera mode.
        if (IsCameraMode) ExitCameraMode();
        // If a rotation tween is mid-flight, kill it now so its tail end
        // doesn't race with ExitCameraMode re-enabling _cameraView after
        // we wanted it off.
        if (_rotateCoroutine != null) { StopCoroutine(_rotateCoroutine); _rotateCoroutine = null; }
        // Kill any in-flight hint breathe — don't leave it pulsing on a
        // closed phone. Schedule (_hintNextFireUnscaledTime) is intentionally
        // NOT reset; the absolute timer keeps marching so the next popup
        // doesn't always land exactly 10 s after reopen.
        HideHintNow();
        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimatePhone(false));
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else        Open();
    }

    // ── Camera mode ─────────────────────────────────────────────────

    // Public hook for the pause-menu phone-resolution slider. If the user
    // changes the resolution while the camera is open, EnsureCameraRig
    // detects the size mismatch and rebuilds the RT — but the CommandBuffer
    // was detached as part of that rebuild, so we re-attach it here so the
    // live feed keeps drawing. Recording is stopped first because the open
    // AVI's header was sized for the old RT.
    public void OnPhoneResolutionChanged()
    {
        if (_isRecording) StopVideoRecording();
        if (!IsCameraMode) return;
        EnsureCameraRig();
        AttachCaptureCmd();
    }

    public void EnterCameraMode()
    {
        if (IsCameraMode) return;

        // Any in-phone app backs out — the camera feed covers the screen.
        ClosePhoneApp();

        // Lazily build the camera GameObject + RenderTexture on first entry.
        EnsureCameraRig();

        IsCameraMode = true;

        // Phone stays portrait — no rotation. Photos come out vertical, which
        // matches the screen aspect and makes the rendering pipeline simpler.

        // Swap home content for the live camera feed.
        if (_cameraBackdrop != null) _cameraBackdrop.enabled = true;
        if (_cameraView != null) _cameraView.enabled = true;
        if (_capturedView != null) _capturedView.enabled = false; // start fresh, no stale photo

        // Show the shutter button at the bottom-center of the screen.
        // Reset inner scale to 1 in case we entered camera mode mid-shrink
        // from a previous session (shouldn't normally happen, but defensive).
        if (_shutterRoot != null) _shutterRoot.gameObject.SetActive(true);
        if (_shutterInnerRT != null) _shutterInnerRT.localScale = Vector3.one;

        // Clear EventSystem focus — clicking the CAMERA button leaves that
        // button as the currentSelectedGameObject, which makes
        // TutorialGate.UISelectionActive() return true, which sets uiHasFocus
        // on PlayerController and blocks mouse-look. Symptom was "cursor locks
        // but I can't look around until I close the phone". Clearing the
        // selection here makes uiHasFocus drop to false next frame.
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        // Also stop the phone canvas from blocking raycasts. With the cursor
        // locked at screen-center, Unity's EventSystem could otherwise hover
        // a phone Selectable that sits under the center point (CAMERA button,
        // Close button, etc.) and re-select it next frame — kicking us back
        // into the look-blocked state. Camera-mode left-click is still read
        // (it's a direct Input.GetMouseButtonDown call, not a UI raycast).
        // interactable=false additionally makes every phone Selectable
        // ineligible for selection — ControllerUINavigator's pad auto-select
        // was grabbing a hidden home-screen button every frame, which
        // PlayerController reads as "UI focused" and zeroes look + movement.
        if (_phoneGroup != null)
        {
            _phoneGroup.blocksRaycasts = false;
            _phoneGroup.interactable = false;
        }

        // Camera mode needs the cursor LOCKED so the player can look around
        // freely to aim the lens. The default Open() unlocked it; we override.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Refresh the slice UV in case the RawImage was resized since last
        // entry (rare, but cheap to recompute).
        RefreshCameraSliceUV();

        // Hook the capture CommandBuffer into the main camera so we get a
        // pixel-perfect copy of its final render every frame, before HUDs.
        AttachCaptureCmd();
    }

    public void ExitCameraMode()
    {
        if (!IsCameraMode) return;
        IsCameraMode = false;
        if (_isRecording) StopVideoRecording(); // flush + close folder cleanly
        DetachCaptureCmd();
        if (_cameraBackdrop != null) _cameraBackdrop.enabled = false;
        if (_cameraView != null) _cameraView.enabled = false;
        if (_capturedView != null) _capturedView.enabled = false;
        if (_shutterRoot != null) _shutterRoot.gameObject.SetActive(false);
        if (_shutterInnerRT != null) _shutterInnerRT.localScale = Vector3.one;
        _isCapturing = false; // exit mid-capture → free lockout

        // Stop any in-flight capture-display coroutine and free the staged
        // Texture2D so we don't leak.
        if (_capturedCoroutine != null) { StopCoroutine(_capturedCoroutine); _capturedCoroutine = null; }
        if (_capturedTex != null) { Destroy(_capturedTex); _capturedTex = null; }

        // Coming back to the home screen — re-enable raycasts so the player
        // can click apps + Close button, and unlock cursor like a regular
        // phone-open state. Close() handles re-locking if the phone is
        // actually closing.
        if (IsOpen)
        {
            if (_phoneGroup != null)
            {
                _phoneGroup.blocksRaycasts = true;
                _phoneGroup.interactable = true;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    void EnsureCameraRig()
    {
        // Match the RT to the actual screen resolution × user-selected
        // phone-resolution scale. Full = native screen res (1:1 photos +
        // live feed, but expensive to record); lower tiers downsize the RT
        // to make video recording cheaper (AsyncGPUReadback + JPEG encode
        // + AVI write are all O(pixels)). Down-Blit from the screen back
        // buffer to a smaller RT stretches via bilinear filtering so the
        // photo / live feed still cover the same FOV — they're just softer.
        float scale = InputSettings.Active != null
            ? InputSettings.Active.GetPhoneResolutionMultiplier()
            : 1f;
        int targetW = Mathf.Max(1, Mathf.RoundToInt(Screen.width  * scale));
        int targetH = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));

        if (_phoneCameraRT != null
            && (_phoneCameraRT.width != targetW || _phoneCameraRT.height != targetH))
        {
            // Screen size changed (resolution change, alt-tab, etc.) — rebuild.
            DetachCaptureCmd();
            _phoneCameraRT.Release();
            _phoneCameraRT = null;
        }

        if (_phoneCameraRT == null)
        {
            _phoneCameraRT = new RenderTexture(targetW, targetH, 24, RenderTextureFormat.ARGB32)
            {
                name = "PhoneCameraRT",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            _phoneCameraRT.Create();
            if (_cameraView != null) _cameraView.texture = _phoneCameraRT;
        }

        // Set the live-view RawImage to crop a vertical slice from the
        // (now-screen-aspect) RT — phone screen is portrait but the RT is
        // landscape, so we show the middle column. Photos read this same slice.
        RefreshCameraSliceUV();
    }

    // The fraction of the landscape RT's WIDTH (in UV space) that the phone
    // screen displays. e.g. phone aspect ~0.45 / RT aspect ~1.78 = ~0.25 →
    // we show the middle 25% of the RT's width.
    float _sliceWidthUV = 0.25f;
    float _sliceLeftUV  = 0.375f;

    void RefreshCameraSliceUV()
    {
        if (_cameraView == null || _phoneCameraRT == null) return;
        if (_isLandscape)
        {
            // Phone is rotated 90° CW and the camera view is counter-rotated
            // back to upright; we want the whole landscape RT visible inside
            // it. RT aspect ~16:9 vs the visible camera-view aspect (~1.75)
            // is close enough — no perceptible squish.
            _sliceLeftUV  = 0f;
            _sliceWidthUV = 1f;
        }
        else
        {
            // Compute screen aspect from the actual RawImage RectTransform.
            var r = _cameraView.rectTransform.rect;
            if (r.width <= 0f || r.height <= 0f) return;
            float phoneAspect = r.width / r.height;          // <1 for portrait
            float rtAspect    = (float)_phoneCameraRT.width / _phoneCameraRT.height;
            _sliceWidthUV = Mathf.Clamp01(phoneAspect / rtAspect);
            _sliceLeftUV  = (1f - _sliceWidthUV) * 0.5f;
        }
        // Negative HEIGHT on uvRect flips the texture vertically on read.
        // CommandBuffer.Blit from BuiltinRenderTextureType.CameraTarget
        // produces a Y-flipped copy (the screen back buffer and a regular
        // RenderTexture use opposite conventions for which side is "v=0").
        // Reading from y=1 downwards inverts that flip so the phone view is
        // right-side up.
        _cameraView.uvRect = new Rect(_sliceLeftUV, 1f, _sliceWidthUV, -1f);
    }

    // Applies portrait/landscape orientation. The chassis rotation tweens
    // smoothly. In CAMERA MODE the live-feed + captured-photo RawImages
    // are hidden for the duration of the tween (so the screen reads as a
    // clean rotating chassis with the black backdrop showing), and snap
    // back to the new orientation at the end. In HOME MODE both
    // RawImages are already disabled, so this hide is a no-op — the home
    // content (apps, status bar) just rotates with the chassis.
    const float RotationDuration = 0.28f;
    Coroutine _rotateCoroutine;

    void ApplyOrientation()
    {
        ApplyOrientation(animate: true);
    }

    void ApplyOrientation(bool animate)
    {
        if (_phoneRT == null) return;

        float targetDeg = _isLandscape ? -90f : 0f;

        if (animate && isActiveAndEnabled && gameObject.activeInHierarchy)
        {
            if (_rotateCoroutine != null) StopCoroutine(_rotateCoroutine);
            float fromDeg = _phoneRT.localRotation.eulerAngles.z;
            if (fromDeg > 180f) fromDeg -= 360f; // unwrap to [-180, 180] so we tween the short way
            _rotateCoroutine = StartCoroutine(RotatePhoneRoutine(fromDeg, targetDeg));
        }
        else
        {
            _phoneRT.localRotation = Quaternion.Euler(0f, 0f, targetDeg);
            SnapCameraContentToOrientation();
            UpdateHintLabelPosition(targetDeg);
        }
    }

    System.Collections.IEnumerator RotatePhoneRoutine(float fromDeg, float toDeg)
    {
        // Disable the screen mask for the duration of the tween. RectMask2D
        // soft-culls children against the mask rect, and as the mask's
        // parent rotates, the cull intersects against bounds that no
        // longer match the rotated rect — which makes home-screen apps
        // (children of _screenRT) blank out mid-tween. Disable up-front
        // in both directions; the end-of-tween SnapCameraContentToOrientation
        // sets the final state based on _isLandscape.
        if (_screenMask != null) _screenMask.enabled = false;

        // Stash and hide the live-feed + captured-photo RawImages for the
        // tween. Only fires in camera mode (both are disabled in home
        // mode → liveWas/snapWas stay false → no-op).
        bool liveWas = _cameraView    != null && _cameraView.enabled;
        bool snapWas = _capturedView  != null && _capturedView.enabled;
        if (liveWas) _cameraView.enabled    = false;
        if (snapWas) _capturedView.enabled  = false;

        float t = 0f;
        while (t < RotationDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / RotationDuration);
            float eased = 1f - Mathf.Pow(1f - u, 3f); // ease-out cubic
            float angle = Mathf.Lerp(fromDeg, toDeg, eased);
            _phoneRT.localRotation = Quaternion.Euler(0f, 0f, angle);
            UpdateHintLabelPosition(angle);
            yield return null;
        }
        _phoneRT.localRotation = Quaternion.Euler(0f, 0f, toDeg);
        SnapCameraContentToOrientation();
        UpdateHintLabelPosition(toDeg);

        // Restore visibility — content reappears in its new orientation,
        // already upright in canvas space.
        if (liveWas && _cameraView   != null) _cameraView.enabled   = true;
        if (snapWas && _capturedView != null) _capturedView.enabled = true;

        _rotateCoroutine = null;
    }

    // Snap mask + every camera-content RT to the orientation indicated by
    // _isLandscape. Idempotent — safe to call repeatedly.
    void SnapCameraContentToOrientation()
    {
        if (_screenMask != null) _screenMask.enabled = !_isLandscape;
        if (_cameraView     != null) ApplyContentOrientation(_cameraView.rectTransform);
        if (_capturedRT     != null) ApplyContentOrientation(_capturedRT);
        if (_cameraBackdrop != null) ApplyContentOrientation(_cameraBackdrop.rectTransform);
        RefreshCameraSliceUV();
    }

    // ── First-open nag ("Press X to open your phone.") ───────────────
    // §3: a PERSISTENT prompt shown when the first message arrives and the
    // player has never opened the phone. Parented to the canvas root (not the
    // sliding phone chassis) so it stays put while the phone is closed, and it
    // does NOT fade — it persists until Open() dismisses it.
    RectTransform   _openNagRT;
    CanvasGroup     _openNagGroup;
    TextMeshProUGUI _openNagLabel;

    void BuildOpenNagLabel()
    {
        _openNagRT = NewUI("PhoneOpenNag", transform);
        _openNagRT.anchorMin = new Vector2(0.5f, 0f);
        _openNagRT.anchorMax = new Vector2(0.5f, 0f);
        _openNagRT.pivot     = new Vector2(0.5f, 0f);
        _openNagRT.sizeDelta = new Vector2(720f, 40f);
        _openNagRT.anchoredPosition = new Vector2(0f, 180f); // above the hotbar

        _openNagGroup = _openNagRT.gameObject.AddComponent<CanvasGroup>();
        _openNagGroup.alpha = 0f;
        _openNagGroup.blocksRaycasts = false;
        _openNagGroup.interactable = false;

        _openNagLabel = _openNagRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_openNagLabel);
        _openNagLabel.text = "Press X to open your phone.";
        _openNagLabel.fontSize = 26f;
        _openNagLabel.color = AccentCyan;
        _openNagLabel.alignment = TextAlignmentOptions.Center;
        _openNagLabel.enableWordWrapping = false;
        _openNagLabel.fontStyle = FontStyles.Bold;
        _openNagLabel.raycastTarget = false;
        var glow = _openNagLabel.gameObject.AddComponent<Shadow>();
        glow.effectColor = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.45f);
        glow.effectDistance = Vector2.zero;
    }

    /// <summary>
    /// §3: show the persistent "Press X to open your phone." prompt for the very
    /// first incoming message. No-op once the player has ever opened the phone.
    /// Called by StoryDirector when the first message arrives.
    /// </summary>
    public void RequestFirstOpenNag()
    {
        if (HasEverOpened) return;
        if (SuppressFirstNag) return;   // muted during/just after the wake-up intro
        // Refresh the binding text at show time — controller players are told
        // D-pad up, not the keyboard X (PromptGlyphs picks live per device).
        if (_openNagLabel != null)
            _openNagLabel.text = $"Press {PromptGlyphs.PhoneOpen} to open your phone.";
        if (_openNagGroup != null) _openNagGroup.alpha = 1f;
    }

    void HideOpenNag()
    {
        if (_openNagGroup != null) _openNagGroup.alpha = 0f;
    }

    // ── Hint label ("Press C for camera, press R to rotate") ─────────

    void BuildHintLabel()
    {
        _hintRT = NewUI("PhoneHint", transform);
        _hintRT.anchorMin = new Vector2(0.5f, 0.5f);
        _hintRT.anchorMax = new Vector2(0.5f, 0.5f);
        _hintRT.pivot     = new Vector2(0.5f, 1f); // pivot top-center → grows downward
        _hintRT.sizeDelta = new Vector2(720f, 36f);
        _hintRT.anchoredPosition = Vector2.zero;

        _hintGroup = _hintRT.gameObject.AddComponent<CanvasGroup>();
        _hintGroup.alpha = 0f;
        _hintGroup.blocksRaycasts = false;
        _hintGroup.interactable = false;

        _hintLabel = _hintRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_hintLabel);
        _hintLabel.text = "Press C for camera";
        _hintLabel.fontSize = 22f;
        _hintLabel.color = AccentCyan;
        _hintLabel.alignment = TextAlignmentOptions.Center;
        _hintLabel.enableWordWrapping = false;
        _hintLabel.fontStyle = FontStyles.Bold;
        _hintLabel.raycastTarget = false;

        // Initial position for portrait (0° rotation).
        UpdateHintLabelPosition(0f);

        // Start the background timer ticking from "now + 10s". From here on
        // the schedule advances strictly on absolute Time.unscaledTime, even
        // when the phone is closed.
        _hintNextFireUnscaledTime = Time.unscaledTime + HintInterval;
    }

    // Position the hint label just under the visible bottom edge of the
    // (possibly mid-rotation) phone. For a W×H rect rotated by θ around
    // its centre, the lowest point's Y is -(|W/2 · sinθ| + |H/2 · cosθ|).
    // Scaled by PhoneScale, then padded by HintMargin. The hint is parented
    // to this canvas (not _phoneRT) so it doesn't rotate with the chassis,
    // so we add the phone's resting OnScreenLift here to keep the hint
    // glued to the phone's bottom regardless of where the phone sits.
    void UpdateHintLabelPosition(float zRotDeg)
    {
        if (_hintRT == null) return;
        float rad = zRotDeg * Mathf.Deg2Rad;
        float halfW = PhoneWidth  * 0.5f;
        float halfH = PhoneHeight * 0.5f;
        float bottomOffset = (Mathf.Abs(halfW * Mathf.Sin(rad))
                            + Mathf.Abs(halfH * Mathf.Cos(rad))) * PhoneScale;
        _hintRT.anchoredPosition = new Vector2(0f, OnScreenLift - (bottomOffset + HintMargin));
    }

    // The hint is suppressed when the phone is closed OR when any of the
    // overlay UIs that the phone launches (Fishingdex, Build menu, the
    // tabbed pause menu opened from the Settings app) is currently up —
    // the player is busy with something the hint shouldn't be advising on.
    // The 10 s schedule keeps ticking regardless; the popup simply skips
    // any window where this returns true.
    bool ShouldHideHint()
    {
        if (!IsOpen) return true;
        if (FishingdexManager.Instance != null && FishingdexManager.IsOpen) return true;
        if (BuildMenuUI.Instance != null && BuildMenuUI.IsOpen) return true;
        if (TabbedPauseMenu.Instance != null && TabbedPauseMenu.Instance.IsOpen) return true;
        return false;
    }

    void HideHintNow()
    {
        if (_hintRoutine != null) { StopCoroutine(_hintRoutine); _hintRoutine = null; }
        _hintShowing = false;
        if (_hintGroup != null) _hintGroup.alpha = 0f;
    }

    // Three full fade-in/fade-out cycles across HintAnimDuration seconds.
    // Cheap: no per-frame string allocations; tracks alpha on the cached
    // CanvasGroup. Single coroutine instance reused per fire.
    System.Collections.IEnumerator HintBreatheRoutine()
    {
        _hintShowing = true;
        float halfCycle = HintAnimDuration / (HintBreatheCycles * 2f);

        for (int i = 0; i < HintBreatheCycles; i++)
        {
            float t = 0f;
            while (t < halfCycle)
            {
                t += Time.unscaledDeltaTime;
                _hintGroup.alpha = Mathf.Clamp01(t / halfCycle);
                yield return null;
            }
            _hintGroup.alpha = 1f;

            t = 0f;
            while (t < halfCycle)
            {
                t += Time.unscaledDeltaTime;
                _hintGroup.alpha = 1f - Mathf.Clamp01(t / halfCycle);
                yield return null;
            }
            _hintGroup.alpha = 0f;
        }

        _hintGroup.alpha = 0f;
        _hintShowing = false;
        // Schedule the next fire 10 s after THIS one's tail — total cycle
        // is HintInterval + HintAnimDuration when the phone stays open.
        _hintNextFireUnscaledTime = Time.unscaledTime + HintInterval;
        _hintRoutine = null;
    }

    // Configures a single camera-content RectTransform for the current
    // orientation. In portrait it returns to its default full-fill of
    // _screenRT. In landscape it becomes a center-anchored rect sized to
    // the swapped screen dimensions, with a +90° local rotation that
    // exactly counter-rotates the phone — net rotation in canvas space
    // is zero, so the texture displays upright.
    void ApplyContentOrientation(RectTransform rt)
    {
        if (rt == null || _screenRT == null) return;
        if (_isLandscape)
        {
            var screen = _screenRT.rect.size;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(screen.y, screen.x); // swap W/H
            rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        else
        {
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    // We hook a CommandBuffer into the main camera at AfterEverything so it
    // fires after the full render: skybox + opaque + transparent + image
    // effects + lens flares + halo, but BEFORE the screen-space-overlay HUD
    // canvases (those don't go through a camera at all). One Blit copies the
    // camera target into our phone RT, which gives a pixel-perfect copy of
    // what the player sees — no double-render cost.
    CommandBuffer _captureBuffer;
    Camera        _captureAttachedTo;
    Material      _opaqueBlitMat;
    const CameraEvent CaptureEvent = CameraEvent.AfterEverything;

    void AttachCaptureCmd()
    {
        DetachCaptureCmd(); // belt and suspenders — never double-attach
        var main = ResolveMainCamera();
        if (main == null || _phoneCameraRT == null) return;

        // Blit through Hidden/PhoneOpaqueBlit so the RT comes out with
        // alpha=1 forced — fixes the dim live feed in alpha-low areas
        // (skybox, additive lasers/lights). The shader lives in
        // Assets/3 - Scripts/UI/Resources/, which guarantees Unity bundles
        // it in builds. Shader.Find is reliable for shaders in Resources.
        if (_opaqueBlitMat == null)
        {
            var sh = Shader.Find("Hidden/PhoneOpaqueBlit");
            if (sh != null) _opaqueBlitMat = new Material(sh);
        }

        _captureBuffer = new CommandBuffer { name = "PhoneCameraCapture" };
        if (_opaqueBlitMat != null)
        {
            // Two-stage blit. CommandBuffer.Blit(BuiltinRenderTextureType.
            // CameraTarget, dst, material) binds _MainTex from the camera
            // target inconsistently between Editor and standalone builds —
            // in builds the source often isn't bound and the shader samples
            // the default white fallback, giving us a pure-white live feed.
            // Workaround: blit the camera target into a regular temporary
            // RT first (plain Blit, no material → bulletproof binding),
            // then blit THAT temp RT through the opaque material into our
            // phone RT. The material's source is now a real RenderTexture,
            // and _MainTex binds correctly on every platform.
            // Stage 1 temp RT MUST match the screen size, not the phone RT.
            // CommandBuffer.Blit from BuiltinRenderTextureType.CameraTarget
            // into a smaller destination does NOT stretch — it copies pixel-
            // for-pixel from the source's top-left corner, so a half-size dest
            // ends up showing only the top-left quadrant of the screen
            // (zoomed in + shifted toward (0,0)). By keeping stage 1 at full
            // screen size we sidestep that entirely — the screen→temp copy is
            // 1:1, and the temp→phoneRT material Blit downsamples cleanly via
            // the standard fullscreen-quad/UV pipeline.
            int tmpId = Shader.PropertyToID("_PhoneCaptureTmp");
            int srcW  = Mathf.Max(1, Screen.width);
            int srcH  = Mathf.Max(1, Screen.height);
            _captureBuffer.GetTemporaryRT(tmpId, srcW, srcH, 0,
                                          FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            _captureBuffer.Blit(BuiltinRenderTextureType.CameraTarget, tmpId);
            _captureBuffer.Blit(tmpId, _phoneCameraRT, _opaqueBlitMat);
            _captureBuffer.ReleaseTemporaryRT(tmpId);
        }
        else
        {
            // Shader missing entirely — fall back to plain Blit. Live feed
            // will be dim in alpha-low areas but won't crash or render white.
            _captureBuffer.Blit(BuiltinRenderTextureType.CameraTarget, _phoneCameraRT);
        }

        main.AddCommandBuffer(CaptureEvent, _captureBuffer);
        _captureAttachedTo = main;
    }

    void DetachCaptureCmd()
    {
        if (_captureAttachedTo != null && _captureBuffer != null)
        {
            _captureAttachedTo.RemoveCommandBuffer(CaptureEvent, _captureBuffer);
        }
        if (_captureBuffer != null) { _captureBuffer.Release(); _captureBuffer = null; }
        _captureAttachedTo = null;
    }

    Camera ResolveMainCamera()
    {
        if (_mainCamRef != null && _mainCamRef.gameObject != null) return _mainCamRef;
        // Prefer the PlayerController's own camera reference — it's the
        // actual gameplay camera that handles look input. Camera.main can
        // be ambiguous if there are multiple "MainCamera"-tagged objects
        // in the scene (cinematic, ship, etc.) and might return one with
        // a different orientation than the player's.
        var pc = FindObjectOfType<PlayerController>(true);
        if (pc != null && pc.Camera != null) { _mainCamRef = pc.Camera; return _mainCamRef; }
        _mainCamRef = Camera.main;
        return _mainCamRef;
    }

    // Capture the current camera frame to disk AND display it on the phone
    // screen for ~3s before shrinking away to reveal the live feed.
    void SnapPhoto()
    {
        // Lockout — one photo at a time. Player has to wait for the
        // preview/shrink/grow-back cycle to complete before snapping again.
        if (_isCapturing) return;
        if (_phoneCameraRT == null || _capturedView == null) return;

        // Read just the vertical slice the phone is displaying — same crop
        // as the live feed. That's the "phone took a vertical photo" result.
        int cropX = Mathf.Clamp(Mathf.RoundToInt(_sliceLeftUV  * _phoneCameraRT.width), 0, _phoneCameraRT.width);
        int cropW = Mathf.Clamp(Mathf.RoundToInt(_sliceWidthUV * _phoneCameraRT.width), 1, _phoneCameraRT.width - cropX);
        int cropH = _phoneCameraRT.height;

        var oldActive = RenderTexture.active;
        RenderTexture.active = _phoneCameraRT;
        var tex = new Texture2D(cropW, cropH, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(cropX, 0, cropW, cropH), 0, 0);
        // Flip the captured pixels vertically to match the (already-
        // unflipped) live view. ReadPixels gives us the RT as-is, which
        // is upside-down relative to the screen due to the back-buffer
        // vs RenderTexture coordinate-system mismatch.
        var pixels = tex.GetPixels32();
        var flipped = new Color32[pixels.Length];
        for (int y = 0; y < cropH; y++)
        for (int x = 0; x < cropW; x++)
            flipped[(cropH - 1 - y) * cropW + x] = pixels[y * cropW + x];
        tex.SetPixels32(flipped);
        tex.Apply();
        RenderTexture.active = oldActive;

        // Persist via the photo roll (JPG + thumbnail + manifest entry).
        // SavePhoto encodes synchronously and does NOT take ownership of
        // tex — the preview lifecycle below still destroys it.
        if (PhotoLibrary.Instance != null) PhotoLibrary.Instance.SavePhoto(tex);
        else Debug.LogWarning("[PlayerPhoneUI] PhotoLibrary.Instance is null — photo not persisted");

        // Show the captured frame on the phone — replaces any prior staged texture.
        if (_capturedCoroutine != null) StopCoroutine(_capturedCoroutine);
        if (_capturedTex != null) Destroy(_capturedTex);
        _capturedTex = tex;
        _capturedView.texture = _capturedTex;
        _capturedView.enabled = true;
        _capturedView.color = Color.white;
        _capturedRT.localScale = Vector3.one;

        // Begin the full snap lifecycle: shutter shrink → preview hold → fade
        // → shutter grow back → clear lockout. Single coroutine so ordering
        // is explicit and ExitCameraMode/Close can stop the whole flow
        // cleanly via StopCoroutine.
        _isCapturing = true;
        _capturedCoroutine = StartCoroutine(SnapLifecycle());
    }

    System.Collections.IEnumerator SnapLifecycle()
    {
        const float ShutterAnimSeconds = 0.2f;
        const float HoldSeconds        = 2.0f;
        const float PreviewFadeSeconds = 0.45f;
        const float ShrinkTo           = 0.5f;

        // The shutter's full shrink-and-grow cycle is fit ENTIRELY inside
        // the 2 s preview window — by the time the preview begins to fade
        // away, the shutter is already back at full size. Phases:
        //   [0.0, 0.2)   shutter shrinks 1.0 → 0.5
        //   [0.2, 1.8)   shutter holds at 0.5
        //   [1.8, 2.0)   shutter grows back 0.5 → 1.0
        //   [2.0, 2.45)  preview fades away (shutter at 1.0)

        // Phase 1 — shutter shrinks over 0.2 s.
        yield return ShutterScale(1f, ShrinkTo, ShutterAnimSeconds);

        // Phase 2 — hold the shrunk shutter for the MIDDLE of the preview:
        // 2.0s total preview − 0.2s shrink − 0.2s grow = 1.6s.
        float middleHold = HoldSeconds - 2f * ShutterAnimSeconds;
        if (middleHold > 0f)
        {
            float t = 0f;
            while (t < middleHold) { t += Time.unscaledDeltaTime; yield return null; }
        }

        // Phase 3 — shutter grows back to full size during the last 0.2 s
        // of the preview hold. Preview is still showing at full opacity.
        yield return ShutterScale(ShrinkTo, 1f, ShutterAnimSeconds);

        // Phase 4 — preview shrinks + fades away. Shutter stays at 1.0
        // throughout. Lockout still on until the preview is fully gone.
        float u = 0f;
        while (u < PreviewFadeSeconds)
        {
            u += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(u / PreviewFadeSeconds);
            float eased = 1f - k * k; // ease-in toward zero
            _capturedRT.localScale = new Vector3(eased, eased, 1f);
            var c = _capturedView.color;
            c.a = eased;
            _capturedView.color = c;
            yield return null;
        }

        // Tear down the preview.
        _capturedView.enabled = false;
        _capturedView.color = Color.white;
        _capturedRT.localScale = Vector3.one;
        if (_capturedTex != null) { Destroy(_capturedTex); _capturedTex = null; }

        _isCapturing = false;
        _capturedCoroutine = null;
    }

    System.Collections.IEnumerator ShutterScale(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float s = Mathf.Lerp(from, to, k);
            if (_shutterInnerRT != null) _shutterInnerRT.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (_shutterInnerRT != null) _shutterInnerRT.localScale = new Vector3(to, to, 1f);
    }

    // ── Video recording ─────────────────────────────────────────────
    //
    // Pipeline: LateUpdate calls AsyncGPUReadback.Request at the chosen
    // capture rate → readback completes 1-2 frames later on the main
    // thread → callback encodes the raw bytes to JPEG via ImageConversion
    // and appends to a Motion-JPEG AVI file. No sync GPU stall, no PNG
    // compression, no per-frame disk thrashing — output is a single .avi
    // playable in VLC / Windows Media Player / browsers.

    void StartVideoRecording()
    {
        if (_isRecording) return;
        if (_phoneCameraRT == null) return;

        // Snapshot crop bounds once at start. The crop matches the live
        // feed's vertical slice so recorded video = what the player sees.
        _videoCropX = Mathf.Clamp(Mathf.RoundToInt(_sliceLeftUV  * _phoneCameraRT.width), 0, _phoneCameraRT.width);
        _videoCropW = Mathf.Clamp(Mathf.RoundToInt(_sliceWidthUV * _phoneCameraRT.width), 1, _phoneCameraRT.width - _videoCropX);
        _videoCropH = _phoneCameraRT.height;

        try
        {
            var rootDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Photos"));
            System.IO.Directory.CreateDirectory(rootDir);
            _videoPath = System.IO.Path.Combine(rootDir, $"video_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.avi");
            _aviWriter = new PhoneAviMjpegWriter(_videoPath, _videoCropW, _videoCropH, (int)VideoFrameRate);
            Debug.Log($"[PlayerPhoneUI] Video recording → {_videoPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerPhoneUI] Failed to open AVI: {e.Message}");
            _aviWriter = null;
            _videoPath = null;
        }

        _isRecording = true;
        _recordingStartTime = Time.unscaledTime;
        _videoNextFrameTime = 0f; // grab the first frame immediately

        // Inner disc shrinks and stays shrunk for the whole recording.
        if (_shutterInnerRT != null) _shutterInnerRT.localScale = new Vector3(0.5f, 0.5f, 1f);

        // Show the timer; hide the status bar behind it.
        if (_recordingTimerRT != null) _recordingTimerRT.gameObject.SetActive(true);
        if (_recordingTimer != null) _recordingTimer.text = "0:00";
        if (_statusBarRT != null) _statusBarRT.gameObject.SetActive(false);
    }

    void StopVideoRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;

        // Detach writer first so any pending readback callbacks see null
        // and skip cleanly (they could otherwise try to write to a closed
        // file 1-2 frames after stop).
        var writer = _aviWriter;
        _aviWriter = null;
        int frameCount = 0;
        if (writer != null)
        {
            try { frameCount = writer.FrameCount; writer.Close(); }
            catch (System.Exception e) { Debug.LogWarning($"[PlayerPhoneUI] Failed to finalize AVI: {e.Message}"); }
        }
        Debug.Log($"[PlayerPhoneUI] Recording stopped — {frameCount} frames saved to {_videoPath ?? "(none)"}");
        _videoPath = null;

        // Inner disc grows back to full size.
        if (_shutterInnerRT != null) _shutterInnerRT.localScale = Vector3.one;

        // Hide timer, restore status bar.
        if (_recordingTimerRT != null) _recordingTimerRT.gameObject.SetActive(false);
        if (_statusBarRT != null) _statusBarRT.gameObject.SetActive(true);
    }

    void TickVideoRecording()
    {
        if (!_isRecording) return;

        // Timer display — change-detected so we only allocate a string
        // when the displayed second actually advances.
        float elapsed = Time.unscaledTime - _recordingStartTime;
        if (_recordingTimer != null)
        {
            int sec  = (int)elapsed;
            int mins = sec / 60;
            int secs = sec % 60;
            string newText = $"{mins}:{secs:00}";
            if (_recordingTimer.text != newText) _recordingTimer.text = newText;
        }

        if (_phoneCameraRT == null || _aviWriter == null) return;
        if (Time.unscaledTime < _videoNextFrameTime) return;
        _videoNextFrameTime = Time.unscaledTime + 1f / VideoFrameRate;

        // Submit an async readback for the cropped vertical slice. This
        // returns immediately — no GPU stall. The callback fires on the
        // main thread when the GPU has finished filling our copy buffer.
        AsyncGPUReadback.Request(_phoneCameraRT, 0,
            _videoCropX, _videoCropW, 0, _videoCropH, 0, 1,
            OnVideoFrameReadbackComplete);
    }

    void OnVideoFrameReadbackComplete(AsyncGPUReadbackRequest req)
    {
        if (req.hasError) return;
        if (_aviWriter == null) return; // recording stopped between request and callback

        int w = _videoCropW;
        int h = _videoCropH;
        int rowBytes = w * 4;

        // Native data is in RT storage order (row 0 = bottom). Flip rows
        // so JPEG encode produces a right-side-up frame matching the live
        // feed orientation — same convention as SnapPhoto.
        var nativeData = req.GetData<byte>();
        var raw = new byte[nativeData.Length];
        nativeData.CopyTo(raw);

        var flipped = new byte[raw.Length];
        for (int y = 0; y < h; y++)
            System.Buffer.BlockCopy(raw, y * rowBytes, flipped, (h - 1 - y) * rowBytes, rowBytes);

        try
        {
            var jpeg = ImageConversion.EncodeArrayToJPG(
                flipped, GraphicsFormat.R8G8B8A8_SRGB,
                (uint)w, (uint)h, 0, VideoJpegQuality);
            _aviWriter.WriteFrame(jpeg);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerPhoneUI] Frame encode failed: {e.Message}");
        }
    }

    // ── Update + animation ──────────────────────────────────────────

    Coroutine _animCoroutine;
    bool _isAnimating;
    bool _animatingToOpen;
    Camera _phoneCam;

    // Phone is anchor+pivot (0.5, 0.5) — sits slightly above canvas centre
    // so its bottom edge + the "Press C for camera, press R to rotate" hint
    // label below it clear the hotbar on ultrawide aspect ratios. With
    // matchWidthOrHeight=0.5 the canvas in design units is ~932 tall on
    // 3440×1440 (vs the reference 1080), which left the hint overlapping
    // the hotbar's top edge. +40 lifts everything cleanly above it on
    // ultrawide while still looking centered on 16:9.
    // 0 = the open monitor sits dead-centre of the view, FNAF2 style.
    const float OnScreenLift = 0f;
    float OnScreenY  => OnScreenLift;
    float OffScreenY
    {
        get
        {
            var parent = _phoneRT != null ? _phoneRT.parent as RectTransform : null;
            float halfH = parent != null ? parent.rect.height * 0.5f : 540f;
            return -(halfH + PhoneHeight * 0.5f * PhoneScale);
        }
    }

    void Update()
    {
        if (!_subscribedToResourceManager) TrySubscribeResourceManager();

        RefreshStatusBar();
        UpdatePosition();
        UpdateAIUnreadBadge();

        // Arm art arrives whenever HelmetHudConfig resolves (scene object;
        // throttled find). The arm is a chassis child, so visibility and the
        // flip pose come for free.
        if (_armImage != null && !_armImage.enabled && Time.unscaledTime >= _nextArmTexFind)
        {
            _nextArmTexFind = Time.unscaledTime + 1f;
            var helmetCfg = HelmetHudConfig.Instance;
            if (helmetCfg != null && helmetCfg.phoneArmTexture != null)
            {
                _armImage.texture = helmetCfg.phoneArmTexture;
                _armImage.enabled = true;
            }
        }

        // Vitals bars track ResourceManager live — only while page 2 is
        // visible AND the phone is open (no point updating an off-screen UI).
        if (IsOpen && _currentPage == 1) RefreshVitals();

        // Movement-warning fade is purely time-based (samples Time.unscaledTime
        // against _warningShownAt). It MUST run before the early-returns below
        // — otherwise the toast freezes at alpha=1 whenever the phone enters
        // camera mode or starts animating, because line 927 (camera mode) and
        // line 930 (_isAnimating) both bail out before reaching the bottom of
        // Update. Symptom: press X → press W → warning shows + phone closes →
        // press C to re-open in camera mode within 2s → warning sticks until
        // the phone fully closes again.
        UpdateMovementWarning();

        // While the AI chat input field has keyboard focus, NO key should
        // trigger phone-level shortcuts (R rotate / X close / C camera /
        // F close / Esc close / WASD movement-close). Visual updates above
        // continue; only input handling below is suppressed.
        if (AIChatScreen.IsTypingActive) return;

        if (_inGalleryTransition) return; // no phone input while zooming to/from the gallery

        // R-rotate is retired: the chest tablet is natively 4:3 landscape,
        // so there's no portrait orientation to flip to. The _isLandscape
        // machinery stays (gallery/orientation plumbing reads it) but is
        // never toggled — it's permanently false.

        // Hint timer — fires only when phone is open AND no overlay UI
        // (Fishingdex / Build menu / Settings tabbed-pause-menu) is up,
        // but the schedule itself is driven by absolute Time.unscaledTime
        // so the wait between fires keeps counting while the phone is
        // closed (the next popup feels random rather than predictably
        // 10 s after each open).
        if (ShouldHideHint())
        {
            if (_hintShowing) HideHintNow();
        }
        else if (!_hintShowing && Time.unscaledTime >= _hintNextFireUnscaledTime)
        {
            if (_hintRoutine != null) StopCoroutine(_hintRoutine);
            _hintRoutine = StartCoroutine(HintBreatheRoutine());
        }

        // ── Camera mode input (handled BEFORE the regular open/close logic
        //    so movement keys don't trigger the home-screen auto-close while
        //    the player is using the camera).
        if (IsCameraMode && !_isAnimating)
        {
            // PlayerController's look gate keys on TutorialGate.UISelectionActive()
            // — if anything is selected in the EventSystem, look + movement
            // are zeroed. Unity's StandaloneInputModule can re-select a
            // hovered Selectable across frames, so force-clear every frame
            // while we're using the camera. Also re-assert the cursor lock
            // in case something else released it.
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // F or C closes the camera AND the phone in one press. F is for
            // the "I need to talk to this NPC right now" case; C is the
            // symmetric "C toggles camera mode" → in camera, C closes it all.
            if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.C))
            {
                Close();
                return;
            }
            // X exits camera mode back to the phone home screen (phone
            // stays open). Symmetric with X-from-Map/Fishingdex/Build.
            // Pad: B backs out the same way.
            if (Input.GetKeyDown(KeyCode.X) || TutorialGate.PadPressed(TutorialGate.PadButton.B))
            {
                ExitCameraMode();
                return;
            }
            // Right click (pad: LT) swaps Photo ↔ Video. Locked out while a
            // snap is mid-lifecycle or a recording is in progress (don't let
            // the player change mode mid-action).
            if (TutorialGate.SecondaryFirePressed() && !_isCapturing && !_isRecording)
            {
                _cameraType = (_cameraType == CameraType.Photo) ? CameraType.Video : CameraType.Photo;
                ApplyCameraTypeColors();
            }

            // Left click (pad: RT) — photo mode snaps; video mode toggles recording.
            if (TutorialGate.FirePressed())
            {
                if (_cameraType == CameraType.Photo)
                {
                    SnapPhoto();
                }
                else
                {
                    if (_isRecording) StopVideoRecording();
                    else              StartVideoRecording();
                }
            }
            // ESC also exits camera mode → home; second ESC then closes the
            // phone (handled by the regular ESC branch below).
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ExitCameraMode();
                ConsumedEscapeThisFrame = true;
                return;
            }
            return; // skip the rest of Update while in camera mode
        }

        if (_isAnimating) return;

        // In-phone app open: X / ESC / pad-B back out to the home screen
        // first; a second press then closes the phone via the branches below.
        if (IsOpen && _activeApp != null
            && (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Escape)
                || TutorialGate.PadPressed(TutorialGate.PadButton.B)))
        {
            ClosePhoneApp();
            ConsumedEscapeThisFrame = true;
            return;
        }

        // ESC closes the phone (without opening the pause menu — see
        // TabbedPauseMenu.Update for the dual guard). Setting
        // ConsumedEscapeThisFrame protects against Update-order races: if
        // TabbedPauseMenu runs after us, it sees the flag and skips its
        // OpenPause branch.
        if (IsOpen && (Input.GetKeyDown(KeyCode.Escape) || TutorialGate.PadPressed(TutorialGate.PadButton.B)))
        {
            Close();
            ConsumedEscapeThisFrame = true;
            return;
        }

        // C toggles camera mode. Press C anywhere to enter; press C again
        // while in camera mode to exit back to home. Opens the phone first
        // if it's closed, and closes any open sub-menu (Fishingdex/Build/Map)
        // out of the way.
        if (Input.GetKeyDown(KeyCode.C) && !_isAnimating && !PhotoGalleryUI.IsOpen)
        {
            if (IsCameraMode)
            {
                ExitCameraMode();
            }
            else
            {
                if (FishingdexManager.IsOpen && FishingdexManager.Instance != null)
                    FishingdexManager.Instance.CloseFishingdex();
                else if (BuildMenuUI.IsOpen && BuildMenuUI.Instance != null)
                    BuildMenuUI.Instance.Close();
                else if (SolarSystemMapController.IsOpen && SolarSystemMapController.Instance != null)
                    SolarSystemMapController.Instance.CloseMap();

                if (!IsOpen) Open();
                EnterCameraMode();
            }
            return;
        }

        // X handling — sub-menu close paths run BEFORE the isInDialogue gate
        // because Fishingdex / Build / Settings set isInDialogue=true when
        // open and would otherwise block this branch entirely. (Map sets
        // isMapOpen instead, which is why X-from-map already worked.)
        // Pad: D-pad up OPENS the phone (on foot only — while piloted D-pad up
        // steps the ship headlight, and closing is B's job once open; app
        // buttons are Selectables so stick-nav + A drives everything inside,
        // including the camera app).
        if (!IsOpen && !Ship.AnyShipPiloted && TutorialGate.DPadDirectionPressed(0)
            && !GhostPlacement.IsPlacing   // D-pad up adjusts ghost distance there
            && !FishingdexManager.IsOpen && !BuildMenuUI.IsOpen && !SolarSystemMapController.IsOpen
            && !PlayerController.isInDialogue
            && (TabbedPauseMenu.Instance == null || !TabbedPauseMenu.Instance.IsOpen))
        {
            Open();
            return;
        }

        // Pad B only acts as "back out of a sub-menu" here — never as the
        // open/close toggle, since B on foot is Drop (X keyboard keeps both roles).
        bool padSubMenuBack = TutorialGate.PadPressed(TutorialGate.PadButton.B)
            && (FishingdexManager.IsOpen || BuildMenuUI.IsOpen || SolarSystemMapController.IsOpen);
        if (Input.GetKeyDown(KeyCode.X) || padSubMenuBack)
        {
            if (FishingdexManager.IsOpen && FishingdexManager.Instance != null)
            {
                FishingdexManager.Instance.CloseFishingdex();
                Open();
                return;
            }
            if (BuildMenuUI.IsOpen && BuildMenuUI.Instance != null)
            {
                BuildMenuUI.Instance.Close();
                Open();
                return;
            }
            if (SolarSystemMapController.IsOpen && SolarSystemMapController.Instance != null)
            {
                SolarSystemMapController.Instance.CloseMap();
                Open();
                return;
            }

            // No sub-menu open — fall through to the normal toggle, but
            // gated on dialogue / pause-menu state so we don't fight other UIs.
            if (!PlayerController.isInDialogue
                && (TabbedPauseMenu.Instance == null || !TabbedPauseMenu.Instance.IsOpen))
            {
                Toggle();
            }
        }

        // Movement input closes the phone — works on foot AND while piloting.
        // Design intent (user): "this teaches the player not to move if they
        // want to use the phone." Same key set in both contexts; LeftCtrl is
        // included because it's the ship's down-thrust button.
        if (IsOpen && !_isAnimating && !AIChatScreen.IsTypingActive)
        {
            // Pad equivalents only count as "movement" while NO phone UI
            // element is focused — otherwise left-stick menu navigation would
            // read as walking and close the phone. NOTE: pad A (JumpHeld) is
            // deliberately NOT in this list — A is the UI submit button, and
            // during screen transitions (e.g. tapping the AI tile) there are
            // frames with no selection while A is still held, which used to
            // slam the phone shut with the movement warning the moment you
            // opened an app. Keyboard Space still closes via the raw key list
            // below; pad players put the phone away with B.
            bool padMoving = !TutorialGate.UISelectionActive()
                && !TutorialGate.WasUIFocusedThisFrameStart()
                && (Mathf.Abs(TutorialGate.MoveAxisHorizontal(TutorialAbility.Move)) > 0.2f ||
                    Mathf.Abs(TutorialGate.MoveAxisVertical(TutorialAbility.Move)) > 0.2f ||
                    TutorialGate.DownThrustHeld(TutorialAbility.DownThrust));
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftControl) ||
                padMoving)
            {
                ShowMovementWarning();
                Close();
            }
        }
        // (UpdateMovementWarning moved to the top of Update — see comment there.)
    }

    // Phone sits horizontally centered (anchor.x = 0.5). No per-frame X
    // shifting needed — the old hotbar-left layout has been retired in
    // favour of a centered resting position between the compass and hotbar.
    void UpdatePosition()
    {
        if (_phoneRT == null) return;
        if (!Mathf.Approximately(_phoneRT.anchoredPosition.x, 0f))
            _phoneRT.anchoredPosition = new Vector2(0f, _phoneRT.anchoredPosition.y);
    }

    void RefreshStatusBar()
    {
        // Time: real-world HH:mm, change-detected on the minute so the TMP
        // text isn't reassigned every frame.
        var now = System.DateTime.Now;
        if (now.Minute != _lastShownMinute)
        {
            _lastShownMinute = now.Minute;
            if (_timeText != null) _timeText.text = now.ToString("HH:mm");
        }

        // Battery percentage text + horizontal fill scaled to pct/100.
        if (_batteryText != null && _batteryText.text == "--%")
            _batteryText.text = $"{_batteryPct}%";
        if (_batteryFill != null)
        {
            float pct = _batteryPct / 100f;
            _batteryFill.anchorMax = new Vector2(pct, 1f);
        }
    }

    System.Collections.IEnumerator AnimatePhone(bool toOpen)
    {
        _isAnimating = true;
        _animatingToOpen = toOpen;

        if (toOpen)
        {
            IsOpen = true;
            _phoneGroup.blocksRaycasts = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            // Navigation events stay ENABLED — controller players drive the
            // phone with D-pad/stick + A, which needs them. The old
            // sendNavigationEvents=false guard existed because the legacy
            // StandaloneInputModule mapped Space to Submit (walking would
            // click app buttons); the InputSystemUIInputModule submits on
            // Enter / pad-A only, so that failure mode is gone. Clearing the
            // selection once on open still prevents a stale pre-open
            // selection from eating the first input.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                es.sendNavigationEvents = true;
                // Pad players need a focused Selectable IMMEDIATELY: the global
                // ControllerUINavigator only rescans every 0.25 s, and Update's
                // movement-close check reads an UNFOCUSED left stick as
                // "walking" — so opening the phone and nudging the stick inside
                // that window closed it right back with the movement warning.
                // Hand focus to the first app tile ourselves (mirrors
                // AIChatScreen's explicit input-field focus). Mouse players
                // keep the old clear-to-null so a stale pre-open selection
                // can't eat the first click.
                if (TutorialGate.ControllerEnabled
                    && _appButtons[0] != null && _appButtons[0].gameObject.activeInHierarchy)
                    es.SetSelectedGameObject(_appButtons[0].gameObject);
                else
                    es.SetSelectedGameObject(null);
            }
        }

        // FNAF2-style flip: arm + tablet sweep as ONE rigid assembly around
        // the hinge at the arm's base (the chest, below the view). A single
        // rotation drives both the rise and the tilt — with the camera-space
        // canvas providing real perspective, it reads as a monitor genuinely
        // flipping up in front of the helmet. Fully opaque throughout.
        // RectMask2D mis-culls children while the chassis holds an
        // X-rotation (same reason RotatePhoneRoutine disables it), so the
        // mask sits out the whole tween.
        if (_screenMask != null) _screenMask.enabled = false;

        // Perspective ONLY for the flip: swap to the camera canvas for the
        // tween, back to pixel-perfect overlay at both endpoints.
        if (_phoneCam != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _phoneCam;
            _canvas.planeDistance = 1f;
            _phoneCam.enabled = true;
        }

        float chassisH = PhoneHeight * PhoneScale;
        float armLen   = (ArmLocalHeight - ArmLocalOverlap) * PhoneScale;

        float fromAng = _flipAngle;
        float toAng   = toOpen ? 0f : FlipClosedAngle;
        float dur     = toOpen ? FlipOpenDuration : FlipCloseDuration;

        _phoneGroup.alpha = 1f;   // opaque for the whole flip, both ways

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float eased;
            if (toOpen)
            {
                // Ease-out-back — snaps up, overshoots a few degrees past
                // face-on, springs back like a latched mount.
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float v = u - 1f;
                eased = 1f + c3 * v * v * v + c1 * v * v;
            }
            else eased = u * u;   // ease-in — drops away fast
            _flipAngle = Mathf.LerpUnclamped(fromAng, toAng, eased);
            ApplyFlip(chassisH, armLen);
            yield return null;
        }

        _flipAngle = toAng;
        ApplyFlip(chassisH, armLen);
        _phoneGroup.alpha = toOpen ? 1f : 0f;
        // Flip done — back to the overlay canvas for exact clicks/rendering.
        if (_phoneCam != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 800;
            _phoneCam.enabled = false;
        }
        // Restore mask + camera-content state for the current orientation.
        SnapCameraContentToOrientation();

        if (!toOpen)
        {
            IsOpen = false;
            _phoneGroup.blocksRaycasts = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            // Restore navigation events for the rest of the game's UI.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.sendNavigationEvents = true;
        }

        _isAnimating = false;
        _animCoroutine = null;
    }

    // Poses the assembly for the current hinge angle. The hinge is the arm
    // BASE below the view; the chassis centre orbits it: at θ=0 it rests at
    // OnScreenY, and rotating back by θ drops it by pivotDist·(1−cosθ)
    // while the whole assembly (arm is a child) tilts. One rotation = rise
    // + tilt together, like a real bottom-hinged monitor.
    void ApplyFlip(float chassisH, float armLen)
    {
        float pivotDist = armLen + chassisH * 0.5f;
        float c = Mathf.Cos(_flipAngle * Mathf.Deg2Rad);
        float cy = OnScreenY - pivotDist * (1f - c);
        _phoneRT.localRotation = Quaternion.Euler(_flipAngle, 0f, 0f);
        _phoneRT.anchoredPosition = new Vector2(0f, cy);
    }

    // ── Canvas + chassis + screen ───────────────────────────────────

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        // HYBRID rendering: Screen Space — OVERLAY while resting (pixel-
        // perfect clicks + rendering, no scene post — exactly the phone-era
        // behaviour), switched to Screen Space — CAMERA on the dedicated
        // perspective PhoneUICamera ONLY for the flip animation, when
        // nothing is clickable anyway. Perspective gives the flip TRUE
        // keystone (an overlay canvas can only squash an X-rotation — it
        // always read as a slide); staying camera-space at rest caused a
        // constant ~90px raycast-vs-visual offset that made buttons miss.
        // Sorting 800 keeps it UNDER the helmet layers (frame 805 / HUDs
        // 830) both ways — the tablet is physically outside the visor.
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 800;
        // The camera must be a ROOT object, NOT a child of this canvas: an
        // SSC canvas drives its own transform to sit in front of its camera,
        // so a child camera gets dragged along in a feedback loop until it
        // ends up inside the canvas plane rendering nothing. Parked far
        // below the world; with a UI-only mask + 3m far clip it can only
        // ever see the canvas Unity places in front of it.
        var camGo = new GameObject("PhoneUICamera");
        DontDestroyOnLoad(camGo);
        camGo.transform.position = new Vector3(0f, -80000f, 0f);
        _phoneCam = camGo.AddComponent<Camera>();
        _phoneCam.clearFlags = CameraClearFlags.Depth;   // draw over the finished frame
        _phoneCam.cullingMask = 1 << 5;                  // UI layer only (this canvas)
        _phoneCam.fieldOfView = 60f;
        _phoneCam.nearClipPlane = 0.05f;
        _phoneCam.farClipPlane = 3f;
        _phoneCam.depth = 90f;                           // after main camera + post
        _phoneCam.allowHDR = false;
        _phoneCam.allowMSAA = false;
        _phoneCam.useOcclusionCulling = false;
        _phoneCam.enabled = false;                       // on only while flipping
        gameObject.layer = 5;                            // canvas culling uses the canvas GO's layer

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        BuildPhone();
        BuildShutterButton();
        BuildHintLabel();
        BuildOpenNagLabel();
    }

    void BuildShutterButton()
    {
        // iPhone-style: outer ring + inner solid disc with a small gap.
        // Parented to the phone SCREEN (not the gameplay canvas) so it sits
        // at the bottom-middle of the live feed inside the phone, not
        // floating over the hotbar in the player's view. LayoutElement
        // ignoreLayout keeps the screen's VerticalLayoutGroup from
        // shuffling it into the home-content row stack.
        _shutterRoot = NewUI("ShutterButton", _screenRT);
        _shutterRoot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _shutterRoot.anchorMin = new Vector2(0.5f, 0f);
        _shutterRoot.anchorMax = new Vector2(0.5f, 0f);
        _shutterRoot.pivot     = new Vector2(0.5f, 0f);
        _shutterRoot.anchoredPosition = new Vector2(0f, 10f);
        _shutterRoot.sizeDelta = new Vector2(50f, 50f);
        _shutterRoot.gameObject.SetActive(false); // shown only in camera mode

        // Outer ring — 50×50 white hollow circle, 3-px thick.
        var outerRT = NewUI("Outer", _shutterRoot);
        outerRT.anchorMin = Vector2.zero; outerRT.anchorMax = Vector2.one;
        outerRT.offsetMin = Vector2.zero; outerRT.offsetMax = Vector2.zero;
        _shutterOuter = outerRT.gameObject.AddComponent<Image>();
        _shutterOuter.sprite = Ring();
        _shutterOuter.color  = Color.white;
        _shutterOuter.raycastTarget = false;

        // Inner disc — 36×36 white filled circle. Gap between inner edge
        // and outer ring ≈ 5 px. This is the one that shrinks on snap.
        _shutterInnerRT = NewUI("Inner", _shutterRoot);
        _shutterInnerRT.anchorMin = new Vector2(0.5f, 0.5f);
        _shutterInnerRT.anchorMax = new Vector2(0.5f, 0.5f);
        _shutterInnerRT.pivot     = new Vector2(0.5f, 0.5f);
        _shutterInnerRT.anchoredPosition = Vector2.zero;
        _shutterInnerRT.sizeDelta = new Vector2(36f, 36f);
        _shutterInner = _shutterInnerRT.gameObject.AddComponent<Image>();
        _shutterInner.sprite = Disc();
        _shutterInner.color  = Color.white;
        _shutterInner.raycastTarget = false;

        BuildRecordingTimer();
    }

    void BuildRecordingTimer()
    {
        // Counts up at the top-middle of the phone screen during a video
        // recording. Hidden by default. Replaces the status bar visually
        // while shown (we toggle _statusBarRT.gameObject.SetActive(false)
        // alongside this to avoid the two overlapping). Anchored top-center
        // of _screenRT; ignoreLayout keeps it out of the VLG flow.
        _recordingTimerRT = NewUI("RecordingTimer", _screenRT);
        _recordingTimerRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _recordingTimerRT.anchorMin = new Vector2(0f, 1f);
        _recordingTimerRT.anchorMax = new Vector2(1f, 1f);
        _recordingTimerRT.pivot     = new Vector2(0.5f, 1f);
        _recordingTimerRT.anchoredPosition = new Vector2(0f, -6f);
        _recordingTimerRT.sizeDelta = new Vector2(0f, 18f);
        _recordingTimerRT.gameObject.SetActive(false);

        // Small red dot — sits to the left of the timer text. "REC" indicator.
        var dot = NewUI("Dot", _recordingTimerRT);
        dot.anchorMin = new Vector2(0.5f, 0.5f);
        dot.anchorMax = new Vector2(0.5f, 0.5f);
        dot.pivot     = new Vector2(1f, 0.5f);
        dot.anchoredPosition = new Vector2(-22f, 0f);
        dot.sizeDelta = new Vector2(8f, 8f);
        _recordingDot = dot.gameObject.AddComponent<Image>();
        _recordingDot.sprite = Disc();
        _recordingDot.color  = ShutterColorVideo;
        _recordingDot.raycastTarget = false;

        // The timer text — centered to the right of the dot.
        var textRT = NewUI("Text", _recordingTimerRT);
        textRT.anchorMin = new Vector2(0.5f, 0f);
        textRT.anchorMax = new Vector2(0.5f, 1f);
        textRT.pivot     = new Vector2(0f, 0.5f);
        textRT.anchoredPosition = new Vector2(-12f, 0f);
        textRT.sizeDelta = new Vector2(60f, 0f);
        _recordingTimer = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_recordingTimer);
        _recordingTimer.text = "0:00";
        _recordingTimer.fontSize = 13f;
        _recordingTimer.color = ShutterColorVideo;
        _recordingTimer.alignment = TextAlignmentOptions.MidlineLeft;
        _recordingTimer.fontStyle = FontStyles.Bold;
        _recordingTimer.raycastTarget = false;
        _recordingTimer.enableWordWrapping = false;
    }

    void ApplyCameraTypeColors()
    {
        var c = (_cameraType == CameraType.Photo) ? ShutterColorPhoto : ShutterColorVideo;
        if (_shutterOuter != null) _shutterOuter.color = c;
        if (_shutterInner != null) _shutterInner.color = c;
    }

    void BuildPhone()
    {
        // Phone root — the flip animation drives anchoredPosition.y +
        // X-rotation (the monitor hinges around its own bottom edge) and
        // CanvasGroup.alpha. Anchored to canvas centre; pivot centre.
        _phoneRT = NewUI("Phone", transform);
        _phoneRT.anchorMin = new Vector2(0.5f, 0.5f);
        _phoneRT.anchorMax = new Vector2(0.5f, 0.5f);
        _phoneRT.pivot     = new Vector2(0.5f, 0.5f);
        _phoneRT.sizeDelta = new Vector2(PhoneWidth, PhoneHeight);
        _phoneRT.localScale = new Vector3(PhoneScale, PhoneScale, 1f);
        _phoneRT.anchoredPosition = new Vector2(0f, OffScreenY); // off-screen (below)

        _phoneGroup = _phoneRT.gameObject.AddComponent<CanvasGroup>();
        _phoneGroup.alpha = 0f;
        _phoneGroup.blocksRaycasts = false;

        // Chassis: dark navy rounded rect with the biggest radius (~26 px on
        // a 220-px-wide phone, matching the mockup's ~14% chassis-to-radius
        // ratio).
        var chassis = _phoneRT.gameObject.AddComponent<Image>();
        chassis.sprite = RoundedRectFilled(26);
        chassis.type   = Image.Type.Sliced;
        chassis.color  = ChassisBg;
        chassis.raycastTarget = false;

        var border = NewUI("Border", _phoneRT);
        border.anchorMin = Vector2.zero; border.anchorMax = Vector2.one;
        border.offsetMin = Vector2.zero; border.offsetMax = Vector2.zero;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = RoundedRectOutline(26); // match chassis radius
        borderImg.type   = Image.Type.Sliced;
        borderImg.color  = ChassisBorder;
        borderImg.raycastTarget = false;

        // Side buttons (left: silent + vol up + vol dn; right: power).
        AddSideButton("SilentSwitch", anchorY: 0.86f, height: 12f, leftSide: true);
        AddSideButton("VolUp",        anchorY: 0.78f, height: 24f, leftSide: true);
        AddSideButton("VolDn",        anchorY: 0.66f, height: 34f, leftSide: true);
        AddSideButton("PowerButton",  anchorY: 0.74f, height: 40f, leftSide: false);

        // Top hardware on the bezel: pill-shaped speaker grille centered in
        // the bezel middle, with the round camera lens just to its right.
        // Bezel midline is at y = -21 (half of the 42 px bezel), so both
        // fixtures use pivot (0.5, 0.5) and anchoredPosition.y = -21 — the
        // camera dot sits next to the speaker's right edge with a small gap.
        const float BezelMidY = -21f;

        var spk = NewUI("Speaker", _phoneRT);
        spk.anchorMin = new Vector2(0.5f, 1f); spk.anchorMax = new Vector2(0.5f, 1f);
        spk.pivot = new Vector2(0.5f, 0.5f);
        spk.anchoredPosition = new Vector2(0f, BezelMidY);
        spk.sizeDelta = new Vector2(56f, 4f);
        var spkImg = spk.gameObject.AddComponent<Image>();
        spkImg.sprite = Disc(); // pill-shaped via 9-slice on a disc
        spkImg.type   = Image.Type.Sliced;
        spkImg.color  = new Color(0.10f, 0.16f, 0.25f, 0.7f);
        spkImg.raycastTarget = false;

        // Camera lens: outer dark ring + inner cyan glow. Speaker right edge
        // is at x = +28 (half of 56), camera is 10 wide, so a center at +39
        // leaves a 6 px gap between the speaker and the camera ring.
        var camRing = NewUI("CameraRing", _phoneRT);
        camRing.anchorMin = new Vector2(0.5f, 1f); camRing.anchorMax = new Vector2(0.5f, 1f);
        camRing.pivot = new Vector2(0.5f, 0.5f);
        camRing.anchoredPosition = new Vector2(39f, BezelMidY);
        camRing.sizeDelta = new Vector2(10f, 10f);
        var camRingImg = camRing.gameObject.AddComponent<Image>();
        camRingImg.sprite = Disc();
        camRingImg.color  = new Color(0.04f, 0.08f, 0.13f, 1f); // dark housing
        camRingImg.raycastTarget = false;

        var camCore = NewUI("CameraCore", camRing);
        camCore.anchorMin = new Vector2(0.5f, 0.5f); camCore.anchorMax = new Vector2(0.5f, 0.5f);
        camCore.pivot = new Vector2(0.5f, 0.5f);
        camCore.anchoredPosition = Vector2.zero;
        camCore.sizeDelta = new Vector2(5f, 5f);
        var camCoreImg = camCore.gameObject.AddComponent<Image>();
        camCoreImg.sprite = Disc();
        camCoreImg.color  = AccentCyan;
        camCoreImg.raycastTarget = false;

        BuildScreen();
        BuildCloseButtonOnBezel();
        BuildMovementWarning();

        // Mount arm — child of the chassis so the assembly flips as one.
        // First sibling: renders right after the chassis bg, under all the
        // other chrome, with only the clamp bracket overlapping the bezel.
        _armRT = NewUI("MountArm", _phoneRT);
        _armRT.SetAsFirstSibling();
        _armRT.anchorMin = new Vector2(0.5f, 0f);
        _armRT.anchorMax = new Vector2(0.5f, 0f);
        _armRT.pivot     = new Vector2(0.5f, 1f);
        _armRT.anchoredPosition = new Vector2(0f, ArmLocalOverlap);
        _armRT.sizeDelta = new Vector2(ArmLocalHeight * ArmAspect, ArmLocalHeight);
        _armImage = _armRT.gameObject.AddComponent<RawImage>();
        _armImage.raycastTarget = false;
        _armImage.enabled = false;   // until the art texture resolves (see Update)
    }

    void BuildMovementWarning()
    {
        // Parented to the root canvas (NOT _phoneRT) so the phone's close
        // slide+fade doesn't carry the warning off-screen with it — the
        // toast needs to persist for 2 s + 0.5 s fade after the phone is
        // gone. Anchored bottom-center, well above the hotbar (hotbar's
        // top edge is ~156 px from screen bottom; warning sits at 175 px
        // with pivot at the bottom so the text rises above that).
        var rt = NewUI("MovementWarning", transform);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 175f);
        rt.sizeDelta = new Vector2(560f, 40f);

        _warningGroup = rt.gameObject.AddComponent<CanvasGroup>();
        _warningGroup.alpha = 0f;
        _warningGroup.blocksRaycasts = false;

        _warningText = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_warningText);
        _warningText.text = "Cannot move while using phone";
        _warningText.fontSize = 24f;
        _warningText.color = AccentCyan;
        _warningText.alignment = TextAlignmentOptions.Center;
        _warningText.enableWordWrapping = false;
        _warningText.raycastTarget = false;
        _warningText.fontStyle = FontStyles.Italic;
        var glow = _warningText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        glow.effectColor = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.45f);
        glow.effectDistance = Vector2.zero;
    }

    void ShowMovementWarning()
    {
        _warningShownAt = Time.unscaledTime;
    }

    void UpdateMovementWarning()
    {
        if (_warningGroup == null) return;
        float elapsed = Time.unscaledTime - _warningShownAt;
        float target;
        if (elapsed < 0f) target = 0f;
        else if (elapsed < WarningHoldSeconds) target = 1f;
        else if (elapsed < WarningHoldSeconds + WarningFadeSeconds)
            target = 1f - (elapsed - WarningHoldSeconds) / WarningFadeSeconds;
        else target = 0f;
        if (!Mathf.Approximately(_warningGroup.alpha, target))
            _warningGroup.alpha = target;
    }

    void AddSideButton(string name, float anchorY, float height, bool leftSide)
    {
        var rt = NewUI(name, _phoneRT);
        rt.anchorMin = new Vector2(leftSide ? 0f : 1f, anchorY);
        rt.anchorMax = new Vector2(leftSide ? 0f : 1f, anchorY);
        rt.pivot     = new Vector2(leftSide ? 1f : 0f, 0.5f);
        rt.anchoredPosition = new Vector2(leftSide ? 3f : -3f, 0f);
        rt.sizeDelta = new Vector2(4f, height);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = ButtonGrey;
        img.raycastTarget = false;
    }

    void BuildScreen()
    {
        // Bezels — 42 px top + 42 px bottom (was 56 px, originally 22 px).
        // Top bezel carries the centered speaker + camera lens; bottom
        // bezel carries the small Close button. 42 keeps enough room for
        // both fixtures while looking trimmer than the previous 56.
        _screenRT = NewUI("Screen", _phoneRT);
        _screenRT.anchorMin = Vector2.zero; _screenRT.anchorMax = Vector2.one;
        _screenRT.offsetMin = new Vector2(12f, 42f);
        _screenRT.offsetMax = new Vector2(-12f, -42f);

        var screenImg = _screenRT.gameObject.AddComponent<Image>();
        screenImg.color = ScreenBg;
        screenImg.raycastTarget = true; // swallows misses so they don't fall through
        _screenMask = _screenRT.gameObject.AddComponent<RectMask2D>();

        var vlg = _screenRT.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 8, 8);   // wider side gutters for the 4:3 screen
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth  = true;  vlg.childControlHeight  = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        BuildStatusBar();
        BuildNotificationStrip();
        BuildPageHost();
        BuildReservedZone();
        BuildCameraButton();

        // All pages built — show page 0 by default.
        GoToPage(0);

        // In-phone app host — full-screen panel OVER the home content that
        // the Build / Fishingdex apps render into (the AI-chat model: use
        // the app inside the phone, back out to home). Sits below the
        // camera-mode overlays so entering the camera covers it.
        _appHostRT = NewUI("AppHost", _screenRT);
        _appHostRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _appHostRT.anchorMin = Vector2.zero; _appHostRT.anchorMax = Vector2.one;
        _appHostRT.offsetMin = Vector2.zero; _appHostRT.offsetMax = Vector2.zero;
        _appHostRT.gameObject.SetActive(false);

        // Opaque black backdrop — sibling BEFORE the live camera RawImage so
        // it draws BEHIND it but ABOVE every home-screen widget. Hides the
        // home content even if the RenderTexture has alpha holes (skybox
        // clear leaves alpha=0 in sky pixels, which is what was making the
        // apps show through the live feed).
        var backdrop = NewUI("CameraBackdrop", _screenRT);
        backdrop.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
        backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
        _cameraBackdrop = backdrop.gameObject.AddComponent<Image>();
        _cameraBackdrop.color = Color.black;
        _cameraBackdrop.raycastTarget = false;
        _cameraBackdrop.enabled = false;

        // Camera-mode live view — RawImage that covers the entire screen.
        // Hidden by default; shown only while IsCameraMode == true. Set to
        // ignoreLayout so the screen's VerticalLayoutGroup doesn't try to
        // stack it as a sibling row. Stacked last (highest sibling index)
        // so it draws ON TOP of the home page when visible.
        var camView = NewUI("CameraView", _screenRT);
        camView.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        camView.anchorMin = Vector2.zero; camView.anchorMax = Vector2.one;
        camView.offsetMin = Vector2.zero; camView.offsetMax = Vector2.zero;
        _cameraView = camView.gameObject.AddComponent<RawImage>();
        _cameraView.raycastTarget = false;
        _cameraView.color = Color.white;
        _cameraView.enabled = false;

        // Captured-photo overlay — shown for 3s right after a snap, then
        // shrinks away to reveal the live feed underneath. Sibling-stacked
        // ABOVE _cameraView so it occludes the live feed while displayed.
        _capturedRT = NewUI("CapturedView", _screenRT);
        _capturedRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _capturedRT.anchorMin = Vector2.zero; _capturedRT.anchorMax = Vector2.one;
        _capturedRT.offsetMin = Vector2.zero; _capturedRT.offsetMax = Vector2.zero;
        _capturedView = _capturedRT.gameObject.AddComponent<RawImage>();
        _capturedView.raycastTarget = false;
        _capturedView.color = Color.white;
        _capturedView.enabled = false;
    }

    // ── Status bar (time + battery) ─────────────────────────────────

    void BuildStatusBar()
    {
        // The previous version used a HorizontalLayoutGroup with childForceExpandHeight=true
        // which stretched the battery shell vertically and produced a mangled icon.
        // Rewritten with explicit absolute positioning — no HLGs in here, so each piece
        // (time text, pct text, battery shell, nub, fill) lands at exactly the size we set.
        _statusBarRT = NewUI("StatusBar", _screenRT);
        _statusBarRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        // Time text — anchored to the LEFT side of the status bar.
        var timeRT = NewUI("Time", _statusBarRT);
        timeRT.anchorMin = new Vector2(0f, 0f);
        timeRT.anchorMax = new Vector2(0.5f, 1f);
        timeRT.offsetMin = new Vector2(6f, 0f);
        timeRT.offsetMax = Vector2.zero;
        _timeText = timeRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_timeText);
        _timeText.text = "--:--";
        _timeText.fontSize = 11f;
        _timeText.color = AccentCyan;
        _timeText.alignment = TextAlignmentOptions.MidlineLeft;
        _timeText.enableWordWrapping = false;
        _timeText.raycastTarget = false;

        // Battery percent text — sits to the LEFT of the battery shell.
        var pctRT = NewUI("Pct", _statusBarRT);
        pctRT.anchorMin = new Vector2(1f, 0f);
        pctRT.anchorMax = new Vector2(1f, 1f);
        pctRT.pivot = new Vector2(1f, 0.5f);
        pctRT.anchoredPosition = new Vector2(-34f, 0f); // leave 34 px for shell + nub + gap
        pctRT.sizeDelta = new Vector2(36f, 0f);
        _batteryText = pctRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(_batteryText);
        _batteryText.text = "--%";
        _batteryText.fontSize = 11f;
        _batteryText.color = AccentCyan;
        _batteryText.alignment = TextAlignmentOptions.MidlineRight;
        _batteryText.enableWordWrapping = false;
        _batteryText.raycastTarget = false;

        // Battery shell — small fixed-size horizontal cell on the FAR RIGHT.
        var shell = NewUI("Shell", _statusBarRT);
        shell.anchorMin = new Vector2(1f, 0.5f);
        shell.anchorMax = new Vector2(1f, 0.5f);
        shell.pivot = new Vector2(1f, 0.5f);
        shell.anchoredPosition = new Vector2(-8f, 0f); // small gap for the nub on the right
        shell.sizeDelta = new Vector2(22f, 10f);
        var shellImg = shell.gameObject.AddComponent<Image>();
        shellImg.sprite = RoundedRectOutline(3); // tiny radius for a 22×10 battery cell — almost rectangular
        shellImg.type   = Image.Type.Sliced;
        shellImg.color  = AccentCyan;
        shellImg.raycastTarget = false;

        // Battery nub — tiny positive terminal on the right of the shell.
        var nub = NewUI("Nub", _statusBarRT);
        nub.anchorMin = new Vector2(1f, 0.5f);
        nub.anchorMax = new Vector2(1f, 0.5f);
        nub.pivot = new Vector2(1f, 0.5f);
        nub.anchoredPosition = new Vector2(-5f, 0f);
        nub.sizeDelta = new Vector2(3f, 4f);
        var nubImg = nub.gameObject.AddComponent<Image>();
        nubImg.color = AccentCyan;
        nubImg.raycastTarget = false;

        // Battery fill — inside the shell, width scaled to _batteryPct/100 every Update.
        _batteryFill = NewUI("Fill", shell);
        _batteryFill.anchorMin = new Vector2(0f, 0f);
        _batteryFill.anchorMax = new Vector2(0f, 1f); // anchorMax.x updated live in RefreshStatusBar
        _batteryFill.pivot = new Vector2(0f, 0.5f);
        _batteryFill.offsetMin = new Vector2(2f, 2f);
        _batteryFill.offsetMax = new Vector2(-2f, -2f);
        var fillImg = _batteryFill.gameObject.AddComponent<Image>();
        fillImg.color = AccentCyan;
        fillImg.raycastTarget = false;
    }

    // ── Notification strip ──────────────────────────────────────────

    void BuildNotificationStrip()
    {
        // Previous version used HLG with childForceExpandHeight=true, which stretched
        // the LED dot vertically into a thin bar. Now using absolute positioning so the
        // dot stays a 6×6 circle and the label flows next to it.
        _notificationStripRT = NewUI("NotificationStrip", _screenRT);
        _notificationStripRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        var bg = _notificationStripRT.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(8); // gentle rounding, not a pill
        bg.type   = Image.Type.Sliced;
        bg.raycastTarget = false;

        // LED dot — small circle, fixed position on the LEFT.
        var dot = NewUI("Dot", _notificationStripRT);
        dot.anchorMin = new Vector2(0f, 0.5f);
        dot.anchorMax = new Vector2(0f, 0.5f);
        dot.pivot = new Vector2(0f, 0.5f);
        dot.anchoredPosition = new Vector2(8f, 0f);
        dot.sizeDelta = new Vector2(6f, 6f);
        var dotImg = dot.gameObject.AddComponent<Image>();
        dotImg.sprite = Disc();
        dotImg.color = AccentCyan;
        dotImg.raycastTarget = false;

        // Label — fills the remaining width after the dot.
        var labelRT = NewUI("Label", _notificationStripRT);
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(20f, 0f); // leaves room for the dot + small gap
        labelRT.offsetMax = new Vector2(-8f, 0f);
        var label = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(label);
        label.text = "NO NEW ALERTS";
        label.fontSize = 10f;
        label.color = LabelWhite;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 2f;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
    }

    // ── 2×2 app grid ────────────────────────────────────────────────

    // Builds the page host (170 px slot in the screen VLG) plus the three
    // overlapping page roots. Each page root is anchored full-stretch inside
    // _pageHostRT so they occupy the same area; only one is SetActive(true)
    // at a time. GoToPage handles the toggle.
    void BuildPageHost()
    {
        _pageHostRT = NewUI("PageHost", _screenRT);
        _pageHostRT.gameObject.AddComponent<LayoutElement>().preferredHeight = 170f;

        BuildAppsPage();    // _pageRoots[0] — 6 tiles incl. AI
        BuildVitalsPage();  // _pageRoots[1]
        BuildQuestsPage();  // _pageRoots[2]
    }

    void BuildAppsPage()
    {
        _appGridRT = NewUI("AppsPage", _pageHostRT);
        _pageRoots[0] = _appGridRT;
        // Full-stretch so the GridLayoutGroup's centered children sit inside
        // the host slot — same area whether we're page 0, 1, or 2.
        _appGridRT.anchorMin = Vector2.zero; _appGridRT.anchorMax = Vector2.one;
        _appGridRT.offsetMin = Vector2.zero; _appGridRT.offsetMax = Vector2.zero;

        var grid = _appGridRT.gameObject.AddComponent<GridLayoutGroup>();
        // 6 tiles → 3 columns × 2 rows on the 4:3 landscape screen. Width
        // budget: 3*110 + 2*10 + 8 = 358 (centered in ~546); height budget
        // 170 px: 2*66 + 10 + 8 = 150.
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.spacing = new Vector2(10f, 10f);
        grid.cellSize = new Vector2(110f, 66f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        // Glyphs are uppercase ASCII letters — the bundled LiberationSans SDF
        // doesn't include the unicode-block symbols (⌬ ▦ ⚙ ◎) that were here
        // originally, so they rendered as missing-character squares. Letters
        // are universally supported and read clearly at this size.
        _appButtons[0] = BuildAppTile(AppKind.Fishingdex, "F", "Fishingdex");
        _appButtons[1] = BuildAppTile(AppKind.Build,      "B", "Build");
        _appButtons[2] = BuildAppTile(AppKind.Settings,   "S", "Settings");
        _appButtons[3] = BuildAppTile(AppKind.Map,        "M", "Map");
        _appButtons[4] = BuildAppTile(AppKind.Photos,     "P", "Photos");
        _appButtons[5] = BuildAITile(_appGridRT);   // AI chat — 6th tile, 3×2 grid
    }

    // Unread-notification badge on the AI app tile + the count of lines the
    // player has already "seen" (i.e. been past the chat panel while they
    // were there). UpdateAIUnreadBadge syncs the badge each frame.
    Image _aiUnreadBadge;
    int   _aiLastSeenLineCount;

    // The one functional tile on the AI page — opens the chat screen.
    Button BuildAITile(RectTransform parent)
    {
        var rt = NewUI("App_AI", parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(14);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;

        AddCornerBracket(rt, new Vector2(0f, 1f), 1.5f);
        AddCornerBracket(rt, new Vector2(1f, 0f), 1.5f);

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 6, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        var glyph = MakeText(rt, "?", 22, AccentCyan, TextAnchor.MiddleCenter);
        glyph.fontStyle = FontStyles.Bold;
        glyph.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var label = MakeText(rt, "AI", 9, LabelWhite, TextAnchor.MiddleCenter);
        label.characterSpacing = 1f;
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        // Notification badge — small red HAL eye in the top-right corner of
        // the AI tile. Visible when HALVolunteeredLog has more entries than
        // the player has "seen" (i.e. been in the AI chat for). ignoreLayout
        // so the VerticalLayoutGroup doesn't try to stack it as a row.
        var badgeRT = NewUI("UnreadBadge", rt);
        badgeRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        badgeRT.anchorMin = new Vector2(1f, 1f);
        badgeRT.anchorMax = new Vector2(1f, 1f);
        badgeRT.pivot     = new Vector2(1f, 1f);
        badgeRT.anchoredPosition = new Vector2(-6f, -6f);
        badgeRT.sizeDelta = new Vector2(11f, 11f);
        _aiUnreadBadge = badgeRT.gameObject.AddComponent<Image>();
        _aiUnreadBadge.sprite = HALVisuals.Disc();
        _aiUnreadBadge.color  = HALVisuals.EyeRed;
        _aiUnreadBadge.raycastTarget = false;
        _aiUnreadBadge.enabled = false; // hidden until there's something unread

        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(EnterAIChat);
        return btn;
    }

    // Keeps the unread badge in sync with HALVolunteeredLog. While the chat
    // is open, the player is actively reading lines as they arrive, so we
    // continuously bump the "seen" count — no badge stays lit after the
    // player closes the chat. Update() polls this every frame.
    void UpdateAIUnreadBadge()
    {
        if (_aiUnreadBadge == null) return;
        var log = HALVolunteeredLog.Instance;
        if (log == null)
        {
            if (_aiUnreadBadge.enabled) _aiUnreadBadge.enabled = false;
            return;
        }

        if (_activeChat != null)
        {
            // Chat is open → consume new lines as they arrive.
            _aiLastSeenLineCount = log.Lines.Count;
            if (_aiUnreadBadge.enabled) _aiUnreadBadge.enabled = false;
            return;
        }

        bool hasUnread = log.Lines.Count > _aiLastSeenLineCount;
        if (_aiUnreadBadge.enabled != hasUnread) _aiUnreadBadge.enabled = hasUnread;
    }

    // Stub tile — shows a "Coming soon" pill on tap.
    Button BuildStubTile(RectTransform parent, string glyph, string label, string stubName)
    {
        var rt = NewUI($"App_Stub_{stubName}", parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(14);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;

        AddCornerBracket(rt, new Vector2(0f, 1f), 1.5f);
        AddCornerBracket(rt, new Vector2(1f, 0f), 1.5f);

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 6, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        var glyphText = MakeText(rt, glyph, 22, ButtonGrey, TextAnchor.MiddleCenter);
        glyphText.fontStyle = FontStyles.Bold;
        glyphText.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var labelText = MakeText(rt, label, 9, ButtonGrey, TextAnchor.MiddleCenter);
        labelText.characterSpacing = 1f;
        labelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        var btn = rt.gameObject.AddComponent<Button>();
        var capturedLabel = label;
        btn.onClick.AddListener(() => OpenComingSoon(capturedLabel));
        return btn;
    }

    Coroutine _comingSoonRoutine;
    GameObject _comingSoonPill;

    void OpenComingSoon(string label)
    {
        if (_comingSoonRoutine != null) StopCoroutine(_comingSoonRoutine);
        if (_comingSoonPill != null) Destroy(_comingSoonPill);

        var pill = NewUI("ComingSoonPill", _screenRT);
        pill.anchorMin = new Vector2(0.5f, 0.5f);
        pill.anchorMax = new Vector2(0.5f, 0.5f);
        pill.pivot     = new Vector2(0.5f, 0.5f);
        pill.sizeDelta = new Vector2(160f, 30f);
        pill.anchoredPosition = Vector2.zero;

        var bg = pill.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(15);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false;

        var text = MakeText(pill, $"Coming soon — {label}", 11, AccentCyan, TextAnchor.MiddleCenter);
        text.characterSpacing = 1f;

        _comingSoonPill = pill.gameObject;
        _comingSoonRoutine = StartCoroutine(DestroyAfter(_comingSoonPill, 1.5f));
    }

    System.Collections.IEnumerator DestroyAfter(GameObject go, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (go != null) Destroy(go);
    }

    AIChatScreen _activeChat;

    void EnterAIChat()
    {
        // Kick the LLM load now (4-5s on GPU first time, instant if already
        // loaded). Gated on InputSettings.aiEnabled inside BeginPreload —
        // if the toggle is off, this is a no-op and the chat opens but
        // can't talk to the model. Releasing this VRAM during gameplay
        // is why the model is no longer preloaded at scene start.
        if (LLMService.Instance != null) LLMService.Instance.BeginPreload();

        // Hide all page-host children so they don't render under the chat.
        for (int i = 0; i < PageCount; i++)
            if (_pageRoots[i] != null)
                _pageRoots[i].gameObject.SetActive(false);

        // Parent the chat to _screenRT (not _pageHostRT) so it overlays the
        // entire screen — covering the status bar, nav dots, scroll arrows,
        // and camera button as well as the page host. ignoreLayout keeps the
        // screen's VerticalLayoutGroup from trying to stack it as a row.
        var go = new GameObject("AIChatScreen", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_screenRT, false);
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _activeChat = go.AddComponent<AIChatScreen>();
        _activeChat.Init(OnChatExit);
    }

    void OnChatExit()
    {
        _activeChat = null;
        // Free the LLM's VRAM/RAM now that the player is leaving the chat.
        // Frees ~6 GB on GPU (Hermes-8B Q4_K_M) — drops the game's GPU
        // bottleneck back to whatever the chosen graphics settings demand.
        // Re-loaded on the next EnterAIChat (4-5 s first-token delay).
        if (LLMService.Instance != null) LLMService.Instance.UnloadModel();
        // Restore the AI-apps page (the one the player tapped from).
        for (int i = 0; i < PageCount; i++)
            if (_pageRoots[i] != null)
                _pageRoots[i].gameObject.SetActive(i == _currentPage);
    }

    // ── Page 2: Vitals ──────────────────────────────────────────────

    void BuildVitalsPage()
    {
        var pageRT = NewUI("VitalsPage", _pageHostRT);
        _pageRoots[1] = pageRT;
        pageRT.anchorMin = Vector2.zero; pageRT.anchorMax = Vector2.one;
        pageRT.offsetMin = Vector2.zero; pageRT.offsetMax = Vector2.zero;

        var vlg = pageRT.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 16, 16);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;  vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        _vitalFills[0] = BuildVitalRow(pageRT, "HUNGER");
        _vitalFills[1] = BuildVitalRow(pageRT, "THIRST");
        _vitalFills[2] = BuildVitalRow(pageRT, "HEALTH");
        _vitalFills[3] = BuildVitalRow(pageRT, "SHIP PWR");
    }

    RectTransform BuildVitalRow(RectTransform parent, string labelText)
    {
        var row = NewUI($"Row_{labelText}", parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;  hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var label = MakeText(row, labelText, 9, AccentCyan, TextAnchor.MiddleLeft);
        label.characterSpacing = 1f;
        var labelLE = label.gameObject.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 54f;
        labelLE.preferredHeight = 14f;

        // Bar background: dark cyan-tinted track.
        var trackRT = NewUI("Track", row);
        var trackLE = trackRT.gameObject.AddComponent<LayoutElement>();
        trackLE.flexibleWidth = 1f;
        trackLE.preferredHeight = 7f;
        var trackImg = trackRT.gameObject.AddComponent<Image>();
        trackImg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.10f);
        trackImg.sprite = RoundedRectFilled(3);
        trackImg.type = Image.Type.Sliced;
        trackImg.raycastTarget = false;

        // Bar fill — child whose anchorMax.x is the live percent.
        var fillRT = NewUI("Fill", trackRT);
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(0f, 1f); // x set live in RefreshVital
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        var fillImg = fillRT.gameObject.AddComponent<Image>();
        fillImg.color = AccentCyan;
        fillImg.sprite = RoundedRectFilled(3);
        fillImg.type = Image.Type.Sliced;
        fillImg.raycastTarget = false;

        return fillRT;
    }

    void RefreshVitals()
    {
        if (ResourceManager.Instance == null) return;
        RefreshVital(0, ResourceManager.Instance.HungerPercent);
        RefreshVital(1, ResourceManager.Instance.ThirstPercent);
        RefreshVital(2, ResourceManager.Instance.HealthPercent);
        var piloted = Ship.PilotedInstance;
        RefreshVital(3, piloted != null ? piloted.PowerPercent : 0f);
    }

    void RefreshVital(int i, float pct)
    {
        if (_vitalFills[i] == null) return;
        int rounded = Mathf.Clamp(Mathf.RoundToInt(pct * 100f), 0, 100);
        if (rounded == _lastVitalPct[i]) return;
        _lastVitalPct[i] = rounded;
        float clamped = Mathf.Clamp01(pct);
        _vitalFills[i].anchorMax = new Vector2(clamped, 1f);
    }

    // ── Page 3: Quests ──────────────────────────────────────────────

    void BuildQuestsPage()
    {
        var pageRT = NewUI("QuestsPage", _pageHostRT);
        _pageRoots[2] = pageRT;
        pageRT.anchorMin = Vector2.zero; pageRT.anchorMax = Vector2.one;
        pageRT.offsetMin = Vector2.zero; pageRT.offsetMax = Vector2.zero;

        var vlg = pageRT.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;  vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        for (int i = 0; i < VisibleQuestRows; i++)
        {
            var row = NewUI($"Quest_{i}", pageRT);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

            var dotRT = NewUI("Dot", row);
            var dotLE = dotRT.gameObject.AddComponent<LayoutElement>();
            dotLE.preferredWidth = 6f;
            dotLE.preferredHeight = 6f;
            var dotImg = dotRT.gameObject.AddComponent<Image>();
            dotImg.sprite = Disc();
            dotImg.color = AccentCyan;
            dotImg.raycastTarget = false;

            var label = MakeText(row, "", 9, LabelWhite, TextAnchor.MiddleLeft);
            label.richText = true;
            var labelLE = label.gameObject.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1f;
            labelLE.preferredHeight = 14f;

            _questRowUI[i] = new QuestRowUI { Dot = dotImg, Label = (TextMeshProUGUI)label };
        }
    }

    void RefreshQuests()
    {
        // Find the first incomplete quest and show a sliding window of
        // VisibleQuestRows centered around it (2 above + the rest below),
        // clamped so the window never runs off either end.
        int firstIncomplete = _quests.Length;
        for (int i = 0; i < _quests.Length; i++)
            if (!_quests[i].Read()) { firstIncomplete = i; break; }

        int start = Mathf.Clamp(firstIncomplete - 2, 0,
            Mathf.Max(0, _quests.Length - VisibleQuestRows));

        for (int slot = 0; slot < VisibleQuestRows; slot++)
        {
            int q = start + slot;
            var ui = _questRowUI[slot];
            if (ui.Dot == null || ui.Label == null) continue;

            if (q >= _quests.Length)
            {
                ui.Dot.enabled = false;
                ui.Label.text = "";
                continue;
            }

            bool done = _quests[q].Read();
            ui.Dot.enabled = true;
            ui.Dot.color = done ? ButtonGrey : AccentCyan;
            ui.Label.color = done ? ButtonGrey : LabelWhite;
            ui.Label.text = done ? $"<s>{_quests[q].Label}</s>" : _quests[q].Label;
        }
    }

    // ── Page navigation ─────────────────────────────────────────────

    void GoToPage(int n)
    {
        _currentPage = ((n % PageCount) + PageCount) % PageCount; // wrap, handles negatives
        for (int i = 0; i < PageCount; i++)
            if (_pageRoots[i] != null)
                _pageRoots[i].gameObject.SetActive(i == _currentPage);

        RefreshDots();
        WirePageNavExplicit();

        // Refresh page-specific content on entry — vitals tick every frame in
        // Update while page 2 is visible, quests only update on phone events
        // so we drive them here.
        if (_currentPage == 2) RefreshQuests();
    }

    // The page arrows sit far apart with the app grid diagonally above, so
    // Unity's automatic navigation "right" from the left arrow scores an app
    // tile higher than the distant right arrow. Explicit links fix the pad
    // path: left ↔ right between the arrows, up into the current page's
    // first Selectable, down to the CAMERA button. Re-wired on every page
    // flip because the up-target changes with the active page.
    void WirePageNavExplicit()
    {
        if (_prevPageBtn == null || _nextPageBtn == null) return;

        // Up-target from the arrows: on the apps page, land on the matching
        // bottom-row corner tile; on other pages, the first Selectable if any.
        Selectable upForPrev = null, upForNext = null;
        if (_currentPage == 0)
        {
            upForPrev = _appButtons[3];   // bottom-left tile (Map — 3-col grid)
            upForNext = _appButtons[5];   // bottom-right tile (AI)
            WireAppGridNav();
        }
        else
        {
            var pageRoot = (_currentPage >= 0 && _currentPage < PageCount) ? _pageRoots[_currentPage] : null;
            if (pageRoot != null)
            {
                var sels = pageRoot.GetComponentsInChildren<Selectable>(false);
                if (sels.Length > 0) { upForPrev = sels[0]; upForNext = sels[0]; }
            }
        }

        _prevPageBtn.navigation = new Navigation {
            mode          = Navigation.Mode.Explicit,
            selectOnRight = _nextPageBtn,
            selectOnUp    = upForPrev,
            selectOnDown  = _putAwayBtn,
        };
        _nextPageBtn.navigation = new Navigation {
            mode          = Navigation.Mode.Explicit,
            selectOnLeft  = _prevPageBtn,
            selectOnUp    = upForNext,
            selectOnDown  = _putAwayBtn,
        };
    }

    // Explicit navigation for the app grid — the tiles are small and
    // close together, so Unity's automatic nav frequently resolves a
    // vertical press to a diagonal neighbour. The grid is 2 ROWS × 3 COLS
    // (F B S / M P ?), index = row*3 + col. This was wired as 3×2 for the
    // old two-column layout, which made stick focus hop to tiles that
    // didn't match the visual grid at all — the "controller layout feels
    // broken" bug. Bottom row falls through to the page arrows.
    void WireAppGridNav()
    {
        for (int i = 0; i < _appButtons.Length; i++)
        {
            var b = _appButtons[i];
            if (b == null) continue;
            int row = i / 3, col = i % 3;
            Selectable down;
            if (row < 1)  down = _appButtons[i + 3];
            else          down = col == 2 ? (Selectable)_nextPageBtn : (Selectable)_prevPageBtn;
            b.navigation = new Navigation {
                mode          = Navigation.Mode.Explicit,
                selectOnLeft  = col > 0 ? _appButtons[i - 1] : null,
                selectOnRight = col < 2 ? _appButtons[i + 1] : null,
                selectOnUp    = row > 0 ? _appButtons[i - 3] : null,
                selectOnDown  = down,
            };
        }
    }

    void RefreshDots()
    {
        for (int i = 0; i < PageCount; i++)
        {
            if (_navDots[i] == null) continue;
            bool active = (i == _currentPage);
            _navDots[i].color = active ? AccentCyan : ButtonGrey;
            _navDots[i].rectTransform.localScale = active
                ? new Vector3(1.25f, 1.25f, 1f)
                : Vector3.one;
            if (_navDotGlows[i] != null) _navDotGlows[i].enabled = active;
        }
    }

    Button BuildAppTile(AppKind kind, string glyph, string label)
    {
        var rt = NewUI($"App_{kind}", _appGridRT);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(14); // app tile — ~18% radius on a 78 px square
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;

        // Scanner-bracket corners (top-left + bottom-right).
        AddCornerBracket(rt, new Vector2(0f, 1f), 1.5f);
        AddCornerBracket(rt, new Vector2(1f, 0f), 1.5f);

        var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 6, 4);
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;

        var glyphText = MakeText(rt, glyph, 22, AccentCyan, TextAnchor.MiddleCenter);
        glyphText.fontStyle = FontStyles.Bold;
        glyphText.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var labelText = MakeText(rt, label, 9, LabelWhite, TextAnchor.MiddleCenter);
        labelText.characterSpacing = 1f;
        labelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        var btn = rt.gameObject.AddComponent<Button>();
        var capturedKind = kind; // closure
        btn.onClick.AddListener(() => OnAppClicked(capturedKind));
        return btn;
    }

    void AddCornerBracket(RectTransform parentRT, Vector2 anchor, float thickness)
    {
        const float armLength = 8f;
        var h = NewUI("BracketH", parentRT);
        h.anchorMin = anchor; h.anchorMax = anchor;
        h.pivot = new Vector2(anchor.x, anchor.y);
        h.anchoredPosition = new Vector2(anchor.x == 0f ? 3f : -3f, anchor.y == 1f ? -3f : 3f);
        h.sizeDelta = new Vector2(armLength, thickness);
        var hImg = h.gameObject.AddComponent<Image>();
        hImg.color = AccentCyan; hImg.raycastTarget = false;

        var v = NewUI("BracketV", parentRT);
        v.anchorMin = anchor; v.anchorMax = anchor;
        v.pivot = new Vector2(anchor.x, anchor.y);
        v.anchoredPosition = new Vector2(anchor.x == 0f ? 3f : -3f, anchor.y == 1f ? -3f : 3f);
        v.sizeDelta = new Vector2(thickness, armLength);
        var vImg = v.gameObject.AddComponent<Image>();
        vImg.color = AccentCyan; vImg.raycastTarget = false;
    }

    void OnAppClicked(AppKind kind)
    {
        // Photos zooms INTO the phone instead of sliding it away.
        if (kind == AppKind.Photos) { OpenPhotosApp(); return; }
        // Build + Fishingdex run INSIDE the tablet screen (the AI-chat
        // model) — no more separate fullscreen panels.
        if (kind == AppKind.Build || kind == AppKind.Fishingdex) { OpenPhoneApp(kind); return; }
        // Everything else (Settings / Map): slide the phone out, THEN open
        // the target UI — like tapping an app on a real phone.
        StartCoroutine(CloseThenOpen(kind));
    }

    // ── In-phone apps (Build / Fishingdex) ──────────────────────────
    RectTransform _appHostRT;
    PhoneAppBase _activeApp;
    PhoneBuildApp _buildApp;
    PhoneFishdexApp _fishdexApp;

    /// True while an in-phone app (Build / Fishingdex) covers the home screen.
    public bool AppViewOpen => _activeApp != null;

    void OpenPhoneApp(AppKind kind)
    {
        if (_appHostRT == null) return;
        ClosePhoneApp();
        _appHostRT.gameObject.SetActive(true);
        switch (kind)
        {
            case AppKind.Build:
                if (_buildApp == null) _buildApp = _appHostRT.gameObject.AddComponent<PhoneBuildApp>();
                _activeApp = _buildApp;
                break;
            case AppKind.Fishingdex:
                if (_fishdexApp == null) _fishdexApp = _appHostRT.gameObject.AddComponent<PhoneFishdexApp>();
                _activeApp = _fishdexApp;
                break;
            default:
                _appHostRT.gameObject.SetActive(false);
                return;
        }
        _activeApp.OpenApp(_appHostRT);
    }

    /// Back out of the in-phone app to the home screen. Safe to call when
    /// no app is open.
    public void ClosePhoneApp()
    {
        if (_activeApp != null) _activeApp.CloseApp();
        _activeApp = null;
        if (_appHostRT != null) _appHostRT.gameObject.SetActive(false);
        // Pad focus was on a row inside the app (just deactivated) — hand it
        // back to the home grid so stick navigation keeps working and the
        // movement-close check doesn't read the next nudge as walking.
        if (IsOpen && !_isAnimating && TutorialGate.ControllerEnabled
            && _appButtons[0] != null && _appButtons[0].gameObject.activeInHierarchy)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(_appButtons[0].gameObject);
        }
    }

    /// <summary>Photos tile entry point — rotate-and-grow into the gallery.</summary>
    public void OpenPhotosApp()
    {
        if (_inGalleryTransition || !IsOpen || _isAnimating) return;
        if (_galleryTransition != null) StopCoroutine(_galleryTransition);
        _galleryTransition = StartCoroutine(GalleryEnterRoutine());
    }

    /// <summary>Gallery's back-out entry point — reverse transition to the hand.</summary>
    public void BeginGalleryExit()
    {
        if (_inGalleryTransition) return;
        if (_galleryTransition != null) StopCoroutine(_galleryTransition);
        _galleryTransition = StartCoroutine(GalleryExitRoutine());
    }

    // The chassis is already landscape 4:3 — no rotation needed; scale so it
    // overflows the canvas on both axes (the bezel ends off-screen and the
    // screen interior covers the viewport). 1.10 = margin.
    float GalleryTargetScale()
    {
        // Fit the SCREEN INTERIOR (chassis minus 12px side / 42px top+bottom
        // bezels), not the chassis — otherwise the bezel peeks at the
        // viewport edges during the crossfade.
        var parent = (RectTransform)_phoneRT.parent;
        return Mathf.Max(parent.rect.width  / (PhoneWidth  - 24f),
                         parent.rect.height / (PhoneHeight - 84f)) * 1.10f;
    }

    System.Collections.IEnumerator GalleryEnterRoutine()
    {
        _inGalleryTransition = true;
        // Movement gates on isInDialogue (not LookBlocked), and our Update
        // early-return suppresses the WASD-auto-close — so assert the gate
        // ourselves for the whole tween. The gallery takes ownership of it
        // in OpenForTransition; the null-gallery fallback releases it below.
        PlayerController.isInDialogue = true;
        HideHintNow();
        // Kill competing tweens (orientation / slide).
        if (_rotateCoroutine != null) { StopCoroutine(_rotateCoroutine); _rotateCoroutine = null; }
        if (_animCoroutine   != null) { StopCoroutine(_animCoroutine);   _animCoroutine = null; _isAnimating = false; }
        // RectMask2D mis-culls children while the chassis rotates — same
        // reason RotatePhoneRoutine disables it for its tween.
        if (_screenMask != null) _screenMask.enabled = false;

        yield return GalleryTween(0f, 0f, PhoneScale, GalleryTargetScale(),
                                  _phoneRT.anchoredPosition.y, 0f, GalleryGrowDuration);

        // Crossfade: the gallery fades in over the now screen-filling phone.
        var gallery = PhotoGalleryUI.Instance;
        if (gallery != null)
        {
            gallery.OpenForTransition(); // gates on, grid populated, alpha 0
            float t = 0f;
            while (t < GalleryFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                gallery.SetTransitionAlpha(Mathf.Clamp01(t / GalleryFadeDuration));
                yield return null;
            }
            gallery.SetTransitionAlpha(1f);
        }
        else
        {
            Debug.LogWarning("[PlayerPhoneUI] PhotoGalleryUI.Instance is null");
            PlayerController.isInDialogue = false; // nobody took ownership — don't brick the player
        }

        // Park the phone closed WITHOUT touching the cursor — the gallery's
        // isInDialogue gate + unlocked cursor own the input state now.
        HideForGallery();
        _inGalleryTransition = false;
        _galleryTransition = null;
    }

    System.Collections.IEnumerator GalleryExitRoutine()
    {
        _inGalleryTransition = true;
        // Stage the phone exactly where the enter transition left it —
        // rotated, screen-filling, visible — hidden UNDER the gallery.
        if (_screenMask != null) _screenMask.enabled = false;
        GoToPage(0);
        IsOpen = true;
        _phoneGroup.alpha = 1f;
        _phoneGroup.blocksRaycasts = true;
        float bigScale = GalleryTargetScale();
        _phoneRT.localRotation = Quaternion.identity;   // landscape chassis fills the screen unrotated
        _phoneRT.localScale = new Vector3(bigScale, bigScale, 1f);
        _phoneRT.anchoredPosition = Vector2.zero;

        // Gallery fades out revealing the oversized phone…
        var gallery = PhotoGalleryUI.Instance;
        if (gallery != null)
        {
            float t = 0f;
            while (t < GalleryFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                gallery.SetTransitionAlpha(1f - Mathf.Clamp01(t / GalleryFadeDuration));
                yield return null;
            }
            // Cursor stays unlocked — the (open) phone owns it now.
            gallery.CloseForPhoneReturn();
            // CloseForPhoneReturn released isInDialogue — re-assert it for the
            // shrink tween (Update's early-return suppresses WASD-auto-close).
            PlayerController.isInDialogue = true;
        }

        // …then the phone shrinks back onto the chest arm.
        yield return GalleryTween(0f, 0f, bigScale, PhoneScale, 0f, OnScreenY, GalleryGrowDuration);

        PlayerController.isInDialogue = false; // hand back to the normal open-phone state
        if (_screenMask != null) _screenMask.enabled = true;
        _isLandscape = false;
        _inGalleryTransition = false;
        _galleryTransition = null;
    }

    System.Collections.IEnumerator GalleryTween(float fromDeg, float toDeg, float fromScale, float toScale,
                                                float fromY, float toY, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            float eased = u * u * (3f - 2f * u); // smoothstep ease-in-out
            _phoneRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromDeg, toDeg, eased));
            float s = Mathf.Lerp(fromScale, toScale, eased);
            _phoneRT.localScale = new Vector3(s, s, 1f);
            _phoneRT.anchoredPosition = new Vector2(0f, Mathf.Lerp(fromY, toY, eased));
            yield return null;
        }
        _phoneRT.localRotation = Quaternion.Euler(0f, 0f, toDeg);
        _phoneRT.localScale = new Vector3(toScale, toScale, 1f);
        _phoneRT.anchoredPosition = new Vector2(0f, toY);
    }

    // Park the phone in its normal closed state without cursor/nav changes
    // (the gallery owns input while it's up).
    void HideForGallery()
    {
        IsOpen = false;
        _phoneGroup.alpha = 0f;
        _phoneGroup.blocksRaycasts = false;
        _isLandscape = false;
        _flipAngle = FlipClosedAngle;   // next Open flips up from the stowed pose
        _phoneRT.localRotation = Quaternion.identity;
        _phoneRT.localScale = new Vector3(PhoneScale, PhoneScale, 1f);
        _phoneRT.anchoredPosition = new Vector2(0f, OffScreenY);
        if (_screenMask != null) _screenMask.enabled = true;
    }

    System.Collections.IEnumerator CloseThenOpen(AppKind kind)
    {
        Close();
        yield return new WaitWhile(() => _isAnimating);

        switch (kind)
        {
            case AppKind.Fishingdex:
                if (FishingdexManager.Instance != null) FishingdexManager.Instance.OpenFishingdex();
                else Debug.LogWarning("[PlayerPhoneUI] FishingdexManager.Instance is null");
                break;
            case AppKind.Build:
                if (BuildMenuUI.Instance != null) BuildMenuUI.Instance.Open();
                else Debug.LogWarning("[PlayerPhoneUI] BuildMenuUI.Instance is null");
                break;
            case AppKind.Settings:
                if (TabbedPauseMenu.Instance != null) TabbedPauseMenu.Instance.OpenAtSettings();
                else Debug.LogWarning("[PlayerPhoneUI] TabbedPauseMenu.Instance is null");
                break;
            case AppKind.Map:
                if (SolarSystemMapController.Instance != null) SolarSystemMapController.Instance.OpenMap();
                else Debug.LogWarning("[PlayerPhoneUI] SolarSystemMapController.Instance is null");
                break;
        }
    }

    // ── Reserved zone + Put Away button ─────────────────────────────

    // Bold Tactile page-nav: [◀]   ●  ○  ○   [▶]
    // Replaces the old "— RESERVED —" placeholder. Left/right buttons flip
    // pages; 3 dots indicate which page is active (active = cyan + 1.25×
    // scale + glow). Wraps modulo 3 in both directions.
    void BuildReservedZone()
    {
        _reservedZoneRT = NewUI("PageNav", _screenRT);
        // preferredHeight gives the row at least enough vertical room for the
        // 24-px buttons + padding. flexibleHeight=1 lets it absorb any leftover
        // screen space the same way the old "— RESERVED —" zone did, so the
        // CAMERA button stays anchored to the bottom of the screen. The
        // buttons themselves stay at 32×24 — they just float centered inside
        // the taller zone via HLG childAlignment.
        var le = _reservedZoneRT.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 30f;
        le.flexibleHeight  = 1f;

        var hlg = _reservedZoneRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 3, 3);
        // Arrows hug the dots as one centered cluster — flexible spacers used
        // to fling them to the screen edges, which read fine on the narrow
        // phone but looked broken across the 4:3 tablet's width.
        hlg.spacing = 18f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;  hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        BuildPageNavArrow(false); // left  → previous page
        BuildPageNavDots();
        BuildPageNavArrow(true);  // right → next page
    }

    void BuildPageNavArrow(bool pointRight)
    {
        var rt = NewUI(pointRight ? "NextPage" : "PrevPage", _reservedZoneRT);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = 32f;
        le.preferredHeight = 24f;

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = TileBg;
        bg.sprite = RoundedRectFilled(6);
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = true;

        var borderRT = NewUI("Border", rt);
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero; borderRT.offsetMax = Vector2.zero;
        borderRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var borderImg = borderRT.gameObject.AddComponent<Image>();
        borderImg.sprite = RoundedRectOutline(6);
        borderImg.type = Image.Type.Sliced;
        borderImg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.32f);
        borderImg.raycastTarget = false;

        // Triangle glyph centered inside the button.
        var triRT = NewUI("Tri", rt);
        triRT.anchorMin = new Vector2(0.5f, 0.5f);
        triRT.anchorMax = new Vector2(0.5f, 0.5f);
        triRT.pivot = new Vector2(0.5f, 0.5f);
        triRT.sizeDelta = new Vector2(10f, 10f);
        triRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var triImg = triRT.gameObject.AddComponent<Image>();
        triImg.sprite = Triangle(pointRight);
        triImg.color = AccentCyan;
        triImg.raycastTarget = false;

        var btn = rt.gameObject.AddComponent<Button>();
        // Hover/press tints — Unity's ColorBlock multiplies these against
        // bg.color (TileBg), so the brightened states show as a slight cyan
        // wash without us needing to wire pointer-enter events ourselves.
        var cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        cb.pressedColor     = new Color(0.7f, 0.7f, 0.7f, 1f);
        cb.selectedColor    = Color.white;
        cb.disabledColor    = new Color(0.4f, 0.4f, 0.4f, 1f);
        btn.colors = cb;
        bool right = pointRight; // capture for closure
        btn.onClick.AddListener(() => GoToPage(_currentPage + (right ? 1 : -1)));
        if (pointRight) _nextPageBtn = btn; else _prevPageBtn = btn;
    }

    // Page-nav arrow buttons, kept for explicit pad-navigation wiring
    // (see WirePageNavExplicit).
    Button _prevPageBtn;
    Button _nextPageBtn;

    void BuildPageNavDots()
    {
        var dotsRT = NewUI("Dots", _reservedZoneRT);
        var dotsLE = dotsRT.gameObject.AddComponent<LayoutElement>();
        // Width = N dots × 9 px + (N-1) gaps × 11 px. Computed from PageCount
        // so adding/removing a page doesn't quietly squash the dots into pills.
        dotsLE.preferredWidth  = PageCount * 9f + (PageCount - 1) * 11f;
        dotsLE.preferredHeight = 24f;

        var hlg = dotsRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.spacing = 11f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;  hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        for (int i = 0; i < PageCount; i++)
        {
            var dot = NewUI($"Dot_{i}", dotsRT);
            var le = dot.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 9f;
            le.preferredHeight = 9f;
            var img = dot.gameObject.AddComponent<Image>();
            img.sprite = Disc();
            img.color = ButtonGrey;
            img.raycastTarget = false;
            // Cyan glow Shadow at zero distance — enabled only on the
            // active dot, mirrors the movement-warning toast glow trick.
            var glow = dot.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            glow.effectColor = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.70f);
            glow.effectDistance = Vector2.zero;
            glow.enabled = false;

            _navDots[i] = img;
            _navDotGlows[i] = glow;
        }
    }

    // Camera launcher — bottom slot inside the screen, where Put Away used
    // to live. Clicking it enters camera mode (phone rotates 90° clockwise,
    // live world feed displayed, cursor relocks for free look).
    void BuildCameraButton()
    {
        // Full-width layout row; the button itself is a fixed-width pill
        // centered inside it — stretched edge-to-edge it read as a thin bar
        // on the 4:3 tablet.
        var row = NewUI("CameraRow", _screenRT);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var rt = NewUI("CameraButton", row);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(320f, 0f);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.10f);
        bg.sprite = RoundedRectFilled(8);
        bg.type   = Image.Type.Sliced;

        var borderRT = NewUI("Border", rt);
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero; borderRT.offsetMax = Vector2.zero;
        borderRT.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var borderImg = borderRT.gameObject.AddComponent<Image>();
        borderImg.sprite = RoundedRectOutline(8);
        borderImg.type = Image.Type.Sliced;
        borderImg.color = AccentCyan;
        borderImg.raycastTarget = false;

        var label = MakeText(rt, "CAMERA", 11, AccentCyan, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 3f;
        label.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var labelRT = label.rectTransform;
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero; labelRT.offsetMax = Vector2.zero;

        _putAwayBtn = rt.gameObject.AddComponent<Button>();
        _putAwayBtn.onClick.AddListener(EnterCameraMode);
    }

    // Small system button on the bottom bezel — replaces the in-screen Put
    // Away button. Sized smaller, just says "Close".
    void BuildCloseButtonOnBezel()
    {
        var rt = NewUI("CloseBezelButton", _phoneRT);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        // Bezel is 42 px tall; button is 22 px tall, anchored 14 px up from
        // bottom → leaves 6 px above the button (room before the screen
        // border) and 14 px below (visual breathing room from phone edge).
        // Width 54 keeps the "Close" label comfortable while looking like a
        // compact pill rather than a fat bar.
        rt.anchoredPosition = new Vector2(0f, 14f);
        rt.sizeDelta = new Vector2(54f, 22f);

        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.10f);
        bg.sprite = RoundedRectFilled(6);
        bg.type   = Image.Type.Sliced;

        var borderRT = NewUI("Border", rt);
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero; borderRT.offsetMax = Vector2.zero;
        var borderImg = borderRT.gameObject.AddComponent<Image>();
        borderImg.sprite = RoundedRectOutline(6);
        borderImg.type = Image.Type.Sliced;
        borderImg.color = AccentCyan;
        borderImg.raycastTarget = false;

        var label = MakeText(rt, "Close", 10, AccentCyan, TextAnchor.MiddleCenter);
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 2f;
        var labelRT = label.rectTransform;
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero; labelRT.offsetMax = Vector2.zero;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(Close);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    TMP_Text MakeText(Transform parent, string text, float fontSize, Color color, TextAnchor anchor)
    {
        var rt = NewUI("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.enableWordWrapping = false;
        t.alignment = TextAnchor_To_TMP(anchor);
        t.raycastTarget = false;
        return t;
    }

    static TextAlignmentOptions TextAnchor_To_TMP(TextAnchor a) => a switch
    {
        TextAnchor.MiddleLeft   => TextAlignmentOptions.MidlineLeft,
        TextAnchor.MiddleRight  => TextAlignmentOptions.MidlineRight,
        TextAnchor.MiddleCenter => TextAlignmentOptions.Midline,
        _                       => TextAlignmentOptions.Midline,
    };

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    // ── Procedural sprites (rounded all 4 corners, no chamfering) ───
    // The existing UIPanelSprites used by the rest of the HUD cuts the
    // top-left + bottom-right corners (the "scanner panel" look). For the
    // phone we want fully rounded corners — generated procedurally below,
    // 9-sliced so a single source texture serves every panel size.

    // Cached per-radius — different elements want different corner radii
    // (chassis ~26 px, screen ~18, app tile ~14, put-away/notif/zone ~8,
    // battery shell ~3). Sharing one source sprite made the small panels
    // look like pills, because 9-slice can't REDUCE the baked corner radius,
    // only stretch the centre. Cache prevents regenerating on every call.
    static readonly System.Collections.Generic.Dictionary<int, Sprite> s_filledByRadius =
        new System.Collections.Generic.Dictionary<int, Sprite>();
    static readonly System.Collections.Generic.Dictionary<int, Sprite> s_outlineByRadius =
        new System.Collections.Generic.Dictionary<int, Sprite>();
    static Sprite _disc;

    static Sprite RoundedRectFilled(int radius)
    {
        if (s_filledByRadius.TryGetValue(radius, out var cached) && cached != null) return cached;
        int size = Mathf.Max(8, radius * 2 + 4); // a few px of stretch zone in the middle
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"PhoneRoundedFilled_{radius}"
        };
        var pixels = new Color[size * size];
        int s = size - 1;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = x < radius ? radius : (x > s - radius ? s - radius : x);
            float cy = y < radius ? radius : (y > s - radius ? s - radius : y);
            float dx = x - cx, dy = y - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float a = radius > 0 ? Mathf.Clamp01(radius - dist + 0.5f) : 1f;
            pixels[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0u, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        spr.name = $"PhoneRoundedFilled_{radius}";
        s_filledByRadius[radius] = spr;
        return spr;
    }

    static Sprite RoundedRectOutline(int radius)
    {
        if (s_outlineByRadius.TryGetValue(radius, out var cached) && cached != null) return cached;
        int size = Mathf.Max(8, radius * 2 + 4);
        const int thickness = 3;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"PhoneRoundedOutline_{radius}"
        };
        var pixels = new Color[size * size];
        int s = size - 1;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float cx = x < radius ? radius : (x > s - radius ? s - radius : x);
            float cy = y < radius ? radius : (y > s - radius ? s - radius : y);
            float dx = x - cx, dy = y - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float outer = radius > 0 ? Mathf.Clamp01(radius - dist + 0.5f) : 1f;
            float inner = Mathf.Clamp01((radius - thickness) - dist + 0.5f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, outer - inner);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0u, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        spr.name = $"PhoneRoundedOutline_{radius}";
        s_outlineByRadius[radius] = spr;
        return spr;
    }

    static Sprite _ring;

    static Sprite Ring()
    {
        if (_ring != null) return _ring;
        const int size = 64;
        const int thickness = 3;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "PhoneRing"
        };
        var pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        float outerR = center;
        float innerR = center - thickness;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center, dy = y - center;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float outerA = Mathf.Clamp01(outerR - dist + 0.5f);
            float innerA = Mathf.Clamp01(innerR - dist + 0.5f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, outerA - innerA);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _ring.name = "PhoneRing";
        return _ring;
    }

    static Sprite Disc()
    {
        if (_disc != null) return _disc;
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "PhoneDisc"
        };
        var pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center, dy = y - center;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(center - dist + 0.5f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _disc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _disc.name = "PhoneDisc";
        return _disc;
    }

    static Sprite _triangleLeft, _triangleRight;

    static Sprite Triangle(bool pointRight)
    {
        if (pointRight && _triangleRight != null) return _triangleRight;
        if (!pointRight && _triangleLeft  != null) return _triangleLeft;

        const int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = pointRight ? "PhoneTriangleRight" : "PhoneTriangleLeft"
        };
        var pixels = new Color[size * size];
        // Isoceles triangle, base on one side, apex on the other. Centered
        // vertically. 1-px anti-alias band at the edge.
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Normalised coords: u = horizontal progress (0..1) from the base
            // toward the apex; v = perpendicular distance from the centre line.
            float u = pointRight ? x / (float)(size - 1) : (size - 1 - x) / (float)(size - 1);
            float halfHeight = 0.5f * (1f - u); // tapers from 0.5 at base to 0 at apex
            float v = Mathf.Abs((y / (float)(size - 1)) - 0.5f);
            float aa = 1f / size; // ~1 px feather
            float a = Mathf.Clamp01((halfHeight - v) / aa);
            pixels[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        spr.name = tex.name;
        if (pointRight) _triangleRight = spr; else _triangleLeft = spr;
        return spr;
    }

    // ── Photos-app rotate-and-grow transition (fields appended at class
    //    end per repo convention; see GalleryEnterRoutine) ────────────
    Coroutine _galleryTransition;
    bool      _inGalleryTransition;
    const float GalleryGrowDuration = 0.45f;
    const float GalleryFadeDuration = 0.12f;
}
