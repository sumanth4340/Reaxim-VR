// PinModeController.cs — Pin Mode orchestration (Task 7).
// Enter from the board menu (PinModeRequested): dims the workspace,
// shows pin labels + dots + banner; exit restores everything in
// 250 ms. Owns the filter state, 10 Hz label refresh, gaze-dwell
// magnifier, toasts, per-board GPIO modes, and the circuit model.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Bench.Circuit;

namespace Bench.PinMode
{
    public enum PinFilter
    {
        All, Power, Ground, Gpio, I2c, Spi, Uart, Free
    }

    public class PinModeController : MonoBehaviour
    {
        public static PinModeController Instance { get; private set; }

        public bool Active { get; private set; }
        public CircuitModel Model { get; } = new CircuitModel();
        public PinFilter Filter { get; private set; } = PinFilter.All;

        private GameObject _banner;
        private TMPro.TMP_Text _statusTxt;
        private GameObject _fixBtn;
        private System.Action _fixAction;
        private float _toastT;
        private WireTool _wire;
        private PinLabels _labels;
        private SchematicView _schematic;
        private float _labelT;
        private readonly Dictionary<string, GpioMode[]> _gpio =
            new Dictionary<string, GpioMode[]>();
        private readonly List<(Renderer r, Color c, bool baseColor)> _dimmed =
            new List<(Renderer, Color, bool)>();
        private GameObject _magnifier;
        private Vector3 _gazeAnchor;
        private float _gazeT;
        private bool _gazeArmed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            Panels.BoardMenu.PinModeRequested += OnPinModeRequested;
            _wire = gameObject.AddComponent<WireTool>();
            _labels = gameObject.AddComponent<PinLabels>();
            _schematic = gameObject.AddComponent<SchematicView>();
            _wire.enabled = false;
            _labels.enabled = false;
        }

        private void OnDestroy()
        {
            Panels.BoardMenu.PinModeRequested -= OnPinModeRequested;
            if (Instance == this) Instance = null;
        }

        private void OnPinModeRequested(Panels.BoardMenu.PlacedPartShim shim)
        {
            Enter();
        }

