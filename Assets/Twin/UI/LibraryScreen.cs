// LibraryScreen.cs — the center overlay (concept pages 2 & 5):
// Boards | Components | Testing tabs inside a 440×650 glass panel.
// Boards: Pi 4B (selected) + Jetson Nano as true 3D models on
// turntables in front of the glass. Components: search pill + a
// 2-column grid of the twin's real catalog (LED, Button, Servo SG90,
// BME280, MCP3008, GPS) — pinch a tile to addPart on the twin.
// The right explainer card mirrors the concept copy per tab.

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LibraryScreen : MonoBehaviour
{
    public GameObject Root { get; private set; }
    GameObject modelsRootObj;

    public event Action<string> OnTab;
    public event Action<string> OnAddPart; // twin kind

    RectTransform boardsList, componentsList;
    GameObject boardsModels, componentsModels, explainerRoot;
    BenchButton tabBoards, tabComponents, tabTesting;
    TextMeshProUGUI explainerTitle, explainerBody, hintText;

    // Boards catalogue (concept page 2)
    static readonly (string name, string sub, string pins, bool sel)[] Boards =
    {
        ("Raspberry Pi 4B", "Quad-core · 3.3 V GPIO", "40 pins", true),
        ("Jetson Nano", "GPU module · 3.3 V logic", "40 pins", false),
    };

    // Components catalogue — the twin's real addPart kinds.
    static readonly (string kind, string label, string sub)[] Catalog =
    {
        ("LED", "LED red", "5 mm · glows when driven"),
        ("Button", "Button", "tact switch · press me"),
        ("Servo", "Servo SG90", "micro servo · sweeps"),
        ("BME280", "BME280", "temp · humidity · pressure"),
        ("MCP3008", "MCP3008", "8-channel ADC · SPI"),
        ("GPS", "GPS module", "u-blox · 3D fix"),
    };

    public void Build(Transform uiRoot)
    {
        var canvas = BenchUI.WorldCanvas(uiRoot, "Library", 420, 30, 440, 650);
        Root = canvas.gameObject;
        var panel = BenchUI.Panel(canvas.transform, "glass", 0, 0, 440, 650);

        // 3D showcase models float just in front of the glass
        var modelsRoot = new GameObject("LibraryModels");
        modelsRootObj = modelsRoot;
        modelsRoot.transform.SetParent(uiRoot, false);
        modelsRoot.transform.position =
            BenchTheme.StagePos(420, 30, 440, 650,
                                 BenchTheme.PlaneZ - 0.06f);
        modelsRoot.transform.rotation = Quaternion.identity;
        boardsModels = new GameObject("BoardsModels");
        boardsModels.transform.SetParent(modelsRoot.transform, false);
        componentsModels = new GameObject("ComponentsModels");
        componentsModels.transform.SetParent(modelsRoot.transform, false);

        const float pad = 28;
        tabBoards = BenchUI.Button(panel, "Boards", pad, pad, 108, active: true);
        tabComponents = BenchUI.Button(panel, "Components", pad + 116, pad, 138);
        tabTesting = BenchUI.Button(panel, "Testing", pad + 262, pad, 108);
        Hook(tabBoards, () => SetTab("Boards"));
        Hook(tabComponents, () => SetTab("Components"));
        Hook(tabTesting, () => OnTab?.Invoke("Testing"));

        boardsList = BenchUI.Rect(panel, "boards", 0, pad + 66, 440, 560);
        componentsList = BenchUI.Rect(panel, "components", 0, pad + 66, 440, 560);

        // ── Boards tab: two cards with 3D models (concept page 2) ──
        float y = 0;
        for (int i = 0; i < Boards.Length; i++)
        {
            var b = Boards[i];
            var rt = BenchUI.Rect(boardsList, "card_" + b.name, 14, y, 412, 190);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.Rounded(412, 190, BenchTheme.RCard,
                b.sel ? BenchTheme.ActiveBg : BenchTheme.CardBg,
                b.sel ? BenchTheme.ActiveBorder : BenchTheme.CardBorder, 1.5f);
            img.type = Image.Type.Sliced;
            BenchUI.Label(rt, "name", b.name, 240, 36,
                164, BenchTheme.FontRowName, BenchTheme.Text, true);
            BenchUI.Label(rt, "sub", b.sub, 240, 66,
                164, BenchTheme.FontSmall, BenchTheme.TextSubtle);
            BenchUI.Label(rt, "pins", b.pins, 240, 92,
                164, BenchTheme.FontSmall, BenchTheme.CyanLight);

            var slot = new GameObject("slot" + i);
            slot.transform.SetParent(boardsModels.transform, false);
            slot.transform.localPosition = new Vector3(
                (14 + 196 / 2f - 220) * BenchTheme.Px,
                (650f / 2 - (y + 95)) * BenchTheme.Px, 0.03f);
            slot.transform.localScale = Vector3.one * 3.2f;
            if (b.name.Contains("Jetson"))
                PartModels.JetsonNano(slot.transform);
            else
                PartModels.Pi4B(slot.transform);
            slot.AddComponent<PartTurntable>().revSeconds = i == 0 ? 16f : 21f;
            y += 204;
        }
        BenchUI.Label(boardsList, "more", "More boards coming soon",
            14, y + 6, 400, BenchTheme.FontSmall, BenchTheme.TextSubtle);

        // ── Components tab: search pill + tile grid (concept page 5) ─
        var searchPill = BenchUI.Rect(componentsList, "search", 14, 0, 412, 48);
        var searchImg = searchPill.gameObject.AddComponent<Image>();
        searchImg.sprite = Art.Rounded(412, 48, 24,
            new Color(0, 0, 0, 0), BenchTheme.BtnBorder, 1.5f);
        searchImg.type = Image.Type.Sliced;
        BenchUI.Label(searchPill, "ph", "Search components", 20, 14,
            360, 17, BenchTheme.TextSubtle);

        float ty = 62;
        for (int i = 0; i < Catalog.Length; i++)
        {
            int col = i % 2;
            var item = Catalog[i];
            var tile = MakeTile(componentsList, col, ty, item, i);
            Hook(tile, () => OnAddPart?.Invoke(item.kind));
            if (col == 1) ty += 154;
        }

        // ── right explainer (concept: dark glass, 300 wide) ────────
        var ex = BenchUI.WorldCanvas(uiRoot, "Explainer", 900, 200, 300, 230);
        explainerRoot = ex.gameObject;
        var exPanel = BenchUI.Panel(ex.transform, "glass", 0, 0, 300, 230,
            dark: true);
        explainerTitle = BenchUI.Label(exPanel, "t", "Drag a board out",
            24, 22, 252, BenchTheme.FontPanel, BenchTheme.Text, true);
        explainerBody = BenchUI.Label(exPanel, "b",
            "The glass dissolves, this panel glides left, and the board " +
            "lands on your real desk.",
            24, 52, 252, BenchTheme.FontSmall, BenchTheme.TextSubtle,
            h: 160);
        explainerBody.textWrappingMode = TMPro.TextWrappingModes.Normal;

        // ── hint (concept footer, centred on the stage) ────────────
        hintText = BenchUI.Label(canvas.transform, "hint",
            "True 3D models: raised chips, ports and header pins, turning " +
            "slowly, LEDs blinking, light sweeping across the PCB",
            -420, 620, 1280, BenchTheme.FontHint, BenchTheme.TextHint,
            align: TextAlignmentOptions.Center);

        SetTab("Boards");
    }

    RectTransform MakeTile(RectTransform parent, int col, float y,
                           (string kind, string label, string sub) item, int i)
    {
        var rt = BenchUI.Rect(parent, "tile_" + item.kind,
            14 + col * 200, y, 184, 140);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Art.Rounded(184, 140, BenchTheme.RTile,
            BenchTheme.CardBg, BenchTheme.CardBorder, 1.5f);
        img.type = Image.Type.Sliced;
        BenchUI.Label(rt, "name", item.label, 10, 104,
            164, 16, BenchTheme.Text, true,
            align: TextAlignmentOptions.Center);
        BenchUI.Label(rt, "sub", item.sub, 10, 86,
            164, 13, BenchTheme.TextSubtle,
            align: TextAlignmentOptions.Center);

        var slot = new GameObject("mini" + item.kind);
        slot.transform.SetParent(componentsModels.transform, false);
        slot.transform.localPosition = new Vector3(
            (14 + col * 200 + 92 - 220) * BenchTheme.Px,
            (650f / 2 - (y + 52)) * BenchTheme.Px, 0.03f);
        slot.transform.localScale = Vector3.one * 2.6f;
        PartModels.ForKind(item.kind, slot.transform);
        slot.AddComponent<PartTurntable>().revSeconds = 16f + i * 1.7f;
        return rt;
    }

    void Hook(Component c, Action a)
    {
        var h = c.gameObject.AddComponent<UiClick>();
        h.on = a;
    }

    public void SetTab(string t)
    {
        tabBoards.SetActive(t == "Boards");
        tabComponents.SetActive(t == "Components");
        tabTesting.SetActive(t == "Testing");
        bool boardsView = t == "Boards";
        boardsList.gameObject.SetActive(boardsView);
        componentsList.gameObject.SetActive(!boardsView);
        boardsModels.SetActive(boardsView);
        componentsModels.SetActive(!boardsView);

        if (t == "Boards")
        {
            explainerTitle.text = "Drag a board out";
            explainerBody.text =
                "The glass dissolves, this panel glides left, and the " +
                "board lands on your real desk.";
            hintText.text = "True 3D models: raised chips, ports and header " +
                "pins, turning slowly, LEDs blinking, light sweeping across " +
                "the PCB";
        }
        else if (t == "Components")
        {
            explainerTitle.text = "Parts that behave";
            explainerBody.text =
                "The LED glows, the ultrasonic sensor pings, the servo " +
                "sweeps, and the breadboard lights its connected strip.";
            hintText.text = "Each part is a true 3D model on a turntable · " +
                "grab it and pull it into the room";
        }
    }

    public void Show(bool v)
    {
        Root.SetActive(v);
        modelsRootObj.SetActive(v);
        explainerRoot.SetActive(v);
    }
}
