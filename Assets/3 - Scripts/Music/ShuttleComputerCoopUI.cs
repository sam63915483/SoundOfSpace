using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The co-op half of the shuttle computer: your partner's pointer moving on
/// your screen, their edits landing under your hands, and one transport driving
/// both sets of speakers.
///
/// ── ONE COMPUTER, ONE SCREEN ─────────────────────────────────────────────
/// The machine has a single screen and edits a single section at a time, so
/// that is what replicates. Whatever it is showing, it is showing to both of
/// you: ESC goes back for both, opening a project puts you both in it, and
/// selecting section C means you are both editing C.
///
/// Reconciled, not replayed. Every frame this compares what the screen is
/// ACTUALLY showing against the last shared snapshot; a difference either gets
/// published (you changed it) or applied (they did). Nothing hooks individual
/// buttons, which is the point — a navigation path nobody remembered to hook
/// still replicates, and a dropped packet corrects on the next comparison
/// rather than leaving the two of you looking at different things.
///
/// ── Why a ghost pointer rather than a second real cursor ─────────────────
/// The computer uses the actual OS cursor over a screen-space canvas — there is
/// no virtual pointer to hand out a second copy of. So the partner's is drawn:
/// a small arrow tinted with their suit colour and labelled with their name,
/// positioned from a NORMALISED coordinate inside the virtual screen rect. That
/// rect is a fixed 1500×940 on both machines whatever the window is doing, so
/// the ghost lands on the same knob they are actually touching.
///
/// Two cursors is the ONE thing that stays per-player, because there genuinely
/// are two people. That plus the volume slider, which is about this player's
/// ears rather than about the machine.
///
/// ── Free-for-all, last write wins (Sam's call) ───────────────────────────
/// Nothing locks anything. Both of you can turn the same knob; the later change
/// sticks. Two people working one track will get in each other's way, and that
/// is the intended pressure — agree who is driving, or split up and let one of
/// you make tapes while the other sells them.
///
/// Partial of ShuttleComputerUI so it shares the palette and the UGUI helpers.
/// </summary>
public partial class ShuttleComputerUI
{
    /// The virtual screen rect — the 1500×940 area inside the bezel. Cursor
    /// positions are normalised into this, never into the window, so two
    /// differently-sized windows agree on where a pointer is.
    RectTransform _screenRT;

    RectTransform _ghostRT;
    Image _ghostArrow;
    Image _ghostOutline;
    Image _ghostChip;
    TextMeshProUGUI _ghostLabel;

    /// Pointer height in virtual screen units. Sized against the 1500×940
    /// screen, not the window, so it looks the same fullscreen and on the mesh.
    const float PointerSize = 26f;

    /// <summary>
    /// Where the ghost is being drawn, chasing where it was last heard to be.
    ///
    /// Packets arrive at a fixed rate and frames do not, so snapping straight to
    /// each one makes a perfectly smooth hand look like a slideshow — the
    /// cursor visibly steps. Smoothing between them costs nothing and is the
    /// difference between "the netcode is bad" and "somebody is moving a mouse".
    /// </summary>
    Vector2 _ghostShown;
    bool _ghostPlaced;

    /// How fast the drawn position converges on the received one. Framerate
    /// independent (exponential), tuned to settle inside about two packets so
    /// it reads as smooth without feeling like it lags behind their clicks.
    const float GhostFollow = 28f;

    float _clickFlash;
    RectTransform _elsewhereRT;
    TextMeshProUGUI _elsewhereLabel;

    int _songRevSeen;
    int _transportRevSeen;
    int _dialRevSeen;
    byte _lastPresenceView = TraxSessionSync.ViewNone;

    /// <summary>
    /// Presence is re-stated every couple of seconds while the computer is open,
    /// the same way StasisDoorSync re-sends the door.
    ///
    /// Cursor packets carry the view, so a late joiner learns somebody is HERE
    /// within a twelfth of a second — but the name and the suit colour only ride
    /// presence. Without a heartbeat, joining while your partner is already
    /// sitting at the terminal gives you an anonymous, default-tinted ghost
    /// until they happen to change screens.
    /// </summary>
    const float PresenceHeartbeat = 2f;
    float _nextPresenceAt;

