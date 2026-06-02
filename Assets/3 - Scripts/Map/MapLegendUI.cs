using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MapLegendUI : MonoBehaviour
{
    const float PanelWidth   = 260f;
    const float SectionH     = 18f;
    const float RowH         = 30f;
    const float HeaderH      = 26f;
    const float AccentH      = 3f;
    const float ContentSpacing = 3f;

    struct EntryView
    {
        public CelestialBody body;
        public Button button;
        public Image background;
        public GameObject selectionBorderRoot; // parent of the 4 border strips, toggled active when selected
        public Image[] selectionBorderStrips;  // 4 strips (top, bottom, left, right) — pulsed by BorderPulse
        public Text label;
    }

    readonly List<EntryView> entries = new List<EntryView>();
    CelestialBody currentSelected;
    Text orbitToggleLabel;
    bool orbitToggleState;
    Text cursorLockHint;

    // ── Ship section (built dynamically at runtime per dish-equipped ship) ──
    struct ShipEntryView
    {
        public Ship ship;
        public Button button;
        public Image background;
        public GameObject selectionBorderRoot;
        public Image[] selectionBorderStrips;
        public Text label;
    }
    readonly List<ShipEntryView> shipEntries = new List<ShipEntryView>();
    Ship currentShipSelected;
    GameObject shipSectionGO;       // the per-ship rows container
    GameObject shipSectionHeader;   // "SHIPS" label, hidden when 0 ships
    SolarSystemMapController _controller; // cached for ship-row click wiring

    public void Build(CelestialBody[] bodies, SolarSystemMapController controller)
    {
        if (bodies == null || controller == null) return;

        var panel = BuildPanel();

        BuildHeader(panel, "STELLAR MAP");
        BuildAccent(panel);

        var sun     = new List<CelestialBody>();
        var planets = new List<CelestialBody>();
        var moons   = new List<CelestialBody>();
        foreach (var b in bodies)
        {
            if (b == null) continue;
            switch (b.bodyType)
            {
                case CelestialBody.BodyType.Sun:    sun.Add(b);     break;
                case CelestialBody.BodyType.Planet: planets.Add(b); break;
                case CelestialBody.BodyType.Moon:   moons.Add(b);   break;
            }
        }
        planets.Sort((a, b) => string.Compare(a.bodyName, b.bodyName, StringComparison.OrdinalIgnoreCase));
        moons.Sort((a, b) => string.Compare(a.bodyName, b.bodyName, StringComparison.OrdinalIgnoreCase));

        if (sun.Count > 0)
        {
            BuildSection(panel, "STAR");
            foreach (var b in sun) BuildEntry(panel, b, controller);
        }
        if (planets.Count > 0)
        {
            BuildSection(panel, "PLANETS");
            foreach (var b in planets) BuildEntry(panel, b, controller);
        }
        if (moons.Count > 0)
        {
            BuildSection(panel, "MOONS");
            foreach (var b in moons) BuildEntry(panel, b, controller);
        }

        // Ship section header + empty container — populated by RefreshShipEntries
        // each time the map opens or the orbit-lines toggle fires. Header is
        // hidden by default so it only appears when at least one dish-equipped
        // ship is in the scene.
        _controller = controller;
        shipSectionHeader = BuildSectionDeferred(panel, "SHIPS");
        shipSectionHeader.SetActive(false);
        shipSectionGO = new GameObject("ShipSection", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        shipSectionGO.transform.SetParent(panel.transform, worldPositionStays: false);
        var ssVlg = shipSectionGO.GetComponent<VerticalLayoutGroup>();
        ssVlg.spacing = ContentSpacing;
        ssVlg.padding = new RectOffset(0, 0, 0, 0);
        ssVlg.childControlWidth = true;
        ssVlg.childControlHeight = true;
        ssVlg.childForceExpandWidth = true;
        ssVlg.childForceExpandHeight = false;
        var ssFitter = shipSectionGO.GetComponent<ContentSizeFitter>();
        ssFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        ssFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildAccent(panel);
        BuildOrbitToggle(panel, controller);
        BuildCursorLockHint(panel);
    }

    // Like BuildSection but returns the GameObject so callers can hide/show
    // it dynamically as ships come and go.
    GameObject BuildSectionDeferred(GameObject parent, string label)
    {
        var go = new GameObject("Section_" + label, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, worldPositionStays: false);

        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 11;
        t.fontStyle = FontStyle.Bold;
        t.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.85f);
        t.alignment = TextAnchor.MiddleLeft;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = SectionH;
        le.preferredHeight = SectionH;
        le.flexibleHeight = 0f;
        return go;
    }

    /// <summary>
    /// Called by the controller whenever the set of dish-equipped ships
    /// might have changed (map open, orbit-lines toggle, etc.). Rebuilds the
    /// SHIPS section to exactly reflect the supplied list. Ships gained get
    /// new rows; ships lost (destroyed or dish removed) lose their rows.
    /// </summary>
    public void RefreshShipEntries(Ship[] ships)
    {
        if (shipSectionGO == null) return;

        // Tear down old rows.
        foreach (var e in shipEntries)
            if (e.button != null) Destroy(e.button.gameObject);
        shipEntries.Clear();
        if (_playerEntryGO != null) { Destroy(_playerEntryGO); _playerEntryGO = null; }

        bool hasShips = ships != null && ships.Length > 0;

        // The PLAYER entry always shows (player exists in every gameplay
        // scene). The section header still appears whether or not ships
        // exist, because we now have at least the player row.
        if (shipSectionHeader != null) shipSectionHeader.SetActive(true);

        BuildPlayerEntry();

        if (hasShips)
        {
            int idx = 0;
            foreach (var ship in ships)
            {
                if (ship == null) continue;
                BuildShipEntry(ship, idx++);
            }
        }
    }

    GameObject _playerEntryGO;
    void BuildPlayerEntry()
    {
        // Mirrors BuildShipEntry's shape but simpler — no per-ship index,
        // no BoughtShip lookup, no per-instance state. Single row at the
        // top of the ship section labeled PLAYER. Click routes through
        // SolarSystemMapController.OnLegendPlayerClick.
        var btnGO = new GameObject("PlayerEntry",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGO.transform.SetParent(shipSectionGO.transform, worldPositionStays: false);
        _playerEntryGO = btnGO;

        var img = btnGO.GetComponent<Image>();
        img.sprite = GalaxyHudKit.SlotSprite();
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            if (_controller != null) _controller.OnLegendPlayerClick();
        });

        var le = btnGO.GetComponent<LayoutElement>();
        le.minHeight = RowH;
        le.preferredHeight = RowH;
        le.flexibleHeight = 0f;

        // Red pip — matches the HAL identity colour, makes the player row
        // visually distinct from ships (green) and planets.
        var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image));
        pip.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var pRT = pip.GetComponent<RectTransform>();
        pRT.anchorMin = new Vector2(0f, 0.5f);
        pRT.anchorMax = new Vector2(0f, 0.5f);
        pRT.pivot = new Vector2(0f, 0.5f);
        pRT.anchoredPosition = new Vector2(12f, 0f);
        pRT.sizeDelta = new Vector2(14f, 14f);
        var pImg = pip.GetComponent<Image>();
        pImg.sprite = GalaxyHudKit.RoundedSprite();
        pImg.type = Image.Type.Sliced;
        pImg.color = new Color(1f, 0.13f, 0.05f, 1f); // HAL red, same as HALVisuals.EyeRed
        pImg.raycastTarget = false;

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(40f, 0f);
        lrt.offsetMax = new Vector2(-12f, 0f);
        var lt = labelGO.GetComponent<Text>();
        lt.text = "PLAYER";
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.fontSize = 13;
        lt.fontStyle = FontStyle.Bold;
        lt.color = GalaxyHudKit.LabelColor;
        lt.alignment = TextAnchor.MiddleLeft;
        lt.raycastTarget = false;
    }

    void BuildShipEntry(Ship ship, int index)
    {
        var btnGO = new GameObject("ShipEntry_" + ship.name,
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGO.transform.SetParent(shipSectionGO.transform, worldPositionStays: false);

        var img = btnGO.GetComponent<Image>();
        img.sprite = GalaxyHudKit.SlotSprite();
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        var captured = ship;
        btn.onClick.AddListener(() =>
        {
            if (_controller != null) _controller.OnLegendShipClick(captured);
        });

        var le = btnGO.GetComponent<LayoutElement>();
        le.minHeight = RowH;
        le.preferredHeight = RowH;
        le.flexibleHeight = 0f;

        // Selection border (same 4-strip frame as planet entries).
        var borderRoot = new GameObject("SelectionBorder", typeof(RectTransform));
        borderRoot.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var brrt = borderRoot.GetComponent<RectTransform>();
        brrt.anchorMin = Vector2.zero;
        brrt.anchorMax = Vector2.one;
        brrt.offsetMin = Vector2.zero;
        brrt.offsetMax = Vector2.zero;
        const float kBorderThickness = 3f;
        var stripTop    = MakeBorderStrip(borderRoot.transform, "Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -kBorderThickness), Vector2.zero);
        var stripBottom = MakeBorderStrip(borderRoot.transform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, kBorderThickness));
        var stripLeft   = MakeBorderStrip(borderRoot.transform, "Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(kBorderThickness, 0f));
        var stripRight  = MakeBorderStrip(borderRoot.transform, "Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-kBorderThickness, 0f), Vector2.zero);
        borderRoot.SetActive(false);

        // Green pip to differentiate from planet entries.
        var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image));
        pip.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var pRT = pip.GetComponent<RectTransform>();
        pRT.anchorMin = new Vector2(0f, 0.5f);
        pRT.anchorMax = new Vector2(0f, 0.5f);
        pRT.pivot = new Vector2(0f, 0.5f);
        pRT.anchoredPosition = new Vector2(12f, 0f);
        pRT.sizeDelta = new Vector2(14f, 14f);
        var pImg = pip.GetComponent<Image>();
        pImg.sprite = GalaxyHudKit.RoundedSprite();
        pImg.type = Image.Type.Sliced;
        pImg.color = new Color(0.45f, 1f, 0.55f, 1f); // matches MapOrbitLines.shipColor
        pImg.raycastTarget = false;

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(40f, 0f);
        lrt.offsetMax = new Vector2(-12f, 0f);
        var lt = labelGO.GetComponent<Text>();
        // Prefer the persistent shipNumber assigned at purchase time (saved
        // and restored). Falls back to the build-order index for any ship
        // missing the marker (legacy saves / future non-vendor spawns).
        var marker = ship.GetComponent<BoughtShip>();
        int shown = marker != null ? marker.shipNumber : index + 1;
        lt.text = $"Ship {shown}";
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.fontSize = 13;
        lt.fontStyle = FontStyle.Bold;
        lt.color = GalaxyHudKit.LabelColor;
        lt.alignment = TextAnchor.MiddleLeft;
        lt.raycastTarget = false;

        var entry = new ShipEntryView
        {
            ship = ship, button = btn, background = img, label = lt,
            selectionBorderRoot = borderRoot,
            selectionBorderStrips = new[] { stripTop, stripBottom, stripLeft, stripRight }
        };
        shipEntries.Add(entry);
        ApplyShipButtonState(entry, false);
    }

    public void SetShipSelected(Ship ship)
    {
        currentShipSelected = ship;
        for (int i = 0; i < shipEntries.Count; i++)
        {
            ApplyShipButtonState(shipEntries[i], shipEntries[i].ship == ship);
        }
    }

    void ApplyShipButtonState(ShipEntryView e, bool isSelected)
    {
        if (e.button == null || e.background == null) return;
        var colors = e.button.colors;
        if (isSelected)
        {
            Color hot = new Color(GalaxyHudKit.BorderHot.r, GalaxyHudKit.BorderHot.g, GalaxyHudKit.BorderHot.b, 1f);
            colors.normalColor = hot;
            colors.highlightedColor = new Color(Mathf.Min(hot.r * 1.15f, 1f), Mathf.Min(hot.g * 1.15f, 1f), Mathf.Min(hot.b * 1.15f, 1f), 1f);
            colors.pressedColor = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 1f);
            colors.selectedColor = colors.highlightedColor;
            if (e.label != null) e.label.color = Color.white;
        }
        else
        {
            colors.normalColor = new Color(1f, 1f, 1f, 0.9f);
            colors.highlightedColor = new Color(GalaxyHudKit.BorderCool.r * 1.4f, GalaxyHudKit.BorderCool.g * 1.4f, GalaxyHudKit.BorderCool.b * 1.4f, 1f);
            colors.pressedColor = new Color(GalaxyHudKit.BorderHot.r, GalaxyHudKit.BorderHot.g, GalaxyHudKit.BorderHot.b, 1f);
            colors.selectedColor = colors.highlightedColor;
            if (e.label != null) e.label.color = GalaxyHudKit.LabelColor;
        }
        colors.fadeDuration = 0.12f;
        e.button.colors = colors;
        if (e.selectionBorderRoot != null) e.selectionBorderRoot.SetActive(isSelected);
    }

    void BuildCursorLockHint(GameObject parent)
    {
        var go = new GameObject("CursorLockHint",
            typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, worldPositionStays: false);

        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 11;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 20f;
        le.preferredHeight = 20f;
        le.flexibleHeight = 0f;

        cursorLockHint = t;
        SyncCursorLockHint(false);
    }

    public void SyncCursorLockHint(bool isLocked)
    {
        if (cursorLockHint == null) return;
        if (isLocked)
        {
            cursorLockHint.text = "CURSOR LOCKED — PRESS G TO UNLOCK";
            cursorLockHint.color = new Color(GalaxyHudKit.BorderHot.r,
                                             GalaxyHudKit.BorderHot.g,
                                             GalaxyHudKit.BorderHot.b, 1f);
        }
        else
        {
            cursorLockHint.text = "PRESS G — TOGGLE CURSOR LOCK";
            cursorLockHint.color = new Color(GalaxyHudKit.BorderCool.r,
                                             GalaxyHudKit.BorderCool.g,
                                             GalaxyHudKit.BorderCool.b, 0.95f);
        }
    }

    void BuildOrbitToggle(GameObject parent, SolarSystemMapController controller)
    {
        var btnGO = new GameObject("OrbitToggle",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGO.transform.SetParent(parent.transform, worldPositionStays: false);

        var img = btnGO.GetComponent<Image>();
        img.sprite = GalaxyHudKit.SlotSprite();
        img.type   = Image.Type.Sliced;
        img.color  = new Color(GalaxyHudKit.BorderCool.r * 0.25f,
                               GalaxyHudKit.BorderCool.g * 0.25f,
                               GalaxyHudKit.BorderCool.b * 0.25f, 1f);

        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor      = new Color(1f, 1f, 1f, 0.95f);
        colors.highlightedColor = new Color(GalaxyHudKit.BorderCool.r * 1.4f,
                                            GalaxyHudKit.BorderCool.g * 1.4f,
                                            GalaxyHudKit.BorderCool.b * 1.4f, 1f);
        colors.pressedColor     = new Color(GalaxyHudKit.BorderHot.r,
                                            GalaxyHudKit.BorderHot.g,
                                            GalaxyHudKit.BorderHot.b, 1f);
        colors.selectedColor    = colors.highlightedColor;
        colors.fadeDuration     = 0.12f;
        btn.colors = colors;

        var le = btnGO.GetComponent<LayoutElement>();
        le.minHeight = RowH + 4f;
        le.preferredHeight = RowH + 4f;
        le.flexibleHeight = 0f;

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        labelGO.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(10f, 0f);
        lrt.offsetMax = new Vector2(-10f, 0f);
        var lt = labelGO.GetComponent<Text>();
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.fontSize = 12;
        lt.fontStyle = FontStyle.Bold;
        lt.color = GalaxyHudKit.LabelColor;
        lt.alignment = TextAnchor.MiddleCenter;
        lt.raycastTarget = false;
        var outline = labelGO.GetComponent<Outline>();
        outline.effectColor = GalaxyHudKit.LabelGlow;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;

        orbitToggleLabel = lt;
        orbitToggleState = false;
        RefreshOrbitToggleLabel();

        btn.onClick.AddListener(() =>
        {
            controller.SuppressWorldClickThisFrame();
            orbitToggleState = controller.ToggleOrbitLines();
            RefreshOrbitToggleLabel();
        });
    }

    public void SyncOrbitToggleVisible(bool isOn)
    {
        orbitToggleState = isOn;
        RefreshOrbitToggleLabel();
    }

    void RefreshOrbitToggleLabel()
    {
        if (orbitToggleLabel == null) return;
        orbitToggleLabel.text = orbitToggleState ? "ORBIT LINES: ON" : "TOGGLE ORBIT LINES";
        orbitToggleLabel.color = orbitToggleState
            ? new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 1f)
            : GalaxyHudKit.LabelColor;
    }

    public void SetSelected(CelestialBody body)
    {
        currentSelected = body;
        for (int i = 0; i < entries.Count; i++)
        {
            ApplyButtonState(entries[i], entries[i].body == body);
        }
    }

    // ── Controller D-pad navigation surface ─────────────────────────────
    public int Count => entries.Count;

    public CelestialBody GetBody(int index)
    {
        if (index < 0 || index >= entries.Count) return null;
        return entries[index].body;
    }

    // Highlight without invoking the click action — for D-pad focus motion.
    // The visible state mirrors SetSelected, so the entry pops the same way
    // a mouse-clicked entry does, but no ring/follow side-effects fire.
    public void HighlightIndex(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        SetSelected(entries[index].body);
    }

    // Locate the index of the body the user last clicked / D-pad-highlighted.
    // Returns -1 if no selection.
    public int IndexOfSelected()
    {
        if (currentSelected == null) return -1;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].body == currentSelected) return i;
        return -1;
    }

    void ApplyButtonState(EntryView e, bool isSelected)
    {
        if (e.button == null || e.background == null) return;
        var colors = e.button.colors;
        if (isSelected)
        {
            // Bright magenta-leaning fill so a marked planet pops in the list.
            Color hot = new Color(GalaxyHudKit.BorderHot.r, GalaxyHudKit.BorderHot.g, GalaxyHudKit.BorderHot.b, 1f);
            colors.normalColor      = hot;
            colors.highlightedColor = new Color(Mathf.Min(hot.r * 1.15f, 1f),
                                                Mathf.Min(hot.g * 1.15f, 1f),
                                                Mathf.Min(hot.b * 1.15f, 1f), 1f);
            colors.pressedColor     = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 1f);
            colors.selectedColor    = colors.highlightedColor;
            if (e.label != null) e.label.color = Color.white;
        }
        else
        {
            colors.normalColor      = new Color(1f, 1f, 1f, 0.9f);
            colors.highlightedColor = new Color(GalaxyHudKit.BorderCool.r * 1.4f,
                                                GalaxyHudKit.BorderCool.g * 1.4f,
                                                GalaxyHudKit.BorderCool.b * 1.4f, 1f);
            colors.pressedColor     = new Color(GalaxyHudKit.BorderHot.r,
                                                GalaxyHudKit.BorderHot.g,
                                                GalaxyHudKit.BorderHot.b, 1f);
            colors.selectedColor    = colors.highlightedColor;
            if (e.label != null) e.label.color = GalaxyHudKit.LabelColor;
        }
        colors.fadeDuration = 0.12f;
        e.button.colors = colors;

        // Show / hide the vibrant cyan-magenta border ring on the selected
        // entry. The fill color change alone was hard to see (the magenta
        // background blended with the panel); the border gives the highlight
        // a clear, controller-friendly visual frame. Pulses on the selected
        // entry via BorderPulse coroutine.
        if (e.selectionBorderRoot != null)
            e.selectionBorderRoot.SetActive(isSelected);
    }

    GameObject BuildPanel()
    {
        var panelGO = new GameObject("Panel",
            typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGO.transform.SetParent(transform, worldPositionStays: false);

        var rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -24f);
        rt.sizeDelta = new Vector2(PanelWidth, 0f);

        var img = panelGO.GetComponent<Image>();
        img.sprite = GalaxyHudKit.NebulaSprite();
        img.type   = Image.Type.Sliced;
        img.color  = new Color(1f, 1f, 1f, 0.92f);

        var glow = new GameObject("OuterGlow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        glow.transform.SetParent(panelGO.transform, worldPositionStays: false);
        var glowRT = glow.GetComponent<RectTransform>();
        glowRT.anchorMin = Vector2.zero;
        glowRT.anchorMax = Vector2.one;
        glowRT.offsetMin = new Vector2(-12f, -12f);
        glowRT.offsetMax = new Vector2( 12f,  12f);
        var glowImg = glow.GetComponent<Image>();
        glowImg.sprite = GalaxyHudKit.GlowSprite();
        glowImg.color  = new Color(GalaxyHudKit.GlowColor.r, GalaxyHudKit.GlowColor.g, GalaxyHudKit.GlowColor.b, 0.35f);
        glowImg.raycastTarget = false;
        glow.GetComponent<LayoutElement>().ignoreLayout = true;
        glow.transform.SetAsFirstSibling();

        var border = new GameObject("Border", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        border.transform.SetParent(panelGO.transform, worldPositionStays: false);
        var bRT = border.GetComponent<RectTransform>();
        bRT.anchorMin = Vector2.zero;
        bRT.anchorMax = Vector2.one;
        bRT.offsetMin = Vector2.zero;
        bRT.offsetMax = Vector2.zero;
        var bImg = border.GetComponent<Image>();
        bImg.sprite = GalaxyHudKit.RoundedSprite();
        bImg.type   = Image.Type.Sliced;
        bImg.color  = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.25f);
        bImg.raycastTarget = false;
        border.GetComponent<LayoutElement>().ignoreLayout = true;

        var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 12);
        vlg.spacing = ContentSpacing;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth   = true;
        vlg.childControlHeight  = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = panelGO.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        return panelGO;
    }

    void BuildHeader(GameObject parent, string text)
    {
        var go = new GameObject("Header", typeof(RectTransform), typeof(Text), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, worldPositionStays: false);

        var t = go.GetComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 16;
        t.fontStyle = FontStyle.Bold;
        t.color = GalaxyHudKit.LabelColor;
        t.alignment = TextAnchor.MiddleCenter;

        var outline = go.GetComponent<Outline>();
        outline.effectColor = GalaxyHudKit.LabelGlow;
        outline.effectDistance = new Vector2(1.4f, -1.4f);
        outline.useGraphicAlpha = false;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = HeaderH;
        le.preferredHeight = HeaderH;
        le.flexibleHeight = 0f;
    }

    void BuildAccent(GameObject parent)
    {
        var go = new GameObject("Accent", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, worldPositionStays: false);

        var img = go.GetComponent<Image>();
        img.sprite = GalaxyHudKit.AccentSprite();
        img.color = Color.white;
        img.raycastTarget = false;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = AccentH;
        le.preferredHeight = AccentH;
        le.flexibleHeight = 0f;
    }

    void BuildSection(GameObject parent, string label)
    {
        var go = new GameObject("Section_" + label, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, worldPositionStays: false);

        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 11;
        t.fontStyle = FontStyle.Bold;
        t.color = new Color(GalaxyHudKit.BorderCool.r, GalaxyHudKit.BorderCool.g, GalaxyHudKit.BorderCool.b, 0.85f);
        t.alignment = TextAnchor.MiddleLeft;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = SectionH;
        le.preferredHeight = SectionH;
        le.flexibleHeight = 0f;
    }

    void BuildEntry(GameObject parent, CelestialBody body, SolarSystemMapController controller)
    {
        var btnGO = new GameObject("Entry_" + body.bodyName,
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGO.transform.SetParent(parent.transform, worldPositionStays: false);

        var img = btnGO.GetComponent<Image>();
        img.sprite = GalaxyHudKit.SlotSprite();
        img.type   = Image.Type.Sliced;
        img.color  = Color.white;

        var btn = btnGO.GetComponent<Button>();
        btn.targetGraphic = img;

        var captured = body;
        btn.onClick.AddListener(() => controller.OnLegendClick(captured));

        var le = btnGO.GetComponent<LayoutElement>();
        le.minHeight = RowH;
        le.preferredHeight = RowH;
        le.flexibleHeight = 0f;

        // Selection border — composed of four thin colored strips (top, bottom,
        // left, right) that frame the row. Built as a single child container
        // toggled active when this entry is selected. Strips render on TOP of
        // the entry's content (added last in sibling order), so the frame is
        // unambiguous even on the magenta selected fill.
        var borderRoot = new GameObject("SelectionBorder", typeof(RectTransform));
        borderRoot.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var brrt = borderRoot.GetComponent<RectTransform>();
        brrt.anchorMin = Vector2.zero;
        brrt.anchorMax = Vector2.one;
        brrt.offsetMin = Vector2.zero;
        brrt.offsetMax = Vector2.zero;

        const float kBorderThickness = 3f;
        var stripTop    = MakeBorderStrip(borderRoot.transform, "Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -kBorderThickness), Vector2.zero);
        var stripBottom = MakeBorderStrip(borderRoot.transform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, kBorderThickness));
        var stripLeft   = MakeBorderStrip(borderRoot.transform, "Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(kBorderThickness, 0f));
        var stripRight  = MakeBorderStrip(borderRoot.transform, "Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-kBorderThickness, 0f), Vector2.zero);
        borderRoot.SetActive(false);

        var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image));
        pip.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var pRT = pip.GetComponent<RectTransform>();
        pRT.anchorMin = new Vector2(0f, 0.5f);
        pRT.anchorMax = new Vector2(0f, 0.5f);
        pRT.pivot     = new Vector2(0f, 0.5f);
        pRT.anchoredPosition = new Vector2(12f, 0f);
        pRT.sizeDelta = new Vector2(14f, 14f);
        var pImg = pip.GetComponent<Image>();
        pImg.sprite = GalaxyHudKit.RoundedSprite();
        pImg.type   = Image.Type.Sliced;
        pImg.color  = ColorForBody(body);
        pImg.raycastTarget = false;

        var halo = new GameObject("PipGlow", typeof(RectTransform), typeof(Image));
        halo.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var hRT = halo.GetComponent<RectTransform>();
        hRT.anchorMin = pRT.anchorMin;
        hRT.anchorMax = pRT.anchorMax;
        hRT.pivot     = pRT.pivot;
        hRT.anchoredPosition = new Vector2(12f - 5f, 0f);
        hRT.sizeDelta = new Vector2(24f, 24f);
        var hImg = halo.GetComponent<Image>();
        hImg.sprite = GalaxyHudKit.GlowSprite();
        Color halo_c = ColorForBody(body);
        hImg.color = new Color(halo_c.r, halo_c.g, halo_c.b, 0.45f);
        hImg.raycastTarget = false;
        halo.transform.SetAsFirstSibling();

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(btnGO.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(40f, 0f);
        lrt.offsetMax = new Vector2(-12f, 0f);
        var lt = labelGO.GetComponent<Text>();
        lt.text = body.bodyName;
        lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lt.fontSize = 13;
        lt.fontStyle = FontStyle.Bold;
        lt.color = GalaxyHudKit.LabelColor;
        lt.alignment = TextAnchor.MiddleLeft;
        lt.raycastTarget = false;

        var entry = new EntryView {
            body = body, button = btn, background = img, label = lt,
            selectionBorderRoot   = borderRoot,
            selectionBorderStrips = new[] { stripTop, stripBottom, stripLeft, stripRight }
        };
        entries.Add(entry);
        ApplyButtonState(entry, false);
    }

    void Awake()
    {
        StartCoroutine(BorderPulse());
    }

    static Image MakeBorderStrip(Transform parent, string name,
                                 Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                 Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var img = go.GetComponent<Image>();
        img.color = GalaxyHudKit.BorderHot;
        img.raycastTarget = false;
        return img;
    }

    System.Collections.IEnumerator BorderPulse()
    {
        while (this != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 1.6f) + 1f) * 0.5f;
            Color pulsed = Color.Lerp(GalaxyHudKit.BorderCool, GalaxyHudKit.BorderHot, t);
            for (int i = 0; i < entries.Count; i++)
            {
                var root   = entries[i].selectionBorderRoot;
                var strips = entries[i].selectionBorderStrips;
                if (root == null || strips == null || !root.activeSelf) continue;
                for (int s = 0; s < strips.Length; s++)
                    if (strips[s] != null) strips[s].color = pulsed;
            }
            for (int i = 0; i < shipEntries.Count; i++)
            {
                var root   = shipEntries[i].selectionBorderRoot;
                var strips = shipEntries[i].selectionBorderStrips;
                if (root == null || strips == null || !root.activeSelf) continue;
                for (int s = 0; s < strips.Length; s++)
                    if (strips[s] != null) strips[s].color = pulsed;
            }
            yield return null;
        }
    }

    Color ColorForBody(CelestialBody body)
    {
        if (body == null) return new Color(0.7f, 0.7f, 0.7f);
        if (body.bodyType == CelestialBody.BodyType.Sun) return new Color(1f, 0.85f, 0.4f);
        string n = body.bodyName != null ? body.bodyName.ToLowerInvariant() : "";
        if (n.Contains("fiery")) return new Color(0.95f, 0.4f, 0.2f);
        if (n.Contains("icey") || n.Contains("icy")) return new Color(0.7f, 0.9f, 1f);
        if (n.Contains("humble")) return new Color(0.45f, 0.85f, 0.55f);
        if (n.Contains("cyclops")) return new Color(0.7f, 0.45f, 0.95f);
        if (n.Contains("companion")) return new Color(0.85f, 0.85f, 0.85f);
        if (n.Contains("tumbling") || n.Contains("bean")) return new Color(0.75f, 0.65f, 0.4f);
        if (n.Contains("watchful")) return new Color(0.95f, 0.78f, 0.4f);
        return new Color(0.7f, 0.7f, 0.7f);
    }
}
