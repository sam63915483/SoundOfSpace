using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Skyrim-style top-center compass. Waypoints are projected to the player's
// surface-tangent plane and mapped to a horizontal strip by bearing angle
// relative to the player camera's forward direction.
//
// Tutorial steps and NPCs add waypoints via AddWaypointByTag(...) — the tag
// resolves to a scene Transform whose world position is queried each frame,
// so the waypoint follows moving targets and survives planet rotation.
//
// Pattern mirrors PickupUIManager: cached camera/player refs, change-detected
// text, single instance auto-created via RuntimeInitializeOnLoadMethod.
//
// Persists active waypoints via SaveCollector → CompassSave.
public class CompassHUD : MonoBehaviour
{
    public static CompassHUD Instance { get; private set; }

    [Header("Strip layout (canvas-reference units, 1920×1080)")]
    [Tooltip("Width of the compass strip in canvas units.")]
    public float stripWidth = 560f;
    [Tooltip("Height of the compass strip.")]
    public float stripHeight = 36f;
    [Tooltip("Top margin from the screen top edge.")]
    public float topMargin = 32f;

    [Header("Bearing")]
    [Tooltip("Half-angle (degrees) of the visible compass field. ±90° = 180° total. Waypoints outside this range clamp to the edges.")]
    public float visibleHalfAngle = 90f;

    [Tooltip("Hide a waypoint marker when the player is within this many world units of the target. Avoids the marker spinning wildly when you're standing on top of the destination.")]
    public float hideWithinDistance = 3f;

    Canvas _canvas;
    RectTransform _strip;
    RectTransform _badgeRT;
    TextMeshProUGUI _badgeText;
    int _lastHeadingShown = int.MinValue;
    string _lastCardinalCode = "";

    public enum WaypointKind { Gameplay, Cardinal, DegreeNumber, Tick }

    sealed class Waypoint
    {
        public string Id;
        public string Label;
        public string SourceTag;          // empty for dynamic-only (Func) waypoints
        public WaypointKind Kind = WaypointKind.Gameplay;
        public System.Func<Vector3> PositionProvider;
        public Sprite Icon;
        public Color Tint = Color.white;
        public bool Active = true;
        public RectTransform Ui;
        public Image IconImage;
        public TextMeshProUGUI LabelText;
        public CanvasGroup Group;
        public string LastShownLabel;
        public float SmoothedX;
        public bool WasVisibleLastFrame;
    }

    readonly List<Waypoint> _waypoints = new List<Waypoint>();

    PlayerController _playerCached;
    Camera _cameraCached;

    // Per-frame cached "north" reference + the body it's derived from. The
    // cardinals/ticks/degree-numbers (≈48 waypoints) and the heading badge all
    // need the SAME surface-north, which comes from the closest celestial body.
    // Deriving it per-waypoint meant ≈49 calls/frame to FindClosestBody →
    // NBodySimulation.Bodies; in scenes with no N-body sim (the backrooms /
    // poolrooms interiors) NBodySimulation.Instance is always null, so each of
    // those calls re-runs a full-scene FindObjectOfType — which dominated the
    // frame and is why the small interiors ran WORSE than the solar system.
    // Pick the body on a throttle, derive north once, reuse for every waypoint.
    CelestialBody _northBody;
    float _nextBodyRefind;
    Vector3 _cachedNorth = Vector3.forward;
    const float BodyRefindInterval = 0.5f;

    // ── Palette ────────────────────────────────────────────────────────────
    static readonly Color StripBgColor      = new Color32(0x0A, 0x18, 0x28, 0xC8); // dark navy, ~78%
    static readonly Color StripSheenColor   = new Color32(0x5C, 0xC8, 0xFF, 0x8C); // 1px cyan edge
    static readonly Color CenterTickColor   = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color CardinalColor     = new Color32(0x78, 0xC8, 0xFF, 0xB3);
    static readonly Color MarkerIconColor   = new Color32(0x5C, 0xC8, 0xFF, 0xFF);
    static readonly Color MarkerLabelColor  = new Color32(0xC8, 0xEA, 0xFF, 0xFF);
    static readonly Color MarkerGlowColor   = new Color(0.36f, 0.78f, 1f, 0.55f);

    static readonly Color BadgeBgColor       = new Color32(0x14, 0x2C, 0x48, 0xF2);
    static readonly Color BadgeBorderColor   = new Color32(0x78, 0xC8, 0xFF, 0x8C);
    static readonly Color BadgeTextColor     = new Color32(0xEA, 0xF6, 0xFF, 0xFF);
    static readonly Color BadgeTextGlowColor = new Color(0.36f, 0.78f, 1f, 0.55f);