    /// <summary>
    /// What the CANVAS is showing, whether or not this player is looking at it.
    ///
    /// Separate from CurrentViewId because the canvas keeps running with the
    /// fullscreen UI closed — that is what draws the partner's session onto the
    /// world monitor — so "what is on screen" and "am I the one at the machine"
    /// stopped being the same question.
    /// </summary>
    byte CanvasViewId
    {
        get
        {
            if (_canvas == null) return TraxSessionSync.ViewNone;
            if (_traxView != null && _traxView.activeSelf) return TraxSessionSync.ViewArranger;
            if (ProjectsOpen)
                return _shelfPane != null && _shelfPane.activeSelf
                     ? TraxSessionSync.ViewShelf : TraxSessionSync.ViewProjectsMenu;
            return TraxSessionSync.ViewHome;
        }
    }

    /// What to TELL people we are looking at. ViewNone unless this player has
    /// actually walked up and opened it — a machine quietly mirroring a
    /// partner's session must never advertise itself as somebody sitting there.
    byte CurrentViewId => _open ? CanvasViewId : TraxSessionSync.ViewNone;

    /// <summary>
    /// Everything about what the machine is showing right now, read straight off
    /// the live UI rather than off a shadow copy — so it cannot drift from what
    /// is actually on screen, whatever route got it there.
    /// </summary>
    TraxSessionSync.Screen ReadScreen()
    {
        return new TraxSessionSync.Screen
        {
            view      = CanvasViewId,
            projectId = _project != null ? _project.id : "",
            section   = _sel,
            saveOpen  = SaveOpen,
            saveText  = SaveOpen && _saveField != null ? _saveField.text : "",
            printOpen = PrintOpen,
        };
    }

    TraxSessionSync.Screen _lastScreen;
    int _screenRevSeen;
    bool _screenPrimed;

    void BuildRemoteCursor(RectTransform parent)
    {
        // ── the pointer ──
        //
        // Two copies of the same arrow: a dark one behind, scaled up from the
        // tip, and the tinted one in front. That is what gives it a crisp
        // outline against a CRT full of green text — a single flat shape
        // disappears into the interface it is pointing at.
        _ghostRT = MakeRect(parent, "PartnerCursor");
        _ghostRT.anchorMin = _ghostRT.anchorMax = new Vector2(0.5f, 0.5f);
        _ghostRT.pivot = new Vector2(0f, 1f);           // the tip
        _ghostRT.sizeDelta = new Vector2(PointerSize, PointerSize);

        _ghostOutline = MakeSprite(_ghostRT, "Outline", TraxUISprites.Pointer, Hex("04120ecc"));
        var ort = _ghostOutline.rectTransform;
        ort.anchorMin = ort.anchorMax = new Vector2(0f, 1f);
        ort.pivot = new Vector2(0f, 1f);
        ort.anchoredPosition = Vector2.zero;
        ort.sizeDelta = new Vector2(PointerSize, PointerSize);
        ort.localScale = new Vector3(1.22f, 1.22f, 1f);

        _ghostArrow = MakeSprite(_ghostRT, "Arrow", TraxUISprites.Pointer, Ink);
        var art = _ghostArrow.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(0f, 1f);
        art.pivot = new Vector2(0f, 1f);
        art.anchoredPosition = Vector2.zero;
        art.sizeDelta = new Vector2(PointerSize, PointerSize);

        // Name on a dark pill, so it stays readable whatever it is over.
        _ghostChip = MakePanel(_ghostRT, "NameChip", Hex("04120ee0"));
        var crt = _ghostChip.rectTransform;
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(0f, 1f);
        crt.anchoredPosition = new Vector2(PointerSize * 0.62f, -PointerSize * 0.68f);
        crt.sizeDelta = new Vector2(10, 15);            // width fitted to the name

        _ghostLabel = MakeText(_ghostChip.rectTransform, "Name", "", 10, Ink,
                               TextAlignmentOptions.Center);
        Stretch(_ghostLabel.rectTransform, 4, 4, 1, 1);
        _ghostLabel.characterSpacing = 6;

        _ghostRT.gameObject.SetActive(false);

        // ── the "they're on another screen" chip ──
        // A pointer would be a lie when they are browsing the shelf and you are
        // on the arranger, but knowing they are HERE still matters — otherwise
        // a project appearing on the shelf has no author.
        var chip = MakePanel(parent, "PartnerChip", PanelHi);
        _elsewhereRT = chip.rectTransform;
        Box(_elsewhereRT, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(260, 20));
        _elsewhereLabel = MakeText(_elsewhereRT, "Label", "", 11, InkGhost, TextAlignmentOptions.Center);
        Stretch(_elsewhereLabel.rectTransform, 0, 0, 0, 0);
        _elsewhereRT.gameObject.SetActive(false);
    }

