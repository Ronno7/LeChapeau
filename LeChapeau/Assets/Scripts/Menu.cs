using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

public class Menu : MonoBehaviourPunCallbacks
{
    [Header("Screens")]
    public GameObject mainScreen;
    public GameObject lobbyScreen;

    [Header("Main Screen")]
    public Button createRoomButton;
    public Button joinRoomButton;

    [Header("Lobby Screen")]
    public TextMeshProUGUI playerListText;
    public Button startGameButton;

    void Awake()
    {
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        RefreshMainButtons();
    }

    void Start()
    {
        RefreshMainButtons();
        SetScreen(mainScreen);
    }

    // NEW: keep checking until connected so the buttons light up as soon as ready
    void Update()
    {
        if (!PhotonNetwork.IsConnectedAndReady)
            RefreshMainButtons();
    }

    void RefreshMainButtons()
    {
        bool ready = PhotonNetwork.IsConnectedAndReady;
        createRoomButton.interactable = ready;
        joinRoomButton.interactable = ready;
    }

    public override void OnConnectedToMaster() => RefreshMainButtons();

    public override void OnDisconnected(DisconnectCause cause)
    {
        createRoomButton.interactable = false;
        joinRoomButton.interactable = false;
        SetScreen(mainScreen);
    }

    void SetScreen(GameObject screen)
    {
        mainScreen.SetActive(false);
        lobbyScreen.SetActive(false);
        screen.SetActive(true);
    }

    public void OnCreateRoomButton(TMP_InputField roomNameInput)
    {
        if (!PhotonNetwork.IsConnectedAndReady) return;
        var name = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        NetworkManager.instance.CreateRoom(name);
    }

    public void OnJoinRoomButton(TMP_InputField roomNameInput)
    {
        if (!PhotonNetwork.IsConnectedAndReady) return;
        var name = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        NetworkManager.instance.JoinRoom(name);
    }

    public void OnPlayerNameUpdate(TMP_InputField playerNameInput)
    {
        PhotonNetwork.NickName = playerNameInput.text.Trim();
    }

    public override void OnJoinedRoom()
    {
        SetScreen(lobbyScreen);
        photonView.RPC(nameof(UpdateLobbyUI), RpcTarget.All);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer) => UpdateLobbyUI();
    public override void OnMasterClientSwitched(Player newMasterClient) => UpdateLobbyUI();

    [PunRPC]
    public void UpdateLobbyUI()
    {
        playerListText.text = "";
        foreach (Player p in PhotonNetwork.PlayerList)
            playerListText.text += p.NickName + "\n";
        startGameButton.interactable = PhotonNetwork.IsMasterClient;
    }

    public override void OnLeftRoom()
    {
        SetScreen(mainScreen);
        RefreshMainButtons();
    }

    public void OnLeaveLobbyButton() => PhotonNetwork.LeaveRoom();
    public void OnStartGameButton() => NetworkManager.instance.StartMatch("Game");
}
