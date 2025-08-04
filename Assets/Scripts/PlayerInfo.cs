using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PlayerInfo : MonoBehaviour
{
    // UI text displaying the player's name
    [SerializeField] private TMP_Text playerName;

    // Stored Steam username and ID
    public string steamName;
    public ulong steamId;

    // UI element for ready status indicator
    public GameObject readyImage;
    public bool isReady;

    private void Start()
    {
        // Hide ready indicator initially
        readyImage.SetActive(false);
        // Set displayed name to Steam name
        playerName.text = steamName;
    }
}