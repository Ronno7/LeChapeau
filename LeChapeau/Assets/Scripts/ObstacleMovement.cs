using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable; // alias to avoid namespace clashes

public class ObstacleMovement : MonoBehaviourPunCallbacks
{
    [Header("Path (CCW)")]
    public List<Transform> points = new List<Transform>(); // turn points in COUNTER-CLOCKWISE order

    [Header("Motion")]
    public float speed = 4f;               // units/sec along the loop
    public float spacingOffset01 = 0f;     // 0..1 phase offset for even spacing
    public float turnDuration = 0.12f;     // time to yaw 90° at corners

    [Header("Net Mode")]
    public bool deterministic = true;      // everyone computes same path from PhotonNetwork.Time
    public bool useMasterAuthority = false;// Master simulates & others interpolate (use when pushable)

    [Header("Optional Physics")]
    public bool pushable = false;          // requires useMasterAuthority=true + PTVC observed by PhotonView
    public Rigidbody rb;                   // used for MovePosition/MoveRotation if present

    [Header("Sync")]
    public string roomStartKey = "obsStart"; // shared start time key (Room.CustomProperties)

    // internal state
    double startTime;
    int prevSeg = -1;
    bool turning = false;
    float turnT = 0f;
    Quaternion rotFrom, rotTo;

    public override void OnEnable()
    {
        base.OnEnable();

        // Deterministic and authority are mutually exclusive. Deterministic wins.
        if (deterministic) useMasterAuthority = false;

        if (!rb) rb = GetComponent<Rigidbody>();
        ConfigurePhysics();
        InitStartTime();
    }

    public override void OnMasterClientSwitched(Player newMasterClient) => ConfigurePhysics();

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable props)
    {
        if (props != null && props.ContainsKey(roomStartKey) && PhotonNetwork.InRoom)
            startTime = (double)PhotonNetwork.CurrentRoom.CustomProperties[roomStartKey];
    }

    void FixedUpdate()
    {
        if (points == null || points.Count < 2) return;

        // If using authority, only Master advances motion; remotes just interpolate via PTVC.
        if (useMasterAuthority && PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
            return;

        float pathLen = TotalLoopLength(points);
        double now = PhotonNetwork.IsConnected ? PhotonNetwork.Time : Time.timeAsDouble;
        float t = (float)((now - startTime) * speed / Mathf.Max(0.0001f, pathLen));
        t = Mathf.Repeat(t + spacingOffset01, 1f);

        int n = points.Count;
        float segF = t * n;
        int seg = Mathf.FloorToInt(segF) % n;

        // Corner entry → start a 90° LEFT yaw around LOCAL up (+Z for your capsule with X=90)
        if (seg != prevSeg)
        {
            rotFrom = rb ? rb.rotation : transform.rotation;
            Vector3 axis = rb ? rb.transform.up : transform.up;
            rotTo = Quaternion.AngleAxis(-90f, axis) * rotFrom;
            turnT = 0f;
            turning = true;
            prevSeg = seg;
        }

        // Position along current segment
        Vector3 pos = SampleLinear(points, t);
        if (rb) rb.MovePosition(pos); else transform.position = pos;

        // Rotation: ease the 90° at corners; otherwise face segment direction projected on local-up plane
        Vector3 up = rb ? rb.transform.up : transform.up;
        if (turning)
        {
            turnT += Time.fixedDeltaTime / Mathf.Max(0.0001f, turnDuration);
            Quaternion r = Quaternion.Slerp(rotFrom, rotTo, Mathf.Clamp01(turnT));
            if (rb) rb.MoveRotation(r); else transform.rotation = r;
            if (turnT >= 1f) turning = false;
        }
        else
        {
            Vector3 a = points[seg].position;
            Vector3 b = points[(seg + 1) % n].position;
            Vector3 dir = Vector3.ProjectOnPlane(b - a, up);
            if (dir.sqrMagnitude > 1e-4f)
            {
                Quaternion r = Quaternion.LookRotation(dir.normalized, up);
                if (rb) rb.MoveRotation(r); else transform.rotation = r;
            }
        }
    }

    // ----- Helpers -----

    void ConfigurePhysics()
    {
        if (!rb) return;

        if (useMasterAuthority)
        {
            bool iAmMaster = !PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient;
            rb.isKinematic = !iAmMaster;  // Master simulates; remotes are kinematic
            rb.interpolation = iAmMaster ? RigidbodyInterpolation.Interpolate
                                         : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = pushable ? CollisionDetectionMode.ContinuousSpeculative
                                                 : CollisionDetectionMode.Discrete;
        }
        else // deterministic: everyone computes; keep kinematic to avoid divergent pushes
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
    }

    void InitStartTime()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
        {
            startTime = Time.timeAsDouble;
            return;
        }

        var room = PhotonNetwork.CurrentRoom;
        if (room.CustomProperties != null && room.CustomProperties.ContainsKey(roomStartKey))
        {
            startTime = (double)room.CustomProperties[roomStartKey];
        }
        else if (PhotonNetwork.IsMasterClient)
        {
            startTime = PhotonNetwork.Time;
            // Use the safe setter from NetworkManager to avoid "not ready"/Leaving errors.
            var ht = new Hashtable { { roomStartKey, startTime } };
            NetworkManager.TrySetRoomProps(ht);
        }
        else
        {
            // Fallback so non-master moves immediately; real value will arrive via OnRoomPropertiesUpdate.
            startTime = PhotonNetwork.Time;
        }
    }

    static float TotalLoopLength(List<Transform> pts)
    {
        float d = 0f;
        for (int i = 0; i < pts.Count; i++)
            d += Vector3.Distance(pts[i].position, pts[(i + 1) % pts.Count].position);
        return d;
    }

    static Vector3 SampleLinear(List<Transform> pts, float t01)
    {
        int n = pts.Count;
        float segF = t01 * n;
        int i = Mathf.FloorToInt(segF) % n;
        int j = (i + 1) % n;
        float u = segF - Mathf.Floor(segF);
        return Vector3.Lerp(pts[i].position, pts[j].position, u);
    }
}
