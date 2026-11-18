// WireTool.cs — Pin Mode wiring (Task 7).
// Every registry pin gets a 10 mm dot + 24 mm collider. Wire by
// tap-tap or poke-drag with a live sagging preview, 3 cm magnetic
// snap, committed only if validation allows. Wires render as
// 4 mm beziers (LineRenderer over a working-material clone), can be
// tapped to select (Delete/Recolour pills), and support 20-step
// undo/redo. Test hooks mirror the pointer paths.
using System;
using System.Collections.Generic;
using UnityEngine;
using Bench.Circuit;
using Bench.Workspace;

namespace Bench.PinMode
{
    public class MVWire
    {
        public string id = "";
        public string a = "";
        public string b = "";
        public GameObject vis;
        public Color colorOverride;
        public bool hasOverride;
    }

    public class WireTool : MonoBehaviour
    {
        public static WireTool Instance { get; private set; }

        private readonly List<MVWire> _wires = new List<MVWire>();
        private readonly List<GameObject> _dots = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _dotByNode =
            new Dictionary<string, GameObject>();
        private LineRenderer _preview;
        private PinDef _pendingA;
        private PinDef _hoverGaze;
        private bool _dragging;
        private Vector3 _dragPoint;
        private MVWire _selected;
        private GameObject _selUI;
        private Material _lineMat;
        private Transform _layer;
        private readonly Stack<IUndoOp> _undo = new Stack<IUndoOp>();
        private readonly Stack<IUndoOp> _redo = new Stack<IUndoOp>();

        public IReadOnlyList<MVWire> Wires => _wires;
        public PinDef PendingA => _pendingA;
        public MVWire Selected => _selected;

        private interface IUndoOp { void Do(); void Undo(); }
        private class AddOp : IUndoOp
        {
            public WireTool tool; public string a; public string b; public string id;
            public void Do() => tool.CommitWire(a, b, id, false);
            public void Undo() => tool.RemoveWire(id, false);
        }
        private class RemoveOp : IUndoOp
        {
            public WireTool tool; public string a; public string b; public string id;
            public void Do() => tool.RemoveWire(id, false);
            public void Undo() => tool.CommitWire(a, b, id, false);
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

        public void BuildDots()
        {
            ClearDots();
            _layer = new GameObject("PinLayer").transform;
            _layer.SetParent(transform, false);
            foreach (var pin in PinRegistry.Pins)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "PinDot";
                dot.transform.SetParent(pin.part.transform, false);
                dot.transform.localPosition = pin.localPos;
                // Constant world size on any part scale: 10 mm dot,
                // 24 mm invisible collider (spec).
                var ls = pin.part.transform.lossyScale;
                float k = 0.01f / Mathf.Max(1e-4f, Mathf.Max(ls.x,
                    Mathf.Max(ls.y, ls.z)));
                dot.transform.localScale = Vector3.one * k;
                var col = dot.GetComponent<Collider>();
                Destroy(col);
                var trig = dot.AddComponent<SphereCollider>();
                trig.radius = 1.2f;
                trig.isTrigger = true;
                var mr = dot.GetComponent<Renderer>();
                var mat = mr.material;
                mat.color = PinModeController.RoleColor(pin.role);
                dot.AddComponent<PinDotClick>().node = pin.node;
                dot.AddComponent<Bench.Hands.PinDotHand>().Node = pin.node;
                _dots.Add(dot);
                _dotByNode[pin.node] = dot;
            }
            // Line material: clone a working in-scene material so the
            // shader is guaranteed included (fresh Shader.Find Unlit
            // renders magenta in builds).
            _lineMat = null;
            foreach (var r in FindObjectsOfType<Renderer>(true))
            {
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null
                    && r.sharedMaterial.shader.name.Contains("PbrMetallicRoughness"))
                {
                    _lineMat = new Material(r.sharedMaterial);
                    break;
                }
            }
            if (_lineMat == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _lineMat = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
                Destroy(tmp);
            }
            _lineMat.color = new Color(0.36f, 0.88f, 0.9f, 1f);
        }

