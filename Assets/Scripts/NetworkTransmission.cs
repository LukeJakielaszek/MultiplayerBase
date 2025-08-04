using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkTransmission : NetworkBehaviour
{
    // Singleton instance for global access
    public static NetworkTransmission instance;

    private void Awake()
    {
        // Ensure single instance survives
        if (instance != null)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }

    // Server RPC to handle incoming chat messages
    [ServerRpc(RequireOwnership = false)]
    public void IWishToSendAChatServerRPC(string _message, ulong _fromWho)
    {
        // Relay message to all clients
        ChatFromServerClientRPC(_message, _fromWho);
    }

    // Client RPC to display chat messages from server
    [ClientRpc]
    private void ChatFromServerClientRPC(string _message, ulong _fromWho)
    {
        GameManager.instance.SendMessageToChat(_message, _fromWho, false);
    }

    // Server RPC to register a new player in the lobby dictionary
    [ServerRpc(RequireOwnership = false)]
    public void AddMeToDictionaryServerRPC(ulong _steamId, string _steamName, ulong _clientId)
    {
        // Notify chat and update GameManager
        GameManager.instance.SendMessageToChat($"{_steamName} has joined", _clientId, true);
        GameManager.instance.AddPlayerToDictionary(_clientId, _steamName, _steamId);
        GameManager.instance.UpdateClients();
    }

    // Server RPC to remove a player from the dictionary
    [ServerRpc(RequireOwnership = false)]
    public void RemoveMeFromDictionaryServerRPC(ulong _steamId)
    {
        // Broadcast removal to clients
        RemovePlayerFromDictionaryClientRPC(_steamId);
    }

    // Client RPC to handle player removal
    [ClientRpc]
    private void RemovePlayerFromDictionaryClientRPC(ulong _steamId)
    {
        Debug.Log("Removing client from UI");
        GameManager.instance.RemovePlayerFromDictionary(_steamId);
    }

    // Client RPC to update each client's view of player info
    [ClientRpc]
    public void UpdateClientsPlayerInfoClientRPC(ulong _steamId, string _steamName, ulong _clientId)
    {
        GameManager.instance.AddPlayerToDictionary(_clientId, _steamName, _steamId);
    }

    // Server RPC to broadcast ready state changes
    [ServerRpc(RequireOwnership = false)]
    public void IsTheClientReadyServerRPC(bool _ready, ulong _clientId)
    {
        AClientMightBeReadyClientRPC(_ready, _clientId);
    }

    // Client RPC to update ready visuals
    [ClientRpc]
    private void AClientMightBeReadyClientRPC(bool _ready, ulong _clientId)
    {
        foreach (KeyValuePair<ulong, GameObject> player in GameManager.instance.playerInfo)
        {
            if (player.Key == _clientId)
            {
                // Toggle ready indicator on the player's card
                player.Value.GetComponent<PlayerInfo>().isReady = _ready;
                player.Value.GetComponent<PlayerInfo>().readyImage.SetActive(_ready);
                // Host can log ready status checks
                if (NetworkManager.Singleton.IsHost)
                {
                    Debug.Log(GameManager.instance.CheckIfPlayersAreReady());
                }
            }
        }
    }
}