    /// <summary>
    /// One pass per frame while the computer is open: say where our mouse is,
    /// draw where theirs is, and take whatever they have sent us.
    ///
    /// Called from Update AFTER the local input handling, so an edit made this
    /// frame is already in _song when the publish below reads it.
    /// </summary>
    /// <summary>
    /// Runs every frame, open or not.
    ///
    /// APPLYING is unconditional: a partner's screen, song and transport have to
    /// keep landing on this machine even with the fullscreen UI closed, because
    /// that is what the world monitor is rendering and what the console is
    /// playing.
    ///
    /// PUBLISHING is not. A machine quietly mirroring someone else's session
    /// has no cursor to report and no navigation of its own, and saying
    /// otherwise would have it announce itself as a second person at the
    /// terminal.
    /// </summary>
    void CoopUpdate()
    {
        ApplyIncoming();

        if (_open)
        {
            PublishLocalCursor();
            ReconcileScreen();
        }

        DrawPartner();
    }

    /// <summary>
    /// Keep the one screen the same on both machines.
    ///
    /// Inbound first (ApplyIncoming ran just above), then this: if what the
    /// screen is showing no longer matches the last agreed snapshot, THIS player
    /// moved it, so tell the other one. Comparing live UI state rather than
    /// hooking navigation means every route is covered — buttons, ESC, the
    /// section strip, a dialog closing itself — including any added later.
    /// </summary>
    void ReconcileScreen()
    {
        var now = ReadScreen();
        if (_screenPrimed && now.Same(_lastScreen))
        {
            // Nothing moved. The host re-states it anyway on a slow beat, which
            // is what lets a partner walking up mid-session adopt it.
            TraxSessionSync.HeartbeatScreen(now);
            return;
        }

        _lastScreen = now;
        // The first pass after opening only records where we are. Publishing it
        // would shove a partner already using the machine back to whatever
        // screen we happened to resume on — and if they ARE using it, the branch
        // in ApplyIncoming has already moved us to theirs instead.
        if (!_screenPrimed) { _screenPrimed = true; return; }

        TraxSessionSync.PublishScreen(now);

        // Send the song with any move onto the arranger. It normally ships on
        // an EDIT, which leaves a partner mirroring a project you opened but
        // haven't touched with nothing to play or draw. Coalesced like every
        // other song publish, so navigating around costs at most one extra
        // message every quarter second.
        if (now.view == TraxSessionSync.ViewArranger && _song != null)
            TraxSessionSync.PublishSong(_song);
    }

