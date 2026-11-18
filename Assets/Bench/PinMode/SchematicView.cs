// SchematicView.cs — Pin Mode as a flat 2D schematic (user request):
// entering Pin Mode brings up a big MR panel showing every board and
// component in the workspace as a labelled box with visible pin dots.
// Pinch/click a pin, drag to another pin, release to wire (same
// WireTool.TryCommit path as the 3D dots, so validation, undo and the
// circuit solver all apply). Exit returns to the normal 3D objects.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Bench.Circuit;
using Bench.Workspace;

namespace Bench.PinMode
{
    public class SchematicView : MonoBehaviour
    {
        public static SchematicView Instance { get; private set; }

        private GameObject _stage;      // Screen_Pin (baked ray+poke)
        private GameObject _root;       // SchematicRoot
        private readonly List<GameObject> _stageKids = new List<GameObject>();
        private readonly Dictionary<string, RectTransform> _pinDots =
            new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, PinDef> _pinByNode =
            new Dictionary<string, PinDef>();
        private Transform _wireLayer;
        private Transform _dotLayer;
        private Image _dragLine;
        private string _dragFrom;
        private int _lastWireCount = -1;
        private float _pollT;

        public bool IsShowing => _root != null && _root.activeSelf;
        public int PinDotCount => _pinDots.Count;
        public int WireLineCount => _wireLayer != null
            ? _wireLayer.childCount : 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── show / hide ──────────────────────────────────────────

        public void Show()
        {
            EnsureStage();
            if (_stage == null) return;
            _stage.SetActive(true);
            foreach (var k in _stageKids)
                if (k != null && k != _root) k.SetActive(false);
            PoseInFront();
            Build();
            _root.SetActive(true);
            _lastWireCount = -1;
            RefreshWires(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
            if (_stage != null) _stage.SetActive(false);
        }

        private void EnsureStage()
        {
            if (_stage != null) return;
            foreach (var t in FindObjectsOfType<Transform>(true))
            {
                if (t.name == "Screen_Pin" && t.parent != null
                    && t.parent.name == "ConceptUI")
                {
                    _stage = t.gameObject;
                    break;
                }
            }
            if (_stage == null) return;
            _stageKids.Clear();
            foreach (Transform c in _stage.transform)
                _stageKids.Add(c.gameObject);
            _root = new GameObject("SchematicRoot");
            _root.transform.SetParent(_stage.transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _root.SetActive(false);
        }

        private void PoseInFront()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 hp = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = hp + fwd * 1.25f;
            pos.y = hp.y - 0.05f;
            _stage.transform.position = pos;
            // Canvas reads from the -z side (same convention as panels).
            _stage.transform.rotation =
                Quaternion.LookRotation(pos - hp, Vector3.up);
        }

        // ── build ────────────────────────────────────────────────

        private void Build()
        {
            foreach (Transform c in _root.transform)
                Destroy(c.gameObject);
            _pinDots.Clear();
            _pinByNode.Clear();

            // Backdrop.
            Rect(_root.transform, 0, 0, 1280, 720,
                new Color(0.05f, 0.07f, 0.10f, 0.96f), raycast: true);
            Label(_root.transform, "PIN MODE · Schematic", 30,
                Color.white, 36, 18, 700, 44, true);
            Label(_root.transform,
                "Pinch a pin and drag to another pin to wire it", 19,
                new Color(1f, 1f, 1f, 0.65f), 36, 62, 760, 30, false);
            var exit = Rect(_root.transform, 1130, 20, 114, 46,
                new Color(0.36f, 0.88f, 0.9f, 0.25f));
            exit.name = "SchematicExit";
            var eb = exit.gameObject.AddComponent<Button>();
            eb.onClick.AddListener(() => PinModeController.Exit());
            Label(exit.transform, "Exit ✕", 20, Color.white,
                0, 0, 114, 46, true, TextAlignmentOptions.Center);

            _wireLayer = Layer("WireLayer");
            _dotLayer = Layer("DotLayer");

            // Group pins by part.
            var byPart = new Dictionary<string, List<PinDef>>();
            var parts = new Dictionary<string, PlacedPart>();
            foreach (var pin in PinRegistry.Pins)
            {
                var part = pin.part != null ? pin.part : pin.board;
                if (part == null) continue;
                if (!byPart.TryGetValue(part.partId, out var list))
                {
                    list = new List<PinDef>();
                    byPart[part.partId] = list;
                    parts[part.partId] = part;
                }
                list.Add(pin);
                _pinByNode[pin.node] = pin;
            }

            float compX = 700f, compY = 120f;
            foreach (var p in WorkspacePhysics.PartsSnapshot())
            {
                if (p == null) continue;
                if (!byPart.TryGetValue(p.partId, out var pins)) continue;
                if (p.isBoard)
                    BoardBox(p, pins, 36f, 120f, 620f, 470f);
                else
                {
                    if (compY > 560f) { compX += 280f; compY = 120f; }
                    CompBox(p, pins, compX, compY, 250f, 150f);
                    compY += 175f;
                }
            }
            if (byPart.Count == 0)
                Label(_root.transform,
                    "Place a board and parts first — they appear here.",
                    22, new Color(1f, 1f, 1f, 0.7f), 36, 300, 900, 40,
                    false);
        }

        private Transform Layer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go.transform;
        }

