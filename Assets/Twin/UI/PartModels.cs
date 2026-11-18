// PartModels.cs — procedural 3D models for the bench parts, built
// from Unity primitives with the concept mock's exact palette
// (green PCB, gold header, silver shields, glowing LEDs…). No
// imported assets: every part is assembled in code so the whole UI
// ships with the scripts. Real-world millimetre scale (Pi 4B = 85×56).
//
// Also carries the 40-pin Pi header table (ported 1:1 from the twin's
// BoardGeometry.swift) — same physical numbers, names, BCM mapping.

using System.Collections.Generic;
using UnityEngine;

public enum PinKind { Gpio, V3, V5, Gnd }

public struct BoardPin
{
    public int physical;   // 1..40
    public string name;     // "3V3", "GPIO2", "GND"…
    public int bcm;         // -1 when not a GPIO
    public PinKind kind;
    public string alt;      // "I2C-1 SDA"… ("" when none)

    public BoardPin(int physical, string name, int bcm, PinKind kind, string alt)
    { this.physical = physical; this.name = name; this.bcm = bcm;
      this.kind = kind; this.alt = alt; }
}

public static class BoardPins
{
    // The twin's header, verbatim (BoardGeometry.swift).
    public static readonly BoardPin[] All =
    {
        new( 1, "3V3",   -1, PinKind.V3,  ""),
        new( 2, "5V",    -1, PinKind.V5,  ""),
        new( 3, "GPIO2",  2, PinKind.Gpio, "I2C-1 SDA"),
        new( 4, "5V",    -1, PinKind.V5,  ""),
        new( 5, "GPIO3",  3, PinKind.Gpio, "I2C-1 SCL"),
        new( 6, "GND",   -1, PinKind.Gnd, ""),
        new( 7, "GPIO4",  4, PinKind.Gpio, "GPCLK0"),
        new( 8, "GPIO14",14, PinKind.Gpio, "UART0 TX"),
        new( 9, "GND",   -1, PinKind.Gnd, ""),
        new(10, "GPIO15",15, PinKind.Gpio, "UART0 RX"),
        new(11, "GPIO17",17, PinKind.Gpio, ""),
        new(12, "GPIO18",18, PinKind.Gpio, "PWM ch0"),
        new(13, "GPIO27",27, PinKind.Gpio, ""),
        new(14, "GND",   -1, PinKind.Gnd, ""),
        new(15, "GPIO22",22, PinKind.Gpio, ""),
        new(16, "GPIO23",23, PinKind.Gpio, ""),
        new(17, "3V3",   -1, PinKind.V3,  ""),
        new(18, "GPIO24",24, PinKind.Gpio, ""),
        new(19, "GPIO10",10, PinKind.Gpio, "SPI0 MOSI"),
        new(20, "GND",   -1, PinKind.Gnd, ""),
        new(21, "GPIO9",  9, PinKind.Gpio, "SPI0 MISO"),
        new(22, "GPIO25",25, PinKind.Gpio, ""),
        new(23, "GPIO11",11, PinKind.Gpio, "SPI0 SCLK"),
        new(24, "GPIO8",  8, PinKind.Gpio, "SPI0 CE0"),
        new(25, "GND",   -1, PinKind.Gnd, ""),
        new(26, "GPIO7",  7, PinKind.Gpio, "SPI0 CE1"),
        new(27, "GPIO0",  0, PinKind.Gpio, "ID EEPROM SDA"),
        new(28, "GPIO1",  1, PinKind.Gpio, "ID EEPROM SCL"),
        new(29, "GPIO5",  5, PinKind.Gpio, ""),
        new(30, "GND",   -1, PinKind.Gnd, ""),
        new(31, "GPIO6",  6, PinKind.Gpio, ""),
        new(32, "GPIO12",12, PinKind.Gpio, "PWM ch0"),
        new(33, "GPIO13",13, PinKind.Gpio, "PWM ch1"),
        new(34, "GND",   -1, PinKind.Gnd, ""),
        new(35, "GPIO19",19, PinKind.Gpio, "PWM ch1"),
        new(36, "GPIO16",16, PinKind.Gpio, ""),
        new(37, "GPIO26",26, PinKind.Gpio, ""),
        new(38, "GPIO20",20, PinKind.Gpio, "SPI1 MOSI"),
        new(39, "GND",   -1, PinKind.Gnd, ""),
        new(40, "GPIO21",21, PinKind.Gpio, "SPI1 SCLK"),
    };

