using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    // Singleton for global access
    public static GameManager instance;

    // UI panels for the main menu and lobby
    [SerializeField] private GameObject multiMenu, multiLobby;

    // UI elements for chat
    [SerializeField] private GameObject chatPanel, textObject;
    [SerializeField] private TMP_InputField inputField;

    // UI container and prefab for displaying player cards
    [SerializeField] private GameObject playerFieldBox, playerCardPrefab;
    // Buttons for ready/not ready and game start
    [SerializeField] private GameObject readyButton, NotreadyButton, startButton;

    // Mapping of client IDs to their UI GameObjects
    public Dictionary<ulong, GameObject> playerInfo = new Dictionary<ulong, GameObject>();

    // Maximum number of chat messages to keep
    [SerializeField]
    private int maxMessages = 20;

    // Internal list to track chat history
    private List<Message> messageList = new List<Message>();

    // Network state flags
    public bool connected;
    public bool inGame;
    public bool isHost;
    public ulong myClientId;

    private void Awake()
    {
        // Initialize singleton instance
        if (instance != null)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }

    private void Update()
    {
        // Handle chat input activation and sending
        if (inputField.text != "")
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                if (inputField.text == " ")
                {
                    // Reset empty input
                    inputField.text = "";
                    inputField.DeactivateInputField();
                    return;
                }
                // Send chat message via server RPC
                NetworkTransmission.instance.IWishToSendAChatServerRPC(inputField.text, myClientId);
                inputField.text = "";
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                // Activate the input field if not typing
                inputField.ActivateInputField();
                inputField.text = " ";
            }
        }
    }

    // Internal structure to track each chat message
    public class Message
    {
        public string text;
        public TMP_Text textObject;
    }

    // Display a new message in the chat panel
    public void SendMessageToChat(string _text, ulong _fromWho, bool _server)
    {
        // Remove oldest message if exceeding max
        if (messageList.Count >= maxMessages)
        {
            Destroy(messageList[0].textObject.gameObject);
            messageList.Remove(messageList[0]);
        }
        Message newMessage = new Message();
        string _name = "Server";

        // Determine sender name if not server
        if (!_server)
        {
            if (playerInfo.ContainsKey(_fromWho))
            {
                _name = playerInfo[_fromWho].GetComponent<PlayerInfo>().steamName;
            }
        }

        newMessage.text = _name + ": " + _text;

        // Instantiate UI text object and add to panel
        GameObject newText = Instantiate(textObject, chatPanel.transform);
        newMessage.textObject = newText.GetComponent<TMP_Text>();
        newMessage.textObject.text = newMessage.text;

        messageList.Add(newMessage);
    }

    // Clear all chat messages from UI and list
    public void ClearChat()
    {
        messageList.Clear();
        GameObject[] chat = GameObject.FindGameObjectsWithTag("ChatMessage");
        foreach (GameObject chit in chat)
        {
            Destroy(chit);
        }
        Debug.Log("Clearing chat UI");
    }

    // Called when host is initialized successfully
    public void HostCreated()
    {
        // Show lobby UI and hide menu
        multiMenu.SetActive(false);
        multiLobby.SetActive(true);
        isHost = true;
        connected = true;
    }

    // Called when client successfully connects to a lobby
    public void ConnectedAsClient()
    {
        multiMenu.SetActive(false);
        multiLobby.SetActive(true);
        isHost = false;
        connected = true;
    }

    // Reset UI and state when disconnected
    public void Disconnected()
    {
        // Clear player list and UI cards
        playerInfo.Clear();
        GameObject[] playercards = GameObject.FindGameObjectsWithTag("PlayerCard");
        foreach (GameObject card in playercards)
        {
            Destroy(card);
        }

        // Show main menu
        multiMenu.SetActive(true);
        multiLobby.SetActive(false);
        isHost = false;
        connected = false;
    }

    // Instantiate a new player card and add to dictionary
    public void AddPlayerToDictionary(ulong _clientId, string _steamName, ulong _steamId)
    {
        if (!playerInfo.ContainsKey(_clientId))
        {
            // Create UI element for player
            PlayerInfo _pi = Instantiate(playerCardPrefab, playerFieldBox.transform).GetComponent<PlayerInfo>();
            _pi.steamId = _steamId;
            _pi.steamName = _steamName;
            playerInfo.Add(_clientId, _pi.gameObject);
        }
    }

    // Broadcast current player info to all clients
    public void UpdateClients()
    {
        foreach (KeyValuePair<ulong, GameObject> _player in playerInfo)
        {
            ulong _steamId = _player.Value.GetComponent<PlayerInfo>().steamId;
            string _steamName = _player.Value.GetComponent<PlayerInfo>().steamName;
            ulong _clientId = _player.Key;

            NetworkTransmission.instance.UpdateClientsPlayerInfoClientRPC(_steamId, _steamName, _clientId);
        }
    }

    // Remove a player entry based on Steam ID
    public void RemovePlayerFromDictionary(ulong _steamId)
    {
        GameObject _value = null;
        ulong _key = 100;
        // Find the matching client
        foreach (KeyValuePair<ulong, GameObject> _player in playerInfo)
        {
            if (_player.Value.GetComponent<PlayerInfo>().steamId == _steamId)
            {
                _value = _player.Value;
                _key = _player.Key;
            }
        }
        if (_key != 100)
        {
            playerInfo.Remove(_key);
        }
        if (_value != null)
        {
            Destroy(_value);
        }
    }

    // Send ready/unready state to server
    public void ReadyButton(bool _ready)
    {
        NetworkTransmission.instance.IsTheClientReadyServerRPC(_ready, myClientId);
    }

    // Check if all players are ready before enabling start
    public bool CheckIfPlayersAreReady()
    {
        bool _ready = false;

        foreach (KeyValuePair<ulong, GameObject> _player in playerInfo)
        {
            if (!_player.Value.GetComponent<PlayerInfo>().isReady)
            {
                startButton.SetActive(false);
                return false;
            }
            else
            {
                startButton.SetActive(true);
                _ready = true;
            }
        }

        return _ready;
    }

    // Quit application (for standalone builds)
    public void Quit()
    {
        Application.Quit();
    }
}