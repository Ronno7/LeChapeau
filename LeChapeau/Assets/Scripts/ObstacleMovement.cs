using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class ObstacleMovement : MonoBehaviourPunCallbacks
{
    [Header("Path (CCW)")]
    public List<Transform> points = new List<Transform>(); // corners/turn points in COUNTER-CLOCKWISE order

    [Header("Motion")]
    public float speed = 4f;               // units/sec along the loop
    public float spacingOffset01 = 0f;     // 0..1 phase offset for even spacing
    public float turnDuration = 0.12f;     // time to yaw 90° at corners

    [Header("Net Mode")]
    public bool deterministic = true;      // everyone computes same path from shared clock (recommended)
    public bool useMasterAuthority = false;// Master simulates and others just receive snapshots (use if pushable)

    [Header("Optional Physics")]
    public bool pushable = false;          // requires useMasterAuthority=true + PTVC on the PhotonView for smooth remotes
    public Rigidbody rb;                   // optional; used for MovePosition/MoveRotation

    [Header("Sync")]
    public string roomStartKey = "obsStart"; // shared start time key in Room.CustomProperties

    // internal state
    double startTime;
    int prevSeg = -1;
    bool turning = false;
    float turnT = 0f;
    Quaternion rotFrom, rotTo;

    public override void OnEnable() // keep it simple and correct for PUN
    {
        base.OnEnable();
        if (!rb) rb = GetComponent<Rigidbody>();
        ConfigurePhysics();
        InitStartTime();
    }

    public override void OnMasterClientSwitched(Player newMasterClient) => ConfigurePhysics();

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable props)
    {
        if (props != null && props.ContainsKey(roomStartKey))
            startTime = (double)PhotonNetwork.CurrentRoom.CustomProperties[roomStartKey];
    }

    void FixedUpdate()
    {
        if (points == null || points.Count < 2) return;

        // Drive only on owner when using authority (Master is the owner of scene objects).
        if (useMasterAuthority && PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
            return;

        float pathLen = TotalLoopLength(points);
        double now = PhotonNetwork.IsConnected ? PhotonNetwork.Time : Time.timeAsDouble;
        float t = (float)((now - startTime) * speed / Mathf.Max(0.0001f, pathLen));
        t = Mathf.Repeat(t + spacingOffset01, 1f);

        int n = points.Count;
        float segF = t * n;
        int seg = Mathf.FloorToInt(segF) % n;

        // Corner entry → begin a 90° LEFT yaw around LOCAL up
        if (seg != prevSeg)
        {
            rotFrom = rb ? rb.rotation : transform.rotation;
            Vector3 axis = (rb ? rb.transform.up : transform.up);    // local up (matches your capsule orientation)
            rotTo = Quaternion.AngleAxis(-90f, axis) * rotFrom;      // left turn = -90
            turnT = 0f;
            turning = true;
            prevSeg = seg;
        }

        // Position along current segment
        Vector3 pos = SampleLinear(points, t);
        if (rb) rb.MovePosition(pos); else transform.position = pos;

        // Rotation: ease the 90° at corners; otherwise face segment direction (projected on local-up plane)
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
            rb.isKinematic = !iAmMaster; // Master simulates, remotes kinematic
            rb.interpolation = iAmMaster ? RigidbodyInterpolation.Interpolate
                                         : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = pushable ? CollisionDetectionMode.ContinuousSpeculative
                                                 : CollisionDetectionMode.Discrete;
        }
        else // deterministic everyone simulates; keep kinematic so there’s no divergent pushes
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
        else
        {
            // Master seeds a shared start time so all clients align deterministically.
            startTime = PhotonNetwork.Time;
            if (PhotonNetwork.IsMasterClient)
            {
                var ht = new ExitGames.Client.Photon.Hashtable { { roomStartKey, startTime } };
                room.SetCustomProperties(ht);
            }
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
