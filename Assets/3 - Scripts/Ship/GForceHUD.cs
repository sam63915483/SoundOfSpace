using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Bottom-left HUD card (jetpack panel). Three pieces:
///
///   1. SPEED tape — a vertical 5-row readout of movement speed (m/s),
///      relative to whichever celestial body the player is aligned to
///      (or the ship's rigidbody when piloting). The middle row is the
///      current value (highlighted); the rows above/below show ±25 and
///      ±50 m/s context so the player can tell at a glance whether they're
///      accelerating or decelerating.
///   2. 3D Thrust indicator — a small 3D "gimbal" rendered via a dedicated
///      camera + RenderTexture. A central sphere (origin marker) sits inside
///      three perpendicular cyan rings (LineRenderers in the XY / YZ / XZ
///      planes). Six small pyramidal arrows are pinned at the cardinal axes
///      (±X, ±Y, ±Z) and each points INWARD toward the center — a lit arrow
///      visualizes the thrust vector being applied to the player. "Thrust
///      opposite to motion" convention drives which arrow lights up:
///        Space (jetpack)         → bottom arrow lights (force pushes up)
///        Ctrl (down-thrust)      → top arrow lights (force pushes down)
///        W (forward-thrust)      → back arrow lights (force pushes forward)
///        S (back-thrust)         → front arrow lights (force pushes back)
///        A (left-strafe)         → right arrow lights (force pushes left)
///        D (right-strafe)        → left arrow lights (force pushes right)
///      Viewed from a fixed 3/4 angle so the three axes remain visually
///      distinct — the user reads it as a 3D direction indicator instead
///      of a flat asterisk.
///   3. Segmented fuel bars — three 8-segment vertical columns labeled
///      UP / DN / DIR, filling bottom-up from each fuel pool:
///        UP  = jetpack fuel (Space)
///        DN  = down-thrust fuel (Ctrl)
///        DIR = directional thrust fuel (WASD strafe/forward/back).
///      Lit segments use the LED cyan; unlit segments stay dim. Updates
///      are change-detected so we don't recolor 24 Images per frame.
///
/// The 3D widget lives 100,000 units above wherever the main camera is
/// (repositioned each LateUpdate). That keeps it FAR outside the main
/// camera's frustum so it never shows up in the actual game render, while
/// the dedicated indicator camera renders it up close to the texture.
///
/// Auto-creates like the other HUD singletons; seeded by
/// MainMenuController.EnsureGameplaySingletons for the build path.
/// </summary>
public class GForceHUD : MonoBehaviour
{
    public static GForceHUD Instance { get; private set; }

    /// <summary>
    /// True when this HUD owns the boost-meter rendering — the legacy scene-side
    /// BoostMeterUI suppresses itself when this is true.
    /// </summary>
    public bool OwnsBoostMeter => true;

    [Header("Layout (1920×1080 reference)")]
    public float bottomOffset = 24f;
    public float leftOffset = 20f;
    public float cardWidth = 330f;
    public float widgetSize = 110f;

    [Header("3D widget")]
    [Tooltip("RenderTexture size in pixels. 192 gives a sharp readout at 100 px UI size on a 1080p screen.")]
    public int rtResolution = 192;

    static readonly Color CardBgColor     = new Color32(0x0A, 0x18, 0x28, 0xF2);
    static readonly Color CardBorderColor = new Color32(0x78, 0xC8, 0xFF, 0x73);
    static readonly Color LedColor        = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color HeaderColor     = new Color32(0x5C, 0xC8, 0xFF, 0xD9);
    static readonly Color ValueColor      = new Color32(0x7B, 0xE2, 0xFF, 0xFF);
    static readonly Color TrackColor      = new Color32(0x0F, 0x19, 0x2A, 0xD9);
    static readonly Color CellOffColor    = new Color32(0x0F, 0x19, 0x2A, 0xE6);
    static readonly Color DimLabelColor   = new Color32(0xEA, 0xF6, 0xFF, 0x73);
    static readonly Color CurrentRowHighlight = new Color32(0x5C, 0xC8, 0xFF, 0x2E);

