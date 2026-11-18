// DisplayWindow.cs — Task 9: the wide Display/SSH window (own
// world-space canvas, 688 x 608, RIGHT slot). Appears only while a
// board is powered (220 ms fade + 40 mm slide), hides on power off.
// Display mode shows the twin's RPi OS GUI frames (or the demo
// desktop); SSH mode is a serial terminal + virtual keyboard.
// Hand mapping: hover = cursor dot, tap = left click, hold-drag =
// drag, wheel/swipe = scroll, long-press = right click, zoom chip
// cycles 1x/1.5x/2x. Device finger specifics (middle-finger pinch
// etc.) ride the same pointer events via the Task-10 baked
// PointableCanvas; editor + harness use the mouse/test path.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Bench.Core;
using Bench.Data;
using Bench.UI;
using Bench.Windows;

namespace Bench.Panels
{
    public class DisplayWindow : MonoBehaviour
    {
        public static DisplayWindow Instance { get; private set; }

        public enum ViewMode { Display, Ssh }

        public const float PanelW = 688f;
        public const float PanelH = 608f;
        public const int DisplayMaxWidth = 960;

        public static readonly int[] StripBcm =
            { 17, 18, 27, 22, 23, 24, 25, 4 };

        public bool IsShowing { get; private set; }
        public ViewMode CurrentView { get; private set; } =
            ViewMode.Display;
        public string BoardName => _boardName;
        public IDataSource ActiveSource => _active;
        public SimulatedSource SimSource => _sim;
        public LiveTwinSource LiveSource => _live;
        public float ZoomLevel => _zoomLevels[_zoomIdx];
        public Vector2 CursorUV => _cursorUV;
        public string TerminalText =>
            _termTmp != null ? _termTmp.text : "";
        public string LastFrameSource => _lastSource;

        private string _boardName = "";
        private bool _powered;
        private Workspace.PlacedPart _board;
        private bool _built;

        private GameObject _winRoot;
        private GlassWindow _glass;
        private CanvasGroup _cg;
        private Transform _canvasT;
        private GameObject _displayView;
        private GameObject _sshView;
        private RawImage _displayImg;
        private Texture2D _displayTex;
        private GameObject _cursorDot;
        private GameObject _waitingGO;
        private TMPro.TMP_Text _sourceTmp;
        private TMPro.TMP_Text _termTmp;
        private GameObject _keyboard;
        private Vector2 _kbShownPos;
        private Vector2 _kbHiddenPos;
        private readonly List<string> _termLines = new List<string>();
        private string _inputLine = "";
        private int _caret;
        private int _scrollOff;
        private readonly List<string> _history = new List<string>();
        private int _historyIdx = -1;
        private bool _shift;
        private bool _ctrlArmed;
        private int _zoomIdx;
        private readonly float[] _zoomLevels = { 1f, 1.5f, 2f };
        private Vector2 _cursorUV = new Vector2(0.5f, 0.5f);
        private string _lastSource = "";
        private float _srcPollT;
        private bool _wide = true;
        private Vector3 _nearPos;
        private int _moveToken;

        private SimulatedSource _sim;
        private LiveTwinSource _live;
        private IDataSource _active;

        private readonly List<Image> _stripDots = new List<Image>();
        private readonly List<TMPro.TMP_Text> _stripTxt =
            new List<TMPro.TMP_Text>();

        private static Texture2D _dotTex;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            BoardMenu.PowerChanged += OnPower;
            Workspace.PlacedPart.BoardDestroyed += OnBoardGone;
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged += OnState;
            SetTabVisible(false);
        }

        private void OnDestroy()
        {
            BoardMenu.PowerChanged -= OnPower;
            Workspace.PlacedPart.BoardDestroyed -= OnBoardGone;
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged -= OnState;
            DetachSource();
            if (Instance == this) Instance = null;
        }

        private void OnState(AppState from, AppState to) =>
            RefreshVisibility();

        private void OnPower(BoardMenu.PlacedPartShim shim, bool on)
        {
            _powered = on;
            if (shim?.Part != null)
            {
                _board = shim.Part;
                _boardName = shim.Part.displayName;
            }
            RefreshVisibility();
        }

        private void OnBoardGone(Workspace.PlacedPart gone)
        {
            if (gone == null || gone != _board) return;
            if (!gameObject.scene.isLoaded) return; // teardown, skip
            _board = null;
            _powered = false;
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            var machine = AppStateMachine.Instance;
            bool inWork = machine == null
                || machine.Current == AppState.Workspace;
            if (_powered && inWork) Show(_boardName);
            else Hide();
        }

        private void Update()
        {
            _srcPollT += Time.unscaledDeltaTime;
            if (_srcPollT >= 1f)
            {
                _srcPollT = 0f;
                if (IsShowing) SelectSource();
            }
            // Long-press advances on the clock (holding still sends
            // no pointer events to advance it).
            if (_pressing && !_rightFired && !_dragSent)
            {
                _pressT += Time.unscaledDeltaTime;
                if (_pressT >= 0.5f
                    && Vector2.Distance(_cursorUV, _pressUV) < 0.02f)
                {
                    _rightFired = true; // long-press = right click
                    SendButton(_cursorUV, 0x111, true);
                }
            }
        }

