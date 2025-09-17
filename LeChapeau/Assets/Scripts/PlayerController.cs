using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class PlayerController : MonoBehaviourPunCallbacks, IPunObservable
{
    [HideInInspector] public int id;

    [Header("Info")]
    public float moveSpeed = 6f;
    public float jumpForce = 7f;
    public GameObject hatObject;

    [HideInInspector] public float curHatTime;

    [Header("Components")]
    public Rigidbody rig;
    public Player photonPlayer;

    [Header("Fall Handling")]
    [SerializeField] float fallY = 0f;          // trigger line
    [SerializeField] float rearmMargin = 0.5f;  // must rise this far to rearm
    bool wasBelow = false;

    // -------------------- Lifecycle --------------------

    [PunRPC]
    public void Initialize(Player player)
    {
        photonPlayer = player;
        id = player.ActorNumber;

        GameManager.instance.RegisterPlayer(this);

        // First actor gets the hat at match start.
        if (id == 1)
            GameManager.instance.GiveHat(id, true);

        // Non-owners don't run physics locally.
        if (!photonView.IsMine && rig) rig.isKinematic = true;
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            // Edge-trigger fall detection (single RPC per drop).
            if (transform.position.y < fallY)
            {
                if (!wasBelow)
                {
                    wasBelow = true;
                    GameManager.instance.photonView.RPC("HandleFall", RpcTarget.MasterClient, id);
                }
            }
            else if (transform.position.y > fallY + rearmMargin)
            {
                wasBelow = false;
            }

            Move();

            if (Input.GetKeyDown(KeyCode.Space))
                TryJump();

            if (hatObject.activeInHierarchy)
                curHatTime += Time.deltaTime;
        }

        // Master checks win condition for this player.
        if (PhotonNetwork.IsMasterClient &&
            curHatTime >= GameManager.instance.timeToWin &&
            !GameManager.instance.gameEnded)
        {
            GameManager.instance.gameEnded = true;
            GameManager.instance.photonView.RPC("WinGame", RpcTarget.All, id);
        }
    }

    // -------------------- Movement --------------------

    void Move()
    {
        float x = Input.GetAxis("Horizontal") * moveSpeed;
        float z = Input.GetAxis("Vertical") * moveSpeed;

        // If you're on standard Rigidbody, use rig.velocity instead of linearVelocity.
        rig.linearVelocity = new Vector3(x, rig.linearVelocity.y, z);
    }

    void TryJump()
    {
        if (Physics.Raycast(new Ray(transform.position, Vector3.down), 0.7f))
            rig.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    // -------------------- Gameplay --------------------

    public void SetHat(bool hasHat) => hatObject.SetActive(hasHat);

    void OnCollisionEnter(Collision collision)
    {
        if (!photonView.IsMine) return;
        if (!collision.gameObject.CompareTag("Player")) return;

        if (GameManager.instance.TryGetPlayer(collision.gameObject, out var other) &&
            other.id == GameManager.instance.playerWithHat &&
            GameManager.instance.CanGetHat())
        {
            GameManager.instance.photonView.RPC("GiveHat", RpcTarget.All, id, false);
        }
    }

    // -------------------- Networking --------------------

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting) stream.SendNext(curHatTime);
        else curHatTime = (float)stream.ReceiveNext();
    }

    [PunRPC]
    public void Teleport(Vector3 newPosition)
    {
        // Small lift avoids immediate ground/edge retrigger.
        transform.position = newPosition + Vector3.up * 0.05f;

        if (rig)
        {
            rig.linearVelocity = Vector3.zero;
            rig.angularVelocity = Vector3.zero;
        }
    }
}
