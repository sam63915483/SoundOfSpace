using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// NAV — the solar map that replaced the grid of planet tiles (2026-09-09).
// Ported from prototypes/nav-map, which Sam signed off on; the README there is
// the design rationale and this file is the same thing in UGUI.
//
// The screen is three things and no more:
//
//   • the MAP, left — sun, orbits, planets, and one magenta dashed circle that
//     IS your jump range. Everything inside it you can fly to now. That circle
//     is the whole fuel economy explained without a sentence of text.
//   • the LIST, right — every world with one number each: kilometres if you can
//     reach it, "OPENS 7:25" if you cannot. This is the old NAV screen, kept.
//   • the DESTINATION block — who, whether you can go, distance, fuel. Then GO.
//
// Select something out of reach and the stretch of ITS orbit that sits inside
// range lights up amber, with a ghost of both worlds where they will be when
// the window opens. Magenta is where you can go now; amber is what you are
// waiting for. Two colours, two promises.
//
// Nothing here simulates anything: every planet rides an exact circular rail in
// one plane, so a position is a sine and a cosine, and "when does it come into
// range" is one arccos (World/OrbitRange.cs). That is why the countdowns can be
// promised and be right.
public partial class ShuttleComputerUI
{
    // ── layout ───────────────────────────────────────────────────────────────
    // The NAV view is ScreenW-2*SidePad wide (1456) and the pane below the title
    // is 878 tall. Map + gap + side column adds up to exactly that.
    const float MapPaneW = 1030f;
    const float NavSideW = 404f;
    const float NavRowH = 33f;
    const float NavTravelH = 68f;
    const float NavPickH = 132f;

    RectTransform _navMapRect;
    // The clipped inner rect. Pointer maths goes through THIS one, never the
    // outer map rect — see NavPointerLocal for why that matters.
    RectTransform _navMapClip;
    NavMapGraphic _navMapGfx;
    Image _navFuelFill;

    // Map labels: one per planet, plus the fixed few. Built once, moved every
    // draw — allocating text objects per frame would be madness.
    readonly List<TextMeshProUGUI> _navMapNames = new List<TextMeshProUGUI>();
    TextMeshProUGUI _navMapSub, _navMapShuttle, _navMapGhost, _navMapRangeCap, _navMapScale;

    class NavRow
    {
        public CelestialBody body;
        public Image frame, dot;
        public TextMeshProUGUI name, value;
        public string lastValue;
        public int lastState = -1;      // 0 here, 1 reachable, 2 out of reach
    }
    readonly List<NavRow> _navRows = new List<NavRow>();
    RectTransform _navRowHolder;
    TextMeshProUGUI _navListHeader;
    int _navRowsBuiltFor = -1;

    Image _navPickDot;
    TextMeshProUGUI _navPickName, _navPickStatus, _navPickDist, _navPickCost;

    // Map camera, in sun-relative world metres and pixels-per-metre.
    Vector2 _navMapCentre;
    float _navMapK = 0.03f;
    bool _navMapFramed;
    CelestialBody _navMapHover;
    CelestialBody _navMapHoverSfx;   // last body the hover SFX fired for
    CelestialBody _navSun;

    // Drag state for pan-vs-click. A press that barely moves is a selection; a
    // press that travels is a pan. Same rule the prototype used.
    bool _navMapDragging;
    Vector2 _navMapDragFrom, _navMapDragCentre;
    float _navMapDragTravel;

    float _navNextDrawAt, _navNextTextAt, _navRowsCheckedAt;
    CelestialBody _navFramedFor;

    // Swatches. The planets have no single "colour" the game can ask for — the
    // shading is a whole asset of gradients — so the map carries its own, from
    // the prototype. Cosmetic; retune freely. Anything not listed falls back to
    // a hue derived from the name, so a new dwarf still shows up.
    static readonly Dictionary<string, Color> NavSwatch = new Dictionary<string, Color>
    {
        { "Fiery Twin",   new Color(0.878f, 0.404f, 0.227f) },
        { "Icey Twin",    new Color(0.659f, 0.863f, 0.918f) },
        { "Puddle",       new Color(0.247f, 0.576f, 0.678f) },
        { "Hearth",       new Color(0.443f, 0.675f, 0.369f) },
        { "Anvil",        new Color(0.651f, 0.553f, 0.424f) },
        { "Ember",        new Color(0.780f, 0.294f, 0.235f) },
        { "Slag",         new Color(0.616f, 0.376f, 0.224f) },
        { "Shard",        new Color(0.576f, 0.663f, 0.855f) },
        { "Pebble",       new Color(0.553f, 0.553f, 0.553f) },
        { "Bruise",       new Color(0.490f, 0.424f, 0.537f) },
        { "Humble Abode", new Color(0.373f, 0.682f, 0.510f) },
        { "Cyclops",      new Color(0.416f, 0.631f, 0.592f) },
    };

    static Color SwatchFor(CelestialBody b)
    {
        if (b == null) return Color.grey;
        if (NavSwatch.TryGetValue(b.bodyName, out var c)) return c;
        float h = Mathf.Abs(b.bodyName.GetHashCode() % 1000) / 1000f;
        return Color.HSVToRGB(h, 0.35f, 0.72f);
    }

    static readonly Color MapAmber = new Color(1f, 0.788f, 0.310f);

    // ── construction ─────────────────────────────────────────────────────────

    /// <summary>Builds the whole PARKED screen: fuel gauge, map, list, GO.
    /// Called by BuildNav in place of the old tile grid.</summary>
    void BuildNavMapPane(RectTransform view)
    {
        // Fuel gauge sits on the title line — it is the number that decides
        // where you can go, so it is never more than a glance away.
        var barBack = MakePanel(view, "FuelBar", Hex("050d10ff"));
        Box(barBack.rectTransform, new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-262, -6), new Vector2(240, 14));
        Outline(barBack.transform, Grid);
        _navFuelFill = MakePanel(barBack.rectTransform, "Fill", Ink);
        var ffrt = _navFuelFill.rectTransform;
        ffrt.anchorMin = new Vector2(0, 0);
        ffrt.anchorMax = new Vector2(1, 1);      // anchorMax.x driven by fuel
        ffrt.pivot = new Vector2(0, 0.5f);
        ffrt.offsetMin = new Vector2(1, 1);
        ffrt.offsetMax = new Vector2(-1, -1);