    public const float Pitch = 2.54f; // header pitch, mm

    /// Local board-space position (mm, board centre = origin,
    /// +x along the 40-pin edge, +y up) of a header pin.
    public static Vector3 LocalPos(int physical)
    {
        int row = (physical - 1) / 2;
        int col = (physical - 1) % 2; // 0 odd pins, 1 even
        return new Vector3(col * Pitch - Pitch / 2f, 0f, row * Pitch - 19.5f * Pitch / 2f + Pitch / 2f);
    }

    /// Badge shape + border colour per the concept:
    /// ▲ power (red) · ● GPIO (cyan) · ■ GND (light).
    public static char Shape(BoardPin p) => p.kind switch
    {
        PinKind.Gpio => '●',
        PinKind.Gnd => '■',
        _ => '▲',
    };
}

// ─────────────────────────────────────────────────────────────────
// Procedural meshes + materials
// ─────────────────────────────────────────────────────────────────
public static class PartModels
{
    static readonly Dictionary<Color, Material> Mats = new();

    static Shader _litShader;
    static Shader LitShader => _litShader ??= Shader.Find(
        "Universal Render Pipeline/Unlit")
        ?? Shader.Find("Sprites/Default");
    // NOTE: flat Unlit by design — the concept mockups are flat
    // colours, and URP Lit materials created at runtime were the one
    // thing that kept rendering error-magenta in captures.

    static Shader _unlitShader;
    static Shader UnlitShader => _unlitShader ??= Shader.Find(
        "Universal Render Pipeline/Unlit")
        ?? Shader.Find("Sprites/Default");

    public static Material Mat(Color c)
    {
        if (Mats.TryGetValue(c, out var m)) return m;
        m = new Material(LitShader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        Mats[c] = m;
        return m;
    }

    /// Emissive unlit material (LED glows, wire highlight).
    static readonly Dictionary<Color, Material> GlowMats = new();

    public static Material GlowMat(Color c)
    {
        if (GlowMats.TryGetValue(c, out var cached)) return cached;
        var m = new Material(UnlitShader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        // transparent surface so the glow sphere reads as a halo
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        GlowMats[c] = m;
        return m;
    }

    // primitive helpers (mm in, world out)
    public static GameObject Box(Transform parent, string name,
                                 Vector3 centerMm, Vector3 sizeMm, Color c)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = centerMm * 0.001f;
        g.transform.localScale = sizeMm * 0.001f;
        return g;
    }

    public static GameObject Cyl(Transform parent, string name,
                                 Vector3 centerMm, float diaMm, float heightMm,
                                 Color c, int seg = 12)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name;
        g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = centerMm * 0.001f;
        g.transform.localRotation = Quaternion.Euler(90, 0, 0); // along +x
        g.transform.localScale = new Vector3(diaMm * 0.001f, heightMm * 0.001f,
                                               diaMm * 0.001f);
        return g;
    }

