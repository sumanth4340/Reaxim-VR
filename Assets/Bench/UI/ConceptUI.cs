// ConceptUI.cs — builders that reproduce the MR Electronics Bench
// concept HTML pixel-for-pixel: 1 canvas unit = 1 px at 0.001 scale,
// Arial type, exact concept hex values. Used by BuildConceptUI to
// construct every screen. Behaviour is wired by later tasks.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bench.UI
{
    public static class ConceptUI
    {
        public static TMP_FontAsset Arial;
        public static TMP_FontAsset ArialBold;

        // Concept palette (exact hexes from the HTML).
        public static readonly Color Ink = Color.white;
        public static readonly Color Sub = new Color(1f, 1f, 1f, 0.8f);
        public static readonly Color HintWhite = new Color(1f, 1f, 1f, 0.88f);
        public static readonly Color Cyan = new Color(0x5C / 255f, 0xE1 / 255f, 0xE6 / 255f, 1f);
        public static readonly Color CyanBright = new Color(0x8D / 255f, 0xF3 / 255f, 0xF6 / 255f, 1f);
        public static readonly Color TabBorder = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color RowFill = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color RowBorder = new Color(1f, 1f, 1f, 0.20f);
        public static readonly Color CardFill = new Color(1f, 1f, 1f, 0.13f);
        public static readonly Color CardBorder = new Color(1f, 1f, 1f, 0.25f);
        public static readonly Color SelFill = new Color(0x5C / 255f, 0xE1 / 255f, 0xE6 / 255f, 0.16f);
        public static readonly Color SelFillStrong = new Color(0x5C / 255f, 0xE1 / 255f, 0xE6 / 255f, 0.22f);
        public static readonly Color LabelBg = new Color(14f / 255f, 18f / 255f, 24f / 255f, 0.85f);
        public static readonly Color Red = new Color(1f, 0x6B / 255f, 0x6B / 255f, 1f);
        public static readonly Color Green = new Color(0x4A / 255f, 0xDE / 255f, 0x80 / 255f, 1f);
        public static readonly Color Amber = new Color(1f, 0xB4 / 255f, 0x54 / 255f, 1f);
        public static readonly Color ScopeBg = new Color(0x0A / 255f, 0x0A / 255f, 0x0A / 255f, 1f);

        /// <summary>Root canvas for one 1280x720 concept screen.</summary>
        public static Canvas Stage(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            go.AddComponent<GraphicRaycaster>();
            var rt = go.GetComponent<RectTransform>();
            rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);
            rt.sizeDelta = new Vector2(1280f, 720f);
            return c;
        }

        /// <summary>Place a child using stage pixel coords (origin top-left).</summary>
        public static RectTransform Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Glass(GameObject go, Color fill, Color rim,
            float radius, float rimAlpha = 1f)
        {
            var img = go.AddComponent<Image>();
            img.color = fill;
            img.raycastTarget = false;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = radius;
            return img;
        }

        public static TMP_Text Text(GameObject go, string s, float sizePx,
            Color color, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            // NOTE: TMP lives on a fresh child: the in-memory builder
            // domain returns null for a second renderable on one GO.
            var child = new GameObject("Txt");
            child.transform.SetParent(go.transform, false);
            var crt = child.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            crt.localScale = Vector3.one;
            var t = child.AddComponent<TextMeshProUGUI>();
            t.text = s;
            t.font = bold ? (ArialBold != null ? ArialBold : Arial) : Arial;
            if (t.font == null)
                t.font = Resources.GetBuiltinResource<TMP_FontAsset>("LiberationSans SDF");
            t.fontSize = sizePx;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }
    }
}
