using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class CCWPathMover : MonoBehaviourPun
{
    [Tooltip("Waypoints ordered COUNTER-CLOCKWISE around the trench.")]
    public List<Transform> points = new List<Transform>();

    [Header("Motion")]
    public float speed = 4f;                 // units/sec along the loop
    public float spacingOffset01 = 0f;       // 0..1 phase offset for even spacing

    [Header("Turning")]
    public float turnDuration = 0.15f;       // seconds to rotate 90° left at corners
    public bool masterAuthority = false;     // true = only Master moves/sends via PhotonTransformView

    [Header("Physics")]
    public Rigidbody rb;                     // optional; script will use MovePosition/MoveRotation

    // deterministic time base (shared across clients)
    double startTime;
    int prevSeg = -1;
    bool turning = false;
    float turnT = 0f;
    Quaternion rotFrom, rotTo;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        startTime = PhotonNetwork.IsConnected ? PhotonNetwork.Time : Time.timeAsDouble;
    }

    void FixedUpdate()
    {
        if (masterAuthority && PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
            return;

        if (points == null || points.Count < 2) return;

        // Parametric progress 0..1 around the closed loop (deterministic)
        float pathLen = TotalPathLength(points);
        float t = (float)(((PhotonNetwork.IsConnected ? PhotonNetwork.Time : Time.timeAsDouble) - startTime)
                          * speed / Mathf.Max(0.0001f, pathLen));
        t = Mathf.Repeat(t + spacingOffset01, 1f);

        // Segment info and corner detection
        int n = points.Count;
        float segF = t * n;
        int seg = Mathf.FloorToInt(segF) % n;

        if (seg != prevSeg) // crossed a corner → begin 90° LEFT turn
        {
            rotFrom = rb ? rb.rotation : transform.rotation;
            rotTo = Quaternion.AngleAxis(90f, Vector3.up) * rotFrom;  // left turn around Y
            turnT = 0f;
            turning = true;
            prevSeg = seg;
        }

        // Position along the linear path
        Vector3 pos = SampleLinear(points, t);
        if (rb) rb.MovePosition(pos); else transform.position = pos;

        // Rotation: ease 90° left at corners, otherwise face along current segment
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
            Vector3 dir = b - a; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Quaternion r = Quaternion.LookRotation(dir.normalized, Vector3.up);
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
