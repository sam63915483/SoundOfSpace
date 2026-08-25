using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// NAV — the shuttle-travel app on the shuttle computer (handoff §6).
// partial-class file, the ShuttleComputerProjectsUI precedent.
//
// Five states, driven by ShuttleAutopilot.CurrentPhase (never a local copy):
//   PARKED    → planet list + TRAVEL button
//   COUNTDOWN → "TAKING OFF IN N"
//   LIFTOFF/TRANSIT → "EN ROUTE TO <planet>" + progress bar
//   HOVER     → downward camera feed, crosshair, altitude, green/red border,
//               "WASD POSITION · SPACE LAND"; WASD/QE/SPACE are read HERE
//               (player movement is already dead behind isInModalSlotUI)
//   LANDING   → feed stays, "LANDING…"
//
// NavDrive() ticks every frame from Update — open or mirroring — so the world
// screen's copy of the countdown/feed never freezes (the DriveMachine rule).
// [AUTHOR] All strings are placeholder; Sam voices them later.
public partial class ShuttleComputerUI
{
    GameObject _navView;
    GameObject _navListPane, _navStatusPane, _navHoverPane;

    // list pane
    class NavTile { public string body; public Image frame; public TextMeshProUGUI label; public bool here; }
    readonly List<NavTile> _navTiles = new List<NavTile>();
    RectTransform _navTileRow;
    string _navListBuiltFor = null;
    string _navSelected = "";
    Image _navTravelBg;
    TextMeshProUGUI _navTravelLabel;
    TextMeshProUGUI _navToastLabel;
    float _navToastUntil2;

    // status pane
    TextMeshProUGUI _navStatusBig, _navStatusSub;
    Image _navProgressFill;

    // hover pane
    RawImage _navFeed;
    Image _navFeedBorder;
    TextMeshProUGUI _navHoverPrompt, _navAltReadout;
    float _navRedFlashUntil;

    ShuttleAutopilot.Phase _navShownPhase = (ShuttleAutopilot.Phase)255;
    ShuttleAutopilot _navSubscribedPilot;

    public bool NavOpen { get { return _navView != null && _navView.activeSelf; } }

    // ── construction ─────────────────────────────────────────────────────

    void BuildNav(RectTransform parent)
    {
        var view = MakeRect(parent, "NavView");
        Stretch(view, SidePad, SidePad, ContentTop, ContentBottom);
        _navView = view.gameObject;

        var title = MakeText(view, "Title", "NAV — PLANETARY TRAVEL", 18, InkGhost, TextAlignmentOptions.TopLeft);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1);
        trt.sizeDelta = new Vector2(0, 26);
        trt.anchoredPosition = Vector2.zero;
        title.characterSpacing = 18;

        BuildNavListPane(view);
        BuildNavStatusPane(view);
        BuildNavHoverPane(view);

