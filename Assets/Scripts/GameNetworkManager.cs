using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using Netcode.Transports.Facepunch;

public class GameNetworkManager : MonoBehaviour
{
    // Singleton instance for global access
    public static GameNetworkManager instance { get; private set; } = null;

    // Transport component used for Facepunch (Steam) networking
    private FacepunchTransport transport = null;

    // Currently active Steam lobby (null if none)
    public Lobby? currentLobby { get; private set; } = null;

    // Steam ID of the host (used when connecting clients)
    public ulong hostId;

    private void Awake()
    {
        // Implement singleton pattern: keep only one instance
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            // Destroy duplicate manager
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // Cache the FacepunchTransport component for later use
        transport = GetComponent<FacepunchTransport>();

        // Register Steam callbacks for lobby lifecycle and invites
        SteamMatchmaking.OnLobbyCreated += SteamMatchmaking_OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered += SteamMatchmaking_OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined += SteamMatchmaking_OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave += SteamMatchmaking_OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyInvite += SteamMatchmaking_OnLobbyInvite;
        SteamMatchmaking.OnLobbyGameCreated += SteamMatchmaking_OnLobbyGameCreated;
        SteamFriends.OnGameLobbyJoinRequested += SteamFriends_OnGameLobbyJoinRequested;
    }

    private void OnDestroy()
    {
        // Unregister Steam callbacks to avoid memory leaks
        SteamMatchmaking.OnLobbyCreated -= SteamMatchmaking_OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered -= SteamMatchmaking_OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= SteamMatchmaking_OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= SteamMatchmaking_OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyInvite -= SteamMatchmaking_OnLobbyInvite;
        SteamMatchmaking.OnLobbyGameCreated -= SteamMatchmaking_OnLobbyGameCreated;
        SteamFriends.OnGameLobbyJoinRequested -= SteamFriends_OnGameLobbyJoinRequested;

        // Unregister Netcode callbacks
        if (NetworkManager.Singleton == null)
        {
            return;
        }
        NetworkManager.Singleton.OnServerStarted -= Singleton_OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback -= Singleton_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback -= Singleton_OnClientDisconnectCallback;
    }

    private void OnApplicationQuit()
    {
        // Clean up when application quits
        Disconnected();
    }

    // Callback when the user accepts an invite or joins a friend's lobby
    private async void SteamFriends_OnGameLobbyJoinRequested(Lobby _lobby, SteamId _steamId)
    {
        RoomEnter result = await _lobby.Join();
        if (result != RoomEnter.Success)
        {
            Debug.Log("Failed to join lobby");
        }
        else
        {
            // Store reference and notify GameManager
            currentLobby = _lobby;
            GameManager.instance.ConnectedAsClient();
            Debug.Log("Joined Lobby");
        }
    }

    // Callback when a game lobby is created on Steam (host side)
    private void SteamMatchmaking_OnLobbyGameCreated(Lobby _lobby, uint _ip, ushort _port, SteamId _steamId)
    {
        Debug.Log("Lobby game setup complete");
        // Inform chat that lobby is ready
        GameManager.instance.SendMessageToChat($"Lobby was created", NetworkManager.Singleton.LocalClientId, true);
    }

    // Callback when receiving an invite from a friend
    private void SteamMatchmaking_OnLobbyInvite(Friend _steamId, Lobby _lobby)
    {
        Debug.Log($"Invite from {_steamId.Name}");
    }

    // Callback when a member leaves the lobby
    private void SteamMatchmaking_OnLobbyMemberLeave(Lobby _lobby, Friend _steamId)
    {
        Debug.Log("Member left the lobby");
        GameManager.instance.SendMessageToChat($"{_steamId.Name} has left", _steamId.Id, true);
        // Remove leaving member from server dictionary
        NetworkTransmission.instance.RemoveMeFromDictionaryServerRPC(_steamId.Id);
    }

    // Callback when a new member joins the lobby
    private void SteamMatchmaking_OnLobbyMemberJoined(Lobby _lobby, Friend _steamId)
    {
        Debug.Log("Member joined the lobby");
    }

    // Callback when entering a lobby (after creating or joining)
    private void SteamMatchmaking_OnLobbyEntered(Lobby _lobby)
    {
        // If this client is the host, skip connecting
        if (NetworkManager.Singleton.IsHost)
        {
            return;
        }
        // Start as client connecting to host's Steam ID
        StartClient(currentLobby.Value.Owner.Id);
    }

    // Callback after attempting to create a lobby
    private void SteamMatchmaking_OnLobbyCreated(Result _result, Lobby _lobby)
    {
        if (_result != Result.OK)
        {
            Debug.Log("Lobby was not created");
            return;
        }
        // Make lobby public and joinable, then register game server info
        _lobby.SetPublic();
        _lobby.SetJoinable(true);
        _lobby.SetGameServer(_lobby.Owner.Id);
        Debug.Log($"Lobby created successfully");
        // Add host to the network dictionary
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, "FakeSteamName", NetworkManager.Singleton.LocalClientId);
    }

    // Public method to start hosting with a maximum number of members
    public async void StartHost(int _maxMembers)
    {
        // Register callback for when the server starts
        NetworkManager.Singleton.OnServerStarted += Singleton_OnServerStarted;
        NetworkManager.Singleton.StartHost();
        // Store own client ID
        GameManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
        // Create a Steam lobby for other players
        currentLobby = await SteamMatchmaking.CreateLobbyAsync(_maxMembers);
    }

    // Public method to start as a client connecting to a host
    public void StartClient(SteamId _sId)
    {
        // Register client connection callbacks
        NetworkManager.Singleton.OnClientConnectedCallback += Singleton_OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += Singleton_OnClientDisconnectCallback;
        // Set the Steam ID of the target host
        transport.targetSteamId = _sId;
        // Store own client ID
        GameManager.instance.myClientId = NetworkManager.Singleton.LocalClientId;
        // Attempt to start the client
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("Client has started");
        }
    }

    // Clean up network and lobby when disconnecting
    public void Disconnected()
    {
        // Leave Steam lobby if in one
        currentLobby?.Leave();
        if (NetworkManager.Singleton == null)
        {
            return;
        }
        // Unregister server or client callbacks
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.OnServerStarted -= Singleton_OnServerStarted;
        }
        else
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= Singleton_OnClientConnectedCallback;
        }
        // Shutdown Netcode and clear UI
        NetworkManager.Singleton.Shutdown(true);
        GameManager.instance.ClearChat();
        GameManager.instance.Disconnected();
        Debug.Log("Disconnected from network");
    }

    // Callback when a client disconnects
    private void Singleton_OnClientDisconnectCallback(ulong _clientId)
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= Singleton_OnClientDisconnectCallback;
        // If the host disconnected (clientId 0), perform full cleanup
        if (_clientId == 0)
        {
            Disconnected();
        }
    }

    // Callback when a client successfully connects
    private void Singleton_OnClientConnectedCallback(ulong _clientId)
    {
        // Register new client in dictionary and notify ready status
        NetworkTransmission.instance.AddMeToDictionaryServerRPC(SteamClient.SteamId, SteamClient.Name, _clientId);
        GameManager.instance.myClientId = _clientId;
        NetworkTransmission.instance.IsTheClientReadyServerRPC(false, _clientId);
        Debug.Log($"Client has connected: AnotherFakeSteamName");
    }

    // Callback when host server has started
    private void Singleton_OnServerStarted()
    {
        Debug.Log("Host started");
        GameManager.instance.HostCreated();
    }
}