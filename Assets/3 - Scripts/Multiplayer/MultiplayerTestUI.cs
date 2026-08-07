using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// OnGUI overlay for the LAN proof test: HOST / JOIN / SHUTDOWN, connection
/// status, player count. Also owns the session-start handoff (scene player
/// stands down, network players take over) and the joiner spawn pose.
/// Zero scene wiring beyond living on the NetworkManager object.
public class MultiplayerTestUI : MonoBehaviour
{
    public string planetName = "Humble Abode";
    public ushort port = 7777;
    public float joinSpawnUpOffset = 4f;
    public float joinSpawnSideOffset = 1.5f;

    string ipField = "127.0.0.1";
    string status = "";
    string lanIP = "?";
    int connectedCount;

    void Awake()
    {
        // Two instances side by side on one desktop: the unfocused one must
        // keep simulating or the connection stalls.
        Application.runInBackground = true;
        lanIP = GetLanIPv4();
    }

    void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    void OnGUI()
    {
        float s = Mathf.Max(1f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

        GUILayout.BeginArea(new Rect(10, 10, 380, 300), GUI.skin.box);
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            GUILayout.Label("NetworkManager missing!");
            GUILayout.EndArea();
            return;
        }

        if (!nm.IsClient && !nm.IsServer)
        {
            GUILayout.Label("MULTIPLAYER TEST");
            if (GUILayout.Button("HOST", GUILayout.Height(32))) StartHost();
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP:", GUILayout.Width(24));
            ipField = GUILayout.TextField(ipField);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("JOIN", GUILayout.Height(32))) StartClient();
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        }
        else
        {
            GUILayout.Label(status);
            if (nm.IsServer) GUILayout.Label($"players: {connectedCount}");
            GUILayout.Label(DebugLine());
            if (GUILayout.Button("SHUTDOWN", GUILayout.Height(26)))
            {
                nm.Shutdown();
                status = "session ended — single player continues; HOST/JOIN again anytime";
            }
        }
        GUILayout.EndArea();
    }

    void StartHost()
    {
        var nm = NetworkManager.Singleton;
        var utp = nm.GetComponent<UnityTransport>();
        CelestialBody planet = FindPlanet();
        if (planet == null)
        {
            status = $"planet '{planetName}' not found — wrong scene?";
            return;
        }

        utp.SetConnectionData(lanIP, port, "0.0.0.0"); // 0.0.0.0 or LAN clients can never connect
        if (nm.StartHost())
            status = $"MULTIPLAYER ACTIVE — hosting on {lanIP}:{port}";
        else
            status = "HOST FAILED to start — see log";
    }

    void StartClient()
    {
        var nm = NetworkManager.Singleton;
        var utp = nm.GetComponent<UnityTransport>();
        CelestialBody planet = FindPlanet();
        if (planet == null)
        {
            status = $"planet '{planetName}' not found — wrong scene?";
            return;
        }

        string ip = ipField.Trim();
        utp.SetConnectionData(ip, port);
        if (nm.StartClient())
        {
            status = $"CONNECTING to {ip}:{port} ...";
            StartCoroutine(ClientConnectTimeout(12f, ip));
        }
        else
            status = "JOIN FAILED to start — see log";
    }

    IEnumerator ClientConnectTimeout(float seconds, string ip)
    {
        var nm = NetworkManager.Singleton;
        float deadline = Time.unscaledTime + seconds;
        while (Time.unscaledTime < deadline)
        {
            if (nm.IsConnectedClient) yield break;
            if (!nm.IsClient) yield break; // disconnect callback already reported
            yield return null;
        }
        status = $"JOIN TIMED OUT after {seconds:0}s — check IP + firewall (UDP {port})";
        nm.Shutdown();
    }

    void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            connectedCount = nm.ConnectedClientsList.Count;
            if (clientId != nm.LocalClientId)
                StartCoroutine(SendJoinerSpawnPose(clientId));
        }
        else if (clientId == nm.LocalClientId)
        {
            status = $"CONNECTED to {ipField.Trim()}:{port}";
        }
    }

    void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            connectedCount = nm.ConnectedClientsList.Count;
        }
        else
        {
            string reason = nm.DisconnectReason;
            status = string.IsNullOrEmpty(reason)
                ? $"DISCONNECTED / join failed — check IP + firewall (UDP {port})"
                : $"DISCONNECTED: {reason}";
        }
    }

    /// Host-side: joiner spawns a few meters above the host, tangent-offset so
    /// it can't land inside them — computed and sent in planet-local space.
    IEnumerator SendJoinerSpawnPose(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        float deadline = Time.unscaledTime + 10f;
        while (Time.unscaledTime < deadline)
        {
            PlanetRelativeSync hostSync = null;
            NetworkObject joinerObj = null;
            if (nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
                hostSync = nm.LocalClient.PlayerObject.GetComponent<PlanetRelativeSync>();
            if (nm.ConnectedClients.TryGetValue(clientId, out var cc))
                joinerObj = cc.PlayerObject;

            if (hostSync != null && joinerObj != null &&
                hostSync.TryGetCurrentLocalPose(out Vector3 hostLocalPos, out Quaternion hostLocalRot))
            {
                // Planet-local frame: the planet's center is the origin, so
                // radial up is simply the normalized local position.
                Vector3 up = hostLocalPos.normalized;
                Vector3 side = Vector3.Cross(up, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(up, Vector3.right);
                side.Normalize();
                Vector3 spawnLocal = hostLocalPos + up * joinSpawnUpOffset + side * joinSpawnSideOffset;

                joinerObj.GetComponent<PlanetRelativeSync>().ServerSetSpawnPose(spawnLocal, hostLocalRot);
                Debug.Log($"[MP] Spawn pose for client {clientId} set (planet-local {spawnLocal})");
                yield break;
            }
            yield return null;
        }
        Debug.LogWarning("[MultiplayerTestUI] Could not send joiner spawn pose within 10s.");
    }

    string debugLine = "";
    float nextDebugTime;
    float nextAltLogTime;

    // Feet clearance above whatever the physics ground is right below this
    // avatar. ~0 = standing on ground; negative = sunk; positive = floating.
    float FeetGap(Transform avatar, CelestialBody planet)
    {
        Vector3 up = (avatar.position - planet.transform.position).normalized;
        var hits = Physics.RaycastAll(avatar.position + up * 2f, -up, 25f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(avatar)) continue; // not the avatar itself
            return (hit.distance - 2f) - 0.965f; // root sits 0.965 above the feet
        }
        return float.NaN;
    }

    // Live session diagnostics: which bodies exist and what state each sync is
    // in. Rebuilt every 0.5s, not per repaint.
    string DebugLine()
    {
        if (Time.unscaledTime >= nextDebugTime)
        {
            nextDebugTime = Time.unscaledTime + 0.5f;
            int scenePlayers = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var pc in FindObjectsOfType<PlayerController>(true))
                if (pc.GetComponent<NetworkObject>() == null) scenePlayers++;
            int netPlayers = 0;
            var planet = FindPlanet();
            foreach (var s in FindObjectsOfType<PlanetRelativeSync>())
            {
                netPlayers++;
                float gap = planet != null ? FeetGap(s.transform, planet) : float.NaN;
                if (s.IsOwner)
                    sb.Append(s.OwnerPoseReady
                        ? $"you: ok alt={s.ShownAltitude:F2} feetGap={gap:F2}\n"
                        : "you: WAITING FOR SPAWN POSE\n")
;
                else
                    sb.Append($"P{s.OwnerClientId + 1}: {(s.RemotePlaced ? "visible" : (s.RemotePoseValid ? "placing" : "no pose yet"))}" +
                              $" sent={s.SyncedAltitude:F2} shown={s.ShownAltitude:F2} feetGap={gap:F2}\n");
            }
            debugLine = $"puppets: {netPlayers}, real player rigs: {scenePlayers} (must be 1)\n{sb}";

            // Numbers into Player.log so build sessions leave evidence.
            if (Time.unscaledTime >= nextAltLogTime)
            {
                nextAltLogTime = Time.unscaledTime + 5f;
                string planetState = planet != null
                    ? $"planet rot={planet.transform.rotation:F4} scale={planet.transform.lossyScale:F3}"
                    : "planet MISSING";
                Debug.Log($"[MP][ALT] {debugLine.Replace('\n', ' ')} | {planetState}");
            }
        }
        return debugLine;
    }

    CelestialBody FindPlanet()
    {
        foreach (var b in NBodySimulation.Bodies)
            if (b != null && b.bodyName == planetName) return b;
        return null;
    }

    static string GetLanIPv4()
    {
        var candidates = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = addr.Address.ToString();
                    if (s.StartsWith("169.254.")) continue; // link-local
                    candidates.Add(s);
                }
            }
        }
        catch { }
        // Prefer typical home-LAN ranges over virtual adapters (Hyper-V, VPN).
        foreach (var c in candidates) if (c.StartsWith("192.168.")) return c;
        foreach (var c in candidates) if (c.StartsWith("10.")) return c;
        return candidates.Count > 0 ? candidates[0] : "127.0.0.1";
    }
}