    public static GameObject Sphere(Transform parent, string name,
                                    Vector3 centerMm, float diaMm, Color c)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = name;
        g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = centerMm * 0.001f;
        g.transform.localScale = Vector3.one * diaMm * 0.001f;
        return g;
    }

    /// Header pin: gold square post (2.54 pitch, 0.9 square × 6 tall).
    public static GameObject Pin(Transform parent, string name,
                                 Vector3 baseMm, Color c)
    {
        var g = Box(parent, name, baseMm + new Vector3(0, 3, 0),
                    new Vector3(0.9f, 6, 0.9f), c);
        return g;
    }

    // ── the parts ─────────────────────────────────────────────────

    /// Raspberry Pi 4 Model B — 85×56 mm green PCB, 40-pin gold
    /// header along the top edge, silver shields, BCM2711 SoC,
    /// blinking ACT LED. Returns the root; pin anchors are named
    /// "pin<physical>".
    public static GameObject Pi4B(Transform parent)
    {
        var root = new GameObject("Pi4B");
        root.transform.SetParent(parent, false);

        // PCB (green, slightly rounded look via flat box)
        Box(root.transform, "PCB", new Vector3(0, 0, 0),
            new Vector3(85, 1.6f, 56), BenchTheme.Pcb);

        // 40-pin header along +z edge (physical 1..40)
        for (int i = 1; i <= 40; i++)
        {
            var p = BoardPins.LocalPos(i);
            var pin = Pin(root.transform, "pin" + i,
                p + new Vector3(-38, 0.8f, 28), BenchTheme.Gold);
            pin.name = "pin" + i;
        }

        // SoC + heat spreader
        Box(root.transform, "SoC", new Vector3(2, 1.6f, 2),
            new Vector3(15, 1.6f, 15), BenchTheme.SocGrey);
        Box(root.transform, "ShieldUSB", new Vector3(-24, 2f, 14),
            new Vector3(28, 6, 22), BenchTheme.Silver);
        Box(root.transform, "ShieldNet", new Vector3(-24, 2f, -14),
            new Vector3(20, 6, 16), BenchTheme.Silver);

        // USB-C power + RJ45 + microSD on the edges
        Box(root.transform, "USBC", new Vector3(-42.5f + 4, 2f, -20),
            new Vector3(9, 3.5f, 8), BenchTheme.ChipDark);
        Box(root.transform, "RJ45", new Vector3(-42.5f + 5, 3f, 6),
            new Vector3(16, 13.5f, 13), BenchTheme.Silver);
        Box(root.transform, "MicroSD", new Vector3(34, 1.6f, -28 + 2),
            new Vector3(12, 1.4f, 5), BenchTheme.ChipDark);

        // status LEDs (concept: red power glow, green ACT blinking)
        var pwr = Sphere(root.transform, "LED_PWR", new Vector3(36, 2.5f, 24),
            3, BenchTheme.Red);
        var act = Sphere(root.transform, "LED_ACT", new Vector3(36, 2.5f, 18),
            3, BenchTheme.Green);
        act.AddComponent<PartBlink>();

        return root;
    }

    /// Jetson Nano module + carrier (library showcase only).
    public static GameObject JetsonNano(Transform parent)
    {
        var root = new GameObject("JetsonNano");
        root.transform.SetParent(parent, false);
        Box(root.transform, "PCB", Vector3.zero, new Vector3(100, 1.6f, 79),
            BenchTheme.PcbJetsonTop);
        // heat sink fins
        for (int i = 0; i < 7; i++)
            Box(root.transform, "Fin" + i, new Vector3(-30 + i * 6, 6, 0),
                new Vector3(3, 10, 45), BenchTheme.Silver);
        Box(root.transform, "Module", new Vector3(28, 3, 10),
            new Vector3(45, 4, 30), BenchTheme.PcbJetsonBottom);
        for (int i = 1; i <= 20; i++)
            Pin(root.transform, "jp" + i,
                new Vector3(-46 + (i - 1) * 2.54f, 0.8f, 34), BenchTheme.Gold);
        Box(root.transform, "Label", new Vector3(28, 5.2f, 10),
            new Vector3(24, 1, 12), BenchTheme.ChipDark);
        return root;
    }

    /// 5 mm LED: red dome + two legs. `levels[0]` drives the glow.
    public static GameObject Led(Transform parent)
    {
        var root = new GameObject("LED");
        root.transform.SetParent(parent, false);
        Sphere(root.transform, "Dome", new Vector3(0, 6, 0), 5, BenchTheme.Red);
        Cyl(root.transform, "Skirt", new Vector3(0, 4.5f, 0), 5.5f, 2, BenchTheme.Red);
        Cyl(root.transform, "LegA", new Vector3(-1.25f, 0, 0), 0.6f, 8, BenchTheme.Silver);
        Cyl(root.transform, "LegC", new Vector3(1.25f, 0, 0), 0.6f, 6, BenchTheme.Silver);
        var glow = Sphere(root.transform, "Glow", new Vector3(0, 6, 0), 9, BenchTheme.Red);
        glow.GetComponent<MeshRenderer>().sharedMaterial = GlowMat(
            new Color(1f, 0.42f, 0.42f, 0.25f));
        glow.AddComponent<PartGlow>();
        return root;
    }

    /// Tactile push button (6 mm): dark base + silver cap.
    public static GameObject ButtonPart(Transform parent)
    {
        var root = new GameObject("Button");
        root.transform.SetParent(parent, false);
        Box(root.transform, "Base", new Vector3(0, 1.5f, 0),
            new Vector3(6, 3, 6), BenchTheme.ChipDark);
        Cyl(root.transform, "Cap", new Vector3(0, 4.6f, 0), 4, 2.4f, BenchTheme.Silver);
        for (int i = 0; i < 4; i++)
        {
            float a = 90 * i * Mathf.Deg2Rad;
            Box(root.transform, "Leg" + i,
                new Vector3(Mathf.Cos(a) * 4f, 0.8f, Mathf.Sin(a) * 4f),
                new Vector3(3.4f, 1.6f, 1.2f), BenchTheme.Silver);
        }
        return root;
    }

    /// SG90 micro servo: blue body, white sweeping horn.
    public static GameObject Servo(Transform parent)
    {
        var root = new GameObject("Servo");
        root.transform.SetParent(parent, false);
        Box(root.transform, "Body", new Vector3(0, 6, 0),
            new Vector3(23, 12, 12.5f), new Color(0.16f, 0.44f, 0.78f));
        Box(root.transform, "Mounts", new Vector3(0, 10.5f, 0),
            new Vector3(32.5f, 1, 12.5f), new Color(0.14f, 0.38f, 0.68f));
        var horn = new GameObject("Horn");
        horn.transform.SetParent(root.transform, false);
        horn.transform.localPosition = new Vector3(0, 0.013f, 0);
        Cyl(horn.transform, "Hub", new Vector3(0, 12.8f, 0), 5, 4, BenchTheme.Text);
        Box(horn.transform, "Arm", new Vector3(0, 12.8f, -5f),
            new Vector3(2, 2, 10), BenchTheme.Text);
        horn.AddComponent<ServoSweep>();
        return root;
    }

    /// BME280 breakout: purple PCB, 4-pin header.
    public static GameObject Bme280(Transform parent)
    {
        var root = new GameObject("BME280");
        root.transform.SetParent(parent, false);
        Box(root.transform, "PCB", Vector3.zero, new Vector3(18, 1.4f, 21),
            new Color(0.35f, 0.2f, 0.5f));
        Box(root.transform, "Sensor", new Vector3(0, 1.6f, 0),
            new Vector3(4, 2, 4), BenchTheme.Silver);
        for (int i = 0; i < 4; i++)
            Pin(root.transform, "p" + i, new Vector3(-6 + i * 4, 0.7f, 9), BenchTheme.Gold);
        return root;
    }

    /// MCP3008: 16-pin DIP ADC chip.
    public static GameObject Mcp3008(Transform parent)
    {
        var root = new GameObject("MCP3008");
        root.transform.SetParent(parent, false);
        Box(root.transform, "Body", new Vector3(0, 3.5f, 0),
            new Vector3(10, 3.5f, 20), BenchTheme.ChipDark);
        for (int i = 0; i < 8; i++)
        {
            Pin(root.transform, "p" + i, new Vector3(6.35f, 0.7f, -7.5f + i * 2.54f), BenchTheme.Silver);
            Pin(root.transform, "p" + (i + 8), new Vector3(-6.35f, 0.7f, 7.5f - i * 2.54f), BenchTheme.Silver);
        }
        Box(root.transform, "Notch", new Vector3(0, 7.1f, 8.8f),
            new Vector3(3, 0.6f, 2), BenchTheme.Silver);
        return root;
    }

    /// GPS breakout: green PCB + ceramic patch antenna.
    public static GameObject Gps(Transform parent)
    {
        var root = new GameObject("GPS");
        root.transform.SetParent(parent, false);
        Box(root.transform, "PCB", Vector3.zero, new Vector3(25, 1.4f, 25),
            BenchTheme.Pcb);
        Box(root.transform, "Patch", new Vector3(0, 2.2f, -5),
            new Vector3(18, 2, 18), BenchTheme.Silver);
        Box(root.transform, "Module", new Vector3(0, 2f, 8),
            new Vector3(12, 1.6f, 8), BenchTheme.ChipDark);
        for (int i = 0; i < 4; i++)
            Pin(root.transform, "p" + i, new Vector3(-7.5f + i * 5, 0.7f, 11), BenchTheme.Gold);
        return root;
    }

    /// Mini breadboard (fixed desk furniture): light body, red/blue
    /// power rails, hole grid (the twin's term:/rail: nodes).
    public static GameObject Breadboard(Transform parent)
    {
        var root = new GameObject("Breadboard");
        root.transform.SetParent(parent, false);
        Box(root.transform, "Body", new Vector3(0, 4, 0),
            new Vector3(170, 8, 55), BenchTheme.Breadboard);
        Box(root.transform, "Well", new Vector3(0, 8.05f, 0),
            new Vector3(160, 0.3f, 40), new Color(0.92f, 0.90f, 0.87f));
        // power rails
        Box(root.transform, "Rail+", new Vector3(0, 8.1f, -21),
            new Vector3(150, 0.5f, 2), BenchTheme.RailRed);
        Box(root.transform, "Rail-", new Vector3(0, 8.1f, 21),
            new Vector3(150, 0.5f, 2), BenchTheme.RailBlue);
        // hole grid
        for (int r = 0; r < 10; r++)
            for (int c = 0; c < 30; c++)
                Box(root.transform, $"h{r}_{c}",
                    new Vector3(-72.5f + c * 5, 8.1f, -16 + r * 4.5f),
                    new Vector3(1.6f, 0.4f, 1.6f), BenchTheme.HoleGrey);
        return root;
    }

    /// Catalog kind → model (mirror of the twin's ComponentKind).
    public static GameObject ForKind(string kind, Transform parent) => kind switch
    {
        "LED" => Led(parent),
        "Button" => ButtonPart(parent),
        "Servo" => Servo(parent),
        "BME280" => Bme280(parent),
        "MCP3008" => Mcp3008(parent),
        "GPS" => Gps(parent),
        _ => Led(parent), // unknown kinds still show *something*
    };
}

