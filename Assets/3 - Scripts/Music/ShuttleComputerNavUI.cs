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

    // List pane — since 2026-09-09 this is the solar MAP plus a destination
    // list; everything it contains is built and driven in the partial file
    // ShuttleComputerNavMapUI.cs. The old grid of planet tiles is gone.
    string _navSelected = "";
    Image _navTravelBg;
    TextMeshProUGUI _navTravelLabel;
    TextMeshProUGUI _navFuelLabel;      // "FUEL 62%  ·  RANGE 8.6 KM"
    string _navFuelShown = null;
    TextMeshProUGUI _navToastLabel;
    float _navToastUntil2;

    // status pane
    TextMeshProUGUI _navStatusBig, _navStatusSub;
    Image _navProgressFill;
    RawImage _navEnRouteFeed;      // live cam behind the EN ROUTE status
    Image _navEnRouteScrim;

    // hover pane
    RawImage _navFeed;
    Image _navFeedBorder;

    /// Seconds before touchdown at which the landing feed goes dark — the
    /// last stretch is the lens ploughing into the terrain. Sam's number.
    const float FeedCutoffSeconds = 2f;
    TextMeshProUGUI _navHoverPrompt, _navAltReadout;
    float _navRedFlashUntil;

    ShuttleAutopilot.Phase _navShownPhase = (ShuttleAutopilot.Phase)255;
    ShuttleAutopilot _navSubscribedPilot;

    public bool NavOpen { get { return _navView != null && _navView.activeSelf; } }

    // While the autopilot hovers and this player is fullscreen on NAV, the pad
    // flies the shuttle: the virtual cursor is suppressed so the left stick
    // positions instead of pointing. Released on any other phase, when NAV or
    // the computer closes, and on destroy — it can never leak.
    const string HoverCursorToken = "nav-hover";
    bool _hoverTokenHeld;

    void SetHoverCursorSuppressed(bool on)
    {
        if (on == _hoverTokenHeld) return;
        _hoverTokenHeld = on;
        if (on) PadCursor.Suppress(HoverCursorToken);
        else    PadCursor.Release(HoverCursorToken);
    }

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

        BuildNavMapPane(view);          // partial: ShuttleComputerNavMapUI
        BuildNavStatusPane(view);
        BuildNavHoverPane(view);

        _navView.SetActive(false);
    }

    void BuildNavStatusPane(RectTransform parent)
    {
        var pane = MakeRect(parent, "NavStatusPane");
        Stretch(pane, 0, 0, 34, 0);
        _navStatusPane = pane.gameObject;

        // En-route camera feed (2026-08-28, Sam's ask): the whole status pane
        // background is a live view — top cam showing what we fly at, left
        // click flips to the bottom cam looking back. Built FIRST so every
        // label renders on top; a dark scrim keeps the text readable over a
        // bright sky.
        var feedGo = new GameObject("EnRouteFeed", typeof(RectTransform));
        feedGo.transform.SetParent(pane, false);
        _navEnRouteFeed = feedGo.AddComponent<RawImage>();
        _navEnRouteFeed.raycastTarget = false;
        _navEnRouteFeed.color = Color.white;
        Stretch((RectTransform)feedGo.transform, 8, 8, 8, 8);
        feedGo.SetActive(false);

        _navEnRouteScrim = MakePanel(pane, "EnRouteScrim", new Color(0f, 0f, 0f, 0.38f));
        _navEnRouteScrim.raycastTarget = false;
        Stretch(_navEnRouteScrim.rectTransform, 8, 8, 8, 8);
        _navEnRouteScrim.gameObject.SetActive(false);

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

        // SKIP — countdown only (playtest 35, Sam's ask): jump the 10 s
        // launch timer. Hidden in every other status phase.
        var skip = MakePanel(pane, "SkipBtn", Panel);
        skip.raycastTarget = true;   // MakePanel defaults raycasts OFF
        _navSkipBg = skip;
        var skrt = skip.rectTransform;
        skrt.anchorMin = new Vector2(0.5f, 0.5f); skrt.anchorMax = new Vector2(0.5f, 0.5f);
        skrt.pivot = new Vector2(0.5f, 0.5f);
        skrt.sizeDelta = new Vector2(260, 52);
        skrt.anchoredPosition = new Vector2(0, -130);
        Outline(skip.transform, Grid);
        var skl = MakeText(skrt, "Label", "SKIP ▸", 22, Ink, TextAlignmentOptions.Center);
        Stretch(skl.rectTransform, 0, 0, 0, 0);
        skl.characterSpacing = 20;
        var skb = skip.gameObject.AddComponent<Button>();
        skb.targetGraphic = skip;
        var skc = skb.colors;
        skc.normalColor = Color.white;
        skc.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        skc.pressedColor = new Color(2f, 2f, 2f, 1f);
        skb.colors = skc;
        skb.onClick.AddListener(() => { ShuttleAutopilot.Instance?.SkipCountdown(); });
        skip.gameObject.SetActive(false);
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
            if (list) _navNextTextAt = 0f;          // "YOU ARE HERE" may have moved
        }

        if (pilot == null) return;

        // SKIP lives only on the countdown screen.
        if (phase != ShuttleAutopilot.Phase.Countdown && _navSkipBg != null && _navSkipBg.gameObject.activeSelf)
            _navSkipBg.gameObject.SetActive(false);

        switch (phase)
        {
            case ShuttleAutopilot.Phase.Parked:
                NavMapDrive(pilot);                // partial: ShuttleComputerNavMapUI
                if (_navToastLabel != null)
                    _navToastLabel.text = Time.unscaledTime < _navToastUntil2 ? _navToastLabel.text : "";
                break;

            case ShuttleAutopilot.Phase.Countdown:
            {
                int n = Mathf.CeilToInt(pilot.CountdownRemaining);
                SetTextIfChanged(_navStatusBig, "TAKING OFF IN " + n);
                SetTextIfChanged(_navStatusSub, pilot.TargetBody != null ? "DESTINATION: " + pilot.TargetBody.bodyName : "");
                SetProgress(1f - pilot.CountdownRemaining / ShuttleAutopilot.CountdownSeconds);
                if (_navSkipBg != null && !_navSkipBg.gameObject.activeSelf && !ShuttleAutopilot.ClientDriven)
                    _navSkipBg.gameObject.SetActive(true);
                HideEnRouteFeed();
                break;
            }

            case ShuttleAutopilot.Phase.Liftoff:
                SetTextIfChanged(_navStatusBig, "LIFTOFF");
                SetTextIfChanged(_navStatusSub, pilot.TargetBody != null ? "DESTINATION: " + pilot.TargetBody.bodyName : "");
                SetProgress(0f);
                DriveEnRouteFeed(pilot);
                break;

            case ShuttleAutopilot.Phase.Transit:
            {
                DriveEnRouteFeed(pilot);
                SetTextIfChanged(_navStatusBig, pilot.TargetBody != null ? "EN ROUTE TO " + pilot.TargetBody.bodyName.ToUpperInvariant() : "EN ROUTE");
                // Rounded to 5 m/s so the readout counts up cleanly instead of
                // flickering every frame — the build-up/brake is the point.
                // Concat only when the value CHANGES: the old per-frame string
                // build was steady GC pressure through the whole flight.
                int vel = Mathf.RoundToInt(pilot.CurrentSpeed / 5f) * 5;
                if (vel != _lastVelShown)
                {
                    _lastVelShown = vel;
                    SetTextIfChanged(_navStatusSub, "AUTOPILOT ENGAGED · VEL " + vel + " M/S");
                }
                SetProgress(pilot.TransitProgress);
                break;
            }

            case ShuttleAutopilot.Phase.Hover:
            case ShuttleAutopilot.Phase.Landing:
            {
                bool landing = phase == ShuttleAutopilot.Phase.Landing;
                // Cut the camera feed for the final stretch of the descent
                // (Sam, 2026-08-30): at touchdown the lens ends up in or under
                // the terrain, and a live shot of the inside of a planet reads
                // as broken. The feed goes dark, the prompt says TOUCHDOWN, and
                // — because the mirror freezes on the last live frame once the
                // phase hits Parked — the cockpit monitor is left holding this
                // clean screen instead of an underground one.
                bool feedCut = landing && pilot.LandingSecondsRemaining <= FeedCutoffSeconds;
                if (_navFeed.gameObject.activeSelf == feedCut)
                    _navFeed.gameObject.SetActive(!feedCut);
                if (!feedCut && pilot.LandingCamera != null && _navFeed.texture != pilot.LandingCamera.Texture)
                    _navFeed.texture = pilot.LandingCamera.Texture;
                Color green = new Color(0.2f, 1f, 0.35f), red = new Color(1f, 0.2f, 0.15f);
                Color border = landing ? Warn : (pilot.LandingValid ? green : red);
                if (Time.unscaledTime < _navRedFlashUntil) border = Color.red;
                if (_navFeedBorder.color != border) _navFeedBorder.color = border;
                // Explicit go/no-go indicator (playtest 17, Sam's ask): the
                // prompt doubles as the landing light — green "LANDING ZONE
                // CLEAR" when the validity check passes, red guidance when not.
                string prompt;
                Color promptColor = Ink;
                if (feedCut) prompt = "TOUCHDOWN…";
                else if (landing) prompt = "LANDING…";
                else if (Time.unscaledTime < _navRedFlashUntil) { prompt = "● NO CLEAR GROUND"; promptColor = red; }
                else if (!ShuttleSync.LocalCanSteer) prompt = "PILOT: " + ShuttleSync.PilotName;
                // PromptGlyphs picks per input source: keyboard text, or the
                // pad's sprite glyphs (A / left stick / LB / RB).
                else if (pilot.LandingValid) { prompt = "● LANDING ZONE CLEAR · " + PromptGlyphs.Jump + " TO LAND"; promptColor = green; }
                else { prompt = "● NO LANDING ZONE · " + PromptGlyphs.Move + " POSITION · " + PromptGlyphs.RollLeft + "/" + PromptGlyphs.RollRight + " YAW"; promptColor = red; }
                SetTextIfChanged(_navHoverPrompt, prompt);
                if (_navHoverPrompt != null && _navHoverPrompt.color != promptColor)
                    _navHoverPrompt.color = promptColor;
                int altM = Mathf.RoundToInt(pilot.CurrentGroundAltitude);
                if (altM != _lastAltShown)
                {
                    _lastAltShown = altM;
                    SetTextIfChanged(_navAltReadout, "ALT " + altM + " M");
                }
                break;
            }
        }
    }

    // En-route live feed (2026-08-28, Sam's ask): the status pane background is
    // the transit camera, under the belly. It used to have a second camera on
    // the roof with a left click to switch — removed 2026-09-09 at Sam's
    // request; one feed, no switch.
    void DriveEnRouteFeed(ShuttleAutopilot pilot)
    {
        var cam = pilot.TransitCamera;
        bool show = cam != null;
        if (_navEnRouteFeed == null) return;
        if (_navEnRouteFeed.gameObject.activeSelf != show)
        {
            _navEnRouteFeed.gameObject.SetActive(show);
            _navEnRouteScrim.gameObject.SetActive(show);
        }
        if (!show) return;
        if (_navEnRouteFeed.texture != cam.Texture) _navEnRouteFeed.texture = cam.Texture;
    }

    void HideEnRouteFeed()
    {
        if (_navEnRouteFeed == null || !_navEnRouteFeed.gameObject.activeSelf) return;
        _navEnRouteFeed.gameObject.SetActive(false);
        _navEnRouteScrim.gameObject.SetActive(false);
    }

    // Hover steering — only while THIS player has the NAV app open fullscreen
    // (the modal flag already keeps these keys away from the player's feet).
    // Pad (Sam's picks, 2026-09-06): left stick = position, LB/RB = yaw (the
    // ship's roll bumpers), A = land. Raw reads on purpose: the cursor mute in
    // TutorialGate would otherwise zero the stick, and no tutorial ability
    // gates the shuttle.
    void NavInput()
    {
        var pilot = ShuttleAutopilot.Instance;
        NavMapInput();               // zoom / pan / pick, PARKED only — partial: NavMapUI
        bool hover = pilot != null && pilot.CurrentPhase == ShuttleAutopilot.Phase.Hover;
        SetHoverCursorSuppressed(hover);
        if (!hover) return;

        // D-3: first NAV user during HOVER owns the stick; everyone else
        // watches the same feed with a "PILOT:" chip instead of the prompt.
        ShuttleSync.TryClaimPilot();
        if (!ShuttleSync.LocalCanSteer) return;

        Vector2 stick = TutorialGate.LeftStickRaw();
        Vector2 move = new Vector2(
            Mathf.Clamp((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f) + stick.x, -1f, 1f),
            Mathf.Clamp((Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f) + stick.y, -1f, 1f));
        float yaw = ((Input.GetKey(KeyCode.E) || TutorialGate.PadHeld(TutorialGate.PadButton.RB)) ? 1f : 0f)
                  - ((Input.GetKey(KeyCode.Q) || TutorialGate.PadHeld(TutorialGate.PadButton.LB)) ? 1f : 0f);
        pilot.SetPilotInput(move, yaw);

        if (Input.GetKeyDown(KeyCode.Space) || TutorialGate.PadPressed(TutorialGate.PadButton.A))
        {
            if (!pilot.RequestLand())
                _navRedFlashUntil = Time.unscaledTime + 0.8f;   // red flash + "NO CLEAR GROUND"
        }
    }

    void OnNavTravelClicked()
    {
        var pilot = ShuttleAutopilot.Instance;
        if (pilot == null || string.IsNullOrEmpty(_navSelected)) return;

        // Say WHY, when the reason is fuel. "TRAVEL UNAVAILABLE" on a planet you
        // simply cannot afford reads like a bug.
        var tank = ShuttleFuel.Instance;
        if (tank != null && pilot.CurrentBody != null)
        {
            var target = NavBodyByName(_navSelected);       // partial: NavMapUI
            if (target != null)
            {
                float metres = pilot.JumpMetresTo(target);
                if (!tank.CanAfford(metres))
                {
                    NavToast($"NEED {tank.CostForMetres(metres):0} FUEL · HAVE {tank.Fuel:0}");
                    return;
                }
            }
        }

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

    // Change-gates for the per-frame readouts (playtest 23 GC hygiene) —
    // appended at the end per house serialization convention.
    int _lastVelShown = int.MinValue;
    int _lastAltShown = int.MinValue;
    Image _navSkipBg;
}
