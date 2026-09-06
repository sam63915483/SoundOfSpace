using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The v2 map's screen-space diagram + legend, built procedurally once and
/// reused. Per body: a leader line from the body's screen position and a name
/// tag fanned outward from the sun (moons fan from their planet), so the whole
/// thing reads like a labelled diagram; nothing is drawn OVER the body itself
/// (Sam: no discs hiding the planets). The name is the button: click = match
/// velocity; click empty space = unmatch. A YOU tag marks the frozen
/// astronaut. The legend rows fly you to the body and match in one click. Right-hand legend lists STAR / PLANETS / DWARF PLANETS / MOONS
/// (styled like the old map: GalaxyHudKit sprites + colours), with ORBIT LINES,
/// NAMES and RECENTER buttons and a control hint. Everything sits under one
/// CanvasGroup so SolarMap can fade it in after the flight and out before the
/// return.
/// </summary>
[DefaultExecutionOrder(216)]   // after SolarMap (210) — reads the final camera pose
public class SolarMapOverlay : MonoBehaviour
{
    const float LegendWidth = 240f;
    const float RowH = 26f;

    class Marker
    {
        public CelestialBody body;
        public bool isMoon;
        public Color color;
        public RectTransform root, lineRT, labelRT;
        public Image line;
        public Text label;
        public Button button;
    }

    class Row { public CelestialBody body; public Image bg; public Text label; }

    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _canvasRT, _markerLayer;
    readonly List<Marker> _markers = new List<Marker>();
    readonly List<Row> _rows = new List<Row>();
    Marker _player;
    Transform _playerT;
    Text _orbitsLabel, _namesLabel, _cursorHint;
    SolarMap _map;
    CelestialBody _sun;
    CelestialBody _followed;
    bool _built, _namesVisible = true, _interactive;
    float _alpha;

    static readonly Color PlanetColor = new Color(0.36f, 0.85f, 1f, 1f);
    static readonly Color DwarfColor  = new Color(0.55f, 0.95f, 0.85f, 1f);
    static readonly Color MoonColor   = new Color(1f, 0.78f, 0.45f, 1f);
    static readonly Color SunColor    = new Color(1f, 0.9f, 0.55f, 1f);
    static readonly Color YouColor    = new Color(0.45f, 1f, 0.55f, 1f);

    // ── public API ──────────────────────────────────────────────────────────
    public void Bind(SolarMap map, CelestialBody[] bodies, Transform player)
    {
        _map = map;
        _playerT = player;
        if (!_built) BuildCanvas();
        // Rebuild the body-dependent parts whenever the body set changes.
        int live = 0;
        foreach (var b in bodies) if (b != null) live++;
        bool stale = false;
        foreach (var m in _markers) if (m.body == null) { stale = true; break; }   // scene reloaded under us
        if (stale || _markers.Count != live) BuildBodies(bodies);
        SetFollowed(null);
    }

    public void SetAlpha(float a)
    {
        _alpha = Mathf.Clamp01(a);
        if (_group != null)
        {
            _group.alpha = _alpha;
            bool on = _interactive && _alpha > 0.5f;
            _group.interactable = on;
            _group.blocksRaycasts = on;
        }
    }

    public void SetInteractive(bool on) { _interactive = on; SetAlpha(_alpha); }
    public void SetNamesVisible(bool on) { _namesVisible = on; RefreshToggleLabels(); }
    public void SetCursorHint(bool locked) { if (_cursorHint != null) _cursorHint.text = locked ? "G  ·  unlock cursor (mouse look on)" : "G  ·  lock cursor for mouse look"; }

    public void SetFollowed(CelestialBody body)
    {
        _followed = body;
        foreach (var r in _rows)
        {
            bool sel = r.body == body;
            r.bg.color = sel ? new Color(GalaxyHudKit.BorderHot.r, GalaxyHudKit.BorderHot.g, GalaxyHudKit.BorderHot.b, 0.9f) : new Color(1f, 1f, 1f, 0.9f);
            r.label.color = sel ? Color.white : GalaxyHudKit.LabelColor;
        }
    }

