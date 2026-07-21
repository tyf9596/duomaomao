using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum NetMode { Offline, Host, Client }

/// <summary>
/// Session lifecycle for the "always a host" model: solo play IS a hosted session
/// with no remote clients, so the match code has exactly one authority path online
/// and offline. Builds the NetworkManager in code (scenes stay dumb), picks a free
/// UDP port so several instances coexist on one machine (Multiplayer Play Mode),
/// and falls back to the pure-offline legacy path if the net prefabs are missing
/// or the transport fails. M3 replaces the dev join UI with Relay + Lobby.
/// </summary>
public class NetGame : MonoBehaviour
{
    public const ushort BasePort = 7777;

    public static NetGame Instance { get; private set; }
    public static NetMode Mode { get; private set; } = NetMode.Offline;
    public static ushort Port { get; private set; }

    /// <summary>True when this peer runs the match rules (offline or hosting).</summary>
    public static bool HasAuthority => Mode != NetMode.Client;

    static bool _pendingJoin;
    static string _joinAddress = "127.0.0.1";
    static ushort _joinPort = BasePort;

    GameObject _characterPrefab, _matchPrefab;

    public static GameObject CharacterPrefab => Instance != null ? Instance._characterPrefab : null;
    public static GameObject MatchPrefab => Instance != null ? Instance._matchPrefab : null;

    /// <summary>Idempotent bootstrap; MatchManager calls this before it spawns.</summary>
    public static void Ensure()
    {
        if (Instance == null)
        {
            var go = new GameObject("NetGame");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetGame>();
            Instance.BuildNetworkManager();
        }
        Instance.StartSessionIfIdle();
    }

    void BuildNetworkManager()
    {
        _characterPrefab = Resources.Load<GameObject>("Net/NetCharacter");
        _matchPrefab = Resources.Load<GameObject>("Net/NetMatch");
        if (_characterPrefab == null || _matchPrefab == null)
        {
            Debug.LogWarning("NetGame: net prefabs missing — running pure offline.");
            return;
        }

        var nmGo = new GameObject("NetworkManager");
        DontDestroyOnLoad(nmGo);
        var nm = nmGo.AddComponent<NetworkManager>();
        var utp = nmGo.AddComponent<UnityTransport>();
        nm.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = utp,
            EnableSceneManagement = false, // all peers load the arena themselves (M1: one map)
            ConnectionApproval = true
        };
        nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _characterPrefab });
        nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _matchPrefab });
        // default gate: only joinable while the lobby is open; MatchManager refines this
        nm.ConnectionApprovalCallback = (req, resp) =>
        {
            var mm = MatchManager.Instance;
            resp.Approved = mm == null || mm.Phase == MatchPhase.Lobby;
            resp.CreatePlayerObject = false;
        };
        nm.OnClientDisconnectCallback += OnClientDisconnect;
    }

    void StartSessionIfIdle()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { Mode = NetMode.Offline; return; }
        if (nm.IsListening || nm.IsClient) return;

        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        if (_pendingJoin)
        {
            _pendingJoin = false;
            utp.SetConnectionData(_joinAddress, _joinPort);
            if (nm.StartClient()) { Mode = NetMode.Client; return; }
            Debug.LogWarning("NetGame: StartClient failed — hosting solo instead.");
        }

        Port = FindFreeUdpPort(BasePort);
        utp.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        Mode = nm.StartHost() ? NetMode.Host : NetMode.Offline;
        if (Mode == NetMode.Offline)
            Debug.LogWarning("NetGame: StartHost failed — running pure offline.");
    }

    static ushort FindFreeUdpPort(ushort start)
    {
        for (ushort p = start; p < start + 10; p++)
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    s.Bind(new IPEndPoint(IPAddress.Any, p));
                return p;
            }
            catch (SocketException) { }
        }
        return start;
    }

    void OnClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        // we were a pure client and lost the host — reload back into our own solo lobby
        if (Mode == NetMode.Client && (clientId == nm.LocalClientId || clientId == NetworkManager.ServerClientId))
            RestartAndReload();
    }

    /// <summary>Dev flow: tear down our own session and join another instance on this machine.</summary>
    public static void JoinLocal(string address, ushort port)
    {
        _pendingJoin = true;
        _joinAddress = address;
        _joinPort = port;
        RestartAndReload();
    }

    /// <summary>Shut the session down and reload the arena — used by PLAY AGAIN, JOIN and
    /// host-loss recovery. The scene-load hook re-bootstraps a fresh session.</summary>
    public static void RestartAndReload()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsClient)) nm.Shutdown();
        Mode = NetMode.Offline;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        float s = Mathf.Max(1f, Screen.dpi / 96f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        GUILayout.BeginArea(new Rect(8, 8, 250, 120));
        var nm = NetworkManager.Singleton;
        int players = nm != null && nm.IsServer ? nm.ConnectedClientsList.Count : -1;
        GUILayout.Label("NET " + Mode + (Mode == NetMode.Host ? " @" + Port + "  ppl:" + players : ""));
        if (Mode == NetMode.Host && Port != BasePort && GUILayout.Button("JOIN 127.0.0.1:" + BasePort))
            JoinLocal("127.0.0.1", BasePort);
        if (Mode == NetMode.Client && GUILayout.Button("LEAVE"))
            RestartAndReload();
        GUILayout.EndArea();
        GUI.matrix = Matrix4x4.identity;
    }
#endif
}
