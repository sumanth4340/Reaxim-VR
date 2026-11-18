// HomeScreen.cs — HOME window content: Create / Open tabs, template
// cards, name field (system keyboard on device, hardware keys in
// editor), recents list with overflow menu + confirm dialog, empty
// state, and the settings gear panel. Talks to ProjectManager
// (Task 3) and AppStateMachine (Task 2). One canvas (the window's).
using System;
using System.Collections.Generic;
using Bench.Core;
using Bench.Panels;
using Bench.UI;
using BUI = global::Bench.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bench.Panels
{
    public class HomeScreen : MonoBehaviour
    {
        [Tooltip("Wired by the build script.")]
        public BUI.GlassWindow window;

        private enum Tab { Create, Open }

        private Tab _tab;
        private GameObject _createPane;
        private GameObject _openPane;
        private TMP_InputField _nameField;
        private string _template = Projects.ProjectTemplates.Blank;
        private readonly List<BUI.BenchControls.Card> _cards =
            new List<BUI.BenchControls.Card>();
        private readonly List<GameObject> _tabPills = new List<GameObject>();
        private RectTransform _openContent;
        private GameObject _emptyState;
        private GameObject _settingsPanel;
        private Projects.ProjectManager Mgr =>
            Projects.ProjectManager.Instance;

        private void Awake()
        {
            if (window == null) window = GetComponent<BUI.GlassWindow>();
        }

        private void OnEnable()
        {
            Build();
            Refresh();
        }

        private void OnDisable()
        {
            if (Mgr != null) { }
        }

        private void Build()
        {
            if (window.tabRow.childCount > 0) return; // built once.
            var theme = BUI.BenchTheme.Current;
            if (window.titleLabel != null)
                window.titleLabel.text = "MR Electronics Bench";

            _tabPills.Add(BUI.BenchControls.PillButton(window.tabRow,
                "Create Project", false, () => SelectTab(Tab.Create)));
            _tabPills.Add(BUI.BenchControls.PillButton(window.tabRow,
                "Open Project", false, () => SelectTab(Tab.Open)));

            _createPane = Pane("CreatePane");
            _openPane = Pane("OpenPane");
            BuildCreatePane();
            BuildOpenPane();
            BuildGear();
        }

        public void Refresh()
        {
            bool hasRecent = Mgr != null && Mgr.Recents().Count > 0;
            SelectTab(hasRecent ? Tab.Open : Tab.Create, instant: true);
            if (_tab == Tab.Open) RebuildRecents();
            SuggestName();
        }

        // ── Tabs ─────────────────────────────────────────────────

        private void SelectTab(Tab tab, bool instant = false)
        {
            _tab = tab;
            SetPill(0, tab == Tab.Create);
            SetPill(1, tab == Tab.Open);
            Crossfade(tab == Tab.Create ? _createPane : _openPane,
                tab == Tab.Create ? _openPane : _createPane, instant);
            if (tab == Tab.Open) RebuildRecents();
            if (tab == Tab.Create) SuggestName();
        }

        private void SetPill(int i, bool selected)
        {
            var theme = BUI.BenchTheme.Current;
            var img = _tabPills[i].GetComponent<Image>();
            img.color = selected
                ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.22f)
                : new Color(1f, 1f, 1f, 0.08f);
        }

        private void Crossfade(GameObject show, GameObject hide, bool instant)
        {
            hide.SetActive(false);
            show.SetActive(true);
            var cg = show.GetComponent<CanvasGroup>();
            if (cg == null) cg = show.AddComponent<CanvasGroup>();
            var theme = BUI.BenchTheme.Current;
            float dur = instant ? 0f : theme.tabSwitchDuration;
            var rt = (RectTransform)show.transform;
            Vector2 from = new Vector2(12f, rt.anchoredPosition.y);
            Vector2 to = new Vector2(0f, rt.anchoredPosition.y);
            if (dur <= 0f) { cg.alpha = 1f; rt.anchoredPosition = to; return; }
            rt.anchoredPosition = from;
            cg.alpha = 0f;
            MiniTween.Float(this, 0f, 1f, dur, t =>
            {
                if (show == null) return;
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
                cg.alpha = t;
            }, null, Ease.OutCubic);
        }

        private GameObject Pane(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(window.contentRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            go.SetActive(false);
            return go;
        }

        // ── Create pane ──────────────────────────────────────────

        private void BuildCreatePane()
        {
            var theme = BUI.BenchTheme.Current;
            _nameField = BuildNameField(_createPane.transform, new Vector2(0f, -34f));

            var cards = new GameObject("Templates");
            cards.transform.SetParent(_createPane.transform, false);
            var cardsRt = cards.AddComponent<RectTransform>();
            cardsRt.localScale = Vector3.one;
            cardsRt.anchorMin = new Vector2(0f, 0.5f);
            cardsRt.anchorMax = new Vector2(1f, 0.5f);
            cardsRt.sizeDelta = new Vector2(0f, 150f);
            cardsRt.anchoredPosition = new Vector2(0f, -10f);
            var layout = cards.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            string[] ids =
            {
                Projects.ProjectTemplates.Blank,
                Projects.ProjectTemplates.PiStarter,
                Projects.ProjectTemplates.JetsonStarter,
            };
            foreach (var id in ids)
            {
                string captured = id;
                var card = BUI.BenchControls.TemplateCard(cards.transform,
                    Projects.ProjectTemplates.DisplayName(id),
                    id == Projects.ProjectTemplates.Blank ? "Empty workspace" :
                    id == Projects.ProjectTemplates.PiStarter ? "Pi 4B + basics" :
                    "Jetson Nano + basics",
                    new Vector2(0f, 140f), () => PickTemplate(captured));
                _cards.Add(card);
            }
            var create = BUI.BenchControls.PillButton(_createPane.transform,
                "Create", true, OnCreate, 220f);
            var createRt = (RectTransform)create.transform;
            createRt.anchorMin = createRt.anchorMax = new Vector2(1f, 0f);
            createRt.anchoredPosition = new Vector2(-120f, 52f);
            _ = theme;
        }

        private TMP_InputField BuildNameField(Transform parent, Vector2 anchored)
        {
            var theme = BUI.BenchTheme.Current;
            var go = new GameObject("NameField");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(0f, 56f);
            rt.anchoredPosition = anchored;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.10f);
            var field = go.AddComponent<TMP_InputField>();
            field.textComponent = MakeFieldText(go.transform, theme, false);
            field.placeholder = MakeFieldText(go.transform, theme, true);
            ((TMP_Text)field.placeholder).text = "Project name";
            field.characterLimit = Projects.ProjectSchema.MaxNameLength;
            field.onValueChanged.AddListener(_ => SoundBankTick());
            return field;
        }

        private TMP_Text MakeFieldText(Transform parent, UITheme theme, bool placeholder)
        {
            var go = new GameObject(placeholder ? "Placeholder" : "Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 6f);
            rt.offsetMax = new Vector2(-20f, -6f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = theme.interRegular;
            tmp.fontSize = BUI.BenchTheme.Scaled(theme.bodySize);
            tmp.color = placeholder ? theme.textSecondary : theme.textPrimary;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void SoundBankTick() { }

        private void PickTemplate(string id)
        {
            _template = id;
            for (int i = 0; i < _cards.Count; i++)
                BUI.BenchControls.SetCardSelected(_cards[i],
                    Projects.ProjectTemplates.All[i] == id);
        }

        private void OnCreate()
        {
            var p = Mgr.Create(_nameField.text, _template);
            if (p == null)
            {
                window.ErrorShake();
                Debug.LogWarning("[Bench] project name invalid (1-40 chars).");
                return;
            }
            BUI.SoundBank.Play(UI.BenchSound.PowerOn);
            Core.AppStateMachine.Instance?.TransitionTo(AppState.ProjectPanel);
        }

        private void SuggestName()
        {
            if (_nameField == null || !string.IsNullOrEmpty(_nameField.text)) return;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Mgr.Recents()) taken.Add(r.name);
            int i = 1;
            while (taken.Contains("Untitled " + i)) i++;
            _nameField.text = "Untitled " + i;
            PickTemplate(_template);
        }

        // ── Open pane ────────────────────────────────────────────

        private void BuildOpenPane()
        {
            _openContent = BUI.BenchControls.ScrollList(_openPane.transform);
            _emptyState = new GameObject("EmptyState");
            _emptyState.transform.SetParent(_openPane.transform, false);
            var eRt = _emptyState.AddComponent<RectTransform>();
            eRt.localScale = Vector3.one;
            eRt.anchorMin = Vector2.zero;
            eRt.anchorMax = Vector2.one;
            eRt.sizeDelta = Vector2.zero;
            var theme = BUI.BenchTheme.Current;
            var msg = BUI.BenchControls.AddLabel(_emptyState,
                "No projects yet.\nYour bench is waiting for its first build.",
                theme.textSecondary, theme.interRegular, theme.bodySize,
                TextAlignmentOptions.Center);
            ((RectTransform)msg.transform).offsetMin = new Vector2(0f, 60f);
            var btn = BUI.BenchControls.PillButton(_emptyState.transform,
                "Create your first project", true,
                () => SelectTab(Tab.Create), 380f);
            var bRt = (RectTransform)btn.transform;
            bRt.anchorMin = bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = new Vector2(0f, -60f);
        }

        private void RebuildRecents()
        {
            var theme = BUI.BenchTheme.Current;
            foreach (Transform c in _openContent) Destroy(c.gameObject);
            var recents = Mgr.Recents();
            _emptyState.SetActive(recents.Count == 0);
            foreach (var r in recents)
            {
                var entry = r;
                var row = BUI.BenchControls.ProjectRow(_openContent,
                    onOpen: () =>
                    {
                        if (Mgr.Open(entry.id) == null)
                        {
                            window.ErrorShake();
                            return;
                        }
                        BUI.SoundBank.Play(UI.BenchSound.PowerOn);
                        Core.AppStateMachine.Instance?.TransitionTo(
                            AppState.ProjectPanel);
                    },
                    onOverflow: () => ShowOverflow(entry.id));
                row.title.text = entry.name;
                row.subtitle.text = ShortBoard(entry.boardType) + "  ·  " +
                    RelativeTime(entry.modifiedAtTicks) + "  ·  " +
                    entry.partCount + (entry.partCount == 1 ? " part" : " parts");
                var dot = row.root.transform.Find("Dot")?.GetComponent<Image>();
                if (dot != null)
                    dot.color = entry.boardType == "JetsonNano"
                        ? new Color(0.45f, 0.85f, 0.45f, 1f) : theme.accent;
            }
            _ = theme;
        }

        private void ShowOverflow(string id)
        {
            var layer = new GameObject("OverflowMenu");
            layer.transform.SetParent(window.contentRoot, false);
            var lRt = layer.AddComponent<RectTransform>();
            lRt.localScale = Vector3.one;
            lRt.anchorMin = Vector2.zero;
            lRt.anchorMax = Vector2.one;
            lRt.sizeDelta = Vector2.zero;
            var dim = layer.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.35f);
            dim.raycastTarget = true;
            var dimBtn = layer.AddComponent<Button>();
            dimBtn.onClick.AddListener(() => Destroy(layer));

            var box = new GameObject("Box");
            box.transform.SetParent(layer.transform, false);
            var bRt = box.AddComponent<RectTransform>();
            bRt.localScale = Vector3.one;
            bRt.anchorMin = bRt.anchorMax = new Vector2(1f, 0.5f);
            bRt.sizeDelta = new Vector2(280f, 210f);
            bRt.anchoredPosition = new Vector2(-170f, 0f);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0x14 / 255f, 0x18 / 255f, 0x20 / 255f, 0.98f);
            boxImg.raycastTarget = true;
            AddMenuButton(box.transform, "Rename", 56f, () =>
            {
                Destroy(layer);
                ShowRename(id);
            });
            AddMenuButton(box.transform, "Duplicate", 0f, () =>
            {
                Destroy(layer);
                Mgr.Duplicate(id);
                RebuildRecents();
            });
            AddMenuButton(box.transform, "Delete", -56f, () =>
            {
                Destroy(layer);
                var p = Mgr.Open(id);
                string name = p != null ? p.name : "this project";
                BUI.BenchControls.Confirm(window.contentRoot,
                    "Delete \"" + name + "\"?", "Delete",
                    () => { Mgr.Delete(id); RebuildRecents(); });
            });
        }

        private void AddMenuButton(Transform parent, string label, float y, Action onClick)
        {
            var b = BUI.BenchControls.PillButton(parent, label, false, () => onClick(), 240f);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        private void ShowRename(string id)
        {
            var layer = new GameObject("RenameDialog");
            layer.transform.SetParent(window.contentRoot, false);
            var lRt = layer.AddComponent<RectTransform>();
            lRt.localScale = Vector3.one;
            lRt.anchorMin = Vector2.zero;
            lRt.anchorMax = Vector2.one;
            lRt.sizeDelta = Vector2.zero;
            var dim = layer.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            var box = new GameObject("Box");
            box.transform.SetParent(layer.transform, false);
            var bRt = box.AddComponent<RectTransform>();
            bRt.localScale = Vector3.one;
            bRt.anchorMin = bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.sizeDelta = new Vector2(440f, 230f);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0x0F / 255f, 0x12 / 255f, 0x18 / 255f, 0.98f);
            var theme = BUI.BenchTheme.Current;
            BUI.BenchControls.AddLabel(box, "Rename project", theme.textPrimary,
                theme.interSemiBold, theme.headingSize, TextAlignmentOptions.Center)
                .GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 66f);

            var field = BuildNameField(box.transform, new Vector2(0f, -6f));
            var cur = Mgr.Open(id);
            field.text = cur != null ? cur.name : "";
            var save = BUI.BenchControls.PillButton(box.transform, "Save", true, () =>
            {
                if (Mgr.Rename(id, field.text))
                {
                    Destroy(layer);
                    RebuildRecents();
                }
                else window.ErrorShake();
            }, 150f);
            ((RectTransform)save.transform).anchorMin =
                ((RectTransform)save.transform).anchorMax = new Vector2(1f, 0f);
            ((RectTransform)save.transform).anchoredPosition = new Vector2(-95f, 42f);
            var cancel = BUI.BenchControls.PillButton(box.transform, "Cancel", false,
                () => Destroy(layer), 150f);
            ((RectTransform)cancel.transform).anchorMin =
                ((RectTransform)cancel.transform).anchorMax = new Vector2(0f, 0f);
            ((RectTransform)cancel.transform).anchoredPosition = new Vector2(95f, 42f);
        }

        private static string ShortBoard(string boardType) =>
            boardType == "JetsonNano" ? "Jetson" : "Pi";

        internal static string RelativeTime(long ticks)
        {
            var span = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + "m ago";
            if (span.TotalHours < 24) return (int)span.TotalHours + "h ago";
            if (span.TotalDays < 7) return (int)span.TotalDays + "d ago";
            return new DateTime(ticks, DateTimeKind.Utc).ToString("MMM d");
        }

        // ── Settings gear ────────────────────────────────────────

        private void BuildGear()
        {
            var anchor = window.transform.Find("PinAnchor");
            var theme = BUI.BenchTheme.Current;
            var gear = new GameObject("SettingsGear");
            gear.transform.SetParent(anchor != null ? anchor : window.transform, false);
            var rt = gear.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(56f, 56f);
            rt.anchoredPosition = Vector2.zero;
            var img = gear.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);
            var btn = gear.AddComponent<Button>();
            btn.targetGraphic = img;
            BUI.BenchControls.AddLabel(gear, "⚙", theme.textSecondary,
                theme.interRegular, 30f, TextAlignmentOptions.Center);
            btn.onClick.AddListener(() =>
            {
                BUI.SoundBank.Play(UI.BenchSound.Press);
                ToggleSettings();
            });
        }

        private void ToggleSettings()
        {
            if (_settingsPanel != null)
            {
                Destroy(_settingsPanel);
                _settingsPanel = null;
                return;
            }
            var theme = BUI.BenchTheme.Current;
            var go = new GameObject("SettingsPanel");
            go.transform.SetParent(window.contentRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-360f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0x0F / 255f, 0x12 / 255f, 0x18 / 255f, 0.96f);
            bg.raycastTarget = true;
            var title = BUI.BenchControls.AddLabel(go, "Settings", theme.textPrimary,
                theme.interSemiBold, theme.headingSize, TextAlignmentOptions.Left);
            var titleRt = (RectTransform)title.transform;
            titleRt.offsetMin = new Vector2(24f, -64f);
            titleRt.offsetMax = new Vector2(-24f, -16f);

            var list = new GameObject("List");
            list.transform.SetParent(go.transform, false);
            var listRt = list.AddComponent<RectTransform>();
            listRt.localScale = Vector3.one;
            listRt.anchorMin = new Vector2(0f, 1f);
            listRt.anchorMax = new Vector2(1f, 1f);
            listRt.offsetMin = new Vector2(24f, -540f);
            listRt.offsetMax = new Vector2(-24f, -80f);
            var layout = list.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;

            BUI.BenchControls.Toggle(list.transform, "Reduce transparency",
                Core.BenchSettings.ReduceTransparency,
                v => Core.BenchSettings.ReduceTransparency = v);
            BUI.BenchControls.Toggle(list.transform, "Reduce motion",
                Core.BenchSettings.ReduceMotion,
                v => Core.BenchSettings.ReduceMotion = v);
            int sizeIdx = Core.BenchSettings.TextScale >= 1.5f ? 2 :
                Core.BenchSettings.TextScale >= 1.25f ? 1 : 0;
            BUI.BenchControls.SegmentedRow(list.transform, "Text size",
                new[] { "100%", "125%", "150%" }, sizeIdx,
                i => Core.BenchSettings.TextScale = i == 2 ? 1.5f : i == 1 ? 1.25f : 1f);
            int handIdx = Core.BenchSettings.LeftHanded ? 0 : 1;
            BUI.BenchControls.SegmentedRow(list.transform, "Hand",
                new[] { "Left", "Right" }, handIdx,
                i => Core.BenchSettings.LeftHanded = i == 0);
            BUI.BenchControls.Toggle(list.transform, "Audio",
                !Core.BenchSettings.AudioMuted,
                v => Core.BenchSettings.AudioMuted = !v);
            BUI.BenchControls.Toggle(list.transform, "Demo mode",
                Core.BenchSettings.DemoMode,
                v => Core.BenchSettings.DemoMode = v);
            var brightLabel = BUI.BenchControls.AddLabel(list, "Brightness",
                theme.textPrimary, theme.interRegular, theme.bodySize,
                TextAlignmentOptions.Left);
            ((RectTransform)brightLabel.transform).sizeDelta = new Vector2(0f, 32f);
            var sliderGo = new GameObject("Brightness");
            sliderGo.transform.SetParent(list.transform, false);
            var sRt = sliderGo.AddComponent<RectTransform>();
            sRt.localScale = Vector3.one;
            sRt.sizeDelta = new Vector2(0f, 48f);
            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = 0.3f;
            slider.maxValue = 1f;
            slider.value = Core.BenchSettings.Brightness;
            slider.onValueChanged.AddListener(v => Core.BenchSettings.Brightness = v);
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.localScale = Vector3.one;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.15f);
            bgImg.raycastTarget = false;
            slider.targetGraphic = bgImg;
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(sliderGo.transform, false);
            var fImg = fillGo.AddComponent<Image>();
            fImg.color = theme.accent;
            fImg.raycastTarget = false;
            slider.fillRect = (RectTransform)fillGo.transform;
            slider.fillRect.anchorMin = Vector2.zero;
            slider.fillRect.anchorMax = new Vector2(0.5f, 1f);
            slider.fillRect.sizeDelta = Vector2.zero;
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(sliderGo.transform, false);
            var hImg = handleGo.AddComponent<Image>();
            hImg.color = Color.white;
            slider.handleRect = (RectTransform)handleGo.transform;
            slider.handleRect.sizeDelta = new Vector2(32f, 32f);

            var close = BUI.BenchControls.PillButton(go.transform, "Done", true,
                () => ToggleSettings(), 150f);
            var cRt = (RectTransform)close.transform;
            cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0f);
            cRt.anchoredPosition = new Vector2(0f, 52f);
            _settingsPanel = go;
        }
    }
}
