// PinBadge.cs — world-space pin markers above the 3D board/part pins.
// Faithful to the concept's .lb badges: 74×30 pill, dark glass fill,
// 1.5px coloured border, bold 15px label, shape glyph (▲ power ·
// ● GPIO · ■ GND). Pins also carry a gaze/pinch target collider that
// the wiring flow uses ("Wire: pinch pin A, then pin B").

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Pin categories for the bottom filter pill (concept:
/// All · Power · Ground · GPIO · I2C · Free pins).
public enum PinCategory { All, Power, Ground, Gpio, I2C, Free }

/// A wireable node on the desk: board header pin, part pin or
/// breadboard hole. Node strings mirror the twin's BridgeNode codec.
public class PinTarget : MonoBehaviour
{
    public string node;        // "gpio:18", "power:0", "comp:<uuid>:2"…
    public string label;       // "GPIO18"
    public string subtitle;    // "PWM ch0" / part pin label
    public PinCategory category;
    public bool free = true;   // no wire attached yet

    public bool Matches(PinCategory filter) => filter == PinCategory.All
        || (filter == PinCategory.Free ? free : category == filter);
}

/// The floating badge visual. Fades with gaze distance (the concept
/// shows the nearest badges — all 40 would collide at 2.54 mm pitch).
public class PinBadge : MonoBehaviour
{
    public PinTarget target;

    Image bg, shape, labelIcon, glowImg;
    TextMeshProUGUI label;
    GameObject glow;
    Camera cam;

    /// Ride this transform (pin anchor) instead of staying put —
    /// badges follow their pin when parts are dragged.
    public Transform follow;
    public Vector3 followOffset = Vector3.zero;

    float alpha = 1f;
    internal float targetAlpha = 1f;
    bool dimmed;
    bool glowOn;

    /// design-px scale multiplier (badge world size).
    public float scale = 0.5f;

    public void Init(PinTarget target)
    {
        this.target = target;
        var c = ColorFor(target.category);

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(BenchTheme.BadgeW, BenchTheme.BadgeH);
        rt.localScale = Vector3.one * BenchTheme.Px * scale;

        // pill background
        bg = MakeImage("bg", new Vector2(37, -15), new Vector2(74, 30));
        bg.sprite = Art.Rounded(BenchTheme.BadgeW, BenchTheme.BadgeH,
            BenchTheme.RPinBadge, BenchTheme.BadgeBg, c, BenchTheme.BadgeBorder);
        bg.type = Image.Type.Sliced;

        // shape glyph (▲ power · ● gpio · ■ gnd)
        shape = MakeImage("shape", new Vector2(13, -15), new Vector2(11, 11));
        shape.sprite = ShapeSprite(c, target.category);

        // label
        var labelRt = new GameObject("label", typeof(RectTransform))
            .transform as RectTransform;
        labelRt.SetParent(transform, false);
        labelRt.anchorMin = labelRt.anchorMax = new Vector2(0, 0.5f);
        labelRt.pivot = new Vector2(0, 0.5f);
        labelRt.anchoredPosition = new Vector2(26, 0);
        labelRt.sizeDelta = new Vector2(BenchTheme.BadgeW - 30, BenchTheme.BadgeH);
        label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = target.label;
        label.fontSize = BenchTheme.FontPinBadge;
        label.font = BenchUI.FontBold ?? BenchUI.Font;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = BenchTheme.Text;
        label.raycastTarget = false;
        label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        // valid-target glow (green dash ring, hidden until wiring)
        glowImg = MakeImage("glow", new Vector2(37, -15),
            new Vector2(BenchTheme.BadgeW + 16, BenchTheme.BadgeH + 16));
        glowImg.sprite = Art.DashRing(BenchTheme.Green);
        glow = glowImg.gameObject;
        glow.SetActive(false);
    }

    Canvas canvas;

    Image MakeImage(string name, Vector2 center, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform))
            .transform as RectTransform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    public static Color ColorFor(PinCategory c) => c switch
    {
        PinCategory.Power => BenchTheme.Red,
        PinCategory.Ground => BenchTheme.GndWhite,
        _ => BenchTheme.Cyan, // GPIO, I2C
    };

    public static Sprite ShapeSprite(Color c, PinCategory cat) => cat switch
    {
        PinCategory.Power => Art.Triangle(c),
        PinCategory.Ground => Art.Square(c),
        _ => Art.Circle(c),
    };

    void LateUpdate()
    {
        if (follow != null)
            transform.position = follow.position + followOffset;
        if (cam == null) cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(
                transform.position - cam.transform.position, Vector3.up);

        float goal = targetAlpha * (dimmed ? 0.25f : 1f);
        alpha = Mathf.Lerp(alpha, goal, Time.deltaTime * 10f);
        bg.color = new Color(1, 1, 1, alpha);
        shape.color = new Color(1, 1, 1, alpha);
        label.color = new Color(1, 1, 1, alpha);

        if (glowOn && glow.activeSelf)
            glowImg.color = new Color(1, 1, 1,
                0.35f + 0.65f * (0.5f + 0.5f *
                    Mathf.Sin(Time.time * Mathf.PI / 0.9f)));
    }

    /// Wiring mode: pulse the green dash ring on valid targets.
    public void SetGlow(bool on)
    {
        glowOn = on;
        glow.SetActive(on && target.free);
    }

    /// Filter pill: fade badges outside the category.
    public void SetDim(bool d) => dimmed = d;
}
