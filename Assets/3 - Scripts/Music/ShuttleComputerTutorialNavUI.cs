using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ── THE DEEP-RANGE NAV MAP ──────────────────────────────────────────────────
//
// Seven solar systems on ONE CONTINUOUS ZOOM. Start on a planet filling the
// screen, wheel back until its neighbours appear, keep going until the whole
// system is a named dot among six others, then wheel into any of them and its
// planets resolve again. Signed off in prototypes/galaxy-map (open its
// README.md; the port notes at the foot of galaxy.js are this file's spec).
//
// Sam, 2026-09-16:
//   "the map should consist of 7 solar systems, each with a sun and planets
//    revolving around it … you can zoom out and see the other planets revolving
//    around the sun, if you keep zooming out youll just see the sun and then
//    name of what system that sun is, so you can zoom out and see konkebular and
//    start zooming into it and see the planets rotating around the sun and click
//    humble abode and travel to it."
//   "this should be an extension of how the gameplay scene map looks, just with
//    expanded solar systems."
//
// So it draws through NavMapGraphic — the SAME one-mesh renderer the real NAV
// map uses — with the same rings, shaded discs and palette. Only the camera and
// the catalogue are new. My first attempt at this screen was four fixed
// pictures with no zoom between them; that is what this replaces.
//
// ⚠️ THE SYSTEMS ARE DRAWN, NOT SIMULATED. Sam: "the solar systems on the
// computer just need to be ui, we dont need to add all of the actual planets to
// the scene it should just be ui on the computer so the only real planet is
// earth." Nothing here touches NBodySimulation, CelestialBody, PlanetEconomy or
// the fuel model, and nothing is ever spawned. The real NAV map is untouched.
//
// The planet the player stands on keeps its scene name ("Humble Abode" — the
// grass band and the O2 refill zone are keyed on that string) and is simply
// DISPLAYED here as EARTH. That the travel target in Konkebular-7 is also called
// Humble Abode is the beat, not a collision: one is where you are, the other is
// where the real game begins.
public partial class ShuttleComputerUI
{
    // ── catalogue ────────────────────────────────────────────────────────
    //
    // Mirrors prototypes/galaxy-map/galaxy.json. Distances are AU; system
    // positions are light years converted on load.
    //
    // ⚠️ LightYearAU IS DELIBERATELY COMPRESSED (a real one is 63,241). At true
    // scale the wheel needs ~97 turns to cross from a planet to the star field,
    // which reads as a broken control rather than a big galaxy.
    //
    // Tightened 475 -> 110 (Sam: "i find that the map feels too big, like you
    // can zoom out alot and zoom in alot, idc if its unrealistic but could you
    // make it smaller?"). Measured, not guessed - the full zoom range went from
    // 24 wheel turns end to end to about 15, and the pointless half of it (the
    // deep close-up past a planet's drawn size) from 12 turns to 5. Every view
    // still resolves in the same order. It is a map, not an ephemeris.
    const float LightYearAU = 110f;

    /// Sol's real orbits span 30 AU to Konkebular's 8, so it needed nearly four
    /// times the zoom to frame and made the map feel lopsided as well as large.
    /// Scaled to land at ~13.8 AU: the same arrangement, a comparable size.
    const float SolOrbitScale = 0.46f;

    class GalPlanet
    {
        public string name;
        public float orbitAU, sizeAU, periodDays, phase;
        /// Body radius in the same units CelestialBody.radius uses, because the
        /// real map sizes its dots from it: NavDotRadius clamps
        /// 3.4 + sqrt(radius) * 0.52 into 4.4 - 15 PIXELS. Matching that is most
        /// of why this now reads as the same map.
        public float bodyRadius;
        public Color colour;
        public bool isTarget, isHere;
        public string blurb;
        public GalSystem sys;
    }

    class GalSystem
    {
        public string id, name, blurb;
        public Vector2 posLY;
        public Color sunColour;
        public float sunAU;
        public GalPlanet[] planets;
        public Vector2 pos;          // AU, filled on build
        public float outerAU;        // widest orbit, drives the zoom LOD
    }

    static GalSystem[] _gal;

    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

