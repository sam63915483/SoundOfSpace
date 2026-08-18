using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Two people at one computer: the same song under both cursors, and the same
/// thing coming out of both speakers.
///
/// ── Session state, not world state ───────────────────────────────────────
/// Nothing here is saved and nothing here rides a version counter. Where a
/// partner's mouse is, and what the working song looks like before anyone has
/// pressed SAVE, are true only while both of you are sitting at the terminal.
/// The moment somebody saves, that IS world state and TraxSync owns it.
///
/// ── ONE COMPUTER (Sam, 2026-08-18, after the first playtest) ─────────────
/// Not two synchronised copies of a computer — one computer, with two people
/// leaning over it. Whatever screen it is showing, it is showing to both of
/// you: press ESC and it goes back for both, open a project and you are both in
/// it, select section C and you are both editing C.
///
/// The first pass only replicated the SONG, which produced the worst of both
/// worlds: you could each be on a different screen, or on different sections of
/// the same song, watching each other's cursors turn knobs that visibly did
/// nothing. The machine has one screen and edits one section at a time, so
/// pretending otherwise was the bug.
///
/// The whole SCREEN STATE therefore travels — which view, which project, which
/// section, which dialog, even what is typed into the save box. It is sent as
/// an absolute snapshot and reconciled rather than replayed as navigation
/// events, so a late joiner walking up to the terminal simply adopts whatever
/// it is showing, and a dropped packet corrects on the next change.
///
/// Deliberately NOT shared: where each mouse is (drawn as ghost cursors, since
/// there are genuinely two people), and the volume slider, which is about this
/// player's ears rather than about the machine.
///
/// ── Free-for-all, last write wins (Sam's call) ───────────────────────────
/// No locks, no per-section ownership, no operational transforms. Either player
/// can turn any knob at any time; if you both grab the same one the last change
/// sticks. Two people trying to work on one track will get in each other's way,
/// and that is the intended social pressure: agree who is driving, or split up
/// and let one of you make tapes while the other sells them.
///
/// The whole song travels rather than a delta. It is at most eight sections of
/// six modules and six dials, coalesced to four messages a second, and it makes
/// a dropped packet a non-event: the next edit re-states everything, so the two
/// machines cannot drift into disagreeing about a pattern and stay that way.
/// Same reasoning as EconomySync's whole-ledger snapshots, at a fraction of the
/// size.
///
/// ── Playback is shared, but the audio is local ───────────────────────────
/// Only the transport EVENT crosses the wire — play, stop, seek. Each machine
/// drives its own TraxAudioEngine from its own copy of the song, which is
/// byte-identical because the song above is. Streaming audio would be absurd
/// here; streaming the button press costs nine bytes and sounds the same.
///
/// ── The relay ────────────────────────────────────────────────────────────
/// A client sends to the host; the host applies it and relays to everyone
/// EXCEPT whoever reported it. The host's own events go straight out. This is
/// WorldSync's Dispatch shape, and it carries the same warning: never
/// SendNamedMessageToAll, because NGO delivers a broadcast back to the host and
/// the relay step on top of that is the rebroadcast storm.
/// </summary>
public class TraxSessionSync : MonoBehaviour
{
    public static TraxSessionSync Instance { get; private set; }

    const string Msg = "TraxSession";

    const byte KindCursor    = 0;   // where their mouse is, 12 Hz, droppable
    const byte KindSong      = 1;   // the whole working song
    const byte KindTransport = 2;   // play / stop / seek
    const byte KindPresence  = 3;   // opened or closed the computer
    const byte KindScreen    = 4;   // WHICH SCREEN the computer is showing
    const byte KindDial      = 5;   // one knob, mid-drag, at screen rate

    /// Which screen the computer is on. Both a presence hint and the first
    /// field of the shared screen state — they are the same question, because
    /// there is only one screen.
    public const byte ViewNone         = 0;
    public const byte ViewHome         = 1;
    public const byte ViewProjectsMenu = 2;   // the TRAX menu (NEW / LOAD)
    public const byte ViewShelf        = 3;   // the project list
    public const byte ViewArranger     = 4;

    public const byte TransportStop     = 0;
    public const byte TransportPlaySong = 1;
    public const byte TransportPlayLoop = 2;
    public const byte TransportSeek     = 3;

    /// <summary>
    /// Cursor rate. Ten bytes a packet, so the honest constraint is packet
    /// count rather than bandwidth — 25/s is well under what the enemy pose
    /// stream already sends and is enough that the receiver's smoothing has
    /// something to smooth BETWEEN rather than something to invent.
    ///
    /// 12/s was the first guess and it read as a bad connection: a hand moving
    /// smoothly arrived as twelve discrete jumps a second.
    /// </summary>
    const float CursorInterval = 1f / 25f;

