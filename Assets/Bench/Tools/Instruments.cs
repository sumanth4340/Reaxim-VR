// Instruments.cs — Task 8: oscilloscope / multimeter / waveform
// analyser + probe pens. Lives on the ConceptUI root; binds the
// Screen_Work ScopePanel views built by BuildConceptUI.
// Signal chain: local CircuitModel DC + GPIO drive shape; live twin
// ProbeViews override when the bridge is connected (readings then
// tagged "sampled": a Pi has no analog inputs, so real volts need
// an ADC and real amps need an INA219 — Task 9 UI note).
// Channel map: CH1 = scope pen, CH2 = red pen, meter = red+black,
// analyser = CH1 net. Pens park in a tray under the scope panel.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Bench.Circuit;
using Bench.Core;
using Bench.PinMode;
using Bench.Twin;
using Bench.UI;

namespace Bench.Tools
{
    public class Instruments : MonoBehaviour
    {
        public static Instruments Instance { get; private set; }

        public enum Mode { Scope, Meter, Analyser }
        public enum MeterMode
        {
            Vdc, Vac, Amp, Watt, Ohm, Cont, Diode
        }

        public Mode CurrentMode { get; private set; } = Mode.Scope;
        public MeterMode CurrentMeter { get; private set; } =
            MeterMode.Vdc;
        public string LastWarning { get; private set; } = "";
        public ProbePen ScopePen => _scopePen;
        public ProbePen RedPen => _redPen;
        public ProbePen BlackPen => _blackPen;

        private static readonly float[] VoltsDivs = { 0.5f, 1f, 2f, 5f };
        private static readonly float[] TimeDivsMs =
            { 0.1f, 0.2f, 0.5f, 1f, 2f, 5f };
        private static readonly float[] TrigLevels =
            { 0.4f, 0.8f, 1.65f, 2.5f };

        private int _vDivIdx = 1;
        private int _tDivIdx = 3;
        private int _trigIdx = 2;
        private bool _trigRising = true;
        private bool _running = true;

        private Transform _work;
        private Transform _scope;
        private GameObject _scopeView;
        private GameObject _meterView;
        private GameObject _anaView;
        private CanvasGroup _scopeCg;
        private CanvasGroup _meterCg;
        private CanvasGroup _anaCg;
        private RawImage _scopeImg;
        private RawImage _anaImg;
        private Texture2D _scopeTex;
        private Texture2D _anaTex;
        private Color32[] _scopePx;
        private Color32[] _anaPx;
        private readonly Dictionary<string, TMPro.TMP_Text> _txt =
            new Dictionary<string, TMPro.TMP_Text>();
        private GameObject _boundScope;
        private bool _bound;
        private AppStateMachine _machine;
        private float _refreshT;

        private readonly float[] _ch1 = new float[Waveforms.BufN];
        private readonly float[] _ch2 = new float[Waveforms.BufN];
        private readonly float[] _scratch = new float[Waveforms.BufN];
        private readonly float[] _anaRe = new float[64];
        private readonly float[] _anaMag = new float[32];
        private readonly float[] _vacBuf = new float[Waveforms.BufN];

        private ProbePen _scopePen;
        private ProbePen _redPen;
        private ProbePen _blackPen;
        private GameObject _tray;

        private readonly Dictionary<int, TwinBridge.ProbeView> _probes =
            new Dictionary<int, TwinBridge.ProbeView>();
        private ITwinLink _twinSub;
        private bool _scopeLive;
        private bool _meterLive;

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
            _machine = AppStateMachine.Instance;
            if (_machine != null) _machine.StateChanged += OnState;
            Rebind();
            SubscribeTwin();
            RefreshNow();
        }

        private void OnDestroy()
        {
            if (_machine != null) _machine.StateChanged -= OnState;
            if (_twinSub != null) _twinSub.OnSnapshot -= OnSnapshot;
            if (Instance == this) Instance = null;
        }

        private void OnState(AppState from, AppState to)
        {
            Rebind();
            if (to == AppState.Workspace)
            {
                EnsurePens();
                RefreshNow();
            }
        }

        // ── binding ──────────────────────────────────────────