    static readonly Color ArrowIdle3D       = new Color(0.15f, 0.30f, 0.45f, 1f);
    static readonly Color ArrowActive3D     = new Color(0.50f, 0.95f, 1.00f, 1f);
    static readonly Color ArrowMatchRed     = new Color(1.00f, 0.30f, 0.30f, 1f);
    static readonly Color ArrowCircularizeBlue = new Color(0.00f, 0.70f, 1.00f, 1f); // electric blue
    static readonly Color CenterColor3D = new Color(0.25f, 0.50f, 0.75f, 1f);
    static readonly Color RTBgColor     = new Color(0.04f, 0.10f, 0.16f, 0.0f); // transparent

    Canvas _canvas;
    RectTransform _cardRT;
    Ship _ship;
    PlayerController _player;
    int _lastShownSpeed = int.MinValue;
    bool _wasShown;   // show-edge detect for the HudBootFX power-on

    // Speed-tape rows: index 2 is the highlighted current value.
    TextMeshProUGUI[] _tape;

    // Segmented fuel bars (8 segments per column, bottom-up fill).
    Image[] _upSegs;
    Image[] _dnSegs;
    Image[] _dirSegs;
    int _lastUp = -1, _lastDn = -1, _lastDir = -1;

    // 3D widget state.
    GameObject _widgetRoot;
    Camera _indicatorCam;
    Camera _mainCam;
    RenderTexture _thrustRT;
    Material[] _arrowMats;
    Color[] _currentArrowColors;
    RawImage _thrustImage;
    Material _ringMat;
    Mesh _arrowMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("GForceHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<GForceHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build3DWidget();
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_thrustRT != null) { _thrustRT.Release(); DestroyImmediate(_thrustRT); }
        if (_widgetRoot != null) DestroyImmediate(_widgetRoot);
        if (_indicatorCam != null && _indicatorCam.gameObject != null) DestroyImmediate(_indicatorCam.gameObject);
        if (_arrowMats != null) foreach (var m in _arrowMats) if (m != null) DestroyImmediate(m);
        if (_ringMat != null) DestroyImmediate(_ringMat);
        if (_arrowMesh != null) DestroyImmediate(_arrowMesh);
    }

    void Update()
    {
        // HUD visibility gate — show when the jetpack is unlocked OR when
        // the player is currently piloting a ship. Either context means
        // thrust is a thing the player can do, so the indicator earns its
        // corner real estate.
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        Ship pilotedForGate = FindPilotedShip();
        bool show = ((_player != null && _player.JetpackUnlocked) || pilotedForGate != null || DroneController.Active != null)
                    && !PlayerController.isMapOpen;
        if (_canvas != null && _canvas.enabled != show) _canvas.enabled = show;
        if (_indicatorCam != null && _indicatorCam.enabled != show) _indicatorCam.enabled = show;
        // Helmet-screen power-on: flicker + scanline sweep whenever the boost
        // cluster comes back on (jetpack equip / boarding a ship / drone).
        if (show && !_wasShown) HudBootFX.Play(GetComponent<CanvasGroup>(), _cardRT);
        _wasShown = show;
        if (!show) return;

        // Speed tape — five rows update together on integer-speed change.
        Vector3 vel = ReadActiveVelocity();
        int speed = Mathf.RoundToInt(vel.magnitude);
        if (speed != _lastShownSpeed && _tape != null)
        {
            _lastShownSpeed = speed;
            // Step 1 m/s — like an altimeter tape. Rows below 0 stay blank
            // instead of clamping to "0" (which would print duplicate zeros).
            _tape[0].text = (speed + 2).ToString();
            _tape[1].text = (speed + 1).ToString();
            _tape[2].text = speed.ToString();
            _tape[3].text = speed >= 1 ? (speed - 1).ToString() : "";
            _tape[4].text = speed >= 2 ? (speed - 2).ToString() : "";
        }

        // Segmented fuel bars — change-detected updates. While the player is
        // inside a cockpit, route the bars to that ship's boost fuel instead
        // of the player's jetpack fuel, so the same HUD doubles as the
        // ship's boost meter (Shift = boost mirrors Shift = sprint).
        if (_upSegs != null && _dnSegs != null && _dirSegs != null)
        {
            // Drone (Mission 1 test) takes priority, then the piloted ship, then the jetpack.
            Ship piloted = FindPilotedShip();
            var drone = DroneController.Active;
            float upPct  = drone != null ? drone.UpBoostFuelPercent   : piloted != null ? piloted.UpBoostFuelPercent   : _player.JetpackFuelPercent;
            float dnPct  = drone != null ? drone.DownBoostFuelPercent : piloted != null ? piloted.DownBoostFuelPercent : _player.DownThrustFuelPercent;
            float dirPct = drone != null ? drone.DirBoostFuelPercent  : piloted != null ? piloted.DirBoostFuelPercent  : _player.DirectionalThrustFuelPercent;
            int upLit  = Mathf.Clamp(Mathf.RoundToInt(upPct  * 8f), 0, 8);
            int dnLit  = Mathf.Clamp(Mathf.RoundToInt(dnPct  * 8f), 0, 8);
            int dirLit = Mathf.Clamp(Mathf.RoundToInt(dirPct * 8f), 0, 8);
            if (upLit  != _lastUp)  { _lastUp  = upLit;  RecolorSegs(_upSegs,  upLit);  }
            if (dnLit  != _lastDn)  { _lastDn  = dnLit;  RecolorSegs(_dnSegs,  dnLit);  }
            if (dirLit != _lastDir) { _lastDir = dirLit; RecolorSegs(_dirSegs, dirLit); }
        }

        // Arrow light fade — lerps each arrow material toward idle / manual /
        // V-assist (red) / O-assist (electric blue). V/O take priority over
        // manual cyan so the player sees WHICH system is currently firing.
        if (_arrowMats != null)
        {
            bool[] active = ReadThrustKeys();
            Ship pilotedArrows = FindPilotedShip();
            var droneArrows = DroneController.Active;
            Vector3 vIn = droneArrows != null ? droneArrows.MatchAssistInput
                        : pilotedArrows != null ? pilotedArrows.MatchAssistInput : Vector3.zero;
            Vector3 oIn = droneArrows != null ? droneArrows.CircularizeAssistInput
                        : pilotedArrows != null ? pilotedArrows.CircularizeAssistInput : Vector3.zero;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _arrowMats.Length; i++)
            {
                Color target;
                if (AssistAxisActive(vIn, i))      target = ArrowMatchRed;
                else if (AssistAxisActive(oIn, i)) target = ArrowCircularizeBlue;
                else if (active[i])                target = ArrowActive3D;
                else                               target = ArrowIdle3D;
                _currentArrowColors[i] = Color.Lerp(_currentArrowColors[i], target, dt * 10f);
                _arrowMats[i].color = _currentArrowColors[i];
            }
        }
    }

    // Maps an assist's local-space thrust input to the gimbal arrow that
    // would light up for that thrust direction. Mirrors the convention in
    // ReadThrustKeys: arrow points where exhaust goes (opposite to motion).
    static bool AssistAxisActive(Vector3 input, int arrowIdx)
    {
        const float kThreshold = 0.1f;
        switch (arrowIdx)
        {
            case 0: return input.y < -kThreshold; // +Y arrow = down-thrust (-Y input)
            case 1: return input.z < -kThreshold; // +Z arrow = back-thrust (-Z input)
            case 2: return input.x < -kThreshold; // +X arrow = left-strafe (-X input)
            case 3: return input.y >  kThreshold; // -Y arrow = up-thrust (+Y input)
            case 4: return input.z >  kThreshold; // -Z arrow = forward-thrust (+Z input)
            case 5: return input.x >  kThreshold; // -X arrow = right-strafe (+X input)
            default: return false;
        }
    }

    void LateUpdate()
    {
        // Keep the 3D widget far above the main camera so the main camera
        // frustum never reaches it. The indicator camera moves with it.
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null || _widgetRoot == null || _indicatorCam == null) return;

        Vector3 widgetPos = _mainCam.transform.position + Vector3.up * 100000f;
        _widgetRoot.transform.position = widgetPos;

        // Camera more directly behind the widget so +Z reads clearly as
        // "forward into the screen". Small +X / +Y offsets keep the other
        // axes visible at a slight 3/4 perspective.
        Vector3 camOffset = new Vector3(1.0f, 1.3f, -3.2f);
        _indicatorCam.transform.position = widgetPos + camOffset;
        _indicatorCam.transform.LookAt(widgetPos, Vector3.up);
    }

    static readonly bool[] _activeScratch = new bool[6];
    bool[] ReadThrustKeys()
    {
        // Arrows light up where THRUST IS APPLIED (opposite to motion).
        // Pressing W moves you forward, but the engine exhaust fires
        // backward — so the -Z (back) arrow lights up. Same for the other
        // axes. Mapping: arrow index = direction the arrow points; key =
        // input that fires thrust OUT in that direction.
        //
        //   Arrow index 0 = +Y (up)      → lights on Ctrl  (down-thrust pushes mass up)
        //   Arrow index 1 = +Z (forward) → lights on S     (back-thrust pushes mass forward)
        //   Arrow index 2 = +X (right)   → lights on A     (left-strafe pushes mass right)
        //   Arrow index 3 = -Y (down)    → lights on Space (jetpack pushes mass down)
        //   Arrow index 4 = -Z (back)    → lights on W     (forward-thrust pushes mass back)
        //   Arrow index 5 = -X (left)    → lights on D     (right-strafe pushes mass left)
        _activeScratch[0] = Input.GetKey(KeyCode.LeftControl);
        _activeScratch[1] = Input.GetKey(KeyCode.S);
        _activeScratch[2] = Input.GetKey(KeyCode.A);
        _activeScratch[3] = Input.GetKey(KeyCode.Space);
        _activeScratch[4] = Input.GetKey(KeyCode.W);
        _activeScratch[5] = Input.GetKey(KeyCode.D);
        return _activeScratch;
    }

    Vector3 ReadActiveVelocity()
    {
        var drone = DroneController.Active;
        if (drone != null && drone.Body != null) return drone.Body.velocity;
        if (_player == null) _player = FindObjectOfType<PlayerController>(true);
        if (_player != null && _player.isActiveAndEnabled)
            return _player.SurfaceVelocity;
        Ship piloted = FindPilotedShip();
        if (piloted != null)
        {
            var rb = piloted.GetComponent<Rigidbody>();
            if (rb != null) return rb.velocity;
        }
        if (_ship == null) _ship = FindObjectOfType<Ship>(true);
        if (_ship != null)
        {
            var rb = _ship.GetComponent<Rigidbody>();
            if (rb != null) return rb.velocity;
        }
        return Vector3.zero;
    }

    // The cached static on Ship is set by PilotShip / cleared on exit, so
    // it's authoritative for "which ship is the player currently flying."
    // This used to FindObjectsOfType<Ship>(true) per call AND was being
    // called 4× per Update — a ~2 ms cost in a busy scene.
    Ship FindPilotedShip()
    {
        return Ship.PilotedInstance;
    }

    // ── Helmet art-housing seating ─────────────────────────────────
    // Called by HelmetOverlayHUD once the art config resolves: centers the
    // card inside the art's recessed bottom-left display, scales to fit, and
    // drops the floating-card chrome (bg/border/bezels) — the helmet's own
    // screen IS the panel. Wire-tags (M/S, BOOST) stay: they read as labels.
    public void SeatInArtHousing(HelmetOverlayHUD.HousingRect h)
    {
        if (_cardRT == null) return;
        DetachProjector();
        _cardRT.anchorMin = _cardRT.anchorMax = h.anchorFrac;
        // Fixed-height card → dead-center in the glass (an off-center readout
        // is what makes it look pasted on rather than displayed by the screen).
        _cardRT.pivot = new Vector2(0.5f, 0.5f);
        _cardRT.anchoredPosition = h.contentOffset;   // hand-tuned nudge from config
        float fit = Mathf.Min(1f, (h.sizeRef.x - 12f) / 370f, (h.sizeRef.y - 16f) / 170f) * h.contentScale;
        _cardRT.localScale = new Vector3(fit, fit, 1f);
        _cardRT.localRotation = Quaternion.Euler(h.euler);   // 3D panel lean matching the painted screen
        VitalsHUD.ApplyIntegratedStyle(_cardRT);
        HelmetSway.Reregister(_cardRT);
    }

    // ── True-perspective seating ───────────────────────────────────
    // Mirrors VitalsHUD.SeatOnArtScreen: card moves into an off-screen
    // capture rig, re-drawn here as a warped quad on the painted screen.
    HudScreenProjector _projector;

    public void SeatOnArtScreen(HelmetOverlayHUD.HousingQuad q)
    {
        if (_cardRT == null) return;
        if (_projector == null)
        {
            _projector = HudScreenProjector.Attach("Boost", _canvas, q.sizeRef, -1f);
            HelmetSway.Unregister(_cardRT);   // the warp quad sways instead
            _cardRT.SetParent(_projector.ContentRoot, false);
        }
        else
        {
            _projector.SetLogicalSize(q.sizeRef);
        }
        // Fixed-height card → dead-center in the glass (same fit numbers as
        // flat seating; rig-canvas units are glass reference units).
        _cardRT.anchorMin = _cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        _cardRT.pivot = new Vector2(0.5f, 0.5f);
        _cardRT.anchoredPosition = q.contentOffset;
        float fit = Mathf.Min(1f, (q.sizeRef.x - 12f) / 370f, (q.sizeRef.y - 16f) / 170f) * q.contentScale;
        // blContentBoost rides in via q.contentScale — clamp against the
        // card's MEASURED size so the boosted card can never overflow the
        // capture canvas (anything past the glass edge would just clip).
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_cardRT);
        float cw = Mathf.Max(1f, _cardRT.rect.width);
        float chh = Mathf.Max(1f, _cardRT.rect.height);
        fit = Mathf.Min(fit, (q.sizeRef.x - 2f) / cw, (q.sizeRef.y - 2f) / chh);
        _cardRT.localScale = new Vector3(fit, fit, 1f);
        _cardRT.localRotation = Quaternion.identity;   // perspective comes from the warp, not a lean
        VitalsHUD.ApplyIntegratedStyle(_cardRT);
        _projector.Warp.SetQuad(q.blFrac, q.brFrac, q.trFrac, q.tlFrac);
        HudIdleSweep.Ensure(_cardRT, _projector.Warp);   // recurring scanline refresh (spatial reveal on the warp)
    }

    void DetachProjector()
    {
        if (_projector == null) return;
        _cardRT.SetParent(transform, false);
        Destroy(_projector);
        _projector = null;
        HelmetSway.Register(_cardRT, 0.85f);
    }

    // ── 3D widget build ────────────────────────────────────────────

    void Build3DWidget()
    {
        // Off-screen render target.
        _thrustRT = new RenderTexture(rtResolution, rtResolution, 16, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _thrustRT.Create();

        _widgetRoot = new GameObject("ThrustIndicator3D_Widget");
        DontDestroyOnLoad(_widgetRoot);
        _widgetRoot.transform.position = new Vector3(0f, 100000f, 0f);

        // Central sphere — origin marker. Reuses CenterColor3D so the central
        // element's color stays consistent with prior widgets.
        var center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        center.name = "Center";
        center.transform.SetParent(_widgetRoot.transform, false);
        center.transform.localScale = Vector3.one * 0.45f;
        DestroyCollider(center);
        center.GetComponent<Renderer>().material = MakeUnlitMat(CenterColor3D);

        // 3 perpendicular rings (LineRenderer × 64 segments each).
        BuildRing("Ring_XY", Quaternion.identity);                    // lies in the XY plane
        BuildRing("Ring_YZ", Quaternion.Euler(0f, 90f, 0f));          // lies in the YZ plane
        BuildRing("Ring_XZ", Quaternion.Euler(90f, 0f, 0f));          // lies in the XZ plane

        // 6 arrows pointing INWARD toward center — one per cardinal axis.
        // A lit arrow visualizes thrust being applied from that side toward the
        // player (e.g., Ctrl fires the top engine, exhaust goes up, force pushes
        // player down → top arrow lights, pointing inward = "force pushing down").
        // INDEX ORDER MUST MATCH ReadThrustKeys() — preserves lighting logic.
        //   0 = +Y (Ctrl)      down-thrust pushes you up
        //   1 = +Z (S)         back-thrust pushes you forward
        //   2 = +X (A)         left-strafe pushes you right
        //   3 = -Y (Space)     jetpack pushes you down (exhaust)
        //   4 = -Z (W)         forward-thrust pushes you back (exhaust)
        //   5 = -X (D)         right-strafe pushes you left (exhaust)
        var dirs = new Vector3[]
        {
            Vector3.up,       // 0 +Y
            Vector3.forward,  // 1 +Z
            Vector3.right,    // 2 +X
            Vector3.down,     // 3 -Y
            Vector3.back,     // 4 -Z
            Vector3.left,     // 5 -X
        };
        if (_arrowMesh == null) _arrowMesh = CreateInwardArrowMesh();
        _arrowMats = new Material[6];
        _currentArrowColors = new Color[6];
        for (int i = 0; i < dirs.Length; i++)
        {
            var arrow = new GameObject("Arrow_" + i);
            arrow.transform.SetParent(_widgetRoot.transform, false);
            // Place at the outer end (base of the arrow on the ring); rotate so
            // the mesh's local +Y (apex direction) points toward origin.
            arrow.transform.localPosition = dirs[i] * 0.95f;
            arrow.transform.localRotation = Quaternion.FromToRotation(Vector3.up, -dirs[i]);
            arrow.AddComponent<MeshFilter>().sharedMesh = _arrowMesh;
            var mr = arrow.AddComponent<MeshRenderer>();
            var mat = MakeUnlitMat(ArrowIdle3D);
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _arrowMats[i] = mat;
            _currentArrowColors[i] = ArrowIdle3D;
        }

        // Dedicated camera that ONLY renders to the RT.
        var camGO = new GameObject("ThrustIndicator3D_Camera");
        DontDestroyOnLoad(camGO);
        _indicatorCam = camGO.AddComponent<Camera>();
        _indicatorCam.targetTexture = _thrustRT;
        _indicatorCam.clearFlags = CameraClearFlags.SolidColor;
        _indicatorCam.backgroundColor = RTBgColor;
        _indicatorCam.fieldOfView = 35f;
        _indicatorCam.nearClipPlane = 0.05f;
        _indicatorCam.farClipPlane = 20f;
        _indicatorCam.depth = -50; // before main camera in render order
        _indicatorCam.allowHDR = false;
        _indicatorCam.allowMSAA = true;
        _indicatorCam.useOcclusionCulling = false;
    }

    void BuildRing(string name, Quaternion localRot)
    {
        if (_ringMat == null)
        {
            var shader = Shader.Find("Sprites/Default");
            _ringMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _ringMat.color = LedColor;
        }

        var go = new GameObject(name);
        go.transform.SetParent(_widgetRoot.transform, false);
        go.transform.localRotation = localRot;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.startWidth = 0.025f;
        lr.endWidth   = 0.025f;
        lr.material   = _ringMat;
        lr.startColor = LedColor;
        lr.endColor   = LedColor;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        const int segs = 64;
        const float radius = 0.9f;
        lr.positionCount = segs;
        for (int i = 0; i < segs; i++)
        {
            float a = (i / (float)segs) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
    }

    static void DestroyCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);
    }

    // Square-base pyramid pointing toward +Y. Used once per arrow GameObject,
    // each arrow rotates it so local +Y aligns with the inward-pointing direction.
    static Mesh CreateInwardArrowMesh()
    {
        const float length = 0.30f; // apex distance from base
        const float halfW  = 0.08f; // half base side

        var m = new Mesh { name = "InwardArrow", hideFlags = HideFlags.HideAndDontSave };
        m.vertices = new[]
        {
            new Vector3(0f,    length, 0f),    // 0 apex (inner tip)
            new Vector3(-halfW, 0f, -halfW),   // 1 base
            new Vector3( halfW, 0f, -halfW),   // 2
            new Vector3( halfW, 0f,  halfW),   // 3
            new Vector3(-halfW, 0f,  halfW),   // 4
        };
        m.triangles = new[]
        {
            // 4 side faces (CCW from outside, apex on top)
            0, 2, 1,
            0, 3, 2,
            0, 4, 3,
            0, 1, 4,
            // base (facing away from apex — toward outer ring)
            1, 2, 3,
            1, 3, 4,
        };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Material MakeUnlitMat(Color color)
    {
        var shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        mat.color = color;
        return mat;
    }

    // ── Canvas build ───────────────────────────────────────────────

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 830; // above LetterboxBars (820) — stays visible during dialogue / cook UI
        HUDSceneGate.Register(_canvas);
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // Seated in the helmet's bottom-left housing (HelmetHudLayout contract
        // — keeps the card aligned with the helmet frame art at any aspect
        // ratio). Width still comes from the ContentSizeFitter below.
        var card = NewUI("Card", transform);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(0f, 0f);
        card.pivot     = new Vector2(0f, 0f);
        card.anchoredPosition = new Vector2(
            HelmetHudLayout.BottomLeftOffset.x + HelmetHudLayout.CardInset,
            HelmetHudLayout.BottomLeftOffset.y + HelmetHudLayout.CardInset);
        card.sizeDelta = new Vector2(cardWidth, 0f);
        _cardRT = card;
        HelmetBezelKit.BuildBezel(card, HelmetHudLayout.CardInset - 4f);
        HelmetSway.Register(card, 0.85f);   // slight parallax vs the frame (1.0)

        // Beveled background panel (same sprite as other HUDs).
        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = UIPanelSprites.GetBeveledPanel();
        bg.type = Image.Type.Sliced;
        bg.color = CardBgColor;
        bg.raycastTarget = false;

        // Border outline.
        var border = NewUI("Border", card);
        Stretch(border);
        var borderImg = border.gameObject.AddComponent<Image>();
        borderImg.sprite = UIPanelSprites.GetBeveledOutline();
        borderImg.type = Image.Type.Sliced;
        borderImg.color = CardBorderColor;
        borderImg.raycastTarget = false;
        border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // Left LED accent stripe (matches VitalsHUD / WaterFillHUD).
        var led = NewUI("Led", card);
        led.anchorMin = new Vector2(0f, 0f);
        led.anchorMax = new Vector2(0f, 1f);
        led.pivot     = new Vector2(0f, 0.5f);
        led.anchoredPosition = new Vector2(9f, 0f);
        led.sizeDelta = new Vector2(3f, -16f);
        led.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var ledImg = led.gameObject.AddComponent<Image>();
        ledImg.color = HelmetHudPalette.Accent;   // tracks the tweakable helmet accent
        ledImg.raycastTarget = false;

        // Horizontal layout: tape | widget | seg-bars.
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 12f;
        hlg.padding = new RectOffset(26, 14, 20, 12); // left padding clears the LED; top clears the wire-tag

        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        BuildSpeedTape(card);
        BuildThrustWidget(card);
        BuildSegBars(card);
    }

    void BuildSpeedTape(RectTransform parent)
    {
        var col = NewUI("SpeedTape", parent);
        var colLE = col.gameObject.AddComponent<LayoutElement>();
        colLE.preferredWidth = 56f;
        colLE.preferredHeight = widgetSize;

        // Track background.
        var bg = col.gameObject.AddComponent<Image>();
        bg.color = TrackColor;
        bg.raycastTarget = false;

        // M/S wire-tag label peeking out top-left.
        var tag = NewUI("MS_Label", col);
        tag.anchorMin = new Vector2(0f, 1f);
        tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot     = new Vector2(0f, 0.5f);
        tag.anchoredPosition = new Vector2(4f, 0f);
        tag.sizeDelta = new Vector2(48f, 22f);
        tag.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var tagBg = tag.gameObject.AddComponent<Image>();
        tagBg.color = new Color32(0x04, 0x10, 0x1E, 0xFF);
        tagBg.raycastTarget = false;
        var tagText = NewText(tag, "Text", "M/S", 13f, FontStyles.Bold, HeaderColor);
        var tagTextRT = tagText.GetComponent<RectTransform>();
        tagTextRT.anchorMin = Vector2.zero;
        tagTextRT.anchorMax = Vector2.one;
        tagTextRT.offsetMin = Vector2.zero;
        tagTextRT.offsetMax = Vector2.zero;
        tagText.alignment = TextAlignmentOptions.Center;
        tagText.characterSpacing = 2f;

        // 5 stacked rows. Middle row (index 2) is the highlighted current value.
        var stack = NewUI("RowStack", col);
        Stretch(stack);
        var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(2, 6, 22, 6); // top padding clears the wire-tag

        _tape = new TextMeshProUGUI[5];
        for (int i = 0; i < 5; i++)
        {
            bool isCurrent = (i == 2);
            var row = NewUI("TapeRow_" + i, stack);
            var rowLE = row.gameObject.AddComponent<LayoutElement>();
            rowLE.preferredHeight = isCurrent ? 20f : 11f;

            if (isCurrent)
            {
                var fill = row.gameObject.AddComponent<Image>();
                fill.color = CurrentRowHighlight;
                fill.raycastTarget = false;
            }

            var t = NewText(row, "Value", "0",
                            isCurrent ? 14f : 9f,
                            isCurrent ? FontStyles.Bold : FontStyles.Normal,
                            isCurrent ? ValueColor : DimLabelColor);
            var tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.MidlineRight;
            t.margin = new Vector4(0f, 0f, 6f, 0f); // right padding
            _tape[i] = t;
        }
    }

    void BuildThrustWidget(RectTransform parent)
    {
        var widget = NewUI("ThrustWidget", parent);
        var widgetLE = widget.gameObject.AddComponent<LayoutElement>();
        widgetLE.preferredWidth = widgetSize;
        widgetLE.preferredHeight = widgetSize;
        widgetLE.flexibleWidth = 0f;
        widgetLE.flexibleHeight = 0f;

        _thrustImage = widget.gameObject.AddComponent<RawImage>();
        _thrustImage.texture = _thrustRT;
        _thrustImage.color = Color.white;
        _thrustImage.raycastTarget = false;
    }

    void BuildSegBars(RectTransform parent)
    {
        var col = NewUI("SegBars", parent);
        var colLE = col.gameObject.AddComponent<LayoutElement>();
        colLE.preferredWidth = 90f;
        colLE.preferredHeight = widgetSize;
        var hl = col.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.LowerCenter;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.spacing = 8f;

        _upSegs  = BuildSegColumn(col, "UP");
        _dnSegs  = BuildSegColumn(col, "DN");
        _dirSegs = BuildSegColumn(col, "DIR");

        // BOOST wire-tag — peeks above the seg-bars, mirrors the M/S tag style.
        var tag = NewUI("BoostLabel", col);
        tag.anchorMin = new Vector2(0.5f, 1f);
        tag.anchorMax = new Vector2(0.5f, 1f);
        tag.pivot     = new Vector2(0.5f, 0.5f);
        tag.anchoredPosition = new Vector2(0f, 0f);
        tag.sizeDelta = new Vector2(64f, 22f);
        tag.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var tagBg = tag.gameObject.AddComponent<Image>();
        tagBg.color = new Color32(0x04, 0x10, 0x1E, 0xFF);
        tagBg.raycastTarget = false;
        var tagText = NewText(tag, "Text", "BOOST", 13f, FontStyles.Bold, HeaderColor);
        var tagTextRT = tagText.GetComponent<RectTransform>();
        tagTextRT.anchorMin = Vector2.zero;
        tagTextRT.anchorMax = Vector2.one;
        tagTextRT.offsetMin = Vector2.zero;
        tagTextRT.offsetMax = Vector2.zero;
        tagText.alignment = TextAlignmentOptions.Center;
        tagText.characterSpacing = 2f;
    }

    Image[] BuildSegColumn(RectTransform parent, string labelText)
    {
        const int segCount  = 8;
        const float segH    = 8f;
        const float segGap  = 2f;
        const float lblH    = 16f;

        var wrap = NewUI("Col_" + labelText, parent);
        var wrapLE = wrap.gameObject.AddComponent<LayoutElement>();
        wrapLE.preferredWidth = 22f;
        wrapLE.preferredHeight = widgetSize;
        var vl = wrap.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.LowerCenter;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.spacing = segGap;
        vl.padding = new RectOffset(0, 0, 0, 0);

        // Stack of segments (bottom-first → built top-to-bottom, fill logic reads bottom-up).
        var segs = new Image[segCount];
        for (int i = segCount - 1; i >= 0; i--)
        {
            var seg = NewUI("Seg_" + i, wrap);
            var segLE = seg.gameObject.AddComponent<LayoutElement>();
            segLE.preferredHeight = segH;
            var img = seg.gameObject.AddComponent<Image>();
            img.color = CellOffColor;
            img.raycastTarget = false;
            segs[i] = img;
        }

        // Label below the column.
        var lblRT = NewUI("Lbl", wrap);
        lblRT.gameObject.AddComponent<LayoutElement>().preferredHeight = lblH;
        var lbl = NewText(lblRT, "Text", labelText, 11f, FontStyles.Bold, HeaderColor);
        var lblTextRT = lbl.GetComponent<RectTransform>();
        lblTextRT.anchorMin = Vector2.zero;
        lblTextRT.anchorMax = Vector2.one;
        lblTextRT.offsetMin = Vector2.zero;
        lblTextRT.offsetMax = Vector2.zero;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.characterSpacing = 1f;

        return segs;
    }

    static void RecolorSegs(Image[] segs, int litCount)
    {
        for (int i = 0; i < segs.Length; i++)
            segs[i].color = (i < litCount) ? LedColor : CellOffColor;
    }

    // ── Helpers ────────────────────────────────────────────────────

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, FontStyles style, Color color)
    {
        var rt = NewUI(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        HudFontResolver.Apply(t);
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }
}