    /// <summary>
    /// Dial rate. A knob drag is the one CONTINUOUS thing on this screen, and
    /// the whole-song publish below is coalesced to four a second — fine for a
    /// section being added, hopeless for a fader being swept, which arrived as
    /// four visible steps.
    ///
    /// So a dial gets its own tiny message (an index and a float) at screen
    /// rate, and the song publish stays as the periodic reconciler behind it.
    /// Absolute values, so a dropped one is corrected by the next.
    /// </summary>
    const float DialInterval = 1f / 30f;

    /// Floor between song publishes. Dragging a knob fires an edit every frame;
    /// without this each one would ship the arrangement.
    const float SongInterval = 0.25f;

    bool _registered;
    float _nextCursorAt;
    float _nextSongAt;
    bool _songPending;
    TraxSong _pendingSong;

    // ── what the local screen reads ──────────────────────────────────────

    /// True while the partner has the computer open. Goes false on close, on
    /// disconnect, and when nothing has been heard for a while.
    public static bool RemoteOpen { get; private set; }
    public static byte RemoteView { get; private set; }
    /// Normalised into the virtual screen rect (0,0 bottom-left → 1,1 top-right),
    /// so it lands on the same widget whatever either window is sized to.
    public static Vector2 RemoteCursor { get; private set; }
    public static bool RemoteClicking { get; private set; }
    public static string RemoteName { get; private set; } = "";
    public static int RemoteSwatch { get; private set; }

    /// Bumped when a song lands. The screen polls this rather than subscribing,
    /// so there is no delegate to leak when the computer is destroyed mid-edit.
    public static int IncomingSongRev { get; private set; }
    public static TraxSong IncomingSong { get; private set; }

    public static int IncomingTransportRev { get; private set; }
    public static byte IncomingTransportMode { get; private set; }
    public static int IncomingTransportStep { get; private set; }

    /// <summary>
    /// What the one computer is showing. An absolute snapshot, reconciled
    /// rather than replayed: the screen compares this against what it is
    /// actually displaying and moves itself to match, so there is no navigation
    /// event to miss and no ordering to get wrong.
    /// </summary>
    public struct Screen
    {
        public byte view;        // ViewHome / ViewProjectsMenu / ViewShelf / ViewArranger
        public string projectId; // which shelf record is open ("" = never saved)
        public int section;      // the section being edited — one at a time, by design
        public bool saveOpen;
        public string saveText;
        public bool printOpen;

        public bool Same(Screen o)
        {
            return view == o.view
                && section == o.section
                && saveOpen == o.saveOpen
                && printOpen == o.printOpen
                && (projectId ?? "") == (o.projectId ?? "")
                && (saveText ?? "") == (o.saveText ?? "");
        }
    }

    public static int IncomingScreenRev { get; private set; }
    public static Screen IncomingScreen { get; private set; }

    /// True once we have heard what the computer is showing at least once this
    /// session. Walking up to a terminal a partner is already using adopts that
    /// screen rather than resuming our own — one computer, one screen — and this
    /// is how the screen knows there is something to adopt.
    public static bool HasScreen { get; private set; }

    /// <summary>
    /// True while an inbound song or transport event is being applied to the
    /// local screen. Applying one runs the SAME code an edit runs, which would
    /// publish it straight back out — a loop that never settles. Every publish
    /// below no-ops while it is set.
    /// </summary>
    public static bool ApplyingRemote { get; set; }