        // ── show / hide ──────────────────────────────────────

        public void Show(string boardName)
        {
            if (!string.IsNullOrEmpty(boardName))
                _boardName = boardName;
            EnsureBuilt();
            SelectSource();
            SetView(ViewMode.Display, instant: true);
            _winRoot.SetActive(true);
            IsShowing = true;
            var mgr = WindowLayoutManager.Instance;
            if (mgr != null)
                mgr.DockToSlot(_glass, SlotKind.Right, animate: false);
            else FallbackPose();
            _nearPos = _winRoot.transform.position;
            // Focus mode: appear parked wide-right (a deliberate
            // head turn); the recall tab brings it back near.
            _wide = true;
            Vector3 slot = WidePose();
            _moveToken++;
            int token = _moveToken;
            Vector3 from = slot + _winRoot.transform.right * 0.04f;
            _winRoot.transform.position = from;
            _winRoot.transform.localScale = Vector3.one * 0.96f;
            _cg.alpha = 0f;
            SetText("BoardTitle", _boardName);
            MiniTween.Float(this, 0f, 1f, 0.22f, t =>
            {
                if (_winRoot == null || token != _moveToken) return;
                _winRoot.transform.position =
                    Vector3.LerpUnclamped(from, slot, t);
                _winRoot.transform.localScale =
                    Vector3.one * Mathf.Lerp(0.96f, 1f, t);
                _cg.alpha = t;
            }, null, Ease.OutCubic);
            _active?.SetDisplayEnabled(true, DisplayMaxWidth);
            SetTabVisible(true);
            SoundBank.Play(BenchSound.Hover);
        }

        private void SetTabVisible(bool show)
        {
            // Driven here, not by the tab itself: an inactive tab's
            // Update never runs, so it could never re-show itself.
            foreach (var t in GameObject.FindObjectsOfType<Transform>(true))
            {
                if (t.name != "DisplayTab") continue;
                if (t.gameObject.activeSelf != show)
                    t.gameObject.SetActive(show);
            }
        }

        /// <summary>Recall tab: wide park or near slot.</summary>
        public void ToggleWide()
        {
            if (!IsShowing || _winRoot == null) return;
            _wide = !_wide;
            _moveToken++;
            int token = _moveToken;
            Vector3 from = _winRoot.transform.position;
            Vector3 to = _wide ? WidePose() : _nearPos;
            MiniTween.Float(this, 0f, 1f, 0.32f, t =>
            {
                if (_winRoot == null || token != _moveToken) return;
                _winRoot.transform.position =
                    Vector3.LerpUnclamped(from, to, t);
            }, null, Ease.OutCubic);
        }

        public bool IsWide() => _wide;

        private Vector3 WidePose()
        {
            var cam = Camera.main;
            Vector3 hp = cam != null ? cam.transform.position
                : new Vector3(0f, 1.5f, 0f);
            float yaw = (cam != null ? cam.transform.eulerAngles.y : 0f)
                + 52f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 p = hp + dir * 0.95f;
            p.y = hp.y - 0.22f;
            return p;
        }

        public void Hide()
        {
            if (!IsShowing && (_winRoot == null
                || !_winRoot.activeSelf))
                return;
            IsShowing = false;
            _active?.SetDisplayEnabled(false, DisplayMaxWidth);
            SetTabVisible(false);
            if (_winRoot == null) return;
            Vector3 slot = _winRoot.transform.position;
            Vector3 to = slot + _winRoot.transform.right * 0.04f;
            MiniTween.Float(this, 1f, 0f, 0.22f, t =>
            {
                if (_winRoot == null) return;
                _winRoot.transform.position =
                    Vector3.LerpUnclamped(to, slot, t);
                _cg.alpha = t;
            }, () =>
            {
                if (_winRoot != null) _winRoot.SetActive(false);
                var mgr = WindowLayoutManager.Instance;
                if (mgr != null && _glass != null)
                    mgr.Release(_glass);
            }, Ease.OutCubic);
        }

        private void FallbackPose()
        {
            var cam = Camera.main;
            Vector3 hp = cam != null ? cam.transform.position
                : new Vector3(0f, 1.5f, 0f);
            Vector3 fwd = cam != null ? cam.transform.forward
                : Vector3.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 p = hp + fwd * 0.9f;
            p.y = hp.y - 0.22f;
            _winRoot.transform.position = p;
            _winRoot.transform.rotation =
                Quaternion.LookRotation(hp - p, Vector3.up);
        }