        public void ClearDots()
        {
            foreach (var d in _dots)
                if (d != null) Destroy(d);
            _dots.Clear();
            _dotByNode.Clear();
            if (_layer != null) Destroy(_layer.gameObject);
            _layer = null;
            ClearPreview();
            ClearSelection();
        }

        public void SetDotsVisible(Func<PinDef, bool> show)
        {
            foreach (var pin in PinRegistry.Pins)
            {
                if (_dotByNode.TryGetValue(pin.node, out var dot) && dot != null)
                    dot.SetActive(show == null || show(pin));
            }
        }

        // ── input (pointer + test hooks share these) ─────────────

        public void TapPin(string node)
        {
            var pin = PinRegistry.Pins.Find(p => p.node == node);
            if (pin == null)
            {
                Deselect();
                return;
            }
            PinModeController.Instance?.Poke();
            if (_pendingA == null)
            {
                _pendingA = pin;
                PulseDot(node, 1.5f);
                PinModeController.Instance?.Status(
                    "Wire from " + pin.label + " — tap a target pin");
                HighlightCompatible(pin);
                Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Press);
            }
            else if (_pendingA.node == node)
            {
                CancelPending();
            }
            else
            {
                var a = _pendingA;
                CancelPending(quiet: true);
                TryCommit(a.node, node);
            }
        }

        public void TestTapPin(string node) => TapPin(node);

        public void TestDragWire(string fromNode, string toNodeOrNull)
        {
            TapPin(fromNode);
            if (_pendingA == null) return;
            if (string.IsNullOrEmpty(toNodeOrNull))
            {
                CancelPending(); // released in empty space
                return;
            }
            TapPin(toNodeOrNull);
        }

        public void BeginDrag(string node, Vector3 pointerWorld)
        {
            var pin = PinRegistry.Pins.Find(p => p.node == node);
            if (pin == null) return;
            _dragging = true;
            _pendingA = pin;
            _dragPoint = pointerWorld;
            ShowPreview(pin.WorldPos, pointerWorld);
            HighlightCompatible(pin);
        }

        public void MoveDrag(Vector3 pointerWorld)
        {
            if (!_dragging || _pendingA == null) return;
            _dragPoint = pointerWorld;
            var snap = NearestValid(pointerWorld, 0.03f, _pendingA.node);
            ShowPreview(_pendingA.WorldPos,
                snap != null ? snap.WorldPos : pointerWorld);
            if (snap != null) PulseDot(snap.node, 1.5f);
        }

        public void EndDrag(Vector3 pointerWorld, bool overUI)
        {
            if (!_dragging) return;
            _dragging = false;
            var a = _pendingA;
            ClearPreview();
            _pendingA = null;
            ClearHighlight();
            if (a == null) return;
            var snap = overUI ? null : NearestValid(pointerWorld, 0.03f, a.node);
            if (snap == null)
            {
                PinModeController.Instance?.Status("Wire cancelled");
                return; // released in empty space: cancel
            }
            if (snap.node == a.node) return;
            TryCommit(a.node, snap.node);
        }

        public void CancelPending(bool quiet = false)
        {
            _pendingA = null;
            ClearPreview();
            ClearHighlight();
            if (!quiet) PinModeController.Instance?.Status("Wire cancelled");
        }

        // ── commit / remove / rewire ─────────────────────────────

        public bool TryCommit(string a, string b, string fixedId = null)
        {
            var model = PinModeController.Instance?.Model;
            var res = PinValidation.Check(a, b, model, FindNearest3V3);
            var ctl = PinModeController.Instance;
            if (res.verdict == Verdict.Block)
            {
                ctl?.Toast(res.title, res.message, true);
                Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Error);
                return false;
            }
            string id = fixedId ?? Guid.NewGuid().ToString("N");
            CommitWire(a, b, id, true);
            if (res.verdict == Verdict.Warn)
                ctl?.Toast(res.title, res.message, true, res.fixLabel, res.fix);
            else
                ctl?.Status("Wired " + ShortName(a) + " ↔ " + ShortName(b));
            return true;
        }

        private void CommitWire(string a, string b, string id, bool record)
        {
            var w = new MVWire { id = id, a = a, b = b };
            w.vis = BuildWireVisual(a, b, NetColor(a, b));
            _wires.Add(w);
            if (record)
                PushUndo(new AddOp { tool = this, a = a, b = b, id = id });
            SyncModel();
            var twin = TwinLink();
            if (twin != null) { try { twin.AddWire(a, b); } catch { } }
            var pa = PinRegistry.Pins.Find(p => p.node == a);
            var pb = PinRegistry.Pins.Find(p => p.node == b);
            Vector3 mid = pa != null && pb != null
                ? (pa.WorldPos + pb.WorldPos) * 0.5f
                : transform.position;
            Bench.UI.SoundBank.PlayAt(Bench.UI.BenchSound.Snap, mid);
        }

        public void RemoveWire(string id, bool record = true)
        {
            var w = _wires.Find(x => x.id == id);
            if (w == null) return;
            if (_selected == w) ClearSelection();
            _wires.Remove(w);
            if (w.vis != null) Destroy(w.vis);
            if (record)
                PushUndo(new RemoveOp { tool = this, a = w.a, b = w.b, id = id });
            SyncModel();
            var twin = TwinLink();
            if (twin != null) { try { twin.RemoveWire(id); } catch { } }
        }

        public static void Rewire(string oldEnd, string keepEnd, string newEnd)
        {
            var t = Instance;
            if (t == null) return;
            var w = t._wires.Find(x =>
                (x.a == oldEnd && x.b == keepEnd)
                || (x.b == oldEnd && x.a == keepEnd));
            if (w == null) return;
            // Re-validate the fix (a bad fix must not create a short).
            string other = w.a == oldEnd ? w.b : w.a;
            var res = PinValidation.Check(newEnd, other,
                PinModeController.Instance?.Model, t.FindNearest3V3);
            if (res.verdict == Verdict.Block)
            {
                PinModeController.Instance?.Toast(res.title,
                    "Fix aborted: " + res.message, true);
                return;
            }
            t.RemoveWire(w.id, true);
            t.TryCommit(newEnd, other);
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var op = _undo.Pop();
            op.Undo();
            _redo.Push(op);
            PinModeController.Instance?.Status("Undone");
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var op = _redo.Pop();
            op.Do();
            _undo.Push(op);
            PinModeController.Instance?.Status("Redone");
        }

        private void PushUndo(IUndoOp op)
        {
            _undo.Push(op);
            _redo.Clear();
            while (_undo.Count > 20)
            {
                var tmp = new Stack<IUndoOp>(_undo);
                var list = new System.Collections.Generic.List<IUndoOp>(tmp);
                list.RemoveAt(list.Count - 1);
                _undo.Clear();
                for (int i = list.Count - 1; i >= 0; i--) _undo.Push(list[i]);
            }
        }

        // ── selection (tap a wire) ───────────────────────────────

        public void TapWireAt(Vector3 worldPoint)
        {
            var hit = NearestWire(worldPoint, 0.02f);
            if (hit == null) { Deselect(); return; }
            Select(hit);
        }

        private void Select(MVWire w)
        {
            ClearSelection();
            _selected = w;
            var lr = w.vis?.GetComponent<LineRenderer>();
            if (lr != null) lr.widthMultiplier = 1.6f;
            ShowSelectUI(w);
            Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Press);
        }

        private void Deselect() => ClearSelection();

        private void ClearSelection()
        {
            if (_selected != null)
            {
                var lr = _selected.vis?.GetComponent<LineRenderer>();
                if (lr != null) lr.widthMultiplier = 1f;
            }
            _selected = null;
            if (_selUI != null) Destroy(_selUI);
            _selUI = null;
        }

        private static readonly Color[] RecolorPalette =
        {
            new Color(0.36f, 0.88f, 0.9f), new Color(1f, 0.71f, 0.33f),
            new Color(0.4f, 0.85f, 0.4f), new Color(0.75f, 0.55f, 1f),
            new Color(1f, 0.45f, 0.45f), new Color(0.95f, 0.95f, 0.95f),
        };

        private void ShowSelectUI(MVWire w)
        {
            ClearSelection();
            _selected = w;
            var lr = w.vis?.GetComponent<LineRenderer>();
            if (lr != null) lr.widthMultiplier = 1.6f;
            _selUI = new GameObject("WireSelectUI");
            _selUI.transform.position = WireMid(w) + Vector3.up * 0.05f;
            _selUI.transform.localScale = Vector3.one * 0.001f;
            Pill(_selUI.transform, "Delete", -70f, Bench.UI.ConceptUI.Red,
                () => RemoveWire(w.id, true));
            Pill(_selUI.transform, "Recolour", 70f,
                new Color(1f, 0.71f, 0.33f), () =>
                {
                    int i = 0;
                    for (; i < RecolorPalette.Length; i++)
                        if (ColorsClose(CurrentWireColor(w), RecolorPalette[i]))
                            break;
                    w.hasOverride = true;
                    w.colorOverride = RecolorPalette[(i + 1) % RecolorPalette.Length];
                    TintWire(w);
                    Deselect();
                });
        }

        private static bool ColorsClose(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g)
            + Mathf.Abs(a.b - b.b) < 0.05f;

        private void Pill(Transform parent, string label, float x, Color tint,
            Action onTap)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Pill_" + label;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.transform.localScale = new Vector3(120f, 44f, 1f);
            var mr = go.GetComponent<Renderer>();
            var mat = mr.material;
            mat.color = new Color(0.06f, 0.07f, 0.09f, 1f);
            var tg = new GameObject("Txt");
            tg.transform.SetParent(go.transform, false);
            var tmp = tg.AddComponent<TMPro.TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = 22f;
            tmp.color = tint;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(120f, 44f);
            // Counter the stretched pill parent so glyphs stay square:
            // world height = 2.46 (probed) * 0.001 (selUI) * k.
            var ps = go.transform.localScale;
            const float k = 8f; // -> ~20 mm text height
            tmp.transform.localScale = new Vector3(k / ps.x, k / ps.y, 1f);
            tmp.rectTransform.localPosition = new Vector3(0f, 0f, -0.5f);
            var click = go.AddComponent<PinClick>();
            click.onTap = onTap;
        }

        // ── visuals ──────────────────────────────────────────────

        private GameObject BuildWireVisual(string a, string b, Color color)
        {
            var go = new GameObject("WireVis");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(_lineMat);
            lr.material.color = color;
            lr.widthMultiplier = 0.004f;
            lr.positionCount = 16;
            lr.useWorldSpace = true;
            SetBezier(lr, PinPos(a), PinPos(b));
            return go;
        }

        private void ShowPreview(Vector3 a, Vector3 b)
        {
            if (_preview == null)
            {
                var go = new GameObject("WirePreview");
                _preview = go.AddComponent<LineRenderer>();
                _preview.material = new Material(_lineMat);
                _preview.material.color = new Color(0.55f, 0.95f, 1f, 1f);
                _preview.widthMultiplier = 0.003f;
                _preview.positionCount = 16;
                _preview.useWorldSpace = true;
            }
            _preview.gameObject.SetActive(true);
            SetBezier(_preview, a, b);
        }

        private void ClearPreview()
        {
            if (_preview != null) _preview.gameObject.SetActive(false);
        }

        private static void SetBezier(LineRenderer lr, Vector3 a, Vector3 b)
        {
            float dist = Vector3.Distance(a, b);
            float sag = Mathf.Max(0.01f, dist * 0.15f);
            var mid = (a + b) / 2f - Vector3.up * sag;
            for (int i = 0; i < 16; i++)
            {
                float t = i / 15f;
                float u = 1f - t;
                lr.SetPosition(i, u * u * a + 2f * u * t * mid + t * t * b);
            }
        }

        private void TintWire(MVWire w)
        {
            var lr = w.vis?.GetComponent<LineRenderer>();
            if (lr == null) return;
            lr.material.color = CurrentWireColor(w);
        }

        private Color CurrentWireColor(MVWire w) =>
            w.hasOverride ? w.colorOverride : NetColor(w.a, w.b);

        private static Color NetColor(string a, string b)
        {
            foreach (var n in new[] { a, b })
            {
                if (n != null && n.StartsWith("power:"))
                {
                    if (PinValidation.IsSupply(n, out var is5, out var isG))
                    {
                        if (isG) return new Color(0.15f, 0.15f, 0.16f);
                        return new Color(0.9f, 0.15f, 0.15f);
                    }
                }
            }
            int h = (a + "|" + b).GetHashCode();
            return RecolorPalette[Mathf.Abs(h) % RecolorPalette.Length];
        }

        private static string ShortName(string node)
        {
            var pin = PinRegistry.Pins.Find(p => p.node == node);
            if (pin != null) return pin.label;
            return node ?? "?";
        }

        private static Vector3 PinPos(string node)
        {
            var pin = PinRegistry.Pins.Find(p => p.node == node);
            if (pin != null) return pin.WorldPos;
            return Vector3.zero;
        }

        private static Vector3 WireMid(MVWire w) =>
            (PinPos(w.a) + PinPos(w.b)) / 2f;

        private MVWire NearestWire(Vector3 p, float maxDist)
        {
            MVWire best = null;
            float bestD = maxDist;
            foreach (var w in _wires)
            {
                float d = DistToSegment(p, PinPos(w.a), PinPos(w.b));
                if (d < bestD) { bestD = d; best = w; }
            }
            return best;
        }

        private static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab)
                / Mathf.Max(1e-8f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        private PinDef NearestValid(Vector3 p, float maxDist, string fromNode)
        {
            PinDef best = null;
            float bestD = maxDist;
            foreach (var pin in PinRegistry.Pins)
            {
                if (pin.node == fromNode) continue;
                float d = Vector3.Distance(pin.WorldPos, p);
                if (d >= bestD) continue;
                var res = PinValidation.Check(fromNode, pin.node,
                    PinModeController.Instance?.Model, FindNearest3V3);
                if (res.verdict == Verdict.Block) continue;
                bestD = d;
                best = pin;
            }
            var hole = PinRegistry.TryHole(p);
            if (hole != null)
            {
                float d = Vector3.Distance(hole.WorldPos, p);
                if (d < bestD)
                {
                    var res = PinValidation.Check(fromNode, hole.node,
                        PinModeController.Instance?.Model, FindNearest3V3);
                    if (res.verdict != Verdict.Block) best = hole;
                }
            }
            return best;
        }

        private string FindNearest3V3(string fromNode)
        {
            var from = PinRegistry.Pins.Find(p => p.node == fromNode);
            Vector3 at = from != null ? from.WorldPos : Vector3.zero;
            PinDef best = null;
            float bestD = float.MaxValue;
            foreach (var pin in PinRegistry.Pins)
            {
                if (!pin.node.StartsWith("power:")) continue;
                if (!PinValidation.IsSupply(pin.node, out _, out var g)
                    || g) continue;
                // Prefer 3V3 over 5V.
                bool is3 = pin.label == "3V3";
                float d = Vector3.Distance(pin.WorldPos, at)
                    + (is3 ? 0f : 1000f);
                if (d < bestD) { bestD = d; best = pin; }
            }
            return best?.node;
        }

        private void HighlightCompatible(PinDef from)
        {
            var model = PinModeController.Instance?.Model;
            foreach (var pin in PinRegistry.Pins)
            {
                if (pin.node == from.node) continue;
                if (!_dotByNode.TryGetValue(pin.node, out var dot)
                    || dot == null) continue;
                var res = PinValidation.Check(from.node, pin.node, model,
                    FindNearest3V3);
                if (res.verdict != Verdict.Block)
                    dot.transform.localScale = Vector3.one * 0.013f;
            }
        }

        private void ClearHighlight()
        {
            foreach (var d in _dots)
                if (d != null) d.transform.localScale = Vector3.one * 0.01f;
        }

        public void HighlightDot(string node) => PulseDot(node, 1.4f);

        private void PulseDot(string node, float mult)
        {
            if (_dotByNode.TryGetValue(node, out var dot) && dot != null)
                dot.transform.localScale = Vector3.one * 0.01f * mult;
        }

        private void SyncModel()
        {
            PinModeController.Instance?.RebuildModel();
            var twin = TwinLink();
            if (twin == null) return;
            // Values sync on demand in Task 8; topology syncs per wire op.
        }

        private static ITwinLink TwinLink()
        {
            try
            {
                if (Bench.Core.BenchSettings.DemoMode) return null;
                var b = FindFirstObjectByType<TwinBridge>(
                    FindObjectsInactive.Include);
                if (b != null && b.Connected) return b;
            }
            catch { }
            return null;
        }

        /// <summary>Drop wires whose endpoints no longer resolve
        /// (parts deleted). Breadboard holes always resolve.</summary>
        public void PruneDead()
        {
            var nodes = new System.Collections.Generic.HashSet<string>();
            foreach (var p in PinRegistry.Pins) nodes.Add(p.node);
            for (int i = _wires.Count - 1; i >= 0; i--)
            {
                var w = _wires[i];
                bool aOk = w.a.StartsWith("term:") || nodes.Contains(w.a);
                bool bOk = w.b.StartsWith("term:") || nodes.Contains(w.b);
                if (!aOk || !bOk) RemoveWire(w.id, false);
            }
            _undo.Clear();
            _redo.Clear();
        }

        public void TestTapWire(Vector3 worldPoint) => TapWireAt(worldPoint);
        public void TestDeleteSelected()
        {
            if (_selected != null) RemoveWire(_selected.id, true);
        }
        public int UndoDepth => _undo.Count;
        public int RedoDepth => _redo.Count;
    }

    /// <summary>Clickable 3D pin dot (mouse in editor; device tap in Task 10).</summary>
    public class PinDotClick : MonoBehaviour
    {
        public string node = "";
        private bool _down;
        private Vector3 _downScreen;

        private void OnMouseDown()
        {
            var t = WireTool.Instance;
            if (t == null) return;
            _down = true;
            _downScreen = Input.mousePosition;
            t.BeginDrag(node, transform.position);
        }

        private void OnMouseDrag()
        {
            var t = WireTool.Instance;
            if (t == null || !_down) return;
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(cam.transform.forward, transform.position);
            if (plane.Raycast(ray, out var d))
                t.MoveDrag(ray.GetPoint(d));
        }

        private void OnMouseUp()
        {
            var t = WireTool.Instance;
            if (t == null || !_down) return;
            _down = false;
            float moved = Vector2.Distance(Input.mousePosition, _downScreen);
            if (moved < 8f)
            {
                // Tap (not a drag): cancel drag state, do tap-tap.
                t.EndDrag(transform.position, true);
                t.TapPin(node);
            }
            else
            {
                var cam = Camera.main;
                Vector3 p = transform.position;
                bool overUI = false;
                if (cam != null)
                {
                    var ray = cam.ScreenPointToRay(Input.mousePosition);
                    var plane = new Plane(cam.transform.forward, transform.position);
                    if (plane.Raycast(ray, out var d)) p = ray.GetPoint(d);
                }
                t.EndDrag(p, overUI);
            }
        }
    }

    /// <summary>Generic 3D click target with injectable callback.</summary>
    public class PinClick : MonoBehaviour
    {
        public Action onTap;
        private void OnMouseDown() { }
        private void OnMouseUpAsButton() => onTap?.Invoke();
    }
}