// ─────────────────────────────────────────────────────────────────
// Part behaviours (concept animations)
// ─────────────────────────────────────────────────────────────────

/// Slow turntable spin for library cards (concept .sp 16 s/rotation).
public class PartTurntable : MonoBehaviour
{
    public float revSeconds = 16f;
    public bool bob;
    Vector3 basePos;
    void Start() => basePos = transform.localPosition;
    void Update()
    {
        transform.localRotation =
            Quaternion.Euler(0, Time.time * 360f / revSeconds % 360f, 0);
        if (bob)
            transform.localPosition = basePos +
                Vector3.up * Mathf.Sin(Time.time * Mathf.PI / 3f) * 0.004f;
    }
}

/// 1.3 s activity blink (concept .bl).
public class PartBlink : MonoBehaviour
{
    MeshRenderer r;
    Material mat;
    void Start()
    {
        r = GetComponent<MeshRenderer>();
        mat = r.sharedMaterial;
        r.sharedMaterial = mat = new Material(mat); // private instance
    }
    void Update()
    {
        float phase = Time.time % 1.3f;
        bool on = phase < 0.45f * 1.3f; // 0–45% on
        var c = mat.color;
        mat.color = new Color(c.r, c.g, c.b, on ? 1f : 0.12f);
    }
}

/// LED glow pulse when powered (concept .gw 2.4 s).
public class PartGlow : MonoBehaviour
{
    public bool lit = true;
    MeshRenderer r;
    void Start() => r = GetComponent<MeshRenderer>();
    void Update()
    {
        if (r == null) return;
        float pulse = 0.2f + 0.55f * (0.5f + 0.5f * Mathf.Sin(
            Time.time * 2f * Mathf.PI / 2.4f));
        var m = r.sharedMaterial;
        if (!lit) pulse = 0.06f;
        var c = m.color;
        m.color = new Color(c.r, c.g, c.b, pulse);
    }
}

/// Servo horn sweep ±40° every 3 s (concept .hn).
public class ServoSweep : MonoBehaviour
{
    void Update()
    {
        float t = Mathf.PingPong(Time.time / 3f, 1f);
        float angle = Mathf.Lerp(-40f, 40f, t);
        transform.localRotation = Quaternion.Euler(0, angle, 0);
    }
}