        // ── construction (once, lazily on first Show) ──────────

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            // Adopt the editor-baked shell when present (canvas +
            // ISDK ray/poke); otherwise build bare (no hand input).
            var shell = transform.Find("DisplayWindow");
            if (shell != null && shell.gameObject != _winRoot)
            {
                _winRoot = shell.gameObject;
                _glass = _winRoot.GetComponent<GlassWindow>();
                if (_glass == null)
                    _glass = _winRoot.AddComponent<GlassWindow>();
                _cg = _winRoot.GetComponent<CanvasGroup>();
                if (_cg == null)
                    _cg = _winRoot.AddComponent<CanvasGroup>();
                _canvasT = _winRoot.transform.Find("DisplayCanvas");
            }
            else
            {
                _winRoot = new GameObject("DisplayWindow");
                _winRoot.transform.SetParent(transform, false);
                _glass = _winRoot.AddComponent<GlassWindow>();
                _cg = _winRoot.AddComponent<CanvasGroup>();
                var go = new GameObject("DisplayCanvas");
                go.transform.SetParent(_winRoot.transform, false);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                go.AddComponent<GraphicRaycaster>();
                var rt = go.GetComponent<RectTransform>();
                rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                rt.sizeDelta = new Vector2(PanelW, PanelH);
                rt.pivot = new Vector2(0.5f, 0.5f);
                _canvasT = go.transform;
            }
            _glass.backplate = null;

            // Backplate.
            var back = new GameObject("Backplate");
            back.transform.SetParent(_canvasT, false);
            ConceptUI.Place(back, 0f, 0f, PanelW, PanelH);
            var bimg = back.AddComponent<Image>();
            bimg.raycastTarget = false;
            var bgp = back.AddComponent<GlassPanel>();
            bgp.light = false;
            bgp.dark = true;
            bgp.radiusOverride = 28f;
            bgp.ApplyTheme();

            // Grab pill: pinch-drag repositions the window.
            var grab = new GameObject("GrabBar");
            grab.transform.SetParent(_canvasT, false);
            ConceptUI.Place(grab, (PanelW - 160f) / 2f, 10f, 160f, 12f);
            var gi = grab.AddComponent<Image>();
            gi.raycastTarget = false;
            gi.color = new Color(1f, 1f, 1f, 0.35f);
            var gbc = grab.AddComponent<BoxCollider>();
            gbc.size = new Vector3(160f, 30f, 30f); // canvas-local (0.001)
            grab.AddComponent<Bench.Hands.WindowBarHand>();

            // Header row.
            Txt(_canvasT, "BoardTitle", _boardName, 19f, true,
                20f, 30f, 200f, 36f, TextAnchor.Left);
            Chip(_canvasT, "ConnChip", 228f, 32f, 110f, 32f, "Demo",
                null);
            Chip(_canvasT, "SegDisplay", 360f, 30f, 150f, 40f,
                "Display", () => SetView(ViewMode.Display));
            Chip(_canvasT, "SegSSH", 518f, 30f, 150f, 40f,
                "SSH / Terminal", () => SetView(ViewMode.Ssh));

            BuildDisplayView();
            BuildSshView();
            BuildStrip();