        public static void Enter()
        {
            var c = Instance;
            if (c == null) return;
            if (c.Active)
            {
                RefreshPins(); // re-entry refreshes (parts may have changed)
                return;
            }
            c.Active = true;
            c._wire.enabled = true;
            c._labels.enabled = true;
            RefreshPins();
            c.Filter = PinFilter.All;
            c.RefreshFilterChips();
            c.DimWorkspace(true);
            c.ShowBanner(true);
            c.RebuildModel();
            c.HideMagnifier();
            // 2D schematic panel (user request): boards + components
            // flat, pins visible, drag pin-to-pin to wire.
            c._schematic?.Show();
            c.Status("Pin Mode — pinch a pin, drag to a pin, release");
            Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Snap);
        }

        /// <summary>Rebuild registry + dots + model (safe anytime).</summary>
        public static void RefreshPins()
        {
            var c = Instance;
            if (c == null || !c.Active) return;
            PinRegistry.Rebuild();
            c._wire.ClearDots();
            c._wire.BuildDots();
            c._wire.PruneDead();
            c.RebuildModel();
            if (c._schematic != null && c._schematic.IsShowing)
                c._schematic.Show();
        }

        public static void Exit()
        {
            var c = Instance;
            if (c == null || !c.Active) return;
            c.Active = false;
            c._schematic?.Hide(); // back to normal 3D objects
            c._wire.CancelPending(quiet: true);
            c._wire.ClearDots();
            c._labels.HideAll();
            c.HideMagnifier();
            c.DimWorkspace(false);
            c.ShowBanner(false);
            c._wire.enabled = false;
            c._labels.enabled = false;
            Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Press);
        }

        public void Poke() { }

        // ── banner ───────────────────────────────────────────────

        private void ShowBanner(bool show)
        {
            if (_banner == null)
            {
                // Inactive-inclusive: the template starts hidden.
                GameObject found = null;
                foreach (var t in GameObject.FindObjectsOfType<Transform>(true))
                {
                    if (t.name == "PinBanner" && t.parent != null
                        && t.parent.name == "ConceptUI")
                    {
                        found = t.gameObject;
                        break;
                    }
                }
                if (found == null) return;
                _banner = found;
                WireBanner(_banner.transform);
            }
            _banner.SetActive(show);
        }

        private void WireBanner(Transform root)
        {
            var exit = root.Find("Card/ExitBtn")?.gameObject;
            Btn(exit, () => Exit());
            var undo = root.Find("Card/UndoBtn")?.gameObject;
            Btn(undo, () => _wire.Undo());
            var redo = root.Find("Card/RedoBtn")?.gameObject;
            Btn(redo, () => _wire.Redo());
            _fixBtn = root.Find("Card/FixBtn")?.gameObject;
            Btn(_fixBtn, () =>
            {
                var a = _fixAction;
                _fixAction = null;
                if (_fixBtn != null) _fixBtn.SetActive(false);
                a?.Invoke();
            });
            if (_fixBtn != null) _fixBtn.SetActive(false);
            var filters = root.Find("Card/Filters");
            if (filters != null)
            {
                Chip(filters, "Filter_All", PinFilter.All);
                Chip(filters, "Filter_Power", PinFilter.Power);
                Chip(filters, "Filter_Ground", PinFilter.Ground);
                Chip(filters, "Filter_GPIO", PinFilter.Gpio);
                Chip(filters, "Filter_I2C", PinFilter.I2c);
                Chip(filters, "Filter_SPI", PinFilter.Spi);
                Chip(filters, "Filter_UART", PinFilter.Uart);
                Chip(filters, "Filter_Free", PinFilter.Free);
            }
            var st = root.Find("Card/Status/Txt")?.GetComponent<TMPro.TMP_Text>();
            if (st != null) _statusTxt = st;
        }

        private void Btn(GameObject go, System.Action onClick)
        {
            if (go == null) return;
            var b = go.GetComponent<Button>();
            if (b == null) return;
            b.onClick.AddListener(() =>
            {
                Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Press);
                onClick?.Invoke();
            });
        }

        private void Chip(Transform filters, string name, PinFilter f)
        {
            var go = filters.Find(name)?.gameObject;
            if (go == null) return;
            Btn(go, () =>
            {
                Filter = f;
                RefreshFilterChips();
                RefreshLabels(true);
            });
        }

        private void RefreshFilterChips()
        {
            if (_banner == null) return;
            var filters = _banner.transform.Find("Card/Filters");
            if (filters == null) return;
            var map = new (string, PinFilter)[]
            {
                ("Filter_All", PinFilter.All), ("Filter_Power", PinFilter.Power),
                ("Filter_Ground", PinFilter.Ground), ("Filter_GPIO", PinFilter.Gpio),
                ("Filter_I2C", PinFilter.I2c), ("Filter_SPI", PinFilter.Spi),
                ("Filter_UART", PinFilter.Uart), ("Filter_Free", PinFilter.Free),
            };
            foreach (var (name, f) in map)
            {
                var go = filters.Find(name)?.gameObject;
                if (go == null) continue;
                var gp = go.GetComponent<Bench.UI.GlassPanel>();
                bool sel = f == Filter;
                if (gp != null)
                {
                    gp.fillOverride = sel ? Bench.UI.ConceptUI.SelFillStrong
                        : new Color(1f, 1f, 1f, 0.06f);
                    gp.rimOverride = sel ? Bench.UI.ConceptUI.Cyan
                        : new Color(1f, 1f, 1f, 0.25f);
                    gp.ApplyTheme();
                }
            }
            _wire?.SetDotsVisible(PinVisible);
        }

        public void TestSetFilter(PinFilter f)
        {
            Filter = f;
            RefreshFilterChips();
            RefreshLabels(true);
        }

        private bool PinVisible(PinDef pin)
        {
            switch (Filter)
            {
                case PinFilter.All: return true;
                case PinFilter.Power:
                    return pin.role == PinRole.Power5V || pin.role == PinRole.Power3V3;
                case PinFilter.Ground: return pin.role == PinRole.Ground;
                case PinFilter.Gpio: return pin.role == PinRole.Gpio;
                case PinFilter.I2c: return pin.role == PinRole.I2c;
                case PinFilter.Spi: return pin.role == PinRole.Spi;
                case PinFilter.Uart: return pin.role == PinRole.Uart;
                case PinFilter.Free: return !IsWired(pin.node);
            }
            return true;
        }

        private bool IsWired(string node)
        {
            if (_wire == null) return false;
            foreach (var w in _wire.Wires)
                if (w.a == node || w.b == node) return true;
            return false;
        }

        public void Status(string text)
        {
            if (_statusTxt != null) _statusTxt.text = text;
            if (_fixBtn != null) _fixBtn.SetActive(false);
            _fixAction = null;
            _toastT = 0f;
        }

        public void Toast(string title, string message, bool error,
            string fixLabel = null, System.Action fix = null)
        {
            if (_statusTxt != null)
                _statusTxt.text = (error ? "⚠ " : "") + title + " — " + message;
            _toastT = 3.5f;
            _fixAction = fix;
            if (_fixBtn != null)
            {
                bool show = fix != null && !string.IsNullOrEmpty(fixLabel);
                _fixBtn.SetActive(show);
                if (show)
                {
                    var t = _fixBtn.GetComponentInChildren<TMPro.TMP_Text>(true);
                    if (t != null) t.text = fixLabel;
                }
            }
            if (error) Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Error);
        }

        // ── model ────────────────────────────────────────────────

        /// <summary>First configured drive for a BCM (instruments).
        /// PWM duty is fixed 50% (not adjustable in Pin Mode UI).</summary>
        public static bool TryGetGpioDrive(int bcm, out GpioMode mode,
            out float duty)
        {
            mode = GpioMode.In;
            duty = 0.5f;
            var c = Instance;
            if (c == null) return false;
            foreach (var kv in c._gpio)
            {
                if (bcm < 0 || bcm >= kv.Value.Length) continue;
                if (kv.Value[bcm] != GpioMode.In)
                {
                    mode = kv.Value[bcm];
                    return true;
                }
            }
            return false;
        }

        public void SetGpioMode(string partId, int bcm, GpioMode mode)
        {
            if (!_gpio.TryGetValue(partId, out var arr))
            {
                arr = new GpioMode[54];
                _gpio[partId] = arr;
            }
            if (bcm >= 0 && bcm < arr.Length) arr[bcm] = mode;
            RebuildModel();
        }

        public void RebuildModel()
        {
            var parts = new List<CircuitModel.CPart>();
            foreach (var p in Workspace.WorkspacePhysics.PartsSnapshot())
            {
                if (p == null) continue;
                var cp = new CircuitModel.CPart
                {
                    id = p.partId,
                    kind = p.type,
                    powered = !p.isBoard || p.powered,
                };
                if (p.isBoard)
                {
                    if (!_gpio.TryGetValue(p.partId, out var arr))
                    {
                        arr = new GpioMode[54];
                        _gpio[p.partId] = arr;
                    }
                    cp.gpioModes = arr;
                    cp.gpioDuty = new float[54];
                    for (int i = 0; i < 54; i++) cp.gpioDuty[i] = 0.5f;
                }
                parts.Add(cp);
            }
            var wires = new List<CircuitModel.CWire>();
            if (_wire != null)
                foreach (var w in _wire.Wires)
                    wires.Add(new CircuitModel.CWire
                        { id = w.id, a = w.a, b = w.b });
            Model.SetParts(parts);
            Model.SetWires(wires);
            Model.Solve();
        }

        // ── per-frame ────────────────────────────────────────────

        private void Update()
        {
            if (!Active) return;
            var cam = Camera.main;
            if (cam != null && _banner != null && _banner.activeSelf)
            {
                _banner.transform.position = cam.transform.position
                    + cam.transform.forward * 0.9f + Vector3.up * 0.28f;
                var look = cam.transform.position - _banner.transform.position;
                if (look.sqrMagnitude > 1e-6f)
                    _banner.transform.rotation = Quaternion.LookRotation(
                        -look.normalized, Vector3.up);
            }
            if (_toastT > 0f)
            {
                _toastT -= Time.unscaledDeltaTime;
                if (_toastT <= 0f && _statusTxt != null)
                {
                    _statusTxt.text = "";
                    if (_fixBtn != null) _fixBtn.SetActive(false);
                    _fixAction = null;
                }
            }
            _labelT += Time.unscaledDeltaTime;
            if (_labelT >= 0.1f)
            {
                _labelT = 0f;
                RefreshLabels(false);
            }
            UpdateMagnifier(cam);
        }

        private void RefreshLabels(bool force)
        {
            if (!Active || _labels == null) return;
            _labels.Rebuild(PinRegistry.Pins, Camera.main, PinVisible,
                _wire != null ? _wire.PendingA?.node : null);
        }

        // ── dim ──────────────────────────────────────────────────

        private void DimWorkspace(bool dim)
        {
            if (dim)
            {
                _dimmed.Clear();
                foreach (var p in Workspace.WorkspacePhysics.PartsSnapshot())
                {
                    if (p == null) continue;
                    foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                    {
                        var mat = r.material;
                        if (mat.HasProperty("_BaseColor"))
                        {
                            var c = mat.GetColor("_BaseColor");
                            _dimmed.Add((r, c, true));
                            mat.SetColor("_BaseColor", c * 0.35f);
                        }
                        else if (mat.HasProperty("_Color"))
                        {
                            var c = mat.GetColor("_Color");
                            _dimmed.Add((r, c, false));
                            mat.SetColor("_Color", c * 0.35f);
                        }
                    }
                }
            }
            else
            {
                foreach (var (r, c, isBase) in _dimmed)
                {
                    if (r == null) continue;
                    var mat = r.material;
                    if (isBase && mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", c);
                    else if (!isBase && mat.HasProperty("_Color"))
                        mat.SetColor("_Color", c);
                }
                _dimmed.Clear();
            }
        }

        // ── magnifier ────────────────────────────────────────────

        public bool MagnifierVisible =>
            _magnifier != null && _magnifier.activeSelf;

        private void UpdateMagnifier(Camera cam)
        {
            if (cam == null) return;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var near = new List<PinDef>();
            foreach (var pin in PinRegistry.Pins)
            {
                if (!PinVisible(pin)) continue;
                Vector3 toPin = pin.WorldPos - ray.origin;
                float along = Vector3.Dot(toPin, ray.direction);
                if (along <= 0f || along > 3f) continue;
                float d = Vector3.Distance(ray.origin + ray.direction * along,
                    pin.WorldPos);
                if (d < 0.04f) near.Add(pin);
            }
            if (near.Count >= 3)
            {
                Vector3 centroid = Vector3.zero;
                foreach (var p in near) centroid += p.WorldPos;
                centroid /= near.Count;
                if (!_gazeArmed || Vector3.Distance(centroid, _gazeAnchor) > 0.03f)
                {
                    _gazeAnchor = centroid;
                    _gazeT = 0f;
                    _gazeArmed = true;
                }
                else
                {
                    _gazeT += Time.unscaledDeltaTime;
                    if (_gazeT >= 0.4f && !MagnifierVisible)
                        ShowMagnifier(near, centroid, cam);
                }
            }
            else
            {
                _gazeArmed = false;
                _gazeT = 0f;
                if (MagnifierVisible) HideMagnifier();
            }
        }

        private void ShowMagnifier(List<PinDef> pins, Vector3 at, Camera cam)
        {
            HideMagnifier();
            _magnifier = new GameObject("PinMagnifier");
            _magnifier.transform.position = at
                + (cam.transform.position - at).normalized * 0.12f
                + Vector3.up * 0.06f;
            _magnifier.transform.rotation = cam.transform.rotation;
            int n = Mathf.Min(12, pins.Count);
            for (int i = 0; i < n; i++)
            {
                var pin = pins[i];
                var pill = GameObject.CreatePrimitive(PrimitiveType.Quad);
                pill.name = "Mag_" + pin.node;
                pill.transform.SetParent(_magnifier.transform, false);
                int cols = 4;
                float x = (i % cols - 1.5f) * 0.03f;
                float y = (1 - i / cols) * 0.034f;
                pill.transform.localPosition = new Vector3(x, y, 0f);
                pill.transform.localScale = new Vector3(0.026f, 0.026f, 1f);
                pill.GetComponent<Renderer>().material.color =
                    new Color(0.055f, 0.07f, 0.094f, 1f);
                var tg = new GameObject("Txt");
                tg.transform.SetParent(pill.transform, false);
                tg.transform.localPosition = new Vector3(0f, 0f, -0.001f);
                var tmp = tg.AddComponent<TMPro.TextMeshPro>();
                tmp.text = PinGlyph(pin) + " " + pin.label;
                tmp.fontSize = 20f;
                tmp.color = RoleColor(pin.role);
                // Fit inside the 26 mm pill: 2.46 * 0.026 * 0.11 ≈ 7 mm caps.
                tmp.transform.localScale = Vector3.one * 0.11f;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.rectTransform.sizeDelta = new Vector2(60f, 26f);
                var click = pill.AddComponent<WireToolPinProxy>();
                click.node = pin.node;
            }
            Bench.UI.SoundBank.Play(Bench.UI.BenchSound.Hover);
        }

        public void TestMagnifierTap(string node)
        {
            if (!MagnifierVisible) return;
            foreach (Transform c in _magnifier.transform)
            {
                if (c.name == "Mag_" + node)
                {
                    _wire?.TapPin(node);
                    HideMagnifier();
                    return;
                }
            }
        }

        private void HideMagnifier()
        {
            if (_magnifier != null) Destroy(_magnifier);
            _magnifier = null;
        }

        // ── shared styling ───────────────────────────────────────

        public static Color RoleColor(PinRole role)
        {
            switch (role)
            {
                case PinRole.Power5V:
                case PinRole.Power3V3: return new Color(1f, 0.42f, 0.42f);
                case PinRole.Ground: return new Color(0.82f, 0.84f, 0.87f);
                case PinRole.Gpio: return new Color(0.36f, 0.88f, 0.9f);
                case PinRole.I2c: return new Color(1f, 0.71f, 0.33f);
                case PinRole.Spi: return new Color(0.75f, 0.55f, 1f);
                case PinRole.Uart: return new Color(0.4f, 0.85f, 0.4f);
                default: return new Color(0.7f, 0.72f, 0.75f);
            }
        }

        public static string PinGlyph(PinDef pin)
        {
            switch (pin.role)
            {
                case PinRole.Power5V:
                case PinRole.Power3V3: return "▲";
                case PinRole.Ground: return "■";
                default: return "●";
            }
        }
    }

    /// <summary>Click proxy for magnifier pills (same tap path).</summary>
    public class WireToolPinProxy : MonoBehaviour
    {
        public string node = "";
        private void OnMouseUpAsButton()
        {
            PinModeController.Instance?.GetComponent<WireTool>()?.TapPin(node);
        }
    }
}
