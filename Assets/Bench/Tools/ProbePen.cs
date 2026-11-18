// ProbePen.cs — grabbable scope/meter probe (Task 8).
// Origin at the tip for placement math. Drag onto a pin, wire,
// hole or lead (3 cm snap); release over the tray to park it.
// Same input paths as parts: mouse in editor, test hooks here,
// device ray via Task-10 baked prefabs (documented in PlacedPart).
using UnityEngine;

namespace Bench.Tools
{
    public class ProbePen : MonoBehaviour
    {
        public string probeId = "";
        public Color color = Color.red;
        public string attachedNode;
        public bool IsAttached => !string.IsNullOrEmpty(attachedNode);

        private bool _carried;
        private bool _testDrive;
        private Vector3 _restPos;
        private Quaternion _restRot;
        private TMPro.TextMeshPro _chip;
        private float _pulseT;

        public static ProbePen Spawn(string id, Color color, Vector3 restPos)
        {
            var go = new GameObject("Probe_" + id);
            go.transform.position = restPos;
            go.transform.rotation = Quaternion.identity;
            var pen = go.AddComponent<ProbePen>();
            pen.probeId = id;
            pen.color = color;
            // Tip cone (origin at the point) + handle.
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tip.name = "Tip";
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            tip.transform.localScale = new Vector3(0.004f, 0.04f, 0.004f);
            tip.GetComponent<Renderer>().material.color =
                new Color(0.75f, 0.75f, 0.78f, 1f);
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Handle";
            grip.transform.SetParent(go.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0.085f, 0f);
            grip.transform.localScale = new Vector3(0.016f, 0.09f, 0.016f);
            grip.GetComponent<Renderer>().material.color = color;
            var tg = new GameObject("NetChip");
            tg.transform.SetParent(go.transform, false);
            tg.transform.localPosition = new Vector3(0f, 0.16f, 0f);
            var tmp = tg.AddComponent<TMPro.TextMeshPro>();
            tmp.text = "—";
            tmp.fontSize = 22f;
            tmp.color = Color.white;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.transform.localScale = Vector3.one * 0.008f;
            pen._chip = tmp;
            pen._restPos = restPos;
            pen._restRot = Quaternion.identity;
            pen.UpdateChip();
            go.AddComponent<Bench.Hands.PenHandGrab>();
            return pen;
        }

        public void SetNet(string node)
        {
            attachedNode = node;
            UpdateChip();
        }

        public void Rest()
        {
            transform.position = _restPos;
            transform.rotation = _restRot;
            attachedNode = null;
            UpdateChip();
        }

        public void Pulse() => _pulseT = 0.25f;

        private void UpdateChip()
        {
            if (_chip != null)
                _chip.text = string.IsNullOrEmpty(attachedNode) ? "—"
                    : ShortNet(attachedNode);
        }

        private static string ShortNet(string node)
        {
            if (node.StartsWith("gpio:")) return "GPIO" + node.Substring(5);
            if (node.StartsWith("power:"))
            {
                if (int.TryParse(node.Substring(6), out var phys))
                    foreach (var p in Twin.TwinCatalog.Header40)
                        if (p.physical == phys) return p.name;
                return node;
            }
            if (node.StartsWith("term:"))
            {
                var parts = node.Substring(5).Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var r)
                    && int.TryParse(parts[1], out var c))
                    return Twin.TwinBreadboard.NetId(r, c);
            }
            if (node.StartsWith("comp:"))
            {
                var parts = node.Split(':');
                if (parts.Length == 3) return "leg " + parts[2];
            }
            return node;
        }

        // ── carry (mirrors PlacedPart paths) ─────────────────────

        public void TestBeginCarry(Vector3 target)
        {
            _testDrive = true;
            BeginCarry();
            transform.position = target;
        }

        public void TestMoveCarry(Vector3 target)
        {
            if (_carried) transform.position = target;
        }

        public void TestEndCarry(bool drop) => EndCarry(drop);

        private void BeginCarry()
        {
            if (_carried) return;
            _carried = true;
            if (!string.IsNullOrEmpty(attachedNode))
                Instruments.Instance?.ReleasePen(this);
        }

        private void EndCarry(bool drop)
        {
            if (!_carried) return;
            _carried = false;
            _testDrive = false;
            if (drop) Instruments.Instance?.DropPen(this, transform.position);
        }

        private void OnMouseDown()
        {
            var cam = Camera.main;
            if (cam == null) return;
            BeginCarry();
        }

        private void OnMouseDrag()
        {
            if (!_carried) return;
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(cam.transform.forward, transform.position);
            if (plane.Raycast(ray, out var d))
                transform.position = ray.GetPoint(d);
        }

        private void OnMouseUp()
        {
            if (_carried) EndCarry(true);
        }

        private void Update()
        {
            if (_pulseT > 0f)
            {
                _pulseT -= Time.unscaledDeltaTime;
                float s = 1f + Mathf.Max(0f, _pulseT) * 1.2f;
                transform.localScale = Vector3.one * s;
                if (_pulseT <= 0f) transform.localScale = Vector3.one;
            }
            if (_carried && !_testDrive)
            {
                var cam = Camera.main;
                if (cam != null)
                    transform.position = cam.transform.position
                        + cam.transform.forward * 0.6f;
            }
        }
    }
}
