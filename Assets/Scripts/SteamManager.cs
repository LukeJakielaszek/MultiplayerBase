// SteamManager.cs
using UnityEngine;
using Steamworks;

public class SteamManager : MonoBehaviour
{
    public static SteamManager instance;
    // Exposes whether Steam is initialized and ready
    public static bool Initialized => instance != null && instance.connectedToSteam;

    [SerializeField]
    private uint appID = 000000; // Replace with your actual App ID
    private bool connectedToSteam = false;

    private void Awake()
    {
        // Ensure singleton
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(this.gameObject);
            ConnectToSteam();
        }
        else
        {
            Destroy(this.gameObject);
        }
    }

    private void OnApplicationQuit()
    {
        DisconnectFromSteam();
    }

    private void Update()
    {
        if (connectedToSteam)
        {
            SteamClient.RunCallbacks();
        }
    }

    private void ConnectToSteam()
    {
        try
        {
            Debug.Log("Attempting Steam initialization");
            SteamClient.Init(appID);
            connectedToSteam = true;
            Debug.Log("Steam is up and running");
        }
        catch (System.Exception e)
        {
            connectedToSteam = false;
            Debug.LogError("Failed to connect to Steam:");
            Debug.LogError(e.ToString());
        }
    }

    public void DisconnectFromSteam()
    {
        if (!connectedToSteam)
        {
            Debug.Log("Skipping Steam shutdown, not currently connected.");
            return;
        }

        try
        {
            Debug.Log("Disconnecting from Steam");
            SteamClient.Shutdown();
            connectedToSteam = false;
            Debug.Log("Successfully disconnected from Steam");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to disconnect from Steam:");
            Debug.LogError(e.ToString());
        }
    }
}