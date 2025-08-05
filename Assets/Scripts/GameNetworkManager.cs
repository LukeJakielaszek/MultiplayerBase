// GameNetworkManager.cs (with lobby name join)
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;          // for UnityTransport
using UnityEngine.UI;                        // for UI Button
using TMPro;                                 // for TMP_InputField
using Steamworks;
using Steamworks.Data;
using Netcode.Transports.Facepunch;          // for FacepunchTransport

public class GameNetworkManager : MonoBehaviour
{
    public enum TransportMode { Local, Steam }

    [Header("Mode")]
    [SerializeField]
    private TransportMode transportMode = TransportMode.Steam;

    [Header("Transports")]
    [SerializeField]
    private FacepunchTransport steamTransport;
    [SerializeField]
    private UnityTransport localTransport;

    [Header("Lobby UI")]
    [SerializeField]
    private TMP_InputField lobbyNameInput;  // used as lobby name or local address:port
    [SerializeField]
    private Button joinButton;

    public static GameNetworkManager instance { get; private set; }
    private Lobby? currentLobby = null;

    private void Awake()
    {
        // Singleton setup
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Configure the chosen transport
        var chosenTransport = transportMode == TransportMode.Steam
            ? (NetworkTransport)steamTransport
            : (NetworkTransport)localTransport;
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = chosenTransport;

        // Register Steam callbacks if using Steam mode
        if (transportMode == TransportMode.Steam)
            RegisterSteamCallbacks();
    }