            _sim = _winRoot.AddComponent<SimulatedSource>();
            _live = _winRoot.AddComponent<LiveTwinSource>();
            _winRoot.SetActive(false);
        }

        private void BuildDisplayView()
        {
            _displayView = new GameObject("DisplayView");
            _displayView.transform.SetParent(_canvasT, false);
            ConceptUI.Place(_displayView, 0f, 80f, PanelW, 420f);
            var area = new GameObject("DisplayArea");
            area.transform.SetParent(_displayView.transform, false);
            ConceptUI.Place(area, 24f, 0f, 640f, 400f);
            var abg = area.AddComponent<Image>();
            abg.color = new Color(0f, 0f, 0f, 1f);
            abg.raycastTarget = true;
            var img = new GameObject("DisplayImage");
            img.transform.SetParent(area.transform, false);
            ConceptUI.Place(img, 0f, 20f, 640f, 360f);
            _displayImg = img.AddComponent<RawImage>();
            _displayImg.raycastTarget = false;
            _displayTex = new Texture2D(2, 2, TextureFormat.RGB24,
                false);
            _displayImg.texture = _displayTex;
            var wait = new GameObject("WaitingLabel");
            wait.transform.SetParent(area.transform, false);
            ConceptUI.Place(wait, 120f, 184f, 400f, 32f);
            ConceptUI.Text(wait, "Waiting for display…",
                22f, new Color(1f, 1f, 1f, 0.7f), false,
                TMPro.TextAlignmentOptions.Center);
            _waitingGO = wait;
            var src = new GameObject("SourceTag");
            src.transform.SetParent(area.transform, false);
            ConceptUI.Place(src, 8f, 360f, 300f, 32f);
            _sourceTmp = ConceptUI.Text(src, "", 22f,
                new Color(1f, 1f, 1f, 0.75f), false,
                TMPro.TextAlignmentOptions.Left);
            var cur = new GameObject("CursorDot");
            cur.transform.SetParent(area.transform, false);
            ConceptUI.Place(cur, 312f, 192f, 16f, 16f);
            var ci = cur.AddComponent<Image>();
            ci.sprite = DotSprite();
            ci.color = Color.white;
            ci.raycastTarget = false;
            _cursorDot = cur;
            _cursorDot.SetActive(false);
            Chip(_displayView.transform, "ZoomChip", 592f, 360f, 72f,
                32f, "1x", CycleZoom);
            var zone = area.AddComponent<DisplayInputZone>();
            zone.window = this;
        }

        private void BuildSshView()
        {
            _sshView = new GameObject("SshView");
            _sshView.transform.SetParent(_canvasT, false);
            ConceptUI.Place(_sshView, 0f, 80f, PanelW, 496f);
            var term = new GameObject("TermArea");
            term.transform.SetParent(_sshView.transform, false);
            ConceptUI.Place(term, 24f, 0f, 640f, 160f);
            var tbg = term.AddComponent<Image>();
            tbg.color = new Color(0f, 0f, 0f, 1f);
            tbg.raycastTarget = true;
            var tt = new GameObject("TermText");
            tt.transform.SetParent(term.transform, false);
            ConceptUI.Place(tt, 8f, 6f, 624f, 148f);
            // 18 px, not the spec's 24: 24 shows only 6 lines in a
            // 160 px terminal; 18 keeps 8+ lines readable.
            _termTmp = ConceptUI.Text(tt, "", 18f, Color.white, false,
                TMPro.TextAlignmentOptions.TopLeft);
            var scroll = term.AddComponent<TermScrollZone>();
            scroll.window = this;
            string[] quick = { "ls", "python3", "ifconfig", "restart",
                "clear" };
            for (int i = 0; i < quick.Length; i++)
            {
                string q = quick[i];
                Chip(_sshView.transform, "Quick_" + q, 24f + i * 130f,
                    168f, 122f, 40f, q, () => RunQuick(q));
            }
            BuildKeyboard();
            _sshView.SetActive(false);
        }

        private void BuildKeyboard()
        {
            _keyboard = new GameObject("Keyboard");
            _keyboard.transform.SetParent(_sshView.transform, false);
            ConceptUI.Place(_keyboard, 24f, 216f, 640f, 280f);
            string[][] rows =
            {
                new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9",
                    "0" },
                new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O",
                    "P" },
                new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L",
                    "Bksp" },
                new[] { "Shift", "Z", "X", "C", "V", "B", "N", "M",
                    ".", "^" },
                new[] { "Tab", "Ctrl", "Space", "/", "-", ":",
                    "<", "v", ">", "Enter" },
            };
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string k = rows[r][c];
                    var b = Chip(_keyboard.transform, "Key_" + KeyId(k),
                        c * 64f, r * 56f, 56f, 48f, k,
                        () => PressKey(k));
                    b.GetComponentInChildren<TMPro.TMP_Text>(true)
                        .fontSize = k.Length > 3 ? 15f : 20f;
                }
            var rt = (RectTransform)_keyboard.transform;
            _kbShownPos = rt.anchoredPosition;
            _kbHiddenPos = _kbShownPos + new Vector2(0f, -300f);
        }

        private static string KeyId(string k)
        {
            switch (k)
            {
                case "^": return "Up";
                case "v": return "Down";
                case "<": return "Left";
                case ">": return "Right";
                case ".": return "Dot";
                case "/": return "Slash";
                case "-": return "Dash";
                case ":": return "Colon";
                default: return k;
            }
        }

        private GameObject _stripGO;

        private void BuildStrip()
        {
            var strip = new GameObject("GpioStrip");
            _stripGO = strip;
            strip.transform.SetParent(_canvasT, false);
            ConceptUI.Place(strip, 24f, 548f, 640f, 44f);
            for (int i = 0; i < StripBcm.Length; i++)
            {
                var cell = new GameObject("GpioCell" + i);
                cell.transform.SetParent(strip.transform, false);
                ConceptUI.Place(cell, i * 80f, 0f, 80f, 44f);
                var dot = new GameObject("Dot");
                dot.transform.SetParent(cell.transform, false);
                ConceptUI.Place(dot, 4f, 12f, 20f, 20f);
                var di = dot.AddComponent<Image>();
                di.sprite = DotSprite();
                di.color = new Color(1f, 1f, 1f, 0.25f);
                di.raycastTarget = false;
                _stripDots.Add(di);
                var tx = new GameObject("Tx");
                tx.transform.SetParent(cell.transform, false);
                ConceptUI.Place(tx, 28f, 6f, 52f, 32f);
                _stripTxt.Add(ConceptUI.Text(tx,
                    StripBcm[i] + " ·", 22f, Color.white, false,
                    TMPro.TextAlignmentOptions.Left));
            }
        }

        // ── small builders ─────────────────────────────────────

        private enum TextAnchor { Left, Center }

        private TMPro.TMP_Text Txt(Transform parent, string name,
            string text, float size, bool bold, float x, float y,
            float w, float h, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, w, h);
            return ConceptUI.Text(go, text, size, Color.white, bold,
                anchor == TextAnchor.Left
                    ? TMPro.TextAlignmentOptions.Left
                    : TMPro.TextAlignmentOptions.Center);
        }

        private Button Chip(Transform parent, string name, float x,
            float y, float w, float h, string text, Action act)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ConceptUI.Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = false;
            gp.dark = true;
            gp.radiusOverride = h * 0.5f;
            gp.fillOverride = new Color(1f, 1f, 1f, 0.10f);
            gp.rimOverride = new Color(1f, 1f, 1f, 0.2f);
            gp.ApplyTheme();
            ConceptUI.Text(go, text, 17f, Color.white, false,
                TMPro.TextAlignmentOptions.Center);
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                act?.Invoke();
            });
            return b;
        }

        private void SetText(string name, string text)
        {
            if (_canvasT == null) return;
            foreach (var t in _canvasT.GetComponentsInChildren<
                Transform>(true))
            {
                if (t.name != name) continue;
                var tmp = t.GetComponentInChildren<TMPro.TMP_Text>(
                    true);
                if (tmp != null) tmp.text = text;
                return;
            }
        }

        private void SetChipSelected(string name, bool selected)
        {
            if (_canvasT == null) return;
            foreach (var t in _canvasT.GetComponentsInChildren<
                Transform>(true))
            {
                if (t.name != name) continue;
                var gp = t.GetComponent<GlassPanel>();
                if (gp == null) return;
                gp.rimOverride = selected ? ConceptUI.Cyan
                    : new Color(1f, 1f, 1f, 0.2f);
                gp.ApplyTheme();
                return;
            }
        }

        private static Sprite DotSprite()
        {
            if (_dotTex == null)
            {
                const int px = 64;
                _dotTex = new Texture2D(px, px, TextureFormat.RGBA32,
                    false);
                var cols = new Color32[px * px];
                for (int y = 0; y < px; y++)
                    for (int x = 0; x < px; x++)
                    {
                        float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f)
                            + (y - 31.5f) * (y - 31.5f));
                        cols[y * px + x] = new Color32(255, 255, 255,
                            (byte)(Mathf.Clamp01((29f - d) / 2f)
                                * 255f));
                    }
                _dotTex.SetPixels32(cols);
                _dotTex.Apply(false, true);
            }
            var spr = Sprite.Create(_dotTex,
                new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f),
                64f);
            spr.name = "DotSprite9";
            return spr;
        }

        // ── views + sources ────────────────────────────────────

        public void SetView(ViewMode mode, bool instant = false)
        {
            if (!_built) return;
            CurrentView = mode;
            SetChipSelected("SegDisplay", mode == ViewMode.Display);
            SetChipSelected("SegSSH", mode == ViewMode.Ssh);
            var krt = (RectTransform)_keyboard.transform;
            if (mode == ViewMode.Ssh)
            {
                _displayView.SetActive(false);
                _sshView.SetActive(true);
                if (_stripGO != null) _stripGO.SetActive(false);
                RenderTerm();
                if (instant)
                {
                    krt.anchoredPosition = _kbShownPos;
                    return;
                }
                krt.anchoredPosition = _kbHiddenPos;
                SoundBank.Play(BenchSound.Hover);
                MiniTween.Float(this, 0f, 1f, 0.18f, t =>
                {
                    if (krt != null)
                        krt.anchoredPosition = Vector2.LerpUnclamped(
                            _kbHiddenPos, _kbShownPos, t);
                }, null, Ease.OutCubic);
            }
            else
            {
                _displayView.SetActive(true);
                if (_stripGO != null) _stripGO.SetActive(true);
                if (instant)
                {
                    _sshView.SetActive(false);
                    krt.anchoredPosition = _kbHiddenPos;
                    return;
                }
                MiniTween.Float(this, 0f, 1f, 0.18f, t =>
                {
                    if (krt != null)
                        krt.anchoredPosition = Vector2.LerpUnclamped(
                            _kbShownPos, _kbHiddenPos, t);
                }, () =>
                {
                    if (_sshView != null
                        && CurrentView == ViewMode.Display)
                        _sshView.SetActive(false);
                }, Ease.OutCubic);
            }
        }

        private void SelectSource()
        {
            if (!_built) return;
            IDataSource want = _sim;
            bool live = false;
            try
            {
                live = !Bench.Core.BenchSettings.DemoMode
                    && _live != null && _live.Connected;
            }
            catch { }
            if (live) want = _live;
            if (want == _active)
            {
                UpdateConnChip();
                return;
            }
            DetachSource();
            _active = want;
            _active.DisplayFrame += OnFrame;
            _active.Terminal += OnTerminal;
            _active.GpioLevels += OnGpio;
            _active.SetDisplayEnabled(true, DisplayMaxWidth);
            UpdateConnChip();
        }

        private void DetachSource()
        {
            if (_active == null) return;
            try { _active.SetDisplayEnabled(false, DisplayMaxWidth); }
            catch { }
            _active.DisplayFrame -= OnFrame;
            _active.Terminal -= OnTerminal;
            _active.GpioLevels -= OnGpio;
            _active = null;
        }

        private void UpdateConnChip()
        {
            string s = "Demo";
            bool liveAvail = false;
            try
            {
                liveAvail = !Bench.Core.BenchSettings.DemoMode
                    && _live != null && _live.Connected;
            }
            catch { }
            if (_active == _live && _live != null) s = "Live";
            else if (!_built) return;
            else if (!Bench.Core.BenchSettings.DemoMode && !liveAvail
                && HasLinkObject()) s = "Connecting";
            SetText("ConnChip", s);
            SetChipSelected("ConnChip", s == "Live");
        }

        private static bool HasLinkObject()
        {
            try
            {
                return UnityEngine.Object.FindFirstObjectByType<
                    TwinBridge>(FindObjectsInactive.Include) != null;
            }
            catch { return false; }
        }

        private void OnFrame(int w, int h, byte[] jpg, string source)
        {
            if (!_built || jpg == null || jpg.Length < 16) return;
            try
            {
                if (ImageConversion.LoadImage(_displayTex, jpg))
                {
                    _lastSource = source ?? "";
                    FitImage(w, h);
                    if (_waitingGO != null)
                        _waitingGO.SetActive(false);
                    _sourceTmp.text = _lastSource;
                }
            }
            catch { }
        }

        private void FitImage(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            const float AW = 640f;
            const float AH = 400f;
            float a = w / (float)h;
            float iw, ih;
            if (a >= AW / AH)
            {
                iw = AW;
                ih = AW / a;
            }
            else
            {
                ih = AH;
                iw = AH * a;
            }
            var rt = (RectTransform)_displayImg.transform;
            rt.sizeDelta = new Vector2(iw, ih);
            rt.anchoredPosition = new Vector2((AW - iw) / 2f,
                -(AH - ih) / 2f);
            ApplyZoom();
        }

        private void OnTerminal(IList<string> lines, int dropped)
        {
            if (!_built || lines == null) return;
            _termLines.Clear();
            _termLines.AddRange(lines);
            _scrollOff = 0;
            RenderTerm();
        }

        private void OnGpio(bool?[] lv)
        {
            if (!_built || lv == null) return;
            for (int i = 0; i < StripBcm.Length
                && i < _stripDots.Count; i++)
            {
                int bcm = StripBcm[i];
                bool? v = bcm >= 0 && bcm < lv.Length
                    ? lv[bcm] : null;
                _stripDots[i].color = !v.HasValue
                    ? new Color(1f, 1f, 1f, 0.25f)
                    : v.Value ? ConceptUI.Green
                    : new Color(1f, 1f, 1f, 0.55f);
                _stripTxt[i].text = bcm + " "
                    + (!v.HasValue ? "·" : v.Value ? "H" : "L");
            }
        }

        // ── terminal + keyboard ────────────────────────────────

        private void RenderTerm()
        {
            if (_termTmp == null) return;
            var sb = new System.Text.StringBuilder();
            int show = 7;
            int end = Math.Max(0, _termLines.Count - _scrollOff);
            int start = Math.Max(0, end - show);
            for (int i = start; i < end; i++)
            {
                string l = _termLines[i];
                if (i == _termLines.Count - 1
                    && l == "pi@bench:~$ ")
                    continue; // input row replaces the bare prompt
                sb.AppendLine(l);
            }
            string head = _inputLine.Substring(0,
                Math.Min(_caret, _inputLine.Length));
            string tail = _inputLine.Substring(
                Math.Min(_caret, _inputLine.Length));
            sb.Append("› " + head + "|" + tail);
            _termTmp.text = sb.ToString();
        }

        public void TermScroll(float delta)
        {
            _scrollOff = Math.Max(0, Math.Min(
                Math.Max(0, _termLines.Count - 7),
                _scrollOff + (delta > 0f ? 1 : -1)));
            RenderTerm();
        }

        private void RunQuick(string q)
        {
            _inputLine = q;
            _caret = _inputLine.Length;
            SubmitLine();
        }

        private void SubmitLine()
        {
            if (_inputLine.Length > 0)
            {
                _history.Add(_inputLine);
                while (_history.Count > 50) _history.RemoveAt(0);
            }
            _historyIdx = -1;
            _active?.SendTerminal(_inputLine);
            _inputLine = "";
            _caret = 0;
            _scrollOff = 0;
            RenderTerm();
        }

        public void PressKey(string k)
        {
            PunchKey("Key_" + KeyId(k));
            switch (k)
            {
                case "Bksp":
                    if (_caret > 0 && _inputLine.Length > 0)
                    {
                        _caret--;
                        _inputLine = _inputLine.Remove(_caret, 1);
                    }
                    break;
                case "Enter":
                    SubmitLine();
                    return;
                case "Shift":
                    _shift = !_shift;
                    SetChipSelected("Key_Shift", _shift);
                    return;
                case "Ctrl":
                    _ctrlArmed = !_ctrlArmed;
                    SetChipSelected("Key_Ctrl", _ctrlArmed);
                    return;
                case "Tab":
                    Complete();
                    break;
                case "Space":
                    Insert(" ");
                    break;
                case "^":
                    History(-1);
                    break;
                case "v":
                    History(1);
                    break;
                case "<":
                    _caret = Math.Max(0, _caret - 1);
                    break;
                case ">":
                    _caret = Math.Min(_inputLine.Length, _caret + 1);
                    break;
                default:
                    if (_ctrlArmed)
                    {
                        CtrlKey(k);
                        _ctrlArmed = false;
                        SetChipSelected("Key_Ctrl", false);
                    }
                    else Insert(Shifted(k));
                    break;
            }
            if (k != "Shift" && _shift && k.Length == 1)
            {
                _shift = false; // one-shot caps
                SetChipSelected("Key_Shift", false);
            }
            RenderTerm();
        }

        private void Insert(string s)
        {
            _caret = Math.Min(_caret, _inputLine.Length);
            _inputLine = _inputLine.Insert(_caret, s);
            _caret += s.Length;
        }

        private string Shifted(string k)
        {
            if (k.Length != 1) return k.ToLowerInvariant();
            if (_shift) return k.ToUpperInvariant();
            return k.ToLowerInvariant();
        }

        private void CtrlKey(string k)
        {
            string u = k.ToUpperInvariant();
            if (u == "C")
            {
                _termLines.Add("› " + _inputLine + "^C");
                _inputLine = "";
                _caret = 0;
            }
            else if (u == "L")
            {
                _termLines.Clear();
            }
        }

        private void Complete()
        {
            string[] cmds = { "help", "ls", "pwd", "echo", "python3",
                "ifconfig", "restart", "clear", "gpio" };
            foreach (var c in cmds)
                if (c.StartsWith(_inputLine)
                    && c.Length > _inputLine.Length)
                {
                    _inputLine = c;
                    _caret = _inputLine.Length;
                    return;
                }
        }

        private void History(int dir)
        {
            if (_history.Count == 0) return;
            if (_historyIdx < 0)
            {
                if (dir > 0) // Down at a fresh line clears it
                {
                    _inputLine = "";
                    _caret = 0;
                    return;
                }
                _historyIdx = _history.Count - 1;
            }
            else
                _historyIdx = Math.Max(0, Math.Min(_history.Count - 1,
                    _historyIdx + dir));
            _inputLine = _history[_historyIdx];
            _caret = _inputLine.Length;
        }

        private void PunchKey(string name)
        {
            if (_canvasT == null) return;
            foreach (var t in _canvasT.GetComponentsInChildren<
                Transform>(true))
            {
                if (t.name != name) continue;
                MiniTween.Float(this, 1f, 0.9f, 0.045f, s =>
                {
                    if (t != null)
                        t.localScale = Vector3.one * s;
                }, () =>
                {
                    if (t == null) return;
                    MiniTween.Float(this, 0.9f, 1f, 0.045f, s =>
                    {
                        if (t != null)
                            t.localScale = Vector3.one * s;
                    }, null);
                });
                return;
            }
        }

        // ── display pointer ────────────────────────────────────

        public void CycleZoom()
        {
            _zoomIdx = (_zoomIdx + 1) % _zoomLevels.Length;
            ApplyZoom();
            SetText("ZoomChip",
                _zoomLevels[_zoomIdx].ToString("0.#") + "x");
        }

        private void ApplyZoom()
        {
            if (_displayImg == null) return;
            float z = _zoomLevels[_zoomIdx];
            float f = 1f / z;
            float u0 = Mathf.Clamp(_cursorUV.x - f / 2f, 0f, 1f - f);
            float vTop = Mathf.Clamp(_cursorUV.y - f / 2f, 0f, 1f - f);
            _displayImg.uvRect = new Rect(u0, 1f - vTop - f, f, f);
            _zoomU0 = u0;
            _zoomV0 = vTop;
            _zoomF = f;
        }

        private float _zoomU0;
        private float _zoomV0;
        private float _zoomF = 1f;

        private bool _pressing;
        private Vector2 _pressUV;
        private float _pressT;
        private bool _rightFired;
        private bool _dragSent;
        private float _moveT;

        /// <summary>Shared pointer path (UI events + test hook).</summary>
        public void PointerUV(Vector2 uv, bool down, bool up,
            float scroll)
        {
            uv.x = Mathf.Clamp01(uv.x);
            uv.y = Mathf.Clamp01(uv.y);
            _cursorUV = uv;
            MoveCursorDot();
            if (scroll != 0f && _active != null)
            {
                // UV is already top-origin, like the guest pixels.
                _active.SendDisplayInput(uv.x, uv.y, true, 0,
                    false, false, (int)Mathf.Sign(scroll), true);
                return;
            }
            if (down)
            {
                _pressing = true;
                _pressUV = uv;
                _pressT = 0f;
                _rightFired = false;
                _dragSent = false;
                SendMove(uv);
            }
            else if (up && _pressing)
            {
                _pressing = false;
                float dist = Vector2.Distance(uv, _pressUV);
                if (_rightFired)
                {
                    SendButton(uv, 0x111, false); // right release
                }
                else if (_dragSent)
                {
                    SendButton(uv, 0x110, false); // left release
                }
                else if (_pressT < 0.4f && dist < 0.02f)
                {
                    SendButton(uv, 0x110, true); // click
                    SendButton(uv, 0x110, false);
                }
                else
                {
                    SendButton(uv, 0x110, false);
                }
            }
            else if (_pressing)
            {
                float dist = Vector2.Distance(uv, _pressUV);
                if (dist >= 0.02f && !_rightFired)
                {
                    if (!_dragSent)
                    {
                        _dragSent = true;
                        SendButton(uv, 0x110, true); // drag start
                    }
                    SendMove(uv);
                }
            }
            else
            {
                _moveT += Time.unscaledDeltaTime;
                if (_moveT >= 0.1f) // hover at 10 Hz
                {
                    _moveT = 0f;
                    SendMove(uv);
                }
            }
        }

        public void PointerLeave()
        {
            _pressing = false;
            if (_cursorDot != null) _cursorDot.SetActive(false);
        }

        public void PointerEnter(Vector2 uv)
        {
            if (_cursorDot != null) _cursorDot.SetActive(true);
            PointerUV(uv, false, false, 0f);
        }

        private void MoveCursorDot()
        {
            if (_cursorDot == null || _displayImg == null) return;
            var rt = (RectTransform)_displayImg.transform;
            float px = rt.anchoredPosition.x + _cursorUV.x * rt.rect.width;
            float py = -rt.anchoredPosition.y + _cursorUV.y * rt.rect.height;
            var crt = (RectTransform)_cursorDot.transform;
            crt.anchoredPosition = new Vector2(px - 8f, -(py - 8f));
            if (!_cursorDot.activeSelf) _cursorDot.SetActive(true);
        }

        private void SendMove(Vector2 uv)
        {
            _active?.SendDisplayInput(
                _zoomU0 + uv.x * _zoomF, _zoomV0 + uv.y * _zoomF,
                true, 0, false, false, 0, false);
        }

        private void SendButton(Vector2 uv, int button, bool pressed)
        {
            _active?.SendDisplayInput(
                _zoomU0 + uv.x * _zoomF, _zoomV0 + uv.y * _zoomF,
                true, button, pressed, true, 0, false);
        }

        internal RectTransform DisplayImageRect =>
            _displayImg != null
                ? (RectTransform)_displayImg.transform : null;

        // ── test hooks ─────────────────────────────────────────

        /// <summary>Harness pointer: normalized image coords.</summary>
        public void TestPointer(float nx, float ny, bool down,
            bool up, float scroll = 0f) =>
            PointerUV(new Vector2(nx, ny), down, up, scroll);

        public string GpioCellText(int i) =>
            i >= 0 && i < _stripTxt.Count ? _stripTxt[i].text : "";

        public string ZoomText()
        {
            if (_canvasT == null) return "";
            foreach (var t in _canvasT.GetComponentsInChildren<
                Transform>(true))
            {
                if (t.name != "ZoomChip") continue;
                return t.GetComponentInChildren<TMPro.TMP_Text>(true)
                    ?.text ?? "";
            }
            return "";
        }
    }

    /// <summary>Pointer events on the display image (mouse in the
    /// editor; ISDK ray routes here on device via the Task-10 baked
    /// PointableCanvas).</summary>
    public class DisplayInputZone : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IDragHandler,
        IScrollHandler
    {
        public DisplayWindow window;

        public void OnPointerEnter(PointerEventData e) =>
            window?.PointerEnter(ToUV(e));
        public void OnPointerExit(PointerEventData e) =>
            window?.PointerLeave();
        public void OnPointerDown(PointerEventData e) =>
            window?.PointerUV(ToUV(e), true, false, 0f);
        public void OnPointerUp(PointerEventData e) =>
            window?.PointerUV(ToUV(e), false, true, 0f);
        public void OnDrag(PointerEventData e) =>
            window?.PointerUV(ToUV(e), false, false, 0f);
        public void OnScroll(PointerEventData e) =>
            window?.PointerUV(ToUV(e), false, false,
                e.scrollDelta.y);

        private Vector2 ToUV(PointerEventData e)
        {
            var rt = window?.DisplayImageRect;
            if (rt == null) return new Vector2(0.5f, 0.5f);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, e.position, e.pressEventCamera, out local);
            float nx = (local.x - rt.rect.xMin) / rt.rect.width;
            float ny = (rt.rect.yMax - local.y) / rt.rect.height;
            return new Vector2(nx, ny);
        }
    }

    /// <summary>Vertical drag scrolls the terminal buffer.</summary>
    public class TermScrollZone : MonoBehaviour, IDragHandler
    {
        public DisplayWindow window;

        public void OnDrag(PointerEventData e) =>
            window?.TermScroll(e.delta.y);
    }
}
