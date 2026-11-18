// TwinMockSource.cs — offline twin simulator. Implements the same
// ITwinLink the real TwinBridge does, so every screen runs fully in
// the Editor with no Swift app: a blinking LED on GPIO18, a servo
// sweeping, sensors with live values, serial boot output, and scope
// probes. Commands (addPart/addWire/setProbes/…) mutate the mock so
// the wiring flow round-trips exactly like the real twin.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class TwinMockSource : MonoBehaviour, ITwinLink
{
    public event Action<JObject> OnSnapshot;
    public event Action<JObject> OnAck;
    public event Action<TwinBridge.DisplayFrame> OnDisplay;

    public bool Connected => true; // always — it's local
    public double LastServerTime => Time.realtimeSinceStartupAsDouble;
    public double LastServerTimeLocalRecv => Time.realtimeSinceStartupAsDouble;
    public double ServerNow => Time.realtimeSinceStartupAsDouble;

    public float snapshotHz = 15f;
    public int[] probePins = { 18, 27 };
    public float probeWindow = 2f;

    class MPart
    {
        public string id, kind;
        public Vector3 posMm;       // twin space
        public float rotRad;
        public string[] labels;
        public string extra = "";
    }

    class MWire { public string id, a, b; }

    readonly List<MPart> parts = new();
    readonly List<MWire> wires = new();
    readonly Dictionary<int, bool?> overrides = new(); // manual gpio sets
    bool powered = true;
    string phase = "running";
    readonly List<string> serial = new()
    { "[    0.000000] Booting BCM2711 on 4 cores",
      "[    0.412881] Motion device registered",
      "[    0.502741] ram 2048MB", "pi@twin:~$ " };
    float nextSnap;
    int partCounter;

    void Start()
    {
        void Add(string kind, string id, Vector3 pos, string[] labels)
        {
            parts.Add(new MPart { id = id, kind = kind, posMm = pos,
                                  labels = labels });
        }
        Add("LED", "c0ffee00-0000-4000-8000-000000000001",
            new Vector3(70, 0, -40), new[] { "anode (+)", "cathode (−)" });
        Add("Servo", "c0ffee00-0000-4000-8000-000000000002",
            new Vector3(150, 0, 30), new[] { "signal", "VCC", "GND" });
        Add("Button", "c0ffee00-0000-4000-8000-000000000003",
            new Vector3(-60, 0, 60), new[] { "pin 1", "pin 2" });
        Add("BME280", "c0ffee00-0000-4000-8000-000000000004",
            new Vector3(-140, 0, -60), new[] { "VCC", "GND", "SCL", "SDA" });
        Add("MCP3008", "c0ffee00-0000-4000-8000-000000000005",
            new Vector3(40, 0, 90),
            new[] { "VDD", "GND", "CLK", "CS", "DIN", "DOUT",
                    "AGND", "CH0", "CH1", "CH2", "CH3", "CH4",
                    "CH5", "CH6", "CH7", "VREF" });
        Add("GPS", "c0ffee00-0000-4000-8000-000000000006",
            new Vector3(210, 0, -70), new[] { "VCC", "GND", "TX", "RX" });

        wires.Add(new MWire { id = "w1000", a = "gpio:18",
            b = "comp:c0ffee00-0000-4000-8000-000000000001:0" });
        wires.Add(new MWire { id = "w1001", a = "power:6",
            b = "comp:c0ffee00-0000-4000-8000-000000000001:1" });
        wires.Add(new MWire { id = "w1002", a = "gpio:17",
            b = "comp:c0ffee00-0000-4000-8000-000000000002:0" });
        Invoke(nameof(DropBootLine), 6f);
    }

    void DropBootLine()
    {
        serial.Add("[   " + Time.realtimeSinceStartup.ToString("F6").PadLeft(8) +
                   "] random: crng init done");
    }

    void Update()
    {
        if (Time.time < nextSnap) return;
        nextSnap = Time.time + 1f / snapshotHz;
        OnSnapshot?.Invoke(BuildSnapshot());
    }

    // ── simulated signals ────────────────────────────────────────
    bool GpioLevel(int bcm, out bool? level)
    {
        level = null;
        if (!powered) return true;
        if (overrides.TryGetValue(bcm, out var o)) { level = o; return true; }
        switch (bcm)
        {
            case 18: level = (Time.time * 30f) % 1f < 0.5f; return true;  // 30 Hz square
            case 17: level = true; return true;                          // servo power
            case 27: level = false; return true;                          // scope CH2 ref
            case 22: level = (Time.time % 2f) < 1f; return true;          // slow blink
            default: return false;                                       // input/floating
        }
    }

    JObject BuildSnapshot()
    {
        var snap = new JObject
        {
            ["type"] = "snapshot",
            ["v"] = 1,
            ["t"] = Time.realtimeSinceStartupAsDouble,
            ["powered"] = powered,
            ["phase"] = phase,
        };

        var partsArr = new JArray();
        foreach (var p in parts)
        {
            var levels = new JArray();
            bool? l0 = null;
            GpioLevel(18, out l0);
            switch (p.kind)
            {
                case "LED":
                    levels.Add(powered ? l0 : (bool?)null);
                    levels.Add(false); // cathode → gnd rail = low
                    break;
                default:
                    for (int i = 0; i < p.labels.Length; i++)
                        levels.Add(i == 0 && p.kind == "Servo" ? (bool?)true : null);
                    break;
            }
            var labels = new JArray();
            foreach (var l in p.labels) labels.Add(l);
            partsArr.Add(new JObject
            {
                ["id"] = p.id,
                ["kind"] = p.kind,
                ["pos"] = new JArray(p.posMm.x, p.posMm.y, p.posMm.z),
                ["rot"] = p.rotRad,
                ["labels"] = labels,
                ["levels"] = levels,
                ["extra"] = ExtraOf(p),
            });
        }
        snap["parts"] = partsArr;

        var sensors = new JArray();
        foreach (var p in parts)
        {
            var values = new JObject();
            switch (p.kind)
            {
                case "BME280":
                    values["tempC"] = 21.3 + Mathf.Sin(Time.time * .1f) * .4;
                    values["rh"] = 41 + Mathf.Sin(Time.time * .07f) * 2;
                    values["hPa"] = 1013.2;
                    break;
                case "GPS":
                    values["fix"] = "3D";
                    values["sats"] = 11;
                    break;
                case "Servo":
                    values["angle"] = ServoAngle();
                    break;
            }
            sensors.Add(new JObject { ["id"] = p.id, ["kind"] = p.kind,
                                      ["values"] = values });
        }
        snap["sensors"] = sensors;

        var wiresArr = new JArray();
        foreach (var w in wires)
            wiresArr.Add(new JObject
            {
                ["id"] = w.id, ["a"] = w.a, ["b"] = w.b,
                ["color"] = new JArray(92, 225, 230),
            });
        snap["wires"] = wiresArr;

        var gpioArr = new JArray();
        for (int b = 0; b < 54; b++)
        {
            bool? lvl = null;
            bool driven = GpioLevel(b, out lvl);
            gpioArr.Add(new JObject
            {
                ["bcm"] = b,
                ["level"] = lvl.HasValue ? (JToken)(bool)lvl : JValue.CreateNull(),
                ["mode"] = driven ? "output" : "input",
            });
        }
        snap["gpio"] = gpioArr;

        var serialObj = new JObject
        {
            ["lines"] = new JArray(serial.TakeLast(80)),
            ["dropped"] = 0,
        };
        snap["serial"] = serialObj;

        var probes = new JArray();
        foreach (var pin in probePins)
            probes.Add(ProbeView(pin));
        snap["probes"] = probes;

        return snap;
    }

    float ServoAngle() =>
        Mathf.PingPong(Time.time / 3f, 1f) * 180f - 90f;

    string ExtraOf(MPart p) => p.kind switch
    {
        "LED" => powered ? "ON " + ((int)((Time.time * 30f) % 1f * 100f)) + "%"
                         : "off",
        "Servo" => $"{ServoAngle():F0}°",
        "BME280" => $"{21.3 + Mathf.Sin(Time.time * .1f) * .4:F1} °C",
        "GPS" => "fix 3D · 11 sats",
        "MCP3008" => "CH0 1.65 V",
        "Button" => "",
        _ => "",
    };

    JObject ProbeView(int pin)
    {
        var edges = new JArray();
        if (powered)
        {
            if (pin == 18)
            {
                // 30 Hz square: edges back through the window
                float w = probeWindow;
                for (int i = 0; i < 60; i++)
                {
                    float age = i / 30f;
                    if (age > w) break;
                    edges.Add(new JArray(age, (i % 2) == 0 ? false : true));
                }
            }
            else if (pin == 22)
            {
                for (int i = 0; i < 4; i++)
                    edges.Add(new JArray(i * 2f % probeWindow, i % 2 == 0));
            }
            // 17/27: steady levels, no edges (flat traces)
        }
        return new JObject
        {
            ["pin"] = pin,
            ["edges"] = edges,
            ["duty"] = pin == 18 && powered ? 0.5 : JValue.CreateNull(),
            ["freqHz"] = pin == 18 && powered ? 30 : JValue.CreateNull(),
            ["dcVolts"] = pin == 18 && powered ? 1.66
                        : pin == 17 && powered ? 3.31
                        : pin == 27 ? 0.0 : JValue.CreateNull(),
        };
    }

    void Ack(string cmd, bool ok = true) =>
        OnAck?.Invoke(new JObject { ["type"] = "ack", ["cmd"] = cmd,
                                     ["ok"] = ok });

    // ── ITwinLink commands ───────────────────────────────────────
    public void Send(JObject cmd) { /* mock consumes commands directly */ }

    public void Hello() { }

    public void MovePart(string id, Vector3 unityMeters, float rotYUnity = 0)
    {
        var mm = TwinBridge.UnityToTwin(unityMeters);
        var p = parts.Find(x => x.id == id);
        if (p == null) return;
        p.posMm = mm;
        p.rotRad = -rotYUnity;
    }

    public void SetGpio(int bcm, bool level)
    {
        overrides[bcm] = level;
        Ack("setGpio");
    }

    public void PressButton(string id, bool held)
    {
        overrides[22] = held; // pressing the desk button lights GPIO22
        Ack("pressButton");
    }

    public void AddPart(string kind)
    {
        partCounter++;
        var p = new MPart
        {
            id = $"c0ffee00-0000-4000-8000-{partCounter:d12}",
            kind = kind,
            posMm = new Vector3(-80 + partCounter * 30, 0, 120),
            labels = kind switch
            {
                "LED" => new[] { "anode (+)", "cathode (−)" },
                "Servo" => new[] { "signal", "VCC", "GND" },
                "BME280" => new[] { "VCC", "GND", "SCL", "SDA" },
                "MCP3008" => new[] { "VDD", "GND", "CLK", "CS", "DIN",
                                      "DOUT", "AGND", "CH0", "CH1", "CH2",
                                      "CH3", "CH4", "CH5", "CH6", "CH7", "VREF" },
                "GPS" => new[] { "VCC", "GND", "TX", "RX" },
                _ => new[] { "pin 1", "pin 2" },
            },
        };
        parts.Add(p);
        Ack("addPart");
    }

    public void RemovePart(string id)
    {
        parts.RemoveAll(x => x.id == id);
        wires.RemoveAll(w => w.a.StartsWith($"comp:{id}:") ||
                             w.b.StartsWith($"comp:{id}:"));
        Ack("removePart");
    }

    public void AddWire(string a, string b)
    {
        wires.Add(new MWire { id = "w" + (1000 + wires.Count), a = a, b = b });
        Ack("addWire");
    }

    public void RemoveWire(string id)
    {
        wires.RemoveAll(x => x.id == id);
        Ack("removeWire");
    }

    public void SerialSend(string text)
    {
        serial.Add("$ " + text);
        serial.Add(text.Trim() switch
        {
            "ls" => "Desktop  Documents  Downloads",
            "uname -a" => "Linux twin 6.6.20-v8+ aarch64 GNU/Linux",
            "sudo reboot" => "Connection closed by remote host.",
            _ => "bash: " + text + ": command not found",
        });
        Ack("serialSend");
    }

    public void SetProbes(int[] pins, float windowSeconds = 2f)
    {
        probePins = pins;
        probeWindow = windowSeconds;
        Ack("setProbes");
    }

    public void Power(bool on)
    {
        powered = on;
        phase = on ? "running" : "halted";
        serial.Add(on ? "[power] ON" : "[power] OFF");
        Ack("power");
    }

    public void Restart()
    {
        phase = "booting";
        serial.Add("[power] RESTART");
        Ack("restart");
        Invoke(nameof(FinishBoot), 2f);
    }

    void FinishBoot() => phase = "running";

    // ── mock display ───────────────────────────────────────────
    bool displayOn;
    int displayMaxWidth = 960;
    float nextFrame;
    byte[] mockJpg;

    public void SetDisplay(bool on, int maxWidth = 960)
    {
        displayOn = on;
        displayMaxWidth = Mathf.Clamp(maxWidth, 320, 1920);
        Ack("setDisplay");
        if (on) SendMockFrame();
    }

    public DisplayInputState LastInput;
    public bool HasInput;

    public void DisplayInput(DisplayInputState input)
    {
        LastInput = input;
        HasInput = true;
        Ack("displayInput");
    }

    void LateUpdate()
    {
        if (!displayOn || Time.time < nextFrame) return;
        nextFrame = Time.time + 0.5f;
        SendMockFrame();
    }

    void SendMockFrame()
    {
        if (mockJpg == null)
        {
            var tex = new Texture2D(480, 270, TextureFormat.RGB24,
                false);
            var px = new Color32[480 * 270];
            for (int y = 0; y < 270; y++)
                for (int x = 0; x < 480; x++)
                    px[y * 480 + x] = new Color32(30, 110, 130,
                        255);
            for (int y = 200; y < 230; y++)
                for (int x = 100; x < 380; x++)
                    px[y * 480 + x] = new Color32(240, 241, 244,
                        255);
            tex.SetPixels32(px);
            tex.Apply(false, false); // stay readable for EncodeToJPG
            mockJpg = ImageConversion.EncodeToJPG(tex, 75);
            Destroy(tex);
        }
        OnDisplay?.Invoke(new TwinBridge.DisplayFrame
        {
            w = 480,
            h = 270,
            source = "mock",
            jpg = mockJpg,
        });
    }
}

static class LinqExtensions
{
    // serial.TakeLast(80) above
    public static IEnumerable<T> TakeLast<T>(this List<T> list, int n)
    {
        for (int i = Mathf.Max(0, list.Count - n); i < list.Count; i++)
            yield return list[i];
    }
}