    /// <summary>
    /// Move this screen to match the shared one.
    ///
    /// Ordered outside-in — view, then project, then section, then dialogs —
    /// because each step can invalidate the ones after it: opening a project
    /// resets the section, and changing view closes the dialogs.
    ///
    /// Every step is guarded by "is it already like that?", so a snapshot that
    /// agrees with us costs nothing and re-sending is always safe. That is what
    /// lets a late joiner simply adopt the machine as it stands.
    /// </summary>
    void ApplyScreen(TraxSessionSync.Screen s)
    {
        if (s.view == TraxSessionSync.ViewNone) return;   // they got up; we keep ours

        // ── the project being edited ──
        // Checked before the view, because opening one navigates by itself.
        string mine = _project != null ? _project.id : "";
        bool wantArranger = s.view == TraxSessionSync.ViewArranger;
        if (wantArranger && (s.projectId ?? "") != mine)
        {
            if (string.IsNullOrEmpty(s.projectId))
            {
                // They started something new. The blank song arrives separately;
                // this just puts us in the right place to receive it.
                _project = null;
                _savedSongId = 0;
                ShowTrax();
            }
            else
            {
                var rec = TraxLibrary.FindById(s.projectId);
                if (rec != null) OpenProject(rec);
            }
        }

        // ── the view ──
        // ⚠️ CanvasViewId, not CurrentViewId. CurrentViewId reports ViewNone
        // whenever this player is not the one at the machine, so on a mirroring
        // screen this test would be true every single time — and the host
        // re-states the screen every 1.5s, which would mean rebuilding the whole
        // shelf, closing any open save dialog and stopping the music forty times
        // a minute.
        if (CanvasViewId != s.view)
        {
            switch (s.view)
            {
                case TraxSessionSync.ViewHome:         _inst.Stop(); ShowHome(); break;
                case TraxSessionSync.ViewProjectsMenu: ShowProjects(); break;
                case TraxSessionSync.ViewShelf:        ShowProjects(); ShowShelfPane(); break;
                case TraxSessionSync.ViewArranger:     ShowTrax(); break;
            }
        }

        // ── the section, one at a time by design ──
        if (s.view == TraxSessionSync.ViewArranger && _song != null
            && s.section != _sel && s.section >= 0 && s.section < _song.sections.Count)
            SelectSection(s.section);

        // ── the dialogs ──
        if (s.printOpen != PrintOpen)
        {
            if (s.printOpen) OpenPrint(); else ClosePrint();
        }
        if (s.saveOpen != SaveOpen)
        {
            if (s.saveOpen) OpenSaveDialog(); else CloseSaveDialog();
        }
        // What they are typing, as they type it. Assigning .text fires
        // onValueChanged, which is why the whole apply runs under ApplyingRemote.
        if (s.saveOpen && SaveOpen && _saveField != null && _saveField.text != (s.saveText ?? ""))
        {
            _saveField.text = s.saveText ?? "";
            _saveField.caretPosition = _saveField.text.Length;
            RefreshSaveNote();
        }
    }