    static readonly string[] CardinalCodes = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    // Special source tag for the internal cardinal-letter waypoints (N/E/S/W).
    // These are seeded at Awake, never persisted, and skip the standard icon
    // rendering in BuildWaypointUI.
    const string CardinalSourceTag = "__CARDINAL__";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        if (SceneManager.GetActiveScene().name == "MainMenu") return;
        var go = new GameObject("CompassHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<CompassHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildCanvas();
        // Stay hidden until a PlayerController appears in the active scene.
        // EnsureGameplaySingletons creates the CompassHUD synchronously when
        // the player clicks PLAY, so without this gate the canvas would
        // render on the main menu during the brief scene-transition window.
        if (_canvas != null) _canvas.enabled = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ──

    /// Add (or replace) a waypoint that resolves its world position from a
    /// scene Transform tagged with `sourceTag`. Tag-based waypoints persist
    /// through saves.
    public void AddWaypointByTag(string id, string sourceTag, string label,
                                  Sprite icon = null, Color? tint = null)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(sourceTag)) return;
        AddWaypointInternal(id, sourceTag, () =>
        {
            try
            {
                var go = GameObject.FindWithTag(sourceTag);
                return go != null ? go.transform.position : Vector3.zero;
            }
            catch (UnityException)
            {
                return Vector3.zero;
            }
        }, label, icon, tint);
    }

    /// Add a dynamic waypoint with a custom position provider. NOT persisted
    /// through saves — use AddWaypointByTag for waypoints that should survive
    /// load/restore.
    public void AddWaypoint(string id, System.Func<Vector3> worldPosProvider,
                             string label, Sprite icon = null, Color? tint = null)
    {
        AddWaypointInternal(id, "", worldPosProvider, label, icon, tint);
    }

    public void RemoveWaypoint(string id)
    {
        for (int i = _waypoints.Count - 1; i >= 0; i--)
        {
            if (_waypoints[i].Id == id)
            {
                if (_waypoints[i].Ui != null) Destroy(_waypoints[i].Ui.gameObject);
                _waypoints.RemoveAt(i);
            }
        }
    }

    public void SetActive(string id, bool active)
    {
        for (int i = 0; i < _waypoints.Count; i++)
        {
            if (_waypoints[i].Id == id)
            {
                _waypoints[i].Active = active;
                if (_waypoints[i].Ui != null) _waypoints[i].Ui.gameObject.SetActive(active);
                return;
            }
        }
    }

    public bool HasWaypoint(string id)
    {
        for (int i = 0; i < _waypoints.Count; i++)
            if (_waypoints[i].Id == id) return true;
        return false;
    }

    public void ClearAll()
    {
        for (int i = 0; i < _waypoints.Count; i++)
            if (_waypoints[i].Ui != null) Destroy(_waypoints[i].Ui.gameObject);
        _waypoints.Clear();
        // The cardinals / degree numbers / ticks are permanent compass chrome, not gameplay
        // waypoints — re-seed them so a New Game reset (NewGameReset calls ClearAll) doesn't
        // leave the strip without N/E/S/W and the degree markers.
        SeedTickWaypoints();
        SeedDegreeNumberWaypoints();
        SeedCardinalWaypoints();
    }

    void AddWaypointInternal(string id, string sourceTag, System.Func<Vector3> provider,
                              string label, Sprite icon, Color? tint)
    {
        if (string.IsNullOrEmpty(id) || provider == null) return;
        RemoveWaypoint(id);
        var wp = new Waypoint
        {
            Id = id,
            SourceTag = sourceTag ?? "",
            Label = label ?? id,
            PositionProvider = provider,
            Icon = icon,
            Tint = tint ?? Color.white,
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }

    // ── Per-frame: place each marker on the strip ──

    void LateUpdate()
    {
        if (_playerCached == null) _playerCached = FindObjectOfType<PlayerController>();
        if (_cameraCached == null) _cameraCached = Camera.main;
        if (_playerCached == null || _cameraCached == null) return;

        // Hide the compass entirely when the map is open — the map UI is
        // the canonical "where am I?" view; the compass strip is redundant
        // there and visually conflicts with the legend.
        if (PlayerController.isMapOpen)
        {
            if (_canvas != null && _canvas.enabled) _canvas.enabled = false;
            return;
        }
        if (_canvas != null && !_canvas.enabled) _canvas.enabled = true;

        Vector3 playerPos = _playerCached.Rigidbody != null
            ? _playerCached.Rigidbody.position
            : _playerCached.transform.position;

        Vector3 surfaceUp = _playerCached.transform.up;
        Vector3 camForward = _cameraCached.transform.forward;

        Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, surfaceUp);
        if (forwardOnPlane.sqrMagnitude < 0.0001f)
            forwardOnPlane = Vector3.ProjectOnPlane(_cameraCached.transform.up, surfaceUp);
        if (forwardOnPlane.sqrMagnitude < 0.0001f) return;
        forwardOnPlane.Normalize();

        // Refresh the closest-body pick on a throttle (it only changes when you
        // travel between bodies), then derive surface-north from it ONCE for the
        // whole frame. Throttling on time alone — not on "is null" — keeps the
        // re-find rate low even in interiors where no body will ever be found.
        if (Time.unscaledTime >= _nextBodyRefind)
        {
            _northBody = FindClosestBody(playerPos);
            _nextBodyRefind = Time.unscaledTime + BodyRefindInterval;
        }
        // north is recomputed every frame from the cached body's CURRENT forward
        // and the player's CURRENT up, so it stays live as the planet rotates and
        // the player turns — only the (expensive) body lookup is throttled.
        _cachedNorth = ComputeNorthFromBody(_northBody, surfaceUp);

        // Heading badge — same bearing math the waypoints use, but reduced
        // to a single 0..360 degree number plus a cardinal short-code.
        UpdateHeadingBadge(playerPos, surfaceUp, forwardOnPlane);

        for (int i = 0; i < _waypoints.Count; i++)
        {
            var wp = _waypoints[i];
            if (wp.Ui == null) continue;
            if (!wp.Active) { wp.Ui.gameObject.SetActive(false); wp.WasVisibleLastFrame = false; continue; }
            if (wp.PositionProvider == null) continue;

            Vector3 targetPos = wp.PositionProvider();
            Vector3 toTarget = targetPos - playerPos;

            float distSqr = toTarget.sqrMagnitude;
            // Cardinal letters are placed 100m out (never below hideWithinDistance),
            // so this guard only kicks in for gameplay waypoints the player has
            // walked up to.
            if (distSqr < hideWithinDistance * hideWithinDistance)
            {
                wp.Ui.gameObject.SetActive(false);
                wp.WasVisibleLastFrame = false;
                continue;
            }

            Vector3 toTargetOnPlane = Vector3.ProjectOnPlane(toTarget, surfaceUp);
            if (toTargetOnPlane.sqrMagnitude < 0.0001f)
            {
                wp.Ui.gameObject.SetActive(false);
                wp.WasVisibleLastFrame = false;
                continue;
            }

            wp.Ui.gameObject.SetActive(true);

            float angle = Vector3.SignedAngle(forwardOnPlane, toTargetOnPlane, surfaceUp);
            float t = Mathf.Clamp(angle / visibleHalfAngle, -1f, 1f);
            float xPos = t * (stripWidth * 0.5f);

            // Gameplay markers shake at close range because tiny player position
            // shifts produce big bearing changes near the target. Smooth the
            // x-position with distance-aware damping: close = strong smoothing,
            // far = effectively snap. Cardinals/ticks/numbers sit at a fixed
            // 100 m radius so they never shake — snap them.
            if (wp.Kind == WaypointKind.Gameplay)
            {
                float distance = Mathf.Sqrt(distSqr);
                float smoothBlend = Mathf.Clamp01((distance - hideWithinDistance) / 30f);
                float lerpRate = Mathf.Lerp(12f, 30f, smoothBlend);
                if (!wp.WasVisibleLastFrame) wp.SmoothedX = xPos;
                wp.SmoothedX = Mathf.Lerp(wp.SmoothedX, xPos, Mathf.Clamp01(lerpRate * Time.unscaledDeltaTime));
                wp.Ui.anchoredPosition = new Vector2(wp.SmoothedX, 0f);
            }
            else
            {
                wp.Ui.anchoredPosition = new Vector2(xPos, 0f);
            }
            wp.WasVisibleLastFrame = true;

            float edgeT = Mathf.Abs(t);
            float alpha = Mathf.Lerp(1f, 0.4f, edgeT * edgeT);
            if (wp.Group != null) wp.Group.alpha = alpha;

            if (wp.LastShownLabel != wp.Label && wp.LabelText != null)
            {
                wp.LabelText.text = wp.Label ?? wp.Id;
                wp.LastShownLabel = wp.Label;
            }
        }
    }

    void UpdateHeadingBadge(Vector3 playerPos, Vector3 surfaceUp, Vector3 forwardOnPlane)
    {
        if (_badgeText == null) return;
        Vector3 northDir = _cachedNorth;   // computed once per frame in LateUpdate
        if (northDir.sqrMagnitude < 0.0001f) return;

        float heading = Vector3.SignedAngle(northDir, forwardOnPlane, surfaceUp);
        // Convert SignedAngle's -180..180 to 0..360, clockwise from north.
        if (heading < 0f) heading += 360f;
        int headingInt = Mathf.RoundToInt(heading) % 360;
        int cardinalIdx = ((int)((heading + 22.5f) / 45f)) % 8;
        if (cardinalIdx < 0) cardinalIdx += 8;
        string cardinalCode = CardinalCodes[cardinalIdx];

        if (headingInt != _lastHeadingShown || cardinalCode != _lastCardinalCode)
        {
            _lastHeadingShown = headingInt;
            _lastCardinalCode = cardinalCode;
            _badgeText.text = $"{headingInt:000}°  {cardinalCode}";
        }
    }

    // ── Helmet art-housing seating ──
    // Called by HelmetOverlayHUD once the art config resolves: centers the
    // strip inside the art's slim brow display and hides the strip's own
    // bg/sheens — the helmet's dark glass IS the instrument housing. The
    // heading badge tucks just under the slot, over the visor top.
    public void SeatInArtHousing(HelmetOverlayHUD.HousingRect h)
    {
        if (_strip == null || _badgeRT == null) return;
        float cs = h.contentScale;
        stripWidth = Mathf.Min(612f, h.sizeRef.x - 44f);   // clear of the glass' rounded ends
        _strip.anchorMin = _strip.anchorMax = h.anchorFrac;
        _strip.pivot = new Vector2(0.5f, 0.5f);
        _strip.sizeDelta = new Vector2(stripWidth, stripHeight);
        _strip.localScale = new Vector3(cs, cs, 1f);
        _badgeRT.localScale = new Vector3(cs, cs, 1f);
        // Strip rides the lower half of the glass; the heading badge tucks
        // into the upper half so the WHOLE instrument lives inside the glass
        // (the badge used to dangle below onto the brow padding).
        // contentOffset = the browContentOffset nudge, moving both together.
        _strip.anchoredPosition = new Vector2(0f, -(h.sizeRef.y * 0.5f - stripHeight * cs * 0.5f - 9f)) + h.contentOffset;
        _badgeRT.anchorMin = _badgeRT.anchorMax = h.anchorFrac;
        _badgeRT.pivot = new Vector2(0.5f, 1f);
        _badgeRT.anchoredPosition = new Vector2(0f, h.sizeRef.y * 0.5f - 3f) + h.contentOffset;
        SetImageEnabled(_strip, false);
        var top = _strip.Find("TopSheen");
        if (top != null) SetImageEnabled(top, false);
        var bot = _strip.Find("BottomSheen");
        if (bot != null) SetImageEnabled(bot, false);
        HelmetSway.Reregister(_strip);
        HelmetSway.Reregister(_badgeRT);
    }

    static void SetImageEnabled(Transform t, bool on)
    {
        var img = t.GetComponent<Image>();
        if (img != null) img.enabled = on;
    }

    // ── Canvas + waypoint UI build ──

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the helmet frame art (805) so the strip sits ON the brow pad,
        // still under LetterboxBars (820) so dialogue cinematics cover it.
        _canvas.sortingOrder = UILayer.CompassHud;
        HUDSceneGate.Register(_canvas);
        HudVisibility.RegisterHideable(_canvas);   // honours the "HIDE HUD" setting / pod cinematic
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Seat the strip + badge inside the helmet's top-brow window
        // (HelmetHudLayout contract). Assigned here rather than as field
        // defaults so an Inspector override on a scene-placed instance would
        // still win. 22 extra units leave room for the heading badge row.
        stripWidth = HelmetHudLayout.TopBrowSize.x - HelmetHudLayout.CardInset * 2f;
        topMargin  = HelmetHudLayout.TopBrowYOffset + HelmetHudLayout.CardInset + 22f;

        var stripGo = new GameObject("Strip", typeof(RectTransform));
        stripGo.transform.SetParent(transform, false);
        _strip = stripGo.GetComponent<RectTransform>();
        _strip.anchorMin = new Vector2(0.5f, 1f);
        _strip.anchorMax = new Vector2(0.5f, 1f);
        _strip.pivot = new Vector2(0.5f, 1f);
        _strip.sizeDelta = new Vector2(stripWidth, stripHeight);
        _strip.anchoredPosition = new Vector2(0f, -topMargin);

        // Faded-edge dark navy background — replaces the flat black bar.
        var bg = stripGo.AddComponent<Image>();
        bg.sprite = GetStripBgSprite();
        bg.type = Image.Type.Sliced;
        bg.color = StripBgColor;
        bg.raycastTarget = false;

        // Top edge sheen — 1px cyan line, fades at the ends.
        var topSheenGo = new GameObject("TopSheen", typeof(RectTransform));
        topSheenGo.transform.SetParent(_strip, false);
        var topSheenRt = topSheenGo.GetComponent<RectTransform>();
        topSheenRt.anchorMin = new Vector2(0f, 1f);
        topSheenRt.anchorMax = new Vector2(1f, 1f);
        topSheenRt.pivot = new Vector2(0.5f, 1f);
        topSheenRt.sizeDelta = new Vector2(0f, 1f);
        topSheenRt.anchoredPosition = Vector2.zero;
        var topSheen = topSheenGo.AddComponent<Image>();
        topSheen.sprite = GetSheenSprite();
        topSheen.color = HelmetHudPalette.GlassSheen;   // tracks the helmet accent
        topSheen.raycastTarget = false;

        // Bottom edge sheen — mirror.
        var botSheenGo = new GameObject("BottomSheen", typeof(RectTransform));
        botSheenGo.transform.SetParent(_strip, false);
        var botSheenRt = botSheenGo.GetComponent<RectTransform>();
        botSheenRt.anchorMin = new Vector2(0f, 0f);
        botSheenRt.anchorMax = new Vector2(1f, 0f);
        botSheenRt.pivot = new Vector2(0.5f, 0f);
        botSheenRt.sizeDelta = new Vector2(0f, 1f);
        botSheenRt.anchoredPosition = Vector2.zero;
        var botSheen = botSheenGo.AddComponent<Image>();
        botSheen.sprite = GetSheenSprite();
        botSheen.color = HelmetHudPalette.GlassSheen;   // tracks the helmet accent
        botSheen.raycastTarget = false;

        stripGo.AddComponent<RectMask2D>();

        // Glowing center tick — replaces the plain white tick.
        var tickGo = new GameObject("CenterTick", typeof(RectTransform));
        tickGo.transform.SetParent(_strip, false);
        var tickRt = tickGo.GetComponent<RectTransform>();
        tickRt.anchorMin = new Vector2(0.5f, 0f);
        tickRt.anchorMax = new Vector2(0.5f, 1f);
        tickRt.pivot = new Vector2(0.5f, 0.5f);
        tickRt.sizeDelta = new Vector2(2f, -8f);
        tickRt.anchoredPosition = Vector2.zero;
        var tickImg = tickGo.AddComponent<Image>();
        tickImg.color = HelmetHudPalette.Accent;   // tracks the helmet accent
        tickImg.raycastTarget = false;
        var tickGlow = tickGo.AddComponent<Shadow>();
        tickGlow.effectColor = MarkerGlowColor;
        tickGlow.effectDistance = new Vector2(0f, 0f);

        // Heading badge — sits 4 px above the strip, centered on the screen.
        var badgeGo = new GameObject("HeadingBadge", typeof(RectTransform));
        badgeGo.transform.SetParent(transform, false);
        _badgeRT = badgeGo.GetComponent<RectTransform>();
        _badgeRT.anchorMin = new Vector2(0.5f, 1f);
        _badgeRT.anchorMax = new Vector2(0.5f, 1f);
        _badgeRT.pivot = new Vector2(0.5f, 1f);
        _badgeRT.sizeDelta = new Vector2(120f, 20f);
        _badgeRT.anchoredPosition = new Vector2(0f, -(topMargin - 24f));

        var badgeBg = badgeGo.AddComponent<Image>();
        badgeBg.color = BadgeBgColor;
        badgeBg.raycastTarget = false;
        var badgeOutline = badgeGo.AddComponent<Outline>();
        badgeOutline.effectColor = BadgeBorderColor;
        badgeOutline.effectDistance = new Vector2(1f, -1f);
        var badgeGlow = badgeGo.AddComponent<Shadow>();
        badgeGlow.effectColor = new Color(BadgeBorderColor.r, BadgeBorderColor.g, BadgeBorderColor.b, 0.30f);
        badgeGlow.effectDistance = new Vector2(0f, 0f);

        var badgeTextGo = new GameObject("Text", typeof(RectTransform));
        badgeTextGo.transform.SetParent(badgeGo.transform, false);
        var badgeTextRt = badgeTextGo.GetComponent<RectTransform>();
        badgeTextRt.anchorMin = Vector2.zero;
        badgeTextRt.anchorMax = Vector2.one;
        badgeTextRt.offsetMin = new Vector2(8f, 2f);
        badgeTextRt.offsetMax = new Vector2(-8f, -2f);
        _badgeText = badgeTextGo.AddComponent<TextMeshProUGUI>();
        _badgeText.text = "---°";
        _badgeText.fontSize = 11f;
        _badgeText.fontStyle = FontStyles.Bold;
        _badgeText.alignment = TextAlignmentOptions.Center;
        _badgeText.characterSpacing = 4f;
        _badgeText.color = BadgeTextColor;
        _badgeText.raycastTarget = false;
        _badgeText.enableWordWrapping = false;
        var badgeTextGlow = badgeTextGo.AddComponent<Shadow>();
        badgeTextGlow.effectColor = BadgeTextGlowColor;
        badgeTextGlow.effectDistance = new Vector2(0f, 0f);

        SeedTickWaypoints();        // bottom layer of the strip's child stack
        SeedDegreeNumberWaypoints();
        SeedCardinalWaypoints();    // letters sit on top, drawn last

        // Helmet sway — registered last so base positions are final.
        HelmetSway.Register(_strip, 0.85f);
        HelmetSway.Register(_badgeRT, 0.85f);
    }

    void SeedCardinalWaypoints()
    {
        AddCardinal("N",  0f);
        AddCardinal("E",  90f);
        AddCardinal("S",  180f);
        AddCardinal("W", -90f);
    }

    void SeedDegreeNumberWaypoints()
    {
        // Every 30° between cardinals (cardinals at 0/90/180/270 are excluded).
        int[] bearings = { 30, 60, 120, 150, 210, 240, 300, 330 };
        for (int i = 0; i < bearings.Length; i++)
            AddDegreeNumber(bearings[i]);
    }

    void AddDegreeNumber(int bearingDegrees)
    {
        float bearingF = bearingDegrees;
        var wp = new Waypoint
        {
            Id = $"deg_{bearingDegrees:000}",
            SourceTag = CardinalSourceTag,
            Kind = WaypointKind.DegreeNumber,
            Label = $"{bearingDegrees:000}",
            PositionProvider = () => BearingPosition(bearingF),
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }

    void SeedTickWaypoints()
    {
        // 36 ticks every 10° around the full circle. Every third tick (0, 30,
        // 60, …) is "major" — taller and brighter.
        for (int deg = 0; deg < 360; deg += 10)
        {
            bool major = (deg % 30) == 0;
            AddTick(deg, major);
        }
    }

    void AddTick(int bearingDegrees, bool major)
    {
        float bearingF = bearingDegrees;
        var wp = new Waypoint
        {
            Id = $"tick_{bearingDegrees:000}",
            SourceTag = major ? "__TICK_MAJOR__" : "__TICK_MINOR__",
            Kind = WaypointKind.Tick,
            Label = "",
            PositionProvider = () => BearingPosition(bearingF),
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }

    void AddCardinal(string letter, float bearingDegrees)
    {
        var wp = new Waypoint
        {
            Id = "cardinal_" + letter,
            SourceTag = CardinalSourceTag,
            Kind = WaypointKind.Cardinal,
            Label = letter,
            PositionProvider = () => BearingPosition(bearingDegrees),
            Tint = CardinalColor,
        };
        BuildWaypointUI(wp);
        _waypoints.Add(wp);
    }

    // Helper: virtual world position 100 m out at the given bearing relative
    // to the body-projected world-north reference, from the player's current
    // position. Shared by cardinal/degree-number/tick waypoint providers.
    Vector3 BearingPosition(float bearingDegrees)
    {
        if (_playerCached == null) return Vector3.zero;
        Vector3 origin = _playerCached.Rigidbody != null
            ? _playerCached.Rigidbody.position
            : _playerCached.transform.position;
        Vector3 surfaceUp = _playerCached.transform.up;
        // Reuse the per-frame cached north (computed in LateUpdate) — every
        // bearing waypoint shares the same closest-body north reference, so the
        // body scan must NOT run once per waypoint. See _cachedNorth.
        Vector3 northDir = _cachedNorth;
        if (northDir.sqrMagnitude < 0.0001f) return Vector3.zero;
        Quaternion rot = Quaternion.AngleAxis(bearingDegrees, surfaceUp);
        Vector3 dir = rot * northDir;
        return origin + dir * 100f;
    }

    // Returns a unit vector pointing along "world North" projected onto the
    // surface-tangent plane, given the already-picked closest body. Uses the
    // body's forward axis as the world North reference (every body has a fixed
    // forward — a stable rotation anchor as the body orbits and rotates). Split
    // out of the old per-call ComputeSurfaceNorth so the (expensive) closest-body
    // scan can be throttled/cached separately from this cheap projection.
    static Vector3 ComputeNorthFromBody(CelestialBody body, Vector3 surfaceUp)
    {
        Vector3 worldNorthRef = (body != null) ? body.transform.forward : Vector3.forward;
        Vector3 northOnPlane = Vector3.ProjectOnPlane(worldNorthRef, surfaceUp);
        if (northOnPlane.sqrMagnitude < 0.0001f)
        {
            // Body forward is parallel to gravity-up — try `right` as a backup.
            Vector3 fallback = (body != null) ? body.transform.right : Vector3.right;
            northOnPlane = Vector3.ProjectOnPlane(fallback, surfaceUp);
        }
        if (northOnPlane.sqrMagnitude < 0.0001f) return Vector3.zero;
        return northOnPlane.normalized;
    }

    static CelestialBody FindClosestBody(Vector3 worldPos)
    {
        var bodies = NBodySimulation.Bodies;
        if (bodies == null) return null;
        CelestialBody closest = null;
        float bestSurfaceDst = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float dst = (b.Position - worldPos).magnitude - b.radius;
            if (dst < bestSurfaceDst) { bestSurfaceDst = dst; closest = b; }
        }
        return closest;
    }

    void BuildWaypointUI(Waypoint wp)
    {
        var containerGo = new GameObject($"WP_{wp.Id}", typeof(RectTransform));
        containerGo.transform.SetParent(_strip, false);
        var rt = containerGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(80f, stripHeight);
        rt.anchoredPosition = Vector2.zero;
        wp.Ui = rt;

        wp.Group = containerGo.AddComponent<CanvasGroup>();
        wp.Group.interactable = false;
        wp.Group.blocksRaycasts = false;

        switch (wp.Kind)
        {
            case WaypointKind.Cardinal:      BuildCardinalUI(wp, containerGo);     break;
            case WaypointKind.DegreeNumber:  BuildDegreeNumberUI(wp, containerGo); break;
            case WaypointKind.Tick:          BuildTickUI(wp, containerGo);         break;
            default:                         BuildGameplayUI(wp, containerGo);     break;
        }
        wp.LastShownLabel = wp.Label;
    }

    void BuildCardinalUI(Waypoint wp, GameObject containerGo)
    {
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.5f, 0.5f);
        labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.sizeDelta = new Vector2(40f, stripHeight);
        labelRt.anchoredPosition = Vector2.zero;
        wp.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
        wp.LabelText.text = wp.Label ?? wp.Id;
        wp.LabelText.fontSize = 14f;
        wp.LabelText.fontStyle = FontStyles.Bold;
        wp.LabelText.alignment = TextAlignmentOptions.Center;
        wp.LabelText.color = CardinalColor;
        wp.LabelText.raycastTarget = false;
        wp.LabelText.outlineColor = Color.black;
        wp.LabelText.outlineWidth = 0.2f;
    }

    void BuildDegreeNumberUI(Waypoint wp, GameObject containerGo)
    {
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.5f, 0.5f);
        labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.sizeDelta = new Vector2(36f, stripHeight);
        labelRt.anchoredPosition = new Vector2(0f, 1f);
        wp.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
        wp.LabelText.text = wp.Label ?? wp.Id;
        wp.LabelText.fontSize = 10f;
        wp.LabelText.fontStyle = FontStyles.Bold;
        wp.LabelText.alignment = TextAlignmentOptions.Center;
        wp.LabelText.color = new Color32(0x78, 0xC8, 0xFF, 0xA6);
        wp.LabelText.characterSpacing = 1f;
        wp.LabelText.raycastTarget = false;
    }

    void BuildTickUI(Waypoint wp, GameObject containerGo)
    {
        bool major = wp.SourceTag == "__TICK_MAJOR__";
        var tickGo = new GameObject("Tick", typeof(RectTransform));
        tickGo.transform.SetParent(containerGo.transform, false);
        var tickRt = tickGo.GetComponent<RectTransform>();
        tickRt.anchorMin = new Vector2(0.5f, 1f);
        tickRt.anchorMax = new Vector2(0.5f, 1f);
        tickRt.pivot = new Vector2(0.5f, 1f);
        tickRt.sizeDelta = new Vector2(1f, major ? 9f : 6f);
        tickRt.anchoredPosition = new Vector2(0f, -2f);
        var img = tickGo.AddComponent<Image>();
        img.color = major
            ? new Color32(0x78, 0xC8, 0xFF, 0xBF)
            : new Color32(0x78, 0xC8, 0xFF, 0x73);
        img.raycastTarget = false;
    }

    void BuildGameplayUI(Waypoint wp, GameObject containerGo)
    {
        // Icon
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(containerGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 1f);
        iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.sizeDelta = new Vector2(16f, 16f);
        iconRt.anchoredPosition = new Vector2(0f, -2f);
        wp.IconImage = iconGo.AddComponent<Image>();
        wp.IconImage.sprite = wp.Icon != null ? wp.Icon : GetDefaultMarkerSprite();
        wp.IconImage.color = wp.Tint == Color.white ? MarkerIconColor : wp.Tint;
        wp.IconImage.raycastTarget = false;
        var iconGlow = iconGo.AddComponent<Shadow>();
        iconGlow.effectColor = MarkerGlowColor;
        iconGlow.effectDistance = new Vector2(0f, 0f);

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(containerGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0f);
        labelRt.pivot = new Vector2(0.5f, 0f);
        labelRt.sizeDelta = new Vector2(0f, 24f);
        labelRt.anchoredPosition = Vector2.zero;
        wp.LabelText = labelGo.AddComponent<TextMeshProUGUI>();
        wp.LabelText.text = wp.Label ?? wp.Id;
        wp.LabelText.fontSize = 12f;
        wp.LabelText.fontStyle = FontStyles.Bold;
        wp.LabelText.alignment = TextAlignmentOptions.Center;
        wp.LabelText.color = MarkerLabelColor;
        wp.LabelText.raycastTarget = false;
        wp.LabelText.outlineColor = Color.black;
        wp.LabelText.outlineWidth = 0.2f;
    }

    static Sprite _defaultMarker;
    static Sprite GetDefaultMarkerSprite()
    {
        if (_defaultMarker != null) return _defaultMarker;
        var tex = MakeDownTriangleTexture(32);
        _defaultMarker = Sprite.Create(tex, new Rect(0, 0, 32, 32),
                                        new Vector2(0.5f, 0.5f), 100f);
        _defaultMarker.name = "CompassMarker_DownTriangle";
        return _defaultMarker;
    }

    static Texture2D MakeDownTriangleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                bool inside = 2f * Mathf.Abs(u - 0.5f) <= v;
                pixels[y * size + x] = new Color(1f, 1f, 1f, inside ? 1f : 0f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ── New procedural sprites for the sci-fi Skyrim look ──

    static Sprite _stripBgSprite;
    static Sprite GetStripBgSprite()
    {
        if (_stripBgSprite != null) return _stripBgSprite;
        var tex = MakeFadedBarTexture(128, 16, 0.18f);
        // 9-slice borders sized so the fade lives inside the corner cells —
        // middle stretches, edges keep the gradient shape.
        _stripBgSprite = Sprite.Create(tex, new Rect(0, 0, 128, 16), new Vector2(0.5f, 0.5f),
                                       100f, 0u, SpriteMeshType.FullRect, new Vector4(28, 0, 28, 0));
        _stripBgSprite.name = "CompassStripBg";
        return _stripBgSprite;
    }

    static Sprite _sheenSprite;
    static Sprite GetSheenSprite()
    {
        if (_sheenSprite != null) return _sheenSprite;
        var tex = MakeHorizontalSheenTexture(128, 4);
        _sheenSprite = Sprite.Create(tex, new Rect(0, 0, 128, 4), new Vector2(0.5f, 0.5f), 100f);
        _sheenSprite.name = "CompassSheen";
        return _sheenSprite;
    }

    static Texture2D MakeFadedBarTexture(int width, int height, float fadeFraction)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        int fadeWidth = Mathf.Max(1, Mathf.RoundToInt(width * fadeFraction));
        for (int x = 0; x < width; x++)
        {
            float a;
            if (x < fadeWidth)               a = Mathf.SmoothStep(0f, 1f, (float)x / fadeWidth);
            else if (x >= width - fadeWidth) a = Mathf.SmoothStep(0f, 1f, (float)(width - 1 - x) / fadeWidth);
            else                             a = 1f;
            for (int y = 0; y < height; y++)
                pixels[y * width + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Texture2D MakeHorizontalSheenTexture(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / (width - 1);
            float d = Mathf.Abs(u - 0.5f) * 2f;
            float a = Mathf.Clamp01(1f - d * d * d);
            for (int y = 0; y < height; y++)
                pixels[y * width + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ── Save / restore ──

    public List<CompassSave.WaypointEntry> GetSaveState()
    {
        var list = new List<CompassSave.WaypointEntry>();
        for (int i = 0; i < _waypoints.Count; i++)
        {
            var wp = _waypoints[i];
            // Only gameplay waypoints persist; everything else (cardinals,
            // degree numbers, ticks) is rebuilt at runtime.
            if (wp.Kind != WaypointKind.Gameplay) continue;
            list.Add(new CompassSave.WaypointEntry
            {
                id = wp.Id,
                label = wp.Label,
                sourceTag = wp.SourceTag,
                active = wp.Active,
            });
        }
        return list;
    }

    public void ApplySaveState(List<CompassSave.WaypointEntry> entries)
    {
        // Clear gameplay waypoints only — keep the cardinal letters intact.
        for (int i = _waypoints.Count - 1; i >= 0; i--)
        {
            if (_waypoints[i].Kind != WaypointKind.Gameplay) continue;
            if (_waypoints[i].Ui != null) Destroy(_waypoints[i].Ui.gameObject);
            _waypoints.RemoveAt(i);
        }
        if (entries == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.sourceTag)) continue;
            if (e.sourceTag == CardinalSourceTag) continue;
            AddWaypointByTag(e.id, e.sourceTag, e.label);
            if (!e.active) SetActive(e.id, false);
        }
    }
}