    private void Start()
    {
        // Hook up the Join button listener
        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinButtonPressed);
    }

    private void OnDestroy()
    {
        // Unregister Steam callbacks if needed
        if (transportMode == TransportMode.Steam)
            UnregisterSteamCallbacks();

        // Unhook Netcode callbacks
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnApplicationQuit()
    {
        Disconnected();
    }

    // Called when the join button is pressed
    private void OnJoinButtonPressed()
    {
        if (transportMode == TransportMode.Steam)
        {
            // Join lobby by name
            JoinSteamLobbyByName(lobbyNameInput.text.Trim());
        }
        else
        {
            // Local UDP: expect "address:port"
            var parts = lobbyNameInput.text.Split(':');
            if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port))
            {
                localTransport.ConnectionData.Address = parts[0];
                localTransport.ConnectionData.Port = port;
                StartClient();
            }
            else
            {
                Debug.LogError("Invalid address:port for local join");
            }
        }
    }

    /// <summary>
    /// Starts hosting a lobby. Hosts on local UDP or Steam with the provided name.
    /// </summary>
    public async void StartHost(int maxMembers)
    {
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.StartHost();
        GameManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;

        if (transportMode == TransportMode.Steam)
        {
            currentLobby = await SteamMatchmaking.CreateLobbyAsync(maxMembers);
            if (currentLobby.HasValue)
                currentLobby.Value.SetData("name", lobbyNameInput.text.Trim());
        }
        else
        {
            // Display local address:port for clients
            var cd = localTransport.ConnectionData;
            lobbyNameInput.text = $"{cd.Address}:{cd.Port}";
            Debug.Log($"Local UDP host started at {cd.Address}:{cd.Port}");
        }
    }

    /// <summary>
    /// Starts a client connection to the host.
    /// </summary>
    public void StartClient(SteamId steamOwnerId = default)
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        if (transportMode == TransportMode.Steam)
            steamTransport.targetSteamId = steamOwnerId;

        NetworkManager.Singleton.StartClient();
        Debug.Log(transportMode == TransportMode.Steam
            ? "Steam client started"
            : "Local UDP client started");
    }

    /// <summary>
    /// Clean up and disconnect from lobby or server.
    /// </summary>
    public void Disconnected()
    {
        if (transportMode == TransportMode.Steam)
            currentLobby?.Leave();

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.IsHost)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        else
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

        NetworkManager.Singleton.Shutdown(true);
        GameManager.instance.ClearChat();
        GameManager.instance.Disconnected();
        Debug.Log("Disconnected");
    }

    /// <summary>
    /// Searches for and joins a Steam lobby by its 'name' metadata.
    /// </summary>
    private async void JoinSteamLobbyByName(string lobbyName)
    {
        // Build a query for Steam lobbies filtered by our 'name' metadata
        var query = SteamMatchmaking.LobbyList
            .WithKeyValue("name", lobbyName)
            .WithMaxResults(1);

        // Execute the query and await matching lobbies
        Lobby[] results = await query.RequestAsync();
        if (results == null || results.Length == 0)
        {
            Debug.LogError($"No Steam lobby found with name '{lobbyName}'");
            return;
        }

        // Join the first matching lobby
        var lobby = results[0];
        RoomEnter joinResult = await lobby.Join();
        if (joinResult != RoomEnter.Success)
        {
            Debug.LogError($"Failed to join Steam lobby '{lobbyName}': {joinResult}");
            return;
        }

        currentLobby = lobby;
        GameManager.instance.ConnectedAsClient();
        Debug.Log($"Joined Steam lobby '{lobbyName}'");
    }

    // ── Netcode Callbacks ──────────────────────────────────
    private void OnServerStarted()
    {
        Debug.Log("Host started");
        GameManager.instance.HostCreated();
    }

    private void OnClientConnected(ulong clientId)
    {
        // Determine Steam or local identity
        ulong steamId = transportMode == TransportMode.Steam
            ? SteamClient.SteamId
            : 0;
        string steamName = transportMode == TransportMode.Steam
            ? SteamClient.Name
            : "LocalUser";

        // Register new client
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(steamId, steamName, clientId);
        GameManager.instance.myClientId = clientId;
        NetworkTransmission.instance.IsTheClientReadyServerRPC(false, clientId);

        // Switch to the lobby UI now that we are connected
        GameManager.instance.ConnectedAsClient();

        Debug.Log($"Client connected: {clientId}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        // If host (clientId==0) disconnected, cleanup
        if (clientId == 0)
            Disconnected();
    }

    // ── Steam Callbacks ───────────────────────────────────
    private void RegisterSteamCallbacks()
    {
        SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeft;
        SteamMatchmaking.OnLobbyInvite += OnLobbyInvite;
        SteamMatchmaking.OnLobbyGameCreated += OnLobbyGameCreated;
        SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
    }

    private void UnregisterSteamCallbacks()
    {
        SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeft;
        SteamMatchmaking.OnLobbyInvite -= OnLobbyInvite;
        SteamMatchmaking.OnLobbyGameCreated -= OnLobbyGameCreated;
        SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
    }

    private void OnLobbyCreated(Result result, Lobby lobby)
    {
        if (result != Result.OK)
        {
            Debug.LogError("Steam lobby creation failed");
            return;
        }
        lobby.SetPublic();
        lobby.SetJoinable(true);
        lobby.SetGameServer(lobby.Owner.Id);
        Debug.Log("Steam lobby created");
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(
            SteamClient.SteamId,
            SteamClient.Name,
            NetworkManager.Singleton.LocalClientId
        );
    }

    private void OnLobbyEntered(Lobby lobby)
    {
        currentLobby = lobby;
        if (!NetworkManager.Singleton.IsHost)
            StartClient(lobby.Owner.Id);
    }

    private void OnLobbyMemberJoined(Lobby lobby, Friend friend)
    {
        Debug.Log($"{friend.Name} joined Steam lobby");
    }

    private void OnLobbyMemberLeft(Lobby lobby, Friend friend)
    {
        Debug.Log($"{friend.Name} left Steam lobby");
        GameManager.instance.SendMessageToChat($"{friend.Name} has left", friend.Id, true);
        NetworkTransmission.instance.RemoveMeFromDictionaryServerRPC(friend.Id);
    }

    private void OnLobbyInvite(Friend friend, Lobby lobby)
    {
        Debug.Log($"Invite from {friend.Name}");
    }

    private void OnLobbyGameCreated(Lobby lobby, uint ip, ushort port, SteamId steamId)
    {
        Debug.Log("Steam lobby game created");
        GameManager.instance.SendMessageToChat(
            "Lobby game created",
            NetworkManager.Singleton.LocalClientId,
            true
        );
    }

    private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId steamId)
    {
        RoomEnter enterResult = await lobby.Join();
        if (enterResult != RoomEnter.Success)
        {
            Debug.LogError("Failed to join Steam lobby via invite");
            return;
        }
        currentLobby = lobby;
        GameManager.instance.ConnectedAsClient();
        Debug.Log("Joined Steam lobby via invite");
    }
}
