// TestingUI.cs — the testing lab (concept page 4): the oscilloscope
// glass panel with live CH1/CH2 traces from the twin's probes, the
// power pill (● On / Restart / Pin Mode), and the Raspberry Pi 4B
// status rail — live ring, Display | SSH / Terminal tabs, the mini
// display mock, and the GPIO ● H / ○ L line. Serial console sends
// serialSend over the link; the display only lights when powered
// ("the display appears only after the board is on").

using System;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TestingUI : MonoBehaviour
{
    public GameObject Root { get; private set; }
    public event Action<string> OnPinMode; // "Pin Mode" pressed

    TwinMirror mirror;
    ScopeGraph scope;
    TextMeshProUGUI legend1, legend2, stats, dcBig, probing;
    TextMeshProUGUI liveText, gpioText, consoleText;
    GameObject displayMock, console;
    RectTransform railBody;
    BenchButton powerOn, displayTab, sshTab;

    public void Build(Transform uiRoot, TwinMirror mirror)
    {
        this.mirror = mirror;

        var canvas = BenchUI.WorldCanvas(uiRoot, "Testing",
            0, 0, BenchTheme.StageW, BenchTheme.StageH);
        Root = canvas.gameObject;

        // ── oscilloscope panel (590×270 dark, concept page 4) ──────
        var scopePanel = BenchUI.Panel(canvas.transform, "scope",
            330, 24, 590, 270, dark: true);
        BenchUI.Label(scopePanel, "t", "Oscilloscope", 20, 18, 300,
            BenchTheme.FontPanel, BenchTheme.Text, true);
        probing = BenchUI.Label(scopePanel, "p", "Probing: GPIO18 net",
            300, 21, 268, BenchTheme.FontSmall, BenchTheme.TextSubtle,
            align: TextAlignmentOptions.MidlineRight);

        // scope screen: dark #0A0A0A rounded rect + graph mesh (the
        // graph is a second Graphic → its own child GameObject)
        var screen = BenchUI.Rect(scopePanel, "screen", 20, 46, 380, 196);
        var screenImg = screen.gameObject.AddComponent<Image>();
        screenImg.sprite = Art.Rounded(380, 196, BenchTheme.RScreen,
            BenchTheme.ScreenBg);
        screenImg.type = Image.Type.Sliced;
        screenImg.raycastTarget = false;
        var graphRt = new GameObject("graph", typeof(RectTransform))
            .transform as RectTransform;
        graphRt.SetParent(screen, false);
        graphRt.anchorMin = Vector2.zero;
        graphRt.anchorMax = Vector2.one;
        graphRt.sizeDelta = Vector2.zero;
        scope = graphRt.gameObject.AddComponent<ScopeGraph>();
        scope.raycastTarget = false;

        // legend column (right of the screen, concept layout)
        legend1 = BenchUI.Label(scopePanel, "l1", "CH1 solid · GPIO18",
            414, 46, 150, BenchTheme.FontSmall, BenchTheme.CyanLight);
        legend2 = BenchUI.Label(scopePanel, "l2", "CH2 dashed · 3V3",
            414, 74, 150, BenchTheme.FontSmall, BenchTheme.Amber);
        stats = BenchUI.Label(scopePanel, "st", "Vpp 3.3 V · 1.0 kHz",
            414, 102, 160, BenchTheme.FontSmall, BenchTheme.TextSubtle);
        BenchUI.Label(scopePanel, "duty", "Duty 50%",
            414, 130, 160, BenchTheme.FontSmall, BenchTheme.TextSubtle);
        dcBig = BenchUI.Label(scopePanel, "dc", "3.31 V DC",
            414, 158, 160, BenchTheme.FontBigValue, BenchTheme.Text, true);

        // ── power pill (380×60, concept page 4) ────────────────────
        var power = BenchUI.Panel(canvas.transform, "powerPill",
            430, 372, 380, 60, dark: true, radius: 30);
        powerOn = BenchUI.Button(power, "  On", 8, 8, 116, h: 44, active: true);
        var dot = BenchUI.Rect(powerOn.transform, "dot", 14, 13, 14, 18);
        var dotImg = dot.gameObject.AddComponent<Image>();
        dotImg.sprite = Art.Circle(BenchTheme.Green);
        dotImg.raycastTarget = false;
        var restart = BenchUI.Button(power, "Restart", 132, 8, 116, h: 44);
        var pinMode = BenchUI.Button(power, "Pin Mode", 256, 8, 116, h: 44);
        Hook(powerOn, () => TogglePower());
        Hook(restart, () => mirror.Restart());
        Hook(pinMode, () => OnPinMode?.Invoke("Pin Mode"));

        // ── status rail (300×520 dark, concept page 4) ──────────────
        var rail = BenchUI.Panel(canvas.transform, "rail",
            960, 90, 300, 520, dark: true);
        BenchUI.Label(rail, "t", "Raspberry Pi 4B", 18, 18, 200,
            BenchTheme.FontPanel, BenchTheme.Text, true);
        var ring = BenchUI.Rect(rail, "ring", 244, 16, 38, 38);
        var ringImg = ring.gameObject.AddComponent<Image>();
        ringImg.sprite = Art.Ring(BenchTheme.Green, 0.16f);
        ringImg.raycastTarget = false;

        liveText = BenchUI.Label(rail, "live", "● Live · Twin synced",
            18, 60, 240, 14, BenchTheme.Green);
        liveText.richText = true;

        displayTab = BenchUI.Button(rail, "Display", 18, 96, 118, h: 38,
            active: true, fontSize: 15);
        sshTab = BenchUI.Button(rail, "SSH / Terminal", 144, 96, 138, h: 38,
            fontSize: 15);
        Hook(displayTab, () => SelectTab(true));
        Hook(sshTab, () => SelectTab(false));

        railBody = BenchUI.Rect(rail, "body", 18, 144, 264, 296);

        // display mock (concept: blue gradient, white card, dark bar)
        displayMock = new GameObject("DisplayMock");
        var dRt = displayMock.AddComponent<RectTransform>();
        dRt.SetParent(railBody, false);
        dRt.anchorMin = dRt.anchorMax = Vector2.zero;
        dRt.pivot = new Vector2(0, 0);
        dRt.anchoredPosition = Vector2.zero;
        dRt.sizeDelta = new Vector2(264, 296);
        var dImg = displayMock.AddComponent<Image>();
        dImg.sprite = Art.Rounded(264, 296, 14,
            new Color(0.11f, 0.30f, 0.54f), null);
        dImg.type = Image.Type.Sliced;
        var dCard = BenchUI.Rect(displayMock.transform, "card", 16, 176,
            170, 100);
        var dCardImg = dCard.gameObject.AddComponent<Image>();
        dCardImg.color = new Color(1, 1, 1, 0.88f);
        var dBar = BenchUI.Rect(displayMock.transform, "bar", 0, 0, 264, 28);
        var dBarImg = dBar.gameObject.AddComponent<Image>();
        dBarImg.color = new Color(0, 0, 0, 0.45f);
        var dDial = BenchUI.Rect(displayMock.transform, "dial", 180, 128,
            18, 18);
        var dDialImg = dDial.gameObject.AddComponent<Image>();
        dDialImg.sprite = Art.Ring(BenchTheme.Cyan, 0.18f);

        // serial console (SSH / Terminal tab)
        console = new GameObject("Console");
        var cRt = console.AddComponent<RectTransform>();
        cRt.SetParent(railBody, false);
        cRt.anchorMin = cRt.anchorMax = Vector2.zero;
        cRt.pivot = new Vector2(0, 0);
        cRt.anchoredPosition = Vector2.zero;
        cRt.sizeDelta = new Vector2(264, 296);
        var cImg = console.AddComponent<Image>();
        cImg.sprite = Art.Rounded(264, 296, 14, BenchTheme.ScreenBg, null);
        cImg.type = Image.Type.Sliced;
        consoleText = BenchUI.Label(cRt, "lines", "", 10, 10, 244, 12,
            BenchTheme.Green);
        consoleText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        consoleText.font = BenchUI.Font;
        consoleText.richText = true;
        float cy = 296 - 44;
        foreach (var cmd in new[] { "ls", "uname -a", "sudo reboot" })
        {
            var b = BenchUI.Button(cRt, cmd, 10, cy, 120, h: 30,
                fontSize: 14);
            var c = cmd;
            Hook(b, () => mirror.SerialSend(c));
            cy -= 38;
        }
        console.SetActive(false);

        // gpio level line (concept: "GPIO 17 ● H · 18 ● H · 27 ○ L …")
        gpioText = BenchUI.Label(rail, "gpio", "", 18, 452, 264, 14,
            BenchTheme.TextSubtle);
        gpioText.richText = true;

        // ── hint ────────────────────────────────────────────────────
        BenchUI.Label(canvas.transform, "hint",
            "Windows never overlap · the display appears only after the " +
            "board is on",
            0, 684, BenchTheme.StageW, BenchTheme.FontHint,
            BenchTheme.TextHint, align: TextAlignmentOptions.Center);

        mirror.OnSnapshotApplied += Refresh;
        SelectTab(true);
    }

    void Hook(Component c, Action a)
    {
        var h = c.gameObject.AddComponent<UiClick>();
        h.on = a;
    }

    void SelectTab(bool display)
    {
        displayTab.SetActive(display);
        sshTab.SetActive(!display);
        displayMock.SetActive(display);
        console.SetActive(!display);
    }

    void TogglePower()
    {
        bool nowOn = !mirror.Powered;
        mirror.Power(nowOn);
        powerOn.SetText(nowOn ? "  On" : "  Off");
    }

    /// Live refresh from the mirror (every snapshot).
    void Refresh()
    {
        scope.SetProbes(mirror.LatestProbes, 2f);
        legend1.text = scope.Ch1Legend;
        legend2.text = scope.Ch2Legend;
        stats.text = scope.StatsLine;
        dcBig.text = scope.DcLine;
        probing.text = scope.Ch1Legend.StartsWith("CH1 solid · GPIO")
            ? "Probing: GPIO" + (scope.Ch1Legend.Length > 16
                ? scope.Ch1Legend[16..] : "") + " net"
            : "Probing: —";

        bool live = mirror.Live;
        liveText.text = live ? "<color=#4ADE80>●</color> Live · Twin synced"
                             : "<color=#FFB454>●</color> Mock · bridge offline";

        // GPIO ● H / ○ L line for the concept's showcase pins
        string line = "";
        foreach (var bcm in new[] { 17, 18, 27, 22 })
        {
            if (!mirror.GpioLine.TryGetValue(bcm, out var lvl))
                continue;
            line += (line.Length > 0 ? " · " : "") + bcm + " ";
            line += lvl switch
            {
                true => "<color=#4ADE80>●</color> H",
                false => "○ L",
                _ => "– –",
            };
        }
        gpioText.text = "GPIO " + line;

        // serial console
        if (console.activeSelf && consoleText != null)
        {
            consoleText.text = string.Join("\n",
                mirror.SerialLines.Count > 14
                    ? mirror.SerialLines.GetRange(
                        mirror.SerialLines.Count - 14, 14)
                    : mirror.SerialLines);
        }

        // display appears only when the board is on
        if (displayMock != null)
            displayMock.SetActive(mirror.Powered && !console.activeSelf);
        powerOn.SetText(mirror.Powered ? "  On" : "  Off");
    }

    public void Show(bool v)
    {
        Root.SetActive(v);
        if (v) Refresh();
    }
}
