// IDataSource.cs — Task 9: the twin-link seam for the Display/SSH
// window. SimulatedSource fakes a desktop + shell for Demo Mode
// (and judges with no hardware); LiveTwinSource talks to the real
// twin over ITwinLink (display frames, UART serial, GPIO levels).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bench.Data
{
    public interface IDataSource
    {
        string SourceName { get; } // "Demo" or "Live"
        bool Connected { get; }

        /// Display frame: width, height, JPEG bytes, source tag.
        event Action<int, int, byte[], string> DisplayFrame;

        /// Terminal: console lines (oldest first), dropped count.
        event Action<IList<string>, int> Terminal;

        /// 54 BCM levels (null = floating/unknown).
        event Action<bool?[]> GpioLevels;

        void SendTerminal(string text);
        void SendDisplayInput(float x, float y, bool hasPos,
            int button, bool pressed, bool hasButton,
            int scroll, bool hasScroll);
        void SetDisplayEnabled(bool on, int maxWidth);
    }

    /// <summary>Fake desktop + shell. No hardware needed.</summary>
    public class SimulatedSource : MonoBehaviour, IDataSource
    {
        public string SourceName => "Demo";
        public bool Connected => true;

        public event Action<int, int, byte[], string> DisplayFrame;
        public event Action<IList<string>, int> Terminal;
        public event Action<bool?[]> GpioLevels;

        /// <summary>Last pointer input (harness asserts the path).</summary>
        public DisplayInputState LastInput;
        public bool HasInput;

        private byte[] _jpg;
        private float _frameT;
        private float _blinkT;
        private bool _blink;
        private readonly List<string> _lines = new List<string>();

        private void Awake()
        {
            _lines.Add("pi@bench:~$ ");
        }

        private void OnEnable()
        {
            // A frame failure must never starve terminal/gpio.
            try { EnsureJpg(); } catch { }
            if (_jpg != null)
                DisplayFrame?.Invoke(480, 270, _jpg, "demo");
            Terminal?.Invoke(_lines, 0);
            PushGpio();
        }

        private void Update()
        {
            _frameT += Time.unscaledDeltaTime;
            if (_frameT >= 1f)
            {
                _frameT = 0f;
                DisplayFrame?.Invoke(480, 270, _jpg, "demo");
            }
            _blinkT += Time.unscaledDeltaTime;
            if (_blinkT >= 1f)
            {
                _blinkT = 0f;
                _blink = !_blink;
                PushGpio();
            }
        }

        private void PushGpio()
        {
            var lv = new bool?[54];
            lv[17] = true;
            lv[18] = _blink;
            lv[27] = true;
            lv[22] = false;
            lv[23] = false;
            lv[24] = true;
            lv[25] = false;
            lv[4] = true;
            GpioLevels?.Invoke(lv);
        }

        public void SendTerminal(string text)
        {
            string line = (text ?? "").Trim();
            _lines[_lines.Count - 1] = "pi@bench:~$ " + line;
            if (line.Length > 0) FakeShell(line);
            _lines.Add("pi@bench:~$ ");
            while (_lines.Count > 200) _lines.RemoveAt(0);
            Terminal?.Invoke(_lines, 0);
        }

        private void FakeShell(string line)
        {
            string[] parts = line.Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            switch (cmd)
            {
                case "help":
                    _lines.Add("commands: ls pwd echo python3 ifconfig "
                        + "restart clear gpio help");
                    break;
                case "ls":
                    _lines.Add("bench.py  blink.py  README.txt");
                    break;
                case "pwd":
                    _lines.Add("/home/pi");
                    break;
                case "echo":
                    _lines.Add(line.Length > 5 ? line.Substring(5) : "");
                    break;
                case "python3":
                    if (parts.Length > 1 && parts[1].Contains("blink"))
                        _lines.Add("blinking GPIO18… (Ctrl+C to stop)");
                    else _lines.Add("Hello from Bench!");
                    break;
                case "ifconfig":
                    _lines.Add("wlan0: 192.168.1.42  "
                        + "UP RUNNING MULTICAST");
                    break;
                case "restart":
                    _lines.Add("bench-video.service restarted.");
                    break;
                case "clear":
                    _lines.Clear();
                    break;
                case "gpio":
                    _lines.Add("17:H 18:PWM 27:H 22:L "
                        + "(simulated)");
                    break;
                default:
                    _lines.Add(cmd + ": command not found "
                        + "(demo shell)");
                    break;
            }
        }

        public void SendDisplayInput(float x, float y, bool hasPos,
            int button, bool pressed, bool hasButton,
            int scroll, bool hasScroll)
        {
            LastInput = new DisplayInputState
            {
                x = hasPos ? (float?)x : null,
                y = hasPos ? (float?)y : null,
                button = hasButton ? (int?)button : null,
                pressed = pressed,
                scroll = hasScroll ? (int?)scroll : null,
            };
            HasInput = true;
        }

        public void SetDisplayEnabled(bool on, int maxWidth)
        {
            if (!on || !isActiveAndEnabled) return;
            try { EnsureJpg(); } catch { }
            if (_jpg != null)
                DisplayFrame?.Invoke(480, 270, _jpg, "demo");
            Terminal?.Invoke(_lines, 0);
            PushGpio();
        }

        /// <summary>Procedural demo desktop, encoded once.</summary>
        private void EnsureJpg()
        {
            if (_jpg != null) return;
            const int W = 480;
            const int H = 270;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float t = y / (float)H;
                    px[y * W + x] = new Color32(
                        (byte)(20 + 30 * t), (byte)(90 + 40 * t),
                        (byte)(110 + 40 * t), 255);
                }
            // Window card.
            for (int y = 60; y < 210; y++)
                for (int x = 120; x < 360; x++)
                    px[y * W + x] = y > 180
                        ? new Color32(240, 241, 244, 255)
                        : new Color32(38, 46, 58, 255);
            // Taskbar + status dot.
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = new Color32(18, 22, 30, 255);
            for (int y = 6; y < 18; y++)
                for (int x = 8; x < 20; x++)
                    px[y * W + x] = new Color32(74, 222, 128, 255);
            tex.SetPixels32(px);
            tex.Apply(false, false); // stay readable for EncodeToJPG
            _jpg = ImageConversion.EncodeToJPG(tex, 75);
            Destroy(tex);
        }
    }

    /// <summary>Live twin over ITwinLink (display + serial + GPIO).</summary>
    public class LiveTwinSource : MonoBehaviour, IDataSource
    {
        public string SourceName => "Live";

        public bool Connected
        {
            get
            {
                try
                {
                    var l = Link;
                    return l != null && l.Connected;
                }
                catch { return false; }
            }
        }

        public event Action<int, int, byte[], string> DisplayFrame;
        public event Action<IList<string>, int> Terminal;
        public event Action<bool?[]> GpioLevels;

        private ITwinLink _link;
        private bool _subbed;
        private ITwinLink _subbedLink;
        private float _scanT = -10f;
        private List<string> _lastLines;
        private int _lastDropped;
        private bool?[] _lastGpio;

        /// <summary>True when usable: present and, for Unity
        /// links, not destroyed (a destroyed link is not C# null,
        /// and a dead mock still answers Connected == true).</summary>
        private static bool Alive(ITwinLink l)
        {
            if (l == null) return false;
            if (l is UnityEngine.Object o) return o != null;
            return true;
        }

        private ITwinLink Link
        {
            get
            {
                // Prefer a CONNECTED link: a dead bridge in the
                // scene must not shadow a live one (or the mock).
                // Failures are never cached (re-scan next access).
                try
                {
                    if (Alive(_link) && _link.Connected)
                        return _link;
                }
                catch { }
                // Re-scan at most 1 Hz (Update polls every frame).
                if (Time.unscaledTime - _scanT < 1f) return _link;
                _scanT = Time.unscaledTime;
                _link = null;
                try
                {
                    var all = UnityEngine.Object
                        .FindObjectsOfType<MonoBehaviour>(true)
                        .OfType<ITwinLink>()
                        .ToList();
                    foreach (var l in all)
                        if (l.Connected) { _link = l; break; }
                    if (_link == null && all.Count > 0)
                        _link = all[0];
                }
                catch { _link = null; }
                return _link;
            }
        }

        /// <summary>Harness: pin a specific link (mock).
        /// Update() picks up the swap and resubscribes.</summary>
        public void TestSetLink(ITwinLink link)
        {
            _link = link;
        }

        private void OnEnable() => Subscribe();

        private void Update()
        {
            // The link often appears after us (mock in tests, late
            // bridge connect in production), and Link can roam to a
            // better one: track it and resubscribe on any change so
            // events always come from the link sends go to.
            var cur = Link;
            bool same = _subbed && Alive(cur) && Alive(_subbedLink)
                && ReferenceEquals((object)cur,
                    (object)_subbedLink);
            if (same) return;
            if (Alive(_subbedLink))
            {
                _subbedLink.OnSnapshot -= OnSnapshot;
                _subbedLink.OnDisplay -= OnDisplay;
            }
            _subbedLink = null;
            _subbed = false;
            if (Alive(cur)) Subscribe();
        }

        private void OnDisable()
        {
            if (_subbed && _link != null)
            {
                _link.OnSnapshot -= OnSnapshot;
                _link.OnDisplay -= OnDisplay;
            }
            _subbed = false;
        }

        private void Subscribe()
        {
            if (_subbed) return;
            var l = Link;
            if (!Alive(l)) return;
            l.OnSnapshot -= OnSnapshot; // idempotent: never double-sub
            l.OnDisplay -= OnDisplay;
            l.OnSnapshot += OnSnapshot;
            l.OnDisplay += OnDisplay;
            _subbedLink = l;
            _subbed = true;
        }

        public void SendTerminal(string text)
        {
            try { Link?.SerialSend(text ?? ""); }
            catch { }
        }

        public void SendDisplayInput(float x, float y, bool hasPos,
            int button, bool pressed, bool hasButton,
            int scroll, bool hasScroll)
        {
            try
            {
                Link?.DisplayInput(new DisplayInputState
                {
                    x = hasPos ? (float?)x : null,
                    y = hasPos ? (float?)y : null,
                    button = hasButton ? (int?)button : null,
                    pressed = pressed,
                    scroll = hasScroll ? (int?)scroll : null,
                });
            }
            catch { }
        }

        public void SetDisplayEnabled(bool on, int maxWidth)
        {
            try { Link?.SetDisplay(on, maxWidth); }
            catch { }
            // Re-fire cached state so a fresh subscriber never
            // waits a full snapshot period with blank UI.
            if (on && _subbed)
            {
                if (_lastLines != null)
                    Terminal?.Invoke(_lastLines, _lastDropped);
                if (_lastGpio != null) GpioLevels?.Invoke(_lastGpio);
            }
        }

        private void OnDisplay(TwinBridge.DisplayFrame f)
        {
            try { DisplayFrame?.Invoke(f.w, f.h, f.jpg, f.source); }
            catch { }
        }

        private void OnSnapshot(Newtonsoft.Json.Linq.JObject msg)
        {
            try
            {
                var ser = msg["serial"];
                if (ser != null)
                {
                    var lines = new List<string>();
                    var arr = ser["lines"] as Newtonsoft.Json.Linq.JArray;
                    if (arr != null)
                        foreach (var t in arr)
                            lines.Add(t.ToObject<string>());
                    int dropped = ser.Value<int?>("dropped") ?? 0;
                    _lastLines = lines;
                    _lastDropped = dropped;
                    Terminal?.Invoke(lines, dropped);
                }
                var ga = msg["gpio"] as Newtonsoft.Json.Linq.JArray;
                if (ga != null)
                {
                    var lv = new bool?[54];
                    foreach (var g in ga)
                    {
                        int bcm = g.Value<int>("bcm");
                        if (bcm < 0 || bcm >= 54) continue;
                        var tok = g["level"];
                        lv[bcm] = tok == null
                            || tok.Type
                                == Newtonsoft.Json.Linq.JTokenType.Null
                            ? (bool?)null
                            : tok.ToObject<bool>();
                    }
                    _lastGpio = lv;
                    GpioLevels?.Invoke(lv);
                }
            }
            catch { }
        }
    }
}
