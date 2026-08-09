using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The multiplayer session: a 4-digit code and a password instead of an IP.
///
/// ── Why this exists as its own singleton ─────────────────────────────────
/// The NetworkManager lives in the GAMEPLAY scene, but the lobby is created and
/// joined from the MAIN MENU. Something has to own the relay allocation and the
/// lobby handle across that scene load and then hand them to the transport once
/// the NetworkManager finally exists. That is this class's whole job.
///
/// ── How joining works ────────────────────────────────────────────────────
/// Relay removes the IP address: both machines make an OUTBOUND connection to a
/// Unity-run relay which forwards packets between them, so there is no port
/// forwarding and nothing to type. Lobby turns that into a code: the lobby holds
/// the relay join code, is stamped with our own 4-digit code in an indexed field
/// we can query on, and is locked with a password. The relay join code is never
/// shown to a player.
///
/// ── The handshake, in order ──────────────────────────────────────────────
///   HOST  : sign in -> allocate relay -> create lobby (password + 4-digit code
///           + relay code in member-visible data) -> heartbeat it -> on START,
///           flip the lobby's "started" flag, load the gameplay scene, then
///           StartHost once NetworkManager exists.
///   CLIENT: sign in -> find the lobby by 4-digit code -> join it with the
///           password (the service rejects a wrong one; it never reaches
///           netcode) -> read the relay code -> wait for "started" -> load the
///           gameplay scene -> StartClient.
///
/// A client that joins a session already in progress takes the same path and
/// simply never waits, because "started" is already set.
///
/// ── What this does NOT do yet ────────────────────────────────────────────
/// This is the front door only. Player poses and the solar system sync (which
/// already existed) work; the WORLD — mushrooms, trees, buildings, the economy —
/// is not replicated yet. That is the rest of Phase B. Two players will see each
/// other move correctly and see different mushrooms.
///
/// All of it sits behind FeatureVault.Multiplayer.
/// </summary>
public class MultiplayerSession : MonoBehaviour
{
    public static MultiplayerSession Instance { get; private set; }

    public enum State { Idle, Working, LobbyOpen, WaitingForHost, Launching, Failed }

    /// Lobby data keys. `CodeKey` is indexed so we can query on it; the relay
    /// code is member-visible so only someone who got past the password sees it.
    const string CodeKey    = "code";
    const string RelayKey   = "relay";
    const string StartedKey = "started";

    const string GameplayScene = "1.6.7.7.7";
    const int    MaxPlayers    = 4;
    const float  HeartbeatSeconds = 15f;   // lobbies expire after ~30s without one
    const float  PollSeconds      = 1.5f;

    // ── public surface the UI reads ──────────────────────────────────────
    public State Current { get; private set; } = State.Idle;
    public string Code { get; private set; } = "";
    public string Status { get; private set; } = "";
    /// The password the HOST typed, kept so they can read it back out to a
    /// friend from the pause menu. Never populated on a guest.
    public string HostPassword { get; private set; } = "";
    public bool IsHost { get; private set; }
    /// Names of everyone currently in the lobby, host first.
    public IReadOnlyList<string> Roster => _roster;
    /// Skips Relay entirely and uses 127.0.0.1 — same-machine testing, and the
    /// path the LAN proof used. Kept because it is instant and costs no
    /// bandwidth while iterating.
    public bool LocalMode { get; set; }

    /// True on a machine that JOINED — read by SecondPlayerArrival to hold the
    /// screen black and wake the player out of the stasis pod instead of
    /// dropping them on top of the host.
    ///
    /// CONSUMED on use (see TakeGuestArrival). Statics survive a trip back
    /// through the main menu, and a sticky flag here would make the NEXT
    /// single-player game wake in the pod too — the same leak that bit
    /// ShuttleExitDoor's stamp and TutorialGate's lock.
    public static bool ArrivingAsGuest { get; private set; }

    /// Reads the guest flag and clears it, so it can only ever fire once.
    public static bool TakeGuestArrival()
    {
        bool v = ArrivingAsGuest;
        ArrivingAsGuest = false;
        return v;
    }

    public static void ClearGuestArrival() => ArrivingAsGuest = false;


