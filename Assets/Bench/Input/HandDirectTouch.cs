// HandDirectTouch.cs — hand control for runtime 3D objects.
// ISDK interactables need editor-baked surfaces, but our dots,
// parts, pens and window bars are all spawned at runtime with
// dynamic geometry — so one thin router reads OVRHand state
// (pinch + pointer ray + index tip) and drives the SAME methods
// the mouse path and test hooks use (WireTool drag, part carry,
// pen carry, window move). Canvas UI keeps ISDK ray/poke.
// Far: ray + pinch (tap = quick pinch, drag = move while held).
// Near: fingertip poke (pin dots), grab = pinch near a part.
// Gaze aims only: nearest pin in the head cone glows.
using System.Collections.Generic;
using UnityEngine;

namespace Bench.Hands
{
    public interface IHandDirect
    {
        void OnHoverEnter(int hand);
        void OnHoverExit(int hand);
        void OnPinchStart(int hand, Vector3 point);
        void OnPinchMove(int hand, Vector3 point);
        void OnPinchEnd(int hand, Vector3 point);
        /// <summary>Drag-plane anchor: dots use their centre (a
        /// surface hit would tilt the plane off the snap radius).</summary>
        Vector3 DragAnchor(Vector3 hitPoint);
    }

    public class HandDirectTouch : MonoBehaviour
    {
        public static HandDirectTouch Instance { get; private set; }

        private class HandState
        {
            public OVRHand hand;
            public OVRSkeleton skeleton;
            public bool wasPinching;
            public IHandDirect held;
            public float grabDepth;
            public Vector3 anchor;
            public IHandDirect hovered;
            public bool test; // driven only by TestPinch, never Update
        }

        private readonly List<HandState> _hands = new List<HandState>();
        private float _scanT;
        private float _hoverSndT;
        private string _gazePin;
        private float _gazePulseT;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("HandDirectTouch");
            go.AddComponent<HandDirectTouch>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            _scanT += Time.unscaledDeltaTime;
            if (_scanT >= 1f) { _scanT = 0f; ScanHands(); }
            for (int i = 0; i < _hands.Count; i++)
                UpdateHand(i);
            UpdateGaze();
            UpdatePoke();
        }

        private void ScanHands()
        {
            var found = FindObjectsOfType<OVRHand>(true);
            foreach (var h in found)
            {
                if (h == null || !h.gameObject.activeInHierarchy) continue;
                bool known = false;
                foreach (var s in _hands)
                    if (s.hand == h) { known = true; break; }
                if (known) continue;
                var st = new HandState { hand = h };
                // Skeleton lives on the same hand object in the rig.
                st.skeleton = h.GetComponent<OVRSkeleton>();
                _hands.Add(st);
            }
            _hands.RemoveAll(s => !s.test && s.hand == null);
        }

        private static Ray HandRay(HandState st, out bool ok)
        {
            ok = st.hand != null && st.hand.IsTracked
                && st.hand.IsPointerPoseValid
                && st.hand.PointerPose != null;
            if (!ok) return default;
            var t = st.hand.PointerPose;
            return new Ray(t.position, t.forward);
        }