        _navFuelLabel = MakeText(view, "Fuel", "", 15, Ink, TextAlignmentOptions.TopRight);
        Box(_navFuelLabel.rectTransform, new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(0, -2), new Vector2(250, 22));
        _navFuelLabel.characterSpacing = 8;

        var pane = MakeRect(view, "NavListPane");
        Stretch(pane, 0, 0, 34, 0);
        _navListPane = pane.gameObject;

        BuildNavMapArea(pane);
        BuildNavSideColumn(pane);
    }

    void BuildNavMapArea(RectTransform pane)
    {
        var map = MakePanel(pane, "Map", Hex("03070aff"));
        var mrt = map.rectTransform;
        mrt.anchorMin = new Vector2(0, 0);
        mrt.anchorMax = new Vector2(0, 1);
        mrt.pivot = new Vector2(0, 0.5f);
        mrt.sizeDelta = new Vector2(MapPaneW, 0);
        mrt.anchoredPosition = Vector2.zero;
        _navMapRect = mrt;
        Outline(map.transform, Grid);

        // Everything that moves goes inside a clip. An orbit ring is a circle
        // thousands of pixels across and a label can sit anywhere on it, so
        // without this the map draws straight over the destination list. The
        // border stays outside the mask or the mask eats it.
        var clip = MakeRect(mrt, "Clip");
        Stretch(clip, 1, 1, 1, 1);
        clip.gameObject.AddComponent<RectMask2D>();
        _navMapClip = clip;

        var gfxGo = new GameObject("MapMesh", typeof(RectTransform));
        gfxGo.transform.SetParent(clip, false);
        _navMapGfx = gfxGo.AddComponent<NavMapGraphic>();
        Stretch((RectTransform)gfxGo.transform, 0, 0, 0, 0);

        // Framing presets. Two, deliberately: the map is not a flight sim.
        float bx = -10f;
        MakeMapTool(mrt, "WHOLE SYSTEM", ref bx, () => { NavFrameSystem(); });
        MakeMapTool(mrt, "MY RANGE", ref bx, () =>
        {
            var pilot = ShuttleAutopilot.Instance;
            NavFrameRange(pilot != null ? pilot.CurrentBody : null, ShuttleFuel.Instance);
        });

        // Labels ride inside the clip with the mesh — a planet just off the
        // left edge must not write its name across the destination list.
        for (int i = 0; i < 24; i++)      // pool: more than the scene will ever hold
        {
            var t = MakeText(clip, "Name" + i, "", 12, Ink, TextAlignmentOptions.Center);
            Box(t.rectTransform, Centre, Centre, Vector2.zero, new Vector2(190, 16));
            t.gameObject.SetActive(false);
            _navMapNames.Add(t);
        }

        _navMapSub      = MakeMapLabel(clip, "Sub", 11, InkDim);
        _navMapShuttle  = MakeMapLabel(clip, "Shuttle", 10, Ink);
        _navMapGhost    = MakeMapLabel(clip, "Ghost", 11, MapAmber);
        _navMapRangeCap = MakeMapLabel(clip, "RangeCap", 11, Accent);
        _navMapScale    = MakeMapLabel(clip, "Scale", 11, Hex("2f6b5cff"));

        // Toast lives over the map so it can never cover the GO button.
        _navToastLabel = MakeText(mrt, "Toast", "", 16, Warn, TextAlignmentOptions.Center);
        var tort = _navToastLabel.rectTransform;
        tort.anchorMin = new Vector2(0, 0); tort.anchorMax = new Vector2(1, 0);
        tort.pivot = new Vector2(0.5f, 0);
        tort.sizeDelta = new Vector2(0, 24);
        tort.anchoredPosition = new Vector2(0, 44);
    }

    TextMeshProUGUI MakeMapLabel(RectTransform parent, string name, float size, Color c)
    {
        var t = MakeText(parent, name, "", size, c, TextAlignmentOptions.Center);
        Box(t.rectTransform, Centre, Centre, Vector2.zero, new Vector2(240, 16));
        t.gameObject.SetActive(false);
        return t;
    }

    void MakeMapTool(RectTransform parent, string label, ref float x, System.Action onClick)
    {
        const float w = 116f, h = 26f;
        var frame = MakePanel(parent, "Tool_" + label, Hex("071014d0"));
        frame.raycastTarget = true;      // MakePanel defaults raycasts OFF
        Box(frame.rectTransform, new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(x, -10f), new Vector2(w, h));
        Outline(frame.transform, Grid);
        var t = MakeText(frame.rectTransform, "Label", label, 11, InkGhost, TextAlignmentOptions.Center);
        Stretch(t.rectTransform, 0, 0, 0, 0);
        t.characterSpacing = 12;
        var btn = frame.gameObject.AddComponent<Button>();
        btn.targetGraphic = frame;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        cb.pressedColor = new Color(2f, 2f, 2f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(delegate { onClick(); });
        x -= w + 6f;
    }

    void BuildNavSideColumn(RectTransform pane)
    {
        // list
        var listPane = MakePanel(pane, "List", Panel);
        var lrt = listPane.rectTransform;
        lrt.anchorMin = new Vector2(1, 1);
        lrt.anchorMax = new Vector2(1, 1);
        lrt.pivot = new Vector2(1, 1);
        lrt.sizeDelta = new Vector2(NavSideW, 40f + NavRowH * 12f);
        lrt.anchoredPosition = Vector2.zero;
        Outline(listPane.transform, Grid);

        _navListHeader = MakeText(lrt, "Hd", "DESTINATIONS", 12, InkGhost, TextAlignmentOptions.TopLeft);
        Box(_navListHeader.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(14, -11), new Vector2(380, 18));
        _navListHeader.characterSpacing = 20;

        _navRowHolder = MakeRect(lrt, "Rows");
        Stretch(_navRowHolder, 8, 8, 38, 6);

        // destination block
        var pick = MakePanel(pane, "Pick", Panel);
        var prt = pick.rectTransform;
        prt.anchorMin = new Vector2(1, 0);
        prt.anchorMax = new Vector2(1, 0);
        prt.pivot = new Vector2(1, 0);
        prt.sizeDelta = new Vector2(NavSideW, NavPickH);
        prt.anchoredPosition = new Vector2(0, NavTravelH + 10f);
        Outline(pick.transform, Grid);

        _navPickDot = MakeSprite(prt, "Dot", TraxUISprites.Disc, Hex("132025ff"));
        Box(_navPickDot.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(16, -20), new Vector2(13, 13));

        _navPickName = MakeText(prt, "Name", "—", 26, Locked, TextAlignmentOptions.TopLeft);
        Box(_navPickName.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(37, -8), new Vector2(350, 34));
        _navPickName.characterSpacing = 10;

        _navPickStatus = MakeText(prt, "Status", "SELECT A DESTINATION", 14, InkGhost,
                                  TextAlignmentOptions.TopLeft);
        Box(_navPickStatus.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(16, -46), new Vector2(372, 20));
        _navPickStatus.characterSpacing = 9;

        MakePickCaption(prt, "DISTANCE", 16f);
        MakePickCaption(prt, "FUEL", 168f);
        _navPickDist = MakeText(prt, "Dist", "—", 17, Ink, TextAlignmentOptions.TopLeft);
        Box(_navPickDist.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(16, -92), new Vector2(150, 22));
        _navPickCost = MakeText(prt, "Cost", "—", 17, Ink, TextAlignmentOptions.TopLeft);
        Box(_navPickCost.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(168, -92), new Vector2(220, 22));

        // TRAVEL — enabled only with a destination you can pay for.
        var btn = MakePanel(pane, "TravelBtn", Panel);
        btn.raycastTarget = true;
        _navTravelBg = btn;
        var brt = btn.rectTransform;
        brt.anchorMin = new Vector2(1, 0);
        brt.anchorMax = new Vector2(1, 0);
        brt.pivot = new Vector2(1, 0);
        brt.sizeDelta = new Vector2(NavSideW, NavTravelH);
        brt.anchoredPosition = Vector2.zero;
        Outline(btn.transform, Grid);
        _navTravelLabel = MakeText(brt, "Label", "TRAVEL", 23, Locked, TextAlignmentOptions.Center);
        Stretch(_navTravelLabel.rectTransform, 0, 0, 0, 0);
        _navTravelLabel.characterSpacing = 24;
        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        var cb2 = b.colors;
        cb2.normalColor = Color.white;
        cb2.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        cb2.pressedColor = new Color(2f, 2f, 2f, 1f);
        b.colors = cb2;
        b.onClick.AddListener(OnNavTravelClicked);
        UiSfxPlayer.Attach(b);   // shared hover + click SFX
    }

    void MakePickCaption(RectTransform parent, string text, float x)
    {
        var t = MakeText(parent, "Cap_" + text, text, 10, InkGhost, TextAlignmentOptions.TopLeft);
        Box(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(x, -74), new Vector2(180, 14));
        t.characterSpacing = 20;
    }

    // ── rows ─────────────────────────────────────────────────────────────────

    /// <summary>The list is built once and then only re-lettered. Rebuilding it
    /// every tick would throw the pad's focus away mid-press, and re-sorting it
    /// live would move rows under the cursor — so the order is fixed, sun
    /// outward, and never moves.</summary>
    void EnsureNavRows()
    {
        // LandablePlanets() allocates a List and walks every body, so this is
        // NOT something to ask once a frame on an open screen. The planet set
        // only changes when the scene does.
        if (_navRows.Count > 0 && Time.unscaledTime < _navRowsCheckedAt) return;
        _navRowsCheckedAt = Time.unscaledTime + 2f;

        var planets = ShuttleAutopilot.LandablePlanets();
        if (planets.Count == _navRowsBuiltFor && _navRows.Count == planets.Count) return;
        _navRowsBuiltFor = planets.Count;

        foreach (Transform child in _navRowHolder) Destroy(child.gameObject);
        _navRows.Clear();
        if (planets.Count == 0) return;

        planets.Sort((x, y) => NavOrbitRadius(x).CompareTo(NavOrbitRadius(y)));

        for (int i = 0; i < planets.Count; i++)
        {
            var body = planets[i];
            var frame = MakePanel(_navRowHolder, "Row_" + body.bodyName, new Color(0, 0, 0, 0));
            frame.raycastTarget = true;      // MakePanel defaults raycasts OFF
            var rt = frame.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, NavRowH);
            rt.anchoredPosition = new Vector2(0, -i * NavRowH);

            var dot = MakeSprite(rt, "Dot", TraxUISprites.Disc, SwatchFor(body));
            Box(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(9, 0), new Vector2(9, 9));

            var name = MakeText(rt, "Name", body.bodyName.ToUpperInvariant(), 15, Ink,
                                TextAlignmentOptions.Left);
            Box(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(26, 0), new Vector2(240, 20));
            name.characterSpacing = 10;

            var value = MakeText(rt, "Val", "", 13, InkDim, TextAlignmentOptions.Right);
            Box(value.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-8, 0), new Vector2(180, 20));

            _navRows.Add(new NavRow { body = body, frame = frame, dot = dot, name = name, value = value });

            string captured = body.bodyName;
            var btn = frame.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            cb.pressedColor = new Color(1.8f, 1.8f, 1.8f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(delegate { OnNavPlanetClicked(captured); });
        }
    }

    static float NavOrbitRadius(CelestialBody b)
    {
        return OrbitRange.TryRail(b, out float r, out _, out _) ? r : 0f;
    }

    void OnNavPlanetClicked(string bodyName)
    {
        _navSelected = bodyName;
        NavSelectionChanged();
    }

    /// <summary>Repaint at once instead of waiting for the next cadence tick —
    /// a click that takes a quarter-second to show is a click that did not
    /// land. Also the co-op hook: the other player's pick arrives this way.</summary>
    void NavSelectionChanged()
    {
        _navNextTextAt = 0f;
        _navNextDrawAt = 0f;
    }

    CelestialBody NavBodyByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < _navRows.Count; i++)
            if (_navRows[i].body != null && _navRows[i].body.bodyName == name) return _navRows[i].body;
        return null;
    }

    CelestialBody NavSun()
    {
        if (_navSun != null) return _navSun;
        var bodies = NBodySimulation.Bodies;
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i] != null && bodies[i].bodyName == "Sun") { _navSun = bodies[i]; break; }
        return _navSun;
    }

    // ── camera ───────────────────────────────────────────────────────────────

    Vector2 NavSunRel(CelestialBody b)
    {
        var sun = NavSun();
        if (b == null || sun == null) return Vector2.zero;
        Vector3 p = b.Position - sun.Position;
        return new Vector2(p.x, p.y);
    }

    Vector2 NavToLocal(Vector2 sunRel)
    {
        return new Vector2((sunRel.x - _navMapCentre.x) * _navMapK,
                           (sunRel.y - _navMapCentre.y) * _navMapK);
    }

    /// The map's height, guarded. A RectTransform read before the layout has
    /// settled can hand back 0, and a zero here would flip the zoom negative
    /// and draw the whole system mirrored and inside out.
    float NavMapHeight
    {
        get
        {
            float h = _navMapRect != null ? _navMapRect.rect.height : 0f;
            return h < 100f ? 860f : h;
        }
    }

    float NavMapShort => Mathf.Min(MapPaneW, NavMapHeight);

    void NavFrameRange(CelestialBody here, ShuttleFuel tank)
    {
        // Frame the tank, not the system: half a view of range either side, and
        // never so tight that an empty tank zooms into nothing.
        float rangeM = tank != null ? tank.RangeKm * 1000f : 8000f;
        float half = Mathf.Max(rangeM * 1.35f, 4000f);
        _navMapCentre = here != null ? NavSunRel(here) : Vector2.zero;
        _navMapK = (NavMapShort - 60f) / (half * 2f);
        _navMapFramed = true;
    }

    void NavFrameSystem()
    {
        float outer = 1000f;
        for (int i = 0; i < _navRows.Count; i++)
            outer = Mathf.Max(outer, NavOrbitRadius(_navRows[i].body));
        _navMapCentre = Vector2.zero;
        _navMapK = (NavMapShort - 76f) / (outer * 2.16f);
        _navMapFramed = true;
    }

    // ── per-frame drive ──────────────────────────────────────────────────────

    /// <summary>Called from NavDrive every frame while PARKED (open or
    /// mirroring). Text refreshes twice a second, the mesh thirty times — the
    /// planets do not move fast enough for anyone to catch the difference, and
    /// rebuilding either every frame is pure waste.</summary>
    void NavMapDrive(ShuttleAutopilot pilot)
    {
        EnsureNavRows();
        var here = pilot != null ? pilot.CurrentBody : null;
        var tank = ShuttleFuel.Instance;
        // Re-frame when the shuttle lands somewhere new, or the map would still
        // be centred on the planet you just left.
        if (here != null && (!_navMapFramed || _navFramedFor != here))
        {
            NavFrameRange(here, tank);
            _navFramedFor = here;
        }

        if (Time.unscaledTime >= _navNextTextAt)
        {
            _navNextTextAt = Time.unscaledTime + 0.25f;
            NavMapText(here, tank);
        }
        if (Time.unscaledTime >= _navNextDrawAt)
        {
            _navNextDrawAt = Time.unscaledTime + 1f / 30f;
            NavMapDraw(here, tank);
        }
    }

    static string NavClock(float seconds)
    {
        if (float.IsInfinity(seconds) || float.IsNaN(seconds)) return "—";
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    /// <summary>What the destination block says about a world: the one line that
    /// decides whether you press GO, wait, or give up.</summary>
    string NavStatusLine(CelestialBody here, CelestialBody target, ShuttleFuel tank, out Color col)
    {
        col = Ink;
        if (here == null || target == null) return "";
        if (target == here) { col = InkDim; return "YOU ARE HERE  ·  RELOCATE"; }

        float metres = Vector3.Distance(target.Position, here.Position);
        float rangeM = tank != null ? tank.RangeKm * 1000f : float.MaxValue;
        if (tank == null || tank.CanAfford(metres))
        {
            // Only mention the window when it is actually about to shut. Saying
            // "closes in 47 minutes" on every planet is noise.
            float closes = OrbitRange.SecondsUntilOutOfRange(here, target, rangeM);
            return closes < 240f ? "IN RANGE  ·  CLOSES " + NavClock(closes) : "IN RANGE";
        }
        var status = OrbitRange.Evaluate(here, target, rangeM, out float wait);
        if (status == OrbitRange.Status.Waiting) { col = Warn; return "IN RANGE IN " + NavClock(wait); }
        col = Hex("ff6b6bff");
        return "OUT OF REACH ON THIS TANK";
    }

    void NavMapText(CelestialBody here, ShuttleFuel tank)
    {
        // fuel gauge
        if (_navFuelLabel != null)
        {
            string line = tank == null ? ""
                : "FUEL " + Mathf.RoundToInt(tank.FuelPercent * 100f) + "%   ·   RANGE "
                  + tank.RangeKm.ToString("0.0") + " KM";
            if (line != _navFuelShown) { _navFuelShown = line; _navFuelLabel.text = line; }
        }
        if (_navFuelFill != null && tank != null)
        {
            float pct = tank.FuelPercent;
            var max = _navFuelFill.rectTransform.anchorMax;
            max.x = Mathf.Clamp01(pct);
            if (_navFuelFill.rectTransform.anchorMax != max) _navFuelFill.rectTransform.anchorMax = max;
            Color want = pct < 0.16f ? Hex("ff5b5bff") : pct < 0.34f ? Warn : Ink;
            if (_navFuelFill.color != want) _navFuelFill.color = want;
        }

        float rangeM = tank != null ? tank.RangeKm * 1000f : float.MaxValue;
        int inRange = 0;

        for (int i = 0; i < _navRows.Count; i++)
        {
            var row = _navRows[i];
            if (row.body == null) continue;
            bool isHere = row.body == here;
            float metres = here != null ? Vector3.Distance(row.body.Position, here.Position) : 0f;
            bool afford = !isHere && here != null && (tank == null || tank.CanAfford(metres));
            if (afford) inRange++;

            string val;
            if (isHere) val = "PARKED";
            else if (afford) val = (metres / 1000f).ToString("0.0") + " km";
            else
            {
                var status = OrbitRange.Evaluate(here, row.body, rangeM, out float wait);
                val = status == OrbitRange.Status.Waiting ? "opens " + NavClock(wait) : "out of reach";
            }
            if (val != row.lastValue) { row.lastValue = val; row.value.text = val; }

            int state = isHere ? 0 : afford ? 1 : 2;
            bool selected = row.body.bodyName == _navSelected;
            int stateKey = state * 2 + (selected ? 1 : 0);
            if (stateKey != row.lastState)
            {
                row.lastState = stateKey;
                row.name.color  = state == 0 ? InkDim : state == 1 ? Ink : Locked;
                row.value.color = state == 0 ? InkGhost : state == 1 ? InkDim : Warn;
                row.dot.color   = state == 2 ? SwatchFor(row.body) * 0.45f : SwatchFor(row.body);
                row.frame.color = selected ? PanelHi : new Color(0, 0, 0, 0);
            }
        }

        if (_navListHeader != null)
        {
            string hd = "DESTINATIONS — " + inRange + " IN RANGE";
            if (_navListHeader.text != hd) _navListHeader.text = hd;
        }

        // destination block
        var sel = NavBodyByName(_navSelected);
        bool canTravel = false;
        if (sel == null || here == null)
        {
            _navPickDot.color = Hex("132025ff");
            SetTextIfChanged(_navPickName, "—");
            if (_navPickName.color != Locked) _navPickName.color = Locked;
            SetTextIfChanged(_navPickStatus, "SELECT A DESTINATION");
            if (_navPickStatus.color != InkGhost) _navPickStatus.color = InkGhost;
            SetTextIfChanged(_navPickDist, "—");
            SetTextIfChanged(_navPickCost, "—");
            if (_navPickCost.color != Ink) _navPickCost.color = Ink;
        }
        else
        {
            float metres = sel == here ? 0f : Vector3.Distance(sel.Position, here.Position);
            float cost = tank != null ? tank.CostForMetres(metres) : 0f;
            bool afford = tank == null || tank.CanAfford(metres);
            canTravel = afford;

            _navPickDot.color = SwatchFor(sel);
            SetTextIfChanged(_navPickName, sel.bodyName.ToUpperInvariant());
            if (_navPickName.color != Ink) _navPickName.color = Ink;
            string status = NavStatusLine(here, sel, tank, out Color scol);
            SetTextIfChanged(_navPickStatus, status);
            if (_navPickStatus.color != scol) _navPickStatus.color = scol;
            SetTextIfChanged(_navPickDist, sel == here ? "HERE" : (metres / 1000f).ToString("0.0") + " KM");
            SetTextIfChanged(_navPickCost, tank == null ? "—"
                : cost.ToString("0") + " u  ·  " + Mathf.RoundToInt(cost / tank.FuelMax * 100f) + "%");
            Color cc = afford ? Ink : Warn;
            if (_navPickCost.color != cc) _navPickCost.color = cc;
        }

        SetTextIfChanged(_navTravelLabel, canTravel || sel == null ? "TRAVEL" : "NOT ENOUGH FUEL");
        Color tl = canTravel ? Ink : Locked;
        if (_navTravelLabel.color != tl) _navTravelLabel.color = tl;
        Color tb = canTravel ? PanelHi : Panel;
        if (_navTravelBg.color != tb) _navTravelBg.color = tb;
    }

    // ── the map itself ───────────────────────────────────────────────────────

    void NavMapDraw(CelestialBody here, ShuttleFuel tank)
    {
        if (_navMapGfx == null || _navMapRect == null) return;
        var g = _navMapGfx;
        g.Clear();

        float halfW = MapPaneW * 0.5f, halfH = NavMapHeight * 0.5f;
        Vector2 sunAt = NavToLocal(Vector2.zero);
        float rangeM = tank != null ? tank.RangeKm * 1000f : 0f;

        NavDrawStars(g, halfW, halfH);

        // orbit rings
        var sel = NavBodyByName(_navSelected);
        for (int i = 0; i < _navRows.Count; i++)
        {
            var b = _navRows[i].body;
            if (b == null) continue;
            float r = NavOrbitRadius(b) * _navMapK;
            if (r < 2f || !NavRingOnScreen(sunAt, r, halfW, halfH)) continue;
            bool lit = b == sel || b == _navMapHover;
            g.Ring(sunAt, r, lit ? Hex("1d5a68ff") : Grid, lit ? 1.4f : 1f);
        }

        // The stretch of the selected world's orbit that sits inside range.
        // Amber while you are waiting for it, mint when you are already there.
        if (sel != null && here != null && sel != here && rangeM > 0f)
        {
            float half = OrbitRange.WindowHalfAngle(here, sel, rangeM);
            if (half > 0.001f && half < Mathf.PI - 0.001f &&
                OrbitRange.TryRail(here, out _, out _, out float hereAng))
            {
                float rr = NavOrbitRadius(sel) * _navMapK;
                bool inNow = tank == null ||
                             tank.CanAfford(Vector3.Distance(sel.Position, here.Position));
                Color arc = inNow ? new Color(Ink.r, Ink.g, Ink.b, 0.5f)
                                  : new Color(MapAmber.r, MapAmber.g, MapAmber.b, 0.6f);
                g.Arc(sunAt, rr, hereAng - half, hereAng + half, arc, 2.4f);
            }
        }

        // jump range — the one circle that matters
        if (here != null && rangeM > 0f)
        {
            Vector2 hp = NavToLocal(NavSunRel(here));
            float rr = rangeM * _navMapK;
            if (rr > 3f)
            {
                g.Disc(hp, rr, new Color(Accent.r, Accent.g, Accent.b, 0f),
                       new Color(Accent.r, Accent.g, Accent.b, 0.07f), Vector2.zero);
                g.DashedRing(hp, rr, new Color(Accent.r, Accent.g, Accent.b, 0.75f), 1.4f, 7f, 6f);
            }
        }

        // sun
        g.Disc(sunAt, 74f, new Color(1f, 0.84f, 0.49f, 0.55f), new Color(1f, 0.59f, 0.24f, 0f), Vector2.zero);
        g.Disc(sunAt, 9f, Hex("ffd98aff"));

        NavDrawGhost(g, here, sel, tank, rangeM);
        NavDrawBodies(g, here, sel, tank);
        NavDrawScale(g, halfW, halfH);

        g.Commit();
    }

    /// A fixed, deterministic starfield in map space. Not parallaxed and not
    /// animated: it is texture, not information. Rolled once — re-rolling it
    /// thirty times a second would allocate a Random and 400 floats per second
    /// to draw exactly the same dots.
    Vector4[] _navStars;    // x, y (fractions of the half-extent), size, alpha

    void NavDrawStars(NavMapGraphic g, float halfW, float halfH)
    {
        if (_navStars == null)
        {
            var rng = new System.Random(1977);
            _navStars = new Vector4[130];
            for (int i = 0; i < _navStars.Length; i++)
                _navStars[i] = new Vector4(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    rng.NextDouble() < 0.12 ? 2f : 1.3f,
                    0.10f + (float)rng.NextDouble() * 0.30f);
        }
        for (int i = 0; i < _navStars.Length; i++)
        {
            var s = _navStars[i];
            g.Dot(new Vector2(s.x * halfW, s.y * halfH), s.z,
                  new Color(0.63f, 0.86f, 0.82f, s.w));
        }
    }

    void NavDrawBodies(NavMapGraphic g, CelestialBody here, CelestialBody sel, ShuttleFuel tank)
    {
        Vector2 sunAt = NavToLocal(Vector2.zero);

        // moons ride along at their real positions — no rails of their own to draw
        var bodies = NBodySimulation.Bodies;
        for (int i = 0; i < bodies.Length; i++)
        {
            var m = bodies[i];
            if (m == null || m.bodyType != CelestialBody.BodyType.Moon) continue;
            g.Disc(NavToLocal(NavSunRel(m)), 2.4f, Hex("6b7a80ff"));
        }

        for (int i = 0; i < _navRows.Count; i++)
        {
            var b = _navRows[i].body;
            if (b == null) continue;
            Vector2 p = NavToLocal(NavSunRel(b));
            float r = NavDotRadius(b);
            bool isHere = b == here;
            bool reach = isHere || here == null || tank == null ||
                         tank.CanAfford(Vector3.Distance(b.Position, here.Position));

            Color swatch = SwatchFor(b);
            Color edge = swatch * (reach ? 0.42f : 0.20f);
            edge.a = 1f;
            Color core = reach ? swatch : swatch * 0.55f;
            core.a = 1f;
            // Lit from the sun, so the map reads as a place and not a chart.
            Vector2 toSun = (sunAt - p);
            if (toSun.sqrMagnitude > 0.01f) toSun = toSun.normalized * (r * 0.45f);
            g.Disc(p, r, core, edge, toSun);

            if (isHere) g.DashedRing(p, r + 7f, Ink, 1.6f, 5f, 4f);
            else if (reach) g.Ring(p, r + 4.5f, new Color(Ink.r, Ink.g, Ink.b, 0.5f), 1.1f);
            if (b == sel) g.Ring(p, r + 11f, Accent, 2f);
            else if (b == _navMapHover) g.Ring(p, r + 11f, new Color(Accent.r, Accent.g, Accent.b, 0.45f), 1.2f);
        }

        NavPlaceLabels(g, here, sel, tank);
    }

    /// <summary>Does a circle centred at <paramref name="c"/> cross the map at
    /// all? Zoomed in, most orbits are either entirely off the edge or entirely
    /// swallowing the view, and either way they are thousands of vertices
    /// nobody sees — this is the difference between a cheap redraw and a
    /// wasteful one.</summary>
    static bool NavRingOnScreen(Vector2 c, float r, float halfW, float halfH)
    {
        float dx = Mathf.Max(Mathf.Abs(c.x) - halfW, 0f);
        float dy = Mathf.Max(Mathf.Abs(c.y) - halfH, 0f);
        float nearest = Mathf.Sqrt(dx * dx + dy * dy);
        float fx = Mathf.Abs(c.x) + halfW, fy = Mathf.Abs(c.y) + halfH;
        float farthest = Mathf.Sqrt(fx * fx + fy * fy);
        return r >= nearest - 2f && r <= farthest + 2f;
    }

    static float NavDotRadius(CelestialBody b)
    {
        return Mathf.Clamp(3.4f + Mathf.Sqrt(Mathf.Max(1f, b.radius)) * 0.52f, 4.4f, 15f);
    }

    /// Where both worlds — and your range circle — will be when the window
    /// opens. This is what turns "in range in 3:54" from a number into a
    /// picture of two planets swinging together.
    void NavDrawGhost(NavMapGraphic g, CelestialBody here, CelestialBody sel,
                      ShuttleFuel tank, float rangeM)
    {
        _navMapGhost.gameObject.SetActive(false);
        if (here == null || sel == null || sel == here || tank == null || rangeM <= 0f) return;
        if (tank.CanAfford(Vector3.Distance(sel.Position, here.Position))) return;
        if (OrbitRange.Evaluate(here, sel, rangeM, out float wait) != OrbitRange.Status.Waiting) return;
        if (!OrbitRange.TryRail(here, out float rH, out float wH, out float aH)) return;
        if (!OrbitRange.TryRail(sel, out float rS, out float wS, out float aS)) return;

        Vector2 futureHere = new Vector2(Mathf.Cos(aH + wH * wait), Mathf.Sin(aH + wH * wait)) * rH;
        Vector2 futureSel  = new Vector2(Mathf.Cos(aS + wS * wait), Mathf.Sin(aS + wS * wait)) * rS;
        Vector2 lh = NavToLocal(futureHere), ls = NavToLocal(futureSel);

        var faint = new Color(MapAmber.r, MapAmber.g, MapAmber.b, 0.30f);
        g.DashedRing(lh, rangeM * _navMapK, faint, 1.1f, 3f, 5f);
        var solid = new Color(MapAmber.r, MapAmber.g, MapAmber.b, 0.75f);
        g.Ring(lh, NavDotRadius(here) * 0.8f, solid, 1.3f);
        g.Ring(ls, NavDotRadius(sel) * 0.8f, solid, 1.3f);

        _navMapGhost.gameObject.SetActive(true);
        _navMapGhost.rectTransform.anchoredPosition = ls + new Vector2(0, -NavDotRadius(sel) - 14f);
        SetTextIfChanged(_navMapGhost, "WINDOW OPENS " + NavClock(wait));
    }

    void NavDrawScale(NavMapGraphic g, float halfW, float halfH)
    {
        // A round number of km that lands near 150 px.
        float raw = 150f / _navMapK / 1000f;
        if (raw <= 0f || float.IsInfinity(raw)) { _navMapScale.gameObject.SetActive(false); return; }
        float pow = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
        float best = pow; float bestErr = Mathf.Abs(pow - raw);
        for (int i = 0; i < NavScaleSteps.Length; i++)
        {
            float cand = NavScaleSteps[i] * pow, err = Mathf.Abs(cand - raw);
            if (err < bestErr) { best = cand; bestErr = err; }
        }
        float px = best * 1000f * _navMapK;
        float x1 = halfW - 22f, x0 = x1 - px, y = -halfH + 22f;
        var c = Hex("2f6b5cff");
        g.Line(new Vector2(x0, y), new Vector2(x1, y), c, 1f);
        g.Line(new Vector2(x0, y), new Vector2(x0, y + 5f), c, 1f);
        g.Line(new Vector2(x1, y), new Vector2(x1, y + 5f), c, 1f);
        _navMapScale.gameObject.SetActive(true);
        _navMapScale.rectTransform.anchoredPosition = new Vector2((x0 + x1) * 0.5f, y + 15f);
        SetTextIfChanged(_navMapScale, best >= 1f ? best.ToString("0.#") + " KM"
                                                  : (best * 1000f).ToString("0") + " M");
    }

    // ── labels, decluttered ──────────────────────────────────────────────────

    struct LabelBox { public float x0, x1, y0, y1; }
    readonly List<LabelBox> _navLabelBoxes = new List<LabelBox>(48);
    readonly List<int> _navLabelOrder = new List<int>(24);

    static bool BoxesHit(LabelBox a, LabelBox b)
    {
        return !(a.x1 < b.x0 || a.x0 > b.x1 || a.y1 < b.y0 || a.y0 > b.y1);
    }

    /// <summary>
    /// The twins sit 1 km apart on the same rail, so at any sensible zoom their
    /// names land on top of each other — the first pass of this map read
    /// "FIER TWIN" with a planet through the middle of it. Anything that would
    /// overlap is nudged clear and gets a leader line back to its dot, and the
    /// planet discs themselves are booked out first so no name is ever written
    /// across a world.
    /// </summary>
    void NavPlaceLabels(NavMapGraphic g, CelestialBody here, CelestialBody sel, ShuttleFuel tank)
    {
        _navLabelBoxes.Clear();
        _navLabelOrder.Clear();

        for (int i = 0; i < _navRows.Count && i < _navMapNames.Count; i++)
        {
            var b = _navRows[i].body;
            if (b == null) continue;
            Vector2 p = NavToLocal(NavSunRel(b));
            float r = NavDotRadius(b);
            _navLabelBoxes.Add(new LabelBox { x0 = p.x - r - 3f, x1 = p.x + r + 3f,
                                              y0 = p.y - r - 3f, y1 = p.y + r + 3f });
            _navLabelOrder.Add(i);
        }

        // Selected first, then here, then anything you can reach: the important
        // labels win a fight for space and the scenery gives way.
        _navLabelOrder.Sort((x, y) => NavLabelRank(_navRows[y].body, here, sel, tank)
                                    - NavLabelRank(_navRows[x].body, here, sel, tank));

        for (int n = 0; n < _navMapNames.Count; n++)
            if (n >= _navLabelOrder.Count) _navMapNames[n].gameObject.SetActive(false);

        _navMapSub.gameObject.SetActive(false);
        _navMapShuttle.gameObject.SetActive(false);

        float halfW = MapPaneW * 0.5f, halfH = NavMapHeight * 0.5f;

        for (int k = 0; k < _navLabelOrder.Count; k++)
        {
            int i = _navLabelOrder[k];
            var b = _navRows[i].body;
            var label = _navMapNames[k];
            Vector2 p = NavToLocal(NavSunRel(b));
            float r = NavDotRadius(b);

            // Off-screen labels cost nothing to skip and stop the declutter from
            // shuffling names nobody can see.
            if (p.x < -halfW - 40f || p.x > halfW + 40f || p.y < -halfH - 40f || p.y > halfH + 40f)
            { label.gameObject.SetActive(false); continue; }

            bool isHere = b == here;
            bool reach = isHere || here == null || tank == null ||
                         tank.CanAfford(Vector3.Distance(b.Position, here.Position));

            string text = b.bodyName.ToUpperInvariant();
            // Measuring real text every frame would force a TMP regeneration per
            // label; the box only has to be about right.
            float halfWidth = Mathf.Max(text.Length * 7.2f, 46f) * 0.5f + 3f;
            bool ringed = b == sel || b == _navMapHover;
            float baseY = p.y + r + (ringed ? 17f : 9f);

            float dy = 0f;
            for (int s = 0; s < NavSlots.Length; s++)
            {
                dy = NavSlots[s];
                var box = new LabelBox { x0 = p.x - halfWidth, x1 = p.x + halfWidth,
                                         y0 = baseY + dy - 3f, y1 = baseY + dy + 11f };
                bool clash = false;
                for (int q = 0; q < _navLabelBoxes.Count && !clash; q++)
                    clash = BoxesHit(box, _navLabelBoxes[q]);
                if (!clash) { _navLabelBoxes.Add(box); break; }
            }

            float ny = baseY + dy;
            if (Mathf.Abs(dy) > 6f)
                g.Line(new Vector2(p.x, ny - 2f), new Vector2(p.x, p.y + r + 3f),
                       new Color(InkDim.r, InkDim.g, InkDim.b, 0.45f), 1f);

            label.gameObject.SetActive(true);
            label.rectTransform.anchoredPosition = new Vector2(p.x, ny);
            SetTextIfChanged(label, text);
            Color lc = isHere ? Ink : reach ? Hex("a9d9cbff") : Hex("4d6a71ff");
            if (label.color != lc) label.color = lc;

            // A distance under EVERY planet was most of the clutter — twelve
            // numbers nobody asked for. Only the one you point at gets one.
            if (isHere)
            {
                _navMapShuttle.gameObject.SetActive(true);
                _navMapShuttle.rectTransform.anchoredPosition = new Vector2(p.x, p.y - r - 15f);
                SetTextIfChanged(_navMapShuttle, "SHUTTLE");
            }
            else if ((b == sel || b == _navMapHover) && here != null)
            {
                float km = Vector3.Distance(b.Position, here.Position) / 1000f;
                _navMapSub.gameObject.SetActive(true);
                _navMapSub.rectTransform.anchoredPosition = new Vector2(p.x, p.y - r - 15f);
                SetTextIfChanged(_navMapSub, km.ToString("0.0") + " km");
                Color sc = reach ? new Color(Ink.r, Ink.g, Ink.b, 0.7f) : Warn;
                if (_navMapSub.color != sc) _navMapSub.color = sc;
            }
        }

        // caption on the range circle
        if (here != null && tank != null && tank.RangeKm > 0.05f)
        {
            Vector2 hp = NavToLocal(NavSunRel(here));
            float rr = tank.RangeKm * 1000f * _navMapK;
            bool show = rr > 60f && rr < 4000f;
            if (_navMapRangeCap.gameObject.activeSelf != show) _navMapRangeCap.gameObject.SetActive(show);
            if (show)
            {
                _navMapRangeCap.rectTransform.anchoredPosition = hp + new Vector2(0, rr + 12f);
                SetTextIfChanged(_navMapRangeCap, "JUMP RANGE " + tank.RangeKm.ToString("0.0") + " KM");
            }
        }
        else if (_navMapRangeCap.gameObject.activeSelf) _navMapRangeCap.gameObject.SetActive(false);
    }

    static readonly float[] NavSlots = { 0f, 15f, -16f, 30f, -31f, 45f, -46f, 60f, -61f };
    static readonly float[] NavScaleSteps = { 2f, 5f, 10f };

    static int NavLabelRank(CelestialBody b, CelestialBody here, CelestialBody sel, ShuttleFuel tank)
    {
        int rank = 0;
        if (b == sel) rank += 4;
        if (b == here) rank += 3;
        else if (here != null && (tank == null || tank.CanAfford(Vector3.Distance(b.Position, here.Position))))
            rank += 1;
        return rank;
    }

    // ── input ────────────────────────────────────────────────────────────────

    /// <summary>Wheel zooms about the pointer, drag pans, a press that barely
    /// moves selects. Only while THIS player has the terminal open — the
    /// cockpit mirror is a picture of someone else's screen.</summary>
    void NavMapInput()
    {
        if (_navMapRect == null || !ShuttleComputerUI.IsOpen) return;
        var pilot = ShuttleAutopilot.Instance;
        if (pilot != null && pilot.CurrentPhase != ShuttleAutopilot.Phase.Parked)
        {
            _navMapDragging = false;
            return;
        }

        if (!NavPointerLocal(out Vector2 local, out bool inside)) return;

        if (inside)
        {
            // Zoom is a MAGNIFIER: whatever is in the middle of the map stays in
            // the middle. Anchoring the zoom on the cursor instead is the usual
            // trick and it is the wrong one here — on a screen you also drive
            // with a pad's virtual cursor, it slides the thing you are looking
            // at off the edge (Sam, playtest 1).
            float notches = PadCursor.ScrollDelta;
            if (Mathf.Abs(notches) > 0.01f)
            {
                _navMapK = Mathf.Clamp(_navMapK * Mathf.Pow(1.14f, notches), 4e-4f, 1.4f);
                _navNextDrawAt = 0f;
            }
            _navMapHover = NavHitTest(local);
        }
        else if (!_navMapDragging) _navMapHover = null;

        // Hover SFX on a CHANGE of body, not on being over one - otherwise this
        // is a per-frame tone generator. Dragging the map is exempt: sweeping
        // the whole system past a parked cursor is a camera move, not a series
        // of deliberate hovers, and it would machine-gun.
        if (_navMapHover != _navMapHoverSfx)
        {
            if (_navMapHover != null && !_navMapDragging) UiSfxPlayer.Hover();
            _navMapHoverSfx = _navMapHover;
        }

        if (PadCursor.PrimaryDown && inside)
        {
            _navMapDragging = true;
            _navMapDragFrom = local;
            _navMapDragCentre = _navMapCentre;
            _navMapDragTravel = 0f;
        }
        else if (_navMapDragging && PadCursor.PrimaryHeld)
        {
            Vector2 delta = local - _navMapDragFrom;
            _navMapDragTravel = Mathf.Max(_navMapDragTravel, delta.magnitude);
            if (_navMapDragTravel > 4f)
            {
                _navMapCentre = _navMapDragCentre - delta / _navMapK;
                _navNextDrawAt = 0f;
            }
        }
        else if (_navMapDragging)
        {
            _navMapDragging = false;
            if (_navMapDragTravel <= 4f)
            {
                var hit = NavHitTest(local);
                if (hit != null) { _navSelected = hit.bodyName; NavSelectionChanged(); }
            }
        }
    }

    /// <summary>
    /// Where the pointer is, in the same space the map draws in.
    ///
    /// <b>The trap:</b> ScreenPointToLocalPointInRectangle answers relative to
    /// the rect's PIVOT, and the map rect is pivoted on its left edge while
    /// everything drawn on it is centred. Reading it raw put every click and
    /// every hover half a map-width to the right — planets could not be picked
    /// at all, and zoom-to-cursor magnified a point off in the margin. Going
    /// through the centred clip rect AND subtracting rect.center makes it
    /// immune to whatever the pivots are.
    /// </summary>
    bool NavPointerLocal(out Vector2 local, out bool inside)
    {
        local = Vector2.zero;
        inside = false;
        var rt = _navMapClip != null ? _navMapClip : _navMapRect;
        if (rt == null) return false;
        Vector2 screen = PadCursor.PointerPosition;
        inside = RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out local))
            return false;
        local -= rt.rect.center;
        return true;
    }

    CelestialBody NavHitTest(Vector2 local)
    {
        CelestialBody best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _navRows.Count; i++)
        {
            var b = _navRows[i].body;
            if (b == null) continue;
            // Generous target — these are 5-pixel dots and they are moving.
            // Nearest wins, so a fat radius cannot grab the wrong planet out of
            // a tight inner-system cluster.
            Vector2 p = NavToLocal(NavSunRel(b));
            float d = Vector2.Distance(p, local);
            if (d <= Mathf.Max(NavDotRadius(b) + 16f, 20f) && d < bestDist) { best = b; bestDist = d; }
        }
        return best;
    }
}