    void PublishLocalCursor()
    {
        byte view = CurrentViewId;
        if (view != _lastPresenceView || Time.unscaledTime >= _nextPresenceAt)
        {
            _lastPresenceView = view;
            _nextPresenceAt = Time.unscaledTime + PresenceHeartbeat;
            TraxSessionSync.PublishPresence(true, view);
        }

        if (_screenRT == null) return;
        // The canvas is ScreenSpaceOverlay, so the camera argument is null —
        // passing one would offset the result by the camera's rect.
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _screenRT, Input.mousePosition, null, out Vector2 local)) return;

        Rect r = _screenRT.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        var normalized = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        TraxSessionSync.PublishCursor(normalized, view, Input.GetMouseButton(0));
    }

    void ApplyIncoming()
    {
        // ⚠️ ApplyingRemote around every one of these: applying an edit runs the
        // same code path a local edit runs, which would publish it straight back
        // out and loop forever between the two machines.
        if (TraxSessionSync.IncomingSongRev != _songRevSeen)
        {
            _songRevSeen = TraxSessionSync.IncomingSongRev;
            var song = TraxSessionSync.IncomingSong;
            if (song != null && song.sections.Count > 0)
            {
                TraxSessionSync.ApplyingRemote = true;
                try { ApplyRemoteSong(song); }
                finally { TraxSessionSync.ApplyingRemote = false; }
            }
        }

        // Before the song, so a stale whole-song snapshot arriving in the same
        // frame as a fresher dial value doesn't undo it for a beat.
        if (TraxSessionSync.IncomingDialRev != _dialRevSeen)
        {
            _dialRevSeen = TraxSessionSync.IncomingDialRev;
            TraxSessionSync.ApplyingRemote = true;
            try { ApplyRemoteDial(TraxSessionSync.IncomingDialIndex,
                                  TraxSessionSync.IncomingDialValue); }
            finally { TraxSessionSync.ApplyingRemote = false; }
        }

        if (TraxSessionSync.IncomingTransportRev != _transportRevSeen)
        {
            _transportRevSeen = TraxSessionSync.IncomingTransportRev;
            TraxSessionSync.ApplyingRemote = true;
            try { ApplyRemoteTransport(TraxSessionSync.IncomingTransportMode,
                                       TraxSessionSync.IncomingTransportStep); }
            finally { TraxSessionSync.ApplyingRemote = false; }
        }

        if (TraxSessionSync.IncomingScreenRev != _screenRevSeen)
        {
            _screenRevSeen = TraxSessionSync.IncomingScreenRev;
            var want = TraxSessionSync.IncomingScreen;
            TraxSessionSync.ApplyingRemote = true;
            try { ApplyScreen(want); }
            finally { TraxSessionSync.ApplyingRemote = false; }
            // Record where we ended up, so ReconcileScreen reads this as
            // "already agreed" instead of publishing their own change back.
            _lastScreen = ReadScreen();
            _screenPrimed = true;
        }
    }

    /// <summary>
    /// Take their arrangement wholesale. Last write wins, so there is nothing to
    /// merge.
    ///
    /// The selection is only CLAMPED here, not chosen — which section is being
    /// edited is part of the shared screen state and arrives through
    /// ApplyScreen. The clamp exists for the one case that outruns it: they
    /// deleted the section we were sitting on, and the song lands before the
    /// screen snapshot that moves us off it.
    /// </summary>
    void ApplyRemoteSong(TraxSong song)
    {
        if (_song == null) { ResetSong(song); return; }

        bool structural = song.sections.Count != _song.sections.Count;
        _song = song;
        _sel = Mathf.Clamp(_sel, 0, _song.sections.Count - 1);

        // Reference-share the selected section with the instrument, exactly as
        // SelectSection does, so the local dirty checks keep converging.
        _inst.LoadTrack(_song.sections[_sel].track);
        _song.sections[_sel].track = _inst.Track;

        // The compiled song is stale by definition; recompile now if it is
        // audible so the change is heard on the bar rather than the next press.
        _songStale = true;
        if (_inst.IsPlayingSong) { _inst.SetSong(_song); _songStale = false; }

        if (structural) RebuildArranger();
        RefreshAllControls();

        // A play we could not honour because there was no song yet. Now there is.
        if (_wantsPlay && !_inst.IsPlayingSong)
        {
            _wantsPlay = false;
            EnsureSongFresh();
            _inst.PlaySong();
            if (_pendingPlayStep > 0) _inst.SeekSong(_pendingPlayStep);
            SyncPlayButton();
        }
    }

    /// A PLAY that arrived before the song did — see ApplyRemoteTransport.
    bool _wantsPlay;
    int _pendingPlayStep;

    /// <summary>
    /// One knob, moved by the other player. Runs the same path a local turn
    /// does — engine, readouts, and the knob widget itself — so the dial ends up
    /// exactly where a local drag would have left it, and the sound changes with
    /// the picture.
    ///
    /// SetSilent on the widget rather than driving it through its own callback:
    /// the callback is what publishes, and echoing a partner's turn back at them
    /// is a loop.
    /// </summary>
    void ApplyRemoteDial(int index, float value)
    {
        if (_inst == null || index < 0) return;

        for (int i = 0; i < _knobs.Count; i++)
            if (_knobs[i] != null && _knobs[i].DialIndex == index) { _knobs[i].SetSilent(value); break; }

        OnKnobChanged(index, value);
    }

    void ApplyRemoteTransport(byte mode, int step)
    {
        if (_inst == null) return;
        switch (mode)
        {
            case TraxSessionSync.TransportStop:
                _wantsPlay = false;
                _inst.Stop();
                ClearPlayhead();
                _lastStepShown = -1;
                break;

            case TraxSessionSync.TransportPlaySong:
                _inst.Stop();
                ClearPlayhead();
                // ⚠️ A machine that has only ever MIRRORED the computer may not
                // have the song yet: it ships on an edit, so a partner who
                // opened a fresh project and pressed play immediately has sent
                // nothing to play. Remember the request and honour it the moment
                // the song lands, or the monitor shows a running transport with
                // no sound and no way back except a stop/play cycle.
                if (_song == null) { _pendingPlayStep = Mathf.Max(0, step); _wantsPlay = true; break; }
                EnsureSongFresh();
                _inst.PlaySong();
                // Start where they started, so the two machines are in the same
                // bar rather than merely both playing.
                if (step > 0) _inst.SeekSong(step);
                break;

            case TraxSessionSync.TransportPlayLoop:
                _inst.Stop();
                ClearPlayhead();
                _inst.Play();
                break;

            case TraxSessionSync.TransportSeek:
                if (_songStale && _inst.IsPlayingSong) EnsureSongFresh();
                _inst.SeekSong(step);
                UpdateArrPlayhead();
                break;
        }
        SyncPlayButton();
    }

    void DrawPartner()
    {
        bool here = TraxSessionSync.RemoteOpen;
        byte theirView = TraxSessionSync.RemoteView;
        // Against the CANVAS view, not this player's — with the UI closed the
        // canvas is mirroring their session onto the world monitor, and their
        // pointer moving across it is most of what makes that worth watching.
        //
        // With the screen shared these agree almost always; the mismatch window
        // is the fraction of a second between one of you navigating and the
        // other's screen catching up, and hiding the pointer through it stops
        // it appearing to point at the wrong thing.
        bool sameScreen = here && theirView == CanvasViewId && theirView != TraxSessionSync.ViewNone;

        if (_ghostRT != null && _ghostRT.gameObject.activeSelf != sameScreen)
            _ghostRT.gameObject.SetActive(sameScreen);

        bool chip = here && !sameScreen;
        if (_elsewhereRT != null && _elsewhereRT.gameObject.activeSelf != chip)
            _elsewhereRT.gameObject.SetActive(chip);

        if (chip && _elsewhereLabel != null)
            _elsewhereLabel.text = (TraxSessionSync.RemoteName ?? "SOMEONE").ToUpperInvariant()
                                 + " IS " + ViewWord(theirView);

        if (!sameScreen || _ghostRT == null || _screenRT == null) { _ghostPlaced = false; return; }

        Rect r = _screenRT.rect;
        Vector2 n = TraxSessionSync.RemoteCursor;
        var target = new Vector2(r.xMin + n.x * r.width, r.yMin + n.y * r.height);

        // Snap the first time it appears — easing in from wherever it was last
        // seen would send it sliding across the screen — then chase.
        if (!_ghostPlaced) { _ghostShown = target; _ghostPlaced = true; }
        else
        {
            float k = 1f - Mathf.Exp(-GhostFollow * Time.unscaledDeltaTime);
            _ghostShown = Vector2.Lerp(_ghostShown, target, k);
        }
        _ghostRT.anchoredPosition = _ghostShown;

        // Their suit colour, so two people at one terminal are told apart the
        // same way they are told apart in the world.
        Color tint = SuitPalette.ColorAt(TraxSessionSync.RemoteSwatch);

        // A click flashes the pointer and gives it a small press — knowing they
        // TOUCHED something rather than hovered is most of what you are
        // watching for. Decays rather than snapping back, or a quick click is a
        // single frame nobody sees.
        _clickFlash = TraxSessionSync.RemoteClicking
            ? 1f
            : Mathf.MoveTowards(_clickFlash, 0f, Time.unscaledDeltaTime * 5f);

        Color lit = Color.Lerp(tint, Color.white, _clickFlash * 0.65f);
        if (_ghostArrow != null) _ghostArrow.color = lit;
        if (_ghostRT != null)
        {
            float squash = 1f - _clickFlash * 0.14f;
            _ghostRT.localScale = new Vector3(squash, squash, 1f);
        }

        if (_ghostLabel != null)
        {
            _ghostLabel.color = tint;
            string want = (TraxSessionSync.RemoteName ?? "").ToUpperInvariant();
            if (_ghostLabel.text != want)
            {
                _ghostLabel.text = want;
                // Fit the pill to the name rather than leaving a fixed slab with
                // a short name rattling around inside it.
                if (_ghostChip != null)
                    _ghostChip.rectTransform.sizeDelta =
                        new Vector2(_ghostLabel.preferredWidth + 10f, 15f);
            }
            if (_ghostChip != null) _ghostChip.gameObject.SetActive(want.Length > 0);
        }
    }

    static string ViewWord(byte view)
    {
        switch (view)
        {
            case TraxSessionSync.ViewArranger:     return "ON THE ARRANGER";
            case TraxSessionSync.ViewShelf:        return "BROWSING THE SHELF";
            case TraxSessionSync.ViewProjectsMenu: return "IN THE TRAX MENU";
            case TraxSessionSync.ViewHome:         return "AT THE DESKTOP";
            default:                               return "HERE";
        }
    }
}