    // ── per-frame layout ────────────────────────────────────────────────────
    void LateUpdate()
    {
        if (_map == null || _alpha <= 0.001f) return;
        var cam = _map.ViewCamera;
        if (cam == null) return;
        RefreshToggleLabels();

        float scale = _canvas.scaleFactor;
        float pxPerUnitAt1 = cam.pixelHeight / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        Vector2 sunLocal = Vector2.zero;
        bool sunOnScreen = _sun != null && ScreenToLocal(cam, _sun.transform.position, out sunLocal);

        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            if (m.body == null) { m.root.gameObject.SetActive(false); continue; }
            if (!ScreenToLocal(cam, m.body.transform.position, out Vector2 local)) { m.root.gameObject.SetActive(false); continue; }
            m.root.gameObject.SetActive(true);
            m.root.anchoredPosition = local;

            // Leader line starts just outside the body's rendered disc so it never
            // covers the planet; from far away that is a few pixels.
            float dist = Vector3.Distance(cam.transform.position, m.body.transform.position);
            float radiusPx = m.body.radius * pxPerUnitAt1 / Mathf.Max(dist, 1f) / scale;
            bool sel = m.body == _followed;

            // Fan direction: away from the sun (moons: away from their planet).
            Vector2 refLocal = sunLocal;
            if (m.isMoon && m.body.coOrbitLeader != null && ScreenToLocal(cam, m.body.coOrbitLeader.transform.position, out Vector2 leaderLocal)) refLocal = leaderLocal;
            Vector2 dir = (sunOnScreen || m.isMoon) ? local - refLocal : Vector2.up;
            if (dir.sqrMagnitude < 4f) dir = Vector2.up; else dir.Normalize();
            if (m.body.bodyType == CelestialBody.BodyType.Sun) dir = new Vector2(0.7f, 0.7f);
            else if (m.body.isStaticAttractor) dir = new Vector2(-0.7f, -0.7f);   // the black hole sits behind the sun on screen

            bool showName = _namesVisible;
            m.lineRT.gameObject.SetActive(showName);
            m.labelRT.gameObject.SetActive(showName);
            if (showName)
            {
                float start = radiusPx + 4f;
                float len = m.isMoon ? 22f : 36f;
                m.lineRT.anchoredPosition = dir * (start + len * 0.5f);
                m.lineRT.sizeDelta = new Vector2(len, 1.5f);
                m.lineRT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                m.line.color = new Color(m.color.r, m.color.g, m.color.b, 0.7f);
                m.labelRT.anchoredPosition = dir * (start + len + 3f);
                m.labelRT.pivot = new Vector2(dir.x >= 0f ? 0f : 1f, 0.5f);
                m.label.alignment = dir.x >= 0f ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                m.label.color = sel ? Color.white : m.color;
            }
        }

