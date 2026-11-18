// WorkbenchUI.cs — the pin-to-pin wiring mode (concept page 3):
// the "Pin Mode" pill up top ("Wire: pinch pin A, then pin B"),
// the filter pill (All · Power · Ground · GPIO · I2C · Free pins),
// the GPIO hover card on the right, and the wiring flow itself:
// pinch a pin → valid targets glow green → pinch the second pin →
// cyan wire grows between them. Pinch a wire to remove it. Badges
// fade with gaze distance so 40 header pins stay readable.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorkbenchUI : MonoBehaviour
{
    public GameObject Root { get; private set; }

    public event Action OnExit;

    TwinMirror mirror;
    GazePinchInputModule input;
    RectTransform hoverCard;
    TextMeshProUGUI hoverTitle, hoverSub, hoverFoot;
    PinTarget wiringFrom;

    BenchButton fAll, fPower, fGround, fGpio, fI2c, fFree;

    public void Build(Transform uiRoot, TwinMirror mirror)
    {
        this.mirror = mirror;
        input = GazePinchInputModule.Instance;

        var canvas = BenchUI.WorldCanvas(uiRoot, "Workbench",
            0, 0, BenchTheme.StageW, BenchTheme.StageH);
        Root = canvas.gameObject;

        // ── top pill: "Pin Mode · Wire: pinch pin A, then pin B" ──
        var pill = BenchUI.Panel(canvas.transform, "pinModePill",
            450, 60, 380, 54, dark: true, radius: 27);
        BenchUI.Label(pill, "t", "Pin Mode", 24, 14, 120,
            18, BenchTheme.CyanLight, true);
        BenchUI.Label(pill, "s", "Wire: pinch pin A, then pin B", 148, 16,
            190, BenchTheme.FontSmall, BenchTheme.TextSubtle);
        var exit = BenchUI.Button(pill, "Exit", 296, 8, 72, h: 38);
        Hook(exit, () => OnExit?.Invoke());

        // ── bottom filter pill (680×60, concept page 3) ────────────
        var filter = BenchUI.Panel(canvas.transform, "filterPill",
            300, 610, 680, 60, dark: true, radius: 30);
        string[] names = { "All", "Power", "Ground", "GPIO", "I2C", "Free pins" };
        var buttons = new BenchButton[names.Length];
        float fx = 12;
        for (int i = 0; i < names.Length; i++)
        {
            float w = names[i].Length > 5 ? 128 : 104;
            var b = BenchUI.Button(filter, names[i], fx, 10, w, h: 40,
                active: i == 0);
            var idx = i;
            Hook(b, () => SelectFilter(idx));
            buttons[i] = b;
            fx += w + 8;
        }
        fAll = buttons[0]; fPower = buttons[1]; fGround = buttons[2];
        fGpio = buttons[3]; fI2c = buttons[4]; fFree = buttons[5];

        // ── right hover card (250 wide, concept: GPIO11 · SPI clock) ─
        hoverCard = BenchUI.Panel(canvas.transform, "hoverCard",
            960, 262, 250, 96, dark: true, radius: 18);
        hoverTitle = BenchUI.Label(hoverCard, "t", "", 18, 16,
            214, BenchTheme.FontPanel, BenchTheme.Text, true);
        hoverSub = BenchUI.Label(hoverCard, "s", "", 18, 44,
            214, BenchTheme.FontSmall, BenchTheme.TextSubtle);
        hoverFoot = BenchUI.Label(hoverCard, "f", "", 18, 68,
            214, BenchTheme.FontSmall, BenchTheme.Green);
        hoverCard.gameObject.SetActive(false);

        // ── hint ────────────────────────────────────────────────────
        BenchUI.Label(canvas.transform, "hint",
            "Labels pair shape, colour and name · gaze aims, pinch wires",
            0, 684, BenchTheme.StageW, BenchTheme.FontHint,
            BenchTheme.TextHint, align: TextAlignmentOptions.Center);

        // ── wiring flow ─────────────────────────────────────────────
        if (input != null)
        {
            input.OnPinPinched += PinPinched;
            input.OnWirePinched += WirePinched;
            input.OnPartDragStart += PartGrabbed;
            input.OnPartDragEnd += PartReleased;
        }
    }

    void Hook(Component c, Action a)
    {
        var h = c.gameObject.AddComponent<UiClick>();
        h.on = a;
    }

    void SelectFilter(int idx)
    {
        var cat = idx switch
        {
            0 => PinCategory.All, 1 => PinCategory.Power,
            2 => PinCategory.Ground, 3 => PinCategory.Gpio,
            4 => PinCategory.I2C, _ => PinCategory.Free,
        };
        fAll.SetActive(cat == PinCategory.All);
        fPower.SetActive(cat == PinCategory.Power);
        fGround.SetActive(cat == PinCategory.Ground);
        fGpio.SetActive(cat == PinCategory.Gpio);
        fI2c.SetActive(cat == PinCategory.I2C);
        fFree.SetActive(cat == PinCategory.Free);
        mirror.SetFilter(cat);
    }

    // ── wiring state machine ──────────────────────────────────────
    void PinPinched(PinTarget pin)
    {
        if (wiringFrom == null)
        {
            wiringFrom = pin;
            GlowValid(true);
        }
        else
        {
            if (pin != wiringFrom && pin.node != wiringFrom.node)
                mirror.AddWire(wiringFrom.node, pin.node);
            wiringFrom = null;
            GlowValid(false);
        }
    }

    void GlowValid(bool on)
    {
        foreach (var b in mirror.AllBadges)
            b.SetGlow(on);
        hoverFoot.text = on ? "Valid targets glow green" : "";
    }

    void WirePinched(WireGrab grab) => mirror.RemoveWire(grab.wire.wireId);

    void PartGrabbed(PartTarget part)
    {
        if (mirror.Parts.TryGetValue(part.partId, out var p) &&
            p.kind == "Button")
            mirror.PressButton(part.partId, true);
    }

    void PartReleased(PartTarget part)
    {
        if (mirror.Parts.TryGetValue(part.partId, out var p) &&
            p.kind == "Button")
            mirror.PressButton(part.partId, false);
    }

    // ── per-frame: hover card + badge gaze fades ──────────────────
    void Update()
    {
        if (input == null) return;

        // hover card follows the gazed pin
        PinTarget hoverPin = null;
        if (input.Hovered3d != null)
            hoverPin = input.Hovered3d.GetComponent<PinTarget>()
                ?? input.Hovered3d.GetComponent<PinBadgeTarget>()?.target;
        if (hoverPin != null)
        {
            hoverCard.gameObject.SetActive(true);
            hoverTitle.text = $"{hoverPin.label} · {ShortSub(hoverPin)}";
            hoverSub.text = hoverPin.subtitle;
            if (wiringFrom != null)
                hoverFoot.text = hoverPin == wiringFrom
                    ? "pinch another pin to cancel"
                    : "Valid targets glow green";
            else if (hoverFoot.text == "") hoverFoot.text = "gaze aims, pinch wires";
        }
        else
        {
            hoverCard.gameObject.SetActive(wiringFrom != null);
            if (wiringFrom != null)
            {
                hoverTitle.text = "Wiring from " + wiringFrom.label;
                hoverSub.text = "pinch another pin to connect";
                hoverFoot.text = "Valid targets glow green";
            }
        }

        // badges fade with gaze distance (nearest stay readable)
        Vector3 g = input.GazePoint;
        foreach (var b in mirror.AllBadges)
        {
            float d = Vector3.Distance(b.transform.position, g);
            b.targetAlpha = Mathf.Clamp01(1.4f - d * 2.2f);
        }
    }

    static string ShortSub(PinTarget p) => p.category switch
    {
        PinCategory.Power => "power",
        PinCategory.Ground => "ground",
        PinCategory.I2C => "I2C",
        _ => "GPIO",
    };

    public void Show(bool v)
    {
        Root.SetActive(v);
        if (!v && wiringFrom != null)
        {
            wiringFrom = null;
            GlowValid(false);
        }
    }
}