        /// <summary>Re-find views after a stage rebuild.</summary>
        public void Rebind()
        {
            _work = transform.Find("Screen_Work");
            if (_work == null) { _bound = false; return; }
            _scope = _work.Find("ScopePanel");
            if (_scope == null) { _bound = false; return; }
            _scopeView = _scope.Find("ScopeView")?.gameObject;
            _meterView = _scope.Find("MeterView")?.gameObject;
            _anaView = _scope.Find("AnalyserView")?.gameObject;
            if (_scopeView == null || _meterView == null
                || _anaView == null)
            {
                _bound = false;
                return;
            }
            _scopeCg = Cg(_scopeView);
            _meterCg = Cg(_meterView);
            _anaCg = Cg(_anaView);
            _scopeImg = _scopeView.transform.Find("ScopeScreen")
                ?.GetComponent<RawImage>();
            _anaImg = _anaView.transform.Find("AnalyserScreen")
                ?.GetComponent<RawImage>();
            EnsureTextures();
            _txt.Clear();
            Cache(_scopeView.transform, "ProbeSummary");
            Cache(_scopeView.transform, "Legend1");
            Cache(_scopeView.transform, "Legend2");
            Cache(_scopeView.transform, "StatsLine");
            Cache(_meterView.transform, "MeterProbes");
            Cache(_meterView.transform, "MeterReading");
            Cache(_meterView.transform, "MeterSub");
            Cache(_anaView.transform, "AnalyserClass");
            Cache(_anaView.transform, "AnalyserPeak");
            if (_boundScope != _scope.gameObject)
            {
                _boundScope = _scope.gameObject;
                WireButtons();
                ApplyModeInstant();
            }
            _bound = true;
        }

        private static CanvasGroup Cg(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            return cg != null ? cg : go.AddComponent<CanvasGroup>();
        }

