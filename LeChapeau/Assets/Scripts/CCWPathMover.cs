using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;

public class CCWPathMover : MonoBehaviourPunCallbacks
{
    [Tooltip("Waypoints ordered COUNTER-CLOCKWISE around the trench.")]
    public List<Transform> points = new List<Transform>();

    [Header("Motion")]
    public float speed = 4f;                 // units/sec along the loop
    public float spacingOffset01 = 0f;       // 0..1 phase offset for even spacing

    [Header("Turning")]
    public float turnDuration = 0.15f;       // seconds to rotate 90° left at corners
    public bool masterAuthority = true;      // owner-only drive (Master by default)

    [Header("Physics")]
    public Rigidbody rb;                     // used for MovePosition/MoveRotation

    // time/path state
    double startTime;
    int prevSeg = -1;
    bool turning = false;
    float turnT = 0f;
    Quaternion rotFrom, rotTo;

    // ownership tracking
    bool lastIsMine;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        ConfigureRB();
        lastIsMine = photonView.IsMine;
    }

    // MUST be public to match base signature
    public override void OnEnable()
    {
        base.OnEnable();
        startTime = PhotonNetwork.IsConnected ? PhotonNetwork.Time : Time.timeAsDouble;
        ConfigureRB();
        lastIsMine = photonView.IsMine;
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        ConfigureRB();
    }

    void ConfigureRB()
    {
        if (!rb) return;

        bool iAmOwner = !PhotonNetwork.IsConnected || photonView.IsMine;

        if (masterAuthority)
        {
            // Owner simulates; remotes are kinematic and interpolate snapshots
            rb.isKinematic = !iAmOwner;
            rb.interpolation = iAmOwner ? RigidbodyInterpolation.Interpolate
                                        : RigidbodyInterpolation.None;
        }
        else
        {
            // Deterministic mode: everyone simulates identical kinematic motion
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    void FixedUpdate()
    {
        // Handle ownership flips (e.g., master handoff) without override hooks
        if (photonView.IsMine != lastIsMine)
        {
            lastIsMine = photonView.IsMine;
            ConfigureRB();
        }

        // Owner-only drive when using authority
        if (masterAuthority && PhotonNetwork.IsConnected && !photonView.IsMine)
            return;

        if (points == null || points.Count < 2) return;

        // Parametric progress 0..1 around the loop
        float pathLen = TotalPathLength(points);
        float t = (float)(((PhotonNetwork.IsConnected ? PhotonNetwork.Time : Time.timeAsDouble) - startTime)
                          * speed / Mathf.Max(0.0001f, pathLen));
        t = Mathf.Repeat(t + spacingOffset01, 1f);

        // Segment + corner detection
        int n = points.Count;
        float segF = t * n;
        int seg = Mathf.FloorToInt(segF) % n;

        if (seg != prevSeg)
        {
            rotFrom = rb ? rb.rotation : transform.rotation;
            Vector3 turnAxis = rb ? rb.transform.up : transform.up; // local up (capsule’s +Z after X=90)
            rotTo = Quaternion.AngleAxis(-90f, turnAxis) * rotFrom; // left turn
            turnT = 0f;
            turning = true;
            prevSeg = seg;
        }

        // Position along path
        Vector3 pos = SampleLinear(points, t);
        if (rb) rb.MovePosition(pos); else transform.position = pos;

        // Rotation: corner ease or face along segment
        Vector3 localUp = rb ? rb.transform.up : transform.up;

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
            Vector3 dir = Vector3.ProjectOnPlane(b - a, localUp);

            if (dir.sqrMagnitude > 1e-4f)
            {
                Quaternion r = Quaternion.LookRotation(dir.normalized, localUp);
                if (rb) rb.MoveRotation(r); else transform.rotation = r;
            }
        }
    }

    // --- helpers ---
    static float TotalPathLength(List<Transform> pts)
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
