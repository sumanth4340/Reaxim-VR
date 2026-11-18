// ConceptHomeWiring.cs — Task 4 behavior for the concept Home
// screen: tab switching, template select, validated Create,
// recents with overflow (rename/duplicate/delete), empty state,
// settings gear. Talks to ProjectManager + AppStateMachine only.
// AddComponent order follows the safe pattern (renderable first,
// TMP on a fresh child, TMP_InputField before Image).
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Bench.Core;
using Bench.Projects;
using Bench.UI;

namespace Bench.Panels
{
    public class ConceptHomeWiring : MonoBehaviour
    {
        [Tooltip("Baked Arial SDF (assigned by the builder).")]
        public TMP_FontAsset arial;
        public TMP_FontAsset arialBold;

        private Transform _screen;
        private Transform _panel;
        private GameObject _tabCreate;
        private GameObject _tabOpen;
        private GameObject _createPane;
        private GameObject _openPane;
        private TMP_InputField _nameField;
        private readonly List<GameObject> _templateCards = new List<GameObject>();
        private GameObject _createBtn;
        private Transform _recentsContent;
        private GameObject _emptyState;
        private GameObject _popup;
        private int _templateIndex;
        private bool _openTab;

        private static readonly string[] Templates =
            { ProjectTemplates.Blank, ProjectTemplates.PiStarter,
              ProjectTemplates.JetsonStarter };

        private void Awake()
        {
            if (arial != null) ConceptUI.Arial = arial;
            if (arialBold != null) ConceptUI.ArialBold = arialBold;
        }

        private void Start()
        {
            var mgr = ProjectManager.Instance;
            var machine = AppStateMachine.Instance;
            if (mgr == null || machine == null)
            {
                Debug.LogError("[Bench] Task4 wiring: manager/machine missing.");
                return;
            }
            var root = transform;
            var st = root.Find("Screen_Home");
            if (st == null) return;
            _screen = st;
            _panel = st.Find("Panel");
            if (_panel == null) return;

            _tabCreate = _panel.Find("Tab_Create")?.gameObject;
            _tabOpen = _panel.Find("Tab_Open")?.gameObject;
            var cp = _panel.Find("CreatePane");
            _createPane = cp?.gameObject;
            var op = _panel.Find("OpenPane");
            _openPane = op?.gameObject;
            if (_tabCreate != null)
                Btn(_tabCreate, () => SelectTab(false));
            if (_tabOpen != null)
                Btn(_tabOpen, () => SelectTab(true));

            if (cp != null)
            {
                var nf = cp.Find("NameField");
                if (nf != null) _nameField = nf.GetComponent<TMP_InputField>();
                _templateCards.Clear();
                for (int i = 0; i < 3; i++)
                {
                    var card = cp.Find("TemplateCard_" + i)?.gameObject;
                    if (card == null) continue;
                    _templateCards.Add(card);
                    int idx = _templateCards.Count - 1;
                    Btn(card, () => SelectTemplate(idx));
                }
                var cb = cp.Find("CreateButton")?.gameObject;
                _createBtn = cb;
                if (cb != null) Btn(cb, CreateProject);
            }
            if (op != null)
            {
                var content = op.Find("Recents/Viewport/Content");
                _recentsContent = content;
                var empty = op.Find("EmptyState");
                _emptyState = empty?.gameObject;
                var eb = op.Find("EmptyState/EmptyCreateButton")?.gameObject;
                if (eb != null) Btn(eb, () => SelectTab(false));
            }
            var gear = _panel.Find("GearButton")?.gameObject;
            if (gear != null) Btn(gear, ShowSettings);

            mgr.ProjectsChanged += RefreshRecents;
            machine.StateChanged += OnState;
            _openTab = mgr.Recents().Count > 0;
            SelectTemplate(0, silent: true);
            ApplyTab();
            RefreshRecents();
        }

        private void OnDestroy()
        {
            var mgr = ProjectManager.Instance;
            if (mgr != null) mgr.ProjectsChanged -= RefreshRecents;
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged -= OnState;
        }

