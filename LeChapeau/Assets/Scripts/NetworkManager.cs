using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime; // for ClientState
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;


[DisallowMultipleComponent]
public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager instance;

    void Awake()
    {
        // Singleton
        if (instance != null && instance != this)
        {
            gameObject.SetActive(false);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        // This object must NOT have a PhotonView (prevents duplicate ViewIDs across scenes)
        var pv = GetComponent<PhotonView>();
        if (pv != null)
        {
            Destroy(pv);
            Debug.LogWarning("[PUN] Removed PhotonView from NetworkManager (DDOL). Callbacks work without it.");
        }

        // PUN scene sync & ticks
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.SendRate = 60;
        PhotonNetwork.SerializationRate = 30;
        PhotonNetwork.IsMessageQueueRunning = true;

        // Connect only if truly disconnected
        if (PhotonNetwork.NetworkClientState == ClientState.Disconnected)
            PhotonNetwork.ConnectUsingSettings();
    }

    void Start() { /* no-op; connection handled in Awake */ }

    public void CreateRoom(string roomName)
    {
        PhotonNetwork.CreateRoom(roomName);
    }

    public void JoinRoom(string roomName)
    {
        PhotonNetwork.JoinRoom(roomName);
    }

    /// <summary>
    /// Start the match / change scene. Only the Master actually loads the level.
    /// With AutomaticallySyncScene=true, all clients follow automatically.
    /// </summary>
    public void StartMatch(string sceneName)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.LoadLevel(sceneName);
    }

    // Kept for backward compatibility with existing RPC calls in your project.
    // If something still does PhotonView.RPC("ChangeScene", RpcTarget.All, "Game"),
    // only Master will act; others safely ignore.
    [PunRPC]
    public void ChangeScene(string sceneName)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.LoadLevel(sceneName);
    }

    // Safe helper for room custom properties (prevents calls while Leaving/NotInRoom)
    public static bool TrySetRoomProps(Hashtable ht)
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
            return true;
        }
        return false;
    }

    // ---- Callbacks ----
    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to master server");
    }

    public override void OnCreatedRoom()
    {
        Debug.Log("Created room: " + PhotonNetwork.CurrentRoom.Name);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"CreateRoom failed ({returnCode}): {message}");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"JoinRoom failed ({returnCode}): {message}");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Disconnected: {cause}");
    }
}
