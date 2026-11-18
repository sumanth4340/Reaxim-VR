// ProjectPanel.cs — the left dock (concept pages 3–4): dark glass
// 250×420 with Boards / Components / Testing stacked pills and a
// live parts list (workbench) or the instrument rows (testing).

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum BenchMode { Home, Workbench, Testing }

public class ProjectPanel : MonoBehaviour
{
    public GameObject Root { get; private set; }

    public event System.Action<string> OnTab;  // "Boards"|"Components"|"Testing"
    public event System.Action<string> OnInstrument; // Oscilloscope…

    BenchButton boards, components, testing;
    TextMeshProUGUI partsList;
    RectTransform testingRows;
    readonly Dictionary<string, BenchButton> instrumentRows = new();
    string selectedInstrument = "Oscilloscope";

    public void Build(Transform parent)
    {
        var canvas = BenchUI.WorldCanvas(parent, "ProjectPanel",
            24, 150, 250, 420);
        Root = canvas.gameObject;
        var panel = BenchUI.Panel(canvas.transform, "dock", 0, 0, 250, 420,
            dark: true);

        const float pad = 20;
        BenchUI.Label(panel, "title", "Project Panel", pad, pad,
            200, BenchTheme.FontPanel, BenchTheme.Text, true);

        boards = BenchUI.Button(panel, "Boards", pad, pad + 40, 210,
            h: 44, active: false);
        components = BenchUI.Button(panel, "Components", pad, pad + 92, 210,
            h: 44, active: false);
        testing = BenchUI.Button(panel, "Testing", pad, pad + 144, 210,
            h: 44, active: false);
        Hook(boards, () => OnTab?.Invoke("Boards"));
        Hook(components, () => OnTab?.Invoke("Components"));
        Hook(testing, () => OnTab?.Invoke("Testing"));

        // workbench: live parts list (filled by RefreshParts)
        partsList = BenchUI.Label(panel, "parts", "", pad, pad + 210,
            210, BenchTheme.FontBody, BenchTheme.TextSubtle,
            h: 150);
        partsList.textWrappingMode = TMPro.TextWrappingModes.Normal;
        partsList.lineSpacing = 14;
        partsList.richText = true; // hint line uses <alpha> markup

        // testing: instrument rows (concept page 4)
        testingRows = BenchUI.Rect(panel, "instruments", pad, pad + 210,
            210, 190);
        float y = 0;
        foreach (var name in new[] { "Oscilloscope", "Multimeter",
                                     "Waveform analyser" })
        {
            var rt = BenchUI.Rect(testingRows, "row_" + name, 0, y,
                210, BenchTheme.RowHSmall);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Art.Rounded(210, BenchTheme.RowHSmall, BenchTheme.RRow,
                name == selectedInstrument
                    ? BenchTheme.RowSelectedBg : BenchTheme.RowBg,
                name == selectedInstrument
                    ? BenchTheme.RowSelectedBorder : BenchTheme.RowBorder, 1.5f);
            img.type = Image.Type.Sliced;
            BenchUI.Label(rt, "lbl", name, 16, BenchTheme.RowHSmall / 2 - 10,
                180, 17, BenchTheme.Text, true);
            var btn = rt.gameObject.AddComponent<BenchButton>();
            btn.Init(img, "", false, 17);
            var n = name;
            Hook(rt, () => SelectInstrument(n));
            instrumentRows[name] = btn;
            y += BenchTheme.RowHSmall + 8;
        }
    }

    void SelectInstrument(string name)
    {
        selectedInstrument = name;
        OnInstrument?.Invoke(name);
    }

    static void Hook(Component c, System.Action a)
    {
        var h = c.gameObject.AddComponent<UiClick>();
        h.on = a;
    }

    public void SetMode(BenchMode mode)
    {
        bool testingView = mode == BenchMode.Testing;
        partsList.gameObject.SetActive(!testingView);
        testingRows.gameObject.SetActive(testingView);
    }

    /// Keep the active tab lit (BenchRoot drives this).
    public void SetActiveTab(string tab)
    {
        boards.SetActive(tab == "Boards");
        components.SetActive(tab == "Components");
        testing.SetActive(tab == "Testing");
    }

    /// Live parts list from the mirror ("LED · Servo · BME280…").
    public void RefreshParts(TwinMirror mirror)
    {
        if (partsList == null) return;
        var names = new List<string>();
        foreach (var p in mirror.Parts.Values) names.Add(p.kind);
        partsList.text = names.Count > 0
            ? string.Join(" · ", names)
            : "no parts placed";
        partsList.text += $"\n\n<alpha=#88>{mirror.SerialLines.Count} serial lines</alpha>";
    }

    public void Show(bool v) => Root.SetActive(v);
}
