using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class GameManager : MonoBehaviourPunCallbacks
{
    // -------------------- Singleton --------------------
    public static GameManager instance;
    void Awake() => instance = this;

    // -------------------- Stats --------------------
    [Header("Stats")]
    public bool gameEnded = false;
    public float timeToWin = 15f;          // required hat time to win
    public float invincibleDuration = 1f;  // grace after pickup
    float hatPickupTime;

    // -------------------- Players & Spawns --------------------
    [Header("Players")]
    public string playerPrefabLocation = "Player";
    public Transform[] spawnPoints;

    public PlayerController[] players;  // indexed by join order; we search by id when needed
    public int playerWithHat = -1;
    int playersInGame = 0;

    // Unique spawn assignment (Master only)
    Queue<int> _freeSpawnIdx;

    // Fall debounce (Master only)
    Dictionary<int, double> _lastFallAt = new Dictionary<int, double>();
    [SerializeField] double fallDebounceSeconds = 1.0;

    // -------------------- Startup --------------------
    void Start()
    {
        players = new PlayerController[Mathf.Max(1, PhotonNetwork.PlayerList.Length)];

        if (PhotonNetwork.IsMasterClient)
        {
            // Prepare a shuffled queue of unique spawn indices.
            var order = Enumerable.Range(0, spawnPoints.Length)
                                  .OrderBy(_ => UnityEngine.Random.value);
            _freeSpawnIdx = new Queue<int>(order);
        }

        photonView.RPC("ImInGame", RpcTarget.AllBuffered);
    }

    // Called by each client after Start.
    [PunRPC]
    void ImInGame()
    {
        playersInGame++;

        if (playersInGame == PhotonNetwork.PlayerList.Length && PhotonNetwork.IsMasterClient)
            AssignInitialSpawns();
    }

    void AssignInitialSpawns()
    {
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (_freeSpawnIdx == null || _freeSpawnIdx.Count == 0)
            {
                // Refill if needed (shouldn't happen if you have enough points).
                var refill = Enumerable.Range(0, spawnPoints.Length)
                                       .OrderBy(_ => UnityEngine.Random.value);
                _freeSpawnIdx = new Queue<int>(refill);
            }

            int idx = _freeSpawnIdx.Dequeue();
            photonView.RPC("Client_SpawnAt", p, idx);
        }
    }

    // Runs only on the targeted client.
    [PunRPC]
    void Client_SpawnAt(int spawnIndex)
    {
        Vector3 pos = spawnPoints[Mathf.Clamp(spawnIndex, 0, spawnPoints.Length - 1)].position;

        GameObject playerObj = PhotonNetwork.Instantiate(playerPrefabLocation, pos, Quaternion.identity);
        var player = playerObj.GetComponent<PlayerController>();
        player.photonView.RPC("Initialize", RpcTarget.All, PhotonNetwork.LocalPlayer);
    }

    // -------------------- Player Registry --------------------
    public void RegisterPlayer(PlayerController pc)
    {
        // Store by (id-1) if array large enough; otherwise append in the first null slot.
        int idx = pc.id - 1;
        if (idx >= 0 && idx < players.Length) players[idx] = pc;
        else
        {
            for (int i = 0; i < players.Length; i++)
                if (players[i] == null) { players[i] = pc; break; }
        }
    }

    public bool TryGetPlayer(int playerId, out PlayerController p)
    {
        p = players.FirstOrDefault(x => x && x.id == playerId);
        return p != null;
    }

    public bool TryGetPlayer(GameObject playerObject, out PlayerController p)
    {
        p = players.FirstOrDefault(x => x && x.gameObject == playerObject);
        return p != null;
    }

    // -------------------- Hat Logic --------------------
    [PunRPC]
    public void GiveHat(int playerId, bool initialGive)
    {
        if (!initialGive && TryGetPlayer(playerWithHat, out var prev))
            prev.SetHat(false);

        playerWithHat = playerId;

        if (TryGetPlayer(playerId, out var next))
            next.SetHat(true);

        hatPickupTime = Time.time;
    }

    public bool CanGetHat() => Time.time > hatPickupTime + invincibleDuration;

    // -------------------- Fall Handling --------------------
    [PunRPC]
    void HandleFall(int playerId)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        double now = PhotonNetwork.Time;
        if (_lastFallAt.TryGetValue(playerId, out var t) && (now - t) < fallDebounceSeconds)
            return; // already handled very recently

        _lastFallAt[playerId] = now;

        // Choose a respawn point.
        int spawnIdx = UnityEngine.Random.Range(0, spawnPoints.Length);
        Vector3 pos = spawnPoints[spawnIdx].position;

        // Teleport once.
        if (TryGetPlayer(playerId, out var fallen))
            fallen.photonView.RPC("Teleport", RpcTarget.All, pos);

        // If they had the hat, give it to a random other player (ignore invincibility).
        if (playerWithHat == playerId)
        {
            var eligible = players.Where(p => p && p.id != playerId).ToList();
            if (eligible.Count > 0)
            {
                int newId = eligible[UnityEngine.Random.Range(0, eligible.Count)].id;
                photonView.RPC("GiveHat", RpcTarget.All, newId, false);
            }
        }
    }

    // -------------------- Win / Scene --------------------
    [PunRPC]
    void WinGame(int playerId)
    {
        gameEnded = true;

        if (TryGetPlayer(playerId, out var player))
            GameUI.instance.SetWinText(player.photonPlayer.NickName);

        Invoke(nameof(GoBackToMenu), 3f);
    }

    void GoBackToMenu()
    {
        PhotonNetwork.LeaveRoom();
        NetworkManager.instance.ChangeScene("Menu");
    }
}
