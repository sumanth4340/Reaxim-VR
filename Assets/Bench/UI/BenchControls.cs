// BenchControls.cs — themed UI factory used by all Bench windows.
// Pill buttons (>=48px targets), 72px list rows, template cards,
// confirm dialogs, toggles, segmented controls. All TMP, all colours
// and sizes from BenchTheme (1 canvas px = 1 mm at 0.001 scale).
// NOTE: built on Unity UI + TMP for exact concept fidelity; migrating
// these wrappers to Meta ISDK UI Set prefabs is a Task-10 polish item.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bench.UI
{
    public static class BenchControls
    {
        public delegate void Clicked();

        public static GameObject PillButton(Transform parent, string label,
            bool selected, Action onClick, float width = 0f)
        {
            var theme = BenchTheme.Current;
            var go = new GameObject("Pill_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            var tmp = AddLabel(go, label, selected ? theme.accent : theme.textPrimary,
                theme.interSemiBold, theme.bodySize, TextAlignmentOptions.Center);
            float w = width > 0 ? width : tmp.preferredWidth + 64f;
            rt.sizeDelta = new Vector2(Mathf.Max(w, 200f), 48f);
            var bg = go.AddComponent<Image>();
            bg.color = selected ? new Color(theme.accent.r, theme.accent.g,
                theme.accent.b, 0.22f) : new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock cb = btn.colors;
            cb.pressedColor = new Color(1f, 1f, 1f, 0.25f);
            cb.selectedColor = bg.color;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick();
            });
            var outline = go.AddComponent<Outline>();
            outline.effectColor = selected ? theme.accent : new Color(1f, 1f, 1f, 0.45f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
            return go;
        }

        public static TMP_Text AddLabel(GameObject parent, string text, Color color,
            TMP_FontAsset font, float size, TextAlignmentOptions align)
        {
            var theme = BenchTheme.Current;
            var go = new GameObject("Label");
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = font != null ? font : theme.interRegular;
            tmp.fontSize = BenchTheme.Scaled(size);
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        public class Row
        {
            public GameObject root;
            public TMP_Text title;
            public TMP_Text subtitle;
            public Button openButton;
            public Button overflowButton;
        }

        /// <summary>72 px project row: name + sub line + open + overflow.</summary>
        public static Row ProjectRow(Transform parent, Action onOpen, Action onOverflow)
        {
            var theme = BenchTheme.Current;
            var go = new GameObject("Row");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(0f, 72f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            bg.raycastTarget = true;
            var open = go.AddComponent<Button>();
            open.targetGraphic = bg;
            if (onOpen != null) open.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onOpen();
            });

            var title = AddLabel(go, "", theme.textPrimary, theme.interSemiBold,
                theme.bodySize, TextAlignmentOptions.Left);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 0.5f);
            titleRt.offsetMin = new Vector2(88f, -2f);
            titleRt.offsetMax = new Vector2(-64f, 34f);

            var sub = AddLabel(go, "", theme.textSecondary, theme.interRegular,
                theme.labelSize, TextAlignmentOptions.Left);
            var subRt = (RectTransform)sub.transform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(1f, 0.5f);
            subRt.offsetMin = new Vector2(88f, -34f);
            subRt.offsetMax = new Vector2(-64f, 2f);

            var dot = new GameObject("Dot");
            dot.transform.SetParent(go.transform, false);
            var dotRt = dot.AddComponent<RectTransform>();
            dotRt.localScale = Vector3.one;
            dotRt.anchorMin = dotRt.anchorMax = new Vector2(0f, 0.5f);
            dotRt.sizeDelta = new Vector2(28f, 28f);
            dotRt.anchoredPosition = new Vector2(52f, 0f);
            var dotImg = dot.AddComponent<Image>();
            dotImg.color = theme.accent;
            dotImg.raycastTarget = false;

            var ov = new GameObject("Overflow");
            ov.transform.SetParent(go.transform, false);
            var ovRt = ov.AddComponent<RectTransform>();
            ovRt.localScale = Vector3.one;
            ovRt.anchorMin = ovRt.anchorMax = new Vector2(1f, 0.5f);
            ovRt.sizeDelta = new Vector2(56f, 56f);
            ovRt.anchoredPosition = new Vector2(-28f, 0f);
            var ovImg = ov.AddComponent<Image>();
            ovImg.color = new Color(1f, 1f, 1f, 0.10f);
            var ovBtn = ov.AddComponent<Button>();
            ovBtn.targetGraphic = ovImg;
            AddLabel(ov, "···", theme.textSecondary, theme.interSemiBold,
                28f, TextAlignmentOptions.Center);
            if (onOverflow != null) ovBtn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onOverflow();
            });
            return new Row { root = go, title = title, subtitle = sub,
                openButton = open, overflowButton = ovBtn };
        }

        public class Card
        {
            public GameObject root;
            public Button button;
            public Image frame;
        }

        /// <summary>Template card with title + spec line.</summary>
        public static Card TemplateCard(Transform parent, string title, string spec,
            Vector2 size, Action onPick)
        {
            var theme = BenchTheme.Current;
            var go = new GameObject("Card_" + title);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.sizeDelta = size;
            var frame = go.AddComponent<Image>();
            frame.color = new Color(1f, 1f, 1f, 0.08f);
            frame.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = frame;
            var t = AddLabel(go, title, theme.textPrimary, theme.interSemiBold,
                theme.headingSize, TextAlignmentOptions.Center);
            var tRt = (RectTransform)t.transform;
            tRt.offsetMin = new Vector2(12f, 44f);
            tRt.offsetMax = new Vector2(-12f, -12f);
            var s = AddLabel(go, spec, theme.textSecondary, theme.interRegular,
                theme.labelSize, TextAlignmentOptions.Center);
            var sRt = (RectTransform)s.transform;
            sRt.offsetMin = new Vector2(12f, 8f);
            sRt.offsetMax = new Vector2(-12f, 44f);
            if (onPick != null) btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onPick();
            });
            return new Card { root = go, button = btn, frame = frame };
        }

        public static void SetCardSelected(Card card, bool selected)
        {
            var theme = BenchTheme.Current;
            card.frame.color = selected
                ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.20f)
                : new Color(1f, 1f, 1f, 0.08f);
        }

        public class ConfirmDialog
        {
            public GameObject root;
        }

        /// <summary>Centred modal: message + Cancel / destructive action.</summary>
        public static ConfirmDialog Confirm(Transform parent, string message,
            string actionLabel, Action onConfirm)
        {
            var theme = BenchTheme.Current;
            var go = new GameObject("ConfirmDialog");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var dim = go.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            var box = new GameObject("Box");
            box.transform.SetParent(go.transform, false);
            var boxRt = box.AddComponent<RectTransform>();
            boxRt.localScale = Vector3.one;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(420f, 220f);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0x0F / 255f, 0x12 / 255f, 0x18 / 255f, 0.98f);
            boxImg.raycastTarget = true;

            var msg = AddLabel(box, message, theme.textPrimary, theme.interRegular,
                theme.bodySize, TextAlignmentOptions.Center);
            var msgRt = (RectTransform)msg.transform;
            msgRt.offsetMin = new Vector2(24f, 80f);
            msgRt.offsetMax = new Vector2(-24f, -16f);

            var cancel = PillButton(box.transform, "Cancel", false,
                () => UnityEngine.Object.Destroy(go), 160f);
            var cancelRt = (RectTransform)cancel.transform;
            cancelRt.anchorMin = cancelRt.anchorMax = new Vector2(0f, 0f);
            cancelRt.anchoredPosition = new Vector2(110f, 52f);
            var del = PillButton(box.transform, actionLabel, true, () =>
            {
                UnityEngine.Object.Destroy(go);
                onConfirm?.Invoke();
            }, 160f);
            var delRt = (RectTransform)del.transform;
            delRt.anchorMin = delRt.anchorMax = new Vector2(1f, 0f);
            delRt.anchoredPosition = new Vector2(-110f, 52f);
            return new ConfirmDialog { root = go };
        }

        public class ToggleRow
        {
            public GameObject root;
            public Button button;
            public GameObject knob;
            public TMP_Text label;
        }

        /// <summary>Settings row: label + on/off pill toggle.</summary>
        public static ToggleRow Toggle(Transform parent, string label, bool value,
            Action<bool> onChange)
        {
            var theme = BenchTheme.Current;
            bool state = value;
            var go = new GameObject("Toggle_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(0f, 56f);
            var lab = AddLabel(go, label, theme.textPrimary, theme.interRegular,
                theme.bodySize, TextAlignmentOptions.Left);
            var labRt = (RectTransform)lab.transform;
            labRt.offsetMin = new Vector2(0f, 0f);
            labRt.offsetMax = new Vector2(-140f, 0f);

            var pill = new GameObject("Pill");
            pill.transform.SetParent(go.transform, false);
            var pillRt = pill.AddComponent<RectTransform>();
            pillRt.localScale = Vector3.one;
            pillRt.anchorMin = pillRt.anchorMax = new Vector2(1f, 0.5f);
            pillRt.sizeDelta = new Vector2(104f, 48f);
            pillRt.anchoredPosition = new Vector2(-64f, 0f);
            var pillImg = pill.AddComponent<Image>();
            pillImg.raycastTarget = true;
            var btn = pill.AddComponent<Button>();
            btn.targetGraphic = pillImg;

            var knob = new GameObject("Knob");
            knob.transform.SetParent(pill.transform, false);
            var knobRt = knob.AddComponent<RectTransform>();
            knobRt.localScale = Vector3.one;
            knobRt.sizeDelta = new Vector2(36f, 36f);
            var knobImg = knob.AddComponent<Image>();
            knobImg.color = Color.white;
            knobImg.raycastTarget = false;

            System.Action refresh = () =>
            {
                pillImg.color = state
                    ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.85f)
                    : new Color(1f, 1f, 1f, 0.18f);
                knobRt.anchorMin = knobRt.anchorMax =
                    new Vector2(state ? 1f : 0f, 0.5f);
                knobRt.anchoredPosition = new Vector2(state ? -26f : 26f, 0f);
            };
            refresh();
            btn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                state = !state;
                refresh();
                onChange?.Invoke(state);
            });
            return new ToggleRow { root = go, button = btn, knob = knob, label = lab };
        }

        public class Segmented
        {
            public GameObject root;
            public List<Button> buttons = new List<Button>();
        }

        /// <summary>Segmented option row (e.g. text size 100/125/150%).</summary>
        public static Segmented SegmentedRow(Transform parent, string label,
            string[] options, int selected, Action<int> onPick)
        {
            var theme = BenchTheme.Current;
            int idx = Mathf.Clamp(selected, 0, options.Length - 1);
            var go = new GameObject("Segmented_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(0f, 56f);
            var lab = AddLabel(go, label, theme.textPrimary, theme.interRegular,
                theme.bodySize, TextAlignmentOptions.Left);
            var labRt = (RectTransform)lab.transform;
            labRt.offsetMin = new Vector2(0f, 0f);
            labRt.offsetMax = new Vector2(-340f, 0f);

            var row = new GameObject("Options");
            row.transform.SetParent(go.transform, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.localScale = Vector3.one;
            rowRt.anchorMin = new Vector2(1f, 0f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.offsetMin = new Vector2(-328f, 4f);
            rowRt.offsetMax = new Vector2(-8f, -4f);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;

            var seg = new Segmented { root = go };
            for (int i = 0; i < options.Length; i++)
            {
                int pick = i;
                var b = PillButton(row.transform, options[i], i == idx, null, 96f);
                var bRt = (RectTransform)b.transform;
                bRt.sizeDelta = new Vector2(96f, 48f);
                var btn = b.GetComponent<Button>();
                var bg = b.GetComponent<Image>();
                seg.buttons.Add(btn);
                btn.onClick.AddListener(() =>
                {
                    SoundBank.Play(BenchSound.Press);
                    onPick?.Invoke(pick);
                });
                _ = bg;
            }
            return seg;
        }

        /// <summary>Vertical-scroll list container (returns content RT).</summary>
        public static RectTransform ScrollList(Transform parent, float padTop = 0f)
        {
            var go = new GameObject("ScrollView");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, -padTop);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            var scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 28f;
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(go.transform, false);
            var vRt = viewport.AddComponent<RectTransform>();
            vRt.localScale = Vector3.one;
            vRt.anchorMin = Vector2.zero;
            vRt.anchorMax = Vector2.one;
            vRt.sizeDelta = Vector2.zero;
            var mask = viewport.AddComponent<RectMask2D>();
                var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var cRt = content.AddComponent<RectTransform>();
            cRt.localScale = Vector3.one;
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.sizeDelta = new Vector2(0f, 0f);
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            content.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = vRt;
            scroll.content = cRt;
            return cRt;
        }
    }
}
