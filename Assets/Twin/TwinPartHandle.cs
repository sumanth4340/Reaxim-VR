// TwinPartHandle.cs — push a part proxy's pose back to the twin.
// Attach to each part proxy. On every incoming snapshot (~15 Hz), if the
// proxy's transform changed since the last push, its new pose is sent via
// movePart. Deliberately input-agnostic: it works with XRI controller
// grabs, Meta Interaction SDK gaze grabs (look-and-pinch on VR Glasses),
// or anything else that moves the proxy. No XR dependencies.
//
// Bench UI: routes through TwinMirror when present so the ACTIVE link
// (real bridge or the offline mock) gets the move, with desk-local
// conversion handled by the mirror.

using Newtonsoft.Json.Linq;
using UnityEngine;

public class TwinPartHandle : MonoBehaviour
{
    [Tooltip("Twin part id from the snapshot (parts[].id).")]
    public string partId;

    [Tooltip("Minimum move (metres) / turn (degrees) before a push is sent.")]
    public float moveEpsilon = 0.001f;
    public float rotEpsilonDeg = 0.5f;

    private TwinBridge _bridge;
    private TwinMirror _mirror;
    private Vector3 _lastPos;
    private float _lastRotY;
    private bool _hasLast;

    private void Awake()
    {
        _bridge = FindObjectOfType<TwinBridge>();
        _mirror = FindObjectOfType<TwinMirror>();
    }

    private void OnEnable()
    {
        if (_mirror != null) _mirror.OnSnapshotApplied += PushIfMoved;
        else if (_bridge != null) _bridge.OnSnapshot += PushIfMovedBridge;
    }

    private void OnDisable()
    {
        if (_mirror != null) _mirror.OnSnapshotApplied -= PushIfMoved;
        else if (_bridge != null) _bridge.OnSnapshot -= PushIfMovedBridge;
    }

    private void PushIfMovedBridge(JObject _) => PushIfMoved();

    private void PushIfMoved()
    {
        if (string.IsNullOrEmpty(partId)) return;
        float rotY = transform.eulerAngles.y;
        if (_hasLast &&
            Vector3.Distance(transform.position, _lastPos) < moveEpsilon &&
            Mathf.Abs(Mathf.DeltaAngle(rotY, _lastRotY)) < rotEpsilonDeg)
            return;
        _hasLast = true;
        _lastPos = transform.position;
        _lastRotY = rotY;
        if (_mirror != null)
            _mirror.MovePart(partId, transform.position, rotY * Mathf.Deg2Rad);
        else if (_bridge != null)
            _bridge.MovePart(partId, transform.position, rotY * Mathf.Deg2Rad);
    }
}