    readonly List<string> _roster = new List<string>();

    Lobby _lobby;
    string _relayJoinCode;
    Allocation _hostAllocation;
    JoinAllocation _guestAllocation;
    bool _servicesReady;
    bool _sceneHandoffArmed;
    Coroutine _heartbeat, _poll;

    // ── lifecycle ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (!FeatureVault.Multiplayer) return;
        if (Instance != null) return;
        // Deliberately does NOT skip MainMenu — unlike the gameplay singletons,
        // this one's whole job starts in the menu. It therefore does not need
        // seeding in EnsureGameplaySingletons either.
        var go = new GameObject("MultiplayerSession");
        DontDestroyOnLoad(go);
        go.AddComponent<MultiplayerSession>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
    }

    /// Returning to the main menu ends the session properly.
    ///
    /// Without this the guest stays a member of the lobby on the service even
    /// though their game is over — and Unity refuses to let you join a lobby you
    /// are already in, which reads back as "wrong code or the session ended"
    /// when you try to rejoin the same one.
    void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "MainMenu") return;
        if (Current == State.Idle) return;
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsClient || nm.IsServer)) nm.Shutdown();
        CancelSession();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
    }

    // ── Unity Services ───────────────────────────────────────────────────

    /// Signs in anonymously. Friends need no Unity account and see no login —
    /// this happens silently the first time they open the multiplayer screen.
    async Task<bool> EnsureServicesAsync()
    {
        if (_servicesReady) return true;
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            _servicesReady = true;
            return true;
        }
        catch (Exception e)
        {
            Fail("Couldn't reach Unity's servers. Check your internet connection.");
            Debug.LogError($"[MultiplayerSession] Services init failed: {e}");
            return false;
        }
    }

    // ── hosting ──────────────────────────────────────────────────────────

    /// Opens a session. Returns false and sets Status on failure.
    public async Task<bool> CreateSessionAsync(string password)
    {
        if (Current == State.Working) return false;
        IsHost = true;
        HostPassword = password ?? "";
        Current = State.Working;
        Status = "Opening a session…";

        if (LocalMode)
        {
            Code = "LOCAL";
            _roster.Clear();
            _roster.Add("You (host)");
            Current = State.LobbyOpen;
            Status = "Local session — same machine only.";
            return true;
        }

        if (!await EnsureServicesAsync()) return false;

        try
        {
            // Relay first: the lobby needs the join code to hand out.
            _hostAllocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers);
            _relayJoinCode  = await RelayService.Instance.GetJoinCodeAsync(_hostAllocation.AllocationId);
        }
        catch (Exception e)
        {
            Fail("Relay wouldn't allocate. Is Relay switched on in the Unity dashboard?");
            Debug.LogError($"[MultiplayerSession] Relay allocation failed: {e}");
            return false;
        }

        // Four digits is only 10,000 codes, so a collision is rare but real.
        // Roll a fresh one and try again rather than handing out a code that
        // resolves to somebody else's game.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            string candidate = UnityEngine.Random.Range(1000, 10000).ToString();
            try
            {
                if (await CodeIsTakenAsync(candidate)) continue;

                var options = new CreateLobbyOptions
                {
                    IsPrivate = false,          // discoverable BY CODE only, never browsable
                    Password  = DerivePassword(password),   // null = open session
                    Data = new Dictionary<string, DataObject>
                    {
                        // Indexed + public so a joiner can find it by code.
                        [CodeKey]  = new DataObject(DataObject.VisibilityOptions.Public,
                                                    candidate, DataObject.IndexOptions.S1),
                        // Member-only: you must clear the password to read this.
                        [RelayKey] = new DataObject(DataObject.VisibilityOptions.Member, _relayJoinCode),
                        [StartedKey] = new DataObject(DataObject.VisibilityOptions.Member, "0"),
                    }
                };

                _lobby = await LobbyService.Instance.CreateLobbyAsync("Sound of Space", MaxPlayers, options);
                Code = candidate;
                Current = State.LobbyOpen;
                Status = "Session open. Share the code.";
                RefreshRoster();
                _heartbeat = StartCoroutine(HeartbeatLoop());
                _poll = StartCoroutine(PollLoop());
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[MultiplayerSession] Lobby create attempt {attempt} failed: {e.Reason}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MultiplayerSession] Lobby create failed: {e}");
                break;
            }
        }

        Fail("Couldn't open a lobby. Is Lobby switched on in the Unity dashboard?");
        return false;
    }

    /// Turns whatever the player typed into something Unity's lobby service will
    /// accept, so the password can be any length — or nothing at all.
    ///
    /// Unity requires 8–64 characters. Rather than forcing that on the player, we
    /// hash their input into a fixed-length token and hand THAT to the service.
    /// Both machines derive the same token from the same typed password, so the
    /// check still happens server-side and a wrong password is still rejected
    /// before it reaches netcode — we have not weakened anything, we have just
    /// stopped making the player satisfy Unity's rule.
    ///
    /// Empty in means null out: a lobby with genuinely no password.
    static string DerivePassword(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("soundofspace:" + raw));
            // Base64 minus the symbols, so the token is plain alphanumeric.
            string s = Convert.ToBase64String(hash)
                             .Replace("+", "").Replace("/", "").Replace("=", "");
            return s.Substring(0, 20);
        }
    }

    async Task<bool> CodeIsTakenAsync(string candidate)
    {
        try
        {
            var q = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = 1,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.S1, candidate, QueryFilter.OpOptions.EQ)
                }
            });
            return q != null && q.Results != null && q.Results.Count > 0;
        }
        catch { return false; }   // if the query fails, just try the code
    }

    // ── joining ──────────────────────────────────────────────────────────

    public async Task<bool> JoinSessionAsync(string code, string password)
    {
        if (Current == State.Working) return false;
        IsHost = false;
        Current = State.Working;
        Status = "Looking for that session…";
        code = (code ?? "").Trim();

        if (LocalMode)
        {
            Current = State.Launching;
            LaunchIntoGame();
            return true;
        }

        if (!await EnsureServicesAsync()) return false;

        string lobbyId;
        bool needsPassword;
        try
        {
            var q = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = 1,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.S1, code, QueryFilter.OpOptions.EQ)
                }
            });
            if (q == null || q.Results == null || q.Results.Count == 0)
            {
                Fail($"No session with code {code}. Check the digits.");
                return false;
            }
            lobbyId = q.Results[0].Id;
            // An open session takes no password at all — passing one to a lobby
            // that has none is an error, not a no-op.
            needsPassword = q.Results[0].HasPassword;
        }
        catch (Exception e)
        {
            Fail("Couldn't search for sessions. Check your internet connection.");
            Debug.LogError($"[MultiplayerSession] Lobby query failed: {e}");
            return false;
        }

        try
        {
            var join = new JoinLobbyByIdOptions();
            if (needsPassword) join.Password = DerivePassword(password);
            _lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, join);
        }
        catch (LobbyServiceException e)
        {
            // The service checks the password, so a wrong one never reaches
            // netcode at all — there is nothing to reject on our side.
            Fail(e.Reason == LobbyExceptionReason.IncorrectPassword
                ? (needsPassword && string.IsNullOrEmpty(password)
                    ? "That session needs a password."
                    : "Wrong password.")
                : "Couldn't join that session — it may have closed.");
            Debug.LogWarning($"[MultiplayerSession] Join failed: {e.Reason}");
            return false;
        }
        catch (Exception e)
        {
            Fail("Couldn't join that session.");
            Debug.LogError($"[MultiplayerSession] Join failed: {e}");
            return false;
        }

        if (_lobby.Data == null || !_lobby.Data.TryGetValue(RelayKey, out var relayData))
        {
            Fail("That session didn't hand out a connection. Ask the host to reopen it.");
            return false;
        }
        // Note: we deliberately do NOT join the Relay allocation yet. That
        // happens at launch (StartNetcodeWhenReady) so it is fresh at the moment
        // we actually connect, however long the guest sits in the lobby.
        _relayJoinCode = relayData.Value;

        Code = code;
        RefreshRoster();
        _poll = StartCoroutine(PollLoop());

        // Already running? Drop straight in. Otherwise sit in the lobby until
        // the host presses START.
        if (LobbyHasStarted())
        {
            Current = State.Launching;
            LaunchIntoGame();
        }
        else
        {
            Current = State.WaitingForHost;
            Status = "In the lobby. Waiting for the host to start.";
        }
        return true;
    }

    bool LobbyHasStarted()
        => _lobby != null && _lobby.Data != null
           && _lobby.Data.TryGetValue(StartedKey, out var d) && d.Value == "1";

    // ── starting the game ────────────────────────────────────────────────

    /// Host only. Flips the lobby's started flag so anyone waiting follows, then
    /// loads the gameplay scene.
    public async void BeginGame()
    {
        if (!IsHost) return;
        Current = State.Launching;
        Status = "Starting…";

        if (!LocalMode && _lobby != null)
        {
            try
            {
                _lobby = await LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        [StartedKey] = new DataObject(DataObject.VisibilityOptions.Member, "1"),
                    }
                });
            }
            catch (Exception e)
            {
                // Not fatal for the host — they can still play; late joiners just
                // won't be released from the lobby until the next update lands.
                Debug.LogWarning($"[MultiplayerSession] Couldn't flag lobby as started: {e}");
            }
        }

        LaunchIntoGame();
    }

    /// Loads the gameplay scene and arms the handoff that starts netcode once
    /// the NetworkManager in that scene exists.
    /// Set when the session is opened or joined from INSIDE the game (the pause
    /// menu) rather than from the main menu. The gameplay scene is already
    /// loaded, so launching must start netcode where we stand instead of
    /// reloading the world out from under the player.
    bool _inPlace;
    public void SetInPlace(bool v) => _inPlace = v;

    void LaunchIntoGame()
    {
        if (_inPlace)
        {
            // Already in the world — no scene load, no pod arrival, just connect.
            Current = State.Launching;
            StartCoroutine(StartNetcodeWhenReady());
            return;
        }

        ArrivingAsGuest = !IsHost;

        // A guest must never replay the shuttle intro. This is the same flag the
        // load path relies on to skip it.
        if (!IsHost) EarlyGameProgress.IntroPlayed = true;

        if (!_sceneHandoffArmed)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _sceneHandoffArmed = true;
        }

        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.LoadSceneAndShow(
                GameplayScene, preSceneSetup: MainMenuController.EnsureGameplaySingletonsAsync);
        else
        {
            MainMenuController.EnsureGameplaySingletons();
            SceneManager.LoadScene(GameplayScene);
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != GameplayScene) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _sceneHandoffArmed = false;
        StartCoroutine(StartNetcodeWhenReady());
    }

    /// NetworkManager is a scene object, so it may not have run Awake yet on the
    /// frame the scene reports loaded. Wait for it rather than racing it.
    IEnumerator StartNetcodeWhenReady()
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (NetworkManager.Singleton == null && Time.realtimeSinceStartup < deadline)
            yield return null;

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Fail("Couldn't find the NetworkManager in the game scene.");
            yield break;
        }

        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null)
        {
            Fail("The NetworkManager has no Unity Transport component.");
            yield break;
        }

        if (LocalMode)
        {
            utp.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
        }
        else if (IsHost)
        {
            ApplyRelay(utp, _hostAllocation.ServerEndpoints, _hostAllocation.RelayServer,
                       _hostAllocation.AllocationIdBytes, _hostAllocation.Key,
                       _hostAllocation.ConnectionData, null);
        }
        else
        {
            // Join the allocation HERE rather than back when the lobby was
            // joined. A guest can sit in the lobby for minutes waiting for the
            // host to start, and an allocation fetched that long ago is a
            // liability; fetching it at the moment we connect is strictly safer.
            var joinTask = RelayService.Instance.JoinAllocationAsync(_relayJoinCode);
            while (!joinTask.IsCompleted) yield return null;
            if (joinTask.IsFaulted || joinTask.Result == null)
            {
                Fail("Couldn't connect through Relay.");
                Debug.LogError($"[MultiplayerSession] Relay join failed: {joinTask.Exception}");
                yield break;
            }
            _guestAllocation = joinTask.Result;

            ApplyRelay(utp, _guestAllocation.ServerEndpoints, _guestAllocation.RelayServer,
                       _guestAllocation.AllocationIdBytes, _guestAllocation.Key,
                       _guestAllocation.ConnectionData, _guestAllocation.HostConnectionData);
        }

        // Surface WHY a connection dropped. Without this a failed join is a
        // silent 60-second timeout and nothing to go on.
        nm.OnClientDisconnectCallback -= OnClientDisconnect;
        nm.OnClientDisconnectCallback += OnClientDisconnect;

        bool ok = IsHost ? nm.StartHost() : nm.StartClient();
        if (!ok)
        {
            Fail(IsHost ? "Couldn't start hosting." : "Couldn't connect to the host.");
            yield break;
        }

        Current = State.Launching;
        Status = IsHost ? "Hosting." : "Connected.";
    }

    /// Point the transport at the right relay endpoint.
    ///
    /// THIS IS THE BIT THAT BIT US. `allocation.RelayServer` is the plain UDP
    /// endpoint — pairing it with isSecure:true makes the transport speak DTLS
    /// at a port that isn't listening for it, so the connection never completes,
    /// the client burns its 60 retry attempts and reports a timeout. With no
    /// connection there are no puppets and no orbit corrections, so both players
    /// sit in their own unsynced worlds.
    ///
    /// The endpoint list carries one entry per protocol. Pick the DTLS one and
    /// say secure; fall back to UDP and say insecure. Never mix them.
    static void ApplyRelay(UnityTransport utp, List<RelayServerEndpoint> endpoints, RelayServer fallback,
                           byte[] allocationId, byte[] key, byte[] connectionData, byte[] hostConnectionData)
    {
        RelayServerEndpoint dtls = null, udp = null;
        if (endpoints != null)
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                var e = endpoints[i];
                if (e == null) continue;
                if (e.ConnectionType == "dtls") dtls = e;
                else if (e.ConnectionType == "udp") udp = e;
            }
        }

        if (dtls != null)
        {
            Debug.Log($"[MultiplayerSession] Relay via DTLS {dtls.Host}:{dtls.Port}");
            utp.SetRelayServerData(dtls.Host, (ushort)dtls.Port, allocationId, key,
                                   connectionData, hostConnectionData, true);
        }
        else if (udp != null)
        {
            Debug.Log($"[MultiplayerSession] Relay via UDP {udp.Host}:{udp.Port}");
            utp.SetRelayServerData(udp.Host, (ushort)udp.Port, allocationId, key,
                                   connectionData, hostConnectionData, false);
        }
        else if (fallback != null)
        {
            Debug.Log($"[MultiplayerSession] Relay via fallback UDP {fallback.IpV4}:{fallback.Port}");
            utp.SetRelayServerData(fallback.IpV4, (ushort)fallback.Port, allocationId, key,
                                   connectionData, hostConnectionData, false);
        }
        else
        {
            Debug.LogError("[MultiplayerSession] Allocation carried no usable relay endpoint.");
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        // Only meaningful for our own connection; the host also gets this for
        // every guest that leaves.
        if (!IsHost && clientId == nm.LocalClientId)
        {
            string reason = string.IsNullOrEmpty(nm.DisconnectReason)
                ? "The host ended the session."
                : nm.DisconnectReason;
            Fail(reason);
            Debug.LogWarning($"[MultiplayerSession] Disconnected: {reason}");

            // The host's world is gone and this client has no world state of its
            // own worth standing in, so go back to the menu rather than leaving
            // the player alone in a half-empty scene.
            StartCoroutine(ReturnToMenu());
        }
    }

    /// Sends a dropped guest back to the main menu, after a beat so the reason
    /// is readable rather than flashing past.
    IEnumerator ReturnToMenu()
    {
        yield return new WaitForSecondsRealtime(2f);
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsClient)) nm.Shutdown();
        // OnAnySceneLoaded tears the lobby membership down when MainMenu loads.
        SceneManager.LoadScene("MainMenu");
    }

    // ── lobby upkeep ─────────────────────────────────────────────────────

    /// A lobby with no heartbeat is reaped in about 30 seconds. Host only.
    IEnumerator HeartbeatLoop()
    {
        var wait = new WaitForSecondsRealtime(HeartbeatSeconds);
        while (_lobby != null && IsHost)
        {
            yield return wait;
            if (_lobby == null) break;
            var task = LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
                Debug.LogWarning($"[MultiplayerSession] Heartbeat failed: {task.Exception?.Message}");
        }
    }

    /// Refreshes the roster for the lobby screen, and — for a guest — watches
    /// for the host flipping the started flag.
    IEnumerator PollLoop()
    {
        var wait = new WaitForSecondsRealtime(PollSeconds);
        while (_lobby != null)
        {
            yield return wait;
            if (_lobby == null) yield break;
            if (Current == State.Launching) yield break;

            var task = LobbyService.Instance.GetLobbyAsync(_lobby.Id);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                // The host closed it, or the network blipped. For a guest sitting
                // in the lobby that is worth surfacing; for the host it is noise.
                if (!IsHost && Current == State.WaitingForHost)
                    Fail("The host closed the session.");
                yield break;
            }

            _lobby = task.Result;
            RefreshRoster();

            if (!IsHost && Current == State.WaitingForHost && LobbyHasStarted())
            {
                Current = State.Launching;
                Status = "Host started — dropping in…";
                LaunchIntoGame();
                yield break;
            }
        }
    }

    void RefreshRoster()
    {
        _roster.Clear();
        if (_lobby == null || _lobby.Players == null) return;
        for (int i = 0; i < _lobby.Players.Count; i++)
        {
            bool isHostPlayer = _lobby.HostId == _lobby.Players[i].Id;
            bool isYou = AuthenticationService.Instance.IsSignedIn
                         && _lobby.Players[i].Id == AuthenticationService.Instance.PlayerId;
            string who = isYou ? "You" : $"Player {i + 1}";
            _roster.Add(isHostPlayer ? who + " (host)" : who);
        }
    }

    // ── teardown ─────────────────────────────────────────────────────────

    /// Ends a session that is already IN PROGRESS and drops back to single
    /// player, without leaving the world.
    ///
    /// Shuts netcode down first so the guests are disconnected cleanly, then
    /// closes the lobby. The host keeps playing exactly where they were — this
    /// is "make it solo again", not "quit to menu".
    public void EndSessionAndGoSolo()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsClient || nm.IsServer))
        {
            nm.OnClientDisconnectCallback -= OnClientDisconnect;
            nm.Shutdown();
        }
        CancelSession();
        Status = "Back to single player.";
    }

    /// A guest deliberately leaving: disconnect and go back to the main menu.
    /// OnAnySceneLoaded does the lobby cleanup once MainMenu loads.
    public void LeaveAndReturnToMenu()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsClient))
        {
            nm.OnClientDisconnectCallback -= OnClientDisconnect;   // this exit is intentional
            nm.Shutdown();
        }
        Time.timeScale = 1f;   // we may be leaving straight from the pause menu
        SceneManager.LoadScene("MainMenu");
    }

    /// Closes the lobby (host) or leaves it (guest) and returns to Idle.
    public async void CancelSession()
    {
        StopLoops();
        var lobby = _lobby;
        _lobby = null;
        // Must clear, or a session started later FROM THE MAIN MENU would skip
        // its scene load and try to connect in the menu scene.
        _inPlace = false;
        ClearGuestArrival();
        Current = State.Idle;
        Status = "";
        Code = "";
        _roster.Clear();

        if (lobby == null || LocalMode) return;
        try
        {
            if (IsHost) await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
            else if (AuthenticationService.Instance.IsSignedIn)
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MultiplayerSession] Cancel cleanup failed: {e}");
        }
    }

    void StopLoops()
    {
        if (_heartbeat != null) { StopCoroutine(_heartbeat); _heartbeat = null; }
        if (_poll != null) { StopCoroutine(_poll); _poll = null; }
    }

    void Fail(string message)
    {
        Current = State.Failed;
        Status = message;
        StopLoops();
    }
}
