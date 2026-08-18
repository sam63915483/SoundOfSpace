using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The co-op half of the shuttle computer: your partner's pointer moving on
/// your screen, their edits landing under your hands, and one transport driving
/// both sets of speakers.
///
/// ── Why a ghost pointer rather than a second real cursor ─────────────────
/// The computer uses the actual OS cursor over a screen-space canvas — there is
/// no virtual pointer to hand out a second copy of. So the partner's is drawn:
/// a small arrow tinted with their suit colour and labelled with their name,
/// positioned from a NORMALISED coordinate inside the virtual screen rect. That
/// rect is a fixed 1500×940 on both machines whatever the window is doing, so
/// the ghost lands on the same knob they are actually touching.
///
/// ── Free-for-all, last write wins (Sam's call) ───────────────────────────
/// Nothing here locks anything. Both of you can turn the same knob; the later
/// change is the one that sticks. What this file adds is only the ability to
/// SEE that happening, which is what makes the rule readable instead of
/// spooky.
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

    /// Which screen this player is looking at, in the sync layer's vocabulary.
    byte CurrentViewId
    {
        get
        {
            if (!_open) return TraxSessionSync.ViewNone;
            if (_traxView != null && _traxView.activeSelf) return TraxSessionSync.ViewArranger;
            if (ProjectsOpen) return TraxSessionSync.ViewProjects;
            return TraxSessionSync.ViewHome;
        }
    }

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
        DrawPartner();
    }

    void PublishLocalCursor()
    {
        byte view = CurrentViewId;
        if (view != _lastPresenceView)
        {
            _lastPresenceView = view;
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
    }

    /// <summary>
    /// Take their arrangement wholesale. Last write wins, so there is nothing to
    /// merge — but the SELECTED SECTION is deliberately preserved rather than
    /// reset to A. Being yanked to a different section every time your partner
    /// turns a knob would make the screen unusable, and which block you are
    /// looking at is a local concern, not part of the song.
    ///
    /// If they deleted the section you were on, the selection clamps to the last
    /// one rather than going out of range.
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
            case TraxSessionSync.ViewArranger: return "ON THE ARRANGER";
            case TraxSessionSync.ViewProjects: return "BROWSING THE SHELF";
            case TraxSessionSync.ViewHome:     return "AT THE DESKTOP";
            default:                           return "HERE";
        }
    }
}
