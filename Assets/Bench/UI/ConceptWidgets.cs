// ConceptWidgets.cs — concept-exact UI building blocks (see the
// MR Electronics Bench HTML mockups): pill tabs, project rows, pin
// labels with leaders, filter chips, component tiles, instrument
// readouts. All positions are stage pixels (1280x720 @ 0.001).
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bench.UI
{
    public static class ConceptWidgets
    {
        /// <summary>Glass card with EXACT concept fill/border/radius.</summary>
        public static GlassPanel Card(Transform parent, float x, float y,
            float w, float h, float radius, Color fill, Color rim)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = radius;
            gp.fillOverride = fill;
            gp.rimOverride = rim;
            gp.ApplyTheme();
            return gp;
        }

        /// <summary>Pill tab (h44, radius 22). Selected = cyan.</summary>
        public static GameObject PillTab(Transform parent, float x, float y,
            float w, string label, bool selected, System.Action onClick)
        {
            var go = new GameObject("Tab_" + label);
            go.transform.SetParent(parent, false);
            var rt = ConceptUI.Place(go, x, y, w, 44f);
            var img = go.AddComponent<Image>();
            img.color = selected ? ConceptUI.SelFillStrong : Color.clear;
            img.raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 22f;
            gp.fillOverride = selected ? ConceptUI.SelFillStrong : new Color(1, 1, 1, 0.02f);
            gp.rimOverride = selected ? ConceptUI.Cyan : ConceptUI.TabBorder;
            gp.ApplyTheme();
            var t = ConceptUI.Text(go, label, 17f,
                selected ? ConceptUI.CyanBright : Color.white,
                true, TextAlignmentOptions.Center);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.sizeDelta = Vector2.zero;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick();
            });
            return go;
        }

        /// <summary>Project row: h78, radius 20, 21px name / 16px sub / 16px time.</summary>
        public static GameObject ProjectRow(Transform parent, float x, float y, float w,
            string name, string sub, string time, bool selected, System.Action onClick)
        {
            var go = new GameObject("Row_" + name);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, w, 78f);
            go.AddComponent<Image>().raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 20f;
            gp.fillOverride = selected ? ConceptUI.SelFill : ConceptUI.RowFill;
            gp.rimOverride = selected ? ConceptUI.Cyan : ConceptUI.RowBorder;
            gp.ApplyTheme();
            var t = ConceptUI.Text(go, name, 21f, Color.white, true);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(22, -8); trt.offsetMax = new Vector2(-80, -34);
            var s = ConceptUI.Text(go, sub, 16f, ConceptUI.Sub, false);
            var srt = (RectTransform)s.transform;
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(22, -38); srt.offsetMax = new Vector2(-80, -62);
            var tm = ConceptUI.Text(go, time, 16f, ConceptUI.Sub, false,
                TextAlignmentOptions.Right);
            var trt2 = (RectTransform)tm.transform;
            trt2.anchorMin = new Vector2(1, 1); trt2.anchorMax = new Vector2(1, 1);
            trt2.offsetMin = new Vector2(-150, -8); trt2.offsetMax = new Vector2(-22, -34);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            if (onClick != null) btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick();
            });
            return go;
        }

        /// <summary>Pin label pill (74x30) + leader line + pin dot.</summary>
        public static GameObject PinLabel(Transform parent, float x, float y,
            string text, Color border)
        {
            var go = new GameObject("Pin_" + text);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, 74f, 30f);
            go.AddComponent<Image>().raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = false; gp.dark = true;
            gp.radiusOverride = 15f;
            gp.fillOverride = ConceptUI.LabelBg;
            gp.rimOverride = border; // rim width stays 2px shader-side.
            gp.ApplyTheme();
            var t = ConceptUI.Text(go, text, 15f, Color.white, true,
                TextAlignmentOptions.Center);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            // Leader line + dot below the pill.
            var line = new GameObject("Leader");
            line.transform.SetParent(parent, false);
            ConceptUI.Place(line, x + 36f, y + 30f, 2f, 14f);
            var li = line.AddComponent<Image>();
            li.color = new Color(1f, 1f, 1f, 0.6f);
            li.raycastTarget = false;
            var dot = new GameObject("Dot");
            dot.transform.SetParent(parent, false);
            ConceptUI.Place(dot, x + 33f, y + 44f, 8f, 8f);
            var di = dot.AddComponent<Image>();
            di.color = Color.white;
            di.raycastTarget = false;
            return go;
        }

        /// <summary>Filter chip (h40 pill, selected = cyan).</summary>
        public static GameObject Chip(Transform parent, float x, float y,
            float w, string label, bool selected, System.Action onClick)
        {
            var go = new GameObject("Chip_" + label);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, w, 40f);
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            var gp = go.AddComponent< GlassPanel>();
            gp.light = !selected;
            gp.dark = false;
            gp.radiusOverride = 20f;
            gp.fillOverride = selected ? ConceptUI.SelFillStrong : new Color(1f, 1f, 1f, 0.06f);
            gp.rimOverride = selected ? ConceptUI.Cyan : new Color(1f, 1f, 1f, 0.25f);
            gp.ApplyTheme();
            var t = ConceptUI.Text(go, label, 15f,
                selected ? ConceptUI.CyanBright : ConceptUI.Sub, true,
                TextAlignmentOptions.Center);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick();
            });
            return go;
        }

        /// <summary>Component tile card 182x104 with name + optional badge.</summary>
        public static GameObject Tile(Transform parent, float x, float y,
            string name, string badge, Color badgeColor, RectTransform viewportParent)
        {
            var go = new GameObject("Tile_" + name);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, 182f, 104f);
            go.AddComponent<Image>().raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 20f;
            gp.fillOverride = ConceptUI.CardFill;
            gp.rimOverride = ConceptUI.CardBorder;
            gp.ApplyTheme();
            // Viewport area occupies the tile; name sits at the bottom.
            var n = ConceptUI.Text(go, name, 15f, Color.white, true,
                TextAlignmentOptions.Center);
            var nrt = (RectTransform)n.transform;
            nrt.anchorMin = new Vector2(0, 0); nrt.anchorMax = new Vector2(1, 0);
            nrt.offsetMin = new Vector2(4, 4); nrt.offsetMax = new Vector2(-4, 26);
            if (!string.IsNullOrEmpty(badge))
            {
                var b = new GameObject("Badge");
                b.transform.SetParent(go.transform, false);
                var brt = ConceptUI.Place(b, 0, 0, 0, 0);
                brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1);
                brt.anchoredPosition = new Vector2(8, -8);
                brt.sizeDelta = new Vector2(64f, 22f);
                var bi = b.AddComponent<Image>();
                bi.color = new Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.9f);
                var bt = ConceptUI.Text(b, badge, 11f, Color.white, true,
                    TextAlignmentOptions.Center);
                var btrt = (RectTransform)bt.transform;
                btrt.anchorMin = Vector2.zero; btrt.anchorMax = Vector2.one;
                btrt.sizeDelta = Vector2.zero;
            }
            return go;
        }

        public sealed class CardRef
        {
            public GameObject root;
        }

        /// <summary>Starter template card (196x150): title + one-line sub.</summary>
        public static CardRef TemplateCard(Transform parent, string title,
            string sub, Vector2 size, System.Action onClick)
        {
            var go = new GameObject("Template_" + title);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, 0f, 0f, size.x, size.y);
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 20f;
            gp.fillOverride = ConceptUI.CardFill;
            gp.rimOverride = ConceptUI.CardBorder;
            gp.ApplyTheme();
            var t = ConceptUI.Text(go, title, 19f, Color.white, true,
                TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(16, -14); trt.offsetMax = new Vector2(-16, -44);
            var s = ConceptUI.Text(go, sub, 15f, ConceptUI.Sub, false,
                TextAlignmentOptions.Left);
            var srt = (RectTransform)s.transform;
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(16, -48); srt.offsetMax = new Vector2(-16, -100);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick();
            });
            return new CardRef { root = go };
        }

        /// <summary>Vertical scroll region (ScrollRect + content column).</summary>
        public static GameObject ScrollList(Transform parent)
        {
            var go = new GameObject("ScrollList");
            go.transform.SetParent(parent, false);
            // Top-left pivot so anchoredPosition pins the top-left corner
            // (default centre pivot would shift content by half size).
            var grt = go.AddComponent<RectTransform>();
            grt.pivot = new Vector2(0f, 1f);
            var sr = go.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            var view = new GameObject("Viewport");
            view.transform.SetParent(go.transform, false);
            var vrt = view.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            view.AddComponent<Image>().raycastTarget = true;
            view.AddComponent<Mask>().showMaskGraphic = false;
            var content = new GameObject("Content");
            content.transform.SetParent(view.transform, false);
            var crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0, 1);
            crt.offsetMin = new Vector2(0, 0); crt.offsetMax = new Vector2(0, 0);
            crt.sizeDelta = new Vector2(0, 0);
            sr.viewport = vrt;
            sr.content = crt;
            return go;
        }

        /// <summary>Full-width centered hint line at stage y.</summary>
        public static void Hint(Transform parent, float y, string text)
        {
            var go = new GameObject("Hint");
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, 0f, y, 1280f, 24f);
            ConceptUI.Text(go, text, 15f, ConceptUI.HintWhite, false,
                TextAlignmentOptions.Center);
        }
    }
}
