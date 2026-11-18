// HomeScreen.cs — the MR Electronics Bench welcome panel (concept
// page 1): title, tagline, Create/Open Project pills and the project
// rows. Faithful mock rows per the approved plan — pinching "Open
// Project" or a row enters the bench; the twin is the live project.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HomeScreen : MonoBehaviour
{
    public event Action OnOpenProject;
    public event Action<string> OnProjectPicked; // row name
    public GameObject Root { get; private set; }

    readonly (string name, string sub, string when, bool sel)[] rows =
    {
        ("Smart Plant Monitor", "Raspberry Pi 4B · 14 parts", "2 min ago", true),
        ("LED Blink Test", "Raspberry Pi 4B · 4 parts", "Yesterday", false),
        ("Motor Driver", "Jetson Nano · 9 parts", "Nov 12", false),
    };

    public void Build(Transform parent)
    {
        var canvas = BenchUI.WorldCanvas(parent, "Home",
            300, 96, 680, 500);
        Root = canvas.gameObject;
        var panel = BenchUI.Panel(canvas.transform, "glass", 0, 0, 680, 500);

        const float pad = 36;
        BenchUI.Label(panel, "title", "MR Electronics Bench",
            pad, pad, 600, BenchTheme.FontTitle, BenchTheme.Text, true);
        BenchUI.Label(panel, "sub",
            "Build, wire and test circuits on your real desk.",
            pad, pad + 42, 600, 17, BenchTheme.TextSubtle);

        var create = BenchUI.Button(panel, "Create Project",
            pad, pad + 82, 190);
        var open = BenchUI.Button(panel, "Open Project",
            pad + 198, pad + 82, 190, active: true);
        Hook(create, () => OnOpenProject?.Invoke());
        Hook(open, () => OnOpenProject?.Invoke());

        float y = pad + 148;
        foreach (var r in rows)
        {
            var row = MakeRow(panel, y, 680 - pad * 2, r);
            Hook(row, () => OnProjectPicked?.Invoke(r.name));
            y += BenchTheme.RowH + 12;
        }
    }

    static void Hook(Component c, Action a)
    {
        var h = c.gameObject.AddComponent<UiClick>();
        h.on = a;
    }

    /// .row from the concept: 78px pill, name + sub left, time right.
    public static RectTransform MakeRow(RectTransform parent, float y,
                                        float w,
                                        (string name, string sub,
                                         string when, bool sel) r)
    {
        var rt = BenchUI.Rect(parent, "row_" + r.name, 36, y, w, BenchTheme.RowH);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Art.Rounded(w, BenchTheme.RowH, BenchTheme.RRow,
            r.sel ? BenchTheme.RowSelectedBg : BenchTheme.RowBg,
            r.sel ? BenchTheme.RowSelectedBorder : BenchTheme.RowBorder, 1.5f);
        img.type = Image.Type.Sliced;

        BenchUI.Label(rt, "name", r.name, 22, BenchTheme.RowH / 2 - 36,
            w - 160, BenchTheme.FontRowName, BenchTheme.Text, true);
        BenchUI.Label(rt, "sub", r.sub, 22, BenchTheme.RowH / 2 - 8,
            w - 160, BenchTheme.FontSmall, BenchTheme.TextSubtle);
        BenchUI.Label(rt, "when", r.when, w - 140, BenchTheme.RowH / 2 - 18,
            100, BenchTheme.FontSmall, BenchTheme.TextSubtle,
            align: TMPro.TextAlignmentOptions.MidlineRight);
        // hover tint on the whole row
        rt.gameObject.AddComponent<RowHover>().Init(img, r.sel);
        return rt;
    }

    public void Show(bool v) => Root.SetActive(v);
}

/// Hover tint for .row pills (BenchButton handles buttons; rows use
/// this small behaviour so their selected tint stays intact).
public class RowHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    Image img;
    Color bg, border;
    bool selected;

    public void Init(Image img, bool selected)
    {
        this.img = img;
        this.selected = selected;
        bg = selected ? BenchTheme.RowSelectedBg : BenchTheme.RowBg;
        border = selected ? BenchTheme.RowSelectedBorder : BenchTheme.RowBorder;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        if (selected) return;
        img.sprite = Art.Rounded(img.rectTransform.rect.width,
            img.rectTransform.rect.height, BenchTheme.RRow,
            BenchTheme.ActiveBg, BenchTheme.ActiveBorder, 1.5f);
        img.type = Image.Type.Sliced;
    }

    public void OnPointerExit(PointerEventData _)
    {
        if (selected) return;
        img.sprite = Art.Rounded(img.rectTransform.rect.width,
            img.rectTransform.rect.height, BenchTheme.RRow,
            bg, border, 1.5f);
        img.type = Image.Type.Sliced;
    }
}