    static void BuildGalaxy()
    {
        if (_gal != null) return;
        _gal = new[]
        {
            new GalSystem { id="sol", name="Sol", posLY=new Vector2(-7.4f,3.1f), sunColour=C("#ffd86b"), sunAU=0.28f,
                blurb="Home. Or it was.",
                planets = new[] {
                    new GalPlanet{ name="Mercury", bodyRadius=90f,  orbitAU=0.39f, sizeAU=0.030f, periodDays=88,    colour=C("#9c9184"), phase=0.12f },
                    new GalPlanet{ name="Venus",   bodyRadius=190f, orbitAU=0.72f, sizeAU=0.050f, periodDays=225,   colour=C("#d2a15e"), phase=0.63f },
                    new GalPlanet{ name="Earth",   bodyRadius=200f, orbitAU=1.00f, sizeAU=0.052f, periodDays=365,   colour=C("#5aa9e8"), phase=0.30f, isHere=true, blurb="You are here." },
                    new GalPlanet{ name="Mars",    bodyRadius=130f, orbitAU=1.52f, sizeAU=0.040f, periodDays=687,   colour=C("#c75b42"), phase=0.80f },
                    new GalPlanet{ name="Jupiter", bodyRadius=520f, orbitAU=5.20f, sizeAU=0.130f, periodDays=4333,  colour=C("#c7b184"), phase=0.45f },
                    new GalPlanet{ name="Saturn",  bodyRadius=440f, orbitAU=9.54f, sizeAU=0.110f, periodDays=10759, colour=C("#d8c48d"), phase=0.05f },
                    new GalPlanet{ name="Uranus",  bodyRadius=300f, orbitAU=19.2f, sizeAU=0.085f, periodDays=30687, colour=C("#8ccdd8"), phase=0.68f },
                    new GalPlanet{ name="Neptune", bodyRadius=290f, orbitAU=30.1f, sizeAU=0.083f, periodDays=60190, colour=C("#5a7fd8"), phase=0.22f },
                } },
            new GalSystem { id="konkebular7", name="Konkebular-7", posLY=new Vector2(9.2f,-4.6f), sunColour=C("#ffca5c"), sunAU=0.34f,
                blurb="Charted. Habitable. A long way from Sol.",
                planets = new[] {
                    new GalPlanet{ name="Fiery Twin",   bodyRadius=300f, orbitAU=0.62f, sizeAU=0.048f, periodDays=140,  colour=C("#e0673a"), phase=0.55f },
                    new GalPlanet{ name="Icey Twin",    bodyRadius=300f, orbitAU=0.74f, sizeAU=0.048f, periodDays=168,  colour=C("#a8dcea"), phase=0.60f },
                    new GalPlanet{ name="Humble Abode", bodyRadius=200f, orbitAU=1.15f, sizeAU=0.070f, periodDays=300,  colour=C("#72d98b"), phase=0.18f, isTarget=true, blurb="Colony site. Breathable. Green." },
                    new GalPlanet{ name="Cyclops",      bodyRadius=380f, orbitAU=2.40f, sizeAU=0.090f, periodDays=760,  colour=C("#d9a05c"), phase=0.72f },
                    new GalPlanet{ name="Scingularity", bodyRadius=60f,  orbitAU=7.80f, sizeAU=0.035f, periodDays=5200, colour=C("#5b4f78"), phase=0.40f, blurb="Something is eating the light out here." },
                } },
            new GalSystem { id="hab9", name="Hab-9", posLY=new Vector2(-15.8f,-10.4f), sunColour=C("#b8ccff"), sunAU=0.22f,
                planets = new[] {
                    new GalPlanet{ name="Hab-9 a", bodyRadius=110f, orbitAU=0.45f, sizeAU=0.034f, periodDays=96,   colour=C("#8d8478"), phase=0.20f },
                    new GalPlanet{ name="Hab-9 b", bodyRadius=240f, orbitAU=1.30f, sizeAU=0.060f, periodDays=410,  colour=C("#6fa6c9"), phase=0.71f },
                    new GalPlanet{ name="Hab-9 c", bodyRadius=400f, orbitAU=3.90f, sizeAU=0.095f, periodDays=2100, colour=C("#b6a077"), phase=0.33f },
                } },
            new GalSystem { id="ordal", name="Ordal Reach", posLY=new Vector2(1.6f,13.2f), sunColour=C("#ffb894"), sunAU=0.40f,
                planets = new[] {
                    new GalPlanet{ name="Ordal I",   bodyRadius=160f, orbitAU=0.80f, sizeAU=0.044f, periodDays=190,   colour=C("#c98f6a"), phase=0.08f },
                    new GalPlanet{ name="Ordal II",  bodyRadius=310f, orbitAU=2.10f, sizeAU=0.075f, periodDays=820,   colour=C("#e0c38f"), phase=0.49f },
                    new GalPlanet{ name="Ordal III", bodyRadius=500f, orbitAU=6.40f, sizeAU=0.120f, periodDays=4400,  colour=C("#a88f6d"), phase=0.88f },
                    new GalPlanet{ name="Ordal IV",  bodyRadius=280f, orbitAU=14.0f, sizeAU=0.070f, periodDays=14000, colour=C("#7fa8b8"), phase=0.27f },
                } },
            new GalSystem { id="vess", name="Vess", posLY=new Vector2(16.4f,9.8f), sunColour=C("#dbe8ff"), sunAU=0.19f,
                planets = new[] {
                    new GalPlanet{ name="Vess Minor", bodyRadius=80f,  orbitAU=0.33f, sizeAU=0.028f, periodDays=62,  colour=C("#9aa4ad"), phase=0.42f },
                    new GalPlanet{ name="Vess Major", bodyRadius=360f, orbitAU=1.70f, sizeAU=0.088f, periodDays=640, colour=C("#7b93b8"), phase=0.15f },
                } },
            new GalSystem { id="tharn", name="Tharn Belt", posLY=new Vector2(-1.2f,-15.6f), sunColour=C("#ffe0a8"), sunAU=0.31f,
                planets = new[] {
                    new GalPlanet{ name="Tharn I",   bodyRadius=120f, orbitAU=0.55f, sizeAU=0.036f, periodDays=120,  colour=C("#b39a72"), phase=0.64f },
                    new GalPlanet{ name="Tharn II",  bodyRadius=230f, orbitAU=1.05f, sizeAU=0.058f, periodDays=330,  colour=C("#8fb36f"), phase=0.31f },
                    new GalPlanet{ name="Tharn III", bodyRadius=430f, orbitAU=2.90f, sizeAU=0.100f, periodDays=1200, colour=C("#c2b58a"), phase=0.77f },
                    new GalPlanet{ name="Tharn IV",  bodyRadius=250f, orbitAU=8.50f, sizeAU=0.065f, periodDays=6100, colour=C("#6f8ba0"), phase=0.02f },
                } },
            new GalSystem { id="brightfold", name="Bright Fold", posLY=new Vector2(13.9f,-14.2f), sunColour=C("#c9d8ff"), sunAU=0.16f,
                planets = new[] {
                    new GalPlanet{ name="Fold a", bodyRadius=70f,  orbitAU=0.29f, sizeAU=0.026f, periodDays=50,  colour=C("#8e96a0"), phase=0.90f },
                    new GalPlanet{ name="Fold b", bodyRadius=170f, orbitAU=0.88f, sizeAU=0.046f, periodDays=240, colour=C("#a7b6c4"), phase=0.37f },
                    new GalPlanet{ name="Fold c", bodyRadius=330f, orbitAU=2.20f, sizeAU=0.072f, periodDays=900, colour=C("#7d8ea3"), phase=0.58f },
                } },
        };

        LoadKonkebularFromScene();

        foreach (var s in _gal)
        {
            s.pos = s.posLY * LightYearAU;
            if (s.id == "sol")
                foreach (var p in s.planets) { p.orbitAU *= SolOrbitScale; p.sizeAU *= SolOrbitScale; }
            // SOL ONLY. Sam asked for Sol's bunched-up inner orbits and nothing
            // else ("because it was fine and i dint ask you to change it, i just
            // asked you to change sols") - and Konkebular's rails are EXTRACTED
            // from the real scene, so evening them out was quietly overwriting
            // the one system whose numbers are supposed to be true.
            if (s.id == "sol") EvenOutOrbits(s);
            s.outerAU = 1f;
            foreach (var p in s.planets) { p.sys = s; if (p.orbitAU > s.outerAU) s.outerAU = p.orbitAU; }
        }
    }

    [System.Serializable]
    class GalJsonPlanet
    {
        public string name; public float orbitAU, bodyRadius, periodDays, phase;
        public string colour; public bool target;
    }

    [System.Serializable]
    class GalJsonFile { public GalJsonPlanet[] planets; }