        private void BoardBox(PlacedPart part, List<PinDef> pins,
            float x, float y, float w, float h)
        {
            var box = Rect(_root.transform, x, y, w, h,
                new Color(0.10f, 0.35f, 0.18f, 0.95f));
            box.name = "Schem_" + part.type;
            Label(box.transform, part.displayName, 24, Color.white,
                18, 10, w - 36, 34, true);
            Label(box.transform, "40-pin header · 3.3 V logic", 16,
                new Color(1f, 1f, 1f, 0.6f), 18, 44, w - 36, 26, false);
            // Two header rows of 20 (physical order).
            var sorted = new List<PinDef>(pins);
            sorted.Sort((a, b) => Physical(a).CompareTo(Physical(b)));
            for (int i = 0; i < sorted.Count; i++)
            {
                int col = i % 20;
                int row = i / 20;
                float px = 26f + col * 29f;
                float py = h - 130f + row * 56f;
                PinDot(box.transform, sorted[i], px, py, 26f);
                Label(box.transform, CompactName(sorted[i]), 11,
                    new Color(1f, 1f, 1f, 0.85f), px - 14f, py + 27f,
                    56, 18, true, TextAlignmentOptions.Center);
            }
        }

        private void CompBox(PlacedPart part, List<PinDef> pins,
            float x, float y, float w, float h)
        {
            var box = Rect(_root.transform, x, y, w, h,
                new Color(0.16f, 0.18f, 0.24f, 0.95f));
            box.name = "Schem_" + part.type;
            Label(box.transform, part.displayName, 20, Color.white,
                14, 8, w - 28, 30, true);
            int n = Mathf.Max(1, pins.Count);
            for (int i = 0; i < pins.Count; i++)
            {
                float px = 18f + (i + 0.5f) / n * (w - 36f) - 13f;
                PinDot(box.transform, pins[i], px, h - 62f, 26f);
                Label(box.transform, pins[i].sub ?? "", 12,
                    new Color(1f, 1f, 1f, 0.8f), px - 14f, h - 34f,
                    54, 18, false, TextAlignmentOptions.Center);
            }
        }

        private void PinDot(Transform parent, PinDef pin, float x,
            float y, float size)
        {
            var img = Rect(parent, x, y, size, size,
                PinModeController.RoleColor(pin.role));
            img.name = "Pin_" + pin.node;
            img.sprite = Circle();
            _pinDots[pin.node] = (RectTransform)img.transform;
            var drag = img.gameObject.AddComponent<SchematicPinDrag>();
            drag.Node = pin.node;
            drag.View = this;
        }

