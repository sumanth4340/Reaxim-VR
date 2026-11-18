// PinLabels.cs — pooled pin labels with declutter (Task 7).
// Glass-dark pills (22px Arial) + thin leader to the pin, billboarded.
// Declutter at 10 Hz: screen-space grid, max 80 labels, nearest and
// selected first; the rest render as tiny dots. Primitives only, so
// everything renders without custom shader variants.
using System.Collections.Generic;
using UnityEngine;
using Bench.Circuit;

namespace Bench.PinMode
{
    public class PinLabels : MonoBehaviour
    {
        private class LabelItem
        {
            public GameObject root;
            public TMPro.TextMeshPro text;
            public Transform pill;
            public Transform leader;
        }

        private readonly List<LabelItem> _pool = new List<LabelItem>();
        private readonly List<GameObject> _dots = new List<GameObject>();
        private const int MaxLabels = 80;
        private const int MaxDots = 200;
        private TMPro.TMP_FontAsset _font;

        private void Awake()
        {
            _font = Bench.UI.ConceptUI.Arial;
            if (_font == null) _font = TMPro.TMP_Settings.defaultFontAsset;
            for (int i = 0; i < MaxLabels; i++)
                _pool.Add(MakeLabel());
            for (int i = 0; i < MaxDots; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                d.name = "PinDot_Small";
                d.transform.SetParent(transform, false);
                d.transform.localScale = Vector3.one * 0.006f;
                var mr = d.GetComponent<Renderer>();
                var mat = mr.material;
                mat.color = new Color(1f, 1f, 1f, 1f);
                d.SetActive(false);
                _dots.Add(d);
            }
        }

        private LabelItem MakeLabel()
        {
            var root = new GameObject("PinLabel");
            root.transform.SetParent(transform, false);
            var pill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pill.name = "Pill";
            pill.transform.SetParent(root.transform, false);
            pill.transform.localScale = new Vector3(0.074f, 0.03f, 1f);
            var pmr = pill.GetComponent<Renderer>();
            var pmat = pmr.material;
            pmat.color = new Color(0.055f, 0.07f, 0.094f, 1f);
            var tg = new GameObject("Txt");
            tg.transform.SetParent(root.transform, false);
            tg.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
            var tmp = tg.AddComponent<TMPro.TextMeshPro>();
            tmp.font = _font;
            tmp.fontSize = 22f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            var rt = tmp.rectTransform;
            rt.sizeDelta = new Vector2(74f, 30f);
            var leader = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leader.name = "Leader";
            leader.transform.SetParent(root.transform, false);
            leader.GetComponent<Renderer>().material.color =
                new Color(1f, 1f, 1f, 0.6f);
            root.SetActive(false);
            return new LabelItem { root = root, text = tmp,
                pill = pill.transform, leader = leader.transform };
        }

        public void HideAll()
        {
            foreach (var l in _pool) l.root.SetActive(false);
            foreach (var d in _dots) d.SetActive(false);
        }

        /// <summary>Full declutter pass (called at 10 Hz).</summary>
        public void Rebuild(List<PinDef> pins, Camera cam,
            System.Func<PinDef, bool> visible, string selectedNode)
        {
            if (cam == null) { HideAll(); return; }
            var scored = new List<(PinDef pin, float score)>();
            foreach (var pin in pins)
            {
                if (visible != null && !visible(pin)) continue;
                Vector3 sp = cam.WorldToScreenPoint(pin.WorldPos);
                if (sp.z <= 0f) continue;
                float dist = Vector3.Distance(cam.transform.position, pin.WorldPos);
                float score = 1000f / (1f + dist);
                if (pin.node == selectedNode) score += 10000f;
                if (pin.part != null && pin.part.isBoard) score += 500f;
                scored.Add((pin, score));
            }
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var used = new HashSet<int>();
            int li = 0, di = 0;
            foreach (var (pin, _) in scored)
            {
                Vector3 sp = cam.WorldToScreenPoint(pin.WorldPos);
                // Cells match the pill footprint (~190x55px) so placed
                // labels never overlap; the rest become dots.
                int cell = ((int)(sp.x / 200f)) * 73856093
                    ^ ((int)(sp.y / 60f)) * 19349663;
                if (li < MaxLabels && !used.Contains(cell))
                {
                    used.Add(cell);
                    ShowLabel(_pool[li++], pin, cam);
                }
                else if (di < MaxDots)
                {
                    var d = _dots[di++];
                    d.SetActive(true);
                    d.transform.position = pin.WorldPos;
                }
            }
            for (int i = li; i < MaxLabels; i++) _pool[i].root.SetActive(false);
            for (int i = di; i < MaxDots; i++) _dots[i].SetActive(false);
        }

        private void ShowLabel(LabelItem item, PinDef pin, Camera cam)
        {
            item.root.SetActive(true);
            item.root.transform.position = pin.WorldPos + Vector3.up * 0.035f;
            item.root.transform.rotation = cam.transform.rotation;
            item.text.text = PinModeController.PinGlyph(pin) + " " + pin.label;
            item.text.color = Color.white;
            // Fixed world scale from a measured constant: fontSize 22 at
            // scale 1 renders 2.46 units tall (probed live), so 0.008
            // gives ~20 mm text height. Pill fits measured width.
            const float s = 0.008f;
            item.text.transform.localScale = Vector3.one * s;
            item.text.ForceMeshUpdate();
            var tb = item.text.textBounds;
            float w = Mathf.Max(0.03f, tb.size.x * s + 0.014f);
            item.pill.localScale = new Vector3(w, 0.03f, 1f);
            var mr = item.pill.GetComponent<Renderer>();
            mr.material.color = new Color(0.055f, 0.07f, 0.094f, 1f);
            // Leader from pill bottom to the pin.
            Vector3 a = item.root.transform.position + Vector3.down * 0.015f;
            Vector3 b = pin.WorldPos;
            Vector3 mid = (a + b) / 2f;
            item.leader.position = mid;
            item.leader.rotation = Quaternion.identity;
            float len = Vector3.Distance(a, b);
            item.leader.localScale = new Vector3(0.002f, Mathf.Max(0.004f, len), 0.002f);
            var lmr = item.leader.GetComponent<Renderer>();
            lmr.material.color = PinModeController.RoleColor(pin.role);
        }

        private void Update()
        {
            // Billboard active labels every frame (cheap, smooth).
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var l in _pool)
            {
                if (l.root.activeSelf)
                    l.root.transform.rotation = cam.transform.rotation;
            }
        }
    }
}