    /// <summary>
    /// Replace Konkebular-7's hand-written planets with the REAL ones, extracted
    /// from the gameplay scene by Tools ▸ Solar System ▸ Extract Konkebular for
    /// the Deep-Range Map.
    ///
    /// Sam, 2026-09-16: "the biggest evidence that you arent using the actual
    /// nav app from scene 1.6.7.7.7 is the fact that theres only 4 planets in
    /// konkebular, and in the actuall gameplay scene theres many more because
    /// theres dwarf planets." There are TWELVE - the four worlds plus the eight
    /// dwarfs (Puddle, Hearth, Anvil, Ember, Slag, Shard, Pebble, Bruise) - at
    /// their real orbital radii, in their real order, in the real map's colours.
    ///
    /// Read rather than typed, so adding a planet to the scene and re-running
    /// the extractor is the whole job. If the file is missing the built-in list
    /// stands in, so the screen can never come up empty.
    /// </summary>
    static void LoadKonkebularFromScene()
    {
        GalSystem k = null;
        foreach (var s in _gal) if (s.id == "konkebular7") { k = s; break; }
        if (k == null) return;

        try
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "galaxy_konkebular.json");
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning("[TutNav] " + path + " missing \u2014 using the built-in Konkebular list. " +
                                 "Run Tools \u25b8 Solar System \u25b8 Extract Konkebular for the Deep-Range Map.");
                return;
            }
            var file = JsonUtility.FromJson<GalJsonFile>(System.IO.File.ReadAllText(path));
            if (file == null || file.planets == null || file.planets.Length == 0) return;

            var list = new List<GalPlanet>(file.planets.Length);
            foreach (var j in file.planets)
            {
                if (j == null || string.IsNullOrEmpty(j.name)) continue;
                ColorUtility.TryParseHtmlString(j.colour, out Color c);
                list.Add(new GalPlanet
                {
                    name = j.name,
                    orbitAU = Mathf.Max(0.05f, j.orbitAU),
                    bodyRadius = Mathf.Max(1f, j.bodyRadius),
                    periodDays = Mathf.Max(10f, j.periodDays),
                    phase = j.phase,
                    colour = c,
                    isTarget = j.target,
                    // sizeAU only matters for the close-up, where the dot grows
                    // past its clamped pixel size. Derived from the body radius
                    // so a big world still fills the screen when you zoom in.
                    sizeAU = Mathf.Clamp(j.bodyRadius / 4000f, 0.012f, 0.14f),
                    blurb = j.target ? "Colony site. Breathable. Green." : null,
                });
            }
            if (list.Count > 0) { SpreadCoOrbitals(list); k.planets = list.ToArray(); }
            Debug.Log("[TutNav] Konkebular-7 loaded from the gameplay scene: " + list.Count + " planets.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[TutNav] could not read the extracted Konkebular (" + e.Message + ") \u2014 using the built-in list.");
        }
    }

    /// <summary>
    /// Space a system's orbits EVENLY instead of by true distance.
    ///
    /// Sam: "for sol mars venus earth and i forget the other all have super small
    /// orbits that are bunched up real close together so it looks bad… it doesnt
    /// have to be accurate it just needs to show you visually the planets."
    ///
    /// He is right, and it is not a Sol problem — it is what every true-scale
    /// system map looks like. Sol spans 0.39 AU to 30: draw that honestly and
    /// the four inner worlds live inside the first twentieth of the radius, in a
    /// knot you cannot point at, while most of the screen is the empty gap out
    /// to the gas giants. Konkebular has the same shape, with Cyclops twice as
    /// far out as everything else.
    ///
    /// So the ORDER is kept and the spacing is thrown away: rank the rails from
    /// the inside out and lay them at even intervals. Inner is still inner and
    /// the picture is legible at a glance, which is the entire job of this
    /// screen. Co-orbital groups (the twins) share a rank, so they stay on one
    /// ring and SpreadCoOrbitals can still separate them along it.
    /// </summary>
    /// ⚠️ SOL ONLY (see the call site). Konkebular's orbits are extracted from
    /// the gameplay scene and must stay as they are.
    static void EvenOutOrbits(GalSystem sys)
    {
        if (sys.planets == null || sys.planets.Length < 2) return;

        var order = new List<GalPlanet>(sys.planets);
        order.Sort((a, b) => a.orbitAU.CompareTo(b.orbitAU));

        // Rank, sharing a rank with anything on the same rail.
        var ranks = new List<List<GalPlanet>>();
        foreach (var p in order)
        {
            if (ranks.Count > 0)
            {
                var last = ranks[ranks.Count - 1];
                float r0 = last[0].orbitAU;
                if (Mathf.Abs(p.orbitAU - r0) <= r0 * 0.02f) { last.Add(p); continue; }
            }
            ranks.Add(new List<GalPlanet> { p });
        }
        if (ranks.Count < 2) return;

        // Keep the system's overall SIZE (so Sol still reads as bigger than
        // Vess) and just redistribute inside it. The innermost ring sits at a
        // quarter of the way out, which leaves the sun and its glow room to
        // breathe without the first planet sitting on it.
        float outer = order[order.Count - 1].orbitAU;
        float inner = outer * 0.25f;
        for (int i = 0; i < ranks.Count; i++)
        {
            float r = Mathf.Lerp(inner, outer, i / (float)(ranks.Count - 1));
            foreach (var p in ranks[i]) p.orbitAU = r;
        }
    }

    /// <summary>
    /// Pull CO-ORBITAL worlds apart so they can be told apart.
    ///
    /// The twins share a rail and sit about a kilometre from each other, so
    /// their real angles are identical to three decimal places and the map drew
    /// them on top of one another — one disc, one name (Sam: "fiery twin and
    /// icey twin appear inside eachother and overlap fully"). The real NAV map
    /// has the same pair and only solves the LABEL half of it, by nudging names
    /// clear; the discs still coincide there because at that map's zoom they are
    /// a kilometre apart and it is telling the truth.
    ///
    /// Here the honest thing and the useful thing differ, so this picks useful:
    /// anything sharing an orbit is spread by a fixed angle about their mean, so
    /// they still read as a pair travelling together but each has its own dot.
    /// The separation is ANGULAR, so it holds at every zoom.
    /// </summary>
    static void SpreadCoOrbitals(List<GalPlanet> list)
    {
        const float SameOrbit = 0.02f;   // within 2 % counts as the same rail
        const float MinGap = 0.035f;     // turns: ~12.6 degrees between neighbours

        var used = new bool[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            if (used[i]) continue;
            var group = new List<int> { i };
            for (int j = i + 1; j < list.Count; j++)
            {
                if (used[j]) continue;
                float d = Mathf.Abs(list[j].orbitAU - list[i].orbitAU);
                if (d <= list[i].orbitAU * SameOrbit) { group.Add(j); used[j] = true; }
            }
            used[i] = true;
            if (group.Count < 2) continue;

            // Mean phase on the circle, so the group stays where it was rather
            // than sliding to wherever the first member happened to be.
            float sx = 0f, sy = 0f;
            foreach (int g in group)
            {
                float a = list[g].phase * Mathf.PI * 2f;
                sx += Mathf.Cos(a); sy += Mathf.Sin(a);
            }
            float mean = Mathf.Atan2(sy, sx) / (Mathf.PI * 2f);
            float span = MinGap * (group.Count - 1);
            for (int n = 0; n < group.Count; n++)
                list[group[n]].phase = Mathf.Repeat(mean - span * 0.5f + MinGap * n, 1f);
        }
    }

    // ── camera ───────────────────────────────────────────────────────────

    // Pixels per AU. Stepped MULTIPLICATIVELY: the range is about 1e5, and a
    // linear zoom crawls at one end and jumps at the other.
    const float GalPPMin = 0.16f;     // every system inside the pane, with headroom
    const float GalPPMax = 280f;      // as far in as is useful: past this a planet
                                      // has stopped gaining detail and you are
                                      // just pushing pixels around

    Vector2 _galCentre;
    float _galPP = 2200f;
    float _galDays;                   // simulated clock for the orbits
    const float GalDaysPerSecond = 24f;

    // A system stops being a place and becomes a named dot when its outermost
    // orbit is this many pixels across; the planets fade in over the next band.
    const float GalResolvePx = 26f;
    const float GalFadePx = 80f;

    RectTransform _screenRoot;      // set by Build(); the tutorial NAV's parent
    bool _galBuildFailed;          // do not retry a build that threw

    GameObject _tutNavView;
    RectTransform _galMapRect, _galClip;
    NavMapGraphic _galGfx;
    readonly List<TextMeshProUGUI> _galLabels = new List<TextMeshProUGUI>();
    TextMeshProUGUI _galScaleLabel, _galWhere, _galSelName, _galSelSys, _galSelBlurb, _galHint;
    Image _galGoBtn;
    TextMeshProUGUI _galGoLabel;

    GalPlanet _galSelected, _galHover;
    GalSystem _galHoverSys;
    bool _galDragging;
    Vector2 _galDragFrom, _galDragCentre;
    bool _galDragMoved;
    bool _tutDeparting;
    float _galHintUntil;

    // ── Eased camera move ────────────────────────────────────────────────
    //
    // Interpolating the centre and the zoom as two independent springs "zooms
    // into nothing, then when its finished zooming it moves to show the system"
    // (Sam). Position is linear in world space and zoom is exponential, so the
    // thing you clicked leaves the screen immediately and only comes back at the
    // end.
    //
    // So the target is pinned INSTEAD: every frame it is placed at a screen
    // offset that eases from wherever it was to the middle, and the centre is
    // solved from that. It cannot leave the view, because its position on screen
    // is the thing being animated.
    bool _galAnim;
    Vector2 _galAnimTarget;      // world AU the move is aimed at
    Vector2 _galAnimFromOffset;  // its screen offset when the move started
    float _galAnimPP, _galAnimPP0;
    float _galAnimU;
    const float GalAnimSeconds = 0.85f;

    static bool TutorialNav => TutorialSession.IsActive;

    /// <summary>
    /// What the player may fly to. ONE place, so the tutorial and the real game
    /// differ by a rule and nothing else.
    ///
    /// Tutorial: Humble Abode, the single charted target.
    /// Gameplay (Sam's framing, for when this is ported): anything inside
    /// Konkebular-7 and nothing outside it — the warp drive that brought you is
    /// dry, and the reactor only moves you planet to planet.
    /// </summary>
    bool GalCanTravel(GalPlanet p, out string why)
    {
        why = "";
        if (p == null) return false;
        if (p.isHere) { why = "You are already here."; return false; }
        if (TutorialNav)
        {
            if (p.isTarget) { why = "Charted colony site."; return true; }
            why = "No landing clearance — the warp is plotted for Humble Abode.";
            return false;
        }
        if (p.sys.id == "konkebular7") { why = "In range — reactor fuel, planet to planet."; return true; }
        why = "Another system. The warp drive is dry; the reactor only moves you planet to planet.";
        return false;
    }

    // ── construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Build the deep-range map the first time it is wanted, not when the
    /// computer is constructed.
    ///
    /// The computer builds itself lazily (ShuttleAutopilot calls EnsureBuilt so
    /// the cockpit monitor is live during the approach), so "are we in the
    /// tutorial scene right now" answered at construction time is a race. Asked
    /// HERE it cannot be: nothing opens NAV before the scene is running.
    /// </summary>
    void EnsureTutorialNavBuilt()
    {
        if (_tutNavView != null || _galBuildFailed) return;
        if (_screenRoot == null) { _galBuildFailed = true; Debug.LogWarning("[TutNav] no screen root - cannot build the deep-range map."); return; }
        try { BuildTutorialNav(_screenRoot); }
        catch (System.Exception e) { _galBuildFailed = true; Debug.LogError("[TutNav] build failed: " + e); }
    }

    void BuildTutorialNav(RectTransform parent)
    {
        BuildGalaxy();

        var view = MakeRect(parent, "TutNavView");
        Stretch(view, SidePad, SidePad, ContentTop, ContentBottom);
        _tutNavView = view.gameObject;

        var title = MakeText(view, "Title", "NAV — DEEP RANGE", 18, InkGhost, TextAlignmentOptions.TopLeft);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1);
        trt.sizeDelta = new Vector2(0, 26);
        trt.anchoredPosition = Vector2.zero;
        title.characterSpacing = 18;

        _galWhere = MakeText(view, "Where", "", 15, InkDim, TextAlignmentOptions.TopRight);
        var wrt = _galWhere.rectTransform;
        wrt.anchorMin = new Vector2(0, 1); wrt.anchorMax = new Vector2(1, 1);
        wrt.pivot = new Vector2(0.5f, 1);
        wrt.sizeDelta = new Vector2(0, 22);
        wrt.anchoredPosition = new Vector2(0, -2f);
        _galWhere.characterSpacing = 8;

        var pane = MakeRect(view, "GalPane");
        Stretch(pane, 0, 0, 34, 0);

        // ── map, same shape as the real NAV map ──
        var map = MakePanel(pane, "Map", Hex("03070aff"));
        var mrt = map.rectTransform;
        mrt.anchorMin = new Vector2(0, 0);
        mrt.anchorMax = new Vector2(0, 1);
        mrt.pivot = new Vector2(0, 0.5f);
        mrt.sizeDelta = new Vector2(MapPaneW, 0);
        mrt.anchoredPosition = Vector2.zero;
        _galMapRect = mrt;
        Outline(map.transform, Grid);

        var clip = MakeRect(mrt, "Clip");
        Stretch(clip, 1, 1, 1, 1);
        clip.gameObject.AddComponent<RectMask2D>();
        _galClip = clip;

        // CanvasRenderer explicitly — RectMask2D reaches the graphic's
        // canvasRenderer before RequireComponent adds one (the 2026-09-14
        // MissingComponentException on every NAV view switch).
        var gfxGo = new GameObject("GalaxyMesh", typeof(RectTransform), typeof(CanvasRenderer));
        gfxGo.transform.SetParent(clip, false);
        _galGfx = gfxGo.AddComponent<NavMapGraphic>();
        Stretch((RectTransform)gfxGo.transform, 0, 0, 0, 0);

        float bx = -10f;
        MakeMapTool(mrt, "HOME", ref bx, GalFrameHome);
        MakeMapTool(mrt, "ALL STARS", ref bx, GalFrameAll);

        // Labels ride inside the clip with the mesh, so a name just off the edge
        // cannot write itself across the destination panel.
        for (int i = 0; i < 40; i++)
        {
            var t = MakeText(clip, "GName" + i, "", 12, Ink, TextAlignmentOptions.Center);
            Box(t.rectTransform, Centre, Centre, Vector2.zero, new Vector2(190, 16));
            t.gameObject.SetActive(false);
            _galLabels.Add(t);
        }
        _galScaleLabel = MakeText(clip, "GScale", "", 11, Hex("2f6b5cff"), TextAlignmentOptions.Center);
        Box(_galScaleLabel.rectTransform, Centre, Centre, Vector2.zero, new Vector2(240, 16));
        _galScaleLabel.gameObject.SetActive(false);

        _galHint = MakeText(mrt, "GHint", "", 12, InkGhost, TextAlignmentOptions.BottomLeft);
        var hrt = _galHint.rectTransform;
        hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0);
        hrt.pivot = new Vector2(0.5f, 0);
        hrt.sizeDelta = new Vector2(0, 20);
        hrt.anchoredPosition = new Vector2(14f, 10f);

        // ── side column ──
        var side = MakeRect(pane, "GalSide");
        side.anchorMin = new Vector2(1, 0); side.anchorMax = new Vector2(1, 1);
        side.pivot = new Vector2(1, 0.5f);
        side.sizeDelta = new Vector2(NavSideW, 0);
        side.anchoredPosition = Vector2.zero;

        var dest = MakePanel(side, "Dest", Panel);
        Stretch(dest.rectTransform, 0, 0, 0, 0);
        Outline(dest.transform, Grid);

        // ⚠️ Box(rt, ANCHOR, PIVOT, pos, size) - the second argument is the
        // PIVOT, not anchorMax, and it always sets a POINT anchor. I read it as
        // a stretch helper and passed size.x = -28 ("parent width minus the
        // padding"), which is a NEGATIVE WIDTH: TMP then wraps every single
        // character onto its own line and the pane came out as a vertical
        // column of stacked letters. Widths here are ABSOLUTE.
        const float SideInner = NavSideW - 28f;

        var dh = MakeText(dest.rectTransform, "H", "DESTINATION", 11, InkGhost, TextAlignmentOptions.TopLeft);
        Box(dh.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -14), new Vector2(SideInner, 16));
        dh.characterSpacing = 20;

        _galSelName = MakeText(dest.rectTransform, "N", "—", 26, Ink, TextAlignmentOptions.TopLeft);
        Box(_galSelName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -40), new Vector2(SideInner, 34));

        _galSelSys = MakeText(dest.rectTransform, "S", "", 12, InkDim, TextAlignmentOptions.TopLeft);
        Box(_galSelSys.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -76), new Vector2(SideInner, 18));
        _galSelSys.characterSpacing = 14;

        _galSelBlurb = MakeText(dest.rectTransform, "B", "Select a world.", 14, InkDim, TextAlignmentOptions.TopLeft);
        Box(_galSelBlurb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -102), new Vector2(SideInner, 110));
        _galSelBlurb.enableWordWrapping = true;

        _galGoBtn = MakePanel(dest.rectTransform, "Go", Panel);
        var grt = _galGoBtn.rectTransform;
        grt.anchorMin = new Vector2(0, 0); grt.anchorMax = new Vector2(1, 0);
        grt.pivot = new Vector2(0.5f, 0);
        grt.offsetMin = new Vector2(14, 14); grt.offsetMax = new Vector2(-14, 14 + 62);
        Outline(_galGoBtn.transform, Hex("2a3a40ff"));
        _galGoLabel = MakeText(grt, "L", "NO CLEARANCE", 17, Hex("2a3a40ff"), TextAlignmentOptions.Center);
        Stretch(_galGoLabel.rectTransform, 0, 0, 0, 0);
        _galGoLabel.characterSpacing = 24;
        var goBtn = _galGoBtn.gameObject.AddComponent<Button>();
        goBtn.transition = Selectable.Transition.None;
        goBtn.onClick.AddListener(GalConfirmTravel);

        _tutNavView.SetActive(false);
    }

    // ── camera helpers ───────────────────────────────────────────────────

    float GalSpanPx(GalSystem s) => s.outerAU * 2f * _galPP;

    Vector2 GalPlanetPos(GalPlanet p)
    {
        float a = (p.phase + _galDays / p.periodDays) * Mathf.PI * 2f;
        return p.sys.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * p.orbitAU;
    }

    Vector2 GalToLocal(Vector2 au) => (au - _galCentre) * _galPP;

    Vector2 GalToWorldAU(Vector2 local) => local / _galPP + _galCentre;

    /// Start an eased move that keeps `targetAU` on screen the whole way.
    void GalFlyTo(Vector2 targetAU, float toPP)
    {
        _galAnimTarget = targetAU;
        _galAnimFromOffset = GalToLocal(targetAU);
        _galAnimPP0 = _galPP;
        _galAnimPP = Mathf.Clamp(toPP, GalPPMin, GalPPMax);
        _galAnimU = 0f;
        _galAnim = true;
    }

    float GalFitPP(GalSystem s)
    {
        float pane = Mathf.Min(_galClip.rect.width, _galClip.rect.height);
        if (pane < 1f) pane = 860f;                       // before the first layout
        return Mathf.Clamp(pane * 0.42f / s.outerAU, GalPPMin, GalPPMax);
    }

    /// HOME is the SYSTEM you are in, centred on its sun — NOT the planet.
    ///
    /// Sam: "when you land the shuttle, the screen changes to show the planet
    /// your on, but if you go press f on the screen to use it the planet
    /// disapears, i think because it probably moved and is rotating around the
    /// sun. so have it start with the sun in the middle showing the solar
    /// system, so that on the screen it matches then when you open the screen it
    /// looks the same."
    ///
    /// Exactly right: a camera parked on a moving planet is a camera the planet
    /// walks out of. The sun does not move, so this framing is stable for as
    /// long as the screen is up.
    void GalFrameHome() => GalFlyTo(_gal[0].pos, GalFitPP(_gal[0]));

    /// Frame every system, computed from where they actually are rather than a
    /// magic multiple of the zoom floor — change the catalogue and this still
    /// fits.
    void GalFrameAll()
    {
        Vector2 lo = _gal[0].pos, hi = _gal[0].pos;
        foreach (var s in _gal)
        {
            lo = Vector2.Min(lo, s.pos);
            hi = Vector2.Max(hi, s.pos);
        }
        Vector2 centre = (lo + hi) * 0.5f;
        float spread = Mathf.Max(hi.x - lo.x, hi.y - lo.y);
        float pane = Mathf.Min(_galClip.rect.width, _galClip.rect.height);
        if (pane < 1f) pane = 860f;
        GalFlyTo(centre, Mathf.Clamp(pane * 0.82f / Mathf.Max(1f, spread), GalPPMin, GalPPMax));
    }

    void GalFrameSystem(GalSystem s) => GalFlyTo(s.pos, GalFitPP(s));

    // ── per-frame ────────────────────────────────────────────────────────

    void GalDrive()
    {
        // ── THE HANDOVER LIVES HERE, NOT IN ShowNav ──────────────────────
        //
        // DoOpen deliberately RESUMES the view you left ("the world monitor
        // shows a freeze-frame of that screen, so coming back anywhere else
        // made the mirror a liar"). The tutorial's LANDING happens on the real
        // NAV view, so when the player sits back down after touchdown that view
        // is simply re-activated and ShowNav is never called again - which left
        // the gate I put in ShowNav dead, and the box showing the real map of a
        // scene that holds two pinned bodies: "the planet your on and the sun",
        // not orbiting. Exactly what Sam reported, twice.
        //
        // Checked every frame instead. Cheap: two null tests and a bool unless
        // the swap is actually due.
        if (TutorialNav && _navView != null && _navView.activeSelf)
        {
            var handoverPilot = ShuttleAutopilot.Instance;
            // ⚠️ NOT FlightActive - that is `_phase != Parked && _phase != Countdown`,
            // so it is FALSE for the whole ten-second countdown. Using it here
            // meant the deep-range map kept shoving itself back over the
            // COUNTDOWN screen the instant TRAVEL was pressed: "normally it
            // should go to the countdown screen, but it doesnt and it just stays
            // on the same screen with the map" (Sam).
            //
            // The question is "has a launch been asked for", and PARKED is the
            // only phase where the answer is no.
            bool stillFlying = handoverPilot != null &&
                               handoverPilot.CurrentPhase != ShuttleAutopilot.Phase.Parked;
            if (!stillFlying)
            {
                EnsureTutorialNavBuilt();
                if (_tutNavView != null)
                {
                    if (!_tutNavLogged) { _tutNavLogged = true; Debug.Log("[TutNav] deep-range map taking over from the landing feed."); }
                    ShowTutorialNav();
                }
            }
        }

        // The reverse handover. Once a launch is under way the real NAV view is
        // the screen - it draws the countdown, then the en-route feed - so the
        // deep-range map gets out of the way wherever the launch came from.
        if (TutorialNav && _tutNavView != null && _tutNavView.activeSelf)
        {
            var launchPilot = ShuttleAutopilot.Instance;
            if (launchPilot != null && launchPilot.CurrentPhase != ShuttleAutopilot.Phase.Parked)
            {
                ShowNav();
                return;
            }
        }

        if (_tutNavView == null || !_tutNavView.activeSelf) return;
        if (_galGfx == null || _galClip == null) return;

        float dt = Time.unscaledDeltaTime;
        _galDays += dt * GalDaysPerSecond;

        if (_galAnim)
        {
            _galAnimU = Mathf.Clamp01(_galAnimU + dt / GalAnimSeconds);
            float e = _galAnimU * _galAnimU * (3f - 2f * _galAnimU);     // smoothstep

            // Zoom in LOG space: lerping it linearly across four orders of
            // magnitude spends the whole move at one end of the range.
            _galPP = Mathf.Exp(Mathf.Lerp(Mathf.Log(_galAnimPP0), Mathf.Log(_galAnimPP), e));

            // Then solve the centre so the target sits at an offset easing to
            // the middle. This is what keeps it in view while the zoom changes.
            Vector2 offset = Vector2.Lerp(_galAnimFromOffset, Vector2.zero, e);
            _galCentre = _galAnimTarget - offset / _galPP;

            if (_galAnimU >= 1f) _galAnim = false;
        }

        GalHandleInput();
        GalDraw();
        GalSyncChrome();
    }

    void GalHandleInput()
    {
        if (_tutDeparting) return;
        bool inside = GalPointerLocal(out Vector2 local);

        // ── ZOOM ABOUT THE MIDDLE OF THE SCREEN ──────────────────────────
        //
        // Zoom-about-the-cursor is the desktop-map convention and it is wrong
        // here (Sam: "i find it hard to zoom in and out accuratly with the map,
        // and its because the zoom doesnt zoom to the middle of the screen like
        // it should, it zooms to wherever you last clicked your cursor"). On a
        // mouse-driven web map the pointer is the thing you aim with; on this
        // screen the pointer is also how you SELECT, so it is usually parked on
        // whatever you last clicked and the view lurches toward it.
        //
        // Zooming about the centre makes the wheel a pure magnifier: what is in
        // the middle stays in the middle, and you aim by dragging. Clicking a
        // sun still flies you to it.
        float scroll = Input.mouseScrollDelta.y;
        if (inside && Mathf.Abs(scroll) > 0.01f)
        {
            _galPP = Mathf.Clamp(_galPP * Mathf.Exp(scroll * 0.5f), GalPPMin, GalPPMax);
            _galAnim = false;
        }

        if (Input.GetMouseButtonDown(0) && inside)
        {
            _galDragging = true; _galDragMoved = false;
            _galDragFrom = local; _galDragCentre = _galCentre;
        }
        if (_galDragging && Input.GetMouseButton(0))
        {
            Vector2 d = local - _galDragFrom;
            if (d.sqrMagnitude > 9f) _galDragMoved = true;
            _galCentre = _galDragCentre - d / _galPP;
            _galAnim = false;
        }
        if (Input.GetMouseButtonUp(0))
        {
            bool wasDrag = _galDragMoved;
            _galDragging = false;
            if (!wasDrag && inside) GalClick(local);
        }

        // ── TRAVEL, HIT-TESTED BY HAND ───────────────────────────────────
        //
        // The UGUI Button on this panel has now failed twice for Sam, and each
        // failure cost a full playthrough. So it no longer matters whether the
        // Button works: the press is ALSO tested directly against the button's
        // rect, using PadCursor.PointerPosition — the same pointer path that
        // picks planets on this very screen and demonstrably does work.
        //
        // Both routes call GalConfirmTravel, which sets _tutDeparting on its
        // first success, so a frame where both fire only departs once.
        if (Input.GetMouseButtonUp(0) && _galGoBtn != null && !_tutDeparting)
        {
            var brt = _galGoBtn.rectTransform;
            if (RectTransformUtility.RectangleContainsScreenPoint(brt, PadCursor.PointerPosition, null))
                GalConfirmTravel();
        }

        _galHover = inside ? GalPickPlanet(local) : null;
        // A system is only "hoverable" when you cannot already see into it —
        // once its planets are drawn, the planets are what you are pointing at.
        _galHoverSys = null;
        if (inside && _galHover == null)
        {
            var hs = GalPickSun(local);
            if (hs != null && GalSpanPx(hs) < GalResolvePx) _galHoverSys = hs;
        }
    }

    void GalClick(Vector2 local)
    {
        var p = GalPickPlanet(local);
        if (p != null) { _galSelected = p; return; }
        var s = GalPickSun(local);
        if (s != null) { _galSelected = null; GalFrameSystem(s); }
        else _galSelected = null;
    }

    GalPlanet GalPickPlanet(Vector2 local)
    {
        GalPlanet best = null;
        float bestD = 24f * 24f;
        foreach (var s in _gal)
        {
            if (GalSpanPx(s) < GalResolvePx) continue;
            foreach (var p in s.planets)
            {
                float d = (GalToLocal(GalPlanetPos(p)) - local).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
        }
        return best;
    }

    GalSystem GalPickSun(Vector2 local)
    {
        GalSystem best = null;
        float bestD = 24f * 24f;
        foreach (var s in _gal)
        {
            float d = (GalToLocal(s.pos) - local).sqrMagnitude;
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    bool GalPointerLocal(out Vector2 local)
    {
        local = Vector2.zero;
        var rt = _galClip;
        if (rt == null) return false;
        Vector2 screen = PadCursor.PointerPosition;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out local))
            return false;
        // ⚠️ PIVOT-RELATIVE. ScreenPointToLocalPointInRectangle returns a point
        // in the rect's own space, which is only centred if the pivot is — this
        // subtraction is what the real NAV map does and what clicking and
        // zooming both depend on.
        local -= rt.rect.center;
        return inside;
    }

    // ── drawing ──────────────────────────────────────────────────────────

    /// <summary>
    /// The SAME treatment ShuttleComputerNavMapUI gives the real solar system,
    /// applied to every resolved system on the map. Sam: "this should be an
    /// extension of how the gameplay scene map looks" — so these are not
    /// approximations of that screen, they are its numbers:
    ///
    ///   sun      Disc(74 px, #ffd67d→transparent) + Disc(9 px, #ffd98a)
    ///   orbits   Ring(Grid, 1 px); the selected/hovered rail lights to #1d5a68
    ///   planets  radius = clamp(3.4 + sqrt(bodyRadius) * 0.52, 4.4, 15) PIXELS,
    ///            core = swatch, rim = swatch * 0.42, gradient offset toward the
    ///            sun by r * 0.45 — "lit from the sun, so the map reads as a
    ///            place and not a chart"
    ///   rings    here = dashed mint; reachable = mint at half alpha;
    ///            selected = Accent 2 px; hover = Accent at 0.45
    ///   labels   ABOVE the dot, upper case, 12 px
    ///
    /// The ONE departure: the sun and the planet dots are scaled DOWN by the
    /// resolve fade as a system shrinks toward a star. The real map never has to
    /// do that because it only ever draws one system; here a fixed 74 px sun
    /// would swallow the galaxy view.
    /// </summary>
    // ── starfield ────────────────────────────────────────────────────────
    //
    // Sam: "can you add stars like we added to our skybox? twinklying and
    // shining stars to give it a better background than just straight black?
    // make them small so they dont look the same size as the suns when you zoom
    // out or else that would be weird."
    //
    // So: deliberately TINY (0.7 - 1.7 px against a sun's 2.2 px floor, which is
    // the smallest a real system ever draws), fixed to the pane rather than the
    // world, and rolled ONCE - re-rolling every frame would allocate a Random
    // and a few hundred floats a second to draw the same dots. Only the
    // brightness moves: each star has its own period and offset, so the field
    // shimmers instead of pulsing in unison.
    Vector4[] _galStars;     // x, y (fractions of the half-extent), size, base alpha
    Vector2[] _galTwinkle;   // speed, phase

    void GalDrawStars(Vector2 half)
    {
        if (_galStars == null)
        {
            var rng = new System.Random(20260916);
            _galStars = new Vector4[190];
            _galTwinkle = new Vector2[190];
            for (int i = 0; i < _galStars.Length; i++)
            {
                _galStars[i] = new Vector4(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    // A handful of brighter ones so the field has structure.
                    rng.NextDouble() < 0.10 ? 1.7f : (rng.NextDouble() < 0.45 ? 1.1f : 0.7f),
                    0.10f + (float)rng.NextDouble() * 0.34f);
                _galTwinkle[i] = new Vector2(
                    0.35f + (float)rng.NextDouble() * 1.5f,      // speed
                    (float)rng.NextDouble() * 6.2832f);          // phase
            }
        }

        float t = Time.unscaledTime;
        for (int i = 0; i < _galStars.Length; i++)
        {
            var st = _galStars[i];
            var tw = _galTwinkle[i];
            // Sin twice at different rates: a single sine reads as a metronome.
            float shimmer = 0.62f + 0.38f * Mathf.Sin(t * tw.x + tw.y)
                                  * Mathf.Cos(t * tw.x * 0.37f + tw.y * 1.7f);
            var c = new Color(0.86f, 0.93f, 1f, st.w * shimmer);
            _galGfx.Dot(new Vector2(st.x * half.x, st.y * half.y), st.z, c);
        }
    }

    void GalDraw()
    {
        _galGfx.Clear();
        int label = 0;
        Vector2 half = _galClip.rect.size * 0.5f;
        if (half.x < 1f) half = new Vector2(515f, 430f);   // before the first layout

        GalDrawStars(half);

        // Pass 1: orbit rails, behind everything.
        foreach (var s in _gal)
        {
            float span = GalSpanPx(s);
            if (span < GalResolvePx) continue;
            Vector2 c = GalToLocal(s.pos);
            if (Mathf.Abs(c.x) > half.x + span || Mathf.Abs(c.y) > half.y + span) continue;

            float fade = Mathf.Clamp01((span - GalResolvePx) / GalFadePx);
            foreach (var p in s.planets)
            {
                float r = p.orbitAU * _galPP;
                if (r < 2f) continue;
                bool lit = p == _galSelected || p == _galHover;
                Color rail = lit ? Hex("1d5a68ff") : Grid;
                rail.a *= fade;
                _galGfx.Ring(c, r, rail, lit ? 1.4f : 1f);
            }
        }

        // Pass 2: suns, then planets.
        foreach (var s in _gal)
        {
            float span = GalSpanPx(s);
            Vector2 c = GalToLocal(s.pos);
            bool onScreen = Mathf.Abs(c.x) < half.x + 200f && Mathf.Abs(c.y) < half.y + 200f;
            float fade = Mathf.Clamp01((span - GalResolvePx) / GalFadePx);

            if (onScreen)
            {
                // The gameplay map's sun, shrunk toward a star as the system
                // stops resolving.
                float glowR = Mathf.Lerp(7f, 74f, fade);
                float coreR = Mathf.Lerp(2.2f, 9f, fade);
                _galGfx.Disc(c, glowR, new Color(1f, 0.84f, 0.49f, 0.55f),
                             new Color(1f, 0.59f, 0.24f, 0f), Vector2.zero);
                _galGfx.Disc(c, coreR, fade > 0.5f ? Hex("ffd98aff") : s.sunColour);

                // "you can click this to go there" — a dashed ring well clear of
                // the star, so at the zoom where every system is a 2 px dot with
                // a name under it there is still something plainly pointable.
                // Dashed rather than solid because a solid ring at this size
                // reads as another body.
                if (s == _galHoverSys)
                    _galGfx.DashedRing(c, coreR + 13f, Ink, 1.4f, 5f, 4f);
            }

            // The system NAME fades out exactly as its planets fade in, so there
            // is always precisely one label saying where you are.
            float nameAlpha = 1f - Mathf.Clamp01((span - GalResolvePx) / (GalFadePx * 1.75f));
            if (onScreen && nameAlpha > 0.02f && label < _galLabels.Count)
            {
                var t = _galLabels[label++];
                t.gameObject.SetActive(true);
                t.text = s.name.ToUpperInvariant();
                t.fontSize = 12;
                t.color = new Color(Ink.r, Ink.g, Ink.b, 0.35f + 0.55f * nameAlpha);
                t.rectTransform.anchoredPosition = c + new Vector2(0f, -(Mathf.Lerp(2.2f, 9f, fade) + 14f));
            }

            if (span < GalResolvePx) continue;

            foreach (var p in s.planets)
            {
                Vector2 pc = GalToLocal(GalPlanetPos(p));
                if (Mathf.Abs(pc.x) > half.x + 60f || Mathf.Abs(pc.y) > half.y + 60f) continue;

                float r = Mathf.Max(GalDotRadius(p) * Mathf.Lerp(0.45f, 1f, fade),
                                    p.sizeAU * _galPP);

                Color swatch = p.colour;
                Color edge = swatch * 0.42f; edge.a = fade;
                Color core = swatch; core.a = fade;
                Vector2 toSun = c - pc;
                toSun = toSun.sqrMagnitude > 0.01f ? toSun.normalized * (r * 0.45f) : Vector2.zero;
                _galGfx.Disc(pc, r, core, edge, toSun);

                bool ringed = p == _galSelected || p == _galHover;
                if (p.isHere) _galGfx.DashedRing(pc, r + 7f, new Color(Ink.r, Ink.g, Ink.b, fade), 1.6f, 5f, 4f);
                else if (GalCanTravel(p, out _)) _galGfx.Ring(pc, r + 4.5f, new Color(Ink.r, Ink.g, Ink.b, 0.5f * fade), 1.1f);
                if (p == _galSelected) _galGfx.Ring(pc, r + 11f, Accent, 2f);
                else if (p == _galHover) _galGfx.Ring(pc, r + 11f, new Color(Accent.r, Accent.g, Accent.b, 0.45f), 1.2f);

                // Names ABOVE the dot, like the real map.
                if (fade > 0.35f && p.orbitAU * _galPP > 34f && label < _galLabels.Count)
                {
                    var t = _galLabels[label++];
                    t.gameObject.SetActive(true);
                    t.text = p.name.ToUpperInvariant();
                    t.fontSize = 12;
                    t.color = new Color(Ink.r, Ink.g, Ink.b, fade);
                    t.rectTransform.anchoredPosition = pc + new Vector2(0f, r + (ringed ? 17f : 9f));
                }
            }
        }

        for (int i = label; i < _galLabels.Count; i++)
            if (_galLabels[i].gameObject.activeSelf) _galLabels[i].gameObject.SetActive(false);

        GalDrawScaleBar(half);
        _galGfx.Commit();
    }

    /// ShuttleComputerNavMapUI.NavDotRadius, verbatim — a CLAMPED PIXEL size
    /// from the body's radius, NOT scaled by zoom. That is what keeps a planet
    /// legible at every zoom and is most of the real map's character.
    ///
    /// The one addition: when you zoom right in past that clamp the disc grows
    /// with the zoom, so the tutorial can open with Earth filling the screen.
    static float GalDotRadius(GalPlanet p)
    {
        float clamped = Mathf.Clamp(3.4f + Mathf.Sqrt(Mathf.Max(1f, p.bodyRadius)) * 0.52f, 4.4f, 15f);
        return clamped;
    }

    void GalDrawScaleBar(Vector2 half)
    {
        // Without a ruler every zoom looks the same. The bar is a round number
        // of AU, or of light years once AU stops being meaningful.
        float au = 150f / _galPP;
        bool inLY = au > 400f;
        float v = inLY ? au / LightYearAU : au;
        float pow = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(v, 1e-6f))));
        float nice = pow;
        foreach (float m in new[] { 1f, 2f, 5f, 10f })
            if (Mathf.Abs(m * pow - v) < Mathf.Abs(nice - v)) nice = m * pow;
        float px = (inLY ? nice * LightYearAU : nice) * _galPP;

        // Bottom RIGHT (Sam). The bottom-left corner is where the hint line
        // sits, and the two were reading as one cluttered block.
        float x0 = half.x - 18f - px, y0 = -half.y + 20f;
        var c = Hex("2f6b5cff");
        _galGfx.Line(new Vector2(x0, y0), new Vector2(x0 + px, y0), c, 1f);
        _galGfx.Line(new Vector2(x0, y0), new Vector2(x0, y0 + 5f), c, 1f);
        _galGfx.Line(new Vector2(x0 + px, y0), new Vector2(x0 + px, y0 + 5f), c, 1f);

        _galScaleLabel.gameObject.SetActive(true);
        _galScaleLabel.text = (nice >= 1f ? nice.ToString("0.##") : nice.ToString("0.###"))
                            + (inLY ? " ly" : " AU");
        _galScaleLabel.rectTransform.anchoredPosition = new Vector2(x0 + px * 0.5f, y0 + 14f);
    }

    // ── chrome ───────────────────────────────────────────────────────────

    void GalSyncChrome()
    {
        // Which system is nearest the middle, and can we resolve it?
        GalSystem near = null; float nd = float.MaxValue;
        foreach (var s in _gal)
        {
            float d = (s.pos - _galCentre).sqrMagnitude;
            if (d < nd) { nd = d; near = s; }
        }
        if (_galWhere != null && near != null)
            _galWhere.text = GalSpanPx(near) < GalResolvePx
                ? "LOCAL STARS · " + _gal.Length + " SYSTEMS"
                : near.name.ToUpperInvariant() + " SYSTEM";

        bool ok = GalCanTravel(_galSelected, out string why);
        if (_galSelName != null) _galSelName.text = _galSelected != null ? _galSelected.name.ToUpperInvariant() : "—";
        if (_galSelSys != null) _galSelSys.text = _galSelected != null ? _galSelected.sys.name.ToUpperInvariant() : "";
        if (_galSelBlurb != null)
            _galSelBlurb.text = _galSelected == null ? "Select a world."
                              : !string.IsNullOrEmpty(_galSelected.blurb) && ok ? _galSelected.blurb
                              : why;

        if (_galGoLabel != null)
        {
            _galGoLabel.text = ok ? "TRAVEL" : "NO CLEARANCE";
            _galGoLabel.color = ok ? Accent : Hex("2a3a40ff");
        }
        if (_galGoBtn != null) _galGoBtn.color = ok ? PanelHi : Panel;

        if (_galHint != null && Time.unscaledTime >= _galHintUntil)
            _galHint.text = "WHEEL  zoom    DRAG  pan    CLICK  a planet to select, a sun to fly to it";
    }

    void GalFlash(string msg)
    {
        if (_galHint != null) _galHint.text = msg;
        _galHintUntil = Time.unscaledTime + 3f;
    }

    // ── departure ────────────────────────────────────────────────────────

    void GalConfirmTravel()
    {
        if (_tutDeparting) return;

        var tank = FindObjectOfType<ShuttleFuel>();
        bool can = GalCanTravel(_galSelected, out string why);
        // One line, every press. If this ever goes quiet again the button is not
        // being pressed; if it speaks and refuses, it says which test failed.
        Debug.Log($"[TutNav] TRAVEL pressed. selected={(_galSelected != null ? _galSelected.name : "<none>")} " +
                  $"canTravel={can} ({why}) tank={(tank != null ? (tank.FuelPercent * 100f).ToString("0") + "%" : "<none>")}");

        if (!can)
        {
            GalFlash(why);
            // Put the refusal where the player is already looking - right above
            // the button - not only in the hint line at the far corner.
            if (_galSelBlurb != null) _galSelBlurb.text = why;
            return;
        }

        if (tank != null && tank.FuelPercent <= 0.001f)
        {
            const string dry = "Tank dry. Feed the reactor fuel crystals first.";
            GalFlash(dry);
            if (_galSelBlurb != null) _galSelBlurb.text = dry;
            return;
        }

        OrientationObjectives.Complete(OrientationObjectives.Objective.SelectDestination);
        _tutDeparting = true;
        GalFlash("Course locked. Stand by.");
        _galHintUntil = float.MaxValue;
        StartCoroutine(TutDepartureSequence(_galSelected.name));
    }

    /// Blast off, fade, and hand the player back to the main menu — the end of
    /// the tutorial. There is nowhere to actually fly to: Konkebular-7 is a
    /// drawing, so this is a send-off, not a journey.
    /// <summary>
    /// The end of the tutorial: a REAL launch, then the lights go out.
    ///
    /// ⚠️ THIS USES THE GAME'S OWN LAUNCH. The first version flew the shuttle by
    /// hand — set isInDialogue, pushed transform.position up, dragged the player
    /// along by rb.position — and every one of those was wrong in a way Sam then
    /// had to play through: isInDialogue is what raises the LETTERBOX BARS and
    /// takes movement away, and moving the hull and the body as two separate
    /// things had the player "glitching in the floor of the shuttle" because
    /// nothing was cageing them to the frame.
    ///
    /// ShuttleAutopilot.RequestTravel does all of it properly: the ten-second
    /// countdown, ShuttleExitDoor.CloseForFlight, ShuttleRiderFrame.CaptureRiders
    /// (which is what actually carries the player), the engine FX, the camera.
    /// The target is the planet we are ALREADY ON — same-planet relocation,
    /// which RequestTravel explicitly allows ("target == _body is ALLOWED …
    /// Same crew/door/capture flow") — so no invented destination is needed and
    /// the player can walk around the cabin the whole time, exactly as they can
    /// on a normal hop.
    ///
    /// The tutorial only adds the ending: three seconds after the door shuts,
    /// fade out and hand over to the main menu. The flight never has to finish.
    /// </summary>
    IEnumerator TutDepartureSequence(string destination)
    {
        Debug.Log("[Tutorial] Departing for " + destination + " - ending the tutorial.");

        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null)
        {
            Debug.LogWarning("[Tutorial] no ShuttleAutopilot - ending without the launch.");
        }
        else if (!pilot.RequestTravel(pilot.CurrentBody))
        {
            // Refused (not parked, or the tank could not cover the hop). Say so
            // and give the screen back rather than stranding the player in a
            // departure that never happens.
            Debug.LogWarning("[Tutorial] RequestTravel refused - is the shuttle parked and fuelled?");
            GalFlash("Launch refused. Check the tank.");
            _tutDeparting = false;
            yield break;
        }

        // Hand the screen to the REAL NAV view and then LEAVE IT ALONE.
        //
        // OnNavTravelClicked - the game's own TRAVEL handler - calls
        // RequestTravel and does nothing else. No closing the computer, no
        // touching the player's input flags. The countdown appears because
        // NavDrive is already driving that view, and the player leaves the
        // console with F when they feel like it, exactly as on any other hop.
        //
        // Everything I added on top of that was a bug: Close() "kicked me out of
        // the computer", and the flag-stomping papered over a freeze that my own
        // Close() had caused by yanking the console out from under an active
        // interaction.
        ShowNav();

        // Drop the UI selection my own button just created.
        //
        // TutorialGate.MovementInputSuppressed counts UISelectionActive() - "the
        // EventSystem has an interactable Selectable focused" - as a reason to
        // take movement away, and clicking a UGUI Button leaves it selected. So
        // pressing TRAVEL can hold the player still afterwards even though the
        // computer has already let go of them. Clearing it is what any UI does
        // once its action is finished, and it can only affect a selection this
        // screen made.
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);

        // Countdown (10 s) runs, then Liftoff closes the door and cages the
        // riders. Waiting on the PHASE rather than a timer means the fade is
        // pinned to the door actually shutting, whatever the countdown is
        // retuned to later.
        yield return new WaitUntil(() => pilot == null ||
                                         pilot.CurrentPhase != ShuttleAutopilot.Phase.Parked &&
                                         pilot.CurrentPhase != ShuttleAutopilot.Phase.Countdown);

        Debug.Log("[Tutorial] door closed, lifting off - fading out in 3 s.");
        yield return new WaitForSeconds(3f);

        var fade = TutorialDepartureFade.Create();
        const float FadeSeconds = 2f;
        float t = 0f;
        while (t < FadeSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / FadeSeconds);
            fade.SetAlpha(k * k * (3f - 2f * k));    // smoothstep in
            yield return null;
        }
        fade.SetAlpha(1f);

        // The fade owns the rest: it survives the load and fades back IN on the
        // menu, then destroys itself. Leaving it up was why the main menu "stayed
        // black" while its buttons were still making sounds under it.
        fade.ArmFadeInAfterLoad(0.8f);

        // Leave nothing set for the next run - these are statics and the main
        // menu is a fresh start.
        PlayerController.isInDialogue = false;
        IntroSequenceController.ShuttleWakeActive = false;
        PauseState.Exit();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        UiSfxPlayer.StopPauseAmbience();
        SceneManager.LoadScene("MainMenu");
    }

    // ── entry ────────────────────────────────────────────────────────────

    void ShowTutorialNav()
    {
        _homeView.SetActive(false);
        _traxView.SetActive(false);
        if (_projectsView != null) _projectsView.SetActive(false);
        if (_navView != null) _navView.SetActive(false);
        if (_inst != null) _inst.Stop();
        SyncPlayButton();
        _tutNavView.SetActive(true);
        _galSelected = null;
        // Snap, do not fly: the screen should already be showing the system when
        // the player sits down, matching the view the landing feed left behind.
        _galPP = GalFitPP(_gal[0]);
        _galCentre = _gal[0].pos;
        _galAnim = false;
    }
}