        /// <summary>Pin centre in wire-layer coords (anchor 0,0,
        /// bottom-left origin, y up).</summary>
        private Vector2 RootPoint(string node)
        {
            if (!_pinDots.TryGetValue(node, out var dot))
                return Vector2.zero;
            Vector3 wp = dot.TransformPoint(dot.rect.center);
            Vector3 lp = _root.transform.InverseTransformPoint(wp);
            var rr = ((RectTransform)_root.transform).rect;
            return new Vector2(lp.x - rr.xMin, lp.y - rr.yMin);
        }

        private static int Physical(PinDef pin)
        {
            foreach (var tp in Bench.Twin.TwinCatalog.Header40)
            {
                string node = tp.kind == Bench.Twin.TwinPinKind.Gpio
                    ? "gpio:" + tp.bcm : "power:" + tp.physical;
                if (node == pin.node) return tp.physical;
            }
            return 99;
        }

        private static string ShortName(PinDef pin)
        {
            string s = pin.label ?? pin.node;
            return s.Length > 6 ? s.Substring(0, 6) : s;
        }

        /// <summary>Compact header caption: 3V3 / 5V / GND full,
        /// GPIO shows its BCM number only (fits the 29 px pitch).</summary>
        private static string CompactName(PinDef pin)
        {
            if (pin.role == PinRole.Power5V) return "5V";
            if (pin.role == PinRole.Power3V3) return "3V3";
            if (pin.role == PinRole.Ground) return "GND";
            if (pin.node != null && pin.node.StartsWith("gpio:"))
                return pin.node.Substring(5);
            return ShortName(pin);
        }

        private static Sprite _circle;
        private static Sprite Circle()
        {
            if (_circle != null) return _circle;
            int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - s / 2f + 0.5f) / (s / 2f);
                    float dy = (y - s / 2f + 0.5f) / (s / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((1f - d) * 4f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, true);
            _circle = Sprite.Create(tex, new Rect(0, 0, s, s),
                new Vector2(0.5f, 0.5f), 64f);
            return _circle;
        }

        // ── wires ────────────────────────────────────────────────

        private void Update()
        {
            if (!IsShowing) return;
            _pollT += Time.unscaledDeltaTime;
            if (_pollT < 0.25f) return;
            _pollT = 0f;
            var wire = WireTool.Instance;
            int n = wire != null ? wire.Wires.Count : 0;
            if (n != _lastWireCount) RefreshWires(false);
        }

        public void RefreshWires(bool force)
        {
            if (_wireLayer == null) return;
            var wire = WireTool.Instance;
            _lastWireCount = wire != null ? wire.Wires.Count : 0;
            foreach (Transform c in _wireLayer)
                Destroy(c.gameObject);
            if (wire == null) return;
            foreach (var w in wire.Wires)
            {
                if (!_pinDots.ContainsKey(w.a)) continue;
                if (!_pinDots.ContainsKey(w.b)) continue;
                Line(_wireLayer, RootPoint(w.a), RootPoint(w.b),
                    new Color(1f, 0.45f, 0.35f, 0.95f), 5f);
            }
        }

        private void Line(Transform parent, Vector2 a, Vector2 b,
            Color color, float thick)
        {
            var go = new GameObject("WireLine");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            Vector2 mid = (a + b) * 0.5f;
            rt.anchoredPosition = mid;
            rt.sizeDelta = new Vector2(Vector2.Distance(a, b), thick);
            rt.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        // ── drag wiring (called by SchematicPinDrag) ────────────

        public void BeginPinDrag(string node, PointerEventData e)
        {
            _dragFrom = node;
            if (_dragLine == null)
            {
                var go = new GameObject("DragLine");
                go.transform.SetParent(_dotLayer, false);
                var rt0 = go.AddComponent<RectTransform>();
                rt0.anchorMin = Vector2.zero;
                rt0.anchorMax = Vector2.zero;
                rt0.pivot = new Vector2(0.5f, 0.5f);
                _dragLine = go.AddComponent<Image>();
                _dragLine.color = new Color(0.36f, 0.88f, 0.9f, 0.9f);
                _dragLine.raycastTarget = false;
            }
            _dragLine.gameObject.SetActive(true);
            var wire = WireTool.Instance;
            if (wire != null)
            {
                wire.CancelPending(quiet: true);
                wire.TapPin(node); // pending + compatible glow
            }
            MovePinDrag(e);
        }

        public void MovePinDrag(PointerEventData e)
        {
            if (_dragLine == null || _dragFrom == null) return;
            Vector2 a = RootPoint(_dragFrom);
            Vector2 to = LocalPoint(e);
            var rt = (RectTransform)_dragLine.transform;
            rt.anchoredPosition = (a + to) * 0.5f;
            rt.sizeDelta = new Vector2(Vector2.Distance(a, to), 4f);
            rt.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(to.y - a.y, to.x - a.x) * Mathf.Rad2Deg);
        }