        private void Cache(Transform root, string name)
        {
            // Names live on the TMP grandchildren (the builder's
            // Label() returns the child text), so scan descendants.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                var tmp = t.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tmp != null) { _txt[name] = tmp; return; }
            }
        }

        private void EnsureTextures()
        {
            if (_scopePx == null || _scopePx.Length != 550 * 130)
            {
                _scopeTex = new Texture2D(550, 130,
                    TextureFormat.RGBA32, false);
                _scopeTex.filterMode = FilterMode.Point;
                _scopePx = new Color32[550 * 130];
            }
            if (_anaPx == null || _anaPx.Length != 550 * 130)
            {
                _anaTex = new Texture2D(550, 130,
                    TextureFormat.RGBA32, false);
                _anaTex.filterMode = FilterMode.Point;
                _anaPx = new Color32[550 * 130];
            }
            if (_scopeImg != null) _scopeImg.texture = _scopeTex;
            if (_anaImg != null) _anaImg.texture = _anaTex;
        }

        private void WireButtons()
        {
            WireRow("Inst_Oscilloscope", Mode.Scope);
            WireRow("Inst_Multimeter", Mode.Meter);
            WireRow("Inst_Waveform analyser", Mode.Analyser);
            WireChip(_scopeView.transform, "VDivChip", CycleVDiv);
            WireChip(_scopeView.transform, "TDivChip", CycleTDiv);
            WireChip(_scopeView.transform, "TrigChip", CycleTrig);
            WireChip(_scopeView.transform, "EdgeChip", ToggleEdge);
            WireChip(_scopeView.transform, "RunChip", ToggleRun);
            WireChip(_scopeView.transform, "AutoChip", AutoSetup);
            WireChip(_meterView.transform, "ModeVdc",
                () => SetMeter(MeterMode.Vdc));
            WireChip(_meterView.transform, "ModeVac",
                () => SetMeter(MeterMode.Vac));
            WireChip(_meterView.transform, "ModeAmp",
                () => SetMeter(MeterMode.Amp));
            WireChip(_meterView.transform, "ModeWatt",
                () => SetMeter(MeterMode.Watt));
            WireChip(_meterView.transform, "ModeOhm",
                () => SetMeter(MeterMode.Ohm));
            WireChip(_meterView.transform, "ModeCont",
                () => SetMeter(MeterMode.Cont));
            WireChip(_meterView.transform, "ModeDiode",
                () => SetMeter(MeterMode.Diode));
        }

        private void WireRow(string name, Mode mode)
        {
            // Rows live in the left panel, not the scope panel.
            var row = _work.GetComponentsInChildren<Transform>(true);
            foreach (var t in row)
            {
                if (t.name != name) continue;
                var b = t.GetComponent<Button>();
                if (b == null) b = t.gameObject.AddComponent<Button>();
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() =>
                {
                    SoundBank.Play(BenchSound.Press);
                    SelectMode(mode);
                });
            }
        }

        private void WireChip(Transform root, string name, Action act)
        {
            var t = root.Find(name);
            if (t == null) return;
            var b = t.GetComponent<Button>();
            if (b == null) b = t.gameObject.AddComponent<Button>();
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                act?.Invoke();
            });
        }

        private void Update()
        {
            if (!_bound) return;
            _refreshT += Time.unscaledDeltaTime;
            if (_refreshT < 1f / 12f) return;
            _refreshT = 0f;
            if (_machine != null && _machine.Current != AppState.Workspace)
                return;
            if (!_scope.gameObject.activeInHierarchy) return;
            RefreshNow();
        }

        // ── modes ────────────────────────────────────────────

        public void SelectMode(Mode mode)
        {
            if (!_bound || mode == CurrentMode)
            {
                RefreshNow();
                return;
            }
            var from = CgFor(CurrentMode);
            CurrentMode = mode;
            var to = CgFor(mode);
            RestyleRows();
            RestyleMeterModes();
            RefreshNow();
            MiniTween.Float(this, 1f, 0f, 0.11f, a => from.alpha = a,
                () =>
                {
                    from.gameObject.SetActive(false);
                    to.gameObject.SetActive(true);
                    MiniTween.Float(this, 0f, 1f, 0.11f,
                        a => to.alpha = a, null);
                });
        }

        private CanvasGroup CgFor(Mode mode)
        {
            if (mode == Mode.Meter) return _meterCg;
            return mode == Mode.Analyser ? _anaCg : _scopeCg;
        }

        private void ApplyModeInstant()
        {
            _scopeView.SetActive(CurrentMode == Mode.Scope);
            _meterView.SetActive(CurrentMode == Mode.Meter);
            _anaView.SetActive(CurrentMode == Mode.Analyser);
            _scopeCg.alpha = CurrentMode == Mode.Scope ? 1f : 0f;
            _meterCg.alpha = CurrentMode == Mode.Meter ? 1f : 0f;
            _anaCg.alpha = CurrentMode == Mode.Analyser ? 1f : 0f;
            RestyleRows();
            RestyleMeterModes();
        }

        private void RestyleRows()
        {
            RestyleRow("Inst_Oscilloscope", CurrentMode == Mode.Scope);
            RestyleRow("Inst_Multimeter", CurrentMode == Mode.Meter);
            RestyleRow("Inst_Waveform analyser",
                CurrentMode == Mode.Analyser);
        }

        private void RestyleRow(string name, bool selected)
        {
            foreach (var t in _work.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                var gp = t.GetComponent<GlassPanel>();
                if (gp == null) continue;
                gp.rimOverride = selected ? ConceptUI.Cyan
                    : new Color(1f, 1f, 1f, 0.2f);
                gp.ApplyTheme();
            }
        }

        public void SetMeter(MeterMode mode)
        {
            CurrentMeter = mode;
            RestyleMeterModes();
            RefreshNow();
        }

        private void RestyleMeterModes()
        {
            if (_meterView == null) return;
            string[] names =
            {
                "ModeVdc", "ModeVac", "ModeAmp", "ModeWatt",
                "ModeOhm", "ModeCont", "ModeDiode"
            };
            for (int i = 0; i < names.Length; i++)
            {
                var t = _meterView.transform.Find(names[i]);
                var gp = t?.GetComponent<GlassPanel>();
                if (gp == null) continue;
                gp.rimOverride = (int)CurrentMeter == i
                    ? ConceptUI.Cyan
                    : new Color(1f, 1f, 1f, 0.2f);
                gp.ApplyTheme();
            }
        }

        // ── pens + tray ──────────────────────────────────────

        public void EnsurePens()
        {
            if (!_bound || _scopePen != null) return;
            // Anchor to the panel corners: the pivot is top-left, so
            // position-based offsets land mid-panel (seen in capture).
            var corners = new Vector3[4];
            ((RectTransform)_scope).GetWorldCorners(corners);
            Vector3 right = (corners[3] - corners[0]).normalized;
            Vector3 up = (corners[1] - corners[0]).normalized;
            Vector3 bottom = (corners[0] + corners[3]) * 0.5f;
            Vector3 c = bottom - up * 0.106f;
            c += _scope.transform.forward * 0.12f; // float in front
            _tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _tray.name = "ProbeTray";
            _tray.transform.position = c;
            _tray.transform.rotation = _scope.transform.rotation;
            _tray.transform.localScale = new Vector3(0.26f, 0.012f, 0.07f);
            _tray.GetComponent<Renderer>().material.color =
                new Color(0.09f, 0.11f, 0.14f, 1f);
            Vector3 top = c + up * 0.006f;
            _scopePen = ProbePen.Spawn("scope",
                new Color(1f, 0.8f, 0.2f, 1f), top - right * 0.08f);
            _redPen = ProbePen.Spawn("red",
                new Color(1f, 0.2f, 0.2f, 1f), top);
            _blackPen = ProbePen.Spawn("black",
                new Color(0.15f, 0.15f, 0.18f, 1f), top + right * 0.08f);
        }

        /// <summary>Pen dropped at world pos: park on the tray, snap
        /// to a pin/hole/lead, or fall back to the tray.</summary>
        public void DropPen(ProbePen pen, Vector3 pos)
        {
            if (pen == null) return;
            LastWarning = "";
            EnsureModel();
            try { PinRegistry.Rebuild(); } catch { }
            if (_tray != null && Vector3.Distance(pos,
                _tray.transform.position) < 0.16f)
            {
                pen.Rest();
                ReleasePen(pen);
                return;
            }
            PinDef hit = null;
            try { hit = PinRegistry.TryHole(pos); } catch { }
            if (hit == null)
                try
                {
                    hit = PinRegistry.Nearest(pos, 0.03f);
                }
                catch { }
            if (hit == null)
            {
                pen.Rest();
                ReleasePen(pen);
                return;
            }
            pen.transform.position = hit.WorldPos;
            pen.transform.rotation = Quaternion.identity;
            pen.SetNet(hit.node);
            pen.Pulse();
            bool clash = OtherPenSharesNet(pen);
            if (clash)
            {
                LastWarning = "Two probes share one net; readings agree.";
                SoundBank.Play(BenchSound.Error);
            }
            else
            {
                SoundBank.PlayAt(BenchSound.Snap, hit.WorldPos);
            }
            if (IsPwmNet(hit.node))
                LastWarning = (LastWarning.Length > 0 ? LastWarning + " "
                    : "") + "Probe loading may disturb fast edges.";
            PushProbes();
            RefreshNow();
        }

        public void ReleasePen(ProbePen pen)
        {
            if (pen == null) return;
            pen.SetNet(null);
            PushProbes();
            RefreshNow();
        }

        private bool OtherPenSharesNet(ProbePen pen)
        {
            if (string.IsNullOrEmpty(pen.attachedNode)) return false;
            foreach (var o in new[] { _scopePen, _redPen, _blackPen })
            {
                if (o == null || o == pen) continue;
                if (string.IsNullOrEmpty(o.attachedNode)) continue;
                if (SameNet(pen.attachedNode, o.attachedNode)) return true;
            }
            return false;
        }

        private static bool IsPwmNet(string node)
        {
            if (string.IsNullOrEmpty(node) || !node.StartsWith("gpio:"))
                return false;
            if (!int.TryParse(node.Substring(5), out var bcm)) return false;
            return PinModeController.TryGetGpioDrive(bcm, out var m,
                out _) && m == GpioMode.OutPwm;
        }

        // ── model + signals ──────────────────────────────────

        private CircuitModel Model =>
            PinModeController.Instance?.Model;

        private void EnsureModel()
        {
            try { PinModeController.Instance?.RebuildModel(); }
            catch { }
        }

        private float Vdc(string node)
        {
            if (string.IsNullOrEmpty(node)) return 0f;
            var m = Model;
            if (m != null && m.NetVolts.ContainsKey(
                CircuitModel.Canonical(node)))
                return m.PinVoltage(node);
            if (node.StartsWith("gpio:") && AnyBoardPowered())
                return CircuitModel.Rail3V3; // input pull-up assumption
            return 0f;
        }

        private static bool AnyBoardPowered()
        {
            try
            {
                foreach (var p in Workspace.WorkspacePhysics.PartsSnapshot())
                    if (p != null && p.isBoard && p.powered)
                        return true;
            }
            catch { }
            return false;
        }

        /// <summary>Fill buf over window seconds for a net. Returns
        /// true when the twin supplied live data ("sampled").</summary>
        private bool SignalForNet(string node, float[] buf, float window)
        {
            int n = buf.Length;
            if (string.IsNullOrEmpty(node))
            {
                Array.Clear(buf, 0, n);
                return false;
            }
            float dt = window / buf.Length;
            if (TryLiveSignal(node, buf, window)) return true;
            var sig = new NetSignal
            {
                kind = SignalKind.Dc,
                dc = Vdc(node),
            };
            if (node.StartsWith("gpio:")
                && int.TryParse(node.Substring(5), out var bcm)
                && PinModeController.TryGetGpioDrive(bcm, out var drive,
                    out var duty))
            {
                if (drive == GpioMode.OutPwm)
                {
                    sig.kind = SignalKind.Square;
                    sig.dc = 0f;
                    sig.high = CircuitModel.Rail3V3;
                    sig.freqHz = 1000f;
                    sig.duty = duty;
                }
                else if (drive == GpioMode.OutHigh)
                {
                    sig.kind = SignalKind.High;
                    sig.high = CircuitModel.Rail3V3;
                }
                else if (drive == GpioMode.OutLow)
                {
                    sig.kind = SignalKind.Dc;
                    sig.dc = 0f;
                }
            }
            Waveforms.Sample(sig, 0f, dt, buf);
            return false;
        }

        private bool TryLiveSignal(string node, float[] buf, float window)
        {
            if (!node.StartsWith("gpio:")) return false;
            if (!int.TryParse(node.Substring(5), out var bcm)) return false;
            if (!_probes.TryGetValue(bcm, out var pv)) return false;
            int n = buf.Length;
            if (pv.edges != null && pv.edges.Count > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    float ago = window * (1f - i / (float)(n - 1));
                    buf[i] = EdgeLevel(pv.edges, ago)
                        * CircuitModel.Rail3V3;
                }
                return true;
            }
            float dt = window / buf.Length;
            if (pv.freqHz.HasValue && pv.duty.HasValue)
            {
                Waveforms.Sample(new NetSignal
                {
                    kind = SignalKind.Square,
                    dc = 0f,
                    high = CircuitModel.Rail3V3,
                    freqHz = pv.freqHz.Value,
                    duty = pv.duty.Value,
                }, 0f, dt, buf);
                return true;
            }
            if (pv.dcVolts.HasValue)
            {
                Waveforms.Sample(new NetSignal
                {
                    kind = SignalKind.Dc,
                    dc = pv.dcVolts.Value,
                }, 0f, dt, buf);
                return true;
            }
            return false;
        }

        /// <summary>Level s seconds ago: the most recent edge at or
        /// before that moment; oldest level backfills earlier time.</summary>
        private static float EdgeLevel(List<Vector2> edges, float s)
        {
            float bestAge = float.MaxValue;
            float lvl = edges[edges.Count - 1].y;
            foreach (var e in edges)
            {
                if (e.x >= s && e.x < bestAge)
                {
                    bestAge = e.x;
                    lvl = e.y;
                }
            }
            return lvl;
        }

        /// <summary>Same copper? Canonical equality plus a BFS over
        /// committed wires (few wires; runs on demand only).</summary>
        private bool SameNet(string a, string b)
        {
            if (a == b) return true;
            a = CircuitModel.Canonical(a);
            b = CircuitModel.Canonical(b);
            if (a == b) return true;
            var seen = new HashSet<string> { a };
            var q = new Queue<string>();
            q.Enqueue(a);
            var wires = PinModeController.Instance?.GetComponent<
                WireTool>()?.Wires;
            if (wires == null) return false;
            while (q.Count > 0)
            {
                string cur = q.Dequeue();
                foreach (var w in wires)
                {
                    string wa = CircuitModel.Canonical(w.a);
                    string wb = CircuitModel.Canonical(w.b);
                    string nxt = null;
                    if (wa == cur) nxt = wb;
                    else if (wb == cur) nxt = wa;
                    if (nxt == null || !seen.Add(nxt)) continue;
                    if (nxt == b) return true;
                    q.Enqueue(nxt);
                }
            }
            return false;
        }

        private bool LedPair(string red, string black,
            out string partId, out float milliamps, out bool redAnode)
        {
            partId = null;
            milliamps = 0f;
            redAnode = true;
            if (string.IsNullOrEmpty(red) || string.IsNullOrEmpty(black))
                return false;
            try
            {
                foreach (var p in Workspace.WorkspacePhysics.PartsSnapshot())
                {
                    if (p == null || p.type != "LED") continue;
                    string an = "comp:" + p.partId + ":0";
                    string ca = "comp:" + p.partId + ":1";
                    bool rAn = SameNet(red, an);
                    bool rCa = SameNet(red, ca);
                    bool bAn = SameNet(black, an);
                    bool bCa = SameNet(black, ca);
                    if ((rAn && bCa) || (rCa && bAn))
                    {
                        partId = p.partId;
                        float i = 0f;
                        Model?.LedMilliamps.TryGetValue(p.partId, out i);
                        milliamps = (rAn && bCa) ? i : -i;
                        redAnode = rAn && bCa;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ── refresh ──────────────────────────────────────────

        public void RefreshNow()
        {
            if (!_bound) return;
            EnsureModel();
            UpdateProbeSummary();
            if (CurrentMode == Mode.Scope) RefreshScope();
            else if (CurrentMode == Mode.Meter) RefreshMeter();
            else RefreshAnalyser();
        }

        private void UpdateProbeSummary()
        {
            Set("ProbeSummary", "S:" + ShortNet(_scopePen?.attachedNode)
                + " R:" + ShortNet(_redPen?.attachedNode)
                + " B:" + ShortNet(_blackPen?.attachedNode));
            Set("MeterProbes", "R:" + ShortNet(_redPen?.attachedNode)
                + " B:" + ShortNet(_blackPen?.attachedNode));
        }

        private void RefreshScope()
        {
            float window = TimeDivsMs[_tDivIdx] / 1000f * 10f;
            if (_running)
            {
                _scopeLive = SignalForNet(
                    _scopePen?.attachedNode, _ch1, window);
                SignalForNet(_redPen?.attachedNode, _ch2, window);
            }
            Waveforms.Measure(_ch1, window / Waveforms.BufN,
                out var pp, out var freq, out var duty, out var dc,
                out _);
            // Alignment is display-only: measure the raw signal so a
            // broken rotation seam can never change the readout, and
            // never re-align a frozen buffer (it would drift).
            if (_running)
                Waveforms.TriggerAlign(_ch1, TrigLevels[_trigIdx],
                    _trigRising, _scratch);
            DrawScope();
            Set("Legend1", "CH1 solid · "
                + ShortNet(_scopePen?.attachedNode));
            Set("Legend2", "CH2 dashed · "
                + ShortNet(_redPen?.attachedNode));
            string stats;
            if (pp < 0.05f)
            {
                stats = "DC " + dc.ToString("F2") + " V";
            }
            else
            {
                stats = "Vpp " + pp.ToString("F2") + " V · "
                    + Waveforms.FormatFreq(freq) + " · D"
                    + Mathf.RoundToInt(duty * 100f) + "%";
            }
            float dtMs = window * 500f;
            stats += " · dt " + dtMs.ToString("F1") + "ms";
            if (_scopeLive) stats += " · sampled";
            Set("StatsLine", stats);
            SetChip(_scopeView.transform, "VDivChip",
                VoltsDivs[_vDivIdx] + " V/div");
            SetChip(_scopeView.transform, "TDivChip",
                TimeDivsMs[_tDivIdx] + " ms/div");
            SetChip(_scopeView.transform, "TrigChip",
                "T " + TrigLevels[_trigIdx].ToString("F2") + "V");
            SetChip(_scopeView.transform, "EdgeChip",
                _trigRising ? "rising" : "falling");
            SetChip(_scopeView.transform, "RunChip",
                _running ? "Stop" : "Run");
        }

        private void DrawScope()
        {
            const int W = 550;
            const int H = 130;
            var bg = new Color32(0, 0, 0, 255);
            var grid = new Color32(255, 255, 255, 31);
            var c1 = new Color32(0x5C, 0xE1, 0xE6, 255);
            var c2 = new Color32(0xFF, 0xB4, 0x54, 255);
            var cur = new Color32(255, 255, 255, 64);
            for (int i = 0; i < W * H; i++) _scopePx[i] = bg;
            for (int x = 0; x < W; x += 55)
                for (int y = 0; y < H; y++) _scopePx[y * W + x] = grid;
            for (int y = 0; y < H; y += 16)
                for (int x = 0; x < W; x++) _scopePx[y * W + x] = grid;
            float pxPerV = (H / 8f) / VoltsDivs[_vDivIdx];
            // Texture row 0 is the BOTTOM: +v goes up.
            for (int x = 0; x < W; x++)
            {
                int i = x * (Waveforms.BufN - 1) / (W - 1);
                int y1 = Mathf.Clamp(
                    Mathf.RoundToInt(H / 2f + _ch1[i] * pxPerV),
                    0, H - 1);
                _scopePx[y1 * W + x] = c1;
                if ((x / 6) % 2 == 0)
                {
                    int y2 = Mathf.Clamp(
                        Mathf.RoundToInt(H / 2f + _ch2[i] * pxPerV),
                        0, H - 1);
                    _scopePx[y2 * W + x] = c2;
                }
            }
            for (int y = 0; y < H; y++)
            {
                _scopePx[y * W + W / 4] = cur;
                _scopePx[y * W + 3 * W / 4] = cur;
            }
            int ty = Mathf.Clamp(
                Mathf.RoundToInt(H / 2f + TrigLevels[_trigIdx] * pxPerV),
                0, H - 1);
            for (int x = 0; x < 7; x++) _scopePx[ty * W + x] = c1;
            _scopeTex.SetPixels32(_scopePx);
            _scopeTex.Apply(false, false);
        }

        private void RefreshMeter()
        {
            string r = _redPen?.attachedNode;
            string b = _blackPen?.attachedNode;
            float vr = Vdc(r);
            float vb = string.IsNullOrEmpty(b) ? 0f : Vdc(b);
            _meterLive = IsLiveNet(r) || IsLiveNet(b);
            string reading = "—";
            string sub = "";
            switch (CurrentMeter)
            {
                case MeterMode.Vdc:
                    reading = Waveforms.FormatVolts(vr - vb);
                    sub = "DC volts · Auto range";
                    break;
                case MeterMode.Vac:
                    SignalForNet(r, _vacBuf, 0.02f);
                    Waveforms.Measure(_vacBuf, 0.02f / _vacBuf.Length,
                        out _, out _, out _, out _,
                        out var ac);
                    reading = Waveforms.FormatVolts(ac);
                    sub = "AC volts RMS · Auto range";
                    break;
                case MeterMode.Amp:
                    if (LedPair(r, b, out _, out var ma, out _))
                    {
                        reading = ma.ToString("F1") + " mA";
                        sub = "LED branch current";
                    }
                    else
                    {
                        sub = "span an LED branch";
                    }
                    break;
                case MeterMode.Watt:
                    if (LedPair(r, b, out _, out var ma2, out _))
                    {
                        reading = ((vr - vb) * ma2 / 1000f)
                            .ToString("F3") + " W";
                        sub = "LED branch power";
                    }
                    else
                    {
                        sub = "span an LED branch";
                    }
                    break;
                case MeterMode.Ohm:
                    if (!string.IsNullOrEmpty(r)
                        && !string.IsNullOrEmpty(b))
                    {
                        if (SameNet(r, b)) reading = "0.2 ohm";
                        else if (LedPair(r, b, out _, out _,
                            out var an)) reading = an ? "1.9 Vf" : "OL";
                        else reading = "OL";
                    }
                    sub = "resistance · Auto range";
                    break;
                case MeterMode.Cont:
                    if (!string.IsNullOrEmpty(r)
                        && !string.IsNullOrEmpty(b)
                        && SameNet(r, b))
                    {
                        reading = "BEEP";
                        sub = "continuity · beep";
                    }
                    else
                    {
                        reading = "OPEN";
                        sub = "continuity · beep";
                    }
                    break;
                case MeterMode.Diode:
                    if (LedPair(r, b, out _, out _,
                        out var redAn))
                        reading = redAn ? "1.90 V" : "OL";
                    else reading = "OL";
                    sub = "diode drop, red on anode";
                    break;
            }
            if (_meterLive) sub += " · sampled";
            Set("MeterReading", reading);
            Set("MeterSub", sub);
        }

        private void RefreshAnalyser()
        {
            string net = _scopePen?.attachedNode;
            float window = TimeDivsMs[_tDivIdx] / 1000f * 10f;
            SignalForNet(net, _ch1, window);
            SignalForNet(net, _vacBuf, 0.01f);
            for (int i = 0; i < 64; i++)
                _anaRe[i] = _vacBuf[i * Waveforms.BufN / 64];
            Waveforms.Fft64(_anaRe, _anaMag);
            Waveforms.Measure(_ch1, window / Waveforms.BufN,
                out var pp, out var freq, out var duty, out var dc,
                out _);
            int peak = 1;
            for (int i = 2; i < _anaMag.Length; i++)
                if (_anaMag[i] > _anaMag[peak]) peak = i;
            float peakHz = peak * 100f; // 64 pts over 10 ms
            string cls = Waveforms.Classify(pp, freq, duty);
            if (cls.StartsWith("PWM"))
                cls = "PWM " + Waveforms.FormatFreq(freq);
            else if (cls == "DC")
                cls = "DC " + dc.ToString("F2") + " V";
            Set("AnalyserClass", cls);
            Set("AnalyserPeak", pp < 0.05f ? "no tone"
                : "peak " + Waveforms.FormatFreq(peakHz));
            DrawSpectrum();
        }

        private void DrawSpectrum()
        {
            const int W = 550;
            const int H = 130;
            var bg = new Color32(0, 0, 0, 255);
            var grid = new Color32(255, 255, 255, 31);
            var bar = new Color32(0x5C, 0xE1, 0xE6, 255);
            var pk = new Color32(0xFF, 0xB4, 0x54, 255);
            for (int i = 0; i < W * H; i++) _anaPx[i] = bg;
            for (int y = 0; y < H; y += 16)
                for (int x = 0; x < W; x++) _anaPx[y * W + x] = grid;
            float max = 0.001f;
            foreach (var m in _anaMag) if (m > max) max = m;
            int bars = Mathf.Min(32, _anaMag.Length);
            int peak = 1;
            for (int i = 2; i < bars; i++)
                if (_anaMag[i] > _anaMag[peak]) peak = i;
            for (int i = 0; i < bars; i++)
            {
                int h = Mathf.RoundToInt(
                    _anaMag[i] / max * (H - 8));
                int x0 = 8 + i * (W - 16) / bars;
                int x1 = 8 + (i + 1) * (W - 16) / bars - 2;
                for (int x = x0; x < x1; x++)
                    for (int y = 0; y < h; y++)
                        _anaPx[y * W + x] = i == peak ? pk : bar;
            }
            _anaTex.SetPixels32(_anaPx);
            _anaTex.Apply(false, false);
        }

        // ── scope controls ───────────────────────────────────

        public void CycleVDiv()
        {
            _vDivIdx = (_vDivIdx + 1) % VoltsDivs.Length;
            RefreshNow();
        }

        public void CycleTDiv()
        {
            _tDivIdx = (_tDivIdx + 1) % TimeDivsMs.Length;
            RefreshNow();
        }

        public void CycleTrig()
        {
            _trigIdx = (_trigIdx + 1) % TrigLevels.Length;
            RefreshNow();
        }

        public void ToggleEdge()
        {
            _trigRising = !_trigRising;
            RefreshNow();
        }

        public void ToggleRun()
        {
            _running = !_running;
            RefreshNow();
        }

        public void AutoSetup()
        {
            float window = TimeDivsMs[_tDivIdx] / 1000f * 10f;
            SignalForNet(_scopePen?.attachedNode, _ch1, window);
            Waveforms.Measure(_ch1, window / Waveforms.BufN,
                out var pp, out var freq, out _, out var dc,
                out _);
            _vDivIdx = 1;
            for (int i = 0; i < VoltsDivs.Length; i++)
                if (VoltsDivs[i] * 6f >= pp) { _vDivIdx = i; break; }
                else _vDivIdx = i;
            if (pp >= 0.05f && freq > 0f)
            {
                float wantMs = 2f / freq / 10f * 1000f;
                _tDivIdx = 0;
                float best = float.MaxValue;
                for (int i = 0; i < TimeDivsMs.Length; i++)
                {
                    float d = Mathf.Abs(TimeDivsMs[i] - wantMs);
                    if (d < best) { best = d; _tDivIdx = i; }
                }
                float mid = dc;
                _trigIdx = 0;
                best = float.MaxValue;
                for (int i = 0; i < TrigLevels.Length; i++)
                {
                    float d = Mathf.Abs(TrigLevels[i] - mid);
                    if (d < best) { best = d; _trigIdx = i; }
                }
            }
            RefreshNow();
        }

        // ── twin live path ───────────────────────────────────

        private static ITwinLink TwinLink()
        {
            try
            {
                if (BenchSettings.DemoMode) return null;
                var b = UnityEngine.Object.FindFirstObjectByType<
                    TwinBridge>(FindObjectsInactive.Include);
                if (b != null && b.Connected) return b;
            }
            catch { }
            return null;
        }

        private void SubscribeTwin()
        {
            if (_twinSub != null) return;
            var link = TwinLink();
            if (link == null) return;
            _twinSub = link;
            _twinSub.OnSnapshot += OnSnapshot;
        }

        private void PushProbes()
        {
            SubscribeTwin();
            var link = _twinSub ?? TwinLink();
            if (link == null) return;
            var bcms = new List<int>();
            foreach (var pen in new[] { _scopePen, _redPen, _blackPen })
            {
                string n = pen?.attachedNode;
                if (string.IsNullOrEmpty(n) || !n.StartsWith("gpio:"))
                    continue;
                if (int.TryParse(n.Substring(5), out var bcm)
                    && !bcms.Contains(bcm))
                    bcms.Add(bcm);
            }
            try { link.SetProbes(bcms.ToArray(), 2f); }
            catch { }
        }

        private void OnSnapshot(JObject msg)
        {
            try
            {
                var arr = msg["probes"] as JArray;
                if (arr == null) return;
                foreach (var p in arr)
                {
                    try
                    {
                        var pv = TwinBridge.ParseProbe(p);
                        _probes[pv.pin] = pv;
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Harness + external feeders: inject a probe view.</summary>
        public void InjectProbe(TwinBridge.ProbeView pv)
        {
            _probes[pv.pin] = pv;
            RefreshNow();
        }

        private bool IsLiveNet(string node)
        {
            if (string.IsNullOrEmpty(node) || !node.StartsWith("gpio:"))
                return false;
            return int.TryParse(node.Substring(5), out var bcm)
                && _probes.ContainsKey(bcm);
        }

        // ── text + format helpers ────────────────────────────

        private void Set(string name, string s)
        {
            if (_txt.TryGetValue(name, out var t) && t != null)
                t.text = s;
        }

        private static void SetChip(Transform root, string name, string s)
        {
            var t = root.Find(name)?.GetComponentInChildren<
                TMPro.TMP_Text>(true);
            if (t != null) t.text = s;
        }

        public static string ShortNet(string node)
        {
            if (string.IsNullOrEmpty(node)) return "—";
            if (node.StartsWith("gpio:")) return "GPIO" + node.Substring(5);
            if (node.StartsWith("power:"))
            {
                if (int.TryParse(node.Substring(6), out var phys))
                    foreach (var p in TwinCatalog.Header40)
                        if (p.physical == phys) return p.name;
                return node;
            }
            if (node.StartsWith("term:"))
            {
                var parts = node.Substring(5).Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var r)
                    && int.TryParse(parts[1], out var c))
                    return TwinBreadboard.NetId(r, c);
            }
            if (node.StartsWith("comp:"))
            {
                var parts = node.Split(':');
                if (parts.Length == 3) return "leg " + parts[2];
            }
            return node;
        }

    }
}