        private static void Btn(GameObject go, Action onClick)
        {
            var b = go.GetComponent<Button>();
            if (b == null) b = go.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick?.Invoke();
            });
        }

        // ── Tabs + templates ──────────────────────────────────────

        private void SelectTab(bool open)
        {
            _openTab = open;
            ApplyTab();
        }

        private void ApplyTab()
        {
            StyleTab(_tabCreate, !_openTab);
            StyleTab(_tabOpen, _openTab);
            if (_createPane != null) _createPane.SetActive(!_openTab);
            if (_openPane != null) _openPane.SetActive(_openTab);
        }

        private static void StyleTab(GameObject tab, bool selected)
        {
            if (tab == null) return;
            var gp = tab.GetComponent<GlassPanel>();
            var img = tab.GetComponent<Image>();
            var txt = tab.GetComponentInChildren<TMP_Text>();
            if (img != null)
                img.color = selected ? ConceptUI.SelFillStrong
                    : new Color(1f, 1f, 1f, 0.02f);
            if (gp != null)
            {
                gp.fillOverride = selected ? ConceptUI.SelFillStrong
                    : new Color(1f, 1f, 1f, 0.02f);
                gp.rimOverride = selected ? ConceptUI.Cyan : ConceptUI.TabBorder;
                gp.ApplyTheme();
            }
            if (txt != null)
                txt.color = selected ? ConceptUI.CyanBright : Color.white;
        }

        private void SelectTemplate(int idx, bool silent = false)
        {
            _templateIndex = Mathf.Clamp(idx, 0, _templateCards.Count - 1);
            for (int i = 0; i < _templateCards.Count; i++)
                StyleCard(_templateCards[i], i == _templateIndex);
            if (!silent) SoundBank.Play(BenchSound.Press);
        }

        private static void StyleCard(GameObject card, bool selected)
        {
            var gp = card.GetComponent<GlassPanel>();
            if (gp == null) return;
            gp.fillOverride = selected ? ConceptUI.SelFill : ConceptUI.CardFill;
            gp.rimOverride = selected ? ConceptUI.Cyan : ConceptUI.CardBorder;
            gp.ApplyTheme();
        }

        // ── Create / open ─────────────────────────────────────────

        private void CreateProject()
        {
            var mgr = ProjectManager.Instance;
            var machine = AppStateMachine.Instance;
            string name = _nameField != null ? _nameField.text : "";
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled 1";
            string template = Templates[Mathf.Clamp(_templateIndex, 0, 2)];
            var p = mgr.Create(name, template);
            if (p == null)
            {
                SoundBank.Play(BenchSound.Error);
                Debug.LogWarning("[Bench] Create failed (invalid name).");
                return;
            }
            SoundBank.Play(BenchSound.Snap);
            if (_nameField != null) _nameField.text = "";
            machine.TransitionTo(AppState.ProjectPanel);
        }

        private void OpenProject(string id)
        {
            var mgr = ProjectManager.Instance;
            var machine = AppStateMachine.Instance;
            if (mgr.Open(id) == null)
            {
                SoundBank.Play(BenchSound.Error);
                return;
            }
            SoundBank.Play(BenchSound.Snap);
            machine.TransitionTo(AppState.Workspace);
        }

        // ── Recents ───────────────────────────────────────────────

        private void RefreshRecents()
        {
            if (_recentsContent == null) return;
            var mgr = ProjectManager.Instance;
            if (mgr == null) return;
            for (int i = _recentsContent.childCount - 1; i >= 0; i--)
                Destroy(_recentsContent.GetChild(i).gameObject);
            var recents = mgr.Recents();
            if (_emptyState != null) _emptyState.SetActive(recents.Count == 0);
            int i2 = 0;
            foreach (var e in recents)
            {
                BuildRow(e, i2 * 86f);
                i2++;
            }
            var crt = (RectTransform)_recentsContent;
            crt.sizeDelta = new Vector2(crt.sizeDelta.x,
                Mathf.Max(300f, recents.Count * 86f));
        }

        private void BuildRow(ProjectIndexEntry e, float y)
        {
            float w = 608f;
            var go = new GameObject("Row_" + e.id);
            go.transform.SetParent(_recentsContent, false);
            ConceptUI.Place(go, 0f, y, w, 78f);
            go.AddComponent<Image>().raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 20f;
            gp.fillOverride = ConceptUI.RowFill;
            gp.rimOverride = ConceptUI.RowBorder;
            gp.ApplyTheme();
            string sub = e.boardType + " · " + e.partCount
                + (e.partCount == 1 ? " part" : " parts");
            var t = ConceptUI.Text(go, e.name, 21f, Color.white, true,
                TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(0f, 1f);
            trt.offsetMin = new Vector2(22f, -8f); trt.offsetMax = new Vector2(w - 250f, -42f);
            var s = ConceptUI.Text(go, sub, 16f, ConceptUI.Sub, false,
                TextAlignmentOptions.Left);
            var srt = (RectTransform)s.transform;
            srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(0f, 1f);
            srt.offsetMin = new Vector2(22f, -40f); srt.offsetMax = new Vector2(w - 250f, -70f);
            var tm = ConceptUI.Text(go, RelativeTime(e.modifiedAtTicks), 16f,
                ConceptUI.Sub, false, TextAlignmentOptions.Right);
            var trt2 = (RectTransform)tm.transform;
            trt2.anchorMin = new Vector2(1f, 1f); trt2.anchorMax = new Vector2(1f, 1f);
            trt2.offsetMin = new Vector2(-228f, -8f); trt2.offsetMax = new Vector2(-70f, -70f);
            string id = e.id;
            var b = go.AddComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                OpenProject(id);
            });
            ConceptWidgets.Chip(go.transform, w - 60f, 19f, 52f, "···",
                false, () => ShowOverflow(id, e.name));
        }

        private static string RelativeTime(long ticks)
        {
            var dt = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (dt.TotalMinutes < 1) return "just now";
            if (dt.TotalHours < 1) return (int)dt.TotalMinutes + "m ago";
            if (dt.TotalDays < 1) return (int)dt.TotalHours + "h ago";
            if (dt.TotalDays < 30) return (int)dt.TotalDays + "d ago";
            return (int)(dt.TotalDays / 30) + "mo ago";
        }

        // ── Popups: overflow / rename / delete-confirm / settings ─

        private Transform ShowPopup(string title, float h)
        {
            ClosePopup();
            var dim = new GameObject("PopupDim");
            dim.transform.SetParent(_screen, false);
            ConceptUI.Place(dim, 0f, 0f, 1280f, 720f);
            var dimg = dim.AddComponent<Image>();
            dimg.color = new Color(0f, 0f, 0f, 0.45f);
            dimg.raycastTarget = true;
            var dbtn = dim.AddComponent<Button>();
            dbtn.targetGraphic = dimg;
            dbtn.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                ClosePopup();
            });
            _popup = dim;
            var card = new GameObject("PopupCard");
            card.transform.SetParent(dim.transform, false);
            ConceptUI.Place(card, 420f, (720f - h) / 2f, 440f, h);
            var cimg = card.AddComponent<Image>();
            cimg.raycastTarget = true;
            var gp = card.AddComponent<GlassPanel>();
            gp.light = false; gp.dark = true;
            gp.radiusOverride = 24f;
            gp.ApplyTheme();
            ConceptUI.Text(card, title, 21f, Color.white, true,
                TextAlignmentOptions.Left);
            var titleTxt = card.GetComponentInChildren<TMP_Text>();
            var trt = (RectTransform)titleTxt.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(24f, -20f); trt.offsetMax = new Vector2(-24f, -54f);
            var body = new GameObject("PopupBody");
            body.transform.SetParent(card.transform, false);
            ConceptUI.Place(body, 24f, 62f, 392f, h - 140f);
            return body.transform;
        }

        private void ClosePopup()
        {
            if (_popup != null) Destroy(_popup);
            _popup = null;
        }

        private void PopupButton(Transform parent, float y, string label,
            bool primary, Action onClick)
        {
            var b = ConceptWidgets.PillTab(parent, 0f, y, 392f, label,
                primary, null);
            var btn = b.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private void ShowOverflow(string id, string name)
        {
            var body = ShowPopup(name, 340f);
            PopupButton(body, 0f, "Rename", false, () => ShowRename(id, name));
            PopupButton(body, 52f, "Duplicate", false, () =>
            {
                if (ProjectManager.Instance.Duplicate(id) != null)
                    SoundBank.Play(BenchSound.Snap);
                else SoundBank.Play(BenchSound.Error);
                ClosePopup();
            });
            PopupButton(body, 104f, "Delete", false,
                () => ShowDeleteConfirm(id, name));
            PopupButton(body, 156f, "Cancel", true, ClosePopup);
        }

        private void ShowRename(string id, string oldName)
        {
            var body = ShowPopup("Rename project", 310f);
            var fieldGO = new GameObject("RenameField");
            fieldGO.transform.SetParent(body, false);
            ConceptUI.Place(fieldGO, 0f, 0f, 392f, 56f);
            var field = fieldGO.AddComponent<TMP_InputField>();
            var fimg = fieldGO.AddComponent<Image>();
            fimg.raycastTarget = true;
            var gp = fieldGO.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 22f;
            gp.fillOverride = new Color(1f, 1f, 1f, 0.10f);
            gp.rimOverride = new Color(1f, 1f, 1f, 0.40f);
            gp.ApplyTheme();
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(fieldGO.transform, false);
            ConceptUI.Place(textGO, 20f, 6f, 352f, 44f);
            var t = ConceptUI.Text(textGO, "", 21f, Color.white, false,
                TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            field.textComponent = (TextMeshProUGUI)t;
            field.text = oldName;
            field.characterLimit = 40;
            PopupButton(body, 68f, "Save", true, () =>
            {
                if (ProjectManager.Instance.Rename(id, field.text))
                    SoundBank.Play(BenchSound.Snap);
                else SoundBank.Play(BenchSound.Error);
                ClosePopup();
            });
            PopupButton(body, 120f, "Cancel", false, ClosePopup);
        }

        private void ShowDeleteConfirm(string id, string name)
        {
            var body = ShowPopup("Delete project", 300f);
            var t = ConceptUI.Text(body.gameObject,
                "Delete '" + name + "' forever?", 17f, ConceptUI.Sub,
                false, TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(0f, -6f); trt.offsetMax = new Vector2(0f, 44f);
            PopupButton(body, 56f, "Delete", false, () =>
            {
                if (ProjectManager.Instance.Delete(id))
                    SoundBank.Play(BenchSound.Snap);
                else SoundBank.Play(BenchSound.Error);
                ClosePopup();
            });
            PopupButton(body, 108f, "Cancel", true, ClosePopup);
        }

        private void ShowSettings()
        {
            var body = ShowPopup("Settings", 480f);
            float y = 0f;
            ToggleRow(body, ref y, "Reduce transparency",
                BenchSettings.ReduceTransparency,
                v => BenchSettings.ReduceTransparency = v);
            ToggleRow(body, ref y, "Reduce motion", BenchSettings.ReduceMotion,
                v => BenchSettings.ReduceMotion = v);
            ToggleRow(body, ref y, "Left-handed mode", BenchSettings.LeftHanded,
                v => BenchSettings.LeftHanded = v);
            ToggleRow(body, ref y, "Mute audio", BenchSettings.AudioMuted,
                v => BenchSettings.AudioMuted = v);
            ToggleRow(body, ref y, "Demo mode", BenchSettings.DemoMode,
                v => BenchSettings.DemoMode = v);
            string[] scales = { "Text 100%", "Text 125%", "Text 150%" };
            float cur = BenchSettings.TextScale;
            int idx = cur >= 1.4f ? 2 : cur >= 1.15f ? 1 : 0;
            var chip = ConceptWidgets.Chip(body, 0f, y, 200f, scales[idx],
                false, null);
            var cbtn = chip.GetComponent<Button>();
            var ctxt = chip.GetComponentInChildren<TMP_Text>();
            cbtn.onClick.AddListener(() =>
            {
                float[] vals = { 1f, 1.25f, 1.5f };
                int next = (Array.IndexOf(vals, BenchSettings.TextScale) + 1) % 3;
                if (next < 0) next = 0;
                BenchSettings.TextScale = vals[next];
                if (ctxt != null) ctxt.text = scales[next];
                SoundBank.Play(BenchSound.Press);
            });
            y += 52f;
            PopupButton(body, y, "Close", true, ClosePopup);
        }

        private void ToggleRow(Transform parent, ref float y, string label,
            bool value, Action<bool> set)
        {
            var t = ConceptUI.Text(parent.gameObject, label, 17f, Color.white,
                false, TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(0f, y - 40f);
            trt.offsetMax = new Vector2(-90f, y);
            var sw = new GameObject("Switch_" + label);
            sw.transform.SetParent(parent, false);
            ConceptUI.Place(sw, 392f - 68f, y, 68f, 36f);
            var simg = sw.AddComponent<Image>();
            simg.raycastTarget = true;
            var gp = sw.AddComponent<GlassPanel>();
            gp.light = true;
            gp.radiusOverride = 18f;
            gp.fillOverride = value ? ConceptUI.SelFillStrong
                : new Color(1f, 1f, 1f, 0.08f);
            gp.rimOverride = value ? ConceptUI.Cyan
                : new Color(1f, 1f, 1f, 0.25f);
            gp.ApplyTheme();
            var knob = new GameObject("Knob");
            knob.transform.SetParent(sw.transform, false);
            ConceptUI.Place(knob, value ? 36f : 4f, 4f, 28f, 28f);
            var nimg = knob.AddComponent<Image>();
            nimg.color = Color.white;
            nimg.raycastTarget = false;
            var ngp = knob.AddComponent<GlassPanel>();
            ngp.light = true;
            ngp.radiusOverride = 14f;
            ngp.fillOverride = new Color(1f, 1f, 1f, 0.95f);
            ngp.rimOverride = new Color(1f, 1f, 1f, 0.2f);
            ngp.ApplyTheme();
            var sbtn = sw.AddComponent<Button>();
            sbtn.targetGraphic = simg;
            sbtn.onClick.AddListener(() =>
            {
                bool nv = !GetToggle(gp);
                set(nv);
                SoundBank.Play(BenchSound.Press);
                ShowSettings();
            });
            y += 48f;
        }

        private static bool GetToggle(GlassPanel gp) =>
            gp.rimOverride == ConceptUI.Cyan;

        // ── State ─────────────────────────────────────────────────

        private void OnState(AppState from, AppState to)
        {
            if (_screen == null) return;
            if (to == AppState.Home)
            {
                _screen.gameObject.SetActive(true);
                RefreshRecents();
            }
            else
            {
                ClosePopup();
                _screen.gameObject.SetActive(false);
            }
            // Other screens are owned by ConceptPanelWiring (Task 5).
        }
    }
}