        public void EndPinDrag(string node, PointerEventData e)
        {
            if (_dragLine != null) _dragLine.gameObject.SetActive(false);
            string target = PinAt(e, node);
            var wire = WireTool.Instance;
            if (wire != null && target != null && target != node)
            {
                wire.CancelPending(quiet: true);
                if (wire.TryCommit(node, target))
                    RefreshWires(false);
            }
            else if (wire != null)
            {
                wire.CancelPending(quiet: true);
                wire.TapPin(node); // plain pinch = select this pin
            }
            _dragFrom = null;
        }

        public void ClickPin(string node)
        {
            WireTool.Instance?.TapPin(node);
            RefreshWires(false);
        }

        private string PinAt(PointerEventData e, string exclude)
        {
            Vector2 p = LocalPoint(e);
            string best = null;
            float bestD = 34f;
            foreach (var kv in _pinDots)
            {
                if (kv.Key == exclude) continue;
                float d = Vector2.Distance(p, RootPoint(kv.Key));
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            return best;
        }

        private Vector2 LocalPoint(PointerEventData e)
        {
            var rt = (RectTransform)_root.transform;
            Vector2 lp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, e.position, e.pressEventCamera, out lp);
            var rr = rt.rect;
            return new Vector2(lp.x - rr.xMin, lp.y - rr.yMin);
        }

        // ── UI helpers ───────────────────────────────────────────

        private static TMPro.TMP_FontAsset _font;
        private static TMPro.TMP_FontAsset Font()
        {
            if (_font != null) return _font;
            foreach (var t in FindObjectsOfType<TMPro.TMP_Text>(true))
                if (t.font != null) { _font = t.font; break; }
            return _font;
        }

        private static Image Rect(Transform parent, float x, float y,
            float w, float h, Color color, bool raycast = true)
        {
            var go = new GameObject("Rect");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static void Label(Transform parent, string text,
            float size, Color color, float x, float y, float w, float h,
            bool bold,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            var f = Font();
            if (f != null) tmp.font = f;
            tmp.fontStyle = bold ? TMPro.FontStyles.Bold
                : TMPro.FontStyles.Normal;
        }

        /// <summary>Test hook: same commit path as a drag release.</summary>
        public bool SimulateDrag(string fromNode, string toNode)
        {
            var wire = WireTool.Instance;
            if (wire == null) return false;
            wire.CancelPending(quiet: true);
            bool ok = wire.TryCommit(fromNode, toNode);
            RefreshWires(false);
            return ok;
        }
    }

    /// <summary>Pointer drag/click on one schematic pin dot.</summary>
    public class SchematicPinDrag : MonoBehaviour, IBeginDragHandler,
        IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public string Node = "";
        public SchematicView View;
        private float _endT = -10f;

        public void OnBeginDrag(PointerEventData e) =>
            View?.BeginPinDrag(Node, e);

        public void OnDrag(PointerEventData e) => View?.MovePinDrag(e);

        public void OnEndDrag(PointerEventData e)
        {
            _endT = Time.unscaledTime;
            View?.EndPinDrag(Node, e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            // Suppress the click Unity fires right after a drag.
            if (Time.unscaledTime - _endT < 0.35f) return;
            View?.ClickPin(Node);
        }
    }
}