        /// <summary>Best target under a ray: nearest pin/dot by angle
        /// (dense headers overlap, so first-hit would shadow rows).</summary>
        private static IHandDirect Pick(Ray ray, float maxDist,
            out Vector3 point)
        {
            point = ray.origin + ray.direction * maxDist;
            var hits = Physics.RaycastAll(ray, maxDist,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            IHandDirect best = null;
            float bestScore = 0.06f; // ~3.4 deg cone
            Vector3 bestP = point;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var target = h.collider.GetComponentInParent<IHandDirect>();
                if (target == null) continue;
                // Dots score by CENTRE angle with a tighter cone and
                // a tie-break bonus, so aiming at a dot wires while
                // aiming at a body grabs it.
                var pd = target as PinDotHand;
                float cone = pd != null ? 0.026f : 0.06f;
                Vector3 anchor = pd != null ? pd.WorldPos : h.point;
                Vector3 to = anchor - ray.origin;
                float ang = Vector3.Angle(ray.direction, to.normalized)
                    * Mathf.Deg2Rad;
                float score = ang - (pd != null ? 0.005f : 0f);
                if (ang < cone && score < bestScore)
                {
                    bestScore = score;
                    best = target;
                    bestP = h.point;
                }
            }
            // Fallback: nearest hit that HAS a target (room meshes
            // without handlers must not shadow real targets).
            if (best == null)
            {
                float bestD = float.MaxValue;
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    var target = h.collider
                        .GetComponentInParent<IHandDirect>();
                    if (target == null || h.distance >= bestD) continue;
                    bestD = h.distance;
                    best = target;
                    bestP = h.point;
                }
            }
            point = bestP;
            return best;
        }

        private void UpdateHand(int i)
        {
            var st = _hands[i];
            if (st.test) return; // harness-driven, not live hands
            Ray ray = HandRay(st, out bool ok);
            if (!ok)
            {
                if (st.held != null)
                {
                    st.held.OnPinchEnd(i, st.anchor);
                    st.held = null;
                }
                return;
            }
            bool pinching = false;
            try { pinching = st.hand.GetFingerIsPinching(OVRHand.HandFinger.Index); }
            catch { }
            Vector3 hitP = ray.origin + ray.direction * 1.2f;
            var target = st.held ?? Pick(ray, 5f, out hitP);

            // Hover (only when not holding something else).
            if (st.held == null && target != st.hovered)
            {
                st.hovered?.OnHoverExit(i);
                st.hovered = target;
                if (target != null)
                {
                    target.OnHoverEnter(i);
                    if (Time.unscaledTime - _hoverSndT > 0.15f)
                    {
                        _hoverSndT = Time.unscaledTime;
                        Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Hover);
                    }
                }
            }
            if (!pinching && st.wasPinching && st.held != null)
            {
                st.held.OnPinchEnd(i, MovePoint(st, ray));
                st.held = null;
            }
            else if (pinching && !st.wasPinching && target != null)
            {
                st.held = target;
                st.anchor = target.DragAnchor(hitP);
                st.grabDepth = Vector3.Distance(ray.origin, hitP);
                target.OnPinchStart(i, hitP);
            }
            else if (pinching && st.held != null)
            {
                st.held.OnPinchMove(i, MovePoint(st, ray));
            }
            st.wasPinching = pinching;
        }

        private static Vector3 MovePoint(HandState st, Ray ray)
        {
            // Drag on a head-facing plane through the grab anchor
            // (stable for wires); parts re-derive depth themselves.
            var cam = Camera.main;
            Vector3 n = cam != null ? cam.transform.forward : -ray.direction;
            var plane = new Plane(n, st.anchor);
            float d;
            if (plane.Raycast(ray, out d)) return ray.GetPoint(d);
            return ray.origin + ray.direction * st.grabDepth;
        }

        // ── near poke + gaze ─────────────────────────────────────

        private static Vector3 IndexTip(HandState st, out bool ok)
        {
            ok = false;
            if (st.skeleton == null || st.skeleton.Bones == null)
                return Vector3.zero;
            foreach (var b in st.skeleton.Bones)
            {
                if (b == null || b.Transform == null) continue;
                if (b.Id == OVRSkeleton.BoneId.Hand_IndexTip
                    || b.Id == OVRSkeleton.BoneId.XRHand_IndexTip)
                {
                    ok = true;
                    return b.Transform.position;
                }
            }
            return Vector3.zero;
        }

        private readonly Dictionary<string, float> _pokeCool =
            new Dictionary<string, float>();

        private void UpdatePoke()
        {
            foreach (var st in _hands)
            {
                Vector3 tip = IndexTip(st, out bool ok);
                if (!ok || !st.hand.IsTracked) continue;
                foreach (var dot in PinDotHand.All)
                {
                    if (dot == null) continue;
                    float d = Vector3.Distance(tip, dot.WorldPos);
                    if (d > 0.025f) continue;
                    float t = Time.unscaledTime;
                    if (_pokeCool.TryGetValue(dot.Node, out float last)
                        && t - last < 0.6f) continue;
                    _pokeCool[dot.Node] = t;
                    PinMode.WireTool.Instance?.TapPin(dot.Node);
                    PinMode.PinModeController.Instance?.RebuildModel();
                }
            }
        }

        private void UpdateGaze()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            string best = null;
            float bestAng = 2.5f;
            foreach (var dot in PinDotHand.All)
            {
                if (dot == null) continue;
                Vector3 to = dot.WorldPos - ray.origin;
                if (to.sqrMagnitude > 9f) continue;
                float ang = Vector3.Angle(ray.direction, to.normalized);
                if (ang < bestAng) { bestAng = ang; best = dot.Node; }
            }
            _gazePin = best;
            if (best != null && Time.unscaledTime - _gazePulseT > 0.5f)
            {
                _gazePulseT = Time.unscaledTime;
                var wire = PinMode.WireTool.Instance;
                if (wire != null) wire.HighlightDot(best);
            }
        }

        // ── test hooks (same routing, synthetic rays) ──────────

        private readonly List<HandState> _testHands =
            new List<HandState>();

        public bool TestPinch(int hand, Vector3 origin, Vector3 dir,
            string phase, out string hitNode)
        {
            hitNode = null;
            while (_testHands.Count <= hand)
                _testHands.Add(new HandState { test = true });
            var st = _testHands[hand];
            var ray = new Ray(origin, dir.normalized);
            Vector3 hitP = ray.origin + ray.direction;
            var target = st.held ?? Pick(ray, 6f, out hitP);
            if (phase == "down")
            {
                if (target == null) return false;
                st.held = target;
                st.anchor = target.DragAnchor(hitP);
                st.grabDepth = Vector3.Distance(origin, hitP);
                var dot = target as PinDotHand;
                hitNode = dot != null ? dot.Node : target.ToString();
                target.OnPinchStart(hand, hitP);
                return true;
            }
            if (st.held == null) return false;
            var cam = Camera.main;
            Vector3 n = cam != null ? cam.transform.forward : -dir;
            var plane = new Plane(n, st.anchor);
            float d;
            Vector3 p = plane.Raycast(ray, out d)
                ? ray.GetPoint(d) : ray.origin + ray.direction * st.grabDepth;
            if (phase == "move") st.held.OnPinchMove(hand, p);
            else { st.held.OnPinchEnd(hand, p); st.held = null; }
            return true;
        }

        public string TestGaze(Vector3 origin, Vector3 dir)
        {
            Ray ray = new Ray(origin, dir.normalized);
            string best = null;
            float bestAng = 2.5f;
            foreach (var dot in PinDotHand.All)
            {
                if (dot == null) continue;
                Vector3 to = dot.WorldPos - origin;
                if (to.sqrMagnitude > 9f) continue;
                float ang = Vector3.Angle(dir, to.normalized);
                if (ang < bestAng) { bestAng = ang; best = dot.Node; }
            }
            return best;
        }
    }

    /// <summary>Hand router endpoint on a pin dot (mouse path kept).</summary>
    public class PinDotHand : MonoBehaviour, IHandDirect
    {
        public string Node = "";
        public static readonly List<PinDotHand> All = new List<PinDotHand>();
        public Vector3 WorldPos => transform.position;

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable() => All.Remove(this);

        public void OnHoverEnter(int hand) =>
            PinMode.WireTool.Instance?.HighlightDot(Node);
        public void OnHoverExit(int hand) { }
        public void OnPinchStart(int hand, Vector3 point) =>
            PinMode.WireTool.Instance?.BeginDrag(Node, point);
        public void OnPinchMove(int hand, Vector3 point) =>
            PinMode.WireTool.Instance?.MoveDrag(point);
        public void OnPinchEnd(int hand, Vector3 point) =>
            PinMode.WireTool.Instance?.EndDrag(point, false);
        public Vector3 DragAnchor(Vector3 hitPoint) => WorldPos;
    }

    /// <summary>Hand grab/carry for placed parts. Boards: tap opens
    /// the menu, drag moves. Components: pinch carries at once.</summary>
    public class PartHandGrab : MonoBehaviour, IHandDirect
    {
        private Workspace.PlacedPart _part;
        private Vector3 _down;
        private bool _carrying;
        private bool _menuDone;

        private void Awake() => _part =
            GetComponent<Workspace.PlacedPart>();

        public void OnHoverEnter(int hand) { }
        public void OnHoverExit(int hand) { }

        public void OnPinchStart(int hand, Vector3 point)
        {
            if (_part == null) return;
            _down = point;
            _carrying = false;
            _menuDone = false;
            if (!_part.isBoard)
            {
                _part.TestBeginCarry(point);
                _carrying = true;
            }
        }

        public void OnPinchMove(int hand, Vector3 point)
        {
            if (_part == null) return;
            if (!_carrying && Vector3.Distance(point, _down) > 0.03f)
            {
                _part.TestBeginCarry(point);
                _carrying = true;
            }
            if (_carrying) _part.TestMoveCarry(point);
        }

        public Vector3 DragAnchor(Vector3 hitPoint) => hitPoint;

        public void OnPinchEnd(int hand, Vector3 point)
        {
            if (_part == null) return;
            if (_carrying)
            {
                _part.TestMoveCarry(point);
                _part.TestEndCarry(true);
                _carrying = false;
                return;
            }
            // Tap (no drag): boards open their menu.
            if (_part.isBoard && !_menuDone)
            {
                _menuDone = true;
                Panels.BoardMenu.Show(_part);
            }
        }
    }

    /// <summary>Hand carry for probe pens (pinch to lift, move,
    /// release over a pin to attach via the normal drop path).</summary>
    public class PenHandGrab : MonoBehaviour, IHandDirect
    {
        private Tools.ProbePen _pen;

        private void Awake() => _pen = GetComponent<Tools.ProbePen>();

        public void OnHoverEnter(int hand) { }
        public void OnHoverExit(int hand) { }
        public Vector3 DragAnchor(Vector3 hitPoint) => hitPoint;
        public void OnPinchStart(int hand, Vector3 point) =>
            _pen?.TestBeginCarry(point);
        public void OnPinchMove(int hand, Vector3 point) =>
            _pen?.TestMoveCarry(point);
        public void OnPinchEnd(int hand, Vector3 point)
        {
            if (_pen == null) return;
            _pen.TestMoveCarry(point);
            _pen.TestEndCarry(true);
        }
    }

    /// <summary>Window grab bars: pinch-drag repositions the window
    /// (clamped to a comfortable shell), release keeps it there.</summary>
    public class WindowBarHand : MonoBehaviour, IHandDirect
    {
        private Transform _win;
        private Vector3 _grabOffset;

        private void Awake()
        {
            var glass = GetComponentInParent<UI.GlassWindow>();
            _win = glass != null ? glass.transform : transform;
        }

        public void OnHoverEnter(int hand) { }
        public void OnHoverExit(int hand) { }
        public Vector3 DragAnchor(Vector3 hitPoint) => hitPoint;

        public void OnPinchStart(int hand, Vector3 point)
        {
            if (_win != null) _grabOffset = _win.position - point;
        }

        public void OnPinchMove(int hand, Vector3 point)
        {
            if (_win == null) return;
            var cam = Camera.main;
            Vector3 hp = cam != null ? cam.transform.position
                : new Vector3(0f, 1.6f, 0f);
            Vector3 want = point + _grabOffset;
            Vector3 flat = new Vector3(want.x - hp.x, 0f, want.z - hp.z);
            float dist = Mathf.Clamp(flat.magnitude, 0.5f, 2.0f);
            if (flat.sqrMagnitude > 1e-6f)
                flat = flat.normalized * dist;
            want = new Vector3(hp.x + flat.x,
                Mathf.Clamp(want.y, 0.8f, 2.2f), hp.z + flat.z);
            _win.position = want;
            // Canvas reads from -z: +z points away from the head.
            if ((want - hp).sqrMagnitude > 1e-4f)
                _win.rotation =
                    Quaternion.LookRotation(want - hp, Vector3.up);
        }

        public void OnPinchEnd(int hand, Vector3 point) =>
            OnPinchMove(hand, point);
    }
}
