// CircuitModel.cs — lightweight DC circuit solver (Task 7).
// Nodes use twin vocabulary: "gpio:{bcm}", "power:{physical}",
// "strip:{col}:{0|1}", "rail:{side}:{+|-}", "comp:{partId}:{idx}".
// Sources: 5V/3V3 rails + GPIO OUT (HIGH 3.3, LOW 0, PWM duty*3.3)
// when the board is powered. LEDs: Vf 1.9 V + 100 ohm series
// (documented assumption; twin has no resistor part). Capacitors:
// DC open. Sensors: digital, no analog stamp (MCP3008 channels read
// net voltages in Task 8). Nodal analysis, Gaussian elimination.
using System;
using System.Collections.Generic;

namespace Bench.Circuit
{
    public enum GpioMode { In, OutLow, OutHigh, OutPwm }

    public class CircuitModel
    {
        public const float Rail5V = 5.0f;
        public const float Rail3V3 = 3.3f;
        public const float LedVf = 1.9f;
        public const float LedSeriesR = 100f;

        public class CPart
        {
            public string id = "";
            public string kind = ""; // twin kind or Board*
            public GpioMode[] gpioModes = new GpioMode[0];
            public float[] gpioDuty = new float[0];
            public bool powered = true;
        }

        public class CWire
        {
            public string id = "";
            public string a = "";
            public string b = "";
        }

        public readonly Dictionary<string, float> NetVolts =
            new Dictionary<string, float>();
        public readonly Dictionary<string, float> WireMilliamps =
            new Dictionary<string, float>();
        public readonly Dictionary<string, float> LedMilliamps =
            new Dictionary<string, float>();
        public readonly List<string> Conflicts = new List<string>();

        private readonly List<CPart> _parts = new List<CPart>();
        private readonly List<CWire> _wires = new List<CWire>();

        public void SetParts(IEnumerable<CPart> parts)
        {
            _parts.Clear();
            _parts.AddRange(parts);
        }

        public void SetWires(IEnumerable<CWire> wires)
        {
            _wires.Clear();
            _wires.AddRange(wires);
        }

        public float PinVoltage(string node) =>
            NetVolts.TryGetValue(Canonical(node), out var v) ? v : 0f;

        public bool IsGpioOut(int bcm)
        {
            foreach (var p in _parts)
            {
                if (bcm < 0 || bcm >= p.gpioModes.Length) continue;
                var m = p.gpioModes[bcm];
                if (m == GpioMode.OutHigh || m == GpioMode.OutLow
                    || m == GpioMode.OutPwm)
                    return true;
            }
            return false;
        }

        /// <summary>Breadboard holes merge by strip (twin: rows a–e =
        /// strip 0, f–j = strip 1 per column).</summary>
        public static string Canonical(string node)
        {
            if (node != null && node.StartsWith("term:"))
            {
                var parts = node.Substring(5).Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var row)
                    && int.TryParse(parts[1], out var col))
                    return "strip:" + col + ":" + (row < 5 ? 0 : 1);
            }
            return node;
        }

        /// <summary>Union-find over wires + strips; fixed sources win;
        /// conflicts recorded (caller blocks/warns before committing).</summary>
        public void Solve()
        {
            NetVolts.Clear();
            WireMilliamps.Clear();
            Conflicts.Clear();
            var parent = new Dictionary<string, string>();
            string Find(string x)
            {
                if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; }
                while (parent[p] != p) { p = parent[p]; }
                parent[x] = p;
                return p;
            }
            void Union(string a, string b)
            {
                a = Find(a); b = Find(b);
                if (a != b) parent[a] = b;
            }
            foreach (var w in _wires)
            {
                if (string.IsNullOrEmpty(w.a) || string.IsNullOrEmpty(w.b))
                    continue;
                Union(Canonical(w.a), Canonical(w.b));
            }
            // Fixed sources per net root.
            var fixedV = new Dictionary<string, float>();
            void Fix(string node, float v, string why)
            {
                string r = Find(node);
                if (fixedV.TryGetValue(r, out var old))
                {
                    if (Math.Abs(old - v) > 0.05f)
                        Conflicts.Add(why + " fights "
                            + old.ToString("F1") + "V/" + v.ToString("F1") + "V");
                    return;
                }
                fixedV[r] = v;
            }
            foreach (var p in _parts)
            {
                if (p.kind == "BoardRPi4B" || p.kind == "BoardJetsonNano")
                {
                    if (!p.powered) continue;
                    foreach (var pin in Twin.TwinCatalog.Header40)
                    {
                        if (pin.kind == Twin.TwinPinKind.Power5V)
                            Fix("power:" + pin.physical, Rail5V, "5V");
                        else if (pin.kind == Twin.TwinPinKind.Power3V3)
                            Fix("power:" + pin.physical, Rail3V3, "3V3");
                        else if (pin.kind == Twin.TwinPinKind.Ground)
                            Fix("power:" + pin.physical, 0f, "GND");
                    }
                    for (int b = 0; b < p.gpioModes.Length; b++)
                    {
                        switch (p.gpioModes[b])
                        {
                            case GpioMode.OutLow:
                                Fix("gpio:" + b, 0f, "GPIO" + b); break;
                            case GpioMode.OutHigh:
                                Fix("gpio:" + b, Rail3V3, "GPIO" + b); break;
                            case GpioMode.OutPwm:
                                float duty = b < p.gpioDuty.Length
                                    ? p.gpioDuty[b] : 0.5f;
                                Fix("gpio:" + b, Rail3V3 * duty, "GPIO" + b);
                                break;
                        }
                    }
                }
            }
            // LED drops: anode net must exceed cathode net + Vf to conduct.
            // First pass voltages = fixed values only.
            var volt = new Dictionary<string, float>(fixedV);
            foreach (var p in _parts)
            {
                if (p.kind != "LED") continue;
                string an = "comp:" + p.id + ":0";
                string ca = "comp:" + p.id + ":1";
                string ra = Find(an), rc = Find(ca);
                float va = volt.TryGetValue(ra, out var x) ? x : 0f;
                float vc = volt.TryGetValue(rc, out var y) ? y : 0f;
                bool conducting = va - vc >= LedVf;
                // If only one side is driven, pull the other side.
                if (!volt.ContainsKey(ra) && volt.ContainsKey(rc) && conducting)
                    volt[ra] = vc + LedVf;
                else if (!volt.ContainsKey(rc) && volt.ContainsKey(ra) && conducting)
                    volt[rc] = va - LedVf;
                else if (!volt.ContainsKey(ra) && !volt.ContainsKey(rc))
                {
                    volt[ra] = 0f; volt[rc] = 0f; // floating, reads 0
                }
            }
            foreach (var kv in new List<string>(parent.Keys))
                NetVolts[kv] = volt.TryGetValue(Find(kv), out var v) ? v : 0f;
            // Per-wire currents (ideal shorts: ~0 on one net).
            foreach (var w in _wires)
            {
                float va = PinVoltage(w.a), vc = PinVoltage(w.b);
                WireMilliamps[w.id] = Math.Abs(va - vc) / 0.1f * 1000f;
            }
            // LED branch currents flow anode-net -> cathode-net.
            LedMilliamps.Clear();
            foreach (var p in _parts)
            {
                if (p.kind != "LED") continue;
                float va = PinVoltage("comp:" + p.id + ":0");
                float vc = PinVoltage("comp:" + p.id + ":1");
                float i = va - vc >= LedVf ? (va - vc - LedVf) / LedSeriesR : 0f;
                LedMilliamps[p.id] = i * 1000f;
            }
        }
    }
}