        _navView.SetActive(false);
    }

    void BuildNavListPane(RectTransform parent)
    {
        var pane = MakeRect(parent, "NavListPane");
        Stretch(pane, 0, 0, 34, 0);
        _navListPane = pane.gameObject;

        var hint = MakeText(pane, "Hint", "SELECT DESTINATION", 14, InkDim, TextAlignmentOptions.Center);
        var hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 24);
        hrt.anchoredPosition = new Vector2(0, -6);
        hint.characterSpacing = 16;

        _navTileRow = MakeRect(pane, "Tiles");
        Stretch(_navTileRow, 0, 0, 40, 120);

        // TRAVEL — enabled only with a non-current planet selected.
        var btn = MakePanel(pane, "TravelBtn", Panel);
        _navTravelBg = btn;
        var brt = btn.rectTransform;
        brt.anchorMin = new Vector2(0.5f, 0); brt.anchorMax = new Vector2(0.5f, 0);
        brt.pivot = new Vector2(0.5f, 0);
        brt.sizeDelta = new Vector2(420, 64);
        brt.anchoredPosition = new Vector2(0, 30);
        Outline(btn.transform, Grid);
        _navTravelLabel = MakeText(brt, "Label", "TRAVEL", 24, Locked, TextAlignmentOptions.Center);
        Stretch(_navTravelLabel.rectTransform, 0, 0, 0, 0);
        _navTravelLabel.characterSpacing = 24;
        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        cb.pressedColor = new Color(2f, 2f, 2f, 1f);
        b.colors = cb;
        b.onClick.AddListener(OnNavTravelClicked);

        _navToastLabel = MakeText(pane, "Toast", "", 16, Warn, TextAlignmentOptions.Center);
        var tort = _navToastLabel.rectTransform;
        tort.anchorMin = new Vector2(0, 0); tort.anchorMax = new Vector2(1, 0);
        tort.pivot = new Vector2(0.5f, 0);
        tort.sizeDelta = new Vector2(0, 24);
        tort.anchoredPosition = new Vector2(0, 100);
    }

    void BuildNavStatusPane(RectTransform parent)
    {
        var pane = MakeRect(parent, "NavStatusPane");
        Stretch(pane, 0, 0, 34, 0);
        _navStatusPane = pane.gameObject;

        _navStatusBig = MakeText(pane, "Big", "", 64, Accent, TextAlignmentOptions.Center);
        var brt = _navStatusBig.rectTransform;
        brt.anchorMin = new Vector2(0, 0.5f); brt.anchorMax = new Vector2(1, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(0, 90);
        brt.anchoredPosition = new Vector2(0, 60);
        _navStatusBig.characterSpacing = 10;

        _navStatusSub = MakeText(pane, "Sub", "", 20, InkDim, TextAlignmentOptions.Center);
        var srt = _navStatusSub.rectTransform;
        srt.anchorMin = new Vector2(0, 0.5f); srt.anchorMax = new Vector2(1, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(0, 30);
        srt.anchoredPosition = new Vector2(0, -20);
        _navStatusSub.characterSpacing = 14;

        var barBack = MakePanel(pane, "ProgressBack", Panel);
        var pbrt = barBack.rectTransform;
        pbrt.anchorMin = new Vector2(0.5f, 0.5f); pbrt.anchorMax = new Vector2(0.5f, 0.5f);
        pbrt.pivot = new Vector2(0.5f, 0.5f);
        pbrt.sizeDelta = new Vector2(760, 18);
        pbrt.anchoredPosition = new Vector2(0, -70);
        Outline(barBack.transform, Grid);

        _navProgressFill = MakePanel(pbrt, "Fill", Accent);
        var frt = _navProgressFill.rectTransform;
        frt.anchorMin = new Vector2(0, 0);
        frt.anchorMax = new Vector2(0, 1);   // anchorMax.x driven by progress
        frt.pivot = new Vector2(0, 0.5f);
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
    }

    void BuildNavHoverPane(RectTransform parent)
    {
        var pane = MakeRect(parent, "NavHoverPane");
        Stretch(pane, 0, 0, 34, 0);
        _navHoverPane = pane.gameObject;

        var feedGo = new GameObject("Feed", typeof(RectTransform));
        feedGo.transform.SetParent(pane, false);
        _navFeed = feedGo.AddComponent<RawImage>();
        _navFeed.raycastTarget = false;
        _navFeed.color = Color.white;
        Stretch((RectTransform)feedGo.transform, 8, 8, 8, 8);

        _navFeedBorder = MakeSprite(pane, "Border", TraxUISprites.Border, Warn);
        _navFeedBorder.type = Image.Type.Sliced;
        _navFeedBorder.raycastTarget = false;
        Stretch(_navFeedBorder.rectTransform, 4, 4, 4, 4);

        // Crosshair — two thin strips at the centre of the feed.
        var chH = MakePanel(pane, "CrossH", new Color(1f, 1f, 1f, 0.55f));
        Box(chH.rectTransform, Centre, Centre, Vector2.zero, new Vector2(46, 2));
        chH.raycastTarget = false;
        var chV = MakePanel(pane, "CrossV", new Color(1f, 1f, 1f, 0.55f));
        Box(chV.rectTransform, Centre, Centre, Vector2.zero, new Vector2(2, 46));
        chV.raycastTarget = false;

        _navAltReadout = MakeText(pane, "Alt", "", 20, Ink, TextAlignmentOptions.TopRight);
        var art = _navAltReadout.rectTransform;
        art.anchorMin = new Vector2(1, 1); art.anchorMax = new Vector2(1, 1);
        art.pivot = new Vector2(1, 1);
        art.sizeDelta = new Vector2(300, 28);
        art.anchoredPosition = new Vector2(-20, -16);

        _navHoverPrompt = MakeText(pane, "Prompt", "", 20, Ink, TextAlignmentOptions.Center);
        var prt = _navHoverPrompt.rectTransform;
        prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(1, 0);
        prt.pivot = new Vector2(0.5f, 0);
        prt.sizeDelta = new Vector2(0, 30);
        prt.anchoredPosition = new Vector2(0, 18);
        _navHoverPrompt.characterSpacing = 14;
    }

    // ── navigation ───────────────────────────────────────────────────────

    void ShowNav()
    {
        _homeView.SetActive(false);
        _traxView.SetActive(false);
        if (_projectsView != null) _projectsView.SetActive(false);
        if (_inst != null) _inst.Stop();
        SyncPlayButton();
        _navView.SetActive(true);
        _navShownPhase = (ShuttleAutopilot.Phase)255;   // force a pane refresh
    }

    void OnAppTileClicked(string appName)
    {
        if (appName == "NAV") { ShowNav(); return; }
        ShowProjects();   // TRAX — the only other enabled app
    }

    // ── per-frame drive (open OR mirroring — the world screen shares this UI) ──

    void NavDrive()
    {
        if (_navView == null || !_navView.activeSelf) return;
        var pilot = ShuttleAutopilot.Instance;

        if (pilot != _navSubscribedPilot)
        {
            if (_navSubscribedPilot != null) _navSubscribedPilot.OnLaunchAborted -= OnNavLaunchAborted;
            _navSubscribedPilot = pilot;
            if (pilot != null) pilot.OnLaunchAborted += OnNavLaunchAborted;
        }

        var phase = pilot != null ? pilot.CurrentPhase : ShuttleAutopilot.Phase.Parked;
        if (phase != _navShownPhase)
        {
            _navShownPhase = phase;
            bool list = phase == ShuttleAutopilot.Phase.Parked;
            bool status = phase == ShuttleAutopilot.Phase.Countdown
                       || phase == ShuttleAutopilot.Phase.Liftoff
                       || phase == ShuttleAutopilot.Phase.Transit;
            bool hover = phase == ShuttleAutopilot.Phase.Hover
                      || phase == ShuttleAutopilot.Phase.Landing;
            _navListPane.SetActive(list);
            _navStatusPane.SetActive(status);
            _navHoverPane.SetActive(hover);
            if (list) { _navListBuiltFor = null; }   // "YOU ARE HERE" may have moved
        }

        if (pilot == null) return;

        switch (phase)
        {
            case ShuttleAutopilot.Phase.Parked:
                NavRefreshList(pilot);
                if (_navToastLabel != null)
                    _navToastLabel.text = Time.unscaledTime < _navToastUntil2 ? _navToastLabel.text : "";
                break;

            case ShuttleAutopilot.Phase.Countdown:
            {
                int n = Mathf.CeilToInt(pilot.CountdownRemaining);
                SetTextIfChanged(_navStatusBig, "TAKING OFF IN " + n);
                SetTextIfChanged(_navStatusSub, pilot.TargetBody != null ? "DESTINATION: " + pilot.TargetBody.bodyName : "");
                SetProgress(1f - pilot.CountdownRemaining / ShuttleAutopilot.CountdownSeconds);
                break;
            }

            case ShuttleAutopilot.Phase.Liftoff:
                SetTextIfChanged(_navStatusBig, "LIFTOFF");
                SetTextIfChanged(_navStatusSub, pilot.TargetBody != null ? "DESTINATION: " + pilot.TargetBody.bodyName : "");
                SetProgress(0f);
                break;

            case ShuttleAutopilot.Phase.Transit:
                SetTextIfChanged(_navStatusBig, pilot.TargetBody != null ? "EN ROUTE TO " + pilot.TargetBody.bodyName.ToUpperInvariant() : "EN ROUTE");
                SetTextIfChanged(_navStatusSub, "AUTOPILOT ENGAGED");
                SetProgress(pilot.TransitProgress);
                break;

            case ShuttleAutopilot.Phase.Hover:
            case ShuttleAutopilot.Phase.Landing:
            {
                if (pilot.LandingCamera != null && _navFeed.texture != pilot.LandingCamera.Texture)
                    _navFeed.texture = pilot.LandingCamera.Texture;
                bool landing = phase == ShuttleAutopilot.Phase.Landing;
                Color border = landing ? Warn : (pilot.LandingValid ? new Color(0.2f, 1f, 0.35f) : new Color(1f, 0.2f, 0.15f));
                if (Time.unscaledTime < _navRedFlashUntil) border = Color.red;
                if (_navFeedBorder.color != border) _navFeedBorder.color = border;
                string prompt;
                if (landing) prompt = "LANDING…";
                else if (Time.unscaledTime < _navRedFlashUntil) prompt = "NO CLEAR GROUND";
                else if (!ShuttleSync.LocalCanSteer) prompt = "PILOT: " + ShuttleSync.PilotName;
                else prompt = "WASD POSITION · Q/E YAW · SPACE LAND";
                SetTextIfChanged(_navHoverPrompt, prompt);
                SetTextIfChanged(_navAltReadout, "ALT " + Mathf.RoundToInt(pilot.CurrentGroundAltitude) + " M");
                break;
            }
        }
    }

    // Hover steering — only while THIS player has the NAV app open fullscreen
    // (the modal flag already keeps these keys away from the player's feet).
    void NavInput()
    {
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null) return;
        if (pilot.CurrentPhase != ShuttleAutopilot.Phase.Hover) return;

        // D-3: first NAV user during HOVER owns the stick; everyone else
        // watches the same feed with a "PILOT:" chip instead of the prompt.
        ShuttleSync.TryClaimPilot();
        if (!ShuttleSync.LocalCanSteer) return;

        Vector2 move = new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
        float yaw = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        pilot.SetPilotInput(move, yaw);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!pilot.RequestLand())
                _navRedFlashUntil = Time.unscaledTime + 0.8f;   // red flash + "NO CLEAR GROUND"
        }
    }

    // ── list pane ────────────────────────────────────────────────────────

    void NavRefreshList(ShuttleAutopilot pilot)
    {
        string here = pilot.CurrentBody != null ? pilot.CurrentBody.bodyName : "";
        if (_navListBuiltFor != here)
        {
            _navListBuiltFor = here;
            NavRebuildTiles(pilot, here);
        }

        bool canTravel = !string.IsNullOrEmpty(_navSelected) && _navSelected != here;
        _navTravelLabel.color = canTravel ? Ink : Locked;
        _navTravelBg.color = canTravel ? PanelHi : Panel;
    }

    void NavRebuildTiles(ShuttleAutopilot pilot, string here)
    {
        foreach (Transform child in _navTileRow) Destroy(child.gameObject);
        _navTiles.Clear();

        var planets = ShuttleAutopilot.LandablePlanets();
        if (planets.Count == 0) return;
        if (!string.IsNullOrEmpty(_navSelected))
        {
            bool stillThere = false;
            foreach (var p in planets) if (p.bodyName == _navSelected) { stillThere = true; break; }
            if (!stillThere || _navSelected == here) _navSelected = "";
        }

        // Grid: up to 4 per row, centred — same hand-packed style as the home row.
        const float cell = 250f, cellH = 120f, gap = 22f;
        int perRow = Mathf.Min(4, planets.Count);
        for (int i = 0; i < planets.Count; i++)
        {
            var body = planets[i];
            bool isHere = body.bodyName == here;
            int row = i / perRow, col = i % perRow;
            int inThisRow = Mathf.Min(perRow, planets.Count - row * perRow);
            float rowW = inThisRow * cell + (inThisRow - 1) * gap;

            var frame = MakePanel(_navTileRow, "Planet_" + body.bodyName, isHere ? Hex("0a1418ff") : Panel);
            var rt = frame.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1); rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(cell, cellH);
            rt.anchoredPosition = new Vector2(-rowW * 0.5f + cell * 0.5f + col * (cell + gap),
                                              -row * (cellH + gap));
            Outline(frame.transform, isHere ? Hex("141d21ff") : Grid);

            var label = MakeText(rt, "Name", body.bodyName.ToUpperInvariant(), 20,
                                 isHere ? Locked : Ink, TextAlignmentOptions.Center);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0, 0.5f); lrt.anchorMax = new Vector2(1, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(0, 30);
            lrt.anchoredPosition = new Vector2(0, isHere ? 14 : 0);
            label.characterSpacing = 10;

            if (isHere)
            {
                var hereLbl = MakeText(rt, "Here", "YOU ARE HERE", 12, Locked, TextAlignmentOptions.Center);
                var hrt2 = hereLbl.rectTransform;
                hrt2.anchorMin = new Vector2(0, 0.5f); hrt2.anchorMax = new Vector2(1, 0.5f);
                hrt2.pivot = new Vector2(0.5f, 0.5f);
                hrt2.sizeDelta = new Vector2(0, 20);
                hrt2.anchoredPosition = new Vector2(0, -16);
                hereLbl.characterSpacing = 16;
            }
            else
            {
                var tile = new NavTile { body = body.bodyName, frame = frame, label = label, here = false };
                _navTiles.Add(tile);
                var btn = frame.gameObject.AddComponent<Button>();
                btn.targetGraphic = frame;
                var cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
                cb.pressedColor = new Color(2f, 2f, 2f, 1f);
                btn.colors = cb;
                string captured = body.bodyName;
                btn.onClick.AddListener(() => OnNavPlanetClicked(captured));
            }
        }
        NavApplySelection();
    }

    void OnNavPlanetClicked(string bodyName)
    {
        _navSelected = bodyName;
        NavApplySelection();
    }

    void NavApplySelection()
    {
        foreach (var t in _navTiles)
        {
            bool sel = t.body == _navSelected;
            t.frame.color = sel ? PanelHi : Panel;
            t.label.color = sel ? Accent : Ink;
        }
    }

    void OnNavTravelClicked()
    {
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null || string.IsNullOrEmpty(_navSelected)) return;
        if (!pilot.RequestTravelByName(_navSelected))
            NavToast("TRAVEL UNAVAILABLE");
    }

    void OnNavLaunchAborted()
    {
        NavToast("NO CREW ABOARD — LAUNCH CANCELLED");   // D-1; selection kept
    }

    void NavToast(string msg)
    {
        if (_navToastLabel == null) return;
        _navToastLabel.text = msg;
        _navToastUntil2 = Time.unscaledTime + 4f;
    }

    // ── tiny helpers ─────────────────────────────────────────────────────

    void SetProgress(float t)
    {
        if (_navProgressFill == null) return;
        var rt = _navProgressFill.rectTransform;
        var max = rt.anchorMax;
        max.x = Mathf.Clamp01(t);
        if (rt.anchorMax != max) rt.anchorMax = max;
    }

    static void SetTextIfChanged(TextMeshProUGUI label, string text)
    {
        if (label != null && label.text != text) label.text = text;
    }
}