        // YOU marker at the astronaut.
        if (_player != null)
        {
            Vector2 pl = Vector2.zero;
            bool ok = _playerT != null && ScreenToLocal(cam, _playerT.position, out pl);
            _player.root.gameObject.SetActive(ok);
            if (ok)
            {
                _player.root.anchoredPosition = pl;
                _player.lineRT.gameObject.SetActive(_namesVisible);
                _player.labelRT.gameObject.SetActive(_namesVisible);
                Vector2 dir = new Vector2(-0.7f, 0.7f);
                _player.lineRT.anchoredPosition = dir * (3f + 13f);
                _player.lineRT.sizeDelta = new Vector2(26f, 1.5f);
                _player.lineRT.localRotation = Quaternion.Euler(0f, 0f, 135f);
                _player.labelRT.anchoredPosition = dir * 36f;
                _player.labelRT.pivot = new Vector2(1f, 0.5f);
                _player.label.alignment = TextAnchor.MiddleRight;
            }
        }
    }

    bool ScreenToLocal(Camera cam, Vector3 world, out Vector2 local)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        local = Vector2.zero;
        if (sp.z <= 0f) return false;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, sp, null, out local);
    }

    void RefreshToggleLabels()
    {
        if (_map == null) return;
        if (_orbitsLabel != null) _orbitsLabel.text = "ORBIT LINES  ·  " + (_map.OrbitsOn ? "ON" : "OFF");
        if (_namesLabel != null) _namesLabel.text = "NAMES  ·  " + (_namesVisible ? "ON" : "OFF");
    }

    // ── construction ────────────────────────────────────────────────────────
    void BuildCanvas()
    {
        _built = true;
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Map;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        gameObject.AddComponent<SkipControllerNav>();   // left stick flies the camera; the legend stays mouse-only
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f; _group.interactable = false; _group.blocksRaycasts = false;
        _canvasRT = GetComponent<RectTransform>();

        _markerLayer = new GameObject("Markers", typeof(RectTransform)).GetComponent<RectTransform>();
        _markerLayer.SetParent(transform, false);
        _markerLayer.anchorMin = new Vector2(0.5f, 0.5f);
        _markerLayer.anchorMax = new Vector2(0.5f, 0.5f);
        _markerLayer.pivot = new Vector2(0.5f, 0.5f);
        _markerLayer.anchoredPosition = Vector2.zero;
        _markerLayer.sizeDelta = Vector2.zero;

        BuildLegendShell();
    }

    void BuildBodies(CelestialBody[] bodies)
    {
        foreach (var m in _markers) if (m.root != null) Destroy(m.root.gameObject);
        _markers.Clear();
        if (_player != null && _player.root != null) Destroy(_player.root.gameObject);
        foreach (var r in _rows) if (r.bg != null) Destroy(r.bg.gameObject);
        _rows.Clear();
        foreach (Transform c in _legendRowsRoot) Destroy(c.gameObject);

        _sun = null;
        var sun = new List<CelestialBody>(); var planets = new List<CelestialBody>(); var dwarfs = new List<CelestialBody>(); var moons = new List<CelestialBody>();
        foreach (var b in bodies)
        {
            if (b == null) continue;
            if (b.isStaticAttractor) { sun.Add(b); continue; }          // the black hole lists with the star
            switch (b.bodyType)
            {
                case CelestialBody.BodyType.Sun: sun.Add(b); _sun = b; break;
                case CelestialBody.BodyType.Planet: if (b.radius < 100f) dwarfs.Add(b); else planets.Add(b); break;
                default: moons.Add(b); break;
            }
        }
        Comparison<CelestialBody> byName = (a, b) => string.Compare(a.bodyName, b.bodyName, StringComparison.OrdinalIgnoreCase);
        planets.Sort(byName); dwarfs.Sort(byName); moons.Sort(byName);

        foreach (var b in sun)     _markers.Add(MakeMarker(b, b.isStaticAttractor ? new Color(0.85f, 0.75f, 1f, 1f) : SunColor, false));
        foreach (var b in planets) _markers.Add(MakeMarker(b, PlanetColor, false));
        foreach (var b in dwarfs)  _markers.Add(MakeMarker(b, DwarfColor, false));
        foreach (var b in moons)   _markers.Add(MakeMarker(b, MoonColor, true));
        _player = MakeMarker(null, YouColor, false);
        _player.label.text = "YOU";

        if (sun.Count > 0)     { Section("STAR  ·  BLACK HOLE"); foreach (var b in sun) MakeRow(b); }
        if (planets.Count > 0) { Section("PLANETS");       foreach (var b in planets) MakeRow(b); }
        if (dwarfs.Count > 0)  { Section("DWARF PLANETS"); foreach (var b in dwarfs) MakeRow(b); }
        if (moons.Count > 0)   { Section("MOONS");         foreach (var b in moons) MakeRow(b); }
    }

    Marker MakeMarker(CelestialBody body, Color color, bool isMoon)
    {
        var m = new Marker { body = body, color = color, isMoon = isMoon };
        var root = new GameObject(body != null ? "Marker " + body.bodyName : "Marker YOU", typeof(RectTransform));
        m.root = root.GetComponent<RectTransform>();
        m.root.SetParent(_markerLayer, false);
        m.root.anchorMin = m.root.anchorMax = new Vector2(0.5f, 0.5f);
        m.root.pivot = new Vector2(0.5f, 0.5f);
        m.root.sizeDelta = Vector2.zero;

        var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
        m.lineRT = line.GetComponent<RectTransform>();
        m.lineRT.SetParent(m.root, false);
        m.lineRT.anchorMin = m.lineRT.anchorMax = new Vector2(0.5f, 0.5f);
        m.lineRT.pivot = new Vector2(0.5f, 0.5f);
        m.line = line.GetComponent<Image>();
        m.line.raycastTarget = false;

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        m.labelRT = label.GetComponent<RectTransform>();
        m.labelRT.SetParent(m.root, false);
        m.labelRT.anchorMin = m.labelRT.anchorMax = new Vector2(0.5f, 0.5f);
        m.labelRT.sizeDelta = new Vector2(220f, 20f);
        m.label = label.GetComponent<Text>();
        m.label.font = Font();
        m.label.fontSize = 14;
        m.label.fontStyle = FontStyle.Bold;
        m.label.text = body != null ? body.bodyName.ToUpperInvariant() : "";
        m.label.color = color;
        m.label.raycastTarget = body != null;   // the name IS the click target
        m.label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var ol = label.GetComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.9f);
        ol.effectDistance = new Vector2(1f, -1f);
        if (body != null)
        {
            m.button = label.AddComponent<Button>();
            m.button.targetGraphic = m.label;
            var cb = m.button.colors; cb.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1f); cb.pressedColor = new Color(2f, 2f, 2f, 1f); cb.fadeDuration = 0.08f;
            m.button.colors = cb;
            var captured = body;
            m.button.onClick.AddListener(() => { if (_map != null) _map.SetFollow(captured); });
        }
        return m;
    }

    // ── legend ──────────────────────────────────────────────────────────────
    RectTransform _legendRowsRoot;

    void BuildLegendShell()
    {
        var panel = new GameObject("Legend", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(transform, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -24f);
        rt.sizeDelta = new Vector2(LegendWidth, 0f);
        var img = panel.GetComponent<Image>();
        img.sprite = GalaxyHudKit.NebulaSprite(); img.type = Image.Type.Sliced; img.color = new Color(1f, 1f, 1f, 0.92f);
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        border.transform.SetParent(panel.transform, false);
        var bRT = border.GetComponent<RectTransform>(); bRT.anchorMin = Vector2.zero; bRT.anchorMax = Vector2.one; bRT.offsetMin = Vector2.zero; bRT.offsetMax = Vector2.zero;
        var bImg = border.GetComponent<Image>(); bImg.sprite = GalaxyHudKit.RoundedSprite(); bImg.type = Image.Type.Sliced;
        bImg.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.25f); bImg.raycastTarget = false;
        border.GetComponent<LayoutElement>().ignoreLayout = true;
        var vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 12); vlg.spacing = 3f; vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = panel.GetComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Header(panel.transform, "STELLAR MAP");
        Accent(panel.transform);

        var rows = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        rows.transform.SetParent(panel.transform, false);
        var rv = rows.GetComponent<VerticalLayoutGroup>(); rv.spacing = 3f; rv.childControlWidth = true; rv.childControlHeight = true; rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;
        var rf = rows.GetComponent<ContentSizeFitter>(); rf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _legendRowsRoot = rows.GetComponent<RectTransform>();

        Accent(panel.transform);
        _orbitsLabel = ActionButton(panel.transform, "ORBIT LINES  ·  ON", () => { if (_map != null) _map.ToggleOrbits(); });
        _namesLabel  = ActionButton(panel.transform, "NAMES  ·  ON", () => { if (_map != null) _map.ToggleNames(); });
        ActionButton(panel.transform, "RECENTER  (R)", () => { if (_map != null) _map.Recenter(); });
        Hint(panel.transform, "RMB drag · look      WASD Space Ctrl · fly\nShift · fast      wheel · zoom      Q E · roll");
        Hint(panel.transform, "click a name · match velocity      click space · unmatch\nlegend row · fly there + match");
        _cursorHint = Hint(panel.transform, "G  ·  lock cursor for mouse look");
        Hint(panel.transform, "M  /  Esc  ·  close");
    }

    void Section(string label)
    {
        var go = new GameObject("Section " + label, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(_legendRowsRoot, false);
        var t = go.GetComponent<Text>();
        t.text = label; t.font = Font(); t.fontSize = 11; t.fontStyle = FontStyle.Bold;
        t.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.85f);
        t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false;
        var le = go.GetComponent<LayoutElement>(); le.minHeight = 18f; le.preferredHeight = 18f;
    }

    void MakeRow(CelestialBody body)
    {
        var go = new GameObject("Row " + body.bodyName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(_legendRowsRoot, false);
        var img = go.GetComponent<Image>(); img.sprite = GalaxyHudKit.SlotSprite(); img.type = Image.Type.Sliced; img.color = new Color(1f, 1f, 1f, 0.9f);
        var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
        var cb = btn.colors;
        cb.highlightedColor = new Color(GalaxyHudKit.BorderCool.r * 1.4f, GalaxyHudKit.BorderCool.g * 1.4f, GalaxyHudKit.BorderCool.b * 1.4f, 1f);
        cb.pressedColor = GalaxyHudKit.BorderHot; cb.fadeDuration = 0.12f; btn.colors = cb;
        var captured = body;
        btn.onClick.AddListener(() => { if (_map != null) _map.FocusAndFollow(captured); });
        var le = go.GetComponent<LayoutElement>(); le.minHeight = RowH; le.preferredHeight = RowH;

        var lg = new GameObject("Label", typeof(RectTransform), typeof(Text));
        lg.transform.SetParent(go.transform, false);
        var lrt = lg.GetComponent<RectTransform>(); lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(10f, 0f); lrt.offsetMax = new Vector2(-10f, 0f);
        var t = lg.GetComponent<Text>();
        t.text = body.bodyName.ToUpperInvariant(); t.font = Font(); t.fontSize = 12; t.fontStyle = FontStyle.Bold;
        t.color = GalaxyHudKit.LabelColor; t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false;
        _rows.Add(new Row { body = body, bg = img, label = t });
    }

    Text ActionButton(Transform parent, string label, Action onClick)
    {
        var go = new GameObject("Button " + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.sprite = GalaxyHudKit.SlotSprite(); img.type = Image.Type.Sliced;
        img.color = new Color(GalaxyHudKit.BorderCool.r * 0.25f, GalaxyHudKit.BorderCool.g * 0.25f, GalaxyHudKit.BorderCool.b * 0.25f, 1f);
        var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
        var cb = btn.colors; cb.normalColor = new Color(1f, 1f, 1f, 0.95f);
        cb.highlightedColor = new Color(GalaxyHudKit.BorderCool.r * 1.4f, GalaxyHudKit.BorderCool.g * 1.4f, GalaxyHudKit.BorderCool.b * 1.4f, 1f);
        cb.pressedColor = GalaxyHudKit.BorderHot; cb.fadeDuration = 0.12f; btn.colors = cb;
        btn.onClick.AddListener(() => onClick());
        var le = go.GetComponent<LayoutElement>(); le.minHeight = RowH + 4f; le.preferredHeight = RowH + 4f;
        var lg = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        lg.transform.SetParent(go.transform, false);
        var lrt = lg.GetComponent<RectTransform>(); lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(10f, 0f); lrt.offsetMax = new Vector2(-10f, 0f);
        var t = lg.GetComponent<Text>();
        t.text = label; t.font = Font(); t.fontSize = 12; t.fontStyle = FontStyle.Bold; t.color = GalaxyHudKit.LabelColor; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
        var ol = lg.GetComponent<Outline>(); ol.effectColor = GalaxyHudKit.LabelGlow; ol.effectDistance = new Vector2(1f, -1f); ol.useGraphicAlpha = false;
        return t;
    }

    Text Hint(Transform parent, string text)
    {
        var go = new GameObject("Hint", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = text; t.font = Font(); t.fontSize = 10; t.color = new Color(GalaxyHudKit.LabelColor.r, GalaxyHudKit.LabelColor.g, GalaxyHudKit.LabelColor.b, 0.75f);
        t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
        var le = go.GetComponent<LayoutElement>(); float h = text.Contains("\n") ? 30f : 16f; le.minHeight = h; le.preferredHeight = h;
        return t;
    }

    void Header(Transform parent, string text)
    {
        var go = new GameObject("Header", typeof(RectTransform), typeof(Text), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = text; t.font = Font(); t.fontSize = 16; t.fontStyle = FontStyle.Bold; t.color = GalaxyHudKit.LabelColor; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
        var ol = go.GetComponent<Outline>(); ol.effectColor = GalaxyHudKit.LabelGlow; ol.effectDistance = new Vector2(1.4f, -1.4f); ol.useGraphicAlpha = false;
        var le = go.GetComponent<LayoutElement>(); le.minHeight = 26f; le.preferredHeight = 26f;
    }

    void Accent(Transform parent)
    {
        var go = new GameObject("Accent", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.sprite = GalaxyHudKit.AccentSprite(); img.color = Color.white; img.raycastTarget = false;
        var le = go.GetComponent<LayoutElement>(); le.minHeight = 3f; le.preferredHeight = 3f;
    }

    static Font Font() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
}
