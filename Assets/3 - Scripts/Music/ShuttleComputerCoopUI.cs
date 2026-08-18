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
    Image _ghostDot;
    TextMeshProUGUI _ghostLabel;
    RectTransform _elsewhereRT;
    TextMeshProUGUI _elsewhereLabel;

    int _songRevSeen;
    int _transportRevSeen;
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

    /// Which screen the computer is on, in the sync layer's vocabulary.
    byte CurrentViewId
    {
        get
        {
            if (!_open) return TraxSessionSync.ViewNone;
            if (_traxView != null && _traxView.activeSelf) return TraxSessionSync.ViewArranger;
            if (ProjectsOpen)
                return _shelfPane != null && _shelfPane.activeSelf
                     ? TraxSessionSync.ViewShelf : TraxSessionSync.ViewProjectsMenu;
            return TraxSessionSync.ViewHome;
        }
    }

    /// <summary>
    /// Everything about what the machine is showing right now, read straight off
    /// the live UI rather than off a shadow copy — so it cannot drift from what
    /// is actually on screen, whatever route got it there.
    /// </summary>
    TraxSessionSync.Screen ReadScreen()
    {
        return new TraxSessionSync.Screen
        {
            view      = CurrentViewId,
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
        _ghostRT = MakeRect(parent, "PartnerCursor");
        _ghostRT.anchorMin = _ghostRT.anchorMax = new Vector2(0.5f, 0.5f);
        _ghostRT.pivot = new Vector2(0.5f, 0.5f);
        _ghostRT.sizeDelta = new Vector2(120, 40);

        // A chunky two-part arrow: a solid wedge with a bright dot at the tip,
        // so it stays findable against the CRT noise without needing a sprite.
        _ghostArrow = MakePanel(_ghostRT, "Arrow", Ink);
        var art = _ghostArrow.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
        art.pivot = new Vector2(0.5f, 0.5f);
        art.sizeDelta = new Vector2(3, 16);
        art.anchoredPosition = new Vector2(0, -8);
        art.localRotation = Quaternion.Euler(0, 0, 28f);

        _ghostDot = MakePanel(_ghostRT, "Tip", Ink);
        var drt = _ghostDot.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(7, 7);
        drt.anchoredPosition = Vector2.zero;

        _ghostLabel = MakeText(_ghostRT, "Name", "", 11, Ink, TextAlignmentOptions.TopLeft);
        Box(_ghostLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 1),
            new Vector2(8, -14), new Vector2(140, 14));
        _ghostLabel.characterSpacing = 8;

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
    void CoopUpdate()
    {
        PublishLocalCursor();
        ApplyIncoming();
        ReconcileScreen();
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
        if (CurrentViewId != s.view)
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
    }

    void ApplyRemoteTransport(byte mode, int step)
    {
        if (_inst == null) return;
        switch (mode)
        {
            case TraxSessionSync.TransportStop:
                _inst.Stop();
                ClearPlayhead();
                _lastStepShown = -1;
                break;

            case TraxSessionSync.TransportPlaySong:
                _inst.Stop();
                ClearPlayhead();
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
        // With the screen shared these agree almost always; the mismatch window
        // is the fraction of a second between one of you navigating and the
        // other's screen catching up, and hiding the pointer through it stops
        // it appearing to point at the wrong thing.
        bool sameScreen = here && theirView == CurrentViewId && theirView != TraxSessionSync.ViewNone;

        if (_ghostRT != null && _ghostRT.gameObject.activeSelf != sameScreen)
            _ghostRT.gameObject.SetActive(sameScreen);

        bool chip = here && !sameScreen;
        if (_elsewhereRT != null && _elsewhereRT.gameObject.activeSelf != chip)
            _elsewhereRT.gameObject.SetActive(chip);

        if (chip && _elsewhereLabel != null)
            _elsewhereLabel.text = (TraxSessionSync.RemoteName ?? "SOMEONE").ToUpperInvariant()
                                 + " IS " + ViewWord(theirView);

        if (!sameScreen || _ghostRT == null || _screenRT == null) return;

        Rect r = _screenRT.rect;
        Vector2 n = TraxSessionSync.RemoteCursor;
        _ghostRT.anchoredPosition = new Vector2(r.xMin + n.x * r.width, r.yMin + n.y * r.height);

        // Their suit colour, so two people at one terminal are told apart the
        // same way they are told apart in the world.
        Color tint = SuitPalette.ColorAt(TraxSessionSync.RemoteSwatch);
        // A click flashes the pointer bright — the one moment where knowing
        // they touched something, rather than merely hovered, matters.
        Color lit = TraxSessionSync.RemoteClicking ? Color.Lerp(tint, Color.white, 0.6f) : tint;
        if (_ghostArrow != null) _ghostArrow.color = lit;
        if (_ghostDot != null) _ghostDot.color = lit;
        if (_ghostLabel != null)
        {
            _ghostLabel.color = tint;
            string want = (TraxSessionSync.RemoteName ?? "").ToUpperInvariant();
            if (_ghostLabel.text != want) _ghostLabel.text = want;
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