    /// Nothing heard for this long and the partner is treated as gone. Covers a
    /// close message lost on the way and a client that dropped without one.
    const float PresenceTimeout = 5f;
    float _remoteHeardAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Does not skip MainMenu, so it never needs seeding in
        // EnsureGameplaySingletons (CLAUDE.md trap #1).
        var go = new GameObject("TraxSessionSync");
        DontDestroyOnLoad(go);
        go.AddComponent<TraxSessionSync>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        _registered = false;
        ClearRemote();
    }

    static void ClearRemote()
    {
        RemoteOpen = false;
        RemoteView = ViewNone;
        RemoteClicking = false;
        IncomingSong = null;
        HasScreen = false;
    }

    /// <summary>
    /// True when this machine is the one whose screen settles an argument.
    ///
    /// If both players navigate in the same instant they would otherwise swap
    /// screens and then keep swapping, each heartbeat undoing the other. The
    /// host applying both changes in arrival order and re-stating the result
    /// gives that a single, deterministic answer — the same reason the host owns
    /// every other timer and dice roll here.
    /// </summary>
    static bool ScreenAuthority
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && nm.IsServer;
        }
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            if (_registered) { _registered = false; ClearRemote(); }
            return;
        }

        // On EVERY machine, not just senders.
        if (!_registered)
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
            _registered = true;
        }

        if (RemoteOpen && Time.unscaledTime - _remoteHeardAt > PresenceTimeout) ClearRemote();

        // Somebody is at the machine. Make sure THIS player's copy of the
        // computer exists even though they have never walked up to it — it is
        // what draws the picture on the world monitor and makes the sound come
        // out of the console, and without it a partner's session would be
        // invisible and silent to anybody who had not used the terminal.
        if (RemoteOpen) ShuttleComputerUI.EnsureExists();

        // Coalesced song publish. Held rather than sent on the edit itself so a
        // knob drag is four messages a second instead of sixty.
        if (_songPending && Time.unscaledTime >= _nextSongAt)
        {
            _songPending = false;
            _nextSongAt = Time.unscaledTime + SongInterval;
            SendSong(_pendingSong);
            _pendingSong = null;
        }
    }

    /// True when there is anybody else in the session to talk to.
    static bool Live
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (Instance == null || nm == null || !nm.IsListening) return false;
            return !nm.IsServer || nm.ConnectedClientsIds.Count > 1;
        }
    }

    // ── publishing ───────────────────────────────────────────────────────

    /// <summary>
    /// Where this player's mouse is on the virtual screen. Throttled here rather
    /// than at the call site so the screen can just call it every frame.
    /// </summary>
    public static void PublishCursor(Vector2 normalized, byte view, bool clicking)
    {
        if (!Live || ApplyingRemote) return;
        if (Time.unscaledTime < Instance._nextCursorAt) return;
        Instance._nextCursorAt = Time.unscaledTime + CursorInterval;

        Instance.Dispatch(w =>
        {
            w.WriteValueSafe(KindCursor);
            w.WriteValueSafe(normalized.x);
            w.WriteValueSafe(normalized.y);
            w.WriteValueSafe(view);
            w.WriteValueSafe((byte)(clicking ? 1 : 0));
        }, ulong.MaxValue, NetworkDelivery.UnreliableSequenced, 48);
    }

    /// <summary>
    /// The working song changed. Queued rather than sent, and only the newest
    /// one survives the wait — an intermediate state of a knob drag is not worth
    /// a packet when the next frame supersedes it.
    /// </summary>
    public static void PublishSong(TraxSong song)
    {
        if (!Live || ApplyingRemote || song == null) return;
        Instance._pendingSong = song.Clone();
        Instance._songPending = true;
    }

    void SendSong(TraxSong song)
    {
        if (song == null) return;
        string json = TraxSongWire.ToJson(song);
        Dispatch(w =>
        {
            w.WriteValueSafe(KindSong);
            w.WriteValueSafe(json);
        }, ulong.MaxValue, NetworkDelivery.ReliableFragmentedSequenced, json.Length * 4 + 128);
    }

    /// <summary>
    /// One knob, being turned right now.
    ///
    /// Unreliable and absolute: the next packet supersedes this one, and the
    /// whole-song publish behind it is the thing that guarantees the two
    /// machines agree once the hand stops moving. Throttled per DIAL rather
    /// than globally, so turning two at once does not halve either one's rate.
    /// </summary>
    public static void PublishDial(int index, double value)
    {
        if (!Live || ApplyingRemote) return;
        if (index < 0 || index >= 8) return;
        if (Time.unscaledTime < Instance._nextDialAt[index]) return;
        Instance._nextDialAt[index] = Time.unscaledTime + DialInterval;

        Instance.Dispatch(w =>
        {
            w.WriteValueSafe(KindDial);
            w.WriteValueSafe(index);
            w.WriteValueSafe((float)value);
        }, ulong.MaxValue, NetworkDelivery.UnreliableSequenced, 32);
    }

    readonly float[] _nextDialAt = new float[8];

    public static int IncomingDialRev { get; private set; }
    public static int IncomingDialIndex { get; private set; }
    public static float IncomingDialValue { get; private set; }

    /// <summary>
    /// Somebody pressed play, stop, or clicked the ruler. Reliable — a missed
    /// stop would leave one machine playing alone, and there is no periodic
    /// re-statement to recover from that.
    /// </summary>
    public static void PublishTransport(byte mode, int step)
    {
        if (!Live || ApplyingRemote) return;
        Instance.Dispatch(w =>
        {
            w.WriteValueSafe(KindTransport);
            w.WriteValueSafe(mode);
            w.WriteValueSafe(step);
        }, ulong.MaxValue, NetworkDelivery.ReliableSequenced, 32);
    }

    /// <summary>
    /// The computer moved to a different screen — a click, an ESC, a dialog, a
    /// section select, a character typed into the save box.
    ///
    /// Reliable, because there is no periodic re-statement to recover from a
    /// dropped one and a lost ESC would leave the two of you looking at
    /// different things indefinitely.
    /// </summary>
    public static void PublishScreen(Screen s)
    {
        if (!Live || ApplyingRemote) return;
        Instance._nextScreenBeatAt = Time.unscaledTime + ScreenHeartbeat;
        Instance.SendScreen(s);
    }

    /// <summary>
    /// The host re-states the screen every so often, so a partner who walks up
    /// mid-session adopts it, and so a simultaneous change on both machines
    /// converges on the host's answer within a beat instead of flip-flopping.
    /// Only the host does this — two machines re-stating would be the argument,
    /// not the fix.
    /// </summary>
    const float ScreenHeartbeat = 1.5f;
    float _nextScreenBeatAt;

    public static void HeartbeatScreen(Screen s)
    {
        if (!Live || ApplyingRemote || !ScreenAuthority) return;
        if (Time.unscaledTime < Instance._nextScreenBeatAt) return;
        Instance._nextScreenBeatAt = Time.unscaledTime + ScreenHeartbeat;
        Instance.SendScreen(s);
    }

    void SendScreen(Screen s)
    {
        Dispatch(w =>
        {
            w.WriteValueSafe(KindScreen);
            w.WriteValueSafe(s.view);
            w.WriteValueSafe(s.projectId ?? "");
            w.WriteValueSafe(s.section);
            w.WriteValueSafe((byte)(s.saveOpen ? 1 : 0));
            w.WriteValueSafe(s.saveText ?? "");
            w.WriteValueSafe((byte)(s.printOpen ? 1 : 0));
        }, ulong.MaxValue, NetworkDelivery.ReliableSequenced,
           (s.projectId != null ? s.projectId.Length : 0) * 4
         + (s.saveText != null ? s.saveText.Length : 0) * 4 + 96);
    }

    /// Opened or closed the computer. Carries the name and suit colour so the
    /// ghost cursor can be labelled and tinted without a second lookup.
    public static void PublishPresence(bool open, byte view)
    {
        if (!Live) return;
        string name = CharacterStore.ActiveName ?? "";
        int swatch = CharacterStore.ActiveSwatch;
        Instance.Dispatch(w =>
        {
            w.WriteValueSafe(KindPresence);
            w.WriteValueSafe((byte)(open ? 1 : 0));
            w.WriteValueSafe(view);
            w.WriteValueSafe(name);
            w.WriteValueSafe(swatch);
        }, ulong.MaxValue, NetworkDelivery.ReliableSequenced, name.Length * 4 + 64);
    }

    // ── inbound ──────────────────────────────────────────────────────────

    void OnMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        var nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsServer;

        switch (kind)
        {
            case KindCursor:
            {
                reader.ReadValueSafe(out float x);
                reader.ReadValueSafe(out float y);
                reader.ReadValueSafe(out byte view);
                reader.ReadValueSafe(out byte clicking);

                RemoteCursor = new Vector2(x, y);
                RemoteView = view;
                RemoteClicking = clicking != 0;
                RemoteOpen = view != ViewNone;
                _remoteHeardAt = Time.unscaledTime;

                if (server) Relay(senderId, NetworkDelivery.UnreliableSequenced, w =>
                {
                    w.WriteValueSafe(KindCursor);
                    w.WriteValueSafe(x); w.WriteValueSafe(y);
                    w.WriteValueSafe(view); w.WriteValueSafe(clicking);
                }, 48);
                break;
            }

            case KindSong:
            {
                reader.ReadValueSafe(out string json);
                var song = TraxSongWire.FromJson(json);
                // A song that didn't parse is ignored rather than applied as an
                // empty one, which would wipe the arranger on both machines.
                if (song != null) { IncomingSong = song; IncomingSongRev++; }
                _remoteHeardAt = Time.unscaledTime;

                if (server) Relay(senderId, NetworkDelivery.ReliableFragmentedSequenced, w =>
                {
                    w.WriteValueSafe(KindSong);
                    w.WriteValueSafe(json);
                }, json.Length * 4 + 128);
                break;
            }

            case KindTransport:
            {
                reader.ReadValueSafe(out byte mode);
                reader.ReadValueSafe(out int step);
                IncomingTransportMode = mode;
                IncomingTransportStep = step;
                IncomingTransportRev++;
                _remoteHeardAt = Time.unscaledTime;

                if (server) Relay(senderId, NetworkDelivery.ReliableSequenced, w =>
                {
                    w.WriteValueSafe(KindTransport);
                    w.WriteValueSafe(mode); w.WriteValueSafe(step);
                }, 32);
                break;
            }

            case KindDial:
            {
                reader.ReadValueSafe(out int index);
                reader.ReadValueSafe(out float value);
                IncomingDialIndex = index;
                IncomingDialValue = value;
                IncomingDialRev++;
                _remoteHeardAt = Time.unscaledTime;

                if (server) Relay(senderId, NetworkDelivery.UnreliableSequenced, w =>
                {
                    w.WriteValueSafe(KindDial);
                    w.WriteValueSafe(index); w.WriteValueSafe(value);
                }, 32);
                break;
            }

            case KindScreen:
            {
                reader.ReadValueSafe(out byte view);
                reader.ReadValueSafe(out string projectId);
                reader.ReadValueSafe(out int section);
                reader.ReadValueSafe(out byte saveOpen);
                reader.ReadValueSafe(out string saveText);
                reader.ReadValueSafe(out byte printOpen);

                IncomingScreen = new Screen
                {
                    view = view, projectId = projectId, section = section,
                    saveOpen = saveOpen != 0, saveText = saveText, printOpen = printOpen != 0,
                };
                IncomingScreenRev++;
                HasScreen = view != ViewNone;
                RemoteView = view;
                RemoteOpen = view != ViewNone;
                _remoteHeardAt = Time.unscaledTime;

                if (server) Relay(senderId, NetworkDelivery.ReliableSequenced, w =>
                {
                    w.WriteValueSafe(KindScreen);
                    w.WriteValueSafe(view); w.WriteValueSafe(projectId ?? "");
                    w.WriteValueSafe(section);
                    w.WriteValueSafe(saveOpen); w.WriteValueSafe(saveText ?? "");
                    w.WriteValueSafe(printOpen);
                }, (projectId != null ? projectId.Length : 0) * 4
                 + (saveText != null ? saveText.Length : 0) * 4 + 96);
                break;
            }

            case KindPresence:
            {
                reader.ReadValueSafe(out byte open);
                reader.ReadValueSafe(out byte view);
                reader.ReadValueSafe(out string name);
                reader.ReadValueSafe(out int swatch);

                RemoteOpen = open != 0;
                RemoteView = RemoteOpen ? view : ViewNone;
                RemoteName = name ?? "";
                RemoteSwatch = swatch;
                if (!RemoteOpen) RemoteClicking = false;
                _remoteHeardAt = Time.unscaledTime;

                if (server) Relay(senderId, NetworkDelivery.ReliableSequenced, w =>
                {
                    w.WriteValueSafe(KindPresence);
                    w.WriteValueSafe(open); w.WriteValueSafe(view);
                    w.WriteValueSafe(name ?? ""); w.WriteValueSafe(swatch);
                }, (name != null ? name.Length : 0) * 4 + 64);
                break;
            }
        }
    }

    // ── transport plumbing ───────────────────────────────────────────────

    /// Host: to every client except itself and `skip`. Client: to the host.
    /// The same shape WorldSync.Dispatch uses, and the same warning applies —
    /// ⚠️ never SendNamedMessageToAll.
    void Dispatch(System.Action<FastBufferWriter> write, ulong skip,
                  NetworkDelivery delivery, int sizeHint)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !_registered) return;

        var w = new FastBufferWriter(sizeHint, Allocator.Temp, 1024 * 1024);
        try
        {
            write(w);
            if (!nm.IsServer)
            {
                nm.CustomMessagingManager.SendNamedMessage(
                    Msg, NetworkManager.ServerClientId, w, delivery);
                return;
            }
            var ids = nm.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == nm.LocalClientId) continue;   // never loop back
                if (ids[i] == skip) continue;               // they told us
                nm.CustomMessagingManager.SendNamedMessage(Msg, ids[i], w, delivery);
            }
        }
        finally { w.Dispose(); }
    }

    void Relay(ulong reporter, NetworkDelivery delivery,
               System.Action<FastBufferWriter> write, int sizeHint)
    {
        Dispatch(write, reporter, delivery, sizeHint);
    }
}
